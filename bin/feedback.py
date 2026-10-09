#!/usr/bin/env python3
"""Run one bounded Unity feedback step and preserve machine-readable evidence."""
import argparse
import hashlib
import json
from pathlib import Path
import subprocess
import sys
import time
from datetime import datetime, timezone

PROJECT = Path(__file__).resolve().parents[1]


def fingerprint():
    digest = hashlib.sha256()
    paths = []
    for folder in ("Assets/BlueCafe/Editor", "Assets/BlueCafe/Shaders", "Assets/BlueCafe/Tests", "Packages", "ProjectSettings", "bin", "tools", "evidence/references/layouts"):
        root = PROJECT / folder
        if root.exists():
            paths.extend(p for p in root.rglob("*") if p.is_file() and p.suffix != ".pyc")
    catalog = PROJECT / "Assets/BlueCafe/Content/catalog.json"
    if catalog.exists():
        paths.append(catalog)
    for path in sorted(paths):
        digest.update(str(path.relative_to(PROJECT)).encode())
        digest.update(path.read_bytes())
    return digest.hexdigest()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("action", choices=("status", "eval", "console", "tests", "test-status", "recompile"))
    parser.add_argument("--code", help="C# evaluated in the current Editor; use the shared BlueCafe.CafeApi.")
    parser.add_argument("--file", type=Path, help="Read C# code from a file instead of shell interpolation.")
    args = parser.parse_args()
    if args.action == "eval" and bool(args.code) == bool(args.file):
        parser.error("eval requires exactly one of --code or --file")
    command = ["unity", "command"]
    name = {"status": "editor_status", "eval": "eval", "console": "console", "tests": "run_tests", "test-status": "test_status", "recompile": "recompile"}[args.action]
    command += [name, "--project-path", str(PROJECT), "--caller", "plugin", "--skill", "unity-cli", "--format", "json", "--timeout", "45"]
    if args.action == "eval":
        code = args.file.read_text() if args.file else args.code
        command += ["--code", code]
    if args.action == "console":
        command += ["--tail", "20", "--level", "error"]
    if args.action == "tests":
        command += ["--mode", "editor", "--filter", "BlueCafe", "--async_tests", "true"]
    if args.action == "recompile":
        command += ["--focus", "true"]
    stamp = datetime.now(timezone.utc).strftime("%Y%m%dT%H%M%S.%fZ")
    evidence = PROJECT / "evidence/feedback" / (stamp + "-" + args.action)
    evidence.mkdir(parents=True)
    before = fingerprint()
    start = time.monotonic()
    try:
        process = subprocess.run(command, cwd=PROJECT, text=True, capture_output=True, timeout=55)
        stdout, stderr, returncode = process.stdout, process.stderr, process.returncode
    except subprocess.TimeoutExpired as error:
        stdout = error.stdout or ""
        stderr = error.stderr or ""
        if isinstance(stdout, bytes): stdout = stdout.decode(errors="replace")
        if isinstance(stderr, bytes): stderr = stderr.decode(errors="replace")
        returncode = 124
    (evidence / "stdout.json").write_text(stdout)
    (evidence / "stderr.log").write_text(stderr)
    try:
        result = json.loads(stdout)
    except ValueError:
        result = {"success": False, "error": "No JSON response"}
    nested = result.get("data", {}) or {}
    evaluated = nested.get("result", {})
    ok = returncode == 0 and result.get("success", False)
    if isinstance(evaluated, dict) and evaluated.get("success") is False:
        ok = False
    after = fingerprint()
    report = {"action": args.action, "success": ok, "exitCode": returncode,
              "elapsedSeconds": round(time.monotonic() - start, 3),
              "sourceFingerprint": before, "sourceUnchanged": before == after,
              "evidence": str(evidence)}
    (evidence / "report.json").write_text(json.dumps(report, indent=2) + "\n")
    print(json.dumps({**report, "response": result}, indent=2))
    return 0 if ok else 1


if __name__ == "__main__":
    sys.exit(main())

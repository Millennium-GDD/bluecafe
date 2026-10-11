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


def run_command(command):
    try:
        process = subprocess.run(command, cwd=PROJECT, text=True, capture_output=True, timeout=55)
        return process.stdout, process.stderr, process.returncode
    except FileNotFoundError:
        return json.dumps({"success": False, "error": "Unity CLI is required on PATH; see docs/DEVELOPMENT.md."}), "", 127
    except subprocess.TimeoutExpired as error:
        stdout, stderr = error.stdout or "", error.stderr or ""
        if isinstance(stdout, bytes): stdout = stdout.decode(errors="replace")
        if isinstance(stderr, bytes): stderr = stderr.decode(errors="replace")
        return stdout, stderr, 124


def parse_response(stdout):
    try:
        result = json.loads(stdout)
        return result if isinstance(result, dict) else {"success": False, "error": "Invalid JSON response"}
    except ValueError:
        return {"success": False, "error": "No JSON response"}


def compilation_ready(result, returncode):
    """Fail closed unless the explicit project's compilation completed cleanly."""
    nested = result.get("data")
    state = nested.get("result") if isinstance(nested, dict) else None
    if returncode != 0 or result.get("success") is not True or not isinstance(state, dict):
        return False
    if state.get("status") not in ("completed", "up_to_date") or state.get("compilationFailed") is not False:
        return False
    def unhealthy(value):
        if isinstance(value, dict):
            return any((key in ("failed", "compilationFailed", "isCompiling", "compiling") and bool(item)) or
                       (key == "success" and item is False) or
                       (key == "errors" and bool(item)) or unhealthy(item)
                       for key, item in value.items())
        return isinstance(value, list) and any(unhealthy(item) for item in value)
    return not unhealthy(result)


def fingerprint():
    digest = hashlib.sha256()
    paths = []
    for folder in ("Assets/BlueCafe/Core", "Assets/BlueCafe/Infrastructure", "Assets/BlueCafe/Unity", "Assets/BlueCafe/App", "Assets/BlueCafe/Agent", "Assets/BlueCafe/Editor", "Assets/BlueCafe/Shaders", "Assets/BlueCafe/Tests", "Packages", "ProjectSettings", "bin", "tools", "evidence/references/layouts", "docs/reconstruction"):
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
    preflight = None
    executed = True
    if args.action in ("eval", "tests"):
        preflight_command = ["unity", "command", "recompile_status", "--project-path", str(PROJECT),
                             "--caller", "plugin", "--skill", "unity-cli", "--format", "json", "--timeout", "45"]
        raw, error, code = run_command(preflight_command)
        (evidence / "preflight-stdout.json").write_text(raw)
        (evidence / "preflight-stderr.log").write_text(error)
        response = parse_response(raw)
        ready = compilation_ready(response, code)
        preflight = {"command": preflight_command, "exitCode": code, "success": ready, "response": response}
        if not ready:
            executed = False
            stdout = json.dumps({"success": False, "error": "Compilation preflight rejected action; recompile and wait for clean completion.",
                                 "code": "COMPILATION_NOT_READY"})
            stderr, returncode = "", 1
    if executed:
        stdout, stderr, returncode = run_command(command)
    (evidence / "stdout.json").write_text(stdout)
    (evidence / "stderr.log").write_text(stderr)
    result = parse_response(stdout)
    nested = result.get("data", {}) or {}
    evaluated = nested.get("result", {})
    ok = returncode == 0 and result.get("success", False)
    if isinstance(evaluated, dict) and evaluated.get("success") is False:
        ok = False
    if args.action == "test-status" and isinstance(evaluated, dict):
        summary = evaluated.get("summary", evaluated.get("Summary", {}))
        if summary.get("failed", summary.get("Failed", 0)) > 0:
            ok = False
    after = fingerprint()
    report = {"action": args.action, "success": ok, "exitCode": returncode,
              "elapsedSeconds": round(time.monotonic() - start, 3),
              "sourceFingerprint": before, "sourceUnchanged": before == after,
              "actionExecuted": executed, "preflight": preflight,
              "evidence": str(evidence)}
    (evidence / "report.json").write_text(json.dumps(report, indent=2) + "\n")
    print(json.dumps({**report, "response": result}, indent=2))
    return 0 if ok else 1


if __name__ == "__main__":
    sys.exit(main())

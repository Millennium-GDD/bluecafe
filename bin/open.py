#!/usr/bin/env python3
"""Open the Designer with the project Editor, reusing its live session."""
import argparse
import json
import os
from pathlib import Path
import subprocess
import sys


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--editor-path", type=Path, help="Unity executable; also settable with UNITY_EDITOR_PATH")
    args = parser.parse_args()
    project = Path(__file__).resolve().parents[1]
    try:
        status = subprocess.run(["unity", "status", "--format", "json"], cwd=project,
                                text=True, capture_output=True, timeout=55)
    except FileNotFoundError:
        parser.exit(1, "Unity CLI is required on PATH. See docs/DEVELOPMENT.md.\n")
    except subprocess.TimeoutExpired:
        parser.exit(1, "Unity CLI status timed out; inspect the running Editor before retrying.\n")
    try:
        instances = json.loads(status.stdout).get("data", {}).get("instances", [])
    except (ValueError, AttributeError):
        instances = []
    if any(Path(item.get("project", "")).resolve() == project for item in instances):
        return subprocess.call(["unity", "command", "menu", "--project-path", str(project),
                                "--caller", "plugin", "--skill", "unity-cli", "--path", "Window/BlueCafe/Designer"], cwd=project)
    if status.returncode != 0:
        parser.exit(1, "Unity CLI could not inspect live Editors; refusing to start a duplicate.\n")
    version_text = (project / "ProjectSettings/ProjectVersion.txt").read_text()
    version = next(line.split(":", 1)[1].strip() for line in version_text.splitlines() if line.startswith("m_EditorVersion:"))
    editor_path = args.editor_path or os.environ.get("UNITY_EDITOR_PATH")
    if editor_path:
        editor_path = Path(editor_path).expanduser().resolve()
        if not editor_path.is_file():
            parser.error("Unity executable does not exist: " + str(editor_path))
    else:
        editors = subprocess.run(["unity", "editors", "--installed", "--format", "json"], cwd=project,
                                 text=True, capture_output=True, check=True, timeout=55)
        editor = next((row for row in json.loads(editors.stdout)["data"] if row["version"] == version), None)
        if not editor:
            parser.exit(1, "Required Unity Editor is not installed: " + version + "\n")
        editor_path = editor["location"]
    log = project / "Logs/editor-automated.log"
    log.parent.mkdir(exist_ok=True)
    subprocess.Popen([str(editor_path), "-automated", "-projectPath", str(project), "-logFile", str(log),
                      "-executeMethod", "BlueCafe.CafeDesignerWindow.Open"],
                     cwd=project, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL, start_new_session=True)
    print("Starting BlueCafe in Unity " + version + ". Log: " + str(log))
    return 0


if __name__ == "__main__":
    sys.exit(main())

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
        payload = json.loads(status.stdout)
        data = payload.get("data") if isinstance(payload, dict) else None
        instances = data.get("instances") if isinstance(data, dict) else None
        if not isinstance(instances, list) or any(not isinstance(item, dict) for item in instances):
            raise ValueError("Invalid Editor inventory")
        errors = payload.get("errors")
        no_instances = (status.returncode == 1 and payload.get("success") is False
                        and data.get("count") == 0 and instances == []
                        and isinstance(errors, list) and len(errors) == 1
                        and isinstance(errors[0], dict) and errors[0].get("code") == "STATUS_NO_INSTANCES")
        ready_inventory = status.returncode == 0 and payload.get("success") is True
        if not (ready_inventory or no_instances):
            raise ValueError("Unity CLI status failed")
    except (ValueError, AttributeError):
        parser.exit(1, "Unity CLI could not inspect live Editors; refusing to start a duplicate.\n")
    if any(Path(item.get("project", "")).resolve() == project for item in instances):
        return subprocess.call(["unity", "command", "menu", "--project-path", str(project),
                                "--caller", "plugin", "--skill", "unity-cli", "--path", "Window/BlueCafe/Designer"], cwd=project)
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

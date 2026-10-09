#!/usr/bin/env python3
"""Open the Designer using the project's installed Editor, reusing a live session."""
import json
from pathlib import Path
import subprocess
import sys

project = Path(__file__).resolve().parents[1]
status = subprocess.run(["unity", "status", "--format", "json"], text=True, capture_output=True)
try:
    instances = json.loads(status.stdout).get("data", {}).get("instances", [])
except (ValueError, AttributeError):
    instances = []
if any(Path(item.get("project", "")) == project for item in instances):
    sys.exit(subprocess.call(["unity", "command", "menu", "--project-path", str(project),
                             "--caller", "plugin", "--skill", "unity-cli", "--path", "Window/BlueCafe/Designer"]))
version_text = (project / "ProjectSettings/ProjectVersion.txt").read_text()
version = next(line.split(":", 1)[1].strip() for line in version_text.splitlines() if line.startswith("m_EditorVersion:"))
editors = subprocess.run(["unity", "editors", "--installed", "--format", "json"], text=True, capture_output=True, check=True)
editor = next((row for row in json.loads(editors.stdout)["data"] if row["version"] == version), None)
if not editor:
    sys.exit("Required Unity Editor is not installed: " + version)
log = project / "Logs/editor-automated.log"
log.parent.mkdir(exist_ok=True)
subprocess.Popen([editor["location"], "-automated", "-projectPath", str(project), "-logFile", str(log),
                  "-executeMethod", "BlueCafe.CafeDesignerWindow.Open"],
                 cwd=project, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL, start_new_session=True)
print("Starting BlueCafe in Unity " + version + ". Log: " + str(log))

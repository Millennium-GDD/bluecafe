# Development

Use Unity 6000.2.6f2 and the platform build-support modules for the desired target. Supply compatible prepared content separately under the permissions applicable to it; see [PREPARED-PACK.md](PREPARED-PACK.md). No content acquisition/conversion tooling or game assets are included.

Open this project in the matching Unity Editor. Only one orchestrator should own its live Editor, package installation and scene integration. Use the installed Unity CLI for guarded feedback against this explicit project:

```sh
python3 bin/feedback.py eval --code 'return UnityEngine.Application.unityVersion;'
python3 bin/feedback.py tests
```

The helper captures compilation state first and rejects incomplete/failed compilation. It preserves generated logs/evidence locally. Use `python3 bin/feedback.py --help` for current command arguments. Unity CLI commands used directly must include explicit `--project-path "$PWD"`; plugin calls also include `--caller plugin --skill unity-cli`. Do not launch a second Editor for the same checkout.

Core and Infrastructure tests use synthetic catalog/mesh fixtures. Integration and rendering tests requiring original assets need an externally supplied compatible pack. Passing synthetic tests or compilation does not establish physical input, visual acceptance or target-machine behavior; see [VERIFICATION.md](VERIFICATION.md).

For Linux, schedule a release build through the resident Editor:

```sh
python3 bin/feedback.py eval --code 'return BlueCafe.App.Editor.StandaloneBuild.ScheduleTo("Builds/Linux-0.1.1/BlueCafe.x86_64", "evidence/linux-0.1.1", "0.1.1", false);'
```

Wait for the local `build-result.json` to report success with no errors. Scheduling alone is not build success. Builds prepare the Bootstrap scene/UI panel through Editor APIs; keep the Player executable, adjacent data and Unity runtime libraries together. Downloaded content stays external. For Windows Mono builds and strict ZIP packaging, see [WINDOWS-BUILD.md](WINDOWS-BUILD.md).

The UI uses the existing Noto Sans Regular font under the included OFL 1.1 license. Do not substitute game artwork or redistribute supplied pack content. Preserve notices for Unity/runtime libraries when preparing a release artifact.

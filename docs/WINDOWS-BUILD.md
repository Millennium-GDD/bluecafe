# Windows standalone build

Use Unity **6000.2.6f2**, a Unity license, and **Windows Build Support (Mono)**.
The script builds a Windows x64 Mono Player with Unity's .NET Framework 4.8 API
profile through the same Bootstrap scene, shader and UI preparation as Linux.
The full API profile provides the Windows Registry API used by System appearance.
Windows IL2CPP is outside this build path.
Install the matching module through Unity Hub before building; missing support
fails before scene preparation. Restart the Editor after installing a module so
its build postprocessor is loaded. The script does not install modules.

## Build in the open Editor

Exit Play mode and let compilation finish. Select **BlueCafe → Build Windows
x64 release**, or use the guarded helper from the project root:

```sh
python3 bin/feedback.py eval --code 'return BlueCafe.App.Editor.WindowsStandaloneBuild.ScheduleTo("Builds/Windows-0.1.1/BlueCafe.exe", "evidence/windows-0.1.1/release", "0.1.1", false);'
```

Scheduling writes `result: "Queued"` immediately and returns. Wait for `evidence/windows-0.1.1/release/build-result.json`
to contain `result: "Succeeded"` before packaging. A failed build records the
exception in that report and `build-error.txt`. A CLI scheduling response is not
build success.

The legacy no-argument builder defaults to `Builds/Windows/BlueCafe.exe` and
`0.1.0-beta.5`, with development mode off. Use explicit paths and version for a
0.1.1 release. Preserve earlier outputs and choose an empty folder for
each new build. A custom build can be scheduled with:

```csharp
BlueCafe.App.Editor.WindowsStandaloneBuild.ScheduleTo(
    "Builds/Windows-0.1.1/BlueCafe.exe",
    "evidence/windows-0.1.1/release",
    "0.1.1",
    false);
```

Outputs must be named `BlueCafe.exe` inside this project's `Builds/`; evidence
must be inside this project's `evidence/`. Versions must start with a digit and
use only letters, digits, dots, hyphens or underscores (at most 80 characters).
The script temporarily selects Mono and .NET Framework 4.8, then restores the
previous active target, backend, API compatibility and window/product settings
afterwards. The report stays `Restoring` while settings cleanup is in progress;
cleanup resumes across a domain reload.
It prepares and saves the shared Bootstrap scene and UI panel like the Linux
builder, so those assets may change during an actual build.

## Build from a terminal / CI

Close this project's Editor first: `unity build` launches a batch Editor. Run
from the project root, using its absolute path for explicit targeting:

```sh
unity build "$PWD" \
  --editor-version 6000.2.6f2 \
  --target StandaloneWindows64 \
  --execute-method BlueCafe.App.Editor.WindowsStandaloneBuild.BuildFromCommandLine \
  --output-path Builds/Windows-0.1.1/BlueCafe.exe
```

PowerShell equivalent:

```powershell
unity build "$($PWD.Path)" `
  --editor-version 6000.2.6f2 `
  --target StandaloneWindows64 `
  --execute-method BlueCafe.App.Editor.WindowsStandaloneBuild.BuildFromCommandLine `
  --output-path Builds/Windows-0.1.1/BlueCafe.exe
```

Custom version/evidence and development mode can be supplied via Unity CLI's
`--args`, for example:

```sh
--args '-blueCafeVersion 0.1.1 -blueCafeEvidence evidence/windows-0.1.1/release'
```

Add `-blueCafeDevelopment` inside that string for a development Player. Use
`--allow-dirty-build` only when intentionally building uncommitted source.

## Package and run

After a successful release build:

```sh
python3 tools/package_windows.py \
  --source Builds/Windows-0.1.1 \
  --evidence evidence/windows-0.1.1/release
```

The package script validates the recorded target, backend, version, output path
and every Player file hash before creating
`Builds/BlueCafe-0.1.1-windows-x86_64.zip`. It includes notices, the font
license, runtime libraries and a manifest. It refuses existing destinations.
Preserve prior packaging records and use fresh evidence for each release: the
packager also refuses existing `manifest.json` and `package.json` records.
Use the matching source and evidence arguments when choosing other build paths.

Extract the entire ZIP and run `BlueCafe.exe`. Unity Editor and Python are not
required at runtime. Supply a separate compatible prepared asset pack through
Settings or PowerShell:

```powershell
.\BlueCafe.exe --asset-pack "C:\BlueCafeAssets"
```

Downloaded game assets are excluded. Native file/folder dialogs currently have
only a Linux adapter; on Windows enter paths in the existing editable fields.
Saved cafes/preferences use Unity's Windows application data directory for
`BlueCafe/BlueCafe`. `--data-path "C:\BlueCafeTestData"` selects an independent
profile; close the previous Player before sharing its data profile.

Compilation, package checks and a successful cross-build do not establish
Windows runtime acceptance. Rendering, physical input, save/reopen, recovery,
clean-machine execution and upgrade preservation still need Windows evidence
under the gates in [VERIFICATION.md](VERIFICATION.md).

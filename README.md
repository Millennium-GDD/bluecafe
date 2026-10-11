# BlueCafe

An independent Unity cafe designer with standalone Linux and Windows apps and retained Editor tools.

The beta adds a Rooms workspace, named floor areas, wall decoration height and movement, complete asset browsing, editable presets and a saved startup cafe. Floors at 40×40 and larger retain their original appearance. The public packages contain the app and required runtime libraries; supply a separate compatible prepared asset pack.

## Run the beta

Download the package for your platform from [Releases](https://github.com/Millennium-GDD/bluecafe/releases), and extract the entire archive.

- **Linux x86_64:** run `launch.sh`. Optional native file dialogs use `zenity`; typed paths also work.
- **Windows x86_64:** run `BlueCafe.exe`. Enter folder/file paths in the editable fields; native Windows file dialogs are not implemented yet.

Unity Editor and Python are not required to run either app. Select your prepared asset folder in Settings, or pass it when launching:

```sh
./launch.sh --asset-pack /path/to/prepared-pack
```

```powershell
.\BlueCafe.exe --asset-pack "C:\BlueCafeAssets"
```

Game models, textures, thumbnails and catalog data are **not included** in this repository or release. BlueCafe cannot download or extract game content. The [prepared pack contract](docs/PREPARED-PACK.md) describes compatible user-supplied inputs.

Save and close an older BlueCafe instance before opening another against the same data folder. Keep a backup when trying a beta. On Linux, cafes/settings/recovery use `~/.config/unity3d/BlueCafe/BlueCafe`; Windows uses Unity's application data directory for `BlueCafe/BlueCafe`. `--data-path` selects an independent profile. Existing v1 layouts remain readable; geometry edits migrate to v2, which the old alpha cannot read.

First startup saves an **Empty room** in My cafes and opens it clean. Select a saved cafe and choose **Open at startup** to change the default.

## Controls

Drag a catalog item into the room, or click it and choose a placement. Drag selected furniture to move; **R** rotates, **Ctrl+D** duplicates, **Delete** removes, **Ctrl+Z** undoes and **Ctrl+Shift+Z** redoes. Shift adds to selection. Right-drag pans, scroll zooms, and **Fit room** restores framing. Escape closes a popup or cancels the current gesture.

Select a wall decoration to change its mounting-center **Height (m)**, raise/lower it by one grid step, or drag it along/up the wall and onto another visible wall. Automatic floor/wall grids appear during movement; the manual Grid control remains available.

Rooms edits the cafe enclosure and named polygon areas. Named areas classify flooring and wallpaper; they do not create internal physical partitions. Presets open on double-click and can be renamed. Build also supports cafe naming.

## Develop

Use Unity **6000.2.6f2**, your own Unity license and the platform's build support module. Open this repository through Unity Hub; Unity resolves `Packages/manifest.json`. Windows builds use Windows Build Support (Mono).

Optional Python helpers use the standard library; Editor helpers require Unity CLI on PATH:

```sh
python3 bin/open.py
python3 bin/feedback.py recompile
python3 bin/feedback.py tests
./launch-latest.sh
```

`launch-latest.sh` starts the newest complete Linux build in the checkout. The Editor designer is at **Window → BlueCafe → Designer**. A release build can be scheduled in the live Editor:

```csharp
BlueCafe.App.Editor.StandaloneBuild.ScheduleTo(
    "Builds/Linux-Beta5/BlueCafe.x86_64", "evidence/linux-beta5",
    "0.1.0-beta.5", false);
```

See [Windows build instructions](docs/WINDOWS-BUILD.md) for Windows builds and packaging. Build outputs and local data are ignored by Git.

## Verification and limitations

Beta.5 passed 196 EditMode tests. Linux executable checks cover original assets, save/reopen, Rooms resizing with undo/redo, styled/offset rooms and default floors through 1,000×1,000. Windows cross-build and package integrity checks passed; Windows execution, rendering, input, recovery and upgrade behavior remain to be tested on Windows. Clean-machine, human timing/visual approval, accessibility and full performance budgets remain open. No macOS package is provided.

Read [verification requirements](docs/VERIFICATION.md) and [integration contracts](docs/CONTRACTS.md) before changing implementation boundaries. Local acquisition evidence and reference captures are excluded. Editor reconstruction diagnostics that need those local files report unavailable when they are absent.

## Notices

BlueCafe is unofficial and is not endorsed by Blue Archive rights holders. Publication does not grant rights to separately supplied content. See [NOTICE.md](NOTICE.md). No project-wide open-source license has been selected. Noto Sans, embedded fonts, Unity runtime and package components retain their respective licenses and notices.

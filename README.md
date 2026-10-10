# BlueCafe

An independent Unity cafe designer with a standalone desktop app and retained Editor tools.

The personal Linux alpha supports furnishing, move/rotate/copy/delete, undo/redo, catalog filters, interior selection, saved cafes, portable layouts, recovery and a local agent bridge. **Rooms remains experimental.** Full standalone and cross-platform acceptance are still open.

## Run the alpha

Download the Linux x86_64 archive from [Releases](https://github.com/Millennium-GDD/bluecafe/releases), extract it and run `launch.sh`. Keep the extracted folder together. Unity Editor and Python are not required to run the app. Optional native file dialogs use `zenity`; typed paths are available.

Select your existing prepared asset folder in Settings, or launch with:

```sh
BLUECAFE_ASSET_PACK=/path/to/prepared-pack ./launch.sh
```

Game models, textures, thumbnails and catalog data are **not included** in the repository or release. The app requires a separately supplied compatible pack; it cannot download or extract game content. See [prepared pack contract](docs/PREPARED-PACK.md).

Save and close an older BlueCafe instance before opening another against the same data folder. On Linux, saved cafes, settings and recovery use `~/.config/unity3d/BlueCafe/BlueCafe`. Use `--data-path /path/to/separate-data` for an independent instance.

## Develop

Install Unity **6000.2.6f2** with Linux Build Support and your own Unity license. Clone this repository, then open its root through Unity Hub. Unity resolves the packages declared in `Packages/manifest.json`.

The optional Python helpers use only the standard library. Editor helpers also require Unity CLI on PATH:

```sh
python3 bin/open.py
python3 bin/feedback.py recompile
python3 bin/feedback.py tests
```

The Editor designer is at **Window → BlueCafe → Designer**. The Linux development build menu is **BlueCafe → Build Linux runtime spine**. The release builder is `BlueCafe.App.Editor.StandaloneBuild.BuildAlphaTo(output, evidence)`, invoked from the Editor or through a scheduled Unity CLI evaluation. Build outputs are ignored by Git.

## Project structure

| Directory | Responsibility |
| --- | --- |
| `Assets/BlueCafe/Core` | Engine-independent layout, placement, session and undo rules |
| `Assets/BlueCafe/Infrastructure` | Read-only pack validation and atomic document storage |
| `Assets/BlueCafe/Unity` | Original-asset runtime rendering and projection |
| `Assets/BlueCafe/App` | Desktop UI and standalone build entry points |
| `Assets/BlueCafe/Agent` | Authenticated loopback command bridge |
| `Assets/BlueCafe/Editor` | Editor designer and reconstruction diagnostics |
| `Assets/BlueCafe/Tests` | EditMode tests with synthetic fixtures |
| `bin` | Optional Editor and standalone agent helpers |

Read [verification requirements](docs/VERIFICATION.md) and [integration contracts](docs/CONTRACTS.md) before changing implementation boundaries. Local acquisition evidence and reference captures are deliberately excluded. Some Editor example/reconstruction diagnostics require those local files and report unavailable when they are absent.

## Controls

Drag a catalog item into the room, or click it and choose a placement. Drag selected furniture to move; **R** rotates, **Ctrl+D** duplicates, **Delete** removes, **Ctrl+Z** undoes and **Ctrl+Shift+Z** redoes. Shift adds to selection. Right-drag pans, scroll zooms, and Frame room restores framing. Escape closes the topmost popup or cancels the current gesture.

## Notices

BlueCafe is unofficial and is not endorsed by Blue Archive rights holders. Source publication does not grant rights to separately supplied game content. See [NOTICE.md](NOTICE.md). No project-wide open-source license has been selected; the bundled Noto Sans font retains its SIL Open Font License 1.1 license.

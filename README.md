# BlueCafe

A desktop cafe layout editor built with Unity.

<img width="3199" height="1917" alt="Screenshot_20261011_153549" src="https://github.com/user-attachments/assets/837ab0da-ba21-43cf-97f2-1630d80c817d" />

## Run

Download a build from [Releases](https://github.com/Millennium-GDD/bluecafe/releases) and extract the archive.

- Linux: run `launch.sh`.
- Windows: run `BlueCafe.exe`.

Select a compatible prepared asset folder in Settings. Game assets are supplied separately; see [asset pack requirements](docs/PREPARED-PACK.md). Unity Editor is not needed to run the app.

## Get and prepare assets

[Blue Archive Asset Downloader](https://github.com/ZM-Kimu/Blue-Archive-Asset-Downloader) can download original game bundles. [Schale Archive's extraction guide](https://github.com/kiraio-moe/Schale-Archive#how-to-get-the-game-assets-by-yourself) describes another acquisition workflow; its Spine files are not a BlueCafe furniture pack.

Keep the original `.bundle` files. Use the included [converter and setup guide](docs/ASSET-CONVERSION.md) to prepare a folder containing `catalog.json`, meshes and textures, then select that folder in BlueCafe's Settings. The converter reads local files; it does not download game content.

These are third-party resources, not official BlueCafe asset packs. Links and conversion code do not grant permission to acquire, use or redistribute game content. Use content only under the permissions applicable to it.

## Development

Open the project with Unity **6000.2.6f2** and the build support module for your platform.

See [development instructions](docs/DEVELOPMENT.md) and [Windows builds](docs/WINDOWS-BUILD.md). Before changing the implementation, read the [integration contracts](docs/CONTRACTS.md) and [verification requirements](docs/VERIFICATION.md).

## Notices

BlueCafe is unofficial. Game content is excluded from the repository and downloads. See [NOTICE.md](NOTICE.md) for content rights and third-party licenses. No project-wide source license has been selected.

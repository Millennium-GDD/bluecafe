# Prepare a BlueCafe asset folder

BlueCafe loads a prepared folder containing `catalog.json`, mesh JSON and PNG textures. `tools/convert_assets.py` converts original local Unity `.bundle` files into that format. It does not download game content, decrypt clients, or import FBX/OBJ exports or Spine files.

## 1. Obtain the original bundles

Use [Blue Archive Asset Downloader](https://github.com/ZM-Kimu/Blue-Archive-Asset-Downloader) and follow its current installation instructions. Its `assets download` command retains the original files. Choose a region and download the Bundle resources. For example, its documented CLI supports:

```sh
ba-downloader assets download --region gl --resources bundle
```

For the current downloader, bundles are under `<workspace>/gl/android/raw/bundles`. JP/CN use their respective region directory. Retain the downloader's release manifests and logs with your original files to preserve acquisition URLs and version evidence. The upstream README recommends reserving 50 GB for a full download/extraction workflow. Filtering can omit shared dependencies, so a café-only selection may produce unavailable items until its dependencies are supplied.

[Schale Archive's guide](https://github.com/kiraio-moe/Schale-Archive#how-to-get-the-game-assets-by-yourself) also describes obtaining game data and extracting bundles. Keep the original `.bundle` files for this converter; the repository's character/lobby Spine files are not furniture bundles. You do not need to export bundles through AssetStudio first.

These third-party links are informational. Publisher permission for this workflow has not been verified. Tool licenses and public download access do not license game content; obtain and use files only under applicable permissions. Do not commit or redistribute downloaded files or converted packs.

## 2. Download furniture metadata

Save these JSON files locally, outside the repository. They map game furniture IDs to names, dimensions, prefab names and sets:

- [SchaleDB furniture metadata](https://schaledb.com/data/en/furniture.json) — required; save as `furniture.json`.
- [SchaleDB localization metadata](https://schaledb.com/data/en/localization.json) — optional; save as `localization.json` for theme names.

Use your browser's Save function. Check that the saved file contains JSON rather than an HTML page, and keep metadata from the same acquisition period as the bundles. Metadata spans multiple regions; a record in it does not establish that your downloaded region has the model.

## 3. Install the converter dependencies

Download or clone this repository's source and open a terminal in its root folder. Use Python 3.11 or later. A virtual environment keeps the converter's packages separate.

Windows PowerShell:

```powershell
py -3 -m venv .cache/converter-venv
.cache/converter-venv/Scripts/python.exe -m pip install -r tools/requirements-convert.txt
```

Linux/macOS:

```sh
python3 -m venv .cache/converter-venv
.cache/converter-venv/bin/python -m pip install -r tools/requirements-convert.txt
```

The converter can run without Unity. Some background prefabs reference Unity's original built-in Quad, so complete background conversion additionally needs an installed Unity Editor's `Editor/Data` directory. Supply it with `--unity-data`; missing resources leave affected entries unavailable. Do not launch Unity for conversion. On macOS the equivalent data directory is normally the Editor app's `Contents` directory; use the directory containing `Resources/unity default resources`.

## 4. Convert the files

Replace the example paths with your actual locations. The output folder must not already exist and must be outside your input folders. Store it outside the source repository.

Windows PowerShell:

```powershell
.cache/converter-venv/Scripts/python.exe tools/convert_assets.py --bundles "C:/BAAD/gl/android/raw/bundles" --metadata "C:/BA-data/furniture.json" --localization "C:/BA-data/localization.json" --output "C:/BlueCafe-packs/global"
```

Linux/macOS:

```sh
.cache/converter-venv/bin/python tools/convert_assets.py --bundles "$HOME/BAAD/gl/android/raw/bundles" --metadata "$HOME/BA-data/furniture.json" --localization "$HOME/BA-data/localization.json" --output "$HOME/BlueCafe-packs/global"
```

Optional arguments:

- `--unity-data "/path/to/Editor/Data"`: supplies original Unity built-in resources for backgrounds.
- `--thumbnails "/path/to/icons"`: copies existing PNG thumbnails named by furniture ID, such as `1200.png`. Without this, thumbnail previews are unavailable independently of model availability.
- `--region global --game-version VERSION`: records your known source labels. These are user declarations, not independently verified source information; omit them if unknown.

The converter scans `.bundle` files recursively, preserves original model/material transforms, and writes pack-relative resource paths. It retains unavailable furniture with explicit reasons and writes source byte counts and SHA-256 hashes under `ConversionReport/`. These local hashes identify the supplied files; they do not verify their publisher origin or acquisition URLs. It reads original bundles without modifying or copying them into the pack.

## 5. Load the pack

Open BlueCafe, go to **Settings**, and select the new output folder containing `catalog.json`. BlueCafe validates the pack when loading it. The app needs neither Python nor Unity after preparation.

If furniture is unavailable, inspect `ConversionReport/extraction-local.json`. Check that you supplied the original bundles and all shared dependencies, and that the metadata matches their release. Regional furniture absent from your source remains unavailable. Preserve the original downloads and create a new output folder when retrying; the converter will not overwrite an existing pack.

Conversion exports static geometry and textures. It does not reproduce proprietary shaders, animation, particles or game scripts, and it does not establish visual parity with the game. Future game formats or prefab naming changes may require converter updates. See [the prepared-pack contract](PREPARED-PACK.md).

#!/usr/bin/env python3
"""Verify and ZIP a reported Windows Mono Player; keep game assets separate."""

import argparse
import hashlib
import json
import re
import shutil
import stat
import zipfile
from pathlib import Path, PurePosixPath

ROOT = Path(__file__).resolve().parents[1]


def digest(path):
    value = hashlib.sha256()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            value.update(chunk)
    return value.hexdigest()


def checked_path(value, root):
    path = Path(value).expanduser()
    if not path.is_absolute():
        path = root / path
    # resolve() alone would hide a symlink in the selected directory's ancestors.
    for ancestor in (path, *path.parents):
        if ancestor.is_symlink():
            raise ValueError(f"Symlinks are not allowed: {ancestor}")
    return path.resolve()


def inside(path, parent):
    return path == parent or parent in path.parents


def inventory(source):
    files = {}
    for path in sorted(source.rglob("*")):
        relative = path.relative_to(source).as_posix()
        if path.is_symlink():
            raise ValueError(f"Symlinks are not allowed: {relative}")
        mode = path.stat().st_mode
        if not (stat.S_ISREG(mode) or stat.S_ISDIR(mode)):
            raise ValueError(f"Unsupported filesystem entry: {relative}")
        parts = relative.lower().split("/")
        if path.name.lower() == "catalog.json" or any(
            part in {"content", "raw", "extracted", "asset-pack", "asset-packs", "downloaded-assets"}
            for part in parts
        ):
            raise ValueError(f"Downloaded game content is not distributable: {relative}")
        if path.is_file() and path.suffix.lower() != ".meta":
            files[relative] = digest(path)
    return files


def reported_files(records):
    if not isinstance(records, list) or not records:
        raise ValueError("Build report must list all Player files in files: [{path, sha256}].")
    files = {}
    windows_names = set()
    for record in records:
        if not isinstance(record, dict):
            raise ValueError("Invalid build file record.")
        name, sha = record.get("path"), record.get("sha256")
        if not isinstance(name, str) or not name or "\\" in name or ":" in name:
            raise ValueError(f"Unsafe build file path: {name!r}")
        path = PurePosixPath(name)
        if path.is_absolute() or any(part in {".", ".."} for part in name.split("/")) or path.as_posix() != name:
            raise ValueError(f"Unsafe build file path: {name!r}")
        for part in path.parts:
            if (re.search(r'[<>:"|?*\x00-\x1f]', part) or part.endswith((".", " "))
                    or re.fullmatch(r"(?i)(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])", part.split(".")[0])):
                raise ValueError(f"Unsafe Windows file path: {name!r}")
        if name.casefold() in windows_names:
            raise ValueError(f"Duplicate Windows file path: {name}")
        if not isinstance(sha, str) or not re.fullmatch(r"[0-9a-f]{64}", sha):
            raise ValueError(f"Invalid SHA256 for {name}")
        windows_names.add(name.casefold())
        files[name] = sha
    return files


def package(source, evidence, output_dir, allow_development=False, root=ROOT):
    root = root.resolve()
    source = checked_path(source, root)
    evidence = checked_path(evidence, root)
    output_dir = checked_path(output_dir, root)
    if not source.is_dir() or source == root or source == Path(source.anchor):
        raise ValueError("Select a dedicated built Player directory.")
    for selected in (source, evidence, output_dir):
        if (selected == root or selected == Path(selected.anchor)
                or inside(selected, root / "Assets") or inside(selected, root / ".cache")):
            raise ValueError("Select dedicated directories separate from downloaded assets and project source.")
    if inside(output_dir, source) or inside(evidence, source) or inside(source, evidence):
        raise ValueError("Player, evidence and package output must be separate directories.")
    report_path = checked_path(evidence / "build-result.json", root)
    report = json.loads(report_path.read_text(encoding="utf-8"))
    if not isinstance(report, dict):
        raise ValueError("Build report must be an object.")
    if report.get("result") != "Succeeded" or type(report.get("errors")) is not int or report["errors"] != 0:
        raise ValueError("Packaging requires a successful build with zero errors.")
    if report.get("target") != "StandaloneWindows64" or report.get("backend") != "Mono":
        raise ValueError("Packaging requires a StandaloneWindows64 Mono build.")
    if type(report.get("development")) is not bool:
        raise ValueError("Build report must state development as a boolean.")
    if report["development"] and not allow_development:
        raise ValueError("Development build rejected; use --allow-development explicitly.")
    for field in ("warnings", "bytes"):
        if type(report.get(field)) is not int or report[field] < 0:
            raise ValueError(f"Build report must state a nonnegative integer {field}.")
    version = report.get("version")
    if not isinstance(version, str) or not re.fullmatch(r"[0-9][A-Za-z0-9._-]{0,79}", version):
        raise ValueError("Build report version must be a safe filename component starting with a digit.")
    fingerprint = report.get("sourceFingerprint")
    if not isinstance(fingerprint, str) or not re.fullmatch(r"[0-9a-f]{64}", fingerprint):
        raise ValueError("Build report must include the build-time SHA256 sourceFingerprint.")
    if not isinstance(report.get("output"), str) or checked_path(report["output"], root) != source / "BlueCafe.exe":
        raise ValueError("Build report output does not match selected source/BlueCafe.exe.")
    expected = reported_files(report.get("files"))
    actual = inventory(source)
    if actual != expected:
        missing = sorted(expected.keys() - actual.keys())
        extra = sorted(actual.keys() - expected.keys())
        changed = sorted(name for name in expected.keys() & actual.keys() if expected[name] != actual[name])
        raise ValueError(f"Player differs from build report: missing={missing}, extra={extra}, changed={changed}")
    for required in ("BlueCafe.exe", "UnityPlayer.dll"):
        if required not in actual or (source / required).stat().st_size == 0:
            raise ValueError(f"Required Player file missing or empty: {required}")
    if not any(name.startswith("BlueCafe_Data/") for name in actual):
        raise ValueError("BlueCafe_Data is missing or empty.")
    if not any(name.startswith("MonoBleedingEdge/") for name in actual):
        raise ValueError("MonoBleedingEdge runtime is missing or empty.")
    notice = checked_path(root / "NOTICE.md", root)
    license_path = checked_path(root / "Assets/BlueCafe/App/Resources/StudioFont-LICENSE.txt", root)
    if not notice.is_file() or not license_path.is_file():
        raise ValueError("NOTICE.md and the Noto Sans OFL license are required.")
    name = f"BlueCafe-{version}-windows-x86_64"
    folder = output_dir / name
    archive = output_dir / f"{name}.zip"
    for destination in (folder, archive, evidence / "package.json", evidence / "manifest.json"):
        if destination.exists() or destination.is_symlink():
            raise ValueError(f"Destination already exists; preserve or move it first: {destination}")
    reserved = {"readme.txt", "notice.md", "manifest.json", "licenses"}
    if any(path.split("/")[0].casefold() in reserved for path in actual):
        raise ValueError("Player contains reserved packaging paths.")

    output_dir.mkdir(parents=True, exist_ok=True)
    folder.mkdir()  # Exclusive creation preserves any existing package.
    archive_created = False
    evidence_created = []
    try:
        for relative, sha in actual.items():
            destination = folder / relative
            destination.parent.mkdir(parents=True, exist_ok=True)
            source_file = checked_path(source / relative, root)
            shutil.copy2(source_file, destination)
            if digest(destination) != sha:
                raise ValueError(f"Player changed during packaging: {relative}")
        if inventory(source) != expected:
            raise ValueError("Player changed during packaging.")
        shutil.copy2(notice, folder / "NOTICE.md")
        (folder / "licenses").mkdir()
        shutil.copy2(license_path, folder / "licenses/NotoSans-OFL.txt")
        (folder / "README.txt").write_text(
            f"""BlueCafe {version} — Windows x86_64 {'development' if report['development'] else 'release'} candidate
Extract this ZIP and keep the entire folder together. Start BlueCafe.exe.
Unity Editor and Python are not required to run the app.

Game assets are excluded. Supply a separately prepared folder containing catalog.json
in Settings, or launch from PowerShell with:
  .\\BlueCafe.exe --asset-pack "C:\\BlueCafeAssets"
Windows native file/folder dialogs are unavailable; enter paths in the app.
For separate saved cafes, preferences and recovery:
  .\\BlueCafe.exe --asset-pack "C:\\BlueCafeAssets" --data-path "C:\\BlueCafeData"
Close the previous app before sharing its data folder. Back up existing user data.
Portable layouts can be opened with --layout "C:\\Layouts\\cafe.layout.json".

This package has verified file integrity. Windows installation, rendering, physical
input, save/reopen, recovery, performance and human visual acceptance remain
unverified until exercised on Windows. Packaging is not Player acceptance.
The manifest records the build-time source fingerprint and packaged file hashes.
""", encoding="utf-8")
        manifest = dict(version=version, platform="windows-x86_64", development=report["development"],
                        gameAssetsBundled=False, sourceFingerprint=fingerprint, build=report,
                        acceptance="Windows Player acceptance unverified",
                        files={path.relative_to(folder).as_posix(): digest(path)
                               for path in sorted(folder.rglob("*")) if path.is_file()})
        manifest_text = json.dumps(manifest, indent=2) + "\n"
        (folder / "manifest.json").write_text(manifest_text, encoding="utf-8")
        with archive.open("xb") as stream:
            archive_created = True
            with zipfile.ZipFile(stream, "w", zipfile.ZIP_DEFLATED) as output:
                for path in sorted(folder.rglob("*")):
                    output.write(path, f"{name}/{path.relative_to(folder).as_posix()}")
        result = dict(path=str(archive), bytes=archive.stat().st_size, sha256=digest(archive),
                      package=str(folder), sourceFingerprint=fingerprint, version=version,
                      target=report["target"], backend=report["backend"], development=report["development"])
        # Exclusive evidence creation also preserves previous packaging records.
        with (evidence / "manifest.json").open("x", encoding="utf-8") as stream:
            evidence_created.append(evidence / "manifest.json")
            stream.write(manifest_text)
        with (evidence / "package.json").open("x", encoding="utf-8") as stream:
            evidence_created.append(evidence / "package.json")
            stream.write(json.dumps(result, indent=2) + "\n")
        return result
    except BaseException:
        shutil.rmtree(folder)
        if archive_created:
            archive.unlink()
        for path in evidence_created:
            path.unlink()
        raise


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--source", default="Builds/Windows", help="Built Player directory (relative to project by default)")
    parser.add_argument("--evidence", default="evidence/windows-build", help="Directory containing build-result.json")
    parser.add_argument("--output-dir", default="Builds", help="Package folder and ZIP destination")
    parser.add_argument("--allow-development", action="store_true", help="Explicitly allow a development Player")
    args = parser.parse_args()
    try:
        result = package(args.source, args.evidence, args.output_dir, args.allow_development)
    except (OSError, ValueError) as error:
        parser.exit(1, f"Packaging failed: {error}\n")
    print(json.dumps(result, indent=2))


if __name__ == "__main__":
    main()

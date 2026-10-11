#!/usr/bin/env python3
"""Convert locally downloaded Blue Archive Unity .bundle files to a BlueCafe pack.

This command does not download assets, launch Unity, or modify original files.
Missing prefabs/dependencies remain explicitly unavailable in the catalog.
"""
from __future__ import annotations

import argparse
import hashlib
import json
from pathlib import Path
import shutil
import sys


def fingerprint(path, relative=None):
    digest = hashlib.sha256()
    with path.open('rb') as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b''):
            digest.update(chunk)
    return {'path': relative or str(path), 'bytes': path.stat().st_size,
            'sha256': digest.hexdigest()}


def read_json(path):
    with path.open(encoding='utf-8-sig') as stream:
        return json.load(stream)


def validate_metadata(metadata):
    if not isinstance(metadata, dict) or not metadata:
        raise ValueError('Metadata must be a nonempty SchaleDB furniture JSON object.')
    ids = set()
    for raw in metadata.values():
        if not isinstance(raw, dict):
            raise ValueError('Every furniture entry must be an object.')
        if not all(field in raw for field in ('Id', 'Icon', 'Name', 'IsReleased')):
            raise ValueError('Furniture requires Id, Icon, Name, and IsReleased fields.')
        identity = str(raw['Id'])
        if not identity.isdecimal() or identity in ids:
            raise ValueError('Furniture IDs must be unique decimal IDs.')
        ids.add(identity)
        if not isinstance(raw['Icon'], str) or not raw['Icon'] or not isinstance(raw['Name'], str) or not raw['Name'].strip():
            raise ValueError('Furniture Icon and Name must be strings.')
        if not isinstance(raw['IsReleased'], list) or len(raw['IsReleased']) < 2:
            raise ValueError('IsReleased must contain the SchaleDB region flags.')
        theme = raw.get('SetGroupId', 0)
        if isinstance(theme, bool) or not isinstance(theme, int):
            raise ValueError('SetGroupId must be an integer.')
        size = raw.get('Size', [1, 1])
        if not isinstance(size, list) or len(size) != 2 or any(
                isinstance(n, bool) or not isinstance(n, (int, float)) or
                not 0 < n < float('inf') for n in size):
            raise ValueError('Furniture Size must contain two finite positive numbers.')
    return metadata


def paths_overlap(a, b):
    return a == b or a in b.parents or b in a.parents


def convert(args, backend=None):
    bundles = args.bundles.resolve(strict=True)
    metadata_path = args.metadata.resolve(strict=True)
    output = args.output.resolve()
    if not bundles.is_dir():
        raise ValueError('--bundles must be a directory containing original .bundle files.')
    if output.exists():
        raise ValueError('--output already exists; choose a new folder.')
    inputs = [bundles, metadata_path]
    if args.localization:
        inputs.append(args.localization.resolve(strict=True))
    if args.thumbnails:
        inputs.append(args.thumbnails.resolve(strict=True))
    if args.unity_data:
        inputs.append(args.unity_data.resolve(strict=True))
    if any(paths_overlap(output, source) for source in inputs):
        raise ValueError('Output must be separate from every input file/folder.')
    paths = sorted(p for p in bundles.rglob('*') if p.suffix.lower() == '.bundle' and p.is_file())
    if not paths:
        raise ValueError('No .bundle files found. Supply downloaded original Unity bundles, not exported models.')
    if any(p.is_symlink() for p in paths):
        raise ValueError('Bundle inputs must be regular files, not symlinks.')
    # Unity reference resolution uses bundle names; duplicates are ambiguous.
    names = [p.name.casefold() for p in paths]
    if len(names) != len(set(names)):
        raise ValueError('Duplicate bundle filenames found; supply one region/version at a time.')
    metadata = validate_metadata(read_json(metadata_path))
    localization = read_json(args.localization) if args.localization else {'FurnitureSet': {}}
    if not isinstance(localization, dict) or not isinstance(localization.get('FurnitureSet', {}), dict):
        raise ValueError('Localization must be a JSON object with a FurnitureSet object.')
    if args.thumbnails and not args.thumbnails.is_dir():
        raise ValueError('--thumbnails must be a directory of furniture ID.png files.')
    provenance = {'format': 1, 'mode': 'local-input', 'region': args.region,
                  'gameVersion': args.game_version, 'metadata': fingerprint(metadata_path),
                  'bundles': [fingerprint(p, p.relative_to(bundles).as_posix()) for p in paths],
                  'originalsModified': False}
    if args.localization:
        provenance['localization'] = fingerprint(args.localization)
    if backend is None:
        from asset_extract import extract
        backend = extract
    output.mkdir(parents=True, exist_ok=False)
    report_dir = output / 'ConversionReport'
    report_dir.mkdir()
    provenance_path = report_dir / 'provenance.json'
    provenance_path.write_text(json.dumps(provenance, ensure_ascii=False, indent=2), encoding='utf-8')
    if args.thumbnails:
        provenance['thumbnails'] = []
        for raw in metadata.values():
            path = args.thumbnails / (str(raw['Id']) + '.png')
            if path.is_file():
                from PIL import Image
                with Image.open(path) as image:
                    if image.format != 'PNG':
                        raise ValueError('Thumbnail must be PNG: ' + str(path))
                    image.verify()
                target = output / 'Thumbnails' / path.name
                target.parent.mkdir(exist_ok=True)
                shutil.copyfile(path, target)
                provenance['thumbnails'].append(fingerprint(path, path.name))
        provenance_path.write_text(json.dumps(provenance, ensure_ascii=False, indent=2), encoding='utf-8')
    source = {'region': args.region, 'latest_build_version': args.game_version,
              'metadataSource': 'local:' + metadata_path.name,
              'patch': {'patch_version': 'unknown', 'resource_path': 'local-input:see ConversionReport/provenance.json'}}
    try:
        report = backend(paths, metadata, localization, output, source, args.unity_data)
        # Verify the originals still match the recorded source bytes.
        if any(fingerprint(p, record['path']) != record for p, record in zip(paths, provenance['bundles'])):
            raise ValueError('Source bundles changed during conversion; discard this output and retry.')
        print(f"Pack: {output}\nAvailable: {report['available']}/{report['catalogEntries']}; "
              f"unavailable: {report['catalogEntries'] - report['available']}\n"
              f"Details: {report_dir / 'extraction-local.json'}", flush=True)
        return report
    except Exception as exc:
        (report_dir / 'FAILED.txt').write_text(str(exc) + '\n', encoding='utf-8')
        raise


def parser():
    result = argparse.ArgumentParser(description=__doc__)
    result.add_argument('--bundles', type=Path, required=True, help='Downloaded .bundle directory (searched recursively); one region/version')
    result.add_argument('--metadata', type=Path, required=True, help='Locally saved SchaleDB furniture.json')
    result.add_argument('--localization', type=Path, help='Optional SchaleDB localization.json for theme names')
    result.add_argument('--thumbnails', type=Path, help='Optional local folder of furniture ID.png thumbnails')
    result.add_argument('--output', type=Path, required=True, help='New pack directory; must be separate from inputs')
    result.add_argument('--unity-data', type=Path, help='Optional installed Unity Editor/Data directory to resolve built-in meshes; Unity is not launched')
    result.add_argument('--region', default='unknown', help='Provenance region label, e.g. global, jp, cn (default: unknown)')
    result.add_argument('--game-version', default='unknown', help='Provenance version label; not inferred or downloaded')
    return result


def main():
    arguments = parser().parse_args()
    try:
        convert(arguments)
    except (OSError, ValueError, ImportError, KeyError, TypeError) as exc:
        print(f'Conversion failed: {exc}', file=sys.stderr)
        return 1
    return 0


if __name__ == '__main__':
    raise SystemExit(main())

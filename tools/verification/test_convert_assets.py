"""Safety and portability checks for the local converter (standard library only)."""
import importlib.util
import json
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest

TOOLS = Path(__file__).resolve().parents[1]
spec = importlib.util.spec_from_file_location('convert_assets', TOOLS / 'convert_assets.py')
converter = importlib.util.module_from_spec(spec)
spec.loader.exec_module(converter)


class ConverterTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.bundles = self.root / 'bundles'
        (self.bundles / 'nested').mkdir(parents=True)
        self.original = self.bundles / 'nested' / 'chair.bundle'
        self.original.write_bytes(b'original source bytes')
        self.metadata = self.root / 'furniture.json'
        self.metadata.write_text(json.dumps({'1': {'Id': 1, 'Icon': 'chair',
                                                 'Name': 'Chair', 'IsReleased': [True, True]}}))
        self.output = self.root / 'pack'

    def arguments(self, *extra):
        return converter.parser().parse_args(['--bundles', str(self.bundles),
                                              '--metadata', str(self.metadata),
                                              '--output', str(self.output), *extra])

    def test_help_requires_no_dependencies(self):
        result = subprocess.run([sys.executable, '-S', str(TOOLS / 'convert_assets.py'), '--help'],
                                text=True, capture_output=True)
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertIn('--bundles', result.stdout)

    def test_recursive_inputs_and_provenance_preserve_original_bytes(self):
        seen = []
        def backend(paths, metadata, localization, output, source, unity_data):
            seen.extend(paths)
            self.assertEqual(localization, {'FurnitureSet': {}})
            self.assertEqual(source['region'], 'unknown')
            self.assertEqual(source['latest_build_version'], 'unknown')
            self.assertFalse((output / 'chair.bundle').exists())
            return {'catalogEntries': 1, 'available': 0}
        converter.convert(self.arguments(), backend)
        self.assertEqual(seen, [self.original])
        self.assertEqual(self.original.read_bytes(), b'original source bytes')
        provenance = json.loads((self.output / 'ConversionReport' / 'provenance.json').read_text())
        self.assertEqual(provenance['bundles'][0], converter.fingerprint(self.original, 'nested/chair.bundle'))

    def test_output_overlap_and_existing_output_are_rejected_before_backend(self):
        for output in (self.bundles / 'pack', self.root):
            args = self.arguments()
            args.output = output
            with self.assertRaises(ValueError):
                converter.convert(args, lambda *a: self.fail('Backend called'))
        self.assertFalse((self.bundles / 'pack').exists())

    def test_duplicate_bundle_names_are_rejected(self):
        (self.bundles / 'CHAIR.bundle').write_bytes(b'other version')
        with self.assertRaisesRegex(ValueError, 'Duplicate bundle'):
            converter.convert(self.arguments(), lambda *a: self.fail('Backend called'))
        self.assertFalse(self.output.exists())

    def test_invalid_metadata_does_not_create_output(self):
        self.metadata.write_text('{"1":{"Id":1}}')
        with self.assertRaises(ValueError):
            converter.convert(self.arguments(), lambda *a: self.fail('Backend called'))
        self.assertFalse(self.output.exists())

    def test_invalid_name_and_theme_are_rejected(self):
        raw = json.loads(self.metadata.read_text())['1']
        for field, value in [('Name', ''), ('SetGroupId', 'not-an-integer')]:
            self.metadata.write_text(json.dumps({'1': {**raw, field: value}}))
            with self.assertRaises(ValueError):
                converter.convert(self.arguments(), lambda *a: self.fail('Backend called'))
            self.assertFalse(self.output.exists())

    def test_changed_source_and_backend_failure_record_failed_output(self):
        def backend(*args):
            self.original.write_bytes(b'changed concurrently')
            return {'catalogEntries': 1, 'available': 1}
        with self.assertRaisesRegex(ValueError, 'changed during conversion'):
            converter.convert(self.arguments(), backend)
        self.assertTrue((self.output / 'ConversionReport' / 'FAILED.txt').exists())
        with self.assertRaisesRegex(ValueError, 'already exists'):
            converter.convert(self.arguments(), backend)


if __name__ == '__main__':
    unittest.main()

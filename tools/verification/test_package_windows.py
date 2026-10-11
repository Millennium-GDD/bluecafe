"""Synthetic Player packaging proof; this does not establish Windows acceptance."""
import importlib.util
import json
import tempfile
import unittest
import zipfile
from pathlib import Path

SPEC = importlib.util.spec_from_file_location("package_windows", Path(__file__).resolve().parents[1] / "package_windows.py")
PACKAGER = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(PACKAGER)


class WindowsPackageTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.source = self.root / "Builds/Windows"
        self.evidence = self.root / "evidence/windows-build"
        self.evidence.mkdir(parents=True)
        for relative, content in {
            "BlueCafe.exe": b"synthetic exe",
            "UnityPlayer.dll": b"synthetic runtime",
            "BlueCafe_Data/globalgamemanagers": b"synthetic player data",
            "BlueCafe_Data/Managed/BlueCafe.App.dll": b"synthetic assembly",
            "MonoBleedingEdge/EmbedRuntime/mono-2.0-bdwgc.dll": b"synthetic mono",
            "ignored.meta": b"Unity metadata",
        }.items():
            path = self.source / relative
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_bytes(content)
        (self.root / "NOTICE.md").write_text("Project notice", encoding="utf-8")
        font = self.root / "Assets/BlueCafe/App/Resources/StudioFont-LICENSE.txt"
        font.parent.mkdir(parents=True)
        font.write_text("Noto OFL fixture", encoding="utf-8")
        self.report = dict(result="Succeeded", version="0.1.0-test", errors=0, warnings=0, bytes=128,
                           development=False, target="StandaloneWindows64", backend="Mono",
                           output=str(self.source / "BlueCafe.exe"), sourceFingerprint="a" * 64,
                           files=[dict(path=path, sha256=sha) for path, sha in PACKAGER.inventory(self.source).items()])
        self.write_report()

    def write_report(self):
        (self.evidence / "build-result.json").write_text(json.dumps(self.report), encoding="utf-8")

    def package(self, allow_development=False):
        return PACKAGER.package("Builds/Windows", "evidence/windows-build", "Builds",
                                allow_development, root=self.root)

    def assert_rejected(self, message):
        with self.assertRaisesRegex(ValueError, message):
            self.package()
        self.assertFalse((self.root / "Builds/BlueCafe-0.1.0-test-windows-x86_64").exists())
        self.assertFalse((self.evidence / "package.json").exists())

    def test_complete_player_archive_roundtrip(self):
        # Also prove project-relative report output paths.
        self.report["output"] = "Builds/Windows/BlueCafe.exe"
        self.write_report()
        result = self.package()
        self.assertEqual(PACKAGER.digest(Path(result["path"])), result["sha256"])
        manifest = json.loads((self.evidence / "manifest.json").read_text())
        self.assertEqual(manifest["sourceFingerprint"], self.report["sourceFingerprint"])
        self.assertEqual(manifest["build"], self.report)
        self.assertFalse(manifest["gameAssetsBundled"])
        with zipfile.ZipFile(result["path"]) as archive:
            self.assertIsNone(archive.testzip())
            archive.extractall(self.root / "roundtrip")
        unpacked = self.root / "roundtrip" / Path(result["package"]).name
        for path, sha in manifest["files"].items():
            self.assertEqual(PACKAGER.digest(unpacked / path), sha)
        self.assertFalse((unpacked / "ignored.meta").exists())
        self.assertTrue((unpacked / "licenses/NotoSans-OFL.txt").exists())
        self.assertIn("--asset-pack", (unpacked / "README.txt").read_text())

    def test_report_must_match_current_player_exactly(self):
        for mutation, message in [("changed", "changed="), ("extra", "extra="), ("missing", "missing=")]:
            with self.subTest(mutation=mutation):
                path = self.source / "UnityPlayer.dll"
                if mutation == "changed":
                    path.write_bytes(b"stale report")
                elif mutation == "extra":
                    (self.source / "leftover.dll").write_bytes(b"old output")
                else:
                    path.unlink()
                self.assert_rejected(message)
                path.write_bytes(b"synthetic runtime")
                (self.source / "leftover.dll").unlink(missing_ok=True)

    def test_reject_linux_failed_or_mismatched_report(self):
        for field, value, message in [
            ("target", "StandaloneLinux64", "StandaloneWindows64"),
            ("backend", "IL2CPP", "Mono"),
            ("result", "Failed", "successful build"),
            ("errors", 1, "zero errors"),
            ("output", "Builds/Old/BlueCafe.exe", "does not match"),
            ("sourceFingerprint", "missing", "sourceFingerprint"),
        ]:
            with self.subTest(field=field):
                old = self.report[field]
                self.report[field] = value
                self.write_report()
                self.assert_rejected(message)
                self.report[field] = old

    def test_development_requires_explicit_override(self):
        self.report["development"] = True
        self.write_report()
        self.assert_rejected("--allow-development")
        self.assertTrue(self.package(allow_development=True)["development"])

    def test_preserves_existing_package_or_archive(self):
        for suffix in ("", ".zip"):
            with self.subTest(suffix=suffix):
                destination = self.root / f"Builds/BlueCafe-0.1.0-test-windows-x86_64{suffix}"
                if suffix:
                    destination.write_bytes(b"preserve archive")
                    marker = destination
                else:
                    destination.mkdir()
                    marker = destination / "existing.txt"
                    marker.write_bytes(b"preserve package")
                before = marker.read_bytes()
                with self.assertRaisesRegex(ValueError, "already exists"):
                    self.package()
                self.assertEqual(marker.read_bytes(), before)
                marker.unlink()
                if not suffix:
                    destination.rmdir()

    def test_reject_symlink_and_downloaded_content(self):
        link = self.source / "linked.dll"
        link.symlink_to(self.source / "UnityPlayer.dll")
        self.assert_rejected("Symlinks")
        link.unlink()
        # Even fully reported downloaded content must stay out of the archive.
        catalog = self.source / "catalog.json"
        catalog.write_text("{}", encoding="utf-8")
        self.report["files"].append(dict(path="catalog.json", sha256=PACKAGER.digest(catalog)))
        self.write_report()
        self.assert_rejected("Downloaded game content")

    def test_reject_unsafe_or_duplicate_paths_and_missing_mono(self):
        original = list(self.report["files"])
        for path in ("../outside", "BlueCafe_Data/../escape", "C:/file", "dir/NUL.txt", "dir/file.", "dir\\file"):
            with self.subTest(path=path):
                self.report["files"] = original + [dict(path=path, sha256="b" * 64)]
                self.write_report()
                self.assert_rejected("Unsafe")
        self.report["files"] = original + [dict(path="BLUECAFE.EXE", sha256="b" * 64)]
        self.write_report()
        self.assert_rejected("Duplicate")
        mono = self.source / "MonoBleedingEdge/EmbedRuntime/mono-2.0-bdwgc.dll"
        mono.unlink()
        self.report["files"] = [item for item in original if not item["path"].startswith("MonoBleedingEdge/")]
        self.write_report()
        self.assert_rejected("MonoBleedingEdge")

    def test_reject_unsafe_directories_missing_notices_and_previous_evidence(self):
        for output in (".", "Assets/Package", "Builds/Windows/Package"):
            with self.subTest(output=output):
                with self.assertRaisesRegex(ValueError, "directories"):
                    PACKAGER.package(self.source, self.evidence, output, root=self.root)
        alias = self.root / "alias"
        alias.symlink_to(self.source, target_is_directory=True)
        with self.assertRaisesRegex(ValueError, "Symlinks"):
            PACKAGER.package(alias, self.evidence, "Builds", root=self.root)
        (self.root / "NOTICE.md").unlink()
        self.assert_rejected("NOTICE.md")
        (self.root / "NOTICE.md").write_text("Restored notice", encoding="utf-8")
        previous = self.evidence / "manifest.json"
        previous.write_bytes(b"preserve evidence")
        with self.assertRaisesRegex(ValueError, "already exists"):
            self.package()
        self.assertEqual(previous.read_bytes(), b"preserve evidence")


if __name__ == "__main__":
    unittest.main()

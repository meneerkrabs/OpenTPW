"""Pinned CDN staging uses tiny ZIP fixtures and never makes HTTP requests."""
import hashlib
import contextlib
import importlib.util
import io
import json
from pathlib import Path
import stat
import sys
import tempfile
import unittest
from unittest import mock
import warnings
import zipfile


def load_module(name, filename):
    spec = importlib.util.spec_from_file_location(name, Path(__file__).with_name(filename))
    module = importlib.util.module_from_spec(spec)
    sys.modules[name] = module
    spec.loader.exec_module(module)
    return module


stage_tool = load_module("stage_texture_pages", "stage_texture_pages.py")
fixtures = load_module("texture_pack_fixtures", "test_package_texture_pack.py")
PREFIX = "content/texture-packs/clarity-enhanced/"
RELEASE = "https://github.com/meneerkrabs/OpenTPW/releases/download/texture-pack-clarity-enhanced-20261011/"


class TexturePagesStagingTests(unittest.TestCase):
    def setUp(self):
        capture = contextlib.redirect_stdout(io.StringIO())
        capture.__enter__()
        self.addCleanup(capture.__exit__, None, None, None)
        self.temp = tempfile.TemporaryDirectory()
        self.root = Path(self.temp.name).resolve()
        self.cache = self.root / "cache"
        self.cache.mkdir()
        self.output = self.root / "site"
        self.index = self.root / "distribution.json"
        self.checksum = self.root / "distribution.sha256"
        self.files = {
            "pack.json": b'{"Format":1,"Textures":2}',
            "textures/ui/textures/button.wct.png": fixtures.png(),
            "textures/levels/jungle/terrain/textures/grass.wct.png": fixtures.png(3, 2),
        }
        self.data = {
            "format": 1, "name": "clarity-enhanced", "version": "20261011",
            "archivePrefix": PREFIX,
            "files": [{"path": path, "size": len(data), "sha256": hashlib.sha256(data).hexdigest()}
                      for path, data in self.files.items()],
            "archives": [],
        }
        self.members = {
            "base": [(PREFIX + path, data, stat.S_IFREG | 0o644)
                     for path, data in self.files.items() if not path.startswith("textures/levels/")],
            "jungle": [(PREFIX + path, data, stat.S_IFREG | 0o644)
                       for path, data in self.files.items() if path.startswith("textures/levels/")],
        }
        self.build_archive("base")
        self.build_archive("jungle")
        self.data["archives"].append({"kind": "movies", "name": "unused-movie.zip",
                                      "url": "https://must-not-download.invalid/movie", "size": 100,
                                      "sha256": "0" * 64})
        self.write_index()

    def tearDown(self):
        self.temp.cleanup()

    def build_archive(self, part):
        name = f"OpenTPW-clarity-enhanced-{part}.zip"
        path = self.cache / name
        with warnings.catch_warnings():
            warnings.simplefilter("ignore", UserWarning)
            with zipfile.ZipFile(path, "w") as archive:
                for filename, data, mode in self.members[part]:
                    entry = zipfile.ZipInfo(filename)
                    entry.create_system = 3
                    entry.external_attr = mode << 16
                    archive.writestr(entry, data)
        record = {"name": name, "url": RELEASE + name, "kind": "textures",
                  "size": path.stat().st_size, "sha256": stage_tool.digest(path)}
        for existing in self.data["archives"]:
            if existing["name"] == name:
                existing.update(record)
                break
        else:
            self.data["archives"].append(record)

    def write_index(self):
        raw = (json.dumps(self.data, indent=2) + "\n").encode("utf-8")
        self.index.write_bytes(raw)
        self.checksum.write_text(hashlib.sha256(raw).hexdigest() + "  distribution.json\n")

    def stage(self):
        self.write_index()
        with mock.patch.object(stage_tool, "urlopen", side_effect=AssertionError("HTTP is forbidden")):
            stage_tool.stage(self.index, self.checksum, self.output, self.cache)

    def assert_no_publication(self):
        self.assertFalse(self.output.exists())
        self.assertEqual(list(self.root.glob("texture-cdn-*")), [])

    def test_cached_stage_preserves_exact_bytes_pins_and_cors_without_movies_or_http(self):
        cached = {path.name: path.read_bytes() for path in self.cache.glob("*.zip")}
        self.stage()
        target = self.output / "clarity-enhanced/20261011"
        for path, data in self.files.items():
            self.assertEqual((target / path).read_bytes(), data)
        self.assertEqual((target / "distribution.json").read_bytes(), self.index.read_bytes())
        self.assertEqual((target / "distribution.sha256").read_text(), self.checksum.read_text())
        self.assertEqual({str(path.relative_to(target)) for path in target.rglob("*") if path.is_file()},
                         set(self.files) | {"distribution.json", "distribution.sha256"})
        headers = (self.output / "_headers").read_text()
        self.assertIn("Access-Control-Allow-Origin: *", headers)
        self.assertIn("Cache-Control: public, max-age=31536000, immutable", headers)
        self.assertIn("X-Content-Type-Options: nosniff", headers)
        self.assertTrue((self.output / "404.html").is_file())
        for name, data in cached.items():
            self.assertEqual((self.cache / name).read_bytes(), data)

    def test_wrong_descriptor_pin_fails_before_cache_or_publication_changes(self):
        self.checksum.write_text("0" * 64 + "  distribution.json\n")
        with mock.patch.object(stage_tool, "urlopen", side_effect=AssertionError("HTTP is forbidden")):
            with self.assertRaisesRegex(ValueError, "Descriptor checksum mismatch"):
                stage_tool.stage(self.index, self.checksum, self.output, self.cache)
        self.assert_no_publication()

    def test_archive_traversal_symlink_directories_and_foreign_prefix_are_rejected(self):
        mutations = [(PREFIX + "../escape.png", stat.S_IFREG | 0o644),
                     (PREFIX + "textures/../../escape.png", stat.S_IFREG | 0o644),
                     (self.members["jungle"][0][0], stat.S_IFLNK | 0o777),
                     (PREFIX + "textures/", stat.S_IFDIR | 0o755),
                     ("outside/asset.png", stat.S_IFREG | 0o644)]
        for filename, mode in mutations:
            with self.subTest(filename=filename, mode=mode):
                self.members["jungle"] = [(filename, b"bad", mode)]
                self.build_archive("jungle")
                with self.assertRaises(ValueError):
                    self.stage()
                self.assert_no_publication()
                self.assertFalse((self.root / "escape.png").exists())

    def test_asset_hash_and_size_mismatches_leave_no_partial_publication(self):
        original = self.members["jungle"][0]
        for mutation in (bytes([original[1][0] ^ 1]) + original[1][1:], original[1] + b"extra"):
            with self.subTest(size=len(mutation)):
                self.members["jungle"] = [(original[0], mutation, original[2])]
                self.build_archive("jungle")
                with self.assertRaisesRegex(ValueError, "Asset (checksum|size) mismatch"):
                    self.stage()
                self.assert_no_publication()

    def test_duplicate_members_and_assets_across_archives_are_rejected(self):
        original = list(self.members["jungle"])
        for members in (original * 2, original + [self.members["base"][0]]):
            with self.subTest(count=len(members)):
                self.members["jungle"] = members
                self.build_archive("jungle")
                with self.assertRaisesRegex(ValueError, "duplicate asset"):
                    self.stage()
                self.assert_no_publication()

    def test_descriptor_paths_extensions_sizes_and_case_duplicates_are_rejected(self):
        original = dict(self.data["files"][1])
        cases = [{**original, "path": path} for path in
                 (".", "../escape.png", "/absolute.png", "textures\\escape.png", "movies/a.mp4")]
        cases += [{**original, "size": True}, {**original, "size": 25 * 1024 * 1024 + 1}]
        for record in cases:
            with self.subTest(record=record):
                self.data["files"][1] = record
                with self.assertRaises(ValueError):
                    self.stage()
                self.assert_no_publication()
        self.data["files"][1] = original
        self.data["files"].append({**original, "path": original["path"].upper()})
        with self.assertRaisesRegex(ValueError, "Duplicate asset path"):
            self.stage()
        self.assert_no_publication()

    def test_cached_archive_url_is_validated_even_when_its_bytes_match(self):
        self.data["archives"][0]["url"] = RELEASE.replace("github.com", "attacker.invalid") + self.data["archives"][0]["name"]
        with self.assertRaisesRegex(ValueError, "Unexpected archive host"):
            self.stage()
        self.assert_no_publication()

    def test_cached_symlinks_are_rejected(self):
        archive = self.cache / self.data["archives"][0]["name"]
        outside = self.root / "outside.zip"
        outside.write_bytes(archive.read_bytes())
        archive.unlink()
        archive.symlink_to(outside)
        with self.assertRaisesRegex(ValueError, "regular files"):
            self.stage()
        self.assert_no_publication()

    def test_output_and_cache_symlinks_are_preserved_and_rejected(self):
        external = self.root / "external"
        external.mkdir()
        self.output.symlink_to(external, target_is_directory=True)
        with self.assertRaisesRegex(ValueError, "must not be symlinks"):
            self.stage()
        self.assertTrue(self.output.is_symlink())
        self.assertEqual(list(external.iterdir()), [])
        self.output.unlink()
        real_cache = self.root / "real-cache"
        self.cache.rename(real_cache)
        self.cache.symlink_to(real_cache, target_is_directory=True)
        with self.assertRaisesRegex(ValueError, "must not be symlinks"):
            self.stage()
        self.assert_no_publication()

    def test_incomplete_archive_set_and_repeated_archive_names_are_rejected(self):
        original = list(self.data["archives"])
        self.data["archives"] = [original[0], original[-1]]
        with self.assertRaisesRegex(ValueError, "Incomplete"):
            self.stage()
        self.assert_no_publication()
        self.data["archives"] = original + [original[0]]
        with self.assertRaisesRegex(ValueError, "Invalid texture archive"):
            self.stage()
        self.assert_no_publication()

    def test_nonempty_output_is_preserved_without_partial_moves(self):
        self.output.mkdir()
        marker = self.output / "404.html"
        marker.write_text("keep")
        with self.assertRaisesRegex(ValueError, "Output must be empty"):
            self.stage()
        self.assertEqual(marker.read_text(), "keep")
        self.assertEqual(list(self.output.iterdir()), [marker])
        self.assertEqual(list(self.root.glob("texture-cdn-*")), [])

    def test_empty_existing_output_can_be_published_together(self):
        self.output.mkdir()
        self.stage()
        self.assertTrue((self.output / "clarity-enhanced/20261011/pack.json").is_file())

    def test_corrupt_cache_is_repaired_with_a_verified_in_memory_download(self):
        archive = self.cache / self.data["archives"][0]["name"]
        correct = archive.read_bytes()
        archive.write_bytes(b"corrupt cached ZIP")
        with mock.patch.object(stage_tool, "urlopen", return_value=io.BytesIO(correct)) as download:
            stage_tool.stage(self.index, self.checksum, self.output, self.cache)
        self.assertEqual(download.call_count, 1)
        self.assertEqual(archive.read_bytes(), correct)
        self.assertTrue((self.output / "clarity-enhanced/20261011/pack.json").is_file())
        self.assertEqual(list(self.cache.glob("*.partial")), [])

    def test_wrong_download_hash_truncation_and_oversize_remove_partial_files(self):
        archive_record = self.data["archives"][0]
        archive = self.cache / archive_record["name"]
        correct = archive.read_bytes()
        archive.unlink()
        invalid = [bytes([correct[0] ^ 1]) + correct[1:], correct[:-8], correct + b"excess"]
        for data in invalid:
            with self.subTest(size=len(data)):
                with mock.patch.object(stage_tool, "urlopen", return_value=io.BytesIO(data)):
                    with self.assertRaisesRegex(ValueError, "Archive checksum mismatch|Oversized archive"):
                        stage_tool.stage(self.index, self.checksum, self.output, self.cache)
                self.assertFalse(archive.exists())
                self.assertEqual(list(self.cache.glob("*.partial")), [])
                self.assert_no_publication()


if __name__ == "__main__":
    unittest.main()

"""Portable texture distribution tests using tiny, synthetic PNG/MP4 fixtures."""
import hashlib
import importlib.util
import json
from pathlib import Path
import struct
import sys
import tempfile
import unittest
from unittest import mock
import zipfile
import zlib

SPEC = importlib.util.spec_from_file_location(
    "package_texture_pack", Path(__file__).with_name("package_texture_pack.py")
)
pack = importlib.util.module_from_spec(SPEC)
sys.modules[SPEC.name] = pack
SPEC.loader.exec_module(pack)


def chunk(kind, data):
    return (struct.pack(">I", len(data)) + kind + data
            + struct.pack(">I", zlib.crc32(kind + data)))


def png(width=2, height=3):
    header = struct.pack(">IIBBBBB", width, height, 8, 6, 0, 0, 0)
    pixels = (b"\x00" + b"\x12\x34\x56\xff" * width) * height
    return (pack.PNG_SIGNATURE + chunk(b"IHDR", header)
            + chunk(b"IDAT", zlib.compress(pixels)) + chunk(b"IEND", b""))


class TexturePackPackagingTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.root = Path(self.temp.name).resolve()
        self.source = self.root / "source"
        self.source.mkdir()
        self.output = self.root / "distribution"
        self.manifest = {"Format": 1, "Scale": 4, "Textures": 5,
                         "InterfaceTextures": 1, "Movies": 9,
                         "Refinements": {"provider": "fixture"}}
        self.write_manifest(self.manifest)
        self.paths = ["textures/ui/textures/button.wct.png"] + [
            f"textures/levels/{world}/terrain/textures/grass.wct.png" for world in pack.WORLDS
        ]
        for path in self.paths:
            self.write(path, png())
        self.write("movies/BFj2.mp4", struct.pack(">I", 24) + b"ftypisom\x00\x00\x02\x00isommp41"
                   + struct.pack(">I", 12) + b"mdatdata")

    def tearDown(self):
        self.temp.cleanup()

    def write(self, relative, data):
        destination = self.source / relative
        destination.parent.mkdir(parents=True, exist_ok=True)
        destination.write_bytes(data)
        return destination

    def write_manifest(self, manifest):
        self.write("pack.json", json.dumps(manifest).encode("utf-8"))

    def package(self, **kwargs):
        return pack.package(self.source, self.output, **kwargs)

    def test_six_archives_extract_beside_the_executable_with_exact_source_bytes(self):
        index = self.package()
        self.assertEqual(index["packManifest"], self.manifest)
        self.assertEqual(len(index["archives"]), 6)
        self.assertEqual(len(index["files"]), 6)
        self.assertEqual(len(index["movies"]), 1)
        extracted = self.root / "application"
        all_names = []
        for record in index["archives"]:
            archive_path = self.output / record["name"]
            self.assertEqual(record["sha256"], pack.sha256_file(archive_path))
            self.assertEqual(record["size"], archive_path.stat().st_size)
            self.assertLess(record["size"], 2 ** 31)
            self.assertIn("texture-pack-clarity-enhanced-20261011/", record["url"])
            with zipfile.ZipFile(archive_path) as archive:
                self.assertIsNone(archive.testzip())
                all_names.extend(archive.namelist())
                self.assertTrue(all(member.compress_type == zipfile.ZIP_STORED
                                    for member in archive.infolist()))
                archive.extractall(extracted)
        expected = set(self.paths) | {"pack.json", "movies/BFj2.mp4"}
        prefix = "content/texture-packs/clarity-enhanced/"
        self.assertEqual(set(all_names), {prefix + path for path in expected})
        self.assertEqual(len(all_names), len(set(all_names)))
        for path in expected:
            self.assertEqual((extracted / prefix / path).read_bytes(),
                             (self.source / path).read_bytes())
        base = next(record for record in index["archives"] if record["name"].endswith("-base.zip"))
        with zipfile.ZipFile(self.output / base["name"]) as archive:
            self.assertEqual(set(archive.namelist()), {
                prefix + "pack.json", prefix + "textures/ui/textures/button.wct.png"
            })

    def test_versioned_static_tree_and_index_hashes_are_self_consistent(self):
        index = self.package()
        index_bytes = (self.output / "distribution.json").read_bytes()
        self.assertEqual(json.loads(index_bytes), index)
        page_root = self.output / "pages/clarity-enhanced/20261011"
        self.assertEqual((page_root / "distribution.json").read_bytes(), index_bytes)
        self.assertEqual(index["textureBaseUrl"],
                         "https://opentpw-textures.pages.dev/clarity-enhanced/20261011/")
        for record in index["files"]:
            data = (page_root / record["path"]).read_bytes()
            self.assertEqual(len(data), record["size"])
            self.assertEqual(hashlib.sha256(data).hexdigest(), record["sha256"])
            if record["path"].endswith(".png"):
                self.assertEqual((record["width"], record["height"]), (2, 3))
        self.assertFalse((page_root / "movies").exists())
        expected_hash = hashlib.sha256(index_bytes).hexdigest()
        self.assertEqual((self.output / "distribution.sha256").read_text(),
                         expected_hash + "  distribution.json\n")
        checksum_lines = (self.output / "SHA256SUMS.txt").read_text().splitlines()
        self.assertEqual(len(checksum_lines), 7)
        for line in checksum_lines:
            digest, filename = line.split("  ", 1)
            self.assertEqual(digest, pack.sha256_file(self.output / filename))

    def test_machine_and_private_review_files_are_excluded(self):
        for path in ("textures/ui/.DS_Store", "movies/.DS_Store", ".DS_Store",
                     "controleverslag.json", "vergelijking.html", "original.exe"):
            self.write(path, b"private metadata")
        index = self.package()
        included = [record["path"] for record in index["files"] + index["movies"]]
        self.assertEqual(set(included), set(self.paths) | {"pack.json", "movies/BFj2.mp4"})
        self.assertNotIn("private metadata", (self.output / "distribution.json").read_text())

    def test_rejects_original_formats_and_executables_inside_asset_trees(self):
        for relative in ("textures/ui/original.wct", "movies/tp.exe", "movies/original.tqi"):
            with self.subTest(relative=relative):
                destination = self.write(relative, b"not distributable")
                with self.assertRaisesRegex(pack.PackError, "Unexpected asset"):
                    self.package()
                self.assertFalse(self.output.exists())
                destination.unlink()

    def test_rejects_symlink_files_and_directories(self):
        target = self.source / self.paths[0]
        for relative, directory in (("textures/ui/link.wct.png", False),
                                    ("textures/ui/link", True)):
            with self.subTest(relative=relative):
                link = self.source / relative
                link.symlink_to(target.parent if directory else target, target_is_directory=directory)
                with self.assertRaises(pack.PackError):
                    self.package()
                link.unlink()
                self.assertFalse(self.output.exists())

    def test_rejects_case_collisions_for_windows_extraction(self):
        # The volume may be case-insensitive. Use a distinct path group with a
        # mocked second enumeration so the collision check runs everywhere.
        existing = self.source / self.paths[0]
        alias = existing.with_name("BUTTON.wct.png")
        paths = list(pack._source_paths(self.source))
        with mock.patch.object(pack, "_source_paths", return_value=iter(paths + [alias])):
            with self.assertRaisesRegex(pack.PackError, "Case-insensitive duplicate"):
                self.package()

    def test_rejects_paths_that_extract_differently_across_platforms(self):
        for path in ("", ".", "../a.png", "/a.png", "a/../b.png", "a//b.png",
                     "a\\b.png", "C:/a.png", "textures/NUL.png", "a/COM1.wct.png",
                     "a/b.png ", "a/b.png.", "a/b\x01.png", "a/b:stream.png"):
            with self.subTest(path=path), self.assertRaises(pack.PackError):
                pack.safe_relative(path)

    def test_rejects_bad_png_headers_crc_fields_and_dimension_limits(self):
        destination = self.source / self.paths[0]
        valid = png()
        invalid = [b"not a PNG", valid[:29] + b"\x00\x00\x00\x00" + valid[33:],
                   valid[:-12], png(0, 2), png(pack.MAX_PNG_DIMENSION + 1, 1)]
        for data in invalid:
            with self.subTest(length=len(data)):
                destination.write_bytes(data)
                with self.assertRaises(pack.PackError):
                    self.package()
        destination.write_bytes(valid)

    def test_rejects_invalid_mp4_and_oversized_static_textures(self):
        movie = self.source / "movies/BFj2.mp4"
        movie.write_bytes(b"not an MP4")
        with self.assertRaisesRegex(pack.PackError, "MP4"):
            self.package()
        movie.unlink()
        with mock.patch.object(pack, "MAX_PAGES_FILE_BYTES", 10):
            with self.assertRaisesRegex(pack.PackError, "25 MiB"):
                self.package()

    def test_rejects_manifest_format_counts_and_nonfinite_metadata(self):
        for data in ({**self.manifest, "Format": 2}, {**self.manifest, "Format": True},
                     {**self.manifest, "Textures": 10}, {**self.manifest, "InterfaceTextures": 0}):
            with self.subTest(data=data):
                self.write_manifest(data)
                with self.assertRaises(pack.PackError):
                    self.package()
        for text in ('{"Format":1,"Scale":NaN}', '{"Format":1,"Scale":1e400}'):
            self.write("pack.json", text.encode("ascii"))
            with self.assertRaisesRegex(pack.PackError, "Non-finite"):
                self.package()

    def test_archive_limit_failure_removes_all_partial_outputs(self):
        with self.assertRaisesRegex(pack.PackError, "archive size limit"):
            self.package(max_archive_bytes=128)
        self.assertFalse(self.output.exists())
        self.assertEqual(list(self.root.glob(".distribution-*")), [])

    def test_source_changed_after_hashing_never_publishes_a_stale_index(self):
        real_write_zip = pack._write_zip
        mutated = False

        def mutate_then_archive(path, files, prefix, maximum):
            nonlocal mutated
            if not mutated:
                (self.source / self.paths[0]).write_bytes(png(3, 3))
                mutated = True
            real_write_zip(path, files, prefix, maximum)

        with mock.patch.object(pack, "_write_zip", side_effect=mutate_then_archive):
            with self.assertRaisesRegex(pack.PackError, "changed while archiving"):
                self.package()
        self.assertFalse(self.output.exists())
        self.assertEqual(list(self.root.glob(".distribution-*")), [])

    def test_existing_output_is_preserved_and_assets_cannot_be_written_to_git(self):
        self.output.mkdir()
        marker = self.output / "keep"
        marker.write_text("preserve")
        with self.assertRaisesRegex(pack.PackError, "already exists"):
            self.package()
        self.assertEqual(marker.read_text(), "preserve")
        repo = self.root / "repo"
        (repo / ".git").mkdir(parents=True)
        with self.assertRaisesRegex(pack.PackError, "outside a Git"):
            pack.package(self.source, repo / "assets")

    def test_output_inside_source_and_unsafe_download_parameters_are_rejected(self):
        with self.assertRaisesRegex(pack.PackError, "outside the source"):
            pack.package(self.source, self.source / "output")
        for kwargs in ({"name": "../pack"}, {"version": "../../escape"},
                       {"repository": "bad/path/extra"}, {"texture_base_url": "http://host/x/"},
                       {"texture_base_url": "https://host/x"},
                       {"texture_base_url": "https://host/x/?token=secret"},
                       {"max_archive_bytes": 2 ** 31}):
            with self.subTest(kwargs=kwargs), self.assertRaises(pack.PackError):
                self.package(**kwargs)


if __name__ == "__main__":
    unittest.main()

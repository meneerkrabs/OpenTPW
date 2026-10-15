"""Portable texture distribution tests using tiny, synthetic PNG/MP4 fixtures."""
import hashlib
import copy
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


class SnapshotReindexTests(unittest.TestCase):
    write = TexturePackPackagingTests.write
    write_manifest = TexturePackPackagingTests.write_manifest
    package = TexturePackPackagingTests.package
    tearDown = TexturePackPackagingTests.tearDown

    def setUp(self):
        TexturePackPackagingTests.setUp(self)
        self.index = self.package()
        self.original_bytes = (self.output / "distribution.json").read_bytes()
        self.map_path = self.root / "snapshots.json"
        tag = "clarity-20261011-001"
        commit = "1" * 40
        self.pngs = [record for record in self.index["files"] if record["path"].endswith(".png")]
        self.mapping = {
            "format": 1, "complete": True, "repository": pack.SNAPSHOT_REPOSITORY,
            "distributionSha256": hashlib.sha256(self.original_bytes).hexdigest(),
            "snapshotPrefix": "clarity-20261011", "maxSnapshotBytes": 1024 * 1024,
            "snapshotCount": 1, "totalFiles": len(self.pngs),
            "totalSize": sum(record["size"] for record in self.pngs),
            "snapshots": [{"tag": tag, "commit": commit, "files": len(self.pngs),
                           "size": sum(record["size"] for record in self.pngs)}],
            "files": [{**{key: record[key] for key in ("path", "size", "sha256")},
                       "tag": tag, "commit": commit,
                       "url": f"https://cdn.jsdelivr.net/gh/{pack.SNAPSHOT_REPOSITORY}@{tag}/{record['path']}"}
                      for record in self.pngs],
        }

    def reindex(self, mapping=None):
        self.map_path.write_text(json.dumps(self.mapping if mapping is None else mapping))
        return pack.reindex_snapshots(self.output, self.map_path)

    def assert_unchanged(self):
        self.assertEqual((self.output / "distribution.json").read_bytes(), self.original_bytes)
        self.assertEqual((self.output / "pages/clarity-enhanced/20261011/distribution.json").read_bytes(),
                         self.original_bytes)
        self.assertFalse((self.output / "distribution.pre-cdn.json").exists())

    def test_reindex_binds_exact_pngs_preserves_media_and_updates_both_index_pins(self):
        archive_bytes = {record["name"]: (self.output / record["name"]).read_bytes()
                         for record in self.index["archives"]}
        rewritten = self.reindex()
        self.assertEqual(rewritten["archives"], self.index["archives"])
        self.assertEqual(rewritten["movies"], self.index["movies"])
        self.assertEqual(rewritten["packManifest"], self.index["packManifest"])
        self.assertEqual(rewritten["textureBaseUrl"], self.index["textureBaseUrl"])
        for original, updated in zip(self.index["files"], rewritten["files"]):
            if original["path"].endswith(".png"):
                self.assertEqual({key: value for key, value in updated.items() if key != "url"}, original)
                self.assertEqual(updated["url"], next(record["url"] for record in self.mapping["files"]
                                                      if record["path"] == original["path"]))
            else:
                self.assertEqual(updated, original)
                self.assertNotIn("url", updated)
        for filename, data in archive_bytes.items():
            self.assertEqual((self.output / filename).read_bytes(), data)
        rewritten_bytes = (self.output / "distribution.json").read_bytes()
        digest = hashlib.sha256(rewritten_bytes).hexdigest()
        self.assertEqual((self.output / "distribution.pre-cdn.json").read_bytes(), self.original_bytes)
        self.assertEqual((self.output / "pages/clarity-enhanced/20261011/distribution.json").read_bytes(),
                         rewritten_bytes)
        self.assertEqual((self.output / "distribution.sha256").read_text(), digest + "  distribution.json\n")
        self.assertIn(digest + "  distribution.json", (self.output / "SHA256SUMS.txt").read_text())

    def test_reindex_rejects_incomplete_wrong_repository_or_unbound_maps_before_writes(self):
        for field, value in (("complete", False), ("complete", 1), ("repository", "attacker/repo"),
                             ("distributionSha256", "0" * 64), ("snapshotPrefix", "another-prefix"),
                             ("format", True)):
            with self.subTest(field=field):
                mapping = {**self.mapping, field: value}
                with self.assertRaises(pack.PackError):
                    self.reindex(mapping)
                self.assert_unchanged()

    def test_reindex_rejects_missing_extra_duplicate_and_changed_asset_records(self):
        variants = []
        for operation in ("missing", "extra", "duplicate", "size", "sha256", "commit", "path"):
            mapping = copy.deepcopy(self.mapping)
            if operation == "missing":
                mapping["files"].pop()
            elif operation == "extra":
                mapping["files"].append({**mapping["files"][0], "path": "textures/ui/extra.wct.png"})
            elif operation == "duplicate":
                mapping["files"].append(mapping["files"][0])
            elif operation == "size":
                mapping["files"][0]["size"] += 1
            elif operation == "sha256":
                mapping["files"][0]["sha256"] = "0" * 64
            elif operation == "commit":
                mapping["files"][0]["commit"] = "2" * 40
            else:
                mapping["files"][0]["path"] = "../escape.png"
            variants.append((operation, mapping))
        for operation, mapping in variants:
            with self.subTest(operation=operation), self.assertRaises(pack.PackError):
                self.reindex(mapping)
            self.assert_unchanged()

    def test_reindex_rejects_untrusted_cdn_hosts_tags_paths_and_queries(self):
        original = self.mapping["files"][0]["url"]
        urls = [original.replace("cdn.jsdelivr.net", "cdn.jsdelivr.net.attacker.invalid"),
                original.replace("https://", "http://"), original + "?token=secret",
                original.replace("clarity-20261011-001", "main"),
                original.replace("/textures/", "/unrelated/"),
                original.replace(pack.SNAPSHOT_REPOSITORY, "attacker/repo")]
        for url in urls:
            with self.subTest(url=url):
                mapping = copy.deepcopy(self.mapping)
                mapping["files"][0]["url"] = url
                with self.assertRaisesRegex(pack.PackError, "Untrusted snapshot URL"):
                    self.reindex(mapping)
                self.assert_unchanged()

    def test_reindex_rejects_snapshot_sequence_and_count_or_size_mismatches(self):
        variants = []
        for field in ("totalFiles", "totalSize", "snapshotCount"):
            mapping = copy.deepcopy(self.mapping)
            mapping[field] += 1
            variants.append(mapping)
        for field in ("size", "files"):
            mapping = copy.deepcopy(self.mapping)
            mapping["snapshots"][0][field] += 1
            variants.append(mapping)
        mapping = copy.deepcopy(self.mapping)
        mapping["snapshots"][0]["tag"] = "clarity-20261011-002"
        variants.append(mapping)
        for mapping in variants:
            with self.subTest(mapping=mapping), self.assertRaises(pack.PackError):
                self.reindex(mapping)
            self.assert_unchanged()

    def test_reindex_hashes_the_actual_static_file_bytes_before_any_writes(self):
        page = self.output / "pages/clarity-enhanced/20261011" / self.pngs[0]["path"]
        content = bytearray(page.read_bytes())
        content[-1] ^= 1
        page.write_bytes(content)
        with self.assertRaisesRegex(pack.PackError, "no longer matches"):
            self.reindex()
        self.assert_unchanged()

    def test_reindex_refuses_an_inconsistent_static_index_or_old_backup(self):
        page_index = self.output / "pages/clarity-enhanced/20261011/distribution.json"
        page_index.write_bytes(b"{}")
        with self.assertRaisesRegex(pack.PackError, "indexes differ"):
            self.reindex()
        self.assertEqual((self.output / "distribution.json").read_bytes(), self.original_bytes)
        page_index.write_bytes(self.original_bytes)
        (self.output / "distribution.pre-cdn.json").write_bytes(b"another backup")
        with self.assertRaisesRegex(pack.PackError, "backup already exists"):
            self.reindex()
        self.assertEqual((self.output / "distribution.json").read_bytes(), self.original_bytes)


if __name__ == "__main__":
    unittest.main()

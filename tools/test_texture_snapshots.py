"""Real Git plumbing tests with tiny, synthetic PNGs; no network is used."""
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import struct
import subprocess
import tempfile
import unittest
import zlib


SPEC = importlib.util.spec_from_file_location("publish_texture_snapshots", Path(__file__).with_name("publish_texture_snapshots.py"))
publisher = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(publisher)


def png(red):
    def chunk(kind, data):
        return struct.pack(">I", len(data)) + kind + data + struct.pack(">I", zlib.crc32(kind + data))
    return publisher.PNG_SIGNATURE + chunk(b"IHDR", struct.pack(">IIBBBBB", 1, 1, 8, 2, 0, 0, 0)) + chunk(b"IDAT", zlib.compress(bytes([0, red, 0, 0]))) + chunk(b"IEND", b"")


class TextureSnapshotTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.root = Path(self.temp.name)
        self.source = self.root / "source" / "textures"
        self.source.mkdir(parents=True)
        self.distribution = self.root / "distribution.json"
        self.git_dir = self.root / "assets.git"
        self.mapping = self.root / "snapshots.json"
        self.paths = ["textures/ui/textures/aerial.wct.png", "textures/level one/a shared.wct.png", "textures/level two/a shared.wct.png"]
        self.records = []
        for i, name in enumerate(self.paths):
            data = png(i + 1)
            file = self.source.joinpath(*name.split("/")[1:])
            file.parent.mkdir(parents=True, exist_ok=True)
            file.write_bytes(data)
            self.records.append({"path": name, "size": len(data), "sha256": hashlib.sha256(data).hexdigest(), "width": 1, "height": 1})
        self.write_manifest()
        self.cap = len(png(1)) * 2

    def tearDown(self):
        self.temp.cleanup()

    def write_manifest(self):
        self.distribution.write_text(json.dumps({"format": 1, "name": "clarity-enhanced", "files": self.records}))

    def prepare(self, **kwargs):
        return publisher.prepare(self.source, self.distribution, self.git_dir, self.mapping, "20261011", max_bytes=self.cap, **kwargs)

    def git(self, *args):
        return subprocess.check_output(["git", "--git-dir", str(self.git_dir), *args])

    def test_orphans_obey_cap_and_preserve_exact_pngs_and_paths(self):
        before = {file: (file.read_bytes(), file.stat().st_mtime_ns) for file in self.source.rglob("*.png")}
        result = self.prepare()
        self.assertTrue(result["complete"])
        self.assertEqual(len(result["files"]), 3)
        self.assertEqual(result["snapshotCount"], 2)
        for snapshot in result["snapshots"]:
            self.assertLessEqual(snapshot["size"], self.cap)
            self.assertEqual(len(self.git("rev-list", "--parents", "-n", "1", snapshot["tag"]).split()), 1)
            names = self.git("ls-tree", "-r", "--name-only", snapshot["tag"]).decode().splitlines()
            expected = [record for record in result["files"] if record["tag"] == snapshot["tag"]]
            self.assertEqual(sorted(names), sorted(record["path"] for record in expected))
            for record in expected:
                data = self.git("show", snapshot["tag"] + ":" + record["path"])
                self.assertEqual(hashlib.sha256(data).hexdigest(), record["sha256"])
        self.assertEqual(self.git("ls-tree", "-r", "--name-only", "main").decode().splitlines(), ["README.md", "index.json"])
        for file, (data, mtime) in before.items():
            self.assertEqual(file.read_bytes(), data)
            self.assertEqual(file.stat().st_mtime_ns, mtime)
        encoded = next(record["url"] for record in result["files"] if "level one" in record["path"])
        self.assertIn("level%20one/a%20shared.wct.png", encoded)
        self.assertEqual(self.git("config", "pack.window").strip(), b"0")

    def test_first_snapshot_can_be_prepared_then_resumed_idempotently(self):
        first = self.prepare(limit_snapshots=1)
        self.assertFalse(first["complete"])
        self.assertEqual(first["files"][0]["path"], self.paths[0])
        self.assertEqual(len(first["snapshots"]), 1)
        final = self.prepare()
        self.assertTrue(final["complete"])
        self.assertEqual(final["snapshots"][0], first["snapshots"][0])
        main = self.git("rev-parse", "main")
        repeat = self.prepare()
        self.assertEqual(final, repeat)
        self.assertEqual(self.git("rev-parse", "main"), main)
        self.assertEqual(json.loads(self.mapping.read_text()), final)
        self.assertEqual(self.prepare(limit_snapshots=1), final)
        self.assertEqual(self.git("rev-parse", "main"), main)

    def test_same_size_checksum_change_is_rejected(self):
        target = self.source / "ui/textures/aerial.wct.png"
        data = bytearray(target.read_bytes())
        data[-1] ^= 1
        target.write_bytes(data)
        with self.assertRaisesRegex(ValueError, "checksum changed"):
            self.prepare()
        self.assertFalse((self.git_dir / "refs/heads/main").exists())

    def test_manifest_and_immutable_tag_cannot_be_replaced(self):
        result = self.prepare()
        refs_before = self.git("show-ref")
        self.distribution.write_text(self.distribution.read_text() + "\n")
        with self.assertRaisesRegex(ValueError, "changed"):
            self.prepare()
        self.assertEqual(self.git("show-ref"), refs_before)
        self.write_manifest()
        subprocess.run(["git", "--git-dir", str(self.git_dir), "update-ref", "refs/tags/" + result["snapshots"][0]["tag"], self.git("rev-parse", "main").decode().strip()], check=True)
        with self.assertRaisesRegex(ValueError, "Immutable snapshot tag"):
            self.prepare()

    def test_existing_unowned_directory_or_git_checkout_is_rejected(self):
        self.git_dir.mkdir()
        (self.git_dir / "user-file").write_text("preserve me")
        with self.assertRaisesRegex(ValueError, "unowned"):
            self.prepare()
        self.assertEqual((self.git_dir / "user-file").read_text(), "preserve me")
        checkout = self.root / "checkout"
        subprocess.run(["git", "init", "-q", str(checkout)], check=True)
        with self.assertRaisesRegex(ValueError, "outside existing Git"):
            publisher.prepare(self.source, self.distribution, checkout / "nested.git", self.mapping, "20261011", max_bytes=self.cap)
        with self.assertRaisesRegex(ValueError, "separate from the texture source"):
            publisher.prepare(self.source, self.distribution, self.source / "nested.git", self.mapping, "20261011", max_bytes=self.cap)

    def test_symlink_parent_cannot_hide_engine_checkout(self):
        checkout = self.root / "engine"
        subprocess.run(["git", "init", "-q", str(checkout)], check=True)
        assets = checkout / "assets"
        assets.mkdir()
        alias = self.root / "asset-alias"
        alias.symlink_to(assets, target_is_directory=True)
        with self.assertRaisesRegex(ValueError, "outside existing Git"):
            publisher.prepare(self.source, self.distribution, alias / "textures.git", self.mapping, "20261011", max_bytes=self.cap)
        self.assertFalse((assets / "textures.git").exists())

    def test_moved_owned_repository_still_cannot_be_inside_engine(self):
        self.prepare()
        checkout = self.root / "engine"
        subprocess.run(["git", "init", "-q", str(checkout)], check=True)
        moved = checkout / "textures.git"
        self.git_dir.rename(moved)
        with self.assertRaisesRegex(ValueError, "outside existing Git"):
            publisher.prepare(self.source, self.distribution, moved, self.mapping, "20261011", max_bytes=self.cap)

    def test_extra_files_traversal_and_symlinks_are_rejected(self):
        extra = self.source / "raw-original.md2"
        extra.write_bytes(b"must not publish")
        with self.assertRaisesRegex(ValueError, "Only textures"):
            self.prepare()
        extra.unlink()
        self.records[0]["path"] = "textures/../private.png"
        self.write_manifest()
        with self.assertRaisesRegex(ValueError, "normalized and relative"):
            self.prepare()
        self.records[0]["path"] = self.paths[0]
        self.write_manifest()
        target = self.source / "ui/textures/aerial.wct.png"
        original = self.root / "elsewhere.png"
        target.rename(original)
        target.symlink_to(original)
        with self.assertRaisesRegex(ValueError, "Symlink"):
            self.prepare()

    def test_limits_and_fake_png_are_rejected(self):
        with self.assertRaisesRegex(ValueError, "exceeds snapshot limit"):
            publisher.prepare(self.source, self.distribution, self.git_dir, self.mapping, "20261011", max_bytes=1)
        with self.assertRaisesRegex(ValueError, "Snapshot limit"):
            publisher.prepare(self.source, self.distribution, self.git_dir, self.mapping, "20261011", max_bytes=publisher.MAX_SNAPSHOT_BYTES + 1)
        target = self.source / "ui/textures/aerial.wct.png"
        target.write_bytes(b"not a real png" * 5)
        self.records[0].update(size=target.stat().st_size, sha256=hashlib.sha256(target.read_bytes()).hexdigest())
        self.write_manifest()
        with self.assertRaisesRegex(ValueError, "not a PNG"):
            self.prepare()

    def test_paths_are_data_and_inherited_git_environment_cannot_redirect(self):
        name = "textures/ui/$(touch INJECTION); ' odd.png"
        data = png(4)
        target = self.source.joinpath(*name.split("/")[1:])
        target.parent.mkdir(parents=True, exist_ok=True)
        target.write_bytes(data)
        self.records.append({"path": name, "size": len(data), "sha256": hashlib.sha256(data).hexdigest()})
        self.write_manifest()
        other = self.root / "other.git"
        subprocess.run(["git", "init", "-q", "--bare", str(other)], check=True)
        saved = {key: os.environ.get(key) for key in ("GIT_DIR", "GIT_INDEX_FILE")}
        try:
            os.environ["GIT_DIR"] = str(other)
            os.environ["GIT_INDEX_FILE"] = str(self.root / "unrelated-index")
            result = self.prepare()
        finally:
            for key, value in saved.items():
                if value is None:
                    os.environ.pop(key, None)
                else:
                    os.environ[key] = value
        record = next(record for record in result["files"] if record["path"] == name)
        self.assertEqual(self.git("show", record["tag"] + ":" + name), data)
        self.assertIn("%24%28touch%20INJECTION%29%3B%20%27%20odd.png", record["url"])
        self.assertFalse((self.root / "INJECTION").exists())
        self.assertFalse((self.root / "unrelated-index").exists())
        self.assertEqual(subprocess.check_output(["git", "--git-dir", str(other), "for-each-ref"]), b"")


if __name__ == "__main__":
    unittest.main()

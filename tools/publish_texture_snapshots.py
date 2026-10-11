#!/usr/bin/env python3
"""Prepare immutable, bounded GitHub texture snapshots without uploading them.

PNGs stay outside the engine repository. Each tag names a parentless commit
whose complete tree stays below jsDelivr's GitHub package limit. A bare Git
repository avoids a second checkout of the texture pack.
"""
import argparse
import hashlib
import json
import os
from pathlib import Path, PurePosixPath
import re
import struct
import subprocess
import tempfile
from datetime import datetime, timezone
from urllib.parse import quote


MAX_SNAPSHOT_BYTES = 45 * 1024 * 1024
OWNER_FILE = "opentpw-texture-snapshots.json"
TOOL_ID = "OpenTPW texture snapshot publisher v1"
PNG_SIGNATURE = b"\x89PNG\r\n\x1a\n"
DEFAULT_REPOSITORY = "meneerkrabs/OpenTPW-Texture-Packs"
DEFAULT_REPRESENTATIVE = "textures/ui/textures/aerial.wct.png"


def json_bytes(value):
    return (json.dumps(value, ensure_ascii=False, indent=2) + "\n").encode("utf-8")


def checked_path(value):
    if not isinstance(value, str) or not value or "\\" in value:
        raise ValueError(f"Invalid texture path: {value!r}")
    if any(ord(char) < 32 or ord(char) == 127 for char in value):
        raise ValueError(f"Control character in texture path: {value!r}")
    path = PurePosixPath(value)
    if path.is_absolute() or any(part in ("", ".", "..") for part in value.split("/")):
        raise ValueError(f"Texture path must be normalized and relative: {value!r}")
    if path.parts[0] != "textures" or path.suffix.lower() != ".png":
        raise ValueError(f"Only textures/*.png may enter a snapshot: {value!r}")
    return path


def load_inventory(source, distribution):
    raw = distribution.read_bytes()
    manifest = json.loads(raw)
    if manifest.get("format") != 1 or manifest.get("name") != "clarity-enhanced":
        raise ValueError("Expected a format-1 clarity-enhanced distribution manifest")
    inventory = {}
    for record in manifest.get("files", []):
        if record.get("path") == "pack.json":
            continue
        path = checked_path(record.get("path"))
        name = str(path)
        if name in inventory:
            raise ValueError(f"Duplicate manifest path: {name}")
        size, checksum = record.get("size"), record.get("sha256")
        if type(size) is not int or size <= 0 or not isinstance(checksum, str) or not re.fullmatch(r"[0-9a-f]{64}", checksum):
            raise ValueError(f"Invalid size or SHA-256 for {name}")
        local = source.joinpath(*path.parts[1:])
        for ancestor in [local, *local.parents]:
            if ancestor == source.parent:
                break
            if ancestor.is_symlink():
                raise ValueError(f"Symlink is not an owned texture: {name}")
        if not local.is_file() or local.stat().st_size != size:
            raise ValueError(f"Missing texture or changed size: {name}")
        inventory[name] = {"path": name, "size": size, "sha256": checksum, "local": local,
                           "width": record.get("width"), "height": record.get("height")}
    if not inventory:
        raise ValueError("Distribution manifest contains no PNG textures")
    actual = set()
    for local in source.rglob("*"):
        if local.is_symlink():
            raise ValueError(f"Symlink in texture source: {local}")
        if local.is_file():
            name = "textures/" + local.relative_to(source).as_posix()
            checked_path(name)
            actual.add(name)
    if actual != set(inventory):
        missing = sorted(set(inventory) - actual)[:3]
        extra = sorted(actual - set(inventory))[:3]
        raise ValueError(f"Source and distribution inventory differ (missing={missing}, extra={extra})")
    return inventory, hashlib.sha256(raw).hexdigest()


def partition(inventory, max_bytes, representative):
    if type(max_bytes) is not int or not 0 < max_bytes <= MAX_SNAPSHOT_BYTES:
        raise ValueError(f"Snapshot limit must be between 1 and {MAX_SNAPSHOT_BYTES} bytes")
    if representative not in inventory:
        raise ValueError(f"Representative PNG is not in the verified inventory: {representative}")
    ordered = [inventory[representative]] + [inventory[name] for name in sorted(inventory) if name != representative]
    snapshots, current, size = [], [], 0
    for record in ordered:
        if record["size"] > max_bytes:
            raise ValueError(f"PNG exceeds snapshot limit: {record['path']}")
        if current and size + record["size"] > max_bytes:
            snapshots.append(current)
            current, size = [], 0
        current.append(record)
        size += record["size"]
    snapshots.append(current)
    return snapshots


class SnapshotRepository:
    def __init__(self, path, ownership):
        self.path = path
        self.env = os.environ.copy()
        # A caller's index, worktree, configuration, or hooks must not redirect
        # these plumbing commands into the engine's checkout.
        for key in list(self.env):
            if key.startswith("GIT_"):
                del self.env[key]
        self.env.update(GIT_CONFIG_NOSYSTEM="1", GIT_CONFIG_GLOBAL=os.devnull,
                        GIT_AUTHOR_NAME="OpenTPW Asset Publisher", GIT_AUTHOR_EMAIL="assets@opentpw.io",
                        GIT_COMMITTER_NAME="OpenTPW Asset Publisher", GIT_COMMITTER_EMAIL="assets@opentpw.io")
        self.env["GIT_AUTHOR_DATE"] = self.env["GIT_COMMITTER_DATE"] = ownership["commitDate"]
        marker = path / OWNER_FILE
        if path.is_symlink():
            raise ValueError("Snapshot repository must not be a symlink")
        for ancestor in path.parents:
            if (ancestor / ".git").exists() or (ancestor / OWNER_FILE).exists():
                raise ValueError("Snapshot repository must be outside existing Git repositories")
        if path.exists():
            if not marker.is_file() or json.loads(marker.read_text()) != ownership:
                raise ValueError("Refusing an unowned repository or changed publication manifest/options")
            if self.git("rev-parse", "--is-bare-repository").strip() != "true":
                raise ValueError("Only the publisher's bare repository may be reused")
        else:
            path.parent.mkdir(parents=True, exist_ok=True)
            subprocess.run(["git", "init", "--bare", "--object-format=sha1", "--initial-branch=main", str(path)],
                           env=self.env, check=True, capture_output=True)
            marker.write_bytes(json_bytes(ownership))
        self.git("config", "pack.window", "0")
        self.git("config", "pack.depth", "0")
        self.git("config", "gc.auto", "0")
        self.git("config", "gc.autoPackLimit", "0")
        self.git("config", "core.looseCompression", "0")
        prefix = re.escape(ownership["snapshotPrefix"])
        for name in self.git("for-each-ref", "--format=%(refname)").splitlines():
            if name != "refs/heads/main" and not re.fullmatch(rf"refs/tags/{prefix}-[0-9]{{3,}}", name):
                raise ValueError(f"Unexpected reference in publisher repository: {name}")
        current = self.ref("refs/heads/main")
        if current:
            names = self.git("ls-tree", "-r", "--name-only", current).splitlines()
            if sorted(names) != ["README.md", "index.json"]:
                raise ValueError("Publisher main must contain only README.md and index.json")

    def git(self, *args, data=None, env=None, allow_missing=False):
        result = subprocess.run(["git", "--git-dir", str(self.path), *args], input=data,
                                env=env or self.env, capture_output=True)
        if result.returncode:
            if allow_missing and result.returncode == 1:
                return None
            raise ValueError(f"git {args[0]} failed: {result.stderr.decode(errors='replace').strip()}")
        return result.stdout.decode("utf-8").strip()

    def ref(self, name):
        return self.git("rev-parse", "--verify", "--quiet", name, allow_missing=True)

    def blob(self, data):
        return self.git("hash-object", "-w", "--stdin", data=data)

    def tree(self, blobs):
        with tempfile.TemporaryDirectory(prefix="opentpw-texture-index-") as directory:
            env = dict(self.env, GIT_INDEX_FILE=str(Path(directory) / "index"))
            self.git("read-tree", "--empty", env=env)
            records = b"".join(f"100644 {oid}\t{name}\0".encode("utf-8") for name, oid in sorted(blobs.items()))
            self.git("update-index", "-z", "--index-info", data=records, env=env)
            return self.git("write-tree", env=env)

    def orphan(self, tag, tree, message):
        commit = self.git("commit-tree", tree, data=message.encode("utf-8"))
        ref = "refs/tags/" + tag
        current = self.ref(ref)
        if current and current != commit:
            raise ValueError(f"Immutable snapshot tag already identifies different content: {tag}")
        if not current:
            self.git("update-ref", ref, commit, "0" * 40)
        return commit

    def publish_index(self, mapping):
        example = mapping["files"][0]["url"]
        readme = ("# OpenTPW texture packs\n\n"
                  "Clarity-enhanced custom PNG textures are published as immutable snapshot tags.\n"
                  "Each snapshot contains at most 45 MiB; no snapshot inherits another snapshot.\n"
                  "The engine and original game installation remain separate.\n\n"
                  "`index.json` records every prepared PNG's SHA-256, size, snapshot commit, and CDN URL.\n"
                  f"A representative texture is available at {example}\n\n"
                  "Native ZIP packages and movie files are distributed separately.\n").encode("utf-8")
        tree = self.tree({"README.md": self.blob(readme), "index.json": self.blob(json_bytes(mapping))})
        current = self.ref("refs/heads/main")
        if current and self.git("rev-parse", current + "^{tree}") == tree:
            return current
        parents = ["-p", current] if current else []
        message = ("Keep texture lookup separate from large snapshots\n\n"
                   "The small index lets clients choose individual verified PNGs without a full checkout.\n\n"
                   "Constraint: Each jsDelivr GitHub snapshot has a package size limit\n"
                   "Confidence: high\nScope-risk: narrow\n"
                   "Directive: Keep main limited to the README and texture index\n")
        commit = self.git("commit-tree", tree, *parents, data=message.encode("utf-8"))
        self.git("update-ref", "refs/heads/main", commit, current or "0" * 40)
        return commit


def verified_bytes(record):
    data = record["local"].read_bytes()
    if len(data) != record["size"] or hashlib.sha256(data).hexdigest() != record["sha256"]:
        raise ValueError(f"Texture checksum changed: {record['path']}")
    if len(data) < 33 or data[:8] != PNG_SIGNATURE or data[8:16] != b"\x00\x00\x00\rIHDR":
        raise ValueError(f"Texture is not a PNG with an IHDR: {record['path']}")
    width, height = struct.unpack(">II", data[16:24])
    if not width or not height:
        raise ValueError(f"Invalid PNG dimensions: {record['path']}")
    for name, actual in (("width", width), ("height", height)):
        if record[name] is not None and record[name] != actual:
            raise ValueError(f"PNG {name} differs from distribution manifest: {record['path']}")
    return data


def prepare(source, distribution, git_dir, mapping_path, version, repository=DEFAULT_REPOSITORY,
            max_bytes=MAX_SNAPSHOT_BYTES, representative=DEFAULT_REPRESENTATIVE, limit_snapshots=None):
    if not re.fullmatch(r"[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+", repository) or any(part in (".", "..") for part in repository.split("/")):
        raise ValueError("Repository must be a GitHub owner/name")
    if not re.fullmatch(r"[0-9]{8}", version):
        raise ValueError("Version must use YYYYMMDD")
    date = datetime.strptime(version, "%Y%m%d").replace(hour=12, tzinfo=timezone.utc).isoformat()
    if limit_snapshots is not None and (type(limit_snapshots) is not int or limit_snapshots < 1):
        raise ValueError("Snapshot limit must be a positive integer")
    source, distribution = Path(source).resolve(), Path(distribution).resolve()
    git_dir, mapping_path = Path(git_dir).absolute(), Path(mapping_path).absolute()
    if not source.is_dir() or not distribution.is_file():
        raise ValueError("Texture source and distribution manifest must exist")
    if git_dir.is_symlink() or git_dir.resolve().is_relative_to(source):
        raise ValueError("Bare repository must be separate from the texture source")
    # Check the real ancestry: a symlink to checkout/assets otherwise hides
    # checkout/.git from lexical Path.parents and can import gigabytes there.
    git_dir = git_dir.resolve()
    inventory, manifest_hash = load_inventory(source, distribution)
    snapshots = partition(inventory, max_bytes, representative)
    prefix = "clarity-" + version
    ownership = {"tool": TOOL_ID, "format": 1, "repository": repository, "source": str(source),
                 "distributionSha256": manifest_hash, "snapshotPrefix": prefix,
                 "maxSnapshotBytes": max_bytes, "representative": representative, "commitDate": date}
    if mapping_path.is_symlink() or mapping_path.resolve().is_relative_to(source) or mapping_path.resolve() == distribution or mapping_path.resolve().is_relative_to(git_dir.resolve()):
        raise ValueError("Mapping output must be separate from source files and the bare repository")
    mapping_path = mapping_path.resolve()
    previously_prepared = 0
    if mapping_path.exists():
        previous = json.loads(mapping_path.read_text())
        if previous.get("publisher") != TOOL_ID or previous.get("distributionSha256") != manifest_hash or previous.get("repository") != repository or previous.get("snapshotPrefix") != prefix:
            raise ValueError("Refusing an unowned or changed mapping output")
        previously_prepared = len(previous.get("snapshots", []))
        if previously_prepared > len(snapshots):
            raise ValueError("Mapping output contains unexpected snapshots")
    repo = SnapshotRepository(git_dir, ownership)
    mapping = {"format": 1, "publisher": TOOL_ID, "repository": repository,
               "distributionSha256": manifest_hash, "snapshotPrefix": prefix,
               "maxSnapshotBytes": max_bytes, "totalFiles": len(inventory),
               "totalSize": sum(record["size"] for record in inventory.values()),
               "snapshotCount": len(snapshots), "complete": False, "snapshots": [], "files": []}
    count = max(previously_prepared, min(limit_snapshots or len(snapshots), len(snapshots)))
    for index, records in enumerate(snapshots[:count], start=1):
        tag = f"{prefix}-{index:03d}"
        blobs = {record["path"]: repo.blob(verified_bytes(record)) for record in records}
        tree = repo.tree(blobs)
        size = sum(record["size"] for record in records)
        message = ("Keep individual textures within the CDN package limit\n\n"
                   f"Snapshot {index}/{len(snapshots)} publishes {len(records)} PNGs ({size} bytes).\n"
                   "No parent is included because each requested GitHub snapshot must stay bounded.\n\n"
                   "Constraint: jsDelivr GitHub packages are limited to 50 MB\n"
                   "Rejected: One full texture snapshot | exceeds the CDN package limit\n"
                   "Confidence: high\nScope-risk: narrow\n"
                   "Directive: Keep snapshot tags immutable and parentless\n"
                   "Tested: Every PNG size and SHA-256 matches the distribution manifest\n")
        commit = repo.orphan(tag, tree, message)
        mapping["snapshots"].append({"tag": tag, "commit": commit, "files": len(records), "size": size})
        for record in records:
            path = record["path"]
            url = f"https://cdn.jsdelivr.net/gh/{repository}@{tag}/" + "/".join(quote(part, safe="") for part in PurePosixPath(path).parts)
            mapping["files"].append({"path": path, "url": url, "commit": commit, "tag": tag,
                                     "sha256": record["sha256"], "size": record["size"]})
        print(f"Prepared {tag}: {len(records)} PNGs, {size} bytes, {commit}", flush=True)
    mapping["complete"] = count == len(snapshots)
    repo.publish_index(mapping)
    mapping_path.parent.mkdir(parents=True, exist_ok=True)
    with tempfile.NamedTemporaryFile(prefix=".texture-snapshots-", dir=mapping_path.parent, delete=False) as handle:
        temporary = Path(handle.name)
        handle.write(json_bytes(mapping))
    os.replace(temporary, mapping_path)
    return mapping


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--source", required=True, type=Path, help="Local clarity-enhanced/textures directory")
    parser.add_argument("--distribution", required=True, type=Path, help="Verified distribution.json")
    parser.add_argument("--git-dir", required=True, type=Path, help="New or publisher-owned bare repository outside the engine checkout")
    parser.add_argument("--mapping", required=True, type=Path, help="Output PNG-to-snapshot URL manifest")
    parser.add_argument("--version", required=True, help="Immutable snapshot version, YYYYMMDD")
    parser.add_argument("--repository", default=DEFAULT_REPOSITORY)
    parser.add_argument("--max-bytes", type=int, default=MAX_SNAPSHOT_BYTES)
    parser.add_argument("--representative", default=DEFAULT_REPRESENTATIVE)
    parser.add_argument("--limit-snapshots", type=int, help="Prepare only the first N snapshots for a CDN smoke check")
    args = parser.parse_args()
    try:
        mapping = prepare(args.source, args.distribution, args.git_dir, args.mapping, args.version,
                          args.repository, args.max_bytes, args.representative, args.limit_snapshots)
    except (ValueError, OSError, subprocess.CalledProcessError) as error:
        parser.exit(1, f"Texture snapshot preparation failed: {error}\n")
    print(f"Mapping: {args.mapping}; {len(mapping['snapshots'])}/{mapping['snapshotCount']} snapshots prepared; no upload performed")


if __name__ == "__main__":
    main()

#!/usr/bin/env python3
"""Package a user-supplied texture pack without putting its assets in Git.

The ZIPs extract beside OpenTPW's executable. ``pages/`` is a separate,
versioned static download tree for the browser; movies stay in their ZIP.
Only Python's standard library is used. This tool never uploads anything.
"""
from __future__ import annotations

import argparse
from dataclasses import dataclass
import hashlib
import json
import math
import os
from pathlib import Path, PurePosixPath
import re
import shutil
import stat
import struct
import tempfile
from urllib.parse import quote, urlsplit
import zipfile
import zlib


MAX_ARCHIVE_BYTES = (2 ** 31) - 1  # GitHub release assets must be below 2 GiB.
MAX_PNG_DIMENSION = 8192
MAX_PAGES_FILE_BYTES = 25 * 1024 * 1024
SNAPSHOT_REPOSITORY = "meneerkrabs/OpenTPW-Texture-Packs"
BASE_GROUPS = frozenset(("esprites", "global", "lobby", "ui"))
WORLDS = ("fantasy", "hallow", "jungle", "space")
PARTS = ("base", *WORLDS, "movies")
PNG_SIGNATURE = b"\x89PNG\r\n\x1a\n"
PNG_END = b"\x00\x00\x00\x00IEND\xaeB`\x82"
BLOCK_SIZE = 1024 * 1024
WINDOWS_RESERVED = {"con", "prn", "aux", "nul"} | {
    f"{prefix}{number}" for prefix in ("com", "lpt") for number in range(1, 10)
}


class PackError(ValueError):
    """A source file cannot be safely included in the distribution."""


@dataclass(frozen=True)
class PackFile:
    path: str
    source: Path
    size: int
    sha256: str
    part: str
    width: int | None = None
    height: int | None = None

    def record(self) -> dict:
        record = {"path": self.path, "size": self.size, "sha256": self.sha256}
        if self.width is not None:
            record.update(width=self.width, height=self.height)
        return record


def safe_relative(path: str) -> PurePosixPath:
    """Accept paths that extract identically on Windows, macOS and Linux."""
    relative = PurePosixPath(path)
    if not path or not relative.parts or relative.is_absolute() or relative.as_posix() != path:
        raise PackError(f"Unsafe pack path: {path!r}")
    for component in relative.parts:
        if (component in (".", "..") or component.endswith((".", " "))
                or any(ord(char) < 32 or char in '\\<>:\"|?*' for char in component)
                or component.split(".", 1)[0].casefold() in WINDOWS_RESERVED):
            raise PackError(f"Unsafe pack path: {path!r}")
    return relative


def sha256_file(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for block in iter(lambda: stream.read(BLOCK_SIZE), b""):
            digest.update(block)
    return digest.hexdigest()


def png_dimensions(path: Path) -> tuple[int, int]:
    with path.open("rb") as stream:
        header = stream.read(33)
        if (len(header) != 33 or header[:8] != PNG_SIGNATURE
                or header[8:16] != b"\x00\x00\x00\rIHDR"):
            raise PackError(f"Invalid PNG header: {path.name}")
        if zlib.crc32(header[12:29]) != struct.unpack(">I", header[29:33])[0]:
            raise PackError(f"Invalid PNG IHDR checksum: {path.name}")
        width, height, depth, color, compression, filtering, interlace = struct.unpack(
            ">IIBBBBB", header[16:29]
        )
        valid_depths = {0: {1, 2, 4, 8, 16}, 2: {8, 16}, 3: {1, 2, 4, 8},
                        4: {8, 16}, 6: {8, 16}}
        if not (1 <= width <= MAX_PNG_DIMENSION and 1 <= height <= MAX_PNG_DIMENSION):
            raise PackError(f"PNG dimensions outside 1..{MAX_PNG_DIMENSION}: {path.name}")
        if (depth not in valid_depths.get(color, set()) or compression != 0
                or filtering != 0 or interlace not in (0, 1)):
            raise PackError(f"Invalid PNG IHDR fields: {path.name}")
        if path.stat().st_size < 57:
            raise PackError(f"Truncated PNG: {path.name}")
        stream.seek(-12, os.SEEK_END)
        if stream.read(12) != PNG_END:
            raise PackError(f"PNG is missing its terminal IEND: {path.name}")
    return width, height


def validate_mp4(path: Path) -> None:
    with path.open("rb") as stream:
        header = stream.read(32)
    if len(header) < 16 or header[4:8] != b"ftyp":
        raise PackError(f"MP4 is missing its initial ftyp box: {path.name}")
    box_size = struct.unpack(">I", header[:4])[0]
    if box_size < 16 or box_size > path.stat().st_size:
        raise PackError(f"Invalid MP4 ftyp size: {path.name}")
    if header[8:12] not in (b"isom", b"iso2", b"mp41", b"mp42", b"avc1", b"M4V "):
        raise PackError(f"Unsupported MP4 brand: {path.name}")


def _regular_file(path: Path) -> os.stat_result:
    info = path.lstat()
    if not stat.S_ISREG(info.st_mode):
        raise PackError(f"Only regular files may be packaged: {path}")
    return info


def _part(relative: PurePosixPath) -> str:
    components = relative.parts
    if relative.as_posix() == "pack.json":
        return "base"
    if components[0] == "movies" and len(components) == 2 and relative.suffix == ".mp4":
        return "movies"
    if components[0] == "textures" and len(components) >= 3 and relative.suffix == ".png":
        if components[1] in BASE_GROUPS:
            return "base"
        if components[1] == "levels" and len(components) >= 4 and components[2] in WORLDS:
            return components[2]
    raise PackError(f"Unexpected asset or texture group: {relative}")


def _source_paths(root: Path):
    yield root / "pack.json"
    for name in ("textures", "movies"):
        directory = root / name
        if directory.is_symlink():
            raise PackError(f"Symlink directory cannot be packaged: {directory}")
        if not directory.exists():
            if name == "textures":
                raise PackError("The pack has no textures directory")
            continue
        if not directory.is_dir():
            raise PackError(f"Expected a directory: {directory}")
        for base, directories, files in os.walk(directory, followlinks=False):
            directories.sort()
            for child in directories:
                child_path = Path(base) / child
                if child_path.is_symlink():
                    raise PackError(f"Symlink directory cannot be packaged: {child_path}")
            for name in sorted(files):
                if name != ".DS_Store":
                    yield Path(base) / name


def collect_files(source: Path) -> tuple[dict, list[PackFile]]:
    if source.is_symlink() or not source.is_dir():
        raise PackError("The source must be a local directory, not a symlink")
    manifest_path = source / "pack.json"
    if _regular_file(manifest_path).st_size > 1024 * 1024:
        raise PackError("pack.json exceeds 1 MiB")
    manifest_bytes = manifest_path.read_bytes()
    try:
        manifest = json.loads(manifest_bytes.decode("utf-8"), parse_float=_finite_float,
                              parse_constant=_invalid_constant)
    except (json.JSONDecodeError, UnicodeError) as error:
        raise PackError(f"Invalid pack.json: {error}") from error
    if (not isinstance(manifest, dict) or type(manifest.get("Format")) is not int
            or manifest["Format"] != 1):
        raise PackError("pack.json must declare Format: 1")

    files = []
    seen = set()
    for path in _source_paths(source):
        relative = safe_relative(path.relative_to(source).as_posix())
        key = str(relative).casefold()
        if key in seen:
            raise PackError(f"Case-insensitive duplicate asset path: {relative}")
        seen.add(key)
        part = _part(relative)
        before = _regular_file(path)
        width = height = None
        if relative.suffix == ".png":
            if before.st_size >= MAX_PAGES_FILE_BYTES:
                raise PackError(f"PNG exceeds the static host's 25 MiB limit: {relative}")
            width, height = png_dimensions(path)
        elif relative.suffix == ".mp4":
            validate_mp4(path)
        digest = sha256_file(path)
        after = _regular_file(path)
        if (before.st_dev, before.st_ino, before.st_size, before.st_mtime_ns) != (
                after.st_dev, after.st_ino, after.st_size, after.st_mtime_ns):
            raise PackError(f"Source changed while hashing: {relative}")
        files.append(PackFile(str(relative), path, before.st_size, digest, part, width, height))
    texture_count = sum(file.width is not None for file in files)
    if next(file.sha256 for file in files if file.path == "pack.json") != hashlib.sha256(manifest_bytes).hexdigest():
        raise PackError("pack.json changed after its metadata was read")
    if not texture_count:
        raise PackError("The pack contains no PNG textures")
    for field, actual in (("Textures", texture_count), ("InterfaceTextures", sum(
            file.path.startswith("textures/ui/") for file in files))):
        if field in manifest and (type(manifest[field]) is not int or manifest[field] != actual):
            raise PackError(f"pack.json {field} says {manifest[field]!r}, but found {actual}")
    return manifest, sorted(files, key=lambda file: file.path)


def _invalid_constant(value: str):
    raise PackError(f"Non-finite JSON value in pack.json: {value}")


def _finite_float(value: str) -> float:
    number = float(value)
    if not math.isfinite(number):
        _invalid_constant(value)
    return number


def _write_zip(path: Path, files: list[PackFile], prefix: str, maximum: int) -> None:
    # Stored PNG/MP4 bytes are already compressed. Check overhead before writing
    # so a changed group cannot unexpectedly produce an oversized release asset.
    estimated = 22 + sum(file.size + 128 + 2 * len((prefix + file.path).encode("utf-8"))
                         for file in files)
    if estimated > maximum:
        raise PackError(f"{path.name} would exceed the archive size limit")
    with zipfile.ZipFile(path, "w", compression=zipfile.ZIP_STORED) as archive:
        for file in files:
            entry = zipfile.ZipInfo(prefix + file.path, date_time=(2026, 1, 1, 0, 0, 0))
            entry.create_system = 3
            entry.external_attr = (stat.S_IFREG | 0o644) << 16
            digest = hashlib.sha256()
            size = 0
            with file.source.open("rb") as source, archive.open(entry, "w") as target:
                for block in iter(lambda: source.read(BLOCK_SIZE), b""):
                    digest.update(block)
                    size += len(block)
                    target.write(block)
            if size != file.size or digest.hexdigest() != file.sha256:
                raise PackError(f"Source changed while archiving: {file.path}")
    if path.stat().st_size > maximum:
        raise PackError(f"{path.name} exceeds the archive size limit")


def _copy_verified(file: PackFile, destination: Path) -> None:
    destination.parent.mkdir(parents=True, exist_ok=True)
    digest = hashlib.sha256()
    size = 0
    with file.source.open("rb") as source, destination.open("xb") as target:
        for block in iter(lambda: source.read(BLOCK_SIZE), b""):
            digest.update(block)
            size += len(block)
            target.write(block)
    if size != file.size or digest.hexdigest() != file.sha256:
        raise PackError(f"Source changed while preparing the static site: {file.path}")


def package(source: Path, output: Path, *, name: str = "clarity-enhanced",
            version: str = "20261011", repository: str = "meneerkrabs/OpenTPW",
            texture_base_url: str | None = None,
            max_archive_bytes: int = MAX_ARCHIVE_BYTES) -> dict:
    if not re.fullmatch(r"[a-z0-9][a-z0-9_-]*", name):
        raise PackError("The pack name must be a plain lowercase directory name")
    if not re.fullmatch(r"[A-Za-z0-9][A-Za-z0-9._-]*", version):
        raise PackError("The version must be a plain path component")
    if not re.fullmatch(r"[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+", repository):
        raise PackError("Expected a GitHub owner/repository")
    if not 1 <= max_archive_bytes <= MAX_ARCHIVE_BYTES:
        raise PackError("The archive limit must remain below 2 GiB")
    if source.is_symlink() or output.is_symlink():
        raise PackError("Source and output must not be symlinks")
    source = source.resolve()
    output = output.resolve()
    if output == source or source in output.parents or output in source.parents:
        raise PackError("The output must be outside the source directory")
    if output.exists() or output.is_symlink():
        raise PackError(f"Output already exists: {output}; choose a new output directory")
    if any((parent / ".git").exists() for parent in (output, *output.parents)):
        raise PackError("Asset output must stay outside a Git working tree")
    texture_base_url = texture_base_url or (
        f"https://opentpw-textures.pages.dev/{name}/{version}/"
    )
    url = urlsplit(texture_base_url)
    if (url.scheme != "https" or not url.netloc or url.username or url.password
            or url.query or url.fragment or not url.path.endswith("/")):
        raise PackError("The texture base URL must be an absolute HTTPS URL ending in /")

    manifest, files = collect_files(source)
    prefix = f"content/texture-packs/{name}/"
    tag = f"texture-pack-{name}-{version}"
    output.parent.mkdir(parents=True, exist_ok=True)
    staging = Path(tempfile.mkdtemp(prefix=f".{output.name}-", dir=output.parent))
    try:
        archives = []
        for part in PARTS:
            members = [file for file in files if file.part == part]
            if not members:
                continue
            archive_name = f"OpenTPW-{name}-{part}.zip"
            archive = staging / archive_name
            _write_zip(archive, members, prefix, max_archive_bytes)
            archives.append({"name": archive_name,
                             "url": f"https://github.com/{repository}/releases/download/{quote(tag)}/{archive_name}",
                             "size": archive.stat().st_size, "sha256": sha256_file(archive),
                             "kind": "movies" if part == "movies" else "textures"})

        distribution = {
            "format": 1, "name": name, "version": version,
            "packManifest": manifest, "archivePrefix": prefix,
            "textureBaseUrl": texture_base_url,
            "files": [file.record() for file in files if file.part != "movies"],
            "movies": [file.record() for file in files if file.part == "movies"],
            "archives": archives,
        }
        index_path = staging / "distribution.json"
        index_path.write_text(json.dumps(distribution, ensure_ascii=False, indent=2, allow_nan=False)
                              + "\n", encoding="utf-8")
        page_root = staging / "pages" / name / version
        page_root.mkdir(parents=True)
        for file in files:
            if file.part != "movies":
                _copy_verified(file, page_root / file.path)
        shutil.copyfile(index_path, page_root / "distribution.json")
        index_hash = sha256_file(index_path)
        (staging / "distribution.sha256").write_text(index_hash + "  distribution.json\n",
                                                     encoding="ascii")
        checksums = [f"{entry['sha256']}  {entry['name']}" for entry in archives]
        checksums.append(f"{index_hash}  distribution.json")
        (staging / "SHA256SUMS.txt").write_text("\n".join(checksums) + "\n", encoding="ascii")
        os.rename(staging, output)
        return distribution
    except BaseException:
        shutil.rmtree(staging)
        raise


def _atomic_bytes(path: Path, data: bytes) -> None:
    with tempfile.NamedTemporaryFile(dir=path.parent, prefix=f".{path.name}-", delete=False) as stream:
        temporary = Path(stream.name)
        try:
            stream.write(data)
            stream.flush()
            os.fsync(stream.fileno())
        except BaseException:
            temporary.unlink()
            raise
    try:
        os.replace(temporary, path)
    finally:
        temporary.unlink(missing_ok=True)


def reindex_snapshots(output: Path, snapshot_map: Path) -> dict:
    """Bind each PNG to a verified, immutable tag URL; leave media untouched."""
    output = output.resolve()
    index_path = output / "distribution.json"
    _regular_file(index_path)
    original_bytes = index_path.read_bytes()
    original_hash = hashlib.sha256(original_bytes).hexdigest()
    try:
        index = json.loads(original_bytes, parse_float=_finite_float, parse_constant=_invalid_constant)
        mapping = json.loads(snapshot_map.read_bytes(), parse_float=_finite_float,
                             parse_constant=_invalid_constant)
    except (json.JSONDecodeError, UnicodeError) as error:
        raise PackError(f"Invalid distribution or snapshot JSON: {error}") from error
    if (not isinstance(index, dict) or type(index.get("format")) is not int
            or index["format"] != 1 or index.get("name") != "clarity-enhanced"
            or not re.fullmatch(r"[0-9]{8}", str(index.get("version", "")))):
        raise PackError("Snapshot reindexing requires a versioned clarity-enhanced distribution")
    prefix = f"clarity-{index['version']}"
    if (not isinstance(mapping, dict) or type(mapping.get("format")) is not int
            or mapping["format"] != 1 or mapping.get("complete") is not True
            or mapping.get("repository") != SNAPSHOT_REPOSITORY
            or mapping.get("snapshotPrefix") != prefix
            or mapping.get("distributionSha256") != original_hash):
        raise PackError("Snapshot mapping must be complete and bound to this distribution and repository")
    snapshots = mapping.get("snapshots")
    mapped_files = mapping.get("files")
    records = index.get("files")
    if not all(isinstance(value, list) for value in (snapshots, mapped_files, records)):
        raise PackError("Snapshot and distribution file lists are required")
    if (type(mapping.get("snapshotCount")) is not int or mapping["snapshotCount"] != len(snapshots)
            or not snapshots or len(snapshots) > 999
            or type(mapping.get("maxSnapshotBytes")) is not int or mapping["maxSnapshotBytes"] <= 0):
        raise PackError("Invalid snapshot count or size limit")
    snapshot_by_tag = {}
    for snapshot in snapshots:
        if (not isinstance(snapshot, dict) or not isinstance(snapshot.get("tag"), str)
                or not re.fullmatch(re.escape(prefix) + r"-[0-9]{3}", snapshot["tag"])
                or not isinstance(snapshot.get("commit"), str)
                or not re.fullmatch(r"[0-9a-f]{40}", snapshot["commit"])
                or type(snapshot.get("files")) is not int or snapshot["files"] <= 0
                or type(snapshot.get("size")) is not int
                or not 0 < snapshot["size"] <= mapping["maxSnapshotBytes"]
                or snapshot["tag"] in snapshot_by_tag):
            raise PackError("Invalid or duplicate snapshot metadata")
        snapshot_by_tag[snapshot["tag"]] = snapshot
    if set(snapshot_by_tag) != {f"{prefix}-{number:03d}" for number in range(1, len(snapshots) + 1)}:
        raise PackError("Snapshot tags must form a complete sequence")

    png_by_path = {}
    seen = set()
    for record in records:
        if not isinstance(record, dict) or not isinstance(record.get("path"), str):
            raise PackError("Invalid distribution file record")
        path = str(safe_relative(record["path"]))
        if _part(PurePosixPath(path)) == "movies" or path.casefold() in seen:
            raise PackError("Invalid or duplicate distribution file path")
        seen.add(path.casefold())
        if path == "pack.json":
            if "url" in record:
                raise PackError("pack.json must keep its existing base URL")
        else:
            png_by_path[path] = record
    if "pack.json" not in seen or not png_by_path:
        raise PackError("The distribution must include pack.json and PNG files")

    urls = {}
    totals = {tag: [0, 0] for tag in snapshot_by_tag}
    for record in mapped_files:
        if not isinstance(record, dict) or not isinstance(record.get("path"), str):
            raise PackError("Invalid mapped PNG record")
        path = str(safe_relative(record["path"]))
        original = png_by_path.get(path)
        snapshot = snapshot_by_tag.get(record.get("tag"))
        if (original is None or path in urls or snapshot is None
                or type(record.get("size")) is not int
                or record["size"] != original.get("size")
                or record.get("sha256") != original.get("sha256")
                or record.get("commit") != snapshot["commit"]):
            raise PackError(f"Mapped PNG does not match the distribution: {path}")
        expected_url = (f"https://cdn.jsdelivr.net/gh/{SNAPSHOT_REPOSITORY}"
                        f"@{snapshot['tag']}/{quote(path, safe='/')}")
        if record.get("url") != expected_url:
            raise PackError(f"Untrusted snapshot URL: {path}")
        urls[path] = expected_url
        totals[snapshot["tag"]][0] += 1
        totals[snapshot["tag"]][1] += record["size"]
    if (set(urls) != set(png_by_path) or type(mapping.get("totalFiles")) is not int
            or mapping["totalFiles"] != len(png_by_path)
            or type(mapping.get("totalSize")) is not int
            or mapping["totalSize"] != sum(record["size"] for record in mapped_files)
            or any(totals[tag] != [snapshot["files"], snapshot["size"]]
                   for tag, snapshot in snapshot_by_tag.items())):
        raise PackError("Snapshot mapping does not cover the exact PNG corpus and sizes")

    page_root = output / "pages" / index["name"] / index["version"]
    page_index = page_root / "distribution.json"
    _regular_file(page_index)
    if page_index.read_bytes() != original_bytes:
        raise PackError("Static and distribution indexes differ before reindexing")
    # Hash the existing bytes, not only the two metadata assertions. All checks
    # finish before the first index/checksum write, and ZIPs are never opened.
    for record in records:
        path = page_root / record["path"]
        if (_regular_file(path).st_size != record.get("size")
                or sha256_file(path) != record.get("sha256")):
            raise PackError(f"Static file no longer matches its distribution hash: {record['path']}")
    backup = output / "distribution.pre-cdn.json"
    if backup.exists() and backup.read_bytes() != original_bytes:
        raise PackError("An earlier pre-CDN index backup already exists")
    for path, url in urls.items():
        png_by_path[path]["url"] = url
    new_bytes = (json.dumps(index, ensure_ascii=False, indent=2, allow_nan=False) + "\n").encode("utf-8")
    new_hash = hashlib.sha256(new_bytes).hexdigest()
    checksums = [f"{entry['sha256']}  {entry['name']}" for entry in index["archives"]]
    checksums.append(f"{new_hash}  distribution.json")
    if not backup.exists():
        _atomic_bytes(backup, original_bytes)
    _atomic_bytes(index_path, new_bytes)
    _atomic_bytes(page_index, new_bytes)
    _atomic_bytes(output / "distribution.sha256", f"{new_hash}  distribution.json\n".encode("ascii"))
    _atomic_bytes(output / "SHA256SUMS.txt", ("\n".join(checksums) + "\n").encode("ascii"))
    return index


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--source", type=Path)
    parser.add_argument("--output", required=True, type=Path)
    parser.add_argument("--snapshot-map", type=Path,
                        help="Add verified PNG CDN URLs to an existing output, preserving archives")
    parser.add_argument("--name", default="clarity-enhanced")
    parser.add_argument("--version", default="20261011")
    parser.add_argument("--repository", default="meneerkrabs/OpenTPW")
    parser.add_argument("--texture-base-url")
    args = parser.parse_args()
    if bool(args.source) == bool(args.snapshot_map):
        parser.error("Choose --source to package, or --snapshot-map to reindex an existing output")
    try:
        if args.snapshot_map:
            distribution = reindex_snapshots(args.output, args.snapshot_map)
        else:
            distribution = package(args.source, args.output, name=args.name, version=args.version,
                                   repository=args.repository, texture_base_url=args.texture_base_url)
    except (PackError, OSError) as error:
        parser.exit(1, f"Texture pack packaging failed: {error}\n")
    if args.snapshot_map:
        print(f"Updated {len(distribution['files']) - 1} PNG URLs; archives and movies unchanged")
    else:
        print(f"Packaged {len(distribution['files']) - 1} PNGs and {len(distribution['movies'])} movies")
    for archive in distribution["archives"]:
        print(f"{archive['name']}: {archive['size'] / 1024 / 1024:.2f} MiB, SHA256 {archive['sha256']}")
    print(f"Distribution index SHA256: {sha256_file(args.output / 'distribution.json')}")
    print(f"Static site: {args.output / 'pages'}")
    movie_claim = distribution["packManifest"].get("Movies")
    if movie_claim is not None and movie_claim != len(distribution["movies"]):
        print(f"Note: original Movies metadata says {movie_claim}; packaged {len(distribution['movies'])} actual MP4s")


if __name__ == "__main__":
    main()

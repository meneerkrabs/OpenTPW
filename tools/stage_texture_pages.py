"""Prepare a static PNG CDN from the checksum-pinned release descriptor."""
from __future__ import annotations

import argparse
import hashlib
import json
from pathlib import Path, PurePosixPath
import re
import shutil
import stat
import tempfile
from urllib.request import Request, urlopen
import zipfile


def digest(path: Path) -> str:
    value = hashlib.sha256()
    with path.open("rb") as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            value.update(block)
    return value.hexdigest()


def safe_path(value: str) -> str:
    if not isinstance(value, str):
        raise ValueError("Invalid asset path")
    path = PurePosixPath(value)
    if (not value or not path.parts or path.is_absolute() or str(path) != value
            or any(p in {".", ".."} or p.endswith((".", " "))
                   or any(ord(c) < 32 or c in '\\<>:\"|?*' for c in p) for p in path.parts)):
        raise ValueError("Invalid asset path")
    return value


def stage(index: Path, checksum: Path, output: Path, cache: Path) -> None:
    raw = index.read_bytes()
    if len(raw) > 16 * 1024 * 1024 or hashlib.sha256(raw).hexdigest() != checksum.read_text().split()[0]:
        raise ValueError("Descriptor checksum mismatch")
    data = json.loads(raw)
    if type(data["format"]) is not int or data["format"] != 1 or data["name"] != "clarity-enhanced" or data["version"] != "20261011":
        raise ValueError("Unexpected texture distribution")
    expected = {safe_path(f["path"]): f for f in data["files"]}
    if len({path.casefold() for path in expected}) != len(data["files"]):
        raise ValueError("Duplicate asset path")
    if "pack.json" not in expected or len(expected) < 2:
        raise ValueError("The distribution must contain pack.json and PNGs")
    for path, record in expected.items():
        if (path != "pack.json" and not (path.startswith("textures/") and path.endswith(".png"))
                or type(record["size"]) is not int or not 0 < record["size"] <= 25 * 1024 * 1024
                or not isinstance(record["sha256"], str) or not re.fullmatch(r"[0-9a-f]{64}", record["sha256"])):
            raise ValueError("Invalid static asset record")
    prefix = "content/texture-packs/clarity-enhanced/"
    if data.get("archivePrefix") != prefix:
        raise ValueError("Unexpected archive prefix")
    if output.is_symlink() or cache.is_symlink():
        raise ValueError("Output and cache must not be symlinks")
    if output.exists() and (not output.is_dir() or any(output.iterdir())):
        raise ValueError("Output must be empty")
    cache.mkdir(parents=True, exist_ok=True)
    output.parent.mkdir(parents=True, exist_ok=True)
    with tempfile.TemporaryDirectory(dir=output.parent, prefix="texture-cdn-") as folder:
        publication = Path(folder) / "site"
        target = publication / "clarity-enhanced" / data["version"]
        target.mkdir(parents=True)
        extracted = set()
        archive_names = set()
        for archive in data["archives"]:
            if archive["kind"] != "textures":
                continue
            name = safe_path(archive["name"])
            if ("/" in name or not name.endswith(".zip") or type(archive["size"]) is not int
                    or not 0 < archive["size"] < 2**31 or name in archive_names
                    or not isinstance(archive["sha256"], str) or not re.fullmatch(r"[0-9a-f]{64}", archive["sha256"])):
                raise ValueError("Invalid texture archive")
            archive_names.add(name)
            url = archive["url"]
            base = "https://github.com/meneerkrabs/OpenTPW/releases/download/texture-pack-clarity-enhanced-20261011/"
            if url != base + name:
                raise ValueError("Unexpected archive host or release")
            zip_path = cache / name
            if zip_path.is_symlink() or zip_path.exists() and not zip_path.is_file():
                raise ValueError("Archive cache entries must be regular files")
            if not zip_path.exists() or zip_path.stat().st_size != archive["size"] or digest(zip_path) != archive["sha256"]:
                with tempfile.NamedTemporaryFile(dir=cache, suffix=".partial", delete=False) as stream:
                    temporary = Path(stream.name)
                try:
                    with urlopen(Request(url, headers={"User-Agent": "OpenTPW texture publication"}), timeout=120) as response, temporary.open("wb") as stream:
                        size = 0
                        while block := response.read(1024 * 1024):
                            size += len(block)
                            if size > archive["size"]:
                                raise ValueError("Oversized archive")
                            stream.write(block)
                    if size != archive["size"] or digest(temporary) != archive["sha256"]:
                        raise ValueError("Archive checksum mismatch")
                    temporary.replace(zip_path)
                finally:
                    temporary.unlink(missing_ok=True)
            with zipfile.ZipFile(zip_path) as source:
                for member in source.infolist():
                    mode = member.external_attr >> 16
                    if member.is_dir() or stat.S_IFMT(mode) not in {0, stat.S_IFREG} or not member.filename.startswith(prefix):
                        raise ValueError("Unexpected archive member")
                    path = safe_path(member.filename[len(prefix):])
                    if path not in expected or path in extracted:
                        raise ValueError("Unexpected or duplicate asset")
                    record = expected[path]
                    if member.file_size != record["size"]:
                        raise ValueError("Asset size mismatch")
                    destination = target / path
                    destination.parent.mkdir(parents=True, exist_ok=True)
                    with source.open(member) as incoming, destination.open("xb") as outgoing:
                        shutil.copyfileobj(incoming, outgoing, 1024 * 1024)
                    if digest(destination) != record["sha256"]:
                        raise ValueError("Asset checksum mismatch")
                    if destination.stat().st_size > 25 * 1024 * 1024:
                        raise ValueError("Asset exceeds Pages limit")
                    extracted.add(path)
            print(f"Prepared {name}", flush=True)
        if extracted != set(expected):
            raise ValueError("Incomplete texture CDN")
        (target / "distribution.json").write_bytes(raw)
        (target / "distribution.sha256").write_text(checksum.read_text())
        (publication / "_headers").write_text("/*\n  Access-Control-Allow-Origin: *\n  Cache-Control: public, max-age=31536000, immutable\n  X-Content-Type-Options: nosniff\n")
        (publication / "404.html").write_text("<!doctype html><title>Texture not found</title><p>Texture not found.</p>")
        if output.exists():
            output.rmdir()
        publication.replace(output)
    print(f"Texture CDN: {len(extracted) - 1} PNGs and pinned metadata verified")


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--index", type=Path, required=True)
    parser.add_argument("--checksum", type=Path, required=True)
    parser.add_argument("--out", type=Path, required=True)
    parser.add_argument("--cache", type=Path, required=True)
    args = parser.parse_args()
    stage(args.index, args.checksum, args.out, args.cache)


if __name__ == "__main__":
    main()

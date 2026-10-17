#!/usr/bin/env python3
"""Writes or checks content/pools/esprites.manifest.json: the pinned list (path, size, SHA-256) of every file below
content/pools/esprites (docs/POOLS.md, "Browser delivery").

The manifest is embedded in the browser build (the sprites themselves are not). The page fetches the sprite files on demand and the game accepts a
file only when its path, size and SHA-256 are in this list. Run it after tools/pools/build-content.sh changes the sprite sets:

    python3 -I tools/pools/sprite_manifest.py --write
    python3 -I tools/pools/sprite_manifest.py --check

PoolSpriteDeliveryTests fails when the committed manifest and the shipped files disagree.
"""
import hashlib
import json
import sys
from pathlib import Path

POOLS = Path(__file__).resolve().parents[2] / "content" / "pools"
MANIFEST = POOLS / "esprites.manifest.json"


def build() -> str:
	files = []
	root = POOLS / "esprites"
	for path in sorted((p for p in root.rglob("*") if p.is_file()), key=lambda p: p.relative_to(root).as_posix()):
		data = path.read_bytes()
		files.append({"path": path.relative_to(root).as_posix(), "size": len(data), "sha256": hashlib.sha256(data).hexdigest()})
	return json.dumps({"format": 1, "files": files}, indent="\t") + "\n"


def main(argv) -> int:
	if argv[1:] not in (["--write"], ["--check"]):
		print(__doc__)
		return 2
	text = build()
	if argv[1] == "--write":
		MANIFEST.write_text(text, newline="\n")
		print(f"wrote {MANIFEST} ({text.count(chr(34) + 'path' + chr(34))} files)")
		return 0
	if not MANIFEST.exists() or MANIFEST.read_text() != text:
		print("content/pools/esprites.manifest.json is out of date: run tools/pools/sprite_manifest.py --write", file=sys.stderr)
		return 1
	return 0


sys.exit(main(sys.argv))

"""Deterministic PEF metadata inventory; no binary contents or disassembly."""
from __future__ import annotations

import argparse
import hashlib
import json
from pathlib import Path

import demangle
import pef


def inventory(path: Path, pattern: str = "") -> dict:
    raw = path.read_bytes()
    container = pef.PEFContainer(raw, path.name)
    result = {
        "file": path.name,
        "sha256": hashlib.sha256(raw).hexdigest(),
        "size": len(raw),
        "sections": [{"index": s.index, "kind": s.kind_name,
                      "total_size": s.total_size, "unpacked_size": s.unpacked_size,
                      "packed_size": s.packed_size} for s in container.sections],
        "entries": {"main": container.main, "init": container.init, "term": container.term},
        "imports": len(container.imports),
        "exports": len(container.exports),
        "libraries": [{"name": lib.name, "imports": lib.symbol_count}
                      for lib in container.libraries],
        "relocations": {str(section): len(words) for section, words in sorted(container.relocs.items())},
    }
    if pattern:
        matches = []
        for direction, symbols in (("import", container.imports), ("export", container.exports)):
            for symbol in symbols:
                display = demangle.demangle(symbol.name)
                if pattern.casefold() not in display.casefold():
                    continue
                match = {"direction": direction, "name": symbol.name, "demangled": display,
                         "class": pef.SYMBOL_CLASSES.get(symbol.sym_class, str(symbol.sym_class))}
                if direction == "import":
                    match["library"] = symbol.library
                else:
                    match["section"] = symbol.section
                    match["offset"] = symbol.value
                matches.append(match)
        result["matching_symbols"] = matches
    return result


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("files", nargs="+", type=Path)
    parser.add_argument("--symbols", default="", help="case-insensitive demangled name substring")
    args = parser.parse_args()
    try:
        results = [inventory(path, args.symbols) for path in sorted(args.files)]
    except (OSError, pef.PEFError) as error:
        parser.exit(1, f"inventory: {error}\n")
    print(json.dumps(results, indent=2, sort_keys=True))


if __name__ == "__main__":
    main()

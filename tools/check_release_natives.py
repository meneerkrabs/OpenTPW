#!/usr/bin/env python3
"""Fails a release whose published Windows folder ships the wrong SDL2.dll.

The win-x64 and win-x86 packages once shipped Veldrid.SDL2's SDL 2.0.8, whose WASAPI
output drops audio on current Windows (issue #754). Every Windows release must hold
exactly one SDL2.dll, next to the executable, built for the runtime's CPU, with a
FileVersion no older than the SDL2 source pin in scripts/build-native.py.

    python3 tools/check_release_natives.py --rid win-x64 publish/OpenTPW-win-x64

Other runtimes have no bundled-SDL2 check: macOS arm64 ships the library built from
the same pin, and Linux uses the system libSDL2 (docs/RELEASES.md).
"""

from __future__ import annotations

import argparse
import importlib.util
import pathlib
import sys

ROOT = pathlib.Path(__file__).resolve().parents[1]


def load_build_native():
    spec = importlib.util.spec_from_file_location("build_native", ROOT / "scripts" / "build-native.py")
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


BUILD_NATIVE = load_build_native()


def check(folder: pathlib.Path, rid: str) -> list[str]:
    """Problems with the folder's SDL2 for this runtime; an empty list means it passes."""
    system, arch = BUILD_NATIVE.RIDS[rid]
    if system != "windows":
        return []
    copies = sorted(path for path in folder.rglob("*") if path.is_file() and path.name.lower() == "sdl2.dll")
    if len(copies) != 1:
        return [f"expected exactly one SDL2.dll, found {len(copies)}: {[str(path.relative_to(folder)) for path in copies]}"]
    library = copies[0]
    problems = []
    if library.parent != folder:
        problems.append(f"SDL2.dll is at {library.relative_to(folder)}, not next to the executable")
    actual = BUILD_NATIVE.binary_arch(library)
    if actual != arch:
        problems.append(f"SDL2.dll is {actual}, expected {arch}")
    pin = BUILD_NATIVE.pinned_version("sdl2")
    version = BUILD_NATIVE.pe_file_version(library.read_bytes())
    if version is None:
        problems.append("SDL2.dll has no FileVersion resource")
    elif version[:3] < pin:
        problems.append(f"SDL2.dll is {'.'.join(map(str, version))}, older than the pinned {'.'.join(map(str, pin))}")
    return problems


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--rid", required=True, choices=sorted(BUILD_NATIVE.RIDS))
    parser.add_argument("folder", type=pathlib.Path, help="the published release folder")
    arguments = parser.parse_args()
    if not arguments.folder.is_dir():
        print(f"No published folder at {arguments.folder}", file=sys.stderr)
        return 1
    problems = check(arguments.folder.resolve(), arguments.rid)
    for problem in problems:
        print(f"{arguments.rid}: {problem}", file=sys.stderr)
    if not problems:
        print(f"{arguments.rid}: bundled native libraries pass")
    return 1 if problems else 0


if __name__ == "__main__":
    sys.exit(main())

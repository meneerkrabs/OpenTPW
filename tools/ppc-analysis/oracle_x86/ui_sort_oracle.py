"""tp.exe's deferred record sort 0x5829E0 and triangle sort 0x565490 run in the x86 oracle sandbox (docs/reverse/ORACLE.md, finding X70; UI-004).

Dev-only tooling like the rest of oracle_x86 (Unicorn and the identified tp.exe, OPENTPW_TPW_EXE, are needed). Both routines sort descending by a float key with a Hoare quicksort around the middle element; a UI model's
vertices all end at depth 0, so the keys of its records and triangles are equal and the permutation they leave is the original's draw order. `record_order` and `triangle_order` return the element indices in
sorted order.

Usage: python3 -I tools/ppc-analysis/oracle_x86/ui_sort_oracle.py   (prints the tie permutations for a few counts; ui_sort_golden.py writes the golden file)
"""
from __future__ import annotations

import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))

from x86_harness import Image, Sandbox, executable_path  # noqa: E402

RECORD_SORT, TRIANGLE_SORT, FACE_TEST = 0x5829E0, 0x565490, 0x581320


class UiSortOracle:
    def __init__(self, exe: Path | None = None):
        path = exe or executable_path('tpw')
        if path is None:
            raise SystemExit('needs OPENTPW_TPW_EXE (tools/ppc-analysis/oracle_x86/README.md)')
        self.sandbox = Sandbox(Image.load(path), [(RECORD_SORT, 0x582AC4), (TRIANGLE_SORT, 0x565558), (FACE_TEST, 0x581403)], budget=5_000_000)

    def record_order(self, keys):
        """0x5829E0(this, indices, 0, n - 1): `this`+0x44 points at 0x38 byte records whose key is the float at +0x30."""
        sb = self.sandbox
        sb.reset()
        count = len(keys)
        records = sb.alloc(0x38 * count + 16)
        owner = sb.alloc(0x100)
        sb.write(owner + 0x44, 'I', records)
        for index, key in enumerate(keys):
            sb.write(records + 0x38 * index + 0x30, 'f', key)
        indices = sb.alloc(4 * count + 16)
        for index in range(count):
            sb.write(indices + 4 * index, 'I', index)
        sb.call(RECORD_SORT, args=(indices, 0, count - 1), this=owner)
        return [sb.read(indices + 4 * index, 'I') for index in range(count)]

    def triangle_order(self, keys):
        """0x565490(keys, triangles, 0, n - 1) (cdecl): a six byte triangle record (a dword and a word) is swapped with each key."""
        sb = self.sandbox
        sb.reset()
        count = len(keys)
        key_array = sb.alloc(4 * count + 16)
        triangles = sb.alloc(6 * count + 16)
        for index, key in enumerate(keys):
            sb.write(key_array + 4 * index, 'f', key)
            sb.write(triangles + 6 * index, 'I', index)
            sb.write(triangles + 6 * index + 4, 'H', index)
        sb.call(TRIANGLE_SORT, args=(key_array, triangles, 0, count - 1))
        return [sb.read(triangles + 6 * index, 'I') for index in range(count)]

    def clockwise(self, points):
        """0x581320(a, b, c) (cdecl): each argument points at a transformed vertex (x at +0, y at +4, the reciprocal w at +0xC); 1 for a strictly clockwise triangle on the screen. `points` is three (x, y) pairs, w = 1."""
        sb = self.sandbox
        sb.reset()
        sb.set_fpcw(0x7F)   # the game's main thread runs the x87 at 24 bits once the Direct3D device exists (X36..X43)
        pointers = []
        for x, y in points:
            vertex = sb.alloc(16)
            sb.write(vertex, 'f', x)
            sb.write(vertex + 4, 'f', y)
            sb.write(vertex + 0xC, 'f', 1.0)
            pointers.append(vertex)
        return sb.call(FACE_TEST, args=tuple(pointers)).eax & 1


def main() -> None:
    oracle = UiSortOracle()
    for count in (2, 3, 4, 5, 8):
        print(count, oracle.record_order([0.0] * count), oracle.triangle_order([0.0] * count))


if __name__ == '__main__':
    main()

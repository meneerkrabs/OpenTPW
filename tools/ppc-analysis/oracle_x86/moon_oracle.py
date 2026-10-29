"""tp.exe's sky object draw 0x5860D0 run in the x86 oracle sandbox (docs/reverse/ORACLE.md, finding X40; UI-107).

Dev-only tooling like the rest of oracle_x86: Unicorn and the identified tp.exe (OPENTPW_TPW_EXE, SHA-256 pinned) are needed, nothing here is shipped. The sky object draw runs with its real
callees, the point transform 0x578D20 and the sprite rotation 0x591430, on a synthetic sky object (the clip matrix, the object's X, Y, Z and size, the roll and the aspect word are set); the
submit 0x565F90 is stubbed to record the polygon it is handed (its four corner positions), and the texture state lookup 0x590C40 answers a constant. The sine table 0x8C63D8, which the
original builds at start (0x5913F0), is filled the same way (sin(i * pi / 128)).

Usage: python3 -I tools/ppc-analysis/oracle_x86/moon_oracle.py   (prints the corner offsets from the quad's centre for a few rolls; SkyTests pins them)
"""
from __future__ import annotations

import math
import struct
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))

from x86_harness import Image, Sandbox, executable_path  # noqa: E402

DRAW, TRANSFORM, ROTATE, SUBMIT, TEXTURE, SINE_TABLE = 0x5860D0, 0x578D20, 0x591430, 0x565F90, 0x590C40, 0x8C63D8
FLAGS, ASPECT, ROLL, DEVICE = 0x8BCBC8, 0x8BCBCC, 0x87AF60, 0x879AB0
SKYDETAIL = 0x400000
IDENTITY = [1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1]


class MoonOracle:
    def __init__(self, exe: Path | None = None):
        path = exe or executable_path('tpw')
        if path is None:
            raise SystemExit('needs OPENTPW_TPW_EXE (tools/ppc-analysis/oracle_x86/README.md)')
        self.captured: list = []
        stubs = {SUBMIT: self._submit, TEXTURE: lambda sb: (0x77, 0)}
        allowed = [(DRAW, 0x5863C0), (TRANSFORM, 0x579000), (ROTATE, 0x591510), (0x67A830, 0x67A860)]   # the last: the float-to-integer helper
        self.sandbox = Sandbox(Image.load(path), allowed, stubs=stubs, budget=200_000)

    def _submit(self, sb):
        """0x565F90(polygon, mode), thiscall on the device: the polygon's +4 holds the four corner positions (x, y, z, w)."""
        polygon, mode = sb.arg(0), sb.arg(1)
        corners = sb.read(polygon + 4, 'I')
        self.captured.append((mode, [struct.unpack('<4f', bytes(sb.uc.mem_read(corners + 16 * i, 16))) for i in range(4)]))
        return 1, 8

    def run(self, position, size, matrix=IDENTITY, aspect=0.75, roll=0.0, front=1):
        """The corner positions (x, y, z, w) the draw submits for one sky object, and the submit's mode word."""
        sb = self.sandbox
        sb.reset()
        self.captured = []
        for index in range(256):
            sb.write(SINE_TABLE + 4 * index, 'f', math.sin(index * math.pi / 128))
        sb.write(FLAGS, 'I', SKYDETAIL)
        sb.write(ASPECT, 'f', aspect)
        sb.write(ROLL, 'f', roll)
        sb.write(DEVICE, 'I', 0x1234)
        sky = sb.alloc(0x3000)
        sb.write(sky + 0x21DC, 'I', 1)
        record = sky + 0x1FF4          # object 0 of the array at +0x1FDC (0x20 bytes each): +0 sprite, +4 texture state, +8 mask, +0xC X, +0x10 Y, +0x14 Z, +0x18 size
        sprite = sb.alloc(0x100)
        sb.write(record - 0x18, 'I', sprite)
        sb.write(record - 0x14, 'I', 0x77)
        sb.write(record - 0x10, 'I', front)
        for index, value in enumerate(position):
            sb.write(record - 0xC + 4 * index, 'f', value)
        sb.write(record, 'f', size)
        for index, value in enumerate(matrix):
            sb.write(sky + 0x2ECC + 4 * index, 'f', value)
        sb.call(DRAW, args=(front,), this=sky)
        return self.captured[0] if self.captured else None


def main() -> None:
    oracle = MoonOracle()
    for roll in (0.0, 0.125, 0.25, -0.125):
        mode, corners = oracle.run((2, 11, 15), 5.0, aspect=1.0, roll=roll)
        print(f'roll {roll}: mode {mode & 0xFFFFFFFF:#x}', [(round(x - 2, 4), round(y - 11, 4)) for x, y, _, _ in corners])


if __name__ == '__main__':
    main()

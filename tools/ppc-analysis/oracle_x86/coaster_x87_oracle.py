"""tp.exe's coaster arithmetic run in the x86 oracle sandbox at the thread's x87 control word (docs/reverse/ORACLE.md, finding X40; COAST-032, COAST-014, COAST-004).

Dev-only tooling like the rest of oracle_x86: Unicorn and the identified tp.exe (OPENTPW_TPW_EXE, SHA-256 pinned) are needed, nothing here is shipped. Every routine runs on synthetic
records (knots, nodes, samples) the oracle lays out in the sandbox heap, with the control word 0x7F the shipped D3DIM device leaves the thread at (24-bit significand, X87Precision); no original
data and no game object enter.
"""
from __future__ import annotations

import math
import struct
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))

from x86_harness import Image, Sandbox, executable_path, have_unicorn  # noqa: E402

CONTROL_WORD = 0x7F
# the table is built by the first pass of the message pump (0x45ABB6 -> 0x449EB0 -> 0x4708D0), before the display init that makes DDSCL_FPUSETUP set the 24-bit word: the CRT's 53 bits (0x27F)
TABLE_CONTROL_WORD = 0x27F
TABLE_BASE = 0x7B095C
TABLE_SIZE = 0x10 + 4 * 4096


def f32(value: float) -> float:
    return struct.unpack('<f', struct.pack('<f', value))[0]


def bits(value: float) -> int:
    return struct.unpack('<I', struct.pack('<f', value))[0]


def from_bits(value: int) -> float:
    return struct.unpack('<f', struct.pack('<I', value & 0xFFFFFFFF))[0]


class CoasterOracle:
    def __init__(self, exe: Path | None = None, budget: int = 2_000_000):
        if not have_unicorn():
            raise RuntimeError('needs Unicorn')
        path = exe or executable_path('tpw')
        if path is None:
            raise RuntimeError('needs OPENTPW_TPW_EXE')
        self.image = Image.load(path)
        self.sandbox = Sandbox(self.image, [(0x401000, 0x6F0000)], budget=budget)
        # the PC sine table is built at run time by 0x4708D0(4096): run it once and keep the table's bytes (scale +0x7B095C, mask +0x7B0960, entries from +0x7B0968)
        sb = self.sandbox
        sb.reset()
        sb.set_fpcw(TABLE_CONTROL_WORD)
        sb.call(0x4708D0, (4096,))
        self.table_bytes = bytes(sb.uc.mem_read(TABLE_BASE, TABLE_SIZE))

    def prepare(self):
        """A pristine sandbox at the thread's control word with the PC sine table in place."""
        sb = self.sandbox
        sb.reset()
        sb.set_fpcw(CONTROL_WORD)
        sb.uc.mem_write(TABLE_BASE, self.table_bytes)
        sb._on_write(None, 0, TABLE_BASE, TABLE_SIZE, 0, None)
        return sb

    # ------------------------------------------------------------------------------ the curve evaluators
    def curve(self, entry: int, p0, forward, back, p3, t: float):
        """0x43E1B0 (the point) or 0x43E320 (the derivative): (node A, node B, t, out) over two knot records; returns the three singles."""
        box = {}

        def setup(sb):
            a = sb.alloc(0x100)
            b = sb.alloc(0x100)
            out = sb.alloc(16)
            for k in range(3):
                sb.write(a + 0x40 + 4 * k, 'f', p0[k])
                sb.write(a + 0x4C + 4 * k, 'f', forward[k])
                sb.write(b + 0x34 + 4 * k, 'f', back[k])
                sb.write(b + 0x40 + 4 * k, 'f', p3[k])
            box.update(a=a, b=b, out=out)

        sb = self.prepare()
        setup(sb)
        sb.call(entry, (box['a'], box['b'], bits(t), box['out']))
        return tuple(sb.read(box['out'] + 4 * k, 'f') for k in range(3))

    # ------------------------------------------------------------------------------ the knot chord
    def chord_scenario(self, scenario: str, positions, other=None, index: int = 1, rail: int = 1):
        """0x432840(index, node, &chord1, &chord2, rail, &vec1, &vec2) over a node whose knot table holds `positions` (stride 0x58, the position at +0x40 of the first rail
        and +0x48 records of the second rail's table at +0x8 of the table struct): returns (chord before, chord after, the two difference vectors)."""
        box = {}

        def setup(sb):
            def table(points):
                struct_ = sb.alloc(0x20)
                array = sb.alloc(0x58 * (len(points) + 1) + 0x20)
                for i, point in enumerate(points):
                    for k in range(3):
                        sb.write(array + 0x58 * i + 0x40 + 4 * k, 'f', point[k])
                sb.write(struct_, 'I', len(points))
                sb.write(struct_ + 4, 'I', array)
                sb.write(struct_ + 8, 'I', array)
                return struct_
            node = sb.alloc(0x200)
            sb.write(node + 0x90, 'I', table(positions))
            if other is not None:
                kind, points = other
                neighbour = sb.alloc(0x200)
                sb.write(neighbour + 0x90, 'I', table(points))
                sb.write(node + (0x64 if kind == 'next' else 0x68), 'I', neighbour)
            outs = sb.alloc(64)
            box.update(node=node, outs=outs)

        sb = self.prepare()
        setup(sb)
        o = box['outs']
        sb.call(0x432840, (index, box['node'], o, o + 4, rail, o + 16, o + 32))
        return (sb.read(o, 'f'), sb.read(o + 4, 'f'), tuple(sb.read(o + 16 + 4 * k, 'f') for k in range(3)), tuple(sb.read(o + 32 + 4 * k, 'f') for k in range(3)))

    # ------------------------------------------------------------------------------ the knot controls
    def knot_controls(self, curviness: float, position, direction, back_chord: float, forward_chord: float):
        """0x432430 on a plain knot (no loop pylon): the back and forward control points the routine stores at +0x34 and +0x4C of the knot record."""
        box = {}

        def setup(sb):
            control = sb.alloc(0x40)
            sb.write(control + 8, 'f', curviness)
            vector = sb.alloc(16)
            record = sb.alloc(0x100)
            node = sb.alloc(0x200)
            for k in range(3):
                sb.write(vector + 4 * k, 'f', direction[k])
                sb.write(record + 0x40 + 4 * k, 'f', position[k])
            sb.write(record + 0x10, 'I', node)
            box.update(control=control, vector=vector, record=record)

        sb = self.prepare()
        setup(sb)
        sb.call(0x432430, (box['control'], box['vector'], box['record'], bits(back_chord), bits(forward_chord), 0, 0, 0, 0, 0))
        r = box['record']
        return (tuple(sb.read(r + 0x34 + 4 * k, 'f') for k in range(3)), tuple(sb.read(r + 0x4C + 4 * k, 'f') for k in range(3)))

    # ------------------------------------------------------------------------------ the sample angles
    def orientation(self, tangent):
        """0x43E590(tangent, t, record, 0): the yaw (+4) and pitch (+8) the routine stores for a tangent."""
        box = {}

        def setup(sb):
            box['record'] = sb.alloc(0x40)

        sb = self.prepare()
        setup(sb)
        sb.call(0x43E590, (bits(tangent[0]), bits(tangent[1]), bits(tangent[2]), bits(0.0), box['record'], 0))
        r = box['record']
        return (sb.read(r + 4, 'f'), sb.read(r + 8, 'f'))


def main() -> int:
    import random
    oracle = CoasterOracle()
    rng = random.Random('coast-x87-v1')
    for index in range(5):
        p0 = [f32(rng.uniform(0, 1200)) for _ in range(3)]
        p3 = [f32(rng.uniform(0, 1200)) for _ in range(3)]
        fw = [f32(p0[k] + rng.uniform(-40, 40)) for k in range(3)]
        bk = [f32(p3[k] + rng.uniform(-40, 40)) for k in range(3)]
        t = f32(rng.random())
        print(oracle.curve(0x43E1B0, p0, fw, bk, p3, t), oracle.curve(0x43E320, p0, fw, bk, p3, t))
    return 0


if __name__ == '__main__':
    sys.exit(main())

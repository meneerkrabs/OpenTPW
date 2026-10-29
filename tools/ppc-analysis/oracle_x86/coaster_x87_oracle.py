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

    # ------------------------------------------------------------------------------ the node tangent
    def node_tangent(self, position, previous=None, following=None, previous_of_previous=None, definition=None, reversed_=False, loop_node=False,
                     previous_loop=None, next_loop=None, head_next=None, straight_divisor=2.0, curviness=1.0, suspended=False, flags_in=0,
                     drop_previous=False, drop_next=False, use_head_next=False):
        """0x431660(node, definition, controller) on a node with the given neighbours (None = no link). `definition` is None (a pylon-less node) or (loop_flag, straight_dot); the neighbours'
        definitions are None (no pylon), False (a pylon without the loop flag) or True (a loop pylon). Returns (back, forward, turn, flags_after, written)."""
        box = {}
        SENTINEL = 12345.0

        def setup(sb):
            def pylon(loop):
                if loop is None:
                    return sb.alloc(0x200)  # a descriptor whose +0x18 is 0: no definition
                descriptor = sb.alloc(0x200)
                definition_ = sb.alloc(0x100)
                sb.write(definition_, 'I', 0x4000 if loop else 0)
                sb.write(descriptor + 0x18, 'I', definition_)
                return descriptor

            def node(position_, loop, flags=0):
                address = sb.alloc(0x200)
                sb.write(address + 8, 'I', flags)
                for k in range(3):
                    sb.write(address + 0x4C + 4 * k, 'f', position_[k])
                sb.write(address + 0x100, 'I', pylon(loop))
                return address

            flags = flags_in | (0x80 if reversed_ else 0) | (0x40 if loop_node else 0) | (0x80000 if drop_previous else 0) | (0x40000 if drop_next else 0) | (0x100000 if use_head_next else 0)
            this = sb.alloc(0x200)
            sb.write(this + 8, 'I', flags)
            for k in range(3):
                sb.write(this + 0x4C + 4 * k, 'f', position[k])
            descriptor = sb.alloc(0x200)
            if definition is not None:
                own = sb.alloc(0x100)
                sb.write(own, 'I', 0x4000 if definition[0] else 0)
                sb.write(own + 0xB0, 'f', definition[1])
                sb.write(descriptor + 0x18, 'I', own)
            sb.write(this + 0x100, 'I', descriptor)
            if previous is not None:
                prev = node(previous, previous_loop)
                sb.write(this + 0x68, 'I', prev)
                if previous_of_previous is not None:
                    sb.write(prev + 0x68, 'I', node(previous_of_previous, None))
            if following is not None:
                sb.write(this + 0x64, 'I', node(following, next_loop))
            defn = sb.alloc(0x40)
            sb.write(defn, 'I', 8 if suspended else 0)
            sb.write(defn + 8, 'f', curviness)
            sb.write(defn + 0xC, 'f', straight_divisor)
            ctl = sb.alloc(0x40)
            head = sb.alloc(0x200)
            if head_next is not None:
                sb.write(head + 0x64, 'I', node(head_next, None))
            sb.write(ctl + 0x18, 'I', head)
            for offset in (0x28, 0x2C, 0x30, 0x34, 0x38, 0x3C, 0x94):
                sb.write(this + offset, 'f', SENTINEL)
            box.update(this=this, defn=defn, ctl=ctl)

        sb = self.prepare()
        setup(sb)
        sb.call(0x431660, (box['this'], box['defn'], box['ctl']))
        t = box['this']
        values = [sb.read(t + o, 'f') for o in (0x28, 0x2C, 0x30, 0x34, 0x38, 0x3C, 0x94)]
        written = values[6] != SENTINEL or values[0] != SENTINEL
        return (tuple(values[0:3]), tuple(values[3:6]), values[6], sb.read(t + 8, 'I'), written)

    # ------------------------------------------------------------------------------ the bank pass
    def st0(self, sb, result) -> float:
        """The top of the x87 stack after a call (a routine returning its answer in st0), as a single."""
        top = (sb.x87_state()['fpsw'] >> 11) & 7
        mantissa, exponent = result.fpu[top]
        sign = -1.0 if exponent & 0x8000 else 1.0
        biased = exponent & 0x7FFF
        if mantissa == 0:
            return 0.0 * sign
        return f32(sign * math.ldexp(mantissa, biased - 16383 - 63))

    def speed_estimate(self, per_height, uphill, downhill, speed, floor, y, y_next):
        """0x43BFB0(definition, speed, floor, y, y_next): the next speed estimate of the bank pass (the definition's +0x24, +0x28, +0x2C)."""
        box = {}

        def setup(sb):
            definition = sb.alloc(0x40)
            sb.write(definition + 0x24, 'f', per_height)
            sb.write(definition + 0x28, 'f', uphill)
            sb.write(definition + 0x2C, 'f', downhill)
            box['d'] = definition

        sb = self.prepare()
        setup(sb)
        result = sb.call(0x43BFB0, (box['d'], bits(speed), bits(floor), bits(y), bits(y_next)))
        return self.st0(sb, result)

    def auto_bank(self, position, previous, following, turn, speed, divisor, flags=0):
        """0x43C160(controller, node, speed): the node's auto-bank over its raw neighbours; the controller's definition (+4) holds the divisor at +0x10."""
        box = {}

        def setup(sb):
            definition = sb.alloc(0x40)
            sb.write(definition + 0x10, 'f', divisor)
            controller = sb.alloc(0x40)
            sb.write(controller + 4, 'I', definition)
            node = sb.alloc(0x200)
            prev = sb.alloc(0x200)
            nxt = sb.alloc(0x200)
            for address, point in ((node, position), (prev, previous), (nxt, following)):
                for k in range(3):
                    sb.write(address + 0x4C + 4 * k, 'f', point[k])
            sb.write(node + 0x94, 'f', turn)
            sb.write(node + 8, 'I', flags)
            sb.write(node + 0x68, 'I', prev)
            sb.write(node + 0x64, 'I', nxt)
            box.update(controller=controller, node=node)

        sb = self.prepare()
        setup(sb)
        result = sb.call(0x43C160, (box['controller'], box['node'], bits(speed)))
        return self.st0(sb, result)


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

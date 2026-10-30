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
        # the CRT's math error path reads and writes the last error
        for name, stub in (('kernel32.dll!GetLastError', lambda box: (0, 0)), ('kernel32.dll!SetLastError', lambda box: (0, 4)), ('kernel32.dll!TlsGetValue', lambda box: (box.alloc(0x1000), 4))):
            try:
                self.sandbox.stubs[self.image.import_trap(name)] = stub
            except Exception:
                pass
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

    # ------------------------------------------------------------------------------ the loop knot
    def turn_fraction(self, yaw: float) -> float:
        """The knot pass's node field +0xEC (0x43110C..0x431157): the fractional part of the node's yaw over 2 pi, plus 1 when negative."""
        sb = self.prepare()
        node = sb.alloc(0x200)
        frame = sb.alloc(0x400) + 0x300
        sb.write(node + 0xE0, 'f', yaw)
        sb.call(0x43110C, (), regs={'esi': node, 'ebp': frame}, stop=0x431157)
        return sb.read(node + 0xEC, 'f')

    def loop_knot(self, curviness, position, direction, back_chord, forward_chord, loop_flag=True, reversed_=False, neighbour=None, neighbour_flags=0,
                  turn_fraction=0.0, loop_rotate=0.0, previous=None, previous_flags=0, following=None, following_flags=0):
        """0x432430 on a knot of a node with a pylon definition: `neighbour` is the position of the raw link the loop side reads (the previous node, or the next one with node flag 0x80);
        previous/following are the raw links' positions and flags for the side factors. Returns (back control, forward control)."""
        box = {}

        def setup(sb):
            control = sb.alloc(0x40)
            sb.write(control + 8, 'f', curviness)
            vector = sb.alloc(16)
            record = sb.alloc(0x100)
            node = sb.alloc(0x200)
            definition = sb.alloc(0x100)
            sb.write(definition, 'I', 0x4000 if loop_flag else 0)
            for k in range(3):
                sb.write(vector + 4 * k, 'f', direction[k])
                sb.write(record + 0x40 + 4 * k, 'f', position[k])
                sb.write(node + 0x4C + 4 * k, 'f', 0.0)
            # the node stands at `position`'s x and z for the side's neighbour difference
            sb.write(node + 0x4C, 'f', position[0])
            sb.write(node + 0x54, 'f', position[2])
            sb.write(node + 8, 'I', 0x80 if reversed_ else 0)
            sb.write(node + 0xEC, 'f', turn_fraction)
            sb.write(node + 0xF8, 'f', loop_rotate)
            sb.write(record + 0x10, 'I', node)

            def link(point, flags):
                address = sb.alloc(0x200)
                sb.write(address + 8, 'I', flags)
                for k in range(3):
                    sb.write(address + 0x4C + 4 * k, 'f', point[k])
                return address
            if previous is not None:
                sb.write(node + 0x68, 'I', link(previous, previous_flags))
            if following is not None:
                sb.write(node + 0x64, 'I', link(following, following_flags))
            box.update(control=control, vector=vector, record=record, definition=definition)

        sb = self.prepare()
        setup(sb)
        sb.call(0x432430, (box['control'], box['vector'], box['record'], bits(back_chord), bits(forward_chord), box['definition'], 0, 0, 0, 0))
        r = box['record']
        return (tuple(sb.read(r + 0x34 + 4 * k, 'f') for k in range(3)), tuple(sb.read(r + 0x4C + 4 * k, 'f') for k in range(3)))

    # ------------------------------------------------------------------------------ the pylon cost
    def pylon_cost(self, t, low, high, position, other, unit, length_rate, height_rate, charge=True):
        """0x447830(controller, charge, node, ...) over a node whose own pylon definition has the given mesh heights and rates (no special flags, so its own rates apply) and a previous node at `other`."""
        box = {}

        def setup(sb):
            control = sb.alloc(0x40)
            definition = sb.alloc(0x100)
            sb.write(control + 4, 'I', definition)
            own = sb.alloc(0x100)
            sb.write(own + 0x5C, 'f', unit)
            sb.write(own + 0x60, 'f', length_rate)
            sb.write(own + 0x64, 'f', height_rate)
            sb.write(own + 0xBC, 'f', low)
            sb.write(own + 0xC0, 'f', high)
            descriptor = sb.alloc(0x100)
            sb.write(descriptor + 0x18, 'I', own)
            node = sb.alloc(0x200)
            previous = sb.alloc(0x200)
            sb.write(node + 0x100, 'I', descriptor)
            sb.write(node + 0xE8, 'f', t)
            sb.write(node + 0x68, 'I', previous)
            sb.write(node + 8, 'I', 0)
            sb.write(node + 0x4C, 'f', position[0])
            sb.write(node + 0x54, 'f', position[1])
            sb.write(previous + 0x4C, 'f', other[0])
            sb.write(previous + 0x54, 'f', other[1])
            box.update(control=control, node=node)

        sb = self.prepare()
        setup(sb)
        result = sb.call(0x447830, (box['control'], 1 if charge else 0, box['node'], 0, 0, 0, 0))
        return self.st0(sb, result)

    # ------------------------------------------------------------------------------ the sampler
    @staticmethod
    def _write_model_controls(sb, tables, nodes, curviness):
        def chord(a, b):
            dx = f32(b[0] - a[0])
            dy = f32(f32(b[1] * f32(0.7)) - f32(a[1] * f32(0.7)))
            dz = f32(b[2] - a[2])
            return f32(math.sqrt(f32(f32(f32(dx * dx) + f32(dy * dy)) + f32(dz * dz))))

        flat = [(k, 0) for knots in nodes for k in knots]
        for rail in (0, 1):
            positions = [tuple(f32(p[j] + (1.0 if (rail == 1 and j == 1) else 0.0)) for j in range(3)) for (p, d), _ in flat]
            index = 0
            for node_index, (first, second, size) in enumerate(tables):
                for i in range(size):
                    (position, direction), _ = flat[index]
                    # the node pass normalises the node's first record's direction for every knot (0x4322d7), here all knots of a node share one vector
                    norm = f32(math.sqrt(f32(f32(f32(direction[0] * direction[0]) + f32(direction[1] * direction[1])) + f32(direction[2] * direction[2]))))
                    unit = [f32(direction[j] / norm) for j in range(3)]
                    back_chord = chord(positions[index - 1], positions[index]) if index > 0 else 1.0
                    forward_chord = chord(positions[index], positions[index + 1]) if index + 1 < len(positions) else 1.0
                    n2 = f32(math.sqrt(f32(f32(f32(unit[0] * unit[0]) + f32(unit[1] * unit[1])) + f32(unit[2] * unit[2]))))
                    scale_back = f32(f32(back_chord * curviness) / n2)
                    scale_forward = f32(f32(forward_chord * curviness) / n2)
                    third = f32(-0.3333333432674408)
                    base = (first if rail == 0 else second) + 0x58 * i
                    for j in range(3):
                        back = f32(positions[index][j] - f32(f32(-unit[j] * scale_back) * third))
                        forward = f32(positions[index][j] - f32(f32(unit[j] * scale_forward) * third))
                        sb.write(base + 0x34 + 4 * j, 'f', back)
                        sb.write(base + 0x4C + 4 * j, 'f', forward)
                    index += 1

    def sampler(self, nodes, curviness: float, spacing: float = 3.0, model_controls: bool = False, extrusion=None):
        """0x440C10 over a chain of nodes whose knots are `nodes[i] = [(position, direction), ...]` (the first node is the head S0, the last the end node, whose raw next link is null).
        The knot controls come from 0x4321C0 (the node pass, each node without a pylon model), the second rail's points are the knots' positions plus (0, 1, 0). Returns the samples
        (position, yaw, pitch, roll, t, knot record index), the sample count n (+0xA4), the end coordinate (+0xA8), every knot record's coordinate (+8) and every node's sample index (+0xFC)."""
        sb = self.prepare()
        sb.stubs[0x45B9D0] = lambda box: (box.alloc(box.arg(0) + 64), 0)
        sb.stubs[0x45BA00] = lambda box: (0, 0)
        sb.stubs[0x43B750] = lambda box: (0, 0)
        records = []
        node_addresses = []
        table_addresses = []
        previous_record = 0
        for knots in nodes:
            node = sb.alloc(0x200)
            node_addresses.append(node)
            table = sb.alloc(0x20)
            first = sb.alloc(0x58 * len(knots) + 0x20)
            second = sb.alloc(0x58 * len(knots) + 0x20)
            sb.write(table, 'I', len(knots))
            sb.write(table + 4, 'I', first)
            sb.write(table + 8, 'I', second)
            sb.write(node + 0x90, 'I', table)
            sb.write(node + 0x100, 'I', sb.alloc(0x100))
            table_addresses.append((first, second, len(knots)))
            for i, (position, direction) in enumerate(knots):
                for rail, base in ((0, first), (1, second)):
                    record = base + 0x58 * i
                    for k in range(3):
                        sb.write(record + 0x40 + 4 * k, 'f', f32(position[k] + (1.0 if (rail == 1 and k == 1) else 0.0)))
                        sb.write(record + 0x28 + 4 * k, 'f', direction[k])
                    sb.write(record + 0x10, 'I', node)
                    if rail == 0:
                        sb.write(record + 0x14, 'I', previous_record)
                if True:
                    previous_record = first + 0x58 * i
        flat_records = [first + 0x58 * i for first, second, size in table_addresses for i in range(size)]
        flat_second = [second + 0x58 * i for first, second, size in table_addresses for i in range(size)]
        for a, b in zip(flat_records, flat_records[1:]):
            sb.write(a + 0x18, 'I', b)
        for a, b in zip(flat_second, flat_second[1:]):
            sb.write(a + 0x18, 'I', b)
            sb.write(b + 0x14, 'I', a)
        for i, node in enumerate(node_addresses):
            if i + 1 < len(node_addresses):
                sb.write(node + 0x64, 'I', node_addresses[i + 1])
            if i > 0:
                sb.write(node + 0x68, 'I', node_addresses[i - 1])
        definition = sb.alloc(0x100)
        sb.write(definition + 8, 'f', curviness)
        sb.write(definition + 0x10, 'f', 4.0)
        holder = sb.alloc(0x20)
        sb.write(holder + 4, 'I', definition)
        sb.write(0x792218, 'I', holder)
        game = sb.alloc(0x40)
        sb.write(game + 0x18, 'I', node_addresses[0])
        sb.write(0x790FE4, 'I', game)
        if model_controls:
            # nodes with several knots stand for pylons whose tags the node pass reads from the model: the controls are written from the verified single steps instead
            self._write_model_controls(sb, table_addresses, nodes, curviness)
        else:
            for node in node_addresses:
                sb.call(0x4321C0, (node, 0))
        controller = sb.alloc(0x400)
        sb.write(controller + 0x54, 'I', node_addresses[0])
        sb.write(controller + 0xAC, 'f', spacing)
        sb.call(0x440C10, (controller,))
        array = sb.read(controller + 0x9C, 'I')
        count = sb.read(controller + 0xA4, 'I')
        samples = []
        by_record = {}
        for n, (first, second, size) in enumerate(table_addresses):
            for i in range(size):
                by_record[first + 0x58 * i] = (n, i)
        flat = {}
        index = 0
        for n, (first, second, size) in enumerate(table_addresses):
            for i in range(size):
                flat[first + 0x58 * i] = index
                index += 1
        for i in range(count + 1):
            base = array + 0x38 * i
            record = sb.read(base + 0x2C, 'I')
            samples.append((tuple(sb.read(base + 4 + 4 * k, 'f') for k in range(3)), sb.read(base + 0x10, 'f'), sb.read(base + 0x14, 'f'), sb.read(base + 0x18, 'f'),
                            sb.read(base + 0x34, 'f'), flat.get(record, -1)))
        coordinates = [sb.read(first + 0x58 * i + 8, 'f') for first, second, size in table_addresses for i in range(size)]
        indexes = [sb.read(node + 0xFC, 'I') for node in node_addresses]
        self.last_runs = self._extrusion_runs(sb, controller, extrusion, table_addresses) if extrusion is not None else None
        self.last_controls = [[tuple(sb.read(base + 0x58 * i + off + 4 * k, 'f') for k in range(3)) for off in (0x34, 0x4C)] for first, second, size in table_addresses for base in (first,) for i in range(size)]
        self.last_controls2 = [[tuple(sb.read(base + 0x58 * i + off + 4 * k, 'f') for k in range(3)) for off in (0x34, 0x4C)] for first, second, size in table_addresses for base in (second,) for i in range(size)]
        return samples, count, sb.read(controller + 0xA8, 'f'), coordinates, indexes, sb.read(controller + 0xA0, 'I')

    # ------------------------------------------------------------------------------ the cross-section run builder
    def _extrusion_runs(self, sb, controller, extrusion, table_addresses):
        """0x4402D0 over the sampled controller. `extrusion` = (selects, lengths, transitions): `selects` is the type's table, one entry per select as
        (windows, answer, from, to) with the eight window singles (speed, direction, pitch, bank: min, max), the answer's section index and the transition's
        from and to section indexes or None; `lengths` the section records' +0x14 singles; `transitions` the type's flag +0x74 bit 0. Returns the run table's
        entries (key, position, weight, section index) up to and including the sentinel."""
        selects, lengths, transitions = extrusion
        # the mesh builder numbers the knot records of both rails along the chain (0x43ECC9..0x43ECDA); the run keys are that number plus the fraction to the next knot
        number = 0
        for first, second, size in table_addresses:
            for i in range(size):
                sb.write(first + 0x58 * i, 'I', number)
                sb.write(second + 0x58 * i, 'I', number)
                number += 1
        sections = sb.alloc(0x18 * (len(lengths) + 1))
        for index, length in enumerate(lengths):
            sb.write(sections + 0x18 * index + 0x14, 'f', length)

        def pointer(index):
            return 0 if index is None else sections + 0x18 * index
        table = sb.alloc(0x2C * len(selects) + 0x20)
        for index, (windows, answer, source, target) in enumerate(selects):
            entry = table + 0x2C * index
            for k, value in enumerate(windows):
                sb.write(entry + 4 * k, 'f', value)
            sb.write(entry + 0x20, 'I', pointer(answer))
            sb.write(entry + 0x24, 'I', pointer(source))
            sb.write(entry + 0x28, 'I', pointer(target))
        kind = sb.alloc(0x100)
        sb.write(kind + 0x74, 'I', 1 if transitions else 0)
        sb.write(kind + 0x78, 'I', table)
        sb.write(kind + 0x7C, 'I', len(selects))
        sb.write(kind + 0x80, 'I', sections + 0x18 * len(lengths))
        sb.write(kind + 0x84, 'I', sections)
        sb.write(controller + 4, 'I', kind)
        sb.write(0x792218, 'I', controller)
        sb.write(0x790FE4, 'I', sb.alloc(0x100))
        sb.call(0x4402D0, ())
        runs = []
        for index in range(2560):
            base = 0x792600 + 0x10 * index
            key, position, weight, section = (sb.read(base, 'f'), sb.read(base + 4, 'f'), sb.read(base + 8, 'f'), sb.read(base + 12, 'I'))
            runs.append((key, position, weight, (section - sections) // 0x18 if section else -1))
            if key >= 100000.0:
                break
        return runs

    # ------------------------------------------------------------------------------ the self-intersection pass
    def prism(self, samples_a, samples_b, points, control_word=0xC7F, capture=()):
        """0x43A2B0 on two links' segments: the pass runs at the thread's word with the rounding mode set to truncate (0xC7F, 0x439AC4). `samples_a/b` are (position, yaw, pitch, roll) of the
        segment's two samples; `points` the collision profile. Returns (hit, poses A, poses B, the angle sums the face test compared (st0 at 0x43A8B3, in order))."""
        box = {}

        def setup(sb):
            def write_samples(samples):
                base = sb.alloc(0x38 * 2 + 8)
                for i, (position, yaw, pitch, roll) in enumerate(samples):
                    for k in range(3):
                        sb.write(base + 0x38 * i + 4 + 4 * k, 'f', position[k])
                    sb.write(base + 0x38 * i + 0x10, 'f', yaw)
                    sb.write(base + 0x38 * i + 0x14, 'f', pitch)
                    sb.write(base + 0x38 * i + 0x18, 'f', roll)
                return base

            array = sb.alloc(8 * len(points) + 8)
            for i, (x, y) in enumerate(points):
                sb.write(array + 8 * i, 'f', x)
                sb.write(array + 8 * i + 4, 'f', y)
            point_object = sb.alloc(0x20)
            sb.write(point_object, 'I', array)
            sb.write(point_object + 4, 'I', len(points))
            sb.write(0x7922DC, 'I', point_object)
            sb.write(0x792244, 'I', point_object)
            sb.write(0x79223C, 'I', write_samples(samples_a))
            sb.write(0x792240, 'I', write_samples(samples_b))

        sb = self.prepare()
        setup(sb)
        sb.set_fpcw(control_word)
        sums = []
        self.captured = []

        def on_code(uc, address, size, user):
            if address in capture:
                top = (sb.x87_state()['fpsw'] >> 11) & 7
                mantissa, exponent = uc.reg_read(x86_const.UC_X86_REG_FP0 + top)
                self.captured.append((address, mantissa, exponent))
            if address in (0x43A8B3, 0x43AD2B):
                top = (sb.x87_state()['fpsw'] >> 11) & 7
                mantissa, exponent = uc.reg_read(x86_const.UC_X86_REG_FP0 + top)
                sign = -1.0 if exponent & 0x8000 else 1.0
                sums.append(float('nan') if (exponent & 0x7FFF) == 0x7FFF else (f32(sign * math.ldexp(mantissa, (exponent & 0x7FFF) - 16383 - 63)) if mantissa else 0.0))

        import unicorn as unicorn_module
        from unicorn import x86_const
        handle = sb.uc.hook_add(unicorn_module.UC_HOOK_CODE, on_code, begin=0x43A600, end=0x43AD30)
        try:
            result = sb.call(0x43A2B0, (0, 0))
        finally:
            sb.uc.hook_del(handle)
        count = len(points)
        poses_a = [tuple(sb.read(0x7922F0 + 12 * i + 4 * k, 'f') for k in range(3)) for i in range(2 * count)]
        poses_b = [tuple(sb.read(0x792248 + 12 * i + 4 * k, 'f') for k in range(3)) for i in range(2 * count)]
        return (result.eax & 0xFF) != 0, poses_a, poses_b, sums

    def link_test(self, samples, nodes, points, radius_values, cut_end=False, same_cell=(False, False), pylonless=(False, False, False, False), first_ordinal_zero=(False, False)):
        """The whole link test 0x439C40(ctl, ctl, A, B, C, D) over a chain of four nodes A -> B -> C -> D laid on `samples` (position, yaw, pitch, roll per sample, stride 0x38); `nodes` are the
        four nodes' sample indexes; `radius_values` the radius each node's pylon definition gives (None: no definition). The link boxes come from 0x439920, run first at the thread's word.
        Returns the crossing lists of A and C (partner node ordinals), their counts, the controller's crossing count and the nodes' overflow flags."""
        box = {}

        def setup(sb):
            definition = sb.alloc(0x400)
            array = sb.alloc(8 * len(points) + 8)
            for i, (x, y) in enumerate(points):
                sb.write(array + 8 * i, 'f', x)
                sb.write(array + 8 * i + 4, 'f', y)
            sb.write(definition + 0x9C, 'I', array)
            sb.write(definition + 0x9C + 4, 'I', len(points))
            controller = sb.alloc(0x400)
            sb.write(controller + 4, 'I', definition)
            sample_array = sb.alloc(0x38 * len(samples) + 8)
            for i, (position, yaw, pitch, roll) in enumerate(samples):
                for k in range(3):
                    sb.write(sample_array + 0x38 * i + 4 + 4 * k, 'f', position[k])
                sb.write(sample_array + 0x38 * i + 0x10, 'f', yaw)
                sb.write(sample_array + 0x38 * i + 0x14, 'f', pitch)
                sb.write(sample_array + 0x38 * i + 0x18, 'f', roll)
            sb.write(controller + 0x9C, 'I', sample_array)
            sb.write(controller + 0xA0, 'I', len(samples))
            shared_descriptor = sb.alloc(0x100)
            addresses = []
            for i, (index, radius) in enumerate(zip(nodes, radius_values)):
                node = sb.alloc(0x200)
                descriptor = sb.alloc(0x100)
                sb.write(descriptor + 4, 'B', 1 if pylonless[i] else 0)
                if radius is not None:
                    pylon = sb.alloc(0x100)
                    sb.write(pylon + 0x58, 'f', radius)
                    sb.write(descriptor + 0x18, 'I', pylon)
                sb.write(node + 0x100, 'I', descriptor)
                sb.write(node + 0xFC, 'I', index)
                sb.write(node, 'I', 0 if (i in (2,) and first_ordinal_zero[0]) or (i == 0 and first_ordinal_zero[1]) else i + 1)
                sb.write(node + 8, 'I', 0x10000 if (cut_end and i == 2) else 0)
                addresses.append(node)
            # descriptors shared by nodes in one cell: B and C (same_cell[0]), A and D (same_cell[1])
            if same_cell[0]:
                sb.write(addresses[1] + 0x100, 'I', sb.read(addresses[2] + 0x100, 'I'))
            if same_cell[1]:
                sb.write(addresses[3] + 0x100, 'I', sb.read(addresses[0] + 0x100, 'I'))
            for a, b in zip(addresses, addresses[1:]):
                sb.write(a + 0x64, 'I', b)
                sb.write(b + 0x68, 'I', a)
            sb.write(controller + 0x54, 'I', addresses[0])
            for global_, size in ((0x792238, 0x1C * len(samples)), (0x792230, 0x1C * len(samples)), (0x7922E8, 0x1000), (0x7922E4, 0x1000)):
                sb.write(global_, 'I', sb.alloc(size + 0x40))
            box.update(controller=controller, nodes=addresses)

        sb = self.prepare()
        setup(sb)
        sb.call(0x439920, (box['controller'],))
        sb.set_fpcw(0xC7F)
        a, b, c, d = box['nodes']
        sb.call(0x439C40, (box['controller'], box['controller'], a, b, c, d))
        node_index = {address: i for i, address in enumerate(box['nodes'])}
        def lst(address):
            return [node_index.get(sb.read(address + 0xB4 + 4 * k, 'I'), -1) for k in range(sb.read(address + 0xB0, 'I'))]
        return lst(a), lst(c), sb.read(box['controller'] + 0x140, 'I'), sb.read(a + 8, 'I'), sb.read(c + 8, 'I')


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

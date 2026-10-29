"""Golden rows of tp.exe's coaster arithmetic run in the x86 oracle at the thread's control word (docs/reverse/ORACLE.md, finding X40; COAST-032, COAST-014, COAST-004).

Every row is `kind | inputs > outputs` with the singles written as 8 hex digits of their bit patterns. The oracle's own synthetic inputs (random knots, directions, chords, parameters) are run
through the real routines in the Unicorn sandbox at CW 0x7F; OpenTPW.Tests replays every row on the C# port and compares the bits.

Usage: python3 -I tools/ppc-analysis/oracle_x86/coaster_x87_golden.py > source/OpenTPW.Tests/CoasterX87Golden.cs   (needs Unicorn and OPENTPW_TPW_EXE; --check FILE compares)
"""
from __future__ import annotations

import base64
import hashlib
import math
import sys
import zlib
from pathlib import Path

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))

from coaster_x87_oracle import CoasterOracle, bits, f32  # noqa: E402
from x86_harness import have_unicorn, seeded  # noqa: E402


def hexes(values) -> str:
    return ' '.join(f'{bits(v):08x}' for v in values)


def curve_rows(oracle: CoasterOracle, rng, count: int) -> list[str]:
    rows = []
    for _ in range(count):
        p0 = [f32(rng.uniform(-100, 1500)) for _ in range(3)]
        p3 = [f32(rng.uniform(-100, 1500)) for _ in range(3)]
        forward = [f32(p0[k] + rng.uniform(-60, 60)) for k in range(3)]
        back = [f32(p3[k] + rng.uniform(-60, 60)) for k in range(3)]
        t = f32(rng.choice([rng.random(), rng.random(), 0.0, 1.0, 0.05, 0.5]))
        inputs = f'{hexes(p0)} | {hexes(forward)} | {hexes(back)} | {hexes(p3)} | {hexes([t])}'
        rows.append(f'point | {inputs} > {hexes(oracle.curve(0x43E1B0, p0, forward, back, p3, t))}')
        rows.append(f'derivative | {inputs} > {hexes(oracle.curve(0x43E320, p0, forward, back, p3, t))}')
    return rows


def chord_rows(oracle: CoasterOracle, rng, count: int) -> list[str]:
    rows = []
    for _ in range(count):
        points = [[f32(rng.uniform(-100, 1500)) for _ in range(3)] for _ in range(3)]
        back, forward, _, _ = oracle.chord_scenario('mid', points, None, 1, 1)
        rows.append(f'chord | {hexes(points[0])} | {hexes(points[1])} | {hexes(points[2])} > {hexes([back, forward])}')
    return rows


def knot_rows(oracle: CoasterOracle, rng, count: int) -> list[str]:
    rows = []
    for _ in range(count):
        position = [f32(rng.uniform(-100, 1500)) for _ in range(3)]
        direction = [rng.uniform(-1, 1) for _ in range(3)]
        norm = math.sqrt(sum(x * x for x in direction))
        direction = [f32(x / norm) for x in direction]
        curviness = f32(rng.uniform(0.4, 3.5))
        back_chord = f32(rng.uniform(0.5, 220))
        forward_chord = f32(rng.uniform(0.5, 220))
        back, forward = oracle.knot_controls(curviness, position, direction, back_chord, forward_chord)
        rows.append(f'knot | {hexes([curviness])} | {hexes(position)} | {hexes(direction)} | {hexes([back_chord, forward_chord])} > {hexes(back)} {hexes(forward)}')
    return rows


def orient_rows(oracle: CoasterOracle, rng, count: int) -> list[str]:
    rows = []
    for _ in range(count):
        tangent = [f32(rng.uniform(-80, 80)) for _ in range(3)]
        if rng.random() < 0.15:
            tangent[1] = f32(rng.uniform(-3, 3))
        rows.append(f'orient | {hexes(tangent)} > {hexes(oracle.orientation(tangent))}')
    return rows


def tangent_rows(oracle: CoasterOracle, rng, count: int) -> list[str]:
    def vec3():
        return [f32(rng.uniform(0, 640)), f32(rng.uniform(0, 120)), f32(rng.uniform(0, 640))]

    def around(origin, scale):
        """A point at a random direction and distance from origin, with the height changed less than the ground distance."""
        angle = rng.uniform(0, 2 * math.pi)
        distance = rng.uniform(6, scale)
        return [f32(origin[0] + distance * math.sin(angle)), f32(origin[1] + rng.uniform(-14, 14)), f32(origin[2] + distance * math.cos(angle))]

    rows = []
    for index in range(count):
        position = vec3()
        kind = rng.random()
        previous = around(position, 60)
        following = around(position, 60)
        if kind < 0.18:
            # near straight: the next node roughly opposite the previous one
            direction = [position[k] - previous[k] for k in range(3)]
            wobble = rng.choice([0.0, 0.002, 0.02, 0.1])
            following = [f32(position[k] + direction[k] * rng.uniform(0.6, 1.4) + rng.uniform(-wobble, wobble) * 10) for k in range(3)]
        elif kind < 0.24:
            following = [f32(v) for v in position]
            following[1] = f32(position[1] + rng.uniform(-3, 3))
        has_previous = rng.random() > 0.1
        has_next = rng.random() > 0.1
        if rng.random() < 0.2:
            has_previous = has_next = True
        # node flags 0x80000 and 0x40000 drop a link that exists, 0x100000 takes the head's next node as the next one
        drop_previous = has_previous and rng.random() < 0.08
        drop_next = has_next and rng.random() < 0.08
        use_head_next = rng.random() < 0.06
        head_next = vec3() if use_head_next else None
        twice = around(previous, 40) if has_previous and rng.random() < 0.8 else None
        definition = None if rng.random() < 0.2 else (rng.random() < 0.4, f32(rng.uniform(-0.999, -0.6)))
        reversed_ = rng.random() < 0.3
        loop_node = rng.random() < 0.25
        previous_loop = rng.choice([None, False, True, False]) if has_previous else None
        next_loop = rng.choice([None, False, True, False]) if has_next else None
        suspended = rng.random() < 0.5
        divisor = f32(rng.uniform(1.5, 3.5))
        curviness = f32(rng.uniform(0.4, 2.5))
        args = dict(position=position, previous=previous if has_previous else None, following=following if has_next else None,
                    previous_of_previous=twice, definition=definition, reversed_=reversed_, loop_node=loop_node, previous_loop=previous_loop,
                    next_loop=next_loop, straight_divisor=divisor, curviness=curviness, suspended=suspended, head_next=head_next,
                    drop_previous=drop_previous, drop_next=drop_next, use_head_next=use_head_next)
        zero = oracle.node_tangent(**args, flags_in=0)
        one = oracle.node_tangent(**args, flags_in=0x10000000)
        loop_text = lambda v: 'n' if v is None else ('1' if v else '0')
        vector_text = lambda v: '-' if v is None else hexes(v)
        definition_text = 'n' if definition is None else f'd {int(definition[0])} {hexes([definition[1]])}'
        masked_previous = None if drop_previous else args['previous']
        masked_next = head_next if use_head_next else (None if drop_next else args['following'])
        masked_twice = None if masked_previous is None else twice
        loop_previous = previous_loop
        inputs = (f'{hexes(position)} | {vector_text(masked_previous)} | {vector_text(masked_next)} | {vector_text(masked_twice)} | {definition_text} | '
                  f'{int(reversed_)} {int(loop_node)} {loop_text(previous_loop)} {loop_text(next_loop)} {int(suspended)} | {hexes([divisor, curviness])}')
        if not zero[4]:
            rows.append(f'tangent | {inputs} > none')
            continue
        flag_zero = (zero[3] >> 28) & 1
        flag_one = (one[3] >> 28) & 1
        verdict = 'unchanged' if (flag_zero, flag_one) == (0, 1) else ('set' if flag_zero else 'clear')
        rows.append(f'tangent | {inputs} > {hexes(list(zero[0]) + list(zero[1]) + [zero[2]])} {verdict}')
    return rows


def bank_rows(oracle: CoasterOracle, rng, count: int) -> list[str]:
    rows = []
    for _ in range(count):
        position = [f32(rng.uniform(0, 640)), f32(rng.uniform(0, 120)), f32(rng.uniform(0, 640))]
        around = lambda: [f32(position[0] + rng.uniform(-60, 60)), f32(position[1] + rng.uniform(-15, 15)), f32(position[2] + rng.uniform(-60, 60))]
        previous, following = around(), around()
        turn = f32(rng.choice([rng.uniform(-math.pi, math.pi), rng.uniform(-0.3, 0.3), 0.0]))
        speed = f32(rng.uniform(0, 60))
        divisor = f32(rng.choice([4.0, rng.uniform(1, 8)]))
        rows.append(f'bank | {hexes(position)} | {hexes(previous)} | {hexes(following)} | {hexes([turn, speed, divisor])} > {hexes([oracle.auto_bank(position, previous, following, turn, speed, divisor)])}')
        per_height, uphill, downhill = f32(rng.uniform(0.5, 3)), f32(rng.uniform(0.3, 1.5)), f32(rng.uniform(0.3, 1.5))
        floor_ = f32(rng.uniform(0, 8))
        y, y_next = f32(rng.uniform(0, 120)), f32(rng.uniform(0, 120))
        if rng.random() < 0.1:
            y_next = y
        rows.append(f'speed | {hexes([per_height, uphill, downhill, speed, floor_, y, y_next])} > {hexes([oracle.speed_estimate(per_height, uphill, downhill, speed, floor_, y, y_next)])}')
    return rows


def loop_rows(oracle: CoasterOracle, rng, count: int) -> list[str]:
    rows = []
    for _ in range(count):
        position = [f32(rng.uniform(0, 640)), f32(rng.uniform(0, 120)), f32(rng.uniform(0, 640))]
        direction = [rng.uniform(-1, 1) for _ in range(3)]
        norm = math.sqrt(sum(x * x for x in direction))
        direction = [f32(x / norm) for x in direction]
        curviness = f32(rng.uniform(0.4, 3.0))
        back_chord, forward_chord = f32(rng.uniform(0.5, 150)), f32(rng.uniform(0.5, 150))
        yaw = f32(rng.choice([rng.uniform(-math.pi, math.pi), rng.uniform(-9, 9), 0.0, math.pi / 2]))
        reversed_ = rng.random() < 0.4
        loop_rotate = f32(rng.choice([0.0, 0.5, rng.random(), rng.uniform(-1, 1)]))
        link = None if rng.random() < 0.1 else [f32(position[0] + rng.uniform(-30, 30)), f32(position[1]), f32(position[2] + rng.uniform(-30, 30))]
        if rng.random() < 0.05 and link is not None:
            link[0], link[2] = position[0], position[2]
        previous_node = None if rng.random() < 0.1 else [f32(v + rng.uniform(-30, 30)) for v in position]
        next_node = None if rng.random() < 0.1 else [f32(v + rng.uniform(-30, 30)) for v in position]
        previous_flags = rng.choice([0, 0x40])
        next_flags = rng.choice([0, 0x40])
        # the side's link is the raw previous node (the next one with node flag 0x80)
        side_link = (next_node if reversed_ else previous_node)
        turn_fraction = oracle.turn_fraction(yaw)
        back, forward = oracle.loop_knot(curviness, position, direction, back_chord, forward_chord, True, reversed_, None, 0, turn_fraction, loop_rotate,
                                         previous_node, previous_flags, next_node, next_flags)
        vector_text = lambda v: '-' if v is None else hexes(v)
        rows.append(f'loopknot | {hexes([curviness, yaw, loop_rotate])} | {hexes(position)} | {hexes(direction)} | {hexes([back_chord, forward_chord])} | {vector_text(previous_node)} {previous_flags >> 6} | '
                    f'{vector_text(next_node)} {next_flags >> 6} | {int(reversed_)} > {hexes([turn_fraction])} {hexes(back)} {hexes(forward)}')
    return rows


def cost_rows(oracle: CoasterOracle, rng, count: int) -> list[str]:
    rows = []
    for _ in range(count):
        t = f32(rng.choice([rng.random(), 0.0, 1.0]))
        low = f32(rng.uniform(0, 20))
        high = f32(low + rng.uniform(0, 40))
        position = [f32(rng.uniform(0, 640)), f32(rng.uniform(0, 640))]
        other = [f32(position[0] + rng.uniform(-90, 90)), f32(position[1] + rng.uniform(-90, 90))]
        unit, length_rate, height_rate = f32(rng.uniform(0, 200)), f32(rng.uniform(0, 30)), f32(rng.uniform(0, 30))
        charge = rng.random() < 0.7
        rows.append(f'cost | {hexes([t, low, high])} | {hexes(position)} | {hexes(other)} | {hexes([unit, length_rate, height_rate])} | {int(charge)} > '
                    f'{hexes([oracle.pylon_cost(t, low, high, position, other, unit, length_rate, height_rate, charge)])}')
    return rows


def sampler_rows(oracle: CoasterOracle, rng, count: int) -> list[str]:
    def knot(position, direction):
        # the knot record holds the node's tangent (not a unit vector): the node pass normalises it (0x4321c0)
        scale = rng.uniform(5, 60) / math.sqrt(sum(x * x for x in direction))
        return (tuple(f32(v) for v in position), tuple(f32(x * scale) for x in direction))

    rows = []
    for _ in range(count):
        size = rng.randint(4, 8)
        base = [rng.uniform(100, 500), rng.uniform(5, 30), rng.uniform(100, 500)]
        nodes = []
        for index in range(size):
            if index == 0:
                position = base
            elif index == size - 1:
                position = nodes[0][0][0]
            else:
                position = [base[0] + rng.uniform(-90, 90), max(2, base[1] + rng.uniform(-8, 30)), base[2] + rng.uniform(-90, 90)]
            direction = [rng.uniform(-1, 1), rng.uniform(-0.4, 0.4), rng.uniform(-1, 1)]
            knots = [knot(position, direction)]
            if 0 < index < size - 1 and rng.random() < 0.3:
                # a pylon with a tag table: three knots a few units apart along its direction
                unit = knots[0][1]
                knots = [(tuple(f32(position[k] + unit[k] / 12 * off) for k in range(3)), unit) for off in (-4.0, 0.0, 4.0)]
            nodes.append(knots)
        curviness = f32(rng.uniform(0.6, 2.0))
        multi = any(len(k) > 1 for k in nodes)
        samples, n, end, coordinates, indexes, _ = oracle.sampler(nodes, curviness, model_controls=multi)
        flat = [k for knots in nodes for k in knots]
        knot_text = ' '.join(hexes(list(p) + list(d)) for p, d in flat)
        counts = ' '.join(str(len(k)) for k in nodes)
        controls = ' '.join(hexes(list(c[0]) + list(c[1])) for c in oracle.last_controls) + ' ' + ' '.join(hexes(list(c[0]) + list(c[1])) for c in oracle.last_controls2)
        rows.append(f'sampler | {hexes([curviness])} | {counts} | {knot_text} > {n} {hexes([end])} | {hexes(coordinates)} | {controls} | '
                    + ' '.join(f"{hexes(list(pos) + [yaw, pitch, roll, t])} {k}" for pos, yaw, pitch, roll, t, k in samples[:n]))
    return rows


def prism_rows(oracle: CoasterOracle, rng, count: int) -> list[str]:
    def sample(position, direction_hint=None):
        yaw = f32(rng.uniform(-math.pi, math.pi))
        pitch = f32(rng.uniform(-1.2, 1.2))
        roll = f32(rng.choice([rng.uniform(-math.pi, math.pi), rng.uniform(-0.5, 0.5), 0.0]))
        return ([f32(v) for v in position], yaw, pitch, roll)

    rows = []
    for _ in range(count):
        size = rng.choice([5, 5, 5, 3, 4, 6])
        points = [(f32(rng.uniform(-10, 10)), f32(rng.uniform(-6, 12))) for _ in range(size)] if rng.random() < 0.4 else [(4.0, 8.0), (4.0, 0.0), (0.0, -3.0), (-4.0, -1.0), (-4.0, 8.0)]
        base = [rng.uniform(-300, 600), rng.uniform(0, 120), rng.uniform(-300, 600)]
        a0 = sample(base)
        step = [rng.uniform(-3, 3) for _ in range(3)]
        a1 = sample([base[k] + step[k] for k in range(3)])
        # the second link's samples stand within a few units, so that the prisms cross now and then
        offset = [rng.uniform(-7, 7) for _ in range(3)]
        b0 = sample([base[k] + offset[k] for k in range(3)])
        step2 = [rng.uniform(-3, 3) for _ in range(3)]
        b1 = sample([base[k] + offset[k] + step2[k] for k in range(3)])
        hit, poses_a, poses_b, sums = oracle.prism([a0, a1], [b0, b1], points)
        text = lambda smp: f'{hexes(smp[0])} {hexes(smp[1:])}'
        rows.append(f'prism | {hexes([c for pt in points for c in pt])} | {text(a0)} | {text(a1)} | {text(b0)} | {text(b1)} > {int(hit)} | '
                    f'{hexes([c for pose in poses_a for c in pose])} | {hexes([c for pose in poses_b for c in pose])} | {hexes(sums)}')
    return rows


def table_rows(oracle: CoasterOracle) -> list[str]:
    entries = oracle.table_bytes[0x0C:0x0C + 4 * 4096]
    return [f'table | {hashlib.sha256(entries).hexdigest()}']


def render() -> str:
    oracle = CoasterOracle()
    rng = seeded('coast-x87-golden-v1')
    lines = table_rows(oracle) + curve_rows(oracle, rng, 100) + chord_rows(oracle, rng, 60) + knot_rows(oracle, rng, 120) + orient_rows(oracle, rng, 120) + tangent_rows(oracle, rng, 300) + bank_rows(oracle, rng, 100) + loop_rows(oracle, rng, 200) + cost_rows(oracle, rng, 120) + sampler_rows(oracle, rng, 6) + prism_rows(oracle, rng, 100)
    packed = base64.b64encode(zlib.compress('\n'.join(lines).encode(), 9)).decode()
    out = ['// Generated by tools/ppc-analysis/oracle_x86/coaster_x87_golden.py from tp.exe\'s own coaster routines run in the x86 oracle at the control word 0x7F (docs/reverse/ORACLE.md, X40; COAST-032).',
           '// Synthetic knots, no original data. One row per line when unpacked, zlib-compressed and base64-coded: `kind | inputs > outputs`, every value the 8 hex digits of a single.',
           'namespace OpenTPW.Tests;', '', 'internal static class CoasterX87Golden', '{',
           f'\t/// <summary>The oracle\'s {len(lines)} rows, packed.</summary>', '\tpublic const string Packed =']
    chunk = 120
    parts = [packed[i:i + chunk] for i in range(0, len(packed), chunk)]
    out += [f'\t\t"{part}"' + (' +' if i + 1 < len(parts) else ';') for i, part in enumerate(parts)]
    out += ['', f'\tpublic const int RowCount = {len(lines)};', '}', '']
    return '\n'.join(out)


def main() -> int:
    if not have_unicorn():
        print('needs Unicorn and OPENTPW_TPW_EXE', file=sys.stderr)
        return 2
    if len(sys.argv) == 3 and sys.argv[1] == '--check':
        if render() != Path(sys.argv[2]).read_text():
            print('CoasterX87Golden.cs differs from the oracle output', file=sys.stderr)
            return 1
        return 0
    print(render(), end='')
    return 0


if __name__ == '__main__':
    sys.exit(main())

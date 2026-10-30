"""Golden rows of tp.exe's cross-section run builder 0x4402D0 run in the x86 oracle at the thread's control word (docs/reverse/ORACLE.md, finding X42; COAST-026).

Each row is `runs | curviness | node knot counts | knots | sections | selects > run table`: a synthetic chain is sampled by the real sampler 0x440C10, the real run builder walks the
sample coordinate over it with a synthetic select table, and the run table (key, position, weight, section) up to the sentinel is read back. Every float is the 8 hex digits of a single; the
selects' windows are the sam's degrees, which the C# side turns into radians exactly as 0x42D130 does. OpenTPW.Tests replays every row on the C# port and compares the bits.

Usage: python3 -I tools/ppc-analysis/oracle_x86/coaster_runs_golden.py > source/OpenTPW.Tests/CoasterRunsGolden.cs   (needs Unicorn and OPENTPW_TPW_EXE; --check FILE compares)
"""
from __future__ import annotations

import base64
import math
import sys
import zlib
from pathlib import Path

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))

from coaster_x87_oracle import CoasterOracle, bits, f32  # noqa: E402
from x86_harness import MemoryFault, have_unicorn, seeded  # noqa: E402

DEGREES = f32(0.017453292)
FLOAT_MAX = f32(3.4028234663852886e38)


def hexes(values) -> str:
    return ' '.join(f'{bits(v):08x}' for v in values)


def windows_of(ranges):
    """The eight window singles of 0x42D130: -FLT_MAX..FLT_MAX unless a pair has a nonzero end, then each value times the degree single."""
    out = []
    for low, high in ranges:
        if low != 0 or high != 0:
            out += [f32(low * DEGREES), f32(high * DEGREES)]
        else:
            out += [-FLOAT_MAX, FLOAT_MAX]
    return out


def scenario(rng):
    """A random chain, a select table of one to three plain selects and up to two transition selects, and section lengths."""
    size = rng.randint(6, 12)
    base = [rng.uniform(100, 500), rng.uniform(5, 30), rng.uniform(100, 500)]
    nodes = []

    def knot(position, direction):
        scale = rng.uniform(5, 60) / math.sqrt(sum(x * x for x in direction))
        return (tuple(f32(v) for v in position), tuple(f32(x * scale) for x in direction))

    for index in range(size):
        if index == 0:
            position = base
        elif index == size - 1:
            position = nodes[0][0][0]
        else:
            position = [base[0] + rng.uniform(-90, 90), max(2, base[1] + rng.uniform(-30, 60)), base[2] + rng.uniform(-90, 90)]
        direction = [rng.uniform(-1, 1), rng.uniform(-0.6, 0.6), rng.uniform(-1, 1)]
        nodes.append([knot(position, direction)])
    plain = rng.randint(1, 3)
    transitioning = plain >= 2 and rng.random() < 0.85
    lengths = [0.0] * plain
    transitions = []
    if transitioning:
        # the section lists of the flume: a blend there (+length) and back (-length), each between two plain sections
        a, b = rng.sample(range(plain), 2)
        forward = f32(rng.choice([1.0, 3.5, 6.0, 9.0, 14.0, 20.0, 30.0]))
        backward = f32(-rng.choice([0.5, 1.0, 2.0, 3.5, 6.0, 9.0, 14.0]))
        lengths += [forward, backward] if rng.random() < 0.5 else [backward, forward]
        transitions = [(plain, a, b), (plain + 1, b, a)]
    selects = []
    for index in range(plain):
        ranges = [(0.0, 0.0)] * 4
        if index < plain - 1:
            pick = rng.choice(['pitch', 'pitch', 'bank', 'direction', 'both'])
            low = -rng.choice([90, 20, 8, 3, 2]) + rng.choice([0, 0, 1, -1])
            high = float(rng.choice([-1, 1, 2, 4]))
            if pick in ('pitch', 'both'):
                ranges[2] = (float(low), float(high))
            if pick in ('bank', 'both'):
                ranges[3] = (-rng.choice([2.0, 20.0, 80.0]), rng.choice([2.0, 20.0, 80.0]))
            if pick == 'direction':
                ranges[1] = (-180.0, rng.choice([0.0, 30.0, 90.0]))
        selects.append((ranges, index, None, None))
    for answer, source, target in transitions:
        selects.append(([(0.0, 0.0)] * 4, answer, source, target))
    return nodes, f32(rng.uniform(0.7, 1.8)), transitioning, lengths, selects


def rows(oracle: CoasterOracle, rng, count: int) -> list[str]:
    out = []
    merged = 0
    attempts = 0
    plain = 0
    while plain < count - count // 4 or merged < count // 4:
        attempts += 1
        if attempts > 4000:
            raise RuntimeError('the scenarios do not reach the merge path')
        nodes, curviness, transitioning, lengths, selects = scenario(rng)
        table = [(windows_of(ranges), answer, source, target) for ranges, answer, source, target in selects]
        try:
            samples, n, end, coordinates, indexes, _ = oracle.sampler(nodes, curviness, extrusion=(table, [f32(v) for v in lengths], transitioning))
        except MemoryFault:
            # a blend that reaches past either end of the sample list makes the original's key walk read outside it: such a scenario has no defined answer
            continue
        runs = oracle.last_runs
        # a run position outside the sampled track makes the original read outside the sample list (its key walk): leave such scenarios out
        if any(position < 0 or position >= end for _, position, _, _ in runs[:-1]):
            continue
        if len(runs) < 3 and rng.random() < 0.6:
            continue
        # the rows the checks of the C# side count: a blend that meets the next change (a weight between 0 and 1) is the rare path, so rows with it are kept first
        has_merge = any(0 < weight < 1 for _, _, weight, _ in runs)
        if not has_merge and plain >= count - count // 4:
            continue
        if has_merge and merged >= count // 4:
            continue
        merged += has_merge
        plain += not has_merge
        flat = [k for knots in nodes for k in knots]
        knot_text = ' '.join(hexes(list(p) + list(d)) for p, d in flat)
        counts = ' '.join(str(len(k)) for k in nodes)
        section_text = ' '.join(f'{bits(v):08x}' for v in lengths)
        select_text = ' ; '.join(f'{hexes([v for pair in ranges for v in pair])} : {answer} {-1 if source is None else source} {-1 if target is None else target}' for ranges, answer, source, target in selects)
        run_text = ' '.join(f'{hexes([key, position, weight])} {section}' for key, position, weight, section in runs)
        out.append(f'runs | {hexes([curviness])} | {counts} | {knot_text} | {section_text} | {select_text} > {len(runs)} | {run_text}')
    return out


def render() -> str:
    oracle = CoasterOracle(budget=80_000_000)
    rng = seeded('coast-runs-golden-v1')
    lines = rows(oracle, rng, 60)
    packed = base64.b64encode(zlib.compress('\n'.join(lines).encode(), 9)).decode()
    out = ['// Generated by tools/ppc-analysis/oracle_x86/coaster_runs_golden.py from tp.exe\'s cross-section run builder 0x4402D0 run in the x86 oracle at the control word 0x7F (docs/reverse/ORACLE.md, X42; COAST-026).',
           '// Synthetic knots and select tables, no original data. One row per line when unpacked, zlib-compressed and base64-coded: `kind | inputs > outputs`, every float the 8 hex digits of a single.',
           'namespace OpenTPW.Tests;', '', 'internal static class CoasterRunsGolden', '{',
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
            print('CoasterRunsGolden.cs differs from the oracle output', file=sys.stderr)
            return 1
        return 0
    print(render(), end='')
    return 0


if __name__ == '__main__':
    sys.exit(main())

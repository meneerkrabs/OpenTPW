"""Golden rows of tp.exe's coaster rating pass run in the x86 oracle at the thread's control word (docs/reverse/ORACLE.md, finding X44; COAST-032, COAST-014).

Every row is `kind | inputs > outputs` with the singles written as 8 hex digits of their bit patterns (integers in decimal). The oracle's own synthetic inputs (random type records, samples,
running states) are run through the real routines in the Unicorn sandbox at CW 0x7F; OpenTPW.Tests replays every row on the C# port and compares the bits.

Usage: python3 -I tools/ppc-analysis/oracle_x86/coaster_rating_golden.py > source/OpenTPW.Tests/CoasterRatingGolden.cs   (needs Unicorn and OPENTPW_TPW_EXE; --check FILE compares)
"""
from __future__ import annotations

import base64
import math
import sys
import zlib
from pathlib import Path

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))

from coaster_rating_oracle import DEF_FIELDS, RatingOracle  # noqa: E402
from coaster_x87_oracle import bits, f32  # noqa: E402
from x86_harness import have_unicorn, seeded  # noqa: E402

# the order the rows write the type record's floats in
DEF_ORDER = list(DEF_FIELDS)


def hexes(values) -> str:
    return ' '.join(f'{bits(v):08x}' for v in values)


def random_definition(rng) -> dict:
    return {
        'acceleration_per_height': f32(rng.uniform(0.05, 3.0)), 'uphill': f32(rng.uniform(0.3, 1.5)), 'downhill': f32(rng.uniform(0.5, 2.0)),
        'winch': f32(rng.uniform(2, 15)), 'friction': f32(rng.uniform(0.5, 1.2)),
        'gravity_x': f32(rng.uniform(-1, 1)), 'gravity_y': f32(-rng.uniform(0.2, 12)), 'gravity_z': f32(rng.uniform(-1, 1)), 'force_multiplier': f32(rng.uniform(0.05, 0.6)),
        'initial_sickness': f32(rng.uniform(0, 20)), 'initial_excitement': f32(rng.uniform(0, 20)),
        'gforce_sick': f32(rng.uniform(0.1, 2)), 'upside_sick': f32(rng.uniform(0.5, 6)), 'speed': f32(rng.uniform(0.02, 0.4)), 'gforce_excite': f32(rng.uniform(0.2, 3)),
        'bank_excite': f32(rng.uniform(0.5, 8)), 'xg_uncomf': f32(rng.uniform(0.2, 3)), 'inverted_uncomf': f32(rng.uniform(0.5, 5)),
        'normal_start': f32(rng.uniform(0.2, 3)), 'terror_start': f32(rng.uniform(0.5, 6)), 'normal_sustain': f32(rng.uniform(1, 40)), 'terror_sustain': f32(rng.uniform(1, 40)),
    }


def definition_text(definition: dict) -> str:
    return hexes([definition[name] for name in DEF_ORDER])


def step_rows(oracle: RatingOracle, rng, count: int) -> list[str]:
    rows = []
    for _ in range(count):
        definition = random_definition(rng)
        if rng.random() < 0.3:
            # discomfort and screams are most interesting when the excitement stays small
            definition['gforce_excite'] = f32(rng.uniform(0.01, 0.3))
            definition['bank_excite'] = f32(rng.uniform(0.01, 0.5))
        half_pi = math.pi / 2
        bank = rng.choice([rng.uniform(-3.2, 3.2), rng.uniform(-3.2, 3.2), half_pi, -half_pi, f32(half_pi) + 1e-6, 0.0, rng.uniform(-0.3, 0.3)])
        bank = f32(bank)
        force = [f32(rng.uniform(-3, 3)) for _ in range(3)]
        if rng.random() < 0.3:
            force[1] = f32(rng.uniform(-0.3, 0.3))
        if rng.random() < 0.1:
            force[1] = f32(0.1)
        speed = f32(rng.uniform(0, 60))
        state = {'sickness': f32(rng.uniform(0, 30)), 'peak_sickness': f32(rng.uniform(0, 40)), 'excitement': f32(rng.uniform(0, 40)), 'peak_excitement': f32(rng.uniform(0, 50)),
                 'previous_excitement': f32(rng.uniform(0, 40)), 'normal': rng.randrange(2), 'terror': rng.randrange(2)}
        if rng.random() < 0.3:
            state['previous_excitement'] = f32(state['excitement'] * 0.5 + rng.uniform(-1, 1))
        index = rng.choice([1, 2, 3, 4, 17])
        flags = rng.choice([0, 8, 0xFF, 0x1F8, 7])
        out = oracle.step(definition, state, index, speed, bank, force, flags)
        inputs = (f'{definition_text(definition)} | {hexes([state[name] for name in oracle.STATE])} {state["normal"]} {state["terror"]} | {index} {flags} | {hexes([speed, bank])} | {hexes(force)}')
        rows.append(f'step | {inputs} > {hexes([out[name] for name in oracle.STATE])} {out["normal"]} {out["terror"]} {out["flags"]}')
    return rows


def final_rows(oracle: RatingOracle, rng, count: int) -> list[str]:
    rows = []
    for _ in range(count):
        kind = rng.random()
        if kind < 0.25:
            state = {'sickness': f32(rng.uniform(-30, 200)), 'peak_sickness': f32(rng.uniform(-30, 200)), 'excitement': f32(rng.uniform(-30, 200)), 'peak_excitement': f32(rng.uniform(-30, 200))}
        elif kind < 0.4:
            state = {'sickness': f32(rng.uniform(0, 40)), 'peak_sickness': f32(rng.choice([-0.0, 0.0, rng.uniform(-1, 1)])), 'excitement': f32(rng.choice([0.0, -0.0, 0.0])),
                     'peak_excitement': f32(rng.choice([-0.0, 0.0, 0.0]))}
        else:
            state = {'sickness': f32(rng.uniform(0, 30)), 'peak_sickness': f32(rng.uniform(0, 40)), 'excitement': f32(rng.uniform(0, 60)), 'peak_excitement': f32(rng.uniform(0, 50))}
        state['peak_sickness'] = state.get('peak_sickness', 0.0)
        state['previous_excitement'] = 0.0
        flags = [rng.choice([0, 0x40, 0x80, 0xC0, 0x1C0, 0x140, 0x180]) for _ in range(rng.randrange(0, 9))]
        spacing = f32(rng.uniform(0.5, 6))
        end = f32(rng.uniform(1, 400))
        max_height = f32(rng.uniform(0, 120))
        max_speed = f32(rng.uniform(0, 80))
        out = oracle.final(state, max_height, max_speed, spacing, end, flags)
        inputs = f'{hexes([state[name] for name in oracle.STATE])} | {hexes([max_height, max_speed, spacing, end])} | {" ".join(str(f) for f in flags)}'
        rows.append(f'final | {inputs} > {hexes([out[name] for name in ("sickness", "excitement", "length", "max_height", "max_speed")])} {out["sections"]}')
    return rows


def track_samples(rng, count: int, spacing: float, bank_range: float) -> list[dict]:
    heading = rng.uniform(0, 2 * math.pi)
    x, y, z = rng.uniform(0, 300), rng.uniform(5, 40), rng.uniform(0, 300)
    slope = rng.uniform(-0.4, 0.4)
    samples = []
    nodes = max(2, count // rng.choice([2, 3, 4]))
    for index in range(count):
        heading += rng.uniform(-0.12, 0.12)
        slope = max(-1.2, min(1.2, slope + rng.uniform(-0.15, 0.15)))
        x += spacing * math.cos(heading) * math.cos(slope)
        z += spacing * math.sin(heading) * math.cos(slope)
        y = max(-5.0, y + spacing * math.sin(slope))
        samples.append({'position': [f32(x), f32(y), f32(z)], 'yaw': f32(heading), 'pitch': f32(slope),
                        'bank': f32(rng.choice([rng.uniform(-bank_range, bank_range), rng.uniform(-0.6, 0.6), 0.0])), 'flags': rng.choice([0, 0, 0, 8, 6]),
                        'node': min(nodes - 1, index * nodes // count)})
    return samples


def pass_rows(oracle: RatingOracle, rng, count: int) -> list[str]:
    rows = []
    for number in range(count):
        definition = random_definition(rng)
        trough = rng.random() < 0.3
        suspended = rng.random() < 0.35
        sample_count = rng.choice([3, 4, 5, 8, 14, 25, 40, 60]) if number > 3 else [3, 4, 5, 8][number]
        spacing = f32(rng.choice([3.0, 3.0, rng.uniform(0.5, 6)]))
        cap = f32(rng.uniform(10, 90))
        samples = track_samples(rng, sample_count, spacing, rng.choice([0.5, 2.0, 3.1]))
        end = f32(sample_count - 1 - rng.choice([0, rng.random(), 2 * rng.random()]))
        tail = rng.randrange(1, sample_count)
        max_trains = rng.choice([1, 2, 4, 6, 6])
        release = rng.choice([15000, 30000, 12500, 20000, 7777])
        result, outputs, winch, captured = oracle.rate(definition, samples, spacing, cap, end, tail, trough=trough, suspended=suspended, max_trains=max_trains, release_rate=release,
                                                       trains=True, nodes=max(s['node'] for s in samples) + 1)
        sample_text = ' ; '.join(f'{hexes(s["position"] + [s["yaw"], s["pitch"], s["bank"]])} {s["flags"]} {s["node"]}' for s in samples)
        inputs = (f'{definition_text(definition)} | {int(trough)} {int(suspended)} {max_trains} {release} | {hexes([spacing, cap, end])} {tail} | {sample_text}')
        out_text = ' ; '.join(f'{o["flags"]} {hexes([o["speed"], o["bank"]] + list(o["force"]))}' for o in outputs)
        results = hexes([result[name] for name in ('sickness', 'excitement', 'length', 'max_height', 'max_speed', 'braking_start')])
        rows.append(f'pass | {inputs} > {out_text} | {"".join("1" if mark else "0" for mark in winch)} | {results} | {captured["count"]} {hexes([captured["lap"]])}')
    return rows


def train_count_rows(oracle: RatingOracle, rng, count: int) -> list[str]:
    """The train count and the lap time of rating passes whose lap time is large, tiny or odd (the count's clamps and the overflow of the truncation)."""
    rows = []
    for _ in range(count):
        definition = random_definition(rng)
        definition['winch'] = f32(rng.choice([0.0001, 0.5, 4.0, 1e-9, 12.0]))
        definition['uphill'] = f32(rng.uniform(0.0, 0.2))
        sample_count = rng.choice([3, 5, 12])
        spacing = f32(rng.choice([3.0, 0.01, 40.0]))
        samples = track_samples(rng, sample_count, 3.0, 1.0)
        release = rng.choice([1, 15000, 1000, 0, 4_000_000_000, 123])
        max_trains = rng.choice([0, 1, 3, 6, 255])
        cap = f32(60.0)
        result, outputs, winch, captured = oracle.rate(definition, samples, spacing, cap, f32(sample_count - 1), 1, trough=False, suspended=False, max_trains=max_trains,
                                                       release_rate=release, trains=True, nodes=max(s['node'] for s in samples) + 1)
        sample_text = ' ; '.join(f'{hexes(s["position"] + [s["yaw"], s["pitch"], s["bank"]])} {s["flags"]} {s["node"]}' for s in samples)
        inputs = f'{definition_text(definition)} | 0 0 {max_trains} {release} | {hexes([spacing, cap, f32(sample_count - 1)])} 1 | {sample_text}'
        out_text = ' ; '.join(f'{o["flags"]} {hexes([o["speed"], o["bank"]] + list(o["force"]))}' for o in outputs)
        rows.append(f'pass | {inputs} > {out_text} | {"".join("1" if mark else "0" for mark in winch)} | '
                    f'{hexes([result[name] for name in ("sickness", "excitement", "length", "max_height", "max_speed", "braking_start")])} | {captured["count"]} {hexes([captured["lap"]])}')
    return rows


def place_rows(oracle: RatingOracle, rng, count: int) -> list[str]:
    """The train placement after the pass (0x43c1f0 and the layout 0x43cbda..): trains spaced by the release time along the rated sample speeds, laid out from the last car."""
    rows = []
    for _ in range(count):
        definition = random_definition(rng)
        sample_count = rng.choice([8, 14, 25, 40, 60])
        spacing = f32(rng.choice([3.0, 3.0, rng.uniform(0.5, 6)]))
        cap = f32(rng.uniform(10, 90))
        samples = track_samples(rng, sample_count, spacing, 1.0)
        end = f32(sample_count - 1 - rng.choice([0, rng.random(), 2 * rng.random()]))
        cars = [dict(front=f32(rng.uniform(0.5, 5)), rear=f32(rng.uniform(0.5, 5)), offset=f32(0)) for _ in range(rng.choice([1, 2, 3, 4, 6]))]
        reference = (len(cars) - 1) >> 1
        running = 0.0
        for car in range(reference - 1, -1, -1):
            running = f32(running + f32(rng.uniform(2, 8)))
            cars[car]['offset'] = running
        running = 0.0
        for car in range(reference + 1, len(cars)):
            running = f32(running - f32(rng.uniform(2, 8)))
            cars[car]['offset'] = running
        trains = rng.choice([1, 2, 3, 5])
        release = rng.choice([1500, 3000, 7777, 20000, 30000])
        lead = f32(rng.choice([rng.uniform(0, sample_count - 1), rng.uniform(-6, 0), 0.0]))
        spec = dict(count=trains, reference=reference, lead=lead, cars=cars)
        oracle.rate(definition, samples, spacing, cap, end, 1, trough=False, suspended=False, max_trains=trains, release_rate=release, trains=True, nodes=max(s['node'] for s in samples) + 1, train_spec=spec)
        # a car in the last segment reads the sampler's copy of the last sample past the list (slot n, copied before the rating: its speed is not a rated one), which the C# port wraps: leave those rows out
        if any(coordinate >= sample_count - 1 or coordinate < 0 for t in oracle.last_trains for coordinate in t['cars']):
            continue
        sample_text = ' ; '.join(f'{hexes(s["position"] + [s["yaw"], s["pitch"], s["bank"]])} {s["flags"]} {s["node"]}' for s in samples)
        car_text = ' ; '.join(hexes([c['front'], c['rear'], c['offset']]) for c in cars)
        inputs = f'{definition_text(definition)} | {trains} {release} {reference} | {hexes([spacing, cap, end, lead])} | {car_text} | {sample_text}'
        out = ' ; '.join(f'{hexes([t["coordinate"], t["position"], t["speed"], t["front"], t["rear"]] + t["cars"])}' for t in oracle.last_trains)
        rows.append(f'place | {inputs} > {out}')
    return rows


def tick_rows(oracle: RatingOracle, rng, count: int) -> list[str]:
    """The motion tick 0x43CE20 over a default four-car train fleet (offsets 7, 0, -7, -14 over spacing, extents 3.5): several ticks of a few milliseconds and now and then a long one,
    with the presentation callees stubbed. Rows list the trains' position, speed, flags (0x40 and 0x80), front and rear after every tick."""
    rows = []
    car_geometry = [(f32(7.0), f32(3.5)), (f32(0.0), f32(3.5)), (f32(-7.0), f32(3.5)), (f32(-14.0), f32(3.5))]
    for _ in range(count):
        definition = random_definition(rng)
        definition['min_speed'] = f32(rng.uniform(0.5, 6))
        definition['winch'] = f32(rng.uniform(2, 12))
        sample_count = rng.choice([20, 30, 45, 60])
        spacing = f32(rng.choice([3.0, 3.0, rng.uniform(1, 5)]))
        total = f32(sample_count - 1 - rng.choice([0, rng.random()]))
        samples = []
        y = rng.uniform(5, 40)
        heading = rng.uniform(0, 6.28)
        x, z = rng.uniform(0, 300), rng.uniform(0, 300)
        for index in range(sample_count):
            heading += rng.uniform(-0.1, 0.1)
            x += spacing * math.cos(heading)
            z += spacing * math.sin(heading)
            y = max(-3.0, y + rng.uniform(-2.5, 2.5))
            samples.append({'position': [f32(x), f32(y), f32(z)], 'speed': f32(rng.uniform(2, 25)), 'friction': rng.random() < 0.12, 'winch': rng.random() < 0.15})
        controller = {'power': f32(1.0), 'cap': f32(rng.uniform(15, 70)), 'braking_start': f32(rng.uniform(0.5, 0.95) * sample_count), 'platform': f32(rng.uniform(0, 0.3) * sample_count)}
        trains = []
        for index in range(rng.choice([1, 2, 3])):
            position = f32(rng.uniform(0, total * 0.97))
            flags = rng.choice([0, 0, 0x80, 0xA0, 0x40, 0xE0])
            train = {'flags': flags, 'position': position, 'speed': f32(rng.choice([rng.uniform(0, 4), rng.uniform(0, 30)])), 'reference': 1,
                     'cars': [{'front': f32(3.5), 'rear': f32(3.5), 'offset': offset} for offset, _ in car_geometry]}
            first = f32(f32(f32(car_geometry[0][0] / spacing) + position) + f32(f32(3.5) / spacing))
            last = f32(f32(f32(f32(-14.0) / spacing) + position) - f32(f32(3.5) / spacing))
            train['front'], train['rear'] = first, last
            trains.append(train)
        steps = [0] + [rng.choice([16, 17, 33, 50, 100, 250, 750]) for _ in range(rng.choice([3, 6, 8]))]
        result = oracle.tick(definition, controller, samples, trains, steps, spacing, total)
        sample_text = ' ; '.join(f'{hexes(s["position"] + [s["speed"]])} {int(s["friction"])} {int(s["winch"])}' for s in samples)
        train_text = ' ; '.join(f'{t["flags"]} {hexes([t["position"], t["speed"], t["front"], t["rear"]])}' for t in trains)
        inputs = (f'{definition_text(definition)} {hexes([definition["min_speed"]])} | {hexes([spacing, total] + [controller[k] for k in ("cap", "braking_start", "platform")])} | {train_text} | '
                  f'{" ".join(str(step) for step in steps)} | {sample_text}')
        outputs = ' / '.join(' ; '.join(f'{t["flags"] & 0xC0} {hexes([t["position"], t["speed"], t["front"], t["rear"]])}' for t in tick['trains']) for tick in result)
        rows.append(f'tick | {inputs} > {outputs}')
    return rows


def cap_rows(oracle: RatingOracle, rng, count: int) -> list[str]:
    rows = []
    for _ in range(count):
        low = f32(rng.uniform(5, 40))
        high = f32(rng.uniform(30, 120))
        setting = rng.choice([0, 100, 50, 60, rng.randrange(0, 101), rng.randrange(0, 101)])
        cap, stored = oracle.speed_cap(low, high, setting)
        rows.append(f'cap | {hexes([low, high])} {setting} > {hexes([cap])} {stored}')
    return rows


def offset_rows(oracle: RatingOracle, rng, count: int) -> list[str]:
    rows = []
    for _ in range(count):
        pairs = [f32(rng.uniform(1, 9)) for _ in range(3)]
        cars = rng.choice([1, 2, 3, 4, 5, 6, 8])
        reference = (cars - 1) >> 1
        spacing = f32(rng.choice([3.0, rng.uniform(0.5, 6)]))
        offsets = oracle.car_offsets(pairs, cars, reference, spacing)
        rows.append(f'offsets | {hexes(pairs)} {cars} {reference} {hexes([spacing])} > {hexes(offsets)}')
    return rows


def render() -> str:
    oracle = RatingOracle()
    rng = seeded('coast-rating-golden-v1')
    lines = step_rows(oracle, rng, 600) + final_rows(oracle, rng, 120) + pass_rows(oracle, rng, 80) + train_count_rows(oracle, rng, 40) + place_rows(oracle, rng, 400) + tick_rows(oracle, rng, 150) + cap_rows(oracle, rng, 120) + offset_rows(oracle, rng, 80)
    packed = base64.b64encode(zlib.compress('\n'.join(lines).encode(), 9)).decode()
    out = ['// Generated by tools/ppc-analysis/oracle_x86/coaster_rating_golden.py from tp.exe\'s own rating pass run in the x86 oracle at the control word 0x7F (docs/reverse/ORACLE.md, X44; COAST-032, COAST-014).',
           '// Synthetic type records and samples, no original data. One row per line when unpacked, zlib-compressed and base64-coded: `kind | inputs > outputs`, every float the 8 hex digits of a single.',
           'namespace OpenTPW.Tests;', '', 'internal static class CoasterRatingGolden', '{',
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
            print('CoasterRatingGolden.cs differs from the oracle output', file=sys.stderr)
            return 1
        return 0
    print(render(), end='')
    return 0


if __name__ == '__main__':
    sys.exit(main())

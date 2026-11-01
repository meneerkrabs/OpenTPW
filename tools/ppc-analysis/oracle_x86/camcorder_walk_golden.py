"""Golden rows of tp.exe's camcorder walk frame (the camera update 0x42B1C0 with the camera flags 2) run in the x86 oracle (docs/reverse/ORACLE.md, finding X58; UI-042).

Each row is one frame: the case (keys, pointer, position, yaw, velocity, zoom, frame time, the seed of the synthetic walk test and ride entrances) and what the update left (position, yaw, velocity, the look
pitch and turn rate, the eye's height, the walk test calls it made, the ride it entered, the camera matrix's forward row). The world is synthetic: a 128 x 128 terrain of ground heights
((7i + 13j) mod 11) / 2 whose cells split along the anti-diagonal when (5i + 3j) mod 7 is 0, a walk test that refuses the map's four edges (0x4D8750's own rule) and any other edge by a hash of (cell, direction, seed), and ride entrances where
(31x + 17z + seed) mod 29 is 0. OpenTPW.Tests replays every row on CamcorderWalk.

Usage: python3 -I tools/ppc-analysis/oracle_x86/camcorder_walk_golden.py > source/OpenTPW.Tests/CamcorderWalkGolden.cs   (needs Unicorn and OPENTPW_TPW_EXE; --check FILE compares)
"""
from __future__ import annotations

import base64
import struct
import sys
import zlib
from pathlib import Path

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))

from camcorder_walk_oracle import ACTIONS, WalkOracle  # noqa: E402
from x86_harness import seeded  # noqa: E402

CASES = 1500


def f32(value: float) -> float:
    return struct.unpack('<f', struct.pack('<f', value))[0]


def bits(value: float) -> int:
    return struct.unpack('<I', struct.pack('<f', value))[0]


def height(i: int, j: int) -> float:
    return ((i * 7 + j * 13) % 11) * 0.5


def cell(i: int, j: int) -> int:
    return 4 if (i * 5 + j * 3) % 7 == 0 else 0


def blocked_by(seed: int):
    def blocked(cx: int, cz: int, direction: int) -> bool:
        # the walk test's own map edge rules (0x4D8750: the byte coordinates 0 and 0x7F) come first
        if (cx == 0 and direction == 3) or (cz == 0 and direction == 0) or (cx == 0x7F and direction == 1) or (cz == 0x7F and direction == 2):
            return True
        value = ((cx & 0xFFFFFFFF) * 73856093 ^ (cz & 0xFFFFFFFF) * 19349663 ^ direction * 83492791 ^ seed) & 0xFFFFFFFF
        return (value & 7) == 0
    return blocked


def ride_by(seed: int):
    return lambda cx, cz: (cx * 31 + cz * 17 + seed) % 29 == 0


def make_case(rng, index: int) -> dict:
    """A case: mostly frames at walking speed on the map, some with a long frame, near an edge or outside the map."""
    seed = rng.randrange(1, 1 << 20)
    keys = {name: rng.random() < 0.3 for name in ('left', 'right', 'forward', 'back')}
    keys['button'] = rng.random() < 0.3
    wide = index % 5 == 0
    x = f32(rng.uniform(-30, 1310) if index % 7 == 0 else rng.uniform(5, 1275))
    z = f32(rng.uniform(-30, 1310) if index % 11 == 0 else rng.uniform(5, 1275))
    # the speeds the walk reaches (0.02 x sqrt(zoom) x 0.1, about 0.02) and above, so that a frame crosses cell edges
    speed = 0.6 if wide else 0.05
    vel = (f32(rng.uniform(-speed, speed)), 0.0, f32(rng.uniform(-speed, speed)))
    last_tick = rng.randrange(1000, 100000)
    elapsed = rng.choice([1, 8, 16, 17, 33, 100, 199, 200, 201, 500]) if wide else rng.randrange(1, 60)
    tick = last_tick + elapsed - 1
    dt = f32(f32(tick + 1) - f32(last_tick))
    if dt > 200.0:
        dt = 100.0
    if dt <= 0.0:
        dt = 1.0
    return {
        'seed': seed, 'keys': keys, 'pointer': (f32(rng.uniform(-1, 1)), f32(rng.uniform(-1, 1))), 'pos': (x, z), 'yaw': f32(rng.uniform(-7, 7)), 'vel': vel,
        'zoom': f32(rng.uniform(70, 130)), 'last_tick': last_tick, 'tick': tick, 'dt': dt, 'index': index,
    }


def run(oracle: WalkOracle, case: dict) -> str:
    keys = case['keys']
    pressed = set()
    for name, actions in ACTIONS.items():
        if keys[name]:
            pressed.add(actions[case['index'] % 2])
    # pointer reference: a 1024 x 768 window, the pointer in pixels
    centre, half = (512.0, 384.0), (512.0, 384.0)
    pointer = (f32(centre[0] + case['pointer'][0] * half[0]), f32(centre[1] - case['pointer'][1] * half[1]))
    result = oracle.run({
        'height': height, 'cell': cell, 'pressed': pressed, 'button': keys['button'], 'pointer': pointer, 'centre': centre, 'half': half, 'pos': case['pos'], 'yaw': case['yaw'],
        'zoom': case['zoom'], 'vel': case['vel'], 'last_tick': case['last_tick'], 'tick': case['tick'],
        'blocked': blocked_by(case['seed']), 'ride': ride_by(case['seed']),
    })
    mask = (keys['left'] << 0) | (keys['right'] << 1) | (keys['forward'] << 2) | (keys['back'] << 3) | (keys['button'] << 4)
    calls = ';'.join(f'{e[1]},{e[2]},{e[3]},{e[5]}' for e in result['log'] if e[0] == 'edge')
    ride = next(((e[1], e[2]) for e in result['log'] if e[0] == 'ride_at'), None)
    forward = result['matrix'][8:11]
    fields = [f'{case["seed"]:x}', f'{mask:x}', f'{bits(pointer[0]):08x}', f'{bits(pointer[1]):08x}', f'{bits(case["pos"][0]):08x}', f'{bits(case["pos"][1]):08x}', f'{bits(case["yaw"]):08x}',
              f'{bits(case["vel"][0]):08x}', f'{bits(case["vel"][2]):08x}', f'{bits(case["zoom"]):08x}', f'{bits(case["dt"]):08x}', '>',
              f'{bits(result["pos"][0]):08x}', f'{bits(result["pos"][1]):08x}', f'{bits(result["yaw"]):08x}', f'{bits(result["vel"][0]):08x}', f'{bits(result["vel"][2]):08x}',
              f'{bits(result["pitch"]):08x}', f'{bits(result["turn"]):08x}', f'{bits(result["eye"][1]):08x}', ' '.join(f'{bits(v):08x}' for v in forward),
              f'{ride[0]},{ride[1]}' if ride else '-', calls or '-']
    return ' '.join(fields)


def render() -> str:
    oracle = WalkOracle()
    rng = seeded('camcorder-walk-golden-v1')
    lines = [run(oracle, make_case(rng, index)) for index in range(CASES)]
    packed = base64.b64encode(zlib.compress('\n'.join(lines).encode(), 9)).decode()
    out = ['// Generated by tools/ppc-analysis/oracle_x86/camcorder_walk_golden.py from tp.exe\'s camera update 0x42B1C0 with the camera flags 2 run in the x86 oracle (docs/reverse/ORACLE.md, X58; UI-042).',
           '// Synthetic terrain, walk test and ride entrances, no original data. One row per line when unpacked, zlib-compressed and base64-coded; see the generator for the fields.',
           'namespace OpenTPW.Tests;', '', 'internal static class CamcorderWalkGolden', '{',
           f'\t/// <summary>The oracle\'s {len(lines)} rows, packed.</summary>', '\tpublic const string Packed =']
    chunk = 120
    parts = [packed[i:i + chunk] for i in range(0, len(packed), chunk)]
    out += [f'\t\t"{part}"' + (' +' if i + 1 < len(parts) else ';') for i, part in enumerate(parts)]
    out += ['', f'\tpublic const int RowCount = {len(lines)};', '}', '']
    return '\n'.join(out)


def main() -> int:
    from x86_harness import have_unicorn
    if not have_unicorn():
        print('needs Unicorn and OPENTPW_TPW_EXE', file=sys.stderr)
        return 2
    if len(sys.argv) == 3 and sys.argv[1] == '--check':
        if render() != Path(sys.argv[2]).read_text():
            print('CamcorderWalkGolden.cs differs from the oracle output', file=sys.stderr)
            return 1
        return 0
    print(render(), end='')
    return 0


if __name__ == '__main__':
    sys.exit(main())

"""Golden person blocks of Theme Park Inc's person serializer (docs/reverse/TPI-NAVIGATOR.md, NV5): Game.exe's 0x609170 with its re-route 0x609b55 on the
map of tpi_wander_golden.py at the game thread's control word 0x7f.

Each case is a person block (396 bytes) whose navigator is a walk of walk_reading.py's on the map (set up to a point and stepped), its cell bytes the
position >> 8, its accurate destination that point or another, its stranded mark, and the other members random bytes of a fixed seed (mSpriteID below
the stubbed sprite count); and a park: the stamp counter and block stamps as the load finds them (a fresh process: 0 and none). The script runs the load
half (whose re-route runs with that counter) and then the save half of the loaded person (which writes it and re-routes again), checks walk_reading.py's
load semantics against the run, and prints the C# rows of ThemeParkIncNativeWalkTests: the block, and per half the counter, the mark and the SHA-256 of
the navigator's record, with the SHA-256 of the bytes the save wrote.

Usage: python3 -I tools/ppc-analysis/oracle_x86/tpi_person_golden.py   (needs Unicorn and OPENTPW_TPI_EXE)
"""
from __future__ import annotations

import hashlib
import random
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))

import tpi_wander_golden as W  # noqa: E402
import walk_reading  # noqa: E402
import x86_targets as T  # noqa: E402
from x86_harness import Image, executable_path, have_unicorn  # noqa: E402

# (name, start x, y in 16.16, mask, the walk's set-up point (x word, y word), its steps, the accurate destination (x word, y word) or None for the
#  set-up's, the mark, the counter and block stamps at the load (None: a fresh process), the counter when the walk was set up)
CASES = [
    ('walking-fresh', (41 << 16) + 0x6000, (40 << 16) + 0x9000, 0, ((54 << 8) | 0x50, (48 << 8) | 0xA0), 10, None, 0, None, 5),
    ('walking-running', (41 << 16) + 0x6000, (40 << 16) + 0x9000, 0, ((54 << 8) | 0x50, (48 << 8) | 0xA0), 10, None, 0, (40, [(3, 2, 33), (2, 2, 39)]), 30),
    ('mark-fresh', (47 << 16) + 0x4000, (40 << 16) + 0xC000, 0, ((44 << 8) | 0x80, (46 << 8) | 0x80), 4, None, 15, None, 12),
    ('mark-held', (41 << 16) + 0x6000, (40 << 16) + 0x9000, 0, ((49 << 8) | 0x80, (44 << 8) | 0x80), 6, None, 15, (20, [(2, 2, 9)]), 12),
    ('mark-released', (41 << 16) + 0x6000, (40 << 16) + 0x9000, 0, ((49 << 8) | 0x80, (44 << 8) | 0x80), 6, None, 15, (20, [(2, 2, 17)]), 12),
    ('mark-above-counter', (41 << 16) + 0x6000, (40 << 16) + 0x9000, 0, ((49 << 8) | 0x80, (44 << 8) | 0x80), 6, None, 90, (20, []), 12),
    ('unreachable', (49 << 16) + 0x5000, (42 << 16) + 0xA000, 0, ((52 << 8) | 0x40, (41 << 8) | 0x40), 8, ((43 << 8) | 0x80, (43 << 8) | 0x80), 0, (10, []), 4),
    ('arrived', (41 << 16) + 0x6000, (40 << 16) + 0x9000, 0, ((42 << 8) | 0x40, (41 << 8) | 0xC0), 60, None, 0, None, 3),
    ('leaving-mask', (46 << 16) + 0x8000, (46 << 16) + 0x4000, 1, ((44 << 8) | 0x20, (41 << 8) | 0x70), 12, None, 0, (7, []), 2),
    ('another-dest', (49 << 16) + 0x5000, (42 << 16) + 0xA000, 0, ((54 << 8) | 0x33, (48 << 8) | 0xC0), 15, ((40 << 8) | 0x05, (49 << 8) | 0x7B), 0, None, 8),
]


def signed_word(word: int) -> int:
    return word - 0x10000 if word & 0x8000 else word


def case_input(case, rng: random.Random) -> dict:
    _, x0, y0, mask, (sx, sy), steps, dest, mark, park, setup_counter = case
    x = W.linked_cells()
    x['walk'] = {'x': x0, 'y': y0, 'vx': 0, 'vy': 0, 'mask': mask, 'max_speed': 13107, 'max_force': 26214}
    x['stamps'] = []
    walk = T.tpi_person_walk(x)
    walk.counter = setup_counter
    walk.setup(walk_reading.s32(signed_word(sx) << 8), walk_reading.s32(signed_word(sy) << 8), False)
    for _ in range(steps):
        walk.step()
    xw, yw = dest or (sx, sy)
    # the person words OpenTPW holds narrower than their dwords take the values the game writes: the wander flag 0 or 1, the turn's start a position near the walk's
    words = {'mX': (walk.x >> 8) & 0xFFFF, 'mY': (walk.y >> 8) & 0xFFFF, 'mAccurateDestX': xw, 'mAccurateDestY': yw, 'mStrandedTime': mark,
             'mSetDestSuccessfully': rng.randrange(2), 'mPreviousX': walk.x - walk.vx, 'mPreviousY': walk.y - walk.vy}
    x['block'] = list(T.tpi_person_block(rng, walk.record(), words))
    counter, stamps = park if park else (0, [])
    x['counter'] = counter
    x['stamps'] = [list(stamp) for stamp in stamps]
    x['behind'] = []
    x['person'] = {'kind': 1, 'mark': 0}
    return x


def main() -> int:
    if not have_unicorn() or executable_path('tpi') is None:
        print('needs Unicorn and OPENTPW_TPI_EXE', file=sys.stderr)
        return 2
    image = Image.load(executable_path('tpi'))
    target = T.TPI_PERSON_REROUTE_TARGET
    rng = random.Random('opentpw-nv5-person-golden')
    sha = lambda data: hashlib.sha256(bytes(data)).hexdigest()
    for case in CASES:
        x = case_input(case, rng)
        sb = target.sandbox(image)
        result = T.run_binary(target, sb, x)
        if result != T.tpi_person_reroute_plan(x):
            print(f'{case[0]}: walk_reading.py disagrees with Game.exe', file=sys.stderr)
            return 1
        loaded, consumed, after_load, saved, written, after_save = result
        assert (loaded, consumed, saved) == (0, walk_reading.PERSON_BYTES, 0)
        counter, stamps = (x['counter'], x['stamps'])
        print(f'\t// {case[0]}')
        print(f'\tnew( "{case[0]}", {counter}, "{";".join(f"{a},{b},{c}" for a, b, c in stamps)}",')
        print(f'\t\t"{bytes(x["block"]).hex()}",')
        print(f'\t\t{after_load[0]}, {after_load[1]}, "{sha(after_load[4])}", "{sha(written)}", {after_save[0]}, {after_save[1]}, "{sha(after_save[4])}" ),')
    return 0


if __name__ == '__main__':
    sys.exit(main())

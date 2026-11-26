"""Golden walk steps of Theme Park Inc's persons (docs/reverse/TPI-NAVIGATOR.md, NV3): Game.exe's walk step 0x60b460 on the map of
tpi_navigator_golden.py at the game thread's control word 0x7f, for a kid, a grown-up guest, a guard and a handyman, each walking the first two
walks of that script with a speed bonus.

Per step the walk step's result, the heading word +0x2c (written on arrival too, D1), the template it queues (D2: only a kid or a guard runs)
and the sprite interval +0x24; '-' where the step writes nothing. The restart test 0x4e4ab0 answers 0, so a walking step always queues its
template. The script checks walk_reading.py's statement against the run and prints the C# rows of ThemeParkIncWalkStepTests: per walk and
person the steps' SHA-256 and the first twelve steps.

Usage: python3 -I tools/ppc-analysis/oracle_x86/tpi_walk_step_golden.py   (needs Unicorn and OPENTPW_TPI_EXE)
"""
from __future__ import annotations

import hashlib
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))

import tpi_navigator_golden as G  # noqa: E402
import x86_targets as T  # noqa: E402
from x86_harness import Image, executable_path, have_unicorn  # noqa: E402

# (kind byte +2, generation +0x2a4, speed bonus word +0xd6)
PERSONS = [(1, 0, 25), (1, 1, 25), (7, 0, 25), (5, 0, 0)]


def step_text(entry) -> str:
    _, result, _, _, _, heading, template, interval, _, _ = entry
    show = lambda value: '-' if value == T.STEP_SENTINEL else f'{value:x}'
    return f'{result}:{show(heading)}:{show(template)}:{show(interval)}'


def main() -> int:
    if not have_unicorn() or executable_path('tpi') is None:
        print('needs Unicorn and OPENTPW_TPI_EXE', file=sys.stderr)
        return 2
    image = Image.load(executable_path('tpi'))
    target = T.TPI_STEP_TARGET
    for index, walk in enumerate(G.WALKS[:2]):
        for kind, generation, bonus in PERSONS:
            x = G.walk_input(walk)
            x['person'] = {'kind': kind, 'generation': generation, 'bonus': bonus, 'state': 0}
            x['restarts'] = [0]
            sb = target.sandbox(image)
            trace = T.run_binary(target, sb, x)
            if trace != T.tpi_step_plan(x):
                print(f'walk {index}, kind {kind}: walk_reading.py disagrees with Game.exe', file=sys.stderr)
                return 1
            steps = [step_text(entry) for entry in trace if entry[0] == 'step']
            digest = hashlib.sha256(','.join(steps).encode()).hexdigest()
            print(f'\tnew( {index}, {kind}, {generation}, {bonus}, "{digest}", "{",".join(steps[:12])}" ),')
    return 0


if __name__ == '__main__':
    sys.exit(main())

"""Theme Park Inc's shop cost of goods 0x5E6260 run in the x86 oracle sandbox and compared with the C# port's formula (ThemeParkIncGoodsCost; ECON-060).

Dev-only tooling like the rest of oracle_x86: Unicorn and the identified Theme Park Inc no-CD Game.exe (OPENTPW_TPI_EXE, SHA-256 pinned) are needed, nothing here is shipped. The routine runs with its
real _ftol (0x86FAB0); the type lookup 0x40C1D0 is stubbed to answer a synthetic type record whose special-ingredient word (+0x218) is set. The thing's cost of goods (+0x88) is set; the three float
arguments are the quality byte, the ingredient byte and the third byte (-1.0 for the shop types 1, 5, 6, 7 and 8).

Usage: python3 -I tools/ppc-analysis/oracle_x86/tpi_goods_cost_oracle.py [cases]   (prints the number of mismatches against the formula, at the control words 0x27F (53 bits) and 0x7F (24 bits))
"""
from __future__ import annotations

import random
import struct
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))

from x86_harness import Image, Sandbox, executable_path  # noqa: E402

ENTRY, LOOKUP, FTOL = 0x5E6260, 0x40C1D0, (0x86FAB0, 0x86FAD7)


def single(value: float) -> float:
    return struct.unpack('<f', struct.pack('<f', value))[0]


def bits(value: float) -> int:
    return struct.unpack('<I', struct.pack('<f', value))[0]


def formula(base: int, quality: float, ingredient: float, third: float, special: int, precision: int) -> int:
    """The routine as the C# port reads it: the terms as singles clamped to +-0.5, the share, then ((terms + 1.0) x cost x share) truncated; 0 becomes 1."""
    step = single(0.005)
    def term(value: float) -> float:
        t = single((value - 50.0) * step)
        return -0.5 if t < -0.5 else (t if t <= 0.5 else 0.5)
    q, i = term(quality), term(ingredient)
    share = 1.0 if third < 0 else single(third * single(0.01))
    if special in (1, 3):
        i = -i
    if precision == 53:
        product = ((i + q) + 1.0) * base * share
    else:
        product = single(single(single(i + q) + 1.0) * base)
        product = single(product * share)
    value = int(product)
    return value if value != 0 else 1


def main(cases: int) -> int:
    path = executable_path('tpi')
    if path is None:
        raise SystemExit('needs OPENTPW_TPI_EXE (tools/ppc-analysis/oracle_x86/README.md)')
    record: dict = {}
    sandbox = Sandbox(Image.load(path), [(ENTRY, 0x5E638A), FTOL], stubs={LOOKUP: lambda sb: (record['info'], 4)})
    rng = random.Random(5)
    failures = 0
    for precision, control in ((53, 0x27F), (24, 0x7F)):
        bad = 0
        for _ in range(cases):
            base = rng.choice([0, 1, 5, 10, 100, rng.randrange(0, 5000)])
            quality = float(rng.choice([50, rng.randrange(0, 256)]))
            ingredient = float(rng.choice([50, rng.randrange(0, 256)]))
            third = float(rng.choice([-1, 50, rng.randrange(0, 101)]))
            special = rng.choice([0, 1, 2, 3, 4])
            sandbox.reset()
            thing = sandbox.object(0x100, {0x88: ('i', base), 0x16: ('H', 7)})
            record['info'] = sandbox.object(0x700, {0x218: ('i', special)})
            sandbox.set_fpcw(control)
            value = sandbox.call(ENTRY, args=(bits(quality), bits(ingredient), bits(third)), this=thing).eax
            value = value - (1 << 32) if value >> 31 else value
            if value != formula(base, quality, ingredient, third, special, precision):
                bad += 1
        print(f'{precision}-bit: {bad} mismatches in {cases} cases')
        failures += bad
    return 1 if failures else 0


if __name__ == '__main__':
    raise SystemExit(main(int(sys.argv[1]) if len(sys.argv) > 1 else 400))

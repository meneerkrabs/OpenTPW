"""Theme Park Inc's scrap percentage of an object that requires watering run in the x86 oracle sandbox and compared with the C# port's reading (ParkEconomy.ThemeParkIncWateringPercent).

Dev-only tooling like the rest of oracle_x86: Unicorn and the identified Theme Park Inc no-CD Game.exe (OPENTPW_TPI_EXE, SHA-256 pinned) are needed, nothing here is shipped. The first branch of
0x5E75F0 runs natively with its real _ftol (0x86FAB0) on a synthetic thing whose flag +0x43 has bit 0x10 set; the script lookup (the thunk 0x40AE89 of 0x6B0740) and the script variable read (the
thunk 0x4078BA of 0x6B0C90) are stubbed to answer the count n. The routine runs at the control word 0x7F (24-bit significand), the game thread's after the Direct3D device started
([EXT:hardware-d3d-path]); the 53-bit word 0x27F is reported beside it. `--golden` prints the native answers for the counts -3..8.

Usage: python3 -I tools/ppc-analysis/oracle_x86/tpi_scrap_percent_oracle.py [cases] [--golden]
"""
from __future__ import annotations

import random
import struct
import sys
from fractions import Fraction
from pathlib import Path

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))

from x86_harness import Image, Sandbox, executable_path  # noqa: E402

ENTRY, BRANCH_END, SCRIPT, VARIABLE = 0x5E75F0, 0x5E765B, 0x40AE89, 0x4078BA
FTOL = (0x86FAB0, 0x86FAD7)
FACTOR = struct.unpack('<f', struct.pack('<f', 33.33333206176758))[0]
MASK = 0xFFFFFFFF


def rnd(value: Fraction, precision: int) -> Fraction:
    """One x87 step's exact result rounded to the control word's significand, round to nearest even."""
    if value == 0:
        return Fraction(0)
    sign = -1 if value < 0 else 1
    value = abs(value)
    exponent = value.numerator.bit_length() - value.denominator.bit_length()
    while Fraction(2) ** exponent > value:
        exponent -= 1
    while Fraction(2) ** (exponent + 1) <= value:
        exponent += 1
    scale = Fraction(2) ** (exponent - precision + 1)
    scaled = value / scale
    whole = scaled.numerator // scaled.denominator
    rest = scaled - whole
    if rest > Fraction(1, 2) or rest == Fraction(1, 2) and whole & 1:
        whole += 1
    return sign * whole * scale


def signed(value: int) -> int:
    value &= MASK
    return value - (1 << 32) if value >> 31 else value


def percent(count: int, precision: int) -> int:
    """fild n, fsubrs 3.0, fmuls 33.333332f, _ftol (the low dword); 3 - n equal to 0.0 returns the truncated product, else 1 + it."""
    remaining = rnd(3 - Fraction(count), precision)
    product = rnd(remaining * Fraction(FACTOR), precision)
    whole = int(product)   # truncation toward zero
    truncated = signed(whole) if -(1 << 63) <= whole < (1 << 63) else 0
    return truncated if remaining == 0 else signed(1 + truncated)


def main(cases: int, golden: bool) -> int:
    path = executable_path('tpi')
    if path is None:
        raise SystemExit('needs OPENTPW_TPI_EXE (tools/ppc-analysis/oracle_x86/README.md)')
    state = {'count': 0}
    sandbox = Sandbox(Image.load(path), [(ENTRY, BRANCH_END), FTOL], stubs={SCRIPT: lambda sb: (0x1234, 0), VARIABLE: lambda sb: (state['count'] & MASK, 0)})
    rng = random.Random(41)
    counts = list(range(-3, 9)) + [0x7FFFFFFF, -0x80000000, 16777217, -16777215] + [rng.choice([rng.randrange(-100, 100), rng.randrange(-(1 << 31), 1 << 31)]) for _ in range(cases)]
    bad = {24: 0, 53: 0}
    lines = []
    seen: set[int] = set()
    for count in counts:
        for precision, control in ((24, 0x7F), (53, 0x27F)):
            sandbox.reset()
            thing = sandbox.object(0x100, {0x43: ('B', 0x10), 0x2C: ('i', 77)})
            state['count'] = count
            sandbox.set_fpcw(control)
            native = signed(sandbox.call(ENTRY, this=thing).eax)
            port = percent(count, precision)
            if native != port:
                bad[precision] += 1
                print(f'percent(n {count}) at {precision} bits: native {native}, port {port}')
            elif golden and precision == 24 and -3 <= count <= 8 and count not in seen:
                seen.add(count)
                lines.append(f'n {count} -> {native} (53 bits: {percent(count, 53)})')
    for line in lines:
        print(line)
    print(f'{len(counts)} watering counts: {bad[24]} mismatches at 24 bits, {bad[53]} at 53 bits')
    return 1 if bad[24] or bad[53] else 0


if __name__ == '__main__':
    arguments = [a for a in sys.argv[1:] if not a.startswith('--')]
    raise SystemExit(main(int(arguments[0]) if arguments else 400, '--golden' in sys.argv))

"""Theme Park Inc's staff formulas run in the x86 oracle sandbox and compared with the C# port's reading (ThemeParkIncStaffSettings).

Dev-only tooling like the rest of oracle_x86: Unicorn and the identified Theme Park Inc no-CD Game.exe (OPENTPW_TPI_EXE, SHA-256 pinned) are needed, nothing here is shipped.
Three routines run natively: the interpolation helper 0x62C900 (cdecl: low, high, x, a, b), the expected wage 0x62CD30 (cdecl: experience, top wage; it reads
AllStaffConstants.MaxExperience at 0xA02260) and the work duration 0x62E4C0 (thiscall on a staff thing: the kind byte +2, experience +0x210, salary +0x214 and the
motivation single +0x234; it reads MaxExperience, MotivationFactor at 0xA02290 and the role pairs at 0xA022D0..0xA0233C, which the oracle writes). The x87
truncation _ftol 0x86FAB0 runs for real. `--golden` prints a few native cases for the C# tests instead of the mismatch count.

Usage: python3 tools/ppc-analysis/oracle_x86/tpi_staff_formula_oracle.py [cases] [--golden]
"""
from __future__ import annotations

import random
import struct
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))

from x86_harness import Image, Sandbox, executable_path  # noqa: E402

LERP, WAGE, DURATION, REVIEW, RAND = 0x62C900, 0x62CD30, 0x62E4C0, 0x62CDA0, 0x870D38
UNHAPPY, HAPPY, LOWEST, AGENCIES = 0xA02264, 0xA02268, 0xA0226C, 0xA0240C
# kind byte -> the role's MaxWage address (kind table 0x62D070, jump tables 0x62D054 and 0x62D084)
STEP, REST, GAIN = 0x62B3E0, 0x62A1C0, 0x629650
RECOVERY = (0xA02270, 0xA02274, 0xA02278, 0xA0227C)   # Min/MaxRecuperationRate, Min/MaxHappinessRecuperationRate (singles)
EPM = {4: 0xA0239C, 5: 0xA02384, 6: 0xA0235C, 7: 0xA023F4, 8: 0xA023B8, 0x14: 0xA02384}
TOP = {4: 0xA0233C, 5: 0xA02308, 6: 0xA022F4, 7: 0xA0231C, 8: 0xA02330, 0x14: 0xA022E0}
FTOL = (0x86FAB0, 0x86FAD7)
MAX_EXPERIENCE, MOTIVATION_FACTOR = 0xA02260, 0xA02290
# kind byte -> (Min, Max) addresses and the inverse form, from the kind table 0x62E64C / jump table 0x62E630 and the settings layout
PAIRS = {4: (0xA02334, 0xA02338, True), 5: (0xA022F8, 0xA022FC, True), 6: (0xA022E4, 0xA022E8, False),
         7: (0xA0230C, 0xA02310, False), 8: (0xA02328, 0xA0232C, False), 0x14: (0xA022D0, 0xA022D4, False)}
MASK = 0xFFFFFFFF


def signed(value: int) -> int:
    value &= MASK
    return value - (1 << 32) if value >> 31 else value


def div0(a: int, b: int) -> int:
    """C's signed division toward zero (idiv)."""
    q = abs(a) // abs(b)
    return q if (a >= 0) == (b >= 0) else -q


def lerp(x: int, low: int, high: int, a: int, b: int) -> int:
    if x > high:
        return b
    offset = signed(x - low)
    if offset < 0:
        return a
    t = div0(signed(offset << 14), signed(high - low))
    return div0(signed((0x4000 - t) * a + t * b), 16384)


def wage(experience: int, top: int, max_experience: int) -> int:
    if experience > max_experience:
        w = top
    elif experience < 0:
        w = 0
    else:
        w = div0(signed(div0(signed(experience << 14), max_experience) * top), 16384)
    w -= w - div0(w, 20) * 20
    return 10 if w < 10 else w


def duration(kind: int, experience: int, motivation: float, salary: int, pair: tuple[int, int], max_experience: int, factor: int) -> int:
    low, high = pair
    inverse = PAIRS[kind][2]
    e = signed(experience - (100 - (int(motivation) & 0xFF)) * factor)
    if e <= 0:
        e = 1
    if (salary & MASK) < 10:
        return 200 if inverse else 1
    if not inverse:
        return high if e > max_experience else lerp(e, 0, max_experience, low, high)
    spread = low - high
    if e > max_experience:
        return low - spread
    return low - div0(signed(div0(signed(e << 14), max_experience) * spread), 16384)


def single(value: float) -> float:
    return struct.unpack('<f', struct.pack('<f', value))[0]


def clamp(total: float) -> float:
    return 100.0 if total > 100.0 else 0.0 if total < 0.0 else single(total)


def review(kind, experience, salary, agency_row, happiness, motivation, unhappy, happy, lowest, max_experience, top, rand_value):
    """ThemeParkIncStaffMember.ReviewWage: (happiness, motivation, shortfall, demand day or None when unchanged)."""
    gain, loss, initial_wage = agency_row
    expected = signed(wage(experience, top, max_experience) + initial_wage)
    demand = None
    if salary < expected:
        x = unhappy
        if loss != 0 and x != 0:
            x = signed(x * (loss + 1))
        motivation = clamp(float(-div0(x, 2)) + motivation)
        happiness = clamp(float(x) * -0.25 + happiness)
        shortfall = signed(expected - salary)
    else:
        shortfall = 0
        demand = 0
        y = happy
        if gain != 0 and y != 0:
            y = signed((gain + 1) * y)
        motivation = clamp(float(y) + motivation)
        happiness = clamp(float(y) + happiness)
    if shortfall != 0 and (int(motivation) & 0xFF) <= lowest:
        demand = rand_value % 27 + 1
    return happiness, motivation, shortfall, demand


def step(experience, energy, happiness, max_experience):
    d = 1.0 - float(experience & MASK) / max_experience
    return clamp(d * single(-0.1) + energy), clamp(d * single(-0.05) + happiness)


def rest(experience, energy, happiness, motivation, rates, recovery, happy_recovery, max_experience):
    min_rate, max_rate, min_happy, max_happy = rates
    f = float(experience & MASK) / max_experience
    e = min_rate + f
    es = single(e)
    if recovery != 0 and e != 0.0:
        e += recovery * e * single(0.01)
        es = single(e)
    if e > max_rate:
        es = max_rate
    h = f * 10.0 * min_happy
    if happy_recovery != 0 and h != 0.0:
        h += happy_recovery * h * single(0.01)
    if h > max_happy:
        h = max_happy
    return clamp(es + energy), clamp(h + happiness), clamp(h + motivation)


def gain(experience, counters, per_month, modifier, max_experience):
    working = (counters[0] + counters[5]) & MASK
    total = sum(counters) & MASK
    if total == 0:
        g = 0
        share = 0.0
    else:
        share = single(working / total)
        g = int(per_month * share)
    if modifier != 0 and g != 0:
        g = int(modifier * share + g)
    experience = signed(experience + g)
    if (experience & MASK) >= max_experience:
        experience = max_experience - 1
    return experience


def main(cases: int, golden: bool) -> int:
    path = executable_path('tpi')
    if path is None:
        raise SystemExit('needs OPENTPW_TPI_EXE (tools/ppc-analysis/oracle_x86/README.md)')
    image = Image.load(path)
    state = {'rand': 0}
    sandbox = Sandbox(image, [(LERP, 0x62C948), (WAGE, 0x62CD80), (DURATION, 0x62E640), (REVIEW, 0x62D054), (STEP, 0x62B4D0), (REST, 0x62A4C0), (GAIN, 0x629774), FTOL], stubs={RAND: lambda sb: (state['rand'], 0)})
    rng = random.Random(29)
    bad = 0
    lines = []
    for index in range(cases):
        sandbox.reset()
        max_experience = rng.choice([100000, 50000, rng.randrange(1, 200000)])
        factor = rng.choice([25, 0, rng.randrange(0, 400)])
        sandbox.write(MAX_EXPERIENCE, 'i', max_experience)
        sandbox.write(MOTIVATION_FACTOR, 'i', factor)
        # the interpolation helper
        low = rng.randrange(-50, 50)
        high = low + rng.randrange(1, 200000)
        x = rng.choice([low - 1, low, high, high + 1, rng.randrange(low - 100, high + 100)])
        a, b = rng.randrange(-500, 500), rng.randrange(-500, 500)
        native = signed(sandbox.call(LERP, args=(low, high, x, a, b)).eax)
        if native != lerp(x, low, high, a, b):
            bad += 1
            print(f'lerp({low}, {high}, {x}, {a}, {b}): native {native}, port {lerp(x, low, high, a, b)}')
        # the expected wage
        experience = rng.choice([-5, 0, 1, max_experience, max_experience + 1, rng.randrange(-1000, max_experience + 1000)])
        top = rng.choice([80, 100, 180, 250, 300, rng.randrange(0, 600)])
        native = signed(sandbox.call(WAGE, args=(experience & MASK, top)).eax)
        if native != wage(experience, top, max_experience):
            bad += 1
            print(f'wage({experience}, {top}): native {native}, port {wage(experience, top, max_experience)}')
        elif golden and index < 12:
            lines.append(f'wage max {max_experience} experience {experience} top {top} -> {native}')
        # the work duration
        kind = rng.choice(list(PAIRS))
        lo_address, hi_address, _ = PAIRS[kind]
        pair = (rng.randrange(1, 200), rng.randrange(1, 200))
        sandbox.write(lo_address, 'i', pair[0])
        sandbox.write(hi_address, 'i', pair[1])
        experience = rng.choice([0, 1, max_experience - 1, max_experience + 5, rng.randrange(0, max_experience)])
        motivation = struct.unpack('<f', struct.pack('<f', rng.choice([100.0, 0.0, 50.5, rng.uniform(0, 100)])))[0]
        salary = rng.choice([0, 9, 10, 11, 80, rng.randrange(0, 400)])
        thing = sandbox.object(0x300, {2: ('B', kind), 0x210: ('i', experience), 0x214: ('i', salary), 0x234: ('f', motivation)})
        sandbox.set_fpcw(0x27F)
        native = signed(sandbox.call(DURATION, this=thing).eax)
        port = duration(kind, experience, motivation, salary, pair, max_experience, factor)
        if native != port:
            bad += 1
            print(f'duration(kind {kind:#x}, exp {experience}, motivation {motivation}, salary {salary}, pair {pair}): native {native}, port {port}')
        elif golden and index < 12:
            lines.append(f'duration max {max_experience} factor {factor} kind {kind:#x} exp {experience} motivation {motivation!r} salary {salary} pair {pair} -> {native}')
        # the wage review
        kind = rng.choice(list(TOP))
        top = rng.choice([80, 250, rng.randrange(0, 600)])
        sandbox.write(TOP[kind], 'i', top)
        unhappy, happy, lowest = rng.choice([5, 0, rng.randrange(0, 40)]), rng.choice([2, 0, rng.randrange(0, 40)]), rng.choice([20, rng.randrange(0, 101)])
        for address, value in ((UNHAPPY, unhappy), (HAPPY, happy), (LOWEST, lowest)):
            sandbox.write(address, 'i', value)
        agency = rng.randrange(4)
        row = (rng.choice([0, 6, rng.randrange(-5, 20)]), rng.choice([0, 7, rng.randrange(-5, 20)]), rng.choice([0, 10, rng.randrange(-50, 60)]))
        base = AGENCIES + 0x24 * agency
        sandbox.write(base + 4, 'i', row[0])
        sandbox.write(base + 8, 'i', row[1])
        sandbox.write(base + 0x14, 'i', row[2])
        experience = rng.choice([0, max_experience, rng.randrange(-100, max_experience + 100)])
        salary = rng.choice([0, 10, 80, rng.randrange(-50, 400)])
        happiness = single(rng.choice([100.0, 0.0, rng.uniform(0, 100)]))
        motivation = single(rng.choice([100.0, 0.0, 19.9, rng.uniform(0, 100)]))
        demand_before = rng.randrange(0, 28)
        state['rand'] = rng.randrange(0, 32768)
        thing = sandbox.object(0x300, {2: ('B', kind), 0x210: ('i', experience), 0x214: ('i', salary), 0x244: ('i', agency),
                                       0x230: ('f', happiness), 0x234: ('f', motivation), 0x26C: ('i', demand_before), 0x270: ('i', 12345)})
        sandbox.set_fpcw(0x27F)
        sandbox.call(REVIEW, this=thing)
        native = (sandbox.read(thing + 0x230, 'f'), sandbox.read(thing + 0x234, 'f'), sandbox.read(thing + 0x270, 'i'), sandbox.read(thing + 0x26C, 'i'))
        h, m, shortfall, demand = review(kind, experience, salary, row, happiness, motivation, unhappy, happy, lowest, max_experience, top, state['rand'])
        port = (h, m, shortfall, demand_before if demand is None else demand)
        if native != port:
            bad += 1
            print(f'review(kind {kind:#x}, exp {experience}, salary {salary}, row {row}, h {happiness}, m {motivation}): native {native}, port {port}')
        # the step, the rest and the daily experience gain
        experience = rng.choice([0, max_experience - 1, rng.randrange(0, max_experience)])
        energy, happiness, motivation = (single(rng.choice([100.0, 0.0, 0.05, rng.uniform(0, 100)])) for _ in range(3))
        thing = sandbox.object(0x300, {2: ('B', kind), 0x210: ('i', experience), 0x244: ('i', agency), 0x230: ('f', happiness), 0x234: ('f', motivation), 0x238: ('f', energy)})
        sandbox.set_fpcw(0x27F)
        sandbox.call(STEP, this=thing)
        native = (sandbox.read(thing + 0x238, 'f'), sandbox.read(thing + 0x230, 'f'))
        if native != step(experience, energy, happiness, max_experience):
            bad += 1
            print(f'step(exp {experience}, e {energy}, h {happiness}): native {native}, port {step(experience, energy, happiness, max_experience)}')
        rates = tuple(single(v) for v in (rng.choice([0.2, rng.uniform(0, 2)]), rng.choice([0.75, rng.uniform(0, 3)]), rng.choice([1.0, rng.uniform(0, 5)]), rng.choice([3.0, rng.uniform(0, 8)])))
        for address, value in zip(RECOVERY, rates):
            sandbox.write(address, 'f', value)
        recovery, happy_recovery = rng.choice([0, 10, rng.randrange(-20, 60)]), rng.choice([0, 20, rng.randrange(-20, 60)])
        sandbox.write(base + 0xC, 'i', recovery)
        sandbox.write(base + 0x10, 'i', happy_recovery)
        thing = sandbox.object(0x300, {2: ('B', kind), 0x210: ('i', experience), 0x244: ('i', agency), 0x230: ('f', happiness), 0x234: ('f', motivation), 0x238: ('f', energy), 0x1B4: ('i', 3)})
        try:
            sandbox.call(REST, this=thing)
            native = (sandbox.read(thing + 0x238, 'f'), sandbox.read(thing + 0x230, 'f'), sandbox.read(thing + 0x234, 'f'))
            port = rest(experience, energy, happiness, motivation, rates, recovery, happy_recovery, max_experience)
            if native != port:
                bad += 1
                print(f'rest(exp {experience}, e {energy}, h {happiness}, m {motivation}, rates {rates}, rm {recovery}, hm {happy_recovery}): native {native}, port {port}')
        except Exception as error:   # the rest end (energy 100) calls out of the routine
            if 'outside the allowed' not in str(error):
                raise
        counters = [rng.choice([0, rng.randrange(0, 5000)]) for _ in range(7)]
        per_month = rng.choice([3, rng.randrange(0, 200)])
        sandbox.write(EPM[kind], 'i', per_month)
        modifier = rng.choice([0, 30, rng.randrange(-50, 100)])
        sandbox.write(base + 0x1C, 'i', modifier)
        # counters in the routine's order: working +0x280, looking +0x284, resting +0x288, idle +0x28C, training +0x290, researching +0x294, strike +0x298
        fields = {2: ('B', kind), 0x210: ('i', experience), 0x244: ('i', agency)}
        for offset, value in zip((0x280, 0x284, 0x288, 0x28C, 0x290, 0x294, 0x298), counters):
            fields[offset] = ('I', value)
        thing = sandbox.object(0x300, fields)
        sandbox.call(GAIN, this=thing)
        native = sandbox.read(thing + 0x210, 'i')
        port = gain(experience, counters, per_month, modifier, max_experience)
        if native != port:
            bad += 1
            print(f'gain(exp {experience}, counters {counters}, epm {per_month}, mod {modifier}): native {native}, port {port}')
    for line in lines:
        print(line)
    print(f'{cases} lerp, wage, work-duration, wage-review, step, rest and experience cases: {bad} mismatches')
    return 1 if bad else 0


if __name__ == '__main__':
    arguments = [a for a in sys.argv[1:] if not a.startswith('--')]
    raise SystemExit(main(int(arguments[0]) if arguments else 2000, '--golden' in sys.argv))

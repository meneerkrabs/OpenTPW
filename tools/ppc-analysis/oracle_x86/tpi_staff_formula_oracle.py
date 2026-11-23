"""Theme Park Inc's staff formulas run in the x86 oracle sandbox and compared with the C# port's reading (ThemeParkIncStaffSettings, ThemeParkIncStaffMember, ThemeParkIncStaffWorld).

Dev-only tooling like the rest of oracle_x86: Unicorn and the identified Theme Park Inc no-CD Game.exe (OPENTPW_TPI_EXE, SHA-256 pinned) are needed, nothing here is shipped.
Three routines run natively: the interpolation helper 0x62C900 (cdecl: low, high, x, a, b), the expected wage 0x62CD30 (cdecl: experience, top wage; it reads
AllStaffConstants.MaxExperience at 0xA02260) and the work duration 0x62E4C0 (thiscall on a staff thing: the kind byte +2, experience +0x210, pay +0x214 and the
motivation single +0x234; it reads MaxExperience, MotivationFactor at 0xA02290 and the role pairs at 0xA022D0..0xA0233C, which the oracle writes). The x87
truncation _ftol 0x86FAB0 runs for real. The wage review 0x62CDA0, the step 0x62B3E0, the rest 0x62A1C0 and the daily experience gain 0x629650 run natively too, and so do the idle end 0x62CA10 and
the handyman's pre-update 0x5D5000 (the search period), with their outside calls stubbed: the idle end's expiry call and the pre-update's radius lerp record whether the term or the period let the
member on. The x87 routines run at the control word 0x7F (24-bit significand), the game thread's after the Direct3D device started ([EXT:hardware-d3d-path]); the 53-bit word 0x27F is reported
beside it. `--golden` prints a few native cases for the C# tests instead of the mismatch count.

Usage: python3 tools/ppc-analysis/oracle_x86/tpi_staff_formula_oracle.py [cases] [--golden]
"""
from __future__ import annotations

import math
import random
import struct
import sys
from fractions import Fraction
from pathlib import Path

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))

from x86_harness import Image, Sandbox, executable_path  # noqa: E402

LERP, WAGE, DURATION, REVIEW, RAND = 0x62C900, 0x62CD30, 0x62E4C0, 0x62CDA0, 0x870D38
UNHAPPY, HAPPY, LOWEST, AGENCIES = 0xA02264, 0xA02268, 0xA0226C, 0xA0240C
# kind byte -> the role's MaxWage address (kind table 0x62D070, jump tables 0x62D054 and 0x62D084)
STEP, REST, GAIN = 0x62B3E0, 0x62A1C0, 0x629650
IDLE_END, IDLE_EXPIRED = 0x62CA10, 0x40C5EA                       # the idle end and its expiry call (the thunk of 0x62D810)
HANDYMAN_PRE, COMMON_PRE, RADIUS, CELL_WORD, PARK, FINDER, THOUGHT = 0x5D5000, 0x40193D, 0x40B924, 0x4080E9, 0x40144C, 0x402167, 0x402BBC
WORLD_POINTER, TURN = 0xADE6B0, 0x1EAB40
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


def duration(kind: int, experience: int, motivation: float, pay: int, pair: tuple[int, int], max_experience: int, factor: int) -> int:
    low, high = pair
    inverse = PAIRS[kind][2]
    e = signed(experience - (100 - (int(motivation) & 0xFF)) * factor)
    if e <= 0:
        e = 1
    if (pay & MASK) < 10:
        return 200 if inverse else 1
    if not inverse:
        return high if e > max_experience else lerp(e, 0, max_experience, low, high)
    spread = low - high
    if e > max_experience:
        return low - spread
    return low - div0(signed(div0(signed(e << 14), max_experience) * spread), 16384)


def single(value: float) -> float:
    return struct.unpack('<f', struct.pack('<f', value))[0]


def rnd(value, precision: int) -> float:
    """One x87 step's exact result rounded to the control word's significand (24 bits: a single; 53 bits: a double), round to nearest even."""
    value = Fraction(value)
    if value == 0:
        return 0.0
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
    rest_ = scaled - whole
    if rest_ > Fraction(1, 2) or rest_ == Fraction(1, 2) and whole & 1:
        whole += 1
    return float(sign * whole * scale)


def quotient(numerator: int, denominator: int, precision: int) -> float:
    """fild / fidiv of two integers; 0 / 0 is a NaN and n / 0 an infinity, as with every exception masked."""
    if denominator == 0:
        return float('nan') if numerator == 0 else math.copysign(math.inf, numerator)
    return rnd(Fraction(numerator, denominator), precision)


def clamp(total: float) -> float:
    return 100.0 if total > 100.0 else 0.0 if total < 0.0 else single(total)


def review(kind, experience, pay, agency_row, happiness, motivation, unhappy, happy, lowest, max_experience, top, rand_value, precision=24):
    """ThemeParkIncStaffMember.ReviewWage: (happiness, motivation, shortfall, demand day or None when unchanged)."""
    gain, loss, initial_wage = agency_row
    expected = signed(wage(experience, top, max_experience) + initial_wage)
    demand = None
    if pay < expected:
        x = unhappy
        if loss != 0 and x != 0:
            x = signed(x * (loss + 1))
        motivation = clamp(rnd(Fraction(-div0(x, 2)) + Fraction(motivation), precision))
        happiness = clamp(rnd(Fraction(rnd(Fraction(x) * Fraction(-0.25), precision)) + Fraction(happiness), precision))
        shortfall = signed(expected - pay)
    else:
        shortfall = 0
        demand = 0
        y = happy
        if gain != 0 and y != 0:
            y = signed((gain + 1) * y)
        motivation = clamp(rnd(Fraction(y) + Fraction(motivation), precision))
        happiness = clamp(rnd(Fraction(y) + Fraction(happiness), precision))
    if shortfall != 0 and (int(motivation) & 0xFF) <= lowest:
        demand = rand_value % 27 + 1
    return happiness, motivation, shortfall, demand


def one_minus_ratio(experience: int, max_experience: int, precision: int) -> float:
    """fildll the unsigned experience, fidivl MaxExperience, fsubrl 1.0."""
    return rnd(1 - Fraction(quotient(experience & MASK, max_experience, precision)), precision)


def step(experience, energy, happiness, max_experience, precision=24):
    d = one_minus_ratio(experience, max_experience, precision)
    return (clamp(rnd(Fraction(rnd(Fraction(d) * Fraction(single(-0.1)), precision)) + Fraction(energy), precision)),
            clamp(rnd(Fraction(rnd(Fraction(d) * Fraction(single(-0.05)), precision)) + Fraction(happiness), precision)))


def rest(experience, energy, happiness, motivation, rates, recovery, happy_recovery, max_experience, precision=24):
    min_rate, max_rate, min_happy, max_happy = rates
    f = quotient(experience & MASK, max_experience, precision)
    e = rnd(Fraction(min_rate) + Fraction(f), precision)
    es = single(e)
    if recovery != 0 and e != 0.0:
        e = rnd(Fraction(e) + Fraction(rnd(Fraction(rnd(Fraction(recovery) * Fraction(e), precision)) * Fraction(single(0.01)), precision)), precision)
        es = single(e)
    if e > max_rate:
        es = max_rate
    h = rnd(Fraction(rnd(Fraction(f) * 10, precision)) * Fraction(min_happy), precision)
    if happy_recovery != 0 and h != 0.0:
        h = rnd(Fraction(h) + Fraction(rnd(Fraction(rnd(Fraction(happy_recovery) * Fraction(h), precision)) * Fraction(single(0.01)), precision)), precision)
    if h > max_happy:
        h = max_happy
    return (clamp(rnd(Fraction(es) + Fraction(energy), precision)), clamp(rnd(Fraction(h) + Fraction(happiness), precision)),
            clamp(rnd(Fraction(h) + Fraction(motivation), precision)))


def gain(experience, counters, per_month, modifier, max_experience, precision=24):
    working = (counters[0] + counters[5]) & MASK
    total = sum(counters) & MASK
    if total == 0:
        g = 0
        share = 0.0
    else:
        share = single(quotient(working, total, precision))
        g = int(rnd(Fraction(per_month) * Fraction(share), precision))
    if modifier != 0 and g != 0:
        g = int(rnd(Fraction(rnd(Fraction(modifier) * Fraction(share), precision)) + g, precision))
    experience = signed(experience + g)
    if (experience & MASK) >= max_experience:
        experience = max_experience - 1
    return experience


def ftol_low(value: float) -> int:
    """_ftol 0x86FAB0: fistp qword with truncation, the low dword (0 for the integer indefinite)."""
    if not math.isfinite(value) or abs(value) >= 2.0 ** 63:
        return 0
    return signed(int(value))


def search_period(experience: int, max_experience: int, precision=24) -> int:
    """ThemeParkIncStaffWorld.SearchPeriod: ftol((1 - experience / MaxExperience) x 50.0f)."""
    return ftol_low(rnd(Fraction(one_minus_ratio(experience, max_experience, precision)) * 50, precision))


def idle_term(experience: int, max_experience: int, precision=24) -> int:
    """ThemeParkIncStaffWorld.IdleTerm: ftol((1 - experience / MaxExperience) x -50.0f)."""
    return ftol_low(rnd(Fraction(one_minus_ratio(experience, max_experience, precision)) * -50, precision))


def main(cases: int, golden: bool) -> int:
    path = executable_path('tpi')
    if path is None:
        raise SystemExit('needs OPENTPW_TPI_EXE (tools/ppc-analysis/oracle_x86/README.md)')
    image = Image.load(path)
    state = {'rand': 0, 'calls': []}

    def record(name: str, pop: int = 0, answer: int = 0):
        def stub(sb):
            state['calls'].append(name)
            return answer() if callable(answer) else answer, pop
        return stub

    sandbox = Sandbox(image, [(LERP, 0x62C948), (WAGE, 0x62CD80), (DURATION, 0x62E640), (REVIEW, 0x62D054), (STEP, 0x62B4D0), (REST, 0x62A4C0), (GAIN, 0x629774),
                              (IDLE_END, 0x62CA65), (HANDYMAN_PRE, 0x5D50E5), FTOL],
                      stubs={RAND: lambda sb: (state['rand'], 0), IDLE_EXPIRED: record('expired'), COMMON_PRE: record('common'), RADIUS: record('radius'),
                             CELL_WORD: record('cell', 12, lambda: state['zero']), PARK: record('park'), FINDER: record('finder', 8, lambda: state['zero']),
                             THOUGHT: record('thought')})
    rng = random.Random(29)
    bad = {24: 0, 53: 0}
    lines = []
    kept: dict[str, list[int]] = {}

    def keep(name: str, line: str, differs: bool) -> None:
        """The golden rows: the first three of each routine and up to four more where 24 and 53 bits give different answers."""
        plain, moved = kept.setdefault(name, [0, 0])
        if differs and moved < 4:
            kept[name][1] += 1
            lines.append(line + '   (53 bits differ)')
        elif plain < 3:
            kept[name][0] += 1
            lines.append(line)
    for index in range(cases):
        sandbox.reset()
        max_experience = rng.choice([100000, 50000, rng.randrange(1, 200000)])
        factor = rng.choice([25, 0, rng.randrange(0, 400)])

        def settings():
            sandbox.write(MAX_EXPERIENCE, 'i', max_experience)
            sandbox.write(MOTIVATION_FACTOR, 'i', factor)
        settings()
        # the interpolation helper
        low = rng.randrange(-50, 50)
        high = low + rng.randrange(1, 200000)
        x = rng.choice([low - 1, low, high, high + 1, rng.randrange(low - 100, high + 100)])
        a, b = rng.randrange(-500, 500), rng.randrange(-500, 500)
        native = signed(sandbox.call(LERP, args=(low, high, x, a, b)).eax)
        if native != lerp(x, low, high, a, b):
            bad[24] += 1
            print(f'lerp({low}, {high}, {x}, {a}, {b}): native {native}, port {lerp(x, low, high, a, b)}')
        # the expected wage
        experience = rng.choice([-5, 0, 1, max_experience, max_experience + 1, rng.randrange(-1000, max_experience + 1000)])
        top = rng.choice([80, 100, 180, 250, 300, rng.randrange(0, 600)])
        native = signed(sandbox.call(WAGE, args=(experience & MASK, top)).eax)
        if native != wage(experience, top, max_experience):
            bad[24] += 1
            print(f'wage({experience}, {top}): native {native}, port {wage(experience, top, max_experience)}')
        elif golden and index < 12:
            lines.append(f'wage max {max_experience} experience {experience} top {top} -> {native}')
        # the work duration
        kind = rng.choice(list(PAIRS))
        lo_address, hi_address, _ = PAIRS[kind]
        pair = (rng.randrange(1, 200), rng.randrange(1, 200))
        experience = rng.choice([0, 1, max_experience - 1, max_experience + 5, rng.randrange(0, max_experience)])
        motivation = struct.unpack('<f', struct.pack('<f', rng.choice([100.0, 0.0, 50.5, rng.uniform(0, 100)])))[0]
        pay = rng.choice([0, 9, 10, 11, 80, rng.randrange(0, 400)])
        port = duration(kind, experience, motivation, pay, pair, max_experience, factor)
        for precision, control in ((24, 0x7F), (53, 0x27F)):
            sandbox.write(lo_address, 'i', pair[0])
            sandbox.write(hi_address, 'i', pair[1])
            thing = sandbox.object(0x300, {2: ('B', kind), 0x210: ('i', experience), 0x214: ('i', pay), 0x234: ('f', motivation)})
            sandbox.set_fpcw(control)
            native = signed(sandbox.call(DURATION, this=thing).eax)
            if native != port:
                bad[precision] += 1
                print(f'duration(kind {kind:#x}, exp {experience}, motivation {motivation}, pay {pay}, pair {pair}) at {precision} bits: native {native}, port {port}')
            elif golden and index < 12 and precision == 24:
                lines.append(f'duration max {max_experience} factor {factor} kind {kind:#x} exp {experience} motivation {motivation!r} pay {pay} pair {pair} -> {native}')
        # the wage review
        kind = rng.choice(list(TOP))
        top = rng.choice([80, 250, rng.randrange(0, 600)])
        unhappy, happy, lowest = rng.choice([5, 0, rng.randrange(0, 40)]), rng.choice([2, 0, rng.randrange(0, 40)]), rng.choice([20, rng.randrange(0, 101)])
        agency = rng.randrange(4)
        row = (rng.choice([0, 6, rng.randrange(-5, 20)]), rng.choice([0, 7, rng.randrange(-5, 20)]), rng.choice([0, 10, rng.randrange(-50, 60)]))
        base = AGENCIES + 0x24 * agency
        experience = rng.choice([0, max_experience, rng.randrange(-100, max_experience + 100)])
        pay = rng.choice([0, 10, 80, rng.randrange(-50, 400)])
        happiness = single(rng.choice([100.0, 0.0, rng.uniform(0, 100)]))
        motivation = single(rng.choice([100.0, 0.0, 19.9, rng.uniform(0, 100)]))
        demand_before = rng.randrange(0, 28)
        state['rand'] = rng.randrange(0, 32768)
        for precision, control in ((24, 0x7F), (53, 0x27F)):
            sandbox.write(TOP[kind], 'i', top)
            for address, value in ((UNHAPPY, unhappy), (HAPPY, happy), (LOWEST, lowest)):
                sandbox.write(address, 'i', value)
            sandbox.write(base + 4, 'i', row[0])
            sandbox.write(base + 8, 'i', row[1])
            sandbox.write(base + 0x14, 'i', row[2])
            thing = sandbox.object(0x300, {2: ('B', kind), 0x210: ('i', experience), 0x214: ('i', pay), 0x244: ('i', agency),
                                           0x230: ('f', happiness), 0x234: ('f', motivation), 0x26C: ('i', demand_before), 0x270: ('i', 12345)})
            sandbox.set_fpcw(control)
            sandbox.call(REVIEW, this=thing)
            native = (sandbox.read(thing + 0x230, 'f'), sandbox.read(thing + 0x234, 'f'), sandbox.read(thing + 0x270, 'i'), sandbox.read(thing + 0x26C, 'i'))
            h, m, shortfall, demand = review(kind, experience, pay, row, happiness, motivation, unhappy, happy, lowest, max_experience, top, state['rand'], precision)
            port = (h, m, shortfall, demand_before if demand is None else demand)
            if native != port:
                bad[precision] += 1
                print(f'review(kind {kind:#x}, exp {experience}, pay {pay}, row {row}, h {happiness}, m {motivation}) at {precision} bits: native {native}, port {port}')
            elif golden and precision == 24:
                keep('review', f'review kind {kind:#x} max {max_experience} exp {experience} pay {pay} top {top} unhappy {unhappy} happy {happy} lowest {lowest} row {row} '
                     f'h {happiness!r} m {motivation!r} -> {native}',
                     review(kind, experience, pay, row, happiness, motivation, unhappy, happy, lowest, max_experience, top, state['rand'], 53) != (h, m, shortfall, demand))
        # the step, the rest and the daily experience gain
        experience = rng.choice([0, max_experience - 1, rng.randrange(0, max_experience)])
        energy, happiness, motivation = (single(rng.choice([100.0, 0.0, 0.05, rng.uniform(0, 100)])) for _ in range(3))
        rates = tuple(single(v) for v in (rng.choice([0.2, rng.uniform(0, 2)]), rng.choice([0.75, rng.uniform(0, 3)]), rng.choice([1.0, rng.uniform(0, 5)]), rng.choice([3.0, rng.uniform(0, 8)])))
        recovery, happy_recovery = rng.choice([0, 10, rng.randrange(-20, 60)]), rng.choice([0, 20, rng.randrange(-20, 60)])
        counters = [rng.choice([0, rng.randrange(0, 5000)]) for _ in range(7)]
        per_month = rng.choice([3, rng.randrange(0, 200)])
        modifier = rng.choice([0, 30, rng.randrange(-50, 100)])
        for precision, control in ((24, 0x7F), (53, 0x27F)):
            thing = sandbox.object(0x300, {2: ('B', kind), 0x210: ('i', experience), 0x244: ('i', agency), 0x230: ('f', happiness), 0x234: ('f', motivation), 0x238: ('f', energy)})
            sandbox.set_fpcw(control)
            sandbox.call(STEP, this=thing)
            native = (sandbox.read(thing + 0x238, 'f'), sandbox.read(thing + 0x230, 'f'))
            port = step(experience, energy, happiness, max_experience, precision)
            if native != port:
                bad[precision] += 1
                print(f'step(exp {experience}, e {energy}, h {happiness}) at {precision} bits: native {native}, port {port}')
            elif golden and precision == 24:
                keep('step', f'step max {max_experience} exp {experience} e {energy!r} h {happiness!r} -> {native}', step(experience, energy, happiness, max_experience, 53) != port)
            for address, value in zip(RECOVERY, rates):
                sandbox.write(address, 'f', value)
            sandbox.write(base + 0xC, 'i', recovery)
            sandbox.write(base + 0x10, 'i', happy_recovery)
            thing = sandbox.object(0x300, {2: ('B', kind), 0x210: ('i', experience), 0x244: ('i', agency), 0x230: ('f', happiness), 0x234: ('f', motivation), 0x238: ('f', energy), 0x1B4: ('i', 3)})
            sandbox.set_fpcw(control)
            try:
                sandbox.call(REST, this=thing)
                native = (sandbox.read(thing + 0x238, 'f'), sandbox.read(thing + 0x230, 'f'), sandbox.read(thing + 0x234, 'f'))
                port = rest(experience, energy, happiness, motivation, rates, recovery, happy_recovery, max_experience, precision)
                if native != port:
                    bad[precision] += 1
                    print(f'rest(exp {experience}, e {energy}, h {happiness}, m {motivation}, rates {rates}, rm {recovery}, hm {happy_recovery}) at {precision} bits: native {native}, port {port}')
                elif golden and precision == 24:
                    keep('rest', f'rest max {max_experience} exp {experience} e {energy!r} h {happiness!r} m {motivation!r} rates {rates} rm {recovery} hm {happy_recovery} -> {native}',
                         rest(experience, energy, happiness, motivation, rates, recovery, happy_recovery, max_experience, 53) != port)
            except Exception as error:   # the rest end (energy 100) calls out of the routine
                if 'outside the allowed' not in str(error):
                    raise
            sandbox.write(EPM[kind], 'i', per_month)
            sandbox.write(base + 0x1C, 'i', modifier)
            # counters in the routine's order: working +0x280, looking +0x284, resting +0x288, idle +0x28C, training +0x290, researching +0x294, strike +0x298
            fields = {2: ('B', kind), 0x210: ('i', experience), 0x244: ('i', agency)}
            for offset, value in zip((0x280, 0x284, 0x288, 0x28C, 0x290, 0x294, 0x298), counters):
                fields[offset] = ('I', value)
            thing = sandbox.object(0x300, fields)
            sandbox.set_fpcw(control)
            sandbox.call(GAIN, this=thing)
            native = sandbox.read(thing + 0x210, 'i')
            port = gain(experience, counters, per_month, modifier, max_experience, precision)
            if native != port:
                bad[precision] += 1
                print(f'gain(exp {experience}, counters {counters}, epm {per_month}, mod {modifier}) at {precision} bits: native {native}, port {port}')
            elif golden and precision == 24:
                keep('gain', f'gain max {max_experience} exp {experience} counters {counters} epm {per_month} mod {modifier} -> {native}',
                     gain(experience, counters, per_month, modifier, max_experience, 53) != port)
        # the idle end's term and the handyman's search period: the experiences where a product falls just below an integer are the ones the precision moves
        experience = rng.choice([0, 34000 * max_experience // 100000, max_experience - 1, rng.randrange(0, max_experience), rng.randrange(0, max_experience + 1000)])
        for precision, control in ((24, 0x7F), (53, 0x27F)):
            term = idle_term(experience, max_experience, precision)
            stamp = 1_000_000
            answers = []
            for turn in ((stamp - term) & MASK, (stamp - term + 1) & MASK):
                sandbox.reset()
                settings()
                world = sandbox.object(TURN + 8, {TURN: ('I', turn)})
                sandbox.write(WORLD_POINTER, 'I', world)
                thing = sandbox.object(0x300, {0x210: ('i', experience), 0x23C: ('i', stamp)})
                state['calls'] = []
                sandbox.set_fpcw(control)
                sandbox.call(IDLE_END, this=thing)
                answers.append('expired' in state['calls'])
            if answers != [False, True]:
                bad[precision] += 1
                print(f'idle term(exp {experience}, max {max_experience}) at {precision} bits: the port says {term}, the native expiry at its edge {answers}')
            period = search_period(experience, max_experience, precision)
            expected, seen = [], []
            for turn in sorted({period & MASK, (period + 1) & MASK, (period - 1) & MASK, (2 * period) & MASK}):
                sandbox.reset()
                settings()
                state['zero'] = sandbox.object(16)
                world = sandbox.object(TURN + 8, {TURN: ('I', turn)})
                sandbox.write(WORLD_POINTER, 'I', world)
                thing = sandbox.object(0x300, {0x210: ('i', experience), 0x1B4: ('i', 0)})
                state['calls'] = []
                sandbox.set_fpcw(control)
                sandbox.call(HANDYMAN_PRE, this=thing)
                seen.append('radius' in state['calls'])
                expected.append(period == 0 or turn % (period & MASK) == 0)
            if seen != expected:
                bad[precision] += 1
                print(f'search period(exp {experience}, max {max_experience}) at {precision} bits: the port says {period}, the native searches {seen}, expected {expected}')
            elif golden and precision == 24:
                keep('period', f'period max {max_experience} exp {experience} -> {period}, idle term {term}',
                     (search_period(experience, max_experience, 53), idle_term(experience, max_experience, 53)) != (period, term))
    # day shares whose single falls just below a tenth: 7 of 10 days is 0.69999999 as a single, so 10 x the share is 6.9999999 at 53 bits and 7.0 at 24
    for counters, per_month, modifier in (([7, 0, 0, 3, 0, 0, 0], 10, 0), ([7, 0, 0, 3, 0, 0, 0], 10, 20), ([3, 0, 4, 0, 0, 4, 9], 20, 0), ([700, 100, 100, 100, 0, 0, 0], 3, 30)):
        sandbox.reset()
        settings()
        experience, kind, agency = 20000, 5, 0
        for precision, control in ((24, 0x7F), (53, 0x27F)):
            sandbox.write(EPM[kind], 'i', per_month)
            sandbox.write(AGENCIES + 0x1C, 'i', modifier)
            fields = {2: ('B', kind), 0x210: ('i', experience), 0x244: ('i', agency)}
            for offset, value in zip((0x280, 0x284, 0x288, 0x28C, 0x290, 0x294, 0x298), counters):
                fields[offset] = ('I', value)
            thing = sandbox.object(0x300, fields)
            sandbox.set_fpcw(control)
            sandbox.call(GAIN, this=thing)
            native = sandbox.read(thing + 0x210, 'i')
            port = gain(experience, counters, per_month, modifier, max_experience, precision)
            if native != port:
                bad[precision] += 1
                print(f'gain(exp {experience}, counters {counters}, epm {per_month}, mod {modifier}) at {precision} bits: native {native}, port {port}')
            elif golden and precision == 24:
                lines.append(f'gain max {max_experience} exp {experience} counters {counters} epm {per_month} mod {modifier} -> {native}'
                             + ('   (53 bits differ)' if gain(experience, counters, per_month, modifier, max_experience, 53) != port else ''))
    for line in lines:
        print(line)
    print(f'{cases} lerp, wage, work-duration, wage-review, step, rest, experience, idle-term and search-period cases: {bad[24]} mismatches at 24 bits, {bad[53]} at 53 bits')
    return 1 if bad[24] or bad[53] else 0


if __name__ == '__main__':
    arguments = [a for a in sys.argv[1:] if not a.startswith('--')]
    raise SystemExit(main(int(arguments[0]) if arguments else 2000, '--golden' in sys.argv))

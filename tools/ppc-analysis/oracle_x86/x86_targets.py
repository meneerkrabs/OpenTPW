"""x86 differential oracle targets: isolated routines of the identified Windows executables vs the plans.

Same shape as the PowerPC oracle (tools/ppc-analysis/oracle/targets.py), against a different image:

* `binary(sandbox, x, log)`: run the original routine in the sandbox on input x, return a comparable value;
* `plan(x)`: the reference the Mac-derived plans give, in Python (a mismatch is a PC-vs-Mac finding);
* `alternatives`: extra readings of the routine itself (what the code does), compared too;
* `inputs(rng, n)`: n seeded random inputs plus explicit boundaries.

Addresses are virtual addresses of the executable at its preferred base. Stubs are listed per target with the
reason they are safe; any other call outside `allowed` aborts the run.
"""
from __future__ import annotations

import importlib.util
import math
import random
import struct
import sys
from fractions import Fraction
from dataclasses import dataclass, field
from pathlib import Path
from typing import Callable

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
sys.path.insert(0, str(HERE.parent / 'lanes' / 'det'))

import det_model  # noqa: E402  (the Mac plan's reference arithmetic)
from x86_harness import MASK32, Image, Sandbox  # noqa: E402


@dataclass
class Target:
    key: str
    exe: str                      # 'tpw' or 'tpi' (x86_harness.IDENTIFIED)
    title: str
    plan_doc: str
    entry: int
    allowed: list[tuple[int, int]]
    stubs: Callable[['Target'], dict]
    binary: Callable
    plan: Callable | None
    inputs: Callable[[random.Random, int], list]
    alternatives: dict[str, Callable] = field(default_factory=dict)
    equal: Callable = staticmethod(lambda a, b: a == b)
    notes: list[str] = field(default_factory=list)
    log: list = field(default_factory=list)
    budget: int = 100_000

    def sandbox(self, image: Image) -> Sandbox:
        self.log = []
        return Sandbox(image, self.allowed, self.stubs(self), self.budget)


def run_binary(target: Target, sandbox: Sandbox, x):
    sandbox.reset()
    target.log.clear()
    return target.binary(sandbox, x, target.log)


# ============================================================================== (a) tp.exe world RNG
WORLD_RNG = 0x1DA708   # object + 0x1da708: the RNG state; +0x1da70c is the tick counter (the Mac world's layout)
WORLD_OBJECT_SIZE = 0x400000   # the state sits ~2 MB in; the sandbox needs the whole object mapped
TPW_RNG_STEP = 0x516330        # thiscall, ecx = object; returns |new state|
TPI_WORLD_RNG = 0x1EAB3C       # TPI Game.exe: the state moved (+0x1eab40 is the tick counter)
TPI_RNG_STEP = 0x6493E0        # thiscall, ecx = object; returns |new state|; code ends with the `ret` at 0x649414


def mask(value: int) -> int:
    return value & MASK32


def ror32(value: int, count: int) -> int:
    return mask((value >> count) | (value << (32 - count)))


def world_rng_binary(sb: Sandbox, state: int, log) -> tuple[int, int]:
    obj = sb.object(WORLD_OBJECT_SIZE, {WORLD_RNG: ('I', state)})
    result = sb.call(TPW_RNG_STEP, this=obj)
    return result.eax, sb.read(obj + WORLD_RNG, 'I')


def tpi_world_rng_binary(sb: Sandbox, state: int, log) -> tuple[int, int]:
    obj = sb.object(WORLD_OBJECT_SIZE, {TPI_WORLD_RNG: ('I', state)})
    result = sb.call(TPI_RNG_STEP, this=obj)
    return result.eax, sb.read(obj + TPI_WORLD_RNG, 'I')


def world_rng_plan(state: int) -> tuple[int, int]:
    """The Mac world LCG (det_model.world_next): store the LCG successor, return its wrapping signed abs."""
    new, returned = det_model.world_next(state)
    return returned, new


def world_rng_routine_reading(state: int) -> tuple[int, int]:
    """What 0x516330 does: LCG successor, rotated right by 13, stored; returns the wrapping signed abs of that."""
    new = ror32(det_model.world_step(state), 13)
    return det_model.wrapping_abs32(new), new


def world_rng_inputs(rng: random.Random, n: int) -> list:
    inverse = pow(1664525, -1, 1 << 32)

    def preimage(successor: int) -> int:
        return mask((successor - 1013904223) * inverse)
    # States whose stored (rotated) successor is INT_MIN and 0, and the Mac plan's INT_MIN and 0.
    boundaries = [0, 1, 0x7FFFFFFF, 0x80000000, MASK32, preimage(ror32(0x80000000, 19)), preimage(0),
                  preimage(0x80000000)]
    return boundaries + [rng.getrandbits(32) for _ in range(n)]


WORLD_RNG_TARGET = Target(
    'tpw-exe-world-rng', 'tpw', 'tp.exe world RNG step 0x516330', 'DET-plan §2.3 / det_model.world_next',
    TPW_RNG_STEP, [(TPW_RNG_STEP, 0x516365)], lambda t: {}, world_rng_binary, world_rng_plan, world_rng_inputs,
    alternatives={'routine-reading': world_rng_routine_reading},
    notes=['Same logical generator as the Mac 0x105328 (ORACLE.md, x86 section): same object offset and constants, '
           'but the PC stores the successor rotated right by 13, so the stream differs.'])

TPI_WORLD_RNG_TARGET = Target(
    'tpi-exe-world-rng', 'tpi', 'Game.exe world RNG step 0x6493e0', 'DET-plan §2.3 / det_model.world_next',
    TPI_RNG_STEP, [(TPI_RNG_STEP, 0x649415)], lambda t: {}, tpi_world_rng_binary, world_rng_plan, world_rng_inputs,
    alternatives={'tpw-pc-reading': world_rng_routine_reading},
    notes=['Same step as the tp.exe 0x516330 (LCG, ror 13, stored, signed abs) at a moved state slot (+0x1eab3c): '
           'the TPI reading is the TPW PC reading, not the Mac plan.'])


# ============================================================================== shared pieces for the Mac-rule targets
GAME_POINTER = 0x80239C        # global: the game object (the world object of the RNG target); the Mac keeps it behind a TOC slot
GAME_POINTER_BANK = 0x7CF83C   # second global holding the same game object, read by the bank code
PARK_OF_GAME = 0x519510        # thiscall (game) -> park record; the Mac 0x1082b4
DEBUG_PRINT = 0x5DA3C0         # cdecl (level or condition, format, ...); logs and assertions, the Mac 0xb678 / 0x5fbcc
FTOL = (0x67A830, 0x67A857)    # float -> int64 by fistp with truncation; callers use the low word (the Mac: fctiwz / 0x1c3fbc)
S64_DIV = (0x67C320, 0x67C3CA)  # signed 64-bit divide (ret 0x10), the Mac 0x1c4100
U64_MUL = (0x67C7F0, 0x67C824)  # 64-bit multiply (ret 0x10)

X87_CONTROL_WORD = 0x27F       # the control word the CRT's pow tests for (53-bit significand, round to nearest); see ORACLE.md X3

_MAC = None


def mac():
    """The PowerPC oracle's targets module (plans, readings and input generators for the Mac rules), loaded lazily
    under its own name so that its `harness`/`targets` modules cannot clash with this directory's."""
    global _MAC
    if _MAC is None:
        oracle = HERE.parent / 'oracle'
        if str(oracle) not in sys.path:
            sys.path.append(str(oracle))
        spec = importlib.util.spec_from_file_location('ppc_oracle_targets', oracle / 'targets.py')
        module = importlib.util.module_from_spec(spec)
        sys.modules['ppc_oracle_targets'] = module
        spec.loader.exec_module(module)
        _MAC = module
    return _MAC


def s32(value: int) -> int:
    value &= MASK32
    return value - (1 << 32) if value & 0x80000000 else value


def trunc_div(a: int, b: int) -> int:
    return abs(a) // abs(b) * (1 if (a < 0) == (b < 0) else -1)


def x87_round(value: Fraction, bits: int = 64) -> Fraction:
    """Round an exact value to a binary floating point number with a `bits`-bit significand (round to nearest even)."""
    if value == 0:
        return Fraction(0)
    sign = -1 if value < 0 else 1
    value = abs(value)
    exponent = value.numerator.bit_length() - value.denominator.bit_length()
    while Fraction(2) ** exponent > value:
        exponent -= 1
    while Fraction(2) ** (exponent + 1) <= value:
        exponent += 1
    scale = Fraction(2) ** (exponent - bits + 1)
    mantissa = value / scale
    whole = mantissa.numerator // mantissa.denominator
    rest = mantissa - whole
    if rest > Fraction(1, 2) or (rest == Fraction(1, 2) and whole % 2 == 1):
        whole += 1
    return sign * whole * scale


def fistp_low32(value: Fraction | float) -> int:
    """`fistp qword` with truncation, then the caller keeps eax: the low 32 bits of the 64-bit integer, or 0 for the
    'integer indefinite' of an overflow, infinity or NaN (0x8000000000000000)."""
    if isinstance(value, float):
        if not math.isfinite(value):
            return 0
        whole = int(value)
    else:
        whole = trunc_div(value.numerator, value.denominator)
    if not -(1 << 63) <= whole < (1 << 63):
        return 0
    return whole & MASK32


def bind_game(sb: Sandbox, size: int = 0x1E0000, fields: dict | None = None) -> int:
    game = sb.object(size, fields)
    sb.write(GAME_POINTER, 'I', game)
    sb.write(GAME_POINTER_BANK, 'I', game)
    return game


def thiscall_check(sb: Sandbox, expected: int, what: str) -> None:
    if sb.reg('ecx') != expected:
        raise AssertionError(f'{what}: called with an unexpected object')


# ============================================================================== (b) tp.exe arrival group size 0x4cf5b0
ARRIVAL_ENTRY = 0x4CF5B0       # thiscall (arrival object, unused); the Mac 0xcb4e8
ARRIVAL_RANGE = (0x4CF5B0, 0x4CF71D)
ARRIVAL_THING_OF_GAME = 0x519590   # (game) -> the thing at the u16 index game+0x1da724 (Mac 0x1083c8, +0x1da734); stubbed
ARRIVAL_COUNTED_GUESTS = 0x4C7FA0  # thiscall (park, 20) -> counted guests, ret 4 (Mac 0xc1fb4)
ARRIVAL_POINTS = 0x4C8240          # thiscall (park) -> park points (Mac 0xc24c0), target tpw-exe-park-points
ARRIVAL_SETTINGS = {'min_people': 0x785310, 'bonus': 0x78531C, 'per_visitor': 0x785320}  # Mac settings +720, +732, +736
ARRIVAL_MODE = 0xFB3B7C        # GameType object word 0 (Mac data:0x53d98); 1 = online
ARRIVAL_MODE_GUARD = 0xFB34C2  # bit 0: the GameType object is constructed (its constructor 0x550ca0 must not run)
GAME_WORLD_MODE = 0x1DA738     # bankrupt world mode 4, as on the Mac
ARRIVAL_STATE = type('ArrivalState', (), {})()


def arrival_stubs(target: Target) -> dict:
    state = ARRIVAL_STATE

    def park(sb):
        thiscall_check(sb, state.game, '0x519510')
        return state.park, 0

    def counted(sb):
        thiscall_check(sb, state.park, '0x4c7fa0')
        if sb.arg(0) != 20:
            raise AssertionError('0x4c7fa0 called with an unexpected argument')
        return state.guests & MASK32, 4

    def points(sb):
        thiscall_check(sb, state.park, '0x4c8240')
        return state.points & MASK32, 0

    def thing(sb):
        thiscall_check(sb, state.game, '0x519590')
        return state.thing, 0

    def log(sb):
        target.log.append(('log', sb.arg(0), sb.arg(1)))
        return 0, 0

    return {PARK_OF_GAME: park, ARRIVAL_COUNTED_GUESTS: counted, ARRIVAL_POINTS: points, ARRIVAL_THING_OF_GAME: thing,
            DEBUG_PRINT: log}


def arrival_binary(sb: Sandbox, x: dict, log, fpcw: int | None = None) -> int:
    state = ARRIVAL_STATE
    state.game = bind_game(sb, fields={GAME_WORLD_MODE: ('i', x['world_mode'])})
    state.park = sb.object(16)
    state.thing = sb.object(64, {0x30: ('i', x['obj48'])})
    state.guests, state.points = x['guests'], x['points']
    for key, address in ARRIVAL_SETTINGS.items():
        sb.write(address, 'i', x[key])
    sb.write(ARRIVAL_MODE, 'i', x['limit_mode'])
    sb.write(ARRIVAL_MODE_GUARD, 'B', 1)
    sb.set_fpcw(X87_CONTROL_WORD if fpcw is None else fpcw)
    return s32(sb.call(ARRIVAL_ENTRY, this=state.park).eax)


def arrival_reading(x: dict, precision: int = 53) -> int:
    """What 0x4cf5b0 does, with the x87 arithmetic: none while the world mode is 4; the divisor is the per-visitor word,
    1 unless it is above 1 (signed); total = points + bonus (32-bit wrap, `fild` signed); the single-precision factor
    (0.8f when the lights thing's dword +0x30 is positive, else 1.2f) is multiplied by the exact integer in a
    `precision`-bit significand (53 under the control word 0x27F; 64 is the extended default, 24 single), truncated by
    fistp and only the low word kept, `cdq`/`idiv` (signed, toward zero), at least MinPeople (signed), then the
    online cut (mode word 1: 500, unsigned compare) or the 1500 cut, both without a floor."""
    if x['world_mode'] == 4:
        return 0
    per_visitor = x['per_visitor'] if x['per_visitor'] > 1 else 1
    total = s32(x['points'] + x['bonus'])
    factor = Fraction(struct.unpack('<f', struct.pack('<f', 0.8 if x['obj48'] > 0 else 1.2))[0])
    low = s32(fistp_low32(x87_round(factor * total, precision)))
    count = max(trunc_div(low, per_visitor), x['min_people'])
    guests = x['guests']
    if x['limit_mode'] == 1 and (guests + count) & MASK32 > 500:
        count = s32(500 - guests)
    if x['limit_mode'] != 1 and (guests + count) & MASK32 > 1500:
        count = s32(1500 - guests)
    return count


def arrival_plan(x: dict) -> int:
    """The Mac routine (F9): `arrival_full` of the PowerPC oracle, which OpenTPW's GuestSimulation.ArrivalGroupSize follows
    except for the floor at 0."""
    return mac().arrival_full(x)


def arrival_inputs(rng: random.Random, n: int) -> list:
    shipped = dict(world_mode=0, guests=0, points=0, bonus=20, per_visitor=6, min_people=1, obj48=0, limit_mode=0)
    # Where the x87 and the Mac's single-precision arithmetic can part: totals above 2^24 (the Mac rounds the sum to
    # single first), products at or above 2^31 (fistp wraps, fctiwz saturates), and a spread of mid-size totals.
    extra = [{**shipped, 'points': p, 'bonus': b, 'obj48': o, 'per_visitor': v}
             for p, b, o, v in ((16777217, 0, 0, 1), (16777217, 0, 1, 1), (33554435, 0, 1, 1), (0x7FFFFF00, 0, 0, 1),
                                (0x7FFFFF00, 0, 1, 1), (0x7FFFFF00, 0, 1, 2), (1500000000, 0, 0, 1), (-1500000000, 0, 0, 1),
                                (1999999999, 0, 0, 3), (0x40000000, 0, 1, 1), (50, 0, 1, 1), (5, 0, 1, 1),
                                (1747629, 0, 0, 1), (5242881, 0, 1, 1), (1747628, 0, 0, 1), (5242880, 0, 1, 1))]
    wide = [{**shipped, 'points': rng.choice([rng.randrange(0, 1 << 31), rng.randrange(1 << 20, 1 << 28),
                                              rng.randrange(-(1 << 31), 0)]),
             'bonus': rng.choice([0, 20, rng.randrange(0, 1 << 20)]), 'obj48': rng.choice([0, 1]),
             'per_visitor': rng.choice([1, 2, 6, rng.randrange(1, 4000)]), 'limit_mode': rng.choice([0, 1, 2]),
             'guests': rng.choice([0, rng.randrange(0, 1600)]), 'min_people': rng.choice([1, 2, rng.randrange(0, 40)])}
            for _ in range(n // 5)]
    # The group is cut to 1500 for large products, so the divisor is chosen to bring the quotient to 0 or 1: the exact
    # product truncated plus one gives 0 for the x87 and 1 where the Mac's single-precision product rounds up.
    precision = []
    for _ in range(n // 5):
        total, obj48 = rng.randrange(1_000_000, 1 << 25), rng.choice([0, 1])
        factor = Fraction(struct.unpack('<f', struct.pack('<f', 0.8 if obj48 > 0 else 1.2))[0])
        precision.append({**shipped, 'points': total, 'bonus': 0, 'obj48': obj48, 'min_people': 0,
                          'per_visitor': int(factor * total) + 1})
    extra += [{**shipped, 'points': 1747629, 'bonus': 0, 'obj48': 0, 'min_people': 0, 'per_visitor': 2097155},
              {**shipped, 'points': 5242881, 'bonus': 0, 'obj48': 1, 'min_people': 0, 'per_visitor': 4194305}]
    return mac().arrival_inputs(rng, n) + extra + wide + precision


ARRIVAL_TARGET = Target(
    'tpw-exe-arrival-group', 'tpw', 'tp.exe arrival group size 0x4cf5b0', 'GUESTS.md "Arrivals" / ORACLE.md F9 (Mac 0xcb4e8)',
    ARRIVAL_ENTRY, [ARRIVAL_RANGE, FTOL], arrival_stubs, arrival_binary, arrival_plan, arrival_inputs,
    alternatives={'routine-reading': arrival_reading},
    notes=['Stubs: 0x519510 game -> park (sentinel, checked), 0x4c7fa0 counted guests (argument 20 checked), 0x4c8240 park '
           'points (chosen), 0x519590 thing lookup (synthetic thing, dword +0x30 chosen), 0x5da3c0 debug print (logged). The '
           'GameType object is marked constructed. Executed: the routine and the float-to-int helper 0x67a830.'])


# ============================================================================== (c) tp.exe loan offers 0x4cf7c0
LOAN_CTOR = 0x4CF7C0           # thiscall (bank object): the bank constructor; the Mac 0xcb7a8
LOAN_CTOR_STOP = 0x4CF8C6      # end of the 8-offer loop (the rest builds the bank object's helpers); the Mac 0xcb908
LOAN_BASE_CTOR = 0x50AFE0      # thiscall (bank, u16 1, 0x10) base-class constructor, ret 8; stubbed (header only)
LOAN_CASH = 0x7851D8           # settings: initial cash (Mac settings +408); the offers follow from 0x7851E0 (+416), 16 bytes each
LOAN_OFFERS_AT = 0x7851E0
# pow: the runtime's own routine (0x67c5d0 with small helpers), not an import: y * log2(x) by fyl2x, then 2^x by
# f2xm1 / fscale, all x87. It runs here, on Unicorn's x87 implementation.
LOAN_POW_RANGES = [(0x67C5D0, 0x67C7F0), (0x686170, 0x6861F0), (0x68620E, 0x686300)]
LOAN_STATE = type('LoanState', (), {})()


def loan_stubs(target: Target) -> dict:
    state = LOAN_STATE

    def base_constructor(sb):
        target.log.append(('base-ctor', sb.reg('ecx'), sb.arg(0) & 0xFFFF, sb.arg(1)))
        return 0, 8

    return {LOAN_BASE_CTOR: base_constructor}


def loan_offers_binary(sb: Sandbox, x: dict, log, fpcw: int | None = None) -> dict:
    """Run the 8-offer loop of 0x4cf7c0 on a zeroed bank object: the settings hold the initial cash at 0x7851d8 and the
    offers (amount, APR, months, lender) from 0x7851e0, 16 bytes each; records are 32 bytes from bank + 0x14."""
    bank = sb.object(0x200)
    sb.write(LOAN_CASH, 'I', x['cash'])
    for index, offer in enumerate(x['offers']):
        for word, value in enumerate(offer):
            sb.write(LOAN_OFFERS_AT + 16 * index + 4 * word, 'I', value)
    sb.set_fpcw(X87_CONTROL_WORD if fpcw is None else fpcw)
    sb.call(LOAN_CTOR, this=bank, stop=LOAN_CTOR_STOP)
    if [entry for entry in log if entry[0] == 'base-ctor'] != [('base-ctor', bank, 1, 0x10)]:
        raise AssertionError('the base constructor was not called once with the bank object, the word 1 and 0x10')
    records = [tuple((sb.read(bank + 0x14 + 32 * i + 4 * w, 'I') for w in range(8)))
               for i in range(8)]
    # record words: available, amount, APR, months, monthly, bought, months repaid, lender
    return {'monthly': [record[4] for record in records],
            'records': [(r[0], r[1], r[2], r[3], r[5], r[6], r[7]) for r in records],
            'bank': tuple(sb.read(bank + offset, 'I') for offset in (12, 16, 276, 280, 284, 288, 292))}


def loan_reading(amount: int, apr: int, months: int) -> int:
    """0x4cf866..0x4cf8ac with the x87 arithmetic: the base is APR * 0.01 + 1 (`fsub` of -1.0, the product and the sum each
    rounded to a 64-bit significand, then stored as a double by the call), the exponent N * (1/12 as a double) * 0.5 (same
    rounding), pow on those two doubles (Python's math.pow stands for the runtime's x87 routine here), times the
    unsigned amount and divided by N in extended precision, then truncated by fistp with only the low word kept. The
    x87 control word 0x27F rounds every intermediate to 53 bits instead (`precision` below)."""
    return loan_reading_precision(amount, apr, months, 53)


def loan_reading_precision(amount: int, apr: int, months: int, precision: int) -> int:
    hundredth = Fraction(0.01)
    twelfth = Fraction(1 / 12)
    base = x87_round(x87_round(apr * hundredth, precision) + 1, precision)
    exponent = x87_round(x87_round(months * twelfth, precision) * Fraction(1, 2), precision)
    base_double, exponent_double = float(base), float(exponent)
    try:
        power = math.pow(base_double, exponent_double)
    except OverflowError:
        power = math.inf
    if not math.isfinite(power):
        return fistp_low32(math.inf)
    scaled = x87_round(Fraction(amount) * Fraction(power), precision)
    if months == 0:
        return fistp_low32(math.inf if scaled != 0 else math.nan)
    return fistp_low32(x87_round(scaled / months, precision))


def loan_offers_reading(x: dict) -> dict:
    offers = x['offers']
    return {'monthly': [loan_reading(a, r, n) for a, r, n, _ in offers],
            'records': [(int(a <= 10000), a, r, n, 0, 0, lender) for a, r, n, lender in offers],
            'bank': (x['cash'], 0, 1, 0, 0, 0, 0)}


def loan_offers_plan(x: dict) -> dict:
    """The Mac-derived plan (F12, LoanMath.MonthlyRepayment): the instalment alone, from the PowerPC oracle."""
    return mac().loan_offers_plan(x)


def loan_offers_mac_reading(x: dict) -> dict:
    """The Mac routine as read (binary64, pow of math, float-to-unsigned saturating): records and bank words included."""
    return mac().loan_offers_reading(x)


def loan_offers_equal(original: dict, other: dict) -> bool:
    return all(original[key] == other[key] for key in other)


def loan_offers_inputs(rng: random.Random, n: int) -> list:
    base = mac().loan_offers_inputs(rng, n)
    # Out of the shipped range: a zero term, amounts and powers beyond 32 bits (the Mac saturates, fistp keeps the low word).
    edges = [{'cash': 0, 'offers': [(100000, 20, 0, 0), (0, 20, 0, 1), (4000000000, 20, 36, 2), (0xFFFFFFFF, 0, 1, 3),
                                    (0xFFFFFFFF, 40, 120, 4), (0xFFFFFFFF, 40, 1, 5), (1, 0, 1, 6), (3000000000, 1, 2, 7)]}]
    return base + edges


LOAN_OFFERS_TARGET = Target(
    'tpw-exe-loan-offers', 'tpw', 'tp.exe loan offer setup 0x4cf7c0', 'ECONOMY.md / ORACLE.md F12 (Mac 0xcb7a8)',
    LOAN_CTOR, [(LOAN_CTOR, LOAN_CTOR_STOP), FTOL] + LOAN_POW_RANGES, loan_stubs, loan_offers_binary, loan_offers_plan,
    loan_offers_inputs, alternatives={'mac-reading': loan_offers_mac_reading, 'routine-reading': loan_offers_reading},
    equal=loan_offers_equal,
    notes=['Fragment 0x4cf7c0..0x4cf8c6 (the 8-offer loop) run from the constructor entry. Stub: 0x50afe0 base constructor '
           '(header only, arguments checked). pow is the runtime routine 0x67c5d0 and its helpers, executed.'])


# ============================================================================== (d) tp.exe loan monthly step 0x4d0370
LOAN_STEP_ENTRY = 0x4D0385     # inside the monthly handler 0x4d0370 (bank message 12), after its exception-frame set-up; the
                               # Mac 0xcc21c. The frame push reads fs:[0], which the sandbox has no segment for.
LOAN_STEP_STOP = 0x4D04D9      # end of the loan loop; the tail (a bank in the red for a month) sends a message
LOAN_STEP_RANGE = (0x4D0385, 0x4D04D9)
LOAN_ACC_IN, LOAN_ACC_OUT = 0x1FC90, 0x1F5A0   # park words raised by credits and debits, as on the Mac
LOAN_TURN = 0x1DA70C
# DEBUG_PRINT formats of the handler -> the Mac message ids (ORACLE.md F13): the batch assertion, the instalment log, the
# zero-term assertion and the paid-off log.
LOAN_MESSAGES = {0x759E04: ('assert', 340), 0x759EC0: ('assert', 525), 0x759F00: ('log465',), 0x759E88: ('log587',)}


def loan_step_stubs(target: Target) -> dict:
    state = LOAN_STATE

    def park(sb):
        thiscall_check(sb, state.game, '0x519510')
        return state.park, 0

    def debug(sb):
        fmt = sb.arg(1)
        kind = LOAN_MESSAGES.get(fmt)
        if kind is None:
            raise AssertionError(f'unexpected debug message {fmt:#x}')
        if kind[0] == 'assert':
            target.log.append(('assert', sb.arg(0) & 0xFF, kind[1]))
        elif kind[0] == 'log465':
            target.log.append(('log465', sb.arg(2), sb.arg(3), sb.arg(4)))   # months repaid + 1, loan index, instalment
        else:
            target.log.append(('log587', sb.arg(2)))                         # loan index
        return 0, 0

    return {PARK_OF_GAME: park, DEBUG_PRINT: debug}


def loan_step_binary(sb: Sandbox, x: dict, log) -> dict:
    """Same bank layout as the Mac (balance +12, batch +16, withdrawals +276, last balance +284, turn entered red +288,
    year profit +292, eight 32-byte loan records from +0x14: P +24, APR +28, months +32, instalment +36, bought +40, months
    repaid +44) and the loan loop of the monthly handler called once per month in one sandbox."""
    state = LOAN_STATE
    state.game = bind_game(sb)
    state.park = sb.object(0x21000)
    sb.set_fpcw(X87_CONTROL_WORD)
    bank = sb.object(512)
    m = mac()
    for offset, key in ((m.BANK_BALANCE, 'bal'), (m.BANK_BATCH, 'batch'), (m.BANK_WITHDRAW, 'wd'), (m.BANK_LAST, 'last'),
                        (m.BANK_RED, 'red'), (m.BANK_PROFIT, 'profit')):
        sb.write(bank + offset, 'I', x[key] & MASK32)
    for index, loan in enumerate(x['loans']):
        base = bank + 32 * index
        for offset, key in ((24, 'amount'), (28, 'apr'), (32, 'months'), (36, 'monthly'), (40, 'bought'), (44, 'repaid')):
            sb.write(base + offset, 'I', loan[key] & MASK32)
    out = {key: [] for key in m.LOAN_OUT_KEYS}
    for turn in x['turns']:
        sb.write(state.game + LOAN_TURN, 'I', turn)
        mark = len(log)
        sb.call(LOAN_STEP_ENTRY, this=bank, stop=LOAN_STEP_STOP)
        out['balance'].append(sb.read(bank + m.BANK_BALANCE, 'i'))
        out['profit'].append(sb.read(bank + m.BANK_PROFIT, 'i'))
        out['batch'].append(sb.read(bank + m.BANK_BATCH, 'I'))
        out['last'].append(sb.read(bank + m.BANK_LAST, 'i'))
        out['red'].append(sb.read(bank + m.BANK_RED, 'I'))
        out['acc_in'].append(sb.read(state.park + LOAN_ACC_IN, 'I'))
        out['acc_out'].append(sb.read(state.park + LOAN_ACC_OUT, 'I'))
        loans = [(sb.read(bank + 32 * i + 40, 'I'), sb.read(bank + 32 * i + 44, 'I')) for i in range(8)]
        out['loans'].append(loans)
        out['active'].append(sum(1 for bought, _ in loans if bought))
        out['events'].append([tuple(entry) for entry in log[mark:]])
    return out


def loan_step_equal(original: dict, other: dict) -> bool:
    return all(original[key] == other[key] for key in other)


def loan_term_plan(x: dict) -> dict:
    return mac().loan_term_plan(x)


def loan_step_reading(x: dict) -> dict:
    return mac().loan_step_reading(x)


def loan_month_inputs(rng: random.Random, n: int) -> list:
    return mac().loan_month_inputs(rng, n)


def loan_term_inputs(rng: random.Random, n: int) -> list:
    return mac().loan_term_inputs(rng, n)


LOAN_TERM_TARGET = Target(
    'tpw-exe-loan-term', 'tpw', 'tp.exe loan monthly step 0x4d0370 over a loan term',
    'ECONOMY.md / ORACLE.md F12, F13 (Mac 0xcc21c)', LOAN_STEP_ENTRY, [LOAN_STEP_RANGE], loan_step_stubs, loan_step_binary,
    loan_term_plan, loan_term_inputs, alternatives={'routine-reading': loan_step_reading}, equal=loan_step_equal,
    notes=['Loop of the monthly handler 0x4d0370 run from 0x4d0385 to 0x4d04d9, once a month over a whole term (withdrawals '
           'enabled, no batch credit), with the instalment M given. Stubs: 0x519510 game -> park (sentinel, checked) and '
           '0x5da3c0 debug print (logged by message). The tail (a bank in the red for a month) is not run.'])
LOAN_MONTH_TARGET = Target(
    'tpw-exe-loan-month', 'tpw', 'tp.exe loan monthly step 0x4d0370, one to three months on any bank state',
    'ECONOMY.md / ORACLE.md F13 (Mac 0xcc21c)', LOAN_STEP_ENTRY, [LOAN_STEP_RANGE], loan_step_stubs, loan_step_binary, None,
    loan_month_inputs, alternatives={'routine-reading': loan_step_reading}, equal=loan_step_equal,
    notes=['The same fragment on random bank states (any batch, withdrawal flag, red state, bought flag, months repaid).'])


# ============================================================================== (e) tp.exe park points 0x4c8240
POINTS_ENTRY = 0x4C8240        # thiscall (park, unused); the Mac 0xc24c0
POINTS_RANGES = [(0x4C8240, 0x4C8424),      # the routine
                 (0x4D3D10, 0x4D3D7A),      # park record search, writes the cache (Mac 0xd1c88)
                 (0x4D3D80, 0x4D3DDD),      # park record search (Mac 0xd1be8)
                 (0x4F88B0, 0x4F88F6),      # turns -> 100 ns units (Mac 0xe4750)
                 S64_DIV, U64_MUL]
POINTS_TYPE_RECORD = 0x412E90  # thiscall (registry 0x7890a0, u16 id) -> type record, ret 4 (Mac 0x118018); stubbed
POINTS_REGISTRY = 0x7890A0
POINTS_COUNTS_TEST = 0x4DD920  # thiscall (thing) -> nonzero when it counts, where the Mac calls 0xdc834; stubbed (chosen flag)
POINTS_OWN_AGE = 0x4DD670      # thiscall (thing) -> days since its purchase timestamp, the Mac 0xdc520; stubbed
POINTS_THINGS = 0x7CFB90       # ThingArray: 20-byte entries, word 0 = the thing (entry 0 is checked to be empty)
POINTS_FIRST = 0x1DA746        # game: index of the first thing in the list (u16), as on the Mac
POINTS_TURN = 0x1DA70C
POINTS_CLOCK = 0x2A0           # game + 0x2a0: the clock; its +0x1c word is the rate
POINTS_TYPE_TABLE = 0x1D943C   # game: per-type park records, 32 bytes: +0 id, +0x18 copies, +0x1c first purchase turn
POINTS_STATE = type('PointsState', (), {})()
# type record fields (PC): +0x58 value, +0x5c decay, +0x9c the track-type word, +0x268 bonus[3], +0x4ac kind (Mac +1960)
POINTS_KIND = 0x4AC


def points_stubs(target: Target) -> dict:
    state = POINTS_STATE

    def lookup(sb):
        thiscall_check(sb, POINTS_REGISTRY, '0x412e90')
        return state.records[sb.arg(0) & 0xFFFF], 4

    def assertion(sb):
        target.log.append(('assert', sb.arg(0)))
        return 0, 0

    def thing_of(sb) -> dict:
        address = sb.reg('ecx')
        if address not in state.things:
            raise AssertionError('called without a thing')
        return state.things[address]

    def counts(sb):
        return (1 if thing_of(sb)['open'] else 0), 0

    def age(sb):
        thing = thing_of(sb)
        return mac().points_own_days(state.x['now'], thing['built'], state.x['scale']) & MASK32, 0

    return {POINTS_TYPE_RECORD: lookup, DEBUG_PRINT: assertion, POINTS_COUNTS_TEST: counts, POINTS_OWN_AGE: age}


def points_binary(sb: Sandbox, x: dict, log) -> int:
    state = POINTS_STATE
    state.x = x
    game = bind_game(sb)
    sb.write(game + POINTS_TURN, 'I', x['now'] & MASK32)
    sb.write(game + POINTS_CLOCK + 0x1C, 'I', x['scale'] & MASK32)
    state.records = {}
    table = game + POINTS_TYPE_TABLE
    m = mac()
    for n, kind in enumerate(x['types']):
        fields = {0x58: ('i', kind['value']), 0x5C: ('i', kind['decay']), 0x9C: ('i', kind['f9c']),
                  POINTS_KIND: ('i', kind['kind'])}
        fields.update({0x268 + 4 * k: ('i', bonus) for k, bonus in enumerate(kind['bonus'])})
        state.records[kind['id']] = sb.object(2048, fields)
        sb.write(table + 32 * n, 'H', kind['id'])
        sb.write(table + 32 * n + 0x18, 'i', m.points_count(x, kind['id']))
        sb.write(table + 32 * n + 0x1C, 'I', kind['first'] & MASK32)
    sb.write(table + 0x12C0, 'i', len(x['types']))   # +0x12c4 (cached id 0, never a type id) and +0x12c8 stay 0
    state.things = {}
    for index, thing in enumerate(x['things'], start=1):
        following = index + 1 if index < len(x['things']) else 0
        address = sb.object(64, {0xC: ('H', following), 0xE: ('H', thing['type'])})
        sb.write(POINTS_THINGS + 20 * index, 'I', address)
        state.things[address] = thing
    sb.write(game + POINTS_FIRST, 'H', 1 if x['things'] else 0)
    sb.set_fpcw(X87_CONTROL_WORD)
    result = s32(sb.call(POINTS_ENTRY, this=sb.object(16)).eax)
    if any(kind == 'assert' and value == 0 for kind, value in log):
        raise AssertionError('an assertion failed (ThingArray entry 0 in use)')
    return result


def points_plan(x: dict) -> int:
    """The Mac routine (F10): `points_full` of the PowerPC oracle, which ParkEconomy.ParkPoints follows (the input's open flag
    standing for the counts-test)."""
    return mac().points_full(x)


def points_inputs(rng: random.Random, n: int) -> list:
    return mac().points_inputs(rng, n)


POINTS_TARGET = Target(
    'tpw-exe-park-points', 'tpw', 'tp.exe park points 0x4c8240', 'GUESTS.md "Arrivals" / ORACLE.md F10 (Mac 0xc24c0)',
    POINTS_ENTRY, POINTS_RANGES, points_stubs, points_binary, points_plan, points_inputs,
    notes=['Executed: 0x4c8240, the park record search 0x4d3d10 / 0x4d3d80, 0x4f88b0 (turns -> 100 ns units) and the 64-bit '
           'divide and multiply helpers. Stubs: 0x412e90 type record, 0x5da3c0 assertion (a zero condition fails the run), '
           '0x4dd920 counts-test (chosen flag), 0x4dd670 own age (signed timestamp difference in days, as on the Mac). '
           'Fields as on the Mac except the kind word (+0x4ac). Park record +0x18 is the number of listed copies and +0x1c '
           'follows the Mac 0xda874 write rule (points_first).'])

# ============================================================================== (f) tp.exe object scrap value 0x4e2400
SCRAP_ENTRY = 0x4E2400         # thiscall (thing) -> the sale refund; the Mac 0xe25e8 (the sale 0x4dd0a0 inlines the same product)
SCRAP_RANGES = [(0x4E2400, 0x4E243F),       # the refund: percentage x record +0x1b8 (Upgrades[0].CostOfUpgrade), unsigned / 100
                (0x4E2290, 0x4E23F4),       # the percentage, the Mac 0xe2424
                (0x4F8690, 0x4F872E),       # the clock's current stamp: clock +0 + turn x rate / 4 x 10^7 (the Mac 0xe4394)
                (0x5FC530, 0x5FC550),       # 64-bit stamp addition
                (0x5FC550, 0x5FC570),       # 64-bit stamp subtraction (the Mac glue 0x1c5c64)
                S64_DIV, U64_MUL]
SCRAP_SYSTEM_TIME = 0x5FC660   # thiscall (stamp; status out, then 8 field out-pointers, null here) -> FileTimeToSystemTime; stubbed
SCRAP_THING = {'type': 0xE, 'stamp': 0x18, 'level': 0x50, 'used': 0x1A0}   # the Mac +14, +20 (big-endian), +76, +412
SCRAP_PERCENT = 0x188          # record: percentages at +0x188 + 64 x level + 4 x year (the Mac +392)
SCRAP_COST = 0x1B8             # record: CostOfUpgrade at +0x1b8 + 64 x level (the Mac +440)
SCRAP_TYPE = 0x101
SCRAP_STATE = type('ScrapState', (), {})()


def scrap_stubs(target: Target) -> dict:
    state = SCRAP_STATE

    def lookup(sb):
        thiscall_check(sb, POINTS_REGISTRY, '0x412e90')
        if sb.arg(0) & 0xFFFF != SCRAP_TYPE:
            raise AssertionError('0x412e90 called with an unexpected type id')
        return state.record, 4

    def system_time(sb):
        # argument 0 is the status word (a slot in 0x4f8690's own arguments, not read again), 1..8 the SYSTEMTIME fields
        if not sb.arg(0) or any(sb.arg(n) for n in range(1, 9)):
            raise AssertionError('0x5fc660 called with unexpected output pointers')
        sb.write(sb.arg(0), 'I', 0)   # the success path's status
        return 0, 0x24

    return {POINTS_TYPE_RECORD: lookup, SCRAP_SYSTEM_TIME: system_time}


def scrap_stamp(turn: int, base: int) -> int:
    return (base + mac().scrap_stamp(turn)) & ((1 << 64) - 1)


def scrap_binary(sb: Sandbox, x: dict, log) -> int:
    state = SCRAP_STATE
    game = bind_game(sb)
    sb.write(game + POINTS_TURN, 'I', x['now'] & MASK32)
    clock = game + POINTS_CLOCK
    sb.write(clock, 'Q', x['base'])
    sb.write(clock + 0x1C, 'I', mac().CLOCK_RATE)
    fields = {SCRAP_COST + 64 * level: ('i', cost) for level, cost in enumerate(x['cost'])}
    for level, row in enumerate(x['pct']):
        fields.update({SCRAP_PERCENT + 64 * level + 4 * year: ('i', value) for year, value in enumerate(row)})
    state.record = sb.object(2048, fields)
    thing = sb.object(0x200, {SCRAP_THING['type']: ('H', SCRAP_TYPE), SCRAP_THING['stamp']: ('Q', scrap_stamp(x['built'], x['base'])),
                              SCRAP_THING['level']: ('B', x['level']), SCRAP_THING['used']: ('I', x['used'] & MASK32)})
    return sb.call(SCRAP_ENTRY, this=thing).eax


def scrap_plan(x: dict) -> int:
    """The Mac routine reading (F21), which ParkEconomy.ScrapValue / ScrapPercent follow; the clock base cancels in the age."""
    return mac().scrap_reading(x)


def scrap_inputs(rng: random.Random, n: int) -> list:
    """The Mac target's inputs (edges and random), each with a clock base: zero, small, or anywhere in the 64-bit range."""
    inputs = mac().scrap_inputs(rng, n)
    for x in inputs:
        x['base'] = rng.choice([0, rng.randrange(1 << 32), rng.randrange(1 << 64)])
    return inputs


SCRAP_TARGET = Target(
    'tpw-exe-scrap-value', 'tpw', 'tp.exe object scrap value 0x4e2400', 'ECONOMY.md / ORACLE.md F21 (Mac 0xe25e8, ECON-025)',
    SCRAP_ENTRY, SCRAP_RANGES, scrap_stubs, scrap_binary, scrap_plan, scrap_inputs,
    alternatives={'all-levels-cost': lambda x: mac().scrap_reading(x, base_cost=False),
                  'no-first-month-refund': lambda x: mac().scrap_reading(x, initial=False),
                  'base-row': lambda x: mac().scrap_reading(x, level_row=False)},
    notes=['Executed: 0x4e2400, the percentage 0x4e2290, the clock stamp 0x4f8690 with its 64-bit add / subtract and the '
           '64-bit divide and multiply helpers. Stubs: 0x412e90 type record (a synthetic one), 0x5fc660 (FileTimeToSystemTime '
           'into null out-pointers; its result is not read). The clock base (+0, a 64-bit stamp) is random and cancels.',
           'Thing fields at other offsets than the Mac: stamp +0x18 (little-endian), level byte +0x50, use counter +0x1a0.'])

# ============================================================================== (g) tp.exe queue re-evaluation 0x501390
QREEVAL_ENTRY = 0x501390       # thiscall (guest), called by the queue edit 0x4de1f0 for every listed guest but the called one
QREEVAL_RANGE = (0x501390, 0x5014D2)
QREEVAL_GUEST_ID = 0x50B350    # thiscall (guest, out) -> out holding the guest's u16 id, ret 4; stubbed
QREEVAL_POSITION = 0x4DDF50    # thiscall (ride, u16 id) -> position in the queue list, ret 4 (Mac 0xdd88c); stubbed (chosen)
QREEVAL_LOOKUP = 0x4754E0      # cdecl (handle, 4 out-pointers): a table read whose outputs are not used afterwards; stubbed
QREEVAL_ANIMATION = 0x50BE80   # thiscall (guest + 0x30, animation, flag), ret 8; stubbed (recorded)
QREEVAL_REMOVE = 0x4DDD20      # thiscall (ride, u16 id): unlink from the queue list, ret 4; stubbed (recorded)
QREEVAL_DISMISS = 0x5012F0     # thiscall (guest): "dismissed from queue", clears the queue words; stubbed (recorded)
QREEVAL_SET_STATE = 0x501DB0   # thiscall (guest, state), ret 4; stubbed (recorded)
QREEVAL_GUEST = {'id': None, 'state': 0x220, 'ride': 0x1DC, 'handle': 0xC, 'animation': 0x30}   # ride: u16 thing index
QREEVAL_CELLS = 0x40           # ride: queue size in cells (mQueueSizeInCells)
QREEVAL_RIDE_INDEX = 7
QREEVAL_STATE = type('QueueReevalState', (), {})()


def qreeval_stubs(target: Target) -> dict:
    state = QREEVAL_STATE

    def guest_id(sb):
        thiscall_check(sb, state.guest, '0x50b350')
        sb.write(sb.arg(0), 'H', state.x['id'])
        return sb.arg(0), 4

    def position(sb):
        thiscall_check(sb, state.ride, '0x4ddf50')
        if sb.arg(0) & 0xFFFF != state.x['id']:
            raise AssertionError('0x4ddf50 asked for another guest')
        return state.x['position'] & MASK32, 4

    def lookup(sb):
        return 0, 0

    def animation(sb):
        thiscall_check(sb, state.guest + QREEVAL_GUEST['animation'], '0x50be80')
        target.log.append(('animation', sb.arg(0), sb.arg(1)))
        return 0, 8

    def remove(sb):
        thiscall_check(sb, state.ride, '0x4ddd20')
        target.log.append(('remove', sb.arg(0) & 0xFFFF))
        return 0, 4

    def dismiss(sb):
        thiscall_check(sb, state.guest, '0x5012f0')
        target.log.append(('dismiss',))
        return 0, 0

    def set_state(sb):
        thiscall_check(sb, state.guest, '0x501db0')
        target.log.append(('state', sb.arg(0), sb.read(state.guest + QREEVAL_GUEST['ride'], 'H')))
        return 0, 4

    def log(sb):
        target.log.append(('log', sb.arg(0), sb.arg(1)))
        return 0, 0

    return {QREEVAL_GUEST_ID: guest_id, QREEVAL_POSITION: position, QREEVAL_LOOKUP: lookup, QREEVAL_ANIMATION: animation,
            QREEVAL_REMOVE: remove, QREEVAL_DISMISS: dismiss, QREEVAL_SET_STATE: set_state, DEBUG_PRINT: log}


def qreeval_binary(sb: Sandbox, x: dict, log) -> tuple:
    """(left, ride word cleared): the recorded calls reduced to the decision. The whole call sequence is checked and fails
    the run when it differs: the entry log and the ThingArray check, then on the exit the "shortened" log, animation 13
    for an id divisible by 3, unlink, dismissal and state 6 with the ride word +0x1dc already 0; on the stay branch
    nothing else and the ride word untouched. The stack must balance (stub pop sizes)."""
    state = QREEVAL_STATE
    state.x = x
    state.ride = sb.object(0x400, {QREEVAL_CELLS: ('I', x['cells'] & MASK32)})
    sb.write(POINTS_THINGS + 20 * QREEVAL_RIDE_INDEX, 'I', state.ride)
    state.guest = sb.object(0x400, {QREEVAL_GUEST['state']: ('I', x['state'] & MASK32),
                                    QREEVAL_GUEST['ride']: ('H', QREEVAL_RIDE_INDEX)})
    result = sb.call(QREEVAL_ENTRY, this=state.guest)
    if result.esp_delta != 0:
        raise AssertionError(f'stack off by {result.esp_delta} after the call')
    events = [e if e[0] != 'log' else ('log', e[1], sb.read(e[2], 'B') and e[2]) for e in log]
    entry = ('log', 2, 0x75E05C)
    left = ('remove', x['id']) in events
    expected = [entry, ('log', 1, 0x747724)]   # the ThingArray check (condition: entry 0 empty, so 1 = holds)
    if left:
        expected += [('log', 2, 0x75E014)] + ([('animation', 13, 0)] if x['id'] % 3 == 0 else []) + \
                    [('remove', x['id']), ('dismiss',), ('state', 6, 0)]
    if events != expected:
        raise AssertionError(f'unexpected call sequence {events}')
    still = sb.read(state.guest + QREEVAL_GUEST['ride'], 'H')
    if still != (0 if left else QREEVAL_RIDE_INDEX):
        raise AssertionError('ride word +0x1dc changed on the stay branch or kept on the exit')
    return left, left


def qreeval_reading(x: dict) -> tuple:
    """0x501390 as read: leave when the position, unsigned, is at least 4 x the queue cells (a 32-bit product) and the
    guest is not in state 14; a leaving guest whose id is a multiple of 3 plays animation 13 first."""
    left = (x['position'] & MASK32) >= ((4 * x['cells']) & MASK32) and x['state'] != 14
    return left, left


def qreeval_csharp_rule(x: dict) -> tuple:
    """GuestSimulation.ReevaluateQueue's departure for a guest in the queue list (position >= 0, the only case the original
    has; C# also sends away a guest missing from the list): a guest other than the called one in states 11, 12 or 13 leaves
    when its position is at least PositionsPerCell (4) x QueueSizeInCells; a guest in state 14 is not visited. The
    Confused departure of a guest standing off the queue (QUEUE-005) depends on the map and is not modelled here."""
    left = x['state'] in (11, 12, 13) and x['position'] >= 4 * x['cells']
    return left, left


def qreeval_inputs(rng: random.Random, n: int) -> list:
    def make(**kw) -> dict:
        cells = rng.choice([0, 1, 2, 3, 5, 8, 12, 25, rng.randrange(0, 40)])
        x = {'cells': cells, 'position': rng.choice([0, 4 * cells - 1, 4 * cells, 4 * cells + 1, rng.randrange(0, 4 * cells + 8)]),
             'state': rng.choice([11, 12, 13, 14]), 'id': rng.randrange(1, 2000)}
        x['position'] = max(0, x['position'])
        x.update(kw)
        return x
    edges = [make(cells=c, position=p, state=s, id=i) for c in (1, 2, 3) for p in (4 * c - 1, 4 * c) for s in (11, 12, 13, 14)
             for i in (3, 4)]
    return edges + [make() for _ in range(n)]


QREEVAL_TARGET = Target(
    'tpw-exe-queue-reevaluate', 'tpw', 'tp.exe queue re-evaluation 0x501390', 'QUEUE-plan.md §4 / QUEUE-005 (Mac 0xee8f8)',
    QREEVAL_ENTRY, [QREEVAL_RANGE], qreeval_stubs, qreeval_binary, qreeval_reading, qreeval_inputs,
    alternatives={'csharp-rule': qreeval_csharp_rule,
                  'no-state-14-exemption': lambda x: (x['position'] >= 4 * x['cells'],) * 2},
    notes=['Executed: 0x501390. Stubs (recorded and checked for their object): 0x50b350 guest id, 0x4ddf50 position (chosen), '
           '0x4754e0 table read (outputs unused), 0x50be80 animation, 0x4ddd20 unlink, 0x5012f0 dismissal, 0x501db0 state, '
           '0x5da3c0 log (a level-0 message fails the run). The exit order is checked: animation 13 (id % 3 == 0), unlink, '
           'dismissal, ride word +0x1dc cleared, state 6. Inputs are the states a listed guest can be in (11 to 14).'])

# ============================================================================== (h) tp.exe park points counts-test 0x4dd920
COUNTS_ENTRY = 0x4DD920        # thiscall (thing) -> 1 when the thing counts for park points; the Mac 0xdc834
COUNTS_RANGE = (0x4DD920, 0x4DDA20)
COUNTS_BACK = 0x4DE130         # thiscall (thing, out u16) -> back-of-queue cell (0: no queue cells from the entrance), ret 4; stubbed
COUNTS_LENGTH = 0x4DDF50       # thiscall (thing, u16 0) -> the queue list's length, ret 4; stubbed (chosen)
COUNTS_COASTER = 0x441970      # cdecl (thing) -> the coaster controller test (riders +0x140 zero, +0x3c bits); stubbed (chosen)
COUNTS_THING = {'flags': 0x32, 'state': 0x19C, 'open': 0x68, 'cells': 0x40, 'track_word': 0x2C, 'type': 0xE}
COUNTS_TRACK_TYPE = 0x9C       # type record: Bumper.WhichTrackType (1 kart, 2 water, 3 coaster)
COUNTS_STATE = type('CountsState', (), {})()


def counts_stubs(target: Target) -> dict:
    state = COUNTS_STATE

    def back(sb):
        thiscall_check(sb, state.thing, '0x4de130')
        sb.write(sb.arg(0), 'H', state.x['back'])
        return sb.arg(0), 4

    def length(sb):
        thiscall_check(sb, state.thing, '0x4ddf50')
        if sb.arg(0) & 0xFFFF:
            raise AssertionError('0x4ddf50 asked for a guest, not the list length')
        return state.x['length'] & MASK32, 4

    def lookup(sb):
        thiscall_check(sb, POINTS_REGISTRY, '0x412e90')
        return state.record, 4

    def coaster(sb):
        target.log.append(('coaster',))
        return int(state.x['controller_ok']), 0

    return {COUNTS_BACK: back, COUNTS_LENGTH: length, POINTS_TYPE_RECORD: lookup, COUNTS_COASTER: coaster}


def counts_binary(sb: Sandbox, x: dict, log) -> int:
    state = COUNTS_STATE
    state.x = x
    state.record = sb.object(0x800, {COUNTS_TRACK_TYPE: ('i', x['track'])})
    f = COUNTS_THING
    state.thing = sb.object(0x400, {f['flags']: ('H', x['flags']), f['state']: ('i', x['state']), f['open']: ('i', x['open']),
                                    f['cells']: ('i', x['cells']), f['track_word']: ('i', x['track_word']), f['type']: ('H', 0x101)})
    result = sb.call(COUNTS_ENTRY, this=state.thing)
    if result.esp_delta != 0:
        raise AssertionError(f'stack off by {result.esp_delta}')
    return result.eax & 0xFF


def counts_reading(x: dict) -> int:
    """0x4dd920 as read: flags +0x32 bit 4 (IsChoosable), state not 1 (broken down) or 4 (condemned), open word +0x68 nonzero,
    a back-of-queue cell, a list shorter than 4 x the queue cells (unsigned), then by track type: a coaster (3) needs the
    controller test, a kart (1) or water ride (2) needs the word +0x2c."""
    if not (x['flags'] & 4 and x['state'] not in (1, 4) and x['open'] and x['back']):
        return 0
    if (x['length'] & MASK32) >= ((x['cells'] << 2) & MASK32):
        return 0
    if x['track'] == 3 and not x['controller_ok']:
        return 0
    if x['track'] in (1, 2) and not x['track_word']:
        return 0
    return 1


def counts_csharp_rule(x: dict) -> int:
    """ParkEconomy.CountsForParkPoints with the level's sync, in the routine's terms: IsChoosable, not broken (states 1 and 4),
    open, and QueueHasRoom (QueueJoinRules.QueueCountsForParkPoints: real queue cells > 0, which is when the back cell exists,
    and length < 4 x cells), a coaster (track type 3) only while CoasterRide.AcceptsGuests holds and a kart or water ride
    (1, 2) only with its track word (BumpController.IsTrackValid). This transcribes the C# rule; the C# helper itself is
    covered by a unit test (ARRIVAL-003)."""
    has_room = x['back'] != 0 and x['cells'] > 0 and x['length'] < 4 * x['cells']
    counts = bool(x['flags'] & 4) and x['state'] not in (1, 4) and bool(x['open']) and has_room
    counts = counts and not (x['track'] == 3 and not x['controller_ok'])
    counts = counts and not (x['track'] in (1, 2) and not x['track_word'])
    return int(counts)


def counts_inputs(rng: random.Random, n: int) -> list:
    def make(**kw) -> dict:
        cells = rng.choice([0, 1, 2, 3, 6, rng.randrange(0, 30)])
        x = {'flags': rng.choice([4, 4, 4, 0, 5, 0x24, rng.randrange(0, 0x10000)]), 'state': rng.choice([0, 0, 0, 1, 2, 3, 4]),
             'open': rng.choice([1, 1, 1, 0]), 'cells': cells, 'back': 0 if cells == 0 else rng.choice([0x1234, 0x1234, 0x1234, 0]),
             'length': rng.choice([0, 4 * cells - 1, 4 * cells, rng.randrange(0, 4 * cells + 6)]),
             'track': rng.choice([0, 0, 1, 2, 3, 3, 4]), 'controller_ok': rng.choice([True, False]), 'track_word': rng.choice([1, 1, 0])}
        x['length'] = max(0, x['length'])
        x.update(kw)
        return x
    base = dict(flags=4, state=0, open=1, cells=2, back=0x1234, length=3, track=0, controller_ok=True, track_word=1)
    edges = [make(**{**base, **change}) for change in (
        {}, {'flags': 0}, {'state': 1}, {'state': 4}, {'state': 2}, {'open': 0}, {'back': 0}, {'length': 8}, {'length': 7},
        {'cells': 0, 'back': 0, 'length': 0}, {'track': 3, 'controller_ok': False}, {'track': 3}, {'track': 1, 'track_word': 0},
        {'track': 2, 'track_word': 0}, {'track': 2})]
    return edges + [make() for _ in range(n)]


def counts_domain_csharp(x: dict) -> bool:
    return x['track'] not in (1, 2, 3)


COUNTS_TARGET = Target(
    'tpw-exe-counts-test', 'tpw', 'tp.exe park points counts-test 0x4dd920', 'GUESTS.md "Arrivals" / ARRIVAL-003 (Mac 0xdc834)',
    COUNTS_ENTRY, [COUNTS_RANGE], counts_stubs, counts_binary, counts_reading, counts_inputs,
    alternatives={'csharp-rule': counts_csharp_rule,
                  'csharp-rule-untracked': lambda x: counts_csharp_rule(x) if counts_domain_csharp(x) else counts_reading(x)},
    notes=['Executed: 0x4dd920. Stubs: 0x4de130 back of queue (chosen; checked for the thing), 0x4ddf50 list length (checked '
           'to be asked with guest 0), 0x412e90 type record (track type chosen), 0x441970 coaster controller test (chosen). '
           'csharp-rule applies every condition, the coaster test and the kart/water word included (ARRIVAL-003); '
           'csharp-rule-untracked takes the routine reading for those types, so both must agree everywhere.'])

# ============================================================================== (i) tp.exe price test 0x4fde50
PRICE_ENTRY = 0x4FDE50         # thiscall (guest, ride), ret 4 -> 1 when the ride is too expensive for the guest
PRICE_RANGES = [(0x4FDE50, 0x4FE182),       # the routine (its jump table at 0x4fe184 is data)
                (0x4E1A00, 0x4E1A07), (0x4E1A10, 0x4E1A17), (0x4E21B0, 0x4E21B7),   # price, cost of goods, win byte getters
                FTOL]
PRICE_TYPE_RECORD = 0x4DD4E0   # thiscall (thing) -> its type record; stubbed (synthetic record)
PRICE_PARK_RECORD = 0x4E2740   # thiscall (thing) -> the park's record for the type (0x4d3d10); stubbed (its word +0xc chosen)
PRICE_STATISTIC = 0x4C74B0     # thiscall (park, category, value), ret 8: the price perception statistic; stubbed (recorded)
PRICE_GUEST = {'happiness': 0x19C, 'money': 0x1A0, 'thirst': 0x1A4, 'hunger': 0x1A8, 'illness': 0x1B0}
PRICE_RIDE = {'goods': 0x188, 'win': 0x190, 'price': 0x194}   # mCostOfGoods, mChanceOfWinning (byte), mPricePerUse
PRICE_RECORD = {'base': 0x140, 'thirst': 0x144, 'hunger': 0x148, 'vomit': 0x14C, 'happy': 0x150, 'product': 0x158, 'extra': 0x15C}
PRICE_STATE = type('PriceState', (), {})()   # record: InitCostOfGoods, Thirst/Hunger/Vomit/HappinessEffect; kind word +0x4ac


def price_stubs(target: Target) -> dict:
    state = PRICE_STATE

    def record(sb):
        thiscall_check(sb, state.ride, '0x4dd4e0')
        return state.record, 0

    def park_record(sb):
        thiscall_check(sb, state.ride, '0x4e2740')
        return state.park_record, 0

    def park(sb):
        thiscall_check(sb, state.game, '0x519510')
        return state.park, 0

    def statistic(sb):
        thiscall_check(sb, state.park, '0x4c74b0')
        target.log.append(('statistic', sb.arg(0), sb.arg(1)))
        return 0, 8

    return {PRICE_TYPE_RECORD: record, PRICE_PARK_RECORD: park_record, PARK_OF_GAME: park, PRICE_STATISTIC: statistic}


def price_binary(sb: Sandbox, x: dict, log) -> tuple:
    state = PRICE_STATE
    state.game = bind_game(sb)
    state.park = sb.object(16)
    r = PRICE_RECORD
    state.record = sb.object(0x800, {r['base']: ('i', x['base']), r['thirst']: ('i', x['e_thirst']), r['hunger']: ('i', x['e_hunger']),
                                     r['vomit']: ('i', x['e_vomit']), r['happy']: ('i', x['e_happy']), r['product']: ('i', x['product']),
                                     r['extra']: ('i', x['extra']), POINTS_KIND: ('i', x['kind'])})
    state.park_record = sb.object(32, {0xC: ('i', x['type_word'])})
    state.ride = sb.object(0x400, {PRICE_RIDE['goods']: ('i', x['goods']), PRICE_RIDE['win']: ('B', x['win'] & 0xFF),
                                   PRICE_RIDE['price']: ('I', x['price'] & MASK32), 0xE: ('H', 0x101)})
    g = PRICE_GUEST
    guest = sb.object(0x400, {g['happiness']: ('f', x['happiness']), g['money']: ('I', x['money'] & MASK32),
                              g['thirst']: ('f', x['thirst']), g['hunger']: ('f', x['hunger']), g['illness']: ('f', x['illness'])})
    sb.set_fpcw(X87_CONTROL_WORD)
    result = sb.call(PRICE_ENTRY, args=(state.ride,), this=guest)
    if result.esp_delta != 0:
        raise AssertionError(f'stack off by {result.esp_delta}')
    return result.eax & 0xFF, tuple(e[1:] for e in log if e[0] == 'statistic')


def _byte(value: float) -> int:
    """The routine reads the meter as a float32 and truncates it (fistp), keeping the low byte."""
    return fistp_low32(struct.unpack('<f', struct.pack('<f', value))[0]) & 0xFF


def _sdiv100(value: int) -> int:
    value = s32(value)
    return trunc_div(value, 100)


def price_willingness(x: dict) -> int:
    """0x4fde50's willingness to pay, an unsigned 32-bit word."""
    h = _byte(x['happiness'])
    total = s32(_sdiv100(x['e_happy'] * (100 - h)) + _sdiv100(x['e_thirst'] * _byte(x['thirst'])) + _sdiv100(x['e_hunger'] * _byte(x['hunger']))
                - _sdiv100(x['e_vomit'] * _byte(x['illness'])) + 100)
    base = _sdiv100(x['base'] * 115)
    value = ((total * base) & MASK32) // 100
    sideshow = _sdiv100(x['goods'] * (x['win'] & 0xFF)) if x['kind'] == 2 else 0
    value = (((sideshow + value) * (x['type_word'] + 100)) & MASK32) // 100
    return ((value * (h + 100)) & MASK32) // 100


def price_reading(x: dict) -> tuple:
    """0x4fde50 as read: a price of 0 is never too expensive; otherwise too expensive when the price exceeds the willingness or
    the guest's money (both unsigned). The statistic min(100, (W + 5) x 10 - price x 10) goes to category 1..4 by the record's
    product word (+0x158) and 6/7 by +0x15c for a shop (kind 1), to 9 for a sideshow (kind 2)."""
    price = x['price'] & MASK32
    if price == 0:
        return 0, ()
    w = price_willingness(x)
    def perceived():
        v = ((w + 5) * 10 - price * 10) & MASK32
        return 100 if v >= 100 else v
    stats = []
    if x['kind'] == 1:
        if 1 <= x['product'] <= 4:
            stats.append((x['product'], perceived()))
        if x['extra'] in (1, 2):
            stats.append((x['extra'] + 5, perceived()))
    elif x['kind'] == 2:
        stats.append((9, perceived()))
    expensive = not (price <= w and price <= (x['money'] & MASK32))
    return int(expensive), tuple(stats)


def price_inputs(rng: random.Random, n: int) -> list:
    def meter() -> float:
        return rng.choice([0.0, 100.0, 50.0, float(rng.randrange(0, 101)), rng.uniform(0, 100)])

    def make(**kw) -> dict:
        x = {'happiness': meter(), 'thirst': meter(), 'hunger': meter(), 'illness': meter(),
             'money': rng.choice([0, 50, 200, 1000, rng.randrange(0, 3000)]),
             'price': rng.choice([0, 1, 5, 10, 20, 50, rng.randrange(0, 400)]),
             'goods': rng.choice([0, 10, 50, rng.randrange(0, 300)]), 'win': rng.choice([0, 30, 70, 100, rng.randrange(0, 256)]),
             'kind': rng.choice([0, 1, 1, 2, 3]), 'base': rng.choice([0, 5, 10, 20, 50, rng.randrange(0, 300)]),
             'e_thirst': rng.choice([0, 20, 50, rng.randrange(-50, 101)]), 'e_hunger': rng.choice([0, 20, 50, rng.randrange(-50, 101)]),
             'e_vomit': rng.choice([0, 10, rng.randrange(-20, 60)]), 'e_happy': rng.choice([0, 10, 30, rng.randrange(-30, 101)]),
             'product': rng.choice([0, 1, 2, 3, 4, 5]), 'extra': rng.choice([0, 0, 1, 2, 3]),
             'type_word': rng.choice([0, 0, 0, 10, -20, rng.randrange(-100, 200)])}
        x.update(kw)
        return x
    edges = [make(price=p, money=m, kind=1, base=20, e_thirst=40, e_hunger=0, e_vomit=0, e_happy=10, type_word=0,
                  happiness=60.0, thirst=80.0, hunger=10.0, illness=0.0, goods=0, win=0, product=2, extra=0)
             for p in (0, 1, 30, 31, 32, 40) for m in (0, 31, 1000)]
    # meters that round up to the next integer as float32 (1.99999999 is stored as 2.0) and that sit just below one
    edges += [make(price=p, money=1000, kind=1, base=150, e_thirst=40, e_hunger=0, e_vomit=0, e_happy=-30, type_word=0,
                   happiness=h, thirst=79.99999999, hunger=10.0, illness=0.0, goods=0, win=0, product=0, extra=0)
              for h in (1.99999999, 1.9999, 2.0) for p in (163, 164, 165, 166)]
    return edges + [make() for _ in range(n)]


PRICE_TARGET = Target(
    'tpw-exe-price-test', 'tpw', 'tp.exe price test 0x4fde50', 'GUEST-003 (the boarding price test; no Mac counterpart traced)',
    PRICE_ENTRY, PRICE_RANGES, price_stubs, price_binary, price_reading, price_inputs,
    alternatives={'no-money-test': lambda x: (int(bool(x['price'] & MASK32) and (x['price'] & MASK32) > price_willingness(x)),
                                              price_reading(x)[1])},
    notes=['Executed: 0x4fde50, the price, cost of goods and win byte getters, _ftol. Stubs: 0x4dd4e0 type record (synthetic: '
           '+0x140 InitCostOfGoods, +0x144..+0x150 Thirst/Hunger/Vomit/HappinessEffect, +0x158 / +0x15c product words, '
           '+0x4ac kind), 0x4e2740 park type record (word +0xc chosen), 0x519510 park, 0x4c74b0 statistic (recorded). '
           'x87 control word 0x27F.'])

# ============================================================================== (j) tp.exe toilet wear 0x4e2440
WEAR_ENTRY = 0x4E2440          # thiscall (thing, toilet byte), ret 4: a relief object's gauge after one use
WEAR_RANGES = [(0x4E2440, 0x4E2647), FTOL]
WEAR_GUARD = 0xFB34B8          # bit 0: the GameType object is constructed (its constructor 0x550ca0 must not run)
WEAR_MODE = 0xFB3B7C           # GameType word 0: 1 = online (the self-cleaning branch, not compared here)
WEAR_MAP = 0x8023A0            # the cell map object the dirty-toilet messages go to
WEAR_MESSAGE_A = 0x4D8460      # thiscall (map, 1, cell), ret 8; stubbed (recorded)
WEAR_MESSAGE_B = 0x4D8440      # thiscall (map, 6, cell), ret 8; stubbed (recorded)
WEAR_STATE = type('WearState', (), {})()
_F32 = lambda v: struct.unpack('<f', struct.pack('<f', v))[0]


def wear_stubs(target: Target) -> dict:
    state = WEAR_STATE

    def message(name):
        def stub(sb):
            thiscall_check(sb, state.map, name)
            target.log.append((name, sb.arg(0), sb.arg(1) & 0xFFFF))
            return 0, 8
        return stub

    def log(sb):
        target.log.append(('log', sb.arg(0)))
        return 0, 0

    return {WEAR_MESSAGE_A: message('0x4d8460'), WEAR_MESSAGE_B: message('0x4d8440'), DEBUG_PRINT: log}


def wear_binary(sb: Sandbox, x: dict, log) -> tuple:
    state = WEAR_STATE
    sb.write(WEAR_GUARD, 'B', 1)
    sb.write(WEAR_MODE, 'I', 0)
    state.map = sb.object(16)
    sb.write(WEAR_MAP, 'I', state.map)
    thing = sb.object(0x200, {0x32: ('H', 1 if x['relief'] else 0), 0x44: ('f', x['gauge']), 5: ('B', x['cx']), 7: ('B', x['cy'])})
    sb.set_fpcw(X87_CONTROL_WORD)
    result = sb.call(WEAR_ENTRY, args=(x['toilet'] & 0xFF,), this=thing)
    if result.esp_delta != 0:
        raise AssertionError(f'stack off by {result.esp_delta}')
    gauge = sb.read(thing + 0x44, 'I')
    return gauge, tuple(e for e in log if e[0] != 'log'), sum(1 for e in log if e[0] == 'log')


def _gauge_byte(value: float) -> int:
    return fistp_low32(_F32(value)) & 0xFF


def wear_reading(x: dict, constant: float | None = None) -> tuple:
    """0x4e2440 offline: a relief object's gauge += toilet x -0.05f (exact in x87 precision), clamped to 0..100, stored as a single;
    a gauge byte that falls below 25 (and was not below it) logs once and posts messages 1 and 6 for the thing's cell."""
    gauge = _F32(x['gauge'])
    step = _F32(-0.05) if constant is None else constant
    value = Fraction(x['toilet'] & 0xFF) * Fraction(step) + Fraction(gauge)
    new = 100.0 if value > 100 else (_F32(float(x87_round(value, 53))) if value >= 0 else 0.0)
    bits = struct.unpack('<I', struct.pack('<f', new))[0]
    was = x['relief'] and _gauge_byte(gauge) < 25
    now = x['relief'] and _gauge_byte(new) < 25
    cell = (x['cy'] * 0x80 + 1 + x['cx']) & 0xFFFF
    events = (('0x4d8460', 1, cell), ('0x4d8440', 6, cell)) if now and not was else ()
    return bits, events, len(events) // 2


def wear_inputs(rng: random.Random, n: int) -> list:
    def make(**kw) -> dict:
        x = {'gauge': rng.choice([100.0, 50.0, 25.0, 24.99, 25.04, 0.0, 3.3, rng.uniform(0, 100)]), 'toilet': rng.choice([0, 1, 100, 255, rng.randrange(256)]),
             'relief': rng.choice([True, True, False]), 'cx': rng.randrange(128), 'cy': rng.randrange(128)}
        x.update(kw)
        return x
    edges = [make(gauge=g, toilet=t, relief=True) for g in (100.0, 25.5, 25.04, 25.0, 24.99, 1.0, 0.0) for t in (0, 1, 10, 255)]
    return edges + [make() for _ in range(n)]


WEAR_TARGET = Target(
    'tpw-exe-toilet-wear', 'tpw', 'tp.exe toilet wear 0x4e2440', 'QUEUE-008 (the relief wear step; ParkEconomy.UseRelief)',
    WEAR_ENTRY, WEAR_RANGES, wear_stubs, wear_binary, wear_reading, wear_inputs,
    alternatives={'double-constant': lambda x: wear_reading(x, -0.05)},
    notes=['Executed: 0x4e2440 in the offline game type and _ftol. Stubs: the dirty-toilet messages 0x4d8460 / 0x4d8440 (recorded '
           'with their cell), 0x5da3c0 log (counted); the GameType object is marked constructed. x87 control word 0x27F. '
           'ParkEconomy.UseRelief computes the same in double precision (the product of a byte and a single plus a single is exact).'])

# ============================================================================== tp.exe guest decision state 6 0x4fec90 (GUEST-010)
STATE6_ENTRY = 0x4FEC90        # thiscall (guest), no arguments: the state-6 handler (dispatcher 0x5019f0 case 6)
STATE6_RANGES = [(0x4FEC90, 0x4FF4D8), FTOL, (DEBUG_PRINT, DEBUG_PRINT + 1)]
STATE6_GUEST = {'x': 0x5, 'y': 0x7, 'happiness': 0x19C, 'illness': 0x1B0, 'litter': 0x1B4, 'exit': 0x1BC, 'prank': 0x1C0,
                'ride': 0x1DC, 'decision': 0x1FC, 'activity': 0x208, 'state': 0x220}
STATE6_CALLS = {
    'rand': 0x516330,          # thiscall (game) -> the world draw; scripted
    'activity': 0x4FC800,      # thiscall (guest, n), ret 4: animation n, +0x208 = turn, +0x224 = state, state 8; recorded
    'litter': 0x4D93B0,        # thiscall (cell record, type), ret 4: litter on the cell; recorded
    'event': 0x50C100,         # thiscall (guest + 0x30, event, thing word), ret 8: a guest event for the log and thoughts; recorded
    'sound': 0x4FAA00,         # thiscall (guest, bank, id), ret 8; recorded
    'bin': 0x500DC0,           # thiscall (guest, out, radius), ret 8: the bin search; never reached (litter meter 0)
    'happiness': 0x4FEA70,     # thiscall (guest, selector), ret 4: the small, medium or big happiness change; recorded
    'closed': 0x51A280,        # thiscall (game): the park-closed word; from the input
    'gate_prep': 0x51A290,     # thiscall (game): called before the gate routes; nothing to model
    'gate_cell': 0x4D86D0,     # thiscall (cell array, out, index), ret 8: the two gate cells; synthetic ids
    'route': 0x4FA530,         # thiscall (guest, cell), ret 4: a route to a gate cell; from the input
    'show': 0x501020,          # thiscall (guest, out), ret 4: the nearest show (presentation); none
    'performers': 0x4C8EB0,    # thiscall (park, 6, cell, 1), ret 0xc: a performer near the cell (presentation); none
    'choice': 0x4FCB10,        # thiscall (guest): the major choice 0x4fcb10 (X16 loop); sets +0x1dc from the input; recorded
    'wander': 0x4F9490,        # thiscall (guest): the wander destination (target tpw-exe-wander-destination); from the input; recorded
    'state': 0x501DB0,         # thiscall (guest, state), ret 4: the state setter; recorded
    'guest_id': 0x50B350,      # thiscall (guest, out), ret 4: the guest's thing id
    'message': 0x40FB10,       # thiscall (bus, message), ret 4: the message bus; the prank message (type 14) is recorded
    'burst': 0x4FE950,         # thiscall (victim): the balloon burst; recorded
}
STATE6_BUS = 0x788D3C          # the message bus, built on first use; pre-set so the run never allocates it
STATE6_STINKBOMB = 0x7850E0    # PeepInfo.StinkbombLikelihood
STATE6_SELF_ID = 7             # the guest's thing id (0x50b350 stub)
STATE6_CHANGES = {0: 5, 1: 15, 2: 25}   # Small/Medium/BigHappinessChange, the bytes at 0x785058 / 0x78505c / 0x785060 (shipped values)


def _happiness_change( happiness: float, amount: int ) -> float:
    value = happiness - amount
    return 100.0 if value > 100.0 else 0.0 if value < 0.0 else struct.unpack('<f', struct.pack('<f', value))[0]
STATE6_CELLS = 0x8023A0        # global: the map's cell records (0x44 bytes each, id = y * 128 + x + 1)
STATE6_STATE = type('State6State', (), {})()


def state6_stubs(target: Target) -> dict:
    state = STATE6_STATE
    c = STATE6_CALLS

    def guest(sb, what):
        thiscall_check(sb, state.guest, what)

    def rand(sb):
        thiscall_check(sb, state.game, '0x516330')
        if not state.draws:
            raise AssertionError('state 6 drew more than scripted')
        return state.draws.pop(0), 0

    def activity(sb):
        guest(sb, '0x4fc800')
        target.log.append(('activity', sb.arg(0)))
        return 0, 4

    def litter(sb):
        target.log.append(('litter', sb.arg(0)))
        return 0, 4

    def event(sb):
        thiscall_check(sb, state.guest + 0x30, '0x50c100')
        target.log.append(('event', sb.arg(0)))
        return 0, 8

    def sound(sb):
        guest(sb, '0x4faa00')
        target.log.append(('sound', sb.arg(1)))
        return 0, 8

    def bin_search(sb):
        guest(sb, '0x500dc0')
        target.log.append(('bin_search', sb.arg(1)))
        sb.write(sb.arg(0), 'H', state.x['bin'])
        return 0, 8

    def happiness(sb):
        # 0x4fea70 as read: subtract the selector's settings byte (0x785058 / 5c / 60) from the happiness float, clamped to 0..100
        guest(sb, '0x4fea70')
        target.log.append(('happiness', sb.arg(0)))
        address = state.guest + STATE6_GUEST['happiness']
        sb.write(address, 'f', _happiness_change( sb.read(address, 'f'), STATE6_CHANGES[sb.arg(0)] ))
        return 0, 4

    def closed(sb):
        thiscall_check(sb, state.game, '0x51a280')
        return int(state.x['closed']), 0

    def gate_prep(sb):
        return 0, 0

    def gate_cell(sb):
        sb.write(sb.arg(0), 'H', 0x1000 + sb.arg(1))
        return sb.arg(0), 8

    def route(sb):
        guest(sb, '0x4fa530')
        target.log.append(('route', sb.arg(0) & 0xFFFF))
        return int(state.x['way_out']), 4

    def show(sb):
        sb.write(sb.arg(0), 'H', 0)
        return sb.arg(0), 4

    def park(sb):
        return state.park, 0

    def performers(sb):
        return 0, 0xC

    def choice(sb):
        guest(sb, '0x4fcb10')
        target.log.append(('choice',))
        sb.write(state.guest + STATE6_GUEST['ride'], 'H', state.x['ride'])
        return 0, 0

    def wander(sb):
        guest(sb, '0x4f9490')
        target.log.append(('wander',))
        return int(state.x['wander']), 0

    def set_state(sb):
        guest(sb, '0x501db0')
        target.log.append(('state', sb.arg(0)))
        return 0, 4

    def guest_id(sb):
        sb.write(sb.arg(0), 'H', STATE6_SELF_ID)
        return sb.arg(0), 4

    def message(sb):
        thiscall_check(sb, state.bus, '0x40fb10')
        msg = sb.arg(0)
        target.log.append(('message', sb.read(msg, 'I'), sb.read(msg + 8, 'H'), sb.read(msg + 0xC, 'I')))
        return 0, 4

    def burst(sb):
        target.log.append(('burst', state.things.get(sb.reg('ecx'))))
        return 0, 0

    return {c['message']: message, c['burst']: burst, c['rand']: rand, c['activity']: activity, c['litter']: litter, c['event']: event, c['sound']: sound,
            c['bin']: bin_search, c['happiness']: happiness, c['closed']: closed, c['gate_prep']: gate_prep,
            c['gate_cell']: gate_cell, c['route']: route, c['show']: show, PARK_OF_GAME: park, c['performers']: performers,
            c['choice']: choice, c['wander']: wander, c['state']: set_state, c['guest_id']: guest_id}


def state6_binary(sb: Sandbox, x: dict, log) -> tuple:
    """(the recorded effects without the gate routes, +0x1fc after, the illness bits after). The routes to the gate cells are
    checked here instead: the gate passes iff a route was tried, and the first route found ends the tries."""
    state = STATE6_STATE
    state.x = x
    state.draws = [x['r'], x['r2']]
    state.game = bind_game(sb, fields={WORLD_RNG + 4: ('I', x['turn'] & MASK32)})
    state.park = sb.object(16)
    sb.write(STATE6_CELLS, 'I', sb.object(0x44 * 0x80 * 0x40))
    state.bus = sb.object(16)
    sb.write(STATE6_BUS, 'I', state.bus)
    sb.write(STATE6_STINKBOMB, 'I', x['stink'])
    g = STATE6_GUEST
    state.guest = sb.object(0x400, {g['x']: ('B', 20), g['y']: ('B', 30), g['happiness']: ('f', x['happiness']),
                                    g['illness']: ('f', x['illness']), g['litter']: ('f', x['litter']), g['exit']: ('i', x['exit']),
                                    g['prank']: ('B', x['prank']), g['decision']: ('I', x['decision'] & MASK32),
                                    g['activity']: ('I', x['activity'] & MASK32), g['state']: ('I', 6)})
    # the guest's cell (20, 30) holds the things of x['things'] in list order: 'self', or (type byte, balloon) for another thing
    state.things = {}
    indices = [STATE6_SELF_ID if thing == 'self' else 40 + n for n, thing in enumerate(x['things'])]
    for n, (thing, index) in enumerate(zip(x['things'], indices)):
        next_index = indices[n + 1] if n + 1 < len(indices) else 0
        if thing == 'self':
            address = state.guest
            sb.write(address + 2, 'B', 1)
        else:
            address = sb.object(0x400, {2: ('B', thing[0]), 0x210: ('I', thing[1])})
        sb.write(address + 0xA, 'H', next_index)
        sb.write(POINTS_THINGS + 20 * index, 'I', address)
        state.things[address] = index
    sb.write(state.game + 0x2B8 + 0x44 * (30 * 128 + 20 + 1), 'H', indices[0] if indices else 0)
    sb.set_fpcw(X87_CONTROL_WORD)
    result = sb.call(STATE6_ENTRY, this=state.guest)
    if result.esp_delta != 0:
        raise AssertionError(f'stack off by {result.esp_delta}')
    routes = [e for e in log if e[0] == 'route']
    if routes and x['way_out'] and len(routes) != 1:
        raise AssertionError('more routes tried after one was found')
    if routes and not x['way_out'] and len(routes) != 4:
        raise AssertionError('the gate tries both cells twice')
    # the gate's big change comes first, then its routes: one ('gate',) entry stands where the change was recorded
    effects = []
    for entry in log:
        if entry == ('happiness', 2) and routes and ('gate',) not in effects:
            effects.append(('gate',))
        if entry[0] != 'route':
            effects.append(entry)
    effects = tuple(effects)
    return (effects, sb.read(state.guest + g['decision'], 'I'), sb.read(state.guest + g['illness'], 'I'), sb.read(state.guest + g['litter'], 'I'),
            sb.read(state.guest + g['ride'], 'H'), sb.read(state.guest + g['happiness'], 'I'))


def _f32_bits(value: float) -> int:
    return struct.unpack('<I', struct.pack('<f', value))[0]


def state6_plan(x: dict, reread: bool = True) -> tuple:
    """docs/reverse/WANDER-plan.md §2 as GuestDecision runs it, with the original's own effects of each branch: cheering
    (activity 5), vomiting (activity 7, litter 7, event 18, sound 0xcc, illness 0), the litter step (the bin search with radius 3:
    a bin sets +0x1dc and state 9; none drops litter 1 + R2 % 5 and empties the meter), the leaving gate (the big change, a route,
    state 18), the major choice (event 2 and state 10, or event 1, activity 4, the small change and the stamp) and the wander
    (state 7, or the stamp)."""
    turn, r = x['turn'] & MASK32, x['r']
    decision, illness, litter, ride = x['decision'] & MASK32, _f32_bits(x['illness']), _f32_bits(x['litter']), 0
    happiness = _byte(x['happiness'])
    level = struct.unpack('<f', struct.pack('<f', x['happiness']))[0]

    def result(effects):
        return tuple(effects), decision, illness, litter, ride, _f32_bits(level)
    if (turn - x['activity']) & MASK32 > 100 and happiness > 80:
        return result([('activity', 5)])
    ill = _byte(x['illness'])
    if ill > 80 and ill == 100 and r % 3 == 0:
        illness = 0
        return result([('activity', 7), ('litter', 7), ('event', 0x12), ('sound', 0xCC)])
    effects = []
    if _byte(x['litter']) >= 90:
        effects.append(('bin_search', 3))
        if x['bin']:
            ride = x['bin']
            return result(effects + [('state', 9)])
        effects.append(('litter', 1 + x['r2'] % 5))
        litter = 0
    if happiness == 0 or x['exit'] == 0 or x['closed']:
        effects += [('gate',), ('happiness', 2)]
        level = _happiness_change( level, STATE6_CHANGES[2] )
        if x['way_out']:
            return result(effects + [('state', 18)])
    # pranks (GuestDecision): happiness byte below 15 and the flag above R % 101
    if _byte(level if reread else x['happiness']) < 15 and x['prank'] > r % 101:
        pranked = False
        if x['prank'] == 100:
            if r % 100 < x['stink']:
                effects += [('litter', 8), ('sound', 0xCF)]
                pranked = True
        elif x['prank'] == 101:
            effects.append(('litter', r % 5 + 1))
            pranked = True
        elif x['prank'] == 102:
            for n, thing in enumerate(x['things']):
                if thing != 'self' and thing[0] == 1 and thing[1]:
                    effects.append(('burst', 40 + n))
                    pranked = True
                    break
        if pranked:
            effects += [('event', 0xF), ('message', 0x7007A8, STATE6_SELF_ID, STATE6_SELF_ID % 3)]
            level = _happiness_change( level, -1 )
    if r % 3 == 0:
        if (decision + 30) & MASK32 < turn:
            effects.append(('choice',))
            ride = x['ride']
            if x['ride']:
                effects += [('event', 2), ('state', 10)]
            else:
                effects += [('event', 1), ('activity', 4), ('happiness', 0)]
                level = _happiness_change( level, STATE6_CHANGES[0] )
                decision = turn
    elif r % 3 == 1:
        effects.append(('wander',))
        if x['wander']:
            effects.append(('state', 7))
        else:
            decision = turn
    return result(effects)


def state6_inputs(rng: random.Random, n: int) -> list:
    def meter(edges):
        return rng.choice(edges + [float(rng.randrange(0, 101)), rng.uniform(0, 100)])

    def make(**kw) -> dict:
        turn = rng.choice([rng.randrange(0, 1 << 32), rng.randrange(0, 200), rng.randrange(1000, 100000)])
        x = {'turn': turn,
             'activity': rng.choice([(turn - rng.randrange(0, 200)) & MASK32, turn, (turn + 5) & MASK32, rng.randrange(0, 1 << 32)]),
             'decision': rng.choice([(turn - rng.randrange(0, 60)) & MASK32, (turn - 30) & MASK32, (turn - 31) & MASK32, 0,
                                     rng.randrange(0, 1 << 32)]),
             'happiness': meter([0.0, 0.5, 1.0, 14.9, 14.99, 15.0, 10.0, 50.0, 80.0, 80.99, 81.0, 100.0]),
             'illness': meter([0.0, 80.0, 99.99, 100.0, 100.0]),
             'exit': rng.choice([0, 0, -1, 1, 50, rng.randrange(-100, 300)]),
             'closed': rng.random() < 0.1, 'r': rng.randrange(0, 1 << 31), 'r2': rng.randrange(0, 1 << 31),
             'litter': rng.choice([0.0, 0.0, 89.99, 90.0, 95.5, 100.0, rng.uniform(0, 100)]), 'bin': rng.choice([0, 0, 5, 0x77]),
             'prank': rng.choice([0, 0, 100, 101, 102, 99]), 'stink': rng.choice([0, 25, 100, rng.randrange(0, 101)]),
             'things': rng.choice([[], ['self'], ['self', (1, 0x5150)], [(1, 0), (2, 0x5150), 'self', (1, 0x6160)], [(1, 0x5150)]]),
             'way_out': rng.random() < 0.7, 'ride': rng.choice([0, 0, 3, 0x1234]), 'wander': rng.random() < 0.7}
        x.update(kw)
        return x
    edges = [make(turn=t, activity=a, happiness=h, illness=0.0, exit=50, closed=False, r=2)
             for t, a in ((200, 99), (200, 100), (5, 450)) for h in (80.99, 81.0)]
    edges += [make(illness=i, r=r, happiness=50.0, activity=0, turn=50, exit=50, closed=False) for i in (99.99, 100.0) for r in (3, 4, 5)]
    edges += [make(turn=t, decision=100, r=r, happiness=50.0, illness=0.0, activity=t, exit=50, closed=False)
              for t in (130, 131) for r in (0, 1, 2)]
    edges += [make(litter=m, bin=b, r2=r2, happiness=50.0, illness=0.0, activity=0, turn=50, exit=50, closed=False, r=2)
              for m in (89.99, 90.0) for b in (0, 5) for r2 in (4, 5)]
    edges += [make(prank=flag, stink=s, r=r, happiness=h, litter=0.0, illness=0.0, activity=0, turn=50, exit=50, closed=False,
                   things=['self', (2, 1), (1, 0), (1, 0x4242)])
              for flag in (100, 101, 102) for s in (0, 25) for r in (24, 25, 100, 202) for h in (14.99, 15.0)]
    edges += [make(prank=101, stink=25, r=4, happiness=h, exit=0, way_out=False, litter=0.0, illness=0.0, activity=0, turn=50, closed=False)
              for h in (30.0, 39.5, 40.0, 41.0)]
    return edges + [make() for _ in range(n)]


STATE6_TARGET = Target(
    'tpw-exe-guest-state6', 'tpw', 'tp.exe guest state 6 0x4fec90', 'WANDER-plan §2 / GuestDecision (GUEST-010)',
    STATE6_ENTRY, STATE6_RANGES, state6_stubs, state6_binary, state6_plan, state6_inputs,
    alternatives={'prank-before-gate-change': lambda x: state6_plan(x, reread=False)},
    notes=['Executed: 0x4fec90 with its SEH frame, _ftol and the debug print. Stubs: the draw (scripted), the activity '
           '0x4fc800, litter 0x4d93b0, events 0x50c100, sound 0x4faa00, the happiness change 0x4fea70, the closed word '
           '0x51a280, the gate cells 0x4d86d0 and routes 0x4fa530, the presentation look-ups 0x501020 / 0x4c8eb0 (none), '
           'the choice 0x4fcb10 (+0x1dc from the input), the wander 0x4f9490 and the state setter 0x501db0, all recorded. '
           'The bin search 0x500dc0 is stubbed (radius recorded, +0x1dc from the input); the prank step runs on the input flag, with '
           'the cell\'s thing list (game +0x2b8 + 0x44 × cell id, next word at +0xa, the thing table 0x7cfb90), the balloon burst '
           '0x4fe950 and the message bus 0x40fb10 (pre-set at 0x788d3c) recorded. The routes are reduced to one "gate" entry. '
           'x87 control word 0x27F.'])


# ============================================================================== tp.exe wander destination 0x4f9490 (GUEST-010)
WANDER_ENTRY = 0x4F9490        # thiscall (guest) -> 1 when a destination is set
WANDER_RANGES = [(0x4F9490, 0x4F9E3E), (0x4D8C50, 0x4D8C5C), (0x4D8300, 0x4D8330), (0x522770, 0x522787), (0x522810, 0x522847),
                 (0x522850, 0x522854), (0x536310, 0x53635C), (DEBUG_PRINT, DEBUG_PRINT + 1)]
WANDER_DIRECTIONS = {0x7CDBA0: (0, -1), 0x7CDBA8: (0, 1), 0x7CDBC8: (1, 0), 0x7CDBD0: (-1, 0)}   # link 1, 16, 4, 64 (0x4d9a65..)
WANDER_CALLS = {'rand': 0x516330, 'clear': 0x4FA670, 'route': 0x510100, 'animation': 0x50BE80, 'stranded': 0x4FA770,
                'guest_id': 0x50B350, 'in_area': 0x506ED0, 'area_cell': 0x506F30}
WANDER_ORIGIN = (20, 30)       # the synthetic grid's corner in the 128 x 128 map
WANDER_SIZE = 7
WANDER_STATE = type('WanderState', (), {})()
LINK_OF = {(0, -1): 1, (1, 0): 4, (0, 1): 16, (-1, 0): 64}


def wander_stubs(target: Target) -> dict:
    state = WANDER_STATE
    c = WANDER_CALLS

    def rand(sb):
        thiscall_check(sb, state.game, '0x516330')
        if not state.draws:
            raise AssertionError('the wander drew more than scripted')
        state.used += 1
        return state.draws.pop(0), 0

    def clear(sb):
        thiscall_check(sb, state.guest, '0x4fa670')
        return 0, 4

    def route(sb):
        thiscall_check(sb, state.guest + 0xD4, '0x510100')   # the steering state +0xd4 (the Mac +212)
        target.log.append(('route', sb.arg(0), sb.arg(1)))
        return 1, 8

    def animation(sb):
        target.log.append(('animation', sb.arg(0)))
        return 0, 8

    def guest_id(sb):
        sb.write(sb.arg(0), 'H', 7)
        return sb.arg(0), 4

    def refuse(what):
        def stub(sb):
            raise AssertionError(f'{what} ran for a guest')
        return stub

    return {c['rand']: rand, c['clear']: clear, c['route']: route, c['animation']: animation, c['guest_id']: guest_id,
            c['stranded']: refuse('0x4fa770'), c['in_area']: refuse('0x506ed0'), c['area_cell']: refuse('0x506f30')}


def wander_binary(sb: Sandbox, x: dict, log) -> tuple:
    """(cell, byte x, byte y, draws used) of the route request, or (None, draws used, stranded) when no destination was set."""
    state = WANDER_STATE
    state.draws, state.used = list(x['draws']), 0
    state.game = bind_game(sb)
    for address, (dx, dy) in WANDER_DIRECTIONS.items():
        sb.write(address, 'i', dx)
        sb.write(address + 4, 'i', dy)
    cells = sb.object(0x44 * 0x80 * 0x80)
    sb.write(STATE6_CELLS, 'I', cells)
    for cx, cy, kind, links, queue_link in x['cells']:
        record = cells + 0x44 * (cy * 0x80 + cx)
        sb.write(record + 8, 'I', kind)
        sb.write(record + 0xC, 'B', links)
        sb.write(record + 0xD, 'B', queue_link)
    sx, sy = x['start']
    state.guest = sb.object(0x400, {0x2: ('B', 1), 0x5: ('B', sx), 0x7: ('B', sy)})
    result = sb.call(WANDER_ENTRY, this=state.guest)
    if result.esp_delta != 0:
        raise AssertionError(f'stack off by {result.esp_delta}')
    routes = [e for e in log if e[0] == 'route']
    if result.eax & 0xFF == 0:
        if routes:
            raise AssertionError('a route was asked for but the routine returned 0')
        return None, state.used, ('animation', 0x11) in log
    if len(routes) != 1:
        raise AssertionError(f'{len(routes)} route requests')
    _, ax, ay = routes[0]
    return ((ax >> 16, ay >> 16), (ax >> 8) & 0xFF, (ay >> 8) & 0xFF, state.used)


def wander_plan(x: dict, either_side: bool = False) -> tuple:
    """GuestWander.Pick: candidates are the current cell's links (& 0x55) to path cells, in the order +Y, +X, -Y, -X; fewer than two:
    the first (none: stranded); else the first from draw & 3 that is not the way back; draw % 5 steps plus one, one more each time the
    last ends on the start; bytes draw & 0x7f clamped to 5..123. A path cell without links gives its own centre."""
    draws = list(x['draws'])
    used = 0

    def draw():
        nonlocal used
        used += 1
        return draws.pop(0)
    cells = {(cx, cy): (kind, links, queue_link) for cx, cy, kind, links, queue_link in x['cells']}

    def path(c):
        return cells.get(c, (0, 0, 0))[0] == 1
    order = [(0, 1), (1, 0), (0, -1), (-1, 0)]
    start = tuple(x['start'])
    remaining = draw() % 5
    if not path(start):
        return None, used, False
    cell, back = start, -1

    def linked(d):
        n = (cell[0] + d[0], cell[1] + d[1])
        back_link = either_side and path(n) and cells[n][1] & LINK_OF[(-d[0], -d[1])]
        return path(n) and bool(cells[cell][1] & LINK_OF[d] or back_link)
    # The step branch needs a link of the start cell (0x522810); either-side links count a neighbour's link back as one.
    if not (any(linked(d) for d in order) if either_side else cells[start][1] & 0x55):
        return start, 0x80, 0x80, used
    while True:
        candidates = [d if linked(d) else None for d in order]
        count = sum(1 for d in candidates if d)
        if count == 0:
            return None, used, True
        if count < 2:
            chosen = next(i for i, d in enumerate(candidates) if d)
        else:
            first = draw() & 3
            chosen = first
            for offset in range(4):
                chosen = (first + offset) & 3
                if candidates[chosen] and chosen != back:
                    break
        d = candidates[chosen]
        cell = (cell[0] + d[0], cell[1] + d[1])
        back = (chosen + 2) & 3
        if cell == start and remaining == 0:
            remaining = 1
        if remaining == 0:
            break
        remaining -= 1
    bx = min(max(draw() & 0x7F, 5), 123)
    by = min(max(draw() & 0x7F, 5), 123)
    return cell, bx, by, used


def wander_inputs(rng: random.Random, n: int) -> list:
    ox, oy = WANDER_ORIGIN

    def grid(queues: float, density: float) -> dict:
        cells = {}
        for cx in range(ox, ox + WANDER_SIZE):
            for cy in range(oy, oy + WANDER_SIZE):
                roll = rng.random()
                if roll < density:
                    cells[(cx, cy)] = [1, 0, 0]
                elif roll < density + queues:
                    cells[(cx, cy)] = [3, 0, rng.choice([1, 4, 16, 64])]
        for (cx, cy), cell in cells.items():
            if cell[0] != 1:
                continue
            for d, link in LINK_OF.items():
                other = cells.get((cx + d[0], cy + d[1]))
                if other is None:
                    continue
                if other[0] == 1 and (cx, cy) < (cx + d[0], cy + d[1]) and rng.random() < 0.85:
                    side = rng.random()   # mostly both sides, as the path builder links; some one-sided links
                    if side < 0.9:
                        cell[1] |= link
                    if side > 0.1:
                        other[1] |= LINK_OF[(-d[0], -d[1])]
                elif other[0] == 3 and rng.random() < 0.5:
                    cell[1] |= link
        return {c: tuple(v) for c, v in cells.items()}

    def make(queues=0.15, density=0.6, linked=True) -> dict:
        while True:
            cells = grid(queues, density)
            starts = [c for c, v in cells.items() if v[0] == 1 and (not linked or any(
                cells.get((c[0] + d[0], c[1] + d[1]), (0,))[0] == 1 and v[1] & link for d, link in LINK_OF.items()))]
            if starts:
                return {'cells': sorted((c[0], c[1]) + v for c, v in cells.items()), 'start': rng.choice(sorted(starts)),
                        'draws': [rng.randrange(0, 1 << 31) for _ in range(24)]}

    def corridor(length: int, draws: list) -> dict:
        cells = [(ox + i, oy, 1, (4 if i + 1 < length else 0) | (64 if i > 0 else 0), 0) for i in range(length)]
        return {'cells': cells, 'start': (ox, oy), 'draws': draws + [0] * 20}
    edges = [corridor(2, [k]) for k in range(5)] + [corridor(1, [3]), corridor(5, [4, 0, 0, 0, 0, 0x7F, 0xFF])]
    edges += [make(linked=False, density=0.2) for _ in range(20)]
    return edges + [make() for _ in range(n)]


WANDER_TARGET = Target(
    'tpw-exe-wander-destination', 'tpw', 'tp.exe wander destination 0x4f9490', 'WANDER-plan §3 / GuestWander (GUEST-010)',
    WANDER_ENTRY, WANDER_RANGES, wander_stubs, wander_binary, wander_plan, wander_inputs,
    alternatives={'either-side-links': lambda x: wander_plan(x, either_side=True)},
    notes=['Executed: 0x4f9490 with its SEH frame, the cell readers 0x522770 / 0x522810 / 0x522850 / 0x536310..0x536350, the '
           'map bounds test 0x4d8300, the counter 0x4d8c50 and the debug print. Synthetic: the cell records ([0x8023a0], 0x44 '
           'bytes; type +8, links +0xc, queue link +0xd) on a 7 x 7 patch at (20, 30), path links mostly on both sides and some '
           'on one, path-to-queue links one way, no links to empty cells (the map keeps none); the direction table 0x7cdba0.. as its initialiser 0x4d9a65 writes it. Stubs: the draw (scripted), '
           '0x4fa670, the route request 0x510100 (always found; its point is the result), the stranded animation 0x50be80, '
           'the thing id 0x50b350; the staff calls 0x506ed0 / 0x506f30 and the stranded test 0x4fa770 must not run.'])

# ============================================================================== tp.exe littering a cell 0x4d93b0 (ECON-020)
LITTER_ENTRY = 0x4D93B0        # thiscall (cell record, type), ret 4: "Littering cell"
LITTER_RANGES = [(0x4D93B0, 0x4D95D3), (0x516330, 0x516365), (0x516370, 0x51637D), FTOL, (DEBUG_PRINT, DEBUG_PRINT + 1)]
LITTER_GAMETYPE = 0xFB3B7C     # the GameType object's word 0 (1 = online); 0xfb34b9 bit 0 marks it constructed
LITTER_GAMETYPE_GUARD = 0xFB34B9
LITTER_CALLS = {'sprite': 0x475A10, 'place': 0x475480, 'effect': 0x4D8440, 'particle': 0x521E60}
LITTER_SPRITES = {1: 0x74F050, 2: 0x74F078, 3: 0x74F0A0, 4: 0x74F0C8, 5: 0x74F0F0, 6: 0x74F118, 7: 0x74F140, 8: 0x74F168}
LITTER_STATE = type('LitterState', (), {})()


def litter_stubs(target: Target) -> dict:
    state = LITTER_STATE
    c = LITTER_CALLS

    def sprite(sb):
        target.log.append(('sprite', sb.arg(0), sb.arg(1)))
        return 0x5150, 0

    def place(sb):
        target.log.append(('place', sb.arg(0), struct.unpack('<f', struct.pack('<I', sb.arg(1)))[0],
                           struct.unpack('<f', struct.pack('<I', sb.arg(3)))[0]))
        return 0, 0

    def effect(sb):
        thiscall_check(sb, state.cells, '0x4d8440')
        target.log.append(('effect', sb.arg(0), sb.arg(1) & 0xFFFF))
        return 0, 8

    def particle(sb):
        target.log.append(('particle', sb.arg(0)))
        return 0x7070, 0

    return {c['sprite']: sprite, c['place']: place, c['effect']: effect, c['particle']: particle}


def litter_binary(sb: Sandbox, x: dict, log) -> tuple:
    """(the stored type, the sprite handle, the collector word, the world generator's state, the recorded calls)."""
    state = LITTER_STATE
    game = bind_game(sb, size=0x1E0000, fields={WORLD_RNG: ('I', x['seed'])})
    sb.write(LITTER_GAMETYPE_GUARD, 'B', 1)
    sb.write(LITTER_GAMETYPE, 'I', 1 if x['online'] else 0)
    state.cells = sb.object(0x44 * 0x80 * 0x40)
    sb.write(STATE6_CELLS, 'I', state.cells)
    cx, cy = x['cell']
    cell_id = cy * 128 + cx + 1
    record = state.cells + 0x44 * (cell_id - 1)
    sb.write(record, 'H', cell_id)
    sb.write(record + 0x2C, 'I', x['occupied'])
    sb.write(record + 0x30, 'H', 0x55)
    sb.set_fpcw(X87_CONTROL_WORD)
    result = sb.call(LITTER_ENTRY, args=(x['type'],), this=record)
    if result.esp_delta != 0:
        raise AssertionError(f'stack off by {result.esp_delta}')
    return (sb.read(record + 0x2C, 'I'), sb.read(record + 0x28, 'I'), sb.read(record + 0x30, 'H'), sb.read(game + WORLD_RNG, 'I'),
            tuple(log))


def _world_step(state: int) -> tuple[int, int]:
    value = (state * 1664525 + 1013904223) & MASK32
    state = ror32(value, 13)
    signed = s32(state)
    return state, (-signed if signed < 0 else signed) & MASK32


def litter_plan(x: dict) -> tuple:
    """ParkLitter.Drop and SpriteOffset: nothing online or on a littered cell; else the type, no collector, a sprite of bank 11 at
    (x·10 + r1 % 9 + 1, y·10 + r2 % 9 + 1) with the generator reseeded to the cell id; vomit and stinkbomb add RegionFX 2 and 5
    (phase B), a stinkbomb its particle."""
    if x['online'] or x['occupied']:
        return x['occupied'], 0, 0x55, x['seed'], ()
    cx, cy = x['cell']
    cell_id = cy * 128 + cx + 1
    seed, r1 = _world_step(cell_id)
    seed, r2 = _world_step(seed)
    kind = x['type']
    calls = []
    if kind == 7:
        calls.append(('effect', 2, cell_id))
    elif kind == 8:
        calls.append(('effect', 5, cell_id))
    calls.append(('sprite', LITTER_SPRITES.get(kind, 0x74F118), 0xB))
    px, pz = float(cx * 10 + r1 % 9 + 1), float(cy * 10 + r2 % 9 + 1)
    calls.append(('place', 0x5150, px, pz))
    if kind == 8:
        calls.append(('particle', 0x32))
    return kind, 0x5150, 0, seed, tuple(calls)


def litter_inputs(rng: random.Random, n: int) -> list:
    def make(**kw) -> dict:
        x = {'cell': (rng.randrange(0, 128), rng.randrange(0, 64)), 'type': rng.choice([1, 2, 3, 4, 5, 6, 7, 8, rng.randrange(1, 9)]),
             'occupied': rng.choice([0, 0, 0, 1, 7]), 'online': rng.random() < 0.1, 'seed': rng.getrandbits(32)}
        x.update(kw)
        return x
    edges = [make(cell=c, type=t, occupied=0, online=False) for c in ((0, 0), (127, 63), (20, 30)) for t in range(1, 9)]
    edges += [make(occupied=1), make(online=True)]
    return edges + [make() for _ in range(n)]


LITTER_TARGET = Target(
    'tpw-exe-litter-drop', 'tpw', 'tp.exe littering a cell 0x4d93b0', 'WANDER-plan §6 / ParkLitter.Drop (ECON-020)',
    LITTER_ENTRY, LITTER_RANGES, litter_stubs, litter_binary, litter_plan, litter_inputs,
    notes=['Executed: 0x4d93b0, the world generator step 0x516330 and its reseed 0x516370, _ftol and the debug print. Stubs: the '
           'sprite creation 0x475a10 (descriptor and bank recorded), its placement 0x475480 (x and z recorded), the RegionFX add '
           '0x4d8440 and the stinkbomb particle 0x521e60. The GameType object is marked constructed (0xfb34b9 bit 0) with word 0 '
           'from the input; synthetic cell records at [0x8023a0] (id +0, type +0x2c, collector +0x30). x87 control word 0x27F.'])


# ============================================================================== tp.exe state 9, the walk to a bin 0x4fff20 (GUEST-005)
BIN_WALK_ENTRY = 0x4FFF20      # thiscall (guest): the state-9 handler
BIN_WALK_RANGES = [(0x4FFF20, 0x4FFFE5), (DEBUG_PRINT, DEBUG_PRINT + 1)]
BIN_WALK_CALLS = {'rand': 0x516330, 'walk': 0x4FA2A0, 'sound': 0x55A070, 'play': 0x55A0B0, 'litter': 0x4D93B0, 'state': 0x501DB0}
BIN_WALK_STATE = type('BinWalkState', (), {})()


def bin_walk_stubs(target: Target) -> dict:
    state = BIN_WALK_STATE
    c = BIN_WALK_CALLS

    def rand(sb):
        thiscall_check(sb, state.game, '0x516330')
        return state.draw, 0

    def walk(sb):
        thiscall_check(sb, state.guest, '0x4fa2a0')
        return state.x['walk'], 0

    def sound(sb):
        target.log.append(('bin_sound', sb.arg(0)))
        return 0x5050, 0

    def play(sb):
        target.log.append(('play', sb.arg(0), sb.arg(1), sb.arg(2)))
        return 0, 0

    def litter(sb):
        target.log.append(('litter', sb.arg(0), (sb.reg('ecx') - state.cells) // 0x44 + 1))
        return 0, 4

    def set_state(sb):
        thiscall_check(sb, state.guest, '0x501db0')
        target.log.append(('state', sb.arg(0)))
        return 0, 4

    return {c['rand']: rand, c['walk']: walk, c['sound']: sound, c['play']: play, c['litter']: litter, c['state']: set_state}


def bin_walk_binary(sb: Sandbox, x: dict, log) -> tuple:
    """(the recorded calls, the litter meter's bits, the +0x1dc word)."""
    state = BIN_WALK_STATE
    state.x, state.draw = x, x['r']
    state.game = bind_game(sb)
    state.cells = sb.object(0x44 * 0x80 * 0x40)
    sb.write(STATE6_CELLS, 'I', state.cells)
    if x['bin']:
        thing = sb.object(0x100, {0x24: ('I', 0x2424)})
        sb.write(POINTS_THINGS + 20 * x['bin'], 'I', thing)
    state.guest = sb.object(0x400, {0x5: ('B', 20), 0x7: ('B', 30), 0x1B4: ('f', 93.0), 0x1DC: ('H', x['bin']), 0x220: ('I', 9)})
    result = sb.call(BIN_WALK_ENTRY, this=state.guest)
    if result.esp_delta != 0:
        raise AssertionError(f'stack off by {result.esp_delta}')
    return tuple(log), sb.read(state.guest + 0x1B4, 'I'), sb.read(state.guest + 0x1DC, 'H')


def bin_walk_plan(x: dict) -> tuple:
    """GuestSimulation.UpdateThrowingAwayLitter: walking on (1) changes nothing; arriving (0) at a bin plays its sound; arriving without
    a bin, or stuck (2), drops litter 1 + rand % 5 on the guest's cell; then the meter is 0 and the state 6. The original leaves +0x1dc
    as it was (OpenTPW clears its bin id; the next major choice overwrites the word in the original)."""
    meter = struct.unpack('<I', struct.pack('<f', 93.0))[0]
    if x['walk'] == 1:
        return (), meter, x['bin']
    if x['walk'] == 0 and x['bin']:
        calls = [('bin_sound', 0x2424), ('play', 0x5050, 0, 1)]
    else:
        calls = [('litter', 1 + x['r'] % 5, 30 * 128 + 20 + 1)]
    return tuple(calls + [('state', 6)]), 0, x['bin']


def bin_walk_inputs(rng: random.Random, n: int) -> list:
    edges = [{'walk': w, 'bin': b, 'r': r} for w in (0, 1, 2) for b in (0, 3) for r in (0, 4, 5)]
    return edges + [{'walk': rng.choice([0, 1, 2]), 'bin': rng.choice([0, 3, 0x41]), 'r': rng.randrange(0, 1 << 31)} for _ in range(n)]


BIN_WALK_TARGET = Target(
    'tpw-exe-bin-walk', 'tpw', 'tp.exe state 9 (walk to a bin) 0x4fff20', 'WANDER-plan §6 / GuestSimulation state 9 (GUEST-005)',
    BIN_WALK_ENTRY, BIN_WALK_RANGES, bin_walk_stubs, bin_walk_binary, bin_walk_plan, bin_walk_inputs,
    notes=['Executed: 0x4fff20 and the debug print. Stubs: the walk step 0x4fa2a0 (0 arrived, 1 walking, 2 stuck, from the input), '
           'the bin sound look-up 0x55a070 and player 0x55a0b0, the draw, the litter drop 0x4d93b0 (type and cell recorded) and '
           'the state setter 0x501db0. The bin is a synthetic thing in the thing table 0x7cfb90 with its sound handle at +0x24.'])

# ============================================================================== tp.exe region effect writer 0x4d8480 (GUEST-006)
REGION_ENTRY = 0x4D8480        # thiscall (cell manager, add flag, effect index, cell id), ret 0xc
REGION_RANGES = [(0x4D8480, 0x4D8608)]
REGION_TABLE = 0x1D9104        # cell manager + 12 × index: five signed words and the radius byte at +10
REGION_GRID = 0x1B1104         # cell manager + 10 × (cell id − 1): five signed words per cell
REGION_WINDOW = 9              # the words read back: a 19 × 19 window around the source (radius up to 9)


def region_binary(sb: Sandbox, x: dict, log) -> tuple:
    """The words of the window around the source after the call, row by row (cells off the map are skipped)."""
    manager = sb.object(REGION_TABLE + 12 * RegionTableRows + 16)
    for index, (radius, coefficients) in enumerate(x['table']):
        for component, value in enumerate(coefficients):
            sb.write(manager + REGION_TABLE + 12 * index + 2 * component, 'h', value)
        sb.write(manager + REGION_TABLE + 12 * index + 10, 'B', radius)
    cx, cy = x['cell']
    for (wx, wy), words in x['grid']:
        for component, value in enumerate(words):
            sb.write(manager + REGION_GRID + 10 * (wy * 128 + wx) + 2 * component, 'h', value)
    result = sb.call(REGION_ENTRY, args=(int(x['add']), x['index'], cy * 128 + cx + 1), this=manager)
    if result.esp_delta != 0:
        raise AssertionError(f'stack off by {result.esp_delta}')
    words = []
    for wy in range(cy - REGION_WINDOW, cy + REGION_WINDOW + 1):
        for wx in range(cx - REGION_WINDOW, cx + REGION_WINDOW + 1):
            if 0 <= wx < 128 and 0 <= wy < 128:
                words.append(tuple(sb.read(manager + REGION_GRID + 10 * (wy * 128 + wx) + 2 * c, 'h') for c in range(5)))
    return tuple(words)


RegionTableRows = 8


def region_plan(x: dict) -> tuple:
    """ParkEnvironment.Apply: each cell of the square radius, clipped to 0..127, gains sign × coefficient / (|dx| + |dy| + 1), truncated
    toward zero, as a wrapping signed word."""
    cx, cy = x['cell']
    grid = {cell: list(words) for cell, words in x['grid']}
    radius, coefficients = x['table'][x['index']]
    sign = 1 if x['add'] else -1
    for wx in range(max(0, cx - radius), min(127, cx + radius) + 1):
        for wy in range(max(0, cy - radius), min(127, cy + radius) + 1):
            words = grid.setdefault((wx, wy), [0] * 5)
            divisor = abs(wx - cx) + abs(wy - cy) + 1
            for c in range(5):
                words[c] = s16(words[c] + trunc_div(sign * coefficients[c], divisor))
    out = []
    for wy in range(cy - REGION_WINDOW, cy + REGION_WINDOW + 1):
        for wx in range(cx - REGION_WINDOW, cx + REGION_WINDOW + 1):
            if 0 <= wx < 128 and 0 <= wy < 128:
                out.append(tuple(grid.get((wx, wy), [0] * 5)))
    return tuple(out)


def s16(value: int) -> int:
    value &= 0xFFFF
    return value - 0x10000 if value & 0x8000 else value


def region_inputs(rng: random.Random, n: int) -> list:
    def coefficient() -> int:
        return rng.choice([0, 1, -1, 2, -3, 3, 10, 20, -32768, 32767, rng.randrange(-40, 41)])

    def make(**kw) -> dict:
        table = [(rng.choice([0, 1, 3, 5, 6, rng.randrange(0, 10)]), tuple(coefficient() for _ in range(5))) for _ in range(RegionTableRows)]
        cell = rng.choice([(rng.randrange(0, 128), rng.randrange(0, 128)), (0, 0), (127, 127), (1, 126), (64, 64)])
        grid = [((min(127, max(0, cell[0] + rng.randrange(-6, 7))), min(127, max(0, cell[1] + rng.randrange(-6, 7)))),
                 tuple(rng.choice([0, 0, 5, -5, 32767, -32768, rng.randrange(-200, 201)]) for _ in range(5))) for _ in range(rng.randrange(0, 6))]
        # one write per cell
        seen, unique = set(), []
        for item in grid:
            if item[0] not in seen:
                seen.add(item[0])
                unique.append(item)
        x = {'table': table, 'index': rng.randrange(0, RegionTableRows), 'cell': cell, 'add': rng.random() < 0.6, 'grid': unique}
        x.update(kw)
        return x
    return [make() for _ in range(n + 20)]


REGION_TARGET = Target(
    'tpw-exe-region-effect', 'tpw', 'tp.exe region effect writer 0x4d8480', 'PPC-guests phase 2 / ParkEnvironment.Apply (GUEST-006)',
    REGION_ENTRY, REGION_RANGES, lambda t: {}, region_binary, region_plan, region_inputs,
    notes=['No stubs: the writer calls nothing. A synthetic cell manager holds the effect table at +0x1d9104 (12 bytes a row) and '
           'the grid at +0x1b1104 (10 bytes a cell); the 19 × 19 window around the source is read back.'])

# ============================================================================== tp.exe handyman litter finder 0x4c8ed0 (ECON-022)
FINDER_ENTRY = 0x4C8ED0        # thiscall (park, out, collector id, cell id, range), ret 0x10
FINDER_RANGES = [(0x4C8ED0, 0x4C9034), (DEBUG_PRINT, DEBUG_PRINT + 1)]
FINDER_SCRATCH = 0x1F020       # the park's scratch buffer of candidate cells


def finder_binary(sb: Sandbox, x: dict, log) -> int:
    """The cell id the routine writes to its out word (0: none)."""
    cells = sb.object(0x44 * 0x80 * 0x80)
    sb.write(STATE6_CELLS, 'I', cells)
    for cx, cy, kind, collector in x['litter']:
        record = cells + 0x44 * (cy * 0x80 + cx)
        sb.write(record + 0x2C, 'I', kind)
        sb.write(record + 0x30, 'H', collector)
    park = sb.object(FINDER_SCRATCH + 2 * 121 + 16)
    out = sb.object(4)
    cx, cy = x['cell']
    result = sb.call(FINDER_ENTRY, args=(out, x['collector'], cy * 128 + cx + 1, x['range']), this=park)
    if result.esp_delta != 0:
        raise AssertionError(f'stack off by {result.esp_delta}')
    return sb.read(out, 'H')


def finder_plan(x: dict) -> int:
    """StaffWorldSimulation.FindLitter: within the clipped square of the range, litter claimed by nobody or the collector, nearest by
    Manhattan distance, the first in the x-outer, y-inner scan on a tie; the cell id or 0."""
    litter = {(cx, cy): (kind, collector) for cx, cy, kind, collector in x['litter']}
    cx, cy = x['cell']
    r = x['range']
    best, best_distance = 0, None
    for wx in range(max(0, cx - r), min(127, cx + r) + 1):
        for wy in range(max(0, cy - r), min(127, cy + r) + 1):
            kind, collector = litter.get((wx, wy), (0, 0))
            if not kind or collector not in (0, x['collector']):
                continue
            distance = abs(wx - cx) + abs(wy - cy)
            if best_distance is None or distance < best_distance:
                best, best_distance = wy * 128 + wx + 1, distance
    return best


def finder_inputs(rng: random.Random, n: int) -> list:
    def make() -> dict:
        cell = rng.choice([(rng.randrange(0, 128), rng.randrange(0, 128)), (0, 0), (127, 127), (2, 125), (64, 64)])
        r = rng.randrange(0, 6)
        litter, seen = [], set()
        for _ in range(rng.randrange(0, 12)):
            spot = (min(127, max(0, cell[0] + rng.randrange(-6, 7))), min(127, max(0, cell[1] + rng.randrange(-6, 7))))
            if spot in seen:
                continue
            seen.add(spot)
            litter.append(spot + (rng.choice([1, 2, 5, 7, 8]), rng.choice([0, 0, 0, 3, 9])))
        return {'cell': cell, 'range': r, 'collector': rng.choice([3, 9, 4]), 'litter': litter}
    return [make() for _ in range(n + 20)]


FINDER_TARGET = Target(
    'tpw-exe-litter-finder', 'tpw', 'tp.exe handyman litter finder 0x4c8ed0', 'WANDER-plan §4 / StaffWorldSimulation.FindLitter (ECON-022)',
    FINDER_ENTRY, FINDER_RANGES, lambda t: {}, finder_binary, finder_plan, finder_inputs,
    notes=['No stubs but the debug print. Synthetic cell records at [0x8023a0] (litter type +0x2c, collector word +0x30) and a park '
           'object whose scratch buffer at +0x1f020 the routine fills; the cell id of the out word is compared.'])

# ============================================================================== tp.exe guard choice 0x5099c0 (ECON-022, GUEST-007)
GUARD_ENTRY = 0x5099C0          # thiscall (receiver, out, prankster cell id), ret 8
GUARD_RANGES = [(0x5099C0, 0x509ADF), (0x4D6330, 0x4D6348), (DEBUG_PRINT, DEBUG_PRINT + 1)]
GUARD_LIST = 0x1DA744           # game word: the first guard's thing index; each guard's next at +0x210


def guard_binary(sb: Sandbox, x: dict, log) -> int:
    """The thing index the routine writes to its out word (0: none free)."""
    game = bind_game(sb)
    guards = x['guards']
    sb.write(game + GUARD_LIST, 'H', 1 if guards else 0)
    for i, (state, gx, gy) in enumerate(guards):
        index = i + 1
        thing = sb.object(0x220, {0x5: ('B', gx), 0x7: ('B', gy), 0x19C: ('I', state), 0x210: ('H', index + 1 if index < len(guards) else 0)})
        sb.write(POINTS_THINGS + 20 * index, 'I', thing)
    hx, hy = x['origin']
    receiver = sb.object(0x10, {0x5: ('B', hx), 0x7: ('B', hy)})
    out = sb.object(4, {0: ('H', 0xBEEF)})
    px, py = x['prankster']
    result = sb.call(GUARD_ENTRY, args=(out, py * 128 + px + 1), this=receiver)
    if result.esp_delta != 0:
        raise AssertionError(f'stack off by {result.esp_delta}')
    return sb.read(out, 'H')


def guard_plan(x: dict) -> int:
    """StaffWorldSimulation.ChooseGuard: in list order the guards in state 0 or 1, the smallest dx² + dy² from the receiver's (the staff HQ's)
    cell, the first on a tie; the prankster's cell plays no part. The guard's thing index (1-based here), or 0."""
    best, chosen = None, 0
    hx, hy = x['origin']
    for i, (state, gx, gy) in enumerate(x['guards']):
        if state not in (0, 1):
            continue
        distance = (gx - hx) ** 2 + (gy - hy) ** 2
        if best is None or distance < best:
            best, chosen = distance, i + 1
    return chosen


def guard_inputs(rng: random.Random, n: int) -> list:
    def make() -> dict:
        origin = rng.choice([(0, 0), (0, 0), (rng.randrange(0, 128), rng.randrange(0, 128)), (127, 127)])
        guards = []
        for _ in range(rng.randrange(0, 7)):
            cell = rng.choice([(rng.randrange(0, 128), rng.randrange(0, 128)), (3, 4), (4, 3), (0, 5), (5, 0), (127, 0)])
            guards.append((rng.choice([0, 1, 1, 0, 2, 3, 4, 5, 7, 16, 18, 19]),) + cell)
        return {'origin': origin, 'guards': guards, 'prankster': (rng.randrange(0, 128), rng.randrange(0, 128))}
    return [make() for _ in range(n + 20)]


GUARD_TARGET = Target(
    'tpw-exe-guard-choice', 'tpw', 'tp.exe guard choice 0x5099c0', 'WANDER-plan §4 / StaffWorldSimulation.ChooseGuard (ECON-022)',
    GUARD_ENTRY, GUARD_RANGES, lambda t: {}, guard_binary, guard_plan, guard_inputs,
    notes=['No stubs but the debug print; the state test 0x4d6330 runs. Synthetic guard things in the thing table 0x7cfb90 (cell bytes +5, +7, '
           'state +0x19c, next word +0x210) behind the game word +0x1da744, and a receiver thing with its cell bytes; the out word is compared. '
           'Cells with equal squared distance test the tie rule; the prankster cell argument varies and must not matter.'])

# ============================================================================== TPI Game.exe: the same four rules
# Addresses are virtual addresses of the TPI no-CD Game.exe at its preferred base (SHA-256 77ea0c41...90d967a).
# The game object keeps the world state at moved offsets (RNG +0x1eab3c, tick +0x1eab40, weather handle +0x1eab58,
# world state +0x1eab78, first thing +0x1eab88) and is reached through two globals (0xade6b0, and 0xaa1484 in the bank).
# Calls go through the image's incremental-link jump thunks; the stubs below sit on the thunk addresses.
TPI_GAME_POINTER = 0xADE6B0
TPI_GAME_POINTER_BANK = 0xAA1484
TPI_GAME_SIZE = 0x200000
TPI_DEBUG_PRINT = 0x403E45          # cdecl (level, format, ...): logs; the release build has no assertion calls
TPI_FTOL = (0x86FAB0, 0x86FAD7)     # float -> int64 by fistp with the rounding bits forced to truncate (the TPW 0x67a830)
TPI_S64_DIV = (0x871A30, 0x871ADA)  # signed 64-bit divide, ret 0x10 (the TPW 0x67c320)
TPI_U64_MUL = (0x871E20, 0x871E52)  # 64-bit multiply, ret 0x10 (the TPW 0x67c7f0)


def tpi_bind_game(sb: Sandbox, fields: dict | None = None) -> int:
    game = sb.object(TPI_GAME_SIZE, fields)
    sb.write(TPI_GAME_POINTER, 'I', game)
    sb.write(TPI_GAME_POINTER_BANK, 'I', game)
    return game


# ------------------------------------------------------------------------------ TPI arrival group size 0x5bcd60
TPI_ARRIVAL_ENTRY = 0x5BCD60       # thiscall (arrival object, unused); the TPW 0x4cf5b0 and the Mac 0xcb4e8
TPI_ARRIVAL_RANGE = (0x5BCD60, 0x5BCF05)
TPI_ARRIVAL_THUNKS = [(0x403A99, 0x403A9E), (0x42A340, 0x42A344)]   # 0x403a99 -> 0x42a340: eax = [ecx + 0x14], run for real
TPI_ARRIVAL_PARK = 0x40144C        # thunk -> 0x64eac0, game -> park (thing at the u16 handle game+0x1eab54); stubbed
TPI_ARRIVAL_COUNTED = 0x40461F     # thunk -> 0x5b0210, thiscall (park, 20) -> counted guests, ret 4; stubbed
TPI_ARRIVAL_POINTS = 0x402BD5      # thunk -> 0x5b05d0, thiscall (park) -> park points; stubbed (target tpi-exe-park-points)
TPI_ARRIVAL_WEATHER = 0x4019B5     # thunk -> 0x64eb80, game -> the thing at the u16 handle game+0x1eab58 (mWeather); stubbed
TPI_ARRIVAL_MODE_OBJECT = 0xA06558  # an object whose dword +0x14 equal to 3 gives no group (not in tp.exe)
TPI_ARRIVAL_SETTINGS = {'min_people': 0xA02230, 'bonus': 0xA0223C, 'per_visitor': 0xA02240}   # settings-table order
TPI_ARRIVAL_PCT = 0xA02244         # Rain, Storm, Snow, Heatwave PercentageModifier (four dwords)
TPI_ARRIVAL_LIMIT_MODE = 0xC65B80  # GameType object word 0 (the TPW 0xfb3b7c); 1 = online, the 500 cut
TPI_ARRIVAL_LIMIT_GUARD = 0xC64F00  # bit 0: the GameType object is constructed (its constructor 0x40ce50 must not run)
TPI_WORLD_MODE = 0x1EAB78          # game: bankrupt world mode 4 (mWorldState; the TPW 0x1da738)
TPI_WEATHER_STATE = 0x70           # weather thing word: 1 to 4 pick the percentage (TPW: the word at +0x30, positive or not)
TPI_WEATHER_SLOT = (3, 0, 1, 2)    # jump table: state 1 -> the word at 0xa02250 (Heatwave), 2 -> 0xa02244 (Rain), 3 -> Storm, 4 -> Snow
TPI_ARRIVAL_STATE = type('TpiArrivalState', (), {})()
TPI_FACTOR_SINGLE = struct.unpack('<f', struct.pack('<f', 0.01))[0]   # the single-precision constant at 0x93ad04


def tpi_arrival_stubs(target: Target) -> dict:
    state = TPI_ARRIVAL_STATE

    def park(sb):
        thiscall_check(sb, state.game, '0x40144c')
        return state.park, 0

    def counted(sb):
        thiscall_check(sb, state.park, '0x40461f')
        if sb.arg(0) != 20:
            raise AssertionError('0x40461f called with an unexpected argument')
        return state.guests & MASK32, 4

    def points(sb):
        thiscall_check(sb, state.park, '0x402bd5')
        return state.points & MASK32, 0

    def weather(sb):
        thiscall_check(sb, state.game, '0x4019b5')
        return state.thing, 0

    def log(sb):
        target.log.append(('log', sb.arg(0), sb.arg(1)))
        return 0, 0

    return {TPI_ARRIVAL_PARK: park, TPI_ARRIVAL_COUNTED: counted, TPI_ARRIVAL_POINTS: points,
            TPI_ARRIVAL_WEATHER: weather, TPI_DEBUG_PRINT: log}


def tpi_arrival_binary(sb: Sandbox, x: dict, log, fpcw: int | None = None) -> int:
    state = TPI_ARRIVAL_STATE
    state.game = tpi_bind_game(sb, fields={TPI_WORLD_MODE: ('i', x['world_mode'])})
    state.park = sb.object(16)
    state.thing = sb.object(0x80, {TPI_WEATHER_STATE: ('i', x['weather'])})
    state.guests, state.points = x['guests'], x['points']
    for key, address in TPI_ARRIVAL_SETTINGS.items():
        sb.write(address, 'i', x[key])
    for index, value in enumerate(x['pct']):
        sb.write(TPI_ARRIVAL_PCT + 4 * index, 'i', value)
    sb.write(TPI_ARRIVAL_MODE_OBJECT + 0x14, 'i', x['mode'])
    sb.write(TPI_ARRIVAL_LIMIT_MODE, 'i', x['limit_mode'])
    sb.write(TPI_ARRIVAL_LIMIT_GUARD, 'B', 1)
    sb.set_fpcw(X87_CONTROL_WORD if fpcw is None else fpcw)
    return s32(sb.call(TPI_ARRIVAL_ENTRY, this=state.park).eax)


def tpi_arrival_percentage(x: dict) -> int:
    """The factor in percent: the weather thing's state word, 1 to 4 (unsigned compare after the decrement), picks one of the
    four settings words through a jump table, state 1 the Heatwave word, 2 Rain, 3 Storm, 4 Snow; any other state,
    including none, gives the constant 100."""
    state = (x['weather'] - 1) & MASK32
    return x['pct'][TPI_WEATHER_SLOT[state]] if state <= 3 else 100


def tpi_arrival_reading(x: dict, precision: int = 53) -> int:
    """What 0x5bcd60 does, with the x87 arithmetic: no group in world mode 4 or when the mode object's +0x14 word is 3; the
    divisor is PointsPerVisitor, 1 unless above 1 (signed); total = points + NewParkBonus (32-bit wrap, `fild` signed); the
    factor percentage (integer) is multiplied by the single-precision 0.01, then the exact integer total, then divided by the
    divisor, each step rounded to a `precision`-bit significand (53 under the control word 0x27F), and the quotient is
    truncated by fistp with only the low word kept (no integer divide); at least MinPeople (signed); then the online cut
    (mode word 1: 500, unsigned compare) or the 1500 cut, both without a floor."""
    if x['world_mode'] == 4 or x['mode'] == 3:
        return 0
    per_visitor = x['per_visitor'] if x['per_visitor'] > 1 else 1
    total = s32(x['points'] + x['bonus'])
    factor = x87_round(Fraction(tpi_arrival_percentage(x)) * Fraction(TPI_FACTOR_SINGLE), precision)
    product = x87_round(Fraction(total) * factor, precision)
    quotient = x87_round(product / per_visitor, precision)
    count = max(s32(fistp_low32(quotient)), x['min_people'])
    guests = x['guests']
    if x['limit_mode'] == 1 and (guests + count) & MASK32 > 500:
        count = s32(500 - guests)
    if x['limit_mode'] != 1 and (guests + count) & MASK32 > 1500:
        count = s32(1500 - guests)
    return count


def tpi_arrival_exact_reading(x: dict) -> int:
    """The same rules with exact arithmetic: trunc(total x percentage / 100 / divisor). Shows what the single-precision 0.01
    does."""
    if x['world_mode'] == 4 or x['mode'] == 3:
        return 0
    per_visitor = x['per_visitor'] if x['per_visitor'] > 1 else 1
    total = s32(x['points'] + x['bonus'])
    value = Fraction(total * tpi_arrival_percentage(x), 100 * per_visitor)
    count = max(s32(fistp_low32(value)), x['min_people'])
    guests = x['guests']
    if x['limit_mode'] == 1 and (guests + count) & MASK32 > 500:
        count = s32(500 - guests)
    if x['limit_mode'] != 1 and (guests + count) & MASK32 > 1500:
        count = s32(1500 - guests)
    return count


def tpi_arrival_tpw_plan(x: dict) -> int:
    """The TPW / Mac rule (F9, `arrival_full`) on the same world state: the factor is 0.8 when the weather word is positive,
    else 1.2, then an integer divide; the TPI-only exit on the mode object does not exist there."""
    return mac().arrival_full({'world_mode': x['world_mode'], 'guests': x['guests'], 'points': x['points'],
                               'bonus': x['bonus'], 'per_visitor': x['per_visitor'], 'min_people': x['min_people'],
                               'obj48': x['weather'], 'limit_mode': x['limit_mode']})


TPI_SHIPPED_PCT = (90, 80, 75, 120)


def tpi_arrival_inputs(rng: random.Random, n: int) -> list:
    shipped = dict(world_mode=0, mode=0, guests=0, points=0, bonus=20, per_visitor=25, min_people=1, weather=0,
                   pct=TPI_SHIPPED_PCT, limit_mode=0)

    def make(**kw):
        x = {**shipped, 'world_mode': rng.choice([0, 0, 1, 2, 3, 4, 5]), 'mode': rng.choice([0, 0, 0, 1, 2, 3, 4]),
             'guests': rng.choice([0, rng.randrange(0, 1600)]),
             'points': rng.choice([0, rng.randrange(0, 2000), rng.randrange(-500, 0), rng.randrange(0, 1 << 20)]),
             'bonus': rng.choice([20, rng.randrange(0, 200)]), 'per_visitor': rng.choice([25, 6, 1, rng.randrange(-2, 60)]),
             'min_people': rng.choice([1, 2, rng.randrange(0, 40)]),
             'weather': rng.choice([0, 0, 1, 2, 3, 4, 5, -1, rng.randrange(-5, 9)]),
             'pct': tuple(rng.choice([base, 100, rng.randrange(0, 300), 0, -20]) for base in TPI_SHIPPED_PCT),
             'limit_mode': rng.choice([0, 0, 0, 1, 2])}
        x.update(kw)
        return x
    edges = [dict(shipped), {**shipped, 'world_mode': 4}, {**shipped, 'mode': 3}, {**shipped, 'mode': 2},
             # points 80 + bonus 20 = 100 over 25: exact arithmetic gives 4, 0.01f and the x87 give 3 (no weather)
             {**shipped, 'points': 80}, {**shipped, 'points': 80, 'weather': 1}, {**shipped, 'points': 80, 'weather': 4},
             {**shipped, 'points': 180, 'weather': 4}, {**shipped, 'points': 0, 'weather': 2},
             {**shipped, 'points': 480, 'weather': 3}, {**shipped, 'points': 1000, 'weather': 0}]
    edges += [{**shipped, 'points': 1000, 'weather': w} for w in (-1, 0, 1, 2, 3, 4, 5, 0x7FFFFFFF)]
    edges += [{**shipped, 'limit_mode': 1, 'guests': 499}, {**shipped, 'limit_mode': 1, 'guests': 600},
              {**shipped, 'guests': 1499}, {**shipped, 'guests': 1500}, {**shipped, 'guests': 1700},
              {**shipped, 'points': 0x7FFFFFFF}, {**shipped, 'points': -0x80000000, 'bonus': -1},
              {**shipped, 'points': 16777217}, {**shipped, 'per_visitor': 1}, {**shipped, 'per_visitor': -7},
              {**shipped, 'min_people': -5, 'bonus': -100}, {**shipped, 'per_visitor': 6, 'points': 100, 'weather': 0},
              {**shipped, 'per_visitor': 6, 'points': 100, 'weather': 1}]
    return edges + [make() for _ in range(n)]


TPI_ARRIVAL_TARGET = Target(
    'tpi-exe-arrival-group', 'tpi', 'Game.exe arrival group size 0x5bcd60',
    'GUESTS.md "Arrivals" / ORACLE.md F9, X3 (TPW 0x4cf5b0, Mac 0xcb4e8)', TPI_ARRIVAL_ENTRY,
    [TPI_ARRIVAL_RANGE, TPI_FTOL] + TPI_ARRIVAL_THUNKS, tpi_arrival_stubs, tpi_arrival_binary, tpi_arrival_tpw_plan,
    tpi_arrival_inputs, alternatives={'routine-reading': tpi_arrival_reading, 'exact-arithmetic': tpi_arrival_exact_reading},
    notes=['Stubs on the jump thunks: 0x40144c game -> park (sentinel, checked), 0x40461f counted guests (argument 20 checked), '
           '0x402bd5 park points (chosen), 0x4019b5 weather thing (synthetic, dword +0x70 chosen), 0x403e45 debug print '
           '(logged). The mode object word (0x403a99 -> 0x42a340) and _ftol 0x86fab0 are executed. The GameType object is '
           'marked constructed. The percentage replaces the TPW 0.8 / 1.2 factor and the integer divide became a '
           'floating divide.'])


# ------------------------------------------------------------------------------ TPI loan offers 0x5bfb70
TPI_LOAN_CTOR = 0x5BFB70           # thiscall (bank object): the bank constructor; the TPW 0x4cf7c0 and the Mac 0xcb7a8
TPI_LOAN_CTOR_STOP = 0x5BFC73      # end of the 8-offer loop
TPI_LOAN_BASE_CTOR = 0x403D19      # thunk -> 0x637100, thiscall (bank, u16 1, 0x10) base constructor, ret 8; stubbed
TPI_LOAN_CASH = 0xA020BC           # settings: BankAccountInfo.InitialCash; the offers follow from 0xA020C4, 16 bytes each
TPI_LOAN_OFFERS_AT = 0xA020C4
# pow is the runtime's own x87 routine here too (0x871c00 with helpers 0x87c410..), the TPW 0x67c5d0.
TPI_LOAN_POW_RANGES = [(0x871C00, 0x871E1D), (0x87C410, 0x87C498), (0x87C4AE, 0x87C5A0)]
TPI_LOAN_RECORDS = 0x1C            # eight 32-byte records from bank + 0x1c (the TPW +0x14)
TPI_LOAN_BANK_WORDS = (0x14, 0x18, 0x11C, 0x120, 0x124, 0x128, 0x12C)   # the TPW words, 8 higher


def tpi_loan_stubs(target: Target) -> dict:
    def base_constructor(sb):
        target.log.append(('base-ctor', sb.reg('ecx'), sb.arg(0) & 0xFFFF, sb.arg(1)))
        return 0, 8

    return {TPI_LOAN_BASE_CTOR: base_constructor}


def tpi_loan_offers_binary(sb: Sandbox, x: dict, log, fpcw: int | None = None) -> dict:
    bank = sb.object(0x200)
    sb.write(TPI_LOAN_CASH, 'I', x['cash'])
    for index, offer in enumerate(x['offers']):
        for word, value in enumerate(offer):
            sb.write(TPI_LOAN_OFFERS_AT + 16 * index + 4 * word, 'I', value)
    sb.set_fpcw(X87_CONTROL_WORD if fpcw is None else fpcw)
    sb.call(TPI_LOAN_CTOR, this=bank, stop=TPI_LOAN_CTOR_STOP)
    if [entry for entry in log if entry[0] == 'base-ctor'] != [('base-ctor', bank, 1, 0x10)]:
        raise AssertionError('the base constructor was not called once with the bank object, the word 1 and 0x10')
    records = [tuple((sb.read(bank + TPI_LOAN_RECORDS + 32 * i + 4 * w, 'I') for w in range(8))) for i in range(8)]
    return {'monthly': [record[4] for record in records],
            'records': [(r[0], r[1], r[2], r[3], r[5], r[6], r[7]) for r in records],
            'bank': tuple(sb.read(bank + offset, 'I') for offset in TPI_LOAN_BANK_WORDS)}


TPI_LOAN_OFFERS_TARGET = Target(
    'tpi-exe-loan-offers', 'tpi', 'Game.exe loan offer setup 0x5bfb70', 'ECONOMY.md / ORACLE.md F12, X4 (TPW 0x4cf7c0, Mac 0xcb7a8)',
    TPI_LOAN_CTOR, [(TPI_LOAN_CTOR, TPI_LOAN_CTOR_STOP), TPI_FTOL] + TPI_LOAN_POW_RANGES, tpi_loan_stubs,
    tpi_loan_offers_binary, loan_offers_plan, loan_offers_inputs,
    alternatives={'mac-reading': loan_offers_mac_reading, 'routine-reading': loan_offers_reading}, equal=loan_offers_equal,
    notes=['Fragment 0x5bfb70..0x5bfc73 (the 8-offer loop) run from the constructor entry. Stub: 0x403d19 base constructor '
           '(header only, arguments checked). pow is the runtime routine 0x871c00 and its helpers, executed.'])


# ------------------------------------------------------------------------------ TPI loan monthly step 0x5c0b10
TPI_LOAN_STEP_ENTRY = 0x5C0B25     # inside the monthly handler 0x5c0b10, after its exception-frame set-up
TPI_LOAN_STEP_STOP = 0x5C0C49      # end of the loan loop; the tail (a bank in the red for a month) sends a message
TPI_LOAN_STEP_RANGE = (0x5C0B25, 0x5C0C49)
TPI_LOAN_PARK = 0x40144C           # the same thunk as the arrival's: game (global 0xaa1484) -> park
TPI_LOAN_ACC_IN, TPI_LOAN_ACC_OUT = 0x1FEF8, 0x1F808
TPI_LOAN_TURN = 0x1EAB40
TPI_LOAN_MESSAGES = {0x9CCDD8: 'log465', 0x9CCD94: 'log587'}   # the instalment line, the paid-off line


def tpi_loan_step_stubs(target: Target) -> dict:
    state = LOAN_STATE

    def park(sb):
        thiscall_check(sb, state.game, '0x40144c')
        return state.park, 0

    def debug(sb):
        kind = TPI_LOAN_MESSAGES.get(sb.arg(1))
        if kind is None:
            raise AssertionError(f'unexpected debug message {sb.arg(1):#x}')
        if kind == 'log465':
            target.log.append(('log465', sb.arg(2), sb.arg(3), sb.arg(4)))   # months repaid + 1, loan index, instalment
        else:
            target.log.append(('log587', sb.arg(2)))                         # loan index
        return 0, 0

    return {TPI_LOAN_PARK: park, TPI_DEBUG_PRINT: debug}


def tpi_loan_step_binary(sb: Sandbox, x: dict, log) -> dict:
    """The TPW bank layout with every bank offset 8 higher (balance +0x14, batch +0x18, withdrawals +0x11c, last balance
    +0x124, turn entered red +0x128, year profit +0x12c, eight 32-byte records from +0x1c); the loan loop of the monthly
    handler is called once per month in one sandbox."""
    state = LOAN_STATE
    state.game = tpi_bind_game(sb)
    state.park = sb.object(0x21000)
    sb.set_fpcw(X87_CONTROL_WORD)
    bank = sb.object(512)
    m = mac()
    shift = 8
    for offset, key in ((m.BANK_BALANCE, 'bal'), (m.BANK_BATCH, 'batch'), (m.BANK_WITHDRAW, 'wd'), (m.BANK_LAST, 'last'),
                        (m.BANK_RED, 'red'), (m.BANK_PROFIT, 'profit')):
        sb.write(bank + offset + shift, 'I', x[key] & MASK32)
    for index, loan in enumerate(x['loans']):
        base = bank + shift + 32 * index
        for offset, key in ((24, 'amount'), (28, 'apr'), (32, 'months'), (36, 'monthly'), (40, 'bought'), (44, 'repaid')):
            sb.write(base + offset, 'I', loan[key] & MASK32)
    out = {key: [] for key in m.LOAN_OUT_KEYS}
    for turn in x['turns']:
        sb.write(state.game + TPI_LOAN_TURN, 'I', turn)
        mark = len(log)
        sb.call(TPI_LOAN_STEP_ENTRY, this=bank, stop=TPI_LOAN_STEP_STOP)
        out['balance'].append(sb.read(bank + m.BANK_BALANCE + shift, 'i'))
        out['profit'].append(sb.read(bank + m.BANK_PROFIT + shift, 'i'))
        out['batch'].append(sb.read(bank + m.BANK_BATCH + shift, 'I'))
        out['last'].append(sb.read(bank + m.BANK_LAST + shift, 'i'))
        out['red'].append(sb.read(bank + m.BANK_RED + shift, 'I'))
        out['acc_in'].append(sb.read(state.park + TPI_LOAN_ACC_IN, 'I'))
        out['acc_out'].append(sb.read(state.park + TPI_LOAN_ACC_OUT, 'I'))
        loans = [(sb.read(bank + shift + 32 * i + 40, 'I'), sb.read(bank + shift + 32 * i + 44, 'I')) for i in range(8)]
        out['loans'].append(loans)
        out['active'].append(sum(1 for bought, _ in loans if bought))
        out['events'].append([tuple(entry) for entry in log[mark:]])
    return out


def tpi_loan_step_reading(x: dict) -> dict:
    """The Mac reading without its two assertion kinds (the batch below one million, a non-zero term): the release build of
    the TPI executable has no assertion calls, so its event list holds only the instalment and paid-off lines."""
    out = mac().loan_step_reading(x)
    out['events'] = [[event for event in events if event[0] != 'assert'] for events in out['events']]
    return out


TPI_LOAN_TERM_TARGET = Target(
    'tpi-exe-loan-term', 'tpi', 'Game.exe loan monthly step 0x5c0b10 over a loan term',
    'ECONOMY.md / ORACLE.md F12, F13, X5 (TPW 0x4d0370, Mac 0xcc21c)', TPI_LOAN_STEP_ENTRY, [TPI_LOAN_STEP_RANGE],
    tpi_loan_step_stubs, tpi_loan_step_binary, loan_term_plan, loan_term_inputs,
    alternatives={'routine-reading': tpi_loan_step_reading}, equal=loan_step_equal,
    notes=['Loop of the monthly handler 0x5c0b10 run from 0x5c0b25 to 0x5c0c49, once a month over a whole term. Stubs: '
           '0x40144c game -> park (sentinel, checked) and 0x403e45 debug print (logged by message). The handler has no '
           'assertion calls. The tail (a bank in the red for a month) is not run.'])
TPI_LOAN_MONTH_TARGET = Target(
    'tpi-exe-loan-month', 'tpi', 'Game.exe loan monthly step 0x5c0b10, one to three months on any bank state',
    'ECONOMY.md / ORACLE.md F13, X5 (TPW 0x4d0370, Mac 0xcc21c)', TPI_LOAN_STEP_ENTRY, [TPI_LOAN_STEP_RANGE],
    tpi_loan_step_stubs, tpi_loan_step_binary, None, loan_month_inputs, alternatives={'routine-reading': tpi_loan_step_reading},
    equal=loan_step_equal, notes=['The same fragment on random bank states.'])


# ------------------------------------------------------------------------------ TPI park points 0x5b05d0
TPI_POINTS_ENTRY = 0x5B05D0        # thiscall (park, unused); the TPW 0x4c8240 and the Mac 0xc24c0
TPI_POINTS_RANGES = [(0x5B05D0, 0x5B07A7),      # the routine
                     (0x5CFD70, 0x5CFDB7),      # park record search (the TPW 0x4d3d80)
                     (0x5CFCF0, 0x5CFD44),      # park record search, writes the cache (the TPW 0x4d3d10)
                     (0x409C41, 0x409C46), (0x403CD8, 0x403CDD), (0x4017EE, 0x4017F3),   # their thunks and the turn conversion's
                     (0x608D50, 0x608D94),      # turns -> 100 ns units (the TPW 0x4f88b0)
                     TPI_S64_DIV, TPI_U64_MUL]
TPI_POINTS_TYPE_RECORD = 0x40C1D0  # thunk -> 0x43c700, thiscall (registry 0xa09ac8, u16 id) -> type record, ret 4; stubbed
TPI_POINTS_REGISTRY = 0xA09AC8
TPI_POINTS_COUNTS_TEST = 0x40998A  # thunk -> 0x5dfc90, thiscall (thing) -> nonzero when it counts; stubbed (chosen flag)
TPI_POINTS_OWN_AGE = 0x403BB6      # thunk -> 0x5df8d0, thiscall (thing) -> days since its purchase timestamp; stubbed
TPI_POINTS_THINGS = 0xAA18B0       # ThingArray: 20-byte entries, word 0 = the thing
TPI_POINTS_FIRST = 0x1EAB88        # game: index of the first thing in the list (u16; mFirstObject)
TPI_POINTS_CLOCK = 0x428           # game + 0x428: the clock; its +0x1c word is the rate
TPI_POINTS_TYPE_TABLE = 0x1E9618   # game: per-type park records, 0x24 bytes: +0 id, +0x18 copies, +0x1c first purchase turn
TPI_POINTS_RECORD_SIZE = 0x24
TPI_POINTS_STATE = type('TpiPointsState', (), {})()
# type record fields (TPI): +0x80 value, +0x84 decay, +0x14c the track-type word, +0x370 bonus[3], +0x600 kind
TPI_POINTS_FIELDS = {'value': 0x80, 'decay': 0x84, 'f9c': 0x14C, 'bonus': 0x370, 'kind': 0x600}


def tpi_points_stubs(target: Target) -> dict:
    state = TPI_POINTS_STATE

    def lookup(sb):
        thiscall_check(sb, TPI_POINTS_REGISTRY, '0x40c1d0')
        return state.records[sb.arg(0) & 0xFFFF], 4

    def thing_of(sb) -> dict:
        address = sb.reg('ecx')
        if address not in state.things:
            raise AssertionError('called without a thing')
        return state.things[address]

    def counts(sb):
        return (1 if thing_of(sb)['open'] else 0), 0

    def age(sb):
        thing = thing_of(sb)
        return mac().points_own_days(state.x['now'], thing['built'], state.x['scale']) & MASK32, 0

    return {TPI_POINTS_TYPE_RECORD: lookup, TPI_POINTS_COUNTS_TEST: counts, TPI_POINTS_OWN_AGE: age}


def tpi_points_binary(sb: Sandbox, x: dict, log) -> int:
    state = TPI_POINTS_STATE
    state.x = x
    game = tpi_bind_game(sb)
    sb.write(game + TPI_LOAN_TURN, 'I', x['now'] & MASK32)
    sb.write(game + TPI_POINTS_CLOCK + 0x1C, 'I', x['scale'] & MASK32)
    state.records = {}
    table = game + TPI_POINTS_TYPE_TABLE
    m = mac()
    size = TPI_POINTS_RECORD_SIZE
    for n, kind in enumerate(x['types']):
        fields = {TPI_POINTS_FIELDS['value']: ('i', kind['value']), TPI_POINTS_FIELDS['decay']: ('i', kind['decay']),
                  TPI_POINTS_FIELDS['f9c']: ('i', kind['f9c']), TPI_POINTS_FIELDS['kind']: ('i', kind['kind'])}
        fields.update({TPI_POINTS_FIELDS['bonus'] + 4 * k: ('i', bonus) for k, bonus in enumerate(kind['bonus'])})
        state.records[kind['id']] = sb.object(2048, fields)
        sb.write(table + size * n, 'H', kind['id'])
        sb.write(table + size * n + 0x18, 'i', m.points_count(x, kind['id']))
        sb.write(table + size * n + 0x1C, 'I', kind['first'] & MASK32)
    sb.write(table + 0x1518, 'i', len(x['types']))   # +0x151c (cached id 0, never a type id) and +0x1520 stay 0
    state.things = {}
    for index, thing in enumerate(x['things'], start=1):
        following = index + 1 if index < len(x['things']) else 0
        address = sb.object(64, {0x14: ('H', following), 0x16: ('H', thing['type'])})
        sb.write(TPI_POINTS_THINGS + 20 * index, 'I', address)
        state.things[address] = thing
    sb.write(game + TPI_POINTS_FIRST, 'H', 1 if x['things'] else 0)
    sb.set_fpcw(X87_CONTROL_WORD)
    return s32(sb.call(TPI_POINTS_ENTRY, this=sb.object(16)).eax)


TPI_POINTS_TARGET = Target(
    'tpi-exe-park-points', 'tpi', 'Game.exe park points 0x5b05d0', 'GUESTS.md "Arrivals" / ORACLE.md F10, X6 (TPW 0x4c8240, Mac 0xc24c0)',
    TPI_POINTS_ENTRY, TPI_POINTS_RANGES, tpi_points_stubs, tpi_points_binary, points_plan, points_inputs,
    notes=['Executed: 0x5b05d0, the park record searches 0x5cfd70 / 0x5cfcf0 (through their thunks), 0x608d50 (turns -> 100 ns '
           'units) and the 64-bit helpers 0x871a30 / 0x871e20. Stubs on the thunks: 0x40c1d0 type record, 0x40998a counts-test '
           '(chosen flag), 0x403bb6 own age (signed timestamp difference in days, as on the Mac). There is no assertion '
           'call. Fields as the TPW ones at other offsets: value +0x80, decay +0x84, track-type word +0x14c, brackets +0x370, '
           'kind +0x600; thing next +0x14 and type +0x16; park record 0x24 bytes.'])


# ============================================================================== (f) coaster rating step and final: tp.exe and Game.exe
# The Mac routines 0x415b4 (step, per 56-byte sample) and 0x41900 (final), F7 and F8. Both Windows executables carry them in
# the same call structure (reset routine, one step per sample from index 1, the final with max Y and max speed). The state
# that the Mac keeps behind TOC slots is a block of static words in the executable's data (previous force and bank, S, E and
# the peaks), written only by the reset, the step and the final.
COAST_PC = {
    'tpw': dict(step=0x43DC50, step_end=0x43DF90, final=0x43DF90, final_end=0x43E0A8, reset=0x43DBF0,
                S=0x7923B4, peak_s=0x7923B0, E=0x7923D8, peak_e=0x7923BC, e_prev=0x7923C0, normal=0x7923AC, terror=0x7923C4),
    'tpi': dict(step=0x480630, step_end=0x480900, final=0x4809C0, final_end=0x480ACA, reset=0x4803A0,
                S=0xA191FC, peak_s=0xA191F8, E=0xA19220, peak_e=0xA19204, e_prev=0xA19208, normal=0xA191F4, terror=0xA1920C),
}
COAST_DEF_FIELDS = {'gforce_sick': 812, 'upside_sick': 816, 'speed': 820, 'gforce_excite': 824, 'bank_excite': 832,
                    'xg_uncomf': 836, 'inverted_uncomf': 840, 'normal_start': 844, 'terror_start': 848,
                    'normal_sustain': 852, 'terror_sustain': 856}   # the Mac's DEF_FIELDS: the same definition offsets


def f32(value: float) -> float:
    return struct.unpack('<f', struct.pack('<f', value))[0]


def f32_bits(value: float) -> int:
    return struct.unpack('<I', struct.pack('<f', value))[0]


def coast_step_binary_for(exe: str):
    layout = COAST_PC[exe]

    def binary(sb: Sandbox, x: dict, log, fpcw: int | None = None) -> dict:
        for name in ('S', 'E', 'peak_s', 'peak_e', 'e_prev'):
            sb.write(layout[name], 'f', x[name])
        sb.write(layout['normal'], 'i', x['normal'])
        sb.write(layout['terror'], 'i', x['terror'])
        # The previous force and bank words (the reset zeroes them, nothing else writes them) stay 0, as on the Mac.
        definition = sb.object(0x380, {offset: ('f', x['def'][name]) for name, offset in COAST_DEF_FIELDS.items()})
        samples = sb.alloc(56 * (x['i'] + 1))
        sb.write(samples + 56 * x['i'], 'I', x['flags'])
        controller = sb.object(0x200, {4: ('I', definition), 156: ('I', samples)})
        force = sb.object(12, {0: ('f', x['F'][0]), 4: ('f', x['F'][1]), 8: ('f', x['F'][2])})
        sb.set_fpcw(X87_CONTROL_WORD if fpcw is None else fpcw)
        # cdecl (controller, sample index, unused, speed, bank, force*, unused); the speed and the bank are singles on the
        # stack. The Mac's stale f0 has no counterpart: the x87 routine keeps no live register between calls.
        sb.call(layout['step'], (controller, x['i'], 0, f32_bits(x['v']), f32_bits(x['bank']), force, 0))
        out = {name: sb.read(layout[name], 'f') for name in ('S', 'E', 'peak_s', 'peak_e', 'e_prev')}
        out.update(flags=sb.read(samples + 56 * x['i'], 'I'), normal=sb.read(layout['normal'], 'i'),
                   terror=sb.read(layout['terror'], 'i'))
        return out
    return binary


def coast_final_binary_for(exe: str):
    layout = COAST_PC[exe]

    def binary(sb: Sandbox, x: dict, log, fpcw: int | None = None) -> dict:
        for name in ('S', 'E', 'peak_s', 'peak_e'):
            sb.write(layout[name], 'f', x[name])
        head = 0
        for flags in reversed(x['sections']):
            head = sb.object(104, {8: ('I', flags), 100: ('I', head)})
        controller = sb.object(0x200, {84: ('I', head), 168: ('f', x['length_a']), 172: ('f', x['length_b'])})
        sb.set_fpcw(X87_CONTROL_WORD if fpcw is None else fpcw)
        sb.call(layout['final'], (controller, f32_bits(x['max_y']), f32_bits(x['max_speed']), 0))   # cdecl; the 4th word is unused
        out = {name: sb.read(controller + offset, 'f') for name, offset in
               (('sickness', 256), ('excitement', 260), ('length', 268), ('max_y', 272), ('max_speed', 276))}
        out['sections'] = sb.read(controller + 264, 'i')
        return out
    return binary


COAST_PI_2 = f32(math.pi / 2)
COAST_TENTH = f32(0.1)
COAST_PEAK_WEIGHT = f32(1.7)


def coast_step_reading(x: dict, exe: str = 'tpw') -> dict:
    """What the Windows step does, in double arithmetic (the control word 0x27F: a 53-bit significand), with a single
    rounding wherever the routine stores a single. The structure is the Mac's (S and E decay by 1/2, scream flags after
    sample 2) with these differences:

    * the term added to the uncomfortableness when the inverted branch is not taken (u = 0, or Fy at least 0.1) is u itself, the
      single (1 + U) the routine stores, not the Mac's stale f0 (Fz); it is 0 when u is 0, so it only matters for a
      track pitched upright (Fy at least 0.1) while |bank| is past pi/2;
    * the sums are built in a different order and rounded to single at different points: tp.exe rounds the g-force sickness
      term to single and the excitement sum never, Game.exe rounds the whole sickness delta and the excitement sum to
      single (the two readings differ in those roundings only)."""
    d = x['def']
    fx, fy, fz = x['F']
    bank = x['bank']
    norm1 = (abs(fz) + abs(fx)) + abs(fy)
    g = norm1 * d['gforce_sick']
    u0 = abs(bank) - COAST_PI_2
    uf = f32(u0)
    if u0 <= 0 or uf <= 0:
        ds = g if exe == 'tpi' else f32(g)
    else:
        ds = (g if exe == 'tpi' else f32(g)) + (1.0 + uf) * d['upside_sick']
    ds_stored = f32(ds) if exe == 'tpi' else ds
    de = ((abs(fy) + abs(fz)) + abs(fx)) * d['gforce_excite']
    de = de + abs(bank) * d['bank_excite']
    de = de + x['v'] * d['speed']
    if exe == 'tpi':
        de = f32(de)
    u = f32(1.0 + uf) if (u0 > 0 and uf > 0) else 0.0
    if u == 0 or not fy < COAST_TENTH:
        inverted = u
    else:
        inverted = u * d['inverted_uncomf']
        if fy < 0:
            inverted = inverted * 3.0
    diff = de - (abs(fx) * d['xg_uncomf'] + inverted)
    if diff <= 0:
        diff = 0.0
    s = f32(x['S'] * 0.5 + ds_stored)
    e = f32(x['E'] * 0.5 + diff)
    out = {'S': s, 'E': e, 'peak_s': max(x['peak_s'], s), 'peak_e': max(x['peak_e'], e)}
    flags, normal, terror = x['flags'] & ~7 & MASK32, x['normal'], x['terror']
    if x['i'] > 2:
        rise = e - x['e_prev']
        if rise > d['normal_start']:
            normal, flags = 1, flags | 4
        elif normal != 0 and e > d['normal_sustain']:
            flags |= 4
        else:
            normal = 0
        if rise > d['terror_start']:
            terror, flags = 1, flags | 2
        elif terror != 0 and e > d['terror_sustain']:
            flags |= 2
        else:
            terror = 0
    out.update(e_prev=e, flags=flags, normal=normal, terror=terror)
    return out


def coast_step_plan(x: dict) -> dict:
    """The Mac routine (F7): the plan with the stale term = Fz, which CoasterRating.Step follows."""
    return mac().coast_step_plan_fz(x)


def coast_step_plan_zero(x: dict) -> dict:
    """COAST-plan 6.3 as written (the stale term taken as 0)."""
    return mac().coast_step_plan(x)


def coast_step_plan_else_u(x: dict) -> dict:
    """The Mac plan in single arithmetic with the Windows term for the branch that is not inverted: u, the single (1 + U)
    for U above 0 and 0 otherwise. What is left against the executables is rounding only."""
    big_u = max(f32(abs(x['bank']) - COAST_PI_2), 0.0)
    return mac().coast_step_plan(x, stale=f32(big_u + (1.0 if big_u != 0 else 0.0)))


def coast_final_reading(x: dict) -> dict:
    """What the Windows final does: sickness = peakS + S and excitement = 1.7 peakE + E/2 (the sum built in double, one
    single rounding), each clamped to 0..100 after the rounding; +264 counts the sections with both 0x40 and 0x80."""
    def clamp(value: float) -> float:
        return 100.0 if value > 100.0 else 0.0 if value < 0.0 else value
    return {'sickness': clamp(f32(x['peak_s'] + x['S'])),
            'excitement': clamp(f32(COAST_PEAK_WEIGHT * x['peak_e'] + 0.5 * x['E'])),
            'length': f32(x['length_a'] * x['length_b']), 'max_y': x['max_y'], 'max_speed': x['max_speed'],
            'sections': sum(1 for flags in x['sections'] if flags & 0xC0 == 0xC0)}


def coast_final_plan(x: dict) -> dict:
    """The Mac routine (F8): the plan with the section count of both flags, which CoasterRating.FinalFor follows."""
    return mac().coast_final_both_flags(x)


def coast_final_plan_documented(x: dict) -> dict:
    """COAST-plan 6.4 as written (a section is counted when neither flag is set)."""
    return mac().coast_final_plan(x)


def coast_step_inputs(rng: random.Random, n: int) -> list:
    return mac().coast_step_inputs(rng, n)


def coast_final_inputs(rng: random.Random, n: int) -> list:
    return mac().coast_final_inputs(rng, n)


def coast_equal(a, b) -> bool:
    return mac().coast_equal(a, b)


def coast_targets() -> list:
    result = []
    for exe, label in (('tpw', 'tp.exe'), ('tpi', 'Game.exe')):
        layout = COAST_PC[exe]
        result.append(Target(
            f'{exe}-exe-coast-rating-step', exe, f'{label} coaster rating step {layout["step"]:#x}',
            'COAST-plan 6.3 / ORACLE.md F7 (Mac 0x415b4)', layout['step'], [(layout['step'], layout['step_end'])],
            lambda t: {}, coast_step_binary_for(exe), coast_step_plan, coast_step_inputs,
            alternatives={'coast-plan-stale0': coast_step_plan_zero, 'mac-plan-else-u': coast_step_plan_else_u,
                          'routine-reading': lambda x, exe=exe: coast_step_reading(x, exe)},
            equal=coast_equal,
            notes=['No stubs: the routine calls nothing. The static state block is written before the call and the previous '
                   'force and bank words stay 0 (the reset zeroes them and nothing else writes them), as in the Mac target. '
                   'The x87 control word is 0x27F.']))
        result.append(Target(
            f'{exe}-exe-coast-rating-final', exe, f'{label} coaster rating final {layout["final"]:#x}',
            'COAST-plan 6.4 / ORACLE.md F8 (Mac 0x41900)', layout['final'], [(layout['final'], layout['final_end'])],
            lambda t: {}, coast_final_binary_for(exe), coast_final_plan, coast_final_inputs,
            alternatives={'coast-plan-documented': coast_final_plan_documented, 'routine-reading': coast_final_reading},
            equal=coast_equal,
            notes=['No stubs: the routine calls nothing. Sections are a synthetic list (flags at +8, next at +100).']))
    return result


COAST_TARGETS = coast_targets()
COAST_STEP_TPW, COAST_FINAL_TPW, COAST_STEP_TPI, COAST_FINAL_TPI = COAST_TARGETS


# ============================================================================== tp.exe guest walk (WALK-002 .. WALK-009, X25)
import walk_reading  # noqa: E402  (the C# port of the walk, restated)

WALK_MAP = 0x7CF6EC             # global: the map object; [map] + 0x294 + id * 0x44 is cell id = y * 128 + x + 1 (type +8, links +0xc, queue link +0xd, flags +0xe)
WALK_OBJECT_TABLE = 0x8023A0    # global read by the object test 0x5363f0 through 0x4d0af0 (cell word +0 = 0: object slot 0, an empty record)
WALK_GAME = 0x7CF83C            # global: the game object; the block stamps at +0x2d8 + 0x1b0000, (y >> 4) * 33 + (x >> 4)
WALK_COUNTER = 0x7CDB98         # the stamp counter 0x4d8c50 raises
WALK_BEHAVIOURS = (0x7CF078, 0x7CF07C)   # the steering list (weight, behaviour) pairs
WALK_EDGE = 0x4D8750            # cdecl (x, y, direction, mask) -> blocked
WALK_COMMIT = 0x510510          # cdecl (from id, to id, mask) -> accepted
WALK_SEARCH = 0x511420          # cdecl (buffer, mask): count +0, start +2, goal +4, waypoints +6; 0x70000000 on failure
WALK_AVOID = 0x50E4A0           # (out, walk), ret 8: avoid_walls
WALK_SETUP = 0x50F8E0           # thiscall walk (target, mode), ret 8
WALK_STEP = 0x50F3B0            # thiscall walk: the steering step
WALK_PROGRESS = 0x50FD40        # thiscall walk -> progress
WALK_CELLS = (0x4D8750, 0x4D8B2C), (0x536300, 0x536460), (0x522770, 0x522790), (0x522850, 0x522860), (0x4D0AF0, 0x4D0B06), \
    (0x53AD20, 0x53ADB0), (0x4D8300, 0x4D8330), (0x510510, 0x5108A0)
WALK_SEARCH_RANGES = [*WALK_CELLS, (0x5108A0, 0x511F00), (0x67BB90, 0x67BF00), (DEBUG_PRINT, DEBUG_PRINT + 1)]
WALK_AVOID_RANGES = WALK_SEARCH_RANGES + [(0x50E4A0, 0x50ECD4), (0x50F870, 0x50F8E0), (0x5104B0, 0x510510), (0x50CD80, 0x50CDA8),
                                          (0x50E080, 0x50E0D4), (0x4D49A0, 0x4D49C2), (0x67A830, 0x67A8A0), (0x5104E0, 0x510502),
                                          (0x50CC20, 0x50CD74), (0x50DF70, 0x50DFB4), (0x50ECE0, 0x50ED08)]
WALK_STEP_RANGES = WALK_AVOID_RANGES + [(0x50F3B0, 0x50F5F8), (0x50F600, 0x50F6F9), (0x50ED10, 0x50F1BD), (0x50F1C0, 0x50F1E2),
                                        (0x50F8E0, 0x50FD3C), (0x50FD40, 0x50FDE4), (0x4D8C50, 0x4D8C5C), (0x4D8CD0, 0x4D8CEB),
                                        (0x50E0E0, 0x50E189), (0x50F1A0, 0x50F1BD), (0x50F700, 0x50F870), (0x4D99C0, 0x4D99D3)]
WALK_NEIGHBOURS = 0x5102F0      # separation's neighbour query: stubbed (no neighbours), the sum it starts from stays 0


def walk_debug_stub(sb: Sandbox):
    return 0, 0


def walk_stubs(target: Target) -> dict:
    return {DEBUG_PRINT: walk_debug_stub, WALK_NEIGHBOURS: walk_debug_stub}


def walk_map_binary(sb: Sandbox, x: dict) -> int:
    """Writes the input's cells into a synthetic 128 x 128 map; returns the cell array's base."""
    holder = sb.object(16)
    base = sb.object(0x294 + (128 * 128 + 2) * 0x44)
    sb.write(holder, 'I', base)
    sb.write(WALK_MAP, 'I', holder)
    sb.write(WALK_OBJECT_TABLE, 'I', sb.object(0x10FFD8 + 64))
    for key, offset, fmt in (('types', 8, 'I'), ('links', 0xC, 'B'), ('qlinks', 0xD, 'B'), ('flags', 0xE, 'H')):
        for cx, cy, value in x[key]:
            sb.write(base + 0x294 + (cy * 128 + cx + 1) * 0x44 + offset, fmt, value)
    return base


def walk_map_reading(x: dict) -> 'walk_reading.Map':
    return walk_reading.Map(*({(cx, cy): v for cx, cy, v in x[key]} for key in ('types', 'links', 'qlinks', 'flags')))


def walk_random_cells(rng: random.Random) -> dict:
    """A park-like window: path corridors (a maze on a 2-cell lattice, or random fill with full rows and columns) plus some queue,
    walkway, type-2/4/5/7/9/10 cells with random links, queue links and the 0x400 flag."""
    w, h = rng.choice([8, 15, 24, 41]), rng.choice([8, 15, 25])
    ox, oy = rng.choice([0, 30, 128 - w]), rng.choice([0, 30, 128 - h])
    types = {}
    if rng.random() < 0.5:
        lattice = [(x, y) for x in range(ox, ox + w, 2) for y in range(oy, oy + h, 2)]
        members, seen, stack = set(lattice), {lattice[0]}, [lattice[0]]
        types[lattice[0]] = 1
        while stack:
            c = stack[-1]
            nbrs = [(c[0] + dx, c[1] + dy) for dx, dy in ((2, 0), (-2, 0), (0, 2), (0, -2)) if (c[0] + dx, c[1] + dy) in members and (c[0] + dx, c[1] + dy) not in seen]
            if not nbrs:
                stack.pop()
                continue
            n = rng.choice(nbrs)
            seen.add(n)
            stack.append(n)
            types[n] = types[((c[0] + n[0]) // 2, (c[1] + n[1]) // 2)] = 1
    else:
        density = rng.choice([0.4, 0.6, 0.8])
        for y in range(oy, oy + h):
            for x in range(ox, ox + w):
                if rng.random() < density:
                    types[(x, y)] = 1
        for _ in range(rng.randint(1, 3)):
            ry, rx = rng.randrange(oy, oy + h), rng.randrange(ox, ox + w)
            for x in range(ox, ox + w):
                types[(x, ry)] = 1
            for y in range(oy, oy + h):
                types[(rx, y)] = 1
    for _ in range(rng.randint(0, w * h // 12)):
        types[(rng.randrange(ox, ox + w), rng.randrange(oy, oy + h))] = rng.choice([3, 3, 9, 4, 0x1E, 2, 10, 5, 7])
    links = [[x, y, rng.choice([0x55, 0, 1, 4, 16, 64, 0x11, 0x44, rng.randrange(256)])] for (x, y), t in sorted(types.items()) if t in (1, 3, 9, 10)]
    qlinks = [[x, y, rng.choice([1, 4, 16, 64, 0])] for (x, y), t in sorted(types.items()) if t in (3, 9, 10)]
    flags = [[x, y, 0x400] for (x, y), t in sorted(types.items()) if t == 0x1E and rng.random() < 0.3]
    paths = sorted(c for c, t in types.items() if t == 1) or [(ox, oy)]
    return {'types': [[x, y, t] for (x, y), t in sorted(types.items())], 'links': links, 'qlinks': qlinks, 'flags': flags,
            'window': (ox, oy, w, h), 'paths': paths}


def walk_edge_binary(sb: Sandbox, x: dict, log) -> tuple:
    walk_map_binary(sb, x)
    return tuple(sb.call(WALK_EDGE, args=(cx, cy, d, x['mask'])).eax & 0xFF for cx, cy, d in x['edges'])


def walk_edge_plan(x: dict) -> tuple:
    m = walk_map_reading(x)
    return tuple(walk_reading.blocked(m, cx, cy, d, x['mask']) for cx, cy, d in x['edges'])


def walk_commit_binary(sb: Sandbox, x: dict, log) -> tuple:
    walk_map_binary(sb, x)
    return tuple(sb.call(WALK_COMMIT, args=(fy * 128 + fx + 1, ty * 128 + tx + 1, x['mask'])).eax for fx, fy, tx, ty in x['steps'])


def walk_commit_plan(x: dict) -> tuple:
    m = walk_map_reading(x)
    return tuple(walk_reading.commit(m, (fx, fy), (tx, ty), x['mask']) for fx, fy, tx, ty in x['steps'])


def walk_cell_inputs(rng: random.Random, n: int, kind: str) -> list:
    out = []
    for _ in range(n):
        x = walk_random_cells(rng)
        x['mask'] = rng.choice([0, 0, 1, 2])
        ox, oy, w, h = x['window']
        def cell():
            return rng.randrange(max(0, ox - 1), min(128, ox + w + 1)), rng.randrange(max(0, oy - 1), min(128, oy + h + 1))
        if kind == 'edge':
            x['edges'] = [cell() + (rng.randrange(4),) for _ in range(12)]
        else:
            steps = []
            while len(steps) < 12:
                fx, fy = cell()
                tx, ty = fx + rng.choice([-1, 0, 1, 1, -2]), fy + rng.choice([-1, 0, 1, -1, 2])
                if 0 <= tx < 128 and 0 <= ty < 128:
                    steps.append((fx, fy, tx, ty))
            x['steps'] = steps
        del x['paths']
        out.append(x)
    # the map edges: x 0 / 127, y 0 / 127 in every direction
    edge = {'types': [[0, 0, 1], [127, 127, 1], [1, 0, 1], [0, 1, 1]], 'links': [], 'qlinks': [], 'flags': [], 'window': (0, 0, 128, 128), 'mask': 0}
    if kind == 'edge':
        out.append(dict(edge, edges=[(0, 0, d) for d in range(4)] + [(127, 127, d) for d in range(4)]))
    else:
        out.append(dict(edge, steps=[(0, 0, 1, 1), (0, 0, 1, 0), (1, 0, 0, 1), (127, 127, 126, 126)]))
    return out


def walk_search_binary(sb: Sandbox, x: dict, log) -> tuple:
    walk_map_binary(sb, x)
    buf = sb.object(0x200)
    (sx, sy), (gx, gy) = x['start'], x['goal']
    for offset, value in ((2, sx), (3, sy), (4, gx), (5, gy)):
        sb.write(buf + offset, 'B', value)
    result = sb.call(WALK_SEARCH, args=(buf, x['mask'])).eax
    if result == walk_reading.FAIL:
        return (False,)
    return (True,) + tuple((sb.read(buf + 6 + 2 * i, 'B'), sb.read(buf + 7 + 2 * i, 'B')) for i in range(sb.read(buf, 'B')))


def walk_search_plan(x: dict) -> tuple:
    result, path = walk_reading.find(walk_map_reading(x), tuple(x['start']), tuple(x['goal']), x['mask'])
    return (False,) if result == walk_reading.FAIL else (True,) + tuple(path)


def walk_search_inputs(rng: random.Random, n: int) -> list:
    out = []
    for _ in range(n):
        x = walk_random_cells(rng)
        ox, oy, w, h = x['window']
        x['mask'] = rng.choice([0, 0, 1, 2])
        x['start'] = rng.choice(x['paths'])
        x['goal'] = rng.choice(x['paths']) if rng.random() < 0.85 else (rng.randrange(ox, min(128, ox + w)), rng.randrange(oy, min(128, oy + h)))
        del x['paths']
        out.append(x)
    return out


def walk_avoid_binary(sb: Sandbox, x: dict, log) -> tuple:
    walk_map_binary(sb, x)
    walk = sb.object(0x100)
    for offset, key in ((4, 'r'), (8, 'x'), (0xC, 'y'), (0x10, 'vx'), (0x14, 'vy'), (0xB4, 'mask')):
        sb.write(walk + offset, 'i', x['walk'][key])
    out = sb.object(16)
    sb.call(WALK_AVOID, args=(out, walk), this=0)
    return sb.read(out, 'i'), sb.read(out + 4, 'i')


def walk_avoid_plan(x: dict) -> tuple:
    return tuple(walk_reading.avoid_walls(walk_map_reading(x), x['walk']))


def walk_avoid_inputs(rng: random.Random, n: int) -> list:
    out = []
    for i in range(n):
        if i % 4 == 0:
            # a queue cell on the diagonal cuts the corner its two path neighbours share: the corner blocks and the ray test
            x = {'types': [[cx, cy, 3 if (cx, cy) == (41, 41) else 1] for cx in (39, 40, 41) for cy in (39, 40, 41)],
                 'links': [], 'qlinks': [], 'flags': [], 'paths': [(40, 40)]}
        else:
            x = walk_random_cells(rng)
        cx, cy = rng.choice(x['paths'])
        near = rng.random() < 0.4
        fx = rng.choice([rng.randrange(0, 16000), rng.randrange(49536, 65536)]) if near else rng.randrange(65536)
        fy = rng.choice([rng.randrange(0, 16000), rng.randrange(49536, 65536)]) if near else rng.randrange(65536)
        x['walk'] = {'r': walk_reading.RADIUS, 'mask': rng.choice([0, 0, 1, 2]), 'x': (cx << 16) + fx, 'y': (cy << 16) + fy,
                     'vx': rng.randint(-30000, 30000) if rng.random() > 0.1 else 0, 'vy': rng.randint(-30000, 30000) if rng.random() > 0.1 else 0}
        del x['paths']
        out.append(x)
    return out


def walk_steering_binary(sb: Sandbox, x: dict, log) -> tuple:
    """Set-up 0x50f8e0, then the steering step 0x50f3b0 with the behaviour list avoid_walls 0xe666, follow_path 0x8000 and
    separation 0x1999 (registration 0x50fdf0); edits write a cell type and stamp its block with the raised counter."""
    base = walk_map_binary(sb, x)
    game = sb.object(0x2D8 + 0x1B0000 + 33 * 9 * 4)
    sb.write(WALK_GAME, 'I', game)
    pairs = sb.object(24)
    for i, (behaviour, weight) in enumerate(((WALK_AVOID, 0xE666), (0x50ED10, 0x8000), (0x50E0E0, 0x1999))):
        table = sb.object(4)
        sb.write(table, 'I', behaviour)
        instance = sb.object(8)
        sb.write(instance, 'I', table)
        sb.write(pairs + 8 * i, 'I', weight)
        sb.write(pairs + 8 * i + 4, 'I', instance)
    sb.write(WALK_BEHAVIOURS[0], 'I', pairs)
    sb.write(WALK_BEHAVIOURS[1], 'I', pairs + 24)
    sb.write(WALK_COUNTER, 'I', 0)
    walk = sb.object(0x100)
    sb.write(walk, 'I', 0x10000)
    sb.write(walk + 4, 'I', walk_reading.RADIUS)
    sb.write(walk + 0x60, 'B', 1)
    init = x['walk']
    for offset, key in ((8, 'x'), (0xC, 'y'), (0x10, 'vx'), (0x14, 'vy'), (0x18, 'max_force'), (0x1C, 'max_speed'), (0xB4, 'mask')):
        sb.write(walk + offset, 'i', init[key])
    target = sb.object(8)

    def read(offset, fmt='i'):
        return sb.read(walk + offset, fmt)

    def fields() -> tuple:
        count = read(0x58)
        return (read(8), read(0xC), read(0x10), read(0x14), read(0x4C), read(0x50), read(0x54), count, read(0x5C), read(0x60, 'B'),
                read(0xA0), read(0xA4), read(0xA8), read(0xAC), read(0xB0) & 0x7FFF, int(read(0xB8) != 0),
                tuple(((read(0x64 + 8 * i) >> 16) & 0xFF, (read(0x68 + 8 * i) >> 16) & 0xFF) for i in range(count)))

    trace = []
    for kind, a, b, c in x['events']:
        if kind == 'go':
            sb.write(target, 'i', a)
            sb.write(target + 4, 'i', b)
            trace.append(('go', sb.call(WALK_SETUP, args=(target, 0), this=walk).eax, fields()))
        elif kind == 'edit':
            sb.write(base + 0x294 + (b * 128 + a + 1) * 0x44 + 8, 'I', c)
            counter = (sb.read(WALK_COUNTER, 'I') + 1) & MASK32
            sb.write(WALK_COUNTER, 'I', counter)
            sb.write(game + 0x2D8 + 0x1B0000 + ((b >> 4) * 33 + (a >> 4)) * 4, 'I', counter)
        else:
            sb.call(WALK_STEP, this=walk)
            progress = sb.call(WALK_PROGRESS, this=walk).eax
            trace.append(('step', 2 if read(0xB8) else 0 if progress == 0x10000 else 1, fields()))
    return tuple(trace)


def walk_steering_plan(x: dict) -> tuple:
    init = x['walk']
    walk = walk_reading.Walk(walk_map_reading(x), {}, init['x'], init['y'], init['vx'], init['vy'], init['mask'], init['max_speed'], init['max_force'])
    trace = []
    for kind, a, b, c in x['events']:
        if kind == 'go':
            trace.append(('go', 1 if walk.setup(a, b, False) else 0, walk.fields()))
        elif kind == 'edit':
            walk.edit(a, b, c)
        else:
            result = walk.step()
            trace.append(('step', result, walk.fields()))
    return tuple(trace)


WALK_STEERING_STEPS = 80
WALK_STEERING_INPUTS = 40      # each input is a whole walk; the step count bounds the run time


def walk_steering_inputs(rng: random.Random, n: int) -> list:
    out = []
    for _ in range(min(n, WALK_STEERING_INPUTS)):
        x = walk_random_cells(rng)
        cx, cy = rng.choice(x['paths'])
        speed = rng.choice([655, 4000, 7733, 13107, 20000, 26214])
        x['walk'] = {'x': (cx << 16) + rng.randrange(8000, 57000), 'y': (cy << 16) + rng.randrange(8000, 57000),
                     'vx': rng.randint(-5000, 5000), 'vy': rng.randint(-5000, 5000), 'mask': rng.choice([0, 0, 0, 1]),
                     'max_speed': speed, 'max_force': min(52428, 2 * speed)}
        events, types = [], {(a, b): t for a, b, t in x['types']}
        for k in range(WALK_STEERING_STEPS):
            if k == 0 or rng.random() < 0.02:
                gx, gy = rng.choice(x['paths'])
                events.append(('go', (gx << 16) + rng.randrange(5 << 8, 123 << 8), (gy << 16) + rng.randrange(5 << 8, 123 << 8), 0))
            if rng.random() < 0.02:
                ex, ey = cx + rng.randint(-3, 3), cy + rng.randint(-3, 3)
                kind = rng.choice([0, 1, 1, 3])
                if 0 <= ex < 128 and 0 <= ey < 128 and types.get((ex, ey), 0) != kind:
                    types[(ex, ey)] = kind
                    events.append(('edit', ex, ey, kind))
            events.append(('step', 0, 0, 0))
        x['events'] = events
        del x['paths']
        out.append(x)
    return out


WALK_EDGE_TARGET = Target(
    'tpw-exe-walk-edge-test', 'tpw', 'tp.exe walk edge test 0x4d8750', 'WalkEdges.Blocked (WALK-005, WALK-006)',
    WALK_EDGE, WALK_SEARCH_RANGES, walk_stubs, walk_edge_binary, walk_edge_plan, lambda rng, n: walk_cell_inputs(rng, n, 'edge'),
    notes=['A synthetic 128 x 128 map at [0x7cf6ec] (cells 0x44 bytes from +0x294, id = y * 128 + x + 1); no objects (the cell word +0 '
           'names an empty record), so 0x5363f0 is false. Twelve edges per map, every mask.'], budget=2_000_000)
WALK_COMMIT_TARGET = Target(
    'tpw-exe-walk-commit', 'tpw', 'tp.exe walk step commit 0x510510', 'WalkEdges.Commit (WALK-005)',
    WALK_COMMIT, WALK_SEARCH_RANGES, walk_stubs, walk_commit_binary, walk_commit_plan, lambda rng, n: walk_cell_inputs(rng, n, 'commit'),
    notes=['The same synthetic maps; twelve steps of up to two cells per map, cell ids as 0x510870 forms them.'], budget=2_000_000)
WALK_SEARCH_TARGET = Target(
    'tpw-exe-walk-search', 'tpw', 'tp.exe walk route search 0x511420', 'WalkSearch.Find (WALK-004)',
    WALK_SEARCH, WALK_SEARCH_RANGES, walk_stubs, walk_search_binary, walk_search_plan, walk_search_inputs,
    notes=['The line search 0x511470, its tracers 0x511c90, the shortcuts 0x5110d0 and the refinement 0x5108a0 with the library '
           'memmove 0x67bb90; the waypoint list is compared, or the failure.'], budget=400_000_000)
WALK_AVOID_TARGET = Target(
    'tpw-exe-walk-avoid-walls', 'tpw', 'tp.exe avoid_walls 0x50e4a0', 'GuestNavigation.AvoidWallsOriginal (WALK-002 retired)',
    WALK_AVOID, WALK_AVOID_RANGES, walk_stubs, walk_avoid_binary, walk_avoid_plan, walk_avoid_inputs,
    notes=['The walk object holds the radius +4, position +8, velocity +0x10 and mask +0xb4; a quarter of the inputs stand next to a '
           'queue cell that cuts a corner, so the corner blocks and the ray test 0x50cc20 run.'], budget=5_000_000)
WALK_STEERING_TARGET = Target(
    'tpw-exe-walk-steering', 'tpw', 'tp.exe walk set-up 0x50f8e0 and steering step 0x50f3b0', 'GuestNavigation.Navigate / Step (WALK-004, -007, -009)',
    WALK_STEP, WALK_STEP_RANGES, walk_stubs, walk_steering_binary, walk_steering_plan, walk_steering_inputs,
    notes=['A whole walk per input: set-ups, 80 steering steps, map edits that stamp their block. The behaviour list is the '
           'registration order with its weights; separation runs with its neighbour query 0x5102f0 stubbed (no neighbours). Every '
           'walk field the step reads is compared after each set-up and step, with the walk step\'s result (0x4fa2a0).'],
    budget=2_000_000_000)

# ============================================================================== (t) tp.exe TQI movie matrix 0x6710c0 and transform pass 0x67673c
TQI_MATRIX = 0x6710C0          # cdecl (int *matrix, int quantiser): fills 64 dwords
TQI_PASS = 0x67673C            # register call: esi = 8 input dwords, edi = output (8 dwords, stride 0x24); no shift
TQI_RANGES = [(0x6710C0, 0x671192), (0x67673C, 0x67687B), (0x67A830, 0x67A860)]   # the matrix, the pass, _ftol
TQI_PRESCALE = 0x705C88        # 64 dwords, Q13 AAN prescale
TQI_WEIGHTS = 0x705D88         # 64 dwords, MPEG-1 intra matrix
TQI_ODD = (0.5411961078643799, 1.3065630197525024, 0.3826834261417389)   # floats at 0xfbb59c, 0xfbb5a0, 0xfbb5a4


def tqi_stubs(target: Target) -> dict:
    return {}


def tqi_matrix_binary(sb: Sandbox, q: int, log) -> list:
    out = sb.alloc(256)
    sb.call(TQI_MATRIX, (out, q))
    return [s32(sb.read(out + 4 * i, 'I')) for i in range(64)]


TQI_PRESCALE_VALUES = [8192, 5906, 6270, 6967, 8192, 10426, 15137, 29692, 5906, 4258, 4520, 5023, 5906, 7517, 10913, 21407,
                       6270, 4520, 4799, 5332, 6270, 7980, 11585, 22725, 6967, 5023, 5332, 5925, 6967, 8867, 12873, 25251,
                       8192, 5906, 6270, 6967, 8192, 10426, 15137, 29692, 10426, 7517, 7980, 8867, 10426, 13270, 19266, 37791,
                       15137, 10913, 11585, 12873, 15137, 19266, 27969, 54864, 29692, 21407, 22725, 25251, 29692, 37791, 54864, 107619]
TQI_WEIGHT_VALUES = [8, 16, 19, 22, 26, 27, 29, 34, 16, 16, 22, 24, 27, 29, 34, 37, 19, 22, 26, 27, 29, 34, 34, 38, 22, 22, 26, 27,
                     29, 34, 37, 40, 22, 26, 27, 29, 32, 35, 40, 48, 26, 27, 29, 32, 35, 40, 48, 58, 26, 27, 29, 34, 38, 46, 56, 69,
                     27, 29, 35, 38, 46, 56, 69, 83]


def tqi_matrix_plan(q: int) -> list:
    """TqiDecoder.BuildMatrix: S · trunc(W · (float)((107 − q) / 16) · 65536), low 16 bits rounded half up; 8 · S for q = 100; M[0] = 2^16."""
    factor = struct.unpack('<f', struct.pack('<f', (107.0 - q) * 0.0625))[0]
    out = []
    for s_, w_ in zip(TQI_PRESCALE_VALUES, TQI_WEIGHT_VALUES):
        weight = 0x80000 if q == 100 else int(w_ * factor * 65536.0)
        out.append(s32((s_ * weight + 0x8000) >> 16))
    out[0] = 0x10000
    return out


def tqi_matrix_inputs(rng: random.Random, n: int) -> list:
    return list(range(0, 108))


def tqi_pass_binary(sb: Sandbox, x: list, log) -> list:
    source, out = sb.alloc(32), sb.alloc(0x120)
    for i, value in enumerate(x):
        sb.write(source + 4 * i, 'I', value & MASK32)
    sb.call(TQI_PASS, regs={'esi': source, 'edi': out})
    return [s32(sb.read(out + 0x24 * i, 'I')) for i in range(8)]


def _tqi_round_even(value: float) -> int:
    return s32(struct.unpack('<q', struct.pack('<d', value + 6755399441055744.0))[0])


def _tqi_half_sqrt2(value: int) -> int:
    return s32(((s32(value) * 0x5A82799A) >> 32) << 1)


def tqi_pass_plan(x: list) -> list:
    """TqiDecoder.Butterfly (with the first pass's zero shortcut): integer adds, doubled Q31 high dwords, the odd part in double precision."""
    if not any(x[1:]):
        return [x[0]] * 8
    a_f, b_f, z_f = TQI_ODD
    z13, z10, z11, z12 = s32(x[3] + x[5]), s32(x[5] - x[3]), s32(x[7] + x[1]), s32(x[1] - x[7])
    t7 = s32(z13 + z11)
    z5 = (float(z12) + z10) * z_f
    a, b = _tqi_round_even(z10 * a_f + z5), _tqi_round_even(z12 * b_f - z5)
    r11 = _tqi_half_sqrt2(z11 - z13)
    p, q, w = s32(a + r11), s32(r11 + b), s32(b + t7)
    t13, t10, t11 = s32(x[6] + x[2]), s32(x[4] + x[0]), s32(x[0] - x[4])
    r = _tqi_half_sqrt2(x[2] - x[6])
    e, plus, minus = s32(t13 + r), s32(r + t11), s32(t11 - r)
    return [s32(e + t10 + w), s32(q + plus), s32(minus + p), s32(t10 - e + a), s32(t10 - e - a), s32(minus - p), s32(plus - q), s32(e + t10 - w)]


def tqi_pass_inputs(rng: random.Random, n: int) -> list:
    """Coefficient rows of the shipped range (|x| < 2^26), with the zero-shortcut rows; rows whose odd-part products end within 0.1
    of a half are left out: Unicorn's x87 rounds those away from zero in about one case of twenty, which no IEEE precision does."""
    out = [[0] * 8, [5 << 16] + [0] * 7, [-(3 << 16), 0, 0, 0, 0, 0, 0, 0]]
    a_f, b_f, z_f = TQI_ODD
    while len(out) < n:
        x = [rng.randint(-(1 << 26), 1 << 26) if rng.random() < 0.6 else 0 for _ in range(8)]
        z10, z12 = s32(x[5] - x[3]), s32(x[1] - x[7])
        z5 = (float(z12) + z10) * z_f
        if any(abs(abs(v - math.floor(v)) - 0.5) < 0.1 for v in (z10 * a_f + z5, z12 * b_f - z5)):
            continue
        out.append(x)
    return out


TQI_MATRIX_TARGET = Target(
    'tpw-exe-tqi-matrix', 'tpw', 'tp.exe TQI dequantisation matrix 0x6710c0', 'TGQ-MOVIES.md (TqiDecoder.BuildMatrix)',
    TQI_MATRIX, TQI_RANGES, tqi_stubs, tqi_matrix_binary, tqi_matrix_plan, tqi_matrix_inputs,
    notes=['Every quantiser 0..107 (the header byte the decoder accepts); _ftol 0x67a830 runs in the sandbox.'])
TQI_PASS_TARGET = Target(
    'tpw-exe-tqi-transform-pass', 'tpw', 'tp.exe TQI transform pass 0x67673c', 'TGQ-MOVIES.md (TqiDecoder.Butterfly)',
    TQI_PASS, TQI_RANGES, tqi_stubs, tqi_pass_binary, tqi_pass_plan, tqi_pass_inputs,
    notes=['The first pass (the second, 0x676965, is the same butterfly without the zero shortcut and with sar 17 per output). '
           'Whole movies were compared too: the decoder 0x6747d0 over 51 sampled frames of the nine movies matched TqiDecoder in '
           'every sample but one, a half-way odd-part rounding Unicorn gets wrong (docs/TGQ-MOVIES.md).'])

# ============================================================================== X27: the procedural terrain texturer
# tp.exe 0x55e710 (ramp table), 0x55e780 (weight plane) and 0x55f780 (the build routine, floor descriptor NULL). Plan: terrain_reading.py,
# the reading OpenTPW's ProceduralTerrainTexturer follows (docs/LND.md).
import terrain_reading  # noqa: E402

TERRAIN_RAMP = 0x55E710
TERRAIN_WEIGHTS = 0x55E780
TERRAIN_BUILD = 0x55F780
TERRAIN_RAMP_RANGES = [(0x55E710, 0x55E77F)]
TERRAIN_WEIGHT_RANGES = [(0x55E780, 0x55E974)]
TERRAIN_BUILD_RANGES = [(0x55E710, 0x561000)]   # the ramp, weights, the two and three layer blends and the build routine itself
TERRAIN_TABLES = {0x8795A8: (0, 1), 0x8796A8: (2, 3), 0x8793A8: (4, 5), 0x8797A8: (6, 7), 0x8794A8: (8, 9)}   # table address -> parameter pair
TERRAIN_STATES = (0x8798A8, 0x8798B8, 0x879398)   # weight plane records: dword N at +0, plane pointer at +12
TERRAIN_BUFFER = 0x8798C8                         # the build buffer pointer (the routine returns early while it is null)
# file image index -> offset of its (w, h, bpp, pointer) record in the set the build routine reads (0x55f470 places them)
TERRAIN_SLOTS = (0x08, 0x18, 0x38, 0x48, 0x88, 0x98, 0x28, 0x58, 0xA8, 0x68, 0x78, 0xB8, 0xC8)


def terrain_stubs(target: Target) -> dict:
    return {}


def terrain_ramp_binary(sb: Sandbox, x: tuple, log) -> bytes:
    table = sb.alloc(256, 16)
    sb.call(TERRAIN_RAMP, (table, x[0], x[1]))
    return bytes(sb.uc.mem_read(table, 256))


def terrain_ramp_plan(x: tuple) -> bytes:
    return terrain_reading.ramp(*x)


def terrain_ramp_inputs(rng: random.Random, n: int) -> list:
    edges = [0, 1, 2, 3, 127, 128, 129, 253, 254, 255]
    return [(c, w) for c in edges for w in edges] + [(rng.randrange(256), rng.randrange(256)) for _ in range(n)]


def terrain_weights_binary(sb: Sandbox, x: dict, log) -> bytes:
    grid = sb.alloc(len(x['grid']), 16)
    sb.write(grid, f"{len(x['grid'])}s", x['grid'])
    record = sb.alloc(16, 16)
    for offset, value in ((0, x['width']), (4, x['height']), (8, 4), (12, grid)):
        sb.write(record + offset, 'I', value)
    plane = sb.alloc(x['n'] * x['n'], 16)
    state = sb.alloc(16, 16)
    sb.write(state, 'I', x['n'])
    sb.write(state + 12, 'I', plane)
    sb.call(TERRAIN_WEIGHTS, (record, x['x'], x['row'], x['k'], state))
    return bytes(sb.uc.mem_read(plane, x['n'] * x['n']))


def terrain_weights_plan(x: dict) -> bytes:
    return bytes(terrain_reading.weights(x, x['n'], x['x'], x['row'], x['k']))


TERRAIN_TEST_CELLS = 3   # the synthetic weight grids are 3 x 3 cells: 13 x 13 samples


def terrain_weights_inputs(rng: random.Random, n: int) -> list:
    side = 4 * TERRAIN_TEST_CELLS + 1
    patterns = [bytes(side * side * 4), bytes([255] * (side * side * 4)),
                bytes((255 if (i // 4 + i // (4 * side)) & 1 else 0) for i in range(side * side * 4)),
                bytes(((i // 4) % side) * 20 & 0xFF for i in range(side * side * 4))]
    out = []
    for index in range(n):
        grid = patterns[index % len(patterns)] if index < 2 * len(patterns) else bytes(rng.randrange(256) for _ in range(side * side * 4))
        out.append({'n': (16, 32, 64)[index % 3], 'x': rng.randrange(TERRAIN_TEST_CELLS), 'row': rng.randrange(TERRAIN_TEST_CELLS),
                    'k': 1 + index % 3, 'width': side, 'height': side, 'grid': grid})
    return out


def terrain_build_binary(sb: Sandbox, x: dict, log) -> bytes:
    """Prepare the level object the routine reads (the loader 0x560FF0 and set reader 0x55f470 place these fields), then build one cell."""
    n_size = x['n']
    set_index = terrain_reading.SETS[n_size]
    level = x['level']
    obj = sb.alloc(0x24 + 3 * 0xD8 + 16, 16)
    sb.write(obj, 'B', n_size)
    sb.write(obj + 1, 'B', set_index)
    for index, value in enumerate(level['params']):
        sb.write(obj + 2 + index, 'B', value)
    sb.write(obj + 0xC, 'i', (level['width'] - 1) >> 2)
    sb.write(obj + 0x10, 'i', (level['height'] - 1) >> 2)
    sb.write(obj + 0x14, 'i', level['width'])
    sb.write(obj + 0x18, 'i', level['height'])
    sb.write(obj + 0x1C, 'i', 4)
    grid = sb.alloc(len(level['grid']), 16)
    sb.write(grid, f"{len(level['grid'])}s", bytes(level['grid']))
    sb.write(obj + 0x20, 'I', grid)
    base = obj + 0x24 + set_index * 0xD8
    for index, (width, height, bpp, data) in enumerate(level['images']):
        pointer = sb.alloc(len(data), 16)
        sb.write(pointer, f'{len(data)}s', bytes(data))
        record = base + TERRAIN_SLOTS[index]
        for offset, value in ((0, width), (4, height), (8, bpp)):
            sb.write(record + offset, 'i', value)
        sb.write(record + 12, 'I', pointer)
    # the ramp tables go through the original's own routine, as the loader does
    for table, (first, second) in TERRAIN_TABLES.items():
        sb.call(TERRAIN_RAMP, (table, level['params'][first], level['params'][second]))
    for state in TERRAIN_STATES:
        sb.write(state, 'i', n_size)
        sb.write(state + 12, 'I', sb.alloc(n_size * n_size, 16))
    sb.write(TERRAIN_BUFFER, 'I', sb.alloc(n_size * n_size * 4, 16))
    destination = sb.alloc(n_size * n_size * 4, 16)
    sb.call(TERRAIN_BUILD, (destination, x['x'], x['z'], 0), this=obj)
    return bytes(sb.uc.mem_read(destination, n_size * n_size * 4))


def terrain_build_plan(x: dict) -> bytes:
    return terrain_reading.build(x['level'], x['n'], x['x'], x['z'])[0]


def terrain_build_per_texel_weight(x: dict) -> bytes:
    """Without the three layer quirk (w1 per texel in a CBA run): differs from the original on every cell that has a CBA run."""
    return terrain_reading.build(x['level'], x['n'], x['x'], x['z'], quirk=False)[0]


# the layer mix of a synthetic level: how the masks (mask 1 over C, mask 2 over B) behave per 16 texel run
TERRAIN_MIXES = ('C', 'B', 'A', 'BA', 'CB', 'CA', 'CBA', 'random')


def terrain_level(rng: random.Random, n_size: int, mix: str, valid: bool = True) -> tuple[dict, int]:
    cells, side = TERRAIN_TEST_CELLS, 4 * TERRAIN_TEST_CELLS + 1
    # mask ramps with a hard step (centre 128, width 0): a masked value is 0 below 128 and 255 above; the layer ramps are random
    params = [rng.randrange(256) for _ in range(10)]
    if mix != 'random':
        params[4:6] = [128, 0]
        params[8:10] = [128, 0]
    mask1, mask2 = {'C': ('all', 'all'), 'B': ('none', 'all'), 'A': ('none', 'none'), 'BA': ('none', 'mixed'), 'CB': ('mixed', 'all'),
                    'CA': ('mixed', 'none'), 'CBA': ('mixed', 'mixed'), 'random': ('random', 'random')}[mix]

    def weight(style):
        return {'all': lambda: 255, 'none': lambda: 0, 'mixed': lambda: 255, 'random': lambda: rng.randrange(256)}[style]()

    def mask_pixel(style):
        return {'all': lambda: 255, 'none': lambda: 0, 'mixed': lambda: rng.choice((0, 255)), 'random': lambda: rng.randrange(256)}[style]()

    grid = bytearray()
    for _ in range(side * side):
        grid += bytes([255 if valid else rng.choice((0, 255)), rng.randrange(256), weight(mask2), weight(mask1)])
    images = []
    for index in range(13):
        width, height = n_size + rng.randrange(1, 25), n_size + rng.randrange(1, 25)
        if index < 6:
            data = bytes(rng.randrange(256) if i % 4 else 0 for i in range(width * height * 4))
        else:
            style = {9: mask2, 10: mask2, 11: mask1, 12: mask1}.get(index)
            data = bytes(mask_pixel(style) if style else rng.randrange(256) for _ in range(width * height))
        images.append((width, height, 4 if index < 6 else 1, data))
    return {'params': bytes(params), 'width': side, 'height': side, 'grid': bytes(grid), 'images': images}, cells


def terrain_build_inputs(rng: random.Random, n: int) -> list:
    """Cells of synthetic levels, one layer mix after the other (every mode of the run decision), sizes 16 and 32 mostly, 64 now and then,
    and a few cells whose inner samples are not all valid (the grey fallback). Capped: every cell runs in the emulator."""
    out = []
    for index in range(min(n, 48)):
        n_size = (16, 32, 16, 32, 64)[index % 5]
        mix = TERRAIN_MIXES[index % len(TERRAIN_MIXES)]
        grey = index % 11 == 10
        level, cells = terrain_level(rng, n_size, mix, valid=not grey)
        if grey:
            # one of the four inner samples of the cell is invalid
            x, z = rng.randrange(cells), rng.randrange(cells)
            row = cells - z - 1
            position = ((4 * row + 1 + rng.randrange(2)) * level['width'] + 4 * x + 1 + rng.randrange(2)) * 4
            grid = bytearray(level['grid'])
            grid[position] = 0x7F
            level['grid'] = bytes(grid)
        else:
            x, z = rng.randrange(cells), rng.randrange(cells)
        out.append({'n': n_size, 'x': x, 'z': z, 'mix': mix, 'level': level})
    return out


TERRAIN_RAMP_TARGET = Target(
    'tpw-exe-terrain-ramp', 'tpw', 'tp.exe procedural terrain ramp table 0x55e710', 'docs/LND.md (ProceduralTerrainTexturer.BuildRamp)',
    TERRAIN_RAMP, TERRAIN_RAMP_RANGES, terrain_stubs, terrain_ramp_binary, terrain_ramp_plan, terrain_ramp_inputs,
    notes=['Every (centre, width) pair of the edge values and random pairs; the table is 0 below c - (w + 1) / 2, 255 above c + (w + 1) / 2, '
           'linear (255 * (i - low)) / (w + 1) between.'])
TERRAIN_WEIGHTS_TARGET = Target(
    'tpw-exe-terrain-weights', 'tpw', 'tp.exe procedural terrain weight plane 0x55e780', 'docs/LND.md (ProceduralTerrainTexturer weight planes)',
    TERRAIN_WEIGHTS, TERRAIN_WEIGHT_RANGES, terrain_stubs, terrain_weights_binary, terrain_weights_plan, terrain_weights_inputs,
    notes=['cdecl (grid record, cell x, file row, weight 1..3, plane record), N = 16, 32 and 64 on constant, checkerboard, ramp and random grids.'])
TERRAIN_BUILD_TARGET = Target(
    'tpw-exe-terrain-build', 'tpw', 'tp.exe procedural terrain build 0x55f780', 'docs/LND.md (ProceduralTerrainTexturer.Build)',
    TERRAIN_BUILD, TERRAIN_BUILD_RANGES, terrain_stubs, terrain_build_binary, terrain_build_plan, terrain_build_inputs,
    budget=50_000_000,
    notes=['thiscall (ecx = level object; destination, cell x, cell z, floor descriptor NULL). Synthetic levels whose masks give every run mode '
           '(C, B, A, BA, CB, CA, CBA) plus random masks and a few invalid cells. A reading without the three layer quirk differs (test_x86_oracle).'])

WALK_TARGETS = [WALK_EDGE_TARGET, WALK_COMMIT_TARGET, WALK_SEARCH_TARGET, WALK_AVOID_TARGET, WALK_STEERING_TARGET]

TARGETS = [WORLD_RNG_TARGET, TPI_WORLD_RNG_TARGET, ARRIVAL_TARGET, LOAN_OFFERS_TARGET, LOAN_TERM_TARGET, LOAN_MONTH_TARGET, POINTS_TARGET,
           SCRAP_TARGET, QREEVAL_TARGET, COUNTS_TARGET, PRICE_TARGET, WEAR_TARGET, STATE6_TARGET, WANDER_TARGET, LITTER_TARGET, BIN_WALK_TARGET, REGION_TARGET, FINDER_TARGET, GUARD_TARGET,
           TPI_ARRIVAL_TARGET, TPI_LOAN_OFFERS_TARGET, TPI_LOAN_TERM_TARGET, TPI_LOAN_MONTH_TARGET, TPI_POINTS_TARGET, TQI_MATRIX_TARGET, TQI_PASS_TARGET, TERRAIN_RAMP_TARGET, TERRAIN_WEIGHTS_TARGET, TERRAIN_BUILD_TARGET] + COAST_TARGETS + WALK_TARGETS

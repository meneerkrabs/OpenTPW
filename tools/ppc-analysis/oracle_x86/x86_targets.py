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

    def sandbox(self, image: Image) -> Sandbox:
        self.log = []
        return Sandbox(image, self.allowed, self.stubs(self))


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

TARGETS = [WORLD_RNG_TARGET, TPI_WORLD_RNG_TARGET, ARRIVAL_TARGET, LOAN_OFFERS_TARGET, LOAN_TERM_TARGET, LOAN_MONTH_TARGET, POINTS_TARGET]

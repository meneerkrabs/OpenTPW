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

TARGETS = [WORLD_RNG_TARGET, TPI_WORLD_RNG_TARGET, ARRIVAL_TARGET, LOAN_OFFERS_TARGET, LOAN_TERM_TARGET, LOAN_MONTH_TARGET, POINTS_TARGET,
           SCRAP_TARGET, QREEVAL_TARGET,
           TPI_ARRIVAL_TARGET, TPI_LOAN_OFFERS_TARGET, TPI_LOAN_TERM_TARGET, TPI_LOAN_MONTH_TARGET, TPI_POINTS_TARGET] + COAST_TARGETS

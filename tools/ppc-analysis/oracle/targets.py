"""Differential oracle targets: isolated routines of the identified Mac binary vs OpenTPW and the plans.

Each target has
* `binary(sandbox, x)`: run the original routine in the sandbox on input x and return a comparable value;
* `plan(x)`: the plan's reference formula (docs/reverse/*-plan.md), in Python;
* optionally `csharp`: (bridge function, request builder, response mapper) for OpenTPW's implementation on
  main, used when the bridge lists the function (`Bridge.capabilities`);
* `inputs(rng, n)`: n seeded random inputs plus explicit boundaries.

Architecturally undefined original behaviour (`divw` by zero, `INT_MIN / -1`) surfaces as the interpreter's
`Undefined`; it is compared as the value UNDEFINED, which only an equally undefined reference matches. Any other
`Unsupported` (an unmodelled instruction, `sc`, `tw`) aborts the run as an error.

Stubs are listed per target with the reason they are safe; any other call aborts the run.
"""
from __future__ import annotations

import math
import random
import struct
import sys
from dataclasses import dataclass, field
from pathlib import Path
from typing import Callable

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
sys.path.insert(0, str(HERE.parent / 'lanes' / 'det'))

import det_model  # noqa: E402  (DET-plan's reference arithmetic)
from harness import STACK_BASE, STACK_SIZE, Image, Sandbox, f32  # noqa: E402
from ppc32 import MASK32, Machine, Undefined, bits_to_double, double_to_bits, s32  # noqa: E402

UNDEFINED = 'UNDEFINED'


def bits(value: float) -> int:
    return struct.unpack('>I', struct.pack('>f', value))[0]


def from_bits(word: int) -> float:
    return struct.unpack('>f', struct.pack('>I', word & MASK32))[0]


def same_float(a: float, b: float) -> bool:
    """Bitwise single equality, except that any NaN equals any NaN."""
    if math.isnan(a) and math.isnan(b):
        return True
    return bits(a) == bits(b)


def random_float(rng: random.Random, low: float, high: float) -> float:
    return f32(rng.uniform(low, high))


def random_single_bits(rng: random.Random) -> float:
    """Any finite single, uniformly over bit patterns."""
    while True:
        value = from_bits(rng.getrandbits(32))
        if math.isfinite(value):
            return value


@dataclass
class Target:
    key: str
    title: str
    plan_doc: str
    entry: int
    allowed: list[tuple[int, int]]
    stubs: Callable[['Target'], dict]
    binary: Callable
    plan: Callable | None
    inputs: Callable[[random.Random, int], list]
    csharp: tuple[str, Callable, Callable] | None = None
    alternatives: dict[str, Callable] = field(default_factory=dict)  # extra reference readings, compared too
    csharp_variants: dict[str, tuple[Callable, Callable]] = field(default_factory=dict)  # other input mappings
    equal: Callable = staticmethod(lambda a, b: a == b)
    notes: list[str] = field(default_factory=list)
    log: list = field(default_factory=list)

    def sandbox(self, image: Image) -> Sandbox:
        self.log = []
        return Sandbox(image, self.allowed, self.stubs(self))


def run_binary(target: Target, sandbox: Sandbox, x):
    sandbox.reset()
    target.log.clear()
    try:
        return target.binary(sandbox, x, target.log)
    except Undefined:
        return UNDEFINED


# ============================================================================== (a) DET: world LCG, RAND
WORLD_RNG = 0x1DA708  # world + 0x1da708 (DET-plan §2.3)


def lcg_binary(sb: Sandbox, state: int, log) -> tuple[int, int]:
    world = sb.alloc(WORLD_RNG + 8)
    sb.write(world + WORLD_RNG, 'I', state)
    r = sb.call(0x105328, {3: world})
    return r.r3, sb.read(world + WORLD_RNG, 'I')


def lcg_plan(state: int) -> tuple[int, int]:
    new, returned = det_model.world_next(state)
    return returned, new


def lcg_inputs(rng: random.Random, n: int) -> list:
    preimage = ((0x80000000 - 1013904223) * pow(1664525, -1, 1 << 32)) & MASK32  # successor INT_MIN
    zero = ((0 - 1013904223) * pow(1664525, -1, 1 << 32)) & MASK32
    return [0, 1, 0x7FFFFFFF, 0x80000000, MASK32, preimage, zero] + [rng.getrandbits(32) for _ in range(n)]


def labs_stub(m: Machine) -> None:
    """C library labs on a 32-bit long; labs(LONG_MIN) keeps the bits (two's-complement wrap)."""
    m.gpr[3] = (-s32(m.gpr[3])) & MASK32 if s32(m.gpr[3]) < 0 else m.gpr[3]


def rand_binary(sb: Sandbox, x: tuple[int, int], log) -> tuple[int, int]:
    state, bound_word = x
    world = sb.alloc(WORLD_RNG + 8)
    sb.write(world + WORLD_RNG, 'I', state)
    holder = sb.struct(4, {0: ('I', world)})
    # Fragment of the RSE interpreter's RAND case: extsh bound, world RNG, labs(r >>> 1) % (bound + 1), labs.
    r = sb.call(0xB07F8, {3: bound_word, 27: holder}, stop=0xB0828)
    return r.r3, sb.read(world + WORLD_RNG, 'I')


def rand_plan(x: tuple[int, int]):
    state, bound_word = x
    new, returned = det_model.world_next(state)
    try:
        return det_model.rse_rand(returned, bound_word), new
    except ValueError:
        return UNDEFINED


def rand_inputs(rng: random.Random, n: int) -> list:
    edges = [(0, 10), (0, 0), (0, 1), (0, 0x7FFF), (0, 0x8000), (0, 0xFFFE), (0, 0xFFFF), (5, 0x12340005),
             (((0x80000000 - 1013904223) * pow(1664525, -1, 1 << 32)) & MASK32, 10)]
    out = [(rng.getrandbits(32), rng.choice([rng.randrange(0, 200), rng.getrandbits(16), rng.getrandbits(32)]))
           for _ in range(n)]
    return edges + out


# ============================================================================== (b) QUEUE: limit 0xdcb74
TYPE_RECORD_LOOKUP = 0x118018  # (manager, type id) -> type record; stubbed with a synthetic record
ASSERT = 0xB678                # (condition, message) debug assertion; logged, execution continues
QUEUE_COUNT = 0xDD118          # (ride) -> guests in queue; stubbed: returns the chosen count, records the limit


def queue_stubs(target: Target) -> dict:
    def lookup(m: Machine) -> None:
        target.log.append(('lookup', m.gpr[4] >> 16))  # the u16 type id travels in a by-value struct word
        m.gpr[3] = target.record

    def assertion(m: Machine) -> None:
        target.log.append(('assert', m.gpr[3]))

    def count(m: Machine) -> None:
        target.log.append(('limit', m.gpr[28]))  # 0xdccf4 moved the limit into r28 before this call
        m.gpr[3] = target.count

    return {TYPE_RECORD_LOOKUP: lookup, ASSERT: assertion, QUEUE_COUNT: count}


def queue_binary(sb: Sandbox, x: dict, log):
    level = x['level']
    target = QUEUE
    target.record = sb.struct(0x1C0 + 64 * 4)
    sb.write(target.record + 424 + 64 * level, 'i', x['init_speed'])
    sb.write(target.record + 436 + 64 * level, 'I', x['qwtc_bits'])
    ride = sb.struct(128, {14: ('H', 0x4321), 46: ('H', 0x8 if x['flag8'] else 0), 76: ('B', level),
                           84: ('I', x['speed'] & MASK32), 88: ('B', x['duration']), 89: ('B', x['capacity'])})
    target.count = x['count']
    r = sb.call(0xDCB74, {3: ride})
    limits = [value for kind, value in log if kind == 'limit']
    lookups = [value for kind, value in log if kind == 'lookup']
    if lookups and set(lookups) != {0x4321}:
        raise AssertionError('type-record lookup got the wrong type id')
    return {'limit': limits[0], 'full': r.r3 & 0xFF}


def queue_plan(x: dict):
    """QUEUE-plan §4 step 3: 100 when ride +46 & 0x8 (Info.HasQueue) is clear, else the QWTC formula as a u32;
    full = count ≥ limit (unsigned)."""
    if not x['flag8']:
        limit = 100
    else:
        speed = x['speed'] & MASK32
        g = 1.0 if speed == 0 else f32_div(f32(float(speed)), f32(float(x['init_speed'])))
        q = f32_div(f32(from_bits(x['qwtc_bits']) * f32(g * x['capacity'])), float(x['duration']))
        q = 4.0 if math.isnan(q) or q <= 4.0 else q
        limit = 0xFFFFFFFF if q >= 4294967296.0 else int(q)
    return {'limit': limit, 'full': int(x['count'] >= limit)}


def f32_div(a: float, b: float) -> float:
    if b == 0:
        return math.nan if a == 0 or math.isnan(a) else math.copysign(math.inf, a) * math.copysign(1.0, b)
    return f32(a / b)


def queue_csharp_request(x: dict) -> dict:
    # HasQueue <-> ride +46 & 0x8: the ride build 0xdab6c sets the bit from type record +64 (Info.HasQueue).
    return {'fn': 'queue_limit', 'has_queue': bool(x['flag8']), 'qwtc_bits': x['qwtc_bits'], 'speed': s32(x['speed']),
            'init_speed': x['init_speed'], 'capacity': x['capacity'], 'duration': x['duration']}


def queue_csharp_result(x: dict, limit):
    # MaximumQueueLength = min(4 × cells, limit); the binary's "full" test is count ≥ limit (unsigned).
    if isinstance(limit, dict):
        return limit
    return {'limit': limit & MASK32, 'full': int(x['count'] >= (limit & MASK32))}


def queue_inputs(rng: random.Random, n: int) -> list:
    def make(flag8=False, qwtc=None, speed=None, init=None, cap=None, dur=None, level=None, count=None):
        q = qwtc if qwtc is not None else rng.choice([random_float(rng, 0, 400), float(rng.randrange(0, 300)),
                                                      random_float(rng, -50, 50)])
        x = {'flag8': int(flag8), 'qwtc_bits': bits(q),
             'speed': speed if speed is not None else rng.choice([0, rng.randrange(1, 400)]),
             'init_speed': init if init is not None else rng.randrange(1, 400),
             'capacity': cap if cap is not None else rng.randrange(0, 256),
             'duration': dur if dur is not None else rng.randrange(1, 256),
             'level': level if level is not None else rng.randrange(0, 4)}
        x['count'] = count if count is not None else rng.randrange(0, 160)
        return x
    # The formula runs when ride +46 & 0x8 is set (bc 4,2 at 0xdcbac); the constant 100 when it is clear.
    formula = [make(True, qwtc=130.0, speed=0, cap=5, dur=30, count=21), make(True, qwtc=130.0, speed=0, cap=5, dur=30, count=20),
               make(True, qwtc=0.0, speed=0, cap=0, dur=1, count=4), make(True, qwtc=4.0, speed=0, cap=1, dur=1, count=3),
               make(True, qwtc=math.nan, speed=0, cap=1, dur=1, count=4), make(True, qwtc=1e9, speed=0, cap=255, dur=1, count=0),
               make(True, qwtc=3e9, speed=0, cap=1, dur=1, count=0), make(True, qwtc=130.0, speed=0, cap=5, dur=0, count=0),
               make(True, qwtc=0.0, speed=0, cap=5, dur=0, count=0), make(True, qwtc=130.0, speed=50, init=0, cap=5, dur=30),
               make(True, qwtc=130.0, speed=0x80000000, init=100, cap=5, dur=30),
               make(True, qwtc=-130.0, speed=0, cap=5, dur=30), make(True, qwtc=math.inf, speed=0, cap=5, dur=30)]
    edges = [make(False, count=99), make(False, count=100), *formula]
    return edges + [make(flag8=rng.random() < 0.5) for _ in range(n)]


# ============================================================================== (c) PATH: SetCellType pricing
CAN_CHANGE = (0x8408C, 0x84154)
TOC_PATH_COST, TOC_QUEUE_COST, TOC_FREE, TOC_GAME_STATIC, TOC_GAME_POINTER = -28060, -28064, -28280, -29664, -30080
TOC_LAST_CELL = -27980  # data:0x84b2c, PATH-plan's "last-cell flag"


def toc_pointer(sb: Sandbox, displacement: int) -> int:
    return sb.image.memory.u32(sb.image.toc + displacement)


def can_change_binary(sb: Sandbox, x: tuple[int, int, int], log) -> bool:
    old, new, flag = x
    sb.write(toc_pointer(sb, TOC_LAST_CELL), 'I', flag)
    cell = sb.struct(64, {8: ('i', old)})
    return bool(sb.call(0x8408C, {3: cell, 4: new}).r3)


def can_change_plan(x: tuple[int, int, int]) -> bool:
    """PATH-plan §3.1; rule 7 allows queue over path while the last-cell flag (data:0x84b2c) is 0."""
    old, new, flag = x
    if new == 0:
        return True
    if new == 4 and old == 4:
        return False
    if new == 21 and old == 21:
        return True
    if new == 1 and old == 3:
        return True
    if new == old or old == 0:
        return True
    if new == 4 and old == 1:
        return True
    return new == 3 and old == 1 and flag == 0


def can_change_inputs(rng: random.Random, n: int) -> list:
    grid = [(old, new, flag) for old in range(32) for new in range(32) for flag in (0, 1)]
    return grid + [(rng.randrange(256), rng.randrange(256), rng.choice([0, 1, rng.getrandbits(32)])) for _ in range(n)]


def set_cell_stubs(target: Target) -> dict:
    def record(name):
        def stub(m: Machine) -> None:
            target.log.append((name, m.gpr[3], m.gpr[4]))
        return stub

    def bank(m: Machine) -> None:
        m.gpr[3] = target.bank

    def spend(m: Machine) -> None:
        target.log.append(('spend', m.gpr[3], m.gpr[4]))

    return {
        0x859B4: record('clear-cell'),     # ClearCell(cell, 0, 0) before queue over path; no pricing effect
        0x108424: bank,                    # game -> bank record (+12 balance, +276 spend enabled)
        0xD7DC8: record('map-update'),     # map bookkeeping on game+728 after the type store
        0xD7DDC: record('map-update'),
        0xCBFDC: spend,                    # Spend(bank, amount): recorded instead of run (lowers +12 when +276 != 0)
        0xD8D20: record('owner-lookup'), ASSERT: record('assert'), 0xDD57C: record('queue-edited'),
    }


def set_cell_binary(sb: Sandbox, x: dict, log) -> dict:
    target = SET_CELL
    sb.write(toc_pointer(sb, TOC_PATH_COST), 'i', x['path_cost'])
    sb.write(toc_pointer(sb, TOC_QUEUE_COST), 'i', x['queue_cost'])
    sb.write(toc_pointer(sb, TOC_FREE), 'B', 0)
    sb.write(toc_pointer(sb, TOC_LAST_CELL), 'I', x['last_flag'])
    sb.write(toc_pointer(sb, TOC_GAME_STATIC) + 36, 'i', 0)  # economy on (game+36 == 0)
    game = sb.struct(0x1000)
    sb.write(toc_pointer(sb, TOC_GAME_POINTER), 'I', game)
    target.bank = sb.struct(0x200, {12: ('i', x['balance']), 276: ('i', 1)})
    cell = sb.struct(64, {8: ('i', x['old']), 16: ('H', 1), 32: ('h', 5)})
    r = sb.call(0x82AC4, {3: cell, 4: x['type']})
    spent = [amount for kind, _, amount in log if kind == 'spend']
    return {'ok': bool(r.r3), 'charged': sum(s32(a) for a in spent), 'bumped': sb.read(cell + 32, 'h') == 6}


def set_cell_plan(x: dict) -> dict:
    """PATH-plan §3.2 with the free byte clear, game+36 == 0 and bank +276 != 0; the money test is 32-bit."""
    old, new = x['old'], x['type']
    if not can_change_plan((old, new, x['last_flag'])):
        return {'ok': False, 'charged': 0, 'bumped': False}
    if new != 0 and new == old:
        return {'ok': True, 'charged': 0, 'bumped': True}
    cost = x['path_cost'] if new == 1 else x['queue_cost'] if new == 3 else 0
    if new in (1, 3) and s32(x['balance'] - cost) < 0:
        return {'ok': False, 'charged': 0, 'bumped': False}
    return {'ok': True, 'charged': cost if new in (1, 3) else 0, 'bumped': False}


def set_cell_request(x: dict) -> dict:
    return {'fn': 'set_cell_type', 'old': x['old'], 'type': x['type'], 'last_cell': x['last_flag'] != 0,
            'balance': x['balance'], 'path_cost': x['path_cost'], 'queue_cost': x['queue_cost']}


def set_cell_inputs(rng: random.Random, n: int) -> list:
    def make(**kw):
        x = {'old': rng.choice([0, 0, 1, 1, 3, 3, 4, 9, 21, rng.randrange(32)]), 'type': rng.choice([1, 3]),
             'last_flag': rng.choice([0, 1]), 'balance': rng.choice([rng.randrange(-500, 5000), rng.randrange(0, 200)]),
             'path_cost': rng.choice([20, 25, rng.randrange(0, 500)]), 'queue_cost': rng.choice([75, rng.randrange(0, 500)])}
        x.update(kw)
        while not set_cell_domain(x):  # keep the pricing domain at full size (see set_cell_domain)
            x['old'] = rng.choice([0, 3, 4, 9, 21, rng.randrange(32)])
        return x
    edges = [make(old=0, type=1, balance=20, path_cost=20), make(old=0, type=1, balance=19, path_cost=20),
             make(old=0, type=3, balance=75, queue_cost=75), make(old=0, type=3, balance=74, queue_cost=75),
             make(old=1, type=1, balance=0), make(old=3, type=3, balance=-10), make(old=3, type=1, balance=20, path_cost=20),
             make(old=0, type=1, balance=-0x80000000, path_cost=1), make(old=0, type=1, balance=0x7FFFFFFF, path_cost=-1),
             make(old=0, type=1, balance=0, path_cost=0), make(old=4, type=1, balance=1000)]
    return edges + [make() for _ in range(n)]


def set_cell_domain(x: dict) -> bool:
    """Pricing domain: excludes queue over path (1 -> 3), whose rule is CAN_CHANGE's to report."""
    return not (x['old'] == 1 and x['type'] == 3)


# ============================================================================== (d) BOARD: UNBOUNCE 0xae020
CLOCK_NOW = 0x10E844  # scheduler clock read (ms); stubbed to the chosen `now`


def unbounce_stubs(target: Target) -> dict:
    def now(m: Machine) -> None:
        m.gpr[3] = target.now & MASK32
    return {CLOCK_NOW: now}


def unbounce_binary(sb: Sandbox, x: dict, log) -> dict:
    UNBOUNCE.now = x['now']
    slots = x['slots']
    table = sb.alloc(16 * max(1, len(slots)))
    for index, (visitor, deadline, start) in enumerate(slots):
        sb.write(table + 16 * index, 'I', visitor)
        sb.write(table + 16 * index + 8, 'i', deadline)
        sb.write(table + 16 * index + 12, 'i', start)
    occupied = sum(1 for visitor, _, _ in slots if visitor)
    script = sb.struct(128, {40: ('I', table), 100: ('i', len(slots)), 108: ('h', occupied)})
    r = sb.call(0xAE020, {3: script})
    return {'guest': r.r3, 'remaining': sb.read(script + 108, 'h')}


def c_div(a: int, b: int) -> int:
    q = abs(a) // abs(b)
    return q if (a < 0) == (b < 0) else -q


def unbounce_plan(x: dict) -> dict:
    """BOARD-plan §5: first slot (index order) with deadline < now and trunc(((now − start) mod 1000) / 200) == 0."""
    now = x['now']
    occupied = sum(1 for visitor, _, _ in x['slots'] if visitor)
    for visitor, deadline, start in x['slots']:
        if not visitor or not deadline < now:
            continue
        elapsed = s32(now - start)
        if c_div(elapsed - c_div(elapsed, 1000) * 1000, 200) == 0:
            return {'guest': visitor, 'remaining': occupied - 1}
    return {'guest': 0, 'remaining': occupied}


def unbounce_request(x: dict) -> dict:
    return {'fn': 'unbounce', 'now': x['now'],
            'slots': [[visitor, deadline, start] for visitor, deadline, start in x['slots']]}


def unbounce_inputs(rng: random.Random, n: int) -> list:
    def make(now=None, count=None):
        now = now if now is not None else rng.randrange(0, 10_000_000)
        slots = []
        for _ in range(count if count is not None else rng.randrange(1, 9)):
            if rng.random() < 0.25:
                slots.append((0, 0, 0))
                continue
            start = now - rng.randrange(0, 70_000)
            slots.append((rng.randrange(1, 0x10000), start + 1000 * rng.randrange(1, 61), start))
        return {'now': now, 'slots': slots}
    edges = [{'now': 31000, 'slots': [(7, 31000, 1000)]}, {'now': 31001, 'slots': [(7, 31000, 1000)]},
             {'now': 31200, 'slots': [(7, 31000, 1000)]}, {'now': 31199, 'slots': [(7, 31000, 1000)]},
             {'now': 50, 'slots': [(0, 0, 0), (9, 40, 49), (8, 10, 50)]}, {'now': 0, 'slots': [(5, -1, -200)]},
             # start > now: the C remainder keeps the sign, so elapsed −150 is inside the window and −250 is not.
             {'now': 1000, 'slots': [(5, 900, 1150)]}, {'now': 1000, 'slots': [(5, 900, 1250)]}]
    return edges + [make() for _ in range(n)]


# ============================================================================== (d2) BOARD: BOUNCE 0xadf40
def bounce_binary(sb: Sandbox, x: dict, log) -> dict:
    BOUNCE.now = x['now']
    slots = x['slots']
    table = sb.alloc(16 * max(1, len(slots)))
    for index, (visitor, deadline, start) in enumerate(slots):
        sb.write(table + 16 * index, 'I', visitor)
        sb.write(table + 16 * index + 8, 'i', deadline)
        sb.write(table + 16 * index + 12, 'i', start)
    occupied = sum(1 for visitor, _, _ in slots if visitor)
    script = sb.struct(128, {40: ('I', table), 100: ('i', len(slots)), 108: ('h', occupied), 112: ('i', 0)})
    r = sb.call(0xADF40, {3: script, 4: x['visitor'], 5: x['dur']})
    after = [[sb.read(table + 16 * index, 'I'), sb.read(table + 16 * index + 8, 'i'), sb.read(table + 16 * index + 12, 'i')]
             for index in range(len(slots))]
    return {'ok': r.r3, 'count': sb.read(script + 108, 'h'), 'slots': after}


def bounce_plan(x: dict) -> dict:
    """BOARD-plan §5: the first free slot (visitor 0) gets visitor, deadline now + 1000·dur and start now; a full
    table stores nothing and returns 0."""
    slots = [list(slot) for slot in x['slots']]
    occupied = sum(1 for visitor, _, _ in slots if visitor)
    for slot in slots:
        if not slot[0]:
            slot[:] = [x['visitor'], s32(x['now'] + 1000 * x['dur']), x['now']]
            return {'ok': 1, 'count': occupied + 1, 'slots': slots}
    return {'ok': 0, 'count': occupied, 'slots': slots}


def bounce_request(x: dict) -> dict:
    return {'fn': 'bounce', 'now': x['now'], 'visitor': x['visitor'], 'dur': x['dur'],
            'slots': [[visitor, deadline, start] for visitor, deadline, start in x['slots']]}


def bounce_inputs(rng: random.Random, n: int) -> list:
    def make():
        now = rng.randrange(0, 10_000_000)
        slots = []
        for _ in range(rng.randrange(0, 9)):
            if rng.random() < 0.4:
                slots.append((0, 0, 0))
            else:
                start = now - rng.randrange(0, 70_000)
                slots.append((rng.randrange(1, 0x10000), start + 1000 * rng.randrange(1, 61), start))
        return {'now': now, 'visitor': rng.randrange(1, 0x10000), 'dur': rng.randrange(1, 61), 'slots': slots}
    edges = [{'now': 500, 'visitor': 9, 'dur': 30, 'slots': []},
             {'now': 500, 'visitor': 9, 'dur': 30, 'slots': [(4, 900, 100), (0, 0, 0), (5, 900, 100)]},
             {'now': 500, 'visitor': 9, 'dur': 30, 'slots': [(4, 900, 100), (5, 900, 100), (0, 0, 0)]},
             {'now': 500, 'visitor': 9, 'dur': 30, 'slots': [(4, 900, 100), (5, 900, 100), (6, 900, 100)]},
             {'now': 500, 'visitor': 9, 'dur': 30, 'slots': [(0, 0, 0), (0, 0, 0)]}]
    return edges + [make() for _ in range(n)]


# ============================================================================== (e) WALK: setter, smoothing
def speed_setter_binary(sb: Sandbox, s: float, log) -> dict:
    mover = sb.struct(64)
    sb.call(0xFFE38, {3: mover}, {1: s})
    return {'force': sb.read(mover + 24, 'i'), 'speed': sb.read(mover + 28, 'i')}


def fctiwz(value: float) -> int:
    if math.isnan(value):
        return -0x80000000
    if value >= 2147483648.0:
        return 0x7FFFFFFF
    if value <= -2147483649.0:
        return -0x80000000
    return int(value)


def speed_setter_plan(s: float) -> dict:
    """WALK-plan §1: +28 = max(655, trunc(min(s, 2)·0.2·65536)), +24 = the same with 0.4 (double products)."""
    capped = 2.0 if s > 2.0 else s
    return {'force': max(655, fctiwz(capped * 0.4 * 65536.0)), 'speed': max(655, fctiwz(capped * 0.2 * 65536.0))}


def speed_setter_inputs(rng: random.Random, n: int) -> list:
    edges = [0.0, -0.0, 0.5, 0.6, 0.0249, 0.02499, 0.025, 1.0, 2.0, f32(2.0000002), 3.0, -1.0, 1e30, -1e30,
             math.inf, -math.inf, math.nan, f32(0.024993896484375)]
    return [f32(e) for e in edges] + [random_float(rng, -0.5, 3.0) for _ in range(n)]


def smoothing_stubs(target: Target) -> dict:
    def record(name):
        def stub(m: Machine) -> None:
            target.log.append((name,))
        return stub
    # Movement updates after the speed is set (0xe6bdc, 0xe6be4): not part of the formula, no return value used.
    return {0xFFD4C: record('mover-update'), 0xD2394: record('position-update')}


def smoothing_binary(sb: Sandbox, x: dict, log) -> dict:
    guest = sb.struct(512, {192: ('H', x['base']), 194: ('H', x['bonus']), 196: ('H', x['boost']), 200: ('f', x['s'])})
    sb.call(0xE6B28, {3: guest})
    return {'s': sb.read(guest + 200, 'f'), 'boost': sb.read(guest + 196, 'H'),
            'force': sb.read(guest + 212 + 24, 'i'), 'speed': sb.read(guest + 212 + 28, 'i')}


def smoothing_plan(x: dict) -> dict:
    """WALK-plan §4 as a straightforward single-precision port: (3·s + sum/100)·0.25, boost = trunc(boost·99/100)."""
    total = x['base'] + x['bonus'] + x['boost']
    s = f32(f32(f32(3.0 * x['s']) + f32(f32(float(total)) / 100.0)) * 0.25)
    return {'s': s, 'boost': (x['boost'] * 99 // 100) & 0xFFFF, **speed_setter_plan(s)}


def smoothing_fused(x: dict) -> dict:
    """The same with the multiply-add fused (one rounding of 3·s + sum/100), as `fmadds` does."""
    total = x['base'] + x['bonus'] + x['boost']
    from fractions import Fraction
    from ppc32 import BINARY32, round_rational
    exact = Fraction(3) * Fraction(x['s']) + Fraction(f32(f32(float(total)) / 100.0))
    inner = round_rational(exact, BINARY32) if exact != 0 else 0.0
    s = f32(inner * 0.25)
    return {'s': s, 'boost': (x['boost'] * 99 // 100) & 0xFFFF, **speed_setter_plan(s)}


def smoothing_equal(a, b) -> bool:
    if a == UNDEFINED or b == UNDEFINED:
        return a == b
    return same_float(a['s'], b['s']) and all(a[k] == b[k] for k in ('boost', 'force', 'speed'))


def smoothing_inputs(rng: random.Random, n: int) -> list:
    def make(**kw):
        x = {'s': rng.choice([random_float(rng, 0.0, 2.0), random_float(rng, 0.5, 1.5)]),
             'base': rng.choice([60, 80, 100, 120, 140]), 'bonus': rng.choice([0, 25]),
             'boost': rng.choice([0, 0, rng.randrange(0, 200), rng.randrange(0, 661)])}
        x.update(kw)
        return x
    edges = [make(s=0.0, base=60, bonus=0, boost=0), make(s=2.0, base=140, bonus=25, boost=0), make(boost=661),
             make(boost=662), make(boost=0xFFFF), make(s=f32(1e-30)), make(s=10.0)]
    return edges + [make() for _ in range(n)]


def speed_setter_request(s: float) -> dict:
    return {'fn': 'guest_speed_limits', 's_bits': bits(s)}


def smoothing_request(x: dict) -> dict:
    return {'fn': 'guest_speed_smoothing', 's_bits': bits(x['s']), 'base': x['base'], 'bonus': x['bonus'], 'boost': x['boost']}


def smoothing_result(x: dict, r: dict) -> dict:
    # The bridge returns the single as its bits, so no decimal round trip intervenes.
    if 's_bits' not in r:
        return r
    return {'s': from_bits(r['s_bits']), 'boost': r['boost'], 'force': r['force'], 'speed': r['speed']}


# ============================================================================== (f) COAST: rating step, final
TOC = {'S': -29460, 'E': -29464, 'prev_bank': -29476, 'prev_force': -29472, 'peak_s': -29484, 'peak_e': -29480,
       'e_prev': -29496, 'normal': -29492, 'terror': -29488}
# Definition offsets as the step reads them (0x415b4): which operand each field multiplies fixes its name.
DEF_FIELDS = {'gforce_sick': 812, 'upside_sick': 816, 'speed': 820, 'gforce_excite': 824, 'bank_excite': 832,
              'xg_uncomf': 836, 'inverted_uncomf': 840, 'normal_start': 844, 'terror_start': 848,
              'normal_sustain': 852, 'terror_sustain': 856}


def coast_step_binary(sb: Sandbox, x: dict, log) -> dict:
    for name in ('S', 'E', 'peak_s', 'peak_e', 'e_prev'):
        sb.write(toc_pointer(sb, TOC[name]), 'f', x[name])
    # Previous bank and force stay 0, as in the game (only the reset 0x4154c/0x41568 and the step read them), so the
    # reference's |F|1 and |bank| equal the step's |dF|1 and |dbank| (REVIEW-ORACLE OV-7).
    sb.write(toc_pointer(sb, TOC['prev_bank']), 'f', 0.0)
    for k in range(3):
        sb.write(toc_pointer(sb, TOC['prev_force']) + 4 * k, 'f', 0.0)
    sb.write(toc_pointer(sb, TOC['normal']), 'i', x['normal'])
    sb.write(toc_pointer(sb, TOC['terror']), 'i', x['terror'])
    definition = sb.struct(0x380, {offset: ('f', x['def'][name]) for name, offset in DEF_FIELDS.items()})
    samples = sb.alloc(56 * (x['i'] + 1))
    sb.write(samples + 56 * x['i'], 'I', x['flags'])
    controller = sb.struct(0x200, {4: ('I', definition), 156: ('I', samples)})
    force = sb.struct(12, {0: ('f', x['F'][0]), 4: ('f', x['F'][1]), 8: ('f', x['F'][2])})
    # 0x3ffa8 calls with f0 = sample+40 (Fz) still live: model the call site, not a cleared register.
    sb.call(0x415B4, {3: controller, 4: x['i'], 8: force}, {1: x['v'], 2: x['bank'], 0: x['f0']})
    out = {name: sb.read(toc_pointer(sb, TOC[name]), 'f') for name in ('S', 'E', 'peak_s', 'peak_e', 'e_prev')}
    out.update(flags=sb.read(samples + 56 * x['i'], 'I'), normal=sb.read(toc_pointer(sb, TOC['normal']), 'i'),
               terror=sb.read(toc_pointer(sb, TOC['terror']), 'i'))
    return out


def coast_step_plan(x: dict, stale: float = 0.0) -> dict:
    """COAST-plan §6.3 in single precision; the stale-f0 term is `stale` (COAST-I uses 0)."""
    d = x['def']
    fx, fy, fz = (abs(c) for c in x['F'])
    norm1 = f32(f32(fx + fy) + fz)
    bank = abs(x['bank'])
    big_u = max(f32(bank - f32(math.pi / 2)), 0.0)
    u = f32(big_u + (1.0 if big_u != 0 else 0.0))
    ds = f32(f32(d['gforce_sick'] * norm1) + f32(d['upside_sick'] * u))
    de = f32(f32(f32(d['speed'] * x['v']) + f32(d['gforce_excite'] * norm1)) + f32(d['bank_excite'] * bank))
    if u != 0 and x['F'][1] < f32(0.1):
        inverted = f32(u * d['inverted_uncomf'])
        if x['F'][1] < 0:
            inverted = f32(inverted * 3.0)
    else:
        inverted = stale
    du = f32(f32(d['xg_uncomf'] * fx) + inverted)
    s = f32(0.5 * x['S'] + ds)
    e = f32(0.5 * x['E'] + max(f32(de - du), 0.0))
    out = {'S': s, 'E': e, 'peak_s': max(x['peak_s'], s), 'peak_e': max(x['peak_e'], e), 'e_prev': e}
    flags, normal, terror = x['flags'] & ~7 & MASK32, x['normal'], x['terror']
    if x['i'] > 2:
        rise = f32(e - x['e_prev'])
        if rise > d['normal_start']:
            normal, flags = 1, flags | 4  # the binary stores 1 (li), a "screaming" state, not a count
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
    out.update(flags=flags, normal=normal, terror=terror)
    return out


def coast_step_plan_fz(x: dict) -> dict:
    return coast_step_plan(x, stale=x['f0'])


def coast_equal(a, b) -> bool:
    if a == UNDEFINED or b == UNDEFINED:
        return a == b
    return all(same_float(a[k], b[k]) if isinstance(a[k], float) else a[k] == b[k] for k in a)


def coast_step_inputs(rng: random.Random, n: int) -> list:
    def make(**kw):
        d = {name: random_float(rng, 0.0, 2.0) for name in DEF_FIELDS}
        d.update(normal_start=random_float(rng, 0, 10), terror_start=random_float(rng, 5, 20),
                 normal_sustain=random_float(rng, 0, 50), terror_sustain=random_float(rng, 20, 80))
        force = [random_float(rng, -3, 3) for _ in range(3)]
        x = {'def': d, 'F': force, 'f0': force[2], 'v': random_float(rng, 0.5, 30), 'i': rng.randrange(1, 40),
             'bank': rng.choice([random_float(rng, -0.6, 0.6), random_float(rng, -math.pi, math.pi)]),
             'S': random_float(rng, 0, 60), 'E': random_float(rng, 0, 60), 'peak_s': random_float(rng, 0, 80),
             'peak_e': random_float(rng, 0, 80), 'e_prev': random_float(rng, 0, 60),
             'normal': rng.choice([0, 0, rng.randrange(1, 5)]), 'terror': rng.choice([0, 0, rng.randrange(1, 5)]),
             'flags': rng.getrandbits(32)}
        x.update(kw)
        return x
    edges = [make(bank=f32(math.pi / 2)), make(bank=f32(-math.pi)), make(F=[0.0, f32(0.05), 0.0], f0=0.0, bank=3.0),
             make(F=[0.0, f32(-0.05), 0.0], f0=0.0, bank=3.0), make(F=[0.0, f32(0.1), 0.0], f0=0.0, bank=3.0),
             make(F=[0.0, 0.0, 0.0], f0=0.0), make(i=1), make(i=2), make(i=3)]
    return edges + [make() for _ in range(n)]


def coast_final_binary(sb: Sandbox, x: dict, log) -> dict:
    for name in ('S', 'E', 'peak_s', 'peak_e'):
        sb.write(toc_pointer(sb, TOC[name]), 'f', x[name])
    head = 0
    for flags in reversed(x['sections']):
        head = sb.struct(104, {8: ('I', flags), 100: ('I', head)})
    controller = sb.struct(0x200, {84: ('I', head), 168: ('f', x['length_a']), 172: ('f', x['length_b'])})
    sb.call(0x41900, {3: controller}, {1: x['max_y'], 2: x['max_speed']})
    out = {name: sb.read(controller + offset, 'f') for name, offset in
           (('sickness', 256), ('excitement', 260), ('length', 268), ('max_y', 272), ('max_speed', 276))}
    out['sections'] = sb.read(controller + 264, 'i')
    return out


def clamp100(value: float) -> float:
    return 100.0 if value > 100.0 else 0.0 if value < 0.0 else value


def coast_final_plan(x: dict) -> dict:
    """COAST-plan §6.4; sections = count of sections without flags 0x40 and 0x80."""
    return {'sickness': clamp100(f32(x['S'] + x['peak_s'])),
            'excitement': clamp100(f32(0.5 * x['E'] + f32(f32(1.7) * x['peak_e']))),
            'length': f32(x['length_a'] * x['length_b']), 'max_y': x['max_y'], 'max_speed': x['max_speed'],
            'sections': sum(1 for flags in x['sections'] if not flags & 0xC0)}


def coast_final_both_flags(x: dict) -> dict:
    """COAST-plan §6.4 with the section count read as the branches at 0x419d0..0x419dc do: flags 0x40 and 0x80 both set."""
    return {**coast_final_plan(x), 'sections': sum(1 for flags in x['sections'] if flags & 0xC0 == 0xC0)}


def coast_final_inputs(rng: random.Random, n: int) -> list:
    def make(**kw):
        x = {name: random_float(rng, -20, 160) for name in ('S', 'E', 'peak_s', 'peak_e')}
        x.update(length_a=random_float(rng, 0, 50), length_b=random_float(rng, 0, 50), max_y=random_float(rng, 0, 30),
                 max_speed=random_float(rng, 0, 40),
                 sections=[rng.choice([0, 0x40, 0x80, 0xC0, rng.getrandbits(16)]) for _ in range(rng.randrange(0, 6))])
        x.update(kw)
        return x
    edges = [make(S=50.0, peak_s=50.0), make(S=50.0, peak_s=50.0000038), make(S=-1.0, peak_s=0.5),
             make(E=0.0, peak_e=0.0), make(sections=[0, 0x40, 0x80, 0xC0]), make(sections=[])]
    return edges + [make() for _ in range(n)]


# ============================================================================== (h) ARRIVAL: group size 0xcb4e8
ARRIVAL_GAME_POINTER = -30124  # TOC slot -> data word holding the game pointer
ARRIVAL_MODE_OBJECT = -30136   # TOC slot -> static object; its word 0 is the mode (1: the 500-guest limit)
ARRIVAL_MODE_GUARD = -30132    # TOC slot -> its construction guard byte (nonzero once 0x12bb64 has run)
ARRIVAL_SETTINGS = -30084      # TOC slot -> settings block: +720 MinPeople, +732 NewParkBonus, +736 PointsPerVisitor
ARRIVAL_WORLD_MODE = 0x1E0000 - 22728  # game + 0x1da738: 4 = bankrupt (no group)
PARK_OF_GAME = 0x1082B4        # (game) -> park; stubbed with a sentinel, checked
COUNTED_GUESTS = 0xC1FB4       # (park, 20) -> guests counted against the limit; stubbed: the chosen count
PARK_POINTS = 0xC24C0          # (park) -> attraction points; stubbed: the chosen points
FACTOR_OBJECT = 0x1083C8       # (game) -> the weather thing (game +0x1da724) whose +48 word, mCurrentDrops, picks the factor; stubbed with a synthetic object
ARRIVAL_STATE = type('ArrivalState', (), {})()  # set per input by arrival_binary, read by the stubs
DEBUG_LOG = 0x5FBCC            # (level, format, ...) debug print; logged, no effect on the result


def arrival_stubs(target: Target) -> dict:
    state = ARRIVAL_STATE  # arrival_binary's memory set-up; shared with copies of the target (mutation tests)

    def park(m: Machine) -> None:
        if m.gpr[3] != state.game:
            raise AssertionError('0x1082b4 called without the game pointer')
        m.gpr[3] = state.park

    def counted(m: Machine) -> None:
        if m.gpr[3] != state.park or m.gpr[4] != 20:
            raise AssertionError('0xc1fb4 called with unexpected arguments')
        m.gpr[3] = state.guests & MASK32

    def points(m: Machine) -> None:
        if m.gpr[3] != state.park:
            raise AssertionError('0xc24c0 called without the park')
        m.gpr[3] = state.points & MASK32

    def factor_object(m: Machine) -> None:
        if m.gpr[3] != state.game:
            raise AssertionError('0x1083c8 called without the game pointer')
        m.gpr[3] = state.factor_object

    def log(m: Machine) -> None:
        target.log.append(('log', m.gpr[3], m.gpr[5]))

    return {PARK_OF_GAME: park, COUNTED_GUESTS: counted, PARK_POINTS: points, FACTOR_OBJECT: factor_object,
            DEBUG_LOG: log}


def arrival_setup(sb: Sandbox, x: dict, park_size: int = 16) -> None:
    state = ARRIVAL_STATE
    state.game = sb.alloc(ARRIVAL_WORLD_MODE + 8)
    sb.write(state.game + ARRIVAL_WORLD_MODE, 'i', x['world_mode'])
    sb.write(toc_pointer(sb, ARRIVAL_GAME_POINTER), 'I', state.game)
    state.park = sb.struct(park_size)
    state.factor_object = sb.struct(64, {48: ('i', x['obj48'])})
    state.guests, state.points = x['guests'], x['points']
    settings = toc_pointer(sb, ARRIVAL_SETTINGS)
    sb.write(settings + 720, 'i', x['min_people'])
    sb.write(settings + 732, 'i', x['bonus'])
    sb.write(settings + 736, 'i', x['per_visitor'])
    sb.write(toc_pointer(sb, ARRIVAL_MODE_OBJECT), 'i', x['limit_mode'])
    sb.write(toc_pointer(sb, ARRIVAL_MODE_GUARD), 'B', 1)  # constructed: 0x12bb64 is not in `allowed` and must not run


def arrival_binary(sb: Sandbox, x: dict, log) -> int:
    arrival_setup(sb, x)
    return s32(sb.call(0xCB4E8).r3)


def fctiwz32(value: float) -> int:
    if math.isnan(value):
        return -0x80000000
    return max(-0x80000000, min(0x7FFFFFFF, math.trunc(value)))


def arrival_core(x: dict, factor: float) -> int:
    per_visitor = x['per_visitor'] if x['per_visitor'] >= 2 else 1
    total = s32(x['points'] + x['bonus'])
    product = f32(f32(factor) * f32(float(total)))
    quotient = int(fctiwz32(product) / per_visitor)  # C / divw: toward zero
    return max(quotient, x['min_people'])


def arrival_plan(x: dict) -> int:
    """GUESTS.md "Arrivals" and GuestSimulation.ArrivalGroupSize: none while bankrupt; (int)(factor × (points +
    NewParkBonus)) / PointsPerVisitor (1 below 2) with factor 0.8 while the weather thing's +48 word (mCurrentDrops) is
    positive (drops are falling) and 1.2 otherwise, at least MinPeople, cut so guests + group stay within 1500, or 500 for an
    online visit (GameType word 1), never below 0."""
    if x['world_mode'] == 4:
        return 0
    count = arrival_core(x, 0.8 if x['obj48'] > 0 else 1.2)
    limit = 500 if x['limit_mode'] == 1 else 1500
    if x['guests'] + count > limit:
        count = max(0, limit - x['guests'])
    return count


def arrival_full(x: dict) -> int:
    """The routine as read from 0xcb4e8..0xcb694: the factor is 0.8 when the 0x1083c8 object's +48 word is positive,
    else 1.2; with the mode word == 1 the group is first cut to 500 − guests; both cuts compare guests + group
    unsigned and can go negative."""
    if x['world_mode'] == 4:
        return 0
    count = arrival_core(x, 0.8 if x['obj48'] > 0 else 1.2)
    guests = x['guests']
    if x['limit_mode'] == 1 and (guests + count) & MASK32 > 500:
        count = s32(500 - guests)
    if x['limit_mode'] != 1 and (guests + count) & MASK32 > 1500:
        count = s32(1500 - guests)
    return count


def arrival_csharp_request(x: dict) -> dict:
    return {'fn': 'arrival_group', **{k: x[k] for k in ('world_mode', 'guests', 'points', 'bonus', 'per_visitor',
                                                         'min_people', 'obj48', 'limit_mode')}}


def arrival_inputs(rng: random.Random, n: int) -> list:
    def make(**kw):
        x = {'world_mode': rng.choice([0, 1, 2, 3, 4, 5]), 'guests': rng.choice([0, rng.randrange(0, 1600)]),
             'points': rng.choice([0, rng.randrange(0, 2000), rng.randrange(-500, 0)]),
             'bonus': rng.choice([20, rng.randrange(0, 200)]), 'per_visitor': rng.choice([6, rng.randrange(-2, 30)]),
             'min_people': rng.choice([1, 2, rng.randrange(0, 40)]), 'obj48': rng.choice([0, 0, 1, rng.randrange(-5, 5)]),
             'limit_mode': rng.choice([0, 0, 0, 1, 2])}
        x.update(kw)
        return x
    shipped = dict(world_mode=0, guests=0, points=0, bonus=20, per_visitor=6, min_people=1, obj48=0, limit_mode=0)
    edges = [make(**shipped), make(**{**shipped, 'world_mode': 4}), make(**{**shipped, 'obj48': 1}),
             make(**{**shipped, 'limit_mode': 1, 'guests': 499}), make(**{**shipped, 'limit_mode': 1, 'guests': 600}),
             make(**{**shipped, 'guests': 1499}), make(**{**shipped, 'guests': 1500}), make(**{**shipped, 'guests': 1700}),
             make(**{**shipped, 'points': 0x7FFFFFFF}), make(**{**shipped, 'points': -0x80000000, 'bonus': -1}),
             make(**{**shipped, 'points': 16777217}), make(**{**shipped, 'per_visitor': 1}),
             make(**{**shipped, 'per_visitor': -7}), make(**{**shipped, 'min_people': -5, 'bonus': -100}),
             make(**{**shipped, 'points': 25, 'bonus': 20})]
    return edges + [make() for _ in range(n)]


# ============================================================================== (j) ARRIVAL: update 0xcb2ac
ARRIVAL_TURN = 0x1E0000 - 22772        # game + 0x1da70c: the world turn, read by the timer routines 0x1210f8 / 0x121098
ARRIVAL_PARK_SIZE = 0x20000 + 8192     # the park record: +0x20000+3264 guests created, +0x20000+5848 group history
ARRIVAL_CREATED = 0x20000 + 3264       # park + 0x20cc0: raised by one for every guest 0xcb2ac creates
ARRIVAL_CONSTRUCTOR = 0xCB028          # (arrival object): timer, group, active and the two other state bytes/words
ARRIVAL_PHASE = 0x109784               # (game) -> state of the arrival vehicle: 2 = at the stop, -1 / 6 = on its way
ARRIVAL_EVENTS = 0x109B78              # (game) -> nonzero when the vehicle script reports the arrival state
ARRIVAL_VEHICLE = 0x109368             # (buffer, game, size) arrival vehicle script step; the buffer is r1 + a frame slot
ARRIVAL_CREATE = 0xCB698               # (arrival object) creates one guest, returns 1
ARRIVAL_HISTORY = 0xCB750              # (park + 0x20000 + 5848, group) ring buffer of the group sizes
EVENT_ASSERT, EVENT_HISTORY = 1000, 2000  # codes in the event lists next to the vehicle script's frame slots 72..82


def arrival_update_stubs(target: Target) -> dict:
    state = ARRIVAL_STATE
    stubs = arrival_stubs(target)

    def phase(m: Machine) -> None:
        if m.gpr[3] != state.game:
            raise AssertionError('0x109784 called without the game pointer')
        m.gpr[3] = state.phase & MASK32

    def reports(m: Machine) -> None:
        if m.gpr[3] != state.game:
            raise AssertionError('0x109b78 called without the game pointer')
        m.gpr[3] = state.flag

    def vehicle(m: Machine) -> None:
        if m.gpr[4] != state.game:
            raise AssertionError('0x109368 called without the game pointer')
        target.log.append(('event', (m.gpr[3] - m.gpr[1]) & MASK32, m.gpr[5]))

    def create(m: Machine) -> None:
        state.guests += 1
        state.created += 1
        m.gpr[3] = 1

    def history(m: Machine) -> None:
        if m.gpr[3] != state.park + ARRIVAL_CREATED + 5848 - 3264:
            raise AssertionError('0xcb750 called without the park history')
        target.log.append(('history', s32(m.gpr[4])))

    def assertion(m: Machine) -> None:
        target.log.append(('assert', m.gpr[3]))

    stubs.update({ARRIVAL_PHASE: phase, ARRIVAL_EVENTS: reports, ARRIVAL_VEHICLE: vehicle, ARRIVAL_CREATE: create,
                  ARRIVAL_HISTORY: history, ASSERT: assertion})
    return stubs


def arrival_update_binary(sb: Sandbox, x: dict, log) -> dict:
    """Construct the arrival object (0xcb028, executed), set the saved state if given, then call 0xcb2ac once per step in
    the same sandbox. Per call: (active, timer start, guests created so far), the group word and the events."""
    state = ARRIVAL_STATE
    arrival_setup(sb, x, ARRIVAL_PARK_SIZE)
    sb.write(toc_pointer(sb, ARRIVAL_SETTINGS) + 724, 'I', x['interval'])
    obj = sb.struct(32)
    sb.call(ARRIVAL_CONSTRUCTOR, {3: obj})
    sb.write(obj + 4, 'I', x['timer0'])
    if x['preset'] is not None:
        sb.write(obj + 12, 'i', x['preset'][0])
        sb.write(obj + 16, 'B', x['preset'][1])
    state.created = 0
    calls, groups, events = [], [], []
    for now, phase, flag in x['steps']:
        sb.write(state.game + ARRIVAL_TURN, 'I', now)
        state.phase, state.flag = phase, flag
        mark = len(log)
        sb.call(0xCB2AC, {3: obj})
        created = sb.read(state.park + ARRIVAL_CREATED, 'i')
        if created != state.created:
            raise AssertionError('the created-guest counter disagrees with the creation calls')
        calls.append((sb.read(obj + 16, 'B'), sb.read(obj + 4, 'I'), created))
        groups.append(sb.read(obj + 12, 'i'))
        codes = {'event': lambda e: (e[1], s32(e[2])), 'assert': lambda e: (EVENT_ASSERT, e[1]),
                 'history': lambda e: (EVENT_HISTORY, e[1])}
        events.append([codes[e[0]](e) for e in log[mark:] if e[0] in codes])
    return {'calls': calls, 'groups': groups, 'events': events}


def arrival_update_reading(x: dict, hold_negative: bool = False, floor: bool = False) -> dict:
    """0xcb2ac as read from the listing. Per call: with no active group and (turn >> 2) − (start >> 2) above the
    interval (unsigned), the group becomes the 0xcb4e8 size at the current count and starts. With an active group:
    phase 2 creates a guest while the group is above 0 (signed) and otherwise restarts the timer, clears the active
    byte and starts the vehicle with size 0; phase −1 or 6 starts the vehicle with size 1/2/3 (group up to 35, 60,
    more; phase 6 logs a failed assertion first). Then, when 0x109b78 is nonzero: phase 2 with group == 0 starts the
    vehicle with size 0 (slot 74), phase 0 (slot 76) and −1 (slot 78) too; with it zero, phase 4 (slot 72).
    `hold_negative` is the static reading of the brief: a negative group neither ends nor creates. `floor` stores the
    group floored at 0, as C# computes it."""
    group, active, start, created = (x['preset'] if x['preset'] is not None else (0, 0)) + (x['timer0'], 0)
    guests = x['guests']
    calls, groups, events = [], [], []
    for now, phase, flag in x['steps']:
        out = []
        if not active and (((now >> 2) - (start >> 2)) & MASK32) > x['interval']:
            group = arrival_full({**x, 'guests': guests})
            if floor:
                group = max(0, group)
            active = 1
            out.append((EVENT_HISTORY, group))
        if active:
            if phase == 2:
                if group > 0:
                    created += 1
                    guests += 1
                    group -= 1
                elif not (hold_negative and group < 0):
                    start, active = now, 0
                    out.append((80, 0))
            elif phase in (-1, 6):
                if phase == 6:
                    out.append((EVENT_ASSERT, 0))
                out.append((82, 1 if group <= 35 else 2 if group <= 60 else 3))
        if flag:
            if phase == 2 and group == 0:
                out.append((74, 0))
            elif phase == 0:
                out.append((76, 0))
            elif phase == -1:
                out.append((78, 0))
        elif phase == 4:
            out.append((72, 0))
        calls.append((active, start, created))
        groups.append(group)
        events.append(out)
    return {'calls': calls, 'groups': groups, 'events': events}


def arrival_update_plan(x: dict, hold_negative: bool = False) -> dict:
    """GUESTS.md "Arrivals" / GuestSimulation.UpdateArrivals with the F9 details (factor, 500 cut) and the group floored
    at 0: when no group is delivering and (turn >> 2) − (start >> 2) exceeds the interval, a group starts; a guest steps
    off per stop (phase 2); an empty group ends and restarts the timer. Only the behaviour is returned: active flag,
    timer start and guests created per call (C# has no group word and no vehicle events)."""
    pending, delivering = (x['preset'] if x['preset'] is not None else (0, 0))
    start, created, guests = x['timer0'], 0, x['guests']
    calls = []
    for now, phase, flag in x['steps']:
        if not delivering and (((now >> 2) - (start >> 2)) & MASK32) > x['interval']:
            pending = max(0, arrival_full({**x, 'guests': guests}))
            delivering = 1
        if delivering and phase == 2:
            if pending < 1 and not (hold_negative and pending < 0):
                delivering, start = 0, now
            elif pending >= 1:
                pending -= 1
                created += 1
                guests += 1
        calls.append((delivering, start, created))
    return {'calls': calls}


def arrival_update_static(x: dict) -> dict:
    """The brief's static reading: only exactly 0 ends a group, so a negative group holds the state."""
    return arrival_update_plan(x, hold_negative=True)


def arrival_update_reading_held(x: dict) -> dict:
    return arrival_update_reading(x, hold_negative=True)


def arrival_update_floored(x: dict) -> dict:
    return arrival_update_reading(x, floor=True)


def arrival_update_equal(original: dict, other: dict) -> bool:
    return all(original[key] == value for key, value in other.items())


def arrival_update_inputs(rng: random.Random, n: int) -> list:
    shipped = dict(world_mode=0, guests=0, points=0, bonus=20, per_visitor=6, min_people=1, obj48=0, limit_mode=0,
                   preset=None)

    def script(first: int, length: int, phases: list, gap: int) -> list:
        now, steps = first, []
        for _ in range(length):
            now += rng.randrange(0, gap + 1)
            steps.append((now, rng.choice(phases), rng.choice([0, 1])))
        return steps

    def make(**kw):
        timer0 = rng.choice([0, 0, rng.randrange(0, 400)])
        x = {**shipped, 'world_mode': rng.choice([0, 0, 0, 0, 4]),
             'guests': rng.choice([0, 1499, 1500, 1700, 2000, rng.randrange(0, 1700)]),
             'points': rng.choice([0, rng.randrange(0, 600)]), 'min_people': rng.choice([1, 1, 2, rng.randrange(0, 70)]),
             'obj48': rng.choice([0, 0, 1]), 'limit_mode': rng.choice([0, 0, 1]),
             'interval': rng.choice([0, 1, 2, 5, rng.randrange(0, 40)]), 'timer0': timer0,
             'preset': rng.choice([None, None, None, None, (rng.randrange(-300, 40), rng.choice([0, 1, 1]))])}
        x.update(kw)
        if 'steps' not in x:
            phases = rng.choice([[2], [2], [2, 2, 2, -1], [-1, 6, 2], [2, -1, 6, 0, 1, 4, 3, 5]])
            x['steps'] = script(x['timer0'] + rng.randrange(0, 30), rng.randrange(8, 40), phases, rng.choice([2, 6, 20, 80]))
        return x
    stop = lambda *s: list(s)
    over = dict(guests=1700, interval=2, timer0=0, preset=None)  # 1700 counted guests: the group is 1500 - 1700 = -200
    edges = [make(**{**over, 'steps': stop((40, 2, 0), (44, 2, 0), (48, 2, 1), (400, 2, 1), (404, 2, 0), (900, 2, 0))}),
             make(**{**over, 'steps': stop((40, -1, 0), (44, 6, 0), (48, 2, 0), (52, 2, 1), (900, 2, 1))}),
             make(**{**over, 'steps': stop((40, -1, 1), (44, -1, 1), (48, -1, 1), (400, -1, 1), (4000, -1, 1))}),
             make(**{**over, 'limit_mode': 1, 'guests': 600, 'steps': stop((40, 2, 1), (44, 2, 1), (48, 2, 1))}),
             make(**{**shipped, 'interval': 2, 'timer0': 0, 'steps': stop((40, 2, 1), (44, 2, 1), (48, 2, 1), (52, 2, 1),
                                                                         (56, 2, 1), (60, 2, 1), (64, 2, 1), (68, 2, 1))}),
             make(**{**shipped, 'guests': 1499, 'min_people': 1, 'interval': 0, 'timer0': 0,
                     'steps': stop((8, 2, 1), (12, 2, 1), (16, 2, 1), (20, 2, 1))}),
             make(**{**shipped, 'world_mode': 4, 'interval': 0, 'timer0': 0, 'steps': stop((8, 2, 1), (12, 2, 1), (16, 2, 1))}),
             make(**{**shipped, 'preset': (-5, 1), 'interval': 0, 'timer0': 0, 'steps': stop((8, 2, 1), (12, 2, 1), (16, 2, 0))}),
             make(**{**shipped, 'preset': (-5, 1), 'interval': 0, 'timer0': 0, 'steps': stop((8, -1, 1), (12, 0, 1), (16, 4, 0))}),
             make(**{**shipped, 'preset': (7, 0), 'interval': 100, 'timer0': 0, 'steps': stop((8, 2, 1), (12, 2, 1), (16, 2, 1))})]
    return edges + [make() for _ in range(n)]


# ============================================================================== (i) ARRIVAL: park points 0xc24c0
POINTS_FIRST = 0x1E0000 - 22714        # game + 0x1da746: index of the first thing in the list (u16)
POINTS_TURN = 0x1E0000 - 22772         # game + 0x1da70c: the world turn, read by 0xe4750
POINTS_CLOCK = 672                     # game + 672: the clock; its +28 word is the rate (15000 s per tick)
POINTS_TYPE_TABLE = 0x1E0000 - 27588   # game + 0x1d943c: per-type park records searched by 0xd1be8 / 0xd1c88
POINTS_THINGS = -30140                 # TOC slot -> ThingArray: 20-byte entries, word 0 = the thing
POINTS_END = -20276                    # TOC slot -> u16 index that ends the thing list
POINTS_REGISTRY = -29976               # TOC slot -> type registry passed to 0x118018
OPEN_TEST = 0xDC834                    # (thing) -> nonzero when the thing counts; stubbed: the chosen flag
THING_AGE = 0xDC520                    # (thing) -> days since the thing's purchase timestamp (+20); stubbed
CLOCK_RATE = 15000                     # clock +28 as the clock constructor sets it (APPROX-TRACE ECON-001/002)
DAY_TICKS = 864_000_000_000            # 100 ns units per day, the divisor at 0xc2600..0xc2608
POINTS_STATE = type('PointsState', (), {})()


def s64(value: int) -> int:
    value &= (1 << 64) - 1
    return value - (1 << 64) if value >> 63 else value


def div64(a: int, b: int) -> int:
    """0x1c4100: signed 64-bit division toward zero; a zero divisor gives 0."""
    return 0 if b == 0 else abs(a) // abs(b) * (1 if (a < 0) == (b < 0) else -1)


def points_days(now: int, turn: int, scale: int) -> int:
    """0xe4750 then the divide at 0xc2600..0xc2608: the turn difference as an unsigned word times the clock rate
    (a 64-bit product), divided by 4, times 10^7, divided by DAY_TICKS; the low word of the quotient."""
    ticks = s64(div64(s64(((now - turn) & MASK32) * (scale & MASK32)), 4) * 10_000_000)
    return s32(div64(ticks, DAY_TICKS))


def points_own_days(now: int, built: int, scale: int) -> int:
    """The 0xdc520 stub: the clock's timestamps (unsigned64(rate x turn / 4) seconds, 0xe4394) subtracted signed
    (TbTimeStamp::operator-), divided by DAY_TICKS (0x1c4100)."""
    return s32(div64((scale * now // 4 - scale * built // 4) * 10_000_000, DAY_TICKS))


def points_count(x: dict, type_id: int) -> int:
    """Park record +24: copies of the type in the park (raised by 0xda874, lowered on removal at 0xdbfcc)."""
    return sum(1 for thing in x['things'] if thing['type'] == type_id)


def points_stubs(target: Target) -> dict:
    state = POINTS_STATE

    def lookup(m: Machine) -> None:
        if m.gpr[3] != state.registry:
            raise AssertionError('0x118018 called without the type registry')
        m.gpr[3] = state.records[m.gpr[4] >> 16]

    def assertion(m: Machine) -> None:
        target.log.append(('assert', m.gpr[3]))

    def thing_of(address: int) -> dict:
        if address not in state.things:
            raise AssertionError('called without a thing')
        return state.things[address]

    def open_test(m: Machine) -> None:
        m.gpr[3] = 1 if thing_of(m.gpr[3])['open'] else 0

    def age(m: Machine) -> None:
        thing = thing_of(m.gpr[3])
        m.gpr[3] = points_own_days(state.x['now'], thing['built'], state.x['scale']) & MASK32

    return {TYPE_RECORD_LOOKUP: lookup, ASSERT: assertion, OPEN_TEST: open_test, THING_AGE: age}


def points_binary(sb: Sandbox, x: dict, log) -> int:
    state = POINTS_STATE
    state.x = x
    game = sb.alloc(0x1E0000)
    sb.write(toc_pointer(sb, ARRIVAL_GAME_POINTER), 'I', game)
    sb.write(game + POINTS_TURN, 'I', x['now'] & MASK32)
    sb.write(game + POINTS_CLOCK + 28, 'I', x['scale'] & MASK32)
    state.registry = toc_pointer(sb, POINTS_REGISTRY)
    state.records = {}
    table = game + POINTS_TYPE_TABLE
    for n, kind in enumerate(x['types']):
        fields = {88: ('i', kind['value']), 92: ('i', kind['decay']), 156: ('i', kind['f9c']), 1960: ('i', kind['kind'])}
        fields.update({616 + 4 * k: ('i', bonus) for k, bonus in enumerate(kind['bonus'])})
        state.records[kind['id']] = sb.struct(2048, fields)
        sb.write(table + 32 * n, 'H', kind['id'])
        sb.write(table + 32 * n + 24, 'i', points_count(x, kind['id']))
        sb.write(table + 32 * n + 28, 'I', kind['first'] & MASK32)
    sb.write(table + 4800, 'i', len(x['types']))  # +4804 (cached id 0, never a type id) and +4808 stay 0
    things = toc_pointer(sb, POINTS_THINGS)
    state.things = {}
    for index, thing in enumerate(x['things'], start=1):
        following = index + 1 if index < len(x['things']) else 0
        address = sb.struct(64, {12: ('H', following), 14: ('H', thing['type'])})
        sb.write(things + 20 * index, 'I', address)
        state.things[address] = thing
    sb.write(toc_pointer(sb, POINTS_END), 'H', 0)
    sb.write(game + POINTS_FIRST, 'H', 1 if x['things'] else 0)
    result = s32(sb.call(0xC24C0, {3: sb.struct(16)}).r3)
    if any(kind == 'assert' and value == 0 for kind, value in log):  # 0xb678 reports a zero condition
        raise AssertionError('an assertion failed (park record not found or ThingArray entry 0 in use)')
    return result


def trunc_div(a: int, b: int) -> int:
    return abs(a) // abs(b) * (1 if (a < 0) == (b < 0) else -1)


def points_bonus(kind: dict, days: int) -> int:
    bracket = trunc_div(days, kind['decay'])
    return kind['bonus'][bracket] if 0 <= bracket < 3 else 0


def points_plan(x: dict) -> int:
    """GUESTS.md "Arrivals" and ParkEconomy.ParkPoints: per ride or sideshow (type kind 0 or 2), AttractionValue plus the
    NewBonus of the age bracket (none outside 0..2). A type without a track (+0x9C, Bumper.WhichTrackType, 0) is aged
    from its first purchase after turn 0 and divided by its copies; a tracked type is aged per copy and counts in full.
    Only things passing the counts-test 0xdc834 count; the input's open flag is the chosen test result (the test itself
    is the park-points-counts-test target, and C# applies the part of it that it has state for, ARRIVAL-003)."""
    return points_full(x)


def points_full(x: dict) -> int:
    """The routine as read from 0xc24c0..0xc26dc: only things passing 0xdc834 count. A type whose +0x9C word is 0 is
    aged from its park record +28 (written by 0xda874 at a purchase while it is still 0) and divided by its count
    (park record +24, 0xc5828); any other type is aged from the thing's own purchase (0xdc520) and divided by 1."""
    types = {kind['id']: kind for kind in x['types']}
    points = 0
    for thing in x['things']:
        kind = types[thing['type']]
        if kind['kind'] not in (0, 2) or not thing['open'] or points_count(x, thing['type']) <= 0:
            continue
        if kind['f9c'] == 0:
            days, divisor = points_days(x['now'], kind['first'], x['scale']), points_count(x, thing['type'])
        else:
            days, divisor = points_own_days(x['now'], thing['built'], x['scale']), 1
        points = s32(points + trunc_div(s32(points_bonus(kind, days) + kind['value']), divisor))
    return points


def points_first(builds: list[int], sold: list[int]) -> int:
    """Park record +28 after purchases at these turns: 0xda874 stores the turn while +28 is still 0, so purchases at
    turn 0 leave it unset and the first purchase at a later turn sets it for good (removal does not clear it)."""
    return next((turn for turn in sorted(builds + sold) if turn != 0), 0)


def points_csharp_request(x: dict) -> dict:
    # The open flag (the chosen 0xdc834 counts-test result) is the object's open state (thing +0x64, ParkObjectState.IsOpen):
    # the other conditions of the test have no C# state (ARRIVAL-003). Type +0x9C is Bumper.WhichTrackType; park record
    # +28 is the first-purchase turn counter.
    return {'fn': 'park_points', 'now': x['now'],
            'types': [{'id': kind['id'], 'kind': kind['kind'], 'value': kind['value'], 'decay': kind['decay'],
                       'bonus': kind['bonus'], 'track_type': kind['f9c'], 'first': kind['first']} for kind in x['types']],
            'things': [{k: thing[k] for k in ('type', 'built', 'open')} for thing in x['things']]}


def points_inputs(rng: random.Random, n: int) -> list:
    day = 86400 * 4 // CLOCK_RATE + 1  # turns per park day, rounded up (23.04 turns at 3750 s per turn)

    def make_type(type_id: int, **kw) -> dict:
        kind = {'id': type_id, 'kind': rng.choice([0, 0, 2, 2, 1, 3, 4]), 'f9c': rng.choice([0, 0, 0, 1, rng.randrange(-3, 4)]),
                'value': rng.choice([10, 25, rng.randrange(-20, 120)]),
                'decay': rng.choice([1, 2, 5, 60, rng.randrange(1, 12)]),
                'bonus': [rng.choice([0, rng.randrange(0, 60), rng.randrange(-30, 80)]) for _ in range(3)]}
        kind.update(kw)
        return kind

    def make(**kw) -> dict:
        now = rng.choice([rng.randrange(0, 12 * day), rng.randrange(0, 200 * day)])
        types = [make_type(0x100 + k) for k in range(rng.randrange(1, 5))]
        things = [{'type': rng.choice(types)['id'], 'open': rng.random() < 0.85,
                   'built': rng.choice([0, now, rng.randrange(0, now + 1)])} for _ in range(rng.randrange(0, 9))]
        for kind in types:
            builds = [thing['built'] for thing in things if thing['type'] == kind['id']]
            sold = [rng.randrange(0, now + 1) for _ in range(rng.choice([0, 0, 1]))]
            kind['first'] = points_first(builds, sold)
        x = {'now': now, 'scale': CLOCK_RATE, 'types': types, 'things': things}
        x.update(kw)
        return x

    ride = dict(id=0x101, kind=0, f9c=0, value=25, decay=2, bonus=[30, 20, 10])
    copies = [{'type': 0x101, 'open': True, 'built': 1}, {'type': 0x101, 'open': True, 'built': 1 + 3 * day}]
    edges = [make(things=[], types=[{**ride, 'first': 0}]),
             # One ride, built now: both readings give 25 + 30.
             make(now=1, types=[{**ride, 'first': 1}], things=[{'type': 0x101, 'open': True, 'built': 1}]),
             # Two copies bought three days apart: the first purchase ages both, the plan ages each copy.
             make(now=1 + 3 * day, types=[{**ride, 'first': 1}], things=copies),
             # The same with +0x9C set: own ages and no division by the count.
             make(now=1 + 3 * day, types=[{**ride, 'f9c': 1, 'first': 1}], things=copies),
             # A copy bought at turn 0 leaves +28 unset; the next purchase sets it and ages both copies from then.
             make(now=1 + 3 * day, types=[{**ride, 'first': 1 + 3 * day}],
                  things=[{'type': 0x101, 'open': True, 'built': 0}, {'type': 0x101, 'open': True, 'built': 1 + 3 * day}]),
             # A thing failing 0xdc834.
             make(now=1, types=[{**ride, 'first': 1}], things=[{'type': 0x101, 'open': False, 'built': 1}]),
             # Kinds other than 0 and 2 never count.
             make(now=1, types=[{**ride, 'kind': 1, 'first': 1}], things=[{'type': 0x101, 'open': True, 'built': 1}])]
    return edges + [make() for _ in range(n)]


# ============================================================================== (i2) ARRIVAL: counts-test 0xdc834
COUNTS_ENTRY_CELL = 0xDD3F4  # (out u16*, thing) -> writes the back-of-queue cell; stubbed: the chosen cell
COUNTS_QUEUE_COUNT = 0xDD118  # (thing) -> guests listed in the thing's queue; stubbed: the chosen count
COUNTS_COASTER = 0x45EAC      # (thing) -> the coaster controller exists and carries no riders; stubbed: the chosen word
COUNTS_NULL_CELL = -19648     # TOC slot -> the u16 the cell is compared with; zero in the file (read, never written)
COUNTS_TYPE_ID = 0x4321
COUNTS_STATE = type('CountsState', (), {})()


def counts_stubs(target: Target) -> dict:
    state = COUNTS_STATE

    def lookup(m: Machine) -> None:
        if m.gpr[3] != state.registry or m.gpr[4] >> 16 != COUNTS_TYPE_ID:
            raise AssertionError('0x118018 called without the registry or the thing type id')
        target.log.append('lookup')
        m.gpr[3] = state.record

    def cell(m: Machine) -> None:
        if m.gpr[4] != state.thing:
            raise AssertionError('0xdd3f4 called without the thing')
        target.log.append('cell')
        m.memory.w16(m.gpr[3], state.x['cell'])

    def count(m: Machine) -> None:
        if m.gpr[3] != state.thing:
            raise AssertionError('0xdd118 called without the thing')
        target.log.append('count')
        m.gpr[3] = state.x['count'] & MASK32

    def coaster(m: Machine) -> None:
        target.log.append('coaster')
        m.gpr[3] = state.x['coaster'] & MASK32

    return {TYPE_RECORD_LOOKUP: lookup, COUNTS_ENTRY_CELL: cell, COUNTS_QUEUE_COUNT: count, COUNTS_COASTER: coaster}


def counts_binary(sb: Sandbox, x: dict, log) -> dict:
    state = COUNTS_STATE
    state.x = x
    state.registry = toc_pointer(sb, POINTS_REGISTRY)
    state.record = sb.struct(2048, {156: ('i', x['track'])})
    state.thing = sb.struct(512, {14: ('H', COUNTS_TYPE_ID), 40: ('i', x['thing40']), 46: ('H', x['flags']),
                                  60: ('I', x['cells'] & MASK32), 100: ('i', x['open']), 408: ('i', x['state'])})
    if sb.image.memory.u16(toc_pointer(sb, COUNTS_NULL_CELL)) != 0:
        raise AssertionError('the u16 behind TOC -19648 is not zero in the file')
    r = sb.call(0xDC834, {3: state.thing})
    return {'counts': r.r3, 'calls': list(log)}


def counts_plan(x: dict) -> dict:
    """The routine as read from 0xdc834..0xdc984, in the order it runs: thing +46 bit 0x4 (Info.IsChoosable), ride state
    (+408) not 1 (BROKEN_DOWN, 0xdfe0c), the open word +100 nonzero (0xdff18 is +100 == 0), state not 4 (CONDEMNED,
    0xdfe20), a back-of-queue cell different from the zero null cell (0xdd3f4), the listed guests below four times the
    queue cells (+60, unsigned, 0xdc988), a coaster (track type 3, 0xe2fe4) only with 0x45eac nonzero, and a track word
    (+40) for track types 1 and 2."""
    calls: list = []

    def done(counts: int) -> dict:
        return {'counts': counts, 'calls': calls}
    if not x['flags'] & 0x4 or s32(x['state']) == 1 or s32(x['open']) == 0 or s32(x['state']) == 4:
        return done(0)
    calls.append('cell')
    if x['cell'] == 0:
        return done(0)
    calls.append('count')
    if not (x['count'] & MASK32) < (x['cells'] << 2) & MASK32:
        return done(0)
    calls.append('lookup')  # 0xe2fe4
    if s32(x['track']) == 3:
        calls.append('coaster')
        if x['coaster'] & MASK32 == 0:
            return done(0)
    calls.append('lookup')
    if s32(x['track']) == 1 and s32(x['thing40']) == 0:
        return done(0)
    calls.append('lookup')
    if s32(x['track']) == 2 and s32(x['thing40']) == 0:
        return done(0)
    return done(1)


def counts_subset(x: dict) -> dict:
    """What ParkEconomy.CountsForParkPoints tests (ARRIVAL-003): Info.IsChoosable (+46 bit 0x4), not broken down or
    condemned (state 1 or 4) and open (+100). The entrance cell, the queue room, the coaster and the track word have no
    C# state, so every input counts as if they held."""
    return {'counts': int(bool(x['flags'] & 0x4) and s32(x['state']) not in (1, 4) and s32(x['open']) != 0),
            'calls': None}  # no call trace: C# has no stubbed callees


def counts_equal(original: dict, other: dict) -> bool:
    return original['counts'] == other['counts'] and (other['calls'] is None or other['calls'] == original['calls'])


def counts_inputs(rng: random.Random, n: int) -> list:
    def make(**kw) -> dict:
        x = {'flags': rng.choice([0x4, 0x4, 0x4, rng.getrandbits(16) | 0x4, rng.getrandbits(16)]),
             'state': rng.choice([0, 0, 0, 0, 1, 2, 3, 4, rng.randrange(-2, 7)]),
             'open': rng.choice([1, 1, 1, 0, rng.randrange(-3, 4)]),
             'cell': rng.choice([rng.randrange(1, 0x10000), rng.randrange(1, 0x10000), 0, 0x8000, 0xFFFF]),
             'cells': rng.choice([rng.randrange(0, 40), rng.randrange(0, 40), 0, 0x40000000, 0x3FFFFFFF, 0xFFFFFFFF]),
             'track': rng.choice([0, 0, 1, 2, 3, 3, rng.randrange(-3, 6)]),
             'thing40': rng.choice([1, 1, 0, rng.randrange(-2, 3)]),
             'coaster': rng.choice([1, 1, 0, 2, 0x100, 0xFFFFFFFF])}
        x['count'] = rng.choice([rng.randrange(0, 4 * max(1, min(x['cells'], 40)) + 2), rng.randrange(0, 160), 0,
                                 4 * x['cells'] & MASK32, (4 * x['cells'] - 1) & MASK32, 0xFFFFFFFF])
        x.update(kw)
        return x
    passing = dict(flags=0x4, state=0, open=1, cell=0x1234, cells=2, count=7, track=0, thing40=1, coaster=1)
    edges = [make(**passing),
             # One condition fails at a time.
             make(**{**passing, 'flags': 0}), make(**{**passing, 'flags': 0xFFFB}), make(**{**passing, 'state': 1}),
             make(**{**passing, 'state': 4}), make(**{**passing, 'state': 2}), make(**{**passing, 'state': 3}),
             make(**{**passing, 'open': 0}), make(**{**passing, 'cell': 0}), make(**{**passing, 'count': 8}),
             make(**{**passing, 'count': 9}), make(**{**passing, 'cells': 0, 'count': 0}),
             make(**{**passing, 'cells': 0x40000000, 'count': 0xFFFFFFFF}), make(**{**passing, 'cells': 0x40000001, 'count': 3}),
             make(**{**passing, 'track': 3, 'coaster': 0}), make(**{**passing, 'track': 3, 'coaster': 1}),
             make(**{**passing, 'track': 3, 'thing40': 0}), make(**{**passing, 'track': 1, 'thing40': 0}),
             make(**{**passing, 'track': 2, 'thing40': 0}), make(**{**passing, 'track': 0, 'thing40': 0}),
             make(**{**passing, 'track': 4, 'thing40': 0}), make(**{**passing, 'track': 1, 'coaster': 0})]
    return edges + [make() for _ in range(n)]


# ============================================================================== (j) LOANS: offers 0xcb7a8
LOAN_SETTINGS = -30084         # TOC slot -> settings block: +408 InitialCash, then 4 words per loan offer from +416
LOAN_BASE_CTOR = 0xFA718       # (bank, u16 1, ...) base-class constructor: writes the object header only; stubbed
LOAN_POW = 'pow'               # MathLib import behind the glue 0x1c61a4
LOAN_TO_UINT = (0x1C3FBC, 0x1C400C)
LOAN_POW_GLUE = (0x1C61A4, 0x1C61BC)
LOAN_UNSIGNED_STOP = 0xCB908   # end of the 8-offer loop (the rest of the constructor builds the bank object's helpers)
SHIPPED_LOANS = [(100000, 20, 36, 0), (50000, 20, 36, 1), (25000, 20, 36, 2), (10000, 20, 36, 3),
                 (18000, 23, 24, 4), (30000, 22, 30, 5), (80000, 18, 48, 6), (65000, 21, 30, 7)]
SHIPPED_MONTHLY = [3651, 1825, 912, 365, 922, 1282, 2320, 2749]  # PPC-economy "Mac expression monthly"


def loan_stubs(target: Target) -> dict:
    def base_constructor(m: Machine) -> None:
        target.log.append(('base-ctor', m.gpr[3], m.gpr[4] >> 16))

    def pow_stub(m: Machine) -> None:
        """MathLib pow(f1, f2) on binary64: stubbed with Python's math.pow (the host C library). The base is
        1 + APR/100 >= 1 and the exponent N/24 >= 0, so every result is finite or +inf (overflow)."""
        try:
            value = math.pow(bits_to_double(m.fpr[1]), bits_to_double(m.fpr[2]))
        except OverflowError:
            value = math.inf
        except ValueError:
            value = math.nan
        target.log.append(('pow', bits_to_double(m.fpr[1]), bits_to_double(m.fpr[2])))
        m.fpr[1] = double_to_bits(value)

    return {LOAN_BASE_CTOR: base_constructor, LOAN_POW: pow_stub}


def loan_offers_binary(sb: Sandbox, x: dict, log) -> dict:
    """Run the 8-offer loop of 0xcb7a8 on a zeroed bank object. The settings block (TOC -30084, data 0x54860) holds
    InitialCash at +408 and the offers (amount, APR, months, lender) from +416, 16 bytes each."""
    bank = sb.struct(512)
    settings = toc_pointer(sb, LOAN_SETTINGS)
    sb.write(settings + 408, 'I', x['cash'])
    for index, (amount, apr, months, lender) in enumerate(x['offers']):
        for word, value in enumerate((amount, apr, months, lender)):
            sb.write(settings + 416 + 16 * index + 4 * word, 'I', value)
    sb.call(0xCB7A8, {3: bank}, stop=LOAN_UNSIGNED_STOP)
    if [entry for entry in log if entry[0] == 'base-ctor'] != [('base-ctor', bank, 1)]:
        raise AssertionError('the base constructor was not called once with the bank object and the word 1')
    records = [tuple(struct.unpack('>8I', sb.image.memory.read(bank + 32 * i + 20, 32))) for i in range(8)]
    # record words: available, amount, APR, months, monthly, bought, months repaid, lender
    return {'monthly': [record[4] for record in records],
            'records': [(r[0], r[1], r[2], r[3], r[5], r[6], r[7]) for r in records],
            'bank': tuple(sb.read(bank + offset, 'I') for offset in (12, 16, 276, 280, 284, 288, 292))}


def trunc_u32(value: float) -> int:
    """0x1c3fbc: negative gives 0, NaN and anything from 2^32 up give 2^32 - 1, else truncation toward zero."""
    if math.isnan(value):
        return MASK32
    if value < 0.0:
        return 0
    return MASK32 if value >= 4294967296.0 else int(value)


def loan_pow(base: float, exponent: float) -> float:
    try:
        return math.pow(base, exponent)
    except OverflowError:
        return math.inf


def loan_fdiv(a: float, b: float) -> float:
    if b == 0.0:
        return math.nan if a == 0.0 or math.isnan(a) else math.copysign(math.inf, a)
    return a / b


def loan_monthly_reading(amount: int, apr: int, months: int, exponent: float = 0.5, rate: float = 100.0) -> int:
    """0xcb8a4..0xcb8f4 in binary64: trunc_u32(P * pow(1 + APR/100, (N/12) * 0.5) / N). The two optional parameters are
    the sharpness variants (full years in the exponent, the APR divisor)."""
    power = loan_pow(1.0 + apr / rate, (months / 12.0) * exponent)
    return trunc_u32(loan_fdiv(float(amount) * power, float(months)))


def loan_offers_reading(x: dict, **variant) -> dict:
    """The routine as read: per offer record (available when the amount is at most 10000, copied fields, bought and
    months repaid 0), the monthly instalment above, and the bank words the constructor initialises (balance =
    InitialCash, batch 0, withdrawals enabled 1, last balance 0, turn entered red 0, year profit 0)."""
    offers = x['offers']
    return {'monthly': [loan_monthly_reading(a, r, n, **variant) for a, r, n, _ in offers],
            'records': [(int(a <= 10000), a, r, n, 0, 0, lender) for a, r, n, lender in offers],
            'bank': (x['cash'], 0, 1, 0, 0, 0, 0)}


def loan_annuity_plan(amount: int, apr: int, months: int) -> int | str:
    """The retired OpenTPW rule (an annuity at APR/12 per month, rounded down; floor(amount / months) at 0 % APR). C#
    no longer uses it; the random loan inputs still draw an instalment from it so that M differs from the original's."""
    if months <= 0 or amount < 0:
        return 'exception'
    if apr <= 0:
        return amount // months
    rate = apr / 1200.0
    return math.floor(amount * rate / (1 - loan_pow(1 + rate, -months)))


def loan_plan(amount: int, apr: int, months: int) -> int | str:
    """ECON-006 resolved (ParkLedger LoanMath.MonthlyRepayment, ORACLE F12): the original's instalment, trunc_u32 of
    P x pow(1 + APR/100, (N/12) x 0.5) / N, for every APR; C# throws for months below 1 or a negative amount."""
    if months <= 0 or amount < 0:
        return 'exception'
    return loan_monthly_reading(amount, apr, months)


def loan_offers_plan(x: dict) -> dict:
    return {'monthly': [loan_plan(a, r, n) for a, r, n, _ in x['offers']]}


def loan_offers_request(x: dict) -> dict:
    return {'fn': 'loan_offers', 'offers': [[a, r, n] for a, r, n, _ in x['offers']]}


def loan_offers_result(x: dict, answer) -> dict:
    return {'monthly': ['exception' if value is None else value for value in answer['monthly']]}


def loan_equal(original, other) -> bool:
    """Compare on the keys the other side has (the plan and C# know only the monthly instalment)."""
    return all(original[key] == other[key] for key in other)


def loan_offers_inputs(rng: random.Random, n: int) -> list:
    def offer(**kw) -> tuple:
        amount = rng.choice([rng.randrange(0, 200001), rng.choice([1000, 5000, 9999, 10000, 10001, 25000, 100000]), 0])
        apr = rng.choice([0, rng.randrange(1, 41), rng.randrange(18, 24)])
        months = rng.choice([rng.randrange(1, 61), rng.choice([1, 12, 24, 36, 48, 120])])
        return (kw.get('amount', amount), kw.get('apr', apr), kw.get('months', months), kw.get('lender', rng.randrange(0, 8)))

    def make(offers=None, cash=None) -> dict:
        return {'cash': cash if cash is not None else rng.choice([0, 50000, 100000, rng.randrange(0, 1 << 20)]),
                'offers': offers if offers is not None else [offer() for _ in range(8)]}
    edges = [make(list(SHIPPED_LOANS), cash=100000),
             make([(10000, 0, 36, i) for i in range(8)], cash=0),
             make([(a, 0, 24, 3) for a in (1000, 1, 23, 24, 25, 99999, 100000, 7)]),
             make([(18000, a, 24, 4) for a in (0, 1, 5, 10, 20, 23, 40, 100)]),
             make([(10000, 20, n, 1) for n in (1, 2, 12, 24, 36, 60, 120, 240)])]
    return edges + [make() for _ in range(n)]


# ============================================================================== (k) LOANS: monthly step 0xcc21c
LOAN_STEP_STOP = 0xCC3D0       # end of the loan loop; the tail (bank in the red for a month) sends a message
LOAN_GAME_SLOTS = (-30080, -30124)  # TOC slots -> words holding the game pointer (0xcc21c reads both)
LOAN_STRINGS = -20224          # TOC slot -> string table (assert / debug messages); ids are offsets from it
LOAN_ACC_IN, LOAN_ACC_OUT = 0x1FC90, 0x1F5A0   # park words raised by credits (money in) and debits (money out)
LOAN_TURN = 0x1DA70C           # game + 0x1da70c: the turn counter
LOAN_STATE = type('LoanState', (), {})()
BANK_BALANCE, BANK_BATCH, BANK_WITHDRAW, BANK_LAST, BANK_RED, BANK_PROFIT = 12, 16, 276, 284, 288, 292
LOAN_OUT_KEYS = ('balance', 'profit', 'active', 'batch', 'last', 'red', 'acc_in', 'acc_out', 'loans', 'events')


def loan_step_stubs(target: Target) -> dict:
    state = LOAN_STATE

    def park(m: Machine) -> None:
        if m.gpr[3] != state.game:
            raise AssertionError('0x1082b4 called without the game pointer')
        m.gpr[3] = state.park

    def assertion(m: Machine) -> None:
        target.log.append(('assert', m.gpr[3] & 0xFF, (m.gpr[4] - state.strings) & MASK32))

    def debug(m: Machine) -> None:
        message = (m.gpr[4] - state.strings) & MASK32
        if message == 465:
            target.log.append(('log465', m.gpr[5], m.gpr[6], m.gpr[7]))   # months repaid + 1, loan index, instalment
        elif message == 587:
            target.log.append(('log587', m.gpr[5]))                       # loan index
        else:
            raise AssertionError(f'unexpected debug message {message}')

    return {PARK_OF_GAME: park, ASSERT: assertion, DEBUG_LOG: debug}


def loan_step_binary(sb: Sandbox, x: dict, log) -> dict:
    """Set up a bank object (balance +12, batch +16, withdrawals +276, last balance +284, turn entered red +288, year
    profit +292, eight 32-byte loan records: P +24, APR +28, months +32, instalment +36, bought +40, months repaid
    +44) and call the loan loop of 0xcc21c once per month in one sandbox."""
    state = LOAN_STATE
    state.game = sb.alloc(0x1E0000)
    state.park = sb.alloc(0x21000)
    state.strings = toc_pointer(sb, LOAN_STRINGS)
    for slot in LOAN_GAME_SLOTS:
        sb.write(toc_pointer(sb, slot), 'I', state.game)
    bank = sb.struct(512)
    for offset, key in ((BANK_BALANCE, 'bal'), (BANK_BATCH, 'batch'), (BANK_WITHDRAW, 'wd'), (BANK_LAST, 'last'),
                        (BANK_RED, 'red'), (BANK_PROFIT, 'profit')):
        sb.write(bank + offset, 'I', x[key] & MASK32)
    for index, loan in enumerate(x['loans']):
        base = bank + 32 * index
        for offset, key in ((24, 'amount'), (28, 'apr'), (32, 'months'), (36, 'monthly'), (40, 'bought'), (44, 'repaid')):
            sb.write(base + offset, 'I', loan[key] & MASK32)
    out = {key: [] for key in LOAN_OUT_KEYS}
    for turn in x['turns']:
        sb.write(state.game + LOAN_TURN, 'I', turn)
        mark = len(log)
        sb.call(0xCC21C, {3: bank}, stop=LOAN_STEP_STOP)
        out['balance'].append(sb.read(bank + BANK_BALANCE, 'i'))
        out['profit'].append(sb.read(bank + BANK_PROFIT, 'i'))
        out['batch'].append(sb.read(bank + BANK_BATCH, 'I'))
        out['last'].append(sb.read(bank + BANK_LAST, 'i'))
        out['red'].append(sb.read(bank + BANK_RED, 'I'))
        out['acc_in'].append(sb.read(state.park + LOAN_ACC_IN, 'I'))
        out['acc_out'].append(sb.read(state.park + LOAN_ACC_OUT, 'I'))
        loans = [(sb.read(bank + 32 * i + 40, 'I'), sb.read(bank + 32 * i + 44, 'I')) for i in range(8)]
        out['loans'].append(loans)
        out['active'].append(sum(1 for bought, _ in loans if bought))
        out['events'].append([tuple(entry) for entry in log[mark:]])
    return out


def loan_step_reading(x: dict, drop: str = '') -> dict:
    """0xcc21c..0xcc3d0 as read. Per month: the batch credit is moved into the balance, the money-in word and the year
    profit; each bought loan then (with withdrawals enabled) takes its stored instalment M off the balance (and records
    the turn the balance first goes negative), raises the money-out word and lowers the year profit by M. Whatever the
    withdrawals flag, the year profit then gains M - floor((M x N - P) / N) in 32-bit unsigned arithmetic (a net
    -floor((M x N - P) / N) with withdrawals), months repaid rise by 1, and when they reach N the bought flag and the
    months repaid clear. `drop` (sharpness tests) removes one part of the reading: 'share', 'withdraw-flag', 'red',
    'clear', 'batch' or 'profit-debit'."""
    bal, batch, last, red, profit = (x[key] & MASK32 for key in ('bal', 'batch', 'last', 'red', 'profit'))
    acc_in = acc_out = 0
    loans = [{**loan, 'bought': loan['bought'] & MASK32, 'repaid': loan['repaid'] & MASK32} for loan in x['loans']]
    out = {key: [] for key in LOAN_OUT_KEYS}
    for turn in x['turns']:
        events = [('assert', int(batch < 1_000_000), 340)]
        if drop != 'batch':
            bal = (bal + batch) & MASK32
            acc_in = (acc_in + batch) & MASK32
            profit = (profit + batch) & MASK32
            batch = 0
        for index, loan in enumerate(loans):
            if loan['bought'] == 0:
                continue
            instalment = loan['monthly'] & MASK32
            events.append(('log465', (loan['repaid'] + 1) & MASK32, index, instalment))
            if x['wd'] & MASK32 or drop == 'withdraw-flag':
                bal = (bal - instalment) & MASK32
                if s32(bal) < 0 and s32(last) >= 0 and drop != 'red':
                    red = turn & MASK32
                last = bal
                acc_out = (acc_out + instalment) & MASK32
                if drop != 'profit-debit':
                    profit = (profit - instalment) & MASK32
            loan['repaid'] = (loan['repaid'] + 1) & MASK32
            months = loan['months'] & MASK32
            events.append(('assert', int(months != 0), 525))
            if months != 0 and drop != 'share':
                share = (((instalment * months) - (loan['amount'] & MASK32)) & MASK32) // months
                profit = (profit + instalment - share) & MASK32
            if loan['repaid'] == months and drop != 'clear':
                events.append(('log587', index))
                loan['bought'] = loan['repaid'] = 0
        out['balance'].append(s32(bal))
        out['profit'].append(s32(profit))
        out['batch'].append(batch)
        out['last'].append(s32(last))
        out['red'].append(red)
        out['acc_in'].append(acc_in)
        out['acc_out'].append(acc_out)
        snapshot = [(loan['bought'], loan['repaid']) for loan in loans]
        out['loans'].append(snapshot)
        out['active'].append(sum(1 for bought, _ in snapshot if bought))
        out['events'].append(events)
    return out


def to_s32(value: int) -> int:
    value &= MASK32
    return value - (1 << 32) if value & 0x80000000 else value


def loan_term_plan(x: dict) -> dict:
    """ECON-006 resolved (ORACLE F13): per month every outstanding loan, in index order, pays exactly M, the
    instalment's interest share (M x N - P) / N (32-bit) lowers the year profit (a 32-bit word, so it wraps), and a loan
    ends after N months. Balance and profit are cumulative from the input state."""
    accounts = [{'left': loan['months'], 'monthly': loan['monthly'], 'months': loan['months'], 'amount': loan['amount']}
                for loan in x['loans'] if loan['bought']]
    balance, profit = x['bal'], x['profit']
    out = {'balance': [], 'profit': [], 'active': []}
    for _ in x['turns']:
        for account in list(accounts):
            balance -= account['monthly']
            profit = to_s32(profit - (((account['monthly'] * account['months']) - account['amount']) & MASK32) // account['months'])
            account['left'] -= 1
            if account['left'] <= 0:
                accounts.remove(account)
        out['balance'].append(balance)
        out['profit'].append(profit)
        out['active'].append(len(accounts))
    return out


def loan_term_request(x: dict) -> dict:
    return {'fn': 'loan_term', 'balance': x['bal'], 'steps': len(x['turns']),
            'loans': [{k: loan[k] for k in ('amount', 'apr', 'months', 'monthly')} for loan in x['loans'] if loan['bought']]}


def loan_term_result(x: dict, answer) -> dict:
    if not isinstance(answer, dict) or 'months' not in answer:
        return {'exception': str(answer)}
    return {'balance': [m['balance'] for m in answer['months']],
            'profit': [to_s32(x['profit'] + m['profit']) for m in answer['months']],
            'active': [m['active'] for m in answer['months']]}


def loan_state_inputs(rng: random.Random, n: int, terms: bool) -> list:
    """Random bank states. `terms`: fresh loans only (bought, none repaid, months at least 1), withdrawals enabled and no
    batch credit, run until two months after the longest term; otherwise any state, one to three months."""
    def loan() -> dict:
        amount = rng.choice([rng.randrange(0, 200001), rng.choice([10000, 18000, 50000, 100000])])
        apr = rng.choice([0, rng.randrange(1, 41), rng.randrange(18, 24)])
        months = rng.choice([rng.randrange(1, 49), rng.choice([1, 2, 12, 24, 36])]) if terms else rng.choice(
            [rng.randrange(0, 49), rng.choice([0, 1, 2, 12, 36])])
        kind = rng.choice(['reading', 'reading', 'annuity', 'random'])
        if kind == 'reading' and months:
            monthly = loan_monthly_reading(amount, apr, months)
        elif kind == 'annuity' and months:
            monthly = loan_annuity_plan(amount, apr, months)
        else:
            monthly = rng.randrange(0, 20000)
        bought = 1 if terms else rng.choice([0, 1, 1, 2])
        repaid = 0 if terms else rng.choice([0, 0, max(0, months - 1), months, rng.randrange(0, months + 2)])
        return {'amount': amount, 'apr': apr, 'months': months, 'monthly': monthly, 'bought': bought, 'repaid': repaid}

    idle = {'amount': 0, 'apr': 0, 'months': 0, 'monthly': 0, 'bought': 0, 'repaid': 0}

    def make() -> dict:
        loans = [loan() for _ in range(rng.randrange(1, 4) if terms else 8)]
        loans += [idle] * (8 - len(loans))
        steps = (max([item['months'] for item in loans if item['bought']] or [0]) + 2) if terms else rng.randrange(1, 4)
        turn = rng.randrange(0, 1 << 20)
        bal = rng.choice([rng.randrange(0, 1 << 24), 1000000, 50000]) if terms else rng.choice(
            [rng.randrange(0, 1 << 20), 0, 50000, rng.randrange(-60000, 60000)])
        return {'bal': bal,
                'batch': 0 if terms else rng.choice([0, 0, rng.randrange(0, 5000), 999999, 1000000, 1000001, rng.getrandbits(32)]),
                'wd': 1 if terms else rng.choice([1, 1, 1, 0]),
                'last': bal if terms else rng.choice([rng.randrange(-1000, 1000), 0, -1, rng.randrange(0, 1 << 20)]),
                'red': rng.randrange(0, 1 << 20), 'profit': rng.choice([0, rng.randrange(-20000, 20000)]),
                'loans': loans, 'turns': [turn + 800 * k for k in range(steps)]}
    return [make() for _ in range(n)]


LOAN_IDLE = {'amount': 0, 'apr': 0, 'months': 0, 'monthly': 0, 'bought': 0, 'repaid': 0}


def loan_month_inputs(rng: random.Random, n: int) -> list:
    rise = {'amount': 100000, 'apr': 20, 'months': 36, 'monthly': 3651, 'bought': 1, 'repaid': 0}
    base = {'bal': 50000, 'batch': 0, 'wd': 1, 'last': 50000, 'red': 0, 'profit': 0, 'turns': [800]}
    first = [rise] + [LOAN_IDLE] * 7
    edges = [{**base, 'loans': first},
             # the balance goes negative: the red turn is set
             {**base, 'bal': 1000, 'last': 1000, 'loans': first},
             # withdrawals disabled: nothing is taken, but the year profit still gains M - interest share
             {**base, 'wd': 0, 'loans': first},
             # a batch credit flushed into the balance, money in and profit
             {**base, 'batch': 2500, 'loans': [LOAN_IDLE] * 8},
             # the loan's last month: bought and months repaid clear
             {**base, 'turns': [800, 1600], 'loans': [{**rise, 'repaid': 35}] + [LOAN_IDLE] * 7},
             {**base, 'batch': 1000000, 'loans': [LOAN_IDLE] * 8}]
    return edges + loan_state_inputs(rng, n, terms=False)


def loan_term_inputs(rng: random.Random, n: int) -> list:
    def one(amount, apr, months, monthly):
        return {'amount': amount, 'apr': apr, 'months': months, 'monthly': monthly, 'bought': 1, 'repaid': 0}
    base = {'bal': 1000000, 'batch': 0, 'wd': 1, 'last': 1000000, 'red': 0, 'profit': 0}
    # 100000 at 20 % over 36 months as the Mac expression stores it (3651) and as OpenTPW's former annuity did (3716),
    # and 10000 at 0 % over 24 months
    edges = [{**base, 'loans': [one(100000, 20, 36, 3651)] + [LOAN_IDLE] * 7, 'turns': [800 * k for k in range(38)]},
             {**base, 'loans': [one(100000, 20, 36, 3716)] + [LOAN_IDLE] * 7, 'turns': [800 * k for k in range(38)]},
             {**base, 'loans': [one(10000, 0, 24, 416)] + [LOAN_IDLE] * 7, 'turns': [800 * k for k in range(26)]}]
    return edges + loan_state_inputs(rng, n, terms=True)


def loan_equal_state(original, other) -> bool:
    return all(original[key] == other[key] for key in other)


LOAN_ALLOWED = [(0xCC21C, LOAN_STEP_STOP)]
LOAN_MONTH = Target('loan-month', 'Loan monthly step 0xcc21c', 'ECONOMY.md / PPC-economy', 0xCC21C, LOAN_ALLOWED,
                    loan_step_stubs, loan_step_binary, None, loan_month_inputs,
                    alternatives={'routine-reading': loan_step_reading}, equal=loan_equal_state,
                    notes=['Fragment 0xcc21c..0xcc3d0 (batch flush and the 8-record loan loop). Stubs: 0x1082b4 game -> park '
                           '(sentinel, checked), 0xb678 assertion and 0x5fbcc debug print (both logged). The tail (a bank '
                           'in the red for a month) is not run.',
                           'No plan side: the plan has no monthly step on the bank object; see loan-term.'])
LOAN_TERM = Target('loan-term', 'Loan term 0xcc21c run over the loan term', 'ECONOMY.md / ParkLedger LoanMath (F12, F13)',
                   0xCC21C, LOAN_ALLOWED, loan_step_stubs, loan_step_binary, loan_term_plan, loan_term_inputs,
                   alternatives={'routine-reading': loan_step_reading}, equal=loan_equal_state,
                   csharp=('loan_term', loan_term_request, loan_term_result),
                   notes=['The same fragment run once per month over a whole term (withdrawals enabled, no batch credit), with '
                          'the instalment M given. The plan and C# sides take M as given and pay it flat (LoanMath.Pay).',
                          'C# hook: bridge function "loan_term" (LoanMath.Pay, ParkLedger.Post and ChargeLoanInterest, the '
                          'calls of ParkEconomy.EndMonth).'])

# ============================================================================== (l) LOANS: taking an offer 0xcc904
LOAN_TAKE_ALLOWED = [(0xCC904, 0xCC9E8)]


def loan_take_binary(sb: Sandbox, x: dict, log) -> dict:
    """Call 0xcc904(bank, index) on a bank with the chosen balance, year profit and the offer's amount and bought flag."""
    state = LOAN_STATE
    state.game = sb.alloc(0x1E0000)
    state.park = sb.alloc(0x21000)
    state.strings = toc_pointer(sb, LOAN_STRINGS)
    for slot in LOAN_GAME_SLOTS:
        sb.write(toc_pointer(sb, slot), 'I', state.game)
    bank = sb.struct(512)
    sb.write(bank + BANK_BALANCE, 'I', x['bal'] & MASK32)
    sb.write(bank + BANK_PROFIT, 'I', x['profit'] & MASK32)
    base = bank + 32 * x['index']
    sb.write(base + 24, 'I', x['amount'] & MASK32)
    sb.write(base + 40, 'I', x['bought'] & MASK32)
    result = sb.call(0xCC904, {3: bank, 4: x['index']})
    return {'taken': result.r3 & 0xFF, 'balance': sb.read(bank + BANK_BALANCE, 'i'), 'profit': sb.read(bank + BANK_PROFIT, 'i'),
            'money_in': sb.read(state.park + LOAN_ACC_IN, 'I'), 'money_out': sb.read(state.park + LOAN_ACC_OUT, 'I'),
            'bought': sb.read(base + 40, 'I'), 'events': [tuple(entry) for entry in log]}


def loan_take_stubs(target: Target) -> dict:
    stubs = loan_step_stubs(target)
    state = LOAN_STATE

    def debug(m: Machine) -> None:
        target.log.append(('log', (m.gpr[4] - state.strings) & MASK32))   # 667 already taken, 701 / 762 taking

    stubs[DEBUG_LOG] = debug
    return stubs


def loan_take_reading(x: dict) -> dict:
    """0xcc904: an offer already bought logs and returns 0. Otherwise it logs twice, credits the amount to the balance
    (+12), the money-in word (park +0x1FC90) and the year profit (+292), takes the same amount off the year profit again,
    sets the bought flag to 1 and returns 1. The amount is asserted below 1,000,000 (the assertion does not stop it)."""
    if x['bought'] & MASK32:
        return {'taken': 0, 'balance': s32(x['bal']), 'profit': s32(x['profit']), 'money_in': 0, 'money_out': 0,
                'bought': x['bought'] & MASK32, 'events': [('log', 667)]}
    amount = x['amount'] & MASK32
    return {'taken': 1, 'balance': s32(x['bal'] + amount), 'profit': s32(x['profit'] + amount - amount), 'money_in': amount,
            'money_out': 0, 'bought': 1,
            'events': [('log', 701), ('log', 762), ('assert', int(amount < 1_000_000), 340)]}


def loan_take_plan(x: dict) -> dict:
    """ECON-005 as OpenTPW has it (ParkEconomy.TakeLoan): the amount is posted as LoanReceived: it raises the balance and
    the money-in total, but not the year's profit; an offer already taken cannot be taken again."""
    if x['bought']:
        return {'taken': 0, 'balance': x['bal'], 'profit': x['profit'], 'money_in': 0, 'bought': x['bought']}
    return {'taken': 1, 'balance': x['bal'] + x['amount'], 'profit': x['profit'], 'money_in': x['amount'], 'bought': 1}


def loan_take_request(x: dict) -> dict:
    return {'fn': 'loan_take', 'cash': x['bal'], 'amount': x['amount'], 'taken_before': bool(x['bought'])}


def loan_take_result(x: dict, answer) -> dict:
    if not isinstance(answer, dict) or 'taken' not in answer:
        return {'exception': str(answer)}
    return {'taken': int(answer['taken']), 'balance': x['bal'] + answer['balance'], 'profit': x['profit'] + answer['profit'],
            'money_in': answer['money_in'], 'bought': 1 if answer['taken'] or x['bought'] else 0}


def loan_take_inputs(rng: random.Random, n: int) -> list:
    def make(**kw) -> dict:
        x = {'bal': rng.choice([0, rng.randrange(0, 1 << 24), 50000]), 'profit': rng.choice([0, rng.randrange(-30000, 30000)]),
             'index': rng.randrange(0, 8), 'amount': rng.choice([rng.randrange(0, 200001), 10000, 100000]),
             'bought': rng.choice([0, 0, 0, 1])}
        x.update(kw)
        return x
    edges = [make(bought=0, amount=100000, bal=50000, index=0), make(bought=1, amount=100000, bal=50000, index=0),
             make(bought=0, amount=1000000, index=7), make(bought=0, amount=1000001, index=7), make(bought=0, amount=0)]
    return edges + [make() for _ in range(n)]


def loan_take_equal(original, other) -> bool:
    return all(original[key] == other[key] for key in other)


LOAN_TAKE = Target('loan-take', 'Loan taken 0xcc904', 'ECONOMY.md / ParkEconomy.TakeLoan (ECON-005)', 0xCC904,
                   LOAN_TAKE_ALLOWED, loan_take_stubs, loan_take_binary, loan_take_plan, loan_take_inputs,
                   alternatives={'routine-reading': loan_take_reading}, equal=loan_take_equal,
                   csharp=('loan_take', loan_take_request, loan_take_result),
                   notes=['Whole routine. Stubs: 0x1082b4 game -> park (sentinel, checked), 0xb678 assertion and 0x5fbcc debug '
                          'print (logged). Only the money-in word is compared with the plan: the plan has no money-out change.',
                          'C# hook: bridge function "loan_take" (ParkEconomy.TakeLoan on an economy with the one offer).'])

# ============================================================================== (m) LOANS: early repayment 0xccc78
LOAN_PAYOFF_ALLOWED = [(0xCCC78, 0xCCDEC)]


def loan_payoff_binary(sb: Sandbox, x: dict, log) -> dict:
    """Call 0xccc78(bank, index): a bank with the chosen balance, withdrawals flag, last balance and year profit and one
    loan record (P +24, months +32, instalment +36, bought +40, months repaid +44)."""
    state = LOAN_STATE
    state.game = sb.alloc(0x1E0000)
    state.park = sb.alloc(0x21000)
    state.strings = toc_pointer(sb, LOAN_STRINGS)
    for slot in LOAN_GAME_SLOTS:
        sb.write(toc_pointer(sb, slot), 'I', state.game)
    bank = sb.struct(512)
    for offset, key in ((BANK_BALANCE, 'bal'), (BANK_WITHDRAW, 'wd'), (BANK_LAST, 'last'), (BANK_PROFIT, 'profit')):
        sb.write(bank + offset, 'I', x[key] & MASK32)
    base = bank + 32 * x['index']
    for offset, key in ((24, 'amount'), (32, 'months'), (36, 'monthly'), (40, 'bought'), (44, 'repaid')):
        sb.write(base + offset, 'I', x[key] & MASK32)
    result = sb.call(0xCCC78, {3: bank, 4: x['index']})
    return {'taken': result.r3 & 0xFF, 'balance': sb.read(bank + BANK_BALANCE, 'i'), 'profit': sb.read(bank + BANK_PROFIT, 'i'),
            'last': sb.read(bank + BANK_LAST, 'i'), 'bought': sb.read(base + 40, 'I'), 'repaid': sb.read(base + 44, 'I'),
            'money_out': sb.read(state.park + LOAN_ACC_OUT, 'I'), 'events': [tuple(entry) for entry in log]}


def loan_payoff_reading(x: dict) -> dict:
    """0xccc78 as read. Not bought: log 813, return 0. The payoff is M x (N - months repaid) (32-bit); a balance (unsigned
    compare) below it logs 855 and returns 0. Otherwise, with withdrawals enabled, the payoff leaves the balance (+12; the
    last balance +284 follows it) and the money-out word and lowers the year profit; whatever the flag the year profit then
    gains the payoff less q x N, q = (M x N - P) / N in 32-bit unsigned arithmetic (months repaid are cleared before they
    are read again, so the whole term counts). Bought and months repaid clear; the term is asserted non-zero (525) and a
    zero term skips the share. Returns 1. A balance that ends negative (the red turn) is not covered by the inputs."""
    bal, profit, last = (x[key] & MASK32 for key in ('bal', 'profit', 'last'))
    amount, months, monthly, repaid = (x[key] & MASK32 for key in ('amount', 'months', 'monthly', 'repaid'))
    keep = {'taken': 0, 'balance': s32(bal), 'profit': s32(profit), 'last': s32(last), 'bought': x['bought'] & MASK32,
            'repaid': repaid, 'money_out': 0}
    if not x['bought'] & MASK32:
        return {**keep, 'events': [('log', 813)]}
    payoff = (monthly * ((months - repaid) & MASK32)) & MASK32
    if bal < payoff:
        return {**keep, 'events': [('log', 855)]}
    money_out = 0
    if x['wd'] & MASK32:
        bal = (bal - payoff) & MASK32
        last = bal
        money_out = payoff
        profit = (profit - payoff) & MASK32
    if months:
        share = ((monthly * months - amount) & MASK32) // months
        profit = (profit + payoff - ((share * months) & MASK32)) & MASK32
    return {'taken': 1, 'balance': s32(bal), 'profit': s32(profit), 'last': s32(last), 'bought': 0, 'repaid': 0,
            'money_out': money_out, 'events': [('assert', int(months != 0), 525)]}


def loan_payoff_plan(x: dict) -> dict:
    """ECON-006 resolved (ParkEconomy.TryRepayLoan, ORACLE F13): the payoff M x (N - months repaid) leaves the balance
    (always, ECON-051) when the balance covers it; the year profit (a 32-bit word) falls by the whole-term share q x N
    (32-bit), not by the payoff; the loan ends. Without the cash or without the loan nothing changes."""
    keep = {'taken': 0, 'balance': x['bal'], 'profit': x['profit'], 'bought': int(bool(x['bought'])), 'repaid': x['repaid'],
            'money_out': 0}
    if not x['bought'] or x['months'] <= 0:
        return keep
    payoff = x['monthly'] * (x['months'] - x['repaid'])
    if payoff > x['bal']:
        return keep
    share = (((x['monthly'] * x['months']) - x['amount']) & MASK32) // x['months']
    return {'taken': 1, 'balance': x['bal'] - payoff, 'profit': to_s32(x['profit'] - ((share * x['months']) & MASK32)),
            'bought': 0, 'repaid': 0, 'money_out': payoff}


def loan_payoff_request(x: dict) -> dict:
    return {'fn': 'loan_payoff', 'bal': x['bal'], 'profit': x['profit'], 'amount': x['amount'], 'apr': x['apr'],
            'months': x['months'], 'monthly': x['monthly'], 'repaid': x['repaid'], 'bought': bool(x['bought'])}


def loan_payoff_result(x: dict, answer) -> dict:
    if not isinstance(answer, dict) or 'taken' not in answer:
        return {'exception': str(answer)}
    # a record that is not bought keeps its stale months repaid; C# has no such record
    return {key: answer[key] for key in ('taken', 'balance', 'profit', 'bought', 'money_out')} | {
        'repaid': answer['repaid'] if x['bought'] else x['repaid']}


def loan_payoff_inputs(rng: random.Random, n: int) -> list:
    def make(**kw) -> dict:
        amount = rng.choice([rng.randrange(0, 200001), 10000, 18000, 100000])
        apr = rng.choice([0, 0, rng.randrange(1, 41), 20])
        months = rng.choice([rng.randrange(1, 49), 36, 24, 1])
        monthly = loan_monthly_reading(amount, apr, months) if rng.random() < 0.7 else rng.randrange(0, 20000)
        repaid = rng.choice([0, months - 1, rng.randrange(0, months), 12 % months])
        payoff = monthly * (months - repaid)
        x = {'bal': rng.choice([rng.randrange(0, 1 << 24), 0, 50000, payoff, payoff + 1, max(payoff - 1, 0), payoff * 2]),
             'profit': rng.choice([0, rng.randrange(-30000, 30000), -2147483640, 2147483640, -(1 << 31), (1 << 31) - 1]),
             'wd': 1, 'last': 0, 'index': rng.randrange(0, 8), 'amount': amount, 'apr': apr, 'months': months,
             'monthly': monthly, 'bought': rng.choice([1, 1, 1, 0]), 'repaid': repaid}
        x['last'] = x['bal']
        x.update(kw)
        return x
    edges = [make(amount=100000, apr=20, months=36, monthly=3651, repaid=12, bal=300000, profit=0, bought=1),
             make(amount=100000, apr=0, months=36, monthly=2777, repaid=0, bal=300000, profit=0, bought=1),
             make(amount=100000, apr=0, months=36, monthly=2777, repaid=9, bal=300000, profit=-2147483600, bought=1),
             make(amount=100000, apr=20, months=36, monthly=3651, repaid=12, bal=87623, profit=0, bought=1),
             make(amount=100000, apr=20, months=36, monthly=3651, repaid=12, bal=87624, profit=0, bought=1),
             make(amount=100000, apr=20, months=36, monthly=3651, repaid=12, bal=300000, profit=0, bought=0)]
    return edges + [make() for _ in range(n)]


def loan_payoff_equal(original, other) -> bool:
    return all(original[key] == other[key] for key in other)


LOAN_PAYOFF = Target('loan-payoff', 'Early repayment 0xccc78', 'ECONOMY.md / ParkEconomy.TryRepayLoan (F13)', 0xCCC78,
                     LOAN_PAYOFF_ALLOWED, loan_take_stubs, loan_payoff_binary, loan_payoff_plan, loan_payoff_inputs,
                     alternatives={'routine-reading': loan_payoff_reading}, equal=loan_payoff_equal,
                     csharp=('loan_payoff', loan_payoff_request, loan_payoff_result),
                     notes=['Whole routine. Stubs: 0x1082b4 game -> park (sentinel, checked), 0xb678 assertion and 0x5fbcc debug '
                            'print (logged). The inputs keep the balance non-negative (the sufficiency test is unsigned) and '
                            'the withdrawals flag on; the routine reading covers the flag.',
                            'C# hook: bridge function "loan_payoff" (ParkEconomy.TryRepayLoan on an imported bank).'])
# ============================================================================== (j) QUEUE: failure predicate 0xdfe34, join gate 0xe9a84 / 0xe02ec
FAILURE_PREDICATE = 0xDFE34      # (thing) -> 1 when the thing is a relief object (+46 & 1) whose gauge +64 is below 25
FAILURE_LIMIT = 25.0             # data: TOC -11108
JOIN_DIFFERENCE = 0xE9A84        # (guest, ride) -> excitement difference, 100 when 0xdfe34 holds
JOIN_FRAGMENT = 0xECD6C          # state-10 handler 0xeccb0 from the 0xe9a84 call to the gate decision
JOIN_PASS, JOIN_REFUSED = 0xECDB0, 0xECF34  # fragment stops: past the gate / after the refusal's thought call
JOIN_STRINGS = -19316            # TOC slot -> the handler's string base (r31); the refusal logs +478 / +516 from it
JOIN_SCRIPT_CONTROLLER = 0xB5E24  # (script) -> coaster controller index of the ride's script, 0 when none
JOIN_PIECES = 0x1EA40            # (word, &p1, &p2, &p3, &p4) -> four counts for the thing behind ride +36
JOIN_REASON = 0xFB7B4            # (guest + 48, reason, guest id) log the refusal's reason
JOIN_THOUGHT = 0xFB54C           # (guest + 48, thought, 0) set the thought bubble
JOIN_CONTROLLERS = -29712        # TOC slot -> array of coaster controller pointers (read by 0x41a14)
JOIN_TYPE_ID = 0x4322
JOIN_GUEST_ID = 0x77
JOIN_STATE = type('JoinState', (), {})()
JOIN_MESSAGES = {12: 'Person: ride is not exciting enough!\n', 15: 'Person: ride is too exciting!\n'}
JOIN_ALLOWED = [(0xDFE34, 0xDFE88), (0xDFF70, 0xE02EC), (0xE02EC, 0xE03D0), (0xE1F70, 0xE1F80), (0xE2364, 0xE2370),
                (0xE2FE4, 0xE3024), (0xE9A84, 0xE9B2C), (0xEB7E4, 0xEB800), (0x41A14, 0x41A34), (0x1C552C, 0x1C5544)]


def failure_binary(sb: Sandbox, x: dict, log) -> int:
    thing = sb.struct(128, {46: ('H', x['flags']), 64: ('f', x['gauge'])})
    return sb.call(FAILURE_PREDICATE, {3: thing}).r3


def failure_plan_var_broken(x: dict) -> int:
    """The claim before F15 (QUEUE-008 / GUESTS.md "Breakdown"): the ride-failure exit is VAR_BROKEN != 0, a script variable the
    routine never reads."""
    return int(x['broken'] != 0)


def failure_plan(x: dict) -> int:
    """The corrected plan (QUEUE-008, docs/reverse/QUEUE-plan.md 5.2 step 3): a relief object whose wear gauge is below 25.
    The same as the routine reading (the 'broken' input is the script variable the routine cannot see)."""
    return failure_reading(x)


def failure_request(x: dict) -> dict:
    return {'fn': 'queue_failure', 'relief': bool(x['flags'] & 1), 'gauge_bits': bits(x['gauge'])}


def failure_reading(x: dict) -> int:
    """0xdfe34 as read: a relief object (+46 bit 0x1, type ProvidesRelief) whose gauge +64, truncated to a byte, is below 25."""
    return int(bool(x['flags'] & 1) and (fctiwz(x['gauge']) & 0xFF) < FAILURE_LIMIT)


def failure_inputs(rng: random.Random, n: int) -> list:
    def make(**kw) -> dict:
        x = {'flags': rng.choice([0, 1, 1, rng.getrandbits(16), rng.getrandbits(16) | 1, rng.getrandbits(16) & 0xFFFE]),
             'gauge': rng.choice([100.0, random_float(rng, 0, 100), random_float(rng, 0, 100), random_float(rng, 0, 30),
                                  random_float(rng, -5, 300), random_single_bits(rng)]),
             'broken': rng.choice([0, 0, 1, 1, 2, -1])}
        x.update(kw)
        return x
    edges = [make(flags=0, gauge=100.0, broken=1),   # a broken-down ordinary ride at full gauge
             make(flags=1, gauge=20.0, broken=0),    # a worn-out relief object that the script has not marked broken
             make(flags=0x4, gauge=20.0, broken=1), make(flags=1, gauge=100.0, broken=0),
             make(flags=1, gauge=24.999, broken=0), make(flags=1, gauge=25.0, broken=0), make(flags=1, gauge=25.5, broken=0),
             make(flags=1, gauge=0.0, broken=0), make(flags=1, gauge=-0.5, broken=0), make(flags=1, gauge=-1.0, broken=0),
             make(flags=1, gauge=255.5, broken=0), make(flags=1, gauge=256.0, broken=0), make(flags=1, gauge=256.9, broken=0),
             make(flags=1, gauge=281.0, broken=0), make(flags=1, gauge=1e10, broken=0), make(flags=1, gauge=-1e10, broken=0),
             make(flags=1, gauge=math.nan, broken=0), make(flags=1, gauge=math.inf, broken=0),
             make(flags=1, gauge=-math.inf, broken=0), make(flags=0xFFFF, gauge=10.0, broken=0),
             make(flags=0xFFFE, gauge=10.0, broken=0)]
    return edges + [make() for _ in range(n)]


def join_setup(sb: Sandbox, x: dict):
    """The ride, guest, type record and settings word for one input; the stubs read the same state."""
    st = JOIN_STATE
    st.x = x
    st.registry = toc_pointer(sb, POINTS_REGISTRY)
    level = x['rlevel']
    st.record = sb.struct(4096, {156: ('I', x['track'] & MASK32), 316: ('I', x['level'] & MASK32),
                                 1960: ('I', x['kind'] & MASK32), 416 + 64 * level: ('I', x['init_dur'] & MASK32),
                                 424 + 64 * level: ('I', x['init_speed'] & MASK32)})
    st.script = sb.alloc(64)
    st.ride = sb.struct(512, {14: ('H', JOIN_TYPE_ID), 32: ('I', st.script), 36: ('I', x['thing36'] & MASK32),
                              46: ('H', x['flags']), 64: ('f', x['gauge']), 76: ('B', level), 84: ('I', x['speed'] & MASK32),
                              88: ('B', x['dur']), 388: ('I', x['goods'] & MASK32), 396: ('I', x['win'] & MASK32),
                              400: ('I', x['price'] & MASK32)})
    st.guest = sb.struct(1024, {476: ('H', JOIN_GUEST_ID), 496: ('B', x['idx'])})
    sb.write(toc_pointer(sb, ARRIVAL_SETTINGS) + 164 + 12 * x['idx'], 'I', x['pref'] & MASK32)
    if x['ctrl']:
        controller = sb.struct(512, {256: ('f', 12.5), 260: ('f', x['coaster_e'])})  # the getter must read +260, not +256
        sb.write(toc_pointer(sb, JOIN_CONTROLLERS) + 4 * x['ctrl'], 'I', controller)
    return st


def join_stubs(target: Target) -> dict:
    state = JOIN_STATE

    def lookup(m: Machine) -> None:
        if m.gpr[3] != state.registry or m.gpr[4] >> 16 != JOIN_TYPE_ID:
            raise AssertionError('0x118018 called without the registry or the ride type id')
        m.gpr[3] = state.record

    def controller(m: Machine) -> None:
        if m.gpr[3] != state.script:
            raise AssertionError('0xb5e24 called without the ride script (+32)')
        m.gpr[3] = state.x['ctrl'] & MASK32

    def pieces(m: Machine) -> None:
        outputs = [m.gpr[r] for r in (4, 5, 6, 7)]
        if m.gpr[3] != state.x['thing36'] & MASK32 or len(set(outputs)) != 4 or any(o % 4 for o in outputs):
            raise AssertionError('0x1ea40 called without the ride +36 word and four distinct word outputs')
        for address, value in zip(outputs, state.x['pieces']):
            m.memory.w32(address, value & MASK32)

    def square_root(m: Machine) -> None:
        value = bits_to_double(m.fpr[1])
        if not value >= 0:
            raise AssertionError('sqrt called with a negative or NaN argument')
        m.fpr[1] = double_to_bits(math.sqrt(value))

    def message(m: Machine) -> None:
        address, text = m.gpr[4], b''
        while m.memory.u8(address + len(text)):
            text += bytes([m.memory.u8(address + len(text))])
        target.log.append(('message', m.gpr[3], text.decode('latin-1')))

    def reason(m: Machine) -> None:
        if m.gpr[3] != state.guest + 48 or m.gpr[5] >> 16 != JOIN_GUEST_ID:  # the u16 id travels in the high half
            raise AssertionError('0xfb7b4 called without the guest +48 object and the guest id')
        target.log.append(('reason', m.gpr[4]))

    def thought(m: Machine) -> None:
        if m.gpr[3] != state.guest + 48 or m.gpr[5] != 0:
            raise AssertionError('0xfb54c called without the guest +48 object and 0')
        target.log.append(('thought', m.gpr[4]))

    return {TYPE_RECORD_LOOKUP: lookup, JOIN_SCRIPT_CONTROLLER: controller, JOIN_PIECES: pieces, 'sqrt': square_root,
            DEBUG_LOG: message, JOIN_REASON: reason, JOIN_THOUGHT: thought}


def join_difference_binary(sb: Sandbox, x: dict, log) -> int:
    st = join_setup(sb, x)
    return s32(sb.call(JOIN_DIFFERENCE, {3: st.guest, 4: st.ride}).r3)


def join_gate_binary(sb: Sandbox, x: dict, log) -> dict:
    st = join_setup(sb, x)
    r = sb.call(JOIN_FRAGMENT, {29: st.guest, 30: st.ride, 31: toc_pointer(sb, JOIN_STRINGS)},
                stop=(JOIN_PASS, JOIN_REFUSED))
    difference = s32(r.gpr[28])
    if sb.machine.pc == sb.image.code(JOIN_PASS):
        if log:
            raise AssertionError('the gate passed with a thought logged')
        return {'outcome': 'pass', 'difference': difference}
    thoughts = [entry[1] for entry in log if entry[0] == 'thought']
    messages = [entry[2] for entry in log if entry[0] == 'message']
    reasons = [entry[1] for entry in log if entry[0] == 'reason']
    if len(thoughts) != 1 or thoughts[0] not in JOIN_MESSAGES or messages != [JOIN_MESSAGES[thoughts[0]]] \
            or reasons != [{12: 5, 15: 4}[thoughts[0]]]:
        raise AssertionError(f'unexpected refusal trace {log}')
    return {'outcome': {12: 'bored', 15: 'scared'}[thoughts[0]], 'difference': difference}


def join_is_ride(x: dict) -> bool:
    """Ride kind 0 and sideshow kind 2 (type +1960, Info.WhichUIType), the kinds C# gates."""
    return x['kind'] in (0, 2)


def join_static_excitement(x: dict) -> int:
    """C# attraction.ExcitementLevel: the .sam UsageInfo.ExcitementLevel, or the coaster's trunc(0.5 E + 50) once its controller
    exists (0 before; COAST-plan 6.1)."""
    if x['track'] == 3:
        return 0 if x['ctrl'] == 0 else fctiwz(f32(float(f32(x['coaster_e'] * 0.5)) + 50.0))
    return x['level']


def join_plan_static(x: dict) -> dict:
    """What GUESTS.md / QUEUE-plan 4 and C# claimed before F16: rides and sideshows refuse when |preferred - ExcitementLevel| >= 45;
    a preference above the ride's excitement is bored (thought 12), below is scared (thought 15)."""
    difference = x['pref'] - join_static_excitement(x)
    if join_is_ride(x) and abs(difference) >= 45:
        return {'outcome': 'bored' if difference > 0 else 'scared', 'difference': None}
    return {'outcome': 'pass', 'difference': None}


def join_plan_static_difference(x: dict) -> int:
    """The same claim for 0xe9a84's value, in its sign (ride minus preferred), unclamped and without the relief case."""
    return join_static_excitement(x) - x['pref']


def clamp_quotient(q: float) -> float:
    return 0.75 if q < 0.75 else 1.25 if q > 1.25 else q  # NaN keeps (neither compare is true)


def join_excitement_reading(x: dict, flag: int = 0) -> int:
    """0xe02ec(ride, flag) with 0xdff70 as read, in the order they run."""
    if flag:
        return x['level'] & 0xFF
    if s32(x['level']) == 0:
        return 0
    if x['kind'] == 2:  # sideshow: 20 + trunc(0.8f * ((win byte * sqrt(clamp(goods - price, 0, 100))) / 10))
        d = s32(x['goods'] - x['price'])
        d = 0 if d < 0 else 100 if d > 100 else d
        shown = f32(f32(0.800000011920929) * f32(((x['win'] & 0xFF) * math.sqrt(d)) / 10.0))
        return s32(fctiwz(shown) + 20)
    if x['track'] == 3:  # coaster: no controller 0, else trunc(E * 0.5 + 50) (fused single)
        if x['ctrl'] == 0:
            return 0
        return fctiwz(f32(float(f32(x['coaster_e'] * 0.5)) + 50.0))
    base = s32(x['level'])
    if x['thing36']:  # 60 % of the data level plus up to 40 for the pieces attached to the thing behind +36
        p1, p2, p3, _ = x['pieces']
        total = s32(3 * p3 + 2 * p1 + p2)
        bonus = 0 if total < 0 else 40 if total > 40 else total
        base = trunc_div(s32(base * 60), 100) + bonus
        base = 0 if base < 0 else 100 if base > 100 else base
    c1 = clamp_quotient(f32_div(f32(float(s32(x['speed']))), f32(float(s32(x['init_speed'])))))
    c2 = clamp_quotient(f32_div(f32(float(x['dur'] & 0xFF)), f32(float(s32(x['init_dur'])))))
    return fctiwz(f32(f32(float(base)) * f32(c1 * c2)))


def join_difference_reading(x: dict) -> int:
    """0xe9a84: 100 while 0xdfe34 holds, else clamp(excitement byte - preferred byte, -50, 50)."""
    if failure_reading(x):
        return 100
    difference = (join_excitement_reading(x) & 0xFF) - (x['pref'] & 0xFF)
    return max(-50, min(50, difference))


def join_gate_reading(x: dict) -> dict:
    """The state-10 gate: a nonzero low byte of the type's ExcitementLevel (0xe02ec(ride, 1)) and |difference| >= 45 refuse,
    below zero 'not exciting enough' (thought 12), otherwise 'too exciting' (thought 15)."""
    difference = join_difference_reading(x)
    if join_excitement_reading(x, 1) and abs(difference) >= 45:
        return {'outcome': 'bored' if difference < 0 else 'scared', 'difference': difference}
    return {'outcome': 'pass', 'difference': difference}


def join_plan(x: dict) -> dict:
    """The corrected plan (QUEUE-plan 4): the routine reading, which GUESTS.md and RideVisitorBridge now follow."""
    return join_gate_reading(x)


def join_plan_difference(x: dict) -> int:
    return join_difference_reading(x)


def join_request(fn: str):
    """The C# hook's inputs: the data of the object (kind, level, track type and controller, speed and duration words, sideshow
    goods, price and win byte, relief flag and gauge) and the guest's preference. The thing behind ride +36 is not sent: C# has none."""
    def request(x: dict) -> dict:
        return {'fn': fn, 'kind': x['kind'], 'level': s32(x['level']), 'track': x['track'], 'ctrl': x['ctrl'] != 0,
                'e_bits': bits(x['coaster_e']), 'speed': s32(x['speed']), 'init_speed': s32(x['init_speed']), 'dur': x['dur'],
                'init_dur': s32(x['init_dur']), 'goods': s32(x['goods']), 'price': s32(x['price']), 'win': x['win'] & 0xFF,
                'relief': bool(x['flags'] & 1), 'gauge_bits': bits(x['gauge']), 'pref': s32(x['pref'])}
    return request


def join_gate_equal(original: dict, other: dict) -> bool:
    return original['outcome'] == other['outcome'] and (other['difference'] is None or original['difference'] == other['difference'])


def join_inputs(rng: random.Random, n: int) -> list:
    def make(**kw) -> dict:
        init_speed, init_dur = rng.randrange(1, 200), rng.randrange(1, 100)
        x = {'idx': rng.randrange(0, 6), 'pref': rng.choice([rng.randrange(0, 101)] * 3 + [30, 50, 60, 70, 80, 100]),
             'kind': rng.choice([0, 0, 0, 1, 2, 2, 3]), 'level': rng.choice([0, 30, 60, 90, rng.randrange(0, 121), rng.randrange(0, 121)]),
             'track': rng.choice([0, 0, 0, 0, 1, 2, 3, 3]), 'flags': rng.choice([0, 1, 4, 8, rng.getrandbits(16)]),
             'gauge': rng.choice([100.0, 100.0, random_float(rng, 0, 100)]), 'rlevel': rng.randrange(0, 4),
             'init_speed': init_speed, 'speed': rng.choice([init_speed, init_speed, rng.randrange(0, 300)]),
             'init_dur': init_dur, 'dur': rng.choice([init_dur, init_dur, rng.randrange(0, 200)]),
             'thing36': rng.choice([0, 0, rng.randrange(1, 60)]), 'pieces': [rng.randrange(-2, 16) for _ in range(4)],
             'goods': rng.randrange(0, 300), 'price': rng.randrange(0, 300), 'win': rng.randrange(0, 256),
             'ctrl': rng.choice([0, 1, 2, 3, 9]), 'coaster_e': random_float(rng, 0, 100)}
        x.update(kw)
        return x
    base = dict(idx=0, pref=80, kind=0, level=60, track=0, flags=0, gauge=100.0, rlevel=0, init_speed=100, speed=100, init_dur=30,
                dur=30, thing36=0, pieces=[0, 0, 0, 0], goods=60, price=35, win=50, ctrl=0, coaster_e=40.0)
    edges = [make(**{**base, 'kind': 2, 'level': 30, 'pref': 80}),   # a sideshow: formula 20 + 0.8 * 50 * 5 / 10 = 40
             make(**base),                                           # an ordinary ride at its data excitement
             make(**{**base, 'level': 90, 'thing36': 5, 'pieces': [3, 2, 1, 7]}),  # attached pieces: 0.6 * 90 + 11
             make(**{**base, 'level': 100, 'pref': 50, 'speed': 150}),   # speed ratio 1.5 clamps to 1.25
             make(**{**base, 'level': 100, 'pref': 50, 'speed': 10}),    # speed ratio 0.1 clamps to 0.75
             make(**{**base, 'level': 0, 'pref': 80}),               # ExcitementLevel 0: no gate, no excitement
             make(**{**base, 'level': 256, 'pref': 80}),             # low byte 0: no gate, but excitement from the word
             make(**{**base, 'kind': 1, 'level': 20, 'pref': 80}),   # a shop with an excitement level: gated
             make(**{**base, 'track': 3, 'ctrl': 2, 'coaster_e': 38.0, 'pref': 90}),  # coaster: trunc(0.5 * 38 + 50) = 69
             make(**{**base, 'track': 3, 'ctrl': 0, 'pref': 90}),    # coaster without a controller: 0
             make(**{**base, 'level': 50, 'pref': 80, 'flags': 1, 'gauge': 10.0}),  # worn-out relief object: 100
             make(**{**base, 'level': 50, 'pref': 20, 'flags': 1, 'gauge': 10.0}),
             make(**{**base, 'level': 50, 'pref': 95}), make(**{**base, 'level': 50, 'pref': 94}),
             make(**{**base, 'level': 50, 'pref': 5}), make(**{**base, 'level': 50, 'pref': 6}),
             make(**{**base, 'level': 50, 'pref': 400}), make(**{**base, 'level': 50, 'pref': 300 + 80}),
             make(**{**base, 'level': 50, 'pref': 80, 'dur': 0, 'init_dur': 0}), make(**{**base, 'level': 50, 'init_speed': 0, 'speed': 0}),
             make(**{**base, 'level': 0x7FFFFFFF, 'pref': 3}), make(**{**base, 'level': -3, 'pref': 3}),
             make(**{**base, 'kind': 2, 'goods': 1000, 'price': -5, 'win': 255, 'level': 30, 'pref': 3}),
             make(**{**base, 'kind': 2, 'goods': 10, 'price': 40, 'win': 255, 'level': 30, 'pref': 80})]
    return edges + [make() for _ in range(n)]




# ============================================================================== (k) QUEUE: ride update 0xe0a8c (F15)
RIDE_UPDATE = 0xE0A8C            # (ride): dispatch on the ride state +408
RIDE_UPDATE_CALLS = {0xE1864: 'admit', 0xEDA04: 'force', 0xDCEBC: 'remove', 0xEE804: 'dismiss', 0xE1758: 'release'}
SCRIPT_OBJECT, SCRIPT_VARIABLE = 0xB5758, 0xB5BE0   # (script +32) -> variable accessor; (accessor, index) -> value
THING_ID_OF = 0xFA9A4            # (out, thing): writes the thing's id for a log line; no effect on the result
NO_THING = -19732                # TOC slot -> u16 "no thing" id compared with the head of the queue
RIDE_UPDATE_STATE = type('RideUpdateState', (), {})()


def ride_update_stubs(target: Target) -> dict:
    state = RIDE_UPDATE_STATE

    def accessor(m: Machine) -> None:
        if m.gpr[3] != state.script:
            raise AssertionError('0xb5758 called without the ride script (+32)')
        m.gpr[3] = state.script

    def variable(m: Machine) -> None:
        if m.gpr[3] != state.script or m.gpr[4] != 0:
            raise AssertionError('0xb5be0 called for something other than variable 0 (VAR_LETMEON)')
        m.gpr[3] = state.x['letmeon'] & MASK32

    def assertion(m: Machine) -> None:
        if m.gpr[3] & 0xFF == 0:
            target.log.append(('assert',))

    def thing_id(m: Machine) -> None:
        m.memory.w16(m.gpr[3], 0x1234)

    def record(name):
        def stub(m: Machine) -> None:
            if name == 'remove':
                target.log.append((name, m.gpr[3] == state.ride, m.gpr[4] >> 16))  # the u16 id travels in the high half
            elif name == 'dismiss' or name == 'force':
                target.log.append((name, m.gpr[3] == state.guest))
            else:
                target.log.append((name, m.gpr[3] == state.ride))
        return stub

    stubs = {SCRIPT_OBJECT: accessor, SCRIPT_VARIABLE: variable, ASSERT: assertion, THING_ID_OF: thing_id, DEBUG_LOG: lambda m: None}
    stubs.update({address: record(name) for address, name in RIDE_UPDATE_CALLS.items()})
    return stubs


def ride_update_binary(sb: Sandbox, x: dict, log) -> list:
    st = RIDE_UPDATE_STATE
    st.x = x
    if sb.read(toc_pointer(sb, NO_THING), 'H') != 0:
        raise AssertionError('the "no thing" id is not zero')
    st.script = sb.alloc(64)
    st.guest = sb.struct(1024, {544: ('I', x['head_state'] & MASK32)})
    st.ride = sb.struct(512, {32: ('I', st.script), 56: ('H', x['head']), 408: ('I', x['state'] & MASK32)})
    if x['head']:
        sb.write(toc_pointer(sb, POINTS_THINGS) + 20 * x['head'], 'I', st.guest)
    sb.call(RIDE_UPDATE, {3: st.ride})
    return list(log)


def ride_update_plan(x: dict) -> list:
    """The ride update as read: state 0 runs the admission, 3 does nothing, 1, 2 and 4 (broken, closing, condemned) hand the head
    guest on, and any other state asserts. A head the script already took (VAR_LETMEON no longer names it) and that waits in state 14
    is forced aboard; otherwise a head guest is removed from the queue and dismissed; then the script's release is handled."""
    state = x['state']
    if state == 3:
        return []
    if state == 0:
        return [('admit', True)]
    if state not in (1, 2, 4):
        return [('assert',)]
    head, calls = x['head'], []
    if head != 0 and head != x['letmeon'] and x['head_state'] == 14:
        calls.append(('force', True))
    elif head != 0:
        calls += [('remove', True, head), ('dismiss', True)]
    return calls + [('release', True)]


def ride_update_inputs(rng: random.Random, n: int) -> list:
    def make(**kw) -> dict:
        head = rng.choice([0, rng.randrange(1, 200)])
        x = {'state': rng.choice([0, 1, 1, 2, 3, 4, 4, 5, 6, -1, 7]), 'head': head,
             'letmeon': rng.choice([0, head, head, rng.randrange(1, 300)]) if head else 0,
             'head_state': rng.choice([14, 14, 11, 13, 6, 16])}
        x.update(kw)
        return x
    edges = [make(state=1, head=5, letmeon=5, head_state=14),      # broken, the head still waits for the script: dismissed
             make(state=1, head=5, letmeon=0, head_state=14),      # broken, the script took the head: forced aboard
             make(state=1, head=5, letmeon=0, head_state=11),      # broken, the head stands in the queue: dismissed
             make(state=4, head=9, letmeon=0, head_state=11),      # condemned: the same
             make(state=2, head=9, letmeon=3, head_state=11), make(state=1, head=0, letmeon=0, head_state=11),
             make(state=0, head=5, letmeon=0, head_state=11), make(state=3, head=5, letmeon=5, head_state=14),
             make(state=5, head=5, letmeon=0, head_state=11), make(state=-1, head=0, letmeon=0, head_state=11)]
    return edges + [make() for _ in range(n)]


# ============================================================================== (l) ECON: what a shop sale does to the guest (F18)
# The sale routine 0xeaaf8 (guest, shop thing): after the "used shop" log it fetches the shop's type record (0xdc320) and adds the five
# UsageInfo effects of that record to the guest's five need meters, each one clamped to 0..100 in single precision:
#   type +0x144 ThirstEffect    -> guest +0x1a4 (subtracted)      type +0x14c VomitEffect     -> guest +0x1b0 (added)
#   type +0x148 HungerEffect    -> guest +0x1a8 (subtracted)      type +0x150 HappinessEffect -> guest +0x19c (added)
#   type +0x154 LitterEffect    -> guest +0x1b4 (added)
# The fragment 0xeabb8..0xeae44 runs the type lookup, the five updates and the "litter gone up" log; the later parts of the routine
# (ingredient and appearance effects, state change) are not executed. The key-to-offset order comes from the key table of the shop
# files (ThirstEffect, HungerEffect, VomitEffect, HappinessEffect, LitterEffect, SpecialIngredient follow InitCostOfGoods at +0x140,
# the order the original's Shops.sam comments give: "How much thirst to deduct" ... "How much litter to add").
SALE_FRAGMENT, SALE_STOP = 0xEABB8, 0xEAE44
SALE_ALLOWED = [(0xEABB8, 0xEAE48), (0xDC320, 0xDC350)]   # the stop 0xeae44 is reached by a stub return, so it lies inside the range
SALE_TYPE_ID = 0x4321
SALE_SELLER_ID = 0x4D
SALE_STRINGS, SALE_GLOBALS = -19392, -30176   # TOC slots loaded into r30 (string base) and r25 (a global struct) by the prologue
SALE_STATE = type('SaleState', (), {})()
SALE_METERS = (('thirst', 420, 324, -1), ('hunger', 424, 328, -1), ('vomit', 432, 332, 1), ('happy', 412, 336, 1), ('litter', 436, 340, 1))


def sale_stubs(target: Target) -> dict:
    state = SALE_STATE

    def lookup(m: Machine) -> None:
        if m.gpr[3] != state.registry or m.gpr[4] >> 16 != SALE_TYPE_ID:   # the u16 id travels in the high half
            raise AssertionError('0x118018 called without the registry or the shop type id')
        m.gpr[3] = state.record

    def drink_or_food(m: Machine) -> None:   # 0xe6d20(guest, [globals + 4], 131 | 132): which of thirst and hunger the shop serves more
        if m.gpr[3] != state.guest:
            raise AssertionError('0xe6d20 called without the guest')
        target.log.append(('serves', m.gpr[5]))

    def message(m: Machine) -> None:
        address, text = m.gpr[4], b''
        while m.memory.u8(address + len(text)):
            text += bytes([m.memory.u8(address + len(text))])
        if not text.startswith(b'Litter'):
            raise AssertionError('0x5fbcc called with an unexpected format')
        target.log.append(('litter-log', m.gpr[3], s32(m.gpr[5]), m.gpr[6]))

    return {TYPE_RECORD_LOOKUP: lookup, 0xE6D20: drink_or_food, DEBUG_LOG: message}


def sale_binary(sb: Sandbox, x: dict, log) -> dict:
    st = SALE_STATE
    st.registry = toc_pointer(sb, POINTS_REGISTRY)
    fields = {324 + 4 * i: ('i', s32(x['effects'][i])) for i in range(5)}
    st.record = sb.struct(4096, fields)
    st.shop = sb.struct(256, {14: ('H', SALE_TYPE_ID)})
    st.guest = sb.struct(1024, {offset: ('f', x['meters'][name]) for name, offset, _, _ in SALE_METERS})
    sb.call(SALE_FRAGMENT, {3: st.shop, 25: toc_pointer(sb, SALE_GLOBALS), 28: st.guest, 30: toc_pointer(sb, SALE_STRINGS)}, stop=SALE_STOP)
    meters = {name: sb.read(st.guest + offset, 'f') for name, offset, _, _ in SALE_METERS}
    return {'meters': meters, 'log': list(log)}


def sale_clamp(value: float) -> float:
    """fcmpo against 100 first (a NaN is not greater), then against 0 (a NaN is not less): a NaN is stored as it is."""
    return 100.0 if value > 100.0 else 0.0 if value < 0.0 else value


def sale_step(before: float, effect: int, sign: int) -> float:
    amount = f32(float(s32(effect)))
    return sale_clamp(f32(before + amount) if sign > 0 else f32(before - amount))


def sale_reading(x: dict, signs: dict | None = None, unclamped: tuple = (), slots: dict | None = None, log_threshold: int = 0) -> dict:
    """The five meter updates and the log as the fragment runs them. The keywords are deliberate deviations for the sharpness tests:
    a sign or effect slot swapped per meter, meters left without the clamp, and another threshold for the "litter gone up" log."""
    meters, log = {}, []
    for index, (name, _, _, sign) in enumerate(SALE_METERS):
        sign = (signs or {}).get(name, sign)
        effect = x['effects'][(slots or {}).get(name, index)]
        amount = f32(float(s32(effect)))
        total = f32(x['meters'][name] + amount) if sign > 0 else f32(x['meters'][name] - amount)
        meters[name] = total if name in unclamped else sale_clamp(total)
    thirst, hunger = s32(x['effects'][0]), s32(x['effects'][1])
    if thirst > hunger:
        log.append(('serves', 131))
    elif thirst < hunger:
        log.append(('serves', 132))
    litter_effect = s32(x['effects'][4])
    if litter_effect > log_threshold:
        log.append(('litter-log', 2, litter_effect, fctiwz(meters['litter']) & 0xFF))
    return {'meters': meters, 'log': log}


def sale_equal(original: dict, other: dict) -> bool:
    if not all(same_float(original['meters'][name], value) for name, value in other['meters'].items()):
        return False
    return 'log' not in other or original['log'] == other['log']


def sale_plan(x: dict) -> dict:
    """The rule before F18 (ECONOMY.md, ParkEconomy.TryBuy): a sale adds LitterEffect, in 1/100 litter items, to the litter store. In
    the guest's meter units that is before + effect, with no floor and no ceiling."""
    return {'meters': {'litter': f32(x['meters']['litter'] + f32(float(s32(x['effects'][4]))))}}


def sale_csharp_request(x: dict) -> dict:
    return {'fn': 'shop_sale_litter', 'effect': s32(x['effects'][4])}


def sale_csharp_result(x: dict, delta) -> dict:
    """C# adds `delta` scaled litter units to the park's store per sale; its store has no per-guest meter, so the guest's meter before
    the sale (the oracle's input) is carried over: before + delta, in single precision."""
    return {'meters': {'litter': f32(x['meters']['litter'] + f32(float(delta)))}}


def sale_inputs(rng: random.Random, n: int) -> list:
    shipped = [0, 0, 10, 25, 50, 50, 5]    # the LitterEffect values of the shipped shop files (0, 10, 25, 50) and the category default 5

    def meter() -> float:
        return rng.choice([0.0, 0.0, 100.0, random_float(rng, 0, 100), random_float(rng, 0, 100), random_float(rng, 0, 100),
                           random_float(rng, 80, 100), random_float(rng, -20, 130)])

    def effect() -> int:
        return rng.choice(shipped + [rng.randrange(-100, 400), rng.randrange(0, 60), rng.randrange(-2 ** 31, 2 ** 31)])

    def make(**kw) -> dict:
        x = {'meters': {name: meter() for name, _, _, _ in SALE_METERS}, 'effects': [effect() for _ in range(5)]}
        x.update(kw)
        return x

    def with_litter(before: float, effect_value: int, **kw) -> dict:
        meters = {'thirst': 40.0, 'hunger': 40.0, 'vomit': 10.0, 'happy': 50.0, 'litter': before}
        return make(meters=meters, effects=[20, 30, 5, 10, effect_value], **kw)
    edges = [with_litter(0.0, 50),            # a burger or drinks sale for a clean guest: litter 50 (the guest drops at 90)
             with_litter(50.0, 50),           # the second sale: 100, the ceiling
             with_litter(60.0, 50),           # 110 clamps to 100: the surplus is lost
             with_litter(0.0, 25), with_litter(75.0, 25), with_litter(80.0, 25),
             with_litter(0.0, 10), with_litter(85.0, 10), with_litter(90.0, 0),
             with_litter(10.0, -30),          # a negative effect lowers the meter, floor 0
             with_litter(10.0, -3), with_litter(0.0, -1),
             with_litter(99.5, 1), with_litter(100.0, 1), with_litter(0.0, 2 ** 31 - 1), with_litter(100.0, -2 ** 31),
             with_litter(16777217.0, 1), with_litter(0.0, 16777217),   # single-precision conversion of a 25-bit effect
             with_litter(math.nan, 5), with_litter(math.inf, 5), with_litter(-math.inf, 5), with_litter(-0.0, 0),
             make(effects=[50, 10, 0, 0, 0]),   # a drinks shop: thirst effect above hunger effect
             make(effects=[10, 50, 0, 0, 0]), make(effects=[20, 20, 0, 0, 5]), make(effects=[-5, 0, 0, 0, 5])]
    return edges + [make() for _ in range(n)]


ECON_SALE = Target('shop-sale-litter', 'Shop sale effects on the guest (fragment 0xeabb8 of 0xeaaf8)', 'ECONOMY.md "UsageInfo.LitterEffect" / ParkEconomy.TryBuy',
                   SALE_FRAGMENT, SALE_ALLOWED, sale_stubs, sale_binary, sale_plan, sale_inputs, equal=sale_equal,
                   alternatives={'routine-reading': sale_reading},
                   csharp=('shop_sale_litter', sale_csharp_request, sale_csharp_result),
                   notes=['C# hook: bridge function "shop_sale_litter" (ParkEconomy.TryBuy on a one-shop economy; the delta of LitterScaled). C# keeps '
                          'a park-wide store, not a guest meter, so the guest\'s meter before the sale is carried over: after = before + delta.',
                          'Executed: the type record getter 0xdc320 and the fragment 0xeabb8..0xeae44 (thirst, hunger, vomit, happiness and litter '
                          'updates with their 0..100 clamps, and the "litter gone up" log). Stubs: 0x118018 type record (synthetic, registry and '
                          'type id checked), 0xe6d20 (which of drink and food the shop serves: logged), 0x5fbcc debug log (arguments logged).',
                          'The guest drops the litter later (meter byte >= 90 in 0xebd20); that and the ingredient and appearance effects are not '
                          'executed.'])


# The trigger of the guest's litter drop (0xebd20, in the guest update 0xeb... that switches on the guest state): the meter +0x1b4 is
# truncated to an integer, taken as a byte and compared with 90 (cmplwi 90, branch when lower). At or above 90 the update goes on to the
# state check (+544 in 6, 7 or 10) and the drop; below it jumps to 0xebdfc. Only the threshold is executed here.
LITTER_TRIGGER, LITTER_BELOW, LITTER_AT_OR_ABOVE = 0xEBD20, 0xEBDFC, 0xEBD3C


def trigger_binary(sb: Sandbox, x: dict, log) -> bool:
    guest = sb.struct(1024, {436: ('f', x['litter'])})
    sb.call(LITTER_TRIGGER, {1: STACK_BASE + STACK_SIZE - 0x1000, 31: guest}, stop=(LITTER_AT_OR_ABOVE, LITTER_BELOW))
    return sb.machine.pc == sb.image.code(LITTER_AT_OR_ABOVE)


def trigger_reading(x: dict) -> bool:
    return (fctiwz(x['litter']) & 0xFF) >= 90


def trigger_inputs(rng: random.Random, n: int) -> list:
    edges = [{'litter': value} for value in (0.0, 89.0, 89.999, 90.0, 90.5, 99.0, 100.0, 255.9, 256.0, 345.9, 346.0, -1.0, -0.5, -166.0, -165.0,
                                              1e10, -1e10, math.nan, math.inf, -math.inf, 50.0)]
    return edges + [{'litter': rng.choice([random_float(rng, 0, 100), random_float(rng, 80, 100), random_float(rng, -400, 400),
                                           float(rng.randrange(80, 100))])} for _ in range(n)]


ECON_TRIGGER = Target('guest-litter-trigger', 'Guest litter drop threshold (0xebd20)', 'ORACLE F18 / ECONOMY.md "LitterEffect"', LITTER_TRIGGER,
                      [(LITTER_TRIGGER, LITTER_BELOW + 4)], lambda t: {}, trigger_binary, None, trigger_inputs,
                      alternatives={'routine-reading': trigger_reading},
                      notes=['No plan side and no C# hook: OpenTPW has no guest litter meter, so no threshold (ECON-020).',
                             'Fragment: the first lines of the litter test in the guest update. Executed alone (no stubs); the run ends at 0xebd3c (the '
                             'meter byte is 90 or more: the state check and the drop follow) or at 0xebdfc (below 90).'])


# ============================================================================== (k) STAFF: the monthly training split 0xfc1c4
TRAIN_ENTRY = 0xFC1C4          # (budget words) month-end training: the five role budgets are split over the staff things
TRAIN_NEXT_GLUE = (0x10507C, 0x105110)  # first / next thing id of the staff list (header +4, thing-array entry +8 -> node +4)
TRAIN_ONE = 0xF3588            # (staff thing, amount) trains one member; also the debit 0xcbfdc and the statistic
TRAIN_LIST_HEADER = -26704     # TOC slot -> variable holding the staff list header (its +4 u16 is the first thing id)
TRAIN_SETTINGS = -30084        # TOC slot -> settings block: per-role PoundsPerTrainingPoint arrays (offset, stride below)
TRAIN_GAME_SLOTS = (-30124, -30080)  # TOC slots -> variables holding the game pointer (0xf3588 / 0xcbfdc and 0xfc1c4)
TRAIN_BASE_TABLE = -19008      # TOC slot -> the five-word table 0xfc1c4 copies into its role counters (static, zero in the file)
TRAIN_ROLE_CLASS = (5, 4, 6, 7, 8)  # StaffType order (handyman 0 ... researcher 4) -> thing class byte +2 (SAVE-plan models)
TRAIN_PRICE_FIELDS = {4: (1008, 8), 5: (948, 12), 6: (868, 16), 7: (1116, 8), 8: (1056, 12)}  # class -> (offset, stride)
TRAIN_BANK_ID, TRAIN_PARK_ID, TRAIN_FIRST_STAFF_ID = 1, 2, 3
TRAIN_START_BALANCE = 1_000_000
TRAIN_POINTS = 100             # training points per grade: 0xf3588 promotes when the byte +488 would pass 99
TRAIN_TOP_GRADE = 4


def train_stubs(target: Target) -> dict:
    def assertion(m: Machine) -> None:
        target.log.append(('assert', m.gpr[3] & 0xFF))

    def debug(m: Machine) -> None:
        target.log.append(('log', m.gpr[3]))

    return {ASSERT: assertion, DEBUG_LOG: debug}


def train_binary(sb: Sandbox, x: dict, log) -> dict:
    """Runs 0xfc1c4 over a staff list built in memory: the list header, the thing array, the settings prices, a bank thing
    and a park thing (both reached through the game's thing ids) so that the debit 0xcbfdc and the statistics run too."""
    base = toc_pointer(sb, TRAIN_BASE_TABLE)
    if [sb.read(base + 4 * i, 'i') for i in range(5)] != [0] * 5:
        raise AssertionError('the role counter table behind TOC -19008 is not zero in the file')
    game = sb.alloc(0x1E0000)
    for slot in TRAIN_GAME_SLOTS:
        sb.write(toc_pointer(sb, slot), 'I', game)
    things = toc_pointer(sb, POINTS_THINGS)
    bank = sb.struct(512, {12: ('i', TRAIN_START_BALANCE), 276: ('i', 1 if x.get('active', True) else 0)})
    park = sb.struct(ARRIVAL_PARK_SIZE)
    sb.write(things + 20 * TRAIN_BANK_ID, 'I', bank)
    sb.write(things + 20 * TRAIN_PARK_ID, 'I', park)
    sb.write(game + 0x1E0000 - 22746, 'H', TRAIN_BANK_ID)
    sb.write(game + 0x1E0000 - 22752, 'H', TRAIN_PARK_ID)
    sb.write(game + 0x1E0000 - 22772, 'I', 1)
    settings = toc_pointer(sb, TRAIN_SETTINGS)
    for role, cls in enumerate(TRAIN_ROLE_CLASS):
        offset, stride = TRAIN_PRICE_FIELDS[cls]
        for grade, price in enumerate(x['prices'][role]):
            sb.write(settings + offset + stride * grade, 'i', price)
    objects = []
    ids = [TRAIN_FIRST_STAFF_ID + n for n in range(len(x['staff']))]
    for n, member in enumerate(x['staff']):
        cls = member['class'] if member['role'] is None else TRAIN_ROLE_CLASS[member['role']]
        following = ids[n + 1] if n + 1 < len(ids) else 0
        node = sb.struct(16, {4: ('I', following)})
        obj = sb.struct(536, {0: ('H', ids[n]), 2: ('B', cls), 484: ('i', member['grade']), 488: ('B', member['points'])})
        sb.write(things + 20 * ids[n], 'I', obj)
        sb.write(things + 20 * ids[n] + 8, 'I', node)
        objects.append(obj)
    header = sb.struct(16, {4: ('I', ids[0] if ids else 0)})
    sb.write(toc_pointer(sb, TRAIN_LIST_HEADER), 'I', header)
    budgets = sb.struct(32)
    for role, budget in enumerate(x['budgets']):
        sb.write(budgets + 4 * role, 'i', budget)
    sb.call(TRAIN_ENTRY, {3: budgets})
    if any(kind == 'assert' and value == 0 for kind, value in log):
        raise AssertionError('an assertion failed while training')
    if [sb.read(budgets + 4 * role, 'i') for role in range(5)] != list(x['budgets']):
        raise AssertionError('the budgets were written')
    balance = TRAIN_START_BALANCE - sb.read(bank + 12, 'i')
    profit, month = -sb.read(bank + 292, 'i'), sb.read(park + 0x20000 - 2656, 'i')
    booked = sb.read(park + 0x20000 - 1472, 'i')
    if x.get('active', True) and len({balance, profit, month, booked}) != 1:
        raise AssertionError(f'the debit and the statistics disagree: {balance} {profit} {month} {booked}')
    return {'staff': [[sb.read(obj + 484, 'i'), sb.read(obj + 488, 'B')] for obj in objects], 'spent': balance,
            'booked': booked}


def train_current(x: dict) -> dict:
    """The rule before F17 (ParkStaff.Train, ECON-013, ECONOMY.md): only members that can still train (below the top grade, a
    positive price) share the budget; each buys min(share / price, points left to the next grade) at its price; the grade
    rises at 100 points with the points reset to 0; only what was bought is spent."""
    staff = [[member['grade'], member['points']] for member in x['staff']]
    spent = 0
    for role in range(5):
        budget = x['budgets'][role]
        trainees = [n for n, member in enumerate(x['staff']) if member['role'] == role and staff[n][0] < TRAIN_TOP_GRADE
                    and x['prices'][role][staff[n][0]] > 0]
        if not trainees or budget <= 0:
            continue
        share = budget // len(trainees)
        for n in trainees:
            price = x['prices'][role][staff[n][0]]
            points = min(share // price, TRAIN_POINTS - staff[n][1])
            spent += points * price
            staff[n][1] += points
            if staff[n][1] >= TRAIN_POINTS:
                staff[n][0] += 1
                staff[n][1] = 0
    return {'staff': staff, 'spent': spent, 'booked': spent}


def train_reading(x: dict, count_all: bool = True, spend_share: bool = True, carry: bool = True, cap: bool = True,
                  base: int = 0, skip_top: bool = True) -> dict:
    """The routine as read from 0xfc1c4 / 0xf3588: each role's budget is divided (signed) by the number of ALL staff of that
    role (the base counts are zero) and every member below the top grade is trained with that share: the whole share is debited,
    min(share / price, 100) points are added, and the points above 99 carry into the next grade. The keyword flags build the
    refuted variants used by the sharpness tests."""
    staff = [[member['grade'], member['points']] for member in x['staff']]
    spent = 0
    for role in range(5):
        members = [n for n, member in enumerate(x['staff']) if member['role'] == role]
        if not members:
            continue
        if count_all:
            divisor = len(members) + base
        else:
            divisor = len([n for n in members if staff[n][0] < TRAIN_TOP_GRADE]) or 1
        share = trunc_div(x['budgets'][role], divisor)
        for n in members:
            if skip_top and staff[n][0] == TRAIN_TOP_GRADE:
                continue
            price = x['prices'][role][staff[n][0]]
            points = trunc_div(share, price)
            if cap:
                points = min(points, TRAIN_POINTS)
            else:
                points = min(points, TRAIN_POINTS - staff[n][1])
            spent += share if spend_share else points * price
            total = staff[n][1] + points
            if total > TRAIN_POINTS - 1:
                staff[n][0] += 1
                staff[n][1] = total - TRAIN_POINTS if carry else 0
            else:
                staff[n][1] = total
    return {'staff': staff, 'spent': spent, 'booked': spent}


def train_request(x: dict) -> dict:
    return {'fn': 'staff_training', 'budgets': x['budgets'], 'prices': x['prices'],
            'staff': [{'role': member['role'], 'grade': member['grade'], 'points': member['points']}
                      for member in x['staff'] if member['role'] is not None]}


def train_result(x: dict, r: dict) -> dict:
    """The bridge returns the real members in input order and what ParkStaff.Train reports as spent (booked as the same
    staff cost); the foreign things the list may carry are never touched by either side."""
    real = iter(r['staff'])
    staff = [[member['grade'], member['points']] if member['role'] is None else list(next(real)) for member in x['staff']]
    return {'staff': staff, 'spent': r['spent'], 'booked': r['spent']}


def train_inputs(rng: random.Random, n: int) -> list:
    def make_staff(role=None, grade=None, points=None) -> dict:
        if role is None and rng.random() < 0.06:
            return {'role': None, 'class': rng.choice([1, 3, 9]), 'grade': 0, 'points': 0}
        return {'role': rng.randrange(5) if role is None else role, 'class': 0,
                'grade': rng.choice([0, 1, 2, 3, 4, rng.randrange(5)]) if grade is None else grade,
                'points': rng.choice([0, 0, 50, 99, rng.randrange(100)]) if points is None else points}

    def make(**kw) -> dict:
        prices = [[rng.choice([1, 5, 8, 12, 15, rng.randrange(1, 60)]) for _ in range(5)] for _ in range(5)]
        staff = [make_staff() for _ in range(rng.choice([0, 1, 2, 3, 4, 6, 9, rng.randrange(0, 13)]))]
        budgets = []
        for role in range(5):
            price = prices[role][0]
            budgets.append(rng.choice([0, 0, price * rng.randrange(0, 150), rng.randrange(0, 5000), rng.randrange(0, 200000),
                                       2 ** 28]))
        x = {'budgets': budgets, 'prices': prices, 'staff': staff, 'active': True}
        x.update(kw)
        return x

    flat = [[5, 8, 12, 15, 1]] * 5
    handymen = [make_staff(0, g, 0) for g in (0, 0, 4)]
    mechanics = [make_staff(1, 1, 0), make_staff(1, 1, 0)]
    edges = [make(budgets=[0] * 5, staff=[]),
             # A budget of 1,000 over 3 handymen (grades 0, 0 and 4) and 2 mechanics (both grade 1): the divisor counts the
             # top-grade handyman, so 333 each for the two trainees (the third share is not spent); 500 each for the mechanics.
             make(budgets=[1000, 1000, 0, 0, 0], prices=flat, staff=handymen + mechanics),
             # The share is below the price: no points, the share is still debited.
             make(budgets=[4, 0, 0, 0, 0], prices=flat, staff=[make_staff(0, 0, 0)]),
             # A share above 100 points: capped at 100, the rest of the share is still debited.
             make(budgets=[5000, 0, 0, 0, 0], prices=flat, staff=[make_staff(0, 0, 0)]),
             # Points carry into the next grade: 99 + 60 = 159 gives grade + 1 and 59 points.
             make(budgets=[300, 0, 0, 0, 0], prices=flat, staff=[make_staff(0, 0, 99)]),
             # Everyone at the top grade: nothing is spent.
             make(budgets=[1000] * 5, prices=flat, staff=[make_staff(role, 4, 0) for role in range(5)]),
             # A foreign thing in the list does not count.
             make(budgets=[600, 0, 0, 0, 0], prices=flat,
                  staff=[make_staff(0, 0, 0), {'role': None, 'class': 3, 'grade': 0, 'points': 0}, make_staff(0, 1, 0)])]
    return edges + [make() for _ in range(n)]


# ============================================================================== registry
LCG =Target('det-world-lcg', 'World LCG step 0x105328', 'DET-plan §2.3 / det_model.world_next', 0x105328,
             [(0x105328, 0x10536C)], lambda t: {}, lcg_binary, lcg_plan, lcg_inputs,
             notes=['C# hook: bridge function "world_rng_next" (DET-I2 WorldRng); main has no port yet (DET-plan D5-D7).'])
RAND = Target('det-rse-rand', 'RSE RAND call site 0xb07f8..0xb0828', 'DET-plan §4.3 / det_model.rse_rand', 0xB07F8,
              [(0xB07F8, 0xB0828), (0x105328, 0x10536C), (0x1C5C04, 0x1C5C1C)], lambda t: {'labs': labs_stub},
              rand_binary, rand_plan, rand_inputs,
              notes=['Fragment of the RSE interpreter; import glue 0x1c5c04 -> labs is stubbed (C labs, 32-bit wrap).',
                     'C# hook: bridge function "rse_rand"; main draws from System.Random per VM (DET-plan D7).'])
QUEUE = Target('queue-limit', 'Queue limit 0xdcb74', 'QUEUE-plan §4 step 3', 0xDCB74,
               [(0xDCB74, 0xDCD34), (0x1C3FBC, 0x1C400C)], queue_stubs, queue_binary, queue_plan, queue_inputs,
               csharp=('queue_limit', queue_csharp_request, queue_csharp_result))
CAN_CHANGE_TARGET = Target('path-can-change', 'CanChangeCellType 0x8408c', 'PATH-plan §3.1', 0x8408C, [CAN_CHANGE],
                           lambda t: {}, can_change_binary, can_change_plan, can_change_inputs,
                           csharp=('can_change_cell_type',
                                   lambda x: {'fn': 'can_change_cell_type', 'old': x[0], 'type': x[1], 'last_cell': x[2] != 0},
                                   lambda x, r: r))
SET_CELL = Target('path-set-cell-type', 'SetCellType pricing 0x82ac4', 'PATH-plan §3.2', 0x82AC4,
                  [(0x82AC4, 0x82D5C), CAN_CHANGE, (0x7BC58, 0x7BC7C), (0xCBF48, 0xCBF50)], set_cell_stubs,
                  set_cell_binary, set_cell_plan, set_cell_inputs, csharp=('set_cell_type', set_cell_request, lambda x, r: r),
                  notes=['Domain: free byte data:0x7de2d clear, game+36 == 0, bank +276 != 0; old 1 -> new 3 left to '
                         'path-can-change.'])
UNBOUNCE = Target('board-unbounce', 'UNBOUNCE release 0xae020', 'BOARD-plan §5', 0xAE020, [(0xAE020, 0xAE114)],
                  unbounce_stubs, unbounce_binary, unbounce_plan, unbounce_inputs,
                  csharp=('unbounce', unbounce_request, lambda x, r: r))
BOUNCE = Target('board-bounce', 'BOUNCE slot 0xadf40', 'BOARD-plan §5', 0xADF40, [(0xADF40, 0xAE020)],
                unbounce_stubs, bounce_binary, bounce_plan, bounce_inputs,
                csharp=('bounce', bounce_request, lambda x, r: r),
                notes=['The slot word +4 (index + script +112) is not compared; the C# bridge has no such field.',
                       'Domain: now + 1000 x dur stays inside 32 bits (the bridge keeps double times).'])
SPEED_SETTER = Target('walk-speed-setter', 'Guest speed setter 0xffe38', 'WALK-plan §1', 0xFFE38, [(0xFFE38, 0xFFEB0)],
                      lambda t: {}, speed_setter_binary, speed_setter_plan, speed_setter_inputs,
                      csharp=('guest_speed_limits', speed_setter_request, lambda s, r: r),
                      notes=['C# hook: bridge function "guest_speed_limits" (WALK-I GuestSpeed.Caps).'])
SMOOTHING = Target('walk-smoothing', 'Guest speed smoothing 0xe6b28', 'WALK-plan §4', 0xE6B28,
                   [(0xE6B28, 0xE6BFC), (0xFFE38, 0xFFEB0)], smoothing_stubs, smoothing_binary, smoothing_plan,
                   smoothing_inputs, equal=smoothing_equal, alternatives={'plan-fused': smoothing_fused},
                   csharp=('guest_speed_smoothing', smoothing_request, smoothing_result),
                   notes=['C# hook: bridge function "guest_speed_smoothing" (WALK-I GuestSpeed.Update).'])
COAST_STEP = Target('coast-rating-step', 'Coaster rating step 0x415b4', 'COAST-plan §6.3 (coast-research2 32dfe49)',
                    0x415B4, [(0x415B4, 0x41900)], lambda t: {}, coast_step_binary, coast_step_plan, coast_step_inputs,
                    equal=coast_equal, alternatives={'plan-stale-fz': coast_step_plan_fz},
                    notes=['C# hook: bridge function "coaster_rating_step" (COAST-I).'])
COAST_FINAL = Target('coast-rating-final', 'Coaster rating final 0x41900', 'COAST-plan §6.4 (coast-research2 32dfe49)',
                     0x41900, [(0x41900, 0x41A14)], lambda t: {}, coast_final_binary, coast_final_plan,
                     coast_final_inputs, equal=coast_equal, alternatives={'plan-both-flags': coast_final_both_flags},
                     notes=['C# hook: bridge function "coaster_rating_final" (COAST-I).'])

ARRIVAL = Target('arrival-group', 'Arrival group size 0xcb4e8', 'GUESTS.md "Arrivals"', 0xCB4E8,
                 [(0xCB4E8, 0xCB698)], arrival_stubs, arrival_binary, arrival_plan, arrival_inputs,
                 alternatives={'routine-reading': arrival_full},
                 csharp=('arrival_group', arrival_csharp_request, lambda x, r: r),
                 notes=['C# hook: bridge function "arrival_group" (GuestSimulation.ArrivalGroupSize); the thing +48 word is '
                        'passed as the weather-drops-falling flag (mCurrentDrops > 0) and GameType word 1 as OnlineVisit (500 cut).',
                        'Stubs: 0x1082b4 park, 0xc1fb4 counted guests, 0xc24c0 points, 0x1083c8 factor object, '
                        '0x5fbcc debug log; the mode object is marked constructed so 0x12bb64 does not run.'])

ARRIVAL_UPDATE = Target('arrival-update', 'Arrival update 0xcb2ac', 'GUESTS.md "Arrivals"', 0xCB2AC,
                        [(0xCB028, 0xCB050), (0xCB2AC, 0xCB4E4), (0xCB4E8, 0xCB698), (0x121098, 0x1210B0),
                         (0x1210F8, 0x12113C), (0x10A9A4, 0x10A9B0)],
                        arrival_update_stubs, arrival_update_binary, arrival_update_plan, arrival_update_inputs,
                        alternatives={'routine-reading': arrival_update_reading, 'static-reading': arrival_update_static,
                                      'reading-held': arrival_update_reading_held},
                        equal=arrival_update_equal,
                        notes=['No C# hook: GuestSimulation.UpdateArrivals runs on a frame clock, not on the original phases.',
                               'Executed: 0xcb028 (constructor), 0xcb2ac, 0xcb4e8 (group size), 0x1210f8 / 0x121098 (timer '
                               'elapsed / restart), 0x10a9a4 (game pointer). Stubs: 0x109784 vehicle phase and 0x109b78 '
                               'report flag (chosen per call), 0x109368 vehicle script step (logged with its frame slot), '
                               '0xcb698 guest creation (counted, raises the counted guests), 0xcb750 group history (logged), '
                               '0xb678 assertion (logged), plus the arrival-group stubs.',
                        'Each input is a script: one 0xcb2ac call per step in the same sandbox.'])

POINTS = Target('park-points', 'Park points 0xc24c0', 'GUESTS.md "Arrivals"', 0xC24C0,
                [(0xC24C0, 0xC26DC), (0xC5828, 0xC5864), (0xD1BE8, 0xD1D1C), (0xE4750, 0xE47F8), (0x10A9A4, 0x10A9B0),
                 (0x1C4100, 0x1C4230)], points_stubs, points_binary, points_plan, points_inputs,
                alternatives={'routine-reading': points_full},
                csharp=('park_points', points_csharp_request, lambda x, r: r),
                notes=['C# hook: bridge function "park_points" (ParkEconomy.ParkPoints): track type and first-purchase turn '
                       'and the open flag (the chosen 0xdc834 result, ParkObjectState.IsOpen) are passed.',
                       'Executed: 0xc5828 (type count = park record +24), 0xd1be8 / 0xd1c88 (park record search over a '
                       'built table), 0xe4750 (turns -> 100 ns units), 0x10a9a4 (game pointer), 0x1c4100 (64-bit divide). '
                       'Stubs: 0x118018 type record, 0xb678 assert (a zero condition fails the run), 0xdc834 counts-test '
                       '(chosen flag), 0xdc520 own age (signed timestamp difference in days).',
                       'Park record +24 is the number of listed copies; +28 follows the 0xda874 write rule '
                       '(points_first). Builds never lie after the current turn.'])

COUNTS = Target('park-points-counts-test', 'Park points counts-test 0xdc834', 'GUESTS.md "Arrivals"', 0xDC834,
                [(0xDC834, 0xDC9D4), (0xDFE0C, 0xDFE34), (0xDFF18, 0xDFF2C), (0xE2FE4, 0xE3024)], counts_stubs,
                counts_binary, counts_plan, counts_inputs, equal=counts_equal,
                alternatives={'implemented-subset': counts_subset},
                notes=['No C# hook: the entrance cell, the queue room and the coaster have no state in ParkEconomy; the '
                       '"implemented-subset" reading is the Python mirror of ParkEconomy.CountsForParkPoints (ARRIVAL-003), '
                       'whose mismatches are the conditions C# omits.',
                       'Executed: 0xdc834, 0xdc988 (queue room), 0xdfe0c / 0xdfe20 (ride state 1 / 4), 0xdff18 (open word '
                       '+100 == 0), 0xe2fe4 (track type 3). Stubs: 0x118018 type record (+156 chosen), 0xdd3f4 back of queue '
                       '(writes the chosen cell), 0xdd118 listed guests (chosen), 0x45eac coaster controller (chosen word).',
                       'The compared result is the returned word and, for the plan, the stub call order; the u16 behind TOC '
                       '-19648 (zero in the file, read only here) is checked to be zero.'])

LOAN_OFFERS = Target('loan-offers', 'Loan offer setup 0xcb7a8', 'ECONOMY.md / ParkLedger LoanMath (F12, F13)', 0xCB7A8,
                     [(0xCB7A8, LOAN_UNSIGNED_STOP), LOAN_TO_UINT, LOAN_POW_GLUE], loan_stubs, loan_offers_binary,
                     loan_offers_plan, loan_offers_inputs, equal=loan_equal,
                     alternatives={'routine-reading': loan_offers_reading},
                     csharp=('loan_offers', loan_offers_request, loan_offers_result),
                     notes=['Fragment 0xcb7a8..0xcb908: the 8-offer loop. Executed: the loop, the u32 field copies, the binary64 '
                            'arithmetic and the float -> unsigned conversion 0x1c3fbc. Stubs: 0xfa718 base constructor (header '
                            'only), MathLib pow behind the glue 0x1c61a4 (Python math.pow).'])

FAILURE = Target('queue-failure-predicate', 'Ride-failure predicate 0xdfe34', 'QUEUE-008 / GUESTS.md "Queue"', 0xDFE34,
                 [(0xDFE34, 0xDFE88)], lambda t: {}, failure_binary, failure_plan, failure_inputs,
                 alternatives={'routine-reading': failure_reading, 'plan-var-broken': failure_plan_var_broken},
                 csharp=('queue_failure', failure_request, lambda x, r: r),
                 notes=['C# hook: bridge function "queue_failure" (RideVisitorBridge.ReliefWornOut on a real bridge: the relief flag and '
                        'the wear gauge as a single).',
                        'Executed alone (no stubs): the routine reads ride +46 bit 0x1 and the float +64; it never reads the script.',
                        'The input "broken" is the script variable the pre-F15 claim ("plan-var-broken") reads; the routine cannot see it.'])

JOIN_DIFFERENCE_TARGET = Target('queue-join-difference', 'Join excitement difference 0xe9a84', 'QUEUE-004 / QUEUE-plan 4',
                                0xE9A84, JOIN_ALLOWED, join_stubs, join_difference_binary, join_plan_difference, join_inputs,
                                alternatives={'routine-reading': join_difference_reading, 'plan-static': join_plan_static_difference},
                                csharp=('queue_join_difference', join_request('queue_join_difference'), lambda x, r: r),
                                notes=['C# hook: bridge function "queue_join_difference" (RideVisitorBridge.JoinDifference on a real bridge). '
                                       'The thing behind ride +36 is not sent: C# has no such thing, so the inputs with one and a ride '
                                       'excitement from the data differ (ORACLE.md F16, QUEUE-004).',
                                       '"plan-static" is the claim before F16: ride minus preferred excitement, unclamped, without the '
                                       'relief case.',
                                       'Executed: 0xe9a84, 0xdfe34, 0xeb7e4, 0xe02ec, 0xdff70, 0xe1f70, 0xe1f78, 0xe2364, '
                                       '0xe2fe4 and the controller getter 0x41a14. Stubs: 0x118018 type record, 0xb5e24 script '
                                       'controller, 0x1ea40 pieces behind ride +36, the import sqrt.'])

JOIN_GATE = Target('queue-join-gate', 'Join excitement gate (state 10, 0xeccb0 from 0xecd6c)', 'QUEUE-004 / QUEUE-plan 4',
                   JOIN_FRAGMENT, JOIN_ALLOWED + [(0xECD6C, 0xECDB4), (0xECED0, 0xECF38)], join_stubs, join_gate_binary,
                   join_plan, join_inputs, equal=join_gate_equal,
                   alternatives={'routine-reading': join_gate_reading, 'plan-static': join_plan_static},
                   csharp=('queue_join_gate', join_request('queue_join_gate'), lambda x, r: r),
                   notes=['C# hook: bridge function "queue_join_gate" (RideVisitorBridge.ExcitementRefusal and JoinDifference on a real '
                          'bridge; outcome and difference are compared). No thing behind ride +36, as queue-join-difference.',
                          'Fragment of the state-10 handler: the 0xe9a84 call, the 0xe02ec(ride, 1) predicate and the 45 test, '
                          'stopping at 0xecdb0 (joined) or after the refusal thought call 0xecf34. Stubs as queue-join-difference '
                          'plus 0x5fbcc log (the message text is read), 0xfb7b4 reason and 0xfb54c thought (arguments checked).',
                          '"plan-static" is the claim before F16: rides and sideshows refuse on |preferred - static level| >= 45.'])

RIDE_UPDATE_TARGET = Target('queue-ride-update', 'Ride update 0xe0a8c (what a broken ride does to its queue)',
                            'QUEUE-008, QUEUE-019 / QUEUE-plan 5.2 step 3 and 6', RIDE_UPDATE, [(RIDE_UPDATE, 0xE0C3C)],
                            ride_update_stubs, ride_update_binary, ride_update_plan, ride_update_inputs,
                            notes=['No C# hook: the C# bridge has only the VAR_BROKEN reading (states 1 and 4), not the ride states 2, 3 '
                                   'and the others; RideVisitorBridge.DismissHead is that reading (QueueTests). CSHARP_PENDING.',
                                   'Executed: the dispatch of 0xe0a8c on the ride state +408 and the head-guest decision of its states 1, 2 and 4. '
                                   'Stubs record the calls instead of running them: 0xe1864 admission, 0xdcebc remove from queue, 0xee804 dismiss, '
                                   '0xeda04 force aboard, 0xe1758 script release; plus 0xb5758 / 0xb5be0 (VAR_LETMEON), 0xb678 assertion, '
                                   '0xfa9a4 and the log 0x5fbcc.',
                                   'Domain: an empty queue has no VAR_LETMEON guest (the original would index ThingArray[0]).'])

TRAIN_TARGET = Target('staff-training-split', 'Staff training split 0xfc1c4', 'ECONOMY.md / APPROX-TRACE ECON-013 (F17)', TRAIN_ENTRY,
                      [(0xFC1C4, 0xFC4D4), TRAIN_NEXT_GLUE, (0xFA9A4, 0xFA9B0), (TRAIN_ONE, 0xF3708), (0x108424, 0x108480),
                       (0x1082B4, 0x108310), (0xCBFDC, 0xCC084)], train_stubs, train_binary, train_current, train_inputs,
                      alternatives={'routine-reading': train_reading},
                      csharp=('staff_training', train_request, train_result),
                      notes=['C# hook: bridge function "staff_training" (ParkStaff.Train on synthetic Standard.sam prices; the '
                             'bridge sends the real members in input order and reads back grade, points and the amount spent).',
                             'Executed: 0xfc1c4 (count loop, per-member loop), the list walkers 0x10507c / 0x1050a4 / 0xfa9a4, '
                             '0xf3588 (one member: points, promotion), the thing lookups 0x108424 / 0x1082b4 and the debit 0xcbfdc '
                             'with its statistics. Stubs: 0xb678 assertion (a zero condition fails the run) and the log 0x5fbcc.',
                             'The role counter table behind TOC -19008 (the "base counts") is checked to be zero in the file: it is '
                             'read, never written. The compared result is each member grade and points, the amount debited from '
                             'the bank (equal to the profit, month and training statistics, which are checked inside the run).',
                             'Domain: prices of at least 1 (divw by zero is undefined); budgets from 0 to 2^28 (the sum of the debits stays inside 32 bits); an active bank.'])

TARGETS = [LCG, RAND, QUEUE, CAN_CHANGE_TARGET, SET_CELL, UNBOUNCE, BOUNCE, SPEED_SETTER, SMOOTHING, COAST_STEP, COAST_FINAL,
           ARRIVAL, ARRIVAL_UPDATE, POINTS, COUNTS, LOAN_OFFERS, LOAN_MONTH, LOAN_TERM, LOAN_TAKE, LOAN_PAYOFF, FAILURE, JOIN_DIFFERENCE_TARGET, JOIN_GATE, RIDE_UPDATE_TARGET, ECON_SALE, ECON_TRIGGER, TRAIN_TARGET]
DOMAINS = {SET_CELL.key: set_cell_domain}

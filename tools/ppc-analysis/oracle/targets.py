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
from harness import Image, Sandbox, f32  # noqa: E402
from ppc32 import MASK32, Machine, Undefined, s32  # noqa: E402

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


# ============================================================================== registry
LCG = Target('det-world-lcg', 'World LCG step 0x105328', 'DET-plan §2.3 / det_model.world_next', 0x105328,
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

TARGETS = [LCG, RAND, QUEUE, CAN_CHANGE_TARGET, SET_CELL, UNBOUNCE, BOUNCE, SPEED_SETTER, SMOOTHING, COAST_STEP, COAST_FINAL]
DOMAINS = {SET_CELL.key: set_cell_domain}

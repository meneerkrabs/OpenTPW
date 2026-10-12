"""Oracle targets of the guests' core decisions (docs/reverse/ORACLE.md F24 attraction choice, F25 needs, F26 lifecycle).

Imported at the end of `targets.py`, which supplies the helpers (`Target`, the join machinery that executes the excitement
callees `0xe02ec` and `0xeb7e4`, the stub conventions). Pure Python, no original bytes: tables the reading needs are read from
the sandbox's memory at test time.
"""
from __future__ import annotations

import math
import random
from pathlib import Path

import targets as T
from harness import STACK_BASE, STACK_SIZE, Image, bin_root, f32
from ppc32 import MASK32, Machine, bits_to_double, double_to_bits, s32
from targets import (ARRIVAL_GAME_POINTER, ASSERT, DEBUG_LOG, FACTOR_OBJECT, JOIN_ALLOWED, JOIN_STATE, JOIN_TYPE_ID, POINTS_THINGS,
                     THING_AGE, UNDEFINED, Target, bits, fctiwz, join_excitement_reading, join_inputs, join_setup, join_stubs,
                     random_float, same_float, sale_clamp, toc_pointer, trunc_div)

# ============================================================================== (m) F24: the attraction score 0xe9164
SCORE_ENTRY = 0xE9164
CELL_OF_THING = 0xDD3F4          # (&out, thing): the back-of-queue cell id of an attraction; stubbed with the chosen cell
QUEUE_COUNT = 0xDD118            # (thing): guests listed in the queue; stubbed with the chosen count
SCORE_ALLOWED = JOIN_ALLOWED + [(0xE9164, 0xE9A84), (0xD7310, 0xD7340), (0xFA9A4, 0xFA9B0), (0xDC320, 0xDC350)]
MAP_POINTER_SLOT = -27300        # TOC slot -> word holding the map base (game + 728)
WEIGHTS_SLOT = -30084            # TOC slot -> the settings block (PeepInfo words from +12 on, 4 bytes per key in file order)
SCORE_STATE = type('ScoreState', (), {})()
WEIGHT_OFFSETS = {'dv1': 80, 'dv2': 84, 'dv3': 88, 'dist': 112, 'queue': 116, 'exc': 120, 'thirst': 124, 'hunger': 128, 'toilet': 132,
                  'ill': 136}
TABLE_CACHE: dict = {}
det_model = T.det_model


def capture_tables(image: Image) -> None:
    """Remember the two match tables from the image the oracle runs on."""
    if not TABLE_CACHE:
        base = image.data_base
        TABLE_CACHE['blob'] = bytes(image.memory.u8(base + 0x41222 + i) for i in range(1024))
        TABLE_CACHE['t2'] = 0x4129B - 0x41222


def score_tables() -> dict:
    """The two match tables as the sandbox holds them: the 11 x 11 thirst/hunger table at data 0x41222 and the toilet/illness table at
    0x4129b (they are adjacent), read from the private binary at test time and cached (by the first run of the binary, or from
    OPENTPW_MAC_BIN). Never copied into the repository."""
    if not TABLE_CACHE:
        root = bin_root()
        if root is None:
            raise RuntimeError('the private binary is needed')
        capture_tables(Image.load(root))
    return TABLE_CACHE


def score_stubs(target: Target) -> dict:
    stubs = dict(join_stubs(target))
    state = SCORE_STATE

    def cell(m: Machine) -> None:
        if m.gpr[4] != state.ride:
            raise AssertionError('0xdd3f4 called without the ride')
        m.memory.w16(m.gpr[3], state.cell)

    def count(m: Machine) -> None:
        if m.gpr[3] != state.ride:
            raise AssertionError('0xdd118 called without the ride')
        m.gpr[3] = state.x['qcount'] & MASK32

    def age(m: Machine) -> None:
        if m.gpr[3] != state.ride:
            raise AssertionError('0xdc520 called without the ride')
        m.gpr[3] = state.x['age'] & MASK32

    def factor_object(m: Machine) -> None:
        if m.gpr[3] != state.game:
            raise AssertionError('0x1083c8 called without the game pointer')
        m.gpr[3] = state.factor_object

    def assertion(m: Machine) -> None:
        if m.gpr[3] & 0xFF == 0:
            raise AssertionError('an assertion failed')

    stubs.update({CELL_OF_THING: cell, QUEUE_COUNT: count, THING_AGE: age, FACTOR_OBJECT: factor_object, ASSERT: assertion,
                  DEBUG_LOG: lambda m: None})
    return stubs


def score_setup(sb, x: dict):
    """The join state (ride, type record, guest, preferred excitement) plus the score's own words: weights, cells, needs, history."""
    st = join_setup(sb, x)
    s = SCORE_STATE
    s.x = x
    s.ride, s.guest = st.ride, st.guest
    s.game = sb.alloc(0x1B13DC + 10 * 16384 + 64)
    s.factor_object = sb.struct(64, {48: ('i', x['drops'])})
    s.cell = 1 + x['rx'] + (x['ry'] << 7)
    sb.write(toc_pointer(sb, ARRIVAL_GAME_POINTER), 'I', s.game)
    sb.write(toc_pointer(sb, MAP_POINTER_SLOT), 'I', s.game + 728)
    sb.write(s.game + 728 + 0x1B1104 + 10 * (s.cell - 1) + 8, 'h', x['mod'])
    weights = toc_pointer(sb, WEIGHTS_SLOT)
    for name, offset in WEIGHT_OFFSETS.items():
        value = x['dv'][int(name[2]) - 1] if name.startswith('dv') else x['w'][['dist', 'queue', 'exc', 'thirst', 'hunger', 'toilet', 'ill'].index(name)]
        sb.write(weights + offset, 'I', value & MASK32)
    sb.write(st.ride + 0, 'H', x['rid'])
    sb.write(st.ride + 60, 'I', x['qcells'] & MASK32)
    sb.write(st.record + 196, 'i', x['golden'])
    sb.write(st.record + 276, 'i', x['indoors'])
    sb.write(st.record + 324, 'I', x['teffect'] & MASK32)
    sb.write(st.record + 328, 'I', x['heffect'] & MASK32)
    sb.write(st.record + 440, 'i', x['cost0'])
    for offset, name in ((420, 'thirst'), (424, 'hunger'), (428, 'toilet'), (432, 'ill')):
        sb.write(st.guest + offset, 'f', x[name])
    sb.write(st.guest + 4, 'B', x['gx'])
    sb.write(st.guest + 6, 'B', x['gy'])
    for slot, value in enumerate(x['used']):
        sb.write(st.guest + 480 + 2 * slot, 'H', value)
    for slot, value in enumerate(x['temp']):
        sb.write(st.guest + 488 + 2 * slot, 'H', value)
    things = toc_pointer(sb, POINTS_THINGS)
    if x['used'][0] and x['hist_thing'] != 'none':   # the thing the most recent ride id names: of the ride's type or of another
        other = sb.struct(64, {14: ('H', JOIN_TYPE_ID if x['hist_thing'] == 'same' else JOIN_TYPE_ID + 1)})
        sb.write(things + 20 * x['used'][0], 'I', other)
    return st


def score_binary(sb, x: dict, log) -> int:
    capture_tables(sb.image)
    st = score_setup(sb, x)
    return sb.call(SCORE_ENTRY, {3: st.guest, 4: st.ride}).r3 & MASK32


def mul32(a: int, b: int) -> int:
    return (a * b) & MASK32


def sdiv(a: int, b: int) -> int:
    """divw / signed truncating division of two 32-bit patterns (b nonzero)."""
    return trunc_div(s32(a), s32(b)) & MASK32


def fctiwz_word(value: float) -> int:
    return fctiwz(value) & MASK32


def to_float_signed(word: int) -> float:
    return float(s32(word))


def need_match_1(blob: bytes, need: float, effect: int) -> int:
    """thirst/hunger: index 11 * ((effect byte) / 10) + (need word / 10), each quotient the byte of mulhwu(0xCCCCCCCD, .) >> 3."""
    word = fctiwz_word(need)
    n10 = ((0xCCCCCCCD * word) >> 32 >> 3) & 0xFF
    e10 = ((0xCCCCCCCD * (effect & 0xFF)) >> 32 >> 3) & 0xFF
    return blob[(e10 * 11 + n10) & MASK32]


def need_match_2(blob: bytes, base: int, need: float, relief: bool) -> int:
    """toilet/illness: only for relief objects: table2[(byte(need) + 4) / 5]."""
    if not relief:
        return 0
    return blob[base + (((fctiwz_word(need) & 0xFF) + 4) // 5)]


def golden_scale(score: int, golden: int) -> int:
    """type +196 above 0: double arithmetic, score * (1 + (golden + 1) / 10) truncated."""
    scaled = float(s32(score)) * (1.0 + float(s32(golden + 1)) / 10.0)
    return fctiwz_word(scaled)


def expensive_scale(score: int, cost: int) -> int:
    """type +440 nonzero: q = single(cost) / 3000f; above 1 the score (as a single) times (1f + q / 10f), truncated."""
    q = f32(f32(float(s32(cost))) / 3000.0)
    if not q > 1.0:
        return score
    return fctiwz_word(f32(f32(float(s32(score))) * f32(1.0 + f32(q / 10.0))))


def score_reading(x: dict, **variant) -> int | str:
    """0xe9164 as read: the seven matches normalised by the sum of all weights, in unsigned 32-bit arithmetic, then the multipliers and
    the history divisions. The keywords are deliberate deviations for the sharpness tests."""
    tables = score_tables()
    blob, t2 = tables['blob'], tables['t2']
    if x['used'][0] != 0 and x['hist_thing'] == 'same' and not variant.get('no_type_exit'):
        return 0
    d2 = (x['gx'] - x['rx']) ** 2 + (x['gy'] - x['ry']) ** 2
    divisor = variant.get('distance_divisor', 450)
    d = min(100, d2 * 100 // divisor)
    dist_match = 100 - d
    if x['mod'] != 0 and not variant.get('no_modifier'):
        dist_match //= (x['mod'] & MASK32)
    near = d2 <= 8 or variant.get('always_queue')
    if near:
        cells = x['qcells'] & MASK32
        size = cells if cells else 1
        queue_match = (100 - (mul32(x['qcount'], 100) // mul32(size, 4))) & MASK32
        if variant.get('clamp_queue'):
            queue_match = max(0, s32(queue_match)) & MASK32
        queue_weight = x['w'][1] & MASK32
    else:
        queue_match, queue_weight = 0, 0
    pref = x['pref'] & 0xFF
    excitement = join_excitement_reading(x, 0) & 0xFF
    diff = min(50, abs(pref - excitement))
    exc_match = 2 * (50 - diff)
    exc_weight = (x['w'][2] & 0xFFFF) if (join_excitement_reading(x, 1) & 0xFF or variant.get('ungated_excitement')) else 0
    thirst_match = need_match_1(blob, x['thirst'], x['teffect'])
    hunger_match = need_match_1(blob, x['hunger'], x['heffect'])
    relief = bool(x['flags'] & 1) or variant.get('always_relief', False)
    toilet_match = need_match_2(blob, t2, x['toilet'], relief)
    ill_match = need_match_2(blob, t2, x['ill'], relief)
    weights = [x['w'][0] & 0xFFFF, queue_weight, exc_weight, x['w'][3] & 0xFFFF, x['w'][4] & 0xFFFF, x['w'][5] & 0xFFFF, x['w'][6] & 0xFFFF]
    matches = [dist_match, queue_match, exc_match, thirst_match, hunger_match, toilet_match, ill_match]
    if variant.get('active_only'):   # the superseded reading: only weights whose match is nonzero count
        weights = [w if m else 0 for w, m in zip(weights, matches)]
    numerator = sum(mul32(w, m & MASK32) for w, m in zip(weights, matches)) & MASK32
    denominator = sum(weights) & MASK32
    if denominator == 0:
        return UNDEFINED
    score = numerator // denominator
    if x['age'] <= (x['dv'][0] & MASK32) and not variant.get('no_new'):
        score = mul32(score, x['dv'][1])
    if x['drops'] > 0 and x['indoors'] != 0 and not variant.get('no_rain'):
        score = mul32(score, x['dv'][2])
    if x['golden'] > 0:
        if not variant.get('no_golden'):
            score = golden_scale(score, x['golden'])
    elif x['cost0'] != 0 and not variant.get('no_expensive'):
        score = expensive_scale(score, x['cost0'])
    used, temp, rid = x['used'], x['temp'], x['rid']
    if not variant.get('no_used'):
        if variant.get('used_all'):
            for slot, divisor in enumerate((5, 4, 3, 2)):
                if used[slot] == rid:
                    score = sdiv(score, divisor)
        else:
            for slot, divisor in enumerate((5, 4, 3, 2)):
                if used[slot] == rid:
                    score = sdiv(score, divisor)
                    break
    if not variant.get('no_temp'):
        for slot, divisor in enumerate((5, 4, 3, 2)):
            if temp[slot] == rid:
                score = sdiv(score, divisor)
                if variant.get('temp_first'):
                    break
    return score


def score_plan(x: dict) -> int:
    """The rule before F24 (GUESTS.md "Decision", the former GuestSimulation.ScoreAttraction without its random 0..9): AttractionValue minus
    distance and queue terms, a static-excitement term for rides and sideshows, need terms for the needs a shop or toilet satisfies,
    minus an illness term, minus the value again for the ride the guest just rode. Weights are the .sam values; distance is the
    cell Manhattan distance (the open-grid path length). Positive scores choose; the result is the signed score as a word."""
    w = x['w']
    kind_ride = x['kind'] in (0, 2)
    score = x['value'] - w[0] * (abs(x['gx'] - x['rx']) + abs(x['gy'] - x['ry'])) - w[1] * x['qcount']
    if kind_ride:
        score += w[2] * (25 - abs((x['pref'] & 0xFF) - x['level']))
    if x['heffect'] > 0 and x['kind'] == 1:
        score += int(w[4] * x['hunger'] / 4)
    if x['teffect'] > 0 and x['kind'] == 1:
        score += int(w[3] * x['thirst'] / 4)
    if x['flags'] & 1:
        score += int(w[5] * x['toilet'] / 4)
    if x['kind'] == 0:
        score -= int(w[6] * x['ill'] / 4)
    if x['used'][0] == x['rid']:
        score -= x['value']
    return score & MASK32


def score_equal(original, other) -> bool:
    return original == other


def score_csharp_request(x: dict) -> dict:
    request = T.join_request('attraction_score')(x)
    request.update({'gx': x['gx'], 'gy': x['gy'], 'rx': x['rx'], 'ry': x['ry'], 'mod': x['mod'], 'qcount': x['qcount'],
                    'qcells': x['qcells'], 'thirst_bits': bits(x['thirst']), 'hunger_bits': bits(x['hunger']),
                    'toilet_bits': bits(x['toilet']), 'ill_bits': bits(x['ill']), 'teffect': x['teffect'], 'heffect': x['heffect'],
                    'golden': x['golden'], 'indoors': x['indoors'] != 0, 'cost0': x['cost0'], 'age': x['age'], 'drops': x['drops'],
                    'w': x['w'], 'dv': x['dv'], 'used': x['used'], 'temp': x['temp'], 'rid': x['rid'],
                    'hist_same': x['used'][0] != 0 and x['hist_thing'] == 'same'})
    return request


def score_inputs(rng: random.Random, n: int) -> list:
    base_inputs = join_inputs(rng, n)

    def extra(x: dict, **kw) -> dict:
        x = dict(x)
        x['thing36'] = 0
        x['kind'] = x['kind'] if x['kind'] != 3 else 1
        rid = rng.choice([7, 41, 200])
        near = rng.random() < 0.5
        gx, gy = rng.randrange(0, 128), rng.randrange(0, 128)
        rx = max(0, min(127, gx + rng.randrange(-3, 4))) if near else rng.randrange(0, 128)
        ry = max(0, min(127, gy + rng.randrange(-3, 4))) if near else rng.randrange(0, 128)
        history = [0, 0, 0, 0]
        if rng.random() < 0.6:
            for slot in range(4):
                history[slot] = rng.choice([0, rid, rng.randrange(1, 300)])
        temp = [rng.choice([0, 0, rid, rng.randrange(1, 300)]) for _ in range(4)]
        x.update({
            'gx': gx, 'gy': gy, 'rx': rx, 'ry': ry, 'mod': rng.choice([0, 0, 0, 0, 2, 5, -3, 100, -32768]),
            'qcells': rng.choice([0, 1, 2, 3, 5, 8, rng.randrange(0, 20)]), 'qcount': rng.choice([0, 1, 4, 9, rng.randrange(0, 60)]),
            'thirst': rng.choice([0.0, 100.0, random_float(rng, 0, 100), random_float(rng, 0, 100), 50.0, float(rng.randrange(0, 101))]),
            'hunger': rng.choice([0.0, 100.0, random_float(rng, 0, 100), random_float(rng, 0, 100), 50.0, float(rng.randrange(0, 101))]),
            'toilet': rng.choice([0.0, 100.0, random_float(rng, 0, 100), 50.0, float(rng.randrange(0, 101))]),
            'ill': rng.choice([0.0, 100.0, random_float(rng, 0, 100), 50.0, float(rng.randrange(0, 101))]),
            'teffect': rng.choice([0, 0, 10, 30, 50, rng.randrange(0, 101)]), 'heffect': rng.choice([0, 0, 10, 30, 50, rng.randrange(0, 101)]),
            'golden': rng.choice([0, 0, 0, 0, 1, 2, 5, -1, -3]), 'cost0': rng.choice([0, 0, 100, 1000, 3001, 6000, 30000, -500]),
            'indoors': rng.choice([0, 0, 1, 1]), 'age': rng.choice([0, 3, 7, 8, 30, rng.randrange(0, 100)]),
            'drops': rng.choice([0, 0, 1, -1, 5000]),
            'w': rng.choice([[1, 1, 1, 2, 2, 2, 2], [1, 1, 1, 2, 2, 2, 2], [rng.randrange(0, 10) for _ in range(7)],
                             [rng.randrange(0, 70000) for _ in range(7)]]),
            'dv': rng.choice([[7, 5, 5], [7, 5, 5], [rng.randrange(0, 20), rng.randrange(0, 8), rng.randrange(0, 8)]]),
            'used': history, 'temp': temp, 'rid': rid,
            'hist_thing': rng.choice(['same', 'other', 'other', 'none']), 'value': rng.randrange(0, 100),
        })
        x.update(kw)
        return x
    inputs = [extra(x) for x in base_inputs]
    shipped = dict(w=[1, 1, 1, 2, 2, 2, 2], dv=[7, 5, 5], mod=0, golden=0, cost0=0, indoors=0, drops=0, age=100, used=[0, 0, 0, 0],
                   temp=[0, 0, 0, 0], hist_thing='none', teffect=0, heffect=0, thirst=0.0, hunger=0.0, toilet=0.0, ill=0.0, qcells=3,
                   qcount=0, gx=10, gy=10, rx=10, ry=10, level=60, kind=0, flags=4, thing36=0, track=0, pref=60, pieces=[0, 0, 0, 0],
                   init_speed=100, speed=100, init_dur=30, dur=30, rlevel=0, idx=0, gauge=100.0, goods=60, price=35, win=50, ctrl=0,
                   coaster_e=40.0, rid=7)

    def edge(**kw) -> dict:
        x = dict(shipped, value=40)
        x.update(kw)
        return x
    edges = [
        edge(),                                                  # a ride next to the guest, matching excitement, an empty queue: 100
        edge(rx=13, ry=10),                                      # 9 squares away: past the queue range, 100 - 2 = 98 distance match
        edge(rx=12, ry=12, qcount=6, qcells=3),                  # squared distance 8, a queue of 6 in 3 cells
        edge(qcount=9, qcells=1),                                # near, the queue count above 4 x cells: the unsigned wrap
        edge(rx=60, ry=60, qcells=1, qcount=9),                  # far: the queue and its weight are not counted
        edge(thirst=50.0, teffect=50, kind=1, level=0),          # a drinks shop for a guest at thirst 50: match 17
        edge(thirst=100.0, teffect=100, kind=1, level=0), edge(hunger=100.0, heffect=100, kind=1, level=0),
        edge(toilet=50.0, flags=5, kind=1, level=0), edge(toilet=100.0, ill=100.0, flags=5, kind=1, level=0),
        edge(toilet=100.0, flags=4, kind=1, level=0),            # the toilet need of a guest, a shop without the relief flag: match 0
        edge(age=7), edge(age=8), edge(age=0, dv=[0, 5, 5]), edge(age=1, dv=[0, 5, 5]),
        edge(drops=1, indoors=1), edge(drops=0, indoors=1), edge(drops=1, indoors=0),
        edge(golden=1), edge(golden=2, cost0=6000), edge(golden=0, cost0=6000), edge(golden=0, cost0=3000), edge(golden=0, cost0=3001),
        edge(used=[7, 0, 0, 0], hist_thing='other'), edge(used=[9, 7, 0, 0], hist_thing='other'),
        edge(used=[9, 8, 7, 0], hist_thing='other'), edge(used=[9, 8, 6, 7], hist_thing='other'),
        edge(used=[7, 7, 7, 7], hist_thing='other'), edge(temp=[7, 0, 0, 0]), edge(temp=[7, 7, 7, 7]), edge(temp=[1, 7, 0, 7]),
        edge(used=[9, 0, 0, 0], hist_thing='same'), edge(used=[0, 0, 0, 0], hist_thing='same'),
        edge(used=[9, 0, 0, 0], hist_thing='none'), edge(used=[7, 0, 0, 0], hist_thing='same', temp=[7, 7, 7, 7]),
        edge(mod=4), edge(mod=-1), edge(mod=1), edge(w=[0, 0, 0, 0, 0, 0, 0]), edge(w=[0, 1, 0, 0, 0, 0, 0], rx=60, ry=60),
        edge(w=[65536, 0, 0, 0, 0, 0, 0]), edge(w=[1, 0, 1, 0, 0, 0, 0], level=0),
        edge(level=0, w=[0, 0, 5, 0, 0, 0, 0]),                  # the excitement weight is dropped for a level byte of 0: a zero sum
        edge(qcells=0, qcount=3), edge(qcells=0, qcount=0),
        edge(thirst=99.9, teffect=100, kind=1, level=0), edge(thirst=100.0, teffect=0, kind=1, level=0),
        edge(pref=10, level=100), edge(pref=100, level=10), edge(pref=60, level=60), edge(pref=0, level=100),
        edge(gx=0, gy=0, rx=127, ry=127), edge(gx=127, gy=0, rx=0, ry=127),
        *[edge(rx=10 + dx, ry=10 + dy, w=[1, 0, 0, 0, 0, 0, 0]) for d2 in range(440, 452) for dx in range(0, 22) for dy in range(0, 22)
          if dx * dx + dy * dy == d2],   # squared distances next to 450 with only the distance weight: the score is the match, so the divisor 450 shows
        edge(value=40, golden=1, used=[7, 0, 0, 0], hist_thing='other', age=0, drops=1, indoors=1, temp=[7, 7, 0, 0]),
    ]
    return edges + inputs


# the 0xe9164 score
SCORE_TARGET = Target('attraction-score', 'Attraction score 0xe9164', 'GUESTS.md "Decision" / the former GuestSimulation.ScoreAttraction', SCORE_ENTRY,
                      SCORE_ALLOWED, score_stubs, score_binary, score_plan, score_inputs, equal=score_equal,
                      alternatives={'routine-reading': score_reading},
                      csharp=('attraction_score', score_csharp_request, lambda x, r: r),
                      notes=['C# hook: bridge function "attraction_score" (AttractionScore.Compute on the inputs, the excitement words from a real '
                             'RideVisitorBridge). The thing behind ride +36 is not sent (always 0 here).',
                             'Executed: 0xe9164 whole, the excitement callees 0xe02ec / 0xdff70 / 0xeb7e4 (as in F16), the cell record helper 0xd7310 '
                             'on a synthetic map, the type record getter 0xdc320 and the id getter 0xfa9a4. Stubs: 0xdd3f4 back cell (chosen), 0xdd118 queue '
                             'count (chosen), 0xdc520 own age in days (chosen), 0x1083c8 weather thing (+48 chosen), 0x118018 type record, 0xb678 '
                             'assertion (a zero condition fails the run), 0x5fbcc debug log. The weights are written into the settings block '
                             '(+80..+88 the three decision variables, +112..+136 the seven weights).',
                             'The two match tables are read from the binary at test time, never stored here.'])


# ============================================================================== (n) F24: what a use does to the guest, 0xea318
OUTCOME_ENTRY = 0xEA318
OUTCOME_ALLOWED = JOIN_ALLOWED + [(0xEA318, 0xEA5A4)]
OUTCOME_WEIGHTS = {'perfect': 36, 'good': 40, 'ok': 44, 'divisor': 48}


def outcome_stubs(target: Target) -> dict:
    stubs = dict(join_stubs(target))
    stubs[DEBUG_LOG] = lambda m: None
    return stubs


def outcome_binary(sb, x: dict, log) -> dict:
    st = join_setup(sb, x)
    weights = toc_pointer(sb, WEIGHTS_SLOT)
    for name, offset in OUTCOME_WEIGHTS.items():
        sb.write(weights + offset, 'i', x[name])
    sb.write(st.guest + 412, 'f', x['happy'])
    sb.write(st.guest + 424, 'f', x['hunger'])
    sb.write(st.guest + 432, 'f', x['ill'])
    sb.call(OUTCOME_ENTRY, {3: st.guest, 4: st.ride})
    return {'happy': sb.read(st.guest + 412, 'f'), 'ill': sb.read(st.guest + 432, 'f')}


def outcome_reading(x: dict, **variant) -> dict | str:
    """0xea318: nothing unless the live excitement byte (0xe02ec(ride, 0)) is nonzero. The difference of the preferred and the live
    excitement (bytes) picks the happiness gain: below 5 the PerfectRide word (weights +36), below 15 GoodRide (+40), below 40 OKRide (+44),
    otherwise none, added to the meter and clamped to 0..100. The illness gains trunc(excitement / RideVomitDivisor) *
    trunc((100 - byte(trunc(hunger))) / 20) (signed, 32 bits), clamped to 0..100."""
    excitement = join_excitement_reading(x, 0) & 0xFF
    if not variant.get('ungated') and excitement == 0:
        return {'happy': x['happy'], 'ill': x['ill']}
    pref = x['pref'] & 0xFF
    diff = abs(pref - excitement)
    inclusive = 1 if variant.get('inclusive') else 0
    gain = x['perfect'] if diff < 5 + inclusive else x['good'] if diff < 15 + inclusive else x['ok'] if diff < 40 + inclusive else None
    if variant.get('penalty') and gain is None:
        gain = -5
    happy = x['happy']
    if gain is not None:
        happy = sale_clamp(f32(f32(float(gain)) + happy))
    divisor = x['divisor']
    if divisor == 0:
        return UNDEFINED
    hunger_byte = fctiwz_word(x['hunger']) & 0xFF
    fed = trunc_div(100 - hunger_byte, 20) if not variant.get('linear_hunger') else (100 - hunger_byte) // 10
    product = mul32(trunc_div(excitement, divisor) & MASK32, fed & MASK32)
    ill = sale_clamp(f32(f32(float(s32(product))) + x['ill']))
    return {'happy': happy, 'ill': ill}


def outcome_plan(x: dict) -> dict:
    """The rule before F24 (GuestSimulation.ApplyVisit): rides and sideshows only, the static ExcitementLevel, bands of at most 5, 15 and 40
    from the preferred excitement (a larger difference costs SmallHappinessChange, here the divisor-free constant 5), nausea raised by
    level / divisor with no hunger factor and a reset with a MediumHappinessChange loss at VomitCapacity 100."""
    if x['kind'] not in (0, 2):
        return {'happy': x['happy'], 'ill': x['ill']}
    level = T.join_static_excitement(x)
    diff = abs((x['pref'] & 0xFF) - level)
    change = x['perfect'] if diff <= 5 else x['good'] if diff <= 15 else x['ok'] if diff <= 40 else -5
    happy = max(0.0, min(100.0, x['happy'] + change))
    ill = x['ill'] + level / max(1, x['divisor'])
    if ill >= 100:
        ill = 0.0
        happy = max(0.0, happy - 15)
    return {'happy': f32(happy), 'ill': f32(ill)}


def outcome_equal(original, other) -> bool:
    if original == UNDEFINED or other == UNDEFINED or isinstance(original, str) or isinstance(other, str):
        return original == other
    return all(same_float(original[k], other[k]) for k in ('happy', 'ill'))


def outcome_csharp_request(x: dict) -> dict:
    request = T.join_request('ride_outcome')(x)
    request.update({'happy_bits': bits(x['happy']), 'hunger_bits': bits(x['hunger']), 'ill_bits': bits(x['ill']), 'perfect': x['perfect'],
                    'good': x['good'], 'ok': x['ok'], 'divisor': x['divisor']})
    return request


def outcome_csharp_result(x: dict, r) -> dict | str:
    if isinstance(r, dict) and 'exception' in r:
        return UNDEFINED
    return {'happy': T.from_bits(r['happy_bits']), 'ill': T.from_bits(r['ill_bits'])}


def outcome_inputs(rng: random.Random, n: int) -> list:
    def extra(x: dict, **kw) -> dict:
        x = dict(x)
        x['thing36'] = 0
        x['kind'] = x['kind'] if x['kind'] != 3 else 1
        x.update({'happy': rng.choice([50.0, 0.0, 100.0, random_float(rng, 0, 100), random_float(rng, 80, 100), random_float(rng, 0, 20)]),
                  'hunger': rng.choice([0.0, 100.0, 80.0, 81.0, random_float(rng, 0, 100), float(rng.randrange(0, 101)), 255.0, 300.0]),
                  'ill': rng.choice([0.0, 50.0, 90.0, random_float(rng, 0, 100)]),
                  'perfect': rng.choice([25, 25, rng.randrange(-30, 90)]), 'good': rng.choice([15, 15, rng.randrange(-30, 90)]),
                  'ok': rng.choice([5, 5, rng.randrange(-30, 90)]), 'divisor': rng.choice([10, 10, 4, rng.randrange(1, 30), 0, -2])})
        x.update(kw)
        return x
    inputs = [extra(x) for x in join_inputs(rng, n)]
    base = dict(idx=0, pref=80, kind=0, level=60, track=0, flags=4, gauge=100.0, rlevel=0, init_speed=100, speed=100, init_dur=30, dur=30,
                thing36=0, pieces=[0, 0, 0, 0], goods=60, price=35, win=50, ctrl=0, coaster_e=40.0, happy=50.0, hunger=0.0, ill=0.0,
                perfect=25, good=15, ok=5, divisor=10)

    def edge(**kw) -> dict:
        return dict(base, **kw)
    edges = [edge(pref=60), edge(pref=64), edge(pref=65), edge(pref=55), edge(pref=56), edge(pref=74), edge(pref=75), edge(pref=45),
             edge(pref=46), edge(pref=20), edge(pref=21), edge(pref=100), edge(pref=19),     # the band edges: strictly below 5, 15 and 40
             edge(level=0), edge(level=256), edge(kind=1, level=20, pref=60),               # no ExcitementLevel byte: nothing happens
             edge(hunger=0.0), edge(hunger=80.0), edge(hunger=81.0), edge(hunger=100.0), edge(hunger=150.0), edge(hunger=255.0),
             edge(hunger=300.0), edge(hunger=-5.0), edge(hunger=math.nan),
             edge(happy=90.0), edge(happy=100.0), edge(happy=0.0, pref=10), edge(ill=95.0), edge(ill=100.0, hunger=0.0),
             edge(divisor=0), edge(divisor=-3), edge(divisor=1000), edge(perfect=-30, happy=10.0), edge(ok=-60, pref=30, happy=5.0),
             edge(level=100, pref=20), edge(kind=2, level=30, pref=40)]
    return edges + inputs


OUTCOME_TARGET = Target('ride-outcome', 'What a use does to happiness and illness 0xea318', 'GUESTS.md "Ride outcome" / the former GuestSimulation.ApplyVisit',
                        OUTCOME_ENTRY, OUTCOME_ALLOWED, outcome_stubs, outcome_binary, outcome_plan, outcome_inputs, equal=outcome_equal,
                        alternatives={'routine-reading': outcome_reading},
                        csharp=('ride_outcome', outcome_csharp_request, outcome_csharp_result),
                        notes=['C# hook: bridge function "ride_outcome" (RideOutcome.Apply on the guest meters, the live and static excitement from a real '
                               'RideVisitorBridge). The thing behind ride +36 is not sent (always 0 here).',
                               'Executed: 0xea318 whole and its excitement callees as in F16. Only the happiness and illness meters are compared; the weights '
                               '+36/+40/+44 (PerfectRide, GoodRide, OKRide) and +48 (RideVomitDivisor) are written into the settings block.'])


# ============================================================================== (o) F24: the choice loop 0xe8ff4
CHOICE_ENTRY = 0xE8FF4
CHOICE_ALLOWED = [(0xE8FF4, 0xE9164)]
CHOICE_SCORE, CHOICE_COUNTS, CHOICE_REACH = 0xE9164, 0xDC834, 0xE67C8
CHOICE_FIRST = 0x1E0000 - 22714        # game + 0x1da746: id of the first thing in the list (u16)
CHOICE_TURN = 0x1E0000 - 22772         # game + 0x1da70c: the world turn
CHOICE_STATE = type('ChoiceState', (), {})()


def choice_stubs(target: Target) -> dict:
    state = CHOICE_STATE

    def cell(m: Machine) -> None:
        thing = state.by_address.get(m.gpr[4])
        if thing is None:
            raise AssertionError('0xdd3f4 called without a listed thing')
        m.memory.w16(m.gpr[3], thing['cell'])

    def score(m: Machine) -> None:
        thing = state.by_address.get(m.gpr[4])
        if m.gpr[3] != state.guest or thing is None:
            raise AssertionError('0xe9164 called without the guest and a listed thing')
        target.log.append(('score', thing['id']))
        m.gpr[3] = thing['score'] & MASK32

    def counts(m: Machine) -> None:
        thing = state.by_address.get(m.gpr[3])
        if thing is None:
            raise AssertionError('0xdc834 called without a listed thing')
        target.log.append(('counts', thing['id']))
        m.gpr[3] = thing['counts']

    def reach(m: Machine) -> None:
        if m.gpr[3] != state.guest:
            raise AssertionError('0xe67c8 called without the guest')
        target.log.append(('reach', m.gpr[4] & 0xFFFF))
        m.gpr[3] = state.reach[m.gpr[4] & 0xFFFF]

    def assertion(m: Machine) -> None:
        if m.gpr[3] & 0xFF == 0:
            raise AssertionError('an assertion failed')

    return {CELL_OF_THING: cell, CHOICE_SCORE: score, CHOICE_COUNTS: counts, CHOICE_REACH: reach, ASSERT: assertion}


def choice_binary(sb, x: dict, log) -> dict:
    st = CHOICE_STATE
    game = sb.alloc(0x1E0000 + 64)
    sb.write(toc_pointer(sb, ARRIVAL_GAME_POINTER), 'I', game)
    sb.write(game + CHOICE_TURN, 'I', x['turn'] & MASK32)
    st.guest = sb.struct(1024, {476: ('H', 0xFFFF)})
    things = toc_pointer(sb, POINTS_THINGS)
    st.by_address, st.reach = {}, {}
    count = len(x['things'])
    sb.write(game + CHOICE_FIRST, 'H', 1 if count else 0)
    for index, thing in enumerate(x['things'], start=1):
        address = sb.struct(64, {12: ('H', index + 1 if index < count else 0), 46: ('H', thing['flags'])})
        sb.write(things + 20 * index, 'I', address)
        st.by_address[address] = {**thing, 'id': index}
        st.reach[thing['cell']] = thing['reach']     # the stub answers by the cell it is given; cells are distinct per thing
    sb.call(CHOICE_ENTRY, {3: st.guest})
    return {'target': sb.read(st.guest + 476, 'H'), 'calls': list(log)}


def choice_reading(x: dict, **variant) -> dict:
    """0xe8ff4: things in list order; only those with +46 bit 0x4 (IsChoosable) and a back-of-queue cell are scored; a score below 10 (unsigned)
    is ignored; one that beats the best so far (signed) or equals it on an odd world turn must pass the counts-test 0xdc834 and the
    reachability test 0xe67c8 to become the target. The best starts at 0 and the target at none."""
    best, target, calls = 0, 0, []
    threshold = variant.get('threshold', 10)
    for index, thing in enumerate(x['things'], start=1):
        if not thing['flags'] & 4 or thing['cell'] == 0:
            continue
        score = thing['score'] & MASK32
        calls.append(('score', index))
        if score < threshold:
            continue
        tie = s32(score) == s32(best) and bool(x['turn'] & 1)
        if variant.get('ties') == 'never':
            tie = False
        if variant.get('ties') == 'always':
            tie = s32(score) == s32(best)
        if not (s32(score) > s32(best) or tie):
            continue
        calls.append(('counts', index))
        if not thing['counts']:
            continue
        calls.append(('reach', thing['cell']))
        if not thing['reach']:
            continue
        best, target = score, index
    return {'target': target, 'calls': calls}


def choice_plan(x: dict) -> dict:
    """The rule before F24 (GuestSimulation.ChooseAttraction): open attractions the guest can reach, the best strictly positive score wins and
    the first of equal scores keeps it (the random 0..9 bonus left out)."""
    best, target = 0, 0
    for index, thing in enumerate(x['things'], start=1):
        if not (thing['counts'] and thing['reach']) or thing['cell'] == 0:
            continue
        score = s32(thing['score'])
        if score > best:
            best, target = score, index
    return {'target': target}


def choice_equal(original: dict, other: dict) -> bool:
    return original['target'] == other['target'] and ('calls' not in other or original['calls'] == other['calls'])


def choice_csharp_request(x: dict) -> dict:
    return {'fn': 'attraction_choice', 'turn': x['turn'], 'things': [[t['flags'], t['cell'], t['score'] & MASK32, t['counts'], t['reach']] for t in x['things']]}


def choice_csharp_result(x: dict, r) -> dict:
    return {'target': r['target'], 'calls': [tuple(call) for call in r['calls']]}


def choice_inputs(rng: random.Random, n: int) -> list:
    cell_pool = list(range(1, 400))

    def make(**kw) -> dict:
        count = rng.randrange(0, 6)
        cells = rng.sample(cell_pool, count)
        things = [{'flags': rng.choice([4, 4, 4, 0, 5, 12, 8]), 'cell': rng.choice([cell, cell, cell, 0]),
                   'score': rng.choice([0, 5, 9, 10, 11, 50, 50, 100, 100, rng.randrange(0, 400), 0xFFFFFF9B, 0x80000000, 0xFFFFFFFF, 0x7FFFFFFF]),
                   'counts': rng.choice([0, 1, 1, 1]), 'reach': rng.choice([0, 1, 1, 1])} for cell in cells]
        x = {'turn': rng.randrange(0, 2 ** 31), 'things': things}
        x.update(kw)
        return x

    def things(*specs) -> list:
        return [{'flags': 4, 'cell': 10 + i, 'score': score, 'counts': counts, 'reach': reach} for i, (score, counts, reach) in enumerate(specs)]
    edges = [make(turn=0, things=things((50, 1, 1))), make(turn=0, things=things((9, 1, 1))), make(turn=0, things=things((10, 1, 1))),
             make(turn=0, things=things((50, 1, 1), (50, 1, 1))), make(turn=1, things=things((50, 1, 1), (50, 1, 1))),
             make(turn=1, things=things((50, 1, 1), (50, 0, 1))), make(turn=1, things=things((50, 1, 1), (50, 1, 0))),
             make(turn=0, things=things((50, 1, 1), (60, 0, 1), (55, 1, 1))), make(turn=0, things=things((0xFFFFFF9B, 1, 1), (50, 1, 1))),
             make(turn=0, things=things((0x80000000, 1, 1), (50, 1, 1))), make(turn=0, things=things((20, 1, 1), (0x80000000, 1, 1))),
             make(turn=0, things=[{'flags': 0, 'cell': 10, 'score': 90, 'counts': 1, 'reach': 1}]),
             make(turn=0, things=[{'flags': 4, 'cell': 0, 'score': 90, 'counts': 1, 'reach': 1}]),
             make(turn=3, things=things((50, 1, 1), (50, 1, 1), (50, 1, 1))), make(turn=2, things=things((50, 1, 1), (50, 1, 1), (50, 1, 1)))]
    return edges + [make() for _ in range(n)]


CHOICE_TARGET = Target('attraction-choice', 'Attraction choice loop 0xe8ff4', 'GUESTS.md "Decision" / the former GuestSimulation.ChooseAttraction',
                       CHOICE_ENTRY, CHOICE_ALLOWED, choice_stubs, choice_binary, choice_plan, choice_inputs, equal=choice_equal,
                       alternatives={'routine-reading': choice_reading},
                       csharp=('attraction_choice', choice_csharp_request, choice_csharp_result),
                       notes=['C# hook: bridge function "attraction_choice" (AttractionChoice.Pick on the chosen scores).',
                              'Executed: the thing-list walk 0xe8ff4 with the IsChoosable bit, the back-cell test, the score threshold and the tie rule on the world '
                              'turn. Stubs: 0xdd3f4 back cell, 0xe9164 score, 0xdc834 counts-test, 0xe67c8 reachability (all chosen, calls logged), 0xb678.'])

TARGETS = [SCORE_TARGET, OUTCOME_TARGET, CHOICE_TARGET]


# ============================================================================== (p) F25: the needs update of one park turn, 0xeece4
NEEDS_ENTRY = 0xEECE4
NEEDS_ALLOWED = [(0xEECE4, 0xEF240), (0xFA9A4, 0xFA9B0), (0xD7310, 0xD7340), (0xEFD84, 0xEFDB8), (0xEA2B8, 0xEA318), (0x105328, 0x10536C)]
NEEDS_SPEED, NEEDS_PREDICATE, NEEDS_THOUGHT, NEEDS_BUBBLE, NEEDS_THINK = 0xE6B28, 0xE6C6C, 0xE8C58, 0xEB544, 0xFB508
WORLD_TURN_OFFSET = 0x1E0000 - 22772   # game + 0x1da70c
WORLD_RNG_OFFSET = 0x1E0000 - 22776    # game + 0x1da708
NEEDS_STATE = type('NeedsState', (), {})()
NEEDS_BONUS_TABLE = -27224            # TOC slot -> two u16: the toilet speed term +194 without and with the need above 80
SPEED_SEED = 0x7777


def needs_stubs(target: Target) -> dict:
    state = NEEDS_STATE

    def record(name):
        def stub(m: Machine) -> None:
            if m.gpr[3] != state.guest and m.gpr[3] != state.guest + 48:
                raise AssertionError(f'{name} called without the guest')
            target.log.append((name,))
        return stub

    def predicate(m: Machine) -> None:
        if m.gpr[3] != state.guest:
            raise AssertionError('0xe6c6c called without the guest')
        target.log.append(('predicate',))
        m.gpr[3] = state.x['predicate']

    def assertion(m: Machine) -> None:
        target.log.append(('assert', m.gpr[3] & 0xFF))

    return {NEEDS_SPEED: record('speed'), NEEDS_PREDICATE: predicate, NEEDS_THOUGHT: record('thought'), NEEDS_BUBBLE: record('bubble'),
            NEEDS_THINK: record('think'), ASSERT: assertion}


def needs_setup(sb, x: dict):
    st = NEEDS_STATE
    st.x = x
    game = sb.alloc(0x1B13DC + 10 * 16384 + 64)
    sb.write(toc_pointer(sb, ARRIVAL_GAME_POINTER), 'I', game)
    sb.write(toc_pointer(sb, MAP_POINTER_SLOT), 'I', game + 728)
    sb.write(game + WORLD_TURN_OFFSET, 'I', x['turn'] & MASK32)
    sb.write(game + WORLD_RNG_OFFSET, 'I', x['rng'] & MASK32)
    cell = 1 + x['gx'] + (x['gy'] << 7)
    record = game + 728 + 0x1B1104 + 10 * (cell - 1)
    for k, value in enumerate(x['cells']):
        sb.write(record + 2 * k, 'h', value)
    st.guest = sb.struct(1024, {0: ('H', x['id']), 4: ('B', x['gx']), 6: ('B', x['gy']), 194: ('H', x['bonus']),
                                412: ('f', x['happy']), 420: ('f', x['thirst']), 424: ('f', x['hunger']), 428: ('f', x['toilet']),
                                432: ('f', x['ill']), 444: ('i', x['exit']), 532: ('I', x['countdown'] & MASK32),
                                544: ('I', x['state']), 548: ('I', x['prev'])})
    for slot, value in enumerate(x['temp']):
        sb.write(st.guest + 488 + 2 * slot, 'H', value)
    st.game = game
    return st


def needs_binary(sb, x: dict, log) -> dict:
    st = needs_setup(sb, x)
    sb.call(NEEDS_ENTRY, {3: st.guest})
    failed = [entry for entry in log if entry[0] == 'assert' and entry[1] == 0]
    if failed:
        raise AssertionError('an assertion failed in the needs update')
    return {'happy': sb.read(st.guest + 412, 'f'), 'thirst': sb.read(st.guest + 420, 'f'), 'hunger': sb.read(st.guest + 424, 'f'),
            'toilet': sb.read(st.guest + 428, 'f'), 'ill': sb.read(st.guest + 432, 'f'), 'exit': sb.read(st.guest + 444, 'i'),
            'bonus': sb.read(st.guest + 194, 'H'), 'countdown': sb.read(st.guest + 532, 'I'),
            'temp': [sb.read(st.guest + 488 + 2 * slot, 'H') for slot in range(4)], 'rng': sb.read(st.game + WORLD_RNG_OFFSET, 'I'),
            'calls': [entry for entry in log if entry[0] != 'assert']}


def clamp_add(meter: float, delta: float) -> float:
    """The clamp of every meter write: single-precision sum, above 100 gives 100, below 0 gives 0, a NaN passes."""
    return sale_clamp(f32(delta + meter))


def byte_of(meter: float) -> int:
    return fctiwz(meter) & 0xFF


def needs_reading(x: dict, **variant) -> dict:
    """0xeece4 as read: the speed update first; then, only when the guest's id and the world turn agree in their low two bits, the exit counter loses 1,
    the three cell words of the guest's cell are added to happiness, illness and hunger, on a turn that is a multiple of 16 toilet gains 1 and hunger and
    thirst 2, each of illness, hunger, thirst and toilet whose byte is exactly 100 costs happiness 1, and the toilet speed term +194 is rewritten from the toilet
    byte; then, outside the states 16 and 17 and when the cell predicate holds, a draw modulo 10 may pick a thought and the thought countdown runs. Every call
    ends with the queue-link check and the temporary history decay on turns that are multiples of 20."""
    meters = {name: x[name] for name in ('happy', 'thirst', 'hunger', 'toilet', 'ill')}
    exit_level, bonus, countdown, rng, calls = x['exit'], x['bonus'], x['countdown'], x['rng'], [('speed',)]
    turn, phase = x['turn'] & MASK32, (x['id'] & 3) == (x['turn'] & 3)
    if variant.get('every_turn'):
        phase = True
    if phase:
        exit_level = s32(exit_level - 1) if not variant.get('no_exit') else exit_level
        c0, c1, c2 = x['cells']
        meters['happy'] = clamp_add(meters['happy'], float(c0))
        meters['ill'] = clamp_add(meters['ill'], float(c1))
        meters['hunger'] = clamp_add(meters['hunger'], float(c2))
        if turn & (variant.get('growth_mask', 15)) == 0:
            meters['toilet'] = clamp_add(meters['toilet'], 1.0)
            meters['hunger'] = clamp_add(meters['hunger'], variant.get('hunger_step', 2.0))
            meters['thirst'] = clamp_add(meters['thirst'], 2.0)
        if not variant.get('no_penalty'):
            for name in ('ill', 'hunger', 'thirst', 'toilet'):
                if (byte_of(meters[name]) == 100) if not variant.get('penalty_at_least') else (byte_of(meters[name]) >= 100):
                    meters['happy'] = clamp_add(meters['happy'], -1.0)
        bonus = 25 if byte_of(meters['toilet']) > 80 else 0
        if x['state'] not in (16, 17):
            calls.append(('predicate',))
            if x['predicate']:
                rng, drawn = det_model.world_next(rng)
                if drawn % 10 == 0:
                    calls.append(('thought',))
                if countdown:
                    countdown = (countdown - 1) & MASK32
                    if countdown == 0:
                        calls.append(('bubble',))
                calls.append(('think',))
            else:
                calls.append(('think',))
        else:
            calls.append(('think',))
    temp = list(x['temp'])
    if turn % 20 == 0 and not variant.get('no_decay'):
        temp = [0] + temp[:3]
    return {**meters, 'exit': exit_level, 'bonus': bonus, 'countdown': countdown, 'temp': temp, 'rng': rng, 'calls': calls}


def needs_plan(x: dict) -> dict:
    """The rule before F25 (GUESTS.md "Needs"): hunger +0.35/s, thirst +0.45/s, toilet +0.3/s, nausea -0.2/s down to 0 and the exit counter -1 per
    second, all continuous (one park turn is 0.248 s), no cell words, no happiness cost of a full meter, no growth gate on the turn, and the toilet speed
    term on the guest's phase."""
    dt = 0.248
    out = {'happy': x['happy'], 'thirst': f32(min(150.0, x['thirst'] + 0.45 * dt)), 'hunger': f32(min(150.0, x['hunger'] + 0.35 * dt)),
           'toilet': f32(min(150.0, x['toilet'] + 0.3 * dt)), 'ill': f32(max(0.0, x['ill'] - 0.2 * dt)), 'exit': x['exit'] - dt, 'bonus': x['bonus']}
    if (x['id'] & 3) == (x['turn'] & 3):
        out['bonus'] = 25 if byte_of(out['toilet']) > 80 else 0
    return out


def needs_equal(original: dict, other: dict) -> bool:
    for name in ('happy', 'thirst', 'hunger', 'toilet', 'ill'):
        if name in other and not same_float(original[name], other[name]):
            return False
    for name in ('exit', 'bonus', 'temp', 'countdown', 'rng', 'calls'):
        if name in other and original[name] != other[name]:
            return False
    return True


def needs_csharp_request(x: dict) -> dict:
    return {'fn': 'needs_turn', 'turn': x['turn'] & MASK32, 'id': x['id'], 'happy_bits': bits(x['happy']), 'thirst_bits': bits(x['thirst']),
            'hunger_bits': bits(x['hunger']), 'toilet_bits': bits(x['toilet']), 'ill_bits': bits(x['ill']), 'exit': x['exit'], 'cells': x['cells'],
            'bonus': x['bonus'], 'temp': x['temp']}


def needs_csharp_result(x: dict, r) -> dict:
    return {'happy': T.from_bits(r['happy_bits']), 'thirst': T.from_bits(r['thirst_bits']), 'hunger': T.from_bits(r['hunger_bits']),
            'toilet': T.from_bits(r['toilet_bits']), 'ill': T.from_bits(r['ill_bits']), 'exit': r['exit'], 'bonus': r['bonus'], 'temp': r['temp']}


def needs_inputs(rng: random.Random, n: int) -> list:
    def meter() -> float:
        return rng.choice([0.0, 100.0, 99.0, 99.5, 100.5, 101.0, 79.0, 80.0, 80.5, 81.0, 98.5, random_float(rng, 0, 100), random_float(rng, 0, 100),
                           random_float(rng, 0, 100), float(rng.randrange(0, 101)), -3.0, 130.0, 255.5, 256.5])

    def cell_word() -> int:
        return rng.choice([0, 0, 0, 0, 1, -1, 2, -2, 5, -5, 100, -100, 32767, -32768, rng.randrange(-32768, 32768)])

    def make(**kw) -> dict:
        ident = rng.choice([rng.randrange(1, 10240), rng.randrange(1, 10240), rng.randrange(0, 65536)])
        mode = rng.random()
        turn = rng.getrandbits(32)
        if mode < 0.55:   # the guest's phase
            turn = (turn & ~3) | (ident & 3)
        if mode < 0.2:    # a multiple of 16 for phase 0 guests
            ident, turn = (ident & ~3), turn & ~15
        elif 0.2 <= mode < 0.3:  # a multiple of 20 (the history decay)
            turn = (turn // 20) * 20
        x = {'turn': turn, 'id': ident, 'gx': rng.randrange(0, 128), 'gy': rng.randrange(0, 128),
             'happy': meter(), 'thirst': meter(), 'hunger': meter(), 'toilet': meter(), 'ill': meter(),
             'exit': rng.choice([0, 1, 2, -1, 120, 179, 60, s32(rng.getrandbits(32)), -2 ** 31, 2 ** 31 - 1]),
             'cells': [cell_word(), cell_word(), cell_word()], 'bonus': rng.choice([0, 25, 0x1234]),
             'state': rng.choice([6, 7, 7, 10, 11, 16, 17, 0, 18, rng.randrange(0, 22)]), 'prev': rng.randrange(0, 22),
             'predicate': rng.choice([0, 1, 1]), 'countdown': rng.choice([0, 0, 1, 2, 5, MASK32]), 'rng': rng.getrandbits(32),
             'temp': [rng.choice([0, 0, 7, rng.randrange(1, 300)]) for _ in range(4)]}
        x.update(kw)
        return x
    base = dict(turn=0, id=4, gx=10, gy=10, happy=50.0, thirst=10.0, hunger=10.0, toilet=10.0, ill=0.0, exit=120, cells=[0, 0, 0], bonus=0,
                state=6, prev=6, predicate=0, countdown=0, rng=1, temp=[0, 0, 0, 0])

    def edge(**kw) -> dict:
        return dict(base, **kw)
    edges = [edge(), edge(id=5),                               # the phase: turn 0 meets ids 4 and 8, not 5
             edge(turn=16, id=8), edge(turn=17, id=1), edge(turn=15, id=3), edge(turn=1, id=5),
             edge(turn=16, id=8, hunger=99.0, thirst=99.0, toilet=99.5),   # 99 + 2 = 101 clamps to 100: the exact-100 penalty
             edge(turn=16, id=8, hunger=98.0, thirst=98.0, toilet=99.0),   # 98 + 2 = 100 byte 100: penalty; toilet 99 + 1 = 100
             edge(turn=16, id=8, hunger=97.5, thirst=97.9, toilet=98.5, ill=100.0),
             edge(turn=4, id=4, hunger=100.0, thirst=100.0, toilet=100.0, ill=100.0),   # four meters at 100: happiness -4
             edge(turn=4, id=4, hunger=100.9, thirst=100.0, toilet=80.0), edge(turn=4, id=4, toilet=80.9), edge(turn=4, id=4, toilet=81.0),
             edge(turn=4, id=4, cells=[5, -5, 3]), edge(turn=4, id=4, cells=[-100, 100, -100], happy=10.0, ill=90.0, hunger=20.0),
             edge(turn=4, id=4, cells=[32767, -32768, 32767]), edge(turn=4, id=4, exit=0), edge(turn=4, id=4, exit=-2 ** 31),
             edge(turn=4, id=4, state=16, predicate=1), edge(turn=4, id=4, state=17, predicate=1), edge(turn=4, id=4, state=15, predicate=1),
             edge(turn=4, id=4, state=7, predicate=1, rng=0, countdown=1), edge(turn=4, id=4, state=7, predicate=1, rng=0, countdown=2),
             edge(turn=20, id=1, temp=[7, 8, 9, 10]), edge(turn=40, id=0, temp=[7, 8, 9, 10]), edge(turn=21, id=1, temp=[7, 8, 9, 10]),
             edge(turn=4, id=4, state=11), edge(turn=4, id=4, state=12, prev=1), edge(turn=3, id=7, state=8, prev=13)]
    return edges + [make() for _ in range(n)]


NEEDS_TARGET = Target('guest-needs-turn', 'The guest needs update of one park turn 0xeece4', 'GUESTS.md "Needs" / the former GuestSimulation.UpdateNeeds',
                      NEEDS_ENTRY, NEEDS_ALLOWED, needs_stubs, needs_binary, needs_plan, needs_inputs, equal=needs_equal,
                      alternatives={'routine-reading': needs_reading},
                      csharp=('needs_turn', needs_csharp_request, needs_csharp_result),
                      notes=['C# hook: bridge function "needs_turn" (NeedsUpdate.Apply and NeedsUpdate.DecayHistory). C# has no thought, bubble or world generator, so only '
                             'the meters, the exit counter, the toilet speed term and the temporary history are compared.',
                             'Executed: the whole function 0xeece4, the id getter 0xfa9a4, the cell record helper 0xd7310 (on a synthetic map), the queue-state predicate '
                             '0xefd84, the history decay 0xea2b8 and the world generator 0x105328. Stubs (logged): 0xe6b28 speed update, 0xe6c6c cell predicate (chosen), '
                             '0xe8c58 thought, 0xeb544 bubble clear, 0xfb508 thought object update, 0xb678 assertion (a zero condition fails the run).'])


# ============================================================================== (q) F25: the guest constructor's starting values, 0xe7644
CTOR_ENTRY, CTOR_STOP = 0xE7644, 0xE7958
CTOR_ALLOWED = [(0xE7644, 0xE795C), (0x105328, 0x10536C), (0xFA9A4, 0xFA9B0)]
CTOR_BASE, CTOR_ARRAY = 0xE4810, 0x1C4A34
CTOR_STATE = type('CtorState', (), {})()


def ctor_stubs(target: Target) -> dict:
    def glue(m: Machine) -> None:
        # the import glue 0x1c4a34 saves the caller's TOC at 20(r1) before it jumps through the transition vector; the caller reloads r2 from there
        m.memory.w32(m.gpr[1] + 20, m.gpr[2])
        target.log.append(('array',))
    return {CTOR_BASE: lambda m: target.log.append(('base',)), CTOR_ARRAY: glue}


def ctor_binary(sb, x: dict, log) -> dict:
    game = sb.alloc(0x1E0000 + 64)
    sb.write(toc_pointer(sb, ARRIVAL_GAME_POINTER), 'I', game)
    sb.write(game + WORLD_TURN_OFFSET, 'I', x['turn'] & MASK32)
    sb.write(game + WORLD_RNG_OFFSET, 'I', x['rng'] & MASK32)
    settings = toc_pointer(sb, WEIGHTS_SLOT)
    sb.write(settings + 12, 'i', x['exit'])
    sb.write(settings + 16, 'i', x['exit_var'])
    sb.write(settings + 20, 'i', x['cash_pc'])
    sb.write(settings + 156, 'I', x['prank'] & MASK32)
    sb.write(settings + 404, 'i', len(x['types']))
    for index, (pref, cash, boredom) in enumerate(x['types']):
        sb.write(settings + 164 + 12 * index, 'i', pref)
        sb.write(settings + 168 + 12 * index, 'i', cash)
        sb.write(settings + 172 + 12 * index, 'i', boredom)
    guest = sb.struct(1024, {0: ('H', x['id'])})
    sb.call(CTOR_ENTRY, {3: guest, 4: 0}, stop=CTOR_STOP)
    return {'happy': sb.read(guest + 412, 'f'), 'money': sb.read(guest + 416, 'i'), 'thirst': sb.read(guest + 420, 'f'),
            'hunger': sb.read(guest + 424, 'f'), 'toilet': sb.read(guest + 428, 'f'), 'ill': sb.read(guest + 432, 'f'),
            'litter': sb.read(guest + 436, 'f'), 'exit': sb.read(guest + 444, 'i'), 'prank': sb.read(guest + 448, 'B'),
            'type': sb.read(guest + 496, 'B'), 'created': sb.read(guest + 468, 'I'), 'rng': sb.read(game + WORLD_RNG_OFFSET, 'I')}


def ctor_draws(state: int, count: int = 8) -> tuple[list[int], int]:
    words = []
    for _ in range(count):
        state, word = det_model.world_next(state)
        words.append(word)
    return words, state


def ctor_reading(x: dict, **variant) -> dict | str:
    """0xe7644's starting values, from the world generator's draws in the order the routine takes them: (1) the exit counter ExitLevel plus the
    signed remainder of the draw by 2 * ExitLevelVar, minus ExitLevelVar; (2) the type, the draw modulo the type count; (3) the money,
    StartingCash * (100 + signed remainder by 2 * StartingCashVarPc + 1 - StartingCashVarPc) / 100, at least 0; (4) thirst and (5) hunger,
    a draw modulo 50 (unsigned); (6) toilet, a draw modulo 30; (7) one draw dropped; (8) the prankery byte, 100 + id modulo 3 when a draw modulo 100 is
    below PrankeryLikelihood, else 0. Happiness is 50, illness and litter 0, the creation turn is stored."""
    words, rng = ctor_draws(x['rng'])
    if x['exit_var'] == 0 or not x['types']:   # divw by zero: the first undefined division of the routine ends the run
        return UNDEFINED
    exit_level = s32(x['exit'] + (trunc_rem(s32(words[0]), 2 * x['exit_var']) - x['exit_var']))
    kind = trunc_rem(s32(words[1]), len(x['types']))
    cash_t = x['types'][kind][1]
    delta = trunc_rem(s32(words[2]), 2 * x['cash_pc'] + 1) - x['cash_pc']
    money = s32(cash_t * (100 + delta))
    money = max(0, trunc_div(money, 100))
    modulus = variant.get('thirst_modulus', 50)
    prank = 100 + x['id'] % 3 if words[7] % 100 < (x['prank'] & MASK32) else 0
    return {'happy': 50.0 if not variant.get('happy60') else 60.0, 'money': money,
            'thirst': f32(float(words[3] % modulus)), 'hunger': f32(float(words[4] % 50)),
            'toilet': f32(float(words[5] % (30 if not variant.get('toilet50') else 50))), 'ill': 0.0, 'litter': 0.0, 'exit': exit_level,
            'prank': prank, 'type': kind, 'created': x['turn'] & MASK32, 'rng': rng}


def trunc_rem(a: int, b: int) -> int:
    """The remainder of divw / mullw / subf: a - (a / b) * b with a truncating division (the sign of the dividend)."""
    return a - trunc_div(a, b) * b


def ctor_plan(x: dict) -> dict:
    """The rule before F25 (GuestSimulation.Create): a uniform type, money within +-StartingCashVarPc percent of the type's cash, the exit counter in
    seconds within +-ExitLevelVar of ExitLevel, happiness 60 and every need 0, with the same draws in OpenTPW's order (type, money, exit)."""
    words, rng = ctor_draws(x['rng'])
    kind = words[0] % len(x['types'])
    cash_t = x['types'][kind][1]
    variance = cash_t * x['cash_pc'] // 100
    money = max(0, cash_t + (-variance + words[1] % (2 * variance + 1)))
    exit_level = x['exit'] + (-x['exit_var'] + words[2] % (2 * x['exit_var'] + 1))
    return {'happy': 60.0, 'money': money, 'thirst': 0.0, 'hunger': 0.0, 'toilet': 0.0, 'ill': 0.0, 'exit': exit_level, 'type': kind}


def ctor_equal(original, other) -> bool:
    if original == UNDEFINED or other == UNDEFINED or isinstance(original, str) or isinstance(other, str):
        return original == other
    for name in ('happy', 'thirst', 'hunger', 'toilet', 'ill', 'litter'):
        if name in other and not same_float(original[name], other[name]):
            return False
    return all(original[name] == other[name] for name in ('money', 'exit', 'prank', 'type', 'created', 'rng') if name in other)


def ctor_csharp_request(x: dict) -> dict:
    words, _ = ctor_draws(x['rng'])
    return {'fn': 'guest_start', 'draws': words, 'exit': x['exit'], 'exit_var': x['exit_var'], 'cash_pc': x['cash_pc'], 'types': x['types']}


def ctor_csharp_result(x: dict, r) -> dict | str:
    if isinstance(r, dict) and 'exception' in r:
        return UNDEFINED
    return {'happy': T.from_bits(r['happy_bits']), 'money': r['money'], 'thirst': T.from_bits(r['thirst_bits']), 'hunger': T.from_bits(r['hunger_bits']),
            'toilet': T.from_bits(r['toilet_bits']), 'ill': T.from_bits(r['ill_bits']), 'litter': T.from_bits(r['litter_bits']), 'exit': r['exit'],
            'type': r['type']}


def ctor_inputs(rng: random.Random, n: int) -> list:
    shipped_types = [(80, 300, 40), (65, 500, 30), (50, 600, 40), (35, 700, 35), (65, 750, 30), (80, 600, 40), (45, 400, 30), (80, 500, 40)]

    def make(**kw) -> dict:
        types = rng.choice([shipped_types, shipped_types, [(50, 500, 40)], [(rng.randrange(0, 101), rng.randrange(0, 2000), rng.randrange(1, 100))
                                                                           for _ in range(rng.randrange(1, 21))]])
        x = {'rng': rng.getrandbits(32), 'turn': rng.getrandbits(32), 'id': rng.choice([rng.randrange(1, 10240), rng.randrange(0, 65536)]),
             'exit': rng.choice([120, 120, rng.randrange(0, 400)]), 'exit_var': rng.choice([60, 60, rng.randrange(1, 100)]),
             'cash_pc': rng.choice([15, 15, rng.randrange(0, 60)]), 'prank': rng.choice([5, 5, 0, 100, rng.randrange(0, 200)]), 'types': types}
        x.update(kw)
        return x
    edges = [make(rng=0), make(rng=1), make(rng=MASK32), make(rng=0x7FFFFFFF), make(rng=0x80000000),
             make(exit=120, exit_var=60, cash_pc=15, prank=5, types=shipped_types),
             make(exit=0, exit_var=1), make(exit_var=2), make(cash_pc=0), make(cash_pc=100, types=[(50, 500, 40)]),
             make(types=[(50, 0, 40)]), make(prank=100), make(prank=0), make(id=0), make(id=1), make(id=2), make(id=3)]
    # successors of the generator that land on a draw with INT_MIN as the first word (abs keeps the sign bit)
    return edges + [make() for _ in range(n)]


CTOR_TARGET = Target('guest-constructor', 'The guest constructor starting values 0xe7644', 'GUESTS.md "Starting values" / the former GuestSimulation.Create',
                     CTOR_ENTRY, CTOR_ALLOWED, ctor_stubs, ctor_binary, ctor_plan, ctor_inputs, equal=ctor_equal,
                     alternatives={'routine-reading': ctor_reading},
                     csharp=('guest_start', ctor_csharp_request, ctor_csharp_result),
                     notes=['C# hook: bridge function "guest_start" (GuestStartValues.FromDraws on the eight world draws the Python side computes with det_model; C# draws '
                            'from its own stream, GuestRandom, DET-014). Only the values C# models are compared: happiness, money, the three needs, illness, litter, the '
                            'exit counter and the type (the prankery byte and the creation turn are not).',
                            'Executed: the constructor 0xe7644 from its entry to 0xe7958 and the world generator 0x105328 (eight draws). Stubs (logged): the person constructor '
                            '0xe4810 and the array initialiser glue 0x1c4a34. The settings words come from the settings block (+12 ExitLevel, +16 ExitLevelVar, +20 StartingCashVarPc, '
                            '+156 PrankeryLikelihood, +404 the type count, +164 + 12 t the PeepTypes rows).'])


TARGETS += [NEEDS_TARGET, CTOR_TARGET]


# ============================================================================== (r) F26: the leaving test of the state-6 handler, 0xebdfc
EXIT_ENTRY = 0xEBDFC
EXIT_STAY, EXIT_LEAVE = 0xEBEF0, 0xEBE30
EXIT_ALLOWED = [(0xEBDFC, 0xEBEF4), (0x1091AC, 0x1091B8)]
CLOSING_OFFSET = 0x1E0000 - 22768      # game + 0x1da710: nonzero while the park closes (read by 0x1091ac)


def exit_binary(sb, x: dict, log) -> bool:
    game = sb.alloc(0x1E0000 + 64)
    sb.write(toc_pointer(sb, ARRIVAL_GAME_POINTER), 'I', game)
    sb.write(game + CLOSING_OFFSET, 'i', x['closing'])
    guest = sb.struct(1024, {412: ('f', x['happy']), 444: ('i', x['exit'])})
    sb.call(EXIT_ENTRY, {1: STACK_BASE + STACK_SIZE - 0x1000, 31: guest, 29: toc_pointer(sb, ARRIVAL_GAME_POINTER)}, stop=(EXIT_STAY, EXIT_LEAVE))
    if sb.machine.pc == sb.image.code(EXIT_LEAVE):
        return True
    if sb.machine.pc == sb.image.code(EXIT_STAY):
        return False
    raise AssertionError('the fragment ended elsewhere')


def exit_reading(x: dict, **variant) -> bool:
    """The state-6 handler leaves when the happiness byte (the meter truncated, its low byte) is 0, or the exit counter is exactly 0, or the game's closing
    word (game +0x1da710) is nonzero."""
    low = byte_of(x['happy'])
    if variant.get('threshold') is not None:
        low = variant['threshold'] + 1 if x['happy'] > variant['threshold'] else 0
    if variant.get('no_byte'):
        low = fctiwz(x['happy'])
    counter = x['exit'] <= 0 if variant.get('at_most') else x['exit'] == 0
    return low == 0 or counter or (x['closing'] != 0 and not variant.get('no_closing'))


def exit_plan(x: dict) -> bool:
    """The rule before F26 (GuestSimulation.ShouldLeave): the exit counter at or below 0 or happiness at or below 10 (a park never closes in C#)."""
    return x['exit'] <= 0 or x['happy'] <= 10


def exit_csharp_request(x: dict) -> dict:
    return {'fn': 'guest_leaves', 'happy_bits': bits(x['happy']), 'exit': x['exit'], 'closing': x['closing']}


def exit_inputs(rng: random.Random, n: int) -> list:
    def make(**kw) -> dict:
        x = {'happy': rng.choice([50.0, 0.0, 0.5, 0.99, 1.0, 1.5, 10.0, 10.5, 11.0, 100.0, 255.9, 256.0, 256.5, 511.0, -1.0, -0.5, math.nan, math.inf,
                                  -math.inf, random_float(rng, 0, 100), random_float(rng, 0, 20)]),
             'exit': rng.choice([120, 1, 0, -1, 2, 60, 179, -2 ** 31, 2 ** 31 - 1, rng.randrange(-200, 200)]),
             'closing': rng.choice([0, 0, 0, 1, -1, 2 ** 31 - 1])}
        x.update(kw)
        return x
    edges = [make(happy=50.0, exit=120, closing=0), make(happy=0.5, exit=120, closing=0), make(happy=0.99, exit=120, closing=0),
             make(happy=1.0, exit=120, closing=0), make(happy=10.0, exit=120, closing=0), make(happy=10.5, exit=120, closing=0),
             make(happy=50.0, exit=0, closing=0), make(happy=50.0, exit=1, closing=0), make(happy=50.0, exit=-1, closing=0),
             make(happy=50.0, exit=120, closing=1), make(happy=256.0, exit=120, closing=0), make(happy=-0.5, exit=120, closing=0),
             make(happy=math.nan, exit=120, closing=0)]
    return edges + [make() for _ in range(n)]


EXIT_TARGET = Target('guest-exit-test', 'The state-6 leaving test 0xebdfc', 'GUESTS.md "Decision" / the former GuestSimulation.ShouldLeave',
                     EXIT_ENTRY, EXIT_ALLOWED, lambda t: {}, exit_binary, exit_plan, exit_inputs,
                     alternatives={'routine-reading': exit_reading},
                     csharp=('guest_leaves', exit_csharp_request, lambda x, r: bool(r)),
                     notes=['C# hook: bridge function "guest_leaves" (GuestLifecycle.ShouldLeave).',
                            'Fragment of the state-6 handler 0xebbd0: the happiness byte test, the exit counter test (cmpwi 0, beq) and the closing word test through 0x1091ac '
                            '(executed); the run ends at 0xebe30 (the guest leaves: happiness loses BigHappinessChange, state 18) or at 0xebef0 (the guest stays).'])

TARGETS += [EXIT_TARGET]

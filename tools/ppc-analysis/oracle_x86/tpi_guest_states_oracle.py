"""Theme Park Inc's native guest states (phases P1 to P3a) run in the x86 oracle sandbox and compared with the C# port's reading (ThemeParkIncGuestStates, GuestSimulation.ThemeParkInc).

Dev-only tooling like the rest of oracle_x86: Unicorn and the identified Theme Park Inc no-CD Game.exe (OPENTPW_TPI_EXE, SHA-256 pinned) are needed, nothing here is shipped.
Phase P1's four routines run natively, at the game thread's x87 control word 0x7F (24-bit significand):
- the catch test 0x614720, exhaustively over the state words -1..40, against ThemeParkIncGuestStates.IsCatchable;
- the state setter 0x61A540 for every state phase P1 enters (0..6, 8..16, 18, 22..25 and 27..30), over random guests, booths and turns: the world generator 0x6493E0, the booth cell
  0x5D79B0, the mode store 0x5D0F90, the speed term 0x60B3F0 and the stay turns 0x6118A0 run for real, the analyser's stay ring 0x5AF080 is stubbed and recorded; the state word, the mode,
  the wait +0x228, the heading +0x2C, the stamps, the counters, the speed term and the stay are compared with the port's entries;
- the pre-update 0x619B50 over random meters, cell words, turns, ids, umbrella states, balloon countdowns and states: the region record 0x5D7420, the clamped adder 0x60D0D0, _ftol 0x86FAB0, the
  speed term 0x60B3F0, the world generator and the history shift 0x611D60/0x611D20 run for real; the speed average 0x60BC90, the weather rule 0x611050, the park-cell test 0x60BE00, the
  thought pick 0x60FC60, the thought object 0x638960/0x6388F0 and the balloon release 0x613D00 are stubbed and recorded. The meters, the exit counter, the speed term, the queue words, the
  history, the countdown, the draws and the stubs' calls are compared with ThemeParkIncPreUpdate;
- the booth's verdict 0x6157D0 over random divisors, fee floors, multipliers, park excitements and fees: the world generator runs for real, the excitement 0x5B05D0, the park record 0x64EBE0
  and the opinion ring 0x5AEDE0 are stubbed and recorded; the verdict, the opinion and the draw are compared with ThemeParkIncBoothVerdict.
Phase P2 adds:
- state 6 (0x614A40) over random meters, prank codes 0..6, turns, stamps, umbrella states and draws A: the world generator, _ftol, the id 0x6375C0, the happiness loss 0x614460 and the heading
  0x91E074 run natively; the mood 0x60CDD0, the spot animation 0x60FB30, the drop 0x5D8C80, the sounds, the bin search 0x618D10, the closed park 0x64FA30, the bus-stop walk 0x615D50, the setter,
  the show finder 0x6193B0, the kind finder 0x5B1B20, the entertainer finder 0x619100, the thought, the message post 0x436DD0, the major decision 0x610080, the wander 0x609F70, the CRT rand()
  0x870D38 and the gated searches (the plant, the infodesk, the guest finder) are stubbed and recorded, the searches finding nothing where the port gates them; the calls, the meters, +0x1B8, the
  heading, +0x29C, +0x2B1, the prank code, +0x204, +0x224, the walk mask and the draws are compared with UpdateTpiIdle, prank codes 0..8 included (the bin prank's search and VAR 10, rumours and
  whinging with the setter, the stand animation 0x60B6E0 and the thought recorded; the setter's state 10, 24 or 25 is the port's gated entry, recorded at the same point);
- the spot animation 0x60FB30 for the animations 0..15 (the id, the mode store native; the sound and the setter recorded) and the mood 0x60CDD0 over generations -1..3 and the bank's byte +0x206;
- the dispatch 0x619FF0 in states 7 (0x61A179) and 8 (0x61A382), the walk, the wander, the restore and the setter stubbed;
- state 9 (0x616F10) with the walk, the bin's customer, its script variables and the drop stubbed; the dirt test 0x5E3990 and its rows, which the port gates, are left out of the comparison.
Phase P3a adds:
- the attraction score 0x6101E0 over random guests, cells, meters, histories, weights, type words (gender, appearance, golden ticket, upgrade, weather and generation percentages) and park
  weather: the id, the region record and _ftol native, the special-ride test, the type record, the bank's gender, the back cell, the desk's target, the queue count, the rating, the park
  record and the age stubbed; against ThemeParkIncAttractionScore.Compute (port_score);
- the queue place 0x5E0280, the room 0x5DFDD0 and the length test 0x5DFF10 with the limit 0x5DFE00 over random lists of guests in random states, the predicate 0x61AC40 native;
- states 13 (0x616AC0) and 14 (0x6176C0) with every callee but the world generator, _ftol and the id stubbed and recorded (the CRT rand() at 0x61795E among them), and state 15 in the
  dispatch; the calls, the fields and the draws against UpdateTpiWalkingToMajor, UpdateTpiStandingInQueue and UpdateTpiTakingQueueStep.

Usage: python3 -I tools/ppc-analysis/oracle_x86/tpi_guest_states_oracle.py [cases]
"""
from __future__ import annotations

import math
import random
import struct
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))

from x86_harness import Image, Sandbox, executable_path  # noqa: E402

MASK = 0xFFFFFFFF
WORLD_POINTER, CELLS_POINTER, TURN, SEED = 0xADE6B0, 0xADE6B4, 0x1EAB40, 0x1EAB3C
REGION = 0x1C1104
BOOTHS, BOOTH_TABLE = 0xA025B4, 0xA02610
CONTROL_WORD = 0x7F


def thunks(*addresses: int) -> list:
    return [(address, address + 5) for address in addresses]


def f32(value: float) -> float:
    return struct.unpack('<f', struct.pack('<f', value))[0]


def x87_single_product(factor: float, integer: int) -> float:
    """GuestSimulation.X87SingleProduct: the exact product rounded once to a 24-bit significand (nearest even)."""
    from fractions import Fraction
    exact = Fraction(factor) * integer
    if exact == 0:
        return 0.0
    sign = -1 if exact < 0 else 1
    exact = abs(exact)
    exponent = exact.numerator.bit_length() - exact.denominator.bit_length()
    if Fraction(2) ** exponent > exact:
        exponent -= 1
    unit = Fraction(2) ** (exponent - 23)
    quotient, remainder = divmod(exact, unit)
    half = unit / 2
    if remainder > half or remainder == half and quotient % 2 == 1:
        quotient += 1
    return sign * float(quotient * unit)


def add(meter: float, delta: float) -> float:
    """MeterMath.Add: the clamped single sum."""
    value = f32(meter + delta)
    return 100.0 if value > 100.0 else 0.0 if value < 0.0 else value


def byte(meter: float) -> int:
    """MeterMath.Byte: the truncated meter's low byte."""
    return int(meter) & 0xFF


class World:
    """WorldRng: the LCG, rotated right 13, its absolute value."""

    def __init__(self, state: int):
        self.state = state & MASK

    def word(self) -> int:
        step = (self.state * 0x19660D + 0x3C6EF35F) & MASK
        self.state = ((step >> 13) | (step << 19)) & MASK
        signed = self.state - (1 << 32) if self.state >> 31 else self.state
        return (-signed) & MASK if signed < 0 else self.state


# ---- The port's reading -------------------------------------------------------------------------------------------------------

def catchable(state: int) -> bool:
    """ThemeParkIncGuestStates.IsCatchable."""
    return not (0 <= state <= 5 or 0xE <= state <= 0x15 or 0x17 <= state <= 0x1E)


ENTRY_MODE = {**{s: 1 for s in (0, 2, 5, 7, 9, 13, 15, 16, 22, 29)}, **{s: 3 for s in (1, 3, 6, 14, 17, 27, 28, 30)}}


def port_setter(state: int, guest: dict, booths: list, turn: int, world: World) -> dict:
    """GuestSimulation.SetThemeParkIncState's entries (the fields the oracle reads)."""
    out = dict(guest)
    out['state'] = state
    if state in ENTRY_MODE:
        out['mode'] = ENTRY_MODE[state]
    if state == 3:
        out['wait'] = world.word() % 150 + 200
    elif state == 4:
        for index, (x, y, angle) in enumerate(booths):
            if ((y << 7) + x + 1) & 0xFFFF == guest['cell']:
                out['heading'] = angle
                out['mode'] = 3
                break
    elif state == 8:
        out['spot'] = turn
    elif state == 14:
        out['w224'] = turn
        out['w21c'] = int(f32(guest['b21b'] * f32(1.2)))
    elif state == 15:
        out['speed'] = 0
    elif state == 22:
        out['target'] = 0
    elif state == 27:
        out['firstaid'] = 100
    elif state == 30 and guest['paid']:
        out['stay'] = ((turn - guest['stamp']) & MASK) >> 2 << 2
    return out


def port_preupdate(guest: dict, turn: int, cell: tuple, in_park: bool, world: World) -> dict:
    """GuestSimulation.ThemeParkIncPreUpdate (with the weather rule a recorded call)."""
    g = dict(guest)
    g['calls'] = []
    if (guest['id'] & 3) == (turn & 3):
        g['calls'].append('weather')
        g['exit'] = (g['exit'] - 1) & MASK
        g['happiness'] = add(g['happiness'], cell[0])
        g['illness'] = add(g['illness'], cell[1])
        g['hunger'] = add(g['hunger'], cell[2])
        if turn & 0xF == 0:
            g['toilet'] = add(g['toilet'], 1.0)
            g['hunger'] = add(g['hunger'], 2.0)
            g['thirst'] = add(g['thirst'], 2.0)
        for need in ('illness', 'hunger', 'thirst'):
            if byte(g[need]) == 100:
                g['happiness'] = add(g['happiness'], -1.0)
        term = 0
        if byte(g['toilet']) > 80:
            g['happiness'] = add(g['happiness'], -1.0)
            if byte(g['toilet']) == 100:
                g['happiness'] = add(g['happiness'], -1.0)
            elif byte(g['toilet']) == 95:
                g['calls'].append(('thought', 4))
            term = 1
        g['speed'] = 0 if term and g['umbrella'] in (1, 3, 4) else (25 if term else 0)
        if g['state'] not in (0x14, 0x15) and in_park:
            if world.word() % 10 == 0:
                g['calls'].append('pick')
            if g['countdown'] > 0:
                g['countdown'] -= 1
                if g['countdown'] == 0:
                    g['calls'].append(('balloon', 0))
        g['calls'].append('object')
    effective = g['saved'] if g['state'] == 8 else g['state']
    if not 14 <= effective <= 17:
        g['q2cc'] = g['q2ce'] = 0
    if turn % 20 == 0:
        a, b = g['history'][0], g['history'][1]
        g['history'] = [0, a, b, b]
    return g


# ---- Phase P2: state 6, the spot animation, the mood, the dispatch's states 7 and 8, state 9 -----------------------------------

def heading(first: int, second: int) -> int:
    """HeadingAngle.Of (0x91E074): the table floor(atan(i / 256) * 1024 / pi) by quadrant."""
    def entry(numerator: int, denominator: int) -> int:
        return math.floor(math.atan(((numerator << 8) // denominator) / 256) * 1024 / math.pi) if (numerator << 8) // denominator < 256 else 256
    if first == 0 and second == 0:
        return 0
    if first >= 0:
        if second >= 0:
            angle = 0x400 - entry(first, second) if first < second else 0x200 + entry(second, first)
        else:
            depth = -second
            angle = entry(first, depth) if first < depth else 0x200 - entry(depth, first)
    else:
        across = -first
        if second >= 0:
            angle = 0x400 + entry(across, second) if across < second else 0x600 - entry(second, across)
        else:
            depth = -second
            angle = 0x800 - entry(across, depth) if across < depth else 0x600 + entry(depth, across)
    return angle & 0x7FF


def cell_of(word: int) -> tuple:
    return ((word - 1) & 0x7F, ((word - 1) & 0xFFFF) >> 7)


PEEP = {'small': 5, 'medium': 15, 'big': 25, 'stinkbomb': 25, 'threshold': 15, 'likelihood': 10}


def port_idle(g: dict, sc: dict, world: World) -> dict:
    """GuestSimulation.UpdateTpiIdle over the oracle's stubs: the calls the port makes at the stubbed points (a gated search is read as finding nothing) and the fields."""
    g = dict(g)
    calls: list = []
    a = world.word()
    g['w1b8'] = 0

    def loss(kind):
        g['happiness'] = add(g['happiness'], -float((PEEP['small'], PEEP['medium'], PEEP['big'])[kind] & 0xFF))

    def tail(kind):
        calls.append(('thought', 0x15))
        calls.append(('post', kind, g['id']))
        g['happiness'] = add(g['happiness'], 1.0)

    if (sc['turn'] - g['spot']) & MASK > 100 and byte(g['happiness']) > 80 and g['umbrella'] in (0, 2):
        calls.append('mood')
        calls.append(('spot', sc['mood']))
        return finish(g, calls)
    if byte(g['illness']) == 100 and a % 3 == 0:
        calls.append(('spot', 0xC))
        calls.append(('litter', g['cell'], 7))
        calls.append(('sound', 0xCC))
        g['illness'] = 0.0
        return finish(g, calls)
    if byte(g['litter']) >= 90 and (world.word() & 3) == 0:
        calls.append(('bins', 3))
        if sc['bin']:
            g['target'] = sc['bin']
            calls.append(('setter', 9))
            return finish(g, calls)
        kind = world.word() % 5 + 1
        if sc['tile'] == 0:
            calls.append(('litter', g['cell'], kind))
            g['litter'] = 0.0
    if byte(g['happiness']) == 0 or (g['exit'] if g['exit'] < 0x80000000 else g['exit'] - (1 << 32)) <= 0 or sc['closed']:
        loss(2)
        calls.append('bus')
        if not sc['bus']:
            g['mask'] = 1
            calls.append('bus')
        calls.append(('setter', 0x16))
    calls.append('show')
    if sc['show']:
        dx, dy = sc['show_cell'][0] - g['cell'][0], sc['show_cell'][1] - g['cell'][1]
        if dx + dy != 0:
            g['heading'] = heading(-dx, dy)
            return finish(g, calls)
    calls.append(('square', 6, (g['cell'][1] << 7) + g['cell'][0] + 1, 1))
    if sc['square']:
        calls.append('nearest')
        if sc['ent_state'] == 0x17:
            dx, dy = sc['ent_cell'][0] - g['cell'][0], sc['ent_cell'][1] - g['cell'][1]
            if dx + dy != 0:
                g['heading'] = heading(-dx, dy)
                g['w1b8'] = 1
                return finish(g, calls)
            g['w1b8'] = 0
    decide = True
    hundred = a % 100
    if byte(g['happiness']) < PEEP['threshold'] and g['w29c'] == 0 and (hundred < PEEP['likelihood'] or g['b2b1'] != 0):
        g['b2b1'] = 0
        s = (g['code'] - 1) & MASK
        if s == 0 and hundred < PEEP['stinkbomb']:
            calls.append(('litter', g['cell'], 8))
            calls.append(('sound', 0xCF))
            tail(1)
        elif s == 1:
            g['toilet'] = 80.0
            g['w29c'] = 1
        elif s == 2:
            calls.append(('bins', 6))
            if sc['bin6']:
                calls.append(('var', 10))
                if sc['var10'] == 0:
                    g['target'] = sc['bin6']
                    calls.append(('setter', 10))
                    return finish(g, calls)
        elif s in (6, 7):
            calls.append(('setter', 0x19 if s == 6 else 0x18))
            calls.append('stand')
            calls.append(('thought', 0x12))
            return finish(g, calls)
        elif s == 3:
            calls.append('plant')
        elif s == 4:
            calls.append(('litter', g['cell'], a % 5 + 1))
            g['code'] = 0
            calls.append(('setter', 7))
            tail(5)
    if decide:
        way = a & 3
        if way in (0, 2):
            if not sc['turn'] <= (g['w224'] + 30) & MASK:
                calls.append('major')
                g['target'] = 0
                calls.append(('spot', 4 if g['umbrella'] in (0, 2) else 3))
                loss(0)
                g['w224'] = sc['turn']
        elif way == 1:
            calls.append('wander')
            if sc['wander']:
                calls.append(('setter', 7))
            else:
                g['w224'] = sc['turn']
        else:
            calls.append('rand')
            if sc['r1'] < 0x3FFF:
                calls.append('rand')
                calls.append('guests' if sc['r2'] >= 0x3FFF else 'infodesk')
    return finish(g, calls)


def finish(g: dict, calls: list) -> dict:
    g['calls'] = calls
    return g


def idle_rounds(image: Image, rng: random.Random, cases: int) -> int:
    """State 6 (0x614A40) natively with every search, the setter, the spot animation and the bus-stop walk stubbed and recorded."""
    bad = 0
    calls: list = []
    sc: dict = {}
    state: dict = {}

    def rec(name, pop, value=0):
        def stub(sb: Sandbox):
            calls.append(name)
            return value() if callable(value) else value, pop
        return stub

    def setter(sb: Sandbox):
        calls.append(('setter', sb.arg(0)))
        return 0, 4

    def spot(sb: Sandbox):
        calls.append(('spot', sb.arg(0)))
        return 0, 4

    def mood(sb: Sandbox):
        calls.append('mood')
        return sc['mood'], 0

    def litter(sb: Sandbox):
        record = sb.reg('ecx')
        calls.append(('litter', cell_of((record - (state['world'] + 0x418)) // 72), sb.arg(0)))
        return 0, 4

    def bins(sb: Sandbox):
        calls.append(('bins', sb.arg(1)))
        sb.write(sb.arg(0), 'H', sc['bin'] if sb.arg(1) == 3 else sc['bin6'])
        return 0, 8

    def bus(sb: Sandbox):
        calls.append('bus')
        return int(sc['bus']), 0

    def show(sb: Sandbox):
        calls.append('show')
        sb.write(sb.arg(0), 'H', sc['show'])
        return 0, 4

    def square(sb: Sandbox):
        calls.append(('square', sb.arg(0), sb.arg(1) & 0xFFFF, sb.arg(2)))
        return int(sc['square']), 12

    def nearest(sb: Sandbox):
        calls.append('nearest')
        sb.write(sb.arg(0), 'H', sc['ent'])
        return sb.arg(0), 4

    def found_none(name):
        def stub(sb: Sandbox):
            calls.append(name)
            sb.write(sb.arg(0), 'H', 0)
            return sb.arg(0), 4
        return stub

    def plant(sb: Sandbox):
        calls.append('plant')
        sb.write(sb.arg(0), 'H', 0)
        return 0, 8

    def major(sb: Sandbox):
        calls.append('major')
        sb.write(sb.reg('ecx') + 0x204, 'H', 0)
        return 0, 0

    def rand(sb: Sandbox):
        calls.append('rand')
        return sc['rands'].pop(0), 0

    def post(sb: Sandbox):
        template = sb.arg(0)
        calls.append(('post', sb.read(template + 0xC, 'I'), sb.read(template + 8, 'H')))
        return 0, 4

    stubs = {0x40A560: mood, 0x404DB3: spot, 0x4036CA: litter, 0x408F71: lambda sb: (calls.append(('sound', sb.arg(1))), (0, 8))[1], 0x40A795: bins,
             0x4080F8: lambda sb: (int(sc['closed']), 0), 0x405EC5: bus, 0x4096E2: setter, 0x40938B: show, 0x40144C: lambda sb: (0x7000000, 0), 0x402734: square,
             0x40B442: nearest, 0x4071EE: lambda sb: (calls.append(('thought', sb.arg(0))), (0, 8))[1], 0x40B76C: post, 0x4052FE: major,
             0x40BAA0: lambda sb: (calls.append('wander'), (int(sc['wander']), 0))[1], 0x870D38: rand, 0x406AE1: found_none('infodesk'), 0x403D9B: found_none('guests'),
             0x4094EE: plant, 0x40A4CA: lambda sb: (calls.append('stand'), (0, 0))[1], 0x40AE89: lambda sb: (0x7300000, 0),
             0x4078BA: lambda sb: (calls.append(('var', sb.arg(1))), (sc['var10'], 0))[1]}
    idle = Sandbox(image, [(0x614A40, 0x615412), (0x6493E0, 0x649415), (0x86FAB0, 0x86FAD7), (0x6375C0, 0x6375CD), (0x614460, 0x6144E3), (0x91E074, 0x91E1B4)]
                   + thunks(0x40A92F, 0x4080E9, 0x406C26), stubs=stubs, budget=20000)
    meters = [0.0, 10.0, 14.0, 15.0, 50.0, 80.0, 81.0, 89.5, 90.0, 95.0, 100.0]
    covered: dict = {}
    for index in range(cases * 25):
        idle.reset()
        idle.set_fpcw(CONTROL_WORD)
        calls.clear()
        thing_id = rng.randrange(1, 0x4000)
        x, y = rng.randrange(2, 126), rng.randrange(2, 126)
        turn = rng.randrange(0, 1 << 20)
        near = lambda: (min(127, max(0, x + rng.randrange(-2, 3))), min(127, max(0, y + rng.randrange(-2, 3))))
        sc.clear()
        sc.update(turn=turn, mood=rng.choice([5, 6, 7, 8, 9]), closed=rng.random() < 0.1, bus=rng.random() < 0.7, bin=rng.choice([0, 0, rng.randrange(1, 0x4000)]),
                  bin6=rng.choice([0, rng.randrange(1, 0x4000)]), var10=rng.choice([0, 0, 1]),
                  tile=rng.choice([0, 0, 0, 3]), show=rng.choice([0, 0, 0, rng.randrange(0x4000, 0x5000)]), show_cell=near(), square=rng.random() < 0.3,
                  ent=rng.randrange(0x5000, 0x6000), ent_cell=near(), ent_state=rng.choice([0, 1, 0x17, 0x17]), wander=rng.random() < 0.6,
                  r1=rng.choice([0, 0x3FFE, 0x3FFF, rng.randrange(0, 0x8000)]), r2=rng.choice([0, 0x3FFE, 0x3FFF, rng.randrange(0, 0x8000)]))
        sc['rands'] = [sc['r1'], sc['r2']]
        g = {'id': thing_id, 'cell': (x, y), 'happiness': f32(rng.choice([5.0, 10.0, 14.0]) if rng.random() < 0.4 else rng.choice(meters + [rng.uniform(0, 100)])), 'illness': f32(rng.choice(meters)),
             'litter': f32(rng.choice(meters)), 'toilet': f32(rng.uniform(0, 100)), 'exit': rng.choice([0, 1, 400, (-3) & MASK]), 'umbrella': rng.randrange(0, 5),
             'spot': rng.choice([turn, (turn - 101) & MASK, (turn - 100) & MASK, rng.randrange(0, 1 << 32)]), 'w224': rng.choice([turn, (turn - 30) & MASK, (turn - 31) & MASK, 0]),
             'w29c': rng.choice([0, 0, 1]), 'b2b1': rng.choice([0, 1]), 'code': rng.choice([0, 1, 2, 3, 4, 5, 6, 7, 8]), 'target': rng.randrange(0, 0x4000),
             'heading': rng.randrange(0, 0x800), 'mask': 0}
        seed = rng.randrange(0, 1 << 32)
        world = idle.object(0x1EAC00, {TURN: ('I', turn), SEED: ('I', seed)})
        state['world'] = world
        idle.write(WORLD_POINTER, 'I', world)
        idle.write(CELLS_POINTER, 'I', world + 0x460)
        word = (y << 7) + x + 1
        idle.write(world + 0x418 + 72 * word + 0x30, 'I', sc['tile'])
        for address, value in ((0xA01A78, PEEP['small']), (0xA01A7C, PEEP['medium']), (0xA01A80, PEEP['big']), (0xA01B00, PEEP['stinkbomb']), (0xA01B04, PEEP['threshold']),
                               (0xA01B08, PEEP['likelihood'])):
            idle.write(address, 'I', value)
        idle.write(0xA09728, 'I', 0x7100000)
        idle.write(0xA01B0C, 'I', 180)
        if sc['bin6']:
            idle.write(0xAA18B0 + 20 * sc['bin6'], 'I', idle.object(0x300, {0x2C: ('I', 1)}))
        for other, (cx, cy), field in ((sc['show'], sc['show_cell'], 0), (sc['ent'], sc['ent_cell'], sc['ent_state'])):
            if other:
                record = idle.object(0x300, {0x5: ('B', cx), 0x7: ('B', cy), 0x1B4: ('I', field)})
                idle.write(0xAA18B0 + 20 * other, 'I', record)
        thing = idle.object(0x300, {0x0: ('H', thing_id), 0x2: ('B', 1), 0x5: ('B', x), 0x7: ('B', y), 0x2C: ('I', g['heading']), 0x1A0: ('I', 0), 0x1B8: ('I', 7),
                                   0x1BC: ('f', g['happiness']), 0x1D0: ('f', g['toilet']), 0x1D4: ('f', g['illness']), 0x1D8: ('f', g['litter']), 0x1E0: ('I', g['exit']),
                                   0x1E4: ('I', g['code']), 0x204: ('H', g['target']), 0x224: ('I', g['w224']), 0x230: ('I', g['spot']), 0x238: ('I', g['umbrella']),
                                   0x29C: ('I', g['w29c']), 0x2B1: ('B', g['b2b1']), 0x2C0: ('I', 6)})
        idle.call(0x614A40, this=thing)
        native = {'calls': list(calls), 'happiness': idle.read(thing + 0x1BC, 'f'), 'illness': idle.read(thing + 0x1D4, 'f'), 'litter': idle.read(thing + 0x1D8, 'f'),
                  'toilet': idle.read(thing + 0x1D0, 'f'), 'w1b8': idle.read(thing + 0x1B8, 'I'), 'heading': idle.read(thing + 0x2C, 'I'), 'w29c': idle.read(thing + 0x29C, 'I'),
                  'b2b1': idle.read(thing + 0x2B1, 'B'), 'code': idle.read(thing + 0x1E4, 'I'), 'target': idle.read(thing + 0x204, 'H'), 'w224': idle.read(thing + 0x224, 'I'),
                  'mask': idle.read(thing + 0x1A0, 'I'), 'seed': idle.read(world + SEED, 'I')}
        for call in calls:
            key = {('setter', 10): 'bin prank', ('setter', 0x18): 'whinging', ('setter', 0x19): 'rumours', ('setter', 9): 'bin walk', ('spot', 0xC): 'vomit'}.get(call) \
                if isinstance(call, tuple) else None
            if call == 'nearest' or (isinstance(call, tuple) and call[0] in ('post',)):
                key = 'entertainer' if call == 'nearest' else 'prank tail'
            if key:
                covered[key] = covered.get(key, 0) + 1
        replica = World(seed)
        port = port_idle(g, sc, replica)
        port['seed'] = replica.state
        for key, value in native.items():
            if port[key] != value:
                bad += 1
                if bad <= 5:
                    print(f'idle id {thing_id}: {key} native {value} port {port[key]}')
                break
    print(f'state 6 branches reached: {dict(sorted(covered.items()))}')
    return bad


def spot_rounds(image: Image, rng: random.Random, cases: int) -> int:
    """The spot animation 0x60FB30 and the mood 0x60CDD0 natively (the sound and the setter stubbed and recorded)."""
    bad = 0
    calls: list = []
    spot = Sandbox(image, [(0x60FB30, 0x60FBDC), (0x6375C0, 0x6375CD), (0x5D0F90, 0x5D0F9A)] + thunks(0x4080E9, 0x406A37),
                   stubs={0x408F71: lambda sb: (calls.append(('sound', sb.arg(1))), (0, 8))[1], 0x4096E2: lambda sb: (calls.append(('setter', sb.arg(0))), (0, 4))[1]}, budget=500)
    for index in range(cases * 5):
        for animation in range(0, 16):
            spot.reset()
            calls.clear()
            thing_id, turn, state = rng.randrange(1, 0x10000), rng.randrange(0, 1 << 32), rng.randrange(0, 31)
            world = spot.object(0x1EAC00, {TURN: ('I', turn)})
            spot.write(WORLD_POINTER, 'I', world)
            thing = spot.object(0x300, {0x0: ('H', thing_id), 0x2C0: ('I', state)})
            spot.call(0x60FB30, args=(animation,), this=thing)
            native = (list(calls), spot.read(thing + 0x20, 'I'), spot.read(thing + 0x2C4, 'I'), spot.read(thing + 0x230, 'I'))
            voice = {4: 0x7E, 10: 0xD5}.get(animation, 0x4D if 5 <= animation <= 9 else None)
            port_calls = ([('sound', voice)] if voice is not None and thing_id % 3 == 0 else []) + [('setter', 8)]
            if native != (port_calls, animation, state, turn):
                bad += 1
                if bad <= 5:
                    print(f'spot {animation} id {thing_id}: native {native} port {(port_calls, animation, state, turn)}')
    mood = Sandbox(image, [(0x60CDD0, 0x60CE38), (0x68E7B0, 0x68E7B7)] + thunks(0x40BEA1), budget=200)
    for index in range(cases * 5):
        for generation in range(-1, 4):
            for female in (0, 1, 2):
                mood.reset()
                category, variant, start = rng.randrange(0, 20), rng.randrange(0, 4), rng.randrange(0, 0x100)
                bank = mood.object(0x300, {0x206: ('B', female)})
                mood.write(0x9D7004 + 12 * category, 'I', start)
                mood.write(0xB88AEC + 4 * (start + variant), 'I', bank)
                thing = mood.object(0x300, {0x30: ('I', variant), 0x34: ('I', category), 0x2A4: ('i', generation)})
                native = mood.call(0x60CDD0, this=thing).eax
                port = {0: 5, 1: 6 if female else 7, 2: 8 if female else 9}.get(generation, 0)
                if native != port:
                    bad += 1
                    if bad <= 5:
                        print(f'mood generation {generation} female {female}: native {native} port {port}')
    return bad


def dispatch_rounds(image: Image, rng: random.Random, cases: int) -> int:
    """The dispatch 0x619FF0 for states 7 (0x61A179) and 8 (0x61A382): the walk, the wander, the restore and the setter stubbed and recorded, the world generator native."""
    bad = 0
    calls: list = []
    sc: dict = {}
    dispatch = Sandbox(image, [(0x619FF0, 0x61A3A8), (0x6493E0, 0x649415)] + thunks(0x40A92F),
                       stubs={0x40989A: lambda sb: (sc['walk'], 0), 0x40BAA0: lambda sb: (calls.append('wander'), (int(sc['wander']), 0))[1],
                              0x404908: lambda sb: (calls.append('restore'), (0, 0))[1], 0x4096E2: lambda sb: (calls.append(('setter', sb.arg(0))), (0, 4))[1]}, budget=500)
    for index in range(cases * 25):
        dispatch.reset()
        calls.clear()
        state = rng.choice([7, 8])
        turn, stamp = rng.randrange(0, 1 << 32), rng.randrange(0, 1 << 32)
        if rng.random() < 0.5:
            stamp = (turn - rng.randrange(0, 20)) & MASK
        sc.update(walk=rng.choice([0, 1, 2, 3]), wander=rng.random() < 0.6)
        seed = rng.randrange(0, 1 << 32)
        world = dispatch.object(0x1EAC00, {TURN: ('I', turn), SEED: ('I', seed)})
        dispatch.write(WORLD_POINTER, 'I', world)
        thing = dispatch.object(0x300, {0x230: ('I', stamp), 0x2C0: ('I', state)})
        dispatch.call(0x619FF0, this=thing)
        replica = World(seed)
        port: list = []
        if state == 8:
            if turn > (stamp + 10) & MASK:
                port.append('restore')
        elif sc['walk'] in (0, 2):
            if replica.word() & 3:
                port.append(('setter', 6))
            else:
                port.append('wander')
                port.append(('setter', 7 if sc['wander'] else 6))
        if (list(calls), dispatch.read(world + SEED, 'I')) != (port, replica.state):
            bad += 1
            if bad <= 5:
                print(f'dispatch {state}: native {calls} port {port}')
    return bad


def bin_rounds(image: Image, rng: random.Random, cases: int) -> int:
    """State 9 (0x616F10): the walk, the bin's customer, its script variables and dirt test, the rows and the drop stubbed and recorded; the rows of a dirty bin are the port's gate."""
    bad = 0
    calls: list = []
    sc: dict = {}
    state: dict = {}

    def variable(sb: Sandbox):
        index = sb.arg(1)
        calls.append(('var', index))
        return sc['vars'].get(index, 0), 0

    def litter(sb: Sandbox):
        calls.append(('litter', cell_of((sb.reg('ecx') - state['cells']) // 72 + 1), sb.arg(0)))
        return 0, 4

    stubs = {0x40989A: lambda sb: (sc['walk'], 0), 0x40B947: lambda sb: (calls.append('customer'), (0, 0))[1], 0x40AE89: lambda sb: (0x7200000, 0), 0x4078BA: variable,
             0x4028EC: lambda sb: (calls.append(('set', sb.arg(1), sb.arg(2))), (0, 0))[1], 0x404F52: lambda sb: (calls.append('dirt'), (int(sc['dirty']), 0))[1],
             0x40BDB6: lambda sb: (calls.append(('row off', sb.arg(0))), (0, 8))[1], 0x40CAC7: lambda sb: (calls.append(('row on', sb.arg(0))), (0, 8))[1], 0x4036CA: litter,
             0x4096E2: lambda sb: (calls.append(('setter', sb.arg(0))), (0, 4))[1]}
    walk = Sandbox(image, [(0x616F10, 0x6170C9), (0x6493E0, 0x649415)] + thunks(0x40A92F), stubs=stubs, budget=1000)
    for index in range(cases * 25):
        walk.reset()
        calls.clear()
        x, y = rng.randrange(0, 128), rng.randrange(0, 128)
        capacity = rng.choice([0, 20, rng.randrange(0, 40)])
        sc.update(walk=rng.choice([0, 0, 1, 2, 3]), dirty=rng.random() < 0.4, vars={3: capacity, 4: rng.choice([0, capacity - 1, capacity, capacity + 1]), 5: rng.choice([0, 0, 1])})
        target = rng.choice([0, rng.randrange(1, 0x4000)])
        seed = rng.randrange(0, 1 << 32)
        world = walk.object(0x1EAC00, {SEED: ('I', seed)})
        walk.write(WORLD_POINTER, 'I', world)
        state['cells'] = world + 0x460
        walk.write(CELLS_POINTER, 'I', state['cells'])
        if target:
            walk.write(0xAA18B0 + 20 * target, 'I', walk.object(0x300, {0x2C: ('I', 1), 0x5: ('B', 3), 0x7: ('B', 4)}))
        thing = walk.object(0x300, {0x5: ('B', x), 0x7: ('B', y), 0x1D8: ('f', 95.0), 0x204: ('H', target)})
        walk.call(0x616F10, this=thing)
        native = [call for call in calls if call != 'dirt' and not (isinstance(call, tuple) and call[0] in ('row off', 'row on'))]
        replica = World(seed)
        port: list = []
        if sc['walk'] in (0, 2):
            used = False
            if sc['walk'] == 0 and target:
                port += ['customer', ('var', 3), ('var', 4), ('var', 5)]
                if sc['vars'][4] < sc['vars'][3] and sc['vars'][5] == 0:
                    port.append(('set', 0, 1))
                    used = True
            if not used:
                port.append(('litter', (x, y), replica.word() % 5 + 1))
            port.append(('setter', 6))
        litter_after = walk.read(thing + 0x1D8, 'f')
        if (native, walk.read(world + SEED, 'I'), litter_after) != (port, replica.state, 0.0 if sc['walk'] in (0, 2) else 95.0):
            bad += 1
            if bad <= 5:
                print(f'state 9 walk {sc["walk"]} target {target} vars {sc["vars"]}: native {native} port {port}')
    return bad


# ---- Phase P3a: the attraction score, the queue functions, states 13, 14 and 15 ---------------------------------------------------

from fractions import Fraction  # noqa: E402


def round24(value: Fraction) -> float:
    """An exact value rounded once to a single (24-bit significand, nearest even), as the x87 at control word 0x7F."""
    if value == 0:
        return 0.0
    sign = -1 if value < 0 else 1
    value = abs(value)
    exponent = value.numerator.bit_length() - value.denominator.bit_length()
    if Fraction(2) ** exponent > value:
        exponent -= 1
    scaled = value / Fraction(2) ** (exponent - 23)
    whole = scaled.numerator // scaled.denominator
    rest = scaled - whole
    if rest > Fraction(1, 2) or (rest == Fraction(1, 2) and whole & 1):
        whole += 1
    return sign * float(Fraction(whole) * Fraction(2) ** (exponent - 23))


TWO_D = Fraction(struct.unpack('<d', struct.pack('<d', 0.1))[0])
NEED_TABLE = None
RELIEF_TABLE = None


def s32(value: int) -> int:
    value &= MASK
    return value - (1 << 32) if value >> 31 else value


def div0(a: int, b: int) -> int:
    """Signed division truncating toward zero."""
    q = abs(a) // abs(b)
    return q if (a >= 0) == (b > 0) else -q


def port_score(c: dict) -> int:
    """ThemeParkIncAttractionScore.Compute (0x6101E0) over the oracle's case."""
    if c['special'] or (c['gender'] and c['gender'] != (2 if c['female'] else 1)) or c['sametype']:
        return 0
    if c['appearance'] == 3 and (c['umbrella'] != 0 or c['weather'] not in (2, 3, 4)):
        return 0
    if c['appearance'] == 1 and (c['balloon'] or c['countdown'] > 0 or c['umbrella'] in (3, 4)):
        return 0
    if c['infodesk']:
        return 0
    d = (c['gx'] - c['bx']) ** 2 + (c['gy'] - c['by']) ** 2
    distance = 100 - min(100, d * 100 // 450)
    if c['modifier']:
        distance = (distance & MASK) // (c['modifier'] & MASK)
    if d <= 8:
        cells = c['cells'] or 1
        qm, qw = (100 - (c['count'] * 100 & MASK) // (cells * 4 & MASK)) & MASK, c['w'][1]
    else:
        qm, qw = 0, 0
    diff = min(50, abs(c['pref'] - c['rating']))
    em, ew = 100 - 2 * diff, (c['w'][2] & 0xFFFF) if c['gate'] else 0

    def need(meter, effect):
        return NEED_TABLE[11 * ((effect & 0xFF) // 10) + (int(meter) & 0xFF) // 10]

    def relief(meter):
        return RELIEF_TABLE[((int(meter) & 0xFF) + 4) // 5] if c['relief'] else 0
    w = [c['w'][0] & 0xFFFF, c['w'][3] & 0xFFFF, c['w'][4] & 0xFFFF, c['w'][5] & 0xFFFF, c['w'][6] & 0xFFFF]
    num = (w[0] * distance + ew * em + qw * qm + w[4] * relief(c['illness']) + w[3] * relief(c['toilet']) + w[2] * need(c['hunger'], c['heff'])
           + w[1] * need(c['thirst'], c['teff'])) & MASK
    den = (sum(w) + ew + qw) & MASK
    if den == 0:
        return None
    score = num // den
    if c['age'] <= c['newdays']:
        score = (score * c['newmul']) & MASK
    if s32(c['golden']) > 0:
        factor = round24(Fraction(round24(TWO_D * (s32(c['golden']) + 1))) + 1)
        score = int(round24(Fraction(factor) * s32(score))) & MASK
    elif c['upgrade']:
        q = round24(Fraction(c['upgrade']) * Fraction(f32(0.00033333333)))
        if q > 1.0:
            factor = round24(Fraction(round24(Fraction(q) * Fraction(f32(0.1)))) + 1)
            score = int(round24(Fraction(factor) * s32(score))) & MASK
    pct = {2: 'rain', 4: 'snow', 1: 'heat', 3: 'storm'}.get(c['weather'])
    if pct and c[pct]:
        score = div0(s32(c[pct] * score), 100) & MASK
    if c['appearance'] == 3 and (c['balloon'] or c['countdown'] > 0) and c['countdown']:
        factor = (100 - (c['countdown'] * 100 & MASK) // 255) & MASK
        if factor:
            if c['discard']:
                factor = (c['discard'] * factor & MASK) // 100
            score = (score * factor & MASK) // 100
    if c['generation'] in (0, 1, 2):
        score = div0(s32(c['gen'][c['generation']] * score), 100) & MASK
    value = s32(score)
    for slot, divisor in enumerate((5, 4, 3, 2)):
        if c['used'][slot] == c['id']:
            value = div0(value, divisor)
            break
    for slot, divisor in enumerate((5, 4, 3, 2)):
        if c['temp'][slot] == c['id']:
            value = div0(value, divisor)
    return value & MASK


def score_rounds(image: Image, rng: random.Random, cases: int) -> int:
    """The attraction score 0x6101E0 with the special-ride test, the type record, the bank's gender, the back cell, the info desk's target, the queue count, the rating, the park's weather and the
    age stubbed; the id, the region record and _ftol native, at control word 0x7F."""
    global NEED_TABLE, RELIEF_TABLE
    bad = 0
    sc: dict = {}

    def out_word(value_key):
        def stub(sb: Sandbox):
            sb.write(sb.arg(0), 'H', sc[value_key])
            return sb.arg(0), 4
        return stub

    score = Sandbox(image, [(0x6101E0, 0x610D4C), (0x6375C0, 0x6375CD), (0x5D7420, 0x5D7446), (0x86FAB0, 0x86FAD7)] + thunks(0x4080E9, 0x40942B),
                    stubs={0x40936D: lambda sb: (0 if sc['special'] else 1, 0), 0x4022F2: lambda sb: (sc['types'][sb.reg('ecx')], 0), 0x40BEA1: lambda sb: (int(sc['female']), 0),
                           0x405B55: out_word('back'), 0x403E45: lambda sb: (0, 0), 0x403968: out_word('link'), 0x402F1D: lambda sb: (sc['count'], 0),
                           0x403062: lambda sb: ((int(sc['gate']) if sb.arg(0) else sc['rating']), 4), 0x4019B5: lambda sb: (sc['park'], 0), 0x403BB6: lambda sb: (sc['age'], 0)},
                    budget=20000)
    NEED_TABLE = list(image.read(0x93E5E8, 121)) if hasattr(image, 'read') else None
    meters = [0.0, 9.5, 10.0, 49.9, 50.0, 80.0, 95.5, 100.0]
    for index in range(cases * 25):
        score.reset()
        score.set_fpcw(CONTROL_WORD)
        if NEED_TABLE is None:
            NEED_TABLE = [score.read(0x93E5E8 + k, 'B') for k in range(121)]
            RELIEF_TABLE = [score.read(0x93E664 + k, 'B') for k in range(26)]
        gx, gy = rng.randrange(0, 128), rng.randrange(0, 128)
        near = rng.random() < 0.5
        bx = min(127, max(0, gx + (rng.randrange(-2, 3) if near else rng.randrange(-30, 31))))
        by = min(127, max(0, gy + (rng.randrange(-2, 3) if near else rng.randrange(-30, 31))))
        c = {'special': rng.random() < 0.05, 'gender': rng.choice([0, 0, 0, 1, 2]), 'female': rng.random() < 0.5, 'sametype': False,
             'appearance': rng.choice([0, 0, 0, 1, 3, 2]), 'umbrella': rng.choice([0, 0, 0, 1, 2, 3, 4]), 'weather': rng.choice([0, 1, 2, 3, 4]), 'balloon': rng.random() < 0.2,
             'countdown': rng.choice([0, 0, 0, 1, 100, 254, 255, rng.randrange(0, 600)]), 'infodesk': rng.random() < 0.05, 'gx': gx, 'gy': gy, 'bx': bx, 'by': by,
             'modifier': rng.choice([0, 0, 0, 1, 2, 3, -1]), 'cells': rng.choice([0, 1, 2, 5]), 'count': rng.choice([0, 1, 3, 8, 20, 40]), 'rating': rng.randrange(0, 256),
             'gate': rng.random() < 0.8, 'pref': rng.randrange(0, 256), 'thirst': f32(rng.choice(meters + [rng.uniform(0, 100)])), 'hunger': f32(rng.choice(meters + [rng.uniform(0, 100)])),
             'toilet': f32(rng.choice(meters + [rng.uniform(0, 100)])), 'illness': f32(rng.choice(meters + [rng.uniform(0, 100)])), 'teff': rng.choice([0, 10, 35, 100, rng.randrange(0, 101)]),
             'heff': rng.choice([0, 10, 35, 100, rng.randrange(0, 101)]), 'relief': rng.random() < 0.3, 'age': rng.choice([0, 3, 7, 8, rng.randrange(0, 400)]),
             'golden': rng.choice([0, 0, 0, 1, 5, 9, rng.randrange(-3, 40)]) & MASK, 'upgrade': rng.choice([0, 0, 1000, 3000, 3001, 6000, rng.randrange(0, 50000)]),
             'rain': rng.choice([0, 50, 150]), 'snow': rng.choice([0, 25, 200]), 'heat': rng.choice([0, 75, 120]), 'storm': rng.choice([0, 60, 130]),
             'generation': rng.choice([0, 1, 2, 3]), 'gen': [rng.choice([0, 75, 100, 125]) for _ in range(3)], 'discard': rng.choice([0, 100, 50]),
             'w': [rng.choice([1, 2, 0x10001]) for _ in range(7)], 'newdays': rng.choice([7, 0]), 'newmul': rng.choice([5, 1]), 'id': rng.randrange(1, 0x4000)}
        c['used'] = [rng.choice([0, c['id'], rng.randrange(1, 0x4000)]) for _ in range(4)]
        c['temp'] = [rng.choice([0, c['id'], rng.randrange(1, 0x4000)]) for _ in range(4)]
        if c['used'][0] and c['used'][0] != c['id'] and rng.random() < 0.15:
            c['sametype'] = True
        type_word = 0x31
        ride_type = score.object(0x300, {0x78: ('I', c['gender']), 0x174: ('I', c['golden']), 0x200: ('I', c['teff']), 0x204: ('I', c['heff']), 0x21C: ('I', c['appearance']),
                                         0x244: ('I', int(c['infodesk'])), 0x2AC: ('I', c['upgrade']), 0xF0: ('i', c['snow']), 0xF4: ('i', c['rain']), 0xF8: ('i', c['storm']),
                                         0xFC: ('i', c['heat']), 0x110: ('i', c['gen'][0]), 0x114: ('i', c['gen'][1]), 0x118: ('i', c['gen'][2])})
        ride = score.object(0x200, {0x0: ('H', c['id']), 0x16: ('H', type_word), 0x42: ('H', 1 if c['relief'] else 0), 0x50: ('I', c['cells'])})
        sc.clear()
        sc.update(c, types={ride: ride_type}, back=((by << 7) + bx + 1) & 0xFFFF, link=0, park=score.object(0x100, {0x70: ('I', c['weather'])}))
        if c['used'][0]:
            other = score.object(0x200, {0x0: ('H', c['used'][0]), 0x16: ('H', type_word if c['sametype'] else 0x32)})
            score.write(0xAA18B0 + 20 * c['used'][0], 'I', other)
        cells = score.object(REGION + 10 * 0x4000 + 0x100)
        score.write(CELLS_POINTER, 'I', cells)
        score.write(cells + REGION + 10 * (by * 128 + bx) + 8, 'h', c['modifier'])
        for k, value in enumerate(c['w']):
            score.write(0xA01AC4 + 4 * k, 'I', value)
        score.write(0xA01AA4, 'I', c['newdays'])
        score.write(0xA01AA8, 'I', c['newmul'])
        score.write(0xA01B68, 'I', c['discard'])
        guest = score.object(0x300, {0x5: ('B', gx), 0x7: ('B', gy), 0x1C8: ('f', c['thirst']), 0x1CC: ('f', c['hunger']), 0x1D0: ('f', c['toilet']), 0x1D4: ('f', c['illness']),
                                     0x238: ('I', c['umbrella']), 0x294: ('I', int(c['balloon'])), 0x298: ('I', c['countdown']), 0x2A4: ('I', c['generation']), 0x2A8: ('B', c['pref'])})
        for k in range(4):
            score.write(guest + 0x20A + 2 * k, 'H', c['used'][k])
            score.write(guest + 0x212 + 2 * k, 'H', c['temp'][k])
        try:
            native = score.call(0x6101E0, args=(ride,), this=guest).eax & MASK
        except Exception as error:  # a weight sum of 0 faults (divl)
            native = None if 'weights' not in str(error) else None
        port = port_score(c)
        if native != port:
            bad += 1
            if bad <= 8:
                print(f'score {c}: native {native} port {port}')
    return bad


def queue_rounds(image: Image, rng: random.Random, cases: int) -> int:
    """The queue place 0x5E0280, the room 0x5DFDD0 and the length test 0x5DFF10 with its limit 0x5DFE00 over random lists of guests in random states: the predicate 0x61AC40, the id 0x6375C0, the
    type record lookup 0x43C700 (stubbed) and _ftol native, at control word 0x7F; against GuestSimulation.TpiQueuePosition, TpiQueueHasRoom, TpiQueueTooLong and ThemeParkIncQueueLimit."""
    bad = 0
    sc: dict = {}
    queue = Sandbox(image, [(0x5E0280, 0x5E033F), (0x5DFDD0, 0x5DFDEE), (0x5DFF10, 0x5DFF32), (0x5DFE00, 0x5DFECE), (0x61AC40, 0x61AC64), (0x6375C0, 0x6375CD), (0x86FAB0, 0x86FAD7)]
                    + thunks(0x405934, 0x404B1A, 0x4080E9, 0x405588),
                    stubs={0x403E45: lambda sb: (0, 0), 0x40C1D0: lambda sb: (sc['type'], 4)}, budget=20000)
    for index in range(cases * 25):
        queue.reset()
        queue.set_fpcw(CONTROL_WORD)
        world = queue.object(0x1EAC00, {TURN: ('I', rng.randrange(0, 1 << 32))})
        queue.write(WORLD_POINTER, 'I', world)
        ids = rng.sample(range(1, 0x400), rng.randrange(0, 7))
        states = [rng.choice([14, 14, 15, 16, 17, 8, 6, 13]) for _ in ids]
        saved = [rng.choice([14, 6, 17, 18]) for _ in ids]
        for k, thing_id in enumerate(ids):
            thing = queue.object(0x300, {0x0: ('H', thing_id), 0x2C0: ('I', states[k]), 0x2C4: ('I', saved[k]), 0x2CC: ('H', ids[k + 1] if k + 1 < len(ids) else 0)})
            queue.write(0xAA18B0 + 20 * thing_id, 'I', thing)
        cells = rng.choice([0, 1, 2, 3])
        has_queue = rng.random() < 0.8
        speed, init = rng.choice([0, 30, 60, 90]), rng.choice([60, 45, 7])
        qwtc, capacity, duration = f32(rng.choice([130.0, 1.0, 37.5, rng.uniform(0, 300)])), rng.randrange(0, 256), rng.choice([0, 1, 3, 30, rng.randrange(0, 256)])
        sc['type'] = queue.object(0x400, {0x29C: ('I', init), 0x2A8: ('f', qwtc)})
        ride = queue.object(0x200, {0x42: ('H', 8 if has_queue else 0), 0x4C: ('H', ids[0] if ids else 0), 0x50: ('I', cells), 0x64: ('B', 0), 0x6C: ('I', speed),
                                    0x70: ('B', duration), 0x71: ('B', capacity)})

        def in_queue(k):
            effective = saved[k] if states[k] == 8 else states[k]
            return 14 <= effective <= 17

        def position(pid):
            for k, thing_id in enumerate(ids):
                if thing_id == pid:
                    return k
                if not in_queue(k):
                    return k + 1 if pid == 0 else -1
            return len(ids) if pid == 0 else -1
        probe = rng.choice(ids + [0, 0x3FF]) if ids else rng.choice([0, 0x3FF])
        native = queue.call(0x5E0280, args=(probe,), this=ride).eax
        native = native - (1 << 32) if native >> 31 else native
        if native != position(probe):
            bad += 1
            if bad <= 5:
                print(f'0x5E0280 ids {ids} states {states} saved {saved} probe {probe}: native {native} port {position(probe)}')
        room = queue.call(0x5DFDD0, this=ride).eax & 0xFF
        if bool(room) != ((position(0) & MASK) < 4 * cells):
            bad += 1
        if not has_queue:
            limit = 100
        else:
            g = 1.0 if speed == 0 else f32(speed / init)
            value = f32(f32(qwtc * capacity) * g)
            # fidivl by a duration of 0: x87 gives infinity, or NaN for 0/0 (masked invalid); a NaN fails fcoms against 4.0 (C0 and C3 set) and becomes 4.0, as C#'s !(value > 4f) does
            value = f32(value / duration) if duration else (math.nan if value == 0 else math.inf)
            limit = (int(value) & MASK) if value > 4.0 and value != math.inf else (4 if not value > 4.0 else 0)
        native_long = queue.call(0x5DFF10, this=ride).eax & 0xFF
        if bool(native_long) != ((position(0) & MASK) >= limit):
            bad += 1
            if bad <= 5:
                print(f'0x5DFF10 length {position(0)} limit {limit} (qwtc {qwtc} cap {capacity} g {speed}/{init} dur {duration}): native {native_long}')
    return bad


def port_state14(g: dict, sc: dict, world: World, rand: list) -> list:
    """GuestSimulation.UpdateTpiStandingInQueue over the oracle's stubs: the calls the port makes at the stubbed points."""
    calls: list = []

    def leave(flag=0):
        calls.extend([('remove', g['id']), ('dismiss', flag), 'walkaway', ('setter', 6)])
    if g['target'] == 0:
        return [('setter', 6)]
    if g['b21b'] == 0 and g['w220'] != 0:
        if sc['loading'] == g['id']:
            calls.append('stand')
            calls.append(('setdest', sc['setdest']))
            if sc['setdest']:
                return calls + [('setter', 16)]
            calls.append(('let go', g['id']))
        leave()
        return calls
    if sc['dirty']:
        calls.append(('thought', 14))
        leave()
        return calls
    if sc['appearance'] == 3 and sc['weather'] not in (2, 3, 4):
        if sc['weather'] == 1:
            leave(1)
            return calls
        if g['b28c'] == 0:
            calls.append('rand')
            g['w290'] = (rand[0] % sc['slowest'] + (g['turn'] >> 2)) & MASK
            g['b28c'] = 1
        elif (g['turn'] >> 2) >= g['w290']:
            leave(1)
            return calls
    p = sc['position']
    calls.append(('position', g['id']))
    if p == -1:
        leave()
        return calls
    if p != g['b21b']:
        if g['w21c'] != 0 and ((g['b21b'] - p) & MASK) <= 2:
            g['w21c'] -= 1
        elif not sc['broken']:
            calls.append('find')
            if not sc['find']:
                leave()
                return calls
    else:
        if g['b21b'] > sc['limit']:
            calls.append(('thought', 16))
            leave()
            return calls
        if sc['coaster'] and not sc['accepts'] or sc['track1'] and sc['word34'] == 0:
            calls.append(('thought', 13))
            leave()
            return calls
    if ((g['turn'] - g['spot']) & MASK) > 30:
        h = byte(g['happiness'])
        if h > 80:
            if g['umbrella'] in (0, 2):
                calls += ['mood', ('spot', 'mood')]
        elif h < 10:
            calls.append(('thought', 11))
            leave()
        elif h < 20:
            if g['umbrella'] in (0, 2):
                calls.append(('spot', 4))
        elif byte(g['toilet']) > 80:
            calls.append(('thought', 4))
            if not sc['relief']:
                leave()
        return calls
    if g['turn'] > (g['w224'] + 100) & MASK:
        calls.append(('thought', 12))
        leave()
        return calls
    if world.word() % 10 != 0:
        return calls
    heading = g['heading'] + world.word() % 800 - 400
    g['heading'] = heading + 0x800 if heading < 0 else heading - 0x800 if heading >= 0x800 else heading
    return calls


def state14_rounds(image: Image, rng: random.Random, cases: int) -> int:
    """State 14 (0x6176C0) with every callee but the world generator, _ftol and the id stubbed and recorded (the ride's loading slot, the stand point, the route, the setter, the list functions, the
    dismissal, the walk-away, the dirt test, the type and park records, the CRT rand(), the place, the broken test, FindQueueDestination, the limit, the coaster tests, the thought, the mood and the
    spot animation); the calls, +0x21C, +0x28C, +0x290, the heading and the draws are compared with UpdateTpiStandingInQueue."""
    bad = 0
    calls: list = []
    sc: dict = {}

    def rec(name, pop, value=None, arg=False):
        def stub(sb: Sandbox):
            calls.append((name, sb.arg(0)) if arg else name)
            return (sc[value] if isinstance(value, str) else (value or 0)), pop
        return stub

    def stand(sb: Sandbox):
        calls.append('stand')
        sb.write(sb.arg(1), 'I', 0x1234)
        sb.write(sb.arg2 if False else sb.arg(2), 'I', 0x5678)
        return 0, 12

    def setdest(sb: Sandbox):
        calls.append(('setdest', sc['setdest']))
        return sc['setdest'], 8

    stubs = {0x40C2B6: lambda sb: (int(sc['loading'] == sb.arg(0)), 4), 0x405510: stand, 0x403DAF: setdest, 0x4096E2: rec('setter', 4, arg=True),
             0x40B4BF: lambda sb: (calls.append(('let go', sb.arg(0) & 0xFFFF)), (0, 4))[1], 0x40853A: lambda sb: (calls.append(('remove', sb.arg(0) & 0xFFFF)), (0, 4))[1],
             0x409C05: lambda sb: (calls.append(('dismiss', sb.arg(0))), (0, 4))[1], 0x405E84: rec('walkaway', 0), 0x404F52: lambda sb: (int(sc['dirty']), 0),
             0x4022F2: lambda sb: (sc['typeobj'], 0), 0x4019B5: lambda sb: (sc['park'], 0), 0x870D38: lambda sb: (calls.append('rand'), (sc['rand'], 0))[1],
             0x405934: lambda sb: (calls.append(('position', sb.arg(0) & 0xFFFF)), (sc['position'] & MASK, 4))[1], 0x403332: lambda sb: (int(sc['broken']), 0),
             0x405556: lambda sb: (calls.append('find'), (int(sc['find']), 0))[1], 0x405588: lambda sb: (sc['limit'], 0), 0x4064A6: lambda sb: (int(sc['coaster']), 0),
             0x405EE3: lambda sb: (int(sc['accepts']), 0), 0x4071EE: lambda sb: (calls.append(('thought', sb.arg(0))), (0, 8))[1], 0x40A560: lambda sb: (calls.append('mood'), (77, 0))[1],
             0x404DB3: lambda sb: (calls.append(('spot', 'mood' if sb.arg(0) == 77 else sb.arg(0))), (0, 4))[1], 0x403E45: lambda sb: (0, 0)}
    state = Sandbox(image, [(0x6176C0, 0x617D4A), (0x6493E0, 0x649415), (0x6375C0, 0x6375CD), (0x86FAB0, 0x86FAD7)] + thunks(0x40A92F, 0x4080E9), stubs=stubs, budget=5000)
    for index in range(cases * 25):
        state.reset()
        state.set_fpcw(CONTROL_WORD)
        calls.clear()
        thing_id = rng.randrange(1, 0x4000)
        turn = rng.randrange(0, 1 << 32)
        g = {'id': thing_id, 'target': rng.choice([0, 40, 40, 40]), 'b21b': rng.choice([0, 0, 1, 2, 5]), 'w220': rng.choice([0, 0, 1]), 'b28c': rng.choice([0, 0, 1]),
             'w290': rng.choice([0, (turn >> 2) + rng.randrange(-3, 4)]) & MASK, 'w21c': rng.choice([0, 1, 3]), 'turn': turn,
             'spot': (turn - rng.choice([0, 10, 30, 31, 200])) & MASK, 'w224': (turn - rng.choice([0, 50, 100, 101, 500])) & MASK, 'happiness': f32(rng.choice([5.0, 9.9, 10.0, 15.0, 19.9, 20.0, 50.0, 80.0, 80.5, 81.0, 95.0])),
             'toilet': f32(rng.choice([10.0, 80.0, 81.0, 99.0])), 'umbrella': rng.choice([0, 0, 2, 3, 4]), 'heading': rng.randrange(0, 0x800)}
        sc.update(loading=rng.choice([0, thing_id, thing_id, 99]), setdest=rng.choice([0, 1, 1]), dirty=rng.random() < 0.1, appearance=rng.choice([0, 0, 0, 3]),
                  weather=rng.choice([0, 1, 2, 3, 4]), slowest=rng.choice([3, 1, 7]), rand=rng.randrange(0, 0x8000), position=rng.choice([-1, 0, 0, 1, 2, 3, 4]),
                  broken=rng.random() < 0.2, find=rng.random() < 0.7, limit=rng.choice([0, 1, 4, 100]), coaster=rng.random() < 0.1, accepts=rng.random() < 0.5,
                  track1=rng.random() < 0.1, word34=rng.choice([0, 1]), relief=rng.random() < 0.3)
        seed = rng.randrange(0, 1 << 32)
        world = state.object(0x1EAC00, {TURN: ('I', turn), SEED: ('I', seed)})
        state.write(WORLD_POINTER, 'I', world)
        state.write(0xAA1484, 'I', world)
        sc['park'] = state.object(0x100, {0x70: ('I', sc['weather'])})
        sc['typeobj'] = state.object(0x300, {0x21C: ('I', sc['appearance']), 0x14C: ('I', 1 if sc['track1'] else 0)})
        state.write(0xA01B64, 'I', sc['slowest'])
        history = state.object(0x600)
        ride = state.object(0x200, {0x34: ('I', sc['word34']), 0x42: ('H', 1 if sc['relief'] else 0), 0x130: ('I', history)})
        state.write(0xAA18B0 + 20 * 40, 'I', ride)
        thing = state.object(0x300, {0x0: ('H', thing_id), 0x2C: ('I', g['heading']), 0x1BC: ('f', g['happiness']), 0x1D0: ('f', g['toilet']), 0x204: ('H', g['target']),
                                     0x21B: ('B', g['b21b']), 0x21C: ('I', g['w21c']), 0x220: ('I', g['w220']), 0x224: ('I', g['w224']), 0x230: ('I', g['spot']),
                                     0x238: ('I', g['umbrella']), 0x28C: ('B', g['b28c']), 0x290: ('I', g['w290'])})
        state.call(0x6176C0, this=thing)
        native = {'calls': list(calls), 'w21c': state.read(thing + 0x21C, 'I'), 'b28c': state.read(thing + 0x28C, 'B'), 'w290': state.read(thing + 0x290, 'I'),
                  'heading': state.read(thing + 0x2C, 'I'), 'seed': state.read(world + SEED, 'I')}
        replica = World(seed)
        port_g = dict(g)
        port_calls = port_state14(port_g, sc, replica, [sc['rand']])
        port = {'calls': port_calls, 'w21c': port_g['w21c'], 'b28c': port_g['b28c'], 'w290': port_g['w290'], 'heading': port_g['heading'], 'seed': replica.state}
        if native != port:
            bad += 1
            if bad <= 5:
                print(f'state 14 {g} {sc}: native {native} port {port}')
    return bad


def port_state13(g: dict, sc: dict) -> list:
    """GuestSimulation.UpdateTpiWalkingToMajor over the oracle's stubs."""
    calls: list = []
    if not sc['slot']:
        return [('setter', 6)]
    calls.append('walk')
    walk = sc['walk']
    if not sc['open'] or walk == 2:
        return calls + [('loss', 2), ('setter', 6)]
    if walk == 0:
        if g['target'] == 0:
            return calls + [('setter', 6)]
        calls.append('back')
        if sc['back'] != g['cell']:
            calls.append('back')
            if sc['back'] == 0:
                return calls + [('setter', 6)]
            calls.append(('route', sc['back']))
            return calls if sc['route'] else calls + [('setter', 6)]
        calls.append('room')
        if not sc['room']:
            return calls + [('setter', 6)]
        calls += ['difference', ('gate', 1)]
        if sc['gate'] and abs(sc['difference']) >= 45:
            return calls + [('thought', 12 if sc['difference'] < 0 else 15), ('history', g['target']), ('setter', 6)]
        calls.append('long')
        if sc['long']:
            return calls + [('history', g['target']), ('thought', 16), ('setter', 6), 'walkaway']
        calls += [('add', g['id']), 'find']
        if sc['find']:
            return calls
        return calls + [('remove', g['id']), ('dismiss', 0), ('setter', 6)]
    if walk != 1:
        return calls
    calls.append('closed')
    if sc['closed']:
        return calls + [('loss', 2), ('setter', 6)]
    if g['target'] != 0:
        if sc['appearance'] == 1 and g['umbrella'] in (3, 4) or sc['appearance'] == 3 and sc['weather'] not in (2, 3, 4):
            return calls + [('setter', 6)]
    if g['count'] + 1 > 11:
        calls.append('minor')
    return calls


def state13_rounds(image: Image, rng: random.Random, cases: int) -> int:
    """State 13 (0x616AC0) with the walk, the back cell, the room, the difference and its gate, the thought, the history word, the setter, the length test, the list functions, FindQueueDestination,
    the dismissal, the walk-away, the route, the happiness loss, the closed park, the type and park records and the minor decision stubbed and recorded; against UpdateTpiWalkingToMajor."""
    bad = 0
    calls: list = []
    sc: dict = {}

    def back(sb: Sandbox):
        calls.append('back')
        sb.write(sb.arg(0), 'H', sc['back'])
        return sb.arg(0), 4

    stubs = {0x40989A: lambda sb: (calls.append('walk'), (sc['walk'], 0))[1], 0x405B55: back, 0x406AA5: lambda sb: (calls.append('room'), (int(sc['room']), 0))[1],
             0x40CAC2: lambda sb: (calls.append('difference'), (sc['difference'] & MASK, 4))[1], 0x403062: lambda sb: (calls.append(('gate', sb.arg(0))), (int(sc['gate']), 4))[1],
             0x4071EE: lambda sb: (calls.append(('thought', sb.arg(0))), (0, 8))[1], 0x404F6B: lambda sb: (calls.append(('history', sb.arg(0) & 0xFFFF)), (0, 4))[1],
             0x4096E2: lambda sb: (calls.append(('setter', sb.arg(0))), (0, 4))[1], 0x40BCDA: lambda sb: (calls.append('long'), (int(sc['long']), 0))[1],
             0x405E84: lambda sb: (calls.append('walkaway'), (0, 0))[1], 0x40CE78: lambda sb: (calls.append(('add', sb.arg(0) & 0xFFFF)), (0, 4))[1],
             0x405556: lambda sb: (calls.append('find'), (int(sc['find']), 0))[1], 0x40853A: lambda sb: (calls.append(('remove', sb.arg(0) & 0xFFFF)), (0, 4))[1],
             0x409C05: lambda sb: (calls.append(('dismiss', sb.arg(0))), (0, 4))[1], 0x4039DB: lambda sb: (calls.append(('route', sb.arg(0) & 0xFFFF)), (int(sc['route']), 4))[1],
             0x406C26: lambda sb: (calls.append(('loss', sb.arg(0))), (0, 4))[1], 0x4080F8: lambda sb: (calls.append('closed'), (int(sc['closed']), 0))[1],
             0x4022F2: lambda sb: (sc['typeobj'], 0), 0x4019B5: lambda sb: (sc['park'], 0), 0x40320B: lambda sb: (calls.append('minor'), (0, 0))[1], 0x403E45: lambda sb: (0, 0)}
    state = Sandbox(image, [(0x616AC0, 0x616E27), (0x6375C0, 0x6375CD)] + thunks(0x4080E9), stubs=stubs, budget=5000)
    for index in range(cases * 25):
        state.reset()
        calls.clear()
        x, y = rng.randrange(0, 128), rng.randrange(0, 128)
        cell = ((y << 7) + x + 1) & 0xFFFF
        g = {'id': rng.randrange(1, 0x4000), 'target': rng.choice([0, 40, 40]), 'cell': cell, 'umbrella': rng.choice([0, 2, 3, 4]), 'count': rng.choice([0, 5, 11, 12])}
        sc.update(slot=rng.random() < 0.95, walk=rng.choice([0, 0, 1, 1, 2]), open=rng.random() < 0.9, back=rng.choice([cell, cell, 0, cell ^ 1]), route=rng.random() < 0.5,
                  room=rng.random() < 0.8, difference=rng.choice([0, 44, 45, -45, -44, 100, -50]), gate=rng.random() < 0.8, long=rng.random() < 0.2, find=rng.random() < 0.7,
                  closed=rng.random() < 0.1, appearance=rng.choice([0, 0, 1, 3]), weather=rng.choice([0, 1, 2, 3, 4]))
        world = state.object(0x1EAC00)
        state.write(WORLD_POINTER, 'I', world)
        state.write(0xAA1484, 'I', world)
        sc['park'] = state.object(0x100, {0x70: ('I', sc['weather'])})
        sc['typeobj'] = state.object(0x300, {0x21C: ('I', sc['appearance'])})
        if sc['slot'] and g['target']:
            ride = state.object(0x200, {0x7C: ('I', int(sc['open'])), 0x130: ('I', state.object(0x600))})
            state.write(0xAA18B0 + 20 * g['target'], 'I', ride)
        elif not g['target']:
            sc['slot'] = False
        thing = state.object(0x300, {0x0: ('H', g['id']), 0x5: ('B', x), 0x7: ('B', y), 0x40: ('B', g['count']), 0x204: ('H', g['target']), 0x238: ('I', g['umbrella'])})
        state.call(0x616AC0, this=thing)
        port = port_state13(g, sc)
        count = state.read(thing + 0x40, 'B')
        expected_count = g['count'] if 'minor' not in port and not (sc['slot'] and sc['walk'] == 1 and 'closed' in port and port[-1] == 'closed') else 0
        if calls != port:
            bad += 1
            if bad <= 5:
                print(f'state 13 {g} {sc}: native {calls} port {port}')
        elif 'minor' in port and count != 0 or port and port[-1] == 'closed' and count != g['count'] + 1:
            bad += 1
            if bad <= 5:
                print(f'state 13 walk count {g["count"]}: native {count}')
    return bad


def state15_rounds(image: Image, rng: random.Random, cases: int) -> int:
    """The dispatch 0x619FF0 in state 15 (0x61A1DB): the walk and the setter stubbed; arrived or stuck set 14."""
    bad = 0
    calls: list = []
    sc: dict = {}
    dispatch = Sandbox(image, [(0x619FF0, 0x61A3A8)], stubs={0x40989A: lambda sb: (sc['walk'], 0), 0x4096E2: lambda sb: (calls.append(('setter', sb.arg(0))), (0, 4))[1]}, budget=500)
    for walk in (0, 1, 2, 3):
        dispatch.reset()
        calls.clear()
        sc['walk'] = walk
        thing = dispatch.object(0x300, {0x2C0: ('I', 15)})
        dispatch.call(0x619FF0, this=thing)
        if calls != ([('setter', 14)] if walk in (0, 2) else []):
            bad += 1
            print(f'state 15 walk {walk}: native {calls}')
    return bad


# ---- The native runs -------------------------------------------------------------------------------------------------------------

def main(cases: int) -> int:
    path = executable_path('tpi')
    if path is None:
        raise SystemExit('needs OPENTPW_TPI_EXE (tools/ppc-analysis/oracle_x86/README.md)')
    image = Image.load(path)
    rng = random.Random(1123)
    bad = {'catch': 0, 'setter': 0, 'pre': 0, 'verdict': 0}

    # the catch test
    catch = Sandbox(image, [(0x614720, 0x61479A)], budget=200)
    native_catchable = []
    for state in range(-1, 41):
        catch.reset()
        guest = catch.object(0x300, {0x2C0: ('i', state)})
        if catch.call(0x614720, this=guest).eax & 0xFF:
            native_catchable.append(state)
        if bool(catch.call(0x614720, this=guest).eax & 0xFF) != catchable(state):
            bad['catch'] += 1
    print(f'catch test: native catchable {native_catchable}')

    # the setter
    recorded: dict = {}

    def stay(sb: Sandbox):
        recorded['stay'] = sb.arg(0)
        return 0, 4

    def analyser(sb: Sandbox):
        return 0x7000000, 0

    def forbidden(name):
        def stub(sb: Sandbox):
            raise AssertionError(f'{name} reached in a P1 entry')
        return stub

    setter = Sandbox(image, [(0x61A540, 0x61AA54), (0x5D0F90, 0x5D0F9A), (0x6493E0, 0x649415), (0x5D79B0, 0x5D79D7), (0x60B3F0, 0x60B42C), (0x6118A0, 0x6118B7), (0x86FAB0, 0x86FAD7)]
                     + thunks(0x406A37, 0x40A92F, 0x4021C6, 0x406BBD, 0x40AE34),
                     stubs={0x40144C: analyser, 0x4084BD: stay, 0x40362F: forbidden('0x5E7A00'), 0x4022F2: forbidden('0x5DF6D0')}, budget=5000)
    p1_states = [s for s in range(31) if s not in (7, 17, 19, 20, 21, 26)]
    for index in range(cases):
        for state in p1_states:
            setter.reset()
            setter.set_fpcw(CONTROL_WORD)
            recorded.clear()
            turn = rng.randrange(0, 1 << 32)
            stamp = rng.choice([turn, rng.randrange(0, 1 << 32)])
            booths = [(rng.randrange(0, 128), rng.randrange(0, 128), rng.randrange(0, 2048)) for _ in range(rng.choice([1, 2, 3]))]
            on_booth = rng.random() < 0.5
            x, y = (booths[rng.randrange(len(booths))][:2]) if on_booth else (rng.randrange(0, 128), rng.randrange(0, 128))
            guest = {'state': rng.randrange(0, 31), 'mode': rng.choice([0, 1, 3]), 'wait': rng.randrange(0, 400), 'heading': rng.randrange(0, 2048), 'spot': rng.randrange(0, 1 << 32),
                     'w224': 0, 'w21c': 0, 'b21b': rng.randrange(0, 256), 'speed': rng.choice([0, 25, 50]), 'target': rng.randrange(0, 0x10000), 'firstaid': rng.randrange(0, 200),
                     'paid': rng.random() < 0.5, 'stamp': stamp, 'cell': ((y << 7) + x + 1) & 0xFFFF}
            seed = rng.randrange(0, 1 << 32)
            world = setter.object(0x1EAC00, {TURN: ('I', turn), SEED: ('I', seed)})
            setter.write(WORLD_POINTER, 'I', world)
            setter.write(BOOTHS, 'I', len(booths))
            for number, (bx, by, angle) in enumerate(booths):
                setter.write(BOOTH_TABLE + 12 * number, 'I', bx)
                setter.write(BOOTH_TABLE + 12 * number + 4, 'I', by)
                setter.write(BOOTH_TABLE + 12 * number + 8, 'I', angle)
            thing = setter.object(0x300, {0x2: ('B', 1), 0x5: ('B', x), 0x7: ('B', y), 0x20: ('I', guest['mode']), 0x2C: ('I', guest['heading']), 0xD6: ('H', guest['speed']),
                                          0x1FC: ('I', stamp), 0x204: ('H', guest['target']), 0x21B: ('B', guest['b21b']), 0x228: ('I', guest['wait']), 0x22C: ('I', int(guest['paid'])),
                                          0x230: ('I', guest['spot']), 0x238: ('I', 0), 0x2B4: ('i', guest['firstaid']), 0x2C0: ('I', guest['state'])})
            setter.call(0x61A540, args=(state,), this=thing)
            native = {'state': setter.read(thing + 0x2C0, 'I'), 'mode': setter.read(thing + 0x20, 'I'), 'wait': setter.read(thing + 0x228, 'I'), 'heading': setter.read(thing + 0x2C, 'I'),
                      'spot': setter.read(thing + 0x230, 'I'), 'w224': setter.read(thing + 0x224, 'I'), 'w21c': setter.read(thing + 0x21C, 'i'), 'speed': setter.read(thing + 0xD6, 'H'),
                      'target': setter.read(thing + 0x204, 'H'), 'firstaid': setter.read(thing + 0x2B4, 'i'), 'seed': setter.read(world + SEED, 'I'), 'stay': recorded.get('stay')}
            replica_world = World(seed)
            port = port_setter(state, guest, booths, turn, replica_world)
            port['seed'] = replica_world.state
            port.setdefault('stay', None)
            for key, value in native.items():
                if port[key] != value:
                    bad['setter'] += 1
                    if bad['setter'] <= 5:
                        print(f'setter {state}: {key} native {value} port {port[key]}')

    # the pre-update
    calls: list = []

    def record(name, pop, value=0):
        def stub(sb: Sandbox):
            calls.append(name if pop == 0 or name == 'object' else (name, sb.arg(0)))
            return value, pop
        return stub

    state_park: dict = {}
    pre = Sandbox(image, [(0x619B50, 0x619EF8), (0x6375C0, 0x6375CD), (0x5D7420, 0x5D7446), (0x60D0D0, 0x60D10B), (0x86FAB0, 0x86FAD7), (0x60B3F0, 0x60B42C),
                          (0x6493E0, 0x649415), (0x611D60, 0x611D86), (0x611D20, 0x611D47)]
                  + thunks(0x4080E9, 0x40942B, 0x402932, 0x406BBD, 0x40A92F, 0x40AB41, 0x404F6B),
                  stubs={0x40C563: lambda sb: (0, 0), 0x408ED1: lambda sb: (calls.append('weather'), (0, 0))[1], 0x40C126: lambda sb: (state_park['in'], 0),
                         0x40332D: lambda sb: (calls.append('pick'), (0, 0))[1], 0x4071EE: lambda sb: (calls.append(('thought', sb.arg(0))), (0, 8))[1],
                         0x406C35: lambda sb: (calls.append(('balloon', sb.arg(0))), (0, 4))[1], 0x402BBC: lambda sb: (calls.append('object'), (0, 0))[1]}, budget=20000)
    meters = [0.0, 50.0, 79.5, 80.0, 80.75, 94.5, 95.0, 99.9, 100.0, 100.0]
    for index in range(cases * 25):
        pre.reset()
        pre.set_fpcw(CONTROL_WORD)
        calls.clear()
        thing_id = rng.randrange(1, 0x10000)
        turn = rng.randrange(0, 1 << 20) * 4 + (thing_id & 3 if rng.random() < 0.8 else rng.randrange(0, 4))
        if rng.random() < 0.2:
            turn = (turn // 80) * 80 + (thing_id & 3) if rng.random() < 0.5 else turn - turn % 16
        x, y = rng.randrange(0, 128), rng.randrange(0, 128)
        cell = tuple(rng.choice([0, 0, 1, -1, 3, -5, 40, -40, 120, -120]) for _ in range(3))
        state_park['in'] = int(rng.random() < 0.7)
        guest = {'id': thing_id, 'happiness': rng.choice(meters + [rng.uniform(0, 100)]), 'thirst': rng.choice(meters + [rng.uniform(0, 100)]),
                 'hunger': rng.choice(meters + [rng.uniform(0, 100)]), 'toilet': rng.choice(meters + [rng.uniform(0, 100)]), 'illness': rng.choice(meters + [rng.uniform(0, 100)]),
                 'exit': rng.randrange(-5, 500) & MASK, 'speed': rng.choice([0, 25, 50]), 'umbrella': rng.randrange(0, 5), 'state': rng.randrange(0, 31), 'saved': rng.randrange(0, 31),
                 'countdown': rng.choice([0, 0, 1, 2, rng.randrange(0, 300)]), 'q2cc': rng.randrange(0, 0x10000), 'q2ce': rng.randrange(0, 0x10000),
                 'history': [rng.randrange(0, 0x10000) for _ in range(4)]}
        for key in ('happiness', 'thirst', 'hunger', 'toilet', 'illness'):
            guest[key] = f32(guest[key])
        seed = rng.randrange(0, 1 << 32)
        world = pre.object(0x1EAC00, {TURN: ('I', turn), SEED: ('I', seed)})
        pre.write(WORLD_POINTER, 'I', world)
        cells = pre.object(REGION + 10 * 0x4000 + 0x100)
        pre.write(CELLS_POINTER, 'I', cells)
        record_address = cells + REGION + 10 * (y * 128 + x)
        for k, value in enumerate(cell):
            pre.write(record_address + 2 * k, 'h', value)
        thing = pre.object(0x300, {0x0: ('H', thing_id), 0x2: ('B', 1), 0x5: ('B', x), 0x7: ('B', y), 0xD6: ('H', guest['speed']),
                                   0x1BC: ('f', guest['happiness']), 0x1C8: ('f', guest['thirst']), 0x1CC: ('f', guest['hunger']), 0x1D0: ('f', guest['toilet']),
                                   0x1D4: ('f', guest['illness']), 0x1E0: ('I', guest['exit']), 0x238: ('I', guest['umbrella']), 0x298: ('I', guest['countdown']),
                                   0x2C0: ('I', guest['state']), 0x2C4: ('I', guest['saved']), 0x2CC: ('H', guest['q2cc']), 0x2CE: ('H', guest['q2ce'])})
        for k, word in enumerate(guest['history']):
            pre.write(thing + 0x212 + 2 * k, 'H', word)
        pre.call(0x619B50, this=thing)
        native = {'happiness': pre.read(thing + 0x1BC, 'f'), 'thirst': pre.read(thing + 0x1C8, 'f'), 'hunger': pre.read(thing + 0x1CC, 'f'), 'toilet': pre.read(thing + 0x1D0, 'f'),
                  'illness': pre.read(thing + 0x1D4, 'f'), 'exit': pre.read(thing + 0x1E0, 'I'), 'speed': pre.read(thing + 0xD6, 'H'), 'countdown': pre.read(thing + 0x298, 'I'),
                  'q2cc': pre.read(thing + 0x2CC, 'H'), 'q2ce': pre.read(thing + 0x2CE, 'H'), 'history': [pre.read(thing + 0x212 + 2 * k, 'H') for k in range(4)],
                  'calls': list(calls), 'seed': pre.read(world + SEED, 'I')}
        replica_world = World(seed)
        port = port_preupdate(guest, turn, cell, bool(state_park['in']), replica_world)
        port['seed'] = replica_world.state
        for key, value in native.items():
            if port[key] != value:
                bad['pre'] += 1
                if bad['pre'] <= 5:
                    print(f'pre-update id {thing_id} turn {turn}: {key} native {value} port {port[key]}')

    # the booth's verdict
    opinions: list = []
    verdict_state: dict = {}
    verdict = Sandbox(image, [(0x6157D0, 0x615912), (0x6493E0, 0x649415)] + thunks(0x40A92F),
                      stubs={0x403E45: lambda sb: (0, 0), 0x40144C: lambda sb: (0x7000000, 0), 0x402BD5: lambda sb: (verdict_state['points'], 0),
                             0x40AE2A: lambda sb: (verdict_state['park'], 0), 0x40B6E5: lambda sb: (opinions.append((sb.arg(1) & 0xFF, sb.arg(0))), (0, 8))[1]}, budget=2000)
    for index in range(cases * 25):
        verdict.reset()
        verdict.set_fpcw(CONTROL_WORD)
        opinions.clear()
        divisor = rng.choice([1, 2, 4, 6, 7, rng.randrange(1, 40)])
        minimum = rng.choice([0, 20, rng.randrange(0, 60)])
        multipliers = [f32(rng.choice([0.75, 0.5, rng.uniform(0.1, 1.0)])), f32(rng.choice([1.25, rng.uniform(1.0, 2.0)])), f32(rng.choice([2.0, rng.uniform(1.5, 4.0)]))]
        opinion = [rng.randrange(0, 101) for _ in range(4)]
        points = rng.choice([0, rng.randrange(0, 200), rng.randrange(0, 5000), rng.randrange(1 << 26, 1 << 32)])
        fee = rng.choice([0, minimum, rng.randrange(0, 200), rng.randrange(0, 2000), rng.randrange(1 << 24, 1 << 31)])
        seed = rng.randrange(0, 1 << 32)
        world = verdict.object(0x1EAC00, {SEED: ('I', seed)})
        verdict.write(WORLD_POINTER, 'I', world)
        park = verdict.object(0x200, {0x120: ('I', fee)})
        verdict_state.update(points=points, park=park)
        for address, value in ((0xA01A90, divisor), (0xA01A94, minimum)):
            verdict.write(address, 'I', value)
        for address, value in zip((0xA01A98, 0xA01A9C, 0xA01AA0), multipliers):
            verdict.write(address, 'f', value)
        for k, value in enumerate(opinion):
            verdict.write(0xA01AEC + 4 * k, 'I', value)
        native = (verdict.call(0x6157D0).eax, list(opinions), verdict.read(world + SEED, 'I'))
        replica_world = World(seed)
        value = (points // (replica_world.word() % ((divisor >> 1) + 1) + divisor) + minimum) & MASK
        if value <= minimum and fee <= minimum:
            port = (3, [])
        else:
            b = fee
            if x87_single_product(multipliers[1], value) <= b:
                port = (0, [(opinion[0], 0)]) if b >= x87_single_product(multipliers[2], value) else (1, [(opinion[1], 0)])
            else:
                port = (2, [(opinion[2], 0)]) if b <= x87_single_product(multipliers[0], value) else (3, [(opinion[3], 0)])
        if native != (*port, replica_world.state):
            bad['verdict'] += 1
            if bad['verdict'] <= 5:
                print(f'verdict points {points} fee {fee} divisor {divisor} minimum {minimum}: native {native} port {port}')

    bad['idle'] = idle_rounds(image, rng, cases)
    bad['spot'] = spot_rounds(image, rng, cases)
    bad['dispatch'] = dispatch_rounds(image, rng, cases)
    bad['bin'] = bin_rounds(image, rng, cases)
    bad['score'] = score_rounds(image, rng, cases)
    bad['queue'] = queue_rounds(image, rng, cases)
    bad['13'] = state13_rounds(image, rng, cases)
    bad['14'] = state14_rounds(image, rng, cases)
    bad['15'] = state15_rounds(image, rng, cases)
    print(f"{cases} cases: catch {bad['catch']}, setter {bad['setter']}, pre-update {bad['pre']}, verdict {bad['verdict']}, idle {bad['idle']}, spot and mood {bad['spot']}, "
          f"states 7 and 8 {bad['dispatch']}, state 9 {bad['bin']}, score {bad['score']}, queue functions {bad['queue']}, state 13 {bad['13']}, state 14 {bad['14']}, "
          f"state 15 {bad['15']} mismatches")
    return sum(bad.values())


if __name__ == '__main__':
    sys.exit(1 if main(int(sys.argv[1]) if len(sys.argv) > 1 else 40) else 0)

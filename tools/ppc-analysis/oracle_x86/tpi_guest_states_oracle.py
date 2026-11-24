"""Theme Park Inc's native guest states (phases P1 and P2) run in the x86 oracle sandbox and compared with the C# port's reading (ThemeParkIncGuestStates, GuestSimulation.ThemeParkInc).

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
  heading, +0x29C, +0x2B1, the prank code, +0x204, +0x224, the walk mask and the draws are compared with UpdateTpiIdle (prank codes 7 and 8, whose rumours and whinging are phase P5's, are left out);
- the spot animation 0x60FB30 for the animations 0..15 (the id, the mode store native; the sound and the setter recorded) and the mood 0x60CDD0 over generations -1..3 and the bank's byte +0x206;
- the dispatch 0x619FF0 in states 7 (0x61A179) and 8 (0x61A382), the walk, the wander, the restore and the setter stubbed;
- state 9 (0x616F10) with the walk, the bin's customer, its script variables and the drop stubbed; the dirt test 0x5E3990 and its rows, which the port gates, are left out of the comparison.

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
        sb.write(sb.arg(0), 'H', sc['bin'] if sb.arg(1) == 3 else 0)
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
             0x4094EE: plant}
    idle = Sandbox(image, [(0x614A40, 0x615412), (0x6493E0, 0x649415), (0x86FAB0, 0x86FAD7), (0x6375C0, 0x6375CD), (0x614460, 0x6144E3), (0x91E074, 0x91E1B4)]
                   + thunks(0x40A92F, 0x4080E9, 0x406C26), stubs=stubs, budget=20000)
    meters = [0.0, 10.0, 14.0, 15.0, 50.0, 80.0, 81.0, 89.5, 90.0, 95.0, 100.0]
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
                  tile=rng.choice([0, 0, 0, 3]), show=rng.choice([0, 0, 0, rng.randrange(0x4000, 0x5000)]), show_cell=near(), square=rng.random() < 0.3,
                  ent=rng.randrange(0x5000, 0x6000), ent_cell=near(), ent_state=rng.choice([0, 1, 0x17, 0x17]), wander=rng.random() < 0.6,
                  r1=rng.choice([0, 0x3FFE, 0x3FFF, rng.randrange(0, 0x8000)]), r2=rng.choice([0, 0x3FFE, 0x3FFF, rng.randrange(0, 0x8000)]))
        sc['rands'] = [sc['r1'], sc['r2']]
        g = {'id': thing_id, 'cell': (x, y), 'happiness': f32(rng.choice(meters + [rng.uniform(0, 100)])), 'illness': f32(rng.choice(meters)),
             'litter': f32(rng.choice(meters)), 'toilet': f32(rng.uniform(0, 100)), 'exit': rng.choice([0, 1, 400, (-3) & MASK]), 'umbrella': rng.randrange(0, 5),
             'spot': rng.choice([turn, (turn - 101) & MASK, (turn - 100) & MASK, rng.randrange(0, 1 << 32)]), 'w224': rng.choice([turn, (turn - 30) & MASK, (turn - 31) & MASK, 0]),
             'w29c': rng.choice([0, 0, 1]), 'b2b1': rng.choice([0, 0, 0, 1]), 'code': rng.choice([0, 1, 2, 3, 4, 5, 6]), 'target': rng.randrange(0, 0x4000),
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
        replica = World(seed)
        port = port_idle(g, sc, replica)
        port['seed'] = replica.state
        for key, value in native.items():
            if port[key] != value:
                bad += 1
                if bad <= 5:
                    print(f'idle id {thing_id}: {key} native {value} port {port[key]}')
                break
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
        points = rng.choice([0, rng.randrange(0, 200), rng.randrange(0, 5000)])
        fee = rng.choice([0, minimum, rng.randrange(0, 200), rng.randrange(0, 2000)])
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
            a, b = f32(float(value)), f32(float(fee))
            if f32(multipliers[1] * a) <= b:
                port = (0, [(opinion[0], 0)]) if b >= f32(multipliers[2] * a) else (1, [(opinion[1], 0)])
            else:
                port = (2, [(opinion[2], 0)]) if b <= f32(multipliers[0] * a) else (3, [(opinion[3], 0)])
        if native != (*port, replica_world.state):
            bad['verdict'] += 1
            if bad['verdict'] <= 5:
                print(f'verdict points {points} fee {fee} divisor {divisor} minimum {minimum}: native {native} port {port}')

    bad['idle'] = idle_rounds(image, rng, cases)
    bad['spot'] = spot_rounds(image, rng, cases)
    bad['dispatch'] = dispatch_rounds(image, rng, cases)
    bad['bin'] = bin_rounds(image, rng, cases)
    print(f"{cases} cases: catch {bad['catch']}, setter {bad['setter']}, pre-update {bad['pre']}, verdict {bad['verdict']}, idle {bad['idle']}, spot and mood {bad['spot']}, "
          f"states 7 and 8 {bad['dispatch']}, state 9 {bad['bin']} mismatches")
    return sum(bad.values())


if __name__ == '__main__':
    sys.exit(1 if main(int(sys.argv[1]) if len(sys.argv) > 1 else 40) else 0)

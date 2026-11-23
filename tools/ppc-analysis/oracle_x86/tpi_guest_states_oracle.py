"""Theme Park Inc's native guest states (phase P1) run in the x86 oracle sandbox and compared with the C# port's reading (ThemeParkIncGuestStates, GuestSimulation.ThemeParkInc).

Dev-only tooling like the rest of oracle_x86: Unicorn and the identified Theme Park Inc no-CD Game.exe (OPENTPW_TPI_EXE, SHA-256 pinned) are needed, nothing here is shipped.
Four routines run natively, at the game thread's x87 control word 0x7F (24-bit significand):
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

Usage: python3 -I tools/ppc-analysis/oracle_x86/tpi_guest_states_oracle.py [cases]
"""
from __future__ import annotations

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

    print(f"{cases} cases: catch {bad['catch']}, setter {bad['setter']}, pre-update {bad['pre']}, verdict {bad['verdict']} mismatches")
    return sum(bad.values())


if __name__ == '__main__':
    sys.exit(1 if main(int(sys.argv[1]) if len(sys.argv) > 1 else 40) else 0)

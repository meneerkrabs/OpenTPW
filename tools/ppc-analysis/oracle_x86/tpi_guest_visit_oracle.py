"""Theme Park Inc's native guest visit (phase P3b) run in the x86 oracle sandbox and compared with the C# port's reading (GuestSimulation.ThemeParkIncVisit).

Dev-only tooling like the rest of oracle_x86: Unicorn and the identified Theme Park Inc no-CD Game.exe (OPENTPW_TPI_EXE, SHA-256 pinned) are needed, nothing here is shipped. Every routine runs natively at
the game thread's x87 control word 0x7F (24-bit significand); the callees outside the routine are stubbed and recorded unless named as native:
- state 16 (0x617F80) over random walks, price verdicts, weather reactions (+0x28C, +0x290, the 0.66 × SlowestWeatherChangeResponse test), balloons, admissions and rejoins: the id and the x87 test
  native; the calls, +0x28C and +0x290 against UpdateTpiBeingLoaded;
- state 17's entry through the setter 0x61A540 with the chance roll 0x5E7A00, the world generator and the mode store native and the script's variable write recorded; +0x21B, VAR 11 and the draw against
  EnterTpiWaitingToBeAdmitted; and state 17 in the dispatch 0x619FF0 (0x61A21D) with the ride lookup, 0x5E4630, the list and the setter stubbed;
- state 20's entry through the setter (0x61A77E) over random type words, balloons and umbrellas: the mode store native; the beam, the thought clear, the balloon release and model, the umbrella, the
  sprite and the challenge events recorded, against EnterTpiUnderObjectControl;
- state 19 (0x618380) over random walks, photo booth and minor destinations (thing classes, appearance words, balloons, umbrellas, back cells, routes) and use results: the calls and +0x204, +0x206
  against UpdateTpiBeingUnloaded;
- the use routine 0x6118D0 with its payment step 0x6126D0, its outcome 0x612740 and the match step 0x611DA0 over random guests, type words (kinds, effects, ingredients, appearance, relief),
  thing words, ratings and preferences: the clamped adder 0x60D0D0, _ftol, the world generator, the sprite draws 0x690370/0x6902F0 over a set-up bank table, the reseed 0x649430, the speed term
  0x60B3F0, the thing words and the photo booth word native; the income and cost bookings, the sound, the customer, the records 0x5AEDE0, the thoughts, the spot animation, the gender, the rating, the
  goods cost, the models, the analyser counters, the toilet wear and the ring 0x5E61D0 recorded. Every meter, word and the draws are compared with ThemeParkIncUseRoutine. The gated parts are left
  out of the inputs: a primed toilet (+0x3C) and the toilet prank's +0x29C (phase P4), and the analyser counters, the ring 0x5E61D0 and the toilet wear are recorded but not compared.

Usage: python3 -I tools/ppc-analysis/oracle_x86/tpi_guest_visit_oracle.py [cases]
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
from tpi_guest_states_oracle import (MASK, WORLD_POINTER, TURN, SEED, CONTROL_WORD, World, add, byte, f32, thunks,  # noqa: E402
                                     x87_single_product)

LOG = 0x403E45


def s32(value: int) -> int:
    value &= MASK
    return value - (1 << 32) if value >> 31 else value


def trunc_div(a: int, b: int) -> int:
    q = abs(a) // abs(b)
    return q if (a >= 0) == (b >= 0) else -q


# ---- State 16 ------------------------------------------------------------------------------------------------------------------

def port_state16(g: dict, sc: dict) -> list:
    """GuestSimulation.UpdateTpiBeingLoaded over the oracle's stubs."""
    calls: list = ['walk']
    if sc['walk'] not in (0, 2):
        return calls
    calls.append('price')
    gid = g['id']
    if sc['price']:
        return calls + [('thought', 6), ('loss', 1), ('let go', gid), ('remove', gid), ('dismiss', 0), 'walkaway', ('setter', 6)]
    if g['b28c'] == 1:
        remaining = (g['w290'] - (g['turn'] >> 2)) & MASK
        limit = x87_single_product(f32(0.66), sc['slowest'])
        if g['balloon'] or g['countdown'] > 0 or not limit < remaining:
            return calls + [('let go', gid), ('remove', gid), ('dismiss', 1), 'walkaway', ('setter', 6)]
        g['b28c'], g['w290'] = 0, 0
    calls.append(('admit', gid))
    if sc['admit']:
        return calls + [('setter', 17)]
    calls.append('find')
    if sc['find']:
        return calls
    return calls + [('remove', gid), ('dismiss', 0), ('setter', 6)]


def state16_rounds(image: Image, rng: random.Random, cases: int) -> int:
    bad = 0
    calls: list = []
    sc: dict = {}

    def rec(name, pop, value=None, arg=False, mask=None):
        def stub(sb: Sandbox):
            a = sb.arg(0) if arg else None
            calls.append((name, a & mask if mask else a) if arg else name)
            return (int(sc[value]) if isinstance(value, str) else 0), pop
        return stub

    stubs = {0x40989A: rec('walk', 0, 'walk'), 0x40B3E3: rec('price', 4, 'price'), LOG: lambda sb: (0, 0), 0x4071EE: lambda sb: (calls.append(('thought', sb.arg(0))), (0, 8))[1],
             0x406C26: rec('loss', 4, arg=True), 0x40B4BF: rec('let go', 4, arg=True, mask=0xFFFF), 0x40853A: rec('remove', 4, arg=True, mask=0xFFFF),
             0x409C05: rec('dismiss', 4, arg=True), 0x405E84: rec('walkaway', 0), 0x4096E2: rec('setter', 4, arg=True), 0x403C7E: rec('admit', 4, 'admit', arg=True, mask=0xFFFF),
             0x405556: rec('find', 0, 'find')}
    state = Sandbox(image, [(0x617F80, 0x6181FA), (0x6375C0, 0x6375CD)] + thunks(0x4080E9), stubs=stubs, budget=3000)
    for _ in range(cases * 25):
        state.reset()
        state.set_fpcw(CONTROL_WORD)
        calls.clear()
        turn = rng.randrange(0, 1 << 32)
        g = {'id': rng.randrange(1, 0x4000), 'turn': turn, 'b28c': rng.choice([0, 1, 1, 2]), 'w290': ((turn >> 2) + rng.randrange(-4, 6)) & MASK,
             'balloon': rng.random() < 0.15, 'countdown': rng.choice([0, 0, 0, 1, 200])}
        sc.update(walk=rng.choice([0, 0, 1, 2]), price=rng.random() < 0.2, slowest=rng.choice([1, 3, 4, 7, 10]), admit=rng.random() < 0.6, find=rng.random() < 0.6)
        world = state.object(0x1EAC00, {TURN: ('I', turn)})
        state.write(0xAA1484, 'I', world)
        state.write(0xA01B64, 'I', sc['slowest'])
        state.write(0xAA18B0 + 20 * 40, 'I', state.object(0x200))
        thing = state.object(0x300, {0x0: ('H', g['id']), 0x204: ('H', 40), 0x28C: ('B', g['b28c']), 0x290: ('I', g['w290']), 0x294: ('I', 0x5000 if g['balloon'] else 0),
                                     0x298: ('I', g['countdown'])})
        state.call(0x617F80, this=thing)
        native = (list(calls), state.read(thing + 0x28C, 'B'), state.read(thing + 0x290, 'I'))
        port_g = dict(g)
        port = (port_state16(port_g, sc), port_g['b28c'], port_g['w290'])
        if native != port:
            bad += 1
            if bad <= 5:
                print(f'state 16 {g} {sc}: native {native} port {port}')
    return bad


# ---- State 17 ------------------------------------------------------------------------------------------------------------------

def state17_rounds(image: Image, rng: random.Random, cases: int) -> int:
    bad = 0
    writes: list = []
    calls: list = []
    sc: dict = {}
    entry = Sandbox(image, [(0x61A540, 0x61AA60), (0x5E7A00, 0x5E7A71), (0x6493E0, 0x649415), (0x5D0F90, 0x5D0F98)] + thunks(0x406A37, 0x40362F, 0x40A92F),
                    stubs={0x40C1D0: lambda sb: (0, 4), 0x40AE89: lambda sb: (0x6000, 0), 0x4028EC: lambda sb: (writes.append((sb.arg(1), sb.arg(2))), (0, 0))[1]}, budget=3000)
    for _ in range(cases * 25):
        entry.reset()
        entry.set_fpcw(CONTROL_WORD)
        writes.clear()
        win = rng.choice([0, 1, 50, 99, 100, 101, 255, rng.randrange(0, 256)])
        seed = rng.randrange(0, 1 << 32)
        world = entry.object(0x1EAC00, {SEED: ('I', seed)})
        entry.write(WORLD_POINTER, 'I', world)
        entry.write(0xAA1484, 'I', world)
        ride = entry.object(0x200, {0x90: ('B', win), 0x2C: ('I', 0x7000)})
        entry.write(0xAA18B0 + 20 * 40, 'I', ride)
        thing = entry.object(0x300, {0x204: ('H', 40), 0x21B: ('B', 3)})
        entry.call(0x61A540, args=(17,), this=thing)
        native = (entry.read(thing + 0x21B, 'B'), list(writes), entry.read(thing + 0x20, 'I'), entry.read(world + SEED, 'I'))
        replica = World(seed)
        draw = replica.word() % 100
        won = 1 if win >= draw else 0
        port = (won, [(11, won)], 3, replica.state)
        if native != port:
            bad += 1
            if bad <= 5:
                print(f'state 17 entry win byte {win}: native {native} port {port}')
    dispatch = Sandbox(image, [(0x619FF0, 0x61A3A8), (0x6375C0, 0x6375CD)] + thunks(0x4080E9),
                       stubs={0x40A268: lambda sb: (sc['ride'], 4), 0x404737: lambda sb: (int(sc['taken']), 0), 0x40853A: lambda sb: (calls.append(('remove', sb.arg(0) & 0xFFFF)), (0, 4))[1],
                              0x4096E2: lambda sb: (calls.append(('setter', sb.arg(0))), (0, 4))[1]}, budget=500)
    for taken in (False, True):
        dispatch.reset()
        calls.clear()
        sc['taken'] = taken
        sc['ride'] = dispatch.object(0x200)
        thing = dispatch.object(0x300, {0x0: ('H', 77), 0x204: ('H', 40), 0x2C0: ('I', 17)})
        dispatch.call(0x619FF0, this=thing)
        if calls != ([('remove', 77), ('setter', 20)] if taken else []):
            bad += 1
            print(f'state 17 taken {taken}: native {calls}')
    return bad


# ---- State 20's entry ----------------------------------------------------------------------------------------------------------

def state20_rounds(image: Image, rng: random.Random, cases: int) -> int:
    bad = 0
    calls: list = []
    sc: dict = {}

    def event(sb: Sandbox):
        calls.append(('event', sb.arg(0), sb.arg(1), sb.arg(2) & 0xFFFF, sb.arg(3)))
        return 0x6100, 16

    stubs = {0x4022F2: lambda sb: (sc['typeobj'], 0), 0x406A5F: lambda sb: (calls.append(('beam', sb.arg(0))), (0, 12))[1], 0x409D6D: lambda sb: (calls.append('clear'), (0, 0))[1],
             0x406C35: lambda sb: (calls.append(('release', sb.arg(0))), (0, 4))[1], 0x409502: lambda sb: (calls.append('model gone'), (0, 0))[1],
             0x4055AB: lambda sb: (calls.append('umbrella'), (0, 0))[1], 0x402E5A: lambda sb: (calls.append(('sprite down', sb.arg(0))), (0, 4))[1], 0x4082C9: event,
             0x4020BD: lambda sb: (0, 0), 0x4089DB: lambda sb: (sb.write(sb.arg(0), 'H', 41), (sb.arg(0), 4))[1]}
    entry = Sandbox(image, [(0x61A540, 0x61AA60), (0x5D0F90, 0x5D0F98)] + thunks(0x406A37), stubs=stubs, budget=3000)
    for _ in range(cases * 25):
        entry.reset()
        calls.clear()
        teleport, appearance, photo = rng.random() < 0.3, rng.choice([0, 1, 3]), rng.random() < 0.1
        handles, balloon, countdown, umbrella = rng.random() < 0.5, rng.random() < 0.3, rng.choice([0, 0, 5]), rng.choice([0, 0, 2, 4, 1])
        sc['typeobj'] = entry.object(0x700, {0x1C0: ('I', int(teleport)), 0x21C: ('I', appearance), 0xCC: ('I', int(photo))})
        ride = entry.object(0x200, {0x16: ('H', 0x123), 0x42: ('H', 0x20 if handles else 0)})
        entry.write(0xAA18B0 + 20 * 40, 'I', ride)
        entry.write(0xAA18B0 + 20 * 41, 'I', entry.object(0x200, {0x16: ('H', 0x456)}))
        thing = entry.object(0x300, {0x204: ('H', 40), 0x20: ('I', 3), 0x294: ('I', 0x5000 if balloon else 0), 0x298: ('I', countdown), 0x238: ('I', umbrella)})
        entry.call(0x61A540, args=(20,), this=thing)
        port = []
        if teleport:
            port.append(('beam', 0x13))
        port.append('clear')
        if balloon or countdown > 0:
            port.append(('release', 1) if appearance == 3 else 'model gone')
        if umbrella != 0:
            port.append('umbrella')
        if not handles:
            port.append(('sprite down', 0))
        port += [('event', 0x11, 1, 0x123, 0), ('event', 0x34, 1, 0x123, 0), ('event', 0x3A, 1, 0x123, 0)]
        port.append(('event', 0x39, 1, 0x456, 1) if photo else ('event', 0x39, 1, 0x123, 0))
        native_294 = entry.read(thing + 0x294, 'I')
        port_294 = 0x5000 if balloon and appearance == 3 else 0
        native = (list(calls), entry.read(thing + 0x20, 'I'), native_294 if appearance != 3 else port_294)
        if native != (port, 3, port_294):
            bad += 1
            if bad <= 5:
                print(f'state 20 entry: native {native} port {(port, 3, port_294)}')
    return bad


# ---- State 19 ------------------------------------------------------------------------------------------------------------------

def port_state19(g: dict, sc: dict) -> list:
    """GuestSimulation.UpdateTpiBeingUnloaded over the oracle's stubs (things 50 and 60 by class and appearance)."""
    calls: list = ['walk']
    if sc['walk'] == 2:
        g['target'] = 0
        return calls + [('setter', 6)]
    if sc['walk'] != 0:
        return calls
    if g['w208']:
        g['target'] = g['w208']
        thing = sc['things'][g['w208']]
        if thing['class'] == 3:
            calls.append('back')
            if thing['back']:
                calls.append(('route', thing['back']))
                if thing['route']:
                    return calls + [('setter', 13)]
        else:
            g['target'] = 0
    if g['w206']:
        g['target'] = g['w206']
        g['w206'] = 0
        thing = sc['things'][g['target']]
        if thing['class'] == 3:
            trapped = thing['appearance'] == 3 and g['umbrella'] != 0 or thing['appearance'] == 1 and (g['balloon'] or g['countdown'] > 0)
            if trapped:
                g['target'] = 0
            else:
                calls.append('back')
                if thing['back']:
                    calls.append(('route', thing['back']))
                    if thing['route']:
                        return calls + [('setter', 13)]
                g['target'] = 0
    calls.append(('setter', 6))
    if g['umbrella'] in (0, 2):
        if g['succeeded']:
            calls += ['mood', ('spot', 'mood')]
        else:
            calls.append(('spot', 10))
    return calls


def state19_rounds(image: Image, rng: random.Random, cases: int) -> int:
    bad = 0
    calls: list = []
    sc: dict = {}
    objects: dict = {}

    def back(sb: Sandbox):
        calls.append('back')
        thing = objects[sb.reg('ecx')]
        sb.write(sb.arg(0), 'I', thing['back'])
        return sb.arg(0), 4

    def route(sb: Sandbox):
        calls.append(('route', sb.arg(0) & 0xFFFF))
        return int(sc['route_now']), 4

    def type_of(sb: Sandbox):
        return objects[sb.reg('ecx')]['typeobj'], 0

    stubs = {0x40989A: lambda sb: (calls.append('walk'), (sc['walk'], 0))[1], 0x405B55: back, 0x4039DB: route, LOG: lambda sb: (0, 0), 0x4096E2: lambda sb: (calls.append(('setter', sb.arg(0))), (0, 4))[1],
             0x4022F2: type_of, 0x40A560: lambda sb: (calls.append('mood'), (77, 0))[1], 0x404DB3: lambda sb: (calls.append(('spot', 'mood' if sb.arg(0) == 77 else sb.arg(0))), (0, 4))[1]}
    state = Sandbox(image, [(0x618380, 0x61857F), (0x6375C0, 0x6375CD)] + thunks(0x4080E9), stubs=stubs, budget=3000)
    for _ in range(cases * 25):
        state.reset()
        calls.clear()
        objects.clear()
        things = {}
        for tid in (50, 60):
            things[tid] = {'class': rng.choice([3, 3, 1]), 'appearance': rng.choice([0, 1, 3]), 'back': rng.choice([0, 0x1234, 0x1234]), 'route': rng.random() < 0.6}
        g = {'id': 9, 'target': 40, 'w208': rng.choice([0, 0, 50]), 'w206': rng.choice([0, 60]), 'umbrella': rng.choice([0, 0, 2, 3, 4]), 'balloon': rng.random() < 0.3,
             'countdown': rng.choice([0, 0, 4]), 'succeeded': rng.random() < 0.5}
        sc.update(walk=rng.choice([0, 0, 0, 1, 2]), things=things)
        for tid, thing in things.items():
            typeobj = state.object(0x300, {0x21C: ('I', thing['appearance'])})
            obj = state.object(0x200, {0x2: ('B', thing['class'])})
            objects[obj] = dict(thing, typeobj=typeobj)
            state.write(0xAA18B0 + 20 * tid, 'I', obj)
        # the route stub answers by the thing whose back cell was read last
        last = {'thing': None}

        def back_tracked(sb: Sandbox):
            last['thing'] = objects[sb.reg('ecx')]
            return back(sb)
        state.stubs[0x405B55] = back_tracked

        def route_tracked(sb: Sandbox):
            calls.append(('route', sb.arg(0) & 0xFFFF))
            return int(last['thing']['route']), 4
        state.stubs[0x4039DB] = route_tracked
        guest = state.object(0x300, {0x0: ('H', 9), 0x204: ('H', g['target']), 0x206: ('H', g['w206']), 0x208: ('H', g['w208']), 0x238: ('I', g['umbrella']),
                                     0x294: ('I', 0x5000 if g['balloon'] else 0), 0x298: ('I', g['countdown']), 0x2E4: ('B', int(g['succeeded']))})
        state.call(0x618380, this=guest)
        native = (list(calls), state.read(guest + 0x204, 'H'), state.read(guest + 0x206, 'H'))
        port_g = dict(g)
        port_calls = port_state19(port_g, sc)
        port = (port_calls, port_g['target'], port_g['w206'])
        if native != port:
            bad += 1
            if bad <= 5:
                print(f'state 19 {g} things {things} walk {sc["walk"]}: native {native} port {port}')
    return bad


# ---- The use routine -------------------------------------------------------------------------------------------------------

def ftol(value: float) -> int:
    """_ftol's low dword: truncation, 0 for a value outside the 64-bit range or not finite."""
    if math.isnan(value) or math.isinf(value) or abs(value) >= 2 ** 63:
        return 0
    return s32(int(value) & MASK)


def play_gain(prize: int, price: int, medium: int) -> int:
    """GuestSimulation.ThemeParkIncPlayGain: the quotient (fidiv) and the product (fmulp) rounded to singles; the ratio of fyl2x's full-precision logarithms rounded once to a single."""
    if price == 0:
        quotient = math.inf if prize > 0 else -math.inf if prize < 0 else math.nan
    else:
        quotient = f32(prize / price)
    if math.isnan(quotient) or quotient < 0:
        ratio = math.nan
    elif quotient == 0:
        ratio = -math.inf
    elif math.isinf(quotient):
        ratio = math.inf
    else:
        ratio = f32(math.log2(quotient))
    product = ratio * medium if not math.isnan(ratio) and not (math.isinf(ratio) and medium == 0) else math.nan
    if not (math.isnan(product) or math.isinf(product)):
        product = f32(product)
    return ftol(product)


def port_use(g: dict, t: dict, w: dict, s: dict, sc: dict, world: World) -> list:
    """GuestSimulation.ThemeParkIncUseRoutine, ThemeParkIncOutcome and ThemeParkIncMatchStep; g, t (type words), w (thing words), s (settings); returns the recorded calls."""
    calls: list = []
    kind = t['kind']
    g['history'] = [0x2A] + g['history'][:2] + [g['history'][1]]
    if 0 <= kind <= 2:
        g['visits'][kind] += 1
    if g['fee'] != 0:
        calls += [('income', g['fee']), ('sound', 0xD0)]
        g['purse'] = (g['purse'] - g['fee']) & MASK
        g['spent'] = (g['spent'] + g['fee']) & MASK
    calls.append('customer')
    g['fatigue'] = add(g['fatigue'], -float(t['fatigue']))
    if t['c0'] == 0x17:
        g['b21b'] = sc['param'] & 0xFF
    if g['b21b'] == 0:
        g['happiness'] = add(g['happiness'], -float(s['medium']))
        g['succeeded'] = 0
        g['words'][3] = add(g['words'][3], -35.0)
        if kind == 2:
            calls.append(('thought', 6))
        return calls
    # the outcome
    g['succeeded'] = 1
    if kind == 2:
        calls.append(('cost', w['cost']))
        g['purse'] = (g['purse'] + w['cost']) & MASK
        g['words'][3] = add(g['words'][3], 50.0)
    elif kind == 1:
        calls.append(('cost', sc['goods']))
    match = 0
    rating = sc['rating'] & 0xFF
    if rating != 0:
        preferred = g['preferred'] & 0xFF
        difference = abs(preferred - rating)
        if difference < s['perfect_match']:
            match = s['perfect']
        elif difference < s['good_match']:
            match = s['good_ride']
        elif difference < s['ok_match']:
            match = s['ok']
        if difference < s['ok_match']:
            g['words'][4] = add(g['words'][4], float(match))
        if s['good_match'] <= difference < s['ok_match'] and preferred < rating:
            g['declined'] += 1
        before = byte(g['illness'])
        g['illness'] = add(g['illness'], float(trunc_div(100 - byte(g['hunger']), 20) * trunc_div(rating, s['vomit'])))
        after = byte(g['illness'])
        if after > before:
            g['words'][4] = add(g['words'][4], -float(after - before))
    g['happiness'] = add(g['happiness'], float(match))
    g['happiness'] = add(g['happiness'], float(t['happiness']))
    mood = f32(t['happiness'] + match)
    g['illness'] = add(g['illness'], float(t['vomit']))
    if kind == 1:
        g['thirst'] = add(g['thirst'], -float(t['thirst']))
        g['hunger'] = add(g['hunger'], -float(t['hunger']))
        if t['thirst'] != t['hunger']:
            calls.append(('sound', 0x83 if t['thirst'] > t['hunger'] else 0x84))
        g['illness'] = add(g['illness'], 50.0)
        g['happiness'] = add(g['happiness'], 50.0)
        mood = f32(50 + mood)
        g['litter'] = add(g['litter'], float(t['litter']))
        g['litter'] = add(g['litter'], 50.0)
        g['toilet'] = add(g['toilet'], float(t['toilet']))
    amount = w['amount'] & 0xFF
    for effect, word in ((t['hunger'], 0), (t['thirst'], 1)):
        if effect == 0:
            continue
        draw = world.word()
        if ((draw & 7) + effect + amount) & MASK < 30:
            g['happiness'] = add(g['happiness'], -float(s['small']))
            mood = f32(mood - s['small'])
            g['words'][word] = add(g['words'][word], -30.0)
        else:
            g['words'][word] = add(g['words'][word], 35.0)
    scaled = trunc_div(t['happiness'] * amount, 100)
    g['happiness'] = add(g['happiness'], float(scaled))
    mood = f32(scaled + mood)
    ingredient = t['ingredient']
    if ingredient == 1:
        g['toilet'] = add(g['toilet'], float(trunc_div((t['fat'] + 1) * amount, 100)))
    elif ingredient == 2:
        g['thirst'] = add(g['thirst'], float(trunc_div((t['salt'] + 1) * amount, 100)))
    elif ingredient == 3:
        g['thirst'] = add(g['thirst'], float(trunc_div((t['ice'] + t['thirst']) * amount, 100)))
    elif ingredient == 4:
        g['boost'] = (g['boost'] + trunc_div((t['sugar'] + 1) * amount, 100)) & 0xFFFF
    quality = w['quality'] & 0xFF
    appearance = t['appearance']
    if appearance == 1:
        g['colour'] = (world.word() >> 2) % 4
        g['balloon'] = 1
        countdown = quality * 255 // 100
        g['countdown'] = 25 if countdown < 25 else 255 if countdown > 255 else countdown
        threshold = world.word() % 10 + s['complain']
        if quality >= threshold:
            g['words'][2] = add(g['words'][2], -33.0)
        else:
            g['declined'] += 1
            g['words'][2] = add(g['words'][2], 33.0)
    elif appearance == 2:
        if g['category'] == 6:
            g['category'] = 0
            world.state = g['id'] & 0xFFFF
            g['variant'] = (world.word() >> 2) % sc['counts'][0]
        else:
            g['category'] = 6
            g['variant'] = (world.word() >> 2) % sc['counts'][6]
            world.word()
            if quality < (world.word() % 10 + s['complain']) & MASK:
                g['declined'] += 1
            g['words'][2] = 33.0
    elif appearance == 3:
        g['umbrella_colour'] = (world.word() >> 2) % 3
        g['umbrella'] = 4
        g['speed'] = 0
        if quality < (world.word() % 10 + s['complain']) & MASK:
            g['declined'] += 1
        g['words'][2] = 33.0
    if t['relief']:
        calls.append(('wear', byte(g['toilet'])))
        if byte(g['illness']) > 90:
            g['illness'] = 0.0
            change = -50
        elif sc['worn']:
            change = -20
        else:
            change = byte(g['toilet']) >> 1
        g['happiness'] = add(g['happiness'], float(change))
        mood = f32(change + mood)
        g['toilet'] = 0.0
        g['speed'] = 0 if g['umbrella'] in (1, 3, 4) else 25
    if kind == 2:
        g['visits'][3] += 1
        gain = play_gain(w['cost'], w['price'], s['medium'])
        g['happiness'] = add(g['happiness'], float(gain))
        mood = f32(gain + mood)
    g['w208'] = 0
    animation = None
    down = g['umbrella'] in (0, 2)
    if s['vgood'] < mood:
        if down and 0 <= g['generation'] <= 2:
            animation = 5 if g['generation'] == 0 else (6 if sc['female'] else 7) if g['generation'] == 1 else (8 if sc['female'] else 9)
        calls.append(('thought', 5))
    elif s['vpoor'] > mood:
        if down:
            animation = 10
        calls.append(('thought', 6))
    elif s['good_band'] < mood:
        calls.append(('thought', 5))
    elif s['poor'] > mood:
        calls.append(('thought', 6))
        if down:
            animation = 10
    if animation is not None:
        calls.append(('spot', animation))
    # back in the use routine
    change = 3 * (byte(g['happiness']) - byte(g['join']))
    value = (change + 50) & 0xFF
    if kind == 1:
        if 1 <= ingredient <= 4:
            calls.append(('record', 0xA + ingredient, value))
        if 1 <= appearance <= 3:
            calls.append(('record', 0xF + appearance, value))
    elif kind == 2:
        calls.append(('record', 0x14, value))
    if kind == 2:
        calls.append(('thought', 5))
    return calls


GUEST_METERS = {'happiness': 0x1BC, 'thirst': 0x1C8, 'hunger': 0x1CC, 'toilet': 0x1D0, 'illness': 0x1D4, 'litter': 0x1D8, 'fatigue': 0x1DC, 'join': 0x234}


def use_rounds(image: Image, rng: random.Random, cases: int) -> int:
    bad = 0
    calls: list = []
    sc: dict = {}
    gated = ('analyser', 'ring', 'models')

    def rec(name, pop, arg=None, value=None):
        def stub(sb: Sandbox):
            calls.append((name,) + tuple(sb.arg(i) for i in arg) if arg else name)
            return (sc[value] if isinstance(value, str) else (value or 0)) & MASK, pop
        return stub

    def wear(sb: Sandbox):
        calls.append(('wear', sb.arg(0) & 0xFF))
        return 0, 4

    stubs = {0x4022F2: lambda sb: (sc['typeobj'], 0), 0x40C1D0: lambda sb: (sc['typeobj'], 4), 0x40A641: rec('income', 4, (0,)),
             0x408F71: lambda sb: (calls.append(('sound', sb.arg(1))), (0, 8))[1], 0x40B947: rec('customer', 0), 0x40AE89: lambda sb: (0x6000, 0),
             0x4078BA: lambda sb: (sc['param'], 0), 0x40144C: lambda sb: (sc['analyser'], 0), 0x40B6E5: lambda sb: (calls.append(('record', sb.arg(0), sb.arg(1) & 0xFF)), (0, 8))[1],
             0x40C392: rec('ring', 0), 0x4071EE: lambda sb: (calls.append(('thought', sb.arg(0))), (0, 8))[1], 0x403062: lambda sb: (sc['rating'], 4),
             0x402608: rec('cost', 4, (0,)), 0x40C45A: lambda sb: (sc['goods'], 0), 0x404A61: rec('models', 0, value=0x5000), 0x407E96: wear,
             0x404F52: lambda sb: (int(sc['worn']), 0), 0x404DB3: lambda sb: (calls.append(('spot', sb.arg(0))), (0, 4))[1], 0x40BEA1: lambda sb: (int(sc['female']), 0),
             0x408F5D: rec('analyser', 0), 0x4075B3: rec('analyser', 0), 0x405BAF: rec('analyser', 0), LOG: lambda sb: (0, 0)}
    ranges = [(0x6118D0, 0x611BF2), (0x611DA0, 0x612013), (0x6126D0, 0x61271B), (0x612740, 0x6136EA), (0x60D0D0, 0x60D109), (0x6493E0, 0x649415), (0x6375C0, 0x6375CD),
              (0x86FAB0, 0x86FAD7), (0x5E6220, 0x5E6227), (0x5E6200, 0x5E6207), (0x5E7510, 0x5E7517), (0x5E7450, 0x5E7457), (0x5E8EE0, 0x5E8EEE), (0x690370, 0x6903AB),
              (0x6902F0, 0x690315), (0x649430, 0x64943D), (0x60B3F0, 0x60B42C)]
    state = Sandbox(image, ranges + thunks(0x4080E9, 0x402932, 0x40A92F, 0x408A67, 0x40A150, 0x405B96, 0x405B28, 0x4097E6, 0x40A2B3, 0x402680, 0x403508, 0x406BBD, 0x409DC7,
                                           0x407E0A, 0x40A74F, 0x409FED), stubs=stubs, budget=20000)
    for _ in range(cases * 25):
        state.reset()
        state.set_fpcw(CONTROL_WORD)
        calls.clear()
        s = {'small': 5, 'medium': rng.choice([15, 10]), 'vomit': rng.choice([10, 4]), 'perfect_match': 5, 'good_match': 15, 'ok_match': 40, 'perfect': 25, 'good_ride': 15, 'ok': 5,
             'vgood': rng.choice([24, 10]), 'good_band': 14, 'poor': 5, 'vpoor': rng.choice([1, -20]), 'complain': rng.choice([20, 50])}
        kind = rng.choice([0, 1, 1, 2, 2, 3])
        t = {'kind': kind, 'c0': rng.choice([0, 0x17]), 'fatigue': rng.choice([0, 5, -3]), 'thirst': rng.choice([0, 0, 10, 30]), 'hunger': rng.choice([0, 0, 10, 25]),
             'vomit': rng.choice([0, 5]), 'happiness': rng.choice([0, 10, -5]), 'litter': rng.choice([0, 10]), 'toilet': rng.choice([0, 15]), 'ingredient': rng.choice([0, 0, 1, 2, 3, 4]),
             'appearance': rng.choice([0, 0, 1, 2, 3]), 'salt': rng.choice([0, 20]), 'fat': rng.choice([0, 30]), 'ice': rng.choice([0, 10]), 'sugar': rng.choice([0, 99]),
             'relief': rng.random() < 0.25}
        w = {'cost': rng.choice([0, 10, 40, rng.randrange(0, 100000)]), 'quality': rng.choice([0, 20, 50, 100]), 'price': rng.choice([0, 5, 20, rng.randrange(1, 100000)]),
             'amount': rng.choice([0, 30, 50, 100])}
        g = {'id': rng.randrange(1, 0x4000), 'fee': rng.choice([0, 0, 15]), 'purse': rng.randrange(0, 500), 'spent': rng.randrange(0, 100),
             'happiness': f32(rng.choice([10.0, 50.0, 80.5, 99.0])), 'thirst': f32(rng.choice([0.0, 50.0, 95.0])), 'hunger': f32(rng.choice([0.0, 60.0, 100.0])),
             'toilet': f32(rng.choice([0.0, 70.0, 99.5])), 'illness': f32(rng.choice([0.0, 40.0, 91.0, 99.0])), 'litter': f32(rng.choice([0.0, 60.0])), 'fatigue': f32(rng.choice([0.0, 50.0])),
             'join': f32(rng.choice([20.0, 50.0, 80.0])), 'b21b': rng.choice([0, 1, 1, 1]), 'visits': [rng.randrange(0, 5) for _ in range(4)], 'history': [rng.randrange(0, 90) for _ in range(4)],
             'preferred': rng.choice([10, 40, 60, 90]), 'generation': rng.choice([0, 1, 2]), 'umbrella': rng.choice([0, 0, 2, 4]), 'boost': rng.randrange(0, 0x10000),
             'category': rng.choice([0, 1, 2, 6]), 'variant': 0, 'words': [f32(rng.choice([0.0, 50.0, 99.0])) for _ in range(5)], 'speed': 0, 'balloon': 0, 'countdown': 0,
             'colour': 0, 'umbrella_colour': 0, 'succeeded': 0, 'w208': 7, 'declined': 0}
        sc.update(param=rng.choice([0, 1, 300]), rating=rng.choice([0, 30, 55, 90, 256 + 50]), goods=rng.choice([0, 7]), worn=rng.random() < 0.3, female=rng.random() < 0.5,
                  counts={0: rng.choice([1, 2, 4]), 6: rng.choice([1, 3])})
        seed = rng.randrange(0, 1 << 32)
        world = state.object(0x1EAC00, {SEED: ('I', seed)})
        state.write(WORLD_POINTER, 'I', world)
        state.write(0xAA1484, 'I', world)
        for address, value in ((0xA01A78, s['small']), (0xA01A7C, s['medium']), (0xA01A84, s['vomit']), (0xA01B1C, s['vgood']), (0xA01B20, s['good_band']), (0xA01B24, s['poor']),
                               (0xA01B28, s['vpoor']), (0xA01B2C, s['perfect_match']), (0xA01B30, s['good_match']), (0xA01B34, s['ok_match']), (0xA01B38, s['perfect']),
                               (0xA01B3C, s['good_ride']), (0xA01B40, s['ok']), (0xA01B44, s['complain'])):
            state.write(address, 'I', value & MASK)
        # the sprite tables: balloons (category 0xE) and umbrellas (0x13) from bank 10 and 11, the kids' and the costumes' counts
        for category, first, count in ((0xE, 10, 1), (0x13, 11, 1), (0, 0, sc['counts'][0]), (6, 12, sc['counts'][6])):
            state.write(0x9D7004 + 12 * category, 'I', first)
            state.write(0x9D7008 + 12 * category, 'I', count)
        state.write(0xB88AEC + 4 * 10, 'I', state.object(0x300, {0x21E: ('H', 4)}))
        state.write(0xB88AEC + 4 * 11, 'I', state.object(0x300, {0x21E: ('H', 3)}))
        sc['analyser'] = state.object(0x100)
        sc['typeobj'] = state.object(0x700, {0xC0: ('I', t['c0']), 0x198: ('I', t['fatigue'] & MASK), 0x200: ('I', t['thirst']), 0x204: ('I', t['hunger']), 0x208: ('I', t['vomit']),
                                             0x20C: ('I', t['happiness'] & MASK), 0x210: ('I', t['litter']), 0x214: ('I', t['toilet']), 0x218: ('I', t['ingredient']),
                                             0x21C: ('I', t['appearance']), 0x228: ('I', t['salt']), 0x22C: ('I', t['fat']), 0x230: ('I', t['ice']), 0x234: ('I', t['sugar']),
                                             0x600: ('I', kind)})
        history = state.object(0x700)
        ride = state.object(0x200, {0x0: ('H', 0x2A), 0x2C: ('I', 0x7000), 0x42: ('H', 1 if t['relief'] else 0), 0x88: ('I', w['cost']), 0x8C: ('B', w['quality']),
                                    0x94: ('I', w['price']), 0x98: ('B', w['amount']), 0x9C: ('I', 50), 0x130: ('I', history)})
        guest_fields = {0x0: ('H', g['id']), 0x2: ('B', 1), 0x5: ('B', 3), 0x7: ('B', 4), 0x30: ('I', g['variant']), 0x34: ('I', g['category']), 0xD6: ('H', 0), 0xD8: ('H', g['boost']),
                        0x1B4: ('I', g['fee']), 0x1C0: ('I', g['purse']), 0x1C4: ('I', g['spent']), 0x21B: ('B', g['b21b']), 0x208: ('H', g['w208']), 0x238: ('I', g['umbrella']),
                        0x2A4: ('I', g['generation']), 0x2A8: ('B', g['preferred'])}
        for name, offset in GUEST_METERS.items():
            guest_fields[offset] = ('f', g[name])
        for k in range(4):
            guest_fields[0x1EC + 4 * k] = ('I', g['visits'][k])
            guest_fields[0x20A + 2 * k] = ('H', g['history'][k])
        for k in range(5):
            guest_fields[0x2D0 + 4 * k] = ('f', g['words'][k])
        guest = state.object(0x300, guest_fields)
        state.call(0x6118D0, args=(ride,), this=guest)
        native_calls = [call for call in calls if (call if isinstance(call, str) else call[0]) not in gated]
        native = {name: state.read(guest + offset, 'f') for name, offset in GUEST_METERS.items() if name != 'join'}
        native.update(purse=state.read(guest + 0x1C0, 'I'), spent=state.read(guest + 0x1C4, 'I'), b21b=state.read(guest + 0x21B, 'B'), succeeded=state.read(guest + 0x2E4, 'B'),
                      visits=[state.read(guest + 0x1EC + 4 * k, 'I') for k in range(4)], history=[state.read(guest + 0x20A + 2 * k, 'H') for k in range(4)],
                      words=[state.read(guest + 0x2D0 + 4 * k, 'f') for k in range(5)], boost=state.read(guest + 0xD8, 'H'), speed=state.read(guest + 0xD6, 'H'),
                      balloon=int(state.read(guest + 0x294, 'I') != 0), countdown=state.read(guest + 0x298, 'I'), colour=state.read(guest + 0x2A0, 'I'),
                      umbrella=state.read(guest + 0x238, 'I'), umbrella_colour=state.read(guest + 0x244, 'I'), category=state.read(guest + 0x34, 'I'),
                      variant=state.read(guest + 0x30, 'I'), w208=state.read(guest + 0x208, 'H'), declined=state.read(history + 0x4C8, 'I'), seed=state.read(world + SEED, 'I'),
                      calls=native_calls)
        replica = World(seed)
        port_g = {key: (list(value) if isinstance(value, list) else value) for key, value in g.items()}
        port_g['history'] = list(g['history'])
        port_calls = port_use(port_g, t, w, s, sc, replica)
        port = {name: port_g[name] for name in GUEST_METERS if name != 'join'}
        port.update(purse=port_g['purse'], spent=port_g['spent'], b21b=port_g['b21b'], succeeded=port_g['succeeded'], visits=port_g['visits'], history=port_g['history'],
                    words=port_g['words'], boost=port_g['boost'], speed=port_g['speed'] if (t['relief'] or t['appearance'] == 3) else 0, balloon=port_g['balloon'],
                    countdown=port_g['countdown'], colour=port_g['colour'], umbrella=port_g['umbrella'], umbrella_colour=port_g['umbrella_colour'],
                    category=port_g['category'], variant=port_g['variant'], w208=port_g['w208'], declined=port_g['declined'], seed=replica.state, calls=port_calls)
        if native != port:
            bad += 1
            if bad <= 5:
                diff = {key: (native[key], port[key]) for key in native if native[key] != port[key]}
                print(f'use t {t} w {w} sc {sc}: {diff}')
    return bad


def main(cases: int) -> int:
    path = executable_path('tpi')
    if path is None:
        raise SystemExit('needs OPENTPW_TPI_EXE (tools/ppc-analysis/oracle_x86/README.md)')
    image = Image.load(path)
    rng = random.Random(0x50334B62)
    bad = {'16': state16_rounds(image, rng, cases), '17': state17_rounds(image, rng, cases), '20': state20_rounds(image, rng, cases), '19': state19_rounds(image, rng, cases),
           'use': use_rounds(image, rng, cases)}
    print(f"{cases} cases: state 16 {bad['16']}, state 17 {bad['17']}, state 20's entry {bad['20']}, state 19 {bad['19']}, the use routine {bad['use']} mismatches")
    return sum(bad.values())


if __name__ == '__main__':
    sys.exit(1 if main(int(sys.argv[1]) if len(sys.argv) > 1 else 40) else 0)

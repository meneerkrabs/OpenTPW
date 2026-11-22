"""Theme Park Inc's plant update and the gardener's plant chooser run in the x86 oracle sandbox and compared with the C# port's reading (ParkEconomy.ThemeParkIncPlants,
ThemeParkIncStaffWorld.Gardeners).

Dev-only tooling like the rest of oracle_x86: Unicorn and the identified Theme Park Inc no-CD Game.exe (OPENTPW_TPI_EXE, SHA-256 pinned) are needed, nothing here is shipped.
Two routines run natively:
- the object state dispatch 0x5E4CB0 on a thing in state 3 (the plant block 0x5E4CDE): the script reads and writes (0x40AE89, 0x4078BA, 0x4028EC), the RegionFX add and remove (0x40CAC7,
  0x40BDB6) and the advisor's notice clearing (0x4078E7, 0x4066D1) are stubbed and recorded; the weather thing lookup 0x64EB80, the handle getter 0x6375C0 and _ftol (0x86FAB0) run for real over the
  world pointer 0xADE6B0 and the thing table 0xAA18B0, with the four wither singles written at 0xA029A4..0xA029B0. VAR_SCALE's and VAR_OLDSTATE's writes, +0x54 and the row calls are compared;
- the gardener's chooser 0x5D2DC0 over a synthetic object list (world +0x1EAB88, each thing's next word +0x14): the claim reader 0x5E37F0 (its lapse with the validator 0x62B280 and the claimant's
  setter 0x628180, stubbed and recorded), the area test 0x62C6B0, the lerp 0x62C900, the request 0x5E3B70, the on-fire test 0x5E3B20, the dry test 0x5E3990 and the claim 0x5E3780 run for real; the
  drop fallback 0x5E9320 (it answers a path word beside each plant) and the route 0x62EAC0 (it answers 1 or 0 by the word) are stubbed and recorded. The chosen handle, the claim words and the
  routes tried are compared.
The routines run at the x87 control word 0x7F (24-bit significand), the game thread's after the Direct3D device started ([EXT:hardware-d3d-path]); the 53-bit word 0x27F is reported beside it.
`--golden` prints native cases for the C# tests instead of the mismatch count.

Usage: python3 -I tools/ppc-analysis/oracle_x86/tpi_gardener_oracle.py [cases] [--golden]
"""
from __future__ import annotations

import random
import struct
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))

from x86_harness import Image, Sandbox, executable_path  # noqa: E402

DISPATCH, CHOOSER = 0x5E4CB0, 0x5D2DC0
HANDLE, READ, WRITE = 0x40AE89, 0x4078BA, 0x4028EC
ROW_ADD, ROW_REMOVE, NOTICE_FIND, NOTICE_CLEAR = 0x40CAC7, 0x40BDB6, 0x4078E7, 0x4066D1
WEATHER_THUNK, ID_THUNK = 0x4019B5, 0x4080E9
CLAIM_READ_THUNK, AREA_THUNK, LERP_THUNK, REQUEST_THUNK, FIRE_THUNK, DRY_THUNK, FALLBACK_THUNK, ROUTE_THUNK, CLAIM_THUNK = (
    0x40896D, 0x401159, 0x40B924, 0x4086AC, 0x405768, 0x404F52, 0x40AA6F, 0x4037BF, 0x403F21)
VALIDATOR_THUNK, LOG_THUNK, SETTER_THUNK = 0x408B61, 0x403E45, 0x407A9F
FTOL = (0x86FAB0, 0x86FAD7)
WORLD_POINTER, TURN, LIST_HEAD, WEATHER_WORD, THINGS = 0xADE6B0, 0x1EAB40, 0x1EAB88, 0x1EAB58, 0xAA18B0
WITHERS = 0xA029A4
MAX_EXPERIENCE, RANGE_PAIR = 0xA02260, (0xA022D8, 0xA022DC)
MASK = 0xFFFFFFFF


def single(value: float) -> float:
    return struct.unpack('<f', struct.pack('<f', value))[0]


def bits(value: float) -> int:
    return struct.unpack('<I', struct.pack('<f', value))[0]


def signed(value: int) -> int:
    value &= MASK
    return value - (1 << 32) if value >> 31 else value


def div0(a: int, b: int) -> int:
    """C's signed division toward zero (idiv)."""
    q = abs(a) // abs(b)
    return q if (a >= 0) == (b >= 0) else -q


def wither(state: int, withers: tuple) -> float:
    """ParkEconomy.PlantWither: heatwave, rain, storm, snow for the states 1 to 4, else 1.0."""
    return withers[state - 1] if 1 <= state <= 4 else 1.0


def scale_word(value: float, precision: int) -> int:
    """ParkEconomy.PlantScaleWord: the product with 1000 rounded at the precision, truncated."""
    product = single(value * single(1000.0)) if precision == 24 else value * 1000.0
    return int(product) & MASK if abs(product) < 2147483648.0 else 0x80000000


def plant_update(variables: dict, state: int, withers: tuple, precision: int) -> tuple:
    """ParkEconomy.UpdateThemeParkIncPlant: (writes, +0x54, row removals, row additions)."""
    writes = []
    s = float(div0(signed(variables.get(14, 0)), 1000))
    w = wither(state, withers)
    if w != s:
        word = scale_word(w, precision)
        writes.append((14, word))
        variables[14] = word
    condition = signed(variables.get(0, 0))
    old = signed(variables.get(10, 0))
    gauge = single(float(signed((3 - condition) * 33)))
    removed, added = [], []
    if condition != old:
        writes.append((10, condition & MASK))
        if 0 <= old <= 3:
            removed.append(10 + old)
        if 0 <= condition <= 3:
            added.append(10 + condition)
    return writes, gauge, removed, added


def lerp(x: int, low: int, high: int, a: int, b: int) -> int:
    """ThemeParkIncStaffSettings.Lerp (0x62C900)."""
    if x > high:
        return b
    offset = signed(x - low)
    if offset < 0:
        return a
    t = div0(signed(offset << 14), signed(high - low))
    return div0(signed((0x4000 - t) * a + t * b), 16384)


def dry(plant: dict) -> bool:
    """ParkEconomy.ThemeParkIncPlantDry."""
    if signed(plant['condition']) <= 0:
        return False
    return plant['dead'] == 0 or plant['fire'] != 0


def choose(plants: list, gardener: dict, turn: int, max_experience: int, pair: tuple) -> tuple:
    """ThemeParkIncStaffWorld.FindPlant over the list: (handle, routes tried, claimant setters, lapsed handles)."""
    best_distance, best = MASK, 0
    routes, setters, lapsed = [], [], []

    def claimant(plant: dict) -> int:
        if plant['claim'] == 0:
            return 0
        if (turn & MASK) > ((plant['claim_turn'] + 100) & MASK):
            holder = plant['claimants'].get(plant['claim'])
            if holder is None or holder != plant['handle']:
                setters.append(plant['claim'])
                lapsed.append(plant['handle'])
                plant['claim'], plant['claim_turn'] = 0, 0
                return 0
        return plant['claim']

    for plant in plants:
        if not plant['plant']:
            continue
        if claimant(plant) != 0 and claimant(plant) != gardener['id']:
            continue
        dx, dy = abs(plant['x'] - gardener['x']), abs(plant['y'] - gardener['y'])
        distance = (dx * dx + dy * dy) & MASK
        radius = lerp(gardener['experience'], 0, max_experience, pair[0], pair[1])
        take = plant['requested'] != 0 and distance < best_distance and plant['fire'] == 0
        if not take:
            if not dry(plant) or distance >= ((radius * radius) & MASK) or plant['fire'] != 0:
                continue
        word = plant['word']
        if word == 0:
            continue
        routes.append(word)
        if not plant['route']:
            continue
        best_distance, best = distance, plant['handle']
    return best, routes, setters, lapsed


def main(cases: int, golden: bool) -> int:
    path = executable_path('tpi')
    if path is None:
        raise SystemExit('needs OPENTPW_TPI_EXE (tools/ppc-analysis/oracle_x86/README.md)')
    image = Image.load(path)
    state: dict = {}

    def read(sb: Sandbox):
        return state['vars'].get(sb.arg(1), 0) if sb.arg(0) == state['script'] else 0, 0

    def write(sb: Sandbox):
        state['writes'].append((sb.arg(1), sb.arg(2)))
        state['vars'][sb.arg(1)] = sb.arg(2)
        return 0, 0

    def row(kind):
        def stub(sb: Sandbox):
            state['rows'].append((kind, sb.arg(0), sb.arg(1) & 0xFFFF))
            return 0, 8
        return stub

    def script_handle(sb: Sandbox):
        return (state['script'] if sb.arg(0) in state['thing_scripts'] else 0), 0

    def chooser_read(sb: Sandbox):
        handle, index = sb.arg(0), sb.arg(1)
        plant = state['by_script'].get(handle)
        if plant is None:
            return 0, 0
        return {0: plant['condition'], 4: plant['dead'], 12: plant['fire']}.get(index, 0), 0

    def chooser_handle(sb: Sandbox):
        return state['script_of'].get(sb.arg(0), 0), 0

    def fallback(sb: Sandbox):
        # thiscall on the plant, (out word pointer, the gardener's cell word): the stubbed path word beside it
        plant = state['by_thing'][sb.reg('ecx')]
        sb.write(sb.arg(0), 'H', plant['word'])
        state['fallback'].append((plant['handle'], sb.arg(1) & 0xFFFF))
        return sb.arg(0), 8

    def route(sb: Sandbox):
        word = sb.arg(0) & 0xFFFF
        state['routes'].append(word)
        return (1 if word in state['routable'] else 0), 4

    def setter(sb: Sandbox):
        state['setters'].append(sb.read(sb.reg('ecx'), 'H'))
        return 0, 4

    stubs = {HANDLE: script_handle, READ: read, WRITE: write, ROW_ADD: row('add'), ROW_REMOVE: row('remove'), NOTICE_FIND: lambda sb: (state['scratch'], 12),
             NOTICE_CLEAR: lambda sb: (0, 0), FALLBACK_THUNK: fallback, ROUTE_THUNK: route, LOG_THUNK: lambda sb: (0, 0), SETTER_THUNK: setter}
    allowed = [(DISPATCH, 0x5E5070), (CHOOSER, 0x5D2FE0), FTOL, (0x64EB80, 0x64EBA0), (0x6375C0, 0x6375E0), (0x5E37F0, 0x5E38B0), (0x62C6B0, 0x62C700), (0x62C900, 0x62C950),
               (0x5E3780, 0x5E37A0), (0x5E3990, 0x5E3AD0), (0x5E3B20, 0x5E3B60), (0x5E3B70, 0x5E3B80), (0x62B280, 0x62B2C0)]
    allowed += [(thunk, thunk + 5) for thunk in (WEATHER_THUNK, ID_THUNK, CLAIM_READ_THUNK, AREA_THUNK, LERP_THUNK, REQUEST_THUNK, FIRE_THUNK, DRY_THUNK, CLAIM_THUNK, VALIDATOR_THUNK)]
    sandbox = Sandbox(image, allowed, stubs=stubs)
    rng = random.Random(20)
    bad = {24: 0, 53: 0}
    lines = []
    seen = {'scale': 0, 'move': 0, 'chosen': 0, 'requested': 0, 'lapsed': 0}
    for index in range(cases):
        for precision, control in ((24, 0x7F), (53, 0x27F)):
            # ---- the plant block
            sandbox.reset()
            state.update(writes=[], rows=[], scratch=sandbox.alloc(0x100), script=0x51, thing_scripts={0x77})
            withers = tuple(single(rng.choice([0.5, 2.0, 1.5, 1.0, 0.0, 0.001, 1.9999, rng.uniform(-3, 3)])) for _ in range(4))
            for slot, value in enumerate(withers):
                sandbox.write(WITHERS + 4 * slot, 'f', value)
            weather_state = rng.choice([0, 1, 2, 3, 4, 5, rng.randrange(-2, 8)])
            weather = sandbox.object(0x100, {0x70: ('i', weather_state)})
            world = sandbox.object(0x1EAC00, {WEATHER_WORD: ('H', 9)})
            sandbox.write(WORLD_POINTER, 'I', world)
            sandbox.write(THINGS + 20 * 9, 'I', weather)
            condition = rng.choice([0, 1, 2, 3, rng.randrange(-3, 7)])
            old = rng.choice([condition, condition, 0, 1, 2, 3, rng.randrange(-3, 7)])
            scale = rng.choice([1000, 2000, 500, 1500, 1999, -999, 0, rng.randrange(-5000, 5000)])
            state['vars'] = {0: condition & MASK, 10: old & MASK, 14: scale & MASK}
            plant = sandbox.object(0x140, {0x2: ('B', 3), 0x5: ('B', 20), 0x7: ('B', 30), 0x2C: ('I', 0x77), 0x43: ('B', 0x10), 0xA0: ('i', 3), 0x54: ('f', 12.5)})
            sandbox.set_fpcw(control)
            sandbox.call(DISPATCH, this=plant)
            word = (30 << 7) + 20 + 1
            native = ([(i, v & MASK) for i, v in state['writes']], sandbox.read(plant + 0x54, 'f'),
                      [r for k, r, w in state['rows'] if k == 'remove' and w == word], [r for k, r, w in state['rows'] if k == 'add' and w == word])
            port_writes, gauge, removed, added = plant_update({0: condition & MASK, 10: old & MASK, 14: scale & MASK}, weather_state, withers, precision)
            port = ([(i, v & MASK) for i, v in port_writes], gauge, removed, added)
            ok = native[0] == port[0] and bits(native[1]) == bits(port[1]) and native[2] == port[2] and native[3] == port[3] and len(state['rows']) == len(removed) + len(added)
            if precision == 24:
                seen['scale'] += any(i == 14 for i, _ in native[0])
                seen['move'] += bool(native[2] or native[3])
            if not ok:
                bad[precision] += 1
                if precision == 24:
                    print(f'plant(condition {condition}, old {old}, scale {scale}, weather {weather_state}, withers {withers}): native {native}, port {port}')
            elif golden and precision == 24 and index < 12:
                lines.append(f'plant condition {condition} old {old} scale {scale} weather {weather_state} withers {[f"{bits(w):#010x}" for w in withers]} -> writes {native[0]} '
                             f'gauge {bits(native[1]):#010x} removed {native[2]} added {native[3]}')
            # ---- the chooser
            sandbox.reset()
            state.update(scratch=sandbox.alloc(0x100), routes=[], setters=[], fallback=[], routable=set(), by_script={}, script_of={}, by_thing={})
            max_experience = rng.choice([100000, rng.randrange(1, 200000)])
            pair = rng.choice([(3, 5), (rng.randrange(0, 12), rng.randrange(0, 12))])
            sandbox.write(MAX_EXPERIENCE, 'i', max_experience)
            sandbox.write(RANGE_PAIR[0], 'i', pair[0])
            sandbox.write(RANGE_PAIR[1], 'i', pair[1])
            turn = rng.randrange(200, 100000)
            world = sandbox.object(0x1EAC00, {TURN: ('I', turn)})
            sandbox.write(WORLD_POINTER, 'I', world)
            gardener_id = 40
            gx, gy = rng.randrange(10, 60), rng.randrange(10, 60)
            experience = rng.choice([0, max_experience, rng.randrange(0, max_experience)])
            gardener = sandbox.object(0x600, {0: ('H', gardener_id), 2: ('B', 0x14), 5: ('B', gx), 7: ('B', gy), 0x210: ('i', experience),
                                              0x24C: ('i', 0), 0x250: ('i', 0), 0x254: ('i', 0x7F), 0x258: ('i', 0x7F)})
            sandbox.write(THINGS + 20 * gardener_id, 'I', gardener)
            # other claimants: gardener 41 targets plant 5, gardener 42 targets nothing
            others = {}
            for other, target in ((41, 5), (42, 0)):
                thing = sandbox.object(0x600, {0: ('H', other), 2: ('B', 0x14), 0x5D8: ('H', target)})
                sandbox.write(THINGS + 20 * other, 'I', thing)
                others[other] = target
            count = rng.randrange(1, 9)
            plants = []
            for slot in range(count):
                handle = 2 + slot
                is_plant = rng.random() < 0.85
                claim = rng.choice([0, 0, 0, gardener_id, 41, 42])
                plant_state = {
                    'handle': handle, 'plant': is_plant, 'x': max(0, min(127, gx + rng.randrange(-8, 9))), 'y': max(0, min(127, gy + rng.randrange(-8, 9))),
                    'condition': rng.choice([0, 1, 2, 3, -1]) & MASK, 'dead': rng.choice([0, 0, 1]), 'fire': rng.choice([0, 0, 0, 1]),
                    'requested': rng.choice([0, 0, 1]), 'claim': claim, 'claim_turn': rng.choice([turn, turn - 100, turn - 101, turn - 500]) & MASK if claim else 0,
                    'word': rng.choice([0, 0x100 + handle]), 'route': rng.random() < 0.8, 'claimants': {gardener_id: 0, **others}}
                plants.append(plant_state)
            things = []
            for slot, plant_state in enumerate(plants):
                script = 0x60 + slot
                fields = {0: ('H', plant_state['handle']), 2: ('B', 3), 5: ('B', plant_state['x']), 7: ('B', plant_state['y']), 0x2C: ('I', 0x200 + slot),
                          0x43: ('B', 0x10 if plant_state['plant'] else 0), 0x72: ('H', plant_state['claim']), 0x74: ('I', plant_state['claim_turn']),
                          0x78: ('I', plant_state['requested']), 0x14: ('H', plants[slot + 1]['handle'] if slot + 1 < len(plants) else 0)}
                thing = sandbox.object(0x140, fields)
                sandbox.write(THINGS + 20 * plant_state['handle'], 'I', thing)
                state['by_script'][script] = plant_state
                state['script_of'][0x200 + slot] = script
                state['by_thing'][thing] = plant_state
                if plant_state['route'] and plant_state['word']:
                    state['routable'].add(plant_state['word'])
                things.append(thing)
            sandbox.write(world + LIST_HEAD, 'H', plants[0]['handle'])
            out = sandbox.alloc(4)
            sandbox.stubs[HANDLE] = chooser_handle
            sandbox.stubs[READ] = chooser_read
            sandbox.set_fpcw(control)
            sandbox.call(CHOOSER, args=(out,), this=gardener)
            sandbox.stubs[HANDLE] = script_handle
            sandbox.stubs[READ] = read
            chosen = sandbox.read(out, 'H')
            claims = [(sandbox.read(thing + 0x72, 'H'), sandbox.read(thing + 0x74, 'I')) for thing in things]
            native = (chosen, state['routes'], state['setters'], claims)
            port_choice, port_routes, port_setters, lapsed = choose([dict(p) for p in plants], {'id': gardener_id, 'x': gx, 'y': gy, 'experience': experience}, turn, max_experience, pair)
            port_claims = []
            for plant_state in plants:
                claim, claim_turn = plant_state['claim'], plant_state['claim_turn']
                if plant_state['handle'] in lapsed:
                    claim, claim_turn = 0, 0
                if plant_state['handle'] == port_choice:
                    claim, claim_turn = gardener_id, turn
                port_claims.append((claim, claim_turn & MASK))
            port = (port_choice, port_routes, port_setters, port_claims)
            if precision == 24:
                seen['chosen'] += chosen != 0
                seen['requested'] += any(p['requested'] and p['handle'] == chosen for p in plants)
                seen['lapsed'] += bool(state['setters'])
            if native != port:
                bad[precision] += 1
                if precision == 24:
                    print(f'chooser(gardener {(gx, gy, experience)}, pair {pair}, max {max_experience}, turn {turn}, plants {plants}): native {native}, port {port}')
            elif golden and precision == 24 and index < 12:
                lines.append(f'chooser gardener {(gx, gy)} exp {experience} max {max_experience} pair {pair} turn {turn} plants '
                             f'{[(p["handle"], p["plant"], p["x"], p["y"], signed(p["condition"]), p["dead"], p["fire"], p["requested"], p["claim"], p["claim_turn"] - turn if p["claim"] else None, p["word"], p["route"]) for p in plants]} '
                             f'-> {chosen} routes {state["routes"]} setters {state["setters"]}')
    for line in lines:
        print(line)
    print(f'{cases} plant-update and chooser cases ({seen["scale"]} scale writes, {seen["move"]} row moves, {seen["chosen"]} plants chosen, {seen["requested"]} requested, '
          f'{seen["lapsed"]} lapsed claims): {bad[24]} mismatches at 24 bits, {bad[53]} at 53 bits')
    return 1 if bad[24] else 0


if __name__ == '__main__':
    arguments = [a for a in sys.argv[1:] if not a.startswith('--')]
    raise SystemExit(main(int(arguments[0]) if arguments else 2000, '--golden' in sys.argv))

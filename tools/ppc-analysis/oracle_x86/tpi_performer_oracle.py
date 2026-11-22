"""Theme Park Inc's entertainer and guard run in the x86 oracle sandbox and compared with the C# port's reading (ThemeParkIncStaffWorld.Performers, ParkEnvironment.ApplyRadius).

Dev-only tooling like the rest of oracle_x86: Unicorn and the identified Theme Park Inc no-CD Game.exe (OPENTPW_TPI_EXE, SHA-256 pinned) are needed, nothing here is shipped.
Five routines run natively:
- the pre-updates 0x5D1960 (entertainer, row 0) and 0x5D3B40 (guard, row 3) over a cell manager with the region grid (0x1C1104, ten bytes a cell) and the row table (0x1E9104, twelve bytes a row):
  0x609E20 (the stored cell word +0xE4), the lerp 0x62C900 and the region writer 0x5D7770 with its wrappers 0x5D7500 and 0x5D7530 run for real, the common step 0x628050 is stubbed and recorded.
  The grid's words and +0xE4 are compared with ParkEnvironment.ApplyRadius and ThemeParkIncStaffWorld.MoveEffect;
- the entertainer's search 0x5D1A30: the gate 0x62BBA0, the tired test 0x62B3B0, the draw 0x6493E0, the finder 0x5B1B20, the destination 0x609F70, the theme mapping 0x42A8D0, the visual spawn
  0x60B700 and the setter 0x5D1D30 are stubbed and recorded; the lerp runs for real. The draws taken, the finder's arguments and the setter's states are compared with SearchEntertainer;
- the entertainer's setter 0x5D1D30 (the draw's remainder by the bank's performance count, the theme record's index, the variant byte): the template queued, the state and the stamp are compared
  with PerformanceTemplateOf;
- the constructors 0x5D1380 and 0x5D3140/0x5D3230 (the base constructor, list, sprite, search and advisor calls stubbed): the world draws taken (the guard's 0x6902F0 variant draw, (R >> 2) mod the loaded banks; the
  entertainer draws none) and the category and variant words;
- the guard's decision 0x5D3C10: the gate, the destination and the setter 0x5D3E70 are stubbed and recorded and compared with DecideGuard.
The pre-updates and the search have no x87; `--golden` prints native cases for the C# tests instead of the mismatch count.

Usage: python3 -I tools/ppc-analysis/oracle_x86/tpi_performer_oracle.py [cases] [--golden]
"""
from __future__ import annotations

import random
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))

from x86_harness import Image, Sandbox, executable_path  # noqa: E402

MASK = 0xFFFFFFFF
WORLD_POINTER, CELLS_POINTER, TURN = 0xADE6B0, 0xADE6B4, 0x1EAB40
GRID, ROWS = 0x1C1104, 0x1E9104
MAX_EXPERIENCE, REST_RANGE = 0xA02260, {'entertainer': (0xA022EC, 0xA022F0), 'guard': (0xA02314, 0xA02318)}
# the thunks the routines call through
COMMON, GATE, TIRED, DRAW, FINDER_HEAD, FINDER, PATROL, THEME, SPAWN, SETTER = 0x40193D, 0x40628A, 0x40B25D, 0x40A92F, 0x40144C, 0x402734, 0x40BAA0, 0x40B46A, 0x406A5F, 0x40A768
COUNT, CAREER, THEME_RECORD, BASE_SETTER, TEMPLATE, GUARD_SETTER = 0x409494, 0x40B528, 0x4098F4, 0x407A9F, 0x406A37, 0x406807
REAL_THUNKS = (0x4081C0, 0x40B924, 0x407077, 0x4070DB, 0x404BE2, 0x402680, 0x40389B, 0x403E3B)
ALLOWED = [(0x5D1960, 0x5D19FA), (0x5D1A30, 0x5D1B68), (0x5D1D30, 0x5D1DDF), (0x5D3B40, 0x5D3BDA), (0x5D3C10, 0x5D3C4B), (0x609E20, 0x609E49), (0x62C900, 0x62C950),
           (0x5D7500, 0x5D7517), (0x5D7530, 0x5D7547), (0x5D7770, 0x5D78F4), (0x5D1380, 0x5D14A3), (0x5D3140, 0x5D31CE), (0x5D3230, 0x5D3336), (0x690330 - 0x40, 0x690315), (0x629860, 0x629863)] + [(thunk, thunk + 5) for thunk in REAL_THUNKS]


def signed(value: int) -> int:
    value &= MASK
    return value - (1 << 32) if value >> 31 else value


def div0(a: int, b: int) -> int:
    """C's signed division toward zero (idiv)."""
    q = abs(a) // abs(b)
    return q if (a >= 0) == (b >= 0) else -q


def lerp(x: int, low: int, high: int, a: int, b: int) -> int:
    """ThemeParkIncStaffSettings.Lerp (0x62C900)."""
    if x > high:
        return b
    offset = signed(x - low)
    if offset < 0:
        return a
    t = div0(signed(offset << 14), signed(high - low))
    return div0(signed((0x4000 - t) * a + t * b), 16384)


def cell_word(x: int, y: int) -> int:
    return ((y << 7) + x + 1) & 0xFFFF


def cell_of(word: int) -> tuple:
    return (word - 1) & 0x7F, ((word - 1) & 0xFFFF) >> 7


def spread(grid: dict, row: list, x: int, y: int, radius: int, sign: int) -> None:
    """ParkEnvironment.ApplyRadius: the grid as {(x, y): [five halfwords]}."""
    radius &= 0xFFFF
    for cx in range(max(0, x - radius), min(127, x + radius) + 1):
        for cy in range(max(0, y - radius), min(127, y + radius) + 1):
            divisor = abs(cx - x) + abs(cy - y) + 1
            words = grid.setdefault((cx, cy), [0] * 5)
            for k in range(5):
                words[k] = (words[k] + div0(sign * row[k], divisor)) & 0xFFFF


def move_effect(grid: dict, row: list, stored: int, now: int, radius: int) -> int:
    """ThemeParkIncStaffWorld.MoveEffect: the stored word afterwards."""
    if stored != now:
        ox, oy = cell_of(stored)
        nx, ny = cell_of(now)
        spread(grid, row, ox, oy, radius, -1)
        spread(grid, row, nx, ny, radius, 1)
    return now


def template_of(remainder: int, theme: int, variant: int) -> int:
    """ThemeParkIncStaffWorld.PerformanceTemplateOf."""
    if remainder == 0:
        return 0x13 if theme != 0 or variant != 0 else 0x14
    return 0x15 if theme == 2 and variant == 1 else 0x16


def search(turn: int, gate: int, tired: int, draws: list, found: int, patrol: int, experience: int, max_experience: int, pair: tuple, cell: tuple) -> tuple:
    """ThemeParkIncStaffWorld.SearchEntertainer: (setter states, finder calls, draws taken)."""
    setters, finders, used = [], [], 0
    if gate:
        return setters, finders, used
    if not tired:
        radius = lerp(experience, 0, max_experience, pair[1], pair[1])
        remainder = draws[used] % 3
        used += 1
        if remainder == 0:
            finders.append((cell, radius))
            if found:
                setters.append(0x17)
                return setters, finders, used
    if turn & 3 == 0:
        setters.append(0)
    elif patrol:
        setters.append(1)
    return setters, finders, used


def main(cases: int, golden: bool) -> int:
    path = executable_path('tpi')
    if path is None:
        raise SystemExit('needs OPENTPW_TPI_EXE (tools/ppc-analysis/oracle_x86/README.md)')
    image = Image.load(path)
    state: dict = {}

    def popper(function, pop):
        def stub(sb: Sandbox):
            return function(sb), pop
        return stub

    def draw(sb: Sandbox):
        state['taken'] += 1
        return state['draws'][state['taken'] - 1]

    def record(name, value=0):
        def function(sb: Sandbox):
            state['calls'].append((name, sb.arg(0), sb.arg(1), sb.arg(2)))
            return value
        return function

    def finder(sb: Sandbox):
        state['finder'].append((sb.arg(0), sb.arg(1) & 0xFFFF, sb.arg(2) & MASK))
        return state['found']

    def setter(sb: Sandbox):
        state['setters'].append(sb.arg(0))
        return 0

    def template(sb: Sandbox):
        state['templates'].append(sb.arg(0))
        return 0

    stubs = {
        0x403E45: popper(lambda sb: 0, 0), 0x401D66: popper(record('base'), 24), 0x405704: popper(lambda sb: 0, 4), 0x401A9B: popper(lambda sb: 0, 4), 0x40B028: popper(lambda sb: 0, 12),
        0x402BC1: popper(lambda sb: 0, 4), 0x8934A0: popper(lambda sb: 0, 0), 0x4078E7: popper(lambda sb: 0x1000, 4), 0x40795F: popper(lambda sb: 0, 0), 0x40ACEA: popper(lambda sb: 0x2000, 8), 0x401960: popper(lambda sb: 0, 0), 0x402829: popper(record('search'), 0), COMMON: popper(record('common'), 0), GATE: popper(lambda sb: state['gate'], 0), TIRED: popper(lambda sb: state['tired'], 0), DRAW: popper(draw, 0),
        FINDER_HEAD: popper(lambda sb: 0x1234, 0), FINDER: popper(finder, 12), PATROL: popper(lambda sb: state['patrol'], 0), THEME: popper(lambda sb: state['theme_value'], 0),
        SPAWN: popper(record('spawn'), 12), SETTER: popper(setter, 4), GUARD_SETTER: popper(setter, 4), COUNT: popper(lambda sb: state['count'], 0),
        CAREER: popper(lambda sb: state['career'], 0), THEME_RECORD: popper(lambda sb: state['theme_record'], 4), BASE_SETTER: popper(setter, 4), TEMPLATE: popper(template, 4)}
    sandbox = Sandbox(image, ALLOWED, stubs=stubs, budget=3_000_000)
    rng = random.Random(21)
    bad = {'move': 0, 'search': 0, 'setter': 0, 'guard': 0, 'ctor': 0}
    lines = []
    seen = {'moved': 0, 'found': 0, 'idle': 0, 'walk': 0, 'performing': 0, 'spawn': 0, 'gate': 0}

    def prepare_world(turn: int) -> int:
        world = sandbox.object(0x1EAC00, {TURN: ('I', turn)})
        sandbox.write(WORLD_POINTER, 'I', world)
        return world

    for index in range(cases):
        # ---- the pre-updates over the real region writer
        for role, entry in (('entertainer', 0x5D1960), ('guard', 0x5D3B40)):
            sandbox.reset()
            state.update(calls=[])
            max_experience = rng.choice([100000, rng.randrange(1, 200000)])
            pair = (rng.randrange(0, 8), rng.randrange(0, 14))
            row_number = 0 if role == 'entertainer' else 3
            sandbox.write(MAX_EXPERIENCE, 'i', max_experience)
            sandbox.write(REST_RANGE[role][0], 'i', pair[0])
            sandbox.write(REST_RANGE[role][1], 'i', pair[1])
            cells = sandbox.object(GRID + 0x28000 + 0x200)
            sandbox.write(CELLS_POINTER, 'I', cells)
            rows = [[rng.choice([0, 0, 2, 10, -7, rng.randrange(-40, 40)]) for _ in range(5)] for _ in range(4)]
            for number, values in enumerate(rows):
                for k, value in enumerate(values):
                    sandbox.write(cells + ROWS + 12 * number + 2 * k, 'h', value)
            x, y = rng.randrange(0, 128), rng.randrange(0, 128)
            stored = cell_word(x, y) if rng.random() < 0.3 else cell_word(rng.randrange(0, 128), rng.randrange(0, 128))
            experience = rng.choice([0, max_experience, rng.randrange(0, max_experience)])
            thing = sandbox.object(0x600, {0x5: ('B', x), 0x7: ('B', y), 0x210: ('i', experience), 0xE4: ('H', stored)})
            prepare_world(0)
            sandbox.call(entry, this=thing)
            native = {}
            for cell in range(128 * 128):
                words = [sandbox.read(cells + GRID + 10 * cell + 2 * k, 'H') for k in range(5)]
                if any(words):
                    native[(cell % 128, cell // 128)] = words
            replica: dict = {}
            radius = lerp(experience, 0, max_experience, pair[0], pair[1])
            after = move_effect(replica, rows[row_number], stored, cell_word(x, y), radius)
            replica = {key: words for key, words in replica.items() if any(words)}
            ok = native == replica and sandbox.read(thing + 0xE4, 'H') == after and len(state['calls']) == 1
            seen['moved'] += stored != cell_word(x, y)
            if not ok:
                bad['move'] += 1
                print(f'{role} pre-update(cell {(x, y)}, stored {cell_of(stored)}, exp {experience}/{max_experience}, pair {pair}, row {rows[row_number]}): native {len(native)} cells, port {len(replica)} cells')
            elif golden and index < 6:
                lines.append(f'{role} move cell {(x, y)} stored {stored} exp {experience} max {max_experience} pair {pair} row {rows[row_number]} radius {radius} -> cells {len(native)} sum {sum(sum(w) for w in native.values()) & 0xFFFF}')
        # ---- the entertainer's search
        sandbox.reset()
        turn = rng.randrange(0, 1000)
        max_experience = rng.choice([100000, rng.randrange(1, 200000)])
        pair = (rng.randrange(0, 8), rng.randrange(0, 14))
        sandbox.write(MAX_EXPERIENCE, 'i', max_experience)
        sandbox.write(REST_RANGE['entertainer'][0], 'i', pair[0])
        sandbox.write(REST_RANGE['entertainer'][1], 'i', pair[1])
        experience = rng.choice([0, max_experience, rng.randrange(0, max_experience)])
        x, y = rng.randrange(0, 128), rng.randrange(0, 128)
        entry_state = rng.choice([0, 1, 0x17])
        variant = rng.choice([0, 1, 2])
        thing = sandbox.object(0x600, {0x5: ('B', x), 0x7: ('B', y), 0x210: ('i', experience), 0x1B4: ('I', entry_state), 0x30: ('I', variant)})
        prepare_world(turn)
        state.update(gate=rng.choice([0, 0, 1]), tired=rng.choice([0, 0, 1]), draws=[rng.randrange(0, 1 << 32) if rng.random() < 0.6 else 3 * rng.randrange(0, 1000) for _ in range(4)], taken=0,
                     found=rng.choice([0, 1]), patrol=rng.choice([0, 1]), theme_value=rng.choice([1, 2, 3, 4]), finder=[], setters=[], calls=[])
        sandbox.call(0x5D1A30, this=thing)
        port = search(turn, state['gate'], state['tired'], state['draws'], state['found'], state['patrol'], experience, max_experience, pair, (x, y))
        # the finder's arguments are (kind 1, the cell word, the radius)
        native_finder = [(cell_of(c), r) for kind, c, r in state['finder'] if kind == 1]
        ok = state['setters'] == port[0] and native_finder == port[1] and state['taken'] == port[2]
        seen['found'] += bool(port[1])
        seen['idle'] += port[0] == [0]
        seen['walk'] += port[0] == [1]
        seen['performing'] += port[0] == [0x17]
        seen['gate'] += bool(state['gate'])
        seen['spawn'] += any(name == 'spawn' for name, *_ in state['calls'])
        if not ok:
            bad['search'] += 1
            print(f'search(turn {turn}, gate {state["gate"]}, tired {state["tired"]}, draws {state["draws"]}, found {state["found"]}, patrol {state["patrol"]}): native {state["setters"]}, finder {native_finder}, taken {state["taken"]}, port {port}')
        elif golden and index < 6:
            lines.append(f'search turn {turn} gate {state["gate"]} tired {state["tired"]} draws {state["draws"][:port[2]]} found {state["found"]} patrol {state["patrol"]} exp {experience}/{max_experience} -> setters {state["setters"]} finder {native_finder}')
        # ---- the entertainer's setter
        sandbox.reset()
        turn = rng.randrange(0, 1 << 20)
        count = rng.choice([1, 2, 3, 4, 5, rng.randrange(1, 50)])
        theme = rng.choice([0, 1, 2, 3, 4])
        variant = rng.choice([0, 1, 2, 3])
        target = rng.choice([0, 1, 0x17, 0x17, 5])
        record_object = sandbox.object(0x100, {0x64: ('I', theme)})
        career_inner = sandbox.object(0x100, {0x64: ('I', 0x55)})
        career = sandbox.object(0x100, {0x4C: ('I', career_inner)})
        thing = sandbox.object(0x600, {0x30: ('I', variant), 0x34: ('I', 8), 0x1B4: ('I', 3), 0x5D4: ('I', 0)})
        prepare_world(turn)
        draws = [rng.randrange(0, 1 << 32)]
        state.update(count=count, career=career, theme_record=record_object, draws=draws, taken=0, setters=[], templates=[], calls=[])
        sandbox.call(0x5D1D30, args=(target,), this=thing)
        remainder = draws[0] % count
        if target == 0x17:
            port = (template_of(remainder, theme, variant), 0x17, turn)
            native = (state['templates'][0] if state['templates'] else None, sandbox.read(thing + 0x1B4, 'I'), sandbox.read(thing + 0x5D4, 'I'))
            ok = native == port and state['taken'] == 1 and not state['setters']
        else:
            native = (state['setters'], sandbox.read(thing + 0x1B4, 'I'))
            port = ([target], 3)
            ok = native == port and state['taken'] == 1 and not state['templates']
        if not ok:
            bad['setter'] += 1
            print(f'setter(target {target:#x}, count {count}, theme {theme}, variant {variant}, draw {draws[0]}): native {native}, port {port}')
        elif golden and index < 8:
            lines.append(f'setter target {target:#x} count {count} theme {theme} variant {variant} draw {draws[0]} -> {native}')
        # ---- the constructors: the world draws they take
        for role, entry in (('entertainer', 0x5D1380), ('guard', 0x5D3140)):
            sandbox.reset()
            turn = rng.randrange(0, 1000)
            prepare_world(turn)
            world_object = sandbox.read(WORLD_POINTER, 'I')
            sandbox.write(0xAA1484, 'I', world_object)
            count = rng.choice([1, 2, 3])
            sandbox.write(0x9D7008 + 12 * 0xB, 'I', count)
            sandbox.write(MAX_EXPERIENCE, 'i', 100000)
            for number in REST_RANGE.values():
                sandbox.write(number[0], 'i', 2)
                sandbox.write(number[1], 'i', 5)
            cells = sandbox.object(GRID + 0x28000 + 0x200)
            sandbox.write(CELLS_POINTER, 'I', cells)
            thing = sandbox.object(0x700, {0x5: ('B', 3), 0x7: ('B', 5), 0x210: ('i', 0)})
            state.update(draws=[rng.randrange(0, 1 << 32) for _ in range(6)], taken=0, calls=[], gate=1, patrol=0, setters=[], tired=0, found=0)
            sandbox.call(entry, args=(0, 0, 0, 0, 0, 0), this=thing)
            if role == 'guard':
                variant = sandbox.read(thing + 0x30, 'I')
                expected_draws = 1
                ok = state['taken'] == expected_draws and variant == (state['draws'][0] >> 2) % count and sandbox.read(thing + 0x34, 'I') == 0xB
            else:
                ok = state['taken'] == 0 and sandbox.read(thing + 0x34, 'I') == 8
            if not ok:
                bad['ctor'] += 1
                print(f'{role} constructor: draws {state["taken"]}, variant {sandbox.read(thing + 0x30, "I")}')
        # ---- the guard's decision
        sandbox.reset()
        turn = rng.randrange(0, 1000)
        thing = sandbox.object(0x600, {0x1B4: ('I', rng.choice([0, 1]))})
        prepare_world(turn)
        state.update(gate=rng.choice([0, 0, 1]), patrol=rng.choice([0, 1]), setters=[], calls=[])
        sandbox.call(0x5D3C10, this=thing)
        port_states = [] if state['gate'] else [1 if turn & 3 != 0 and state['patrol'] else 0]
        if state['setters'] != port_states:
            bad['guard'] += 1
            print(f'guard(turn {turn}, gate {state["gate"]}, patrol {state["patrol"]}): native {state["setters"]}, port {port_states}')
    if golden:
        print('\n'.join(lines))
        return 0
    print(f'{cases} cases: mismatches {bad}; coverage {seen}')
    return 1 if any(bad.values()) else 0


if __name__ == '__main__':
    arguments = [argument for argument in sys.argv[1:] if not argument.startswith('--')]
    sys.exit(main(int(arguments[0]) if arguments else 2000, '--golden' in sys.argv))

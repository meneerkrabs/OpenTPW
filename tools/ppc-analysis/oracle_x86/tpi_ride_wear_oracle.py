"""Theme Park Inc's ride wear and the mechanic's repair timer run in the x86 oracle sandbox and compared with the C# port's reading (ParkEconomy.ThemeParkIncRides, ThemeParkIncStaffWorld).

Dev-only tooling like the rest of oracle_x86: Unicorn and the identified Theme Park Inc no-CD Game.exe (OPENTPW_TPI_EXE, SHA-256 pinned) are needed, nothing here is shipped.
Three routines run natively with their real _ftol (0x86FAB0):
- the wear amount 0x5E21A0 (thiscall on a ride thing, four stack arguments: the speed, variables 2 and 3 as singles and the rider flag 0); the script reads 0x40AE89/0x4078BA and the type
  lookup 0x40C1D0 are stubbed (the lookup answers a synthetic type record: MaxCapacity +0x1E4, MaxSpeed +0x1F4, the level's RedLineCapacity +0x290, RedLineSpeed +0x2A0 and WearRate +0x2A4);
- the wear tick 0x5E25B0 (thiscall on the ride thing): the same stubs, the script write 0x4028EC recorded, the world pointer 0xADE6B0 with its turn +0x1EAB40, and the advice chain
  (0x4080E9, 0x40A592, 0x4077F2, 0x40A600) stubbed and recorded; the ride's repair (+0x54) and life (+0x58) singles, VAR_WORN and the advice are compared;
- the mechanic's setter 0x5DA150(0x16) (thiscall on a mechanic thing): the sprite call 0x406A37 is stubbed, the work duration 0x62E4C0 and the state tests 0x5E3B90 run for real over the thing
  table 0xAA18B0; the job timer +0x5D4 is compared.
The routines run at the x87 control word 0x7F (24-bit significand), the game thread's after the Direct3D device started ([EXT:hardware-d3d-path]); the 53-bit word 0x27F is reported beside it.
`--golden` prints native cases for the C# tests instead of the mismatch count.

Usage: python3 -I tools/ppc-analysis/oracle_x86/tpi_ride_wear_oracle.py [cases] [--golden]
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

AMOUNT, TICK, SETTER, DURATION = 0x5E21A0, 0x5E25B0, 0x5DA150, 0x62E4C0
HANDLE, READ, WRITE, LOOKUP, SPEED = 0x40AE89, 0x4078BA, 0x4028EC, 0x40C1D0, 0x405524
ADVICE = (0x4080E9, 0x40A592, 0x4077F2, 0x40A600)
SPRITE, AMOUNT_THUNK, DURATION_THUNK, UPGRADING_THUNK, TYPE_THUNK = 0x406A37, 0x406113, 0x404511, 0x40154B, 0x4022F2
FTOL = (0x86FAB0, 0x86FAD7)
WORLD_POINTER, TURN, THINGS = 0xADE6B0, 0x1EAB40, 0xAA18B0
MAX_EXPERIENCE, MOTIVATION_FACTOR, MECHANIC_PAIR = 0xA02260, 0xA02290, (0xA02334, 0xA02338)
MASK = 0xFFFFFFFF


def single(value: float) -> float:
    return struct.unpack('<f', struct.pack('<f', value))[0]


def bits(value: float) -> int:
    return struct.unpack('<I', struct.pack('<f', value))[0]


def gauge_byte(value: float) -> int:
    return int(value) & 0xFF


def op(value: float, precision: int) -> float:
    """One x87 step's rounding at the control word's precision (24 bits: a single; 53 bits: a double)."""
    return single(value) if precision == 24 else value


def amount(speed: int, riders: int, max_capacity: int, max_speed: int, red_capacity: int, red_speed: int, wear_rate: int, precision: int) -> float:
    """ParkEconomy.ThemeParkIncWearAmount at the precision: the terms stored as singles, the sum and products rounded per step."""
    riders = 0 if riders < 0 else max_capacity if riders > max_capacity else riders
    s = single(op(op(float(speed) / max_speed, precision) * single(0.9), precision))
    s = single(op(op(s + single(1.1), precision) * 0.5, precision)) if red_speed <= speed else single(op(s + single(0.1), precision))
    c = single(op(op(riders / max_capacity, precision) * single(0.9), precision))
    c = single(op(op(c + single(1.1), precision) * 0.5, precision)) if red_capacity <= op(riders * single(0.1), precision) else single(op(c + single(0.1), precision))
    return single(op(op(op(c + s, precision) * wear_rate, precision) * 0.5, precision))


def clamp(total: float) -> float:
    return 100.0 if total > 100.0 else 0.0 if not total >= 0.0 else single(total)


def tick(repair: float, life: float, wear: float, special: bool, warn: float, imminent: float, precision: int) -> tuple:
    """ParkEconomy.WearThemeParkInc after the gates: (repair, life, VAR_WORN written, advice posted)."""
    repair = clamp(op(-wear + repair, precision))
    if not special:
        life = clamp(op(-op(single(0.02) * wear, precision) + life, precision))
    byte = gauge_byte(repair)
    worn = byte < warn
    advice = byte < imminent and not (op(byte + wear, precision) < imminent)
    return repair, life, worn, advice


def timer(repair: float, duration: int) -> int:
    product = ((100 - gauge_byte(repair)) * duration) & MASK
    product = product - (1 << 32) if product >> 31 else product
    q = abs(product) // 100
    return (q if product >= 0 else -q) & MASK


def signed(value: int) -> int:
    value &= MASK
    return value - (1 << 32) if value >> 31 else value


def div0(a: int, b: int) -> int:
    """C's signed division toward zero (idiv)."""
    q = abs(a) // abs(b)
    return q if (a >= 0) == (b >= 0) else -q


def duration(experience: int, motivation: float, pay: int, low: int, high: int, max_experience: int, factor: int) -> int:
    """ThemeParkIncStaffSettings.WorkDuration for the mechanic (the inverse form; tpi_staff_formula_oracle.py checks it on its own)."""
    e = signed(experience - (100 - (int(motivation) & 0xFF)) * factor)
    if e <= 0:
        e = 1
    if (pay & MASK) < 10:
        return 200
    spread = low - high
    if e > max_experience:
        return low - spread
    return low - div0(signed(div0(signed(e << 14), max_experience) * spread), 16384)


def st0(sandbox: Sandbox, result) -> float:
    """The top of the x87 stack after a routine that returns its answer in st0, as a single (coaster_x87_oracle.py's reading)."""
    top = (sandbox.x87_state()['fpsw'] >> 11) & 7
    mantissa, exponent = result.fpu[top]
    sign = -1.0 if exponent & 0x8000 else 1.0
    if mantissa == 0:
        return 0.0 * sign
    return single(sign * math.ldexp(mantissa, (exponent & 0x7FFF) - 16383 - 63))


def main(cases: int, golden: bool) -> int:
    path = executable_path('tpi')
    if path is None:
        raise SystemExit('needs OPENTPW_TPI_EXE (tools/ppc-analysis/oracle_x86/README.md)')
    image = Image.load(path)
    state: dict = {}

    def read(sb: Sandbox):
        return state['vars'].get(sb.arg(1), 0), 0

    def write(sb: Sandbox):
        state['writes'].append((sb.arg(1), sb.arg(2)))
        return 0, 0

    def advice(sb: Sandbox):
        state['advice'] = True
        return state['scratch'], 0x18

    # the advice chain: 0x6375C0 (thiscall, one argument) answers a word pointer, 0x71A930 (thiscall, six) the message, 0x64EA00 (thiscall, none) the queue, 0x718820 (thiscall, one) posts
    stubs = {HANDLE: lambda sb: (0x1234, 0), READ: read, WRITE: write, LOOKUP: lambda sb: (state['type'], 4), SPEED: lambda sb: (state['speed'], 0),
             SPRITE: lambda sb: (0, 4), ADVICE[0]: lambda sb: (state['scratch'], 4), ADVICE[1]: advice, ADVICE[2]: lambda sb: (state['scratch'], 0), ADVICE[3]: lambda sb: (0, 4)}
    sandbox = Sandbox(image, [(AMOUNT, 0x5E2385), (TICK, 0x5E27FC), (SETTER, 0x5DA22F), (DURATION, 0x62E640), (0x5E3B90, 0x5E3BA0), FTOL,
                              (AMOUNT_THUNK, AMOUNT_THUNK + 5), (DURATION_THUNK, DURATION_THUNK + 5), (UPGRADING_THUNK, UPGRADING_THUNK + 5)], stubs=stubs)
    rng = random.Random(64)
    bad = {24: 0, 53: 0}
    seen = {'worn': 0, 'advice': 0, 'red': 0}
    lines = []
    tick_kinds: set = set()
    for index in range(cases):
        for precision, control in ((24, 0x7F), (53, 0x27F)):
            sandbox.reset()
            state.update(writes=[], advice=False, scratch=sandbox.alloc(0x100))
            max_capacity = rng.choice([1, 2, 5, 8, 12, 24, rng.randrange(1, 40)])
            max_speed = rng.choice([100, rng.randrange(1, 200)])
            red_capacity = rng.choice([0, 1, 2, rng.randrange(0, 10)])
            red_speed = rng.choice([60, rng.randrange(0, 150)])
            wear_rate = rng.choice([1, 2, 4, 6, 9, rng.randrange(1, 12)])
            level = rng.randrange(3)
            special = rng.random() < 0.2
            info = sandbox.object(0x400, {0x1E4: ('i', max_capacity), 0x1F4: ('i', max_speed), 0x5C: ('i', 1 if special else 0),
                                          0x290 + 72 * level: ('i', red_capacity), 0x2A0 + 72 * level: ('i', red_speed), 0x2A4 + 72 * level: ('i', wear_rate),
                                          0x3C8: ('f', 10.0), 0x3CC: ('f', 5.0)})
            state['type'] = info
            riders = rng.choice([0, 1, max_capacity, max_capacity + 3, rng.randrange(-2, max_capacity + 5)])
            speed = rng.choice([0, red_speed, red_speed - 1, max_speed, rng.randrange(0, max_speed + 50)])
            state['speed'] = speed
            state['vars'] = {5: riders & MASK, 9: rng.choice([0, 1]), 2: 0, 3: 0}
            repair = single(rng.choice([100.0, 10.4, 9.99, 5.2, 0.3, rng.uniform(0, 100)]))
            life = single(rng.choice([100.0, 40.0, 0.01, rng.uniform(0, 100)]))
            ride = sandbox.object(0x140, {0x16: ('H', 7), 0x2C: ('I', 0x99), 0x64: ('B', level), 0x54: ('f', repair), 0x58: ('f', life)})
            sandbox.set_fpcw(control)
            result = sandbox.call(AMOUNT, args=(bits(float(speed)), 0, 0, 0), this=ride)
            native = st0(sandbox, result)
            port = amount(speed, riders, max_capacity, max_speed, red_capacity, red_speed, wear_rate, precision)
            if bits(native) != bits(port):
                bad[precision] += 1
                if precision == 24:
                    print(f'amount(speed {speed}, riders {riders}, caps {max_capacity}/{max_speed}, red {red_capacity}/{red_speed}, rate {wear_rate}): native {native!r}, port {port!r}')
            elif golden and precision == 24 and index < 10:
                lines.append(f'amount speed {speed} riders {riders} maxcap {max_capacity} maxspeed {max_speed} redcap {red_capacity} redspeed {red_speed} rate {wear_rate} -> {native!r} ({bits(native):#010x})')
            # the whole tick, on a turn that is a multiple of 64
            sandbox.reset()
            state.update(writes=[], advice=False, scratch=sandbox.alloc(0x100))
            info = sandbox.object(0x400, {0x1E4: ('i', max_capacity), 0x1F4: ('i', max_speed), 0x5C: ('i', 1 if special else 0),
                                          0x290 + 72 * level: ('i', red_capacity), 0x2A0 + 72 * level: ('i', red_speed), 0x2A4 + 72 * level: ('i', wear_rate),
                                          0x3C8: ('f', 10.0), 0x3CC: ('f', 5.0)})
            state['type'] = info
            ride = sandbox.object(0x140, {0x16: ('H', 7), 0x2C: ('I', 0x99), 0x64: ('B', level), 0x54: ('f', repair), 0x58: ('f', life), 0x43: ('B', 0)})
            world = sandbox.object(0x1EAC00, {TURN: ('I', 64 * rng.randrange(1, 1000))})
            sandbox.write(WORLD_POINTER, 'I', world)
            state['vars'][9] = 1
            sandbox.set_fpcw(control)
            sandbox.call(TICK, this=ride)
            native_tick = (sandbox.read(ride + 0x54, 'f'), sandbox.read(ride + 0x58, 'f'), (8, 1) in state['writes'], state['advice'])
            if precision == 24:
                seen['worn'] += native_tick[2]
                seen['advice'] += native_tick[3]
                seen['red'] += red_speed <= speed
            wear = 0.0 if riders == 0 else port
            port_tick = tick(repair, life, wear, special, 5.0, 10.0, precision)
            if tuple(bits(v) if isinstance(v, float) else v for v in native_tick) != tuple(bits(v) if isinstance(v, float) else v for v in port_tick):
                bad[precision] += 1
                if precision == 24:
                    print(f'tick(repair {repair!r}, life {life!r}, wear {wear!r}, special {special}): native {native_tick}, port {port_tick}')
            elif golden and precision == 24:
                # the first ten ticks, then the first of each kind the tests pin: the repair or the life clamped at 0, a byte below WarnPlayer (VAR_WORN) and a special ride
                kinds = [kind for kind, hit in (('repair clamp', native_tick[0] == 0.0 and repair > 0.0), ('life clamp', native_tick[1] == 0.0 and life > 0.0),
                                                ('worn', native_tick[2]), ('special', special and wear > 0.0)) if hit and kind not in tick_kinds]
                if index < 10 or kinds:
                    tick_kinds.update(kinds)
                    lines.append(f'tick speed {speed} riders {riders} maxcap {max_capacity} maxspeed {max_speed} redcap {red_capacity} redspeed {red_speed} rate {wear_rate} '
                                 f'repair {bits(repair):#010x} life {bits(life):#010x} special {special} -> repair {bits(native_tick[0]):#010x} life {bits(native_tick[1]):#010x} '
                                 f'worn {native_tick[2]} advice {native_tick[3]}{" (" + ", ".join(kinds) + ")" if kinds else ""}')
            # the repair timer: the setter 0x5DA150(0x16) on a mechanic whose ride is thing 3
            sandbox.reset()
            state.update(writes=[], advice=False, scratch=sandbox.alloc(0x100))
            max_experience = rng.choice([100000, rng.randrange(1, 200000)])
            factor = rng.choice([25, rng.randrange(0, 100)])
            low, high = rng.choice([(150, 50), (rng.randrange(1, 300), rng.randrange(1, 300))])
            sandbox.write(MAX_EXPERIENCE, 'i', max_experience)
            sandbox.write(MOTIVATION_FACTOR, 'i', factor)
            sandbox.write(MECHANIC_PAIR[0], 'i', low)
            sandbox.write(MECHANIC_PAIR[1], 'i', high)
            ride = sandbox.object(0x140, {0x54: ('f', repair), 0xA0: ('i', rng.choice([0, 1]))})
            sandbox.write(THINGS + 20 * 3, 'I', ride)
            experience = rng.choice([0, max_experience - 1, rng.randrange(0, max_experience)])
            motivation = single(rng.choice([100.0, 50.5, rng.uniform(0, 100)]))
            pay = rng.choice([9, 10, 120, rng.randrange(0, 400)])
            mechanic = sandbox.object(0x600, {2: ('B', 4), 0x210: ('i', experience), 0x214: ('i', pay), 0x234: ('f', motivation), 0x5D8: ('H', 3)})
            sandbox.set_fpcw(control)
            sandbox.call(SETTER, args=(0x16,), this=mechanic)
            native_timer = (sandbox.read(mechanic + 0x5D4, 'I'), sandbox.read(mechanic + 0x1B4, 'i'))
            port_timer = (timer(repair, duration(experience, motivation, pay, low, high, max_experience, factor)), 0x16)
            if native_timer != port_timer:
                bad[precision] += 1
                if precision == 24:
                    print(f'timer(repair {repair!r}, exp {experience}, motivation {motivation!r}, pay {pay}, pair {(low, high)}): native {native_timer}, port {port_timer}')
            elif golden and precision == 24 and index < 10:
                lines.append(f'timer repair {repair!r} max {max_experience} factor {factor} pair {(low, high)} exp {experience} motivation {motivation!r} pay {pay} -> {native_timer[0]}')
    for line in lines:
        print(line)
    print(f'{cases} wear-amount, wear-tick and repair-timer cases ({seen["worn"]} worn, {seen["advice"]} advice, {seen["red"]} at the red line): {bad[24]} mismatches at 24 bits, {bad[53]} at 53 bits')
    return 1 if bad[24] else 0


if __name__ == '__main__':
    arguments = [a for a in sys.argv[1:] if not a.startswith('--')]
    raise SystemExit(main(int(arguments[0]) if arguments else 2000, '--golden' in sys.argv))

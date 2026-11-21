"""Theme Park Inc's staff candidate constructor 0x6302C0 run in the x86 oracle sandbox and compared with the C# port's reading (ThemeParkIncStaffCandidates.Generate).

Dev-only tooling like the rest of oracle_x86: Unicorn and the identified Theme Park Inc no-CD Game.exe (OPENTPW_TPI_EXE, SHA-256 pinned) are needed, nothing here is shipped.
The constructor (thiscall on a 0x20-byte candidate record; arguments role and pool slot) runs natively with the real expected wage 0x62CD30 and name draw 0x6300F0; the
oracle writes the settings words it reads (the agency rows at 0xA0240C, the pool keys, MaxExperience, the six MaxWage words) and stubs its outside calls: the career getter
0x40B528 (a record whose rank +0x1060 is given), the C runtime rand() 0x870D38 and the world generator 0x40A92F (both answer scripted words), the sprite draw 0x402680 (one world
word, answered as (word >> 2)), the name text 0x404AD9, the name-taken test 0x4029D2 (scripted answers), the pool notice 0x401992 and the time stamp 0x402BB2.

The port draws the agency from the world generator ([EXT:tpi-staff-crt-rand]); here the native rand() is handed that same agency value, which is below the agency count, so
rand() % count gives it back, and the world words follow in the port's order. `--golden` prints a few cases for the C# tests.

Usage: python3 tools/ppc-analysis/oracle_x86/tpi_staff_candidate_oracle.py [cases] [--golden]
"""
from __future__ import annotations

import random
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))

from x86_harness import Image, Sandbox, executable_path  # noqa: E402

ENTRY, WAGE, NAME = 0x6302C0, 0x62CD30, 0x6300F0
CAREER, RAND, WORLD, SPRITE, NAME_TEXT, NAME_TAKEN, NOTICE, STAMP, ERROR = 0x40B528, 0x870D38, 0x40A92F, 0x402680, 0x404AD9, 0x4029D2, 0x401992, 0x402BB2, 0x401983
AGENCIES, MAX_EXPERIENCE, AVG_WAGE_DIFF, TIMEOUT = 0xA0240C, 0xA02260, 0xA0222C, 0xA02224
# role -> (AvgExperienceOf key address, MaxWage address); the gardener (5) reads the handyman's average and its own MaxWage
ROLES = {0: (0xA021A4, 0xA02308), 1: (0xA021A8, 0xA0233C), 2: (0xA021AC, 0xA022F4), 3: (0xA021B0, 0xA0231C), 4: (0xA021B4, 0xA02330), 5: (0xA021A4, 0xA022E0)}
MASK = 0xFFFFFFFF


def signed(value: int) -> int:
    value &= MASK
    return value - (1 << 32) if value >> 31 else value


def div0(a: int, b: int) -> int:
    q = abs(a) // abs(b)
    return q if (a >= 0) == (b >= 0) else -q


def wage_of(experience: int, top: int, max_experience: int) -> int:
    if experience > max_experience:
        w = top
    elif experience < 0:
        w = 0
    else:
        w = div0(signed(div0(signed(experience << 14), max_experience) * top), 16384)
    w -= w - div0(w, 20) * 20
    return 10 if w < 10 else w


def port(role: int, words: list[int], agency: int, average: int, top: int, spread: int, max_experience: int, timeout_key: int,
         exp_modifier: int, wage_modifier: int, taken: list[bool]) -> dict:
    """ThemeParkIncStaffCandidates.Generate after the agency draw: the world words in the port's order."""
    words = list(words)
    sprite = words.pop(0)
    experience = signed(average + (words.pop(0) % spread) - div0(spread, 2))
    if exp_modifier != 0 and experience != 0:
        experience = signed(experience + div0(signed(exp_modifier * experience), 100))
    if experience > max_experience:
        experience = max_experience
    wage = wage_of(experience, top, max_experience)
    if wage_modifier != 0 and wage != 0:
        wage = signed(((wage * wage_modifier) & MASK) // 100 + wage)
    draws = 0
    taken = list(taken)
    while True:
        name = words.pop(0) % 35
        draws += 1
        if not (taken.pop(0) if taken else False) or draws >= 15:
            break
    timeout = timeout_key + words.pop(0) % (timeout_key >> 1)
    return {'agency': agency, 'sprite': (sprite >> 2) & 0xFF, 'experience': experience, 'wage': wage, 'name': name, 'timeout': timeout}


def main(cases: int, golden: bool) -> int:
    path = executable_path('tpi')
    if path is None:
        raise SystemExit('needs OPENTPW_TPI_EXE (tools/ppc-analysis/oracle_x86/README.md)')
    state: dict = {}

    def world(sb):
        return state['words'].pop(0), 0

    def sprite(sb):
        return state['words'].pop(0) >> 2, 0

    def taken(sb):
        return (1 if (state['taken'].pop(0) if state['taken'] else False) else 0), 4

    stubs = {CAREER: lambda sb: (state['career'], 0), RAND: lambda sb: (state['rand'], 0), WORLD: world, SPRITE: sprite,
             NAME_TEXT: lambda sb: (0, 8), NAME_TAKEN: taken, NOTICE: lambda sb: (0, 4), STAMP: lambda sb: (0, 0)}
    sandbox = Sandbox(Image.load(path), [(ENTRY, 0x6306B4), (WAGE, 0x62CD80), (NAME, 0x630158), (0x4082A6, 0x4082AB), (0x4080E4, 0x4080E9)], stubs=stubs)
    rng = random.Random(41)
    bad = 0
    lines = []
    for index in range(cases):
        sandbox.reset()
        role = rng.randrange(6)
        rank = rng.randrange(0, 4)
        levels = sorted(rng.randrange(0, 5) for _ in range(4))
        levels[0] = 0
        rows = [(levels[a], rng.choice([0, 10, 60, -20, rng.randrange(-100, 200)]), rng.choice([0, 10, 20, rng.randrange(0, 300)])) for a in range(4)]
        count = 0
        while count < 4 and rows[count][0] <= rank:
            count += 1
        agency = rng.randrange(count)
        for a, (level, exp_mod, wage_mod) in enumerate(rows):
            base = AGENCIES + 0x24 * a
            sandbox.write(base, 'i', level)
            sandbox.write(base + 0x18, 'i', exp_mod)    # InitialExperienceModifier (0xA02424 + 36 a)
            sandbox.write(base + 0x14, 'i', wage_mod)   # InitialWageModifier (0xA02420 + 36 a)
        max_experience = rng.choice([100000, rng.randrange(1000, 200000)])
        spread = rng.choice([15000, rng.randrange(1, 40000)])
        timeout_key = rng.choice([120, rng.randrange(2, 500)])
        average_address, top_address = ROLES[role]
        average = rng.choice([20000, 15000, rng.randrange(0, 120000)])
        top = rng.choice([80, 250, rng.randrange(0, 600)])
        for address, value in ((MAX_EXPERIENCE, max_experience), (AVG_WAGE_DIFF, spread), (TIMEOUT, timeout_key), (average_address, average), (top_address, top)):
            sandbox.write(address, 'i', value)
        career = sandbox.object(0x1100, {0x1060: ('i', rank)})
        words = [rng.getrandbits(31) for _ in range(24)]
        name_taken = [rng.random() < 0.3 for _ in range(20)]
        state.update(career=career, rand=agency, words=list(words), taken=list(name_taken))
        record = sandbox.object(0x20)
        sandbox.call(ENTRY, args=(role, 3), this=record)
        native = {'agency': sandbox.read(record + 0xC, 'i'), 'sprite': sandbox.read(record + 0x10, 'B'), 'experience': sandbox.read(record + 0x1C, 'i'),
                  'wage': sandbox.read(record + 8, 'i'), 'name': sandbox.read(record + 4, 'i'), 'timeout': sandbox.read(record + 0x18, 'i')}
        expected = port(role, words, agency, average, top, spread, max_experience, timeout_key, rows[agency][1], rows[agency][2], name_taken)
        if native != expected:
            bad += 1
            print(f'case {index} role {role}: native {native}, port {expected}')
        elif golden and index < 8:
            lines.append(f'role {role} agency {agency} rows {rows} max {max_experience} spread {spread} timeout {timeout_key} average {average} top {top} words {words[:8]} taken {name_taken[:6]} -> {native}')
    for line in lines:
        print(line)
    print(f'{cases} candidates: {bad} mismatches')
    return 1 if bad else 0


if __name__ == '__main__':
    arguments = [a for a in sys.argv[1:] if not a.startswith('--')]
    raise SystemExit(main(int(arguments[0]) if arguments else 1000, '--golden' in sys.argv))

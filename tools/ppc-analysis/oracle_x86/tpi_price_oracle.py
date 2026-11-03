"""Theme Park Inc's guest price test 0x6120C0 run in the x86 oracle sandbox (ECON-062; docs/reverse/ORACLE.md finding X62).

Dev-only tooling like the rest of oracle_x86: Unicorn and the identified Theme Park Inc no-CD Game.exe (OPENTPW_TPI_EXE, SHA-256 pinned) are needed, nothing here is shipped.
The routine runs with its real getters (price 0x5E6200, goods 0x5E6220, win byte 0x5E74A0) and _ftol (0x86FAB0). Stubbed, each with a synthetic answer: the type lookup 0x4022F2 (a type record with the
kind word +0x600, the base +0x88, the effects +0x200..+0x20C, the ingredient +0x218, the appearance +0x21C and Info.WhichUIType +0x70), the per-type record 0x404D77 (word +0xC), the rating byte 0x403062
(the attraction's rating, 0x5E41D0, asked with 0), the park 0x40144C and the perception statistic 0x40B6E5 (recorded). The threshold PcOfGoodPriceIsCheap sits at 0xA01B18 and the guest's words at +0x1BC, +0x1C0,
+0x1C8, +0x1CC, +0x1D4, +0x2A8; the attraction's statistics block (+0x130) holds the three rings' running values +0x5D8, +0x660 and +0x4C8.

Usage: python3 -I tools/ppc-analysis/oracle_x86/tpi_price_oracle.py [cases]            (prints the mismatches of the C# reading in Python, `reading`)
       python3 -I tools/ppc-analysis/oracle_x86/tpi_price_oracle.py --golden [cases]   (prints source/OpenTPW.Tests/ThemeParkIncPriceGolden.cs; --check FILE compares)
"""
from __future__ import annotations

import random
import struct
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))

from x86_harness import Image, Sandbox, executable_path  # noqa: E402

M = 0xFFFFFFFF
ENTRY = 0x6120C0
GAME_POINTER = 0xAA1484
THRESHOLD = 0xA01B18
FTOL = (0x86FAB0, 0x86FAD7)
GETTERS = [(0x40A150, 0x40A155), (0x5E6200, 0x5E6207), (0x408A67, 0x408A6C), (0x5E6220, 0x5E6227), (0x401933, 0x401938), (0x5E74A0, 0x5E74A7)]
LOOKUP, TYPE_RECORD, RATING, PARK, STATISTIC = 0x4022F2, 0x404D77, 0x403062, 0x40144C, 0x40B6E5
STATE = type('State', (), {})()


def s32(v: int) -> int:
    v &= M
    return v - (1 << 32) if v >> 31 else v


def tdiv(a: int, b: int) -> int:
    q = abs(a) // abs(b)
    return q if (a >= 0) == (b > 0) else -q


def byte(meter: float) -> int:
    return int(struct.unpack('<f', struct.pack('<f', meter))[0]) & 0xFF


def reading(x: dict) -> tuple:
    """The C# port (ThemeParkIncPriceTest) in Python."""
    price = x['price'] & M
    if price == 0:
        return 0, 0, 0, 0, ()
    h = byte(x['happiness'])
    base = tdiv((x['win'] & 0xFF) * x['goods'], 100) if x['kind'] == 2 else x['expected']
    total = s32(tdiv(x['e_happy'] * (100 - h), 100) - tdiv(x['e_vomit'] * byte(x['illness']), 100) + tdiv(x['e_hunger'] * byte(x['hunger']), 100)
                + tdiv(x['e_thirst'] * byte(x['thirst']), 100) + 100)
    w = ((total * base) & M) // 100
    w = (((x['type_word'] + 100) * w) & M) // 100
    w = ((w * (h + 100)) & M) // 100
    if x['rating'] & 0xFF:
        d = abs((x['preferred'] & 0xFF) - (x['rating'] & 0xFF))
        d = min(d, 100)
        w = (((tdiv(50 * (100 - d), 100) + 100) * w) & M) // 100
    stats = []
    if x['kind'] != 0:
        v = (w * 10 + 50 - price * 10) & M
        v = 100 if v >= 100 else v
        if x['kind'] == 2:
            stats.append((10, v))
        else:
            if 1 <= x['ingredient'] <= 4:
                stats.append((x['ingredient'], v))
            if 1 <= x['appearance'] <= 3:
                stats.append((x['appearance'] + 5, v))
    if price > w or (x['money'] & M) < price:
        return 1, 0, 1, int(x['kind'] == 2), tuple(stats)
    cheap = int(((x['cheap'] * w) & M) // 100 > price) if False else int((x['cheap'] * w) // 100 > price)
    return 0, cheap, 0, 0, tuple(stats)


def binary(sb: Sandbox, x: dict) -> tuple:
    st = STATE
    st.log = []
    st.game = sb.object(0x100)
    st.park = sb.object(0x100)
    sb.write(GAME_POINTER, 'I', st.game)
    sb.write(THRESHOLD, 'I', x['cheap'])
    st.info = sb.object(0x700, {0x600: ('i', x['kind']), 0x88: ('i', x['expected']), 0x200: ('i', x['e_thirst']), 0x204: ('i', x['e_hunger']), 0x208: ('i', x['e_vomit']),
                                0x20C: ('i', x['e_happy']), 0x218: ('i', x['ingredient']), 0x21C: ('i', x['appearance']), 0x70: ('i', 2 if x['kind'] == 2 else 0)})
    st.typerec = sb.object(0x40, {0xC: ('i', x['type_word'])})
    st.stats = sb.object(0x1500)
    st.thing = sb.object(0x200, {0x88: ('i', x['goods']), 0x90: ('B', x['win'] & 0xFF), 0x94: ('I', x['price'] & M), 0x130: ('I', st.stats), 0x16: ('H', 5)})
    st.guest = sb.object(0x400, {0x1BC: ('f', x['happiness']), 0x1C0: ('I', x['money'] & M), 0x1C8: ('f', x['thirst']), 0x1CC: ('f', x['hunger']), 0x1D4: ('f', x['illness']),
                                 0x2A8: ('B', x['preferred'] & 0xFF)})
    sb.set_fpcw(0x27F)
    r = sb.call(ENTRY, args=(st.thing,), this=st.guest)
    if r.esp_delta != 0:
        raise AssertionError(f'stack off by {r.esp_delta}')
    rings = tuple(sb.read(st.stats + o, 'I') for o in (0x5D8, 0x660, 0x4C8))
    return (r.eax & 0xFF, rings[0], rings[1], rings[2], tuple(st.log))


def stubs() -> dict:
    st = STATE
    def lookup(sb):
        if sb.reg('ecx') != st.thing: raise AssertionError('0x4022f2 asked for another thing')
        return st.info, 0
    def typerec(sb):
        if sb.reg('ecx') != st.thing: raise AssertionError('0x404d77 asked for another thing')
        return st.typerec, 0
    def rating(sb):
        if sb.reg('ecx') != st.thing or sb.arg(0) != 0: raise AssertionError('0x403062 called unexpectedly')
        return st.x['rating'] & 0xFF, 4
    def park(sb):
        return st.park, 0
    def statistic(sb):
        if sb.reg('ecx') != st.park: raise AssertionError('0x40b6e5 on another park')
        st.log.append((sb.arg(0), sb.arg(1)))
        return 0, 8
    return {LOOKUP: lookup, TYPE_RECORD: typerec, RATING: rating, PARK: park, STATISTIC: statistic}


def inputs(rng: random.Random, n: int) -> list:
    def meter() -> float:
        return rng.choice([0.0, 100.0, 50.0, float(rng.randrange(0, 101)), rng.uniform(0, 100)])
    def make(**kw) -> dict:
        x = {'happiness': meter(), 'thirst': meter(), 'hunger': meter(), 'illness': meter(), 'money': rng.choice([0, 20, 50, 200, 1000, rng.randrange(0, 3000)]),
             'price': rng.choice([0, 1, 5, 10, 20, 25, 50, rng.randrange(0, 400)]), 'goods': rng.choice([0, 10, 50, rng.randrange(0, 300)]),
             'win': rng.choice([0, 30, 70, 100, rng.randrange(0, 256)]), 'kind': rng.choice([0, 0, 1, 1, 2]), 'expected': rng.choice([0, 5, 10, 25, 50, rng.randrange(0, 300)]),
             'e_thirst': rng.choice([0, 20, 50, rng.randrange(-50, 101)]), 'e_hunger': rng.choice([0, 20, 50, rng.randrange(-50, 101)]),
             'e_vomit': rng.choice([0, 10, rng.randrange(-20, 60)]), 'e_happy': rng.choice([0, 10, 30, rng.randrange(-30, 101)]),
             'ingredient': rng.choice([0, 1, 2, 3, 4, 5]), 'appearance': rng.choice([0, 0, 1, 2, 3, 4]), 'type_word': rng.choice([0, 0, 10, 100, -20, rng.randrange(-100, 200)]),
             'rating': rng.choice([0, 0, 25, 60, 100, rng.randrange(0, 256)]), 'preferred': rng.choice([10, 50, 90, rng.randrange(0, 256)]), 'cheap': rng.choice([70, 70, rng.randrange(0, 150)])}
        x.update(kw)
        return x
    edges = [make(price=p, money=m, kind=k, expected=25, e_thirst=40, e_hunger=0, e_vomit=0, e_happy=10, type_word=0, happiness=60.0, thirst=80.0, hunger=10.0, illness=0.0,
                  rating=r, preferred=50, cheap=70) for p in (0, 1, 20, 30, 40) for m in (0, 20, 1000) for k in (0, 1, 2) for r in (0, 60)]
    return edges + [make() for _ in range(n)]


def run(cases: int) -> list:
    path = executable_path('tpi')
    if path is None:
        raise SystemExit('needs OPENTPW_TPI_EXE (tools/ppc-analysis/oracle_x86/README.md)')
    sb = Sandbox(Image.load(path), [(ENTRY, 0x6124A0), FTOL, *GETTERS], stubs=stubs())
    rows = []
    for x in inputs(random.Random(62), cases):
        sb.reset()
        STATE.x = x
        rows.append((x, binary(sb, x)))
    return rows


def expected_of(x: dict) -> tuple:
    r = reading(x)
    rings = (r[1], r[2], r[3])
    return (r[0], *rings, r[4])


def golden(rows) -> str:
    out = ['// Generated by tools/ppc-analysis/oracle_x86/tpi_price_oracle.py from the Theme Park Inc no-CD Game.exe 0x6120C0 in the x86 oracle (finding X62). Do not edit.', 'namespace OpenTPW.Tests;', '',
           'internal static class ThemeParkIncPriceGolden', '{', '\t/// <summary>kind price goods win expected thirst hunger vomit happy typeword rating preferred ingredient appearance money cheap | happiness thirst hunger illness (hex single bits) > verdict cheapRing expensiveRing declinedRing | statistics (category:value)</summary>', '\tpublic static readonly string[] Rows =', '\t[']
    for x, got in rows:
        def fb(v): return f'{struct.unpack("<I", struct.pack("<f", v))[0]:08x}'
        a = ' '.join(str(x[k]) for k in ('kind', 'price', 'goods', 'win', 'expected', 'e_thirst', 'e_hunger', 'e_vomit', 'e_happy', 'type_word', 'rating', 'preferred', 'ingredient', 'appearance', 'money', 'cheap'))
        m = ' '.join(fb(x[k]) for k in ('happiness', 'thirst', 'hunger', 'illness'))
        stats = ','.join(f'{c}:{v}' for c, v in got[4])
        out.append(f'\t\t"{a} | {m} > {got[0]} {got[1]} {got[2]} {got[3]} | {stats}",')
    out += ['\t];', '}', '']
    return '\n'.join(out)


def main(argv: list) -> int:
    args = [a for a in argv if not a.startswith('--')]
    cases = int(args[0]) if args else 400
    rows = run(cases)
    bad = [(x, got) for x, got in rows if (got[0], got[1], got[2], got[3], tuple(got[4])) != expected_of(x)]
    if '--golden' in argv:
        sys.stdout.write(golden(rows[::5]))
        return 0
    print(f'{len(bad)} mismatches in {len(rows)} cases')
    for x, got in bad[:5]:
        print(x, got, expected_of(x))
    return 1 if bad else 0


if __name__ == '__main__':
    raise SystemExit(main(sys.argv[1:]))

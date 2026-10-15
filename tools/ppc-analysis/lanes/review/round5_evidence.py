"""Round-5 review witnesses: UI root-name binder, advisor completion tuple, guest temporary
history, MD2 clip-bind flag clear, Layer I requantizer table, rides controller dispatch and
the economy save locator.

Usage: python3 -I round5_evidence.py /path/to/mac-feral/bin
           [--pc-data /path/to/theme-park-world/Data ...] [--rides-root /path/to/ppc-rides]

Every native check pins an identified Feral Mac container (review_evidence.Binary), decodes
instruction fields at hand-reviewed addresses and compares them with values the reviewer
derived independently. Output is metadata only: addresses, decoded meanings, counts and
conclusions. Nothing from the original programs is executed, emulated, printed or stored.
The optional PC data check reads LoanInfo numbers from the user's install at run time.
The pure models are independently written statements of what the pinned instructions
compute; they are not translations of original code.
"""
from __future__ import annotations

import argparse
import json
import math
import re
import struct
import sys
from fractions import Fraction
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import review_evidence as rv  # noqa: E402
import round3_evidence  # noqa: E402,F401  (registers the engine/ltms identities)
from review_evidence import Binary, ReviewError, decode, require  # noqa: E402

rv.IDENTITIES.setdefault('sound_shared.data', '7132c2f1d772de25b458b9c6e0303e130e6c536d7650a9d2cde5c8cacd6bb94f')
U32 = 0xFFFFFFFF
SHADOW_MEMBER = 'w_small_shadow.md2'


def x31(rs_rt: int, ra: int, rb: int, xo: int, rc: int = 0) -> tuple:
    return ('x31', rs_rt, ra, rb, xo, rc)


def lhz(b: Binary, at: int) -> tuple:
    word = b.word(at)
    require(word >> 26, 40, f'{b.name} lhz at {at:#x}')
    return (word >> 21 & 31, word >> 16 & 31, rv._s16(word & 0xFFFF))


# --- Independent models ----------------------------------------------------------------------

def root_name_key(name: bytes, multiplier: int = 47) -> int:
    """h = 0; per nonzero byte: h = (h XOR sign-extended byte) * multiplier, mod 2^32; signed result."""
    h = 0
    for byte in name:
        if byte == 0:
            break
        h = ((h ^ ((byte - 256 if byte & 0x80 else byte) & U32)) * multiplier) & U32
    return h - (1 << 32) if h & 0x80000000 else h


def trunc_div(value: int, divisor: int) -> int:
    q = abs(value) // abs(divisor)
    return q if (value < 0) == (divisor < 0) else -q


def native_history(score: int, attraction: int, used: list, temporary: list) -> int:
    """Used history is first-match (branch to the join); temporary history falls through."""
    for i, entry in enumerate(used):
        if entry == attraction:
            score = trunc_div(score, 5 - i)
            break
    for i, entry in enumerate(temporary):
        if entry == attraction:
            score = trunc_div(score, 5 - i)
    return score


def first_match_history(score: int, attraction: int, used: list, temporary: list) -> int:
    """The reviewed 00a6393 helper reading: first match in each array."""
    for history in (used, temporary):
        for i, entry in enumerate(history):
            if entry == attraction:
                score = trunc_div(score, 5 - i)
                break
    return score


def mulhw(a: int, b: int) -> int:
    return (a * b) >> 32


def magic_div5(x: int) -> int:
    q = mulhw(0x66666667, x) >> 1
    return q + ((q & U32) >> 31 if q < 0 else 0)


def magic_div3(x: int) -> int:
    q = mulhw(0x55555556, x)
    return q + (1 if q < 0 else 0)


def srawi_addze(x: int, sh: int) -> int:
    """srawi sets CA when a negative value loses one bits; addze adds it (truncation toward zero)."""
    q = x >> sh
    carry = 1 if x < 0 and (x & ((1 << sh) - 1)) else 0
    return q + carry


def f32(value: float) -> float:
    return struct.unpack('>f', struct.pack('>f', value))[0]


def f32_exact(value: Fraction) -> float:
    """Round an exact rational to binary32, nearest-even (via binary64 when exactly representable)."""
    candidate = f32(float(value))
    best = None
    for c in (candidate, math.nextafter(candidate, -math.inf), math.nextafter(candidate, math.inf)):
        c = f32(c)
        err = abs(Fraction(c) - value)
        if best is None or err < best[0] or (err == best[0] and struct.unpack('>I', struct.pack('>f', c))[0] % 2 == 0):
            best = (err, c)
    return best[1]


def fused_single(a: float, b: float, c: float) -> float:
    return f32_exact(Fraction(a) * Fraction(b) + Fraction(c))


def separate_single(a: float, b: float, c: float) -> float:
    return f32_exact(Fraction(f32_exact(Fraction(a) * Fraction(b))) + Fraction(c))


def layer1_factor(allocation: int, scale: int) -> float:
    return f32(math.pow(2, 2 - scale / 3) / ((1 << (allocation + 1)) - 1))


def midpoint_margin_ulps(value: float, stored: float) -> float:
    """Distance of a binary64 value from the nearest binary32 rounding midpoint around stored."""
    _, e = math.frexp(stored)
    half = 2.0 ** (e - 25)
    return min(abs(value - (stored - half)), abs(value - (stored + half))) / 2.0 ** (math.frexp(value)[1] - 53)


def mac_loan_monthly(amount: int, apr: int, months: int) -> int:
    """Round-1 constructor model: u32sat(trunc(amount * pow(1 + APR/100, (months/12)*0.5) / months))."""
    return min(math.trunc(amount * math.pow(1 + apr / 100, (months / 12) * 0.5) / months), U32)


def annuity_monthly(amount: int, apr: int, months: int) -> int:
    if apr <= 0:
        return amount // months
    rate = apr / 1200.0
    return math.floor(amount * rate / (1 - math.pow(1 + rate, -months)))


def locator_accepts(amount: int, months: int, monthly: int) -> bool:
    """ab3c74d IsLoanRecord plausibility bounds for the repayment."""
    return monthly > 0 and monthly * months >= amount - months and monthly * months <= amount * 4


CONTROLLER_BEHAVIOR = {
    ('preserve', 'resolved input'): 'PreserveInput',
    ('preserve', 'consumed and ignored'): 'PreserveIgnored',
    ('result', 'consumed and ignored'): 'ResultIgnored',
    ('host field +40', 'consumed and ignored'): 'ResultIgnored',
    ('result', 'required variable input'): 'RequiredInputResult',
    ('result', 'required variable output'): 'RequiredOutputResult',
    ('result', 'optional variable output'): 'OptionalOutputResult',
    ('original input', 'resolved input multiplied by 30'): 'OriginalInput',
    ('original input', 'resolved input negated'): 'OriginalInput',
    ('preserve', 'resolved input multiplied by 1000'): 'PreserveInput',
}


def expected_controller_table(contracts: dict) -> dict:
    """Reviewer's mapping from the rides lane's native contract classes to dispatch behaviours."""
    table = {}
    for row in contracts['commands']:
        key = (row['family'], row['raw_command'])
        if row['family'] == 'COAST' and row['raw_command'] == contracts['coast_noop_command']:
            require(row['direct_controller_calls'], [], 'COAST no-op has no controller call')
            table[key] = 'NoOperation'
            continue
        table[key] = CONTROLLER_BEHAVIOR[(row['accumulator'], row['parameter'])]
    return table


def parse_dispatch_table(source: str) -> dict:
    table = {}
    for family, commands, behavior in re.findall(r'\(Opcode\.(\w+),\s*([0-9 or]+)\)\s*=>\s*CommandBehavior\.(\w+)', source):
        for command in re.findall(r'\d+', commands):
            key = (family, int(command))
            require(key in table, False, f'duplicate dispatch entry {key}')
            table[key] = behavior
    return table


def read_loan_offers(text: str) -> list:
    fields = {}
    for index, field, value in re.findall(r'LoanInfo\[(\d+)\]\.(\w+)\s+(-?\d+)', text):
        fields.setdefault(int(index), {})[field] = int(value)
    return [(i, f['LoanAmount'], f['APRInPercent'], f['RepaymentPeriodInMonths'])
            for i, f in sorted(fields.items()) if {'LoanAmount', 'APRInPercent', 'RepaymentPeriodInMonths'} <= f.keys()]


# --- Native audits ---------------------------------------------------------------------------

def ui_audit(app: Binary) -> dict:
    app.expect(0x1709b8, 'addi', 6, 0, 0)
    app.expect(0x1709c0, *x31(5, 0, 0, 954))
    app.expect(0x1709c8, *x31(0, 0, 6, 316))
    app.expect(0x1709cc, *x31(6, 4, 0, 235))
    app.expect(0x1709d0, 'lbz', 5, 3, 0)
    app.expect(0x1709d4, *x31(5, 0, 0, 954, 1))
    app.expect(0x1709d8, 'bc', 4, 2, 0x1709c0)
    app.expect(0x17387c, 'addi', 4, 0, 47)
    require(app.call(0x173880), 0x1709b8, 'registration hashes with multiplier 47')
    slot = app.slot(0x13c164, 4)
    require((slot.kind, slot.target), ('section', app.c.code.index), 'shadow constant lives in code')
    base = struct.unpack_from('>I', app.data, app.toc + decode(app.word(0x13c164))[3])[0]
    app.expect(0x13c168, 'lwz', 3, 29, 32)
    app.expect(0x13c16c, 'addi', 4, 4, 81)
    require(app.cstr(base + 81), SHADOW_MEMBER, 'excluded member name')
    require(app.glue(app.call(0x13c170)), 'strcmp', 'case-sensitive comparison')
    app.expect(0x13c178, 'cmpi', 0, 3, 0)
    app.expect(0x13c17c, 'bc', 4, 2, 0x13c198)
    app.expect(0x13c190, 'addi', 3, 0, 0)
    app.expect(0x13c194, 'b', 0x13c21c)
    return {'hash': 'h=(h^sext(byte))*47 from 0', 'b_buy': root_name_key(b'b_buy'),
            'shadow_exclusion': f'strcmp(name, "{SHADOW_MEMBER}") == 0 returns null before registration',
            'case_dependency': 'fires only if the enumerated name is lowercase; WAD members are stored as .MD2'}


def advisor_audit(app: Binary) -> dict:
    app.expect(0x89f4, 'addi', 5, 29, 0)
    require(app.call(0x89fc), 0xba54, 'wrapper receives the chosen variant')
    app.expect(0x8a00, 'cmpi', 0, 3, 0)
    app.expect(0x8a04, 'bc', 12, 2, 0x8afc)
    app.expect(0x8a10, 'addi', 4, 4, 1000)
    app.expect(0x8a3c, 'stw', 29, 3, 228)
    app.expect(0x8a4c, 'stb', 4, 3, 232)
    for at in range(0x89e4, 0x8a3c, 4):
        fields = decode(app.word(at), at)
        op = fields[0]
        if op in ('addi', 'addis', 'lwz', 'lbz', 'mulli'):
            written = fields[1]
        elif op == 'rlwinm':
            written = fields[2]
        elif op == 'x31' and fields[4] in (28, 124, 316, 444, 954, 824):
            written = fields[2]
        elif op == 'x31':
            written = fields[1] if fields[4] in (266, 40, 235, 202) else None
        else:
            written = None
        require(written == 29, False, f'r29 not written at {at:#x}')
    return {'variant': 'nonvolatile r29 passes to wrapper 0xba54 and is stored at 0x8a3c only on nonzero return'}


def guest_audit(app: Binary) -> dict:
    used = [(0xe98cc, 480, 0xe98f8), (0xe9904, 482, 0xe9920), (0xe992c, 484, 0xe9954), (0xe9960, 486, 0xe9978)]
    temp = [(0xe9994, 488, 0xe99bc), (0xe99c8, 490, 0xe99e0), (0xe99ec, 492, 0xe9a10), (0xe9a1c, 494, 0xe9a34)]
    for array, joins in ((used, True), (temp, False)):
        for at, offset, next_block in array:
            require(lhz(app, at), (3, 28, offset), f'history slot +{offset}')
            app.expect(at + 8, *x31(0, 3, 0, 32))
            app.expect(at + 12, 'bc', 4, 2, next_block)
            before_next = decode(app.word(next_block - 4), next_block - 4)
            if joins and offset != 486:
                require(before_next, ('b', 0xe9978), f'used slot +{offset} joins after one match')
            else:
                require(before_next[0] != 'b', True, f'slot +{offset} falls through')
    app.expect(0xe99a4, 'addis', 3, 0, 0x6666)
    app.expect(0xe99a8, 'addi', 0, 3, 0x6667)
    app.expect(0xe99fc, 'addis', 3, 0, 0x5555)
    app.expect(0xe9a00, 'addi', 0, 3, 0x5556)
    app.expect(0xe99d8, *x31(31, 31, 2, 824))
    app.expect(0xe9a2c, *x31(31, 31, 1, 824))
    return {'used': 'first match', 'temporary': 'every matching slot divides cumulatively (5,4,3,2)',
            'counterexample': {'score': 600, 'used': [0, 0, 0, 0], 'temporary': [7, 7, 7, 7],
                               'native': native_history(600, 7, [0] * 4, [7] * 4),
                               'first_match_helper': first_match_history(600, 7, [0] * 4, [7] * 4)}}


def bind_audit(app: Binary) -> dict:
    app.expect(0xa594c, 'lwz', 0, 24, 48)
    app.expect(0xa5950, 'rlwinm', 0, 0, 0, 29, 29, 1)
    app.expect(0xa5954, 'bc', 12, 2, 0xa5960)
    app.expect(0xa5958, 'addis', 26, 0, 0x80)
    app.expect(0xa5960, 'addis', 26, 0, 0xF4)
    app.expect(0xa59e0, *x31(26, 5, 26, 124))
    app.expect(0xa5a38, *x31(0, 0, 5, 28))
    return {'cleared': {'header flag 0x4': '0x00800000', 'otherwise': '0x00F40000'}}


def layer1_audit(sound: Binary) -> dict:
    op, rt, ra, d = decode(sound.word(0x2d2c))
    require((op, rt, ra, d), ('addi', 27, 2, -21128), 'factor table base from TOC')
    base = sound.toc + d
    mismatches, worst = 0, None
    for allocation in range(1, 15):
        for scale in range(63):
            stored = struct.unpack_from('>f', sound.data, base + (allocation + 1) * 256 + scale * 4)[0]
            wide = math.pow(2, 2 - scale / 3) / ((1 << (allocation + 1)) - 1)
            mismatches += f32(wide) != stored
            margin = midpoint_margin_ulps(wide, stored)
            worst = margin if worst is None else min(worst, margin)
    require(mismatches, 0, 'all 882 valid factors equal double-then-float formula')
    column = {struct.unpack_from('>f', sound.data, base + (a + 1) * 256 + 63 * 4)[0] for a in range(1, 15)}
    require(column, {0.0}, 'scalefactor 63 column mutes')
    require(worst > 1 << 16, True, 'host pow differences cannot change any float factor')
    return {'factors': 882, 'sf63': 'zero (mute) in original; OpenTPW rejects', 'min_midpoint_margin_double_ulps': worst}


def pc_loan_audit(data_roots: list) -> dict:
    rows = []
    for root in data_roots:
        for path in sorted(Path(root).glob('levels/**/*.sam')):
            for index, amount, apr, months in read_loan_offers(path.read_text('latin-1')):
                mac, annuity = mac_loan_monthly(amount, apr, months), annuity_monthly(amount, apr, months)
                rows.append({'file': str(path.relative_to(root)), 'offer': index, 'apr': apr, 'months': months,
                             'mac_accepted': locator_accepts(amount, months, mac),
                             'annuity_accepted': locator_accepts(amount, months, annuity),
                             'max_ratio': max(mac, annuity) * months / amount})
    require(bool(rows), True, 'loan offers found')
    require(all(r['mac_accepted'] and r['annuity_accepted'] for r in rows), True, 'locator accepts shipped offers')
    return {'offers': len(rows), 'positive_apr': sum(r['apr'] > 0 for r in rows),
            'max_total_over_amount': max(r['max_ratio'] for r in rows)}


def rides_audit(rides_root: Path) -> dict:
    contracts = json.loads((rides_root / 'tools/ppc-analysis/lanes/rides/controller-contracts.json').read_text())
    source = (rides_root / 'source/OpenTPW/VM/Handlers/Visitors.cs').read_text()
    expected, actual = expected_controller_table(contracts), parse_dispatch_table(source)
    require(len(expected), 39, 'reviewed command roles')
    require(actual, expected, 'dispatch table equals native contract classes')
    return {'commands': len(actual), 'equal': True}


def inspect(root: Path, pc_data: list | None = None, rides_root: Path | None = None) -> dict:
    app = Binary(root, 'SimThemePark.data', 'SimThemePark.data', rv.APP_TOC)
    sound = Binary(root, 'sound_shared.data', 'libraries/sound_shared.data', 0x8000)
    result = {'ui': ui_audit(app), 'advisor': advisor_audit(app), 'guests': guest_audit(app),
              'md2_bind': bind_audit(app), 'layer1': layer1_audit(sound),
              'fma_cancellation': {'fused': fused_single(3.0, f32(0.1), f32(-0.3)),
                                   'separate': separate_single(3.0, f32(0.1), f32(-0.3))}}
    if pc_data:
        result['loan_locator'] = pc_loan_audit(pc_data)
    if rides_root:
        result['rides_dispatch'] = rides_audit(rides_root)
    return result


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('bin_root', type=Path)
    parser.add_argument('--pc-data', type=Path, action='append')
    parser.add_argument('--rides-root', type=Path)
    args = parser.parse_args()
    try:
        result = inspect(args.bin_root, args.pc_data, args.rides_root)
    except (OSError, ValueError, KeyError, rv.pef.PEFError, ReviewError) as error:
        parser.exit(1, f'round5 evidence: {error}\n')
    print(json.dumps(result, indent=2, sort_keys=True))


if __name__ == '__main__':
    main()

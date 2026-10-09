"""Identity-pinned static economy witnesses; prints metadata, never instructions.

Run with a local Feral bin directory and optionally --sam for a balance file.
No original program is executed. Formula examples run independently written
Python arithmetic, not an emulator or translated original function.
"""
from __future__ import annotations

import argparse
import hashlib
import json
import math
from pathlib import Path
import re
import struct
import sys

sys.path.insert(0, str(Path(__file__).resolve().parents[2]))
import pef
from timer_evidence import call_target, d_fields, glue_import, require, vector

APP_SHA = '04809cd4ccee5433c7fb0b7c93d32f6a7aa629c1849181c0b7906415e5e295f5'
BULL_SHA = 'b67b56b7b2b75962b8b34559e20fd97b623b2d0f7f08a0035f82072ffbf4ec06'
SPANS = {
    'loan_constructor': (0xcb7a8, 0xcba34, 'ef8885600d588751ccf530f8202cd32a8db5fa13a9c43c753e05dfde9eeda7ef'),
    'loan_month': (0xcc21c, 0xcc4d0, '4976309036d92d5754a38f9fee471ef821dee6e90467fab574e951cddd8f4e28'),
    'loan_payoff': (0xccc78, 0xccdec, 'ef62c0a2af19fd821570c4480a9aa2c3847f59a61903a1b210245324ba83c1a5'),
    'uint_conversion': (0x1c3fbc, 0x1c4010, '1b81ce4f120459932643cbc133150a21a853e4a72d861e9b64872af4d1667ba6'),
    'calendar_constructor': (0xe3c90, 0xe3d30, 'a3af42a6307c264c89946b44398dc2b2e3d26019745e10478693934282679f81'),
    'calendar_get': (0xe4394, 0xe44a8, '7a2dc4d2caa9c7a1f60213a81f9d113cd74ecb1c15b76018b0b49955885c87bc'),
    'calendar_epoch': (0xe4348, 0xe4394, '4bad4f1971647d871c1b674c9649e2874ed00e251ae4e9d720fbe53cb0153d36'),
    'research_tick': (0xf0284, 0xf0304, '9e3818f2982129fdccf00ae1f7cf656f1eb34e921c53d3eff2860f61dc71a92b'),
    'research_add': (0xf0728, 0xf07a0, '750f4a0d535561d9a864cb98b6f3fd4363d4f28ebd9753325d13726a8f3949d0'),
    'research_split': (0xf0df0, 0xf0ef0, '851cb489336d21ddbb69a5d222f595a8eda202c852767aca28bd1be37c79ed69'),
    'research_group': (0xf15b4, 0xf1838, '033eb018402ec422061d84bc73b7f825cbbea7aa02a0ac22166900f0f092f9f1'),
    'staff_train': (0xf3588, 0xf3708, '3a70395c0691fb709fa330afef493facc285774c26ef48b5f594c3beb4908648'),
    'staff_skill': (0xf41dc, 0xf424c, 'd4a27347e184d20e9ffbf96641ff2ddb9319c496f9ea81e9d8d3c5e4104c099f'),
    'staff_wage': (0xf46bc, 0xf477c, 'be6d0e255302581d8c6d22889006451a5a8a410a2f709b2bd3fb419eca37c250'),
    'ride_wear': (0xdec2c, 0xdef2c, '934e47eaa6bc104cd470ee1ad0d0bd1abf1e6fd0f4d3d029c18278ec4d62e982'),
    'ride_repair': (0xdef2c, 0xdf5b4, '44034d73cc7ffb6f5105bb606470900b738d4ae9e4db6055a034747805f82cd1'),
    'guest_charge': (0xeaa80, 0xeaaf8, '1c61bdfc51af65bbc0cb4fa78eea251892e3510ea29d38f34fb559371d446e30'),
    'object_receipt': (0xe1b60, 0xe1e58, '191adc96833930c9d21af0c82c07265ae53ddef2356b2a5734737e27c77711d0'),
    'object_cost': (0xe1e58, 0xe1f60, '3d52900f144f79c74e52e292b63a65cb911f91de6805bfc03ed8a548fd61bacc'),
    'analyser_composite': (0xc7b24, 0xc8664, 'd8f8b9d12a3c5bddb569bc6a84f764b542dcea45fd30c3defde8c41e41984203'),
    'scrap_percentage': (0xe2424, 0xe25e8, '85815c170f7384174fa6443c5744398ee84bdd9b4af96de7dcd76c461b849d43'),
    'scrap_amount': (0xe25e8, 0xe2650, 'a7dfb10295b24b2bc2dbae58393e07d25b91e6e89be86e92f00e6989858987c6'),
    'mechanic_duration': (0xd9c0c, 0xd9e04, 'c5d916eed23f333e81d82354467807f7781762ded1587b75547be3a924c9f070'),
    'mechanic_complete': (0xd9e04, 0xd9ef0, 'e0bfc355af422354589349806196b2afb96068c6170b1966e65178b300398456'),
    'cleaner_complete': (0xd60c8, 0xd6230, '54296576055c8acc284aa666cb1955ac6412bbdf30d909e598d7c4bb41551514'),
}


def load(path: Path, identity: str) -> pef.PEFContainer:
    raw = path.read_bytes()
    require(hashlib.sha256(raw).hexdigest(), identity, str(path))
    return pef.PEFContainer(raw, path.name)


def floating(c: pef.PEFContainer, offset: int, expected: float, double=True):
    result = struct.unpack_from('>d' if double else '>f', c.data_section.data, offset)[0]
    require(result, expected, f'data constant {offset:#x}')
    return result


def xform(c: pef.PEFContainer, offset: int, opcode: int, operation: int, regs: tuple):
    word = pef._u32(c.code.data, offset)
    require((word >> 26, word >> 1 & 1023), (opcode, operation), f'operation at {offset:#x}')
    require((word >> 21 & 31, word >> 16 & 31, word >> 11 & 31), regs, f'operands at {offset:#x}')


def monthly_payment(principal: int, apr: int, months: int) -> int:
    if principal < 0 or apr < 0 or months <= 0:
        raise ValueError('requires nonnegative principal/APR and positive term')
    value = principal * math.pow(1 + apr / 100.0, months / 24.0) / months
    return min(math.trunc(value), 0xffffffff)


def sam_examples(path: Path) -> dict:
    raw = path.read_bytes()
    values = {}
    for line in raw.decode('latin1').splitlines():
        match = re.match(r'\s*(LoanInfo\[\d\]\.(?:LoanAmount|APRInPercent|RepaymentPeriodInMonths))\s+(-?\d+)', line)
        if match:
            values[match[1]] = int(match[2])
    loans = []
    for index in range(8):
        prefix = f'LoanInfo[{index}].'
        keys = ['LoanAmount', 'APRInPercent', 'RepaymentPeriodInMonths']
        if not all(prefix + key in values for key in keys):
            continue
        principal, apr, months = (values[prefix + key] for key in keys)
        payment = monthly_payment(principal, apr, months)
        loans.append({'index': index, 'principal': principal, 'apr_percent': apr,
                      'months': months, 'mac_formula_payment': payment,
                      'total_installments': payment * months})
    return {'file': path.name, 'sha256': hashlib.sha256(raw).hexdigest(), 'loans': loans,
            'limitation': 'PC SAM inputs do not prove PC executable arithmetic.'}


def inspect(root: Path) -> dict:
    c = load(root / 'SimThemePark.data', APP_SHA)
    bull = load(root / 'libraries/bullfrog_shared.data', BULL_SHA)
    spans = {}
    for name, (start, end, identity) in SPANS.items():
        digest = hashlib.sha256(c.code.data[start:end]).hexdigest()
        require(digest, identity, name)
        spans[name] = {'code_start': start, 'code_end_exclusive': end, 'sha256': digest}
    for addr, opcode, operands in [
        (0xcb85c, 36, (0, 25, 24)), (0xcb864, 36, (0, 25, 28)),
        (0xcb86c, 36, (0, 25, 32)), (0xcb8f4, 36, (3, 25, 36)),
        (0xcbbd8, 14, (4, 30, 24)), (0xcbbfc, 14, (4, 30, 28)),
        (0xcbc20, 14, (4, 30, 32)), (0xcbc44, 14, (4, 30, 36)),
        (0xcbc68, 14, (4, 30, 40)), (0xcbc8c, 14, (4, 30, 44)),
        (0xcc2d0, 32, (27, 26, 36)), (0xcc2e4, 36, (0, 24, 12)),
        (0xcc3b8, 36, (0, 26, 40)), (0xcc3bc, 36, (0, 26, 44)),
        (0xf4754, 32, (4, 3, 832)), (0xf475c, 32, (0, 3, 748)),
        (0xf3694, 11, (0, 0, 100)), (0xf36ac, 11, (0, 0, 99)),
        (0xe3ca8, 14, (0, 0, 15000)), (0xe3cc0, 36, (0, 30, 28)),
        (0xe4358, 14, (5, 0, 2000)), (0xe435c, 14, (6, 0, 1)),
        (0xe4364, 14, (7, 0, 1)), (0xe43e8, 14, (6, 0, 4)),
        (0xe4410, 14, (0, 6, -27008)), (0xf02d4, 7, (0, 0, 20)),
        (0xf0750, 32, (31, 5, 1052)), (0xf0748, 7, (0, 0, 12)),
        (0xeaad0, 32, (0, 29, 416)), (0xeaad8, 36, (0, 29, 416)),
        (0xdef88, 52, (0, 31, 64)),
        (0xe2620, 32, (0, 31, 440)), (0xd9ed0, 36, (0, 29, 528)),
    ]:
        require(d_fields(c, addr, opcode), operands, f'field/immediate {addr:#x}')
    for offset, operation, regs in [(0xf4760, 235, (3, 4, 0)),
                                    (0xcccd8, 235, (29, 4, 3)),
                                    (0xf3690, 491, (0, 29, 26))]:
        xform(c, offset, 31, operation, regs)
    # Selected floating operations use XO+register checks; full spans pin context.
    xform(c, 0xcb8c0, 63, 18, (0, 0, 28))
    xform(c, 0xcb8bc, 63, 18, (1, 26, 29))
    xform(c, 0xcb8e8, 63, 18, (1, 0, 26))
    constants = {hex(o): floating(c, o, value) for o, value in
                 [(0x5410, .5), (0x5418, 1.0), (0x5420, 12.0), (0x5428, 100.0), (0x5618, 100.0)]}
    constants['0x5620'] = floating(c, 0x5620, 20.0, False)
    pow_call = glue_import(c, call_target(c, 0xcb8cc), 0x8000)
    require(pow_call['symbol'], 'pow', 'loan exponent function')
    require(call_target(c, 0xcb8ec), 0x1c3fbc, 'unsigned truncation helper')
    require(call_target(c, 0xf02e4), 0xf0728, 'periodic researcher contribution')
    require(call_target(c, 0xf0788), 0xf0df0, 'lab distribution')
    require(call_target(c, 0xeaab8), 0xe1b60, 'guest object receipt')
    require(call_target(c, 0xe1b8c), 0xcbf50, 'receipt bank deposit')
    require(call_target(c, 0xe1ea8), 0xcbfdc, 'goods bank withdrawal')
    require(call_target(c, 0xdc05c), 0xe25e8, 'deleted object scrap amount')
    require(call_target(c, 0xe261c), 0xe2424, 'scrap percentage')
    require(call_target(c, 0xd9eac), 0xdef2c, 'mechanic full repair completion')
    timestamp_set = vector(bull, 'SetTime__11TbTimeStampFiiiiiii')
    timestamp_get = vector(bull, 'GetTime__11TbTimeStampCFPiPiPiPiPiPiPiPi')
    require((timestamp_set['code_offset'], timestamp_get['code_offset']), (0x1bb50, 0x1bbfc), 'date exports')
    set_api = glue_import(bull, call_target(bull, 0x1bb94))
    get_api = glue_import(bull, call_target(bull, 0x1bc70))
    return {'app_sha256': APP_SHA, 'bullfrog_sha256': BULL_SHA, 'spans': spans,
            'constants': constants, 'loan_pow': pow_call,
            'date_exports': [timestamp_set, timestamp_get], 'date_conversion_imports': [set_api, get_api],
            'mac_formula': 'trunc_u32(P * (1 + APR/100) ** (months/24) / months)',
            'payoff_formula': 'monthly_payment * (term - months_repaid)',
            'wage_formula': 'BaseWage[grade] * PayMultiplier[type]',
            'skill_formula': 'trunc_u32(20 * (grade + percentage_through_grade/100))',
            'research_period_turns': 20, 'wear_inner_period_turns': 64,
            'calendar_seconds_formula': 'floor(uint64(turn * funny_seconds_per_real_second) / 4)',
            'calendar_default_multiplier': 15000, 'calendar_epoch': '2000-01-01T00:00:00',
            'scrap_basis_field': 'base catalogue cost at +440; excludes higher-level costs',
            'limitation': 'Static Mac witnesses; no PC fidelity or observed wall-clock cadence established here.'}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('bin_root', type=Path)
    parser.add_argument('--sam', type=Path, action='append', default=[])
    args = parser.parse_args()
    try:
        result = inspect(args.bin_root)
        result['sam_examples'] = [sam_examples(path) for path in args.sam]
    except (OSError, pef.PEFError, ValueError) as error:
        parser.exit(1, f'economy evidence: {error}\n')
    print(json.dumps(result, indent=2, sort_keys=True))


if __name__ == '__main__':
    main()

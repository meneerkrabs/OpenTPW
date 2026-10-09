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
from schema import fields as schema_fields

APP_SHA = '04809cd4ccee5433c7fb0b7c93d32f6a7aa629c1849181c0b7906415e5e295f5'
BULL_SHA = 'b67b56b7b2b75962b8b34559e20fd97b623b2d0f7f08a0035f82072ffbf4ec06'
MACDOZE_SHA = 'ba11331a70bce77140ae6e7fe73145fe4e13cce5f042707a494cb77654b32f0d'
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
    'schema_builder': (0x16f4c, 0x1721c, 'b70e3749d13c115d2359b045caee7683c85758638121f22d49e0e507567d45c3'),
    'sam_int_store': (0x182a0, 0x18378, '74fcc3941ed705125172c8bf965bc9d453ff672f522fd7b197491c876332b3e6'),
    'sam_parse': (0x18c54, 0x197a4, '5142f753af32e28f0522fe459471962e112edc45ca5d30af696ebe709b3dbd68'),
    'balance_getters': (0x19950, 0x19968, '15c63efa1288e1753a8b741f6d2c10dfdd6d8af251e6de43bea6aa335fdad8ae'),
    'catalogue_getters': (0x119fe8, 0x11a000, '42297eefccdc7c5e7588e945f4bf136cb3e5e70eeb3a754ed897743f3a8b411a'),
    'catalogue_loader': (0x119328, 0x119d14, '0624ae33514566e9bdc12f01248ab8fd948807acb51cfad52b732e1f7a36fb4a'),
    'rating_ui': (0x16eeb0, 0x16f780, 'f02ed0adc1c74ed44c6e851535a88af698aae89dbbd7700138e5d578c625ae1d'),
    'calendar_events': (0xe3f0c, 0xe41cc, '61f181fd326964e7a7eb753d2a7faef24364b09c8886b0204abe5986b20d53ea'),
    'calendar_message_types': (0xe47f8, 0xe4810, 'de00cb6476fc7b59ef8af432f3f1748e4291889c2f231ad5d71f0393b19fdeef'),
    'wear_amount': (0xde904, 0xdebf0, 'e0db3b0fa70a893f0d91c1d92431da1761ac59b53b53038f63c1dc43f396350e'),
    'rating_visitors': (0xc3684, 0xc3758, '8f3f3214ea9fed1c702e11d155d207f834a9c47292643d9dd8b6f24cf16a3567'),
    'rating_objects': (0xc5864, 0xc5968, 'b4a9f5584fa7fc8586ccca42e1ca6e25f454ba5e0b8b3be0ff54d128bce078f1'),
    'rating_staff': (0xc4064, 0xc41a4, 'e2acf06807e7ff2a26cea76d998e0286ba0b2540d5518215e051dafaa3da0f91'),
    'world_write': (0x105d3c, 0x106708, 'ae1748ed541c3e22dfbd569030f9f8ae95c637d33b226f5a42dcbb8db9e6f499'),
    'world_read': (0x106708, 0x1077e8, '3ba90e2276fb011bab72c9cbb3d2f34551583e5ee5056b5df84bc2742d0ce0c2'),
    'world_bank_get': (0x108424, 0x108480, 'cd56a954afa38753b2dc3b3c3785d5271c7b6a8a7dee7d43a6f0d66206c05357'),
    'world_component_io': (0xd6710, 0xd67b0, 'c0e108dfbd3729eb5f6fe778a863ff1721e912e7dacbbe6305d7d2ca46f4bfee'),
    'calendar_io': (0xe3d30, 0xe3f0c, '338e566bcdd21963339caf0696de3993a6317b1570420574ffa7bf3eb6d2758b'),
    'calendar_init_reset': (0xe42b0, 0xe4348, '2fcba293f664a8aac9b302580022c7266cbfc849dd7f5799608962a38866698e'),
    'world_component_init': (0xd67b0, 0xd67f0, 'a877d03627af467a64da0ef5619bfef287f10c3b915935ad395cb94a71a3df59'),
    'action_record_io': (0x10bc1c, 0x10bea0, 'b98fd8d7c7a1aafa044e8fd310db612d12becaefe2acd05efae25293d741741a'),
    'bank_io': (0xcba54, 0xcbf48, '33657afe573f6ece267b5680ec857a0dfe1039a0a06a7dde69060df336903580'),
    'thing_base_io': (0xfa808, 0xfa9a4, 'c52f5315e5b3fa8c5e1ac55c67d6db742db295056da44c2173fb8ff73872f6ca'),
    'bank_withdraw': (0xcbfdc, 0xcc084, 'c85be4bd002fa2895bca0c6e26b26678a20ad9a4cee8383f718b593163941105'),
    'arrival_io': (0xcb050, 0xcb274, 'de82716f4b5bb6271def1c49e23087815569c0b7fa2d608c555e9e4fcbb181cb'),
    'arrival_timer_io': (0x121000, 0x121098, 'e86611790e6794db4aad56fea1bfc8b3c4bde7a745cf98240cb2cd4f4f92e68a'),
    'save_restore': (0x11b5ac, 0x11c480, 'ab474f4ea14f1aa0bb78d52bc4b9f1cee1933631c125bd5231683ad2113c3215'),
    'world_init': (0x10474c, 0x104da8, 'f9b72fae9b7729a6d6ad027bfe0eb0a4daab46952c142b99b190c77800856da4'),
    'staff_io': (0xf2c28, 0xf30b0, '7539e24d28468ad2227b0a8f4f22d28c25f50eaf2cf4aec5ba6231e509a9c32e'),
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


def below_cap_branch(c: pef.PEFContainer, offset: int, cap_target: int):
    word = pef._u32(c.code.data, offset)
    require((word >> 26, word >> 21 & 31, word >> 16 & 31, word & 3),
            (16, 4, 0, 0), f'branch on not-less-than at {offset:#x}')
    displacement = word & 0xfffc
    if displacement & 0x8000:
        displacement -= 0x10000
    require(offset + displacement, cap_target, 'rating cap branch target')


def monthly_payment(principal: int, apr: int, months: int) -> int:
    if principal < 0 or apr < 0 or months <= 0:
        raise ValueError('requires nonnegative principal/APR and positive term')
    value = principal * math.pow(1 + apr / 100.0, months / 24.0) / months
    return min(math.trunc(value), 0xffffffff)


def profit_interest(principal: int, payment: int, months: int) -> int:
    """Independent arithmetic example of the witnessed unsigned division."""
    if months <= 0:
        raise ValueError('requires positive term')
    return ((payment * months - principal) & 0xffffffff) // months


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
                      'total_installments': payment * months,
                      'profit_interest_per_month': profit_interest(principal, payment, months)})
    return {'file': path.name, 'sha256': hashlib.sha256(raw).hexdigest(), 'loans': loans,
            'limitation': 'PC SAM inputs do not prove PC executable arithmetic.'}


def language_labels(folder: Path) -> dict:
    """Read only the five labels coupling the annual-summary controls.

    Uses the existing repository BFST/BFMU format interpretation; no assets
    are copied to the repository and neither table's full contents are output.
    """
    table = (folder / 'UITEXT.str').read_bytes()
    mapping = (folder / 'MBToUni.dat').read_bytes()
    table_sha = '3fe8b89c994bdd177b7226a51f24222621cc7e27beee94668942dc1f821137cf'
    map_sha = '69f23492ef61a27ed79dd4df67978536720d403f7e2733acb6b43e6f4f78c587'
    require(hashlib.sha256(table).hexdigest(), table_sha, 'identified English UITEXT')
    require(hashlib.sha256(mapping).hexdigest(), map_sha, 'identified English BFMU')
    count = struct.unpack_from('<H', mapping, 6)[0]
    characters = struct.unpack_from('<' + str(count) + 'H', mapping, 8)
    labels = {}
    for index, expected in [(186, 'End Of Year Summary'), (187, 'Last Year'),
                            (188, 'This Year'), (189, 'Park value'), (190, 'Park rating')]:
        offset = 12 + struct.unpack_from('<I', table, 12 + 4 * index)[0]
        require(table[offset], 1, 'BFST record marker')
        length = int.from_bytes(table[offset + 1:offset + 4], 'little')
        if length > 64 or offset + 4 + length > len(table):
            raise ValueError('label outside bounded record')
        coded = table[offset + 4:offset + 4 + length]
        if any(not 1 <= value <= count for value in coded):
            raise ValueError('label outside character mapping')
        label = ''.join(chr(characters[value - 1]) for value in coded)
        require(label, expected, f'UITEXT {index}')
        labels[str(index)] = label
    return {'uitext_sha256': table_sha, 'bfmu_sha256': map_sha, 'labels': labels,
            'limitation': 'PC language label cross-check; Mac layout and accessor are separately witnessed.'}


def inspect(root: Path) -> dict:
    c = load(root / 'SimThemePark.data', APP_SHA)
    bull = load(root / 'libraries/bullfrog_shared.data', BULL_SHA)
    mac = load(root / 'libraries/macdoze_shared.data', MACDOZE_SHA)
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
        (0x16f0c4, 14, (3, 0, 190)), (0x16f078, 14, (4, 4, -21085)),
        (0x16f328, 14, (4, 4, -21086)), (0x16f3a8, 14, (3, 3, 5040)),
        (0x16f450, 14, (3, 3, 5040)), (0x16f44c, 14, (5, 0, 12)),
        (0xe47f8, 14, (3, 0, 13)), (0xe4800, 14, (3, 0, 12)),
        (0xe4808, 14, (3, 0, 11)), (0x19960, 14, (3, 3, 8)),
        (0x119ff8, 14, (3, 3, 8)), (0x1194a0, 14, (3, 19, 4)),
        (0x16f90, 14, (27, 0, 1)), (0x18354, 36, (30, 29, 0)),
        (0xcc158, 11, (0, 3, 13)), (0xcc1e8, 36, (0, 29, 292)),
        (0xcb9fc, 14, (5, 0, 13)), (0xde940, 14, (4, 0, 5)),
        (0x105eac, 14, (4, 4, -22746)), (0x10683c, 14, (4, 4, -22746)),
        (0x105efc, 14, (4, 4, -22772)), (0x10688c, 14, (4, 4, -22772)),
        (0x10842c, 15, (4, 3, 30)), (0x108440, 40, (0, 4, -22746)),
        (0x108464, 7, (0, 0, 20)), (0x107620, 14, (3, 0, 296)),
        (0xd6758, 14, (3, 27, 672)), (0xd6780, 14, (3, 27, 708)),
        (0xd67d4, 14, (3, 31, 672)), (0x106bf8, 14, (3, 25, 728)),
        (0xe3d98, 14, (4, 29, 8)), (0xe3dbc, 14, (4, 29, 16)),
        (0xe3de0, 14, (4, 29, 20)), (0xe3e04, 14, (4, 29, 28)),
        (0xe3e60, 14, (4, 29, 8)), (0xe3e84, 14, (4, 29, 16)),
        (0xe3ea8, 14, (4, 29, 20)), (0xe3ecc, 14, (4, 29, 28)),
        (0xcbab0, 14, (4, 27, 280)), (0xcbad0, 14, (4, 27, 12)),
        (0xcbaf4, 14, (4, 27, 16)), (0xcbb18, 14, (4, 27, 276)),
        (0xcbb3c, 14, (4, 27, 284)), (0xcbb60, 14, (4, 27, 288)),
        (0xcbb84, 14, (4, 27, 292)), (0xcc044, 36, (0, 30, 284)),
        (0x10bd9c, 14, (4, 28, 8)), (0x10be00, 14, (4, 29, 0)),
        (0xfa84c, 14, (4, 29, 4)), (0xfa870, 14, (4, 29, 6)),
        (0xfa894, 14, (4, 29, 10)), (0xfa8b8, 14, (4, 29, 8)),
        (0xf2c80, 14, (4, 27, 484)), (0xf2ec0, 14, (4, 27, 484)),
        (0xf2da4, 14, (4, 27, 488)), (0xf2fbc, 14, (4, 27, 488)),
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
    require(call_target(c, 0x16f0c8), 0x138504, 'rating label UITEXT accessor')
    require(call_target(c, 0xe3fb0), 0x116488, 'day event constructor')
    require(call_target(c, 0xe4088), 0x1164a4, 'month event constructor')
    require(call_target(c, 0xe4148), 0x1164c0, 'year event constructor')
    require(call_target(c, 0xc17b8), 0xc7b24, 'rating history writer')
    require(call_target(c, 0xcba00), 0x116740, 'bank subscribes to year event')
    require(call_target(c, 0xfac04), 0xcc120, 'actor dispatcher bank handler')
    for at, target in [
        (0x105eb0, 0xbefc), (0x106840, 0xbffc),
        (0x105f00, 0xc90c), (0x106890, 0xc3f8),
        (0x10663c, 0xcba54), (0x10765c, 0xcba54), (0xcba78, 0xfa808),
        (0x1062a4, 0xd6710), (0x106bdc, 0xd6710),
        (0xd6734, 0xf5f1c), (0xd675c, 0xe3d30), (0xd6784, 0xcb050),
        (0x106c04, 0xd6c90), (0xd67d8, 0xe42b0), (0x104920, 0xd67b0),
        (0xe3d80, 0xcacc0), (0xe3da4, 0xcacc0), (0xe3dc8, 0xc7f8),
        (0xe3dec, 0xc7f8), (0xe3e10, 0xc90c),
        (0xe3e48, 0xca6d4), (0xe3e6c, 0xca6d4), (0xe3e90, 0xc2fc),
        (0xe3eb4, 0xc2fc), (0xe3ed8, 0xc3f8),
        (0x11b610, 0x10bc1c), (0x11b680, 0x106708),
        (0x10bda8, 0xc2fc), (0x10bdd4, 0xcd0b4),
        (0xfa858, 0xcde50), (0xfa87c, 0xcde50), (0xfa8a0, 0xbefc), (0xfa8c4, 0xbefc),
        (0xcb0c8, 0x121000), (0xcb1b8, 0x121000),
        (0x12103c, 0xc90c), (0x121070, 0xc3f8),
        (0xf2c4c, 0xe4a54), (0xf2c8c, 0xf530c), (0xf2ecc, 0xf5114),
        (0xf2db0, 0xcabc4), (0xf2fc8, 0xca9bc),
    ]:
        require(call_target(c, at), target, f'owner/serializer call {at:#x}')
    xform(c, 0x108468, 31, 23, (3, 31, 0))
    relocs = c.relocs[c.data_section.index]
    require((relocs[0xa44].target, relocs[0xa44].addend), (1, 0xecef4), 'ThingArray relocation')
    require((relocs[0x449b4].target, relocs[0x449b4].addend), (0, 0x107620), 'type16 bank restore case')
    rating_sources = {}
    # name, comparison, register, limit, branch, cap store, cap register/value,
    # provider call/entry, selector. Caps are direct operands, not SAM data.
    for name, cmp_at, reg, limit, branch, cap_at, cap_reg, cap_value, call, provider, selector in [
        ('visitors', 0xc7bec, 19, 1000, 0xc7bf0, 0xc7c00, 3, 1000, 0xc7bf8, 0xc3684, None),
        ('rides', 0xc7ce8, 0, 20, 0xc7cec, 0xc7d10, 25, 20, 0xc7cfc, 0xc5864, (0, 0)),
        ('shops', 0xc7dc8, 0, 10, 0xc7dcc, 0xc7de8, 20, 10, 0xc7ddc, 0xc5864, (1, 0)),
        ('sideshows', 0xc7ea0, 0, 10, 0xc7ea4, 0xc7ec0, 21, 10, 0xc7eb4, 0xc5864, (2, 0)),
        ('features', 0xc7f74, 19, 10, 0xc7f78, 0xc7f94, 22, 10, 0xc7f88, 0xc5864, (3, 0)),
        ('upgraded_rides', 0xc8054, 19, 10, 0xc8058, 0xc8074, 24, 10, 0xc8068, 0xc5864, (0, 2)),
        ('handymen', 0xc817c, 19, 4, 0xc8180, 0xc8198, 28, 4, 0xc818c, 0xc4064, (5,)),
        ('mechanics', 0xc82a0, 19, 4, 0xc82a4, 0xc82bc, 27, 4, 0xc82b0, 0xc4064, (4,)),
        ('entertainers', 0xc83c4, 19, 4, 0xc83c8, 0xc83e0, 23, 4, 0xc83d4, 0xc4064, (6,)),
        ('guards', 0xc84e8, 19, 4, 0xc84ec, 0xc8504, 19, 4, 0xc84f8, 0xc4064, (7,)),
        ('researchers', 0xc860c, 17, 4, 0xc8610, 0xc8624, 3, 4, 0xc861c, 0xc4064, (8,)),
    ]:
        require(d_fields(c, cmp_at, 11), (0, reg, limit), 'signed rating threshold comparison')
        below_cap_branch(c, branch, cap_at)
        require(d_fields(c, cap_at, 14), (cap_reg, 0, cap_value), 'rating literal cap')
        require(call_target(c, call), provider, 'rating below-cap recount provider')
        if selector:
            at = call - 4 * len(selector)
            for index, value in enumerate(selector):
                require(d_fields(c, at + 4 * index, 14), (4 + index, 0, value), 'rating provider selector')
        rating_sources[name] = {'signed_compare_offset': cmp_at, 'cap_branch_offset': branch,
                                'cap_target': cap_at, 'provider_call': call, 'provider': provider,
                                'selector': selector, 'cap_operand': cap_value}
    require(d_fields(c, 0xc7c04, 7), (0, 3, 20), 'visitor low-word multiplication after population cap')
    require(d_fields(c, 0xc7cdc, 7), (0, 19, 3), 'ride low-word multiplication before /2')
    xform(c, 0xc7ce0, 31, 824, (0, 0, 1))
    xform(c, 0xc7ce4, 31, 202, (0, 0, 0))
    for offset, opcode, operation, registers in [
        (0xf4220, 63, 18, (0, 1, 0)),  # double percentage /100
        (0xf4228, 59, 20, (1, 1, 2)),  # single-rounded grade conversion
        (0xf422c, 63, 21, (0, 1, 0)),  # double addition
        (0xf4230, 63, 12, (0, 0, 0)),  # round combined grade/fraction to single
        (0xf4234, 59, 25, (1, 3, 0)),  # single multiplication by20
    ]:
        xform(c, offset, opcode, operation, registers)
    main_slot = 0x8000 + d_fields(c, 0x19954, 32)[2]
    ride_slot = 0x8000 + d_fields(c, 0x119fec, 32)[2]
    relocs = c.relocs[c.data_section.index]
    require(relocs[main_slot].addend, 0x34d10, 'CMainBalance descriptor relocation')
    require(relocs[ride_slot].addend, 0x39b64, 'CRideBalance descriptor relocation')
    for base, end, digest in [
        (0x34d10, 0x38fa0, '775aaa73be12c71658d7b26147807416cd4415ef54fabf5bfd57c10ba69f70c8'),
        (0x39b64, 0x3ba54, 'eb137c713ebb51bede258b87c9aa759f7a42f25530c8b7fd137474a83ef5ae0f'),
    ]:
        require(hashlib.sha256(c.data_section.data[base:end]).hexdigest(), digest, 'schema region')
    selected = {}
    main_rows = schema_fields(c.data_section.data, 0x34d10)
    ride_rows = schema_fields(c.data_section.data, 0x39b64, embedded_offset=4)
    expected_fields = {
        'LoanInfo.LoanAmount': 416, 'LoanInfo.APRInPercent': 420,
        'LoanInfo.RepaymentPeriodInMonths': 424, 'PerGradeStaffConsts.BaseWage': 748,
        'PerTypeStaffConsts.PayMultiplier': 832, 'ResearcherConstsPerGrade.ResearchAbility': 1052,
        'ResearchTech.PercentageForThisTech': 1276, 'Upgrades.WearRate': 432,
        'Upgrades.ScrapValueYear1': 392, 'Upgrades.ScrapValueYear2': 396,
        'Upgrades.ScrapValueYear3': 400, 'Upgrades.ScrapValueYear4': 404,
        'Upgrades.CostOfUpgrade': 440, 'Upgrades.DurationOfUpgrade': 444,
        'UsageInfo.MaxCapacity': 296, 'UsageInfo.MaxSpeed': 312,
        'Upgrades.RedLineCapacity': 412, 'Upgrades.RedLineSpeed': 428,
    }
    for row in main_rows + ride_rows:
        if row['path'] in expected_fields:
            require(row['runtime_offset'], expected_fields[row['path']], row['path'])
            selected[row['path']] = row
    require(set(selected), set(expected_fields), 'complete selected schema field set')
    timestamp_set = vector(bull, 'SetTime__11TbTimeStampFiiiiiii')
    timestamp_get = vector(bull, 'GetTime__11TbTimeStampCFPiPiPiPiPiPiPiPi')
    require((timestamp_set['code_offset'], timestamp_get['code_offset']), (0x1bb50, 0x1bbfc), 'date exports')
    set_api = glue_import(bull, call_target(bull, 0x1bb94))
    get_api = glue_import(bull, call_target(bull, 0x1bc70))
    mac_get = vector(mac, get_api['symbol'])
    mac_set = vector(mac, set_api['symbol'])
    require((mac_get['code_offset'], mac_set['code_offset']), (0x4180, 0x4270), 'macdoze conversion exports')
    long_get = glue_import(mac, call_target(mac, 0x4210))
    long_set = glue_import(mac, call_target(mac, 0x42c4))
    require((long_get['symbol'], long_set['symbol']), ('LongSecondsToDate', 'LongDateToSeconds'), 'OS calendar conversion imports')
    for address, src, dst in [(0x4218, 58, 0), (0x4224, 60, 2),
                              (0x422c, 70, 4), (0x4234, 62, 6),
                              (0x423c, 64, 8), (0x4244, 66, 10), (0x424c, 68, 12)]:
        require(d_fields(mac, address, 40), (0, 1, src), 'LongDateRec field load')
        store = address + (8 if address == 0x4218 else 4)
        require(d_fields(mac, store, 44), (0, 30, dst), 'SYSTEMTIME field store')
    mac_spans = {}
    for start, end, digest in [
        (0x4180, 0x4270, 'd3620c96a34dc81ce915971ed6948a532531ae6e9f8c181a6b39d684c3feea63'),
        (0x4270, 0x4340, 'ce0f5fd1676febb4525092af0a5b420312428785e4acedd929261db53ab1cf7c'),
    ]:
        require(hashlib.sha256(mac.code.data[start:end]).hexdigest(), digest, 'macdoze calendar region')
        mac_spans[hex(start)] = {'code_end_exclusive': end, 'sha256': digest}
    return {'app_sha256': APP_SHA, 'bullfrog_sha256': BULL_SHA, 'macdoze_sha256': MACDOZE_SHA, 'spans': spans,
            'constants': constants, 'loan_pow': pow_call,
            'date_exports': [timestamp_set, timestamp_get], 'date_conversion_imports': [set_api, get_api],
            'os_date_imports': [long_set, long_get], 'selected_schema_fields': selected,
            'macdoze_calendar_spans': mac_spans,
            'calendar_event_types': {'day': 11, 'month': 12, 'year': 13},
            'rating_current_control': 44450, 'rating_label_control': 44451,
            'rating_label_uitext_index': 190, 'rating_history_offset': 0x213b0,
            'rating_component_sources': rating_sources,
            'world_economy_ownership': {'bank_id_offset': 0x1da726, 'game_tick_offset': 0x1da70c,
                                        'calendar_offset': 672, 'arrival_offset': 708, 'map_offset': 728,
                                        'bank_id_width': 2, 'thing_array_data': 0xecef4,
                                        'thing_array_entry_stride': 20, 'bank_type': 16,
                                        'bank_restore_case': 0x107620, 'calendar_serializer': 0xe3d30,
                                        'calendar_persisted_bytes': 28,
                                        'calendar_year_cache_offset_not_serialized': 24,
                                        'calendar_init_reset': 0xe42b0,
                                        'calendar_post_load_reset': 'not established'},
            'staff_skill_operation_order': 'double percentage/100; f32 grade; double sum; f32 round; f32 multiply by20; u32 saturation',
            'mac_formula': 'trunc_u32(P * (1 + APR/100) ** (months/24) / months)',
            'payoff_formula': 'monthly_payment * (term - months_repaid)',
            'wage_formula': 'BaseWage[grade] * PayMultiplier[type]',
            'skill_formula': 'trunc_u32(20 * (grade + percentage_through_grade/100))',
            'research_period_turns': 20, 'wear_inner_period_turns': 64,
            'wear_runtime_equation': 'D=0.5*(speed_adjustment+load_adjustment)*WearRate; see report for branches',
            'calendar_seconds_formula': 'floor(uint64(turn * funny_seconds_per_real_second) / 4)',
            'calendar_default_multiplier': 15000, 'calendar_epoch': '2000-01-01T00:00:00',
            'scrap_basis_field': 'base catalogue cost at +440; excludes higher-level costs',
            'limitation': 'Static Mac witnesses; no PC fidelity or observed wall-clock cadence established here.'}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('bin_root', type=Path)
    parser.add_argument('--sam', type=Path, action='append', default=[])
    parser.add_argument('--language', type=Path, help='identified English folder for five selected label witnesses')
    args = parser.parse_args()
    try:
        result = inspect(args.bin_root)
        result['sam_examples'] = [sam_examples(path) for path in args.sam]
        if args.language:
            result['annual_summary_labels'] = language_labels(args.language)
    except (OSError, pef.PEFError, ValueError) as error:
        parser.exit(1, f'economy evidence: {error}\n')
    print(json.dumps(result, indent=2, sort_keys=True))


if __name__ == '__main__':
    main()

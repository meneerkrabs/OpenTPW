"""Bounded static guest witnesses; never execute or emit original instructions.

Usage: python3 evidence.py /path/to/mac-feral/bin
Addresses are section-relative, and arithmetic is interpreted offline.
"""
from __future__ import annotations

import argparse
import hashlib
import json
from pathlib import Path
import struct
import sys

sys.path.insert(0, str(Path(__file__).resolve().parents[2]))
import pef
from timer_evidence import load_identified, require, call_target, d_fields, glue_import

BLOCKS = {
    'score': (0xe9164, 0xe9a84, 'ced5934e024658af2fd2dfe3f164e0b4b2df58bd92efc08add5bf2f987b72802'),
    'needs_update': (0xeece4, 0xef240, '1e69e8a1d836b1666b9ec6139aa555c3deb7cf4f7dd1f2f46a8800ea1f2bf4ae'),
    'state_dispatch': (0xef240, 0xef700, '327b61c052e3a8e2ce19feb98975d9faf7296dfa1122d54d5e0082f0896bff97'),
    'navigation_speed': (0xffe38, 0xffeb0, 'ca62577c5c8326c022a2556a2443ddf203ffc881307164a942e6213b5a6c94a3'),
    'cell_record': (0xd7310, 0xd7340, 'ae744f64d3d294b403361b7316ce9c550e60742cda0086a78266e331a3d3e3cf'),
    'standing_queue': (0xed244, 0xed754, 'ab165d0f08b0db025f41b6e3fe024074f8543f9c59c3b61ab6ae522e92f65522'),
    'queue_slot': (0xddcc4, 0xde02c, '887248bd61929abb31c31989b7693603c8c5a5df3759faf55e71d5bc18a9a2cd'),
    'queue_limit': (0xdcb74, 0xdcd34, '43e0b767478495cac9edea4102c5e2c0e07d2ced1e2f38131578c5511782c492'),
    'set_destination': (0xff224, 0xff738, '0bd62157967dab81dbf06c03dd0799fae65c5a3a053f4783e9c16a11781422fc'),
    'guest_constructor': (0xe7644, 0xe7bbc, 'c8c08309bb1faf6cce65942603ebb33983dc4e7f9204985dc28607b9cbc2e9cc'),
    'person_constructor': (0xe4810, 0xe49a8, '6e2fd3e6213aa6082cf2c904277c226a076de478aa7f61880964a338d6de2819'),
    'thing_constructor': (0xfa718, 0xfa7f8, '55378d60d10ce2f0e31826ad9018790cd33ee706a22b535d2dc4b6b9905e6824'),
    'free_slots_initialize': (0x104e84, 0x104f38, 'a56d8d8e72565994ddfdcb329c1b4736bd4f6cbc048e07fc661d41675ac6f55b'),
    'thing_allocate': (0x105238, 0x1052fc, 'cffd10e621c539a4e2c2b206168fcf98e53b2f8a87c357c240ca07f966a3cc18'),
    'park_turn': (0x10536c, 0x10565c, '3650a4c24a53a7ed27b2d586f93baf996e30103508f1f605dca53373f87efab6'),
    'map_constructor': (0x104500, 0x10465c, '42e6fd6f2fede3b6f50e81cc567a297accfc02aa1ec33f7c78512526173ce141'),
    'map_global_bind': (0x104e4c, 0x104e84, '276ae921931c6b3d75c578e703e016d07965fe134a3df503a5a62aced3632957'),
    'cell_constructor': (0xefddc, 0xefe18, 'aa9e9ad0605a5bb5389fe7332dc95a446c969a7da71d6081af5086b25f5bfdf1'),
    'region_apply': (0xd73a0, 0xd74fc, 'a552960bcf19b07a6813698f0ac0bfd5689370b78ba300e70e0d7f77848ea71c'),
    'region_add': (0xd7340, 0xd7370, '73a56c10a149aed09a6e90754eebf977fad18a2c6d5f7e35a185679c6ca7381f'),
    'region_remove': (0xd7370, 0xd73a0, 'afc0d798b70ef0842c1c447dd47123fb269f8afad3985039a666275e768fb74e'),
    'ride_outcome': (0xea318, 0xea5a4, 'bdf19eede0022d441d0615dd7a5bc5dda884b098c3e065e6fe90d8387d659b16'),
    'shop_outcome': (0xeaaf8, 0xeb49c, '911e8e225f6304dfaa68e54c0053e6f4265909a3b967dedf17b4dd1fb15f9aca'),
    'state_entry': (0xef700, 0xefd84, '1a878ae687d7f5ed5d9025988a63ee7b2d41e4bd0b8b1d9fc2829873eb16626a'),
    'sprite_behavior_set': (0xd2290, 0xd2298, '80966d67214da37bdaf414292e997fa52df02f7e2d3f41d6d6717bd109e66dd9'),
    'admit_person': (0xe03d0, 0xe05c8, '708496620a8eb46c922d37ef811e5fdb99119a10251eb89730a14a5348c809e9'),
    'script_admission_changed': (0xe05c8, 0xe0620, '7b79933ea2ce723f6f0983807de49ffe7539877359af7fe3777123b2115dad86'),
    'script_variable_read': (0xb5be0, 0xb5c5c, '55a1e15769c5dda3814d33f9abe06e01312c2538ec3aff22827200f5fe41f1a0'),
    'script_variable_write': (0xb57d4, 0xb584c, '008691d15a895a1f9aa4d8f2f64692563a7f1b104e0797d678c24e1cab9b5897'),
    'waiting_count': (0xc3758, 0xc3868, 'c177f08be8d39603ad2fd70edb83d23019a686fa5fcbe42f5ecc0a02acc2af9f'),
    'hungry_count': (0xc3964, 0xc3a60, '8892d9982a0c122790f0ff6112bb805130ddfb3cd8d23749075e232301cbbffb'),
    'thirsty_count': (0xc3a60, 0xc3b5c, '5b385ad60e8f18741de3f7261b068c2939c8d16079ef1324fac5f0af25fa20f1'),
    'need_statistics_cell_predicate': (0xe6c6c, 0xe6d20, '6f71cf95d3ff846a3c56c1bfd94479951e9f1ef3eae26971b4861c76e3d7819c'),
}
DISPATCH = (0xef288, 0xef2c8, 0xef36c, 0xef420, 0xef378, 0xef384,
            0xef438, 0xef444, 0xef6c8, 0xef42c, 0xef49c, 0xef4a8,
            0xef4b4, 0xef4ec, 0xef4f8, 0xef57c, 0xef608, 0xef624,
            0xef588, 0xef594, 0xef5a0, 0xef5fc)


def distance_quotient(squared_cells):
    """Interpret the signed multiply-high/add/shift sequence at 0xe9330.

    This derives the divisor without a statistical fit to gameplay rates.
    The input domain is two squared displacements in the 128-cell map grid.
    """
    scaled = squared_cells * 100
    multiplier = ((-28253 << 16) - 19515)
    shifted = (((multiplier * scaled) >> 32) + scaled) >> 8
    return shifted + (1 if shifted < 0 else 0)


def region_delta(coefficient, dx, dy, adding=True):
    """Signed truncation in the region component loop; excludes radius clipping."""
    numerator = coefficient * (1 if adding else -1)
    magnitude = abs(numerator) // (abs(dx) + abs(dy) + 1)
    return magnitude if numerator >= 0 else -magnitude


def ride_illness_increment(excitement, divisor, hunger_byte):
    """Bounded nonnegative inputs from the ride-outcome arithmetic, not a rate."""
    return (excitement // divisor) * ((100 - hunger_byte) // 20)


def needs_phase(counter, thing_id):
    """Return eligible-update and fixed-increment predicates from the nested guards."""
    eligible = (counter & 3) == (thing_id & 3)
    return eligible, eligible and (counter & 15) == 0


def pointer(c, slot, section, address):
    r = c.relocs[c.data_section.index].get(slot)
    if r is None:
        raise pef.PEFError(f'missing relocation at data:{slot:#x}')
    require((r.kind, r.target, r.addend), ('section', section, address),
            f'relocation at data:{slot:#x}')
    return address


def toc_pointer(c, at, target_section, target):
    rd, base, displacement = d_fields(c, at, 32)
    require(base, 2, f'TOC base at code:{at:#x}')
    pointer(c, 0x8000 + displacement, target_section, target)
    return {'instruction': at, 'register': rd, 'toc_slot': 0x8000 + displacement,
            'target_section': target_section, 'target_offset': target}


def inspect(bin_root: Path):
    c = load_identified(bin_root / 'SimThemePark.data')
    require((c.code.index, c.data_section.index), (0, 1), 'section indices')
    require(c.main, (1, 0x8d48), 'application entry vector')
    pointer(c, 0x8d4c, 1, 0x8000)
    blocks = {}
    for name, (start, end, expected) in BLOCKS.items():
        digest = hashlib.sha256(c.code.data[start:end]).hexdigest()
        require(digest, expected, f'{name} bounded code digest')
        blocks[name] = {'code_start': start, 'code_end_exclusive': end,
                        'sha256': digest, 'name_status': 'analyst label, not exported symbol'}
    pointers = [toc_pointer(c, at, section, address) for at, section, address in (
        (0xe916c, 1, 0x41222), (0xe95e0, 1, 0x4129b),
        (0xe9174, 0, 0x1d04c2), (0xe9180, 1, 0x54860),
        (0xeed48, 1, 0x11ef00), (0xef274, 1, 0x41314),
        (0xff22c, 1, 0xeca00), (0x105240, 1, 0xecef0),
        (0x104e84, 1, 0xecef4), (0x105388, 1, 0xeceec),
        (0x10457c, 1, 0x6f50), (0xef730, 1, 0x4136c),
        (0x104e5c, 1, 0x11ef00))]
    pointer(c, 0x6f50, 0, 0xefddc)
    pointer(c, 0x6f54, 1, 0x8000)
    for i, target in enumerate(DISPATCH):
        pointer(c, 0x41314 + i * 4, 0, target)
    for at, op, expected in (
        (0xe94b8, 48, (0, 28, 420)), (0xe9528, 48, (0, 28, 424)),
        (0xe95a0, 48, (1, 28, 428)), (0xe963c, 48, (1, 28, 432)),
        (0xeed78, 42, (4, 3, 0)), (0xeede0, 42, (4, 3, 2)),
        (0xeee48, 42, (3, 3, 4)), (0xef268, 32, (0, 3, 544)),
        (0xed670, 14, (0, 4, 100)), (0xffe64, 36, (0, 3, 24)),
        (0xffe80, 36, (0, 3, 28)), (0xff244, 32, (0, 3, 8)),
        (0xff254, 32, (0, 31, 12)), (0xddd30, 14, (21, 21, -4)),
        (0xdcc58, 48, (31, 3, 436)), (0xe932c, 15, (3, 0, -28253)),
        (0xe9330, 14, (0, 3, -19515)), (0x104edc, 36, (6, 5, 4)),
        (0x104ee0, 14, (6, 6, 1)), (0x1052e0, 32, (0, 3, 4)),
        (0x1052e4, 44, (0, 26, 0)), (0xfa77c, 44, (0, 31, 0)),
        (0x10539c, 14, (0, 3, 1)), (0x1053a0, 36, (0, 4, -22772)),
        (0xe7820, 52, (0, 28, 412)), (0xe785c, 52, (0, 28, 420)),
        (0xe7888, 52, (0, 28, 424)), (0xe78bc, 52, (0, 28, 428)),
        (0x104e58, 14, (0, 3, 728)), (0x104580, 14, (5, 0, 0)),
        (0x104584, 14, (6, 0, 10)), (0x104588, 14, (7, 0, 16384)),
        (0xefde8, 14, (4, 0, 0)), (0xefdf0, 14, (5, 0, 10)),
        (0xd73a4, 7, (7, 5, 12)), (0xd73bc, 34, (4, 4, -28402)),
        (0xd74b0, 42, (22, 22, -28412)), (0xd74cc, 44, (0, 8, 0)),
        (0xd2290, 36, (4, 3, 4)), (0xe05ec, 40, (0, 31, 56)),
        (0xe051c, 44, (0, 29, 104)), (0xb5c1c, 32, (3, 3, 28)),
        (0xb5810, 32, (3, 3, 28)), (0xc3afc, 48, (0, 25, 420)),
        (0xc3a00, 48, (0, 25, 424)), (0xc3804, 32, (0, 3, 544)),
        (0xc3808, 11, (0, 0, 3))):
        require(d_fields(c, at, op), expected, f'field witness at code:{at:#x}')
    calls = {hex(at): call_target(c, at) for at in
             (0xfaa5c, 0xfaa64, 0xeed74, 0xe90a0, 0xe9d2c, 0xef4ac, 0xff27c,
              0xe767c, 0xe483c, 0xfa768, 0x10541c, 0xefdf8,
              0xd735c, 0xd738c, 0x104590, 0xe0530, 0xe0550, 0xe05e8,
              0xef528, 0xc3af0, 0xc3794)}
    require(calls, {'0xfaa5c': 0xeece4, '0xfaa64': 0xef240,
                    '0xeed74': 0xd7310, '0xe90a0': 0xe9164,
                    '0xe9d2c': 0xe9164, '0xef4ac': 0xed244,
                    '0xff27c': 0x101c1c, '0xe767c': 0xe4810,
                    '0xe483c': 0xfa718, '0xfa768': 0x105238,
                    '0x10541c': 0xfa9b0, '0xefdf8': 0x1c483c,
                    '0xd735c': 0xd73a0, '0xd738c': 0xd73a0,
                    '0x104590': 0x1c4a34, '0xe0530': 0xb5be0,
                    '0xe0550': 0xb57d4, '0xe05e8': 0xb5be0,
                    '0xef528': 0xe05c8, '0xc3af0': 0xe6c6c,
                    '0xc3794': 0xd74fc}, 'selected direct call path')
    zero_import = glue_import(c, 0x1c483c, 0x8000)
    require(zero_import['symbol'], 'memset', 'cell zero-initialization import')
    # Tables are reported as semantic values, never as binary data.
    needs_table = list(c.data_section.data[0x41222:0x41222 + 121])
    unary_table = list(c.data_section.data[0x4129b:0x4129b + 21])
    require((needs_table[0], needs_table[60], needs_table[-1]), (0, 17, 100), 'need-match table samples')
    require(unary_table, [0, 0, 0, 0, 0, 0, 0, 0, 0, 1, 2, 5, 10, 20, 30, 40, 50, 60, 75, 90, 100],
            'toilet/illness match table')
    constants = {}
    for off, fmt, expected in ((0x5650, '>d', 0.2), (0x5658, '>d', 0.4),
                               (0x5660, '>d', 65536.0), (0x5668, '>f', 2.0),
                               (0x55c0, '>f', 100.0), (0x55c8, '>f', 0.0),
                               (0x54d8, '>f', 0.25), (0x54dc, '>f', 255.0),
                               (0x54e8, '>f', 4.0), (0x5590, '>f', 50.0)):
        value = struct.unpack_from(fmt, c.data_section.data, off)[0]
        require(value, expected, f'data constant {off:#x}')
        constants[hex(off)] = value
    for squared in range(2 * 127 * 127 + 1):
        require(distance_quotient(squared), squared * 100 // 450,
                'bounded distance division arithmetic')
    return {'binary_sha256': hashlib.sha256(c.raw).hexdigest(), 'toc_offset': 0x8000,
            'blocks': blocks, 'relocated_pointers': pointers, 'calls': calls,
            'state_dispatch': DISPATCH, 'constants': constants,
            'need_match_table_rows': [needs_table[i:i+11] for i in range(0, 121, 11)],
            'toilet_illness_match_table': unary_table,
            'distance_divisor': 450,
            'fresh_slot_id_range': [1, 10239],
            'fixed_needs_phase_condition': 'counter mod16 ==0 AND guest thing ID mod4 ==0',
            'initial_need_remainders': {'thirst': 50, 'hunger': 50, 'toilet': 30},
            'cell_initialization_import': zero_import,
            'region_layout': {'map_offset': 0x1d9104, 'stride': 12,
                              'signed_component_count': 5, 'radius_byte_offset': 10},
            'admission_script_variable_index': 0,
            'resolved_approximation_ids': [],
            'limitation': 'No original execution; no caller cadence or complete SAM/cell field initialization proof.'}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('bin_root', type=Path)
    args = parser.parse_args()
    try:
        result = inspect(args.bin_root)
    except (OSError, pef.PEFError) as error:
        parser.exit(1, f'guest evidence: {error}\n')
    print(json.dumps(result, sort_keys=True, indent=2))


if __name__ == '__main__':
    main()

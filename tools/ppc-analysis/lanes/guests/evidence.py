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
from timer_evidence import load_identified, require, call_target, d_fields

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
        (0x104e84, 1, 0xecef4), (0x105388, 1, 0xeceec))]
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
        (0xe7888, 52, (0, 28, 424)), (0xe78bc, 52, (0, 28, 428))):
        require(d_fields(c, at, op), expected, f'field witness at code:{at:#x}')
    calls = {hex(at): call_target(c, at) for at in
             (0xfaa5c, 0xfaa64, 0xeed74, 0xe90a0, 0xe9d2c, 0xef4ac, 0xff27c,
              0xe767c, 0xe483c, 0xfa768, 0x10541c)}
    require(calls, {'0xfaa5c': 0xeece4, '0xfaa64': 0xef240,
                    '0xeed74': 0xd7310, '0xe90a0': 0xe9164,
                    '0xe9d2c': 0xe9164, '0xef4ac': 0xed244,
                    '0xff27c': 0x101c1c, '0xe767c': 0xe4810,
                    '0xe483c': 0xfa718, '0xfa768': 0x105238,
                    '0x10541c': 0xfa9b0}, 'selected direct call path')
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

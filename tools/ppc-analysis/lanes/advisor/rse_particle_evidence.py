"""Identity-pinned EVENT1/2 particle classification; never execute original code.

Only interpreted addresses/operands are emitted. No binaries or disassembly are
stored. This proves dispatch/classification, not particle rendering or parity.
"""
import argparse
import json
from pathlib import Path
import struct

import evidence as common


def native(root: Path):
    app = common.pef.PEFContainer(common.identified(
        root / 'SimThemePark.data', common.IDENTITIES['SimThemePark.data']))
    table = common.toc_pointer(app, 0xae968)
    common.require(table, 0x3f278, 'EVENT dispatch table')
    for index, destination in ((0, 0xaeee0), (1, 0xae97c), (2, 0xaea58)):
        common.require(common.data_pointer(app, table + index * 4, app.code.index),
                       destination, f'EVENT type {index}')
    # Operand order: type, key, effect; EVENT adds default control 1000.
    for at, fields in ((0xaf9dc, (4, 26, 0)), (0xaf9e0, (5, 27, 0)),
                       (0xaf9d4, (6, 3, 0)), (0xaf9e4, (7, 0, 1000))):
        common.require(common.d_fields(app, at, 14), fields, f'EVENT argument {at:#x}')
    common.require(common.call_target(app, 0xaf9e8), 0xae930, 'EVENT dispatcher')
    common.require(common.call_target(app, 0xafa60), 0xae930, 'EVENT_EXT dispatcher')
    # Both constructors and the tick/shutdown share the particle pool; shutdown
    # points to the existing PTCL diagnostic, independent of proximity/naming.
    pool = common.toc_pointer(app, 0x9fd88)
    common.require(pool, 0x8e060, 'particle pool')
    for at in (0x9f980, 0x9f758, 0x9f35c):
        common.require(common.toc_pointer(app, at), pool, f'particle pool {at:#x}')
    _, _, diagnostic_offset = common.d_fields(app, 0x9f3ac, 32)
    diagnostic = common.data_pointer(app, 0x8000 + diagnostic_offset, app.code.index)
    common.require(common.cstring(app, diagnostic), 'PTCL: Awooga Awooga! Illegal Sprite code',
                   'particle shutdown diagnostic')
    rows = []
    for event_type, key_call, constructor_call, constructor, bound, copy, stride in (
            (1, 0xae990, 0xaea34, 0x9fd70, 0x9fdb0, 0x9fe8c, 0x9fe78),
            (2, 0xaea6c, 0xaeb40, 0x9f968, 0x9f9b4, 0x9fa90, 0x9fa7c)):
        common.require(common.call_target(app, key_call), 0xac7f0, 'model key resolver')
        common.require(common.call_target(app, constructor_call), constructor, 'particle constructor')
        effect_register = 23 if event_type == 1 else 20
        common.require(common.d_fields(app, bound, 11), (0, effect_register, 105), 'template bound')
        common.require(common.d_fields(app, stride, 7), (28, effect_register, 320), 'template stride')
        common.require(common.call_target(app, copy), 0x1c498c, 'template copy')
        common.require(common.d_fields(app, copy - 32, 14), (5, 0, 320), 'template copy length')
        # Constructors use the same template array as well as the same pool.
        common.require(common.toc_pointer(app, constructor + 16), 0x85500, 'particle templates')
        rows.append({'type': event_type, 'constructor': constructor,
                     'key_resolver': 0xac7f0, 'template_count': 105,
                     'template_stride': 320, 'direction_vector': event_type == 2})
    for start, end in ((0x9f968, 0x9fd70), (0x9fd70, 0xa0218)):
        for at in range(start, end, 4):
            word = common.pef._u32(app.code.data, at)
            if word >> 26 == 18 and word & 1:
                if common.call_target(app, at) == 0xbb4fc:
                    raise common.pef.PEFError('particle constructor submits audio')
    # Direction affects three velocity fields via the template coefficient.
    for at, fields in ((0x9fb54, (3, 27, 176)), (0x9fb64, (3, 27, 56)),
                       (0x9fb78, (3, 27, 60)), (0x9fb8c, (3, 27, 64))):
        common.require(common.d_fields(app, at, 32 if at == 0x9fb54 else 36), fields,
                       f'direction/velocity {at:#x}')
    constant = common.d_fields(app, 0xae9f0, 48)
    common.require(constant[:2], (3, 2), 'position scale load')
    scale_slot = 0x8000 + constant[2]
    common.require(struct.unpack_from('>f', app.data_section.data, scale_slot)[0], 1024.0,
                   'position scale')
    for at, fields in ((0x9fac8, (4, 27, 20)), (0x9facc, (3, 27, 24)),
                       (0x9fad0, (0, 27, 28)), (0x9fec4, (4, 27, 20)),
                       (0x9fec8, (3, 27, 24)), (0x9fecc, (0, 27, 28))):
        common.require(common.d_fields(app, at, 36), fields, f'position field {at:#x}')
    for at, fields in ((0x9fab8, (21, 4, 4)), (0x9fabc, (22, 3, 4)),
                       (0x9fac4, (23, 0, 4)), (0x9feb4, (24, 4, 4)),
                       (0x9feb8, (25, 3, 4)), (0x9fec0, (26, 0, 4))):
        common.require(common.x_fields(app, at, 824), fields, f'position shift {at:#x}')
    for call in (0xaea4c, 0xaeb58):
        common.require(common.call_target(app, call), 0x9f5e8, 'nondefault control')
    return {'classification': 'particles', 'routes': rows, 'pool_data': pool,
            'position_scale': 1024, 'default_control': 1000,
            'qualified': 'static dispatch only; no original execution or particle rendering parity'}


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('binary_root', type=Path)
    args = parser.parse_args()
    print(json.dumps(native(args.binary_root), indent=2))

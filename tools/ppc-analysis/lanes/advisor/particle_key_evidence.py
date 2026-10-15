"""Identity-pinned particle key metadata and matrix operand checks; no original execution."""
import argparse
from pathlib import Path
import struct

import evidence as common


def native(root: Path):
    app = common.pef.PEFContainer(common.identified(
        root / 'SimThemePark.data', common.IDENTITIES['SimThemePark.data']))
    fields = (
        (0xac7f8, 11, (0, 6, 0)),       # signed key test, negative fallback
        (0x19b2d4, 32, (0, 8, 4)),     # lookup value
        (0x19b2e0, 32, (0, 8, 0)),     # lookup selector flags
        (0x19ae64, 40, (0, 3, 70)),    # first attributed node (+0x46)
        (0x19ae68, 40, (8, 3, 68)),    # mesh count (+0x44)
        (0x19ae78, 7, (0, 0, 160)),    # mesh stride
        (0x19ae90, 7, (0, 0, 88)),     # dummy stride
        (0xac880, 48, (0, 3, 48)),
        (0xac884, 48, (1, 3, 52)),
        (0xac888, 48, (2, 3, 56)),
        (0xac920, 48, (0, 3, 32)),
        (0xac928, 48, (0, 3, 36)),
        (0xac930, 48, (0, 3, 40)),
    )
    for address, opcode, expected in fields:
        common.require(common.d_fields(app, address, opcode), expected, f'key metadata {address:#x}')
    common.require(common.call_target(app, 0xac840), 0x19b298, 'key lookup')
    common.require(common.call_target(app, 0xac93c), 0x224e4, 'direction normalization helper')
    # Pin interpreted rotate/mask fields, not an embedded original instruction listing.
    for address, bounds in ((0x19ae9c, (26, 27)), (0x19aec8, (21, 21)), (0xac8f0, (27, 27))):
        word = common.pef._u32(app.code.data, address)
        fields = tuple((word >> shift) & mask for shift, mask in
                       ((26, 63), (21, 31), (16, 31), (11, 31), (6, 31), (1, 31), (0, 1)))
        common.require(fields, (21, 0, 0, 0, *bounds, 1), f'key mask {address:#x}')
    constants = {}
    for offset, expected in ((-11572, 0.0), (-11576, 1.0), (-11580, 1024.0), (-11568, 0.5)):
        value = struct.unpack_from('>f', app.data_section.data, 0x8000 + offset)[0]
        common.require(value, expected, f'key constant {offset}')
        constants[str(offset)] = value
    return {'lookup_value_offset': 4, 'lookup_record_bytes': 20,
            'first_node_header_offset': 70, 'matrix_position_offsets': [48, 52, 56],
            'matrix_direction_offsets': [32, 36, 40], 'constants': constants,
            'qualified': 'Static metadata only; native refresh timing and negative-key object fallback unqualified'}


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('binary_root', type=Path)
    print(native(parser.parse_args().binary_root))

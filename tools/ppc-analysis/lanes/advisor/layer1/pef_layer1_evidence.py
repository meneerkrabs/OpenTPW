"""Identify original Layer I allocation, normal frame count and requantizer facts.

Static interpreted operands/math only; never execute original code or emit its
bytes. All 882 valid combined scale/quantizer floats are compared to a formula.
"""
from __future__ import annotations

import argparse
import hashlib
import json
import math
from pathlib import Path
import struct
import sys

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import evidence as common


def inspect(root: Path):
    raw = common.identified(root / 'libraries/sound_shared.data', common.IDENTITIES['sound_shared.data'])
    c = common.pef.PEFContainer(raw)
    decoder = common.vector(c, 'Decode_Layer1__9CMpegBaseFPs')
    common.require(decoder['code_offset'], 0x2d0c, 'Layer I code entry')
    common.require(common.d_fields(c, 0x2de8, 14), (4, 0, 4), 'mono allocation read width')
    common.require(common.call_target(c, 0x2dec), 0x1824, 'mono allocation bit reader')
    common.require(common.d_fields(c, 0x2df8, 11), (0, 18, 32), 'allocation subband count')
    common.require(common.d_fields(c, 0x2e20, 14), (4, 0, 6), 'scalefactor read width')
    common.require(common.d_fields(c, 0x2fe4, 14), (4, 4, 1), 'sample width allocation plus one')
    common.require(common.call_target(c, 0x2fe8), 0x1824, 'Layer I sample bit reader')
    common.require(common.d_fields(c, 0x3114, 11), (0, 25, 12), 'synthesis slot count')
    common.require(common.call_target(c, 0x3108), 0x53a0, 'shared original synthesis')
    common.require(common.d_fields(c, 0x1bc0, 7), (0, 0, 768), 'normal Layer I PCM bytes per channel')
    common.require(common.d_fields(c, 0x3028, 14), (4, 0, -1), 'requantizer bias base')
    common.require(common.x_fields(c, 0x3030, 24), (4, 5, 9), 'negative power-of-two bias')
    common.require(common.d_fields(c, 0x3034, 14), (4, 3, 1), 'code plus one')
    common.require(common.x_fields(c, 0x303c, 266), (4, 5, 4), 'centered integer numerator')
    common.require(common.d_fields(c, 0x2d2c, 14), (27, 2, -21128), 'combined factor table base')
    mpeg1_bitrates = (0, 32, 64, 96, 128, 160, 192, 224, 256, 288, 320, 352, 384, 416, 448)
    mpeg2_bitrates = (0, 32, 48, 56, 64, 80, 96, 112, 128, 144, 160, 176, 192, 224, 256)
    common.require(struct.unpack_from('>15H', c.data_section.data, 0x2c54), mpeg1_bitrates, 'original MPEG1 Layer I bitrate table')
    common.require(struct.unpack_from('>15H', c.data_section.data, 0x2cb4), mpeg2_bitrates, 'original MPEG2 Layer I bitrate table')
    common.require(struct.unpack_from('>3I', c.data_section.data, 0x2c30), (44100, 48000, 32000), 'original MPEG1 sample rates')
    common.require(struct.unpack_from('>3I', c.data_section.data, 0x2c3c), (22050, 24000, 16000), 'original MPEG2 sample rates')
    base = 0x8000 - 21128
    differences = 0
    for allocation in range(1, 15):
        for scale in range(63):
            expected = struct.unpack('>f', struct.pack('>f', math.pow(2, 2 - scale / 3) / ((1 << (allocation + 1)) - 1)))[0]
            actual = struct.unpack_from('>f', c.data_section.data, base + (allocation + 1) * 256 + scale * 4)[0]
            differences += actual != expected
    common.require(differences, 0, 'all valid combined factors match double formula before float conversion')
    undefined_column = [struct.unpack_from('>f', c.data_section.data, base + (allocation + 1) * 256 + 63 * 4)[0]
                        for allocation in range(1, 15)]
    common.require(set(undefined_column), {0.0}, 'original undefined scalefactor column mutes')
    return {'identity': common.IDENTITIES['sound_shared.data'], 'decoder': decoder,
            'allocation_bits': 4, 'allocated_sample_bits': 'allocation + 1', 'scalefactor_bits': 6,
            'subbands': 32, 'normal_samples_per_frame_per_channel': 384,
            'mpeg1_bitrate_table': 0x2c54, 'mpeg2_bitrate_table': 0x2cb4,
            'mpeg1_sample_rate_table': 0x2c30, 'mpeg2_sample_rate_table': 0x2c3c,
            'synthesis_slots': 12, 'shared_synthesis_code': 0x53a0,
            'requantization': '(code + 1 - 2^allocation) * float(2^(2 - scale/3) / (2^(allocation+1) - 1))',
            'factor_table_base': base, 'exact_valid_factor_comparisons': 882,
            'factor_table_sha256': hashlib.sha256(c.data_section.data[base + 512:base + 4096]).hexdigest(),
            'decoder_range_sha256': hashlib.sha256(c.code.data[0x2d0c:0x3144]).hexdigest(),
            'limitation': 'Normal full-rate static decoder path; undefined SF63 mutes in original, while strict ISO-profile reader rejects it. No original execution or device output proof.'}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('bin_root', type=Path)
    args = parser.parse_args()
    try:
        result = inspect(args.bin_root)
    except (OSError, common.pef.PEFError, struct.error) as error:
        parser.exit(1, f'Layer I evidence: {error}\n')
    print(json.dumps(result, sort_keys=True, indent=2))


if __name__ == '__main__':
    main()

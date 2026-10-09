"""Static staff cadence, serialized widths and bankruptcy-gate witnesses.

Only metadata is emitted. Native instructions are neither executed nor
copied. Run with the identified Mac bin folder; --save checks the identified
PC researcher through a bounded actor-chain prefix.
"""
from __future__ import annotations
import argparse
import hashlib
import json
from pathlib import Path
import struct
import evidence
import save_bridge_evidence as bridge

SPANS = [
    (0xf3d8c, 0xf3ee0, '82b6472ff73e42958dbe325a9811e2eef65785aa2dbacf443a600a855e027463'),
    (0xf44c0, 0xf4674, 'addf63acb088621e66c6c5577797297764c61e50f6bc04563f721c39fbd18fc4'),
    (0xf4900, 0xf4c18, '161c7e2dd21c7fa4e4427fdf17068cd6f2c0a3824d6aa771409a486ac7569f96'),
    (0xf4170, 0xf41dc, 'ee8f38a2cac32233ad6cb990c6ba35eccf91225d50106ec9b9962990c9b33e86'),
    (0xf00f0, 0xf0200, 'aeb75e55a9035238e390bc20857412c0899be393919e19a347bd32661c74f6d6'),
    (0xe4a54, 0xe5060, '92aa99226f0f985dcb7dfde2bd4bce406bca8a69288929bd67fe4384520406dd'),
    (0xfcbcc, 0xfd398, '0b6b4bd8b70943a918a48cd9bbc373c589c4e1b7bd6191056fd6c18596795708'),
    (0xfb304, 0xfb508, 'a8ae1893dba7729d00d6a107a0dcc91327bf6bb07a2bb26b729a9e2be997d5d7'),
    (0xd2144, 0xd2290, '50d645991d13d8fbb057fe78e7f4cba6a03f71dac76bc1a8decf02031a375e0e'),
    (0xe7f34, 0xe8b74, '8da660dee74605b3f6a344eb5d85e039f5ae4087f11407af29166e47eb030d99'),
    (0x105c6c, 0x105d3c, '122ed233a59acab56f927d91a90e9b2d72d770adac13cbd5008ab2e705afe754'),
    (0x105b50, 0x105c6c, 'b6b5a3a3e1a1cf41747171391aa42522079148c0f93c9bb8967346c7e0bb88cf'),
    (0xf8410, 0xf8bc0, 'bb98777372b165775eec1ee491b8ee0fabde3bbcc0e2226c29bd1780444c40a1'),
    (0xf7e18, 0xf83c4, 'ac99633e615e4a7d8dd360777a276fd8b6a02e735b3cdf8485c2d223012c875b'),
    (0xf0284, 0xf0728, '54258c678efa164c8a3528ad8a906f0a6fcc97bd483ba0ea104fbed267dbc796'),
    (0xf311c, 0xf32f4, 'af6c9e9dfeae73dd9f0a597a0409f4c12b73dcc8e7e1c5a4b29d75f5f35ef474'),
    (0xcc120, 0xcc21c, 'b66b2562e64b11e47dbf931dda3af4614ae9dfd5ab3aa50126d205a9990f4b06'),
    (0x1059e0, 0x105b50, 'bcf09a9c38e7cac84153e29ed7d2342fd69bc9f3f19dfcfb49479207c5d4e594'),
    (0xe4750, 0xe47f8, '34a39985ee9345c1a0490afab9e998ce4481eb3d6974ec0ab1e8742d46c2899a'),
    (0x10536c, 0x10565c, '3650a4c24a53a7ed27b2d586f93baf996e30103508f1f605dca53373f87efab6'),
]
# helper, width immediate, file-write call, width. These are operand addresses,
# not copied instructions. Whole-file identity pins surrounding context.
WIDTHS = [
    (0xbefc, 0xbf78, 0xbf7c, 2), (0xc90c, 0xc99c, 0xc9a0, 4),
    (0xc7f8, 0xc888, 0xc88c, 4), (0xc6e4, 0xc774, 0xc778, 4),
    (0xcd1b0, 0xcd240, 0xcd244, 4), (0xcde50, 0xcded8, 0xcdedc, 2),
    (0xcdd44, 0xcddcc, 0xcddd0, 2), (0xcabc4, 0xcac3c, 0xcac40, 1),
    (0xe7338, 0xe73c8, 0xe73cc, 4), (0xffeb0, 0xfff3c, 0xfff58, 8),
    (0x1001e0, 0x100270, 0x100274, 4), (0xca20, 0xca98, 0xca9c, 1),
    (0xcaab0, 0xcab40, 0xcab44, 4), (0xfbbf0, 0xfbc74, 0xfbc88, 4),
    (0xfbadc, 0xfbb6c, 0xfbb70, 4), (0xd2534, 0xd25c4, 0xd25c8, 4),
    (0xf530c, 0xf539c, 0xf53a0, 4), (0xf5420, 0xf54b0, 0xf54b4, 4),
    (0xe3930, 0xe39b8, 0xe39bc, 2), (0xebaa0, 0xebb30, 0xebb34, 4),
    (0xce070, 0xce0ec, 0xce0f0, 2), (0xcacc0, 0xcae48, 0xcae4c, 8),
]


def calls(c, start, end, allowed):
    return [evidence.call_target(c, at) for at in range(start, end, 4)
            if evidence.pef._u32(c.code.data, at) >> 26 == 18
            and evidence.pef._u32(c.code.data, at) & 1
            and evidence.call_target(c, at) in allowed]


def inspect(bin_root: Path) -> dict:
    c = evidence.load(bin_root / 'SimThemePark.data', evidence.APP_SHA)
    for start, end, digest in SPANS:
        evidence.require(hashlib.sha256(c.code.data[start:end]).hexdigest(), digest, f'staff region {start:#x}')
    widths = {}
    for helper, operand, write, width in WIDTHS:
        evidence.require(evidence.d_fields(c, operand, 14), (6, 0, width), 'serializer write width')
        api = evidence.glue_import(c, evidence.call_target(c, write), 0x8000)
        evidence.require(api['symbol'].startswith('LbFile_Write'), True, 'serializer write import')
        widths[helper] = width
    # Verify call sequences, then compose widths with native fixed loop counts.
    schemas = [
        ('thing', 0xfa848, 0xfa8d0, [0xcde50, 0xcde50, 0xbefc, 0xbefc], {}, 8, 0xfa808),
        ('sprite_animation', 0xd2184, 0xd21e0, [0xc6e4, 0xd2534, 0xc6e4], {}, 12, 0xd2144),
        ('navigation', 0xfcc04, 0xfcfa4,
         [0xffeb0]*4 + [0xc6e4]*3 + [0xc7f8, 0x1001e0, 0xc7f8, 0xc7f8, 0xca20,
          0xc7f8, 0xc90c, 0xc7f8, 0xc7f8, 0xffeb0, 0xc90c, 0xc6e4, 0xc7f8,
          0xffeb0, 0xc6e4, 0xffeb0, 0xc7f8, 0xffeb0],
         {22: 5, 23: 5}, 177, 0xfcbcc),
        ('thoughts', 0xfb33c, 0xfb40c,
         [0xcd1b0, 0xfbbf0, 0xfbadc, 0xc6e4, 0xc90c], {1: 32}, 144, 0xfb304),
        ('person', 0xe4ad4, 0xe4d54,
         [0xcde50, 0xcde50, 0xcdd44, 0xcdd44, 0xcabc4, 0xe7338, 0xce070, 0xfcbcc,
          0xcaab0, 0xc6e4, 0xc6e4, 0xc90c, 0xcdd44, 0xc7f8, 0xc90c, 0xcd1b0, 0xc7f8, 0xfb304],
         {}, 390, 0xe4a54),
        ('staff_tail', 0xf2c7c, 0xf2ea8,
         [0xf530c, 0xcaab0, 0xcd1b0, 0xe3930, 0xce070, 0xce070, 0xcabc4,
          0xbefc, 0xf5420, 0xc90c, 0xcacc0, 0xcaab0], {3: 33}, 105, 0xf2c28),
        ('researcher_tail', 0xf0144, 0xf017c, [0xc90c, 0xbefc], {}, 6, 0xf00f0),
        ('guest_tail', 0xe7f8c, 0xe85e0,
         [0xc90c, 0xc90c, 0xc6e4, 0xc7f8, 0xc90c, 0xc6e4] + [0xcaab0]*5 +
         [0xbefc] + [0xc90c]*4 + [0xc7f8, 0xcd1b0, 0xcabc4, 0xcabc4, 0xbefc, 0xbefc,
          0xbefc, 0xbefc, 0xcd1b0, 0xcabc4, 0xcd1b0, 0xbefc, 0xebaa0, 0xebaa0, 0xcaab0,
          0xc90c, 0xc90c, 0xcaab0, 0xcaab0, 0xcaab0], {20: 4, 21: 4}, 135, 0xe7f34),
    ]
    serialized = {}
    for name, start, end, expected, repeat, total, target in schemas:
        evidence.require(calls(c, start, end, widths), expected, name + ' serializer call sequence')
        value = sum(widths[helper] * repeat.get(index, 1) for index, helper in enumerate(expected))
        if name == 'person':
            value += widths[0xfa808] + widths[0xd2144]
        evidence.require(value, total, name + ' composed width')
        widths[target] = value
        serialized[name] = value
    for at, op, fields in [
        (0xfcf80, 11, (0, 28, 5)), (0xfb398, 11, (0, 29, 32)),
        (0xf2d50, 11, (0, 29, 33)), (0xe8338, 11, (0, 27, 4)),
        (0xf3db8, 48, (1, 3, 756)), (0xf3dfc, 48, (1, 3, 760)),
        (0xf3dc8, 52, (0, 31, 504)), (0xf3e0c, 52, (0, 31, 500)),
        (0xf44cc, 8, (4, 4, 6)), (0xf45a8, 8, (4, 4, 6)),
        (0xf2f14, 52, (0, 27, 500)), (0xf3094, 52, (0, 27, 504)),
        (0xf0148, 14, (4, 27, 528)), (0xf016c, 14, (4, 27, 524)),
        (0xf058c, 32, (0, 4, 1048)), (0xf066c, 32, (0, 3, 752)),
        (0xf4900 + 0x164, 32, (0, 28, 740)), (0xf4ae0, 32, (3, 28, 744)),
        (0xcc19c, 32, (0, 4, -22728)), (0xcc1a0, 11, (0, 0, 4)),
        (0x105a30, 14, (0, 0, 4)), (0x105a40, 36, (0, 27, -22728)),
        (0xf8058, 11, (0, 4, 24)), (0xf8b68, 10, (0, 20, 3)),
        (0xf8b74, 10, (0, 0, 15)), (0xf4bb0, 10, (0, 0, 10)),
    ]:
        evidence.require(evidence.d_fields(c, at, op), fields, f'staff operand {at:#x}')
    for at, target in [
        (0x10541c, 0xfa9b0), (0xfaa48, 0xf0284), (0xfaa50, 0xf0304),
        (0xf035c, 0xf3d8c), (0xf0530, 0xf44c0), (0xf0568, 0xf459c),
        (0xf0114, 0xf2c28), (0x106f78, 0xf00f0), (0xf2c4c, 0xe4a54),
        (0xe4a7c, 0xfa808), (0xe4aa4, 0xd2144), (0xe7f5c, 0xe4a54),
        (0xf4998, 0xf8bc0), (0xf49a8, 0x1091b8), (0xf4a48, 0xf311c),
        (0xf4ab8, 0xf311c), (0xf3e4c, 0xf4d00), (0xf3eb4, 0xf0380),
        (0xcc1a8, 0x105c6c), (0xcc1b8, 0x1059e0), (0xf8068, 0xf8410),
        (0xf7e48, 0xfc1c4), (0xf7f80, 0xf8bc0), (0xf7fa4, 0xf7fe0),
    ]:
        evidence.require(evidence.call_target(c, at), target, f'staff caller {at:#x}')
    relocs = c.relocs[c.data_section.index]
    evidence.require(relocs[0x44994].addend, 0x106f18, 'model8 restore branch')
    winning_slot = 0x8000 + evidence.d_fields(c, 0x105c6c, 32)[2]
    sentinel_slot = 0x8000 + evidence.d_fields(c, 0x105c70, 32)[2]
    evidence.require(relocs[winning_slot].addend, 0xecdcc, 'winning-sequence ID')
    sentinel = struct.unpack_from('>H', c.data_section.data, relocs[sentinel_slot].addend)[0]
    evidence.require(sentinel, 0, 'no-winning-object sentinel')
    settings = {}
    selected = {'AllStaffConstants.RestLevel': 740, 'AllStaffConstants.HappyHitCosNoRestArea': 744,
                'PerGradeStaffConsts.IdleDuration': 752, 'PerGradeStaffConsts.RecuperationRate': 756,
                'PerGradeStaffConsts.HappinessRecuperationRate': 760,
                'ResearcherConstsPerGrade.WorkDuration': 1048}
    for row in evidence.schema_fields(c.data_section.data, 0x34d10):
        if row['path'] in selected:
            evidence.require(row['runtime_offset'], selected[row['path']], row['path'])
            settings[row['path']] = row
    evidence.require(set(settings), set(selected), 'staff setting bindings')
    constants = {hex(at): evidence.floating(c, at, value, double) for at, value, double in
                 [(0x5610, .012, True), (0x5608, .005, True), (0x55f8, .01, True),
                  (0x5600, .02500000037252903, False)]}
    return {'app_sha256': evidence.APP_SHA,
            'spans': [{'start': a, 'end_exclusive': b, 'sha256': h} for a, b, h in SPANS],
            'serializer_write_widths': {hex(k): v for k, v in widths.items()},
            'serialized_bytes': serialized, 'staff_setting_fields': settings, 'constants': constants,
            'staff_vitals': {'happiness_runtime_offset': 500, 'energy_runtime_offset': 504,
                            'save_encoding': 'f32 of truncated low-byte integer for each vital; not a saved byte'},
            'winning_sequence': {'id_data_offset': 0xecdcc, 'null_id': sentinel, 'predicate': 0x105c6c},
            'limitation': 'Mac operation/width witnesses plus one identified shared save; no general actor parser, '
                          'production import, PC rule equivalence, or native execution.'}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('bin_root', type=Path)
    parser.add_argument('--save', type=Path)
    args = parser.parse_args()
    try:
        result = inspect(args.bin_root)
        if args.save:
            result['pc_save'] = bridge.inspect_save(args.save)
    except (OSError, ValueError, evidence.pef.PEFError) as error:
        parser.exit(1, f'staff evidence: {error}\n')
    print(json.dumps(result, indent=2, sort_keys=True))


if __name__ == '__main__':
    main()

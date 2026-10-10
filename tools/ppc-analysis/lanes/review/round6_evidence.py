"""Round-6 review witnesses: rides passenger-ring selectors and SDT sound metadata.

Usage: python3 -I round6_evidence.py [--bin-root /path/to/mac-feral/bin]
           [--pc-data /path/to/theme-park-world/Data ...]

--bin-root pins SimThemePark.data and decodes the two selector routines behind rides 56a9d26
(PassengerRing.QueryRoom and PlanAllowance), which take a maximum where the helper takes a
minimum. --pc-data checks advisor 2b8e4bf (MP2File reports the first validated frame's rate and channels instead
of a fixed 22050 Hz) against every SDT bank under each data root. The MPEG header reader here
is written independently from ISO 11172-3 / 13818-3, not from Mp2Decoder. Output is counts
and conclusions only; no audio bytes are printed or stored. Nothing original is executed.
"""
from __future__ import annotations

import argparse
import json
import os
import struct
import sys
from collections import Counter
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import review_evidence as rv  # noqa: E402
from review_evidence import Binary, ReviewError, require  # noqa: E402

I32 = 1 << 32


def wrap32(value: int) -> int:
    value &= I32 - 1
    return value - I32 if value & 0x80000000 else value


def room_query(global_limit: int, configured: int, held: int, queued: int) -> int:
    """0x3dd14: diff = configured - (held + queued) (32-bit); return diff only if global < diff."""
    diff = wrap32(configured - wrap32(held + queued))
    return diff if global_limit < diff else global_limit


def capacity_plan(global_limit: int, request: int, definition: int, train: int) -> int:
    """0x3df24: max(global, request), then min with definition+792, then min with the train limit."""
    value = request if global_limit < request else global_limit
    value = value if value < definition else definition
    return value if value < train else train


def x31(rs_rt: int, ra: int, rb: int, xo: int, rc: int = 0) -> tuple:
    return ('x31', rs_rt, ra, rb, xo, rc)


def rides_selector_audit(app: Binary) -> dict:
    # Room query: r0 = cfg(+184) - (+188 + +192); cmpw global, r0; keep &global unless global < r0.
    app.expect(0x3dd34, *x31(3, 4, 3, 266))
    app.expect(0x3dd38, *x31(0, 3, 0, 40))
    app.expect(0x3dd48, *x31(0, 3, 0, 0))
    app.expect(0x3dd4c, 'bc', 4, 0, 0x3dd54)
    app.expect(0x3dd50, 'addi', 5, 1, -12)
    app.expect(0x3dd54, 'lwz', 3, 5, 0)
    # Capacity plan: same max select for (global, request), then two min selects, then update.
    app.expect(0x3df4c, *x31(0, 3, 0, 0))
    app.expect(0x3df54, 'bc', 4, 0, 0x3df5c)
    app.expect(0x3df58, 'addi', 6, 1, 124)
    app.expect(0x3df6c, 'lwz', 0, 5, 792)
    for compare, branch, keep, other, limit_slot in ((0x3df78, 0x3df7c, 0x3df80, 0x3df88, 92),
                                                     (0x3dfac, 0x3dfb0, 0x3dfb4, 0x3dfbc, 88)):
        app.expect(compare, *x31(0, 4, 0, 0))
        app.expect(branch, 'bc', 4, 0, other)
        app.expect(keep, 'addi', 4, 1, 124)
        app.expect(other, 'addi', 4, 1, limit_slot)
    app.expect(0x3dfd4, 'bc', 12, 2, 0x3dfe4)
    require(app.call(0x3dfe0), 0x3ea74, 'capacity plan updates only when the value changed')
    return {
        'room_query_0x3dd14': 'max(global, configured - (held + queued)), signed',
        'capacity_plan_0x3df24': 'min(min(max(global, request), definition+792), train limit), signed',
        'counterexamples': {
            'room(global=100, configured=2, held=0, queued=3)':
                {'native': room_query(100, 2, 0, 3), 'helper_min': min(100, 2 - 3)},
            'plan(global=10, request=100, definition=100, train=100)':
                {'native': capacity_plan(10, 100, 100, 100), 'helper_min': min(10, 100, 100, 100)},
        },
    }

RATES = {3: (44100, 48000, 32000), 2: (22050, 24000, 16000), 0: (11025, 12000, 8000)}
KBPS_V1 = {
    1: (0, 32, 64, 96, 128, 160, 192, 224, 256, 288, 320, 352, 384, 416, 448),
    2: (0, 32, 48, 56, 64, 80, 96, 112, 128, 160, 192, 224, 256, 320, 384),
    3: (0, 32, 40, 48, 56, 64, 80, 96, 112, 128, 160, 192, 224, 256, 320),
}
KBPS_LSF = {
    1: (0, 32, 48, 56, 64, 80, 96, 112, 128, 144, 160, 176, 192, 224, 256),
    2: (0, 8, 16, 24, 32, 40, 48, 56, 64, 80, 96, 112, 128, 144, 160),
}
KBPS_LSF[3] = KBPS_LSF[2]


def frame_format(data: bytes) -> tuple | None:
    """(rate, channels, layer, frame_bytes) for a complete-able first header, else None.

    Free-format (bitrate index 0), reserved bitrate 15, reserved version 1, reserved layer
    and reserved rate index 3 are rejected.
    """
    if len(data) < 4:
        return None
    word = struct.unpack('>I', data[:4])[0]
    if word >> 21 != 0x7FF:
        return None
    version, layer_bits = word >> 19 & 3, word >> 17 & 3
    index, rate_index, padding, mode = word >> 12 & 15, word >> 10 & 3, word >> 9 & 1, word >> 6 & 3
    if version == 1 or layer_bits == 0 or index in (0, 15) or rate_index == 3:
        return None
    layer = 4 - layer_bits
    rate = RATES[version][rate_index]
    bitrate = (KBPS_V1 if version == 3 else KBPS_LSF)[layer][index] * 1000
    if layer == 1:
        size = (12 * bitrate // rate + padding) * 4
    elif layer == 3 and version != 3:
        size = 72 * bitrate // rate + padding
    else:
        size = 144 * bitrate // rate + padding
    return rate, 1 if mode == 3 else 2, layer, size


def legacy_container_rate(entry: bytes) -> int:
    """The int16 the legacy SoundFile reader takes from entry offset 24."""
    return struct.unpack_from('<h', entry, 24)[0]


def sdt_entries(bank: bytes):
    require(len(bank) >= 4, True, 'SDT bank holds an entry count')
    count = struct.unpack_from('<i', bank, 0)[0]
    require(0 <= count and 4 + 4 * count <= len(bank), True, 'SDT entry table fits')
    for i in range(count):
        offset = struct.unpack_from('<i', bank, 4 + 4 * i)[0]
        require(4 + 4 * count <= offset <= len(bank) - 40, True, f'SDT entry {i} offset')
        header, size = struct.unpack_from('<ii', bank, offset)
        require(40 <= header and 0 <= size and offset + header + size <= len(bank), True,
                f'SDT entry {i} span')
        yield bank[offset:offset + header + size], header


def sdt_audit(data_roots: list) -> dict:
    kinds, banks, valid, fallback, wrong_fixed, field_mismatch = Counter(), 0, 0, 0, 0, Counter()
    for root in data_roots:
        for directory, _, names in sorted(os.walk(root)):
            for name in sorted(names):
                if not name.lower().endswith('.sdt'):
                    continue
                banks += 1
                for entry, header in sdt_entries(Path(directory, name).read_bytes()):
                    found = frame_format(entry[header:])
                    if found is None or found[3] > len(entry) - header:
                        fallback += 1
                        continue
                    rate, channels, layer, _ = found
                    valid += 1
                    kinds[f'layer{layer}/{rate}Hz/{channels}ch'] += 1
                    wrong_fixed += rate != 22050
                    if legacy_container_rate(entry) != rate:
                        field_mismatch[(legacy_container_rate(entry), rate)] += 1
    require(banks > 0, True, 'at least one SDT bank under --pc-data')
    require(fallback, 0, 'every shipped SDT entry has a complete supported first frame')
    require(set(field_mismatch), {(44100 - 65536, 44100)} if field_mismatch else set(),
            'legacy int16 field differs from the frame rate only by 44100 Hz sign wrap')
    return {
        'banks': banks,
        'entries': valid,
        'fallback_entries': fallback,
        'formats': dict(sorted(kinds.items())),
        'fixed_22050_wrong': wrong_fixed,
        'legacy_int16_mismatches': sum(field_mismatch.values()),
        'conclusion': 'frame metadata fixes every non-22050 entry; the container fallback is '
                      'never reached by shipped banks but would report the signed-16-bit field',
    }


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--bin-root', type=Path)
    parser.add_argument('--pc-data', type=Path, action='append', default=[])
    args = parser.parse_args()
    if not args.bin_root and not args.pc_data:
        parser.error('give --bin-root and/or --pc-data')
    result = {}
    try:
        if args.bin_root:
            app = Binary(args.bin_root, 'SimThemePark.data', 'SimThemePark.data', rv.APP_TOC)
            result['rides_selectors'] = rides_selector_audit(app)
        for root in args.pc_data:
            result[f'sdt:{root}'] = sdt_audit([root])
    except (OSError, ValueError, KeyError, rv.pef.PEFError, ReviewError) as error:
        parser.exit(1, f'round6 evidence: {error}\n')
    print(json.dumps(result, indent=2, sort_keys=True))


if __name__ == '__main__':
    main()

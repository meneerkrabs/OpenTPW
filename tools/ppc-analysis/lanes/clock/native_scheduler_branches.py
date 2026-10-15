"""Pin interpreted scheduler branch fields; never store original instruction words."""
import argparse
import json
from pathlib import Path

import clock_evidence as evidence
import pef
import timer_evidence as timer


def inspect(root: Path) -> dict:
    app = timer.load_identified(root / 'SimThemePark.data')
    branches = {}
    for name, at, expected in [
        ('application_flag8', 0x1c22fc, (4, 2, 0x1c230c)),
        ('gameplay_flag1', 0x1c2308, (4, 2, 0x1c24c0)),
        ('mode0', 0x1c2388, (12, 2, 0x1c23b4)),
        ('mode2', 0x1c23b0, (4, 2, 0x1c23c0)),
        ('mode1', 0x1c23e4, (4, 2, 0x1c2424)),
    ]:
        word = evidence.word_at(app.code.data, at)
        timer.require(word >> 26, 16, 'conditional branch operation')
        fields = (word >> 21 & 31, word >> 16 & 31, evidence.branch_target(app.code.data, at))
        timer.require(fields, expected, f'BO/BI/target at {at:#x}')
        branches[name] = {'offset': at, 'bo': fields[0], 'bi': fields[1], 'target': fields[2]}
    for at, expected in [(0x1c22f8, (0, 0, 0, 28, 28, 1)),
                         (0x1c2304, (0, 0, 0, 31, 31, 1))]:
        word = evidence.word_at(app.code.data, at)
        timer.require(word >> 26, 21, 'mask operation')
        fields = (word >> 21 & 31, word >> 16 & 31, word >> 11 & 31,
                  word >> 6 & 31, word >> 1 & 31, word & 1)
        timer.require(fields, expected, 'mask records EQ for zero result')
    comparisons = {}
    for name, at, value in [('mode0', 0x1c2384, 0), ('mode2', 0x1c23ac, 2), ('mode1', 0x1c23e0, 1)]:
        timer.require(timer.d_fields(app, at, 11), (0, 0, value), 'mode comparison')
        comparisons[name] = value
    timer.require(timer.call_target(app, 0x1c23b8), 0x10536c, 'direct world tick')
    timer.require(timer.call_target(app, 0x1c23ec), 0x10565c, 'wrapper world tick')
    # The SDK contract must use binary64 FMA, not separate multiplication/addition.
    word = pef._u32(app.code.data, 0x127d14)
    timer.require((word >> 26, word >> 21 & 31, word >> 16 & 31,
                   word >> 11 & 31, word >> 6 & 31, word >> 1 & 31),
                  (63, 0, 2, 0, 1, 29), 'scaled accumulator binary64 fused multiply-add')
    return {'identity_sha256': timer.IDENTITIES['SimThemePark.data'], 'branches': branches,
            'mode_comparisons': comparisons, 'work_target': 0x1c230c, 'skip_target': 0x1c24c0,
            'direct_target': 0x1c23b4, 'wrapper_target': 0x1c23e8, 'mode1_check_target': 0x1c23c0,
            'after_world_target': 0x1c2424, 'fma_code_offset': 0x127d14,
            'limitations': 'Decoded condition metadata, not original bytes or native execution.'}


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('bin_root', type=Path)
    args = parser.parse_args()
    try:
        print(json.dumps(inspect(args.bin_root), indent=2, sort_keys=True))
    except (OSError, pef.PEFError) as error:
        parser.exit(1, f'native scheduler branches: {error}\n')

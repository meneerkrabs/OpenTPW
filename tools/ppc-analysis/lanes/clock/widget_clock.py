"""Identity-pinned widget clock selection metadata; no original execution."""
import argparse
import hashlib
import json
from pathlib import Path

import clock_evidence
import pef
import timer_evidence as timer


def inspect(root: Path) -> dict:
    app = timer.load_identified(root / 'SimThemePark.data')
    for at, opcode, expected in [
        (0x171ef8, 32, (3, 2, -16156)), (0x171f04, 32, (3, 3, 0)),
        (0x171f0c, 32, (12, 12, 8)),
        (0x13cb14, 32, (3, 2, -26132)),
        (0x13e218, 32, (0, 2, -26228)), (0x13e21c, 32, (3, 2, -26132)),
        (0x13e220, 36, (0, 3, 0)),
        (0x171f4c, 32, (0, 2, -25012)), (0x171f50, 32, (3, 2, -25004)),
        (0x171f54, 36, (0, 3, 0)),
        (0x171ee8, 32, (4, 2, -16156)), (0x171eec, 36, (3, 4, 0)),
        (0x171eb8, 14, (5, 0, 0)), (0x171ec0, 14, (6, 0, 1000)),
    ]:
        timer.require(timer.d_fields(app, at, opcode), expected, f'widget clock field at {at:#x}')
    for slot, target in [(0x198c, 0x47ddc), (0x19ec, 0x120e74),
                         (0x1e4c, 0x4fc44), (0x1e54, 0x135e6c), (0x40e4, 0x4fc24)]:
        relocation = app.relocs[1][slot]
        timer.require((relocation.kind, relocation.target, relocation.addend),
                      ('section', 1, target), f'widget relocated pointer at {slot:#x}')
    for slot, code in [(0x47de4, 0x13ca44), (0x4fc4c, 0x171e9c)]:
        pointer = app.relocs[1][slot]
        timer.require((pointer.kind, pointer.target), ('section', 1), 'clock vtable vector')
        vector = app.relocs[1][pointer.addend]
        timer.require((vector.kind, vector.target, vector.addend), ('section', 0, code),
                      'widget clock callback code')
    timer.require(timer.call_target(app, 0x171ec4), 0x1c4010, 'default clock divider')
    divider_digest = hashlib.sha256(app.code.data[0x1c4010:0x1c40fc]).hexdigest()
    timer.require(divider_digest,
                  '9a130bd65ecb0edbca64d29fd90f712e96be72cd7089a05b7e64a2fe4af41d83',
                  'default divider matches the inspected Bullfrog unsigned division helper')
    returned = pef._u32(app.code.data, 0x171ecc)
    timer.require((returned >> 26, returned >> 21 & 31, returned >> 16 & 31,
                   returned >> 11 & 31, returned >> 1 & 0x3ff),
                  (31, 4, 3, 4, 444), 'default clock returns low quotient')
    timer.require(timer.call_target(app, 0x13cb18), 0x171edc, 'UI installs clock object')
    timer.require(timer.call_target(app, 0x13ca50), 0x1c4d64, 'installed clock imports LbTime')
    timer.glue_import(app, 0x1c4d64, 0x8000)
    timer.require(timer.call_target(app, 0x171eac), 0x1c5844, 'default clock imports GetAbsolute')
    timer.glue_import(app, 0x1c5844, 0x8000)
    return {'identity': timer.IDENTITIES['SimThemePark.data'], 'toc': '0x8000',
            'widget_getter_code': '0x171ef4', 'current_object_pointer_data': '0x4fc24',
            'default_callback': '0x171e9c', 'default_source': 'GetAbsolute divided by 1000',
            'installed_callback': '0x13ca44', 'installed_source': 'LbTime_GetClock',
            'installed_at_code': '0x13cb18', 'identified_widget_time_unit': 'milliseconds',
            'default_divider_matches_bullfrog_sha256': divider_digest,
            'limitations': ['Alternate injected clock objects are not qualified.',
                            'This is a raw widget clock, separate from scaled park elapsed clocks.',
                            'Widget repeat thresholds and event handling are UI-lane evidence.',
                            'No original execution or Windows qualification.']}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('bin_root', type=Path)
    args = parser.parse_args()
    try:
        print(json.dumps(inspect(args.bin_root), indent=2, sort_keys=True))
    except (OSError, pef.PEFError) as error:
        parser.exit(1, f'widget clock: {error}\n')


if __name__ == '__main__':
    main()

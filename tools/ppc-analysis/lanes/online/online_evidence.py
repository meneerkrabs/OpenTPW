"""Bounded, read-only online-service evidence from the Feral Mac release.

Covers what the original online client did, without emitting original payload
bytes: the dialog layout streams of the online screens (their window ids,
rectangles and hierarchy), the chat-satellite connector that fills the globe's
five satellites with chat-room player counts, and the online shared-library
export surface (Chat/City/News/Auth/Upload/Vote/Mail classes).

Run directly for a summary report; test_online_evidence.py asserts the same
facts against the identified binaries.
"""
from __future__ import annotations

import argparse
from pathlib import Path

import analyze

APP_SHA = '04809cd4ccee5433c7fb0b7c93d32f6a7aa629c1849181c0b7906415e5e295f5'
ONLINE_SHA = '05c21d2eda5fa4b800859a874ab986433adae2e303de230476329df6c019b2cb'

# Layout streams of the online screens, as (data offset, building function).
# The building functions are the Mac ones from docs/reverse/UI-MAP.md minus
# the 0x10000000 PEF load base.
ONLINE_TABLES = {
    'online_status': (0x5011c, 0x182e00),
    'city_lobby': (0x50172, 0x183274),
    'selected_labels': (0x50580, 0x1846a8),
    'login_dialog': (0x505f4, 0x184b48),
    'news_panel': (0x50738, 0x18539c),
    'unsent_postcards': (0x508bc, 0x186a84),
    'chat_overlay_list': (0x50b2c, 0x189e64),
    'overlay_bubble_b': (0x50d1a, 0x189e64),
    'overlay_bubble_a': (0x50e56, 0x189e64),
    'find_parks': (0x50f84, 0x18ab84),
    'park_details': (0x51150, 0x18b474),
    'other_parks_chart': (0x5137e, 0x18c880),
    'publish_park': (0x514de, 0x18ce80),
    'world_lobby': (0x523f0, 0x1b9068),
}

# The globe's chat satellites: the connector at 0x1b8808 (its failure string
# names it "ONLINE CHATELLITES") connects the Chat client, asks RoomInfo for
# each name of a run-time list and writes each formatted "%d" player count
# onto one satellite control (0x1b96d8 finds control 90120 + index and calls
# a virtual setter).
SATELLITE_IDS = [90119 + index for index in range(1, 6)]


def identified(path: Path, expected: str) -> analyze.Analysis:
    import hashlib
    digest = hashlib.sha256(path.read_bytes()).hexdigest()
    if digest != expected:
        raise ValueError(f'{path.name} is not the identified release ({digest[:12]}…)')


def require(actual, expected, context):
    if actual != expected:
        raise AssertionError(f'{context}: {actual!r} != {expected!r}')
    return actual


def satellite_immediates(app):
    """The five satellite control ids in the setter (0x1b96d8) and the connector's five bound."""
    import struct
    hits = set()
    bound = False
    code = app.code
    for start, end in ((0x1b8808, 0x1b8b08), (0x1b96d8, 0x1b97f8)):
        for offset in range(start, end, 4):
            word = struct.unpack_from('>I', code, offset)[0]
            if word >> 26 == 14:  # addi
                immediate = word & 0xFFFF
                if any(immediate == value & 0xFFFF for value in SATELLITE_IDS):
                    hits.add(0x10000 | immediate)
                if start == 0x1b8808 and immediate == 5 and (word >> 21 & 31) != (word >> 16 & 31):
                    bound = True
    return sorted(hits), bound


def inspect(bin_root: Path):
    import sys
    sys.path.insert(0, str(Path(__file__).resolve().parents[2]))
    sys.path.insert(0, str(Path(__file__).resolve().parent))
    from corpus import layout_table

    app = analyze.load(str(bin_root / 'SimThemePark.data'))
    identified(Path(bin_root / 'SimThemePark.data'), APP_SHA)

    tables = {}
    for name, (offset, builder) in ONLINE_TABLES.items():
        decoded = layout_table(app.data, offset)
        tables[name] = {
            'offset': f'data:{offset:#x}', 'builder': f'{builder:#x}',
            'windows': decoded['windows'], 'external': decoded['external_parent_properties'],
        }

    # The chatellite connector: failure string, Chat::Connect and RoomInfo glue
    # targets, the "%d" format and the five satellite controls.
    connector = app.functions.get(0x1b8808)
    imports = {glue: app.c.imports[index].name for glue, index in app.glue.items()}
    calls = {ref.target for ref in connector.refs if ref.kind == 'call'}
    strings = [app.cstring_data(ref.target) or app.cstring_code(ref.target)
               for ref in connector.refs if ref.kind == 'string']
    satellite_ids, bound = satellite_immediates(app)

    online = analyze.load(str(bin_root / 'libraries' / 'online_shared.data'))
    identified(Path(bin_root / 'libraries' / 'online_shared.data'), ONLINE_SHA)
    export_names = {export.name.split('__', 1)[0].strip('_') for export in online.c.exports}

    return {
        'tables': tables,
        'chatellites': {
            'failure_string': strings,
            'connect_import': [imports[target] for target in calls if target in imports
                               and imports[target].startswith('Connect__4Chat')],
            'room_info_import': [imports[target] for target in calls if target in imports
                                 and imports[target].startswith('RoomInfo__4Chat')],
            'format': [string for string in strings if string == '%d'],
            'satellite_controls': satellite_ids,
            'at_most_five': bound,
        },
        'online_classes': sorted(export_names),
    }


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('bin_root', type=Path)
    args = parser.parse_args()
    report = inspect(args.bin_root)
    for name, table in report['tables'].items():
        print(f"{name} ({table['offset']}, built by {table['builder']}): "
              f"{len(table['windows'])} windows")
        for window in table['windows']:
            print(f"  id={window['id']:<9} type={window['type']:<2} "
                  f"rect={window['rectangle']} parent={window['parent']}")
    print('chatellites:', report['chatellites'])
    print('online shared classes:', ', '.join(report['online_classes']))


if __name__ == '__main__':
    main()

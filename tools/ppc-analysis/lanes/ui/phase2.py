"""Enumerate identified layout consumers and selected model bindings, without table dumps."""
from __future__ import annotations

import argparse
import hashlib
import json
from pathlib import Path
import struct

from corpus import layout_table, model_summary, name_hash, string_labels, wad
from witness import APP_SHA, MAC_UI_SHA, branch, d_form, identified, relocation, require, x_form

# Each load is the actual r4 TOC argument load, followed by the identified
# direct call. The set includes nine status-panel forwarders whose generic
# helper forwards the table to the original layout interpreter.
DIRECT_CALLS = [
    (0x8cfec, 0x8cff4), (0x13cfac, 0x13cfb0), (0x13d2e0, 0x13d2e4),
    (0x13d4e4, 0x13d5b8), (0x13d4e4, 0x13d66c), (0x13daf0, 0x13dafc),
    (0x13dbc0, 0x13dbc8), (0x13dd1c, 0x13dd28), (0x144c34, 0x144c44),
    (0x147ad4, 0x147ae0), (0x148684, 0x148690), (0x149c18, 0x149c20),
    (0x149f20, 0x149f28), (0x14a1e0, 0x14a1e8), (0x14a4ac, 0x14a4b4),
    (0x14ac30, 0x14ac3c), (0x14ba8c, 0x14ba98), (0x14cc70, 0x14cc7c),
    (0x14dd7c, 0x14dd88), (0x14e908, 0x14e914), (0x14ec8c, 0x14ec98),
    (0x14f338, 0x14f348), (0x150c94, 0x150ca0), (0x152534, 0x152540),
    (0x154b10, 0x154b1c), (0x156c18, 0x156c24), (0x158cc8, 0x158cd4),
    (0x15af68, 0x15af74), (0x15c874, 0x15c880), (0x15e068, 0x15e070),
    (0x15eff0, 0x15eff8), (0x161a50, 0x161a5c), (0x164504, 0x164528),
    (0x165f08, 0x165f10), (0x168de0, 0x168dec), (0x16a5e4, 0x16a5f4),
    (0x16a6a4, 0x16a6ac), (0x16e494, 0x16e4a0), (0x16eee4, 0x16eef0),
    (0x182e50, 0x182e5c), (0x1832b0, 0x1832b8), (0x18399c, 0x1839a4),
    (0x1846d4, 0x1846e4), (0x189e98, 0x189eb0), (0x18a078, 0x18a07c),
    (0x18a0b4, 0x18a0b8), (0x18abbc, 0x18abcc), (0x18b518, 0x18b528),
    (0x18c8b0, 0x18c8b8), (0x1b90a4, 0x1b90ac), (0x1b9d24, 0x1b9d30),
]
STATUS_CALLS = [
    (0x10b82c, 0x10b830), (0x14cacc, 0x14cad8), (0x14fd9c, 0x14fda0),
    (0x167384, 0x1673a4), (0x1689b4, 0x1689bc), (0x16c3d0, 0x16c3d8),
    (0x16d2e0, 0x16d2e4), (0x16e1d0, 0x16e1d4), (0x16ed64, 0x16ed68),
]

# Selected controls only. All remaining tables are summarized by identity,
# interpreted shape and caller addresses; no whole original stream is emitted.
SELECTED = {
    'main_hud': (0x4ab38, [29, 32, 33, 38, 39, 40, 41, 42, 43, 47, 48]),
    'options': (0x4b0dc, [120000, 120020, 120021, 120009, -1, -2]),
    'world_lobby': (0x523f0, [90111, 90112, 90113, 90114, 90115, 90116, 90117, 90118]),
    'theme_lobby': (0x50366, [123111, 123112, 123113, 123116, 123119, 123120, 123121]),
    'buy_items': (0x4cd94, [489, 490, 492, 504, 505, 506, 507, 508, 509, -2]),
    'ride_status': (None, [15892, 15893, 15904, 15898, 15900, 15908, 15909, 15915, 15928]),
}
ENGINE_SHA = 'c549123f647dcf33c3e2c3d515bffe2cfcb19d11680f743f02ca42bb09dbd73b'
MAC_LANGUAGE_SHA = {
    'uitext': 'c3768d1f448c0f85952ae3edda41da750081e85996eb76750df53fad7688bdef',
    'uihelp': 'adc16efc04bdb8c605e916a23b7794342d507d0a90cc3f8f635dadf20cf59b75',
    'mbtouni': 'f682ed03507d6ea20f5f1689c321f32b52f8a21e838e9440fbc3207e7236b67d',
}


def float_multiply(app, at, expected):
    word = int.from_bytes(app.code.data[at:at + 4], 'big')
    require((word >> 26, word >> 1 & 31), (59, 25), 'single-precision product')
    require((word >> 21 & 31, word >> 16 & 31, word >> 6 & 31), expected,
            f'floating register operands at {at:#x}')


def sign_effects(app, bin_root):
    engine = identified(bin_root / 'libraries/engine_shared.data', ENGINE_SHA)
    for at, op, fields in [(0xab2ec, 48, (25, 24, 12)),
                            (0xab2f4, 48, (16, 24, 16)),
                            (0xab2fc, 48, (18, 24, 20)),
                            (0xab4b8, 48, (2, 24, 24)),
                            (0xab54c, 38, (0, 28, 0)),
                            (0xab564, 38, (0, 28, 1)),
                            (0xab56c, 38, (3, 28, 2)),
                            (0xab570, 38, (0, 28, 3))]:
        d_form(app, at, op, fields)
    for at, fields in [(0xab484, (28, 0, 25)), (0xab488, (29, 2, 25)),
                        (0xab48c, (30, 3, 25)), (0xab494, (4, 16, 4)),
                        (0xab4c8, (0, 18, 0))]:
        float_multiply(app, at, fields)
    exports = [e for e in engine.exports if e.name == 'swizzle_for_gimex__6BitmapFv']
    require(len(exports), 1, 'engine bitmap swizzle export')
    entry = exports[0]
    code = relocation(engine, entry.value, engine.code.index)
    require(code, 0x3e6c8, 'swizzle code target')
    # Swizzle reverses four channel bytes in place without resizing: tracing
    # it avoids assuming the intermediate image was resized before splitting.
    for at, op, fields in [(0x3e6d4, 34, (4, 5, 0)), (0x3e6dc, 34, (7, 5, 1)),
                            (0x3e6e0, 34, (8, 5, 2)), (0x3e6e4, 34, (0, 5, 3)),
                            (0x3e6e8, 38, (0, 5, 0)), (0x3e6ec, 38, (8, 5, 1)),
                            (0x3e6f0, 38, (7, 5, 2)), (0x3e6f4, 38, (4, 5, 3))]:
        d_form(engine, at, op, fields)
    return {'engine_sha256': ENGINE_SHA, 'swizzle_code': code,
            'effect_parameters_2_to_4': 'shared base, diffuse and white-specular coefficients; not RGB',
            'alpha': 'mask byte copied to channel 0 before swizzle',
            'intermediate_order': 'alpha, red, green, blue',
            'swizzled_order': 'blue, green, red, alpha',
            'swizzle_resizes': False,
            'caller_destination_dimensions': [128, 128],
            'destinations': 2,
            'output_limit': 'Final copy/pack loops process base width columns twice and base height rows; geometry UV orientation remains unverified.'}


def input_events(app):
    for at, op, fields in [(0x114b14, 42, (0, 10, 2)), (0x114b20, 42, (0, 10, 4)),
                            (0x114b30, 14, (8, 8, 20)), (0x114b60, 32, (12, 4, 16)),
                            (0x114b80, 44, (5, 4, 6)), (0x114bf0, 42, (0, 10, 2)),
                            (0x114bfc, 42, (0, 10, 4)), (0x114c0c, 14, (8, 8, 20)),
                            (0x114c3c, 32, (12, 4, 12)), (0x114c5c, 44, (5, 4, 6)),
                            (0x114dd0, 14, (6, 0, 15)), (0x114de8, 44, (6, 26, 4)),
                            (0x171ff4, 14, (7, 0, 500)), (0x171ff8, 14, (6, 0, 125)),
                            (0x172710, 24, (3, 0, 2)), (0x172720, 38, (0, 31, 312)),
                            (0x172810, 38, (0, 31, 312)), (0x172880, 38, (0, 31, 312)),
                            (0x172a34, 14, (4, 0, 256))]:
        d_form(app, at, op, fields)
    records = []
    for row, expected_key, vector, code in [(0x452c4, 0x6d00, 0x7508, 0x11315c),
                                           (0x452d8, 0x6b00, 0x7510, 0x113184)]:
        index, key, modifiers = struct.unpack_from('>3H', app.data_section.data, row)
        require((key, modifiers), (expected_key, 0), 'speed key and modifiers')
        require(relocation(app, row + 12, app.data_section.index), vector, 'release callback vector')
        require(relocation(app, vector, app.code.index), code, 'speed callback entry')
        require(struct.unpack_from('>I', app.data_section.data, row + 16)[0], 0, 'no press callback')
        records.append({'record_data': row, 'record_index': index, 'encoded_key': key,
                        'modifiers': modifiers, 'release_vector_data': vector, 'release_code': code})
    return {'keyboard_group_count': 15, 'keyboard_record_stride': 20,
            'match_fields': {'key': 2, 'modifiers': 4},
            'press_callback_offset': 16, 'release_callback_offset': 12,
            'speed_records': records,
            'button_activation_message': 256,
            'button_repeat_milliseconds': [500, 125],
            'widget_clock_handoff': 'Clock lane traces getter 0x171ef4 to raw LbTime/GetAbsolute milliseconds, separate from scaled simulation.',
            'limitations': ['Encoded speed keys require hardware/platform key translation before naming physical keys.',
                            'Top-level pointer event producer requires separate tracing.']}


def table_argument(app, load, call, expected_target):
    word = int.from_bytes(app.code.data[load:load + 4], 'big')
    immediate = word & 65535
    immediate -= 65536 if immediate & 32768 else 0
    register = word >> 21 & 31
    d_form(app, load, 32, (register, 2, immediate))
    if register != 4:
        copies = []
        for at in range(call - 64, call, 4):
            move = int.from_bytes(app.code.data[at:at + 4], 'big')
            if (move >> 26, move >> 21 & 31, move >> 16 & 31, move & 65535) == (14, 4, register, 0):
                copies.append(at)
        require(bool(copies), True, 'table argument forwarded from preserved register')
    require(branch(app, call), expected_target, 'layout consumer call')
    return relocation(app, 0x8000 + immediate, app.data_section.index)


def inspect(bin_root: Path, mac_ui: Path, language_paths: dict[str, Path] | None = None):
    app = identified(bin_root / 'SimThemePark.data', APP_SHA)
    require(hashlib.sha256(mac_ui.read_bytes()).hexdigest(), MAC_UI_SHA, 'Mac UI identity')
    # Prove registry keys come from a model's root node name, not filename.
    d_form(app, 0x13c1fc, 32, (4, 4, 120))
    d_form(app, 0x13c200, 32, (4, 4, 84))
    require(branch(app, 0x13c204), 0x173844, 'root name registration')
    d_form(app, 0x17387c, 14, (4, 0, 47))
    require(branch(app, 0x173880), 0x1709b8, 'registry name hash')
    x_form(app, 0x1709c8, 316, (0, 0, 6))
    x_form(app, 0x1709cc, 235, (6, 4, 0))
    d_form(app, 0x17390c, 36, (30, 31, 16))
    require(branch(app, 0x181c9c), 0x173a70, 'layout drawing-reference resolver')
    d_form(app, 0x173a90, 32, (0, 4, 16))
    d_form(app, 0x173aa4, 32, (4, 4, 20))
    # The status initializer's table argument r4 is preserved from caller r4
    # through r27 and forwarded to the same interpreter, avoiding name guesses.
    d_form(app, 0x147204, 14, (27, 4, 0))
    d_form(app, 0x147248, 14, (4, 27, 0))
    require(branch(app, 0x147254), 0x181aac, 'status table forwarding')
    members = wad(mac_ui)
    models = {}
    for name, data in members.items():
        if name.lower().endswith('.md2'):
            summary = model_summary(data)
            root = summary['nodes'][summary['root_node_index']]['name']
            models.setdefault(name_hash(root), []).append({'member': name, 'root': root,
                                                           'sha256': summary['sha256']})
    tables, call_rows = {}, []
    for calls, target in [(DIRECT_CALLS, 0x181aac), (STATUS_CALLS, 0x1471f8)]:
        for load, call in calls:
            offset = table_argument(app, load, call, target)
            if offset not in tables:
                tables[offset] = layout_table(bytes(app.data_section.data), offset)
            call_rows.append({'load_code': load, 'call_code': call, 'table_data': offset,
                              'via_status_initializer': target == 0x1471f8})
    selected = {}
    for label, (offset, identifiers) in SELECTED.items():
        if offset is None:
            offset = table_argument(app, 0x167384, 0x1673a4, 0x1471f8)
        decoded = tables[offset]
        windows = []
        for window in decoded['windows']:
            if window['id'] not in identifiers:
                continue
            item = {key: value for key, value in window.items()
                    if key in ('data_offset', 'type', 'id', 'rectangle', 'parent', 'origin_command',
                               'command_3', 'command_17', 'command_18')}
            for command in (1, 2):
                reference = window.get(f'command_{command}')
                if reference is not None:
                    item[f'drawing_{command}'] = {'key': reference,
                                                'model_candidates': models.get(reference, [])}
            windows.append(item)
        selected[label] = {'table_data': offset, 'windows': windows}
    catalog = []
    for offset, decoded in sorted(tables.items()):
        end = offset + decoded['words_consumed'] * 2
        root = decoded['windows'][0]
        catalog.append({'table_data': offset, 'end_data': end,
                        'sha256': hashlib.sha256(bytes(app.data_section.data[offset:end])).hexdigest(),
                        'window_count': len(decoded['windows']), 'root_id': root['id'],
                        'root_rectangle': root['rectangle'],
                        'outer_properties': decoded['external_parent_properties']})
    # Date/cash font and color setters are original constructor operands.
    for at, op, expected in [(0x157030, 14, (3, 0, 1)), (0x157058, 14, (4, 0, 255)),
                              (0x15705c, 14, (5, 0, 255)), (0x157060, 14, (6, 0, 255)),
                              (0x157064, 14, (7, 0, 255)), (0x1573ac, 14, (0, 0, 3)),
                              (0x1573cc, 14, (4, 0, 0)), (0x1573d0, 14, (5, 0, 0)),
                              (0x1573d4, 14, (6, 0, 0)), (0x1573d8, 14, (7, 0, 255)),
                              (0x172d90, 34, (0, 3, 312)), (0x172d9c, 32, (3, 3, 144)),
                              (0x172da4, 32, (3, 3, 140))]:
        d_form(app, at, op, expected)
    require(branch(app, 0x157034), 0x1383c8, 'cash font accessor')
    require(branch(app, 0x1573b8), 0x1383c8, 'date font accessor')
    language = None
    if language_paths:
        files = {key: path.read_bytes() for key, path in language_paths.items()}
        require(set(files), set(MAC_LANGUAGE_SHA), 'complete Mac language inputs')
        for key, data in files.items():
            require(hashlib.sha256(data).hexdigest(), MAC_LANGUAGE_SHA[key], f'Mac {key} identity')
        language = {'identities': MAC_LANGUAGE_SHA,
                    'selected_uitext': string_labels(files['uitext'], files['mbtouni'],
                                                     [186, 187, 188, 190, 315, 319, 320, 321, 324, 327, 330]),
                    'selected_uihelp': string_labels(files['uihelp'], files['mbtouni'],
                                                     [12, 13, 469, 470, 471, 472, 473, 474])}
        require(language['selected_uihelp'][12], 'Click to open the ride', 'door alternate help')
        require(language['selected_uihelp'][13], 'Click to close the ride', 'door ordinary help')
    return {'app_sha256': APP_SHA, 'mac_ui_sha256': MAC_UI_SHA,
            'consumer_calls': call_rows, 'catalog': catalog, 'selected': selected,
            'sign_effects': sign_effects(app, bin_root),
            'input_events': input_events(app),
            'mac_language': language,
            'hud_text': {'cash': {'id': 47, 'font_slot': 1, 'rgba': [255, 255, 255, 255]},
                         'date': {'id': 32, 'font_slot': 3, 'rgba': [0, 0, 0, 255]}},
            'door_help_selector': {'state_1_field': 144, 'other_state_field': 140,
                                   'alternate_help_id': 12, 'ordinary_help_id': 13},
            'limitation': 'Allocation metadata and root-name bindings; no original pixels or final coordinate transforms.'}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('bin_root', type=Path)
    parser.add_argument('mac_ui', type=Path)
    parser.add_argument('--mac-uitext', type=Path)
    parser.add_argument('--mac-uihelp', type=Path)
    parser.add_argument('--mac-mbtouni', type=Path)
    args = parser.parse_args()
    try:
        paths = {'uitext': args.mac_uitext, 'uihelp': args.mac_uihelp, 'mbtouni': args.mac_mbtouni}
        if any(paths.values()) and not all(paths.values()):
            raise ValueError('supply all three Mac language paths together')
        result = inspect(args.bin_root, args.mac_ui, paths if all(paths.values()) else None)
    except (OSError, ValueError, KeyError) as error:
        parser.exit(1, f'UI layout evidence: {error}\n')
    print(json.dumps(result, sort_keys=True, indent=2))


if __name__ == '__main__':
    main()

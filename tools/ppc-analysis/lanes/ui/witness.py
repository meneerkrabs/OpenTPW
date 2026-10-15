"""Identified Mac UI operand/relocation witnesses; no execution or disassembly output.

Use the shared pef.py reader on PYTHONPATH when running in an isolated lane.
The report deliberately contains selected interpreted facts, not original bytes.
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
from corpus import layout_table, model_summary, resource_index, wad

APP_SHA = '04809cd4ccee5433c7fb0b7c93d32f6a7aa629c1849181c0b7906415e5e295f5'
MACDOZE_SHA = 'ba11331a70bce77140ae6e7fe73145fe4e13cce5f042707a494cb77654b32f0d'
MAC_UI_SHA = '5d710990242a9cc52656d282a50af77257dcc967e1d41985b130576150baa135'
RESIDX_SHA = '02b937519e897a9fc7f36fec29197bc1abe40350979800306062c56f039174f0'


def require(actual, expected, context):
    if actual != expected:
        raise ValueError(f'{context}: interpreted value {actual!r}, expected {expected!r}')
    return actual


def identified(path: Path, expected: str):
    data = path.read_bytes()
    require(hashlib.sha256(data).hexdigest(), expected, path.name)
    return pef.PEFContainer(data, path.name)


def d_form(container, at, opcode, expected):
    word = pef._u32(container.code.data, at)
    require(word >> 26, opcode, f'operation at code:{at:#x}')
    immediate = word & 65535
    if immediate & 32768:
        immediate -= 65536
    return require((word >> 21 & 31, word >> 16 & 31, immediate), expected,
                   f'operands at code:{at:#x}')


def branch(container, at):
    word = pef._u32(container.code.data, at)
    require((word >> 26, word & 3), (18, 1), f'linked relative branch at {at:#x}')
    displacement = word & 0x3fffffc
    if displacement & 0x2000000:
        displacement -= 0x4000000
    return at + displacement


def x_form(container, at, operation, expected):
    word = pef._u32(container.code.data, at)
    require((word >> 26, word >> 1 & 1023), (31, operation), f'indexed operation at {at:#x}')
    return require((word >> 21 & 31, word >> 16 & 31, word >> 11 & 31), expected,
                   f'indexed operands at {at:#x}')


def mask_form(container, at, expected):
    word = pef._u32(container.code.data, at)
    require(word >> 26, 21, f'mask operation at {at:#x}')
    return require((word >> 21 & 31, word >> 16 & 31, word >> 11 & 31,
                    word >> 6 & 31, word >> 1 & 31, word & 1), expected,
                   f'mask operands at {at:#x}')


def relocation(container, at, section):
    target = container.relocs[container.data_section.index].get(at)
    if target is None or target.kind != 'section' or target.target != section:
        raise ValueError(f'missing relocation at data:{at:#x}')
    return target.addend


def class_slot(container, table, name, slot, expected):
    typeinfo = relocation(container, table, container.data_section.index)
    name_offset = relocation(container, typeinfo, container.code.index)
    data = container.code.data
    end = data.find(b'\0', name_offset, name_offset + 128)
    require(bytes(data[name_offset:end]).decode('ascii'), name, 'RTTI class')
    vector = relocation(container, table + 8 + slot * 4, container.data_section.index)
    code = relocation(container, vector, container.code.index)
    require(code, expected, f'{name} virtual slot {slot}')
    return {'class': name, 'rtti_name_code': name_offset, 'typeinfo_data': typeinfo,
            'vtable_data': table, 'slot': slot, 'vector_data': vector, 'code': code}


def import_at(container, target, toc=0x8000):
    word = pef._u32(container.code.data, target)
    require((word >> 26, word >> 21 & 31, word >> 16 & 31), (32, 12, 2), 'import glue load')
    immediate = word & 65535
    if immediate & 32768:
        immediate -= 65536
    entry = container.relocs[container.data_section.index].get(toc + immediate)
    if entry is None or entry.kind != 'import':
        raise ValueError('unrelocated import glue')
    item = container.imports[entry.target]
    return {'symbol': item.name, 'library': item.library, 'glue_code': target}


def inspect(bin_root: Path, mac_ui: Path, residx: Path, pc_ui: Path | None = None):
    app = identified(bin_root / 'SimThemePark.data', APP_SHA)
    macdoze = identified(bin_root / 'libraries/macdoze_shared.data', MACDOZE_SHA)
    require(hashlib.sha256(mac_ui.read_bytes()).hexdigest(), MAC_UI_SHA, 'Mac ui.wad')
    index_data = residx.read_bytes()
    require(hashlib.sha256(index_data).hexdigest(), RESIDX_SHA, 'American resource index')
    resources = resource_index(index_data)
    slots = [class_slot(app, *args) for args in [
        (0x474e8, 'InterfaceFont', 2, 0x128164),
        (0x474e8, 'InterfaceFont', 3, 0x128328),
        (0x474c4, 'InterfaceFontWrapper', 2, 0x127e28),
        (0x474c4, 'InterfaceFontWrapper', 3, 0x127f3c),
        (0x480e0, 'CUIHelpWindow', 14, 0x141a94),
        (0x4fc6c, 'InterfaceButton', 5, 0x1720e0),
        (0x47f28, 'InterfaceDrawEngineMesh', 3, 0x13e83c),
    ]]
    # Four groups; each contributes exactly 13 resource requests. Resource IDs
    # are read from operands and joined to the original language index.
    groups = [(0x11f138, 0x11f6bc), (0x11f6c4, 0x11fc3c),
              (0x11fc50, 0x1201c8), (0x1201dc, 0x120760)]
    expected_ids = [
        [1045, 1039, 1040, 1048, 1023, 1036, 1021, 1023, 1021, 1021, 1034, 1023, 1052],
        [1046, 1041, 1042, 1049, 1024, 1037, 1024, 1026, 1024, 1024, 1034, 1024, 1052],
        [1047, 1043, 1044, 1050, 1031, 1038, 1026, 1028, 1024, 1026, 1034, 1026, 1052],
        [1047, 1043, 1044, 1051, 1032, 1038, 1028, 1030, 1032, 1028, 1034, 1028, 1052],
    ]
    banks = []
    for index, (lo, hi) in enumerate(groups):
        found = []
        for at in range(lo, hi, 4):
            word = pef._u32(app.code.data, at)
            if (word >> 26, word >> 21 & 31, word >> 16 & 31) == (14, 6, 0):
                identifier = word & 65535
                if identifier in resources:
                    found.append({'request_code': at, 'id': identifier, 'file': resources[identifier]})
        require([item['id'] for item in found], expected_ids[index], f'font bank {index}')
        banks.append(found)
    # Accessor uses the same 13-slot, 24-byte record, 312-byte bank layout.
    for at, op, expected in [(0x1383d8, 7, (0, 3, 24)), (0x1383dc, 7, (3, 4, 312)),
                              (0x1383e4, 14, (3, 3, 28)),
                              (0x128328, 32, (5, 3, 8)), (0x128338, 40, (0, 3, 0)),
                              (0x12834c, 40, (3, 3, 2)), (0x127fec, 44, (0, 3, 2)),
                              (0x127fdc, 14, (0, 0, 2))]:
        d_form(app, at, op, expected)
    for at, expected in [(0x171f6c, (0, 0, 0, 30, 30, 1)),
                         (0x171f8c, (0, 0, 0, 31, 31, 1)),
                         (0x171fac, (3, 0, 0, 30, 30, 1)),
                         (0x171fbc, (3, 0, 0, 31, 31, 1))]:
        mask_form(app, at, expected)
    x_form(app, 0x128350, 87, (0, 5, 0))
    x_form(app, 0x128354, 954, (0, 0, 0))
    # Popup rectangle is forwarded through factory to the base window's four
    # coordinate fields; no guessed screen-center label is used as a witness.
    for at, op, expected in [(0x141af0, 14, (7, 0, 574)), (0x141af4, 14, (9, 0, 1474)),
                              (0x141afc, 8, (0, 10, 1520)), (0x141b74, 14, (4, 0, 255)),
                              (0x17dca0, 44, (28, 24, 8)), (0x17dca4, 44, (29, 24, 10)),
                              (0x17dca8, 44, (30, 24, 12)), (0x17dcac, 44, (31, 24, 14)),
                              (0x141ad8, 15, (4, 0, -13)), (0x141ae0, 14, (6, 4, 21988))]:
        d_form(app, at, op, expected)
    for at, operation, expected in [(0xaa494, 266, (0, 0, 3)),
                                     (0xaa49c, 266, (0, 0, 4)),
                                     (0xaa4a4, 266, (0, 3, 0)),
                                     (0xaa4a8, 824, (0, 0, 2))]:
        x_form(app, at, operation, expected)
    require(branch(app, 0x141b08), 0x17fe78, 'popup window factory')
    require(branch(app, 0x141b98), 0x1739d4, 'popup drawing object factory')
    art_slot = 0x8000 - 17232
    art = relocation(app, art_slot, app.code.index)
    require(bytes(app.code.data[art:art + 9]), b'f_helpbg\0', 'popup art name')
    # State classification returns all six indexes, with disabled prioritized.
    for at, op, expected in [(0x171f68, 32, (0, 3, 68)), (0x171f7c, 34, (0, 3, 313)),
                              (0x171f88, 34, (0, 3, 312)),
                              (0x171f74, 14, (3, 0, 1)), (0x171f94, 14, (3, 0, 3)),
                              (0x171f9c, 14, (3, 0, 2)), (0x171fb4, 14, (3, 0, 4)),
                              (0x171fc4, 14, (3, 0, 5)), (0x171fcc, 14, (3, 0, 0)),
                              (0x13e7d8, 32, (6, 6, 12)), (0x13e808, 36, (6, 3, 124))]:
        d_form(app, at, op, expected)
    # Sign text uses OS measurement and drawing, then integer 2x2 coverage
    # reduction; this is distinct from OpenTPW's 16-subscanline rasterizer.
    for at, op, expected in [(0xaa08c, 14, (0, 0, 16)), (0xaa0b4, 44, (6, 27, 14)),
                              (0xaa078, 14, (6, 0, 8)), (0xaa0b8, 36, (5, 27, 16)),
                              (0xaa3fc, 14, (6, 28, 0)), (0xaa40c, 8, (4, 0, 256)),
                              (0xaa480, 34, (3, 9, 0)), (0xaa488, 34, (0, 8, 1)),
                              (0xaa48c, 34, (4, 9, 1)), (0xaa498, 34, (3, 8, 0)),
                              (0x18e728, 14, (0, 0, 128)), (0x18e744, 36, (0, 1, 1128)),
                              (0x18e748, 36, (0, 1, 1132))]:
        d_form(app, at, op, expected)
    imports = [import_at(app, branch(app, at)) for at in (0xaa1fc, 0xaa264, 0xaa29c, 0xaa410)]
    require([item['symbol'].split('__')[0] for item in imports],
            ['CreateDIBSection', 'CreateFontIndirect', 'GetTextExtentPoint', 'TextOut'], 'sign call path')
    require(branch(macdoze, 0x64c8), 0x9a58, 'Mac TextOut wrapper')
    backend = import_at(macdoze, branch(macdoze, 0x9b1c), toc=0)
    require(backend['symbol'], 'StdText', 'Mac QuickDraw text backend')
    # The embedded layout interpreter consumes signed big-endian halfwords,
    # combines low/high ID words and forwards its rectangle to the same factory.
    x_form(app, 0x181b4c, 343, (0, 30, 0))
    x_form(app, 0x181bcc, 279, (10, 30, 0))
    for at, expected in [(0x181bf0, (8, 6, 16, 0, 15, 0))]:
        # slwi is the immediate rotate/mask form used to assemble the high half.
        mask_form(app, at, expected)
    require(branch(app, 0x181c3c), 0x17fe78, 'layout opcode-zero window allocation')
    require(branch(app, 0x181c64), 0x181afc, 'layout recursive allocation')
    require(branch(app, 0x16f0c8), 0x138504, 'park rating label resource accessor')
    d_form(app, 0x16f078, 14, (4, 4, -21085))
    d_form(app, 0x16f0c4, 14, (3, 0, 190))
    layout = layout_table(bytes(app.data_section.data), 0x4f81c)
    require((len(layout['windows']), layout['words_consumed']), (27, 478), 'annual summary layout shape')
    by_id = {window['id']: window for window in layout['windows']}
    require(by_id[44450]['rectangle'], [901, 427, 1136, 472], 'park rating value rectangle')
    require(by_id[44451]['rectangle'], [462, 427, 859, 472], 'park rating label rectangle')
    meshes = wad(mac_ui)
    selected = ['b_buy.MD2', 'b_door.MD2', 'panel.MD2', 'f_helpbg.MD2', 'f_chat.MD2']
    summaries = {name: model_summary(meshes[name]) for name in selected}
    comparison = None
    if pc_ui:
        pc = wad(pc_ui)
        require(set(meshes), set(pc), 'Mac/PC UI member set')
        changed = sorted(name for name in meshes if meshes[name] != pc[name])
        require(changed, ['stexture/tpw_logo.wct', 'textures/tpw_logo.wct'], 'Mac/PC UI member differences')
        comparison = {'pc_ui_sha256': hashlib.sha256(pc_ui.read_bytes()).hexdigest(),
                      'members': len(meshes), 'changed': changed,
                      'identical_models': sum(name.lower().endswith('.md2') for name in meshes)}
    return {'identities': {'app': APP_SHA, 'macdoze': MACDOZE_SHA, 'mac_ui': MAC_UI_SHA,
                           'residx': hashlib.sha256(residx.read_bytes()).hexdigest()},
            'rtti_witnesses': slots, 'font_banks': banks,
            'font_spacing': {'signed_character_width_stride': 8, 'added_u16_spacing': 2,
                             'constructor_default_spacing': 2, 'pair_kerning_in_this_width_method': False},
            'popup': {'left': 574, 'right': 1474, 'bottom': 1520,
                      'height': '2 * font_slot_7_height * 1536 / drawable_height + 10',
                      'drawing_object': 'f_helpbg', 'text_rgba': [255, 255, 255, 255]},
            'state_indexes': {'ordinary': 0, 'disabled': 1, 'field313_only': 2,
                              'field313_and_field312_bit0': 3, 'field312_bit1': 4, 'field312_bit0': 5},
            'annual_summary_layout': layout,
            'rating_label': {'window_id': 44451, 'uitext_id': 190, 'font_slot': 6},
            'sign': {'call_path': imports, 'mac_backend': backend,
                     'caller_texture_dimensions': [128, 128], 'dib_scale_from_mask': 2,
                     'dib_bits_per_pixel': 8, 'dib_compression': 0,
                     'horizontal_origin': '256 - measured_text_width / 2',
                     'mask_reduce': 'floor((p00 + p01 + p10 + p11) / 4)'},
            'asset_models': summaries, 'edition_comparison': comparison,
            'resolved_approximation_ids': [],
            'limitations': ['Mac build only; no original runtime or pixel oracle.',
                            'Font bank screen-role names beyond identified slot consumers remain open.',
                            'Button bit event semantics and final sign compositing remain partially open.']}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('bin_root', type=Path)
    parser.add_argument('mac_ui', type=Path)
    parser.add_argument('residx', type=Path)
    parser.add_argument('--pc-ui', type=Path)
    args = parser.parse_args()
    try:
        result = inspect(args.bin_root, args.mac_ui, args.residx, args.pc_ui)
    except (OSError, ValueError, pef.PEFError) as error:
        parser.exit(1, f'UI evidence: {error}\n')
    print(json.dumps(result, sort_keys=True, indent=2))


if __name__ == '__main__':
    main()

"""Write or check the Theme Park Inc front-end layout tables from the Windows Game.exe of Theme Park Inc.

Theme Park Inc (Sim Coaster) is built on the same window builder as Theme Park World: its layout tables are the same
embedded command streams (grammar in ``tpw_exe_layouts``: commands 0 to 18, command word reader, rectangle reader).
The grammar was checked once, by hand, by reading all 86 layout tables of the executable with it (every stream ended exactly where
the next table starts); that scan is not part of this script, whose ``--check`` reproduces only the tables below. The executable is the no-CD ``Game.exe`` (SHA-256 below); the retail one is SafeDisc
encrypted, so its tables are not readable statically.

Phase 1 of the Theme Park Inc front end needs the first two tables; the in-game letter window the third; the mission window the
next two; the tag tube the sixth; the wage demand window and the wage negotiation dialog the last two:

* ``PersonnelFile`` 0x9C0FBC: the start menu. Builder 0x56ADB0 (``CreateWindow`` 0x84DEA0 over this table), handler
  0x568CE0/0x568D40. Window ``!window_s``, four ``filetab`` player slots, the ``filepage`` label areas, the name box
  (``filebox`` + ``filebutok``, text field 0xC758), the ``filebut`` buttons Enter Game (0xC754), Delete Player (0xC755)
  and Load A Game (0xC759), the Coaster Design Kit and Quit Game toggles and the ``mugshot`` picture.
* ``QuitConfirm`` 0x9B18BC: the generic confirm dialog every caller of builder 0x4F47F0 uses; its two buttons take
  their models (b_okay / b_cancel) from the caller's mode words (0x4F4937 and 0x4F49D4), so the table carries no model
  for them.
* ``LetterWindow`` 0x9B4854: the mail window the tag system opens on a letter (builder 0x526CD0, procedure 0x526960):
  the To/From/Subject/Date label and value pairs (0xA7FF/0xA800, 0xA7FB/0xA7FC, 0xA7F9/0xA7FA, 0xA803/0xA802), the
  sender's ``mugshot`` (0xA7FE in the frame 0xA7FD), the body frame 0xA801, the title 0xA806 and the ``b_close``,
  ``b_okay``, ``b_help`` and ``b_goto`` buttons (0xA804, 0xA805, 0xA807, 0xA808).
* ``MissionWindow`` 0x9BE808: the "Current Objective" window (builder 0x55A7F0, procedure 0x55BA70): the title 0xCEC6A, the
  Objectives and Challenges tabs (0xCEC59, 0xCEC58), the mission title 0xCEC56, the ``objectim`` picture (0xCEC55 in the
  frame 0xCEC54), the letter frame 0xCEC5A with the From/To/Subject pairs (0xCEC5B/0xCEC5C, 0xCEC60/0xCEC61,
  0xCEC5E/0xCEC5F) and the ``mugshot`` 0xCEC5D, the body scroll bar 0xCEC62, the obstacle group 0xCEC63 (staff list
  0xCEC64, lines 0xCEC65 to 0xCEC67), the page toggle 0xCEC69, Proceed 0xCEC68, help 0xCEC6B and the close and cancel
  buttons, which both carry the id -2.
* ``ChallengesWindow`` 0x9B54CC: the mission window's other tab (builder 0x529960); its Objectives tab 0xC1200 opens
  the mission window again.
* ``TagTube`` 0x9BBC84: the tag tube, the mail list window (builder 0x551CF0, procedure 0x5517B0): the title 0xBBB, the
  sorted ``!list`` 0xBBE with its two column headers (0x10, 0x11) and scroll parts, the detail ``!frame`` 0xBC1 with
  the From/To/Subject pairs (0xBC3/0xBC4, 0xBC7/0xBC8, 0xBC5/0xBC6), the ``mugshot`` 0xBC2, the body scroll bar 0xBC9,
  the footer 0xBCA and ``b_goto`` 0xBCB, the empty-list label 0xBCC and the ``b_close``, ``b_okay``, ``b_delete`` and
  ``b_help`` buttons (0xBB9, 0xBBA, 0xBBC, 0xBCD).
* ``WageDemand`` 0x9C9F64: the wage demand window a wage demand row's go-to opens (builder 0x59ABB0, procedure 0x59A900
  through 0x40CCAC): the title 0xCA199, the member's picture frame 0xCA198 and name frame 0xCA187, the demand text frame 0xCA186,
  the statistics frame 0xCA188 with its label and value pairs (0xCA18C..0xCA196) and the three bars (type 9: 0xCA189, 0xCA18A,
  0xCA18B), the "Your Offer" label 0xCA197 over the ``varibox`` 0xCA185 (``b_minus`` 2, ``b_plus`` 1 and its value field 3, the
  layout command 19), and the ``b_fire`` 0xCA183, ``b_okay`` 0xCA184, ``b_close`` 0xCA182, ``b_cancel`` (-2) and ``b_help``
  0xCA19A buttons.
* ``WageNegotiation`` 0x9BE518: the wage negotiation results dialog (builder 0x559C40, procedure 0x559860 through 0x559DA0):
  the title 0xB28A, the member's picture 0xB289, the body frame 0xB286 with the name label pair 0xB287/0xB288, the ``b_okay``
  0xB28B, ``b_close`` 0xB285 and ``b_help`` 0xB291 buttons, and the choice panel 0xB28C with its two ``checkbox2`` toggles
  (0xB28D accept the demand, 0xB28E let them leave) and their labels 0xB28F and 0xB290.

A model hash is the root-name hash of ``UiModels.RootNameKey``; the names are resolved from the game's ui.wad and an
unresolved or ambiguous hash is an error. Rectangles are (left, top, right, bottom) in the 2048 x 1536 virtual canvas.
Only derived values are written: rectangles, help ids, ids, attributes and model names.

Usage: python3 tpi_exe_layouts.py <Game.exe> <ui.wad> --write FILE | --check FILE
"""
from __future__ import annotations

import argparse
import hashlib
from pathlib import Path
import sys

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / 'advisor'))
sys.path.insert(0, str(Path(__file__).resolve().parent))
from tpw_exe_tables import Image  # noqa: E402
import tpw_exe_layouts as base  # noqa: E402

TPI_SHA = '77ea0c41410aad7c94a5eda658e31cb4ac05f7253aaf6746ad5e8a90afbd967a'

# name, address, root id, root rectangle, address after the last command of the stream, count of nodes
TABLES = [
    ('PersonnelFile', 0x9C0FBC, 0xC738, (273, 72, 1775, 1464), 0x9C15AC, 39),
    ('QuitConfirm', 0x9B18BC, 0xEA60, (499, 232, 1518, 830), 0x9B197E, 5),
    ('LetterWindow', 0x9B4854, 0xA7F8, (46, 44, 2002, 1492), 0x9B4ADE, 17),
    ('MissionWindow', 0x9BE808, 0xCEC52, (196, 38, 2028, 1318), 0x9BECCC, 34),
    ('ChallengesWindow', 0x9B54CC, 0xC11FB, (196, 38, 2028, 1318), 0x9B5924, 30),
    ('TagTube', 0x9BBC84, 0xBB8, (196, 38, 2028, 1318), 0x9BC08C, 31),
    ('WageDemand', 0x9C9F64, 0xCA181, (453, 191, 1771, 1165), 0x9CA38A, 30),
    ('WageNegotiation', 0x9BE518, 0xB284, (453, 430, 1772, 926), 0x9BE740, 14),
]

# Controls the C# reads by model name or id; a table that lacks one is an error.
REQUIRED_MODELS = {
    'PersonnelFile': ['!window_s', 'filetab', 'filepage', 'filebut', 'filebox', 'filebutok', 'mugshot', 'b_ltoggle10', '!f_plain', '!frame'],
    'QuitConfirm': ['!window', 'b_close'],
    'LetterWindow': ['!window', '!frame', 'mugshot', 'b_close', 'b_okay', 'b_help', 'b_goto'],
    'MissionWindow': ['!window', '!f_plain', '!frame', 'objectim', 'mugshot', 'tab_text', 'b_close', 'b_cancel', 'b_help', 'b_ltoggle10',
                      'b_updown', 'b_vscrollup', 'b_vscroller', 'b_vscrolldn'],
    'ChallengesWindow': ['!window', 'tab_text', '!listnarrow', 'b_ltoggle10'],
    'TagTube': ['!window', '!f_plain', '!frame', '!list', 'mugshot', 'b_close', 'b_okay', 'b_delete', 'b_goto', 'b_help',
                'b_vscrollup', 'b_vscroller', 'b_vscrolldn'],
    'WageDemand': ['!window', '!frame', 'varibox', 'b_minus', 'b_plus', 'b_fire', 'b_okay', 'b_close', 'b_cancel', 'b_help'],
    'WageNegotiation': ['!window', '!frame', '!f_plain', 'checkbox2', 'b_okay', 'b_close', 'b_help'],
}
REQUIRED_IDS = {
    'PersonnelFile': [0xC73D, 0xC73E, 0xC73F, 0xC740, 0xC754, 0xC755, 0xC759, 0xC73A, 0xC75B, 0xC758, 0xC757, 0xC75C, 0xC75E],
    'QuitConfirm': [0xEA61, 0xEA62, 0xEA63, 0xEA64],
    'LetterWindow': list(range(0xA7F9, 0xA809)),
    'MissionWindow': list(range(0xCEC53, 0xCEC6C)) + [0xFFFFFFFE],
    'ChallengesWindow': [0xC1200, 0xC1202, 0xC120E],
    'TagTube': list(range(0xBB8, 0xBCE)) + [0x10, 0x11],
    'WageDemand': list(range(0xCA181, 0xCA19B)) + [1, 2, 3, 0xFFFFFFFE],
    'WageNegotiation': list(range(0xB284, 0xB292)),
}


def render(tables: dict[str, tuple[int, list[dict]]], names: dict[int, str]) -> str:
    lines = [
        '// <auto-generated>',
        '// Generated by tools/ppc-analysis/lanes/ui/tpi_exe_layouts.py from the no-CD Theme Park Inc executable',
        f'// (Game.exe, SHA-256 {TPI_SHA}); do not edit. Derived values only: rectangles, help ids, ids, model names.',
        '// </auto-generated>',
        'namespace OpenTPW.UI.Original;',
        '',
        '/// <summary>The Theme Park Inc front-end layout tables of its executable (see <see cref="LayoutNode"/>).</summary>',
        'public static class ThemeParkIncLayoutTables',
        '{',
    ]
    for name, (address, roots) in tables.items():
        lines.append(f'\t/// <summary>[BIN:TPI-EXE:{address:#x}] layout table {name}.</summary>')
        lines.append(f'\tpublic static readonly LayoutTable {name} = new( "{name}", 0x{address:X}, new LayoutNode[]')
        lines.append('\t{')
        lines.append(',\n'.join(base.render_node(root, names, name, 2) for root in roots) + ',')
        lines.append('\t} );')
        lines.append('')
    lines[-1:] = ['}', '']
    return '\n'.join(lines)


def build(exe: bytes, wad: bytes) -> str:
    digest = hashlib.sha256(exe).hexdigest()
    if digest != TPI_SHA:
        raise ValueError(f'Game.exe has SHA-256 {digest}, expected {TPI_SHA}')
    image = Image(exe)
    names = base.ui_root_names(wad)
    tables: dict[str, tuple[int, list[dict]]] = {}
    for name, address, root_id, rect, end, count in TABLES:
        roots, after = base.parse_layout(image, address)
        first = roots[0]
        flat = base.flatten(roots)
        if after != end or first['id'] != root_id or tuple(first['rect']) != rect or len(flat) != count:
            raise ValueError(f'{name} at {address:#x} changed: end {after:#x}, root {first["id"]:#x} {first["rect"]}, {len(flat)} nodes')
        models = {base.resolve(node, names, name) for node in flat}
        for required in REQUIRED_MODELS[name]:
            if required not in models:
                raise ValueError(f'{name} has no control with model {required}')
        ids = {node['id'] for node in flat}
        for required in REQUIRED_IDS[name]:
            if required not in ids:
                raise ValueError(f'{name} has no control {required:#x}')
        tables[name] = (address, roots)
    return render(tables, names)


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument('exe', type=Path)
    parser.add_argument('ui_wad', type=Path)
    group = parser.add_mutually_exclusive_group(required=True)
    group.add_argument('--write', type=Path)
    group.add_argument('--check', type=Path)
    args = parser.parse_args()
    text = build(args.exe.read_bytes(), args.ui_wad.read_bytes())
    path = args.write or args.check
    if args.write:
        path.write_text(text, encoding='utf-8', newline='\n')
        print(f'wrote {path}')
    elif path.read_text(encoding='utf-8') != text:
        raise SystemExit(f'{path} is not what the generator writes')
    else:
        print(f'{path} matches')


if __name__ == '__main__':
    main()

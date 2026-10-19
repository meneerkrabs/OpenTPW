"""Write or check the original UI layout tables and pointer tables from the Windows tp.exe.

The Windows v2.0 executable (SHA-256 below) holds the PC twins of the Mac layout tables: embedded command
streams that its window builder 0x65FD58 interprets (command word reader 0x65FC18, rectangle reader 0x65FC38,
jump table 0x66076E with the commands 0 to 18). The command grammar is the one of ``corpus.layout_table`` (the Mac
twin) and was checked against the PC dispatcher instruction by instruction:

* 0 type, attributes, id, rectangle: a child window (builder 0x65DD7B), followed by its own commands up to a 5;
* 1 and 2: root-name hash of the first and second model (window +0x98 and +0x9C), 3 and 10: inner rectangles,
  4: hit geometry, 11 and 13: value tables, 17: help id (+0x90), 18: second help id (+0x94), 5: end of the children;
* 6, 7, 8, 9, 12, 14, 15, 16: implied children (type, attributes and id fixed by the command; 12 and 16 read a number).

A model hash is the root-name hash of ``UiModels.RootNameKey`` ((h ^ c) * 47 over the Latin-1 bytes). The names are
resolved from the game's ui.wad, and an unresolved or ambiguous hash is an error. Rectangles are (left, top, right,
bottom) in the 2048 x 1536 virtual canvas.

The pointer part reads the cursor loader 0x4899C0 (the .tga file of each pointer id) and the tool-to-pointer
switch 0x52F950 (byte table 0x52FA48 over the tool mode, jump table 0x52FA1C, five blocks of flag tests and pushes).

Only derived values are written: rectangles, help ids, model names, pointer ids and file names, and the pointer
rule per tool mode. Usage: python3 tpw_exe_layouts.py <tp.exe> <ui.wad> --write FILE | --check FILE
"""
from __future__ import annotations

import argparse
import hashlib
from pathlib import Path
import re
import struct
import sys

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / 'advisor'))
from tpw_exe_tables import Image  # noqa: E402

TPW_SHA = '5472f97b5eb28f58b5a49f3a15f6f66adffa6b2625a623f503202c9696d1b805'

# name, address, root id, root rectangle, address after the last command of the stream, count of nodes
TABLES = [
    ('RideWindow', 0x755150, 0x3E14, (248, 30, 1800, 1007), 0x755750, 42),
    ('RideUpgradeDialog', 0x755750, 0x1F4C, (500, 233, 1516, 830), 0x75583A, 8),
    ('ShopWindow', 0x755908, 0xC066, (248, 30, 1800, 1007), 0x755E80, 41),
    ('SideshowWindow', 0x755E80, 0xA088, (248, 30, 1800, 1007), 0x7563FA, 40),
    ('MessagePanel', 0x7501A0, 0x10D2D, (458, 1101, 1073, 1442), 0x7502B2, 7),
    ('CameraPanel', 0x751720, 0x62, (466, 1316, 785, 1469), 0x751794, 3),
    ('Camcorder', 0x74FA98, 0x89DB52, (170, 170, 1878, 1366), 0x74FAEA, 2),
    ('ConfirmDialog', 0x7583E8, 0x48, (500, 233, 1516, 830), 0x75852C, 8),
    ('NameDialog', 0x753F50, 0x708, (500, 233, 1516, 830), 0x7540BE, 9),
    ('Lobby', 0x757F60, 0x1E0E7, (28, 855, 476, 1521), 0x75817A, 11),
    ('LobbyCity', 0x757D88, 0x49D4, (28, 541, 476, 1521), 0x757F5A, 9),
    ('TrainingBudget', 0x756400, 0x2D9, (208, 30, 1840, 1007), 0x756944, 40),
    ('EmployeeWindow', 0x756F48, 0x50A, (328, 130, 1720, 901), 0x7572E4, 25),
    ('EmployeeRenameDialog', 0x7572E8, 0x5DA9, (500, 233, 1516, 830), 0x7573C2, 5),
    ('VisitorInfoWindow', 0x7575E0, 0x371F, (328, 130, 1720, 901), 0x7579C8, 27),
    ('PlayerScreen', 0x753C68, 0x7A13, (0, 0, 2048, 1536), 0x753F4E, 19),
    ('HudArm', 0x7529DC, 0x21, (331, 1069, 1006, 1495), 0x752C60, 12),
    ('VisitorsWindow', 0x7506C8, 0x1E496, (186, 30, 2018, 1007), 0x7508B2, 17),
    ('AttractionsWindow', 0x7508E0, 0x12C4B7, (186, 30, 2018, 1007), 0x750AB6, 12),
    ('RideShopList', 0x750AB8, 0x190, (266, 318, 1825, 876), 0x750BA0, 10),
    ('SideshowList', 0x750BA0, 0x190, (261, 318, 1825, 876), 0x750C9A, 11),
    ('FeatureList', 0x750CA0, 0x190, (266, 318, 1825, 876), 0x750D52, 7),
    ('LocatorPanels', 0x756950, 0x95C, (474, 1159, 941, 1470), 0x756AD6, 10),
    ('LocatorStrip', 0x756AD8, 0x146, (448, 964, 1074, 1066), 0x756B96, 5),
    ('LoansWindow', 0x7525A0, 0x12C4B9, (186, 30, 2018, 1007), 0x752934, 29),
    ('LoadDialog', 0x7523F0, 0x23BCDF, (248, 30, 1800, 1007), 0x752564, 10),
    ('PostcardWindow', 0x7540D8, 0x5D9D, (186, 30, 2018, 1007), 0x7542B4, 11),
    ('ParkInfoWindow', 0x7537D0, 0x4A68BA, (208, 30, 1840, 1007), 0x753C48, 28),
    ('ChatOverlay', 0x758AE8, 0xC3122, (0, 0, 2048, 1536), 0x758CD6, 17),
    ('ChatBuddyPopup', 0x758CD8, 0x1788, (0, 0, 2048, 1536), 0x758E14, 8),
    ('ChatPlayerPopup', 0x758E18, 0x3002, (0, 0, 2048, 1536), 0x758F04, 6),
]
# The second confirm-dialog table must be the same stream, byte for byte in its decoded tree.
CONFIRM_TWIN = 0x7585A8
CONFIRM_TWIN_END = 0x7586EC

# Controls the C# reads by model name; a table that lacks one is an error.
REQUIRED = {
    'RideWindow': ['b_queue', 'b_ride it!', 'b_callmech', 'b_upgrade', 'b_door', 'b_move', 'b_track', 'b_erase', 'b_allthings', 'b_arup', 'b_ardown', 'b_okay'],
    'RideUpgradeDialog': ['wdialogw', 'b_exit', 'upgrades', 'b_up', 'b_down', 'b_scroller'],
    'ShopWindow': ['b_erase', 'b_move', 'b_applyall', 'b_door', 'b_allthings', 'b_arup', 'b_ardown', 'b_okay'],
    'SideshowWindow': ['b_erase', 'b_move', 'b_applyall', 'b_door', 'b_allthings', 'b_arup', 'b_ardown', 'b_okay'],
    'MessagePanel': ['mb_erase'],
    'CameraPanel': ['b_1person', 'b_postcard'],
    'Camcorder': ['viewfinder', 'b_eject'],
    'ConfirmDialog': ['wdialogw', 'b_okay', 'b_exit'],
    'NameDialog': ['wdialogw', 'b_okay', 'b_exit'],
    'Lobby': ['islandlob', 'b_entpark', 'b_worldview', 'b_lobleft', 'b_lobright'],
    'TrainingBudget': ['window3', 'b_okay', 'b_entry', 'b_finance', 'b_loans', 'f_train', 'b_minus', 'b_plus', 'h_handy', 'h_mech', 'h_enter', 'h_guard', 'h_resrcher'],
    'EmployeeWindow': ['window1', 'b_fire', 'b_pick', 'b_okay', 'b_arup', 'b_ardown', 'b_route', 'b_allstaff', 'b_locate'],
    'EmployeeRenameDialog': ['wdialogw', '!frame', 'b_okay', 'b_exit'],
    'VisitorInfoWindow': ['window1', 'b_okay', 'b_arup', 'b_ardown', 'b_kids', 'b_locate', 'Ethoughts3', 'ti_hungry'],
    'PlayerScreen': ['b_login', 'b_dellog', 'e_joy', 'tpwlogo'],
    'HudArm': ['b_retract', 'b_buy', 'b_info', 'b_money', 'b_resrch', 'b_map', 'b_camera'],
    'VisitorsWindow': ['window4', 'list_kids', 'b_okay', 'b_pkinfo', 'b_allstaff', 'b_allthings', 'b_up', 'b_down', 'b_scroller'],
    'LobbyCity': ['citylob', 'b_listpark', 'b_bck2lob', 'b_worldview', 'b_entpark', 'b_lobleft', 'b_lobright'],
    'AttractionsWindow': ['window4', 'list_all', 'b_sride', 'b_sshow', 'b_sshop', 'b_sfeature', 'b_okay', 'b_pkinfo', 'b_allstaff', 'b_kids'],
    'RideShopList': ['b_up', 'b_down', 'b_scroller'],
    'SideshowList': ['b_up', 'b_down', 'b_scroller'],
    'FeatureList': ['b_up', 'b_down', 'b_scroller'],
    'LocatorPanels': ['!f_plain', 'b_shandy', 'b_smech', 'b_sresrcher', 'b_senter', 'b_sguard', 'b_skids'],
    'LocatorStrip': ['!frame', 'staffstat', 'ti_hungry'],
    'LoansWindow': ['window4', 'f_loans', '!frame', 'b_sloan', 'b_spayup', 'b_up', 'b_down', 'b_scroller', 'b_exit', 'b_finance', 'b_entry',
                    'b_staffcost'],
    'LoadDialog': ['window2', 'f_load', '!frame', 'b_up', 'b_down', 'b_scroller', 'b_exit', 'b_okay'],
    'PostcardWindow': ['window4', '!frame', 'b_address', 'b_clear', 'b_preview', 'b_okay', 'b_exit'],
    'ParkInfoWindow': ['window3', 'b_okay', 'graphpark', 'toughts', 'ti_hungry', 'b_kids', 'b_allstaff', 'b_allthings'],
    'ChatOverlay': ['chat', 'b_up', 'b_down', 'b_scroller', 'b_speeps', 'b_sbuddi', 'b_muff', 'b_vote', 'b_chtpcard'],
    'ChatBuddyPopup': ['!f_plain', 'b_bmark', 'b_gopeep', 'b_ignore', 'b_unbuddi', 'b_peeploc', 'b_page'],
    'ChatPlayerPopup': ['!f_plain', 'b_bmark', 'b_gopeep', 'b_ignore', 'b_unbuddi'],
}

LOADER = 0x4899C0
LOADER_LENGTH = 0x2B0
POINTER_COUNT = 22
TOOL_SWITCH = 0x52F950
TOOL_JUMPS = 0x52FA1C
TOOL_JUMP_COUNT = 11
TOOL_BYTES = 0x52FA48
TOOL_MODES = 0x3C
SET_POINTER = 0x4A2AA0
NOPLACE_TEST = 0x527950
CASH_FLAG = 0x816D5C
FLAGS = {0x816D60: 'Link', 0x816D4C: 'End'}

HASH_LIMIT = 0xFFFFFFFF


def root_name_hash(name: str) -> int:
    value = 0
    for byte in name.encode('latin1'):
        signed = byte - 256 if byte > 127 else byte
        value = ((value ^ (signed & 0xFFFFFFFF)) * 47) & 0xFFFFFFFF
    return value


# ---------------------------------------------------------------------------------------------------------------
# layout streams

class Stream:
    def __init__(self, image: Image, address: int):
        self.image, self.position = image, address

    def u16(self) -> int:
        value = struct.unpack('<H', self.image.read(self.position, 2))[0]
        self.position += 2
        return value

    def s16(self) -> int:
        value = self.u16()
        return value - 0x10000 if value & 0x8000 else value

    def u32(self) -> int:
        low = self.u16()
        return low | self.u16() << 16

    def s32(self) -> int:
        value = self.u32()
        return value - 0x100000000 if value & 0x80000000 else value


IMPLIED = {6: (2, 0x21, 1), 7: (2, 0x21, 2), 8: (2, 1, 3), 9: (3, 0x10, 1), 12: (2, 1, None), 14: (2, 0x21, 1), 15: (2, 0x21, 2), 16: (6, 1, None)}


def parse_layout(image: Image, address: int) -> tuple[list[dict], int]:
    """The nodes of one layout stream and the address after its last command."""
    stream = Stream(image, address)
    count = 0
    external: dict = {}

    def walk(parent: dict | None, depth: int) -> list[dict]:
        nonlocal count
        nodes: list[dict] = []
        if depth > 16:
            raise ValueError(f'layout nesting too deep at {stream.position:#x}')
        while True:
            at = stream.position
            command = stream.u16()
            if command == 0 or command in IMPLIED:
                count += 1
                if count > 512:
                    raise ValueError('layout has too many nodes')
                if command == 0:
                    kind, attributes, identifier = stream.u16(), stream.u32(), stream.u32()
                    node_id = identifier
                else:
                    kind, attributes, node_id = IMPLIED[command]
                    if command in (12, 16):
                        number = stream.s16()
                        node_id = number + 16 if command == 12 else 0x11100000 | number
                rect = [stream.s16() for _ in range(4)]
                node = {'type': kind, 'attributes': attributes, 'id': node_id, 'rect': rect, 'help': -1, 'help2': -1,
                        'hash': None, 'hash2': None, 'inner': None, 'implied': command, 'at': at, 'children': []}
                nodes.append(node)
                node['children'] = walk(node, depth + 1)
                continue
            if command == 5:
                return nodes
            if parent is None:
                # properties of the window the stream is applied to (not a node of the stream)
                parent = external
            if command == 1:
                parent['hash'] = stream.u32()
            elif command == 2:
                parent['hash2'] = stream.u32()
            elif command == 3:
                parent['inner'] = [stream.s16() for _ in range(4)]
            elif command == 4:
                subtype = stream.u16()
                if subtype == 4:
                    for _ in range(stream.s16() * 2):
                        stream.s16()
                elif subtype in (1, 3):
                    for _ in range(4):
                        stream.s16()
                elif subtype == 2:
                    for _ in range(3):
                        stream.s16()
                else:
                    raise ValueError(f'unknown geometry subtype {subtype} at {at:#x}')
            elif command == 10:
                for _ in range(4):
                    stream.s16()
            elif command == 11:
                for _ in range(stream.s16() * 2):
                    stream.s16()
            elif command == 13:
                stream.s16(), stream.s16()
            elif command == 17:
                parent['help'] = stream.s32()
            elif command == 18:
                parent['help2'] = stream.s32()
            else:
                raise ValueError(f'unsupported layout command {command} at {at:#x}')

    roots = walk(None, 0)
    return roots, stream.position


def flatten(nodes: list[dict]) -> list[dict]:
    result: list[dict] = []
    for node in nodes:
        result.append(node)
        result.extend(flatten(node['children']))
    return result


def same_shape(left: list[dict], right: list[dict]) -> bool:
    keys = ('type', 'attributes', 'id', 'rect', 'help', 'help2', 'hash', 'hash2', 'inner')
    first, second = flatten(left), flatten(right)
    return len(first) == len(second) and all(all(a[key] == b[key] for key in keys) for a, b in zip(first, second))


# ---------------------------------------------------------------------------------------------------------------
# ui.wad root names

def refpack(data: bytes) -> bytes:
    out = bytearray()
    position, length = 5, len(data)
    while position < length - 1:
        first = data[position]
        if first & 0x80 == 0:
            literal, copy = first & 3, ((first & 0x1C) >> 2) + 3
            distance = ((first & 0x60) << 3) + data[position + 1] + 1
            out += data[position + 2:position + 2 + literal]
            position += 2 + literal
        elif first & 0xC0 == 0x80:
            literal, copy = (data[position + 1] & 0xC0) >> 6, (first & 0x3F) + 4
            distance = ((data[position + 1] & 0x3F) << 8) + data[position + 2] + 1
            out += data[position + 3:position + 3 + literal]
            position += 3 + literal
        elif first & 0xE0 == 0xC0:
            literal = first & 3
            copy = ((first & 0x0C) << 6) + data[position + 3] + 5
            distance = ((first & 0x10) << 12) + (data[position + 1] << 8) + data[position + 2] + 1
            out += data[position + 4:position + 4 + literal]
            position += 4 + literal
        else:
            run = ((first & 0x1F) + 1) << 2
            if run <= 0x70:
                out += data[position + 1:position + 1 + run]
                position += 1 + run
                continue
            literal = first & 3
            out += data[position + 1:position + 1 + literal]
            break
        start = len(out) - distance
        if start < 0:
            raise ValueError('refpack back reference before the start')
        for index in range(copy):
            out.append(out[start + index])
    return bytes(out)


def ui_root_names(wad: bytes) -> dict[int, str]:
    """Root-node name of every ui.wad model by its hash; two names with one hash are an error."""
    if wad[:4] != b'DWFB':
        raise ValueError('ui.wad is not a DWFB archive')
    names: dict[int, str] = {}
    count = struct.unpack_from('<i', wad, 72)[0]
    for index in range(count):
        _, name_offset, name_length, data_offset, data_length, compression, _ = struct.unpack_from('<7I', wad, 88 + index * 40)
        file_name = wad[name_offset:name_offset + name_length - 1].decode('latin1')
        if not file_name.lower().endswith('.md2'):
            continue
        data = wad[data_offset:data_offset + data_length]
        if compression == 4:
            data = refpack(data)
        if len(data) <= 0xB0:
            continue
        offsets = struct.unpack_from('<12I', data, 0x50)
        root = offsets[10]
        if root + 88 > len(data):
            continue
        pointer = struct.unpack_from('<I', data, root + 84)[0]
        end = data.find(b'\0', pointer)
        if pointer == 0 or end < 0:
            continue
        name = data[pointer:end].decode('latin1')
        key = root_name_hash(name)
        if key in names and names[key] != name:
            raise ValueError(f'ui.wad root names {names[key]!r} and {name!r} share hash {key:#x}')
        names[key] = name
    return names


# ---------------------------------------------------------------------------------------------------------------
# pointers

def pointer_files(image: Image) -> dict[int, str]:
    """The .tga file of each pointer id, from the loader's (string, id) call pairs."""
    code = image.read(LOADER, LOADER_LENGTH)
    patterns = [
        re.compile(rb'\x68(....)\xe8....\x83\xc4\x04\x85\xc0\x74.\x50\x6a(.)\xb9\xb0\x9a\x7b\x00\xe8....', re.S),
        re.compile(rb'\x68(....)\xe8....\x8b\xf0\x83\xc4\x04\x85\xf6\x74.\x56\x6a(.)\xb9\xb0\x9a\x7b\x00\xe8....', re.S),
    ]
    found: dict[int, str] = {}
    for pattern in patterns:
        for match in pattern.finditer(code):
            address = struct.unpack('<I', match.group(1))[0]
            text = image.read(address, 32)
            name = text[:text.index(0)].decode('ascii')
            identifier = match.group(2)[0]
            if identifier in found:
                raise ValueError(f'pointer id {identifier} loaded twice')
            found[identifier] = name
    missing = set(range(POINTER_COUNT)) - set(found)
    # the carry pointer reuses the pick pointer's load shape (esi variant) and every id has a file name
    if missing or any(not re.fullmatch(r'C[A-Za-z]{3}\.tga', name) for name in found.values()):
        raise ValueError(f'pointer loader did not yield ids 0..{POINTER_COUNT - 1}: missing {sorted(missing)}, got {found}')
    return dict(sorted(found.items()))


def tool_block(image: Image, address: int, call: int) -> tuple[list[tuple[str, int]], int]:
    """A switch block as ordered (flag, pointer) overrides and its default pointer.

    Decodes only mov eax,[flag]; test eax,eax; je; push imm8; jmp call, ending in push imm8 (then the call
    0x4A2AA0, directly or through a jmp); anything else is an error."""
    overrides: list[tuple[str, int]] = []
    position, flag = address, None
    for _ in range(64):
        code = image.read(position, 8)
        if code[0] == 0xA1:
            flag = struct.unpack_from('<I', code, 1)[0]
            if flag not in FLAGS:
                raise ValueError(f'unknown tool flag {flag:#x} at {position:#x}')
            position += 5
        elif code[:2] == b'\x85\xc0':
            position += 2
        elif code[0] == 0x74:
            push = image.read(position + 2, 4)
            if flag is None or push[0] != 0x6A or push[2] != 0xEB or position + 6 + struct.unpack_from('b', push, 3)[0] != call:
                raise ValueError(f'unexpected branch shape at {position:#x}')
            overrides.append((FLAGS[flag], push[1]))
            position += 2 + code[1]
        elif code[0] == 0x6A:
            after = code[2]
            if after == 0xEB and position + 4 + struct.unpack_from('b', code, 3)[0] == call:
                return overrides, code[1]
            if after == 0xE8 and position + 2 == call:
                return overrides, code[1]
            raise ValueError(f'push at {position:#x} does not reach the pointer call')
        else:
            raise ValueError(f'unsupported tool block byte {code[0]:#04x} at {position:#x}')
    raise ValueError('tool block did not end')


def tool_rules(image: Image) -> list[tuple[int, list[tuple[str, int]], int]]:
    """(tool mode, ordered (flag, pointer) overrides, default pointer) for the tool modes that choose one."""
    jumps = [struct.unpack('<I', image.read(TOOL_JUMPS + 4 * index, 4))[0] for index in range(TOOL_JUMP_COUNT)]
    table = image.read(TOOL_BYTES, TOOL_MODES)
    none_target = jumps[0]
    # the tail shared by every path: noplace when 0x527950 says so, then cash when 0x816D5C is set
    tail = image.read(none_target, 32)
    noplace = re.match(rb'\xe8(....)\x85\xc0\x74.\x6a\x08\xe8....\x83\xc4\x04\xa1(....)\x85\xc0\x74.\x6a\x14\xe8', tail, re.S)
    if not noplace:
        raise ValueError('tool switch tail is not the noplace/cash tail')
    call = none_target + 5 + struct.unpack('<i', noplace.group(1))[0]
    if call != NOPLACE_TEST or struct.unpack('<I', noplace.group(2))[0] != CASH_FLAG:
        raise ValueError('tool switch tail tests unexpected data')
    call_address = 0x52F9EB
    check = image.read(call_address, 5)
    if check[0] != 0xE8 or call_address + 5 + struct.unpack_from('<i', check, 1)[0] != SET_POINTER:
        raise ValueError('the shared pointer call is not 0x4A2AA0')
    code = image.read(TOOL_SWITCH, 32)
    if not code.startswith(b'\xa1\x2c\xae\x81\x00\x83\xf8\x3b\x0f\x87'):
        raise ValueError('tool switch prologue changed')
    rules = []
    for mode, selector in enumerate(table):
        if selector >= TOOL_JUMP_COUNT:
            raise ValueError(f'tool mode {mode} selects jump {selector}')
        target = jumps[selector]
        if target == none_target:
            continue
        overrides, default = tool_block(image, target, call_address)
        rules.append((mode, overrides, default))
    return rules


# ---------------------------------------------------------------------------------------------------------------
# rendering

def resolve(node: dict, names: dict[int, str], table: str, key: str = 'hash') -> str | None:
    if node[key] is None:
        return None
    if node[key] not in names:
        raise ValueError(f'{table}: model hash {node[key]:#x} of control {node["id"]:#x} is not a ui.wad root name')
    return names[node[key]]


def cs_string(text: str | None) -> str:
    return 'null' if text is None else '"' + text.replace('\\', '\\\\').replace('"', '\\"') + '"'


def render_node(node: dict, names: dict[int, str], table: str, indent: int) -> str:
    pad = '\t' * indent
    left, top, right, bottom = node['rect']
    inner = 'null' if node['inner'] is None else 'new[] { ' + ', '.join(str(value) for value in node['inner']) + ' }'
    children = node['children']
    child_text = 'System.Array.Empty<LayoutNode>()' if not children else (
        'new LayoutNode[]\n' + pad + '{\n' + ',\n'.join(render_node(child, names, table, indent + 1) for child in children) + ',\n' + pad + '}')
    node_id = node['id']
    id_text = f'0x{node_id:X}u'
    return (f'{pad}new( {node["type"]}, 0x{node["attributes"]:X}u, {id_text}, {left}, {top}, {right}, {bottom}, {node["help"]}, {node["help2"]}, '
            f'{cs_string(resolve(node, names, table))}, {cs_string(resolve(node, names, table, "hash2"))}, {inner}, {child_text} )')


def render(tables: dict[str, tuple[int, list[dict]]], pointers: dict[int, str], rules: list[tuple[int, list[tuple[str, int]], int]],
           names: dict[int, str]) -> str:
    lines = [
        '// <auto-generated>',
        '// Generated by tools/ppc-analysis/lanes/ui/tpw_exe_layouts.py from the Windows Sim Theme Park World executable',
        f'// (tp.exe v2.0, SHA-256 {TPW_SHA}); do not edit. Derived values only: rectangles, help ids, model names, pointer ids.',
        '// </auto-generated>',
        'namespace OpenTPW.UI.Original;',
        '',
        '/// <summary>The original UI layout tables of the Windows executable (see <see cref="LayoutNode"/>).</summary>',
        'public static class OriginalLayoutTables',
        '{',
    ]
    for name, (address, roots) in tables.items():
        lines.append(f'\t/// <summary>[BIN:TPW-EXE:{address:#x}] layout table {name}.</summary>')
        lines.append(f'\tpublic static readonly LayoutTable {name} = new( "{name}", 0x{address:X}, new LayoutNode[]')
        lines.append('\t{')
        lines.append(',\n'.join(render_node(root, names, name, 2) for root in roots) + ',')
        lines.append('\t} );')
        lines.append('')
    lines[-1:] = ['}', '', '/// <summary>The original game pointers: their ids and .tga files, and the tool-to-pointer switch.</summary>', 'public static class OriginalPointerTables', '{',
                  '\t/// <summary>[BIN:TPW-EXE:0x4899c0] the file of each pointer id of the cursor loader (the .ani set shares the ids).</summary>',
                  '\tpublic static readonly (int Id, string File)[] Files =', '\t{']
    for identifier, file in pointers.items():
        lines.append(f'\t\t( {identifier}, "{file}" ),')
    lines += ['\t};', '',
              '\t/// <summary>[BIN:TPW-EXE:0x52f950] per tool mode (byte table 0x52fa48): the ordered flag overrides and the default pointer.</summary>',
              '\tpublic static readonly (int Mode, (ToolPointerFlag Flag, int Pointer)[] Overrides, int Default)[] ToolRules =', '\t{']
    for mode, overrides, default in rules:
        items = ', '.join(f'( ToolPointerFlag.{flag}, {pointer} )' for flag, pointer in overrides)
        array = f'new (ToolPointerFlag, int)[] {{ {items} }}' if items else 'System.Array.Empty<(ToolPointerFlag, int)>()'
        lines.append(f'\t\t( {mode}, {array}, {default} ),')
    lines += ['\t};', '}', '']
    return '\n'.join(lines)


def build(exe: bytes, wad: bytes) -> str:
    digest = hashlib.sha256(exe).hexdigest()
    if digest != TPW_SHA:
        raise ValueError(f'tp.exe has SHA-256 {digest}, expected {TPW_SHA}')
    image = Image(exe)
    names = ui_root_names(wad)
    tables: dict[str, tuple[int, list[dict]]] = {}
    for name, address, root_id, rect, end, nodes in TABLES:
        roots, after = parse_layout(image, address)
        first = roots[0]
        flat = flatten(roots)
        if after != end or first['id'] != root_id or tuple(first['rect']) != rect or len(flat) != nodes:
            raise ValueError(f'{name} at {address:#x} changed: end {after:#x}, root {first["id"]:#x} {first["rect"]}, {len(flat)} nodes')
        models = {resolve(node, names, name) for node in flat}
        for required in REQUIRED[name]:
            if required not in models:
                raise ValueError(f'{name} has no control with model {required}')
        tables[name] = (address, roots)
    twin, twin_end = parse_layout(image, CONFIRM_TWIN)
    if twin_end != CONFIRM_TWIN_END or not same_shape(twin, tables['ConfirmDialog'][1]):
        raise ValueError('the second confirm-dialog table differs from the first')
    pointers = pointer_files(image)
    rules = tool_rules(image)
    modes = {mode for mode, _, _ in rules}
    if modes != {1, 3, 4, 11, 16, 21, 22, 57, 59}:
        raise ValueError(f'tool modes with a pointer changed: {sorted(modes)}')
    return render(tables, pointers, rules, names)


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

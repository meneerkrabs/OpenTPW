"""Player-profile witnesses for the identified Feral Mac executable.

Covers how a player (profile) is created, enumerated, selected, persisted,
unloaded and deleted, which state holds the game mode, what happens when the
player-info file is missing or unreadable, the theme key gate's refusal path,
and the park loader's header gate that a resumed save must pass. Same rules as
scenario_evidence.py: bounded instruction-field checks at named offsets,
interpreted values only, nothing executed.

``qualify_tpwi_header`` applies the header checks traced here to a PC park file
and reports interpreted values only (version, flags, equality results); it never
prints the file's contents.
"""
from __future__ import annotations

from pathlib import Path
import struct
import sys

sys.path.insert(0, str(Path(__file__).resolve().parent))
from scenario_evidence import Evidence, pef  # noqa: E402
from followup_evidence import data_refs, exact_callers, toc_users  # noqa: E402
from park_entry_evidence import stores_at, wide  # noqa: E402
from progression_evidence import vtable_slot  # noqa: E402

GAME_TYPE = ('data', 0x53d98)
PATHS = ('data', 0x1577c0)
INFO = ('data', 0x120d84)
INFO_NAME = 0x120e30
CHAR_POOL = 0x1ddcf2
WIDE_POOL = 0x1de224
SCAN = 0x1369e4
CREATE = 0x13741c
SETUP_DIRS = 0x137600
LOAD = 0x13781c
UNLOAD = 0x137a88
DELETE = 0x137350
SAVE_SLOT = 0x137dbc
SAVE = 0x137de0
INFO_SAVE = 0x128f5c
INFO_LOAD = 0x129060
INFO_RESET = 0x1287d4
SERIALIZE = 0x129308
PLAYER_DIR = 0x137284
USERS_DIR = 0x1370d4
SAVE_DIR = 0x137058
SLOT_NAME = 0x1371cc
THEME_INFO = 0x129ae0
GATE_VTABLE = 0x3ecc8
HEADER = 0x11c880
PARK_LOAD = 0x11acfc
BANNER = 0x1d6144
SAVE_TAG = 0x46fb8
INFO_VERSION = 12
VERSION_LIMIT = 500


def import_at(e: Evidence, offset: int, name: str) -> None:
    e.import_call(offset, name)
    e.checked += 1


def paths(e: Evidence) -> dict:
    """<base>save:users:<slot+1><name>:gms.dat; the base comes from an object not traced here."""
    # Wide path members of the 0x1577c0 path object (wide pool from 0x1de224, loaded into r29).
    names = {}
    for site, member, offset, expected in ((0x1bf9e4, 17600, 60, ':'), (0x1bf9f4, 17620, 64, 'save'),
                                           (0x1bfa04, 17640, 74, 'users'), (0x1bfa14, 17660, 86, 'online')):
        e.d(site, 14, 3, 31, member)
        e.d(site + 4, 14, 4, 29, offset)
        names[member] = wide(e, WIDE_POOL + offset, expected)
    e.d(0x1bf654, 14, 3, 31, 16460)  # char ':' and 'levels' for the creation-time copy
    e.d(0x1bf658, 14, 4, 30, 17)
    e.text(CHAR_POOL + 17, ':')
    e.d(0x1bf694, 14, 3, 31, 16540)
    e.d(0x1bf698, 14, 4, 30, 34)
    e.text(CHAR_POOL + 34, 'levels')
    # <base> + 'save'
    e.toc_load(0x13708c, 4, PATHS)
    e.d(0x137094, 14, 4, 4, 17620)
    # save dir + ':' + 'users'
    e.bl(0x1370f4, SAVE_DIR)
    e.d(0x13711c, 14, 4, 31, 17600)
    e.d(0x13712c, 14, 4, 31, 17640)
    # users dir + ':' + decimal(slot + 1) + name (name omitted when empty)
    e.bl(0x1372ac, USERS_DIR)
    e.d(0x1372d8, 14, 4, 4, 17600)
    e.bl(0x1372f0, SLOT_NAME)
    e.d(0x1371d8, 14, 5, 0, 10)
    e.d(0x1371ec, 14, 3, 31, 1)
    import_at(e, 0x1371fc, '_itow__10NS_MacDozeFiPwi')
    e.bc(0x13722c, 12, 2, 0x137234)
    e.d(0x137234, 14, 31, 0, 0)
    # The player-info file name is a static wide string built from 'gms.dat'.
    e.toc_load(0x137f64, 31, ('code', 0x1d9a6c))
    e.toc_load(0x137f98, 3, ('data', INFO_NAME))
    e.d(0x137f9c, 14, 4, 31, 16)
    info_name = wide(e, 0x1d9a6c + 16, 'gms.dat')
    if data_refs(e, INFO_NAME) != [0x3bb4] or toc_users(e, 0x3bb4) != [0x1369fc, 0x1378d8, 0x137e40, 0x137f98]:
        raise pef.PEFError('player-info name users')
    e.checked += 1
    # Save: player dir + ':' + gms.dat; load and scan build the same path.
    e.bl(0x137e04, PLAYER_DIR)
    e.d(0x137e30, 14, 4, 4, 17600)
    e.toc_load(0x137e40, 4, ('data', INFO_NAME))
    e.bl(0x137e98, INFO_SAVE)
    e.bl(0x13784c, PLAYER_DIR)
    e.d(0x1378c8, 14, 4, 4, 17600)
    e.toc_load(0x1378d8, 4, ('data', INFO_NAME))
    e.toc_load(0x1369fc, 29, ('data', INFO_NAME))
    e.d(0x136cbc, 14, 4, 29, 0)
    # 'online' sits beside 'users' under save (0x137d20); not part of offline profiles.
    e.d(0x137d78, 14, 4, 31, 17660)
    return {'player_directory': '<base>save:users:<decimal slot+1><player name>',
            'player_info_file': f'<player directory>:{info_name}',
            'theme_directory': '<player directory>:<theme name> (park saves, see park_entry)',
            'members': names, 'base': 'object at data 0x53750 (not traced)'}


def manager(e: Evidence) -> dict:
    """212-byte manager: 4 slots of 24 bytes (name string + mode byte word at +20), current slot at +96."""
    e.d(0x15cfcc, 14, 3, 0, 212)
    e.bl(0x15cfe4, 0x136860)
    e.d(0x13686c, 14, 6, 0, 24)
    e.d(0x136874, 14, 7, 0, 4)
    import_at(e, 0x136888, '__construct_array')
    e.d(0x1368a4, 14, 0, 0, -1)
    e.d(0x1368ac, 36, 0, 31, 96)
    # slot getters: name (0 when empty) and the +20 word
    e.d(0x13717c, 7, 0, 4, 24)
    e.bc(0x137198, 12, 2, 0x1371a4)
    e.d(0x1371a4, 14, 3, 0, 0)
    e.d(0x1371bc, 7, 0, 4, 24)
    e.d(0x1371c4, 32, 3, 3, 20)
    slot_mode_readers = exact_callers(e, 0x1371bc, [0x15c454, 0x15c584, 0x15c6b4, 0x15c7e4])
    return {'slots': 4, 'slot_size': 24, 'slot_mode_offset': 20, 'current_slot_offset': 96,
            'no_player': -1, 'slot_mode_readers': slot_mode_readers}


def scan(e: Evidence) -> dict:
    """Start-up enumeration of save:users (sole caller 0x498)."""
    exact_callers(e, SCAN, [0x498])
    # clear all four slots (name "", +20 = 0)
    e.toc_load(0x136a0c, 27, ('code', 0x1d9a6c))
    e.d(0x136a3c, 36, 25, 23, 20)
    e.d(0x136a40, 11, 0, 22, 3)
    e.bc(0x136a48, 4, 1, 0x136a20)
    # missing save directory: create save, Users and Online; missing Users: create Users
    import_at(e, 0x136b38, 'Open__Q33bfl10FileSystem16CDiscFileStorageFPCc')
    e.bc(0x136b48, 12, 0, 0x136e34)
    import_at(e, 0x136e3c, 'LbFile_CreateDirectory__FPCc')
    import_at(e, 0x136e4c, 'LbFile_CreateDirectory__FPCc')
    import_at(e, 0x136e5c, 'LbFile_CreateDirectory__FPCc')
    e.bc(0x136b68, 12, 0, 0x136df0)
    import_at(e, 0x136df8, 'LbFile_CreateDirectory__FPCc')
    # entries: '*' in Users; name longer than 1, a directory not starting with '.', first char a digit 1..4
    e.d(0x136b88, 32, 5, 24, 16608)
    import_at(e, 0x136b8c, 'FindFirst__Q33bfl10FileSystem12CFileStorageFPCcRQ33bfl10FileSystem9CFileInfo')
    e.d(0x136bb0, 10, 0, 3, 1)
    e.bc(0x136bb4, 4, 1, 0x136dcc)
    e.bl(0x136bbc, 0x37e0)
    e.rlwinm(0x37e4, 0, 0, 0, 27, 27)
    e.d(0x37f4, 11, 0, 0, 46)
    e.bc(0x136bc4, 12, 2, 0x136dcc)
    e.rlwinm(0x136bd8, 0, 0, 0, 27, 27)
    e.toc_load(0x136a00, 30, ('import', '__ctype_map'))
    e.d(0x136be4, 14, 0, 0, 0)
    e.d(0x136bec, 38, 0, 1, 297)  # one-character string
    import_at(e, 0x136bf0, 'atoi')
    e.d(0x136bfc, 11, 0, 25, 1)
    e.bc(0x136c00, 12, 0, 0x136dcc)
    e.d(0x136c04, 11, 0, 25, 4)
    e.bc(0x136c08, 12, 1, 0x136dcc)
    import_at(e, 0x136c38, 'Right__15TbStringBase<c>CFUi')
    e.d(0x136c34, 14, 5, 5, -1)
    e.d(0x136c50, 14, 22, 25, -1)
    # load gms.dat into a fresh record; the result is not tested
    e.bl(0x136d14, 0x1284e4)
    e.bl(0x136d24, INFO_LOAD)
    e.d(0x136d28, 32, 0, 28, 0)
    e.bl(0x136d58, 0x128f4c)
    e.d(0x128f4c, 34, 3, 3, 36)
    e.d(0x136d60, 36, 0, 23, 20)
    e.bl(0x136d88, 0x136eec)  # create <player>:<theme> for every theme
    exact_callers(e, 0x136eec, [0x136d88])
    import_at(e, 0x136f98, 'LbFile_CreateDirectory__FPCc')
    import_at(e, 0x136dd8, 'FindNext__Q33bfl10FileSystem12CFileStorageFRQ33bfl10FileSystem9CFileInfo')
    e.bc(0x136de8, 4, 0, 0x136ba4)
    return {'pattern': '* in save:users', 'accepts': 'directory, not starting with ".", length > 1, '
                                                     'first character a digit 1..4 (atoi of that character)',
            'slot': 'digit - 1; name = the remaining characters; a later entry with the same digit replaces it',
            'slot_mode_source': 'mEasyModeUser of that gms.dat (byte +36)',
            'load_result_used': False, 'creates_missing': ['save', 'users', 'online', '<player>:<theme>']}


def info_file(e: Evidence) -> dict:
    """gms.dat: version tag 12, then the named-member serializer 0x129308."""
    exact_callers(e, INFO_SAVE, [0x137e98])
    exact_callers(e, INFO_LOAD, [0x136d24, 0x137930])
    exact_callers(e, SAVE, [0x1375d8, 0x137dcc])
    save_points = exact_callers(e, SAVE_SLOT, [0x137b0c, 0x15ceb4, 0x1989b8])
    # write
    e.toc_load(0x128f68, 31, ('code', 0x1d8725))
    e.text(0x1d8725, 'Failed to open %s')
    e.text(0x1d8725 + 18, 'version')
    import_at(e, 0x128f94, 'LbFile_Open__FRPvPCcUl')
    e.d(0x128fc0, 14, 0, 0, INFO_VERSION)
    e.d(0x128fd0, 14, 5, 31, 18)
    e.d(0x129008, 14, 5, 0, 1)
    e.bl(0x12900c, SERIALIZE)
    e.d(0x137e9c, 14, 3, 1, 76)  # the caller ignores the result
    # read: reset first, then open, version >= 12, serializer
    e.bl(0x129088, INFO_RESET)
    import_at(e, 0x1290a4, 'LbFile_Open__FRPvPCcUl')
    e.d(0x1290bc, 14, 4, 28, 26)
    e.text(0x1d8725 + 26, "Couldn't open file %s")
    e.d(0x1290c8, 14, 3, 0, 0)
    e.d(0x129108, 10, 0, 6, INFO_VERSION)
    e.bc(0x12910c, 4, 0, 0x129128)
    e.d(0x129120, 14, 3, 0, 0)
    e.d(0x129130, 14, 5, 0, 0)
    e.bl(0x129134, SERIALIZE)
    e.d(0x129150, 14, 3, 0, 0)
    e.d(0x1292f0, 14, 3, 0, 1)
    return {'version_written': INFO_VERSION, 'version_required': f'>= {INFO_VERSION}',
            'read_order': 'reset record, open, version tag, members',
            'failure': 'returns 0 (open, header, version < 12 or member read); every caller ignores it',
            'save_callers': save_points, 'save_result_used': False}


def create(e: Evidence) -> dict:
    exact_callers(e, CREATE, [0x15cffc, 0x1c1bd4])
    # dialog: trailing spaces removed; an empty name creates nothing and keeps the dialog
    exact_callers(e, 0x15cf00, [0x15d144])
    e.toc_load(0x15cf20, 28, ('data', 0x134d8c))
    e.d(0x15cf74, 10, 0, 0, 32)
    e.bc(0x15cf8c, 12, 2, 0x15d050)
    e.d(0x15d050, 14, 3, 0, 0)
    e.d(0x15cfa4, 14, 4, 0, 1804)
    e.d(0x15cfb8, 8, 3, 3, 1805)
    e.d(0x15cff0, 14, 4, 30, 0)
    e.bl(0x15d034, LOAD)
    e.d(0x15d14c, 10, 0, 0, 1)
    e.bc(0x15d150, 4, 2, 0x15d1ec)
    # CreatePlayer: an existing directory of a named slot is deleted first
    import_at(e, 0x13744c, 'Length__15TbStringBase<w>CFv')
    e.bc(0x137458, 12, 2, 0x1374ec)
    e.bl(0x1374a8, 0x380c)
    e.d(0x1374cc, 36, 0, 30, 20)
    # player and theme directories are created for both modes; only the copy needs the flag
    e.bl(0x137548, SETUP_DIRS)
    import_at(e, 0x137644, 'LbFile_CreateDirectory__FPCc')
    import_at(e, 0x1376a8, 'LbFile_CreateDirectory__FPCc')
    e.d(0x1376b0, 11, 0, 24, 0)
    # a fresh record (reset values) with mEasyModeUser = slot flag, saved immediately
    e.bl(0x13755c, 0x12870c)
    e.d(0x137568, 32, 0, 29, 0)
    e.bc(0x137570, 12, 2, 0x1375a4)
    e.bl(0x13758c, 0x1284e4)
    e.d(0x137598, 14, 4, 0, 1)
    e.bl(0x13759c, 0x128f54)
    e.bl(0x1375bc, 0x1284e4)
    e.d(0x1375c8, 14, 4, 0, 0)
    e.bl(0x1375cc, 0x128f54)
    e.d(0x128f54, 38, 4, 3, 36)
    e.bl(0x1375d8, SAVE)
    # state 9 without a loaded player: slot 0 'debug' (Full Simulation) when empty, then load slot 0
    e.d(0x1c1b24, 32, 0, 3, 96)
    e.d(0x1c1b28, 11, 0, 0, -1)
    e.d(0x1c1b78, 14, 4, 0, 0)
    e.bl(0x1c1b7c, 0x137170)
    e.bc(0x1c1b84, 4, 2, 0x1c1be8)
    e.toc_load(0x1c1b8c, 4, ('code', 0x1de61c))
    debug_name = wide(e, 0x1de61c, 'debug')
    e.d(0x1c1bcc, 14, 4, 0, 0)
    e.d(0x1c1bd0, 14, 6, 0, 0)
    e.d(0x1c1c18, 14, 4, 0, 0)
    e.bl(0x1c1c1c, LOAD)
    return {'dialog': 'name with trailing spaces removed; empty -> nothing created (dialog stays)',
            'mode_choice': 'control 1805 -> Instant Action flag 1, otherwise 0',
            'slot': 'chosen by the dialog; a named slot is overwritten and its directory deleted',
            'record': 'fresh (reset) record, mEasyModeUser = flag, gms.dat written at once',
            'directories': 'player and every <player>:<theme> directory for both modes',
            'state_9_fallback': f'no loaded player: slot 0 "{debug_name}" (flag 0) when empty, then load slot 0'}


def select_and_unload(e: Evidence) -> dict:
    load_callers = exact_callers(e, LOAD, [0x15c060, 0x15d034, 0x1c1c1c])
    e.d(0x137844, 36, 5, 3, 96)
    e.bl(0x137930, INFO_LOAD)
    e.d(0x137934, 34, 0, 30, 0)  # next instruction ignores the result
    e.toc_load(0x13782c, 29, GAME_TYPE)
    e.d(0x137954, 11, 0, 0, 1)
    e.bc(0x137958, 12, 2, 0x1379ec)
    e.bl(0x13798c, 0x128f4c)
    e.bc(0x137994, 12, 2, 0x1379c4)
    e.d(0x1379b4, 14, 4, 0, 2)
    e.bl(0x1379bc, 0x12bbf4)
    e.d(0x1379e0, 14, 4, 0, 0)
    e.bl(0x1379e8, 0x12bbf4)
    e.bl(0x137a38, THEME_INFO)
    unload_callers = exact_callers(e, UNLOAD, [0x1979f0, 0x1c2b38])
    e.bl(0x137b0c, SAVE_SLOT)
    e.d(0x137dc8, 32, 4, 3, 96)
    e.d(0x137ba8, 14, 0, 0, -1)
    e.d(0x137bb0, 36, 0, 29, 96)
    delete_callers = exact_callers(e, DELETE, [0x15bedc])
    e.bl(0x1373b8, 0x380c)
    e.d(0x1373e0, 36, 0, 3, 20)
    if stores_at(e, LOAD, UNLOAD, 96) != [0x137844] or stores_at(e, UNLOAD, 0x137c0c, 96) != [0x137bb0]:
        raise pef.PEFError('current-slot stores')
    e.checked += 1
    return {'load_callers': load_callers, 'unload_callers': unload_callers, 'delete_callers': delete_callers,
            'load': 'slot +96 = slot; gms.dat read into a reset record (result unused); GameType = 2 if '
                    'mEasyModeUser else 0 unless GameType is 1; per-theme records found or created',
            'unload': 'gms.dat written, record freed, +96 = -1',
            'delete': 'player directory deleted, slot name "" and +20 = 0'}


def key_gate(e: Evidence) -> dict:
    """Lobby door: virtual +72 tests, +76 starts entering, +80 commits the park choice."""
    vtable_slot(e, GATE_VTABLE, 72, 0x964c4)
    vtable_slot(e, GATE_VTABLE, 76, 0x96630)
    vtable_slot(e, GATE_VTABLE, 80, 0x9665c)
    e.toc_load(0x975f4, 0, ('data', GATE_VTABLE))
    e.d(0x975f8, 36, 0, 30, 0)
    e.d(0x964f0, 32, 0, 3, 20)
    e.bc(0x964f8, 4, 2, 0x96610)
    e.d(0x964fc, 32, 0, 30, 12)
    e.bc(0x96504, 12, 2, 0x96610)
    e.d(0x096528, 11, 0, 0, 2)
    e.bc(0x09652c, 12, 2, 0x965fc)
    e.d(0x096538, 32, 4, 4, 48)
    e.bl(0x096578, THEME_INFO)
    e.bc(0x096598, 12, 2, 0x96610)
    e.bl(0x0965cc, 0x128b60)
    e.bl(0x0965d8, 0x12a4c8)
    e.x(0x0965dc, 31, 0, 0, 3, 31)
    e.bc(0x0965e0, 12, 1, 0x96610)
    e.d(0x0965ec, 32, 12, 12, 76)
    e.d(0x096604, 32, 12, 12, 76)
    # The refusal target is the epilogue: no call between it and the return.
    for offset in range(0x96610, 0x9662c, 4):
        if e.word(offset) >> 26 == 18:
            e.fail(offset, 'call in refusal path', hex(e.word(offset)))
    e.x(0x9662c, 19, 16, 20, 0, 0)
    # +76 marks entering (+20 = 1); +80 commits the theme and front-end exit code 2.
    e.d(0x096638, 14, 0, 0, 1)
    e.d(0x096640, 36, 0, 3, 20)
    e.bl(0x0966a8, 0x110758)
    e.d(0x0966c0, 14, 0, 0, 2)
    e.d(0x0966c8, 36, 0, 3, 20)
    # Theme record: find or create, then 0x12a50c must pass (as in the cost getter), else 0 (door refuses).
    e.bl(0x129b28, 0x12a50c)
    e.d(0x129b3c, 14, 3, 0, 0)
    e.d(0x129b50, 14, 4, 4, 444)
    e.bl(0x129b60, 0x129b9c)
    e.bl(0x129b68, 0x12a50c)
    e.d(0x129b7c, 14, 3, 0, 0)
    e.d(0x12a4ec, 32, 3, 3, 20)
    e.d(0x12a4f4, 14, 3, 0, -1)
    return {'order': 'already entering (+20) or no target -> nothing; GameType 2 -> enter; else theme record '
                     '(0x12a50c must pass) and signed CostToEnter > Keys() -> nothing',
            'refusal_side_effects': 'none in this routine: no message, no state, no key change',
            'enter': '+76 sets +20 = 1; +80 selects the theme and sets front-end exit code 2',
            'unresolved': 'door presentation (locked look, hover text) is not traced'}


def header_gate(e: Evidence) -> dict:
    """Park loader 0x11acfc and its header reader 0x11c880, as reached by the resume (mode 2)."""
    e.d(0x11ae44, 11, 0, 29, 2)
    e.bc(0x11ae5c, 4, 2, 0x11aea4)
    e.d(0x11ae64, 11, 0, 0, VERSION_LIMIT)
    e.bc(0x11ae68, 4, 1, 0x11aea4)
    e.d(0x11ae6c, 14, 0, 0, 9)
    e.d(0x11ae70, 14, 3, 28, 442)
    e.text(0x1d514e + 442, 'Trying to load future version savegame into earlier game - get a patch')
    e.rlwinm(0x11ae48, 3, 0, 8, 8, 15)  # first word is little-endian
    e.bl(0x11aeb4, HEADER)
    exact_callers(e, HEADER, [0x11aeb4])
    # language byte, then 640 UTF-16LE characters compared with the banner table entry
    e.d(0x11c89c, 14, 6, 0, 1)
    e.d(0x11c8ec, 14, 6, 0, 1280)
    e.d(0x11c91c, 14, 4, 0, 640)
    import_at(e, 0x11c920, '_USwapBlock16__13SamsUtilitiesFPUsl')
    e.toc_load(0x11c930, 3, ('data', 0x120114))
    e.d(0x11c934, 7, 0, 0, 20)
    import_at(e, 0x11c93c, '__ne__15TbStringBase<w>CFPCw')
    e.d(0x11c950, 14, 4, 31, 2957)
    e.text(0x1d514e + 2957, 'The save game legal text has been jiggered with!')
    e.toc_load(0x11dbd4, 30, ('code', BANNER))
    e.toc_load(0x11dbec, 31, ('data', 0x120114))
    e.x(0x11dc04, 31, 444, 31, 3, 31)  # first table entry
    e.d(0x11dc2c, 14, 4, 30, 826)
    banner_end = BANNER
    while e.code[banner_end:banner_end + 2] != b'\0\0':
        banner_end += 2
        if banner_end - BANNER > 1280:
            raise pef.PEFError('banner unbounded')
    banner = e.code[BANNER:banner_end].decode('utf-16-be')
    if banner_end + 2 - BANNER != 826:
        raise pef.PEFError(f'banner length {banner_end - BANNER}')
    e.checked += 1
    # 256-byte object block checked by 0x109e0c (mode 2 passes 1); a non-zero result is error 8
    e.d(0x11c970, 14, 6, 0, 256)
    e.d(0x11c99c, 11, 0, 27, 2)
    e.bl(0x11c9bc, 0x109e0c)
    e.d(0x11c9c8, 14, 0, 0, 8)
    # 4 raw bytes equal to the word at data 0x46fb8 (error 6 otherwise)
    e.d(0x11ca04, 14, 6, 0, 4)
    e.toc_load(0x11ca30, 3, ('data', SAVE_TAG))
    e.x(0x11ca3c, 31, 32, 0, 4, 0)
    e.d(0x11ca44, 14, 0, 0, 6)
    tag = struct.unpack_from('>I', e.data, SAVE_TAG)[0]
    if data_refs(e, SAVE_TAG) != [0x185c] or toc_users(e, 0x185c) != [0x11c728, 0x11ca30]:
        raise pef.PEFError('save tag users')
    e.checked += 1
    # then a little-endian word: 1 = embedded header follows, 0 = none
    e.d(0x11ca64, 14, 6, 0, 4)
    e.rlwinm(0x11ca94, 3, 0, 8, 8, 15)
    e.d(0x11cacc, 11, 0, 0, 1)
    return {'version': f'little-endian int32; resume (mode 2) rejects > {VERSION_LIMIT} (error 9)',
            'language_banner': 'byte index, then 1280 bytes UTF-16LE compared up to its NUL with table entry',
            'banner_entry_0_characters': len(banner), 'object_block': '256 bytes -> 0x109e0c (not traced)',
            'tag': f'{tag:#010x} raw', 'header_flag': 'little-endian int32 (1 = embedded header)',
            '_banner': banner}


def qualify_tpwi_header(raw: bytes, banner: str, tag: int) -> dict:
    """Apply the traced header checks to a park file; interpreted values only."""
    if len(raw) < 1549:
        return {'qualified': False, 'reason': 'shorter than the traced header'}
    version = struct.unpack_from('<i', raw, 0)[0]
    language = raw[4]
    text = raw[5:1285].decode('utf-16-le', errors='replace').split('\0')[0]
    block_zero = raw[1285:1541] == bytes(256)
    file_tag = struct.unpack_from('>I', raw, 1541)[0]
    flag = struct.unpack_from('<i', raw, 1545)[0]
    # Only table entry 0 is bound here; other language indices stay unchecked (None).
    checks = {'version_le_limit': version <= VERSION_LIMIT,
              'banner_equal': text == banner if language == 0 else None,
              'tag_equal': file_tag == tag}
    return {'version': version, 'language_index': language, 'object_block_all_zero': block_zero,
            'header_flag': flag, **checks, 'qualified': all(v is True for v in checks.values()),
            'not_checked': 'object block result of 0x109e0c, payload, sections after the header'}


def inspect_profiles(e: Evidence) -> dict:
    gate = header_gate(e)
    gate.pop('_banner')
    return {'profile_paths': paths(e), 'profile_manager': manager(e), 'profile_scan': scan(e),
            'profile_info_file': info_file(e), 'profile_create': create(e),
            'profile_select': select_and_unload(e), 'key_gate': key_gate(e), 'park_header_gate': gate}


def main() -> None:
    import argparse
    import json
    from scenario_evidence import load_identified
    parser = argparse.ArgumentParser(description='Apply the traced Mac park-header checks to PC park files.')
    parser.add_argument('bin_root', type=Path, help='directory containing SimThemePark.data')
    parser.add_argument('park', type=Path, nargs='+', help='PC .TPWI/.TPWS files')
    args = parser.parse_args()
    try:
        e = Evidence(load_identified(args.bin_root / 'SimThemePark.data'))
        gate = header_gate(e)
        tag = struct.unpack_from('>I', e.data, SAVE_TAG)[0]
        out = {str(path): qualify_tpwi_header(path.read_bytes(), gate['_banner'], tag) for path in args.park}
    except (OSError, pef.PEFError) as error:
        parser.exit(1, f'profile evidence: {error}\n')
    print(json.dumps(out, indent=2, sort_keys=True))


if __name__ == '__main__':
    main()

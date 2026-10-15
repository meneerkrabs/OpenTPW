"""Park-entry witnesses for the identified Feral Mac executable.

Covers which park file an offline park entry (main-loop state 9) reads: the
theme's level and balance files, then the newest ``*.TPW*`` in the player's
theme directory, and nothing from ``levels:<theme>:easymode.TPWI`` directly.
Also the autosave written on leaving a park (state 11), the restart snapshot
(state 15) and the Instant Action Easy_ layer of object text files. Same rules as scenario_evidence.py: bounded instruction-field
checks at named offsets, interpreted values only, nothing executed.
"""
from __future__ import annotations

from scenario_evidence import Evidence, pef
from followup_evidence import data_refs, exact_callers, toc_users

GAME_TYPE = ('data', 0x53d98)
PATHS = ('data', 0x1577c0)
THEME = ('data', 0x11f42c)
PLAYER = ('data', 0x120e2c)
SAVE_DIR = ('data', 0x136d74)
LOADER_OBJECT = ('data', 0x1201f4)
STARTUP_SAVE = 0x11f5ac
MAIN_STATES = 0x52cc8
PARK_LOAD = 0x11acfc
NEWEST_SAVE = 0x198e50
PLAYER_THEME_DIR = 0x137c3c
EASY_COPY = 0x137600
# Functions that load the startup-save object's TOC slot, with their extents.
STARTUP_SAVE_USERS = {0x10efa4: (0x10ef9c, 0x10f0a0), 0x116004: (0x115ffc, 0x1162d0),
                      0x116318: (0x116318, 0x116330), 0x1c1f54: (0x1c1208, 0x1c2ca0)}


def wide(e: Evidence, offset: int, expected: str) -> str:
    """Big-endian UTF-16 identifier string in the code section."""
    end = offset
    while e.code[end:end + 2] != b'\0\0':
        end += 2
        if end - offset > 64:
            raise pef.PEFError(f'code:{offset:#x} has no bounded wide string')
    actual = e.code[offset:end].decode('utf-16-be')
    if actual != expected:
        e.fail(offset, 'wide identifier string', actual)
    return actual


def stores_at(e: Evidence, start: int, end: int, displacement: int) -> list[int]:
    """D-form stores (any width, update or not) with the given displacement in [start, end)."""
    return [o for o in range(start, end, 4)
            if e.word(o) >> 26 in (36, 37, 38, 39, 44, 45) and e.word(o) & 0xffff == displacement]


def park_entry(e: Evidence) -> dict:
    # Path strings: char members at 0x1577c0 + n (string base 0x1ddcf2), wide ones from 0x1de224.
    e.toc_load(0x1bf618, 30, ('code', 0x1ddcf2))
    e.toc_load(0x1bf620, 29, ('code', 0x1de224))
    e.d(0x1bf994, 14, 3, 31, 17500)
    e.d(0x1bf998, 14, 4, 30, 406)
    e.text(0x1ddcf2 + 406, '.TPW*')
    e.d(0x1bf6c4, 14, 3, 31, 16600)
    e.d(0x1bf6c8, 14, 4, 30, 52)
    e.text(0x1ddcf2 + 52, '*')
    e.d(0x1bf9e4, 14, 3, 31, 17600)
    e.d(0x1bf9e8, 14, 4, 29, 60)
    e.d(0x1bfa24, 14, 3, 31, 17680)
    e.d(0x1bfa28, 14, 4, 29, 100)
    e.d(0x1bfa34, 14, 3, 31, 17700)
    e.d(0x1bfa38, 14, 4, 29, 118)
    e.d(0x1bf9d4, 14, 3, 31, 17580)
    e.d(0x1bf9d8, 14, 4, 29, 48)
    e.d(0x1bf964, 14, 3, 31, 17440)
    e.d(0x1bf968, 14, 4, 29, 0)
    names = {'separator': wide(e, 0x1de224 + 60, ':'), 'autosave': wide(e, 0x1de224 + 100, 'autosave'),
             'restart': wide(e, 0x1de224 + 118, 'restart'), 'restart_ext': wide(e, 0x1de224 + 48, '.INTS'),
             'autosave_ext': wide(e, 0x1de224, '.TPWS')}

    # Player theme directory: player directory + ':' + current theme name (theme object +0, a string).
    e.d(0x137c80, 14, 4, 4, 17600)
    e.toc_load(0x137c8c, 3, THEME)
    e.bl(0x137c90, 0x110a38)
    e.x(0x110a38, 19, 16, 20, 0, 0)  # blr: returns the theme object itself
    e.d(0x110a50, 32, 6, 3, 40)  # current theme picked from the theme list (+40) by index (+20)
    e.d(0x110a4c, 32, 5, 3, 20)

    # Main-loop state 9 (park load): jump-table entry, level and balance, then the offline resume.
    if e.slot_target(MAIN_STATES + 4 * 9) != ('code', 0x1c1940):
        raise pef.PEFError(f'data:{MAIN_STATES + 36:#x} state 9 entry {e.slot_target(MAIN_STATES + 36)!r}')
    e.checked += 1
    e.toc_load(0x1c1228, 30, GAME_TYPE)
    e.toc_load(0x1c1238, 20, THEME)
    e.toc_load(0x1c1248, 21, ('data', 0x15c488))
    e.bl(0x1c1ce0, 0x10ef9c)
    e.bl(0x10efe0, 0x10474c)  # balance loader: GameType 2 adds the Easy_ layer (modes())
    # The only other file read there is a save named on the command line (startup-save object +0).
    e.toc_load(0x10efa4, 31, ('data', STARTUP_SAVE))
    e.d(0x10eff4, 32, 0, 31, 0)
    e.bl(0x10f030, 0x19878c)
    e.d(0x116204, 32, 20, 31, 17428)  # argument ends in .TPWS
    e.d(0x116268, 36, 0, 30, 0)
    # Resume branch: startup-save object +1028 must be 0, then GameType 1 goes online, else newest save.
    e.toc_load(0x1c1f54, 3, ('data', STARTUP_SAVE))
    e.d(0x1c1f58, 32, 0, 3, 1028)
    e.bc(0x1c1f60, 4, 2, 0x1c2028)
    e.d(0x1c1f80, 32, 0, 30, 0)
    e.d(0x1c1f84, 11, 0, 0, 1)
    e.bc(0x1c1f88, 4, 2, 0x1c2020)
    e.toc_load(0x1c2020, 3, SAVE_DIR)
    e.bl(0x1c2024, NEWEST_SAVE)
    # +1028 is zeroed by the static initializer and stored nowhere else in the slot's users.
    if data_refs(e, STARTUP_SAVE) != [0x17f8] or toc_users(e, 0x17f8) != sorted(STARTUP_SAVE_USERS):
        raise pef.PEFError('startup-save object users')
    writers = [o for start, end in STARTUP_SAVE_USERS.values() for o in stores_at(e, start, end, 1028)]
    if writers != [0x116324]:
        raise pef.PEFError(f'startup-save +1028 stores {[hex(w) for w in writers]}')
    e.d(0x11631c, 14, 0, 0, 0)
    e.d(0x116324, 36, 0, 3, 1028)
    e.checked += 1

    # Newest save: '*' + '.TPW*' in the player theme directory, newest stamp wins, none -> no load.
    e.toc_load(0x198e58, 24, PLAYER)
    e.toc_load(0x198e60, 30, PATHS)
    e.bl(0x198ea0, PLAYER_THEME_DIR)
    e.d(0x198f30, 14, 4, 30, 16600)
    e.d(0x198f40, 14, 4, 30, 17500)
    e.import_call(0x198f5c, 'FindFirst__Q33bfl10FileSystem12CFileStorageFPCcRQ33bfl10FileSystem9CFileInfo')
    e.d(0x198f88, 14, 24, 0, 0)
    e.d(0x198ff8, 14, 24, 0, 1)
    e.import_call(0x199018, 'LbFile_CompareFileStamps__FPC11TbFileStampPC11TbFileStamp')
    e.import_call(0x199098, 'FindNext__Q33bfl10FileSystem12CFileStorageFRQ33bfl10FileSystem9CFileInfo')
    e.d(0x1990ac, 11, 0, 24, 0)
    e.bc(0x1990b0, 12, 2, 0x199108)
    e.toc_load(0x1990ec, 3, LOADER_OBJECT)
    e.d(0x1990f0, 14, 6, 0, 2)
    e.bl(0x1990f4, PARK_LOAD)
    resume_callers = exact_callers(e, NEWEST_SAVE, [0x1c2024])
    load_callers = exact_callers(e, PARK_LOAD, [0x112b10, 0x198914, 0x1990f4, 0x1c200c])

    # easymode.TPWI is named only by the creation-time copy, which needs the easy flag.
    easy_slots = e.pointer_refs(0x1d9a62)
    if easy_slots != [0x3bb0] or toc_users(e, 0x3bb0) != [0x137608]:
        raise pef.PEFError(f'easymode references {easy_slots!r}')
    e.checked += 1
    e.text(0x1d9a62, 'easymode')
    e.d(0x137438, 14, 28, 6, 0)  # CreatePlayer keeps its easy-flag argument in r28
    e.d(0x137508, 37, 28, 29, 20)
    e.d(0x137540, 14, 5, 28, 0)
    e.bl(0x137548, EASY_COPY)
    e.x(0x137614, 31, 444, 5, 24, 5)  # mr r24, r5
    e.d(0x1376b0, 11, 0, 24, 0)
    e.bc(0x1376b4, 12, 2, 0x1377e0)
    exact_callers(e, EASY_COPY, [0x137548])
    e.d(0x137658, 14, 31, 31, 17460)

    # Object text files: GameType 2 also reads '%s:%s%s' with the Easy_ prefix; a missing one is skipped.
    e.toc_load(0x119340, 23, GAME_TYPE)
    e.toc_load(0x119354, 27, PATHS)
    e.d(0x119860, 32, 0, 23, 0)
    e.d(0x119864, 11, 0, 0, 2)
    e.bc(0x119868, 4, 2, 0x119920)
    e.d(0x119878, 32, 20, 27, 17028)
    e.d(0x119898, 14, 4, 29, 145)
    e.text(0x1d502a + 145, '%s:%s%s')
    e.bc(0x1198b4, 12, 2, 0x119920)

    # Leaving a park (state 11) offline saves <player>:<theme>:autosave.TPWS; state 15 writes restart.INTS.
    if e.slot_target(MAIN_STATES + 4 * 11) != ('code', 0x1c2850):
        raise pef.PEFError('state 11 entry')
    e.d(0x1c2870, 11, 0, 0, 1)
    e.bc(0x1c2874, 12, 2, 0x1c2888)
    e.d(0x1c2880, 14, 4, 4, 17680)
    e.bl(0x1c2884, 0x1987dc)
    e.d(0x1987e8, 14, 5, 5, 17440)
    e.bl(0x1989fc, PLAYER_THEME_DIR)
    e.bl(0x198a7c, 0x11a5f4)
    if e.slot_target(MAIN_STATES + 4 * 15) != ('code', 0x1c222c):
        raise pef.PEFError('state 15 entry')
    e.bl(0x1c2254, 0x1c37b4)
    e.bl(0x1c3814, PLAYER_THEME_DIR)
    e.d(0x1c3848, 14, 31, 30, 17700)
    e.d(0x1c3860, 14, 4, 30, 17580)
    return {'state_9_offline_resume': 'GameType != 1 and startup-save +1028 == 0: newest *.TPW* in player:theme',
            'resume_pattern': '* + .TPW* (wildcard matching is the imported CFileStorage; .INTS cannot match)',
            'resume_load_mode': 2, 'resume_callers': resume_callers, 'park_load_callers': load_callers,
            'no_save_found': 'this path does not call the park loader 0x11acfc; no save or easymode file is read',
            'easymode_readers': ['0x137608 (creation-time copy, easy flag only)'],
            'startup_save_1028_stores': ['0x116324 (static zero)'],
            'leave_park_save': '<player>:<theme>:autosave.TPWS (state 11, GameType != 1)',
            'object_easy_layer': 'GameType 2 object text files add the Easy_ file when present (0x119878)',
            'restart_snapshot': '<player>:<theme>:restart.INTS (state 15, GameType != 1)',
            'names': names}

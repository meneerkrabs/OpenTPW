"""Player-file (gms.dat) schema, key-award ordering and saved-profile flags for the Feral Mac executable.

Same rules as scenario_evidence.py: bounded instruction-field checks at named
offsets, interpreted values only, nothing executed. Every witness below belongs
to the identified Mac binary; nothing here is evidence for a PC player file
(none exists in the supplied assets).

``MAC_PLAYER_FILE`` is the typed schema the witnesses pin. ``read_mac_player_file``
applies the traced read order and failure rules to bytes; it is exercised on
synthetic bytes only and is not a parser claim for any PC file.
"""
from __future__ import annotations

from pathlib import Path
import struct
import sys

sys.path.insert(0, str(Path(__file__).resolve().parent))
from scenario_evidence import Evidence, pef  # noqa: E402
from followup_evidence import exact_callers  # noqa: E402
from profile_evidence import CREATE, INFO_LOAD, INFO_RESET, INFO_SAVE, LOAD, SAVE, SAVE_SLOT, SERIALIZE, import_at  # noqa: E402

THEME_RECORD = 0x12a0bc
OPTIONS = 0x12653c
OPTIONS_OBJECT = ('data', 0x120a14)
GAME_TYPE_DATA = 0x53d98
NAMES = 0x1d8725      # member names of the player record and theme record
OPTION_NAMES = 0x1d83d0
WRITE = 'LbFile_Write__FPvPCvUlPUl'
READ = 'LbFile_Read__FPvPvUlPUl'

# helper -> (direction, bytes on disk, encoding); call site of the file import, width site, swap witness
IO = {
    0xca20: ('write', 1, 'u8', 0xca9c, 0xca98, None),
    0xc4f4: ('read', 1, 'u8', 0xc550, 0xc54c, None),
    0xc7f8: ('write', 4, 'i32le', 0xc88c, 0xc888, ('rlwinm', 0xc870, 3)),
    0xc2fc: ('read', 4, 'i32le', 0xc358, 0xc354, ('stwbrx', 0xc3d4, 4, 31)),
    0xcd1b0: ('write', 4, 'i32le', 0xcd244, 0xcd240, ('rlwinm', 0xcd228, 3)),
    0xcd0b4: ('read', 4, 'i32le', 0xcd110, 0xcd10c, ('stwbrx', 0xcd18c, 4, 31)),
    0x12aeb0: ('write', 1, 'u8', 0x12af2c, 0x12af28, None),
    0x12adbc: ('read', 1, 'u8', 0x12ae18, 0x12ae14, None),
    0xe3930: ('write', 2, 'u16le', 0xe39bc, 0xe39b8, ('rlwinm16', 0xe39a8, 3)),
    0xe3738: ('read', 2, 'u16le', 0xe3794, 0xe3790, ('sthbrx', 0xe3810, 4, 31)),
    0xcdd44: ('write', 2, 'u16le', 0xcddd0, 0xcddcc, ('rlwinm16', 0xcddbc, 3)),
    0xcd960: ('read', 2, 'u16le', 0xcd9bc, 0xcd9b8, ('sthbrx', 0xcda38, 4, 31)),
    0x126ddc: ('write', 8, 'u32raw+u32le', 0x126e6c, 0x126e68, ('rlwinm', 0x126e50, 5)),
    0x126eec: ('read', 8, 'u32raw+u32le', 0x126f48, 0x126f44, ('stwbrx', 0x126fbc, 5, 0)),
}

# Player record (0x129308). (member, count, record offset, stride, encoding, name offset,
#   write: (offset site, base reg, call, helper), read: (offset site, base reg, call, helper)).
PLAYER = [
    ('mEarnedGlobalTicket[i]', 4, 24, 1, 'u8', 102, (0x129338, 23, 0x12934c, 0xca20), (0x12961c, 23, 0x129630, 0xc4f4)),
    ('mEarnedSecretTicket[i]', 2, 38, 1, 'u8', 125, (0x129370, 23, 0x129384, 0xca20), (0x129654, 23, 0x129668, 0xc4f4)),
    ('mSpentTickets', 1, 28, 0, 'i32le', 148, (0x1293a8, 26, 0x1293b4, 0xc7f8), (0x12968c, 26, 0x129698, 0xc2fc)),
    ('mExtraKeys', 1, 32, 0, 'i32le', 162, (0x1293cc, 26, 0x1293d8, 0xc7f8), (0x1296b0, 26, 0x1296bc, 0xc2fc)),
    ('mEasyModeUser', 1, 36, 0, 'u8', 173, (0x1293f0, 26, 0x1293fc, 0xca20), (0x1296d4, 26, 0x1296e0, 0xc4f4)),
    ('mSwearFilterOn', 1, 37, 0, 'u8', 187, (0x129414, 26, 0x129420, 0xca20), (0x1296f8, 26, 0x129704, 0xc4f4)),
    ('mFirstTimePlayer', 1, 72, 0, 'u8', 202, (0x129438, 26, 0x129444, 0xca20), (0x12971c, 26, 0x129728, 0xc4f4)),
]
# Theme record (0x12a0bc, 188-byte object); mAward/mAwardScore and mSignNameA/B alternate per index.
THEME = [
    ('mEarnedLocalTicket[i]', 6, 0, 1, 'u8', 625, (None, 0, 0x12a0f4, 0xca20), (None, 0, 0x12a23c, 0xc4f4)),
    ('mAward[i]', 4, 6, 1, 'u8', 647, (0x12a120, 29, 0x12a134, 0xca20), (0x12a268, 29, 0x12a27c, 0xc4f4)),
    ('mAwardScore[i]', 4, 12, 4, 'i32le', 657, (0x12a14c, 30, 0x12a158, 0xc7f8), (0x12a294, 30, 0x12a2a0, 0xc2fc)),
    ('mSignNameA[i]', 33, 52, 2, 'u16le', 672, (0x12a18c, 30, 0x12a198, 0xe3930), (0x12a2d4, 30, 0x12a2e0, 0xe3738)),
    ('mSignNameB[i]', 33, 118, 2, 'u16le', 686, (0x12a1b0, 30, 0x12a1bc, 0xe3930), (0x12a2f8, 30, 0x12a304, 0xe3738)),
    ('mNameChanged', 1, 184, 0, 'u8', 700, (0x12a1e4, 27, 0x12a1f0, 0xca20), (0x12a32c, 27, 0x12a338, 0xc4f4)),
    ('mAllResearchCompleted', 1, 185, 0, 'u8', 713, (0x12a208, 27, 0x12a214, 0xca20), (0x12a350, 27, 0x12a35c, 0xc4f4)),
]
# Settings object at data 0x120a14 (0x12653c), stored inside every player file.
OPTION = [
    ('SFXVolume', 16, 'u32raw+u32le', 0, (0x126570, 0x126578), (0x1266fc, 0x126704)),
    ('MusicVolume', 24, 'u32raw+u32le', 10, (0x126590, 0x12659c), (0x12671c, 0x126728)),
    ('SpeechVolume', 32, 'u32raw+u32le', 22, (0x1265b4, 0x1265c0), (0x126740, 0x12674c)),
    ('MovieVolume', 40, 'u32raw+u32le', 35, (0x1265d8, 0x1265e4), (0x126764, 0x126770)),
    ('AdvisorOn', 52, 'u8', 47, (0x1265fc, 0x126608), (0x126788, 0x126794)),
    ('TutorialOn', 53, 'u8', 57, (0x126620, 0x12662c), (0x1267ac, 0x1267b8)),
    ('TooltipsOn', 54, 'u8', 68, (0x126644, 0x126650), (0x1267d0, 0x1267dc)),
    ('ConfirmDeleteOn', 55, 'u8', 79, (0x126668, 0x126674), (0x1267f4, 0x126800)),
    ('RMBScrollOn', 56, 'u8', 95, (0x12668c, 0x126698), (0x126818, 0x126824)),
    ('RMBCancelOn', 57, 'u8', 107, (0x1266b0, 0x1266bc), (0x12683c, 0x126848)),
    ('IsometricOn', 58, 'u8', 119, (0x1266d4, 0x1266e0), (0x126860, 0x12686c)),
]
WIDTH = {'u8': 1, 'u16le': 2, 'i32le': 4, 'u32raw+u32le': 8}
RESET = {'mEarnedGlobalTicket': [0] * 4, 'mEarnedSecretTicket': [0] * 2, 'mSpentTickets': 0, 'mExtraKeys': 0,
         'mEasyModeUser': 0, 'mSwearFilterOn': 1, 'mFirstTimePlayer': 1}


def branch(e: Evidence, offset: int, target: int) -> None:
    """Unconditional relative branch without link."""
    w = e.word(offset)
    disp = w & 0x03fffffc
    if disp & 0x02000000:
        disp -= 0x04000000
    if w >> 26 != 18 or w & 3 != 0 or offset + disp != target:
        e.fail(offset, 'branch', hex(w))
    e.checked += 1


def checked_call(e: Evidence, call: int, helper: int, name_base: int, name: int, expected: str,
                 name_site: int | None = None, success: int | None = None) -> None:
    """name in r5, call, then 'nonzero result -> return 1' (the caller's failure exit)."""
    e.d(call - 8 if name_site is None else name_site, 14, 5, 31, name)
    e.bl(call, helper)
    e.d(call + 4, 11, 0, 3, 0)
    e.bc(call + 8, 12, 2, call + 20 if success is None else success)
    e.d(call + 12, 14, 3, 0, 1)
    e.text(name_base + name, expected)


def io_helpers(e: Evidence) -> dict:
    """Width and byte order of every helper the player file uses; failure returns 1 and skips the swap."""
    out = {}
    for helper, (direction, width, encoding, glue, width_site, swap) in IO.items():
        import_at(e, glue, WRITE if direction == 'write' else READ)
        e.d(width_site, 14, 6, 0, width)
        if swap and swap[0] == 'rlwinm':
            e.rlwinm(swap[1], swap[2], 0, 8, 8, 15)
        elif swap and swap[0] == 'rlwinm16':
            e.rlwinm(swap[1], swap[2], 0, 24, 24, 31)
        elif swap:
            e.x(swap[1], 31, 662 if swap[0] == 'stwbrx' else 918, swap[2], 0, swap[3])
        out[hex(helper)] = f'{direction} {width} byte(s) {encoding}'
    # Read helpers write straight into the member; on failure they return 1 before the swap.
    e.d(0xc540, 14, 5, 29, 0)
    e.d(0xc5a4, 14, 3, 0, 1)
    e.d(0xc5c4, 14, 3, 0, 0)
    e.d(0xc348, 14, 5, 31, 0)
    e.d(0xc3ac, 14, 3, 0, 1)
    branch(e, 0xc3b0, 0xc3d8)
    e.d(0xc3d0, 14, 3, 0, 0)
    e.d(0x126f38, 14, 5, 29, 0)
    e.d(0x126f9c, 14, 3, 0, 1)
    e.d(0x126fb0, 32, 5, 29, 4)  # only the second word is swapped
    e.d(0x126fb4, 14, 0, 29, 4)
    out['failed_read'] = 'returns 1; the member already holds whatever bytes the read stored, unswapped'
    return out


def source_reads(e: Evidence, start: int, end: int, reg: int) -> list[int]:
    """Instructions in [start, end) that read reg as a source (forms used in these routines)."""
    stores = {36, 37, 38, 39, 44, 45, 52, 53, 54, 55}
    x_rs = {662, 918, 151, 215, 407, 444, 28, 316, 124, 24, 536, 792, 26, 954, 922, 824}
    hits = []
    for o in range(start, end, 4):
        w = e.word(o)
        op, rt, ra, rb = w >> 26, w >> 21 & 31, w >> 16 & 31, w >> 11 & 31
        if op in (46, 47):  # lmw/stmw: register save area, not a value use
            continue
        if op in (10, 11, 12, 13, 14, 15) or 32 <= op <= 55:
            if ra == reg and not (op in (14, 15) and ra == 0):
                hits.append(o)
            elif op in stores and rt == reg:
                hits.append(o)
        elif op in (20, 21, 23, 24, 25, 26, 27, 28, 29) and rt == reg:
            hits.append(o)
        elif op == 31 and (ra == reg or rb == reg or (rt == reg and w >> 1 & 0x3ff in x_rs)):
            hits.append(o)
    return hits


def version_gate(e: Evidence) -> dict:
    """The version reaches only the two sub-serializers, which never read it; the header gate is unsigned."""
    e.d(0x129108, 10, 0, 6, 12)  # cmplwi: unsigned
    e.bc(0x12910c, 4, 0, 0x129128)
    e.d(0x129324, 14, 29, 6, 0)
    users = {0x129308: source_reads(e, 0x129308, 0x12998c, 6), 'r29': source_reads(e, 0x129308, 0x12998c, 29),
             THEME_RECORD: source_reads(e, THEME_RECORD, 0x12a388, 6), OPTIONS: source_reads(e, OPTIONS, 0x1268a8, 6)}
    expected = {0x129308: [0x129324], 'r29': [0x129540, 0x129580, 0x129834, 0x1298ec], THEME_RECORD: [], OPTIONS: []}
    if users != expected:
        raise pef.PEFError(f'version argument users {users}')
    e.checked += 1
    for site in (0x129540, 0x129580, 0x129834, 0x1298ec):
        e.d(site, 14, 6, 29, 0)
    return {'written': 12, 'accepted': 'unsigned version >= 12 (so 0xFFFFFFFF is accepted)',
            'layout_gates': 'none: the version is passed on but never read; every accepted file uses one layout'}


def members(e: Evidence, table, record_reg: int, names: int) -> list[dict]:
    out = []
    for name, count, offset, stride, encoding, name_off, write, read in table:
        for site, base, call, helper in (write, read):
            if site is not None:
                e.d(site, 14, 4, base, offset)
            else:
                e.x(call - 12, 31, 266, 4, record_reg, 29)  # add r4, record, index
            # The last theme write falls through to the shared success exit.
            checked_call(e, call, helper, names, name_off, name, success=0x12a370 if call == 0x12a214 else None)
            if IO[helper][2] != encoding:
                raise pef.PEFError(f'{name}: helper encoding {IO[helper][2]}')
        out.append({'member': name, 'count': count, 'record_offset': offset, 'stride': stride or None,
                    'encoding': encoding, 'bytes': count * WIDTH[encoding]})
    return out


def player_record(e: Evidence) -> dict:
    exact_callers(e, SERIALIZE, [0x12900c, 0x129134])
    e.x(0x129310, 31, 444, 5, 28, 5)
    e.bc(0x129330, 12, 2, 0x129618)  # direction 0 -> read
    e.toc_load(0x129314, 30, OPTIONS_OBJECT)
    record = members(e, PLAYER, 26, NAMES)
    # Theme list: count, then per theme a char-string name, then the theme record.
    e.d(0x129458, 32, 0, 26, 52)
    checked_call(e, 0x129470, 0xc7f8, NAMES, 219, 'size', name_site=0x129464)
    import_at(e, 0x129498, 'Length__15TbStringBase<c>CFv')
    checked_call(e, 0x1294b4, 0xcd1b0, NAMES, 224, 'str_length')
    checked_call(e, 0x129504, 0x12aeb0, NAMES, 235, 'str[i]')
    e.bl(0x12954c, THEME_RECORD)
    checked_call(e, 0x12974c, 0xc2fc, NAMES, 219, 'size')
    e.x(0x1298d8, 31, 0, 0, 23, 0)  # cmpw: signed count
    e.bc(0x1298dc, 12, 0, 0x12976c)
    checked_call(e, 0x12977c, 0xcd0b4, NAMES, 249, 'name_str_len')
    import_at(e, 0x129798, '__nwa__FUl')
    checked_call(e, 0x1297bc, 0x12adbc, NAMES, 262, 'str[j]')
    e.x(0x1297e8, 31, 215, 0, 24, 3)  # stbx: NUL after the name
    e.d(0x129804, 14, 3, 0, 188)
    e.bl(0x129824, 0x129f18)
    e.bl(0x129838, THEME_RECORD)
    e.bl(0x12987c, 0x12b2ac)
    e.d(0x129880, 34, 24, 1, 232)
    e.d(0x129894, 10, 0, 24, 0)
    e.d(0x1298a0, 14, 4, 31, 269)
    e.text(NAMES + 269, 'Something went wrong when trying to insert the recently serialised (loaded) theme into the map\n')
    e.d(0x1298b8, 14, 3, 0, 1)
    theme = members(e, THEME, 27, NAMES)
    e.d(0x12a10c, 11, 0, 29, 6)
    e.d(0x12a174, 11, 0, 29, 4)
    e.d(0x12a1d8, 11, 0, 29, 33)
    for site, reg, bound in ((0x129364, 23, 4), (0x12939c, 23, 2), (0x129648, 23, 4), (0x129680, 23, 2),
                             (0x12a254, 29, 6), (0x12a2bc, 29, 4), (0x12a320, 29, 33)):
        e.d(site, 11, 0, reg, bound)  # loop bounds
    # Settings block, then the mystery-item set.
    e.bl(0x129584, OPTIONS)
    e.bl(0x1298f0, OPTIONS)
    exact_callers(e, OPTIONS, [0x129584, 0x1298f0])
    e.d(0x129598, 32, 0, 26, 40)
    checked_call(e, 0x1295b0, 0xc7f8, NAMES, 219, 'size', name_site=0x1295a4)
    e.d(0x1295d4, 40, 0, 3, 12)
    checked_call(e, 0x1295ec, 0xcdd44, NAMES, 242, 'rideId', name_site=0x1295e0)
    checked_call(e, 0x129914, 0xc2fc, NAMES, 219, 'size')
    checked_call(e, 0x129940, 0xcd960, NAMES, 242, 'rideId')
    e.d(0x129958, 14, 4, 26, 40)
    e.bl(0x129960, 0x12b5a8)
    e.x(0x12996c, 31, 0, 0, 24, 0)
    e.d(0x129974, 14, 3, 0, 0)
    options = []
    e.toc_load(0x126548, 31, ('code', OPTION_NAMES))
    for name, offset, encoding, name_off, write, read in OPTION:
        for site, call in (write, read):
            e.d(site, 14, 4, 29, offset)
            helper = (0x126ddc if site == write[0] else 0x126eec) if encoding != 'u8' else \
                     (0xca20 if site == write[0] else 0xc4f4)
            e.bl(call, helper)
            e.text(OPTION_NAMES + name_off, name)
        options.append({'member': name, 'object_offset': offset, 'encoding': encoding})
    e.bl(0x126884, 0x126460)
    theme_bytes = sum(m['bytes'] for m in theme)
    settings_bytes = sum(WIDTH[m['encoding']] for m in options)
    if (theme_bytes, settings_bytes) != (160, 39):
        raise pef.PEFError(f'record sizes {theme_bytes} {settings_bytes}')
    e.checked += 1
    return {'order': ['version', 'mEarnedGlobalTicket[4]', 'mEarnedSecretTicket[2]', 'mSpentTickets', 'mExtraKeys',
                      'mEasyModeUser', 'mSwearFilterOn', 'mFirstTimePlayer', 'theme count',
                      'themes: name length, name bytes, theme record', 'settings block',
                      'mystery count', 'mystery rideId values'],
            'player': record, 'theme_record': theme, 'theme_record_bytes': theme_bytes, 'settings': options,
            'settings_bytes': settings_bytes, 'theme_count': 'i32le, signed loop (a negative count reads no themes)',
            'theme_name': 'i32le length then that many bytes, no terminator on disk',
            'mystery': 'i32le count, then u16le rideId each (inserted into the player-wide set)',
            'settings_target': 'the static settings object (data 0x120a14), not the player record'}


def read_order(e: Evidence) -> dict:
    """Reset, clear themes, open, version, members; post-read swear-list hook only after full success."""
    e.bl(0x129088, INFO_RESET)
    e.bl(0x129090, 0x129c70)
    exact_callers(e, 0x129c70, [0x128730, 0x129090])
    e.d(0x1290e0 - 8, 14, 5, 28, 18)
    e.bl(0x1290e0, 0xc2fc)
    e.d(0x129150, 14, 3, 0, 0)
    # The hook reads the record through the global pointer; both readers store the new record there first.
    e.toc_load(0x129078, 31, ('data', 0x120d84))
    e.d(0x137924, 36, 27, 31, 0)
    e.d(0x136d18, 36, 22, 28, 0)
    e.bl(0x1291e8, 0x129ad8)
    e.d(0x129214, 14, 4, 28, 48)
    e.text(NAMES + 48, '%s:Language:%s:swears.txt')
    e.d(0x129240, 14, 4, 28, 74)
    e.text(NAMES + 74, '%s:Language:%s:alloweds.txt')
    e.bl(0x12925c, 0x1a5594)
    e.d(0x1292e8, 14, 4, 0, 0)
    e.bl(0x1292ec, 0x129ad0)  # word lists failed: filter off
    e.d(0x1292f0, 14, 3, 0, 1)
    # The reset touches neither the mystery set (+40) nor the theme map; both readers pass a new record.
    for o in range(INFO_RESET, 0x128814, 4):
        w = e.word(o)
        if w >> 26 in (36, 38) and w >> 16 & 31 == 3 and (w & 0xffff) in (40, 44, 48, 52, 56, 64):
            e.fail(o, 'reset touches a container', hex(w))
    e.checked += 1
    e.bl(0x137920, 0x1284e4)
    e.bl(0x136d14, 0x1284e4)
    e.bl(0x128548, INFO_RESET)
    return {'order': ['reset scalar members (0x1287d4)', 'free theme records (0x129c70)', 'open', 'version',
                      'members in file order', 'swear-list hook (success only)'],
            'partial_failure': 'members read before the failing one keep their file values; later ones keep '
                               'reset values; a theme that fails or duplicates a name is not inserted; settings '
                               'members read before the failure are already in the global settings object',
            'swear_hook': 'mSwearFilterOn set and swears.txt/alloweds.txt not loadable -> mSwearFilterOn = 0 '
                          '(in memory; written at the next save)'}


def key_award(e: Evidence) -> dict:
    """Creation writes 0 extra keys; the later award adds 1 and saves; both writes ignore failure."""
    e.d(0x128800, 36, 4, 3, 32)
    e.d(0x1287d4, 14, 4, 0, 0)
    e.bl(0x137548, 0x137600)
    e.bl(0x13755c, 0x12870c)
    e.bl(0x1375d8, SAVE)
    e.d(0x1375dc, 14, 3, 1, 136)  # result overwritten
    e.bl(0x15cffc, CREATE)
    e.bl(0x15d034, LOAD)
    e.bl(0x1378b4, 0x12870c)  # load frees the in-memory record ...
    e.d(0x1378bc, 14, 0, 0, 0)
    e.d(0x1378c0, 36, 0, 31, 0)
    e.bl(0x137930, INFO_LOAD)  # ... and re-reads the file just written
    e.bl(0x15d144, 0x15cf00)
    e.d(0x15d158, 14, 0, 0, 1)
    e.d(0x15d164, 36, 0, 5, 0)
    e.bl(0x15d178, 0x15cd38)
    e.d(0x15ce30, 11, 0, 0, 2)
    e.bc(0x15ce34, 4, 2, 0x15ce50)
    e.bl(0x15ce80, 0x128f3c)
    e.d(0x128f40, 14, 0, 4, 1)
    e.bl(0x15ceb4, SAVE_SLOT)
    e.d(0x15ceb8, 32, 3, 30, 0)  # result overwritten
    exact_callers(e, 0x128f3c, [0x15ce80])
    exact_callers(e, INFO_SAVE, [0x137e98])
    return {'creation': 'directories and easymode copy, fresh record (mExtraKeys 0, mFirstTimePlayer 1), gms.dat '
                        'written; the result is not used',
            'selection_after_creation': 'the record is freed and gms.dat re-read, so the mode and keys come from '
                                        'the file, not from the record creation built',
            'award': 'new-player flag set -> GameType 2: lobby message only; any other GameType (0 or 1): '
                     'mExtraKeys += 1, gms.dat written (result unused), message 393',
            'ordering_consequences': [
                'creation write failed: selection reads reset values -> GameType 0 (even for an Instant Action '
                'choice, whose easymode copies already exist) and the award adds 1 key in memory',
                'award write failed: the file keeps mExtraKeys 0; the in-memory 1 reaches disk only at a later '
                'successful write (leaving a park, unload); the award cannot repeat once a slot is named']}


def keys_source(e: Evidence) -> dict:
    """Keys() = mExtraKeys + signed(earned / 3); earned counts non-zero bytes; locals only for usable themes."""
    e.d(0x128b7c, 34, 0, 3, 24)
    e.d(0x128b94, 34, 0, 29, 25)
    e.d(0x128bb0, 34, 0, 29, 26)
    e.bl(0x128c04, 0x12a50c)
    e.bl(0x128c1c, 0x12a388)
    e.d(0x128c38, 34, 0, 29, 38)
    e.d(0x128c50, 34, 0, 29, 39)
    e.d(0x128c6c, 15, 3, 0, 21845)
    e.d(0x128c74, 14, 0, 3, 21846)
    e.x(0x128c7c, 31, 75, 3, 0, 5)  # mulhw: signed
    e.rlwinm(0x128c80, 3, 0, 1, 31, 31)
    e.d(0x128c78, 32, 4, 29, 32)
    # Local-ticket count: six bytes, each non-zero byte counts 1.
    e.d(0x12a388, 34, 0, 3, 0)
    e.d(0x12a38c, 10, 0, 0, 0)
    e.d(0x12a394, 14, 0, 0, 1)
    # Usable theme: global.sam object present or loaded now; a failed load frees it and logs.
    e.d(0x12a534, 32, 0, 3, 28)
    e.d(0x12a5f4, 14, 3, 0, 1)
    e.d(0x12a5ec, 14, 3, 0, 0)
    e.d(0x12a590, 14, 4, 31, 735)
    e.text(NAMES + 735, 'data:levels:%s:global.sam')
    # Available tickets subtract mSpentTickets; Keys() does not.
    e.d(0x128b38, 32, 0, 29, 28)
    e.x(0x128b40, 31, 40, 3, 0, 3)  # subf r3, r0, r3
    readers = exact_callers(e, 0x128b60, [0x965cc, 0x129cf8, 0x129e38, 0x155c74, 0x155cb4, 0x155cec,
                                          0x155d7c, 0x184194, 0x184304, 0x184354, 0x1843f0])
    return {'keys': 'mExtraKeys + trunc_toward_zero(earned / 3)', 'earned': 'non-zero global bytes + non-zero '
            'local bytes of every theme whose global.sam loads + non-zero secret bytes',
            'side_effect': 'counting loads global.sam for each listed theme that has none yet',
            'spent': 'only available tickets subtract mSpentTickets', 'readers': readers,
            'instant_action_bypass': 'theme door: GameType 2 enters without calling Keys() (profile_evidence)'}


def saved_flags(e: Evidence) -> dict:
    """mFirstTimePlayer and mSwearFilterOn: every accessor call site."""
    e.d(0x129f04, 34, 3, 3, 72)
    e.d(0x129f10, 38, 0, 3, 72)
    exact_callers(e, 0x129f04, [0x1c208c])
    exact_callers(e, 0x129f0c, [0x1c20d4])
    e.toc_load(0x1c1228, 30, ('data', GAME_TYPE_DATA))
    for o in range(0x1c122c, 0x1c2044, 4):  # r30 still holds &GameType
        w = e.word(o)
        op, rt, ra = w >> 26, w >> 21 & 31, w >> 16 & 31
        if (op in (14, 15, 32, 33, 34, 35, 40, 41, 42, 43) and rt == 30) or (op == 46 and rt <= 30) or \
                (op == 31 and ra == 30 and w >> 1 & 0x3ff in (444, 28, 316, 266, 40, 8, 23, 87, 279, 311)):
            e.fail(o, 'r30 rewritten', hex(w))
    e.checked += 1
    e.d(0x1c2044, 32, 0, 30, 0)
    e.d(0x1c2048, 11, 0, 0, 1)
    e.bc(0x1c204c, 12, 2, 0x1c21d8)
    e.bc(0x1c2094, 12, 2, 0x1c21d8)
    e.d(0x1c20f8, 11, 0, 0, 2)
    e.bc(0x1c20fc, 4, 2, 0x1c21d8)
    for o in range(0x1c2044, 0x1c21d8, 4):
        w = e.word(o)
        if w >> 26 == 18 and w & 1:
            disp = w & 0x03fffffc
            disp = disp - 0x04000000 if disp & 0x02000000 else disp
            if o + disp in (SAVE, SAVE_SLOT, INFO_SAVE):
                e.fail(o, 'player file written at first-time clear', hex(o + disp))
    e.checked += 1
    swear_writers = exact_callers(e, 0x129ad0, [0x1292ec, 0x1af8c8, 0x1af944])
    swear_readers = exact_callers(e, 0x129ad8, [0x1291e8, 0x1835e4, 0x18a94c, 0x18b7bc, 0x18beb8, 0x18bf6c,
                                                0x18c700, 0x1ad0d0, 0x1af888])
    return {'mFirstTimePlayer': 'set by reset (creation); cleared in memory after a park is entered when GameType '
                                'is not 1 and the flag is set; GameType 2 then posts two events (10, 0) to the '
                                'object at data 0x11f9bc (meaning not traced); persisted only by a later write',
            'mSwearFilterOn': {'writers': swear_writers, 'readers': swear_readers,
                               'note': 'reset 1; cleared by the read hook when the word lists fail'},
            'counters': 'mExtraKeys (only +1 at the award), mSpentTickets (mystery purchase), ticket bytes'}


def inspect_player_file(e: Evidence) -> dict:
    return {'player_file_io': io_helpers(e), 'player_file_version': version_gate(e),
            'player_file_schema': player_record(e), 'player_file_read_order': read_order(e),
            'key_award_order': key_award(e), 'keys_source': keys_source(e), 'saved_profile_flags': saved_flags(e)}


# -- reference reader (synthetic bytes only) ----------------------------------------------------------
class Truncated(Exception):
    pass


def _take(raw: bytes, pos: int, n: int) -> tuple[bytes, int]:
    if pos + n > len(raw):
        raise Truncated(pos)
    return raw[pos:pos + n], pos + n


def _value(raw: bytes, pos: int, encoding: str):
    chunk, pos = _take(raw, pos, WIDTH[encoding])
    if encoding == 'u8':
        return chunk[0], pos
    if encoding == 'u16le':
        return struct.unpack('<H', chunk)[0], pos
    if encoding == 'i32le':
        return struct.unpack('<i', chunk)[0], pos
    return (chunk[:4], struct.unpack('<I', chunk[4:])[0]), pos  # first word in Mac native order


def read_mac_player_file(raw: bytes) -> dict:
    """Traced read order and failure rules. ``ok`` mirrors the reader's return; callers ignore it."""
    record = {k: (list(v) if isinstance(v, list) else v) for k, v in RESET.items()}
    out = {'ok': False, 'record': record, 'themes': {}, 'settings': {}, 'mystery': set(), 'failed_at': None}
    pos = 0
    try:
        version, pos = _value(raw, pos, 'i32le')
        if version & 0xffffffff < 12:
            out['failed_at'] = 'version'
            return out
        for name, count, _, _, encoding, _, _, _ in PLAYER:
            key = name.removesuffix('[i]')
            for i in range(count):
                out['failed_at'] = name
                value, pos = _value(raw, pos, encoding)
                if count > 1:
                    record[key][i] = value
                else:
                    record[key] = value
        out['failed_at'] = 'theme count'
        themes, pos = _value(raw, pos, 'i32le')
        for _ in range(max(themes, 0)):
            out['failed_at'] = 'theme name'
            length, pos = _value(raw, pos, 'i32le')
            name, pos = _take(raw, pos, length & 0xffffffff)
            out['failed_at'] = 'theme record'
            theme = {'mEarnedLocalTicket[i]': [], 'mAward[i]': [], 'mAwardScore[i]': [],
                     'mSignNameA[i]': [], 'mSignNameB[i]': []}
            for _ in range(6):
                value, pos = _value(raw, pos, 'u8')
                theme['mEarnedLocalTicket[i]'].append(value)
            for _ in range(4):
                for member, encoding in (('mAward[i]', 'u8'), ('mAwardScore[i]', 'i32le')):
                    value, pos = _value(raw, pos, encoding)
                    theme[member].append(value)
            for _ in range(33):
                for member in ('mSignNameA[i]', 'mSignNameB[i]'):
                    value, pos = _value(raw, pos, 'u16le')
                    theme[member].append(value)
            theme['mNameChanged'], pos = _value(raw, pos, 'u8')
            theme['mAllResearchCompleted'], pos = _value(raw, pos, 'u8')
            if name in out['themes']:
                out['failed_at'] = 'duplicate theme'
                return out
            out['themes'][name] = theme
        for name, _, encoding, _, _, _ in OPTION:
            out['failed_at'] = name
            out['settings'][name], pos = _value(raw, pos, encoding)
        out['failed_at'] = 'mystery count'
        count, pos = _value(raw, pos, 'i32le')
        for _ in range(max(count, 0)):
            out['failed_at'] = 'rideId'
            ride, pos = _value(raw, pos, 'u16le')
            out['mystery'].add(ride)
    except Truncated:
        return out
    out['ok'], out['failed_at'], out['consumed'] = True, None, pos
    return out

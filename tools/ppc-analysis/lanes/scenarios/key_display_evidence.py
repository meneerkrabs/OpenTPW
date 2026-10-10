"""Front-end key/ticket displays, the theme-door display gate and mystery-purchase spending (Feral Mac).

Same rules as scenario_evidence.py: bounded instruction-field checks at named
offsets, interpreted values only, nothing executed. Every witness belongs to the
identified Mac binary and is not evidence for the PC ``TP.EXE`` or Patch 2.

The reference functions at the end restate the traced rules over caller-supplied
values with explicit preconditions. They are exercised on synthetic values only.
They do not load ``global.sam``, read ``gms.dat`` or name any screen, control
art or path; theme usability is an input, not something they decide.
"""
from __future__ import annotations

from pathlib import Path
import sys

sys.path.insert(0, str(Path(__file__).resolve().parent))
from scenario_evidence import Evidence, pef  # noqa: E402
from followup_evidence import data_refs, exact_callers, toc_users  # noqa: E402
from park_entry_evidence import wide  # noqa: E402
from player_file_evidence import branch  # noqa: E402

KEYS = 0x128b60          # Keys() = mExtraKeys + earned / 3
AVAILABLE = 0x128a2c     # earned - mSpentTickets
COST = 0x12a4c8          # CostToEnter getter: -1 unless the theme's global.sam is usable
USABLE = 0x12a50c        # loads global.sam on demand
THEME_LOOKUP = 0x129ae0  # find-or-create the per-theme record; 0 unless usable
CONTROL = 0x17f770       # child control by id
CHECKED = 0x17fa64       # sets/clears bit 0 of control +0x44 (UI lane: meaning not settled)
STATE = 0x17f450         # 16-bit control state setter (UI lane)
GAME_TYPE = ('data', 0x53d98)
GAME_TYPE_READY = ('data', 0x53d9c)
PLAYER = ('data', 0x120d84)
HUD_PROC = 0x155b7c
LOBBY_REFRESH = 0x184028
LOBBY_CACHED = 0x183dd0
KEY_CACHE = 0x135fb0
KEY_CACHE_SLOT = 0x416c  # TOC slot naming KEY_CACHE
AFFORDABLE = 0xd313c
PURCHASE = 0xd3000
INSERT = 0x128ca8
FRONT_END_KEY_READERS = [0x155c74, 0x155cb4, 0x155cec, 0x155d7c, 0x184194, 0x184304, 0x184354, 0x1843f0]


def li(e: Evidence, offset: int, reg: int, value: int) -> None:
    e.d(offset, 14, reg, 0, value)


def set_flag(e: Evidence, select: int, control: int, flag_site: int, flag: int, call: int) -> None:
    """li r4,<id>; bl child; li r4,<0|1>; bl CHECKED."""
    li(e, select, 4, control)
    e.bl(select + 4, CONTROL)
    li(e, flag_site, 4, flag)
    e.bl(call, CHECKED)


# -- in-park count panel (window proc 0x155b7c, control ids 0x34..0x37 under child 0x33) ---------------
def count_panel(e: Evidence) -> dict:
    e.toc_load(0x155b8c, 27, ('code', 0x1da332))
    e.toc_load(0x155b94, 30, PLAYER)
    e.toc_load(0x155b9c, 22, GAME_TYPE)
    e.toc_load(0x155ba4, 23, GAME_TYPE_READY)
    e.toc_load(0x155ba8, 24, ('data', 0x134c84))  # cached key count
    e.toc_load(0x155bb0, 29, ('data', 0x134c8c))  # cached available-ticket count
    fmt = wide(e, 0x1da332, '%d  x')
    # Message 0x15 resets both caches to -1; message 0x1e refreshes.
    e.d(0x155bf4, 11, 0, 28, 30)
    e.bc(0x155bf8, 12, 2, 0x155c1c)
    e.d(0x155c00, 11, 0, 28, 21)
    li(e, 0x155c0c, 0, -1)
    e.d(0x155c10, 36, 0, 29, 0)
    e.d(0x155c14, 36, 0, 24, 0)
    # GameType 2 skips the key group entirely (straight to the ticket group).
    e.bl(0x155c2c, 0x12bb64)
    e.d(0x155c38, 32, 0, 22, 0)
    e.d(0x155c3c, 11, 0, 0, 2)
    e.bc(0x155c40, 12, 2, 0x155db8)
    # Keys group: unchanged cache -> nothing; else cache, then 0 -> both controls cleared.
    e.bl(0x155c74, KEYS)
    e.d(0x155c78, 32, 0, 24, 0)
    e.x(0x155c7c, 31, 0, 0, 0, 3)
    e.bc(0x155c80, 12, 2, 0x155db8)
    e.bl(0x155cb4, KEYS)
    e.d(0x155cb8, 36, 3, 24, 0)
    e.bl(0x155cec, KEYS)
    e.d(0x155cf0, 11, 0, 3, 0)
    e.bc(0x155cf4, 4, 2, 0x155d24)
    set_flag(e, 0x155cfc, 0x36, 0x155d04, 0, 0x155d08)
    set_flag(e, 0x155d10, 0x34, 0x155d18, 0, 0x155d1c)
    set_flag(e, 0x155d28, 0x36, 0x155d30, 1, 0x155d34)
    set_flag(e, 0x155d3c, 0x34, 0x155d44, 1, 0x155d48)
    e.bl(0x155d7c, KEYS)
    e.d(0x155d80, 14, 6, 3, 0)
    e.d(0x155d84, 14, 5, 27, 0)
    li(e, 0x155d8c, 4, 8)
    e.import_call(0x155d90, 'swprintf')
    li(e, 0x155d9c, 4, 0x36)
    e.d(0x155da4, 32, 12, 3, 0x118)
    e.d(0x155dac, 32, 12, 12, 0x2c)
    # Ticket group: always runs; shows AVAILABLE tickets (spent subtracted), not earned.
    e.bl(0x155de8, AVAILABLE)
    e.d(0x155dec, 32, 0, 29, 0)
    e.x(0x155df0, 31, 0, 0, 0, 3)
    e.bc(0x155df4, 12, 2, 0x155e90)
    e.d(0x155df8, 11, 0, 3, 0)
    e.d(0x155dfc, 36, 3, 29, 0)
    e.bc(0x155e00, 4, 2, 0x155e30)
    set_flag(e, 0x155e08, 0x37, 0x155e10, 0, 0x155e14)
    set_flag(e, 0x155e1c, 0x35, 0x155e24, 0, 0x155e28)
    set_flag(e, 0x155e34, 0x37, 0x155e3c, 1, 0x155e40)
    set_flag(e, 0x155e48, 0x35, 0x155e50, 1, 0x155e54)
    e.d(0x155e58, 32, 6, 29, 0)
    li(e, 0x155e64, 4, 8)
    e.import_call(0x155e68, 'swprintf')
    li(e, 0x155e74, 4, 0x37)
    # The proc is only address-taken: transition vector data 0x7f70, named by one data word,
    # installed on child 0x33 by the builder 0x156bac (sole caller 0x13cfb4).
    if e.calls_to(HUD_PROC) or e.slot_target(0x7f70) != ('code', HUD_PROC) or data_refs(e, 0x7f70) != [0x1c6c]:
        e.fail(HUD_PROC, 'count panel proc references', data_refs(e, 0x7f70))
    e.checked += 1
    li(e, 0x156c34, 4, 0x33)
    e.toc_load(0x156c68, 4, ('data', 0x7f70))
    e.bl(0x156c6c, 0x180264)
    e.toc_load(0x156bc8, 25, ('code', 0x1da332))
    li(e, 0x156c74, 4, 0x37)
    e.d(0x156d00, 14, 4, 25, 12)
    initial = wide(e, 0x1da332 + 12, '0  x')
    e.toc_load(0x156bcc, 26, GAME_TYPE)
    e.d(0x156d30, 32, 0, 26, 0)
    e.d(0x156d34, 11, 0, 0, 2)
    e.bc(0x156d38, 12, 2, 0x156df8)
    builder = e.sole_callers(0x156bac, [0x13cfb4])
    # CHECKED: bit 0 of +0x44, set by ori 1, cleared by rlwinm 0,0,30.
    e.d(0x17fa84, 32, 3, 3, 0x44)
    e.rlwinm(0x17fabc, 0, 0, 0, 0, 30)
    e.d(0x17facc, 24, 0, 0, 1)
    return {'proc': hex(HUD_PROC), 'builder_callers': builder, 'format': fmt, 'ticket_initial_text': initial,
            'messages': {'0x15': 'both caches = -1', '0x1e': 'refresh'},
            'key_group': 'GameType 2: untouched; else Keys() != cache -> cache = Keys(); 0 -> bit 0 of +0x44 '
                         'cleared on 0x36 and 0x34; > or < 0 -> set on both and 0x36 text = format(Keys())',
            'ticket_group': 'every GameType: available tickets (earned - mSpentTickets) != cache -> same pattern '
                            'on 0x37/0x35',
            'builder_game_type_2': 'the builder branches on GameType 2 at 0x156d38 (UI lane: 0x34 hidden)'}


# -- lobby key display: live refresh 0x184028 and cached compare 0x183dd0 ----------------------------
def lobby_display(e: Evidence) -> dict:
    e.toc_load(0x184030, 29, ('data', 0x135fb4))  # lobby window pointer
    e.toc_load(0x184038, 31, PLAYER)
    e.toc_load(0x18403c, 26, GAME_TYPE)
    e.toc_load(0x184040, 27, GAME_TYPE_READY)
    e.toc_load(0x184044, 28, ('data', 0x135fac))  # looping sound handle
    e.d(0x184050, 32, 0, 29, 0)
    e.d(0x184054, 10, 0, 0, 0)
    e.bc(0x184058, 12, 2, 0x1843fc)
    # Door group: the theme record must be usable, else the group is left as it is.
    e.bl(0x1840c8, THEME_LOOKUP)
    e.d(0x1840e4, 15, 4, 0, 2)
    e.d(0x1840ec, 14, 4, 4, -0x1f16)
    e.bl(0x1840f0, CONTROL)
    e.d(0x1840f4, 10, 0, 25, 0)
    e.bc(0x1840fc, 12, 2, 0x184298)
    e.d(0x18411c, 32, 0, 26, 0)
    e.d(0x184120, 11, 0, 0, 2)
    e.bc(0x184124, 4, 2, 0x184140)
    li(e, 0x18412c, 4, 0)
    e.bl(0x184130, CHECKED)
    # Signed Keys() < CostToEnter -> locked branch; the same predicate as the door (cost <= keys enters).
    e.bl(0x184188, COST)
    e.d(0x18418c, 14, 23, 3, 0)
    e.bl(0x184194, KEYS)
    e.x(0x184198, 31, 0, 0, 3, 23)
    e.bc(0x18419c, 12, 0, 0x184250)
    li(e, 0x1841a4, 4, 0)
    e.bl(0x1841a8, STATE)
    e.d(0x1841c0, 14, 4, 4, -0x1f15)
    e.d(0x1841c8, 14, 4, 25, -1)
    e.bl(0x1841cc, STATE)
    li(e, 0x184230, 3, 0x61)
    e.bl(0x184238, 0x9fd70)
    li(e, 0x184254, 4, 1)
    e.bl(0x184258, STATE)
    e.d(0x184270, 14, 4, 4, -0x1f15)
    e.d(0x184278, 14, 4, 25, 4)
    e.bl(0x18428c, 0x9f4f0)
    # Count group: GameType 2 clears it; Keys() <= 0 clears it; else text = format(Keys()).
    e.d(0x1842a0, 14, 4, 4, -0x1f14)
    e.d(0x1842cc, 11, 0, 0, 2)
    e.bc(0x1842d0, 12, 2, 0x1843ac)
    e.bl(0x184304, KEYS)
    e.d(0x184308, 11, 0, 3, 0)
    e.bc(0x18430c, 4, 1, 0x18439c)
    li(e, 0x184314, 4, 1)
    e.bl(0x184318, CHECKED)
    e.bl(0x184354, KEYS)
    e.toc_load(0x184358, 4, ('code', 0x1db022))
    e.d(0x184364, 14, 5, 4, 0x18)
    li(e, 0x184368, 4, 0x3fff)
    e.import_call(0x18436c, 'swprintf')
    e.d(0x18437c, 14, 4, 4, -0x1f12)
    li(e, 0x1843a0, 4, 0)
    li(e, 0x1843b0, 4, 0)
    fmt = wide(e, 0x1db022 + 0x18, '%d x')
    # Every refresh, any GameType, ends by caching Keys().
    e.bl(0x1843f0, KEYS)
    e.toc_load(0x1843f4, 4, ('data', KEY_CACHE))
    e.d(0x1843f8, 36, 3, 4, 0)
    refresh_callers = exact_callers(e, LOBBY_REFRESH, [0x75e0, 0x183830, 0x183a94])
    # Cache users: lobby init zeroes it and refreshes at once; the cached compare reads it.
    if toc_users(e, KEY_CACHE_SLOT) != [0x183a80, 0x183ed4, 0x1843f4]:
        e.fail(KEY_CACHE_SLOT, 'key cache users', [hex(o) for o in toc_users(e, KEY_CACHE_SLOT)])
    e.checked += 1
    li(e, 0x183a84, 0, 0)
    e.d(0x183a8c, 36, 0, 4, 0)
    e.bl(0x183a94, LOBBY_REFRESH)
    e.d(0x183eb4, 11, 0, 0, 2)
    e.bc(0x183eb8, 4, 2, 0x183ecc)
    e.bl(0x183ed0, COST)
    e.d(0x183ed8, 32, 0, 4, 0)
    e.x(0x183edc, 31, 0, 0, 0, 3)
    e.bc(0x183ee0, 12, 0, 0x183f94)
    e.d(0x183f0c, 14, 4, 24, -1)
    e.d(0x183fbc, 14, 4, 24, 4)
    cached_callers = exact_callers(e, LOBBY_CACHED, [0x960bc, 0x962dc, 0x9675c, 0x967e0, 0x15cee8])
    return {'refresh': hex(LOBBY_REFRESH), 'refresh_callers': refresh_callers, 'format': fmt,
            'door_group_controls': {'state': hex(0x1e0ea), 'value': hex(0x1e0eb)},
            'door_group': 'record unusable -> untouched; GameType 2 -> bit 0 cleared; else signed Keys() < '
                          'CostToEnter -> state 1, value cost+4, sound stopped; otherwise state 0, value cost-1, '
                          'looping sound 0x61 started if none',
            'count_group_controls': {'group': hex(0x1e0ec), 'text': hex(0x1e0ee)},
            'count_group': 'GameType 2 or Keys() <= 0 -> bit 0 cleared; else set and text = format(Keys())',
            'cache': hex(KEY_CACHE), 'cached_compare': hex(LOBBY_CACHED), 'cached_compare_callers': cached_callers,
            'cached_rule': 'same signed cached-keys < cost test; GameType 2 clears the door group',
            'presentation': 'state/value/sound meanings (locked art, labels) are not traced'}


# -- door gate order and the shared getters ------------------------------------------------------------
def door_and_getters(e: Evidence) -> dict:
    e.d(0x964f0, 32, 0, 3, 0x14)
    e.bc(0x964f8, 4, 2, 0x96610)
    e.d(0x964fc, 32, 0, 30, 0xc)
    e.bc(0x96504, 12, 2, 0x96610)
    e.d(0x96528, 11, 0, 0, 2)
    e.bc(0x9652c, 12, 2, 0x965fc)
    e.bl(0x96578, THEME_LOOKUP)
    e.bc(0x96598, 12, 2, 0x96610)
    e.bl(0x965cc, KEYS)
    e.bl(0x965d8, COST)
    e.x(0x965dc, 31, 0, 0, 3, 31)
    e.bc(0x965e0, 12, 1, 0x96610)
    # Lookup: find or create, then 0 unless the theme's global.sam is usable.
    e.bl(0x129b28, USABLE)
    li(e, 0x129b3c, 3, 0)
    e.bl(0x129b60, 0x129b9c)
    e.bl(0x129b68, USABLE)
    li(e, 0x129b7c, 3, 0)
    # Cost getter: usable -> global.sam object +20, else -1.
    e.bl(0x12a4dc, USABLE)
    e.d(0x12a4e8, 32, 3, 31, 0x1c)
    e.d(0x12a4ec, 32, 3, 3, 0x14)
    li(e, 0x12a4f4, 3, -1)
    cost_callers = exact_callers(e, COST, [0x965d8, 0x129e2c, 0x183ed0, 0x183ef4, 0x183fa4, 0x184188,
                                           0x1841b0, 0x184260])
    # Keys(): signed 32-bit add of mExtraKeys (+32) and the truncated third; no +28 (spent) load.
    e.d(0x128c78, 32, 4, 29, 32)
    e.x(0x128c7c, 31, 75, 3, 0, 5)
    e.rlwinm(0x128c80, 3, 0, 1, 31, 31)
    e.x(0x128c84, 31, 266, 0, 3, 0)
    e.x(0x128c88, 31, 266, 3, 4, 0)
    for o in range(KEYS, INSERT, 4):
        w = e.word(o)
        if w >> 26 in (32, 33) and w & 0xffff == 28:
            e.fail(o, 'Keys() reads mSpentTickets', hex(w))
    e.checked += 1
    # Available tickets: same earned count (usable themes only), minus +28, 32-bit subtract.
    e.bl(0x128ad0, USABLE)
    e.bl(0x128ae8, 0x12a388)
    e.d(0x128b38, 32, 0, 29, 28)
    e.x(0x128b40, 31, 40, 3, 0, 3)
    available_callers = exact_callers(e, AVAILABLE, [0xd319c, 0x155de8])
    readers = exact_callers(e, KEYS, sorted([0x965cc, 0x129cf8, 0x129e38] + FRONT_END_KEY_READERS))
    return {'door_order': ['already entering (+20) -> nothing', 'no target theme (+12) -> nothing',
                           'GameType 2 -> enter', 'theme record unusable -> nothing',
                           'Keys() then CostToEnter; signed cost > keys -> nothing', 'enter'],
            'cost_getter_callers': cost_callers, 'available_callers': available_callers, 'keys_readers': readers,
            'front_end_key_readers': [hex(o) for o in FRONT_END_KEY_READERS],
            'widths': 'mExtraKeys, mSpentTickets, CostToEnter, Keys() and available tickets are signed 32-bit; '
                      'Keys() adds and available subtracts with 32-bit wrap'}


# -- mystery purchase: tickets spent, keys untouched ---------------------------------------------------
def mystery_spending(e: Evidence) -> dict:
    # Affordability: signed cost (catalog +196) <= available tickets; no GameType test.
    e.d(0xd316c, 32, 30, 3, 0xc4)
    e.bl(0xd319c, AVAILABLE)
    e.x(0xd31a0, 31, 0, 0, 30, 3)
    e.bc(0xd31a4, 12, 1, 0xd31b0)
    li(e, 0xd31a8, 3, 1)
    li(e, 0xd31b0, 3, 0)
    game_type_slot = e.toc - 0x75b8
    if [o for o in toc_users(e, game_type_slot) if AFFORDABLE <= o < 0xd31d0]:
        e.fail(AFFORDABLE, 'GameType read in affordability', None)
    e.checked += 1
    gate_callers = exact_callers(e, AFFORDABLE, [0xd3064, 0x163240, 0x163a04, 0x164094])
    # Purchase: GameType 1 -> 1 unrecorded; unaffordable -> 0; else insert (key = high half of the id word).
    e.d(0xd3024, 36, 4, 1, 0x7c)
    e.d(0xd3034, 32, 28, 3, 0xc4)
    e.d(0xd3054, 11, 0, 0, 1)
    e.bc(0xd3058, 12, 2, 0xd30b8)
    li(e, 0xd30b8, 3, 1)
    e.bl(0xd3064, AFFORDABLE)
    e.bc(0xd306c, 4, 2, 0xd3078)
    li(e, 0xd3070, 3, 0)
    e.x(0xd30a8, 31, 444, 28, 5, 28)
    e.d(0xd30ac, 40, 4, 1, 0x7c)
    e.bl(0xd30b0, INSERT)
    e.d(0xd30e8, 36, 4, 1, 0x6c)
    e.d(0xd311c, 40, 4, 1, 0x6c)
    # Insert: only a new id adds the cost to +28 (signed 32-bit add) and returns 1.
    e.d(0x128cc4, 44, 4, 1, 0x9e)
    e.d(0x128cd8, 34, 0, 1, 0x60)
    e.bc(0x128ce0, 4, 2, 0x128cec)
    li(e, 0x128ce4, 3, 0)
    e.d(0x128cec, 32, 0, 30, 0x1c)
    li(e, 0x128cf0, 3, 1)
    e.x(0x128cf4, 31, 266, 0, 0, 31)
    e.d(0x128cf8, 36, 0, 30, 0x1c)
    for o in range(INSERT, 0x128d14, 4):
        w = e.word(o)
        if w >> 26 in (36, 37) and w & 0xffff == 32:
            e.fail(o, 'purchase writes mExtraKeys', hex(w))
    e.checked += 1
    # Placement: cost > 0 and not owned -> purchase (result ignored, no money); else money price.
    e.d(0xdacc8, 32, 0, 28, 0xc4)
    e.d(0xdaccc, 11, 0, 0, 0)
    e.bc(0xdacd0, 4, 1, 0xdad00)
    e.bl(0xdace0, 0xd30d0)
    e.bc(0xdace8, 4, 2, 0xdad00)
    e.bl(0xdacf8, PURCHASE)
    branch(e, 0xdacfc, 0xdad10)
    e.bl(0xdad0c, 0xcbfdc)
    # Build-menu gate: cost != 0 and not owned -> affordability; refusal calls 0x139a40 and leaves.
    e.d(0x1639d8, 11, 0, 0, 0)
    e.bc(0x1639dc, 12, 2, 0x163b04)
    e.bl(0x1639ec, 0xd30d0)
    e.bc(0x1639f4, 4, 2, 0x163b04)
    e.bc(0x163a0c, 12, 2, 0x163afc)
    e.bl(0x163afc, 0x139a40)
    branch(e, 0x163b00, 0x163e54)
    li(e, 0x139a50, 5, 0x1d)
    e.bl(0x139a68, 0xbb4fc)
    if e.slot_target(0x8090) != ('code', 0x163748):
        e.fail(0x163748, 'build-menu proc vector', e.slot_target(0x8090))
    e.checked += 1
    return {'affordability': 'signed cost <= earned - mSpentTickets; no GameType test', 'gate_callers': gate_callers,
            'purchase': ['GameType 1 -> success, nothing recorded or spent',
                         'unaffordable -> 0, nothing recorded or spent',
                         'id already owned -> 0, nothing spent', 'else id inserted, mSpentTickets += cost'],
            'owned_set_key': 'high 16 bits of the 32-bit item-id word (stored at +0x7c, read by lhz +0x7c)',
            'keys_unchanged': 'no purchase path writes mExtraKeys; Keys() never reads mSpentTickets',
            'placement': 'cost > 0 and id not owned -> purchase only (result ignored, no money even when '
                         'unaffordable); else money price', 'menu_gate': 'proc 0x163748 (vector data 0x8090): '
                         'cost != 0 and not owned -> unaffordable calls 0x139a40 (0xbb4fc with 29) and returns',
            'gate_differences': 'menu tests cost != 0, placement tests cost > 0; shipped costs not re-checked here'}


def inspect_key_display(e: Evidence) -> dict:
    return {'key_count_panel': count_panel(e), 'lobby_key_display': lobby_display(e),
            'key_door_and_getters': door_and_getters(e), 'mystery_spending': mystery_spending(e)}


# -- bounded reference (synthetic values only) ----------------------------------------------------------
I32_MIN, I32_MAX = -2 ** 31, 2 ** 31 - 1
GAME_TYPES = (0, 1, 2)


def _wrap32(value: int) -> int:
    return (value + 2 ** 31) % 2 ** 32 - 2 ** 31


def _i32(name: str, value) -> int:
    if type(value) is not int or not I32_MIN <= value <= I32_MAX:
        raise ValueError(f'{name} must be a signed 32-bit int, got {value!r}')
    return value


def _bytes(name: str, values, count: int) -> list[int]:
    values = list(values)
    if len(values) != count or any(type(v) is not int or not 0 <= v <= 255 for v in values):
        raise ValueError(f'{name} must be {count} unsigned bytes, got {values!r}')
    return values


def _game_type(value) -> int:
    if value not in GAME_TYPES or type(value) is not int:
        raise ValueError(f'game_type must be one of {GAME_TYPES}, got {value!r}')
    return value


def earned_tickets(global_bytes, secret_bytes, themes) -> int:
    """Non-zero bytes: 4 global + 6 local per usable theme + 2 secret.

    ``themes`` is a sequence of ``(local_bytes, usable)``; ``usable`` is the caller's
    statement that the theme's ``global.sam`` loads (``0x12a50c``). The original loads
    it on demand; this function does not.
    """
    g = _bytes('global_bytes', global_bytes, 4)
    s = _bytes('secret_bytes', secret_bytes, 2)
    total = sum(1 for b in g if b) + sum(1 for b in s if b)
    for index, theme in enumerate(themes):
        local, usable = theme
        local = _bytes(f'themes[{index}] local bytes', local, 6)
        if type(usable) is not bool:
            raise ValueError(f'themes[{index}] usable must be bool, got {usable!r}')
        if usable:
            total += sum(1 for b in local if b)
    if total > I32_MAX:
        raise ValueError('earned ticket count exceeds signed 32-bit')
    return total


def mac_keys(extra_keys: int, earned: int) -> int:
    """mExtraKeys + trunc(earned / 3), signed 32-bit add with wrap (0x128c88)."""
    extra_keys = _i32('extra_keys', extra_keys)
    earned = _i32('earned', earned)
    if earned < 0:
        raise ValueError('earned is a count of non-zero bytes and cannot be negative')
    return _wrap32(extra_keys + earned // 3)


def mac_available_tickets(earned: int, spent: int) -> int:
    """earned - mSpentTickets, signed 32-bit subtract with wrap (0x128b40)."""
    return _wrap32(_i32('earned', earned) - _i32('spent', spent))


def mac_theme_door(game_type: int, entering: bool, has_target: bool, theme_usable: bool,
                   cost: int, keys: int) -> str:
    """Door +72 (0x964c4) in traced order; ``cost`` is ignored unless the theme is usable."""
    game_type = _game_type(game_type)
    if entering:
        return 'ignored: already entering'
    if not has_target:
        return 'ignored: no target theme'
    if game_type == 2:
        return 'enter'
    if not theme_usable:
        return 'refused: theme record unusable'
    if _i32('cost', cost) > _i32('keys', keys):
        return 'refused: cost above keys'
    return 'enter'


def mac_lobby_door_display(game_type: int, theme_usable: bool, cost: int, keys: int) -> dict:
    """Door group of 0x184028; returned values are control operands, not presentation."""
    game_type = _game_type(game_type)
    if not theme_usable:
        return {'touched': False}
    if game_type == 2:
        return {'touched': True, 'checked_bit': 0}
    cost, keys = _i32('cost', cost), _i32('keys', keys)
    if keys < cost:
        return {'touched': True, 'checked_bit': 1, 'state': 1, 'value': _wrap32(cost + 4), 'loop_sound': False}
    return {'touched': True, 'checked_bit': 1, 'state': 0, 'value': _wrap32(cost - 1), 'loop_sound': True}


def mac_count_panel(game_type: int, keys: int, available: int, cache: tuple[int, int]) -> dict:
    """Message 0x1e of 0x155b7c. ``cache`` = (keys, tickets) last shown; -1 after message 0x15."""
    game_type = _game_type(game_type)
    keys, available = _i32('keys', keys), _i32('available', available)
    cached_keys, cached_tickets = (_i32('cache', c) for c in cache)
    out = {'keys_group': None, 'tickets_group': None}
    if game_type != 2 and keys != cached_keys:
        cached_keys = keys
        out['keys_group'] = {'checked_bit': 0} if keys == 0 else {'checked_bit': 1, 'text_value': keys}
    if available != cached_tickets:
        cached_tickets = available
        out['tickets_group'] = {'checked_bit': 0} if available == 0 else {'checked_bit': 1, 'text_value': available}
    out['cache'] = (cached_keys, cached_tickets)
    return out


def mac_mystery_place(game_type: int, cost: int, item_id: int, owned: frozenset, earned: int,
                      spent: int) -> dict:
    """Placement 0xda874 and purchase 0xd3000. ``item_id`` is the 16-bit owned-set key.

    Order: cost > 0 and not owned -> purchase (GameType 1 -> free, unrecorded;
    unaffordable -> nothing; else record and spend); otherwise the money price.
    """
    game_type = _game_type(game_type)
    cost = _i32('cost', cost)
    if type(item_id) is not int or not 0 <= item_id <= 0xffff:
        raise ValueError(f'item_id must be an unsigned 16-bit key, got {item_id!r}')
    earned, spent = _i32('earned', earned), _i32('spent', spent)
    if cost <= 0 or item_id in owned:
        return {'path': 'money', 'owned': owned, 'spent': spent, 'purchase': None}
    if game_type == 1:
        return {'path': 'tickets', 'owned': owned, 'spent': spent, 'purchase': 1}
    if cost > mac_available_tickets(earned, spent):
        return {'path': 'tickets', 'owned': owned, 'spent': spent, 'purchase': 0}
    return {'path': 'tickets', 'owned': owned | {item_id}, 'spent': _wrap32(spent + cost), 'purchase': 1}


def mac_menu_affordable(cost: int, item_id: int, owned: frozenset, earned: int, spent: int) -> bool | None:
    """Build-menu gate at 0x163a04: None when the gate does not apply (cost 0 or already owned)."""
    cost = _i32('cost', cost)
    if type(item_id) is not int or not 0 <= item_id <= 0xffff:
        raise ValueError(f'item_id must be an unsigned 16-bit key, got {item_id!r}')
    if cost == 0 or item_id in owned:
        return None
    return cost <= mac_available_tickets(earned, spent)

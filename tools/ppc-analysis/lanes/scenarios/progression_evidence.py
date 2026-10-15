"""Second follow-up scenario witnesses for the identified Feral Mac executable.

Covers the new-player key award re-entry closure (player-window lifetime), the
map-cell ``mType`` binding and the cell classes used by ticket statistics, the
guest count/happiness statistics, the unreachable "own all land" secret ticket,
the "all researched and built" predicate, mystery-item placement charging and
the Instant Action UI gates (research, bank/loans, upgrades, completion text).
Same rules as scenario_evidence.py: bounded instruction-field checks at named
offsets, interpreted values only, nothing executed.
"""
from __future__ import annotations

import struct

from scenario_evidence import Evidence, pef
from followup_evidence import data_refs, exact_callers, toc_users

GAME_TYPE = ('data', 0x53d98)
PLAYER_WINDOW = 0x134db4
WINDOW_VTABLE = 0x4808c
SEND = 0x170f98
TEXT = 0x138504


def branch(e: Evidence, offset: int, target: int) -> None:
    """Unconditional relative branch without link."""
    w = e.word(offset)
    disp = w & 0x03fffffc
    if disp & 0x02000000:
        disp -= 0x04000000
    if w >> 26 != 18 or w & 3 or offset + disp != target:
        e.fail(offset, 'unconditional branch', (w >> 26, w & 3, hex(offset + disp)))
    e.checked += 1


def vtable_slot(e: Evidence, table: int, index: int, function: int) -> None:
    descriptor = e.slot_target(table + index)
    if descriptor[0] != 'data' or e.slot_target(descriptor[1]) != ('code', function):
        raise pef.PEFError(f'data:{table + index:#x} vtable entry {descriptor!r}')
    e.checked += 1


def no_base_stores(e: Evidence, start: int, end: int, base: int) -> None:
    """No D-form store (stw/stwu/stb/stbu/sth/sthu) uses the register as its base in [start, end)."""
    for offset in range(start, end, 4):
        w = e.word(offset)
        if w >> 26 in (36, 37, 38, 39, 44, 45) and w >> 16 & 31 == base:
            e.fail(offset, f'store through r{base}', hex(w))
    e.checked += 1


# -- new-player key award: the player window cannot outlive the first award -------
def player_window(e: Evidence) -> dict:
    # The award routine sends message 4 to the player window before it tests its argument
    # (0x15cdbc) or reads the new-player flag (0x15ce04).
    e.toc_load(0x15cd40, 26, ('data', PLAYER_WINDOW))
    e.d(0x15cd78, 32, 3, 26, 0)
    e.d(0x15cd7c, 14, 4, 0, 4)
    e.bl(0x15cd88, SEND)
    e.d(0x15cdbc, 11, 0, 24, 0)
    e.d(0x15ce04, 32, 0, 3, 0)
    # The sender drops messages to windows whose byte +0 (destroyed) is set.
    e.d(0x170fc8, 34, 0, 28, 0)
    e.bc(0x170fd0, 4, 2, 0x171014)
    e.d(0x171014, 14, 3, 0, -1)
    # InterfaceWindow constructor: vtable 0x4808c at +280, default procedure at +272/+276.
    e.toc_load(0x17dbbc, 25, ('data', 0x8790))
    if e.slot_target(0x8790) != ('code', 0x1813d0):
        raise pef.PEFError('default window procedure descriptor')
    e.toc_load(0x17dbe0, 0, ('data', WINDOW_VTABLE))
    e.d(0x17dbf0, 36, 0, 24, 280)
    e.d(0x17dc34, 36, 25, 24, 272)
    e.d(0x17dc38, 36, 25, 24, 276)
    rtti = e.slot_target(WINDOW_VTABLE)
    if rtti != ('data', 0x47f54) or e.slot_target(0x47f54) != ('code', 0x1d9d17):
        raise pef.PEFError('InterfaceWindow vtable RTTI')
    name = e.text(0x1d9d17, 'InterfaceWindow')
    # Default procedure: message 4 -> virtual +16 (deleting destructor, flag 1); message 5 -> send 4.
    e.rlwinm(0x181400, 4, 0, 0, 0, 0)
    e.bc(0x181408, 4, 2, 0x181a94)
    e.d(0x18140c, 11, 0, 26, 16)
    e.bc(0x181414, 4, 0, 0x181470)
    e.d(0x181418, 11, 0, 26, 6)
    e.bc(0x181420, 4, 0, 0x18144c)
    e.d(0x181424, 11, 0, 26, 3)
    e.bc(0x181428, 12, 2, 0x181568)
    e.bc(0x18142c, 4, 0, 0x181440)
    e.d(0x181440, 11, 0, 26, 5)
    e.bc(0x181444, 4, 0, 0x1819ac)
    branch(e, 0x181448, 0x181984)
    e.d(0x181990, 32, 12, 3, 280)
    e.d(0x181994, 14, 4, 0, 1)
    e.d(0x181998, 32, 12, 12, 16)
    e.d(0x1819b0, 14, 4, 0, 4)
    e.bl(0x1819bc, 0x170ec8)
    vtable_slot(e, WINDOW_VTABLE, 16, 0x17ddfc)
    # Destructor: a live window is destroyed (0x17e0b8): message 20 to itself, then byte +0 = 1.
    e.d(0x17de2c, 34, 0, 30, 0)
    e.bc(0x17de34, 4, 2, 0x17de40)
    e.bl(0x17de3c, 0x17e0b8)
    e.d(0x17e0cc, 14, 4, 0, 20)
    e.bl(0x17e0e0, SEND)
    e.d(0x17e0e4, 14, 0, 0, 1)
    e.d(0x17e0e8, 38, 0, 28, 0)
    # Player-window handler: message 20 clears the global pointer; message 5 is the award.
    e.d(0x15be38, 11, 0, 4, 20)
    e.bc(0x15be44, 12, 2, 0x15be68)
    e.d(0x15be4c, 11, 0, 4, 5)
    e.toc_load(0x15be68, 7, ('data', PLAYER_WINDOW))
    e.d(0x15be6c, 14, 0, 0, 0)
    e.d(0x15be70, 36, 0, 7, 0)
    # The only creator: front-end init (callers 0x8d228, 0x1979f4) with handler 0x15be34; slot
    # children get the slot-selection handler 0x15bf04.
    slots = data_refs(e, PLAYER_WINDOW)
    users = toc_users(e, slots[0]) if len(slots) == 1 else None
    if slots != [0x1cf0] or users != [0x15be68, 0x15c32c, 0x15c848, 0x15cd40, 0x15d240]:
        raise pef.PEFError(f'player-window pointer references {slots!r} {users!r}')
    e.checked += 1
    e.toc_load(0x15c838, 27, ('data', 0x7fe0))
    e.toc_load(0x15c87c, 5, ('data', 0x7ff0))
    if e.slot_target(0x7ff0) != ('code', 0x15be34) or e.slot_target(0x7fe0) != ('code', 0x15bf04):
        raise pef.PEFError('player-window handler descriptors')
    e.bl(0x15c880, 0x181aac)
    e.d(0x15c888, 36, 3, 29, 0)
    e.d(0x15c960, 14, 4, 27, 0)
    e.bl(0x15c964, 0x180264)
    init_callers = exact_callers(e, 0x15c828, [0x8d228, 0x1979f4])
    # The other users only read it: refresh (0x15c31c) and the award keep it in a callee-saved
    # register without storing through it; the creation dialog opener bails when it is null.
    no_base_stores(e, 0x15c32c, 0x15c808, 30)
    no_base_stores(e, 0x15cd40, 0x15cefc, 26)
    e.d(0x15d244, 32, 0, 3, 0)
    e.d(0x15d248, 10, 0, 0, 0)
    e.bc(0x15d24c, 4, 2, 0x15d260)
    exact_callers(e, 0x15d220, [0x15c090])
    # Creation loads the new slot (+96 = slot) before returning 1, so its award sees a loaded player.
    e.bl(0x15d034, 0x13781c)
    e.d(0x137844, 36, 5, 3, 96)
    e.d(0x15d048, 14, 3, 0, 1)
    e.bl(0x15d144, 0x15cf00)
    e.d(0x15d14c, 10, 0, 0, 1)
    return {'window_class': name,
            'award_closes_window_first': 'message 4 at 0x15cd88 precedes the argument test and flag read',
            'message_4': 'InterfaceWindow default procedure -> virtual +16 (deleting destructor)',
            'destroy': 'message 20 to the window, byte +0 = 1; later sends to it return -1',
            'message_20_handler': 'clears the player-window pointer',
            'pointer_writers': {'create': '0x15c888 (front-end init)', 'clear': '0x15be70 (message 20)'},
            'front_end_init_callers': init_callers,
            'conclusion': 'every award call deletes the player window; message 5 and slot selection need '
                          'that window, which only front-end init recreates after recomputing the flag'}


# -- map cell type and the cell classes used by ticket statistics ---------------------
def cell_types(e: Evidence) -> dict:
    e.toc_load(0xcd414, 31, ('code', 0x1ccd1a))
    e.text(0x1ccd1a, 'mDirection')
    field = e.text(0x1ccd1a + 80, 'mType')
    e.d(0xcd548, 14, 4, 29, 8)
    e.d(0xcd54c, 14, 5, 31, 80)
    e.bl(0xcd554, 0xcdf5c)
    predicates = {0x85190: (1,), 0x851ac: (3, 9), 0x851d0: (9,), 0x851ec: (10,)}
    for function, values in predicates.items():
        e.d(function, 32, 0, 3, 8)
        for index, value in enumerate(values):
            e.d(function + 4 + 8 * index, 11, 0, 0, value)
    e.d(0x85258, 32, 0, 3, 8)
    e.d(0x8525c, 11, 0, 0, 0)
    e.bc(0x85260, 4, 2, 0x85274)
    e.d(0x85274, 14, 3, 0, 0)
    # 0xe6c6c: cell (y*128 + x) of 68 bytes; true when any predicate holds, else false.
    e.rlwinm(0xe6c84, 0, 4, 7, 0, 24)
    e.d(0xe6ca8, 7, 0, 0, 68)
    for call, target in ((0xe6cb4, 0x85190), (0xe6cc4, 0x851ac), (0xe6cd4, 0x851d0),
                         (0xe6ce4, 0x851ec), (0xe6cf4, 0x85258)):
        e.bl(call, target)
    for test in (0xe6cbc, 0xe6ccc, 0xe6cdc, 0xe6cec):
        e.bc(test, 4, 2, 0xe6d00)
    e.bc(0xe6cfc, 12, 2, 0xe6d08)
    e.d(0xe6d00, 14, 3, 0, 1)
    e.d(0xe6d08, 14, 3, 0, 0)
    return {'runtime_field': f'map cell +8 = {field} (serializer 0xcd408 binds the name)',
            'guest_stat_cells': [0, 1, 3, 9, 10],
            'save_fixture_correlation': {'0': 'empty park land', '1': 'path', '2': 'water', '3': 'queue',
                                         '4': 'object footprint', '7': 'outside the park', '9': 'entrance',
                                         '10': 'exit', '30': 'approach road outside the gate'},
            'correlation_basis': 'jungle Easymode.TPWI cell grid (formats lane parser); not code-proven names'}


# -- guest statistics and the happiness ticket ------------------------------------------
def guest_stats(e: Evidence) -> dict:
    # 0xc3684 ignores its argument: 0x10a9a4 returns the global world.
    e.toc_load(0x10a9a4, 3, ('data', 0x11ef04))
    e.toc_load(0xc3694, 30, ('data', 0xecef4))
    e.d(0xc36f0, 7, 0, 0, 20)
    e.d(0xc36f8, 34, 0, 3, 2)
    e.d(0xc3700, 11, 0, 0, 1)
    e.bl(0xc3708, 0xe6c6c)
    e.d(0xc3714, 14, 28, 28, 1)
    # 0xc3524 = park open (world field -22768 == 0; the strike code reads the same field as closed).
    e.bl(0xc3534, 0x1091ac)
    e.d(0xc3538, 11, 0, 3, 0)
    e.d(0x1091b0, 32, 3, 3, -22768)
    # 0xc19e4: 0.0 while the park is closed, else mean of (u8)trunc(+412) over the same guests.
    e.bl(0xc1a0c, 0xc3524)
    e.bc(0xc1a14, 4, 2, 0xc1a20)
    e.d(0xc1a18, 48, 1, 2, -11288)
    if struct.unpack_from('>f', e.data, e.toc - 11288)[0] != 0.0:
        raise pef.PEFError('happiness statistic fallback')
    e.d(0xc1a88, 34, 0, 24, 2)
    e.d(0xc1a90, 11, 0, 0, 1)
    e.bl(0xc1a9c, 0xe6c6c)
    e.d(0xc1aa8, 48, 0, 24, 412)
    e.d(0xc1ab4, 14, 27, 27, 1)
    e.rlwinm(0xc1ad0, 0, 0, 0, 24, 31)
    # +412 is the guest happiness logged by the shop/sideshow interaction routine.
    e.toc_load(0xeab18, 30, ('code', 0x1d04c2))
    e.d(0xeb3d4, 48, 0, 28, 412)
    e.d(0xeb3dc, 14, 4, 30, 1616)
    log = e.text(0x1d04c2 + 1616, 'Sideshow won - happiness up %d points to %d')
    for site in (0xead8c, 0xeaf20, 0xeb444):
        e.d(site, 52, 0, 28, 412)
    # Happiness ticket: float mean > (float)(signed threshold 1880), then count > 1884.
    e.bl(0xd3304, 0xc19e4)
    e.d(0xd3308, 32, 3, 31, 1880)
    e.d(0xd3314, 27, 3, 3, -32768)
    e.x(0xd3328, 63, 32, 0, 1, 0)
    e.bc(0xd332c, 4, 1, 0xd3350)
    e.bl(0xd3334, 0xc3684)
    e.d(0xd3338, 32, 0, 31, 1884)
    e.x(0xd333c, 31, 0, 0, 3, 0)
    # Thing class byte +2 for staff (wage type index): 5 handyman, 4 mechanic, 6 entertainer,
    # 7 guard, 8 researcher.
    e.d(0xf46d8, 34, 0, 3, 2)
    e.d(0xf46dc, 11, 0, 0, 6)
    e.d(0xf46e8, 11, 0, 0, 4)
    e.d(0xf46f8, 11, 0, 0, 8)
    for site, index in ((0xf4708, 0), (0xf4710, 1), (0xf4718, 2), (0xf4720, 3), (0xf4728, 4)):
        e.d(site, 14, 3, 0, index)
    e.x(0xf4760, 31, 235, 3, 4, 0)
    return {'guests_in_park': '0xc3684: things with class byte 1 on cells of type 0/1/3/9/10 (global)',
            'park_open': '0xc3524: world field -22768 == 0',
            'mean_happiness': '0xc19e4: 0.0 while the park is closed, else mean of (u8)trunc(happiness +412) '
                              'over the same set',
            'happiness_field_log': log,
            'happiness_ticket': 'mean > (float)HappinessThreshold[1880] and the same guest count > [1884]',
            'staff_class_bytes': {'handyman': 5, 'mechanic': 4, 'entertainer': 6, 'guard': 7, 'researcher': 8},
            'wage_multiply': 'mullw (low 32 bits)'}


# -- secret tickets, all researched and built -------------------------------------------
def secrets_and_research(e: Evidence) -> dict:
    exact_callers(e, 0xd381c, [0xd3640])
    e.bl(0xd362c, 0xc8b08)
    e.d(0xd3630, 11, 0, 3, 100)
    e.d(0xd363c, 14, 4, 0, 0)
    exact_callers(e, 0x128efc, [0xd3878])
    e.d(0x128f0c, 35, 0, 4, 38)
    e.d(0x128f24, 38, 0, 4, 0)
    stores = [o for o in range(0x128000, 0x12a400, 4)
              if e.word(o) >> 26 in (38, 39) and e.word(o) & 0xffff in (38, 39)]
    if stores != [0x1287f4, 0x1287f8]:
        raise pef.PEFError(f'secret ticket byte stores {[hex(s) for s in stores]}')
    e.checked += 1
    # 0xc5510: no unresearched lab item (category 5 = all), then every catalog item with kind
    # (+1960) != 4 and ticket cost (+196) == 0 needs park record +24 != 0.
    e.d(0xc5538, 14, 4, 0, 5)
    e.bl(0xc553c, 0xf151c)
    e.bc(0xc5544, 4, 1, 0xc5550)
    e.d(0xc5548, 14, 3, 0, 0)
    e.d(0xc5560, 32, 0, 30, 1960)
    e.d(0xc5564, 11, 0, 0, 4)
    e.bc(0xc5568, 12, 2, 0xc55a8)
    e.d(0xc5588, 32, 0, 3, 24)
    e.bc(0xc5590, 4, 2, 0xc55a8)
    e.d(0xc5594, 32, 0, 30, 196)
    e.bc(0xc559c, 4, 2, 0xc55a8)
    e.d(0xc55a0, 14, 3, 0, 0)
    e.d(0xc55bc, 14, 3, 0, 1)
    e.d(0xf1540, 32, 5, 3, 5212)
    e.d(0xf1558, 11, 0, 28, 5)
    e.bl(0xf1570, 0xf2494)
    e.bc(0xf1578, 4, 2, 0xf1580)
    e.d(0xf157c, 14, 30, 30, 1)
    # Researched = progress (+16, accumulated at 0xf2370) >= cost (+12).
    e.d(0xf2494, 48, 1, 3, 16)
    e.d(0xf2498, 48, 0, 3, 12)
    e.x(0xf249c, 63, 32, 0, 1, 0)
    e.d(0xf2364, 48, 0, 28, 16)
    e.d(0xf2370, 52, 0, 28, 16)
    return {'secret_award_routine': '0xd381c, sole caller 0xd3640 with index 0 (camera coverage == 100)',
            'secret_1_own_all_land': 'no award path: the only setter 0x128efc is reached with index 0; '
                                     'byte +39 is only reset, deserialized and counted',
            'all_researched_and_built': 'unresearched(lab, all) == 0 and every item with kind != 4 and '
                                        'ticket cost 0 has park record +24 != 0',
            'researched_item': 'progress +16 >= cost +12 (float)'}


# -- mystery items -------------------------------------------------------------------
def mystery_items(e: Evidence) -> dict:
    e.d(0xdacc8, 32, 0, 28, 196)
    e.d(0xdaccc, 11, 0, 0, 0)
    e.bc(0xdacd0, 4, 1, 0xdad00)
    e.bl(0xdace0, 0xd30d0)
    e.bc(0xdace8, 4, 2, 0xdad00)
    e.bl(0xdacf8, 0xd3000)
    branch(e, 0xdacfc, 0xdad10)
    e.d(0xdad08, 32, 4, 28, 440)
    e.bl(0xdad0c, 0xcbfdc)
    e.bl(0xd3120, 0x128d14)
    e.d(0x128d30, 14, 4, 31, 40)
    e.d(0x128d40, 14, 0, 31, 44)
    # Purchase: GameType 1 succeeds without recording; else cost <= available, then insert.
    e.toc_load(0xd3010, 30, GAME_TYPE)
    e.d(0xd3054, 11, 0, 0, 1)
    e.bc(0xd3058, 12, 2, 0xd30b8)
    e.d(0xd30b8, 14, 3, 0, 1)
    e.bl(0xd3064, 0xd313c)
    e.bc(0xd306c, 4, 2, 0xd3078)
    e.d(0xd3070, 14, 3, 0, 0)
    e.bl(0xd30b0, 0x128ca8)
    e.d(0xd316c, 32, 30, 3, 196)
    e.bl(0xd319c, 0x128a2c)
    e.x(0xd31a0, 31, 0, 0, 30, 3)
    e.bc(0xd31a4, 12, 1, 0xd31b0)
    e.d(0x128ccc, 14, 4, 30, 40)
    e.bc(0x128ce0, 4, 2, 0x128cec)
    e.d(0x128cec, 32, 0, 30, 28)
    e.d(0x128cf8, 36, 0, 30, 28)
    placement = exact_callers(e, 0xd3000, [0xdacf8])
    return {'placement': '0xda874: ticket cost > 0 and not yet in the player set -> ticket purchase, '
                         'no money; otherwise the money price (catalog +440) is debited',
            'purchase_result_ignored_by_placement': True, 'purchase_callers': placement,
            'online': 'purchase returns success without recording the item, so placement is free',
            'affordability': 'signed cost <= earned - spent'}


# -- Instant Action UI gates -----------------------------------------------------------
def instant_action_ui(e: Evidence) -> dict:
    e.import_call(0x138528, 'GetConstString__8TbITableCFUl')
    # Research panel: GameType 2 shows UITEXT 468 and returns; otherwise 467 when no researcher.
    e.toc_load(0x161920, 31, GAME_TYPE)
    e.d(0x161980, 11, 0, 0, 2)
    e.bc(0x161984, 4, 2, 0x1619dc)
    e.d(0x161988, 14, 3, 0, 468)
    e.bl(0x16198c, TEXT)
    branch(e, 0x1619d8, 0x162244)
    e.d(0x1619f8, 14, 3, 0, 467)
    research = exact_callers(e, 0x161910, [0x145310, 0x155480, 0x18f9b0])
    # Bank panel ("Available Loans", UITEXT 170) is not opened for GameType 2.
    e.toc_load(0x154ab0, 27, GAME_TYPE)
    e.d(0x154af0, 11, 0, 0, 2)
    e.bc(0x154af4, 4, 2, 0x154afc)
    branch(e, 0x154af8, 0x1551ec)
    e.d(0x154b34, 14, 3, 0, 170)
    # Loan buttons disabled (virtual +28 with 0 sets the disabled bit 0x2 of +68) on three panels.
    vtable_slot(e, WINDOW_VTABLE, 28, 0x17fb64)
    e.d(0x17fb9c, 32, 5, 3, 68)
    e.d(0x17fba8, 24, 5, 0, 2)
    e.d(0x17fbb0, 36, 0, 3, 68)
    buttons = {}
    for cmp, call, ids in ((0x14eeec, 0x14ef00, (0x14eef4, 5, 0x14eefc, -3156)),
                           (0x15161c, 0x151630, (0x151624, 1, 0x15162c, 8831)),
                           (0x1698e8, 0x1698f8, None)):
        e.d(cmp, 11, 0, 0, 2)
        e.bl(call, 0x17f770)
        e.d(call + 4, 32, 12, 3, 280)
        e.d(call + 8, 14, 4, 0, 0)
        e.d(call + 12, 32, 12, 12, 28)
        if ids:
            e.d(ids[0], 15, 4, 0, ids[1])
            e.d(ids[2], 14, 4, 4, ids[3])
            buttons[hex(cmp)] = (ids[1] << 16) + ids[3]
        else:
            e.d(0x1698f4, 14, 4, 0, 733)
            buttons[hex(cmp)] = 733
    # Ride upgrades: GameType 2 skips the list and shows UITEXT 27.
    e.d(0x165a90, 11, 0, 0, 2)
    e.bc(0x165a94, 12, 2, 0x165c70)
    e.d(0x165c98, 11, 0, 0, 2)
    e.bc(0x165c9c, 4, 2, 0x165cac)
    e.d(0x165ca0, 14, 3, 0, 27)
    e.bl(0x165ca4, TEXT)
    # Instant Action completion text (UITEXT 471) in the pointer-dispatched handler 0x13d708.
    e.d(0x13d82c, 11, 0, 0, 2)
    e.d(0x13d838, 14, 4, 0, 471)
    e.import_call(0x13d83c, 'GetConstString__8TbITableCFUl')
    return {'research_panel': {'instant_action_text_mac': 468, 'no_researcher_text_mac': 467,
                               'callers': research},
            'bank_panel': 'not opened for GameType 2 (UITEXT 170 "Available Loans" panel)',
            'loan_button_control_ids': buttons,
            'loan_button_binding': 'help texts 196/219/174 "take out or repay loans" via the UI lane layout decoder',
            'upgrades': 'GameType 2: no upgrade list, UITEXT 27',
            'completion_text_mac': 471,
            'text_index_note': 'Mac UITEXT = Windows + 1 from 207 (468/467/471 -> Windows 467/466/470)'}


def inspect_progression(e: Evidence) -> dict:
    return {'player_window': player_window(e), 'cell_types': cell_types(e), 'guest_stats': guest_stats(e),
            'secrets_and_research': secrets_and_research(e), 'mystery_items': mystery_items(e),
            'instant_action_ui': instant_action_ui(e)}

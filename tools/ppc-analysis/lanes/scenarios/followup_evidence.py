"""Follow-up scenario witnesses for the identified Feral Mac executable.

Covers the new-player key award lifetime, GameType versus front-end/main-loop
state transitions, advisor rule/response tables, staff wages/rest/dismissal/
training, the in-the-red bankruptcy path and the complete CMainBalance
(Standard.sam) field binding. Same rules as scenario_evidence.py: bounded
instruction-field checks at named offsets, interpreted values only, no
disassembly or binary contents in the output, nothing executed.
"""
from __future__ import annotations

import struct

from scenario_evidence import Evidence, magic, pef

BALANCE = ('data', 0x54860)
MAIN_TABLE = 0x34d10
RECORD = 60
RESPONSES = 0x18ff4
RULES = 0x1f2b4


def toc_users(e: Evidence, slot: int) -> list[int]:
    """Code offsets whose D-form instruction addresses the given TOC slot through r2."""
    disp = (slot - e.toc) & 0xffff
    return [o for o in range(0, len(e.code), 4)
            if e.word(o) >> 26 in (32, 34, 36, 38, 40, 44, 48, 50, 52, 14)
            and e.word(o) >> 16 & 31 == 2 and e.word(o) & 0xffff == disp]


def data_refs(e: Evidence, target: int) -> list[int]:
    index = e.c.data_section.index
    return sorted(o for o, r in e.relocs.items() if r.kind == 'section' and r.target == index and r.addend == target)


def exact_callers(e: Evidence, target: int, expected: list[int]) -> list[str]:
    callers = e.calls_to(target)
    if callers != expected:
        raise pef.PEFError(f'code:{target:#x} callers {[hex(c) for c in callers]}')
    e.checked += 1
    return [hex(c) for c in callers]


def double(e: Evidence, toc_disp: int) -> float:
    return struct.unpack_from('>d', e.data, e.toc + toc_disp)[0]


def single(e: Evidence, toc_disp: int) -> float:
    return struct.unpack_from('>f', e.data, e.toc + toc_disp)[0]


# -- balance schema ----------------------------------------------------------
def schema_records(data: bytes, base: int) -> list[tuple[int, str, int]]:
    """(type, name, count) per 60-byte record up to the type-12 TABLE_END."""
    out = []
    offset = base
    while True:
        if offset + RECORD > len(data):
            raise pef.PEFError(f'data:{base:#x} schema has no TABLE_END')
        kind = struct.unpack_from('>I', data, offset)[0]
        raw = data[offset + 4:offset + 52]
        if b'\0' not in raw:
            raise pef.PEFError(f'data:{offset:#x} unterminated schema name')
        name = raw[:raw.index(b'\0')].decode('ascii')
        count = struct.unpack_from('>I', data, offset + 52)[0]
        if kind == 12:
            return out
        if kind > 12:
            raise pef.PEFError(f'data:{offset:#x} schema record type {kind}')
        out.append((kind, name, count))
        offset += RECORD


def balance_layout(records: list[tuple[int, str, int]], value_base: int = 8) -> dict[str, int]:
    """Mirror of the parser at code 0x16f4c: word counter starts at 1, types 4..11 take one
    word, a type-2 opener starts a flat struct array closed by type 3 (count at record +52),
    which advances by (count - 1) * fields + 1 words. Type 1 names the scalar group before it."""
    words = 1
    fields = count = 0
    array_fields: list[tuple[str, int]] = []
    pending: list[tuple[str, int]] = []
    layout: dict[str, int] = {}
    for index, (kind, name, record_count) in enumerate(records):
        if kind == 3:
            for field, word in array_fields:
                for element in range(count):
                    layout[f'{name}[{element}].{field}'] = value_base + 4 * (word + element * fields)
            words += (count - 1) * fields + 1
            fields = count = 0
            array_fields = []
        elif kind == 2:
            end = index + 1
            while end < len(records) and records[end][0] > 2 and records[end][0] != 3:
                end += 1
            if end >= len(records) or records[end][0] != 3:
                raise pef.PEFError(f'schema array opened at record {index} has no closing record')
            fields, count = end - index - 1, records[end][2]
        elif 4 <= kind <= 11:
            (array_fields if fields else pending).append((name, words))
            words += 1
        elif kind == 1:
            for field, word in pending:
                layout[f'{name}.{field}'] = value_base + 4 * word
            pending = []
    for field, word in pending:
        layout[field] = value_base + 4 * word
    return layout


def main_balance(e: Evidence) -> dict:
    # Static initializer: CMainBalance vtable at object+4, then the parser runs on data 0x54860.
    e.toc_load(0x19904, 31, BALANCE)
    e.toc_load(0x19914, 0, ('data', 0x38fbc))
    e.d(0x19920, 36, 0, 31, 4)
    e.bl(0x19924, 0x16f4c)
    for slot, expected in ((0x38fbc, ('data', 0x38fb4)), (0x38fb4, ('code', 0x1c8aa1)),
                           (0x38fc4, ('data', 0x62c0)), (0x38fc8, ('data', 0x62c8)),
                           (0x62c0, ('code', 0x19950)), (0x62c8, ('code', 0x19960))):
        if e.slot_target(slot) != expected:
            raise pef.PEFError(f'data:{slot:#x} CMainBalance vtable {e.slot_target(slot)!r}')
    rtti = e.text(0x1c8aa1, 'CMainBalance')
    # Record accessor (vtable +8): table + 60*i; value base (vtable +12): this + 8.
    e.d(0x19950, 7, 0, 4, RECORD)
    e.toc_load(0x19954, 3, ('data', MAIN_TABLE))
    e.d(0x19960, 14, 3, 3, 8)
    # Parser: counter starts at 1; arrays advance (count-1)*fields+1; scalars one word.
    e.d(0x16f90, 14, 27, 0, 1)
    e.d(0x16fc4, 11, 0, 0, 3)
    e.d(0x16fe8, 11, 0, 0, 12)
    e.d(0x16ff0, 11, 0, 0, 10)
    e.d(0x17084, 10, 0, 0, 2)
    e.d(0x170d0, 32, 25, 3, 52)
    e.d(0x17128, 14, 0, 25, -1)
    e.x(0x17138, 31, 235, 6, 0, 26)
    e.x(0x17154, 31, 266, 27, 6, 27)
    e.d(0x1715c, 14, 27, 27, 1)
    e.rlwinm(0x1717c, 27, 0, 2, 0, 29)
    e.d(0x17194, 14, 27, 27, 1)
    e.d(0x171d8, 14, 27, 27, 1)
    e.d(0x197e0, 11, 0, 0, 12)
    records = schema_records(e.data, MAIN_TABLE)
    layout = balance_layout(records)
    # Independent anchors: every offset below is a displacement read by code from data 0x54860.
    anchors = {'PerGradeStaffConsts[0].BaseWage': (0xf475c, 748),
               'PerGradeStaffConsts[0].RecuperationRate': (0xf3db8, 756),
               'PerGradeStaffConsts[0].HappinessRecuperationRate': (0xf3dfc, 760),
               'PerTypeStaffConsts[0].PayMultiplier': (0xf4754, 832),
               'EntertainerConstsPerGrade[0].PoundsPerTrainingPoint': (0xf362c, 868),
               'HandymanConstsPerGrade[0].PoundsPerTrainingPoint': (0xf3640, 948),
               'MechanicConstsPerGrade[0].PoundsPerTrainingPoint': (0xf3668, 1008),
               'ResearcherConstsPerGrade[0].ResearchAbility': (0xf0750, 1052),
               'ResearcherConstsPerGrade[0].PoundsPerTrainingPoint': (0xf367c, 1056),
               'GuardConstsPerGrade[0].PoundsPerTrainingPoint': (0xf3654, 1116),
               'ResearchTech[1].PercentageForThisTech': (0xf1678, 1280),
               'Research.StartingWorkLoad': (0xf088c, 1344),
               'BankAccountInfo.InitialCash': (0xcc1d4, 408),
               'GoldenTicketLocal.Visitors': (0xd3244, 1872),
               'GoldenTicketLocal.ProfitYear': (0xd33f8, 1888),
               'GoldenTicketGlobal.MinCellsCovered': (0xd357c, 1916),
               'Challenges.DaysUntilFirstChallenge': (0xcffa4, 1928)}
    for name, (site, offset) in anchors.items():
        if layout.get(name) != offset:
            raise pef.PEFError(f'balance layout {name}: computed {layout.get(name)!r}, code reads {offset}')
        if e.word(site) & 0xffff != offset:
            raise pef.PEFError(f'code:{site:#x} displacement for {name}')
        e.checked += 1
    return {'class': rtti, 'object': hex(BALANCE[1]), 'schema_table': hex(MAIN_TABLE),
            'records': len(records), 'bound_fields': len(layout),
            'value_rule': 'object + 8 + 4 * (1 + words before); arrays add one word after their elements',
            'anchors_verified': {k: v[1] for k, v in anchors.items()},
            'derived': {'GoldenTicketGlobal.MinCellsOwned': layout['GoldenTicketGlobal.MinCellsOwned'],
                        'ResearchTech[0].PercentageForThisTech': layout['ResearchTech[0].PercentageForThisTech'],
                        'ResearchTech_count': sum(1 for k in layout if k.startswith('ResearchTech['))}}


# -- new-player key award lifetime ------------------------------------------------
def new_player_flag(e: Evidence) -> dict:
    flag = 0x134db0
    slots = data_refs(e, flag)
    users = toc_users(e, slots[0]) if len(slots) == 1 else None
    if slots != [0x1cc4] or users != [0x15c830, 0x15ce00, 0x15d154]:
        raise pef.PEFError(f'new-player flag references {slots!r} {users!r}')
    e.checked += 1
    # Only writers: front-end init (1 if no named slot, else 0) and creation (1). The lobby
    # routine only reads it (0x15ce04) and never clears it.
    e.d(0x15ce04, 32, 0, 3, 0)
    # Lobby routine argument != 0 returns before the award; it also needs a loaded player (+96 != -1).
    e.x(0x15cd44, 31, 444, 3, 24, 3)
    e.d(0x15cdbc, 11, 0, 24, 0)
    e.bc(0x15cdc0, 4, 2, 0x15ceec)
    e.d(0x15cdf4, 32, 0, 3, 96)
    e.d(0x15cdf8, 11, 0, 0, -1)
    e.bc(0x15cdfc, 12, 2, 0x15cecc)
    callers = exact_callers(e, 0x15cd38, [0x15be5c, 0x15c068, 0x15c1c8, 0x15d178])
    for at, arg in ((0x15be58, 0), (0x15c064, 0), (0x15c1c4, 1), (0x15d174, 0)):
        e.d(at, 14, 3, 0, arg)
    e.d(0x15be4c, 11, 0, 4, 5)
    e.bl(0x15c060, 0x13781c)
    handlers = {hex(f): [hex(p) for p in e.pointer_refs(f)] for f in (0x15be34, 0x15bf04, 0x15c1ac)}
    if handlers != {'0x15be34': ['0x7ff0'], '0x15bf04': ['0x7fe0'], '0x15c1ac': ['0x7fd8']}:
        raise pef.PEFError(f'front-end handler registrations {handlers!r}')
    # Front-end init re-evaluates the flag; it runs from the front-end entry only with no loaded
    # player, and from the return path after the player is unloaded (+96 = -1).
    init_callers = exact_callers(e, 0x15c828, [0x8d228, 0x1979f4])
    e.d(0x8d21c, 32, 0, 3, 96)
    e.d(0x8d220, 11, 0, 0, -1)
    e.bc(0x8d224, 4, 2, 0x8d22c)
    e.bl(0x1979f0, 0x137a88)
    e.d(0x137ba8, 14, 0, 0, -1)
    e.d(0x137bb0, 36, 0, 29, 96)
    e.d(0x137844, 36, 5, 3, 96)
    return {'flag': hex(flag), 'toc_users': [hex(u) for u in users],
            'writers': {'0x15ccf4': 1, '0x15cd14': 0, '0x15d164': 1},
            'award_routine_callers': callers, 'award_requires': 'argument 0, loaded player (+96 != -1), flag != 0',
            'front_end_handlers': handlers, 'front_end_init_callers': init_callers,
            'reinit_path': '0x1979a8: unload player (+96 = -1) then front-end init (flag = 0 when any player exists)',
            'residual': 'a second argument-0 call between creation and leaving the front end '
                        '(message 5 or slot selection while the flag is still 1) is not excluded statically'}


# -- GameType versus app state ------------------------------------------------------
def game_type_transitions(e: Evidence) -> dict:
    set_callers = exact_callers(e, 0x12bbf4, [0x971fc, 0x12bb98, 0x12bbb0, 0x12bbc8, 0x12bbd8,
                                              0x1379bc, 0x1379e8, 0x18b13c, 0x1c2a9c, 0x1c2ac8])
    e.text(0x1d8c50, 'Invalid GameType in SetGameType')
    e.d(0x12bc08, 14, 4, 0, 3)
    # Online entry: front-end exit code 2 together with GameType 1.
    e.toc_load(0x971b4, 3, ('data', 0x84b80))
    for at, value in ((0x971c4, 2), (0x18b10c, 2)):
        e.d(at, 14, 0, 0, value)
    e.d(0x971d4, 36, 0, 3, 20)
    e.d(0x18b114, 36, 0, 3, 20)
    e.d(0x971f4, 14, 4, 0, 1)
    e.d(0x18b134, 14, 4, 0, 1)
    e.d(0x18b0ec, 11, 0, 3, 0)
    # Front-end exit code (*0x84b80 + 20): 1 running, 2 enter park, 3 quit.
    for set_at, store_at, value in ((0x8cfd0, 0x8cfd8, 1), (0x966c0, 0x966c8, 2), (0x8d6a4, 0x8d6b0, 3)):
        e.d(set_at, 14, 0, 0, value)
        e.d(store_at, 36, 0, 3, 20)
    e.d(0x8d4e8, 32, 3, 30, 20)
    # Main loop state (data 0x15c488, 16-way jump table at data 0x52cc8).
    e.toc_load(0x1c1248, 21, ('data', 0x15c488))
    e.toc_load(0x1c123c, 19, ('data', 0x84b80))
    e.d(0x1c135c, 10, 0, 0, 15)
    e.toc_load(0x1c1364, 3, ('data', 0x52cc8))
    cases = {2: 0x1c16e4, 3: 0x1c17c8, 11: 0x1c2850, 12: 0x1c2ad0}
    for case, target in cases.items():
        if e.slot_target(0x52cc8 + 4 * case) != ('code', target):
            raise pef.PEFError(f'main-loop case {case}')
    e.d(0x1c16a0, 14, 0, 0, 2)
    e.d(0x1c16e8, 32, 0, 3, 20)
    e.d(0x1c16ec, 11, 0, 0, 1)
    e.d(0x1c17bc, 14, 0, 0, 3)
    e.bl(0x1c17cc, 0x8d3b0)
    e.d(0x1c1910, 11, 0, 17, 3)
    e.d(0x1c191c, 11, 0, 17, 2)
    e.d(0x1c1928, 14, 0, 0, 9)
    e.d(0x1c1934, 14, 0, 0, 12)
    # Case 11 (park left): while GameType 1 and session field +1008 == 0, restore from mEasyModeUser.
    e.d(0x1c29d0, 11, 0, 0, 1)
    e.bc(0x1c29d4, 4, 2, 0x1c2c84)
    e.d(0x1c2a24, 32, 0, 3, 1008)
    e.bl(0x1c2a6c, 0x128f4c)
    e.d(0x1c2a94, 14, 4, 0, 2)
    e.d(0x1c2ac0, 14, 4, 0, 0)
    e.d(0x128f4c, 34, 3, 3, 36)
    return {'game_type': 'data 0x53d98: 0 Full Simulation, 1 online, 2 Instant Action; not serialized '
                         '(only mEasyModeUser is)',
            'set_game_type_callers': set_callers,
            'online_entry': ['0x971ac', '0x18b0e4 (argument 0)'],
            'front_end_exit_code': {'1': 'front end running', '2': 'enter park (main state 9)',
                                    '3': 'quit (main state 12)'},
            'main_loop_state': 'data 0x15c488 (0..15); state 2 runs the front end, 3 leaves it',
            'online_exit_restore': 'main state 11: GameType 1 and +1008 == 0 -> GameType 2 if mEasyModeUser else 0'}


# -- advisor rules and responses -----------------------------------------------------
def advisor_tables(e: Evidence) -> dict:
    e.toc_load(0x6b94, 17, ('data', RESPONSES))
    e.toc_load(0x6b98, 30, ('code', 0x1c814b))
    e.d(0x6c18, 11, 0, 0, 9999)
    e.d(0x6c0c, 14, 4, 4, 32)
    e.d(0x6c50, 32, 19, 5, 8)
    e.d(0x6d04, 14, 4, 30, 55)
    e.text(0x1c814b + 28, 'Advisor says ResponseID %d')
    e.text(0x1c814b + 55, ':Speech:lips:sp_%03d.lip')
    e.toc_load(0x7274, 4, ('data', RESPONSES))
    e.d(0x72bc, 32, 3, 3, 28)
    # Rule -> first response (+32) and variant count (+36), 48-byte records; rules 351/352 excluded.
    e.toc_load(0xd46c, 3, ('data', RULES))
    e.d(0xd474, 14, 0, 4, -351)
    e.d(0xd48c, 7, 7, 4, 48)
    e.d(0xd5d8, 32, 3, 5, 32)
    e.toc_load(0xd5f0, 3, ('data', RULES))
    e.d(0xd75c, 32, 3, 5, 36)
    # Message build: response = first(rule) + variant, then CMsgTag(response +28 tag id).
    e.bl(0xb944, 0xd468)
    e.x(0xb948, 31, 266, 24, 24, 3)
    e.bl(0xb958, 0x6b7c)
    e.bl(0xb964, 0x7274)
    e.bl(0xb97c, 0x1165b4)
    e.toc_load(0x1165c0, 0, ('data', 0x1e194))
    for slot, expected in ((0x1e194, ('data', 0x1e18c)), (0x1e18c, ('code', 0x1c81e0))):
        if e.slot_target(slot) != expected:
            raise pef.PEFError(f'data:{slot:#x} CMsgTag RTTI')
    e.text(0x1c81e0, 'CMsgTag')
    # Lobby queue plays response ids directly.
    e.bl(0x8dc38, 0x6b7c)
    e.bl(0x15ccd8, 0x8dda0)
    # Award rules by award code and ticket index.
    rule_sites = {215: 0xd3980, 216: 0xd3a14, 217: 0xd3aa8, 220: 0xd3c1c, 221: 0xd3cb0, 222: 0xd3d44,
                  234: 0xd3e08, 235: 0xd3e9c, 236: 0xd3f30, 237: 0xd3fc4, 167: 0xf19a8}
    for rule, at in rule_sites.items():
        e.d(at, 14, 4, 0, rule)
    for at in (0xd3994, 0xd3a28, 0xd3abc, 0xd3c30, 0xd3cc4, 0xd3d58, 0xd3e20, 0xd3eb4, 0xd3f48, 0xd3fdc, 0xf19c4):
        e.bl(at, 0xb6d8)
    e.bl(0xd36dc, 0xd3900)
    e.bl(0xd37bc, 0xd3b98)
    e.bl(0xd36c0, 0x128d9c)
    e.bl(0xd37a0, 0x128de4)
    responses = {}
    offset = RESPONSES
    while True:
        rid, _, sample, _, _, _, _, tag = struct.unpack_from('>8i', e.data, offset)
        if rid == 9999:
            break
        responses[rid] = (sample, tag)
        offset += 32
    rules = {}
    for rule in range(351):
        record = struct.unpack_from('>12i', e.data, RULES + 48 * rule)
        if record[1] != rule:
            raise pef.PEFError(f'advisor rule record {rule}')
        rules[rule] = (record[8], record[9])
    if len(responses) != 610 or struct.unpack_from('>i', e.data, RULES + 48 * 351 + 4)[0] == 351:
        raise pef.PEFError('advisor table sizes')
    e.checked += 2

    def rule_view(rule):
        first, n = rules[rule]
        return {'responses': [first, first + n - 1],
                'samples': sorted({responses[r][0] for r in range(first, first + n)}),
                'tag_ids': [responses[r][1] for r in range(first, first + n)]}
    named = {'167': 'research complete (Instant Action: current theme; offline: all themes)',
             '215': 'local ticket won (award 1), variant = local ticket 0..5',
             '216': 'local ticket and key (award 2)', '217': 'local ticket, key and new theme (award 3)',
             '220': 'global ticket won (award 1), variant = global ticket 0..3',
             '221': 'global ticket and key (award 2)', '222': 'global ticket, key and new theme (award 3)',
             '234': 'global ticket 0 moved here (award 4)', '235': 'global ticket 1 moved here',
             '236': 'global ticket 2 moved here', '237': 'global ticket 3 moved here'}
    lobby = {str(r): {'sample': responses[r][0], 'tag_id': responses[r][1]} for r in (390, 391, 393, 394, 398)}
    return {'response_table': {'data': hex(RESPONSES), 'records': len(responses), 'record_bytes': 32,
                               'fields': '+0 response id, +8 speech sample sp_NNN, +28 CMsgTag id'},
            'rule_table': {'data': hex(RULES), 'rules': 351, 'record_bytes': 48,
                           'fields': '+4 rule id, +32 first response, +36 variants'},
            'rules': {k: dict(rule_view(int(k)), meaning=v) for k, v in named.items()},
            'lobby_response_ids': lobby,
            'lobby_meaning': {'390/391': 'front end, no named player', '398': 'front end, players exist',
                              '393': 'first lobby entry, Full Simulation (extra key)',
                              '394': 'first lobby entry, Instant Action'},
            'unresolved': 'CMsgTag id -> TAG_SYSTEM text mapping (ids are not TAG_SYSTEM indices; 383 occurs '
                          'for responses without a tag) and the spoken content of samples'}


# -- staff economy ---------------------------------------------------------------------
def staff_economy(e: Evidence) -> dict:
    # Wage = PerTypeStaffConsts[type].PayMultiplier * PerGradeStaffConsts[grade].BaseWage.
    e.toc_load(0xf46c4, 31, BALANCE)
    for cmp_at, kind in ((0xf46dc, 6), (0xf46e8, 4), (0xf46f8, 8)):
        e.d(cmp_at, 11, 0, 0, kind)
    for at, index in ((0xf4708, 0), (0xf4710, 1), (0xf4718, 2), (0xf4720, 3), (0xf4728, 4), (0xf4740, 5)):
        e.d(at, 14, 3, 0, index)
    e.d(0xf4744, 32, 0, 30, 484)
    e.d(0xf4754, 32, 4, 3, 832)
    e.d(0xf475c, 32, 0, 3, 748)
    e.x(0xf4760, 31, 235, 3, 4, 0)
    # Debit (0xcbfdc): money -= amount; first crossing below zero records the world tick (+288).
    e.d(0xcc004, 32, 0, 30, 12)
    e.x(0xcc008, 31, 40, 0, 31, 0)
    e.d(0xcc01c, 32, 0, 30, 284)
    e.d(0xcc034, 32, 0, 3, -22772)
    e.d(0xcc038, 36, 0, 30, 288)
    e.d(0xcc060, 32, 0, 30, 292)
    e.x(0xcc064, 31, 40, 0, 31, 0)
    debit_callers = e.calls_to(0xcbfdc)
    if len(debit_callers) != 16:
        raise pef.PEFError(f'debit callers {len(debit_callers)}')
    e.checked += 1
    # Month end (message 12): each staff member is paid one wage, posted to the wage statistic.
    e.d(0xf2b8c, 11, 0, 3, 11)
    e.d(0xf2ba4, 11, 0, 3, 13)
    e.bl(0xf2bb0, 0xf3364)
    e.bl(0xf338c, 0xf46bc)
    e.bl(0xf33a0, 0xcbfdc)
    e.d(0xf33c0, 32, 0, 3, -2064)
    # Dismissal: optional DISMISS EMPLOYEE confirmation (Mac UITEXT 397), one wage, state 19, removal.
    exact_callers(e, 0xf33ec, [0x16e0d8, 0x16e174])
    e.d(0x16e110, 34, 0, 4, 55)
    e.d(0x16e11c, 14, 3, 0, 397)
    e.bl(0xf3520, 0xf46bc)
    e.bl(0xf3534, 0xcbfdc)
    e.d(0xf3558, 32, 0, 5, -2064)
    e.d(0xf355c, 14, 4, 0, 19)
    e.bl(0xf3568, 0xe66b0)
    e.bl(0xf3570, 0xfad64)
    # Training: grade 4 cannot train; points = amount / PoundsPerTrainingPoint, capped at 100;
    # reaching 100 raises the grade and resets happiness to 100.
    e.d(0xf35b0, 11, 0, 0, 4)
    e.bl(0xf35d4, 0xcbfdc)
    e.d(0xf35e4, 32, 0, 3, -1472)
    for at, offset in ((0xf362c, 868), (0xf3640, 948), (0xf3654, 1116), (0xf3668, 1008), (0xf367c, 1056)):
        e.d(at, 32, 26, 3, offset)
    e.x(0xf3690, 31, 491, 0, 29, 26)
    e.d(0xf3694, 11, 0, 0, 100)
    e.d(0xf36a4, 34, 0, 28, 488)
    e.d(0xf36ac, 11, 0, 0, 99)
    e.d(0xf36d0, 14, 3, 3, 1)
    e.d(0xf36d4, 36, 3, 28, 484)
    e.d(0xf36e4, 48, 0, 2, -10696)
    e.d(0xf36e8, 52, 0, 28, 500)
    # Resting: energy(+504) += RecuperationRate[grade], happiness(+500) += HappinessRecuperationRate[grade],
    # each clamped to 0..100; work resumes when energy reaches 100.
    e.d(0xf3da4, 32, 0, 3, 484)
    e.d(0xf3da8, 48, 2, 3, 504)
    e.rlwinm(0xf3dac, 0, 0, 4, 0, 27)
    e.d(0xf3db8, 48, 1, 3, 756)
    e.d(0xf3dec, 48, 2, 31, 500)
    e.d(0xf3dfc, 48, 1, 3, 760)
    e.d(0xf3e40, 10, 0, 0, 100)
    # Working: energy -= 0.012*(6-grade), happiness -= 0.005*(6-grade), clamped.
    e.d(0xf44c0, 32, 4, 3, 484)
    e.d(0xf44cc, 8, 4, 4, 6)
    e.d(0xf44d0, 50, 3, 2, -10736)
    e.d(0xf44d8, 48, 4, 3, 504)
    e.d(0xf453c, 50, 3, 2, -10744)
    e.d(0xf4544, 48, 4, 3, 500)
    drain = (double(e, -10736), double(e, -10744))
    clamp = (single(e, -10692), single(e, -10696))
    if drain != (0.012, 0.005) or clamp != (0.0, 100.0):
        raise pef.PEFError('staff energy constants')
    return {'wage': 'PayMultiplier[type] * BaseWage[grade] (balance 832+4*type, 748+16*grade)',
            'type_index': {'handyman': 0, 'mechanic': 1, 'entertainer': 2, 'guard': 3, 'researcher': 4,
                           'other': 5},
            'wage_paid': 'per staff member on CMsgEndOfMonth (type 12)',
            'dismissal_cost': 'one wage (same formula), after optional UITEXT 397 confirmation',
            'hire_cost': 'none among the 16 direct callers of the debit routine 0xcbfdc',
            'debit_callers': len(debit_callers),
            'training': 'amount/PoundsPerTrainingPoint[type][grade] points, cap 100 per payment; '
                        '100 points -> grade+1, remainder kept, happiness = 100; grade 4 cannot train',
            'rest_recovery_per_step': {'energy_field': 504, 'happiness_field': 500,
                                       'energy': 'RecuperationRate[grade]', 'happiness': 'HappinessRecuperationRate[grade]',
                                       'clamp': list(clamp), 'resume_at_energy': 100},
            'work_drain_per_step': {'energy': f'{drain[0]} * (6 - grade)', 'happiness': f'{drain[1]} * (6 - grade)'},
            'unresolved': 'call cadence of the rest/work steps (world ticks per step)'}


# -- bankruptcy, profit year, research workload --------------------------------------
def finance(e: Evidence) -> dict:
    e.d(0xcc158, 11, 0, 3, 13)
    e.d(0xcc164, 11, 0, 3, 12)
    e.d(0xcc170, 11, 0, 3, 19)
    e.bl(0xcc180, 0xcc21c)
    e.d(0xcc1e4, 14, 0, 0, 0)
    e.d(0xcc1e8, 36, 0, 29, 292)
    e.d(0xcc18c, 11, 0, 0, 2)
    e.d(0xcc19c, 32, 0, 4, -22728)
    e.d(0xcc1a0, 11, 0, 0, 4)
    e.bl(0xcc1a8, 0x105c6c)
    e.bl(0xcc1b8, 0x1059e0)
    e.d(0xcc1c4, 11, 0, 0, 10)
    e.d(0xcc1d4, 32, 3, 3, 408)
    e.d(0xcc1d8, 36, 3, 29, 12)
    # Month end: money < 0 and >= 6 thirty-day months since the balance went negative -> CMsgEvent(2).
    e.d(0xcc3d0, 32, 0, 24, 12)
    e.d(0xcc3d4, 11, 0, 0, 0)
    e.d(0xcc404, 32, 0, 24, 288)
    e.bl(0xcc434, 0xe4750)
    e.d(0xcc440, 15, 5, 0, -1947)
    e.d(0xcc444, 14, 6, 5, -32768)
    e.d(0xcc44c, 14, 5, 0, 6034)
    e.bl(0xcc450, 0x1c4100)
    e.d(0xcc454, 11, 0, 4, 6)
    e.bc(0xcc458, 12, 0, 0xcc4bc)
    e.d(0xcc460, 14, 4, 0, 2)
    e.bl(0xcc464, 0x116528)
    e.d(0x116530, 32, 0, 2, -27276)
    for slot, expected in ((0x408a4, ('data', 0x4089c)), (0x4089c, ('code', 0x1cc8bf)),
                           (0x408ac, ('data', 0x6e18)), (0x6e18, ('code', 0xcd0ac))):
        if e.slot_target(slot) != expected:
            raise pef.PEFError(f'data:{slot:#x} CMsgEvent RTTI')
    event = e.text(0x1cc8bf, 'CMsgEvent')
    e.d(0xcd0ac, 14, 3, 0, 19)
    # Bankrupt: world mode 4 unless 0x105c6c reports a non-matching id pair.
    e.d(0x105a30, 14, 0, 0, 4)
    e.d(0x105a40, 36, 0, 27, -22728)
    e.x(0x105c7c, 31, 32, 0, 4, 0)
    e.d(0x105c84, 14, 3, 0, 0)
    e.d(0x105c8c, 14, 3, 0, 1)
    # Profit-year ticket reads +292, zeroed on end of year (type 13).
    e.d(0xccfa8, 32, 3, 3, 292)
    # Research workload: lab[5216] = StartingWorkLoad at park init; research panel sets 0..100.
    e.toc_load(0xf085c, 4, BALANCE)
    e.d(0xf088c, 32, 0, 4, 1344)
    e.d(0xf0890, 36, 0, 29, 5216)
    exact_callers(e, 0xf0854, [0x104d18])
    e.d(0xf1310, 10, 0, 4, 100)
    e.d(0xf1328, 36, 4, 3, 5216)
    exact_callers(e, 0xf130c, [0x1610b4])
    month = (6034 << 32) + magic(-1947, -32768)
    return {'monthly_finance': '0xcc21c on CMsgEndOfMonth (12): pending income, eight loan slots, red check',
            'red_start_field': 288, 'bankrupt_after_months_in_red': 6, 'month_divisor_100ns': month,
            'bankrupt_event': f'{event}(2) (GetType 19) -> 0x1059e0 sets world mode 4',
            'bankrupt_gate': 'world mode != 4 and halfword data 0xecdcc == halfword data 0x4491e (meaning unresolved)',
            'initial_money': 'BankAccountInfo.InitialCash (balance 408) on CMsgEvent(10)',
            'profit_year_ticket_value': '+292: credits add, debits subtract, zeroed on CMsgEndOfYear (13)',
            'research_workload': 'lab 5216 = Research.StartingWorkLoad (balance 1344); panel setter clamps to 100'}


def inspect_followup(e: Evidence) -> dict:
    return {'main_balance': main_balance(e), 'new_player_flag': new_player_flag(e),
            'game_type_transitions': game_type_transitions(e), 'advisor': advisor_tables(e),
            'staff_economy': staff_economy(e), 'finance': finance(e)}

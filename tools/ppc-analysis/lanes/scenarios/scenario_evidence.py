"""Reproduce static scenario/progression facts for the identified Feral Mac executable.

Covers game modes, golden tickets and keys, theme entry, challenge activation,
strikes and research group opening. No original instructions are executed and
the heuristic function finder is not used. Every check names a code offset and
the interpreted operands expected there; the output is interpreted metadata
(offsets, operands, constants, short identifier strings), never binary contents
or disassembly. See docs/reverse/PPC-scenarios.md for the interpretation.
"""
from __future__ import annotations

import argparse
import hashlib
import json
from pathlib import Path
import struct
import sys

sys.path.insert(0, str(Path(__file__).resolve().parents[2]))
sys.path.insert(0, str(Path(__file__).resolve().parent))
import pef  # noqa: E402

APP_SHA256 = '04809cd4ccee5433c7fb0b7c93d32f6a7aa629c1849181c0b7906415e5e295f5'


class Evidence:
    """Bounded instruction-field checks against one identified container."""

    def __init__(self, container: pef.PEFContainer):
        self.c = container
        self.code = bytes(container.code.data)
        self.data = bytes(container.data_section.data)
        self.relocs = container.relocs[container.data_section.index]
        self.toc = self.relocs[container.main[1] + 4].addend
        self.checked = 0
        self._calls = None

    # -- primitive decoding -------------------------------------------------
    def word(self, offset: int) -> int:
        return pef._u32(self.code, offset)

    def fail(self, offset: int, what: str, actual):
        raise pef.PEFError(f'code:{offset:#x} {what}: unexpected interpreted value {actual!r}')

    def d(self, offset: int, op: int, rt: int, ra: int, imm: int) -> None:
        """D-form: primary opcode, rT/rS (or crf<<2|L), rA, signed immediate."""
        w = self.word(offset)
        value = w & 0xffff
        if op not in (10, 24, 25, 28, 29) and value & 0x8000:
            value -= 0x10000
        actual = (w >> 26, w >> 21 & 31, w >> 16 & 31, value)
        if actual != (op, rt, ra, imm):
            self.fail(offset, 'D-form', actual)
        self.checked += 1

    def x(self, offset: int, op: int, xo: int, rt: int, ra: int, rb: int) -> None:
        """X/XO-form (record bit ignored): primary opcode, 10-bit extended opcode, fields."""
        w = self.word(offset)
        actual = (w >> 26, w >> 1 & 0x3ff, w >> 21 & 31, w >> 16 & 31, w >> 11 & 31)
        if actual != (op, xo, rt, ra, rb):
            self.fail(offset, 'X-form', actual)
        self.checked += 1

    def a(self, offset: int, op: int, xo: int, rt: int, ra: int, rb: int) -> None:
        w = self.word(offset)
        actual = (w >> 26, w >> 1 & 31, w >> 21 & 31, w >> 16 & 31, w >> 11 & 31)
        if actual != (op, xo, rt, ra, rb):
            self.fail(offset, 'A-form', actual)
        self.checked += 1

    def rlwinm(self, offset: int, rs: int, ra: int, sh: int, mb: int, me: int) -> None:
        w = self.word(offset)
        actual = (w >> 26, w >> 21 & 31, w >> 16 & 31, w >> 11 & 31, w >> 6 & 31, w >> 1 & 31)
        if actual != (21, rs, ra, sh, mb, me):
            self.fail(offset, 'rlwinm', actual)
        self.checked += 1

    def bl(self, offset: int, target: int) -> None:
        w = self.word(offset)
        if w >> 26 != 18 or w & 3 != 1:
            self.fail(offset, 'relative linked branch', w >> 26)
        disp = w & 0x03fffffc
        if disp & 0x02000000:
            disp -= 0x04000000
        if offset + disp != target:
            self.fail(offset, 'call target', hex(offset + disp))
        self.checked += 1

    def bc(self, offset: int, bo: int, bi: int, target: int) -> None:
        w = self.word(offset)
        disp = w & 0xfffc
        if disp & 0x8000:
            disp -= 0x10000
        actual = (w >> 26, w >> 21 & 31, w >> 16 & 31, w & 3, offset + disp)
        if actual != (16, bo, bi, 0, target):
            self.fail(offset, 'conditional branch', actual)
        self.checked += 1

    # -- relocation-backed targets ------------------------------------------
    def slot_target(self, slot: int):
        r = self.relocs.get(slot)
        if r is None:
            raise pef.PEFError(f'data:{slot:#x} is not a relocated word')
        if r.kind == 'import':
            return ('import', self.c.imports[r.target].name)
        return ('code' if r.target == self.c.code.index else 'data', r.addend)

    def toc_load(self, offset: int, rt: int, expected) -> None:
        """lwz rT, d(r2) whose TOC slot relocates to the expected target."""
        w = self.word(offset)
        disp = w & 0xffff
        if disp & 0x8000:
            disp -= 0x10000
        self.d(offset, 32, rt, 2, disp)
        target = self.slot_target(self.toc + disp)
        if target != expected:
            self.fail(offset, 'TOC slot target', target)

    def import_call(self, offset: int, name: str) -> None:
        """bl to CFM glue whose first instruction loads the named import's slot."""
        w = self.word(offset)
        disp = w & 0x03fffffc
        if disp & 0x02000000:
            disp -= 0x04000000
        glue = offset + disp
        self.bl(offset, glue)
        g = self.word(glue)
        if g >> 16 != 0x8182:
            self.fail(glue, 'import glue', hex(g))
        gd = g & 0xffff
        if gd & 0x8000:
            gd -= 0x10000
        if self.slot_target(self.toc + gd) != ('import', name):
            self.fail(glue, 'glue import', self.slot_target(self.toc + gd))

    def cstring(self, offset: int, limit: int = 96) -> str:
        end = self.code.find(b'\0', offset, offset + limit)
        if end < 0:
            raise pef.PEFError(f'code:{offset:#x} has no bounded string')
        return self.code[offset:end].decode('mac_roman')

    def text(self, offset: int, expected: str) -> str:
        actual = self.cstring(offset).rstrip('\n')
        if actual != expected.rstrip('\n'):
            self.fail(offset, 'identifier string', actual)
        return actual

    def schema(self, offset: int, names: list[tuple[int, str]]) -> list[str]:
        """Balance-schema records are 60 bytes: a type word and a 56-byte name."""
        out = []
        for index, (kind, name) in enumerate(names):
            record = offset + 60 * index
            actual_kind = struct.unpack_from('>I', self.data, record)[0]
            raw = self.data[record + 4:record + 60]
            actual_name = raw[:raw.index(b'\0')].decode('ascii')
            if (actual_kind, actual_name) != (kind, name):
                raise pef.PEFError(f'data:{record:#x} schema record {actual_kind, actual_name!r}')
            out.append(f'{name}:{kind}')
        return out

    def calls_to(self, target: int) -> list[int]:
        if self._calls is None:
            self._calls = {}
            for offset in range(0, len(self.code), 4):
                w = self.word(offset)
                if w >> 26 == 18 and w & 3 == 1:
                    disp = w & 0x03fffffc
                    if disp & 0x02000000:
                        disp -= 0x04000000
                    self._calls.setdefault(offset + disp, []).append(offset)
        return self._calls.get(target, [])

    def pointer_refs(self, target: int) -> list[int]:
        index = self.c.code.index
        return sorted(o for o, r in self.relocs.items()
                      if r.kind == 'section' and r.target == index and r.addend == target)

    def sole_callers(self, target: int, expected: list[int]) -> list[str]:
        callers = self.calls_to(target)
        if callers != expected or self.pointer_refs(target):
            raise pef.PEFError(f'code:{target:#x} callers {[hex(c) for c in callers]} / pointers')
        self.checked += 1
        return [hex(c) for c in callers]


def magic(high: int, low: int) -> int:
    """Value of a lis/addi pair (addi sign-extends its immediate)."""
    return ((high << 16) + low) & 0xffffffff


def load_identified(path: Path) -> pef.PEFContainer:
    raw = path.read_bytes()
    digest = hashlib.sha256(raw).hexdigest()
    if digest != APP_SHA256:
        raise pef.PEFError(f'identity of {path.name}: unexpected interpreted value {digest!r}')
    return pef.PEFContainer(raw, path.name)


def progression(e: Evidence) -> dict:
    """Player-global save info (76-byte singleton at data 0x120d84)."""
    # Constructor 0x1284e4 calls the field reset 0x1287d4.
    e.bl(0x128548, 0x1287d4)
    e.d(0x1287d4, 14, 4, 0, 0)
    e.d(0x1287d8, 14, 0, 0, 1)
    for i, field in enumerate((24, 25, 26, 27, 28, 29)):
        e.d(0x1287dc + 4 * i, 38, 4, 3, field)
    e.d(0x1287f4, 38, 4, 3, 38)
    e.d(0x1287f8, 38, 4, 3, 39)
    e.d(0x1287fc, 36, 4, 3, 28)
    e.d(0x128800, 36, 4, 3, 32)
    e.d(0x128804, 38, 4, 3, 36)
    e.d(0x128808, 38, 0, 3, 37)
    e.d(0x12880c, 38, 0, 3, 72)
    # Serializer 0x129308: (member offset, name) pairs; r31 is the string pool.
    e.toc_load(0x129318, 31, ('code', 0x1d8725))
    pool = 0x1d8725
    members = {}
    for at, (member_base, name_offset, name) in {
            0x129338: (24, 102, 'mEarnedGlobalTicket[i]'), 0x129370: (38, 125, 'mEarnedSecretTicket[i]'),
            0x1293a8: (28, 148, 'mSpentTickets'), 0x1293cc: (32, 162, 'mExtraKeys'),
            0x1293f0: (36, 173, 'mEasyModeUser'), 0x129414: (37, 187, 'mSwearFilterOn'),
            0x129438: (72, 202, 'mFirstTimePlayer')}.items():
        indexed = name.endswith('[i]')
        e.d(at, 14, 4, 23 if indexed else 26, member_base)
        e.d(at + (12 if indexed else 4), 14, 5, 31, name_offset)
        members[name] = member_base
        e.text(pool + name_offset, name)
    # Earned-ticket count (globals + per-theme locals + secrets) and keys.
    for at, field, base in ((0x128b7c, 24, 3), (0x128b94, 25, 29), (0x128bb0, 26, 29),
                            (0x128bcc, 27, 29), (0x128c38, 38, 29), (0x128c50, 39, 29)):
        e.d(at, 34, 0, base, field)
    e.bl(0x128c04, 0x12a50c)
    e.bl(0x128c1c, 0x12a388)
    e.d(0x128c6c, 15, 3, 0, 21845)
    e.d(0x128c74, 14, 0, 3, 21846)
    e.d(0x128c78, 32, 4, 29, 32)
    e.x(0x128c7c, 31, 75, 3, 0, 5)
    e.rlwinm(0x128c80, 3, 0, 1, 31, 31)
    e.x(0x128c84, 31, 266, 0, 3, 0)
    e.x(0x128c88, 31, 266, 3, 4, 0)
    # Available tickets subtract mSpentTickets; spending adds to it once per item id.
    e.d(0x128b38, 32, 0, 29, 28)
    e.x(0x128b40, 31, 40, 3, 0, 3)
    e.bl(0x128cd4, 0x12b5a8)
    e.d(0x128cec, 32, 0, 30, 28)
    e.x(0x128cf4, 31, 266, 0, 0, 31)
    e.d(0x128cf8, 36, 0, 30, 28)
    # Mystery purchase: online skips tickets; otherwise cost (catalog +196) <= available, then spend once per id.
    e.d(0xd3034, 32, 28, 3, 196)
    e.d(0xd3054, 11, 0, 0, 1)
    e.bc(0xd3058, 12, 2, 0xd30b8)
    e.bl(0xd3064, 0xd313c)
    e.bl(0xd30b0, 0x128ca8)
    e.d(0xd30b8, 14, 3, 0, 1)
    e.d(0xd316c, 32, 30, 3, 196)
    e.bl(0xd319c, 0x128a2c)
    e.x(0xd31a0, 31, 0, 0, 30, 3)
    e.bc(0xd31a4, 12, 1, 0xd31b0)
    # Per-theme local ticket flag: set once, report whether newly set.
    e.d(0x12a444, 34, 0, 4, 0)
    e.d(0x12a448, 10, 0, 0, 1)
    e.d(0x12a460, 38, 0, 4, 0)
    # The only mExtraKeys increment and its only caller.
    e.d(0x128f3c, 32, 4, 3, 32)
    e.d(0x128f40, 14, 0, 4, 1)
    e.d(0x128f44, 36, 0, 3, 32)
    e.d(0x128f54, 38, 4, 3, 36)
    e.d(0x128f4c, 34, 3, 3, 36)
    magic3 = magic(21845, 21846)
    return {
        'save_info_size': 76,
        'reset_values': {'mEarnedGlobalTicket[0..3]': 0, 'mEarnedSecretTicket[0..1]': 0, 'mSpentTickets': 0,
                         'mExtraKeys': 0, 'mEasyModeUser': 0, 'mSwearFilterOn': 1, 'mFirstTimePlayer': 1},
        'serialized_members': members,
        'keys_expression': 'mExtraKeys + trunc((global + local + secret earned) / 3)',
        'division_by_three_multiplier': hex(magic3),
        'available_tickets_expression': '(global + local + secret earned) - mSpentTickets',
        'mystery_purchase': 'GameType 1: allowed without tickets; else requires catalog[+196] <= available, '
                            'then item id is added to the player-wide set and its cost added to mSpentTickets once',
        'extra_key_increment_callers': e.sole_callers(0x128f3c, [0x15ce80]),
        'keys_writers_found': ['reset 0x1287d4', 'serializer 0x129308', 'increment 0x128f3c'],
    }


def awards(e: Evidence) -> dict:
    # Award result after a newly earned ticket: 1 ticket, 2 ticket+key, 3 ticket+key+theme.
    e.bl(0x129cf8, 0x128b60)
    e.d(0x129cfc, 11, 0, 3, 0)
    e.bc(0x129d00, 4, 1, 0x129e68)
    e.d(0x129df4, 15, 3, 0, 21845)
    e.d(0x129e0c, 7, 0, 0, 3)
    e.x(0x129e10, 31, 40, 0, 0, 4)
    e.bc(0x129e14, 4, 2, 0x129e68)
    e.bl(0x129e2c, 0x12a4c8)
    e.bl(0x129e38, 0x128b60)
    e.x(0x129e3c, 31, 0, 0, 3, 29)
    e.d(0x129e44, 14, 3, 0, 3)
    e.d(0x129e60, 14, 3, 0, 2)
    e.d(0x129e68, 14, 3, 0, 1)
    # Theme CostToEnter from the lazily loaded 28-byte global.sam object (-1 if unloaded).
    e.bl(0x12a4dc, 0x12a50c)
    e.d(0x12a4e8, 32, 3, 31, 28)
    e.d(0x12a4ec, 32, 3, 3, 20)
    e.d(0x12a4f4, 14, 3, 0, -1)
    e.d(0x12a548, 14, 3, 0, 28)
    e.import_call(0x12a54c, '__nw__FUl')
    e.d(0x12a590, 14, 4, 31, 735)
    path = e.text(0x1d8725 + 735, 'data:levels:%s:global.sam')
    e.toc_load(0x12a514, 31, ('code', 0x1d8725))
    global_sam = e.schema(0x34960, [(4, 'CanEarnTicketsInTheme'), (4, 'CanSpendTicketsInTheme'), (1, 'Tickets')])
    global_sam += e.schema(0x34a50, [(4, 'CostToEnter'), (1, 'Keys')])
    global_sam += e.schema(0x34b04, [(5, 'GateObjectId'), (1, 'ParkName')])
    # Global tickets: re-award records a better value in the current theme ("moved here").
    e.x(0x128e6c, 31, 0, 0, 30, 3)
    e.bc(0x128e70, 4, 1, 0x128ea0)
    e.d(0x128e98, 14, 3, 0, 4)
    # TellTheWorld switch values (local and global).
    for at, value in ((0xd3940, 2), (0xd394c, 1), (0xd3958, 4)):
        e.d(at, 11, 0, 25, value)
    for at, value in ((0xd3bd8, 3), (0xd3be4, 1), (0xd3bf4, 5)):
        e.d(at, 11, 0, 25, value)
    for at, name in ((0x1cd6ee, 'Local ticket only'), (0x1cd701, 'Local ticket and key'),
                     (0x1cd717, 'Local ticket, key and park'), (0x1cd809, 'Global ticket award moved here')):
        e.text(at, name)
    return {'award_result_codes': {'1': 'ticket only', '2': 'ticket and key',
                                   '3': 'ticket, key and park (Keys() equals some theme CostToEnter)',
                                   '4': 'global ticket moved to this theme (global awards only)'},
            'global_sam_path_format': path, 'global_sam_object_size': 28,
            'global_sam_schema_order': global_sam,
            'cost_to_enter_object_offset': 20, 'cost_to_enter_missing_value': -1}


def modes(e: Evidence) -> dict:
    game_type = ('data', 0x53d98)
    # Startup flags select GameType 2/1/0; SetGameType mirrors the type into one flag bit.
    for at, mb, value_at, value in ((0x12bb88, 6, 0x12bb94, 2), (0x12bba0, 7, 0x12bbac, 1),
                                    (0x12bbb8, 8, 0x12bbc4, 0)):
        e.rlwinm(at, 3, 0, 0, mb, mb)
        e.d(value_at, 14, 4, 0, value)
    e.d(0x12bc40, 36, 30, 29, 0)
    e.d(0x12bc68, 25, 0, 0, 128)
    e.d(0x12bc9c, 25, 0, 0, 256)
    e.d(0x12bcd0, 25, 0, 0, 512)
    # Player load: unless GameType is 1, mEasyModeUser selects GameType 2, else 0.
    e.toc_load(0x13782c, 29, game_type)
    e.d(0x137954, 11, 0, 0, 1)
    e.bc(0x137958, 12, 2, 0x1379ec)
    e.bl(0x13798c, 0x128f4c)
    e.d(0x1379b4, 14, 4, 0, 2)
    e.bl(0x1379bc, 0x12bbf4)
    e.d(0x1379e0, 14, 4, 0, 0)
    e.bl(0x1379e8, 0x12bbf4)
    # Player creation: easy flag = (selected control == 1805); stored per slot and in mEasyModeUser.
    e.d(0x15cfa4, 14, 4, 0, 1804)
    e.d(0x15cfb8, 8, 3, 3, 1805)
    e.x(0x15cfbc, 31, 26, 3, 3, 0)
    e.rlwinm(0x15cfc4, 3, 28, 27, 24, 31)
    e.bl(0x15cffc, 0x13741c)
    e.bl(0x15d034, 0x13781c)
    e.d(0x137508, 37, 28, 29, 20)
    e.bl(0x13758c, 0x1284e4)
    e.d(0x137598, 14, 4, 0, 1)
    e.bl(0x13759c, 0x128f54)
    e.d(0x1375c8, 14, 4, 0, 0)
    e.bl(0x1375cc, 0x128f54)
    e.d(0x136fe0 + 0x4c, 11, 0, 29, 3)
    # Easy players receive each theme's easymode.TPWI copy when it exists.
    e.bl(0x137548, 0x137600)
    e.toc_load(0x137608, 29, ('code', 0x1d9a62))
    e.text(0x1d9a62, 'easymode')
    e.d(0x1376b0, 11, 0, 24, 0)
    e.import_call(0x137750, 'LbFile_Exists__FPCc')
    e.import_call(0x1377b8, 'LbFile_Copy__FPCcPCc')
    e.d(0x1bf974, 14, 3, 31, 17460)
    e.d(0x1bf978, 14, 4, 30, 400)
    # Balance files: GameType 1 online overlay; otherwise Standard, and GameType 2 adds the Easy_ prefix.
    e.toc_load(0x104788, 29, ('code', 0x1d369c))
    e.d(0x1047e0, 11, 0, 0, 1)
    e.d(0x1047ec, 14, 4, 29, 15)
    e.d(0x104840, 14, 4, 29, 73)
    e.d(0x104864, 14, 6, 29, 98)
    e.d(0x1048ac, 11, 0, 0, 2)
    e.d(0x1048c0, 14, 4, 29, 111)
    e.toc_load(0x1048b4, 6, ('data', 0x1577c0))
    e.d(0x1048c4, 32, 6, 6, 17028)
    files = [e.text(0x1d369c + o, s) for o, s in ((15, 'data:levels:online_Standard.sam'),
                                                   (73, 'data:levels:Standard.sam'), (98, 'Standard.sam'),
                                                   (111, '%s:%s%s'))]
    e.toc_load(0x1c0048, 3, ('data', 0x1577c0))
    e.bl(0x1c0054, 0x1bf60c)
    e.d(0x1bf814, 14, 3, 31, 17020)
    e.d(0x1bf818, 14, 4, 30, 284)
    e.d(0x1bf824, 14, 3, 31, 17040)
    e.d(0x1bf828, 14, 4, 30, 290)
    prefixes = [e.text(0x1ddcf2 + 284, 'Easy_'), e.text(0x1ddcf2 + 290, 'Online_'),
                e.text(0x1ddcf2 + 400, '.TPWI')]
    # World init zeroes the world tick counter used by the cadence checks below.
    e.d(0x1048f8, 15, 20, 31, 30)
    e.d(0x1048fc, 14, 21, 0, 0)
    e.d(0x104900, 36, 21, 20, -22772)
    return {'game_type_object': hex(0x53d98),
            'game_types': {'0': 'offline Full Simulation (mEasyModeUser 0)', '1': 'online (online_Standard.sam)',
                           '2': 'Instant Action / easy user (Easy_ theme overlay)'},
            'startup_flag_bits_for_types_2_1_0': ['0x02000000', '0x01000000', '0x00800000'],
            'create_player_easy_control_id': 1805, 'create_player_other_control_id': 1804,
            'player_slots': 4, 'balance_files': files, 'prefix_strings': prefixes,
            'easy_prefix_object': 'data 0x1577c0 + 17020 (string object), pointer read at +17028 (inferred char* member)',
            'world_tick_counter': 'world + 0x1e0000 - 22772'}


def entry_and_keys(e: Evidence) -> dict:
    game_type = ('data', 0x53d98)
    # Lobby enter: GameType 2 bypasses keys; otherwise enter iff CostToEnter <= Keys().
    e.toc_load(0x964e4, 28, game_type)
    e.d(0x96528, 11, 0, 0, 2)
    e.bc(0x9652c, 12, 2, 0x965fc)
    e.bl(0x96578, 0x129ae0)
    e.bl(0x965cc, 0x128b60)
    e.bl(0x965d8, 0x12a4c8)
    e.x(0x965dc, 31, 0, 0, 3, 31)
    e.bc(0x965e0, 12, 1, 0x96610)
    e.d(0x965ec, 32, 12, 12, 76)
    e.d(0x96604, 32, 12, 12, 76)
    # New-player flag: set by creation and by front-end init when no player exists.
    flag = ('data', 0x134db0)
    e.bl(0x15d144, 0x15cf00)
    e.toc_load(0x15d154, 5, flag)
    e.d(0x15d158, 14, 0, 0, 1)
    e.d(0x15d164, 36, 0, 5, 0)
    e.bl(0x15d178, 0x15cd38)
    e.toc_load(0x15c830, 24, flag)
    e.bl(0x15ccbc, 0x136fe0)
    e.d(0x15ccf0, 14, 0, 0, 1)
    e.d(0x15ccf4, 36, 0, 24, 0)
    e.d(0x15cd10, 14, 0, 0, 0)
    e.d(0x15cd14, 36, 0, 24, 0)
    # Lobby entry with the flag: easy users get queue id 394; others mExtraKeys++, save, id 393.
    e.toc_load(0x15ce00, 3, flag)
    e.d(0x15ce08, 11, 0, 0, 0)
    e.bc(0x15ce0c, 12, 2, 0x15cecc)
    e.d(0x15ce30, 11, 0, 0, 2)
    e.d(0x15ce3c, 14, 4, 0, 394)
    e.bl(0x15ce80, 0x128f3c)
    e.bl(0x15ceb4, 0x137dbc)
    e.d(0x15cebc, 14, 4, 0, 393)
    writers = e.pointer_refs(0x15cd38)
    return {'theme_entry_rule': 'GameType 2: no key check; otherwise CostToEnter(theme) <= Keys()',
            'key_consumption_on_entry': False,
            'new_player_extra_key': 'Full Simulation (GameType != 2) players: +1 mExtraKeys on first lobby entry after creation',
            'new_player_flag_writers': ['0x15ccf4 (=1 when no players)', '0x15cd14 (=0)', '0x15d164 (=1 after creation)'],
            'lobby_entry_callers': [hex(c) for c in e.calls_to(0x15cd38)], 'lobby_entry_pointer_refs': writers,
            'queued_ids': {'393': 'non-easy new player (extra key)', '394': 'easy new player'}}


def golden_ticket_checks(e: Evidence) -> dict:
    balance = ('data', 0x54860)
    # Checks run only for GameType 0, every world tick divisible by 100.
    e.toc_load(0xd2f2c, 30, ('data', 0x53d98))
    e.d(0xd2f60, 11, 0, 0, 0)
    e.bc(0xd2f64, 4, 2, 0xd2f80)
    e.bl(0xd2f6c, 0xd31d0)
    e.bl(0xd2f74, 0xd34c0)
    e.bl(0xd2f7c, 0xd35b4)
    e.d(0xd6820, 15, 3, 0, 20972)
    e.d(0xd6824, 14, 0, 3, -31457)
    e.d(0xd6834, 32, 3, 3, -22772)
    e.x(0xd6838, 31, 11, 0, 0, 3)
    e.rlwinm(0xd683c, 0, 0, 27, 5, 31)
    e.d(0xd6840, 7, 0, 0, 100)
    e.bl(0xd6850, 0xd2f1c)
    # Local tickets (strict greater-than tests against GoldenTicketLocal fields).
    e.toc_load(0xd31e0, 31, balance)
    e.bl(0xd3240, 0xc3b7c)
    e.d(0xd3244, 32, 0, 31, 1872)
    e.x(0xd3248, 31, 0, 0, 3, 0)
    e.bc(0xd324c, 4, 1, 0xd325c)
    e.bl(0xd32a0, 0xc3684)
    e.d(0xd32a4, 32, 0, 31, 1876)
    e.bl(0xd3304, 0xc19e4)
    e.d(0xd3308, 32, 3, 31, 1880)
    e.x(0xd3328, 63, 32, 0, 1, 0)
    e.bc(0xd332c, 4, 1, 0xd3350)
    e.bl(0xd3334, 0xc3684)
    e.d(0xd3338, 32, 0, 31, 1884)
    e.bl(0xd3394, 0xc5510)
    e.bl(0xd33f0, 0x108424)
    e.bl(0xd33f4, 0xccfa8)
    e.d(0xd33f8, 32, 0, 31, 1888)
    e.d(0xd3458, 32, 4, 31, 1896)
    e.bl(0xd345c, 0xc3b88)
    e.d(0xd3460, 32, 0, 31, 1892)
    e.bl(0xd3478, 0xe4628)
    e.d(0xd347c, 15, 5, 0, -1947)
    e.d(0xd3484, 14, 6, 5, -32768)
    e.d(0xd348c, 14, 5, 0, 6034)
    e.bl(0xd3490, 0x1c4100)
    e.x(0xd3498, 31, 0, 0, 4, 0)
    for at, index in ((0xd3254, 0), (0xd32b4, 1), (0xd3348, 2), (0xd33a4, 3), (0xd3408, 4), (0xd34a4, 5)):
        e.d(at, 14, 4, 0, index)
        e.bl(at + 4, 0xd3664)
    # Global tickets: value > threshold; big park reads offset 1916, not 1912.
    for call, stat, load, field, award, index in ((0xd3500, 0xc73bc, 0xd3504, 1900, 0xd3518, 0),
                                                  (0xd3528, 0xc6cec, 0xd352c, 1904, 0xd3540, 1),
                                                  (0xd3554, 0xc710c, 0xd3558, 1908, 0xd356c, 2),
                                                  (0xd3578, 0xc8cc0, 0xd357c, 1916, 0xd3590, 3)):
        e.bl(call, stat)
        e.d(load, 32, 0, 30, field)
        e.d(award, 14, 4, 0, index)
        e.bl(award + 4, 0xd3748)
    stack_only = []
    for offset in range(0, len(e.code), 4):
        w = e.word(offset)
        if w >> 26 in (32, 34, 40, 42, 48, 50) and w & 0xffff == 1912 and w >> 16 & 31 != 1:
            stack_only.append(hex(offset))
    if stack_only:
        raise pef.PEFError(f'non-stack displacement-1912 loads at {stack_only}')
    # Secret camera ticket: coverage percentage == 100.
    e.bl(0xd361c, 0x128d90)
    e.bl(0xd362c, 0xc8b08)
    e.d(0xd3630, 11, 0, 3, 100)
    e.d(0xd363c, 14, 4, 0, 0)
    e.bl(0xd3640, 0xd381c)
    e.d(0xc8bd4, 7, 0, 27, 100)
    e.x(0xc8bd8, 31, 491, 3, 0, 28)
    for predicate, cell_type in ((0x85178, 2), (0x85214, 7), (0x8521c, 30), (0x851d4, 9), (0x851f0, 10),
                                 (0x85240, 4), (0x852f8, 21)):
        e.d(predicate, 11, 0, 0, cell_type)
    e.d(0x85234, 14, 3, 0, 0)
    local = e.schema(0x38988, [(4, 'Visitors'), (4, 'PeopleInPark'), (4, 'Happiness'),
                               (5, 'AtLeastThisManyHappyPeople'), (4, 'ProfitYear'), (4, 'RecentVisitors'),
                               (4, 'RecentVisitorMonths'), (1, 'GoldenTicketLocal')])
    glob = e.schema(0x38ba4, [(4, 'CoasterHeight'), (4, 'GokartExcitement'), (4, 'WaterLength'),
                              (4, 'MinCellsOwned'), (4, 'MinCellsCovered'), (1, 'GoldenTicketGlobal')])
    month = (6034 << 32) + magic(-1947, -32768)
    return {'game_type_required': 0, 'world_tick_period': 100,
            'reciprocal_multiplier': hex(magic(20972, -31457)),
            'schema_local': local, 'schema_global': glob,
            'balance_offsets': {'Visitors': 1872, 'PeopleInPark': 1876, 'Happiness': 1880,
                                'AtLeastThisManyHappyPeople': 1884, 'ProfitYear': 1888, 'RecentVisitors': 1892,
                                'RecentVisitorMonths': 1896, 'CoasterHeight': 1900, 'GokartExcitement': 1904,
                                'WaterLength': 1908, 'MinCellsOwned': 1912, 'MinCellsCovered': 1916},
            'happiness_second_operand_function': hex(0xc3684),
            'people_in_park_function': hex(0xc3684),
            'month_divisor_100ns': month, 'month_divisor_days': month / 864_000_000_000,
            'non_stack_reads_of_offset_1912': [],
            'camera_ineligible_cell_types': [2, 7, 30], 'big_park_cell_types': [4, 9, 10, 21]}


def challenges(e: Evidence) -> dict:
    e.toc_load(0xcff14, 28, ('data', 0x53d98))
    e.d(0xcff40, 11, 0, 0, 0)
    e.bc(0xcff44, 12, 2, 0xcff50)
    e.d(0xcff50, 34, 0, 31, 984)
    e.bl(0xcff84, 0xe4628)
    e.d(0xcff88, 15, 5, 0, 10858)
    e.d(0xcff90, 14, 6, 5, -16384)
    e.d(0xcff98, 14, 5, 0, 201)
    e.bl(0xcff9c, 0x1c4100)
    e.d(0xcffa4, 32, 0, 3, 1928)
    e.x(0xcffa8, 31, 0, 0, 4, 0)
    e.bc(0xcffac, 12, 0, 0xcffcc)
    e.d(0xcffb8, 38, 0, 31, 984)
    names = e.schema(0x38d48, [(4, 'DaysAfterCompletedChallenge'), (4, 'DaysAfterDeclinedChallenge'),
                               (4, 'DaysUntilFirstChallenge'), (4, 'DeclinesToForfeit')])
    day = (201 << 32) + magic(10858, -16384)
    return {'game_type_required': 0, 'day_divisor_100ns': day, 'day_divisor_seconds': day / 10_000_000,
            'activation': 'park-age days >= DaysUntilFirstChallenge (offset 1928)', 'schema': names}


def strikes(e: Evidence) -> dict:
    # Message type 12 is CMsgEndOfMonth (vtable -> transition vector -> GetType returning 12).
    e.d(0xe4800, 14, 3, 0, 12)
    for slot, expected in ((0x6ef0, ('code', 0xe4800)), (0x4102c, ('data', 0x6ef0)),
                           (0x41024, ('data', 0x4101c)), (0x4101c, ('code', 0x1cfa9d))):
        if e.slot_target(slot) != expected:
            raise pef.PEFError(f'data:{slot:#x} RTTI chain {e.slot_target(slot)!r}')
    rtti = e.text(0x1cfa9d, 'CMsgEndOfMonth')
    e.d(0xf7aa4, 11, 0, 3, 13)
    e.d(0xf7ab0, 11, 0, 3, 12)
    e.bl(0xf7acc, 0xf7e18)
    # Consider each of five staff types once per tick: striking types stop, others are evaluated.
    e.d(0xf7e4c, 32, 0, 23, 100)
    e.bl(0xf7e5c, 0x1091ac)
    e.bl(0xf7f80, 0xf8bc0)
    e.bl(0xf7f94, 0xf8bd0)
    e.bl(0xf7fa4, 0xf7fe0)
    e.d(0xf7fb0, 11, 0, 24, 5)
    e.d(0xf8bc0, 7, 0, 4, 12)
    e.d(0xf8bc8, 32, 3, 3, 40)
    e.d(0xf8bd4, 14, 4, 0, 0)
    e.d(0xf8bdc, 36, 4, 3, 40)
    # Park age gate (24 thirty-day months) and escalation 0 -> 1 -> 2 -> 3 -> 4 (4 stays 4).
    e.bl(0xf803c, 0xe4628)
    e.d(0xf8050, 14, 5, 0, 6034)
    e.bl(0xf8054, 0x1c4100)
    e.d(0xf8058, 11, 0, 4, 24)
    e.bc(0xf805c, 12, 0, 0xf83b0)
    e.bl(0xf8068, 0xf8410)
    e.d(0xf807c, 33, 0, 24, 36)
    for at, level in ((0xf80b8, 1), (0xf8130, 2), (0xf81b4, 3), (0xf8238, 4), (0xf82b0, 4)):
        e.d(at, 14, 0, 0, level)
        e.d(at + 8, 36, 0, 24, 0)
    for at in (0xf8148, 0xf81cc, 0xf8250, 0xf82c8):
        e.d(at, 36, 0, 5, 40)
    for at, kind in ((0xf80c8, 0), (0xf814c, 2), (0xf81d0, 2), (0xf8254, 2), (0xf82cc, 2), (0xf8344, 3)):
        e.d(at, 14, 5, 0, kind)
    e.d(0xf8334, 11, 0, 0, 1)
    e.d(0xf839c, 14, 0, 0, 0)
    e.d(0xf83a4, 36, 0, 23, 0)
    e.bl(0xf83ac, 0xf8bd0)
    # Closed and empty park ends a strike (staff update).
    e.bl(0xf3fc4, 0x1091ac)
    e.bl(0xf3fd8, 0xc3684)
    e.bl(0xf4000, 0xf8bd0)
    end_strike = e.sole_callers(0xf8bd0, [0xf4000, 0xf7f94, 0xf83ac])
    # Grievance: forced flag, >3 staff with average fatigue(+504)/happiness(+500) < 15; handymen ratio > 0.2.
    e.d(0xf845c, 32, 0, 3, 100)
    ratio = struct.unpack('>f', struct.pack('>I', 0x3e4ccccd))[0]
    e.d(0xf84dc, 48, 0, 2, -10680)
    if struct.unpack_from('>I', e.data, e.toc - 10680)[0] != 0x3e4ccccd:
        raise pef.PEFError('handyman grievance ratio constant')
    e.a(0xf84f0, 59, 18, 1, 1, 31)
    e.x(0xf84f4, 63, 32, 0, 1, 0)
    e.d(0xf8550, 48, 0, 3, 504)
    e.d(0xf8580, 10, 0, 19, 3)
    e.d(0xf858c, 10, 0, 0, 15)
    e.d(0xf8594, 14, 4, 30, 705)
    e.d(0xf85f8, 48, 0, 3, 500)
    e.d(0xf863c, 14, 4, 30, 746)
    pool = 0x1d21f6
    labels = [e.text(pool + 705, '%ss are striking through fatigue'),
              e.text(pool + 746, '%ss are striking through unhappiness')]
    return {'consideration_message': rtti, 'consideration_message_type': 12,
            'minimum_park_age_months': 24, 'month_divisor_100ns': (6034 << 32) + magic(-1947, -32768),
            'levels': {'0': 'none', '1': 'warning', '2': 'first strike', '3': 'second strike', '4': 'third and later strikes'},
            'message_kinds_emitted': {'0': 'warning issued', '2': 'strike started', '3': 'warning called off'},
            'strike_end_callers': end_strike,
            'grievance': {'forced_flag_offset': 100, 'minimum_staff_exclusive': 3, 'average_threshold_exclusive': 15,
                          'fatigue_field': 504, 'happiness_field': 500, 'handyman_ratio_exclusive': ratio},
            'debug_labels': labels, 'staff_types': 5}


def research(e: Evidence) -> dict:
    e.toc_load(0xf0730, 5, ('data', 0x54860))
    e.d(0xf0740, 32, 0, 3, 484)
    e.d(0xf0748, 7, 0, 0, 12)
    e.d(0xf0750, 32, 31, 5, 1052)
    e.bl(0xf0788, 0xf0df0)
    contributors = e.sole_callers(0xf0df0, [0xf0788])
    e.sole_callers(0xf0728, [0xf02e4])
    e.bl(0xf0e10, 0xf1238)
    e.d(0xf0e24, 48, 31, 2, -10768)
    if struct.unpack_from('>f', e.data, e.toc - 10768)[0] != 100.0:
        raise pef.PEFError('research percentage divisor')
    e.d(0xf0e60, 32, 4, 26, 5216)
    e.a(0xf0ebc, 59, 18, 1, 0, 31)
    e.bl(0xf0ec0, 0xf2328)
    e.d(0xf1230, 32, 3, 3, 5132)
    names = e.schema(0x36b10, [(5, 'WorkDuration'), (5, 'ResearchAbility'),
                                                    (5, 'PoundsPerTrainingPoint'), (3, 'ResearcherConstsPerGrade')])
    # Group opening: cumulative researched percentage over groups <= g against the group-g threshold.
    e.d(0xf1618, 32, 0, 22, 16)
    e.d(0xf1624, 34, 0, 22, 20)
    e.bl(0xf1638, 0xf2494)
    e.d(0xf1668, 7, 0, 24, 100)
    e.x(0xf166c, 31, 459, 4, 0, 25)
    e.d(0xf1678, 14, 0, 3, 1280)
    e.x(0xf167c, 31, 23, 0, 31, 0)
    e.x(0xf1680, 31, 32, 0, 4, 0)
    e.bc(0xf1684, 12, 0, 0xf1690)
    e.d(0xf1688, 10, 0, 5, 7)
    e.d(0xf15f0, 38, 0, 29, 5144)
    tech = e.schema(0x373bc, [(6, 'PercentageForThisTech'), (3, 'ResearchTech')])
    # All research complete: Instant Action ends on the current theme; offline only when all themes are done.
    e.bl(0xf1934, 0xf1af4)
    e.bl(0xf1970, 0x129eec)
    e.d(0xf1994, 11, 0, 0, 2)
    e.d(0xf19a8, 14, 4, 0, 167)
    e.bl(0xf19e0, 0x105b50)
    e.d(0xf1a08, 11, 0, 0, 0)
    e.bl(0xf1a40, 0x129e88)
    e.d(0xf1a58, 14, 4, 0, 167)
    e.d(0x129efc, 38, 0, 3, 185)
    e.d(0x129eac, 34, 0, 3, 185)
    return {'points_per_contribution': 'ResearcherConstsPerGrade[grade].ResearchAbility (balance 1052 + 12*grade)',
            'research_point_sources': contributors,
            'allocation': 'points * effort[c] / sum(effort) * lab[5216] / 100 to category c current item',
            'group_threshold_balance_offset': '1280 + 4*current_group = ResearchTech[current_group + 1].PercentageForThisTech',
            'maximum_group': 7,
            'schema': names + tech,
            'completion_advisor_id': 167, 'theme_all_research_flag_offset': 185}


def inspect(bin_root: Path) -> dict:
    e = Evidence(load_identified(bin_root / 'SimThemePark.data'))
    if e.toc != 0x8000:
        raise pef.PEFError(f'unexpected TOC base {e.toc:#x}')
    result = {'identity': {'SimThemePark.data': APP_SHA256}, 'toc_base': hex(e.toc),
              'progression': progression(e), 'awards': awards(e), 'modes': modes(e),
              'theme_entry': entry_and_keys(e), 'golden_ticket_checks': golden_ticket_checks(e),
              'challenges': challenges(e), 'strikes': strikes(e), 'research': research(e)}
    from followup_evidence import inspect_followup  # noqa: E402 (imports this module)
    result.update(inspect_followup(e))
    from progression_evidence import inspect_progression  # noqa: E402 (imports this module)
    result.update(inspect_progression(e))
    result['instruction_checks'] = e.checked
    result['limitation'] = ('Static Mac (Feral 2000) evidence only; not PC Patch 2 or runtime proof. '
                            'Advisor speech/tag text, stat-function semantics and calendar scale remain unresolved.')
    return result


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('bin_root', type=Path, help='directory containing SimThemePark.data')
    args = parser.parse_args()
    try:
        result = inspect(args.bin_root)
    except (OSError, pef.PEFError) as error:
        parser.exit(1, f'scenario evidence: {error}\n')
    print(json.dumps(result, indent=2, sort_keys=True))


if __name__ == '__main__':
    main()

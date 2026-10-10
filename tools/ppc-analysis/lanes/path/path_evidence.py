"""Bounded static path-building witnesses (PATH-R); never execute or emit original instructions.

Usage: python3 -I path_evidence.py /path/to/mac-feral/bin [--pc-data /path/to/Data]

Addresses are section-relative. Instructions are decoded into fields and compared with expected
operands; only digests, decoded fields and derived values are reported. The pure functions below
restate the decoded rules so that tests can pin them without the binary. See
docs/reverse/PATH-plan.md for the narrative and the claim bounds.
"""
from __future__ import annotations

import argparse
import hashlib
import json
from pathlib import Path
import re
import struct
import sys

HERE = Path(__file__).resolve().parent
TOOLS = HERE.parents[1]
REPO = TOOLS.parents[1]
sys.path.insert(0, str(TOOLS))
sys.path.insert(0, str(TOOLS / 'lanes' / 'economy'))
import pef  # noqa: E402
from timer_evidence import call_target, d_fields, load_identified, require  # noqa: E402
from schema import fields as schema_fields  # noqa: E402

TOC = 0x8000

# Map cell types named by their proven consumers (section 2 of PATH-plan.md).
CELL_EMPTY = 0
CELL_PATH = 1      # Costs.PathCell, hover help 442, mode 1
CELL_QUEUE = 3     # Costs.QueueCell, hover help 444, mode 3 (QUEUE-R)

# Cell flag halfword +14.
FLAG_NOMODIFY = 0x20   # set on MAP InitialPath cells; blocks removal while the cell has neighbours
FLAG_UNOWNED = 0x40    # cells Buy Land charges Costs.MapCell for; the validator refuses them

# SetCell (0x80c50) type word flags.
SETCELL_REMOVE_GHOST = 0x80   # type|0x80 calls bracket the commit (ghost clean-up, medium)
SETCELL_PREVIEW = 0x100       # drag preview: LayLine(mode | 0x100, ...)
SETCELL_TEST_ONLY = 0x200     # validator only (0x84154), no write

VERTEX_CAPACITY = 1024        # 0x7bcf4 refuses a push at count >= 1024

BLOCKS = {
    'set_cell_type': (0x82ac4, 0x82d5c, '277b7c7393b14b5b83d4db553158f54df971cc6492f4bcee20bac39c53b9b4e8'),
    'can_change_cell_type': (0x8408c, 0x84154, '2bbdff82784a6b61868271eced74b11cb84a0d40f29896d6556276e2a269e422'),
    'place_validator': (0x84154, 0x84a74, '1025a402828428403768a1ae0f7a5607d3a2bab06b40d5af3b8f4643ca0309e7'),
    'lay_line': (0x84ea4, 0x85174, '578dc27c5561487f8a8d0e70cdbe1a8386d1cacd985aa20e6220a244f74b7fe0'),
    'cell_predicates': (0x85174, 0x85310, 'c11706d1bc2f94a91489e8fe38feb8ca03bce7d6bde74867c39cb384765b2de6'),
    'cell_init': (0x85538, 0x85740, '0fa6d763d4cba8420a8dbe9affbb48e1812151620fd8d8d7608a3e45e3862928'),
    'clear_cell': (0x859b4, 0x86020, '13ccc481f98b7f9a18bc75f8048ea59cb5c11320d645fe39d8a7ba59eef06ba0'),
    'build_globals': (0x7bc40, 0x7bd34, 'cb324fd7e3f5f669b23d6b0d0a023ab73154fb0423fdc5e989af407f669ad653'),
    'money': (0xcbf48, 0xcc084, 'f4306536a3cbbbaf268b23dc79352f2af4ef7c631be609f77ac52f174c4bea86'),
    'level_cost_loader': (0x10f29c, 0x10f404, 'a8128c61e46aee2be47c262ee5f1822afeef0a417767717652841886739c8ae8'),
    'park_view_hover': (0x139c64, 0x13a8ec, 'd548393347c0188c3c77ba62a434c7506d24121ff0ab05e1bfa726545e1a8fae'),
    'drag_preview': (0x6f440, 0x6f5dc, '7c30ff38535cef4f83a074478b43e174584c4d2f970e1ba9a9bb08996caf62a1'),
    'drag_commit': (0x70f84, 0x71304, '4fafe6807bada95ec3d275caf99913e207e980e5020016a5e1cbf0c310623fa5'),
    'buy_land_count': (0x7ebf0, 0x7ec70, '30e5438976a33955ce02129a6853824f00e651e20089fe5d95c0574965c256d3'),
    'set_mode': (0x7b320, 0x7b630, '424588c6d32dd3ddf4f6054c0eacc95d0964f1f6870149f4758f453fc7c951af'),
    'mode_accessors': (0x7b878, 0x7b8b0, '436c131ef5294414b537efe5119d7df4d2d9a2d9258bd0d41c25fecdf85d50f5'),
}

# (code offset, primary opcode, (rD/rS, rA, signed immediate), meaning). Decoded, never raw words.
FIELDS = (
    # Level cost loader 0x10f29c: CMainBalance fields -> cost globals (FogColour anchors the schema).
    (0x10f2d0, 32, (0, 31, 1684), 'ThemeEngine.FogColour (+1684) feeds SetFogColor'),
    (0x10f3cc, 32, (3, 31, 1472), 'Costs.PathCell (+1472) -> path cost setter'),
    (0x10f3d4, 32, (3, 31, 1468), 'Costs.QueueCell (+1468) -> queue cost setter'),
    (0x10f3dc, 32, (3, 31, 1476), 'Costs.KartTrackCell (+1476)'),
    (0x10f3e4, 32, (3, 31, 1480), 'Costs.WaterTrackCell (+1480)'),
    # Build globals 0x7bc40..: setters/getters through TOC slots, vertex stack cap.
    (0x7bc4c, 32, (4, 2, -28060), 'path cost setter: TOC slot 0x1264'),
    (0x7bc58, 32, (3, 2, -28060), 'path cost getter: TOC slot 0x1264'),
    (0x7bc64, 32, (4, 2, -28064), 'queue cost setter: TOC slot 0x1260'),
    (0x7bc70, 32, (3, 2, -28064), 'queue cost getter: TOC slot 0x1260'),
    (0x7bd00, 11, (0, 0, 1024), 'vertex stack refuses a push at count >= 1024'),
    (0x7b878, 32, (3, 2, -28044), 'mode getter: TOC slot 0x1274'),
    # SetCellType 0x82ac4(cell, type).
    (0x82b08, 32, (0, 25, 8), 'cell type word at +8'),
    (0x82b14, 42, (3, 25, 32), 'same type: placement counter +32 (signed half)'),
    (0x82b18, 14, (0, 3, 1), 'same type: counter + 1, no charge'),
    (0x82b24, 11, (0, 26, 1), 'new type 1 (path)'),
    (0x82b30, 11, (0, 0, 3), 'old type 3 (queue) turned into path flags the ride'),
    (0x82b58, 11, (0, 26, 3), 'new type 3 (queue)'),
    (0x82bb0, 32, (0, 3, 36), 'balance check only while game +36 == 0'),
    (0x82c08, 36, (26, 25, 8), 'store the new type at +8'),
    (0x82c58, 7, (0, 0, 68), '68-byte map cell record'),
    # CanChangeCellType 0x8408c(cell, new).
    (0x8408c, 11, (0, 4, 0), 'new type 0 always allowed'),
    (0x8409c, 11, (0, 4, 4), 'new type 4 ...'),
    (0x840a8, 11, (0, 0, 4), '... refused over type 4'),
    (0x840d4, 11, (0, 4, 1), 'new type 1 ...'),
    (0x840e0, 11, (0, 0, 3), '... allowed over a queue cell'),
    (0x840fc, 11, (0, 0, 0), 'same type or empty cell allowed'),
    (0x84124, 11, (0, 4, 3), 'new type 3 ...'),
    (0x8412c, 11, (0, 0, 1), '... over a path cell ...'),
    (0x84134, 32, (3, 2, -27980), '... only with the last-cell flag (TOC slot 0x12b4)'),
    # Validator 0x84154: line cost accumulation and land ownership.
    (0x843c0, 11, (0, 27, 1), 'type 1 prices with the path cost'),
    (0x843d0, 11, (0, 27, 3), 'type 3 prices with the queue cost'),
    (0x84524, 32, (0, 3, 36), 'money test only while game +36 == 0'),
    (0x8432c, 14, (4, 0, 64), 'cell flag 0x40 (unowned land) ...'),
    (0x8433c, 14, (3, 0, 1), '... refuses the cell'),
    (0x84864, 11, (0, 27, 1), 'path over ...'),
    (0x84870, 11, (0, 0, 1), '... path is allowed (returns 0)'),
    (0x84984, 11, (0, 5, 4), 'path over type 4 refused'),
    (0x8498c, 11, (0, 5, 9), 'path over type 9 refused'),
    (0x849e8, 14, (3, 0, 8), 'path over a queue end returns code 8'),
    # LayLine 0x84ea4(type, x0, y0, &x1, &y1).
    (0x84eb4, 32, (30, 2, -27980), 'last-cell flag (TOC slot 0x12b4)'),
    (0x84f48, 14, (0, 27, 1), 'cell id = 1 + x + (y << 7)'),
    (0x84f70, 7, (0, 0, 68), 'cell record stride 68'),
    (0x84f78, 14, (3, 3, 728), 'map cell array at game +728'),
    (0x84fb4, 14, (0, 0, 1), 'end cell sets the last-cell flag'),
    # Cell predicates 0x85174..
    (0x85194, 11, (0, 0, 1), 'IsPath: type == 1'),
    (0x8525c, 11, (0, 0, 0), 'IsEmpty: type == 0'),
    # Cell initialisation from the MAP attribute byte 0x85538.
    (0x855c4, 34, (31, 29, 38), 'MAP attribute byte at cell +38'),
    (0x85614, 14, (0, 0, 1), 'InitialPath -> type 1'),
    (0x8561c, 36, (0, 29, 8), 'type store'),
    (0x85634, 14, (0, 0, 32), 'InitialPath -> NOMODIFY 0x20'),
    (0x85638, 44, (0, 29, 14), 'flags store'),
    (0x85668, 24, (3, 3, 64), 'other cells get flag 0x40 (unowned)'),
    (0x8571c, 44, (0, 29, 32), 'placement counter reset'),
    # ClearCell 0x859b4(cell, a, b).
    (0x859ec, 11, (0, 0, 1), 'path cell ...'),
    (0x85a08, 40, (0, 31, 14), '... flags +14 (NOMODIFY test)'),
    (0x85a80, 10, (0, 0, 21), 'type switch bound 21'),
    (0x85aac, 14, (0, 0, -1), 'a == b == 0: counter = -1 (force removal)'),
    (0x85abc, 14, (0, 3, -1), 'otherwise counter - 1'),
    (0x85c44, 14, (4, 0, 2), 'next cardinal link: rotate by 2 bits'),
    (0x85c80, 14, (4, 0, 0), 'SetCellType(cell, 0)'),
    # Park-view hover help 0x139c64.
    (0x13a6ec, 14, (4, 0, 441), 'empty cell: help 441 "build path"'),
    (0x13a748, 14, (4, 0, 442), 'path cell: help 442 "extend this path"'),
    (0x13a780, 14, (4, 0, 444), 'queue cell: help 444 "edit this queue"'),
    # Drag preview and commit.
    (0x6f448, 14, (3, 0, 1), 'preview in mode 1'),
    (0x6f458, 14, (3, 0, 3), 'preview in mode 3'),
    (0x6f53c, 24, (3, 3, 256), 'preview type = mode | 0x100'),
    (0x710a4, 14, (3, 0, 135), 'ghost clean-up before the commit'),
    (0x710d4, 14, (3, 0, 128), 'ghost clean-up after the commit'),
    (0x71160, 14, (3, 0, 129), 'path ghost clean-up'),
    # Buy Land counts unowned cells and charges Costs.MapCell.
    (0x7ebfc, 14, (4, 0, 64), 'Buy Land counts flag 0x40 cells'),
    (0x7ec34, 32, (0, 19, 1484), 'Costs.MapCell (+1484) per counted cell'),
    # Money: balance, Spend, Earn (UI-MAP ledger offsets).
    (0xcbf48, 32, (3, 3, 12), 'balance at bank +12'),
    (0xcbfac, 32, (0, 3, -880), 'Earn posts cash-in (game + 0x1fc90)'),
    (0xcc054, 32, (0, 3, -2656), 'Spend posts total costs (game + 0x1f5a0)'),
    # HasQueue ride placement enters mode 3 (QUEUE-I context).
    (0x7495c, 32, (0, 3, 64), 'type record +64 (Info.HasQueue)'),
    (0x74974, 14, (3, 0, 3), 'SetMode(3)'),
)

CALLS = {
    0x10f2f4: 0x1c67bc,  # SetFogColor glue (anchors +1684)
    0x10f3d0: 0x7bc4c, 0x10f3d8: 0x7bc64, 0x10f3e0: 0x7bc7c, 0x10f3e8: 0x7bc94,
    0x82af4: 0x8408c, 0x82b50: 0x7bc58, 0x82b6c: 0x7bc70, 0x82b9c: 0x859b4,
    0x82bc0: 0x108424, 0x82bc4: 0xcbf48, 0x82cc8: 0xdd57c, 0x82cec: 0xcbfdc, 0x82d2c: 0xcbfdc,
    0x82a7c: 0x82ac4,  # SetCell's write branch
    0x843c8: 0x7bc58, 0x843d8: 0x7bc70, 0x844e0: 0x8408c, 0x8451c: 0x7bcb8, 0x84544: 0xcbf48,
    0x84330: 0x84a74, 0x80cd4: 0x84154,
    0x84f7c: 0x80c50, 0x85008: 0x80c50, 0x850a0: 0x80c50, 0x85130: 0x80c50,
    0x85630: 0x82d6c, 0x85a2c: 0x6e110, 0x85a40: 0x5fbcc, 0x85b14: 0xd8b64, 0x85c3c: 0xdd57c,
    0x85c48: 0xd7d7c, 0x85c90: 0x82ac4, 0x85dd4: 0x7bc70, 0x85df8: 0xcbf50, 0x85dcc: 0xe2424,
    0x13a6c4: 0x85258,
    0x6f44c: 0x7b890, 0x6f45c: 0x7b890, 0x6f534: 0x7b878, 0x6f54c: 0x84ea4,
    0x71090: 0x7bcf4, 0x710a8: 0x84ea4, 0x710ac: 0x7b878, 0x710c0: 0x84ea4, 0x71188: 0xdd57c,
    0x7129c: 0x7b320, 0x7ec24: 0x84a74, 0x7497c: 0x7b320,
}

# TOC slot (data offset) -> relocated data offset of the global it points at.
TOC_SLOTS = {
    0x1264: 0x84ad4,  # path cost (Costs.PathCell)
    0x1260: 0x84ad0,  # queue cost (Costs.QueueCell)
    0x125c: 0x84acc,  # kart track cost
    0x1258: 0x84ac8,  # water track cost
    0x1284: 0x84ac4,  # pending line cost (validator accumulator)
    0x1274: 0x84b04,  # current tool mode
    0x12b4: 0x84b2c,  # last-cell flag
    0x12b0: 0x84b34,  # "ended on path/queue" flag
    0x121c: 0x84adc,  # conversion-in-progress flag (path -> queue)
    0x2dc4: 0x84ab8,  # vertex stack count
    0xa7c: 0x54860,   # level balance record read by the cost loader
}

# (data offset of a relocated jump table, entry count, {entry index: code target}).
JUMP_TABLES = (
    (0x3e530, 22, {0: 0x8600c, 1: 0x85a9c, 3: 0x85ca0, 4: 0x85fcc, 9: 0x85ca0, 10: 0x85a9c, 21: 0x85fbc}),
    (0x47b7c, 22, {0: 0x13a894, 1: 0x13a730, 3: 0x13a768, 4: 0x13a7a0, 9: 0x13a7a0, 10: 0x13a7a0, 21: 0x13a858}),
)

SCHEMA_EXPECTED = {
    'Costs.QueueCell': 1468, 'Costs.PathCell': 1472, 'Costs.KartTrackCell': 1476,
    'Costs.WaterTrackCell': 1480, 'Costs.MapCell': 1484, 'ThemeEngine.FogColour': 1684,
}

DIAGNOSTICS = {  # string base + offset -> leading text, checked as text, not as code
    (0x1ca631, 67): b'Removing path cell with no neighbours but NOMODIFY set',
}

# Negative witnesses: (start, end, forbidden call target, meaning). Bounded to the range.
NO_CALLS = (
    (0x85a9c, 0x85ca0, 0xcbf50, 'path removal case earns nothing (no refund)'),
    (0x84ea4, 0x85174, 0xcbfdc, 'LayLine spends nothing itself'),
)

PC_HELP = {
    441: 'Left-click to build path',
    442: 'Left-click to extend this path',
    443: 'Left-click to build the path, BACKSPACE to undo\nClick onto existing path to complete it',
    444: 'Left-click to edit this queue',
    445: 'Left-click to build the queue, click again to stop building\nClick onto path to connect the queue to it',
}


# ---------------------------------------------------------------- pure restatements

def can_change_cell_type(current: int, new: int, last_cell: bool) -> bool:
    """0x8408c: may a cell of type `current` take type `new`?"""
    if new == 0:
        return True
    if new == 4 and current == 4:
        return False
    if new == 21 and current == 21:
        return True
    if new == CELL_PATH and current == CELL_QUEUE:
        return True
    if new == current or current == CELL_EMPTY:
        return True
    if new == 4 and current == CELL_PATH:
        return True
    return new == CELL_QUEUE and current == CELL_PATH and last_cell


def set_cell_type(current: int, new: int, counter: int, balance: int, path_cost: int, queue_cost: int,
                  free_build: bool = False, economy_off: bool = False, last_cell: bool = False) -> dict:
    """0x82ac4: the write that charges. Returns the outcome; nothing is clamped or rounded.

    Same nonzero type: counter + 1, no charge. Path/queue cost a cell each, checked against the
    balance (signed, balance >= cost) unless game +36 is set; Spend posts it afterwards.
    """
    if not can_change_cell_type(current, new, last_cell):
        return {'ok': False, 'type': current, 'counter': counter, 'charged': 0}
    if new != 0 and current == new:
        return {'ok': True, 'type': current, 'counter': counter + 1, 'charged': 0}
    cost = 0
    if not free_build:
        cost = path_cost if new == CELL_PATH else queue_cost if new == CELL_QUEUE else 0
    affordable = economy_off or balance - cost >= 0
    if new in (CELL_PATH, CELL_QUEUE) and not affordable:
        return {'ok': False, 'type': current, 'counter': counter, 'charged': 0}
    return {'ok': True, 'type': new, 'counter': counter, 'charged': cost,
            'queue_to_path': new == CELL_PATH and current == CELL_QUEUE,
            'path_to_queue': new == CELL_QUEUE and current == CELL_PATH}


def snap_end(start: tuple[int, int], end: tuple[int, int]) -> tuple[int, int]:
    """0x6f4b0..0x6f528 / 0x70fe4..0x7105c: keep the dominant axis; a tie keeps X."""
    (sx, sy), (ex, ey) = start, end
    if abs(ex - sx) >= abs(ey - sy):
        ey = sy
    if abs(ex - sx) <= abs(ey - sy):
        ex = sx
    return ex, ey


def lay_line(start: tuple[int, int], end: tuple[int, int]) -> list[tuple[int, int, int, int, bool]]:
    """0x84ea4: cells written for one segment, as (x, y, dx, dy, last_cell).

    Walks only the dominant axis (|dx| > |dy| -> X at row y0, else Y at column x0); the other
    end coordinate is ignored. Every cell before the end goes through SetCell with the step
    direction; the end cell is written with the last-cell flag raised. The caller stops at the
    first refused cell (LayLine returns 0 there).
    """
    (x0, y0), (x1, y1) = start, end
    cells = []
    if abs(x1 - x0) > abs(y1 - y0):
        step = -1 if x0 > x1 else 1
        for x in range(x0, x1, step):
            cells.append((x, y0, step, 0, False))
        cells.append((x1, y0, step, 0, True))
    else:
        step = -1 if y0 > y1 else 1
        for y in range(y0, y1, step):
            cells.append((x0, y, 0, step, False))
        cells.append((x0, y1, 0, step, True))
    return cells


def cell_id(x: int, y: int) -> int:
    """1 + x + (y << 7); id 0 means no cell."""
    return 1 + x + (y << 7)


def remove_path(counter: int, nomodify: bool, has_neighbours: bool, a: int = 0, b: int = 0,
                converting: bool = False) -> dict:
    """0x859b4 for a type 1/10 cell: NOMODIFY and the placement counter decide; no refund."""
    if nomodify and not has_neighbours:
        nomodify = False  # "Removing path cell with no neighbours but NOMODIFY set": flag cleared
    if nomodify and not converting:
        return {'removed': False, 'counter': counter, 'refund': 0}
    counter = -1 if (a == 0 and b == 0) else counter - 1
    if counter >= 0 and not converting:
        return {'removed': False, 'counter': counter, 'refund': 0}
    return {'removed': True, 'counter': 0, 'refund': 0}


CARDINAL_LINKS = (1, 4, 16, 64)  # 0x85c44: rotate left by 2 bits, starting at 1


def next_link(link: int) -> int:
    return ((link << 2) | (link >> 6)) & 0xff


def corner(connections: int) -> int:
    """0x85310: the connection byte when exactly two perpendicular cardinal links are set, else 0."""
    if bin(connections & 0xff).count('1') != 2:  # 0x6e1c0 count (assumed popcount, medium)
        return 0
    for a, b in ((1, 4), (1, 64), (16, 4), (16, 64)):
        if connections & a and connections & b:
            return connections
    return 0


def initial_cell(attribute: int) -> tuple[int, int]:
    """0x85538, branch where 0x4d5d0(x, y) is nonzero: (type, flags) from the MAP attribute byte.

    Bounded: the blocked/water/walkway type stores in this branch are overwritten (type 0) unless
    the InitialPath bit is set, as decoded; the other branch (types 2/30/7) is not restated.
    """
    if attribute & 0x08:
        flags, kind = FLAG_NOMODIFY, CELL_PATH
    else:
        flags, kind = FLAG_UNOWNED, CELL_EMPTY
    if attribute & 0x10:
        flags |= 0x80
    if attribute & 0x04:
        flags |= 0x400
    return kind, flags


def validate_path_cell(current: int, flags: int) -> int:
    """Bounded 0x84154 restatement for type 1: 0 allowed, 1 refused, 8 queue end (meaning open)."""
    if flags & FLAG_UNOWNED:
        return 1
    if current in (CELL_EMPTY, CELL_PATH):
        return 0
    if current in (4, 9):
        return 1
    if current == CELL_QUEUE:
        return 8
    return 1


def line_affordable(cells_to_buy: int, unit_cost: int, balance: int, economy_off: bool = False) -> bool:
    """Validator money test: the pending total must not exceed the balance (refuses when total > balance)."""
    return economy_off or cells_to_buy * unit_cost <= balance


def queue_cell_refund(queue_cost: int, scrap_percent: int) -> int:
    """0x85dcc..0x85dec: Earn(QueueCost * percent / 100), unsigned (QUEUE-I context)."""
    return ((queue_cost * scrap_percent) & 0xffffffff) // 100


# ---------------------------------------------------------------- binary witness

def rlwinm_fields(c, at: int) -> tuple[int, int, int, int, int]:
    word = pef._u32(c.code.data, at)
    require(word >> 26, 21, f'rlwinm at code:{at:#x}')
    return (word >> 21 & 31, word >> 16 & 31, word >> 11 & 31, word >> 6 & 31, word >> 1 & 31)


def x_compare(c, at: int) -> tuple[str, int, int, int]:
    word = pef._u32(c.code.data, at)
    require(word >> 26, 31, f'X-form compare at code:{at:#x}')
    kind = {0: 'cmpw', 32: 'cmplw'}.get(word >> 1 & 0x3ff)
    require(kind is not None, True, f'compare kind at code:{at:#x}')
    return (kind, word >> 23 & 7, word >> 16 & 31, word >> 11 & 31)


def calls_in(c, start: int, end: int) -> set[int]:
    targets = set()
    for at in range(start, end, 4):
        word = pef._u32(c.code.data, at)
        if word >> 26 == 18 and word & 1:
            targets.add(call_target(c, at))
    return targets


def check_negatives(c) -> list[dict]:
    negatives = []
    for start, end, target, meaning in NO_CALLS:
        require(target in calls_in(c, start, end), False, meaning)
        negatives.append({'range': [start, end], 'absent_call': target, 'meaning': meaning})
    return negatives


def check_fields(c) -> int:
    for at, op, expected, meaning in FIELDS:
        require(d_fields(c, at, op), expected, f'{meaning} at code:{at:#x}')
    return len(FIELDS)


def check_relocations(c) -> dict:
    relocs = c.relocs.get(c.data_section.index, {})
    out = {}
    for slot, target in TOC_SLOTS.items():
        entry = relocs.get(slot)
        require(entry is not None and entry.kind == 'section' and entry.target == c.data_section.index, True,
                f'TOC slot {slot:#x} relocation')
        require(entry.addend, target, f'TOC slot {slot:#x} target')
        out[hex(slot)] = hex(target)
    for base, count, expected in JUMP_TABLES:
        for index, target in expected.items():
            entry = relocs.get(base + 4 * index)
            require(entry is not None and entry.kind == 'section' and entry.target == c.code.index, True,
                    f'jump table {base:#x}[{index}] relocation')
            require(entry.addend, target, f'jump table {base:#x}[{index}]')
        require(index < count, True, 'jump table bound')
    return out


def check_code(c) -> dict:
    blocks = {}
    for name, (start, end, expected) in BLOCKS.items():
        digest = hashlib.sha256(c.code.data[start:end]).hexdigest()
        require(digest, expected, f'{name} bounded code digest')
        blocks[name] = {'code_start': start, 'code_end_exclusive': end, 'sha256': digest}
    check_fields(c)
    for at, target in CALLS.items():
        require(call_target(c, at), target, f'call at code:{at:#x}')
    # Masks: NOMODIFY test 0x20, its clear, InitialPath 0x08, LayLine row shift y << 7.
    require(rlwinm_fields(c, 0x85a0c), (0, 0, 0, 26, 26), 'NOMODIFY mask 0x20')
    require(rlwinm_fields(c, 0x85a48), (0, 0, 0, 27, 25), 'NOMODIFY clear (~0x20)')
    require(rlwinm_fields(c, 0x8560c), (31, 0, 0, 28, 28), 'MAP InitialPath mask 0x08')
    require(rlwinm_fields(c, 0x84f28), (24, 26, 7, 0, 24), 'row offset y << 7')
    # LayLine picks the X walk only when |dx| > |dy|; the validator compares total with balance.
    require(x_compare(c, 0x84f08), ('cmpw', 0, 3, 26), 'LayLine |dx| vs |dy|')
    require(x_compare(c, 0x84550), ('cmpw', 0, 3, 18), 'pending cost vs balance')
    # The commit passes the mode unmodified: nothing between the getter and LayLine but argument loads.
    for at in range(0x710b0, 0x710c0, 4):
        require(pef._u32(c.code.data, at) >> 26 in (32, 14), True, f'commit argument load at code:{at:#x}')
    negatives = check_negatives(c)
    diagnostics = {}
    for (base, offset), text in DIAGNOSTICS.items():
        at = base + offset
        require(bytes(c.code.data[at:at + len(text)]), text, f'diagnostic text at code:{at:#x}')
        diagnostics[f'{at:#x}'] = text.decode('ascii')
    return {'blocks': blocks, 'diagnostics': diagnostics, 'negative_witnesses': negatives,
            'toc_slots': check_relocations(c), 'field_witnesses': len(FIELDS), 'call_witnesses': len(CALLS)}


def schema_offsets(data: bytes) -> dict:
    selected = {row['path']: row['runtime_offset'] for row in schema_fields(data, 0x34d10)
                if row['path'] in SCHEMA_EXPECTED}
    require(selected, SCHEMA_EXPECTED, 'main SAM schema runtime offsets')
    return selected


def inspect(bin_root: Path) -> dict:
    c = load_identified(bin_root / 'SimThemePark.data')
    require((c.code.index, c.data_section.index), (0, 1), 'section indices')
    report = check_code(c)
    report['schema_offsets'] = schema_offsets(bytes(c.data_section.data))
    report['binary_sha256'] = hashlib.sha256(c.raw).hexdigest()
    report['witness_source'] = Path(__file__).resolve().relative_to(REPO).as_posix()
    report['plan'] = 'docs/reverse/PATH-plan.md'
    report['limitation'] = ('Static, bounded: no original execution; the click that enters mode 1, BACKSPACE '
                            'undo, the connection writer 0x82d6c and the player bulldozer route are not traced.')
    return report


# ---------------------------------------------------------------- PC data (optional)

SAM_LINE = re.compile(r'^\s*([A-Za-z][\w.\[\]]*)\s+("[^"]*"|\S+)', re.M)


def sam_values(text: str) -> dict[str, str]:
    return {key: value for key, value in SAM_LINE.findall(text)}


def read_bfst(table: bytes, characters: list[str]) -> list[str]:
    """BFST string table (OpenTPW BFSTReader): 1-based indices into the BFMU character list."""
    require(table[:4], b'BFST', 'string table magic')
    count = struct.unpack_from('<i', table, 8)[0]
    require(0 <= count <= (len(table) - 12) // 4, True, 'string count')
    out = []
    for index in range(count):
        at = 12 + struct.unpack_from('<i', table, 12 + 4 * index)[0]
        require(table[at], 1, 'string marker')
        length = table[at + 1] | table[at + 2] << 8 | table[at + 3] << 16
        out.append(''.join(characters[b - 1] for b in table[at + 4:at + 4 + length]))
    return out


def read_bfmu(table: bytes) -> list[str]:
    require(table[:4], b'BFMU', 'character table magic')
    count = struct.unpack_from('<H', table, 6)[0]
    return [chr(struct.unpack_from('<H', table, 8 + 2 * i)[0]) for i in range(count)]


def pc_facts(pc_data: Path) -> dict:
    standard = sam_values((pc_data / 'levels' / 'Standard.sam').read_text(encoding='latin-1'))
    english = pc_data / 'Language' / 'English'
    characters = read_bfmu((english / 'MBToUni.dat').read_bytes())
    help_text = read_bfst((english / 'UIHELPTEXT.str').read_bytes(), characters)
    return {'costs': {key: int(standard[key]) for key in ('Costs.PathCell', 'Costs.QueueCell', 'Costs.MapCell')},
            'help': {index: help_text[index] for index in PC_HELP}}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('bin_root', type=Path)
    parser.add_argument('--pc-data', type=Path)
    args = parser.parse_args()
    try:
        result = inspect(args.bin_root)
        if args.pc_data:
            result['pc'] = pc_facts(args.pc_data)
    except (OSError, KeyError, ValueError, pef.PEFError) as error:
        parser.exit(1, f'path evidence: {error}\n')
    print(json.dumps(result, sort_keys=True, indent=2, default=str))


if __name__ == '__main__':
    main()

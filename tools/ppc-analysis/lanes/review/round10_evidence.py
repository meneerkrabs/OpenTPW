"""Round-10 review witnesses: clock saved-script graph (21bd7ef), rides topology gates (0973fbe),
UI catalog event delivery (917eb36).

Usage: python3 -I round10_evidence.py --bin-root /path/to/mac-feral/bin [--pc-save Easymode.TPWI]

--bin-root pins SimThemePark.data and re-decodes the operands each lane cites, plus the review's
own additions (head clear, unchecked PAD_/width words, the +212/+230/+176 rewrites, the rides
stream-order prerequisite, the UI queue drop and unchecked row-ID status). --pc-save walks the
identified PC fixture's script-manager block with the standard-library zlib and struct modules only
(no lane code). Output is addresses, decoded operand fields, counts, offsets and conclusions.
Nothing original is executed, and no original bytes are stored.
"""
from __future__ import annotations

import argparse
import json
import struct
import sys
import zlib
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import review_evidence as rv  # noqa: E402
import round9_evidence as r9  # noqa: E402
from review_evidence import Binary, ReviewError, require  # noqa: E402

LBFILE_READ, LBFILE_WRITE = 0x1c4b24, 0x1c4b54
MANAGER_SLOT = -27616            # lwz rX,-27616(r2): the global script manager
MANAGER_OFFSET = 1595542         # lane constant; re-checked here by the RSSE magic and the walk
SCRIPT_IDS = [15, 14, 13, 12, 11, 10, 9, 8, 7, 6, 4, 3, 2, 1]


def expect(app: Binary, at: int, *fields):
    return r9.expect(app, at, *fields)


def range_words(app: Binary, start: int, end: int):
    for at in range(start, end, 4):
        yield at, app.word(at)


# --- clock: saved-script reader/writer (21bd7ef) -----------------------------------------------

def manager_loads(app: Binary) -> list[int]:
    pattern = 32 << 26 | 2 << 16 | (MANAGER_SLOT & 0xFFFF)
    return [at for at in range(0, len(app.code), 4) if app.word(at) & 0xFC1FFFFF == pattern]


def first_blr_extent(app: Binary, start: int, limit: int = 0x4000) -> int:
    for at in range(start, start + limit, 4):
        if app.word(at) == 0x4E800020:
            return at + 4
    raise ReviewError(f'no blr within {limit:#x} of {start:#x}')


def saved_script(app: Binary) -> dict:
    reader, reader_end = 0xb4818, 0xb56a0
    # Only one caller; the reader itself loads the manager once.
    callers = [at for at in range(0, len(app.code), 4)
               if rv.decode(app.word(at), at) == ('bl', reader)]
    require(callers, [0x11bea8], 'reader callers')
    expect(app, 0xb4834, 'lwz', 26, 2, MANAGER_SLOT)
    # Header: magic RSSE (0x45535352 after the LE swap), then a declared size read straight into
    # the manager (r5 = r26, r6 = wire size). The size is not compared with 20.
    expect(app, 0xb48d8, 'addis', 0, 3, -17747)
    expect(app, 0xb48dc, 'cmpli', 0, 0, 0x5352)
    expect(app, 0xb4910, 'lwz', 6, 1, 384)
    expect(app, 0xb4918, 'addi', 5, 26, 0)
    require(app.call(0xb4924), LBFILE_READ, 'header read')
    # Count restored by stwbrx into +12, head restored then overwritten with r0 = 0.
    expect(app, 0xb4978, 'addi', 8, 26, 12)
    expect(app, 0xb4980, 'x31', 9, 0, 8, 662, 0)
    expect(app, 0xb4934, 'addi', 0, 0, 0)
    expect(app, 0xb49a8, 'stw', 8, 26, 16)
    expect(app, 0xb49ac, 'stw', 0, 26, 16)
    # Five 4-byte reads after the header have no compare between them (PAD_ is unchecked).
    pads = [at for at, w in range_words(app, 0xb49b0, 0xb4ad0) if rv.decode(w, at) == ('bl', LBFILE_READ)]
    compares = [at for at, w in range_words(app, 0xb49b0, 0xb4ad0)
                if rv.decode(w, at)[0] in ('cmpi', 'cmpli') or (w >> 26 == 31 and (w >> 1 & 0x3FF) in (0, 32))]
    require((len(pads), compares), (7, []), 'five PAD_ reads plus count and width, none compared')
    # Fixed record read length is the saved width word (372(r1)), not the 244 allocation.
    expect(app, 0xb4aec, 'addi', 3, 0, 244)
    expect(app, 0xb4b54, 'lwz', 6, 1, 372)
    require(app.call(0xb4b58), LBFILE_READ, 'fixed record read')
    # Count +12 increments per inserted script; insertion at head +16.
    expect(app, 0xb4938, 'addi', 25, 26, 12)
    expect(app, 0xb4b0c, 'lwz', 3, 25, 0)
    expect(app, 0xb4b14, 'addi', 3, 3, 1)
    expect(app, 0xb4b18, 'stw', 3, 25, 0)
    expect(app, 0xb4940, 'addi', 27, 26, 16)
    expect(app, 0xb4b38, 'stw', 31, 27, 0)
    expect(app, 0xb4b48, 'lwz', 20, 31, 0)
    expect(app, 0xb4b50, 'lwz', 21, 31, 4)
    expect(app, 0xb4d80, 'stw', 20, 31, 0)
    expect(app, 0xb4d90, 'stw', 21, 31, 4)
    # Outer loop bound is the wire record count (380(r1)), not the restored header count.
    expect(app, 0xb5654, 'lwz', 0, 1, 380)
    # After the name block the reader rewrites three fields the graph witness does not expose.
    expect(app, 0xb53b8, 'lwz', 0, 31, 212)
    require(app.call(0xb54b8), 0xbc0e0, '+212 recreated')
    expect(app, 0xb54bc, 'stw', 3, 31, 212)
    expect(app, 0xb54c0, 'addi', 3, 0, -1)
    require(rv.decode(app.word(0xb54c8), 0xb54c8)[0], '?', 'sth form')
    require(app.word(0xb54c8), 0xB07F00E6, 'sth r3,230(r31)')
    expect(app, 0xb54d8, 'stw', 0, 31, 176)
    # Writer: header word +12 is written as-is; the record count is a separate list traversal.
    expect(app, 0xb3930, 'lwz', 0, 21, 12)
    expect(app, 0xb3b28, 'lwz', 4, 21, 16)
    expect(app, 0xb3b34, 'addi', 0, 3, 1)
    expect(app, 0xb3b3c, 'lwz', 4, 4, 0)
    # Direct callees after the count restore (0xb4980): none loads the manager global within its
    # first-blr extent. (0xb23a0 free and 0xb2760 init do, but run before the restore.)
    require([hex(rv.decode(app.word(a), a)[1]) for a in (0xb486c, 0xb48a4)], ['0xb23a0', '0xb2760'], 'pre-restore calls')
    callees = sorted({rv.decode(w, at)[1] for at, w in range_words(app, 0xb4984, reader_end)
                      if rv.decode(w, at)[0] == 'bl'} - set(range(reader, reader_end)))
    sites = manager_loads(app)
    hits = [(hex(c), hex(s)) for c in callees for s in sites if c <= s < first_blr_extent(app, c)]
    require(hits, [], 'direct reader callees loading the manager')
    return {
        'reader': hex(reader), 'caller': '0x11bea8', 'manager_load_sites': len(sites),
        'direct_callees': [hex(c) for c in callees], 'callee_manager_loads': hits,
        'header': 'RSSE magic checked; declared header size read into the manager unchecked',
        'head_cleared_after_restore': '0xb49ac stw r0(=0),16(r26)',
        'count': 'restored at 0xb4980, +1 per insertion at 0xb4b0c..0xb4b18; no reset in the reader or '
                 'in any direct callee (first-blr extent); deeper callees and post-load code untraced',
        'native_trusts': ['PAD_ words (no compare)', 'header size (read length)', 'fixed width (read length into 244-byte alloc)'],
        'rewritten_on_load': {'+212': 'recreated via 0x18d100/0xbc0e0 when nonzero', '+230': 'set to 0xffff',
                              '+176': 'cleared, then rebuilt by the OBJ list'},
        'conclusion': 'head insertion, token discard, ID lookup and count projection hold; the graph '
                      'witness leaves +212/+230 unmodelled and its PAD_/size guards are stricter than native',
    }


def script_walk(payload: bytes, offset: int = MANAGER_OFFSET) -> dict:
    """Independent framing walk in native read order (stdlib only)."""
    require(payload[offset:offset + 4], b'RSSE', 'manager magic')
    size = struct.unpack_from('<I', payload, offset + 4)[0]
    require(size, 20, 'header size')
    init, phase, next_id, count, head = struct.unpack_from('<5I', payload, offset + 8)
    at = offset + 8 + size
    require(payload[at:at + 20], b'PAD_' * 5, 'pad words')
    records, width = struct.unpack_from('<2I', payload, at + 20)
    require(width, 244, 'fixed width')
    at += 28
    ids, bindings, deadlines = [], [], {}
    for _ in range(records):
        fixed = at
        at += width
        ids.append(struct.unpack_from('<I', payload, fixed + 8)[0])
        for block in range(9):                      # code, label, variable, literal, md8, md16, md32, aux, name
            declared = struct.unpack_from('<I', payload, at)[0]
            at += 4 + (declared * 32 if block == 6 else declared)
            require(at <= len(payload), True, 'block inside payload')
        require(payload[at:at + 4], b'OBJ ', 'object marker')
        objects, object_width = struct.unpack_from('<2I', payload, at + 4)
        require(object_width, 28, 'object width')
        at += 12 + objects * object_width
        bindings.append(objects)
        wait, anim, timer = (struct.unpack_from('<I', payload, fixed + o)[0] for o in (160, 164, 196))
        if wait or anim or timer:
            deadlines[ids[-1]] = [wait, anim, timer]
    return {'header': {'initialized': init, 'pass': phase, 'next_id': next_id, 'count': count,
                       'head_token_nonzero': head != 0},
            'records': records, 'ids': ids, 'end': at, 'next_magic': payload[at:at + 4].decode('ascii'),
            'object_bindings': sum(bindings), 'nonzero_deadlines': deadlines,
            'projected_count_after_load': (count + records) & 0xFFFFFFFF,
            'matches_lane': ids == SCRIPT_IDS and at == 1606398}


# --- rides: topology gates (0973fbe) ------------------------------------------------------------

def topology_order(app: Binary) -> dict:
    # Saver, per controller: header count loop excludes flag 0x10; then ONE list walk in which each
    # node first gets the ordinal-2 auxiliary check, then the flag-0x10 filter, then record+links.
    expect(app, 0x39720, 'lwz', 3, 27, 84)
    expect(app, 0x39744, 'lwz', 3, 3, 100)
    expect(app, 0x39980, 'lwz', 31, 27, 84)
    expect(app, 0x39988, 'lwz', 0, 31, 0)
    expect(app, 0x3998c, 'cmpli', 0, 0, 2)
    expect(app, 0x39994, 'lwz', 0, 31, 176)            # aux count is the node's own link count
    expect(app, 0x39a08, 'lwz', 6, 29, 180)            # items are linked sections' ordinals
    expect(app, 0x39a18, 'lwz', 0, 6, 0)
    require(app.call(0x399d0), LBFILE_WRITE, 'aux count write')
    expect(app, 0x39a8c, 'lwz', 0, 3, 4)
    require(app.word(0x39a90), 0x540006F7, 'rlwinm. flag 0x10')
    expect(app, 0x39d70, 'lwz', 31, 31, 100)
    # Loader: initial topology, aux read into the THIRD list node, then the record loop.
    require(app.call(0x3a2d4), 0x366bc, 'initial topology')
    expect(app, 0x3a344, 'lwz', 5, 28, 84)
    expect(app, 0x3a354, 'lwz', 5, 5, 100)
    expect(app, 0x3a35c, 'lwz', 0, 5, 100)
    expect(app, 0x3a368, 'addi', 5, 5, 176)
    require(app.call(0x3a36c), LBFILE_READ, 'aux count read')
    expect(app, 0x3a440, 'addi', 6, 0, 34)
    require(app.call(0x3a444), LBFILE_READ, 'first section record read follows aux')
    # Link restore walks from controller+60+24 (= +84) by ordinal: ordinal == list position.
    expect(app, 0x3a8e8, 'addi', 3, 28, 60)
    expect(app, 0x41dd0, 'lwz', 3, 3, 24)
    expect(app, 0x41de8, 'lwz', 3, 3, 100)
    return {'saver_node_order': ['ordinal==2 aux', 'flag 0x10 filter', '34-byte record + links'],
            'loader_order': ['controller32', 'initial topology 0x366bc', 'aux into third node', 'records'],
            'third_node_load': 'controller+84 -> +100 -> +100, count into +176, items into +180 slots',
            'link_resolution': '0x41dcc walks controller+84 by ordinal (ordinal equals list position)',
            'prerequisite': 'the streams align only if every node before the ordinal-2 node is flag-0x10 '
                            'filtered (or ordinal 2 heads the list) and exactly one ordinal-2 node exists; '
                            'otherwise the saver emits records before the aux block the loader reads first',
            'conclusion': '0973fbe claims verified; the cross-node stream-order prerequisite is implicit in its '
                          '"initial-node/filter invariants" and should be stated before any decoder'}


# --- UI: catalog event delivery (917eb36) -------------------------------------------------------

def catalog_delivery(app: Binary) -> dict:
    # Queued poster 0x170ec8 -> 0x16fd80; ring slot = base + 16 * index(+12). An occupied slot drops.
    require(app.call(0x170f78), 0x16fd80, 'queued post')
    expect(app, 0x16fd80, 'lwz', 0, 3, 12)
    expect(app, 0x16fd90, 'lwz', 0, 8, 0)
    expect(app, 0x16fd94, 'cmpli', 0, 0, 0)
    require(rv.decode(app.word(0x16fd98), 0x16fd98), ('bc', 4, 2, 0x16fe14), 'occupied slot skips the write')
    expect(app, 0x16fe14, 'addi', 3, 0, 0)
    require(app.word(0x16fdf4), 0x3404FFFF, 'addic. r0,r4,-1: write index counts down')
    expect(app, 0x16fdf8, 'stw', 0, 3, 12)
    # Both posters require a live target (byte 0 == 0); synchronous returns -1 otherwise.
    expect(app, 0x170f64, 'lbz', 0, 4, 0)
    expect(app, 0x170fc8, 'lbz', 0, 28, 0)
    expect(app, 0x171014, 'addi', 3, 0, -1)
    require(app.call(0x17100c), 0x181398, 'synchronous dispatch')
    # Row-ID accessor 0x17991c: fails (r3 = 0, out slot untouched) for ordinal < 0, >= +336 or null +324.
    expect(app, 0x179950, 'lwz', 0, 28, 336)
    expect(app, 0x17995c, 'lwz', 0, 28, 324)
    expect(app, 0x1799ac, 'lwz', 0, 3, 4)
    expect(app, 0x1799b0, 'stw', 0, 30, 0)
    expect(app, 0x1799bc, 'addi', 3, 0, 0)
    # Both root call sites read the out slot without testing r3.
    require(app.call(0x163994), 0x17991c, 'activation accessor')
    expect(app, 0x163998, 'lwz', 0, 1, 144)
    require(app.call(0x163d70), 0x17991c, 'selection accessor')
    expect(app, 0x163d74, 'lwz', 0, 1, 136)
    # Sort selector: negated only when direction word +362 equals exactly 1.
    expect(app, 0x17a4c0, 'cmpi', 0, 0, 1)
    expect(app, 0x17a4c4, 'addi', 6, 3, 1)
    return {'queued': 'ring of 16-byte records, index +12 counts down; occupied slot -> not written, r3 = 0',
            'live_target_gate': 'byte 0 of the target must be 0 for both queued and synchronous delivery',
            'row_id_status_untested': ['0x163998', '0x163d74'],
            'sort_sign': 'negative iff +362 == 1',
            'conclusion': '917eb36 claims verified; queue overflow drops events and the root reads an '
                          'unwritten slot if the accessor fails; neither is modelled or claimed'}


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--bin-root', type=Path, required=True)
    parser.add_argument('--pc-save', type=Path)
    args = parser.parse_args()
    try:
        app = Binary(args.bin_root, 'SimThemePark.data', 'SimThemePark.data', rv.APP_TOC)
        result = {'saved_script': saved_script(app), 'topology_order': topology_order(app),
                  'catalog_delivery': catalog_delivery(app)}
        if args.pc_save:
            result['script_walk'] = script_walk(r9.pc_payload(args.pc_save))
    except (OSError, ReviewError, zlib.error, struct.error) as error:
        parser.exit(1, f'round 10 evidence: {error}\n')
    print(json.dumps(result, indent=2, sort_keys=True))


if __name__ == '__main__':
    main()

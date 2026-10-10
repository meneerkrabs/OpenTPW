"""Round-9 review witnesses: formats channel clock and copy-back gate, economy scientist tail,
rides coaster save boundary and byte-12 ordinal, TPI shifted container header.

Usage: python3 -I round9_evidence.py --bin-root /path/to/mac-feral/bin
           [--pc-save Easymode.TPWI] [--tpi-save prebuilt.TPWS] [--git /path/to/OpenTPW]

--bin-root pins SimThemePark.data and re-decodes the operands each lane cites. --pc-save walks
the identified PC fixture with the standard-library zlib module only (no OpenTPW code) for the
scientist prefix and the EMAK..SAOC coaster body. --tpi-save reads the supplied TPI save's header
at the legacy fixed offsets and at the 24-byte shifted offsets. --git reports which committed
refs carry the corrected rides capacity wording. Output is addresses, decoded operand fields,
counts, offsets, hashes and conclusions. Nothing original is executed, and no bytes are stored.
"""
from __future__ import annotations

import argparse
import hashlib
import json
import struct
import subprocess
import sys
import zlib
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import review_evidence as rv  # noqa: E402
from review_evidence import Binary, ReviewError, require  # noqa: E402

PC_SAVE_SHA = '6d89303d098900364bf5e80b236b64bd85976fb947e9e4609d088547f430b39a'
PC_PAYLOAD_SHA = 'a3c9a28252c37ad49a8eb78e4a0c5e1d5229d01548fa35801db67015d2589173'
TPI_SAVE_SHA = '05ea01f2151757b3f4bbe7c15a1cb702af9f53b959c533c5e346d970695d882d'


def decode(word: int, at: int) -> tuple:
    """review_evidence.decode plus the single-precision A-form (op 59) and ori/oris."""
    op = word >> 26
    rt, ra, rb = word >> 21 & 31, word >> 16 & 31, word >> 11 & 31
    if op == 59:
        return ('a59', rt, ra, rb, word >> 6 & 31, word >> 1 & 31)
    if op in (24, 25):
        return ('ori' if op == 24 else 'oris', rt, ra, word & 0xFFFF)
    if op == 63:
        return ('x63', rt, ra, rb, word >> 1 & 0x3FF)
    return rv.decode(word, at)


def expect(app: Binary, at: int, *fields):
    return require(decode(app.word(at), at), fields, f'{app.name} code:{at:#x}')


def toc_float(app: Binary, at: int, reg: int) -> float:
    op, rt, ra, d = decode(app.word(at), at)
    require((op, rt, ra), ('lfs', reg, 2), f'TOC float load at {at:#x}')
    require(app.toc + d in app.rel, False, f'constant {app.toc + d:#x} relocation')
    return struct.unpack_from('>f', app.data, app.toc + d)[0]


# --- formats: channel clock (c546a24) -----------------------------------------------------------

def channel_clock(app: Binary) -> dict:
    # 0xa6484 GetFrame: elapsed = (float)(u32)(now(+20) - start(+16)); frame = speed(+12) * (30*elapsed/1000).
    expect(app, 0xa649c, 'lwz', 5, 3, 16)
    expect(app, 0xa64a4, 'lwz', 4, 3, 20)
    expect(app, 0xa64ac, 'x31', 4, 5, 4, 40, 0)            # subf r4,r5,r4
    expect(app, 0xa64a0, 'addis', 0, 0, 0x4330)
    op, rt, ra, d = decode(app.word(0xa64a8), 0xa64a8)
    require((op, rt, ra), ('lfd', 2, 2), 'conversion bias load')
    require(app.data[app.toc + d:app.toc + d + 8], (0x4330 << 48).to_bytes(8, 'big'), 'unsigned 2^52 bias')
    thirty, thousand = toc_float(app, 0xa64b0, 3), toc_float(app, 0xa64b8, 0)
    require((thirty, thousand), (30.0, 1000.0), 'frame constants')
    expect(app, 0xa64c0, 'lfs', 4, 3, 12)
    expect(app, 0xa64c8, 'a59', 1, 1, 2, 0, 20)             # fsubs: single rounding of the elapsed ms
    expect(app, 0xa64cc, 'a59', 1, 3, 0, 1, 25)             # fmuls 30*elapsed
    expect(app, 0xa64d0, 'a59', 0, 1, 0, 0, 18)             # fdivs /1000
    expect(app, 0xa64d4, 'a59', 0, 4, 0, 0, 25)             # fmuls by speed last
    expect(app, 0xa64d8, 'stfs', 0, 3, 32)
    # 0xa7360: replay only when frame(+32) > duration(+28) (fcmpo, branch if not gt).
    expect(app, 0xa7358, 'lfs', 1, 26, 32)
    expect(app, 0xa735c, 'lfs', 0, 26, 28)
    expect(app, 0xa7360, 'x63', 0, 1, 0, 32)
    expect(app, 0xa7364, 'bc', 4, 1, 0xa74f4)
    # 0xa6398 restart: r5 != 0 picks the start clock by channel flag 0x40; r5 == 0 applies the carry.
    expect(app, 0xa63e8, 'lwz', 0, 31, 0)
    expect(app, 0xa63ec, 'rlwinm', 0, 0, 0, 25, 25, 1)
    expect(app, 0xa63f4, 'lwz', 0, 7, 16408)
    expect(app, 0xa63fc, 'lwz', 0, 7, 16400)
    expect(app, 0xa6400, 'stw', 0, 31, 16)
    expect(app, 0xa6418, 'lfs', 0, 31, 28)
    expect(app, 0xa641c, 'x63', 0, 1, 0, 32)                # carry vs duration
    expect(app, 0xa6420, 'bc', 4, 1, 0xa6428)
    expect(app, 0xa6424, 'x63', 1, 0, 0, 72)                # cap: fmr f1,f0
    require((toc_float(app, 0xa6428, 0), toc_float(app, 0xa642c, 2)), (1000.0, 30.0), 'carry constants')
    expect(app, 0xa6430, 'a59', 1, 0, 0, 1, 25)             # 1000*carry
    expect(app, 0xa6434, 'lfs', 0, 31, 12)
    expect(app, 0xa6438, 'a59', 1, 1, 2, 0, 18)             # /30
    expect(app, 0xa643c, 'a59', 1, 1, 0, 0, 18)             # /speed
    require(app.call(0xa6440), 0x1c3fbc, 'carry conversion call')
    expect(app, 0xa6448, 'x31', 0, 3, 0, 40, 0)             # start = now(+20) - ms
    expect(app, 0xa644c, 'stw', 0, 31, 16)
    expect(app, 0x1c3ff4, 'x63', 2, 0, 2, 15)               # fctiwz (truncating)
    expect(app, 0x1c4004, 'addis', 3, 3, -32768)            # unsigned upper half
    return {
        'frame': 'speed * (30 * (float)(u32)(now - start) / 1000), single precision, speed applied last',
        'replay_test': '0xa7360 fcmpo frame > duration only',
        'carry': 'min(carry, duration) -> 1000*c/30/speed single -> truncating unsigned conversion; start = now - ms',
        'start_clock': 'channel flag 0x40 selects +16408 (set) or +16400 (clear) of the global block',
    }


def f32(x: float) -> float:
    return struct.unpack('<f', struct.pack('<f', x))[0]


def frame(ms: int, rate: float) -> float:
    return f32(f32(rate * f32(float(ms))) / 1000.0)


def carry_ms(carry: float, duration: float, rate: float) -> int:
    carry = min(carry, duration)
    return int(f32(f32(1000.0 * carry) / rate))


class Channel:
    """The decoded clock at speed 1.0 with `rate` in place of the constant 30 (rate 30 is the original)."""

    def __init__(self, duration: float, rate: float = 30.0, loop: bool = True):
        self.duration, self.rate, self.loop = duration, rate, loop
        self.now = self.start = 0
        self.finished = False
        self.tick = 0.0

    def advance(self, ms: int) -> float:
        self.now += ms
        if self.finished:
            return self.tick
        value = frame(self.now - self.start, self.rate)
        if value > self.duration:
            if self.loop:
                self.start = self.now - carry_ms(value - self.duration, self.duration, self.rate)
                value = frame(self.now - self.start, self.rate)
            else:
                self.finished = True
        self.tick = value
        return value


def clock_cases() -> dict:
    """The formats test expectations, recomputed from the decoded formula (rate 15 is an extension)."""
    c = Channel(30)
    out = {'30Hz_999_1000_1100': [c.advance(999), c.advance(1), c.advance(100)]}
    require(out['30Hz_999_1000_1100'], [f32(29.97), 30.0, 3.0], '30 Hz endpoint and carry')
    c = Channel(30)
    require(c.advance(2500), 30.0, '30 Hz single long update')
    c = Channel(10)
    require((c.advance(334), c.advance(333)), (0.0, f32(9.99)), '30 Hz 10-tick restart')
    h = Channel(30, 15)
    out['15Hz_1000_2000_2100'] = [h.advance(1000), h.advance(1000), h.advance(100)]
    require(out['15Hz_1000_2000_2100'], [15.0, 30.0, 1.5], '15 Hz endpoint and carry')
    h = Channel(30, 15)
    require((h.advance(5000), h.advance(100)), (30.0, 1.5), '15 Hz single long update')
    h = Channel(10, 15)
    require((h.advance(667), h.advance(666)), (0.0, f32(f32(15 * 666.0) / 1000)), '15 Hz 10-tick carry truncation')
    n = Channel(30, 15, loop=False)
    n.advance(2000)
    require(n.finished, False, '15 Hz non-looping endpoint')
    n.advance(1)
    require(n.finished, True, '15 Hz non-looping past end')
    out['conclusion'] = ('formats round-6 working-tree 15 Hz expectations equal the decoded formula with 15 '
                         'substituted for both constants; the original has no rate parameter')
    return out


# --- formats: copy-back gate and its flag sources (2760acb, 2d3442f) ---------------------------

def copy_back_gate(app: Binary) -> dict:
    expect(app, 0xa58a0, 'lwz', 7, 2, -30340)
    expect(app, 0xa58cc, 'lwz', 27, 7, 16396)                # option word
    expect(app, 0xa58bc, 'lwz', 0, 3, 4)                     # object flags
    expect(app, 0xa58c8, 'rlwinm', 0, 8, 0, 28, 28, 1)       # & 0x8
    expect(app, 0xa58dc, 'bc', 4, 2, 0xa58e8)
    expect(app, 0xa58e0, 'rlwinm', 0, 0, 0, 11, 11, 1)       # & 0x00100000
    expect(app, 0xa58e4, 'bc', 12, 2, 0xa594c)
    expect(app, 0xa58e8, 'cmpli', 0, 8, 0)
    expect(app, 0xa58ec, 'bc', 12, 2, 0xa5948)
    expect(app, 0xa5948, 'ori', 27, 27, 1)                   # keep pose
    expect(app, 0xa5950, 'rlwinm', 0, 0, 0, 29, 29, 1)       # header flag 0x4
    # Sole store of the option word, reached with r3 = 0 from setup.
    expect(app, 0xa7eec, 'lwz', 4, 2, -30340)
    expect(app, 0xa7ef0, 'stw', 3, 4, 16396)
    expect(app, 0x54c04, 'addi', 3, 0, 0)
    require(app.call(0x54c08), 0xa7eec, 'setup option-word call')
    # Builder 0x594c8: caller 0x200 -> ride 0x100, 0x40000 -> ride 0x1000.
    expect(app, 0x5958c, 'rlwinm', 24, 0, 0, 23, 23, 1)
    expect(app, 0x59594, 'ori', 19, 19, 1)
    expect(app, 0x59598, 'rlwinm', 24, 0, 0, 22, 22, 1)
    expect(app, 0x595a0, 'ori', 19, 19, 256)
    expect(app, 0x595c8, 'rlwinm', 24, 0, 0, 13, 13, 1)
    expect(app, 0x595d0, 'ori', 19, 19, 4096)
    # Loader 0x58a3c: ride 0x100 -> object 0x8, ride 0x1000 -> object 0x00100000.
    expect(app, 0x58c80, 'rlwinm', 17, 0, 0, 23, 23, 1)
    expect(app, 0x58c8c, 'ori', 0, 0, 8)
    expect(app, 0x58ce8, 'rlwinm', 17, 0, 0, 19, 19, 1)
    expect(app, 0x58cf4, 'oris', 0, 0, 16)
    # Catalog loader: descriptor +56 non-zero -> caller flags 0x50c00 (0x40000 set, 0x200 clear).
    expect(app, 0x119a60, 'lwz', 0, 9, 56)
    expect(app, 0x119a68, 'bc', 12, 2, 0x119ad0)
    expect(app, 0x119a70, 'addis', 3, 0, 5)
    expect(app, 0x119a74, 'addi', 7, 3, 3072)
    expect(app, 0x119a6c, 'lwz', 0, 9, 132)
    expect(app, 0x119a80, 'oris', 7, 7, 64)
    require(app.call(0x119ac0), 0x594c8, 'catalog builder call')
    return {
        'gate': 'keep pose when option bit 0, or object flag 0x00100000 with 0x8 clear; header flag 0x4 checked next',
        'option_word': 'only store 0xa7eec; setup 0x54c08 passes 0 (bulk writes to the block not excluded)',
        'flag_chain': 'descriptor +56 != 0 -> 0x50c00 -> ride 0x1000 -> object 0x00100000; 0x200 absent so 0x8 clear',
        'scope': 'object built by the catalog loader; placed-instance flags and their relation are not traced',
    }


# --- economy: scientist serializer tail (abbb52c against 6305d32) ------------------------------

STAFF_TAIL = [  # (call site, runtime offset, field-name offset from the tail string base, wire offset, width)
    (0xf2c8c, 484, 141, 0, 4), (0xf2d04, 512, 161, 8, 4), (0xf2d34, 416, 171, 12, 2),
    (0xf2d68, 518, 180, 78, 2), (0xf2d8c, 520, 196, 80, 2), (0xf2db0, 488, 212, 82, 1),
    (0xf2dd4, 516, 236, 83, 2), (0xf2df8, 412, 246, 85, 4), (0xf2e1c, 508, 253, 89, 4),
    (0xf2e40, 492, 272, 93, 8),
]
STAFF_NAMES = ['mCurrentPayGrade', 'mJobsDone', 'mName[i]', 'mPatrolRegionBL', 'mPatrolRegionTR',
               'mPercentageThroughGrade', 'mRestArea', 'mState', 'mTimeStartedIdling', 'mTimeHired']


def scientist_tail(app: Binary) -> dict:
    tail_base = app.slot(0xf2c30, 31)
    research_base = app.slot(0xf00f8, 31)
    names = []
    for (call, runtime, name, _, _), expected in zip(STAFF_TAIL, STAFF_NAMES):
        expect(app, call - 12, 'addi', 4, 30 if runtime == 416 else 27, runtime)
        expect(app, call - 8, 'addi', 5, 31, name)
        names.append(require(app.cstr(tail_base.addend + name), expected, f'field name at {call:#x}'))
    # Happiness (+500) and energy (+504): float -> fctiwz -> low byte -> float, then the float writer.
    for load, store, offset in ((0xf2ca0, 0xf2cc8, 500), (0xf2e54, 0xf2e7c, 504)):
        expect(app, load, 'lfs', 0, 27, offset)
        expect(app, load + 16, 'x63', 0, 0, 0, 15)
        expect(app, store, 'rlwinm', 7, 7, 0, 24, 31, 0)
    require(app.call(0xf2ce0), 0xcaab0, 'happiness writer')
    require(app.call(0xf2e94), 0xcaab0, 'energy writer')
    for call, runtime, name, expected in ((0xf0154, 528, 33, 'mTimeStartedResearching'), (0xf0178, 524, 57, 'mNext')):
        expect(app, call - 12, 'addi', 4, 27, runtime)
        expect(app, call - 8, 'addi', 5, 31, name)
        require(app.cstr(research_base.addend + name), expected, f'researcher field at {call:#x}')
    return {
        'staff_tail': [f'{wire}:{w}B {n}' for (_, _, _, wire, w), n in zip(STAFF_TAIL, names)]
                      + ['4:4B happiness(+500)', '101:4B energy(+504)'],
        'researcher_tail': ['105:4B mTimeStartedResearching', '109:2B mNext'],
        'float_vitals': 'saved as float(low byte of truncated value); the reader keeps the bits',
        'conclusion': 'abbb52c offsets and labels equal the original serializer order and widths (105 + 6)',
    }


def pc_payload(path: Path) -> bytes:
    raw = path.read_bytes()
    require(hashlib.sha256(raw).hexdigest(), PC_SAVE_SHA, 'PC fixture identity')
    require(raw[0x60d:0x611], b'BILZ', 'PC fixture chunk')
    declared = struct.unpack_from('<I', raw, 0x60d + 4)[0]
    inflater = zlib.decompressobj()
    payload = inflater.decompress(raw[0x60d + 28:], declared + 1)
    require((len(payload), inflater.eof), (declared, True), 'bounded inflate')
    require(hashlib.sha256(payload).hexdigest(), PC_PAYLOAD_SHA, 'PC payload identity')
    return payload


def scientist_walk(payload: bytes, head: int = 1385521) -> dict:
    world = 8 + struct.unpack_from('<I', payload, 4)[0]
    require(struct.unpack_from('<I', payload, world)[0], 2, 'world version')
    first = struct.unpack_from('<H', payload, world + 60)[0]
    current, offset, seen, models = struct.unpack_from('<I', payload, head)[0], head + 4, set(), []
    while True:
        require(current in seen, False, 'cycle')
        seen.add(current)
        nxt, model = struct.unpack_from('<II', payload, offset)
        models.append(model)
        size = 390 + (135 if model == 1 else 111)
        require(model in (1, 8), True, f'model at {offset}')
        if model == 8:
            staff = offset + 8 + 390
            return {'world_offset': world, 'first_researcher': first, 'scientist': current, 'header_offset': offset,
                    'body': [offset + 8, offset + 8 + size], 'actors': len(models),
                    'guests_before': models.count(1), 'next_used': nxt,
                    'grade': struct.unpack_from('<I', payload, staff)[0],
                    'state': struct.unpack_from('<I', payload, staff + 85)[0],
                    'research_tick': struct.unpack_from('<I', payload, staff + 105)[0],
                    'next_researcher': struct.unpack_from('<H', payload, staff + 109)[0],
                    'matches_first_researcher': current == first}
        current, offset = nxt, offset + 8 + size


# --- rides: coaster save boundary and byte-12 ordinal (3d9a01c) --------------------------------

def coaster_save(app: Binary) -> dict:
    # Load: EMAK (0x4b414d45) checked, coaster loader, then SAOC (0x434f4153) checked.
    expect(app, 0x11c014, 'addis', 0, 3, -0x4b41)
    expect(app, 0x11c018, 'cmpli', 0, 0, 0x4d45)
    require(app.call(0x11c038), 0x39f98, 'coaster loader')
    require(app.glue(app.call(0x11c098)), 'LbFile_Read__FPvPvUlPUl', 'marker read after the loader')
    expect(app, 0x11c0dc, 'addis', 0, 3, -0x434f)
    # Save: coaster saver, then SAOC written.
    require(app.call(0x11d6c8), 0x394b8, 'coaster saver')
    expect(app, 0x11d6e8, 'addis', 3, 0, 0x434f)
    expect(app, 0x11d6f0, 'addi', 0, 3, 0x4153)
    require(app.glue(app.call(0x11d728)), 'LbFile_Write__FPvPCvUlPUl', 'trailing marker write')
    # Byte 12: 255 picks a computed value, else passes through r10 -> r28 -> r6 -> r26 -> section +4.
    expect(app, 0x377b8, 'rlwinm', 20, 0, 0, 24, 31, 0)
    expect(app, 0x377bc, 'cmpli', 0, 0, 255)
    expect(app, 0x377ec, 'rlwinm', 20, 10, 0, 24, 31, 0)
    require(app.call(0x37804), 0x35fdc, 'builder call')
    expect(app, 0x36020, 'addi', 28, 10, 0)
    expect(app, 0x36514, 'addi', 6, 28, 0)
    require(app.call(0x36528), 0x34d90, 'insertion call')
    expect(app, 0x34da0, 'addi', 26, 6, 0)
    expect(app, 0x34f60, 'stw', 24, 3, 52)                   # cell array +52 [ordinal*4] = section
    expect(app, 0x34f74, 'stw', 26, 24, 4)                   # section +4 = ordinal
    expect(app, 0x34f7c, 'stw', 25, 24, 256)                 # section +256 = cell
    expect(app, 0x34f84, 'addi', 0, 3, 1)
    expect(app, 0x34f88, 'stw', 0, 25, 48)                   # cell count +48
    expect(app, 0x34f48, 'stw', 3, 5, 4)                     # shifted sections renumbered
    return {
        'boundary': 'EMAK, coaster body, SAOC (trailing); save writes SAOC after the saver returns',
        'byte12': 'cell stack insertion ordinal (255 -> computed); single assignment of r28 in 0x35fdc',
    }


def coaster_body(payload: bytes) -> dict:
    emak, saoc = b'EMAK', b'SAOC'
    starts = [i for i in range(len(payload) - 3) if payload[i:i + 4] == emak]
    ends = [i for i in range(len(payload) - 3) if payload[i:i + 4] == saoc]
    require(len(ends), 1, 'single SAOC in the fixture')
    before = [s for s in starts if s < ends[0]]
    start = before[-1] + 4
    body = payload[start:ends[0]]
    return {'emak_count': len(starts), 'saoc_count': len(ends), 'body': [start, ends[0]], 'bytes': len(body),
            'words': list(struct.unpack(f'<{len(body) // 4}I', body)) if len(body) % 4 == 0 else None,
            'sha256': hashlib.sha256(body).hexdigest()}


# --- TPI shifted header (0930182) ----------------------------------------------------------------

def tpi_header(path: Path) -> dict:
    raw = path.read_bytes()
    require(hashlib.sha256(raw).hexdigest(), TPI_SAVE_SHA, 'TPI save identity')
    return header_offsets(raw)


def header_offsets(raw: bytes) -> dict:
    """Legacy fixed offsets (SaveReader 0x608/0x609/0x60d) beside the length-prefixed shift."""
    require(raw[4], 1, 'length-prefixed profile byte')
    label = struct.unpack_from('<I', raw, 5)[0]
    shift = 5 + label
    return {'magic': struct.unpack_from('<I', raw)[0], 'byte4': raw[4], 'label_bytes': label, 'shift': shift,
            'legacy_0x608': raw[0x608], 'legacy_0x609': raw[0x609], 'legacy_bilz_at_0x60d': raw[0x60d:0x611] == b'BILZ',
            'shifted_version': raw[0x608 + shift], 'shifted_offline': raw[0x609 + shift],
            'shifted_bilz': raw[0x60d + shift:0x611 + shift] == b'BILZ',
            'legacy_reader_outcome': (f'rejects as version {raw[0x608]}' if raw[0x608] != 133 else 'passes version')}


# --- committed root doc status -------------------------------------------------------------------

def rides_doc_status(git: Path, refs: list[str]) -> dict:
    out = {}
    for ref in refs:
        shown = subprocess.run(['git', '-C', str(git), 'show', f'{ref}:docs/reverse/PPC-rides.md'],
                               capture_output=True, text=True)
        if shown.returncode:
            out[ref] = 'no PPC-rides.md'
            continue
        old = 'min(global_admission_limit' in shown.stdout
        new = 'max(global_admission_minimum' in shown.stdout
        out[ref] = 'corrected (max)' if new and not old else 'old min wording' if old else 'neither phrase'
    return out


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--bin-root', type=Path, required=True)
    parser.add_argument('--pc-save', type=Path)
    parser.add_argument('--tpi-save', type=Path)
    parser.add_argument('--git', type=Path)
    parser.add_argument('--refs', nargs='*', default=['main', 'origin/main', '8f27b00', '3d9a01c'])
    args = parser.parse_args()
    try:
        app = Binary(args.bin_root, 'SimThemePark.data', 'SimThemePark.data', rv.APP_TOC)
        result = {'channel_clock': channel_clock(app), 'clock_cases': clock_cases(),
                  'copy_back_gate': copy_back_gate(app), 'scientist_tail': scientist_tail(app),
                  'coaster_save': coaster_save(app)}
        if args.pc_save:
            payload = pc_payload(args.pc_save)
            result['scientist_walk'] = scientist_walk(payload)
            result['coaster_body'] = coaster_body(payload)
        if args.tpi_save:
            result['tpi_header'] = tpi_header(args.tpi_save)
        if args.git:
            result['rides_doc'] = rides_doc_status(args.git, args.refs)
    except (OSError, ReviewError, zlib.error, struct.error) as error:
        parser.exit(1, f'round 9 evidence: {error}\n')
    print(json.dumps(result, indent=2, sort_keys=True))


if __name__ == '__main__':
    main()

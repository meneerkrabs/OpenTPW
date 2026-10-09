"""Identity-pinned static witnesses for original format decoders in the Feral Mac build.

Reads the PEF data forks of SimThemePark, engine_shared and ltms_shared (and,
for the negative MTR/TQI searches, every container in the corpus), checks
selected instruction fields, relocated TOC slots, literal constants and label
strings, and prints interpreted JSON facts. Nothing is executed; no original
bytes, instruction text or disassembly is written. Offsets are code/data
section offsets of the pinned identities and are not portable to other builds.

Usage: python3 -I format_witness.py /path/to/mac-feral/bin
"""
from __future__ import annotations

import argparse
import hashlib
import json
import re
import struct
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))

from ppcfields import (WitnessError, a_form, branch_conditional, branch_target, compare_immediate, cstring,
                       d_form, require, rotate, word, x_form)
import pef  # noqa: E402  (ppcfields adds the shared reader directory)

IDENTITIES = {
    'SimThemePark.data': '04809cd4ccee5433c7fb0b7c93d32f6a7aa629c1849181c0b7906415e5e295f5',
    'engine_shared.data': 'c549123f647dcf33c3e2c3d515bffe2cfcb19d11680f743f02ca42bb09dbd73b',
    'ltms_shared.data': '2b0f7ac92c1f8b67761d271dd5832fca1bd19a8dd8d494b7b89b6ffa693e6ee8',
}
LIS, ADDI, ADDIS, LWZ, LFS, LHZ, LHA, ORI, ORIS = 15, 14, 15, 32, 48, 40, 42, 24, 25
BT, BF, CR_LT, CR_GT, CR_EQ = 12, 4, 0, 1, 2
RLWIMI, RLWINM = 20, 21
SRAWI = 824


class Image:
    """One identified container with code/data views and its TOC base."""

    def __init__(self, path: Path):
        raw = path.read_bytes()
        require(hashlib.sha256(raw).hexdigest(), IDENTITIES[path.name], f'identity of {path.name}')
        self.container = pef.PEFContainer(raw, path.name)
        self.code = bytes(self.container.code.data)
        self.data = bytes(self.container.data_section.data)
        self.relocs = self.container.relocs.get(self.container.data_section.index, {})
        self.toc = 0

    def w(self, offset: int) -> int:
        return word(self.code, offset)

    def vector(self, name: str) -> int:
        exports = [e for e in self.container.exports if e.name == name]
        require(len(exports), 1, f'export {name}')
        target = self.relocs.get(exports[0].value)
        toc = self.relocs.get(exports[0].value + 4)
        if not target or not toc or target.target != self.container.code.index:
            raise WitnessError(f'unrelocated transition vector {name}')
        self.toc = toc.addend
        return target.addend

    def toc_slot(self, offset: int, register: int):
        """Interpret `lwz rX, d(r2)`/`lfs`/`lfd` at offset: (relocation, slot)."""
        op, rt, ra, displacement = d_form(self.w(offset))
        require(ra, 2, f'TOC-relative access at {offset:#x}')
        require(rt, register, f'destination register at {offset:#x}')
        slot = self.toc + displacement
        return op, self.relocs.get(slot), slot

    def toc_string(self, offset: int, register: int) -> tuple[int, str]:
        op, target, slot = self.toc_slot(offset, register)
        require(op, LWZ, f'TOC pointer load at {offset:#x}')
        if not target or target.kind != 'section' or target.target != self.container.code.index:
            raise WitnessError(f'TOC slot {slot:#x} is not a code pointer')
        return target.addend, cstring(self.code, target.addend)

    def toc_float(self, offset: int, register: int) -> float:
        op, target, slot = self.toc_slot(offset, register)
        if target is not None:
            raise WitnessError(f'TOC slot {slot:#x} is relocated, not a literal')
        if op == LFS:
            return struct.unpack_from('>f', self.data, slot)[0]
        require(op, 50, f'floating load at {offset:#x}')
        return struct.unpack_from('>d', self.data, slot)[0]

    def call(self, offset: int) -> int:
        return branch_target(self.w(offset), offset)

    def glue_symbol(self, address: int) -> str:
        op, rt, ra, displacement = d_form(self.w(address))
        require((op, rt, ra), (LWZ, 12, 2), f'import glue at {address:#x}')
        target = self.relocs.get(self.toc + displacement)
        if not target or target.kind != 'import':
            raise WitnessError(f'glue at {address:#x} does not load an import')
        return self.container.imports[target.target].name

    def calls_to(self, target: int, start: int = 0, end: int | None = None) -> list[int]:
        end = len(self.code) if end is None else end
        sites = []
        for offset in range(start, end - 3, 4):
            w = self.w(offset)
            if w >> 26 == 18 and w & 3 == 1 and branch_target(w, offset) == target:
                sites.append(offset)
        return sites


def d(image: Image, offset: int, expected: tuple, context: str):
    return require(d_form(image.w(offset)), expected, context)


def rot(image: Image, offset: int, expected: tuple, context: str):
    return require(rotate(image.w(offset)), expected, context)


def bc(image: Image, offset: int, expected: tuple, context: str):
    """Conditional branch: (BO, BI, target)."""
    return require(branch_conditional(image.w(offset), offset), expected, context)


def fp(image: Image, offset: int, expected: tuple, context: str):
    """Single-precision A-form (opcode 59): (extended opcode, frt, fra, frb, frc)."""
    op, frt, fra, frb, frc, xo = a_form(image.w(offset))
    require(op, 59, f'single-precision arithmetic at {offset:#x}')
    return require((xo, frt, fra, frb, frc), expected, context)


def xop(image: Image, offset: int, expected: tuple, context: str):
    """Opcode-31/63 X-form: (opcode, extended opcode, rt, ra, rb)."""
    op, rt, ra, rb, xo, _ = x_form(image.w(offset))
    return require((op, xo, rt, ra, rb), expected, context)


FADDS, FSUBS, FMULS, FMADDS = 21, 20, 25, 29


def cmpli(image: Image, offset: int, expected: tuple, context: str):
    return require(compare_immediate(image.w(offset)), expected, context)


def lis_addi_constant(image: Image, lis_at: int, addi_at: int, register: int) -> int:
    op, rt, ra, high = d_form(image.w(lis_at))
    require((op, ra), (LIS, 0), f'lis at {lis_at:#x}')
    op2, rt2, ra2, low = d_form(image.w(addi_at))
    require((op2, ra2, rt2), (ADDI, rt, register), f'addi at {addi_at:#x}')
    return ((high << 16) + low) & 0xffffffff


def label_suffix(text: str) -> str:
    match = re.fullmatch(r'%s \((.+)\)', text)
    if not match:
        raise WitnessError(f'unexpected status label format {text!r}')
    return match.group(1)


# ------------------------------------------------------------------ MD2
def md2_loader(engine: Image) -> dict:
    load = engine.vector('LoadM3D2Header__FPcUl')
    require(load, 0x3f9d8, 'LoadM3D2Header code entry')
    strings, rb = engine.toc_string(0x3f9e8, 31)
    require(rb, 'rb', 'open mode')
    magic_high = d(engine, 0x3fc38, (ADDIS, 0, 3, -7377), 'magic subtract')[3]
    magic_low = cmpli(engine, 0x3fc3c, (10, 0, 0, 23878), 'magic compare')[3]
    magic = ((-magic_high) << 16) + magic_low
    cmpli(engine, 0x3fc78, (10, 0, 0, 221), 'mesh version compare')
    cmpli(engine, 0x3ff1c, (10, 0, 0, 203), 'animation version compare')
    cmpli(engine, 0x3fffc, (10, 0, 0, 221), 'old mesh warning compare')
    rot(engine, 0x3f9f0, (RLWINM, 4, 27, 0, 31, 31, 0), 'allow-old flag bit 1')
    rot(engine, 0x3fcec, (RLWINM, 26, 0, 0, 30, 30, 1), 'animation flag bit 2')
    d(engine, 0x3fcf4, (LWZ, 0, 29, 0x98), 'animation trailer pointer at header 0x98')
    status_sites = {0x3fc44: 'magic', 0x3fc80: 'major>221', 0x3fcbc: 'major<221 without allow-old',
                    0x3fd00: 'mesh requested, trailer present', 0x3fd3c: 'animation requested, no trailer',
                    0x3ff30: 'minor>203 (trailer dropped)', 0x3ff5c: 'minor<203 (trailer dropped)',
                    0x3ffe4: 'unreachable after minor==203', 0x40004: 'major<221 accepted without trailer',
                    0x40034: 'clean load'}
    labels = {}
    for site, meaning in status_sites.items():
        _, rt, ra, displacement = d_form(engine.w(site))
        require((rt, ra), (3, 31), f'status label operand at {site:#x}')
        labels[meaning] = label_suffix(cstring(engine.code, strings + displacement))
    # Call sites in the application: LoadM3D2Header flags are 0, 2 or 6 (never bit 1).
    return {'code_entry': load, 'magic': magic, 'mesh_version': 221, 'animation_version': 203,
            'status_labels': labels, 'flag_bits': {'1': 'allow major<221', '2': 'animation member',
                                                    '4': 'passed to texture-slot swap'}}


def md2_trailer_and_records(engine: Image) -> dict:
    # (call site, callee, pointer offset in trailer, count offset in trailer)
    calls = {0x3ff88: (0x40664, 32, 12, 'position headers (16-byte, same swap as header 0xAC records)'),
             0x3ff98: (0x412e0, 48, 24, 'texture-frame tracks: {u16 slot, u16 count, ptr->(u16 tick, u16 frame)}'),
             0x3ffa8: (0x413b4, 44, 18, 'record table (64-byte records)'),
             0x3ffb4: (0x41604, 36, 14, 'scale keys (16-byte)'),
             0x3ffc4: (0x4171c, 40, 16, 'rotation keys (20-byte)'),
             0x3ffd4: (0x417c4, 56, 26, 'node list (u16)')}
    roles = {}
    for site, (callee, pointer, count, meaning) in calls.items():
        require(engine.call(site), callee, f'trailer helper call at {site:#x}')
        loads = {d_form(engine.w(site - k)) for k in range(4, 24, 4)}
        if (LWZ, 3, 26, pointer) not in loads or (LHZ, 4, 26, count) not in loads:
            raise WitnessError(f'trailer operands before {site:#x}')
        roles[f'trailer+{pointer}'] = {'count_at': f'trailer+{count}', 'meaning': meaning}
    # Records: relocated pointers +24..+52 except +40, which is handled by the vertex helper.
    for site, offset in ((0x41574, 24), (0x41588, 32), (0x4159c, 28), (0x415b0, 36), (0x415c4, 44),
                         (0x415d8, 48), (0x415ec, 52)):
        d(engine, site, (LWZ, 0, 3, offset), f'record pointer relocation +{offset}')
    d(engine, 0x41ab8, (LWZ, 5, 3, 40), 'vertex block pointer')
    d(engine, 0x41ac4, (LWZ, 0, 3, 4), 'record flags')
    rot(engine, 0x41ac8, (RLWINM, 0, 0, 0, 17, 17, 1), 'flag 0x4000 selects the 12-byte vertex block')
    require(engine.call(0x414c4), 0x41890, '0x20000 list helper')
    d(engine, 0x414c0, (LHZ, 4, 30, 22), '0x20000 list count at record+22')
    require(engine.call(0x414d4), 0x418c0, '0x10000 block helper')
    return {'trailer': roles,
            'record_pointers': {'+24': 'position header', '+28': 'rotation keys', '+32': 'scale keys',
                                '+36': 'path-parameter block', '+40': 'vertex block (flag 0x4000 selects layout)',
                                '+44': '0x10000 block', '+48': '0x20000 visibility list (count +22)',
                                '+52': 'easing curves'}}


def md2_vertex_packing(engine: Image) -> dict:
    expected = {0x41cbc: (RLWIMI, 10, 5, 8, 22, 23, 0), 0x41cc4: (RLWIMI, 5, 0, 6, 16, 25, 0),
                0x41cd0: (RLWINM, 9, 0, 6, 22, 25, 0), 0x41cd4: (RLWIMI, 10, 0, 30, 26, 31, 0),
                0x41ce0: (RLWIMI, 5, 0, 12, 10, 19, 0), 0x41ce4: (RLWINM, 6, 5, 4, 22, 27, 0),
                0x41cec: (RLWIMI, 9, 5, 28, 28, 31, 0), 0x41cf4: (RLWIMI, 5, 0, 2, 20, 29, 0),
                0x41d00: (RLWIMI, 4, 0, 0, 30, 31, 0)}
    for site, fields in expected.items():
        rot(engine, site, fields, f'packed key conversion at {site:#x}')
    return {'fields_little_endian_bits': [[0, 10], [10, 20], [20, 30]], 'cleared_bits': [30, 31],
            'conversion_code': 0x41cac}


def md2_runtime(app: Image) -> dict:
    app.toc = app.relocs[app.container.main[1] + 4].addend
    # 0xa4344 per-group sampler: signed 10-bit extraction and offset/scale vectors.
    d(app, 0xa434c, (ADDI, 9, 3, 20), 'offset vector at block+20')
    d(app, 0xa4350, (ADDI, 10, 3, 32), 'scale vector at block+32')
    d(app, 0xa4364, (LWZ, 0, 5, 16), 'group key cursor at group+16')
    for site, sh in ((0xa43bc, 10), (0xa43cc, 20), (0xa43e8, 16)):
        rotated = rotate(app.w(site))
        require((rotated[0], rotated[3], rotated[4], rotated[5]), (RLWINM, sh, 0, 10), f'field rotate {site:#x}')
    for site in (0xa43c8, 0xa43d8, 0xa43f4):
        fields = x_form(app.w(site))
        require((fields[0], fields[4], fields[3]), (31, SRAWI, 22), f'arithmetic shift {site:#x}')
    fused = a_form(app.w(0xa446c))
    require((fused[0], fused[5], fused[2], fused[4], fused[3]), (59, 29, 2, 5, 6), 'q * scale + offset')
    require(app.toc_float(0xa438c, 0), 1.0, 'interpolation complement')
    xop(app, 0xa436c, (31, 235, 7, 0, 11), 'key row = cursor * vertices (key-major packed words)')
    fp(app, 0xa4484, (FMULS, 2, 2, 0, 1), 'next value * fraction')
    fp(app, 0xa44ac, (FMADDS, 2, 3, 2, 0), 'current * (1 - fraction) + product')
    # 0xa4a58 group dispatcher; r3 is the node state (0xa5054), not the instance.
    xop(app, 0xa5054, (31, 444, 27, 3, 27), 'dispatcher argument r3 = node state')
    rot(app, 0xa5060, (RLWINM, 0, 7, 0, 29, 29, 0), 'add mode = header flags (+0x30) bit 0x4')
    d(app, 0xa5058, (LWZ, 4, 28, 40), 'vertex block at record+40')
    rot(app, 0xa4ac8, (RLWINM, 0, 0, 0, 8, 8, 1), 'node-state flag 0x00800000')
    bc(app, 0xa4ad0, (BF, CR_EQ, 0xa4de8), 'flag set: keep cursors and skip the static group')
    d(app, 0xa4af0, (36, 4, 30, 16), 'flag clear: reset group cursors (stw 0 to group+16)')
    xop(app, 0xa4b74, (63, 32, 0, 0, 31), 'compare ticks[cursor + 1] with time')
    bc(app, 0xa4b78, (BT, CR_LT, 0xa4b48), 'advance the cursor while ticks[cursor + 1] < time')
    d(app, 0xa4b4c, (ADDI, 3, 3, 1), 'cursor + 1')
    require(app.call(0xa4bc8), 0xa468c, 'group 0 bounds helper')
    d(app, 0xa4ba0, (ADDI, 4, 26, 120), 'group 0 target at node state+120')
    rot(app, 0xa4bd8, (RLWINM, 0, 0, 0, 30, 30, 1), 'header bit 0x2: static second group')
    d(app, 0xa4ddc, (ORIS, 0, 0, 128), 'set-mode pass sets node-state flag 0x00800000')
    require(app.call(0xa4f2c), 0xa4344, 'remaining groups sampler')
    # 0xa468c: vertex 0 -> lerp - scale - 0.25 at +0..+8, vertex 1 -> scale + lerp + 0.25 at +12..+20.
    require(app.toc_float(0xa47e0, 6), 0.25, 'group 0 quarter-unit adjustment')
    fp(app, 0xa47fc, (FSUBS, 0, 5, 0, 0), 'lower: lerp - scale')
    fp(app, 0xa4800, (FSUBS, 0, 0, 6, 0), 'lower: - 0.25')
    d(app, 0xa4888, (LHA, 9, 3, 4), 'vertex 1 word of the current key')
    d(app, 0xa4890, (LWZ, 0, 3, 4), 'vertex 1 word of the current key')
    d(app, 0xa48a8, (LHA, 0, 5, 4), 'vertex 1 word of the next key')
    require(app.toc_float(0xa499c, 5), 0.25, 'upper quarter-unit adjustment')
    fp(app, 0xa49b8, (FADDS, 2, 8, 4, 0), 'upper: scale + lerp')
    fp(app, 0xa49bc, (FADDS, 2, 5, 2, 0), 'upper: + 0.25')
    d(app, 0xa49c4, (52, 2, 4, 12), 'upper stored at target+12 (node state+132)')
    # 0xa4f68 record sampler dispatch.
    d(app, 0xa4f94, (LFS, 1, 3, 32), 'player time')
    d(app, 0xa4f98, (LFS, 0, 3, 28), 'player duration')
    bc(app, 0xa4fa0, (BF, CR_GT, 0xa4fac), 'records are skipped when time > duration')
    rot(app, 0xa4fb4, (RLWINM, 0, 0, 0, 14, 14, 1), 'node-flag toggle list flag 0x20000')
    d(app, 0xa4fcc, (ADDI, 6, 4, -2), 'toggle scan starts at the last entry')
    xop(app, 0xa4fec, (31, 104, 4, 4, 0), 'toggle magnitude: neg')
    xop(app, 0xa4ff4, (31, 32, 0, 3, 0), 'unsigned compare of tick with magnitude')
    bc(app, 0xa4ff8, (BT, CR_LT, 0xa5004), 'skip entries with tick < magnitude')
    bc(app, 0xa501c, (BT, CR_GT, 0xa5030), 'positive entry clears')
    d(app, 0xa5024, (ORI, 0, 0, 16), 'zero/negative entry sets node-state bit 0x10')
    rot(app, 0xa5034, (RLWINM, 0, 0, 0, 28, 26, 0), 'positive entry clears node-state bit 0x10')
    rot(app, 0xa5040, (RLWINM, 3, 0, 0, 19, 19, 1), 'vertex flag 0x1000')
    rot(app, 0xa5048, (RLWINM, 3, 0, 0, 17, 17, 1), 'vertex layout flag 0x4000')
    require(app.call(0xa5068), 0xa4a58, 'quantized vertex animation')
    # 0xa56f8 clip update: texture-frame tracks need trailer word 0 bit 0x2 set and global option bit 0x8 clear.
    d(app, 0xa5760, (LWZ, 0, 29, 0), 'trailer word 0')
    rot(app, 0xa5764, (RLWINM, 0, 0, 0, 30, 30, 1), 'trailer word 0 bit 0x2')
    bc(app, 0xa5768, (BT, CR_EQ, 0xa578c), 'skip when trailer bit 0x2 is clear')
    d(app, 0xa5770, (LWZ, 0, 3, 16396), 'global option word')
    rot(app, 0xa5774, (RLWINM, 0, 0, 0, 28, 28, 1), 'global option bit 0x8')
    bc(app, 0xa5778, (BF, CR_EQ, 0xa578c), 'skip when global option bit 0x8 is set')
    require(app.call(0xa5788), 0xa4160, 'texture-frame track sampler')
    d(app, 0xa4184, (LWZ, 9, 31, 48), 'texture-frame tracks at trailer+48')
    d(app, 0xa4188, (LHZ, 8, 31, 24), 'texture-frame track count at trailer+24')
    d(app, 0xa41a8, (ADDI, 4, 4, -4), 'scan starts at the last key')
    xop(app, 0xa41c4, (31, 32, 0, 3, 0), 'compare tick with key tick')
    bc(app, 0xa41c8, (BT, CR_LT, 0xa41f0), 'tick < key tick: step back one key')
    d(app, 0xa41f0, (ADDI, 4, 4, -4), 'previous key')
    d(app, 0xa41e4, (24, 0, 0, 1024), 'texture slot dirty bit 0x400')
    rot(app, 0xa507c, (RLWINM, 0, 0, 0, 28, 28, 1), 'rotation flag 0x8')
    d(app, 0xa509c, (ADDI, 6, 0, 2), 'rotation key kind')
    d(app, 0xa50a0, (ADDI, 7, 0, 1), 'rotation key search wraps')
    cmpli(app, 0xa50cc, (10, 0, 0, 65535), 'no-easing index')
    easing_scale = app.toc_float(0xa50d4, 1)
    sample_scale = app.toc_float(0xa511c, 0)
    rot(app, 0xa51fc, (RLWINM, 0, 0, 0, 24, 24, 1), 'scale flag 0x80')
    d(app, 0xa521c, (ADDI, 6, 0, 4), 'scale key kind')
    d(app, 0xa5220, (ADDI, 7, 0, 0), 'scale key search holds')
    d(app, 0xa52fc, (ADDI, 6, 0, 1), 'position time kind')
    d(app, 0xa5300, (ADDI, 7, 0, 0), 'position search holds')
    rot(app, 0xa54ec, (RLWINM, 3, 0, 0, 30, 30, 1), 'position kind bit 2: Bezier')
    require(app.call(0xa5508), 0xa85ac, 'Bezier evaluator')
    rot(app, 0xa5510, (RLWINM, 3, 0, 0, 28, 28, 1), 'position kind bit 8: linear')
    require(app.call(0xa552c), 0xa88fc, 'linear evaluator')
    # 0xa3ff0 key search strides and truncating time conversion.
    strides = {kind: d(app, site, (ADDI, 26, 0, stride), f'kind {kind} stride')[3]
               for kind, site, stride in ((1, 0xa4044, 4), (2, 0xa404c, 20), (4, 0xa4054, 16))}
    require(app.call(0xa4074), 0x1c3fbc, 'double to unsigned conversion')
    # Rotation pair and interpolation mode.
    d(app, 0xa8228, (LWZ, 3, 5, 16412), 'rotation key count global')
    rot(app, 0xa8248, (RLWINM, 0, 0, 0, 30, 30, 1), 'global option bit 0x2')
    bc(app, 0xa824c, (BT, CR_EQ, 0xa82c4), 'option bit 0x2 clear: table slerp; set: linear blend')
    require(app.call(0xa82e0), 0xa7fc8, 'slerp helper')
    require(app.call(0xa82ec), 0xa7ef8, 'quaternion to matrix with 2/|q|^2')
    slerp_threshold = app.toc_float(0xa8030, 2)
    # Clock.
    ticks = app.toc_float(0xa642c, 2)
    milliseconds = app.toc_float(0xa6428, 0)
    require((app.toc_float(0xa64b0, 3), app.toc_float(0xa64b8, 0)), (ticks, milliseconds), 'UpdateTime constants')
    per_tick = app.toc_float(0xa65a8, 2)
    clock_glue = app.call(0x10edc8)
    require(app.glue_symbol(clock_glue), 'LbTime_GetClock__Fv', 'scene clock source')
    require(app.glue_symbol(app.call(0x127ce4)), 'LbTime_GetClock__Fv', 'scaled clock source')
    # LoadM3D2Header call sites.
    glue = app.call(0x58f04)
    require(app.glue_symbol(glue), 'LoadM3D2Header__FPcUl', 'loader import')
    sites = app.calls_to(glue)
    require(sites, [0x58f04, 0x99ad8], 'loader call sites')
    flags = sorted({d(app, 0x58f00, (ADDI, 4, 0, 0), 'mesh flags')[3],
                    d(app, 0x99acc, (ADDI, 4, 0, 6), 'animation flags')[3],
                    d(app, 0x99ad4, (ADDI, 4, 0, 2), 'animation flags')[3]})
    return {'vertex_sampler': 0xa4344, 'vertex_group_dispatch': 0xa4a58, 'record_sampler': 0xa4f68,
            'key_search': 0xa3ff0, 'key_strides': strides, 'easing_scale': easing_scale,
            'easing_sample_divisor': sample_scale, 'slerp_threshold': slerp_threshold,
            'group0_vectors': {'lower': 'lerp(vertex 0) - scale - 0.25 -> node state +120',
                               'upper': 'scale + lerp(vertex 1) + 0.25 -> node state +132'},
            'texture_frames_require': 'trailer word 0 & 0x2 set and global option & 0x8 clear',
            'rotation_mode': 'global option & 0x2 clear: table slerp 0xa7fc8; set: linear blend',
            'animation_ticks_per_second': ticks, 'milliseconds_per_second': milliseconds,
            'milliseconds_per_tick': per_tick, 'loader_call_sites': sites, 'loader_flags_used': flags}


MULLI, ANDI_DOT, STW = 7, 28, 36


def md2_runtime_binding(app: Image) -> dict:
    """Which objects the vertex sampler writes, how a clip bind resets them, and the relative-animation flag."""
    # The sampler's "instance" is the geometry M3D2 header: object +8 -> model object, whose +4 the
    # geometry loader passes to CMesh::Init(tag_sM3D2Header *) (0x99b84).
    d(app, 0x99b84, (LWZ, 4, 31, 4), 'model object +4 is the header given to CMesh::Init')
    require(app.glue_symbol(app.call(0x99b88)), 'Init__5CMeshFP15tag_sM3D2Header', 'CMesh::Init import')
    for site in (0xa573c, 0xa7238):
        d(app, site, (LWZ, 4, 28, 4), f'record sampler instance = model object +4 ({site:#x})')
    d(app, 0xa5734, (LWZ, 6, 31, 60), 'node state = record +60')
    # Clip bind 0xa5894: r24 = target header, r25 = base header, both as object +8 -> +4.
    d(app, 0xa58b8, (LWZ, 6, 3, 0), 'bind: base object')
    d(app, 0xa58c0, (LWZ, 4, 6, 8), 'bind: base model object')
    d(app, 0xa58c4, (LWZ, 3, 3, 8), 'bind: target model object')
    d(app, 0xa58d4, (LWZ, 25, 4, 4), 'bind: base header')
    d(app, 0xa58d8, (LWZ, 24, 3, 4), 'bind: target header')
    # Node index -> header mesh records (+0x70, 160 bytes) below the mesh count (+0x44), else dummies (+0x74, 88).
    d(app, 0xa5a08, (LHZ, 3, 24, 68), 'mesh count at header +0x44')
    d(app, 0xa5a14, (MULLI, 0, 0, 160), 'mesh record stride 160')
    d(app, 0xa5a18, (LWZ, 3, 24, 112), 'mesh records at header +0x70')
    d(app, 0xa5a28, (LWZ, 3, 24, 116), 'dummy records at header +0x74')
    d(app, 0xa5a2c, (MULLI, 0, 0, 88), 'dummy record stride 88')
    # Flags cleared on the replaced clip's nodes: 0x00800000 with relative animation, else 0x00F40000.
    d(app, 0xa594c, (LWZ, 0, 24, 48), 'header flags (+0x30)')
    rot(app, 0xa5950, (RLWINM, 0, 0, 0, 29, 29, 1), 'header flag 0x4')
    bc(app, 0xa5954, (BT, CR_EQ, 0xa5960), 'flag 0x4 clear: wider mask')
    d(app, 0xa5958, (ADDIS, 26, 0, 128), 'mask 0x00800000')
    d(app, 0xa5960, (ADDIS, 26, 0, 244), 'mask 0x00F40000')
    xop(app, 0xa59e0, (31, 124, 26, 5, 26), 'complement of the mask')
    xop(app, 0xa5a38, (31, 28, 0, 0, 5), 'node flags &= ~mask')
    # Channels animated by the replaced clip but not the new one are copied back from the base header.
    xop(app, 0xa5aa8, (31, 60, 4, 0, 0), 'old record flags & ~new record flags')
    d(app, 0xa5bfc, (ANDI_DOT, 29, 0, 0x289), 'matrix channels 0x289 restore the local matrix rows')
    rot(app, 0xa5c64, (RLWINM, 29, 0, 0, 19, 19, 1), 'vertex flag 0x1000 restores positions and bounds')
    d(app, 0xa5c6c, (LWZ, 3, 31, 96), 'target positions at mesh record +96')
    d(app, 0xa5c78, (LWZ, 4, 30, 96), 'base positions at mesh record +96')
    d(app, 0xa5c84, (LHZ, 5, 31, 88), 'position count at mesh record +88')
    d(app, 0xa5c98, (MULLI, 5, 0, 48), '48 bytes per block of four positions')
    require(app.glue_symbol(app.call(0xa5c9c)), 'memcpy', 'position copy')
    for site, field in ((0xa5cac, 120), (0xa5cbc, 128), (0xa5cc0, 132), (0xa5cd0, 140)):
        d(app, site, (STW, 3 if field in (120, 128) else 0, 31, field), f'bounds word +{field} copied')
    rot(app, 0xa5cd4, (RLWINM, 29, 0, 0, 15, 15, 1), 'flag 0x10000 restores the +104 array')
    d(app, 0xa5cdc, (LWZ, 3, 31, 104), 'mesh record +104 (texture coordinates)')
    d(app, 0xa5cf4, (LHZ, 5, 31, 94), 'count at mesh record +94 (corners)')
    # Vertex writes: positions at node state +96 in blocks of four; after the pass node flag 0x00010000 is set
    # and the sampler sets header flag 0x00040000.
    d(app, 0xa4c0c, (LWZ, 6, 26, 96), 'positions at node state +96')
    d(app, 0xa4c2c, (MULLI, 5, 5, 48), 'position block of four')
    d(app, 0xa4f44, (ORIS, 0, 0, 1), 'node-state flag 0x00010000 after a vertex pass')
    d(app, 0xa5050, (LWZ, 0, 26, 48), 'add mode read from header flags')
    d(app, 0xa5070, (ORIS, 0, 0, 4), 'header flag 0x00040000 after a vertex pass')
    # Ride loader 0x58a3c strips header flag 0x4 unless its argument has bit 0x20000.
    d(app, 0x58a44, (ADDI, 17, 4, 0), 'loader argument word')
    rot(app, 0x58bd0, (RLWINM, 17, 0, 0, 14, 14, 1), 'loader argument bit 0x20000')
    bc(app, 0x58bd4, (BF, CR_EQ, 0x58c14), 'bit 0x20000 set: keep relative animation')
    d(app, 0x58bd8, (LWZ, 4, 22, 4), 'loaded geometry header')
    d(app, 0x58bdc, (LWZ, 0, 4, 48), 'header flags')
    rot(app, 0x58be0, (RLWINM, 0, 0, 0, 29, 29, 1), 'header flag 0x4')
    bc(app, 0x58be4, (BT, CR_EQ, 0x58c14), 'flag clear: nothing to strip')
    _, base, _ = app.toc_slot(0x58a5c, 24)
    d(app, 0x58be8, (ADDI, 3, 24, 374), 'warning text')
    warning = cstring(app.code, base.addend + 374)
    require(warning.startswith('Ride %s has relative animation incorrectly set'), True, 'relative animation warning')
    rot(app, 0x58bfc, (RLWINM, 0, 0, 0, 30, 28, 0), 'clear header flag 0x4')
    d(app, 0x58c0c, (ORIS, 0, 0, 256), 'set header flag 0x01000000')
    callers = app.calls_to(0x58a3c)
    require(callers, [0x29180, 0x4da20, 0x4da44, 0x596b0, 0x59704], 'ride loader call sites')
    arguments = {0x29180: lis_addi_constant(app, 0x29168, 0x29174, 4),
                 0x4da20: d(app, 0x4da14, (ADDI, 4, 0, 8), 'argument')[3],
                 0x4da44: d(app, 0x4da38, (ADDI, 4, 0, 8), 'argument')[3],
                 0x59704: d(app, 0x596f4, (ADDI, 4, 0, 0), 'argument')[3]}
    require(any(value & 0x20000 for value in arguments.values()), False, 'constant arguments strip the flag')
    # 0x594c8 builds the argument for 0x596b0: bit 0x20000 only from bit 0x01000000 of its r7 word.
    d(app, 0x594e8, (ADDI, 24, 7, 0), 'flag word argument')
    rot(app, 0x595f8, (RLWINM, 24, 0, 0, 7, 7, 1), 'flag word bit 0x01000000')
    bc(app, 0x595fc, (BT, CR_EQ, 0x59604), 'bit clear: argument keeps 0x20000 clear')
    d(app, 0x59600, (ORIS, 19, 19, 2), 'argument bit 0x20000')
    d(app, 0x596a0, (ADDI, 4, 19, 0), 'argument passed to the ride loader')
    return {'instance': 'geometry M3D2 header (model object +4)',
            'node_state': 'header mesh record (+0x70, 160 bytes) or dummy record (+0x74, 88 bytes)',
            'positions': 'mesh record +96, count +88; bounds +120/+132 are the stored mesh bounds fields',
            'bind_clear_mask': {'relative': 0x00800000, 'set_mode': 0x00F40000},
            'relative_animation_flag': 0x4, 'warning': warning,
            'ride_loader_call_sites': callers, 'ride_loader_constant_arguments': {f'{k:#x}': v for k, v in arguments.items()},
            'relative_animation_kept_when': 'loader argument & 0x20000 (0x594c8: flag word & 0x01000000)'}


def md2_clip_lifecycle(app: Image) -> dict:
    """Loop replay, cursor reset, copy-back gate and normal recomputation around the vertex sampler."""
    # Object update 0xa7960(object, r4, r5) passes r4 to the channel update 0xa7190 as r27.
    d(app, 0xa796c, (ADDI, 30, 4, 0), 'object update keeps its second argument')
    d(app, 0xa7a58, (ADDI, 5, 30, 0), 'passed as the channel update third argument')
    require(app.call(0xa7a5c), 0xa7190, 'channel update call')
    d(app, 0xa71a4, (ADDI, 27, 5, 0), 'channel update keeps it in r27')
    # Wrap: AnimFrame (+32) > TotalAnimFrames (+28), no deferred clip (+36 == 12), object flags 0x18 clear.
    d(app, 0xa7358, (LFS, 1, 26, 32), 'channel AnimFrame')
    d(app, 0xa735c, (LFS, 0, 26, 28), 'channel TotalAnimFrames')
    xop(app, 0xa7360, (63, 32, 0, 1, 0), 'fcmpo AnimFrame, TotalAnimFrames')
    bc(app, 0xa7364, (BF, CR_GT, 0xa74f4), 'not past the end: no wrap')
    d(app, 0xa7380, (LWZ, 4, 26, 36), 'channel DeferredAnimID')
    cmpli(app, 0xa7384, (10, 0, 4, 12), 'no deferred clip is 12')
    bc(app, 0xa7388, (BF, CR_EQ, 0xa74a8), 'deferred clip present: switch to it')
    rot(app, 0xa7390, (RLWINM, 3, 0, 0, 27, 28, 1), 'object flags 0x18 (stop or hold)')
    bc(app, 0xa7394, (BT, CR_EQ, 0xa7420), 'neither set: replay path')
    rot(app, 0xa7428, (RLWINM, 0, 0, 0, 31, 31, 1), 'channel loop bit 0x1')
    bc(app, 0xa742c, (BT, CR_EQ, 0xa7478), 'loop bit clear: no replay of the same clip')
    # Replay of the same AnimID/SubAnim through 0xa67d8 with flags 1 (r27 != 0) or 9 (r27 == 0).
    require(compare_immediate(app.w(0xa7448)), (11, 0, 27, 0), 'r27 tested')
    bc(app, 0xa744c, (BT, CR_EQ, 0xa7458), 'r27 == 0 selects flag 8')
    d(app, 0xa7450, (ADDI, 0, 0, 0), 'r27 != 0: no flag 8')
    d(app, 0xa7458, (ADDI, 0, 0, 8), 'r27 == 0: flag 8')
    d(app, 0xa745c, (LWZ, 4, 26, 4), 'same AnimID')
    d(app, 0xa7464, (LWZ, 5, 26, 8), 'same SubAnim')
    d(app, 0xa746c, (ORI, 0, 7, 1), 'flags | 1 (loop)')
    require(app.call(0xa7470), 0xa67d8, 'replay call')
    # 0xa67d8: carry = time - duration, bind only when flags & 0xC == 0, old clip = current clip.
    d(app, 0xa68f4, (LFS, 1, 25, 32), 'replay: AnimFrame')
    d(app, 0xa68f8, (LFS, 0, 25, 28), 'replay: TotalAnimFrames')
    bc(app, 0xa6900, (BF, CR_GT, 0xa6910), 'not past the end: no carry')
    fp(app, 0xa6904, (FSUBS, 31, 1, 0, 0), 'carry = AnimFrame - TotalAnimFrames')
    d(app, 0xa6948, (LWZ, 4, 3, 4), 'old clip = current clip entry +4')
    rot(app, 0xa6994, (RLWINM, 26, 0, 0, 28, 29, 1), 'replay flags 0xC')
    bc(app, 0xa6998, (BF, CR_EQ, 0xa69a8), 'flag 4 or 8: no bind')
    d(app, 0xa69a0, (ADDI, 5, 30, 0), 'new clip')
    require(app.call(0xa69a4), 0xa5894, 'bind call')
    xop(app, 0xa69ac, (63, 72, 1, 0, 31), 'start time argument = carry')
    require(app.call(0xa69c4), 0xa6398, 'set-clip call')
    # 0xa6398 caps the start time at the duration.
    xop(app, 0xa641c, (63, 32, 0, 1, 0), 'start time against duration')
    bc(app, 0xa6420, (BF, CR_GT, 0xa6428), 'within the duration: kept')
    xop(app, 0xa6424, (63, 72, 1, 0, 0), 'else capped to the duration')
    # The object-list update passes r4 = 0 (flag 8); other updates pass 1.
    for site, r4, r5 in ((0x4d39c, 1, 0), (0x4d420, 0, 8), (0x4d4b0, 1, 4)):
        require(app.call(site), 0xa7960, f'object update call {site:#x}')
        d(app, site - 8, (ADDI, 4, 0, r4), f'second argument at {site:#x}')
        d(app, site - 4, (ADDI, 5, 0, r5), f'third argument at {site:#x}')
    # With r27 == 0 the channel samples only when object flag 0x00400000 is set.
    rot(app, 0xa7644, (RLWINM, 0, 0, 0, 9, 9, 1), 'object flag 0x00400000')
    bc(app, 0xa7648, (BF, CR_EQ, 0xa7668), 'set: sample')
    bc(app, 0xa7650, (BT, CR_EQ, 0xa76f4), 'r27 == 0: no sample')
    # Mesh-record flag 0x00800000 is cleared only by the bind and the frame-capture loop 0x4f584.
    clears = [offset for offset in range(0, len(app.code) - 3, 4)
              if rotate(app.w(offset))[0] == RLWINM and rotate(app.w(offset))[3:6] == (0, 9, 7)]
    require(clears, [0x2234c, 0x4d2c0, 0x4fa4c, 0x4fa58, 0x4fa64, 0x4fa70, 0x4fa7c, 0x4fa88, 0x4fa94, 0x4faa0,
                     0x4fac0, 0x5d310, 0x12bc90, 0x12bcb8], 'rlwinm clears of bit 0x00800000')
    d(app, 0x4fa2c, (LWZ, 4, 26, 112), 'frame-capture loop walks the mesh records')
    d(app, 0x4fac8, (ADDI, 4, 4, 160), 'mesh record stride')
    d(app, 0x5d30c, (LWZ, 0, 31, 48), '0x5d310 clears a header flag word, not a mesh record')
    # Copy-back gate in the bind: skipped when r27 bit 0 is set and header flag 0x4 is clear.
    rot(app, 0xa58c8, (RLWINM, 0, 8, 0, 28, 28, 1), 'object flag 0x8')
    d(app, 0xa58cc, (LWZ, 27, 7, 16396), 'global option word')
    bc(app, 0xa58dc, (BF, CR_EQ, 0xa58e8), 'object flag 0x8 set')
    rot(app, 0xa58e0, (RLWINM, 0, 0, 0, 11, 11, 1), 'object flag 0x00100000')
    bc(app, 0xa58e4, (BT, CR_EQ, 0xa594c), '0x00100000 clear: option word unchanged')
    bc(app, 0xa58ec, (BT, CR_EQ, 0xa5948), '0x8 clear and 0x00100000 set: set bit 0')
    require(branch_target(app.w(0xa5944), 0xa5944, link=False), 0xa594c, 'flag 0x8 path skips the set')
    d(app, 0xa5948, (ORI, 27, 27, 1), 'option bit 0 forced')
    rot(app, 0xa5a5c, (RLWINM, 27, 0, 0, 31, 31, 1), 'option bit 0')
    bc(app, 0xa5a60, (BT, CR_EQ, 0xa5a70), 'clear: copy back')
    d(app, 0xa5a64, (LWZ, 0, 24, 48), 'header flags')
    bc(app, 0xa5a6c, (BT, CR_EQ, 0xa5d24), 'set and header flag 0x4 clear: skip the copy-back')
    bc(app, 0xa5d20, (BF, CR_EQ, 0xa5b98), 'copy-back loop ends before 0xa5d24')
    # Relative-animation models: per-update restore through the bind without a new clip, then normals.
    d(app, 0xa79e8, (LWZ, 0, 3, 48), 'object update: header flags')
    rot(app, 0xa79ec, (RLWINM, 0, 0, 0, 29, 29, 1), 'header flag 0x4')
    bc(app, 0xa79f0, (BT, CR_EQ, 0xa7a44), 'clear: no restore')
    d(app, 0xa7a28, (ADDI, 5, 0, 0), 'no new clip')
    require(app.call(0xa7a30), 0xa5894, 'restore through the bind')
    d(app, 0xa7b20, (LWZ, 0, 27, 48), 'after the channels: header flags')
    rot(app, 0xa7b24, (RLWINM, 0, 0, 0, 29, 29, 1), 'header flag 0x4')
    bc(app, 0xa7b28, (BT, CR_EQ, 0xa7b68), 'clear: no normal recomputation here')
    rot(app, 0xa7b3c, (RLWINM, 0, 0, 0, 15, 15, 1), 'mesh flag 0x00010000 (after a vertex pass)')
    require(app.call(0xa7b48), 0xa772c, 'face-normal recomputation')
    d(app, 0xa7750, (LWZ, 30, 3, 112), 'faces at mesh record +112')
    d(app, 0xa7754, (LHZ, 31, 3, 92), 'face count at mesh record +92')
    d(app, 0xa778c, (LWZ, 11, 28, 96), 'positions at mesh record +96')
    d(app, 0xa779c, (LWZ, 8, 28, 100), 'normals at mesh record +100')
    recompute = app.calls_to(0xa772c)
    require(recompute, [0xa7924, 0xa7b48, 0x19ac0c], 'face-normal recomputation call sites')
    require(app.calls_to(0xa78ec), [0x567e4, 0x5a4b4], 'all-mesh recomputation call sites')
    rot(app, 0x19abf8, (RLWINM, 3, 0, 0, 3, 3, 1), 'renderer: mesh flag 0x10000000')
    rot(app, 0x19ac00, (RLWINM, 3, 0, 0, 15, 15, 1), 'renderer: mesh flag 0x00010000')
    return {'replay': 'AnimFrame > TotalAnimFrames, no deferred clip, object flags 0x18 clear, channel bit 0x1: '
                      'same clip through 0xa67d8 starting at min(time - duration, duration)',
            'replay_flags': {'object_update_r4_nonzero': 1, 'object_update_r4_zero': 9},
            'cursor_reset': 'bind 0xa5894 (flags & 0xC == 0) or frame-capture loop 0x4f584',
            'object_list_update_r4_zero': '0x4d420 (flag 8; samples only with object flag 0x00400000)',
            'copy_back_when': 'header flag 0x4, or option bit 0 clear and not (object 0x8 clear and 0x00100000 set)',
            'normal_recompute_sites': recompute,
            'relative_animation_update': 'restore via 0xa5894 without a new clip, add, recompute face normals'}



STFS, LFD, FDIVS = 52, 50, 18


def md2_clip_clock(app: Image) -> dict:
    """Channel clock: whole-millisecond start, single-precision frame, carry truncated on replay."""
    # 0xa6484 UpdateTime: AnimFrame = speed * (30 * (float)(unsigned)(now - start) / 1000).
    d(app, 0xa649c, (LWZ, 5, 3, 16), 'StartAnimTime (+16)')
    d(app, 0xa64a4, (LWZ, 4, 3, 20), 'AnimTime (+20)')
    xop(app, 0xa64ac, (31, 40, 4, 5, 4), 'now - start (integer milliseconds)')
    d(app, 0xa64a0, (LIS, 0, 0, 0x4330), 'conversion magic high word')
    op, target, slot = app.toc_slot(0xa64a8, 2)
    require((op, target), (LFD, None), 'conversion bias is a TOC literal')
    require(struct.unpack_from('>Q', app.data, slot)[0], 0x4330000000000000, 'bias 2^52: unsigned conversion')
    fp(app, 0xa64c8, (FSUBS, 1, 1, 2, 0), 'elapsed rounded to single')
    fp(app, 0xa64cc, (FMULS, 1, 3, 0, 1), '30 * elapsed')
    fp(app, 0xa64d0, (FDIVS, 0, 1, 0, 0), '/ 1000')
    fp(app, 0xa64d4, (FMULS, 0, 4, 0, 0), '* speed (+12)')
    d(app, 0xa64d8, (STFS, 0, 3, 32), 'AnimFrame (+32)')
    # 0xa6398 SetClip(player, clip, fresh r5, start time f1).
    require(compare_immediate(app.w(0xa63c4)), (11, 0, 5, 0), 'fresh-start argument tested')
    bc(app, 0xa63e4, (BT, CR_EQ, 0xa6418), 'r5 == 0: start from the carried time')
    d(app, 0xa63fc, (LWZ, 0, 7, 16400), 'fresh start: scene clock')
    d(app, 0xa6400, (STW, 0, 31, 16), 'StartAnimTime = clock')
    d(app, 0xa6410, (STW, 0, 31, 20), 'AnimTime = clock (frame 0)')
    fp(app, 0xa6430, (FMULS, 1, 0, 0, 1), '1000 * carry')
    fp(app, 0xa6438, (FDIVS, 1, 1, 2, 0), '/ 30')
    d(app, 0xa6434, (LFS, 0, 31, 12), 'speed')
    fp(app, 0xa643c, (FDIVS, 1, 1, 0, 0), '/ speed')
    require(app.call(0xa6440), 0x1c3fbc, 'double to unsigned conversion of the carry')
    xop(app, 0x1c3ff4, (63, 15, 2, 0, 2), 'fctiwz: truncation toward zero')
    d(app, 0xa6444, (LWZ, 0, 31, 20), 'AnimTime of the last update')
    xop(app, 0xa6448, (31, 40, 0, 3, 0), 'start = now - carry milliseconds')
    d(app, 0xa644c, (STW, 0, 31, 16), 'StartAnimTime')
    # 0xa67d8 passes fresh = 0 only with a carry and recomputes AnimFrame from the new start.
    d(app, 0xa6908, (ADDI, 28, 0, 0), 'carry: fresh = 0')
    d(app, 0xa6910, (ADDI, 28, 0, 1), 'no carry: fresh = 1')
    xop(app, 0xa69bc, (31, 444, 28, 5, 28), 'fresh passed as r5')
    d(app, 0xa69c8, (LWZ, 0, 22, 4), 'object flags')
    rot(app, 0xa69cc, (RLWINM, 0, 0, 0, 16, 16, 1), 'object flag 0x8000')
    bc(app, 0xa69d0, (BF, CR_EQ, 0xa6a14), 'set: no recomputation')
    xop(app, 0xa69e4, (31, 40, 3, 4, 3), 'now - start after the replay')
    fp(app, 0xa6a04, (FMULS, 1, 3, 0, 1), '30 * elapsed')
    fp(app, 0xa6a08, (FDIVS, 0, 1, 0, 0), '/ 1000')
    d(app, 0xa6a10, (STFS, 0, 25, 32), 'AnimFrame after the replay')
    return {'frame': 'speed * (30 * (float)(unsigned)(now - start) / 1000), single precision; now/start whole ms',
            'replay_start': 'now - (unsigned)trunc(1000 * min(carry, duration) / 30 / speed)'}


# ------------------------------------------------------------------ TPWS
def tpws_writer(app: Image) -> dict:
    base, first = app.toc_string(0x11cb84, 31)
    require(first.startswith('Cannot have header-only save'), True, 'writer label table')
    sequence = []
    pending_call = None
    highs = {}
    offset = 0x11cb60
    while offset < 0x11db44:
        w = app.w(offset)
        op, rt, ra, imm = d_form(w)
        if w >> 26 == 18 and w & 3 == 1:
            target = branch_target(w, offset)
            if target not in (0x5fbc8, 0x5fbcc, 0x11db8c, 0x11db84) and target < 0x1c4000:
                pending_call = target
        elif op == LIS and ra == 0:
            highs[rt] = imm
        elif op == ADDI and rt == 0 and ra in highs:
            tag = ((highs[ra] << 16) + imm) & 0xffffffff
            text = tag.to_bytes(4, 'big')
            if all(65 <= b <= 90 for b in text):
                sequence.append({'tag_constant': text.decode('ascii'),
                                 'file_bytes': tag.to_bytes(4, 'little').decode('ascii'),
                                 'writer_call': pending_call})
        elif op == ADDI and ra == 31 and rt == 4 and sequence:
            label = cstring(app.code, base + imm)
            if 'saved %d bytes' in label:
                sequence[-1].setdefault('log_label', label.split(':')[0])
        offset += 4
    expected_tags = ['WRLD', 'SPSC', 'PART', 'MESS', 'CLOK', 'VANT', 'GSYS', 'RSYS', 'TRAK', 'FLYR',
                     'RSSE', 'KAME', 'COAS', 'ADVS', 'SOUN', 'CHTS', 'ADSC']
    require([s['tag_constant'] for s in sequence], expected_tags, 'delimiter order')
    require(app.call(0x11cbc4), 0x10bc1c, 'action record writer before world')
    require(app.call(0x11cc28), 0x105d3c, 'world writer before WRLD delimiter')
    require(app.call(0x11db40), 0x18f738, 'untagged final block')
    require(app.glue_symbol(app.call(0x127adc)), 'LbTime_GetClock__Fv', 'VanillaTime value source')
    d(app, 0x127afc, (ADDI, 6, 0, 4), 'VanillaTime writes four bytes')
    require(helper_width(app, 0x127ac8), 4, 'VanillaTime block width')
    return {'writer': 0x11cb60, 'action_record_writer': 0x10bc1c, 'world_writer': 0x105d3c,
            'sections': sequence, 'final_untagged_writer': 0x18f738}


def helper_width(app: Image, helper: int) -> int:
    """Byte count passed to the first LbFile_Write in a save helper."""
    offset = helper
    width = None
    while offset < helper + 0x200:
        w = app.w(offset)
        op, rt, ra, imm = d_form(w)
        if op == ADDI and rt == 6 and ra == 0:
            width = imm
        if w >> 26 == 18 and w & 3 == 1:
            target = branch_target(w, offset)
            if target >= 0x1c4000 and app.glue_symbol(target) == 'LbFile_Write__FPvPCvUlPUl':
                if width is None:
                    raise WitnessError(f'no write width in helper {helper:#x}')
                return width
        offset += 4
    raise WitnessError(f'no write in helper {helper:#x}')


def labelled_fields(app: Image, start: int, end: int) -> list[tuple[str, int]]:
    """(label, written width) for each save-helper call preceded by `addi r5, rBase, label`."""
    bases = {}
    fields = []
    label = None
    for offset in range(start, end, 4):
        w = app.w(offset)
        op, rt, ra, imm = d_form(w)
        if op == LWZ and ra == 2:
            target = app.relocs.get(app.toc + imm)
            if target and target.kind == 'section' and target.target == app.container.code.index:
                bases[rt] = target.addend
            else:
                bases.pop(rt, None)
        elif op == ADDI and rt == 5 and ra in bases:
            label = cstring(app.code, bases[ra] + imm).strip()
        elif w >> 26 == 18 and w & 3 == 1 and label is not None:
            fields.append((label, helper_width(app, branch_target(w, offset))))
            label = None
    return fields


def tpws_schema(app: Image) -> dict:
    world = labelled_fields(app, 0x105d3c, 0x10620c)
    action = labelled_fields(app, 0x10bc1c, 0x10bcb0)
    cell_base = labelled_fields(app, 0xcd408, 0xcd590)
    map_cell = labelled_fields(app, 0xd8048, 0xd81b0)
    track_cell = labelled_fields(app, 0xfbd54, 0xfbdc0)
    region = labelled_fields(app, 0xefe54, 0xefec0)
    require(app.call(0xd806c), 0xcd408, 'map cell writes the shared cell base first')
    require(app.call(0xfbd78), 0xcd408, 'track cell writes the shared cell base first')
    sizes = {name: sum(width for _, width in fields) for name, fields in
             (('cell_base', cell_base), ('map_cell_own', map_cell), ('track_cell_own', track_cell))}
    record = 1 + 2 * sizes['cell_base'] + sizes['map_cell_own'] + sizes['track_cell_own']
    return {'action_record': action, 'world_vars': world, 'cell_base': cell_base, 'map_cell': map_cell,
            'track_cell': track_cell, 'region_effect_entry': region, 'sizes': sizes,
            'map_and_track_record_bytes': record}


def tpws_cells(app: Image) -> dict:
    for site, bit in ((0xd6d7c, 1), (0xd6db0, 2), (0xd6dd4, 4)):
        d(app, site, (24, 0, 0, bit), f'cell status bit {bit}')  # ori r0, r0, bit (rs, ra swapped in field view)
    cmp = compare_immediate(app.w(0xd6eb4))
    require((cmp[0], cmp[3]), (11, 16384), 'cell count')
    d(app, 0xd6ebc, (ADDI, 29, 29, 68), 'map cell stride in memory')
    d(app, 0xd6eb0, (ADDI, 28, 28, 40), 'track cell stride in memory')
    d(app, 0xd6eb8, (ADDI, 27, 27, 10), 'region-effect cell stride')
    require(app.call(0xd6e20), 0xd8048, 'map cell writer')
    d(app, 0xd8144, (ADDI, 4, 27, 38), 'saved mStatusFlags is map cell +38 (the MAP byte destination)')
    require(app.call(0xd6e58), 0xfbd54, 'track cell writer')
    require(app.call(0xd6e94), 0xefe54, 'region-effect writer')
    require(compare_immediate(app.w(0xefecc))[3], 5, 'region-effect entries per cell')
    d(app, 0xefec8, (ADDI, 30, 30, 2), 'region-effect entry stride')
    return {'map_save': 0xd6c90, 'region_effect_entries': 5, 'status_bits': {'1': 'map cell', '2': 'track cell', '4': 'region-effect cell'},
            'cells': 16384}


# ------------------------------------------------------------------- MAP
def tp2m(app: Image) -> dict:
    file_tag = lis_addi_constant(app, 0x107f24, 0x107f38, 0)
    chunk_tag = lis_addi_constant(app, 0x107f60, 0x107f64, 6)
    d(app, 0xd7e70, (ADDI, 7, 4, 28), 'cells follow 28-byte chunk header')
    for site in (0xd7f78, 0xd7f88):
        require(compare_immediate(app.w(site))[3], 128, f'grid bound at {site:#x}')
    d(app, 0xd7fb8, (7, 4, 4, 68), 'map cell stride')  # mulli
    d(app, 0xd7fbc, (ADDI, 4, 4, 38), 'attribute byte at cell+38')
    # The five values after width/height are swapped into stack slots that are never loaded.
    stores = set()
    loads = set()
    for offset in range(0xd7e6c, 0xd7fe4, 4):
        op, rt, ra, imm = d_form(app.w(offset))
        if ra == 1 and op == 36:
            stores.add(imm)
        if ra == 1 and op == LWZ:
            loads.add(imm)
    dead = sorted(slot for slot in (-60, -64, -76, -88, -100) if slot in stores and slot not in loads)
    require(dead, [-100, -88, -76, -64, -60], 'unused header value slots')
    callers = app.calls_to(0x107f1c)
    require(callers, [0x4db74], 'TP2M loader call sites')
    fmt_base, _ = app.toc_string(0x4db3c, 4)
    path_format = cstring(app.code, fmt_base + d_form(app.w(0x4db48))[3])
    return {'file_tag_bytes': file_tag.to_bytes(4, 'little').decode('ascii'),
            'chunk_tag_bytes': chunk_tag.to_bytes(4, 'little').decode('ascii'),
            'handler': 0xd7e6c, 'unused_header_words': 5, 'loader_call_sites': callers,
            'path_format': path_format}


# ------------------------------------------------------------------- BF4
def bf4(ltms: Image) -> dict:
    ctor = ltms.vector('__ct__14TbIRLE4bitFontFPUcP16TbIResourceIndex')
    require(ctor, 0x2f80, 'font constructor')
    widths = {}
    for site in range(0x3020, 0x3060, 8):
        _, rt, ra, imm = d_form(ltms.w(site))
        target = ltms.call(site + 4)
        widths[imm] = {0x30f4: 2, 0x30e8: 4}[target]
    require(widths, {0: 2, 2: 2, 4: 4, 8: 4, 12: 4, 16: 2, 18: 2, 22: 2}, 'glyph record swaps')
    draw = ltms.vector('DrawCharTo8888__31TbOneColour4BitFontRenderMethodFR12TbRenderAreaRC31C4BitFontRenderMethodParameters')
    require(draw, 0xb41c, 'one-colour 8888 draw')
    divisor = lis_addi_constant(ltms, 0xb4dc, 0xb4f4, 27)
    require(divisor, 0x88888889, 'divide-by-15 multiplier')
    high = x_form(ltms.w(0xb574))
    require((high[0], high[4]), (31, 11), 'unsigned multiply-high')
    shift = rotate(ltms.w(0xb57c))
    require((shift[0], shift[3], shift[4], shift[5]), (RLWINM, 29, 3, 31), 'shift right 3')
    alpha = lis_addi_constant(ltms, 0xb60c, 0xb628, 21)
    return {'record_field_widths': {str(k): v for k, v in widths.items()}, 'unswapped_bytes': [20, 21],
            'draw_8888': draw, 'divide_by_15_multiplier': divisor, 'divide_by_255_multiplier': alpha}


# ------------------------------------------------------- negative searches
def negative_searches(root: Path) -> dict:
    containers = [root / 'SimThemePark.data'] + sorted((root / 'libraries').glob('*.data'))
    patterns = {'.mtr': (b'.mtr', b'.MTR', b'.Mtr'), 'MTR magic': (b'\xaf\x15\x59\x2e', b'\x2e\x59\x15\xaf'),
                'TQI/TGQ chunk tags': (b'pIQT', b'TQIp', b'SCHl', b'lHCS')}
    found = {name: [] for name in patterns}
    for path in containers:
        container = pef.PEFContainer(path.read_bytes(), path.name)
        for section in container.sections:
            if section.kind not in (0, 1, 2, 3):
                continue
            blob = bytes(section.data)
            for name, needles in patterns.items():
                if any(n in blob for n in needles):
                    found[name].append(path.name)
    return {'containers_searched': len(containers), 'matches': found}


def inspect(root: Path) -> dict:
    app = Image(root / 'SimThemePark.data')
    engine = Image(root / 'libraries/engine_shared.data')
    ltms = Image(root / 'libraries/ltms_shared.data')
    app.toc = app.relocs[app.container.main[1] + 4].addend
    result = {'identities': IDENTITIES,
              'md2_loader': md2_loader(engine),
              'md2_trailer': md2_trailer_and_records(engine),
              'md2_vertex_packing': md2_vertex_packing(engine),
              'md2_runtime': md2_runtime(app),
              'md2_runtime_binding': md2_runtime_binding(app),
              'md2_clip_lifecycle': md2_clip_lifecycle(app),
              'md2_clip_clock': md2_clip_clock(app),
              'tpws_writer': tpws_writer(app),
              'tpws_schema': tpws_schema(app),
              'tpws_cells': tpws_cells(app),
              'tp2m': tp2m(app),
              'bf4': bf4(ltms),
              'negative_searches': negative_searches(root)}
    return result


ADDRESS_KEYS = {'code_entry', 'normal_recompute_sites', 'ride_loader_call_sites', 'bind_clear_mask', 'relative', 'set_mode', 'vertex_sampler', 'vertex_group_dispatch', 'record_sampler', 'key_search',
                'loader_call_sites', 'writer', 'action_record_writer', 'world_writer', 'writer_call',
                'final_untagged_writer', 'map_save', 'handler', 'draw_8888', 'conversion_code',
                'divide_by_15_multiplier', 'divide_by_255_multiplier', 'magic'}


def hexify(value, key: str = ''):
    """Render code offsets and constants as hexadecimal strings for review."""
    if isinstance(value, dict):
        return {k: hexify(v, k) for k, v in value.items()}
    if isinstance(value, list):
        return [hexify(v, key) for v in value]
    if isinstance(value, int) and not isinstance(value, bool) and key in ADDRESS_KEYS:
        return f'{value:#x}'
    return value


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('bin_root', type=Path)
    args = parser.parse_args()
    try:
        result = inspect(args.bin_root)
    except (OSError, KeyError, pef.PEFError) as error:
        parser.exit(1, f'format witness: {error}\n')
    print(json.dumps(hexify(result), indent=2, sort_keys=True))


if __name__ == '__main__':
    main()

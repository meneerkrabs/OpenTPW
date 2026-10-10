"""ANIM-R witness: animation record kinds, script animation opcodes and advisor clip starts (Feral Mac build).

Usage: python3 -I anim_witness.py /path/to/mac-feral/bin

Every check names one instruction (or one data-section word) of an identity-pinned
container and the exact interpreted fields it must have: opcode, registers, displacement
or immediate, rotate mask, call target, TOC float or switch-table target. A check passes
only when the instruction word equals the word those fields encode, so any changed operand
(mask bit, displacement, immediate, register, target) fails. Nothing is executed; the
output is interpreted JSON (offsets, fields, masks, values), never instruction bytes or
disassembly text. Mac static evidence; it does not qualify the PC build.
"""
from __future__ import annotations

import hashlib
import json
import struct
import sys
from dataclasses import dataclass
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[2]))

import pef  # noqa: E402

APP = 'SimThemePark.data'
ENGINE = 'engine_shared.data'
IDENTITIES = {
    APP: '04809cd4ccee5433c7fb0b7c93d32f6a7aa629c1849181c0b7906415e5e295f5',
    ENGINE: 'c549123f647dcf33c3e2c3d515bffe2cfcb19d11680f743f02ca42bb09dbd73b',
}
TOC_BASE = 0x8000  # both containers (PPC-formats.md)
SWITCH_TABLE = 0x3f2a4  # RSE dispatcher table, data section of SimThemePark (PPC-rides.md)


class WitnessError(Exception):
    pass


# ------------------------------------------------------------------ encoders
OPS = {'mulli': 7, 'cmpli': 10, 'cmpi': 11, 'addi': 14, 'bc': 16, 'b': 18, 'rlwinm': 21, 'ori': 24, 'oris': 25,
       'andi.': 28, 'x': 31, 'lwz': 32, 'stw': 36, 'lhz': 40, 'lfs': 48, 'lfd': 50, 'stfs': 52, 'xoris': 27}


def d_word(op: str, a: int, b: int, imm: int) -> int:
    """D-form: bits 21-25 = a (rt/rs/frt, or crf << 2 for compares), 16-20 = b (ra), low 16 = imm."""
    return (OPS[op] << 26) | (a << 21) | (b << 16) | (imm & 0xffff)


def rlwinm_word(rs: int, ra: int, sh: int, mb: int, me: int, record: bool) -> int:
    return (21 << 26) | (rs << 21) | (ra << 16) | (sh << 11) | (mb << 6) | (me << 1) | int(record)


def x_word(rt: int, ra: int, rb: int, xo: int, record: bool = False) -> int:
    return (31 << 26) | (rt << 21) | (ra << 16) | (rb << 11) | (xo << 1) | int(record)


def bl_word(at: int, target: int) -> int:
    displacement = target - at
    if not -0x2000000 <= displacement < 0x2000000 or displacement % 4:
        raise WitnessError(f'call {at:#x} -> {target:#x} not encodable')
    return (18 << 26) | (displacement & 0x03fffffc) | 1


def rotate_mask(mb: int, me: int) -> int:
    bits, i = 0, mb
    while True:
        bits |= 1 << (31 - i)
        if i == me:
            return bits
        i = (i + 1) & 31


# -------------------------------------------------------------------- checks
@dataclass(frozen=True)
class Check:
    """One instruction (kind 'word') or data word ('toc_float', 'switch') with its expected fields."""
    fact: str
    container: str
    offset: int
    kind: str
    fields: tuple
    meaning: str

    def expected_word(self) -> int:
        k, f = self.kind, self.fields
        if k == 'd':
            return d_word(*f)
        if k == 'rlwinm':
            return rlwinm_word(*f)
        if k == 'x':
            return x_word(*f)
        if k == 'bl':
            return bl_word(self.offset, f[0])
        if k == 'toc_float':
            return d_word('lfs', f[0], 2, f[1] - TOC_BASE)
        raise WitnessError(f'{self.kind} has no instruction word')

    def interpreted(self) -> dict:
        k, f = self.kind, self.fields
        out = {'fact': self.fact, 'container': self.container, 'offset': hex(self.offset), 'meaning': self.meaning}
        if k == 'd':
            out.update(op=f[0], a=f[1], b=f[2], immediate=f[3])
        elif k == 'rlwinm':
            out.update(op='rlwinm' + ('.' if f[5] else ''), rs=f[0], ra=f[1], shift=f[2],
                       mask=hex(rotate_mask(f[3], f[4])))
        elif k == 'x':
            out.update(op='x-form', rt=f[0], ra=f[1], rb=f[2], extended=f[3])
        elif k == 'bl':
            out.update(op='bl', target=hex(f[0]))
        elif k == 'toc_float':
            out.update(op='lfs', register=f[0], toc_slot=hex(f[1]), value=f[2])
        elif k == 'switch':
            out.update(opcode=f[0], handler=hex(f[1]), name=f[2])
        return out


def C(fact, offset, kind, fields, meaning, container=APP):
    return Check(fact, container, offset, kind, tuple(fields), meaning)


CHECKS: tuple[Check, ...] = (
    # ---- record +44 / flag 0x10000: per-corner UV animation (consumer 0xa83e8) -----------------
    C('uv_track', 0xa56bc, 'd', ('lwz', 0, 28, 4), 'sampler reads record flags (+4)'),
    C('uv_track', 0xa56c0, 'rlwinm', (0, 0, 0, 15, 15, True), 'record flag 0x10000 selects the UV pass'),
    C('uv_track', 0xa56d8, 'bl', (0xa83e8,), 'UV pass 0xa83e8(player, record, add)'),
    C('uv_gate', 0xa5698, 'd', ('lwz', 0, 31, 16396), 'global option word (+16396)'),
    C('uv_gate', 0xa569c, 'rlwinm', (0, 0, 0, 27, 27, True), 'option bit 0x10 skips the UV pass (unless relative)'),
    C('uv_gate', 0xa56a4, 'd', ('lwz', 0, 27, 0), 'node-state word (+0)'),
    C('uv_gate', 0xa56a8, 'rlwinm', (0, 0, 0, 17, 17, True), 'node-state bit 0x4000 skips the UV pass (unless relative)'),
    C('uv_gate', 0xa56b4, 'rlwinm', (0, 0, 0, 29, 29, True), 'header flag 0x4 (relative) forces the UV pass'),
    C('uv_track', 0xa8404, 'd', ('lwz', 29, 4, 60), 'record +60: bound node state (mesh record)'),
    C('uv_track', 0xa8408, 'd', ('lwz', 30, 4, 44), 'record +44: UV block'),
    C('uv_track', 0xa83f0, 'd', ('cmpi', 0, 5, 0), 'add mode (header flag 0x4) chooses add over set'),
    C('uv_track', 0xa8420, 'd', ('lwz', 4, 30, 4), 'block +4: per-corner (first key, key count) u16 pairs'),
    C('uv_track', 0xa8428, 'd', ('lwz', 9, 30, 12), 'block +12: u16 key ticks'),
    C('uv_track', 0xa8454, 'd', ('lwz', 9, 30, 16), 'block +16: (u, v) float pairs'),
    C('uv_track', 0xa84cc, 'd', ('lwz', 0, 30, 0), 'block +0: corner count (loop bound)'),
    C('uv_track', 0xa8460, 'bl', (0xa8304,), 'key search 0xa8304'),
    C('uv_track', 0xa8474, 'd', ('lwz', 4, 29, 104), 'destination: mesh record +104 (texture coordinates)'),
    C('uv_track', 0xa847c, 'rlwinm', (31, 0, 3, 0, 26, False), 'corner i -> 32-byte block (i / 4) * 32'),
    C('uv_track', 0xa8488, 'rlwinm', (31, 0, 2, 28, 29, False), 'corner i -> lane (i % 4) * 4'),
    C('uv_track', 0xa84c0, 'd', ('lfs', 0, 4, 16), 'V lane at block +16 (U at +0)'),
    C('uv_track', 0xa8410, 'toc_float', (31, 0x51d0, 1.0), 'interpolation weight complement 1.0'),
    C('uv_search', 0xa834c, 'x', (0, 3, 0, 32), 'cmplw tick, ticks[i]: first key with tick < ticks[i]'),
    C('uv_search', 0xa8354, 'd', ('addi', 3, 4, -1), 'key = i - 1'),
    C('uv_search', 0xa83c4, 'd', ('addi', 0, 28, -2), 'no later key: key = count - 2'),
    C('uv_search', 0xa83cc, 'toc_float', (0, 0x51d0, 1.0), '... with fraction 1.0'),
    # ---- record +40 with flag 0x4000: the 12-byte block ---------------------------------------
    C('block12_loader', 0x41ac4, 'd', ('lwz', 0, 3, 4), 'engine record relocation reads record flags',
      ENGINE),
    C('block12_loader', 0x41ac8, 'rlwinm', (0, 0, 0, 17, 17, True), 'flag 0x4000 selects the 12-byte block', ENGINE),
    C('block12_loader', 0x41b64, 'd', ('mulli', 4, 0, 3), 'swaps count x 3 words at block +8', ENGINE),
    C('block12_loader', 0x41b68, 'bl', (0x425c4,), 'USwapBlock32 glue', ENGINE),
    C('record_bind', 0xa3e44, 'd', ('lwz', 29, 3, 4), 'bind: geometry header = object header +4'),
    C('record_bind', 0xa3e48, 'd', ('lwz', 30, 4, 44), 'bind: clip record table (trailer +44)'),
    C('record_bind', 0xa3e54, 'rlwinm', (0, 0, 0, 29, 29, True), 'object-header bit 0x4: bind by mesh +80 index'),
    C('record_bind', 0xa3e98, 'd', ('lhz', 4, 5, 80), 'mesh record +80 index'),
    C('record_bind', 0xa3e9c, 'd', ('lhz', 0, 30, 20), 'record +20 node index'),
    C('record_bind', 0xa3ea8, 'd', ('stw', 5, 30, 60), 'record +60 = matched mesh record'),
    C('record_bind', 0xa3ed0, 'd', ('mulli', 0, 0, 160), 'direct mode: mesh record index x 160'),
    C('record_bind', 0xa3ee8, 'd', ('mulli', 0, 0, 88), '... or dummy record x 88'),
    C('record_bind', 0xa3ef0, 'd', ('stw', 0, 30, 60), 'record +60 = node record'),
    C('block12_bind', 0xa3ef4, 'd', ('lwz', 0, 30, 4), 'bind reads record flags'),
    C('block12_bind', 0xa3ef8, 'rlwinm', (0, 0, 0, 17, 17, True), 'flag 0x4000'),
    C('block12_bind', 0xa3f04, 'd', ('lwz', 3, 30, 40), 'record +40: the 12-byte block'),
    C('block12_bind', 0xa3f08, 'd', ('lhz', 4, 4, 88), 'swap count = node position count'),
    C('block12_bind', 0xa3f24, 'd', ('ori', 0, 0, 0x800), 'node state |= 0x800'),
    C('block12_bind', 0xa3f38, 'rlwinm', (0, 0, 0, 24, 22, False), 'node state &= ~0x100 (category != 0)'),
    C('block12_bind', 0xa7cdc, 'bl', (0xa3e24,), 'channel reset binds every clip (0xa3e24)'),
    C('block12_bind', 0xa7c88, 'd', ('ori', 0, 0, 0x100), 'channel reset sets node bit 0x100 on every node'),
    C('block12_clear', 0xa7140, 'rlwinm', (4, 0, 0, 23, 23, True), 'node bit 0x100 ...'),
    C('block12_clear', 0xa7148, 'rlwinm', (4, 0, 0, 21, 19, False), '... clears node bit 0x800'),
    C('block12_clear', 0xa7ac0, 'rlwinm', (3, 0, 0, 23, 23, True), 'object update: node bit 0x100 ...'),
    C('block12_clear', 0xa7ac8, 'rlwinm', (3, 0, 0, 21, 19, False), '... clears node bit 0x800'),
    C('block12_sampler', 0xa5048, 'rlwinm', (3, 0, 0, 17, 17, True), 'sampler skips vertex animation for 0x4000'),
    # ---- clip trigger 0xa6cc0 and start 0xa67d8 -----------------------------------------------
    C('trigger', 0xa6cec, 'd', ('mulli', 3, 8, 56), 'channel record = object +16 + channel x 56'),
    C('trigger', 0xa6d88, 'd', ('addi', 0, 28, -13), 'AnimID 13/14 never defer'),
    C('trigger', 0xa6d8c, 'd', ('cmpli', 0, 0, 1), '(AnimID - 13) <= 1 unsigned'),
    C('trigger', 0xa6d94, 'rlwinm', (30, 0, 0, 30, 30, True), 'trigger flag 0x2 defers behind a running clip'),
    C('trigger', 0xa6db0, 'd', ('stw', 28, 31, 36), 'DeferredAnimID (+36)'),
    C('trigger', 0xa6db4, 'd', ('stw', 29, 31, 40), 'DeferredSubAnim (+40)'),
    C('trigger', 0xa6db8, 'd', ('stw', 30, 31, 44), 'deferred flags (+44)'),
    C('trigger', 0xa6e0c, 'toc_float', (2, 0x5168, 33.33333206176758), 'remaining ms = (total - frame) x 33.333332'),
    C('trigger', 0xa6f04, 'd', ('addi', 31, 31, 1000), 'deferred clip missing: +1000 ms'),
    C('trigger', 0xa6f54, 'd', ('addi', 3, 0, 1000), 'start failed: returns 1000 ms'),
    C('start', 0xa6c2c, 'toc_float', (3, 0x5170, 1000.0), 'returned length = 1000 x ticks / 30'),
    C('start', 0xa6c38, 'toc_float', (0, 0x516c, 30.0), '... divided by 30 (no speed term)'),
    C('start', 0xa6808, 'rlwinm', (7, 0, 0, 31, 31, True), 'start flag 0x1 -> channel loop bit'),
    C('start', 0xa6830, 'rlwinm', (26, 0, 0, 29, 29, True), 'start flag 0x4 -> channel bit 0x20'),
    C('flush', 0xa6c90, 'd', ('mulli', 0, 4, 56), 'flush addresses one channel'),
    C('flush', 0xa6c9c, 'd', ('lwz', 0, 4, 36), 'reads DeferredAnimID only'),
    C('flush', 0xa6ca8, 'd', ('addi', 0, 0, 12), 'and resets it to 12 (none)'),
    C('flush', 0xa6cb0, 'd', ('stw', 0, 4, 36), 'store to +36; the current clip (+4) is untouched'),
    # ---- end of a non-looping clip (channel update 0xa7190) -----------------------------------
    C('clip_end', 0xa7498, 'd', ('addi', 4, 0, 14), 'non-looping end (no defer, no loop): restart as AnimID 14'),
    C('clip_end', 0xa68ac, 'd', ('ori', 0, 0, 0x14), 'AnimID 14: channel flags |= 0x14 (frozen at end, ended)'),
    C('clip_end', 0xa689c, 'd', ('ori', 0, 0, 0x2), 'AnimID 13: channel flags |= 0x2 (frozen at start)'),
    C('clip_end', 0xa694c, 'd', ('cmpi', 0, 27, 0), 'AnimID 13/14 branch past the bind: no copy-back'),
    # ---- two update routes for placed instances ----------------------------------------------
    C('instance_routes', 0x59f30, 'd', ('lwz', 26, 2, 0xae4 - TOC_BASE), 'placement: instance table (data 0x67c9c)'),
    C('instance_routes', 0x5a090, 'x', (3, 26, 0, 151), 'stwx: the new instance enters that table'),
    C('instance_routes', 0x5a3b8, 'd', ('lwz', 0, 2, 0x2c00 - TOC_BASE), 'placement: callback vector (data 0x6408)'),
    C('instance_routes', 0x5a3bc, 'd', ('stw', 0, 3, 320), 'stored in the instance render record +320'),
    C('instance_routes', 0x59ed8, 'd', ('addi', 4, 0, 1), 'callback: object update with r4 = 1 ...'),
    C('instance_routes', 0x59edc, 'bl', (0x4d280,), '... through 0x4d280 -> 0xa7960'),
    C('instance_routes', 0x59ee4, 'd', ('oris', 0, 0, 0x20), 'and marks the object 0x00200000'),
    C('instance_routes', 0x4d368, 'd', ('lwz', 30, 2, 0xae4 - TOC_BASE), 'object-list pass walks the instance table'),
    C('instance_routes', 0x4d3f8, 'rlwinm', (3, 0, 0, 10, 10, True), 'skipping (and clearing) marked objects'),
    C('instance_routes', 0x4d418, 'd', ('addi', 4, 0, 0), 'others: object update with r4 = 0 ...'),
    C('instance_routes', 0x4d41c, 'd', ('addi', 5, 0, 8), '... and r5 = 8 (replay flags 9: no bind)'),
    C('instance_routes', 0x4d420, 'bl', (0xa7960,), 'object update'),
    C('instance_routes', 0xa7644, 'rlwinm', (0, 0, 0, 9, 9, True), 'with r4 = 0 a channel samples only if object 0x00400000'),
    C('instance_routes', 0xa764c, 'd', ('cmpi', 0, 27, 0), 'r4 tested'),
    # ---- channel order and idle restart in the object update 0xa7960 --------------------------
    C('channel_order', 0xa7a48, 'd', ('lhz', 28, 29, 14), 'object channel count (+14)'),
    C('channel_order', 0xa7a5c, 'bl', (0xa7190,), 'channel update (samples the clip)'),
    C('channel_order', 0xa7a60, 'd', ('addi', 27, 27, 56), 'ascending channel order'),
    C('idle_restart', 0xa798c, 'd', ('andi.', 0, 0, 0x8004), 'only objects with +4 & 0x8004 clear'),
    C('idle_restart', 0xa79a8, 'd', ('addi', 4, 0, 5), 'restart AnimID 5 ...'),
    C('idle_restart', 0xa79ac, 'd', ('addi', 5, 0, 0), '... sub-animation 0'),
    C('idle_restart', 0xa79bc, 'd', ('cmpli', 0, 27, 3), 'at most 3 attempts per update'),
    C('idle_restart', 0xa79c8, 'bl', (0xa7000,), 'while the all-ended check (channel 0) is true'),
    # ---- RSE handlers ---------------------------------------------------------------------------
    C('rse_flush', 0xafa88, 'd', ('addi', 4, 0, 0), 'FLUSHANIM: channel 0'),
    C('rse_flush', 0xafa8c, 'bl', (0xa6c90,), 'FLUSHANIM -> deferred flush'),
    C('rse_flush', 0xb0130, 'x', (26, 4, 26, 444), 'FLUSHANIM_CH: channel operand'),
    C('rse_flush', 0xb0134, 'bl', (0xa6c90,), 'FLUSHANIM_CH -> deferred flush'),
    C('rse_trig', 0xafb0c, 'd', ('addi', 6, 0, 0), 'TRIGANIM flags 0'),
    C('rse_trig', 0xafb10, 'd', ('addi', 8, 0, 0), 'TRIGANIM channel 0'),
    C('rse_trig', 0xafb14, 'bl', (0xa6cc0,), 'TRIGANIM -> trigger'),
    C('rse_trig', 0xafb20, 'd', ('addi', 0, 3, -300), 'duration-out = returned ms - 300 ...'),
    C('rse_trig', 0xafb2c, 'd', ('cmpi', 0, 0, 300), '... at least 300 (signed)'),
    C('rse_trig', 0xafb80, 'd', ('lfd', 1, 2, 0x5290 - TOC_BASE), 'signed int -> float bias (2^52 + 2^31)'),
    C('rse_trig', 0xafb84, 'd', ('xoris', 0, 6, 0x8000), 'sign flip: the ms value converts as signed'),
    C('rse_trig', 0xafbb0, 'd', ('stw', 3, 31, 164), 'WAIT4ANIM deadline (+164) overwritten'),
    C('rse_wait', 0xafc64, 'd', ('addi', 6, 0, 0), 'WAITANIM flags 0'),
    C('rse_wait', 0xafc68, 'd', ('addi', 8, 0, 0), 'WAITANIM channel 0'),
    C('rse_wait', 0xafc78, 'd', ('addi', 3, 3, -300), 'wait = returned ms - 300 ...'),
    C('rse_wait', 0xafc88, 'd', ('lfd', 1, 2, 0x5288 - TOC_BASE), 'unsigned bias 2^52: ms - 300 converts unsigned'),
    C('rse_wait', 0xafcb4, 'd', ('cmpli', 0, 0, 300), '... at least 300 (unsigned, after / speed)'),
    C('rse_wait', 0xafcf4, 'd', ('stw', 5, 31, 164), 'WAITANIM clears the WAIT4ANIM deadline'),
    C('rse_loop', 0xafd2c, 'rlwinm', (3, 0, 16, 0, 15, False), 'LOOPANIM token = anim + (sub << 16)'),
    C('rse_loop', 0xafd3c, 'x', (0, 4, 26, 32), 'token equal to script +168: no restart'),
    C('rse_loop', 0xafd78, 'd', ('addi', 6, 0, 1), 'LOOPANIM flags 1 (loop)'),
    C('rse_loop', 0xafd7c, 'd', ('addi', 8, 0, 0), 'LOOPANIM channel 0'),
    C('rse_loop', 0xafd88, 'd', ('stw', 0, 31, 164), 'LOOPANIM clears the WAIT4ANIM deadline'),
    C('rse_trigwait', 0xafe08, 'bl', (0xa7dcc,), 'TRIGWAITANIM resume: query channel 0 AnimID'),
    C('rse_trigwait', 0xafe14, 'd', ('addi', 3, 3, 1), 'AnimID + 1 ...'),
    C('rse_trigwait', 0xafe28, 'd', ('addi', 3, 3, -4), '... differs from +188: re-execute (wait)'),
    C('rse_trigwait', 0xafe78, 'd', ('addi', 6, 0, 0), 'TRIGWAITANIM flags 0'),
    C('rse_trigwait', 0xafe7c, 'd', ('addi', 8, 0, 0), 'TRIGWAITANIM channel 0'),
    C('rse_trigwait', 0xafefc, 'd', ('addi', 0, 27, 1), 'wait token = AnimID + 1'),
    C('rse_trigwait', 0xaff28, 'd', ('stw', 0, 31, 188), 'stored in script +188'),
    C('rse_speed', 0xb0058, 'd', ('addi', 6, 0, 0), 'TRIGANIMSPEED flags 0'),
    C('rse_speed', 0xb005c, 'd', ('addi', 8, 0, 0), 'TRIGANIMSPEED channel 0'),
    C('rse_speed', 0xb00dc, 'd', ('mulli', 4, 5, 1000), 'deadline = now + ms x 1000 / operand'),
    C('rse_speed', 0xb00e0, 'x', (4, 4, 28, 491), 'signed divw by the operand'),
    C('rse_channel', 0xb01c4, 'd', ('addi', 8, 28, 0), 'TRIGANIM_CH channel = operand 4'),
    C('rse_channel', 0xb01c8, 'd', ('addi', 6, 0, 0), 'TRIGANIM_CH flags 0'),
    C('rse_channel', 0xb031c, 'd', ('addi', 8, 28, 0), 'WAITANIM_CH channel = operand 3'),
    C('rse_channel', 0xb0320, 'd', ('addi', 6, 0, 0), 'WAITANIM_CH flags 0'),
    C('rse_channel', 0xb03ec, 'd', ('addi', 8, 28, 0), 'LOOPANIM_CH channel = operand 3'),
    C('rse_channel', 0xb03f0, 'd', ('addi', 6, 0, 1), 'LOOPANIM_CH flags 1'),
    C('rse_channel', 0xb0500, 'd', ('addi', 8, 28, 0), 'TRIGWAITANIM_CH channel'),
    C('rse_channel', 0xb0504, 'd', ('addi', 6, 0, 0), 'TRIGWAITANIM_CH flags 0'),
    C('rse_getanim', 0xaff80, 'rlwinm', (3, 0, 0, 29, 29, True), 'GETANIM: channel flag 0x4 (ended, frozen) ...'),
    C('rse_getanim', 0xaff88, 'd', ('addi', 0, 0, -1), '... gives -1, else the AnimID'),
    C('rse_getanim', 0xb0614, 'rlwinm', (3, 0, 0, 29, 29, True), 'GETANIM_CH: channel flag 0x4 ...'),
    C('rse_getanim', 0xb061c, 'd', ('addi', 0, 0, -1), '... gives -1'),
    C('rse_wait4', 0xb07a0, 'd', ('lwz', 0, 31, 164), 'WAIT4ANIM: deadline 0 = no wait'),
    C('rse_wait4', 0xb07c4, 'd', ('stw', 0, 31, 164), 'deadline reached: cleared'),
    C('rse_wait4', 0xb07d4, 'd', ('addi', 3, 3, -1), 'otherwise re-execute'),
    # ---- advisor clip starts ------------------------------------------------------------------
    C('advisor', 0x29414, 'd', ('addi', 5, 30, -1), 'start helper: sub = animation id - 1'),
    C('advisor', 0x29418, 'd', ('addi', 4, 0, 5), 'AnimID 5 (suffix m)'),
    C('advisor', 0x2941c, 'd', ('addi', 6, 0, 16), 'flags 0x10'),
    C('advisor', 0x29420, 'd', ('addi', 8, 0, 0), 'channel 0'),
    C('advisor', 0x29424, 'bl', (0xa6cc0,), 'trigger'),
    C('advisor', 0x762c, 'bl', (0xa7e00,), 'update: read the deferred slot'),
    C('advisor', 0x7634, 'd', ('cmpli', 0, 0, 12), 'continue the sequence only with nothing deferred'),
    C('advisor', 0x7648, 'd', ('addi', 4, 0, 5), 'AnimID 5'),
    C('advisor', 0x7660, 'd', ('addi', 5, 5, -1), 'sub = sequence entry - 1'),
    C('advisor', 0x7664, 'bl', (0xa6cc0,), 'trigger'),
    # ---- node state persisted by the object save --------------------------------------------
    C('node_save', 0x5b3b4, 'rlwinm', (0, 0, 0, 27, 27, True), 'node bit 0x10 ...'),
    C('node_save', 0x5b3c4, 'd', ('ori', 0, 0, 1), '... saved as bit 0x1'),
    C('node_save', 0x5b3d0, 'rlwinm', (0, 0, 0, 26, 26, True), 'node bit 0x20 ...'),
    C('node_save', 0x5b3e0, 'd', ('ori', 0, 0, 2), '... saved as bit 0x2'),
    C('node_save', 0x5b408, 'rlwinm', (0, 0, 0, 15, 15, True), 'node bit 0x00010000 ...'),
    C('node_save', 0x5b418, 'd', ('ori', 0, 0, 8), '... saved as bit 0x8'),
    # ---- loader gate for versions below 221 (garrow/rarrow are 24.23) ------------------------
    C('loader_gate', 0x3f9f0, 'rlwinm', (4, 27, 0, 31, 31, False), 'argument bit 0x1 (allow old) kept in r27',
      ENGINE),
    C('loader_gate', 0x3fc74, 'd', ('lwz', 0, 29, 4), 'header major (+4)', ENGINE),
    C('loader_gate', 0x3fc78, 'd', ('cmpli', 0, 0, 221), 'compared with 221', ENGINE),
    C('loader_gate', 0x3fcb4, 'd', ('cmpi', 0, 27, 0), 'major < 221 without bit 0x1: DeadMesh', ENGINE),
    C('loader_gate', 0x58f00, 'd', ('addi', 4, 0, 0), 'application load site 1 passes flags 0'),
    C('loader_gate', 0x99acc, 'd', ('addi', 4, 0, 6), 'application load site 2 passes 6 ...'),
    C('loader_gate', 0x99ad4, 'd', ('addi', 4, 0, 2), '... or 2'),
)

# Opcode -> (handler, name) through the relocated switch table; the names come from the native
# debug-name table (PPC-rides.md), the handlers from the table entries.
SWITCH = ((15, 0xafa68, 'FLUSHANIM'), (16, 0xafa94, 'TRIGANIM'), (17, 0xafbbc, 'WAITANIM'),
          (18, 0xafd00, 'LOOPANIM'), (19, 0xafd94, 'TRIGWAITANIM'), (20, 0xaff3c, 'GETANIM'),
          (21, 0xaffc4, 'TRIGANIMSPEED'), (22, 0xb00f4, 'FLUSHANIM_CH'), (23, 0xb013c, 'TRIGANIM_CH'),
          (24, 0xb0268, 'WAITANIM_CH'), (25, 0xb037c, 'LOOPANIM_CH'), (26, 0xb0404, 'TRIGWAITANIM_CH'),
          (27, 0xb05b8, 'GETANIM_CH'), (46, 0xb07a0, 'WAIT4ANIM'))
SWITCH_CHECKS = tuple(Check('rse_switch', APP, SWITCH_TABLE + 4 * op, 'switch', (op, handler, name),
                            f'opcode {op} {name}') for op, handler, name in SWITCH) + (
    Check('instance_routes', APP, 0x6408, 'switch', (None, 0x59eb4, 'placement update callback'),
          'transition vector of the placement update callback'),)

# Record-word masks read by the record sampler 0xa4f68 (record in r28). The bind 0xa3e24 (record in
# r30) and the engine record relocation (record in r3) read 0x4000 only; the copy-back 0xa5894 uses
# 0x289 = 0x1 | 0x8 | 0x80 | 0x200 and 0x1000/0x10000 (PPC-formats.md). Bits outside this set are
# never read there (exporter metadata).
READ_RECORD_BITS = frozenset({0x1, 0x8, 0x80, 0x200, 0x400, 0x1000, 0x4000, 0x10000, 0x20000})
UNREAD_RECORD_BITS = frozenset({0x10, 0x20, 0x40, 0x100, 0x800, 0x2000, 0x8000})
RECORD_SCANS = ((APP, 0xa4f68, 0xa56f8, 28, READ_RECORD_BITS),
                (APP, 0xa3e24, 0xa3f68, 30, frozenset({0x4000})),
                (ENGINE, 0x3f9d8, 0x42200, 3, frozenset({0x4000})))


# --------------------------------------------------------------------- images
class Image:
    """Identity-pinned container: code words, data relocations and TOC floats."""

    def __init__(self, path: Path):
        raw = path.read_bytes()
        digest = hashlib.sha256(raw).hexdigest()
        if digest != IDENTITIES.get(path.name):
            raise WitnessError(f'identity of {path.name}: unexpected SHA-256')
        container = pef.PEFContainer(raw, path.name)
        self.code = bytes(container.code.data)
        self.data = bytes(container.data_section.data)
        self.code_index = container.code.index
        self.relocs = container.relocs.get(container.data_section.index, {})

    def w(self, offset: int) -> int:
        if offset < 0 or offset % 4 or offset + 4 > len(self.code):
            raise WitnessError(f'instruction offset {offset:#x} outside code')
        return struct.unpack_from('>I', self.code, offset)[0]

    def code_pointer(self, data_offset: int) -> int | None:
        target = self.relocs.get(data_offset)
        if target is None or target.kind != 'section' or target.target != self.code_index:
            return None
        return target.addend

    def toc_float(self, slot: int) -> float:
        if slot < 0 or slot + 4 > len(self.data):
            raise WitnessError(f'TOC slot {slot:#x} outside data')
        return struct.unpack_from('>f', self.data, slot)[0]


def verify(check: Check, image) -> dict:
    """Raise WitnessError unless the image holds exactly the expected fields; return the interpreted fact."""
    where = f'{check.container}:{check.offset:#x} ({check.meaning})'
    if check.kind == 'switch':
        handler = image.code_pointer(check.offset)
        if handler != check.fields[1]:
            raise WitnessError(f'{where}: switch entry is not the expected handler')
        return check.interpreted()
    if image.w(check.offset) != check.expected_word():
        raise WitnessError(f'{where}: instruction fields differ from the expected operands')
    if check.kind == 'toc_float':
        value = image.toc_float(check.fields[1])
        if struct.pack('>f', value) != struct.pack('>f', check.fields[2]):
            raise WitnessError(f'{where}: TOC float {value!r} differs')
    return check.interpreted()


RT_WRITERS = frozenset({7, 12, 13, 14, 15, 32, 33, 34, 35, 40, 41, 42, 43})  # D-form ops writing bits 21-25
RA_WRITERS = frozenset({20, 21, 23, 24, 25, 26, 27, 28, 29})  # rotate/logical ops writing bits 16-20
X_NON_WRITERS = frozenset({0, 32, 151, 183, 215, 247, 407, 439, 662, 918, 150, 86, 54, 278, 1014})  # compares, stores, cache ops


def writes_register(w: int, register: int) -> bool:
    op = w >> 26
    if op in RT_WRITERS:
        return w >> 21 & 31 == register
    if op in RA_WRITERS:
        return w >> 16 & 31 == register
    if op == 31 and (w >> 1 & 0x3ff) not in X_NON_WRITERS:
        return register in (w >> 21 & 31, w >> 16 & 31)
    return op == 18 and bool(w & 1)  # a call clobbers volatile registers; stop following


def record_flag_masks(image, start: int, end: int, record_register: int, follow: int = 12) -> set[int]:
    """Masks tested (rlwinm. with shift 0, andi.) on the word loaded by `lwz rX, 4(record)`.

    The loaded register is followed for up to `follow` words, across branches, until an
    instruction overwrites it or a call clobbers it.
    """
    masks = set()
    for offset in range(start, end, 4):
        w = image.w(offset)
        if w >> 26 != 32 or w >> 16 & 31 != record_register or w & 0xffff != 4:
            continue
        loaded = w >> 21 & 31
        for at in range(offset + 4, min(end, offset + 4 + 4 * follow), 4):
            x = image.w(at)
            if x >> 26 == 21 and x & 1 and x >> 21 & 31 == loaded and not x >> 11 & 31:
                masks.add(rotate_mask(x >> 6 & 31, x >> 1 & 31))
            elif x >> 26 == 28 and x >> 21 & 31 == loaded:
                masks.add(x & 0xffff)
            if writes_register(x, loaded):
                break
    return masks


def single_bit_tests(image, start: int, end: int) -> set[int]:
    """Every single-bit mask tested by a recording rlwinm (shift 0) or andi. in a range."""
    found = set()
    for offset in range(start, end, 4):
        w = image.w(offset)
        if w >> 26 == 21 and w & 1 and not w >> 11 & 31:
            mask = rotate_mask(w >> 6 & 31, w >> 1 & 31)
        elif w >> 26 == 28:
            mask = w & 0xffff
        else:
            continue
        if mask and mask & (mask - 1) == 0:
            found.add(mask)
    return found


def run(images: dict) -> dict:
    facts = [verify(check, images[check.container]) for check in CHECKS + SWITCH_CHECKS]
    scans = {}
    for container, start, end, register, expected in RECORD_SCANS:
        masks = record_flag_masks(images[container], start, end, register)
        if masks != expected or masks & UNREAD_RECORD_BITS:
            raise WitnessError(f'{container}:{start:#x} record-flag masks {sorted(map(hex, masks))} differ')
        scans[f'{container}:{start:#x}'] = sorted(hex(m) for m in masks)
    by_fact: dict[str, int] = {}
    for fact in facts:
        by_fact[fact['fact']] = by_fact.get(fact['fact'], 0) + 1
    return {
        'checks': len(facts) + len(RECORD_SCANS),
        'checks_by_fact': by_fact,
        'record_flag_scans': scans,
        'read_record_bits': sorted(hex(m) for m in READ_RECORD_BITS),
        'unread_record_bits': sorted(hex(m) for m in UNREAD_RECORD_BITS),
        'facts': facts,
    }


def load_images(bin_root: Path) -> dict:
    return {APP: Image(bin_root / APP), ENGINE: Image(bin_root / 'libraries' / ENGINE)}


def main(argv: list[str]) -> int:
    if len(argv) != 2:
        print(__doc__.splitlines()[2], file=sys.stderr)
        return 2
    report = run(load_images(Path(argv[1])))
    print(json.dumps({key: value for key, value in report.items() if key != 'facts'}, indent=2))
    return 0


if __name__ == '__main__':
    sys.exit(main(sys.argv))

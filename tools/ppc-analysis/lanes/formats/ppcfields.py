"""Bounded PowerPC instruction-field decoders for the format witnesses.

Only fields of single 32-bit words are decoded; nothing is executed and no
instruction text or bytes are emitted.
"""
from __future__ import annotations

import os
import struct
import sys

sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', '..'))

import pef  # noqa: E402


class WitnessError(pef.PEFError):
    pass


def require(actual, expected, context: str):
    if actual != expected:
        raise WitnessError(f'{context}: unexpected interpreted value {actual!r}, expected {expected!r}')
    return actual


def word(buf: bytes | bytearray, offset: int) -> int:
    if offset < 0 or offset % 4 or offset + 4 > len(buf):
        raise WitnessError(f'instruction offset {offset:#x} outside code')
    return struct.unpack_from('>I', buf, offset)[0]


def signed16(value: int) -> int:
    return value - 0x10000 if value & 0x8000 else value


def d_form(w: int) -> tuple[int, int, int, int]:
    """Primary opcode, rt/rs, ra and signed immediate."""
    return w >> 26, w >> 21 & 31, w >> 16 & 31, signed16(w & 0xffff)


def rotate(w: int) -> tuple[int, int, int, int, int, int, int]:
    """rlwimi/rlwinm: opcode, rs, ra, sh, mb, me, record bit."""
    return w >> 26, w >> 21 & 31, w >> 16 & 31, w >> 11 & 31, w >> 6 & 31, w >> 1 & 31, w & 1


def x_form(w: int) -> tuple[int, int, int, int, int, int]:
    """Opcode 31/59/63 forms: opcode, rt, ra, rb, extended opcode (10 bits), record bit."""
    return w >> 26, w >> 21 & 31, w >> 16 & 31, w >> 11 & 31, w >> 1 & 0x3ff, w & 1


def a_form(w: int) -> tuple[int, int, int, int, int, int]:
    """Floating A-form: opcode, frt, fra, frb, frc, extended opcode (5 bits)."""
    return w >> 26, w >> 21 & 31, w >> 16 & 31, w >> 11 & 31, w >> 6 & 31, w >> 1 & 31


def compare_immediate(w: int) -> tuple[int, int, int, int]:
    """cmpli (10) / cmpi (11): opcode, condition field, ra, immediate."""
    op = w >> 26
    value = w & 0xffff
    return op, w >> 23 & 7, w >> 16 & 31, value if op == 10 else signed16(value)


def branch_target(w: int, offset: int, link: bool = True) -> int:
    if w >> 26 != 18 or bool(w & 1) != link:
        raise WitnessError(f'not an I-form {"linked " if link else ""}branch at {offset:#x}')
    displacement = w & 0x03fffffc
    if displacement & 0x02000000:
        displacement -= 0x04000000
    return displacement if w & 2 else offset + displacement


def branch_conditional(w: int, offset: int) -> tuple[int, int, int]:
    """bc without link/absolute: BO (12 = branch if CR bit set, 4 = if clear), BI and target."""
    if w >> 26 != 16 or w & 3:
        raise WitnessError(f'not a relative conditional branch at {offset:#x}')
    displacement = w & 0xfffc
    if displacement & 0x8000:
        displacement -= 0x10000
    return w >> 21 & 31, w >> 16 & 31, offset + displacement


def rotate_mask(mb: int, me: int) -> int:
    """32-bit mask for IBM bit numbers mb..me (wrapping when mb > me)."""
    bits = 0
    i = mb
    while True:
        bits |= 1 << (31 - i)
        if i == me:
            return bits
        i = (i + 1) & 31


def rlwimi(target: int, source: int, sh: int, mb: int, me: int) -> int:
    rotated = ((source << sh) | (source >> (32 - sh))) & 0xffffffff if sh else source
    mask = rotate_mask(mb, me)
    return (rotated & mask) | (target & ~mask & 0xffffffff)


def rlwinm(source: int, sh: int, mb: int, me: int) -> int:
    return rlwimi(0, source, sh, mb, me)


def cstring(buf: bytes | bytearray, offset: int, limit: int = 160) -> str:
    if not 0 <= offset < len(buf):
        raise WitnessError(f'string offset {offset:#x} outside section')
    end = bytes(buf[offset:offset + limit]).find(b'\0')
    if end < 0:
        raise WitnessError(f'unterminated string at {offset:#x}')
    text = bytes(buf[offset:offset + end])
    if not all(32 <= b < 127 or b in (9, 10) for b in text):
        raise WitnessError(f'non-text bytes at {offset:#x}')
    return text.decode('ascii')

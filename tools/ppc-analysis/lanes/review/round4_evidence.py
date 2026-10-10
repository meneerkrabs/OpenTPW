"""Round-4 review witnesses: clock branch repair, economy loan model, formats MD2 tracks,
scenarios follow-up, advisor queue edges and the UI factory.

Usage: python3 -I round4_evidence.py /path/to/mac-feral/bin

Every check pins the identified Feral Mac containers (via review_evidence.Binary), decodes
instruction fields at hand-reviewed addresses and compares them with values the reviewer
derived independently. Output is metadata only: addresses, decoded meanings, counts and
conclusions. Nothing from the original programs is executed, emulated, printed or stored.

The pure models below are independently written statements of what the reviewed
instructions compute, using the operand fields this script pins. They are not translations
of original code and do not interpret instruction streams.
"""
from __future__ import annotations

import argparse
import json
import math
import struct
import sys
from fractions import Fraction
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import review_evidence as rv  # noqa: E402
import round3_evidence  # noqa: E402,F401  (registers the engine/ltms identities)
from review_evidence import Binary, ReviewError, decode, require  # noqa: E402

U32 = 0xFFFFFFFF


# --- Field helpers for forms review_evidence.decode leaves opaque ---------------------------

def m_form(word: int) -> tuple:
    """rlwimi (20) / rlwinm (21): (op, rs, ra, sh, mb, me, rc)."""
    return (word >> 26, word >> 21 & 31, word >> 16 & 31, word >> 11 & 31, word >> 6 & 31, word >> 1 & 31, word & 1)


def a59(word: int) -> tuple:
    """Single-precision A-form (opcode 59): (frD, frA, frB, frC, xo5)."""
    require(word >> 26, 59, 'single-precision arithmetic opcode')
    return (word >> 21 & 31, word >> 16 & 31, word >> 11 & 31, word >> 6 & 31, word >> 1 & 31)


def d_form(word: int) -> tuple:
    return (word >> 26, word >> 21 & 31, word >> 16 & 31, rv._s16(word & 0xFFFF))


# --- Independent models --------------------------------------------------------------------

def branch_taken(bo: int, condition_bit_set: bool) -> bool:
    """Only the two BO encodings used at the reviewed sites: 4 = branch if false, 12 = if true."""
    if bo == 4:
        return not condition_bit_set
    if bo == 12:
        return condition_bit_set
    raise ValueError(f'unreviewed BO {bo}')


def scheduler_route(branches: dict, flag8: bool, flag1: bool) -> str:
    """Follow the pinned gate branches. CR0[EQ] is set when the record-form mask is zero."""
    bo, target = branches['flag8']
    if branch_taken(bo, not flag8):
        return 'work' if target == branches['work'] else 'skip'
    bo, target = branches['flag1']
    if branch_taken(bo, not flag1):
        return 'work' if target == branches['work'] else 'skip'
    return 'work'  # fall-through of the second branch is the work block


def mode_route(branches: dict, mode: int) -> str:
    """Follow the pinned compare/branch chain; fall-throughs are the next block in address order."""
    bo, target = branches['mode0']
    if branch_taken(bo, mode == 0):
        return 'direct' if target == branches['direct'] else 'other'
    bo, target = branches['mode2']
    if not branch_taken(bo, mode == 2):
        return 'direct'
    if target != branches['mode1_check']:
        return 'other'
    bo, target = branches['mode1']
    if branch_taken(bo, mode == 1):
        return 'none' if target == branches['after_world'] else 'other'
    return 'wrapper'


def rotl32(value: int, sh: int) -> int:
    value &= U32
    return ((value << sh) | (value >> (32 - sh))) & U32 if sh else value


def mask32(mb: int, me: int) -> int:
    """IBM bit numbering (bit 0 = MSB), wrapping when mb > me."""
    bits = range(mb, me + 1) if mb <= me else list(range(mb, 32)) + list(range(0, me + 1))
    return sum(1 << (31 - b) for b in bits)


def rlwinm(rs: int, sh: int, mb: int, me: int) -> int:
    return rotl32(rs, sh) & mask32(mb, me)


def rlwimi(ra: int, rs: int, sh: int, mb: int, me: int) -> int:
    m = mask32(mb, me)
    return (rotl32(rs, sh) & m) | (ra & ~m & U32)


def sext16(value: int) -> int:
    value &= 0xFFFF
    return (value - 0x10000 if value & 0x8000 else value) & U32


def s10(value: int) -> int:
    value &= 0x3FF
    return value - 0x400 if value & 0x200 else value


ENGINE_REPACK = {  # engine_shared 0x41cac..0x41d04, hand-read (sh, mb, me)
    'x_join': (0x41cbc, 20, 5, 10, 8, 22, 23),
    'x_insert': (0x41cc4, 20, 0, 5, 6, 16, 25),
    'y_low': (0x41cd0, 21, 0, 9, 6, 22, 25),
    'y_high': (0x41cd4, 20, 0, 10, 30, 26, 31),
    'y_insert': (0x41ce0, 20, 0, 5, 12, 10, 19),
    'z_high': (0x41ce4, 21, 5, 6, 4, 22, 27),
    'z_low': (0x41cec, 20, 5, 9, 28, 28, 31),
    'z_insert': (0x41cf4, 20, 0, 5, 2, 20, 29),
    'clear_low_bits': (0x41d00, 20, 0, 4, 0, 30, 31),
}


def engine_repack(little_endian_word: int) -> int:
    """In-memory word after 0x41cac for one packed vertex (bytes b0..b3 in file order)."""
    b0, b1, b2, b3 = struct.pack('<I', little_endian_word & U32)
    x = rlwimi(b0, b1, 8, 22, 23)                                  # b0 | (b1 & 3) << 8
    h0 = rlwimi(sext16(b0 << 8 | b1), x, 6, 16, 25) & 0xFFFF       # sth 0(8)
    y = rlwimi(rlwinm(b2, 6, 22, 25), b1, 30, 26, 31) & 0xFFFF     # clrlwi 16
    word = rlwimi(h0 << 16 | b2 << 8 | b3, y, 12, 10, 19)          # stw 0(8)
    z = rlwimi(rlwinm(b3, 4, 22, 27), b2, 28, 28, 31)
    h1 = rlwimi(sext16(word), z, 2, 20, 29) & 0xFFFF               # sth 2(8)
    word = (word & 0xFFFF0000) | h1
    return rlwimi(word, 0, 0, 30, 31)                              # stb 3(8) with r4 = 0


APP_EXTRACT = {  # SimThemePark 0xa43b0..0xa43f4: (source half/word, rlwinm sh, mb, me), then srawi 22
    'x': (0xa43e8, 'high_half', 16, 0, 10),
    'y': (0xa43bc, 'word', 10, 0, 10),
    'z': (0xa43cc, 'low_half', 20, 0, 10),
}


def app_extract(memory_word: int) -> tuple[int, int, int]:
    def field(source: str, sh: int, mb: int, me: int) -> int:
        value = {'word': memory_word & U32, 'high_half': sext16(memory_word >> 16),
                 'low_half': sext16(memory_word)}[source]
        rotated = rlwinm(value, sh, mb, me)
        signed = rotated - (1 << 32) if rotated & 0x80000000 else rotated
        return signed >> 22  # srawi 22
    return tuple(field(*APP_EXTRACT[a][1:]) for a in 'xyz')


def le_fields(word: int) -> tuple[int, int, int]:
    """The formats lane's reading: signed 10-bit X/Y/Z at little-endian bits 0-9/10-19/20-29."""
    return s10(word), s10(word >> 10), s10(word >> 20)


def f32(value) -> float:
    return struct.unpack('<f', struct.pack('<f', float(value)))[0]


def f32_round(exact: Fraction) -> float:
    """Round an exact rational once to binary32 (nearest-even); normal range only."""
    if exact == 0:
        return 0.0
    sign = -1 if exact < 0 else 1
    q = abs(exact)
    e = math.floor(math.log2(q.numerator) - math.log2(q.denominator))
    while Fraction(2) ** e > q:
        e -= 1
    while Fraction(2) ** (e + 1) <= q:
        e += 1
    if not -126 <= e <= 127:
        raise ValueError('outside the normal binary32 range')
    scaled = q / Fraction(2) ** (e - 23)
    n, rem = divmod(scaled.numerator, scaled.denominator)
    twice = 2 * rem
    if twice > scaled.denominator or (twice == scaled.denominator and n & 1):
        n += 1
    return sign * float(n * Fraction(2) ** (e - 23))


def fmadds(a: float, c: float, b: float) -> float:
    """PowerPC fmadds frD = frA*frC + frB with a single binary32 rounding."""
    return f32_round(Fraction(a) * Fraction(c) + Fraction(b))


def vertex_lerp(current: float, nxt: float, t: float) -> float:
    """0xa4344: f = next*t (fmuls), result = current*(1-t) + f (fmadds), 1-t by fsubs."""
    return fmadds(current, f32(1 - Fraction(t)), f32(Fraction(nxt) * Fraction(t)))


def bounds_lower(lerp: float, scale: float) -> float:
    """0xa47fc/0xa4800: (lerp - scale) - 0.25, two fsubs."""
    return f32(Fraction(f32(Fraction(lerp) - Fraction(scale))) - Fraction(1, 4))


def bounds_upper(lerp: float, scale: float) -> float:
    """0xa49b8/0xa49bc: (scale + lerp) then 0.25 + that, two fadds."""
    return f32(Fraction(1, 4) + Fraction(f32(Fraction(scale) + Fraction(lerp))))


def loan_month(balance: int, profit: int, enabled: bool, p: int, n: int, m: int, repaid: int):
    """0xcc2cc..0xcc3bc for one bought loan with n > 0 (u32 words)."""
    if enabled:
        balance, profit = (balance - m) & U32, (profit - m) & U32
    repaid = (repaid + 1) & U32
    profit = (profit + m - ((m * n - p) & U32) // n) & U32
    done = repaid == n
    return balance, profit, (0 if done else repaid), not done


def loan_payoff(balance: int, profit: int, enabled: bool, p: int, n: int, m: int, repaid: int):
    """0xcccb8..0xccdd0: unsigned affordability, clear, then interest over n - 0 months."""
    amount = (m * ((n - repaid) & U32)) & U32
    if (balance & U32) < amount:
        return None
    if enabled:
        balance, profit = (balance - amount) & U32, (profit - amount) & U32
    repaid = 0  # cleared at 0xccd78 before 0xccda8 reloads it
    q = ((m * n - p) & U32) // n
    profit = (profit + amount - (q * ((n - repaid) & U32) & U32)) & U32
    return balance, profit


def signed32(value: int) -> int:
    value &= U32
    return value - (1 << 32) if value & 0x80000000 else value


# --- Instruction witnesses -----------------------------------------------------------------

X31_WRITES_RT = {8, 10, 11, 19, 23, 40, 55, 75, 87, 104, 119, 136, 138, 200, 202, 232, 234, 235, 266,
                 279, 311, 339, 343, 375, 459, 491}
X31_WRITES_RA = {24, 26, 28, 60, 124, 284, 316, 412, 444, 476, 536, 792, 824, 922, 954}


def _no_write_to(b: Binary, reg: int, start: int, end: int) -> None:
    """Fail if a decoded integer form in [start,end) writes `reg`; calls preserve r13-r31 by ABI."""
    for at in range(start, end, 4):
        word = b.word(at)
        op = word >> 26
        rt, ra = word >> 21 & 31, word >> 16 & 31
        xo = word >> 1 & 0x3FF
        writes = ((op in (7, 8, 12, 13, 14, 15, 32, 33, 34, 35, 40, 41, 42, 43) and rt == reg)
                  or (op in (20, 21, 23, 24, 25, 26, 27, 28, 29) and ra == reg)
                  or (op == 31 and xo in X31_WRITES_RT and rt == reg)
                  or (op == 31 and xo in X31_WRITES_RA and ra == reg)
                  or (op == 46 and rt <= reg))
        if writes:
            raise ReviewError(f'r{reg} written at {at:#x}')


def _toc_global(b: Binary, at: int, reg: int) -> int:
    t = b.slot(at, reg)
    require((t.kind, t.target), ('section', b.c.data_section.index), f'TOC global at {at:#x}')
    return t.addend


def _bc(b: Binary, at: int, bo: int, bi: int, target: int) -> tuple[int, int]:
    b.expect(at, 'bc', bo, bi, target)
    return bo, target


def scheduler_audit(app: Binary) -> dict:
    # The mode word is the GameType object: r30 = data 0x53d98, never rewritten before 0x1c2380.
    require(_toc_global(app, 0x1c1228, 30), 0x53d98, 'scheduler r30 = GameType object')
    require(_toc_global(app, 0x10566c, 30), 0x53d98, 'wrapper r30 = GameType object')
    _no_write_to(app, 30, 0x1c122c, 0x1c2384)
    app.expect(0x1c2380, 'lwz', 0, 30, 0)
    # Gate: rlwinm. masks set CR0[EQ] when the flag is clear.
    app.expect(0x1c22f8, 'rlwinm', 0, 0, 0, 28, 28, 1)   # & 0x8
    app.expect(0x1c2304, 'rlwinm', 0, 0, 0, 31, 31, 1)   # & 0x1
    gate = {'flag8': _bc(app, 0x1c22fc, 4, 2, 0x1c230c), 'flag1': _bc(app, 0x1c2308, 4, 2, 0x1c24c0),
            'work': 0x1c230c}
    require(app.call(0x1c230c), 0x9f748, 'work block begins at the branch target')
    # Time and phase advance before the gate.
    app.expect(0x1c22e0, 'addi', 0, 3, 31)
    app.expect(0x1c22ec, 'addi', 0, 3, 1)
    modes = {'mode0': _bc(app, 0x1c2388, 12, 2, 0x1c23b4), 'mode2': _bc(app, 0x1c23b0, 4, 2, 0x1c23c0),
             'mode1': _bc(app, 0x1c23e4, 4, 2, 0x1c2424),
             'direct': 0x1c23b4, 'mode1_check': 0x1c23c0, 'after_world': 0x1c2424}
    app.expect(0x1c2384, 'cmpi', 0, 0, 0)
    app.expect(0x1c23ac, 'cmpi', 0, 0, 2)
    app.expect(0x1c23e0, 'cmpi', 0, 0, 1)
    require(app.call(0x1c23b8), 0x10536c, 'direct world tick')
    require(app.call(0x1c23ec), 0x10565c, 'mode-1 wrapper')
    app.expect(0x1056a0, 'cmpi', 0, 0, 1)
    app.expect(0x1056a4, 'bc', 4, 2, 0x1056b0)
    require(app.call(0x1056ac), 0x10536c, 'wrapper rechecks mode 1 then ticks')
    truth = {f'flag8={int(a)},flag1={int(g)}': scheduler_route(gate, a, g) for a in (False, True) for g in (False, True)}
    require(truth, {'flag8=0,flag1=0': 'work', 'flag8=0,flag1=1': 'skip',
                    'flag8=1,flag1=0': 'work', 'flag8=1,flag1=1': 'work'}, 'gate truth table')
    routes = {m: mode_route(modes, m) for m in (0, 1, 2, 3)}
    require(routes, {0: 'direct', 1: 'wrapper', 2: 'direct', 3: 'none'}, 'mode routes')
    # Scaled clock: delta converted unsigned (2^52 bias), then one binary64 fmadd.
    app.expect(0x127cf0, 'addis', 0, 0, 0x4330)
    app.expect(0x127cf4, 'lfd', 3, 2, 0x56f0 - rv.APP_TOC)
    require(app.double(0x56f0), 2.0 ** 52, 'unsigned conversion bias')
    app.expect(0x127d10, 'a63', 2, 2, 3, 0, 20)          # fsub: delta
    app.expect(0x127d14, 'a63', 0, 2, 0, 1, 29)          # fmadd f0 = delta*scale + acc
    app.expect(0x127cfc, 'lfd', 1, 31, 24)               # scale
    app.expect(0x127d04, 'lfd', 0, 31, 16)               # accumulator
    return {'mode_object': '0x53d98 (GameType: 0 Full Simulation, 1 online, 2 Instant Action)',
            'gate_truth_table': truth, 'mode_routes': {str(k): v for k, v in routes.items()},
            'scaled_clock': 'acc = fma(double(u32 delta), scale, acc), binary64'}


def economy_audit(app: Binary) -> dict:
    app.expect(0xcc368, 'cmpli', 0, 5, 0)
    app.expect(0xcc36c, 'bc', 12, 2, 0xcc394)            # months == 0 skips the division
    require(app.call(0xcc360), 0xb678, 'diagnostic assert precedes the guard')
    app.expect(0xcc378, 'x31', 0, 6, 5, 235, 0)          # mullw M*N
    app.expect(0xcc380, 'x31', 0, 3, 0, 40, 0)           # subf: M*N - P
    app.expect(0xcc384, 'x31', 0, 0, 5, 459, 0)          # divwu
    app.expect(0xcc388, 'x31', 0, 0, 6, 40, 0)           # subf: M - q
    app.expect(0xcc38c, 'x31', 0, 4, 0, 266, 0)          # profit + (M - q)
    app.expect(0xcc390, 'stw', 0, 24, 292)
    app.expect(0xcc344, 'addi', 0, 3, 1)                 # months repaid ++ regardless of withdrawals
    app.expect(0xcc2d8, 'bc', 12, 2, 0xcc33c)            # withdrawals disabled skips debit only
    app.expect(0xcc39c, 'x31', 0, 3, 0, 32, 0)           # cmplw repaid, months
    app.expect(0xcc3a0, 'bc', 4, 2, 0xcc3c0)             # equality-only completion
    app.expect(0xcccd8, 'x31', 29, 4, 3, 235, 0)         # payoff = M*(N - repaid)
    app.expect(0xcccdc, 'x31', 0, 0, 29, 32, 0)          # cmplw balance, payoff
    app.expect(0xccce0, 'bc', 4, 0, 0xcccf8)
    app.expect(0xccd74, 'stw', 0, 28, 0)                 # bought = 0
    app.expect(0xccd78, 'stw', 0, 27, 0)                 # repaid = 0 ...
    app.expect(0xccda8, 'lwz', 0, 27, 0)                 # ... then reloaded for the interest term
    app.expect(0xccdc0, 'x31', 3, 3, 6, 459, 0)
    app.expect(0xccdc4, 'x31', 0, 3, 0, 235, 0)
    for at, fields in [(0xcb8bc, (1, 26, 29, 0, 18)), (0xcb8c0, (0, 0, 28, 0, 18)), (0xcb8c4, (2, 1, 0, 31, 25)),
                       (0xcb8c8, (1, 30, 0, 0, 21)), (0xcb8e4, (0, 0, 0, 1, 25)), (0xcb8e8, (1, 0, 26, 0, 18))]:
        app.expect(at, 'a63', *fields)
    fused = [at for at in range(0xcb7a8, 0xcb910, 4)
             if app.word(at) >> 26 in (59, 63) and (app.word(at) >> 1 & 31) in (28, 29, 30, 31)]
    require(fused, [], 'no fused multiply-add on the loan constructor path')
    return {'monthly': 'profit += M - divwu(M*N - P, N) after optional debit; repaid++ always; done iff repaid == N',
            'payoff': 'unsigned balance >= M*(N-repaid); clear; profit += payoff - q*N',
            'constructor_fp': 'fdiv, fmul, fadd, pow, fmul, fdiv; no fused operation'}


def formats_audit(app: Binary, engine: Binary) -> dict:
    for name, (at, op, ra, rs, sh, mb, me) in ENGINE_REPACK.items():
        require(m_form(engine.word(at)), (op, rs, ra, sh, mb, me, 0), f'engine repack {name} at {at:#x}')
    for axis, (at, _, sh, mb, me) in APP_EXTRACT.items():
        require(m_form(app.word(at))[3:6], (sh, mb, me), f'extract {axis} at {at:#x}')
    for at in (0xa43c8, 0xa43d8, 0xa43f4):
        word = app.word(at)
        require((word >> 26, word >> 1 & 0x3FF, word >> 11 & 31), (31, 824, 22), f'srawi 22 at {at:#x}')
    # X/Y/Z scale and offset slots and destinations (+0/+16/+32).
    for at, (frd, slot) in [(0xa4434, (5, 0)), (0xa4470, (9, 4)), (0xa44a0, (11, 8))]:
        app.expect(at, 'lfs', frd, 10, slot)
    for at, (frs, disp) in [(0xa44d4, (3, 0)), (0xa44f4, (3, 16)), (0xa4504, (2, 32))]:
        app.expect(at, 'stfs', frs, 3, disp)
    require(a59(app.word(0xa446c)), (2, 2, 6, 5, 29), 'fmadds q*scale + offset')
    require(a59(app.word(0xa4484)), (2, 2, 0, 1, 25), 'fmuls next*t')
    require(a59(app.word(0xa44ac)), (2, 3, 2, 0, 29), 'fmadds cur*(1-t) + next*t')
    # Key-major rows.
    app.expect(0xa436c, 'x31', 7, 0, 11, 235, 0)
    # Texture-frame gate and rotation mode.
    app.expect(0xa5764, 'rlwinm', 0, 0, 0, 30, 30, 1)
    app.expect(0xa5768, 'bc', 12, 2, 0xa578c)
    app.expect(0xa5774, 'rlwinm', 0, 0, 0, 28, 28, 1)
    app.expect(0xa5778, 'bc', 4, 2, 0xa578c)
    require(app.call(0xa5788), 0xa4160, 'texture-frame update')
    app.expect(0xa8248, 'rlwinm', 0, 0, 0, 30, 30, 1)
    app.expect(0xa824c, 'bc', 12, 2, 0xa82c4)
    require(app.call(0xa82e0), 0xa7fc8, 'slerp on the clear-bit path')
    require(a59(app.word(0xa827c)), (0, 2, 0, 3, 29), 'linear rotation blend is fused')
    # Rotation partner count: the global is filled from the record before 0xa820c reads it.
    require(_toc_global(app, 0xa4f78, 31), 0x1577c0, 'sampler global block')
    require(_toc_global(app, 0xa8214, 5), 0x1577c0, 'rotation global block')
    require(d_form(app.word(0xa50b0)), (40, 0, 28, 16), 'lhz record rotation-key count')
    app.expect(0xa50b4, 'stw', 0, 31, 16412)
    app.expect(0xa8228, 'lwz', 3, 5, 16412)
    users = [at for at in range(0, len(app.code) - 3, 4)
             if app.word(at) >> 26 in (32, 36) and app.word(at) & 0xFFFF == 16412]
    require(users, [0xa50b4, 0xa8228], 'only writer and reader of global +16412')
    # Dispatcher: cursor reset and static group only while node flag 0x00800000 is clear.
    app.expect(0xa4ac8, 'rlwinm', 0, 0, 0, 8, 8, 1)
    app.expect(0xa4ad0, 'bc', 4, 2, 0xa4de8)
    app.expect(0xa4b78, 'bc', 12, 0, 0xa4b48)            # advance while ticks[c+1] < time
    require(a59(app.word(0xa4bc4)), (1, 1, 0, 0, 18), 'single-precision fraction')
    # Group-0 rounding order and padding constant.
    require(a59(app.word(0xa47fc)), (0, 5, 0, 0, 20), 'lerp - scale')
    require(a59(app.word(0xa4800)), (0, 0, 6, 0, 20), '... - 0.25')
    require(a59(app.word(0xa49b8)), (2, 8, 4, 0, 21), 'scale + lerp')
    require(a59(app.word(0xa49bc)), (2, 5, 2, 0, 21), '0.25 + ...')
    app.expect(0xa47e0, 'lfs', 6, 2, 0x519c - rv.APP_TOC)
    require(struct.unpack_from('>f', app.data, 0x519c)[0], 0.25, 'bounds padding')
    # Unreported flag writes (consumers not traced).
    flags = {}
    for at, reg, value in [(0xa4ddc, 0, 0x0080), (0xa4f44, 0, 0x0001), (0xa5070, 0, 0x0004)]:
        word = app.word(at)
        require((word >> 26, word >> 21 & 31, word >> 16 & 31, word & 0xFFFF), (25, reg, reg, value), f'oris at {at:#x}')
        flags[hex(at)] = hex(value << 16)
    app.expect(0xa5074, 'stw', 0, 26, 48)
    app.expect(0xa4f48, 'stw', 0, 26, 0)
    return {'packed_vertex': 'engine repack moves LE X/Y/Z bits 0-9/10-19/20-29 to memory bits 22-31/12-21/2-11',
            'rotation_partner_count': 'record +16 copied to global +16412 at 0xa50b4 before 0xa820c',
            'group0': 'lower = (lerp - scale) - 0.25; upper = 0.25 + (scale + lerp)',
            'unreported_flag_writes': {'0xa4f44': 'node state |= 0x00010000 after every vertex pass',
                                       '0xa5070': 'instance +48 |= 0x00040000 after the vertex call'}}


def scenarios_audit(app: Binary) -> dict:
    app.expect(0xccfa8, 'lwz', 3, 3, 292)
    require(app.call(0xd33f4), 0xccfa8, 'profit-year accessor')
    app.expect(0xd33f8, 'lwz', 0, 31, 1888)
    app.expect(0xd33fc, 'x31', 0, 3, 0, 0, 0)             # cmpw: signed
    app.expect(0xd3400, 'bc', 4, 1, 0xd3410)
    app.expect(0xf4744, 'lwz', 0, 30, 484)               # staff grade
    app.expect(0xf4750, 'rlwinm', 0, 0, 4, 0, 27, 0)     # grade * 16
    app.expect(0xf4754, 'lwz', 4, 3, 832)                # PayMultiplier[type]
    app.expect(0xf475c, 'lwz', 0, 3, 748)                # BaseWage[grade]
    app.expect(0xf4760, 'x31', 3, 4, 0, 235, 0)          # mullw
    return {'profit_year_ticket': 'signed (int)+292 > [1888]',
            'wage': 'PayMultiplier[type] * BaseWage[grade], 32-bit product'}


def advisor_audit(app: Binary) -> dict:
    app.expect(0xa154, 'x31', 0, 4, 0, 266, 0)
    app.expect(0xa158, 'x31', 0, 3, 0, 32, 0)            # cmplw now, start + duration
    app.expect(0xa15c, 'bc', 4, 0, 0xa168)
    app.expect(0x90a0, 'cmpli', 0, 3, 0)
    app.expect(0x90a4, 'bc', 12, 2, 0x90fc)
    app.expect(0x90ec, 'x31', 0, 3, 22, 32, 0)
    app.expect(0x90f0, 'bc', 4, 0, 0x90fc)
    app.expect(0xb854, 'x31', 0, 3, 0, 0, 0)
    app.expect(0xb858, 'bc', 4, 0, 0xb864)
    app.expect(0x89d8, 'x31', 0, 29, 3, 0, 0)
    app.expect(0x89dc, 'bc', 12, 0, 0x89e4)
    app.expect(0x9230, 'x31', 0, 23, 0, 0, 0)
    app.expect(0x9234, 'bc', 12, 0, 0x9240)
    return {'busy': 'unsigned now < start + duration', 'repeat': 'zero quarter skips; unsigned elapsed >= interval',
            'revalidation': 'signed score >= minimum', 'variant': 'signed v < count keeps',
            'duplicates': 'signed count < max admits'}


def ui_audit(app: Binary) -> dict:
    app.expect(0x17fe80, 'cmpli', 0, 4, 13)
    app.expect(0x17fea8, 'bc', 12, 1, 0x17fea8 + 868)
    table = _toc_global(app, 0x17feac, 3)
    require(table, 0x50098, 'factory jump table')
    entries = []
    for i in range(14):
        target = app.rel.get(table + 4 * i)
        require(bool(target) and (target.kind, target.target) == ('section', app.c.code.index), True,
                f'jump entry {i} relocated to code')
        entries.append(struct.unpack_from('>I', app.data, table + 4 * i)[0])
    require(entries[0], 0x18020c, 'type 0 entry')
    require(0x18020c in entries[1:], False, 'type 0 target unique')
    app.expect(0x18020c, 'addi', 3, 0, 0)
    return {'factory': 'types 0..13 via data 0x50098; type 0 returns null; > 13 takes the default path'}


def inspect(root: Path) -> dict:
    app = Binary(root, 'SimThemePark.data', 'SimThemePark.data', rv.APP_TOC)
    engine = Binary(root, 'engine_shared.data', 'libraries/engine_shared.data', 0x8000)
    return {'scheduler': scheduler_audit(app), 'economy': economy_audit(app),
            'formats': formats_audit(app, engine), 'scenarios': scenarios_audit(app),
            'advisor': advisor_audit(app), 'ui': ui_audit(app)}


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('bin_root', type=Path)
    args = parser.parse_args()
    try:
        result = inspect(args.bin_root)
    except (OSError, rv.pef.PEFError, ReviewError) as error:
        parser.exit(1, f'round4 evidence: {error}\n')
    print(json.dumps(result, indent=2, sort_keys=True))


if __name__ == '__main__':
    main()

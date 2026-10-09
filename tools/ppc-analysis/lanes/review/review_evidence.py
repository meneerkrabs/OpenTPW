"""Independent review witnesses for the PPC lanes; metadata output only.

Usage: python3 -I review_evidence.py /path/to/mac-feral/bin [--pc-save Easymode.TPWI]

Every check decodes instruction *fields* of identity-pinned Feral Mac binaries
and compares them with values that a reviewer derived by hand. Nothing from the
original programs is executed, emulated, printed or stored; output consists of
addresses, decoded operands, interpreted constants and conclusions. The
optional PC save check decodes integers from an original save payload with the
standard-library zlib module (no OpenTPW code involved) to compare the Mac
serializer field order with the PC data layout.
"""
from __future__ import annotations

import argparse
import hashlib
import json
import math
import os
import struct
import sys
import zlib
from pathlib import Path

sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', '..'))
import pef  # noqa: E402

IDENTITIES = {
    'SimThemePark.data': '04809cd4ccee5433c7fb0b7c93d32f6a7aa629c1849181c0b7906415e5e295f5',
    'bullfrog_shared.data': 'b67b56b7b2b75962b8b34559e20fd97b623b2d0f7f08a0035f82072ffbf4ec06',
    'sams_utils_shared.data': '1959a54b2280c95ddcc25ec77c070b4d609dcca7b7e59683b460c92c7297bcd6',
}
PC_SAVE_SHA = None  # reported, not pinned: the save is an optional cross-check input
GLUE_TAIL = (0x90410014, 0x800C0000, 0x804C0004, 0x7C0903A6, 0x4E800420)
APP_TOC = 0x8000


class ReviewError(Exception):
    pass


def require(actual, expected, context: str):
    if actual != expected:
        raise ReviewError(f'{context}: expected {expected!r}, found {actual!r}')
    return actual


def _s16(v: int) -> int:
    return v - 0x10000 if v & 0x8000 else v


def decode(word: int, at: int = 0) -> tuple:
    """Decode the fields this review relies on; unknown forms return ('?', word)."""
    op = word >> 26
    rt, ra, rb = word >> 21 & 31, word >> 16 & 31, word >> 11 & 31
    if op == 10:  # cmpli: crf, ra, uimm
        return ('cmpli', rt >> 2, ra, word & 0xFFFF)
    if op in (7, 11, 12, 14, 15, 32, 34, 36, 38, 48, 50, 52, 54):
        names = {7: 'mulli', 11: 'cmpi', 12: 'addic', 14: 'addi', 15: 'addis', 32: 'lwz', 34: 'lbz',
                 36: 'stw', 38: 'stb', 48: 'lfs', 50: 'lfd', 52: 'stfs', 54: 'stfd'}
        first = rt >> 2 if op == 11 else rt
        return (names[op], first, ra, _s16(word & 0xFFFF))
    if op == 18:
        li = word & 0x03FFFFFC
        li = li - 0x04000000 if li & 0x02000000 else li
        return ('bl' if word & 1 else 'b', li if word & 2 else at + li)
    if op == 16:
        bd = word & 0xFFFC
        bd = bd - 0x10000 if bd & 0x8000 else bd
        return ('bc' + ('l' if word & 1 else ''), rt, ra, bd if word & 2 else at + bd)
    if op == 19 and (word >> 1 & 0x3FF) == 16:
        return ('bclr', rt, ra)
    if op == 21:
        return ('rlwinm', rt, ra, rb, word >> 6 & 31, word >> 1 & 31, word & 1)
    if op == 31:
        return ('x31', rt, ra, rb, word >> 1 & 0x3FF, word & 1)
    if op == 63:
        xo5 = word >> 1 & 31
        if xo5 in (18, 20, 21, 25, 29):
            return ('a63', rt, ra, rb, word >> 6 & 31, xo5)
        return ('x63', rt, ra, rb, word >> 1 & 0x3FF)
    return ('?', word)


class Binary:
    def __init__(self, root: Path, name: str, rel: str, toc: int):
        raw = (root / rel).read_bytes()
        require(hashlib.sha256(raw).hexdigest(), IDENTITIES[name], f'identity of {name}')
        self.c = pef.PEFContainer(raw, name)
        self.code = bytes(self.c.code.data)
        self.data = bytes(self.c.data_section.data)
        self.rel = self.c.relocs[self.c.data_section.index]
        self.toc = toc
        self.name = name

    def word(self, at: int) -> int:
        return struct.unpack_from('>I', self.code, at)[0]

    def expect(self, at: int, *fields):
        return require(decode(self.word(at), at), fields, f'{self.name} code:{at:#x}')

    def glue(self, at: int) -> str:
        """Full six-word CFM glue stub check (stricter than a first-word match)."""
        words = struct.unpack_from('>6I', self.code, at)
        if words[0] >> 16 != 0x8182 or words[1:] != GLUE_TAIL:
            raise ReviewError(f'{self.name} code:{at:#x} is not a complete import glue stub')
        target = self.rel.get(self.toc + _s16(words[0] & 0xFFFF))
        if not target or target.kind != 'import':
            raise ReviewError(f'{self.name} glue {at:#x} TOC slot is not an import')
        return self.c.imports[target.target].name

    def call(self, at: int) -> int:
        kind, target = decode(self.word(at), at)
        require(kind, 'bl', f'{self.name} linked branch at {at:#x}')
        return target

    def slot(self, at: int, reg: int) -> pef.RelocTarget:
        op, rt, ra, d = decode(self.word(at), at)
        require((op, rt, ra), ('lwz', reg, 2), f'{self.name} TOC load at {at:#x}')
        target = self.rel.get(self.toc + d)
        if target is None:
            raise ReviewError(f'{self.name} TOC slot {self.toc + d:#x} is unrelocated')
        return target

    def double(self, offset: int) -> float:
        require(offset in self.rel or offset + 4 in self.rel, False, f'{self.name} constant {offset:#x} relocation')
        return struct.unpack_from('>d', self.data, offset)[0]

    def cstr(self, offset: int) -> str:
        end = self.code.index(b'\0', offset)
        return self.code[offset:end].decode('mac_roman')

    def span_sha(self, start: int, end: int) -> str:
        return hashlib.sha256(self.code[start:end]).hexdigest()


def vector_code(b: Binary, name: str) -> int:
    export = [e for e in b.c.exports if e.name == name]
    require(len(export), 1, f'export {name}')
    target = b.rel[export[0].value]
    require(target.kind, 'section', f'{name} transition vector')
    return target.addend


def timer_audit(app: Binary, bull: Binary, sams: Binary) -> dict:
    # LbTime_GetClock: result buffer of GetAbsolute is the dividend source.
    clock = vector_code(bull, 'LbTime_GetClock__Fv')
    bull.expect(clock + 0x0c, 'addi', 3, 1, 80)
    require(bull.glue(bull.call(clock + 0x10)), 'GetAbsolute__Q213SamsUtilities6UTimerFv', 'clock source')
    bull.expect(clock + 0x18, 'lwz', 3, 1, 80)
    bull.expect(clock + 0x20, 'lwz', 4, 1, 84)
    # Division helper returns the quotient pair, and its successor is the signed variant.
    bull.expect(0x59e6c, 'x31', 4, 4, 4, 138, 0)  # adde r4,r4,r4
    bull.expect(0x59e70, 'x31', 3, 3, 3, 138, 0)  # adde r3,r3,r3
    require(bull.word(0x59e74), 0x4E800020, 'unsigned helper return')
    bull.expect(0x59e88, 'rlwinm', 3, 9, 0, 0, 0, 1)  # sign-bit test: signed variant
    unsigned_sha = bull.span_sha(0x59d98, 0x59e84)
    require(app.span_sha(0x1c4010, 0x1c40fc), unsigned_sha, 'app copy of unsigned 64-bit divide')
    require(app.span_sha(0x1c4100, 0x1c4200), bull.span_sha(0x59e88, 0x59f88), 'app copy of signed 64-bit divide')
    # GetAbsolute fallback: Microseconds writes r1+0x48, r31 is the caller's result pointer.
    absolute = vector_code(sams, 'GetAbsolute__Q213SamsUtilities6UTimerFv')
    sams.expect(absolute + 0x0c, 'x31', 3, 31, 3, 444, 0)  # mr r31,r3
    sams.expect(0xa660, 'addi', 3, 1, 72)
    require(sams.glue(sams.call(0xa664)), 'Microseconds', 'fallback source')
    # SetRate is exactly five field moves and a return: no scaling.
    set_rate = vector_code(sams, 'SetRate__Q213SamsUtilities6UTimerFRCQ213SamsUtilities13UMicrosecondsUc')
    require(sams.word(set_rate + 0x14), 0x4E800020, 'SetRate return after five moves')
    # HasTicked: strict elapsed > interval; flag +8 selects resynchronisation to now.
    ticked = vector_code(sams, 'HasTicked__Q213SamsUtilities6UTimerFv')
    require(ticked, 0xa698, 'HasTicked code entry')
    require(sams.call(0xa6b4), absolute, 'HasTicked reads GetAbsolute')
    sams.expect(0xa6c8, 'x31', 0, 5, 7, 8, 0)    # subfc r0,r5,r7: now.lo - last.lo
    sams.expect(0xa6d8, 'x31', 0, 0, 9, 8, 0)    # subfc r0,r0,r9: interval.lo - elapsed.lo
    sams.expect(0xa6e8, 'bc', 12, 2, 0xa71c)     # beq -> return 0 when interval >= elapsed
    sams.expect(0xa6ec, 'lbz', 0, 30, 8)
    sams.expect(0xa6f8, 'stw', 7, 30, 4)         # flag set: last = now
    sams.expect(0xa704, 'x31', 0, 5, 9, 10, 0)   # flag clear: last += interval (addc)
    sams.expect(0xa714, 'addi', 3, 0, 1)
    sams.expect(0xa71c, 'addi', 3, 0, 0)
    # Application: second constant in the same constructor, previously unreported.
    app.expect(0x30c, 'addis', 4, 0, 8)
    app.expect(0x314, 'addi', 0, 4, -24288)
    app.expect(0x31c, 'stw', 0, 27, 0x70)
    require(app.glue(app.call(0x338)), 'SetRate__Q213SamsUtilities6UTimerFRCQ213SamsUtilities13UMicrosecondsUc',
            'application SetRate')
    return {'division_helper': 'unsigned quotient (bullfrog 0x59d98); signed variant follows at 0x59e88',
            'app_div_copies': {'unsigned': '0x1c4010', 'signed': '0x1c4100'},
            'has_ticked': {'code': hex(ticked), 'ticks_when': 'elapsed > interval (unsigned 64-bit)',
                           'flag_nonzero': 'last = now', 'flag_zero': 'last += interval'},
            'app_constructor_other_constant': {'value': (8 << 16) - 24288, 'stored_at': 'this+0x70',
                                               'role': 'unknown'}}


def loan_audit(app: Binary) -> dict:
    labels_base = app.slot(0xcba5c, 31)
    require((labels_base.kind, labels_base.target, labels_base.addend), ('section', 0, 0x1cc8ce), 'bank labels')
    base = labels_base.addend
    fields = {}
    # Read path: (field addi, label addi) pairs in serializer order.
    for field_at, field_reg, label_at in [
            (0xcbd00, 27, 0xcbcfc), (0xcbd20, 27, 0xcbd24), (0xcbd44, 27, 0xcbd48), (0xcbd68, 27, 0xcbd6c),
            (0xcbd8c, 27, 0xcbd90), (0xcbdb0, 27, 0xcbdb4), (0xcbdd4, 27, 0xcbdd8),
            (0xcbe04, 30, 0xcbe08), (0xcbe28, 30, 0xcbe2c), (0xcbe4c, 30, 0xcbe50), (0xcbe70, 30, 0xcbe74),
            (0xcbe94, 30, 0xcbe98), (0xcbeb8, 30, 0xcbebc), (0xcbedc, 30, 0xcbee0), (0xcbf00, 30, 0xcbf04)]:
        op, rd, ra, offset = decode(app.word(field_at), field_at)
        require((op, rd, ra), ('addi', 4, field_reg), f'field operand {field_at:#x}')
        op, rd, ra, label = decode(app.word(label_at), label_at)
        require((op, rd, ra), ('addi', 5, 31), f'label operand {label_at:#x}')
        key = f'this+{offset:#x}' if field_reg == 27 else f'loan+{offset:#x}'
        fields[key] = app.cstr(base + label)
    app.expect(0xcbdf8, 'rlwinm', 29, 0, 5, 0, 26, 0)  # loan index * 32
    for helper, read_size_at, swap_at in [(0xcd0b4, 0xcd10c, 0xcd18c), (0xc2fc, 0xc354, 0xc3d4),
                                          (0xc3f8, 0xc450, 0xc4d0)]:
        app.expect(read_size_at, 'addi', 6, 0, 4)
        require(app.glue(app.call(read_size_at + 4)), 'LbFile_Read__FPvPvUlPUl', f'reader {helper:#x}')
        app.expect(swap_at, 'x31', 4, 0, 31, 662, 0)  # stwbrx: little-endian 32-bit field
    # Constructor 0xcb7a8: copies from the settings global into each 32-byte record.
    source = app.slot(0xcb7e0, 30)
    require((source.kind, source.target, source.addend), ('section', 1, 0x54860), 'settings global')
    require(set(app.data[0x54860 + 0x198:0x54860 + 0x240]), {0}, 'settings global is runtime-populated')
    app.expect(0xcb824, 'lwz', 0, 30, 0x198)
    app.expect(0xcb828, 'stw', 0, 31, 0x0c)
    app.expect(0xcb7cc, 'addi', 22, 0, 1)
    app.expect(0xcb834, 'stw', 22, 31, 0x114)
    for load_at, src, store_at, dst in [(0xcb84c, 0x1ac, 0xcb854, 0x30), (0xcb858, 0x1a0, 0xcb85c, 0x18),
                                        (0xcb860, 0x1a4, 0xcb864, 0x1c), (0xcb868, 0x1a8, 0xcb86c, 0x20)]:
        app.expect(load_at, 'lwz', 0, 26, src)
        app.expect(store_at, 'stw', 0, 25, dst)
    app.expect(0xcb850, 'addi', 3, 0, 0)
    app.expect(0xcb870, 'stw', 3, 25, 0x28)
    app.expect(0xcb874, 'stw', 3, 25, 0x2c)
    app.expect(0xcb87c, 'cmpli', 0, 0, 10000)
    app.expect(0xcb880, 'bc', 12, 1, 0xcb888)
    app.expect(0xcb884, 'addi', 3, 0, 1)
    app.expect(0xcb88c, 'stw', 0, 25, 0x14)
    # Arithmetic: integer -> double by the 2^52 bias, then the expression.
    app.expect(0xcb80c, 'addis', 23, 0, 0x4330)
    for at, reg, offset in [(0xcb838, 27, 0x5408), (0xcb83c, 28, 0x5428), (0xcb840, 29, 0x5420),
                            (0xcb844, 30, 0x5418), (0xcb848, 31, 0x5410)]:
        app.expect(at, 'lfd', reg, 2, offset - APP_TOC)
    constants = {hex(o): app.double(o) for o in (0x5408, 0x5410, 0x5418, 0x5420, 0x5428)}
    require(constants, {'0x5408': 2.0 ** 52, '0x5410': 0.5, '0x5418': 1.0, '0x5420': 12.0, '0x5428': 100.0},
            'loan constants')
    app.expect(0xcb890, 'lwz', 0, 25, 0x20)   # months
    app.expect(0xcb894, 'lwz', 3, 25, 0x1c)   # APR percent
    app.expect(0xcb89c, 'lwz', 22, 25, 0x18)  # amount
    app.expect(0xcb898, 'stw', 0, 1, 76)
    app.expect(0xcb8a0, 'stw', 3, 1, 84)
    app.expect(0xcb8ac, 'lfd', 0, 1, 72)
    app.expect(0xcb8b0, 'lfd', 1, 1, 80)
    app.expect(0xcb8b4, 'a63', 26, 0, 27, 0, 20)   # f26 = months
    app.expect(0xcb8b8, 'a63', 0, 1, 27, 0, 20)    # f0 = APR
    app.expect(0xcb8bc, 'a63', 1, 26, 29, 0, 18)   # months / 12
    app.expect(0xcb8c0, 'a63', 0, 0, 28, 0, 18)    # APR / 100
    app.expect(0xcb8c4, 'a63', 2, 1, 0, 31, 25)    # (months / 12) * 0.5
    app.expect(0xcb8c8, 'a63', 1, 30, 0, 0, 21)    # 1 + APR / 100
    require(app.glue(app.call(0xcb8cc)), 'pow', 'loan exponentiation')
    app.expect(0xcb8d4, 'stw', 22, 1, 68)
    app.expect(0xcb8dc, 'lfd', 0, 1, 64)
    app.expect(0xcb8e0, 'a63', 0, 0, 27, 0, 20)    # amount
    app.expect(0xcb8e4, 'a63', 0, 0, 0, 1, 25)     # amount * pow
    app.expect(0xcb8e8, 'a63', 1, 0, 26, 0, 18)    # / months
    require(app.call(0xcb8ec), 0x1c3fbc, 'double to u32 conversion')
    app.expect(0xcb8f4, 'stw', 3, 25, 0x24)
    app.expect(0xcb8f8, 'cmpli', 0, 24, 8)
    app.expect(0xcb8fc, 'addi', 26, 26, 16)
    app.expect(0xcb900, 'addi', 25, 25, 32)
    # Saturating conversion helper.
    bounds = app.slot(0x1c3fbc, 4)
    require((bounds.kind, bounds.target, bounds.addend), ('section', 1, 0x52e24), 'conversion bounds')
    require([app.double(0x52e24 + 8 * k) for k in range(3)], [0.0, 2.0 ** 32, 2.0 ** 31], 'conversion bounds')
    app.expect(0x1c3fd8, 'bclr', 12, 0)            # x < 0 -> 0
    app.expect(0x1c3fdc, 'addi', 3, 3, -1)
    app.expect(0x1c3fe0, 'bclr', 4, 24)            # !(x < 2^32), including NaN -> 0xffffffff
    app.expect(0x1c3ff4, 'x63', 2, 0, 2, 15)       # fctiwz
    app.expect(0x1c4004, 'addis', 3, 3, -32768)
    # Early payoff (0xccc78): amount, unsigned affordability test, optional debit.
    app.expect(0xccc84, 'rlwinm', 4, 30, 5, 0, 26, 0)
    app.expect(0xcccd0, 'x31', 3, 3, 0, 40, 0)     # term - months_repaid
    app.expect(0xcccd8, 'x31', 29, 4, 3, 235, 0)   # payoff = monthly * remaining
    app.expect(0xcccdc, 'x31', 0, 0, 29, 32, 0)    # cmplw balance, payoff (unsigned)
    app.expect(0xccce0, 'bc', 4, 0, 0xcccf8)
    app.expect(0xcccf8, 'lwz', 0, 24, 0x114)       # withdrawals enabled?
    app.expect(0xccd00, 'bc', 12, 2, 0xccd6c)      # disabled: skip the debit entirely
    app.expect(0xccd08, 'x31', 0, 29, 0, 40, 0)    # balance -= payoff
    app.expect(0xccd64, 'x31', 0, 29, 0, 40, 0)    # profit this year -= payoff
    app.expect(0xccd74, 'stw', 0, 28, 0)           # loan_bought = 0
    app.expect(0xccd78, 'stw', 0, 27, 0)           # months_repaid = 0
    pow_callers = [o for o in range(0, len(app.code), 4)
                   if app.word(o) >> 26 == 18 and app.word(o) & 3 == 1 and decode(app.word(o), o)[1] == 0x1c61a4]
    # Schema: .sam LoanInfo member order and array bound.
    schema = []
    for entry in range(0x357d8, 0x359f4, 0x3c):
        kind = struct.unpack_from('>I', app.data, entry)[0]
        name = app.data[entry + 4:entry + 0x24].split(b'\0')[0].decode('ascii')
        schema.append((kind, name))
    require(schema, [(4, 'InitialCash'), (5, 'InitialAdmissionFee'), (1, 'BankAccountInfo'), (2, ''),
                     (5, 'LoanAmount'), (6, 'APRInPercent'), (5, 'RepaymentPeriodInMonths'), (5, 'Lendername'),
                     (3, 'LoanInfo')], 'settings schema order')
    loan_info_bound = struct.unpack_from('>I', app.data, 0x359b8 + 0x34)[0]
    return {'bank_fields': fields, 'serializer_field_width': '4-byte little-endian',
            'constructor': {'mBalance': 'settings+0x198', 'mWithdrawalsEnabled': 1, 'loans': 8,
                            'loan_available': 'amount <= 10000 (unsigned)',
                            'copied': {'amount': 'settings+0x1a0+16i', 'APR': '+0x1a4', 'months': '+0x1a8',
                                       'lender': '+0x1ac'}},
            'monthly_repayment': 'u32sat(trunc(amount * pow(1 + APR/100, (months/12)*0.5) / months))',
            'u32sat': 'x<0 -> 0; x>=2^32, +inf or NaN -> 0xffffffff; else truncate toward zero',
            'constants': constants, 'pow_callers': [hex(o) for o in pow_callers],
            'schema_order': [n for _, n in schema if n], 'loan_info_schema_bound': loan_info_bound,
            'payoff': {'amount': 'monthly_repayment * (term - months_repaid)',
                       'refused_when': 'mBalance < amount as UNSIGNED 32-bit (negative balances pass)',
                       'debit_only_if': 'mWithdrawalsEnabled != 0'}}


def calendar_audit(app: Binary, bull: Binary) -> dict:
    app.expect(0xe3ca8, 'addi', 0, 0, 15000)
    app.expect(0xe3cc0, 'stw', 0, 30, 0x1c)
    require(app.call(0xe3cc4), 0xe4348, 'calendar epoch helper')
    for at, reg, value in [(0xe4358, 5, 2000), (0xe435c, 6, 1), (0xe4364, 7, 1), (0xe4368, 8, 0),
                           (0xe436c, 9, 0), (0xe4378, 10, 0)]:
        app.expect(at, 'addi', reg, 0, value)
    app.expect(0xe434c, 'addi', 4, 3, 0)
    require(app.glue(app.call(0xe437c)), 'SetTime__11TbTimeStampFiiiiiii', 'epoch setter')
    labels = app.slot(0xe3d3c, 31)
    require((labels.kind, labels.target, labels.addend), ('section', 0, 0x1cfab9), 'calendar labels')
    fields = {}
    for field_at, label_at in [(0xe3d74, 0xe3d78), (0xe3d98, 0xe3d9c), (0xe3dbc, 0xe3dc0),
                               (0xe3de0, 0xe3de4), (0xe3e04, 0xe3e08)]:
        _, rd, ra, offset = decode(app.word(field_at), field_at)
        require((rd, ra), (4, 29), f'calendar field {field_at:#x}')
        _, rd, ra, label = decode(app.word(label_at), label_at)
        require((rd, ra), (5, 31), f'calendar label {label_at:#x}')
        fields[f'this+{offset:#x}'] = app.cstr(labels.addend + label)
    # Conversion: (tick * secs_per_real_sec) / 4 seconds -> 100 ns TbTimeDiff.
    require(app.call(0xe43d0), 0x10a9a4, 'world accessor')
    world = app.slot(0x10a9a4, 3)
    app.expect(0xe43d4, 'addis', 3, 3, 30)
    app.expect(0xe43d8, 'lwz', 7, 22, 0x1c)
    app.expect(0xe43dc, 'lwz', 4, 3, -22772)
    app.expect(0xe43ec, 'x31', 0, 7, 4, 11, 0)    # mulhwu
    app.expect(0xe43fc, 'x31', 4, 7, 4, 235, 0)   # mullw
    app.expect(0xe43e4, 'addi', 5, 0, 0)
    app.expect(0xe43e8, 'addi', 6, 0, 4)
    require(app.call(0xe4404), 0x1c4100, 'signed 64-bit divide')
    app.expect(0xe4408, 'addis', 6, 0, 153)
    app.expect(0xe4410, 'addi', 0, 6, -27008)
    app.expect(0xe4430, 'addi', 4, 22, 0)
    require(app.glue(app.call(0xe443c)), '__pl__11TbTimeStampCFRC10TbTimeDiff', 'start + elapsed')
    # TbTimeDiff unit: Set(days, hours, minutes, seconds, ms) builds ms then multiplies by 10,000.
    set_diff = vector_code(bull, 'Set__10TbTimeDiffFiiiii')
    require(set_diff, 0xe618, 'TbTimeDiff::Set entry')
    bull.expect(0xe61c, 'addis', 10, 0, 1)
    bull.expect(0xe620, 'addi', 0, 10, -5536)       # 60,000 ms per minute
    bull.expect(0xe624, 'addis', 11, 0, 55)
    bull.expect(0xe628, 'addi', 31, 11, -4480)      # 3,600,000 ms per hour
    bull.expect(0xe62c, 'addis', 10, 0, 1318)
    bull.expect(0xe634, 'addi', 30, 10, 23552)      # 86,400,000 ms per day
    bull.expect(0xe650, 'mulli', 0, 8, 1000)
    bull.expect(0xe6c0, 'addi', 0, 0, 10000)        # ms -> 100 ns
    # mGameTick: CWorld serializer label, increment and reset sites.
    world_labels = app.slot(0x105d54, 29)
    require((world_labels.kind, world_labels.target, world_labels.addend), ('section', 0, 0x1d369c), 'world labels')
    app.expect(0x105eec, 'addis', 4, 28, 30)
    app.expect(0x105efc, 'addi', 4, 4, -22772)
    app.expect(0x105ef4, 'addi', 5, 29, 1158)
    tick_label = app.cstr(world_labels.addend + 1158)
    app.expect(0x10537c, 'addis', 4, 26, 30)
    app.expect(0x105398, 'lwz', 3, 4, -22772)
    app.expect(0x10539c, 'addi', 0, 3, 1)
    app.expect(0x1053a0, 'stw', 0, 4, -22772)
    app.expect(0x104900, 'stw', 21, 20, -22772)
    app.expect(0x1048fc, 'addi', 21, 0, 0)
    stores = [hex(o) for o in range(0, len(app.code), 4)
              if app.word(o) & 0xFFFF == 0xA70C and app.word(o) >> 26 in (36, 37, 44, 45, 47)]
    require(stores, ['0x104900', '0x1053a0'], 'only reset and increment store mGameTick')
    # Main loop: fixed 31-unit steps, world tick dispatch when (step & 7) == 0, 2000-unit catch-up clamp.
    require(app.call(0x1c22b4), 0x10e844, 'game clock read')
    app.expect(0x1c22b8, 'stw', 3, 24, 0)
    app.expect(0x1c22c4, 'x31', 0, 0, 3, 40, 0)     # now - accumulated
    app.expect(0x1c22c8, 'cmpi', 0, 0, 2000)
    app.expect(0x1c22d0, 'addi', 0, 3, -2000)
    app.expect(0x1c22d4, 'stw', 0, 28, 0)
    app.expect(0x1c22dc, 'lwz', 3, 28, 0)
    app.expect(0x1c22e0, 'addi', 0, 3, 31)
    app.expect(0x1c22e4, 'stw', 0, 28, 0)
    app.expect(0x1c22e8, 'lwz', 3, 26, 0)
    app.expect(0x1c22ec, 'addi', 0, 3, 1)
    app.expect(0x1c22f0, 'stw', 0, 26, 0)
    app.expect(0x1c233c, 'rlwinm', 0, 0, 0, 29, 31, 1)
    require(app.call(0x1c23b8), 0x10536c, 'mode 2 world tick')
    require(app.call(0x1c23ec), 0x10565c, 'mode 1 world tick wrapper')
    require(app.call(0x1056ac), 0x10536c, 'wrapper reaches world tick')
    app.expect(0x1c24c0, 'lwz', 3, 24, 0)
    app.expect(0x1c24c4, 'lwz', 0, 28, 0)
    app.expect(0x1c24c8, 'x31', 0, 3, 0, 0, 0)       # cmpw cr0, now, accumulated
    app.expect(0x1c24cc, 'bc', 12, 1, 0x1c22dc)
    # Scaled game clock behind 0x10e844: LbTime_GetClock deltas times a double rate.
    chain = [(0x10e850, 0x11a588), (0x11a59c, 0x10ed54), (0x10ed80, 0x117d74), (0x117d98, 0x127cd0)]
    for at, target in chain:
        require(app.call(at), target, f'clock chain {at:#x}')
    require(app.glue(app.call(0x127ce4)), 'LbTime_GetClock__Fv', 'scaled clock source')
    app.expect(0x127cf8, 'x31', 4, 4, 3, 40, 0)      # now - last
    app.expect(0x127cfc, 'lfd', 1, 31, 0x18)
    app.expect(0x127d04, 'lfd', 0, 31, 0x10)
    app.expect(0x127d14, 'a63', 0, 2, 0, 1, 29)       # acc = delta * rate + acc
    app.expect(0x127d18, 'stfd', 0, 31, 0x10)
    require(app.call(0x127d24), 0x1c3fbc, 'scaled clock conversion')
    return {'calendar_fields': fields, 'mFunnySecsPerRealSec_initial': 15000,
            'epoch': '2000-01-01 00:00:00.000', 'tick_field': {'label': tick_label.strip(),
                                                               'world_global': f'sec1+{world.addend:#x}',
                                                               'offset': '+0x1da70c'},
            'funny_time': 'epoch + floor(mGameTick * mFunnySecsPerRealSec / 4) s; TbTimeDiff unit 100 ns',
            'main_loop': {'step': 31, 'tick_every_steps': 8, 'catch_up_clamp': 2000,
                          'clock_units': 'LbTime_GetClock ms scaled by double rate at clock+0x18'},
            'derived_if_rate_is_1': {'ms_per_world_tick': 248, 'ticks_per_real_second': 1000 / 248,
                                     'real_seconds_per_game_day': 86400 / 3750 * 0.248}}


def bank_record_mac_order(words: list[int]) -> dict:
    """Interpret 7 bank words + 8x8 loan words in the Mac serializer order."""
    if len(words) != 7 + 64:
        raise ReviewError('bank block needs 71 words')
    head = dict(zip(['mAdmissionFee', 'mBalance', 'mBatchBalance', 'mWithdrawalsEnabled', 'mLastBalance',
                     'mTurnEnteredRed', 'mProfitThisYear'], words[:7]))
    names = ['loan_available', 'amount_available', 'APR_in_percent', 'repayment_period_in_months',
             'monthly_repayment', 'loan_bought', 'months_repaid', 'lenderNameIndex']
    loans = [dict(zip(names, words[7 + 8 * i:15 + 8 * i])) for i in range(8)]
    return {'bank': head, 'loans': loans}


def legacy_i64_amount(words: list[int], loan: int) -> int:
    """OpenTPW SaveEconomyRecords reading: little-endian i64 starting at the amount word."""
    lo = words[7 + 8 * loan + 1] & 0xFFFFFFFF
    hi = words[7 + 8 * loan + 2]
    return (hi << 32) | lo


def pc_save_audit(path: Path) -> dict:
    raw = path.read_bytes()
    require(raw[0x60d:0x611], b'BILZ', 'PC save chunk marker')
    payload = zlib.decompressobj().decompress(raw[0x60d + 28:])
    matches = []
    for start in range(0, len(payload) - 71 * 4):
        if struct.unpack_from('<i', payload, start + 28)[0] not in (0, 1) \
                or not 0 < struct.unpack_from('<i', payload, start + 40)[0] <= 600:
            continue
        words = list(struct.unpack_from('<71i', payload, start))
        record = bank_record_mac_order(words)
        loans = record['loans']
        if all(l['lenderNameIndex'] in range(0, 64) and 0 < l['repayment_period_in_months'] <= 600
               and 0 < l['amount_available'] <= 100_000_000 and l['loan_available'] in (0, 1)
               and l['loan_bought'] in (0, 1) and 0 <= l['APR_in_percent'] <= 100 for l in loans) \
                and record['bank']['mWithdrawalsEnabled'] in (0, 1):
            matches.append((start, record, words))
    require(len(matches), 1, 'unique Mac-order bank block in PC save')
    start, record, words = matches[0]
    for loan in record['loans']:
        expected = min(math.trunc(loan['amount_available'] * math.pow(
            1 + loan['APR_in_percent'] / 100.0, loan['repayment_period_in_months'] / 12 * 0.5)
            / loan['repayment_period_in_months']), 0xFFFFFFFF)
        require(loan['monthly_repayment'], expected, 'PC stored repayment vs Mac formula')
        require(loan['loan_available'], int(loan['amount_available'] <= 10000), 'initial availability rule')
    return {'file': path.name, 'sha256': hashlib.sha256(raw).hexdigest(), 'bank_block_payload_offset': start,
            'loan_table_amount_offset': start + 4 * 8, **record,
            'legacy_i64_amounts': [legacy_i64_amount(words, i) for i in range(8)],
            'limitation': 'One 0 % APR save: matches the Mac order and formula but cannot test APR > 0 arithmetic.'}


def inspect(root: Path, pc_save: Path | None) -> dict:
    app = Binary(root, 'SimThemePark.data', 'SimThemePark.data', APP_TOC)
    bull = Binary(root, 'bullfrog_shared.data', 'libraries/bullfrog_shared.data', 0)
    sams = Binary(root, 'sams_utils_shared.data', 'libraries/sams_utils_shared.data', 0)
    result = {'identities': IDENTITIES, 'timer': timer_audit(app, bull, sams), 'loan': loan_audit(app),
              'calendar': calendar_audit(app, bull)}
    if pc_save:
        result['pc_save'] = pc_save_audit(pc_save)
    return result


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('bin_root', type=Path)
    parser.add_argument('--pc-save', type=Path)
    args = parser.parse_args()
    try:
        result = inspect(args.bin_root, args.pc_save)
    except (OSError, pef.PEFError, ReviewError, zlib.error) as error:
        parser.exit(1, f'review evidence: {error}\n')
    print(json.dumps(result, indent=2, sort_keys=True))


if __name__ == '__main__':
    main()

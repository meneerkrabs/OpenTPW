"""A small, strict PowerPC 32-bit (big-endian, user mode) interpreter for the differential oracle.

Scope (docs/reverse/ORACLE.md): integer ALU, loads/stores, branches and CR logic, multiply/divide,
rotates, and scalar single/double floating point with exact IEEE-754 rounding. Anything else
(system calls, cache/sync instructions, SPRs other than LR/CTR/XER, estimates, unaligned
string ops) raises `Unsupported`, so a routine cannot silently leave the modelled subset.

Memory is a set of explicitly mapped regions; every access outside them raises `MemoryFault`.
Every control transfer is checked against `Machine.allowed_code` (code the oracle chose to run) and
`Machine.stubs` (documented Python replacements); anything else raises `UnknownCall`. Execution
stops when the link register sentinel is reached, and `BudgetExceeded` is raised after
`Machine.budget` instructions.

Floating point: FPRs hold raw 64-bit patterns. Arithmetic on finite operands is evaluated exactly
with `fractions.Fraction` and rounded once to binary64 or binary32 under FPSCR[RN], which is what
the architecture specifies (single-precision ops round the exact result straight to single; no
double rounding). Non-finite operands and exact zero results take Python's double arithmetic,
whose IEEE semantics coincide there. Exception flags are not modelled beyond FPSCR[FX..VX] being
left untouched; routines that read them are out of scope.

Field helpers come from lanes/formats/ppcfields.py so that decoding is shared with the witnesses.
"""
from __future__ import annotations

import math
import os
import struct
import sys
from dataclasses import dataclass, field
from fractions import Fraction
from typing import Callable

sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', 'lanes', 'formats'))

from ppcfields import rlwimi as _rlwimi, rotate_mask, signed16  # noqa: E402

MASK32 = 0xFFFFFFFF
MASK64 = 0xFFFFFFFFFFFFFFFF
RETURN_SENTINEL = 0x7FFF0000  # LR value that ends a call; never mapped as code


class OracleError(Exception):
    """Base class: every abnormal stop of the interpreter."""


class Unsupported(OracleError):
    pass


class MemoryFault(OracleError):
    pass


class UnknownCall(OracleError):
    pass


class BudgetExceeded(OracleError):
    pass


def s32(value: int) -> int:
    value &= MASK32
    return value - (1 << 32) if value & 0x80000000 else value


# ----------------------------------------------------------------------------------- floating point
def bits_to_double(bits: int) -> float:
    return struct.unpack('>d', struct.pack('>Q', bits & MASK64))[0]


def double_to_bits(value: float) -> int:
    return struct.unpack('>Q', struct.pack('>d', value))[0]


def single_bits_to_double(bits: int) -> float:
    return struct.unpack('>f', struct.pack('>I', bits & MASK32))[0]


RN_NEAREST, RN_ZERO, RN_UP, RN_DOWN = 0, 1, 2, 3
BINARY64 = (52, -1022, 1024)  # fraction bits, minimum normal exponent, overflow exponent
BINARY32 = (23, -126, 128)


def _round_integer(n: Fraction, mode: int, negative: bool) -> int:
    """Round a non-negative rational magnitude to an integer under a rounding mode."""
    floor = n.numerator // n.denominator
    rest = n - floor
    if rest == 0:
        return floor
    if mode == RN_NEAREST:
        half = Fraction(1, 2)
        return floor + 1 if rest > half or (rest == half and floor & 1) else floor
    if mode == RN_ZERO:
        return floor
    if mode == RN_UP:
        return floor if negative else floor + 1
    return floor + 1 if negative else floor  # RN_DOWN (toward -inf)


def round_rational(value: Fraction, fmt: tuple[int, int, int], mode: int = RN_NEAREST) -> float:
    """Correctly round a nonzero rational to binary64/binary32 (returned as a Python float)."""
    precision, emin, emax = fmt
    negative = value < 0
    magnitude = -value if negative else value
    exponent = magnitude.numerator.bit_length() - magnitude.denominator.bit_length()
    if Fraction(2) ** exponent > magnitude:
        exponent -= 1
    quantum_exponent = max(exponent, emin) - precision
    scaled = magnitude / (Fraction(2) ** quantum_exponent)
    integer = _round_integer(scaled, mode, negative)
    result = Fraction(integer) * Fraction(2) ** quantum_exponent
    if result >= Fraction(2) ** emax:
        overflow_to_max = mode == RN_ZERO or (mode == RN_UP and negative) or (mode == RN_DOWN and not negative)
        if overflow_to_max:
            largest = (Fraction(2) ** emax) - Fraction(2) ** (emax - 1 - precision)
            return float(-largest if negative else largest)
        return -math.inf if negative else math.inf
    out = float(result)  # exact: result is representable in binary64
    return -out if negative else out


def round_to_single(value: float, mode: int = RN_NEAREST) -> float:
    """frsp: round a double to the nearest single (as a double)."""
    if value == 0 or not math.isfinite(value):
        return single_bits_to_double(struct.unpack('>I', struct.pack('>f', value))[0]) if not math.isnan(value) \
            else value
    return round_rational(Fraction(value), BINARY32, mode)


def _exact(op: str, a: float, b: float, c: float | None = None) -> float | Fraction:
    """Exact result of op, or a float when IEEE special handling applies (non-finite or zero)."""
    if op == 'div' and b == 0:
        return _float_op(op, a, b, c)
    if not all(math.isfinite(x) for x in (a, b) + ((c,) if c is not None else ())):
        return _float_op(op, a, b, c)
    fa, fb = Fraction(a), Fraction(b)
    if op == 'add':
        q = fa + fb
    elif op == 'sub':
        q = fa - fb
    elif op == 'mul':
        q = fa * fb
    elif op == 'div':
        q = fa / fb
    elif op in ('madd', 'msub'):
        product = fa * Fraction(c)
        q = product + fb if op == 'madd' else product - fb
    else:
        raise Unsupported(op)
    if q == 0:
        return _float_op(op, a, b, c)
    return q


def _float_op(op: str, a: float, b: float, c: float | None) -> float:
    try:
        if op == 'add':
            return a + b
        if op == 'sub':
            return a - b
        if op == 'mul':
            return a * b
        if op == 'div':
            if b == 0:
                if a == 0 or math.isnan(a):
                    return math.nan
                return math.copysign(math.inf, a) * math.copysign(1.0, b)
            return a / b
        if op in ('madd', 'msub'):
            addend = b if op == 'madd' else -b
            if math.isfinite(a) and math.isfinite(c):
                if not math.isfinite(addend):
                    return addend  # a finite product never cancels an infinity (no intermediate overflow)
                # Exact zero result (the only finite case routed here): -0 only when both terms are -0.
                product_negative = (math.copysign(1.0, a) < 0) != (math.copysign(1.0, c) < 0)
                zero_product = a == 0 or c == 0
                negative = zero_product and product_negative and addend == 0 and math.copysign(1.0, addend) < 0
                return -0.0 if negative else 0.0
            return a * c + addend
    except (OverflowError, ValueError):
        return math.nan
    raise Unsupported(op)


def fp_arith(op: str, a: float, b: float, c: float | None = None, single: bool = False,
             mode: int = RN_NEAREST) -> float:
    """IEEE result of a scalar FP op; c is the multiplier for madd/msub (a*c ± b)."""
    q = _exact(op, a, b, c)
    if isinstance(q, Fraction):
        return round_rational(q, BINARY32 if single else BINARY64, mode)
    if math.isnan(q):
        # The architecture propagates the first NaN operand (quietened); otherwise the default QNaN.
        for x in (a, b) if c is None else (a, c, b):
            if math.isnan(x):
                return bits_to_double(double_to_bits(x) | 0x0008000000000000)
        return bits_to_double(0x7FF8000000000000)
    return round_to_single(q, mode) if single else q


def double_to_single_store(bits: int) -> int:
    """stfs conversion (Book I 4.6.3): bit selection, no rounding; denormals shifted."""
    exponent = (bits >> 52) & 0x7FF
    if exponent > 896 or exponent == 0x7FF or (exponent == 0 and bits & 0x000FFFFFFFFFFFFF == 0):
        return ((bits >> 32) & 0xC0000000) | ((bits >> 29) & 0x3FFFFFFF)
    if exponent >= 874:  # result is a single denormal
        sign = (bits >> 63) & 1
        mantissa = (1 << 52) | (bits & 0x000FFFFFFFFFFFFF)
        shift = 897 - exponent
        return (sign << 31) | ((mantissa >> (29 + shift)) & 0x7FFFFF)
    return ((bits >> 32) & 0xC0000000) | ((bits >> 29) & 0x3FFFFFFF)  # undefined; architected bit pick


def fctiw(value: float, mode: int) -> int:
    """fctiw/fctiwz: signed word with saturation; NaN gives 0x80000000."""
    if math.isnan(value):
        return 0x80000000
    if math.isinf(value):
        return 0x7FFFFFFF if value > 0 else 0x80000000
    integer = _round_integer(abs(Fraction(value)), mode, value < 0)
    integer = -integer if value < 0 else integer
    if integer > 0x7FFFFFFF:
        return 0x7FFFFFFF
    if integer < -0x80000000:
        return 0x80000000
    return integer & MASK32


# ------------------------------------------------------------------------------------------ memory
@dataclass
class Region:
    name: str
    base: int
    data: bytearray
    writable: bool = True


class Memory:
    """Flat big-endian memory made of explicitly mapped regions; writes are journalled for reset()."""

    def __init__(self):
        self.regions: list[Region] = []
        self._journal: dict[tuple[int, int], bytes] = {}

    def map(self, name: str, base: int, data: bytes | bytearray | int, writable: bool = True) -> Region:
        buffer = bytearray(data)  # an int maps that many zero bytes
        end = base + len(buffer)
        if base < 0x1000 or end > 1 << 32:
            raise MemoryFault(f'region {name} [{base:#x}, {end:#x}) outside the mappable range')
        for region in self.regions:
            if base < region.base + len(region.data) and region.base < end:
                raise MemoryFault(f'region {name} overlaps {region.name}')
        region = Region(name, base, buffer, writable)
        self.regions.append(region)
        return region

    def find(self, address: int, size: int) -> tuple[Region, int]:
        for region in self.regions:
            offset = address - region.base
            if 0 <= offset and offset + size <= len(region.data):
                return region, offset
        raise MemoryFault(f'unmapped access of {size} bytes at {address:#010x}')

    def read(self, address: int, size: int) -> bytes:
        region, offset = self.find(address & MASK32, size)
        return bytes(region.data[offset:offset + size])

    def write(self, address: int, payload: bytes) -> None:
        region, offset = self.find(address & MASK32, len(payload))
        if not region.writable:
            raise MemoryFault(f'write to read-only region {region.name} at {address:#010x}')
        for page in range(offset >> 12, ((offset + len(payload) - 1) >> 12) + 1):
            key = (id(region), page)
            if key not in self._journal:
                self._journal[key] = bytes(region.data[page << 12:(page + 1) << 12])
        region.data[offset:offset + len(payload)] = payload

    def reset(self) -> None:
        """Restore every page written since the last reset."""
        by_id = {id(region): region for region in self.regions}
        for (region_id, page), original in self._journal.items():
            by_id[region_id].data[page << 12:(page << 12) + len(original)] = original
        self._journal.clear()

    def u8(self, a: int) -> int:
        return self.read(a, 1)[0]

    def u16(self, a: int) -> int:
        return struct.unpack('>H', self.read(a, 2))[0]

    def u32(self, a: int) -> int:
        return struct.unpack('>I', self.read(a, 4))[0]

    def u64(self, a: int) -> int:
        return struct.unpack('>Q', self.read(a, 8))[0]

    def w8(self, a: int, v: int) -> None:
        self.write(a, bytes([v & 0xFF]))

    def w16(self, a: int, v: int) -> None:
        self.write(a, struct.pack('>H', v & 0xFFFF))

    def w32(self, a: int, v: int) -> None:
        self.write(a, struct.pack('>I', v & MASK32))

    def w64(self, a: int, v: int) -> None:
        self.write(a, struct.pack('>Q', v & MASK64))


# ----------------------------------------------------------------------------------------- machine
Stub = Callable[['Machine'], None]


@dataclass
class Machine:
    memory: Memory
    allowed_code: list[tuple[int, int]] = field(default_factory=list)  # [start, end) ranges that may execute
    stubs: dict[int, Stub] = field(default_factory=dict)              # entry -> documented replacement
    budget: int = 200_000

    def __post_init__(self):
        self.gpr = [0] * 32
        self.fpr = [0] * 32  # raw IEEE-754 binary64 bits
        self.cr = 0
        self.lr = 0
        self.ctr = 0
        self.xer = 0
        self.fpscr = 0
        self.pc = 0
        self.executed = 0
        self.trace: list[int] | None = None
        self._decoded: dict[int, int] = {}

    # ------------------------------------------------------------------ registers
    def f(self, i: int) -> float:
        return bits_to_double(self.fpr[i])

    def set_f(self, i: int, value: float) -> None:
        self.fpr[i] = double_to_bits(value)

    @property
    def rounding(self) -> int:
        return self.fpscr & 3

    def cr_bit(self, bit: int) -> int:
        return (self.cr >> (31 - bit)) & 1

    def set_cr_bit(self, bit: int, value: int) -> None:
        mask = 1 << (31 - bit)
        self.cr = (self.cr | mask) if value else (self.cr & ~mask & MASK32)

    def set_cr_field(self, field_: int, nibble: int) -> None:
        shift = 28 - 4 * field_
        self.cr = (self.cr & ~(0xF << shift) & MASK32) | ((nibble & 0xF) << shift)

    @property
    def so(self) -> int:
        return (self.xer >> 31) & 1

    @property
    def ca(self) -> int:
        return (self.xer >> 29) & 1

    def set_ca(self, value: int) -> None:
        self.xer = (self.xer | 0x20000000) if value else (self.xer & ~0x20000000 & MASK32)

    def set_ov(self, value: int) -> None:
        self.xer = (self.xer | 0xC0000000) if value else (self.xer & ~0x40000000 & MASK32)

    def record(self, value: int) -> None:
        v = s32(value)
        self.set_cr_field(0, (8 if v < 0 else 4 if v > 0 else 2) | self.so)

    # ------------------------------------------------------------------ control
    def _allowed(self, address: int) -> bool:
        return any(start <= address < end for start, end in self.allowed_code)

    def call(self, entry: int, *, lr: int = RETURN_SENTINEL, stop: frozenset[int] = frozenset()) -> None:
        """Run from entry until a return to lr (the sentinel) or until pc reaches an address in stop
        (fragment runs); stubs run in place of their entry."""
        self.lr = lr
        self.pc = entry
        self._transfer(entry, from_pc=None)
        while self.pc != RETURN_SENTINEL and self.pc not in stop:
            if self.executed >= self.budget:
                raise BudgetExceeded(f'instruction budget {self.budget} exhausted at {self.pc:#x}')
            self.step()

    def _transfer(self, target: int, from_pc: int | None) -> None:
        target &= MASK32
        if target == RETURN_SENTINEL:
            self.pc = target
            return
        stub = self.stubs.get(target)
        if stub is not None:
            stub(self)
            self.pc = self.lr  # a stub returns like blr
            if self.pc != RETURN_SENTINEL and not self._allowed(self.pc) and self.pc not in self.stubs:
                raise UnknownCall(f'stub {target:#x} returned to {self.pc:#x} outside allowed code')
            return
        if target & 3 or not self._allowed(target):
            where = f' from {from_pc:#x}' if from_pc is not None else ''
            raise UnknownCall(f'control transfer to {target:#x}{where} outside allowed code and stubs')
        self.pc = target

    def fetch(self, pc: int) -> int:
        word = self._decoded.get(pc)
        if word is None:
            word = self.memory.u32(pc)
            self._decoded[pc] = word
        return word

    def step(self) -> None:
        pc = self.pc
        if not self._allowed(pc):
            raise UnknownCall(f'execution reached {pc:#x} outside allowed code')
        w = self.fetch(pc)
        self.executed += 1
        if self.trace is not None:
            self.trace.append(pc)
        self.next_pc = pc + 4
        self.branched = False
        op = w >> 26
        handler = _PRIMARY.get(op)
        if handler is None:
            raise Unsupported(f'primary opcode {op} ({w:#010x}) at {pc:#x}')
        handler(self, w)
        if self.branched:
            self._transfer(self.next_pc, pc)
        else:
            self.pc = self.next_pc

    # ------------------------------------------------------------------ helpers for stubs/harness
    def ea(self, ra: int, displacement: int) -> int:
        return ((self.gpr[ra] if ra else 0) + displacement) & MASK32


# ------------------------------------------------------------------------------------ instructions
def _d(w: int) -> tuple[int, int, int]:
    return (w >> 21) & 31, (w >> 16) & 31, signed16(w & 0xFFFF)


def _branch_condition(m: Machine, bo: int, bi: int) -> bool:
    if not bo & 4:
        m.ctr = (m.ctr - 1) & MASK32
    ctr_ok = bool(bo & 4) or ((m.ctr != 0) ^ bool(bo & 2))
    cond_ok = bool(bo & 16) or (m.cr_bit(bi) == ((bo >> 3) & 1))
    return ctr_ok and cond_ok


def op_twi(m, w):
    raise Unsupported(f'trap instruction {w:#010x} at {m.pc:#x}')


def op_mulli(m, w):
    rd, ra, imm = _d(w)
    m.gpr[rd] = (s32(m.gpr[ra]) * imm) & MASK32


def op_subfic(m, w):
    rd, ra, imm = _d(w)
    a = m.gpr[ra]
    m.gpr[rd] = ((~a & MASK32) + (imm & MASK32) + 1) & MASK32
    m.set_ca(((~a & MASK32) + (imm & MASK32) + 1) >> 32)


def op_cmpli(m, w):
    crf, l_bit = (w >> 23) & 7, (w >> 21) & 1
    if l_bit:
        raise Unsupported('64-bit compare')
    a, b = m.gpr[(w >> 16) & 31], w & 0xFFFF
    m.set_cr_field(crf, (8 if a < b else 4 if a > b else 2) | m.so)


def op_cmpi(m, w):
    crf, l_bit = (w >> 23) & 7, (w >> 21) & 1
    if l_bit:
        raise Unsupported('64-bit compare')
    a, b = s32(m.gpr[(w >> 16) & 31]), signed16(w & 0xFFFF)
    m.set_cr_field(crf, (8 if a < b else 4 if a > b else 2) | m.so)


def op_addic(m, w, record=False):
    rd, ra, imm = _d(w)
    total = m.gpr[ra] + (imm & MASK32)
    m.gpr[rd] = total & MASK32
    m.set_ca(total >> 32)
    if record:
        m.record(m.gpr[rd])


def op_addi(m, w):
    rd, ra, imm = _d(w)
    m.gpr[rd] = ((m.gpr[ra] if ra else 0) + imm) & MASK32


def op_addis(m, w):
    rd, ra, imm = _d(w)
    m.gpr[rd] = ((m.gpr[ra] if ra else 0) + (imm << 16)) & MASK32


def op_bc(m, w):
    bo, bi, bd = (w >> 21) & 31, (w >> 16) & 31, signed16(w & 0xFFFC)
    taken = _branch_condition(m, bo, bi)
    if w & 1:
        m.lr = (m.pc + 4) & MASK32
    if taken:
        m.next_pc = (bd if w & 2 else m.pc + bd) & MASK32
        m.branched = True


def op_sc(m, w):
    raise Unsupported(f'system call at {m.pc:#x}')


def op_b(m, w):
    li = w & 0x03FFFFFC
    if li & 0x02000000:
        li -= 0x04000000
    if w & 1:
        m.lr = (m.pc + 4) & MASK32
    m.next_pc = (li if w & 2 else m.pc + li) & MASK32
    m.branched = True


def op_19(m, w):
    xo = (w >> 1) & 0x3FF
    bt, ba, bb = (w >> 21) & 31, (w >> 16) & 31, (w >> 11) & 31
    if xo == 16 or xo == 528:  # bclr / bcctr
        if xo == 528 and not (bt & 4):
            raise Unsupported('bcctr with CTR decrement')
        target = (m.lr if xo == 16 else m.ctr) & ~3 & MASK32
        taken = _branch_condition(m, bt, ba)
        if w & 1:
            m.lr = (m.pc + 4) & MASK32
        if taken:
            m.next_pc = target
            m.branched = True
        return
    if xo == 0:  # mcrf
        src, dst = (w >> 18) & 7, (w >> 23) & 7
        m.set_cr_field(dst, (m.cr >> (28 - 4 * src)) & 0xF)
        return
    if xo == 150:  # isync
        return
    a, b = m.cr_bit(ba), m.cr_bit(bb)
    ops = {257: a & b, 129: a & (1 - b), 289: 1 - (a ^ b), 225: 1 - (a & b), 33: 1 - (a | b),
           449: a | b, 417: a | (1 - b), 193: a ^ b}
    if xo not in ops:
        raise Unsupported(f'opcode 19 xo {xo} at {m.pc:#x}')
    m.set_cr_bit(bt, ops[xo])


def op_rlwimi(m, w):
    rs, ra, sh, mb, me = (w >> 21) & 31, (w >> 16) & 31, (w >> 11) & 31, (w >> 6) & 31, (w >> 1) & 31
    m.gpr[ra] = _rlwimi(m.gpr[ra], m.gpr[rs], sh, mb, me)
    if w & 1:
        m.record(m.gpr[ra])


def _rotl(value: int, n: int) -> int:
    n &= 31
    return ((value << n) | (value >> (32 - n))) & MASK32 if n else value


def op_rlwinm(m, w):
    rs, ra, sh, mb, me = (w >> 21) & 31, (w >> 16) & 31, (w >> 11) & 31, (w >> 6) & 31, (w >> 1) & 31
    m.gpr[ra] = _rotl(m.gpr[rs], sh) & rotate_mask(mb, me)
    if w & 1:
        m.record(m.gpr[ra])


def op_rlwnm(m, w):
    rs, ra, rb, mb, me = (w >> 21) & 31, (w >> 16) & 31, (w >> 11) & 31, (w >> 6) & 31, (w >> 1) & 31
    m.gpr[ra] = _rotl(m.gpr[rs], m.gpr[rb] & 31) & rotate_mask(mb, me)
    if w & 1:
        m.record(m.gpr[ra])


def _logical_imm(fn, shifted=False, record=False):
    def handler(m, w):
        rs, ra, imm = (w >> 21) & 31, (w >> 16) & 31, w & 0xFFFF
        m.gpr[ra] = fn(m.gpr[rs], imm << 16 if shifted else imm) & MASK32
        if record:
            m.record(m.gpr[ra])
    return handler


# ---- loads and stores (D-form); update forms require ra != 0 and, for loads, ra != rd
def _load(size, signed=False, update=False):
    def handler(m, w):
        rd, ra, d = _d(w)
        if update and (ra == 0 or ra == rd):
            raise Unsupported('invalid update load form')
        address = m.ea(ra, d)
        raw = m.memory.read(address, size)
        value = int.from_bytes(raw, 'big')
        if signed and value & (1 << (8 * size - 1)):
            value -= 1 << (8 * size)
        m.gpr[rd] = value & MASK32
        if update:
            m.gpr[ra] = address
    return handler


def _store(size, update=False):
    def handler(m, w):
        rs, ra, d = _d(w)
        if update and ra == 0:
            raise Unsupported('invalid update store form')
        address = m.ea(ra, d)
        m.memory.write(address, (m.gpr[rs] & ((1 << (8 * size)) - 1)).to_bytes(size, 'big'))
        if update:
            m.gpr[ra] = address
    return handler


def op_lmw(m, w):
    rd, ra, d = _d(w)
    address = m.ea(ra, d)
    for r in range(rd, 32):
        m.gpr[r] = m.memory.u32(address)
        address += 4


def op_stmw(m, w):
    rs, ra, d = _d(w)
    address = m.ea(ra, d)
    for r in range(rs, 32):
        m.memory.w32(address, m.gpr[r])
        address += 4


def _fload(double, update=False):
    def handler(m, w):
        rd, ra, d = _d(w)
        if update and ra == 0:
            raise Unsupported('invalid update load form')
        address = m.ea(ra, d)
        _fload_at(m, rd, address, double)
        if update:
            m.gpr[ra] = address
    return handler


def _fload_at(m, rd, address, double):
    if double:
        m.fpr[rd] = m.memory.u64(address)
    else:
        m.set_f(rd, single_bits_to_double(m.memory.u32(address)))  # exact widening; keeps NaN payload top


def _fstore(double, update=False):
    def handler(m, w):
        rs, ra, d = _d(w)
        if update and ra == 0:
            raise Unsupported('invalid update store form')
        address = m.ea(ra, d)
        _fstore_at(m, rs, address, double)
        if update:
            m.gpr[ra] = address
    return handler


def _fstore_at(m, rs, address, double):
    if double:
        m.memory.w64(address, m.fpr[rs])
    else:
        m.memory.w32(address, double_to_single_store(m.fpr[rs]))


# ---- opcode 31
def op_31(m, w):
    rd, ra, rb, xo, rc = (w >> 21) & 31, (w >> 16) & 31, (w >> 11) & 31, (w >> 1) & 0x3FF, w & 1
    g = m.gpr
    a, b = g[ra], g[rb]
    oe = (w >> 10) & 1
    xo9 = xo & 0x1FF
    # XO-form arithmetic (the OE bit sits in the 10-bit field)
    arithmetic = {266, 10, 138, 234, 202, 40, 8, 136, 232, 200, 104, 235, 11, 75, 491, 459}
    if xo9 in arithmetic:
        _arith31(m, xo9, rd, ra, rb, oe, rc)
        return
    if xo in (0, 32):  # cmp / cmpl
        crf = (w >> 23) & 7
        if (w >> 21) & 1:
            raise Unsupported('64-bit compare')
        x, y = (s32(a), s32(b)) if xo == 0 else (a, b)
        m.set_cr_field(crf, (8 if x < y else 4 if x > y else 2) | m.so)
        return
    logical = {28: lambda s, t: s & t, 60: lambda s, t: s & ~t, 444: lambda s, t: s | t,
               412: lambda s, t: s | ~t, 124: lambda s, t: ~(s | t), 476: lambda s, t: ~(s & t),
               316: lambda s, t: s ^ t, 284: lambda s, t: ~(s ^ t)}
    s = g[rd]
    if xo in logical:
        g[ra] = logical[xo](s, b) & MASK32
    elif xo == 24:  # slw
        n = b & 0x3F
        g[ra] = (s << n) & MASK32 if n < 32 else 0
    elif xo == 536:  # srw
        n = b & 0x3F
        g[ra] = s >> n if n < 32 else 0
    elif xo == 792:  # sraw
        n = b & 0x3F
        value = s32(s)
        result = value >> min(n, 31)
        m.set_ca(1 if value < 0 and (n >= 32 or (value & ((1 << n) - 1))) else 0)
        g[ra] = result & MASK32
    elif xo == 824:  # srawi
        n = rb
        value = s32(s)
        m.set_ca(1 if value < 0 and (value & ((1 << n) - 1)) else 0)
        g[ra] = (value >> n) & MASK32
    elif xo == 26:  # cntlzw
        g[ra] = 32 - s.bit_length()
    elif xo == 954:  # extsb
        g[ra] = ((s & 0xFF) - (0x100 if s & 0x80 else 0)) & MASK32
    elif xo == 922:  # extsh
        g[ra] = ((s & 0xFFFF) - (0x10000 if s & 0x8000 else 0)) & MASK32
    elif xo in (339, 467):  # mfspr / mtspr
        spr = ((w >> 16) & 31) | (((w >> 11) & 31) << 5)
        names = {1: 'xer', 8: 'lr', 9: 'ctr'}
        if spr not in names:
            raise Unsupported(f'SPR {spr} at {m.pc:#x}')
        if xo == 339:
            g[rd] = getattr(m, names[spr]) & MASK32
        else:
            setattr(m, names[spr], g[rd] & MASK32)
        return
    elif xo == 19:  # mfcr
        g[rd] = m.cr
        return
    elif xo == 144:  # mtcrf
        fxm = (w >> 12) & 0xFF
        mask = 0
        for i in range(8):
            if fxm & (0x80 >> i):
                mask |= 0xF << (28 - 4 * i)
        m.cr = (s & mask) | (m.cr & ~mask & MASK32)
        return
    elif xo in _INDEXED:
        _INDEXED[xo](m, rd, ra, rb)
        return
    elif xo == 598:  # sync
        return
    else:
        raise Unsupported(f'opcode 31 xo {xo} ({w:#010x}) at {m.pc:#x}')
    if rc:
        m.record(g[ra])


def _arith31(m, xo, rd, ra, rb, oe, rc):
    a, b = m.gpr[ra], m.gpr[rb]
    ov = 0
    if xo == 266:  # add
        result = a + b
        ov = s32(a) + s32(b) != s32(result)
    elif xo in (10, 138, 234, 202):  # addc adde addme addze
        carry_in = m.ca if xo in (138, 234, 202) else 0
        y = {10: b, 138: b, 234: MASK32, 202: 0}[xo]
        result = a + y + carry_in
        m.set_ca(result >> 32)
        ov = s32(a) + s32(y) + carry_in != s32(result)
    elif xo == 40:  # subf
        result = b - a
        ov = s32(b) - s32(a) != s32(result)
    elif xo in (8, 136, 232, 200):  # subfc subfe subfme subfze
        carry_in = 1 if xo == 8 else m.ca
        y = {8: b, 136: b, 232: MASK32, 200: 0}[xo]
        result = (~a & MASK32) + y + carry_in
        m.set_ca(result >> 32)
        ov = s32(~a & MASK32) + s32(y) + carry_in != s32(result)
    elif xo == 104:  # neg
        result = -a
        ov = a == 0x80000000
    elif xo == 235:  # mullw
        product = s32(a) * s32(b)
        result = product
        ov = not -0x80000000 <= product <= 0x7FFFFFFF
    elif xo == 75:  # mulhw
        result = (s32(a) * s32(b)) >> 32
    elif xo == 11:  # mulhwu
        result = (a * b) >> 32
    elif xo == 491:  # divw: undefined results are pinned to 0 and flagged via OV
        x, y = s32(a), s32(b)
        if y == 0 or (x == -0x80000000 and y == -1):
            raise Unsupported(f'divw with undefined result ({x} / {y}) at {m.pc:#x}')
        q = abs(x) // abs(y)
        result = q if (x < 0) == (y < 0) else -q
    elif xo == 459:  # divwu
        if b == 0:
            raise Unsupported(f'divwu by zero at {m.pc:#x}')
        result = a // b
    else:
        raise Unsupported(f'opcode 31 arithmetic xo {xo}')
    m.gpr[rd] = result & MASK32
    if oe:
        if xo in (75, 11):
            raise Unsupported('mulhw with OE')
        m.set_ov(int(bool(ov)))
    if rc:
        m.record(m.gpr[rd])


def _indexed_load(size, signed=False, update=False, reverse=False):
    def handler(m, rd, ra, rb):
        address = ((m.gpr[ra] if ra else 0) + m.gpr[rb]) & MASK32
        raw = m.memory.read(address, size)
        value = int.from_bytes(raw, 'little' if reverse else 'big')
        if signed and value & (1 << (8 * size - 1)):
            value -= 1 << (8 * size)
        m.gpr[rd] = value & MASK32
        if update:
            m.gpr[ra] = address
    return handler


def _indexed_store(size, update=False, reverse=False):
    def handler(m, rs, ra, rb):
        address = ((m.gpr[ra] if ra else 0) + m.gpr[rb]) & MASK32
        m.memory.write(address, (m.gpr[rs] & ((1 << (8 * size)) - 1)).to_bytes(size, 'little' if reverse else 'big'))
        if update:
            m.gpr[ra] = address
    return handler


def _indexed_fload(double, update=False):
    def handler(m, rd, ra, rb):
        address = ((m.gpr[ra] if ra else 0) + m.gpr[rb]) & MASK32
        _fload_at(m, rd, address, double)
        if update:
            m.gpr[ra] = address
    return handler


def _indexed_fstore(double, update=False):
    def handler(m, rs, ra, rb):
        address = ((m.gpr[ra] if ra else 0) + m.gpr[rb]) & MASK32
        _fstore_at(m, rs, address, double)
        if update:
            m.gpr[ra] = address
    return handler


def _stfiwx(m, rs, ra, rb):
    m.memory.w32(((m.gpr[ra] if ra else 0) + m.gpr[rb]) & MASK32, m.fpr[rs] & MASK32)


_INDEXED = {
    23: _indexed_load(4), 55: _indexed_load(4, update=True), 87: _indexed_load(1), 119: _indexed_load(1, update=True),
    279: _indexed_load(2), 311: _indexed_load(2, update=True), 343: _indexed_load(2, signed=True),
    375: _indexed_load(2, signed=True, update=True), 534: _indexed_load(4, reverse=True),
    790: _indexed_load(2, reverse=True),
    151: _indexed_store(4), 183: _indexed_store(4, update=True), 215: _indexed_store(1),
    247: _indexed_store(1, update=True), 407: _indexed_store(2), 439: _indexed_store(2, update=True),
    662: _indexed_store(4, reverse=True), 918: _indexed_store(2, reverse=True),
    535: _indexed_fload(False), 567: _indexed_fload(False, True), 599: _indexed_fload(True),
    631: _indexed_fload(True, True), 663: _indexed_fstore(False), 695: _indexed_fstore(False, True),
    727: _indexed_fstore(True), 759: _indexed_fstore(True, True), 983: _stfiwx,
}


# ---- floating point (opcodes 59 single, 63 double)
def _fcompare(m, crf, a, b):
    if math.isnan(a) or math.isnan(b):
        nibble = 1
    else:
        nibble = 8 if a < b else 4 if a > b else 2
    m.set_cr_field(crf, nibble)
    m.fpscr = (m.fpscr & ~0xF000) | (nibble << 12)


def _fp_record(m, rc):
    if rc:
        raise Unsupported(f'floating-point record form at {m.pc:#x} (CR1 from FPSCR not modelled)')


def op_59(m, w):
    _fp_a_form(m, w, single=True)


def _fp_a_form(m, w, single):
    frt, fra, frb, frc, xo5 = (w >> 21) & 31, (w >> 16) & 31, (w >> 11) & 31, (w >> 6) & 31, (w >> 1) & 31
    _fp_record(m, w & 1)
    a, b, c = m.f(fra), m.f(frb), m.f(frc)
    mode = m.rounding
    if xo5 == 21:
        r = fp_arith('add', a, b, single=single, mode=mode)
    elif xo5 == 20:
        r = fp_arith('sub', a, b, single=single, mode=mode)
    elif xo5 == 25:
        r = fp_arith('mul', a, c, single=single, mode=mode)
    elif xo5 == 18:
        r = fp_arith('div', a, b, single=single, mode=mode)
    elif xo5 in (29, 28, 31, 30):  # fmadd fmsub fnmadd fnmsub: (a*c) ± b
        r = fp_arith('madd' if xo5 in (29, 31) else 'msub', a, b, c, single=single, mode=mode)
        if xo5 in (31, 30) and not math.isnan(r):
            r = -r
    elif xo5 == 23 and not single:  # fsel
        r = c if (not math.isnan(a) and a >= 0) else b
    else:
        raise Unsupported(f'floating opcode {"59" if single else "63"} xo {xo5} ({w:#010x}) at {m.pc:#x}')
    m.set_f(frt, r)


def op_63(m, w):
    xo = (w >> 1) & 0x3FF
    frt, fra, frb = (w >> 21) & 31, (w >> 16) & 31, (w >> 11) & 31
    if (xo & 0x1F) in (18, 20, 21, 23, 25, 28, 29, 30, 31):  # A-form; no X-form xo of opcode 63 collides
        _fp_a_form(m, w, single=False)
        return
    if xo in (0, 32):  # fcmpu / fcmpo
        _fcompare(m, (w >> 23) & 7, m.f(fra), m.f(frb))
        return
    _fp_record(m, w & 1)
    bits = m.fpr[frb]
    if xo == 72:  # fmr
        m.fpr[frt] = bits
    elif xo == 40:  # fneg
        m.fpr[frt] = bits ^ (1 << 63)
    elif xo == 264:  # fabs
        m.fpr[frt] = bits & ~(1 << 63)
    elif xo == 136:  # fnabs
        m.fpr[frt] = bits | (1 << 63)
    elif xo == 12:  # frsp
        value = m.f(frb)
        if math.isnan(value):
            m.fpr[frt] = (bits | 0x0008000000000000) & 0xFFFFFFFFE0000000
        else:
            m.set_f(frt, round_to_single(value, m.rounding))
    elif xo in (14, 15):  # fctiw / fctiwz
        mode = RN_ZERO if xo == 15 else m.rounding
        m.fpr[frt] = 0xFFF8000000000000 | fctiw(m.f(frb), mode)
    elif xo == 583:  # mffs
        m.fpr[frt] = 0xFFF8000000000000 | m.fpscr
    elif xo == 711:  # mtfsf
        flm = (w >> 17) & 0xFF
        mask = 0
        for i in range(8):
            if flm & (0x80 >> i):
                mask |= 0xF << (28 - 4 * i)
        m.fpscr = (bits & mask) | (m.fpscr & ~mask & MASK32)
    elif xo in (38, 70):  # mtfsb1 / mtfsb0
        bit = frt
        mask = 1 << (31 - bit)
        m.fpscr = (m.fpscr | mask) if xo == 38 else (m.fpscr & ~mask & MASK32)
    elif xo == 134:  # mtfsfi
        crf, imm = (w >> 23) & 7, (w >> 12) & 0xF
        shift = 28 - 4 * crf
        m.fpscr = (m.fpscr & ~(0xF << shift) & MASK32) | (imm << shift)
    else:
        raise Unsupported(f'floating opcode 63 xo {xo} ({w:#010x}) at {m.pc:#x}')


_PRIMARY: dict[int, Callable] = {
    3: op_twi, 7: op_mulli, 8: op_subfic, 10: op_cmpli, 11: op_cmpi,
    12: op_addic, 13: lambda m, w: op_addic(m, w, record=True), 14: op_addi, 15: op_addis,
    16: op_bc, 17: op_sc, 18: op_b, 19: op_19, 20: op_rlwimi, 21: op_rlwinm, 23: op_rlwnm,
    24: _logical_imm(lambda s, i: s | i), 25: _logical_imm(lambda s, i: s | i, shifted=True),
    26: _logical_imm(lambda s, i: s ^ i), 27: _logical_imm(lambda s, i: s ^ i, shifted=True),
    28: _logical_imm(lambda s, i: s & i, record=True), 29: _logical_imm(lambda s, i: s & i, shifted=True, record=True),
    31: op_31,
    32: _load(4), 33: _load(4, update=True), 34: _load(1), 35: _load(1, update=True),
    40: _load(2), 41: _load(2, update=True), 42: _load(2, signed=True), 43: _load(2, signed=True, update=True),
    36: _store(4), 37: _store(4, update=True), 38: _store(1), 39: _store(1, update=True),
    44: _store(2), 45: _store(2, update=True), 46: op_lmw, 47: op_stmw,
    48: _fload(False), 49: _fload(False, True), 50: _fload(True), 51: _fload(True, True),
    52: _fstore(False), 53: _fstore(False, True), 54: _fstore(True), 55: _fstore(True, True),
    59: op_59, 63: op_63,
}

SUPPORTED_SUMMARY = (
    'integer: addi addis addic addic. subfic mulli cmpi cmpli cmp cmpl add[c|e|me|ze][o][.] '
    'subf[c|e|me|ze][o][.] neg[o][.] mullw[o][.] mulhw[.] mulhwu[.] divw[o][.] divwu[o][.] '
    'and andc or orc nor nand xor eqv ori oris xori xoris andi. andis. slw srw sraw srawi cntlzw extsb extsh '
    'rlwinm rlwimi rlwnm (all with Rc); '
    'loads/stores: lbz lhz lha lwz (+u, +x, +ux) stb sth stw (+u, +x, +ux) lmw stmw lhbrx lwbrx sthbrx stwbrx; '
    'branches: b bl ba bc bcl bclr bclrl bcctr bcctrl (CTR decrement on bc/bclr), CR ops crand cror crxor '
    'crnand crnor creqv crandc crorc mcrf, mfcr mtcrf, mflr mtlr mfctr mtctr mfxer mtxer, isync sync; '
    'floating: lfs lfd stfs stfd (+u, +x, +ux) stfiwx fadd[s] fsub[s] fmul[s] fdiv[s] fmadd[s] fmsub[s] '
    'fnmadd[s] fnmsub[s] fsel fmr fneg fabs fnabs frsp fctiw fctiwz fcmpu fcmpo mffs mtfsf mtfsfi mtfsb0 mtfsb1'
)

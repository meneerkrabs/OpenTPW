"""Tiny PowerPC encoder for the interpreter's synthetic unit tests (no original bytes involved)."""
from __future__ import annotations


def _r(*fields: tuple[int, int]) -> int:
    """Pack (value, shift) pairs into a word."""
    word = 0
    for value, shift in fields:
        word |= value << shift
    return word & 0xFFFFFFFF


def d(op: int, rt: int, ra: int, imm: int) -> int:
    return _r((op, 26), (rt, 21), (ra, 16), (imm & 0xFFFF, 0))


def x(op: int, rt: int, ra: int, rb: int, xo: int, rc: int = 0) -> int:
    return _r((op, 26), (rt, 21), (ra, 16), (rb, 11), (xo, 1), (rc, 0))


def xo(rt: int, ra: int, rb: int, xo_: int, oe: int = 0, rc: int = 0) -> int:
    return _r((31, 26), (rt, 21), (ra, 16), (rb, 11), (oe, 10), (xo_, 1), (rc, 0))


def a(op: int, frt: int, fra: int, frb: int, frc: int, xo_: int, rc: int = 0) -> int:
    return _r((op, 26), (frt, 21), (fra, 16), (frb, 11), (frc, 6), (xo_, 1), (rc, 0))


def addi(rt, ra, imm): return d(14, rt, ra, imm)
def addis(rt, ra, imm): return d(15, rt, ra, imm)
def li(rt, imm): return addi(rt, 0, imm)
def lis(rt, imm): return addis(rt, 0, imm)
def addic(rt, ra, imm, rc=0): return d(13 if rc else 12, rt, ra, imm)
def subfic(rt, ra, imm): return d(8, rt, ra, imm)
def mulli(rt, ra, imm): return d(7, rt, ra, imm)
def ori(ra, rs, imm): return d(24, rs, ra, imm)
def oris(ra, rs, imm): return d(25, rs, ra, imm)
def xori(ra, rs, imm): return d(26, rs, ra, imm)
def andi_(ra, rs, imm): return d(28, rs, ra, imm)
def cmpwi(crf, ra, imm): return d(11, crf << 2, ra, imm)
def cmplwi(crf, ra, imm): return d(10, crf << 2, ra, imm)
def cmpw(crf, ra, rb): return x(31, crf << 2, ra, rb, 0)
def cmplw(crf, ra, rb): return x(31, crf << 2, ra, rb, 32)
def lwz(rt, disp, ra): return d(32, rt, ra, disp)
def lwzu(rt, disp, ra): return d(33, rt, ra, disp)
def lbz(rt, disp, ra): return d(34, rt, ra, disp)
def lhz(rt, disp, ra): return d(40, rt, ra, disp)
def lha(rt, disp, ra): return d(42, rt, ra, disp)
def stw(rs, disp, ra): return d(36, rs, ra, disp)
def stwu(rs, disp, ra): return d(37, rs, ra, disp)
def stb(rs, disp, ra): return d(38, rs, ra, disp)
def sth(rs, disp, ra): return d(44, rs, ra, disp)
def lmw(rt, disp, ra): return d(46, rt, ra, disp)
def stmw(rs, disp, ra): return d(47, rs, ra, disp)
def lfs(frt, disp, ra): return d(48, frt, ra, disp)
def lfd(frt, disp, ra): return d(50, frt, ra, disp)
def stfs(frs, disp, ra): return d(52, frs, ra, disp)
def stfd(frs, disp, ra): return d(54, frs, ra, disp)
def lwzx(rt, ra, rb): return x(31, rt, ra, rb, 23)
def stwx(rs, ra, rb): return x(31, rs, ra, rb, 151)
def lwbrx(rt, ra, rb): return x(31, rt, ra, rb, 534)
def stfiwx(frs, ra, rb): return x(31, frs, ra, rb, 983)
def rlwinm(ra, rs, sh, mb, me, rc=0): return _r((21, 26), (rs, 21), (ra, 16), (sh, 11), (mb, 6), (me, 1), (rc, 0))
def rlwimi(ra, rs, sh, mb, me, rc=0): return _r((20, 26), (rs, 21), (ra, 16), (sh, 11), (mb, 6), (me, 1), (rc, 0))
def rlwnm(ra, rs, rb, mb, me): return _r((23, 26), (rs, 21), (ra, 16), (rb, 11), (mb, 6), (me, 1))
def logical(xo_, ra, rs, rb, rc=0): return x(31, rs, ra, rb, xo_, rc)
def srawi(ra, rs, sh, rc=0): return x(31, rs, ra, sh, 824, rc)
def unary(xo_, ra, rs, rc=0): return x(31, rs, ra, 0, xo_, rc)
def mfspr(rt, spr): return x(31, rt, spr & 31, spr >> 5, 339)
def mtspr(spr, rs): return x(31, rs, spr & 31, spr >> 5, 467)
def mflr(rt): return mfspr(rt, 8)
def mtlr(rs): return mtspr(8, rs)
def mtctr(rs): return mtspr(9, rs)
def mfcr(rt): return x(31, rt, 0, 0, 19)
def b(offset, link=0, absolute=0): return _r((18, 26), (offset & 0x03FFFFFC, 0), (absolute, 1), (link, 0))
def bc(bo, bi, offset, link=0): return _r((16, 26), (bo, 21), (bi, 16), (offset & 0xFFFC, 0), (link, 0))
def blr(): return 0x4E800020
def bctr(): return 0x4E800420
def bctrl(): return 0x4E800421
def bclr(bo, bi): return _r((19, 26), (bo, 21), (bi, 16), (16, 1))
def crop(xo_, bt, ba, bb): return x(19, bt, ba, bb, xo_)
def sc(): return 0x44000002
def fp(op, xo_, frt, fra=0, frb=0, frc=0): return a(op, frt, fra, frb, frc, xo_)
def fx(xo_, frt, frb, fra=0, rc=0): return x(63, frt, fra, frb, xo_, rc)
def fcmpo(crf, fra, frb): return x(63, crf << 2, fra, frb, 32)
def fcmpu(crf, fra, frb): return x(63, crf << 2, fra, frb, 0)
def mtfsfi(crf, imm): return _r((63, 26), (crf, 23), (imm, 12), (134, 1))

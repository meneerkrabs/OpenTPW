"""Terminal-only listing helper for choosing oracle inputs: prints a code range of the private PEF.

Output goes to stdout for the person or agent wiring a target; it must never be redirected into
the checkout (ORACLE.md, "What stays out of Git"). The decoder covers the interpreter's subset and
prints anything else as `.long`.

Usage: python3 -I disasm.py BIN_ROOT START END   (code offsets, hex)
"""
from __future__ import annotations

import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))

from ppc32 import signed16  # noqa: E402

D_NAMES = {7: 'mulli', 8: 'subfic', 12: 'addic', 13: 'addic.', 14: 'addi', 15: 'addis', 24: 'ori', 25: 'oris',
           26: 'xori', 27: 'xoris', 28: 'andi.', 29: 'andis.', 32: 'lwz', 33: 'lwzu', 34: 'lbz', 35: 'lbzu',
           36: 'stw', 37: 'stwu', 38: 'stb', 39: 'stbu', 40: 'lhz', 41: 'lhzu', 42: 'lha', 43: 'lhau', 44: 'sth',
           45: 'sthu', 46: 'lmw', 47: 'stmw', 48: 'lfs', 49: 'lfsu', 50: 'lfd', 51: 'lfdu', 52: 'stfs',
           53: 'stfsu', 54: 'stfd', 55: 'stfdu'}
X31 = {266: 'add', 10: 'addc', 138: 'adde', 234: 'addme', 202: 'addze', 40: 'subf', 8: 'subfc', 136: 'subfe',
       232: 'subfme', 200: 'subfze', 104: 'neg', 235: 'mullw', 75: 'mulhw', 11: 'mulhwu', 491: 'divw',
       459: 'divwu', 28: 'and', 60: 'andc', 444: 'or', 412: 'orc', 124: 'nor', 476: 'nand', 316: 'xor',
       284: 'eqv', 24: 'slw', 536: 'srw', 792: 'sraw', 824: 'srawi', 26: 'cntlzw', 954: 'extsb', 922: 'extsh',
       0: 'cmpw', 32: 'cmplw', 339: 'mfspr', 467: 'mtspr', 19: 'mfcr', 144: 'mtcrf', 23: 'lwzx', 87: 'lbzx',
       279: 'lhzx', 343: 'lhax', 151: 'stwx', 215: 'stbx', 407: 'sthx', 535: 'lfsx', 599: 'lfdx',
       663: 'stfsx', 727: 'stfdx', 983: 'stfiwx', 55: 'lwzux', 183: 'stwux'}
A_NAMES = {21: 'fadd', 20: 'fsub', 25: 'fmul', 18: 'fdiv', 29: 'fmadd', 28: 'fmsub', 31: 'fnmadd', 30: 'fnmsub',
           23: 'fsel'}
X63 = {0: 'fcmpu', 32: 'fcmpo', 72: 'fmr', 40: 'fneg', 264: 'fabs', 136: 'fnabs', 12: 'frsp', 14: 'fctiw',
       15: 'fctiwz', 583: 'mffs', 711: 'mtfsf'}
CR19 = {257: 'crand', 129: 'crandc', 289: 'creqv', 225: 'crnand', 33: 'crnor', 449: 'cror', 417: 'crorc',
        193: 'crxor'}


def text(w: int, pc: int) -> str:
    op = w >> 26
    rd, ra, rb = (w >> 21) & 31, (w >> 16) & 31, (w >> 11) & 31
    imm = signed16(w & 0xFFFF)
    if op in D_NAMES:
        name = D_NAMES[op]
        if op in (24, 25, 26, 27, 28, 29):
            return f'{name} r{ra},r{rd},{w & 0xFFFF:#x}'
        if op >= 48:
            return f'{name} f{rd},{imm}(r{ra})'
        if op >= 32:
            return f'{name} r{rd},{imm}(r{ra})'
        return f'{name} r{rd},r{ra},{imm}'
    if op in (10, 11):
        return f'{"cmplwi" if op == 10 else "cmpwi"} cr{(w >> 23) & 7},r{ra},{w & 0xFFFF if op == 10 else imm}'
    if op in (20, 21, 23):
        name = {20: 'rlwimi', 21: 'rlwinm', 23: 'rlwnm'}[op] + ('.' if w & 1 else '')
        sh = f'r{rb}' if op == 23 else str(rb)
        return f'{name} r{ra},r{rd},{sh},{(w >> 6) & 31},{(w >> 1) & 31}'
    if op == 18:
        li = w & 0x03FFFFFC
        li -= 0x04000000 if li & 0x02000000 else 0
        return f'b{"l" if w & 1 else ""} {(li if w & 2 else pc + li):#x}'
    if op == 16:
        bd = signed16(w & 0xFFFC)
        return f'bc{"l" if w & 1 else ""} {rd},{ra},{(pc + bd):#x}'
    if op == 19:
        xo = (w >> 1) & 0x3FF
        if xo in (16, 528):
            return f'{"bclr" if xo == 16 else "bcctr"}{"l" if w & 1 else ""} {rd},{ra}'
        if xo in CR19:
            return f'{CR19[xo]} {rd},{ra},{rb}'
    if op == 31:
        xo = (w >> 1) & 0x3FF
        name = X31.get(xo) or X31.get(xo & 0x1FF)
        if name:
            if xo in (339, 467):
                return f'{name} r{rd},{ra | (rb << 5)}'
            if xo == 824:
                return f'srawi r{ra},r{rd},{rb}'
            return f'{name}{"." if w & 1 else ""} r{rd},r{ra},r{rb}'
    if op in (59, 63):
        xo5 = (w >> 1) & 31
        if xo5 in A_NAMES:
            return f'{A_NAMES[xo5]}{"s" if op == 59 else ""} f{rd},f{ra},f{rb},f{(w >> 6) & 31}'
        xo = (w >> 1) & 0x3FF
        if op == 63 and xo in X63:
            return f'{X63[xo]} f{rd}/cr{rd >> 2},f{ra},f{rb}'
    return f'.long {w:#010x}'


def main(argv: list[str]) -> None:
    from harness import Image
    image = Image.load(Path(argv[0]))
    start, end = int(argv[1], 16), int(argv[2], 16)
    for pc in range(start, end, 4):
        w = image.memory.u32(image.code(pc))
        print(f'{pc:#08x}  {text(w, pc)}')


if __name__ == '__main__':
    main(sys.argv[1:])

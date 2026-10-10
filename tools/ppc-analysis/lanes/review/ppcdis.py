"""Print local llvm-mc disassembly of a PEF code range to stdout (never to Git).

Usage: python3 -I dis.py <container.data> <start-hex> <end-hex>
Annotates TOC-slot loads (lwz rX,d(r2)) with relocation targets when --toc is
given and bl targets with import glue names. Review aid only; nothing executes.
"""
from __future__ import annotations

import argparse
import os
import struct
import subprocess
import sys

sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', '..'))
import pef  # noqa: E402

LLVM_MC = os.environ.get('LLVM_MC', '/opt/homebrew/opt/llvm/bin/llvm-mc')
GLUE_TAIL = (0x90410014, 0x800C0000, 0x804C0004, 0x7C0903A6, 0x4E800420)


def glue_name(c: pef.PEFContainer, at: int, toc: int) -> str | None:
    code = c.code.data
    if at < 0 or at + 24 > len(code):
        return None
    w = struct.unpack_from('>6I', code, at)
    if w[0] >> 16 != 0x8182 or w[1:] != GLUE_TAIL:
        return None
    d = w[0] & 0xFFFF
    d = d - 0x10000 if d & 0x8000 else d
    t = c.relocs.get(c.data_section.index, {}).get(toc + d)
    if not t or t.kind != 'import':
        return None
    imp = c.imports[t.target]
    return f'{imp.library}:{imp.name}'


def disassemble(c: pef.PEFContainer, start: int, end: int, toc: int) -> list[str]:
    code = bytes(c.code.data[start:end])
    words = struct.unpack(f'>{len(code) // 4}I', code)
    text = ' '.join(f'0x{b:02x}' for b in code)
    out = subprocess.run([LLVM_MC, '--disassemble', '-triple=powerpc-unknown-unknown'],
                         input=text, capture_output=True, text=True, check=True).stdout
    lines = [l.strip() for l in out.splitlines() if l.strip() and not l.strip().startswith('.text')]
    if len(lines) != len(words):  # fall back to word-by-word when llvm-mc skips invalid words
        lines = []
        for w in words:
            o = subprocess.run([LLVM_MC, '--disassemble', '-triple=powerpc-unknown-unknown'],
                               input=' '.join(f'0x{b:02x}' for b in struct.pack('>I', w)),
                               capture_output=True, text=True).stdout
            ls = [l.strip() for l in o.splitlines() if l.strip() and not l.strip().startswith('.text')]
            lines.append(ls[0] if ls else f'.long 0x{w:08x}')
    rel = c.relocs.get(c.data_section.index, {})
    result = []
    for i, (w, line) in enumerate(zip(words, lines)):
        at = start + 4 * i
        note = ''
        if w >> 26 == 18:
            li = w & 0x03FFFFFC
            li = li - 0x04000000 if li & 0x02000000 else li
            tgt = li if w & 2 else at + li
            note = f' -> {tgt:#x}'
            g = glue_name(c, tgt, toc)
            if g:
                note += f' [{g}]'
        elif w >> 26 in (32, 48, 50, 14) and (w >> 16) & 31 == 2:
            d = w & 0xFFFF
            d = d - 0x10000 if d & 0x8000 else d
            slot = toc + d
            t = rel.get(slot)
            if t is None:
                note = f' ; toc[{slot:#x}] unrelocated'
            elif t.kind == 'import':
                note = f' ; toc[{slot:#x}] import {c.imports[t.target].name}'
            else:
                note = f' ; toc[{slot:#x}] sec{t.target}+{t.addend:#x}'
        result.append(f'{at:#08x}: {w:08x}  {line}{note}')
    return result


def main() -> None:
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument('container')
    p.add_argument('start', type=lambda s: int(s, 16))
    p.add_argument('end', type=lambda s: int(s, 16))
    p.add_argument('--toc', type=lambda s: int(s, 16), default=None)
    a = p.parse_args()
    c = pef.load(a.container)
    toc = a.toc
    if toc is None:
        entry = c.main or c.init
        toc = c.relocs[entry[0]][entry[1] + 4].addend if entry else 0
    print('\n'.join(disassemble(c, a.start, a.end, toc)))


if __name__ == '__main__':
    main()

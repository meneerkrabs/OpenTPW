"""Audit PEF relocation repeat blocks for unit-versus-halfword ambiguity.

The PEF specification counts RelocSmRepeat/RelocLgRepeat blocks in 2-byte
relocation chunks. ``pef.py`` replays the preceding *instruction units*. The
two readings agree only when every repeated block consists of 1-chunk
instructions and contains no nested repeat. This reports, per container, how
many repeat blocks exist and how many would be ambiguous. Metadata only.
"""
from __future__ import annotations

import argparse
import json
import os
import struct
import sys
from pathlib import Path

sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', '..'))
import pef  # noqa: E402


def split_units(instrs: list[int]) -> list[tuple[int, ...]]:
    units, k = [], 0
    while k < len(instrs):
        if instrs[k] >> 13 == 0b101:
            units.append(tuple(instrs[k:k + 2]))
            k += 2
        else:
            units.append((instrs[k],))
            k += 1
    return units


def is_repeat(ins: int) -> bool:
    return ins >> 12 == 0b1001 or ins >> 10 == 0b101100


def audit_instrs(instrs: list[int]) -> dict:
    units = split_units(instrs)
    total = ambiguous = 0
    for i, unit in enumerate(units):
        ins = unit[0]
        if ins >> 12 == 0b1001:
            block = ((ins >> 8) & 0xF) + 1
        elif ins >> 10 == 0b101100:
            block = ((ins >> 6) & 0xF) + 1
        else:
            continue
        total += 1
        prior = units[max(0, i - block):i]
        if len(prior) < block or any(len(u) != 1 or is_repeat(u[0]) for u in prior):
            ambiguous += 1
    return {'repeat_blocks': total, 'ambiguous_blocks': ambiguous}


def audit(path: Path) -> dict:
    raw = path.read_bytes()
    c = pef.PEFContainer(raw, path.name)
    loader = bytes(c.section_by_kind(4).data)
    reloc_count, instr_off = struct.unpack_from('>II', loader, 32)
    o = 56 + 24 * len(c.libraries) + 4 * len(c.imports)
    result = {'file': path.name, 'repeat_blocks': 0, 'ambiguous_blocks': 0}
    for _ in range(reloc_count):
        _sec, _r, count, first = struct.unpack_from('>HHII', loader, o)
        o += 12
        start = instr_off + first
        instrs = [struct.unpack_from('>H', loader, start + 2 * k)[0] for k in range(count)]
        part = audit_instrs(instrs)
        for key in ('repeat_blocks', 'ambiguous_blocks'):
            result[key] += part[key]
    return result


def main() -> None:
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument('files', nargs='+', type=Path)
    a = p.parse_args()
    print(json.dumps([audit(f) for f in sorted(a.files)], indent=1))


if __name__ == '__main__':
    main()

"""Golden cases of the queue and path cell validator 0x535670 (docs/reverse/ORACLE.md, finding F31; QUEUE-012): tp.exe's own routine on synthetic cells.

For the two tool modes (1 path, 3 queue) every combination of the cell's type, flag word, link byte and owner, the structure record's type and links, the line state (is it the last cell, is it
inside a line) and the sticky latches 0x816D50 / 0x816D58 is run through the real validator in the Unicorn sandbox (no money: the park's money is off); this script records the answer and the latches
after it. For the types 4, 9 and 10 the answer depends on the owner's overwrite priority (0x818C14), which is set directly. OpenTPW.Tests replays every row on QueueCellValidator.

Usage: python3 -I tools/ppc-analysis/oracle_x86/validator_golden.py > source/OpenTPW.Tests/QueueCellValidatorGolden.cs   (needs Unicorn and OPENTPW_TPW_EXE)
"""
from __future__ import annotations

import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))

from fence_oracle import FenceOracle, MAP  # noqa: E402

X, Y = 10, 10
TOOL_OWNER = 2996
LATCHES = {'l50': 0x816D50, 'l58': 0x816D58, 'l60': 0x816D60, 'l4c': 0x816D4C}
GLOBALS = {'last': 0x820AB8, 'inside': 0x820AA4, 'ride': 0x818688, 'owner': 0x818698, 'priority': 0x818C10, 'cellPriority': 0x818C14}


def pop(value: int) -> int:
    return bin(value).count('1')


def rows(oracle: FenceOracle):
    sb = oracle.sandbox
    cell = oracle.game + 0x2D8 + (Y * MAP + X) * 0x44
    record = oracle.game + 0x1102D8 + (Y * MAP + X) * 0x28
    neighbour = {1: (0, -1), 4: (1, 0), 0x10: (0, 1), 0x40: (-1, 0)}
    table = sb.alloc(0x2000)
    thing = sb.alloc(64)
    sb.write(0x7CFB90, 'I', table)
    sb.write(table, 'I', thing)
    out = []
    for mode in (3, 1):
        for ctype in (0, 1, 2, 3, 4, 7, 9, 10, 0x15, 0x1E):
            for flags in (0, 0x20, 0x40):
                for st, sl in ((0, 0), (0x19, 0), (0xB, 0x11), (0xB, 0x05), (0xB, 0x15), (0xB, 0x04)):
                    for links in ((0,) if ctype not in (1, 3) else (0, 4, 4 | 0x40, 1)):
                        for owner_ok in ((True,) if ctype != 3 else (True, False)):
                            for last in (0, 1):
                                for inside in (0, 1):
                                    for l50, l58 in ((0, 0), (1, 0), (0, 1)):
                                        for nbtype in ((0,) if not (mode == 1 and ctype == 3) else (0, 3)):
                                            for priority in ((0,) if ctype not in (4, 9, 10) else (0, 2, 3, 4)):
                                                out.append((mode, ctype, flags, st, sl, links, owner_ok, last, inside, l50, l58, nbtype, priority))
    results = []
    for row in out:
        mode, ctype, flags, st, sl, links, owner_ok, last, inside, l50, l58, nbtype, priority = row
        owner = TOOL_OWNER if owner_ok else TOOL_OWNER + 1
        sb.executed = 0
        sb.write(cell + 8, 'I', ctype)
        sb.write(cell + 0xE, 'H', flags)
        sb.write(cell + 0xC, 'B', links)
        sb.write(cell + 0x10, 'H', owner)
        sb.write(record + 8, 'I', st)
        sb.write(record + 0xC, 'B', sl)
        # the neighbour a path-mode queue cell's single link points to
        for bit, (dx, dy) in neighbour.items():
            other = oracle.game + 0x2D8 + ((Y + dy) * MAP + X + dx) * 0x44
            sb.write(other + 8, 'I', 0)
        if mode == 1 and ctype == 3 and pop(links) == 1:
            dx, dy = neighbour[links]
            sb.write(oracle.game + 0x2D8 + ((Y + dy) * MAP + X + dx) * 0x44 + 8, 'I', nbtype)
        values = {'l50': l50, 'l58': l58, 'l60': 0, 'l4c': 0}
        for name, value in values.items():
            sb.write(LATCHES[name], 'I', value)
        for name, value in (('last', last), ('inside', inside), ('ride', inside), ('owner', TOOL_OWNER), ('priority', mode), ('cellPriority', priority)):
            sb.write(GLOBALS[name], 'I', value)
        for address, value in ((0x81AF4C, 0), (0x785A2C, 0), (0x8187F8, 0), (0x81EA14, 0), (0x81B6DC, 0), (0x81B4CC, 0), (0x8186D0, 0), (0x8186D4, 0), (0x816D5C, 0)):
            sb.write(address, 'B' if address == 0x81B4CC else 'I', value)
        answer = sb.call(0x535670, args=(mode, 0), this=cell).eax & 0xFFFFFFFF
        latches = tuple(sb.read(LATCHES[name], 'I') for name in ('l50', 'l58', 'l60', 'l4c'))
        results.append((row, answer, latches))
    return results


def sample(results):
    """Every combination with an answer or latch outcome not seen often yet, and every sixty-first of the rest: a few hundred rows that keep each branch."""
    seen: dict = {}
    chosen = []
    for index, (row, answer, latches) in enumerate(results):
        key = (row[0], row[1], row[2] & 0x60, row[3], answer, latches)
        seen[key] = seen.get(key, 0) + 1
        if seen[key] <= 2 or index % 61 == 0:
            chosen.append((row, answer, latches))
    return chosen


def render() -> str:
    oracle = FenceOracle()
    oracle.start([], [])
    results = sample(rows(oracle))
    out = ['// Generated by tools/ppc-analysis/oracle_x86/validator_golden.py from tp.exe\'s own cell validator 0x535670 run in the x86 oracle (docs/reverse/ORACLE.md, F31).',
           '// Synthetic cells, no original data. Each row: mode, cell type, cell flags, structure type, structure links, cell links, owner is the tool\'s ride, last cell of the line, inside a line, latch 0x816D50,',
           '// latch 0x816D58, the type of the neighbour a queue cell\'s link points to, the owner\'s overwrite priority; then the validator\'s answer and the latches 0x816D50, 0x816D58, 0x816D60 and 0x816D4C after it.',
           'namespace OpenTPW.Tests;', '', 'internal static class QueueCellValidatorGolden', '{',
           '\t/// <summary>The oracle\'s rows.</summary>', '\tpublic static readonly string[] Rows =', '\t[']
    for (mode, ctype, flags, st, sl, links, owner_ok, last, inside, l50, l58, nbtype, priority), answer, latches in results:
        out.append(f'\t\t"{mode} {ctype} {flags} {st} {sl} {links} {int(owner_ok)} {last} {inside} {l50} {l58} {nbtype} {priority} > {answer} {latches[0]} {latches[1]} {latches[2]} {latches[3]}",')
    out += ['\t];', '}', '']
    return '\n'.join(out)


def main() -> int:
    if len(sys.argv) == 3 and sys.argv[1] == '--check':
        if render() != Path(sys.argv[2]).read_text():
            print('QueueCellValidatorGolden.cs differs from the oracle output', file=sys.stderr)
            return 1
        return 0
    print(render(), end='')
    return 0


if __name__ == '__main__':
    sys.exit(main())

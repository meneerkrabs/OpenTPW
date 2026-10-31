"""Golden cases of a ride's demolition with a queue (docs/reverse/ORACLE.md, finding X44; QUEUE-015, ECON-050, QUEUE-003): tp.exe's own 0x527EE0 on synthetic queue layouts.

The demolition of a ride whose type has a queue fills the corner stack with the walk 0x530120, enters the queue tool's mode with the forced-clear flag 0x81D7A8 and calls Back 0x52FE50 until it returns 0: every
point of the stack is popped and the line from it to the new last point is cleared by the executable's own click handler and queue line walker (0x536100, passes 0x32 and 0x86) through the cell remover
0x5367A0, which credits a queue cell's cost while its owner cell is not empty; one cell's cost is debited at the end, and the footprint passes of 0x527EE0 follow. The cases lay chains of every shape, stray
owned cells, a joined path, foreign cells, NoModify and counter values, a front that is no queue cell and a cached size that is 0, and write every cell before and after, the credits and the debits.
OpenTPW.Tests replays them on QueuePaths.ClearAll and the cell remover (`QueueDemolitionTests`).

Usage: python3 -I tools/ppc-analysis/oracle_x86/queue_demolition_golden.py > source/OpenTPW.Tests/QueueDemolitionGolden.cs   (needs Unicorn and OPENTPW_TPW_EXE; `--check FILE` compares)
"""
from __future__ import annotations

import random
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))

from x86_harness import BudgetExceeded, OracleError  # noqa: E402
from queue_oracle import DOWN, LEFT, OPPOSITE, RIGHT, STEP, TYPE_ENTRANCE, TYPE_OBJECT, TYPE_PATH, TYPE_QUEUE, UP, QueueWalk, cell_id  # noqa: E402
from queue_golden import DIRECTIONS, add, chain, queue_cell, random_steps  # noqa: E402

FIELDS = ('type', 'links', 'queue', 'flags', 'owner', 'counter')
MASK = [[1, 1]]   # the object cell and the entrance cell; a case whose anchor is its entrance has the entrance cell alone


def ride(entrance, front_direction, steps, back='open', counters=False, owner=None):
    """The ride's object cell next to its entrance cell (the access cell), and the chain from the entrance."""
    anchor = add(entrance, OPPOSITE[front_direction])
    owner = owner if owner is not None else cell_id(*anchor)
    cells = chain(owner, entrance, front_direction, steps, back=back, counters=counters)
    cells[anchor] = {'type': TYPE_OBJECT, 'links': 0, 'queue': 0, 'flags': 0x40, 'owner': owner, 'counter': 0}
    return anchor, owner, cells


def mask_for(front_direction):
    """The footprint mask the demolition reads: an object cell and the entrance cell, in the row the anchor and the entrance share (the anchor at the left end)."""
    return MASK


def cases():
    rng = random.Random(527)
    out = []
    entrance = (60, 60)
    # chains, front to the right (the mask's row is horizontal)
    for count in (0, 1, 2, 4, 7):
        steps = random_steps(rng, RIGHT, count, 0.5)
        anchor, owner, cells = ride(entrance, RIGHT, steps)
        out.append((f'chain of {count + 1} cells', anchor, entrance, 0, -1, cells))
    for index in range(10):
        while True:
            steps = random_steps(rng, RIGHT, rng.randint(2, 16))
            anchor, owner, cells = ride(entrance, RIGHT, steps, counters=index % 2 == 0)
            if len(cells) == len(steps) + 3:
                break
        out.append((f'random chain {index}', anchor, entrance, 0, -1, cells))
    anchor, owner, base = ride(entrance, RIGHT, [RIGHT, DOWN, DOWN, LEFT])
    positions = [p for p in base if base[p]['type'] == TYPE_QUEUE]
    # joined to a path
    anchor, owner, cells = ride(entrance, RIGHT, [RIGHT, DOWN, DOWN, LEFT], back='path')
    out.append(('chain joined to a path', anchor, entrance, 0, -1, cells))
    anchor, owner, cells = ride(entrance, RIGHT, [RIGHT, DOWN, DOWN, LEFT], back='path-unlinked')
    out.append(('chain beside a path that has no link', anchor, entrance, 0, -1, cells))
    anchor, owner, cells = ride(entrance, RIGHT, [], back='path')
    out.append(('one queue cell joined to a path', anchor, entrance, 0, -1, cells))
    # stray owned queue cells: not linked, linked one way, a branch off the chain
    anchor, owner, cells = ride(entrance, RIGHT, [RIGHT, DOWN, DOWN, LEFT])
    cells[(70, 70)] = queue_cell(0, 0, owner)
    out.append(('stray queue cell of the ride, unlinked', anchor, entrance, 0, -1, cells))
    anchor, owner, cells = ride(entrance, RIGHT, [RIGHT, DOWN, DOWN, LEFT])
    cells[(63, 60)] = queue_cell(LEFT, LEFT, owner)
    cells[(62, 60)]['links'] |= RIGHT
    out.append(('branch off a corner', anchor, entrance, 0, -1, cells))
    anchor, owner, cells = ride(entrance, RIGHT, [RIGHT, RIGHT, RIGHT, DOWN])
    cells[(62, 61)] = queue_cell(UP, UP, owner)
    cells[(62, 60)]['links'] |= DOWN
    out.append(('branch off a straight cell', anchor, entrance, 0, -1, cells))
    # cells of other rides
    anchor, owner, cells = ride(entrance, RIGHT, [RIGHT, DOWN, DOWN, LEFT])
    cells[(62, 61)]['owner'] = cell_id(10, 10)
    out.append(('chain with a cell of another owner', anchor, entrance, 0, -1, cells))
    anchor, owner, cells = ride(entrance, RIGHT, [RIGHT, DOWN, DOWN, LEFT])
    cells[(62, 61)]['owner'] = 0
    out.append(('chain with an unowned cell', anchor, entrance, 0, -1, cells))
    # flags and counters
    for index, flags in enumerate((0x20, 0x40, 0x60, 0x10)):
        anchor, owner, cells = ride(entrance, RIGHT, [RIGHT, DOWN, DOWN, LEFT])
        cells[(62, 61)]['flags'] = flags
        cells[(61, 62)]['flags'] = flags
        out.append((f'chain with flags {flags:#x} on two cells', anchor, entrance, 0, -1, cells))
    anchor, owner, cells = ride(entrance, RIGHT, [RIGHT, DOWN, DOWN, LEFT])
    for position in positions:
        cells[position]['counter'] = 2
    out.append(('chain whose cells were laid twice', anchor, entrance, 0, -1, cells))
    anchor, owner, cells = ride(entrance, RIGHT, [RIGHT, DOWN, DOWN, LEFT])
    for position in positions:
        cells[position]['counter'] = -1
    out.append(('chain with negative counters', anchor, entrance, 0, -1, cells))
    # the cached size
    for size in (0, 1, 3, 9):
        anchor, owner, cells = ride(entrance, RIGHT, [RIGHT, DOWN, DOWN, LEFT])
        out.append((f'chain with a cached size of {size}', anchor, entrance, 0, size, cells))
    # the front is no queue cell
    for kind, links in ((TYPE_PATH, 0), (TYPE_PATH, LEFT), (0, 0), (TYPE_PATH, LEFT | UP)):
        anchor = add(entrance, LEFT)
        owner = cell_id(*anchor)
        cells = {entrance: queue_cell(RIGHT, RIGHT, owner, 0x20, kind=TYPE_ENTRANCE), anchor: {'type': TYPE_OBJECT, 'links': 0, 'queue': 0, 'flags': 0x40, 'owner': owner, 'counter': 0},
                 add(entrance, RIGHT): {'type': kind, 'links': links, 'queue': 0, 'flags': 0, 'owner': 0, 'counter': 0}}
        out.append((f'front cell of type {kind} with links {links}', anchor, entrance, 0, -1, cells))
    # an entrance without a link byte
    for rotation in (0, 90, 180, 270):
        # the footprint is the entrance cell alone, so that the rotation turns nothing in it
        owner = cell_id(*entrance)
        cells = {entrance: queue_cell(0, RIGHT, owner, 0x20, kind=TYPE_ENTRANCE)}
        out.append((f'entrance without links, rotation {rotation}', entrance, entrance, rotation, -1, cells))
    # the front is a queue cell of another ride
    anchor, owner, cells = ride(entrance, RIGHT, [RIGHT, DOWN])
    for position in positions[:3]:
        if position in cells:
            cells[position]['owner'] = cell_id(10, 10)
    out.append(('chain of another owner', anchor, entrance, 0, -1, cells))
    return out


def render() -> str:
    oracle = QueueWalk()
    lines = []
    for case in cases():
        name, anchor, entrance, rotation, size, cells = case
        try:
            result = oracle.demolish(cells, entrance, anchor, rotation=rotation, mask=MASK if anchor != entrance else [[1]], cached_size=size)
        except (BudgetExceeded, OracleError) as error:
            print(f'does not end: {name}: {error}', file=sys.stderr)
            continue
        lines.append(f'case {name}|{anchor[0]} {anchor[1]}|{entrance[0]} {entrance[1]}|{rotation}|{result["size"]}')
        for (x, y), (before, after) in sorted(result['cells'].items()):
            lines.append(f'cell {x} {y}|' + ' '.join(str(before[f]) for f in FIELDS) + '|' + ' '.join(str(after[f]) for f in FIELDS))
        lines.append('credits ' + ' '.join(str(c) for c in result['credits']) + '|debits ' + ' '.join(str(d) for d in result['debits']))
    body = ',\n'.join(f'\t\t"{line}"' for line in lines)
    return '\n'.join([
        '// Generated by tools/ppc-analysis/oracle_x86/queue_demolition_golden.py from tp.exe\'s own demolition 0x527EE0 run in the x86 oracle (docs/reverse/ORACLE.md, X44).',
        '// Synthetic cells, no original data. Per case: the anchor, the entrance cell, the rotation and the cached queue size (+0x40) the demolition read; then each cell as "type links queue flags owner counter"',
        '// before|after; then the credits and debits of the queue cells\' cost (20 at a scrap percentage of 50: 10).',
        'namespace OpenTPW.Tests;', '', 'internal static class QueueDemolitionGolden', '{',
        '\tpublic static readonly string[] Lines =', '\t[', body, '\t];', '}', ''])


def main() -> int:
    if len(sys.argv) == 3 and sys.argv[1] == '--check':
        if render() != Path(sys.argv[2]).read_text():
            print('QueueDemolitionGolden.cs differs from the oracle output', file=sys.stderr)
            return 1
        return 0
    print(render(), end='')
    return 0


if __name__ == '__main__':
    sys.exit(main())

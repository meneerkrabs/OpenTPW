"""Golden cases of the queue tool's corner walk 0x530120 (docs/reverse/ORACLE.md, finding X44; QUEUE-015, ECON-050): tp.exe's own routine on synthetic queue layouts.

The cases lay an entrance cell (type 9) and queue chains (type 3) of random shape and length, a path cell at the back, a front cell that is no queue cell, a front cell of another ride, entrances with no link
byte (the rotation's cell), broken chains (a dead end, a cell off the chain, a cell of another type) and a serpentine longer than the stack, and write for each the stack, the line start, the returned byte and every
cell's type, links, queue link, flags, owner and counter before and after. OpenTPW.Tests replays them on QueueCornerWalk (`QueueCornerWalkTests`).

Usage: python3 -I tools/ppc-analysis/oracle_x86/queue_golden.py > source/OpenTPW.Tests/QueueCornerWalkGolden.cs   (needs Unicorn and OPENTPW_TPW_EXE; `--check FILE` compares)
"""
from __future__ import annotations

import random
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))

from x86_harness import BudgetExceeded  # noqa: E402
from queue_oracle import (DOWN, LEFT, OPPOSITE, RIGHT, STEP, TYPE_ENTRANCE, TYPE_OBJECT, TYPE_PATH, TYPE_QUEUE, UP, QueueWalk, cell_id)  # noqa: E402

DIRECTIONS = (UP, RIGHT, DOWN, LEFT)
FIELDS = ('type', 'links', 'queue', 'flags', 'owner', 'counter')


def add(position, direction):
    return (position[0] + STEP[direction][0], position[1] + STEP[direction][1])


def queue_cell(links, queue, owner, flags=0, counter=0, kind=TYPE_QUEUE):
    return {'type': kind, 'links': links, 'queue': queue, 'flags': flags, 'owner': owner, 'counter': counter}


def chain(owner, entrance, front_direction, steps, back='open', counters=False):
    """An entrance cell linking to a chain of queue cells: each cell links back to the one before it and on to the next. `back` is 'open' (the last cell has the one link back), 'path' (it links on
    to a type-1 path cell that links back), 'path-unlinked' (a path cell beside it that has no link) or 'dead' (the last cell links on to an empty cell)."""
    cells = {entrance: queue_cell(front_direction, front_direction, owner, 0x20, kind=TYPE_ENTRANCE)}
    direction = front_direction
    position = add(entrance, direction)
    for index in range(len(steps) + 1):
        cells[position] = queue_cell(OPPOSITE[direction], OPPOSITE[direction], owner, 0x20 if index == 0 else 0, counter=(index % 3) if counters else 0)
        if index < len(steps):
            direction = steps[index]
            cells[position]['links'] |= direction
            position = add(position, direction)
    last = add(position, direction)
    if back == 'path':
        cells[position]['links'] |= direction
        cells[last] = {'type': TYPE_PATH, 'links': OPPOSITE[direction], 'queue': 0, 'flags': 0, 'owner': owner, 'counter': 0}
    elif back == 'path-unlinked':
        cells[last] = {'type': TYPE_PATH, 'links': 0, 'queue': 0, 'flags': 0, 'owner': owner, 'counter': 0}
    elif back == 'dead':
        cells[position]['links'] |= direction
    return cells


def random_steps(rng, front_direction, count, turn_chance=0.45):
    steps = []
    direction = front_direction
    for _ in range(count):
        if rng.random() < turn_chance:
            direction = rng.choice([d for d in DIRECTIONS if d not in (direction, OPPOSITE[direction])])
        steps.append(direction)
    return steps


def fits(cells, low=4, high=123):
    return all(low <= x <= high and low <= y <= high for x, y in cells) and True


def serpentine(owner, entrance):
    """A zigzag of corner cells across the map, row after row: more corners than the stack holds."""
    steps = []
    for row in range(12):
        horizontal = RIGHT if row % 2 == 0 else LEFT
        for pair in range(60):
            steps.append(horizontal)
            steps.append(DOWN if pair % 2 == 0 else UP)
        steps += [horizontal, DOWN, DOWN, DOWN]
    cells = chain(owner, entrance, RIGHT, steps[:-1])
    assert len(cells) == len(steps) + 1, 'the serpentine crosses itself'
    return cells


def cases():
    rng = random.Random(530120)
    out = []
    owner = cell_id(20, 20)
    other = cell_id(21, 21)
    entrance = (60, 60)
    # straight and bent chains, every front direction
    for front in DIRECTIONS:
        for count in (0, 1, 2, 5):
            steps = random_steps(rng, front, count, 0.5)
            out.append((f'chain of {count + 1} cells, front {front}', entrance, 0, chain(owner, entrance, front, steps)))
    for index in range(14):
        while True:
            front = rng.choice(DIRECTIONS)
            steps = random_steps(rng, front, rng.randint(2, 14))
            cells = chain(owner, entrance, front, steps, counters=index % 2 == 0)
            if len(cells) == len(steps) + 2 and fits(cells, 20, 100):
                break
        out.append((f'random chain {index}', entrance, rng.choice((0, 90, 180, 270)), cells))
    # the back joined to a path, and paths that do not join
    for front in DIRECTIONS:
        out.append((f'chain ending on a path, front {front}', entrance, 0, chain(owner, entrance, front, random_steps(rng, front, 4), back='path')))
    out.append(('chain ending on a path that has no link', entrance, 0, chain(owner, entrance, RIGHT, [RIGHT, DOWN, DOWN], back='path-unlinked')))
    out.append(('chain whose last link leads to an empty cell', entrance, 0, chain(owner, entrance, RIGHT, [RIGHT, DOWN, DOWN], back='dead')))
    for count in (0, 1, 3):
        out.append((f'front is the path, {count + 1} cells', entrance, 0, chain(owner, entrance, DOWN, [DOWN] * count, back='path')))
    # the front cell is no queue cell: a path cell, an empty cell, an object cell, a path cell with a link back
    for kind, links, queue in ((TYPE_PATH, 0, 0), (TYPE_PATH, UP, 0), (0, 0, 0), (TYPE_OBJECT, 0, 0), (TYPE_PATH, UP | LEFT, 0)):
        cells = {entrance: queue_cell(DOWN, DOWN, owner, 0x20, kind=TYPE_ENTRANCE), add(entrance, DOWN): {'type': kind, 'links': links, 'queue': queue, 'flags': 0, 'owner': 0, 'counter': 0}}
        out.append((f'front cell of type {kind} with links {links}', entrance, 0, cells))
    # the front cell is a queue cell of another ride
    cells = chain(other, (60, 61), RIGHT, [RIGHT, DOWN])
    cells[entrance] = queue_cell(DOWN, DOWN, owner, 0x20, kind=TYPE_ENTRANCE)
    cells[(60, 61)]['type'] = TYPE_QUEUE
    cells[(60, 61)]['links'] = UP | RIGHT
    out.append(('front cell is a queue cell of another ride', entrance, 0, cells))
    cells = {entrance: queue_cell(DOWN, DOWN, owner, 0x20, kind=TYPE_ENTRANCE), add(entrance, DOWN): queue_cell(UP, UP, 0)}
    out.append(('front cell is a queue cell that no ride owns', entrance, 0, cells))
    cells = {entrance: queue_cell(DOWN, DOWN, owner, 0x20, kind=TYPE_ENTRANCE), add(entrance, DOWN): queue_cell(UP, UP, owner, kind=TYPE_ENTRANCE)}
    out.append(('front cell is another entrance cell of the ride', entrance, 0, cells))
    # an entrance with no link byte: the cell its queue link names, turned by the rotation
    for rotation in (0, 90, 180, 270):
        for queue in (UP, RIGHT, DOWN, LEFT, 0):
            cells = {entrance: queue_cell(0, queue, owner, 0x20, kind=TYPE_ENTRANCE)}
            out.append((f'entrance without links, queue link {queue}, rotation {rotation}', entrance, rotation, cells))
    # an entrance on the map's edge looking out
    for corner, front in (((0, 5), LEFT), ((5, 0), UP), ((127, 5), RIGHT), ((5, 127), DOWN)):
        cells = {corner: queue_cell(front, front, owner, 0x20, kind=TYPE_ENTRANCE)}
        out.append((f'entrance at the edge {corner} looking out', corner, 0, cells))
    # broken chains
    steps = random_steps(rng, RIGHT, 6, 0.5)
    base = chain(owner, entrance, RIGHT, steps)
    positions = list(base)
    for index in (2, 3, 4, 6):
        cells = {key: dict(value) for key, value in base.items()}
        cells[positions[index]]['links'] = cells[positions[index]]['queue']
        out.append((f'chain with a one-link cell at {index}', entrance, 0, cells))
    cells = {key: dict(value) for key, value in base.items()}
    cells[positions[3]]['type'] = TYPE_PATH
    out.append(('chain with a path cell in the middle', entrance, 0, cells))
    cells = {key: dict(value) for key, value in base.items()}
    cells[positions[3]]['type'] = TYPE_OBJECT
    out.append(('chain with an object cell in the middle', entrance, 0, cells))
    cells = {key: dict(value) for key, value in base.items()}
    cells[positions[3]]['links'] |= UP | DOWN | LEFT | RIGHT
    out.append(('chain with a four-way cell', entrance, 0, cells))
    cells = {key: dict(value) for key, value in base.items()}
    cells[positions[2]]['links'] |= UP if cells[positions[2]]['links'] & UP == 0 else DOWN
    out.append(('chain with a branch cell', entrance, 0, cells))
    cells = {key: dict(value) for key, value in base.items()}
    cells[positions[4]]['owner'] = other
    out.append(('chain with a cell of another owner', entrance, 0, cells))
    # a corner at the back cell that leads on to a path
    cells = chain(owner, entrance, RIGHT, [RIGHT, DOWN], back='path')
    out.append(('chain with a corner before the path', entrance, 0, cells))
    # more corners than the stack holds
    out.append(('serpentine', (4, 4), 0, serpentine(owner, (4, 4))))
    return out


def render() -> str:
    oracle = QueueWalk()
    lines = []
    for name, entrance, rotation, cells in cases():
        try:
            result = oracle.walk(cells, entrance, rotation)
        except BudgetExceeded:
            print(f'does not end: {name}', file=sys.stderr)
            continue
        lines.append(f'case {name}|{entrance[0]} {entrance[1]}|{rotation}')
        for (x, y), (before, after) in sorted(result['cells'].items()):
            lines.append(f'cell {x} {y}|' + ' '.join(str(before[f]) for f in FIELDS) + '|' + ' '.join(str(after[f]) for f in FIELDS))
        stack = ';'.join(f'{x},{y}' for x, y in result['stack'])
        lines.append(f'stack {result["count"]}|{result["result"]}|{result["start"][0]} {result["start"][1]}|{stack}')
    body = ',\n'.join(f'\t\t"{line}"' for line in lines)
    return '\n'.join([
        '// Generated by tools/ppc-analysis/oracle_x86/queue_golden.py from tp.exe\'s own corner walk 0x530120 run in the x86 oracle (docs/reverse/ORACLE.md, X44).',
        '// Synthetic cells, no original data. Per case: the entrance cell and the ride\'s rotation; then each cell as "type links queue flags owner counter" before|after; then "stack count|returned byte|line start|points".',
        'namespace OpenTPW.Tests;', '', 'internal static class QueueCornerWalkGolden', '{',
        '\tpublic static readonly string[] Lines =', '\t[', body, '\t];', '}', ''])


def main() -> int:
    if len(sys.argv) == 3 and sys.argv[1] == '--check':
        if render() != Path(sys.argv[2]).read_text():
            print('QueueCornerWalkGolden.cs differs from the oracle output', file=sys.stderr)
            return 1
        return 0
    print(render(), end='')
    return 0


if __name__ == '__main__':
    sys.exit(main())

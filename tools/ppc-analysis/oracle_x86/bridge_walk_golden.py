"""Golden cases of the bridge walks 0x52C6F0 and 0x52CCB0 (docs/reverse/ORACLE.md, finding X46; TRACK-010): tp.exe's own routines on synthetic second-layer records.

Each case places a kart or water ride at a random rotation (the placement of finding X38 gives its station tiles, exit and entry), then rewrites the records around it: a random self-avoiding route from the exit
tile to the entry tile on the tile lattice (arms follow the route, the exit and entry keep their arm toward the station), cut, branched, crossed or left dangling at random, with random bridge flag words
(0, 1, 2, 4, 8 and a stray add-on bit 0x10), ride words (the ride's, another ride's, none), station tiles and kind 25 (hoarding) records of other rides on the lattice beside the route, and misaligned tiles whose
extension cells lie on the lattice. The case then runs the op 0x86 pair as the line walker runs it: the forward walk and, when it answers 0, the reverse walk. Layouts on which a walk never ends (an arm that
leads into empty ground: the executable reads past the array there) are left out. Per case the golden records every record that holds something before and after, and the two answers.
OpenTPW.Tests replays them on TrackBridges (`TrackBridgeWalkGoldenTests`).

Usage: python3 -I tools/ppc-analysis/oracle_x86/bridge_walk_golden.py > source/OpenTPW.Tests/TrackBridgeWalkGolden.cs   (needs Unicorn and OPENTPW_TPW_EXE; `--check FILE` compares)
"""
from __future__ import annotations

import random
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))

from bridge_walk_oracle import BridgeWalkOracle, DOWN, LEFT, OPPOSITE, RIGHT, STEP, UP, cell_word  # noqa: E402
from fence_oracle import MAP  # noqa: E402
from x86_harness import BudgetExceeded  # noqa: E402

DIRECTIONS = (UP, RIGHT, DOWN, LEFT)
CLICK = (60, 60)
RADIUS = 14


def lattice_step(point, direction):
    return (point[0] + 2 * STEP[direction][0], point[1] + 2 * STEP[direction][1])


def direction_between(a, b):
    for direction in DIRECTIONS:
        if lattice_step(a, direction) == b:
            return direction
    raise ValueError((a, b))


def find_route(rng, start, goal, blocked, limit):
    """A random self-avoiding lattice route from start to goal (inclusive) of at most `limit` tiles, or None."""
    route = [start]
    seen = {start}

    def extend():
        if route[-1] == goal:
            return True
        if len(route) >= limit:
            return False
        options = [d for d in DIRECTIONS if (n := lattice_step(route[-1], d)) not in seen and n not in blocked and abs(n[0] - goal[0]) <= 2 * 6 and abs(n[1] - goal[1]) <= 2 * 6]
        rng.shuffle(options)
        for direction in options:
            nxt = lattice_step(route[-1], direction)
            route.append(nxt)
            seen.add(nxt)
            if extend():
                return True
            route.pop()
            seen.discard(nxt)
        return False

    return route if extend() else None


def tile(x, y, kind, arms, flags, word):
    return {'x': x, 'y': y, 'kind': kind, 'arms': arms, 'flags': flags, 'word': word}


PLACED: dict = {}


def placed(oracle, rotation, track_type):
    """The map after the ride's placement (made once per rotation and type): the cells, the records, the anchor, the exit and entry tiles and the heading."""
    key = (rotation, track_type)
    if key not in PLACED:
        oracle.place(CLICK, rotation, track_type)
        PLACED[key] = (bytes(oracle.cells), bytes(oracle.records), oracle.anchor(), oracle.exit, oracle.entry, oracle.heading)
    cells, records, anchor, exit_tile, entry_tile, heading = PLACED[key]
    oracle.cells, oracle.records = bytearray(cells), bytearray(records)
    oracle.exit, oracle.entry, oracle.heading = exit_tile, entry_tile, heading
    oracle.flush()
    return anchor


def build_case(rng, oracle, index):
    rotation = rng.choice((0, 90, 180, 270))
    track_type = rng.choice((1, 2))
    anchor = placed(oracle, rotation, track_type)
    word = cell_word(*anchor)
    exit_tile, entry_tile, heading = oracle.exit, oracle.entry, oracle.heading
    # the exit tile of the oracle is the cell after the second station tile on the lattice; the station tiles lie between the entry and the exit
    first = lattice_step(entry_tile, heading)
    second = lattice_step(first, heading)
    station = {first, second}
    ordinary = rng.choice((11, 16)) if track_type == 1 else rng.choice((16, 11))
    tiles = {}
    route = None
    for _ in range(40):
        route = find_route(rng, exit_tile, entry_tile, station, rng.randint(5, 26))
        if route:
            break
    assert route, 'no route'
    name = [f'rotation {rotation}, {"kart" if track_type == 1 else "water"}, route of {len(route)}']
    for position, point in enumerate(route):
        arms = 0
        if position > 0:
            arms |= direction_between(point, route[position - 1])
        if position < len(route) - 1:
            arms |= direction_between(point, route[position + 1])
        tiles[point] = tile(point[0], point[1], ordinary, arms, 0, word)
    tiles[exit_tile]['arms'] |= OPPOSITE[heading]
    tiles[entry_tile]['arms'] |= heading
    # the station tiles: the axis arms and the ride's word
    station_kind = 13 if track_type == 1 else 18
    tiles[first] = tile(first[0], first[1], station_kind, heading | OPPOSITE[heading], 0, word)
    tiles[second] = tile(second[0], second[1], station_kind, heading | OPPOSITE[heading], 0, word)
    # random mutations
    mutation = rng.random()
    points = list(route)
    if mutation < 0.18 and len(points) > 4:
        # an open track: cut after a random tile, the last tile keeps only its arm back or also an arm into the gap
        cut = rng.randint(1, len(points) - 2)
        for point in points[cut + 1:]:
            if point not in (exit_tile, entry_tile):
                del tiles[point]
        last = points[cut]
        if rng.random() < 0.5:
            tiles[last]['arms'] &= ~direction_between(last, points[cut + 1])
        name.append(f'cut at {cut}')
    elif mutation < 0.3:
        # a junction: a spur tile beside a route tile, with the arm both ways
        for _ in range(rng.randint(1, 3)):
            base = rng.choice(points[1:-1] or points)
            direction = rng.choice(DIRECTIONS)
            spur = lattice_step(base, direction)
            if spur in tiles or spur in station or spur in (exit_tile, entry_tile) or not (3 <= spur[0] <= 124 and 3 <= spur[1] <= 124):
                continue
            tiles[spur] = tile(spur[0], spur[1], ordinary, OPPOSITE[direction], 0, word)
            tiles[base]['arms'] |= direction
        name.append('spurs')
    elif mutation < 0.36:
        # the exit or the entry loses its arm toward the station
        point, arm = rng.choice(((exit_tile, OPPOSITE[heading]), (entry_tile, heading)))
        tiles[point]['arms'] &= ~arm
        name.append('station arm lost')
    elif mutation < 0.42:
        # a tile of another ride on the route
        victim = rng.choice(points[1:-1] or points)
        tiles[victim]['word'] = rng.choice((0, word + 7, word + 123))
        name.append('tile of another ride')
    # random flag words (the walk only asks whether the nibble is set, and assigns whole words)
    for point, record in tiles.items():
        if record['kind'] in (11, 16):
            straight = record['arms'] in (0x11, 0x44)
            roll = rng.random()
            if roll < (0.65 if straight else 0.12):
                record['flags'] = rng.choice((1, 1, 1, 2, 4, 8))
            if rng.random() < 0.05:
                record['flags'] |= 0x10
    extras = []
    # records of other things on the lattice beside the route
    if rng.random() < 0.35:
        for _ in range(rng.randint(1, 3)):
            base = rng.choice(points)
            direction = rng.choice(DIRECTIONS)
            spot = lattice_step(base, direction)
            if spot in tiles or spot in station or not (3 <= spot[0] <= 124 and 3 <= spot[1] <= 124):
                continue
            kind = rng.choice((13, 18, 25, 25, 11))
            extras.append(tile(spot[0], spot[1], kind, rng.choice((0, 0, 0x44, 0x11)) if kind != 25 else 0, rng.choice((0, 1)), rng.choice((word, word + 5, 0))))
            name.append(f'record of kind {kind} beside the route')
    return rotation, track_type, anchor, list(tiles.values()), extras, ' '.join(name)


def region(anchor):
    return [(x, y) for y in range(max(0, anchor[1] - RADIUS), min(MAP, anchor[1] + RADIUS + 1)) for x in range(max(0, anchor[0] - RADIUS), min(MAP, anchor[0] + RADIUS + 1))]


def record_lines(rows, after):
    out = []
    for (x, y), before, post in zip(region_cells, rows, after):
        if before[0] == 0 and post[0] == 0:
            continue
        out.append(f'rec {x} {y}|{before[0]} {before[1]} {before[2]} {before[3]} {before[4]}|{post[3]}')
    return out


region_cells: list = []


def render() -> str:
    global region_cells
    rng = random.Random(52_6_6_0)
    oracle = BridgeWalkOracle()
    lines = []
    produced = 0
    attempts = 0
    while produced < 700 and attempts < 5000:
        attempts += 1
        rotation, track_type, anchor, tiles, extras, name = build_case(rng, oracle, attempts)
        region_cells = region(anchor)
        oracle.lay(tiles + extras)
        before = oracle.state(region_cells)
        try:
            forward, reverse = oracle.walk(anchor, rotation)
        except BudgetExceeded:
            continue
        after = oracle.state(region_cells)
        lines.append(f'case {produced}: {name}|{anchor[0]} {anchor[1]}|{rotation}|{track_type}')
        lines.extend(record_lines(before, after))
        lines.append(f'walk {forward}|{-1 if reverse is None else reverse}')
        produced += 1
    body = ',\n'.join(f'\t\t"{line}"' for line in lines)
    return '\n'.join([
        '// Generated by tools/ppc-analysis/oracle_x86/bridge_walk_golden.py from tp.exe\'s own bridge walks 0x52C6F0 and 0x52CCB0 run in the x86 oracle (docs/reverse/ORACLE.md, X46).',
        '// Synthetic records, no original data. Per case: the ride\'s anchor cell, rotation and track type (1 kart, 2 water); then each record that holds something as "kind arms link flags word" before | the flag word after;',
        '// then "walk forward|reverse" (the reverse walk is -1 when the forward walk answered 1).',
        'namespace OpenTPW.Tests;', '', 'internal static class TrackBridgeWalkGolden', '{',
        '\tpublic static readonly string[] Lines =', '\t[', body, '\t];', '}', ''])


def main() -> int:
    if len(sys.argv) == 3 and sys.argv[1] == '--check':
        if render() != Path(sys.argv[2]).read_text():
            print('TrackBridgeWalkGolden.cs differs from the oracle output', file=sys.stderr)
            return 1
        return 0
    print(render(), end='')
    return 0


if __name__ == '__main__':
    sys.exit(main())

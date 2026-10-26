"""Python reading of tp.exe's visible-cell cache lookup 0x572440 (docs/LND.md, "The cache"): a ring of nodes, a 128 x 128 key table, a miss taking the node before the head, a hit moving its node
in front of the node that was the head when the call began. The x86 oracle target `terrain_cache` compares this reading with the original's code on random sequences of frames; `terrain_cache_golden.py`
turns the same sequences into the C# test's expectations."""
from __future__ import annotations

import random


class Cache:
    def __init__(self, capacity: int) -> None:
        self.capacity = capacity
        self.prev = [capacity - 1 if node == 0 else node - 1 for node in range(capacity)]
        self.next = [0 if node == capacity - 1 else node + 1 for node in range(capacity)]
        self.owner = [-1] * capacity
        self.table: dict[int, int] = {}
        self.head = capacity >> 1

    @staticmethod
    def entry(key: int) -> int:
        return ((key >> 8) << 7) + (key & 0xFF)

    def resolve(self, keys: list[int], dirty: list[bool]) -> tuple[list[int], list[bool]]:
        frame_start = self.head
        slots, builds = [], []
        for index, key in enumerate(keys):
            entry = self.entry(key)
            node = self.table.get(entry, -1)
            if node < 0:
                taken = self.prev[self.head]
                self.head = taken
                if self.owner[taken] >= 0:
                    self.table.pop(self.owner[taken], None)
                self.table[entry] = taken
                self.owner[taken] = entry
                slots.append(taken)
                builds.append(True)
                continue
            slots.append(node)
            builds.append(dirty[index])
            if node == frame_start:
                frame_start = self.next[frame_start]
                continue
            self.next[self.prev[node]] = self.next[node]
            self.prev[self.next[node]] = self.prev[node]
            self.next[self.prev[frame_start]] = node
            self.prev[node] = self.prev[frame_start]
            self.next[node] = frame_start
            self.prev[frame_start] = node
            if frame_start == self.head:
                self.head = self.prev[self.head]
        return slots, builds

    def ring(self) -> list[int]:
        order, node = [], self.head
        for _ in range(self.capacity):
            order.append(node)
            node = self.next[node]
        return order


def random_sequences(rng: random.Random, n: int) -> list[dict]:
    """n runs of frames over a small universe of cells: 3 to 12 nodes; each frame lists the cells of a window that drifts over the universe, row by row, a part of them left out at random and at most
    capacity + 3 of them (a frame can list more cells than the cache holds), some hits marked dirty."""
    runs = []
    for _ in range(n):
        capacity = rng.randint(3, 12)
        columns, rows = rng.choice([(6, 3), (9, 4), (14, 3)])
        frames = []
        left = rng.randint(0, 3)
        for _ in range(rng.randint(3, 16)):
            left = max(0, min(columns - 1, left + rng.choice([-2, -1, 0, 0, 1, 2])))
            width = rng.randint(1, 4)
            keep = rng.choice([1.0, 0.8, 0.5])
            cells = [(x, z) for z in range(rows) for x in range(left, min(columns, left + width)) if rng.random() < keep]
            cells = cells[:capacity + 3]
            frames.append({'keys': [(z << 8) + x for x, z in cells], 'dirty': [rng.random() < 0.1 for _ in cells]})
        runs.append({'capacity': capacity, 'frames': frames})
    return runs

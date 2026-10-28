"""Second-layer track records on the park fence's cells, run in the x86 oracle (docs/reverse/ORACLE.md, finding X37; PATH-007).

`fence_oracle.FenceOracle` runs tp.exe's own fence routines on a synthetic 128 x 128 map whose structure records are all blank. This module lays synthetic track records on it (the records the track
tool writes: kart or water origin 0xB / 0x10, station 0xD / 0x12, extension 0xC; the track-mode links +0xC, the walk link +0xD, the flags +0xE with the bridge bits and the add-on bit 0x10, the word +0x10: the
ride word of an origin or station, the origin's cell word of an extension) and runs the fence set-up and Buy Land purchases over them, so the lattice of a purchase edge crosses the track records. Nothing here
carries original data: every record is synthetic.
"""
from __future__ import annotations

import hashlib
import struct
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))

from fence_oracle import CELLS, MAP, FenceOracle  # noqa: E402
from x86_harness import BudgetExceeded, MemoryFault  # noqa: E402

EXTENSION = 0xC


class TrackFenceOracle(FenceOracle):
    """A fence oracle that can lay synthetic track records after the set-up and report them back."""

    def purchase(self, post_a, post_b, first, second) -> None:
        """0x5327E0 from a fresh line tool: the previous-step globals 0x763B00 / 0x763B04 (the line walker's link-back reads the last step of whatever line ran before) hold 0x80, as the tool resets (0x525C06, 0x5275D4) leave them and as OpenTPW starts every purchase."""
        for address in (0x763B00, 0x763B04):
            self.sandbox.write(address, 'I', 0x80)
        super().purchase(post_a, post_b, first, second)

    def lay_records(self, tracks) -> None:
        """tracks: (x, y, type, flags +0xE, word +0x10, mask +0xC, link +0xD); the oracle map's fence-mode records are rewritten in place."""
        self.pull()
        for x, y, kind, flags, word, mask, link, *rest in tracks:
            counter = rest[0] if rest else 0
            base = (y * MAP + x) * 0x28
            struct.pack_into('<I', self.records, base + 8, kind)
            self.records[base + 0xC] = mask
            self.records[base + 0xD] = link
            self.records[base + 0xE] = flags
            struct.pack_into('<H', self.records, base + 0x10, word)
            struct.pack_into('<H', self.records, base + 0x20, counter)
        self.flush()

    def track_state(self, tracks) -> list[tuple[int, ...]]:
        """For every laid record: type +8, mask +0xC, link +0xD, flags +0xE, word +0x10, fence links +0x22, counter +0x20."""
        out = []
        for x, y, *_ in tracks:
            base = (y * MAP + x) * 0x28
            out.append((struct.unpack_from('<I', self.records, base + 8)[0], self.records[base + 0xC], self.records[base + 0xD], self.records[base + 0xE],
                        struct.unpack_from('<H', self.records, base + 0x10)[0], self.records[base + 0x22], struct.unpack_from('<H', self.records, base + 0x20)[0]))
        return out

    def layer_sha256_without(self, skip: set[int]) -> str:
        """The fence layer's hash of golden.layer_sha256 with the track cells left out."""
        digest = hashlib.sha256()
        for index in range(CELLS):
            if index in skip:
                digest.update(bytes(5))
                continue
            kind, links, owner, direction = self.record(index)
            digest.update(bytes((kind, links, direction)) + struct.pack('<H', owner))
        return digest.hexdigest()


def tile(x, y, kind=0xB, word=0, flags=0, mask=5, link=0):
    """A 2 x 2 track tile: the origin at (x, y) with the ride word, three extension cells naming the origin."""
    cells = [(x, y, kind, flags, word, mask, link)]
    origin = y * MAP + x + 1
    for dx, dy in ((1, 0), (0, 1), (1, 1)):
        cells.append((x + dx, y + dy, EXTENSION, flags, origin, mask, link))
    return cells


def owned_tiles(spec, oracle: TrackFenceOracle):
    """The tiles of spec['tracks'] (four cells each, origin first) that a real track tool could have laid after the set-up: every cell owned (flag 0x40 clear) and without a structure record."""
    oracle.start(spec['terrain'], spec['no_modify'])
    oracle.lay_fence(spec['a'], spec['b'], spec['clicks'])
    oracle.pull()
    keep = []
    for at in range(0, len(spec['tracks']), 4):
        tile_cells = spec['tracks'][at:at + 4]
        if all(not oracle.cell_flags(y * MAP + x) & 0x40 and oracle.record(y * MAP + x)[0] == 0 for x, y, *_ in tile_cells):
            keep += tile_cells
    return keep


def run(spec, oracle: TrackFenceOracle, realistic=False):
    """spec: a fence_golden case plus 'tracks' (4-cell tiles). Returns the comparable outcome; with realistic only the tiles a track tool could have laid (owned, free cells) are kept (spec['tracks'] is rewritten)."""
    if realistic:
        spec['tracks'] = owned_tiles(spec, oracle)
    tracks = spec['tracks']
    skip = {y * MAP + x for x, y, *_ in tracks}

    def build(applied):
        oracle.start(spec['terrain'], spec['no_modify'])
        oracle.lay_fence(spec['a'], spec['b'], spec['clicks'])
        oracle.lay_records(tracks)
        for first, second in applied:
            oracle.purchase(spec['a'], spec['b'], first, second)

    build([])
    ops = []
    applied = []
    for first, second in spec['buys']:
        accepted, cells = oracle.validate(spec['a'], spec['b'], first, second)
        faults = False
        if accepted:
            try:
                oracle.purchase(spec['a'], spec['b'], first, second)
                applied.append((first, second))
            except (MemoryFault, BudgetExceeded):
                faults = True
                build(applied)
        ops.append((first, second, accepted, cells, faults))
    oracle.pull()
    return {'ops': ops, 'runs': oracle.flag_runs(), 'layer': oracle.layer_sha256_without(skip), 'owned': oracle.owned_cells(),
            'posts': sum(1 for index in range(CELLS) if index not in skip and oracle.record(index)[0] == 0x19), 'tracks': oracle.track_state(tracks)}

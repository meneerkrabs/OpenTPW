"""TRACK-010 proposed migration contract, NOT an OpenTPW runtime/save codec.

Only synthetic four-cell footprints are modeled. Unknown legacy content must
never silently become empty or a fabricated path. Legacy bridge memory is kept
separately; it cannot supply per-cell content, queue metadata, or native parity.
"""
from dataclasses import dataclass, replace
import json


@dataclass(frozen=True)
class Cell:
    kind: str
    links: int = 0
    owner: int = 0
    queue_direction: int = 0
    placement_count: int = 0

    def __post_init__(self):
        if self.kind not in ("unknown", "empty", "path", "queue"):
            raise ValueError("unknown cell kind")
        fields = (self.links, self.owner, self.queue_direction, self.placement_count)
        if any(type(value) is not int or value < 0 for value in fields):
            raise ValueError("metadata must be nonnegative integers")
        if self.kind in ("unknown", "empty") and any(fields):
            raise ValueError("unknown/empty cells cannot carry invented metadata")


UNKNOWN = Cell("unknown")
EMPTY = Cell("empty")


@dataclass(frozen=True)
class Footprint:
    cells: tuple
    track: bool = False
    legacy_over_path: bool | None = None

    def __post_init__(self):
        if type(self.cells) is not tuple or len(self.cells) != 4:
            raise ValueError("exactly four ordered cells required")
        if not all(isinstance(cell, Cell) for cell in self.cells):
            raise ValueError("invalid cell")
        if type(self.track) is not bool:
            raise ValueError("track must be boolean")
        if self.legacy_over_path is not None and type(self.legacy_over_path) is not bool:
            raise ValueError("legacy memory must be boolean or absent")


def migrate_legacy(over_path):
    """Conservative policy: neither old bit value establishes a full underlay.

    True cannot distinguish 15 path subsets. False alone does not prove queue
    absence or provide all primary-cell metadata. Preserve the bit, not a guess.
    """
    if type(over_path) is not bool:
        raise ValueError("legacy bit must be boolean")
    return Footprint((UNKNOWN,) * 4, True, over_path)


def put_track(footprint):
    if footprint.track:
        raise ValueError("track already present")
    return replace(footprint, track=True)


def put_cell(footprint, index, cell):
    """An explicit accepted edit supplies authoritative content for ONE cell.

    Admission, ownership, no-modify rules and native writer ordering are outside
    this model. This function does not claim such an edit is legal in the game.
    """
    if type(index) is not int or not 0 <= index < 4:
        raise ValueError("cell index outside footprint")
    if not isinstance(cell, Cell) or cell.kind == "unknown":
        raise ValueError("explicit edit requires known content")
    cells = list(footprint.cells)
    cells[index] = cell
    return replace(footprint, cells=tuple(cells))


def remove_track(footprint):
    if not footprint.track:
        raise ValueError("no track to remove")
    if any(cell.kind == "unknown" for cell in footprint.cells):
        raise ValueError("unknown underlay requires an explicit migration policy")
    return replace(footprint, track=False)


def path_presence(footprint):
    """Three-valued presence, not bridge eligibility or a renderer predicate."""
    if any(cell.kind in ("path", "queue") for cell in footprint.cells):
        return True
    if any(cell.kind == "unknown" for cell in footprint.cells):
        return None
    return False


def encode(footprint):
    """Versioned *proposal* record; not a format implemented by TrackSave."""
    return json.dumps({"version": 1, "track": footprint.track,
                       "legacy_over_path": footprint.legacy_over_path,
                       "cells": [vars(cell) for cell in footprint.cells]},
                      sort_keys=True, separators=(",", ":"))


def decode(payload):
    record = json.loads(payload)
    if type(record) is not dict or set(record) != {
        "version", "track", "legacy_over_path", "cells"
    }:
        raise ValueError("invalid proposal record")
    if type(record["version"]) is not int or record["version"] != 1:
        raise ValueError("unsupported proposal version")
    if type(record["cells"]) is not list or len(record["cells"]) != 4:
        raise ValueError("exactly four cells required")
    keys = {"kind", "links", "owner", "queue_direction", "placement_count"}
    if any(type(cell) is not dict or set(cell) != keys for cell in record["cells"]):
        raise ValueError("invalid cell record")
    return Footprint(tuple(Cell(**cell) for cell in record["cells"]),
                     record["track"], record["legacy_over_path"])

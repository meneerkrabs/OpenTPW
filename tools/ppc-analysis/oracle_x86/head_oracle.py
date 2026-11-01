"""tp.exe's head placement routine 0x0044B510 run in the x86 oracle sandbox (docs/reverse/ORACLE.md; HEADS-004).

Dev-only tooling like the rest of oracle_x86: Unicorn and the identified tp.exe (OPENTPW_TPW_EXE, SHA-256 pinned) are needed, nothing here is shipped. The routine runs on synthetic
head records (one node, one world matrix, one camera matrix) the oracle lays out in the sandbox heap, at the control word 0x7F the shipped D3DIM device leaves the thread at (24-bit significand), with
the PC sine table built the way the first pass of the message pump builds it (0x4708D0(4096) at the CRT's 53-bit control word). The sprite register writers 0x475480, 0x475590 and 0x4755D0 are
recorded instead of run: the oracle returns what the head sets (the position, the frame register 0xD, the roll register 9 and the scale register 8). No original data and no game object enter.
"""
from __future__ import annotations

import struct
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))

from x86_harness import Image, Sandbox, executable_path, have_unicorn  # noqa: E402

CONTROL_WORD = 0x7F
TABLE_CONTROL_WORD = 0x27F
TABLE_BASE = 0x7B095C
TABLE_SIZE = 0x10 + 4 * 4096
CAMERA_POINTER = 0x790A98


def bits(value: float) -> int:
    return struct.unpack('<I', struct.pack('<f', value))[0]


def from_bits(value: int) -> float:
    return struct.unpack('<f', struct.pack('<I', value & 0xFFFFFFFF))[0]


class HeadOracle:
    def __init__(self, exe: Path | None = None, budget: int = 5_000_000):
        if not have_unicorn():
            raise RuntimeError('needs Unicorn')
        path = exe or executable_path('tpw')
        if path is None:
            raise RuntimeError('needs OPENTPW_TPW_EXE')
        self.image = Image.load(path)
        self.events: list[tuple] = []
        stubs = {
            0x475590: lambda box: (self._record('int', box), 0),
            0x4755D0: lambda box: (self._record('float', box), 0),
            0x475480: lambda box: (self._record_position(box), 0),
        }
        self.sandbox = Sandbox(self.image, [(0x401000, 0x6F0000), (0x67B410, 0x67B440)], stubs=stubs, budget=budget)
        sb = self.sandbox
        sb.reset()
        sb.set_fpcw(TABLE_CONTROL_WORD)
        sb.call(0x4708D0, (4096,))
        self.table_bytes = bytes(sb.uc.mem_read(TABLE_BASE, TABLE_SIZE))

    def _record(self, kind: str, box) -> int:
        self.events.append((kind, box.arg(0), box.arg(1), box.arg(2)))
        return 0

    def _record_position(self, box) -> int:
        self.events.append(('position', box.arg(0), box.arg(1), box.arg(2), box.arg(3)))
        return 0

    def head(self, node_matrix: list[int], camera_matrix: list[int], node_flags: int, handle: int = 0x1234):
        """Runs 0x44B510 over one head node: both matrices as 16 float bit patterns (row major, translation in the fourth row), the node's flag word (+0 of the node record).
        Returns the recorded writes as (frame or None for a hidden head, roll bits, scale bits, position bits)."""
        sb = self.sandbox
        sb.reset()
        sb.set_fpcw(CONTROL_WORD)
        sb.uc.mem_write(TABLE_BASE, self.table_bytes)
        sb._on_write(None, 0, TABLE_BASE, TABLE_SIZE, 0, None)
        self.events = []
        matrix = sb.alloc(0x40)
        camera = sb.alloc(0x40)
        for index, value in enumerate(node_matrix):
            sb.write(matrix + 4 * index, 'I', value)
        for index, value in enumerate(camera_matrix):
            sb.write(camera + 4 * index, 'I', value)
        sb.write(CAMERA_POINTER, 'I', camera)
        record = sb.alloc(0xA0)
        sb.write(record, 'I', node_flags)
        records = sb.alloc(0xA0)
        for k in range(0xA0 // 4):
            sb.write(records + 4 * k, 'I', sb.read(record + 4 * k, 'I'))
        entries = sb.alloc(0x14)
        sb.write(entries, 'B', 2)
        sb.write(entries + 4, 'I', matrix)
        sb.write(entries + 0xC, 'I', handle)
        flags = sb.alloc(8)
        sb.write(flags, 'B', 4)
        sb.write(flags + 4, 'I', entries)
        model = sb.alloc(0x80)
        sb.write(model + 0x44, 'H', 1)
        sb.write(model + 0x46, 'H', 0)
        sb.write(model + 0x48, 'H', 1)
        sb.write(model + 0x70, 'I', records)
        owner = sb.alloc(0x40)
        sb.write(owner + 4, 'I', model)
        sb.write(owner + 0x28, 'I', flags)
        root = sb.alloc(8)
        sb.write(root, 'I', owner)
        sb.call(0x44B510, (root,))
        frame = None
        roll = None
        scale = None
        position = None
        for event in self.events:
            if event[0] == 'int' and event[2] == 0xD:
                value = event[3]
                frame = value - (1 << 32) if value >= 1 << 31 else value
            elif event[0] == 'float' and event[2] == 9:
                roll = event[3]
            elif event[0] == 'float' and event[2] == 8:
                scale = event[3]
            elif event[0] == 'position':
                position = tuple(event[2:5])
        return frame, roll, scale, position

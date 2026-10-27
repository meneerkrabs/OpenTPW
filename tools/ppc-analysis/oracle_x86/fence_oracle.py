"""tp.exe's park fence routines run in the x86 oracle sandbox (docs/reverse/ORACLE.md, finding F30; PATH-007, ECON-026, ECON-050).

Dev-only tooling like the rest of oracle_x86: Unicorn and the identified tp.exe (OPENTPW_TPW_EXE, SHA-256 pinned) are needed, nothing here is shipped. The sandbox runs the executable's own routines on a synthetic
128 x 128 map, so no original data enters the checkout:

* the fence set-up 0x5323F0: the click handler's mode 0x19 branch (0x524960) around the real line walker 0x53AAC0 and step 0x5370E0, then the mark 0x531040, the fill 0x5311C0, the unmark, the ring walk
  0x52BA50 and the strip in front of the gate;
* the Buy Land validator 0x531A50 and the purchase 0x5327E0 (the same tool passes, then the walk, the mark, the fill and the unmark).

Only the presentation callees are stubbed (the cell refresh 0x5365D0, the piece models 0x53AFA0 and 0x53C8D0, the sound, the bank). The same routines reproduce the four restart.INTS saves of the original
cell for cell (the fence's structure records and every cell flag), which is how the layout below was checked (the saves stay outside the checkout).

Layout: G = the game object; the map cells (0x44 bytes) sit at G + 0x294 + id * 0x44 and the structure records (0x28 bytes) at G + 0x1102B0 + id * 0x28, id = y * 128 + x + 1.
"""
from __future__ import annotations

import hashlib
import struct
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))

from x86_harness import Image, Sandbox, executable_path  # noqa: E402

MAP = 128
CELLS = MAP * MAP
CODE = [(0x401000, 0x6FD000)]
GAME_SIZE = 0x1B02D8 + 0x10000

# the direction table the executable's static constructors fill (bit -> (dx, dy)); it lies in BSS
DIRECTIONS = {0x7CDBA0: (0, -1), 0x7CDBA8: (0, 1), 0x7CDBB0: (-1, 1), 0x7CDBB8: (1, 1), 0x7CDBC0: (-1, -1), 0x7CDBC8: (1, 0), 0x7CDBD0: (-1, 0), 0x7CDBD8: (1, -1)}

# the callees that only draw, play or pay: each is a no-op here (the oracle matches the original's saved start states with them)
STUBS = {0x5365D0: 0, 0x53AFA0: 0, 0x53C8D0: 0x20, 0x450D10: 0, 0x532960: 0, 0x5DA3C0: 0, 0x452AE0: 0, 0x536CA0: 4, 0x527940: 4, 0x4D01F0: 4}
BANK_BALANCE = 0x7FFFFFFF
COST_PER_CELL = 100


class FenceOracle:
    """One sandbox over tp.exe with a synthetic map; every method runs the executable's routines."""

    def __init__(self, exe: Path | None = None, budget: int = 40_000_000):
        path = exe or executable_path('tpw')
        if path is None:
            raise RuntimeError('OPENTPW_TPW_EXE is not set')
        self.image = Image.load(path)
        stubs = {address: self._noop(pop) for address, pop in STUBS.items()}
        stubs[0x5195D0] = lambda sb: (self.bank, 0)
        self.sandbox = Sandbox(self.image, CODE, stubs, budget)
        self.game = 0
        self.bank = 0
        self.cells = bytearray()
        self.records = bytearray()

    @staticmethod
    def _noop(pop: int):
        return lambda sb: (0, pop)

    # ------------------------------------------------------------------ state

    def start(self, terrain, no_modify) -> None:
        """A fresh map: cell types from the terrain rectangles (type, x, y, w, h; later ones win), flag 0x20 on the NoModify cells, the world init's 0x40 on every other cell."""
        sb = self.sandbox
        sb.reset()
        self.game = sb.alloc(GAME_SIZE)
        self.bank = sb.alloc(64)
        sb.write(self.bank + 0xC, 'i', BANK_BALANCE)
        self.cells = bytearray(CELLS * 0x44)
        self.records = bytearray(CELLS * 0x28)
        for k in range(CELLS):
            struct.pack_into('<H', self.cells, k * 0x44, k + 1)
            struct.pack_into('<H', self.records, k * 0x28, k + 1)
            self.records[k * 0x28 + 2] = 2
        types = [0] * CELLS
        for kind, x0, y0, w, h in terrain:
            for y in range(y0, min(MAP, y0 + h)):
                for x in range(x0, min(MAP, x0 + w)):
                    types[y * MAP + x] = kind
        flags = [0] * CELLS
        for x, y in no_modify:
            types[y * MAP + x] = 1
            flags[y * MAP + x] |= 0x20
        for k in range(CELLS):
            flag = flags[k] if flags[k] & 0x20 else flags[k] | 0x40
            struct.pack_into('<I', self.cells, k * 0x44 + 8, types[k])
            struct.pack_into('<H', self.cells, k * 0x44 + 0xE, flag)
            struct.pack_into('<I', self.records, k * 0x28 + 8, 7 if types[k] in (2, 7, 0x1E) else 0)
        self.flush()

    def flush(self) -> None:
        sb = self.sandbox
        for base, data in ((self.game + 0x2D8, self.cells), (self.game + 0x1102D8, self.records)):
            sb._on_write(None, 0, base, len(data), 0, None)
            sb.uc.mem_write(base, bytes(data))
        for address, (dx, dy) in DIRECTIONS.items():
            sb.write(address, 'i', dx)
            sb.write(address + 4, 'i', dy)
        for address, value in ((0x7CF83C, self.game), (0x80239C, self.game), (0x8023A0, self.game + 0x2D8), (0x81B1B0, MAP), (0x81D740, MAP), (0x78560C, COST_PER_CELL)):
            sb.write(address, 'I', value)

    def pull(self) -> None:
        sb = self.sandbox
        self.cells = bytearray(sb.uc.mem_read(self.game + 0x2D8, CELLS * 0x44))
        self.records = bytearray(sb.uc.mem_read(self.game + 0x1102D8, CELLS * 0x28))

    def cell_flags(self, index: int) -> int:
        return struct.unpack_from('<H', self.cells, index * 0x44 + 0xE)[0]

    def cell_type(self, index: int) -> int:
        return struct.unpack_from('<I', self.cells, index * 0x44 + 8)[0]

    def record(self, index: int) -> tuple[int, int, int, int]:
        """(type, links +0x22, owner +0x10, direction +0xD) of the structure record; the no-terrain type 7 reads as 0 like OpenTPW's fence layer."""
        base = index * 0x28
        kind = struct.unpack_from('<I', self.records, base + 8)[0]
        return (0 if kind == 7 else kind, self.records[base + 0x22], struct.unpack_from('<H', self.records, base + 0x10)[0], self.records[base + 0xD])

    # ------------------------------------------------------------------ routines

    def _gates(self, post_a, post_b, mode: int) -> None:
        sb = self.sandbox
        for address, value in ((0x7639F0, post_a[0]), (0x7639F4, post_a[1]), (0x7639F8, post_b[0]), (0x7639FC, post_b[1])):
            sb.write(address, 'I', value)
        sb.write(0x81B4CC, 'B', mode)

    def lay_fence(self, post_a, post_b, clicks) -> None:
        """0x5323F0 from the clicks: lay, mark, fill, unmark, walk and the gate strip."""
        sb = self.sandbox
        sb.executed = 0
        self.flush()
        self._gates(post_a, post_b, 1)
        sb.write(0x818C50, 'B', 4)
        sb.write(0x81EDE4, 'i', -1)
        sb.write(0x81EDE8, 'i', -1)
        sb.write(0x79D86C, 'I', 0)
        tool = _Tool(sb, post_a)
        for dx, dy in clicks:
            tool.click(post_a[0] + dx, post_a[1] + dy)
        sb.call(0x531040, args=(1,))
        sb.call(0x5311C0)
        sb.call(0x531040, args=(0,))
        sb.call(0x52BA50, args=(post_a[0], post_a[1], post_b[0], post_b[1], 4, 1))
        self.pull()
        for x in range(post_b[0] + 2, post_a[0]):
            for y in (post_a[1], post_a[1] + 1):
                index = y * MAP + x
                struct.pack_into('<H', self.cells, index * 0x44 + 0xE, self.cell_flags(index) & ~0x40)
        self.flush()
        sb.write(0x81B4CC, 'B', 0)

    def validate(self, post_a, post_b, first, second):
        """The Buy Land validator 0x531A50: (accepted, cells to pay for)."""
        sb = self.sandbox
        sb.executed = 0
        self._gates(post_a, post_b, 0)
        accepted = sb.call(0x531A50, args=(first[0], first[1], second[0], second[1], 1)).eax & 0xFF
        return bool(accepted), sb.read(0x81AF4C, 'i') // COST_PER_CELL

    def purchase(self, post_a, post_b, first, second) -> None:
        """0x5327E0: the five clicks, the walk, the mark, the fill and the unmark."""
        sb = self.sandbox
        sb.executed = 0
        self._gates(post_a, post_b, 1)
        xmin, xmax = min(first[0], second[0]), max(first[0], second[0])
        ymin, ymax = min(first[1], second[1]), max(first[1], second[1])
        sb.write(0x81EDE4, 'i', -1)
        sb.write(0x81EDE8, 'i', -1)
        tool = _Tool(sb, post_a)
        for x, y in ((xmin, ymin), (xmax, ymin), (xmax, ymax), (xmin, ymax), (xmin, ymin)):
            tool.click(x, y)
        sb.call(0x52BA50, args=(post_a[0], post_a[1], post_b[0], post_b[1], 4, 1))
        sb.call(0x531040, args=(1,))
        sb.call(0x5311C0)
        sb.call(0x531040, args=(0,))
        sb.write(0x81B4CC, 'B', 0)

    # ------------------------------------------------------------------ comparable output

    def flag_runs(self) -> list[int]:
        """The cell flag words in run-length form: start, length, value for every run."""
        runs: list[int] = []
        previous = None
        for index in range(CELLS):
            flag = self.cell_flags(index)
            if flag == previous:
                runs[-2] += 1
            else:
                runs += [index, 1, flag]
                previous = flag
        return runs

    def layer_sha256(self) -> str:
        digest = hashlib.sha256()
        for index in range(CELLS):
            kind, links, owner, direction = self.record(index)
            digest.update(bytes((kind, links, direction)) + struct.pack('<H', owner))
        return digest.hexdigest()

    def owned_cells(self) -> int:
        return sum(1 for index in range(CELLS) if not self.cell_flags(index) & 0x40)

    def post_count(self) -> int:
        return sum(1 for index in range(CELLS) if self.record(index)[0] == 0x19)


class _Tool:
    """The click handler's mode 0x19 branch (0x524960) around the executable's line walker 0x53AAC0."""

    PASSES = (0x19, 0x80, 0x82, 0x85, 0x86, 0x81)

    def __init__(self, sandbox: Sandbox, post_a):
        self.sandbox = sandbox
        self.post_a = post_a
        self.pointers = sandbox.alloc(8)
        self.x0 = self.y0 = -1
        self.active = True

    def click(self, x: int, y: int) -> None:
        if not self.active:
            return
        sb = self.sandbox
        if self.x0 == -1 and self.y0 == -1:
            self.x0, self.y0 = x, y
            return
        dx, dy = abs(x - self.x0), abs(y - self.y0)
        if dy <= dx:
            y = self.y0
        if dx <= abs(y - self.y0):
            x = self.x0
        if self.x0 != x or self.y0 != y or self.post_a == (x, y):
            sb.write(self.pointers, 'i', x)
            sb.write(self.pointers + 4, 'i', y)
            for op in self.PASSES:
                sb.call(0x53AAC0, args=(op, self.x0, self.y0, self.pointers, self.pointers + 4))
            x, y = sb.read(self.pointers, 'i'), sb.read(self.pointers + 4, 'i')
        if self.x0 == x and self.y0 == y:
            self.x0 = self.y0 = -1
            self.active = False
            return
        self.x0, self.y0 = x, y

"""Sandbox and call harness around ppc32.Machine for isolated routines of the identified Mac PEF.

`Image.load(bin_root)` reads SimThemePark.data from the private Feral `bin` directory (never from the
checkout), checks its SHA-256 and maps:

* every instantiated PEF section at its own base (code at CODE_BASE, so `code offset + CODE_BASE` is
  the run-time address; data at DATA_BASE), with the loader's relocations applied: section
  pointers become `base + offset`, imported symbols point at per-import trap transition vectors;
* the import traps: an import's code pointer lands in a region that is never executable, so calling
  any import without a stub raises UnknownCall;
* a scratch stack and a bump-allocated heap for argument structs.

Only code ranges the oracle target names (`allowed`) may execute; calls elsewhere abort the run.
Every call starts from pristine memory (`Memory.reset`), registers zeroed, r1 at the stack top,
r2 = the application TOC and LR = the return sentinel.
"""
from __future__ import annotations

import hashlib
import os
import random
import struct
import sys
from dataclasses import dataclass
from pathlib import Path

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE.parent))
sys.path.insert(0, str(HERE))

import pef  # noqa: E402
from ppc32 import MASK32, Machine, Memory, OracleError, double_to_bits, bits_to_double  # noqa: E402

APP_NAME = 'SimThemePark.data'
APP_SHA256 = '04809cd4ccee5433c7fb0b7c93d32f6a7aa629c1849181c0b7906415e5e295f5'
CODE_BASE = 0x10000000
DATA_BASE = 0x20000000
SECTION_STRIDE = 0x04000000
IMPORT_VECTORS = 0x38000000  # 8-byte transition vector per import: (trap code address, 0)
IMPORT_TRAPS = 0x39000000    # never mapped: reaching one is an unstubbed import call
HEAP_BASE = 0x40000000
HEAP_SIZE = 0x00400000
STACK_BASE = 0x60000000
STACK_SIZE = 0x00040000


def bin_root() -> Path | None:
    """The private Feral bin directory from the environment, or None (tests skip)."""
    for value in (os.environ.get('OPENTPW_MAC_BIN'), os.environ.get('OPENTPW_PPC_BIN_ROOT')):
        if value and (Path(value) / APP_NAME).is_file():
            return Path(value)
    return None


class IdentityError(OracleError):
    pass


class Image:
    """The identified application container, mapped once; runs reset memory between calls."""

    def __init__(self, raw: bytes, name: str = APP_NAME):
        if hashlib.sha256(raw).hexdigest() != APP_SHA256:
            raise IdentityError(f'{name} is not the identified Feral build')
        self.container = pef.PEFContainer(raw, name)
        self.memory = Memory()
        self.bases: dict[int, int] = {}
        loadable = [s for s in self.container.sections if s.kind in (0, 1, 2, 3, 6)]
        others = [s for s in loadable if s is not self.container.code]
        self.bases[self.container.code.index] = CODE_BASE
        for n, section in enumerate(others):
            self.bases[section.index] = DATA_BASE + n * SECTION_STRIDE
        for section in loadable:
            data = bytearray(section.data)
            for offset, target in self.container.relocs.get(section.index, {}).items():
                if target.kind == 'section':
                    value = self.bases[target.target] + target.addend
                else:
                    value = IMPORT_VECTORS + 8 * target.target + target.addend
                struct.pack_into('>I', data, offset, value & MASK32)
            self.memory.map(f'{section.kind_name}{section.index}', self.bases[section.index], data,
                            writable=section.kind != 0)
        vectors = bytearray(8 * max(1, len(self.container.imports)))
        for i in range(len(self.container.imports)):
            struct.pack_into('>II', vectors, 8 * i, IMPORT_TRAPS + 4 * i, 0)
        self.memory.map('import-vectors', IMPORT_VECTORS, vectors, writable=False)
        self.memory.map('heap', HEAP_BASE, HEAP_SIZE)
        self.memory.map('stack', STACK_BASE, STACK_SIZE)
        self.code_base = self.bases[self.container.code.index]
        self.data_base = self.bases[self.container.data_section.index]
        self.toc = self._toc()

    @classmethod
    def load(cls, root: Path) -> 'Image':
        return cls((root / APP_NAME).read_bytes())

    def _toc(self) -> int:
        """TOC from the main transition vector (the loader section's main symbol)."""
        if self.container.main is None:
            raise IdentityError('no main transition vector')
        section, offset = self.container.main
        return self.memory.u32(self.bases[section] + offset + 4)

    def code(self, offset: int) -> int:
        return self.code_base + offset

    def import_index(self, name: str) -> int:
        matches = [i for i, s in enumerate(self.container.imports) if s.name == name]
        if len(matches) != 1:
            raise IdentityError(f'import {name}: {len(matches)} matches')
        return matches[0]

    def import_trap(self, name: str) -> int:
        return IMPORT_TRAPS + 4 * self.import_index(name)


@dataclass
class Result:
    gpr: list[int]
    fpr: list[int]
    cr: int
    executed: int

    @property
    def r3(self) -> int:
        return self.gpr[3]

    @property
    def f1(self) -> float:
        return bits_to_double(self.fpr[1])


class Sandbox:
    """One configured routine: allowed code ranges (offsets), stubs (offsets or import names)."""

    def __init__(self, image: Image, allowed: list[tuple[int, int]], stubs: dict | None = None,
                 budget: int = 100_000):
        self.image = image
        resolved = {}
        for key, stub in (stubs or {}).items():
            resolved[image.import_trap(key) if isinstance(key, str) else image.code(key)] = stub
        self.machine = Machine(image.memory, [(image.code(a), image.code(b)) for a, b in allowed], resolved, budget)
        self._heap = HEAP_BASE

    # ---------------------------------------------------------------- argument memory
    def alloc(self, size: int, align: int = 8) -> int:
        address = (self._heap + align - 1) & ~(align - 1)
        if address + size > HEAP_BASE + HEAP_SIZE:
            raise OracleError('sandbox heap exhausted')
        self._heap = address + size
        return address

    def struct(self, size: int, fields: dict[int, tuple[str, object]] | None = None) -> int:
        """Allocate a zeroed struct and write fields {offset: (struct format, value)}."""
        address = self.alloc(size)
        for offset, (fmt, value) in (fields or {}).items():
            self.image.memory.write(address + offset, struct.pack('>' + fmt, value))
        return address

    def read(self, address: int, fmt: str):
        return struct.unpack('>' + fmt, self.image.memory.read(address, struct.calcsize('>' + fmt)))[0]

    def write(self, address: int, fmt: str, value) -> None:
        self.image.memory.write(address, struct.pack('>' + fmt, value))

    def data(self, offset: int) -> int:
        """Run-time address of a data-section offset."""
        return self.image.data_base + offset

    # ---------------------------------------------------------------- calls
    def reset(self) -> None:
        self.image.memory.reset()
        self._heap = HEAP_BASE
        m = self.machine
        m.gpr = [0] * 32
        m.fpr = [0] * 32
        m.cr = m.xer = m.ctr = m.fpscr = 0
        m.executed = 0

    def call(self, entry: int, gpr: dict[int, int] | None = None, fpr: dict[int, float] | None = None,
             stop: int | tuple[int, ...] | None = None) -> Result:
        """Call code offset `entry` with the given argument registers; memory must be prepared after reset().
        With `stop` (a code offset, or several) the run ends when execution reaches one: a fragment inside a larger routine."""
        m = self.machine
        m.gpr[1] = STACK_BASE + STACK_SIZE - 0x100
        m.gpr[2] = self.image.toc
        for r, value in (gpr or {}).items():
            m.gpr[r] = value & MASK32
        for r, value in (fpr or {}).items():
            m.fpr[r] = double_to_bits(value)
        stops = () if stop is None else (stop,) if isinstance(stop, int) else tuple(stop)
        m.call(self.image.code(entry), stop=frozenset(self.image.code(s) for s in stops))
        return Result(list(m.gpr), list(m.fpr), m.cr, m.executed)


# ------------------------------------------------------------------------------- inputs
def seeded(seed: str) -> random.Random:
    """Deterministic generator per target (string seeds hash stably in random.Random)."""
    return random.Random(seed)


def f32(value: float) -> float:
    """Round a Python float to the nearest single (what a C# float holds)."""
    return struct.unpack('>f', struct.pack('>f', value))[0]

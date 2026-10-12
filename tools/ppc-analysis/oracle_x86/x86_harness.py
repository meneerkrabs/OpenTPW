"""Sandbox and call harness around Unicorn for isolated routines of the identified Windows executables.

Dev-only oracle tooling (docs/reverse/ORACLE.md, x86 section): Unicorn is imported lazily, only here, and
nothing in the game or in the shipped tools may import this module. Without Unicorn (or without the private
executable) the x86 tests skip.

`Image.load(path)` reads one of the two identified no-CD executables (SHA-256 pinned, anything else is
refused), lays its PE32 sections out at the preferred base (relocated if another base is asked for) and
points every import address table slot at a per-import trap address. `Sandbox` maps the image plus a heap,
a stack, an import trap page and a return sentinel, and runs one routine per `call`:

* only code ranges the target names (`allowed`, virtual addresses) may execute, and a call into any other
  address, or into an import trap without a stub, aborts the run (UnknownCall), like ppc32's;
* an instruction budget bounds every run (BudgetExceeded);
* every run starts from pristine memory (pages written by the previous run are restored), zeroed
  registers, a clean x87 and SSE state, esp at the stack top and the return sentinel on the stack;
* cdecl, stdcall and thiscall: stack arguments are pushed right to left, `this` goes in ecx.
"""
from __future__ import annotations

import hashlib
import os
import random
import struct
import sys
from dataclasses import dataclass, field
from pathlib import Path
from typing import Callable

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))

import x86_pe  # noqa: E402

try:  # dev-only dependency: every consumer must handle its absence
    import unicorn
    from unicorn import x86_const as x86
except ImportError:  # pragma: no cover - exercised by the CI environment, which has no Unicorn
    unicorn = None
    x86 = None

UNICORN_VERSION = '2.1.4'
UNICORN_HINT = f'requires unicorn (dev-only: pip install unicorn=={UNICORN_VERSION}; tools/ppc-analysis/oracle_x86/README.md)'

# Identified executables. Only these may be analysed (docs/COMPLETION-PLAN.md, "Provenance of game rules").
IDENTIFIED = {
    'tpw': {
        'name': 'tp.exe',
        'title': 'Sim Theme Park World v2.0 no-CD tp.exe',
        'size': 3_734_528,
        'sha256': '5472f97b5eb28f58b5a49f3a15f6f66adffa6b2625a623f503202c9696d1b805',
        'variable': 'OPENTPW_TPW_EXE',
    },
    'tpi': {
        'name': 'Game.exe',
        'title': 'Theme Park Inc no-CD Game.exe',
        'size': 6_742_016,
        'sha256': '77ea0c41410aad7c94a5eda658e31cb4ac05f7253aaf6746ad5e8a90afbd967a',
        'variable': 'OPENTPW_TPI_EXE',
    },
}

HEAP_BASE = 0x20000000
HEAP_SIZE = 0x01000000   # the world object puts its RNG state ~2 MB in
STACK_BASE = 0x60000000
STACK_SIZE = 0x00040000
TRAP_BASE = 0x70000000   # one 16-byte slot per import; mapped executable, never allowed to run
TRAP_SLOT = 16
SENTINEL = 0x78000000    # return address of every call: a page holding `hlt`
PAGE = 0x1000
MASK32 = 0xFFFFFFFF


class OracleError(Exception):
    pass


class IdentityError(OracleError):
    pass


class UnknownCall(OracleError):
    """Execution reached code outside the allowed ranges, or an import without a stub."""


class BudgetExceeded(OracleError):
    pass


class MemoryFault(OracleError):
    pass


class Unsupported(OracleError):
    pass


def have_unicorn() -> bool:
    return unicorn is not None


def executable_path(key: str) -> Path | None:
    """The private executable from its environment variable, or None (tests skip)."""
    value = {'tpw': os.environ.get('OPENTPW_TPW_EXE'), 'tpi': os.environ.get('OPENTPW_TPI_EXE')}[key]
    if value and Path(value).is_file():
        return Path(value)
    return None


def identify(raw: bytes) -> str:
    digest = hashlib.sha256(raw).hexdigest()
    for key, known in IDENTIFIED.items():
        if digest == known['sha256'] and len(raw) == known['size']:
            return key
    raise IdentityError('not an identified executable (only the decrypted TPW v2.0 tp.exe and the TPI no-CD Game.exe '
                        'may be analysed)')


class Image:
    """One identified executable (or, in tests, a synthetic one with its own pinned hash), laid out flat."""

    def __init__(self, raw: bytes, base: int | None = None, identity: str | None = None):
        self.identity = identity if identity is not None else identify(raw)
        self.pe = x86_pe.parse(raw)
        self.base = self.pe.preferred_base if base is None else base
        self.data = self.pe.layout()
        delta = (self.base - self.pe.preferred_base) & MASK32
        if delta:
            for rva in self.pe.relocations():
                value = struct.unpack_from('<I', self.data, rva)[0]
                struct.pack_into('<I', self.data, rva, (value + delta) & MASK32)
        self.import_traps: dict[str, int] = {}
        for index, entry in enumerate(self.pe.imports):
            address = TRAP_BASE + TRAP_SLOT * index
            struct.pack_into('<I', self.data, entry.slot_rva, address)
            self.import_traps.setdefault(entry.key, address)
        self.trap_end = TRAP_BASE + TRAP_SLOT * max(1, len(self.pe.imports))

    @classmethod
    def load(cls, path: Path, base: int | None = None) -> 'Image':
        return cls(Path(path).read_bytes(), base)

    def import_trap(self, key: str) -> int:
        """Trap address of `DLL!name`: the DLL name compares case-insensitively, the symbol name exactly."""
        dll, _, name = key.partition('!')
        try:
            return self.import_traps[f'{dll.lower()}!{name}']
        except KeyError:
            raise IdentityError(f'no import {key}') from None


@dataclass
class Result:
    regs: dict
    executed: int
    esp_delta: int            # final esp minus the esp just above the pushed arguments (0 when the callee cleaned up)
    fpu: list = field(default_factory=list)   # raw (mantissa, exponent) of ST0..ST7 as Unicorn reports them

    @property
    def eax(self) -> int:
        return self.regs['eax']


REGISTERS = ('eax', 'ecx', 'edx', 'ebx', 'esp', 'ebp', 'esi', 'edi')


def _uc_reg(name: str) -> int:
    return getattr(x86, f'UC_X86_REG_{name.upper()}')


class Sandbox:
    """One configured routine: allowed code ranges (virtual addresses), stubs (addresses or `dll!name`)."""

    def __init__(self, image: Image, allowed: list[tuple[int, int]], stubs: dict | None = None,
                 budget: int = 100_000):
        if unicorn is None:
            raise OracleError(UNICORN_HINT)
        self.image = image
        self.allowed = list(allowed)
        self.budget = budget
        self.stubs: dict[int, Callable] = {}
        for key, stub in (stubs or {}).items():
            self.stubs[image.import_trap(key) if isinstance(key, str) else key] = stub
        self.uc = unicorn.Uc(unicorn.UC_ARCH_X86, unicorn.UC_MODE_32)
        self._pristine: list[tuple[int, int, bytes | None]] = []   # (start, end, original bytes; None = zero)
        self._dirty: set[int] = set()
        self._map_image()
        self._map('heap', HEAP_BASE, HEAP_SIZE, None, unicorn.UC_PROT_ALL & ~unicorn.UC_PROT_EXEC)
        self._map('stack', STACK_BASE, STACK_SIZE, None, unicorn.UC_PROT_READ | unicorn.UC_PROT_WRITE)
        self.uc.mem_map(TRAP_BASE, x86_pe.page_up(image.trap_end - TRAP_BASE), unicorn.UC_PROT_READ | unicorn.UC_PROT_EXEC)
        self.uc.mem_write(TRAP_BASE, b'\xCC' * (image.trap_end - TRAP_BASE))
        self.uc.mem_map(SENTINEL, PAGE, unicorn.UC_PROT_READ | unicorn.UC_PROT_EXEC)
        self.uc.mem_write(SENTINEL, b'\xF4')
        self.uc.hook_add(unicorn.UC_HOOK_MEM_WRITE, self._on_write)
        self.uc.hook_add(unicorn.UC_HOOK_CODE, self._on_code)
        self.executed = 0
        self._error: Exception | None = None
        self._heap = HEAP_BASE
        self._running = False

    # ---------------------------------------------------------------- memory
    def _map(self, name: str, start: int, size: int, content: bytes | None, perms: int) -> None:
        self.uc.mem_map(start, size, perms)
        if content is not None:
            self.uc.mem_write(start, bytes(content))
        self._pristine.append((start, start + size, content))

    def _map_image(self) -> None:
        image, uc = self.image, unicorn
        headers = x86_pe.page_up(image.pe.sections[0].rva)
        self._map('headers', image.base, headers, image.data[:headers], uc.UC_PROT_READ)
        for section in image.pe.sections:
            perms = uc.UC_PROT_READ
            if section.writable:
                perms |= uc.UC_PROT_WRITE
            if section.executable:
                perms |= uc.UC_PROT_EXEC
            data = image.data[section.rva:section.rva + section.span]
            self._map(section.name, image.base + section.rva, section.span, bytes(data), perms)

    def _on_write(self, uc, access, address, size, value, user) -> None:
        for page in range(address >> 12, ((address + size - 1) >> 12) + 1):
            self._dirty.add(page)

    def _restore(self) -> None:
        for page in self._dirty:
            start = page << 12
            for lo, hi, content in self._pristine:
                if lo <= start < hi:
                    self.uc.mem_write(start, bytes(PAGE) if content is None else content[start - lo:start - lo + PAGE])
                    break
        self._dirty.clear()

    def alloc(self, size: int, align: int = 8) -> int:
        address = (self._heap + align - 1) & ~(align - 1)
        if address + size > HEAP_BASE + HEAP_SIZE:
            raise OracleError('sandbox heap exhausted')
        self._heap = address + size
        return address

    def object(self, size: int, fields: dict[int, tuple[str, object]] | None = None) -> int:
        """Allocate a zeroed object (the heap is pristine zero after reset) and write {offset: (format, value)}."""
        address = self.alloc(size)
        for offset, (fmt, value) in (fields or {}).items():
            self.write(address + offset, fmt, value)
        return address

    def read(self, address: int, fmt: str):
        return struct.unpack('<' + fmt, bytes(self.uc.mem_read(address, struct.calcsize('<' + fmt))))[0]

    def write(self, address: int, fmt: str, value) -> None:
        data = struct.pack('<' + fmt, value)
        self._on_write(None, 0, address, len(data), 0, None)
        self.uc.mem_write(address, data)

    # ---------------------------------------------------------------- execution
    def _on_code(self, uc, address, size, user) -> None:
        if self._error is not None:
            return
        try:
            if TRAP_BASE <= address < self.image.trap_end:
                stub = self.stubs.get(address)
                if stub is None:
                    index = (address - TRAP_BASE) // TRAP_SLOT
                    entry = self.image.pe.imports[index]
                    raise UnknownCall(f'unstubbed import {entry.key}')
                self._run_stub(stub)
                return
            stub = self.stubs.get(address)
            if stub is not None:
                self._run_stub(stub)
                return
            if not any(lo <= address < hi for lo, hi in self.allowed):
                raise UnknownCall(f'call into {address:#x}, outside the allowed ranges')
            self.executed += 1
            if self.executed > self.budget:
                raise BudgetExceeded(f'more than {self.budget} instructions')
        except OracleError as error:
            self._error = error
            uc.emu_stop()

    def _run_stub(self, stub: Callable) -> None:
        """A stub receives the sandbox and returns (eax, bytes the callee pops); it acts as the routine's `ret`."""
        eax, pop = stub(self)
        uc = self.uc
        esp = uc.reg_read(x86.UC_X86_REG_ESP)
        return_to = self.read(esp, 'I')
        uc.reg_write(x86.UC_X86_REG_EAX, eax & MASK32)
        uc.reg_write(x86.UC_X86_REG_ESP, (esp + 4 + pop) & MASK32)
        uc.reg_write(x86.UC_X86_REG_EIP, return_to)

    def arg(self, index: int) -> int:
        """Stack argument `index` (0-based) while a stub runs."""
        return self.read((self.uc.reg_read(x86.UC_X86_REG_ESP) + 4 + 4 * index) & MASK32, 'I')

    def reset(self) -> None:
        self._restore()
        self._heap = HEAP_BASE
        uc = self.uc
        for name in REGISTERS + ('ebp',):
            uc.reg_write(_uc_reg(name), 0)
        uc.reg_write(x86.UC_X86_REG_EFLAGS, 0x202)
        uc.reg_write(x86.UC_X86_REG_FPCW, 0x37F)   # x87: extended precision, round to nearest, all exceptions masked
        uc.reg_write(x86.UC_X86_REG_FPSW, 0)
        uc.reg_write(x86.UC_X86_REG_FPTAG, 0xFFFF)  # every register empty
        for i in range(8):
            uc.reg_write(x86.UC_X86_REG_FP0 + i, (0, 0))
        uc.reg_write(x86.UC_X86_REG_FPTAG, 0xFFFF)
        uc.reg_write(x86.UC_X86_REG_MXCSR, 0x1F80)
        self.executed = 0
        self._error = None

    def call(self, entry: int, args: tuple = (), this: int | None = None, regs: dict[str, int] | None = None,
             stop: int | None = None) -> Result:
        """Call virtual address `entry`. `args` are 32-bit stack arguments (first argument first), `this` is ecx
        (thiscall), `regs` any other initial registers. With `stop` (an address) the run ends when execution reaches
        it, for a fragment inside a larger routine. Memory must be prepared after reset()."""
        uc = self.uc
        esp = STACK_BASE + STACK_SIZE - 0x100
        for value in reversed(args):
            esp -= 4
            self.write(esp, 'I', value & MASK32)
        arguments_top = esp + 4 * len(args)
        esp -= 4
        self.write(esp, 'I', SENTINEL)
        uc.reg_write(x86.UC_X86_REG_ESP, esp)
        if this is not None:
            uc.reg_write(x86.UC_X86_REG_ECX, this & MASK32)
        for name, value in (regs or {}).items():
            uc.reg_write(_uc_reg(name), value & MASK32)
        self._error = None
        try:
            uc.emu_start(entry, SENTINEL if stop is None else stop)
        except unicorn.UcError as error:
            if self._error is None:
                code = error.errno
                if code == unicorn.UC_ERR_INSN_INVALID:
                    raise Unsupported(f'invalid instruction at {uc.reg_read(x86.UC_X86_REG_EIP):#x}') from None
                raise MemoryFault(f'{error} at eip {uc.reg_read(x86.UC_X86_REG_EIP):#x}') from None
        if self._error is not None:
            raise self._error
        if stop is None and uc.reg_read(x86.UC_X86_REG_EIP) != SENTINEL:
            raise OracleError(f'run ended at {uc.reg_read(x86.UC_X86_REG_EIP):#x}, not at the return sentinel')
        result = {name: uc.reg_read(_uc_reg(name)) for name in REGISTERS}
        result['eflags'] = uc.reg_read(x86.UC_X86_REG_EFLAGS)
        fpu = [uc.reg_read(x86.UC_X86_REG_FP0 + i) for i in range(8)]
        return Result(result, self.executed, result['esp'] - arguments_top, fpu)

    def x87_state(self) -> dict:
        uc = self.uc
        return {'fpcw': uc.reg_read(x86.UC_X86_REG_FPCW), 'fpsw': uc.reg_read(x86.UC_X86_REG_FPSW),
                'fptag': uc.reg_read(x86.UC_X86_REG_FPTAG)}


def seeded(seed: str) -> random.Random:
    """Deterministic generator per target (string seeds hash stably in random.Random)."""
    return random.Random(seed)

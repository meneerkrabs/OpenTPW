"""Preferred Executable Format (PEF) loader for classic Mac OS PowerPC binaries.

Reads a PEF container (the data fork of a CFM application or shared library,
starting with ``Joy!peffpwpc``) and produces:

* the section list with instantiated contents (pattern-initialised data is
  expanded),
* the loader section: imported libraries and symbols, exported symbols,
  main/init/term entry points,
* the relocation instructions, executed against the data section so that every
  relocated word is known as (target kind, target section / import, addend).

Only the file format is interpreted; nothing is executed. Written from the
public "Mac OS Runtime Architectures" PEF specification.

Usage as a script: ``python3 -I pef.py <file.data>`` prints a summary.
"""

from __future__ import annotations

import struct
import sys
from dataclasses import dataclass, field

SECTION_KINDS = {
    0: "code",
    1: "data",
    2: "pidata",
    3: "const",
    4: "loader",
    5: "debug",
    6: "execdata",
    7: "exception",
    8: "traceback",
}

SYMBOL_CLASSES = {0: "code", 1: "data", 2: "tvector", 3: "toc", 4: "glue"}


class PEFError(Exception):
    pass


MAX_SECTION_SIZE = 64 * 1024 * 1024
MAX_RELOCATION_STEPS = 1_000_000


def _span(b: bytes, offset: int, size: int) -> None:
    if offset < 0 or size < 0 or offset + size > len(b):
        raise PEFError(f"out-of-range span at {offset:#x}, size {size:#x}")


@dataclass
class Section:
    index: int
    name: str
    default_address: int
    total_size: int
    unpacked_size: int
    packed_size: int
    container_offset: int
    kind: int
    share_kind: int
    alignment: int
    data: bytearray = field(default_factory=bytearray)

    @property
    def kind_name(self) -> str:
        return SECTION_KINDS.get(self.kind, f"kind{self.kind}")


@dataclass
class ImportedLibrary:
    name: str
    old_imp_version: int
    current_version: int
    symbol_count: int
    first_symbol: int
    options: int


@dataclass
class ImportedSymbol:
    index: int
    name: str
    sym_class: int
    weak: bool
    library: str = ""


@dataclass
class ExportedSymbol:
    name: str
    sym_class: int
    value: int
    section: int


@dataclass
class RelocTarget:
    """What a relocated 32-bit word points to.

    kind: 'section' (target = section index, addend = stored word = offset in
    that section) or 'import' (target = imported symbol index, addend = stored
    word).
    """

    kind: str
    target: int
    addend: int


def _u32(b: bytes, o: int) -> int:
    _span(b, o, 4)
    return struct.unpack_from(">I", b, o)[0]


def _i32(b: bytes, o: int) -> int:
    _span(b, o, 4)
    return struct.unpack_from(">i", b, o)[0]


def _u16(b: bytes, o: int) -> int:
    _span(b, o, 2)
    return struct.unpack_from(">H", b, o)[0]


def _cstr(b: bytes, o: int) -> str:
    _span(b, o, 1)
    end = b.find(b"\0", o)
    if end < 0:
        raise PEFError("unterminated string")
    return b[o:end].decode("mac_roman")


def unpack_pidata(packed: bytes, unpacked_size: int) -> bytearray:
    """Expand a pattern-initialised data section."""
    if not 0 <= unpacked_size <= MAX_SECTION_SIZE:
        raise PEFError("pidata exceeds section size limit")
    out = bytearray()
    pos = 0

    def read(size: int) -> bytes:
        nonlocal pos
        _span(packed, pos, size)
        block = packed[pos:pos + size]
        pos += size
        return block

    def room(size: int) -> None:
        if len(out) + size > unpacked_size:
            raise PEFError("pidata output exceeds declared size")

    def arg() -> int:
        nonlocal pos
        value = 0
        while True:
            byte = read(1)[0]
            value = (value << 7) | (byte & 0x7F)
            if value > MAX_SECTION_SIZE:
                raise PEFError("pidata argument exceeds size limit")
            if not byte & 0x80:
                return value

    while pos < len(packed):
        byte = packed[pos]
        pos += 1
        opcode = byte >> 5
        count = byte & 0x1F
        if count == 0:
            count = arg()
        if opcode == 0:  # zero
            room(count)
            out += bytes(count)
        elif opcode == 1:  # block copy
            room(count)
            out += read(count)
        elif opcode == 2:  # repeated block
            repeat = arg() + 1
            room(count * repeat)
            block = read(count)
            out += block * repeat
        elif opcode == 3:  # interleave repeat block with block copy
            custom_size = arg()
            repeat = arg()
            if repeat > MAX_RELOCATION_STEPS:
                raise PEFError("pidata repeat exceeds work limit")
            room(count * (repeat + 1) + custom_size * repeat)
            common = read(count)
            for _ in range(repeat):
                out += common
                out += read(custom_size)
            out += common
        elif opcode == 4:  # interleave repeat block with zero
            custom_size = arg()
            repeat = arg()
            if repeat > MAX_RELOCATION_STEPS:
                raise PEFError("pidata repeat exceeds work limit")
            room(count * (repeat + 1) + custom_size * repeat)
            common = bytes(count)
            for _ in range(repeat):
                out += common
                out += read(custom_size)
            out += common
        else:
            raise PEFError(f"unknown pidata opcode {opcode} at {pos - 1}")
    if len(out) != unpacked_size:
        raise PEFError(f"pidata expanded to {len(out)} bytes, expected {unpacked_size}")
    return out


class PEFContainer:
    def __init__(self, raw: bytes, name: str = ""):
        _span(raw, 0, 40)
        self.raw = raw
        self.name = name
        if raw[:12] != b"Joy!peffpwpc":
            raise PEFError("not a PowerPC PEF container")
        (self.format_version, self.timestamp, self.old_def_version, self.old_imp_version,
         self.current_version) = struct.unpack_from(">IIIII", raw, 12)
        self.section_count, self.inst_section_count = struct.unpack_from(">HH", raw, 32)
        if self.format_version != 1:
            raise PEFError("unsupported PEF format version")
        if self.inst_section_count > self.section_count:
            raise PEFError("instantiated section count exceeds section count")
        _span(raw, 40, 28 * self.section_count)
        self.sections: list[Section] = []
        name_table = 40 + 28 * self.section_count
        for i in range(self.section_count):
            o = 40 + 28 * i
            (name_off, addr, total, unpacked, packed, coff, kind, share, align, _r) = \
                struct.unpack_from(">iIIIIIBBBB", raw, o)
            sec_name = _cstr(raw, name_table + name_off) if name_off >= 0 else ""
            sec = Section(i, sec_name, addr, total, unpacked, packed, coff, kind, share, align)
            _span(raw, coff, packed)
            if total > MAX_SECTION_SIZE or unpacked > MAX_SECTION_SIZE:
                raise PEFError("section exceeds size limit")
            if kind in (0, 1, 2, 3, 6) and (unpacked > total or (kind != 2 and packed != unpacked)):
                raise PEFError("inconsistent section sizes")
            if sum(s.total_size for s in self.sections) + total > MAX_SECTION_SIZE:
                raise PEFError("container exceeds total instantiated size limit")
            body = raw[coff:coff + packed]
            if kind == 2:
                data = unpack_pidata(body, unpacked)
            else:
                data = bytearray(body)
            if kind in (0, 1, 2, 3, 6):
                data += bytes(max(0, total - len(data)))
            sec.data = data
            self.sections.append(sec)
        self.imports: list[ImportedSymbol] = []
        self.libraries: list[ImportedLibrary] = []
        self.exports: list[ExportedSymbol] = []
        self.relocs: dict[int, dict[int, RelocTarget]] = {}
        self.main = self.init = self.term = None
        loader = next((s for s in self.sections if s.kind == 4), None)
        if loader is not None:
            self._parse_loader(bytes(loader.data))

    # ------------------------------------------------------------------ loader
    def _parse_loader(self, ld: bytes) -> None:
        _span(ld, 0, 56)
        (main_sec, main_off, init_sec, init_off, term_sec, term_off, lib_count, sym_count,
         reloc_sec_count, reloc_instr_off, strings_off, hash_off, hash_power,
         export_count) = struct.unpack_from(">iIiIiIIIIIIIII", ld, 0)
        self.main = (main_sec, main_off) if main_sec >= 0 else None
        self.init = (init_sec, init_off) if init_sec >= 0 else None
        self.term = (term_sec, term_off) if term_sec >= 0 else None
        _span(ld, 56, 24 * lib_count + 4 * sym_count + 12 * reloc_sec_count)
        _span(ld, strings_off, 0)
        if hash_power > 20:
            raise PEFError("export hash table exceeds limit")
        _span(ld, hash_off, 4 * (1 << hash_power) + 14 * export_count)
        o = 56
        for _ in range(lib_count):
            name_off, old_imp, cur, cnt, first, opts = struct.unpack_from(">IIIIIB", ld, o)
            self.libraries.append(ImportedLibrary(_cstr(ld, strings_off + name_off), old_imp, cur,
                                                  cnt, first, opts))
            o += 24
        for i in range(sym_count):
            word = _u32(ld, o)
            o += 4
            cls = word >> 24
            self.imports.append(ImportedSymbol(i, _cstr(ld, strings_off + (word & 0xFFFFFF)),
                                               cls & 0x0F, bool(cls & 0x80)))
        for lib in self.libraries:
            if lib.first_symbol + lib.symbol_count > len(self.imports):
                raise PEFError("library import range exceeds symbol table")
            for i in range(lib.first_symbol, lib.first_symbol + lib.symbol_count):
                self.imports[i].library = lib.name
        reloc_headers = []
        for _ in range(reloc_sec_count):
            sec_index, _r, count, first = struct.unpack_from(">HHII", ld, o)
            reloc_headers.append((sec_index, count, first))
            o += 12
        for sec_index, count, first in reloc_headers:
            start = reloc_instr_off + first
            instrs = [_u16(ld, start + 2 * k) for k in range(count)]
            self.relocs[sec_index] = self._run_relocs(sec_index, instrs)
        # exports
        hash_entries = 1 << hash_power
        key_table = hash_off + 4 * hash_entries
        sym_table = key_table + 4 * export_count
        for i in range(export_count):
            key = _u32(ld, key_table + 4 * i)
            name_len = key >> 16
            word, value, sec = struct.unpack_from(">IIh", ld, sym_table + 10 * i)
            name_off = word & 0xFFFFFF
            _span(ld, strings_off + name_off, name_len)
            name = ld[strings_off + name_off:strings_off + name_off + name_len].decode("mac_roman")
            self.exports.append(ExportedSymbol(name, (word >> 24) & 0x0F, value, sec))

    def _run_relocs(self, sec_index: int, instrs: list[int]) -> dict[int, RelocTarget]:
        """Execute relocation instructions symbolically.

        Returns a map offset-in-section -> RelocTarget. Section-relative
        relocations are recorded with the section index they add; the stored
        word is the offset into that section.
        """
        if not 0 <= sec_index < self.inst_section_count:
            raise PEFError("invalid relocation section")
        data = self.sections[sec_index].data
        result: dict[int, RelocTarget] = {}
        inst_secs = [s.index for s in self.sections if s.kind in (0, 1, 2, 3, 6)]
        state = {
            "c": inst_secs[0] if len(inst_secs) > 0 else -1,
            "d": inst_secs[1] if len(inst_secs) > 1 else -1,
            "addr": 0,
            "imp": 0,
        }

        def by_section(sec: int) -> None:
            if not 0 <= sec < self.inst_section_count:
                raise PEFError("invalid relocation target section")
            a = state["addr"]
            result[a] = RelocTarget("section", sec, _u32(data, a))
            state["addr"] = a + 4

        def by_import(idx: int) -> None:
            if not 0 <= idx < len(self.imports):
                raise PEFError("invalid relocation import")
            a = state["addr"]
            result[a] = RelocTarget("import", idx, _u32(data, a))
            state["addr"] = a + 4

        # Keep instruction boundaries and 2-byte block offsets separately.
        # Apple Mac OS Runtime Architectures, pp. 8-32/8-34 and GL-6:
        # repeat blockCount counts halfwords, not variable-width instructions.
        units: list[tuple[int, ...]] = []
        block_offsets: list[int] = []
        instruction_at_block: dict[int, int] = {}
        k = 0
        while k < len(instrs):
            block_offsets.append(k)
            instruction_at_block[k] = len(units)
            ins = instrs[k]
            if ins >> 13 == 0b101:  # all 32-bit forms start with 101
                if k + 1 >= len(instrs):
                    raise PEFError("truncated relocation instruction")
                units.append((ins, instrs[k + 1]))
                k += 2
            else:
                units.append((ins,))
                k += 1

        def repeat_units(i: int, block_count: int) -> range:
            start_block = block_offsets[i] - block_count
            if start_block < 0:
                raise PEFError("relocation repeat before start of stream")
            if start_block not in instruction_at_block:
                raise PEFError("relocation repeat splits an instruction boundary")
            selected = range(instruction_at_block[start_block], i)
            for j in selected:
                opcode = units[j][0]
                if opcode >> 12 == 0b1001 or opcode >> 10 == 0b101100:
                    raise PEFError("nested relocation repeat is not permitted")
            return selected

        steps = 0

        def execute(i: int) -> None:
            nonlocal steps
            steps += 1
            if steps > MAX_RELOCATION_STEPS:
                raise PEFError("relocation execution exceeds limit")
            if not 0 <= i < len(units):
                raise PEFError("relocation repeat before start of stream")
            unit = units[i]
            ins = unit[0]
            if ins >> 14 == 0:  # RelocBySectDWithSkip
                state["addr"] += 4 * ((ins >> 6) & 0xFF)
                for _ in range(ins & 0x3F):
                    by_section(state["d"])
            elif ins >> 13 == 0b010:  # RelocRun
                sub = (ins >> 9) & 0xF
                for _ in range((ins & 0x1FF) + 1):
                    if sub == 0:
                        by_section(state["c"])
                    elif sub == 1:
                        by_section(state["d"])
                    elif sub == 2:
                        by_section(state["c"])
                        by_section(state["d"])
                        state["addr"] += 4
                    elif sub == 3:
                        by_section(state["c"])
                        by_section(state["d"])
                    elif sub == 4:
                        by_section(state["d"])
                        state["addr"] += 4
                    elif sub == 5:
                        by_import(state["imp"])
                        state["imp"] += 1
                    else:
                        raise PEFError(f"bad RelocRun subop {sub}")
            elif ins >> 13 == 0b011:  # RelocSmIndex
                sub = (ins >> 9) & 0xF
                idx = ins & 0x1FF
                if sub == 0:
                    by_import(idx)
                    state["imp"] = idx + 1
                elif sub == 1:
                    state["c"] = idx
                elif sub == 2:
                    state["d"] = idx
                elif sub == 3:
                    by_section(idx)
                else:
                    raise PEFError(f"bad RelocSmIndex subop {sub}")
            elif ins >> 12 == 0b1000:  # RelocIncrPosition
                state["addr"] += (ins & 0xFFF) + 1
            elif ins >> 12 == 0b1001:  # RelocSmRepeat
                selected = repeat_units(i, ((ins >> 8) & 0xF) + 1)
                for _ in range((ins & 0xFF) + 1):
                    for j in selected:
                        execute(j)
            elif ins >> 10 == 0b101000:  # RelocSetPosition
                state["addr"] = ((ins & 0x3FF) << 16) | unit[1]
            elif ins >> 10 == 0b101001:  # RelocLgByImport
                idx = ((ins & 0x3FF) << 16) | unit[1]
                by_import(idx)
                state["imp"] = idx + 1
            elif ins >> 10 == 0b101100:  # RelocLgRepeat
                selected = repeat_units(i, ((ins >> 6) & 0xF) + 1)
                for _ in range(((ins & 0x3F) << 16) | unit[1]):
                    for j in selected:
                        execute(j)
            elif ins >> 10 == 0b101101:  # RelocLgSetOrBySection
                sub = (ins >> 6) & 0xF
                idx = ((ins & 0x3F) << 16) | unit[1]
                if sub == 0:
                    by_section(idx)
                elif sub == 1:
                    state["c"] = idx
                elif sub == 2:
                    state["d"] = idx
                else:
                    raise PEFError(f"bad RelocLgSetOrBySection subop {sub}")
            else:
                raise PEFError(f"unknown relocation opcode {ins:#06x}")

        for i in range(len(units)):
            execute(i)
        return result

    # ------------------------------------------------------------- utilities
    def section_by_kind(self, kind: int) -> Section | None:
        return next((s for s in self.sections if s.kind == kind), None)

    @property
    def code(self) -> Section:
        return self.section_by_kind(0)

    @property
    def data_section(self) -> Section:
        return next(s for s in self.sections if s.kind in (1, 2))

    def import_name(self, idx: int) -> str:
        return self.imports[idx].name

    def summary(self) -> str:
        lines = [f"PEF {self.name}: {self.section_count} sections ({self.inst_section_count} instantiated)"]
        for s in self.sections:
            lines.append(f"  [{s.index}] {s.kind_name:9} size={s.total_size:#x} packed={s.packed_size:#x} "
                         f"unpacked={s.unpacked_size:#x} align={s.alignment}")
        lines.append(f"  main={self.main} init={self.init} term={self.term}")
        lines.append(f"  imports: {len(self.imports)} symbols from {len(self.libraries)} libraries")
        for lib in self.libraries:
            lines.append(f"    {lib.name}: {lib.symbol_count}")
        lines.append(f"  exports: {len(self.exports)}")
        for sec, rel in self.relocs.items():
            lines.append(f"  relocations in section {sec}: {len(rel)} words")
        return "\n".join(lines)


def load(path: str) -> PEFContainer:
    with open(path, "rb") as f:
        return PEFContainer(f.read(), path)


if __name__ == "__main__":
    for arg in sys.argv[1:]:
        print(load(arg).summary())

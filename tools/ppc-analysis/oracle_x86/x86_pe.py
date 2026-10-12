"""Minimal PE32 reader for the x86 differential oracle (docs/reverse/ORACLE.md, x86 section).

Pure Python, no Unicorn: it parses the headers, lays the sections out in a flat image, applies base
relocations when the image is loaded away from its preferred base, and rewrites every import address
table slot to a per-import trap address (`DLL!name`). The loader keeps no executable bytes outside the
process: `PeImage` holds them in memory only, and nothing here writes them anywhere.
"""
from __future__ import annotations

import struct
from dataclasses import dataclass, field

PAGE = 0x1000


class PeError(Exception):
    pass


def page_up(value: int) -> int:
    return (value + PAGE - 1) & ~(PAGE - 1)


@dataclass
class Section:
    name: str
    rva: int
    virtual_size: int
    raw_offset: int
    raw_size: int
    flags: int

    @property
    def writable(self) -> bool:
        return bool(self.flags & 0x80000000)

    @property
    def executable(self) -> bool:
        return bool(self.flags & 0x20000000)

    @property
    def span(self) -> int:
        """Bytes the section occupies in memory, page-rounded."""
        return page_up(max(self.virtual_size, self.raw_size))


@dataclass
class Import:
    dll: str
    name: str          # symbol name, or '#<ordinal>'
    slot_rva: int      # RVA of the IAT slot

    @property
    def key(self) -> str:
        return f'{self.dll.lower()}!{self.name}'


@dataclass
class PeImage:
    """Parsed PE32. `data` is the flat image from `base`, headers included, imports not yet trapped."""
    preferred_base: int
    size_of_image: int
    entry_rva: int
    sections: list[Section]
    imports: list[Import]
    reloc_directory: tuple[int, int]
    raw: bytes = field(repr=False)

    def section_at(self, rva: int) -> Section | None:
        for section in self.sections:
            if section.rva <= rva < section.rva + section.span:
                return section
        return None

    def read_cstring(self, rva: int) -> str:
        offset = self.rva_to_offset(rva)
        end = self.raw.index(b'\0', offset)
        return self.raw[offset:end].decode('latin-1')

    def rva_to_offset(self, rva: int) -> int:
        if rva < self.sections[0].rva:
            return rva  # headers
        section = self.section_at(rva)
        if section is None or rva - section.rva >= section.raw_size:
            raise PeError(f'RVA {rva:#x} has no file backing')
        return section.raw_offset + rva - section.rva

    def layout(self) -> bytearray:
        """The flat in-memory image (size_of_image bytes), sections copied, nothing relocated or trapped."""
        image = bytearray(page_up(self.size_of_image))
        first = self.sections[0].rva
        image[:first] = self.raw[:first]
        for section in self.sections:
            count = min(section.raw_size, section.virtual_size or section.raw_size)
            image[section.rva:section.rva + count] = self.raw[section.raw_offset:section.raw_offset + count]
        return image

    def relocations(self) -> list[int]:
        """RVAs of every HIGHLOW fixup."""
        rva, size = self.reloc_directory
        if not rva or not size:
            return []
        offset = self.rva_to_offset(rva)
        end = offset + size
        result = []
        while offset + 8 <= end:
            page, block = struct.unpack_from('<II', self.raw, offset)
            if block < 8:
                break
            for entry in struct.unpack_from(f'<{(block - 8) // 2}H', self.raw, offset + 8):
                kind, low = entry >> 12, entry & 0xFFF
                if kind == 3:
                    result.append(page + low)
                elif kind != 0:
                    raise PeError(f'unsupported relocation type {kind}')
            offset += block
        return result


def parse(raw: bytes) -> PeImage:
    if len(raw) < 0x40 or raw[:2] != b'MZ':
        raise PeError('not an MZ executable')
    pe = struct.unpack_from('<I', raw, 0x3C)[0]
    if raw[pe:pe + 4] != b'PE\0\0':
        raise PeError('no PE signature')
    machine, count = struct.unpack_from('<HH', raw, pe + 4)
    optional_size = struct.unpack_from('<H', raw, pe + 20)[0]
    if machine != 0x14C:
        raise PeError(f'not an x86 image (machine {machine:#x})')
    optional = pe + 24
    if struct.unpack_from('<H', raw, optional)[0] != 0x10B:
        raise PeError('not a PE32 image')
    entry = struct.unpack_from('<I', raw, optional + 16)[0]
    base = struct.unpack_from('<I', raw, optional + 28)[0]
    image_size = struct.unpack_from('<I', raw, optional + 56)[0]
    directories = [struct.unpack_from('<II', raw, optional + 96 + 8 * i) for i in range(16)]
    sections = []
    table = optional + optional_size
    for i in range(count):
        name, vsize, rva, rsize, roff = struct.unpack_from('<8sIIII', raw, table + 40 * i)
        flags = struct.unpack_from('<I', raw, table + 40 * i + 36)[0]
        sections.append(Section(name.rstrip(b'\0').decode('latin-1'), rva, vsize, roff, rsize, flags))
    image = PeImage(base, image_size, entry, sections, [], directories[5], raw)
    image.imports = _imports(image, directories[1])
    return image


def _imports(image: PeImage, directory: tuple[int, int]) -> list[Import]:
    rva, size = directory
    if not rva:
        return []
    result = []
    offset = image.rva_to_offset(rva)
    while True:
        original, _, _, name_rva, first = struct.unpack_from('<IIIII', image.raw, offset)
        if not (original or name_rva or first):
            break
        dll = image.read_cstring(name_rva)
        lookup = original or first
        n = 0
        while True:
            entry = struct.unpack_from('<I', image.raw, image.rva_to_offset(lookup + 4 * n))[0]
            if entry == 0:
                break
            if entry & 0x80000000:
                name = f'#{entry & 0xFFFF}'
            else:
                name = image.read_cstring((entry & 0x7FFFFFFF) + 2)
            result.append(Import(dll, name, first + 4 * n))
            n += 1
        offset += 20
    return result

"""Theme-key string, map/set order and short-read destinations behind the Mac gms.dat reader.

Seventh follow-up. Same rules as scenario_evidence.py: bounded instruction-field
checks at named offsets, interpreted values only, nothing executed. Unlike the
earlier passes this one also reads three of the shared libraries the identified
executable imports from (``libraries/`` next to ``SimThemePark.data``), each
pinned by SHA256: the Bullfrog string and file classes, the Feral file layer
and the C library. ``FSRead`` itself is a Mac OS routine that is not in the
assets; what it stores before end of file is the one documented-but-untraced
step left in the short-read chain.

Nothing here is evidence for a PC player file (none exists in the supplied assets).
"""
from __future__ import annotations

import hashlib
from pathlib import Path
import sys

sys.path.insert(0, str(Path(__file__).resolve().parent))
from scenario_evidence import Evidence, pef  # noqa: E402
from profile_evidence import import_at  # noqa: E402

LIBRARIES = {
    'bullfrog_shared.data': 'b67b56b7b2b75962b8b34559e20fd97b623b2d0f7f08a0035f82072ffbf4ec06',
    'c_c++_shared.data': '5e04f9c00c922dc78a787d1b93067c75d37a3e65b0a0202e50c2f449f131b27f',
    'macdoze_shared.data': 'ba11331a70bce77140ae6e7fe73145fe4e13cce5f042707a494cb77654b32f0d',
}
LT = '__lt__15TbStringBase<c>CFRC15TbStringBase<c>'


class LibraryEvidence(Evidence):
    """Evidence over an imported shared library: no main vector, TOC taken from the export vectors."""

    def __init__(self, container: pef.PEFContainer):
        self.c = container
        self.code = bytes(container.code.data)
        self.data = bytes(container.data_section.data)
        self.relocs = container.relocs[container.data_section.index]
        self.checked = 0
        self._calls = None
        tocs = {self.relocs[x.value + 4].addend for x in container.exports
                if x.sym_class == 2 and x.section == container.data_section.index and x.value + 4 in self.relocs}
        if len(tocs) != 1:
            raise pef.PEFError(f'{container.name}: export vectors carry TOC bases {sorted(tocs)}')
        self.toc = tocs.pop()

    def export(self, name: str) -> int:
        """Code offset of an exported routine (through its transition vector)."""
        for x in self.c.exports:
            if x.name == name:
                return self.vector(x.value)
        raise pef.PEFError(f'{self.c.name}: no export {name!r}')

    def vector(self, data_offset: int) -> int:
        r = self.relocs.get(data_offset)
        if r is None or r.kind != 'section' or r.target != self.c.code.index:
            raise pef.PEFError(f'data:{data_offset:#x} is not a code vector')
        return r.addend

    def slot(self, vtable: int, offset: int) -> int:
        """Code offset of a virtual slot: vtable word -> transition vector -> code."""
        r = self.relocs.get(vtable + offset)
        if r is None or r.kind != 'section' or r.target != self.c.data_section.index:
            raise pef.PEFError(f'data:{vtable + offset:#x} is not a vector pointer')
        return self.vector(r.addend)

    def is_at(self, actual: int, expected: int, what: str) -> None:
        if actual != expected:
            self.fail(actual, what, hex(actual))
        self.checked += 1

    def glue_callers(self, name: str) -> list[int]:
        """Every linked branch whose target is CFM glue for the named import."""
        out = []
        for target in range(0, len(self.code), 4):
            w = self.word(target)
            if w >> 16 != 0x8182:
                continue
            disp = w & 0xffff
            disp = disp - 0x10000 if disp & 0x8000 else disp
            r = self.relocs.get(self.toc + disp)
            if r is not None and r.kind == 'import' and self.c.imports[r.target].name == name:
                out += self.calls_to(target)
        return sorted(out)


def load_library(bin_root: Path, name: str) -> LibraryEvidence:
    raw = (bin_root / 'libraries' / name).read_bytes()
    digest = hashlib.sha256(raw).hexdigest()
    if digest != LIBRARIES[name]:
        raise pef.PEFError(f'identity of {name}: unexpected interpreted value {digest!r}')
    return LibraryEvidence(pef.PEFContainer(raw, name))


def writes(w: int) -> set[int]:
    """Registers an integer instruction of the forms used below may write (conservative)."""
    op, rt, ra = w >> 26, w >> 21 & 31, w >> 16 & 31
    if op in (7, 8, 10, 11, 12, 13, 14, 15) or 32 <= op <= 35 or 40 <= op <= 43:
        return {rt} | ({ra} if op in (33, 35, 41, 43) else set())
    if op in (37, 39, 45):           # update stores
        return {ra}
    if op == 46:                     # lmw
        return set(range(rt, 32))
    if op in (20, 21, 23, 24, 25, 26, 27, 28, 29):
        return {ra}
    if op == 31:
        xo = w >> 1 & 0x3ff
        if xo in (444, 28, 316, 124, 24, 536, 792, 26, 954, 922, 824, 476, 412, 284, 60):
            return {ra}
        if xo in (151, 215, 407, 662, 918, 0, 32):   # stores and compares
            return set()
        return {rt}
    return set()


def keeps(e: Evidence, start: int, end: int, reg: int) -> None:
    for o in range(start, end, 4):
        if reg in writes(e.word(o)):
            e.fail(o, f'r{reg} rewritten', hex(e.word(o)))
    e.checked += 1


# -- theme key: string constructor and comparator ---------------------------------------------------
def theme_key(bf: LibraryEvidence, libc: LibraryEvidence) -> dict:
    """TbDynamicStringTemplate<c>(const char *) = strlen + AllocBuffer(n) + strcpy; operator< = strncmp
    over the shorter length, then the shorter string is less."""
    ct = bf.export('__ct__26TbDynamicStringTemplate<c>FPCc')
    bf.is_at(ct, 0xadcc, 'char* constructor')
    bf.x(0xadd4, 31, 444, 4, 31, 4)                  # or. r31, r4, r4: null pointer -> empty string
    bf.bc(0xae04, 12, 2, 0xae40)
    bf.d(0xadf4, 14, 0, 2, 0x37e0)                   # vptr TbDynamicStringTemplate<c>
    bf.d(0xae00, 36, 0, 30, 0)
    bf.bl(0xae10, 0xb42c)                            # length of the argument ...
    bf.import_call(0xb43c, 'strlen')
    bf.d(0xae20, 32, 12, 12, 0x10)                   # ... passed to virtual slot 0x10 ...
    bf.is_at(bf.slot(0x37e0, 0x10), bf.export('AllocBuffer__26TbDynamicStringTemplate<c>FUi'), 'slot 0x10')
    bf.d(0xae2c, 32, 4, 30, 8)                       # ... then strcpy(buffer, argument)
    bf.d(0xae34, 14, 5, 31, 0)
    bf.bl(0xae38, 0xb454)
    bf.import_call(0xb468, 'strcpy')
    alloc = bf.export('AllocBuffer__26TbDynamicStringTemplate<c>FUi')
    bf.d(alloc + 0x1c, 36, 4, 3, 4)                  # length = n
    bf.d(alloc + 0x4c, 14, 4, 0, 0)
    bf.x(alloc + 0x58, 31, 215, 4, 3, 0)             # buffer[n] = 0
    lt = bf.export(LT)
    bf.is_at(lt, 0xcdc0, 'operator<')
    bf.d(0xcddc, 32, 3, 3, 4)
    bf.d(0xcde0, 32, 0, 4, 4)
    bf.x(0xcde4, 31, 32, 0, 3, 0)                    # cmplw: shorter length, unsigned
    bf.import_call(0xce04, 'strncmp')
    bf.d(0xce0c, 11, 0, 3, 0)
    bf.bc(0xce10, 4, 2, 0xce34)
    bf.d(0xce14, 32, 3, 31, 4)                       # equal prefix: other.length > this.length
    bf.d(0xce18, 32, 0, 30, 4)
    bf.x(0xce1c, 31, 32, 0, 3, 0)
    bf.bc(0xce20, 4, 1, 0xce2c)
    bf.d(0xce24, 14, 3, 0, 1)
    bf.d(0xce2c, 14, 3, 0, 0)
    bf.rlwinm(0xce40, 0, 0, 1, 31, 31)               # differing prefix: strncmp result < 0
    bf.rlwinm(0xce48, 3, 3, 0, 31, 31)
    # The C library: strlen and strcpy stop at the first zero byte; strncmp compares zero-extended bytes.
    strlen, strcpy, strncmp = (libc.export(n) for n in ('strlen', 'strcpy', 'strncmp'))
    for at, expected in ((strlen, 0x20db8), (strcpy, 0x20dd8), (strncmp, 0x20ee4)):
        libc.is_at(at, expected, 'C library export')
    libc.d(0x20dc0, 35, 0, 3, 1)                     # lbzu
    libc.d(0x20dc8, 10, 0, 0, 0)
    libc.d(0x20de0, 35, 0, 4, 1)
    libc.d(0x20de4, 10, 0, 0, 0)
    libc.d(0x20de8, 39, 0, 5, 1)                     # stbu: the terminator is copied, then the loop ends
    libc.d(0x20ef4, 35, 0, 3, 1)
    libc.d(0x20ef8, 35, 5, 4, 1)
    libc.x(0x20efc, 31, 32, 0, 0, 5)                 # cmplw on zero-extended bytes
    libc.x(0x20f04, 31, 40, 3, 5, 0)                 # return a - b
    libc.d(0x20f0c, 10, 0, 0, 0)                     # stops at a shared zero byte
    return {'constructor': 'const char*: strlen(argument), AllocBuffer(length) sets length and a terminator, '
                           'strcpy copies through the first zero byte; a null pointer gives the empty string',
            'key': 'the name bytes before the first NUL (high)',
            'comparator': 'operator<: strncmp over min(length) on unsigned bytes; on an equal prefix the shorter '
                          'string is less. Keys hold no NUL, so this is unsigned lexicographic byte order',
            'equality': 'neither operand less: identical key bytes'}


# -- map and set: insert, iteration, writer order ---------------------------------------------------
def containers(e: Evidence) -> dict:
    """Theme map and mystery set: red-black trees (left +0, right +4, parent|colour +8, value +0xc) with
    the leftmost node cached; the writer walks leftmost -> in-order successor -> header."""
    # Map insert-unique: operator< both ways, root at tree+4, leftmost at tree+0xc.
    e.d(0x12b2d8, 32, 29, 4, 4)
    e.d(0x12b2e8, 14, 4, 29, 12)
    import_at(e, 0x12b2ec, LT)                      # key < node
    e.rlwinm(0x12b2f4, 3, 0, 0, 24, 31)
    e.d(0x12b2fc, 32, 29, 29, 0)                     # less: left
    e.d(0x12b30c, 32, 29, 29, 4)                     # not less: right, remember the node
    e.d(0x12b328, 14, 3, 31, 12)
    import_at(e, 0x12b330, LT)                      # remembered < key, else duplicate
    e.bc(0x12b33c, 12, 2, 0x12b36c)
    e.bl(0x12b354, 0x12b698)
    e.rlwinm(0x12b7fc, 27, 0, 0, 24, 31)             # every step went left ...
    e.d(0x12b804, 36, 29, 24, 12)                    # ... new leftmost at tree+0xc
    # Set insert-unique: u16 compared unsigned, leftmost at tree+8.
    e.d(0x12b5d8, 40, 3, 7, 0)
    e.d(0x12b5e0, 40, 0, 10, 12)
    e.x(0x12b5e4, 31, 32, 0, 3, 0)
    e.bc(0x12b5e8, 4, 0, 0x12b5f8)
    e.x(0x12b620, 31, 32, 0, 3, 0)
    e.bc(0x12b624, 4, 0, 0x12b648)
    e.bl(0x12b630, 0x12b868)
    e.d(0x12b998, 36, 29, 24, 8)
    # In-order successor.
    e.d(0x116e98, 32, 4, 4, 4)
    e.d(0x116eac, 32, 0, 4, 0)
    e.rlwinm(0x116ed0, 0, 4, 0, 0, 30)
    e.x(0x116ed8, 31, 32, 0, 5, 0)
    # Writer: theme map (tree at record+0x34) then mystery set (record+0x28).
    e.d(0x129458, 32, 0, 26, 0x34)                   # count = map size
    e.d(0x129484, 32, 0, 26, 0x40)                   # begin = leftmost
    e.d(0x129488, 14, 24, 26, 0x38)                  # end = header
    e.d(0x129494, 14, 3, 3, 12)
    import_at(e, 0x129498, 'Length__15TbStringBase<c>CFv')
    e.d(0x1294e0, 32, 4, 4, 0x14)                    # name bytes = key buffer
    import_at(e, 0x1294e4, 'strcpy')
    e.d(0x129548, 32, 3, 3, 0x14)                    # value: the theme record pointer
    e.bl(0x129564, 0x116e94)
    e.x(0x12956c, 31, 32, 0, 3, 24)
    e.d(0x129598, 32, 0, 26, 0x28)
    e.d(0x1295c4, 32, 0, 26, 0x30)
    e.d(0x1295c8, 14, 24, 26, 0x2c)
    e.d(0x1295d4, 40, 0, 3, 12)
    e.bl(0x129604, 0x116e94)
    e.x(0x12960c, 31, 32, 0, 3, 24)
    return {'theme_map': 'tree at record+0x34: size +0, root +4 (header), leftmost +0xc; insert-unique with '
                         'TbStringBase<c>::operator<',
            'mystery_set': 'tree at record+0x28: size +0, root +4 (header), leftmost +8; u16 compared unsigned',
            'writer_order': 'themes ascending by key (count = map size, name = key); rideIds ascending unsigned '
                            '(count = set size)'}


# -- short reads: where the bytes land ---------------------------------------------------------------
def short_reads(e: Evidence, bf: LibraryEvidence, md: LibraryEvidence) -> dict:
    """Member imports read straight into the member through LbFile_Read -> disk file -> FSRead and fail,
    without the swap, unless the full width arrived."""
    read = bf.export('LbFile_Read__FPvPvUlPUl')
    bf.is_at(read, 0x994, 'LbFile_Read')
    bf.d(0x9c8, 32, 12, 12, 0x10)                    # file->Read(buffer, n)
    bf.d(0x9dc, 36, 3, 31, 0)                        # *got = returned count
    bf.x(0x9e0, 31, 40, 0, 3, 30)                    # status = (n - got == 0)
    bf.x(0x9e4, 31, 26, 0, 0, 0)
    bf.rlwinm(0x9e8, 0, 0, 27, 24, 31)
    keeps(bf, read, 0x9c0, 5)                        # the buffer argument (r5) is passed on as r4
    bf.d(0x9c0, 14, 4, 5, 0)
    keeps(bf, 0x9c4, 0x9cc, 4)
    # LbFile_Open's 8-byte handle (vptr 0x440c) forwards Read to its inner file's slot 0xc.
    bf.d(0x1e20c, 14, 0, 2, 0x440c)
    bf.is_at(bf.slot(0x440c, 0x10), 0xa0c, 'handle Read slot')
    bf.d(0xa18, 32, 3, 3, 4)
    bf.d(0xa30, 32, 12, 12, 0xc)
    keeps(bf, 0xa0c, 0xa34, 4)
    keeps(bf, 0xa0c, 0xa34, 5)
    # The disk file class (vptr 0x2c04) built by the storage open at 0x3d8c.
    bf.d(0x3484, 14, 5, 2, 0x2c04)
    bf.bl(0x3d8c, 0x3460)
    bf.is_at(bf.slot(0x2c04, 0xc), 0x368c, 'disk Read slot')
    callers = bf.glue_callers('ReadFile__10NS_MacDozeFPQ210NS_MacDoze14InternalHandlePvUlPUlPv')
    if callers != [0x36bc]:
        bf.fail(0x36bc, 'ReadFile callers', [hex(c) for c in callers])
    bf.checked += 1
    bf.d(0x36b0, 32, 3, 31, 0xc)
    keeps(bf, 0x368c, 0x36bc, 4)
    keeps(bf, 0x368c, 0x36bc, 5)
    bf.d(0x36d4, 32, 3, 1, 0x38)                     # returns the count FSRead reported
    # Feral's ReadFile: count = n, FSRead(refNum, &count, buffer).
    rf = md.export('ReadFile__10NS_MacDozeFPQ210NS_MacDoze14InternalHandlePvUlPUlPv')
    md.is_at(rf, 0x2ad8, 'ReadFile')
    md.d(0x2ae8, 14, 28, 4, 0)
    md.d(0x2b18, 36, 29, 30, 0)
    md.d(0x2b1c, 14, 4, 30, 0)
    md.d(0x2b20, 14, 5, 28, 0)
    md.import_call(0x2b2c, 'FSRead')
    keeps(md, 0x2aec, 0x2b20, 28)
    # Game-side helpers: the member is the destination; success needs the full width.
    e.d(0xc354, 14, 6, 0, 4)
    e.d(0xc398, 10, 0, 0, 4)
    e.bc(0xc39c, 12, 2, 0xc3b4)
    e.x(0xc3d4, 31, 662, 4, 0, 31)
    e.d(0x126f88, 10, 0, 0, 8)
    e.bc(0x126f8c, 12, 2, 0x126fa4)
    # rideIds and both counts are read into stack temporaries; the set insert follows success only.
    e.d(0x129908, 14, 4, 1, 0x110)
    e.d(0x129934, 14, 4, 1, 0x10c)
    e.bc(0x129948, 12, 2, 0x129954)
    e.d(0x12995c, 14, 5, 1, 0x10c)
    return {'chain': 'member import -> LbFile_Read (status = full count) -> handle slot 0x10 -> disk file '
                     'slot 0xc -> NS_MacDoze::ReadFile -> FSRead(refNum, &count, member address)',
            'u8': '1 byte requested: a short read delivers nothing, the member keeps its value',
            'i32': 'k < 4 bytes delivered land in member bytes 0..k-1 (native big-endian); the swap is skipped, '
                   'so the member reads big-endian over its earlier bytes',
            'settings_8_byte': 'k < 8 bytes land in the game-wide settings member; neither word is swapped',
            'stack_temporaries': 'counts and rideIds: a short read changes no record state',
            'dependency': 'FSRead placing the bytes before end of file into the buffer is Mac OS behaviour '
                          '(Inside Macintosh), not code in the assets'}


def settings_apply(e: Evidence) -> dict:
    """0x126460 tests byte 0 of each 8-byte volume member and passes the second word on; it stores nothing."""
    e.toc_load(0x126468, 31, ('data', 0x120a14))
    for flag, level, offset in ((0x126474, 0x126488, 0x18), (0x12649c, 0x1264b0, 0x20),
                                (0x1264c4, 0x1264d8, 0x10), (0x1264ec, 0x1264f8, 0x28)):
        e.d(flag, 34, 0, 31, offset)                 # lbz: first (raw) word, byte 0
        e.d(level, 32, 3 if level != 0x1264f8 else 0, 31, offset + 4)
    e.d(0x1264fc, 15, 3, 0, 0x51ec)
    e.d(0x126504, 7, 0, 0, 0x3ff)                    # movie: level * 1023 / 100
    for o in range(0x126460, 0x12653c, 4):           # no store through the settings pointer
        w = e.word(o)
        if 36 <= w >> 26 <= 45 and w >> 16 & 31 == 31:
            e.fail(o, 'settings stored', hex(w))
    e.checked += 1
    return {'order': ['MusicVolume', 'SpeechVolume', 'SFXVolume', 'MovieVolume'],
            'volume_member': 'disk byte 0 (raw first word, high byte) non-zero = enabled; second word (LE on disk) '
                             '= level; MovieVolume level scaled trunc(level * 1023 / 100)',
            'stores': 'none into the settings object; the eight one-byte members are not read here'}


def inspect_native_io(e: Evidence, bin_root: Path) -> dict:
    bf = load_library(bin_root, 'bullfrog_shared.data')
    libc = load_library(bin_root, 'c_c++_shared.data')
    md = load_library(bin_root, 'macdoze_shared.data')
    out = {'native_io_identity': dict(LIBRARIES), 'theme_key_string': theme_key(bf, libc),
           'profile_containers': containers(e), 'short_read_destination': short_reads(e, bf, md),
           'settings_apply': settings_apply(e)}
    e.checked += bf.checked + libc.checked + md.checked
    return out

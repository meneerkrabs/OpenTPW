"""Static analysis of a PEF PowerPC container: functions, cross references, strings.

Builds on ``pef.py``. For one container it derives

* the TOC base (from the main/init transition vector or the most common TOC
  word of all transition vectors),
* function entry points: transition-vector code pointers, ``bl`` targets,
  export table entries and prologue starts after ``blr``; glue stubs for
  imported functions are recognised and named after the import,
* names: exports (demangled), import glue, CodeWarrior traceback-table names
  where present, RTTI class names attached to virtual-table slots
  (``Class::vf<N>``) and user-supplied names from ``names/<container>.txt``,
* per-instruction value tracking inside a function (TOC loads, ``lis/addi``
  pairs, ``addi`` offsets) giving cross references to code (calls), data
  (loads/stores, with offset), imports, strings and float constants.

The analysis result is cached in ``$XDG_CACHE_HOME/opentpw-ppc`` (or
``~/.cache/opentpw-ppc``) keyed by the container's size and mtime.

Nothing from the analysed binary is written into the repository; the cache
lives outside it.
"""

from __future__ import annotations

import bisect
import collections
import os
import pickle
import re
import struct
from dataclasses import dataclass, field

import demangle
import pef

CACHE_VERSION = 7
HERE = os.path.dirname(os.path.abspath(__file__))


def _cache_dir() -> str:
    base = os.environ.get("XDG_CACHE_HOME") or os.path.join(os.path.expanduser("~"), ".cache")
    path_ = os.path.join(base, "opentpw-ppc")
    os.makedirs(path_, exist_ok=True)
    return path_


# --------------------------------------------------------------------- decode
def sext16(v: int) -> int:
    return v - 0x10000 if v & 0x8000 else v


def decode_fields(w: int) -> tuple[int, int, int, int]:
    """primary opcode, rD/rS, rA, 16-bit immediate"""
    return w >> 26, (w >> 21) & 31, (w >> 16) & 31, w & 0xFFFF


LOAD_OPS = {32: ("lwz", 4), 33: ("lwzu", 4), 34: ("lbz", 1), 35: ("lbzu", 1), 40: ("lhz", 2),
            41: ("lhzu", 2), 42: ("lha", 2), 43: ("lhau", 2), 46: ("lmw", 4)}
STORE_OPS = {36: ("stw", 4), 37: ("stwu", 4), 38: ("stb", 1), 39: ("stbu", 1), 44: ("sth", 2),
             45: ("sthu", 2), 47: ("stmw", 4)}
FLOAD_OPS = {48: ("lfs", 4), 49: ("lfsu", 4), 50: ("lfd", 8), 51: ("lfdu", 8)}
FSTORE_OPS = {52: ("stfs", 4), 53: ("stfsu", 4), 54: ("stfd", 8), 55: ("stfdu", 8)}


def integer_dest(w: int) -> int | None:
    """Register written by an integer instruction (None if none / not an integer GPR write)."""
    op, rd, ra, _ = decode_fields(w)
    if op in (14, 15, 7, 8, 12, 13) or op in LOAD_OPS:
        return rd
    if op in (24, 25, 26, 27, 28, 29, 20, 21, 23):  # ori oris xori xoris andi. andis. rlwimi rlwinm rlwnm
        return ra
    if op == 31:
        xo = (w >> 1) & 0x3FF
        # logical / shift forms write rA
        if xo in (28, 60, 124, 284, 316, 412, 444, 476, 24, 536, 792, 824, 26, 954, 922, 986):
            return ra
        if xo in (339, 371, 19, 83, 23, 55, 87, 119, 279, 311, 343, 375, 533, 534, 790, 597):
            return rd  # mfspr mftb mfcr mfmsr lwzx ... lhbrx etc
        if xo in (266, 10, 138, 40, 8, 136, 104, 232, 200, 202, 234, 235, 11, 75, 459, 491, 107,
                  266 | 0x200, 10 | 0x200, 138 | 0x200, 40 | 0x200, 8 | 0x200, 104 | 0x200,
                  235 | 0x200, 491 | 0x200, 459 | 0x200):
            return rd
        if (xo & 0x1FF) in (266, 10, 138, 40, 8, 136, 104, 232, 200, 202, 234, 235, 11, 75, 459, 491):
            return rd
    return None


@dataclass
class Ref:
    at: int          # instruction address (code offset)
    kind: str        # call, tailcall, code, data_r, data_w, data_addr, import, string, float, const, vcall
    target: object   # int address / import index / value
    extra: object = None


@dataclass
class Function:
    start: int
    end: int
    name: str = ""
    origin: str = ""   # export, tvector, bl, prologue, glue, traceback
    refs: list[Ref] = field(default_factory=list)

    @property
    def size(self) -> int:
        return self.end - self.start


class Analysis:
    def __init__(self, container: pef.PEFContainer):
        self.c = container
        self.short = os.path.basename(container.name).replace(".data", "")
        self.code = bytes(container.code.data)
        self.code_index = container.code.index
        ds = container.data_section
        self.data = bytes(ds.data)
        self.data_index = ds.index
        self.relocs = container.relocs.get(self.data_index, {})
        self.toc = self._find_toc()
        self.functions: dict[int, Function] = {}
        self.starts: list[int] = []
        self.vtables: dict[int, dict] = {}
        self.glue: dict[int, int] = {}  # code addr -> import index
        self.names_user: dict[int, str] = {}
        self._build()

    # ---------------------------------------------------------------- basics
    def u32c(self, o: int) -> int:
        return struct.unpack_from(">I", self.code, o)[0]

    def u32d(self, o: int) -> int:
        return struct.unpack_from(">I", self.data, o)[0]

    def reloc(self, data_off: int):
        return self.relocs.get(data_off)

    def pointer_at(self, data_off: int):
        """('code'|'data'|'import', value) for a relocated data word, else None."""
        t = self.relocs.get(data_off)
        if t is None:
            return None
        if t.kind == "import":
            return ("import", t.target)
        return ("code" if t.target == self.code_index else "data", t.addend)

    def _find_toc(self) -> int:
        for entry in (self.c.main, self.c.init):
            if entry and entry[0] == self.data_index:
                t = self.relocs.get(entry[1] + 4)
                if t and t.kind == "section":
                    return t.addend
        counter = collections.Counter()
        for off, t in self.relocs.items():
            if t.kind == "section" and t.target == self.code_index:
                n = self.relocs.get(off + 4)
                if n and n.kind == "section" and n.target == self.data_index:
                    counter[n.addend] += 1
        return counter.most_common(1)[0][0] if counter else 0

    def cstring_code(self, off: int, limit: int = 300) -> str | None:
        return _cstring(self.code, off, limit)

    def cstring_data(self, off: int, limit: int = 300) -> str | None:
        return _cstring(self.data, off, limit)

    # ------------------------------------------------------------ functions
    def _build(self) -> None:
        code = self.code
        n = len(code)
        starts: dict[int, str] = {}
        names: dict[int, str] = {}
        # exports
        for e in self.c.exports:
            if e.sym_class == 2 and e.section == self.data_index:  # tvector
                t = self.relocs.get(e.value)
                if t and t.kind == "section" and t.target == self.code_index:
                    starts[t.addend] = "export"
                    names.setdefault(t.addend, e.name)
            elif e.sym_class == 0 and e.section == self.code_index:
                starts[e.value] = "export"
                names.setdefault(e.value, e.name)
        # transition vectors
        self.tvectors: dict[int, int] = {}
        for off, t in self.relocs.items():
            if t.kind == "section" and t.target == self.code_index:
                nxt = self.relocs.get(off + 4)
                if nxt and nxt.kind == "section" and nxt.target == self.data_index and nxt.addend == self.toc:
                    self.tvectors[off] = t.addend
                    starts.setdefault(t.addend, "tvector")
        # glue stubs: lwz r12,X(r2); stw r2,20(r1); lwz r0,0(r12); lwz r2,4(r12); mtctr r0; bctr
        for o in range(0, n - 24, 4):
            w = self.u32c(o)
            if w >> 16 == 0x8182 and self.u32c(o + 4) == 0x90410014 and self.u32c(o + 8) == 0x800C0000 \
                    and self.u32c(o + 12) == 0x804C0004 and self.u32c(o + 16) == 0x7C0903A6 \
                    and self.u32c(o + 20) == 0x4E800420:
                slot = self.toc + sext16(w & 0xFFFF)
                t = self.relocs.get(slot)
                if t and t.kind == "import":
                    self.glue[o] = t.target
                    starts[o] = "glue"
                    names[o] = "glue:" + self.c.imports[t.target].name
        # bl targets
        for o in range(0, n, 4):
            w = self.u32c(o)
            if w >> 26 == 18 and (w & 3) == 1:
                tgt = o + _li(w)
                if 0 <= tgt < n:
                    starts.setdefault(tgt, "bl")
        # prologues after an unconditional return / branch
        for o in range(0, n - 4, 4):
            w = self.u32c(o)
            if w == 0x4E800020 or (w >> 26 == 18 and (w & 3) == 0) or w == 0x4E800420:
                nxt = self.u32c(o + 4)
                if nxt == 0x7C0802A6 or nxt >> 16 == 0x9421:  # mflr r0 / stwu r1,-x(r1)
                    starts.setdefault(o + 4, "prologue")
        # traceback tables (CodeWarrior "traceback tables" option): a zero word
        # followed by version 0 and the name_present bit.
        for o in range(0, n - 16, 4):
            if self.u32c(o) == 0 and code[o + 4] == 0 and code[o + 7] & 0x40 and code[o + 6] & 0x20:
                tb = o + 4
                fixed, flt = code[tb + 6], code[tb + 7] >> 1
                p = tb + 8
                if fixed or flt:
                    p += 4
                tb_off = struct.unpack_from(">I", code, p)[0]
                p += 4
                if code[tb + 3] & 0x80:
                    p += 4
                if code[tb + 2] & 0x08:
                    cnt = struct.unpack_from(">I", code, p)[0]
                    p += 4 + 4 * cnt
                if 0 < tb_off < 0x100000 and tb_off % 4 == 0 and o - tb_off >= 0:
                    ln = struct.unpack_from(">H", code, p)[0]
                    nm = code[p + 2:p + 2 + ln]
                    if 0 < ln < 512 and all(32 <= b < 127 for b in nm):
                        s = o - tb_off
                        starts[s] = "traceback"
                        names.setdefault(s, nm.decode())
        ordered = sorted(starts)
        self.starts = ordered
        for i, s in enumerate(ordered):
            end = ordered[i + 1] if i + 1 < len(ordered) else n
            self.functions[s] = Function(s, end, names.get(s, ""), starts[s])
        self._load_user_names()
        self._rtti()
        for f in self.functions.values():
            self._scan(f)

    def _load_user_names(self) -> None:
        path_ = os.path.join(HERE, "names", self.short + ".txt")
        if not os.path.exists(path_):
            return
        with open(path_, encoding="utf-8") as fh:
            for line in fh:
                line = line.split("#", 1)[0].strip()
                if not line:
                    continue
                addr, nm = line.split(None, 1)
                self.names_user[int(addr, 16)] = nm.strip()

    def _rtti(self) -> None:
        """CodeWarrior RTTI: typeinfo = {char* name (code), bases*}; vtable = {typeinfo*, 0, slots...}."""
        typeinfo: dict[int, str] = {}
        for off, t in self.relocs.items():
            if t.kind == "section" and t.target == self.code_index and off % 4 == 0:
                s = self.cstring_code(t.addend, 200)
                if s and re.fullmatch(r"[A-Za-z_][\w:<>, *&]*", s) and len(s) > 2:
                    typeinfo[off] = s
        for off, t in self.relocs.items():
            if t.kind == "section" and t.target == self.data_index and t.addend in typeinfo:
                if off + 4 in self.relocs or self.u32d(off + 4) != 0:
                    continue
                slots = []
                p = off + 8
                while p in self.relocs:
                    pt = self.relocs[p]
                    if pt.kind == "import":
                        slots.append(("import", pt.target))
                    elif pt.target == self.data_index and pt.addend in self.tvectors:
                        slots.append(("code", self.tvectors[pt.addend]))
                    else:
                        break
                    p += 4
                if slots:
                    cls = typeinfo[t.addend]
                    self.vtables[off] = {"class": cls, "slots": slots}
                    for k, (kind, v) in enumerate(slots):
                        if kind == "code" and v in self.functions:
                            f = self.functions[v]
                            label = f"{cls}::vf{k}"
                            if not f.name:
                                f.name = label
                            elif label not in f.name and f.name.count("|") < 3:
                                f.name += "|" + label

    # ------------------------------------------------------------- scanning
    def _scan(self, f: Function) -> None:
        if f.origin == "glue":
            return
        regs: dict[int, tuple] = {}
        fregs = {}
        code = self.code
        for o in range(f.start, f.end, 4):
            w = struct.unpack_from(">I", code, o)[0]
            op, rd, ra, imm = decode_fields(w)
            simm = sext16(imm)
            if o in self.functions and o != f.start:
                regs.clear()
            if op == 18:
                tgt = o + _li(w) if not (w & 2) else _li(w)
                if w & 1:
                    f.refs.append(Ref(o, "call", tgt))
                    for r in range(0, 13):
                        regs.pop(r, None)
                else:
                    if not (f.start <= tgt < f.end):
                        f.refs.append(Ref(o, "tailcall", tgt))
                continue
            if op == 32 and ra == 2:  # lwz rD, d(r2): TOC slot
                slot = self.toc + simm
                p = self.pointer_at(slot)
                if p is None:
                    val = self.u32d(slot) if 0 <= slot < len(self.data) - 3 else None
                    regs[rd] = ("const", val)
                    f.refs.append(Ref(o, "toc_const", slot, val))
                else:
                    regs[rd] = p
                    self._ref_pointer(f, o, p, 0)
                continue
            if op in (48, 50) and ra == 2:  # lfs/lfd directly from TOC
                slot = self.toc + simm
                val = _float_at(self.data, slot, op == 50)
                f.refs.append(Ref(o, "float", val, ("toc", slot)))
                continue
            if op == 14:  # addi
                base = regs.get(ra) if ra else ("const", 0)
                if base is None:
                    regs.pop(rd, None)
                    continue
                if base[0] == "const":
                    regs[rd] = ("const", None if base[1] is None else (base[1] + simm) & 0xFFFFFFFF)
                elif base[0] in ("code", "data"):
                    regs[rd] = (base[0], base[1] + simm)
                    if simm:
                        self._ref_pointer(f, o, regs[rd], 0, addr_only=True)
                else:
                    regs.pop(rd, None)
                continue
            if op == 15:  # addis
                base = regs.get(ra) if ra else ("const", 0)
                if base and base[0] == "const" and base[1] is not None:
                    regs[rd] = ("const", (base[1] + (simm << 16)) & 0xFFFFFFFF)
                elif base and base[0] in ("code", "data"):
                    regs[rd] = (base[0], base[1] + (simm << 16))
                else:
                    regs.pop(rd, None)
                continue
            if op == 24 and w != 0x60000000:  # ori rA, rS, imm
                base = regs.get(rd)
                if base and base[0] == "const" and base[1] is not None:
                    regs[ra] = ("const", base[1] | imm)
                else:
                    regs.pop(ra, None)
                continue
            if op == 31 and ((w >> 1) & 0x3FF) == 444 and rd == ((w >> 11) & 31):  # mr
                if rd in regs:
                    regs[ra] = regs[rd]
                else:
                    regs.pop(ra, None)
                continue
            if op in LOAD_OPS or op in STORE_OPS or op in FLOAD_OPS or op in FSTORE_OPS:
                base = regs.get(ra) if ra else None
                if base and base[0] in ("data", "code"):
                    addr = base[1] + simm
                    if op in FLOAD_OPS:
                        if base[0] == "data":
                            val = _float_at(self.data, addr, op in (50, 51))
                        else:
                            val = _float_at(self.code, addr, op in (50, 51))
                        f.refs.append(Ref(o, "float", val, (base[0], addr)))
                    elif op in LOAD_OPS and base[0] == "data":
                        f.refs.append(Ref(o, "data_r", addr, LOAD_OPS[op][0]))
                        p = self.pointer_at(addr) if op == 32 else None
                        if p:
                            regs[rd] = p
                            continue
                    elif op in STORE_OPS or op in FSTORE_OPS:
                        f.refs.append(Ref(o, "data_w", addr, (STORE_OPS.get(op) or FSTORE_OPS[op])[0]))
                    elif op in LOAD_OPS:
                        f.refs.append(Ref(o, "code_r", addr, LOAD_OPS[op][0]))
                if op in LOAD_OPS:
                    regs.pop(rd, None)
                continue
            if op == 19 and ((w >> 1) & 0x3FF) == 528 and (w & 1):  # bctrl
                f.refs.append(Ref(o, "icall", None))
                for r in range(0, 13):
                    regs.pop(r, None)
                continue
            d = integer_dest(w)
            if d is not None:
                regs.pop(d, None)

    def _ref_pointer(self, f: Function, at: int, p: tuple, extra, addr_only: bool = False) -> None:
        kind, v = p
        if kind == "import":
            f.refs.append(Ref(at, "import", v))
        elif kind == "code":
            s = self.cstring_code(v, 400)
            if s is not None and len(s) >= 2:
                f.refs.append(Ref(at, "string", v, s))
            elif v in self.functions:
                f.refs.append(Ref(at, "funcptr", v))
            elif not addr_only:
                f.refs.append(Ref(at, "code_addr", v))
        else:
            if v in self.tvectors:
                f.refs.append(Ref(at, "funcptr", self.tvectors[v]))
                return
            s = self.cstring_data(v, 400)
            if s is not None and len(s) >= 3:
                f.refs.append(Ref(at, "string", ("data", v), s))
            f.refs.append(Ref(at, "data_addr", v))

    # ---------------------------------------------------------------- query
    def func_at(self, addr: int) -> Function | None:
        i = bisect.bisect_right(self.starts, addr) - 1
        if i < 0:
            return None
        return self.functions[self.starts[i]]

    def name_of(self, addr: int) -> str:
        if addr in self.names_user:
            return self.names_user[addr]
        f = self.functions.get(addr)
        if f and f.name:
            nm = f.name
            if nm.startswith("glue:"):
                return "glue:" + demangle.demangle(nm[5:])
            return demangle.demangle(nm) if "|" not in nm else nm
        return f"sub_{addr:06x}"

    def label(self, addr: int) -> str:
        f = self.func_at(addr)
        if f is None:
            return f"{addr:#x}"
        base = self.name_of(f.start)
        return base if addr == f.start else f"{base}+{addr - f.start:#x}"

    def callers(self, addr: int) -> list[tuple[int, int]]:
        out = []
        for f in self.functions.values():
            for r in f.refs:
                if r.kind in ("call", "tailcall", "funcptr") and r.target == addr:
                    out.append((f.start, r.at))
        return out

    def callees(self, addr: int) -> list[int]:
        f = self.functions[addr]
        return [r.target for r in f.refs if r.kind in ("call", "tailcall")]

    def import_name(self, idx: int) -> str:
        return self.c.imports[idx].name


def _li(w: int) -> int:
    li = w & 0x03FFFFFC
    if li & 0x02000000:
        li -= 0x04000000
    return li


def _float_at(buf: bytes, off: int, double: bool):
    try:
        if double:
            return struct.unpack_from(">d", buf, off)[0]
        return struct.unpack_from(">f", buf, off)[0]
    except struct.error:
        return None


def _cstring(buf: bytes, off: int, limit: int) -> str | None:
    if off < 0 or off >= len(buf):
        return None
    end = buf.find(b"\0", off, off + limit)
    if end <= off:
        return None
    raw = buf[off:end]
    if not all(32 <= b < 127 or b in (9, 10, 13) or b >= 0xA0 for b in raw):
        return None
    if sum(chr(b).isalpha() for b in raw) < max(1, len(raw) // 4):
        return None
    return raw.decode("mac_roman")


def load(path_: str) -> Analysis:
    st = os.stat(path_)
    key = f"{os.path.basename(path_)}-{st.st_size}-{int(st.st_mtime)}-{CACHE_VERSION}"
    names_file = os.path.join(HERE, "names", os.path.basename(path_).replace(".data", "") + ".txt")
    if os.path.exists(names_file):
        key += f"-{int(os.stat(names_file).st_mtime)}"
    cache = os.path.join(_cache_dir(), key + ".pickle")
    if os.path.exists(cache):
        with open(cache, "rb") as fh:
            return pickle.load(fh)
    a = Analysis(pef.load(path_))
    with open(cache, "wb") as fh:
        pickle.dump(a, fh)
    return a

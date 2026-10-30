"""Static data-flow scan of tp.exe for every reader of the coaster controller's first dword (the "track flags", controller +0).

Usage: python3 -I tools/ppc-analysis/controller_flag_readers.py [--check] [--verbose] [TP_EXE]       (TP_EXE defaults to $OPENTPW_TPW_EXE)

Dev tool for the evidence of SAVE-921 (docs/reverse/COAST-plan.md, section 24). It needs the dev-only `capstone` package and the identified tp.exe
(SHA-256 pinned, the same file the x86 oracle accepts, tools/ppc-analysis/oracle_x86/x86_harness.py); without either it prints a skip line and exits 0,
so CI, which has neither, passes it by. The executable is only read: nothing from it is written anywhere, and the report prints addresses and the
text of the instructions it names, nothing else.

The question: does any code of tp.exe observe bits 0x20..0x400 of the controller's first dword (the saver 0x437300 writes them to the Coasters block
header flag word, the loader 0x437B60 reads them back) other than the saver and the loader themselves? The scan answers it in four layers.

1. Seeds. A controller is the 0x14C-byte record the constructor 0x4368F0 allocates, and its pointer is obtained in exactly three ways: the thing-pool
   table at 0x790BD0 (indexed by the id the constructor hands out), the list head 0x790FE0 (next link at +0x18) and the "being built" global
   0x790FEC. The scan lists every place of the whole image that names those three addresses and requires them all to lie in the coaster module.
2. Flow. From the functions holding those places it follows pointer values to a fixpoint through registers, stack frames (exact esp offsets, out
   parameters, pointers to locals), direct calls (only the registers and stack arguments the callee reads, `ret N` and `add esp` pops, the returned
   eax), the controller's own fields, global holders and the fields of the other constant-size records the code allocates (nodes, cars, ...). A value
   is a token (kind, offset): ('C', k) is the controller + k, ('H48', k) a 0x48-byte record + k. Every memory operand whose base carries a controller
   token and whose byte range meets controller +0..3 is a reader; a second pass follows the loaded value (masks, compares, copies, returns) and
   says which bits it can observe. Over-approximation is the rule: the flow ignores branch conditions (except the null edge of a zero test), merges
   all callers of a function, and treats the register-and-stack state of a loop as one; what it cannot place is reported, not dropped.
3. Residue. Stores of a tracked pointer into memory the flow cannot type, calls it cannot follow and unresolved jumps are listed; every hit that is
   neither harmless nor listed in REVIEWED_* below is NEW and makes `--check` fail. The REVIEWED_* entries were read by hand in tp.exe.
4. Independent layer. Every `test [reg+0..3], imm` of the whole image whose immediate selects a track flag (67 of them) is listed with the last
   assignment of its base register, whether or not the flow reaches it.

What the scan does not do: follow pointers through arrays inside records (indexed element stores and loads), through records of unknown size, or
through the thing table 0x7A4610 (+0xD4 holds a car); it assumes the 0x10 bytes a pointer argument reaches in the caller's frame and non-negative
indexes. The report states these as UNTYPED stores; docs/reverse/COAST-plan.md section 24 gives the reading.
"""
from __future__ import annotations

import hashlib
import os
import struct
import sys
import time
from collections import defaultdict
from pathlib import Path

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE / 'oracle_x86'))
try:
    import capstone
    from capstone import x86 as cx
except ImportError:      # dev-only dependency
    capstone = None

TPW_SHA256 = '5472f97b5eb28f58b5a49f3a15f6f66adffa6b2625a623f503202c9696d1b805'
TPW_SIZE = 3_734_528

# The three ways a controller pointer is obtained (docs/reverse/COAST-plan.md, the constructor 0x4368F0 and the saver 0x437300).
SEED_ADDRESSES = {
    0x790BD0: 'thing pool table (index = thing id)',
    0x790FE0: 'controller list head (next link +0x18)',
    0x790FEC: 'controller being built / edited',
}
MODULE = (0x42F000, 0x44C000)       # the coaster module; the seed sites must all lie inside
SAVER, LOADER = 0x437300, 0x437B60
# Heap records the flow tells apart by their allocation size: the controller (0x14C bytes); every other constant size is a kind 'H<size>' (the 0x48-byte
# track node, built by 0x432C10, keeps the edited controller 0x790FEC in its +0x14, so a node leads back to its controller; the 0x104-byte cars, ...).
CONTROLLER_ALLOCATION = 0x14C
NON_POINTERS = ('S', 'G', 'P')      # token kinds that are not pointers to tracked heap records


def is_pointer(token):
    return token[0] not in NON_POINTERS


MASK32 = 0xFFFFFFFF
IMPORT_POPS: dict[str, int] = {}     # stack bytes popped by the imports the followed code calls (none is known: noted, assumed 0)
CALL_REGISTERS = ('ecx', 'edx')        # `this` and the second fastcall register (MSVC passes nothing else in registers)
ALLOCATOR = 0x45B9D0                # the heap allocator the constructor 0x4368F0 calls with the controller size
# The thing-id pool library, read by hand (it only stores or clears the pointer in the table it is given and never dereferences it): alloc(..., table, pointer, id) is
# arg 4 = table, arg 5 = pointer; free(..., table, id) stores a zero. Modelled instead of followed, because its one body serves every pool in the game.
POOL_ALLOC, POOL_FREE = 0x468760, 0x468810
UNKNOWN = '?'
REALIGNED = 0x100000                # esp origin after `and esp, -8`
ARG_SLOTS = 8                       # stack arguments passed on to a callee
FRAME_OBJECT = 0x10                 # bytes of a caller's frame a pointer argument is taken to reach (pointers held there are followed)
PARENT = {}
for _name, _subs in {'eax': ('ax', 'al', 'ah'), 'ecx': ('cx', 'cl', 'ch'), 'edx': ('dx', 'dl', 'dh'), 'ebx': ('bx', 'bl', 'bh'),
                     'esi': ('si',), 'edi': ('di',), 'ebp': ('bp',), 'esp': ('sp',)}.items():
    PARENT[_name] = _name
    for _sub in _subs:
        PARENT[_sub] = _name

# Token kinds: ('C', k) controller + k; ('H<size>', k) a heap record of that size (hex) + k; ('S', k) a pointer into the current frame (entry esp + k);
# ('G', a) the address a of a data object; ('P', argkey, k) a pointer into a caller's frame (argkey names the parameter it came in by) + k, seen
# from a callee. An offset k is an int (exact), ('R', lo) (at least lo: an indexed access, indexes never negative) or '?' (unknown).
EMPTY = frozenset()


def _shift(tokens, delta):
    """Pointer arithmetic by a known amount. Offsets are an int (exact), ('R', lo) (at least lo: indexed access) or '?'."""
    out = set()
    for token in tokens:
        if len(token) == 2 and isinstance(token[1], int):
            out.add((token[0], token[1] + delta))
        elif len(token) == 2 and isinstance(token[1], tuple):
            out.add((token[0], ('R', token[1][1] + delta)))
        elif len(token) == 3 and isinstance(token[2], int):
            out.add((token[0], token[1], token[2] + delta))
        else:
            out.add(token)
    return frozenset(out)


def _range(tokens):
    """The result of adding a non-negative unknown amount (an index): the same object, at least the old offset."""
    out = set()
    for token in tokens:
        if len(token) == 2 and isinstance(token[1], int):
            out.add((token[0], ('R', token[1])))
        elif len(token) == 3 and isinstance(token[2], int):
            out.add((token[0], token[1], UNKNOWN))
        else:
            out.add(token)
    return frozenset(out)


def _blur(tokens):
    """The result of bit operations or other arithmetic with an unknown amount: the same object, unknown offset."""
    out = set()
    for token in tokens:
        if len(token) == 2:
            out.add((token[0], UNKNOWN))
        elif len(token) == 3:
            out.add((token[0], token[1], UNKNOWN))
        else:
            out.add(token)
    return frozenset(out)


class Image:
    """The identified tp.exe laid out flat (x86_pe) with an on-demand instruction decoder."""

    def __init__(self, raw: bytes):
        import x86_pe
        self.pe = x86_pe.parse(raw)
        self.base = self.pe.preferred_base
        self.flat = self.pe.layout()
        self.imports = {self.base + imp.slot_rva: imp.key for imp in self.pe.imports}
        self.text = [(self.base + s.rva, self.base + s.rva + s.virtual_size) for s in self.pe.sections if s.executable]
        self.cs = capstone.Cs(capstone.CS_ARCH_X86, capstone.CS_MODE_32)
        self.cs.detail = True
        self.cache: dict[int, object] = {}

    def in_text(self, va):
        return any(lo <= va < hi for lo, hi in self.text)

    def in_data(self, va):
        return any(self.base + s.rva <= va < self.base + s.rva + max(s.virtual_size, s.raw_size) for s in self.pe.sections if not s.executable)

    def u32(self, va):
        return struct.unpack_from('<I', self.flat, va - self.base)[0]

    def decode(self, va):
        if va in self.cache:
            return self.cache[va]
        offset = va - self.base
        insn = None
        if 0 <= offset < len(self.flat) - 16 and self.in_text(va):
            for found in self.cs.disasm(bytes(self.flat[offset:offset + 16]), va, 1):
                insn = found
        self.cache[va] = insn
        return insn


class Function:
    """Flow graph of one function found by recursive descent from its entry (calls are not followed)."""

    def __init__(self, image: Image, entry: int):
        self.entry = entry
        self.insns: dict[int, object] = {}
        self.succ: dict[int, list[int]] = {}
        self.unresolved: list[tuple[int, str]] = []
        self.switches: dict[int, list[int]] = {}
        self.pop = None            # `ret N`
        self.pops = set()
        work = [entry]
        while work:
            va = work.pop()
            while va not in self.insns:
                insn = image.decode(va)
                if insn is None:
                    self.unresolved.append((va, 'undecodable'))
                    break
                self.insns[va] = insn
                nxt = va + insn.size
                if insn.mnemonic == 'ret':
                    count = insn.operands[0].imm if insn.operands else 0
                    self.pops.add(count)
                    self.succ[va] = []
                    break
                if insn.mnemonic == 'jmp':
                    op = insn.operands[0]
                    if op.type == cx.X86_OP_IMM:
                        self.succ[va] = [op.imm]
                        work.append(op.imm)
                    elif op.type == cx.X86_OP_MEM and op.mem.base == 0 and op.mem.index and op.mem.scale == 4:
                        table = self._table(image, insn, op.mem.disp)
                        self.switches[va] = table
                        self.succ[va] = list(table)
                        work.extend(table)
                    else:
                        self.succ[va] = []
                        self.unresolved.append((va, 'indirect jmp ' + insn.op_str))
                    break
                if insn.mnemonic.startswith('j') and insn.operands and insn.operands[0].type == cx.X86_OP_IMM:
                    self.succ[va] = [nxt, insn.operands[0].imm]
                    work.append(insn.operands[0].imm)
                    va = nxt
                    continue
                if insn.mnemonic in ('int3', 'ud2', 'hlt'):
                    self.succ[va] = []
                    break
                self.succ[va] = [nxt]
                va = nxt
        if len(self.pops) == 1:
            self.pop = next(iter(self.pops))
        elif self.pops:
            self.pop = max(self.pops)
        self.order = sorted(self.insns)

    def covers(self, site):
        """Whether `site` lies inside an instruction of this function."""
        import bisect
        index = bisect.bisect_right(self.order, site) - 1
        return index >= 0 and site < self.order[index] + self.insns[self.order[index]].size

    def _table(self, image, insn, table):
        """The targets of a `jmp [reg*4 + table]`: the dwords that point into this image's code near the jump."""
        out = []
        for index in range(512):
            try:
                target = image.u32(table + 4 * index)
            except struct.error:
                break
            if not (image.in_text(target) and abs(target - insn.address) < 0x6000):
                break
            out.append(target)
        return out


class State:
    __slots__ = ('regs', 'stack', 'esp', 'undef', 'ztest')

    def __init__(self, regs=None, stack=None, esp=0, undef=None, ztest=None):
        self.regs = regs if regs is not None else {}
        self.stack = stack if stack is not None else {}
        self.esp = esp
        self.undef = undef        # probe runs only: registers no instruction has written yet
        self.ztest = ztest        # register a `test r, r` / `cmp r, 0` just compared with zero (null edges lose the pointer)

    def copy(self):
        return State(dict(self.regs), dict(self.stack), self.esp, None if self.undef is None else set(self.undef), self.ztest)

    def join(self, other):
        """Merge `other` in; True when this state grew."""
        changed = False
        for reg, tokens in other.regs.items():
            old = self.regs.get(reg, EMPTY)
            if not tokens <= old:
                self.regs[reg] = old | tokens
                changed = True
        for key, tokens in other.stack.items():
            old = self.stack.get(key, EMPTY)
            if not tokens <= old:
                self.stack[key] = old | tokens
                changed = True
        if self.esp != other.esp and self.esp is not None:
            self.esp = None
            changed = True
        if self.undef is not None and other.undef is not None and not other.undef <= self.undef:
            self.undef |= other.undef
            changed = True
        if self.ztest != other.ztest and self.ztest is not None:
            self.ztest = None
            changed = True
        return changed


class Event:
    """One memory access through a controller pointer that meets controller +0..3 (or cannot be placed)."""

    def __init__(self, address, text, access, lo, hi, why):
        self.address, self.text, self.access, self.lo, self.hi, self.why = address, text, access, lo, hi, why
        self.functions = set()
        self.reaching = set()


class Analysis:
    def __init__(self, image: Image):
        self.image = image
        self.functions: dict[int, Function] = {}
        self.entry_regs: dict[int, dict] = defaultdict(dict)
        self.entry_args: dict[int, dict] = defaultdict(dict)
        self.ret_regs: dict[int, dict] = defaultdict(dict)
        self.analyzed: set[int] = set()
        # holders: what the flow has seen stored where, as pointer tokens ('C' controller, 'N' node record) with their offsets
        self.abs_holders: dict[int, frozenset] = {0x790FE0: frozenset({('C', 0)}), 0x790FEC: frozenset({('C', 0)})}
        self.table_holders: dict[int, frozenset] = {0x790BD0: frozenset({('C', 0)})}
        self.fields: dict[tuple, frozenset] = {}          # (kind, offset) of a tracked object -> tokens stored there
        self.alloc_sites: dict[int, str] = {}
        self.pcontents: dict[tuple, frozenset] = {}      # (callee, argkey) -> tracked pointers held in the caller frame a parameter points into
        self.out_stores: dict[tuple, dict] = {}         # (function, argkey) -> {offset: tokens}: pointers it stores through a frame pointer parameter
        self.events: dict[tuple, Event] = {}
        self.holder_stores: dict[int, str] = {}
        self.escapes: dict[tuple, str] = {}
        self.notes: dict[tuple, str] = {}
        self.changed = False
        self.verbose = False
        self.current = None
        self.call_sites: dict[int, list[int]] = defaultdict(list)
        self.starts: list[int] = []
        self.data_refs: dict[int, list[int]] = defaultdict(list)
        self.input_cache: dict[int, tuple] = {}
        self.probing = False

    # --- infrastructure -------------------------------------------------------------------------------------------------------------
    def func(self, entry):
        if entry not in self.functions:
            self.functions[entry] = Function(self.image, entry)
        return self.functions[entry]

    def add_entry(self, callee, regs, args, pcont=None):
        changed = False
        for argkey, tokens in (pcont or {}).items():
            old = self.pcontents.get((callee, argkey), EMPTY)
            if not tokens <= old:
                self.pcontents[(callee, argkey)] = old | tokens
                changed = True
        for reg, tokens in regs.items():
            old = self.entry_regs[callee].get(reg, EMPTY)
            if not tokens <= old:
                self.entry_regs[callee][reg] = old | tokens
                changed = True
        for index, tokens in args.items():
            old = self.entry_args[callee].get(index, EMPTY)
            if not tokens <= old:
                self.entry_args[callee][index] = old | tokens
                changed = True
        if callee not in self.analyzed:
            self.analyzed.add(callee)
            changed = True
        if changed:
            self.changed = True

    # --- operand evaluation ----------------------------------------------------------------------------------------------------------
    def reg_tokens(self, st, name):
        name = PARENT.get(name, name)
        if name == 'esp':
            return frozenset({('S', st.esp)}) if st.esp is not None else EMPTY
        return st.regs.get(name, EMPTY)

    def set_reg(self, st, name, tokens):
        name = PARENT.get(name, name)
        if name == 'esp':
            st.esp = None
            for token in tokens:
                if token[0] == 'S' and isinstance(token[1], int):
                    st.esp = token[1]
            return
        if tokens:
            st.regs[name] = frozenset(tokens)
        else:
            st.regs.pop(name, None)

    def global_token(self, imm):
        """An immediate that is the address of a data object: pointer to it, so stores through it reach the holder tables."""
        if self.image.in_data(imm):
            return frozenset({('G', imm)})
        return EMPTY

    def load(self, insn, op, st):
        """The tokens loaded from memory operand `op` (and nothing else: events are recorded in `access`)."""
        mem = op.mem
        if insn.reg_name(mem.segment) in ('fs', 'gs'):
            return EMPTY
        base = insn.reg_name(mem.base) if mem.base else None
        index = bool(mem.index)
        out = set()
        if base is None:
            if mem.disp in self.table_holders:
                out |= self.table_holders[mem.disp]
            if not index and mem.disp in self.abs_holders:
                out |= self.abs_holders[mem.disp]
            return frozenset(out)
        for token in self.reg_tokens(st, base):
            kind = token[0]
            if kind == 'S':
                if isinstance(token[1], int) and not index and st.esp is not None:
                    out |= st.stack.get(token[1] + mem.disp, EMPTY)
            elif kind not in NON_POINTERS:
                if isinstance(token[1], int) and not index:
                    out |= self.fields.get((kind, token[1] + mem.disp), EMPTY)
                else:
                    self.notes[(insn.address, 'indexed-load')] = f'indexed load through a {kind} pointer: elements of arrays inside records are not followed'
            elif kind == 'G':
                a = token[1] + mem.disp if isinstance(token[1], int) else None
                if token[1] in self.table_holders:
                    out |= self.table_holders[token[1]]
                if a is not None and a in self.abs_holders:
                    out |= self.abs_holders[a]
                if a is not None and index and a in self.table_holders:
                    out |= self.table_holders[a]
            elif kind == 'P':
                out |= self.pcontents.get((self.current, token[1]), EMPTY)
        return frozenset(out)

    def value(self, insn, op, st):
        if op.type == cx.X86_OP_REG:
            return self.reg_tokens(st, insn.reg_name(op.reg))
        if op.type == cx.X86_OP_IMM:
            return self.global_token(op.imm & MASK32)
        if op.type == cx.X86_OP_MEM and op.size == 4:
            return self.load(insn, op, st)
        return EMPTY

    def hold(self, table, key, tokens):
        """Record that `tokens` were stored at `key` of a holder table; stores made outside the coaster module are listed, never followed."""
        if self.current is not None and not MODULE[0] <= self.current < MODULE[1]:
            return
        old = table.get(key, EMPTY)
        if not tokens <= old:
            table[key] = old | tokens
            self.changed = True

    def record_out_store(self, function, argkey, rel, tokens):
        table = self.out_stores.setdefault((function, argkey), {})
        old = table.get(rel, EMPTY)
        if not tokens <= old:
            table[rel] = old | tokens
            self.changed = True

    def store(self, insn, op, st, tokens):
        mem = op.mem
        if insn.reg_name(mem.segment) in ('fs', 'gs'):
            return
        base = insn.reg_name(mem.base) if mem.base else None
        index = bool(mem.index)
        pointers = frozenset(t for t in tokens if is_pointer(t))
        text = f'{insn.mnemonic} {insn.op_str} [{"|".join(sorted({t[0] for t in pointers}))}]'
        if base is None:
            if pointers:
                self.hold(self.table_holders if index else self.abs_holders, mem.disp, pointers)
                self.holder_stores.setdefault(insn.address, f'{text} (absolute holder 0x{mem.disp:X})')
            return
        base_tokens = self.reg_tokens(st, base)
        if not base_tokens:
            if pointers:
                self.holder_stores.setdefault(insn.address, f'{text} (object field +0x{mem.disp & MASK32:X}, base unknown)')
            return
        for token in base_tokens:
            kind = token[0]
            if kind == 'S':
                if isinstance(token[1], int) and not index and st.esp is not None:
                    key = token[1] + mem.disp
                    st.stack[key] = frozenset(tokens) if len(base_tokens) == 1 else st.stack.get(key, EMPTY) | tokens
            elif kind not in NON_POINTERS:
                if pointers:
                    if isinstance(token[1], int) and not index:
                        self.hold(self.fields, (kind, token[1] + mem.disp), pointers)
                        self.holder_stores.setdefault(insn.address, f'{text} ({kind} field +0x{token[1] + mem.disp:X})')
                    else:
                        self.holder_stores.setdefault(insn.address, f'{text} ({kind} array element, not followed)')
            elif kind == 'G':
                if pointers:
                    a = token[1] + mem.disp if isinstance(token[1], int) else None
                    if index or a is None:
                        self.hold(self.table_holders, token[1] if isinstance(token[1], int) else mem.disp, pointers)
                    else:
                        self.hold(self.abs_holders, a, pointers)
                    self.holder_stores.setdefault(insn.address, f'{text} (global holder)')
            elif kind == 'P':
                if pointers:
                    self.record_out_store(self.current, token[1], token[2] + mem.disp if isinstance(token[2], int) and not index else UNKNOWN, pointers)

    # --- events ----------------------------------------------------------------------------------------------------------------------
    def access(self, insn, op, st, entry):
        """Record the memory operand if it can address controller +0..3 (or an unplaceable controller access)."""
        mem = op.mem
        if not mem.base or insn.reg_name(mem.segment) in ('fs', 'gs'):
            return
        for token in self.reg_tokens(st, insn.reg_name(mem.base)):
            if token[0] != 'C':
                continue
            size = op.size or 4
            string = insn.mnemonic.startswith(('rep', 'movs', 'cmps', 'lods', 'scas', 'stos'))
            offset = token[1]
            if isinstance(offset, int) and not mem.index:
                lo = offset + mem.disp
                hi = None if string else lo + size
                if not ((hi is None and lo < 4) or (hi is not None and lo < 4 and hi > 0)):
                    continue
                why = 'string operation from the controller' if string else ''
            else:
                low = offset if isinstance(offset, int) else offset[1] if isinstance(offset, tuple) else 0     # '?' only comes from widening: forward
                if low + mem.disp >= 4:
                    continue            # indexes and offsets reach forward only: past +3
                lo, hi = None, None
                why = 'unplaceable offset'
                if mem.disp > 0x400 and not string:
                    continue
            acc = ('r' if op.access & 1 else '') + ('w' if op.access & 2 else '')
            key = (insn.address, lo, acc)
            event = self.events.get(key)
            if event is None:
                event = self.events[key] = Event(insn.address, f'{insn.mnemonic} {insn.op_str}', acc, lo, hi, why)
                self.changed = True
            if entry not in event.functions:
                event.functions.add(entry)
                self.changed = True

    # --- transfer function -----------------------------------------------------------------------------------------------------------
    def step(self, entry, insn, st):
        m = insn.mnemonic
        ops = insn.operands
        zero = None
        if m == 'test' and ops[0].type == ops[1].type == cx.X86_OP_REG and ops[0].reg == ops[1].reg and ops[0].size == 4:
            zero = PARENT[insn.reg_name(ops[0].reg)]
        elif m == 'cmp' and ops[0].type == cx.X86_OP_REG and ops[0].size == 4 and ops[1].type == cx.X86_OP_IMM and ops[1].imm == 0:
            zero = PARENT[insn.reg_name(ops[0].reg)]
        if zero is not None or not (m.startswith('j') or m in FLAG_NEUTRAL):
            st.ztest = zero
        elif st.ztest and m in FLAG_NEUTRAL:
            try:
                if st.ztest in {PARENT.get(insn.reg_name(r)) for r in insn.regs_access()[1]}:
                    st.ztest = None
            except capstone.CsError:
                st.ztest = None
        if m != 'lea' and not m.startswith('nop'):
            for op in ops:
                if op.type == cx.X86_OP_MEM:
                    self.access(insn, op, st, entry)
        if m not in ('mov', 'push', 'call', 'xchg', 'pop') and st.esp is not None:
            for op in ops:
                # a stack slot written by anything but a plain move, or read as a float by the x87, no longer holds a pointer
                if op.type == cx.X86_OP_MEM and (op.access & 2 or m.startswith('f')) and op.mem.base and not op.mem.index:
                    for token in self.reg_tokens(st, insn.reg_name(op.mem.base)):
                        if token[0] == 'S' and isinstance(token[1], int):
                            st.stack.pop(token[1] + op.mem.disp, None)
        if m == 'call':
            return self.do_call(entry, insn, st)
        if m == 'ret':
            for reg in ('eax',):      # the compiler's return register; ecx and edx are scratch, ebx/esi/edi/ebp are restored
                tokens = st.regs.get(reg, EMPTY)
                if tokens:
                    old = self.ret_regs[entry].get(reg, EMPTY)
                    if not tokens <= old:
                        self.ret_regs[entry][reg] = old | tokens
                        self.changed = True
            return
        if m == 'push':
            value = self.value(insn, ops[0], st) if ops[0].size == 4 or ops[0].type == cx.X86_OP_IMM else EMPTY
            if st.esp is not None:
                st.esp -= 4
                st.stack[st.esp] = value
            return
        if m == 'pop':
            value = EMPTY
            if st.esp is not None:
                value = st.stack.get(st.esp, EMPTY)
                st.esp += 4
            if ops[0].type == cx.X86_OP_REG:
                self.set_reg(st, insn.reg_name(ops[0].reg), value)
            return
        if m == 'leave':
            frame = [t for t in self.reg_tokens(st, 'ebp') if t[0] == 'S']
            st.esp = None
            if len(frame) == 1 and isinstance(frame[0][1], int):
                st.esp = frame[0][1] + 4
                self.set_reg(st, 'ebp', st.stack.get(frame[0][1], EMPTY))
            return
        if m == 'mov':
            dst, src = ops
            value = self.value(insn, src, st)
            if dst.type == cx.X86_OP_REG:
                if dst.size == 4:
                    self.set_reg(st, insn.reg_name(dst.reg), value)
            elif dst.size == 4:
                self.store(insn, dst, st, value)
            return
        if m == 'lea':
            dst, src = ops
            mem = src.mem
            base = insn.reg_name(mem.base) if mem.base else None
            if base is None:
                tokens = self.global_token(mem.disp & MASK32)
            else:
                tokens = _shift(self.reg_tokens(st, base), mem.disp)
                if mem.index:
                    tokens = _range(tokens)
            self.set_reg(st, insn.reg_name(dst.reg), tokens)
            return
        if m in ('add', 'sub') and ops[0].type == cx.X86_OP_REG and ops[0].size == 4:
            name = PARENT[insn.reg_name(ops[0].reg)]
            if m == 'sub' and ops[1].type == cx.X86_OP_REG and PARENT[insn.reg_name(ops[1].reg)] == name:
                self.set_reg(st, name, EMPTY)
            elif ops[1].type == cx.X86_OP_IMM:
                delta = ops[1].imm if m == 'add' else -ops[1].imm
                if name == 'esp':
                    if st.esp is not None:
                        st.esp += delta
                else:
                    self.set_reg(st, name, _shift(self.reg_tokens(st, name), delta))
            else:
                other = self.value(insn, ops[1], st)
                tokens = self.reg_tokens(st, name)
                if name == 'esp':
                    st.esp = None
                elif m == 'sub' and other:
                    self.set_reg(st, name, EMPTY)
                else:
                    self.set_reg(st, name, _range(tokens | other) if m == 'add' else _blur(tokens | other))
            return
        if m in ('and', 'or', 'xor', 'adc', 'sbb') and ops[0].type == cx.X86_OP_REG and ops[0].size == 4:
            name = PARENT[insn.reg_name(ops[0].reg)]
            if m in ('xor', 'sbb') and ops[1].type == cx.X86_OP_REG and PARENT[insn.reg_name(ops[1].reg)] == name:
                self.set_reg(st, name, EMPTY)
            elif name == 'esp':
                # `and esp, -8`: a realigned frame; its offsets live in their own key space (entry-relative arguments stay reachable through ebp)
                st.esp = REALIGNED if m == 'and' and ops[1].type == cx.X86_OP_IMM and ops[1].imm & 0x80000000 else None
            else:
                other = self.value(insn, ops[1], st)
                self.set_reg(st, name, _blur(self.reg_tokens(st, name) | other))
            return
        if m in ('inc', 'dec') and ops[0].type == cx.X86_OP_REG and ops[0].size == 4:
            name = PARENT[insn.reg_name(ops[0].reg)]
            if name == 'esp':
                if st.esp is not None:
                    st.esp += 1 if m == 'inc' else -1
            else:
                self.set_reg(st, name, _shift(self.reg_tokens(st, name), 1 if m == 'inc' else -1))
            return
        if m == 'xchg':
            a, b = ops
            if a.type == b.type == cx.X86_OP_REG and a.size == 4:
                ta, tb = self.reg_tokens(st, insn.reg_name(a.reg)), self.reg_tokens(st, insn.reg_name(b.reg))
                self.set_reg(st, insn.reg_name(a.reg), tb)
                self.set_reg(st, insn.reg_name(b.reg), ta)
            else:
                for op in ops:
                    if op.type == cx.X86_OP_REG:
                        self.set_reg(st, insn.reg_name(op.reg), EMPTY)
            return
        if m.startswith('cmov'):
            if ops[0].type == cx.X86_OP_REG and ops[0].size == 4:
                name = insn.reg_name(ops[0].reg)
                self.set_reg(st, name, self.reg_tokens(st, name) | self.value(insn, ops[1], st))
            return
        if m in ('cmp', 'test', 'jmp') or m.startswith('j') or m.startswith('f') or m in ('nop', 'int3', 'wait', 'cld', 'std'):
            return
        if m.startswith(('movs', 'rep movs', 'repne', 'repe', 'rep ', 'stos', 'lods', 'scas', 'cmps')) and m not in ('movsx', 'movsxd'):
            for reg in ('esi', 'edi'):
                self.set_reg(st, reg, _blur(self.reg_tokens(st, reg)))
            st.regs.pop('ecx', None)
            if 'lods' in m:
                st.regs.pop('eax', None)
            return
        # everything else: the registers it writes lose their pointer tokens (movzx, shifts, imul, setcc, cdq, ...)
        try:
            written = insn.regs_access()[1]
        except capstone.CsError:
            written = ()
        for reg in written:
            name = PARENT.get(insn.reg_name(reg))
            if name == 'esp':
                st.esp = None
            elif name:
                st.regs.pop(name, None)

    def argument_count(self, insn, target):
        """How many stack arguments the call pushes: `ret N` of the callee, else the `add esp, N` that follows the call (None when unknown)."""
        callee = self.func(target)
        if callee.pop:
            return callee.pop // 4
        nxt = self.image.decode(insn.address + insn.size)
        if nxt is not None and nxt.mnemonic == 'add' and nxt.operands[0].type == cx.X86_OP_REG and nxt.operands[0].reg == cx.X86_REG_ESP \
                and nxt.operands[1].type == cx.X86_OP_IMM:
            return (nxt.operands[1].imm & MASK32) // 4
        return None

    def apply_out_stores(self, entry, callee, pmap, st):
        """What the callee stored through its frame-pointer parameters lands in this frame (or goes on to this function's own caller)."""
        for (function, argkey), table in list(self.out_stores.items()):
            if function != callee:
                continue
            for rel, tokens in list(table.items()):
                for token in pmap.get(argkey, ()):
                    if token[0] == 'S' and isinstance(token[1], int) and isinstance(rel, int) and st.esp is not None:
                        key = token[1] + rel
                        st.stack[key] = st.stack.get(key, EMPTY) | tokens
                    elif token[0] == 'P':
                        self.record_out_store(entry, token[1], token[2] + rel if isinstance(token[2], int) and isinstance(rel, int) else UNKNOWN, tokens)

    def is_import_thunk(self, entry):
        first = self.func(entry).insns.get(entry)
        return first is not None and first.mnemonic == 'jmp' and first.operands[0].type == cx.X86_OP_MEM

    def do_call(self, entry, insn, st):
        op = insn.operands[0]
        regs, args, pmap, pcont = {}, {}, {}, {}

        def convert(argkey, tokens):
            """Frame pointers become parameter-relative ('P', argkey, 0); pmap remembers what they pointed at in this frame."""
            out = set()
            for token in tokens:
                if token[0] in ('S', 'P'):
                    out.add(('P', argkey, 0))
                    pmap.setdefault(argkey, set()).add(token)
                    if token[0] == 'S' and isinstance(token[1], int):
                        held = frozenset(t for key, ts in st.stack.items() if token[1] <= key < token[1] + FRAME_OBJECT for t in ts if is_pointer(t))
                    else:
                        held = self.pcontents.get((entry, token[1]), EMPTY) if token[0] == 'P' else EMPTY
                    if held:
                        pcont[argkey] = pcont.get(argkey, EMPTY) | held
                else:
                    out.add(token)
            return frozenset(out)

        for reg in CALL_REGISTERS:
            tokens = st.regs.get(reg, EMPTY)
            if tokens:
                regs[reg] = convert(('r', reg), tokens)
        if st.esp is not None:
            for index in range(ARG_SLOTS):
                tokens = st.stack.get(st.esp + 4 * index, EMPTY)
                if tokens:
                    args[index] = convert(('a', index), tokens)
        direct = op.type == cx.X86_OP_IMM
        if direct and args:
            count = self.argument_count(insn, op.imm)
            if count is not None:
                args = {index: tokens for index, tokens in args.items() if index < count}
        if direct and not self.probing and (regs or args):
            callee_entry = op.imm
            if self.func(callee_entry).insns and not self.is_import_thunk(callee_entry):
                live, touched = self.inputs(callee_entry)
                regs = {reg: tokens for reg, tokens in regs.items() if reg in live}
                args = {index: tokens for index, tokens in args.items() if index in touched}
        tainted = any(is_pointer(t) or (t[0] == 'P' and pcont) for ts in list(regs.values()) + list(args.values()) for t in ts)
        result = {}
        pop = 0
        if op.type != cx.X86_OP_IMM:
            if tainted:
                self.escapes[(insn.address, 'indirect')] = f'call {insn.op_str} with a controller or node pointer in a register or argument'
            st.regs.pop('eax', None)
            st.regs.pop('ecx', None)
            st.regs.pop('edx', None)
            return
        target = op.imm
        if target in (POOL_ALLOC, POOL_FREE) and not self.probing:
            if target == POOL_ALLOC:
                for table in args.get(4, EMPTY):
                    if table[0] == 'G':
                        self.hold(self.table_holders, table[1], frozenset(t for t in args.get(5, EMPTY) if is_pointer(t)))
            for reg in ('eax', 'ecx', 'edx'):
                st.regs.pop(reg, None)
            return
        callee = self.func(target)
        first = callee.insns.get(target)
        external = first is None
        if first is not None and first.mnemonic == 'jmp' and first.operands and first.operands[0].type == cx.X86_OP_MEM \
                and first.operands[0].mem.base == 0 and not first.operands[0].mem.index:
            external = self.image.imports.get(first.operands[0].mem.disp, 'indirect jump')
        if tainted:
            if external:
                self.escapes[(insn.address, target)] = f'call 0x{target:X} ({external if isinstance(external, str) else "undecodable"}) with a controller or node pointer'
            else:
                self.add_entry(target, regs, args, pcont)
        if direct and not external and not self.probing:
            self.apply_out_stores(entry, target, pmap, st)
        if external:
            pop = IMPORT_POPS.get(external, 0) if isinstance(external, str) else 0
            if isinstance(external, str) and external not in IMPORT_POPS:
                self.notes[(insn.address, 'import')] = f'call to import {external}: stack pop assumed 0'
        elif callee.pop is not None:
            pop = callee.pop
        for reg in ('eax', 'ecx', 'edx'):
            st.regs.pop(reg, None)
        if not external:
            tokens = self.ret_regs.get(target, {}).get('eax', EMPTY)
            if tokens:
                st.regs['eax'] = tokens
        if insn.address in self.alloc_sites:
            st.regs['eax'] = frozenset({(self.alloc_sites[insn.address], 0)})
        if st.esp is not None:
            st.esp += pop

    # --- one function ----------------------------------------------------------------------------------------------------------------
    def run(self, entry, probe=False):
        outer, self.current = self.current, entry
        try:
            self._run(entry, probe)
        finally:
            self.current = outer

    def _run(self, entry, probe):
        f = self.func(entry)
        if probe:
            regs, stack, undef = {}, {}, set(CALL_REGISTERS)
        else:
            regs = dict(self.entry_regs.get(entry, {}))
            stack = {4 + 4 * index: tokens for index, tokens in self.entry_args.get(entry, {}).items()}
            undef = None
        states = {entry: State(regs, stack, 0, undef)}
        work = [entry]
        while work:
            va = work.pop()
            insn = f.insns.get(va)
            if insn is None:
                continue
            before = states[va]
            st = before.copy()
            if probe:
                self.probe_reads(entry, insn, st)
            self.step(entry, insn, st)
            if st.esp is None and before.esp is not None and insn.mnemonic != 'ret':
                self.notes[('esp', entry)] = f'esp offset lost in 0x{entry:X} at 0x{va:X}: {insn.mnemonic} {insn.op_str}'
            for index, nxt in enumerate(f.succ.get(va, [])):
                if nxt not in f.insns:
                    continue
                edge = st
                if st.ztest and insn.mnemonic in ('je', 'jz', 'jne', 'jnz') and len(f.succ[va]) == 2:
                    # succ order is [fall through, taken]; the zero edge is the taken one of je/jz and the fall through of jne/jnz
                    zero_edge = 1 if insn.mnemonic in ('je', 'jz') else 0
                    tested = st.regs.get(st.ztest, EMPTY)
                    if index == zero_edge and tested:
                        if all(t[0] == 'S' for t in tested):
                            continue        # the address of a local is never null: `lea r, [esp+k]; test r, r` has no zero edge
                        edge = st.copy()
                        edge.regs.pop(st.ztest, None)
                if nxt in states:
                    if states[nxt].join(self.cap(edge)):
                        work.append(nxt)
                else:
                    states[nxt] = self.cap(edge).copy()
                    work.append(nxt)

    # --- what a callee reads of its inputs ---------------------------------------------------------------------------------------------
    def inputs(self, entry):
        """(registers among CALL_REGISTERS the function reads before writing them, indexes of the stack arguments it touches)."""
        if entry not in self.input_cache:
            self.input_cache[entry] = (set(), set())
            saved = (self.events, self.holder_stores, self.escapes, self.notes, self.changed, self.analysis_state())
            self.events, self.holder_stores, self.escapes, self.notes = {}, {}, {}, {}
            self.probing = True
            try:
                self.run(entry, probe=True)
            finally:
                self.probing = False
                self.events, self.holder_stores, self.escapes, self.notes, self.changed, state = saved
                self.restore_state(state)
        return self.input_cache[entry]

    def analysis_state(self):
        return (dict(self.abs_holders), dict(self.table_holders), dict(self.fields), dict(self.pcontents), {k: dict(v) for k, v in self.out_stores.items()})

    def restore_state(self, state):
        self.abs_holders, self.table_holders, self.fields, self.pcontents, self.out_stores = state

    def probe_reads(self, entry, insn, st):
        live, args = self.input_cache[entry]
        m = insn.mnemonic
        try:
            read = insn.regs_access()[0]
        except capstone.CsError:
            read = ()
        names = {PARENT.get(insn.reg_name(r)) for r in read}
        ops = insn.operands
        if m in ('xor', 'sub', 'sbb') and len(ops) == 2 and ops[0].type == ops[1].type == cx.X86_OP_REG and ops[0].reg == ops[1].reg:
            names = set()
        if m == 'push' and ops and ops[0].type == cx.X86_OP_REG and PARENT.get(insn.reg_name(ops[0].reg)) in ('ebx', 'esi', 'edi', 'ebp'):
            names = set()
        for name in names & st.undef:
            live.add(name)
        for op in ops:
            if op.type == cx.X86_OP_MEM and op.mem.base:
                for token in self.reg_tokens(st, insn.reg_name(op.mem.base)):
                    if token[0] == 'S' and isinstance(token[1], int):
                        key = token[1] + op.mem.disp
                        if 4 <= key < 4 + 4 * ARG_SLOTS:
                            args.add((key - 4) // 4)
                        elif op.mem.index and 4 <= token[1] < 4 + 4 * ARG_SLOTS:
                            args.update(range((token[1] - 4) // 4, ARG_SLOTS))
        if m == 'lea' and ops[1].type == cx.X86_OP_MEM and ops[1].mem.base:
            for token in self.reg_tokens(st, insn.reg_name(ops[1].mem.base)):
                if token[0] == 'S' and isinstance(token[1], int) and 4 <= token[1] + ops[1].mem.disp < 4 + 4 * ARG_SLOTS:
                    args.update(range((token[1] + ops[1].mem.disp - 4) // 4, ARG_SLOTS))
        try:
            written = insn.regs_access()[1]
        except capstone.CsError:
            written = ()
        for r in written:
            st.undef.discard(PARENT.get(insn.reg_name(r)))
        if m == 'call':
            st.undef.discard('ecx')
            st.undef.discard('edx')

    @staticmethod
    def cap(st):
        """Widen a state so loops cannot grow a register's token set without bound: more than six offsets of one object become 'unknown'."""
        for table in (st.regs, st.stack):
            for key, tokens in list(table.items()):
                groups = defaultdict(list)
                for t in tokens:
                    if len(t) == 2:
                        groups[(t[0], 'int' if isinstance(t[1], int) else 'range' if isinstance(t[1], tuple) else None)].append(t)
                    elif len(t) == 3 and isinstance(t[2], int):
                        groups[(t[0], t[1], 'int')].append(t)
                for head, same in groups.items():
                    if head[-1] is not None and len(same) > 6:
                        unknown = (head[0], UNKNOWN) if len(head) == 2 else (head[0], head[1], UNKNOWN)
                        tokens = frozenset(t for t in tokens if t not in same) | {unknown}
                        table[key] = tokens
        return st

    # --- roots -----------------------------------------------------------------------------------------------------------------------
    def call_targets(self):
        """Every `call rel32` target in the code (byte scan: over-approximates, which only adds candidate function starts)."""
        found = set()
        flat = self.image.flat
        for lo, hi in self.image.text:
            at = lo - self.image.base
            end = hi - self.image.base
            while True:
                at = flat.find(b'\xE8', at, end)
                if at < 0:
                    break
                if at + 5 <= end:
                    target = (self.image.base + at + 5 + struct.unpack_from('<i', flat, at + 1)[0]) & MASK32
                    if self.image.in_text(target):
                        found.add(target)
                        self.call_sites[target].append(self.image.base + at)
                at += 1
        return sorted(found)

    def enclosing(self, starts, site):
        """The function containing `site`: the lowest candidate start whose flow graph covers it (call targets the byte scan finds
        inside instructions are later starts whose graphs are suffixes of the real function's)."""
        import bisect
        index = bisect.bisect_right(starts, site)
        best = None
        for start in reversed(starts[max(0, index - 80):index]):
            f = self.func(start)
            if f.covers(site):
                best = start
        return best

    def seed_sites(self):
        """Every occurrence of a seed address in the image, as (address of the dword, seed)."""
        out = []
        flat = self.image.flat
        for seed in SEED_ADDRESSES:
            pattern = struct.pack('<I', seed)
            at = 0
            while True:
                at = flat.find(pattern, at)
                if at < 0:
                    break
                out.append((self.image.base + at, seed))
                at += 1
        return out

    def find_alloc_sites(self):
        """Every call of the allocator whose size argument is a pushed constant: {call address: record kind}. The controller is the 0x14C-byte
        record ('C'); any other size is a record kind of its own ('H<size>'). A call whose size is not a constant is listed as a note."""
        out = {}
        for site in self.call_sites.get(ALLOCATOR, ()):
            entry = self.enclosing(self.starts, site)
            size = None
            if entry is not None:
                insns = self.func(entry).insns
                order = sorted(insns)
                if site in insns:
                    for va in reversed(order[max(0, order.index(site) - 6):order.index(site)]):
                        insn = insns[va]
                        if insn.mnemonic == 'call':
                            break
                        if insn.mnemonic == 'push':      # the last push before the call is the first (size) argument
                            if insn.operands[0].type == cx.X86_OP_IMM:
                                size = insn.operands[0].imm & MASK32
                            break
            if size is None:
                self.notes[(site, 'alloc')] = 'allocation whose size is not a pushed constant: the record is not followed'
                continue
            out[site] = 'C' if size == CONTROLLER_ALLOCATION else f'H{size:X}'
        return out

    def occurrences(self, address):
        """Every place of the image holding the 32-bit value `address`."""
        flat = self.image.flat
        pattern = struct.pack('<I', address)
        out = []
        at = 0
        while True:
            at = flat.find(pattern, at)
            if at < 0:
                return out
            out.append(self.image.base + at)
            at += 1

    def expand(self):
        """New analysis roots: the functions naming a holder global or table, and the callers of functions that return a controller."""
        roots = set()
        for address in sorted(a for a in {*self.abs_holders, *self.table_holders} if self.image.in_data(a)):
            for site in self.occurrences(address):
                if not self.image.in_text(site):
                    if site not in self.data_refs[address]:
                        self.data_refs[address].append(site)
                    continue
                entry = self.enclosing(self.starts, site)
                if entry is None:
                    self.notes[(site, 'orphan')] = f'0x{address:X} named at 0x{site:X} outside every function found'
                elif entry not in self.analyzed:
                    roots.add(entry)
        returning = [e for e in sorted(self.analyzed) if any(is_pointer(t) for t in self.ret_regs.get(e, {}).get('eax', EMPTY))]
        for entry in returning:
            for site in self.call_sites.get(entry, ()):
                caller = self.enclosing(self.starts, site)
                if caller is not None and caller not in self.analyzed:
                    roots.add(caller)
        return roots

    def solve(self, roots, limit=80):
        """Fixpoint over the roots; roots grow with the holders and returns the fixpoint finds (see expand)."""
        self.analyzed |= set(roots)
        rounds = 0
        while True:
            for _ in range(limit):
                rounds += 1
                if self.verbose:
                    print(f'round {rounds}: {len(self.analyzed)} functions', file=sys.stderr, flush=True)
                self.changed = False
                self.holder_stores = {}      # only the last, complete round counts: stores that looked unknown early may have a known base later
                for entry in sorted(self.analyzed):
                    began = time.monotonic()
                    self.run(entry)
                    if self.verbose and time.monotonic() - began > 3:
                        print(f'  slow: 0x{entry:X} {time.monotonic() - began:.1f}s', file=sys.stderr, flush=True)
                if not self.changed:
                    break
            else:
                raise RuntimeError('data-flow did not converge')
            more = self.expand()
            if not more:
                return rounds
            self.analyzed |= more

    def initial_roots(self):
        self.starts = self.call_targets()
        self.alloc_sites = self.find_alloc_sites()
        roots, orphans = set(), []
        for site, seed in self.seed_sites():
            if not self.image.in_text(site):
                self.data_refs[seed].append(site)
                continue
            entry = self.enclosing(self.starts, site)
            if entry is None:
                orphans.append(site)
            else:
                roots.add(entry)
        for site in (a for a, kind in self.alloc_sites.items() if kind == 'C'):
            entry = self.enclosing(self.starts, site)
            if entry is None:
                orphans.append(site)
            else:
                roots.add(entry)
        return roots, orphans


# ------------------------------------------------------------------------------------------------------------------------------------
# What each reader does with the word it loads
# ------------------------------------------------------------------------------------------------------------------------------------
TRACK_BITS = 0x7E0          # controller +0 bits 0x20, 0x40, 0x80, 0x100, 0x200, 0x400: the track flags of SAVE-921
SUBREG = {'eax': (0xFFFFFFFF, 0), 'ax': (0xFFFF, 0), 'al': (0xFF, 0), 'ah': (0xFF, 8),
          'ecx': (0xFFFFFFFF, 0), 'cx': (0xFFFF, 0), 'cl': (0xFF, 0), 'ch': (0xFF, 8),
          'edx': (0xFFFFFFFF, 0), 'dx': (0xFFFF, 0), 'dl': (0xFF, 0), 'dh': (0xFF, 8),
          'ebx': (0xFFFFFFFF, 0), 'bx': (0xFFFF, 0), 'bl': (0xFF, 0), 'bh': (0xFF, 8),
          'esi': (0xFFFFFFFF, 0), 'si': (0xFFFF, 0), 'edi': (0xFFFFFFFF, 0), 'di': (0xFFFF, 0),
          'ebp': (0xFFFFFFFF, 0), 'bp': (0xFFFF, 0)}
FLAG_NEUTRAL = ('mov', 'lea', 'push', 'pop', 'nop', 'movzx', 'movsx', 'xchg')
FLAG_READERS = ('adc', 'sbb', 'lahf', 'pushfd', 'pushf')
FLAG_FREE = ('mov', 'lea', 'push', 'pop', 'nop', 'xchg', 'cdq', 'cwde', 'movzx', 'movsx', 'fld', 'fstp', 'fst', 'fxch', 'fild')


class Verdict:
    def __init__(self):
        self.observed = 0
        self.stored_back = False
        self.returned = False
        self.unclassified: list[str] = []
        self.notes: list[str] = []

    def text(self):
        if self.unclassified:
            return 'UNCLASSIFIED: ' + '; '.join(sorted(set(self.unclassified)))
        notes = (' (' + ', '.join(sorted(set(self.notes))) + ')') if self.notes else ''
        if self.observed & TRACK_BITS:
            return f'OBSERVES 0x{self.observed & TRACK_BITS:X} of the track flags; bits read 0x{self.observed:X}{notes}'
        if self.observed:
            return f'observes bits 0x{self.observed:X} only{notes}'
        return 'observes no bit (read-modify-write, or the value is dropped)' + notes


class Classifier:
    STEP_LIMIT = 600

    def __init__(self, analysis: Analysis):
        self.a = analysis
        self.image = analysis.image

    def flags_consumed(self, insn):
        """Whether the flags this instruction sets are read before the next flag writer."""
        va = insn.address + insn.size
        for _ in range(8):
            nxt = self.image.decode(va)
            if nxt is None:
                return True
            m = nxt.mnemonic
            if m.startswith('j') and m != 'jmp' or m.startswith(('set', 'cmov')) or m in FLAG_READERS:
                return True
            if m in ('jmp', 'ret', 'call'):
                return False
            if m in FLAG_FREE:
                va += nxt.size
                continue
            try:
                writes = [nxt.reg_name(r) for r in nxt.regs_access()[1]]
            except capstone.CsError:
                return True
            if 'eflags' in writes:
                return False
            va += nxt.size
        return True

    def verdict(self, event, entry) -> Verdict:
        v = Verdict()
        insn = self.image.decode(event.address)
        m = insn.mnemonic
        ops = insn.operands
        mem_index = next((i for i, op in enumerate(ops) if op.type == cx.X86_OP_MEM), None)
        if mem_index is None or event.lo is None:
            v.unclassified.append('offset not placed' if event.lo is None else 'no memory operand')
            return v
        mem = ops[mem_index]
        size = mem.size or 4
        lo = event.lo
        loaded = (((1 << (8 * size)) - 1) << (8 * lo)) & MASK32
        if m.startswith(('rep', 'movs', 'cmps', 'lods', 'scas')) and 'es:' in insn.op_str or m.startswith('rep'):
            v.unclassified.append('string copy from the controller')
            return v
        if m == 'test':
            other = ops[1 - mem_index]
            v.observed |= ((other.imm & ((1 << (8 * size)) - 1)) << (8 * lo)) & MASK32 if other.type == cx.X86_OP_IMM else loaded
            return v
        if m == 'cmp':
            v.observed |= loaded
            return v
        if m in ('mov', 'movzx', 'movsx') and mem_index == 1 and ops[0].type == cx.X86_OP_REG:
            self.follow(v, insn, entry, ops[0], loaded, lo, mem)
            if v.returned:
                used = [site for site in self.a.call_sites.get(entry, ()) if not self.eax_unused_after(site)]
                if used or not self.a.call_sites.get(entry):
                    v.unclassified.append('value returned in eax' + (f' and read by the caller at 0x{used[0]:X}' if used else ' to an unknown caller'))
                else:
                    v.notes.append('value returned in eax, which no caller reads')
            return v
        if m == 'mov' and mem_index == 0:
            v.notes.append('plain store')
            return v
        if m in ('and', 'or', 'xor', 'add', 'sub') and mem_index == 0:
            imm = ops[1].imm & MASK32 if ops[1].type == cx.X86_OP_IMM else None
            if self.flags_consumed(insn):
                if imm is None:
                    v.unclassified.append(f'flags of {m} on the word are consumed')
                elif m == 'and':
                    v.observed |= (imm & ((1 << (8 * size)) - 1)) << (8 * lo)
                else:
                    v.observed |= loaded
            return v
        if m.startswith('f'):
            v.unclassified.append(f'x87 access ({m}) to controller +0')
            return v
        v.unclassified.append(f'{m} reads the word')
        return v

    def consumed_by_noop(self, push):
        """A `push` whose value reaches a call of a function that is only `ret`/`ret N` (the retail build's empty debug print)."""
        va = push.address + push.size
        for _ in range(14):
            insn = self.image.decode(va)
            if insn is None or insn.mnemonic in ('jmp', 'ret') or insn.mnemonic.startswith('j'):
                return False
            if insn.mnemonic == 'call':
                if insn.operands[0].type != cx.X86_OP_IMM:
                    return False
                first = self.image.decode(insn.operands[0].imm)
                return first is not None and first.mnemonic == 'ret'
            va += insn.size
        return False

    def eax_unused_after(self, call_site):
        """Whether the instruction stream after the call at `call_site` overwrites eax before reading it."""
        va = call_site + 5
        for _ in range(10):
            insn = self.image.decode(va)
            if insn is None:
                return False
            try:
                read, written = insn.regs_access()
            except capstone.CsError:
                return False
            read32 = {PARENT.get(insn.reg_name(r)) for r in read}
            write32 = {PARENT.get(insn.reg_name(r)) for r in written}
            if insn.mnemonic in ('xor', 'sub') and len(insn.operands) == 2 and insn.operands[0].type == insn.operands[1].type == cx.X86_OP_REG \
                    and insn.operands[0].reg == insn.operands[1].reg:
                read32 = set()
            if 'eax' in read32:
                return False
            if 'eax' in write32:
                return True
            if insn.mnemonic in ('jmp', 'ret', 'call') or insn.mnemonic.startswith('j'):
                return False
            va += insn.size
        return False

    @staticmethod
    def same_location(insn, mem):
        """`insn` has a memory operand naming the same base register, disp and no index as `mem`."""
        for op in insn.operands:
            if op.type == cx.X86_OP_MEM and op.mem.base == mem.mem.base and op.mem.disp == mem.mem.disp and not op.mem.index:
                return True
        return False

    @staticmethod
    def regspace(insn, op, imm=None):
        """The bits of the 32-bit register that operand `op` (a register) touches; with `imm`, only those the immediate selects."""
        mask, shift = SUBREG[insn.reg_name(op.reg)]
        if imm is None:
            return mask << shift
        return ((imm & MASK32) & mask) << shift

    def follow(self, v, load, entry, dst, loaded, lo, mem):
        """Follow the loaded value (register `dst`) through the function and record what is done with it.

        An alias is (register, shift, live, in_place): register bit i holds word bit i + shift while bit i is set in `live`; `in_place`
        is true while the base register of the load is unchanged, so a store to the same operand writes the word back where it came from."""
        f = self.a.func(entry)
        base = PARENT.get(load.reg_name(mem.mem.base)) if mem.mem.base else None
        size = mem.size or 4
        start = (PARENT[load.reg_name(dst.reg)], 8 * lo, (1 << (8 * size)) - 1, True)
        queue = [(load.address + load.size, frozenset({start}))]
        seen = set()
        while queue:
            va, aliases = queue.pop()
            if (va, aliases) in seen:
                continue
            seen.add((va, aliases))
            if len(seen) > self.STEP_LIMIT:
                v.unclassified.append('value flow too long to follow')
                return
            insn = f.insns.get(va)
            if insn is None or not aliases:
                continue
            m = insn.mnemonic
            ops = insn.operands
            try:
                read, written = insn.regs_access()
            except capstone.CsError:
                read, written = (), ()
            read32 = {PARENT.get(insn.reg_name(r)) for r in read}
            written32 = {PARENT.get(insn.reg_name(r)) for r in written}
            if m in ('xor', 'sub', 'sbb') and len(ops) == 2 and ops[0].type == ops[1].type == cx.X86_OP_REG and ops[0].reg == ops[1].reg:
                read32 = set()
            hit = [a for a in aliases if a[0] in read32]
            new = {a for a in aliases if a not in hit}
            if m == 'call':
                target = ops[0].imm if ops[0].type == cx.X86_OP_IMM else None
                live_in = self.a.inputs(target)[0] if target is not None and not self.a.is_import_thunk(target) else {'ecx', 'edx'}
                for a in hit:
                    if a[0] in live_in or target is None:
                        v.unclassified.append(f'0x{insn.address:X} passed to {"callee 0x%X" % target if target else "an indirect call"} in {a[0]}')
                new = {a for a in new if a[0] not in ('eax', 'ecx', 'edx')}
                written32 = set()
                hit = []
            new = {a for a in new if a[0] not in written32}
            for reg, shift, live, in_place in hit:
                a = (reg, shift, live, in_place)
                if m == 'test':
                    o0, o1 = ops
                    if o0.type == cx.X86_OP_REG and PARENT[insn.reg_name(o0.reg)] == reg:
                        bits = self.regspace(insn, o0, o1.imm if o1.type == cx.X86_OP_IMM else None)
                    else:
                        bits = MASK32
                    v.observed |= ((bits & live) << shift) & MASK32
                    new.add(a)
                elif m == 'cmp':
                    bits = 0
                    for o in ops:
                        if o.type == cx.X86_OP_REG and PARENT[insn.reg_name(o.reg)] == reg:
                            bits |= self.regspace(insn, o)
                    v.observed |= ((bits & live) << shift) & MASK32
                    new.add(a)
                elif m in ('and', 'or', 'xor') and ops[0].type == cx.X86_OP_REG and PARENT[insn.reg_name(ops[0].reg)] == reg \
                        and ops[1].type == cx.X86_OP_IMM:
                    region = self.regspace(insn, ops[0])
                    imm_bits = self.regspace(insn, ops[0], ops[1].imm)
                    consumed = self.flags_consumed(insn)
                    if m == 'and':
                        if consumed:
                            v.observed |= ((live & region & imm_bits) << shift) & MASK32
                        keep = (live & ~region) | (live & imm_bits)
                    else:
                        if consumed:
                            v.observed |= ((live & region) << shift) & MASK32
                        keep = (live & ~region) | (live & region & ~imm_bits) if m == 'or' else live
                    if keep:
                        new.add((reg, shift, keep, in_place))
                elif m == 'mov' and ops[0].type == cx.X86_OP_MEM and ops[1].type == cx.X86_OP_REG and PARENT[insn.reg_name(ops[1].reg)] == reg \
                        and not (ops[0].mem.base and PARENT.get(insn.reg_name(ops[0].mem.base)) == reg):
                    if in_place and self.same_location(insn, mem):
                        v.stored_back = True
                    else:
                        v.unclassified.append(f'0x{insn.address:X} copied to memory: {m} {insn.op_str}')
                elif m in ('mov', 'movzx', 'movsx') and ops[0].type == cx.X86_OP_REG and ops[1].type == cx.X86_OP_REG and SUBREG[insn.reg_name(ops[1].reg)][1] == 0:
                    dest = PARENT[insn.reg_name(ops[0].reg)]
                    if dest != reg:
                        new.add((dest, shift, live, in_place))
                    new.add(a)
                elif m == 'push' and self.consumed_by_noop(insn):
                    v.notes.append('passed to a function that returns at once')
                    new.add(a)
                else:
                    v.unclassified.append(f'0x{insn.address:X} {m} {insn.op_str}')
            if m == 'ret':
                if any(a[0] == 'eax' for a in new):
                    v.returned = True
                continue
            if base is not None and base in written32:
                new = {(a[0], a[1], a[2], False) for a in new}
            for nxt in f.succ.get(va, []):
                queue.append((nxt, frozenset(new)))


def immediate_track_tests(analysis: Analysis):
    """Third layer, independent of the flow: every `test [reg+0..3], imm` anywhere in the image whose immediate selects one of the track flags
    (0x20..0x400 of the dword at that address), with the last assignment of its base register seen just before it."""
    image = analysis.image
    cs = capstone.Cs(capstone.CS_ARCH_X86, capstone.CS_MODE_32)
    cs.detail = True
    cs.skipdata = True
    events = {e.address for e in analysis.events.values()}
    rows = []
    for lo, hi in image.text:
        recent = []
        offset = lo - image.base
        for insn in cs.disasm(bytes(image.flat[offset:offset + hi - lo]), lo):
            if insn.id == 0:
                recent.clear()
                continue
            recent.append(insn)
            del recent[:-12]
            if insn.mnemonic not in ('test', 'bt') or len(insn.operands) != 2:
                continue
            mem, imm = insn.operands
            if mem.type != cx.X86_OP_MEM or imm.type != cx.X86_OP_IMM:
                continue
            if not mem.mem.base or mem.mem.index or not 0 <= mem.mem.disp <= 3 or insn.reg_name(mem.mem.base) == 'esp' or insn.reg_name(mem.mem.segment) in ('fs', 'gs'):
                continue
            size = mem.size or 4
            bits = ((imm.imm & ((1 << (8 * size)) - 1)) << (8 * mem.mem.disp)) & MASK32
            if insn.mnemonic == 'bt':
                bits = (1 << (imm.imm & 31)) << (8 * mem.mem.disp) & MASK32
            if not bits & TRACK_BITS:
                continue
            base = PARENT[insn.reg_name(mem.mem.base)]
            source = ''
            for before in reversed(recent[:-1]):
                try:
                    if base in {PARENT.get(before.reg_name(r)) for r in before.regs_access()[1]}:
                        source = f'{before.mnemonic} {before.op_str}'
                        break
                except capstone.CsError:
                    break
            rows.append((insn.address, f'{insn.mnemonic} {insn.op_str}', insn.address in events, source))
    return rows


# ------------------------------------------------------------------------------------------------------------------------------------
# Hand-reviewed dispositions (each read in tp.exe; the scan prints every hit that is not listed here as NEW and `--check` fails on it)
# ------------------------------------------------------------------------------------------------------------------------------------
REVIEWED_READERS = {
    0x43F641: 'cmp [edi], 0 of 0x43F4E0 tests `*param_1`, a car record, not the controller: the callers 0x43EF5F and 0x43EFCC (0x43EB70) push the car walked '
              'from [[0x790FE4]+0x18] (controller +0x54) through its +0x64 links; the controller enters the loop only as the end sentinel in ebp '
              '(0x43EEA9, when there is no car), a path on which esi is zero and the loop is left at 0x43EEEB',
}
REVIEWED_HOLDERS = {
    0x43392A: 'the global-array copy of 0x4338E0: [ebx - 4] reads the last slot of 0x791D08 + n * 0x104 (n < 5, the car count at 0x791020 bounds the loop), '
              'never the adjacent 0x792218',
    0x43BA09: '0x43B8B0 reuses its first argument slot as an out parameter (`&param_1` goes to 0x43B7E0), the stored value is the section index it returns, '
              'not the controller the slot held on entry',
}
REVIEWED_ESCAPES = {
    **{a: 'action-record log write (COAST-plan section 23): the stream object stores the value of a local through its vtable' for a in
       (0x403D25, 0x403D33, 0x403D85, 0x403D93, 0x403DA1, 0x403EF5, 0x403F03, 0x403F11)},
    0x45BA09: 'GlobalFree of the freed record (the wrapper 0x45BA00): the pointer is released, not read',
    0x4680F9: 'GetDC(window handle from 0x7A130C), not a controller',
    0x5F5E31: 'the save stream\'s Read through its vtable (0x5F5E20): fills a buffer, reads no controller memory',
    0x5F5E61: 'the save stream\'s Write through its vtable (0x5F5E50): the saver passes the address of its local wire words (0x437300), not the controller',
}
# Stores of a car, node or other record pointer into a record the scan has no type for (an array element, a thing record at 0x7A4610 +0xD4, ...):
# none stores the controller (those are the two reviewed C entries above). The scan lists them so a change shows; they are not reviewed one by one.
UNTYPED_STORES = (0x433D7B, 0x434296, 0x434551, 0x438288, 0x439FE3, 0x43A01D, 0x43EC0C, 0x43EC17, 0x43EC3D, 0x43EC59, 0x43ECA3, 0x43ECB5, 0x44106F, 0x44108C,
                  0x4424F7, 0x4424FD, 0x467CFD, 0x467D5D)


# ------------------------------------------------------------------------------------------------------------------------------------
# Driver
# ------------------------------------------------------------------------------------------------------------------------------------
def load_image(path: Path) -> Image:
    raw = path.read_bytes()
    if len(raw) != TPW_SIZE or hashlib.sha256(raw).hexdigest() != TPW_SHA256:
        raise SystemExit(f'{path}: not the identified Sim Theme Park World v2.0 no-CD tp.exe (only that file may be analysed)')
    return Image(raw)


def run_analysis(image: Image) -> tuple[Analysis, int, list[int]]:
    analysis = Analysis(image)
    roots, orphans = analysis.initial_roots()
    rounds = analysis.solve(roots)
    return analysis, rounds, orphans


def describe(tokens) -> str:
    return '|'.join(sorted(f'{t[0]}+{t[1]:#x}' if isinstance(t[1], int) else f'{t[0]}+?' for t in tokens if len(t) == 2))


def report(analysis: Analysis, rounds: int, orphans: list[int], out=sys.stdout) -> list[str]:
    """Print the evidence; return the problems (empty when every hit is either harmless or reviewed)."""
    image = analysis.image
    problems: list[str] = []
    classifier = Classifier(analysis)
    inside = sorted(e for e in analysis.analyzed if MODULE[0] <= e < MODULE[1])
    outside = sorted(e for e in analysis.analyzed if not MODULE[0] <= e < MODULE[1])
    print(f'tp.exe controller +0 reader scan: {len(inside)} functions of the coaster module 0x{MODULE[0]:X}..0x{MODULE[1]:X} and {len(outside)} outside it '
          f'followed, {rounds} data-flow rounds', file=out)
    sites = analysis.seed_sites()
    for seed, name in SEED_ADDRESSES.items():
        where = sorted(a for a, s in sites if s == seed and image.in_text(a))
        data = [a for a, s in sites if s == seed and not image.in_text(a)]
        inside_module = all(MODULE[0] <= a < MODULE[1] for a in where)
        print(f'seed 0x{seed:X} ({name}): {len(where)} code sites in 0x{where[0]:X}..0x{where[-1]:X}, {len(data)} data references, all in the module: {inside_module}', file=out)
        if not inside_module or data:
            problems.append(f'seed 0x{seed:X} is named outside the coaster module')
    kinds = sorted(set(analysis.alloc_sites.values()))
    print(f'controller allocation: {", ".join("0x%X" % a for a, k in sorted(analysis.alloc_sites.items()) if k == "C")} (the only 0x{CONTROLLER_ALLOCATION:X}-byte '
          f'allocation of {len(analysis.alloc_sites)} constant-size calls of 0x{ALLOCATOR:X}, {len(kinds)} record kinds)', file=out)
    print('holders found: ' + ', '.join(f'0x{a:X}={describe(t)}' for a, t in sorted(analysis.abs_holders.items())) + '; pool table '
          + ', '.join(f'0x{a:X}={describe(t)}' for a, t in sorted(analysis.table_holders.items())), file=out)
    print('fields found that hold a tracked pointer: ' + ', '.join(
        f'{k}+{hex(off)}={describe(t)}' for (k, off), t in sorted(analysis.fields.items(), key=lambda kv: (kv[0][0], kv[0][1])) if isinstance(off, int)
        and any(x[0] in ('C', 'H48') for x in t)), file=out)
    if orphans:
        problems.append('seed sites outside every function found: ' + ', '.join(f'0x{a:X}' for a in orphans))
    print(file=out)
    print('READERS of controller +0..3 (address, function, offset, access, instruction -> what it does with the word)', file=out)
    bits = 0
    for event in sorted((e for e in analysis.events.values() if e.lo is not None and 'r' in e.access), key=lambda e: (e.address, e.lo)):
        verdict = classifier.verdict(event, min(event.functions))
        expected = bool({SAVER, LOADER} & event.functions)
        tag = 'saver/loader' if expected else ''
        flagged = bool(verdict.observed & TRACK_BITS or verdict.unclassified)
        if flagged and not expected:
            if event.address in REVIEWED_READERS:
                tag = 'REVIEWED: ' + REVIEWED_READERS[event.address]
            else:
                tag = 'NEW'
                problems.append(f'reader 0x{event.address:X} ({event.text}): {verdict.text()}')
        elif not expected:
            bits |= verdict.observed
        print(f'0x{event.address:X}  0x{min(event.functions):X}  +{event.lo} {event.access}  {event.text}  -> {verdict.text()}  {tag}', file=out)
    stale = [a for a in REVIEWED_READERS if not any(e.address == a for e in analysis.events.values())]
    problems += [f'reviewed reader 0x{a:X} no longer found' for a in stale]
    print(f'bits observed by the readers outside the saver and loader (reviewed hits left out): 0x{bits:X}; of the track flags (0x{TRACK_BITS:X}): 0x{bits & TRACK_BITS:X}', file=out)
    unplaced = [e for e in analysis.events.values() if e.lo is None]
    if unplaced:
        problems.append(f'{len(unplaced)} accesses through a controller pointer of unknown offset')
        for event in sorted(unplaced, key=lambda e: e.address)[:20]:
            print(f'UNPLACED 0x{event.address:X}  {event.text}', file=out)
    print(file=out)
    print('STORES of a tracked pointer through a base the scan does not know', file=out)
    for address, text in sorted(analysis.holder_stores.items()):
        if 'base unknown' not in text and 'not followed' not in text:
            continue
        controller = ' [C]' in text
        if address in REVIEWED_HOLDERS:
            tag = 'REVIEWED: ' + REVIEWED_HOLDERS[address]
        elif address in UNTYPED_STORES and not controller:
            tag = 'untyped record pointer, never the controller'
        else:
            tag = 'NEW'
            problems.append(f'store 0x{address:X}: {text}')
        print(f'0x{address:X}  {text}  {tag}', file=out)
    problems += [f'reviewed store 0x{a:X} no longer found' for a in REVIEWED_HOLDERS if a not in analysis.holder_stores]
    print(file=out)
    print('ESCAPES (a tracked pointer in a call the scan cannot follow)', file=out)
    for (address, kind), text in sorted(analysis.escapes.items(), key=lambda kv: kv[0][0]):
        if address in REVIEWED_ESCAPES:
            tag = 'REVIEWED: ' + REVIEWED_ESCAPES[address]
        else:
            tag = 'NEW'
            problems.append(f'escape 0x{address:X}: {text}')
        print(f'0x{address:X}  {text}  {tag}', file=out)
    problems += [f'reviewed escape 0x{a:X} no longer found' for a in REVIEWED_ESCAPES if not any(k[0] == a for k in analysis.escapes)]
    print(file=out)
    jumps = [(e, u) for e in sorted(analysis.analyzed) for u in analysis.func(e).unresolved]
    print(f'UNRESOLVED CONTROL FLOW in the followed functions: {len(jumps)}', file=out)
    for entry, (address, why) in jumps:
        print(f'0x{address:X}  in 0x{entry:X}: {why}', file=out)
        problems.append(f'unresolved control flow at 0x{address:X}: {why}')
    print(file=out)
    tests = immediate_track_tests(analysis)
    print(f'TESTS of a track flag at [reg+0..3] anywhere in the image (third layer, independent of the flow): {len(tests)}, '
          f'{sum(1 for t in tests if MODULE[0] <= t[0] < MODULE[1])} in the coaster module', file=out)
    for address, text, flow, source in tests:
        place = 'module' if MODULE[0] <= address < MODULE[1] else 'outside'
        who = 'saver/loader' if address in analysis.func(SAVER).insns or address in analysis.func(LOADER).insns else (
            'flow reader' if flow else 'not a controller pointer in the flow')
        print(f'0x{address:X}  {place:7}  {text}   (base set by: {source or "an earlier block"})  {who}', file=out)
    print(file=out)
    print('PROBLEMS: ' + ('none' if not problems else str(len(problems))), file=out)
    for problem in problems:
        print('  ' + problem, file=out)
    return problems


def main(argv) -> int:
    import argparse
    parser = argparse.ArgumentParser(description=__doc__.split('\n')[0])
    parser.add_argument('exe', nargs='?', help='the identified tp.exe (default: $OPENTPW_TPW_EXE)')
    parser.add_argument('--check', action='store_true', help='exit 1 when a hit is neither harmless nor reviewed')
    parser.add_argument('--verbose', action='store_true', help='print the data-flow rounds to stderr')
    args = parser.parse_args(argv[1:])
    if capstone is None:
        print('SKIP: needs the dev-only capstone package (pip install capstone)')
        return 0
    path = args.exe or os.environ.get('OPENTPW_TPW_EXE')
    if not path or not Path(path).is_file():
        print('SKIP: pass the identified tp.exe or set OPENTPW_TPW_EXE')
        return 0
    analysis = Analysis(load_image(Path(path)))
    analysis.verbose = args.verbose
    roots, orphans = analysis.initial_roots()
    rounds = analysis.solve(roots)
    problems = report(analysis, rounds, orphans)
    return 1 if args.check and problems else 0


if __name__ == '__main__':
    sys.exit(main(sys.argv))

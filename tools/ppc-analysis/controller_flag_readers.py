"""Static data-flow scan of tp.exe for every reader of the coaster controller's first dword (the "track flags", controller +0).

Usage: python3 -I tools/ppc-analysis/controller_flag_readers.py [TP_EXE]      (TP_EXE defaults to $OPENTPW_TPW_EXE)

Needs the dev-only `capstone` package; without it, or without the identified tp.exe (SHA-256 pinned by
oracle_x86/x86_harness.py), the script prints a skip line and exits 0 (CI has neither).  The executable is only read; its bytes
are never written anywhere and nothing is printed from it but addresses and instruction text of the reader sites.

What it does (see docs/reverse/COAST-plan.md "Track flags at controller +0 (SAVE-921)"):

1. Seeds.  Every controller is a record of the coaster module whose pointer is obtained in exactly three ways: the thing pool
   table at 0x790BD0 (indexed by the thing id the constructor 0x4368F0 hands out), the list head 0x790FE0 (next link at +0x18) and
   the "being built" global 0x790FEC.  The scan lists every place of the whole image that names those three addresses (as an
   immediate, so also `push`, `lea` and `mov reg, imm`) and requires each to lie in the coaster module.
2. Taint.  From the functions holding those places it follows pointer values through registers, the stack frame (pushes, locals,
   arguments with the exact esp offset), the controller's own fields, global and heap holders, direct calls (argument and return
   values, `ret N` callee pops) until nothing changes.  A value is the token ('C', k): the controller pointer plus k.
3. Readers.  Every memory operand whose base carries a controller token and whose byte range meets controller +0..3 is an event;
   so is every access through a token of unknown offset, every escape (a controller pointer stored into a holder the scan cannot
   follow, passed to an indirect or imported callee, returned from a function called from outside) and every unresolved
   indirect jump or esp loss.  Events are printed as a table: address, function, instruction, access, bits it can observe.

Nothing is committed from the executable: the script reads the file named on the command line at run time.
"""
from __future__ import annotations

import hashlib
import os
import struct
import sys
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
MASK32 = 0xFFFFFFFF
IMPORT_POPS: dict[str, int] = {}     # stdcall imports the coaster module reaches (none is known to receive a controller pointer)
DESCEND: set[int] = set()           # callees outside the module the data-flow follows into (reviewed by hand)
CALL_REGISTERS = ('ecx', 'edx')        # `this` and the second fastcall register; any other register input is reported (see register_inputs)
ALLOCATOR = 0x45B9D0                # the heap allocator the constructor 0x4368F0 calls with the controller size
CONTROLLER_SIZE = 0x14C
UNKNOWN = '?'
REALIGNED = 0x100000                # esp origin after `and esp, -8`
ARG_SLOTS = 8                       # stack arguments passed on to a callee
REG32 = ('eax', 'ecx', 'edx', 'ebx', 'esi', 'edi', 'ebp')
PARENT = {}
for _name, _subs in {'eax': ('ax', 'al', 'ah'), 'ecx': ('cx', 'cl', 'ch'), 'edx': ('dx', 'dl', 'dh'), 'ebx': ('bx', 'bl', 'bh'),
                     'esi': ('si',), 'edi': ('di',), 'ebp': ('bp',), 'esp': ('sp',)}.items():
    PARENT[_name] = _name
    for _sub in _subs:
        PARENT[_sub] = _name

# Token kinds: ('C', k) controller pointer + k (k int or '?'); ('S', k) caller-frame pointer = entry esp + k; ('G', a) address a of a seed global
# (or a pointer to the pool table, which the pool library indexes); ('P',) pointer into a caller's frame, seen from a callee.
EMPTY = frozenset()


def _shift(tokens, delta):
    out = set()
    for token in tokens:
        if token[0] in 'CSG' and isinstance(token[1], int):
            out.add((token[0], token[1] + delta))
        else:
            out.add(token)
    return frozenset(out)


def _blur(tokens):
    """The result of pointer arithmetic with an unknown amount: the same object, unknown offset."""
    out = set()
    for token in tokens:
        if token[0] == 'C':
            out.add(('C', UNKNOWN))
        elif token[0] in 'SG':
            out.add((token[0], UNKNOWN))
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
        self.table_holders = {0x790BD0}
        self.abs_holders = {0x790FE0, 0x790FEC}
        self.ctrl_fields: set[int] = set()
        self.field_disps: set[int] = set()
        self.alloc_sites: set[int] = set()
        self.events: dict[tuple, Event] = {}
        self.holder_stores: dict[int, str] = {}
        self.escapes: dict[tuple, str] = {}
        self.notes: dict[tuple, str] = {}
        self.calls_out: dict[int, set[int]] = defaultdict(set)
        self.changed = False
        self.descend_all = False
        self.watch: dict[int, dict] = {}
        self.current = None
        self.call_sites: dict[int, list[int]] = defaultdict(list)
        self.starts: list[int] = []
        self.data_refs: dict[int, list[int]] = defaultdict(list)
        self.input_cache: dict[int, tuple] = {}
        self.probing = False
        self.seen_pointer_into_frame = False

    # --- infrastructure -------------------------------------------------------------------------------------------------------------
    def func(self, entry):
        if entry not in self.functions:
            self.functions[entry] = Function(self.image, entry)
        return self.functions[entry]

    def add_entry(self, callee, regs, args):
        changed = False
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

    def add_set(self, target, value):
        """Register a holder; stores made outside the coaster module are only listed (self.outside_stores), never followed."""
        if self.current is not None and not MODULE[0] <= self.current < MODULE[1]:
            return
        if value not in target:
            target.add(value)
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
        if imm in SEED_ADDRESSES or imm in self.abs_holders or imm in self.table_holders:
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
            if mem.disp in self.table_holders or (not index and mem.disp in self.abs_holders):
                out.add(('C', 0))
            return frozenset(out)
        tokens = self.reg_tokens(st, base)
        if not tokens:
            return frozenset(out)
        for token in tokens:
            kind = token[0]
            if kind == 'S':
                if isinstance(token[1], int) and not index and st.esp is not None:
                    out |= st.stack.get(token[1] + mem.disp, EMPTY)
            elif kind == 'C':
                field = token[1] + mem.disp if isinstance(token[1], int) and not index else None
                if (field is not None and field in self.ctrl_fields) or (field is None and mem.disp in self.ctrl_fields):
                    out.add(('C', 0))
            elif kind == 'G':
                a = token[1] + mem.disp if isinstance(token[1], int) else None
                if (token[1] in self.table_holders) or (a is not None and (a in self.abs_holders or (index and a in self.table_holders))):
                    out.add(('C', 0))
            elif kind == 'P':
                if self.seen_pointer_into_frame:
                    self.notes[(insn.address, 'frame-load')] = 'load through a pointer into a caller frame that holds a controller pointer'
        return frozenset(out)

    def value(self, insn, op, st):
        if op.type == cx.X86_OP_REG:
            return self.reg_tokens(st, insn.reg_name(op.reg))
        if op.type == cx.X86_OP_IMM:
            return self.global_token(op.imm & MASK32)
        if op.type == cx.X86_OP_MEM and op.size == 4:
            return self.load(insn, op, st)
        return EMPTY

    def store(self, insn, op, st, tokens):
        mem = op.mem
        if insn.reg_name(mem.segment) in ('fs', 'gs'):
            return
        base = insn.reg_name(mem.base) if mem.base else None
        index = bool(mem.index)
        has_c = any(t[0] == 'C' for t in tokens)
        if base is None:
            if has_c:
                if index:
                    self.add_set(self.table_holders, mem.disp)
                else:
                    self.add_set(self.abs_holders, mem.disp)
                self.holder_stores.setdefault(insn.address, f'{insn.mnemonic} {insn.op_str} (absolute holder 0x{mem.disp:X})')
            return
        base_tokens = self.reg_tokens(st, base)
        if not base_tokens:
            if has_c:
                self.holder_stores.setdefault(insn.address, f'{insn.mnemonic} {insn.op_str} (object field +0x{mem.disp & MASK32:X}, base unknown)')
            return
        for token in base_tokens:
            kind = token[0]
            if kind == 'S':
                if isinstance(token[1], int) and not index and st.esp is not None:
                    key = token[1] + mem.disp
                    st.stack[key] = frozenset(tokens) if len(base_tokens) == 1 else st.stack.get(key, EMPTY) | tokens
            elif kind == 'C':
                if has_c:
                    if isinstance(token[1], int) and not index:
                        self.add_set(self.ctrl_fields, token[1] + mem.disp)
                        self.holder_stores.setdefault(insn.address, f'{insn.mnemonic} {insn.op_str} (controller field +0x{token[1] + mem.disp:X})')
                    else:
                        self.holder_stores.setdefault(insn.address, f'{insn.mnemonic} {insn.op_str} (controller field, offset unknown)')
            elif kind == 'G':
                if has_c:
                    a = token[1] + mem.disp if isinstance(token[1], int) else None
                    if index or a is None:
                        self.add_set(self.table_holders, token[1] if isinstance(token[1], int) else mem.disp)
                    else:
                        self.add_set(self.abs_holders, a)
                    self.holder_stores.setdefault(insn.address, f'{insn.mnemonic} {insn.op_str} (global holder)')
            elif kind == 'P':
                if has_c:
                    self.holder_stores.setdefault(insn.address, f'{insn.mnemonic} {insn.op_str} (caller frame)')

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
            if isinstance(token[1], int) and not mem.index:
                lo = token[1] + mem.disp
                hi = None if string else lo + size
                if not ((hi is None and lo < 4) or (hi is not None and lo < 4 and hi > 0)):
                    continue
                why = 'string operation from the controller' if string else ''
            else:
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
                    tokens = _blur(tokens)
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
                    self.set_reg(st, name, _blur(tokens | other))
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

    def is_import_thunk(self, entry):
        first = self.func(entry).insns.get(entry)
        return first is not None and first.mnemonic == 'jmp' and first.operands[0].type == cx.X86_OP_MEM

    def do_call(self, entry, insn, st):
        op = insn.operands[0]
        regs = {}
        for reg in CALL_REGISTERS:
            tokens = st.regs.get(reg, EMPTY)
            if tokens:
                regs[reg] = frozenset(('P',) if t[0] == 'S' else t for t in tokens)
        args = {}
        if st.esp is not None:
            for index in range(ARG_SLOTS):
                tokens = st.stack.get(st.esp + 4 * index, EMPTY)
                if tokens:
                    args[index] = frozenset(('P',) if t[0] == 'S' else t for t in tokens)
        if op.type == cx.X86_OP_IMM and not self.probing and (regs or args):
            callee_entry = op.imm
            if self.func(callee_entry).insns and not self.is_import_thunk(callee_entry):
                live, touched = self.inputs(callee_entry)
                regs = {reg: tokens for reg, tokens in regs.items() if reg in live}
                args = {index: tokens for index, tokens in args.items() if index in touched}
        tainted = any(t[0] == 'C' for ts in list(regs.values()) + list(args.values()) for t in ts)
        frame_pointer = any(t[0] == 'P' for ts in list(regs.values()) + list(args.values()) for t in ts)
        if frame_pointer and any(t[0] == 'C' for ts in st.stack.values() for t in ts) and not self.seen_pointer_into_frame:
            self.seen_pointer_into_frame = True
            self.changed = True
        result = {}
        pop = 0
        if op.type != cx.X86_OP_IMM:
            if tainted:
                self.escapes[(insn.address, 'indirect')] = f'call {insn.op_str} with a controller or pool pointer in a register or argument'
            st.regs.pop('eax', None)
            st.regs.pop('ecx', None)
            st.regs.pop('edx', None)
            return
        target = op.imm
        callee = self.func(target)
        first = callee.insns.get(target)
        external = first is None
        if first is not None and first.mnemonic == 'jmp' and first.operands and first.operands[0].type == cx.X86_OP_MEM \
                and first.operands[0].mem.base == 0 and not first.operands[0].mem.index:
            external = self.image.imports.get(first.operands[0].mem.disp, 'indirect jump')
        outside = not external and not (self.descend_all or MODULE[0] <= target < MODULE[1] or target in DESCEND)
        if tainted:
            if external or outside:
                self.escapes[(insn.address, target)] = f'call 0x{target:X} ({external if isinstance(external, str) else "outside the module"}) with a controller or pool pointer'
            else:
                self.add_entry(target, regs, args)
            self.calls_out[entry].add(target)
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
            st.regs['eax'] = frozenset({('C', 0)})
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
            if va in self.watch and not probe:
                self.watch[va].setdefault(entry, {})
                self.watch[va][entry] = {r: sorted(str(t) for t in tokens) for r, tokens in st.regs.items()}
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
        return (set(self.abs_holders), set(self.table_holders), set(self.ctrl_fields), self.seen_pointer_into_frame)

    def restore_state(self, state):
        self.abs_holders, self.table_holders, self.ctrl_fields, self.seen_pointer_into_frame = state

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
        """Widen a state so loops cannot grow a register's token set without bound."""
        for table in (st.regs, st.stack):
            for key, tokens in list(table.items()):
                for kind in 'CSG':
                    same = [t for t in tokens if t[0] == kind and isinstance(t[1], int)]
                    if len(same) > 6:
                        tokens = frozenset(t for t in tokens if t not in same) | {(kind, UNKNOWN)}
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
            if any(va <= site < va + insn.size for va, insn in f.insns.items()):
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
        """Calls of the allocator preceded by `push CONTROLLER_SIZE`: the constructor's allocation (the only controller source besides the seeds)."""
        flat = self.image.flat
        pattern = b'\x68' + struct.pack('<I', CONTROLLER_SIZE)
        out = []
        for lo, hi in self.image.text:
            at = lo - self.image.base
            end = hi - self.image.base
            while True:
                at = flat.find(pattern, at, end)
                if at < 0:
                    break
                va = self.image.base + at
                for _ in range(6):
                    insn = self.image.decode(va)
                    if insn is None:
                        break
                    if insn.mnemonic == 'call' and insn.operands[0].type == cx.X86_OP_IMM and insn.operands[0].imm == ALLOCATOR:
                        out.append(insn.address)
                        break
                    va += insn.size
                at += 1
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
        for address in sorted(self.abs_holders | self.table_holders):
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
        for entry in sorted(self.analyzed):
            if any(t[0] == 'C' for t in self.ret_regs.get(entry, {}).get('eax', EMPTY)):
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
                self.changed = False
                for entry in sorted(self.analyzed):
                    self.run(entry)
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
        self.alloc_sites = set(self.find_alloc_sites())
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
        for site in self.alloc_sites:
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
        return 'observes no bit (value merged back unchanged where it is not dropped)' + notes


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
                else:
                    v.unclassified.append(f'0x{insn.address:X} {m} {insn.op_str}')
            if m == 'ret':
                for a in new:
                    if a[0] == 'eax':
                        v.unclassified.append('value returned in eax')
                continue
            if base is not None and base in written32:
                new = {(a[0], a[1], a[2], False) for a in new}
            for nxt in f.succ.get(va, []):
                queue.append((nxt, frozenset(new)))


def module_instructions(analysis: Analysis):
    """address -> (instruction, owning function start) for every instruction of the coaster module reached from a call target in it."""
    owners = {}
    for start in analysis.starts:
        if MODULE[0] <= start < MODULE[1]:
            for va, insn in analysis.func(start).insns.items():
                if MODULE[0] <= va < MODULE[1] and (va not in owners or start < owners[va][1]):
                    owners[va] = (insn, start)
    return owners


def crosscheck(analysis: Analysis):
    """Second layer: every other `[reg+0..3]` access of the coaster module (not an event of the flow), run through the same value follower.

    These are accesses whose base the data-flow does not take for a controller (descriptors, sections, nodes, other records). The ones that would
    observe a track flag if the base *were* a controller are the ones to look at by hand."""
    classifier = Classifier(analysis)
    seen = {e.address for e in analysis.events.values()}
    rows = []
    for va, (insn, start) in sorted(module_instructions(analysis).items()):
        if va in seen or insn.mnemonic in ('lea', 'nop') or insn.mnemonic.startswith('f'):
            continue
        for op in insn.operands:
            if op.type == cx.X86_OP_MEM and op.mem.base and not op.mem.index and 0 <= op.mem.disp <= 3 and insn.reg_name(op.mem.base) != 'esp' \
                    and insn.reg_name(op.mem.segment) not in ('fs', 'gs') and 'r' in ('r' if op.access & 1 else ''):
                event = Event(va, f'{insn.mnemonic} {insn.op_str}', 'r', op.mem.disp, None, '')
                rows.append((insn, start, classifier.verdict(event, start)))
                break
    return rows


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
    analysis.descend_all = True
    roots, orphans = analysis.initial_roots()
    rounds = analysis.solve(roots)
    return analysis, rounds, orphans


def report(analysis: Analysis, rounds: int, orphans: list[int], out=sys.stdout) -> int:
    image = analysis.image
    classifier = Classifier(analysis)
    inside = sorted(e for e in analysis.analyzed if MODULE[0] <= e < MODULE[1])
    outside = sorted(e for e in analysis.analyzed if not MODULE[0] <= e < MODULE[1])
    print(f'functions followed: {len(inside)} in the coaster module 0x{MODULE[0]:X}..0x{MODULE[1]:X}, {len(outside)} outside it; {rounds} data-flow rounds', file=out)
    sites = analysis.seed_sites()
    for seed, name in SEED_ADDRESSES.items():
        where = sorted(a for a, s in sites if s == seed and image.in_text(a))
        data = sorted(a for a, s in sites if s == seed and not image.in_text(a))
        span = f'0x{where[0]:X}..0x{where[-1]:X}' if where else 'none'
        print(f'seed 0x{seed:X} ({name}): {len(where)} code sites {span}, {len(data)} data references; all in the module: '
              f'{all(MODULE[0] <= a < MODULE[1] for a in where)}', file=out)
    print(f'controller allocations: {", ".join("0x%X" % a for a in sorted(analysis.alloc_sites))} (push 0x{CONTROLLER_SIZE:X} then the allocator 0x{ALLOCATOR:X})', file=out)
    print(f'holders: globals {", ".join("0x%X" % a for a in sorted(analysis.abs_holders))}; tables {", ".join("0x%X" % a for a in sorted(analysis.table_holders))}; '
          f'controller fields {", ".join("+0x%X" % a for a in sorted(analysis.ctrl_fields))}', file=out)
    if orphans:
        print('sites outside every function found: ' + ', '.join(f'0x{a:X}' for a in orphans), file=out)
    print(file=out)
    placed = [e for e in analysis.events.values() if e.lo is not None and 'r' in e.access]
    unplaced = [e for e in analysis.events.values() if e.lo is None]
    print('READERS of controller +0..3 (address, function context, access, instruction, verdict)', file=out)
    bad = 0
    for event in sorted(placed, key=lambda e: (e.address, e.lo)):
        entry = min(event.functions)
        verdict = classifier.verdict(event, entry)
        expected = bool({SAVER, LOADER} & event.functions)
        flag = 'saver/loader' if expected else ''
        if (verdict.observed & TRACK_BITS or verdict.unclassified) and not expected:
            bad += 1
        print(f'0x{event.address:X}  {",".join("0x%X" % f for f in sorted(event.functions))}  +{event.lo} {event.access}  {event.text}  -> {verdict.text()} {flag}', file=out)
    print(file=out)
    print('UNPLACED accesses through a controller pointer of unknown offset', file=out)
    for event in sorted(unplaced, key=lambda e: e.address):
        print(f'0x{event.address:X}  {",".join("0x%X" % f for f in sorted(event.functions))}  {event.access}  {event.text}', file=out)
    print(file=out)
    print('HOLDER STORES the flow cannot follow (a controller pointer stored through a base it does not know)', file=out)
    for address, text in sorted(analysis.holder_stores.items()):
        if 'base unknown' in text or 'caller frame' in text or not MODULE[0] <= address < MODULE[1]:
            print(f'0x{address:X}  {text}', file=out)
    print('ESCAPES', file=out)
    for (address, kind), text in sorted(analysis.escapes.items(), key=lambda kv: kv[0][0]):
        print(f'0x{address:X}  {text}', file=out)
    print('NOTES', file=out)
    for key, text in sorted(analysis.notes.items(), key=lambda kv: str(kv[0])):
        print(f'{key[0] if isinstance(key[0], str) else hex(key[0])}  {text}', file=out)
    print(file=out)
    print(f'readers outside the saver and loader that observe the track flags or are unclassified: {bad}', file=out)
    return bad


def main(argv) -> int:
    if capstone is None:
        print('SKIP: needs the dev-only capstone package (pip install capstone)')
        return 0
    path = argv[1] if len(argv) > 1 else os.environ.get('OPENTPW_TPW_EXE')
    if not path or not Path(path).is_file():
        print('SKIP: pass the identified tp.exe or set OPENTPW_TPW_EXE')
        return 0
    analysis, rounds, orphans = run_analysis(load_image(Path(path)))
    report(analysis, rounds, orphans)
    return 0


if __name__ == '__main__':
    sys.exit(main(sys.argv))

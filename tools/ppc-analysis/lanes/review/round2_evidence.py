"""Round-2 review witnesses for the committed clock/economy/guests/rides/advisor/UI lanes.

Usage: python3 -I round2_evidence.py /path/to/mac-feral/bin
           [--mac-hfs hfs.img --pc-speech theme-park-world/Data/global/Speech]

Every check pins the identified Feral Mac application (via review_evidence.Binary),
decodes instruction fields at hand-reviewed addresses and compares them with
values a reviewer derived independently. Output is metadata only: addresses,
decoded meanings, interpreted constants and conclusions. Nothing from the
original programs is executed, emulated, printed or stored. The optional asset
check reports only file sizes, SHA-256 digests and image offsets.

The pure-arithmetic models below are independently written Python statements of
what the reviewed instructions compute, so lanes and tests can share one
explicit reading; they are not translations or emulations of original code.
"""
from __future__ import annotations

import argparse
import hashlib
import json
import mmap
import struct
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import review_evidence as rv  # noqa: E402
from review_evidence import Binary, ReviewError, decode, require  # noqa: E402

U32 = 0xFFFFFFFF


# --- Independent arithmetic models -------------------------------------------------

def s32(value: int) -> int:
    value &= U32
    return value - (1 << 32) if value & 0x80000000 else value


def loan_month_profit_delta(m: int, n: int, p: int, withdrawals_enabled: bool = True) -> int:
    """Net change to mProfitThisYear for one bought loan in the month handler (0xcc21c).

    Withdrawal path subtracts M; the interest adjustment then adds
    M - divwu(M*N - P, N) in wrapping 32-bit arithmetic, independent of the
    withdrawal flag. N == 0 skips the adjustment (its assert is diagnostic only).
    """
    delta = -m if withdrawals_enabled else 0
    if n:
        q = ((m * n - p) & U32) // (n & U32)
        delta += m - q
    return s32(delta)


def payoff_profit_delta(m: int, n: int, p: int, months_repaid: int, withdrawals_enabled: bool = True) -> int:
    """Net change to mProfitThisYear from early payoff (0xccc78).

    Outstanding X = M*(N - repaid) is subtracted when withdrawals are enabled;
    months_repaid is cleared *before* the adjustment, which therefore adds
    X - q*(N - 0) with q = divwu(M*N - P, N).
    """
    x = (m * ((n - months_repaid) & U32)) & U32
    delta = -x if withdrawals_enabled else 0
    if n:
        q = ((m * n - p) & U32) // (n & U32)
        delta += x - ((q * n) & U32)
    return s32(delta)


def naive_month_profit_delta(m: int, n: int, p: int) -> int:
    """The economy lane's stated rule: profit decreases by floor((M*N - P)/N)."""
    return -((m * n - p) // n)


def f32(x: float) -> float:
    return struct.unpack('>f', struct.pack('>f', x))[0]


def skill_single_precision(grade: int, percentage: int) -> int:
    """0xf41dc: frsp(double(grade_single) + pct/100.0) * 20.0f, then saturating u32 truncation."""
    value = f32(20.0 * f32(f32(float(grade)) + percentage / 100.0))
    return 0 if value < 0 else min(int(value), U32)


def queue_match(queue_count: int, field_60: int) -> int:
    """0xe93cc..0xe9400: 100 - divwu(queue*100, 4*max(field_60,1)); no clamp before the weighted sum."""
    cap = field_60 if field_60 else 1
    return s32(100 - (((queue_count * 100) & U32) // ((4 * cap) & U32)))


def anim_wait_ms(animator_return: int) -> int:
    """0xafb20/0xafc78/0xb007c: accumulator = max(ret - 300, 300) (signed compare in TRIGANIM)."""
    value = s32(animator_return - 300)
    return 300 if value < 300 else value


# --- Instruction witnesses -----------------------------------------------------------

def _x31(rt: int, ra: int, rb: int, xo: int, rc: int = 0) -> tuple:
    return ('x31', rt, ra, rb, xo, rc)


def _section_target(b: Binary, slot: int) -> tuple:
    t = b.rel.get(slot)
    if t is None:
        raise ReviewError(f'data:{slot:#x} is unrelocated')
    return (t.kind, t.target, t.addend)


def _code_of_vector(b: Binary, vector: int) -> int:
    kind, target, addend = _section_target(b, vector)
    require((kind, target), ('section', b.c.code.index), f'vector {vector:#x}')
    return addend


def _toc_global(b: Binary, at: int, reg: int) -> int:
    t = b.slot(at, reg)
    require((t.kind, t.target), ('section', b.c.data_section.index), f'TOC global at {at:#x}')
    return t.addend


def _branch_targets_within(b: Binary, start: int, end: int, lo: int, hi: int) -> None:
    """Fail if an unconditional or conditional (non-linking) branch in [start,end) leaves [lo,hi]."""
    for at in range(start, end, 4):
        d = decode(b.word(at), at)
        if d[0] in ('b', 'bc') and not lo <= d[-1] <= hi:
            raise ReviewError(f'branch at {at:#x} leaves {lo:#x}..{hi:#x}')
        if d[0] == 'bclr':
            raise ReviewError(f'return at {at:#x} inside {start:#x}..{end:#x}')


_LOGICAL_XO = {24, 26, 28, 60, 124, 284, 316, 412, 444, 476, 536, 792, 824, 922, 954, 986}
_NO_DEST_XO = {0, 32, 54, 86, 151, 183, 215, 247, 278, 407, 438, 439, 470, 662, 663, 695, 727, 759, 918, 982,
               1014, 4, 339, 467, 144, 371, 595}


def _dest_gpr(word: int) -> int | None:
    op = word >> 26
    rt, ra = word >> 21 & 31, word >> 16 & 31
    if op in (7, 8, 12, 13, 14, 15, 32, 33, 34, 35, 40, 41, 42, 43, 46):
        return rt
    if op in (20, 21, 23, 24, 25, 26, 27, 28, 29):
        return ra
    if op == 31:
        xo = word >> 1 & 0x3FF
        if xo in _NO_DEST_XO:
            return None
        return ra if xo in _LOGICAL_XO else rt
    return None


def clock_loop_audit(app: Binary) -> dict:
    # State-machine dispatch: bound 15, table entry 10 is the in-game catch-up loop.
    app.expect(0x1c135c, 'cmpli', 0, 0, 15)
    table = _toc_global(app, 0x1c1364, 3)
    require(_section_target(app, table + 4 * 10), ('section', 0, 0x1c2264), 'game-state case 10')
    # Prologue globals that the loop dereferences (no reassignment between 0x1c2264 and the loop).
    globals_ = {'substep_counter': _toc_global(app, 0x1c1224, 26), 'previous_ms': _toc_global(app, 0x1c1230, 28),
                'now_ms': _toc_global(app, 0x1c1234, 24), 'park_turns_this_callback': _toc_global(app, 0x1c1240, 18)}
    require(globals_['park_turns_this_callback'], 0x15c6b4, 'catch-up cap global')
    # Optional forced stepping: global 0x15c490 nonzero requests rate 32, i.e. 1000/32 = 31 ms.
    require(_toc_global(app, 0x1c2284, 3), 0x15c490, 'forced-step request global')
    app.expect(0x1c2290, 'bc', 12, 2, 0x1c22a4)
    app.expect(0x1c2298, 'addi', 4, 0, 32)
    require(app.call(0x1c229c), 0x10ec60, 'forced-step setter')
    app.expect(0x1c22a8, 'bl', 0x10ecc0)
    app.expect(0x10ec90, 'addi', 0, 0, 1000)
    app.expect(0x10ec98, *_x31(3, 0, 31, 459))       # divwu r3 = 1000 / rate
    app.expect(0x10eca0, 'stw', 3, 30, 52)
    # Backlog bound 2000 (signed), step 31, counter ++ before the skip gate.
    app.expect(0x1c22c4, *_x31(0, 0, 3, 40))          # subf: now - previous
    app.expect(0x1c22c8, 'cmpi', 0, 0, 2000)
    app.expect(0x1c22cc, 'bc', 4, 1, 0x1c24c0)
    app.expect(0x1c22d0, 'addi', 0, 3, -2000)
    app.expect(0x1c22e0, 'addi', 0, 3, 31)
    app.expect(0x1c22ec, 'addi', 0, 3, 1)
    app.expect(0x1c22f8, 'rlwinm', 0, 0, 0, 28, 28, 1)  # flags 0x52e44 & 8
    app.expect(0x1c2304, 'rlwinm', 0, 0, 0, 31, 31, 1)  # flags 0x52e40 & 1
    app.expect(0x1c2308, 'bc', 4, 2, 0x1c24c0)          # skip all substep work, counter already advanced
    require(_toc_global(app, 0x1c1220, 16), 0x52e44, 'skip-gate flag word A')
    require(_toc_global(app, 0x1c1250, 14), 0x52e40, 'skip-gate flag word B')
    app.expect(0x1c233c, 'rlwinm', 0, 0, 0, 29, 31, 1)
    app.expect(0x1c2354, 'cmpli', 0, 3, 3)
    # Loop condition is a SIGNED compare of now with previous.
    app.expect(0x1c24c8, *_x31(0, 3, 0, 0))
    app.expect(0x1c24cc, 'bc', 12, 1, 0x1c22dc)
    # Cap counter reset after the loop on every case-10 path (no branch escapes first).
    _branch_targets_within(app, 0x1c24d0, 0x1c27a8, 0x1c24d0, 0x1c27a8)
    app.expect(0x1c27a4, 'addi', 0, 0, 0)
    app.expect(0x1c27a8, 'stw', 0, 18, 0)
    # World tick: turn increments before the +0x1da738 == 4 early exit.
    app.expect(0x10539c, 'addi', 0, 3, 1)
    app.expect(0x1053a0, 'stw', 0, 4, -22772)
    app.expect(0x1053a4, 'lwz', 0, 4, -22728)
    app.expect(0x1053a8, 'cmpi', 0, 0, 4)
    require(app.call(0x10541c), 0xfa9b0, 'per-thing dispatcher')
    # Thing allocator: new slot becomes live-list head -> world tick visits newest first.
    require(_toc_global(app, 0x105250, 30), 0xeceec, 'live head global')
    app.expect(0x1052a8, 'lwz', 0, 30, 0)
    app.expect(0x1052ac, 'stw', 0, 28, 8)
    app.expect(0x1052c0, 'stw', 28, 30, 0)
    app.expect(0x104e8c, 'addi', 0, 0, 5119)
    app.expect(0x104f10, 'addi', 0, 0, 10239)
    return {'case': {'table': hex(table), 'entry_10': '0x1c2264'}, 'globals': {k: hex(v) for k, v in globals_.items()},
            'forced_step': 'global 0x15c490 != 0 -> rate 32 -> divwu 1000/32 = 31 ms stored at clock+52',
            'backlog': 'now - previous > 2000 (signed) -> previous = now - 2000',
            'substep': 'previous += 31; counter += 1; THEN skip all work when !(0x52e44 & 8) && (0x52e40 & 1)',
            'loop_compare': 'signed now > previous',
            'cap_reset': '0x1c27a8 on every case-10 path after the loop',
            'world_tick': 'turn += 1 at 0x1053a0 precedes the mode==4 exit at 0x1053a8',
            'thing_order': 'allocator pushes new slots at live head (0x1052c0); tick walks head -> older'}


def opcode_names_audit(app: Binary) -> dict:
    """The application carries (name, argument-spec) pairs for opcodes 0..105 at data 0x3ef20."""
    base = 0x3ef20

    def name(slot: int) -> str | None:
        t = app.rel.get(slot)
        if not t or t.kind != 'section' or t.target != app.c.code.index:
            return None
        return app.cstr(t.addend)

    names = [name(base + 8 * i) for i in range(106)]
    specs = [name(base + 8 * i + 4) for i in range(106)]
    require(all(names) and all(specs), True, 'opcode name table completeness')
    require(name(base + 8 * 106) is None or name(base + 8 * 106 + 4) is None, True, 'table end after 106 entries')
    app.expect(0xaf5b8, 'cmpli', 0, 0, 105)
    dispatch = _toc_global(app, 0xaf5c0, 3)
    picked = {}
    for op, label in [(1, 'CRIT_LOCK'), (2, 'CRIT_UNLOCK'), (6, 'ENDSLICE'), (7, 'GETTIME'), (44, 'WAIT'),
                      (45, 'WAITABS'), (95, 'SETTIMER'), (96, 'GETTIMER')]:
        require(names[op], label, f'native name of opcode {op}')
        picked[label] = {'opcode': op, 'case': hex(_section_target(app, dispatch + 4 * op)[2])}
    return {'table': hex(base), 'count': 106, 'dispatch_table': hex(dispatch), 'checked': picked}


def loan_profit_audit(app: Binary) -> dict:
    # Month handler 0xcc21c: batch balance flush precedes the loan loop.
    app.expect(0xcc24c, 'lwz', 27, 24, 16)
    app.expect(0xcc268, *_x31(0, 0, 27, 266))
    app.expect(0xcc26c, 'stw', 0, 24, 12)
    app.expect(0xcc298, *_x31(3, 3, 27, 266))
    app.expect(0xcc29c, 'stw', 3, 24, 292)
    app.expect(0xcc2a0, 'stw', 0, 24, 16)
    # Withdrawal flag gates balance and the -M profit write only.
    app.expect(0xcc2cc, 'lwz', 0, 24, 276)
    app.expect(0xcc2d8, 'bc', 12, 2, 0xcc33c)
    app.expect(0xcc334, *_x31(0, 27, 0, 40))         # profit - M
    app.expect(0xcc348, 'stw', 0, 26, 44)              # months_repaid += 1 (before adjustment)
    # Interest adjustment: profit += M - divwu(M*N - P, N), wrapping u32.
    app.expect(0xcc364, 'lwz', 5, 26, 32)
    app.expect(0xcc36c, 'bc', 12, 2, 0xcc394)
    app.expect(0xcc370, 'lwz', 6, 26, 36)
    app.expect(0xcc374, 'lwz', 3, 26, 24)
    app.expect(0xcc378, *_x31(0, 6, 5, 235))         # mullw M*N
    app.expect(0xcc37c, 'lwz', 4, 24, 292)
    app.expect(0xcc380, *_x31(0, 3, 0, 40))          # subf: M*N - P
    app.expect(0xcc384, *_x31(0, 0, 5, 459))         # divwu (UNSIGNED)
    app.expect(0xcc388, *_x31(0, 0, 6, 40))          # subf: M - q
    app.expect(0xcc38c, *_x31(0, 4, 0, 266))
    app.expect(0xcc390, 'stw', 0, 24, 292)
    app.expect(0xcc39c, *_x31(0, 3, 0, 32))          # cmplw repaid, N (equality only)
    app.expect(0xcc3a0, 'bc', 4, 2, 0xcc3c0)
    # Early payoff 0xccc78: unsigned affordability, clears repaid before reading it again.
    app.expect(0xcccd8, *_x31(29, 4, 3, 235))
    app.expect(0xcccdc, *_x31(0, 0, 29, 32))
    app.expect(0xccd74, 'stw', 0, 28, 0)
    app.expect(0xccd78, 'stw', 0, 27, 0)
    app.expect(0xccda8, 'lwz', 0, 27, 0)
    app.expect(0xccdb4, *_x31(0, 0, 6, 40))
    app.expect(0xccdc0, *_x31(3, 3, 6, 459))
    app.expect(0xccdc4, *_x31(0, 3, 0, 235))
    # Message routing: CMsgEndOfMonth (12) -> month handler, CMsgEndOfYear (13) -> profit reset.
    app.expect(0xcc158, 'cmpi', 0, 3, 13)
    app.expect(0xcc164, 'cmpi', 0, 3, 12)
    require(app.call(0xcc180), 0xcc21c, 'month handler call')
    app.expect(0xcc1e8, 'stw', 0, 29, 292)
    events = {}
    for ctor, caller, expected in [(0x116488, 0xe3fb0, ('CMsgEndOfDay', 11)),
                                   (0x1164a4, 0xe4088, ('CMsgEndOfMonth', 12)),
                                   (0x1164c0, 0xe4148, ('CMsgEndOfYear', 13))]:
        require(app.call(caller), ctor, f'calendar update call {caller:#x}')
        vtable = _toc_global(app, ctor + 8, 0)
        rtti = _section_target(app, _section_target(app, vtable)[2])[2]  # vtable -> RTTI record -> name
        type_fn = _code_of_vector(app, _section_target(app, vtable + 8)[2])
        _, _, _, type_id = decode(app.word(type_fn), type_fn)
        require(app.word(type_fn + 4), 0x4E800020, f'type getter {type_fn:#x} returns immediately')
        require((app.cstr(rtti), type_id), expected, f'event vtable {vtable:#x}')
        events[expected[0]] = {'type': type_id, 'constructor': hex(ctor), 'emitted_at': hex(caller),
                               'vtable': hex(vtable), 'type_getter': hex(type_fn)}
    easy = [(100000, 36), (50000, 36), (25000, 36), (10000, 36), (18000, 24), (30000, 30), (80000, 48),
            (65000, 30)]
    examples = []
    for p, n in easy:
        m = p // n  # 0 % APR: (1+0)^x == 1, so trunc(P/N)
        examples.append({'P': p, 'N': n, 'M': m, 'mac_month_profit_delta': loan_month_profit_delta(m, n, p),
                         'stated_rule_delta': naive_month_profit_delta(m, n, p)})
    return {'month_handler': {
                'batch_flush': 'balance += batch; stats += batch; profit += batch; batch = 0 (before loans)',
                'per_loan': 'if withdrawals: balance -= M, profit -= M; repaid += 1; '
                            'if N: profit += M - divwu(M*N - P, N)  [u32 wrap]; if repaid == N: clear',
                'wrap_condition': 'M*N < P (e.g. every 0 % APR offer whose P is not a multiple of N)'},
            'payoff': 'repaid cleared before adjustment -> profit += X - divwu(M*N-P, N)*N; net -(total interest)',
            'events': events, 'easy_sam_0pct_examples': examples}


def guest_queue_audit(app: Binary) -> dict:
    app.expect(0xe93c4, 'cmpi', 0, 0, 8)
    app.expect(0xe93e8, 'mulli', 0, 3, 100)
    app.expect(0xe93ec, 'rlwinm', 31, 5, 2, 0, 29, 0)  # slwi r5 = cap << 2
    app.expect(0xe93fc, *_x31(0, 0, 5, 459))
    app.expect(0xe9400, 'subfic', 22, 0, 100)
    writers = [hex(at) for at in range(0xe9404, 0xe96d8, 4) if _dest_gpr(app.word(at)) == 22]
    require(writers, [hex(0xe9414)], 'only the far-distance zeroing writes queue match before the sum')
    app.expect(0xe9414, 'addi', 22, 0, 0)
    app.expect(0xe96d8, *_x31(0, 21, 22, 235))
    app.expect(0xe9714, *_x31(31, 31, 7, 459))
    return {'queue_match': '100 - divwu(queue*100, 4*max(f60,1)); unclamped (negative when queue > 4*cap)',
            'combination': 'divwu(sum(weight*match) mod 2^32, sum(weights)) -> a negative total becomes huge',
            'writers_of_r22': writers}


def vm_audit(app: Binary) -> dict:
    # Dispatcher: non-opcode word -> diagnostic + PC = -10000.
    app.expect(0xaf5b0, 'bc', 4, 2, 0xb2340)
    app.expect(0xb234c, 'addi', 0, 0, -10000)
    app.expect(0xb2350, 'stw', 0, 31, 60)
    # COPY: literal destination exits before fetching the source operand.
    app.expect(0xaf5f4, 'bl', 0xaf3bc)
    app.expect(0xaf604, 'bc', 4, 2, 0xb2354)
    app.expect(0xaf610, 'bl', 0xaf3bc)
    app.expect(0xaf638, 'stw', 0, 31, 72)
    # RAND: bound is the raw sign-extended operand; no resolver call.
    app.expect(0xb07f4, 'bl', 0xaf3bc)
    app.expect(0xb07f8, *_x31(3, 28, 0, 922))
    require(app.call(0xb0800), 0x105328, 'RAND generator call')
    app.expect(0xb0810, 'addi', 4, 28, 1)
    app.expect(0xb0814, *_x31(0, 3, 4, 491))
    # Animation waits: max(ret - 300, 300).
    for sub, cmp_at, cmp_fields, set_at in [(0xafb20, 0xafb2c, ('cmpi', 0, 0, 300), 0xafb34),
                                            (0xb007c, 0xb0088, ('cmpi', 0, 0, 300), 0xb0090)]:
        app.expect(sub, 'addi', 0, 3, -300)
        app.expect(cmp_at, *cmp_fields)
        app.expect(set_at, 'addi', 0, 0, 300)
    app.expect(0xafc78, 'addi', 3, 3, -300)
    app.expect(0xafcb4, 'cmpli', 0, 0, 300)
    # TRIGANIMSPEED deadline uses the per-mille operand only (not script speed).
    app.expect(0xb00dc, 'mulli', 4, 5, 1000)
    app.expect(0xb00e0, *_x31(4, 4, 28, 491))
    return {'bad_word': 'diagnostic and PC = -10000 (script stops)',
            'copy_literal_destination': 'exits before reading source; next dispatch sees the source operand '
                                        'as an opcode word -> PC = -10000',
            'rand_bound': 'raw extsh operand (variables are not resolved)',
            'anim_wait': 'max(ret - 300, 300) in TRIGANIM/WAITANIM/TRIGANIMSPEED',
            'triganimspeed_deadline': 'now + acc*1000 / operand (signed divw)'}


def speed_control_audit(app: Binary) -> dict:
    out = {}
    for vector, record, code, target, label in [(0x7508, 0x452d0, 0x11315c, 0x127c88, 'divide_by_1.25'),
                                                (0x7510, 0x452e4, 0x113184, 0x127c48, 'multiply_by_1.25'),
                                                (0x7518, 0x452f8, 0x1131ac, 0x127c40, 'set')]:
        require(_code_of_vector(app, vector), code, f'callback vector {vector:#x}')
        require(_section_target(app, record), ('section', app.c.data_section.index, vector), f'record {record:#x}')
        out[label] = {'record': hex(record), 'vector': hex(vector), 'code': hex(code), 'calls': hex(target)}
    require(app.call(0x1131c0), 0x127c40, 'set-scale call')
    app.expect(0x1131bc, 'lfd', 1, 2, 0x56d8 - APP_TOC)
    require(app.double(0x56d8), 1.0, 'reset value')
    app.expect(0x127c40, 'stfd', 1, 3, 24)
    out['set']['value'] = 1.0
    return out


APP_TOC = rv.APP_TOC


def speech_identity(hfs: Path, pc_dir: Path) -> dict:
    result = {}
    with hfs.open('rb') as handle, mmap.mmap(handle.fileno(), 0, access=mmap.ACCESS_READ) as image:
        for name in ('speechHD.SDT', 'lips.wad', 'cat_speechSFX.map', 'cat_speechBANK.map'):
            data = (pc_dir / name).read_bytes()
            probe = data[:4096]
            hits, at = [], image.find(probe)
            while at >= 0:
                hits.append(at)
                at = image.find(probe, at + 1)
            identical = [h for h in hits if image[h:h + len(data)] == data]
            result[name] = {'bytes': len(data), 'sha256': hashlib.sha256(data).hexdigest(),
                            'prefix_hits': len(hits), 'contiguous_identical_offsets': identical}
    return result


def inspect(root: Path, hfs: Path | None, pc_speech: Path | None) -> dict:
    app = Binary(root, 'SimThemePark.data', 'SimThemePark.data', rv.APP_TOC)
    result = {'identity': rv.IDENTITIES['SimThemePark.data'], 'clock_loop': clock_loop_audit(app),
              'opcode_names': opcode_names_audit(app), 'loan_profit': loan_profit_audit(app),
              'guest_queue': guest_queue_audit(app), 'vm': vm_audit(app), 'speed_control': speed_control_audit(app)}
    if hfs and pc_speech:
        result['speech_identity'] = speech_identity(hfs, pc_speech)
    return result


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('bin_root', type=Path)
    parser.add_argument('--mac-hfs', type=Path)
    parser.add_argument('--pc-speech', type=Path)
    args = parser.parse_args()
    try:
        result = inspect(args.bin_root, args.mac_hfs, args.pc_speech)
    except (OSError, rv.pef.PEFError, ReviewError) as error:
        parser.exit(1, f'round2 evidence: {error}\n')
    print(json.dumps(result, indent=2, sort_keys=True))


if __name__ == '__main__':
    main()

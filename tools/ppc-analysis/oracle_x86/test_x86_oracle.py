"""x86 differential oracle tests (docs/reverse/ORACLE.md, x86 section).

Dev-only tooling. Two layers:

* the PE reader and the identity gate need neither Unicorn nor an executable (synthetic images built here);
* the sandbox tests need Unicorn (`pip install unicorn==2.1.4` in a dev venv) and run synthetic code;
* the oracle tests need Unicorn and the private executables (OPENTPW_TPW_EXE, OPENTPW_TPI_EXE, SHA-256 pinned)
  and skip without them, exactly like the PowerPC oracle skips without the Mac binary.

Every comparison is pinned in EXPECTED: an agreement must stay an agreement, and a known mismatch (a finding,
docs/reverse/ORACLE.md, x86 section) must keep its count and first counterexample until someone fixes it and
updates the table and ORACLE.md together. Gates only get stricter. No executable bytes appear in this file:
the synthetic images are a few instructions written for these tests.
"""
from __future__ import annotations

import os
import struct
import sys
import unittest
from pathlib import Path

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))

import x86_pe  # noqa: E402
import x86_targets as targets  # noqa: E402
from x86_differential import DEFAULT_COUNT, compare  # noqa: E402
from x86_harness import (IDENTIFIED, UNICORN_HINT, BudgetExceeded, IdentityError, Image, MemoryFault,  # noqa: E402
                         Sandbox, UnknownCall, executable_path, have_unicorn, identify)

AGREE = 'agree'
TPW = executable_path('tpw')
TPI = executable_path('tpi')
UNICORN = have_unicorn()
NO_TPI = 'requires the identified TPI Game.exe (OPENTPW_TPI_EXE; tools/ppc-analysis/oracle_x86/README.md)'
NO_TPW = 'requires the identified tp.exe (OPENTPW_TPW_EXE; tools/ppc-analysis/oracle_x86/README.md)'

# (target, side) -> AGREE, or (mismatches, first counterexample input). Seed 'oracle-v1', 500 random + boundaries.
EXPECTED = {
    # X1: tp.exe 0x516330 stores the LCG successor rotated right by 13; the Mac 0x105328 stores it unrotated. Only the
    # state whose successor is 0 (a rotation fixed point) gives the same pair.
    ('tpw-exe-world-rng', 'plan'): (507, 0),
    ('tpw-exe-world-rng', 'routine-reading'): AGREE,
    # X2: the TPI Game.exe 0x6493e0 is the same step as tp.exe 0x516330 (rotation included) at state slot +0x1eab3c.
    ('tpi-exe-world-rng', 'plan'): (507, 0),
    ('tpi-exe-world-rng', 'tpw-pc-reading'): AGREE,
    # X3 (arrival group size 0x4cf5b0 vs the Mac 0xcb4e8, F9): the same rules; 54 of 733 inputs differ, all in the x87
    # arithmetic. 8 are totals whose product is beyond 31 bits (fistp keeps the low word, the Mac saturates); 46 are totals
    # of 1.7 million and up (the x87 truncates the exact product, the Mac rounds it to single first). First: the wrap.
    ('tpw-exe-arrival-group', 'plan'): (54, {'world_mode': 0, 'guests': 0, 'points': 2147483647, 'bonus': 20,
                                              'per_visitor': 6, 'min_people': 1, 'obj48': 0, 'limit_mode': 0}),
    ('tpw-exe-arrival-group', 'routine-reading'): AGREE,
    # X4 (loan offer instalment 0x4cf7c0 vs the Mac 0xcb7a8, F12): the same formula; 505 of 506 offer sets agree with the
    # Mac plan, records and bank words included. The one set holds the out-of-range offers: a zero term (the Mac plan
    # throws, the Mac routine saturates to 2^32-1, the PC gives 0) and a result above 32 bits (the PC keeps the low word).
    ('tpw-exe-loan-offers', 'plan'): (1, {'cash': 0, 'offers': [(100000, 20, 0, 0), (0, 20, 0, 1), (4000000000, 20, 36, 2),
                                                                  (4294967295, 0, 1, 3), (4294967295, 40, 120, 4),
                                                                  (4294967295, 40, 1, 5), (1, 0, 1, 6),
                                                                  (3000000000, 1, 2, 7)]}),
    ('tpw-exe-loan-offers', 'mac-reading'): (1, {'cash': 0, 'offers': [(100000, 20, 0, 0), (0, 20, 0, 1),
                                                                         (4000000000, 20, 36, 2), (4294967295, 0, 1, 3),
                                                                         (4294967295, 40, 120, 4), (4294967295, 40, 1, 5),
                                                                         (1, 0, 1, 6), (3000000000, 1, 2, 7)]}),
    ('tpw-exe-loan-offers', 'routine-reading'): AGREE,
    # X5 (loan monthly step 0x4d0370 vs the Mac 0xcc21c, F13): identical, 503 whole terms and 506 random bank states,
    # assertions and log messages in the same order.
    ('tpw-exe-loan-term', 'plan'): AGREE,
    ('tpw-exe-loan-term', 'routine-reading'): AGREE,
    ('tpw-exe-loan-month', 'routine-reading'): AGREE,
    # X6 (park points 0x4c8240 vs the Mac 0xc24c0, F10): identical on 507 inputs.
    ('tpw-exe-park-points', 'plan'): AGREE,
    # X7 (arrival group size 0x5bcd60 vs the TPW / Mac rule, F9): a different rule. The factor is the weather percentage
    # (state 1 to 4: Heatwave, Rain, Storm, Snow words, else 100) instead of 0.8 / 1.2, the divide is floating (times the
    # single-precision 0.01), and a mode object word of 3 gives no group. 216 of 532 inputs differ from the TPW rule; the
    # routine reading agrees on all, and exact arithmetic differs on 30 (the 0.01f truncation, first: 80 + 20 points over 25).
    ('tpi-exe-arrival-group', 'plan'): (216, {'world_mode': 0, 'mode': 3, 'guests': 0, 'points': 0, 'bonus': 20,
                                              'per_visitor': 25, 'min_people': 1, 'weather': 0, 'pct': (90, 80, 75, 120),
                                              'limit_mode': 0}),
    ('tpi-exe-arrival-group', 'routine-reading'): AGREE,
    ('tpi-exe-arrival-group', 'exact-arithmetic'): (30, {'world_mode': 0, 'mode': 0, 'guests': 0, 'points': 80, 'bonus': 20,
                                                          'per_visitor': 25, 'min_people': 1, 'weather': 0,
                                                          'pct': (90, 80, 75, 120), 'limit_mode': 0}),
    # X8 (loan offer instalment 0x5bfb70 vs F12): the same formula and the same single mismatch as tp.exe (the out-of-range set).
    ('tpi-exe-loan-offers', 'plan'): (1, {'cash': 0, 'offers': [(100000, 20, 0, 0), (0, 20, 0, 1), (4000000000, 20, 36, 2),
                                                                  (4294967295, 0, 1, 3), (4294967295, 40, 120, 4),
                                                                  (4294967295, 40, 1, 5), (1, 0, 1, 6),
                                                                  (3000000000, 1, 2, 7)]}),
    ('tpi-exe-loan-offers', 'mac-reading'): (1, {'cash': 0, 'offers': [(100000, 20, 0, 0), (0, 20, 0, 1),
                                                                         (4000000000, 20, 36, 2), (4294967295, 0, 1, 3),
                                                                         (4294967295, 40, 120, 4), (4294967295, 40, 1, 5),
                                                                         (1, 0, 1, 6), (3000000000, 1, 2, 7)]}),
    ('tpi-exe-loan-offers', 'routine-reading'): AGREE,
    # X9 (loan monthly step 0x5c0b10 vs F13): identical state changes; the release build has no assertion calls, so the
    # reading compared here is the Mac one without its two assertion events.
    ('tpi-exe-loan-term', 'plan'): AGREE,
    ('tpi-exe-loan-term', 'routine-reading'): AGREE,
    ('tpi-exe-loan-month', 'routine-reading'): AGREE,
    # X10 (park points 0x5b05d0 vs F10): identical on 507 inputs, fields at other offsets.
    ('tpi-exe-park-points', 'plan'): AGREE,
}

BASE = 0x400000
TEXT, DATA, IDATA, RELOC = 0x1000, 0x2000, 0x3000, 0x4000


def build_pe(code: bytes, data: bytes = b'', relocs: tuple[int, ...] = ()) -> bytes:
    """A tiny synthetic PE32: .text at 0x1000, .data at 0x2000, .idata at 0x3000 (KERNEL32.dll: Sleep at 0x3040,
    GetTickCount at 0x3044), optional .reloc at 0x4000 holding HIGHLOW fixups for the given RVAs."""
    def section(raw: bytes, size: int = 0x200) -> bytes:
        return raw.ljust(size, b'\0')
    idata = bytearray(0x200)
    struct.pack_into('<IIIII', idata, 0, IDATA + 0x30, 0, 0, IDATA + 0x90, IDATA + 0x40)   # descriptor; null follows
    struct.pack_into('<III', idata, 0x30, IDATA + 0x60, IDATA + 0x70, 0)                    # lookup table
    struct.pack_into('<III', idata, 0x40, IDATA + 0x60, IDATA + 0x70, 0)                    # address table
    idata[0x60:0x60 + 8] = b'\0\0Sleep\0\0'
    idata[0x70:0x70 + 15] = b'\0\0GetTickCount'
    idata[0x90:0x90 + 13] = b'KERNEL32.dll\0'
    reloc = bytearray()
    for page in sorted({rva & ~0xFFF for rva in relocs}):   # one block per 4 KiB page, like a real .reloc
        entries = [(3 << 12) | (rva & 0xFFF) for rva in relocs if rva & ~0xFFF == page]
        reloc += struct.pack('<II', page, 8 + 2 * len(entries)) + struct.pack(f'<{len(entries)}H', *entries)
    sections = [(b'.text', TEXT, code, 0x60000020), (b'.data', DATA, data, 0xC0000040), (b'.idata', IDATA, bytes(idata), 0xC0000040)]
    if reloc:
        sections.append((b'.reloc', RELOC, bytes(reloc), 0x42000040))
    headers = bytearray(0x400)
    headers[:2] = b'MZ'
    struct.pack_into('<I', headers, 0x3C, 0x80)
    pe = 0x80
    headers[pe:pe + 4] = b'PE\0\0'
    optional_size = 0xE0
    struct.pack_into('<HHIIIHH', headers, pe + 4, 0x14C, len(sections), 0, 0, 0, optional_size, 0x102)
    optional = pe + 24
    struct.pack_into('<H', headers, optional, 0x10B)
    struct.pack_into('<I', headers, optional + 16, TEXT)           # entry
    struct.pack_into('<I', headers, optional + 28, BASE)           # image base
    struct.pack_into('<II', headers, optional + 32, 0x1000, 0x200)
    struct.pack_into('<I', headers, optional + 56, 0x5000)         # size of image
    struct.pack_into('<I', headers, optional + 60, 0x400)          # size of headers
    struct.pack_into('<II', headers, optional + 96 + 8 * 1, IDATA, 0x28)
    if reloc:
        struct.pack_into('<II', headers, optional + 96 + 8 * 5, RELOC, len(reloc))
    table = optional + optional_size
    body = b''
    for i, (name, rva, raw, flags) in enumerate(sections):
        struct.pack_into('<8sIIIIIIHHI', headers, table + 40 * i, name, 0x200, rva, 0x200, 0x400 + len(body), 0, 0, 0, 0, flags)
        body += section(raw)
    return bytes(headers) + body


def slot(rva: int, base: int = BASE) -> bytes:
    return struct.pack('<I', base + rva)


# thiscall: eax = [ecx + 8] + arg0, callee pops
THISCALL = bytes.fromhex('8b4108' '03442404' 'c20400')
# cdecl: eax = arg0 + arg1, caller pops
CDECL = bytes.fromhex('8b442404' '03442408' 'c3')
SPIN = bytes.fromhex('ebfe')


class PeReader(unittest.TestCase):
    """No Unicorn needed."""

    def test_sections_imports_and_traps(self):
        image = Image(build_pe(THISCALL), identity='synthetic')
        pe = image.pe
        self.assertEqual((pe.preferred_base, pe.entry_rva), (BASE, TEXT))
        self.assertEqual([s.name for s in pe.sections], ['.text', '.data', '.idata'])
        self.assertEqual([i.key for i in pe.imports], ['kernel32.dll!Sleep', 'kernel32.dll!GetTickCount'])
        slots = struct.unpack_from('<2I', image.data, IDATA + 0x40)
        self.assertEqual(slots[0], image.import_trap('KERNEL32.DLL!Sleep'))
        self.assertEqual(slots[1], image.import_trap('kernel32.dll!GetTickCount'))
        self.assertNotEqual(slots[0], slots[1])
        with self.assertRaises(IdentityError):
            image.import_trap('kernel32.dll!sleep')   # symbol names are exact

    def test_relocation_moves_absolute_operands(self):
        code = b'\xA1' + slot(DATA) + b'\xC3'     # mov eax, [data]
        image = Image(build_pe(code, relocs=(TEXT + 1,)), base=0x10000000, identity='synthetic')
        self.assertEqual(struct.unpack_from('<I', image.data, TEXT + 1)[0], 0x10000000 + DATA)

    def test_fixups_past_the_image_end_are_skipped(self):
        """The TPI image lists 16 fixups beyond its SizeOfImage; a relocated load must skip them, not crash."""
        code = b'\xA1' + slot(DATA) + b'\xC3'
        image = Image(build_pe(code, relocs=(TEXT + 1, 0x5000 + 0x202, 0x5000 + 0x207)), base=0x10000000,
                      identity='synthetic')
        self.assertEqual(struct.unpack_from('<I', image.data, TEXT + 1)[0], 0x10000000 + DATA)
        self.assertEqual(image.skipped_relocations, 2)

    def test_image_size_need_not_be_page_aligned(self):
        raw = bytearray(build_pe(THISCALL))
        struct.pack_into('<I', raw, 0x80 + 24 + 56, 0x4A9B)   # SizeOfImage like the TPI image (ends mid-page)
        image = Image(bytes(raw), identity='synthetic')
        self.assertEqual(len(image.data), 0x5000)

    def test_unknown_executable_is_refused(self):
        with self.assertRaises(IdentityError):
            Image(build_pe(THISCALL))
        with self.assertRaises(IdentityError):
            identify(b'MZ' + bytes(100))

    def test_identified_hashes_are_the_documented_ones(self):
        self.assertEqual(IDENTIFIED['tpw']['sha256'][:16], '5472f97b5eb28f58')
        self.assertEqual(IDENTIFIED['tpi']['sha256'][:16], '77ea0c41410aad7c')
        self.assertEqual((IDENTIFIED['tpw']['size'], IDENTIFIED['tpi']['size']), (3_734_528, 6_742_016))

    def test_non_pe_is_rejected(self):
        with self.assertRaises(x86_pe.PeError):
            x86_pe.parse(b'ELF' + bytes(100))


@unittest.skipUnless(UNICORN, UNICORN_HINT)
class SandboxSynthetic(unittest.TestCase):
    """The harness on synthetic code (no original binary)."""

    def sandbox(self, code: bytes, data: bytes = b'', allowed=None, stubs=None, budget=1000, base=None):
        image = Image(build_pe(code, data), base=base, identity='synthetic')
        entry = (BASE if base is None else base) + TEXT
        sb = Sandbox(image, allowed if allowed is not None else [(entry, entry + len(code))], stubs, budget)
        sb.reset()
        return sb, entry

    def test_thiscall_with_stack_argument(self):
        sb, entry = self.sandbox(THISCALL)
        obj = sb.object(64, {8: ('I', 40)})
        result = sb.call(entry, (2,), this=obj)
        self.assertEqual((result.eax, result.esp_delta), (42, 0))

    def test_cdecl_leaves_the_arguments_to_the_caller(self):
        sb, entry = self.sandbox(CDECL)
        result = sb.call(entry, (0xFFFFFFFF, 3))
        self.assertEqual((result.eax, result.esp_delta), (2, -8))

    def test_budget_bounds_a_run(self):
        sb, entry = self.sandbox(SPIN, budget=50)
        with self.assertRaises(BudgetExceeded):
            sb.call(entry)

    def test_call_outside_allowed_ranges_aborts(self):
        code = b'\xE8' + struct.pack('<i', 0x10) + b'\xC3'     # call +0x10: lands past the allowed range
        sb, entry = self.sandbox(code + bytes(0x20))
        sb = Sandbox(sb.image, [(entry, entry + 6)], {}, 1000)
        sb.reset()
        with self.assertRaises(UnknownCall):
            sb.call(entry)

    def test_unstubbed_import_aborts_and_a_stub_answers(self):
        code = b'\xFF\x15' + slot(IDATA + 0x44) + b'\xC3'   # call [GetTickCount]; ret
        sb, entry = self.sandbox(code)
        with self.assertRaises(UnknownCall) as caught:
            sb.call(entry)
        self.assertIn('GetTickCount', str(caught.exception))
        sb, entry = self.sandbox(code, stubs={'kernel32.dll!GetTickCount': lambda s: (1234, 0)})
        self.assertEqual(sb.call(entry).eax, 1234)

    def test_stub_pops_stdcall_arguments_and_reads_them(self):
        seen = []
        code = b'\x6A\x07' + b'\xFF\x15' + slot(IDATA + 0x40) + b'\xC3'   # push 7; call [Sleep]; ret
        sb, entry = self.sandbox(code, stubs={'kernel32.dll!Sleep': lambda s: (seen.append(s.arg(0)) or 0, 4)})
        result = sb.call(entry)
        self.assertEqual((seen, result.esp_delta), ([7], 0))

    def test_memory_is_pristine_for_every_run(self):
        # mov eax, [data]; mov dword [data], 9; mov dword [ecx], 5; ret
        code = b'\xA1' + slot(DATA) + b'\xC7\x05' + slot(DATA) + struct.pack('<I', 9) + b'\xC7\x01' + struct.pack('<I', 5) + b'\xC3'
        sb, entry = self.sandbox(code, struct.pack('<I', 7))
        obj = sb.object(16)
        self.assertEqual(sb.call(entry, this=obj).eax, 7)
        self.assertEqual((sb.read(BASE + DATA, 'I'), sb.read(obj, 'I')), (9, 5))
        sb.reset()
        obj = sb.object(16)
        self.assertEqual((sb.read(BASE + DATA, 'I'), sb.read(obj, 'I')), (7, 0))
        self.assertEqual(sb.call(entry, this=obj).eax, 7)

    def test_unmapped_access_is_a_memory_fault(self):
        sb, entry = self.sandbox(b'\xA1' + struct.pack('<I', 0x30000000) + b'\xC3')
        with self.assertRaises(MemoryFault):
            sb.call(entry)

    def test_x87_keeps_extended_precision_and_is_reset(self):
        one, tiny = struct.pack('<d', 1.0), struct.pack('<d', 2.0 ** -60)
        # fld qword [one]; fadd qword [tiny]; fsub qword [one]; fstp qword [out]; fld1; ret (leaves ST0 occupied)
        code = (b'\xDD\x05' + slot(DATA) + b'\xDC\x05' + slot(DATA + 8) + b'\xDC\x25' + slot(DATA) +
                b'\xDD\x1D' + slot(DATA + 16) + b'\xD9\xE8' + b'\xC3')
        sb, entry = self.sandbox(code, one + tiny)
        sb.call(entry)
        self.assertEqual(sb.read(BASE + DATA + 16, 'd'), 2.0 ** -60)
        self.assertNotEqual(sb.x87_state()['fptag'], 0xFFFF)
        sb.reset()
        self.assertEqual(sb.x87_state(), {'fpcw': 0x37F, 'fpsw': 0, 'fptag': 0xFFFF})

    def test_relocated_image_runs(self):
        # assembled for BASE; the loader applies the fixup for the new base
        code = b'\xA1' + slot(DATA) + b'\xC3'
        image = Image(build_pe(code, struct.pack('<I', 77), relocs=(TEXT + 1,)), base=0x10000000, identity='synthetic')
        sb = Sandbox(image, [(0x10000000 + TEXT, 0x10000000 + TEXT + 6)], {}, 100)
        sb.reset()
        self.assertEqual(sb.call(0x10000000 + TEXT).eax, 77)


@unittest.skipUnless(UNICORN, UNICORN_HINT)
@unittest.skipUnless(TPW, NO_TPW)
class Oracle(unittest.TestCase):
    exe, path = 'tpw', TPW

    @classmethod
    def setUpClass(cls):
        cls.image = Image.load(cls.path)
        cls.results = {}
        for target in targets.TARGETS:
            if target.exe != cls.exe:
                continue
            for comparison in compare(target, cls.image, DEFAULT_COUNT):
                cls.results[(target.key, comparison.side)] = comparison

    def check(self, key):
        comparison = self.results[key]
        expected = EXPECTED[key]
        if expected == AGREE:
            self.assertTrue(comparison.agrees, f'{key}: {comparison.mismatches} mismatches, first {comparison.first}')
        else:
            count, first_input = expected
            self.assertEqual(comparison.mismatches, count, f'{key}: first {comparison.first}')
            self.assertEqual(comparison.first['input'], first_input, key)

    def test_every_comparison_is_pinned(self):
        self.assertEqual(set(self.results), {key for key in EXPECTED if key[0].startswith(self.exe + '-')})

    def test_identity_is_pinned(self):
        self.assertEqual(self.image.identity, self.exe)

    def test_world_rng_mac_plan_differs(self):
        self.check(('tpw-exe-world-rng', 'plan'))

    def test_world_rng_routine_reading_agrees(self):
        self.check(('tpw-exe-world-rng', 'routine-reading'))

    def test_world_rng_fixed_point_is_the_only_agreement(self):
        """The one agreeing input is the state whose successor is 0, the rotation's fixed point."""
        target = targets.WORLD_RNG_TARGET
        inputs = targets.world_rng_inputs(__import__('x86_harness').seeded('oracle-v1:' + target.key), DEFAULT_COUNT)
        sandbox = target.sandbox(self.image)
        agreeing = [x for x in inputs if targets.run_binary(target, sandbox, x) == targets.world_rng_plan(x)]
        self.assertEqual(agreeing, [(0 - 1013904223) * pow(1664525, -1, 1 << 32) & 0xFFFFFFFF])

    def test_mutated_reading_is_caught(self):
        """One operand changed in a reference (the rotation count) turns an agreement into a mismatch."""
        def mutated(state):
            new = targets.ror32((state * 1664525 + 1013904223) & 0xFFFFFFFF, 12)
            return targets.det_model.wrapping_abs32(new), new
        comparison = compare(targets.WORLD_RNG_TARGET, self.image, 50, plan=mutated)[0]
        self.assertGreater(comparison.mismatches, 0)

    def test_unstubbed_call_aborts_the_run(self):
        """The same routine with an allowed range one instruction short: the harness refuses the overrun."""
        sandbox = Sandbox(self.image, [(targets.TPW_RNG_STEP, targets.TPW_RNG_STEP + 0x10)], {})
        sandbox.reset()
        obj = sandbox.object(targets.WORLD_OBJECT_SIZE)
        with self.assertRaises(UnknownCall):
            sandbox.call(targets.TPW_RNG_STEP, this=obj)

    # ------------------------------------------------------------------ X3: arrival group size
    def run_target(self, target, x, **kwargs):
        sandbox = target.sandbox(self.image)
        sandbox.reset()
        target.log.clear()
        return target.binary(sandbox, x, target.log, **kwargs)

    def test_arrival_differences_are_the_x87_arithmetic_only(self):
        """Every mismatch with the Mac plan is either a product beyond 31 bits (fistp keeps the low word, fctiwz saturates)
        or a total of 1.7 million and up (exact product truncated, against a product rounded to single first)."""
        target = targets.ARRIVAL_TARGET
        wrap, rounding, smallest = 0, 0, {}
        sandbox = target.sandbox(self.image)
        for x in target.inputs(__import__('x86_harness').seeded('oracle-v1:' + target.key), DEFAULT_COUNT):
            if targets.run_binary(target, sandbox, x) == targets.arrival_plan(x):
                continue
            total, factor = targets.s32(x['points'] + x['bonus']), 0.8 if x['obj48'] > 0 else 1.2
            if abs(total * factor) >= 2 ** 31:
                wrap += 1
            else:
                rounding += 1
                smallest[x['obj48'] > 0] = min(smallest.get(x['obj48'] > 0, total), total)
        self.assertEqual((wrap, rounding), (8, 46))
        self.assertGreaterEqual(smallest[False], 1747629)
        self.assertGreaterEqual(smallest[True], 5242881)

    def test_arrival_first_rounding_difference(self):
        """Total 1747629 with the 1.2 factor: the exact product truncates to 2097154, the Mac's single-precision product
        rounds up to 2097155. A divisor of 2097155 and MinPeople 0 make the group 0 on the PC and 1 on the Mac."""
        shipped = dict(world_mode=0, guests=0, points=0, bonus=0, per_visitor=6, min_people=0, obj48=0, limit_mode=0)
        target = targets.ARRIVAL_TARGET
        for points, divisor, mac_group in ((1747629, 2097155, 1), (1747628, 2097155, 0)):
            x = {**shipped, 'points': points, 'per_visitor': divisor}
            self.assertEqual((self.run_target(target, x), targets.arrival_plan(x)), (0, mac_group))
        x = {**shipped, 'points': 5242881, 'obj48': 1, 'per_visitor': 4194305}
        self.assertEqual((self.run_target(target, x), targets.arrival_plan(x)), (0, 1))

    def test_arrival_follows_the_control_word(self):
        """The result depends on the x87 precision control: each setting agrees with the reading at that precision, 53 and
        64 bits give the same groups on these inputs, and 24 bits (single) agree with the Mac's rounding of the product."""
        target = targets.ARRIVAL_TARGET
        inputs = target.inputs(__import__('x86_harness').seeded('oracle-v1:' + target.key), DEFAULT_COUNT)
        sandbox = target.sandbox(self.image)
        counts = {}
        for control, precision in ((0x27F, 53), (0x37F, 64), (0x7F, 24)):
            wrong = 0
            for x in inputs:
                sandbox.reset()
                target.log.clear()
                wrong += targets.arrival_binary(sandbox, x, target.log, fpcw=control) != targets.arrival_reading(x, precision)
            counts[control] = wrong
        self.assertEqual(counts, {0x27F: 0, 0x37F: 0, 0x7F: 0})
        self.assertGreater(sum(targets.arrival_reading(x, 24) != targets.arrival_reading(x, 53) for x in inputs), 0)

    def test_arrival_agrees_with_the_mac_in_the_reachable_range(self):
        """Park points beyond a million are out of reach; below that the PC and the Mac group sizes are identical."""
        target = targets.ARRIVAL_TARGET
        sandbox = target.sandbox(self.image)
        inputs = [x for x in target.inputs(__import__('x86_harness').seeded('oracle-v1:' + target.key), DEFAULT_COUNT)
                  if -1_000_000 < x['points'] + x['bonus'] < 1_000_000]
        self.assertGreater(len(inputs), 500)
        self.assertEqual([x for x in inputs if targets.run_binary(target, sandbox, x) != targets.arrival_plan(x)], [])

    def test_arrival_reading_is_sharp(self):
        """The factor test, the online cut and the divisor floor each matter."""
        def swapped(x):
            return targets.arrival_reading({**x, 'obj48': 1 - (1 if x['obj48'] > 0 else 0)})

        def no_online_cut(x):
            return targets.arrival_reading({**x, 'limit_mode': 0 if x['limit_mode'] == 1 else x['limit_mode']})

        def no_divisor_floor(x):
            return targets.arrival_reading({**x, 'per_visitor': x['per_visitor'] if x['per_visitor'] else 1})
        for name, mutated in (('factor', swapped), ('online cut', no_online_cut)):
            comparison = compare(targets.ARRIVAL_TARGET, self.image, 100, plan=mutated)[0]
            self.assertGreater(comparison.mismatches, 0, name)
        shipped = dict(world_mode=0, guests=0, points=0, bonus=20, per_visitor=1, min_people=1, obj48=1, limit_mode=0)
        self.assertEqual(self.run_target(targets.ARRIVAL_TARGET, shipped), 16)           # 0.8 * 20
        self.assertEqual(self.run_target(targets.ARRIVAL_TARGET, {**shipped, 'obj48': 0}), 24)   # 1.2 * 20
        self.assertEqual(self.run_target(targets.ARRIVAL_TARGET, {**shipped, 'per_visitor': -9}), 16)

    # ------------------------------------------------------------------ X4: loan offers
    def test_loan_offers_shipped_instalments_at_every_control_word(self):
        target = targets.LOAN_OFFERS_TARGET
        shipped = {'cash': 100000, 'offers': targets.mac().SHIPPED_LOANS}
        for control in (0x27F, 0x37F, 0x7F):
            result = self.run_target(target, shipped, fpcw=control)
            self.assertEqual(result['monthly'], targets.mac().SHIPPED_MONTHLY, hex(control))

    def test_loan_offers_depend_on_the_control_word_where_the_instalment_is_an_integer(self):
        """18000 over 24 months at 20 % is exactly 900: pow(1.2, 1) in 53 bits (the Mac's doubles) truncates to 900, in the
        64-bit extended default of the emulator to 899 (13 of 506 offer sets differ between the two)."""
        target = targets.LOAN_OFFERS_TARGET
        x = {'cash': 0, 'offers': [(18000, 20, 24, 0)] * 8}
        self.assertEqual(self.run_target(target, x)['monthly'][0], 900)
        self.assertEqual(self.run_target(target, x, fpcw=0x37F)['monthly'][0], 899)
        inputs = target.inputs(__import__('x86_harness').seeded('oracle-v1:' + target.key), DEFAULT_COUNT)
        different = 0
        for x in inputs:
            a = self.run_target(target, x)['monthly']
            b = self.run_target(target, x, fpcw=0x37F)['monthly']
            different += a != b
        self.assertEqual(different, 13)

    def test_loan_pow_is_executed(self):
        """The power function is the executable's own x87 routine (no import and no stub)."""
        target = targets.LOAN_OFFERS_TARGET
        self.assertTrue(any(lo <= 0x67C5D0 < hi for lo, hi in target.allowed))
        self.assertEqual(set(target.stubs(target)), {targets.LOAN_BASE_CTOR})

    def test_loan_offers_out_of_range_cases(self):
        """A zero term gives 0 (the Mac saturates to 2^32-1), 0xFFFFFFFF at 40 % over one month keeps the low word of a
        result above 32 bits (the Mac saturates)."""
        target = targets.LOAN_OFFERS_TARGET
        x = {'cash': 0, 'offers': [(100000, 20, 0, 0), (0, 20, 0, 1), (0xFFFFFFFF, 40, 1, 2), (0xFFFFFFFF, 0, 1, 3)] +
             [(1, 0, 1, 4)] * 4}
        original = self.run_target(target, x)['monthly'][:4]
        self.assertEqual(original, [0, 0, 60638121, 0xFFFFFFFF])
        self.assertEqual(targets.mac().loan_offers_reading(x)['monthly'][:4], [0xFFFFFFFF, 0xFFFFFFFF, 0xFFFFFFFF, 0xFFFFFFFF])

    def test_loan_offers_reading_is_sharp(self):
        def exponent_in_years(x):
            result = targets.loan_offers_reading(x)
            result['monthly'] = [targets.mac().loan_monthly_reading(a, r, n, exponent=1.0) for a, r, n, _ in x['offers']]
            return result

        def annuity(x):
            result = targets.loan_offers_reading(x)
            result['monthly'] = [targets.mac().loan_annuity_plan(a, r, n) for a, r, n, _ in x['offers']]
            return result
        for mutated in (exponent_in_years, annuity):
            comparison = compare(targets.LOAN_OFFERS_TARGET, self.image, 50, plan=mutated)[0]
            self.assertGreater(comparison.mismatches, 0)

    # ------------------------------------------------------------------ X5: loan monthly step
    def test_loan_step_reading_is_sharp(self):
        """Dropping any one part of the reading brings mismatches back."""
        for part in ('share', 'withdraw-flag', 'red', 'clear', 'batch', 'profit-debit'):
            comparison = compare(targets.LOAN_MONTH_TARGET, self.image, 100,
                                 plan=lambda x, part=part: targets.mac().loan_step_reading(x, drop=part))[0]
            self.assertGreater(comparison.mismatches, 0, part)

    def test_loan_term_deducts_the_instalment_exactly_n_times(self):
        """100,000 over 36 months at 20 % (M = 3651): 36 deductions of 3651 and the year profit falling by 873 a month."""
        target = targets.LOAN_TERM_TARGET
        loan = {'amount': 100000, 'apr': 20, 'months': 36, 'monthly': 3651, 'bought': 1, 'repaid': 0}
        idle = targets.mac().LOAN_IDLE
        x = {'bal': 1000000, 'batch': 0, 'wd': 1, 'last': 1000000, 'red': 0, 'profit': 0, 'loans': [loan] + [idle] * 7,
             'turns': [800 * k for k in range(38)]}
        result = self.run_target(target, x)
        self.assertEqual(result['balance'][35], 1000000 - 36 * 3651)
        self.assertEqual(result['balance'][37], 1000000 - 36 * 3651)
        self.assertEqual(result['profit'][0], -873)
        self.assertEqual(result['profit'][35], -36 * 873)
        self.assertEqual((result['active'][34], result['active'][35]), (1, 0))

    # ------------------------------------------------------------------ X6: park points
    def test_points_reading_is_sharp(self):
        """Each of the three Mac rules matters: the track-type branch, the first-purchase age and the counts-test."""
        def flipped(x):
            return targets.mac().points_full({**x, 'types': [{**k, 'f9c': 0 if k['f9c'] else 1} for k in x['types']]})

        def every_thing_counts(x):
            return targets.mac().points_full({**x, 'things': [{**t, 'open': True} for t in x['things']]})

        def own_age_always(x):
            return targets.mac().points_full({**x, 'types': [{**k, 'f9c': 1} for k in x['types']]})
        for name, mutated in (('track type', flipped), ('counts-test', every_thing_counts), ('first purchase', own_age_always)):
            comparison = compare(targets.POINTS_TARGET, self.image, 100, plan=mutated)[0]
            self.assertGreater(comparison.mismatches, 0, name)

    def test_points_copies_age_from_the_first_purchase(self):
        """Two copies of an untracked ride bought three days apart: both age from the first purchase and share the value
        (25 + 30 each, divided by 2 copies), as on the Mac; with the track-type word set each copy ages on its own."""
        day = 86400 * 4 // 15000 + 1
        ride = dict(id=0x101, kind=0, f9c=0, value=25, decay=2, bonus=[30, 20, 10], first=1)
        copies = [{'type': 0x101, 'open': True, 'built': 1}, {'type': 0x101, 'open': True, 'built': 1 + 3 * day}]
        x = {'now': 1 + 3 * day, 'scale': 15000, 'types': [ride], 'things': copies}
        target = targets.POINTS_TARGET
        self.assertEqual(self.run_target(target, x), targets.mac().points_full(x))
        tracked = {**x, 'types': [{**ride, 'f9c': 1}]}
        self.assertEqual(self.run_target(target, tracked), targets.mac().points_full(tracked))
        self.assertNotEqual(self.run_target(target, x), self.run_target(target, tracked))

    def test_original_hash_refuses_a_modified_copy(self):
        raw = bytearray(TPW.read_bytes())
        raw[0x1000] ^= 1
        with self.assertRaises(IdentityError):
            Image(bytes(raw))


@unittest.skipUnless(UNICORN, UNICORN_HINT)
@unittest.skipUnless(TPI, NO_TPI)
class OracleTpi(unittest.TestCase):
    """X2: the TPI Game.exe world RNG step (the image has a relocation directory and an .idata section); X7 to X10: the
    TPI counterparts of the arrival, loan and park-points rules."""

    @classmethod
    def setUpClass(cls):
        cls.image = Image.load(TPI)
        cls.results = {}
        for target in targets.TARGETS:
            if target.exe == 'tpi':
                cls.results.update({(target.key, c.side): c for c in compare(target, cls.image, DEFAULT_COUNT)})

    def check(self, key):
        comparison = self.results[key]
        expected = EXPECTED[key]
        if expected == AGREE:
            self.assertTrue(comparison.agrees, f'{key}: {comparison.mismatches} mismatches, first {comparison.first}')
        else:
            count, first_input = expected
            self.assertEqual(comparison.mismatches, count, f'{key}: first {comparison.first}')
            self.assertEqual(comparison.first['input'], first_input, key)

    def run_target(self, target, x, **kwargs):
        sandbox = target.sandbox(self.image)
        sandbox.reset()
        target.log.clear()
        return target.binary(sandbox, x, target.log, **kwargs)

    def test_every_comparison_is_pinned(self):
        self.assertEqual(set(self.results), {key for key in EXPECTED if key[0].startswith('tpi-')})

    def test_every_pinned_tpi_comparison_holds(self):
        for key in EXPECTED:
            if key[0].startswith('tpi-'):
                self.check(key)

    # ------------------------------------------------------------------ X7: arrival group size
    def test_arrival_factor_is_the_weather_percentage(self):
        """The weather thing's state 1 to 4 picks Heatwave, Rain, Storm, Snow (jump table); 0 and out-of-range states give 100.
        Points 480 + bonus 20 over 25 (shipped settings): 100 % gives 19 (19.99999), 120 % 23, 90 % 17, 80 % 15, 75 % 14."""
        shipped = dict(world_mode=0, mode=0, guests=0, points=480, bonus=20, per_visitor=25, min_people=1, weather=0,
                       pct=targets.TPI_SHIPPED_PCT, limit_mode=0)
        target = targets.TPI_ARRIVAL_TARGET
        groups = {w: self.run_target(target, {**shipped, 'weather': w}) for w in (-1, 0, 1, 2, 3, 4, 5, 0x7FFFFFFF)}
        self.assertEqual(groups, {-1: 19, 0: 19, 1: 23, 2: 17, 3: 15, 4: 14, 5: 19, 0x7FFFFFFF: 19})
        self.assertEqual(targets.TPI_SHIPPED_PCT, (90, 80, 75, 120))   # the shipped Rain, Storm, Snow, Heatwave words
        # the TPW rule on the same state: 1.2 without weather, 0.8 with (any positive word), a different group
        self.assertEqual([targets.tpi_arrival_tpw_plan({**shipped, 'weather': w}) for w in (0, 1, 4)], [24, 16, 16])

    def test_arrival_mode_object_word_three_gives_no_group(self):
        target = targets.TPI_ARRIVAL_TARGET
        x = dict(world_mode=0, mode=3, guests=0, points=480, bonus=20, per_visitor=25, min_people=1, weather=0,
                 pct=targets.TPI_SHIPPED_PCT, limit_mode=0)
        self.assertEqual(self.run_target(target, x), 0)
        self.assertEqual(self.run_target(target, {**x, 'mode': 2}), 19)
        self.assertEqual(self.run_target(target, {**x, 'mode': 3, 'world_mode': 4}), 0)
        self.assertEqual(targets.tpi_arrival_tpw_plan(x), 24)   # the TPW rule has no such exit

    def test_arrival_float_divide_truncates_below_exact_multiples(self):
        """The factor is the integer percentage times the single-precision 0.01 (0.0099999998), so a total that is an exact
        multiple of the divisor gives one group less: 80 points + 20 over 25 is 3, not 4. 30 of 532 inputs differ from the
        exact arithmetic, all by one."""
        shipped = dict(world_mode=0, mode=0, guests=0, points=80, bonus=20, per_visitor=25, min_people=1, weather=0,
                       pct=targets.TPI_SHIPPED_PCT, limit_mode=0)
        target = targets.TPI_ARRIVAL_TARGET
        self.assertEqual((self.run_target(target, shipped), targets.tpi_arrival_exact_reading(shipped)), (3, 4))
        inputs = target.inputs(__import__('x86_harness').seeded('oracle-v1:' + target.key), DEFAULT_COUNT)
        sandbox = target.sandbox(self.image)
        deltas = [targets.tpi_arrival_exact_reading(x) - targets.run_binary(target, sandbox, x) for x in inputs]
        self.assertEqual({d for d in deltas if d}, {1})
        self.assertEqual(sum(1 for d in deltas if d), 30)

    def test_arrival_follows_the_control_word(self):
        """Each x87 precision agrees with the reading at that precision; 53 and 64 bits give the same groups on these
        inputs, 24 bits (single) differ."""
        target = targets.TPI_ARRIVAL_TARGET
        inputs = target.inputs(__import__('x86_harness').seeded('oracle-v1:' + target.key), DEFAULT_COUNT)
        sandbox = target.sandbox(self.image)
        groups = {}
        for control, precision in ((0x27F, 53), (0x37F, 64), (0x7F, 24)):
            results = []
            for x in inputs:
                sandbox.reset()
                target.log.clear()
                results.append(targets.tpi_arrival_binary(sandbox, x, target.log, fpcw=control))
            groups[control] = results
            self.assertEqual(results, [targets.tpi_arrival_reading(x, precision) for x in inputs], hex(control))
        self.assertEqual(groups[0x27F], groups[0x37F])
        self.assertNotEqual(groups[0x27F], groups[0x7F])

    def test_arrival_reading_is_sharp(self):
        """The slot of each weather state, the percentage constant, the mode exit and the online cut each matter."""
        def slots_in_table_order(x):
            return targets.tpi_arrival_reading({**x, 'weather': {1: 2, 2: 3, 3: 4, 4: 1}.get(x['weather'], x['weather'])})

        def tpw_factor(x):   # the TPW factor and integer divide, with the TPI mode exit kept
            return 0 if x['mode'] == 3 else targets.tpi_arrival_tpw_plan(x)

        def no_mode_exit(x):
            return targets.tpi_arrival_reading({**x, 'mode': 0})

        def no_online_cut(x):
            return targets.tpi_arrival_reading({**x, 'limit_mode': 0 if x['limit_mode'] == 1 else x['limit_mode']})
        for name, mutated in (('slots', slots_in_table_order), ('TPW factor', tpw_factor), ('mode exit', no_mode_exit),
                              ('online cut', no_online_cut)):
            comparison = compare(targets.TPI_ARRIVAL_TARGET, self.image, 100, plan=mutated)[0]
            self.assertGreater(comparison.mismatches, 0, name)

    # ------------------------------------------------------------------ X8: loan offers
    def test_loan_offers_shipped_instalments_at_every_control_word(self):
        target = targets.TPI_LOAN_OFFERS_TARGET
        shipped = {'cash': 50000, 'offers': targets.mac().SHIPPED_LOANS}   # the LoanInfo values of the TPI Standard.sam
        for control in (0x27F, 0x37F, 0x7F):
            self.assertEqual(self.run_target(target, shipped, fpcw=control)['monthly'], targets.mac().SHIPPED_MONTHLY,
                             hex(control))

    def test_loan_offers_depend_on_the_control_word_where_the_instalment_is_an_integer(self):
        """As in tp.exe: 18000 over 24 months at 20 % is exactly 900 at 0x27F and 899 at 0x37F; 16 of this target's 506 offer
        sets change between the two words."""
        target = targets.TPI_LOAN_OFFERS_TARGET
        x = {'cash': 0, 'offers': [(18000, 20, 24, 0)] * 8}
        self.assertEqual(self.run_target(target, x)['monthly'][0], 900)
        self.assertEqual(self.run_target(target, x, fpcw=0x37F)['monthly'][0], 899)
        inputs = target.inputs(__import__('x86_harness').seeded('oracle-v1:' + target.key), DEFAULT_COUNT)
        different = sum(self.run_target(target, x)['monthly'] != self.run_target(target, x, fpcw=0x37F)['monthly']
                        for x in inputs)
        self.assertEqual(different, 16)

    def test_loan_pow_is_executed(self):
        target = targets.TPI_LOAN_OFFERS_TARGET
        self.assertTrue(any(lo <= 0x871C00 < hi for lo, hi in target.allowed))
        self.assertEqual(set(target.stubs(target)), {targets.TPI_LOAN_BASE_CTOR})

    def test_loan_offers_reading_is_sharp(self):
        def exponent_in_years(x):
            result = targets.loan_offers_reading(x)
            result['monthly'] = [targets.mac().loan_monthly_reading(a, r, n, exponent=1.0) for a, r, n, _ in x['offers']]
            return result
        comparison = compare(targets.TPI_LOAN_OFFERS_TARGET, self.image, 50, plan=exponent_in_years)[0]
        self.assertGreater(comparison.mismatches, 0)

    # ------------------------------------------------------------------ X9: loan monthly step
    def test_loan_step_reading_is_sharp(self):
        for part in ('share', 'withdraw-flag', 'red', 'clear', 'batch', 'profit-debit'):
            def mutated(x, part=part):
                out = targets.mac().loan_step_reading(x, drop=part)
                out['events'] = [[e for e in events if e[0] != 'assert'] for events in out['events']]
                return out
            comparison = compare(targets.TPI_LOAN_MONTH_TARGET, self.image, 100, plan=mutated)[0]
            self.assertGreater(comparison.mismatches, 0, part)

    def test_loan_step_has_no_assertion_events(self):
        """Unlike tp.exe, the handler raises no assertions: the event list holds only the instalment and paid-off lines, and
        the Mac reading with its assertion events does not match."""
        target = targets.TPI_LOAN_MONTH_TARGET
        loan = {'amount': 100000, 'apr': 20, 'months': 36, 'monthly': 3651, 'bought': 1, 'repaid': 0}
        idle = targets.mac().LOAN_IDLE
        x = {'bal': 50000, 'batch': 2000000, 'wd': 1, 'last': 50000, 'red': 0, 'profit': 0, 'loans': [loan] + [idle] * 7,
             'turns': [800]}
        result = self.run_target(target, x)
        self.assertEqual(result['events'], [[('log465', 1, 0, 3651)]])
        self.assertEqual(targets.mac().loan_step_reading(x)['events'][0][0], ('assert', 0, 340))

    def test_loan_term_deducts_the_instalment_exactly_n_times(self):
        target = targets.TPI_LOAN_TERM_TARGET
        loan = {'amount': 100000, 'apr': 20, 'months': 36, 'monthly': 3651, 'bought': 1, 'repaid': 0}
        idle = targets.mac().LOAN_IDLE
        x = {'bal': 1000000, 'batch': 0, 'wd': 1, 'last': 1000000, 'red': 0, 'profit': 0, 'loans': [loan] + [idle] * 7,
             'turns': [800 * k for k in range(38)]}
        result = self.run_target(target, x)
        self.assertEqual(result['balance'][35], 1000000 - 36 * 3651)
        self.assertEqual(result['balance'][37], 1000000 - 36 * 3651)
        self.assertEqual((result['profit'][0], result['profit'][35]), (-873, -36 * 873))
        self.assertEqual((result['active'][34], result['active'][35]), (1, 0))

    # ------------------------------------------------------------------ X10: park points
    def test_points_reading_is_sharp(self):
        def flipped(x):
            return targets.mac().points_full({**x, 'types': [{**k, 'f9c': 0 if k['f9c'] else 1} for k in x['types']]})

        def every_thing_counts(x):
            return targets.mac().points_full({**x, 'things': [{**t, 'open': True} for t in x['things']]})

        def own_age_always(x):
            return targets.mac().points_full({**x, 'types': [{**k, 'f9c': 1} for k in x['types']]})
        for name, mutated in (('track type', flipped), ('counts-test', every_thing_counts), ('first purchase', own_age_always)):
            comparison = compare(targets.TPI_POINTS_TARGET, self.image, 100, plan=mutated)[0]
            self.assertGreater(comparison.mismatches, 0, name)

    def test_points_copies_age_from_the_first_purchase(self):
        day = 86400 * 4 // 15000 + 1
        ride = dict(id=0x101, kind=0, f9c=0, value=25, decay=2, bonus=[30, 20, 10], first=1)
        copies = [{'type': 0x101, 'open': True, 'built': 1}, {'type': 0x101, 'open': True, 'built': 1 + 3 * day}]
        x = {'now': 1 + 3 * day, 'scale': 15000, 'types': [ride], 'things': copies}
        target = targets.TPI_POINTS_TARGET
        self.assertEqual(self.run_target(target, x), targets.mac().points_full(x))
        tracked = {**x, 'types': [{**ride, 'f9c': 1}]}
        self.assertEqual(self.run_target(target, tracked), targets.mac().points_full(tracked))
        self.assertNotEqual(self.run_target(target, x), self.run_target(target, tracked))

    def test_identity_and_loader(self):
        self.assertEqual(self.image.identity, 'tpi')
        self.assertEqual(len(self.image.pe.imports), 393)
        self.assertEqual(len(self.image.pe.relocations()), 144929)
        self.assertEqual(self.image.skipped_relocations, 0)   # preferred base: nothing applied, nothing skipped

    def test_tpi_matches_the_pc_reading_not_the_mac_plan(self):
        for key, expected in (('plan', (507, 0)), ('tpw-pc-reading', AGREE)):
            comparison = self.results[('tpi-exe-world-rng', key)]
            self.assertEqual(EXPECTED[('tpi-exe-world-rng', key)], expected)
            if expected == AGREE:
                self.assertTrue(comparison.agrees, comparison.first)
            else:
                self.assertEqual((comparison.mismatches, comparison.first['input']), expected)

    def test_tpi_state_slot_and_step_equal_the_tpw_pc_step(self):
        """Moving the slot is the only difference: the same input gives the same (returned, stored) pair."""
        target = targets.TPI_WORLD_RNG_TARGET
        sandbox = target.sandbox(self.image)
        inputs = targets.world_rng_inputs(__import__('x86_harness').seeded('oracle-v1:' + target.key), DEFAULT_COUNT)
        self.assertTrue(all(targets.run_binary(target, sandbox, x) == targets.world_rng_routine_reading(x)
                            for x in inputs))
        self.assertEqual((targets.TPI_WORLD_RNG, targets.WORLD_RNG), (0x1EAB3C, 0x1DA708))

    def test_relocated_load_skips_the_fixups_past_the_image(self):
        image = Image.load(TPI, base=0x10000000)
        self.assertEqual(image.skipped_relocations, 16)

    def test_overrun_of_the_allowed_range_aborts(self):
        sandbox = Sandbox(self.image, [(targets.TPI_RNG_STEP, targets.TPI_RNG_STEP + 0x10)], {})
        sandbox.reset()
        obj = sandbox.object(targets.WORLD_OBJECT_SIZE)
        with self.assertRaises(UnknownCall):
            sandbox.call(targets.TPI_RNG_STEP, this=obj)


if __name__ == '__main__':
    unittest.main()

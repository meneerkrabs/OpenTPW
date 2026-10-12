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

    def test_original_hash_refuses_a_modified_copy(self):
        raw = bytearray(TPW.read_bytes())
        raw[0x1000] ^= 1
        with self.assertRaises(IdentityError):
            Image(bytes(raw))


@unittest.skipUnless(UNICORN, UNICORN_HINT)
@unittest.skipUnless(TPI, NO_TPI)
class OracleTpi(unittest.TestCase):
    """X2: the TPI Game.exe world RNG step (the image has a relocation directory and an .idata section)."""

    @classmethod
    def setUpClass(cls):
        cls.image = Image.load(TPI)
        target = targets.TPI_WORLD_RNG_TARGET
        cls.results = {(target.key, c.side): c for c in compare(target, cls.image, DEFAULT_COUNT)}

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

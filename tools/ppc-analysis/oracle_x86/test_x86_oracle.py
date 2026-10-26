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
    # X13 (object scrap value 0x4e2400 / percentage 0x4e2290 vs the Mac 0xe25e8 / 0xe2424, F21): identical on 572 inputs; the
    # three wrong readings (every level's cost, no first-month refund, the base level's row) are each caught.
    ('tpw-exe-scrap-value', 'plan'): AGREE,
    # X14 (queue re-evaluation 0x501390 vs QUEUE-005): one exit, position >= 4 x cells unless in state 14; C#'s rule agrees.
    ('tpw-exe-queue-reevaluate', 'plan'): AGREE,
    # X15 (park points counts-test 0x4dd920 vs ARRIVAL-003): the Mac test at PC offsets; C# applies all of it, the coaster
    # controller test (CoasterRide.AcceptsGuests) and the kart/water track word (BumpController.IsTrackValid) included.
    ('tpw-exe-counts-test', 'plan'): AGREE,
    ('tpw-exe-counts-test', 'csharp-rule'): AGREE,
    ('tpw-exe-counts-test', 'csharp-rule-untracked'): AGREE,
    # X16 (price test 0x4fde50, GUEST-003): the willingness and the statistic agree with the reading; without the money test 56 differ.
    ('tpw-exe-price-test', 'plan'): AGREE,
    # X17 (toilet wear 0x4e2440, QUEUE-008): the single step and the dirty crossing agree; a double -0.05 differs in the last bit once.
    ('tpw-exe-toilet-wear', 'plan'): AGREE,
    ('tpw-exe-toilet-wear', 'double-constant'): (1, {'gauge': 3.3, 'toilet': 49, 'relief': True, 'cx': 122, 'cy': 2}),
    ('tpw-exe-price-test', 'no-money-test'): (56, {'happiness': 60.0, 'thirst': 80.0, 'hunger': 10.0, 'illness': 0.0, 'money': 0, 'price': 1,
                                                   'goods': 0, 'win': 0, 'kind': 1, 'base': 20, 'e_thirst': 40, 'e_hunger': 0, 'e_vomit': 0,
                                                   'e_happy': 10, 'product': 2, 'extra': 0, 'type_word': 0}),
    # X18 (guest state 6 0x4fec90, GUEST-010): the decision order of WANDER-plan §2, every branch's effects included.
    ('tpw-exe-guest-state6', 'plan'): AGREE,
    # The prank test reads happiness after a leaving gate without a way out took the big change; with the earlier value 2 differ.
    ('tpw-exe-guest-state6', 'prank-before-gate-change'): (2, {'turn': 50, 'activity': 0, 'decision': 3645728797, 'happiness': 30.0, 'illness': 0.0,
                                                             'exit': 0, 'closed': False, 'r': 4, 'r2': 2096108995, 'litter': 0.0, 'bin': 0,
                                                             'prank': 101, 'stink': 25, 'things': ['self', (1, 20816)], 'way_out': False,
                                                             'ride': 0, 'wander': False}),
    # X19 (wander destination 0x4f9490, GUEST-010): GuestWander's steps, draws and sub-cell bytes on synthetic cells.
    ('tpw-exe-wander-destination', 'plan'): AGREE,
    # The rule a step reads is the current cell's own link; with links on either side 164 differ (inputs with one-sided links).
    ('tpw-exe-wander-destination', 'either-side-links'): (164, ((24, 31), 1062577813)),
    # X20 (littering a cell 0x4d93b0, ECON-020): one item per cell, nothing online, the sprite offset from the cell-id reseed.
    ('tpw-exe-litter-drop', 'plan'): AGREE,
    # X21 (state 9, the walk to a bin 0x4fff20, GUEST-005): the bin's sound on arrival, else litter on the cell; the meter empties.
    ('tpw-exe-bin-walk', 'plan'): AGREE,
    # X22 (region effect writer 0x4d8480, GUEST-006): ParkEnvironment.Apply, square radius, Manhattan divisor, wrapping words.
    ('tpw-exe-region-effect', 'plan'): AGREE,
    # X23 (the handyman's litter finder 0x4c8ed0, ECON-022): StaffWorldSimulation.FindLitter, range square, claims, Manhattan, first on a tie.
    ('tpw-exe-litter-finder', 'plan'): AGREE,
    ('tpw-exe-guard-choice', 'plan'): AGREE,
    # X25 (the guest walk, WALK-002 .. WALK-009): the edge test 0x4d8750, the step commit 0x510510, the route search 0x511420,
    # avoid_walls 0x50e4a0 and whole walks through the set-up 0x50f8e0 and the steering step 0x50f3b0 (walk_reading.py).
    ('tpw-exe-walk-edge-test', 'plan'): AGREE,
    ('tpw-exe-walk-commit', 'plan'): AGREE,
    ('tpw-exe-walk-search', 'plan'): AGREE,
    ('tpw-exe-walk-avoid-walls', 'plan'): AGREE,
    ('tpw-exe-walk-steering', 'plan'): AGREE,
    # X26 (the TQI movie decoder, TGQ-MOVIES.md): the matrix builder 0x6710c0 for every quantiser and the transform pass 0x67673c.
    ('tpw-exe-tqi-matrix', 'plan'): AGREE,
    ('tpw-exe-tqi-transform-pass', 'plan'): AGREE,
    # X27 (the procedural terrain texturer, docs/LND.md): the ramp table 0x55e710, the weight plane 0x55e780 and the build routine 0x55f780.
    ('tpw-exe-terrain-ramp', 'plan'): AGREE,
    ('tpw-exe-terrain-weights', 'plan'): AGREE,
    ('tpw-exe-terrain-build', 'plan'): AGREE,
    # the visible-cell span selection 0x56bb00 with the ray, hull, clip and rasteriser under it, and the visible-cell cache lookup 0x572440
    ('tpw-exe-terrain-spans', 'plan'): AGREE,
    ('tpw-exe-terrain-cache', 'plan'): AGREE,
    ('tpw-exe-queue-reevaluate', 'csharp-rule'): AGREE,
    ('tpw-exe-queue-reevaluate', 'no-state-14-exemption'): (70, {'cells': 1, 'position': 4, 'state': 14, 'id': 3}),
    ('tpw-exe-scrap-value', 'all-levels-cost'): (221, {'now': 1000, 'built': 1000, 'level': 1, 'used': 0, 'cost': [10000, 8000, 12000],
                                                       'pct': [[50, 30, 20, 10], [45, 25, 15, 8], [40, 20, 10, 5]], 'base': 0}),
    ('tpw-exe-scrap-value', 'no-first-month-refund'): (57, {'now': 1000, 'built': 1000, 'level': 0, 'used': 0,
                                                            'cost': [10000, 8000, 12000],
                                                            'pct': [[50, 30, 20, 10], [45, 25, 15, 8], [40, 20, 10, 5]], 'base': 0}),
    ('tpw-exe-scrap-value', 'base-row'): (181, {'now': 1692, 'built': 1000, 'level': 1, 'used': 0, 'cost': [10000, 8000, 12000],
                                                'pct': [[50, 30, 20, 10], [45, 25, 15, 8], [40, 20, 10, 5]], 'base': 587586201}),
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
    # X11 (coaster rating step, tp.exe 0x43dc50 and Game.exe 0x480630 vs the Mac 0x415b4, F7). The pin of a mismatch is its
    # count and a summary of the first counterexample: (sample index, bank, speed) for the step, (S, E, peak S, peak E) for the
    # final. 'plan' is the Mac routine with the stale term = Fz (what CoasterRating.Step follows): the Windows step adds u, the
    # single (1 + U), where the Mac adds Fz, so most inputs differ. 'coast-plan-stale0' is the documented COAST-plan 6.3 (0).
    # 'mac-plan-else-u' is the Mac plan with the Windows term: what is left is rounding (x87 sums against singles, at most
    # 3 ulps in S and E, no scream decision changes in this input set).
    ('tpw-exe-coast-rating-step', 'plan'): (469, (33, 1.5707963705062866, 22.126415252685547)),
    ('tpw-exe-coast-rating-step', 'coast-plan-stale0'): (229, (33, 1.5707963705062866, 22.126415252685547)),
    ('tpw-exe-coast-rating-step', 'mac-plan-else-u'): (190, (33, 1.5707963705062866, 22.126415252685547)),
    ('tpw-exe-coast-rating-step', 'routine-reading'): AGREE,
    # X12 (coaster rating final, tp.exe 0x43df90 and Game.exe 0x4809c0 vs the Mac 0x41900, F8): the same rules (both flags
    # counted, the 1.7 and 1/2 weights, the 0..100 clamps, +268 = +168 * +172); only the excitement differs, in the last bit,
    # where the x87 adds the exact product 1.7 * peak E and the Mac rounds it to single first. 'coast-plan-documented' is
    # COAST-plan 6.4 as written (a section counted when neither flag is set).
    ('tpw-exe-coast-rating-final', 'plan'): (23, (6.844004154205322, 34.513729095458984, 87.3030014038086, 18.75595474243164)),
    ('tpw-exe-coast-rating-final', 'coast-plan-documented'): (315, (50.0, 126.62156677246094, 50.0000038, 51.75424575805664)),
    ('tpw-exe-coast-rating-final', 'routine-reading'): AGREE,
    ('tpi-exe-coast-rating-step', 'plan'): (470, (14, 1.5707963705062866, 9.884252548217773)),
    ('tpi-exe-coast-rating-step', 'coast-plan-stale0'): (217, (39, -3.1415927410125732, 10.482479095458984)),
    ('tpi-exe-coast-rating-step', 'mac-plan-else-u'): (181, (4, 3.0, 9.048203468322754)),
    ('tpi-exe-coast-rating-step', 'routine-reading'): AGREE,
    ('tpi-exe-coast-rating-final', 'plan'): (30, (50.0, 109.31599426269531, 50.0000038, 22.756824493408203)),
    ('tpi-exe-coast-rating-final', 'coast-plan-documented'): (271, (50.0, 77.47186279296875, 50.0, -13.047860145568848)),
    ('tpi-exe-coast-rating-final', 'routine-reading'): AGREE,
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


def check_pinned(case, comparison, key):
    """One comparison against its pin: AGREE, or (mismatches, first counterexample or its coaster summary)."""
    expected = EXPECTED[key]
    if expected == AGREE:
        case.assertTrue(comparison.agrees, f'{key}: {comparison.mismatches} mismatches, first {comparison.first}')
    else:
        count, first_input = expected
        case.assertEqual(comparison.mismatches, count, f'{key}: first {comparison.first}')
        first = comparison.first['input']
        if isinstance(first_input, tuple):
            first = wander_summary(first) if key[0] == 'tpw-exe-wander-destination' else coast_summary(first)
        case.assertEqual(first, first_input, key)


def coast_summary(x):
    """Short identity of a coaster input: (sample index, bank, speed) for a step, (S, E, peak S, peak E) for a final."""
    return (x['i'], x['bank'], x['v']) if 'def' in x else (x['S'], x['E'], x['peak_s'], x['peak_e'])


def wander_summary(x):
    """Short identity of a wander input: the start cell and the first draw (the cells come from the same seeded stream)."""
    return tuple(x['start']), x['draws'][0]


def ulps(a: float, b: float) -> int:
    """Distance of two singles in units in the last place."""
    ia, ib = (struct.unpack('<i', struct.pack('<f', value))[0] for value in (a, b))
    return abs(ia - ib)


class CoastTests:
    """X11 and X12 for either executable: the coaster rating step and final (class attributes `exe` and `image`)."""

    def coast(self, step: bool):
        return {('tpw', True): targets.COAST_STEP_TPW, ('tpw', False): targets.COAST_FINAL_TPW,
                ('tpi', True): targets.COAST_STEP_TPI, ('tpi', False): targets.COAST_FINAL_TPI}[(self.exe, step)]

    def coast_runs(self, target, count=DEFAULT_COUNT, **kwargs):
        sandbox = target.sandbox(self.image)
        inputs = target.inputs(__import__('x86_harness').seeded('oracle-v1:' + target.key), count)
        results = []
        for x in inputs:
            sandbox.reset()
            target.log.clear()
            results.append(target.binary(sandbox, x, target.log, **kwargs))
        return inputs, results

    def test_coast_pins(self):
        for step, sides in ((True, ('plan', 'coast-plan-stale0', 'mac-plan-else-u', 'routine-reading')),
                            (False, ('plan', 'coast-plan-documented', 'routine-reading'))):
            target = self.coast(step)
            results = {c.side: c for c in compare(target, self.image, DEFAULT_COUNT)}
            self.assertEqual(set(results), set(sides))
            for side in sides:
                check_pinned(self, results[side], (target.key, side))

    def test_coast_step_adds_u_where_the_mac_adds_the_stale_fz(self):
        """The branch that is not inverted (u = 0, or Fy at least 0.1): the Mac adds Fz (F7), the Windows step adds u itself.
        With the default weights and force (0.4, 1.3, 0.5), speed 20, E = 1: level bank, u = 0, the Windows E is
        1/2 + (2 + 2.2 + 5 * 0.2 - 0.4) = 5.3, the Mac 4.8 (it also subtracts Fz = 0.5); upside down (bank 3) with Fy = 0.5
        the Windows step subtracts u = 1 + (3 - pi/2) = 2.4292, the Mac Fz = 0.5; where the inverted branch is taken
        (Fy below 0.1) both agree."""
        defaults = dict(gforce_sick=0.5, upside_sick=3.0, speed=0.1, gforce_excite=1.0, bank_excite=5.0, xg_uncomf=1.0,
                        inverted_uncomf=2.0, normal_start=0.5, terror_start=1.0, normal_sustain=10.0, terror_sustain=20.0)

        def sample(force, bank):
            return {'def': dict(defaults), 'F': force, 'f0': force[2], 'v': 20.0, 'i': 5, 'bank': bank, 'S': 0.0, 'E': 1.0,
                    'peak_s': 0.0, 'peak_e': 0.0, 'e_prev': 1.0, 'normal': 0, 'terror': 0, 'flags': 0}
        target = self.coast(True)
        sandbox = target.sandbox(self.image)
        level = targets.run_binary(target, sandbox, sample([0.4, 1.3, 0.5], 0.2))
        self.assertAlmostEqual(level['E'], 5.3, places=5)
        self.assertAlmostEqual(targets.coast_step_plan(sample([0.4, 1.3, 0.5], 0.2))['E'], 4.8, places=5)
        upside = sample([0.4, 0.5, 0.5], 3.0)
        self.assertAlmostEqual(targets.run_binary(target, sandbox, upside)['E'], 16.0708, places=3)
        self.assertAlmostEqual(targets.coast_step_plan(upside)['E'], 18.0, places=5)
        for force in ([0.4, 0.05, 0.5], [0.4, -0.5, 0.5]):
            inverted = sample(force, 3.0)
            self.assertAlmostEqual(targets.run_binary(target, sandbox, inverted)['E'], targets.coast_step_plan(inverted)['E'],
                                   delta=1e-5)
        self.assertAlmostEqual(targets.run_binary(target, sandbox, sample([0.4, -0.5, 0.5], 3.0))['E'], 3.92478, places=4)

    def test_coast_step_rest_is_rounding_only(self):
        """Against the Mac plan with the Windows term every difference is S or E (and what follows from them) within 3 ulps,
        and no scream flag or state differs: the x87 builds the sums in a different order and rounds to single later."""
        target = self.coast(True)
        inputs, results = self.coast_runs(target)
        worst = 0
        for x, result in zip(inputs, results):
            reference = targets.coast_step_plan_else_u(x)
            for key in ('flags', 'normal', 'terror'):
                self.assertEqual(result[key], reference[key], key)
            for key in ('S', 'E', 'peak_s', 'peak_e', 'e_prev'):
                worst = max(worst, ulps(result[key], reference[key]))
        self.assertLessEqual(worst, 3)
        self.assertGreater(worst, 0)   # a precision difference does exist: the readings differ from the single arithmetic

    def test_coast_step_follows_the_state_block_not_a_stale_register(self):
        """Calling the step twice in a row on the same state block (the second call's S, E and peaks are the first's) matches
        the reading twice: no state outside the static block carries over."""
        target = self.coast(True)
        inputs = target.inputs(__import__('x86_harness').seeded('oracle-v1:chain:' + target.key), 40)
        sandbox = target.sandbox(self.image)
        for x in inputs:
            first = targets.run_binary(target, sandbox, x)
            second_input = {**x, 'S': first['S'], 'E': first['E'], 'peak_s': first['peak_s'], 'peak_e': first['peak_e'],
                            'e_prev': first['e_prev'], 'normal': first['normal'], 'terror': first['terror'], 'i': x['i'] + 1}
            second = targets.run_binary(target, sandbox, second_input)
            self.assertTrue(targets.coast_equal(second, targets.coast_step_reading(second_input, self.exe)))

    def test_coast_step_precision_is_the_same_at_53_and_64_bits(self):
        """The control words 0x27F and 0x37F give identical results on every input (single stores hide the extra bits)."""
        target = self.coast(True)
        _, narrow = self.coast_runs(target, fpcw=0x27F)
        _, wide = self.coast_runs(target, fpcw=0x37F)
        self.assertTrue(all(targets.coast_equal(a, b) for a, b in zip(narrow, wide)))

    def test_coast_step_readings_are_sharp(self):
        """The two executables round at different points: the other executable's reading is wrong on part of the inputs."""
        target = self.coast(True)
        other = 'tpi' if self.exe == 'tpw' else 'tpw'
        inputs, results = self.coast_runs(target)
        mismatches = sum(1 for x, r in zip(inputs, results) if not targets.coast_equal(r, targets.coast_step_reading(x, other)))
        self.assertGreater(mismatches, 20)
        self.assertEqual(sum(1 for x, r in zip(inputs, results)
                             if not targets.coast_equal(r, targets.coast_step_reading(x, self.exe))), 0)

    def test_coast_final_differs_from_the_mac_in_the_last_bit_only(self):
        """Sickness, length, maxima and the section count agree with the Mac routine (F8) on every input; the excitement
        differs in the last bit(s) where it differs at all, and agrees on all inputs at the single-precision word 0x7F."""
        target = self.coast(False)
        inputs, results = self.coast_runs(target)
        for x, result in zip(inputs, results):
            reference = targets.coast_final_plan(x)
            for key in ('sickness', 'length', 'max_y', 'max_speed', 'sections'):
                self.assertEqual(result[key], reference[key], key)
            self.assertAlmostEqual(result['excitement'], reference['excitement'], delta=1e-4)
        _, single = self.coast_runs(target, fpcw=0x7F)
        for x, result in zip(inputs, single):
            self.assertTrue(targets.coast_equal(result, targets.coast_final_plan(x)))

    def test_coast_final_example_and_section_count(self):
        """S = 30, peak S = 40, E = 20, peak E = 30: sickness 70, excitement 20/2 + 1.7 * 30 = 61; a sum past 100 clamps; the
        sections count only the ones with both 0x40 and 0x80 (2 of [0, 0x40, 0x80, 0xC0, 0xC0])."""
        target = self.coast(False)
        sandbox = target.sandbox(self.image)
        x = dict(S=30.0, E=20.0, peak_s=40.0, peak_e=30.0, length_a=10.0, length_b=7.0, max_y=12.0, max_speed=30.0,
                 sections=[0, 0x40, 0x80, 0xC0, 0xC0])
        self.assertEqual(targets.run_binary(target, sandbox, x),
                         {'sickness': 70.0, 'excitement': 61.0, 'length': 70.0, 'max_y': 12.0, 'max_speed': 30.0, 'sections': 2})
        clamped = targets.run_binary(target, sandbox, {**x, 'S': 80.0, 'peak_e': 70.0})
        self.assertEqual((clamped['sickness'], clamped['excitement']), (100.0, 100.0))
        negative = targets.run_binary(target, sandbox, {**x, 'S': -90.0, 'E': -50.0, 'peak_e': 1.0})
        self.assertEqual((negative['sickness'], negative['excitement']), (0.0, 0.0))
        self.assertEqual(targets.run_binary(target, sandbox, {**x, 'sections': []})['sections'], 0)


@unittest.skipUnless(UNICORN, UNICORN_HINT)
@unittest.skipUnless(TPW, NO_TPW)
class Oracle(CoastTests, unittest.TestCase):
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
        check_pinned(self, self.results[key], key)

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

    # ------------------------------------------------------------------ X13: object scrap value
    def test_scrap_value_matches_the_mac_rule(self):
        self.check(('tpw-exe-scrap-value', 'plan'))
        for side in ('all-levels-cost', 'no-first-month-refund', 'base-row'):
            self.check(('tpw-exe-scrap-value', side))

    def test_scrap_first_month_needs_an_unused_object_and_the_clock_base_cancels(self):
        """A new Belly Bounce-like ride: its whole price within 30 days while unused (+0x1a0 = 0), the year-1 row once a
        guest has left it or after 30 days; the same refunds with any clock base."""
        target = targets.SCRAP_TARGET
        rows = dict(cost=[500, 400, 500], pct=[[50, 30, 20, 10], [60, 40, 30, 15], [65, 45, 35, 20]], level=0)
        for base in (0, 0xFFFFFFFF00000000):
            new = {**rows, 'now': 1010, 'built': 1000, 'used': 0, 'base': base}
            self.assertEqual(self.run_target(target, new), 500)
            self.assertEqual(self.run_target(target, {**new, 'used': 1}), 250)
            self.assertEqual(self.run_target(target, {**new, 'now': 1000 + 692}), 250)

    # ------------------------------------------------------------------ X14: queue re-evaluation
    def test_queue_reevaluation_has_one_exit(self):
        for side in ('plan', 'csharp-rule', 'no-state-14-exemption'):
            self.check(('tpw-exe-queue-reevaluate', side))

    # ------------------------------------------------------------------ X15: park points counts-test
    def test_counts_test_matches_csharp(self):
        for side in ('plan', 'csharp-rule', 'csharp-rule-untracked'):
            self.check(('tpw-exe-counts-test', side))

    # ------------------------------------------------------------------ X16: price test
    def test_price_test_matches_the_reading(self):
        for side in ('plan', 'no-money-test'):
            self.check(('tpw-exe-price-test', side))

    # ------------------------------------------------------------------ X17: toilet wear
    def test_toilet_wear_matches_the_reading(self):
        for side in ('plan', 'double-constant'):
            self.check(('tpw-exe-toilet-wear', side))

    # ------------------------------------------------------------------ X18 / X19: guest states 6 and 7
    def test_state_six_runs_the_planned_order(self):
        for side in ('plan', 'prank-before-gate-change'):
            self.check(('tpw-exe-guest-state6', side))

    def test_wander_destination_follows_guest_wander(self):
        for side in ('plan', 'either-side-links'):
            self.check(('tpw-exe-wander-destination', side))

    # ------------------------------------------------------------------ X20 / X21: litter
    def test_litter_drop_keeps_one_item_per_cell(self):
        self.check(('tpw-exe-litter-drop', 'plan'))

    def test_bin_walk_bins_or_drops_the_litter(self):
        self.check(('tpw-exe-bin-walk', 'plan'))

    # ------------------------------------------------------------------ X22: region effects
    def test_region_effect_spreads_like_the_environment_grid(self):
        self.check(('tpw-exe-region-effect', 'plan'))

    # ------------------------------------------------------------------ X23: the handyman's litter finder
    def test_litter_finder_takes_the_nearest_unclaimed_cell(self):
        self.check(('tpw-exe-litter-finder', 'plan'))

    def test_guard_choice_measures_from_the_staff_hq(self):
        self.check(('tpw-exe-guard-choice', 'plan'))

    def test_guard_choice_is_sharp(self):
        # Measuring from the prankster (the routine's own cell argument) or letting a tie go to the later guard both disagree.
        def from_prankster(x):
            return targets.guard_plan(dict(x, origin=x['prankster']))

        def ties_last(x):
            best, chosen = None, 0
            for i, (state, gx, gy) in enumerate(x['guards']):
                d = (gx - x['origin'][0]) ** 2 + (gy - x['origin'][1]) ** 2
                if state in (0, 1) and (best is None or d <= best):
                    best, chosen = d, i + 1
            return chosen
        for plan in (from_prankster, ties_last):
            [comparison] = [c for c in compare(targets.GUARD_TARGET, self.image, plan=plan) if c.side == 'plan']
            self.assertGreater(comparison.mismatches, 0, plan.__name__)

    # ------------------------------------------------------------------ X25: the guest walk
    def test_walk_edge_test_and_commit_agree(self):
        self.check(('tpw-exe-walk-edge-test', 'plan'))
        self.check(('tpw-exe-walk-commit', 'plan'))

    def test_walk_search_agrees(self):
        self.check(('tpw-exe-walk-search', 'plan'))

    def test_walk_avoid_walls_agrees(self):
        self.check(('tpw-exe-walk-avoid-walls', 'plan'))

    def test_walk_steering_agrees(self):
        self.check(('tpw-exe-walk-steering', 'plan'))

    # ------------------------------------------------------------------ X26: the TQI movie decoder
    def test_tqi_matrix_and_transform_agree(self):
        self.check(('tpw-exe-tqi-matrix', 'plan'))
        self.check(('tpw-exe-tqi-transform-pass', 'plan'))

    # ------------------------------------------------------------------ X27: the procedural terrain texturer
    def test_terrain_ramp_weights_and_build_agree(self):
        self.check(('tpw-exe-terrain-ramp', 'plan'))
        self.check(('tpw-exe-terrain-weights', 'plan'))
        self.check(('tpw-exe-terrain-build', 'plan'))

    def test_terrain_spans_and_cache_agree(self):
        self.check(('tpw-exe-terrain-spans', 'plan'))
        self.check(('tpw-exe-terrain-cache', 'plan'))

    def test_terrain_build_inputs_reach_every_run_mode_and_the_grey_fallback(self):
        inputs = targets.terrain_build_inputs(__import__('x86_harness').seeded('oracle-v1:tpw-exe-terrain-build'), DEFAULT_COUNT)
        seen = set()
        grey = 0
        for x in inputs:
            texture, modes = targets.terrain_reading.build(x['level'], x['n'], x['x'], x['z'])
            seen.update(modes.values())
            grey += not modes
        self.assertEqual(seen, set(targets.terrain_reading.MODES))
        self.assertGreater(grey, 0)

    def test_terrain_build_readings_are_sharp(self):
        # Without the three layer quirk (weight 1 per texel in a CBA run) the reading differs from the original; so does one that writes the
        # rows top-down instead of bottom-up.
        def flipped(x):
            texture = targets.terrain_build_plan(x)
            n = x['n']
            return b''.join(texture[(n - 1 - row) * n * 4:(n - row) * n * 4] for row in range(n))
        for name, plan in (('quirk', targets.terrain_build_per_texel_weight), ('rows', flipped)):
            [comparison] = [c for c in compare(targets.TERRAIN_BUILD_TARGET, self.image, 24, plan=plan) if c.side == 'plan']
            self.assertGreater(comparison.mismatches, 0, name)

    def test_walk_readings_are_sharp(self):
        # The walk's mask matters to the edge test and the search: a reading that ignores it disagrees.
        for target, plan in ((targets.WALK_EDGE_TARGET, lambda x: targets.walk_edge_plan(dict(x, mask=0))),
                             (targets.WALK_SEARCH_TARGET, lambda x: targets.walk_search_plan(dict(x, mask=0)))):
            [comparison] = [c for c in compare(target, self.image, 100, plan=plan) if c.side == 'plan']
            self.assertGreater(comparison.mismatches, 0, target.key)

    def test_original_hash_refuses_a_modified_copy(self):
        raw = bytearray(TPW.read_bytes())
        raw[0x1000] ^= 1
        with self.assertRaises(IdentityError):
            Image(bytes(raw))


class TerrainReading(unittest.TestCase):
    """The Python reading of the procedural terrain texturer needs neither Unicorn nor an executable."""

    def test_ramp_values(self):
        table = targets.terrain_reading.ramp(48, 40)
        self.assertEqual((table[27], table[28], table[29], table[68], table[69]), (0, 0, 6, 248, 255))
        step = targets.terrain_reading.ramp(128, 0)
        self.assertEqual((step[127], step[128], step[129]), (0, 0, 255))

    def test_constant_weight_grid_gives_a_constant_plane(self):
        side = 13
        level = {'width': side, 'height': side, 'grid': bytes([255, 77, 0, 0]) * side * side}
        for n_size in (16, 32, 64):
            self.assertEqual(set(targets.terrain_reading.weights(level, n_size, 1, 1, 1)), {77})

    def test_synthetic_inputs_reach_every_mode(self):
        inputs = targets.terrain_build_inputs(__import__('x86_harness').seeded('oracle-v1:tpw-exe-terrain-build'), DEFAULT_COUNT)
        seen = set()
        for x in inputs:
            seen.update(targets.terrain_reading.build(x['level'], x['n'], x['x'], x['z'])[1].values())
        self.assertEqual(seen, set(targets.terrain_reading.MODES))


class TerrainSpansAndCacheReading(unittest.TestCase):
    """The Python readings of the visible-cell span selection and cache behind TerrainVisibleSpansGolden.cs and ProceduralTextureCacheGolden.cs need neither Unicorn nor an executable."""

    def test_spans_inputs_reach_every_hull_and_clip_branch(self):
        spans = __import__('terrain_spans')
        spans.BRANCHES.clear()
        for x in targets.terrain_spans_inputs(__import__('x86_harness').seeded('oracle-v1:tpw-exe-terrain-spans'), DEFAULT_COUNT):
            spans.spans(x)
        self.assertEqual(set(spans.BRANCHES), {'hull1', 'hull2', 'hull2-right', 'hull2-left', 'hull2-collinear', 'hull-one-edge', 'hull-many-edges', 'hull-inside'})

    def test_spans_reach_the_last_column_and_row_of_a_level_field_but_not_of_a_tiny_one(self):
        # an end is the edge plus 0.999999 in single precision: from about 8 cells on the sum rounds up to the next whole cell, so the last column and row are listed when the view reaches them
        spans = __import__('terrain_spans')
        wide = deep = False
        for x in targets.terrain_spans_inputs(__import__('x86_harness').seeded('oracle-v1:tpw-exe-terrain-spans'), DEFAULT_COUNT):
            if x['origin'] != (0.0, 0.0) or x['size'] != (10.0, 10.0) or x['cells'] != (96, 85):
                continue
            result = spans.spans(x)
            wide |= any(result['end'][row] == 96 for row in range(result['min'], min(result['max'], 128)))
            deep |= result['max'] == 85
        self.assertTrue(wide and deep)
        tiny = dict(spans.special_scenes()[0], cells=(5, 4), camera=(25.0, 200.0, 20.0))
        result = spans.spans(tiny)
        self.assertEqual((max(result['end'][result['min']:result['max']]), result['max']), (4, 3))

    def test_cache_sequences_hit_miss_and_overflow(self):
        cache_reading = __import__('terrain_cache')
        hits = misses = overflow = 0
        for run in cache_reading.random_sequences(__import__('x86_harness').seeded('oracle-v1:tpw-exe-terrain-cache'), 100):
            cache = cache_reading.Cache(run['capacity'])
            for frame in run['frames']:
                before = dict(cache.table)
                cache.resolve(frame['keys'], frame['dirty'])
                hits += sum(1 for key in frame['keys'] if cache.entry(key) in before)
                misses += sum(1 for key in frame['keys'] if cache.entry(key) not in before)
                overflow += len(frame['keys']) > run['capacity']
        self.assertTrue(hits > 0 and misses > 0 and overflow > 0, (hits, misses, overflow))


class TerrainSyntheticLevels(unittest.TestCase):
    """The reproducible synthetic levels behind ProceduralTerrainSyntheticGolden.cs need neither Unicorn nor an executable."""

    def test_levels_reach_every_mode_and_the_grey_fallback(self):
        reading = targets.terrain_reading
        seen, grey, count = set(), 0, 0
        for seed, mix, n_size, is_grey in reading.synthetic_specs():
            level = reading.synthetic_level(seed, mix, n_size, is_grey)
            for x, z in reading.synthetic_cells(seed, is_grey):
                modes = reading.build(level, n_size, x, z)[1]
                seen.update(modes.values())
                grey += not modes
                count += 1
        self.assertEqual(seen, set(reading.MODES))
        self.assertEqual((count, grey), (81, 3))

    def test_generator_is_deterministic(self):
        reading = targets.terrain_reading
        self.assertEqual(reading.synthetic_level(1003, 'BA', 16, False), reading.synthetic_level(1003, 'BA', 16, False))
        self.assertEqual(reading.Lcg(1).next(), ((1664525 + 1013904223) & 0xFFFFFFFF) >> 24)


class WalkReadingCoverage(unittest.TestCase):
    """X25's inputs, on the reading alone (no Unicorn or executable): the whole walks reach every step result, and the
    searches both succeed and fail."""

    def test_steering_walks_arrive_walk_and_get_stuck(self):
        target = targets.WALK_STEERING_TARGET
        inputs = target.inputs(__import__('x86_harness').seeded('oracle-v1:' + target.key), DEFAULT_COUNT)
        results = {result for x in inputs for kind, result, _ in targets.walk_steering_plan(x) if kind == 'step'}
        self.assertEqual(results, {0, 1, 2})

    def test_searches_succeed_and_fail(self):
        target = targets.WALK_SEARCH_TARGET
        inputs = target.inputs(__import__('x86_harness').seeded('oracle-v1:' + target.key), DEFAULT_COUNT)
        found = [targets.walk_search_plan(x)[0] for x in inputs]
        self.assertTrue(any(found) and not all(found))


@unittest.skipUnless(UNICORN, UNICORN_HINT)
@unittest.skipUnless(TPI, NO_TPI)
class OracleTpi(CoastTests, unittest.TestCase):
    """X2: the TPI Game.exe world RNG step (the image has a relocation directory and an .idata section); X7 to X10: the
    TPI counterparts of the arrival, loan and park-points rules; X11 and X12: the coaster rating."""
    exe = 'tpi'

    @classmethod
    def setUpClass(cls):
        cls.image = Image.load(TPI)
        cls.results = {}
        for target in targets.TARGETS:
            if target.exe == 'tpi':
                cls.results.update({(target.key, c.side): c for c in compare(target, cls.image, DEFAULT_COUNT)})

    def check(self, key):
        check_pinned(self, self.results[key], key)

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

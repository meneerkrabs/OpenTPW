"""Tests of the controller +0 reader scan (tools/ppc-analysis/controller_flag_readers.py, docs/reverse/COAST-plan.md section 24).

Dev-only tooling. The synthetic tests run a few instructions written here through the real scan (they need the dev-only `capstone` package and skip
without it); the check against the identified tp.exe needs OPENTPW_TPW_EXE and skips without it, like the x86 oracle. No executable bytes appear in
this file.
"""
from __future__ import annotations

import io
import os
import struct
import sys
import unittest
from pathlib import Path
from unittest import mock

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
sys.path.insert(0, str(HERE / 'oracle_x86'))

import controller_flag_readers as scan  # noqa: E402

BASE, TEXT = 0x400000, 0x1000
LIST_HEAD, EDITED = 0x790FE0, 0x790FEC


def pe_with(code: bytes) -> bytes:
    """A one-section synthetic PE32 whose .text holds `code` at 0x401000."""
    section = code.ljust(0x400, b'\xcc')
    headers = bytearray(0x400)
    headers[:2] = b'MZ'
    struct.pack_into('<I', headers, 0x3C, 0x80)
    headers[0x80:0x84] = b'PE\0\0'
    struct.pack_into('<HHIIIHH', headers, 0x84, 0x14C, 1, 0, 0, 0, 0xE0, 0x102)
    optional = 0x80 + 24
    struct.pack_into('<H', headers, optional, 0x10B)
    struct.pack_into('<I', headers, optional + 16, TEXT)
    struct.pack_into('<I', headers, optional + 28, BASE)
    struct.pack_into('<II', headers, optional + 32, 0x1000, 0x200)
    struct.pack_into('<I', headers, optional + 56, 0x2000)
    struct.pack_into('<I', headers, optional + 60, 0x400)
    struct.pack_into('<8sIIIIIIHHI', headers, optional + 0xE0, b'.text', 0x400, TEXT, 0x400, 0x400, 0, 0, 0, 0, 0x60000020)
    return bytes(headers) + section


def dword(address: int) -> bytes:
    return struct.pack('<I', address)


def run(code: bytes, entries=(BASE + TEXT,)):
    """Run the scan on `code` with the whole synthetic .text as the coaster module; returns (analysis, classifier)."""
    analysis = scan.Analysis(scan.Image(pe_with(code)))
    analysis.starts = []
    with mock.patch.object(scan, 'MODULE', (BASE + TEXT, BASE + TEXT + 0x400)):
        analysis.solve(set(entries))
    return analysis, scan.Classifier(analysis)


def verdicts(analysis, classifier):
    out = {}
    for event in analysis.events.values():
        if event.lo is not None and 'r' in event.access:
            out[event.address] = classifier.verdict(event, min(event.functions))
    return out


@unittest.skipIf(scan.capstone is None, 'needs the dev-only capstone package')
class SyntheticFlowTests(unittest.TestCase):
    def test_a_test_of_a_track_flag_through_the_list_head(self):
        code = b'\xa1' + dword(LIST_HEAD) + bytes.fromhex('f60020') + b'\xc3'          # mov eax,[head]; test byte [eax],0x20; ret
        analysis, classifier = run(code)
        found = verdicts(analysis, classifier)
        self.assertEqual(list(found), [BASE + TEXT + 5])
        self.assertEqual(found[BASE + TEXT + 5].observed, 0x20)
        self.assertTrue(found[BASE + TEXT + 5].observed & scan.TRACK_BITS)

    def test_the_pointer_follows_a_call_in_ecx_and_the_callee_masks_the_word(self):
        main = b'\x8b\x0d' + dword(EDITED) + b'\xe8' + struct.pack('<i', 0x100 - 6 - 5) + b'\xc3'            # mov ecx,[edited]; call +0x100; ret
        callee = bytes.fromhex('8b01' 'a900080000') + b'\xc3'                                                 # mov eax,[ecx]; test eax,0x800; ret
        analysis, classifier = run(main.ljust(0x100, b'\xcc') + callee)
        found = verdicts(analysis, classifier)
        address = BASE + TEXT + 0x100
        self.assertEqual(list(found), [address])
        self.assertEqual(found[address].observed, 0x800)
        self.assertFalse(found[address].observed & scan.TRACK_BITS)

    def test_a_read_modify_write_observes_nothing(self):
        code = b'\xa1' + dword(EDITED) + bytes.fromhex('8b08' '83c920' '8908') + b'\xc3'      # mov eax,[edited]; mov ecx,[eax]; or ecx,0x20; mov [eax],ecx
        analysis, classifier = run(code)
        verdict = verdicts(analysis, classifier)[BASE + TEXT + 5]
        self.assertEqual(verdict.observed, 0)
        self.assertTrue(verdict.stored_back)
        self.assertEqual(verdict.unclassified, [])

    def test_a_null_edge_drops_the_pointer(self):
        code = (b'\xa1' + dword(LIST_HEAD) + bytes.fromhex('85c0' '7402' '8b00') + b'\x8b\x08\xc3')   # eax=[head]; test eax,eax; je +2; mov eax,[eax]; mov ecx,[eax]; ret
        analysis, classifier = run(code)
        offsets = sorted(event.address - BASE - TEXT for event in analysis.events.values())
        self.assertIn(0x9, offsets)                       # the load on the non-null path
        self.assertNotIn(0xB, offsets)                    # eax is the next controller's link: not a tracked field, so not a controller read

    def test_a_pointer_stored_through_an_unknown_base_is_listed(self):
        code = b'\xa1' + dword(LIST_HEAD) + bytes.fromhex('8906') + b'\xc3'                   # mov eax,[head]; mov [esi],eax; ret
        analysis, _ = run(code)
        self.assertEqual(len(analysis.holder_stores), 1)
        self.assertIn('base unknown', next(iter(analysis.holder_stores.values())))

    def test_shifts_keep_exact_offsets_and_ranges_move_forward(self):
        tokens = frozenset({('C', 4), ('H48', ('R', 8)), ('G', 0x1000)})
        shifted = scan._shift(tokens, 0x10)
        self.assertEqual(shifted, frozenset({('C', 0x14), ('H48', ('R', 0x18)), ('G', 0x1010)}))
        self.assertEqual(scan._range(frozenset({('C', 4)})), frozenset({('C', ('R', 4))}))
        self.assertEqual(scan._blur(frozenset({('C', 4)})), frozenset({('C', '?')}))


class CommandLineTests(unittest.TestCase):
    def test_without_the_executable_the_script_skips(self):
        buffer = io.StringIO()
        with mock.patch.dict(os.environ, {}, clear=False), mock.patch('sys.stdout', buffer):
            os.environ.pop('OPENTPW_TPW_EXE', None)
            self.assertEqual(scan.main(['controller_flag_readers.py']), 0)
        self.assertTrue(buffer.getvalue().startswith('SKIP'))

    @unittest.skipIf(scan.capstone is None, 'needs the dev-only capstone package')
    def test_a_file_that_is_not_the_identified_executable_is_refused(self):
        with self.assertRaises(SystemExit):
            scan.load_image(Path(__file__))


@unittest.skipUnless(scan.capstone is not None and os.environ.get('OPENTPW_TPW_EXE') and Path(os.environ['OPENTPW_TPW_EXE']).is_file(),
                     'requires capstone and the identified tp.exe (OPENTPW_TPW_EXE)')
class IdentifiedExecutableTests(unittest.TestCase):
    def test_every_hit_is_harmless_or_reviewed(self):
        analysis, rounds, orphans = scan.run_analysis(scan.load_image(Path(os.environ['OPENTPW_TPW_EXE'])))
        with mock.patch('sys.stdout', io.StringIO()):
            problems = scan.report(analysis, rounds, orphans, out=io.StringIO())
        self.assertEqual(problems, [])


if __name__ == '__main__':
    unittest.main()

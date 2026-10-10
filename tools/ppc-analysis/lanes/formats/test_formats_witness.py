"""Synthetic instruction-field and corpus-reader tests; no original binaries are required."""
import struct
import sys
import unittest
import zlib
from pathlib import Path
from tempfile import TemporaryDirectory

sys.path.insert(0, str(Path(__file__).resolve().parent))

import corpus_check  # noqa: E402
import format_witness  # noqa: E402
from ppcfields import (WitnessError, a_form, branch_target, compare_immediate, cstring, d_form,  # noqa: E402
                       require, rlwimi, rlwinm, rotate, rotate_mask, x_form)


class FieldTests(unittest.TestCase):
    def test_d_form_and_compares(self):
        self.assertEqual(d_form((15 << 26) | (0 << 21) | (3 << 16) | (0x10000 - 7377)), (15, 0, 3, -7377))
        self.assertEqual(compare_immediate((10 << 26) | (0 << 23) | (0 << 16) | 23878), (10, 0, 0, 23878))
        self.assertEqual(compare_immediate((11 << 26) | (5 << 16) | 0xffff), (11, 0, 5, -1))

    def test_rotate_fields_and_semantics(self):
        w = (20 << 26) | (10 << 21) | (5 << 16) | (8 << 11) | (22 << 6) | (23 << 1)
        self.assertEqual(rotate(w), (20, 10, 5, 8, 22, 23, 0))
        self.assertEqual(rotate_mask(22, 23), 0x300)
        self.assertEqual(rotate_mask(30, 1), 0xc0000003)
        self.assertEqual(rlwimi(0x12, 0x3, 8, 22, 23), 0x312)
        self.assertEqual(rlwinm(0xABCD, 0, 16, 31), 0xABCD)
        self.assertEqual(rlwinm(0x80000001, 1, 0, 31), 0x3)

    def test_x_and_a_forms(self):
        srawi = (31 << 26) | (30 << 21) | (30 << 16) | (22 << 11) | (824 << 1)
        self.assertEqual(x_form(srawi)[3:5], (22, 824))
        fmadds = (59 << 26) | (2 << 21) | (2 << 16) | (6 << 11) | (5 << 6) | (29 << 1)
        self.assertEqual(a_form(fmadds), (59, 2, 2, 6, 5, 29))

    def test_branch_targets(self):
        self.assertEqual(branch_target((18 << 26) | 0x100 | 1, 0x40), 0x140)
        self.assertEqual(branch_target((18 << 26) | 0x03fffffc | 1, 0x40), 0x3c)
        with self.assertRaises(WitnessError):
            branch_target(18 << 26, 0)

    def test_strings_and_require(self):
        self.assertEqual(cstring(b'xx%s (Ok)\0', 2), '%s (Ok)')
        with self.assertRaises(WitnessError):
            cstring(b'abc', 0)
        with self.assertRaises(WitnessError):
            cstring(b'\x01\x02\0', 0)
        self.assertEqual(format_witness.label_suffix('%s (DeadMesh)'), 'DeadMesh')
        with self.assertRaises(WitnessError):
            format_witness.label_suffix('DeadMesh')
        with self.assertRaisesRegex(WitnessError, 'context'):
            require(1, 2, 'context')

    def test_identity_rejects_unknown_binary_before_parsing(self):
        with TemporaryDirectory() as directory:
            path = Path(directory) / 'SimThemePark.data'
            path.write_text('synthetic input, not a game binary')
            with self.assertRaisesRegex(WitnessError, 'identity'):
                format_witness.Image(path)

    def test_logical_immediates_are_unsigned(self):
        # d_form sign-extends; `ori 0x8000` ORs 0x8000, not -0x8000.
        ori = (24 << 26) | (19 << 21) | (19 << 16) | 0x8000
        op, _, _, imm = d_form(ori)
        self.assertEqual(imm, -0x8000)
        self.assertEqual(format_witness.logical_immediate(op, imm), 0x8000)
        self.assertEqual(format_witness.logical_immediate(25, -0x8000), 0x80000000)
        with self.assertRaises(WitnessError):
            format_witness.logical_immediate(14, 1)
        self.assertEqual(format_witness.ride_flags_from_builder_word({0x10: 0x2, 0x200: 0x100}, 0x210), 0x102)
        self.assertEqual(format_witness.ride_flags_from_builder_word({0x10: 0x2}, 0x20), 0)

    def test_gpr_destination(self):
        addi_r17 = (14 << 26) | (17 << 21) | (4 << 16)
        stw_r17 = (36 << 26) | (17 << 21) | (1 << 16) | 8
        ori_into_r17 = (24 << 26) | (3 << 21) | (17 << 16) | 1
        or_into_r17 = (31 << 26) | (5 << 21) | (17 << 16) | (5 << 11) | (444 << 1)
        lwzx_r17 = (31 << 26) | (17 << 21) | (3 << 16) | (4 << 11) | (23 << 1)
        stwx_r17 = (31 << 26) | (17 << 21) | (3 << 16) | (4 << 11) | (151 << 1)
        cmpw_r17 = (31 << 26) | (17 << 16) | (4 << 11)
        self.assertEqual([format_witness.gpr_destination(w) for w in
                          (addi_r17, stw_r17, ori_into_r17, or_into_r17, lwzx_r17, stwx_r17, cmpw_r17)],
                         [17, None, 17, 17, 17, None, None])

    def test_channel_flag_scan(self):
        class Words:
            def __init__(self, words):
                self.words = words

            def w(self, offset):
                return self.words[offset // 4]

        lwz = (32 << 26) | (0 << 21) | (26 << 16)
        ori_40 = (24 << 26) | (0 << 21) | (0 << 16) | 0x40
        clear_40 = (21 << 26) | (0 << 21) | (0 << 16) | (26 << 6) | (24 << 1)
        clear_60 = (21 << 26) | (0 << 21) | (0 << 16) | (27 << 6) | (24 << 1)
        stw_0 = (36 << 26) | (0 << 21) | (26 << 16)
        stw_4 = stw_0 | 4
        stw_other = (36 << 26) | (3 << 21) | (26 << 16)
        nop = 24 << 26
        words = [lwz, ori_40, stw_0, ori_40, stw_4, ori_40, stw_other, clear_40, nop, stw_0, clear_60, stw_0,
                 nop, nop, nop]
        self.assertEqual(format_witness.channel_flag_0x40_stores(Words(words), 0, len(words) * 4),
                         [(4, 'set'), (28, 'clear')])

    def test_register_flow(self):
        class Words:
            def __init__(self, words):
                self.words = words

            def w(self, offset):
                return self.words[offset // 4]

        words = [(32 << 26) | (30 << 21) | (2 << 16) | (0x10000 - 0x75d8),  # lwz r30, clock object
                 (14 << 26) | (3 << 21) | (30 << 16) | 0x44,  # addi r3, r30, 0x44
                 (36 << 26) | (0 << 21) | (30 << 16) | 8,  # stw r0, 8(r30): store through it
                 (36 << 26) | (30 << 21) | (1 << 16),  # stw r30, 0(r1): the pointer escapes
                 (18 << 26) | 8 | 1,  # bl +8 with r3 = object + 0x44; r3 dies, r30 survives
                 (31 << 26) | (30 << 21) | (29 << 16) | (30 << 11) | (444 << 1),  # mr r29, r30
                 (14 << 26) | (30 << 21),  # li r30, 0: r30 no longer holds it
                 (36 << 26) | (29 << 16) | 4,  # stw r0, 4(r29): store through the copy
                 (36 << 26) | (30 << 16) | 12,  # stw r0, 12(r30): not the object any more
                 format_witness.BLR]
        flow = format_witness.register_flow(Words(words), 0, 30)
        self.assertEqual(flow, {'stores': [(8, 'stw', 8), (28, 'stw', 4)], 'escapes': [12],
                                'calls': [(16, 24, {3: 0x44})]})
        stmw_r29 = (47 << 26) | (29 << 21) | (1 << 16) | (0x10000 - 12)
        self.assertEqual(format_witness.register_flow(Words(words[:1] + [stmw_r29, format_witness.BLR]), 0, 30),
                         {'stores': [], 'escapes': [4], 'calls': []}, 'stmw r29 spills r30 too')
        with self.assertRaises(WitnessError):
            format_witness.register_flow(Words(words[:-1] + [0] * 4), 0, 30, limit=8)

    def test_hexify_only_addresses(self):
        self.assertEqual(format_witness.hexify({'handler': 16, 'cells': 16, 'loader_call_sites': [1, 2]}),
                         {'handler': '0x10', 'cells': 16, 'loader_call_sites': ['0x1', '0x2']})


class CorpusReaderTests(unittest.TestCase):
    def test_refpack_literals_and_copy(self):
        # Header: flags 0x10 0xfb, 3-byte size 6; one 2-byte copy command with 3 literals.
        stream = bytes((0x10, 0xfb, 0, 0, 6)) + bytes((0x03, 0x02)) + b'abc' + bytes((0xfc,))
        self.assertEqual(corpus_check.refpack(stream), b'abcabc')
        with self.assertRaises(corpus_check.CorpusError):
            corpus_check.refpack(bytes((0x10, 0xfb, 0, 0, 6)) + bytes((0x03, 0x0f)) + b'abc' + bytes((0xfc,)))
        with self.assertRaises(corpus_check.CorpusError):
            corpus_check.refpack(bytes((0x10, 0xfb, 0x7f, 0, 0, 0xfc)), limit=16)
        with self.assertRaises(corpus_check.CorpusError):
            corpus_check.refpack(b'\x10\xfb')

    def test_wad_directory_bounds(self):
        with TemporaryDirectory() as directory:
            path = Path(directory) / 'x.wad'
            header = b'DWFB' + bytes(68) + struct.pack('<IIII', 1, 88, 40, 0)
            entry = struct.pack('<7I', 0, 128, 5, 133, 3, 0, 3) + bytes(12)
            path.write_bytes(header + entry + b'a.md2' + b'xyz')
            self.assertEqual(list(corpus_check.wad_members(path)), [('a.md2', b'xyz')])
            path.write_bytes(header + struct.pack('<7I', 0, 128, 5, 133, 99, 0, 3) + bytes(12) + b'a.md2xyz')
            with self.assertRaises(corpus_check.CorpusError):
                list(corpus_check.wad_members(path))

    def test_bounded_integer_reads(self):
        self.assertEqual(corpus_check.u16(b'\x01\x02', 0), 0x0201)
        with self.assertRaises(corpus_check.CorpusError):
            corpus_check.u32(b'\0\0\0', 0)

    def test_zlib_is_available_for_tpws_payloads(self):
        self.assertEqual(zlib.decompress(zlib.compress(b'payload')), b'payload')


if __name__ == '__main__':
    unittest.main()

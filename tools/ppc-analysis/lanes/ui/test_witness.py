"""Corpus and hostile-input tests for the bounded UI evidence lane."""
import os
from pathlib import Path
import struct
import types
import unittest

from corpus import layout_table, model_summary, resource_index, span, unpack
from witness import branch, d_form, inspect


class BoundsTests(unittest.TestCase):
    def test_span_rejects_negative_and_overflow(self):
        for offset, length in [(-1, 1), (0, -1), (2, 1), (1, 2)]:
            with self.assertRaises(ValueError):
                span(b'ab', offset, length)

    def test_refpack_literal_and_overlap(self):
        # One literal, then repeated backwards references can overlap output.
        compressed = b'\x10\xfb\x00\x00\x04\x01\x00A\xfc'
        self.assertEqual(unpack(compressed), b'AAAA')

    def test_refpack_invalid_reference(self):
        with self.assertRaisesRegex(ValueError, 'back reference'):
            unpack(b'\x10\xfb\x00\x00\x03\x00\x00\xfc')

    def test_refpack_declared_length_and_terminal(self):
        for payload in [b'\x10\xfb\x00\x00\x01\xfc',
                        b'\x10\xfb\x00\x00\x00\xfcX',
                        b'\x10\xfb\x00\x00\x00\xfdA']:
            with self.assertRaises(ValueError):
                unpack(payload)

    def test_resource_offsets_are_relative_to_byte_eight(self):
        index = b'BFRI' + struct.pack('<I', 1) + struct.pack('<IHHI', 42, 4, 12, 0) + b'FONT.bf4\0'
        self.assertEqual(resource_index(index), {42: 'FONT.bf4'})
        with self.assertRaises(ValueError):
            resource_index(index[:-1])

    def test_model_rejects_unbounded_node_table(self):
        data = bytearray(184)
        struct.pack_into('<3I', data, 0, 0x1cd15d46, 221, 203)
        struct.pack_into('<HH', data, 0x42, 513, 1)
        with self.assertRaisesRegex(ValueError, 'node count'):
            model_summary(bytes(data))

    def test_signed_instruction_operand(self):
        word = (14 << 26) | (3 << 21) | (4 << 16) | 65535
        container = types.SimpleNamespace(code=types.SimpleNamespace(data=struct.pack('>I', word)))
        self.assertEqual(d_form(container, 0, 14, (3, 4, -1)), (3, 4, -1))
        with self.assertRaises(ValueError):
            d_form(container, 0, 14, (3, 4, 65535))

    def test_layout_signed_coordinates_and_low_high_id(self):
        words = [0, 1, 0, 0, 2, 1, -4, -3, 6, 9, 17, -1, -1, 5, 5]
        data = struct.pack('>' + 'H' * len(words), *(word & 65535 for word in words))
        result = layout_table(data, 0)
        self.assertEqual(result['windows'][0]['id'], 65538)
        self.assertEqual(result['windows'][0]['rectangle'], [-4, -3, 6, 9])
        self.assertEqual(result['words_consumed'], len(words))

    def test_layout_unknown_and_truncated_commands_fail(self):
        with self.assertRaisesRegex(ValueError, 'unsupported layout command'):
            layout_table(struct.pack('>H', 19), 0)
        with self.assertRaisesRegex(ValueError, 'out-of-range'):
            layout_table(struct.pack('>2H', 0, 1), 0)

    def test_branch_decodes_negative_displacement_and_rejects_unlinked(self):
        word = (18 << 26) | (0x3fffffc) | 1
        container = types.SimpleNamespace(code=types.SimpleNamespace(data=struct.pack('>I', word)))
        self.assertEqual(branch(container, 0), -4)
        container.code.data = struct.pack('>I', word & ~1)
        with self.assertRaises(ValueError):
            branch(container, 0)


@unittest.skipUnless(os.environ.get('UI_EVIDENCE_BIN_ROOT'), 'private corpus paths not supplied')
class CorpusTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.report = inspect(Path(os.environ['UI_EVIDENCE_BIN_ROOT']),
                             Path(os.environ['UI_EVIDENCE_MAC_UI']),
                             Path(os.environ['UI_EVIDENCE_RESIDX']),
                             Path(os.environ['UI_EVIDENCE_PC_UI']))

    def test_all_banks_and_edition_difference(self):
        self.assertEqual([len(bank) for bank in self.report['font_banks']], [13] * 4)
        self.assertEqual(self.report['font_banks'][0][3]['file'], 'DATETINY.bf4')
        self.assertEqual(self.report['font_banks'][3][3]['file'], 'DATEBIG.bf4')
        comparison = self.report['edition_comparison']
        self.assertEqual(comparison['identical_models'], 278)
        self.assertEqual(comparison['members'], 1202)
        self.assertEqual(len(comparison['changed']), 2)

    def test_assets_and_code_have_independent_witnesses(self):
        self.assertEqual([node['name'] for node in self.report['asset_models']['b_buy.MD2']['nodes']],
                         ['b_buy', 'disable', 'hilite', 'heldown', 'hidown', 'down'])
        bounds = self.report['asset_models']['f_chat.MD2']
        self.assertAlmostEqual(bounds['bounds_max'][0] - bounds['bounds_min'][0], 2048, places=3)
        self.assertAlmostEqual(bounds['bounds_max'][1] - bounds['bounds_min'][1], 1536, places=3)
        self.assertEqual(self.report['popup']['bottom'], 1520)
        self.assertEqual(self.report['sign']['mac_backend']['symbol'], 'StdText')
        self.assertEqual(self.report['resolved_approximation_ids'], [])
        windows = {window['id']: window for window in self.report['annual_summary_layout']['windows']}
        self.assertEqual(windows[44450]['parent'], 44449)
        self.assertEqual(windows[44461]['rectangle'], [1244, 427, 1479, 472])


if __name__ == '__main__':
    unittest.main()

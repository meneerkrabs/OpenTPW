"""Catalog event identity and unsupported/corrupted boundary regressions."""
import os
from pathlib import Path
import types
import unittest

from catalog_events import inspect, prove, decode_sort_word, header_column, scroll_range
from witness import APP_SHA, identified


class CatalogDecoderTests(unittest.TestCase):
    def test_signed_selector_is_distinct_from_header_id(self):
        self.assertEqual(decode_sort_word(1), {'column': 0, 'direction_bit': 0})
        self.assertEqual(decode_sort_word(-3), {'column': 2, 'direction_bit': 1})
        self.assertEqual([header_column(value) for value in (16, 17, 18)], [0, 1, 2])

    def test_unsupported_sort_and_header_domains_rejected(self):
        for value in (0, 4, -4, 16, 0x7fffffff, True, None, "1"):
            with self.assertRaises(ValueError):
                decode_sort_word(value)
        for value in (-1, 15, 19, 504):
            with self.assertRaises(ValueError):
                header_column(value)

    def test_range_carries_first_and_bounded_first_plus_visible(self):
        self.assertEqual(scroll_range(4, 3, 12), (4, 7))
        self.assertEqual(scroll_range(10, 3, 12), (10, 11))
        for values in ((0, 0, 12), (-1, 3, 12), (12, 3, 12), (0, 3, 0), (0, 513, 600)):
            with self.assertRaises(ValueError):
                scroll_range(*values)


@unittest.skipUnless(os.environ.get('UI_EVIDENCE_BIN_ROOT'), 'private app path not supplied')
class NativeCatalogTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.root = Path(os.environ['UI_EVIDENCE_BIN_ROOT'])
        cls.app = identified(cls.root / 'SimThemePark.data', APP_SHA)
        cls.report = inspect(cls.root)

    def mutated(self):
        # Only in-memory private copies. Original code/data is never written.
        relocs = {section: dict(values) for section, values in self.app.relocs.items()}
        return types.SimpleNamespace(code=types.SimpleNamespace(data=bytearray(self.app.code.data), index=self.app.code.index),
                                     data_section=self.app.data_section, relocs=relocs, imports=self.app.imports)

    def test_callback_identities_keep_default_delegate_and_root_handler_separate(self):
        self.assertEqual(self.report['identity']['class'], 'InterfaceListControl2')
        self.assertEqual(self.report['callback_fields'], {'override': 272, 'default': 276})
        self.assertEqual((self.report['root_callback'], self.report['list_override_callback'], self.report['list_default_callback']), (0x163748, 0x163e7c, 0x17a5fc))

    def test_sort_selection_activation_and_scroll_use_distinct_events(self):
        self.assertEqual((self.report['header_command'], self.report['category_command']), (256, 257))
        self.assertEqual((self.report['sort_event'], self.report['range_event'], self.report['selection_event'], self.report['activation_event']), (1030, 1029, 1025, 1024))
        self.assertIn('queued', self.report['delivery']['selection_and_activation'])
        self.assertIn('synchronous', self.report['delivery']['sort_and_range'])
        self.assertEqual(self.report['column_types'], [0, 1, 1])
        self.assertIn('never root-name hash', self.report['row_identifier'])
        self.assertEqual(self.report['catalog_drawing_key'], -2006327033)
        with self.assertRaises(ValueError):
            decode_sort_word(self.report['catalog_drawing_key'])

    def test_unlinked_event_delivery_is_rejected(self):
        app = self.mutated()
        app.code.data[0x17aa2f] &= 0xfe
        with self.assertRaisesRegex(ValueError, 'linked relative branch'):
            prove(app)

    def test_missing_root_callback_relocation_is_rejected(self):
        app = self.mutated()
        del app.relocs[app.data_section.index][0x8090]
        with self.assertRaisesRegex(ValueError, 'missing relocation'):
            prove(app)

    def test_retagged_rtti_and_wrong_event_operand_are_rejected(self):
        for at in (self.report['identity']['rtti_name_code'], 0x17a4d7):
            app = self.mutated()
            app.code.data[at] ^= 1
            with self.assertRaises(ValueError):
                prove(app)

    def test_changed_activation_predicate_is_rejected(self):
        app = self.mutated()
        app.code.data[0x17aa11] ^= 128
        with self.assertRaisesRegex(ValueError, 'catalog conditional'):
            prove(app)


if __name__ == '__main__':
    unittest.main()

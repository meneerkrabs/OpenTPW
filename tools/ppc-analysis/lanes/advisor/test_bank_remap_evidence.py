"""Synthetic ordinal/identity tests, with optional supplied binary/corpus checks."""
import os
from pathlib import Path
import struct
import unittest

import bank_remap_evidence as remap


def bank_blob(paths):
    data = bytearray(28 + 11 * len(paths))
    struct.pack_into('<I', data, 24, len(paths))
    for index in range(len(paths)):
        struct.pack_into('<IBIH', data, 28 + 11 * index, 123456, 99, 654321, 1)
    for path in paths:
        raw = path.encode() + b'\0'
        data += struct.pack('<I', len(raw)) + raw
    return data


class RemapTests(unittest.TestCase):
    def test_record_order_is_one_based_and_cache_words_do_not_change_paths(self):
        data = bank_blob(['Sound\\First', 'Sound\\Second'])
        expected = [{'ordinal': 1, 'family': 'Sound\\First', 'flags': 1},
                    {'ordinal': 2, 'family': 'Sound\\Second', 'flags': 1}]
        self.assertEqual(remap.bank_records(data), expected)
        for offset in (28, 33, 39, 44):
            struct.pack_into('<I', data, offset, 0)
        self.assertEqual(remap.bank_records(data), expected)

    def test_bank_ordinal_resolves_before_sample_ordinal(self):
        records = remap.bank_records(bank_blob(['Sound\\First', 'Sound\\Second']))
        entries = {'Sound\\First': [{'name': 'first', 'offset': 40}],
                   'Sound\\Second': [{'name': 'second', 'offset': 200}, {'name': 'third.mp', 'offset': 400}]}
        self.assertEqual(remap.resolve_choice({'bank_id': 2, 'sample_id': 2}, records, entries),
                         {'bank_ordinal': 2, 'family': 'Sound\\Second', 'sample_ordinal': 2,
                          'stored_name': 'third.mp', 'entry_offset': 400})

    def test_truncated_duplicate_names_remain_distinct_numeric_members(self):
        records = remap.bank_records(bank_blob(['Sound\\Duplicate']))
        entries = {'Sound\\Duplicate': [{'name': 'truncated_name.', 'offset': 40},
                                       {'name': 'truncated_name.', 'offset': 200}]}
        first = remap.resolve_choice({'bank_id': 1, 'sample_id': 1}, records, entries)
        second = remap.resolve_choice({'bank_id': 1, 'sample_id': 2}, records, entries)
        self.assertEqual(first['stored_name'], second['stored_name'])
        self.assertNotEqual(first['entry_offset'], second['entry_offset'])

    def test_zero_out_of_range_bank_and_sample_are_not_repaired(self):
        records = remap.bank_records(bank_blob(['Sound\\First']))
        entries = {'Sound\\First': [{'name': 'first', 'offset': 40}]}
        for bank, sample in [(0, 1), (2, 1), (1, 0), (1, 2)]:
            with self.subTest(bank=bank, sample=sample), self.assertRaises(remap.common.pef.PEFError):
                remap.resolve_choice({'bank_id': bank, 'sample_id': sample}, records, entries)

    def test_bad_bank_path_and_truncation_rejected(self):
        with self.assertRaises(remap.common.pef.PEFError):
            remap.bank_records(bank_blob(['Sound\\First'])[:-1])
        with self.assertRaises(remap.common.pef.PEFError):
            remap.case_path(Path('/tmp'), '../escape')

    def test_quality_suffix_is_explicit_and_bounded(self):
        with self.assertRaises(remap.common.pef.PEFError):
            remap.corpus(Path('/tmp'), '../bank.sdt')

    def test_same_catalog_id_in_different_contexts_is_not_a_global_alias(self):
        pc = {'selected': [{'catalog': 'one', 'catalog_id': 10000, 'choices': ['first']},
                           {'catalog': 'two', 'catalog_id': 10000, 'choices': ['second']}]}
        mac = {'selected': [{'catalog': 'one', 'catalog_id': 10000, 'choices': ['first']},
                            {'catalog': 'two', 'catalog_id': 10000, 'choices': ['changed']}]}
        self.assertEqual([row['resolved_choices_equal'] for row in remap.compare_selected(pc, mac)], [True, False])

    @unittest.skipUnless(os.environ.get('OPENTPW_MAC_DATA'), 'selected extracted Mac corpus not supplied')
    def test_actual_mac_catalog_bank_and_clip_metadata(self):
        result = remap.corpus(Path(os.environ['OPENTPW_MAC_DATA']), 'HD.sdt')
        self.assertEqual(result['resolved_choices'], 1105)
        self.assertEqual(len(result['catalogs']), 4)
        selected = next(row for row in result['selected'] if row['catalog'] == 'levels/hallow/Sound/cat_ridesSFX.map' and row['catalog_id'] == 175)
        self.assertEqual({row['family'] for row in selected['choices']}, {'Sound\\xRide'})
        self.assertEqual(selected['choices'][0]['stored_name'], 'mt_crmbl1.mp2')

    @unittest.skipUnless(os.environ.get('OPENTPW_PPC_BIN_ROOT'), 'supplied Mac binaries not selected')
    def test_selected_native_new_reuse_fixup_and_teardown_operands(self):
        result = remap.native(Path(os.environ['OPENTPW_PPC_BIN_ROOT']))
        self.assertEqual(result['new_append'], 0x15c30)
        self.assertEqual(result['reused_append'], 0x15638)
        self.assertEqual(result['sample_bank_rewrite'], 0x16b04)

    @unittest.skipUnless(os.environ.get('OPENTPW_PC_DATA'), 'supplied PC corpus not selected')
    def test_selected_pc_catalog_choices_resolve_through_bank_maps(self):
        result = remap.corpus(Path(os.environ['OPENTPW_PC_DATA']), 'HD.sdt')
        self.assertEqual(result['resolved_choices'], 3631)
        selected = next(row for row in result['selected'] if row['catalog'] == 'levels/fantasy/Sound/cat_ridesSFX.map' and row['catalog_id'] == 145)
        self.assertEqual({row['family'] for row in selected['choices']}, {'Sound\\xRide'})
        self.assertEqual(selected['choices'][0]['stored_name'], 'dull_crmbl1.mp2')


if __name__ == '__main__':
    unittest.main()

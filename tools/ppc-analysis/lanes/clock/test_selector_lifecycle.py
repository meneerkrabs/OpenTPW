"""Decoded numeric selector route regressions; no native instructions executed."""
import os
from pathlib import Path
import struct
import unittest

import selector_lifecycle as selector


RULES = {
    'selector_zero_continue': {'bo': 4, 'bi': 2},
    'selector_one_skip_pre_hook': {'bo': 12, 'bi': 2},
    'selector_one_full_body': {'bo': 4, 'bi': 2},
    'selector_one_skip_post_hook': {'bo': 12, 'bi': 2},
}


class SelectorLifecycleTests(unittest.TestCase):
    def test_selector1_prefix_return_excludes_later_clock_and_script_reads(self):
        self.assertEqual({'route': 'prefix_return1', 'allows_clock_pair_read': False,
                          'allows_saved_script_read': False, 'calls_pre_hook': False, 'calls_post_hook': False},
                         selector.successful_route(1, RULES))

    def test_selector0_returns_after_header_before_reader_and_hooks(self):
        self.assertEqual({'route': 'header_return1', 'allows_clock_pair_read': False,
                          'allows_saved_script_read': False, 'calls_pre_hook': False, 'calls_post_hook': False},
                         selector.successful_route(0, RULES))

    def test_other_selectors_allow_full_body_and_both_hooks(self):
        for value in [-1, 2, 3, 0x7fffffff, -0x80000000]:
            with self.subTest(value=value):
                result = selector.successful_route(value, RULES)
                self.assertEqual('full_body', result['route'])
                self.assertTrue(result['allows_clock_pair_read'])
                self.assertTrue(result['allows_saved_script_read'])
                self.assertTrue(result['calls_pre_hook'])
                self.assertTrue(result['calls_post_hook'])

    def test_nonregister_selector_values_are_rejected(self):
        for value in [1 << 31, -(1 << 31) - 1]:
            with self.subTest(value=value), self.assertRaises(ValueError):
                selector.successful_route(value, RULES)

    def test_decoder_distinguishes_eq_clear_and_eq_set(self):
        for bo in [4, 12]:
            code = struct.pack('>I', (16 << 26) | (bo << 21) | (2 << 16) | 16)
            branch = selector.conditional(code, 0)
            self.assertEqual((bo, 2, 16), (branch['bo'], branch['bi'], branch['target']))
            self.assertEqual(bo == 12, selector.branch_taken(branch, True))
            self.assertEqual(bo == 4, selector.branch_taken(branch, False))

    def test_decoder_rejects_lookalike_lt_or_counter_branch(self):
        for bo, bi in [(12, 0), (16, 2), (0, 2)]:
            code = struct.pack('>I', (16 << 26) | (bo << 21) | (bi << 16) | 16)
            with self.subTest(bo=bo, bi=bi), self.assertRaises(selector.pef.PEFError):
                selector.conditional(code, 0)
        with self.assertRaises(selector.pef.PEFError):
            selector.conditional(bytes(4), 0)
        with self.assertRaises(selector.pef.PEFError):
            selector.conditional(bytes(3), 0)

    @unittest.skipUnless(os.environ.get('OPENTPW_PPC_BIN_ROOT'), 'requires identified original PEF')
    def test_actual_source_route_excludes_selector1_restore(self):
        result = selector.inspect(Path(os.environ['OPENTPW_PPC_BIN_ROOT']))
        self.assertEqual(4, len(result['region_sha256']))
        route = result['successful_control_routes']['1']
        self.assertFalse(route['allows_clock_pair_read'])
        self.assertFalse(route['allows_saved_script_read'])
        self.assertEqual(0x11b674, result['branch_rules']['selector_one_full_body']['target'])
        self.assertEqual('full_body', result['successful_control_routes']['2']['route'])


if __name__ == '__main__':
    unittest.main()

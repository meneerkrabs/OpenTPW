"""Tests for fail-closed static witnesses, with optional identified local assets."""
import os
from pathlib import Path
import struct
from types import SimpleNamespace
import unittest

import evidence


class WitnessTests(unittest.TestCase):
    def test_distance_division_at_clamp_boundary(self):
        self.assertEqual(evidence.distance_quotient(441), 98)
        self.assertEqual(evidence.distance_quotient(449), 99)
        self.assertEqual(evidence.distance_quotient(450), 100)

    def test_region_add_remove_cancel_for_signed_coefficients(self):
        for coefficient in (-32768, -3, -1, 0, 1, 3, 32767):
            for dx, dy in ((0, 0), (1, 0), (3, 3), (-3, 2)):
                self.assertEqual(evidence.region_delta(coefficient, dx, dy),
                                 -evidence.region_delta(coefficient, dx, dy, False))

    def test_region_signed_division_truncates_toward_zero(self):
        self.assertEqual(evidence.region_delta(-1, 1, 0), 0)
        self.assertEqual(evidence.region_delta(-3, 1, 0), -1)
        self.assertEqual(evidence.region_delta(10, 3, 3), 1)

    def test_ride_illness_depends_on_hunger_with_integer_bands(self):
        self.assertEqual(evidence.ride_illness_increment(70, 10, 0), 35)
        self.assertEqual(evidence.ride_illness_increment(70, 10, 81), 0)
        self.assertEqual(evidence.ride_illness_increment(79, 10, 80), 7)

    def test_nested_guards_exclude_nonzero_id_phases_from_fixed_growth(self):
        self.assertEqual(evidence.needs_phase(16, 4), (True, True))
        self.assertEqual(evidence.needs_phase(16, 5), (False, False))
        self.assertEqual(evidence.needs_phase(17, 5), (True, False))
        self.assertEqual(evidence.needs_phase(20, 4), (True, False))

    def test_signed_displacement(self):
        word = (32 << 26) | (12 << 21) | (2 << 16) | 0xfffc
        c = SimpleNamespace(code=SimpleNamespace(data=struct.pack('>I', word)))
        self.assertEqual(evidence.d_fields(c, 0, 32), (12, 2, -4))

    def test_mismatched_operation_is_rejected(self):
        c = SimpleNamespace(code=SimpleNamespace(data=bytes(4)))
        with self.assertRaises(evidence.pef.PEFError):
            evidence.d_fields(c, 0, 32)

    def test_backward_linked_branch(self):
        c = SimpleNamespace(code=SimpleNamespace(data=struct.pack('>I', (18 << 26) | 0x03fffffd)))
        self.assertEqual(evidence.call_target(c, 0), -4)

    def test_missing_relocation_is_rejected(self):
        c = SimpleNamespace(data_section=SimpleNamespace(index=1), relocs={1: {}})
        with self.assertRaises(evidence.pef.PEFError):
            evidence.pointer(c, 0x100, 0, 0x200)

    def test_wrong_relocation_kind_is_rejected(self):
        c = SimpleNamespace(data_section=SimpleNamespace(index=1), relocs={
            1: {0x100: evidence.pef.RelocTarget('import', 0, 0x200)}})
        with self.assertRaises(evidence.pef.PEFError):
            evidence.pointer(c, 0x100, 0, 0x200)

    @unittest.skipUnless(os.environ.get('OPENTPW_PPC_BIN_ROOT'), 'requires local identified PEF')
    def test_identified_original_witness(self):
        report = evidence.inspect(Path(os.environ['OPENTPW_PPC_BIN_ROOT']))
        self.assertEqual(report['toc_offset'], 0x8000)
        self.assertEqual(len(report['state_dispatch']), 22)
        self.assertEqual(len(report['blocks']), 33)
        self.assertEqual(report['distance_divisor'], 450)
        self.assertEqual(report['fresh_slot_id_range'], [1, 10239])
        self.assertEqual(report['cell_initialization_import']['symbol'], 'memset')
        self.assertEqual(report['resolved_approximation_ids'], [])


if __name__ == '__main__':
    unittest.main()

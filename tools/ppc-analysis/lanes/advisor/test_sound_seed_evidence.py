"""Synthetic ownership boundaries and optional identified bundle linkage."""
import os
from pathlib import Path
import struct
from types import SimpleNamespace
import unittest

import sound_seed_evidence as seed


def container(symbol_class=1, target=1):
    return SimpleNamespace(
        data_section=SimpleNamespace(index=1),
        code=SimpleNamespace(data=b''),
        exports=[SimpleNamespace(name=seed.SEED, sym_class=symbol_class, section=1, value=0xc2e4)],
        relocs={1: {0x348: SimpleNamespace(kind='section', target=target, addend=0xc2e4)}})


class SoundSeedTests(unittest.TestCase):
    def test_seed_alias_is_a_data_pointer_not_an_executable_vector(self):
        report = seed.ownership(container())
        self.assertEqual(report['export_class'], 1)
        self.assertEqual(report['alias_offset'], 0x348)
        self.assertEqual(report['alias_direct_accesses'], [])
        with self.assertRaises(seed.common.pef.PEFError):
            seed.ownership(container(symbol_class=2))
        with self.assertRaises(seed.common.pef.PEFError):
            seed.ownership(container(target=0))

    def test_alias_access_scan_rebases_toc_and_requires_r2(self):
        c = container()
        displacement = (0x348 - 0x8000) & 65535
        words = [(32 << 26) | (5 << 21) | (2 << 16) | displacement,
                 (14 << 26) | (6 << 21) | (2 << 16) | displacement,
                 (32 << 26) | (5 << 21) | (3 << 16) | displacement]
        c.code.data = b''.join(struct.pack('>I', word) for word in words)
        self.assertEqual(seed.direct_alias_accesses(c, 0x348, 0x8000), [0, 4])
        self.assertEqual(seed.direct_alias_accesses(c, 0x348, 0), [])

    def test_relocated_indirect_pointer_to_alias_is_reported(self):
        c = container()
        c.relocs[1][0x200] = SimpleNamespace(kind='section', target=1, addend=0x348)
        self.assertEqual(seed.ownership(c)['alias_incoming_relocations'], [(1, 0x200)])

    def test_pascal_lookup_names_are_bounded_not_null_terminated(self):
        c = SimpleNamespace(sections=[SimpleNamespace(data=b'\x04TimeSuffix')])
        self.assertEqual(seed.pascal(c, 0, 0), 'Time')
        c.sections[0].data = b'\x04Tim'
        with self.assertRaises(seed.common.pef.PEFError):
            seed.pascal(c, 0, 0)

    def test_selection_reuses_snapshot_until_an_explicit_external_change(self):
        first = seed.selection.candidate(123)
        self.assertEqual(first, seed.selection.candidate(123))
        self.assertNotEqual(first, seed.selection.candidate(124))

    @unittest.skipUnless(os.environ.get('OPENTPW_PPC_BIN_ROOT'), 'supplied Mac bundle not selected')
    def test_identified_bundle_seed_export_alias_and_clock_only_symbol_lookup(self):
        report = seed.native(Path(os.environ['OPENTPW_PPC_BIN_ROOT']))
        self.assertEqual(len(report['bundle_sha256']), 16)
        self.assertEqual(report['seed_imports'], [])
        self.assertEqual(report['dynamic_lookup_targets'], ['DriverServicesLib', 'UpTime', 'AbsoluteToNanoseconds'])


if __name__ == '__main__':
    unittest.main()

"""Round-10 tests. The script-manager walk is exercised on synthetic blocks; the operand audits run
only when OPENTPW_PPC_BIN_ROOT names the identified Feral Mac bin directory (each audit is also
checked to fail on a one-word mutation), and the fixture walk only when OPENTPW_PC_DATA names a TPW
Data directory holding the identified jungle Easymode.TPWI.
"""
from __future__ import annotations

import copy
import os
import struct
import sys
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import round9_evidence as r9  # noqa: E402
import round10_evidence as r10  # noqa: E402
from review_evidence import APP_TOC, Binary, ReviewError  # noqa: E402


def script(identifier: int, md32_records: int = 0, objects: int = 0, wait: int = 0) -> bytes:
    fixed = bytearray(244)
    struct.pack_into('<I', fixed, 8, identifier)
    struct.pack_into('<I', fixed, 160, wait)
    blocks = b''
    for index in range(9):
        if index == 6:
            blocks += struct.pack('<I', md32_records) + bytes(32 * md32_records)
        else:
            blocks += struct.pack('<I', 3) + b'abc'      # byte lengths; unaligned on purpose
    return bytes(fixed) + blocks + b'OBJ ' + struct.pack('<II', objects, 28) + bytes(28 * objects)


def manager(*records: bytes, count: int | None = None, pad: bytes = b'PAD_') -> bytes:
    header = struct.pack('<5I', 1, 7, 99, len(records) if count is None else count, 0x1234)
    return (b'RSSE' + struct.pack('<I', 20) + header + pad * 5
            + struct.pack('<II', len(records), 244) + b''.join(records) + b'ESSR')


class ScriptWalk(unittest.TestCase):
    def test_walk_frames_records_and_projects_count(self):
        block = b'\0' * 5 + manager(script(2, md32_records=2, objects=1, wait=77), script(1))
        out = r10.script_walk(block, offset=5)
        self.assertEqual((out['ids'], out['records'], out['object_bindings']), ([2, 1], 2, 1))
        self.assertEqual((out['next_magic'], out['nonzero_deadlines']), ('ESSR', {2: [77, 0, 0]}))
        self.assertEqual(out['projected_count_after_load'], 4)
        self.assertFalse(out['matches_lane'])

    def test_metadata32_is_a_record_count(self):
        # Treating md32's word as a byte length lands mid-block and misses the OBJ marker.
        block = manager(script(1, md32_records=1))
        self.assertEqual(r10.script_walk(block, offset=0)['records'], 1)
        broken = bytearray(block)
        start = 8 + 20 + 20 + 8 + 244 + 6 * 7
        struct.pack_into('<I', broken, start, 32)        # 32 records = 1,024 bytes: overruns
        with self.assertRaises((ReviewError, struct.error)):
            r10.script_walk(bytes(broken), offset=0)

    def test_stored_count_is_kept_apart_from_physical_records(self):
        out = r10.script_walk(manager(script(1), count=9), offset=0)
        self.assertEqual((out['header']['count'], out['records'], out['projected_count_after_load']), (9, 1, 10))

    def test_review_walk_requires_pad_although_native_does_not(self):
        with self.assertRaises(ReviewError):
            r10.script_walk(manager(script(1), pad=b'XXXX'), offset=0)

    def test_wrong_magic_rejected(self):
        with self.assertRaises(ReviewError):
            r10.script_walk(b'ESSR' + manager(script(1))[4:], offset=0)


@unittest.skipUnless(os.environ.get('OPENTPW_PPC_BIN_ROOT'), 'set OPENTPW_PPC_BIN_ROOT for the operand audit')
class OriginalOperands(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.app = Binary(Path(os.environ['OPENTPW_PPC_BIN_ROOT']), 'SimThemePark.data', 'SimThemePark.data', APP_TOC)

    def mutated(self, at: int, word: int) -> Binary:
        app = copy.copy(self.app)
        code = bytearray(self.app.code)
        struct.pack_into('>I', code, at, word)
        app.code = bytes(code)
        return app

    def test_saved_script(self):
        out = r10.saved_script(self.app)
        self.assertEqual(out['callee_manager_loads'], [])
        self.assertIn('+230', out['rewritten_on_load'])

    def test_head_clear_is_required(self):
        with self.assertRaises(ReviewError):
            r10.saved_script(self.mutated(0xb49ac, 0x60000000))   # nop instead of stw r0,16(r26)

    def test_topology_order(self):
        self.assertIn('flag-0x10', r10.topology_order(self.app)['prerequisite'])

    def test_topology_third_node_is_required(self):
        with self.assertRaises(ReviewError):
            r10.topology_order(self.mutated(0x3a35c, 0x60000000))

    def test_catalog_delivery(self):
        self.assertEqual(r10.catalog_delivery(self.app)['row_id_status_untested'], ['0x163998', '0x163d74'])

    def test_queue_drop_branch_is_required(self):
        with self.assertRaises(ReviewError):
            r10.catalog_delivery(self.mutated(0x16fd98, 0x60000000))


@unittest.skipUnless(os.environ.get('OPENTPW_PC_DATA'), 'set OPENTPW_PC_DATA to a TPW Data directory')
class PcFixture(unittest.TestCase):
    def test_script_manager_block(self):
        payload = r9.pc_payload(Path(os.environ['OPENTPW_PC_DATA']) / 'levels' / 'jungle' / 'Easymode.TPWI')
        out = r10.script_walk(payload)
        self.assertTrue(out['matches_lane'])
        self.assertEqual((out['header']['count'], out['header']['pass'], out['header']['next_id']), (14, 6055, 16))
        self.assertEqual((out['object_bindings'], out['projected_count_after_load'], out['next_magic']), (3, 28, 'ESSR'))
        self.assertEqual(out['nonzero_deadlines'][9], [114377145, 0, 0])


if __name__ == '__main__':
    unittest.main()

"""Synthetic rejection/decoder checks and optional identified original witness."""
from pathlib import Path
import os
import struct
import tempfile
from types import SimpleNamespace
import unittest

import evidence
import contracts
import controller_native


class RideEvidenceTests(unittest.TestCase):
    def fixture(self, code=b"\0" * 16, relocations=None):
        return SimpleNamespace(code=SimpleNamespace(index=0, data=bytearray(code)),
                               data_section=SimpleNamespace(index=1, data=bytearray(16)),
                               relocs={1: relocations or {}})

    def test_identity_rejected_before_container_parse(self):
        with tempfile.TemporaryDirectory() as root:
            path = Path(root) / "foreign.data"
            path.write_bytes(b"not the original executable")
            with self.assertRaisesRegex(evidence.pef.PEFError, "identity"):
                evidence.identified(path)

    def test_linked_branch_signed_and_absolute(self):
        relative = (18 << 26) | ((-4) & 0x03fffffc) | 1
        absolute = (18 << 26) | 8 | 3
        fixture = self.fixture(struct.pack(">IIII", 0, relative, absolute, 0))
        self.assertEqual(evidence.call_target(fixture, 4), 0)
        self.assertEqual(evidence.call_target(fixture, 8), 8)

    def test_noncall_rejected(self):
        with self.assertRaisesRegex(evidence.pef.PEFError, "linked branch"):
            evidence.call_target(self.fixture(), 0)

    def test_missing_and_import_relocation_rejected(self):
        with self.assertRaisesRegex(evidence.pef.PEFError, "missing relocation"):
            evidence.pointer(self.fixture(), 0, 1)
        fixture = self.fixture(relocations={0: evidence.pef.RelocTarget("import", 1, 0)})
        with self.assertRaisesRegex(evidence.pef.PEFError, "relocation target"):
            evidence.pointer(fixture, 0, 1)

    def test_switch_bounds_and_expected_addresses(self):
        relocations = {0: evidence.pef.RelocTarget("section", 1, 4),
                       4: evidence.pef.RelocTarget("section", 0, 8)}
        fixture = self.fixture(relocations=relocations)
        self.assertEqual(evidence.table(fixture, 0, 1, 4, [8]), [8])
        with self.assertRaisesRegex(evidence.pef.PEFError, "switch targets"):
            evidence.table(fixture, 0, 1, 4, [12])
        relocations[4].addend = 16
        with self.assertRaisesRegex(evidence.pef.PEFError, "out-of-range"):
            evidence.table(fixture, 0, 1, 4)
        relocations[4].addend = 3
        with self.assertRaisesRegex(evidence.pef.PEFError, "unaligned"):
            evidence.table(fixture, 0, 1, 4)

    def test_operand_decode_rejects_wrong_operation(self):
        # Synthetic store register 4 into object register 7 at offset -8.
        word = (36 << 26) | (4 << 21) | (7 << 16) | 0xfff8
        fixture = self.fixture(struct.pack(">IIII", word, 0, 0, 0))
        self.assertEqual(evidence.d_fields(fixture, 0, 36), (4, 7, -8))
        with self.assertRaises(evidence.pef.PEFError):
            evidence.d_fields(fixture, 0, 32)

    @unittest.skipUnless(os.environ.get("OPENTPW_MAC_APP"), "set OPENTPW_MAC_APP for original-file witness")
    def test_original_witness(self):
        result = evidence.inspect(Path(os.environ["OPENTPW_MAC_APP"]))
        self.assertEqual(result["sha256"], evidence.SHA)
        self.assertEqual(result["plain_channel"], 0)
        self.assertEqual(len(result["animation_binding"]), 12)
        self.assertGreaterEqual(result["checked_instruction_count"], 50)
        self.assertEqual(len(result["controller_case_addresses"]["COAST"]), 9)

    @unittest.skipUnless(os.environ.get("OPENTPW_MAC_APP"), "set OPENTPW_MAC_APP for native controller witnesses")
    def test_original_controller_contracts(self):
        path = Path(os.environ["OPENTPW_MAC_APP"])
        result = contracts.inspect(path)
        coast = {c["raw_command"]: c for c in result["commands"] if c["family"] == "COAST"}
        self.assertEqual(coast[2]["direct_controller_calls"], [0x3dd14])
        self.assertEqual(coast[3]["direct_controller_calls"], [0x3dd5c])
        self.assertEqual(coast[6]["direct_controller_calls"], [0x3df24])
        self.assertEqual(coast[7]["direct_controller_calls"], [])
        self.assertEqual(coast[4]["accumulator"], "preserve")
        layout = controller_native.inspect(path)
        self.assertEqual(layout["tour"]["allocation_bytes"], layout["tour"]["header_bytes"]
                         + layout["tour"]["record_stride"] * layout["tour"]["record_count"])
        self.assertGreaterEqual(layout["checked_instruction_count"], 60)


if __name__ == "__main__":
    unittest.main()

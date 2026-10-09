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
import animation_evidence
import motion_evidence


class RideEvidenceTests(unittest.TestCase):
    def test_scalar_descriptor_offsets_expand_arrays_and_skip_strings(self):
        records = [(2, "", 0), (7, "x", 0), (7, "y", 0), (3, "points", 3),
                   (0, "", 0), (1, "filename", 0), (7, "speed", 0), (12, "", 0)]
        self.assertEqual(motion_evidence.scalar_offsets(records), {1: ("x", 12), 2: ("y", 16), 6: ("speed", 40)})

    def test_scalar_descriptor_offsets_reject_unsupported_shapes(self):
        for records in ([(3, "orphan", 2), (12, "", 0)], [(2, "", 0), (12, "", 0)],
                        [(2, "", 0), (2, "nested", 0), (3, "end", 2), (12, "", 0)],
                        [(11, "unknown", 0), (12, "", 0)], [(7, "unfinished", 0)]):
            with self.assertRaises(evidence.pef.PEFError): motion_evidence.scalar_offsets(records)

    @unittest.skipUnless(os.environ.get("OPENTPW_MAC_APP"), "set OPENTPW_MAC_APP for coaster motion witness")
    def test_original_motion_schema_and_boarding(self):
        result = motion_evidence.inspect(Path(os.environ["OPENTPW_MAC_APP"]))
        settings = {entry["name"]: entry["definition_field"] for entry in result["settings"]["mapping"]}
        self.assertEqual(settings["fWinchSpeed"], 68)
        self.assertEqual(settings["fMinSpeed"], 72)
        self.assertEqual(settings["fUphillAccelModifier"], 40)
        self.assertEqual(result["motion"]["path_stride"], 56)
        self.assertEqual(result["tour_unload"]["raw_command"], 14)
        self.assertEqual(result["sound_output"]["record"], "train, not car")
        self.assertGreaterEqual(result["checked_instruction_count"], 150)

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

    @unittest.skipUnless(os.environ.get("OPENTPW_MAC_APP"), "set OPENTPW_MAC_APP for animation scalar witness")
    def test_original_animation_scalar_paths(self):
        result = animation_evidence.inspect(Path(os.environ["OPENTPW_MAC_APP"]))
        self.assertEqual(set(result["signed_trigger_clamps"]), {"16", "19", "21", "23"})
        self.assertFalse(result["waitanim_supported"])
        self.assertIn("unsigned", result["waitanim_reason"])
        self.assertGreaterEqual(result["checked_instruction_count"], 30)


if __name__ == "__main__":
    unittest.main()

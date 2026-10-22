import json
import pathlib
import sys
import tempfile
import unittest

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parent))

import coop_join_timings  # noqa: E402


def write_run(root, cut, decode, completed=True):
    root.mkdir(parents=True)
    environment = {"Version": "1.0+abc", "Configuration": "Release", "Runtime": ".NET 11", "Os": "macOS", "Architecture": "Arm64", "Processors": 8,
                   "Silent": True, "Advisor": True}
    capture = {"Generation": 1, "CutMilliseconds": cut, "DigestMilliseconds": cut / 2, "SnapshotMilliseconds": 1, "Save": {"Milliseconds": 9, "LongestMilliseconds": 4},
               "Compression": {"Milliseconds": 1.5, "LongestMilliseconds": 1.5}, "LongestFrameMilliseconds": cut, "Frames": 6, "PayloadBytes": 2048, "SaveBytes": 10240}
    join = {"Generation": 1, "Outcome": "joined", "DecodeMilliseconds": decode, "CheckMilliseconds": 5, "SwapMilliseconds": 2, "BuildFrames": 11,
            "LongestBuildFrameMilliseconds": 150, "CatchUpTicks": 8, "Build": [{"Name": "level", "Milliseconds": 480, "LongestMilliseconds": 157}],
            "LevelSections": {"objects": 156}}
    frames = [{"Frame": 1, "Step": "Joining", "Milliseconds": 20}, {"Frame": 2, "Step": "Joining", "Milliseconds": 160}, {"Frame": 3, "Step": "Following", "Milliseconds": 600}]
    (root / "host-evidence.json").write_text(json.dumps({"Completed": completed, "CaptureTimings": [capture], "Environment": environment}), encoding="utf-8")
    (root / "join-evidence.json").write_text(json.dumps({"Completed": True, "JoinTimings": [join], "Frames": frames, "Environment": environment}), encoding="utf-8")


class CoopJoinTimingsTests(unittest.TestCase):
    def test_every_value_has_its_median_range_and_runs(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = pathlib.Path(temporary)
            for index, (cut, decode) in enumerate([(30.0, 3.0), (40.0, 6.0), (35.0, 4.0)]):
                write_run(root / f"run-{index}", cut, decode)
            table = coop_join_timings.table([coop_join_timings.load(root / f"run-{index}") for index in range(3)])
        self.assertIn("3 runs; Release build 1.0+abc", table)
        self.assertNotIn("differ", table)
        self.assertIn("| capture 1: cut | 35.0 | 30.0-40.0 | 30.0, 40.0, 35.0 |", table)
        self.assertIn("| joined: decode | 4.0 | 3.0-6.0 | 3.0, 6.0, 4.0 |", table)
        self.assertIn("| capture 1: payload KiB | 2.0 | 2.0-2.0 | 2.0, 2.0, 2.0 |", table)
        self.assertIn("| joined: level, longest step | 157 | 157-157 | 157, 157, 157 |", table)
        self.assertIn("| joined: level section objects | 156 |", table)
        self.assertIn("| joined: longest update frame while building | 160 |", table)
        self.assertIn("| joined: first update frame after the swap | 600 |", table)
        # a value no run has (the resync, the second capture) is left out
        self.assertNotIn("resynced: decode", table)
        self.assertNotIn("capture 2: cut", table)

    def test_a_run_whose_phase_did_not_complete_is_refused(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = pathlib.Path(temporary) / "run"
            write_run(root, 30.0, 3.0, completed=False)
            with self.assertRaises(ValueError):
                coop_join_timings.load(root)


if __name__ == "__main__":
    unittest.main()

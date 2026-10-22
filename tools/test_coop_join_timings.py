import json
import pathlib
import sys
import tempfile
import unittest

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parent))

import coop_join_timings  # noqa: E402


def write_run(root, cut, decode, completed=True, mode="measure", captures=(1, 2), outcomes=("joined", "resynced"), long_step=True):
    root.mkdir(parents=True)
    environment = {"Version": "1.0+abc", "Configuration": "Release", "Runtime": ".NET 11", "Os": "macOS", "Architecture": "Arm64", "Processors": 8,
                   "Silent": True, "Advisor": True}
    capture = {"Generation": 1, "CutMilliseconds": cut, "DigestMilliseconds": cut / 2, "SnapshotMilliseconds": 1,
               "Save": {"Milliseconds": 9, "LongestMilliseconds": 4, "Steps": 3}, "Compression": {"Milliseconds": 1.5, "LongestMilliseconds": 1.5, "Steps": 1},
               "LongestFrameMilliseconds": cut, "Frames": 6, "UploadSeconds": 0.0125, "PayloadBytes": 2048, "SaveBytes": 10240}
    stages = [{"Name": "level", "Status": "Measured", "Milliseconds": 480, "LongestMilliseconds": 157, "Steps": 40, "Frames": 12},
              {"Name": "manifest", "Status": "Measured", "Milliseconds": 30, "LongestMilliseconds": 6, "Steps": 90, "Frames": 9}]
    join = {"Generation": 1, "Outcome": "joined", "DownloadAttempts": 1, "DownloadSeconds": 0.0151, "LastAttemptSeconds": 0.0151, "PayloadBytes": 2048,
            "DecodeMilliseconds": decode, "CheckMilliseconds": 5, "SwapMilliseconds": 2, "BuildFrames": 11, "LongestBuildFrameMilliseconds": 150, "CatchUpTicks": 8,
            "CatchUpFrames": 2, "CatchUpSeconds": 0.07, "ProcessBuild": 1, "Build": stages, "Sections": {"objects": 156},
            "LongSteps": [{"Stage": "level", "Step": 23, "Milliseconds": 157, "Progress": 0.75}] if long_step else []}
    resync = dict(join, Outcome="resynced", Generation=2, DecodeMilliseconds=decode / 2, ProcessBuild=2, LongSteps=[],
                  Build=[stages[0], dict(stages[1], Status="Cached", Milliseconds=0.1, LongestMilliseconds=0.1, Steps=1, Frames=1)])
    joins = [{"joined": join, "resynced": resync}[outcome] for outcome in outcomes]
    frames = [{"Frame": 1, "Step": "Joining", "Milliseconds": 20}, {"Frame": 2, "Step": "Joining", "Milliseconds": 160}, {"Frame": 3, "Step": "Following", "Milliseconds": 600},
              {"Frame": 4, "Step": "Resyncing", "Milliseconds": 90}, {"Frame": 5, "Step": "Resynced", "Milliseconds": 70}]
    host = {"Completed": completed, "CaptureTimings": [dict(capture, Generation=generation) for generation in captures], "Environment": environment}
    (root / "host-evidence.json").write_text(json.dumps(host), encoding="utf-8")
    (root / "join-evidence.json").write_text(json.dumps({"Completed": True, "Mode": mode, "JoinTimings": joins, "Frames": frames, "Environment": environment}),
                                             encoding="utf-8")


class CoopJoinTimingsTests(unittest.TestCase):
    def test_every_value_has_its_median_range_and_runs(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = pathlib.Path(temporary)
            for index, (cut, decode) in enumerate([(30.0, 3.0), (40.0, 6.0), (35.0, 4.0)]):
                write_run(root / f"run-{index}", cut, decode, long_step=index > 0)
            table = coop_join_timings.table([coop_join_timings.load(root / f"run-{index}") for index in range(3)], "Apple M5\n16 GB")
        self.assertIn("3 runs; Release build 1.0+abc", table)
        self.assertNotIn("differ", table)
        self.assertIn("    Apple M5", table)
        self.assertIn("| capture 1: cut | 35.0 | 30.0-40.0 | 30.0, 40.0, 35.0 |", table)
        self.assertIn("| capture 2: cut | 35.0 |", table)
        self.assertIn("| capture 1: payload KiB | 2.0 | 2.0-2.0 | 2.0, 2.0, 2.0 |", table)
        self.assertIn("| capture 1: save, slices | 3 | 3-3 | 3, 3, 3 |", table)
        # seconds keep their milliseconds
        self.assertIn("| capture 1: upload s | 0.013 |", table)
        self.assertIn("| joined: download s | 0.015 |", table)
        self.assertIn("| joined: last attempt s | 0.015 |", table)
        self.assertIn("| joined: download attempts | 1 |", table)
        self.assertIn("| joined: payload KiB | 2.0 |", table)
        self.assertIn("| joined: decode | 4.0 | 3.0-6.0 | 3.0, 6.0, 4.0 |", table)
        self.assertIn("| resynced: decode | 2.0 | 1.5-3.0 | 1.5, 3.0, 2.0 |", table)
        self.assertIn("| joined: level, longest step | 157 | 157-157 | 157, 157, 157 |", table)
        self.assertIn("| joined: level, steps | 40 |", table)
        self.assertIn("| joined: level, frames | 12 |", table)
        # a stage that ran no work of its own says so
        self.assertIn("| joined: manifest | 30.0 |", table)
        self.assertIn("| resynced: manifest (cached) | 0.1 |", table)
        self.assertIn("| joined: section objects | 156 |", table)
        self.assertIn("| joined: catch-up frames | 2 |", table)
        # a part that took the budget in some runs only says how many
        self.assertIn("| joined: long step, level 23 (to 0.75) (2 of 3 runs) | 157 | 157-157 | -, 157, 157 |", table)
        self.assertNotIn("resynced: long step", table)
        self.assertIn("| joined: longest update frame while building | 160 |", table)
        self.assertIn("| joined: first update frame after the swap | 600 |", table)
        self.assertIn("| resynced: longest update frame while building | 90.0 |", table)
        self.assertIn("| resynced: first update frame after the swap | 70.0 |", table)

    def test_a_run_whose_phase_did_not_complete_is_refused(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = pathlib.Path(temporary) / "run"
            write_run(root, 30.0, 3.0, completed=False)
            with self.assertRaises(ValueError):
                coop_join_timings.load(root)

    def test_the_adversarial_join_phase_is_no_measurement(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = pathlib.Path(temporary) / "run"
            write_run(root, 30.0, 3.0, mode="join")
            with self.assertRaises(ValueError):
                coop_join_timings.load(root)

    def test_a_run_without_both_captures_or_both_builds_is_refused(self):
        for name, arguments in [("one capture", {"captures": (1,)}), ("no resync", {"outcomes": ("joined",)}),
                                ("builds out of order", {"outcomes": ("resynced", "joined")})]:
            with self.subTest(name), tempfile.TemporaryDirectory() as temporary:
                root = pathlib.Path(temporary) / "run"
                write_run(root, 30.0, 3.0, **arguments)
                with self.assertRaises(ValueError):
                    coop_join_timings.load(root)


if __name__ == "__main__":
    unittest.main()

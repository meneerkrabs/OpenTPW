import json
import pathlib
import sys
import tempfile
import unittest

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parent))

import coop_join_timings  # noqa: E402


def write_run(root, cut, decode, completed=True, mode="measure", captures=(1, 2), outcomes=("joined", "resynced"), long_step=True, version="1.0+abc",
              change=None, page=None):
    root.mkdir(parents=True)
    environment = {"Version": version, "Configuration": "Release", "Runtime": ".NET 11", "Os": "macOS", "Architecture": "Arm64", "Processors": 8,
                   "Silent": True, "Advisor": True}
    work = {"Steps": 1, "Frames": 1, "Milliseconds": 1.5, "LongestMilliseconds": 1.5}
    capture = {"Generation": 1, "Ticks": 721, "CutMilliseconds": cut, "PublishMilliseconds": 0.1, "DigestMilliseconds": cut / 2, "StateMilliseconds": 0.9,
               "SnapshotMilliseconds": 1, "SerializerSetupMilliseconds": 0, "Save": dict(work, Milliseconds=9, LongestMilliseconds=4, Steps=3, Frames=3),
               "SaveCopyMilliseconds": 0.2, "WriterSetupMilliseconds": 1.2, "Compression": work, "FinishMilliseconds": 0.1, "LongestFrameMilliseconds": cut,
               "Frames": 6, "UploadSeconds": 0.0125, "PayloadBytes": 2048, "BodyBytes": 10300, "SaveBytes": 10240}
    stages = [dict(work, Name=name, Status="Measured") for name in coop_join_timings.STAGES]
    # a level whose longest part took the budget lists it among the long steps
    level = {"Name": "level", "Status": "Measured", "Milliseconds": 480, "LongestMilliseconds": 157 if long_step else 11, "Steps": 40, "Frames": 12}
    stages[coop_join_timings.STAGES.index("level")] = level
    stages[coop_join_timings.STAGES.index("manifest")] = {"Name": "manifest", "Status": "Measured", "Milliseconds": 30, "LongestMilliseconds": 6, "Steps": 90,
                                                          "Frames": 9}
    join = {"Generation": 1, "Outcome": "joined", "DownloadAttempts": 1, "DownloadSeconds": 0.0151, "LastAttemptSeconds": 0.0151, "PayloadBytes": 2048,
            "DecodeMilliseconds": decode, "CheckMilliseconds": 5, "SwapMilliseconds": 2, "BuildFrames": 11, "LongestBuildFrameMilliseconds": 150, "CatchUpTicks": 8,
            "CatchUpFrames": 2, "CatchUpSeconds": 0.07, "ProcessBuild": 1, "Build": stages, "Sections": {"objects": 156},
            "LongSteps": [{"Stage": "level", "Step": 23, "Milliseconds": 157, "Progress": 0.75}] if long_step else []}
    manifest = coop_join_timings.STAGES.index("manifest")
    resynced = [dict(stage, LongestMilliseconds=11) if stage is level else stage for stage in stages]
    resync = dict(join, Outcome="resynced", Generation=2, DecodeMilliseconds=decode / 2, ProcessBuild=2, LongSteps=[],
                  Build=resynced[:manifest] + [dict(stages[manifest], Status="Cached", Milliseconds=0.1, LongestMilliseconds=0.1, Steps=1, Frames=1)]
                  + resynced[manifest + 1:])
    joins = [{"joined": join, "resynced": resync}[outcome] for outcome in outcomes]
    frames = [{"Frame": 1, "Step": "Joining", "Milliseconds": 20}, {"Frame": 2, "Step": "Joining", "Milliseconds": 160}, {"Frame": 3, "Step": "Following", "Milliseconds": 600},
              {"Frame": 4, "Step": "Resyncing", "Milliseconds": 90}, {"Frame": 5, "Step": "Resynced", "Milliseconds": 70}]
    host = {"Completed": completed, "CaptureTimings": [dict(capture, Generation=generation) for generation in captures], "Environment": environment}
    evidence = {"Completed": True, "Mode": mode, "JoinTimings": joins, "Frames": frames, "Environment": environment}
    if change:
        change(host, evidence)
    (root / "host-evidence.json").write_text(json.dumps(host), encoding="utf-8")
    (root / "join-evidence.json").write_text(json.dumps(evidence), encoding="utf-8")
    if page is not None:
        (root / "run.json").write_text(json.dumps(page), encoding="utf-8")


PAGE = {"mode": "measure", "label": "aot", "chrome": "Chrome/155", "renderer": "ANGLE (Apple M5)", "size": "1280x900", "muted": True, "frameCap": 0,
        "poolSprites": "settled", "state": "passed", "seconds": 56}


def browser(host, join):
    """The browser's run: the guest in a page compiled ahead of time, its frames with their whole call times and the final check's frame (a page records it)."""
    join["Environment"] = dict(join["Environment"], Os="Browser", Architecture="Wasm", Processors=1, Silent=False, Compilation="aot")
    join["Frames"].append({"Frame": 6, "Step": "Done", "Milliseconds": 80})
    for index, item in enumerate(join["Frames"]):
        item["CallMilliseconds"] = item["Milliseconds"] + 10 + index


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
        self.assertIn("| joined: level, longest step | 157 | 11.0-157 | 11.0, 157, 157 |", table)
        self.assertIn("| resynced: level, longest step | 11.0 | 11.0-11.0 | 11.0, 11.0, 11.0 |", table)
        self.assertIn("| joined: level, steps | 40 |", table)
        self.assertIn("| joined: level, frames | 12 |", table)
        self.assertIn("| joined: save | 1.5 |", table)
        # a stage that ran no work of its own says so
        self.assertIn("| joined: manifest | 30.0 |", table)
        self.assertIn("| resynced: manifest (cached) | 0.1 |", table)
        self.assertIn("| joined: section objects | 156 |", table)
        self.assertIn("| joined: catch-up frames | 2 |", table)
        # a part that took the budget in some runs only says how many
        self.assertIn("| joined: long step, level 23 (to 0.75) (2 of 3 runs) | 157 | 157-157 | -, 157, 157 |", table)
        self.assertNotIn("resynced: long step", table)
        self.assertIn("| joined: longest update frame while building | 160 |", table)
        # the swap's frame is the build step's last, right before the step after the swap
        self.assertIn("| joined: the swap's update frame | 160 |", table)
        self.assertIn("| joined: first update frame after the swap | 600 |", table)
        self.assertIn("| resynced: longest update frame while building | 90.0 |", table)
        self.assertIn("| resynced: the swap's update frame | 90.0 |", table)
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

    def test_a_run_with_a_partial_timing_is_refused(self):
        def drop(path):
            def change(host, join):
                item = {"host": host, "join": join}[path[0]]
                for key in path[1:-1]:
                    item = item[key]
                del item[path[-1]]
            return change
        for path in [("host", "CaptureTimings", 1, "DigestMilliseconds"), ("host", "CaptureTimings", 0, "Save", "Steps"),
                     ("join", "JoinTimings", 0, "CheckMilliseconds"), ("join", "JoinTimings", 1, "CatchUpSeconds"), ("join", "JoinTimings", 0, "Build", 3),
                     ("join", "JoinTimings", 1, "Build", 6, "LongestMilliseconds"), ("join", "JoinTimings", 0, "LongSteps"), ("join", "Frames", 4),
                     ("join", "Frames", 2, "Milliseconds"), ("join", "Environment")]:
            with self.subTest(path), tempfile.TemporaryDirectory() as temporary:
                root = pathlib.Path(temporary) / "run"
                write_run(root, 30.0, 3.0, change=drop(path))
                with self.assertRaises(ValueError):
                    coop_join_timings.load(root)

    def test_a_run_with_a_partial_nested_timing_is_refused(self):
        def section_null(host, join):
            join["JoinTimings"][0]["Sections"]["objects"] = None

        def long_step_missing(host, join):
            join["JoinTimings"][0]["LongSteps"] = []

        def status_missing(host, join):
            del join["JoinTimings"][1]["Build"][2]["Status"]

        def status_unknown(host, join):
            join["JoinTimings"][0]["Build"][2]["Status"] = "Partly"

        def long_step_under_the_budget(host, join):
            join["JoinTimings"][0]["LongSteps"].append({"Stage": "save", "Step": 1, "Milliseconds": 11.9, "Progress": None})

        def long_step_over_its_stage(host, join):
            join["JoinTimings"][0]["LongSteps"][0]["Milliseconds"] = 158

        def long_step_twice(host, join):
            join["JoinTimings"][0]["LongSteps"].append(dict(join["JoinTimings"][0]["LongSteps"][0], Milliseconds=150))

        def long_step_past_its_stage(host, join):
            join["JoinTimings"][0]["LongSteps"][0]["Step"] = 41

        def not_finite(host, join):
            join["JoinTimings"][0]["DecodeMilliseconds"] = float("nan")

        def stage_not_an_object(host, join):
            join["JoinTimings"][0]["Build"][0] = "save"

        for change in [section_null, long_step_missing, status_missing, status_unknown, long_step_under_the_budget, long_step_over_its_stage, long_step_twice,
                       long_step_past_its_stage, not_finite, stage_not_an_object]:
            with self.subTest(change.__name__), tempfile.TemporaryDirectory() as temporary:
                root = pathlib.Path(temporary) / "run"
                write_run(root, 30.0, 3.0, change=change)
                with self.assertRaises(ValueError):
                    coop_join_timings.load(root)

    def test_a_skipped_stage_and_a_long_step_below_its_stage_longest_are_whole(self):
        def skipped_advisor_and_shorter_step(host, join):
            for timing in join["JoinTimings"]:
                timing["Build"][-1] = {"Name": "advisor", "Status": "Skipped", "Steps": 0, "Frames": 0, "Milliseconds": 0, "LongestMilliseconds": 0}
            join["JoinTimings"][0]["LongSteps"].append({"Stage": "level", "Step": 22, "Milliseconds": 156, "Progress": 0.7})

        with tempfile.TemporaryDirectory() as temporary:
            root = pathlib.Path(temporary) / "run"
            write_run(root, 30.0, 3.0, change=skipped_advisor_and_shorter_step)
            table = coop_join_timings.table([coop_join_timings.load(root)])
        self.assertIn("| joined: advisor (skipped) | 0.0 |", table)
        self.assertIn("| joined: long step, level 22 (to 0.7) | 156 |", table)

    def test_a_browser_guest_of_one_desktop_host_phase(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = pathlib.Path(temporary)
            for index, decode in enumerate([3.0, 6.0, 4.0]):
                write_run(root / f"run-{index}", 30.0, decode, change=browser, page=dict(PAGE, seconds=50 + index))
            table = coop_join_timings.table([coop_join_timings.load(root / f"run-{index}") for index in range(3)])
        # each phase's environment, the host's once: the browser's runs replay one recording
        self.assertIn("3 runs; host phase (one host phase, reused by every run): Release build 1.0+abc, .NET 11, macOS Arm64, 8 processors", table)
        self.assertIn("join phase: Release build 1.0+abc, .NET 11, Browser Wasm, 1 processors, silent False, advisor True, compiled aot", table)
        self.assertIn("page: Chrome/155, ANGLE (Apple M5), 1280x900, muted True, frame cap 0, pool sprites settled", table)
        self.assertIn("| capture 1: cut | 30.0 | 30.0-30.0 | 30.0 |", table)
        self.assertIn("| joined: decode | 4.0 | 3.0-6.0 | 3.0, 6.0, 4.0 |", table)
        # the page's whole frame calls beside the updates, and the final check's frame
        self.assertIn("| joined: longest whole frame while building | 171 |", table)
        self.assertIn("| joined: the swap's whole frame | 171 |", table)
        self.assertIn("| joined: first whole frame after the swap | 612 |", table)
        self.assertIn("| resynced: longest whole frame while building | 103 |", table)
        self.assertIn("| resynced: the swap's whole frame | 103 |", table)
        self.assertIn("| final check: update frame | 80.0 |", table)
        self.assertIn("| final check: whole frame | 95.0 |", table)

    def test_a_native_run_has_no_whole_frames_and_a_host_phase_each(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = pathlib.Path(temporary)
            for index, cut in enumerate([30.0, 40.0]):
                write_run(root / f"run-{index}", cut, 3.0)
            table = coop_join_timings.table([coop_join_timings.load(root / f"run-{index}") for index in range(2)])
        self.assertIn("2 runs; Release build 1.0+abc", table)
        self.assertNotIn("host phase", table)
        self.assertNotIn("page:", table)
        self.assertNotIn("whole frame", table)
        # a native run's evidence is written within its last frame, which it does not hold
        self.assertNotIn("final check", table)
        self.assertIn("| capture 1: cut | 35.0 | 30.0-40.0 | 30.0, 40.0 |", table)

    def test_the_swaps_frame_is_only_a_frame_of_the_build_right_before_the_step_after_it(self):
        def gap(host, join):
            # a frame of another step between the build's last and the first after the swap: no frame is known to be the swap's
            join["Frames"].insert(2, {"Frame": 3, "Step": "FrontEnd", "Milliseconds": 5})
        with tempfile.TemporaryDirectory() as temporary:
            root = pathlib.Path(temporary)
            write_run(root / "run-0", 30.0, 3.0)
            write_run(root / "run-1", 30.0, 3.0, change=gap)
            table = coop_join_timings.table([coop_join_timings.load(root / f"run-{index}") for index in range(2)])
        self.assertIn("| joined: the swap's update frame (1 of 2 runs) | 160 | 160-160 | 160, - |", table)
        self.assertIn("| resynced: the swap's update frame | 90.0 |", table)

    def test_a_pages_run_that_did_not_pass_or_lacks_its_record_or_a_frames_time_is_refused(self):
        def gap(host, join):
            browser(host, join)
            del join["Frames"][1]["CallMilliseconds"]

        def untimed(host, join):
            browser(host, join)
            for item in join["Frames"]:
                del item["CallMilliseconds"]
        for name, page, change in [("failed", dict(PAGE, state="failed"), browser), ("adversarial", dict(PAGE, mode="join"), browser),
                                   ("no record", None, browser), ("a frame's time missing", PAGE, gap), ("no frame's time", PAGE, untimed)]:
            with self.subTest(name), tempfile.TemporaryDirectory() as temporary:
                root = pathlib.Path(temporary) / "run"
                write_run(root, 30.0, 3.0, change=change, page=page)
                with self.assertRaises(ValueError):
                    coop_join_timings.load(root)

    def test_pages_in_different_browsers_or_guests_in_different_builds_are_no_one_measurement(self):
        for name, second in [("another browser", {"page": dict(PAGE, chrome="Chrome/156")}),
                             ("another compilation", {"change": lambda host, join: (browser(host, join), join["Environment"].update(Compilation="interpreter"))}),
                             ("a page and a native run", {"change": None, "page": None})]:
            with self.subTest(name), tempfile.TemporaryDirectory() as temporary:
                root = pathlib.Path(temporary)
                write_run(root / "run-0", 30.0, 3.0, change=browser, page=PAGE)
                write_run(root / "run-1", 30.0, 3.0, **{"change": browser, "page": PAGE, **second})
                with self.assertRaises(ValueError):
                    coop_join_timings.table([coop_join_timings.load(root / f"run-{index}") for index in range(2)])

    def test_runs_of_different_builds_are_no_one_measurement(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = pathlib.Path(temporary)
            write_run(root / "run-0", 30.0, 3.0)
            write_run(root / "run-1", 30.0, 3.0, version="1.0+def")
            with self.assertRaises(ValueError):
                coop_join_timings.table([coop_join_timings.load(root / f"run-{index}") for index in range(2)])


if __name__ == "__main__":
    unittest.main()

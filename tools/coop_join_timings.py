#!/usr/bin/env python3
"""The co-op join's measurements (docs/COOP.md, "Measurements").

Reads the evidence of the join smoke's runs, one directory per run with the host phase's host-evidence.json and the measuring
join phase's join-evidence.json: the native ones (scripts/measure-coop-join.sh) or the browser's (tools/web-coop-join.mjs, a
desktop host with a guest in the page, whose run.json the directory holds too). It prints, for each timing it reports, the
median, the range and each run's value as a Markdown table, after the runs' environments and, with --machine, the machine's own
description. A value some runs lack names how many runs have it (a part of a build's step that took the frame's budget in some
runs only, say). Milliseconds unless the row says otherwise. A run must be complete: both phases completed, the host's two
captures and the guest's join of the first and resync to the second, each with every timing. Each phase must have run in one
recorded environment in every run (build, runtime, system, processors, settings, compilation; the evidence names no machine, so
the measure scripts record the one they ran on), and a page's runs in one browser setting (run.json); the host and the guest may
differ (a desktop host, a browser guest). One host phase reused by every run (the browser's runs replay one recording) is one
sample: its rows show it once. Evidence of the adversarial join phase is refused: its rewritten payloads and its refused and
abandoned builds warm the process first. The named work of the swaps' frames and of the frames after them (the smoke's marks, which
nest: a parent's time includes its children's, so they are never added up) is listed for every mark of at least 1 ms in some run;
that of the builds' long frames (30 ms or more), named by the stage and step that ended each, for every mark of at least 3 ms, with
the garbage collections each ran when the evidence keeps them, as it keeps those of each stage's steps.

Usage: python3 -I tools/coop_join_timings.py [--machine <file>] <evidence directory> [<evidence directory> ...]
"""
import json
import math
import pathlib
import statistics
import sys

STAGES = ["codec", "save", "files", "catalogue", "manifest", "validation", "economy", "level", "hud", "advisor", "prepare"]
# a build from before the codec's metadata was made in steps of its own (C2c-2b-3a) or the entities prepared their first frame (C2c-2b-3c) lists no such stage
OLDER_STAGES = [STAGES[:-1], STAGES[1:-1]]
OUTCOMES = [("joined", 1, "Following"), ("resynced", 2, "Resynced")]
CAPTURE_FIELDS = ["Ticks", "PublishMilliseconds", "DigestMilliseconds", "StateMilliseconds", "SnapshotMilliseconds", "SerializerSetupMilliseconds",
                  "SaveCopyMilliseconds", "WriterSetupMilliseconds", "FinishMilliseconds", "SaveBytes", "BodyBytes", "PayloadBytes", "Frames",
                  "LongestFrameMilliseconds", "UploadSeconds", "CutMilliseconds"]
JOIN_FIELDS = ["DownloadAttempts", "DownloadSeconds", "LastAttemptSeconds", "PayloadBytes", "DecodeMilliseconds", "BuildFrames",
               "LongestBuildFrameMilliseconds", "ProcessBuild", "CheckMilliseconds", "SwapMilliseconds", "CatchUpTicks", "CatchUpFrames", "CatchUpSeconds"]
WORK_FIELDS = ["Steps", "Frames", "Milliseconds", "LongestMilliseconds"]
STATUSES = {"Skipped", "Measured", "Cached"}
# the build's budget a frame (GameFlow.ParkBuildBudgetMilliseconds): a part of a step that alone takes it is a long step
BUDGET_MILLISECONDS = 12
# the frames whose named work the measuring smoke records, and the smallest mark the summary lists
MARK_ROLES = [("swap", "the swap's frame"), ("after", "the frame after the swap")]
MARK_MILLISECONDS = 1
# the build frames the measuring smoke records with their named work (CoopJoinSmokeTest.LongBuildFrameMilliseconds), and the smallest of their marks the summary lists
LONG_FRAME_MILLISECONDS = 30
LONG_FRAME_MARK_MILLISECONDS = 3


def is_number(value):
    return isinstance(value, (int, float)) and not isinstance(value, bool) and math.isfinite(value)


def number_fields(item, fields, where):
    """Every field a number (a whole timing, not a partial one)."""
    missing = [field for field in fields if not isinstance(item, dict) or not is_number(item.get(field))]
    if missing:
        raise ValueError(f"{where} lacks {', '.join(missing)}")


def require_whole(root, host, join):
    for capture in host["CaptureTimings"]:
        where = f"{root}: capture {capture.get('Generation')}"
        number_fields(capture, CAPTURE_FIELDS, where)
        for part in ("Save", "Compression"):
            number_fields(capture.get(part) or {}, WORK_FIELDS, f"{where}'s {part.lower()}")
    for timing in join["JoinTimings"]:
        where = f"{root}: build {timing.get('Generation')}"
        number_fields(timing, JOIN_FIELDS, where)
        stages = timing.get("Build") or []
        names = [item.get("Name") if isinstance(item, dict) else None for item in stages]
        if names != STAGES and names not in OLDER_STAGES:
            raise ValueError(f"{where} lists the stages {names}, not {STAGES}")
        for item in stages:
            number_fields(item, WORK_FIELDS, f"{where}'s stage {item['Name']}")
            if item.get("Status") not in STATUSES:
                raise ValueError(f"{where}'s stage {item['Name']} has no status")
            # its garbage collections, when kept: counts, or null where nothing counted them
            for key in ("Collections", "FullCollections"):
                count = item.get(key)
                if count is not None and (not isinstance(count, int) or isinstance(count, bool) or count < 0):
                    raise ValueError(f"{where}'s stage {item['Name']} has a {key} that is no count")
        sections = timing.get("Sections")
        if not isinstance(sections, dict) or not sections or not all(is_number(value) for value in sections.values()):
            raise ValueError(f"{where} lacks its sections")
        long_steps = timing.get("LongSteps")
        by_name = {item["Name"]: item for item in stages}
        # a long step: of a stage, once, within the stage's parts, at least the budget and at most the stage's longest
        if not isinstance(long_steps, list) or not all(isinstance(item, dict) and item.get("Stage") in STAGES and is_number(item.get("Step"))
                                                       and 1 <= item["Step"] <= by_name[item["Stage"]]["Steps"] and is_number(item.get("Milliseconds"))
                                                       and BUDGET_MILLISECONDS <= item["Milliseconds"] <= by_name[item["Stage"]]["LongestMilliseconds"]
                                                       for item in long_steps) \
                or len({(item["Stage"], item["Step"]) for item in long_steps}) != len(long_steps):
            raise ValueError(f"{where} lacks its long steps, or lists one that is not whole")
        # each stage's longest part, when it took the budget alone, is among them
        for item in stages:
            if item["LongestMilliseconds"] >= BUDGET_MILLISECONDS and not any(step["Stage"] == item["Name"] and step["Milliseconds"] == item["LongestMilliseconds"]
                                                                                for step in long_steps):
                raise ValueError(f"{where}'s long steps lack the longest of its stage {item['Name']}")
    frames = join.get("Frames") or []
    if not all(isinstance(item, dict) and is_number(item.get("Frame")) and isinstance(item.get("Step"), str) and is_number(item.get("Milliseconds"))
               for item in frames):
        raise ValueError(f"{root}: the join phase's update frames are not whole")
    if not {"Joining", "Following", "Resyncing", "Resynced"} <= {item["Step"] for item in frames}:
        raise ValueError(f"{root}: the join phase's update frames lack a step of the join or the resync")
    # a page times every frame's whole call, a native run none: a frame without its time would drop out of the longest
    calls = [item.get("CallMilliseconds") for item in frames]
    page = (join.get("Environment") or {}).get("Os") == "Browser"
    if (page or any(value is not None for value in calls)) and not all(is_number(value) for value in calls):
        raise ValueError(f"{root}: some of the page's frames lack their whole call time")
    # the swaps' named work, when recorded: one swap's frame and one after it a build, the swap's the frame the step labels name
    marks = join.get("SwapFrames")
    if marks is not None:
        if not isinstance(marks, list) or not all(isinstance(item, dict) and isinstance(item.get("Marks"), dict) and is_number(item.get("Frame"))
                                                  and all(isinstance(value, list) and len(value) == 2 and all(is_number(part) for part in value)
                                                          for value in item["Marks"].values()) for item in marks):
            raise ValueError(f"{root}: the swaps' marks are not whole")
        for outcome, _, step in OUTCOMES:
            roles = sorted(item.get("Role") for item in marks if item.get("Outcome") == outcome)
            swapped = swap_frame(frames, "Joining" if outcome == "joined" else "Resyncing", step, "Frame")
            following = next((item["Frame"] for item in frames if item["Step"] == step), None)
            recorded = {item.get("Role"): item["Frame"] for item in marks if item.get("Outcome") == outcome}
            if roles != ["after", "swap"] or recorded["swap"] != swapped or recorded["after"] != following:
                raise ValueError(f"{root}: the {outcome} build's marks are not those of its swap's frame and the next ({roles}, frames {recorded} "
                                 f"for {swapped} and {following})")
    # the builds' long frames, when recorded: each a frame of its build's step, at least the length that records it, with its build's stage and that stage's steps so far
    long_frames = join.get("LongBuildFrames")
    if long_frames is not None:
        def whole(item):
            if not (isinstance(item, dict) and isinstance(item.get("Marks"), dict) and is_number(item.get("Frame")) and is_number(item.get("Milliseconds"))
                    and item.get("Outcome") in {outcome for outcome, _, _ in OUTCOMES} and isinstance(item.get("Stage"), str)
                    and isinstance(item.get("Steps"), int) and not isinstance(item.get("Steps"), bool)
                    and all(isinstance(value, list) and len(value) == 2 and all(is_number(part) for part in value) for value in item["Marks"].values())):
                return False
            timing = next((build for build in join["JoinTimings"] if build.get("Outcome") == item["Outcome"]), None)
            stage = next((part for part in (timing or {}).get("Build") or [] if part.get("Name") == item["Stage"]), None)
            step = "Joining" if item["Outcome"] == "joined" else "Resyncing"
            # the garbage collections the frame ran, by generation (absent in evidence from before they were kept)
            collections = item.get("Collections")
            if collections is not None and not (isinstance(collections, list) and collections
                                                and all(isinstance(count, int) and not isinstance(count, bool) and count >= 0 for count in collections)):
                return False
            return (stage is not None and 1 <= item["Steps"] <= stage["Steps"] and item["Milliseconds"] >= LONG_FRAME_MILLISECONDS
                    and any(frame["Frame"] == item["Frame"] and frame["Step"] == step for frame in frames))
        if not isinstance(long_frames, list) or not all(whole(item) for item in long_frames) \
                or len({(item["Outcome"], item["Frame"]) for item in long_frames}) != len(long_frames):
            raise ValueError(f"{root}: the builds' long frames are not whole, or not frames of their builds")


def load(directory):
    """One run: its host and join evidence (the join phase's in its measuring mode), refused when incomplete."""
    root = pathlib.Path(directory)
    host = json.loads((root / "host-evidence.json").read_text(encoding="utf-8"))
    join = json.loads((root / "join-evidence.json").read_text(encoding="utf-8"))
    if not host.get("Completed") or not join.get("Completed"):
        raise ValueError(f"{root}: a phase did not complete")
    if join.get("Mode") != "measure":
        raise ValueError(f"{root}: the join phase ran in its {join.get('Mode', 'join')!r} mode, not the measuring one")
    captures = [item.get("Generation") for item in host.get("CaptureTimings") or []]
    if captures != [1, 2]:
        raise ValueError(f"{root}: the host's captures are {captures}, not the generations 1 and 2")
    builds = [(item.get("Outcome"), item.get("Generation")) for item in join.get("JoinTimings") or []]
    if builds != [(outcome, generation) for outcome, generation, _ in OUTCOMES]:
        raise ValueError(f"{root}: the guest's builds are {builds}, not the join of the first capture and the resync to the second")
    require_whole(root, host, join)
    if not host.get("Environment") or not join.get("Environment"):
        raise ValueError(f"{root}: a phase's environment is missing")
    path = root / "run.json"
    page = json.loads(path.read_text(encoding="utf-8")) if path.exists() else None
    # a page's run is the driver's record of it: one that did not pass (its evidence may be an earlier run's) or that is missing is no measurement
    if page is None and join["Environment"].get("Os") == "Browser":
        raise ValueError(f"{root}: a page's run lacks the driver's record of it (run.json)")
    if page is not None and (page.get("state") != "passed" or page.get("mode") != "measure"):
        raise ValueError(f"{root}: the page's run did not pass in the measuring mode (run.json: {page.get('mode')!r}, {page.get('state')!r})")
    return host, join, page


def stage(timing, name):
    return next((item for item in timing.get("Build", []) if item["Name"] == name), None)


def first_frame(frames, step):
    return next((item["Milliseconds"] for item in frames if item["Step"] == step), None)


def longest_frame(frames, step, key="Milliseconds"):
    values = [item[key] for item in frames if item["Step"] == step and item.get(key) is not None]
    return max(values) if values else None


def first_call(frames, step):
    return next((item.get("CallMilliseconds") for item in frames if item["Step"] == step), None)


def swap_frame(frames, building, step, key="Milliseconds"):
    """The frame that checked the build and swapped it in: the last one of the build's step, right before the first of the step after the swap (the smoke takes that step
    in the frame after the swap's)."""
    index = next((position for position, item in enumerate(frames) if item["Step"] == step), None)
    return frames[index - 1].get(key) if index and frames[index - 1]["Step"] == building else None


def final_frame(frames, key="Milliseconds"):
    """The frame of the run's final check (the digest, the world hash, the join state and the save compared with the host's), its last once recorded: a page writes the
    evidence again after that frame, a native run only within it, before the frame's end is recorded."""
    return frames[-1].get(key) if frames and frames[-1]["Step"] == "Done" else None


def host_reused(runs):
    """Whether every run replays one host phase (the browser's runs): its timings are then one sample, not one a run."""
    return len(runs) > 1 and len({json.dumps(run[0], sort_keys=True) for run in runs}) == 1


def rows(runs):
    """(row name, unit, one value per run) for every measured value; a value a run lacks is None. A host phase every run reused gives one value."""
    def capture(host, index):
        return (host.get("CaptureTimings") or [])[index]

    def join(evidence, outcome):
        return next(item for item in evidence.get("JoinTimings") or [] if item["Outcome"] == outcome)

    def value(get, *path):
        def read(run):
            item = get(run)
            for key in path:
                if item is None:
                    return None
                item = item.get(key) if isinstance(item, dict) else None
            return item
        return read

    def kib(read):
        return lambda run: None if read(run) is None else read(run) / 1024

    result = []
    host_runs = runs[:1] if host_reused(runs) else runs
    for index in range(2):
        label = f"capture {index + 1}"
        get = lambda run, index=index: capture(run[0], index)
        for name, unit, path in [("cut", "ms", ("CutMilliseconds",)), ("publish", "ms", ("PublishMilliseconds",)),
                                 ("digest", "ms", ("DigestMilliseconds",)), ("state", "ms", ("StateMilliseconds",)),
                                 ("snapshot", "ms", ("SnapshotMilliseconds",)), ("serializer setup", "ms", ("SerializerSetupMilliseconds",)),
                                 ("save", "ms", ("Save", "Milliseconds")), ("save, longest slice", "ms", ("Save", "LongestMilliseconds")),
                                 ("save, slices", "count", ("Save", "Steps")), ("save copy", "ms", ("SaveCopyMilliseconds",)),
                                 ("writer setup", "ms", ("WriterSetupMilliseconds",)), ("compression", "ms", ("Compression", "Milliseconds")),
                                 ("compression, slices", "count", ("Compression", "Steps")), ("finish", "ms", ("FinishMilliseconds",)),
                                 ("longest frame", "ms", ("LongestFrameMilliseconds",)), ("frames", "count", ("Frames",)),
                                 ("upload s", "s", ("UploadSeconds",)), ("payload KiB", "KiB", ("PayloadBytes",)),
                                 ("body KiB", "KiB", ("BodyBytes",)), ("save KiB", "KiB", ("SaveBytes",))]:
            read = value(get, *path)
            result.append((f"{label}: {name}", unit, [(kib(read) if unit == "KiB" else read)(run) for run in host_runs]))
    for outcome, _, swap_step in OUTCOMES:
        get = lambda run, outcome=outcome: join(run[1], outcome)
        for name, unit, path in [("process's build", "count", ("ProcessBuild",)), ("download attempts", "count", ("DownloadAttempts",)),
                                 ("download s", "s", ("DownloadSeconds",)), ("last attempt s", "s", ("LastAttemptSeconds",)),
                                 ("payload KiB", "KiB", ("PayloadBytes",)), ("decode", "ms", ("DecodeMilliseconds",)),
                                 ("check", "ms", ("CheckMilliseconds",)), ("swap", "ms", ("SwapMilliseconds",)),
                                 ("build frames", "count", ("BuildFrames",)), ("longest build frame", "ms", ("LongestBuildFrameMilliseconds",)),
                                 ("catch-up ticks", "count", ("CatchUpTicks",)), ("catch-up frames", "count", ("CatchUpFrames",)),
                                 ("catch-up s", "s", ("CatchUpSeconds",))]:
            read = value(get, *path)
            result.append((f"{outcome}: {name}", unit, [(kib(read) if unit == "KiB" else read)(run) for run in runs]))
        for name in STAGES:
            timings = [stage(get(run), name) for run in runs]
            # a stage that did not run its own work in every run says so (a manifest kept from the earlier build is cached, an advisor that is off skipped)
            statuses = sorted({item["Status"] for item in timings if item is not None})
            label = f"{outcome}: {name}" + ("" if statuses == ["Measured"] else f" ({', '.join(status.lower() for status in statuses)})")
            for suffix, unit, key in [("", "ms", "Milliseconds"), (", longest step", "ms", "LongestMilliseconds"), (", steps", "count", "Steps"),
                                      (", frames", "count", "Frames")]:
                result.append((label + suffix, unit, [None if item is None else item[key] for item in timings]))
            # the garbage collections its steps ran, generation 0's count and the oldest generation's, when the evidence keeps them
            for suffix, key in [(", collections", "Collections"), (", full collections", "FullCollections")]:
                if any(item is not None and item.get(key) is not None for item in timings):
                    result.append((label + suffix, "count", [None if item is None else item.get(key) for item in timings]))
        # the parts of a step that took the budget alone, by stage and part (the level's with its construction progress after it)
        steps = sorted({(item["Stage"], item["Step"], item.get("Progress")) for run in runs for item in get(run).get("LongSteps") or []},
                       key=lambda key: (STAGES.index(key[0]) if key[0] in STAGES else len(STAGES), key[1], key[2] or 0))
        for name, number, progress in steps:
            label = f"{outcome}: long step, {name} {number}" + ("" if progress is None else f" (to {progress:.3g})")
            result.append((label, "ms", [next((item["Milliseconds"] for item in get(run).get("LongSteps") or []
                                              if (item["Stage"], item["Step"], item.get("Progress")) == (name, number, progress)), None) for run in runs]))
        sections = sorted({name for run in runs for name in get(run).get("Sections", {})})
        for section in sections:
            result.append((f"{outcome}: section {section}", "ms", [get(run).get("Sections", {}).get(section) for run in runs]))
        building = "Joining" if outcome == "joined" else "Resyncing"
        result.append((f"{outcome}: longest update frame while building", "ms", [longest_frame(run[1].get("Frames") or [], building) for run in runs]))
        # the check at the cut, the swap and whatever else that frame ran (the build's last step when it fitted the slice, the park's first update after the swap)
        result.append((f"{outcome}: the swap's update frame", "ms", [swap_frame(run[1].get("Frames") or [], building, swap_step) for run in runs]))
        result.append((f"{outcome}: first update frame after the swap", "ms", [first_frame(run[1].get("Frames") or [], swap_step) for run in runs]))
        # a page times each whole frame call too, the drawing with the update (its presentation not)
        result.append((f"{outcome}: longest whole frame while building", "ms", [longest_frame(run[1].get("Frames") or [], building, "CallMilliseconds") for run in runs]))
        result.append((f"{outcome}: the swap's whole frame", "ms", [swap_frame(run[1].get("Frames") or [], building, swap_step, "CallMilliseconds") for run in runs]))
        result.append((f"{outcome}: first whole frame after the swap", "ms", [first_call(run[1].get("Frames") or [], swap_step) for run in runs]))
    # the swaps' named work, every mark of at least 1 ms in some run (marks nest: never added up)
    for outcome, _, _ in OUTCOMES:
        for role, label in MARK_ROLES:
            def marks_of(run, outcome=outcome, role=role):
                return next((item["Marks"] for item in run[1].get("SwapFrames") or [] if item.get("Outcome") == outcome and item.get("Role") == role), {})
            names = sorted({name for run in runs for name, value in marks_of(run).items() if value[0] >= MARK_MILLISECONDS})
            for name in names:
                result.append((f"{outcome}: {label}, mark {name}", "ms", [(marks_of(run).get(name) or [None])[0] for run in runs]))
    # the builds' long frames' named work, by the stage and step that ended them (a long step ends its slice, so its frame is the step and the short ones before it), every mark of
    # at least 3 ms in some run (marks nest: never added up)
    for outcome, _, _ in OUTCOMES:
        def long_frames_of(run, outcome=outcome):
            return {(item["Stage"], item["Steps"]): item for item in run[1].get("LongBuildFrames") or [] if item.get("Outcome") == outcome}
        keys = sorted({key for run in runs for key in long_frames_of(run)}, key=lambda key: (STAGES.index(key[0]) if key[0] in STAGES else len(STAGES), key[1]))
        for name, number in keys:
            frames_of = [long_frames_of(run).get((name, number)) for run in runs]
            result.append((f"{outcome}: long frame, {name} {number}", "ms", [None if item is None else item["Milliseconds"] for item in frames_of]))
            # the garbage collections it ran: generation 0's count and the runtime's oldest generation's (a full collection)
            if any(item is not None and item.get("Collections") for item in frames_of):
                result.append((f"{outcome}: long frame, {name} {number}, collections", "count",
                               [None if item is None or not item.get("Collections") else item["Collections"][0] for item in frames_of]))
                result.append((f"{outcome}: long frame, {name} {number}, full collections", "count",
                               [None if item is None or not item.get("Collections") else item["Collections"][-1] for item in frames_of]))
            marks = sorted({mark for item in frames_of if item is not None for mark, value in item["Marks"].items() if value[0] >= LONG_FRAME_MARK_MILLISECONDS})
            for mark in marks:
                result.append((f"{outcome}: long frame, {name} {number}, mark {mark}", "ms",
                               [None if item is None else (item["Marks"].get(mark) or [None])[0] for item in frames_of]))
    result.append(("final check: update frame", "ms", [final_frame(run[1].get("Frames") or []) for run in runs]))
    result.append(("final check: whole frame", "ms", [final_frame(run[1].get("Frames") or [], "CallMilliseconds") for run in runs]))
    return result


def summarize(values):
    present = [item for item in values if item is not None]
    if not present:
        return None
    return statistics.median(present), min(present), max(present)


def number(value, unit):
    if value is None:
        return "-"
    if unit == "s":
        return f"{value:.3f}"
    if unit == "count" and float(value).is_integer():
        return f"{value:.0f}"
    return f"{value:.0f}" if abs(value) >= 100 else f"{value:.1f}"


def describe(environment):
    return (f"{environment.get('Configuration', '?')} build {environment.get('Version', '?')}, {environment.get('Runtime', '?')}, "
            f"{environment.get('Os', '?')} {environment.get('Architecture', '?')}, {environment.get('Processors', '?')} processors, "
            f"silent {environment.get('Silent', '?')}, advisor {environment.get('Advisor', '?')}"
            + (f", compiled {environment['Compilation']}" if environment.get("Compilation") else ""))


# what a page's run records of its browser (tools/web-coop-join.mjs), which every run of a measurement must share
PAGE_SETTING = ["mode", "label", "chrome", "renderer", "size", "muted", "frameCap", "poolSprites"]


def table(runs, machine=None):
    lines = []
    for phase, index in [("host", 0), ("join", 1)]:
        if len({json.dumps(run[index].get("Environment"), sort_keys=True) for run in runs}) != 1:
            raise ValueError(f"the runs' {phase} phases ran in different environments (another build, runtime, system or setting): their timings are not one measurement")
    pages = {json.dumps({key: (run[2] or {}).get(key) for key in PAGE_SETTING}, sort_keys=True) if run[2] else None for run in runs}
    if len(pages) != 1:
        raise ValueError("the runs' pages ran in different browsers or settings (run.json): their timings are not one measurement")
    host, join, page = runs[0][0]["Environment"], runs[0][1]["Environment"], runs[0][2]
    if host == join:
        lines.append(f"{len(runs)} runs; {describe(join)}")
    else:
        reused = " (one host phase, reused by every run)" if host_reused(runs) else ""
        lines.append(f"{len(runs)} runs; host phase{reused}: {describe(host)}; join phase: {describe(join)}")
    if page:
        lines.append(f"page: {page.get('chrome', '?')}, {page.get('renderer', '?')}, {page.get('size', '?')}, muted {page.get('muted', '?')}, "
                     f"frame cap {page.get('frameCap', '?')}, pool sprites {page.get('poolSprites', '?')}")
    if machine:
        lines.append("")
        lines.extend(f"    {line}" for line in machine.strip().splitlines())
    lines.append("")
    lines.append("| Value | Median | Range | Runs |")
    lines.append("|---|---|---|---|")
    for name, unit, values in rows(runs):
        summary = summarize(values)
        if summary is None:
            continue
        median, low, high = summary
        present = sum(item is not None for item in values)
        label = name if present == len(values) else f"{name} ({present} of {len(values)} runs)"
        lines.append(f"| {label} | {number(median, unit)} | {number(low, unit)}-{number(high, unit)} | {', '.join(number(item, unit) for item in values)} |")
    return "\n".join(lines)


def main(argv):
    arguments = argv[1:]
    machine = None
    if arguments[:1] == ["--machine"] and len(arguments) >= 2:
        machine = pathlib.Path(arguments[1]).read_text(encoding="utf-8")
        arguments = arguments[2:]
    if not arguments:
        print(__doc__.strip(), file=sys.stderr)
        return 2
    print(table([load(directory) for directory in arguments], machine))
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))

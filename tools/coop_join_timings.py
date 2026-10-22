#!/usr/bin/env python3
"""The co-op join's measurements (docs/COOP.md, "Measurements").

Reads the evidence of the native join smoke's runs (scripts/measure-coop-join.sh writes one directory per run, each with the
host phase's host-evidence.json and the measuring join phase's join-evidence.json) and prints, for each timing it reports, the
median, the range and each run's value as a Markdown table, after the runs' environment and, with --machine, the machine's own
description. A value some runs lack names how many runs have it (a part of a build's step that took the frame's budget in some
runs only, say). Milliseconds unless the row says otherwise. A run must be complete: both phases completed, the host's two
captures and the guest's join of the first and resync to the second, each with every timing, and all runs with one recorded
environment (build, runtime, system, processors and settings; the evidence names no machine, so the measure script records the
one it ran on). Evidence of the adversarial join phase is refused: its rewritten payloads and its refused and abandoned builds
warm the process first.

Usage: python3 -I tools/coop_join_timings.py [--machine <file>] <evidence directory> [<evidence directory> ...]
"""
import json
import math
import pathlib
import statistics
import sys

STAGES = ["save", "files", "catalogue", "manifest", "validation", "economy", "level", "hud", "advisor"]
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
        if names != STAGES:
            raise ValueError(f"{where} lists the stages {names}, not {STAGES}")
        for item in stages:
            number_fields(item, WORK_FIELDS, f"{where}'s stage {item['Name']}")
            if item.get("Status") not in STATUSES:
                raise ValueError(f"{where}'s stage {item['Name']} has no status")
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
    if not host.get("Environment") or host.get("Environment") != join.get("Environment"):
        raise ValueError(f"{root}: the phases' environments differ or are missing")
    return host, join


def stage(timing, name):
    return next((item for item in timing.get("Build", []) if item["Name"] == name), None)


def first_frame(frames, step):
    return next((item["Milliseconds"] for item in frames if item["Step"] == step), None)


def longest_frame(frames, step):
    values = [item["Milliseconds"] for item in frames if item["Step"] == step]
    return max(values) if values else None


def rows(runs):
    """(row name, unit, one value per run) for every measured value; a value a run lacks is None."""
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
            result.append((f"{label}: {name}", unit, [(kib(read) if unit == "KiB" else read)(run) for run in runs]))
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
        result.append((f"{outcome}: first update frame after the swap", "ms", [first_frame(run[1].get("Frames") or [], swap_step) for run in runs]))
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


def table(runs, machine=None):
    lines = []
    environments = {json.dumps(run[0].get("Environment"), sort_keys=True) for run in runs} | {json.dumps(run[1].get("Environment"), sort_keys=True) for run in runs}
    if len(environments) != 1:
        raise ValueError("the runs' environments differ (another build, runtime, system or setting): their timings are not one measurement")
    environment = runs[0][1].get("Environment") or {}
    lines.append(f"{len(runs)} runs; {environment.get('Configuration', '?')} build {environment.get('Version', '?')}, {environment.get('Runtime', '?')}, "
                 f"{environment.get('Os', '?')} {environment.get('Architecture', '?')}, {environment.get('Processors', '?')} processors, "
                 f"silent {environment.get('Silent', '?')}, advisor {environment.get('Advisor', '?')}")
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

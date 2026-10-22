#!/usr/bin/env python3
"""The co-op join's measurements (docs/COOP.md, "Measurements").

Reads the evidence of the native join smoke's runs (scripts/measure-coop-join.sh writes one directory per run, each with the
host phase's host-evidence.json and the measuring join phase's join-evidence.json) and prints, for each timing it reports, the
median, the range and each run's value as a Markdown table, after the runs' environment and, with --machine, the machine's own
description. A value some runs lack names how many runs have it. Milliseconds unless the row says otherwise. Evidence of the
adversarial join phase is refused: its rewritten payloads and its refused and abandoned builds warm the process first.

Usage: python3 -I tools/coop_join_timings.py [--machine <file>] <evidence directory> [<evidence directory> ...]
"""
import json
import pathlib
import statistics
import sys


def load(directory):
    """One run: its host and join evidence (the join phase's in its measuring mode)."""
    root = pathlib.Path(directory)
    host = json.loads((root / "host-evidence.json").read_text(encoding="utf-8"))
    join = json.loads((root / "join-evidence.json").read_text(encoding="utf-8"))
    if not host.get("Completed") or not join.get("Completed"):
        raise ValueError(f"{root}: a phase did not complete")
    if join.get("Mode") != "measure":
        raise ValueError(f"{root}: the join phase ran in its {join.get('Mode', 'join')!r} mode, not the measuring one")
    return host, join


def stage(timing, name):
    return next((item for item in timing.get("Build", []) if item["Name"] == name), None)


def first_frame(frames, step):
    return next((item["Milliseconds"] for item in frames if item["Step"] == step), None)


def longest_frame(frames, step):
    values = [item["Milliseconds"] for item in frames if item["Step"] == step]
    return max(values) if values else None


def rows(runs):
    """(row name, one value per run) for every measured value; a value a run lacks is None."""
    def capture(host, index):
        timings = host.get("CaptureTimings") or []
        return timings[index] if index < len(timings) else None

    def join(evidence, outcome):
        return next((item for item in evidence.get("JoinTimings") or [] if item["Outcome"] == outcome), None)

    def value(get, *path):
        def read(run):
            item = get(run)
            for key in path:
                if item is None:
                    return None
                item = item.get(key) if isinstance(item, dict) else None
            return item
        return read

    result = []
    for index in range(2):
        label = f"capture {index + 1}"
        get = lambda run, index=index: capture(run[0], index)
        for name, path in [("cut", ("CutMilliseconds",)), ("publish", ("PublishMilliseconds",)), ("digest", ("DigestMilliseconds",)),
                           ("state", ("StateMilliseconds",)), ("snapshot", ("SnapshotMilliseconds",)), ("serializer setup", ("SerializerSetupMilliseconds",)),
                           ("save", ("Save", "Milliseconds")), ("save, longest slice", ("Save", "LongestMilliseconds")), ("save, slices", ("Save", "Steps")),
                           ("save copy", ("SaveCopyMilliseconds",)), ("writer setup", ("WriterSetupMilliseconds",)),
                           ("compression", ("Compression", "Milliseconds")), ("compression, slices", ("Compression", "Steps")), ("finish", ("FinishMilliseconds",)),
                           ("longest frame", ("LongestFrameMilliseconds",)), ("frames", ("Frames",)), ("upload s", ("UploadSeconds",)),
                           ("payload KiB", ("PayloadBytes",)), ("body KiB", ("BodyBytes",)), ("save KiB", ("SaveBytes",))]:
            read = value(get, *path)
            if name.endswith("KiB"):
                read = lambda run, read=read: None if read(run) is None else read(run) / 1024
            result.append((f"{label}: {name}", [read(run) for run in runs]))
    for outcome, swap_step in [("joined", "Following"), ("resynced", "Resynced")]:
        get = lambda run, outcome=outcome: join(run[1], outcome)
        for name, path in [("process's build", ("ProcessBuild",)), ("download s", ("DownloadSeconds",)), ("decode", ("DecodeMilliseconds",)),
                           ("check", ("CheckMilliseconds",)), ("swap", ("SwapMilliseconds",)), ("build frames", ("BuildFrames",)),
                           ("longest build frame", ("LongestBuildFrameMilliseconds",)), ("catch-up ticks", ("CatchUpTicks",)),
                           ("catch-up frames", ("CatchUpFrames",)), ("catch-up s", ("CatchUpSeconds",))]:
            result.append((f"{outcome}: {name}", [value(get, *path)(run) for run in runs]))
        for name in ["save", "files", "catalogue", "manifest", "validation", "economy", "level", "hud", "advisor"]:
            result.append((f"{outcome}: {name}", [None if get(run) is None or stage(get(run), name) is None else stage(get(run), name)["Milliseconds"] for run in runs]))
            result.append((f"{outcome}: {name}, longest step", [None if get(run) is None or stage(get(run), name) is None else stage(get(run), name)["LongestMilliseconds"]
                                                                for run in runs]))
        sections = sorted({name for run in runs if get(run) is not None for name in get(run).get("Sections", {})})
        for section in sections:
            result.append((f"{outcome}: section {section}", [None if get(run) is None else get(run).get("Sections", {}).get(section) for run in runs]))
        building = "Joining" if outcome == "joined" else "Resyncing"
        result.append((f"{outcome}: longest update frame while building", [longest_frame(run[1].get("Frames") or [], building) for run in runs]))
        result.append((f"{outcome}: first update frame after the swap", [first_frame(run[1].get("Frames") or [], swap_step) for run in runs]))
    return result


def summarize(values):
    present = [item for item in values if item is not None]
    if not present:
        return None
    return statistics.median(present), min(present), max(present)


def number(value):
    return "-" if value is None else (f"{value:.0f}" if abs(value) >= 100 else f"{value:.1f}")


def table(runs, machine=None):
    lines = []
    environments = {json.dumps(run[0].get("Environment"), sort_keys=True) for run in runs} | {json.dumps(run[1].get("Environment"), sort_keys=True) for run in runs}
    environment = runs[0][1].get("Environment") or {}
    lines.append(f"{len(runs)} runs; {environment.get('Configuration', '?')} build {environment.get('Version', '?')}, {environment.get('Runtime', '?')}, "
                 f"{environment.get('Os', '?')} {environment.get('Architecture', '?')}, {environment.get('Processors', '?')} processors, "
                 f"silent {environment.get('Silent', '?')}, advisor {environment.get('Advisor', '?')}"
                 + ("" if len(environments) == 1 else " (the runs' environments differ)"))
    if machine:
        lines.append("")
        lines.extend(f"    {line}" for line in machine.strip().splitlines())
    lines.append("")
    lines.append("| Value | Median | Range | Runs |")
    lines.append("|---|---|---|---|")
    for name, values in rows(runs):
        summary = summarize(values)
        if summary is None:
            continue
        median, low, high = summary
        present = sum(item is not None for item in values)
        label = name if present == len(values) else f"{name} ({present} of {len(values)} runs)"
        lines.append(f"| {label} | {number(median)} | {number(low)}-{number(high)} | {', '.join(number(item) for item in values)} |")
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

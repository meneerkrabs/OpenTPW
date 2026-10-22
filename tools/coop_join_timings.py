#!/usr/bin/env python3
"""The co-op join's measurements (docs/COOP.md, "Measurements").

Reads the evidence of the native join smoke's runs (scripts/measure-coop-join.sh writes one directory per run, each with the
host phase's host-evidence.json and the measuring join phase's join-evidence.json) and prints, for every value, the median, the
range and each run's value as a Markdown table, after the runs' environment. Milliseconds unless the row says otherwise.

Usage: python3 -I tools/coop_join_timings.py <evidence directory> [<evidence directory> ...]
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
        for name, path in [("cut", ("CutMilliseconds",)), ("digest", ("DigestMilliseconds",)), ("snapshot", ("SnapshotMilliseconds",)),
                           ("save", ("Save", "Milliseconds")), ("save, longest slice", ("Save", "LongestMilliseconds")),
                           ("compression", ("Compression", "Milliseconds")), ("longest frame", ("LongestFrameMilliseconds",)),
                           ("frames", ("Frames",)), ("payload KiB", ("PayloadBytes",)), ("save KiB", ("SaveBytes",))]:
            read = value(get, *path)
            if name.endswith("KiB"):
                read = lambda run, read=read: None if read(run) is None else read(run) / 1024
            result.append((f"{label}: {name}", [read(run) for run in runs]))
    for outcome, swap_step in [("joined", "Following"), ("resynced", "Resynced")]:
        get = lambda run, outcome=outcome: join(run[1], outcome)
        for name, path in [("decode", ("DecodeMilliseconds",)), ("check", ("CheckMilliseconds",)), ("swap", ("SwapMilliseconds",)),
                           ("build frames", ("BuildFrames",)), ("longest build frame", ("LongestBuildFrameMilliseconds",)),
                           ("catch-up ticks", ("CatchUpTicks",))]:
            result.append((f"{outcome}: {name}", [value(get, *path)(run) for run in runs]))
        for name in ["save", "catalogue", "manifest", "validation", "economy", "level", "hud", "advisor"]:
            result.append((f"{outcome}: {name}", [None if get(run) is None or stage(get(run), name) is None else stage(get(run), name)["Milliseconds"] for run in runs]))
            result.append((f"{outcome}: {name}, longest step", [None if get(run) is None or stage(get(run), name) is None else stage(get(run), name)["LongestMilliseconds"]
                                                                for run in runs]))
        for section in ["objects", "guests", "terrain", "resume", "staff-gate-economy", "original-park", "level-hud", "join-state"]:
            result.append((f"{outcome}: level section {section}", [None if get(run) is None else get(run).get("LevelSections", {}).get(section) for run in runs]))
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


def table(runs):
    lines = []
    environments = {json.dumps(run[0].get("Environment"), sort_keys=True) for run in runs} | {json.dumps(run[1].get("Environment"), sort_keys=True) for run in runs}
    environment = runs[0][1].get("Environment") or {}
    lines.append(f"{len(runs)} runs; {environment.get('Configuration', '?')} build {environment.get('Version', '?')}, {environment.get('Runtime', '?')}, "
                 f"{environment.get('Os', '?')} {environment.get('Architecture', '?')}, {environment.get('Processors', '?')} processors, "
                 f"silent {environment.get('Silent', '?')}, advisor {environment.get('Advisor', '?')}"
                 + ("" if len(environments) == 1 else " (the runs' environments differ)"))
    lines.append("")
    lines.append("| Value | Median | Range | Runs |")
    lines.append("|---|---|---|---|")
    for name, values in rows(runs):
        summary = summarize(values)
        if summary is None:
            continue
        median, low, high = summary
        lines.append(f"| {name} | {number(median)} | {number(low)}-{number(high)} | {', '.join(number(item) for item in values)} |")
    return "\n".join(lines)


def main(argv):
    if len(argv) < 2:
        print(__doc__.strip(), file=sys.stderr)
        return 2
    print(table([load(directory) for directory in argv[1:]]))
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))

"""Area map: what each part of the Mac game binary is for, and how much of it OpenTPW has researched or built.

Usage: python3 -I tools/ppc-analysis/areas.py --mac-bin FERAL_BIN [--repo CHECKOUT] [--write | --check]

The main container's game code has no function names (only RTTI classes and import glue are named), so
every function gets an area from the strongest evidence available, in this order:

1. override: a function pinned in areas.json;
2. repository: the files that cite it (an address in a source file, a reverse-engineering note or an
   analysis lane), mapped to areas by areas.json; several citing areas resolve by weighted majority, and
   research notes and lanes (which describe what a function is) outweigh source files (which may only call it);
3. class: the RTTI class whose virtual table holds it;
4. library: import glue, by the shared library it calls;
5. strings: the debug strings it references, without generic ones (areas.json 'stop_strings');
6. index: citations from cross-cutting indexes (areas.json 'weak_files', such as the UI handler map);
7. imports: the libraries of the glue it calls, when they are not platform libraries;
8. propagated: the majority area of its callers (callers to callees only, a few rounds);
9. unknown.

Large ranges that hold only branch-shaped words and no data or string references (string pools the
analyser reads as code) are left out and listed separately.

Status per function: built (cited from source/), researched (cited only from docs/ or tools/) or uncited.
docs/reverse/AREAS.md reports, per area, the code size in each status and the open approximations of the
fidelity register, and ranks the gaps by the areas.json weight. Like COVERAGE.md it holds only addresses,
sizes, names and short strings the code references (generic strings are not shown). --check regenerates in memory and fails when the
committed file differs.
"""

from __future__ import annotations

import argparse
import collections
import json
import os
import re
import sys
from dataclasses import dataclass, field

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
sys.path.insert(1, os.path.dirname(HERE))

import coverage  # noqa: E402

OUTPUT = coverage.GENERATED[1]
CONFIG = os.path.join(HERE, "areas.json")
LEVELS = ("override", "repository", "class", "library", "strings", "index", "imports", "propagated", "unknown")
# A range this large whose only references are branches is data that happens to decode as code.
DATA_RANGE_BYTES = 8192
CODE_REFERENCES = frozenset({"data_r", "data_w", "data_addr", "string", "float", "const", "import", "vcall", "code"})
UNKNOWN = "(unknown)"
TOP = 15
PROPAGATION_ROUNDS = 3


@dataclass
class Facts:
    """What is known about one function, independent of the binary reader (so tests can build it)."""

    address: int
    size: int
    cited_files: list[str] = field(default_factory=list)
    classes: list[str] = field(default_factory=list)
    library: str | None = None
    strings: list[str] = field(default_factory=list)
    called_libraries: list[str] = field(default_factory=list)
    callers: list[int] = field(default_factory=list)


# Citations from these files count twice: a research note or lane describes what a function is.
RESEARCH = re.compile(r"^(docs/reverse/|tools/ppc-analysis/lanes/)")


def compile_rules(rules: list[list[str]]) -> list[tuple[re.Pattern, str]]:
    return [(re.compile(pattern, re.IGNORECASE), area) for pattern, area in rules]


def first_match(rules: list[tuple[re.Pattern, str]], text: str) -> str | None:
    for pattern, area in rules:
        if pattern.search(text):
            return area
    return None


def majority(areas: list[str]) -> str | None:
    """The most frequent area; ties go to the alphabetically first, so the result is deterministic."""
    if not areas:
        return None
    counts = collections.Counter(areas)
    best = max(counts.values())
    return min(area for area, count in counts.items() if count == best)


def status(cited_files: list[str]) -> str:
    if any(path.startswith("source/") for path in cited_files):
        return "built"
    return "researched" if cited_files else "uncited"


class Labeller:
    def __init__(self, config: dict):
        self.overrides = {int(address, 16): area for address, area in config["functions"].items()}
        self.files = compile_rules(config["files"])
        self.weak_files = compile_rules(config["weak_files"])
        self.classes = compile_rules(config["classes"])
        self.libraries = compile_rules(config["libraries"])
        self.strings = compile_rules(config["strings"])
        self.stop = re.compile(config["stop_strings"], re.IGNORECASE)

    def platform(self, library: str) -> bool:
        return first_match(self.libraries, library) == "platform"

    def direct(self, facts: Facts) -> tuple[str | None, str, bool]:
        """(area, level, conflict) from the function's own evidence; conflict marks citing files that disagree."""
        if facts.address in self.overrides:
            return self.overrides[facts.address], "override", False
        cited = [area for path in facts.cited_files if not first_match(self.weak_files, path)
                 for area in [first_match(self.files, path)] * (2 if RESEARCH.match(path) else 1) if area]
        if cited:
            return majority(cited), "repository", len(set(cited)) > 1
        classes = [area for area in (first_match(self.classes, name) for name in facts.classes) if area]
        if classes:
            return majority(classes), "class", False
        if facts.library is not None:
            return first_match(self.libraries, facts.library), "library", False
        strings = [area for area in (first_match(self.strings, text) for text in facts.strings if not self.stop.search(text)) if area]
        if strings:
            return majority(strings), "strings", False
        indexed = [area for area in (first_match(self.weak_files, path) for path in facts.cited_files) if area]
        if indexed:
            return majority(indexed), "index", False
        libraries = [area for area in (first_match(self.libraries, name) for name in facts.called_libraries if not self.platform(name)) if area]
        if libraries:
            return majority(libraries), "imports", False
        return None, "unknown", False

    def label(self, functions: list[Facts]) -> tuple[list[tuple[str, str]], int]:
        """Area and level per function, and the number of functions whose citing files disagree."""
        labels: list[tuple[str | None, str]] = []
        conflicts = 0
        for facts in functions:
            area, level, conflict = self.direct(facts)
            labels.append((area, level))
            conflicts += conflict
        # Callers to callees only: a helper takes the area of the code that uses it, never the reverse. Every
        # round recomputes all propagated labels from the previous round's, so a helper follows the majority
        # of its callers once they are labelled; the round limit bounds how far a label travels.
        undecided = [index for index, (area, _) in enumerate(labels) if area is None]
        for _ in range(PROPAGATION_ROUNDS):
            updates = {}
            for index in undecided:
                area = majority([labels[caller][0] for caller in functions[index].callers if labels[caller][0] is not None])
                updates[index] = (area, "propagated") if area else (None, "unknown")
            if all(labels[index] == value for index, value in updates.items()):
                break
            for index, value in updates.items():
                labels[index] = value
        return [(area or UNKNOWN, level) for area, level in labels], conflicts


def open_approximations(register: str, prefixes: dict[str, str]) -> collections.Counter:
    """Open fidelity-register IDs per area (rows before the retired section, each ID once)."""
    import fidelity_register

    text = register.split(fidelity_register.RETIRED_HEADING)[0]
    ids = {m.group(1) for m in map(fidelity_register.DOC_ROW.match, text.splitlines()) if m}
    counts: collections.Counter = collections.Counter()
    for identifier in ids:
        area = prefixes.get(identifier.rsplit("-", 1)[0])
        counts[area or UNKNOWN] += 1
    return counts


def kilobytes(size: int) -> str:
    return f"{size / 1024:.0f}"


def is_data_range(size: int, reference_kinds: set[str]) -> bool:
    return size >= DATA_RANGE_BYTES and not (reference_kinds & CODE_REFERENCES)


def render(config: dict, functions: list[Facts], labels: list[tuple[str, str]], conflicts: int,
           approximations: collections.Counter, classes: dict[str, list[int]], libraries: list[tuple[str, str, int, int]],
           data_ranges: list[tuple[int, int]] = ()) -> str:
    weights: dict[str, int] = config["weights"]
    stop = re.compile(config["stop_strings"], re.IGNORECASE)
    areas = sorted(set(weights) | {area for area, _ in labels})
    per_area: dict[str, dict[str, int]] = {area: collections.Counter() for area in areas}
    for facts, (area, _) in zip(functions, labels):
        bucket = per_area[area]
        bucket["functions"] += 1
        bucket["bytes"] += facts.size
        bucket[status(facts.cited_files)] += facts.size

    def gap(area: str) -> float:
        bucket = per_area[area]
        return weights.get(area, 0) * (bucket["researched"] + bucket["uncited"]) / 1024

    order = sorted(areas, key=lambda area: (-gap(area), area))
    total = sum(f.size for f in functions)
    lines = [
        "# Areas of the Mac game binary",
        "",
        "Generated by `python3 -I tools/ppc-analysis/areas.py --mac-bin <Feral bin> --write` from the inputs in",
        "`tools/ppc-analysis/areas.json`; do not edit by hand. The game code of `SimThemePark` has no function",
        "names, so each function's area comes from the strongest evidence available (see the label sources",
        "below). Labels from strings, imports and callers are leads, not facts.",
        "",
        "Status of a function: **built** when an address inside it is cited from `source/`, **researched** when",
        "it is cited only from `docs/` or `tools/`, else **uncited**. A citation can be a note that something is",
        "not done, and small helpers are often covered through their callers. The gap score is the area's",
        "weight (how much a player notices it, `areas.json`) times its researched and uncited kilobytes.",
        "",
        "## Label sources",
        "",
        "| Source | Functions | KB |",
        "| --- | ---: | ---: |",
    ]
    by_level: dict[str, list[int]] = {level: [0, 0] for level in LEVELS}
    for facts, (_, level) in zip(functions, labels):
        by_level[level][0] += 1
        by_level[level][1] += facts.size
    for level in LEVELS:
        lines.append(f"| {level} | {by_level[level][0]} | {kilobytes(by_level[level][1])} |")
    lines += [
        "",
        f"{len(functions)} functions, {kilobytes(total)} KB. Functions cited from files of more than one area: {conflicts}.",
        *([""] + [f"Left out as data read as code: " + ", ".join(f"`0x{start:08X}` ({size} bytes)" for start, size in data_ranges) + "."]
          if data_ranges else []),
        "",
        "## Areas",
        "",
        "Kilobytes of code per status. Open approximations are fidelity-register IDs whose prefix belongs to the area.",
        "",
        "| Area | Weight | Functions | KB | Built | Researched | Uncited | Open approximations | Gap |",
        "| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |",
    ]
    for area in order:
        bucket = per_area[area]
        lines.append(
            f"| {area} | {weights.get(area, 0)} | {bucket['functions']} | {kilobytes(bucket['bytes'])} | "
            f"{kilobytes(bucket['built'])} | {kilobytes(bucket['researched'])} | {kilobytes(bucket['uncited'])} | "
            f"{approximations.get(area, 0)} | {gap(area):.0f} |")
    if approximations.get(UNKNOWN):
        lines += ["", f"Open approximations without an area (test gates and unmapped prefixes): {approximations[UNKNOWN]}."]
    lines += [
        "",
        "## Shared libraries",
        "",
        "Exports of the Feral shared libraries next to the game. Named in repository: exports whose function name",
        "(six characters or more, without its class) appears as a word in `source/`, `docs/` or `tools/`.",
        "",
        "| Library | Area | Exports | Named in repository |",
        "| --- | --- | ---: | ---: |",
    ]
    for name, area, exports, named in libraries:
        lines.append(f"| `{name}` | {area} | {exports} | {named} |")

    class_area: dict[str, str] = {}
    for name, indices in classes.items():
        class_area[name] = majority([labels[i][0] for i in indices]) or UNKNOWN
    for area in order:
        bucket = per_area[area]
        if not bucket["functions"]:
            continue
        lines += ["", f"## {area}", ""]
        names = sorted(name for name, owner in class_area.items() if owner == area)
        if names:
            lines += ["| Class | Virtual methods | Cited |", "| --- | ---: | ---: |"]
            for name in names:
                indices = classes[name]
                cited = sum(1 for i in indices if functions[i].cited_files)
                lines.append(f"| `{name}` | {len(indices)} | {cited} |")
            lines.append("")
        for wanted, title in (("uncited", "Largest uncited functions"), ("researched", "Largest researched, not built")):
            rows = sorted(
                (i for i, (owner, _) in enumerate(labels) if owner == area and status(functions[i].cited_files) == wanted),
                key=lambda i: (-functions[i].size, functions[i].address))[:TOP]
            if not rows:
                continue
            lines += [f"{title}:", "", "| Address | Bytes | Label source | Strings |", "| --- | ---: | --- | --- |"]
            for i in rows:
                facts = functions[i]
                strings = " · ".join([text for text in facts.strings if not stop.search(text)][:3])
                lines.append(f"| `0x{facts.address:08X}` | {facts.size} | {labels[i][1]} | {strings} |")
            lines.append("")
    while lines and lines[-1] == "":
        lines.pop()
    return "\n".join(lines) + "\n"


def library_summary(mac_bin: str, repo: str, labeller: Labeller) -> list[tuple[str, str, int, int]]:
    import demangle
    import pef

    directory = os.path.join(mac_bin, "libraries")
    rows = []
    if not os.path.isdir(directory):
        return rows
    for file in sorted(os.listdir(directory)):
        if not file.endswith(".data"):
            continue
        with open(os.path.join(directory, file), "rb") as handle:
            container = pef.PEFContainer(handle.read(), file)
        words_by_export = []
        for export in container.exports:
            # Only the function's own name: a class name in a document would otherwise mark all its methods.
            name = re.sub(r"<.*", "", re.sub(r"\(.*", "", demangle.demangle(export.name)).split("::")[-1]).strip()
            words_by_export.append({name} if len(name) >= 6 else set())
        found = set()
        candidates = sorted({word for words in words_by_export for word in words if re.fullmatch(r"\w+", word)})
        for start in range(0, len(candidates), 400):
            found |= coverage.repository_words(repo, candidates[start:start + 400])
        named = sum(1 for words in words_by_export if words & found)
        library = file[: -len(".data")].replace("_", " ").replace("c c++", "c/c++")
        rows.append((library, first_match(labeller.libraries, library) or UNKNOWN, len(container.exports), named))
    return rows


def build(mac_bin: str, repo: str) -> str:
    import analyze

    with open(CONFIG, encoding="utf-8") as handle:
        config = json.load(handle)
    labeller = Labeller(config)
    a = analyze.load(os.path.join(mac_bin, "SimThemePark.data"))
    everything = sorted(a.functions.values(), key=lambda f: f.start)
    raw = [f for f in everything if not is_data_range(f.size, {r.kind for r in f.refs})]
    data_ranges = [(coverage.MAC_BASE + f.start, f.size) for f in everything if is_data_range(f.size, {r.kind for r in f.refs})]
    starts = [coverage.MAC_BASE + f.start for f in raw]
    ends = [coverage.MAC_BASE + f.end for f in raw]
    index_of = {f.start: i for i, f in enumerate(raw)}

    functions = [Facts(coverage.MAC_BASE + f.start, f.size) for f in raw]
    for path, citations in sorted(coverage.citations_by_file(repo).items()):
        for i in sorted(coverage.resolve(citations, starts, ends)):
            functions[i].cited_files.append(path)

    classes: dict[str, list[int]] = {}
    for i, f in enumerate(raw):
        for part in (f.name or "").split("|"):
            m = coverage.VIRTUAL.match(part)
            if m:
                functions[i].classes.append(m.group(1))
                classes.setdefault(m.group(1), []).append(i)
        if f.start in a.glue:
            functions[i].library = a.c.imports[a.glue[f.start]].library
    for i, f in enumerate(raw):
        for r in f.refs:
            if r.kind == "string":
                text = r.extra if isinstance(r.extra, str) else None
                if text is None and isinstance(r.target, int):
                    text = a.cstring_code(r.target) or a.cstring_data(r.target)
                text = coverage.clean(text) if text else None
                if text and text not in functions[i].strings:
                    functions[i].strings.append(text)
            elif r.kind in ("call", "tailcall") and isinstance(r.target, int):
                j = index_of.get(r.target)
                if j is None or j == i:
                    continue
                if functions[j].library is not None:
                    functions[i].called_libraries.append(functions[j].library)
                elif i not in functions[j].callers:
                    functions[j].callers.append(i)

    labels, conflicts = labeller.label(functions)
    register_path = os.path.join(repo, "docs", "FIDELITY-REGISTER.md")
    register = open(register_path, encoding="utf-8").read() if os.path.exists(register_path) else ""
    approximations = open_approximations(register, config["approximations"])
    libraries = library_summary(mac_bin, repo, labeller)
    return render(config, functions, labels, conflicts, approximations, classes, libraries, data_ranges)


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("--mac-bin", default=os.environ.get("OPENTPW_MAC_BIN"), help="Feral bin directory")
    parser.add_argument("--repo", default=os.path.abspath(os.path.join(HERE, "..", "..")))
    mode = parser.add_mutually_exclusive_group()
    mode.add_argument("--write", action="store_true")
    mode.add_argument("--check", action="store_true")
    args = parser.parse_args()
    if not args.mac_bin or not os.path.isfile(os.path.join(args.mac_bin, "SimThemePark.data")):
        print("areas: --mac-bin must be the Feral bin directory holding SimThemePark.data", file=sys.stderr)
        return 2
    text = build(args.mac_bin, args.repo)
    path = os.path.join(args.repo, OUTPUT)
    if args.check:
        current = open(path, encoding="utf-8").read() if os.path.exists(path) else ""
        if current != text:
            print(f"{OUTPUT} is stale; run --write", file=sys.stderr)
            return 1
        print(f"{OUTPUT} is current")
        return 0
    if args.write:
        with open(path, "w", encoding="utf-8") as handle:
            handle.write(text)
        print(f"wrote {OUTPUT}")
        return 0
    sys.stdout.write(text)
    return 0


if __name__ == "__main__":
    sys.exit(main())

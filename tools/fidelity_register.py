"""Inventory explicit C# fidelity annotations and their existing declarations.

The register document (docs/FIDELITY-REGISTER.md) is a build artifact: --write generates it locally (and the
website publishes it), --check validates the sources it is generated from. Retired and reserved IDs are kept in
the committed tools/fidelity_retired.txt."""
from __future__ import annotations

import argparse
from collections import Counter
from dataclasses import dataclass, field
from pathlib import Path
import re

REGISTERS = {
    "ECON": "source/OpenTPW/Economy/EconomyApproximations.cs",
    "UI": "source/OpenTPW/UI/Original/UiApproximations.cs",
    "RIDES": "source/OpenTPW/World/Objects/RidesApproximations.cs",
    "MD2": "source/OpenTPW/World/Objects/RidesApproximations.cs",
    "COMPAT": "source/OpenTPW.Files/Public/ApproximationRegister.cs",
    "ADVISOR": "source/OpenTPW/World/Advisor.cs",
    "AUDIO": "source/OpenTPW/Audio/AudioApproximations.cs",
    "DET": "source/OpenTPW/World/DeterminismApproximations.cs",
    "GATE": "source/OpenTPW/Client/M3GateApproximations.cs",
    "GATEM4": "source/OpenTPW/Client/M4GateApproximations.cs",
    "QUEUE": "source/OpenTPW/World/Guests/QueueApproximations.cs",
    "PATH": "source/OpenTPW/World/PathApproximations.cs",
    "PTCL": "source/OpenTPW/World/Particles/ParticleApproximations.cs",
    "COAST": "source/OpenTPW/World/Coasters/CoasterApproximations.cs",
    "WALK": "source/OpenTPW/World/Guests/WalkApproximations.cs",
    "HEADS": "source/OpenTPW/World/Guests/HeadApproximations.cs",
    "SAVE": "source/OpenTPW/Economy/SaveApproximations.cs",
    "BLUEPRINT": "source/OpenTPW/World/Original/BlueprintApproximations.cs",
    "ARRIVAL": "source/OpenTPW/World/Guests/ArrivalApproximations.cs",
    "GUEST": "source/OpenTPW/World/Guests/GuestApproximations.cs",
    "VM": "source/OpenTPW/VM/VmApproximations.cs",
    "TRACK": "source/OpenTPW/World/Tracks/TrackApproximations.cs",
    "TOUR": "source/OpenTPW/World/Tours/TourApproximations.cs",
    "UMB": "source/OpenTPW/World/Umbrellas/UmbrellaApproximations.cs",
}
# Closed areas: no register, and their IDs stay reserved. The OpenTPW online extension's former APPROX:ONLINE IDs were
# closed as design choices with EXT:online-* labels (docs/ONLINE.md, "Online design choices"); online uncertainty takes an
# EXT label, not a new APPROX ID.
CLOSED_AREAS = {"ONLINE": "the online extension records its choices as EXT:online-* labels (docs/ONLINE.md)"}
ORIGINAL_SCOPE = "Original-fidelity area (scope unadjudicated)"

ID = re.compile(r"[A-Z][A-Z0-9]*(?:-[A-Z][A-Z0-9]*)*-\d{3}\Z")
EMPTY_REGISTER = re.compile(r"\s*//\s*register-empty:")
TEMPLATE = re.compile(r"[A-Z]+-NNN\Z")
MARKER = re.compile(r"\[(APPROX|EXT|DATA|BIN)\b")
# Executables a BIN label may cite (docs/COMPLETION-PLAN.md, "Provenance of game rules").
BINARIES = ("D3DIM-DLL", "DDRAW-DLL", "QMIXER-DLL", "STP-PPC", "TPI-EXE", "TPW-EXE", "USP10-DLL")
BIN_LABEL = re.compile(r"(%s):\S.*\Z" % "|".join(BINARIES))
COMMENT = re.compile(r"^\s*//+\s?(.*)$")
DECLARATION = re.compile(r'^\s*(?:new\s*)?\(\s*"([^"\n]+)"\s*,\s*"((?:[^"\\]|\\.)*)"')
DOCUMENT = "docs/FIDELITY-REGISTER.md"
RETIRED_LIST = "tools/fidelity_retired.txt"
RETIRED_LINE = re.compile(r"^([A-Z][A-Z0-9]*(?:-[A-Z][A-Z0-9]*)*-\d{3})\s+(\S.*?)\s*$")
RETIRED_HEADING = "## Retired and reserved IDs"
DOC_ROW = re.compile(r"^\|\s*([A-Z][A-Z0-9]*(?:-[A-Z][A-Z0-9]*)*-\d{3})\s*\|")
DOC_MENTION = re.compile(r"APPROX:([A-Z][A-Z0-9]*(?:-[A-Z][A-Z0-9]*)*-\d{3})\b")
# Docs prose that retires IDs without an `APPROX:` citation: an ID list right before "retired" ("TRACK-012 and TRACK-023
# retired", "UI-001, 038 and 040 are retired") or right after a "Retired ...:" lead ("Retired with a code change: TRACK-009
# (snap), 011 (tick)"). A list item is a full ID, or a bare number that takes the prefix of the full ID before it, with an
# optional `/NNN` tail and one parenthetical; items join with ", ", " and " or ", and ". Anything else between the list
# and "retired" ("collided with main's retired", "is not retired", "narrowed, ...") does not match, and prose_retired()
# skips negated, conditional and questioned claims (HEDGE, NEGATION).
_PROSE_ID = r"[A-Z][A-Z0-9]*(?:-[A-Z][A-Z0-9]*)*-\d{3}(?![\w-])"
_PROSE_TAIL = r"[`\]]{0,2}(?:/\d{3}(?![\w-]))*(?:\s\([^()\n]{0,200}\))?"
_PROSE_LIST = (rf"[`\[]{{0,2}}(?:APPROX:)?{_PROSE_ID}{_PROSE_TAIL}"
               rf"(?:(?:,\s+(?:and\s+)?|\s+and\s+)[`\[]{{0,2}}(?:APPROX:)?(?:{_PROSE_ID}|\d{{3}}(?![\w-])){_PROSE_TAIL})*")
PROSE_RETIRED = re.compile(rf"(?<![\w-])(?P<before>{_PROSE_LIST})\s+(?:(?:is|are|was|were)\s+)?retired\b"
                           rf"|(?<![\w-])[Rr]etired\b(?P<lead>[^:.;|\n]{{0,60}}):\s+(?P<after>{_PROSE_LIST})")
# Words that make a "retired" claim negated, conditional or a question: before the list or in a lead ("Not retired:",
# "Retired once tests pass:", "Is TRACK-009 retired"), in a parenthetical of the list ("TRACK-005 (not) retired").
NEGATION = re.compile(r"\b(?:not|never|no|nor)\b", re.IGNORECASE)
HEDGE = re.compile(r"\b(?:not|never|no|nor|neither|if|unless|when|whether|once|until|only|after|before|pending|planned"
                   r"|will|would|should|could|may|might|can|be|is|are|was|were)\b", re.IGNORECASE)
PROSE_PARENTHETICAL = re.compile(r"\([^()\n]*\)")
PROSE_ITEM = re.compile(rf"({_PROSE_ID})|(?<![\w-])(\d{{3}})(?!\w)")


@dataclass(frozen=True)
class Site:
    kind: str
    label: str
    path: str
    line: int
    context: str


@dataclass(frozen=True)
class Declaration:
    path: str
    line: int
    description: str
    scope: str = ORIGINAL_SCOPE
    evidence: str = "See source annotation/runtime register"


@dataclass
class Inventory:
    sites: list[Site]
    declarations: dict[str, Declaration]
    errors: list[str]
    register_count: int = len(REGISTERS)
    retired: dict[str, str] = field(default_factory=dict)
    # Retired or reserved IDs found in docs that tools/fidelity_retired.txt does not record yet (--write adds them).
    unrecorded: dict[str, str] = field(default_factory=dict)

    @property
    def unresolved(self) -> list[str]:
        return sorted({s.label for s in self.sites if s.kind == "APPROX"} | set(self.declarations))


def closing_bracket(text: str, start: int) -> int:
    depth = 0
    for index in range(start, len(text)):
        if text[index] == "[":
            depth += 1
        elif text[index] == "]":
            depth -= 1
            if depth == 0:
                return index
    return -1


def annotations(text: str, path: str) -> tuple[list[Site], list[str]]:
    """Scan whole-line // and /// comments, excluding documented ID templates."""
    sites, errors = [], []
    lines = text.splitlines()
    index = 0
    while index < len(lines):
        number = index + 1
        comment = COMMENT.match(lines[index])
        index += 1
        if not comment:
            continue
        context = comment.group(1).strip()
        # Existing XML documentation has DATA annotations across consecutive lines.
        while any(closing_bracket(context, m.start()) < 0 for m in MARKER.finditer(context)):
            next_comment = COMMENT.match(lines[index]) if index < len(lines) else None
            if not next_comment or MARKER.search(next_comment.group(1)):
                break
            context += " " + next_comment.group(1).strip()
            index += 1
        for marker in MARKER.finditer(context):
            kind = marker.group(1)
            end = closing_bracket(context, marker.start())
            if end < 0:
                errors.append(f"{path}:{number}: unterminated {kind} annotation")
                continue
            payload = context[marker.end():end]
            if not payload.startswith(":") or not payload[1:].strip():
                errors.append(f"{path}:{number}: missing {kind} label")
                continue
            label = payload[1:]
            if kind == "APPROX":
                if TEMPLATE.fullmatch(label):
                    continue
                if not ID.fullmatch(label):
                    errors.append(f"{path}:{number}: malformed APPROX ID {label!r}")
                    continue
            elif kind == "BIN" and "<" in label:
                continue  # documentation template such as [BIN:STP-PPC:<address>]
            elif kind == "BIN" and not BIN_LABEL.fullmatch(label):
                errors.append(f"{path}:{number}: BIN label {label!r} must cite {' or '.join(BINARIES)} and a function or address")
                continue
            elif "&lt;id&gt;" in label:
                continue
            sites.append(Site(kind, label, path, number, context))
    return sites, errors


def collect(root: Path, registers: dict[str, str] | None = None,
            closed_areas: dict[str, str] | None = None) -> Inventory:
    if closed_areas is None:
        closed_areas = CLOSED_AREAS if registers is None else {}
    registers = REGISTERS if registers is None else registers
    sites, errors, declarations = [], [], {}
    for path in sorted((root / "source").rglob("*.cs")):
        if any(part in {"obj", "bin"} for part in path.relative_to(root).parts):
            continue
        found, problems = annotations(path.read_text(encoding="utf-8"), path.relative_to(root).as_posix())
        sites.extend(found)
        errors.extend(problems)
    for prefix, relative in sorted(registers.items()):
        path = root / relative
        if not path.is_file():
            errors.append(f"missing declaration register: {relative}")
            continue
        count = 0
        register_lines = path.read_text(encoding="utf-8").splitlines()
        for number, line in enumerate(register_lines, 1):
            match = DECLARATION.match(line)
            if not match:
                continue
            label, description = match.groups()
            if not label.startswith(prefix + "-"):
                continue
            count += 1
            if not ID.fullmatch(label):
                errors.append(f"{relative}:{number}: malformed declaration ID {label!r}")
            elif label in declarations:
                errors.append(f"{relative}:{number}: duplicate declaration {label}")
            else:
                declarations[label] = Declaration(relative, number, description)
        # A register whose last entry was retired says so with a whole-line marker comment; entries are still parsed normally.
        if not count and not any(EMPTY_REGISTER.match(line) for line in register_lines):
            errors.append(f"no declarations found in {relative}")
    tagged = {s.label for s in sites if s.kind == "APPROX"}
    for label in sorted(tagged - declarations.keys()):
        area = label.rsplit("-", 1)[0]
        if area in closed_areas:
            errors.append(f"{label}: {area} is a closed area without APPROX IDs: {closed_areas[area]}")
        else:
            errors.append(f"{label}: annotation has no declaration in the configured registers")
    for label in sorted(declarations.keys() - tagged):
        errors.append(f"{label}: declaration has no source annotation")
    listed = read_retired(root, errors)
    retired = retired_ids(root, set(registers) | set(closed_areas), declarations, errors, listed)
    unrecorded = {label: where for label, where in retired.items() if label not in listed}
    return Inventory(sites, declarations, errors, len(registers), retired, unrecorded)


def read_retired(root: Path, errors: list[str]) -> dict[str, str]:
    """The committed list of retired and reserved IDs (`ID where` lines; # comments and blank lines are skipped).

    Order is not checked, so a union merge of two branches stays valid; --write rewrites the file sorted."""
    listed: dict[str, str] = {}
    path = root / RETIRED_LIST
    if not path.is_file():
        return listed
    for number, line in enumerate(path.read_text(encoding="utf-8").splitlines(), 1):
        if not line.strip() or line.lstrip().startswith("#"):
            continue
        match = RETIRED_LINE.match(line)
        if not match:
            errors.append(f"{RETIRED_LIST}:{number}: expected `ID where`")
        else:
            listed.setdefault(match.group(1), match.group(2))
    return listed


def write_retired(root: Path, retired: dict[str, str]) -> None:
    path = root / RETIRED_LIST
    lines = path.read_text(encoding="utf-8").splitlines() if path.is_file() else []
    header = []
    for line in lines:
        if line.strip() and not line.lstrip().startswith("#"):
            break
        header.append(line)
    if header and header[-1].strip():
        header.append("")
    body = [f"{label} {where}" for label, where in sorted(retired.items())]
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text("\n".join(header + body) + "\n", encoding="utf-8")


def prose_retired(line: str) -> list[str]:
    """IDs that one docs line retires in prose (PROSE_RETIRED), in order; parentheticals are skipped, so an ID cited
    inside one is not taken for a list item."""
    labels: list[str] = []
    if "retired" not in line.lower():
        return labels
    for match in PROSE_RETIRED.finditer(line):
        # The words right before the match (one before a list, three before a lead; not across punctuation, so "at once
        # (AUDIO-027 is retired)" stands) and the lead.
        words = 1 if match.group("before") else 3
        preceding = re.search(r"(?:\w+[ \t]+){0,%d}\Z" % words, line[max(0, match.start() - 40):match.start()]).group(0)
        if HEDGE.search(preceding + " " + (match.group("lead") or "")):
            continue  # "Not retired: ...", "Retired once tests pass: ...", "Is TRACK-009 retired"
        if match.group("before") and (line[match.end():].lstrip().startswith("?")
                                      or any(NEGATION.search(text) for text in PROSE_PARENTHETICAL.findall(match.group("before")))):
            continue  # "TRACK-009 retired?", "TRACK-005 (not) retired"
        prefix = None
        for item in PROSE_ITEM.finditer(PROSE_PARENTHETICAL.sub("", match.group("before") or match.group("after"))):
            if item.group(1):
                labels.append(item.group(1))
                prefix = item.group(1).rsplit("-", 1)[0]
            elif prefix:
                labels.append(f"{prefix}-{item.group(2)}")
    return labels


def retired_ids(root: Path, prefixes: set[str], declarations: dict[str, Declaration], errors: list[str],
                listed: dict[str, str] | None = None) -> dict[str, str]:
    """IDs that a Markdown table row under docs keys, or docs prose cites as `APPROX:ID` or retires in plain words
    ("TRACK-009 retired", PROSE_RETIRED), but no register declares (retired, or reserved by a plan; prose mentions
    since REVIEW-WALK-2 WV2-2).

    The committed list (tools/fidelity_retired.txt, formerly a section of the committed document) only grows: an ID
    listed there may not be declared again, so a new approximation cannot take a number the docs already use with
    another meaning (REVIEW-ORACLE-2 OV2-1)."""
    listed = dict(listed or {})
    for label in sorted(listed.keys() & declarations.keys()):
        errors.append(f"{label}: declared, but {RETIRED_LIST} lists it as retired or reserved ({listed[label]}); use a free ID")
    found: dict[str, str] = {}
    docs = root / "docs"
    for path in sorted(docs.rglob("*.md")) if docs.is_dir() else []:
        relative = path.relative_to(root).as_posix()
        if relative == DOCUMENT:
            continue  # the generated document, when one was written locally
        for line in path.read_text(encoding="utf-8").splitlines():
            match = DOC_ROW.match(line)
            labels = [match.group(1)] if match else []
            labels += [mention.group(1) for mention in DOC_MENTION.finditer(line)]
            labels += prose_retired(line)
            for label in labels:
                if label.rsplit("-", 1)[0] in prefixes:
                    found.setdefault(label, relative)
    retired = dict(listed)
    for label, where in found.items():
        if label not in declarations:
            retired.setdefault(label, where)
    return dict(sorted(retired.items()))


def escape(text: str) -> str:
    return text.replace("\\", "\\\\").replace("|", "\\|").replace("<", "&lt;").replace(">", "&gt;").replace("`", "&#96;")


NUMBER_WORDS = ("no", "one", "two", "three", "four", "five", "six", "seven", "eight", "nine", "ten", "eleven", "twelve",
                "thirteen", "fourteen", "fifteen", "sixteen", "seventeen", "eighteen", "nineteen", "twenty")


def in_words(count: int) -> str:
    """A count as the document writes it: in words up to twenty, else in digits."""
    return NUMBER_WORDS[count] if 0 <= count < len(NUMBER_WORDS) else str(count)


def location(path: str, line: int) -> str:
    return f"`{path}:{line}`"


def render(data: Inventory) -> str:
    counts = Counter(s.kind for s in data.sites)
    lines = ["# Fidelity register", "", "Generated by `tools/fidelity_register.py`; edit the source annotations or declarations, then regenerate. This document is a build artifact: it is not committed, and the website publishes the copy generated from `main`.", "",
             "The acceptance gate is **zero unresolved assumptions in required original behavior**. All APPROX IDs below remain unresolved while their labels exist. Tags describing developer-only behavior or presentation conventions also remain listed; this inventory does not judge their relevance or downgrade them.", "",
             "EXT labels identify deliberate extensions. DATA labels identify claimed original-data provenance. BIN labels cite the original executable logic a rule was traced to (`STP-PPC` = Mac PowerPC Sim Theme Park, `TPI-EXE` = Theme Park Inc Game.exe, `TPW-EXE` = Windows Sim Theme Park World v2.0 tp.exe, `QMIXER-DLL` = the game's QMixer.dll sound mixer, `USP10-DLL` = the game's usp10.dll (Uniscribe), `D3DIM-DLL` and `DDRAW-DLL` = the DirectX 6.1 d3dim.dll and ddraw.dll in the game's Redist folder; see docs/reverse/FINDINGS.md). None of these labels proves semantic correctness or cancels an APPROX label at the same site. Acceptance also requires evidence for required behavior, assets, inputs and outputs; a zero tag count alone cannot satisfy that gate.", "",
             "This is a tag inventory, not enforcement of all semantics. It does not detect unlabelled magic numbers, formulas, logic, omitted features or false provenance claims. It scans whole-line C# // and /// comments under source, excluding bin/obj; inline trailing comments, block comments and other languages are outside its scope. ID templates and runtime log string templates are not code-site annotations.", "",
             "```sh", "python3 tools/fidelity_register.py --write", "python3 tools/fidelity_register.py --check", "python3 -m unittest discover -s tools -p 'test_fidelity_register.py' -v", "```", "",
             f"Current inventory: **{len(data.unresolved)} unresolved unique APPROX IDs**, {counts['APPROX']} APPROX occurrences, {counts['EXT']} EXT occurrences, {counts['DATA']} DATA occurrences and {counts['BIN']} BIN occurrences.", "",
             "All of them belong to the original-fidelity areas (required-behavior scope remains unadjudicated). The allowed OpenTPW online extension keeps no APPROX IDs: its former ONLINE approximations are closed as OpenTPW design choices with EXT:online-* labels, whose known original evidence docs/ONLINE.md keeps for a later fidelity pass, and their IDs stay reserved below.", "",
             "CI checks annotation/declaration consistency only. It does not fail the build based on the unresolved count and does not establish the original-fidelity release gate.", "",
             f"Repeated source occurrences of one ID are allowed and listed separately. Duplicate register declarations, malformed/missing ID syntax, unregistered IDs and declarations without source annotations fail the check. Register declarations are read from the {in_words(data.register_count)} configured C# registers (current one-entry-per-line tuple/new syntax); these are bounded lexical readers, not general C# parsers. New registers must be explicitly configured. A new APPROX ID in a closed area (ONLINE) fails the check with a pointer to EXT labels. The check also requires every retired or reserved ID below to be recorded in `tools/fidelity_retired.txt`.", "",
             "| Area | Unique unresolved IDs | Source occurrences |", "| --- | ---: | ---: |"]
    grouped = Counter(label.rsplit("-", 1)[0] for label in data.unresolved)
    for area, count in sorted(grouped.items()):
        occurrences = sum(s.kind == "APPROX" and s.label.startswith(area + "-") for s in data.sites)
        lines.append(f"| {area} | {count} | {occurrences} |")
    if data.errors:
        lines += ["", "## Inventory errors", ""] + [f"- {escape(error)}" for error in data.errors]
    lines += ["", "## Approximation declarations", "", "| ID | Scope | Existing declaration | Evidence needed | Register location |", "| --- | --- | --- | --- | --- |"]
    for label in data.unresolved:
        declaration = data.declarations.get(label)
        if declaration:
            lines.append(f"| {label} | {escape(declaration.scope)} | {escape(declaration.description)} | {escape(declaration.evidence)} | {location(declaration.path, declaration.line)} |")
        else:
            lines.append(f"| {label} | Unknown | Missing declaration | — | — |")
    lines += ["", RETIRED_HEADING, "",
              "IDs that a table row in docs keys, or docs prose cites as `APPROX:ID`, but no register declares: retired "
              "approximations and IDs a plan reserves. "
              "Declaring one of them fails the check, so a new approximation cannot take a number the docs already use "
              "with another meaning. The list is kept in `tools/fidelity_retired.txt`; `--write` records new entries and "
              "keeps every listed ID. Remove an entry by hand only when its docs agree with the new declaration.", ""]
    lines += [f"- `{label}` ({where})" for label, where in data.retired.items()] or ["None."]
    for kind, title in [("APPROX", "Approximation sites"), ("EXT", "Extension sites"), ("DATA", "Data provenance sites"), ("BIN", "Binary evidence sites")]:
        lines += ["", f"## {title}", "", "| Label | Location | Source comment context |", "| --- | --- | --- |"]
        for site in sorted((s for s in data.sites if s.kind == kind), key=lambda s: (s.label, s.path, s.line)):
            lines.append(f"| {escape(site.label)} | {location(site.path, site.line)} | {escape(site.context)} |")
    return "\n".join(lines) + "\n"


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--root", type=Path, default=Path(__file__).resolve().parents[1])
    mode = parser.add_mutually_exclusive_group()
    mode.add_argument("--write", action="store_true",
                      help=f"generate {DOCUMENT} (not committed) and record new retired IDs in {RETIRED_LIST}")
    mode.add_argument("--check", action="store_true",
                      help=f"validate annotations, declarations and the retired-ID list ({RETIRED_LIST}); writes nothing")
    parser.add_argument("--output", type=Path, help=f"with --write: the document path (default: <root>/{DOCUMENT})")
    args = parser.parse_args()
    if args.output and not args.write:
        parser.error("--output needs --write")
    try:
        data = collect(args.root)
        if data.errors:
            parser.exit(1, "\n".join(data.errors) + "\n")
        if args.check:
            if data.unrecorded:
                listed = "\n".join(f"{label}: retired or reserved ({where}) but not recorded in {RETIRED_LIST}"
                                   for label, where in data.unrecorded.items())
                parser.exit(1, listed + "\nRun --write and commit " + RETIRED_LIST + ".\n")
            render(data)
            print(f"Fidelity register sources consistent: {len(data.unresolved)} unresolved unique APPROX IDs.")
        elif args.write:
            if data.unrecorded:
                write_retired(args.root, data.retired)
                print(f"Recorded {len(data.unrecorded)} retired or reserved ID(s) in {RETIRED_LIST}; commit it.")
            target = args.output or args.root / DOCUMENT
            target.parent.mkdir(parents=True, exist_ok=True)
            target.write_text(render(data), encoding="utf-8")
            print(f"Wrote {target}: {len(data.unresolved)} unresolved unique APPROX IDs.")
        else:
            print(render(data), end="")
    except OSError as error:
        parser.exit(1, f"fidelity register: {error}\n")


if __name__ == "__main__":
    main()

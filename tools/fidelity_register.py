"""Inventory explicit C# fidelity annotations and their existing declarations."""
from __future__ import annotations

import argparse
from collections import Counter
from dataclasses import dataclass
from pathlib import Path
import re

REGISTERS = {
    "ECON": "source/OpenTPW/Economy/EconomyApproximations.cs",
    "UI": "source/OpenTPW/UI/Original/UiApproximations.cs",
    "RIDES": "source/OpenTPW/World/Objects/RidesApproximations.cs",
    "COMPAT": "source/OpenTPW.Files/Public/ApproximationRegister.cs",
    "ADVISOR": "source/OpenTPW/World/Advisor.cs",
}
MARKDOWN_REGISTERS = {"ONLINE": "docs/ONLINE.md"}
EXTENSION_SCOPE = "OpenTPW online extension"
ORIGINAL_SCOPE = "Original-fidelity area (scope unadjudicated)"

ID = re.compile(r"[A-Z][A-Z0-9]*(?:-[A-Z][A-Z0-9]*)*-\d{3}\Z")
TEMPLATE = re.compile(r"[A-Z]+-NNN\Z")
MARKER = re.compile(r"\[(APPROX|EXT|DATA|BIN)\b")
# Executables a BIN label may cite (docs/COMPLETION-PLAN.md, "Provenance of game rules").
BINARIES = ("STP-PPC", "TPI-EXE")
BIN_LABEL = re.compile(r"(%s):\S.*\Z" % "|".join(BINARIES))
COMMENT = re.compile(r"^\s*//+\s?(.*)$")
DECLARATION = re.compile(r'^\s*(?:new\s*)?\(\s*"([^"\n]+)"\s*,\s*"((?:[^"\\]|\\.)*)"')


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


def markdown_declarations(text: str, path: str, prefix: str) -> tuple[list[tuple[str, Declaration]], list[str]]:
    """Read explicit three-column rows inside the Approximation register section."""
    entries, errors = [], []
    active = False
    for number, line in enumerate(text.splitlines(), 1):
        if line.startswith("## "):
            active = line.strip() == "## Approximation register"
            continue
        if not active or not line.lstrip().startswith("|"):
            continue
        cells = [cell.strip().replace(r"\|", "|") for cell in re.split(r"(?<!\\)\|", line.strip())[1:-1]]
        if not cells or cells[0] == "ID" or all(re.fullmatch(r"[: -]+", cell) for cell in cells):
            continue
        if len(cells) != 3 or not cells[0].startswith(prefix + "-") or not ID.fullmatch(cells[0]):
            errors.append(f"{path}:{number}: malformed Markdown declaration row")
        elif not cells[1] or not cells[2]:
            errors.append(f"{path}:{number}: Markdown declaration requires assumption and evidence")
        else:
            entries.append((cells[0], Declaration(path, number, cells[1], EXTENSION_SCOPE, cells[2])))
    if not entries:
        errors.append(f"no Markdown declarations found in {path}")
    return entries, errors


def collect(root: Path, registers: dict[str, str] | None = None,
            markdown_registers: dict[str, str] | None = None) -> Inventory:
    if markdown_registers is None:
        markdown_registers = MARKDOWN_REGISTERS if registers is None else {}
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
        for number, line in enumerate(path.read_text(encoding="utf-8").splitlines(), 1):
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
        if not count:
            errors.append(f"no declarations found in {relative}")
    for prefix, relative in sorted(markdown_registers.items()):
        path = root / relative
        if not path.is_file():
            errors.append(f"missing Markdown declaration register: {relative}")
            continue
        entries, problems = markdown_declarations(path.read_text(encoding="utf-8"), relative, prefix)
        errors.extend(problems)
        for label, declaration in entries:
            if label in declarations:
                errors.append(f"{relative}:{declaration.line}: duplicate declaration {label}")
            else:
                declarations[label] = declaration
    tagged = {s.label for s in sites if s.kind == "APPROX"}
    for label in sorted(tagged - declarations.keys()):
        errors.append(f"{label}: annotation has no declaration in the configured registers")
    for label in sorted(declarations.keys() - tagged):
        errors.append(f"{label}: declaration has no source annotation")
    return Inventory(sites, declarations, errors)


def escape(text: str) -> str:
    return text.replace("\\", "\\\\").replace("|", "\\|").replace("<", "&lt;").replace(">", "&gt;").replace("`", "&#96;")


def location(path: str, line: int) -> str:
    return f"`{path}:{line}`"


def render(data: Inventory) -> str:
    counts = Counter(s.kind for s in data.sites)
    extension_count = sum(d.scope == EXTENSION_SCOPE for d in data.declarations.values())
    lines = ["# Fidelity register", "", "Generated by `tools/fidelity_register.py`; edit the source annotations or declarations, then regenerate.", "",
             "The acceptance gate is **zero unresolved assumptions in required original behavior**. All APPROX IDs below remain unresolved while their labels exist. Tags describing developer-only behavior or presentation conventions also remain listed; this inventory does not judge their relevance or downgrade them.", "",
             "EXT labels identify deliberate extensions. DATA labels identify claimed original-data provenance. BIN labels cite the original executable logic a rule was traced to (`STP-PPC` = Mac PowerPC Sim Theme Park, `TPI-EXE` = Theme Park Inc Game.exe; see docs/reverse/FINDINGS.md). None of these labels proves semantic correctness or cancels an APPROX label at the same site. Acceptance also requires evidence for required behavior, assets, inputs and outputs; a zero tag count alone cannot satisfy that gate.", "",
             "This is a tag inventory, not enforcement of all semantics. It does not detect unlabelled magic numbers, formulas, logic, omitted features or false provenance claims. It scans whole-line C# // and /// comments under source, excluding bin/obj; inline trailing comments, block comments and other languages are outside its scope. ID templates and runtime log string templates are not code-site annotations.", "",
             "```sh", "python3 tools/fidelity_register.py --write", "python3 tools/fidelity_register.py --check", "python3 -m unittest discover -s tools -p 'test_fidelity_register.py' -v", "```", "",
             f"Current inventory: **{len(data.unresolved)} unresolved unique APPROX IDs**, {counts['APPROX']} APPROX occurrences, {counts['EXT']} EXT occurrences, {counts['DATA']} DATA occurrences and {counts['BIN']} BIN occurrences.", "",
             f"Of these, {len(data.unresolved) - extension_count} IDs belong to the existing original-fidelity areas (required-behavior scope remains unadjudicated), and {extension_count} ONLINE IDs record uncertainty inside the allowed OpenTPW online extension. The extension scope does not hide, resolve or subtract those APPROX IDs from the total.", "",
             "CI checks annotation/declaration consistency and document freshness only. It does not fail the build based on the unresolved count and does not establish the original-fidelity release gate.", "",
             "Repeated source occurrences of one ID are allowed and listed separately. Duplicate register declarations, malformed/missing ID syntax, unregistered IDs and declarations without source annotations fail the check. Register declarations are read from the five configured existing C# registers (current one-entry-per-line tuple/new syntax) and the explicit three-column Approximation register table in docs/ONLINE.md; these are bounded lexical readers, not general C# or Markdown parsers. New registers must be explicitly configured. The check also compares the complete regenerated document against this file.", "",
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
    for kind, title in [("APPROX", "Approximation sites"), ("EXT", "Extension sites"), ("DATA", "Data provenance sites"), ("BIN", "Binary evidence sites")]:
        lines += ["", f"## {title}", "", "| Label | Location | Source comment context |", "| --- | --- | --- |"]
        for site in sorted((s for s in data.sites if s.kind == kind), key=lambda s: (s.label, s.path, s.line)):
            lines.append(f"| {escape(site.label)} | {location(site.path, site.line)} | {escape(site.context)} |")
    return "\n".join(lines) + "\n"


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--root", type=Path, default=Path(__file__).resolve().parents[1])
    mode = parser.add_mutually_exclusive_group()
    mode.add_argument("--write", action="store_true", help="regenerate docs/FIDELITY-REGISTER.md")
    mode.add_argument("--check", action="store_true", help="validate annotations, declarations and generated document")
    args = parser.parse_args()
    try:
        data = collect(args.root)
        if data.errors:
            parser.exit(1, "\n".join(data.errors) + "\n")
        document = render(data)
        target = args.root / "docs/FIDELITY-REGISTER.md"
        if args.check:
            if not target.is_file() or target.read_text(encoding="utf-8") != document:
                parser.exit(1, "Fidelity register is missing or stale; run --write.\n")
            print(f"Fidelity register complete: {len(data.unresolved)} unresolved unique APPROX IDs.")
        elif args.write:
            target.write_text(document, encoding="utf-8")
            print(f"Wrote {target}: {len(data.unresolved)} unresolved unique APPROX IDs.")
        else:
            print(document, end="")
    except OSError as error:
        parser.exit(1, f"fidelity register: {error}\n")


if __name__ == "__main__":
    main()

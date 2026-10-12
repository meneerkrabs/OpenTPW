"""Coverage map: which functions and classes of the Mac game binary OpenTPW already cites.

Usage: python3 -I tools/ppc-analysis/coverage.py --mac-bin FERAL_BIN [--repo CHECKOUT] [--write | --check]

FERAL_BIN is the Feral `bin` directory (the one holding SimThemePark.data and libraries/). The
script analyses the main container with analyze.py (function starts, names from exports, traceback
tables and RTTI virtual tables, string references), collects every Mac address the repository
cites (`0x10xxxxxx`, and `STP-PPC:0x...` short forms as code offsets), and writes
docs/reverse/COVERAGE.md:

* totals: functions and code bytes inside a cited function;
* every RTTI class: its virtual methods, how many are cited, and whether the class name appears;
* uncited functions of 600 bytes or more, largest first, with the short debug strings they
  reference, which usually say what the function is for.

Only names, addresses, sizes and short strings the code references are written (the same kind of
citation the docs already make); nothing else from the binary enters the repository. --check
regenerates in memory and fails when the committed file differs.
"""

from __future__ import annotations

import argparse
import bisect
import os
import re
import subprocess
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)

MAC_BASE = 0x10000000
MIN_SIZE = 600
OUTPUT = os.path.join("docs", "reverse", "COVERAGE.md")
# Generated maps list uncited addresses and class names; reading them back would mark everything cited.
GENERATED = (OUTPUT, os.path.join("docs", "reverse", "AREAS.md"))
LONG_ADDRESS = re.compile(r"0x10[0-9a-fA-F]{6}\b")
SHORT_ADDRESS = re.compile(r"STP-PPC:?\s*0x([0-9a-fA-F]{4,6})\b")
VIRTUAL = re.compile(r"(.+)::vf\d+$")
# Research notes and analysis lanes also cite code-section offsets in short form (`0x84ea4`). Data and table
# offsets look the same, so a short value counts only when it is exactly a function's start (see resolve);
# values after ':', '+', '-' or '.' (`data:0x…`, `+0x…`) and past the 2 MiB code section are never read.
SHORT_OFFSET = re.compile(r"(?<![\w:+\-.−])0x([0-9a-fA-F]{5,6})\b")
RESEARCH_FILE = re.compile(r"^(docs/reverse/|tools/ppc-analysis/lanes/)")
CODE_LIMIT = 0x200000


def cited_addresses(text: str) -> set[int]:
    """Mac addresses cited in repository text: full 0x10xxxxxx values and STP-PPC code offsets."""
    found = {int(m.group(0), 16) for m in LONG_ADDRESS.finditer(text)}
    found |= {MAC_BASE + int(m.group(1), 16) for m in SHORT_ADDRESS.finditer(text)}
    return found


def short_offsets(text: str) -> set[int]:
    """Bare 5-6 digit code-section offsets in research text, as Mac addresses (candidates only; see resolve)."""
    return {MAC_BASE + value for value in (int(m.group(1), 16) for m in SHORT_OFFSET.finditer(text)) if value < CODE_LIMIT}


def containing(starts: list[int], ends: list[int], address: int) -> int | None:
    """Index of the function whose [start, end) holds address, or None."""
    i = bisect.bisect_right(starts, address) - 1
    return i if i >= 0 and starts[i] <= address < max(ends[i], starts[i] + 4) else None


def clean(text: str, limit: int = 60) -> str | None:
    """A debug string fit for a table cell: printable ASCII, no format-only noise, at most limit chars."""
    text = " ".join(text.split())
    if len(text) < 4 or sum(32 <= ord(ch) < 127 for ch in text) < len(text) or not re.search(r"[A-Za-z]{3}", text):
        return None
    text = text.replace("|", "/")
    return text if len(text) <= limit else text[: limit - 1] + "…"


def repository_text(repo: str) -> str:
    out = subprocess.run(["git", "-C", repo, "grep", "-h", "-I", "-e", "0x", "--", "source", "docs", "tools",
                          *(":!" + path for path in GENERATED)],
                         capture_output=True, text=True, check=True).stdout
    return out


def citations_by_file(repo: str) -> dict[str, tuple[set[int], set[int]]]:
    """Per tracked file (checkout-relative, '/' separated): the Mac addresses it cites, and the short code-offset
    candidates of research notes and lanes."""
    out = subprocess.run(["git", "-C", repo, "grep", "-I", "--full-name", "-e", "0x", "--", "source", "docs", "tools",
                          *(":!" + path for path in GENERATED)],
                         capture_output=True, text=True, check=True).stdout
    found: dict[str, tuple[set[int], set[int]]] = {}
    for line in out.splitlines():
        path, _, text = line.partition(":")
        full = cited_addresses(text)
        short = short_offsets(text) if RESEARCH_FILE.match(path) else set()
        if full or short:
            entry = found.setdefault(path, (set(), set()))
            entry[0].update(full)
            entry[1].update(short)
    return found


def resolve(citations: tuple[set[int], set[int]], starts: list[int], ends: list[int]) -> set[int]:
    """Indices of the functions a file cites: any address inside a function, or a short offset at its start."""
    full, short = citations
    hit = {i for i in (containing(starts, ends, address) for address in full) if i is not None}
    for address in short:
        i = containing(starts, ends, address)
        if i is not None and starts[i] == address:
            hit.add(i)
    return hit


def repository_words(repo: str, names: list[str]) -> set[str]:
    if not names:
        return set()
    out = subprocess.run(["git", "-C", repo, "grep", "-h", "-o", "-w", "-F", *sum((["-e", n] for n in names), []),
                          "--", "source", "docs", "tools", *(":!" + path for path in GENERATED)],
                         capture_output=True, text=True).stdout
    return set(out.split())


def build(mac_bin: str, repo: str) -> str:
    import analyze

    a = analyze.load(os.path.join(mac_bin, "SimThemePark.data"))
    functions = sorted(a.functions.values(), key=lambda f: f.start)
    starts = [MAC_BASE + f.start for f in functions]
    ends = [MAC_BASE + f.end for f in functions]
    hit: set[int] = set()
    for citations in citations_by_file(repo).values():
        hit |= resolve(citations, starts, ends)
    total_bytes = sum(f.size for f in functions)
    hit_bytes = sum(functions[i].size for i in hit)

    classes: dict[str, list[int]] = {}
    for i, f in enumerate(functions):
        for part in (f.name or "").split("|"):
            m = VIRTUAL.match(part)
            if m:
                classes.setdefault(m.group(1), []).append(i)
    rtti = sorted({v["class"] for v in a.vtables.values()} | set(classes))
    plain = sorted({re.sub(r"<.*", "", c).split("::")[-1] for c in rtti})
    mentioned = repository_words(repo, plain)

    lines = [
        "# Coverage of the Mac game binary",
        "",
        "Generated by `python3 -I tools/ppc-analysis/coverage.py --mac-bin <Feral bin> --write`; do not edit by hand.",
        "A function counts as cited when any Mac address inside it appears in `source/`, `docs/` or `tools/`",
        "(`0x10xxxxxx`, or `STP-PPC:0x...` as a code offset), or when `docs/reverse/` or an analysis lane cites its",
        "start as a bare 5–6 digit code offset. Cited is not the same as implemented: a citation",
        "can be a note that something is not done. Uncited is a lead, not proof of a gap: small helpers are",
        "often covered through their callers.",
        "",
        "## Totals",
        "",
        f"| | Count | Cited | Share |",
        f"| --- | ---: | ---: | ---: |",
        f"| Functions in SimThemePark | {len(functions)} | {len(hit)} | {100 * len(hit) / max(1, len(functions)):.1f} % |",
        f"| Code bytes in those functions | {total_bytes} | {hit_bytes} | {100 * hit_bytes / max(1, total_bytes):.1f} % |",
        f"| RTTI classes (names in the repository) | {len(plain)} | {sum(1 for c in plain if c in mentioned)} | |",
        "",
        "## Classes",
        "",
        "Virtual methods come from the classes' virtual tables; generic container and `std::` classes are left out.",
        "",
        "| Class | Virtual methods | Cited | Name in repository |",
        "| --- | ---: | ---: | --- |",
    ]
    for cls in rtti:
        if cls.startswith(("std::", "TbPtrList", "TbTList")):
            continue
        methods = sorted(set(classes.get(cls, [])))
        cited = sum(1 for i in methods if i in hit)
        short = re.sub(r"<.*", "", cls).split("::")[-1]
        lines.append(f"| `{cls}` | {len(methods)} | {cited} | {'yes' if short in mentioned else 'no'} |")
    lines += [
        "",
        f"## Uncited functions of {MIN_SIZE} bytes or more",
        "",
        "Largest first. Strings are the first short debug strings the function references.",
        "",
        "| Address | Bytes | Name | Strings |",
        "| --- | ---: | --- | --- |",
    ]
    rows = []
    for i, f in enumerate(functions):
        if i in hit or f.size < MIN_SIZE:
            continue
        strings: list[str] = []
        for r in f.refs:
            if r.kind != "string":
                continue
            raw = r.extra if isinstance(r.extra, str) else None
            if raw is None and isinstance(r.target, int):
                raw = a.cstring_code(r.target) or a.cstring_data(r.target)
            text = clean(raw) if raw else None
            if text and text not in strings:
                strings.append(text)
            if len(strings) == 3:
                break
        rows.append((f.size, MAC_BASE + f.start, f.name or "", strings))
    rows.sort(key=lambda row: (-row[0], row[1]))
    for size, address, name, strings in rows:
        label = f"`{name.split('|')[0]}`" if name else ""
        lines.append(f"| `0x{address:08X}` | {size} | {label} | {' · '.join(strings)} |")
    return "\n".join(lines) + "\n"


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("--mac-bin", default=os.environ.get("OPENTPW_MAC_BIN"), help="Feral bin directory")
    parser.add_argument("--repo", default=os.path.abspath(os.path.join(HERE, "..", "..")))
    mode = parser.add_mutually_exclusive_group()
    mode.add_argument("--write", action="store_true")
    mode.add_argument("--check", action="store_true")
    args = parser.parse_args()
    if not args.mac_bin or not os.path.isfile(os.path.join(args.mac_bin, "SimThemePark.data")):
        print("coverage: --mac-bin must be the Feral bin directory holding SimThemePark.data", file=sys.stderr)
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

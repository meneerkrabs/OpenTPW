"""Assemble the progress log from one fragment per pull request (Python standard library only).

Usage: python3 tools/progress.py [--root CHECKOUT] [--check | --output PATH | --migrate OLD_PROGRESS_MD]

Every pull request adds its entry as its own file, docs/progress/YYYY-MM-DD-<slug>.md, whose first line is the
entry heading `## Title (Month D, YYYY)` with the same date as the file name. Separate files never conflict, unlike
entries inserted at the top of one shared log. docs/PROGRESS.md keeps the entries written before this convention
as frozen history.

Without options the assembled log is printed: the preamble of docs/PROGRESS.md, the fragments newest first (same
date: by file name), then the history. --output writes it to a file instead; the website build uses the same
assembly for its "Progress notes" page. Relative links in a fragment are written as seen from docs/progress/ and
are rewritten to resolve from docs/. --check validates fragment names and headings and that docs/PROGRESS.md has
not grown new entries.

--migrate helps a branch that still added its entry to docs/PROGRESS.md: given that branch's copy of the file (for
example `git show HEAD:docs/PROGRESS.md > /tmp/old.md` before merging main), it writes every entry above the frozen
history into its own fragment. Links are written as found (from docs/); fix them to start from docs/progress/.
"""
from __future__ import annotations

import argparse
import datetime
import posixpath
import re
import sys
from pathlib import Path

FRAGMENTS = "docs/progress"
HISTORY = "docs/PROGRESS.md"
# The newest entry of the frozen history. New entries go into docs/progress/, so this stays the first heading.
FROZEN_TOP = "## Ride vehicles: engine voices, tour cars and the shared bump module (October 15, 2026)"
# The history's note about the convention; it is dropped from the assembled log.
NOTE = "> **New entries:**"
MONTHS = ("January", "February", "March", "April", "May", "June", "July", "August", "September", "October",
          "November", "December")
NAME = re.compile(r"(\d{4})-(\d{2})-(\d{2})-([a-z0-9]+(?:-[a-z0-9]+)*)\.md\Z")
HEADING = re.compile(r"## (\S.*\S|\S) \((%s) ([1-9]\d?), (\d{4})\)\Z" % "|".join(MONTHS))
FENCE = re.compile(r"^\s*(```|~~~)")
INLINE_LINK = re.compile(r"(\]\()([^)\s]+)")
REFERENCE = re.compile(r"^(\s*\[[^\]]+\]:\s*)(\S+)", re.M)


def fragment_date(name: str) -> datetime.date | None:
    match = NAME.fullmatch(name)
    if not match:
        return None
    try:
        return datetime.date(int(match.group(1)), int(match.group(2)), int(match.group(3)))
    except ValueError:
        return None


def heading_date(line: str) -> datetime.date | None:
    match = HEADING.fullmatch(line)
    if not match:
        return None
    try:
        return datetime.date(int(match.group(4)), MONTHS.index(match.group(2)) + 1, int(match.group(3)))
    except ValueError:
        return None


def headings(text: str) -> list[tuple[int, str]]:
    """Level-one and level-two ATX headings outside fenced code blocks, with their line numbers."""
    found, fenced = [], None
    for number, line in enumerate(text.splitlines(), 1):
        fence = FENCE.match(line)
        if fence:
            fenced = None if fenced == fence.group(1) else (fenced or fence.group(1))
            continue
        if not fenced and re.match(r"#{1,2} ", line):
            found.append((number, line))
    return found


def check_fragment(path: Path, relative: str) -> list[str]:
    errors = []
    date = fragment_date(path.name)
    if date is None:
        return [f"{relative}: expected a file named YYYY-MM-DD-<lowercase-slug>.md with a real date"]
    raw = path.read_bytes()
    try:
        text = raw.decode("utf-8")
    except UnicodeDecodeError:
        return [f"{relative}: not UTF-8"]
    if text.startswith("\ufeff") or "\r" in text:
        errors.append(f"{relative}: use UTF-8 without a byte-order mark and LF line endings")
    lines = text.splitlines()
    first = lines[0] if lines else ""
    when = heading_date(first)
    if when is None:
        errors.append(f"{relative}:1: the first line must be the entry heading `## Title (Month D, YYYY)`")
    elif when != date:
        errors.append(f"{relative}:1: heading date {when.isoformat()} differs from the file name's {date.isoformat()}")
    for number, line in headings(text)[1:] if when else []:
        errors.append(f"{relative}:{number}: one entry per file; use ### or deeper inside an entry ({line!r})")
    if when is not None and not "\n".join(lines[1:]).strip():
        errors.append(f"{relative}: the entry has no text below its heading")
    return errors


def fragments(root: Path) -> list[Path]:
    """Fragment files, newest first; same date by file name."""
    folder = root / FRAGMENTS
    files = [path for path in folder.iterdir() if path.is_file() and path.suffix == ".md"] if folder.is_dir() else []
    return sorted(sorted(files, key=lambda path: path.name), key=lambda path: path.name[:10], reverse=True)


def check(root: Path) -> list[str]:
    errors = []
    folder = root / FRAGMENTS
    if folder.is_dir():
        for path in sorted(folder.iterdir()):
            relative = path.relative_to(root).as_posix()
            if path.is_symlink() or not path.is_file():
                errors.append(f"{relative}: only fragment files belong in {FRAGMENTS}/")
            else:
                errors.extend(check_fragment(path, relative))
    history = root / HISTORY
    if not history.is_file():
        return errors + [f"missing {HISTORY}"]
    text = history.read_text(encoding="utf-8")
    preamble, _ = split_history(text)
    if not any(line.startswith(NOTE) for line in preamble.splitlines()):
        errors.append(f"{HISTORY}: the note starting with {NOTE!r} is missing above the first entry")
    entries = [line for _, line in headings(text) if line.startswith("## ")]
    if not entries or entries[0] != FROZEN_TOP:
        top = entries[0] if entries else "none"
        errors.append(f"{HISTORY}: the history is frozen and its first entry must stay {FROZEN_TOP!r} (found {top!r}); "
                      f"add a new entry as a file in {FRAGMENTS}/ instead")
    return errors


def split_history(text: str) -> tuple[str, str]:
    """The preamble above the first entry heading, and the entries."""
    match = re.search(r"^## ", text, re.M)
    return (text, "") if match is None else (text[:match.start()], text[match.start():])


def without_note(preamble: str) -> str:
    """Drop the convention note (a blockquote that starts with NOTE) from the preamble."""
    kept, skipping = [], False
    for line in preamble.splitlines():
        if line.startswith(NOTE):
            skipping = True
        elif skipping and not line.startswith(">"):
            skipping = False
        if not skipping:
            kept.append(line)
    return re.sub(r"\n{3,}", "\n\n", "\n".join(kept)).strip()


def relink(text: str) -> str:
    """Rewrite relative link targets written from docs/progress/ so they resolve from docs/."""
    def target(value: str) -> str:
        if value.startswith(("#", "/", "<")) or re.match(r"[A-Za-z][A-Za-z0-9+.-]*:", value):
            return value
        path, hash_, fragment = value.partition("#")
        return posixpath.normpath(posixpath.join("progress", path)) + hash_ + fragment if path else value

    lines, fenced = [], None
    for line in text.split("\n"):
        fence = FENCE.match(line)
        if fence:
            fenced = None if fenced == fence.group(1) else (fenced or fence.group(1))
        elif not fenced:
            line = INLINE_LINK.sub(lambda m: m.group(1) + target(m.group(2)), line)
            line = REFERENCE.sub(lambda m: m.group(1) + target(m.group(2)), line)
        lines.append(line)
    return "\n".join(lines)


def assemble(root: Path) -> str:
    history = root / HISTORY
    text = history.read_text(encoding="utf-8") if history.is_file() else ""
    preamble, entries = split_history(text)
    parts = [without_note(preamble)]
    parts += [relink(path.read_text(encoding="utf-8").strip()) for path in fragments(root)]
    parts.append(entries.strip())
    return "\n\n".join(part for part in parts if part) + "\n"


def slug(title: str) -> str:
    words = re.sub(r"[^a-z0-9]+", "-", title.lower()).strip("-")
    return words[:60].rstrip("-") or "entry"


def migrate(root: Path, old: str) -> list[Path]:
    """Write the entries above FROZEN_TOP in an old copy of docs/PROGRESS.md as fragments."""
    _, entries = split_history(old)
    frozen = re.search(r"^%s$" % re.escape(FROZEN_TOP), entries, re.M)
    if frozen is None:
        raise ValueError(f"the old log does not contain the frozen history's first entry {FROZEN_TOP!r}")
    new = entries[:frozen.start()]
    blocks = [block for block in re.split(r"^(?=## )", new, flags=re.M) if block.strip()]
    planned = []
    for block in blocks:
        heading = block.splitlines()[0]
        date = heading_date(heading)
        if date is None:
            raise ValueError(f"no `## Title (Month D, YYYY)` heading: {heading!r}")
        path = root / FRAGMENTS / f"{date.isoformat()}-{slug(HEADING.fullmatch(heading).group(1))}.md"
        if path.exists() or path in (target for target, _ in planned):
            raise ValueError(f"{path.relative_to(root).as_posix()} exists already")
        planned.append((path, block.strip() + "\n"))
    for path, text in planned:
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(text, encoding="utf-8")
    return [path for path, _ in planned]


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("--root", type=Path, default=Path(__file__).resolve().parents[1])
    mode = parser.add_mutually_exclusive_group()
    mode.add_argument("--check", action="store_true", help="validate the fragments and the frozen history; writes nothing")
    mode.add_argument("--output", type=Path, help="write the assembled log to this file instead of standard output")
    mode.add_argument("--migrate", type=Path, metavar="OLD_PROGRESS_MD",
                      help="write the entries above the frozen history in this old copy of docs/PROGRESS.md as fragments")
    args = parser.parse_args()
    if args.migrate:
        try:
            written = migrate(args.root, args.migrate.read_text(encoding="utf-8"))
        except (OSError, ValueError) as error:
            print(f"progress: {error}", file=sys.stderr)
            return 1
        for path in written:
            print(f"Wrote {path.relative_to(args.root).as_posix()}")
        print(f"{len(written)} entr{'y' if len(written) == 1 else 'ies'} moved; check their relative links and run --check.")
        return 0
    if args.check:
        errors = check(args.root)
        if errors:
            print("\n".join(errors), file=sys.stderr)
            return 1
        print(f"Progress log consistent: {len(fragments(args.root))} fragment(s) in {FRAGMENTS}/.")
        return 0
    log = assemble(args.root)
    if args.output:
        args.output.parent.mkdir(parents=True, exist_ok=True)
        args.output.write_text(log, encoding="utf-8")
        print(f"Wrote {args.output}.")
    else:
        sys.stdout.write(log)
    return 0


if __name__ == "__main__":
    sys.exit(main())

#!/usr/bin/env python3
"""Source-text contract: no loop and no LINQ call inside a `finally` body of the code the browser build compiles.

The Mono interpreter (the browser build without AOT) overwrote locals that a `try` had assigned, and that were read after the `finally`, with the temporaries of a
`for` or `foreach` loop in the `finally` body, once the method was hot (about 70 calls, the interpreter's tier-up): a returned `(ulong, ulong)` came back as
`0x111122220000000B, 0x555566660000000C`. The controls and the evidence are in source/OpenTPW.Tests/MonoInterpreterTryFinallyProbeTests.cs. The shape that holds is a `finally`
that calls a helper method, so a loop (or a LINQ call, which loops) goes into a helper.

The scan covers every project the browser build compiles (OpenTPW, OpenTPW.Common, OpenTPW.Files, OpenTPW.Online, OpenTPW.Web, OpenTPW.Web.Veldrid) and not the tests. ALLOWED lists the
bodies that keep a loop, each with the reason it is safe (nothing the `try` assigned is read after it, or the code never runs in the browser).
"""
import re
import sys
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
PROJECTS = ["OpenTPW", "OpenTPW.Common", "OpenTPW.Files", "OpenTPW.Online", "OpenTPW.Web", "OpenTPW.Web.Veldrid"]

# "path:line of the finally" -> why it is safe
ALLOWED = {}

LOOP = re.compile(r"\b(for|foreach|while|do)\b")
LINQ = re.compile(r"(?<!Math)(?<!MathF)(?<!Math\.)\.(Select|SelectMany|Where|Any|All|ToArray|ToList|ToDictionary|ToHashSet|OrderBy|OrderByDescending|ThenBy|GroupBy|Distinct|Concat|Skip|Take|Sum|Min|Max|Aggregate|Zip|Reverse)\s*[(<]")


def strip(source: str) -> str:
    """The source with comments, strings and character literals blanked (newlines kept), so braces and keywords inside them do not count."""
    out = []
    i, n = 0, len(source)
    while i < n:
        c = source[i]
        nxt = source[i + 1] if i + 1 < n else ""
        if c == "/" and nxt == "/":
            while i < n and source[i] != "\n":
                out.append(" ")
                i += 1
        elif c == "/" and nxt == "*":
            while i < n and not (source[i] == "*" and i + 1 < n and source[i + 1] == "/"):
                out.append("\n" if source[i] == "\n" else " ")
                i += 1
            out.append("  ")
            i += 2
        elif c in "$@" and nxt in '"$@' or c == '"':
            verbatim = False
            j = i
            while j < n and source[j] in "$@":
                verbatim |= source[j] == "@"
                j += 1
            if j >= n or source[j] != '"':
                out.append(c)
                i += 1
                continue
            raw = source.startswith('"""', j)
            if raw:
                end = source.find('"""', j + 3)
                end = n if end < 0 else end + 3
                chunk = source[i:end]
            else:
                k = j + 1
                while k < n:
                    if verbatim and source[k] == '"' and source[k + 1:k + 2] == '"':
                        k += 2
                    elif not verbatim and source[k] == "\\":
                        k += 2
                    elif source[k] == '"':
                        break
                    else:
                        k += 1
                chunk = source[i:k + 1]
                end = k + 1
            out.append("".join("\n" if ch == "\n" else " " for ch in chunk))
            i = end
        elif c == "'":
            k = i + 1
            while k < n and source[k] != "'":
                k += 2 if source[k] == "\\" else 1
            out.append(" " * (k + 1 - i))
            i = k + 1
        else:
            out.append(c)
            i += 1
    return "".join(out)


def finally_bodies(source: str):
    """(line of `finally`, body text) for each `finally { ... }` of the source."""
    text = strip(source)
    for match in re.finditer(r"\bfinally\b\s*\{", text):
        depth, i = 1, match.end()
        while i < len(text) and depth:
            depth += {"{": 1, "}": -1}.get(text[i], 0)
            i += 1
        yield text.count("\n", 0, match.start()) + 1, text[match.end():i - 1]


def violations(root: Path = ROOT):
    found = []
    for project in PROJECTS:
        base = root / "source" / project
        if not base.is_dir():
            continue
        for path in sorted(base.rglob("*.cs")):
            relative = path.relative_to(root).as_posix()
            if "/obj/" in relative or "/bin/" in relative:
                continue
            for line, body in finally_bodies(path.read_text(encoding="utf-8-sig")):
                hit = LOOP.search(body) or LINQ.search(body)
                if hit and f"{relative}:{line}" not in ALLOWED:
                    found.append(f"{relative}:{line}: `{hit.group(0).strip()}` in a finally body")
    return found


class NoLoopInFinallyTests(unittest.TestCase):
    def test_no_finally_body_of_the_browser_build_loops(self):
        self.assertEqual([], violations(), "a loop or LINQ call in a finally body: move it into a helper method (tools/test_no_loop_in_finally.py)")

    def test_the_scan_sees_loops_linq_and_ignores_comments_and_strings(self):
        source = 'class C { void A() { try { } finally { foreach ( var x in y ) { } } }\n void B() { try { } finally { var t = "for"; // while\n Call(); } }\n void C2() { try { } finally { xs.Where( p => p ); } } }'
        bodies = list(finally_bodies(source))
        self.assertEqual(3, len(bodies))
        self.assertTrue(LOOP.search(bodies[0][1]))
        self.assertFalse(LOOP.search(bodies[1][1]) or LINQ.search(bodies[1][1]))
        self.assertTrue(LINQ.search(bodies[2][1]))

    def test_every_allowance_names_a_finally_that_still_exists(self):
        for key, reason in ALLOWED.items():
            self.assertTrue(reason.strip(), f"{key} needs a reason")
            path, line = key.rsplit(":", 1)
            lines = [number for number, _ in finally_bodies((ROOT / path).read_text(encoding="utf-8-sig"))]
            self.assertIn(int(line), lines, f"{key} no longer names a finally")


if __name__ == "__main__":
    sys.exit(unittest.main())

"""Progress-log fragments: names, headings, the frozen history and deterministic assembly (standard library only)."""
import subprocess
import sys
import unittest
from pathlib import Path
from tempfile import TemporaryDirectory

import progress

HISTORY = f"""# Execution status

Intro.

{progress.NOTE} new entries live in docs/progress/.
> Second line of the note.

{progress.FROZEN_TOP}

Frozen text, see [the plan](reverse/PLAN.md).

## Older entry (October 1, 2026)

Older text.
"""


class ProgressTests(unittest.TestCase):
    def setUp(self):
        self.temp = TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.write(progress.HISTORY, HISTORY)

    def write(self, relative, text):
        path = self.root / relative
        path.parent.mkdir(parents=True, exist_ok=True)
        # newline="\n": progress.py requires LF, and write_text would write CRLF on Windows
        path.write_text(text, encoding="utf-8", newline="\n")
        return path

    def fragment(self, name, text):
        return self.write(f"{progress.FRAGMENTS}/{name}", text)

    def test_valid_fragments_pass_and_assemble_newest_first_before_the_history(self):
        self.fragment("2026-10-16-first.md", "## First (October 16, 2026)\n\nOne.\n")
        self.fragment("2026-10-18-later.md", "## Later (October 18, 2026)\n\n### Detail\n\nTwo.\n")
        self.fragment("2026-10-16-another.md", "## Another (October 16, 2026)\n\nThree.\n")
        self.assertEqual(progress.check(self.root), [])
        log = progress.assemble(self.root)
        order = [log.index(text) for text in ("## Later", "## Another", "## First", progress.FROZEN_TOP, "## Older entry")]
        self.assertEqual(order, sorted(order))
        self.assertTrue(log.startswith("# Execution status\n\nIntro.\n\n## Later (October 18, 2026)"))
        self.assertNotIn("new entries live", log)
        self.assertNotIn("Second line of the note", log)
        self.assertTrue(log.endswith("Older text.\n"))
        self.assertEqual(log, progress.assemble(self.root))

    def test_history_alone_assembles_to_itself_without_the_note(self):
        log = progress.assemble(self.root)
        self.assertEqual(log, HISTORY.replace(f"{progress.NOTE} new entries live in docs/progress/.\n> Second line of the note.\n\n", ""))

    def test_relative_links_are_rewritten_to_resolve_from_docs(self):
        self.fragment("2026-10-16-links.md", "## Links (October 16, 2026)\n\n"
                      "[a](../TRACKS.md#junctions) [b](../reverse/X.md) [c](../../tools/progress.py) [d](#local) "
                      "[e](https://opentpw.io/docs/X.html) [f](/docs/Y.md)\n\n[ref]: ../GUESTS.md\n\n"
                      "```\n[code](../KEEP.md)\n```\n")
        log = progress.assemble(self.root)
        for expected in ("[a](TRACKS.md#junctions)", "[b](reverse/X.md)", "[c](../tools/progress.py)", "[d](#local)",
                         "[e](https://opentpw.io/docs/X.html)", "[f](/docs/Y.md)", "[ref]: GUESTS.md", "[code](../KEEP.md)"):
            self.assertIn(expected, log)

    def test_bad_names_and_headings_fail(self):
        cases = {
            "2026-10-16.md": "## Title (October 16, 2026)\n\nx\n",
            "2026-10-16-Upper.md": "## Title (October 16, 2026)\n\nx\n",
            "2026-02-30-bad-date.md": "## Title (February 30, 2026)\n\nx\n",
            "2026-10-16-notes.txt": "## Title (October 16, 2026)\n\nx\n",
            "2026-10-16-no-heading.md": "Text first.\n",
            "2026-10-16-wrong-date.md": "## Title (October 17, 2026)\n\nx\n",
            "2026-10-16-no-date.md": "## Title\n\nx\n",
            "2026-10-16-padded.md": "## Title (October 06, 2026)\n\nx\n",
            "2026-10-16-two-entries.md": "## One (October 16, 2026)\n\nx\n\n## Two (October 16, 2026)\n\ny\n",
            "2026-10-16-title.md": "## One (October 16, 2026)\n\n# Title\n",
            "2026-10-16-empty.md": "## One (October 16, 2026)\n\n",
            "2026-10-16-crlf.md": "## One (October 16, 2026)\r\n\r\nx\r\n",
        }
        for name, text in cases.items():
            with self.subTest(name=name):
                path = self.fragment(name, text)
                try:
                    errors = progress.check(self.root)
                    self.assertEqual(len(errors), 1, errors)
                    self.assertTrue(errors[0].startswith(f"{progress.FRAGMENTS}/{name}"), errors)
                finally:
                    path.unlink()
        self.assertEqual(progress.check(self.root), [])

    def test_headings_inside_fences_are_not_entries(self):
        self.fragment("2026-10-16-fence.md", "## One (October 16, 2026)\n\n```sh\n# a shell comment\n## not a heading\n```\n")
        self.assertEqual(progress.check(self.root), [])

    def test_subdirectories_are_rejected(self):
        (self.root / progress.FRAGMENTS / "nested").mkdir(parents=True)
        self.assertTrue(any("only fragment files" in error for error in progress.check(self.root)))

    def test_history_is_frozen_and_keeps_its_note(self):
        self.write(progress.HISTORY, HISTORY.replace(progress.FROZEN_TOP, "## Sneaked in (October 20, 2026)\n\nNew.\n\n" + progress.FROZEN_TOP))
        self.assertTrue(any("history is frozen" in error for error in progress.check(self.root)))
        self.write(progress.HISTORY, HISTORY.replace(progress.NOTE, "> Note"))
        self.assertTrue(any("note starting with" in error for error in progress.check(self.root)))

    def test_cli(self):
        tool = Path(__file__).with_name("progress.py")
        self.fragment("2026-10-16-first.md", "## First (October 16, 2026)\n\nOne.\n")
        result = subprocess.run([sys.executable, str(tool), "--root", str(self.root), "--check"], capture_output=True, text=True)
        self.assertEqual(result.returncode, 0, result.stderr)
        output = self.root / "out/PROGRESS.md"
        result = subprocess.run([sys.executable, str(tool), "--root", str(self.root), "--output", str(output)],
                                capture_output=True, text=True)
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertEqual(output.read_text(encoding="utf-8"), progress.assemble(self.root))
        self.fragment("2026-10-16-bad.md", "no heading\n")
        result = subprocess.run([sys.executable, str(tool), "--root", str(self.root), "--check"], capture_output=True, text=True)
        self.assertEqual(result.returncode, 1)
        self.assertIn("2026-10-16-bad.md:1", result.stderr)

    def test_migrate_moves_entries_above_the_frozen_history_into_fragments(self):
        old = HISTORY.replace(progress.FROZEN_TOP, "## Branch work: part 2! (October 17, 2026)\n\nTwo.\n\n"
                              "## Branch work (October 16, 2026)\n\n### Detail\n\nOne.\n\n" + progress.FROZEN_TOP)
        written = progress.migrate(self.root, old)
        self.assertEqual([path.name for path in written], ["2026-10-17-branch-work-part-2.md", "2026-10-16-branch-work.md"])
        self.assertEqual(written[1].read_text(encoding="utf-8"), "## Branch work (October 16, 2026)\n\n### Detail\n\nOne.\n")
        self.assertEqual(progress.check(self.root), [])
        with self.assertRaises(ValueError):
            progress.migrate(self.root, old)  # never overwrites
        self.assertEqual(progress.migrate(self.root, HISTORY), [])
        with self.assertRaises(ValueError):
            progress.migrate(self.root, "## Unrelated (October 1, 2026)\n")

    def test_repository_log_is_consistent(self):
        root = Path(__file__).resolve().parents[1]
        self.assertEqual(progress.check(root), [])


if __name__ == "__main__":
    unittest.main()

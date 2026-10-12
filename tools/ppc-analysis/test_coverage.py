"""Synthetic tests for coverage.py's pure helpers (no game binary needed)."""

import os
import sys
import unittest

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import coverage  # noqa: E402


class CoverageTests(unittest.TestCase):
    def test_cited_addresses_reads_full_and_short_forms(self):
        text = "see 0x100BB4FC and [BIN:STP-PPC:0x1007497C ride] and STP-PPC 0xafa68; not 0x2000 or 0x1234567890"
        self.assertEqual(coverage.cited_addresses(text), {0x100BB4FC, 0x1007497C, 0x100AFA68})

    def test_containing_finds_the_function_range(self):
        starts, ends = [0x10, 0x40, 0x80], [0x30, 0x80, 0x84]
        self.assertEqual(coverage.containing(starts, ends, 0x10), 0)
        self.assertEqual(coverage.containing(starts, ends, 0x2C), 0)
        self.assertIsNone(coverage.containing(starts, ends, 0x34))
        self.assertEqual(coverage.containing(starts, ends, 0x7C), 1)
        self.assertIsNone(coverage.containing(starts, ends, 0x08))

    def test_clean_keeps_readable_strings_only(self):
        self.assertEqual(coverage.clean("Save world FAILED\n"), "Save world FAILED")
        self.assertIsNone(coverage.clean("\xff\xff\xff\xff"))
        self.assertIsNone(coverage.clean("%d"))
        self.assertEqual(coverage.clean("a|b ccc"), "a/b ccc")
        self.assertEqual(len(coverage.clean("x" * 10 + "abc" * 40, limit=20)), 20)


class RepositoryTextTests(unittest.TestCase):
    def test_the_generated_map_is_not_counted_as_a_citation(self):
        import subprocess, tempfile
        with tempfile.TemporaryDirectory() as repo:
            subprocess.run(["git", "init", "-q", repo], check=True)
            os.makedirs(os.path.join(repo, "docs", "reverse"))
            with open(os.path.join(repo, "docs", "a.md"), "w") as f:
                f.write("cites 0x100BB4FC\n")
            for generated in coverage.GENERATED:
                with open(os.path.join(repo, generated), "w") as f:
                    f.write("| `0x10063DD0` | uncited |\n")
            subprocess.run(["git", "-C", repo, "add", "-A"], check=True)
            self.assertEqual(coverage.cited_addresses(coverage.repository_text(repo)), {0x100BB4FC})
            self.assertEqual(coverage.citations_by_file(repo), {"docs/a.md": ({0x100BB4FC}, set())})

    def test_short_offsets_are_read_in_research_notes_only(self):
        import subprocess, tempfile
        with tempfile.TemporaryDirectory() as repo:
            subprocess.run(["git", "init", "-q", repo], check=True)
            os.makedirs(os.path.join(repo, "docs", "reverse"))
            with open(os.path.join(repo, "docs", "reverse", "PATH-plan.md"), "w") as f:
                f.write("LayLine `0x84ea4`, flag data:0x84b2c, field +0x2d8\n")
            with open(os.path.join(repo, "docs", "b.md"), "w") as f:
                f.write("outside the notes: 0x84ea4\n")
            subprocess.run(["git", "-C", repo, "add", "-A"], check=True)
            self.assertEqual(coverage.citations_by_file(repo),
                             {"docs/reverse/PATH-plan.md": (set(), {coverage.MAC_BASE + 0x84EA4})}, "b.md has no full address")


    def test_the_area_configuration_is_not_a_citation(self):
        import subprocess, tempfile
        with tempfile.TemporaryDirectory() as repo:
            subprocess.run(["git", "init", "-q", repo], check=True)
            os.makedirs(os.path.join(repo, "tools", "ppc-analysis"))
            with open(os.path.join(repo, "tools", "ppc-analysis", "areas.json"), "w") as f:
                f.write('{"functions": {"0x10F1E418": "text"}}\n')
            subprocess.run(["git", "-C", repo, "add", "-A"], check=True)
            self.assertEqual(coverage.citations_by_file(repo), {})
            self.assertEqual(coverage.cited_addresses(coverage.repository_text(repo)), set())


class ShortOffsetTests(unittest.TestCase):
    def test_explicit_code_offsets_are_read_everywhere(self):
        self.assertEqual(coverage.cited_addresses("see code:0x1c3fbc, not data:0x1c3fbc"), {coverage.MAC_BASE + 0x1C3FBC})

    def test_short_offsets_skip_data_and_structure_offsets(self):
        text = "`0x84ea4` and 0x1c22c4; data:0x84b04, +0x2d8, TOC -0x6dd0, 0x7fffff, 0x12345678"
        self.assertEqual(coverage.short_offsets(text), {coverage.MAC_BASE + 0x84EA4, coverage.MAC_BASE + 0x1C22C4})

    def test_a_short_offset_counts_only_at_a_function_start(self):
        # Synthetic ranges past the end of the code section, so this file cites no real function.
        starts, ends = [0x10F84EA4, 0x10F85200], [0x10F85174, 0x10F85300]
        self.assertEqual(coverage.resolve((set(), {0x10F84EA4, 0x10F85210}), starts, ends), {0})
        self.assertEqual(coverage.resolve(({0x10F85210}, set()), starts, ends), {1})


if __name__ == "__main__":
    unittest.main()

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
            with open(os.path.join(repo, coverage.OUTPUT), "w") as f:
                f.write("| `0x10063DD0` | uncited |\n")
            subprocess.run(["git", "-C", repo, "add", "-A"], check=True)
            self.assertEqual(coverage.cited_addresses(coverage.repository_text(repo)), {0x100BB4FC})


if __name__ == "__main__":
    unittest.main()

"""Publishing must reject broken local navigation and unsafe link schemes."""

import importlib.util
from pathlib import Path
import tempfile
import unittest

spec = importlib.util.spec_from_file_location("check_site", Path(__file__).with_name("check_site.py"))
checker = importlib.util.module_from_spec(spec)
spec.loader.exec_module(checker)


class SiteCheckTests(unittest.TestCase):
    def check(self, first: str, second: str = '<h1 id="section">Guide</h1>') -> list[str]:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            (root / "docs").mkdir()
            (root / "index.html").write_text(first, encoding="utf-8")
            (root / "docs/guide.html").write_text(second, encoding="utf-8")
            return checker.check(root)

    def test_valid_local_anchor_and_external_links(self):
        self.assertEqual([], self.check('<a href="/docs/guide.html#section">Guide</a><a href="https://example.com">External</a>'))

    def test_missing_page_and_section_fail(self):
        errors = self.check('<a href="missing.html">Page</a><a href="docs/guide.html#missing">Section</a>')
        self.assertEqual(2, len(errors))
        self.assertTrue(any("Missing local target" in error for error in errors))
        self.assertTrue(any("Missing section anchor" in error for error in errors))

    def test_traversal_and_active_schemes_fail(self):
        errors = self.check('<a href="../outside.html">Outside</a><a href="javascript:alert(1)">Active</a>')
        self.assertEqual(2, len(errors))
        self.assertTrue(any("escapes website" in error for error in errors))
        self.assertTrue(any("Unsupported link scheme" in error for error in errors))

    def test_own_repository_markdown_link_fails(self):
        errors = self.check('<a href="https://github.com/meneerkrabs/OpenTPW/blob/main/docs/SETUP.md">Setup</a>')
        self.assertEqual(1, len(errors))
        self.assertIn("Documentation links to Markdown", errors[0])

    def test_extensionless_page_and_percent_encoded_anchor(self):
        self.assertEqual([], self.check('<a href="docs/guide#sec%74ion">Section</a>'))

    def test_content_policy_blocks_inline_style_and_third_party_image(self):
        errors = self.check('<p style="color:red">Text</p><img src="https://example.com/image.png">')
        self.assertEqual(2, len(errors))
        self.assertTrue(all("content policy" in error for error in errors))


if __name__ == "__main__":
    unittest.main()

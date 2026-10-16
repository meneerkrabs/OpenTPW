"""Regression checks for public HTML docs (Python standard library only)."""

from __future__ import annotations

import contextlib
import importlib.util
import io
import tempfile
import unittest
from html.parser import HTMLParser
from pathlib import Path
from urllib.parse import unquote, urlsplit


MODULE = Path(__file__).with_name("site_docs.py")
SPEC = importlib.util.spec_from_file_location("site_docs", MODULE)
site_docs = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(site_docs)


class Links(HTMLParser):
    def __init__(self, source: str):
        super().__init__()
        self.links: list[str] = []
        self.ids: list[str] = []
        self.tags: list[str] = []
        self.feed(source)

    def handle_starttag(self, tag: str, attrs: list[tuple[str, str | None]]) -> None:
        self.tags.append(tag)
        values = dict(attrs)
        for key in ("href", "src"):
            if values.get(key):
                self.links.append(values[key])
        if values.get("id"):
            self.ids.append(values["id"])


class SiteDocsTests(unittest.TestCase):
    def setUp(self) -> None:
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.repo = Path(self.temp.name) / "repo"
        self.repo.mkdir()
        self.write("README.md", "# OpenTPW\n\n[Docs](docs/) and [licence](LICENSE.md).")
        self.write("LICENSE.md", "The MIT License\n===============\n\nPermission is granted.")
        self.write("docs/START.md", "# Start\n\n## Game folder\n")
        self.write("docs/reverse/EVIDENCE.md", "# Evidence\n")
        self.write("tools/example/README.md", "# Example tool\n")
        self.write("source/engine.cs", "// Example source")
        self.write("images/logo.png", "placeholder")

    def write(self, relative: str, text: str) -> None:
        path = self.repo / relative
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(text, encoding="utf-8")

    def converter(self, source: str = "docs/START.md"):
        return site_docs.Converter(source, str(self.repo))

    def test_document_links_are_html_at_every_depth(self) -> None:
        converter = self.converter("docs/reverse/EVIDENCE.md")
        self.assertEqual(converter.link_target("../START.md#game-folder"), "../START.html#game-folder")
        self.assertEqual(converter.link_target("../../README.md"), "../README.html")
        self.assertEqual(converter.link_target("../../LICENSE.md"), "../LICENSE.html")
        self.assertEqual(converter.link_target("../../tools/example/README.md"), "../tools/example/README.html")
        self.assertEqual(converter.link_target("https://github.com/meneerkrabs/OpenTPW/blob/main/docs/START.md#game-folder"), "../START.html#game-folder")
        self.assertEqual(converter.link_target("https://github.com/OpenTPW/OpenTPW/blob/revision/docs/START.md"), "../START.html")
        self.assertEqual(converter.link_target("https://raw.githubusercontent.com/meneerkrabs/OpenTPW/main/docs/START.md"), "../START.html")
        self.assertEqual(converter.link_target("https://opentpw.io/docs/START.md"), "../START.html")
        self.assertEqual(self.converter("README.md").link_target("docs/"), "index.html")

    def test_code_and_emphasis_inside_link_labels(self) -> None:
        result = self.converter().inline("[the `GameFolder` **setting**](START.md#game-folder)")
        self.assertEqual(result, '<a href="START.html#game-folder">the <code>GameFolder</code> <strong>setting</strong></a>')
        code = self.converter().inline("``literal `tick` [not a link](START.md)``")
        self.assertEqual(code, "<code>literal `tick` [not a link](START.md)</code>")

    def test_link_destination_parentheses_escapes_and_angle_wrapping(self) -> None:
        self.write("docs/A (B).md", "# Space")
        converter = self.converter()
        result = converter.inline('[file](<A (B).md> "A title") and [reference](https://example.org/page_(one)) and \\[literal]')
        self.assertIn('href="A%20%28B%29.html"', result)
        self.assertIn('href="https://example.org/page_(one)"', result)
        self.assertIn("[literal]", result)

    def test_dangerous_and_private_urls_never_become_links_or_images(self) -> None:
        converter = self.converter()
        targets = ["javascript:alert(1)", "JAVASCRIPT:alert(1)", "data:text/html,<script>", "file:///Users/private/game.png", "/Users/private/game.png", "/Volumes/private/game.png", "../../secret.md", "https://[bad"]
        for target in targets:
            with self.subTest(target=target):
                self.assertIsNone(converter.link_target(target))
                self.assertIsNone(converter.link_target(target, image=True))
        rendered = converter.inline("[bad](javascript:alert(1)) ![private](/Users/private/game.png)")
        self.assertNotIn("href=", rendered)
        self.assertNotIn("src=", rendered)
        self.assertEqual(len(Links(rendered).links), 0)
        self.assertTrue(converter.warnings)

    def test_external_images_remain_links_under_the_site_csp(self) -> None:
        converter = self.converter()
        source = 'https://example.com/figure.png'
        for markdown in ('![Figure](' + source + ')', '<img src="' + source + '" alt="Figure">'):
            result = converter.inline(markdown)
            self.assertIn('href="' + source + '"', result)
            self.assertIn('View image: Figure', result)
            self.assertNotIn('<img', result)

    def test_website_screenshots_use_the_existing_static_asset(self) -> None:
        self.write("website/images/shot.png", "fixture")
        self.assertEqual(self.converter("README.md").link_target("website/images/shot.png", image=True), "../images/shot.png")
        self.assertEqual(self.converter("docs/reverse/EVIDENCE.md").link_target("../../website/images/shot.png", image=True), "../../images/shot.png")

    def test_existing_public_images_and_source_links(self) -> None:
        converter = self.converter()
        self.assertEqual(converter.link_target("../images/logo.png", image=True), "https://raw.githubusercontent.com/meneerkrabs/OpenTPW/main/images/logo.png")
        self.assertEqual(converter.link_target("../source/engine.cs"), site_docs.GITHUB + "source/engine.cs")
        self.assertIsNone(converter.link_target("../source/engine.cs", image=True))
        self.assertIsNone(converter.link_target("../images/missing.png", image=True))
        outside = Path(self.temp.name) / "private.png"
        outside.write_text("private", encoding="utf-8")
        (self.repo / "images" / "private.png").symlink_to(outside)
        self.assertIsNone(converter.link_target("../images/private.png", image=True))

    def test_readme_html_header_is_safe_and_keeps_navigation(self) -> None:
        converter = self.converter("README.md")
        result = converter.convert('<p align="center">\n<h1 onclick="bad()">OpenTPW</h1>\n<a href="docs/">Documentation</a>\n<script>alert(1)</script>\n<a href="javascript:bad()">Bad link</a>\n</p>\n\nNormal **text**.')
        self.assertIn('<h1 id="opentpw">OpenTPW</h1>', result)
        self.assertIn('<a href="index.html">Documentation</a>', result)
        self.assertNotIn("script", result)
        self.assertNotIn("onclick", result)
        self.assertNotIn("javascript:", result)
        self.assertIn("Bad link", result)
        self.assertEqual(converter.title, "OpenTPW")

    def test_nested_lists_fences_and_ordered_start_preserve_content(self) -> None:
        result = self.converter().convert("3. Windows\n   - Extract the files.\n   - Run `OpenTPW.exe`.\n4. Linux\n\n   ```sh\n   ./OpenTPW --game-path /games/TPW\n   ```\n\n- [x] Tested\n- [ ] Pending\n")
        self.assertIn('<ol start="3">', result)
        self.assertIn("<ul>", result)
        self.assertIn("<code>OpenTPW.exe</code>", result)
        self.assertIn('<pre><code class="language-sh">./OpenTPW --game-path /games/TPW</code></pre>', result)
        self.assertIn('type="checkbox" disabled checked', result)
        self.assertIn("Pending", result)
        self.assertEqual(result.count("<li>"), 6)

    def test_table_pipes_inside_code_are_not_cell_boundaries(self) -> None:
        result = self.converter().convert("Name | Value\n:--- | ---:\n``a|b`` | a\\|b\n")
        self.assertEqual(result.count("<td"), 2)
        self.assertIn("<code>a|b</code>", result)
        self.assertIn('class="align-right">a|b</td>', result)

    def test_long_fence_can_contain_shorter_fence_and_markup(self) -> None:
        result = self.converter().convert("````markdown\n```\n[link](START.md)\n<script>literal</script>\n```\n````\n")
        self.assertIn("&lt;script&gt;literal&lt;/script&gt;", result)
        self.assertIn("[link](START.md)", result)
        self.assertEqual(result.count("<pre>"), 1)
        self.assertNotIn("<a ", result)

    def test_headings_reference_links_and_duplicate_ids(self) -> None:
        converter = self.converter()
        result = converter.convert("A title\n=======\n\n## Same\n## Same-1\n## Same\n\n[Guide][start]\n\n[start]: START.md#game-folder\n")
        ids = Links(result).ids
        self.assertEqual(len(ids), len(set(ids)))
        self.assertIn("same-2", ids)
        self.assertIn('<a href="START.html#game-folder">Guide</a>', result)
        self.assertEqual(converter.title, "A title")
        self.assertEqual(site_docs.slug("Ride data: `<ride>.sam`"), "ride-data-ridesam")
        self.assertEqual(site_docs.slug("`ANIM_Main`"), "anim_main")

    def test_hard_breaks_do_not_leave_escape_markers(self) -> None:
        result = self.converter().convert("First  \nSecond\\\nThird")
        self.assertEqual(result, "<p>First<br>Second<br>Third</p>")

    def test_stale_source_links_are_qualified_and_reported(self) -> None:
        self.write("docs/START.md", "# Start\n\n[Old section](START.md#renamed) and [missing](MISSING.md).")
        out = self.repo / "website" / "docs"
        warnings = io.StringIO()
        with contextlib.redirect_stdout(io.StringIO()), contextlib.redirect_stderr(warnings):
            self.assertEqual(site_docs.build(str(self.repo), str(out)), 0)
        rendered = (out / "START.html").read_text(encoding="utf-8")
        self.assertNotIn('href="START.html#renamed"', rendered)
        self.assertIn('href="START.html" title="This source section has moved;', rendered)
        self.assertNotIn('href="MISSING.md"', rendered)
        self.assertIn("source section missing", warnings.getvalue())
        self.assertIn("missing public document", warnings.getvalue())

    def test_build_publishes_public_sources_and_only_removes_owned_stale_pages(self) -> None:
        self.write("CLAUDE.md", "Private agent instructions")
        self.write("AGENTS.md", "Internal guide")
        out = self.repo / "website" / "docs"
        with contextlib.redirect_stdout(io.StringIO()):
            self.assertEqual(site_docs.build(str(self.repo), str(out)), 0)
        self.assertTrue((out / "README.html").is_file())
        self.assertTrue((out / "LICENSE.html").is_file())
        self.assertTrue((out / "tools/example/README.html").is_file())
        self.assertFalse((out / "CLAUDE.html").exists())
        self.assertFalse((out / "AGENTS.html").exists())
        manual = out / "manual.html"
        manual.write_text("Hand-authored page", encoding="utf-8")
        (self.repo / "docs/reverse/EVIDENCE.md").unlink()
        with contextlib.redirect_stdout(io.StringIO()):
            site_docs.build(str(self.repo), str(out))
        self.assertFalse((out / "reverse/EVIDENCE.html").exists())
        self.assertEqual(manual.read_text(encoding="utf-8"), "Hand-authored page")

    def test_progress_log_is_assembled_and_fragments_are_not_pages(self) -> None:
        self.write("docs/PROGRESS.md", "# Progress\n\n> **New entries:** see docs/progress/.\n\n## Old entry (October 1, 2026)\n\nOld text.\n")
        self.write("docs/progress/2026-10-20-new-thing.md", "## New thing (October 20, 2026)\n\nSee [Start](../START.md#game-folder).\n")
        out = self.repo / "website" / "docs"
        warnings = io.StringIO()
        with contextlib.redirect_stdout(io.StringIO()), contextlib.redirect_stderr(warnings):
            self.assertEqual(site_docs.build(str(self.repo), str(out)), 0)
        self.assertEqual(warnings.getvalue(), "")
        log = (out / "PROGRESS.html").read_text(encoding="utf-8")
        self.assertLess(log.index("New thing"), log.index("Old entry"))
        self.assertIn('href="START.html#game-folder"', log)
        self.assertNotIn("New entries", log)
        self.assertFalse((out / "progress").exists())
        self.assertNotIn("docs/progress/2026-10-20-new-thing.md", site_docs.document_sources(self.repo))

    def test_actual_public_documentation_has_complete_internal_navigation(self) -> None:
        """Exercise the real corpus, including deeply nested tooling links."""
        repo = MODULE.resolve().parents[1]
        out = Path(self.temp.name) / "full-docs"
        warnings = io.StringIO()
        with contextlib.redirect_stdout(io.StringIO()), contextlib.redirect_stderr(warnings):
            self.assertEqual(site_docs.build(str(repo), str(out)), 0)
        documents = {path.resolve(): Links(path.read_text(encoding="utf-8")) for path in out.rglob("*.html")}
        published = site_docs.document_sources(repo)
        self.assertEqual(len(documents), len(published) + 1)
        for path, parsed in documents.items():
            self.assertEqual(len(parsed.ids), len(set(parsed.ids)), path)
            for href in parsed.links:
                url = urlsplit(href)
                if url.scheme or url.netloc:
                    self.assertIn(url.scheme, {"http", "https", "mailto"}, (path, href))
                    if url.netloc == "github.com" and url.path.startswith("/meneerkrabs/OpenTPW/"):
                        self.assertFalse(url.path.endswith(".md"), (path, href))
                    continue
                destination = (path.parent / unquote(url.path)).resolve() if url.path else path
                if destination.suffix == ".html" and destination.is_relative_to(out.resolve()):
                    self.assertIn(destination, documents, (path, href))
                    if url.fragment:
                        self.assertIn(unquote(url.fragment), documents[destination].ids, (path, href))
                self.assertFalse(url.path.endswith(".md"), (path, href))
        self.assertEqual(warnings.getvalue(), "", warnings.getvalue())


if __name__ == "__main__":
    unittest.main()

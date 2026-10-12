"""Tests for tools/site_status.py on a small synthetic repository (no network)."""

from __future__ import annotations

import importlib.util
import json
import subprocess
import tempfile
import unittest
from pathlib import Path

MODULE = Path(__file__).with_name("site_status.py")
SPEC = importlib.util.spec_from_file_location("site_status", MODULE)
site_status = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(site_status)

REGISTER = """# Fidelity register

Current inventory: **12 unresolved unique APPROX IDs**, 15 APPROX occurrences.

Of these, 9 IDs belong to the existing original-fidelity areas (x), and 3 ONLINE IDs record uncertainty.

| Area | Unique unresolved IDs | Source occurrences |
| --- | ---: | ---: |
| AUDIO | 9 | 11 |
| ONLINE | 3 | 4 |

## Approximation declarations
"""

ORACLE = """# Oracle

## 4. Targets, stubs and results

| Target | Routine (allowed code) | Stubs | Inputs | vs plan | vs C# on main |
| --- | --- | --- | --- | --- | --- |
| `a-target` | `0x1` | none | 5 | **agree** | **agree** (F1 fixed) |
| `b-target` | `0x2` | none | 5 | **agree** | `X.Y` through `y`: **agree** except 4 inputs (cause) |
| `c-target` | `0x3` | none | 5 | **F9**: 49 differ | **F9**: 49 differ, the same inputs |
| `d-target` | `0x4` | none | 5 | **agree** | no C# hook (CSHARP_PENDING) |
| `e-target` | `0x5` | none | 5 | **agree** | no port on main (hook `x`) |

## 5. Findings

**F1, queue limit polarity (QUEUE-plan §4, `Foo.Bar`).** Text that mentions fixed in C# later on the same line.

**F12, loan instalment (ECON-006, `LoanMath`; fixed in C#).** Text.

**F2, saturation (C#).** Text.

### 7.4 Finding X1: tp.exe is the same generator, with a different step

**X3, arrival group size (Mac `0xcb4e8`, F9): tp.exe; same rules.**
"""

MATRIX = """# Matrix

| Required area | Gate | Implementation | macOS | Windows | Linux | Original VM | Evidence / remaining requirement |
| --- | --- | --- | --- | --- | --- | --- | --- |
| Economy | M3 | approximated | prototype-pass | unverified | unverified | not-applicable | [ECONOMY.md](ECONOMY.md): ledger |
| Saves | M2 | unassessed | unverified | unverified | unverified | unverified | Full state |
"""

README = """# OpenTPW

### File formats

| Format | Status | Verified support / remaining limit |
|---|---|---|
| Textures ([.WCT](https://x/wct.html)) | ✅ | Decoder loads textures. |
| Models ([.MD2](https://x/m.html)) | ⚠️ | Most parse. |
"""

CURATED = {
    "can": [{"text": "Build parks.", "href": "docs/ECONOMY.html"}],
    "cannot": [{"text": "Build karts.", "matrix": ["Saves"]}],
    "later": "Theme Park Inc is later.",
}

INDEX = f"""<html><body>
{site_status.BEGIN}
{site_status.END}
</body></html>
"""


def make_repo(root: Path) -> Path:
    files = {"docs/FIDELITY-REGISTER.md": REGISTER, "docs/reverse/ORACLE.md": ORACLE, "docs/FEATURE-MATRIX.md": MATRIX,
             "docs/ECONOMY.md": "# Economy\n", "README.md": README, "website/index.html": INDEX,
             "tools/site_status_curated.json": json.dumps(CURATED)}
    for name, text in files.items():
        path = root / name
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(text, encoding="utf-8")
    return root


class Parsing(unittest.TestCase):
    def test_register(self):
        r = site_status.parse_register(REGISTER)
        self.assertEqual((r["total"], r["original"], r["online"]), (12, 9, 3))
        self.assertEqual(r["areas"][0], {"area": "AUDIO", "ids": 9, "occurrences": 11})

    def test_register_missing_is_an_error(self):
        with self.assertRaises(ValueError):
            site_status.parse_register("# nothing")

    def test_oracle_titles_and_fixed(self):
        o = site_status.parse_oracle(ORACLE)
        ids = {i["id"]: i for i in o["findings"]}
        self.assertEqual(list(ids), ["F1", "F2", "F12", "X1", "X3"])
        self.assertEqual(ids["F12"]["title"], "loan instalment")
        self.assertEqual(ids["X3"]["title"], "arrival group size")
        self.assertEqual((o["f_count"], o["x_count"]), (3, 2))

    def test_csharp_agreement_rule(self):
        o = site_status.parse_oracle(ORACLE)
        self.assertEqual({t["target"]: t["csharp"] for t in o["targets"]},
                         {"a-target": "agree", "b-target": "partial", "c-target": "differs", "d-target": "no-port", "e-target": "no-port"})
        self.assertEqual((o["csharp_agrees"], o["target_count"]), (1, 5))
        self.assertEqual(site_status.csharp_status("**Agree** 5/5"), "agree")

    def test_matrix_and_formats(self):
        m = site_status.parse_matrix(MATRIX)
        self.assertEqual(m["counts"], {"approximated": 1, "unassessed": 1})
        self.assertEqual(m["rows"][0]["evidence"], "ECONOMY.md: ledger")
        f = site_status.parse_formats(README)
        self.assertEqual((f["decoded"], f["partial"]), (1, 1))
        self.assertEqual(f["rows"][0]["format"], "Textures")

    def test_pulls_keep_only_merged_newest_first(self):
        data = [{"number": 1, "title": "closed", "merged_at": None, "html_url": "u1"},
                {"number": 2, "title": "old", "merged_at": "2026-10-01T10:00:00Z", "html_url": "u2"},
                {"number": 3, "title": "new", "merged_at": "2026-10-03T10:00:00Z", "html_url": "u3"}]
        got = site_status.pulls_from_api(data, 5)
        self.assertEqual([p["number"] for p in got], [3, 2])
        self.assertEqual(got[0]["date"], "2026-10-03")


class Repository(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.addCleanup(self.tmp.cleanup)
        self.repo = make_repo(Path(self.tmp.name))

    def commit(self, *subjects: str):
        run = lambda *a: subprocess.run(["git", "-C", str(self.repo), "-c", "user.name=t", "-c", "user.email=t@t", *a], check=True, capture_output=True)
        run("init", "-q")
        for subject in subjects:
            run("commit", "-q", "--allow-empty", "-m", subject)

    def test_git_fallback_reads_first_parent_log(self):
        self.commit("First change (#7)", "Second change")
        items = site_status.recent(self.repo, None)
        self.assertEqual(items["source"], "git")
        self.assertEqual([i["title"] for i in items["items"]], ["Second change", "First change"])
        self.assertEqual(items["items"][1]["number"], 7)
        self.assertTrue(items["items"][1]["url"].endswith("/pull/7"))

    def test_api_file_wins_and_a_broken_file_falls_back(self):
        self.commit("Only commit")
        good = self.repo / "pulls.json"
        good.write_text(json.dumps([{"number": 5, "title": "T", "merged_at": "2026-10-02T00:00:00Z", "html_url": "https://github.com/x/y/pull/5"}]))
        self.assertEqual(site_status.recent(self.repo, good)["source"], "github")
        good.write_text("not json")
        self.assertEqual(site_status.recent(self.repo, good)["source"], "git")

    def test_not_a_git_checkout_still_builds(self):
        status = site_status.collect(self.repo)
        self.assertEqual(status["commit"]["short"], "unknown")
        self.assertEqual(status["recent"]["items"], [])

    def test_curated_validation(self):
        matrix = site_status.parse_matrix(MATRIX)
        site_status.load_curated(self.repo, matrix)
        bad = dict(CURATED, cannot=[{"text": "x", "matrix": ["No such area"]}], can=[{"text": "y", "href": "docs/MISSING.html"}])
        (self.repo / "tools/site_status_curated.json").write_text(json.dumps(bad))
        with self.assertRaises(ValueError) as caught:
            site_status.load_curated(self.repo, matrix)
        self.assertIn("No such area", str(caught.exception))
        self.assertIn("docs/MISSING.html", str(caught.exception))

    def run_main(self, *args: str) -> int:
        old = site_status.sys.argv
        site_status.sys.argv = ["site_status.py", "--repo", str(self.repo), *args]
        try:
            return site_status.main()
        finally:
            site_status.sys.argv = old

    def test_front_block_check_write_and_outputs(self):
        self.commit("Change (#1)")
        with contextlib_redirect():
            self.assertEqual(self.run_main(), 1, "stale front page block fails the build")
            self.assertEqual(self.run_main("--write-front"), 0)
            self.assertEqual(self.run_main(), 0)
        index = (self.repo / "website/index.html").read_text(encoding="utf-8")
        self.assertIn("Build parks.", index)
        self.assertIn('<a href="docs/ECONOMY.html">Details</a>', index)
        data = json.loads((self.repo / "website/docs/status.json").read_text(encoding="utf-8"))
        self.assertEqual(data["fidelity"]["total"], 12)
        self.assertEqual(data["oracle"]["csharp_agrees"], 1)
        page = (self.repo / "website/docs/status.html").read_text(encoding="utf-8")
        self.assertIn("Generated from commit", page)
        self.assertIn("loan instalment", page)

    def test_output_is_deterministic(self):
        self.commit("Change")
        with contextlib_redirect():
            self.run_main("--write-front")
            first = (self.repo / "website/docs/status.json").read_bytes(), (self.repo / "website/docs/status.html").read_bytes()
            self.run_main()
            second = (self.repo / "website/docs/status.json").read_bytes(), (self.repo / "website/docs/status.html").read_bytes()
        self.assertEqual(first, second)

    def test_html_is_escaped(self):
        status = site_status.collect(self.repo)
        status["recent"] = {"source": "github", "items": [{"number": 1, "title": "<script>x</script>", "date": "2026-10-01", "url": "https://github.com/x"}]}
        self.assertNotIn("<script>x", site_status.render_page(status))


def contextlib_redirect():
    import contextlib
    import io
    stack = contextlib.ExitStack()
    stack.enter_context(contextlib.redirect_stdout(io.StringIO()))
    stack.enter_context(contextlib.redirect_stderr(io.StringIO()))
    return stack


class RealRepository(unittest.TestCase):
    def test_the_real_documents_parse_and_front_page_is_current(self):
        repo = Path(__file__).resolve().parents[1]
        status = site_status.collect(repo)
        self.assertGreater(status["fidelity"]["total"], 0)
        self.assertEqual(status["fidelity"]["total"], status["fidelity"]["original"] + status["fidelity"]["online"])
        self.assertEqual(status["fidelity"]["total"], sum(a["ids"] for a in status["fidelity"]["areas"]))
        self.assertGreater(status["oracle"]["f_count"], 0)
        current, _ = site_status.front_current(repo, status["curated"])
        self.assertTrue(current, "website/index.html is out of date: run python3 tools/site_status.py --write-front")


if __name__ == "__main__":
    unittest.main()

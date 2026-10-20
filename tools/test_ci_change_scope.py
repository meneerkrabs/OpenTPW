import subprocess
import unittest
from unittest.mock import patch

import ci_change_scope as scope


class ChangeScopeTests(unittest.TestCase):
    def test_only_proposal_and_progress_markdown_qualify(self):
        self.assertTrue(scope.proposal_docs_only(["docs/proposals/a.md", "docs/progress/2026-10-18-a.md"]))
        self.assertTrue(scope.proposal_docs_only(["docs/proposals/subdir/a b.md"]))

    def test_empty_other_docs_code_workflow_and_assets_require_full_ci(self):
        for paths in [[], ["docs/LIPS.md"], ["README.md"], ["source/OpenTPW/a.cs"],
                      [".github/workflows/build.yml"], ["tools/ci_change_scope.py"],
                      ["docs/proposals/a.png"], ["docs/proposals/a.json"],
                      ["docs/proposals/a.md", "source/OpenTPW/a.cs"]]:
            with self.subTest(paths=paths):
                self.assertFalse(scope.proposal_docs_only(paths))

    def test_ambiguous_or_escaping_paths_require_full_ci(self):
        for path in ["/docs/proposals/a.md", "docs/proposals/../a.md", "docs/proposals//a.md",
                     "docs/proposals/./a.md", "docs/proposals/a\\b.md", "docs/proposal/a.md"]:
            with self.subTest(path=path):
                self.assertFalse(scope.proposal_docs_only([path]))

    def test_non_pull_request_events_never_use_lightweight_mode(self):
        with patch.object(scope, "changed_paths") as changed:
            for event in ["push", "workflow_dispatch", "pull_request_target", "", "release"]:
                self.assertFalse(scope.classify(event, "a" * 40, "b" * 40))
            changed.assert_not_called()

    def test_unknown_diff_is_full_ci(self):
        for error in [OSError(), ValueError(), UnicodeError(), subprocess.CalledProcessError(1, "git")]:
            with patch.object(scope, "changed_paths", side_effect=error):
                self.assertFalse(scope.classify("pull_request", "a" * 40, "b" * 40))

    def test_pr_classification_includes_both_sides_of_a_rename(self):
        with patch.object(scope, "changed_paths", return_value=["source/Old.cs", "docs/proposals/new.md"]):
            self.assertFalse(scope.classify("pull_request", "a" * 40, "b" * 40))
        with patch.object(scope, "changed_paths", return_value=["docs/progress/a.md"]):
            self.assertTrue(scope.classify("pull_request", "a" * 40, "b" * 40))

    def test_git_command_disables_renames_and_keeps_nul_delimiters(self):
        with patch.object(scope.subprocess, "run", return_value=subprocess.CompletedProcess([], 0, b"docs/proposals/a b.md\0docs/progress/x.md\0")) as run:
            self.assertEqual(["docs/proposals/a b.md", "docs/progress/x.md"], scope.changed_paths("a" * 40, "b" * 40))
            self.assertEqual(["git", "diff", "--no-renames", "--name-only", "-z", "a" * 40 + "..." + "b" * 40, "--"], run.call_args.args[0])

    def test_invalid_commit_names_cannot_be_git_options(self):
        for name in ["main", "--output=/tmp/x", "a" * 39, "a" * 40 + "\n"]:
            with self.assertRaises(ValueError):
                scope.changed_paths(name, "b" * 40)

class GitIntegrationTests(unittest.TestCase):
    def test_actual_merge_base_renames_and_cli_output(self):
        import os
        from pathlib import Path
        import tempfile
        import sys
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            def git(*args):
                return subprocess.check_output(["git", "-C", directory, *args], text=True).strip()
            git("init", "-q")
            git("config", "user.name", "CI fixture")
            git("config", "user.email", "fixture@example.invalid")
            (root / "source").mkdir()
            (root / "source/Old.cs").write_text("source fixture\n")
            git("add", ".")
            git("commit", "-qm", "base")
            base = git("rev-parse", "HEAD")
            (root / "docs/proposals").mkdir(parents=True)
            (root / "docs/proposals/a b.md").write_text("proposal\n")
            git("add", ".")
            git("commit", "-qm", "proposal")
            proposal = git("rev-parse", "HEAD")
            # Diverged main source edits must not count as changes made by this PR.
            git("checkout", "-q", "-b", "other-main", base)
            (root / "source/Other.cs").write_text("main only\n")
            git("add", ".")
            git("commit", "-qm", "main advances")
            advanced_base = git("rev-parse", "HEAD")
            output = root / "outputs"
            env = dict(os.environ, GITHUB_EVENT_NAME="pull_request", PR_BASE_SHA=advanced_base,
                       PR_HEAD_SHA=proposal, GITHUB_OUTPUT=str(output))
            script = Path(scope.__file__).resolve()
            subprocess.run([sys.executable, str(script)], cwd=root, env=env, check=True, capture_output=True)
            self.assertEqual("docs_only=true\n", output.read_text())
            git("checkout", "-q", "--detach", proposal)
            git("mv", "source/Old.cs", "docs/proposals/renamed.md")
            git("commit", "-qm", "rename code")
            env["PR_HEAD_SHA"] = git("rev-parse", "HEAD")
            subprocess.run([sys.executable, str(script)], cwd=root, env=env, check=True, capture_output=True)
            self.assertEqual("docs_only=true\ndocs_only=false\n", output.read_text())


if __name__ == "__main__":
    unittest.main()

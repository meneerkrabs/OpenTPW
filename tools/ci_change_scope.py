"""Conservatively identify proposal/progress-only pull requests for lightweight CI."""
from __future__ import annotations

import argparse
import os
from pathlib import Path
import re
import subprocess


_ALLOWED = ("docs/proposals/", "docs/progress/")
_COMMIT = re.compile(r"(?:[0-9a-fA-F]{40}|[0-9a-fA-F]{64})\Z")


def proposal_docs_only(paths: list[str]) -> bool:
    """No empty/ambiguous changeset, renamed-away code or other path may skip full CI."""
    return bool(paths) and all(
        path.startswith(_ALLOWED)
        and path.endswith(".md")
        and "\\" not in path
        and all(part not in {"", ".", ".."} for part in path.split("/"))
        for path in paths
    )


def changed_paths(base: str, head: str) -> list[str]:
    if not _COMMIT.fullmatch(base) or not _COMMIT.fullmatch(head):
        raise ValueError("base/head must be complete commit hashes")
    # Triple-dot matches the PR changeset, not unrelated changes that landed on main.
    # Disabling rename detection includes BOTH the old and new path: renaming source
    # into a proposal must never hide its source deletion from the full-build gate.
    result = subprocess.run(
        ["git", "diff", "--no-renames", "--name-only", "-z", f"{base}...{head}", "--"],
        check=True, stdout=subprocess.PIPE, stderr=subprocess.PIPE,
    )
    return [part.decode("utf-8", errors="strict") for part in result.stdout.split(b"\0") if part]


def classify(event: str, base: str, head: str) -> bool:
    if event != "pull_request":
        return False
    try:
        return proposal_docs_only(changed_paths(base, head))
    except (OSError, ValueError, UnicodeError, subprocess.CalledProcessError):
        # Unknown classification is full CI, never a silent skip.
        return False


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--event", default=os.environ.get("GITHUB_EVENT_NAME", ""))
    parser.add_argument("--base", default=os.environ.get("PR_BASE_SHA", ""))
    parser.add_argument("--head", default=os.environ.get("PR_HEAD_SHA", ""))
    args = parser.parse_args()
    docs_only = classify(args.event, args.base, args.head)
    value = "true" if docs_only else "false"
    print(f"Proposal/progress-only pull request: {value}")
    if output := os.environ.get("GITHUB_OUTPUT"):
        with Path(output).open("a", encoding="utf-8") as stream:
            stream.write(f"docs_only={value}\n")


if __name__ == "__main__":
    main()

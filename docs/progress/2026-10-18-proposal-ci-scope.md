## Keep proposal-only CI lightweight (October 18, 2026)

Pull requests changing only Markdown under docs/proposals/ and docs/progress/ run lightweight register/progress checks instead of three platform builds and the server image. The existing Website job still checks generated documentation links. Any other path, unknown/empty changeset, code moved into docs, or non-PR event keeps full build/test coverage. Draft status is never used to skip checks. Added classifier regressions; no release, deployment, permission or security-analysis policy changed.

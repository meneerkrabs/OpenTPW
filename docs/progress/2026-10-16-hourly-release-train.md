## Hourly release train (October 16, 2026)

Build and release tooling, no game behaviour changes:

- **An hourly train** (`.github/workflows/release-train.yml`, `tools/release_train.py`) publishes a normal stable release when `main` has
  commits newer than the newest stable tag and the build workflow succeeded on that exact commit. Versions count up from the newest
  stable tag (`v0.8.6`, then `v0.8.7`, ...), and each release becomes the latest download like a manual one. A half-failed release
  (tag exists, release missing or still a draft) is retried under the same version.
- **Pinned commit:** Release builds gained optional `commit` and `notes` inputs, so the train's validated commit is what gets built and tagged
  even if `main` moves in between. Manual releases are unchanged.
- Documented in `docs/RELEASES.md`; the rules are unit tested in `tools/test_release_train.py` and the pinned-commit path in `tools/test_publish_release.py`.

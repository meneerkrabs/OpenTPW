## Hourly preview release train (October 16, 2026)

Build and release tooling, no game behaviour changes:

- **An hourly train** (`.github/workflows/release-train.yml`, `tools/release_train.py`) publishes a preview release when `main` has
  commits newer than the newest `v*` tag and the build workflow succeeded on that exact commit. Previews are `vX.Y.(Z+1)-preview.N`,
  never marked latest, never deployed; stable releases stay manual.
- **Pinned commit:** Release builds gained optional `commit` and `notes` inputs, so the train's validated commit is what gets built and tagged
  even if `main` moves in between. Manual releases are unchanged.
- Documented in `docs/RELEASES.md`; the rules are unit tested in `tools/test_release_train.py` and the pinned-commit path in `tools/test_publish_release.py`.

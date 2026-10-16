## Conflict-free fidelity register and progress log (October 16, 2026)

Parallel pull requests no longer conflict over two shared documents:

- **The fidelity register** is generated, not committed. `python3 tools/fidelity_register.py --write` writes
  `docs/FIDELITY-REGISTER.md` locally (it is ignored by git), and the website workflow generates and publishes it.
  `--check` validates the sources only: annotations against declarations, ID syntax, scopes and the retired-ID
  guard. The retired and reserved IDs, until now kept in the committed document, are in `tools/fidelity_retired.txt`;
  `--write` records new ones and the check fails until they are committed.
- **The progress log** takes one file per pull request in `docs/progress/` (this entry is the first).
  `python3 tools/progress.py` assembles the fragments, newest first, with the frozen history in
  [PROGRESS.md](../PROGRESS.md); `--check` validates fragment names and headings in CI, and the website's progress
  page shows the assembled log.

The ppc-analysis areas report and the DET-V2 and WALK-V2 review lanes render the register from the sources instead of
reading the committed copy.

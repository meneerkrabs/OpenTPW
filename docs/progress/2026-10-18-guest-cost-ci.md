## The smarter-guests CPU gate is measured on a hosted runner (October 18, 2026)

The release of `opentpw.smarter-guests` waits for its CPU gate (at most 1.10 times the original guests' update time per
guest-turn, docs/SMARTER-GUESTS.md "Cost and release") to be confirmed on a machine nobody else is using, and the
development machine never became quiet. A new workflow, `Smarter guests CPU cost` (`.github/workflows/guest-cost.yml`),
runs `--guest-report --guest-cost` on a hosted Ubuntu and macOS runner in four synthetic scenarios (default, food shop,
litter with bins, litter with bins and food) and writes the tables to the job summary. It needs no game data, runs by hand
(`workflow_dispatch`, repeats and variants as inputs) or on a pull request that changes the workflow, and is not part of the
normal build. Nothing in the game changes.

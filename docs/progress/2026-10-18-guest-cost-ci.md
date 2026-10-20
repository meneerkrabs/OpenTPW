## Smarter guests is released as opt-in after a CPU measurement on hosted runners (October 18, 2026)

The release of `opentpw.smarter-guests` waits for its CPU gate (at most 1.10 times the original guests' update time per
guest-turn, docs/SMARTER-GUESTS.md "Cost and release") to be confirmed on a machine nobody else is using, and the
development machine never became quiet. A new workflow, `Smarter guests CPU cost` (`.github/workflows/guest-cost.yml`),
runs `--guest-report --guest-cost` on a hosted Ubuntu and macOS runner in four synthetic scenarios (default, food shop,
litter with bins, litter with bins and food) and writes the tables to the job summary. It needs no game data, runs by hand
(`workflow_dispatch`, repeats and variants as inputs) or on a pull request that changes the workflow, and is not part of the
normal build. Nothing in the game changes.

**Result, and the release.** On the Ubuntu runner 1.6.0 costs 1.072, 1.091, 1.075 and 1.081 times the original guests per
guest-turn in the four scenarios (1.5.0 within 0.005 of it), so the gate is met and `opentpw.smarter-guests` 1.6.0 is
released as opt-in: `PlayerSelectable: true`, still off by default, on the Extensions and mods screen, in `mods.json` and
with `--enable-mod`. The macOS runner was too noisy to decide on (identical code paths differed by up to 0.05); its figures
are in docs/SMARTER-GUESTS.md. README, docs/MODS.md and the mods-screen tests follow.

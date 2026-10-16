## Economy and saves sweep: costs, the bank batch, challenges and shop costs from tp.exe (October 16, 2026)

A sweep over the economy and save approximations, read in tp.exe via REA:

- **Negative costs are refused** (ECON-048 retired): the `Costs.*` keys are type-5 balance keys whose parser (`0x401d7c`: `test ebp,ebp;
  jl 0x40260d`) reports "Balance problem: invalid -ve value specified"; OpenTPW now refuses them instead of booking them as income.
- **The bank's batch credit** (SAVE-008 retired): the month routine `0x4d0370` adds the batch word to the balance, the money-in total and
  the year profit and zeroes it; only the constructor, message 10 and the month routine write it. It is now a pending credit, imported
  from original saves, paid at the next month end and kept in the park save as an optional `BatchCredit`.
- **Challenges** (ECON-034 and ECON-035 retired, SAVE-005 narrowed): `0x4e16b0` maps shop type and special ingredient to challenge
  types through two jump tables (`0x4e18e4`, `0x4e18fc`); types 24, 25 and 27 accumulate a daily value (toilet cleanliness, all-staff
  happiness, the challenge's staff type) compared over `TargetTime` with `TargetVal` (`0x4d2860`, `0x4d1660`), so challenge 10 can now be
  won; message kind `0xb` is the calendar day (`0x4f8260`). A decline never forfeits (`0x4d2ec0` does not write the declined count), and
  the first offer waits for an open park (`0x4c9050`, `0x51a280`).
- **The shop's cost per sale** (ECON-056 retired): `0x4e1b40` books trunc(cost × (1 + q + ±i)), each term (byte − 50) × 0.005 clamped to
  ±0.5, the ingredient sign flipped for special ingredients 1 and 3; the same value goes to the bank, the cost rings and the type 12/13
  accumulators (`0x4e1920`), and a sideshow books the plain cost (`0x4e1a10`).
- **The big park ticket** (ECON-038 narrowed) counts more than `MinCellsCovered` cells of type 4, 9, 10 or 21 or under a counted
  structure, owned or not (`0x4d4e50`, `0x4cd7f0`); ECON-017 drops a stale claim about group 0.

- **The clock** (ECON-001 split): the deterministic fixed 60 Hz clock sampled into 248 ms park turns is an OpenTPW design choice (`EXT:fixed-60hz-clock`);
  ECON-001 keeps only the original's 31 ms substeps with their backlog catch-up under load.

The shop's arithmetic rounds to a single after every x87 step (ECON-059: the precision DDSCL_FPUSETUP gives, the simulation thread's own control
word is not read); the pinned run hash is unchanged (default shop quality and ingredient give the plain cost in every precision). Two optional save fields (`BatchCredit`,
`Objectives.SwitchedOn`, inferred for older saves) keep older saves loading. Verified with 4593 tests passing without game data, 5041 with
it, and all five native smokes.

Still open: the challenge check's per-update gate (`0x4d1e90`, `(turn & 3) == 0`, now ECON-058), the carried object `mActualThing` (`+0x3e0`) that
filters every accumulated event, the attraction history rings (layout read: `0x4e1c70` and its readers), the fence structure records,
the TAG_SYSTEM 123–127 producer, removal cell writes and the larger save blocks (SAVE-004, 006, 007, 009–011, 920–923).

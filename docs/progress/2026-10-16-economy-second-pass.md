## Economy second pass: daily rings, the carried challenge object and the per-turn challenge check from tp.exe (October 16, 2026)

A second pass over the economy approximations, read in tp.exe via REA:

- **Retired:** ECON-038 (the camera golden ticket needs exactly 100 % security coverage, `0x4D4A00` with `0x4CD720`; the all-land ticket is
  never awarded) and ECON-052 (staff state 6 is written only by the save loader; all 20 SetState call sites enumerated).
- **Daily rings** (ECON-057): the six 30-entry daily rings per attraction (costs, takings, customers, walk-aways, winners, happiness
  change) are built as the history constructor `0x4DB090`, the day roll `0x4DD320` and the count, read and sum routines `0x495D40`,
  `0x495CF0` and `0x495C20` keep them; the object windows and the attractions list show their 30-day sums, and they are saved and
  imported from original saves.
- **The carried challenge object** (ECON-055, SAVE-005): `mActualThing` (`+0x3E0`) filters every accumulated event (`0x4D27A0`);
  challenge types 14 and 18 carry the best coaster (`0x4CC810`, newest first, strictly greater).
- **The challenge check** (ECON-058) runs every park turn (`0x4D1E90`), checking a running challenge when `(turn & 3) != 0`, with
  offers and deadlines in exact seconds.
- **Research order** (ECON-017): the table follows the original scan order (Ride, Feature, Upgrade, Shop, Sideshow), checked against
  the shipped Jungle lab, with the dynamic category-4 table and the golden-ticket add-on (`0x5AF780`, `0x4D4B70`).
- **Also:** hoardings count in net worth and the big park count (ECON-026); the analyser's saved state (`0x4C5D70` → `0x4C85B0`,
  ECON-020), the object destructor (ECON-050), the guest event ring (debug only, ECON-044) and the renderer's precision-control writes
  (ECON-059) are read.

The pinned run hash moves because the new saved rings are part of the economy digest (excluding them reproduces the previous pin; no
behaviour changed); docs/DETERMINISM.md records it. The new save fields are optional and validated, and older saves seed the first rings
from last month's totals. Verified with 4682 tests passing without game data, 5133 with it, and all five native smokes.

Still open: the happiness-change ring's writer, the kart best object (`0x4CC4B0`), the challenge progress message and warning event, the
loss counts and offer day, the hoarding footprint, the world-update cadence against `0x877D34` (ECON-014) and the TAG_SYSTEM 123–127
producer (ECON-030).

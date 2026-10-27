# What an original save's things import (inventory)

An original `TPWS`/`TPWI` save lists every thing of the park as one chain of used thing slots, each with its model number (docs/TPWS-PAYLOAD.md, "Thing list"). OpenTPW rebuilds part of them
(`OriginalParkImport`, `OriginalEconomyImport`, `GuestSimulation.ImportOriginal`) and keeps the thing pool's slots as the original's load does (docs/GUESTS.md, "Thing ids"): a saved thing the
import does not rebuild holds a **placeholder slot** (`ThingPool.Hold`, `OriginalThingSlots.HoldUnbuilt`), so the next thing built gets the id the original hands out. A placeholder is not simulated and is never
freed (`WALK-010` keeps that difference). OpenTPW's own park save keeps the placeholders (`[EXT:resume-exact-thing-pool]`).

This page lists, per class, what the import skips or only partly rebuilds, with the original constructor (all twelve reach the thing pool through the base constructor `0x50AFE0`; the person
base is `0x4F8940`, the guest constructor `0x4FAEC0`, the staff constructors `0x504A90` and `0x504B10`) and the open `APPROX` ID that covers each gap. Model 2 has no serializer and model 18 (the online
persons) is refused by the saver, so neither appears in a save.

## Saves counted

Twelve original saves were walked: the shipped `Data/levels/jungle/Easymode.TPWI` and the eleven private saves of the second fixture set (docs/TPWS-PAYLOAD.md; never committed, read through `OPENTPW_TPWS_SAVES`).
The Theme Park Inc `prebuilt.TPWS` does not decode its World state (`ImportThemeParkInc`).

- **Easymode** (42 things, ids 1 to 42, none free, so the original's load hands out id 43 next; `OriginalThingSlotsTests` pins this). The counts in the first table below are Easymode's.
- **The eleven private saves** (seven `.TPWS`, four `restart.INTS`; per-save table below). Every park is empty (no placed objects, no staff, no coaster), so they add only the five fixed items, the guests the
  cheat parks keep and, in one theme, a guest in a state the import drops. `OriginalSaveCorpusTests` (`OPENTPW_TPWS_SAVES`) reads them; the four `.INTS` pass it too (see "The restart saves").

On Easymode every saved guest, object and staff member is rebuilt, so no placeholder is held there; a placeholder appears for a guest in a state the import drops (SAVE-002), an object that does not resolve
and a staff member nobody employs, which `OriginalThingSlotsTests` exercises on the Easymode chain. Of the eleven private saves only `fantasy/Wonder Land.TPWS` and `fantasy/autosave.TPWS` hold one (guest 39, state 20).

`OriginalGuestPlaceholderImportTests` covers the rejection boundary without reading those saves: synthetic states 11–17 and 20 (`WalkingToVehicle`) keep guest placeholders, preserve slot models and saved used-list order,
and retain their held IDs and free-list allocation order through pool snapshot restoration. State 20 remains unsupported by `GuestSimulation.ImportsOriginalState`; these are loss-containment regressions, not restored vehicle walks.
Restoring that state still requires the arrival-vehicle identity and script-continuation integration, with a validated saved destination and continuation contract. `ImportOriginalArrivals` rejects cached/current vehicle handles;
simply adding 20 to the accepted states would not satisfy this gate. SAVE-002 and proposal #536 remain open; no new private-save inspection or original-runtime parity is claimed by these synthetic tests.

## Inventory (Easymode)

| Model | tp.exe constructor | Things (count) | Info.Id | Rebuilt | Skipped or partly rebuilt | Covered by |
| --- | --- | --- | --- | --- | --- | --- |
| 1 Guest | `0x4FAEC0` (person base `0x4F8940`) | 13: ids 29, 31 to 42; 5 in state 2, 1 in state 3, 7 in state 5 | none | 13 of 13 (guest 33, paid and waiting at the gate, enters the park at once) | arrival index, admission flags, booth wait (+0x200), queue links, saved state, event ring; a guest in states 11 to 17 or 20 would be dropped, one off the paths is kept where it stands (none here) | SAVE-002, GUEST-011 |
| 4 Mechanic, 5 Handyman, 6 Entertainer, 7 Guard, 8 Researcher | `0x504A90`, `0x504B10` (person base `0x4F8940`) | 5: ids 26, 25, 27, 28, 30, one per class, all patrolling | none | 5 of 5 with their jobs (state 1, patrol) | a member's sprite variant (not saved, GUEST-014), a guard escort is imported idle (none here), a litter job is imported with the litter it targets; `mJobsDone` has no reader | SAVE-002, GUEST-014 |
| 3 Attraction | `0x4DB090` | 14: ids 11 to 24; Gates 1601 (11), Lights 1603 (12), Belly Bounce 1100 (13), Jungle Spray 1303 (14), Bus 1600 (15), Drinks Shop 1203 (16), Litter Bin 1406 (17), Security Camera 1413 x2 (18, 19), Staff Room 1411 (20), Small Toilet 1402 x3 (21, 22, 23), Round Fountain 1403 (24) | as listed | 14 of 14 objects with price, goods, upgrade level, repair, life, quality, chance, state, rings and walk-aways | claims with unresolved owners, unsupported staff roles or future stamps, queue ends and called guest (rebuilt from the laid queue), mesh instance, flags; known mechanic/handyman claims retain their saved turn until the existing lazy expiry, including an idle owner; the open word of an idle attraction and the gate's sign lines (the park's name in a player's own save) are imported | SAVE-006, ECON-022 |
| 3 Attraction (coaster) | `0x4DB090`, loader `0x437B60` | 0 (the Coasters block is empty) | none | not rebuilt in any save: `ParkObjects.ImportOriginal` refuses a park with a coaster | the coaster's controller, nodes, collision lists, trains and cars | SAVE-920 to SAVE-923 |
| 9 StaffHq | `0x5089C0` | 1: id 1 | none | training budgets, strike levels, activity, the staff pool (16 candidates) | the picked-up staff id (+0x60: read to the end, the original keeps a member saved in state 7 there for good and OpenTPW puts him back to idle, `[EXT:staff-pickup-reconcile]`), force strike and trained byte (never read); the pool's sub-types are imported as the candidates' sprite variants and a candidate saved on the pointer is imported in the pool | SAVE-004 |
| 10 MechanicHq | `0x4DA920` | 1: id 2 (10 bytes) | none | nothing | the whole thing | SAVE-010 (bundled, needs its own ID) |
| 11 AdvisorController | `0x599E70` | 1: id 3 (5838 bytes) | none | pending advice, history and the last tutorial advice, only when the automatic advisor attaches | time signature, sound, raw words, `mRequestedLastTutorial`, `mMessagePlaying`; everything when no advisor attaches | SAVE-010 |
| 12 TagSystem | `0x509B40` | 1: id 4 (8 bytes) | none | nothing | the whole thing | SAVE-010 (bundled, needs its own ID) |
| 13 ParkAnalyser | `0x4C58C0` | 1: id 5 (73,536 bytes, width closure-derived) | none | the four recent-research rings, the five monthly statistics rings with live arrivals and the recent stays; for the automatic advisor the arrival ring and the stamps of the newest construction and hire (`mLastObjectBuilt`, `mLastStaffHired`) | the grid cells, the trackers and the other rings, the counters, the other four stamps and the three extremes | SAVE-010 (bundled, needs its own ID) |
| 14 ResearchLab | `0x502D20` | 1: id 6 | none | 24 projects, groups, current projects (nothing dropped on Easymode) | a project of an object the archives lack or a current project the picker would not offer (none in the corpus); the highest open groups are imported as saved and the table order is the original's scan (ECON-017) | SAVE-007 |
| 15 Weather | `0x511F70` | 1: id 7 | none | nothing | the whole thing (OpenTPW draws weather from its own stream, `[EXT:weather-stream]`) | SAVE-010 (bundled, needs its own ID) |
| 16 Bank | `0x4CF7C0` | 1: id 8 | none | balance, fee, profit, loans, words | none reported | none open |
| 17 UIMsgReceiver | `0x50C9B0` | 1: id 9 (8 bytes) | none | nothing | the whole thing | SAVE-010 (bundled, needs its own ID) |
| 19 ChallengeSystem | `0x4D1BD0` | 1: id 10 | none | the 8 saved challenges (they equal the level's list) | loss counts and offered words are read and not kept (no reader); a save whose slots differ from the level's list plays the saved definitions (adopted; an optional field in OpenTPW's own save) | none (SAVE-005 retired) |

Things that are not saved things (the engine dumps of the Scripts, Particles, MessageCentre, TrackRides, FlyingRides, Rsse and Sound blocks, `[EXT:original-engine-state-dumps]`; the Clock, Camera,
GameSystem and other runtime blocks, `SAVE-010`) are outside this inventory.

**Design choice, `[EXT:staff-pickup-reconcile]`** (retired `APPROX:SAVE-009`, user-approved 2026-10-17): a staff member saved in state 7 (picked up) with the staff HQ's picked-up id (+0x60) naming him is imported idle (`StaffPlacement.Reconcile`). The original keeps him in state 7 for good: the id's only reader is the drop `0x509AF0` from the carry tool's exit `0x46CC40`, no carry tool exists after a load (loader `0x508BB0`) and the pick-up guard `0x505BD0` refuses state 7. The force strike (+0x64) and the trained byte need no import: they are never read in play.

## Inventory (the eleven private saves)

Counted by reading the thing chain (`SaveWorld.Things`), the attraction and guest records and the Coasters block, and by running `GuestSimulation.ImportOriginal` over the saved guests; whether OpenTPW rebuilds an
attraction is judged from the object catalog (every fixed item below resolves to a non-tool catalog entry, so `ParkObjects.ImportOriginal` places it and keeps its thing id). A named save and its autosave hold the same things.
Every save also holds the ten world singletons (ids 1 to 10: StaffHq 1, MechanicHq 2, AdvisorController 3, TagSystem 4, ParkAnalyser 5, ResearchLab 6, Weather 7, Bank 8, UIMsgReceiver 9, ChallengeSystem 10) with the
same gaps as Easymode's table above, and no staff member (models 4 to 8: none in any of the eleven).

| Save (theme folder) | Attraction things (id: Info.Id) | Guest things (id, person type, state) | Not rebuilt by the import | Next ids after the import |
| --- | --- | --- | --- | --- |
| `fantasy/Wonder Land.TPWS`, `fantasy/autosave.TPWS` | 11: 4601 Gates, 12: 4603 Lights, 17: 4600 Bus, 19: 4604 Ferry, 22: 4602 Plane | 39 (type 5, state 20) | guest 39 (state 20 is not one the import takes: SAVE-002), kept as a placeholder | 13, 14, 15 |
| `hallow/Halloween World.TPWS`, `hallow/autosave.TPWS` | 11: 2601 Gates, 12: 2603 Lights, 20: 2600 Bus, 23: 2604 Ferry, 26: 2602 Plane | 25 (3, state 3), 28 (3, 3), 30 (4, 3), 31 (0, 3) | none; the four guests are imported leaving (state 18, the park is closed) | 13, 14, 15 |
| `jungle/Lost Kingdom.TPWS`, `jungle/autosave.TPWS` | 11: 1601 Gates, 12: 1603 Lights, 24: 1600 Bus, 28: 1602 Seaplane, 29: 1604 Ferry | 27 (0, state 3), 37 (1, 3), 38 (5, 3), 39 (5, 3) | none; the four guests are imported leaving | 13, 14, 15 |
| `space/Space Zone.TPWS` (no autosave) | 11: 3601 Gates, 12: 3603 Lights, 20: 3600 Bus, 24: 3602 Plane, 26: 3604 Ferry | 21 (1, state 3), 27 (6, 3), 34 (6, 3), 36 (2, 3) | none; the four guests are imported leaving | 13, 14, 15 |
| `fantasy`, `hallow`, `jungle`, `space` `restart.INTS` (four saves) | 11: Gates (`*601`), 12: Lights (`*603`), with `*` = 4 fantasy, 2 hallow, 1 jungle, 3 space | none | the chain cannot be imported yet (see below); by its things nothing would be dropped | 13, 14, 15 |

Per class over the seven TPWS saves: Attraction (model 3) 5 each (35 things, all fixed items: no placed object, no Info.Id outside `*600` to `*604` of the save's theme); Guest (model 1) 1 in the fantasy saves and 4 in
the others (22 things over the seven saves; person types 0 to 6, states 3 and 20); Staff (models 4 to 8) none; no coaster, no track ride; the ten singletons as listed.
Two Info.Ids are new against Easymode: the Seaplane (`*602`, Plane in the fantasy, hallow and space catalogs) and the Ferry (`*604`); the importer keeps their thing ids like the Bus's. Not run: a full `Level` load of
any of these saves (the smokes load only Easymode), so "rebuilt" for the fixed items rests on the catalog lookup and the guest simulation, not on a finished level.

### Coasters

None of the eleven saves holds a coaster: every Coasters block is the empty boundary (count 0, globals 0, 0, 1, 16 bytes), so no train and no car, and none of them has a track ride or flying ride (the TrackRides block is the 44 bytes and the FlyingRides block the 452 bytes of Easymode's empty ones in all eleven). The coaster saves SAVE-920 to SAVE-923 wait for are therefore still missing: a save made after building a coaster (stations, track and train) is needed.

### The restart saves

The four `restart.INTS` files are initial saves: no guest, no staff, two attraction things (Gates 11, Lights 12) and the ten singletons, so the next id is 13. They read through `OriginalParkImport.Import` since the importer accepts the two initial states the original builds: the macro-AI calendar of the constructor `0x4F7E80` (start 2000-01-01, month cache 0, day cache -1,
rate 15000, `SaveCalendar.IsInitial`) and the empty history ring of `0x51ADE0` (current entry 0xFFFFFFFF, 30 entries, no wrap, `SaveAttraction.EmptyRingHead`). The loaders `0x4F7F30` and `0x4E2C10` take both unchecked, so
nothing looser is accepted than what the constructors write. Because no date equals day -1, the original's first update after loading one posts a day message (`0x4F8260`), as a new park's first update does (the world init `0x5156A0` syncs only the month and year caches, `0x4F85A0`); OpenTPW preserves that pending first-day dispatch for supported TPW initial calendars at turn 0, including an own save made before the first actual turn. A new park starts with the same pending first day (ECON-063, retired), and restart snapshots retain their captured state. The original's restart reloads the file: the Mac writes it after a park load (state 15) and reloads it in state 14, and tp.exe's park state machine `0x54E360` does the same
(state 15 writes it through `0x550B30`, a restart request takes state 10 to 13 and 14, which loads it through `0x5AC5F0` and `0x414D40`). OpenTPW's Restart Park reloads its own snapshot of the same moment, in the park-resume format (`SAVE-012`). That all four shipped files are unplayed initial states fits the original's writer, which saves only when the file cannot be opened (`0x550C14`) and so keeps the first snapshot of a player's theme folder (OpenTPW retakes it per park, `[EXT:restart-snapshot-per-start]`).

## IDs that are still needed

`SAVE-010` bundles unrelated things: the saved state of the MechanicHq, TagSystem, ParkAnalyser, Weather and UIMsgReceiver things (five classes with no restore at all), the advisor controller's own words, the
world variables, the arrival bytes and the runtime blocks. Each of the five classes needs an ID of its own once OpenTPW has a counterpart to restore into (the park analyser's 73,536-byte state has none
recorded yet; the weather has `[EXT:weather-stream]`). No ID is allocated here. Every other gap above is covered by an open ID. The placeholders add one new difference, a placeholder never runs on and is
never destroyed, and `WALK-010` carries it.

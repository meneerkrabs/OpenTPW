# What an original save's things import (inventory)

An original `TPWS`/`TPWI` save lists every thing of the park as one chain of used thing slots, each with its model number (docs/TPWS-PAYLOAD.md, "Thing list"). OpenTPW rebuilds part of them
(`OriginalParkImport`, `OriginalEconomyImport`, `GuestSimulation.ImportOriginal`) and keeps the thing pool's slots as the original's load does (docs/GUESTS.md, "Thing ids"): a saved thing the
import does not rebuild holds a **placeholder slot** (`ThingPool.Hold`, `OriginalThingSlots.HoldUnbuilt`), so the next thing built gets the id the original hands out. A placeholder is not simulated and is never
freed (`WALK-010` keeps that difference). OpenTPW's own park save keeps the placeholders (`[EXT:resume-exact-thing-pool]`).

This page lists, per class, what the import skips or only partly rebuilds, with the original constructor (all twelve reach the thing pool through the base constructor `0x50AFE0`; the person
base is `0x4F8940`, the guest constructor `0x4FAEC0`, the staff constructors `0x504A90` and `0x504B10`) and the open `APPROX` ID that covers each gap. Model 2 has no serializer and model 18 (the online
persons) is refused by the saver, so neither appears in a save.

## Saves counted

Only one original save ships with the game: `Data/levels/jungle/Easymode.TPWI` (42 things, ids 1 to 42, none free, so the original's load hands out id 43 next; `OriginalThingSlotsTests` pins this). The
Theme Park Inc `prebuilt.TPWS` does not decode its World state (`ImportThemeParkInc`). The eleven private saves of the second fixture set (docs/TPWS-PAYLOAD.md) were not available to this inventory (their
thing chain was never walked); by that page they hold five attraction records each (Bus, Seaplane, Ferry, Traffic Lights, the `*601` park object, all fixed items) and, in the TPWS, the same singleton classes.
`OriginalSaveCorpusTests` (`OPENTPW_TPWS_SAVES`) covers them when the folder is set. The counts below are Easymode's, from the economy and guest import evidence.

On Easymode every saved guest, object and staff member is rebuilt, so no placeholder is held there; a placeholder appears for a guest in a state the import drops (SAVE-002), an object that does not resolve
and a staff member nobody employs, which `OriginalThingSlotsTests` exercises on the Easymode chain.

## Inventory (Easymode)

| Model | tp.exe constructor | Things (count) | Info.Id | Rebuilt | Skipped or partly rebuilt | Covered by |
| --- | --- | --- | --- | --- | --- | --- |
| 1 Guest | `0x4FAEC0` (person base `0x4F8940`) | 13: ids 29, 31 to 42; 5 in state 2, 1 in state 3, 7 in state 5 | none | 13 of 13 (guest 33, paid and waiting at the gate, enters the park at once) | arrival index, admission flags, booth wait (+0x200), queue links, saved state, event ring; a guest in states 11 to 17 or off the paths would be dropped (none here) | SAVE-002, GUEST-011 |
| 4 Mechanic, 5 Handyman, 6 Entertainer, 7 Guard, 8 Researcher | `0x504A90`, `0x504B10` (person base `0x4F8940`) | 5: ids 26, 25, 27, 28, 30, one per class, all patrolling | none | 5 of 5 with their jobs (state 1, patrol) | sprite variant (GUEST-014), a litter job or a guard escort is imported idle (none here); `mJobsDone` has no reader | SAVE-004, SAVE-002, SAVE-010, GUEST-014 |
| 3 Attraction | `0x4DB090` | 14: ids 11 to 24; Gates 1601 (11), Lights 1603 (12), Belly Bounce 1100 (13), Jungle Spray 1303 (14), Bus 1600 (15), Drinks Shop 1203 (16), Litter Bin 1406 (17), Security Camera 1413 x2 (18, 19), Staff Room 1411 (20), Small Toilet 1402 x3 (21, 22, 23), Round Fountain 1403 (24) | as listed | 14 of 14 objects with price, goods, upgrade level, repair, life, quality, chance, state, rings and walk-aways | open word, sign lines, staff claim, queue ends and called guest (rebuilt from the laid queue), mesh instance, flags | SAVE-006, ECON-022 |
| 3 Attraction (coaster) | `0x4DB090`, loader `0x437B60` | 0 (the Coasters block is empty) | none | not rebuilt in any save: `ParkObjects.ImportOriginal` refuses a park with a coaster | the coaster's controller, nodes, links, trains and cars | SAVE-920 to SAVE-923 |
| 9 StaffHq | `0x5089C0` | 1: id 1 | none | training budgets, strike levels, activity, the staff pool (16 candidates) | picked-up staff id (+0x60), force strike, trained byte, pool sub-types and on-pointer | SAVE-009, SAVE-004 |
| 10 MechanicHq | `0x4DA920` | 1: id 2 (10 bytes) | none | nothing | the whole thing | SAVE-010 (bundled, needs its own ID) |
| 11 AdvisorController | `0x599E70` | 1: id 3 (5838 bytes) | none | pending advice, history and the last tutorial advice, only when the automatic advisor attaches | time signature, sound, raw words, `mRequestedLastTutorial`, `mMessagePlaying`; everything when no advisor attaches | SAVE-010 |
| 12 TagSystem | `0x509B40` | 1: id 4 (8 bytes) | none | nothing | the whole thing | SAVE-010 (bundled, needs its own ID) |
| 13 ParkAnalyser | `0x4C58C0` | 1: id 5 (73,536 bytes, width closure-derived) | none | nothing | the whole thing | SAVE-010 (bundled, needs its own ID) |
| 14 ResearchLab | `0x502D20` | 1: id 6 | none | 24 projects, groups, current projects (nothing dropped on Easymode) | the lab's table order | SAVE-007, ECON-017 |
| 15 Weather | `0x511F70` | 1: id 7 | none | nothing | the whole thing (OpenTPW draws weather from its own stream, `[EXT:weather-stream]`) | SAVE-010 (bundled, needs its own ID) |
| 16 Bank | `0x4CF7C0` | 1: id 8 | none | balance, fee, profit, loans, words | none reported | none open |
| 17 UIMsgReceiver | `0x50C9B0` | 1: id 9 (8 bytes) | none | nothing | the whole thing | SAVE-010 (bundled, needs its own ID) |
| 19 ChallengeSystem | `0x4D1BD0` | 1: id 10 | none | the 8 saved challenges (they equal the level's list) | loss counts and offered words are read and not kept (no reader); a save whose slots differ from the level's list keeps the new park's state | SAVE-005 |

Things that are not saved things (the engine dumps of the Scripts, Particles, MessageCentre, TrackRides, FlyingRides, Rsse and Sound blocks, `[EXT:original-engine-state-dumps]`; the Clock, Camera,
GameSystem and other runtime blocks, `SAVE-010`) are outside this inventory.

## IDs that are still needed

`SAVE-010` bundles unrelated things: the saved state of the MechanicHq, TagSystem, ParkAnalyser, Weather and UIMsgReceiver things (five classes with no restore at all), the advisor controller's own words, the
world variables, the arrival bytes and the runtime blocks. Each of the five classes needs an ID of its own once OpenTPW has a counterpart to restore into (the park analyser's 73,536-byte state has none
recorded yet; the weather has `[EXT:weather-stream]`). No ID is allocated here. Every other gap above is covered by an open ID. The placeholders add one new difference, a placeholder never runs on and is
never destroyed, and `WALK-010` carries it.

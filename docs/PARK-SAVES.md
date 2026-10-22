# Saving and resuming a park

## Experimental Theme Park Inc layout checkpoints

The TPI experimental HUD's pause menu has **Save layout checkpoint** and **Load layout — new session**.
These use a separate application-owned format (`opentpw-tpi-layout`, version 1, `.tpilayout`), one slot
per theme in the existing save folder: `opentpw-science.tpilayout`, for example. They do not use the
TPW world-save picker or write native TPWS/TPWI/GMS/GMI/COS files. Native TPI saves, campaign,
economy, staff and sharing remain unsupported.

A checkpoint records every typed cell and placed catalog object, its anchor/rotation/open flag,
creation seed and attraction ID, the ID/seed allocators and world seed. Load explicitly confirms
replacing the current world and starting a **new experimental session**: guests, queues' live
membership, script instruction/variable/animation progress, fixed-step clock and audio restart.
The saved queue geometry remains. Camera, speed and menu focus start fresh. This is layout
persistence, never continuation or original TPI parity (an OpenTPW extension, `EXT:tpi-layout-checkpoint`). Prototype and coaster layouts
are refused instead of losing their state.

The format uses the existing trusted simulation-asset manifest without constructing TPW economy
rules. Manifest identifiers from the file are compared with hashes captured from the selected
catalog and terrain; they are never opened as paths. Changed/missing assets, wrong theme/dimensions,
invalid objects/cells/IDs, unknown or duplicate JSON fields, unsupported versions/policies and native
suffixes are refused. The strict file has the world-save size limit (64 MiB), contains metadata and
layout records only, and stores no original asset bytes. File writes use a flushed unique sibling
temporary file and move, preserving the prior checkpoint on ordinary validation/write/move failure.

Restoration reuses `ParkLayoutSnapshot.RestoreObjects` and `RestoreCellMap` before guests are wired,
and skips shipped/default placement so fixed items are not duplicated. The candidate and its HUD
are built under `EntityConstructionScope` before retiring the current world. Preflight or candidate
construction failure preserves the live world; retirement callbacks throwing after replacement
begins are outside this rollback guarantee. Successful load stops the retired HUD/frame immediately;
later drawing uses the current HUD. Synthetic tests reconstruct actual CPU object/runtime/cell state
with a valid dummy-only model, and check staged rollback/commit and callback lifetime. They do not
qualify original data, GPU/browser persistence, crash recovery, disk-full or native save behavior.

The remainder of this document describes Theme Park World continuation saves.

OpenTPW saves parks in its **own world-save format** (`"Format": "opentpw-world"`, version 1): one
file holds the whole running park, and Load resumes it where it left off. This is separate from the
original game's save format — original `TPWS`/`TPWI` files stay read-only imports
([TPWS-PAYLOAD.md](TPWS-PAYLOAD.md)): nothing in the running game writes them (a library writer exists, `OriginalParkExport`, and no menu or command reaches it), and an own-format save is not an
original-format save. What saves today is the fixed-ride Theme Park World park: paths, queues,
placed objects with their scripts and animations, guests, staff, money and world state. The limits
are listed below.

## Saving

- **Pause the game (Esc) and choose Save.** The park is captured between simulation ticks; the game
  reports the save or the exact reason it failed, and play continues either way.
- **One slot per theme**: the save is `save/opentpw-<theme>-world.json` — for example
  `opentpw-jungle-world.json`. Saving again overwrites that theme's slot; each theme keeps its own
  park. The write is atomic (a complete temporary file is flushed and moved into place), so a failed
  write leaves the previous save untouched.
- **Where the folder is:** on the desktop, `save` beside the game's `Data` folder. When that folder
  is read-only (a mounted CD, for instance), saves go to `save` under the settings folder
  (`~/Library/Application Support/OpenTPW` on macOS, `~/.config/OpenTPW` on Linux,
  `%APPDATA%\OpenTPW` on Windows; [RUNNING.md](RUNNING.md)) and the log says so.
- **In the browser** ([play.opentpw.io](https://play.opentpw.io/)) the same save is kept in the
  browser's storage (IndexedDB) and survives page and runtime reloads. Your original game files are
  not kept and nothing is uploaded: the next visit asks for the Theme Park World folder again
  ([WEB.md](WEB.md)), and the saved park is listed once you choose it.

### Atomic I/O audit (2026-10-13)

`ParkResumeSaveFile.Save` validates the snapshot, serializes the full JSON and
checks its size before opening a file. It creates a unique sibling temporary
file with `CreateNew`, records ownership only after opening succeeds, writes and
flushes it to disk, closes it, and moves it over the destination. Failure cleanup
deletes that exact owned temporary path; it does not search for other `.tmp` files.

`ParkResumeAtomicIoTests` uses synthetic snapshots and local temporary folders to
check validation refusal, a regular file obstructing the parent path, a late move
failure over an existing directory, a nonwritable Unix directory, repeated whole
snapshot overwrites and preservation of foreign temporary files. These paths pass
without production changes. The Windows case for a destination held without delete
sharing is platform-specific and skipped on macOS; permission tests also skip when
an elevated identity bypasses the tested directory permissions.

The audit covers ordinary local I/O failures, not abrupt power loss, disk-full or
flush-failure injection, a concurrent directory/permission change that prevents
cleanup, or crash recovery of interrupted temporary files. No original data or
original-format save behavior is involved.

## Periodic autosave

`[EXT:periodic-autosave]` (user-approved 2026-10-18). A browser player lost a park to a crash because the only automatic save was the checkpoint of Exit To Lobby
([AUTOMATIC-CHECKPOINT-INGRESS.md](AUTOMATIC-CHECKPOINT-INGRESS.md)). OpenTPW now also saves a park in play on a timer, so a crash costs at most a few minutes.

- **What the original does.** Checked statically in tp.exe (REA, 2026-10-18): the park-save writer `0x5AC610` has four callers. The park state machine `0x54E360` calls it in state 0xB,
  and only when the game type is not the online visit (`0x54E360` case 0xB: `if (DAT_00fb3b7c != 1) FUN_005ac610(&DAT_00877c60)`); the main running state (0xA) reaches state 0xB only
  when the exit request word `DAT_00879088` is 2 or 3 (the pause menu's Exit To Lobby and its relatives) or, in the online visit, when the chat session ends. The other three callers are the quick-save command
  `0x40BF90` (it logs "QuickSave_OK" or "Quicksave failed"), `0x49E9B0`, a save-screen action, and a menu callback at `0x424BB0` (code Ghidra has not defined as a function; it stores request 2 through `0x5508B0` and then saves at `0x424BD7`); none has a direct static caller (they run from the UI's callbacks and callback tables). No timer, frame counter or periodic call to
  the writer was found, so the original's per-theme `autosave.TPWS` (Mac evidence: ECONOMY.md, "Original flow that OpenTPW does not reproduce") is written when a park is left, not periodically. The file name
  itself is built from a buffer (`0x877C60`) filled at run time, so the PC name was not resolved statically. A timer reached through an indirect call, which the static callers do not list, cannot be ruled out
  from the executable alone; the periodic save is therefore an OpenTPW extension, not a reproduction.
- **When it saves.** After every 5 minutes of play time (`PeriodicAutosave.IntervalSeconds`) in a park the player owns: a Theme Park World Full Simulation or Instant Action park of one of the four themes
  (the same eligibility as the Exit To Lobby checkpoint). It never runs in a read-only visit, the front end, the sandbox or a Theme Park Inc park. Play time is the real time of frames in which the park
  simulates: while the pause menu, the map window or a texture-pack download holds the park, or while the next park is being built, nothing counts, so a long pause delays the save rather than triggering
  one the moment play resumes; a frame of a suspended machine counts at most a quarter of a second. Changing the park (a new or resumed one) starts the count again.
- **Refused captures are retried soon.** The capture follows the manual save's rules and refusals (`Level.CaptureResumeSnapshot`, the same validation against the catalogue, balance and assets). While the
  advisor is speaking the capture refuses (its speech has no snapshot seam), and so does a park with a staff member in hand or a hire being placed (the capture would cancel it, as the manual save does at the
  pause menu); the autosave tries again after 15 seconds, not after another 5 minutes, and logs the first refusal of a run once. After 8 refusals in a row (a park that can never be captured) it waits a full
  interval. A failed write is retried the same way, and the previous autosave stays.
- **Where it saves.** `save/opentpw-<theme>-autosave-world.json`, its own slot per theme, in the same own format as every world save. It never touches the manual `opentpw-<theme>-world.json` or the Exit To Lobby
  checkpoint `opentpw-<theme>-automatic-world.json`. The write is atomic like the manual save's (`ParkResumeSaveFile.SaveBytes`: a complete temporary file is flushed and moved over the slot), so a crash during the
  write leaves the previous autosave intact; a temporary file a dead process left behind is removed by the next write. Unlike the exit checkpoint, the autosave does not flush the ticket progress and does not
  retire the park.
- **No hitch.** The capture runs in one frame between two simulation ticks (it must see one consistent park), and takes 1.3 ms for a park of 1500 guests and 3.3 ms for the smoke's real park (measurements below); its closing
  validation, the JSON serialization and the file write are moved out of that frame. The snapshot is a detached copy of the park (every capture clones its mutable arrays), so
  `SlicedJsonSerializer` walks its top levels through the serializer's own contract metadata and serializes it in slices of 2 ms per frame, producing the same bytes as the manual save's serializer; the browser
  has no second thread, so the slices work everywhere. The file write runs on a worker thread on the desktop and inline in the browser, where `/save` is an in-memory folder that the page mirrors to IndexedDB
  ([WEB.md](WEB.md#saves-and-settings)). The asset manifest, which hashes every simulation asset and would dominate a capture, is hashed once per park (the park start already does it for its restart snapshot) and
  kept; it is hashed again when the catalogue changes.
- **No gameplay change.** The capture draws from no random stream and changes nothing the simulation reads (`StaffPlacement.PrepareForSave` is the one capture step that does, so the autosave refuses instead of
  cancelling a tool in hand). `PeriodicAutosaveTests` check that the state hash, which covers every random stream, is the same after repeated captures, and that a captured snapshot serializes to the same bytes
  after the park has run on; the park-resume smoke does both on a real park. Determinism pins do not move.
- **Recovery.** When the player enters a theme, the continuation menu offers "Continue autosave" in place of "Continue automatic save" when the autosave is newer than the Exit To Lobby checkpoint and than the
  manual save ([UI.md](UI.md#explicit-theme-continuation)). Continuing reads and validates it like any resume.
- **Option.** OpenTPW page of the options, "Autosave: On/Off" (`GameOptions.Autosave`, on by default).

**Measured** (2026-10-18, Apple silicon, .NET 11 RC2, debug build, with other work running on the machine). A real jungle park of the park-resume smoke (14 guests, 0.85 to 0.92 MB, two runs): capture on the game
thread 3.3 ms, then validation and serialization 9.4 to 9.8 ms over 4 frames (the longest 5.1 to 5.5 ms is the validation, which is one step), file write 4.8 to 6.5 ms on the worker; before the validation moved out of
the capture, the capture alone took 7.8 to 8.7 ms. A synthetic fixture of two rides and a 30 x 30 path block, which makes the guests the whole cost (`OPENTPW_AUTOSAVE_BENCH=<guests> dotnet test source/OpenTPW.Tests
--filter MeasureCaptureSerializeAndWriteOfALargePark` prints it): 1500 guests, a 2.7 MB save: capture 1.3 ms (median, 3.5 ms at worst), validation 4 ms, serialization 2 ms per frame over 4 frames, file write
6 ms on the worker. Serializing in one piece would have cost 8 to 20 ms for that park in one frame. At 60 Hz a frame is 16.7 ms. Browser (WebAssembly) timings are not measured here.

## Loading

**Load** (lobby or pause menu) lists your saved parks by name and theme above the shipped original
saves. Loading resumes the saved park exactly: the same objects at the same cells with the same ids,
guests in the same queues and rides with their routes and timers, the same money, loans, research,
staff and prices, the same weather and clock, and the park open or closed as it was saved — the
gameplay mode and start kind (Full Simulation, Instant Action, reference) come back with it, so an
Instant Action park resumes under its saved balance layer and a closed park stays closed.

The [save/weather roundtrip audit](SAVE-WEATHER-ROUNDTRIP.md) records synthetic file,
clock and weather continuation coverage and the remaining asset-backed integration limits.

A save that does not validate is refused with its reason (a dialog in the UI), and the park or lobby
you were in stays untouched — a rejected load never half-applies. Corrupt files, unknown or missing
members, unsupported versions, inconsistent theme or balance values, and changed game assets are all
refused this way before anything is replaced.

## What the save holds

| Part | Saved |
| --- | --- |
| Layout | every path, queue and placed object with its cell, rotation, attraction id and creation seed, plus the id allocators — a demolished object leaves its id hole, ids are never renumbered |
| Guests | every guest: needs, money, route and navigation buffers, the paid flag, the visitor number and the gate wait, queue/boarding/riding/walk-off/limbo/bounce state, and each attraction's queue cells and membership, and its entrance and exit cells as they stand (a placement resolves them once, the nearest walkable cell when nothing laid them, and a path laid nearer later does not move them; a save from before re-resolves them) |
| Thing pool | the park's one thing pool (`Things`, an optional part: the used list newest first with each thing's model, the head of the free list in its exact order, the destroy requests that wait and the placeholder slots of an imported original save; `[EXT:resume-exact-thing-pool]`), the objects' `ThingId` (layout) and the staff members' `ThingId` (economy), which every guest, object and member of the save must name as a slot of its own model; guest ids are slots from snapshot schema 8 on. A save from before the pool has none: its guests keep their counter ids as slots, and its objects and staff take the next free ones (docs/GUESTS.md, "Thing ids") |
| Ride scripts | the whole script world: every script's identity and graph position, program state, stacks, variables, timers and waits, RNG state, animation channels and rider head slots |
| Prototype ride | the developer Totem (the sandbox ride you place in a park): its position, open state, script seed and full runtime state — animator, head slots, cycle counters — with its economy association re-linked to the saved instance, never bought again; the entrance and exit cells its guests queue at are saved and restored verbatim (the placement's derivation is history: the live park keeps it across later path edits, so re-deriving from the saved paths would resume a different park) |
| Coasters | every live coaster station's track (station ends, pylons with height, pylon kind and stack, node flags), controller state (open, breakdown, speed, seats, money, clocks, whether the motion tick has run) and trains with every rider on board or leaving — joined by attraction id, a rider mid-hand-over (the script has seated it, the bridge has not marked it aboard yet) included; the curve, rating and braking are re-derived on load, so a loaded coaster continues exactly where it was; a track saved with the two-node station (before the three-node station of COAST-029) keeps its pylons on the station the placement builds, is sampled again and lets its riders leave, because the train placement of the new sampling is another one |
| Tracks | the kart/water track layer (every track cell with its mask, flow link, flags, parent, piece and segment), every water ride's controller (phase, state, laps, performance, tick clock, waiting and leaving guests, the sections for verification) with every boat (all integer fields, riders) joined by attraction id, and the cosmetic generator — on load each circuit is walked again and must reproduce the saved validity and sections; collision objects and buoys are rebuilt ([TRACKS.md](TRACKS.md)); absent for a park without a track |
| Economy | the full economy record (bank, loans, ledger, objects with price/level/repair/statistics, staff, research, challenges, golden tickets, RNG streams) and the exact attraction-to-economy-instance links, so two identical rides never swap their money |
| Staff locator order | `StaffData.RoleOrder` in the embedded economy: five independent arrays of member IDs, in `StaffType` order (handyman, mechanic, entertainer, guard, researcher), for presentation only; independent of the saved roster and global ThingPool order |
| Litter | every littered cell with its type and the claiming handyman (in the economy record); a save from before litter by cell had a park-wide count instead, which loads as a clean park (logged) |
| World | the fixed-step clock, the sound-event seed, weather with its pending thunder, the park gate, staff positions, and the advisor's pending advice and history when the park had them |
| Particles | the initialized particle pool's optional configuration, live emitters and effectors, generator, generation counters and free-list order, including empty pools (an optional presentation part, docs/PARTICLES.md): the particles themselves are not saved and screen-space emitters are killed on load, as in the original's PART/PTCL block; not part of the world hash |
| Header | theme, world seed, gameplay mode and start kind, park name, save date |

## Staff locator order and compatibility

`ParkStaff` owns the locator's independent role sequences. A fresh hire prepends to the matching role;
firing removes only that member and preserves the survivors' order. The arrays contain roster member IDs,
not ThingPool slot IDs. Employee-window arrows still follow the global pool, which is saved independently.

The optional `StaffData.RoleOrder` is additive in the existing economy/world formats. When present, it must
contain exactly five non-null arrays in `StaffType` order, with every current member exactly once in the
matching role and no invalid, duplicate, missing, unknown or wrong-role IDs. Validation covers the complete
value before installation, with at most 10,239 current members (`ThingPool.Capacity`). Roster IDs may
grow above that count; they are not pool slot IDs. Restore and capture use copies, so caller-owned arrays cannot mutate the live
owner and captured arrays cannot alias it. Capture always writes the effective order, including empty roles.

Absent or null `RoleOrder` is the explicit older-save compatibility path: read matching live pool members in
used-chain order, then unsupported members in reverse roster order. This projection remains read-only until
a hire or fire materializes it. Opening/cycling the locator and capturing a save do not materialize it, reorder
staff/pool data or consume RNG. Repeated `AttachThings` cannot replace an explicit or materialized order.
The next capture records that effective projection, so loading the new save preserves it; it cannot recover
an independent original role chain that the older save never retained.

Original `TPWS` import is separate from own-format validation. Each complete role chain is read from its
saved world head and staff `RoleNextId` (`+0x210`), mapped from ThingIds to imported member IDs, and checked
against all imported members of that role before being installed. A cycle, wrong-role or missing link,
out-of-bounds ID, or orphaned member produces precise per-role import evidence and replaces the entire
malformed role with its saved-global-order projection. A valid prefix is never partially installed; other
valid roles keep their exact independent chains. The fallback is an explicit robustness policy, not evidence
that global pool order equals role order. Existing file-layer rejection of a nonzero invalid role head is
unchanged. A roster above the supported capacity remains unsupported; the fallback does not expand that limit.

`RoleOrder` is saved presentation state and is the only new nested staff property excluded from the
[canonical hash](DETERMINISM.md#canonical-state-hash). Training budgets, `HaveEverTrained`, members and all
other gameplay fields remain hashed. Synthetic coverage is in `StaffRoleOrderTests` and
`LocatorStaffRoleOrderTests`; local C# execution is unavailable without the dotnet SDK and awaits exact-head CI.

## Limits

- **The prototype ride is saved as what it is.** The developer Totem (the sandbox ride) saves and
  resumes like any attraction — its script mid-instruction, guests queuing, aboard or mid-boarding —
  but the part describes exactly the Inca Totem; any other entry in that slot is refused rather
  than accepted as opaque state. Player-built coasters are saved too (see above). The
  original `TPWS` import stays partial and read-only ([TPWS-PAYLOAD.md](TPWS-PAYLOAD.md)).
- **Transient sounds are not saved.** No voice, chain, music or other audio state persists through a save. A ride
  that holds a scream is saved as its script holds it: the handle, its STARTSCREAM and SCREAMLEVEL operands, and the count
  of handles that refused starts lost. These are the same with or without audio. The held scream plays again once the
  loaded park's audio runs ([AUDIO.md](AUDIO.md), "Held screams in a park save"); the chain of a lost handle does not. The
  pause menu's Save does not wait for advice being spoken: the speech goes on, it is not saved, and a loaded park starts
  silent. A park cannot be saved while a placed object owns a live flag-`0x8` looping EVENT or repair-hide voice. This
  remains a live-audio persistence limitation: stop the owning object/sound before saving; one-shots, finished loop
  voices and headless/stopped owners do not trigger it.
- **The camera and menu focus are not saved**; the view and interface start fresh after a load.
- **Older saves keep their old cell types.** The cell map is saved as it was. A save made before the cells were typed as
  tp.exe types them (no terrain, water, fixed walkway and the cell flags 0x80 and 0x400 at level start, and the 4/9/10 footprint types
  of placed objects; `ParkCellMap.MapStartCell`, `WriteFootprint`) resumes with every such cell still empty (type 0). The load does
  not type them again, because that would change a newer save's cell map version and the caches keyed on it; the census, the
  litter counts and the park value of such a park count those cells as empty ground.
- **Imported queues are restored once.** A Jungle park saved before the import kept the shipped save's queue cells has empty ground
  where the Belly Bounce's queue lies; the load lays those cells again from the level's shipped save (`[EXT:legacy-save-queue-cells]`,
  user-approved 2026-10-20, [GUESTS.md](GUESTS.md#closed-or-split-in-the-guests-queues-and-arrivals-pass)). It changes such a save's cell
  map once; a newer save, or one whose queue the player removed, is not touched.
  A save older than the access-link fix of 2026-10-17 is not repaired: its entrance cell has its queue link but no link byte,
  which is also the state of a newer save whose queue the player removed, so the load cannot tell them apart and leaves the
  ride as the save holds it (no heuristic guesses which it was).
- **The analyser's caches are saved outside the run hash.** The net worth and the litter/path block counts are cached with their
  turn stamps as the original's analyser keeps them; an older save without them counts afresh at the first read.
- **The save is pinned to the game data it was made with.** It carries a SHA-256 manifest of the
  simulation assets (the theme's object, balance and terrain files; see below). Load it with the
  same game files: when a simulation asset changed — another edition, modified data — loading is
  refused naming that asset, before your current park is touched. Textures, sounds, music and
  texture packs are not pinned; changing them does not invalidate a park.

## The file

JSON with `"Format": "opentpw-world"` and `"Version": 1`, capped at 64 MiB, written with its own
suffix only (`.json` or `.otpw`). Parsing is strict — unknown members and inconsistent values are
rejected — and `TPWS`/`TPWI` paths are refused outright, so an original save is never read or
written as a world save. The economy part is the economy-only save of [ECONOMY.md](ECONOMY.md)
embedded by value, so both formats' validators run on every load.

## Developer notes

- **API.** `Level.CaptureResumeSnapshot` captures between ticks (refusing a placed object's live looping
  EVENT or repair-hide voice, and advice being spoken unless the pause menu's Save asks); `ParkResumeSaveFile.Save`/`Load` do the
  atomic, size-capped, suffix-checked file I/O; `GameFlow.SaveCurrentPark` (the pause menu's Save)
  and `GameFlow.ResumePark` (Load) wire it into the UI. The developer Totem rides along as the
  envelope's optional prototype part (`PrototypeRideSave`), joined by attraction id exactly like a
  placed object — the version stays 1, so saves from before the part (parks without the Totem) load
  unchanged.
- **Loading is a transaction.** `ParkResumeSaveFile.ValidateSnapshot` validates purely against the
  freshly loaded catalog, balance settings and asset manifest; the park is then rebuilt into an
  isolated candidate (runtimes, the prototype Totem, script world, queue geometry, guests, clock,
  the coasters, then the
  optional weather/gate/staff/advisor parts, with reference graphs restored in two passes), and only
  a complete candidate replaces the live park. Every failure path keeps the current park running. Ids,
  iteration order and next-id allocators are restored exactly as saved: nothing is renumbered or
  re-registered, no construction fee is charged again, and no default fixed item appears twice.
- **Validation layers.** The pure shape check (identity, order, allocator agreement, bounds,
  cross-references — run before a byte is written and again on load), the park check (theme, balance
  layer, mode/start-kind pinning, catalog, economy bindings, the coaster part against the live
  coaster stations, asset manifest), and
  the destination modules' own validators during candidate construction.
- **Asset manifest.** SHA-256 over the decompressed content of every `.sam`, `.rse` and `.MD2`
  member of the theme's object catalog (bonus objects included), the category default settings, the
  balance layers (`Standard.sam` global and theme, `Easy_Standard.sam` in easy mode,
  `Challenges.sam`, the theme's `global.sam`), the advisor settings and the theme's terrain
  `base.map`/`base.MD2`. Presentation (textures, sounds, music, fonts, strings) and texture packs
  are out of scope by design. Keys are canonical identifiers; the loader hashes only files the
  trusted catalog names, never a path stored in the save.
- **Legacy economy-only save.** `ParkSaveFile` (`"opentpw-park"`, version 2;
  [ECONOMY.md](ECONOMY.md) "Save format", [DETERMINISM.md](DETERMINISM.md) "Park save") is unchanged:
  it holds the economy and the RNG streams, not the world, and does not save the start kind. The
  world envelope embeds its record by value. Older notes saying "the park save" keeps guests and
  scripts running describe that legacy file.
- **Smoke hook** (development builds from source): the two-phase native verification runs with

  ```sh
  bash scripts/run.sh --game-path '/path/to/Theme Park World' --mute --no-advisor \
    --park-resume-smoke capture --park-resume-file park.otpw --park-resume-evidence evidence/
  bash scripts/run.sh --game-path '/path/to/Theme Park World' --mute --no-advisor \
    --park-resume-smoke restore --park-resume-file park.otpw --park-resume-evidence evidence/
  ```

  The capture phase builds a seeded Jungle park (the same ride placed twice at rotations 0 and 180
  with edited queues, two identical shops at different prices, a toilet, a demolished object leaving
  an id hole, two staff and fourteen guests), saves it through the pause menu's own save handler and
  continues the live park. The restore phase — a cold second process — requires the save's bytes to
  survive, rejects seven corrupted candidates (guest/queue/attraction references, a doctored
  asset manifest, a runtime script-identity mismatch, an unsupported version and a malformed menu
  entry) with the live park intact
  each time, then resumes and compares every typed section hash and the composite world hash against
  the capture run at each checkpoint, finishing with post-load economy checks (admission booked
  once, a sale credited once with its links removed, a new placement, wages charged once, payment
  bindings one-to-one). The capture phase continues one tick a frame and makes the continuation's
  edits between frames as the HUD does (the speed raised and set back, the first attraction closed and
  reopened, the entrance fee); the restore phase continues through the level's own frame step in
  seeded random frames of 1 to 16 ticks (`OPENTPW_SMOKE_BATCHING`: `random` by default, or `1` or
  `16`) with the same edits replayed inside its frames, so the pair also proves with game data that the
  park after a tick does not depend on how the ticks fall into frames
  ([DETERMINISM.md](DETERMINISM.md), "Frame batching"). The continuation's clock section leaves out the
  clock's remainder of a tick, which follows the frames' time. The evidence JSON holds hashes and counters; the directory also contains
  the own-format save and screenshot readbacks, without original game files.
  These are controlled, accelerated fixtures that validate persistence of current OpenTPW behaviour,
  not original-game fidelity.
- **Smoke hook, prototype variant** (same command with `OPENTPW_SMOKE_PROTOTYPE=1` exported for
  both phases): the fixture also places the developer Totem beside the path spine, so the pair
  proves the prototype part natively — the save written through the HUD handler carries the Totem,
  the cold resume rebuilds it with its saved guest cells and re-links its saved economy instance
  (never buying again), the Totem's own corrupt candidates (the part dropped, its guest cells moved
  off the map) are rejected like the rest, and the post-load checks remove the resumed Totem to see
  its instance sold exactly once when the live park had bought it (deregistered when it had not).

Verified in October 2026 development builds: the native capture/restore pair above passed as two
separate processes on real Metal with identical section hashes throughout the continuation; the
browser build saved from the real pause menu, kept the park across a page and WebAssembly reload
with the game folder chosen again, and resumed with identical save bytes, stable ids, visitors and
income still arriving; an invalid-version save and a late script-identity failure were both
rejected with the running park's state unchanged. The browser also re-saved and reloaded the richer
park with eight placed objects, fourteen visitors, two staff and two same-type shops priced at
$60/$120. The save bytes, object/guest/staff ids, prices and closed-park state survived the page and
WebAssembly restart. The 600-tick equality check above was performed in the native processes.

## Continuing a theme from the lobby

The explicit Continue choice described in [UI.md](UI.md#explicit-theme-continuation) reuses the existing manual own-world slot and staged resume. It does not create an automatic save, rewrite a manual checkpoint, change saved game mode or resume a TPI layout checkpoint. The automatic entry next to it reads the Exit To Lobby checkpoint or, after a crash, the newer [periodic autosave](#periodic-autosave), through the same staged resume. New Park remains a distinct action. A readable matching save makes Continue available; trusted game-asset and full candidate validation still happen on activation, and failures preserve the existing world instead of falling back to New.


New live-world mechanic dispatch may persist `AwaitingJobArrival` in the embedded
own economy staff record. It is omitted when false; old missing fields retain
already-started countdowns. A true phase requires a mechanic with positive work,
matching assigned ride and reciprocal owner. Capture validation refuses invalid
pending jobs before an owned save write, and load validates them again. Existing
staff actor position/fraction supports mid-travel resume; no world binding means
pending work waits rather than becoming a headless repair. The binding is rebuilt
by the Level staff setup. This is an additive own-format phase, not a native staff
field or a save-format version change; canonical gameplay hash schema14 includes it.

## Restart snapshot persistence

Each owned park start/load captures its restart point once, before play advances. Capture, asset hashing and validation stay on the game thread. The detached JSON is serialized in soft 2 ms slices. Desktop completion copies the immutable buffer, hashes it and performs the atomic writes on a worker; immediate Restart, park replacement and orderly shutdown wait for that job. The browser copies and hashes at most 64 KiB per copy/hash step, then writes into its in-memory filesystem. Allocation, an individual serializer step and the final browser write can still exceed the soft budget; this is not a hard frame-time bound.

`opentpw-<theme>-restart.json.pending.json` is an internal recovery marker, not a park save. It initially contains `"pending"`; before replacement it contains the completed JSON's SHA256. The previous restart JSON is retained during this work but is not accepted as the new park's restart point. A process interrupted before replacement leaves the prior file retained and unavailable through Restart; an interruption after replacement but before marker cleanup can recover the new file if its hash matches. A file with no marker keeps the existing format compatibility. The initial marker and target-digest marker retain the same flushed temporary-file/atomic-rename protocol as the snapshot; no durability barrier is removed. On failure, Instant Action can start a new park, while other modes display the load-failed message. The browser's durability boundary is its transactional IndexedDB mirror, described in [WEB.md](WEB.md#saves-and-settings).

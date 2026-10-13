# Saving and resuming a park

OpenTPW saves parks in its **own world-save format** (`"Format": "opentpw-world"`, version 1): one
file holds the whole running park, and Load resumes it where it left off. This is separate from the
original game's save format — original `TPWS`/`TPWI` files stay read-only imports
([TPWS-PAYLOAD.md](TPWS-PAYLOAD.md)), OpenTPW never writes them, and an own-format save is not an
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

## Loading

**Load** (lobby or pause menu) lists your saved parks by name and theme above the shipped original
saves. Loading resumes the saved park exactly: the same objects at the same cells with the same ids,
guests in the same queues and rides with their routes and timers, the same money, loans, research,
staff and prices, the same weather and clock, and the park open or closed as it was saved — the
gameplay mode and start kind (Full Simulation, Instant Action, reference) come back with it, so an
Instant Action park resumes under its saved balance layer and a closed park stays closed.

A save that does not validate is refused with its reason (a dialog in the UI), and the park or lobby
you were in stays untouched — a rejected load never half-applies. Corrupt files, unknown or missing
members, unsupported versions, inconsistent theme or balance values, and changed game assets are all
refused this way before anything is replaced.

## What the save holds

| Part | Saved |
| --- | --- |
| Layout | every path, queue and placed object with its cell, rotation, attraction id and creation seed, plus the id allocators — a demolished object leaves its id hole, ids are never renumbered |
| Guests | every guest: needs, money, route and navigation buffers, arrival lane, queue/boarding/riding/walk-off/limbo/bounce state, and each attraction's queue cells and membership |
| Ride scripts | the whole script world: every script's identity and graph position, program state, stacks, variables, timers and waits, RNG state, animation channels and rider head slots |
| Coasters | every live coaster station's track (station ends, pylons with height, pylon kind and stack, node flags), controller state (open, breakdown, speed, seats, money, clocks) and trains with every rider on board or leaving — joined by attraction id, a rider mid-hand-over (the script has seated it, the bridge has not marked it aboard yet) included; the curve, rating and braking are re-derived on load, so a loaded coaster continues exactly where it was |
| Economy | the full economy record (bank, loans, ledger, objects with price/level/repair/statistics, staff, research, challenges, golden tickets, RNG streams) and the exact attraction-to-economy-instance links, so two identical rides never swap their money |
| World | the fixed-step clock, the sound-event seed, weather with its pending thunder, the park gate, staff positions, and the advisor's pending advice and history when the park had them |
| Header | theme, world seed, gameplay mode and start kind, park name, save date |

## Limits

- **Prototype rides are not saved yet.** A park that has one cannot be saved;
  the save is refused with that reason instead of writing a park that would come back wrong.
  Player-built coasters are saved (see above). The
  original `TPWS` import stays partial and read-only ([TPWS-PAYLOAD.md](TPWS-PAYLOAD.md)).
- **Transient sounds are not saved.** A park cannot be saved while a ride scream is playing or the
  advisor is speaking — save again a moment later. No voice, music or audio state persists through a
  save.
- **The camera and menu focus are not saved**; the view and interface start fresh after a load.
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

- **API.** `Level.CaptureResumeSnapshot` captures between ticks (refusing prototype rides,
  an attraction's active scream and active advisor speech); `ParkResumeSaveFile.Save`/`Load` do the
  atomic, size-capped, suffix-checked file I/O; `GameFlow.SaveCurrentPark` (the pause menu's Save)
  and `GameFlow.ResumePark` (Load) wire it into the UI.
- **Loading is a transaction.** `ParkResumeSaveFile.ValidateSnapshot` validates purely against the
  freshly loaded catalog, balance settings and asset manifest; the park is then rebuilt into an
  isolated candidate (runtimes, script world, queue geometry, guests, clock, the coasters, then the
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
  bindings one-to-one). The evidence JSON holds hashes and counters; the directory also contains
  the own-format save and screenshot readbacks, without original game files.
  These are controlled, accelerated fixtures that validate persistence of current OpenTPW behaviour,
  not original-game fidelity.

Verified in October 2026 development builds: the native capture/restore pair above passed as two
separate processes on real Metal with identical section hashes throughout the continuation; the
browser build saved from the real pause menu, kept the park across a page and WebAssembly reload
with the game folder chosen again, and resumed with identical save bytes, stable ids, visitors and
income still arriving; an invalid-version save and a late script-identity failure were both
rejected with the running park's state unchanged. The browser also re-saved and reloaded the richer
park with eight placed objects, fourteen visitors, two staff and two same-type shops priced at
$60/$120. The save bytes, object/guest/staff ids, prices and closed-park state survived the page and
WebAssembly restart. The 600-tick equality check above was performed in the native processes.

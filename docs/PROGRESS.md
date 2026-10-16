# Execution status — October 12, 2026

This is a development prototype, not the complete offline game. The full plan
and final acceptance remain unchanged. The user approved publishing the plan,
progress, and verified code to the fork. Git history records the published
snapshots; original assets, native binaries, captures, and local agent runtime
files remain outside that publication.

## Economy rules: bankruptcy, challenges, cell types, net worth and breakdowns (October 15, 2026)

Economy rules read again in tp.exe and built:

- **Bankruptcy** (ECON-030 narrowed): the bank acts once on message 2 (`0x4D02D0`), skipped while a winning sequence shows; `0x5168F0`
  sets world state 4, breaks every attraction (VAR_BREAKSTAT = 1) and sets the gate's variable 0 to 2. State 4 skips the thing loop
  (`0x516380`) while the calendar, bank, staff pool and golden tickets run on; the park stays open, and the warnings at 3 and 5 months
  are gone (the bank only compares with 5).
- **Challenges** go to the message tube with Accept and Decline, which call AcceptChallenge and DeclineChallenge (`0x48E0F0` actions
  `0x1A`/`0x1B` → `0x4D2DF0`/`0x4D2EC0`) for whichever offer is waiting.
- **Cell types** (ECON-050 narrowed): world init types each cell from the MAP and the terrain (`0x536490`): cells without terrain become
  7, 2 or 30, every cell without the fixed flag `0x20` gets the unowned flag `0x40`, which the level's land (Buy Land's `ParkLand`) clears
  inside the fence, and placement writes footprint types (`0x528A70` → `0x5346D0`).
- **Park rating and net worth**: the visitor term counts guests on cell types 0/1/3/9/10, capped at 1000 (`0x4CCEB0`, ECON-027 retired);
  owned cells add 2/3/5/6/1/50 points (`0x4C7DB0`) over the land Buy Land maintains, so the net worth and the loans' credit test count
  only owned land, and the net worth is cached as the original does (ECON-026 narrowed: the fence's hoardings are not counted).
- **Loans** (ECON-007 retired): the Available tab lists an offer that is not bought and passed the stored credit test, amount ≤ net worth
  − bought loans' remaining instalments (`0x4D06B0`), run when the dialog opens or a tab is selected; the stored tests are imported from
  original saves (SAVE-008 narrowed). The park save gains the optional `AvailableLoanOffers`; older saves get the bank's default.
- **Litter and path block caches** (`0x4C8BA0`, `0x4C8AE0`) are read with the strike's and the thought's ages (ECON-020 narrowed).
- **Breakdowns go through the ride scripts** (ECON-023 narrowed): `0x4E0B90` writes VAR_BREAKSTAT = 1, the ride's state 1 breaks it down and
  calls a mechanic, and the repair waits until the script clears VAR_BROKEN (`0x4DA830`).
- **The bank's message 2 every month** (ADVISOR-020 retired): `0x4D0370` posts it at each month end more than five months in the red,
  and the advisor answers each with advice 106; the bank's handler still loses the park once.
- **Saves**: the analyser caches (net worth, litter and path blocks) and the last observed script state are saved as optional fields
  outside the run hash, and an unanswered challenge offer whose message is gone is posted again (UI-025).

The pinned run hash moves to `0x650CEA6712B82D88` on top of the guest walk and the staff candidate draws (docs/DETERMINISM.md).

## Guests walk with tp.exe's walk (October 15, 2026)

Guests walking the park's cells now use the original walk, checked instruction by instruction in tp.exe and against the original
routines run in the repo's x86 oracle (finding X25 in docs/reverse/ORACLE.md: the edge test, step commit, route search, avoid_walls
and 40 whole walks through the set-up and steering step agree on every pinned input; larger runs while porting, 12,700 edge, commit and
search cases, 8,000 avoid_walls cases and about 22,000 whole-walk steps, agreed as well):

- **Route search, edge test and step commit** (`0x511420`, `0x4d8750`, `0x510510`): a failed search makes the step return stuck (2, `+0xB8`),
  which the arrival state treats as arrival and the litter state as a drop (GUEST-010).
- **avoid_walls** (`0x50e4a0`) takes the position's cell, its corner blocks by quadrant and the ray test `0x50cc20` (WALK-002 retired);
  the walk's progress is `ONE − (rem << 16) / +0xA0` over the whole route (`0x50fd40`, WALK-009 retired).
- **Block stamps** (`0x4d8c60`): a cell type change stamps its block, and a walk searches again when its block's stamp moved (`0x50ed6c`).
- **Walk mask**: guests walk with mask 0; the guard's set-down sets 1 (`0x4febab`) and the recall clears it (`0x500ab9`).
- The walk uses tp.exe's fixed-point arithmetic: its multiply rounds towards −∞ (`0x50df70`) and its divide towards 0 (`0x50e080`).

Queue walks, arrival lanes, staff and walks restored from older saves keep the earlier walk: OpenTPW's queue cells carry no `+0x0C`
neighbour links yet (QUEUE-003), so WALK-004 to -008 are narrowed and GATE-006 and QUEUE-010 stay blocked. The saves gain optional
fields (`OriginalWalk`, `Waypoints`, `Mask` on the walk; `BlockStamps`, `StampCounter` on the layout); older saves load with the
earlier walk and unstamped blocks. The pinned run hash moves to `0x4A4014649DF391DB` with the staff candidate draws (the walk alone gave `0x0B7C79BA1B5AC12A`; docs/DETERMINISM.md).

## Interface reading round, part B (October 15, 2026)

Twenty-two more interface approximations were read again in tp.exe:

- **The buy window** hides its scroll bar while nothing scrolls (`0x664495`, as the video shows with four rides; UI-049 retired) and its
  working-life gauge keeps its fixed 100/1024 (`0x4abae0`).
- **The park map** draws its overlay cells on the original's grid, one pixel left of and above the art's terrain (`0x5f23dc`; UI-085
  retired), and opens with only the rides layer.
- **Placing staff** posts the advisor's help 309 (`0x49b9dd`, withdrawn by the Place tool at `0x46c8d4`; UI-097 retired) instead of
  OpenTPW's own hint line.
- **Training budgets** step by 25 within 0..10000 with each row's grade gauge (`0x4b27d7`, `0x4c9c00`), and **the research workload
  knob** maps to the original's workload of 70..100 (`0x4aacce`, `0x4a985b`).
- The left mouse button is button 0 (`0x634963`), so UI-910 keeps only the per-visitor window `0x4b79b0`, which is not ported;
  13 interface items are narrowed.

## Online, economy and path reading round (October 15, 2026)

Nineteen approximations were read again in tp.exe:

- **Chat**: blanks before a `/` are skipped (`0x5e45d0`; ONLINE-002 retired). **The globe** starts at yaw 0, pitch 0 whenever a globe screen
  opens (`0x5dcfe0`; ONLINE-008 retired), edge steering works only in the Pushscroll option and right-button steering only in the other,
  and the pitch limit stops the turn. **Pins** replace their root matrix instead of composing it (`0x5d81c0`, no doubled scale).
- **The online world screen** puts the city labels at the top right (table `0x7581a0`), empty when no city is hovered; satellite counts are
  centred in white (`0x4c4fb0`); the publish globe has no back button.
- ONLINE-011, -015, -016 and -018, ECON-005 (bank `+0x10` is the batch balance, only cleared; OpenTPW's finer ledger split carries
  `[EXT:ECON-LEDGER-SPLIT]`) and PATH-009 (bank `+0x114` is the withdrawals flag) are retired; 11 items are narrowed.

## The per-employee window and All Visitors from tp.exe's tables (October 15, 2026)

- **The per-employee window** (`0x4b6080`, table `0x756f48`) opens from the staff list as the original's All Staff list does: the name as
  title, days employed, wage, skill, happiness and energy bars and the status, refreshed every 4 s; dismiss (with the Confirmations
  option's yes/no box), pick-up (`0x505c50`), the patrol toggle (`0x4b5b50`), the cycle arrows, all staff, locate and the tick; locate and Ctrl+S
  open the staff locator panel on the employee (`0x4b5f07`, `0x481790`) (UI-098 retired, UI-092 narrowed).
- **All Visitors** is built from table `0x7506c8` and its builder `0x493530`: six sortable columns (number, cash, time in park in hours or
  days, rides, thought, happiness), a value refresh every 2 s and the park info, all staff and all attractions buttons (UI-907 narrowed).
- **The hire window's balance** reads the last closed month (`0x49bdd0`), and the string table keeps whole records, so "%d hours/day/days"
  (UITEXT 440–442) are no longer empty.

## Coaster trains and pylons from tp.exe (October 15, 2026)

- **Trains** are laid out around their middle car, (n − 1) >> 1, from the last car forward, placed after each rating pass with the release
  time as their spacing (`0x43c450`, `0x43c1f0`), and run at the mean speed of the samples under their cars; the hops queue by remaining
  budget, the middle car giving the hop and the front car braking (`0x43ced8`; COAST-022 retired). The ride view and camera follow the
  front car.
- **Seats** are only in the centre-type cars (`0x43ba90`); COAST 6 bounds the capacity by the fleet's and the train's head counts, a head
  being a dummy record with flag `0x80` (`0x43b330`, `0x44b2b0`; COAST-023 retired). The Caterpillar has 16 heads, not 12.
- **Pylons** play all five clips (loft, rotate, incline, bank, loop tilt; `0x430ca0`, `0x430ed0`) through their clip maps, with each dummy
  record on its own node (`0x44ab90`); the station ends take the auto bank (`0x4310c8`). Cars stand on the curve at their knot-pair
  parameter (`0x43c0a0` → `0x43e6b0`) and riders sit on the PC head sockets (COAST-015, -017, -033 and -034 narrowed).
- Saves gain two optional fields (a train's reference car, the ride's lead-car coordinate, which also joins the state hash); older saves
  convert from the front car. The coaster hash content changes with all of this (docs/DETERMINISM.md); no pinned hash depended on it.

## Nine divergences from tp.exe fixed (October 15, 2026)

Divergences found while reading other areas, each checked in tp.exe and fixed in its own commit:

- **Upgrades are paid at installation** (`0x4df8f0`, debit at `0x4dfa96` with no balance check); buying (`0x4af4e9`) only requests the
  upgrade when the balance is above the cost. A save's pending upgrade gains the optional `UpgradeCostDue`; one loaded from an older
  save, already charged at purchase, is not charged again.
- **A sideshow play wins** when R % 100 ≤ the win byte (100 − InitChanceOfLoosing, `0x4e26e2`, `0x4db3a1`).
- **A sale or play at price 0 books no takings** (`0x4fe1a0` skips `0x4e16b0`); the cost of goods and the customer count still apply
  (ECON-034 narrowed).
- **A cleaned toilet opens** through `0x4df390` without the reopening guard (ECON-049 text extended).
- **A called ride gets its mechanic** in any state, condemned included (`0x4da740`, `0x4daa90`).
- **Removing an attraction or bin** sends its guests the original's removal message (`0x4fb360`, message 10): riders get their model
  back, queued guests are dismissed, every targeting guest goes to state 6, and every guest's history slots naming the object are
  cleared (GUEST-004 and GUEST-005 narrowed).
- **A closed park stops visitors on the walk to the booth** (`0x4ff730`, `0x4ff7f0`): they go home with no thought; the booth and the
  booking read no closed state, so a guest who paid at a closed park is sent home without a refund. A bankrupt park still takes no fee
  (its thing loop stops in tp.exe, ECON-030).
- **A staff candidate draws** variant, spare, grade, name and timeout in tp.exe's order (`0x507600`, GUEST-014 narrowed).
- **A new park starts with only the research-free objects researched** (`Upgrades[0].CostOfResearch` = 0, `0x5031a0`): Jungle's Wooden
  Log now has to be researched.

A tenth candidate (the dismissal leaving VAR_LETMEON set) does not hold: every caller unlinks first (`0x4ddd20`). The pinned run hash
moves to `0xDB1DDEE4817DE5B0` (the staff candidate draws; docs/DETERMINISM.md).

## Advisor presentation, crowd sound and particle density from tp.exe (October 15, 2026)

- **The advisor moves**: its antennae, hands and eyes vertex tracks and the level advisors' eye texture tracks play through the shared
  instance draw (`0x429df0` → `0x473c70`); clip node bits carry from clip to clip, so bit `0x10000` hides the bug head and mouth and
  `0x40000` plays cat_ui `0xC5` (the ticket close-up, ADVISOR-006 and -026 narrowed).
- **The slap and F1**: cutting the advisor off while it speaks plays one of three slaps (`0x5994e0`), and F1 in the eight help windows
  replays the last tutorial record (`0x488c02` → `0x59ab50`, saved as an optional field; ADVISOR-027 retired). The help descriptors read
  `HelpMessage.Score`, not `ContextHelpScore` (both 1000 in the shipped file).
- **The crowd sound follows its parameter** (`0x6bbb80`): a parameter change recomputes the playing sample's volume and pitch, on the
  original's beat of every 32nd 31 ms step (`0x54f82d`, AUDIO-036 retired). The ticket timer plays cat_ui `0xC6` (`0x5999b2`).
- **Particle density** comes from the player's graphics preset as the original's detail files set it (500/1000/1500, `0x44dc60`;
  PTCL-906 narrowed).

## Buy Land, as in the original (October 15, 2026)

The original's land purchase is in: the build window's Miscellaneous tab has "Buy Land" (with the `Costs.MapCell` price) and "Clear
Land" (`0x4ab12f`), and the land a park owns now limits what can be built.

- **The fence and the owned land** come from each theme's `Hoardings.sam` (loaded at `0x407f20`): the fence runs from post A (the gate's
  second entrance + 3) to post B (the first − 4, `0x5323f0`), and every cell outside it is unowned (`0x40`, set at `0x536490`). An
  imported original save keeps its own flags and fence (a save whose fence cannot be walked from post A to post B keeps its flags
  but has no Buy Land); on the Jungle Easymode save the fill rule reproduces all 16,384 cells' flags, and that save owns 674 cells
  more than a new park (land the player bought).
- **The Buy Land tool** (mode `0x39`) takes two clicks on the fence-post lattice and checks the rectangle as `0x531a50` does (map
  bounds and size, gate posts, runs over no-terrain or fixed-walkway cells (types 7 and 0x1e; water does not count), contact with
  the park, no new land, no enclosed pocket, money with an equal balance allowed); each unowned cell in [x0+2, x1) × [y0+2, y1)
  costs `Costs.MapCell` (`0x532303`; 100 in the standard balance, 10 in Jungle's easy one), booked as other costs. The fence is
  walked round the new land (`0x5327e0`), one purchase per activation.
- **Unowned land** refuses paths, queues, objects, tracks and staff drops (`0x535670`). Saves keep the land as an optional record;
  older saves fall back to the land computed from the fence.
- Not yet: the fence models and the unowned-terrain tint, Clear Land (the rectangle bulldozer, PATH-005), and the coaster tool's
  ownership test. PATH-007 and ECON-026 are narrowed.

## Interface windows from tp.exe's tables (October 15, 2026)

- **All Attractions** is built from four decoded tables (`0x7508e0`, `0x750ab8`, `0x750ba0`, `0x750ca0`): the original per-category
  columns, a value refresh every 2 seconds, sorting on any column, reopening on the last category and the park info, all staff and all
  visitors buttons; OpenTPW's detail rows and status column are gone, as the original list has neither (UI-905 narrowed).
- **The staff and visitor locator panels** (tables `0x756950`, `0x756ad8`) open with Ctrl+S and Ctrl+V (`0x481790` → `0x4b3f60`,
  `0x4b4280`): a role's button finds and cycles that role's people and the camera follows; Ctrl+V locates the visitor whose info a list
  row click or a park click opened (tp.exe's visitor info window `0x7cc408`), and a key without an actor keeps the strip's last name (UI-904
  retired, UI-091 narrowed).
- **The research lab's fluids** play tp.exe's animation (`0x4a95b0`, `0x4a9fc0`) at the rate of the category's effort share and staffing
  (`0x503870`, `0x503980`); the **HUD key counter** shows the player's earned tickets / 3 plus extra keys (`0x5af680`), hidden in Instant
  Action; the **loans summary** shows the last closed month's money in and out minus outstanding instalments (`0x49fb30`); the
  **game-mode toggles** latch and only the tick, or Enter (message `0x802`), starts the park (`0x4a6d00`). UI-066, -051, -063 and -013 are narrowed.

## Audio, weather and advisor reading round (October 15, 2026)

Thirty-one audio, advisor and compatibility approximations were read again in tp.exe:

- **Weather draws with the original's render states** (decoder `0x567620`): lightning is additive (`0x200d52`, SRCALPHA/ONE, no depth
  writes), snow writes depth, and all three discard alpha below 16/255; the parallax is absolute (`0x57efd0`); snow is one triangle per
  flake with a small jitter each frame (`0x8bcbc8` bit `0x100` is never set on PC); the weather's step follows the game speed (`0x57ff60`
  reads the rate-scaled clock `0x402d70`).
- **Sounds**: a level event id is looked up in the level's own category only (`0x6b87d0`; incagod.RSE's EVENT 3 id 43 is silent, as in
  the original), and event and repair sounds start at whole units (`0x5573d0`).
- ADVISOR-007, AUDIO-011, AUDIO-015, AUDIO-026 and AUDIO-908 are retired, ADVISOR-010's manual tools are an extension
  (`[EXT:speech-tool-audio-clock]`), and 15 items are narrowed. Items that need QMixer.dll or usp10.dll stay open: only tp.exe is analysed.

## Interface reading round, part A (October 15, 2026)

Twenty-two interface approximations were read again in tp.exe:

- **The lobby island camera follows the original** (`0x5e1210`, `0x5d5c50`): the eye and target glide separately by 0.1 and 0.2 of the
  frame step (the frame's ms × 0.01, capped at 500 ms), the field of view is ISLANDFOV (100°, vertical, `0x5e13ed`) instead of 60°,
  and the angle wraps into [0, 2π) (UI-017 retired).
- UI-016 (the island angle and height already matched) and UI-029 (the door button latch and help, `0x4ad4e0`, `0x669410`) are retired.
- Nine items are narrowed with what was read: the font bank chosen by screen mode (`0x419710`), control colours and the single-pass
  text renderer without a shadow (`0x48f830`), the four readers of RMBCancelOn (`0x4881a0`), the greedy text wrap (`0x6aec80`), the
  slider class, the options labels and the buy-list row pitch (font slot 7's line height, `0x4ad240`).

## MD2 paths: vehicles and karts follow their routes (October 15, 2026)

The path parameters decoded earlier today index a part of the geometry models that was still opaque: the
16-byte "extra records" (header 0x40/0xAC) are paths, 18 of them in 15 distinct models (Bézier, one linear).

- **Paths** (`ModelPath`, `ModelFile.Paths`): kind (closed, Bézier, linear, otherwise Catmull-Rom), point count and
  points. A node follows one when its parent is a type-0x208 dummy (`Kart_path01`, `Flightpath`, `FERRYSPLINE`…);
  the dummy's stored index selects the path in its high 16 bits.
- **Playback** from tp.exe's sampler 0x471860: the per-tick value (0x474EA0: per-tick blend, seam across 100 → 0)
  becomes `fmod(v + 1000, 100)` percent of the path's segments, evaluated by 0x474840/0x474BF0/0x474CC0; flag 0x400
  turns the node along the path (0x474A20/0x474C70, rows built by 0x470780). `ObjectAnimator` and
  `ModelAnimationPlayer` play it, so the Haunted House karts, the go-karts, the bus, ferry, seaplane and slide car
  and the Halloween advisor's bats follow their routes instead of standing at their stored pose (every such record
  evaluates on its path; moving playback is checked for the Haunted House karts).
- **Checks:** every flag-0x200 node hangs under a path dummy; where a clip starts on the stored pose (Haunted House
  B/E/M/S, Busm1, slidec) tick 0 lands within 0.02 of the stored translation and faces within a few degrees of the
  stored orientation; a placed Haunted House moves its four karts along their paths. The open Bézier routes store a
  closing control pair the sampler never reads, so the bus and ferry jump back to their route's start when the
  value wraps past 100, as the original does.
- Not played: flag 0x400 together with position keys (no record has it). OpenTPW guards: path tables decode at
  most four times the member, empty or non-finite paths are rejected, and a direction parallel to up (a division
  by zero in the original) faces +X. No native render was run.
- Verification (macOS arm64, original data): the full test suite passes (4,496 passed, 109 skipped); an independent
  Codex review's findings (path-table allocation budget, non-finite and empty paths, scale after facing across
  channels, a vertical direction, claims beyond the tests) are fixed.

## Format table reconciled; SAM reader, LIP checks and MD2 path parameters (October 15, 2026)

The README's format table mixed decoding with game integration: the sound banks' ⚠️ listed untriggered
events and the mixer, not the one entry word that is really still unknown. It now has separate columns
for what decodes, what is still unknown in the files and what the game does not use yet; `tools/site_status.py` reads the new
columns, and docs/FORMAT-BACKLOG.md replaces the October 9 list that had fallen behind the code.

- **SAM:** `SettingsFile`'s parser was character-based and skipped newlines after a key, so a key
  without a value took the next line's key as its value, quoted names lost everything after the first
  space (`"Inca Totem"` read as `"Inca`) and the rows of `---` blocks became settings. It now reads the
  line grammar the shipped files use (one-token or quoted value, `#`/`//` comments, blocks skipped) and
  agrees with `ObjectSettingsFile` on every shipped `.sam`, loose and in WADs. It reads the rest of any
  stream instead of trusting `Length`, so non-seekable and partly read streams work.
- **LIP:** the two corpus checks no longer skip `z_error`, the one Layer I clip, since the decoder reads
  Layer I: 639 clips, still 70.2 dB talking against 41.9 dB silent, `sp_478` still the only overrun.
- **MD2 path parameters:** the record +36 block of flag 0x200 is decoded (`ModelPathParameters`): a 16-byte
  header and one float per tick, a position along the node's path in percent, as the PC sampler
  0x471860/0x474EA0 reads it. All 71 corpus blocks fit. The path it indexes (model instance +0xAC) is not
  decoded, so nothing moves along it yet and 0x200/0x400 stay unsupported.
- The merge of main into the older `formats-phase1` branch keeps its exact Layer I corpus counts and
  ffmpeg-pinned Layer I clips; main already had its decoder.
- Verification (macOS arm64, original data): the full test suite passes (4,486 passed, 109 skipped);
  an independent Codex review's four findings (path table allocated before its bounds check, flag 0x200
  without a block, an unclosed SAM block, an overclaimed sound-bank ✅) are fixed. No native run was needed.

## Economy rules settled against tp.exe, part two (October 15, 2026)

Sixteen more economy approximations were checked again, item by item, in tp.exe (leads from an earlier REA survey, every address re-read):

- **A new park starts closed** (`0x51562d` writes the closed flag): only the gate button, the two keys and the online park creation open it
  (`0x519ef0`), and only an imported park saved open is open on load (ECON-031 retired). The M3/M4 gate runs open their park explicitly.
- **Challenges:** a won challenge with a follow-up marks the follow-up won, so the challenge can be offered again (`0x4d2fb0` re-reads the
  index the offer moved); declining a follow-up that is not Independent pays half its prize (`0x4d2ec0`); the follow-up is the first open
  slot of its type in table order (`0x4d2b60`, no same-object preference); type 18 is the best coaster's counted sections with no floor
  (`0x4d1873`, ECON-036 retired); types 14 and 19 read the coasters' counted sections and type 22 counts ride uses (ECON-034, -035, -055
  narrowed).
- **Fixed items** are the objects whose WhichUIType is 4, as the buy menu tests (`0x4aaff0`; ECON-041 retired; the same six per theme), and
  a repairing mechanic cannot be picked up (`0x505bee` refuses state 13; ECON-052 narrowed).
- ECON-040 (one golden key once, `0x5afc30`), ECON-051 (the withdrawals flag) and ECON-053 (a carried member is put down where he was
  before a save, `0x46cdc0`) are retired; ECON-038, -044, -048, -049, -050 and -054 are narrowed.

## Guest rules settled against tp.exe (October 15, 2026)

Sixteen guest approximations were checked again, item by item, in tp.exe (leads from an earlier REA survey, every address re-read):

- **Prices against RipOffOK** (`0x4d3e00`, key 42 of the table at `0x7456e8`): each shop or sideshow type's own RipOffOK now sets how far
  above the fair price a guest still pays (100 for shops, 250 for sideshows in the shipped data, so up to 2× and 3.5×); a noticeable
  balance change (GUEST-003 narrowed).
- **A full queue costs nothing** (`0x4ffbc0`): a guest finding no room records the event and walks on, with no thought and no happiness
  loss, and a queue that is too long no longer costs happiness either (GUEST-004 narrowed).
- **Newest attraction first** (`0x4fcb10`, `0x519d80` pushes at the head): the choice walks the objects newest first, which decides ties
  (GUEST-002 narrowed).
- **The use routine's other terms** (`0x4fe1e0`): the small-portion penalty, scaled happiness, fat, salt and ice from the ingredients and
  the toilet speed term (GUEST-005 narrowed).
- **Energy** is tp.exe's field `+0x1B8`, 0 at birth and changed only by `0x4fd970` (GUEST-008 retired); 15 guest items are narrowed. The
  determinism hash is now `0xD28386DACAD73241`.

## Queue exits as in tp.exe (October 15, 2026)

- **A closed ride drains its queue one head per turn** (`0x4e1220` → `0x4e0450`): the others keep waiting, a closed ride that breaks down
  sends away two that turn (`0x4e0e60`), and joins and the walk to a ride no longer test the open word or the broken state, as the
  original's join `0x4ffbc0` does not (QUEUE-018 retired; the choice still skips closed rides). A guest at its place in a coaster's queue
  leaves with thought 13 while the track refuses guests (`0x500612`; COAST-024 narrowed).
- **Every queue exit but a removed ride costs the medium happiness drop** (the dismissal `0x5012f0`, `0x4fea70(1)`): feeling sick, bored,
  unhappy, needing the toilet, a failed move-up, a queue edit or a refused price alike; a water ride with a broken circuit is only
  skipped by the choice, its queue stays (`0x500612` tests coasters and kart tracks). The "problem with a queue" exit (`0x500270`) thinks nothing, and a toilet above 80
  thinks "need toilet" before the relief test (`0x5003aa`).

## Economy rules settled against tp.exe, part one (October 15, 2026)

Seventeen economy approximations were checked again, item by item, in tp.exe (leads from an earlier REA survey, every address re-read):

- **Research categories come from the object data** (`0x4aaffc`, the add-on id at `+0x284`; `+0x508` only means "model not loaded"): a
  project takes the object's own Research.Category and Group, and an object outside category 4 gets level projects (ECON-018 retired; no
  change on the shipped data, which a game-data test pins).
- **The research cursor moves on** after a project completes to the next eligible project in table order, wrapping (`0x4aaec0` → `0x5034f0`),
  instead of falling back to the first selectable one (ECON-017 narrowed: the table's fill order is still open).
- **Staff training is booked as other costs** (`0x505a10` writes its own word `+0x1fa40`, not the staff-costs series of the eight-series
  finance graph `0x49a7b0`); total money out is unchanged (ECON-005 narrowed).
- ECON-002 (the 2000-01-01 Gregorian UTC calendar, `0x4f7eb0`) and ECON-028 (`0x523658`) are retired; the ×2/×4 speed buttons are an
  OpenTPW extension (`[EXT:speed-buttons]`: the original has only the numpad keys, `0x7480a0`) and so is the headless economy's own
  mechanic dispatch to worn rides (`[EXT:headless-economy]`: the finder `0x4daa90` only takes broken, upgrading or called rides). Ten more
  items are narrowed to what is still unknown.

## Path tool and walking settled against tp.exe (October 15, 2026)

The path and walk approximations were checked again, item by item, in tp.exe (leads from an earlier REA survey, every address re-read):

- **A refused preview lays nothing** (`0x524a63`, flag `0x816d48`): a click whose line contains a refused cell ends the path or queue tool
  without laying the cells before the refusal. After a commit the tool ends only when the line reached a cell that was already a path or a
  queue cell, the ride's own queue included (`0x5345a1`); otherwise the snapped end is the next start, and the queue-join sound plays only for
  a committed line (PATH-003 and PATH-012 retired).
- **Backspace without a tool** takes one placement off the path cell under the cursor, as `0x40be8b` → ClearCell(0, 1) does, so a cell laid
  twice survives one press.
- PATH-002 (the tool's undo already matched; the old text saying Backspace does nothing was wrong), PATH-010 (path costs land in the other
  costs, `0x4d01f0`) and PATH-011 (Escape ends the tool first, `0x40c180`; only the next Escape opens the pause menu) are retired, and 20 path
  and walk items are narrowed to what is still unknown.

## Queue rules settled against tp.exe (October 15, 2026)

The queue and gate approximations were checked again, item by item, in tp.exe (leads from an earlier REA survey, every address re-read):

- **Full rides are skipped** (`0x4dd920`): a guest choosing an attraction passes over a ride whose queue already holds 4 × its cells, as the
  original's choice does; the join keeps its own room check for a queue that fills while the guest walks there (QUEUE-017 retired).
- **Queued guests turn as the original does** (`0x4ffff0`): only within 30 turns of their last interlude, one in ten turns, by −400..+399 of
  2048 instead of a fixed ±400 (QUEUE-009 retired).
- QUEUE-016 is retired (the turn order `0x516394` → `0x50b360` matches), the M3 gate minute is an OpenTPW extension
  (`[EXT:m3-gate-minute]`: the original's clock runs on scaled real time, 31 ms substeps and a park turn every eighth), and 16 queue items
  are narrowed to what is still unknown. Two register claims were wrong and are corrected: a queue event goes into the guest's own history,
  and a 0/0 queue score takes the 0.75 floor on PC (`0x4e07d2`), not 0. The determinism hash is now `0x4AEFBABAD786E2FD`.

## Original saves bring their research, challenges, staff HQ and hiring pool (October 15, 2026)

Importing an original save (the Jungle Easymode start) now restores four more parts, all read in tp.exe:

- **Research** (`0x502dc0`): the lab's projects, their points and each category's current project, effort and work load. The Jungle start
  continues researching rides 1150, shops 1212, sideshows 1304 and features 1414 where it left off; the lab's own table order differs from
  OpenTPW's info-id order (ECON-017 narrowed with the evidence).
- **Challenges** (`0x4d2320`, `0x4d1e90`): won and declined challenges, a running or unanswered challenge and the next offer day. A system not
  yet switched on offers its first challenge on park day `DaysUntilFirstChallenge`, as the original counts from the park's start: the Jungle
  start now gets its first offer on day 540 instead of 572.
- **Staff HQ** (`0x508bb0`, `0x50c800`): training budgets in the role order handyman, mechanic, entertainer, guard, researcher, and each
  role's strike level, active flag and last-considered turn (`0x508e70`).
- **Hiring pool** (`0x507850`, the 670 bytes before the calendar): the Jungle start keeps its 16 saved candidates with their own expiry
  turns, and the pool refills on its own schedule. The saved stamps (refills on turns 361 and 722) confirm the pool timing now read in
  `0x5084f0`: the pool refills on the first turn past 4 × `TimeBetweenStaffUpdates` turns, and each candidate draws its own timeout of
  `StaffTimeoutTime` + R % (`StaffTimeoutTime` / 2) units of four turns (`0x507600`) instead of a fixed 120 seconds (ECON-010 retired).

Imported staff also take the display state of their saved state word (`0x5064f0`): the word is the member's own state, not the
STAFFSTATES index, so a member walking to rest or striking no longer shows as working or picked up. SAVE-003, 004, 005, 007 and 009 are narrowed.
## The All Attractions button is a round icon again (October 15, 2026)

The button that opens the All Attractions window from the buy window (above All Staff) and the staff window was a text
button in a round slot about 100 units wide. Its label ("All Rides", in Dutch "Alle Attracties") fitted no font size,
so the text fallback wrapped it one letter per line down the panel. It now wears the attractions window's own rides tab
icon (`b_srides`), round like its neighbours, with the name as its popup help.

## Scream voices limited to the sound manager's pool of twelve (October 14, 2026)

Ride screams no longer start new children everywhere in the park. tp.exe's sound manager (`0x6b62b0`) ticks only 12 audible
voices, ranked by event priority with the ground distance as tie-break (`0x6bb9f0`, `0x6b6bc0`), and a held scream chain makes its
children, the first included, only in that tick (`0x6bdae0` → `0x6bdb50`). Screams (priority 2700) rank below the crowd (5300) and
most ride and ambient sounds, so OpenTPW now holds back a scream chain that is out of the listener's range (95 sound units over the
ground) or outside the twelve highest-ranked voices; its started children play out (AUDIO-038). The timing of a chain itself was confirmed in tp.exe: a full
ride (8+ riders, event 74) screams every 0–0.5 s, as in the original data.

## Ticket booth verdict, broken-ride state and no boredom loss (October 14, 2026)

The ticket booth now weighs the entrance fee against the park as tp.exe does (`0x4ff5b0`, `0x4ff9d0`): the park value is the attraction
points over 4..6 plus `MinimumEntryFee`, and the fee against it is far too expensive (home), expensive (a wait of 50 + R % 50 park turns
and a medium happiness loss, home when happiness runs out), cheap (a medium gain) or fair, with the thresholds from `PeepInfo` in
Standard.sam (ARRIVAL-006 narrowed; ARRIVAL-007 registers that OpenTPW still charges the guest's purse). A broken ride now keeps state 1
until it is opened again, as the ride dispatch `0x4e0e00` does, instead of following `VAR_BROKEN`: guests may still choose it until its
update notices the breakdown, and a script that clears `VAR_BROKEN` early no longer releases the whole queue at once (QUEUE-017..019
narrowed). The boredom loss is gone: the major decision `0x4ff419` has no boredom test and nothing in tp.exe reads `BoredomThreshold`
(GUEST-009 retired). The determinism hash schema is 19.

## Challenge chains, training and speed keys from tp.exe (October 14, 2026)

A won challenge with a follow-up type no longer pays its prize: the original (`0x4d2fb0`) credits the prize only when the challenge
has no follow-up, so a chain pays the prize of its last part (jungle's chain 7 → 8 now pays 30,000 instead of 37,500). A lost
challenge is no longer marked finished (`0x4d3140` only counts the loss), so it can be offered again. The challenge check
`0x4d1660` confirms the accumulated types and the balloon and costume census percentages (ECON-034 narrowed); the follow-up's
same-object rule is registered as ECON-055. Training's 100 points per grade is read in `0x505a10` (ECON-008 retired; the
Standard.sam comment's 1000 does not match the code). The clock's 31 ms substeps and three-turns-per-frame limit (`0x54e360`) and
the speed keys (numpad +, − and *: × 1.25, × 0.8 within 0.25..2, reset) narrow ECON-001 and ECON-004.

## Bank rules from tp.exe: bankruptcy timing and the withdrawals flag (October 14, 2026)

The park's bankruptcy now follows the bank's month routine (`0x4d0370`) instead of counting month ends in the red. A debit that
takes the balance from 0 or more below 0 stamps the park turn, and every debit keeps the balance after it (`0x4d01f0`); at a month
end, a park whose balance and last balance are both below 0 counts the 30-day months since the stamp, and above 5 it is bankrupt
(the original's message type 2, ECON-030). The bank's withdrawals flag is honoured: with it clear, debits, instalments and payoffs
charge nothing, and instalments raise the year profit by their principal share. The three bank words are saved (older saves count
the stamp back from their months in the red) and imported from original saves (SAVE-008 narrowed). An early payoff compares the
balance as an unsigned word, so a balance below 0 does not refuse it (`0x4d0850`). ECON-005, 007 and 051 are narrowed to the
traced routines; the determinism hash schema is 18.

## Automatic per-theme checkpoints (October 14, 2026)

Exit to Lobby from a Theme Park World Full Simulation or Instant Action park now saves an automatic checkpoint for that theme
(`opentpw-{theme}-automatic-world.json`, written atomically after the owned progress is flushed), beside the manual save, which it
never replaces. Entering the theme again offers Continue for it. A failure before the write keeps the park with Retry and Back; a
failure while leaving the park after the write is reported instead of crashing. Details: docs/AUTOMATIC-CHECKPOINT-INGRESS.md and
docs/AUTOMATIC-CHECKPOINT-CORE.md.

Two checkpoint tests that capture a synthetic level failed locally when they ran after the original-data asset tests: those
tests left the global `FileSystem` on the game data, so `Level.SetupStaff` loaded real staff atlases and created textures without
a graphics device. The checkpoint tests' CPU renderer scope now also pins `FileSystem` to an empty directory, and the coaster,
staff sprite and M4 gate asset tests restore `FileSystem` (and `Log`) after each test. CI has no game data, so it never saw this.

## Application-owned progress entry and smoke repair (October 14, 2026)

New Full Simulation checks the selected theme's explicit entry cost against
application-owned ticket history before constructing a park. Missing history
opens a starter-key consent screen; only confirmation can stage a new ledger
with one extra key before activation. The model still defaults to zero keys.
Known zero-key or incompatible history is not reset to make entry succeed.
Instant Action creates no progress file, and manual Continue/Load retain their
existing saved-mode behavior without a new-entry key gate. See
[player progress entry and lifetime](PLAYER-PROGRESS-INGRESS.md) for storage,
precommit failure, tracking and retirement qualifications.

The front-end smoke uses a fresh temporary save directory. Its plan now asserts
that `newProgress` is a `ProgressChoiceScreen` and that the progress file is absent
before injecting the actual starter-consent button click. It then waits in the
queued readiness check for completed construction, the activated Jungle level
and its HUD. It reports a progress failure dialog explicitly and retains the
strict 3,000-frame budget; no history is preseeded. The former 40-frame delay
could reach the original post-load assertion while construction was still
active. CPU regression controls drive those actual queued smoke steps and
menu/admission callbacks with synthetic theme metadata, checking successful
consent, Back cancellation and preservation of known zero-key history. Additional
controller controls wait beyond 40 frames, reject a wrong theme or missing HUD,
report a real failure dialog, and retain the strict noncompletion bound. These
CPU controls model activation fields; native runs use actual construction state.

The original native smoke objection is not counted as a successful native rerun.
These CPU controls do not exercise native text readback, the GPU/window, original
Jungle assets or graphical park construction. A native run must observe the
consent screen, accept it, load Jungle and finish the existing complete smoke
without resetting history. Earlier native smoke results below belong to their
older source snapshots, not this progress-entry change.

## Native smokes on a 120 Hz arm64 Mac (October 13, 2026)

- **Retail WCT textures decode again.** The WCT bounds audit (`docs/WCT-ROBUSTNESS.md`) was written
  without original data and refused 5,055 of the 8,416 shipped textures: every BILZ chunk (its stream
  size counts the 28-byte header too) and every LZSS chunk of odd length (the last 16-bit word is the
  final byte plus zero bits). Both are now read as the retail data stores them; all 8,412 textures the
  decoder handled before the audit decode to byte-identical RGBA, and a corpus-wide test
  (`EveryOriginalTextureDecodesExceptThePinnedFailures`) walks every shipped WCT.
- **Front-end smoke and advisor speech.** The pause-menu save waits for the Jungle welcome line
  (response 1, 28.63 s, the right clip from the level bank). The speech plays in real time, so the frames
  it takes follow the display: with the frame cap off, a 120 Hz screen spent the whole 3,000-frame budget
  waiting. Those frames no longer count against the budget; the wait is bounded by the clip's remaining
  length plus 5 s instead. Measured: the speech ended 24.16 s into a predicted 24.17 s wait; the test
  passed at 2560×1440 px, UI scale 2, in 3,697 frames, 2,897 of them speech.
- **Sandbox smoke ride-view click.** The guard clicked the first cell of the first removable object, which
  at 1280×720 logical projects below the bottom edge of the park camera's view. It now clicks a removable
  object's cell that is in view, picks back to itself and is occupied by that object. The sandbox smoke
  passed in 2,032 frames at the same size.

## Merged PowerPC evidence lanes

The reviewed clock/PEF, economy/save, VM, UI-binding, advisor/audio, guest
and TPI-comparison lanes were merged into the verified local bundle. Positive-APR
save validation does not apply an unproven annuity equation. Layer I effects
decode; the original PRNG, complete controllers, clock/event/audio integration
and final fidelity remain explicit open work.

Before the remote integration, bounded merge checks passed: 31 toolkit,
7 fidelity and 84 review tests; 22 save/economy, 41 VM, 56 UI and 49 audio/LIP
checks; standalone scheduler 35, loan 5,437 assertions, advisor 52 and guest 23.
The combined Release regression selection passed 154 cases without skips and
the Release build had zero errors. A Dutch Metal smoke passed 222 frames,
including reload, payments and read-only guards. These results qualify their
bounded slices, not every original game rule. Reference helpers remain separate
from production gameplay; vertex-animation/runtime and mode changes continue
in their own worktrees with review.

## Remote integration on .NET 10

The verified local bundle was reconciled with the remote .NET 10, first-run
setup, Linux native-library and Retina updates in an isolated integration
worktree. The merge retains the independently validated Layer I factor math,
both branches' codec tests, the packed 40-byte SDT header/name fixes and actual
MPEG sample-rate/channel metadata. Repository documentation remains English.

Review fixes preserve selected Instant Action mode when loading after a return
to the lobby, qualify the still-missing original credit eligibility predicate,
and keep setup responsive when a mounted drive stalls. Discovery/inspection
use bounded child processes with cancellation and conservative exit ownership;
unknown editions are not classified from generic folder trees, while the exact
supplied unsupported Sim Coaster retail signature is rejected.

Own macOS arm64 verification on SDK 10.0.401:

- The full original/six-language/bonus/MTR/native suite ran **955 cases**:
  **952 passed, one failed, two skipped**, in 7.30 minutes. The only failure was
  the missing manual ECON-007 documentation row; it was restored and the
  approximation consistency test then passed on its own. The skipped checks
  require Linux libdl or a case-sensitive filesystem.
- After the final cleanup-ownership fix, **41 setup/discovery/lifecycle and
  approximation checks passed without skips**, including two new injected
  kill/status-failure regressions and picker cancellation. **106 codec/metadata/SDT/economy checks passed
  without skips**.
- Dutch Metal smoke passed **231 frames**: the original payment/reload/read-only
  checks plus Instant Action → lobby → load, with Easy balance layers and
  $100,000 retained. The requested UI scale 2 fitted to 1 at 1280×720.
- The isolated evidence runner reached **230 Python cases**, with no failures
  and eight missing private UI-fixture skips; all six retained SDK 8 standalone
  harnesses passed. Source-reference tools follow .NET 10; source-link-only
  helpers retain their compatible framework.

Release builds have zero errors; existing dependency, nullable/compiler and
new SDK analyzer warnings remain. These checks qualify the merge and bounded
regressions, not original gameplay/device fidelity or every other OS/edition.

## Latest remote UI and graphics updates

The integration also includes remote commit `e31c804`: optional local texture
packs, world-only capture, original-style online screens, text input/scrolling
and Options > Game files. First-run setup asks only for the game folder; its
bounded inspection/picker behavior survives that simplification. In-game
folder changes use the same cancellable children, provide explicit typed-path
recovery and cancel owned work when screens or GameFlow are disposed.

After this update, **182 focused texture/UI/online/setup/display/compatibility
checks passed with no skips**, and the Release build had zero errors. Dutch
Metal smoke passed **264 frames**, including the new online and folder screens
and the earlier mode/payment/reload/read-only regressions. A world-only capture
wrote a validated **1280×720 PNG** outside Git. Text readback now selects the
exact glyph range of its draw; the earlier overlap heuristic incorrectly
included covered glyphs from the underlying lobby. No threshold was relaxed.

`scripts/run.sh` selects the private SDK 10 fallback before the retained SDK 8
on the verified arm64 route and respects explicit `DOTNET`; shell syntax and
normal selection were checked. Linux/Windows native execution, actual external
upscaler execution and every platform's live folder dialog remain separate
checks. No original assets, captures or native binaries are published.

## Integrated check after resumption

The advisor/compat, front-end, rides, and online work has been merged with
economy, visitors, and display settings. This qualifies as a development
preview; original runtime fidelity and the complete offline scope remain open.

- Full macOS arm64 suite on the catalog/online/shader integration: **793
  passed, 0 skipped, 0 failed**, including original data, six languages, bonus
  content, MTR fixtures, and native shader checks; 10m46s.
- The UI scale fit was then corrected. On the updated code: **51 targeted
  UI/online command tests passed, 0 skipped**, Release solution build with
  0 errors. Existing nullable, compiler, and dependency warnings remain.
- Own Metal final check: Dutch, 1280×720, Nearest 50%, requested UI scale 2
  with a visibly explained fit to 1. **222 frames**: lobby, options and glyphs,
  two different catalog objects each paid for once, sale, overlapping build
  refused, back to lobby, reload, and read-only visit.
- ShaderCompiler keeps compiled code and reflection; native shader objects are
  owned per renderer. The reproduced Metal crash on level reload is thereby
  resolved. Archive root listing and case-insensitive SDT members were repaired,
  with regression tests. Level/GameFlow manages level and online lifetime.
- Own CLI export/import followed by a native visit: **8 frames**, 14 unchanged
  objects, no park economy; build/delete/save/load/export refused. The full
  theme catalog is shown in the HUD; Level owns purchase/sale so the UI does not
  charge money again. Modified shared layouts cannot yet be rendered.
- [Patch 2](PATCH-2.md) was applied on a Windows GitHub runner: three native
  patch steps passed, 43 installation differences checked. The game data has
  28 differences and 24 modified decompressed WAD members. Only hashes, own
  descriptions, and analysis tools were published. Temporary transfer, secrets,
  and the encrypted artifact were removed; reference data is kept outside git.
- [Mac evidence](reverse/FINDINGS.md): 16 PEF containers read; the conditional
  timer fallback and the millisecond conversion are recorded with pinned
  witnesses. That does not prove an animation, calendar, or advisor frequency.
  16 Python evidence tests and 7 fidelity register tests pass; the patch
  comparer has synthetic tests.
- The supplied [Windows manual](REFERENCE-MANUAL.md) establishes one key per
  three earned golden tickets. That ratio has been corrected with boundary,
  purchase, and save regression tests; start count and access usage remain
  marked as uncertain. Own targeted economy check: 28 passed, 0 skipped.
- The README distinguishes decoder status from gameplay fidelity: BF4 is
  implemented for 33 fonts in each of six tested languages. All remaining format
  limitations are listed per table row; movie handling and preview status have
  been updated.
- Nine independent worktrees were started for the PowerPC follow-up analysis and
  TPI comparison (six native and three app subagents). Their new source and
  renderer changes will be reviewed and tested separately before merging.
- The PowerPC differential oracle ran the arrival rule: the group size `0xcb4e8`
  (F9) and the park points `0xc24c0` (F10), see [ORACLE.md](reverse/ORACLE.md).
  Both differ from GUESTS.md and the C# rule; the routine readings agree on every
  input. `GuestSimulation.ArrivalGroupSize` is compared through the C# bridge
  (now 49 differences, see the next bullet) and the differences are
  tracked as ARRIVAL-002..004. `ParkEconomy.ParkPoints` now follows F10's track
  type rule: an untracked type is aged from its first purchase and divided by
  its copies, every copy of a kart, water ride or coaster counts in full. Only
  the counts-test `0xdc834` remained (121 differences, ARRIVAL-003).
- The counts-test `0xdc834` was traced and executed (oracle target
  `park-points-counts-test`, 522/522 against the reading, call order included).
  It needs `Info.IsChoosable`, a ride that is not broken down or condemned, the
  open word, a connected entrance, queue room, a riderless coaster and a kart or
  water track word. `ParkEconomy.ParkPoints` now applies the first three
  (`IsChoosable`, `IsBrokenDown`, `IsOpen`): the park points bridge input's open
  flag is passed as `IsOpen` and C# agrees on 507/507. ARRIVAL-003 stays open for
  the conditions that need world state (entrance, queue room, coaster riders).
- ARRIVAL-002 (factor) is resolved, and the earlier lights finding is corrected.
  `0x1083c8` reads the thing index at game `+0x1da724` (`mWeather`), not `+0x1da734`
  (`mTrafficLights`; the offset had been miscalculated in the first reading). The
  `+48` word of that weather object is `mCurrentDrops`, so the arrival factor is
  0.8 while rain or snow drops are falling and 1.2 otherwise; the Traffic Lights
  play no part. `GuestSimulation.ArrivalGroupSize` takes `WeatherDropsFalling`
  (`ParkWeather.CurrentDrops > 0`, set by `Level`); `TrafficLightsPlaced` and
  `ObjectCatalogEntry.IsTrafficLights` are gone. `ParkWeather` now ramps snow
  drops too (AUDIO-028 closed). The oracle pin is unchanged (49 differences, all
  the missing floor, ARRIVAL-004). A non-sandbox online visit still cuts groups to
  500 - guests (`OnlineVisit`; GameType 1 stays an assumption under ARRIVAL-002).
- The oracle also executed the arrival update `0xcb2ac` over scripts of repeated
  calls (F11, ARRIVAL-004). A negative group (1700 guests: -200) behaves like an
  empty one: no guest, timer restart and cleared active byte at the next stop,
  so nothing holds (the static reading was wrong). Only the stored group word
  and one arrival-vehicle step differ; C# behavior is unchanged.
- The oracle executed the queue's two untraced predicates (F15, F16 in [ORACLE.md](reverse/ORACLE.md)).
  `0xdfe34`, the guests' ride-failure exit, never reads `VAR_BROKEN`: it is a relief object (toilet) whose
  wear gauge is below 25 (QUEUE-008 stays approximate: a broken ride has no such exit). The join excitement gate
  `0xe9a84`/`0xe02ec` gates on the type's `ExcitementLevel` byte and compares a live excitement (sideshow
  formula, speed and duration ratios, coaster `0.5 E + 50`) over bytes, clamped to ±50; C# compares the static
  level (155 of 524 oracle inputs differ). `RideVisitorBridge.ExcitementRefusal` is now the gate on its own
  (behaviour unchanged), compared through the C# bridge; QUEUE-004/008/019 comments say what execution showed.
- The queue's two executed findings are now C# (F15, F16 in [ORACLE.md](reverse/ORACLE.md)). The join gate tests a guest
  only when the low byte of the object's `ExcitementLevel` is nonzero and compares the live excitement over bytes, clamped
  to ±50 (a sideshow's goods, price and win chance, the speed and duration ratios, a coaster's rating): a default sideshow
  with a prize 25 above its price and a win chance of 50 scores 40 and takes a guest preferring 80, a level-100 ride at a
  tenth of its speed scores 75 and takes one preferring 50. A broken ride no longer sends its queue away "feeling sick"
  (that exit is a worn-out toilet, `0xdfe34`); the ride update (`0xe0a8c`, executed in the oracle) dismisses its head guest
  each park turn instead, the rest waiting. The oracle compares the C# side on `queue-failure-predicate`,
  `queue-join-difference` and `queue-join-gate`; only a ride with a thing behind ride `+36` differs (QUEUE-004).
- The oracle executed what a shop sale does to the buying guest (F18 in [ORACLE.md](reverse/ORACLE.md), ECON-020, `0xeaaf8`).
  The five shop effects (thirst, hunger, vomit, happiness, litter) are added to five guest meters, each clamped to 0..100; litter is the
  raw `LitterEffect` (type `+0x154`), but it fills the guest's own meter, and the guest update drops litter once that meter reaches 90.
  The static trace's "unscaled, up to 100x" was a misreading of the unit: with the shipped values (50, 25, 10, 0) a guest who keeps buying
  makes 1/2, 1/4, 1/9 and 0 litter item per sale (a burger shop: 0.5, as before), within 11 % of OpenTPW's `LitterEffect/100`. C# is
  unchanged (it has no guest litter meter; ECON-020 stays `[APPROX]` with the sharper text); the oracle pins the 213 inputs where the
  old rule leaves the 0..100 range.
- The oracle executed the mechanic's arrival routine `0xd9c0c` (F19 in [ORACLE.md](reverse/ORACLE.md), ECON-021) and C# follows it:
  a repair takes `WorkDuration x (100 - state of repair) / 100` park turns (a grade-0 mechanic on a ride at 40 %: 48, not a flat 80 hours;
  the ride's level does not matter) and an upgrade `WorkDuration x DurationOfUpgrade` of the level it installs, without a minimum. Mechanic job
  time now counts park turns (saved as `BusyTurns`; older saves' `BusyTicks` convert from tenths of an hour, rounded up). The walk to the ride stays an approximation (ECON-021).
- The guest speed boost is settled (ORACLE.md F20, WALK-001). `0xeb0f4` is the store at the end of a block in the outcome routine `0xeaaf8`
  (one call site, the guest leaving an object): only the Sugar ingredient raises it, by trunc(6 x amount / 100) with the ride build's amount 50, so a guest
  leaving an Ice Cream Shop walks about one percent faster for three turns (at most +15 per use, so the u16 wrap above 661 never happens in play). The oracle
  target `walk-boost` executes the routine (521/521); `GuestSpeed.RaiseBoost` does the same in `ApplyVisit` and agrees. WALK-001 now only keeps the shop screen's
  amount and its saved field.
- The oracle settled ECON-039 (F23 in [ORACLE.md](reverse/ORACLE.md)): the yearly profit golden ticket is won when the bank's year profit is
  signed strictly greater than `GoldenTicketLocal.ProfitYear` (`0xd33dc`, executed), with no factor. The 0.9 multiplier the register
  saw at `0x13fdc` is the advisor's near-ticket hint (`GoldTicketNearToXProfit`), executed separately. OpenTPW now compares `>` instead
  of `>=` (the two differ only at exactly 15000 in jungle) and the `[APPROX:ECON-039]` is gone. The same oracle run executed every
  other local and global ticket arm: all are signed strictly-greater compares (so 100 admissions no longer win the visitors ticket), the
  happiness ticket is the single average above `Happiness` and more than `AtLeastThisManyHappyPeople` guests, and OpenTPW follows; the recent-visitors
  ticket's park-age gate is registered as ECON-052.
- Dependency audit is clean: ImageSharp is replaced by an in-tree PNG writer
  (`PngWriter`, `RgbaResampler`), Zio is 0.22.2, and Newtonsoft.Json is pinned to
  13.0.4 (it came in transitively through Veldrid's NativeLibraryLoader).
- Two original-data regressions for the SDT Layer I decoder: every Layer I entry
  in all installed `.sdt` banks decodes (2,650, of which 5 MPEG-1 at 44.1 kHz),
  and the MPEG-1 clip `tp_riderepaired` (stored without a `.mp2` suffix) matches
  the pinned ffmpeg reference within 1 LSB. No decoder change: `Mp2Decoder`
  already covers MPEG-1/2 Layer I.
- Loans follow the original (ORACLE F12-F14, ECON-006 resolved). The monthly
  instalment is trunc(P x pow(1 + APR/100, N/24) / N) for every APR, paid flat for
  N months with no interest on a balance and no final remainder; the total
  payable is M x N and an early repayment pays M x the months left (100,000 at
  20 % over 36 months: 3,651 a month, 131,436 in all, instead of 3,716 and
  133,795). At 0 % APR the loan repays 99,972, as in the original. `LoanAccount`
  stores the months repaid; saves with the old remaining balance load with their
  months left. Original-save import checks the stored instalment for every APR and keeps it when it differs.
  The loan oracle targets now agree with C# (505/505 offers, 503/503 terms). The
  withdrawals flag is assumed on (new ECON-051).
  Early repayment also charges the year profit the whole-term share (oracle
  target `loan-payoff`), the year-profit word wraps as a signed 32-bit value on
  every update, and a saved instalment that differs from this host's pow is kept
  and reported instead of rejected. The 0 % APR share (about 119 million a month
  in jungle Easy mode) is original behaviour and stays.
- An x86 differential oracle now runs isolated routines of the decrypted TPW v2.0
  `tp.exe` (Unicorn engine, a dev-only optional tool; ORACLE.md section 7, owner
  decision 2026-10-12). Its first target is the world RNG step `0x516330`: it is the
  same logical generator as the Mac `0x105328` (same object offset, constants,
  setter, tick and inline copies) but stores the successor rotated right by 13, so
  the PC and Mac streams differ (finding X1: 507 of 508 inputs). OpenTPW has no
  world LCG yet and DET-plan follows the Mac; nothing in the game changed.
  Finding X2: the Theme Park Inc `Game.exe` (no-CD, SHA-256 `77ea0c41...`) has the
  same step at `0x6493e0` with the state slot moved to `+0x1eab3c`, rotation
  included (executed, 508 inputs; agrees with the TPW PC reading, differs from the
  Mac plan in 507). The TPI image's 16 out-of-range relocation entries no longer
  break a relocated load.
- The x86 oracle now executes the `tp.exe` counterparts of the four Mac-derived rules
  (ORACLE.md 7.6, findings X3 to X6): arrival group size `0x4cf5b0`, loan offers
  `0x4cf7c0`, the monthly loan step `0x4d0370` and park points `0x4c8240` implement the
  same rules as the Mac, so C# needs no change. The only differences are x87 arithmetic: an
  integer wrap and a single-vs-extended rounding at totals no park reaches, and a last-bit
  dependence of exactly-integer instalments on the x87 control word (which the code expects to
  be `0x27F`). The runtime's own `pow` is executed, not stubbed.
- The same four rules in the Theme Park Inc `Game.exe` (ORACLE.md 7.7, findings X7 to X10; analysis only, no C#
  change): loan offers `0x5bfb70`, the monthly loan step `0x5c0b10` and park points `0x5b05d0` are the same rules
  as TPW at moved offsets (shipped offers and instalments unchanged). The arrival group size `0x5bcd60` differs:
  the 0.8 / 1.2 factor became a weather percentage (Rain 90, Storm 80, Snow 75, Heatwave 120 in `Standard.sam`),
  the integer divide became a floating one (80 points + 20 over 25 gives 3, not 4), and a mode word of 3 gives no
  group. The note also records that tp.exe's arrival routine reads the `mWeather` handle, not Traffic Lights.
- The oracle now executes the Mac month-end staff training `0xfc1c4` (finding F17, ECON-013): each role's budget is divided by all of the role's staff (the base counts are zero), every member below the top grade is charged the whole share and gets min(share / price, 100) points, and points above 99 carry into the next grade. `ParkStaff.Train` now does the same (358 of 507 oracle inputs differed): a budget of 1,000 for 3 handymen (grades 0, 0, 4) trains the two at 333 each instead of 500, and a share below the price is still spent. ECON-013 is retired.
- The oracle executed the object sale refund (`0xe25e8` / `0xe2424`, ORACLE.md F21, ECON-025) and C# follows it: the
  refund is the base price `Upgrades[0].CostOfUpgrade` (no upgrade costs) times the current level's age percentage
  (exact 365-day years), and 100 % for an object no guest used in its first 30 days. A used Belly Bounce at level 2
  sold at two years refunds 175, not 490; an unused new one refunds its whole price. The new
  `ParkObjectState.TimesUsed` counter (raised when a guest leaves, imported from a save's customer count) is saved and hashed, so the
  hash schema is 11 and the determinism pin re-pinned (behaviour unchanged);
  the Windows `tp.exe` counterpart is not executed yet.
- The oracle executed the staff vitals, rest and strike routines (F22 in [ORACLE.md](reverse/ORACLE.md), ECON-014: the old claim "happiness never
  changes, no strikes" is refuted). Staff energy and happiness (singles, start 100) fall every park turn while a member walks (0.012 / 0.005 x (6 - grade))
  or works (0.025 / 0.01 x (6 - grade)) and rise while it rests at a Staff Room (the one `ChillsYouOut` object; 0.2..0.75 energy and 1..3 happiness a turn);
  without a room a tired member loses 2 happiness one turn in 16. At a month end of an open, guest-free park a role of more than three whose average energy or
  happiness is below 15 is warned, then strikes (TAG_SYSTEM 38-62, no dispatch or research until the next month end). C#: `StaffVitals`, `StaffStrikeBoard`,
  saves carry energy and the strike board; the oracle agrees on all four targets. Open: the staff state machine, the handymen's litter statistics and the stamp epoch.
- The x86 oracle now executes the coaster rating step and final of both Windows executables: tp.exe `0x43dc50`/`0x43df90` and the
  Theme Park Inc `Game.exe` `0x480630`/`0x4809c0` (ORACLE.md 7.8, findings X11 and X12). The final is the Mac rule (clamps, 1.7 and 1/2 weights,
  both-flags section count) with only last-bit differences in the excitement. The step differs in one rule: where the inverted arm does not apply the
  Windows builds add `u` (the single 1 + U), the Mac the stale `Fz` that C# follows, so 469 of 509 Mac-target inputs differ; the rest is x87
  rounding (at most 3 ulps), and the two Windows builds round differently from each other. A TPI coaster mode reuses the layout and the C# machinery but needs that
  one term and a rounding decision. No C# change.
- The x86 oracle now executes the Windows sale refund: tp.exe `0x4e2400` with its percentage `0x4e2290` and the clock stamp `0x4f8690`
  (ORACLE.md 7.9, finding X13). It is the Mac rule of F21 that C# follows, identical on all 572 inputs (random 64-bit clock bases included):
  base price × the current level's age percentage / 100, 100 % in the first 30 days of an object nobody used. The use counter is thing
  `+0x1a0`, cleared by the constructor and raised when a guest leaves (`0x4e1690` from `0x4fd970`, the one increment a byte scan finds), as on the Mac. ECON-025 is retired;
  no C# behaviour change. The routines were located with REA (a Ghidra MCP front end, local only) from the 365- and 30-day constants.
- The x86 oracle now executes the queue re-evaluation tp.exe `0x501390` (ORACLE.md 7.10, finding X14, QUEUE-005): a queue edit sends
  away only guests whose place is at or beyond 4 × the queue cells, and never one in state 14. "Queue edited underneath me" turned out
  to be the log line every guest prints, not a second exit. C# now also sends away a non-called guest in state 13 beyond the room; it
  keeps its Confused departure for a guest whose cell left the queue (its walk cannot route that guest back), which QUEUE-005 now names
  as the remaining difference. Found in the REA research round over the register (2026-10-13).
- QUEUE-001 is retired: tp.exe's run-time neighbour table (`0x4d97e0`) maps the links 1, 4, 16, 64 to −Y, +X, +Y, −X, and the
  queue's predecessor search `0x4de670` reads the link byte +13 with it, as C# assumed. QUEUE-015 is narrowed: the original's cell clear
  `0x5367a0` removes only the one queue cell (refunded, neighbours unlinked); C# still removes every cell behind it.
- QUEUE-008 is specified from tp.exe, without a C# change yet: every use of a toilet lowers its gauge by 0.05 × the guest's toilet
  meter (`0x4e2440`); handymen with no litter in reach walk to the nearest dirty toilet and clean it back to 100 after a per-grade
  time (`0x4d7880`, state 11, `0x4dfd80`). Wear alone would leave toilets dirty for good, so it waits for the handyman's toilet job.
- Park points now apply the queue part of the original's counts-test (tp.exe `0x4dd920`, ORACLE.md 7.11, X15, ARRIVAL-003): an
  attraction counts only with queue cells from its entrance and a queue shorter than 4 × those cells, so a ride without a queue, or with
  a full one, adds no points and draws no arrivals. Only the coaster controller test and the kart/water word remain open.
- ECON-052 is retired: the recent-visitors golden ticket now follows tp.exe `0x4c94d0` / `0x4d4bc0`. It takes the lifetime visitor
  counter minus its snapshot at the month close RecentVisitorMonths back, so the open month counts and one more close is needed.
  It also needs the park clock to be more than that many exact 30-day months old; both comparisons are strict.
- QUEUE-002 is retired: the queue's predecessor search (tp.exe `0x4de670`) tries the neighbours −Y, +Y, +X, −X (links 16, 1, 64, 4),
  and `RideVisitorBridge.NextQueueCell` now uses that order instead of the grid's direction order.
- ARRIVAL-001 is narrowed: the original's arrival update (tp.exe `0x4cf3e0`) runs once per world turn (`0x516380`) and drops one guest per
  turn while the bus stands at its stop. OpenTPW's guests therefore step off one per park turn from the turn after a group starts,
  instead of every 0.4 s. Only the bus's drive to the stop (its animation) is missing. Three behaviour pins moved: the fixed-run hash,
  the cross-grid run (62 → 60 admissions) and the Jungle run (23 → 22).
- QUEUE-006 is narrowed: queue places follow tp.exe `0x4de840`. The lateral byte is no longer mirrored for queues whose front lies
  towards +Y or −X, and the fourth place in a cell takes the axis of the cell behind it (`depth − 128` for links towards +X). Only the back
  cell's fourth place still depends on OpenTPW's join cell.
- Guests now test prices as the original does (tp.exe `0x4fde50`, ORACLE.md 7.12, X16, GUEST-003). They no longer skip an attraction
  they cannot afford when choosing. When called forward at a shop or sideshow, a guest compares the price with a willingness to pay
  (InitCostOfGoods × 1.15, raised by thirst, hunger and unhappiness against the type's effects, lowered by illness, plus the expected
  prize for a sideshow, scaled by happiness) and with its money, and leaves the queue when either is too low. Pricing a shop too high
  now costs customers at the counter. The C# rule is pinned on willingness values produced by the original routine.
- CI runs each pull request once: a push builds only `main` and the `v*` release tags, so a push to a PR branch no longer starts the
  whole build twice, and a new push to a pull request cancels its older run (`.github/workflows/build.yml`).
- Thought bubbles are drawn, and staff think too, as in the original (tp.exe `0x50be80`, `0x506a40`, `0x4fa030`; GUEST-006, QUEUE-005;
  [GUESTS.md](GUESTS.md) "Thoughts"). Guests and staff show the original bubbles above their heads (a burger for hunger, a drink for thirst,
  a thumb up after a sideshow win, a sleeping face for a tired handyman, a protest sign for a striker). Staff think when unhappy, very happy,
  tired, on strike or lost; a guest sent away by a queue edit is angry one time in three, as in the original.
- Guests now think as in the original (tp.exe `0x4fc8a0`, `0x50be80`, `0x50be40`; GUEST-006, GUEST-009 narrowed, ARRIVAL-006;
  [GUESTS.md](GUESTS.md) "Thoughts"). On one needs phase in ten a guest picks from its needs, the litter around it ("Too messy", new) and its
  happiness, and keeps its thought otherwise. Rides and shops no longer set "Pleased" or "Dissatisfied" (only sideshows do), and the
  invented thresholds ("Super happy", "Okay", level 70) are gone. Each thought times a bubble: 12 turns, and a gap of 0 to 60 turns before
  the next by the thought's priority (not drawn yet). The fixed-run, Jungle and cross-grid hashes moved; guest snapshots move to schema 7.
- Toilets, security cameras and fireworks now act on the cells around them, as in the original (tp.exe `0x4db090`, `0x4dd0a0`,
  `0x4e2440`, `0x4dfd80`, `0x4e0b90`; GUEST-006, GUEST-001 retired; [GUESTS.md](GUESTS.md) "The environment grid"). A clean toilet makes
  guests nearby a little ill and less hungry, and more so once it is dirty, until a handyman cleans it. A camera spreads security over five
  cells, so a prank there brings a guard. A Firework Show or Laser Show cheers guests up around it and changes how attractive the rides
  beside it look, until its show has run. All eight original region sources are now modelled.
- Balloons and costumes are drawn, as in the original (tp.exe `0x4fa030`, `0x4f9f00`, `0x4fe950`; GUEST-017; [GUESTS.md](GUESTS.md)
  "Rendering"). A guest in costume walks in the theme's costume (Jungle's tiger, Fantasy's knight, and so on), and riders in costume show
  the costume head where the theme has one. Balloons in four colours hang over their guests, trail behind and dip while they walk, bob, and
  show their burst frame when they pop. The texture pack builder now includes the balloon and costume sprites, and
  `--debug-guest-looks` dresses guests for captures.
- Guests now buy balloons and costumes, as in the original (tp.exe `0x4fe1e0`, `0x501650`, `0x501db0`; ECON-044, GUEST-007;
  [GUESTS.md](GUESTS.md) "Balloons and costumes"). A balloon shop gives a balloon that lives 127 needs phases (about two minutes) at the
  build's quality of goods; a ride puts it away and gives it back. A costume shop puts a costume on, and a second visit returns it. Balloon
  pranksters now find victims: they burst another guest's balloon on their cell. The challenges "kids with balloons" and "kids with
  costumes" count real guests instead of 0. Balloons and costumes are not drawn yet. Guest schema 6, world hash schema 17; the fixed-run,
  Jungle and cross-grid hashes moved.
- The player can call a mechanic and draw patrol areas, as in the original (tp.exe `0x4dfe30`, `0x4b5b50`, `0x524960`, `0x506f30`;
  ECON-049, ECON-022; [WANDER-plan.md](reverse/WANDER-plan.md) §4). The ride panel shows the original call button (`b_callmech`, or
  `b_callhandy` on a toilet): a call closes the ride and the nearest free mechanic comes even when nothing is broken (a toilet's call brings a
  handyman from anywhere); the repair reopens it, and a second click cancels the call (greyed with no mechanics or on strike, UIHELPTEXT
  574/575). The employee view of the staff window has "Patrol area" and "Whole park" buttons (UIHELPTEXT 86/87): two clicks on the map
  set the rectangle, and staff then keep to it, walking into it first when they stand outside.
- Mechanics now walk to their rides as in the original (tp.exe `0x4da490`, `0x4da5b0`, `0x4daa90`; ECON-021, ECON-022;
  [WANDER-plan.md](reverse/WANDER-plan.md) §4). In a level a mechanic idles or patrols and, from where he stands, takes the nearest ride
  that is broken down or being upgraded (not the park's worst one), walks there and starts the repair (`(100 − state of repair) ×
  WorkDuration / 100` turns) or the upgrade only when he arrives. Rides merely worn are no longer serviced before they break, as in the
  original. Every staff role in a level now runs its own state machine.
- Researchers now wander and research as in the original (tp.exe `0x5029f0`; ECON-022): a spell of `WorkDuration` turns where they stand
  (cat_staff 138 now and then), then they wander on; their points still come every 20th turn. Every staff role a level drives now walks
  to rest and to strike (`0x506a40`, `0x505fe0`, `0x5061d0`, `0x506300`; [WANDER-plan.md](reverse/WANDER-plan.md) §4): a tired member
  walks to the nearest Staff Room and rests there until its energy is full (the room's `VAR_STAFFIN` counts it), and while its role
  strikes and the gates are open it walks out past the ticket booth to the strike area and back in when the strike ends. Without a
  rest area a tired member loses happiness now and then, as before.
- Entertainers and guards now walk the park as in the original (tp.exe `0x4d4810`, `0x4d6410`, `0x508a30`; ECON-022, GUEST-006, GUEST-007;
  [WANDER-plan.md](reverse/WANDER-plan.md) §4). An entertainer patrols and, when guests stand within his `ActivationDistance` (3 to 5
  cells), performs for `WorkDuration` turns (cat_staff 135 at the end); wherever he stands he cheers the cells around him (`RegionFX[0]`).
  A guard spreads security (`RegionFX[3]`) around him; a prank within three cells of a guard sends the free guard nearest the staff HQ
  (map cell (0, 0); x86 finding X24, ORACLE.md 7.18), who chases the prankster (136), catches it (137), carries it to the gate and sets it down there (cat_kids 128); as in the original, a
  prankster that still wants to stay walks back in.
  Their idle and walking voices come from their state machines (AUDIO-024 narrowed); the park save keeps their state, target and rows.
- A guest's litter going into a bin now fires the bin's script trigger (variable 0, `VAR_TRIGGER` in every theme; tp.exe `0x4fff20`,
  `0x55a0b0`), so the bin plays its own sound, and a stinkbomb plays `cat_kids` 207 at the prankster (`0x4ff289`).
- Handymen now walk and search as in the original (tp.exe `0x4d73c0`, `0x4d7100`, `0x4c8ed0`; ORACLE.md 7.17, X23; ECON-022;
  [WANDER-plan.md](reverse/WANDER-plan.md) §4). In a level each handyman patrols the paths, looks for litter within his DetectionRange
  (2 to 5 cells by grade) and, only when there is none, for a dirty toilet in reach; he walks there, faster than on patrol, and sweeps or
  cleans for WorkDuration turns from his arrival. An idle handyman looks again every IdleDuration turns. His idle and walking voices come
  from his own state machine (AUDIO-024 narrowed). The park save keeps his state and walk. Rest and strike still need no walk.
- Pranksters prank, as in the original (tp.exe `0x4fb114`, `0x4ff0f5`, `0x4d8480`; ORACLE.md 7.16, X18 and X22; GUEST-007, GUEST-006;
  [WANDER-plan.md](reverse/WANDER-plan.md) §7). `PrankeryLikelihood` guests in 100 (5 in the shipped balance) are pranksters for life; when
  unhappy they drop stinkbombs or litter on their cell and cheer up a little. Vomit and stinkbombs now spread their `RegionFX` on the
  cell environment grid: guests standing in a stinkbomb's cloud lose happiness and grow ill. Balloons, the advisor's prank lines and the
  guards who chase pranksters are not modelled yet. Guest schema 5, world hash schema 16; the fixed-run, Jungle and cross-grid hashes moved.
- Litter now lies on map cells, as in the original (tp.exe `0x4d93b0`, `0x500dc0`, `0x4fff20`; ORACLE.md 7.15, X20 and X21; ECON-020,
  GUEST-005; [WANDER-plan.md](reverse/WANDER-plan.md) §6). A purchase fills the buyer's litter meter instead of a park-wide count; at 90
  the guest walks to a litter bin less than 3 cells away or drops its litter on its cell, and vomit stays where the guest was sick. A
  cell holds one item, and only handymen remove it: litter comes before toilets, and a free handyman sweeps the first unclaimed
  littered cell for WorkDuration turns (still without a position, ECON-022). The handymen strike when more than a fifth of the path is
  littered. Saves keep the littered cells and the guests' meters (guest schema 4; older saves load clean), the world hash schema
  moves to 15, and the fixed-run, Jungle and cross-grid hashes moved.
- Guests now decide and wander as the original does (tp.exe `0x4fec90` and `0x4f9490`, ORACLE.md 7.14, X18 and X19, GUEST-010;
  [WANDER-plan.md](reverse/WANDER-plan.md)). Once per park turn a guest standing on the paths (state 6) cheers, vomits, leaves, makes a
  major choice at most every 30 turns, or picks a wander destination one to five cells away and walks there (state 7); the 3-second
  decision timer and the one-cell wander are gone. A choice that finds nothing costs a little happiness, so a park without rides empties.
  The leaving test runs in state 6 only: a guest that walks through the four turns its counter reads 0 stays. Saves move to guest schema
  3 (schema 1 and 2 still load), the world hash schema to 13, and three behaviour pins moved (the fixed-run hash, the cross-grid run's
  hash with 60 admissions, the Jungle run with 23). The x86 sandbox gives routines with a frame handler an `fs` segment.
- ECON-015 is retired: tp.exe's researcher update `0x502960` skips display states 3, 4 and 5 of `0x5064f0`, which maps the internal
  staff state to the STAFFSTATES names (Idle, Patrolling, Working, Resting, On strike, Picked up); C#'s StaffState is that index, and its
  exclusion of Resting, OnStrike and PickedUp is the original's. GUEST-007 is narrowed with the prank rule of the state-6 handler.
- The Escape pause menu now stops the park simulation, as the original's does: on the Mac, Escape in the park (`0x157968`, key 27)
  calls `0x198514(0)`, which builds the pause menu `0x197d90` and then calls the game pause `0x110518` (`0x198580`) while the
  game-state pause gate `+0x3c` is set; the menu's close routine `0x1985c0` re-arms the gate and calls the resume `0x110598`. The
  pause freezes the scaled clock (`0x10e888` → `0x117c54`) that drives the scheduler's substeps and park turns, together with the
  advisor's clock. Before, OpenTPW froze only the advisor and a front-end smoke run saw the park date move three days while the
  menu was open. `ParkHud.TimeScaleFor` returns 0 while the pause menu (or a screen opened from it) is on the stack, and the speed
  setting is left alone, so the park resumes at the same speed (`ParkPauseTests`, front-end smoke check). UI-096 names the
  original's other pausing screens (full-screen map, message dialog, Load dialog, `0x15f67c`) that do not stop the park here yet.
- Toilets now wear and handymen clean them (tp.exe `0x4e2440`, `0x4d7880`, `0x4dfd80`, ORACLE.md 7.13, X17, QUEUE-008). Every visit
  lowers a toilet's gauge by 0.05 × the guest's toilet meter. Below 25 the toilet is dirty, and guests in its queue give up (the worn-toilet
  exit now fires). A free handyman takes the dirtiest dirty toilet and is busy for WorkDuration + 1 park turns, after which the toilet is
  back at 100. A toilet visit also clears an illness above 90. The walk to the toilet and the litter-first rule wait for handyman
  positions (ECON-022).
- The oracle now executes the Mac park calendar and the cadence of park turns (finding F30, ECON-001/002/003, [ORACLE.md](reverse/ORACLE.md)):
  the conversion `0xe4394` (2000-01-01 + floor(turn × rate / 4) s through the civil calendar, 652 inputs around every month, leap-day and year
  boundary), the update `0xe3f0c` (a day, month and year message when the date's day, month or year differs from the stored one, 579 scripts) and the
  scheduler loop `0x1c22b0..0x1c24d0` (31 ms substeps, a park turn on every eighth, three at most per call, a 2000 ms backlog dropped, scaled time =
  raw ms × the clock scale). At the default scale that is 4.03 park turns a second: a game day takes 5.7 s of real time, January 177 s, the leap
  year 2000 34.9 minutes, as OpenTPW already had it. `ParkCalendar` gains `DateAtTurn`, `FieldsAtTurn` and `Update`, and `ParkEconomy` raises
  its day, month and year events through them (no change for players; the oracle's C# side agrees on every turn). The original's first update
  of a new calendar also posts a day and a year message (stored day -1, year 0); OpenTPW starts synchronised. ECON-001 stays open with a narrower
  text (the 60 Hz sampling, the catch-up limits, the clock route).
- The oracle executed the golden-ticket check cadence (finding F31, ECON-033, [ORACLE.md](reverse/ORACLE.md)): the world's park-turn update `0xd67f0`
  runs the bank, the staff pool and the calendar update, then, on every turn that is a multiple of 100 (unsigned, 4.34 game days, 24.8 s at normal
  speed) and for game type 0 only, the ticket dispatcher `0xd2f1c`. `ParkEconomy` had the cadence and the mode (507 scripts agree, 0 mismatches); it
  ran the check before the calendar update, so the year's profit ticket could be awarded from the old year's profit on the turn the year changed, and now
  runs it after (`ParkEconomy.RunsGoldenTicketCheck`). The determinism hash did not change.
- The oracle executed the original's speed scale (finding F32, ECON-004/UI-022, [ORACLE.md](reverse/ORACLE.md)): three key callbacks divide the scheduler clock's
  scale by 1.25, multiply it by 1.25 and store 1.0, clamped to 0.25..2.0, and the clock that park turns, ride scripts (WAIT, GETTIME), the board code and the
  animation updater read (60 call sites; 11 read the unscaled clock) adds each raw gap times the scale. OpenTPW's speed buttons sped up the economy alone
  while guests and rides kept x1; a speed is now the clock scale of the whole simulation (`GameSpeedScale`, `Level` feeds the fixed clock scaled time, the economy
  runs one tick per fixed tick). At x2 a park day takes 2.9 s of real time (x1 5.7 s, x4 1.4 s) and guests, rides and the economy run at x2 together; x4 stays an
  OpenTPW extension beyond the original's maximum scale 2.0. The determinism hash did not change. ECON-004 and UI-022 stay open with narrower texts (the
  keys' names, whether a speed control is shown).
- Guests choose attractions with the original's score (ORACLE.md finding F24, `GUESTS.md` "Attraction choice"). The oracle now executes the Mac score `0xe9164`, the choice loop `0xe8ff4` and the use outcome `0xea318`, and C# follows them (`AttractionScore`, `AttractionChoice`, `RideOutcome`): seven matches normalised by the sum of all seven `DecisionVar*Weight` values, so a perfect ride for a guest without needs scores 27 (not 100) and a new one (up to 7 days old) 135; an attraction of the type just used scores 0, one the guest was turned away from a fifth for about 20 s; the best score of at least 10 wins with no random bonus; a use gives `PerfectRide` below 5, `GoodRide` below 15 and `OKRide` below 40 from the preferred excitement (no penalty beyond) and raises illness by excitement / divisor times a hunger factor. The loader's binding of the seven weights is read from the schema order and four independent consumers; the 11 × 11 need table is a formula (checked on all 121 entries), the 21-byte toilet curve is written as constants. The guests' used and temporary histories join the snapshot (schema 2) and the determinism hash (schema 11, re-pinned). New register IDs `GUEST-001..005`.
- Guests start and grow by the original's rules (ORACLE.md finding F25, `GUESTS.md` "Needs and starting values"). The oracle executes the Mac needs function `0xeece4` and the constructor `0xe7644`; C# follows (`NeedsUpdate`, `GuestStart`, `UseEffects`): a new guest has happiness 50, thirst and hunger 0..49, toilet 0..29 and an exit counter of 60..179 that loses one per four park turns (0.992 s) in every state; the needs change only on the guest's phase (turn & 3 == id & 3), toilet +1 and hunger and thirst +2 only on a turn that is a multiple of 16, so only a guest with an id divisible by four ever grows (three in four keep their starting needs); a need at exactly 100 costs a happiness point per phase; illness does not decay; a use applies the type's thirst, hunger, vomit and happiness effects (hunger 80 at a shop with effect 50 goes to 30, not 0). The invented per-second rates and the 60 starting happiness are gone. New register IDs `GUEST-006..009`; the determinism hash is re-pinned.
- Guests leave by the original's rule (ORACLE.md finding F26, `GUESTS.md` "Lifecycle"). The oracle executes the leaving test of the Mac state-6 handler (`0xebdfc`): a guest goes home when its happiness byte is 0 (below 1, not "at most 10"), when its exit counter is exactly 0 or when the park is closed, losing `BigHappinessChange` on the way; the counter counts one per needs phase (0.992 s, the `.sam`'s "seconds") in every state, and a ride does not extend a visit (nothing in the binary adds to it). Closing the park now sends every walking guest home; a guest that is queueing or riding while its counter reads 0 never leaves by it. The starting happiness (50, F25) replaces OpenTPW's 60. New register IDs `GUEST-010` and `GUEST-011` (the closure also pulls guests out of queues and drops `GoingToRide` reservations, or a closed park would strand every guest whose ride holds admission; what the original does with queued guests at the closing word is not traced).
- Review round on the guest batch (Codex). Four findings fixed: the attraction score now measures the guest against the bridge's **last queue cell** (`0xdd3f4`), not its join cell — the queue term's d² ≤ 8 range and the distance match both read the back cell, so a queue the guest stands beside actually counts (a unit test pins a score of 19 where the join cell would give 16); **world saves written by guest-snapshot schema 1** (before the histories and the integer exit counter) load again — the schema gate accepts {1, 2}, the missing histories start empty and the former seconds-based exit value truncates toward zero into the counter, while a schema-2 save that lost a history or carries a non-finite exit value is rejected by both the envelope's validation and the simulation's; closing the park pulls **queued guests out** of queues and drops `GoingToRide` reservations (`GUEST-011`); the guest-slice validation checks the histories (null only under schema 1) and bounds the exit value. The x86 oracle's guest targets also load again (a double execution of `targets.py` under the `ppc_oracle_targets` module name left a half-initialised `targets_guests`; the file now aliases itself into `sys.modules` as `targets` before importing the guests module).
- Second review round on the guest batch (Codex). Five findings fixed: schema-1 saves whose hunger, thirst or toilet grew past the counter model's ceiling load again (the bounds accept the old model's 150 cap and the restore clamps to 100) and a schema-2 exit counter must be whole; the closure pull-out also covers a guest called to board (`Boarding`); the **state-18 recall** (`0xedc98`: counter above 0, happiness byte nonzero, park open) is implemented — a closed-park dismissal unwinds when the park reopens, with the untraced cell predicate `0xe6c6c` approximated by the state boundary (new register ID `GUEST-012`, the road ends the recall and the booth refusal is exempt); and the shop-sale oracle's C# meter comparison rejects a short or empty answer instead of passing vacuously.
- The oracle now executes the Mac ride's life from breakdown to repair (finding F27, ECON-049, ECON-024, ECON-023, QUEUE-019): the ride update `0xe077c` skips closed, broken-down and inert rides, `SetState` `0xe0c3c` writes the open word and VAR_RIDECLOSED together, the reopening guard `0xde654` reads the ride's state (not `VAR_BROKEN`), and a repair `0xdef2c` writes 100.0 into the wear gauge, clears the reservation, maintenance flag and VAR_BREAKSTAT and opens the ride (the old rule differed on 264, 280 and 423 of about 500 inputs per target). OpenTPW now keeps a broken-down, upgrading or incomplete-coaster ride closed when the park door opens, no longer lets a closed ride break down or a broken ride wear, runs the admission on the breakdown turn and reopens a repaired ride. After the rebase on the F19 repair timer, mechanics follow the derived ride state as the arrival `0xd9c0c` does: a condemned ride (life gauge 0) gets no mechanic (it was repaired and broke down again for ever), and a broken ride with a purchased upgrade is repaired before the upgrade is installed. `ECON-049` and `ECON-023` keep only the maintenance button, the queue link test, relief objects and a breakdown that only the script reports.
- The Codex review of PR #64 closed three seams: the park door and the reopening after a repair followed the economy that an in-place `LoadPark` had discarded (they now subscribe through the persistent runtime and the gate is rebuilt), a ride with a bought upgrade kept taking guests while its wear stood still (it now closes until the upgrade is installed and then reopens, `0xdf928` start and end, read statically), and the docs no longer present the economy's breakdown as reaching guests: it does not, until the economy writes VAR_BREAKSTAT to the ride script. RIDE-WEAR.md drops its superseded 2-second wear estimate and the retired ECON-047 row.
- The oracle now executes what a Mac staff member costs (finding F28, ECON-012, ECON-043): the monthly wage `0xf3364` debits PayMultiplier × BaseWage (a 32-bit product) at the month's end, and the dismissal routine `0xf33ec` charges the same wage once more; the hire path has no debit (the bank's Spend has sixteen direct callers, none on it, and `BaseCostPerStaff` / `CostPerQualityLevel` are never read). `ParkEconomy.Fire` now charges a month's wage (it was free: 506 of 511 oracle inputs differed), hiring stays free, and ECON-012 is retired.
- The oracle now executes the Mac park rating `0xc7b24` and park value `0xc1d4c` (finding F29, ECON-027, ECON-026): objects count in the rating by the type's `Info.WhichUIType` (the add-ons are rides), the visitor term is the census of guests on cells of type 0, 1, 3, 9 and 10, and a staff member counts only with its byte `+3` clear; the park value is the cell points (2 per owned empty cell, ...) plus, per object that is not condemned, the costs of its levels times its state of repair / 100, not the scrap values. `ParkEconomy` follows the object parts (the old rules differed on 249 and 355 of about 520 oracle inputs); the census, the staff byte and the cell points stay open in ECON-026 and ECON-027.
- [Fidelity register](FIDELITY-REGISTER.md): **326 original fidelity items** plus
  **24 online extension** IDs (350 total) remain open. CI checks explicit tags and register consistency; it finds no unmarked logic and proves no parity.
  Windows/Linux GPU/input/audio, original captures, coaster controllers, missing
  script effects, and further scenario acceptance remain release blockers.

Local logs: `/tmp/opentpw-root-final-tests.log`, `-final-ui-tests.log`,
`-final-release-build.log`, `-final-frontend-smoke.log`, `-final-visit.log`.
The other sections below keep earlier slices and the checks they had at the time.

## Delivered and verified

- Native macOS arm64: SDL2 windowing, Metal rendering, ImGui initialization, and
  the original Jungle ground texture and Totem model. No Rosetta in this test.
- Placing, starting, procedural motion, stopping/resetting, removing, and placing
  again; isolated JSON save/load of a running attraction and an empty park.
- Reproducible native smoke test of 150 frames with GPU readback. Checks non-black
  images and texture detail; capture without UI, not an original fidelity test.
- Portable file paths and asset tests; original files remain outside git.
- SPIR-V compiled once with resource names preserved; the same bytecode feeds
  backend shaders and reflection. MSL/HLSL/GLSL bindings have native regression
  tests.
- Own material pipelines use Improved resource binding. Legacy Metal counted
  vertex buffers into uniform bindings, while SPIRV-Cross expected buffer 0.
  ImGui keeps its existing binding contract; culling is unchanged.
- Fog uses view-space distance instead of projected coordinates. The chosen fog
  distance is a sandbox setting, not a reconstructed game value.
- Engine content is included in build/publish, and shader paths are relative to
  the executable. The native smoke test passes when started from `/tmp`, outside
  the repo.
- Framework-dependent packages build for `osx-arm64`, `win-x64`, and
  `linux-x64`. These are local development packages, not qualified releases.

## Simulation foundation: limited M2 slice

The prototype attraction runs on a chosen 60 Hz fixed-step clock, separate from
render frames. CPU tests compare 30/60/144/240 fps with the same 360 ticks and
the same final phase and height. Frame delay causes at most 16 catch-up ticks;
extra time is explicitly counted as `DroppedSeconds` and is not caught up
without limit. Renderer timing uses a monotonic stopwatch instead of the system
clock.

This only proves consistency of our prototype motion. It does not prove the
original tick rate, RSE execution, or bit-identical full simulation on all
platforms. RNG, event replay, and full tick/RNG save state are still missing.
The current sandbox save format restarts phase and clock when loading.

## Fresh verification

- Whole native asset suite, including shader, save container, and font
  regressions: **185 passed, 0 skipped, 0 failed**, .NET SDK 8.0.425, `osx-arm64`.
- Without original assets or native shader opt-in: **165 passed, 20 skipped,
  0 failed**.
- Save container slice: **31 passed**, including the hashed original TPWI fixture.
- Isolated clock tests: **12 passed**.
- Release solution build: **0 errors**; existing warnings remain.
- Native Metal smoke test: exit code 0, 150 frames, original assets, save/load in
  a temporary directory; the user's sandbox save remains untouched.
- Three RID publish builds passed; rerun after every source change.
- Republished Mac build started from `/tmp`: read-only save inspection and native
  Metal smoke test both exit code 0. An invalid or truncated save file and a
  missing inspection argument give exit code 1; no original files changed.
- `git diff --check` should remain clean.

Reproduction steps are in RUNNING.md. Local logs are under `/tmp/opentpw-*.log`;
GPU captures and publish output are under the ignored `artifacts/`. Captures
contain original assets and are not distributed as repository fixtures.

## Still open / no completion claim

- M0: complete native and transitive dependency and distribution license
  inventory, parser boundaries, edition identification, and all required asset
  fixtures.
- M1: human and automatic UI input qualification, camera/zoom/focus, and full
  lifecycle and resource check. Offscreen captures do not show the buttons.
- M2–M6: original map/save import, path network, visitors, queues, economy,
  staff, all rides and coasters and RSE opcodes, worlds/scenarios, progression,
  audio/video, localization, and original behavior references.
- M7: actually running on Windows/Linux, packaging and licenses, long sessions,
  complete native dependency evidence, and a supported release runtime policy.
- Existing code warnings have not been cleaned up. No dependency upgrades without explicit authorization.
- Subagent follow-up was blocked by a revoked refresh token. Work continued
  locally; independent architect or review sign-off is not claimed.

Next substantive gate: investigate the remaining MAP/TPWS fields (see
TPWS-PAYLOAD.md) and a legally runnable original offline behavior reference. No
copy-protection bypass and no fictitious simulation rules as a substitute for
evidence.

## Design extension: optional upscaling

Added at the user's request to M6, the PRD, and the test specification on
October 9, 2026: `docs/UPSCALING-DESIGN.md`. Native/100% remains the default;
portable Linear/Nearest with an adjustable world resolution comes first, with
the UI at output resolution. Simulation, save, and replay remain untouched.
Vendor and temporal upscaling and dynamic resolution only come after separate
capability, license, input, and performance evidence.

Save and map research remains the next gameplay gate; upscaling does not move
this critical path.

Implementation M6-U1 + high resolutions (October 9, 2026, worktree `display`):
arbitrary window sizes (`--resolution`), borderless/exclusive fullscreen
(`--fullscreen`, `--fullscreen-exclusive`, Alt+Enter/F11), HiDPI drawables,
Native/Linear/Nearest with presets 77/67/59/50 and custom 50–100 (`--upscale`,
`--render-scale`), integer BF4 UI scale, user settings in `display.json`, the
ImGui section "Display", and the UI-agnostic `OpenTPW.IDisplaySettings` API
(with confirm/revert). Only the 3D world scales; BF4 text, ImGui, and movies
render at output size.

Evidence (macOS arm64/Metal only, 1x display 1920×1080): CPU tests for size,
rounding, fallbacks, DPI, picking, and configuration; native smoke for the
sandbox and `--load-original-level jungle` at 1280×720, 1920×1080 borderless
(67%), and 2560×1440 output via `OPENTPW_TEST_PIXEL_SCALE=2` with 50% Linear
(world 1280×720, UI scale 2, BF4 text exact, picking correct), plus runtime
scale, window, and fullscreen switches.
Not proven: real Retina, exclusive fullscreen, Windows/Linux GPU, performance.
See `docs/UPSCALING-DESIGN.md`.

## Delivered save container slice

`SaveReader` now supports the observed offline version-133 TPWI envelope
alongside the old synthetic 500 header. Bounded binary intake and zlib decoding
replace one-shot reads and unbounded decompression; caller-owned streams stay
open and repeated reads work. Header, chunk, and decoded lengths, checksum,
stream completion, and trailing data are checked, with hard 64 MiB limits.

`--inspect-save` gives metadata and hashes without GPU or write access to
assets. The real Jungle Easymode file decodes to 1,608,309 bytes with the
expected SHA-256; corpus and offsets are in `REFERENCE-CORPUS.md` and
`SAVE-CONTAINER.md`. There is no park payload schema, playable importer, or
evidence for all original TPWS layouts. `MapFile` remains incomplete, and
`scape.omp` semantics have not been guessed.

Follow-up subagents could not deliver a review (a model/account restriction and
a blocked review call). Tests and local checks were run; independent review
sign-off and completion of the full game are not claimed.

## Focus on missing/partial formats

At the user's request, the ❌/⚠️ list takes priority. `FORMAT-BACKLOG.md`
separates concrete fixtures, decoder work, and integration/fidelity; existing
green marks are not interpreted as full game or platform qualification.

- New `FontFile`: bounded BF4 header/offset/glyph parser, raw four-bit,
  nibble RLE, and monochrome. All 33 original English fonts / 8,217 entries
  decode; three hashed fixtures pin metrics and independently computed samples.
- README: BF4 changed from ❌ to ⚠️. No GPU font atlas, game UI, or original
  visual comparison yet; no unfounded complete-font claim.
- `--inspect-font`: read-only CPU diagnostics without changing assets or
  starting the GPU. Published Mac build from `/tmp`: valid font exit 0, missing
  argument exit 1. Three RID packages rebuilt.
- BF4 text: deterministic glyph atlas (coverage × 17 → alpha), layout with
  original advance/offsets/line height, line wrapping, and `?` fallback; pinned
  on GAME8AA/SESHMED and UITEXT/OBJECT_NAMES. The sandbox panel draws "Totem" and
  ride labels; the Metal smoke test compares readback with the CPU composite
  (max. difference 0). No original UI screen or AA comparison; D3D11/Vulkan not
  run.
- Read-only metadata inventory of 312 DWFB archives: 2,118 MD2, 308 RSE, and
  7 MAP members. Jungle terrain contains `base.map` and `terrain.map`; this does
  not yet prove map semantics or import. Nine TGQ videos found. No standalone
  MTR/LIPS/TQI names in these locations; embedded or differently named data
  remains open.
- Upstream format notes were recovered via the GitHub docs source. MTR/LIPS
  remain TODO; the MAP note concerns sound maps, not proven terrain records.

Parallel format slice (7 formats, isolated worktrees, October 9, 2026):

- MD2: `ModelFile` rewritten as a bounded reader; 2,116 of 2,118 corpus members
  parse (838 geometry, 1,278 animation containers with opaque payload), and 2
  version 207.201 files are explicitly rejected. Unused stub
  `OpenTPW.Files/Public/ModelFile.cs` removed.
- MD2 animation: position (Bézier/linear), rotation (slerp with easing curves),
  and scale tracks decoded and validated against all 1,278 animations; node
  matrices are parent-relative. The sandbox Totem plays `totemm1.MD2` when
  `Totem.RSE` triggers ANIM_Main (30 ticks/s is our own choice); the smoke test
  requires changing nodes and pixels. Vertex animation and other record kinds
  remain undecoded ([MD2-MODELS.md](MD2-MODELS.md)).
- MAP: TP2M terrain maps (128×128 cells, 5 pinned fixtures); 64 sound catalog
  `.map` files distinguished and rejected. Cell meaning unknown.
- RSE: `RideScriptFile` + static analysis; all 308 scripts parse, 84 opcodes
  used, none unknown. `RideVM` now executes them (see [RSE-VM.md](RSE-VM.md)):
  33 opcodes fully in the VM, 51 via a hook that logs "unimplemented effect"
  (visitors, animation, sound, objects, ride controllers, park clock). All 263
  startable scripts plus 44 children run for 60 s without errors; the sandbox
  Totem runs `Totem.RSE`, which determines when the original `totemm1.MD2`
  animation starts. Slicing, milliseconds, and CRIT_LOCK are inferred, not
  traced.
- Visitors (approximated, [GUESTS.md](GUESTS.md)): children are original sprites
  (`esprites.wad`, ESP/FPC/TPC decoded, 8 sets × 175 frames, 5 directions plus
  mirroring). Fixed-tick, seeded simulation: arrival via bus stop and ticket
  booth (`FixedItemInfo`), entry, path finding with flow fields over the save
  connection bits, needs, choice using the `.sam` DecisionVar weights, queue, and
  the LETMEON/LETMEOFF host protocol plus visitor opcodes via
  `RideVisitorBridge`. The Totem fills with real visitors (start ≈3.3 s instead
  of 11.3 s) and lets them disembark again via HOP/WALKOFF/WALKGET. Speeds, need
  rates, arrival unit, and score formula are our own approximations. 600 visitors
  ≈0.06 ms per tick.
- TPWS/TPWI: 17 unique section markers located in the only fixture; content
  opaque. No other TPWI/TPWS file is on the ISO.
- LIPS: found as `.LIP` (639 in `lips.wad` + 4 level files); strict reader,
  unit (probably µs) unconfirmed.
- MTR: only 11 ISO files (`Meshes/<taal>/*.mtr`); structural reader, meaning
  and use by the game unknown. Tests via `OPENTPW_MTR_PATH`.
- TQI/TGQ: all nine movies; container and EA ADPCM audio (bit-exact against an
  external reference) and TQI video (integer IDCT, 56–61 dB, not bit-exact).
  Playback via `--play-movie <naam>` (SDL2 audio, audio clock, frame drop/hold,
  GPU, native smoke test); no in-game trigger (no evidence in the data).

Verification after integration: native assets + shaders **403 passed, 0 skipped**;
without assets 339 passed, 64 skipped; Release solution 0 errors; Metal smoke
test 150 frames passed. Windows/Linux GPU not qualified.

GitHub CI for snapshot `5f23094`: macOS and Ubuntu build/CPU tests pass. Windows
had one assert failure because of `UnauthorizedAccessException` instead of
`IOException` when replacing a directory. The test now distinguishes the
OS-specific exception type; preserving the directory and cleaning up temporary
files remain required. Windows re-verification is still needed for this change;
CPU CI is not GPU, audio, or full native release qualification.

Terrain/save slice (October 9, 2026, worktree `terrain`):

- MAP axes proven: file row = game X, column = game Y, cell = 10 MD2 units
  (Standard.sam fixed items, `bridge01` node, rasterization of all jungle MD2
  triangles).
- `base.MD2` 0x6C block decoded as a 96×85 heightfield: corners, holes (= MAP
  water, entrance, and fixed walkways in all four themes), and ground texture
  slots.
- Five MAP bits typed (`MapCellFlags`); bit 0x04 and header values remain opaque.
- Easymode.TPWI: per-cell grid (16,384 records, MAP byte identical to
  `base.map`, path/connections/occupancy) and SYSG object records (Info.Id → .sam
  names) decoded; read-only `OriginalParkImport` (78 path cells, 11 objects,
  3 fixed items). Money, time, and guests are not imported (not verifiable with
  one fixture).
- `--load-original-level jungle`: original terrain meshes and heightfield,
  imported paths and footprints, build rules on proven bits.
- After rebase onto `55b1b93` (RSE VM, BF4 text, TGQ, MD2 animation): native
  assets 479 passed, 10 skipped (opt-in MTR/native shader tests); without assets
  397 passed, 92 skipped. Metal smoke test passed for sandbox (script, animation,
  text, save/load) and `--load-original-level jungle`/`fantasy`.

## Languages (October 9, 2026)

- All five bundled languages (English, Danish, French, German, Swedish) decode:
  `.str` now uses `MBToUni.dat` from the language's own folder (Danish/Swedish
  248 characters, others 249). Previously 2,254/2,358 Danish and 2,268/2,358
  Swedish strings were garbled, plus 3 French (œ) and 1 German (š). The 24-bit
  string length is now read in full; UITEXT strings longer than 255 characters
  were truncated (English too). The character table is built once instead of
  per character.
- Language selection: `--language`, `OPENTPW_LANGUAGE`, or the `Language` setting;
  defaults to English if installed, otherwise the installed language. Other
  languages via `--language-data` / `OPENTPW_LANGUAGE_DATA`: a read-only,
  case-insensitive overlay of the extracted CD folders `<Lang>/data` and
  `<Lang>/Meshes`; nothing is copied to the game folder or git. Strings and fonts
  (Localization, sandbox panel) follow the choice; speech, LIP, and banners are
  only resolvable, not played or drawn.
- Tests: synthetic reader and resolution tests; with the CD data per language,
  21 string tables / 2,358 strings, pinned examples ("Gå Online", "Koppla upp",
  "d'œuvre", "Unfuhg Gibsniš"), glyph coverage in all 33 fonts per language, and
  LIP/banners. Native Metal smoke test passes in English and German (readback
  difference 0). Full suite: without assets 394 passed / 98 skipped; with
  `OPENTPW_GAME_PATH` 466 / 26; plus `OPENTPW_LANGUAGE_DATA` 482 / 10 (the 10 are
  the opt-in native shader and MTR tests); 0 failed.
- Open: no original menu/UI screens, no language switch during play,
  `UniToMB.dat` (text input) unread, other editions not examined. See
  [LANGUAGES.md](LANGUAGES.md).

## Park economy (October 9, 2026, worktree `economy`)

- Layered `.sam` settings (`levels/Standard.sam` → theme `Standard.sam` →
  `Easy_Standard.sam`; category → object → `Easy_` object files) with a source
  reference per value. Inventory of all economy, staff, research, and goal
  settings with meaning and use in [ECONOMY.md](ECONOMY.md).
- `ParkEconomy`: deterministic park clock (first day = 240 fixed ticks with 30-day
  months; now the traced 248 ms turns and civil calendar, see ECONOMY.md), pause/speeds, ledger with the original UI categories, entry
  price, shop and sideshow prices, build, upgrade, cell and land costs, scrap
  value, loans (LOANNAMES), bankruptcy after six months in the red (TAG_SYSTEM
  texts), staff pool, wages (BaseWage × PayMultiplier), training,
  mechanics/cleaners (approximated), research, challenges, golden tickets, and
  keys. Approximations are documented per line.
- Easymode.TPWI: loan table (8 × 32 bytes; repayment floor(amount/months) = 0%
  interest, hence `Easy_Standard.sam`) and challenge list (8 × 45 bytes = jungle
  `ChallengesInThisLevel`) found and checked. 87,987 / −12,013 remains unexplained
  (odd, while all build costs are multiples of 5); the balance therefore starts at
  `InitialCash`.
- Own versioned park save format (JSON, atomic, strictly validated) with clock,
  RNG, money, staff, research, and goals; round trip and identical continuation
  after loading tested.
- `--load-original-level <thema>`: economy runs on the fixed clock and logs
  date/balance per in-game day; the smoke test pays one month of wages and
  performs a park save round trip (all four themes passed, macOS arm64 Metal).
  Full suite: without assets 427 passed / 122 skipped; with `OPENTPW_GAME_PATH`
  519 / 30; plus language data 539 / 10; 0 failed.
- Open: visitor spending, staff behavior/strikes, advisor, HUD screens (other
  slices), and original formulas for day length, interest, park rating, and keys.

## Compatibility (October 9, 2026)

- Own TrueType parser/rasterizer reads the 17 fonts in memory from `fonts.wad`
  (identical to `tpwfnt`, pinned hashes, metrics, and raster hashes). `.sgn`
  (84 files) supplies two LOGFONT text lines per sign; the jungle gate shows
  "Lost Kingdom" in Young Itch AOE 144 px (the native smoke test requires text
  pixels). All sign characters in six languages have a glyph in all 17 fonts.
- `--cd-data`: read-only, case-insensitive fallback for missing movies and music,
  with startup diagnostics; synthetic minimal-installation test.
- Detail presets Low/Medium/High from `low/med/high.sam` + Enhanced (EXT);
  filtering/mipmaps to samplers, view distance to the fog; `IGraphicsSettings`,
  `graphics.json`.
- Profile Original (default) / Recommended / Custom with stable fix IDs,
  `CompatibilityFlags` save metadata, and hash-bound in-memory data corrections
  (megacost.sgn refers to a font that is not bundled).
- Robustness: missing strings → English → internal name (found UITEXT[473]
  outside every table and French [457] empty), damaged SDT entries skipped,
  `UniToMB.dat` read.
- Approximations are listed in the register in [COMPATIBILITY.md](COMPATIBILITY.md).

## Advisor/compat integration (October 9, 2026)

- Advisor `3a5107c` and compat `7065401` were merged into main `517c588` with
  history preserved, on `integrate-advisor-compat-20261009` (`b1eac76`). Guests,
  park economy, display settings, and their existing smoke tests remain present.
- The advisor used logical window coordinates directly on the world target;
  `--render-scale 50` therefore clipped the mouth outside the target. The
  viewport is now converted to target pixels, with three regression cases (100%,
  50%, HiDPI). The placement remains the registered approximation `ADVISOR-003`.
- macOS arm64 build: 0 errors. Full suite with original assets and language
  data: 689 passed / 11 skipped / 0 failed (700 tests, before the three viewport
  cases were added). Then the advisor subset including new cases: 17 passed;
  MTR/native shader subset with original language MTR fixtures: 41 passed /
  0 skipped.
- Native Metal/SDL advisor smokes: English `sp_001` (513 changed mouth pixels),
  German `sp_001` at 50% render scale (142 pixels), English with test HiDPI 2×
  (1,948 pixels). All three run through the audio queue and close the mouth after
  the clip.
- Native jungle smoke: gate text from the original TrueType font, Totem RSE with
  boarding and disembarking, guest sprites (3,194 readback pixels), BF4/output
  readback, monthly wage ($92), and identical park save round trip pass.
- The existing compiler and dependency advisory warnings are not resolved; no
  dependencies added. Windows/Linux native execution and comparison with original
  advisor and sign captures remain unverified. The registers in
  [LIPS.md](LIPS.md), [COMPATIBILITY.md](COMPATIBILITY.md),
  [GUESTS.md](GUESTS.md), and [ECONOMY.md](ECONOMY.md) still apply.

## Original UI: front end and HUD (October 9, 2026, worktree `frontend`)

- Evidence (UI.md): the 278 `ui.wad` models are written in a virtual screen of
  2048×1536 (full-screen frames span exactly 0..2048 × 0..−1536); HUD models sit
  at their screen position, and generic buttons/windows sit at the origin. Child
  nodes are alternative state frames (normal/disabled/highlighted/pressed), not
  parts; this explains the "uncomposed" bounds from MD2-MODELS.md. Texture V runs
  from bottom to top; pure pink is transparent. `lobby.wad` describes the 3D lobby
  (four islands, camera positions, sky color).
- The default start is now a front end in the original style: 3D lobby with the
  four islands, a menu with original buttons, texts, and tooltips
  (UITEXT/UIHELPTEXT), game mode, load, options, and quit; then the original level
  with a HUD (main panel, date, balance, build arm with categories and rotating
  preview models, info arm, messages, pause menu). `--sandbox`,
  `--load-original-level`, and a bare `--smoke-test` skip the front end as before.
- Options: original rows (resolution, volumes, pop-up help) plus OpenTPW rows
  (window mode, upscaling, render scale, interface scale, language) in the same
  style, with the original confirm/revert/restart messages. Missing labels come
  from our own supplementary table, translated into six languages. Display is
  handled via a stub of `IDisplaySettings` (display slice).
- Approximations (documented): positions of elements placed by code, font level
  per resolution, speed buttons, calendar and starting balance stub (the starting
  balance itself from `Easy_Standard.sam`), Totem price from `Totem.sam`.
  Interfaces for economy, catalog, and display are in UI.md.
- Tests: without assets 433 passed / 129 skipped; with `OPENTPW_GAME_PATH` and
  `OPENTPW_LANGUAGE_DATA` 552 passed / 10 skipped; 0 failed. Metal smoke test
  `--front-end --smoke-test` passed in all six languages (text checked texel by
  texel in readback); the existing sandbox, jungle, and fantasy smoke tests still
  pass.
- Open: original screen positions, profiles/online, cursor, button animations and
  sounds, comparison with original captures, D3D11/Vulkan.

## Original objects: rides, shops, sideshows, features (October 9, 2026)

- Catalog of all 274 objects of the four themes from WAD + `.sam` layers
  (Info.Id, OBJECT_NAMES name for 220, category, shape/footprint, raw cost and
  upgrade values, model, script, animations); 911 animations bound to their model.
- Shape, rotation, and entrance/exit verified against Easymode (0°, 90°, 270°);
  180° and non-square rotations follow from the same rigid rotation (derived).
- ANIM_* → file letter/variant (c,i,l,s,m,e,u,b,r,o) derived from file names and
  scripts; all 262 object scripts run for 60 s in one world without faults.
- Easymode import shows real models instead of orange markers; building,
  rotating, and removing each catalog object works in the sandbox and original
  levels (ImGui panel). The Totem prototype is now a thin wrapper; the native
  smoke test (both modes) shows Sun God, Crazy Ape, Tom Tom Twister, and Eruption
  animating.
- Official bonus objects (35 `_name_N.wad`) via `--bonus-data`/`OPENTPW_BONUS_DATA`.
- Not implemented: visitors (only `IRideVisitorBridge`), sound/EVENT/particles,
  coaster/kart/tour controllers, vertex animation, original build rules and
  costs. Details: [OBJECTS.md](OBJECTS.md).

## SDT sound effects: MPEG Layer I decoder (October 9, 2026)

- `Mp2Decoder` now also decodes MPEG audio Layer I (MPEG-1 and MPEG-2/LSF, all
  sample rates, mono/stereo/dual/intensity stereo, CRC skipped), the format of the
  sound effects and speech `z_error`. Layer II restrictions are unchanged.
- Verification without original files: 73 generated Layer I streams are the same
  length as, and differ by at most 1 LSB from, ffmpeg's `mp1` decoder. Unit tests
  cover silence, DC, intensity stereo, frame size/padding, forbidden allocation,
  and a stream pinned against ffmpeg.
- Tests: without assets 592 passed / 198 skipped; 0 failed.
- Open: run `OriginalLayerOneSoundEffectsDecode` with `OPENTPW_GAME_PATH` (the MPEG
  version and sample rates of the original effects are still unconfirmed); sound
  effects are not yet played anywhere in the game. Details: [LIPS.md](LIPS.md).

## Linux preparation M7 (October 9, 2026)

Measured in an Ubuntu 24.04 x64 container with .NET 8.0.425, Xvfb, and Mesa
llvmpipe (software Vulkan 1.4); without original assets.

- Fixed: Vulkan startup failed on every distribution with glibc ≥ 2.34
  (`vk` 1.0.25 loads `libdl`, but only `libdl.so.2` exists);
  `LinuxNativeLibraries` redirects this. The app then creates an SDL2 window,
  Vulkan device, and swapchain (1280×720).
- Fixed: case-sensitive paths. `BaseFileSystem` corrects existing segments to the
  spelling on disk (exact match wins); the data folder is found in any spelling
  (`data`/`Data`/`DATA`). Three regression tests fail without the fix.
- Clear errors for a missing display (X11/Wayland) or Vulkan driver.
- `dotnet publish -r linux-x64|win-x64|linux-arm64 --self-contained` succeeds;
  the linux-x64 build starts. linux-arm64 lacks native `libveldrid-spirv` and
  `libcimgui` (blocked); Windows is built but not run.
- Tests: 605 passed / 189 skipped / 0 failed, including native shader tests
  (`OPENTPW_NATIVE_SHADER_TESTS=1`) on Linux x64.
- Open: rendering, input, and audio with original assets on Linux; running
  Windows; linux-arm64 natives; packaging. Details: RUNNING.md, FEATURE-MATRIX.md.

## Linux with original data; Theme Park Inc comparison (October 9, 2026)

Using the Mac CD of Sim Theme Park (HFS, November 2000) as original data:

- Linux x64 (Xvfb, Mesa llvmpipe): the sandbox smoke test (439 frames, original
  objects, animations, Totem script, BF4 text, upscaling, fullscreen, save/load)
  and the front-end smoke test (lobby, menus, jungle, HUD, economy) pass. For this,
  the Mac data temporarily received the PC fonts `GAMEBOLD*.bf4`; none of that is
  in git.
- Fixed: every `Shader` created its own `FileSystemWatcher`; on Linux that is an
  inotify instance (default 128 per user), and the lobby crashed. Now there is one
  watcher per shader file; on a shortage, only a warning.
- Layer I: all 2,650 Mac clips equal to ffmpeg (≤ 1 LSB, same length).
- The Mac edition differs from the pinned Windows edition (language folder
  `American`, QuickTime movies, no `fonts.wad`/`lips.wad`, 290 objects); the tests
  that pin Windows hashes and counts fail on it, see THEME-PARK-INC.md.
- Theme Park Inc compared: same engine family and identical RSE VM; textures are
  EA `SHPI`/`.fsh` instead of `.wct`. Details and follow-up work:
  THEME-PARK-INC.md.

## Decision: provenance of game rules (October 9, 2026)

- Game rules count as original only when they are traced back to logic in an
  original executable: the unencrypted PowerPC `SimTheme Park` (TPW) and
  `Game.exe` (Theme Park Inc). Manuals, websites, and community sources remain
  `[APPROX]`; example: the Instant Action rules from the manual and `UITEXT.str`.
- By decision of the project owner, the decrypted no-CD `Game.exe` from the Theme
  Park Inc CD may be statically analyzed (interoperability, Art. 6 Software
  Directive); none of it is in git. Conditions in COMPLETION-PLAN.md.
- Checked: that `Game.exe` has the same sections as the SafeDisc original with
  readable code (`.text` entropy 5.83 instead of 7.92) and the import list of
  TPW's `TP.ICD` (DDRAW, DINPUT, DSOUND, QMIXER, `wea*`, WSOCK32, USP10). The PE
  timestamp (1996) was rewritten by the crack and says nothing about the build.

## Final pre-push remote updates — October 10, 2026

Remote commits `2160e19` and `f4482c4` are retained: read-only PS2 FKNL/SSH
viewing/export support and button-label fitting. The generated register was
reconciled at 136 unresolved IDs. The affected FKNL, text-fit and widget filter
passed **16 cases**; one private PS2 corpus case was skipped because its fixture
path was not supplied. Release build passed with zero errors, and the Dutch
Metal smoke passed **264 frames** with strict text readback and the existing
online/folder, mode, payment, reload and read-only guards. No wider codec or
research batch was included in this merge.

## Automatic advisor (October 10, 2026)

- The advisor speaks by itself in parks for the traced game events: level start
  (events 10 then 0: welcome response 1 from the level bank; in Instant Action
  also advice 323 → response 587 → global 606), bankruptcy (event 2 → 274/275)
  and park open/close (events 3/4 → 308–311). Descriptor rows for these five
  messages are in `AdvisorTables`; responses come from the shipped table by
  stored ID. The scored eight-slot queue, eligibility and history are a port of
  the reviewed lane model; scores and group controls come from `Advisor.sam` at
  runtime. Park open/close score 20 is below the minimum 25, so it queues and
  never plays. `--no-advisor` and the Game Options Advisor/Tutorial switches are
  honoured. Details and the seven new approximations ADVISOR-015–021: LIPS.md,
  "Automatic advice".
- Tests: full suite without assets 895 passed / 233 skipped, with
  `OPENTPW_GAME_PATH` 1,065 / 63; 0 failed. Advisor subset with assets 63 / 1
  (German overlay not supplied). Native macOS arm64 Metal `--smoke-test
  --load-original-level jungle` passed and logged response 1 from
  `/levels/jungle/Speech/speechHD.SDT` (28.63 s, 35 LIP marks).
- Open: the other 346 descriptors and computed score producers, the original
  producers' conditions for events 2/3/4, the pause-aware advisor clock, the
  animation/deferred speech start, and listening/visual checks of the result.

## Browser build, official server and website (October 10, 2026)

- **Pitch variation and build effects (October 15):** every sample playback now draws its pitch step from the sound's signed range
  on the sound chooser stream (`min + r % (max - min)`, after the volume draw) and plays at ratio 1 for step 0, otherwise
  2^(sign x (|step| + 1) / 96), as tp.exe `0x6bc170` and the mixer wrapper `0x6d2820` do; the tables (`0x782f40`/`0x783540`,
  one step off) hold those powers written with six decimals, and OpenTPW computes them by that rule, and the six steps of the scream, chain and crowd
  sounds (0..6, the maximum never drawn, 2^(1/96) never played) match the 141 playbacks measured in the original's video, so
  the repeated screams and laughter are detuned like the original's (AUDIO-003 retired for pitch, narrowed to the delay of
  non-chain sounds, reverb, the fixed-pitch flag `0x10000` and a parameter-bound pitch). A voice out of range without the any-distance flag (event flag
  `0x200`) has its sample stopped by mixer message 3 (`0x6bca70` -> handler `0x6b8d60` -> `0x6bb7c0`), for one-shots and
  sentence segments, and a chain starts no new child (AUDIO-038 narrowed: a chain's playing children play out, loops keep
  advancing, nothing read restarts a stopped loop or shows how a sentence resumes). The
  build, demolish, repair and upgrade effects every category `.sam` names (rides 79 / 75 / 51 / 89) start at the object's
  footprint middle and are sized to it (`0x558e7c`, `0x5590f7`, `0x556DE0`, `0x55A100`, `0x520030`); REPAIREFFECT 1 and 0 switch
  the repair effect (PTCL-908 narrowed). Captured in the native jungle level: a build shows green and yellow stars over the
  footprint, a demolition a green and white burst. The leaked-chain count of a 120-minute jungle gate run is 0 of 791
  STARTSCREAM (169 in the all-rides gate), so the scream rate is not a leak. GraphicsSettings no longer says the particle pool
  uses density 1000 (it is 500). New: `AudioPitchTests`, `ObjectParticleEffectTests`, `ScreamDiagnosticsTests`, a volume-extent
  case in `ParticleSystemPortTests`; `--debug-object-effect` and `--debug-sim-minutes` for captures and diagnostics.
- **Coaster three-node station (October 15):** the coaster's station is the original's three nodes (COAST-029, tp.exe `0x436ac0`, read again at the
  instruction level): S0, a StdPylon in the cell beyond the `<` cell (node flags `0x400`, winch, no previous link), S1, pylon-less on the exact
  middle dummy (`0x200 | 0x400`, winch) and S2, a StdPylon in the cell beyond the `>` cell (`0x200`, no next link). The three dummies are read from the
  station model with the placement's rotation applied (`CoasterStationLayout`; all 12 PC stations, four rotations, checked against the shape's
  `<` and `>` cells); the pylon heights are the dummies' heights without the fMinHeight/fMaxHeight clamp (creation parameter bit 4). The end node of a
  finished track stands on S0's knot with the tangent of S0 (`0x435570`, `0x431660`), S0 leaves straight toward S1 and S2 straight along the platform,
  the closing click is S0's cell, and no pylon stacks on a station node (`0x200`/`0x400`). The sampler's closing pair E → S1 emits one sample, so
  the list starts on S0 and the braking zone, the station window (the knot coordinates of S0 and S2) and the swing's fade follow the original's frame
  (`PlatformStart` and the two-node workaround are gone, COAST-019 retired); the platform links S0 → S1 and S1 → S2 are ordinary links of the collision
  pass (COAST-004 narrowed). COAST-029 stays open only for the stations whose dummies lie below the StdPylon's h0 (Ghosta Coasta, Scatty Batty, Candy
  Coaster, Escape Velocity: the loft parameter stops at 0, so S0 and S2 stand above the middle knot) and for the middle knot's fTrackHeightAdjust.
  The controller has the original's states (COAST-012 retired, `0x435730`): 1 unfinished, 2 built or out of service (trains run empty, no station
  step), 4 open, 8 closed. Entering the coaster tool closes the ride object and sets state 2 for a finished track (its riders and the admission ring
  leave, the trains stay) or 1 (the trains are removed); leaving the tool opens the ride again when the state is 2 (`0x447520` is the tool's entry,
  not its exit as round 2 labelled it). A save with the two-node station migrates on load (the placement's three nodes, the saved pylons, the end
  node on S0, a new sampling, the riders of the old trains leave); the coaster hash changed shape (docs/DETERMINISM.md). Register: COAST-012 and 019
  retired, COAST-004 and 029 narrowed, COAST-014, 015, 024 and 030 lost their station clauses. The coaster tool panel (COAST-028/035) is untouched.
- **Coaster tool panel (October 15):** the coaster tool shows the original's slide-out panel with its nine buttons (tp.exe `0x4982a0`, `0x446fa0`, table `0x751530`; the `cb_*` models of ui.wad
  at the table's rectangles, their tooltip and click help). Every sub-mode follows its handler read at the instruction level: build and loop (`0x445220`, `0x442cd0`), height (`0x441a00`, 0.005 per pixel
  within the loft range of `0x433280`, money settled on the release), rotate (`0x441e80`), tilt (`0x442010`, `0x441cd0`, the hand-set bank keeps its node out of the auto-bank pass), move (`0x446d50`, `0x445a70`),
  stack swaps (`0x4423f0`, `0x4425c0`) and the demolish button's cut (`0x446740`). The cut is what extends a finished circuit again: it sets state 1 and leaves the track open at the chosen pylon (or,
  cut in the middle, with a gap that new pylons fill and a click on the chain start closes); a test finishes a coaster, boards guests, cuts, extends, completes and checks that every boarded guest leaves.
  The enable mask is `0x445910` (build and loop off on a finished circuit, demolish off otherwise). The build cursor is the original's spare cursor node drawn as a translucent blue or red ribbon without a
  pylon, the placement area yellow cells in the box of `0x434f10` (the per-cell test `0x5243a6` is traced, its cell types only partly known). The mouse wheel, Tab and Enter controls are gone (Backspace stays
  as an extension); saves carry the hand-set yaw and incline and the cut. Register: COAST-028 and 035 narrowed, the mouse-to-value rate and the placement test traced (the pointer scale, picking by
  cell, the area's occupancy test and the guessed clip angles stay approximations).

- **Coaster tool panel review fixes (October 15):** the way back into the tool exists: a selected coaster shows the ride window's `b_track` button (control `0x3e2a`, handler `0x4af200` → `0x4988c0` →
  `0x4474d0`, read at the instruction level) on the ride window (the table-driven one, at its original place), so select, track button, cut, extend and complete all work in normal play (tested, and the jungle smoke does it through the HUD). The cut refuses the second
  release on the same station node (`0x44678c`) and any cut that starts inside the station; a loop-tilted loop rotation (−0.35) and a height above the model's top (t above 1, as `0x434680` allows) load again;
  moving a stacked pylon away and back keeps the stack whole; the panel's background takes its clicks; Backspace waits for a drag and a released drag while paused ends; a stale cut start is dropped; a save taken
  in the tool lets the ride out on load; a removed station cleans the tool up; drags fit the controller once, on the release. The `[EXT:coaster-tool-admission]` claim was wrong (4 → 2 and 8 → 2 run the closing
  move, `0x43587f`..`0x4358cb`): it is a BIN tag now and the one difference left is `[EXT:coaster-state-clear]`.
- **Sign and gate round (October 15):** the PC v2.0 video settled sign composition. Sign faces now start transparent (COMPAT-030 retired, `BackingColor` gone), so wood and sky show between the letters, and paint masks are shifted by their stored offsets (COMPAT-003 narrowed: Wilde Aap's drop shadow, the gate's shadow; growth and blur stay open). The gate shows the park name on two lines (`SplitAtMiddleSpace`) with the stored paint and no cream override; the earlier gate-paint-legibility decision (COMPAT-031) is contradicted and recorded as such, and the upward sign normals that came with it are gone (they rendered the relief letters white; with the model's own normals the gate is dark stone with grey-blue letters, as in the video). COMPAT-001, -004, -007 and -016 are narrowed (ride signs already show the localized type name). VM-901 is retired: `WAITABS` wakes after the unsigned distance to the deadline minus the clock fraction, so a wait no longer ends at once after 2^32 ms (tests at the wrap). ADVISOR-025 records 0x4B9340 as the HUD golden-ticket/key counter refresh, ADVISOR-027 gains the video's slap-sound evidence, and DET-plan.md and DETERMINISM.md are corrected (rotate by 13, `ScriptRandom`). Review fixes: the gate overlay's normals are converted to engine axes like its positions (the first version passed model-axis normals, so one face lit up and the other down), the sign halves are straight alpha with bled edge colours (`[EXT:sign-straight-alpha]`, no more double-darkened fringes), the engine's alpha test and unsorted blend for sign faces is COMPAT-032, and the `fidelity_register.py` empty-register marker is a whole-line comment with a test. Sign baselines moved twice and were re-pinned.
- **Silent automated runs (October 15):** smokes, captures and park-resume runs open no audio device unless `--smoke-audio` is given (`SdlMovieAudioOutput.Suppressed`, `[EXT:silent-automated-runs]`), so the many test runs on a developer machine make no sound; `--silent` or `OPENTPW_SILENT=1` silences any run. Every audio path already falls back to the wall clock without a device, so timing and smoke results are unchanged.
- **Layout-table review follow-ups (October 15):** the TPI layout-load confirmation lists the tick first (a source scan of every
  `UiDialogs` caller guards the order); Escape and the right click (RMBCancelOn) close the object window; the window layout is
  cached; the shop and sideshow windows use their own row kinds, the ride one shows the age; the theme-start dialog, the game-mode
  screen and the training-budget window follow their tables; the object windows gain the queue, track and apply-all buttons;
  the lobby shows a menu hint. Restart Park leaves an older automatic checkpoint alone.
- **Original UI layout tables (October 15):** the Windows executable holds the layout command streams of its windows (the
  grammar of the Mac tables, checked against the window builder 0x65FD58 and its dispatcher 0x66076E), and
  `tools/ppc-analysis/lanes/ui/tpw_exe_layouts.py` generates the derived rectangles, help ids and ui.wad model names of ten of
  them (ride, shop and sideshow windows, message panel, camera panel, camcorder, confirm and name dialogs, the two lobby
  tables) plus the 22 pointer ids and the tool-to-pointer rules (0x4899C0, 0x52F950) into
  `UI/Original/OriginalLayoutTables.cs`; the script fails on any unexpected command, address, count or model hash, and `--check`
  compares the file. What uses them: the object windows (w_med frame, yellow name, preview, white stat rows, the round buttons
  `b_door`, `b_erase`, `b_callmech`, `b_upgrade`, Ride it!, the arrows, the list and the tick; sliders, `b_queue`, `b_move`,
  `b_track` and `b_applyall` are not drawn), the help box (fixed `f_helpbg` rectangle 574..1474 ending at y 1520, UI-010 retired), the
  confirm dialog (the (500,233)-(1516,830) window with the stacked tick and X buttons, the first choice the tick), a frameless
  cyan pause menu in the original order (Restart Park only for Instant Action parks, Publish Park opens the online publish
  flow, Go Online stays as `[EXT]`), the lobby at the table rectangles without logo or menu column (UI-014 retired; its Escape
  menu holds Go Online, Load as `[EXT]`, Options, Resume and Quit; the globe button opens the online world), the full pointer
  table with the tool rules (queue, track and camcorder pointers; link, end, cash and the crosshair stay unchosen), the message
  panel's `messagel/m/r` rectangles with `mb_erase` and two action buttons whose art follows the action's kind (no message uses
  one yet), the camera slide-out panel (camcorder, postcard and the `[EXT]` view cycle), buy-list rows of 41.4 units and no white
  lightning flash in the lobby. UI-093 retired (the E key is `[EXT:ride-view-key]`); UI-006, 013, 018, 024, 025, 029, 042,
  044, 047 and 100 narrowed. Not done: the right-click back-out gated on RMBCancelOn (UI-012), the ride view and camcorder overlay
  (another worker), the employee, training-budget and player-screen tables, the game-mode screen on the name dialog's table.

- **Ride view (October 15):** the ride view is the original camcorder now (UI-093/094/095). The eye is the model's seat (the first dummy with bit 0x1000 and value 1, tp.exe 0x44b220), so every attraction with such a dummy can be ridden, not only coasters (a coaster uses its front car model's seat); the orientation is the seat's with the traced one-frame blend (weight (1000 - dt_ms) * 0.0005, about 0.48 at 30 fps) and the pointer turns the view (dead zone 0.2, 3.5 rad per unit horizontally, 1.2 / 1.0 vertically; the camera can roll with the car). The screen shows the `viewfinder` model and the eject button at the table rectangles with the HUD hidden, a yellow crosshair and 0.33 s of generated TV static on entry and exit ([EXT]). Exits: Escape, C, eject, and a right click after 2 s with RMB cancel on; leaving re-centres the park camera on the object under its target. The info arm has the b_rideit button. Not traced: the field of view, the car choice, the yaw rule on exit.
- **Tour register round (October 15):** the TOUR items were traced in tp.exe (docs/TOURS.md, "Register changes"). The tour tick is 62 ms, not 31: the world step counter's low bit gates the call
  (`0x0054F5D7`), and the draw divides by 62 ms, so flights last twice as long and cars move half as fast per second (TOUR-001 retired; the wall-clock catch-up is `[EXT:tour-fixed-step]`; saves from the
  31 ms clock still restore). The cars are drawn as the original does: the model's +Z axis is forward, yaw = pi/2 - heading, roll negated, pitch counted in 1024ths (four times finer),
  applied roll, then pitch, then yaw, with the per-frame quarter/eighth smoothing, and the riders hang from a head node picked by the car's own random word (`TourPose`, TOUR-008 narrowed to the
  animations and shadow). A new car keeps its slot's stale heading, corner, approach heading and seat ids, and they are saved and hashed (TOUR-009 retired); closing no longer forces the ready flag, so a
  car whose departure was ordered stays docked with its riders until the ride reopens, and deleting the ride still releases everyone (TOUR-010 retired). The cars clear the `.hmp` height map of the
  object on a cell (`ObjectHeightMaps`; all 274 catalogue objects ship one and every footprint lies inside it at all rotations; TOUR-005 narrowed). TOUR-006 is `[EXT:legacy-tour-save]`, TOUR-007 is narrowed
  with the walk-off formula and left to the walk owner (no walk nodes exist yet), TOUR-002 records that thrown-off riders keep inflating the rider count in the original. Tests: `TourRegisterRoundTests`
  and the changed `TourControllerTests` and `TourAssetTests`.
- **HUD video round review fixes (October 15):** the software pointer is drawn last (after the advisor and progress overlays) and the system pointer is hidden only when the cursor art from `Data/ui/cursors` (found case-insensitively on disk) is loaded and decodes, the pointer is inside the window and ImGui does not want the mouse; an empty message tube no longer swallows park clicks, opening a message closes the info arm, the status line hides under the info arm and message panel, hover selects an applicant only when the pointer moves, and per-frame allocations are gone.
- **HUD video round (October 14):** a gameplay video of the Dutch PC build settled the HUD and window register items. The hire window now has the Vaardigheid gauge and the mini balance (money in, staff costs plus the wage, other costs, balance) that follow the hovered candidate; the click-to-hire trigger stays (the original's 0x400 list message and its advisor line, UI-083 retired) and the 3D candidate figure is registered (UI-082). The yellow text under the cash is the cost of the build action under way and is hidden outside the build tools (UI-047). Pending messages stack in a red message tube left of the HUD, left click opens one in a green slide-out panel and right click deletes it (UI-025); OpenTPW's own feedback lines moved to a status line. The popup help box sits at the bottom centre on translucent black (UI-010). The golden ticket and key counters show only above zero as "n ×", keys are tracked (UI-051). Excitement, reliability, repair and life are red-to-green gauges (UI-028/046), the buy list's third column is a tick box (UI-050), and the park draws the original glove, arrow and no-entry pointers (UI-100). UI-011 is retired, UI-042 narrowed and the shortcut letters of the help texts recorded under UI-012.
- **Particle allocation test (October 14):** `ParticleSystemPortTests.TickAllocatesNothing` measures windows of 100 ticks until one is clean, so tiered-compilation allocations in a busy full run no longer fail it; a real per-tick allocation still never gives a clean window.
- **Rides register round 2 (October 14):** the rides items were traced in tp.exe and the code follows what was found
  (docs/OBJECTS.md, "Retired and reclassified"). Names: the executable binds OBJECT_NAMES by Info.Id through a 281-row
  table (0x7488D8), `ObjectNameTable`, instead of the English matcher; 262 of 274 entries now bind (only the three tools of each
  theme do not), the gates, signs, lights, seaplane, loudspeakers and renamed rides get their names, and the fantasy
  Fountain and the space Crater point at the right entry (RIDES-010 retired; COMPAT-016 cites the table). Shapes: every
  symbol comes from the executable's 19-row table (0x7396C8) with a cell type and a compass bit; the entrances `8 6 4 O`, the
  exits `X` and `W` and the cells `Q # D` exist now, the open side of an access cell comes from its symbol (exit: its bit,
  entrance: the opposite), all 209 corpus access cells open as before, and the bit-to-axis mapping stays APPROX (RIDES-012,
  medium confidence). 12-byte vertex block: the PC sampler does read it (0x4718F7). The table holds one pose per tick
  (duration + 1 poses, all 30 records), the sampler shows pose `trunc(time)` with no blending, and `ObjectAnimator` does the
  same; 19 of the 30 records (speakers, rocks, tree, droid, plane) were drawn wrong before, and the 10 catalog clips that were
  reported as limitations play now (RIDES-041 retired; docs/MD2-MODELS.md corrected). Upgrade completion sets the script
  speed bias to the new level's `InitSpeed` when above 0 (0x4DF8F0, RIDES-033 narrowed to the unmodelled ride-settings UI).
  Low detail: `stexture` and `ssharete` replace `textures` and `sharetex` exactly when the active detail has TEXTUREQUALITY 0
  or 1, and they hold every texture the full folders hold (RIDES-022 retired). `coaster.sam` is the coaster's own settings
  as in the loader (0x413C10, 0x42FAD0; RIDES-009 retired, values identical). Also retired: RIDES-017 (build-menu filler
  0x4AAF70), RIDES-036 (UV pass gate) and RIDES-043 (objects load in set mode; the TrackMaster Add-mode remainder moved to
  COAST-026); reclassified as OpenTPW conventions: RIDES-026 (`[EXT:md2-engine-scale]`), RIDES-030
  (`[EXT:malformed-texture-coordinates]`) and RIDES-038 (`[EXT:trig-anim-speed-guard]`, the corpus has four literal uses
  from 1800 to 4000); narrowed: RIDES-011, 012, 019 (the original picks bus, seaplane or ferry per arrival with the LCG at
  0x51A2F0, not implemented here), 024, 025, 031, 033, 035 and 044. Open and not implemented: in Instant Action the
  original does not load an object without `Easy_<object>.sam` (0x413AC4), which would change the objects of that catalog
  (docs/OBJECTS.md, "Remaining gates"). Re-pins: see docs/DETERMINISM.md.
- **Advisor, compatibility and VM register round 2 (October 14):** the advisor's fixed sequences are imported: the ten rows in use of table `0x76DC18` (`AdvisorFixedSequences`), a
  span of the clip sum + 1000 + clip 15, a row that sums to 0 (sequence 28, or clips missing from the archives) generated instead, the immediate start derived from
  "first clip is not 14", and the timer entries' effect (particle `0x521E60`, sound `0xC6`, `0x4B9340`) registered as ADVISOR-025; ADVISOR-016 is retired. ADVISOR-018 is settled at the
  instruction level: the animation sampler `0x4735D0` hides the advisor model at the end of the last clip (nothing queued, model flag 8, then `0x45BDD0`), which agrees with the earlier
  finding that the normal end of advice `0x599880` calls no Kill, so the model now hides when the sequence's clip sum has passed instead of staying for another second and clip 15
  (LIPS.md fixed). Game events 3 and 4 are raised on every press of the open/close button and the Ctrl-O/Ctrl-C shortcuts (`0x519EF0`, `0x40C410`), also when the park
  is already in that state (ADVISOR-020 narrowed to event 2). COMPAT-017 is retired: the sign lines come from the selected language's file only and a missing SIGNA or SIGNB shows
  "MISSING STRING" (`0x413600`). HEADS-005 is retired: child and sound scripts have no head table on the PC (thing id 0 at init), so their ADDHEAD/DELHEAD do nothing; the per-child tables and the
  save refusal are gone and no pinned hash moved. VM-901 is narrowed: WAITABS keeps a wrapping 32-bit deadline compared unsigned, 0 meaning not armed, and wakes when the integer clock reaches it (`0x553828`); only a wait spanning the clock word's 2^32 wrap is unmodelled. VM-902 is retired:
  MIN and SEC read host-local time like YEAR to HOUR (`0x5566FF`, `0x55674E`). BLUEPRINT-002 is retired (the PC UV table and face builder `0x53DF30` match the whole-texture faces). Reclassified as
  OpenTPW decisions: ADVISOR-011, COMPAT-011, COMPAT-012, COMPAT-031, TPI-900 to TPI-904 and DET-016 (the player's park save already stores guests, scripts and the advisor). Narrowed: ADVISOR-006, -007, -012,
  -017, -022, COMPAT-019 (`dlake` is the drained lake obstacle 1704 of Theme Park Inc), HEADS-001 and BLUEPRINT-003. The PC world generator `0x516330` rotates its state right by 13: DET-plan §4.3,
  DETERMINISM.md and the HEADS-001 text say so, and DET-013 to DET-015 now carry the port requirements (eight reseed sites, the saved `mRandomSeed`, the update order); the port itself is not started.
  This round took 16 IDs out of the register (VM-901 stays, narrowed) and added ADVISOR-025.
- **Audio register round 2 (October 14):** the second static round over tp.exe settled the open sound items
  ([AUDIO.md](AUDIO.md) "Register round 2"). Behaviour changes, each with a test: a bank lives under `Global` when its map record has
  flag 0x20 and otherwise in its own folder, with no second try (AUDIO-002; every shipped bank still resolves); **one shared, advancing
  sound chooser** (`s·0x19660D + 0x3C6EF35F` stored on every draw, `ror(s, 13)` then `>> 16` or `% range`, a lone sound, sample or empty
  range draws nothing) replaces the frozen Mac reading, the crowd's separate stream and the fixed volume draw (AUDIO-035 retired; the
  Mac-versus-PC contradiction is documented in AUDIO.md and DETERMINISM.md, the park still reseeds and saves the chooser, AUDIO-037); the
  drawer plays 0x1C when it starts to close as well as 0xC2 at its end; flat one-shots with flag 0x8 loop like positioned ones (AUDIO-019);
  a placed scape sentence plays as a positioned sentence for the park's life (AUDIO-020); the listener helper takes the eye in the
  camera modes `0x790ab0 & 0x16` and the midpoint otherwise (AUDIO-021); the weather's constructed quality is never applied and the first
  update rolls a forecast at once, with the schedule on game-clock timestamps instead of day indices and the apply falling through to the
  roll test (AUDIO-026/027; the snapshot's schedule fields now hold game seconds, older day-index saves are migrated on restore); screams, the repair sound and EVENT sounds are
  positioned at the ride's footprint middle, the object and the key's model node (selector 0x200), and an unknown EVENT key plays nothing
  (AUDIO-032/008/908); the refused path click plays at the clicked cell (also fixing `Level.CellSoundPosition`, which returned a
  height/ground mix); `cat_ui` 0xD2 plays once at start-up; every front-end list line and row clicks like a button (AUDIO-012); the
  weather thing's own flat 0x21 rain voice sounds beside the level-driven one while it rains, which keeps following the drops in snow
  (AUDIO-015). Retired: 002, 006, 007, 010, 012, 019, 020, 021, 027, 032, 035. Reclassified as `[EXT:...]` decisions: 005, 013, 014, 024, 030,
  031, 905-907 and the drawer window. Narrowed: 003, 004, 008, 009, 011, 015, 022, 025, 026, 034, 036, 900, 908. New: AUDIO-037. No canonical
  hash pin moved (the weather and the sound chooser are outside the hash, [DETERMINISM.md](DETERMINISM.md)).
- **Tour rides take and release riders (October 14):** the four original tour rides (Jurassic, Flightmare, Star and Tweety Tours) no longer
  leave guests in the queue. Their scripts' `TOUR` commands, traced from tp.exe (the flying-car module `FLY_`), drive a controller of up to 20 free-flying
  cars with no track: a car lifts off from the ride's launch pad, flies a terrain-avoiding lattice walk for the ride's duration, returns, circles until the landing slot is
  free and lands, where the script unloads and loads the next riders. Real-script asset tests run each theme with 16 guests for ten minutes (boarded equals released,
  nobody left Using); saves carry an optional `Tours` part, older saves restore with fresh controllers. See [tours](TOURS.md).
- **Desktop setup chooses TPW or experimental TPI (October 14):** the setup window offers Theme Park World or the experimental Theme Park Inc / Sim Coaster
  (`[EXT:tpi-viewer]`, building and guests only, no economy or campaign) with an edition-aware inspection that refuses mixed or wrong-edition folders and
  needs an available world; the TPI choice is stored beside the TPW paths in `setup.json` and starts through `--load-original-level`. An ordinary desktop
  start does not open the window: it uses the saved choice, then the old setting, then detection, and only a first start with no usable folder, a missing or
  invalid saved TPI folder or world (including a timed-out re-inspection) or `--setup` opens it. Options > Game files has an Edition button for switching
  on desktop, effective at the next start. The TPI/TPW world lists come from `GameEditions` instead of a copy. Tests cover direct TPW and TPI starts, the
  wizard cases and the edition switch persisting to `setup.json`.
  Review fixes: a saved, old or detected folder holding Theme Park Inc data no longer passes as Theme Park World (the window opens instead of every start failing);
  `--setup` with an explicit tool mode refuses before any window; a stale or timed-out choice keeps the window open with the typed folder and a reason; and
  Options > Game files reports a timed-out check as such.
- **Coasters drawn with their original track, pylons and cars (October 14):** the track is the `coaster.sam` cross section
  extruded along the curve (ten slices per knot pair, V every six units) with the real track textures, so the minecart track
  has rails and sleepers, the Gorilla Thrilla a rope, the flumes water that flows and a ratcheted dry section where they
  climb, and chain lifts a scrolling chain. Pylons are the StdPylon/LoopPylon models stretched by their loft clip to the node
  height and turned by their rotate clip; cars are the car models posed on the samples with roll, pitch and yaw and spaced by
  their own length. A track under construction is the curve through the placed pylons and a ghost pylon at the cursor instead
  of straight green lines. Traced from tp.exe (rebuild 0x43eb70, slice emitter 0x43f4e0/0x43fd60, loader 0x42fad0, car pose
  0x43ce20, edit step 0x435d50); COAST-026 is narrowed and COAST-033..035 added (docs/COASTERS.md). Rider heads in the cars
  and the dry/wet blend sections are not done.

- **MULT traced (October 14):** the RSE interpreter's case 48 in tp.exe (`0x553eb9`) confirms OpenTPW's MULT: destination
  first, `a × b` with a 32-bit wrap, the product in the flag word and a variable destination. VM-900 is retired, and DET-002 (the explicit world seed) is reclassified as OpenTPW's replay policy
  `[EXT:explicit-world-seed]`.
- **Server tests no longer hang on macOS (October 14):** `ServerProgram.Build` turns off appsettings.json reload-on-change
  (`--hostBuilder:reloadConfigOnChange=false`, which a later argument can override). The server binds its options once, so the
  watcher did nothing, and on macOS starting it calls `sync(2)`, which never returns while a mounted volume (here a stalled SMB
  share) hangs; that hung every loopback-server test locally while CI on Linux passed. The news test's own web host skips the
  watcher too, and `BaseFileSystem.CreateWatcher` returns its watcher unstarted (the caller subscribes, then starts it), so the
  test suite starts no watcher at all. The test `LoopbackServer` now builds, starts and stops within 30 seconds or fails with
  a clear message, and requests stay bounded by `OnlineClient`'s HttpClient timeout.
- **Placing and drawing staff (October 14):** hiring no longer employs anyone at the gate: the staff window closes and the Place tool starts with the
  applicant still in the pool (it does not expire meanwhile); a click on a valid cell (grass, path, queue, queue end, owned, under no object) hires and
  places the member on the cell centre as an idle one with the whole map as patrol area, right click or Escape hires nobody. The employee view has a
  Pick up button (refused on strike, in hand, escorting or on an unsuitable cell) that starts the Carry tool; a valid click drops the member on the cell
  centre, idle, with no steering state, litter or toilet claim, and a mechanic loses its ride so the dispatch runs again; the patrol rectangle is never
  touched, cancelling drops it back where it was, a new pick-up first drops the previous member, and a save drops a carried member on its old cell.
  Staff are drawn as camera-facing sprites of `esprites.wad` through `StaffRenderer` (folder by role and theme, bank from the member, clip by job:
  stand, walk at full or half rate, sweep, repair, research, strike, guard carry), resting members are hidden, and the tool shows the slot 6 dangling
  sprite over the hovered cell and a red ground marker on invalid cells. The only change in `StaffWorldSimulation` is the `Synchronize` condition that
  leaves a member in hand without a position alone. New register IDs ECON-052/053, GUEST-013..016 and UI-097..100; tests `StaffPlacementTests`,
  `StaffSpriteTests`, `StaffSpriteAssetTests`; the native jungle smoke hires, places, draws (GPU readback), picks up and drops a handyman
  ([STAFF-PLACEMENT.md](STAFF-PLACEMENT.md)).
- **Saving with a screaming ride (October 14):** a park can be saved while a ride holds a scream. The held scream is saved
  with its STARTSCREAM operands (band, level, script speed) and starts again on load; a stopped scream no longer counts as
  live (the stopped chain stayed in the ledger until the next STARTSCREAM, so most parks refused "an attraction is still
  playing a scream" after the first ride cycle).
- **Browser options no longer stop the game (October 14):** the browser has no .NET configuration system, so opening
  Options → Game files read the old `Settings.Default` and stopped the game with "Configuration system failed to
  initialize". The browser chooses its game folder on the page, so it no longer shows that button, and the old settings are
  read through `LegacySettings`, which returns nothing in the browser or when a damaged user.config cannot be read.
- **Water ride tracks, phase 1 (October 14):** one water ride, Splish Splash on the Jungle level, works end to end on the traced original rules
  ([TRACKS.md](TRACKS.md)). The track layer mirrors the original's 40-byte cell record (origin type 16, three type-12 extension cells), the
  Q1 piece tables and adjustments, the 14 controller templates and the `.hmp` height profiles (`HmpFile`) are ported, and the water track tool lays
  two-click lines in two-cell tile steps, charged `Costs.WaterTrackCell` per tile, clearing paths and marking bridges. The circuit walk
  (`0x52A970`) writes the attraction's validity word (the PC's +0x2C, which the park points read) and the segment numbers; the sections, collision
  objects and buoys are built in the original's fixed-point units. `Wateride.RSE` drives a `BumpController` through `BUMP` 1-14, 16 and 17 (the PC
  dispatch shows BUMP 5 is the validity read and BUMP 17 with 0 starts the water): integer steering, integration, walls, keep-out circles, boat
  collisions, laps and unload at 30 ticks per second, the sine table regenerated from the start-up code. Pieces and boats are drawn from the ride's
  meshes, and the cells, controller and boats are saved in the park-resume envelope. Jumps, tunnels and the water animation stay
  open; TRACK-001 to 021 are registered, QUEUE-004 is narrowed to karts and ARRIVAL-003 applies the water ride's track word.
  Review round: every tile records its ride (TRACK-021), so rides side by side never connect and a demolition removes only its own tiles; a circuit
  that overflows the section, object or buoy pool is invalid (TRACK-020); the park-resume capture and restore share one live-controller rule; the
  resume check bounds guests, positions, velocities and the tick clock and the restore checks boat indexes against the walked circuit; the canonical state
  covers every restored boat field; the tick and the renderer no longer copy the boat list.
- **Go-kart tracks, phase 2 (October 14):** the go-karts of all four themes run on the shared controller, end to end for the Jungle's Dino Karts (`GoKarts.RSE`
  drives a real race in the tests and the native smoke). From `tp.exe`: the grid (eight flag-2 buoys in a staggered pair of columns, then the finish buoy,
  oriented by the orientation bits), the n-th kart aiming at the n-th grid slot and parking there, `BUMP 3` starting the race with a per-lap thrust bias from the
  world generator, laps and the race over (the first finisher sets phase 3, the others leave at their next crossing, the last kart out resets to loading),
  broken karts coasting, and the effects: crash (`cat_rides` 12), horn (EventMap slot 3 on the module LCG), exhaust (particles 1 and 10, `cat_rides` 24), the engine
  voice, particle 15 at spawn and unload, the speed-40 animation switch. Karts are drawn with the four-probe pose over the piece's `.hmp` profile (the hover karts
  of space and fantasy with a bob, a bank and a shadow instance), their riders as head sprites at the kart's head dummies, and the kart fields join the saves
  and the digest of kart parks only. `BUMP 8` returns nothing. TRACK-015 and 016 are narrowed (bumper cars; water spray), TRACK-023 to 026 added; QUEUE-004 now
  covers neither water nor kart rides. `--debug-kart-track` places the ride for captures.
- **Bumper cars release their riders (October 14):** guests who rode the bumper cars of any theme (jungle Bumper Cars, Halloween, space, Bumble Bugs) never came
  out: the four `bumper.RSE` scripts drive the ride with `BUMP` commands and the ride had no controller, so `BUMP 2` never returned a guest (a headless probe boarded 16
  and released 0, all 16 stuck in `GuestState.Using`). The shared controller now runs the bumper types (−1, −6, −11, −14) from `tp.exe`: open sets phase 1, `BUMP 4` makes a car
  that drops at a random free spot of the arena (one keep-in circle and a ring of eight buoys), `BUMP 12` seats a guest in the first empty car, `BUMP 3` starts the session
  and every car counts down the duration the script set with `BUMP 13`; at 0 each car hands its riders to the leaving list, and with nobody left the ride goes back to loading
  while the cars stay. Closing, breakdown and `BUMP 16` follow the traced routines. Cars wander between the buoys on the shared integer physics. The real script of all four
  rides board, run and release 16 guests over several cycles in `BumperCarAssetTests`. A save from before the controller restores the ride freshly and hands its riders to
  the leaving list. The cars chase one another as traced (a random other car for 90 ticks), crash with the traced code 5 (silent in the jungle), throw sparks and run the first car's engine voice, and are
drawn from the ride's own car meshes with their riders' heads; bumper state is saved and hashed only for parks with a bumper ride. TRACK-015 retired; TRACK-030 (arena centre) and 031 (look and sound ids) added.
- **Advisor and compatibility register round (October 14):** from the Windows `tp.exe`: the advisor's audio now starts on the
  advisor clock as the original's does (immediately for sequences 16, 17 and 29, else stopped and restarted 800 ms after the
  request with the LIP clock moving there; the immediate start's LIP clock leads by 200 ms; ADVISOR-008 and ADVISOR-013 retired),
  generated sequences are built by the original's rule (`AdvisorSequences`, clip lengths from the advisor clip trailers; the
  clips themselves are still not drawn, ADVISOR-006 narrowed), the response span is the sequence sum + 1000 + clip 15 (counted
  twice for generated sequences; fixed sequences keep the estimate, ADVISOR-016 narrowed), and the model stays shown until
  "Kill advisor" (level exit), not until the speech ends (ADVISOR-018 narrowed). The advisor material draws both faces as the
  original's device does (ADVISOR-007 narrowed). ADVISOR-014 (synthesis window equals the table at `0x70BD08`) and ADVISOR-019
  (balance fields +0x18/+0x1C are never read) are retired; ADVISOR-011/017/020 are narrowed to the traced rules and ADVISOR-024 is
  an extension. Sign text: the `.sgn` slot percent now scales each character's width (COMPAT-005 retired; the composed-sign
  baseline hashes of 71 of 75 signs were re-pinned), object sign lines keep 32 characters and line 1 loses a trailing "--"
  (COMPAT-017 narrowed), COMPAT-006 now names Uniscribe instead of GDI TextOut, COMPAT-008 and COMPAT-013 are extensions, COMPAT-011/012
  are narrowed. 392 to 384 unresolved IDs.
- **Advisor video round (October 14):** from the Dutch gameplay video and the Windows `tp.exe`. The advisor now wears a hat: each response's two
  accessory selectors (response record `+0x14`/`+0x18`, `content/data/advisor-costumes.toml`) show one node by dummy attribute (hard hat for
  breakdowns, security cap, handyman cap, research hair, bowler with bow tie, ticket and kiosk hats, fast-food hat with spatula, no hat for help
  text) and the level's theme costume (`ThemeAdvisorCostumes` in `Standard.sam`: pith helmet, axe, space helmet, fantasy helm) on the level
  advisor's own model. The clips are played: the node tracks give the rise and the sink, the blink toggles the blinks (vertex tracks still not), and
  the model hides when the clips have played out, clip 14's 1000 ms included; a new response restarts at clip 14. Contextual help is a new producer
  family: 81 help messages (`content/data/advisor-help-messages.toml`, replayed from the descriptor initializer `0x5A0A50`) are posted when
  the buy, hire, attractions, loans, finance, park-information, visitors and ticket-price windows open and withdrawn when they close; a
  withdrawn speaking help cuts off at once, counts as unplayed and gains a slap (three cuts discard it), while event advice is never cut.
  ADVISOR-002 and ADVISOR-023 retired, ADVISOR-006/009/022 narrowed, ADVISOR-026/027 added. Tests: `AdvisorHelpTests` (27) and a script test.
- **UI register round (October 14):** findings from the Windows `tp.exe` land. The hire window's control 9366 is the plain
  cash label ("Cash $ N", UITEXT `0x1ca`/`0x1cb`); the applicant/employee toggle is now a labelled extension button
  (UI-090 retired). The map zoom is 1.0–3.0 in half steps and a pan moves the centre one map cell whatever the zoom (UI-087
  retired); each map category button toggles its own layer, not an exclusive filter (UI-086 keeps only the colours). A list
  column press toggles that column's direction, a new column starts ascending and column 0 starts ascending, so the first
  press on it descends (UI-084 keeps the per-list initial state). The HUD cash shows the bare balance in the original's
  red-to-white colour steps (table `0x752e30`), without grouping or prefix (UI-021 keeps the control 47 placement). The
  volume options are percentages (defaults 75/60/75/100) and old 0–10 options files migrate ×10 (UI-030 keeps the slider
  step). Before each start-up movie the game waits until Esc, Space and all three mouse buttons are released (UI-035
  retired; the letterbox is `[EXT:movie-letterbox]`). UI-019 and UI-062 are retired (the shipped lobby and `LOANNAMES.str` are
  complete; data tests added) and UI-023 (test fixture). Widescreen anchors, HiDPI glyphs, the fixed options rows, the
  autorun focus rectangle, the speed buttons and time keys, the sandbox map and the overview detail rows are now documented
  `[EXT:...]` design decisions (UI-001, 038, 040, 043, 022, 902, 089, 906, 908 retired). The staff, locator, hire, dismissal,
  overview, camera-button, slider, fluid and key-table items are narrowed to what the executable leaves open (UI.md).
- **UI register round 2 (October 14):** the second `tp.exe` read of the UI items. Comma now adds 90 degrees to the build angle
  (one `RotateBuild` step) and full stop adds 270 (three steps), where they were swapped (UI-903 retired); leaving the build tool
  resets the angle to 0, and holding exactly Ctrl at the placing click (or at the last click of a ride's queue) keeps the build
  tool armed (UI-031 narrowed to the coaster and track sub-modes). Window lists follow the original's list control: the pointer
  selects the hovered row, a click counts only when released within 500 ms and moved at most 6 px, a left click on an
  applicant hires (and so starts the Place tool), a left click on an attraction or visitor row opens its info panel and keeps the
  list, a right click centres the camera and closes (UI-083/910 keep the medium-confidence "button 0 is the left button"). Each
  list keeps its last sort for the session (UI-045, UI-084 retired). The buy window's control 512 shows the cash balance
  (UITEXT `0x1ca`/`0x1cb`, UI-048 retired); the HUD cash box is nine digits wide and centred on `i_dollar` (UI-021 retired);
  the research effort sliders truncate a drag (UI-065 retired, UI-030 retired). Ctrl+H flips the global Popup help option and
  the host persists it (UI-920 retired); the Confirmations option gates the dismissal prompt (UI-092 narrowed); the map window
  pauses the park (UI-096 narrowed); Instant Action skips the enter-park sound (UI-942 narrowed); the pick-up button is the
  original `b_pick` model with help 88 (UI-098 narrowed). Shortcut letters now come from the language's `KEYBOARD.str` except a
  plain letter that is a camera-pan key (Dutch hire A keeps H; UI-012 narrowed). The advisor message 309 (response 569) is not
  posted: its score is a field of the `Advisor.sam` balance object whose key is untraced (UI-097 narrowed). UI-015, 900, 909 and
  931-935 are documented `[EXT:...]` decisions. Review fixes: list hover only highlights (the click selects, so buttons beside a list never retarget), a plain shortcut never fires under Ctrl, Escape and a right click leave a Ctrl-kept build tool, the click time limit ignores a single long frame, and the browser keeps the default for Ctrl+W/T/N chords. 407 to 391 unresolved IDs.

- **Coaster register round (October 14):** the coaster register read against the Windows tp.exe. COAST-013 is retired
  (the guest rating `0x4e0560` discards the sickness). COAST 5 is gated as in `0x43b2f0`, COAST 6 floors at the literal 0
  instead of 1, COAST 8 applies the script speed bias 50 (cap 105 instead of 110), and the station step is skipped in
  mode 64. The suspended swing now follows the PC restatement (`0x43c76c..0x43ca45`). COAST-012, 014, 018, 023, 024 and 031 are
  narrowed to what remains. No determinism or coaster pin moved.
- **Coaster register round 2 (October 14):** the COAST findings read against the Windows tp.exe. Trains never stop: arriving trains
  unload in any state, guests board only while the ride is open as cars roll through the station window (admission ring filled by
  COAST 1), headway comes from the halved winch floor and the collision exchange, closed trains circulate empty. The track's
  self-intersections are counted (`0x439ab0`) and guests refuse a coaster with a nonzero count. Pylon heights come from the
  model's track dummy, with the stack offset and loop pylons placed as a pair. The swing uses the PC sine table, textures scroll
  by the power factor, and the end node keeps the PC flags. COAST-007, 016, 018 retired; 025, 027, 031 and the completion click
  are extensions; the rest narrowed; COAST-019 and the three-node station (COAST-029) stay open. Old coaster saves migrate.
- **Particle register round (October 14):** `ParticleSystem` is now ported from the traced `tp.exe` instructions
  (docs/PARTICLES.md): density as `(rate * density) >> 10` clamped to 127 (negative rates scaled too, default PARTICLEDENSITY
  500), the pool init (flat-zero ground stub, 0x800 particles), the signed and unsigned random forms (a jitter of -1 still
  draws three times), LIFO emitter slots with a global generation, the rate quarter taken from the remaining life
  (immortal emitters stay on the first rate), a burst that ignores the cap and one cap check per tick, the horizontal
  disc, ring and box volumes with the Y rotation, the horizontal radial burst that replaces the base velocity, colour
  flicker, the frame, size and spin formulas, physics on the spawn tick, dead emitters and the child rules (+0xBC, +0xAA,
  the child offset), the effectors and the spiral; `ParticleEffectorTemplate` names the effector record. PTCL-902, 903 and
  907 are retired (the save is an optional `Particles` part of the park-resume envelope: emitters, effectors and the
  generator, no particles, screen-space emitters killed on load, outside the world hash). PTCL-900, 904, 905, 906 and 908
  are narrowed: a key entry without a matrix starts at the world origin, the quad's half-height is size / 1024, the
  render flags decide the blend of untextured particles too, positions truncate like `_ftol`, and RSE ADDOBJ types 1
  and 2 start effects whose handle `KILLOBJ` ends. Where the research report and the instructions disagreed the
  instructions won: byte +0xC2 != 0 is the box and 0 the disc, the ring flag is word +0xA8, the gravity word +0x28 and the
  jitter word +0x50 are words, and a non-immortal child never sees its life -1.
- **Ride particles (October 14):** RSE EVENT types 1 and 2 now create, simulate and draw particles of the original system
  (docs/PARTICLES.md). `Data/Particle/Tp2.plb` (105 templates, 20 effectors) is read by `ParticleTemplateFile`; `ParticleSystem`
  mirrors the traced pool (120 emitter slots, 2048 particles, private LCG seeded 1, 31 ms ticks, bursts, per-quarter rates,
  chained and death effects, the type-2 direction, the control scale); `ParticleRenderer` draws camera-facing sprites from the
  `particles` set (`esprites.wad` SPR_PA/PB) with the template's blend, as a weather-style entity with a precompiled web shader
  and a new one-plus-one material flag. A prepared request is converted back to native units and handed to the pool
  (fire and forget, no save state, outside the determinism hash). PTCL-901 is retired for PTCL-902 to PTCL-908 (arithmetic,
  effectors, sprite scale, draw order, settings and ground callback, the PART save block, the callers not wired); PTCL-900 is
  narrowed. In the shipped jungle the Aztec Mayhem torches already fire EVENT 2.
- **Allocation test made sturdy (October 14):** `PerFrameExactTierLookupAddsNoDefaultParameterAllocations` keeps the
  smallest of three measured passes per loop, so a one-off runtime allocation on the thread (seen once on ubuntu Release,
  48 bytes) no longer fails it while a per-call allocation still does.
- **Rides register round (October 14):** the rides register is read against the decrypted Windows tp.exe. Ride init writes
  `InitDuration` only when above 0, as its low byte clamped to `UsageInfo.MinDuration..MaxDuration` (also the tier seed;
  RIDES-016 retired), and the script speed is now `0.5 + bias / 100` with the bias from the ride's `Upgrades[0].InitSpeed`
  (children copy it; jungle Bouncy runs at 1.1, RIDES-033 narrowed to the settings/upgrade writers). A literal TRIGWAITANIM
  out operand resumes a script without an object once its value equals the triggered AnimID (RIDES-037 retired). The member
  letter table is the original's twelve (C D I L S M E U W B R O by AnimID), numbered clips load up to the first gap and the
  unnumbered one only without number 1 (RIDES-008 retired). RIDES-001 and RIDES-034 are retired with PC citations (30 frames
  a second; placed rides never take the idle restart); RIDES-022, 032, 036, 038, 043 and 044 are rewritten around what the
  PC shows. WAIT is divided by the script speed as well (`0xb0658`), screams use the script's speed word (AUDIO-016
  retired), and the M3 gate's boarding period follows the scaled WAIT (Belly Bounce: P 3, H 55).
- **Track register round (October 14):** the kart and water register is read against tp.exe. The controller ticks once per
  31 ms world substep (`0x54F4C4`, the bump module tick called at `0x54F55F`; the BUMP 13 duration unit stays 30), boats and
  karts start at heading 0 (only bumper cars draw a random one), a water crossing's centre circle carries the 0x80 flag so
  boats pass through each other there, and the kart bank has no clamp. The track line snaps X only when |dx| > |dy| and ends at
  `start + 2 * floor((cursor - start) / 2)` (a tie goes to Y, the − direction rounds away from the start). Boats raise the spray
  as particle 96, start an engine voice at the first launch and play BeamUpCar with a silent voice stop at the unload; the break
  smoke is a persistent particle-2 emitter per carrying car, made by BUMP 8 and removed by the fix or the unload (it follows the
  car every tick; the flag is saved, not hashed). The bumper chase was already ported as traced (TRACK-029 retired). Register:
  TRACK-009, 011, 012, 013, 016, 017, 023, 024 and 029 retired; TRACK-020, 021, 027 and 028 become OpenTPW decisions
  (`[EXT:...]`); TRACK-002, 005, 019, 025, 026, 030 and 031 narrowed (the station table of `0x52A1A0`, the arena formula of
  `0x529E10`). The tick and heading changes move the boat, kart and bumper goldens and the track resume hashes; the pinned fixed
  run and the hash schema are unchanged (DETERMINISM.md).
- **Riders' heads on their traced seats (October 14):** a head is now attached through the model's own records, traced in
  the Mac binary: attribute record `i` belongs to node `Unknown46 + i`, a head id is the first record with flag `0x80` and
  that value, and the node-name guess is gone (Eruption's `Head1a` and the other previously unresolved ids now draw;
  `monkey.MD2` id 3 is node 10 `Head02`, `Bird.MD2` id 9 is node 2 `Head03`). The head is drawn the way `0x1019B6F8` does:
  at exactly the node's world position, hidden for node flag `0x8000`, the camera binned in the node's frame to the 7 x 8
  frames of the kids' head art (direction row times 8 plus azimuth column), and the sprite rolled with the seat
  (`atan2(-m10, m11)`), at scale 0.685. The new GPU-free `HeadPresentation` is unit-tested and `RideHeadRenderer` draws from
  a list of head sources, ready for the coaster seats. Every script (root, child, sound) has its own head table, as the
  script init allocates one for each, and the slot draw uses the original `labs(rand >> 1) % length` formula on the ride's
  own stream. Register: HEADS-002 and HEADS-003 retired, HEADS-001/004/005 narrowed.
- **Audio register round (October 14):** the storm's thunder follows the original countdown (`0x101033CC`/`0x101034D8`):
  a bolt starts the countdown only when none is running, at `ThunderStartsThisFarAway` minus the storm's bolts so far plus a
  random term, and the thunder rings when the countdown steps from 1 to 0 (a computed 0 never rings), at the last bolt's top;
  AUDIO-029 is retired. Speech ducking (AUDIO-001) is retired with its Mac citation (`0x100BB18C`); AUDIO-002, 005, 019,
  025 and 033 are narrowed to what the PC and Mac traces leave open (AUDIO.md).
- **Lobby rain and lightning drawn (October 14):** the lobby's rain and bolts now go through the original weather renderer
  (CRain) as the original's do, at the lobby's own scale: rain around hallow by distance, branched 500 ms bolts within ±50 of
  the island with their own bottoms, and the one-frame white flash. The renderer's source hands strikes with both points
  (`WeatherStrike`); the park keeps its ground point below the top.
- **Original weather visuals (October 14):** the procedural rain, snow and bolt approximations are replaced by the
  original engine's `CRain` renderer, traced in the Mac `engine_shared` library and tp.exe (`0x57F1F0-0x57F380`). Rain and
  snow are one fixed 1024-point field in a -4..4 box around the camera eye (original units, wrapping per axis, scrolling
  down at 2.5 / 0.8 units a second plus camera parallax), drawn count `min(CurrentDrops, MaxRaindrops)`; rain streaks use the
  original's depth-to-grey ramp and 0x80..0xFF alpha, snow flakes the 2x2 atlas. Bolts are recursive midpoint-displaced
  ribbons from the strike's top down to original y = 0, up to 16, living `LightningTime` (2000 ms). The images come from the
  game's `Data/Generic/Weather` (nothing is committed). Time is wall-clock capped at 500 ms and frozen while paused. The
  renderer reads an `IWeatherVisualSource`, so the lobby can reuse it. Still approximate (AUDIO-900..904): the blend, the
  parallax sign, the snow swirl flag, the near clip, and the bolt's displacement and pulse curve.
- **Lobby effects (October 14):** the front end's island effects follow the original's islands update (`0x10094474`):
  the sky eases toward the nearest island's colour, hallow's lightning strikes on the lobby generator's draws with the
  thunder (`cat_globallobbysfx` 1), the island one-shots come from the same draws instead of a timer (AUDIO-017 resolved),
  the globe plays its own music and ambience (2, 3) and entering a park plays 4. The lobby's rain and bolts are stepped
  and tested but drawn only once the original weather renderer lands (UI-018, UI-940..942; AUDIO.md "Lobby effects").
- **Front-end smoke follows the saved-park offer (October 14):** since entering an island with a saved park first offers to
  continue it (79726a00), the native front-end smoke, which saves a Jungle park earlier on, met that dialog instead of the game
  mode menu and stopped; it now checks the offer and takes "New park".
- **Visitor click, backtrack and queue-join sounds (October 14):** traced in both executables. Opening a visitor's details
  (a park click or a roster activation) plays the visitor click `cat_kids` 129 (`0x1010B808` / tp.exe `0x4B79B0`). Backspace
  over a path cell with no tool on plays `cat_ui` 95 at the cell and removes it, as the original's "backtrack" key does
  (`0x10112430` / `0x40BDA0`). A queue line ending on a path is the join (validator code 8) instead of a refusal, and plays
  `cat_ui` 139 at that cell (`0x100711CC` / `0x527587`). `cat_ui` 95 had been mislabelled a visitor-panel sound and 139 a
  path-tool link-end; AUDIO-023 keeps only 139's second source (path linking to a queue).
- **README's open list corrected (October 14):** RSE EVENT types 1 and 2 are particle effects, not sounds (the positioned
  ride sounds of the other types already play); the weather is drawn, as procedural approximations; visitors can be selected,
  and what remains of the visitor click is its sound and the visitor panel's sounds.
- **Free visits pass the price test (October 13):** a shop or sideshow set to a price, cost of goods and losing chance
  of 0 in play no longer falls back to its starting price in the price test (tp.exe `0x4fde50`), so guests do not refuse
  a free visit; live terms are now recorded as set (`RideVisitorBridge.HasSideshowTerms`). Follow-up to the review of #82.
- **A fairground setup screen (October 14):** the browser's start screen now looks like the original's front end: a night sky
  with bunting and balloons, a yellow-outlined logo, a gold-rimmed panel and chunky gold buttons (Play leads once game files are
  kept; choosing another folder turns blue). CSS and inline SVG only: no original art and no web fonts (the content security
  policy allows none); it fits a phone and keeps still for reduced motion.
- **One write at a time per kept file (October 13):** writes to the same kept browser file now wait for each other, so one
  write's bytes can no longer end up under another's hash; writes to different files stay independent.
- **Kept game files survive a failed rewrite (October 13):** when the browser rewrites a file it keeps (after a failed read),
  the manifest first marks it as not stored and only marks it kept, with the new hash, once the new bytes are written; a clear or
  folder change in between drops the write. A failed rewrite therefore makes the file count as missing instead of offering stale
  bytes under the new hash (BROWSER-ORIGINAL-CACHE-RECOVERY.md).
- **Experimental TPI layout checkpoints (October 13):** the Theme Park Inc slice's pause menu can save its world's layout (cells
  and objects, with asset hashes) to `opentpw-<theme>.tpilayout` and load it as a new experimental session: a versioned, strictly
  validated file written atomically, staged beside the live world before it is retired. Guests, script progress and the clock
  restart; it is not an original TPI save (SAVE-930). Theme Park World paths are unaffected.
- **WAD ranges checked before allocating (October 13):** a WAD's header, directory and member ranges are checked
  against the file before any buffer is reserved, so a short, truncated or wrapped archive is refused with a clear error
  instead of a failed allocation or hidden zeros, and a reopened member read checks its range first. Every shipped archive
  still reads byte for byte as before; an archive with a member past its end is now refused as a whole (WAD-ROBUSTNESS.md).
- **Script civil time (October 13):** TPW scripts' `YEAR`/`MONTH`/`DAY`/`HOUR` read the host's local civil time instead
  of unimplemented zeros, as the Mac RSE does (`0xb2144`, C `time`/`localtime`, PPC-clock.md; VM-902 keeps the timezone and
  flag-ordering limits). The clock is injectable: the M4 gate and the browser digest use a fixed time, because Halloween's
  Haunted Tower reads `HOUR` and its hour reaches the world hash; the game keeps the host clock.
- **Particle event diagnostics (October 13):** an object script's EVENT 1/2 particle request now records its latest prepared
  event (effect, control and position in render coordinates) for diagnostics, cleared on Stop and kept out of saves and the
  simulation hash; particles are still not drawn (PTCL-901).
- **Faulted ride scripts stop their object (October 13):** when an object's root script faults, its clock stops, its child and
  sound scripts are removed, and the object releases its riders, trains, coaster controller and sounds instead of running on half
  stopped; healthy runs hash as before. It is OpenTPW's own containment, now marked `[EXT:vm-fault-containment]` (DETERMINISM.md).
- **Pick visitors in the park (October 13):** a mouse press on a visible visitor in the park view selects it in the visitors'
  roster, using the same sprite rectangle the renderer draws; HUD controls, tools, overlays and the ride view keep the press, and
  the pick only reads the simulation (UI-930: rectangle picking without occlusion or transparent pixels).
- **Localized detail labels (October 14):** the attractions and visitors windows' detail rows show their labels from the
  original UITEXT table (users last month, wait, cost and the rest) instead of the enum names they had shown since the
  overview windows came in; the extra rows keep their six-language supplementary strings.
- **Experimental TPI themes in the browser (October 14):** with Theme Park Inc data, the browser page starts one of its three
  themes (arabian, science, water) only when it is chosen explicitly in the edition selector; Theme Park World starts exactly as
  before, and the kept-files store keeps one witness file per theme folder so the edition check still finds them.
- **Shop and sideshow prices in the HUD (October 14, #109):** a shop's or sideshow's price can be set through the HUD; the edit
  goes through the park economy's price setter, is saved with the park and reaches the guests' price test at the next step
  (UI-933: the original's slider bounds are not traced).
- **Walk camera (October 13, #106):** C arms a terrain-following walk camera in the park and a click starts walking; it is an
  OpenTPW extension (UI-932) that restores the park camera exactly on leaving and stays frozen while a park is being built.
- **Map legend and model totals (October 13):** the live map totals its satisfaction legend by counting
  visitors directly instead of building their cell groups a second time (1,500 visitors: 165,168 to 680
  bytes allocated per call, a managed allocation measurement); MD2 readers reject mesh totals above the
  header before decoding another mesh (every shipped model still parses); new save/weather roundtrip
  tests cover real files, paused and fast speeds, live weather, pending thunder and malformed JSON.
- **FSH image export (October 13):** `--export-fsh-png <file.fsh> <image index> <file.png>` writes one chosen image of an FSH
  file as PNG without starting the game or its renderer; the file is decoded fully before any output exists, an existing
  destination is never overwritten, and the result is moved into place from a temporary file (FSH.md, `[EXT:fsh-export]`).
- **Satisfaction map without regrouping (October 13):** the live map's satisfaction overlay counts each cell's pleased and
  dissatisfied visitors directly, in the order the grouping gave, instead of building transient groups every draw; the drawn
  result is the same (MapCellAggregationTests compares it with the old grouping).
- **Parks built over frames (October 13):** a park chosen in the front end, loaded or visited is built
  over several frames beside the running lobby, with a progress line, instead of in one frame; the
  construction runs the same steps in the same order, and the smoke test finds the same world hash,
  entities and objects. On the AOT build the largest frame of a cold Jungle start fell from 509 to
  107 ms; the park is ready 16 % later (832 against 717 ms), the digest is unchanged. Phase 5 of the browser plan (time-slicing).
- **Files read on demand (October 13):** WAD archives opened from a path and SDT sound banks now read
  a member or entry from the file when it is needed instead of holding the whole file (byte-identical
  on every original archive member and bank entry); `Texture.Missing` is one shared texture, and the
  coaster palette and the park gate's sign release their textures and materials. Live memory in Jungle fell from 94
  to 41 MB and in the lobby from 47 to 24 MB, the page's wasm memory from 254 to 176 MB after the
  first park; textures without a path no longer accumulate, and the mixer stops growing. Digest
  unchanged, start time the same. Phase 4 of the browser plan, second part.
- **Memory across park changes (October 13):** the harness's `switch` scenario (Jungle, lobby,
  another park, lobby, Jungle) with live counters found two leaks. A static `RootPanel.Instance`
  that nothing used kept the first park alive for the whole session; it is gone. The decoded-sample
  cache kept every park's sound categories and banks alive; `SoundEventSystem.Forget` drops them
  when a park or the lobby ends. In the measured run, live memory in the lobby after a park fell from
  67 to 47 MB, after a second park from 81 to 58 MB, and no old park stayed alive. Diagnostics for the harness name the
  static paths to stale parks and the largest holders. Still open: run-time textures without a path,
  whole archive and sound-bank files in memory. Phase 4 of the browser plan, first part.
- **Three start costs removed (October 13):** the RefPack decoder of the WAD archives reflected over
  the assembly on every call and built its output byte by byte (269 ms of the Jungle start); it is
  now one pass, byte-identical to the former decoder on every compressed member of the original
  archives. The game data's path lookups are remembered (`CacheLookups`, forgotten when files are
  added), and the placed ambient sounds start once their samples are decoded instead of in the
  first frame. AOT, three alternating runs: the cold Jungle frame fell from 1,143 to 520 ms, the
  park is ready 804 ms after the click (1,421 before), the first buy menu takes 42 ms (64);
  digests unchanged. The page's wasm memory peak rose from 212 to 254 MB (live memory unchanged,
  fewer collections).
- **Where the park start goes (October 13):** `LoadProfile` times named sections of a park start
  for the browser harness (off by default). Cold Jungle in the AOT build: about 1,040 ms, of which
  texture decoding is 63 ms (6 %) and model parsing 64 ms; the object catalogue (121 ms), the kid
  sprite sets (108 ms), starting the park audio (131 ms) and placing the objects (253 ms) are the
  large shares. A decoded-texture cache (phase 3 of the browser plan) would save too little and
  waits; those sections are the next targets.
- **Game files kept in the browser (October 13):** the browser page can keep the player's own
  game files in OPFS, so a later visit starts with Play instead of choosing the folder. The setup
  screen chooses what is kept: what the parks use (default), all at once, or nothing; a manifest
  rebuilds the file list, a theme not kept yet asks for the folder once more, and Remove kept files
  deletes them. Writing waits until the game has started, so the first start is not slower
  (1,974 against about 1,990 ms); a revisit shows Play after about 165 ms and the lobby 1.6-1.7 s
  after it. `opentpw-originals.js` has Node tests against an in-memory OPFS, and the harness gained
  `--keep` and a `revisit` scenario. Phase 2 of the browser performance plan.
- **Frame rate limit (October 13):** the OpenTPW options page has a frame rate limit (off by
  default, 30 or 60 fps) for the desktop and the browser. It caps drawing only (`FrameLimiter`:
  a grid of 1/cap seconds with jitter slack; the browser skips early animation frames, the
  desktop sleeps); the simulation keeps its fixed clock. In two browser runs per setting a 30 fps
  cap took 43 % off the main thread in the lobby and in Jungle, and a 60 fps cap on a 60 Hz display drew
  59.96–59.99 fps. The browser now keeps its display settings in `/config`, and the harness
  counts drawn frames apart from skipped calls (`--frame-cap`). Phase 1b of the browser plan.
- **Browser build compiled ahead of time (October 13):** the published page (server image) now
  runs AOT-compiled WebAssembly instead of the interpreter. Measured with the harness, three
  alternating runs each on an Apple M5: the cold Jungle construction frame fell from 2,540 to
  1,136 ms, the first buy-menu frame from 104 to 54 ms, main-thread occupancy in the park from
  23 to 15 %, texture decoding by 74 %; the download grows from 11.8 to 16.5 MB. Interpreter, AOT
  and the desktop runtime give identical digests for the theme's textures and a fixed economy and
  guest run (rides, scripts and weather are not in it). The harness gained the
  `digest` scenario and `compare.mjs`. Phase 1 of the browser performance plan.
- **Browser performance harness (October 13):** `tools/web-perf/run.mjs` repeats the browser
  measurements with one command: a local server for the published page and the player's own
  files, a fresh headless Chrome over the DevTools Protocol, and fixed scenarios (start, lobby,
  cold Jungle, first buy-menu selections, memory). The page records frame times only with
  `?perf=1`. The baseline on an Apple M5 (BROWSER-PERFORMANCE.md) is phase 0 of the browser
  performance plan; its Node tests and the page's texture tests now run in CI.

- **Browser:** `OpenTPW.Web` runs the shared game code on WebAssembly with a
  source-compatible WebGL2 replacement for Veldrid (docs/WEB.md). Theme folders
  load when a park first needs them, sound plays through WebAudio, and saves,
  online files and settings persist in IndexedDB. The player's game files stay in
  the browser and are never uploaded. Checked in the browser pane: lobby
  ~119 fps, park ~97 fps without and ~69 fps with audio; an options change
  survived a reload.
- **Browser languages:** the Language toggle now lists the CD's other languages
  (the page copies `<Lang>/data/Language` and `<Lang>/Meshes`, 1 to 3 MB each,
  and the chosen language's advisor speech, 28 MB, when it is chosen; the advisor's
  level speech (the Welcome line) now follows the language on the desktop too) and the saved choice in `/config/language.json` is used
  at start, through the `LanguageStartup` code the desktop shares. Checked in
  the browser: English to Nederlands to Italian, Dutch menu, Dutch Welcome speech bank in use, still Dutch after a reload.
- **Server:** the OpenTPW server also serves the browser game (Brotli, cache
  headers, CSP) and ships as `ghcr.io/meneerkrabs/opentpw-server`. The official
  instance at play.opentpw.io runs on Hetzner behind Caddy, HTTPS only, with
  fail2ban and SSH keys from an allow-list. It updates nightly, and at once when
  a release tag asks it to: the deploy route only leaves a file that the host's
  `opentpw-deploy.path` turns into the regular update (docs/SERVER.md).
- **Website:** opentpw.io (Cloudflare Pages, `website/`), with downloads,
  screenshots, privacy and community rules. It shows the server's news and the
  most-voted parks whose builders opted in to the website list.
- **Releases:** v0.4.0 and v0.5.0 were tagged from this work; v0.5.1–v0.6.1 were
  tagged later.

## Fidelity fixes from the binaries (October 10, 2026)

- **Advisor:** the advisor is drawn without the camera in the bottom right of the
  screen, as the decrypted Windows v2.0 `tp.exe` draws it: translation
  (0.6, −0.6, 0.2), root scale (0.015 × 0.75, 0.001, 0.015), model flag 0x200
  skips the camera and projection. ADVISOR-003/004 are retired, and ADVISOR-023/024
  cover the screen-space reading and non-4:3 outputs. The owner's decision to
  analyse the decrypted `tp.exe` statically is in COMPLETION-PLAN.md, with the
  new BIN prefix `TPW-EXE`.
- **Text:** BF4 text drawn above 1× (HiDPI, 1440p and up) is magnified per glyph
  instead of pixel-doubled (UI-043); 1× stays pixel-exact.
- **Editions:** official Patch 2 data (PATCH-2.md) is supported next to the 1999
  data. The HUD date uses the DATE fonts when present, and the corpus tests
  accept both editions. A game folder copied from the CD finds its banners and
  offers all of its languages; Polish is left out because its character table
  is not decoded.
- **Startup:** shader hot reload is opt-in (`--shader-hot-reload`). On macOS a
  file watcher calls `sync()`, and a hung SMB share kept the game from starting.

## Online world (October 10–11, 2026)

The original online service ran in the game, not the browser (research
summary: manuals, reviews and archived pages; Platinum Tickets were real and
were earned from visits to published parks). What OpenTPW rebuilds:

- **News:** the server serves the operator's Game News and System News
  (`news/*.txt`, `GET /api/v1/news`). The Online World menu shows them under the
  original column titles.
- **Globe:** Go Online opens the original online world from
  `lobby.wad/globe`: the `worldlob` panel and five chat satellites
  (Mac table `data:0x523f0`), and the globe camera of `tp.exe` 0x005DC860
  (orbit, zoom 475/200, damped turning, no self-spin). The 24 globe textures
  form a world map, so cities sit at their real positions.
- **Cities:** the server's cities are public (`GET /api/v1/cities`,
  operator-editable `cities.json`). The defaults hold "North Pole" (the Bullfrog
  pin) and the four known original cities. Pins grow with a city's park count,
  and the cursor names the city.
- **City lobby:** a click on a pin opens the city's lobby (Mac table
  `data:0x50172`), with its ten most-voted parks as their theme islands at the
  lobby's island slots. The panel lists other parks, returns to the globe or
  your own lobby, and enters a park. The rank and name labels follow the item
  detail overlay.
- **Publishing:** Publish Park starts on the globe to choose the city
  (UITEXT 419). The globe also works over a park and restores the park camera
  afterwards. The opt-in website list is chosen in the same form.
- **Approximations:** ONLINE-007–017, listed in ONLINE.md (tick rate, start
  view, steering scale, label places, panel backgrounds, pin scale, island theme
  per park, publish globe layout).
- **Verification:**
  - The full test suite passes: 1,129 passed / 287 skipped / 0 failed without assets.
  - The macOS Metal front-end smoke test passes at 2560×1440, UI scale 2. It checks the globe replacing the lobby, the pins, picking London, a city lobby with three islands, and the globe over a park with the park camera restored.
- **Open:**
  - player counts for the chat satellites;
  - avatars and positional chat in visited parks;
  - Platinum Tickets and a ride shop;
  - the city lobby's own islands when opened from a park;
  - the original layout of the publish globe.

## Render performance, build markers and arrivals (October 11, 2026)

- **Render performance (v0.7.8):** interface draws and their vertex lists are
  reused from frame to frame, interface vertices are written into fenced frame
  slots instead of through the command list, and model parts wholly outside
  the view are skipped. `--no-vsync` and `--frame-stats` measure frames, CPU and
  allocations. With the browser-build changes merged first, a jungle park at
  120 Hz on Apple silicon uses about 0.35 cores instead of 0.85 and allocates
  about 4 MB/s instead of 67 MB/s; the frame rate and GPU time are unchanged.
- **Blueprint markers:** the path and queue tool's preview line and the
  footprint of an object being placed are drawn as the original's blue (or red)
  BlueprintMesh boxes from `Data/generic/dynamic/textures`, traced from the Mac
  binary (docs/reverse/BLUEPRINT.md). They replace the interface squares of
  PATH-001; BLUEPRINT-001..003 remain open, including the entrance/exit and
  track symbols.
- **Queue tool through the HUD:** the jungle smoke test now buys a HasQueue
  ride, places it with a park click and lays its queue out of the entrance. The
  route already worked; without visible markers it was hard to see that the
  queue tool had started.
- **Arrivals:** guests arrive in groups as in the Mac binary: a group every
  `TimeBetweenArrivals` units of four park turns (about 2.5 minutes for the
  shipped 150) after the last one emptied, sized
  `1.2 × (park points + NewParkBonus) / PointsPerVisitor` (0.8 while rain or snow
  is falling, ARRIVAL-002) with park points from
  the rides' and sideshows' AttractionValue and age bonus (docs/GUESTS.md,
  "Arrivals"). A 30-minute M3 run now admits 105 guests instead of 1,800.
  ARRIVAL-001..003 remain open.
- **Verification (macOS arm64, Metal, original data):** the full test suite
  passes (1,686 passed, 91 skipped); the M3 gate passes 16/16 over 30 minutes;
  the M4 gate's rows are unchanged from main (15 pass, 10 fail, 2 unresolved);
  the front-end and jungle smoke tests pass, the jungle one with the new HUD
  queue and marker checks (its Totem pick check fails intermittently, which was
  also seen on an earlier build without these changes); the native Metal shader
  tests pass; the browser project builds. Direct3D 11, Vulkan and WebGL were not run.

## Website: live status and reading options (October 12, 2026)

- **Live status:** `tools/site_status.py` reads the fidelity register, the oracle
  findings (F/X, with how many are marked fixed in C#), the feature matrix, the
  README format table, the merged pull requests (GitHub API in the website
  workflow, `git log --first-parent` as fallback) and the commit, and writes
  `website/docs/status.html` and `status.json`; the front page shows a summary
  from the JSON. The front page's "You can / You cannot yet" block comes from
  `tools/site_status_curated.json`, checked against the feature matrix and the docs.
- **Reading options:** Atkinson Hyperlegible and OpenDyslexic (SIL OFL, vendored
  under `website/fonts/`), high-contrast and calm colours, and a reduce-motion
  switch that also follows the system setting; stored in localStorage only
  (`website/a11y.js`).
- Verification: `tools/test_site_status.py` (synthetic repository, no network),
  `site_docs.py`, `site_status.py` and `check_site.py`; checked in a browser in
  default, high-contrast and calm/OpenDyslexic modes. The Cloudflare deploy was not run.

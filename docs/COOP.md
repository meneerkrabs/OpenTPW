# Building a park together (co-op)

OpenTPW's co-op lets up to four players build one park together online. The original game had no
shared building (its online features were visits, chat parks and movement relay,
[ONLINE-FEATURES.md](reverse/ONLINE-FEATURES.md)), so everything here is an OpenTPW extension of the
online extension `opentpw.online`, labelled `[EXT:online-coop-*]` in the code. The plan with its
phases (C1 to C7) and the comparison of the architectures is in the project notes
(`plans/coop-plan.md`); this page records what is built and the decisions it rests on.

Status: phase C1, the command layer, in progress. No session code exists yet; single player is
unchanged.

## User decisions

The user approved the co-op design on October 19, 2026 (AskUserQuestion in the main session). The
decisions as recorded in the plan's "User decisions" section:

> - Q1 architecture: "Lockstep, host ordent" (A).
> - Q6 money: "Eén gedeelde bank" (A).
> - Q7 invited player's default rights: "Host stelt per speler in" (C).
> - Q2 placement: inside opentpw.online (A).
> - Q3 parks: any of the host's own TPW parks, never TPI (A).
> - Q4 joining: both invite+code and open globe listing, host chooses per session (C).
> - Q5 players: up to 4 (B).
> - Q8 pause/speed: host only (A).
> - Q9 saves: host's slot plus a co-op copy per builder (B).
> - Q10 golden tickets/keys/unlocks: EVERYONE in the session gets them (B).
> - Q11 versions: exactly the same release (A).
> - Q12 guests: normal 1500 (A).
> - Q13 editions/languages: allowed if simulation data matches (A).
> - Q14 undo: own commands, normal refund (A).
> - Q15 browser: join AND host from the start (B in the plan's wording "A join and host"), so C1b browser bit-identity is a hard gate.
> - Q16 mods: allowed if everyone has the same simulation-changing set (A).
> - Q17 cheats: refused in a session (A).
> - Q18 host leaves: the host role moves to another player (B) — needs host migration (snapshot + frame order handover).
> - The proposed EXT labels are NOT yet individually approved; the decisions above approve the design they describe.
> - DetMath (C1b): user chose "Als EXT: deterministic-math" (2026-10-19) for the double-precision portable evaluation of run-time sin/cos/atan2/acos/pow in the simulation; no pins move.
> - EXT labels (2026-10-19, AskUserQuestion in the online-presence session): all proposed `online-coop-*` labels approved as named in section 4; new labels outside that list are asked separately.

In English: host-sequenced lockstep (every player runs the simulation, the host orders the
commands); one shared bank; the host sets each player's rights; the feature lives in the online
extension; any of the host's own Theme Park World parks (never Theme Park Inc); invite with a code or
an open listing on the globe, chosen per session; up to four players; only the host pauses or
changes speed; the host's save plus a co-op copy for each builder; everyone in the session earns the
golden tickets, keys and unlocks; exactly the same release; the normal 1500 guests; different
editions and languages when the simulation data matches; undo of a player's own commands at the
normal refund; the browser joins and hosts from the start; the same simulation-changing mods for
everyone; cheats refused in a session; when the host leaves, the host role moves to another player.

The labels: the plan's first record, made with the design decisions, says the proposed labels were
not yet individually approved. Its later entry records the user's answer, given the same day in the
online-presence session: every proposed `online-coop-*` label in the plan's section 4 is approved as
named (among them `online-coop-commands` and `online-coop-permissions`, the two this code uses: the
command layer and the rights hook), and a label outside that list is asked for separately. The
Totem's commands keep the older `developer-prototype` label.

## The command layer (C1)

`[EXT:online-coop-commands]` Every park edit of a HUD window or tool is a serializable command
(`ParkCommand`, `source/OpenTPW/World/Commands/`) handed to the park's one funnel, `Level.Commands`
(`ParkCommands`). A command is keyed on cells, catalogue entries (their content-root key and archive
path), rotations, attraction ids and modes, never on screen positions or the camera, and it carries
the issuing player's session slot (`Player`, 0 = the local player and host).

- **Before a tick.** A command applies before simulation tick N, the next tick the fixed-step clock
  runs. In single player it runs at once in the input handler, exactly where every edit ran before
  the command layer (between two ticks, before the frame's simulation), so no single-player run and
  no determinism pin changes. `ParkCommands.Schedule` holds a command for a later tick and
  `Level.AdvanceSimulation` applies the held commands at the start of their tick: a replayed log
  uses it now, a session's lockstep frames will.
- **The executor reads no tool state.** `Level.ExecuteCommand` takes everything from the command and
  the world: the armed entry, the build angle, the Move tool's flag and the add-on ride are fields of
  the command, so the same command does the same on every peer. Presentation (the action message,
  sounds) still happens where the edit runs.
- **Multi-click tools stay local.** The path and queue tools keep their start, vertex stack and ghost
  on the issuer's machine; each committed line and each Backspace inside the tool is one command
  (`LayLineCommand`, `UndoLineCommand`) through `ParkCommandLineWriter`. The Move tool's pick-up is a
  command (`PickUpObjectCommand`) and the placement another (`PlaceObjectCommand` with `Moving`); a
  moving placement is accepted only for the entry the same player picked up, because the Move tool's
  purchase skips research and the balance test.
- **Refusals.** A command whose fields are out of range, any command in a read-only visit (economy
  commands included: `ParkEconomy` itself has no visit gate), and a command the player's
  rights refuse are rejected before they touch the park. A refusal changes nothing and is logged
  like an accepted command, so a replay refuses it the same way; only a malformed command, which
  has no byte form, is left out of the log.
  `ParkCommandLevelTests` fuzzes the executor with a seeded stream of every kind (cells off the map
  and outside the codec's range, unknown and negative ids and players, rotations and line modes no
  tool has, non-finite and off-terrain prototype positions): no command throws, and every refused one
  leaves the world hash unchanged.
- **Rights.** `[EXT:online-coop-permissions]` `IParkCommandRights.Allows(player, kind)` is the hook for
  the host's per-player rights (decision Q7). Only the hook exists; single player has none.
- **Nested edits.** A command whose execution issues another (a tool callback) runs that one as part
  of itself; only the outer command is logged.

### Settings

The HUD windows' settings are commands too (`ParkSettingCommand`, `ParkSettingCommands.cs`, kinds 25
and 30 to 53, C1 part 2): the park door, the entrance fee, attractions open and closed (the reopening
guard is asked first, as the open buttons ask it), the ride window's speed, capacity and duration, a
shop's or sideshow's price, a winnable sideshow's prize and chance of losing, a shop's quality and
ingredient, a staff role's training budget, the research lab's effort, workload and current
projects, the loans dialog's list build, taking and paying off loans, ride upgrades and their
cancellation, maintenance calls, the answer to a challenge, renaming and dismissing staff, patrol
areas (the patrol tool's second click and the employee window's clear button), the park's name, the
game speed, the cheats (the secret park name's mode, the Alt keys and the cheats window) and the
developer object panel's open-all and close-all.

- An object's setting names it by its attraction id, as the object commands do; the executor finds
  its economy object through the level.
- Each command keeps the rules of the control that issues it, so a peer or a log cannot do what the
  window would not:
  - a prize only for a winnable sideshow;
  - a maintenance call only for a placed ride or toilet and, for a new call, a hired employee of the
    role who is not on strike (UIHELPTEXT 574, 575);
  - none of the door, the call and the upgrades of a condemned ride, and no door for a ride whose
    track is unfinished (the ride window's refresh, tp.exe `0x4ad4e0`);
  - the ride window's settings for a placed ride only, not the developer prototype;
  - the other cheats only once the cheat mode is on or waits for its turn;
  - a loan only from the Available tab, and the loans dialog's list only where the park has loans.

  It checks what its edit would refuse and then makes the park's own call, so a refused setting
  changes nothing and is logged like any refusal. A park call that throws anyway rejects the command
  (`ParkSettingCommand.Run`) rather than end the game loop.
- An answer names what the player saw, so one that applies a few ticks later (a session's frame)
  cannot act on something else: a challenge answer names its offer (definition and offer day), a loan
  the amount the confirmation showed, a repayment the remaining balance.
- The loans dialog's list build (it opens, a tab is chosen) stores every offer's credit test, which
  the park saves and the world hash reads, so it is a command (`RefreshLoanOffersCommand`) like any
  edit.
- The fields keep to the windows' ranges: prices 0 to 500, a prize's cost 0 to 10,000, chances,
  efforts, workloads and ingredients 0 to 100, the quality slider's three steps (0, 50, 100), a
  training budget up to the training window's 10,000, one step of a research project; a ride's speed
  is held to its slider's `UsageInfo.MinSpeed..MaxSpeed`, as the window's load holds it. A name must be valid
  UTF-16 whose UTF-8 fits the codec's 512-byte string, so it decodes to the same text; anything else
  is malformed and never logged.
- The game speed: a park's is part of its state, so the speed buttons and keys and the HUD's status
  (`EconomyParkStatus`, bound to its level) set it through a command. A visited park has no economy
  and keeps its speed in the HUD, as before.
- The developer panel's open-all and close-all are one command (`SetAllObjectsOpenCommand`, kind 25)
  that opens or closes every placed object without the reopening guard, as the buttons did; its
  prototype button has its own command (`SetPrototypeRunningCommand`, kind 22).
- Hiring is not a setting: an applicant is hired when the Place tool puts the employee down, so it is
  converted with the staff placement.

`ParkSettingCommandTests` checks the economy settings, the loans, a challenge answer, the park's name,
the refusals, a throwing park call, the malformed fields and a visit on a level without game data, and
replays a log of settings; `ParkSettingCommandLevelTests` checks the attraction settings, the
maintenance rules and an upgrade on placed rides, and replays a recorded session of settings on a twin
park with the same hash after every tick. Shop and sideshow prices and prizes are exercised through
their windows (`ShopPriceIngressTests`).

### Serialization

`ParkCommandCodec` writes a command as an envelope (version1 for legacy kinds, version2 for native queue-segment kinds16/17, the kind's tag, the player) and
its fields in declared order: integers as zigzag LEB128 varints in their shortest form, strings as a
byte length and UTF-8 (at most 512 bytes), floats by their bit pattern, cell lists as a count and
pairs (at most 4096). Coordinates are bounded by 4096 and rotations are the four build angles. The
decoder refuses truncated bytes, trailing bytes, an unsupported version/kind combination, an unknown kind, a padded varint and
out-of-range fields. Kind tags are never reused or renumbered. The schema test
(`ParkCommandCodecTests`) pins the bytes of one sample of every kind; a change of any of them needs a
new format version.

### Recording and replay

`ParkCommandRecorder` (`Level.StartCommandRecording`) records every applied command with its tick and
the world hash (`WorldStateHash`) every 60 ticks. `ParkCommandLogFile` is its file: the magic
`OTPWCMDS`, format version2 (version1 logs remain readable but cannot contain new queue commands), the theme, the world seed, the start tick and start hash, then the
commands and checkpoints in tick order and an end mark. `ParkCommandReplay.Run` schedules a log on a
park that stands where the log started and runs it tick by tick, so each command lands where it was
recorded; `FirstMismatch` compares the checkpoints.

`ParkCommandLevelTests` runs a scripted build session (objects, the path tool with lines, undo and
cancel, path removals, Backspace, the Move tool, a sale, the remove tool and refused commands) on a
no-data level, replays its log on a twin level and finds the same hash after every tick.

### The guard

`ParkCommandLayerTests` reads the tool layer's source (the HUD, the UI, the front end and online
screens without their smoke harnesses, the tools, `Level.Input.cs` and the Theme Park Inc HUD) and
fails on a direct call of any converted park mutator. The mutators not yet converted are listed with
the part of C1 that converts them; that list may only shrink, so a converted one must move to the
guarded list.

### Converted and remaining

| Area | Commands | State |
| --- | --- | --- |
| Objects | place (the build tool, the Move tool, track add-ons), the remove tool, remove at a cell, sell from the object and attraction windows, the Move tool's pick-up, the bulldozer (area clear: one command per cleared cell) | converted |
| Paths and queues | path and queue lines, undo inside the tool, path and queue cell removal, Backspace over a cell, the queue edit click | converted |
| Developer prototype | place and remove the Totem, open and close it (the developer panel's button) | converted |
| Winning screen | the Instant Action finale's dismissal (Resume Game, Escape, the right click) | converted |
| Settings | park open and close, ride open and close, the ride window's settings, entrance fee, prices, prizes and shop settings, loans and the loans dialog's list build, dismissal, training budget, research, challenges, maintenance, ride upgrades, park and staff rename, patrol areas, cheats, speed, the developer panel's open-all and close-all | converted |
| Tools | land, pools and waves, slides and pool attractions, pool bridges, kart and water tracks, coasters and the coaster panel's edits, staff hiring and placement | part 3 |

## Known limits of C1

- Wall-time updates outside the tick (`RefreshPools`, the evening, snow, the scene clock), the body
  pose preparation after a frame's ticks (`FlatRideBounceRiders.Prepare`, which changes the riders'
  ride-body state), the staff placement and staff world updates at the start of a frame
  (`StaffPlacement.Reconcile`, `StaffWorld.Synchronize`) and the entities' updates after the
  simulation are not part of a replay; C1b audits them. A replay runs one
  tick per frame, the game several, so a recording made in play must be checked against its
  checkpoints, which the log carries.
- A remote player's command will run the same presentation (the action message, sounds) as a local
  one; C3 separates the issuer's feedback from everyone's.
- The in-memory log keeps the newest 65,536 commands in a ring (adding to a full log overwrites the
  oldest entry in constant time, so a long bulldozer drag does not stall); a recording keeps all.
- A log replays only with the extensions it was recorded with, as a save loads only with its
  simulation-changing extensions: the extensions' systems read the process's extension switches, and
  so do the cheats of the cheats extension (the weather, the arrivals, the ride vehicles). The log
  does not record the switches yet; a session's version gate (C2) checks them.
- The reopening guard an attraction command asks reads whether the coaster tool is editing that
  coaster (`CoasterRide.BeingEdited`), tool state the coaster commands of part 3 make shared.
- The developer and smoke options (`--perf-*`, `--debug-*`) set their parks up directly; a recording
  made with them does not replay.
- The developer prototype can be opened by two kinds (32 with the reopening guard, 22 without), and
  kind 25 opens every placed object without the guard; the host's rights (C3) must name all three.
- A park call that throws after it began to change the park (an event listener of the economy, for
  example) is reported as a rejection with the change made. The throw is the same for the same park,
  so peers stay together, but only while no peer has a listener of its own that throws: presentation
  listeners must never throw.
- The secret park name ("Kevin the" / "Cosmic Cow") turns the cheat mode on inside `Level.RenamePark`,
  so a session that refuses the cheats (decision Q17) must refuse that name in `RenameParkCommand`
  too, not only `QueueCheatCommand`.
- The developer panel's sandbox load (`Level.LoadSandbox`) replaces the park outside the commands; it is
  listed in the guard for C2, where a session replaces the park from the host.

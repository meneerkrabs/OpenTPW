# Building a park together (co-op)

OpenTPW's co-op lets up to four players build one park together online. The original game had no
shared building (its online features were visits, chat parks and movement relay,
[ONLINE-FEATURES.md](reverse/ONLINE-FEATURES.md)), so everything here is an OpenTPW extension of the
online extension `opentpw.online`, labelled `[EXT:online-coop-*]` in the code. The plan with its
phases (C1 to C7) and the comparison of the architectures is in the project notes
(`plans/coop-plan.md`); this page records what is built and the decisions it rests on.

Status: the command layer (C1) is in place, and of the sessions (C2) the server, the game's link, the
host's command stream, the level in a session and the stream's join and resync are built. The game's
join (C2c-2) and the screens (C2d) follow, so no game starts a session yet.

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
named (among them `online-coop-commands`, `online-coop-permissions`, `online-coop-lockstep` and `online-coop-claims`, the four this
code uses: the command layer, the rights hook, the frame-batching independence of the fixed tick, and one drag per coaster with its
conflict rules), and a label outside that list is asked for separately. The
Totem's commands keep the older `developer-prototype` label.

## Sessions (C2)

`[EXT:online-coop-session]` The server side of a shared session exists: the co-op socket, sessions with
their code, the open list, invitations, the version and mod gate, the relay of the host's frames in
order, resume after a lost connection or a restart, and the join snapshots
([ONLINE.md](ONLINE.md), "Shared parks"; part C2a). So does the game's link to it (`CoopLink` under
`OnlineSession`): it opens, resumes and closes the socket and offers the requests and events a shared
park needs. The host's command stream runs over it (C2b-1, below), a level takes part in a session
through `CoopSession` (C2b-2, below), and a member joins a running session or resyncs from a join
snapshot (C2c-1, below). The game's join (C2c-2) and the game's screens (C2d) follow; until then no
game starts a session.

### The host's command stream (C2b-1)

`[EXT:online-coop-lockstep]` A guest's park follows the host's by replaying the host's commands at the
host's ticks, checked by digests (`CoopHostStream`, `CoopGuestStream`; the frames and messages in
`CoopStreamCodec`). Both run each frame's simulation step for their level (`Level.SessionStream`).

- **Records and frames.** Every top-level command the host's funnel applies (accepted, refused, or one
  that threw) is a record of the stream: its number, the tick it applied before, what became of it and
  its bytes. Never its reason text, which depends on the runtime and the language. The host publishes
  the records in frames, at most every 100 ms and only when something changed, each with a horizon:
  every tick below it ran on the host, and every record before it is out. A frame cut at the size
  limit stops its horizon at the first record it does not hold. A paused park sends nothing until a
  command comes.
- **A guest's clock.** At one point of each game frame, before its ticks and never inside one, a guest
  applies the frames that came: each record's command is held until its tick (one for the tick the
  guest stands at applies at once), and its ticks run only up to the horizon. It runs at the park's own
  rate about one published frame behind the horizon and catches up a share of any further lag each
  frame, within a time budget, never past the horizon and never dropping a tick.
- **Checks.** After every 60th tick (a park second) at the normal speed, and once a second of real
  time at the faster ones (every 120th or 240th tick, "Measurements" below), the host takes the
  session digest (`CoopDigest`):
  the world hash and what a session shares that the world hash leaves out. That is the placed objects'
  layout, economy links and run flags; the weather, without the thunder that the frames drain; every
  player's carried catalogue entry, hand and coaster drag; the pending hire; the coasters'
  being-edited marks; and the session clock. Presentation a player may switch alone stays out (the
  winter snow's cover changes no simulation and is no part of a session's identity). After a command that threw
  the host also takes a digest right after it (at most one a frame period). A guest compares each
  digest at the same point, and each record's outcome as it applies. A mismatch is a desync: the
  guest's clock stops, it reports the desync with every progress until the host has seen it, and it
  waits for a resync (C2c).
- **Progress, resends and flow control.**
  - A guest reports its state, its last frame without a gap and its ticks twice a second.
  - A frame lost on its way is asked for and resent from the host's history: one copy on its way at a
    time, at most a megabyte a guest has not acknowledged, and a megabyte a second for all guests. A
    request from before the history is told to resync. After its own resume a guest asks for every
    frame from the first it lacks, also those after the frames it holds past a gap, and a request that
    restarts within a copy on its way keeps that copy's end (C2c-1).
  - The host's park runs at most three park seconds ahead of its slowest following guest, so a slow
    guest paces the session, as lockstep does. A guest that has not reported for ten seconds is not
    waited for until it is back within reach. A guest catching up that holds every frame and stays out
    of reach for a minute is told to resync.
  - While the host's own link is down, its park holds.
  - After a server restart that lost the frame number, the host resends from its oldest frame. A
    server that needs a frame the host no longer holds ends the session with the reason.
- **The session clock.** The ride scripts read the civil time (`[EXT:vm-host-time]`, which two machines
  never share). In a session the host issues a clock command (`SetSessionClockCommand`, kind 100)
  before the session's first tick and, while its park runs, whenever its clock's second changes, so
  every peer's scripts read the same time from the same tick on. Applying the command switches that park's scripts to it, so a
  replayed session log reads the session's time. A Theme Park Inc park reads no civil time and issues
  none.
- **The funnel's session rules** (`ParkCommands.SessionRules`, `InTick`):
  - a command applied inside a tick is dropped, since a peer would apply it at the tick's start;
  - a command issued inside another is refused;
  - a command of this machine that the rights refuse leaves no trace, so a guest's own clicks stay out
    of its log, which is the host's stream; a streamed record the rights refuse is logged as refused,
    so the stream sees the mismatch;
  - a held command never rethrows: its error is in its outcome, which the stream compares.

  In C2 only player 0 builds (`CoopHostOnlyRights`).

The tests run a host and its guests as levels of one asset-free park in one process over an in-memory
server with the real one's guarantees, each driven through whole game frames of random lengths
(`CoopStreamTests`, `CoopStreamCodecTests`).

### The level in a session (C2b-2)

`[EXT:online-coop-lockstep]` `CoopSession` puts a level into the session its link is in, as the host
(`Host`) or as a guest whose park is the host's (`Join`; the join snapshot that gives a guest that
park is C2c's, the screens that call both are C2d's), and owns the level's stream. The level knows its
part (`Level.CoopRole`: none, host or guest).

- **Which park is shared.** `CoopSession.CanShare`: a running Theme Park World park with its guests
  (decision Q3: never Theme Park Inc, and never the developer sandbox, whose load replaces the park
  outside the commands), not a visit and not in a session already, with no cheat waiting for its park
  turn (it would apply inside the session, decision Q17), in a game not started with a developer
  option that changes the park outside the commands. Every `--debug-*` and `--perf-*` option counts,
  and so do the smokes that build their parks directly (`--smoke-test`, `--tpi-smoke-test`,
  `--park-resume-smoke`, `--smoke-presence`) and the browser page of the performance harness
  (`?perf=1`): their setups and hooks build, fill or edit the park directly, so a peer would never
  see their changes.
- **Only the host builds.** In C2 the rights let only player 0 build (`CoopHostOnlyRights`). A
  guest's own click is refused before it runs and leaves no trace in its log. The funnel raises
  `SessionRefused`, and the guest's HUD says "Only the host builds in a shared park"
  (`OpenTpwText.OnlyTheHostBuilds`, in the eleven languages of the OpenTPW texts) with the error
  sound, as it does for a cell it cannot build on.
- **Rules every peer applies alike.** These checks read the funnel's session rules, so a guest
  refuses the host's record as the host did, and the record's outcome check agrees:
  - the cheats (`QueueCheatCommand`) and the secret park name of the rename are refused (decision
    Q17); any other name goes through;
  - so are the developer panel's open-all and close-all (`SetAllObjectsOpenCommand`) and the
    developer prototype's run (`SetPrototypeRunningCommand`), which skip the reopening guard.
- **No local pause.** In a session the level runs at the park's own speed (`GameFlow.SessionTimeScale`)
  whatever screens a player has open: the pause menu, the map window, a postcard or a delete
  confirmation stop no shared park. Only the host pauses or changes the speed, with the speed commands
  (decision Q8); a guest's park follows the host's stream.
- **A guest's park is never saved.** It is the host's: it takes no automatic checkpoint, periodic
  autosave, exit checkpoint or restart snapshot (`GameFlow.AutomaticEligible`, `TakesRestartSnapshot`),
  its pause menu offers no Save, Restart Park or Publish Park, the online menu and the developer panel
  neither publish nor export it (`ParkSharing.ExportLevel`: "A shared park is published by its
  host."), and leaving the park goes to the front end, as a visit does. That holds after its session
  ended too (`Level.IsCoopGuestPark`), so the host's park never replaces the guest's own park of the
  theme, and such a park is never shared again. The host saves as before. A park that lost an
  economy binding is repaired as it starts hosting, while it is still that game's alone; in a session
  a save that would repair one refuses instead, as a shared park changes only through its commands.
  The co-op copies of decision Q9 come later.
- **The end.** The session ends for a game when its level closes (a load, a restart, a replaced park,
  leaving the park: `Level.Dispose`), when the game quits, when its link's session ends (a kick, the
  host leaving, a session that ended while the game was away) and when the host's stream ends it. A
  player's own end leaves the session, and a host's leave ends it for everyone. The stream's rules go
  with the session; a guest's park stays the host's, which that game never saves (where the player
  goes next is the screens' part, C2d).
- **A tool changes nothing in the park.** A tool is one player's alone, so entering one must not change
  what the peers share. The queue tool's entry (after a ride is placed, after a track's circuit closes
  or a coaster is completed, from the ride window's edit button, on a queue click) used to recompute
  the ride's queue on that machine. It now reads the queue as it stands from a copy
  (`RideVisitorBridge.QueueView`), and every queue line command brings the ride's queue up to date
  first, as the queue edit already did, on every peer alike.

`CoopSessionTests` runs a host and a guest of the asset-free park through `CoopSession` over the
in-memory server, and once over the game's real links to a loopback server with a lost socket on
each side.

### Joining a running session (C2c-1)

`[EXT:online-coop-snapshot-join]` A member that joins a session already running, or a guest whose park
lost the stream (a desync, frames the host no longer holds), gets the host's park from a join snapshot
at a cut of the host's stream and goes on from there without leaving the session (design
`coop-c2c-design.md`, v4.1).

- **The cut.** The host takes a capture at the end of a frame, after it published everything: every
  record up to `P` is applied and published, tick `B` runs next and frame `F` is the last, whose
  horizon is `B`. The guest's stream starts at frame `F+1`, record `P+1` and horizon `B`, so the
  stream's checks hold unchanged; the host keeps the frames from `F+1` for it until it acknowledges
  them.
- **The snapshot** (`CoopJoinPayload`): "OTPWJOIN", its version, its generation and its body's length,
  then the body compressed with zlib (the browser runtime's Brotli is a stub): the cut, the session
  digest `D` and the world hash `W` at it, the session state the save leaves out or a load resets
  (`CoopJoinState`: every player's carried catalogue entry and employee in hand, the pending hire and
  the stale on-pointer candidate, the coasters' being-edited marks, the session clock), and the park's
  save. The save is `Level.CaptureJoinSnapshot`: the resume save's parts without the save's
  preparations (the hands stay in hand) and without any repair (an object without its economy binding
  refuses the capture), with no advisor state and the park's kept asset manifest. The host serializes
  and compresses it in slices over frames, as the periodic autosave does, and uploads it to the
  server (`PUT /api/v1/coop/{id}/snapshot`, its own limit per session); the guest downloads it with a
  stall timeout, resumes a broken download with a range and checks its digest, the zlib checksum and
  the cut the host named. The server's limits answer a 429 with a `Retry-After`.
- **Generations.** Each capture has a number, and every stream message carries the generation of its
  member's stream (0 a session's identical start); each side ignores a stream message of another
  generation, so a late resend, desync or too-far-back of an old stream changes nothing.
- **The handshake.** The host assigns a member a capture at once when it joins, or when it asks with
  a failure (a desync, too far back, a failed download or check, a snapshot the server no longer
  keeps, a capture that failed for now), and tells it (`SnapshotAssigned`): the member's old stream
  stops at once. The assignments made within two seconds share one capture, at most one capture is
  on its way, and a member gets at most one every ten seconds. A capture waits while a coaster drag
  is held (its edited pylon is no part of the save), for 30 seconds at most, then fails for now. A
  park the save path refuses (catalogue defaults it cannot write, an object without its economy
  binding) or one too large for a snapshot fails for good. The answer is
  `SnapshotReady` or `SnapshotFailed` (for now, with a delay that doubles with the host's failed
  uploads in a row and is at least the server's Retry-After, up to an hour; or for good: too large, cannot be
  captured, out of step, and the member leaves). An upload that has not answered within three
  minutes is given up for now, and its late answer changes nothing. A member asks with the newest generation it has an
  answer for: an older one gets its assignment and answer again, never a new capture, so a lost
  message, a reconnect or a slow upload is answered once. The host repeats an outstanding assignment
  on the member's reconnect, on its own resume and to a member still on an older generation; the
  member ignores an assignment or answer it has.
- **Flow control.** A member that awaits its capture is not waited for. From its first report of its
  generation it catches up within a join window of its own: the smallest lag it reported plus the
  ordinary window, so the park runs on while it catches up and its lag never grows by more than that;
  once that lag is within the ordinary window it is an ordinary following guest. A joiner that cannot
  run faster than the park paces it, as every following guest does. Back from a stall or a
  reconnect, it is waited for again only once it is within its join window, as an ordinary guest is
  within the ordinary window, so a joiner that fell further behind meanwhile stops no park.
- **The guest's session.** `CoopSession.JoinRunning` starts a guest's session without a park
  (`CoopGuestPhase.Joining`); a following guest's park that loses the stream resyncs the same way
  (`Resyncing`). It buffers the frames, downloads the capture and, at its step (once a game frame
  after the online pump, never inside a callback), lets the game build the candidate (`ICoopJoinHost`),
  checks `D` and `W` at the cut and swaps it in: the old park is detached from the session first, so
  its teardown does not end it. Then it asks the host for every frame from the first it lacks: a
  socket that dropped during the download lost frames that an idle host would never show. A guest
  counts its failed joins: a failed download or check (a candidate the game cannot build is one), and
  a desync or too far back within three minutes of following a park (a capture's, or the session's
  identical start); the third within ten minutes leaves the session. A failure for now, a snapshot the
  server rotated out, and a download the server asks to try again later (too many downloads, a server
  error) do not count, unless the server still asks after ten retries; the guest waits as long as the
  server's `Retry-After` says (five seconds without one; an address limit names its whole minute), and
  for a wait longer than a minute the guest leaves the session with the server's reason (a resyncing
  guest too), since the host keeps the frames after the cut for a while only. A
  download the server refuses for good, or a candidate the game cannot install, leaves the session.
  Only a session with
  the game's join host (`ICoopJoinHost`, C2c-2) resyncs: a guest without one stops as in C2b.
- **Golden tickets in a session.** While a park is in a session it is outside its player's progress:
  every peer decides the global tickets by the park's own records. When the session ends, the park
  returns to its player's progress through a hand-back: each global ticket won in the session goes
  through the player-wide rule with its record at the win, and one the rule keeps with another park is
  taken from the park. Every way out of a shared park (leaving it, a load, a restart, quitting) ends
  the session and runs the hand-back before anything saves the park, also when a progress check then
  keeps the player in the park (the player asked to leave; a host's leave ends the session for
  everyone). A guest's park, also one that joined at the session's identical start, never hands the
  host's tickets to its player.
- **The digest's version 2.** A capture read the coaster drags through a getter that made their set,
  and the digest counted whether the set existed: one machine's read would have been a desync. The
  capture reads the drags without making the set, and the digest hashes no set as an empty one.

`CoopJoinTests` covers the snapshot's parts (round trips, every malformed input, the session state
bringing a twin's per-player state to the host's, a capture that changes nothing in the host's park).
`CoopJoinSessionTests` runs joins and resyncs over the in-memory server, the host's captures real and
the guest's candidate built by replay (a twin from the identical start driven by the host's records to
the cut, its per-player state cleared and the capture's applied): a late joiner with frames lost on
their way, lost assignments and answers, a slow upload and a reconnect, joiners sharing a capture or
getting the next, a request storm, a desync and too far back resynced without leaving, stale messages,
the join window, failed and permanent failures and the host's growing waits, the failure budget, a held
coaster drag, a park too large or impossible to capture, an upload that never answers, a socket lost
during the download, a joiner back from a stall, busy and refused downloads, a candidate that cannot be
built or installed, and a late join through the real links, the server's snapshot store and HTTP.
`CoopSnapshotDownloadTests` runs the client's ranged download against a scripted server (a stall or a
break resumed with a range, a server that ignores the range, a line that broke after its last byte,
the length and digest checks) and the link's reading of each transfer failure. The save path's
completeness is the game's join's to prove (C2c-2, with data): the asset-free park has no full restore.

### Measurements

The design fixed two constants only after measuring them (`coop-c2b-design.md`, section 11).

- **The digest's cost.** The digest is the world hash, which serialises the economy, plus a little
  more. The median of 15 in the published browser build (Mono AOT, Chrome 155 headless, Apple M5;
  `tools/web-perf/run.mjs --scenarios start,jungle,coopDigest --crowd 1500`): Jungle as Full
  Simulation starts it, 2.3 ms; with 80 of the buy menu's rides and shops built, 5.7 ms; with 1,500
  guests staged on top, 6.7 ms; the same park after 20 seconds of play, 10.0 ms. On the desktop
  (Release, `--perf-run 3600 --perf-crowd 1500 --perf-digest`, the median of five every 600th frame)
  the opening Jungle with 1,400 to 1,500 guests took 4.5 to 5.3 ms once warm. A steady Jungle frame
  in the browser has a p95 of 4.5 ms, so a digest a park second fits at the normal speed, but four a
  second at the fastest (40 ms a second, each one most of a frame) do not: a faster park takes its
  digests as many park seconds apart as its speed, once a second of real time
  (`CoopHostStream.CheckpointInterval`).
- **Frames.** Through a build session at the fastest speed (`CoopSessionTests`) the host published
  9.2 frames a second, 25 bytes on average and 81 at most (231 bytes a second), and 19 digests in 20
  seconds. The 100 ms frame period stays.

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
  of itself; only the outer command is logged. The inner command takes the outer one's player and is
  not asked of the rights again, so a guest that replays the host's command runs it the same way.
- **The issuer's follow-ups.** What a HUD does in answer to its own edit, such as the tool a placed ride
  opens, runs once the command has returned (`ParkCommands.AfterIssue`), so the commands it issues are
  logged on their own instead of hiding inside the placement. A scheduled command (a replay, a
  peer's) is not this machine's issue (`ParkCommands.IsIssuing`), and no tool follows it.
- **A command that throws.** A run that throws (a rule the command's own checks missed: a hostile
  peer's or a damaged log's command) is a rejection that carries the exception
  (`ParkCommandResult.Error`), with whatever the command wrote before the throw, and is logged like any
  other: a replay or a peer throws at the same place and stays in step, and the funnel goes on, however
  deep in nested commands the throw was. The setting and tool commands turn the park's own refusals
  (`InvalidOperationException` and the like) into rejections; `ParkCommands.Apply` catches the rest and
  writes the exception to the diagnostic log. The tests and a strict developer run
  (`OPENTPW_STRICT_COMMANDS=1`, `ParkCommands.Strict`) rethrow it once it is logged, so no test passes
  over a crashed command.
- **No command inside a tick.** The level marks each simulation tick from after its held commands
  applied (`ParkCommands.EnterTick`): a command applied then would apply inside tick N on this machine
  but at the start of tick N in a replay or on a peer (its log entry says "before tick N"). The tests
  and a strict run throw on one, so every code path that does it is found; a game of one runs it at
  once, as it always did, and a session (C2) will not take one. A tick-end seam that wants an edit
  holds it for a later tick (`ParkCommands.Schedule`).

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
  is held to its slider's `UsageInfo.MinSpeed..MaxSpeed`, as the window's load holds it, and its
  capacity and duration go to the ride's setters as they are (the setters clamp them, as before), so a
  mod ride whose ranges pass the shipped ones applies as it did. A name must be valid
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

### Tools

The tools' writes are commands too (`ParkToolCommand`, `ParkToolCommands.cs`, `PoolCommands.cs`,
`TrackCommands.cs`, `CoasterCommands.cs`, kinds 54 to 98 with gaps, C1 part 3): the land, the hire and the
employee in hand, the pools extension's tool and wave machines, the kart and water rides' track tools,
and the coaster tool with its panel. A tool keeps its own state between clicks (a stored fence post, a
stroke and its undo stack, the candidate being placed or the employee in hand, a tile picked for a cut,
the panel's selection and a drag's pointer) and issues one
command for each write it made before the command layer, at the same moment, so no single-player run
changes.

- **Land.** The Buy Land tool's second click is `BuyLandCommand`, with the post the first click stored
  and the clicked one. The command checks that both are on the fence's post lattice (the tool snaps
  every click there) and runs the validator again with the money test; its value is the verdict the
  tool shows. A Theme Park Inc park sells no land: the command refuses it before its economy would
  throw.
- **Staff.** The hire button begins a placement (`BeginStaffHireCommand`: the candidate is marked as
  being placed, so it does not expire); the Place tool's valid click hires it and puts the employee
  down (`PlaceStaffCommand`); right click, Escape, another tool or a save end the placement
  (`CancelStaffHireCommand`). The pick-up button puts an employee in the issuing player's hand
  (`PickUpStaffCommand`), and the Carry tool's valid click or its cancel puts it down
  (`DropStaffCommand`; a cancel and a save drop it back on the cell it came from). A level without
  the Place tool (no guest world) hires nobody.
  - The employee in a player's hand is the level's state (`StaffHands`), not the tool's: the frame's
    reconcile (`StaffPlacement.Reconcile`, which frees a member an original save left picked up)
    leaves a member a command picked up, on a peer and in a replay too.
  - A full thing pool refuses a hire before the placement ends (the hire threw after it before).
- **Pools.** Each cell a paint or erase stroke writes is a command (`PaintPoolCellCommand`,
  `ErasePoolCellCommand`), as the tool wrote each cell while the drag crossed it; Backspace takes a
  stroke back with one command that carries its cells (`UndoPoolStrokeCommand`). A stroke of more cells
  than a command takes (4096) goes back in chunks that write in the one command's order (an erase
  stroke's cells painted again in order, a paint stroke's river marks first and then its cells from
  the end), and a repaint that runs out of money issues no further chunk. Each slide piece and
  its undo (`LayPoolSlidePieceCommand`, `UndoPoolSlidePieceCommand`) and each bridge built or taken down
  (`PlacePoolBridgeCommand`, `RemovePoolBridgeCommand`) are commands, and the wave machine window's tick
  is `SetPoolWavesCommand` (an unchanged choice writes nothing). The strokes, the drag and the undo
  stack stay the tool's; the builders' own checks refuse every write while the extension is off.
- **Kart and water tracks.** Each line of a ride's track tool and each Backspace inside it is a command
  (`LayTrackLineCommand`, `UndoTrackLineCommand`, through `ParkCommandTrackWriter`), and the edit
  tool's second click cuts through `CutTrackCommand`. The command walks the circuit again
  (`Level.WalkTrack`: the controller, the add-ons, the pieces and the ride's door); the tool's step after
  a valid walk (it ends, the queue tool follows, or a line clicked with exactly Ctrl restarts it) is the
  issuer's alone (`Level.ContinueTrackTool`), so a peer's or a replayed command moves no tool. A cut
  needs a closed circuit and two tiles the edit tool's test accepts, as the tool does.
  - The line tool on an open circuit starts from the walk to the track's open end
    (`TrackEditor.FindOpenEnd`), which sets every walked tile's overlap word, the word Backspace reads
    to decide whether a tile goes. So the walk is a command: `OpenTrackLineToolCommand` when the track
    button opens the tool, and inside `CutTrackCommand` when the cut leaves the circuit open. The tool
    takes only the corners the walk found.
  - A track add-on's placement or removal walks the circuit inside its command, without the tool's
    step (`Level.WalkTrack`).

- **Coasters.** The coaster tool's entry and exit are commands (`BeginCoasterToolCommand`,
  `EndCoasterToolCommand`). The entry closes the ride, gives its controller the tool's state, opens a
  closed circuit whose closing link fails and marks the coaster as being edited, which guests read
  (`CoasterRide.BeingEdited`: none boards it); the exit clears the mark and opens a ride the tool took
  out of service.
  - Each click of the build buttons is `CoasterClickCommand`, which carries the panel's height and loop
    edit mode, the tool state the placement reads. A click on the closed circuit's station end
    completes the track; the tool's end after it (the panel closes, the queue tool follows) is the
    issuer's. Backspace is `RemoveLastCoasterPylonCommand`.
  - A drag in the coaster panel is a press (`BeginCoasterNodeEditCommand`, with the drag: move,
    height, rotate, tilt or loop tilt), one command for each step (`EditCoasterNodeCommand`: the loft,
    yaw, bank, incline, loop rotation or cell), as the panel wrote the pylon each frame, and a release
    (`EndCoasterNodeEditCommand`) that settles the money of a height or move drag. The stack buttons
    and the demolish button's cut are `SwapCoasterStackCommand` and `CutCoasterCommand`.
  - The commands keep the panel's rules. The press checks the pylon's controls and keeps the range
    the panel clamps to (`CoasterBuilder.DragRange`); a step must be the drag's own value and one the
    clamp can give (an end of the range, or a value between its ends). A click in loop mode needs the
    loop button; the click tests nothing else of the panel, as the original's click handler does not.
    Every panel command needs its coaster in the tool, whose entry let the riders off.
  - `[EXT:online-coop-claims]` One drag runs on a coaster at a time, as one pointer gives in a game
    of one. A press releases the player's own drag still held and waits while another player's drag
    runs; so do a stack swap (the drag's release could change the stack it was asked on), the close
    (it checks the last pylon, which a drag can hold) and the completion (it ends the tool every
    drag needs). A click that places a pylon, Backspace and the cut release the running drag first,
    as its own release would, so no drag settles against a pylon list or a neighbour that changed
    under it. The tool's exit ends the drags on its coaster, and a station that goes drops them
    unsettled with its track.
  - The pylon a player is editing is the level's state (`CoasterEdits`), as the employee in hand is;
    the panel, its selection and the drag's pointer stay the issuer's. The height and loop mode a click
    carries are the issuer's panel state: the builder of a peer keeps its own, and the issuer's takes
    the state the placement left (`CoasterClickDone.Next`).
  - The coaster tool a placed station opens, the water and kart track tool and the queue tool follow
    the HUD's own placement after its command, so their entry commands are logged.

`ParkToolCommandLevelTests` drives the staff and pool tools on a level without game data and applies
their logged commands to a twin park, which reaches the same hash (the twin's reconcile between the
pick-up and the drop keeps the employee picked up). `TrackAddOnLevelTests` applies a kart ride's
logged lines, undo, cut, the line tool's reopening and Backspace after it to a twin whose track layer
and ride controller match the issuer's. `CoasterCommandTests` runs a coaster tool session (clicks,
Backspace, the panel's drags and a cut) and builds the same coaster (its pylons, its controller and
the money paid) on a twin from the logged commands alone, and fuzzes three players' panel commands on
a coaster in each state the tool reaches (no refused command changes the coaster or the drags);
`ParkToolCommandLevelTests` enters and leaves the coaster tool on a level through its commands and
completes a coaster there that a twin completes from the log. `PoolsTests` takes back a
stroke longer than a command in chunks, and `ParkLandTests` checks the land command's posts and
verdict.

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

`ParkCommandRecorder` records every applied command with its tick, and the world hash
(`WorldStateHash`) at its checkpoints: the level's own recording (`Level.StartCommandRecording`,
`--record-commands`) takes one every 60 ticks. `ParkCommandLogFile` is its file: the magic
`OTPWCMDS`, format version 3, the theme, the world seed, the start tick and start hash, a flags byte
(bit 0: the commands applied under a co-op session's rules), then the commands and checkpoints in
tick order and an end mark. Versions 1 and 2 have no flags byte and remain readable (version 1 cannot
contain the new queue commands). `ParkCommandReplay.Run` schedules a log on a park that stands where
the log started and runs it tick by tick, so each command lands where it was recorded, under the
session's rules when the log says so; `FirstMismatch` compares the checkpoints.

A co-op session records its own log (`CoopHostStream.SessionLog`, kept at the first desync and when
the session ends): every command from the session's start, with the world hash of each tick digest as
its checkpoints. It takes none between the digests (a world hash is most of a digest's cost), only at
its start, at the first desync and at its end. It is the only log that sets the session-rules flag:
the refusals of the cheats, the secret park name, the developer kinds and a command issued inside
another read those rules, and `ParkCommandReplay.Run` replays the log under them on the session's
starting park.

`ParkCommandLevelTests` runs a scripted build session (objects, the path tool with lines, undo and
cancel, path removals, Backspace, the Move tool, a sale, the remove tool and refused commands) on a
no-data level, replays its log on a twin level and finds the same hash after every tick.

### The guard

`ParkCommandLayerTests` reads the tool layer's source (the HUD, the UI, the front end and online
screens without their smoke harnesses, the tools, `Level.Input.cs` and the Theme Park Inc HUD) and
fails on a direct call of any converted park mutator. A visit's park construction from its package
(`SharedParkLayout`) is not a tool and is left out, as a save's restore is. The mutators not yet converted are listed with
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
| Tools | land, staff hiring and the employee in hand, pool strokes and their undo, wave machines, slide pieces, pool bridges, kart and water track lines, their undo, the edit tool's cut and the line tool's walk to the open end | converted |
| Coasters | the coaster tool's entry and exit, its pylons and Backspace, the circuit's closing and completion, the coaster panel's drags, stack swaps and cuts | converted |

## Known limits of C1

- The frame's work outside the fixed tick was audited in C1b ([DETERMINISM.md](DETERMINISM.md),
  "Frame batching"): the pools' drawing, the evening, the snow's easing, the scene clock and the
  entities' updates are presentation; the staff host's bookkeeping (`StaffPlacement.Reconcile`,
  `StaffWorld.Synchronize`) now runs at the start of every tick after its commands and the ride
  bodies' pose phase (`FlatRideBounceRiders.Prepare`) at its end. The reconcile frees only a member an
  original save left picked up with nobody holding it: the employees in hand are the level's state since
  part 3. The park after a tick therefore depends only on the ticks and the commands, so a replay may
  run its ticks in frames of any size (`FrameBatchingTests`, and with game data the park-resume smoke's
  restore phase). What stays frame-coupled is presentation a save keeps but no hash or script reads:
  the particle pool, the ADDOBJ records of ended effects and the hoardings' rise, so two players' saves
  can differ there. The clock's remainder of a tick follows the frames' time; a session guest's clock
  (C2b) runs whole ticks from the host's stream.
- A remote player's command will run the same presentation (the action message, sounds) as a local
  one; C3 separates the issuer's feedback from everyone's.
- The in-memory log keeps the newest 65,536 commands in a ring (adding to a full log overwrites the
  oldest entry in constant time, so a long bulldozer drag does not stall); a recording keeps all.
- A log replays only with the extensions it was recorded with, as a save loads only with its
  simulation-changing extensions: the extensions' systems read the process's extension switches, and
  so do the cheats of the cheats extension (the weather, the arrivals, the ride vehicles). The log
  does not record the switches yet; a session's version gate (C2) checks them.
- The coaster-being-edited mark (`CoasterRide.BeingEdited`, which guests and the reopening guard read)
  is one flag per coaster, as the original's was one global: two players editing the same coaster
  clear it when the first one leaves the tool. C3 must keep a second player out of a coaster another
  edits, or count the editors.
- A command a tool callback issues inside another command runs as part of it, with its player
  (Nested edits). This machine's tools differ from a peer's, so a callback that only this machine's
  tool makes must not write inside a peer's command, which every peer runs alike: a session refuses
  every command issued inside another (C2b-1, `ParkCommands.SessionRules`). The HUD's
  follow-ups of its own placement already wait for its command to return (`ParkCommands.AfterIssue`),
  and a coaster station that goes drops the drags on it before this machine's panel closes, so that
  panel's release finds no drag to end.
- The pylon each player is editing in a coaster panel drag (`CoasterEdits`) is not in the world hash,
  as the employees in hand are not; the session digest covers both (C2b-1). A join snapshot waits
  for every drag's release instead of carrying it (C2c-1), since a drag's edited pylon is no part of
  the save.
- The developer and smoke options (`--perf-*`, `--debug-*`, `--smoke-test`, `--tpi-smoke-test`,
  `--park-resume-smoke`, `--smoke-presence`) set their parks up directly; a recording made with them
  does not replay, and a game started with them shares no park (C2b-2).
- The level's own recording (`--record-commands`) never sets the session-rules flag, and one flag
  could not describe a recording that spans a session's start or end: a recording made during a
  session replays the commands the session refused as applied. A session replays from its own log
  ("Recording and replay").
- The developer prototype can be opened by two kinds (32 with the reopening guard, 22 without), and
  kind 25 opens every placed object without the guard; the coaster tool's entry and exit (90, 91)
  close and open a coaster without it too. A session refuses 22 and 25 on every peer (C2b-2); the
  host's rights (C3) must name the other three.
- A park call that throws after it began to change the park (an event listener of the economy, for
  example) is reported as a rejection with the change made. The throw is the same for the same park,
  so peers stay together, but only while no peer has a listener of its own that throws: presentation
  listeners must never throw. A tool that issued the command takes the rejection as no write (the
  track line writer answers a stopped line, for example), so its undo stack misses what was written.
- The secret park name ("Kevin the" / "Cosmic Cow") turns the cheat mode on inside `Level.RenamePark`,
  so a session, which refuses the cheats (decision Q17), refuses that name in `RenameParkCommand` as
  well as the cheats in `QueueCheatCommand` (C2b-2).
- The developer panel's sandbox load (`Level.LoadSandbox`) replaces the park outside the commands; it
  stays in the guard's list, as the developer sandbox is never shared (C2b-2, `CoopSession.CanShare`).
- The employees in the players' hands (`StaffHands`) are not in the world hash, as the Move tool's
  carried entry is not (the session digest covers both, C2b-1). A save drops the employee in the saving machine's own hand first (a `DropStaffCommand`), so
  that hand is empty in the save; another player's held employee is saved picked up. A session's join
  snapshot does not drop it: it skips the save's preparations and carries every player's hand
  (C2c-1).
- The staff tool's cancel ends the tool even when its drop is refused, as a player's rights could
  refuse it mid-carry (C3): the employee then stays in that player's hand and the player's next
  pick-up is refused. C3 must always allow the drop back to where the employee came from.
- A hire's pending candidate (`ParkStaff.PendingPlacementId`) is one per park: two players placing hires
  at once would take it from each other (C3 gives each player a placement).
- Opening the queue tool (after a ride is placed, its track circuit closes or its coaster is completed,
  from the ride window or on a queue click) recomputed the ride's queue on the issuer's machine
  (`Level.EnterQueueTool`), outside the commands. Since C2b-2 it reads the queue from a copy, and the
  queue line commands bring the ride's queue up to date first ("The level in a session"). The tool's
  previews still read the ride's own queue, which a grid edit since the last recompute (in a paused
  park) leaves behind until the line's command or the next tick takes it in.
- The game's join (C2c-2) is not built yet: the game's candidate build with a join restore mode (the
  audit of every restore step against a live park), the swap in `GameFlow`, its native smoke and the
  join's measurements. A snapshot's upload has the online client's overall 60-second timeout (its
  download resumes with a range instead). A server that names a frame the host no longer holds still
  ends the session (C2c-3).
- A join capture uses the park's kept asset manifest; when the catalogue changed since the park's
  start (the pools extension adds its objects as they are used), the capture's frame hashes it again,
  as the periodic autosave's does.
- An autosave taken inside a session goes through the player's ordinary ticket reconcile when it is
  loaded later (C6 records a session's tickets).
- A guest's park must be built as a guest from its start (C2c's join): the game takes the restart
  snapshot when a park starts (`GameFlow.TakeRestartSnapshot`), before a session could mark the park
  as a guest's, and would write the host's park over the guest's own restart file.
- A guest's HUD still offers the build tools (C2d): each refused click says "Only the host builds",
  at most once every 1.5 seconds. Two tools add their own feedback to it: Buy Land's refused second
  click also says that the land cannot be bought, with a second error sound, and a refused queue line
  still plays the queue-join sound. Opening the loans window refreshes its offers by command, which a guest is refused,
  and the winning screen's Resume is the host's alone, so a guest's winning screen stays until the
  host dismisses it.

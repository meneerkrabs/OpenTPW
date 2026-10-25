# Building a park together (co-op)

OpenTPW's co-op lets up to four players build one park together online. The original game had no
shared building (its online features were visits, chat parks and movement relay,
[ONLINE-FEATURES.md](reverse/ONLINE-FEATURES.md)), so everything here is an OpenTPW extension of the
online extension `opentpw.online`, labelled `[EXT:online-coop-*]` in the code. The plan with its
phases (C1 to C7) and the comparison of the architectures is in the project notes
(`plans/coop-plan.md`); this page records what is built and the decisions it rests on.

Status: the command layer (C1) is in place. Of the sessions (C2), the server, the game's link, the host's
command stream, the level in a session, the stream's join and resync, the host's skip of frames it no
longer holds, the game's join and the browser's are built, and the joins are measured. Players share and
join a park through the game's screens (C2d), and the live acceptance runs two native games through the
real server (C2d-5a); the page's sides of it follow (C2d-5b). Of building by every player (C3), the
requests, their records and the host's grants are built (C3a-1); the tools follow (C3a-2).

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
snapshot (C2c-1, below), which the game builds and swaps in (C2c-2a, below) and which is measured on the
desktop (C2c-2b-1, "Measurements"). A browser joins (C2c-2b-2, below) and hosts (C2c-2b-4, below) in the
smokes, and its own transport carries a session through the real server (C2c-2b-5, below). The game's
screens (C2d) give a session its own chat room (C2d-1, [ONLINE.md](ONLINE.md), "Shared parks"). The
player shares the open park from its pause menu or a saved park from the lobby (C2d-2, below), and joins
with a code, an invitation or the open list (C2d-3, below). The co-builders walk as figures (C2d-4, below),
and two native games share a park live through the real server (C2d-5a, "The live acceptance" below).

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
    server that needs a frame the host no longer holds is told the frames go on past it, and every
    member gets the park again (C2c-3, "The host's trimmed frame" below).
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
  after the online pump, never inside a callback), lets the game build the candidate over frames (`ICoopJoinHost`, below),
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
completeness is the game's join's to prove with the game's data: the asset-free park has no full restore
(the native smoke below does).

### The game's join (C2c-2a)

`[EXT:online-coop-snapshot-join]` The game's side of a join (`coop-c2c2-plan.md`, v3): the game is the
session's join host (`ICoopJoinHost`). It builds the host's park from a join snapshot over frames, beside
the live game, and swaps it in.

- **Entries.** `GameFlow.JoinSharedPark` joins a running session from the front end. It is refused while
  a park is open, a join is held, a park is being started or a transition waits.
  `GameFlow.FollowSharedPark` follows a session with the open park, which must be the host's park at the
  session's identical start (C2b); a park that loses the stream later resyncs through the same join host.
  The screens that call them are C2d's.
- **The step.** The session steps once a frame, right after the online pump and before the HUD's and the
  level's update (`GameFlow.StepCoopSession`). It waits while a park is built over frames or a transition
  waits: there is one construction scene at a time, and a queued transition would tear the guest's park
  down after the swap.
- **The build** (`CoopJoinBuild`, in steps within the park build's frame budget):
  1. It makes the save codec's reflection metadata in steps, one type each, as a park start makes it for
     its first snapshot (`ParkResumeJson.WarmupSteps`; a process's first parse would make it in one go,
     a warm process finds it made), and so the metadata of the economy's digest, which serializes the
     park save with the serializer's default options (`WorldStateHash.WarmupSteps`, C2c-2b-3i; both
     through `JsonMetadataWarmup`). It then reads the save over steps (`ParkResumeSaveFile.DeserializeSteps`,
     C2c-2b-3h): its parse takes a 32 KiB slice of the bytes a step, by the serializer's own asynchronous
     reader fed a slice a step (`SlicedJsonDeserializer`: the same value and the same failures, message for
     message, as one parse), and then its shape's checks run in their steps. The save must hold the gate's
     and the staff's state.
  2. It refuses at once when this game's simulation mods are not the host's or the park's (decision Q16).
  3. It waits for the theme's files, loads the object catalogue in steps, and reads the balance settings
     and the economy catalogue. A browser copies a theme's files on its first use, between frames: the
     build asks once a frame whether they are there and leaves the rest of the frame to the live game
     (C2c-2b-3), instead of asking until the frame's budget is spent.
  4. It hashes this game's asset manifest in steps (kept for the next join while the catalogue is the
     same) and refuses at once when it differs from the host's (decision Q11): "This game's files differ
     from the host's".
  5. It validates the snapshot in steps and restores the save.
  6. It builds the level over frames in the join restore mode (below), with its HUD and, when the player
     has the automatic advisor on, a fresh advisor. A construction step that announces a long one next
     (`Level.NextStepIsLong`) ends the frame's slice, so the long one starts a slice of its own, as in a
     park start (docs/BROWSER-PERFORMANCE.md, "Build steps"). Before it restores the saved objects, the
     level reads what each saved type's first use reads, in steps of its own (`SavedTypePreparation`,
     C2c-2b-3g): first every type's model and, when its parts show a sign and it is not the gate, its
     sign, composed on the desktop's sign worker (`ObjectSigns.ComposeAhead`) or in the page over the
     composition's own steps (`ObjectSigns.ComposeAheadSteps`) and handed to the type's first object as
     the worker's is, its log written then; then every type's clips, each texture its parts and its
     animated slots draw, and its hoarding boards with the level's hoarding textures. The steps fill only
     the caches an object reads and touch nothing of the park. A read that fails leaves its failure to the
     object: a texture's lookup ahead of use remembers no failed read of a folder, only a missing folder.
     The steps take the build's files as unchanged while it runs. The level then restores the saved objects one a step
     (`ParkLayoutSnapshot.RestoreObjectSteps`, C2c-2b-3d): the first step checks the whole snapshot and
     places nothing, each next places one object in the saved order with its saved seed and ids, and the
     last restores the allocators. After the level's entities it restores the saved simulation a part a
     step (`Level.RestoreResumeSimulationSteps`, C2c-2b-3j): the objects' runtimes, the scripts, the
     guests' routing, the guests, the weather, the gate and the staff, the coasters with the tracks, the
     slides and tours, then the drawn tracks, in the order of one call. A restore given up between its
     steps is not rolled back: the build drops its candidate whole. An ordinary resume still builds its
     level in one call.
  7. It does the one-time work of the joined park's first frame where an entity can do it ahead
     (`IFirstFramePreparation`, `[EXT:park-entry-preparation]`, the stage `prepare`): the coasters'
     views, their types' assets and their meshes, the pool bridges' assets and meshes, and the
     swimmers' sprite sets, read and decoded ahead (`Level.PrepareSwimSpriteSteps`; their renderer is
     still made in the first frame, in its place among the entities). Presentation only: the steps read
     the park and change neither its state nor the entities' order, and a preparation that fails leaves
     its work to the first frame, which meets the same failure as before. When the build made an
     advisor, the stage then reads what that advisor's first line reads, as a park start's build does
     (`AdvisorEntryPreparation`, C2c-2b-3e): the response, caption and costume tables, the advisor
     shader in a slice of its own, the global and level rigs, both archives' clip lengths and the theme
     costume. Then it decodes the lines the park's start says first (the Welcome, in Instant Action
     also the prebuilt park's), a slice of their MPEG a step (`AdvisorEntryPreparation.LineSteps`). It
     keeps them in a store of its own (`AdvisorEntryPreparation.Store`), which the live game's frames do
     not drop. The swap hands that store to the shared one once the candidate is the game's park. The
     results are kept until the joined advisor has made its presentation, cannot make one, or advice is
     off; a line decoded ahead waits for its own saying until a teardown. Before the advisor is made,
     its stage warms the shipped message tables a step each (`AdvisorTables.WarmSteps`), and the
     advisor's clock is held from its creation until its first update, so the build's last frames are
     no time it waits. A park start decodes the same lines in its own preparation.
  8. It runs the economy digest's serialization once ahead, over the candidate's economy, a part of its
     park save a step (the stage `digest`, `WorldStateHash.SerializationWarmupSteps`, C2c-2b-3i): the
     save's capture, then each of its parts serialized and written in the digest's canonical form, then
     the whole save's part of the digest. The check at the cut then runs none of that code for the first
     time, but for a part that failed here. Nothing is kept, the economy is only read and no hash changes; a save that cannot be captured or
     a part that cannot be written is left for the check to meet.

  A refusal (`CoopJoinRefusedException`) ends the join, since no capture would build; any other failure
  is a failed check (above), and so is a build that runs longer than 300 seconds.
- **Beside the live game.** The build runs in a construction scene of its own
  (`EntityConstructionScope`). Each step resumes it and pauses it again, so the front end, or the
  guest's own park in a resync, stays live and untouched while the build runs.
- **The check and the swap.** The check at the cut takes the frame the build completes in (or the next,
  when its last step spent the frame's budget), and the swap the frame after it (C2c-2b-3c): each has a
  frame's worth of first-time work, so they never share one. The candidate waits set aside and unchanged
  between them; a failed check ends the build there. The swapped-in park's first frame applies the
  host's frames that came but runs no ticks (`CoopGuestStream.HoldTicksOneFrame`): they run from the
  next frame on, deferred, never dropped, since the park after a tick depends on the ticks and the
  records only. A join's catch-up ends at a frame point that ran ticks.
- **The swap** (`GameFlow.InstallJoin`), the frame after the check:
  1. The build's HUD is made again if the player switched the language meanwhile.
  2. The old park (none on a first join) is torn down without ending the session and without applying a
     pending language.
  3. The build's scene becomes the live one, and the level, its HUD, the advisor, the guest sounds and
     the park's audio come in.
  4. A resync keeps the player's camera where it was.

  A swap that fails ends the session. A first join then leaves the player in the front end with the
  reason; a resync whose swap fails before the teardown leaves the player in the guest's own park, as
  any other end of the session does (the screens that tell the player are C2d's).
- **The join restore mode** (`Level.BeginJoinOverFrames`). A join loads the host's live park, not a save,
  and a load's repairs would make the guest's park differ from the host's. So it keeps the park exactly
  as its snapshot holds it:
  - no footprint is written again, no entrance or exit link is repaired, and no legacy queue is restored
    (a placement made under a negative balance leaves its entrance and exit without links, which the
    repair would lay; the footprint writer changes nothing on a live park anyway);
  - no land is laid on the cell map (an imported park whose land walk failed has none);
  - a built coaster that is closed (by its tool, or by the park's door) is not reopened, as a resume
    reopens it (`coaster-resume-reopen`);
  - a tour ride between its script's TOUR 2 and TOUR 1 gets no fresh controller;
  - the park's name is the host's, none included: the sign's default lines are not its name;
  - each coaster's marker binding is taken over from the cells without writing (`CoasterCellMarkers`'s
    adoption). The cells the host's binding did not hold stay unheld and the displaced ones displaced
    (`CoopJoinState.CoasterUnheldCells`, the join state's version 2), so the next change of the track
    claims the same cells on every machine. A bind would claim at once, also a cell another coaster's
    marker held when the pylon was placed and has released since;
  - the join's session state is applied last, before the park's first frame (`Level.ApplyJoinState`), and
    the park is a guest's from the build's first step.

  Kept, with the reason: the queue routing with pool bridges (the guests' restore writes every bridge's
  queue back), the gate's start from the park state (the script world's restore overwrites its
  variables) and the track circuits' re-walk (every live edit ends with a walk). Settled by reading: the
  thing pool's free list, the owner words, the bump module clock and finite floats all round-trip
  exactly through the save.
- **The lifecycle.** A join without a park (`GameFlow.HeldJoin`) and a resync's build hold the game's
  one construction scene. A park start, a load, a visit or a Theme Park Inc layout ends them first ("The
  player left the shared park."), and so does any other teardown; the front end's rebuild for a new
  language keeps the join. A join that ends without a park shows its reason on the front end, unless the
  player ended it.

`CoopJoinSessionTests.GameFlow` runs the game's join host without the game's data: the build is the
replay twin, staged in a scene of its own. It covers the build beside the front end and the swap, a resync
that keeps the view, the refusals, the step waiting during a transition or a park build, a park start that
ends a join or a resync's build first, the front end's rebuild that keeps a join, the other teardowns that
end it, a join that ends without a park, and a failed swap. The coaster binding's adoption is tested at the
binding level (`CoasterCellMarkerTests`): a replay twin replays the host's binding history, so a test
through it would pass without the adoption.

The native smoke, `scripts/smoke-coop-join.sh` (two processes with the game's data, silent and
offscreen: `--coop-join-smoke host|join --coop-join-evidence <directory>`), runs the game's join for real:

- **The host phase** starts Jungle as Instant Action with the default extensions, compatibility and
  graphics settings whatever the developer's configuration holds, as the `--smoke-test` smokes do. It
  builds what a join must carry: two coasters by the coaster tool's commands (the start has not
  researched them, so the stations are placed without a purchase before the park is shared), staff, a
  pool with a lazy river, a bridge over it, its changing room, water attractions, a slide and swimmers,
  a water ride whose track add-on was placed, sold and placed again, and an object put back by the Move
  tool while the balance is below zero (its entrance without its link). No Instant Action player can buy
  the add-on: its upgrade list lists nothing (UITEXT 27; `ParkEconomy.Features.Upgrades` is false), and
  the list's add-on row is the one way to arm the build tool with an add-on. The smoke arms the build
  tool as the row would, which is sound for the join: the flag gates only the list, the level purchase
  and the advice, and the placement, the walk, the save and the restore do not read the start kind. The
  phase hosts a session over a recording transport. Before the first cut it cuts the water ride's track
  open off the add-on's tiles with the track edit tool and takes a line back with Backspace, and puts an
  employee in hand, a pending hire, a carried Move entry and the coaster tool open on a coaster. Before
  the second it has undone those and mended the track, the add-on's sections linked again, and it leaves
  the park paused with edits since its last tick, a built coaster closed and a tour ride bought while
  paused (its script has not run, so it has no controller). A virtual member joins, and later asks again
  with a desync, so the host takes two captures through its stream. The phase checks that each cut holds
  what was staged, that the second holds nothing the first staged and undid (the Move tool's end among
  them, `EndMoveCommand`), and that the capture changed nothing. It runs park minutes at the normal and
  the fastest speed, sells the add-on after the second cut while the member follows, and writes the
  frames, the captures and its final state.
- **The join phase** is a fresh process. It starts in the front end, runs the real game loop and joins
  through `GameFlow.JoinSharedPark` over a transport that replays the recorded host. The first capture's
  answer names a wrong digest: it builds beside the front end, fails its check at the cut and is
  abandoned while the front end stays. The guest asks again and gets the first capture; part way through
  its level's construction (once the build has staged entities in its scene, with steps of its level
  left: the same capture takes the same steps) the host assigns a newer one of the same cut, the session
  abandons that build at once, and the newer one builds and swaps in. Every frame before the swap checks
  that the front end is the live scene, whole, with no entity of a build in it. The guest follows the
  host's frames at twice the recorded pace, its stream checking the host's digest at every checkpoint
  (80 up to the second cut). At the second cut its park's digest and world hash equal the host's; it is
  then assigned the second capture while following, a resync over its park, and follows to the end (20
  more checkpoints), the add-on's sale among the frames. It ends with the host's digest, world hash,
  join state and save, apart from the save's presentation parts (the time, the particles, the snow cover
  and the sound seed, also the economy part's copy of it).
  A measuring mode (`--coop-join-smoke measure`; `OPENTPW_COOP_JOIN_PHASE=measure` for the script) joins
  the untouched captures once each, for the numbers below ("Measurements"); both modes require every
  build's timings with its outcome, finite and not negative, and the host phase every capture's.
  `OPENTPW_COOP_JOIN_PHASE=host` runs the host phase only, for a join phase elsewhere (C2c-2b-2's
  browser). The smoke's runner owns its loop and its end: the native one runs `Render.Run` with a
  watchdog, closes the window on completion and exits on a failure, and the join phase reads the captures
  only by flat file names in its evidence directory.

A mutation run took each skip of the restore mode that the smoke stages out, in turn, and the smoke caught
every one: a join that reopens a closed built coaster, gives a tour ride a fresh controller, repairs the
links (with the footprint re-stamp and the legacy queue restore, which share that skip), or applies no
session state builds a park that differs from the host's at the cut, and the guest asks for the capture
again. The link repair is what the smoke catches there; the footprint re-stamp and the legacy queue
restore are kept by reading (the footprint writer writes only differences, and no Theme Park World shape
has the cells it would add; the legacy restore acts only on an imported queue ride whose entrance links
into empty ground, as in a park saved before the import took the shipped save's queue cells).
The land and the park name's fallback are kept by reading as well (the Jungle has both), and the coaster
binding's adoption is proven at the binding level.

The smoke found four bugs in the save path, each fixed on its own: a coaster's station-head marker read
as a pool wave flag refused every save of a park with a coaster; the arrival vehicles and the finale's End
had no economy record; a park started from an original save could not be saved, its imported animations
starting before their clocks; and a ride's entrance and exit, resolved once to the nearest walkable cell,
were resolved again on a load, so a path laid nearer since moved them.

Known limits: the guest's files and simulation mods must be the host's (the build refuses otherwise); a
park state with a non-finite float (NaN or an infinity) cannot be captured, since the save's JSON holds
none; a single build step can take longer than the frame budget, which C2c-2b measures.

### The browser's join (C2c-2b-2)

`[EXT:online-coop-snapshot-join]` The same join phase runs in the browser's game (Mono WASM, compiled
ahead of time or interpreted) over a join recorded on the desktop, so a desktop host and a browser guest
are proven to run one simulation: `tools/web-coop-join.mjs` serves a published page and the game's
folder on 127.0.0.1, opens the page in a fresh headless, muted Chrome profile with `?coopSmoke=1`
(docs/WEB.md), copies the host phase's evidence (`OPENTPW_COOP_JOIN_PHASE=host` for
`scripts/smoke-coop-join.sh`) into `/coop-smoke` with `Program.CoopSmokeAddFile`, starts the join phase
from the front end (`CoopJoinSmokeStart`: `join`, the adversarial one, or `measure`), follows its
status and reads its evidence back. The page is the smoke's runner (`CoopJoinSmokeTest.Page.cs`): it
attaches the smoke's handlers around the game's update in the native order, times each whole frame call
(the update and the drawing) for the smoke's frame, makes any throw out of a frame the run's failure
before the page stops its loop (also a throw after the run completed in that frame: a pass is published
only once its frame returned whole and the evidence is written again, with that frame's time), and
takes the handlers off once the run has ended, the game running on with the player's saves and the
profile back. The mode checks the page's own address, not the word of the script that asks for it. One
run a page, since the builds it checks must be the runtime's first; the driver starts a fresh profile
for each. It fails fast on a page that crashed or lost its DevTools connection, could not copy a level's
files, stopped its loop or drew no frame for two minutes, and on pool sprites still on their way after
two minutes. What it proves is the
native join phase's contract, unchanged: the cut's tick, digest and world hash, every checkpoint's
digest, the final digest, world hash and join state, and the structural save comparison. Two limits of
the session can end a slow page's run without a wrong digest: a build's 300 s deadline (the wait for the
level's files included) and the smoke's catch-up limit of 3,600 frames; the failure says which.
The evidence names how the page was compiled (`OpenTpwBrowserCompilation`: `aot`, `interpreter` or
`coreclr`; `jit` natively). The audio differs from the native smoke's: Chrome is muted, but its Web
Audio runs (the native silent run opens no device).

Both modes pass in the page compiled ahead of time and in the interpreted one (Chrome 155 headless on an
Apple M5, over a host phase recorded by the desktop build of the same source): the failed check, the
abandon, the join, all 80 checkpoints to the second cut, the resync, the 20 to the end, and the final
digest, world hash, join state and save, in about a minute and some 3,200 frames each. So a desktop host
and a browser guest run one simulation through a whole session, a stronger check than the determinism
pins' fixed runs. The browser's numbers follow in "Measurements" (C2c-2b-2c).

### The browser's host (C2c-2b-4)

`[EXT:online-coop-snapshot-join]` The host phase runs in the browser's game too, so a browser host and a
desktop guest are proven to run one simulation: `tools/web-coop-join.mjs --mode host` opens the page as
for the join (`?coopSmoke=1`, a fresh headless, muted Chrome profile), has it copy the host's level
(`CoopSmokeLevelReady`, which asks the page for the level's files, since the phase's level start reads
them at once), starts the host phase from the front end (`CoopJoinSmokeStart("host")`) and reads back
the recording the phase wrote into `/coop-smoke` (`ReadFile`): the host evidence, the frames, both
captures, and the final save and join state, with the page's record (`host-run.json`) and its frame
calls beside them. The page runs the native host phase (`CoopJoinSmokeTest.StartPageHost`) with its
handler in the game update's place, as the native runner has it: the host's frame advances the park
itself, through the session's stream. It records each frame of 30 ms or more with its drawing, the host
stage whose step it ran and its named work (`LongFrames`); the native host times no frame.
`scripts/smoke-coop-join-browser-host.sh` records the host phase in the page and has the native join
phase replay it (`OPENTPW_COOP_HOST_EVIDENCE` for `scripts/smoke-coop-join.sh`), and
`scripts/measure-coop-join-browser-host.sh` measures it.

Both the adversarial and the measuring join phases pass over the page's recordings (Chrome 155 headless
on an Apple M5, the page compiled ahead of time, the desktop build of the same source): the failed check,
the abandon, the join, all 80 checkpoints to the second cut, the resync, the 20 to the end, and the final
digest, world hash, join state and save. So a browser host and a desktop guest run one simulation too.
The page's host phase takes about two minutes for its 6,900 frames.

`CoopSession.PrepareHostSteps` is what a host is to run before it hosts, a step a frame: the session
digest's code once ahead over the park's economy (`WorldStateHash.SerializationWarmupSteps`), so the
stream's first checkpoint and a capture's cut run none of it for the first time. The smoke's host phase,
the native one too, runs the steps while its park settles, so neither host's first checkpoint is the
digest's first run any more. The game's own sharing does not run them yet: it comes with the sharing's
flow (C2d).

### The digest's canonical text (C2c-2b-4b)

The session digest hashes the park save's economy part as canonical text (docs/DETERMINISM.md, "Canonical
state hash"): at every checkpoint on the host and on every guest, at a capture's cut and at a
join's check. That text was built from a JSON tree: the save serialized into nodes, every object's keys
sorted into a new string, and the string hashed a character at a time, eight FNV steps each. Now the
save is serialized to UTF-8 bytes and read as a document, and a JSON writer writes the canonical form
straight into bytes (`WorldStateHash.CanonicalEconomy`): every object's members in ordinal order of
their names, the parts the digest leaves out skipped at their place. The writer escapes every character
outside ASCII, so each byte of the text is one character, and the hasher takes it a byte a step
(`StateHasher.AddAscii`: the seven zero bytes of a character's eight fold into one multiplication by the
prime's eighth power); a string's characters take two steps each. The text and the hash are the old
ones, byte for byte. `WorldStateHashCanonicalTests` keeps the tree's form as its reference, every pinned
hash is unchanged, and the join smokes compare every checkpoint between the desktop and the page in both
directions. The serialization of the save is as it was, and it is now most of the digest's time
("Measurements", below).

### The browser's link to a real server (C2c-2b-5)

`[EXT:online-coop-session]` The browser's own transport carries the co-op link through the real server.
The browser's join and host above run over recordings, so until this check the browser runtime's
WebSocket and HttpClient had carried chat but never a shared session. `scripts/smoke-coop-link-browser.sh
<wwwroot>` builds the server, and `tools/web-coop-link.mjs` runs it on 127.0.0.1 with a fresh data
folder, no two-step login and the published page as its own (`WebClientDirectory`: one origin, so no
CORS and no proxy). It registers two players and opens two pages of that server in headless, muted
Chrome, each in a browser context of its own and in the page's link check mode (`?coopLinkSmoke=1`,
which starts no game, reads no game file and claims none of the page's stores). Each page runs a
`CoopLinkProbe` (`Program.CoopLinkSmoke.cs`): the game's own `OnlineSession` in a folder of its own, its
login, and its `CoopLink`, which in a browser sends the login's token in the socket's first frame. In
order:

1. Both log in over HTTP and open the link, and the server welcomes both.
2. The host creates a session on the open list. The guest finds it on the open list (an HTTP GET) with
   its host and players, the host invites the guest (the socket), and the guest joins with the
   invitation's code (C2d-3b). The host hears the guest join.
3. The host stores a snapshot through the link's own upload (an HTTP PUT of 150 KiB of seeded bytes,
   which the server takes only once a member has joined), and the guest downloads it through the link's
   own download (an HTTP GET, checked against its SHA-256).
4. Both logins' chats move to the session's room (C2d-1), and a chat line goes each way through it.
5. The host's frame reaches the guest, and a message goes each way.
6. The host leaves, and the guest hears its session end.

The driver chooses every payload and checks each one the pages record against its own length and
SHA-256; a refusal, a lost or stopped link, a failed transfer or a step past its deadline fails the run.
No token or password is written anywhere. `CoopLinkProbeTests` runs the same probes in the same order
against the loopback server on the desktop. The check needs no game data, so the browser determinism
workflow runs it against both of its pages (compiled ahead of time and interpreted). Locally (Chrome 155
headless on an Apple M5) both pages passed all ten checks over the server's protocol 7; the open list and
the invitation (C2d-3b) add two, and the chat line each way (C2d-5a) one.

A whole live join over the server, with the game's data and its own screens, is the live acceptance's
(C2d-5, below); the page's part in it, with the link's timeouts and retries there, comes with its page
sides (C2d-5b).

### Sharing the park from the game (C2d-2a)

`[EXT:online-coop-session]` The park's pause menu has a Build Together line, after Go Online, for the
player's own park: not a visit, and not a guest's copy of a shared park. A lost park keeps the line only
while it is shared. The line opens the Build Together screen over the pause menu. Every line on the
screen is in the player's language.

- **Before sharing**, the screen says what sharing means (the others watch the host build, walk around
  and chat; only the host builds), why the park cannot be shared when it cannot, or that the player has
  to log in, with a login button.
- **The choices:** the title (by default the player's park, by the online name), whether the park is on
  the open list (by default not: code and invitation only), and how many players it takes (2 to 4, by
  default 4).
- **While it is being shared**, the screen shows that, with a cancel.
- **Once hosted**, it shows the code, whether the park is listed, and the players: the host's line is
  marked, an away member's too, and every guest has a removal, asked first, after which the account
  cannot join again. An invitation goes by name. The server answers an invitation only with a refusal,
  so the host invites one player at a time: the next invitation waits three seconds for the refusal
  (`CoopShare.InviteAnswerSeconds`), and one without a refusal by then went out. Every refusal names
  its player (`CoopProtocol.InviteRefusalText`, which the server builds it with), so a refusal is the
  invitation its text names, whenever it comes; a player invited in the last thirty seconds is not
  invited again (`RepeatInviteSeconds`), so two invitations never share a refusal. The line under the
  invitation keeps the latest invitation's outcome; a refusal of an older one (a slow link) has its own
  line below it. Both name their player: a player who is not online for shared parks is to be given
  the code, and too many invitations says who was not invited. Of a removal's refusals only too many
  requests is said, on the screen's status line; the player list shows the rest. End sharing ends the
  park for everyone, asked first; the park stays the host's.

The pause menu does not stop a shared park, so the park runs on under these screens.

`CoopShare` (`GameFlow.CoopShare.cs` owns it) is one attempt to share, and it owns its create from the
request to the answer:

1. It prepares the park for hosting (`CoopSession.PrepareHostSteps`, a step a frame) while the link opens.
2. Once both are done and the park can still be shared, it sends the create.
3. The game steps it once a frame right after the online pump and before the co-op session's and the
   park's update. In the step after the server's answer, never in the link's callback, it checks that
   the same park is still the game's and can still be shared, then hosts it (`CoopSession.Host`).

A cancel before the create sends nothing; one after it waits for the answer and leaves the session the
server made. A park that is closed or replaced meanwhile (a load, an exit, a transition) is treated the
same way. A park that can no longer be shared when the answer comes, or a hosting that fails, leaves
the session too. A link that stops (another game of the player's holds it, with a take-over offered;
an older server; a lost connection) or a create the server refuses (too many sessions, too many
requests, a refused title, an answer lost with the socket) ends the attempt with the reason in words.

Every way the host leaves sends a leave, so guests hear at once that the host left:

- Ending the session from the screen sends one.
- Every way out of the park sends one: an exit to the lobby, a load, a restart, a visit, a quit.
- A logout, another server or a deleted account leaves the held session too. `OnlineSession.Disconnect`
  closes the link with a leave reason (`CoopLink.Close(leaveReason)`). The leave is queued on the co-op
  socket, which outlives the login's HTTP client and sends what it holds before it closes. The game
  hears its own leave as the session's end, so it sends no second one.
- The same close leaves a session that a create or join still waiting for its answer makes: the leave
  follows the request on its socket, which the server reads in order. A quit while the create waits
  (the attempt is forgotten, then the login's close) leaves nothing on the server.
- A leave that cannot go out now (the link is reconnecting) goes out at the next welcome that names the
  session. It belongs to the account and the server of the socket that held the session: another
  server's or another account's welcome keeps it, so a return still leaves with its reason. Until then
  the server keeps the session for its away limit.
- A game that quits while its socket is lost cannot reach the server at all, also when the server made
  a session for a create whose answer the lost socket took with it. The server then ends that session
  after its away limit, or at once when the account's next welcome names it.

While the park is shared or being shared, the HUD shows a status line in a translucent box at the top
centre: the code and the connected players out of the most, that the link reconnects, or that the park
is being shared. The line stays put; the screens over the HUD dim it with the park. When the session
ends without the player ending it (the server, a logout, a failure), the line says so for ten seconds,
counted from the end itself (`CoopShare.EndedAt`, by the attempt's monotonic clock), which often comes
in the online pump before the attempt's step. Dismissing the outcome on the screen keeps the notice;
the game lets that attempt, and the park it holds, go once the notice's time is over or the park closes.
While the game is in a shared park, the online screens' chat entries (a chat park's room, a published
park's chat) leave the chat in the session's room.

`CoopShareTests` runs the attempt over a real link to the loopback server, with the stream tests'
asset-free park and a second player's link as the guest:

- the share: hosting, the guest's frames, and the end the guest hears;
- cancels before and after the create, a park closed before the answer, and a refusal at the answer
  (a cheat waiting for its turn);
- a logout while hosting (the guest hears the leave's reason at once) and while the create waits, and a
  quit while it waits (the server keeps no session and no member);
- an end the online pump delivers (another game takes the link over), which the status line shows for
  its ten seconds, also once dismissed, until its time is over or its park closes;
- an older server, and a hosting that fails;
- an invitation to a player who is not online, and a kick;
- one invitation at a time (late in a long run too), a refusal that comes after its wait while a newer
  invitation waits (a late result of its own player; the newer one keeps its outcome), refusals for
  too many invitations that name their players on both lines, no second invitation of a player within
  thirty seconds, and a refused removal (no invitation's answer);
- the screen before sharing (the login, the choices, a refusal in words) and while hosted (the code,
  the players and their removals, the invitation's answer for its own player, End sharing asked first).

`CoopLinkTests` checks that a logout's leave that could not go out outlasts another server's and another
account's welcome, and that a close while a join waits leaves the seat the join takes.

The live acceptance (C2d-5, below) shares a park through these screens natively; browser hosting
through them comes with its page sides (C2d-5b).

### Sharing a saved park from the lobby (C2d-2b)

`[EXT:online-coop-session]` The lobby's Escape menu has a Build Together line after Go Online, also
only while the online extension is on. It opens the lobby's Build Together screen, which offers the
player's own saved parks: the sandbox and the shipped parks are not offered, as they are no park of
the player's own. The rows read as on the Load Park screen: the park's name, marked when it is
cheated or needs extensions, and when it was saved. Below them are the same choices as on the park's
screen (title, open list, players), a login when the player is logged out, and Share. Without a
saved park, the screen says so.

Share loads the chosen park as the Load Park screen does, and the share goes with that load:

1. The save's extensions are asked about first (the extension switch); a refusal there starts
   nothing.
2. The load is queued like any park load. Another transition replaces it, and the share goes with it.
   The lobby screen's Back gives the share up, with its load, while the load still waits (the browser
   copies a level's files first).
3. The save is resumed at once, without frames. Only a park this load made is shared
   (`GameFlow.ShareLoadedPark`). A failed load shares nothing and leaves the lobby as it was.
4. The park opens with its pause menu, so a park that is not shared yet stays paused under the
   screens, as when the pause menu opens them.
5. The share starts as from the pause menu (`StartSharing`), and only on the server and account that
   asked for it. Logging in on another server meanwhile leaves the choice to the player. The start
   checks the login and the loaded park again. When the load's extension choices turned online play
   off, a message says why nothing was shared.
6. The park's Build Together screen opens over the pause menu. It shows the attempt, or why it could
   not start, with the player's choices kept for another try.

`CoopShareTests` covers:
- the lobby screen: the saved parks, the login, the choices that go with the chosen park, Back giving
  the share up, and no saved park;
- the share's checks and its queued load: giving it up removes the load, and a failed load shares
  nothing;
- the share of the park a load made, only on the login that asked, through the game's own step up to
  the hosting;
- the screens over the loaded park: the pause menu and Build Together, or the message when online play
  is off.
`OnlineExtensionGateTests` checks that the line goes with the online extension.

### Joining a shared park from the lobby (C2d-3a)

`[EXT:online-coop-session]` The lobby's Build Together line opens a hub with two choices: share one of
the player's saved parks (C2d-2b) or join with a code. The join screen takes the code the host gave;
case, spaces and dashes do not matter, and a code that cannot be one is said and not sent. It offers a
login when the player is logged out. While the game joins, the screen shows the stage in words, with a
cancel: joining, getting the host's park, building it. A refusal is said in words, with a way back to
the code. Every line is in the player's language.

`CoopJoinAttempt` is one attempt to join, and the game owns it (`GameFlow.CoopJoinAttempt.cs`):

1. Once the link is open, it sends the join, unless this game could not follow a shared park
   (`CoopSession.JoinRefusalKind`: another edition, or a run with a developer option that changes the
   park). Then nothing goes out and the screen says why, each in its own words: another edition is told
   that only Theme Park World parks are shared, a developer option is told to restart without it.
2. The server's answer is taken in the game's step right after the online pump (and after the share's
   step), never in the link's callback. The game joins (`GameFlow.JoinSharedPark`) only while the link
   still holds that seat and the game is free: no park, no park build, no transition, no saved park's
   share and no share attempt. Otherwise the seat is left, and so is a seat whose join throws. While an
   attempt still settles its answer (a cancel waits for it), no share starts.
3. From then on the joined session gets the host's park and builds it, and its own resume and capture
   retries apply. The attempt only watches until the park is the game's, or until the session ends.

Every way out leaves what the server gave:

- A cancel before the answer waits for it and leaves the seat it gives.
- A cancel once joined ends the session, which leaves it and abandons a partial build. So does Back.
- A seat the server ended in the same pump as its answer is reported as the server's end.
- A lost socket refuses the waiting join; the next welcome leaves a seat the server made.

The session now records what ended it (`CoopSession.EndCause`, `ServerReason`): this game's leave, the
server's end with its reason, a lost link, the guest's failed joins (`FailJoin`), or the host's stream.
The screens say an end in words by that, never by the server's English (`GameFlow.JoinEndText`,
`JoinRefusalText`). One presenter says each end:

- Before the game took the join, the join screen says it.
- Once taken, the game's own dialog says it over the lobby (`OnJoinEnded`). Nothing is said when this
  game left.
- A joined guest whose session ends (the host left, a removal, a lost connection) hears why over its
  park, and the dialog's OK leaves for the lobby. The park is the host's and is never saved.

A guest in a running shared park is asked before the pause menu's Exit To Lobby leaves it ("the park
stays the host's"); once the session ended, the exit goes at once. The guest's HUD shows its own status
line: reconnecting, then waiting for the host (away), then catching up (a resync), else the park's
title, its host and the connected players.

The tests:

- `CoopJoinSessionTests` (part `CoopJoinSessionTests.Attempt.cs`) runs a host on the loopback server
  with the stream tests' asset-free park, and the guest's game with the replay twin. It covers:
  - the join through the game's attempt until the host's park is the guest's;
  - the guest's status line;
  - the confirmed exit;
  - the host's end said over the guest's park;
  - a cancel while the park is built, which leaves the seat;
  - refusals for an unknown code and a full park, and a code that cannot be one;
  - a cancel before the answer, and an answer the game cannot take;
  - a seat the server ended before the game took it;
  - the game's checks (only in the lobby, only while free to join), a run that cannot follow (another
    edition or a developer option, each with its words; nothing goes out), and a share kept waiting
    while a cancelled join settles;
  - Yes to the guest's exit and the end dialog's OK: the seat is given back, and the queued exit retires
    the guest's park for the lobby without saving it (run through the game's retire seam);
  - the words for every end and refusal.
- `CoopShareTests` (part `CoopShareTests.Join.cs`) covers the hub and the join screen: the login, the
  code, the stage, the end, and Back giving the join up.

### Invitations and the open list (C2d-3b)

`[EXT:online-coop-session]` A host's invitation reaches a player anywhere in the lobby, and the lobby's
hub has a third line, Find a shared park, which opens the open list. Every line is in the player's
language.

**The lobby's link** (`CoopLobbyLink`, stepped by the game after the join's step). The link is open while
a logged-in player who could follow a shared park is in the lobby: no park, no park on its way, no
Theme Park Inc front end. Elsewhere an idle link is closed, so a player in an ordinary park is not
online for shared parks. The host then hears that the player is not online and is told to give the code.

- A link in use is never touched: one that holds a session (also while it reconnects), one whose create
  or join waits for its answer (`CoopLink.Waiting`: the server would seat the player once it reads it,
  also after a close), and one a join attempt, a share or a saved park's share is using.
- A create or join whose answer was lost with its socket may still have made a seat. The link's own
  reconnect leaves any seat it does not hold, as it always did. To give it that welcome, the lobby keeps a
  link with something to settle (`CoopLink.Settling`) open for up to a minute after it stopped wanting it.
- A leave is named only by its session's id, never guessed from a code or title. A leave sent at a welcome
  (one that could not go out earlier) is kept until the server confirms it: the session's end for this
  game, a refusal that it is in none, or a welcome that does not name it. If it is lost with its socket,
  the next welcome sends it again. A leave sent on a live socket is done once sent, so a logout keeps
  nothing.
- Accepted limit: if the link cannot reconnect within that minute, a lost answer's seat stays on the
  server. A later lobby open cannot tell it from another game's park, so it stops (`HeldElsewhere`). The
  seat goes with the player's next join or share (whose open leaves it), the host's removal, or the
  park's end.
- Each time the link becomes wanted (the lobby entered, a login there, another login), it opens once
  (`CoopLink.OpenInLobby`). It never takes the link from another game of the account; that game keeps it
  (`InUseElsewhere`).
- It never leaves a shared park of the account that it did not ask for. Such a park may be another
  game's, whose socket is away for a moment, so the link stops (`CoopStop.HeldElsewhere`) instead of
  leaving it. Once the link sends a create or join, it is any open's again, and it leaves a seat the
  server made for it.
- A stop is kept until the next time the link becomes wanted, or until the player acts: a join or a share
  opens the link itself. After `HeldElsewhere` that open is the player's own act, so it leaves the park the
  lobby left alone, as any open that did not ask for it does.
- Only a link lost for its whole reconnect window opens again by itself. It waits a minute (spread from
  70 to 130 per cent), at most ten times in one stay in the lobby. A link that drops soon after it opened
  goes on with the stay's count; only one open for a minute counts from nought again.
- The link's own reconnect waits are spread too (80 to 120 per cent). With a link open in every lobby, a
  server that restarts must not bring every game back at the same moment.

**Invitations** (`GameFlow.CoopLobby.cs`). An invitation is the login's that received it.

- It shows as a dialog over whatever lobby screen is open ("{host} invites you to a shared park:
  {title}"), with Join and Not now (also the dialog's Back).
- One shows at a time, and only while the game is free to join: no join or share running, no park on its
  way, not during the lobby's enter sequence. The others wait, at most four, and one that waited for a
  minute is dropped unseen.
- A repeat of an invitation already waiting or shown takes its place.
- A rebuild of the lobby (a language change) shows the invitation on screen again.
- Leaving the lobby, a logout or another login forgets every invitation, the one on screen included.
- The protocol has no answer to an invitation. Not now only puts it aside. Join starts the game's join
  attempt with the invitation's code, and the join screen shows it as a join with a typed code.

**The open list** (`sharedParks`).

- It shows the parks of this build that their hosts list for everyone, with their hosts and players, and
  has a Refresh button.
- The rows belong to the login that listed them: another login clears them and lists again. Only the
  latest request's answer is shown.
- A full park cannot be joined.
- Join starts the same attempt as a code join and opens the join screen. A park that ended or filled
  after the list came is refused there in words. The join screen keeps the park's code for another
  try, as it does an invitation's.

**A game that cannot follow a shared park** (another edition, or developer options that change the park)
opens no socket. The join screen and the list say why before anything is sent.

**The join screen's take-over.** When another game of the account holds the link, the join screen says
what taking it over means: the other game leaves its shared park (a host's leave ends the park). Use this
game then takes the link over and joins with the same code. Nothing is taken over before that click.

The tests:

- `CoopLinkTests` (part `CoopLinkTests.Lobby.cs`), over real links to the loopback server, covers the
  lobby's link:
  - it opens for a logged-in player and closes idle elsewhere;
  - it leaves a link in use alone: a waiting create or join, a session, an attempt;
  - it never takes another game's link, and tries once a stay;
  - it never leaves another game's park while that game's socket is away, and that game's park goes on;
  - a link whose lost answer made a seat is kept for a minute so its welcome leaves it, and no longer;
  - after a lost link, that seat is left by the player's next act, never by the lobby;
  - a leave lost before it went out goes again at the next welcome, until the server confirms it;
  - a lobby link that asks for a seat is any open's;
  - a lost link opens again on its spread timer, at most ten times, and a brief open keeps the count;
  - the reconnect waits are spread.
- `CoopJoinSessionTests` (part `CoopJoinSessionTests.Lobby.cs`), with a real host and the guest's game,
  covers:
  - an invitation over another lobby screen, whose Join joins the host's park;
  - Not now, Back and a repeat;
  - invitations that wait while a join runs, expire, and go with a logout;
  - a rebuild, a park on its way and an ordinary park (the host hears not online);
  - the take-over only on the click;
  - a join that leaves an earlier run's park, which the lobby's link stopped at;
  - a stale or filled row of the open list, refused in words.
- `CoopShareTests` (part `CoopShareTests.OpenList.cs`) covers the open list's screen: the hub's line,
  the latest answer of the login's requests, a full park, Join with the chosen code, another login's
  rows, and a game that cannot follow.
- The browser's link check (`tools/web-coop-link.mjs`, and `CoopLinkProbeTests` on the desktop) also
  covers the open list and an invitation over the page's own HTTP and WebSocket. The live acceptance
  (C2d-5, below) joins with a code through these screens natively; the browser's screens and a whole
  join in the page come with its page sides (C2d-5b).

### Co-builders as figures (C2d-4)

`[EXT:online-coop-presence]` In a shared park every member walks as a figure with a name plate, as the
players of a visited park do (decision 8; [ONLINE.md](ONLINE.md), "Players in a visited park"): the
guests' kid sprites, the name on a dark plate over the head, the player's own in yellow, under the HUD's
windows.

**Where a figure goes.** No click walks a figure in a shared park: the host's clicks build, and a
guest's clicks are the HUD's. Each member's own figure follows that member's view instead:

- The view's centre is the overview's target, or the free walk's place, in cells (`GameFlow.ViewCentre`,
  `OriginalParkPlacement.ToCells`). Other views (the ride view, the globe) have none, and the figure
  stays.
- The figure walks there at the visit's pace (1.2 cells a second). It plans again when the centre moved
  a cell or more from the centre it last followed, at most every quarter of a second, so a panning view
  does not search a route every frame.
- A centre twelve cells or more from the figure (a locate, a map click, a fast pan) moves it there at
  once, and its place goes out as a teleport.
- A centre on a cell no figure stands on (water, a ride) is walked or moved to the nearest free cell
  within three cells, once; with none that near (the middle of a lake), the figure stops where it is.
  A centre the figure is not on its way to itself is tried again when the park's ground changes: a
  co-builder may have opened it, and a figure built over walks out to the nearest free cell.
- A figure enters where the view looks, on a free cell near it, or at the park's entrance when the view
  looks at the middle of a lake.
- While the player rides, the figure is hidden for the others, as in a visit, and comes out at the
  ride's exit; then it follows the view again.
- The host builds while the figures walk. A walk planned on an older cell map (`ParkCellMap.Version`)
  is planned again on the new one, at most every quarter of a second, and the route search labels the
  park's areas again, so a figure goes round what was built and through what was opened. Until then
  it stops in front of a cell of its route that was built on.

**The room.** The figures meet in the session's chat room (`coop:<id>`, C2d-1). The online session owns
that chat: the shared park's presence never joins or leaves a room and never opens or closes the chat.
A figure's places go out only once the room is the connection's own, confirmed
(`OnlineSession.SharedRoomReady`): a chat connection whose own server version is read (protocol 8), no
join on its way or waiting, and the server's roster names the room. Its first place there is a teleport,
which the server answers with where the others stand, and a reconnection announces the figure again
once the new connection's join is confirmed. The others are drawn from the room's relayed places and
roster, so a member who leaves goes from the park. A server without shared parks' rooms shows no
figures.

**The game's side** (`GameFlow.CoopPresence.cs`). After the session's step, the game makes the shared
park's presence (`ParkPresence.ForSharedPark`) while it is logged in and in a running session's park
(host or guest, not a visit) whose room its chat follows. The presence belongs to that park and that
session: it goes when the park changes (a resync's swap makes a new one for the new park), when the
session ends, or at a logout. A session can end during the level's update, so the presence also checks
every frame that its session still runs, and sends nothing once it does not. A visit's presence is the
visit's own; the step never touches it.

**No sparkle.** A visit's figures sparkle through the level's particle pool. A shared park's do not: the
pool is captured with the park and object runtimes keep handles into it, so a local effect would make
the members' parks differ.

**The HUD.**

- A shared park starts in its HUD, where the host builds. **F4** switches to the chat mode (the chat
  overlay in place of the HUD, as in a visit) and back, and **Enter** over the park opens it with the
  entry taking the keys.
- Entering the chat mode ends every armed tool, as their own Escape ends them: the build and Move
  tools, the remove tool, the coaster, staff, patrol, land, clearing and pool tools, and the path and
  queue tool. While it is on, its park clicks never reach the level's tools (`Level.WorldPointerRouted`),
  and no click, Backspace or line starts or edits a path. A click on a rideable ride rides it, as in a
  visit's chat mode; a click never walks.
- A name popup's **Go to** moves the view to that co-builder's figure when it is drawn here (a free walk
  ends first), and sends no command, so the chat does not say to leave the park. A name with no figure
  here (a buddy elsewhere, a player who rides) goes to the server as before, which says to leave the park
  first. A visit's goto is unchanged.

The tests:

- `ParkPresenceTests` (part `ParkPresenceTests.SharedPark.cs`) covers the presence: no room joins or
  leaves, no chat opened or closed, no click walk, no goto, no sparkle; places only in the confirmed
  room and again on a new connection; none on a server without shared parks' rooms; nothing after the
  session ended; following, throttled planning, the jump and the ride; a view over water planned once;
  and a walk that goes round a co-builder's edit and through what they open.
- `SharedParkChatModeTests` covers the HUD: the HUD first, F4 and Enter, the tools retired and the
  level's clicks kept from it, and Go to moving the view; `ChatPanelTests` covers the popup's own goto,
  which sends no command.
- `CoopFiguresViewTests` covers the view's centre: the overview's target, the free walk's place, and no
  centre in another view or park.
- Over real links to the loopback server: `CoopLinkTests` (part `CoopLinkTests.Figures.cs`) covers the
  figures meeting in the session's room, each first place answered with the other's, a walk and a jump
  relayed, and a member's leave; `CoopShareTests` (part `CoopShareTests.Figures.cs`) covers the host's
  game making the presence once hosted, keeping it, leaving a visit's alone, and dropping it with the
  session; `CoopJoinSessionTests` (part `CoopJoinSessionTests.Figures.cs`) covers a guest's game showing
  the host's figure, a new presence for the park a resync swaps in, and its end with the session.
- The live acceptance (C2d-5, below) checks the figures in two real games on the desktop, host and
  guest; the browser's come with its page sides (C2d-5b).

### The live acceptance (C2d-5)

`[EXT:online-coop-session]` Two games share a park through the real server, with the game's data. The
sharing, the join, the building, the chat and the session's end go through the game's own screens and
input. The headless tests check each part against the loopback server, the smokes join over recordings,
and the browser's link check runs without data; here it all runs together, live. C2d-5a runs two native
games; C2d-5b adds the page as host and as guest.

`scripts/coop-live.sh <out> ['/path/to/Theme Park World'] [--scenario ...]` builds the game (Release)
and the server once. Then `tools/coop-live.mjs` runs:

- **The server** on 127.0.0.1 with a fresh data folder and no two-step login. Two throwaway players with
  random names and passwords are registered over HTTP.
- **Two native games** (`--coop-live host|join`), silent and offscreen. Each has its own configuration
  (copies of the developer's settings files and links to the art folders, never the online folder), its
  own online folder, and its own save folder (`OPENTPW_SAVE_DIR`). The guest starts once the host has
  published its code.
- **The secrets.** A password reaches a game only in its environment, never in its arguments, a file or
  a log; the driver's logs and failures have it replaced. The server keeps hashes, and the games keep
  the server's address and the player's name, as a player's game does.

Each game runs a script of steps (`CoopLiveTest`), each waiting for a condition with a deadline of wall
time. A failed step ends the run with its evidence, and the other game and the driver stop at once.

- **Screens.** A game acts on the top screen only. It finds an element by its id, of its type (a button,
  a menu line, a field, a label), visible and enabled. It activates a button or a menu line and types into
  a field as the screen's own input does. Three things the harness does itself: it starts the host's
  park, moves the views (the camera itself, not a drag or a key), and reads the games' state for its
  checks.
- **Input.** Escape, Enter, the chat line, F4 and the park's clicks go through the game's input.
- **Barriers.** The games and the driver meet at barriers: each publishes its status in the shared
  evidence folder and waits on the others'. The chat is under test, not the control channel.

The steps:

1. **The host shares.** The harness starts Jungle as Instant Action does, not through the front end's
   menus. Escape opens the pause menu, and Build Together opens the screen. The host logs in there
   (server, name, password), writes the title, picks two players, leaves the park off the open list and
   shares. The screen shows the session's code.
2. **The guest joins.** It starts in the lobby: Escape, Build Together, Join with a code, a login there,
   the code, Join. The host's park is fetched, built and swapped in, and follows the host's stream with
   its digests compared.
3. **The figures.** The host sees the guest's figure and name drawn in its view, then the guest sees the
   host's; a picture of each view is kept. Then the harness moves the host's view far away: the host's
   figure jumps there, and the guest sees it there. The same follows for the guest's view.
4. **Building.** The host lays a path line through its HUD: a press on free land starts the tool, and a
   press on the end lays the line. The guest's park takes the line's record, its cells become paths, and
   checkpoints are compared after it. The guest's own line is refused (only the host builds), its cells
   stay free, and its park follows on.
5. **The chat.** A line each way through the chat mode's entry (Enter over the park, the line, Enter),
   heard in the other game.
6. **The guest's controls.** F4 to the HUD and back. A click on a ride in the chat mode rides it: the host
   sees the figure gone. Escape leaves the ride: the host sees the figure back. Go to in the host's name
   popup moves the view to the host's figure with no park command.
7. **The end.** The host ends sharing from the Build Together screen (asked first). The guest's end
   dialog leaves for the lobby, and the host's park runs on alone.

The driver then checks both games' evidence against each other (`checkEvidence`, with
`tools/test_coop_live.mjs`):

- one session, the host's, in both games;
- the guest's park built from the host's capture;
- the host's line applied by the guest at its record, with checkpoints compared after it;
- the guest's line refused;
- each figure drawn with its name, also where its view moved;
- the chat both ways;
- the ride and Go to;
- the session's end.

The scenarios:

- **baseline**: the steps above, with no resync, no desync and no lost link.
- **recovery** (`--scenario recovery`): after the first line, the driver kills the server and starts it
  again on the same port and data three seconds later. After that barrier both games' links go from
  reconnecting to open with a resume of the session, and both chats reconnect: the counts at the barrier
  must grow. The guest follows again (a resync is allowed here). The host's view jumps again, which the
  guest sees, and a second line reaches the guest's park, with checkpoints after it.
- **endurance** (`--scenario endurance --minutes M`): after the chat, the host runs the fastest speed (the
  HUD's `>>>`) for M whole minutes of wall time, from setting it to setting the normal speed back. The guest
  takes its ticks before the host sets the speed, and the host its own when it does. Every half minute both
  games sample the park's ticks; the host also checks that the speed is still the fastest, and asks for the
  guest's last sample before it sets the normal speed back. From those first ticks to the last sample, both
  parks must run at least 180 ticks a second over every stretch of five seconds or more, three times the
  normal 60, which only the fastest speed (240) reaches. The guest follows with no resync and no desync, comparing a
  checkpoint at least every two seconds. Then the rest of the steps follow.

**The pages' way in.** The pages (C2d-5b) open through the drivers' own local server in front of the
real one (`tools/web-perf/lib.mjs`). It serves the game folder (`/game/`) itself and forwards the rest
to the server: the page, from the server's own web folder, and `/api/`. It forwards the sockets'
upgrades below `/api/` only: at most 16 at a time, the server's whole answer within 10 seconds (an
answer that refuses the upgrade at most 64 KiB, passed back as it is), and a half-closed socket closed
after 5. The browser workflow also runs the link check through it (`tools/web-coop-link.mjs
--proxy`): it carries two co-op and two chat upgrades with bytes both ways, and each page's chat line
reaches the other page through it (a page's only origin is the proxy's). The page's policy lets them: its
`connect-src` names the server's own address, which the forwarded requests carry, and `'self'` covers the
page's. A refusal (an upgrade outside `/api/`, or none left) is answered, and a client that keeps its side
open after any answer is closed after the half-close wait.

Results (Apple M5, macOS 26, Release, native host and native guest):

| Scenario | Checks | Wall time | Notes |
|---|---|---|---|
| baseline | 12 of 12 | 19 s | the guest followed 3.3 s after Join (a 104 KiB capture) |
| recovery | 14 of 14 | 29 s | both links back 3.3 and 3.4 s after the server went down (for 3 s); no resync |
| endurance, 30 minutes | 13 of 13 | 30 min 19 s | 1800 s at the fastest speed; the slowest five-second stretches at 239.96 (host) and 238.41 (guest) ticks a second; 1806 checkpoints, no resync or desync |

No CI job runs it: it needs the game's data. A run's `run.json` keeps every check, both games' evidence
and the run's facts; its logs, pictures and status files stay in its folder.

### The host's trimmed frame (C2c-3)

`[EXT:online-coop-snapshot-join]` The host trims the frames every member is known to hold. A server
whose stored frame number fell behind that (a restart that stored a number older than what it relayed
since) used to end the session: it needed a frame no one holds any more. Now the host gets everyone the
park again instead (`CoopHostStream.SkipTrimmed`, design `coop-c2c-design.md` section 6.6):

- It assigns every member a capture at once, past its last frame `L`, also a member assigned within the
  last ten seconds and one that is away (it hears the assignment when it is back). The members' old
  streams stop, and each starts from its capture, as a resync does; a forced resync is no failed join.
- It tells the server its frames go on at `L+1` (`CoopSkipFrames`, server protocol 7), which the server
  takes next. No member needs the frames between, since every capture starts after them. The server
  never goes back: a skip to a frame it already relayed is answered with the frame it takes next, as a
  frame out of turn is, and only the host skips.
- A refusal of a frame sent before the server took the skip names a frame the skip passed over, and
  changes nothing. A skip lost with the host's socket goes out again with its resume, without assigning
  the members anew, while the host still holds the frames from it; a server back behind a skip whose
  frames the host trimmed since gets a new one.
- A game opens its co-op link on protocol 7 servers only: an older server's welcome stops the link as a
  server too old for the game, so a session's server can always take the skip. A transport that cannot
  take one ends the session as before.

The server keeps the skip rare. A frame number a deploy stores exactly holds for the next run only: that
start stores it again as unknown at once, so a crash later in the run brings it back unknown, never as an
exact number behind the frames relayed since, and the host resends from its oldest frame (a guest drops
what it has) instead of skipping. Two cases are left. A long stretch in which no member acknowledges a
frame lets the caps trim frames the host handed to the link before the server confirmed them, which a
lost socket then takes with it. And a start whose rewrite of the stored positions fails (a full disk,
reported) followed by a crash before any later write brings the old exact number back.

`CoopServerTests` covers the server's side (a skip from the host, one back, one from a guest or from
outside a session, a stored position exact for the next run only, a hello of an older protocol);
`CoopLinkTests` the link's skip through the real server; `CoopStreamTests` the host's (one skip, the
assignments, a stale refusal, a skip lost with the socket, a server that cannot skip);
`CoopJoinSessionTests` sessions whose members resync from their captures and follow the host's park,
equal at the end: after one skip and a second over the first, with a member away at the skip, and with a
capture on its way up at the skip, which serves no one.

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
- **The join's cost (C2c-2b-1).** `scripts/measure-coop-join.sh` runs the native join smoke's host phase
  and its measuring join phase (`--coop-join-smoke measure`: the untouched captures, joined from the
  front end and resynced once each) five times in a Release build, each phase a fresh process, silent
  and offscreen, and records the machine and the source's commit; `tools/coop_join_timings.py` prints
  each timing's median, range and runs (it refuses a run whose timings are not whole, runs whose recorded
  environments differ, and the adversarial join's evidence, whose rewritten payloads and refused and
  abandoned builds warm the process first). The timings are wall-clock (`Stopwatch`, never a session's
  clock), kept per capture on the host (`CoopHostStream.CaptureTimings`) and per build on the guest
  (`CoopSession.JoinTimings`: the stages of `ICoopJoinBuild.Timings`, every part of a step that alone
  took the build's 12 ms budget, and the build's `LoadProfile` sections over its own slices), logged,
  and written into the smoke's evidence with the phase's update frames and its environment. They change
  nothing they time but for their clock reads: the build's budget leaves their bookkeeping out, and the
  host's cut leaves out a test's hook. The smoke's upload and download are local, so their times are no
  network's. On an Apple M5 (Mac17,2, 32 GB, macOS 26.6, .NET 11 RC 2, the advisor on), medians of five
  with their ranges:
  - **The host, at a cut.** The first capture's cut took 10.5 ms (9.4-11.7) in one frame: the digest
    3.9 ms, the save's snapshot 4.8 ms; the second's 4.8 ms (4.1-5.4): the digest 2.2 ms, the snapshot
    2.3 ms. The captures took 14 and 6 frames. The save's longest slice took 34.7 ms (33.7-37.5) in the
    first, the serializer's first use in the process, and 4.1 ms (3.5-9.2) in the second; the
    compression 1.5 and 1.4 ms, for a 128 and a 149 KiB payload of a 980 and a 1,107 KiB save.
  - **The guest, joining from the front end** (the process's first build): 21 frames (19-23), of which
    eleven steps took the build's budget alone in every run:
    - the level's step that restores the park's objects (`ParkLayoutSnapshot.RestoreObjects`, the
      objects section 433 ms) and sets up its coasters: 436 ms (392-479);
    - the level's last step: 172 ms (147-193), with the paths, the staff, gate and economy (53 ms), the
      resume's restore (62 ms), the join state and the level's HUD (5 ms);
    - the save's parse: 167 ms (153-198);
    - the level's guests: 135 ms (102-146);
    - the level's step after the terrain (the blueprint markers, the park's name, the camera's start):
      64 ms (56-70);
    - the terrain's two steps: 34 and 17 ms;
    - the advisor 30 ms, a validation step 21 ms, the catalogue's settings 18 ms, the economy 17 ms;
    - in one run of the five, a catalogue step of 14 ms.

    The decode took 4.2 ms (4.1-4.5). The check at the cut took 45 ms (40-46, the process's first digest)
    and the swap 13 ms (8-15), in a frame of their own (the advisor's step had spent the slice before).
    That frame's update took 325 ms (279-442), of which about 270 ms went to other work than the check
    and the swap (the joined park's first update among it), which these timings do not take apart. The
    next frame's update took 20 ms (18-35). From the swap to the session step that found the guest caught
    up (after one tick and one frame; 8 ticks in one run), 0.30 s passed (0.26-0.42): the rest of the
    swap's frame (its update and its drawing) and the next frame's start.
  - **The guest, resyncing over its park** (the process's second build, warm): 9 frames (9-10), the
    longest 88 ms (76-103), the guests' step; the last step 51 ms, the step after the terrain 38 ms, the
    objects' step 20 ms, the save's parse 17 ms, the original park's load 15 ms, and in three runs of
    five the catalogue's settings 16 ms. The decode took 1.5 ms; the check 2.3 ms and the swap 8 ms ran in
    a frame of 64 ms (53-85) with the advisor's step; the next frame's update took 49 ms, and 0.06 s
    passed to the session step that found it caught up.

  A join thus stalls a guest's frames for 0.44 s at its worst step (0.40 to 0.48 s in these runs; 0.09
  s warm) and for 0.33 s in the swap's frame (0.06 s warm), and the host's for one frame of 4 to 12 ms at
  the cut (a process's first capture also for a 35 ms slice).

  Every value of the five runs: [COOP-MEASUREMENTS.md](COOP-MEASUREMENTS.md), "The desktop's join".

- **The join in the browser (C2c-2b-2c).** `scripts/measure-coop-join-browser.sh` records the host
  phase once, in the desktop's Release build, silent and offscreen, and runs the page's measuring join
  phase over it several times (`tools/web-coop-join.mjs --mode measure`), each in a fresh headless,
  muted Chrome profile once the pools' sprite sets have arrived. `tools/coop_join_timings.py` counts the
  reused host phase once, adds each frame's whole call (its update and its drawing) and the frame of the
  smoke's final check, and refuses runs whose pages ran in another browser or with other settings. In
  Chrome 155 headless on the same Apple M5 (ANGLE over Metal, 1,280 by 900, no frame cap), over a host
  phase of the same source (`d710ffe88`, clean), medians with their ranges, the native process's first
  build in brackets:
  - **Compiled ahead of time** (five runs). The join's build took 393 frames (277-400), 375 of them a
    wait for the level's files (below). Its longest steps:
    - the save's parse: 374 ms (366-402; native 167);
    - the level's step that restores the park's objects and sets up its coasters: 320 ms (318-354;
      native 436), the objects section 318 ms;
    - the level's last step 141 ms (172), the guests 85 ms (135), the step after the terrain 38 ms
      (64), the advisor 34 ms (30), the terrain's two steps 30 and 22 ms (34 and 17), a validation step
      26 ms (21), the economy 17 ms (17), the catalogue's settings 17 ms (18) and its first step 14 ms.

    The decode took 15 ms (4.2). The check at the cut took 76 ms (45) and the swap 9.4 ms (13), again in
    a frame of their own: 331 ms (313-364; native 325), of which about 245 ms (native 270) went to other
    work than the check and the swap. With its drawing, the longest frame while building took 392 ms
    (387-427), the save's parse, and the swap's 356 ms (338-384), the joined park's first drawing in it.
    After the swap the guest ran 638 ticks (423-691) in 10 frames (0.59 s) before the session found it
    caught up, within the session's catch-up budget (the host's frames went on arriving meanwhile). The
    resync over its park (the page's second build) took 10 frames, the longest 57 ms (the level's last
    step, 54 ms); the save's parse took 52 ms (native 17), the check 8.3 ms, the swap 10 ms and the
    swap's frame, with the advisor's step, 140 ms (native 64).
  - **Interpreted** (three runs), slower in the longest steps: the objects' step 1.41 s (1.34-1.42), the
    save's parse 508 ms, the level's last step 307 ms, the guests 250 ms, two validation steps 237 and
    165 ms, the terrain's second step 195 ms and the step after the terrain 194 ms; the check 101 ms and
    the swap's frame 644 ms (706 ms with its drawing); caught up after 758 ticks in 29 frames (2.0 s).
    The resync's longest build frame took 195 ms, its swap's frame 327 ms.
  - **The wait for the level's files.** A page copies a theme's folder on the theme's first use
    (docs/WEB.md, "Game files on demand"), and the join's build waits for it as a park start does: 375
    frames (258-382) and 6.4 s (4.5-6.6) by the page's own frame record (`page-frames.json`) ahead of
    time, 383 frames and 7.2 s interpreted. The build's loop asks whether the files are there until the
    slice's 12 ms budget is spent (10.8 million times over the wait), so each frame of the wait took 16 ms
    of update instead of the front end's 1 ms; the files stage was charged 1,958 ms (1,353-1,980) of it,
    the loop's own clock reads not. Whether the loop also slows the copy, whose work runs on the page's
    main thread between frames, shows once the wait ends its slice. The desktop's files are there at
    once.
  - **The final check**, the smoke's own (the digest, the world hash, the join state and the save
    compared with the host's), took one frame of 519 ms (507-540; 1.63 s interpreted). A player's join
    runs none of it.

  Ahead of time, a join thus stalls the guest's page for 0.39 s at its worst step and 0.36 s in the
  swap's frame, a resync for 0.14 s (interpreted 1.42, 0.71 and 0.33 s). Several of the level's steps
  took less time than in the native process's first build (the objects' step 320 against 436 ms, the
  guests 85 against 135 ms), which also compiles its code as it first runs it. What C2c-2b-3 splits, by
  these numbers: the save's parse, the swap's frame (the check and the rest of that frame's work; the
  native second-longest frame too), the objects' step, the level's last step and the guests; and the wait
  for the files ends its slice instead of spending it. The join's build also lacks the steps that prepare
  a park start's entering frame (`GameFlow.PrepareEntry`, docs/BROWSER-PERFORMANCE.md, "The entering
  frame"), the first lead for the swap's frame. Main's #741, merged after these runs, splits several of
  the level's construction steps for a park start (the guests by their sprite sets, the gate sign, the
  ride scripts' handlers, the level's last step in parts), which the join's build shares, and lets a step
  known to be long start a frame of its own: C2c-2b-3 measures the desktop and the page again before it
  changes anything.

  Every value of the five runs compiled ahead of time: [COOP-MEASUREMENTS.md](COOP-MEASUREMENTS.md), "The join in the
  browser".

  Every value of the three interpreted runs: [COOP-MEASUREMENTS.md](COOP-MEASUREMENTS.md), "The join in the browser".

- **Main after the build steps were split (C2c-2b-3's baseline).** Main with #741 (the park start's build
  steps split, which the join's level shares), #799 and #802 (`922fe3dab`, clean), measured as above: the
  desktop five times, the page five times ahead of time and three times interpreted. The level's last
  step fell to 65 ms on the desktop (172 before) and 62 ms ahead of time (141), the guests' step to 32
  and 24 ms (135 and 85). Left: on the desktop the swap's frame, 368 ms (287-433), now the longest, the
  objects' step 350 ms and the save's parse 154 ms; ahead of time the save's parse, 384 ms, the swap's
  frame, 316 ms (341 ms with its drawing), the objects' step 283 ms and a wait for the level's files of
  371 frames (403 build frames, 8.9 s from the frame that asked to join to the swap's, by the page's
  frame record); interpreted the objects' step 1.36 s, the swap's frame 658 ms with its drawing and the
  save's parse 582 ms.

  Every value: [COOP-MEASUREMENTS.md](COOP-MEASUREMENTS.md), "Main after the build steps were split".

- **Where a join's slice ends (C2c-2b-3a).** The build ends a frame's slice after the wait asked for the
  level's files once and before a step known to be long, and makes the save codec's metadata in a stage
  of its own (`codec`) before the parse; the measuring smoke records the named work of the swaps' frames
  (`FrameMarks`). Measured at `864ec4dc9` (clean) as above, medians with their ranges. The marks were on
  while each park was built and for the frame after its swap, where the baseline had none: their clock
  reads and records cost a little in those frames, so small differences from the baseline are no
  finding. They cover each frame's update, not its drawing.
  - **The wait for the files was most of a page's join.** Asked once a frame, the level's files arrived
    in 9 frames (8-10) ahead of time instead of 371 and in 8 interpreted instead of 387. The page's copy
    runs between its frames, so the loop that spent every frame's budget asking for them most likely kept
    it waiting (these timings do not follow the copy itself). The join's build took 64 frames (63-68)
    ahead of time instead of 403 and 107 interpreted instead of 471; from the frame that asked to join to
    the swap's, 2.7 s instead of 8.9 (interpreted 5.2 s instead of 13.2), and the catch-up after the swap
    took 0.26 s instead of 0.57 (0.75 instead of 2.17).
  - **The save's parse without the codec's metadata.** The metadata took 230 steps of one type each (on
    the desktop 69 ms over 6 frames, the longest step 5 ms; ahead of time 298 ms over 21 frames, the
    longest 19 ms), and the parse fell to 83 ms on the desktop (154), 88 ms ahead of time (384) and 191
    ms interpreted (582): of it, the JSON 45, 61 and 141 ms and the shape's check 37, 26 and 48 ms.
  - **The swap's frame**, now the longest frame of a join ahead of time, and on the desktop about as long
    as the longest frame while building (359 ms, the objects' step; interpreted, the objects' step took
    1.23 s): 352 ms on the desktop (293-384), 323 ms ahead of time (345 ms with its
    drawing) and 573 ms interpreted (627 ms). Its marks, the medians of the desktop's and of the page's
    compiled ahead of time:
    - the session's step, the check at the cut and the swap among it: 54 and 88 ms (the check 40 and
      75 ms);
    - the joined park's first update of its level (`park.level`): 116 and 185 ms, of it its entities'
      first update 65 and 67 ms (the first uses of object models, textures and materials), the pools' 19
      and 72 ms and the first simulation step 29 and 45 ms;
    - the advisor's first update (`park.advisor`): 166 ms on the desktop, where loading its welcome line
      takes 79 ms, 77 of them decoding the line's MP2 audio in one go, and compiling shaders on first use
      most of the rest (57 ms, `load:shader-spirv`; first put in the entities' first update here, until
      C2c-2b-3c's marks left no other part of the frame long enough to hold them), and 39 ms in the page,
      which decodes it over frames (`[EXT:advisor-sliced-decode]`);
    - the HUD's first update: 14 and 5 ms.

    A resync's swap's frame took 62 ms on the desktop and 141 ms ahead of time (`park.level` 42 and
    113 ms, the pools 13 and 65 ms of it); its advisor's first update came in the frame after (43 and
    13 ms).

  The swap's frame is C2c-2b-3c's: a park start prepares most of that first-time work in its build
  (`GameFlow.PrepareEntry`: the advisor's reads, the sky, the HUD's textures), which the join's build
  does not yet; the objects' step (352, 288 and 1,230 ms) is C2c-2b-3d's.

  Every value: [COOP-MEASUREMENTS.md](COOP-MEASUREMENTS.md), "Where a join's slice ends".

- **The swap's frame (C2c-2b-3c).** The session now checks the candidate at the cut in the frame the
  build completes in and swaps it in the next, the swapped-in stream holds its ticks to the frame after
  the swap, and the build's last stage (`prepare`) prepares the joined park's first frame: the coasters'
  views (their track, cars, models and textures), the pool bridges' assets and meshes, and the swimmers'
  sprite sets read ahead. Marks for the swap's parts (`coop.check`, `coop.install`, `coop.ready`,
  `coop.ticks`, `sim.commands`, each entity type's `update:<type>`, from the second measurement the
  pools' `pools.*`) first named the old swap's frame ahead of time: the session's step 90 ms, 77 of it
  the check at the cut; the park's first update of its level 184 ms, of it the pools' first frame 72, the
  first ticks 42 (a host's placement replayed among them) and the bridge's and the coasters' first
  updates 35 and 20 (their models, textures and materials made on first use); the advisor's first update
  39 ms. The pools' own marks then put 60 of their 70 ms in the swimmers' renderer, which read and decoded
  the swimmers' sprite sets in one go. Medians, in milliseconds (C2c-2b-3a's measurement five runs, the
  others three):

  | The swap's frame | Before | Check and swap apart, no ticks | First frame prepared |
  | --- | ---: | ---: | ---: |
  | Desktop, its update | 352 | 279 | 233 |
  | Desktop, a resync's update | 62.0 | 49.1 | 28.5 |
  | Ahead of time, with its drawing | 345 | 229 | 119 |
  | Ahead of time, a resync's, with its drawing | 144 | 121 | 39.0 |

  The check moved to the frame the build completes in (74 ms ahead of time, 39 on the desktop), the
  first ticks to the frame after the swap (that frame 51 ms ahead of time and 52 on the desktop, a
  replayed placement's first models among it: `sim.commands` 23 ms), and the prepared work to the
  build's last stage (113 ms ahead of time over 7 frames, its longest step 39 ms; 52 ms over 4 frames on
  the desktop, its longest 25 ms). What the swap's frame keeps: ahead of time the advisor's first update
  37 ms, the pools' first frame 23 ms (the swimmers' renderer, still made there, 15 of it), the session's
  step 13 ms, the entities' first update 12 ms (the pool's and the slides' renderers) and the HUD's 6 ms;
  on the desktop the advisor's first update, 109 to 170 ms (decoding its welcome line's MP2 audio in one
  go, 23 to 78 ms by the line it picks, and compiling its shaders on first use, 54 ms, which no other
  part of the frame is long enough to hold), then the pools' 18 ms, the entities' 15 and the HUD's 15. A
  join's longest frame is now the objects' step in both builds measured (292 ms ahead of time with its
  drawing, 357 ms on the desktop's update), C2c-2b-3d's; the interpreted build was not measured again.
  These marks cover more than C2c-2b-3a's did, and their clock reads cost a little in the frames they
  cover.

  Every value: [COOP-MEASUREMENTS.md](COOP-MEASUREMENTS.md), "The swap's frame".

- **The objects' restore over steps (C2c-2b-3d).** The level restores a joined park's saved objects one a
  step instead of all in one, and on the desktop the saved types' signs are composed on the sign worker.
  Three runs each, medians in milliseconds, before (`7d5b99985`, the first frame prepared) and after
  (`34606a26a`):

  | Part of the join | Desktop before | Desktop after | Ahead of time before | Ahead of time after |
  | --- | ---: | ---: | ---: | ---: |
  | The longest build frame, its update | 357 | 83.3 | 290 | 87.0 |
  | The objects' sections (`objects`) | 348 | 194 | 287 | 266 |
  | Of it the signs (`object-sign`) | 190 | 48.8 | 143 | 134 |
  | The level stage | 779 over 24 frames | 581 over 28 | 672 over 23 | 622 over 27 |
  | The join's longest frame | 357, the objects' step | 173, the swap's | 292 with its drawing, the objects' step | 110 with its drawing, the swap's |

  The level stage has 24 more steps, one per saved object and the allocators' last, over 4 more frames.
  Its longest step is now the restore's first, with the snapshot's checks and the first object: 60 ms on
  the desktop, 87 ms ahead of time. Three more of the restore's steps took 15 to 37 ms on the desktop and
  35 to 58 ms ahead of time. On the desktop 49 of the signs' 190 ms are still spent on the frames, where
  an object's step waits for its sign or composes one the worker has not reached; the page has no
  worker, and its signs are still composed on their objects' steps. A join's longest frame is now the
  swap's in both builds measured: 173 ms on the desktop (168-228), the advisor's first update most of
  it, and 110 ms with its drawing ahead of time, the park's first update of its level and the advisor's
  about 35 ms each. A resync's longest frame stays its save's parse (58.6
  ms ahead of time). Leads: the advisor's first update, the restore's first step (the checks and the
  signs' queue in steps of their own) and, in the page, a type's sign in a step of its own.

  Every value: [COOP-MEASUREMENTS.md](COOP-MEASUREMENTS.md), "The objects' restore over steps".

- **The advisor and its first lines prepared in the build (C2c-2b-3e).** The join's build reads what its
  advisor's first line reads and decodes the lines its start says first, so the swap's frame does
  neither. Three runs each, medians in milliseconds, before (`34606a26a`, the objects' restore over
  steps) and after (`86de0f752`):

  | Part of the join | Desktop before | Desktop after | Ahead of time before | Ahead of time after |
  | --- | ---: | ---: | ---: | ---: |
  | The swap's frame | 173 (update) | 75.3 (update) | 110 with its drawing | 90.1 with its drawing |
  | Of it the advisor's first update (`park.advisor`) | 106 | 5.3 | 33.9 | 4.3 |
  | The first frame after the swap | 47.1 (update) | 53.1 (update) | 48.5 with its drawing | 41.8 with its drawing |
  | The build's `prepare` stage | 47.3 over 4 frames | 235 over 14 | 103 over 7 frames | 187 over 14 |
  | The join's longest frame | 173, the swap's | 84.6, a build frame | 110 with its drawing, the swap's | 94.4 with its drawing, a build frame |

  On the desktop the Welcome (28.6 s) was decoded in one go in the swap's frame, in about 22 or about 75
  ms by the run (the same line), so the swap's frame took 168 to 228 ms; now it takes 70 to 78. Its
  advisor shader no longer compiles there (`load:shader-spirv` 50 ms before, 2 after). In the page the
  advisor's first update fell from 34 to 4 ms, and a line decoded ahead now starts at its time, as on the
  desktop, instead of a few frames late after its decode over frames (`[EXT:advisor-sliced-decode]`
  still decodes every later line). The `prepare` stage's longest step took 51 ms on the desktop, one of
  the advisor's, and 38 ms ahead of time, a coaster's as before (the advisor's took at most 14 there);
  the lines' decode is spread over slices of 5 ms. The join's longest frame is now a build frame: on the
  desktop 79 to 84 ms, the frame of the save's parse (79 to 85 ms), and in one run 189 ms, the restore's
  first step's (C2c-2b-3d's, 61 to 68 ms in the others); ahead of time 94 ms with its drawing (median),
  while the restore's first step took 93 ms and the save's parse 88 (medians). Ahead of
  time the save stage, which this change does not touch, took 87.6 ms against the before's 80.7. A
  measurement of the presentation's part alone (`4fdb4979a`) is kept with the tables; it ran after the
  Mac had slept, and its save stage took 97.8 ms ahead of time. The review fixes after the measured commit (the
  advisor's clock held until its first update, the kept preparation's end when the presentation cannot
  be made, the lines kept until said) move none of the measured work.

  Every value: [COOP-MEASUREMENTS.md](COOP-MEASUREMENTS.md), "The advisor and its first lines prepared".

- **A type's first use in steps of its own (C2c-2b-3g).** The level reads every saved type's model, sign,
  clips, textures and hoarding in steps of its own before the restore places the objects (step 6 above),
  the restore's checks end a step of their own, and the layout's checks format a message only when one
  fails (`CheckMessage`): the cell map's checks had made about ten strings for each of its 9,642 records,
  three times a join. The measuring smoke now also records each build frame of 30 ms or more with the
  stage and the step that ended it and its named work (`LongBuildFrames`), which
  `tools/coop_join_timings.py` lists by step and mark; a section open over several steps (the objects',
  the entities') shows its whole time in the frame it ends in. Three runs each, medians in milliseconds,
  before (`b5de0764e`, these records alone; the desktop's runs at `424247027`, the same with #810's fix of
  the guests' bubble turns) and after (`2615b32c0`):

  | Part of the join | Desktop before | Desktop after | Ahead of time before | Ahead of time after |
  | --- | ---: | ---: | ---: | ---: |
  | The restore's first object | 62.4, with the checks | 15.6 | 94.0, with the checks | 12.1 |
  | A type's first object, the longest | 36.0, the water ride | 32.0, the water ride | 62.2, the water ride | 20.5, the minecart |
  | The types' own steps, the longest | none | under the budget | none | 13.5 |
  | The validation stage's longest step | 20.9 | 8.0 | 27.4 | 6.7 |
  | The cells' checks, three a join (`cell-checks`) | 42.3 | 15.9 | 63.1 | 12.4 |
  | The level stage | 657 over 30 frames | 605 over 35 | 656 over 27 | 620 over 41 |
  | The join's longest build frame | 97.1, the save's parse | 86.4, the save's parse | 94.1, the restore's first step | 86.3, the save's parse |

  The objects' part of the level has no step over 21 ms ahead of time and no frame of 30 ms; its signs
  cost what they did (141 ms composed in steps against 142 on their objects' steps), spread over 314
  more steps. Of the minecart's 20.5 ms, 16.8 are its coaster ride's first use (`object-coaster`). On
  the desktop the water ride's object still waits for its sign on the worker (27 to 35 ms), one of the
  last signs the worker composes; reading every type's model and queueing its sign before the rest
  (`2615b32c0`) left that wait as it was in a measurement of the order before it. The save stage is
  untouched: its frame is the desktop's longest before and after (78 to 98 ms, 80 to 91). A join's steps
  of more than 50 ms are now the save's parse (86 ms ahead of time), the check at the cut (71 ms, in the
  frame its build completes in) and the level's last step (64 ms, the resume's restore). The review fixes
  after the measured commit leave the level's hoarding textures for the boards to gather (the steps
  still make each of them first) and a failed read of a texture folder unremembered ahead of use. They
  move one piece of work to the first boards' step: the four hoarding textures' paths are resolved
  again there and their textures made over the ones the steps made, which they share. These timings
  are of the measured commit, not of the fixes.

  Every value: [COOP-MEASUREMENTS.md](COOP-MEASUREMENTS.md), "A type's first use in steps of its own".

- **The save's read over steps (C2c-2b-3h).** A join's save is parsed a 32 KiB slice a step and its shape
  checked in steps of their own (step 1 above), and the save's checks format a message only when one fails
  (`CheckMessage`). The build's stages now also count the garbage collections their steps ran
  (`Collections`, `FullCollections`, as `GC.CollectionCount` counts them), the measuring smoke those of each
  long build frame, and the check and the level's last step name their parts: the world hash's and the
  session digest's sections (`hash.*`, `digest.session`) and the resume's restore (`resume-*`). Three runs
  each, medians in milliseconds, before (`9e48a4ee4`, C2c-2b-3g's last commit) and after (`0d7383931`):

  | Part of the join | Desktop before | Desktop after | Ahead of time before | Ahead of time after |
  | --- | ---: | ---: | ---: | ---: |
  | The save stage's longest step | 79.8 | 14.6 | 91.4 | 14.7 |
  | The save stage | 79.8 in one step | 87.6 over 43 steps and 7 frames | 91.4 in one step | 90.0 over 43 steps and 7 frames |
  | A resync's save stage's longest step | 25.9 | 1.8 | 53.8 | 3.5 |
  | The join's longest build frame | 82.5, the save's parse | 76.8, the level's last step | 91.5, the save's parse | 76.9, the level's last step |
  | A resync's longest build frame | 33.9 | 33.0 | 55.7 | 30.3 |

  The parse costs what it did (`save-json`: on the desktop 42.4 ms before, 46.8 after; ahead of time 62.9
  and 62.7), spread over its steps. Ahead of time the validation stage's last step, the comparison of the
  asset manifests, took 19.7 ms in five of the six runs of this change (`5baf9f4fb` and `0d7383931`)
  against 7 to 9 before, and 7.1 in the sixth; no garbage collection ran in it, and the cause is not known.
  It and the economy's step share a frame of 45 ms. The collections show that a cold join's build
  collects mostly in its level stage (13 young and 14 full collections ahead of time) and its `prepare`
  stage (4 and 4). A join's steps of more than 50 ms are now two, and both are mostly a process's first
  use of their code:
  - the check at the cut, 71.5 ms ahead of time against 7.9 in a resync: 56.5 ms of it is the economy's
    serialization for its digest (`hash.economyNode`), whose first run makes the serializer's metadata for
    the park save;
  - the level's last step, 69 ms: the resume's restore takes 59 ms of it against 6.3 in a resync (the
    coasters' 24.7, the drawn tracks' 11.1, the guests' 6.5).

  The review fixes after the measured commit complete the parse's reads with the thread's synchronization
  context set aside, so the serializer's own continuations run in the step too. Should the runtime still
  run one elsewhere, the parse fails instead of going on outside its steps. A save that begins with a UTF-8
  byte order mark is left to the whole parse, which refuses it. The fixes also make the save's last eager
  checks format their messages only on failure: the thing pool's names, the coasters' riders' places and
  the asset manifests' comparison, which runs twice in a join's validation. These timings are of the
  measured commit, not of the fixes.

  Every value: [COOP-MEASUREMENTS.md](COOP-MEASUREMENTS.md), "The save's read over steps".

- **The check and the level's last step (C2c-2b-3i, 3j).** Both were mostly a process's first use of
  their code: in the page, compiled ahead of time, the code a call runs for the first time still costs its
  compilation by the browser's WebAssembly engine. The build now runs the economy digest's serialization
  once ahead in a last stage of its own (step 8), after making that serialization's metadata in the codec
  stage (step 1), and restores the saved simulation a part a step (step 6). Three runs each, medians in
  milliseconds, before (`0d7383931`, C2c-2b-3h's measured commit) and after (`7e725f699`):

  | Part of the join | Desktop before | Desktop after | Ahead of time before | Ahead of time after |
  | --- | ---: | ---: | ---: | ---: |
  | The check at the cut | 40.2 | 12.2 | 71.5 | 13.4 |
  | The level stage's longest step | 66.3, its last | 39.8 | 68.9, its last | 28.1 |
  | The digest stage | none | 27.6 over 43 steps and 2 frames, longest 12.3 | none | 49.1 over 43 steps and 3 frames, longest 23.7 |
  | The codec stage | 67.5 over 230 steps | 77.1 over 312 | 288 over 230 steps | 308 over 312 |
  | The join's longest build frame | 76.8, the level's last step | 51.9, the advisor's shader | 76.9, the level's last step | 40.5 |
  | A resync's check | 2.0 | 2.5 | 7.9 | 8.0 |
  | A resync's digest stage | none | 7.0 | none | 25.0 over 3 frames |

  The resume's restore costs what it did (ahead of time 59.0 ms before and 55.2 after, the coasters'
  part the largest at 19.4), now over nine steps. The metadata with the resume's steps alone (`059006236`,
  without the digest stage) took the check ahead of time to 46 to 81 ms; the serialization run ahead takes it
  to 13.4 to 14.0. No build frame of a join now reaches 50 ms ahead of time: the longest are the
  validation's last step with the economy's (39 to 45 ms), the `prepare` stage's first and third steps
  (38 to 41) and the level's guests (37). On the desktop the advisor shader's compilation (52 ms, in
  `prepare`) remains a join's longest build frame.

  The swap's frame ahead of time measured 93 to 106 ms with its drawing (88.7 before, 87 to 92). That
  frame's pool swimmers (`pools.swimmers`) vary from run to run: in two more rounds of two runs each,
  alternating the pages of `0d7383931` and `7e725f699`, the before's swap frames took 101 to 119 ms and
  its swimmers 20 to 40, the after's 95 to 114 ms and 21 to 35. The change does not move it; the
  swimmers' first update is a lead.

  Every value: [COOP-MEASUREMENTS.md](COOP-MEASUREMENTS.md), "The check and the level's last step".

- **The browser's host (C2c-2b-4).** The page records the host phase and the desktop's measuring join
  phase replays it (above, "The browser's host"). Three runs, medians in milliseconds, the page compiled
  ahead of time against the desktop host of "The check and the level's last step" (`7e725f699`):

  | Part of a capture | Desktop host, capture 1 | Page host, capture 1 | Desktop host, capture 2 | Page host, capture 2 |
  | --- | ---: | ---: | ---: | ---: |
  | The cut (one step at a tick) | 9.9 | 17.0 | 8.3 | 20.7 |
  | Of it the session digest | 4.2 | 7.3 | 4.3 | 14.6 |
  | The save's serialization | 64.9 over 12 slices, longest 31.9 | 62.4 over 12, longest 22.7 | 11.0, longest 5.9 | 31.7, longest 17.5 |
  | The compression | 1.6 | 4.1 | 1.8 | 2.5 |
  | The capture's longest frame | 32.2 | 22.7 | 8.3 | 20.7 |

  Every frame of the page's host phase of 30 ms or more is recorded with its named work. Before the
  host's preparation (`ae3695e68`), the frame of the stream's first checkpoint after the park was
  shared took 69 to 77 ms, 48 to 51 of them the economy's digest running its code for the first time.
  With it (`10fdc69dd`) that frame took 31 to 33 ms in two runs and under 30 in the third, its digest
  warm. The other long frames are the smoke's own work, not a host's:
  - the fixture it builds (370 to 380 ms) and the first frame of its new rides and pools (about 180);
  - the cut's frames (51 to 63 ms), where the smoke takes two digests of its own besides the capture's;
  - the tour ride it buys at the second cut, whose type is used for the first time (59 to 68, its sign
    47 to 55 of them);
  - its final checks (83 to 104).

  What remains of a host's own work is the digest at each checkpoint, once a second of real time. At the
  session's end, ahead of time, it took 30 ms in a frame of 33 to 37 ms (the economy's canonical text 16
  to 18 of them, its serialization 9 to 10).

  Every value: [COOP-MEASUREMENTS.md](COOP-MEASUREMENTS.md), "The browser's host".

- **The digest's canonical text (C2c-2b-4b).** The economy's canonical text written from the save's
  bytes, without a JSON tree (above, "The digest's canonical text"). Three runs each, medians in
  milliseconds, before (`996b0e133`, C2c-2b-4's head) and after (`43bf2274e`); the page's join and the
  desktop's join each over a desktop host, the page's host over the desktop's join:

  | Part | Desktop before | Desktop after | Page before | Page after |
  | --- | ---: | ---: | ---: | ---: |
  | The guest's check at the join's cut | 13.1 | 9.7 | 12.6 | 11.2 |
  | The guest's check at the resync's cut | 2.0 | 1.1 | 7.6 | 5.9 |
  | The join's digest stage | 27.3 | 24.7 | 45.5 | 42.6 |
  | The resync's digest stage | 7.2 | 2.3 | 23.3 | 11.8 |
  | The host's digest at capture 1's cut, at capture 2's | 2.1, 2.5 | 1.7, 2.0 | 7.2, 11.7 | 6.1, 11.5 |

  In the page host's long frames the economy's canonical text took 7.9 to 14.0 ms and now 4.4 to 8.8
  (the medians of the `hash.economyCanonical` mark by stage), while the save's serialization took 8 to
  18 ms and still does (`hash.economyNode`, now `hash.economySave`). The page host's checkpoint frames
  in its last 1,500 frames (every 60th) took a median of 17 to 23 ms a run before, at most 32, and now
  16 to 18, at most 21: no checkpoint frame reaches 30 ms any more, and the last stage's long frames are
  its final checks alone. The save's serialization is the digest's remaining cost in the page.

  The resync's longest build frame in the page is the prepare stage's third step: 31 to 36 ms before,
  37 to 50 after, with a full collection in it in two of three runs (none before). That step is not this
  change's work.

  Every value: [COOP-MEASUREMENTS.md](COOP-MEASUREMENTS.md), "The digest's canonical text".

## Building by every player (C3)

`[EXT:online-coop-commands]` `[EXT:online-coop-permissions]` In C2 the host builds and the guests watch.
C3 lets every player build through the same stream the host orders, so every park stays the host's and
no guest's click runs on its own park first. C3a-1 builds the requests, their records and the host's
grants; the guests' tools use them in C3a-2 (with the host's switch for each guest's grant).

**A guest's request.** A guest never applies its own command: its park follows the host's stream only
(`ParkCommands.FollowsStream`), whatever its rights. It sends the command to the host as a request
instead (`CoopGuestCommand`: the guest's own number for it, from 1, and the command's bytes), over the
session's relay, which stamps the sender's member id. The host runs the command as that member's seat
(the server's word, never the command's own player) between two ticks at the start of its next frame:
after its own input, before the frame's ticks, and while paused too (`CoopHostStream.ApplyGuestCommands`,
`ParkCommands.ApplyRemote`). Every player's commands have one order, the host's.

**Its record.** Every well-formed command the host applies becomes a record of the stream, as the
host's own do: accepted, or refused by the rights or the park's rules on every peer alike. A guest's
record also carries its origin, the member id and the request's number (the stream frame's version 2).
The guest resolves its request when that record applies in its own park, with the command's result
there, which the stream's determinism makes the host's.

**Refusals without a record.** The host takes no record of a request that is larger than 1 KiB, comes
too fast (a bucket of 40 that refills at 20 a second), comes from no guest's seat or does not decode
(`CoopGuestRefusal`), and answers it alone (`CoopGuestCommandAnswer`). At most 64 requests of one guest
wait for the host's next frame; more are dropped unanswered, and the next request shows the gap.

**Lost and repeated requests.** A request travels at most once. After its link resumes, the guest
sends every open request again, in order. The host keeps the answers to each guest's last 256 requests:
a request it had already gets its record or its refusal again and never applies twice, an older one is
forgotten (what became of it is not known), and one that skips a number is refused with the number the
host expects, from which the guest sends again (once for a burst of such answers).

**Resyncs.** The requests are the session's, not the park's: a resync replaces the guest's park and
stream and keeps its requests (`CoopGuestCommands`). While the resync runs, new requests wait. Once the
new park is in, a request whose record is inside the snapshot's cut resolves as applied there (its
result here is not known), and the others go out again; the host answers those it had with their
record. The session's end resolves the open requests as ended.

**Grants.** The host grants building per guest (decision Q7) with a command of the stream
(`SetBuildGrantCommand`), so every peer's park holds the same grants from the same tick on. A join
snapshot carries them (the join state's version 3), and the session digest covers them once there is
one, so a session without grants keeps the digests it had. A grant is its member's: when the member
leaves or is kicked, the host takes it back at its next frame, before that frame's requests, so a member
that takes the freed seat starts without one.

**What a granted guest may build (C3a).** `CoopBuildRights` asks of a command's kind, its fields and its
target, at execution on every peer: path and queue lines, the queue's edit, a placed object (not a moving
placement, not a track add-on), the removal of a cell's object, path or queue (not a coaster's or a track
ride's station, whose edits need claims), and land. Everything else stays the host's: pause and speed
(Q8), cheats (Q17), money and the park's management (C4), undo lines (whose cells a request could name
freely), the Move tool, staff, pools, tracks and coasters (C3b, C3c) and the grants themselves.

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
  purchase skips research and the balance test. The entry stays carried until the player's Move tool
  ends, whatever ends it (a placement without exactly Ctrl, the queue or track tool a placed ride
  opens, a cancel, another tool): the end is a command of its own (`EndMoveCommand`, issued as the
  build tool leaves the Move mode), since a placement cannot say whether the tool stays. While it
  stays (a placement holding exactly Ctrl), another moving placement of the type is accepted and
  charged, as the original's mode 0x3b takes it. The queue or track tool a moved ride opens ends the
  move for good, so its end is issued as that tool opens, not when it ends: the original's queue tool
  ends in mode 0 at its last click whatever the modifier (tp.exe `0x5275f2`), with neither mode 0x3b
  nor the build tool's mode 4 armed again (docs/UI.md, "The Move tool").
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
session's rules when the log says so; `FirstMismatch` compares the checkpoints. A log is guaranteed to replay only on the build that recorded it: kind 14 (`BacktrackCommand`) gained meaning over pool water and pool bridge cells with the pools extension's Backspace removal, so a log recorded before that, with the key pressed over such a cell, did nothing then and does something now.

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
| Objects | place (the build tool, the Move tool, track add-ons), the remove tool, remove at a cell, sell from the object and attraction windows, the Move tool's pick-up and its end, the bulldozer (area clear: one command per cleared cell) | converted |
| Paths and queues | path and queue lines, undo inside the tool, path and queue cell removal, Backspace over a cell, the queue edit click | converted |
| Developer prototype | place and remove the Totem, open and close it (the developer panel's button) | converted |
| Winning screen | the end-of-park finale's dismissal in either game type (Escape, the late right click) | converted |
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
- The Move tool's end ends the issuer's tool whatever its command's result (`EndMoveCommand`): a
  refusal would leave the picked-up type carried on every peer while the issuer's tool is gone. C3
  must always allow it, and a member that leaves the session mid-move must have its Move tool ended
  on every peer (as its hand and its hire must be), or the next player in its slot inherits the
  carried type.
- A hire's pending candidate (`ParkStaff.PendingPlacementId`) is one per park: two players placing hires
  at once would take it from each other (C3 gives each player a placement).
- Opening the queue tool (after a ride is placed, its track circuit closes or its coaster is completed,
  from the ride window or on a queue click) recomputed the ride's queue on the issuer's machine
  (`Level.EnterQueueTool`), outside the commands. Since C2b-2 it reads the queue from a copy, and the
  queue line commands bring the ride's queue up to date first ("The level in a session"). The tool's
  previews still read the ride's own queue, which a grid edit since the last recompute (in a paused
  park) leaves behind until the line's command or the next tick takes it in.
- The join's measurements and the browser's join (C2c-2b) follow the game's join (C2c-2a). A snapshot's
  upload has the online client's overall 60-second timeout (its download resumes with a range instead).
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

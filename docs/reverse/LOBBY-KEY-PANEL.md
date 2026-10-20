# Lobby key counter: bounded static contract

## Scope

ADVISOR-025 remains open. This slice displays the saved application-owned Full
Simulation key counter in the actual `FrontEndMenu`, using Lobby EC's ED icon
and EE text slots. It does not restore the EA/EB affordability display, native
player profiles, lobby advisor dispatch, navigation state changes or effect 0x61.
No original executable was run and no original media is included.

## Static evidence

Build examined: TPW `sp20_nocd/tp.exe`, SHA-256
`5472f97b5eb28f58b5a49f3a15f6f66adffa6b2625a623f503202c9696d1b805`.
Bounded disassembly: `0x4b9340..0x4b9838`, supporting callee
`0x65d9dd..0x65da83` (visibility bit), and `0x5b11a0..0x5b11b8`.
This is a static branch trace, not a reference-run or pixel-parity claim.

- `0x4b935d..0x4b9365`: absent lobby window returns without creating UI.
- The first phase finds a selected record via `0x5b09c0`. A missing record
  skips to the counter phase at `0x4b9681`.
- `0x4b943f..0x4b9537`: GameType global `0xfb3b7c == 2` hides EA and EC;
  the other branch shows them. Both branches also call the F0/F1 virtual
  at offset +0x14 with 0/1 respectively. Do not infer that EF is in a
  contiguous visibility group: this function does not address EF.
- `0x4b9573..0x4b9589`: the selected record's `0x5b11a0` result is compared
  with ledger keys from `0x5af680`. The callee returns a valid record's
  nested +0x14 field, or -1 for an invalid record. This slice does not
  assign missing-record semantics or create an affordability API.
- At or above that threshold, EA frame 0 and EB frame `threshold - 1`
  are selected, and `0x4b960e..0x4b9624` creates effect 0x61 only if its
  global handle is absent. Below it, EA frame 1 and EB frame
  `threshold + 4` are selected and `0x4b9662..0x4b967b` removes an
  existing effect. The previous annotation's **count-drop effect claim
  was incorrect**: this branch compares current keys to a record field,
  not to the previous key count.
- Counter phase `0x4b96be..0x4b9714`: GameType 2 suppresses EC; otherwise
  ledger keys <= 0 hide EC (`0x4b97f8`). Positive keys show EC and also
  show EA and call the F0/F1 virtual with 1 (`0x4b971a..0x4b9786`).
- `0x4b97c6..0x4b97f3`: format the ledger key count with the UTF-16
  literal `%d x` at `0x758194`, assign EE's text virtual +0x24.
- `0x4b9816..0x4b9824` stores the current key count after refresh; no
  previous-count comparison occurs in this function.

GameType 2 is Instant Action, as already documented in `docs/UI.md` from
`0x550ca0`. No additional interpretation of the arrow virtual is needed
for the counter-only slice.

## Host ownership and adaptation

`GameFlow.ShowFrontEnd` first calls `TearDown`, which requires progress to
be saved and detaches the park owner. Only then does the menu initializer
read `PlayerTicketFile.ReadIfPresent`. It never attaches a second mutable
owner, manufactures a starter key, writes a profile or reads native gms.dat.

The host chooses mode per park and has no persistent native player-mode
selector. The counter therefore represents only its saved Full Simulation
ledger, regardless of which park mode was last played. Missing, invalid,
unreadable or other-mode progress yields no display. Read errors use the
existing progress exception classification and are logged; entry retains
its existing validation and failure dialog. TPI/unsupported economy routes
supply no count.

This is a creation-time snapshot. Returning from a park rebuilds the menu
and reads the newly saved count. No unused refresh/notification API is
introduced. Existing menu navigation/name controls retain their current
host behavior; this is not a claim of native visibility parity for them.
Text font, fitting and right alignment use the existing HUD counter style;
the native text style and rendered visual match are not newly established.

## Synthetic acceptance

`LobbyKeyCounterTests` exercises 0, 1 and many keys, exact counter/icon
rectangles, count text, missing and corrupt files without writes, mode
filtering and renewed snapshots after changed/deleted progress. It also
checks that the host's navigation/name controls remain present. These
are host widget/data tests, not execution of native branches, end-to-end
rendering, or a full `ShowFrontEnd` scene transition.

## Remaining gates

The other live refresh path: the lobby window procedure also calls the
refresh on message 0x11 with a nonzero parameter (`0x4b8c60..0x4b8c64`);
REA lists `0x4b8b70`, `0x4b8ca0` and `0x599880` as the refresh's callers.
OpenTPW makes none of these refreshes.

EA/EB art, native arrow virtual semantics and any hidden/show side effects;
selected-record threshold provenance; effect lifetime outside this function;
lobby sequences 28/29 and timer reachability; particle positioning and pass
ordering (PTCL-909); original visual captures. No ADVISOR-025 retirement or
full feature completion is claimed by this slice.

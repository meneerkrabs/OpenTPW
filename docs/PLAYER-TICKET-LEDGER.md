# Application-owned player ticket ledger

The application-owned ledger now connects to guarded New Full Simulation entry
and eligible live park awards. It remains separate from native profiles and
player-global mystery purchases. The legacy `PlayerProgress` count helper is
unchanged. The host entry and lifecycle policy is documented in
[Progress entry](PLAYER-PROGRESS-INGRESS.md).

`PlayerTicketLedger` records the existing `GoldenTicketKind` identities. Visitors,
PeopleInPark, Happiness, AllResearchedAndBuilt, ProfitYear and RecentVisitors are
local to jungle, hallow, fantasy or space. CoasterHeight, GokartExcitement,
WaterLength and BigPark are player-wide globals; CamerasEverywhere and AllLandOwned
are player-wide secrets. Recording a global or secret in another theme does not
create a second identity. Known earned park identities can be reconciled repeatedly,
including after restore. A batch is completely validated before the ledger changes.
Storing AllLandOwned records an existing host identity, not a newly traced native
award path or a change to its earning predicate.

The scope and key arithmetic reuse the player/ticket evidence in
[the scenario notes](reverse/PPC-scenarios.md): extra keys plus
floor(unique earned identities / 3). Spent available tickets remain a separate
saved count; they do not erase earned identities or keys. This first slice has no
purchase/uncovered-item transaction or spend action. Mode, extra keys and spent
count round-trip as explicit validated state. A newly constructed own ledger has
zero earned tickets and zero extra keys. It does not award the native first-lobby
key: that requires a separately reviewed application policy and frontend owner.

`PlayerTicketFile` uses format `opentpw-player-progress`, version 1, with a proposed
own filename `opentpw-player-progress.json`. The GameFlow owner uses the existing own save directory; no named/multiple
profile slots are installed. JSON destinations only prevent
native `gms.dat`/TPWS/TPWI access. Missing files return unknown (`null`), distinct
from an explicitly created empty ledger. Legacy per-park ticket counts never
supply unknown player-wide or cross-theme history.

The file rejects missing/unknown/duplicate JSON fields, numeric or unknown enums,
noncanonical or forged identity scopes, duplicate earned identities, bad counters,
unknown versions, and bodies above 64 KiB. Enum fields require one exact,
case-sensitive name; numeric strings, surrounding whitespace, different case and
comma-combined aliases are refused. Built-in enum parsing can combine names into
another defined non-Flags value, so this own reader validates the exact name. There are at most 30 valid identities
(24 locals, four globals, two secrets). Model restore copies caller state; captured
identities are read-only. Serialization validates before filesystem mutation;
existing temporary/flush/rename logic publishes complete files and cleans its own
failed temporary file. Loading reads current bytes and never mutates another live
ledger. A changed or malformed file is not silently replaced by an empty profile.

Synthetic tests cover all twelve kinds and four themes, idempotent cross-theme and
restored reconciliation, invalid-batch nonmutation, scope forgery, immutable copies,
mode/extra/spent arithmetic, strict JSON, size bounds, failed validation preserving
a previous file, rename refusal/temporary cleanup, and changed-source reload.
There is no original profile interoperability, multislot UI, global mystery-spend
transaction, TPI progression, native parity or actual theme-gate qualification.

A separate correction review covers every pair of ticket names whose bitwise
combination would be a defined value (112 pairs), plus all four mode pairs. They
are refused even when the supplied theme is valid for that forged result. Exact
names apply to decoded JSON strings: legitimate Unicode escapes for the same
known name remain accepted and serialize back to the unchanged canonical tokens
for both modes and all twelve kinds.

## Global ticket holder (tp.exe 0x5AF940)

A global ticket (CoasterHeight, GokartExcitement, WaterLength, BigPark) is held by one park
at a time. The first win sets the player-wide flag and the winning park's held byte and
record; afterwards a park that does not hold it takes it from the park that does when its
record is higher than the holder's (result 4 of `0x5AF940`). The old park loses only its
held byte: the player-wide count (`0x5AF4A0`, the four flags once plus the locals of every
park) does not change, so no park's ticket total or key count moves. What the player sees is
one advisor line, message `0xEA + type` (234 to 237, one response each, responses 474 to
477), queued by `0x4D5270`, plus a debug log line "Global ticket award moved here"
(`0x75A71C`). Nothing is shown on the old park, and no other code reads the held bytes.

The ledger keeps the holder per global (`GlobalHolders` in the progress file: kind, theme,
record), optional and validated: a holder must be a global kind that is earned, a canonical
theme and a record of at least 0, at most one per kind. A file without the field loads with
unknown holders, and the first park that reports a global it has earned adopts the holder
with its record (a holder adopted by `Reconcile` without a record keeps 0 until its park reports one). `PlayerProgressRuntimeOwner` attaches the ledger to the park's objectives while
it is eligible, so a park without a holder role does not earn the ticket a second time, a park
with a better record takes it (`ParkEventKind.GlobalTicketMoved`, advisor message 234 + type),
and a lower or equal record changes nothing. Without an eligible owner (Instant Action, no
progress file) the park keeps its own set as before.

## Tickets a cheat awarded (docs/CHEATS.md)

The original's ticket cheats (Alt+G, Alt+Num1..9, `0x40C6C0..0x40C7E0`) write the same ledger bytes as a win (`0x5AF810`, `0x5AF940` with record 100), so a cheat ticket counts toward
the keys and the theme unlocks like any other, and OpenTPW keeps that (the user's decision of 2026-10-19). A ticket cheat raises `ParkEventKind.CheatApplied`, not `GoldenTicketWon`, so the
advisor says nothing, as in the original. `PlayerProgressRuntimeOwner` records the ticket: a local one is earned now, a global one was earned through the holder rule above (record 100: it
moves from a park with a lower record, a record of 100 or more keeps it). `[EXT:cheats-ticket-provenance]` The ledger also keeps which earned identities a cheat awarded
(`PlayerTicketLedger.Cheated`, `"Cheated": true` on the ticket in the progress file, absent otherwise, so a file without cheats keeps its bytes), only when the cheat earned the identity:
a ticket the player had earned before, or a global that only moved, keeps its honest provenance. Nothing reads the mark yet; it lets a later online profile leave cheat tickets out.

## Parks whose research is complete (tp.exe 0x5B0C40)

The original player's file keeps one record per park (theme) with a byte `mAllResearchCompleted`
(`+0xB9`, written through `0x5B0D70`); the lab's completion handler sets the current park's byte
whenever nothing is left to research (`0x5B0CA0`, every game type), and the Full Simulation starts
the end-of-park finale only when every park's byte is set (`0x5B0C40`). The ledger keeps that list
(`PlayerTicketLedger.ResearchCompleted`, `"ResearchCompleted": ["jungle", ...]` in the progress
file in theme order), optional and validated: canonical theme names, each once, at most four. A
file without the field (written before it existed, or by a player who has completed no park)
loads with none, and such a ledger does not write it, so the file keeps its bytes. A Full
Simulation park records through its owner; an Instant Action park marks the progress file when
there is one. The finale and its rules are in [the end-of-park finale](reverse/INSTANT-ACTION-FINALE.md).

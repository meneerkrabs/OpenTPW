# Application-owned player ticket ledger

The first ledger slice provides a model and strict own JSON file only. It is not
connected to the frontend, a running park's event stream, native profiles, theme
entry or mystery purchases. Existing `PlayerProgress` count behavior and lobby
policies remain unchanged until a separate integration decision.

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
own filename `opentpw-player-progress.json`. Callers supply an own save path; no
production caller or profile slot is installed. JSON destinations only prevent
native `gms.dat`/TPWS/TPWI access. Missing files return unknown (`null`), distinct
from an explicitly created empty ledger. Legacy per-park ticket counts never
supply unknown player-wide or cross-theme history.

The file rejects missing/unknown/duplicate JSON fields, numeric or unknown enums,
noncanonical or forged identity scopes, duplicate earned identities, bad counters,
unknown versions, and bodies above 64 KiB. There are at most 30 valid identities
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

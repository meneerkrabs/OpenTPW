# Original snapshot JSON persistence contract

`OriginalScientistSnapshotEnvelope` is standalone and dependency-free. It
uses its own format `opentpw-original-scientist-snapshot`, version 1, with
mandatory scope `original-data-only; persisted-source-claims`. This is a
separate envelope, not production `ParkSaveFile` version 1 or an original
TPWS writer. No production file, runtime staff, actor selection, calendar,
mode, candidate pool, or global restore is changed.

`Serialize(snapshot)` returns bounded UTF8 JSON. `Deserialize(json)` returns
`PersistedOriginalScientistSnapshot` containing a newly constructed immutable
snapshot, after all syntax, shape and consistency checks pass. It never
modifies an existing snapshot or applies it to a simulation. There is no
file-write/restore API in this helper.

Every persisted snapshot field and provenance member is explicit and required,
including nullable provenance fields. Missing, unknown, case-mismatched and
duplicate members reject at every object level; trailing JSON, comments and
depth over 16 reject. Input is limited to 128 KiB, a resource bound for this
small contract. Qualification accepts only the two exact enum names, not
integers or numeric strings. Version/format/scope mismatch rejects.

Names are arrays of exactly 33 unsigned UTF16 code units, preserving embedded
NULs and lone surrogates without Unicode decoding/replacement/normalization.
The name SHA is recomputed from little-endian units. Vitals store their raw
u32 float bits, so NaN payloads, infinities, subnormals and negative zero do
not pass through JSON floating-point normalization. Grade, state, coordinate,
patrol, rest/job/link and native-u32 tick words remain unchanged. Native hire
time is exactly 16 lowercase hex digits: this avoids rounding native-u64
values through JSON consumers that use double-precision numbers. It remains
a raw timestamp with no unit/calendar conversion.

The prefix contains its recorded IDs, models and offsets; loading does not
infer any actor from array order or a game mode. Validation checks those facts
agree: 1..64 rows, guest models before the final scientist, explicit next-ID
connections, unique/nonzero IDs, no visible back/self cycles, native sizes
525/501, contiguous framing, matching final actor and supplied role-head ID,
and bounded nonnegative unparsed tail size. The scientist's role self-link
also rejects. Other following actors/researcher links and person contents
remain unparsed; no whole-World completeness is gained by persistence.

Digests must be canonical lowercase hex. The schema reference must match
the separately reviewed Mac framing reference; its citation is bounded and
retained. Caller-boundary provenance cannot grow a container SHA or verified
world tick. Identified-fixture provenance must keep the exact known
container/payload identities, head boundary, role-head ID and world tick.

**Persisted source provenance remains a claim.** The JSON envelope contains
neither the original container nor its decoded payload. It cannot reauthenticate
that payload, the unparsed person hash, or each original scalar. Valid scalar
edits with internally consistent metadata are not cryptographically detected.
`SourcePayloadRevalidated` and `CanRestoreRuntimeStaff` are always false on
the persisted wrapper. Original source qualification is preserved as origin
metadata rather than promoted into newly verified source evidence. A future
consumer requiring verified original provenance must separately compare with
the identified source; it must not confuse envelope validation with original
file or gameplay-rule validation.

Malformed envelopes fail before the immutable result is published. Tests
check every missing root/snapshot/provenance/actor/prefix member, unknown and
duplicate fields, nulls/types/versions/enums, name/hash mismatches, invalid
links/offsets/models, bounds/depth and preservation of existing state after
refused loads. Roundtrip tests compare all stored representations and canonical
JSON, with a 66-case float-bit/native-u64 boundary matrix and actual PC source
snapshot/provenance. No source assets or generated fixture JSON are checked in.
Release and Debug with the real fixture each pass 16 groups / 1,648 assertions.

The existing standalone runner includes these tests:

```sh
dotnet run --project tools/ppc-analysis/lanes/economy/OriginalScientistSnapshot.Tests.csproj --configuration Release -- --fixture /external/path/Easymode.TPWI
dotnet run --project tools/ppc-analysis/lanes/economy/OriginalScientistSnapshot.Tests.csproj --configuration Debug -- --fixture /external/path/Easymode.TPWI
```

Production handoff is a separate decision: persist this envelope only through
an explicitly versioned original-snapshot field, preserve current production
save migration/mode contracts, and qualify every runtime ID/name/state/time
bridge before restoring any gameplay state.

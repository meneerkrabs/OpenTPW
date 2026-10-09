# Standalone original scientist snapshot

`OriginalScientistSnapshot.cs` implements only the reviewed actor-chain prefix
from `6305d32` / `staff_evidence.py`. It has no production or package references
and does not execute original code. It returns original data, never a runtime
`StaffMember`, inferred `ParkGameMode`, selected arithmetic policy, or complete
World restoration.

`ReadPrefix(payload, usedThingHeadOffset, expectedFirstResearcherId?, maximumActors)`
follows used-list IDs without scanning. The caller must qualify the boundary;
the returned provenance remains `CallerSuppliedPrefixBoundary`, even if its
payload happens to match the known fixture. Its payload hash is computed, not
accepted from the caller. Only model 1 (guest) and model 8 (scientist) are supported
before the first scientist. Sizes are person 390 + guest 135, or person 390 +
staff 105 + scientist 6, with an additional 8-byte next/model header per actor.
The operational bounds are 8 MiB and 1..64 records; these are reader resource
limits, not assertions about all original variants.

`ReadIdentifiedPcContainer(container)` first requires the reviewed PC container
SHA `6d89303d098900364bf5e80b236b64bd85976fb947e9e4609d088547f430b39a`.
It performs bounded data-only zlib decompression and checks decoded SHA
`a3c9a28252c37ad49a8eb78e4a0c5e1d5229d01548fa35801db67015d2589173`.
Its fixed fixture boundary is qualified by that identity and the existing
formats map-end evidence; it is not a general map/calendar locator. The source
World-v2 header supplies FirstResearcher 30 and observed original game tick 755.
Other containers are rejected before decompression. No container decoder for
unknown variants or general TPWS support is claimed.

The immutable result preserves original actor/current-next IDs and prefix
offsets; coordinate/map words; grade word; jobs done; patrol words; percentage
byte; rest ID; raw state word; u32 idle/research ticks; u64 hired timestamp;
and researcher-linked next ID. Name storage preserves all 33 UTF16 code units,
including NULs and unpaired surrogates. It supplies no guessed NameIndex.
Happiness/energy preserve their exact saved 32-bit float patterns, including
NaN payloads and negative zero. The native save writer's truncation/low-byte
conversion happened before those floats were serialized; the reader does not
repeat that lossy conversion or normalize them. Native times remain in their
original domains; no 60 Hz tick, elapsed-day or runtime hire-time conversion
is performed.

Provenance records the computed payload/container identities, the inspected
Mac schema reference SHA, evidence revision, boundary, expected role-head ID
and observed original world tick when qualified. The Mac schema reference is
an evidence citation, not a claim that this reader re-inspected the PEF or that
PC work/rest/research arithmetic matches Mac behavior.

Person navigation/thought contents are not interpreted; their record hash is
retained. The reader stops at the first scientist. `PayloadBytesAfterRecord`,
`HasUnparsedUsedActors` and `HasUnparsedResearcherLinks` identify remaining
data/links; `IsCompleteWorldSnapshot` is always false. Unsupported/truncated
tails after the returned record remain unqualified rather than being decoded
with invented sizes. Visible used-list back-links/self-links and scientist
role self-links reject; cycles entirely inside an unparsed suffix cannot be
detected. Qualified-prefix truncation, unsupported preceding models, absent
scientist, expected-ID mismatch and record-limit exhaustion also reject.

The actual PC fixture walks UsedThingHead 42 at 1,385,521 through twelve guest
records to scientist 30 at 1,391,921, body 1,391,929..1,392,430. It yields grade 2,
percentage 0, saved happiness 97/energy 93, raw state 1, idleTick 0,
researchTick 697, hired timestamp 125935884000000000 and researcherNext 0.
The next used actor is 29, so the returned suffix is explicitly incomplete.
No original bytes, names, containers or assets are included in Git.

Run the dependency-free regression project with an externally supplied fixture:

```sh
dotnet run --project tools/ppc-analysis/lanes/economy/OriginalScientistSnapshot.Tests.csproj --configuration Release -- --fixture /external/path/Easymode.TPWI
dotnet run --project tools/ppc-analysis/lanes/economy/OriginalScientistSnapshot.Tests.csproj --configuration Debug -- --fixture /external/path/Easymode.TPWI
```

Without `--fixture`, synthetic groups still run and the actual fixture is
explicitly reported as not run. Tests cover every truncated prefix length,
current/next-ID distinction, raw field preservation, UTF16 ownership, float
bits, visible cycles, unsupported models, absent scientist, wrong world ID,
resource bounds, explicit unparsed tails and identity rejection. The real-file
group checks all interpreted scientist fields, 13 actor headers, exact offsets,
name fingerprint, original world tick and provenance. It prints metadata/hash
only. The same decoded fixture through the caller-boundary API retains its lower
qualification, confirming that payload identity never silently promotes the
origin. The combined reader/envelope runner now passes 16 groups / 1,648 assertions
in Release and Debug with the actual fixture. The separate persistence contract
is documented in [SCIENTIST-ENVELOPE.md](SCIENTIST-ENVELOPE.md).

Production handoff still requires explicit seed selection, ID/name/time/state
bridges and versioned persistence. Do not clear the candidate pool while adding
this employee or derive lab research history, wages already paid, bank history,
calendar event phase, or mode from this snapshot.

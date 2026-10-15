# TPI balance profile preparation

`TpiBalanceProfile` reads a bounded cash/admission/build-cost subset without creating
`BalanceSettings`, which requires TPW staff, research and challenge fields. It is a
settings adapter for later runtime work; it does not change `GameEditions.Capabilities`,
`Level`, `GameFlow`, weather, staff, missions, saves or campaign progression.

Every present field is a `TpiBalanceValue<T>` carrying the winning `SamEntry` with its
original key, source file, line and tokens. Missing fields remain null and appear in
`Readiness.MissingSupportedKeys`. Noninteger values retained by `SamDocument`
throw instead of receiving defaults. The shared parser omits lines whose first
value is an unquoted, unrecognized token; those keys remain missing rather than
becoming ready. The existing `CellCosts` contract is available only when all five
cost fields are present. An original zero is distinguishable from an absent value.

| Typed field | Exact SAM key |
|---|---|
| InitialCash | BankAccountInfo.InitialCash |
| InitialAdmissionFee | BankAccountInfo.InitialAdmissionFee |
| QueueCell | Costs.QueueCell |
| PathCell | Costs.PathCell |
| KartTrackCell | Costs.KartTrackCell |
| WaterTrackCell | Costs.WaterTrackCell |
| MapCell | Costs.MapCell |

No renamed financial fields, loan schemas or staff-field aliases are qualified here.
`FromLayers` accepts explicit caller-selected SAM documents in override order; it
does not assume TPW's easy-mode ordering is TPI's campaign ordering. For example:

```csharp
var profile = TpiBalanceProfile.FromLayers(new[] { globalStandard, themeStandard });
if (profile.Costs is { } costs)
    Console.WriteLine($"Path cell: {costs.PathCell} ({profile.PathCell!.Provenance.Source})");
```

## Readiness and the 89/155 claim

`SupportedSettingsReady` reports only availability of the seven fields above.
`EconomyReady` remains false even for complete data because the existing runtime
requires unqualified TPI execution rules and the broad TPW `BalanceSettings` type.
Its blockers explicitly name bank/admission/calendar/loan execution, staff,
research, missions/progress and the runtime's staff/weather/park-gate coupling.

The current TPW functional-test schema contains **152 fixed Standard keys** once
index-discovered `LoanInfo[n]` and `ChallengesInThisLevel[n]` tables are excluded.
`BalanceSettings` also reads three **optional** theme-global fields:
`Keys.CostToEnter`, `Tickets.CanEarnTicketsInTheme`, and
`Tickets.CanSpendTicketsInTheme`. Adding those yields 155, but neither a flat key
count nor absent optional keys establish constructor or runtime compatibility.
Loan and challenge tables, theme-local golden-ticket fields and overridden values
must be assessed separately.

`TpiBalanceProfileOriginalDataTests` audits the retail global file against the 152
fixed keys and logs every missing key. The theme tests read global plus each
Arabian/science/water/wIntro Standard layer and log supported field file/line/value
provenance. They require `OPENTPW_TPI_PATH` pointing to an installation or its Data
folder, and do not commit original assets.

**Verification limitation (2026-10-13):** the assigned private corpus path resolves
to `/Volumes/USBSSD/repos/offload/game-assets`; filesystem reads stalled during this
work. Therefore the earlier **89 missing** claim was not reverified, exact retail
values/lines and renamed-field equivalence remain unqualified, and the corpus tests
have not passed against original data. Existing comparison evidence reports shared
BankAccountInfo/Costs families; it does not substitute for these tests. Run them
before integrating this adapter as a qualified retail TPI profile:

```sh
OPENTPW_TPI_PATH=/path/to/TPI/Data dotnet test source/OpenTPW.Tests/OpenTPW.Tests.csproj \
  --filter FullyQualifiedName~TpiBalanceProfileOriginalDataTests \
  --logger 'console;verbosity=normal'
```

## Runtime integration blockers

1. Supply an edition-scoped economy settings contract that consumes this subset
   without supplying fabricated TPW staff/research/challenge values.
2. Qualify TPI bank/admission/loan/calendar and object purchase/refund rules before
   charging a park. Matching SAM keys prove data availability, not execution rules.
3. Separate cash/building capability from `Economy`, whose current level/frame path
   creates staff, weather and park gates and whose constructor consumes campaign
   settings. Do not enable those systems via this profile's data-readiness flag.
4. Qualify missions/research/availability and TPI save/import behavior independently;
   preserve existing refusals until each subsystem is ready.

## Independent review

Review target: `30eb477a33aa8eafe6385a8a90d72ce421ad2073` against
`c884e2b2`. **ACCEPT for settings preparation only**, subject to the original-data
qualification limits above. No production adapter change was needed. The review
clarified parser omissions in this document and added three bounded synthetic
tests to the existing five cases.

The seven fields use exact full SAM keys with the shared case-insensitive lookup;
suffix/prefix aliases are not consulted. Later documents win, and the last
duplicate line within a document wins. Provenance is the actual winning entry,
including the original key spelling, source, line and token list. The adapter
does not choose campaign layering or merge rules on behalf of a caller.

Integer conversion uses invariant signed decimal `long` parsing and checked
`int` conversion. Fractional, exponent, nonfinite and quoted thousands-separated
forms retained by the parser are rejected; no rounding or clamp occurs. Signed
negative values and integer endpoints remain data, without implying that TPI
runtime rules permit them. A `long` overflow is an invalid integer; an otherwise
valid `long` outside `int` range throws `OverflowException`. The adapter reads the
first token, matching the existing typed SAM reader; extra numeric tokens remain
in provenance. Missing and explicit zero remain distinct.

The fixed-schema check confirms 152 unique keys in the current TPW synthetic
Standard schema, excluding dynamic loan/challenge tables. Code inspection
confirms the three optional theme-global reads listed above. This count is an
audit boundary, not proof of retail TPI schema completeness, renamed-field
equivalence, or runtime compatibility. The pre-existing 89-key claim in the
edition capability comment is not revalidated by this review.

The profile remains unused by park runtime construction. Complete supported
settings still leave `EconomyReady` false. Economy/staff capability values and
front-end/save refusals remain unchanged; no TPI campaign or staff rules are
enabled. The two environment-gated original-data tests are skipped without
`OPENTPW_TPI_PATH`, and are not counted as passing original-data evidence.

Review verification: build/typecheck succeeded with existing repository warnings;
70 synthetic/related cases passed (including all eight profile cases), and the
two original-data cases skipped with their environment variable unset. The
changed test file passed `dotnet format whitespace --verify-no-changes`; the
fidelity register check and its eleven Python tests passed. The diff check passed
with `cr-at-eol`, matching the repository formatter's C# line endings. No original
asset reads or original-code execution were attempted during this review.

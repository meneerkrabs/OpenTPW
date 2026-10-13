# TPI balance profile preparation

`TpiBalanceProfile` reads a bounded cash/admission/build-cost subset without creating
`BalanceSettings`, which requires TPW staff, research and challenge fields. It is a
settings adapter for later runtime work; it does not change `GameEditions.Capabilities`,
`Level`, `GameFlow`, weather, staff, missions, saves or campaign progression.

Every present field is a `TpiBalanceValue<T>` carrying the winning `SamEntry` with its
original key, source file, line and tokens. Missing fields remain null and appear in
`Readiness.MissingSupportedKeys`. Malformed present integer values throw instead of
receiving defaults. The existing `CellCosts` contract is available only when all five
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

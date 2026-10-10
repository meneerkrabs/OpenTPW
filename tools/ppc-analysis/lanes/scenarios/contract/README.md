# Original progression contract (Mac evidence)

This dependency-free .NET 8 helper is outside the production projects. It states the player
mode, progression, key, ticket-eligibility, mystery-item and Instant Action availability rules
recovered statically from the Feral Mac executable. Nothing in OpenTPW calls it. It does not
define phase rules, a game-flow mode, or any wiring into `ParkEconomy`, `PlayerProgress` or the
front end; the root decides that after independent review.

Run its synthetic regressions from the repository root:

```sh
dotnet run --project tools/ppc-analysis/lanes/scenarios/contract/OriginalProgressionContract.Tests.csproj --configuration Release
```

Expected result: 23 of 23 cases passed. No original assets are needed. The thresholds in the
tests are synthetic and deliberately differ from the shipped `Standard.sam`; every counter,
statistic and balance value is a caller argument. `../scenario_evidence.py` (with
`../followup_evidence.py` and `../progression_evidence.py`) validates the addresses and
operands on the identified binary.

Binary identity: Feral `SimThemePark.data`, SHA-256
`04809cd4ccee5433c7fb0b7c93d32f6a7aa629c1849181c0b7906415e5e295f5`.

| Member | Anchor | Scope and limits |
| --- | --- | --- |
| `OriginalGameType`, `OriginalFrontEndExitCode`, `OriginalWorldState` | data `0x53d98`, `*(0x84b80)+20`, world `−22728` | Three separate variables; only world state 4 is bound |
| `EarnedTickets`, `Keys`, `AvailableTickets` | `0x128b60`, `0x128a2c` | Globals/secrets once per player, locals per loaded theme; no starting key |
| `AwardAfterEarning` | `0x129cdc` | Codes 1–3; code 4 (global moved here) comes from `0x128de4`, not modelled |
| `CanEnterTheme` | `0x964c4` | Signed `cost <= keys`; Instant Action skips; theme lookup failure is the caller's |
| `TicketChecksRun` | `0xd67f0`, `0xd2f1c` | Unsigned `mGameTick % 100`; Full Simulation only |
| `LocalTicketWon`, `GlobalTicketWon`, `CameraSecretWon` | `0xd3230..0xd3640` | Strict signed `>`; happiness compares a float mean; already-earned skipping is the caller's |
| `OwnAllLandSecretAwardable` | `0xd381c`, `0x128efc` | `false`: no award path in the Mac binary |
| Cell-class helpers | `0xe6c6c`, ticket statistics | Type numbers code-proven; names only correlated with one save fixture |
| `AllResearchedAndBuilt` | `0xc5510` | Kind 4 and ticket items skipped; meaning of park record +24 not bound |
| `ChallengesActive` | `0xcfef4` | Activation only; offers, prizes and forfeits are not modelled |
| `OpenResearchGroups` | `0xf15b4` | `ResearchTech[g+1]` threshold, unsigned percent/compare, cap 7 |
| `PlacementCharge`, `TryUncoverMysteryItem` | `0xda874`, `0xd3000` | Placement ignores the purchase result; online stays free |
| `MonthlyWage`, `StaffTypeForThingClass` | `0xf46bc` | Low-32-bit product; class bytes 4–8 |
| `BankruptcyEventDue`, `AfterBankruptcyEvent` | `0xcc3d0`, `0x1059e0` | Month count is the caller's; the id gate `0x105c6c` is opaque |
| `Availability` | GameType tests listed in the doc | Online UI gates are `NotTraced` |
| `OriginalFrontEndSession` | `0x15c828`, `0x15cd38`, `0x15d074` | Synchronous dispatch; queued input to a deleted window not modelled |

Not established and therefore not modelled: PC `TP.EXE`/Patch 2 equivalence, statistic producers
(coaster height, go-kart excitement, water length, camera coverage, cumulative visitors and the
visitor history), research item selection order, the strike rules (Mac Easy Guide says strikes
were removed) and the calendar scale.

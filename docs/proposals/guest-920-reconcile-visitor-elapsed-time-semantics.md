# GUEST-920: Integer-turn visitor display age

- Classification: **approximation**, narrowed but not retired.
- Scope: existing All Visitors and individual Visitor Info duration cells only.
- Source baseline: `b12fd8625adb87382808365ec076329ab78c6726` (2026-10-20).
- Original proposal ancestry: `9db720914930f9208fbc1527cfdcfeba9b4ec106`, PR #538.
- Status: bounded implementation and synthetic regressions prepared; exact-tree independent review and remote C# CI are required before readiness. No original executable runtime test or full GUEST-920 completion is claimed.

## Evidence and mismatch

Static disassembly of the user-provided TPW `tp.exe`, SHA-256
`5472f97b5eb28f58b5a49f3a15f6f66adffa6b2625a623f503202c9696d1b805`:

| Location | Observed operation |
| --- | --- |
| `0x4FAFC5..0x4FAFDB` | Read world `+0x1DA70C`; store the current integer turn at guest `+0x1D4` during construction. |
| `0x4FD950..0x4FD966` | Subtract that stamp from the current uint32 world turn; logical shift right by 2. |
| `0x4932CC..0x4932E6` | Refresh calls the age getter, shifts its result left by 2 and converts to park hours. |
| `0x49381E..0x493834` | New-row creation converts the age getter without shifting back left. |

Only bounded static inspection was performed. No executable was run or published.
The existing import already decodes valid `mArrivalDate`; no decoder changes are needed.

Two synthetic arithmetic counterexamples, not original-game runtime observations:

1. Birth at fixed tick 75 (turn 5), read at tick 134 (turn 9): exact age 4,
   but 59 accumulated float ticks floor to 3 turns. Refreshed hours must be 4
   rather than 0; a new row must show 1 rather than 0.
2. Original world turn 1005, arrival 905, aligned first fixed tick 14955:
   59 later ticks give age 104, while the unchanged seconds fallback floors to 103.

## Bounded contract

`DisplayTimeInParkTurns` retains an age in 0..uint.MaxValue. It starts at zero at
creation, or at the existing valid imported age. Once per existing host turn
boundary it advances with unsigned wrap. A birth during the turn starts at zero;
it is not counted again by a later guest sweep. A modular age accumulator is
algebraically the native current-turn-minus-stamp subtraction within this clock
continuation, without adding another gameplay clock owner.

The 60-Hz/248-ms clock repeats every 372 fixed ticks (25 turns).
`DisplayTurnPhaseTicks` stores only that presentation offset, 0..371. Boundary
sampling uses the previous local tick modulo 372 plus the offset, then compares
that sample with sample+1. It does not modulo each endpoint independently.
`AlignTurns` establishes the following display boundaries but does not invent
historical age on arbitrary clock resets or rebinding. The existing pooled lost
park path still begins swept turns, so display age advances even while the guest
walk and seconds counter remain stopped.

Own-format snapshots retain the current age and presentation phase. Restore does
not mutate gameplay `turnPhase`; a restored HUD can show the correct age before
the host's first reattachment, and standalone continuation retains the saved
boundary phase. No schema bump is needed for optional presentation metadata.

`TimeInParkSeconds` remains the authoritative known/unknown marker. Null stays
unknown, and capture omits a stale exact cache for such a guest. Old snapshots
without exact metadata use their previous seconds-derived display fallback; no
integer provenance is invented from rounded seconds. Persisted exact age without
a known duration or phase, an age outside uint32, or a phase outside 0..371 is
rejected by both standalone and world-envelope validators before state replacement.

Existing original-import policy is unchanged: valid in-park arrival stamps seed
exact age; future stamps and on-map arrivals whose duration was previously unknown
remain unknown. The seconds import, including its half-millisecond allowance,
is unchanged for its other consumers.

## Deliberate exclusions

- No change to `TimeInParkSeconds`, stay statistics, admission/departure accounting,
  umbrellas, rain/winter gates, RNG or gameplay hashes.
- No new roster membership, ordering, selection or refresh timing. All Visitors
  keeps its 2-second refresh and quarter-valued new rows; Visitor Info keeps its
  4-second refresh and refreshed-value projection.
- No shared Level owner, UI approximation register, particle, audio or collaborator changes.
- No claim that message `0x1c`/`0x1b` senders or original roster membership have
  been established. GUEST-920 remains open for those and the seconds-based domains.
- No full-game or original-media visual/runtime oracle claim.

## Regression evidence

- `GuestDisplayAgeTests`: the live mismatch, every birth phase, swept birth, zero
  delta versus paused frames, nonzero-phase restore into fresh/used targets with
  immediate HUD access, pre/post boundaries, cycle and uint wrap, legacy/unknown
  fallback, both validators, hashes/RNG/seconds/statistics and 24/48-hour formatting.
- `OriginalGuestImportTests`: valid/future/on-map policies and the 1005/905
  counterexample, including an already-ticked empty importer.
- `LostParkTurnTests`: actual pooled Level lost-park path preserves display turns
  while seconds stay stopped.
- `VisitorInfoWindowTests`: both real HUD consumers, unchanged membership/order,
  new-row versus refresh values and the separate 2/4-second cadences.
- Existing time-in-park fixtures retain their seconds-only assertions explicitly
  as legacy-fallback cases. The umbrella/rain/winter and determinism suites remain
  required, with no weakened pins.
- `tools/test_guest_display_age_contract.py`: independently runnable integer
  arithmetic and source-integration guards; these are not C# runtime execution.

Local .NET execution is not used in this environment. Publication remains a draft
until frozen-source review and exact-head native/browser CI establish the runtime
acceptance results. There is no binary or original game-media payload in this PR.

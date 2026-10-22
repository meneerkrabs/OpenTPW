# Draft proposal: UMB-002: Resolve umbrella view-condition evidence

## Status and scope
Plan-only research/acceptance draft. Classification: evidence-only. No runtime implementation, original-parity claim or register retirement.
This ID was discovered in the U2 base of stacked PR #598 after the initial full-register audit. Inspected source: `build/rain-mood` at `c078efb0b0c3f8b97df65626e7dedd735e04a3eb`; the code is not yet on audited main.

## Observed residual
`source/OpenTPW/World/Guests/GuestSimulation.Umbrellas.cs`, `UpdateUmbrella`, `Schedule` and `CarryOut`, implements day, inside and due-time gates but no view gate. The new register describes two low-confidence original thing calls (0x40A344 / 0x405F01, callees 0x67F890 / 0x685680) that suppress both scheduling and carrying out. The description “not identified” is not an established camera-visibility contract.

## Bounded proposal
Trace the two predicates and their owning records before introducing state. Do not substitute frustum visibility, guest selection or online identification merely because the label sounds plausible. If the condition is an unavailable TPI-only subsystem, record an explicit extension policy rather than fake fidelity.

## Missing evidence
Callable identities, condition polarity, state lifetime and whether both predicates apply independently remain unproven. Static source annotations are leads, not a newly executed original-game oracle.
No original executable is run or distributed by this proposal. Do not add original media or private input metadata to the repository.

## Acceptance plan (not run)
1. Obtain a reference truth table for both predicates at scheduling and at a pending due transition.
2. Test false/true transitions while due, inside an attraction, and while weather reverses; specify whether due time is retained or cancelled from evidence.
3. If view-derived state is proven, test camera/viewport changes without nondeterministic guest ordering or hidden shared RNG consumption.
4. Keep current inside/day/due gates and balloon release behavior unchanged outside the evidenced condition; cover deterministic save/resume or explicitly transient state.

Existing regression homes on the inspected branch: `source/OpenTPW.Tests/UmbrellaRulesTests.cs`, `source/OpenTPW.Tests/UmbrellasSimulationTests.cs` and `source/OpenTPW.Tests/UmbrellasContentTests.cs`. Extend the relevant cases only after ownership and evidence gates are met.

## Ownership and dependencies
The existing umbrella owner retains U2/U3/U4 runtime work. Coordinate with `build/umbrellas-sim` and stacked [#598](https://github.com/meneerkrabs/OpenTPW/pull/598); its base must merge before it can target main. This proposal reserves no runtime files and does not duplicate rain-mood or umbrella simulation implementation.

## Completion boundary
Keep draft until the evidence/scope decision is documented, any chosen implementation is tested, and independent review accepts the exact results. A legitimate decision to retain an extension approximation must be explicit; this proposal does not itself retire the ID.

## Current-source reconciliation (2026-10-22)

This addendum supersedes the historical “not identified”/view-condition premise above. Source inspection is pinned to public main `1c874922df6e42425dc5638f7e2554c9fde2c68c`; this audit did not independently disassemble or execute the original.

### Identified predicates, not camera visibility

The current `source/OpenTPW/World/Umbrellas/UmbrellaApproximations.cs` UMB-002 entry records the two predicates as tests on the guest's kart/water track cell, indexed by `[+7] * 128 + [+5] + 1`:

- `0x67F890` (thunk `0x40A344`) follows the 72-byte tile record to its 40-byte record through the first word, and extension records through their owner to the head, then applies track predicate `0x6856D0`.
- `0x685680` (thunk `0x405F01`) reads the 40-byte record at the same cell index. The register states both predicates must hold.
- The register also identifies the draw route `0x60AE00`, which can close the umbrella through `0x6142E0`, not only scheduling and due-change execution.

These are existing public reverse-engineering records, not fresh verification in this reconciliation. Do not replace them with frustum visibility, selection or online-identification state.

### Current host behavior and evidence boundary

`GuestSimulation.Umbrellas.cs` is now on main, so the historical unmerged-U2-base dependency is no longer an accurate description of where this implementation lives. `UpdateUmbrella` uses the age/day gate and weather action; `Schedule` checks the host inside-attraction predicate; `CarryOut` checks inside and due time. None queries a track-cell predicate. The code explicitly documents this omission.

The register records 71,591 sampled Jungle guest positions, with and without kart/boat tracks, with no walking guest on a track cell; riders are represented as inside their attraction. This is bounded recorded sample evidence, not a universal reachability proof for every save, extension, malformed input or future movement path, and was not rerun here.

### Next bounded action

1. Preserve the existing omission for the supported host model while documenting it as an approximation, not a fidelity completion.
2. Review every transition capable of placing a guest on a track cell, including restored and extension states. Establish whether the condition is actually reachable before adding new guest state or callbacks.
3. If a supported reachable case is found, produce the two-predicate truth table and separate schedule, due-change and draw-close traces. Specify pending due-time retention, inside-attraction interaction and weather reversals before implementing a shared predicate.
4. If no supported reachable case exists, record that bounded policy and its evidence without claiming that a finite sample proves all states impossible.

The residual is the omitted track-cell test, not unknown camera/view state. UMB-001 delay, UMB-003 inside-state mapping and UMB-004 rendering remain separate owners. This is a documentation reconciliation only: no runtime change, new native evidence, tests-run claim, identifier retirement or full UMB-002 acceptance. Keep draft until the scoped policy/evidence and review gates are met.


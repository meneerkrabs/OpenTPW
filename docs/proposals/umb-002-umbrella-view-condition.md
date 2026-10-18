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

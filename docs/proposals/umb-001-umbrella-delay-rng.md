# Draft proposal: UMB-001: Decide umbrella delay RNG compatibility

## Status and scope
Plan-only research/acceptance draft. Classification: extension-policy decision / outside original TPW parity. This item remains included in the user’s full approximation coverage request. No runtime implementation, original-parity claim or register retirement.
This ID was discovered in the U2 base of stacked PR #598 after the initial full-register audit. Inspected source: `build/rain-mood` at `c078efb0b0c3f8b97df65626e7dedd735e04a3eb`; the code is not yet on audited main.

## Observed residual
`source/OpenTPW/World/Umbrellas/UmbrellaRules.cs`, `Delay` (around line 193), uses SplitMix64 of guest ID and park turn modulo the configured bound. It already preserves the balloon-life delay arithmetic and takes no shared world/guest RNG draw. The register contrasts this with TPI CRT rand() at 0x870D38, called by 0x613EA0. The same inputs therefore deliberately yield one deterministic delay rather than the original hidden CRT sequence.

## Bounded proposal
Adjudicate whether the TPW umbrella extension should keep this deterministic policy or offer a separately evidenced compatibility mode. Do not silently replace it or consume the shared simulation RNG. Establish original CRT seed, algorithm, consumption order and save-state ownership before designing compatibility behavior.

## Missing evidence
Original CRT state/seed lifetime and other consumers are unavailable in the supplied DATA-only corpus. A modulo bound and nominal rand() name cannot establish sequence parity.
No original executable is run or distributed by this proposal. Do not add original media or private input metadata to the repository.

## Acceptance plan (not run)
1. Pin repeatability for equal IDs/turns and the existing delay range, including slowest <= 0.
2. Test balloon life at zero, boundaries and full scale, and discardPercent zero; preserve the already-traced arithmetic.
3. If compatibility is explicitly chosen, compare an independently evidenced multi-consumer sequence and save/resume continuation, without changing purchase colour or unrelated world/guest RNG draws.
4. Keep existing extension-off hashes and current deterministic-mode hashes; any intentional variant must be separately versioned and reviewed.

Existing regression homes on the inspected branch: `source/OpenTPW.Tests/UmbrellaRulesTests.cs`, `source/OpenTPW.Tests/UmbrellasSimulationTests.cs` and `source/OpenTPW.Tests/UmbrellasContentTests.cs`. Extend the relevant cases only after ownership and evidence gates are met.

## Ownership and dependencies
The existing umbrella owner retains U2/U3/U4 runtime work. Coordinate with `build/umbrellas-sim` and stacked [#598](https://github.com/meneerkrabs/OpenTPW/pull/598); its base must merge before it can target main. This proposal reserves no runtime files and does not duplicate rain-mood or umbrella simulation implementation.

## Completion boundary
Keep draft until the evidence/scope decision is documented, any chosen implementation is tested, and independent review accepts the exact results. A legitimate decision to retain an extension approximation must be explicit; this proposal does not itself retire the ID.

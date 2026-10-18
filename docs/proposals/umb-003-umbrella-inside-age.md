# Draft proposal: UMB-003: Qualify umbrella inside and age proxies

## Status and scope
Plan-only research/acceptance draft. Classification: approximation. No runtime implementation, original-parity claim or register retirement.
This ID was discovered in the U2 base of stacked PR #598 after the initial full-register audit. Inspected source: `build/rain-mood` at `c078efb0b0c3f8b97df65626e7dedd735e04a3eb`; the code is not yet on audited main.

## Observed residual
`source/OpenTPW/World/Guests/GuestSimulation.Umbrellas.cs`, `IsInsideAttraction`, maps Using, Swimming, ClimbingOut, ChangingOut and PoolActivity to inside; `AgeTurnsOf` floors TimeInParkSeconds over ParkCalendar.TurnMilliseconds, or returns zero when unknown. The register contrasts TPI words +0x38/+0x3C and integer world-turn age, with medium-confidence attraction classification and calendar scaling. Imported arrival timestamp support already exists; do not claim all imported ages are missing.

## Bounded proposal
Create an attraction/state matrix and a separately bounded age/day-gate qualification. Coordinate with GUEST-920 rather than invent another arrival timestamp. Preserve the extension’s pending-change save policy. Only narrow or widen inside states after proving the original flags for the relevant attraction categories.

## Missing evidence
The exact flag-0x20 attraction classes, transitions of both inside words, clock-scale semantics and unknown/imported-age policy need evidence. TPI behavior is not automatically a TPW requirement.
No original executable is run or distributed by this proposal. Do not add original media or private input metadata to the repository.

## Acceptance plan (not run)
1. Compare ride, shop, changing-room entry/exit, swim activity and queue/walk states against independently evidenced inside-word transitions.
2. Exercise both Schedule and CarryOut across inside/outside transitions and due times, preserving balloon release and pending-state behavior.
3. Test age exactly before/at/after a park-turn and qualifying-day boundary, unknown age, imported known age and save/resume.
4. Resolve age/calendar ownership with GUEST-920 and rain-mood #598; extension-off behavior and existing non-umbrella hashes must remain unchanged.

Existing regression homes on the inspected branch: `source/OpenTPW.Tests/UmbrellaRulesTests.cs`, `source/OpenTPW.Tests/UmbrellasSimulationTests.cs` and `source/OpenTPW.Tests/UmbrellasContentTests.cs`. Extend the relevant cases only after ownership and evidence gates are met.

## Ownership and dependencies
The existing umbrella owner retains U2/U3/U4 runtime work. Coordinate with `build/umbrellas-sim` and stacked [#598](https://github.com/meneerkrabs/OpenTPW/pull/598); its base must merge before it can target main. This proposal reserves no runtime files and does not duplicate rain-mood or umbrella simulation implementation.

## Completion boundary
Keep draft until the evidence/scope decision is documented, any chosen implementation is tested, and independent review accepts the exact results. A legitimate decision to retain an extension approximation must be explicit; this proposal does not itself retire the ID.

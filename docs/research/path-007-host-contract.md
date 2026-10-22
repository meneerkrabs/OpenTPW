# PATH-007: synthetic host regression boundary

Related proposal: https://github.com/meneerkrabs/OpenTPW/pull/576

Source snapshot: `dcdf4d0c53723e7b7df9c7c7a5ae3834c4f7130b` (2026-10-22).
`ParkLand.cs` blob: `e3476ddf028080bebc0cf56f53b00e0208aefc3e`.

## What this establishes

The new `ParkLandEdgeCaseTests` pins the existing host approximation, not native fidelity. It adds six synthetic cases across four methods:

- A south-east cut-out rectangle is owned at x=10..25/y=12..17 and x=10..17/y=18..21, plus (18,18). That is 129 owned cells. The 18x18 clipped map retains 48 of those cells. These counts exclude an intentionally pre-owned outside cell (0,0).
- A U-shaped ring owns its top rectangle x=10..25/y=12..15, legs x=10..13 and x=22..25/y=16..21, and concave extras (14,16) and (21,16): 114 cells. The central open notch remains unowned.
- Filling clears only `Unowned`, preserves existing ownership outside the ring and unrelated flags, leaves queue type/link/owner/placement data and the type-change stamp intact, and produces no writes when repeated.
- An imported four-post cycle disconnected from post B returns null within `MaximumPosts` link reads. The callback itself fails past that bound, so a guard regression cannot hang the test. An implementation that detects a repeated state sooner is allowed.
- A synthetic gate-crossing purchase (15,7)-(19,15), in both corner orders, returns `Gate`, zero cells and cost, with unchanged balance, cell flags, map version and land identity. This protects the deliberate host safeguard rather than declaring the original's acceptance erroneous.

All coordinates and maps are synthetic. No game media, saves, executable bytes or private disassembly are included. `ClaimEnclosedCells` is tested directly: its contract is monotonic ownership, unlike initialization's initial unowned pass. No change is made to runtime code or to the PATH-007 annotation.

## Verification status

Authored C# tests have not been compiled or executed in this task. Local .NET execution was not attempted because it remains outside this task's permitted execution route. Public CI must compile and run `ParkLandEdgeCaseTests` (six cases) and the existing `ParkLandTests` before acceptance.

A separate Python fixture calculation checked 8,516 map coordinates using orthogonal polygon crossings and the documented concave-corner rule, plus 4,096 synthetic cycle steps. It matched the above expected coordinates/counts. This validates fixture arithmetic only, not C# behavior, command execution, or original-game parity. The existing C# suite already covers a single concave corner, simple broken links and a gate-validator refusal; the new assertions cover full maps, mutation boundaries and cycles.

## Smallest native evidence still needed

There are three distinct unresolved questions, not one blanket implementation blocker:

1. **Fill scan (0x5311c0).** For each synthetic L/U ring, supply the ordered posts, entering heading, marked-post set and initial ownership flags, then an ordered trace of visited 2x2 blocks and resulting ownership bitmap. Include both concave corners and the cell immediately beyond each. Compare the full bitmap with the host output; matching one imported Jungle save is not proof for arbitrary rings. Record any clipping/bounds assumptions separately.
2. **Gate-crossing purchase.** For the proposal's actual Jungle rectangle (45,11)-(49,17), record the pre-purchase fence links/flags and balance, validator verdict, charged amount, ring walk states `(post, heading)`, retained/removed hoardings and post-purchase ownership delta. A trace bounded by repeated `(post, heading)` detects a suspected cycle without claiming it terminates. A stopped trace cannot establish the final charged/no-land outcome. The synthetic gate test above is not this Jungle experiment.
3. **Six-click mode.** Trace the meaning and lifetime of level-word bit 0x400 at 0x79d86c and record the exact six generated offsets, coordinate anchor and entry/exit conditions at fence setup 0x5323f0. Six authored clicks in an ordinary synthetic rectangle do not exercise this native mode.

Until those observations or equivalent independently reviewed static proofs exist, retain the gate/ring safeguards, existing fill approximation and draft status of #576. Passing the new host tests does not retire PATH-007 or make the research proposal merge-ready.

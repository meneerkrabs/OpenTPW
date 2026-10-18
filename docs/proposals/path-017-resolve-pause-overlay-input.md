# PATH-017: Resolve pause overlay input

## Status and classification
- Classification: evidence-only.
- Plan-only local proposal: no runtime implementation, annotation retirement, publication or merge performed.
- Snapshot supplied as main 5ccddcab (export has no .git; commit not independently verified).
- Existing PR: none mapped for this independent residual.
- Draft eligibility: evidence/scope gates below must remain visible; this is not a completed feature.

## Current residual
PATH-PAUSE-WINDOW: the pause key P (tp.exe 0x40bf70 → 0x409350(1)) toggles a window (the global handle 0x7cb2cc, 0x4a27f0 → 0x47ed80): a full-screen layer (0x65dd7b(1, 1, 1, 0, 0, 0x7ff, 0x5ff), the 'lolight' backdrop) under the panel 5094 (174,500)-(1874,900) of table 0x752e00; whether that layer stops the click route to the park click handler 0x4879d0 (which reads neither the pause flag 0x786b84 nor this handle, only the pause-menu object 0x7c2534 through 0x48c8d0) is not read, so OpenTPW's speed pause keeps the tool live as the click handler does (evidence needed: the window manager's hit test of the 0x65dd7b layer classes)

## Source inspection
- `source/OpenTPW/Hud/ParkHud.cs:966`: 968: if ( paused || level.BuildEntry != null || level.IsPlacing || level.IsRemovingObjects || level.IsCoasterToolActive; 969: || level.PatrolTool is { IsActive: true } || level.StaffPlacement is { IsActive: true } || level.BuyLandTool is { IsActive: true }; 970: || level.AreaClearTool is { IsActive: true } || level.PoolTool is { IsActive: true } ); 971: return false;

## Bounded work
Determine full-screen lolight layer hit testing before changing tool-live speed-pause behavior.
- Preserve already-implemented behavior described above; restrict future changes to the named residual.
- For evidence-only items, establish an oracle first and file implementation only if a behavioral difference is proven.

## Missing evidence
Decode window manager hit test/class of 0x65dd7b and dispatch to 0x4879d0.
- Source annotations record prior reverse engineering; this audit checked adjacent implementation but did not execute the original.

## Acceptance cases
1. Press P with armed path tool and click overlay; compare event delivery and map mutation to original.
2. Unpause leaves tool selection and cursor consistent; Escape menu behavior remains separately tested.
3. Add a reproducible synthetic fixture or reference trace identifying input, expected output, and affected function; a passing build alone is insufficient.
4. Re-run focused regression tests for the listed source owner and document what remains unproven.

## Dependencies and ownership
- World/audio implementation owner to be assigned; no runtime ownership claimed.
- Original-executable or reference-capture evidence is unavailable in this read-only source audit; unresolved claims are hypotheses.

## Review boundary
- Stop at a reviewable design/evidence package; this draft authorizes no tag removal or parity claim.
- Do not publish original media or private game data in fixtures, screenshots, or commits.
- A future implementation needs its own reviewed evidence and acceptance results; do not treat this proposal as merge-ready.

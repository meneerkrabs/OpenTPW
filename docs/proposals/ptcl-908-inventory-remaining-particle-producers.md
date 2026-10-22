# PTCL-908: Inventory remaining particle producers

## Status and classification
- Classification: approximation.
- Plan-only local proposal: no runtime implementation, annotation retirement, publication or merge performed.
- Snapshot supplied as main 5ccddcab (export has no .git; commit not independently verified).
- Existing PR: none mapped for this independent residual.
- Draft eligibility: evidence/scope gates below must remain visible; this is not a completed feature.

## Current residual
Wired: EVENT 1/2, ADDOBJ and ADDOBJ_EXT 1/2 (tp.exe 0x551F5F, 0x5520E8 with its control operand, 0x5522C5) whose kept effects follow their key node on every object pass in whole units (0x55189D, 0x51FE30, 0x51FD90 for type 2), a negative key at the footprint box middle (0x556B90/0x466B70), the stinkbomb litter's effect 50 (0x4D95BD, 0x4D9620), the per-type effects of the category .sam (build 0x558e7c, demolish 0x5590f7, repair 0x556DE0, upgrade 0x55A100; the removal's mode 2 of 0x4dd0a0 is the world's clear and reset loops 0x515dd0 and 0x515fb0, which OpenTPW's teardown does not run through the player's removal), the land effects of scape.omp (0x550e00 type 2: Jungle's waterfall, effect 20), every bump vehicle's rider beam 19 at the BUMP 12 load and the bumper session's unload (0x549C60 and 0x54AC70 with the flag; OpenTPW raises it at the vehicle's simulation position, the original at its instance's root translation, and checks the seat's head dummy where 0x54AC70 checks that the head is still hung), the guests' teleport beam 19 (0x501db0 states 15 and 16 with UsageInfo.RequiresTeleport, position 0x4fa470) and, through the vehicle and tour code, the break smoke 2 (0x544C80), the boats' spray 96 (0x547F50), the retire beam 15 (0x54AE50) and a kart's first-draw beam 15, the walk beam 19 (0x5572EB, 0x557f0e) and the flyers' smoke and fire 39 and 40 (0x55DA70). Not wired, none reached by shipped data unless said: a type-2 effect with a negative key (0x556B90 leaves its direction unset, the dispatcher passes uninitialised stack; the only shipped use is Fantasy's bugstv ADDOBJ effect 16), an object without the footprint box (0x559060 would create its demolish effect anyway at unset coordinates; 0x413c10 requests the box for every type (model flag 0x400, loaded by 0x451640, so a missing box file is not read), and no shipped fixed item names a type effect or a negative key), a scoped effect (bit 0x8000: the type record's slot +0x4E0 + 4n, 0x4145D0, template ids 0x414420 loads from the type's archive; no shipped script names one literally, Jungle's porkpie EVENT takes its effect from a variable), the scoped message after the build effect (0x51C640; the upgrade's 0x51BFC0 is its sound, wired), the beam when a guest is created (0x4faec0, taken when 0x4fa990 finds none of five cell types), the staff members' beams (0x505790, 0x500bd0) and the online avatars (0x4f7c60), the first-draw beam 15 of the other vehicles (0x549DB0 sets flag 0x4000000 for every vehicle it creates, 0x54a009; its only reader is 0x546a89 in the per-vehicle draw 0x546280, which 0x519060 calls for every type-3 object with a controller, and no family check was found; OpenTPW raises it for karts only), the lobby's key-panel effect 0x61 (0x4b8ee0, 0x4b90f0, 0x4b9340; OpenTPW has no island lobby key panel, ADVISOR-025), the UI pool groups (0x5ED5A0 and its callers) and the message-tag effects 90 and 91 of the region 0x48DE00-0x48E0C0 that Ghidra left unanalysed (0x48de1a effect 0x5b kept in control property 9 and moved by 0x51fe30; 0x48dfbe -> 0x486b90 effect 0x5a at a control's centre)

## Source inspection
- `source/OpenTPW/World/Objects/OriginalObjectRuntime.ObjectEffects.cs:43`: 45: internal int StartObjectEffect( ObjectParticleEffect effect ); 46: {; 47: var id = ParticleEffectId( effect );; 48: if ( stopped || id <= 0 || ParticleStarter is not { } starter || particleFootprint?.Invoke() is not { } footprint )
- `source/OpenTPW/World/Objects/OriginalObjectRuntime.ParticleRequests.cs:90`: 91: internal void ObserveParticleEvent(RideEffectCall call); 92: {; 93: if (stopped || call.Opcode is not (Opcode.EVENT or Opcode.EVENT_EXT) || call.Argument(0) is not (1 or 2)) return;; 95: var control = call.Opcode == Opcode.EVENT_EXT && call.OperandCount > 3 ? Word(call, 3) : 1000;

## Bounded work
Reconcile merged beam fixes then plan reachable missing producers; undefined stack-direction cases require evidence, not invented values.
- Preserve already-implemented behavior described above; restrict future changes to the named residual.
- For evidence-only items, establish an oracle first and file implementation only if a behavioral difference is proven.

## Missing evidence
Check #425/#435 current head, scoped template lookup, UI groups and guest/staff creation paths.
- Source annotations record prior reverse engineering; this audit checked adjacent implementation but did not execute the original.

## Acceptance cases
1. First draw of kart, boat and bumper emits beam 15 once, never again on later redraw.
2. Exercise guest creation fallback, staff and scoped effects with explicit producer/position/handle oracle; mark unreachable shipped cases separately.
3. Add a reproducible synthetic fixture or reference trace identifying input, expected output, and affected function; a passing build alone is insufficient.
4. Re-run focused regression tests for the listed source owner and document what remains unproven.

## Dependencies and ownership
- Claude currently owns particle/tour runtime. This document is planning only; agree file boundaries before runtime work.
- Original-executable or reference-capture evidence is unavailable in this read-only source audit; unresolved claims are hypotheses.
- Coordinate with Claude, reconcile merged #425 and #435 before any runtime work.

## Review boundary
- Stop at a reviewable design/evidence package; this draft authorizes no tag removal or parity claim.
- Do not publish original media or private game data in fixtures, screenshots, or commits.
- A future implementation needs its own reviewed evidence and acceptance results; do not treat this proposal as merge-ready.

## 2026-10-22 public-source reconciliation

Audit snapshot: main `f4be81cbbe120d1638cfb0355a701bdac3d59f1e`; proposal head
`d44be284c03017ba8a5366a6ab7a854772d508d5`. This addendum refines the
historical residual above; it is not a native capture, a test execution report,
or permission to alter another owner's runtime.

### What is already present, and what it proves

| Producer / boundary | Inspected host evidence | Remaining distinction |
| --- | --- | --- |
| Land waterfall | `Level.PlacedParticles.StartPlacedParticles` filters scape records to type 2. `LandParticleEffectTests` covers record selection, refused effects, Jungle waterfall and other levels. | Several tests require shipped templates/assets. Their presence is not a fresh run or visual validation. |
| Ride teleport beam 19 | `Level.PlacedParticles.StartGuestBeam` uses `GuestBeamPlace` and the heightfield. `GuestSimulation` exposes the ride teleport event. | This is not the unrelated guest-creation fallback at 0x4faec0. #435 explicitly left the full GameFlow-to-Level hookup test undone; this audit does not close it. |
| Vehicle rider beam 19 | `BumpController.LoadWaiting` calls `RaiseRiderBeams`; the helper uses host-supplied seat-head flags and refuses unknown seats. Bumper unloading calls the same helper. | Existing host position is simulation position, not evidence that the native instance-root position and head-hung lifetime agree. |
| Vehicle beam 15 | `BumpController.Launch` raises it only under `Template.IsKart`, after `Relaunch` and setting `Launched` (lines 647–694). Retire paths also raise it. | The current kart hook is at **launch**, not at first draw. The earlier phrase “kart's first-draw beam” must not be interpreted as proof of draw-time semantics. Boats/bumper creation and first-draw timing remain separate requirements. |
| Script and scoped effects | `OriginalObjectRuntime.ParticleRequests.PrepareParticle` explicitly refuses bit 0x8000 as `UnsupportedScopedEffect`, IDs outside 0..104, and directed negative keys. `ParticleEventRequestTests.RefusalsHaveNoPoseAndReplacePreviousAttempt` and `ANegativeKeyStartsTheEffectAtTheFootprintMiddleAndATypeTwoOneStartsNothing` encode these boundaries. | Do not invent a direction for the undefined native directed-negative-key case or treat explicit refusal as implemented scoped-template lookup. |
| Footprint-less paths | A type-1 negative-key script request with an installed footprint provider returning null prepares a world-origin request; an absent provider is refused. `StartObjectEffect` still requires a non-null footprint. | Script negative-key fallback and category build/demolish/repair/upgrade effects are different code paths. One does not close the other's missing-box residual. |
| Tour smoke/fire | `OnTourEffect` owns emitter handles by car slot, kills replacement/stop handles and moves held emitters. `TourEffectTests` contains condition/repair ordering, draw-follow, save and randomness cases. | Authored tests are host evidence only; no new tour/native execution was performed. |
| Lobby screen effects | `LobbyEnterEffectTests.InstantActionStartsNothingAndTheOtherModeStartsEffect0x62Once` and its menu test cover entry effect **0x62**. `GameFlow` starts that screen renderer/effect. | This does not implement the key-panel **0x61** residual, generic UI pool groups, or message-tag effects 90/91. Do not group these IDs together as one completed lobby feature. |

Merged dependencies were rechecked through GitHub: [#425](https://github.com/meneerkrabs/OpenTPW/pull/425)
merged as `3c1a090a8c186bbf437b0b8269633b5745d46fce`;
[#435](https://github.com/meneerkrabs/OpenTPW/pull/435) merged as
`5ccddcab56ffb9bda4ded1821988a8492bee8943`. Their reported historical test
results are not results from this audit.

### Next bounded implementation gates

1. Agree the vehicle runtime boundary with its existing owner before changing it.
   Capture/supply a producer trace for successful creation without a draw, first
   draw, repeated draw, retirement before first draw and save/restore for each
   kart/boat/bumper family. Assert emission ID, count, position and ordering.
   Merely removing `Template.IsKart` would broaden a launch-time hook, not implement
   the documented native first-draw flag.
2. Guest-creation fallback, staff and online avatar beams need their own triggering
   state and position/handle oracle; the ride teleport test is not a substitute.
   These remain documented residuals, not newly demonstrated failures.
3. Scoped effects need archive-name/type-slot resolution and handle lifetime
   evidence before allowing 0x8000 requests. The shipped-script census distinguishes
   literal operands from Jungle porkpie's variable operand; no literal use does
   not prove the variable cannot become scoped.
4. Keep lobby 0x61, entry 0x62, generic UI groups and message tags 90/91 as separate
   cases with owner/control lifetime and coordinate-space evidence.

Validation in this audit: public source and merge identities inspected, exact
source blobs recorded in the review package, existing test names/conditions
cross-checked. No C# compilation, CI rerun, browser, GPU or original executable
execution. No runtime files changed; PTCL-908 and #581 remain open/draft.

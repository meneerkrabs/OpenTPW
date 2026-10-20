# HEADS-007: persistent visitor body animation

## Bounded implementation status

Replace the bounce body's elapsed-time approximation with persistent visitor-owned sprite program state. Implementation and tests are in progress; exact-head CI and final integrated review remain pending. HEADS-007 stays open for the explicit host-input, clock, import and unsupported-control boundaries below. This document does not claim original rendered equivalence.

## Corrected evidence

The original run template at `0x74DBC0` emits frames 0 through 7, not 0 through 4. Its FRAME handler is `0x476940`. A fresh sprite has interval 62 milliseconds, but replacing the old divisor with 62 would still be incorrect.

The scheduler at `0x475360` tests signed `now > due`, emits at most one frame and assigns `due = now + interval`. It has no catch-up loop. The observed static main-loop caller at `0x54F5FB` runs on even substep opportunities before the guest turn. Its timer sample is independent of the logical substep timestamp. Renderer visibility and bounce-table membership do not control that scheduler.

The body wrapper keeps its player, pending template and pending interval separately. Guest begin-update applies pending changes; template application retains due, interval and previously emitted output. A fresh player has no visible output before its first FRAME. Finite guest programs jump to stand while retaining their active/requested identity. A repeated matching request need not replay a finite program.

All four shipped bounce rides retain the visitor's pre-existing body at boarding through `UsageInfo.RideHandlesSprite`. BOUNCE insertion does not initialize the body clock. The shared body request checks active and pending templates; walking checks active only; ordinary state entry queues unconditionally. Those distinctions are part of the contract.

## Implementation scope

- A GPU-free player/wrapper with explicit current program, active identity, next-frame cursor, due/interval, nullable emitted output and pending requests.
- Shared guest ownership from creation, observing semantic state entry, existing integer navigation results, activities, guard capture/release, admission/release and removal. No extra navigation or random draw.
- Copied host appearance category at actual body creation; a retained body does not change category merely because the person's costume changes.
- Read-only bridge metadata for inherited RideHandlesSprite, rather than guessing from teleport or ride membership.
- One scheduler opportunity in the host's even-substep phase; pooled guest-turn ordering follows the established scheduler-before-sweep contract.
- One shared post-batch body-request/pose phase. Renderers read cached output and cannot double-step, restart or create body state by drawing.
- Explicit scripted hidden-body events contribute to shared history. Ordinary walking and scripted WALK visual frame selection remain their existing behavior in this slice.
- Optional saved presentation state with validated cursor/output/template bounds, an explicit epoch and older/native-save reconstruction qualification. New presentation fields do not alter canonical gameplay or RNG digests.

## Host boundaries that remain approximations

The host retains its established fixed-60Hz clock and supplies an explicitly defined integer millisecond sample. This differs from the original independently sampled timer during catch-up. Non-pooled host update ordering stays unchanged and must not be described as the original thing sweep. Pose requests are once per host advance batch; pause/zero-delta handling and headless behavior need concrete integration tests.

Current simulation events are the inputs. In particular, current queue move-up lacks the original ToiletBonus reset, and current queue interludes do not fully encode the original saved activity state. This presentation work does not silently change speed, AI or those gameplay histories. Existing host appearance variation differs from original bank selection; copied category does not repair that independent approximation.

Older saves and imported original saves do not supply the connected body history. Reconstructing a body at load is a named fallback, never a claim that bounce insertion recovers original phase. The parsed original sprite records would require a separately validated wrapper/slot/program/clock import path.

Swimming and generic teleport/control callbacks remain explicit extension or unsupported-history boundaries. Normal walker/staff rendering, staff scheduling, camera policy, ride geometry and simulation mechanics are outside this slice. No whole-ID retirement follows from implementing the bounded owner.

## Acceptance requirements

1. Strict signed due/equality/wrap behavior, one frame per eligible opportunity, frames 0–7, no visibility before first FRAME and no catch-up.
2. Pending application preserves output, due and interval; active-only, active-or-pending and unconditional request paths remain distinct.
3. Finite program completion retains active identity; snapshot round trips preserve current segment, cursor and old output after a new template is applied.
4. Navigation is stepped once; only Walking results queue interval/template changes; Arrived/Stuck do not. Canonical gameplay/RNG controls remain unchanged.
5. Admission retain/delete, hidden WALK actions, missing nodes, expiration, guard capture/release, attraction removal, ordinary release and identity reuse follow distinct events.
6. Retained costume change versus absent-body recreation selects exactly one kid/costume consumer using the appropriate copied category.
7. Zero, one or two render passes cannot change state. Hidden renderers retain scheduler progression. Pause suppresses scheduler and pose mutation; lost parks continue eligible sprite scheduling without applying skipped guest turns.
8. Shared post-batch order resolves explicit hides after earlier poses; unknown actions or missing nodes must not invent deletion.
9. New saves validate and restore all presentation fields and epoch, including an absent player with pending requests. Older/native saves use the documented fallback. Existing gameplay fields still affect canonical hashes while only the new presentation state is excluded.
10. Independent integrated source/test review and full exact-head code CI precede readiness and merge. Synthetic tests are not original runtime captures.

## Source anchors and ownership

Relevant implementation: `World/Guests/VisitorSpriteState.cs`, `GuestSimulation.SpriteState.cs`, semantic hooks in GuestSimulation and ArrivalWalk, `BounceRiders.cs`, `GuestRenderer.cs`, `Level.Simulation.cs`, and guest snapshot capture/restore. Original anchors: creation `0x4758F0`, request `0x4F9E60`, application `0x4D4190`, state entry `0x501DB0`, walking `0x4FA2A0`, destruction `0x4D4170`, and walker body events `0x5580A0`.

Winter guest/save fields and renderer additions remain their owners' work and must be preserved on integration. Private original files, metadata, dumps and resources are excluded from publication. Review and test outcomes belong in the PR/progress record once verified, not inferred from this proposal.

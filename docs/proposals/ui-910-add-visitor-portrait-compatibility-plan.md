# UI-910: Ordinary visitor live portrait and remaining clone fidelity

## Status and scope

- The arrow-order correction remains in place. The next bounded implementation observes ordinary registered kid/costume world body output in portrait frame `0x3722`; publication and exact-head CI are pending.
- UI-910 stays open for independent native sprite-clone timing, source-loss epochs and visual/lifecycle residuals. Ride, swimmer and specialized chooser sources are excluded.
- Statistics cadence, arrows, gauge, camera, sound, keyboard, save format and simulation behavior are unchanged.

## Evidence and implementation

The visitor-window constructor sets kind -2. Handler `0x4b7720` routes down/next (`0x3721`) to `0x48caf0` and up/previous (`0x3720`) to `0x48cbe0`. Kind -2 selects mask `0x40`; `0x483740` and `0x483770` traverse type-1 things with `0x4824d0` and `0x482da0`, retrying from the used-chain head or tail at the boundary. The visitor's pre-hook at `0x5da3c0` does nothing, so there is no additional state, visibility or camera-eligibility filter. Only a changed, non-null actor invokes the selection refresh at `0x4b7660`.

The info window now uses `LocatorOrder.VisitorIds` directly. This exposes the existing locator projection without changing its algorithm: first positive-ID roster claims win, including Gone claims; matching live Guest slots follow the used chain; queued, placeholder, wrong-model and missing-roster slots are excluded. Active pool-free first claims append in ascending ID order as an explicit host-only compatibility fallback. Rejected used slots cannot reenter through that fallback. A `ThingPool.Hold` placeholder is different from a live visitor held by a guard.

`VisitorInfoRules.Cycle` preserves that sequence instead of sorting numeric IDs. For the nonmonotonic used chain `[2 Guest, 90 Attraction, 9 Guest, 14 Handyman, 7 Guest]`, next visits `2 → 9 → 7 → 2` and previous visits `2 → 7 → 9 → 2`. Reusing a low-number pool slot makes it the newest visitor regardless of ID magnitude.

The info window keeps its own selection-loss policy: missing/Gone current visitors refuse navigation, then the next UI update closes the window. It does not adopt locator stale-selection recovery. The helper's absent-current head/tail endpoints are deterministic host-fixture behavior, not a claim about original pointer-lifecycle timing. A fabricated rejected pool slot with a lingering active roster selection is outside this bounded lifecycle change. Singleton arrows do not refresh aged stats.

The destruction annotation is also corrected without a behavior change: the request sets the destroyed flag, enqueues at `0x50b8fd`, then sets type 2 at `0x50b906` before returning. It does not set type 2 before enqueueing.

## Verification boundary

Private static research established 33 byte/call/table checks and 24 derived contract checks. Those results support the bounded ordering contract; they are not production C# execution or original-runtime visual proof. No original executable, disassembly, assets, private paths or metadata are included here.

`VisitorInfoArrowOrderTests` adds synthetic real-button coverage for both directions and wrap, immediate title/counter/thought refresh, hidden/riding/guard-held/queueing states, strict candidate exclusions and duplicate first claims, missing/Gone selection and stale callbacks, explicit reopen, low-slot reuse and exact pool restore, singleton timer behavior, and snapshot/roster/RNG/hash nonmutation. Existing rule tests now expect supplied order rather than numeric-ID chronology. Existing locator and employee behavior remains unchanged.

The bounded Python source guards are wired into the full build workflow. The .NET SDK is unavailable in the local implementation environment, so no local C# compilation, unit-test execution, rendered screenshot or full application pass is claimed. Exact-head CI must run the complete C# suite, including visitor, locator, employee, pool and resume regressions. Python checks and aggregate evidence validation are reported separately in the implementation's review summary.

## Bounded ordinary-body portrait

Static analysis establishes that the original portrait clones the selected visitor's existing body source. The control's signed depth field plus two supplies UI depth, not a figure ID. The raw float 12.0 is primary scale, not playback speed. The visitor's full refresh creates/replaces the clone; UI live-copy messages refresh selected source fields, while the four-second timer refreshes statistics. The source clone is independently scheduled and has interpreter state the current host guest model cannot reproduce completely.

`GuestRenderer.PrepareWorld` is the actual world's CPU selection/quad path. Existing consumer gates, pose collection, frame selection, quad order and GPU submission remain in their original order. Only a successful ordinary quad can publish a value-only slot/step/heading record. Ride poses and presentation-only `Figures` never publish visitor portraits. A publication records exact guest, simulation, level and renderer identity, world/renderer passes, asset generation, atlas identity and weak-key resource identity. Texture reload invalidates before replacing its first slot; deletion clears publications before queued disposal. Publications hold no GPU handle or UI frame.

`VisitorFrame` is observational. It requires a current registered kids/costumes renderer, current simulation and exact roster member, ordinary source category, unchanged pass/generation/atlas/resource and a live texture. Extension slot/feet/depth/direction/rotation paths and all swim/ride bodies are excluded. It uses the captured animation slot/step and heading at literal yaw zero, including limited-direction mirroring. It never calls consumer/chooser delegates, pose collection, a sprite player, the simulation or shared RNG. No first publication or unavailable source means an empty portrait, with no frozen or invented clone. A new valid world publication can become visible in an already open window; that is the declared host policy.

The HUD keeps its existing public ID-based info selection behavior. Its portrait independently captures an exact Guest, simulation and monotonically replaced selection-session token on open/changed arrows; close or any stack removal invalidates the token. Retained old callbacks cannot revive after close/reopen or cycling, even for the same Guest. A current numeric-ID replacement does not inherit the prior portrait selection. A lower window can draw under a modal without relying on its `Update` call. Singleton arrows preserve the statistics age and existing callback.

## Nominal geometry

The portrait frame model keeps the outer rectangle `(456,267)..(720,531)`. The sprite helper uses the control's explicit inner rectangle `(477,281)..(704,510)`. For signed integer coordinates, the visitor-specific feet are `left + ((right-left) >> 1) + 8`, `bottom - ((bottom-top) >> 2) - 3`: the odd 227 by 229 rectangle therefore yields `(598,450)`, not floating `(598.5,449.75)` or outer-based `(596,462)`. This arithmetic happens in original canvas units before host viewport scaling/anchor translation.

The downstream screen-space path applies explicit scale `12 × 0.05`, with display aspect on X and frame pixels-per-unit A/B. Ideal 2048 by 1536 conversion gives the visitor-only nominal factor 3.6, multiplied by the existing `128/A` and `128/B` frame factors. Shared `UiSpriteFigure` defaults remain 3.2 and retain their original rectangle-relative feet. UI-082 is unchanged. The original viewport uses subpixel offsets/scales, and conditional rotations and depth have separate semantics, so this is not exact native framebuffer or raster parity.

## Tests and remaining work

`VisitorLivePortraitTests` uses synthetic sprite banks, managed lifetime sentinels, actual CPU world preparation and the actual HUD callback/Draw. Coverage includes PeepTypes and ID-derived variants, costumes, walk/wait/stand phases, yaw-zero direction/mirroring, identity/session reuse, hidden/riding/swimming/specialized sources, omitted renderer passes, replacement/reload/deletion, malformed data, observational snapshot/hash/RNG/body-state behavior, real arrows/close/reopen, unchanged statistics cadence and nominal geometry across viewports/non-128 A/B. These are CPU contracts, not GPU or original-media visual comparisons.

The source-integration guards run alongside existing arrow, staff portrait, locator and full C# regression suites; normal CI scope is not weakened. The local implementation environment has no .NET SDK, so compilation and focused/full C# execution require exact-head CI. Private original-data visual comparison is still needed for visual claims; original software was not executed and no original media, private paths, hashes or disassembly are published.

Full parity still requires an independently scheduled sprite-VM clone, its copied interpreter/control state and partial refresh semantics, emitted visibility/opacity/flags, exact source creation/loss/return epochs, and screen-space rotation/depth/viewport behavior. An ordinary world-output observer does not establish those properties. UI-910 is not retired.

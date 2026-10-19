# UI-910: Visitor-info arrow order and portrait residual

## Status and scope

- Bounded implementation based on main `68cee0a7` (October 19, 2026); publication/CI pending, following the October 18 plan-only proposal.
- The arrow-order mismatch is corrected; UI-910 stays open for the portrait and independent visual/lifecycle residuals.
- Portrait frame `0x3722` is still empty. No portrait, gauge, camera, sound, keyboard, save-format or simulation behavior is added here.

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

## Portrait work still required

Verify visitor figure identity/phase, rendering scale and timing before implementing the portrait, with UI-082, UI-092 and HEADS-004 ownership considered. The old speed-8.0 comment is not a verified portrait contract: the bounded inspection found a different raw float argument (12.0), but did not establish that parameter's complete rendering/timing semantics. No guessed speed correction or portrait implementation follows from it.

Remaining portrait acceptance cases include every visitor type, moving/held/riding states, leave and reopen, cycling, and the unchanged four-second stats refresh. Use synthetic fixtures and private original comparisons; never redistribute original media. UI-910 is not retired by this change.

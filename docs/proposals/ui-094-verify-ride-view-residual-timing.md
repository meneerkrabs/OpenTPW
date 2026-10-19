# UI-094: Verify ride-view residual timing

## Status and classification

- Bounded timer implementation; UI-094 remains an `approximation`.
- Implementation baseline: main `9eb541f193bcd34af836b6a4b53276f85a9bfeab` (2026-10-19).
- Continues the existing UI-094 draft proposal. The October 18 progress fragment is retained as historical scope, not a description of the later implementation.
- No original executable was run. Original binaries, disassembly and assets remain private; repository tests use synthetic state only.

## Verified timing contract

The timer block is independently verified from static inspection:

- `TPW-EXE 0x54fabb–0x54fb1a`: compare the current Boolean `(gui_CameraFlags & 0x16) != 0` with the previous observation. Both edges reset; changing between nonzero masked modes does not.
- `0x54faf7`: reset the budget to `0x12c`, or **300 milliseconds**, replacing the previous measured 330 ms midpoint.
- `0x54fb20`: draw static while the incoming budget is positive. Only afterward, `0x54fb46–0x54fb57` subtracts elapsed wall-clock milliseconds; it does not clamp at zero. Thus a long interval still presents the already-latched static frame, with normal rendering on the next nonpositive-budget observation.
- `0x54fb5c`: a second post-draw `GetTickCount` read becomes the next stamp. The interval between the two post-draw samples is not charged. The host single-sample implementation below does not claim this exact native accounting.
- `0x42a6b8`/`0x42af24`: mode 4 is the ride seat and mode 2 the terrain camcorder. The role/setter of mask bit `0x10` is still unresolved.

The enclosing gate at `0x54f8e0` can bypass the timer entirely. Its two flags' product meaning is not established; they are not labeled as pause or focus. Native session initialization/reuse is also only partly recovered. These limits constrain the host adaptation, rather than making the verified 300 ms reset uncertain.

## Implemented bounded slice

- `Hud/EyeModeStaticTransition.cs` owns only instance-local presentation state. It observes the final mode once at the end of an eligible `ParkHud.Update`, latches positivity and the existing noise key, then subtracts elapsed monotonic time once.
- `SyncRideScreen` still runs before and after input, but only synchronizes the viewfinder/eject screen. Enter/exit callbacks within the update are visible to the single final observation. Intermediate changes wholly between observations are not invented.
- The active predicate requires reference identity between `Camera.Mode` and this level's ride or free-walk owner. A retained retired owner and an armed camcorder do not count. Walk-to-ride and ride-to-walk do not restart the countdown.
- A newly created HUD snapshots its current active camera without synthesizing an entry. Finale-owned updates clear only the visible frame latch; no mode observation, clock sample, or countdown charge occurs. Eligible resume charges the skipped interval after latching a positive frame, unless an observed edge starts a fresh budget.
- Production samples `Stopwatch` once per eligible update, independent of simulation speed and `UiContext.Delta`. Invalid/backward test samples cannot extend or poison the budget. This finite monotonic arithmetic and the HUD eligibility policy are explicit platform adaptations, not exact native two-clock, frame-gate or session behavior.
- Drawing reads the prepared state. Repeated Draw calls cannot shorten the timer, resample time, change the prepared noise key, or consume simulation RNG. Save/hash schemas are untouched.
- The procedural noise implementation, controls, two-second right-click guard, HUD/advisor/cursor layering, seat selection and projection are unchanged.

## Residual work, kept separate

1. Port/review the now-traced native noise generator (`0x53e760`) and decide presentation-local sequence handling: 320 x 240 opaque black/white/grey150 pixels with CRT random draws per invocation. The current grayscale hash, 30 Hz key and scan bands intentionally remain. Exact native RNG sequence coupling is not claimed, and rendering must never consume authoritative world RNG.
2. Recover the full native enclosing gate, startup/session lifecycle, unresolved mask bit `0x10` and relevant composition order. No host mode is invented for the missing bit.
3. Retain the unported native invisible ride window (`0x48a740`), seat/fallback axis and prototype-heading qualifications, and the original crowd-call camera-cell source question. Related UI-095 restoration ownership is unchanged.
4. Obtain exact-head C# execution and real-data visual comparisons before claiming runtime/visual validation. Synthetic tests do not prove original rendered fidelity.

## Authored validation and execution boundary

- Pure lifecycle regressions: both edges, replacement during active static, unchanged Boolean, zero/negative exhaustion, a long elapsed interval preserving the current frame, skipped eligibility, invalid/backward host samples, already-active initialization and isolated instances.
- Actual HUD integration: shortcut entry, screen-Back exit between screen synchronizations, arming and ground-click walk entry, walk/ride swaps, unobserved leave/reentry, stale owners, paused zero-delta updates, finale cleanup, multiple HUDs and repeated CPU Draw triangles.
- Repeated drawing compares timer state, sampled-clock count, prepared noise triangles, guest snapshot, simulation RNG and the world-state hash. This tests the retained host renderer without replacing its algorithm.
- Local Python source guards, fidelity/progress checks and synthetic evidence suites are run separately and reported with their real output. They do not execute the C# tests. The local environment has no `dotnet`; exact-head build/unit execution belongs to CI. No local GPU or original-game visual validation is claimed.

## Ownership and completion boundary

This slice belongs to the existing UI-094 draft only. Other UI, winter, online and bridge work is out of scope. The timer's formerly untraced reset/edge/latch semantics are now implemented, but UI-094 is not retired and the independent residual work above remains open. Review and exact-head CI are required before publication is described as validated.

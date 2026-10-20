## Locator visitor order and blank carried-staff icon (October 19, 2026)

The visitor locator radio now follows the live ThingPool used chain, including visitors on rides or carried
by guards. Selection advances by actual links and wraps to the head, so restored chains and reused low IDs
keep their order. Queued destroys, placeholders, wrong models, missing guest objects and Gone visitors are
excluded. Unsupported pool-free fixtures retain an explicit ascending-ID fallback with deterministic deduplication.
Navigation does not mutate simulation data, pool order, RNG or saves.

The locator's staff state control now draws nothing for state frame 9 and restores the correct mesh on a
valid state. The shared model frame fallback is unchanged. Source and UI documentation also stop describing
existing advisor help posts `0xc6`/`0x11f` as missing sound events.

UI-091 stays open: staff radios need their independently persisted role chains; camera smoothing, patrol
rectangles and panel sliding remain. Visitor position failure keeps its existing safe policy: no invented
camera target, then a cleared visitor selection on update. Staff identity behavior is unchanged.

Synthetic C# regressions cover real radio callbacks, lifecycle/fallback/restore cases, save/RNG immutability,
camera guards and the actual four-frame/blank-icon Draw path. The local environment has no dotnet SDK, so
these tests await exact-head CI; Python source guards and fidelity/progress checks do not establish C# or
real-data visual parity. See the [implementation and residual contract](../proposals/ui-091-complete-locator-presentation-behavior.md).

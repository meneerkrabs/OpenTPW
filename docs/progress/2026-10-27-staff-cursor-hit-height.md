## Preserve the staff cursor's picked height (October 27, 2026)

The staff hire/carry presentation now keeps the cursor pick's full engine-space
terrain-or-model point, instead of reducing it to cell coordinates and sampling
terrain height again. The dangling sprite retains its existing five-original-unit
lift (one engine unit). Cell mapping, hover validity, drop rules, pointer admission
and the legacy cell-only terrain fallback are unchanged.

Synthetic CPU regressions are authored for elevated tower hits in hire and carry,
model-to-terrain refresh, a miss and cancel, a model behind terrain, and stale-height
clearing on cell-only updates and tool replacement. These tests need no original
media or GPU. C# compilation and execution remain unverified in the preparation
environment because the pinned .NET SDK is unavailable. GUEST-021 remains open:
original glove/sprite screen alignment and visual acceptance are not established.

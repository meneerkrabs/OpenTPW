## Pools: the activity host in the swim session (October 17, 2026)

The host side of `IPoolActivity` ([POOLS.md](../POOLS.md#activities-extpools-activities)), shared by pool extras, water slides and water attractions.

- **State 25 `PoolActivity`** with generic per-guest fields (id, target, phase, turns, point), snapshotted, validated and hashed only while a guest is in it; the registry `GuestSimulation.PoolActivities`, filled by Level while the extension is on.
- **Offer cadence** from the swimmer's own stream: nothing is drawn when the registry is empty or offers nothing, so every existing pin (including `PoolsChangingRoomFixedRunHash`) is unchanged. Activities with a start chance (a shower) are offered as the swim begins.
- Update handling of continue, back in the water and end of session; aborts on demolition, erased pool or target, park close; swimwear slot constants and per-activity slot selection.
- Proven with a test-only fake activity (`PoolsActivityHostTests`). No activity ships with this change.

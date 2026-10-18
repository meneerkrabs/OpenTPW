## Coaster wheel clips follow train-slot setup (October 18, 2026)

The wheel-animation start time now belongs to the train slot. Non-bend cars start their looping clip when a slot is
initialized (`0x43be10`: animation 5, sub 0, loop flag 1, initial speed 1.0), independently of first visibility or renderer
creation. The rating pass initializes only newly activated slots (`0x43cad6`); retained slots keep their epoch. The
incomplete → built state transition (`0x435730`, 1 → 2) restarts all slots, while ordinary open/closed/tool, power and
capacity changes preserve them. Removed and reactivated slots restart even when the renderer sees the same train count.

The existing whole-millisecond, current-speed and looping arithmetic of `CoasterCarRig.WheelTick` is unchanged. No guest
simulation, deterministic-hash or save fields change. A park restore initializes fresh presentation clips at the restored
controller time; wheel-phase continuity across save/load is outside this slice.

Synthetic fixtures cover delayed first presentation versus repeated phase sampling, renderer recreation, same-count slot
replacement, retained and newly activated rating slots, ordinary state changes and the existing frame arithmetic. An
original-data fixture compares the wheel UV tracks loaded immediately and after setup without exporting game assets.
These exercise the renderer's CPU phase query and model UV application, not a graphics-device render or native-game oracle.

Validation: Python fidelity/progress checks and their unit tests pass; the C# build and focused
`CoasterPresentationTests`, `CoasterPresentationAssetTests` and `CoasterTrainLayoutTests` remain unrun because the pinned
.NET SDK is unavailable in this executor. Independent review and an SDK-equipped test run are still required.
COAST-034 remains open for bend sampling/endpoints, flume-clip invocation and unbent rider sockets.

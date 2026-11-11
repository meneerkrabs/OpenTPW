## RIDES-011: the type-3 tail of the click handler is reachable (November 11, 2026)

The previous change closed the coaster tool's entry half of the sticky tool globals and left one clause open: the type-3 tail of the click handler, whose reachability from a coaster marker tool could not be shown. It is reachable.

At `0x5251F5` and `0x525838` the handler writes the selected thing's **raw** anchor into both pairs, skipping the `0x52A3A0`/`0x52A420` mapping, when `0x52FAF0()` answers 3 — the coaster track type. The way in is the ride window's track button `0x4AF200`: it arms the build tool at `0x4AF33E` (`0x46C350`) and then calls the click handler `0x524960` at `0x4AF34B`, and again at `0x4AF40F`, so a coaster-track session does reach that branch.

OpenTPW maps on every path, so after a coaster-track add-on it leaves the mapped station cells where the original leaves the raw anchor. That is the remaining half of the entry: the declaration now records the branch, its trigger and the reachability, so the change is a known one rather than a question.

The reachability was established with the Ghidra provider rather than byte scanning: the plain `e8` sweeps had found no caller of the button's handler because the path runs through the tool arming first.

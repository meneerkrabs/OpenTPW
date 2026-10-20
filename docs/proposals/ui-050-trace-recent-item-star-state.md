# UI-050: recent-research stars in the buy list

## Implementation scope and status

The two original evidence gates have been resolved by static inspection. The TPW implementation and synthetic regressions have passed independent source review. Exact-head CI remains required before merge; the implementation retires the resolved UI-050 declaration. This note claims neither original execution nor rendered-pixel equivalence.

## Corrected behavior

Research project completion (`0x504630`) constructs event `0x11` (`0x40F580`). The analyser handler (`0x4C7370`, writer `0x4C73AB`) appends the completed item's ID to a capacity-three ring selected by its descriptor's `WhichUIType`, not its research category. Four rings cover rides, shops, sideshows and features. Upgrade-level completions may append the same ID repeatedly; paid add-ons also consume a slot in their own UI category. There is no deduplication. Initial free items and propagated free completions do not send this event.

The buy-list filler (`0x4AAF70`) chooses state 1 when ownership is positive, otherwise state 2 when one of three newest entries matches, otherwise state 0. Ownership masks the star without consuming history, so selling the last copy may reveal it again. Auxiliary tool rows keep state 0.

The numeric list cell calls its model-state setter (`0x66DA4D`); the geometry handler (`0x476F10` / `0x476E80`) selects the root followed by that many next-node links. Thus states 0/1/2 select model frames 0/1/2. The earlier `0xC00` lead concerns text alignment, not icon-state selection. Independent UI model transform/depth approximations remain separate.

## Persistence and defensive bounds

The original analyser serializer (`0x4C5D70`, ring helper `0x4CDB80`) persists all four rings. The existing TPW analyser layout places them at body offset 62,492, four 17-byte records: head i32, capacity i32, wrapped u8, temporary u16 and three physical u16 entries. Preserve temporary words and unused entries as well as active history.

The implementation adds optional own-format presentation state, empty for older saves without that field. Completed-project order cannot reconstruct chronology. Native TPW import uses the world-referenced analyser and verifies its type and bounds. Export with explicit research state patches only the 68-byte ring range; absent research options retain template history. Reject malformed present capacity/head/wrapped/state lengths using bounded validation, explicitly a defensive host policy rather than the original unchecked loader's behavior.

Only the new presentation property is excluded from the canonical gameplay hash. Research completion, progress, effort and other gameplay state remain hashed. Theme Park Inc behavior and offsets are not established by this TPW trace and must not be silently inferred.

## Verification coverage and remaining execution

- Actual research completion path: paid/free distinction, duplicate upgrades, add-ons, cross-category isolation and four-event eviction.
- Actual buy-list DrawModel frames 0/1/2, ownership priority, selling, pseudo rows and unchanged sizing/sort behavior.
- Own-save continuation, old-save fallback, malformed-state rejection, deep copies and opaque unused values.
- Native typed-reference/model/bounds validation; exact ring round-trip including temporary words; byte preservation everywhere outside the 68-byte region.
- Ring-only save changes preserve canonical hashes; separate completion and progress controls still change them.
- Independent source review and full exact-head build/test checks. Synthetic tests do not constitute original runtime or rendered fidelity proof.

The implementation keeps unrelated research arithmetic, selection, scoring, RNG, UI geometry and other owners' work unchanged.

The focused C# tests cover these cases, but local execution was unavailable because this environment has no .NET SDK. CI results are recorded in the pull request; this note does not imply a successful run before those checks complete.

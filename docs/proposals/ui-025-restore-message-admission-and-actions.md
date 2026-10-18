# UI-025: Restore message admission and actions

## Status
This remains a plan-only draft for the residual tube behavior. Progress rows were implemented in #442; the bounded terminal lifecycle is implemented in #623 and awaiting its full CI. Neither change closes UI-025.

## Corrected evidence
The original tube insertion at 0x48EB20 scans ten reusable item slots. The advisor's eight-slot advice queue at 0x59A940 is a different structure. The earlier version of this proposal incorrectly assigned that advisor capacity, priority replacement and per-type caps to the message tube; those are not acceptance requirements for this ID without separate tube evidence.

Current OpenTPW keeps a five-slot tube and generic oldest-item eviction. New challenge progress and terminal rows refuse full occupancy. Progress refreshes matching tag/context rows without another insertion sound. Terminal instances retain read/closed rows, cancel timers on first read, and keep hidden occupancy until delayed final release. The exact implemented scope and native ownership/queue residuals are documented in docs/reverse/CHALLENGE-TERMINALS.md.

## Remaining bounded investigation
1. Trace the complete tube allocation, slot reuse and layout behavior before changing the host capacity or compaction policy.
2. Map remaining original action kinds to their owners and safe missing-target behavior. Preserve challenge gameplay action guards.
3. Trace original pending-message serialization and restoration separately. The current host does not save the tube; that is a residual, not proof the original omitted it.
4. Keep generic message read/removal policy and existing extensions explicit. Do not copy advisor admission rules into the tube.

## Acceptance cases
- Ten-slot allocation and an eleventh insertion, using the proven tube policy rather than guessed priorities.
- Hidden removal-pending occupancy, slot reuse, selection and same-tag duplicate instance ownership.
- Read, close, right-click/delete, synchronous replacement and delayed release.
- Missing action targets, read-only gameplay guards and challenge offer/result ordering.
- Save/load behavior only after its native contract is traced; synthetic tests must not stand in for that evidence.
- Original art and rendered layout require separate visual validation.

## Ownership and completion
Coordinate with active message, challenge and online owners only where files or behavior overlap. Reuse #442 and #623 rather than duplicating their work. UI particles 90/91 remain under PTCL-908.

No original assets, strings, binaries or private input metadata are included. This proposal changes no gameplay code and remains draft. Publish any later implementation with its specific evidence, independent review and actual test results; retire no whole ID based on a partial slice.

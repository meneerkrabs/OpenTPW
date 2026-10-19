# Instant Action research-finale boundary

## Traced lifecycle

The lab's completion-message handler (`0x503D40`) checks the remaining research after the
completed item and group notifications. In game type 2 it posts advisor message `0xA7` (167)
and calls `0x516B00`. Game type 1 does not take this route. Type 0 also requires the
player-wide completed-park records behind `0x5B0C40`; OpenTPW does not implement that prerequisite.
The call to `0x409CD0` constructs the 16-bit handle argument for the advice message; it is not
evidence of a separate flag being cleared.

`0x516B00` finds the case-insensitive definition name `End`, constructs the fixed object and
stores its thing handle. `0x516C00` answers whether that handle is nonzero; the bank message
handler uses it to suppress bankruptcy during the finale. Creation also calls
`0x481B60` → `0x48AE70`, which exits the previous camera mode, hides HUD windows and creates
a focused full-screen winning window with handler `0x48A970`.

That handler exits on Escape (`0x48A99E`, key `0x1B`, no modifier bits), or the right-button
click (`0x48AADB`) when `RMBCancelOn` is enabled and elapsed milliseconds are strictly greater
than 2000. Both call `0x516C10`, which requests destruction of the winning thing and clears
its handle. Script destruction clears an owned `DIPMUSIC` request (`0x558513`). The handler
also changes profile state and, in Instant Action, shows UITEXT 471; those steps are not ported.

## Implemented slice

- A new `ResearchAllDone` event creates `End` only in a writable TPW Instant Action park.
- The fixed settings name, not a localized display name or hard-coded Info.Id, selects it.
- Existing live objects are reused. A missing definition changes nothing.
- Placement uses the existing fixed-object origin and ordinary seed/thing allocators; no purchase occurs.
- The economy's bank predicate follows the live object and is rebound when the economy is replaced.
- Startup/load only binds that predicate. Completed research alone never triggers creation.
- A restored live End is dismissible through the same screen without a second allocation.
- Normal object removal stops the root/child scripts, releases held sounds and particles,
  clears the script's music duck, and queues its thing slot for end-of-turn destruction.
  Unheld one-shot EVENT particles retain the existing runtime lifetime behavior.
- A focused full-screen HUD layer owns input through the dismissal frame. Park tools and
  camera controls cannot act underneath it; normal HUD input resumes on the following frame.

The screen's visible, localized Resume Game button is an explicit OpenTPW fallback, including
its ordinary button/Enter activation. It is not a reproduction of the original panel. The
right-click edge uses OpenTPW's existing per-frame input, which does not preserve the original
window-message timestamps.

## Script support and remaining work

The finale's root script uses music ducking, held sound categories 5/9, category-5 EVENT sound,
and keyed EVENT particles. Its child animation script uses held particles, ambient sounds and
WAITANIM. These use the existing runtime handlers; this change adds no guessed opcode behavior
and includes no original script or model bytes.

RIDES-019 stays open for Full Simulation's player-wide condition, the winning-camera follow,
the original panel, post-dismissal profile/dialog changes, and faithful winning-UI restoration.
Synthetic tests exercise the actual object and screen code but cannot prove the original visual
composition. No visual or full ending parity is claimed.

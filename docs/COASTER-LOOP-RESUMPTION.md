# Coaster loop-pair resumption

This implements a bounded part of COAST-035: returning to the loop tool after the
first pylon has been placed resumes the second pylon. COAST-035 remains open for
the auxiliary dispatcher and loader qualifications below.

## Corrected premise

The previously proposed "selected-pylon" interpretation was too broad. The
confirmed predicate reads the current **chain endpoint**, not an arbitrary
pylon picked with the mouse. An incomplete track uses its tail; a completed track
with a cut uses the predecessor of the gap. A completed, uncut track has no
pending pair. The node's `0x80` flag is sufficient for this predicate; neither
node flag `0x40` nor the active loop-mode bit is required.

Clearing the tool mode therefore cannot clear the pending-pair condition.
Previously the panel's condition depended on `LoopMode`, but opening the panel
cleared that mode before asking whether a pair was pending. Switching through
an edit button also lost the pair counter and restarted it at two.

## Static evidence and host mapping

These addresses are from read-only disassembly of the PC build. No original
executable was run for this change.

- `0x444c00`: after the controller and its definition's flag-2 gate, test node
  flag `0x80` on the incomplete tail (`+0x58`) or completed-cut predecessor
  (`+0x60`). Return false for completed, uncut circuits. `Track.LastIndex`
  provides the host endpoint through `CutAfter ?? Nodes.Count - 1`.
- `0x442d10`: a pending endpoint writes pair counter `1` and sets mode bit
  `0x40`. It does not pick a pylon or start a new pair otherwise.
- Loop selection `0x446fa0` → `0x447135` → `0x442d10`, followed by
  `0x4445b0(2)` → `0x442cd0`: resume first; otherwise start counter `2` only
  if loop mode is off. Already active mode is preserved.
- Panel opening `0x4455c0` independently resumes counter `1` and mode `0x40`
  for a pending endpoint before selecting Loop. Completed, uncut tracks
  select Height. `0x445910` disables Build from the endpoint predicate,
  independently of the current mode.
- Leaving loop mode (`0x444c80`) clears the mode and counter, while the node
  flag remains. Cursor flags (`0x436230`) add `0x80` only for counter `2`;
  the resumed cursor carries `0x40` without `0x80`.

`CoasterBuilder.LoopPairPending` owns the endpoint condition;
`StartOrResumeLoopPair` applies the transition. Opening, selecting Loop and
refreshing a disabled panel selection share the same behavior. The host's
existing available-loop-pylon and upgrade-level gates remain unchanged. If a
pending pair's Loop button is unavailable, the panel falls back to Height and
keeps both build buttons disabled.

## Regression coverage and limits

Synthetic `CoasterPanelTests` cover close/reopen, Height/Move interruptions,
repeated selection, second-pylon preview flags and rotation, ending a resumed
pair, starting a new pair from the still-selected Loop button, cancellation
before placement, the cut predecessor rather than the physical tail, flag-only
endpoints, completed uncut tracks, availability/upgrade gates, disabled-selection
refresh and resumption from existing saved node flags.

No save schema, placement/hover geometry, picking, cost arithmetic or spending
path changes. The auxiliary dispatcher `0x447a10` still lacks a verified host
caller. The original controller/definition flag-2 loader gate also remains
unrepresented by the host's strongly typed controller and definition. These are
still COAST-035 qualifications; this patch does not claim whole-ID closure,
original-runtime parity or visual verification.

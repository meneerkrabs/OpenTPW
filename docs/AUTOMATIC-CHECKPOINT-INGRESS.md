# Application automatic checkpoints

Normal Exit to Lobby in a writable Theme Park World Full Simulation or Instant
Action park now flushes application-owned ticket progress, captures the current
park and atomically saves `opentpw-{theme}-automatic-world.json` before retiring
the park. Manual `opentpw-{theme}-world.json` saves remain separate. Quit, manual
Save, generic replacement/loading and teardown do not create this checkpoint.
Read-only visits, reference inspection, sandbox and Theme Park Inc retain their
existing exit policy without automatic writes. This is OpenTPW host policy, not
native autosave/profile parity.

The actual mounted pause Exit button leases its HUD, Level, runtime, save root
and progress owner. Capture runs in the queued transition, after the click
frame's last park update. A legitimately earned ticket in that update can advance
the owned committed profile receipt. Missing/inactive tracking retains its
original capped opaque profile-byte binding instead of inventing a ledger.
External changes or failed dirty progress flush refuse capture. The receipt is
never derived by blindly adopting current storage.

A pre-retirement failure keeps the live park and shows its reason with Retry
and Back. Retry flushes and captures fresh state; Back stays in the park and wins
simultaneous acceptance. Unsupported capture state, including live sound loops
or controllers, can block this exit indefinitely. No force-stop, discarded state
or weakened capture is provided. Normal Quit keeps its existing progress-only
policy. A lazy pending load is cancelled by an explicit exit request under the
existing queue policy; a failed checkpoint does not restore that request.

After the automatic write returns, retirement/lobby failures propagate outside
the save-failure dialog boundary. The saved checkpoint remains committed; there
is no claim that partial teardown restored the old world. Progress can remain
committed while a later checkpoint fails. No two-file transaction, cross-process
CAS, historical profile creator identity, browser durability or forced OS-close
guarantee is added.

Theme Enter offers distinct manual Continue, automatic Continue, New and Back
as available, with each saved mode and display name identified. Its dedicated
two-row choice layout remains inside the dialog. Automatic metadata probing is
read-only and bounded; missing/corrupt/unsupported automatic data is not offered.
The offer freezes a current namespace and opaque profile digest/absence as well
as the core's semantic decoded-envelope fingerprint. Whitespace-only world-file
changes remain equivalent; raw profile bytes are exact. An in-flight changed
profile, selected theme, root, screen or saved state refuses activation. This
lease makes no cross-session historical attribution.

Automatic continuation uses its own trusted generated slot and the existing
full staged ResumePark path. It restores the saved Full/Instant mode without a
New key gate or profile bootstrap, and uses the existing compatible progress
attach/reconciliation after activation. Manual Continue and Load remain ungated.
It does not fall back to New when data or candidate validation fails.

CPU tests drive actual theme/pause stack input, mounted/retained callback guards,
queued exit ownership and last-update receipt changes, atomic save failures,
fresh retry/cancellation and late-retirement propagation. Synthetic registered
objects run the actual HUD host binding, Level capture, own codec and simulation
restore; asset-free capture/resume seams supply verified synthetic asset hashes
and avoid renderer/lobby construction. Full graphical candidate construction,
original assets, native oracles, GPU/window lifecycle and browser durability are
not qualified by these tests. Existing capture/schema/audio guards are retained.

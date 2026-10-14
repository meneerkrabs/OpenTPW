# Automatic checkpoint core — application wiring pending

This isolated core does not install automatic saving, a lobby choice or an exit
handler. Shared GameFlow, menu and progress-adapter wiring remains a separate
reviewed slice. No existing manual, New, Continue, Quit or TPI route changes here.

`AutomaticThemeContinuation` owns only the generated
`opentpw-{canonicalTPWTheme}-automatic-world.json` slot in a caller's save
namespace. The manual `opentpw-{theme}-world.json` slot remains separate. Probe
uses the existing bounded world codec and shape checks, requiring the selected
theme, Full Simulation or Instant Action mode/start and gate/staff state. It does
not open or hash original assets; full asset/candidate validation still belongs
to the existing staged resume. Invalid or missing automatic data is not offered
or converted to a new park. There is no native save/profile writer or new format.

The transient selection stores its namespace reference, generated path, metadata
and a SHA-256 fingerprint of the decoded envelope reserialized by the existing
codec. This is **semantic own-envelope identity**, not exact raw-file identity:
whitespace-only changes are equivalent, while a changed clock or other saved
state refuses an old selection even when menu metadata is unchanged. It is not
historical creator/profile attribution. The selection retains no full snapshot.

`AutomaticCheckpointSession` owns one caller-created attempt. Its constructor
captures the current namespace and capped opaque own-profile digest/absence.
The attempt requires the same namespace and live-owner predicate, verifies the
profile before flush, receives the expected post-flush digest from trusted owned
progress state, and compares actual bytes against that receipt before capture.
A no-op/unbound flush returns `BoundProfileDigest`; an owned write returns its
expected serialized-file digest. A caller must not derive the receipt by blindly
adopting a replacement file. The opaque reader does not parse malformed history,
create a ledger, reset state, grant keys or exceed PlayerTicketFile's existing cap.

The caller performs its strict current progress-owner/version checks before
creating/using the core. The core compares the supplied receipt, captures fresh
state through the injected existing capture path, checks theme/mode eligibility,
revalidates namespace/profile just before the existing atomic save, then invokes
retirement. Callers remain responsible for exact Level/HUD ownership, writable
eligibility and the between-fixed-ticks boundary. The core does not infer them
from a snapshot or replace the existing capture/asset/audio/runtime guards.

Capture/serialization/IO errors propagate without retiring. Retry captures anew;
cancel/stale/reentrant/already-committed attempts cannot write. Once the write
returns, `CheckpointCommitted` is set before retirement. A late retirement error
propagates with that marker and cannot be retried as a pre-write rollback;
`Completed` becomes true only after retirement returns. Progress already
committed before a failed checkpoint stays committed. This is not a two-file
transaction, cross-process CAS, file lock or browser/OS-close durability claim.

Future queued exit wiring must flush progress, capture/write immediately before
retirement, catch only pre-retirement failures on the current HUD, and offer fresh
retry or cancel-to-stay. Unsupported live loops/controllers may block capture
indefinitely; no force-stop, discard, weakened snapshot or hidden New fallback is
provided. Read-only visits, inspection, sandbox and TPI remain excluded by the
future caller and current mode/edition guards. Ordinary manual continuation and
New keep their existing behavior.

Synthetic core controls run the actual own codec/atomic writer, semantic read
selection, namespace/profile changes before and during capture, no-op flush
replacement, bounded pre-allocation refusal, cancellation/reentry, late move and
post-write retirement failures. A reused existing object/VM/cell fixture calls
the real Level.CaptureResumeSnapshot with verified synthetic asset hashes, then
the real writer/reader; an unrepresentable factory-parameter capture still
refuses and preserves the prior file. It does not exercise native assets,
renderer/GPU construction/disposal or actual application/menu retirement.

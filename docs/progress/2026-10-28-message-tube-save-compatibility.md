## Read message tube saves written before the loader correction (October 28, 2026)

The strict world-save reader now accepts the message-tube row shape emitted by
#888 as well as the current #891 shape. A legacy terminal's `Active` flag maps to
the inverse `Read` flag only after its complete legacy shape and timer bounds are
validated. Unknown fields, incomplete legacy rows, mixed `Read`/legacy shapes,
and inconsistent pending flags are still refused. New saves retain the current
seven-field row shape; no save version or simulation digest changes.

This does not revert #891's loader policy: unread terminal rows restart both
timers from the full lifetime, read rows stay stopped, and obsolete elapsed-time
and pending-delivery state is not restored. Co-op join snapshots still exclude
the host's tube. Staff initialization and saving are unchanged.

Synthetic raw-JSON regressions cover old plain, offer, progress and terminal
rows, read-state migration, current byte stability, whole and sliced world-save
loading, and malformed/unknown-field refusal. No actual user save was inspected;
whether anyone saved with the short-lived older shape is unknown. Local C#
compilation and MSTest execution were not possible because the SDK is absent;
exact-head CI is needed. No original executable or game media was used.

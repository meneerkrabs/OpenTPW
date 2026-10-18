## Release completion catches up green main (October 18, 2026)

A successful stable main-dispatched release now explicitly dispatches a catch-up train with its run ID. The train
validates that source and waits at most three minutes for successful completion before applying the unchanged
active-release, exact-SHA CI, tag/version and final-recheck gates. This closes the case where green main was
skipped during an active release and otherwise waited for the hourly fallback. Explicit dispatch avoids relying
on downstream workflow_run events from token-dispatched releases. An already-tagged head remains a no-op;
failed/cancelled source runs, invalid IDs, API errors and wait timeouts stop before planning. Tests run the actual
wait shell with command doubles for success, delayed completion, invalid sources and bounded timeout, plus the
backlog/repeated-completion scenario. No new credential or game behaviour change.

# Browser original-file cache recovery

Synthetic Node tests exercise the shipped `opentpw-originals.js` module with
in-memory file and directory handles. No user OPFS, browser profile or game
assets are opened.

Two integrity failures were reproduced before their repairs:

- A digest rejection after a same-sized rewrite left replacement bytes readable
  while the current and persisted manifest retained the original stored hash.
  Hashing now finishes before a writable file is opened. A rejected digest
  preserves the old bytes and metadata, and a later write can retry.
- A successful replacement followed by a rejected manifest flush left the old
  stored hash advertising the replacement bytes on the next visit. Replacement
  now persists `stored: false` before touching a previously kept path, and
  publishes new size/hash metadata only after the file closes. If invalidation
  fails, bytes remain unchanged; the retained old hash causes a retry to attempt
  invalidation again. A path marked missing after a read error follows the same
  rule. Initial files with no previous stored hash need no extra manifest flush.

A digest that completes after the folder was cleared or replaced is dropped
before opening its file. Entry identity is checked again after the invalidation
flush, so that wait cannot publish into a new folder's manifest.

The new regression and recovery controls cover digest rejection and retry,
interrupted hashing, file write/close rejection, manifest write/close rejection,
failed cached reads, failed replacement publication, and failed pre-replacement
invalidation. They complement existing successful caching, real SHA-256 content
comparison, overlapping manifest flush, folder replacement and removal checks.
Independent controls also keep the old bytes/hash readable during deferred
hashing without opening a writable file, and verify pre-invalidation write and
close failure/retry with content-sensitive hashes.
All 51 browser-helper Node tests pass, including the texture and performance
helper selections; these results are functional checks, not performance data.

An exposed-store concurrency probe also reproduced final `BB` bytes advertised
with an `AA` content hash when two writes to the same listed path overlapped and
the first close completed later. Mutations to that path within one store run through a
promise tail after hashing. A rejection does not block the next mutation, and the
last settled tail is removed. Entry identity is rechecked when a queued mutation
starts, so an old queued request cannot write into a replacement folder. Different
paths remain independent. Tests cover the mismatch, rejection followed by queued
and later retries, unrelated-path completion/flush, and a queued request invalidated
by clear/begin. An initial lazy write still adds no manifest checkpoint.

The page's `main.js` already serializes its ordinary writes with `keepChain` and
skips kept/queued paths; this probe does not demonstrate corruption in that normal
call path. The change protects the exposed store API and leaves the page queue and
its lazy checkpoint schedule unchanged. Mutation order follows completed digests,
not a promise of invocation order while hashes are still pending.

Failed rewrites can temporarily invalidate cache availability, even when the
older physical file remains. The page can ask for that missing file again.
The module does not promise an atomic transaction across data and manifest files
or power-loss durability. The in-memory handles model writes that reject before
committing and cannot qualify browser-specific quota, permission, stream cleanup
or crash behavior. No real-browser storage or user-profile check was performed.
Entry checks cover digest, invalidation and queued-start waits. Clear/begin is not
serialized with mutations already inside later file-handle/write/close waits, and
separately opened store instances do not share mutation tails. There is no atomic
transaction across different paths. Caller-owned input bytes must remain unchanged
until their write completes. Cached reads still validate size, not the content hash.

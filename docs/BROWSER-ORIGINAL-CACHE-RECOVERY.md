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
All 45 browser-helper Node tests pass, including the texture and performance
helper selections; these results are functional checks, not performance data.

Failed rewrites can temporarily invalidate cache availability, even when the
older physical file remains. The page can ask for that missing file again.
The module does not promise an atomic transaction across data and manifest files
or power-loss durability. The in-memory handles model writes that reject before
committing and cannot qualify browser-specific quota, permission, stream cleanup
or crash behavior. No real-browser storage or user-profile check was performed.
The entry checks cover the digest and invalidation waits; they do not serialize
clear/begin or concurrent writes to the same path during subsequent file-handle,
write or close waits. The page's ordinary write queue remains responsible for
serializing queued writes. Cached reads still validate size, not the content hash.

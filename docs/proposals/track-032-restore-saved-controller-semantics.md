# TRACK-032: Restore saved controller semantics

## Status and classification
- Classification: approximation; TRACK-032 remains open.
- PR #597 began as a plan-only proposal. The October 20 structural-reader increment adds immutable decoding and synthetic tests, not runtime restoration.
- Baseline for this increment: main d655ed1b, retaining the original proposal ancestry.
- No `Level.Tracks` or `RewalkTrack` integration is included; that runtime area has separate active ownership.

## Corrected native evidence

Static examination of authorized tp.exe corrects the original proposal's terminology:

- Chunk 3 serializes 40 bytes of scalar fields, including duration. **0xd0 is the in-memory ride stride**, not the serialized record width.
- Chunk 5 serializes a handle, a raw 0xac controller record and five relocation words. These records are emitted in pool order, not circuit order.
- Chunk 6 serializes only a ride handle. It chooses from the separate **0x1c waypoint chain** at ride+0xa8, not from chunk 5's raw records.
- Chunk 3's station/grid construction and chunk 4's tile geometry create that waypoint chain. Native selector `0x5439bb..0x5439e4` finds the first flag-4 waypoint, always advances at least once, wraps a null successor to the head, and skips flag-2 candidates. Its initial marker scan has no wrap/null guard.

The verified field layout, addresses and parser policy are documented in [TPWS-PAYLOAD](../TPWS-PAYLOAD.md#trackrides-structural-reader-and-track-import-track-032-narrowed).

## Bounded implemented scope

`SaveTrackRideBlock` reads a full outer container or an explicitly headerless subchunk body. It preserves every header word,
unknown payload, duplicate identity/position, repeated reference request and stream order. Typed chunks use strict writer-shape sizes;
this differs deliberately from native short-read zero-fill and excess-byte skipping. No runtime identity validation is implied.

Waypoint relocation keys remain absent (owner zero) or unresolved (owner nonzero). Existing owner headers alone cannot resolve them.
There is no fabricated reference index, raw-pointer traversal or inferred circuit chain. All parsing is staged before a complete
immutable document is returned; this establishes parser atomic return only, not park-restoration atomicity.

## Remaining evidence and acceptance gates

1. Independently trace geometry-specific chunk-4 waypoint construction and all station/grid variants needed for a faithful chain.
2. Trace the remaining raw-controller fields and saved-versus-re-derived walk verdict before runtime integration.
3. Prove a saved controller differing from a fresh map walk restores duration, controller state and reference waypoint correctly.
4. Test selector wrap, mandatory advancement, multiple markers, skipped grid waypoints and safely bounded malformed-chain refusal.
5. Distinguish native first-match duplicate-coordinate lookup from any stricter managed ambiguity policy.
6. Prove failed runtime restoration leaves the park unchanged, with independent source review and exact-head CI.

## Review boundary

The structural-reader increment is independently testable but does not close TRACK-032. No original executable/media is included
in tests or documentation. Runtime work, approximation retirement and parity claims remain outside this increment.

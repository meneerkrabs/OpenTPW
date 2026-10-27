# QUEUE-003: supported typed queue front

Baseline: main 8382dea5. Static evidence: sp20_nocd/tp.exe SHA-256
5472f97b5eb28f58b5a49f3a15f6f66adffa6b2625a623f503202c9696d1b805.
No original executable was run. Regression fixtures are synthetic.

The front selector 0x4DE040 reads the ride's mEntryPos (+0x36), then the
entry cell's link byte (+0x0C, accessor 0x522770), not its queue direction
(+0x0D). It chooses bits 1,16,64,4 in that order (-Y,+Y,-X,+X). It performs
no neighbour type, ownership or reciprocal-link check. No cardinal bit
returns native cell id 0. The back walker 0x4DE130 initializes count +0x40
to zero and leaves it zero for that missing front.

For live typed HasQueue objects, Level rebinds an entry resolver at object
registration. It reuses the queue tool's owner-matched, last shape entrance
reconstruction, requiring a type-9 entry with the matching owner. These are
supported host reconstruction boundaries, not extra native selector tests.
The bridge reads that entry's live links on queue recomputation and updates
both front and direction. A selected in-map neighbour is retained even if
it is empty, foreign-owned, or lacks a reciprocal link. Link edits use the
existing grid-version queue refresh; no additional RNG draw or command is
introduced. The resolver is not serialized. Registration rebinds it before
queue restoration. Optional paired TypedQueueFront/TypedQueueDirection snapshot
fields preserve the cached positive front/direction exactly, including saves after
link edits but before queue sync. They are omitted for untyped bridges; existing
canonical field ordering is unchanged. Partial pairs, invalid directions and
out-of-map fronts are rejected. Legacy snapshots without the pair retain the old
shape-registration cache reconstruction until a normal queue refresh; historical
directions are not guessed from changed links. Existing cell-map/snapshot geometry
checks remain strict, including rejection of unsupported mismatched current caches.

Zero front (fidelity round 9): when no cardinal bit is set the selector writes 0 (`0x4DE0F9`), so the ride has no front, `0x4DE130` returns 0 with `+0x40` = 0, the back-of-queue test `0x4DE4A0` reports a queue ride
unconnected, and the demolition `0x527EE0` finds no count to undo (a front that holds no queue cell still counts 1: Back clears nothing and one cell is debited, `0x527FB4`).
`RideVisitorBridge.ReadNativeFront` reads it; `Level.SettleDemolishedQueue` and `RideVisitorBridge.NotifyQueueEdited` use it. The opening guard `ParkGate.QueueConnected` is ECON-049's and still answers permissively for such a ride (the sandbox smoke's track rides are placed without their access cells and rely on it); the guests' joining and scoring keep the host fallback.

This is not retirement of QUEUE-003. Linkless/diagonal-only entries, unavailable
entry reconstruction and out-of-map results retain the existing host fallback,
not native zero-front semantics. Queue arrays remain nonempty and existing
snapshot validation is unchanged. Divergent imported mEntryPos and overlapping
or deleted entry reconstruction remain unsupported. Untyped harnesses, TPI
and read-only visits do not receive the provider. Back-cell admission and
fourth-place geometry are unchanged. Native cached-back invalidation timing
is not newly claimed here.

Tests cover four rotations, mixed-bit priority, +0x0C versus +0x0D, stale
fronts, last entrance selection, live link edits, positive snapshot restore,
and canonical-state equivalence when replaying synthetic link edits. This
input replay is not a claim of an original-game execution oracle or browser
verification. Existing queue, resume and deterministic command tests remain
required CI regressions.

Imported parks: the typed front leaves no fallback when the entry's linked
neighbour is not a queue cell, so an import must restore the saved queue cells.
tp.exe's cell base serializer 0x4D0B30 (mode 0) reads mDirection (+0x0D), mFlags
(+0x0E), mNeighbours (+0x0C), mParentID (+0x10) and mType (+8) of every cell into
the map; `ParkCellMap.FromOriginal` restores type-3 cells with these fields. The
Jungle Easymode Belly Bounce's entry link selects (52, 22), the first of its four
saved queue cells, and the back walk ends at the saved mBackOfQueue (49, 22).

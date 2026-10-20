# Queue exits at owned entrances

A guest dismissed or refusing a price at a supported typed entrance now remains
at its exact navigation position. The entrance must be the same ride's stand
cell, type 9, with a nonzero matching owner word and reciprocal links to its
actual front cell. The shared walk edge test must permit that edge.

The existing dismissal, thought, happiness, voice, observer and queue accounting
paths are unchanged. This slice only extends the existing position-preserving
branch. It depends on the linked access-cell wander start support. Untyped,
unowned, mismatched or disconnected geometry keeps the previous fallback.
QUEUE-010 remains open for those cases; no general cleared-cell relocation
policy is changed.

Synthetic `TypedQueueExitTests` drive actual queue admission, stand-point price
refusal and ride-head dismissal. They check exact position and existing effects,
malformed ownership/link controls, and a JSON-resumed next decision and walk.
C# execution is still required before runtime validation is claimed.

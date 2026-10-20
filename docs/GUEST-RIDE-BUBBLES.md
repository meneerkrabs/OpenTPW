# Guest thought bubbles in ride view

The Level-owned guest simulation skips new bubble work while its ride view is open. Logical thoughts and their observer callbacks still run, followed by the existing voice path. An existing bubble is neither hidden nor cleared by entering the camera; ordinary needs-phase expiry continues. Returning to the park applies the existing priority cooldown on the next thought.

The presentation predicate is not serialized or hashed. Standalone simulations default to allowing bubbles, and online visits retain their existing bubble extension. Each Level binds its own simulation; a retired simulation cannot consult the replacement's camera state. Snapshot restore keeps the current binding. Staff thoughts and their saved/hash-visible bubble state are unchanged.

Synthetic tests cover direct thought/observer ordering, cooldown and expiry, normal needs-phase RNG/hash/voice equivalence, actual ride entry/exit, two Level owners, replacement and restore. C# execution remains an exact-head CI requirement; these tests do not establish rendered or original-executable equivalence.

GUEST-006 remains open for the other camera mask bits, environment restoration and fireworks lifecycle evidence. The existing public ride-camera bit-4 and thought-setter annotations support this bounded mapping.

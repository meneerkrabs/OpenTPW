## The session digest's canonical text without a JSON tree (October 23, 2026)

The economy part of the session digest is now written as canonical text straight from the park save's bytes and hashed a byte a step, with the same text and the same hash as before ([COOP.md](../COOP.md), "The digest's canonical text"; [DETERMINISM.md](../DETERMINISM.md), "Canonical state hash"). This is C2c-2b-4b.

- A guest's check at the resync's cut takes 5.9 ms in the page instead of 7.6, and the resync's digest stage 11.8 ms instead of 23.3; on the desktop 1.1 and 2.3 ms instead of 2.0 and 7.2.
- No checkpoint frame of a page host reaches 30 ms any more. The save's serialization is now the digest's largest part in the page.
- Every pinned hash is unchanged; a new test keeps the old tree-built text as the reference.

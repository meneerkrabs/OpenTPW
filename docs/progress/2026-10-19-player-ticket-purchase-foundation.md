## Player-ticket purchase foundation (October 19, 2026)

Added an application-ledger purchase operation and a versioned representation of known purchased Info.Id identities. This is a tested-by-fixture foundation; live park purchases still use the existing park-local path. UI-051 remains open.

A valid positive ticket cost is charged once per nonnegative int32 Info.Id. Existing ownership and insufficient funds leave all state unchanged. Spending never removes earned tickets, keys, global holders or cheat provenance. The API has no filesystem access or runtime-owner callback; callers must eventually use the established dirty/flush and deterministic command-state contracts.

Version 1 profiles with legacy scalar spending continue to serialize without a purchase field. Known purchases use version 2 with required identity/cost records, defensive copies, unique identities and a known-cost sum bounded by total spending. Any difference between total and known spending remains unknown legacy history; no identities are inferred. New readers accept both versions. Older strict readers deliberately reject version 2, so downgrading after known purchases is unsupported. This bounded foundation does not automatically upgrade any active profile.

The synthetic tests cover exact funds, repeat/no-funds/invalid operations, earned/key/holder/provenance preservation, deterministic versioned roundtrips, legacy spending, copy isolation and malformed envelopes. These tests require current-head C# CI; source review is not execution evidence.

Remaining activation gates include the captured wallet view for replay/co-op, old park snapshot provenance without double charging, global owner dirty/flush failure handling, UI/research consistency and the established bought-object behavior after a placement handler fails. No native profile export, original visual equivalence or UI-051 retirement is claimed.

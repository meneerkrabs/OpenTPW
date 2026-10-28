## Keep a pending offer decline across a park save (October 28, 2026)

A deleted or expired challenge offer is hidden for 500 ms before its normal
removal issues the decline command. Saving during that interval used to omit the
hidden row while retaining the unanswered objective, so loading could repost the
offer instead. The own-format save now optionally records the remaining release
delay of a hidden offer that still names that unanswered objective. Loading keeps
it hidden and occupied, with no message cue or immediate command; normal delayed
release still performs the identity- and rights-checked decline.

Offer rows bind to the command layer's existing definition-and-offered-day
identity during the current runtime. Cleanup of an already answered offer is
inert and is not saved, and a pending removal cannot rebind to a newer follow-up.
Visible legacy rows still have no saved offer identity. The original 500 ms runtime delay,
co-op guest/read-only restrictions and exclusion of the host's tube from join
snapshots remain. Hidden terminal notifications are still omitted.

`OfferReleaseDelayMs` is optional, valid only on an offer, and bounded to 0–500;
zero is retained for a removal already due. The world envelope also requires an
unanswered objective. Rows without the field keep their old bytes, and the #888
legacy converter remains strict. Older strict readers cannot load a new save
that contains the field; no downgrade-compatibility claim is made.

Synthetic regression tests cover deletion and expiry, whole and sliced world
loads, re-saving and overdue delay, exact-once release, hidden occupancy, stale
follow-ups, ownership restrictions, malformed input and silent restoration.
The C# SDK is unavailable locally, so compilation and MSTest were not run;
exact-head CI is required. No original executable or game media was used.

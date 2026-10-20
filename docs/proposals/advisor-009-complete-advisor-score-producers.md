# ADVISOR-009: Complete advisor score producers

## Current scope (2026-10-19)

The text below is the retained 2026-10-18 proposal snapshot. Its “current residual” counts and plan-only/draft wording describe that historical baseline, not the current implementation. The current [advisor inventory](../reverse/ADVISOR-SCAN.md) has nine evidence-gated scan callbacks and three evidence-gated help routes remaining. This PR's bounded correction classifies the existing first-challenge-offer message 344 as event/wired and updates the inventory regression and documentation; it does not implement the remaining routes or retire ADVISOR-009.

## Status and classification
- Classification: approximation.
- Plan-only local proposal: no runtime implementation, annotation retirement, publication or merge performed.
- Snapshot supplied as main 5ccddcab (export has no .git; commit not independently verified).
- Existing PR: none mapped for this independent residual.
- Draft eligibility: evidence/scope gates below must remain visible; this is not a completed feature.

## Current residual
--advisor-say plays global clips by number and starts them at its first rendered frame; the controller says the messages its producers post: the game events (0, 106, 128, 129, 323), the pranks (125-127), the challenge system's short-time warning (271), the mechanics' message (70), the ride breakdown lines (66 imminent, 68 and 69 for the mechanics' state, 258 condemned), the staff strike messages (33-57), the research completions (93-97), the new research groups (133-136), all research done in Instant Action (167), the global golden ticket moves (234-237) and the help messages of the HUD windows, panels and tools OpenTPW has (the buy, hire staff, all attractions, loans, finance, park information, all visitors, ticket price, research, employee, visitor and map windows, the object windows of rides, shops, sideshows, features and add-ons, the message, camera, locator and coaster panels, the path, queue, patrol, build, Buy Land and Clear Land tools and the Place tool of a hire); all 351 descriptors of the table 0x76E300 are bound as data and every producer is mapped (docs/reverse/ADVISOR-SCAN.md, content/data/advisor-producers.toml), the background scan (0x59C680, one scanned descriptor per update, the ones whose first word is 0) is ported (AdvisorScan, paced by the park's turn counter: ADVISOR-017) with the money callbacks 100-105 and 107 (0x59E8D0-0x59EB80, read over EconomyMoneyState: the net worth is read fresh and the offers' stored credit tests are not rewritten, which the loan list rebuilds before it reads) and the 31 park callbacks of the rides, cleanliness and guest mood batches (3-10, 60, 62, 71, 73-75, 77-79, 81, 82, 85-89, 145, 158, 159 and 259-262: AdvisorParkCallbacks over EconomyParkState, read-only; the object and hire stamps behind the days-since tests (0x4C9070, 0x4C90D0) are the advisor's own record of the newest construction and hire, kept from the build and hire events and saved in its snapshot, and derived at attach from the newest standing object and employed member for a loaded or original-save park because the save's park words are not parsed, and the helpers' block and toilet caches are recounted fresh); the other 117 scanned messages score 0 until their callbacks are ported: 21 of them are the original's constant-zero stub (0x5D60C0) and 96 compute their score from the park's state through park-statistics helpers (0x4C7BB0-0x4D0A00, 0x519450-0x5195D0), and game code posts the advice 215-233, 311 and 315 with such scores (167 outside Instant Action needs the per-park research flags of the player ledger, 0x5B0C40); help messages without a window or tool here: 285 (the All Staff window 0x496620), 308 (the address book 0x4A8140) and 307 (the postcard window 0x4A8C00 posts it only outside game type 1, where OpenTPW's online-only postcard never runs), and 36 help ids that no call of 0x486B00 or 0x59B590 names

## Source inspection
- `source/OpenTPW/World/Advisor.cs:256`: 257: if ( language != null ); 258: {; 259: var bankPath = language.ResolveDataFile( "global/Speech/speechHD.SDT" ); 260: ?? throw new FileNotFoundException( $"No global speech bank for {language.Name}." );

## Bounded work
Inventory the 96 non-stub unported score callbacks and unbound help/game posts; stage implementations by callback family.
- Preserve already-implemented behavior described above; restrict future changes to the named residual.
- For evidence-only items, establish an oracle first and file implementation only if a behavioral difference is proven.

## Missing evidence
Decode inputs, score formulas and producer reachability; distinguish 21 constant-zero stubs from missing logic.
- Source annotations record prior reverse engineering; this audit checked adjacent implementation but did not execute the original.

## Acceptance cases
1. Drive each selected score callback just below/at/above its threshold and assert message/score.
2. Assert unimplemented or unreachable help IDs never become arbitrary generic speech; preserve 31 existing park callbacks.
3. Add a reproducible synthetic fixture or reference trace identifying input, expected output, and affected function; a passing build alone is insufficient.
4. Re-run focused regression tests for the listed source owner and document what remains unproven.

## Dependencies and ownership
- World/audio implementation owner to be assigned; no runtime ownership claimed.
- Original-executable or reference-capture evidence is unavailable in this read-only source audit; unresolved claims are hypotheses.

## Review boundary
- Stop at a reviewable design/evidence package; this draft authorizes no tag removal or parity claim.
- Do not publish original media or private game data in fixtures, screenshots, or commits.
- A future implementation needs its own reviewed evidence and acceptance results; do not treat this proposal as merge-ready.


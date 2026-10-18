# AUDIO-038: Trace out-of-range voice resumption

## Status and classification
- Classification: approximation.
- Plan-only local proposal: no runtime implementation, annotation retirement, publication or merge performed.
- Snapshot supplied as main 5ccddcab (export has no .git; commit not independently verified).
- Existing PR: none mapped for this independent residual.
- Draft eligibility: evidence/scope gates below must remain visible; this is not a completed feature.

## Current residual
the voice pool of 12 (tp.exe 0x6b62b0) ranks the held chains (screams) with the running sentences, placed sounds and one-shots of the event system by priority and ground distance (0x6bb9f0), a voice entering only while its rank exceeds the pool's lowest (0x6b6bc0) and among equal ranks the newer voice keeping its slot (0x6b6a80 links a new voice at the head of the voice list); a voice out of range without the any-distance flag (event flag 0x200, voice flag 8 at 0x6bca44) has its sample stopped by mixer message 3 (0x6bca70 -> 0x6b8d60 -> 0x6bb7c0) and its handle at +0x24 left in place, and is not ticked: OpenTPW stops one-shots and sentence segments, and a chain starts no new child, but a chain's children already playing play out and looping voices are not stopped (nothing read restarts a stopped loop); how a stopped sentence resumes is not read and voices the mixer plays outside the event system, including the children a chain makes, do not compete

## Source inspection
- `source/OpenTPW/Audio/SoundEvents.cs:157`: 158: private void StopVoicesOutOfRange(); 159: {; 160: foreach ( var (voice, _, flags, _) in oneShots ); 161: if ( !voice.Loops && voice.Position != null && voice.Range > 0 && (flags & AnyDistanceFlag) == 0 && !Audible( voice.Position, voice.Range ) )
- `source/OpenTPW/Audio/SoundEvents.cs:1061`: 1062: if ( OutOfRange ); 1063: {; 1064: if ( voice is { IsFinished: false } playing ); 1065: {

## Bounded work
Replace silent loop advance and sentence-next-segment assumptions only after determining stop/restart lifecycle; include already-playing chain children.
- Preserve already-implemented behavior described above; restrict future changes to the named residual.
- For evidence-only items, establish an oracle first and file implementation only if a behavioral difference is proven.

## Missing evidence
Find original restart path preserving handle +0x24 and whether stopped sentence resumes or advances.
- Source annotations record prior reverse engineering; this audit checked adjacent implementation but did not execute the original.

## Acceptance cases
1. Exit/re-enter waterfall range mid-loop and compare playback phase, handle lifetime and restart count.
2. Move out during a sentence and held chain; assert child stop behavior and resumed segment against trace.
3. Add a reproducible synthetic fixture or reference trace identifying input, expected output, and affected function; a passing build alone is insufficient.
4. Re-run focused regression tests for the listed source owner and document what remains unproven.

## Dependencies and ownership
- World/audio implementation owner to be assigned; no runtime ownership claimed.
- Original-executable or reference-capture evidence is unavailable in this read-only source audit; unresolved claims are hypotheses.

## Review boundary
- Stop at a reviewable design/evidence package; this draft authorizes no tag removal or parity claim.
- Do not publish original media or private game data in fixtures, screenshots, or commits.
- A future implementation needs its own reviewed evidence and acceptance results; do not treat this proposal as merge-ready.

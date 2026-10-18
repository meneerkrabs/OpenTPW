# AUDIO-040: Plan SoundQuality fade mode

## Status and classification
- Classification: missing.
- Plan-only local proposal: no runtime implementation, annotation retirement, publication or merge performed.
- Snapshot supplied as main 5ccddcab (export has no .git; commit not independently verified).
- Existing PR: none mapped for this independent residual.
- Draft eligibility: evidence/scope gates below must remain visible; this is not a completed feature.

## Current residual
FADEOBJ on a script's sound records stops the sound exactly as KILLOBJ does (tp.exe 0x5523D0 -> 0x51C300 -> 0x6B8930 takes the stop call 0x6B89B0 unless bit 0x400 of the sound library's settings word is set), which is the original's behaviour at SoundQuality 0: the bit is `SoundQuality > [0x785920]` (0x51B920, sent from the options apply 0x4237F0 and the sound start 0x54DE0E) and the threshold and the default [0x78591C] are never written (BSS), so the bit is clear in a fresh install; with SoundQuality above 0 the call is the voice's vf+0x30 (0x6B7130), which only stores 0x3C as the fixed volume (placeholder +0x44) and sets the flag 0x400, the manager tick (0x6B62B0) re-applies 60 % to the playing sample (vf+0x2C, 0x6BCB80, while the manager byte +0x50 is set) and the voice, whose record is freed, plays on dimmed instead of ending; OpenTPW has no SoundQuality option

## Source inspection
- `source/OpenTPW/World/Objects/OriginalObjectRuntime.HeldSounds.cs:70`: 71: internal void EndHeldSounds( int scriptId, int slot ); 72: {; 73: for ( var index = heldSounds.Count - 1; index >= 0; index-- ); 74: {

## Bounded work
Add explicit SoundQuality-dependent FADEOBJ behavior: zero stops, positive quality retains voice at fixed 60 percent.
- Preserve already-implemented behavior described above; restrict future changes to the named residual.
- For evidence-only items, establish an oracle first and file implementation only if a behavioral difference is proven.

## Missing evidence
Confirm option persistence and manager update/lifetime semantics for a freed held record.
- Source annotations record prior reverse engineering; this audit checked adjacent implementation but did not execute the original.

## Acceptance cases
1. At quality 0 both KILLOBJ and FADEOBJ stop immediately.
2. At quality 1 FADEOBJ removes record but voice continues at 60 percent; KILLOBJ still stops and later script cleanup does not double-free.
3. Add a reproducible synthetic fixture or reference trace identifying input, expected output, and affected function; a passing build alone is insufficient.
4. Re-run focused regression tests for the listed source owner and document what remains unproven.

## Dependencies and ownership
- World/audio implementation owner to be assigned; no runtime ownership claimed.
- Original-executable or reference-capture evidence is unavailable in this read-only source audit; unresolved claims are hypotheses.

## Review boundary
- Stop at a reviewable design/evidence package; this draft authorizes no tag removal or parity claim.
- Do not publish original media or private game data in fixtures, screenshots, or commits.
- A future implementation needs its own reviewed evidence and acceptance results; do not treat this proposal as merge-ready.

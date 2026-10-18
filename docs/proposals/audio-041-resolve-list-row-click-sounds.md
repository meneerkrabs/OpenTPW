# AUDIO-041: Resolve list-row click sounds

## Status and classification
- Classification: approximation.
- Plan-only local proposal: no runtime implementation, annotation retirement, publication or merge performed.
- Snapshot supplied as main 5ccddcab (export has no .git; commit not independently verified).
- Existing PR: none mapped for this independent residual.
- Draft eligibility: evidence/scope gates below must remain visible; this is not a completed feature.

## Current residual
New residual reported from implementation PR #448: original list-row notifications 0x400–0x406 may be silent while UiScrollList still plays event 0x1F.

## Source inspection
- `source/OpenTPW/UI/Original/UiTextWidgets.cs:191`: 191: GameAudio.PlayUi( GameAudio.ClickEvent );; 192: RowActivated( Selected );; 193: }; 196: public int RowAt( UiCanvas canvas, System.Numerics.Vector2 point )

## Bounded work
New residual from #448: distinguish row messages 0x400-0x406 from activation 0x100; remove unconditional row click only if trace proves silence.
- Preserve already-implemented behavior described above; restrict future changes to the named residual.
- For evidence-only items, establish an oracle first and file implementation only if a behavioral difference is proven.

## Missing evidence
Trace original list procedure dispatch and global sound hook for selected row, child buttons and repeated selection.
- Source annotations record prior reverse engineering; this audit checked adjacent implementation but did not execute the original.

## Acceptance cases
1. Click an ordinary list row and assert no 0x1F if only messages 0x400-0x406 reach hook.
2. Row child activation still chooses receiver style 0x1F/0xBD; keyboard selection and double click do not duplicate sound.
3. Add a reproducible synthetic fixture or reference trace identifying input, expected output, and affected function; a passing build alone is insufficient.
4. Re-run focused regression tests for the listed source owner and document what remains unproven.

## Dependencies and ownership
- World/audio implementation owner to be assigned; no runtime ownership claimed.
- Original-executable or reference-capture evidence is unavailable in this read-only source audit; unresolved claims are hypotheses.
- Rebase evidence after #448; row-message residual is distinct from receiver-style activation.

## Review boundary
- Stop at a reviewable design/evidence package; this draft authorizes no tag removal or parity claim.
- Do not publish original media or private game data in fixtures, screenshots, or commits.
- A future implementation needs its own reviewed evidence and acceptance results; do not treat this proposal as merge-ready.

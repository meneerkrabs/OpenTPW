# COAST-032: Match remaining x87 operation order

## Status and classification
- Classification: approximation.
- Plan-only local proposal: no runtime implementation, annotation retirement, publication or merge performed.
- Snapshot supplied as main 5ccddcab (export has no .git; commit not independently verified).
- Existing PR: none mapped for this independent residual.
- Draft eligibility: evidence/scope gates below must remain visible; this is not a completed feature.

## Current residual
COAST-PRECISION: the thread's x87 runs at 24 bits ([BIN:TPW-EXE:0x00563914 control word]: the CRT's 53 bits, then DDSCL_FPUSETUP (0x851 at 0x563914, 0x808 at 0x563B92) and the shipped D3DIM CreateDevice set CW 0x7F and nothing restores it; the renderer 0x576EC0 only saves and restores it around itself, see X87Precision), so every PC multiply, add, subtract and square root rounds to a single, which the single-precision C# arithmetic does one operation at a time; built that way from the instruction sequences: the pylon cost 0x447830 with its height 0x4346a0 (length x f32 0.1, unit, rate x height), the suspended swing 0x43C76C (rotations, atan2 difference, wraps, braking), the sine-table cosine index 0x43C7D0 and the loop knot factor 0x432430; not yet read at instruction level on the PC side: the knot chord 0x432840 and the knot norms and tangents (0x430ED0, 0x431660, 0x432298 as C# keeps them in single steps), and the self-intersection pass (0x439AB0 -> 0x43A2B0), which still carries its x87 intermediates in double and rounds at the original's stores, whose sequence at 24 bits rounds every operation

## Source inspection
- `source/OpenTPW/World/Coasters/CoasterCurve.cs:134`: 135: public static float Chord( SVector3 a, SVector3 b ); 136: {; 137: var dx = b.X - a.X;; 138: var dy = b.Y * ChordHeightWeight - a.Y * ChordHeightWeight;
- `source/OpenTPW/World/Coasters/CoasterCurve.cs:145`: 146: public static (SVector3 Back, SVector3 Forward) KnotControls( SVector3 position, SVector3 direction, float backChord, float forwardChord, float backFactor, float forwardFactor ); 147: {; 148: var norm = direction.Length();; 149: var back = backFactor * backChord / norm;
- `source/OpenTPW/World/Coasters/CoasterTrack.cs:1038`: 1039: public static SVector3 ForwardTangent( SVector3 previous, SVector3 position, SVector3 next, float curviness, float straightDivisor, float? straightDot, bool loopPylon, bool loopNeighbour = false, bool reversed = false ); 1040: {; 1041: var a = previous - position;; 1042: var b = next - position;

## Bounded work
Port remaining knot/chord/tangent and collision arithmetic to proven 24-bit step sequence; own COAST-014 residual.
- Preserve already-implemented behavior described above; restrict future changes to the named residual.
- For evidence-only items, establish an oracle first and file implementation only if a behavioral difference is proven.

## Missing evidence
Instruction-level traces for 0x432840/0x430ED0/0x431660/0x432298 and collision 0x439AB0..0x43A2B0.
- Source annotations record prior reverse engineering; this audit checked adjacent implementation but did not execute the original.

## Acceptance cases
1. Use cancellation/near-collinear input vectors with expected per-operation single rounding.
2. Compare collision threshold neighbors and tangent/sampler coordinates bitwise where deterministic; keep already-matched cost and swing unchanged.
3. Add a reproducible synthetic fixture or reference trace identifying input, expected output, and affected function; a passing build alone is insufficient.
4. Re-run focused regression tests for the listed source owner and document what remains unproven.

## Dependencies and ownership
- World/audio implementation owner to be assigned; no runtime ownership claimed.
- Original-executable or reference-capture evidence is unavailable in this read-only source audit; unresolved claims are hypotheses.

## Review boundary
- Stop at a reviewable design/evidence package; this draft authorizes no tag removal or parity claim.
- Do not publish original media or private game data in fixtures, screenshots, or commits.
- A future implementation needs its own reviewed evidence and acceptance results; do not treat this proposal as merge-ready.

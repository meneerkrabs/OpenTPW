# TOUR-011: Match walker frame progression

## Implementation update (October 20, 2026)

A bounded implementation now makes scripted WALK poses consume the visitor-owned last emitted frame already requested by the shared pose phase. An absent emitted frame stays hidden, rather than falling back to elapsed-time animation. Provider-free geometry helpers retain their legacy fallback. Synthetic regression coverage includes finite gesture/cheer prefixes and stand tails, BOUNCE-before-WALK ordering, due equality, read-only repeated collection and same-epoch body-history JSON continuation. No movement, scheduler, VM or save-schema changes are included.

Independent source review passed. Python fidelity tests (13) and progress tests (10), their consistency checks and whitespace checks passed. C# compilation and execution await CI; no original visual equivalence or full TOUR-011 completion is claimed. Clock, terrain, movement and attachment residuals remain. The following is the original proposal, retained as historical scope, not current implementation status.

## Original proposal status and classification
- Classification: approximation.
- Plan-only local proposal: no runtime implementation, annotation retirement, publication or merge performed.
- Snapshot supplied as main 5ccddcab (export has no .git; commit not independently verified).
- Existing PR: none mapped for this independent residual.
- Draft eligibility: evidence/scope gates below must remain visible; this is not a completed feature.

## Current residual
TOUR-WALKON: the walk table itself and the walking bodies are the original's (RideVisitorBridge: WALKON 0x00556F40 takes the first free entry of the header walk size, sets state 1 for ftol(the distance between its nodes u1, class 0x800, and u2, class 0x80 for the head action 4, else 0x800) x 100 ms, 100 ms for 0, and the heading word atan2(z2 - z1, x2 - x1) as a rounded eighth of a turn (0x005570C2); the walk update 0x00557AB0 that the main loop pass 0x0054E360 runs at 0x0054FA08, once per rendered frame after the park's substeps, then 0x00557D80, moves 1 to 2 when the end has passed (the head action 4 hangs the head on head id u2, 0x0044B410; action 2 creates effect 0x13 at u2) and a WALKOFF 0x005571A0 entry from 3 to 4 after ftol(the distance between u3 and u4) x 100 ms, and WALKGET 0x00557110 hands out the first entry in state 4 of the table), and the update draws each entry's body through 0x005580A0 (WalkerBodies, FlatRideBounceRiders.CollectWalkers: the nodes' positions by the elapsed share, template 1 walking while walking on or off and ready, on board the head actions 1, 2 and 4 draw none, the actions 3, 5 and 6 template 3, 8 and 5 at u2 with the heading of u2's orientation, and the walk float of the script swings the body in template 2), but the templates' frame steppers (0x0074DB60.. through the handlers at 0x00476940 and 0x00476020) are not decoded, so every body steps a frame each 100 ms of its state, the terrain height of a body whose last WALKON word has bit 0 clear (0x004511A0; every shipped script sets it) is not modelled, the state changes run in the ride's host step where the original runs the update once per rendered frame (so a lagging frame's share above 1000, which the original clamps to 1000 and then moves to the next state in the same pass, 0x00557E56/0x00558010, is not drawn), the engine's X and Y are taken for the original's x and z in the headings and the node flag 0x10 (which negates the orientation) is not read, and a node missing from the model makes the original log (0x00556D2C) and leave its stack words as they were, where OpenTPW takes no walk-on time, the ride's default walk-off time (1000 ms for a tour ride, else none) and draws no body — evidence needed: the stepper handlers 0x00476940 and 0x00476020 (the frame interval of each template) and a capture of an original walk-on

## Source inspection
- `source/OpenTPW/World/Guests/BounceRiders.cs:123`: 124: internal static void CollectWalkers( Dictionary<int, BounceRiderPose> poses, OriginalObjectRuntime runtime, Func<int, System.Numerics.Matrix4x4> nodeWorld, List<RideVisitorBridge.WalkerView> scratch ); 125: {; 126: if ( runtime.Script is not { } script ); 127: return;
- `source/OpenTPW/World/Guests/RideVisitorBridge.cs:1350`: 1351: if ( guest <= 0 ); 1352: return true;; 1353: var capacity = Math.Max( 0, call.VM.Script.WalkSize );; 1354: var slot = FreeWalkSlot( capacity );
- `source/OpenTPW/World/Guests/WalkerBodies.cs:102`: 103: public static int StepOf( double now, double began ) => Math.Max( 0, unchecked( (int)now - (int)began ) / 100 );; 107: public static bool HidesOnBoard( int action ) => action is 1 or 2 or 4;; 111: public static int Template( int action ) => action switch { 3 => 3, 5 => 8, 6 => 5, _ => 0 };; 112: }

## Bounded work
Decode template steppers, render-frame state transitions, terrain mode and node-orientation flag; coordinate current tour owner.
- Preserve already-implemented behavior described above; restrict future changes to the named residual.
- For evidence-only items, establish an oracle first and file implementation only if a behavioral difference is proven.

## Missing evidence
Trace 0x476940/0x476020 and capture original walk-on; undefined missing-node stack is not deterministic behavior to invent.
- Source annotations record prior reverse engineering; this audit checked adjacent implementation but did not execute the original.

## Acceptance cases
1. Lagging frame crosses walk-end: clamp share to 1000 and transition in same rendered pass, not host-substep cadence.
2. Bit-0-clear walker samples terrain and node flag 0x10 reverses orientation; template-specific intervals replace universal 100ms only with evidence.
3. Add a reproducible synthetic fixture or reference trace identifying input, expected output, and affected function; a passing build alone is insufficient.
4. Re-run focused regression tests for the listed source owner and document what remains unproven.

## Dependencies and ownership
- Claude currently owns particle/tour runtime. This document is planning only; agree file boundaries before runtime work.
- Original-executable or reference-capture evidence is unavailable in this read-only source audit; unresolved claims are hypotheses.
- Coordinate with Claude, reconcile merged #425 and #435 before any runtime work.

## Review boundary
- Stop at a reviewable design/evidence package; this draft authorizes no tag removal or parity claim.
- Do not publish original media or private game data in fixtures, screenshots, or commits.
- A future implementation needs its own reviewed evidence and acceptance results; do not treat this proposal as merge-ready.

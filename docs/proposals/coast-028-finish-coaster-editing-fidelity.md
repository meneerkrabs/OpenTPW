# COAST-028: Finish coaster editing fidelity

## Status and classification
- Classification: approximation.
- Plan-only local proposal: no runtime implementation, annotation retirement, publication or merge performed.
- Snapshot supplied as main 5ccddcab (export has no .git; commit not independently verified).
- Existing PR: none mapped for this independent residual.
- Draft eligibility: evidence/scope gates below must remain visible; this is not a completed feature.

## Current residual
COAST-TOOL: the panel of nine buttons, their enable mask, the press/drag/release handlers (height 0x441a00, rotate 0x441e80, tilt 0x442010/0x441cd0, move 0x443340/0x445a70, stack swaps 0x4423f0/0x4425c0, demolish 0x446740), the drag rates, the tilt drag's camera yaw (0x443371) and the placement area's cell test (0x5243a6) are the original's; approximated: the drag counts the pointer in 1024-pixel units, a pylon is picked by its cell (a stack by the nearest projected piece) where the original picks the pylon model under the pointer (0x443ee0 maps the picked model handle through the table 0x7a4610 to the node, skipping cell descriptors with flag 1), without the pylon model (no game data) the hand-set yaw and incline turn the knot direction by a guessed angle, the panel appears at once instead of sliding out at a window position measured from the video (the table 0x751530 holds none; the handler 0x4982a0 is the panel's message handler, its builder 0x498790 hands the table to the window code 0x65fd0e), the pylon capabilities come from the models' clips (a generic coaster takes the defaults), the loop button is hidden below ride level 1 as the original's (0x4980D6, 0x49880F) and also on a coaster without a loop pylon control; the area's texture is generated (the dynamic texture table 0x763b3c lists blue, red, orange, m_enter, m_exit and so on; orange.tga is not on the PC CD and the slot of face type 0x11f is not traced); a drag refits the track on the release where the original rebuilds every frame (the mesh and the crossing markers follow the drag live here, the samples, the rating and the trains are refitted on the release); Backspace deleting the last pylon is an extension ([EXT:coaster-tool-controls]); the coaster click refuses no pylon for money

## Source inspection
- `source/OpenTPW/Hud/ParkHud.CoasterPanel.cs:15`: 16: private const float CoasterPanelDx = -372, CoasterPanelDy = 232;; 18: private static UiRect CoasterRect( float left, float top, float right, float bottom ) => Rect( left + CoasterPanelDx, top + CoasterPanelDy, right + CoasterPanelDx, bottom + CoasterPanelDy );; 21: private static readonly UiRect CoasterArmRect = Rect( 405, 1090, 1360, 1445 );; 22: private static readonly UiRect CoasterLeftBox = CoasterRect( 859, 916, 1003, 1190 );
- `source/OpenTPW/World/Coasters/CoasterBuilder.cs:318`: 319: private void AfterEdit( bool deferRebuild ); 320: {; 321: Track.Revalidate( Site, Ride.World, Ride );; 322: if ( Track.IsRunnable && !deferRebuild )
- `source/OpenTPW/World/Coasters/CoasterToolPanel.cs:237`: 238: public IReadOnlyList<int> NodesAt( int x, int y ) => Enumerable.Range( 0, Track.Nodes.Count ); 239: .Where( index => !Track.Nodes[index].IsPylonless && Track.Nodes[index].X == x && Track.Nodes[index].Y == y ); 240: .OrderBy( index => Track.Nodes[index].StackIndex ).ToList();; 244: private sealed class DragState
- `source/OpenTPW/World/Coasters/CoasterTrack.cs:1420`: 1421: private static SVector3 ApplyManualPose( CoasterNode node, SVector3 direction ); 1422: {; 1423: if ( (node.Flags & (CoasterNodeFlags.ManualYaw | CoasterNodeFlags.ManualIncline)) == 0 ); 1424: return direction;
- `source/OpenTPW/World/Original/BlueprintMarkers.cs:69`: 70: private static Texture AreaTexture(); 71: {; 72: const int Size = 8, Square = 4;; 73: var data = new byte[Size * Size * 4];

## Bounded work
Map model-based pylon picking, pointer units, drawer transition and live rebuild semantics; keep Backspace extension explicit.
- Preserve already-implemented behavior described above; restrict future changes to the named residual.
- For evidence-only items, establish an oracle first and file implementation only if a behavioral difference is proven.

## Missing evidence
Trace model-handle pick table, panel builder placement/animation and generated area texture slot.
- Source annotations record prior reverse engineering; this audit checked adjacent implementation but did not execute the original.

## Acceptance cases
1. Pick overlapping stacked pylons by visible model and test drag at two resolutions.
2. While dragging assert sample/rating/train updates at original cadence, then verify release does not apply twice.
3. Add a reproducible synthetic fixture or reference trace identifying input, expected output, and affected function; a passing build alone is insufficient.
4. Re-run focused regression tests for the listed source owner and document what remains unproven.

## Dependencies and ownership
- World/audio implementation owner to be assigned; no runtime ownership claimed.
- Original-executable or reference-capture evidence is unavailable in this read-only source audit; unresolved claims are hypotheses.

## Review boundary
- Stop at a reviewable design/evidence package; this draft authorizes no tag removal or parity claim.
- Do not publish original media or private game data in fixtures, screenshots, or commits.
- A future implementation needs its own reviewed evidence and acceptance results; do not treat this proposal as merge-ready.

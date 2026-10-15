using SVector3 = System.Numerics.Vector3;

namespace OpenTPW;

/// <summary>What the placement validator <c>0x36ba4</c> says about a cell, in the order it tests (COAST-plan §4.1).</summary>
public enum CoasterPlaceResult
{
	Ok,
	/// <summary>Test 1: x or y beyond the map width (+24) or height (+28).</summary>
	OutsideMap,
	/// <summary>Test 2 (new pylons only): the cell has no ground in the level map as it stood at game start.</summary>
	Blocked,
	/// <summary>Test 4: the cell of the previous node.</summary>
	SameCell,
	/// <summary>Test 5: d² below the pylon's squared minimum place distance.</summary>
	TooClose,
	/// <summary>Test 5: d² above the squared maximum place distance.</summary>
	TooFar,
	/// <summary>Test 6: the turn angle at the previous node is below the window's low limit (too sharp).</summary>
	TurnTooSharp,
	/// <summary>Test 6: the turn angle is above the window's high limit.</summary>
	TurnTooWide,
	/// <summary>Test 7: the cell's pylon belongs to another coaster.</summary>
	OtherCoaster,
	/// <summary>Test 7: the cell's pylon uses the other pylon control.</summary>
	OtherPylonKind,
	/// <summary>Test 7: the cell already holds <c>uiMaxStackedPylons</c> pylons.</summary>
	StackFull,
	/// <summary>Moving: the node after the moved one, or the one after that, would no longer validate (test 8).</summary>
	BreaksNextLink,
	/// <summary>The edit does not apply: the track is complete (reopen it first), closed, or the node cannot be moved.</summary>
	NotEditable,
	/// <summary>The coaster has no loop pylon control.</summary>
	NoLoopPylon,
	/// <summary>Completion: the circuit is not closed yet.</summary>
	NotClosed,
	/// <summary>Completion: a node is marked invalid (flag 0x4000).</summary>
	InvalidNodes,
	NotEnoughMoney
}

/// <summary>Node flags (node <c>+8</c>).</summary>
[Flags]
public enum CoasterNodeFlags : uint
{
	None = 0,
	/// <summary>The node's yaw (node <c>+0xE0</c>) was set by hand with the rotate button: the pylon and the knot direction turn to it instead of following the track (node flag 1, set by <c>0x441e80</c>).</summary>
	// [BIN:TPW-EXE:0x441e80 rotate drag] the rotate drag sets node flag 1 and writes the yaw to +0xE0 within the automatic yaw (+0xD4) ± the pylon's fMaxRotnAngleDeviance (+0x94)
	ManualYaw = 0x1,
	/// <summary>The node's incline (node <c>+0xDC</c>) was set by hand with the tilt button (node flag 2, set by <c>0x442010</c>).</summary>
	ManualIncline = 0x2,
	/// <summary>Auto-banking skips the node (<c>0x3360c</c> banks only when 0x4 is clear): the tilt button set the bank (node <c>+0xD8</c>) by hand (node flag 4, set by <c>0x442010</c>).</summary>
	// [BIN:TPW-EXE:0x442010 tilt drag] the tilt drag writes the bank to +0xD8 (range ±π, pylon flag 0x100) and sets node flag 4, the incline to +0xDC (pylon flag 0x80) and sets node flag 2
	NoAutoBank = 0x4,
	/// <summary>Set on both pylons of a loop pair (<c>0x434153..0x434162</c>: the first gets 0xe0, the second 0x60).</summary>
	LoopMark = 0x20,
	/// <summary>A loop node: the next link uses the LoopPylon limits; a pylon-less loop node's knot direction is (0, −1, 0) (no node flag 0x80).</summary>
	Loop = 0x40,
	/// <summary>The first pylon of a loop pair (<c>0x43415a</c>): its tangents and knot sides are mirrored, a loop pylon after it pays the LoopPylon rates, and with 0x40 it counts as a section of the rating's final.</summary>
	LoopFirst = 0x80,
	/// <summary>Forces the node bank to 0 (<c>0x3f584</c>); the PC sets it on every node of a stack but the top one (<c>0x433bd0</c>), which OpenTPW does not.</summary>
	ZeroBank = 0x100,
	/// <summary>A chain-lift (winch) section, set by the rating pass.</summary>
	Winch = 0x800,
	/// <summary>The node or the link after it failed the validator (shown as invalid; the edit is kept).</summary>
	Invalid = 0x4000,
	/// <summary>The station node at the departing end (S2) and the middle one (S1): no pylon stacks on a node with this flag (creation parameter 8).</summary>
	StationTail = 0x200,
	/// <summary>The station node at the arriving end (S0) and the middle one (S1): no pylon stacks on a node with this flag (creation parameter 0x10).</summary>
	StationHead = 0x400,
	/// <summary>The node has no previous neighbour: its tangent and the chain walk ignore the node before it (creation parameter 0x40; S0 and the station end node).</summary>
	NoPreviousLink = 0x80000,
	/// <summary>The node has no next neighbour: its tangent ignores the node after it (creation parameter 0x80; S2, which leaves straight along the platform).</summary>
	NoNextLink = 0x40000,
	/// <summary>The node's next neighbour for its tangent and its closing pair is the second node of the chain (creation parameter 0x100; the station end node).</summary>
	EndNextIsSecond = 0x100000,
	/// <summary>The node before the cut of a reopened circuit: its tangent ignores the next node (<c>0x446740</c> marks it 0x50000 = 0x10000 | no next link).</summary>
	CutEnd = 0x10000,
	/// <summary>The node after the cut of a reopened circuit: its tangent ignores the previous node (<c>0x446740</c> marks it 0xa0000 = 0x20000 | no previous link).</summary>
	CutStart = 0x20000,
	/// <summary>The build cursor's spare node, linked after the last node while the cursor hovers a cell (node flag 0x8000; the original adds 0x4000 when the cell fails the validator).</summary>
	// [BIN:TPW-EXE:0x435d50 cursor node] the spare node (controller +0x6c) gets 0x8000 as the cursor and 0x4000 when the validator 0x434740 refuses the hovered cell, otherwise 0x4000 is cleared
	Cursor = 0x8000
}

/// <summary>
/// One node of a coaster track (COAST-plan §3.3, §13.1, COAST-029): the three station nodes first (S0 and S2 are StdPylon nodes in the cells beyond the
/// station's '&lt;' and '&gt;' cells, S1 is pylon-less on the middle dummy), then the pylons in build order, then the station end node completion
/// appends. <see cref="Height"/> is the pylon height above the ground after the clamp, <see cref="T"/> the stored height parameter (node <c>+232</c>),
/// <see cref="StackIndex"/> the position in the cell's pylon stack, <see cref="Bank"/> the auto-bank (node <c>+216</c>). A pylon-less station node
/// stands on its <see cref="ExactPosition"/>; the station end node on the knot of S0.
/// </summary>
public sealed record CoasterNode( int X, int Y, float Height, float T, bool LoopPylon, int StackIndex, bool IsStation, bool IsStationEnd = false, SVector3? ExactPosition = null )
{
	public CoasterNodeFlags Flags { get; set; }
	public float Bank { get; set; }
	/// <summary>The loop-rotate parameter (node <c>+0xf8</c>): 0 for the first pylon of a loop pair, 0.5 for the second (<c>0x4341ec</c>); a pylon outside a pair takes the old default 0.5.</summary>
	public float LoopRotate { get; set; } = DefaultLoopRotateValue;
	internal const float DefaultLoopRotateValue = 0.5f;
	/// <summary>The yaw the rotate button set (node <c>+0xE0</c>, radians), in force while <see cref="CoasterNodeFlags.ManualYaw"/> is set.</summary>
	public float Yaw { get; set; }
	/// <summary>The incline the tilt button set (node <c>+0xDC</c>, radians in [−π, π]), in force while <see cref="CoasterNodeFlags.ManualIncline"/> is set.</summary>
	public float Incline { get; set; }
	/// <summary>A pylon-less node (descriptor flag bit 0): the middle station node and the station end node.</summary>
	public bool IsPylonless => IsStationEnd || ExactPosition != null;
	/// <summary>A node the player placed: not one of the station's nodes (descriptor flag 2) and not the station end node. Only these move, change height and are deleted.</summary>
	public bool IsPlayerPylon => !IsStation && !IsStationEnd;
}

/// <summary>The cells and heights a coaster track is checked against (map bounds, the no-ground cells, ground heights).</summary>
public sealed class CoasterSite
{
	public CoasterSite( int width, int height, Func<int, int, bool>? isBlocked = null, Func<int, int, float>? groundHeight = null )
	{
		Width = width;
		Height = height;
		IsBlocked = isBlocked ?? (( _, _ ) => false);
		GroundHeight = groundHeight ?? (( _, _ ) => 0f);
	}

	/// <summary>Map width (+24) and height (+28) in cells.</summary>
	public int Width { get; }
	public int Height { get; }
	/// <summary>Bit 0 of the cell flags in the map snapshot taken at game start: the cell has no ground.</summary>
	public Func<int, int, bool> IsBlocked { get; }
	/// <summary>Ground height of a cell in original world units (10 per cell).</summary>
	public Func<int, int, float> GroundHeight { get; }

	/// <summary>
	/// Freezes the no-ground cells of a grid as they are now (the validator's map snapshot): cells without a terrain
	/// surface or outside the terrain.
	/// </summary>
	// [BIN:STP-PPC:0x10036CD0 blocked cell] the validator's bit-0 test reads the map snapshot *0x66f78 (copied at level load and refreshed once at game start, 0x1011B478 → 0x10054EE0 → 0x1004DCB8), not the live map; bit 0 is the no-ground bit (height query → FLT_MAX at 0x1005052C); paths, queues, scenery and rides placed in play never set it
	public static CoasterSite FromGrid( IParkGrid grid )
	{
		var blocked = new bool[grid.Width * grid.Height];
		for ( var y = 0; y < grid.Height; y++ )
		{
			for ( var x = 0; x < grid.Width; x++ )
				blocked[y * grid.Width + x] = grid.CheckTerrain( x, y ) is OriginalPlacementResult.NoTerrainSurface or OriginalPlacementResult.OutsideTerrain;
		}
		var width = grid.Width;
		return new CoasterSite( grid.Width, grid.Height, ( x, y ) => blocked[y * width + x], ( x, y ) => grid.GetCellHeight( Math.Clamp( x, 0, grid.Width - 1 ), Math.Clamp( y, 0, grid.Height - 1 ) ) / ObjectPlacement.ModelScale );
	}
}

/// <summary>
/// A coaster's track (COAST-plan §9.1, §9.7): the station ends, the ordered nodes, the placement rules of
/// <c>0x36ba4</c> (which mark nodes invalid instead of refusing them), closure, completion, the height clamp of
/// <c>0x34d90</c>, the per-node cost of <c>0x4b168</c> and the node geometry of <c>0x33398</c>.
/// </summary>
public sealed class CoasterTrack
{
	/// <summary>Original world units per map cell (one cell is 10 MD2 units).</summary>
	public const float CellUnits = 10f;
	public const float HalfCell = 5f;
	/// <summary>The station's three nodes (S0, S1, S2) at the head of every track.</summary>
	public const int StationNodeCount = 3;

	/// <summary>The cell a world coordinate falls in: the truncation of x·0.1 (<c>0x436ac0</c> converts the dummy's position with ftol(x · 0.1)).</summary>
	// [BIN:TPW-EXE:0x436b9c station cell] the station nodes' cells are ftol(x · 0.1) and ftol(z · 0.1) of the dummy's world position (the constant at 0x6fdedc is 0.1)
	public static int CellOf( float coordinate ) => (int)MathF.Truncate( coordinate * 0.1f );
	/// <summary>The cost divisor (constant <c>-13300(r2)</c>): rates are per 10 world units.</summary>
	public const float CostUnitDivisor = 10f;
	/// <summary>Definition <c>+16</c>: the auto-bank divisor (fAutoBankDivisor is never loaded).</summary>
	// [BIN:STP-PPC:0x1002D2D0 coaster defaults] definition +16 = 4.0, the auto-bank divisor; no code reads settings offset 15060 (fAutoBankDivisor)
	public const float AutoBankDivisor = 4f;
	/// <summary>The loop-rotate parameter (node <c>+248</c>) of a loop pylon that is not part of a placed pair (a save from before the pair rule): 0.5.</summary>
	public const float DefaultLoopRotate = 0.5f;

	private readonly List<CoasterNode> nodes = new();

	public CoasterTrack( CoasterDefinition definition ) => Definition = definition ?? throw new ArgumentNullException( nameof( definition ) );

	public CoasterDefinition Definition { get; }
	/// <summary>The station end the track leaves from (shape '&gt;'): the cell before the station's last node S2.</summary>
	public (int X, int Y)? DepartCell { get; private set; }
	/// <summary>The station end the track returns to (shape '&lt;'): the cell after the station's first node S0.</summary>
	public (int X, int Y)? ReturnCell { get; private set; }
	public IReadOnlyList<CoasterNode> Nodes => nodes;
	/// <summary>Pylons the player placed after the station's three nodes.</summary>
	public int PylonCount => nodes.Count( node => node.IsPlayerPylon );
	/// <summary>Controller <c>+60</c> bit 1 clear: the chain's ends are linked.</summary>
	public bool IsClosed { get; private set; }
	/// <summary>Controller <c>+60</c> bit 0: completed (station end nodes appended).</summary>
	public bool IsComplete { get; private set; }
	/// <summary>
	/// The index of the node before the cut of a circuit the demolish button opened in the middle (controller <c>+60</c> bit 1, the open-circuit node <c>+0x60</c>); the node after it,
	/// <c>+0x64</c>, is the chain start the circuit closes on. Null while the circuit is whole or was reopened at its end (the chain then starts at S0).
	/// </summary>
	// [BIN:TPW-EXE:0x446740 cut range] the cut marks the node before the range 0x50000 and the one after it 0xa0000, removes the nodes between (0x444cb0 keeps the open-circuit pair +0x60 / +0x64 on the node before and after the last removed one) and sets controller flag 2; a cut that reaches the last node clears flags 1 and 2 instead (the track is incomplete again, 0x435530)
	public int? CutAfter { get; private set; }
	/// <summary>Whether the circuit is cut open in the middle (the build tool inserts new pylons into the gap).</summary>
	public bool IsCut => CutAfter != null;
	/// <summary>A coaster runs only when complete and closed (<c>0x45da4</c>) and not cut open.</summary>
	public bool IsRunnable => IsComplete && IsClosed && CutAfter == null;
	/// <summary>The index of the node new pylons follow: the last node, or the one before the cut.</summary>
	public int LastIndex => CutAfter ?? nodes.Count - 1;
	/// <summary>The list position a new pylon takes: after <see cref="LastIndex"/>.</summary>
	public int InsertIndex => CutAfter is int cut ? cut + 1 : nodes.Count;
	public bool HasInvalidNodes => nodes.Any( node => (node.Flags & CoasterNodeFlags.Invalid) != 0 );
	/// <summary>Incremented by every edit.</summary>
	public int Version { get; private set; }

	/// <summary>
	/// Sets the station ends and builds the station's three nodes (descriptor flag 2) from its model frame, removing any pylons: S0 a StdPylon node in the
	/// cell beyond '&lt;' (no previous link, node flag 0x400), S1 pylon-less on the middle dummy (0x200 | 0x400) and S2 a StdPylon node in the cell beyond '&gt;'
	/// (no next link, 0x200); S0 and S1 are chain lifts. The pylon heights are the dummies' heights and skip the fMinHeight/fMaxHeight clamp.
	/// </summary>
	// [BIN:TPW-EXE:0x436ac0 station nodes] S0: creation parameters 0x274 = node flags 0x400 | 0x1800 (winch) | 0x80000 (no previous link), bit 4 skips the height clamp (0x433bd0 → 0x433790), bit 0x200 registers the pylon in its cell; S1: 0x3c = 0x200 | 0x400 | 0x1800, the exact dummy position as knot (node +0x4c..+0x54); S2: 0x28c = 0x200 | 0x40000 (no next link); ordinals 0, 1, 2
	// [BIN:TPW-EXE:0x434060 node creation parameters] the constructor maps its parameter bits to node flags: 8 → 0x200, 0x10 → 0x400, 0x20 → 0x1800, 0x40 → 0x80000, 0x80 → 0x40000, 0x100 → 0x100000 (0x4343a6..0x4343eb); bit 0x200 puts the pylon in its map cell (0x452d30)
	// [APPROX:COAST-029] the middle node's winch mark is re-derived by the rating pass instead of kept from the creation parameter; the middle knot takes fTrackHeightAdjust like every pylon knot (only the Big Dripper's −1.5 is nonzero) where the original's raw descriptor position may not; and a station whose dummies lie below the StdPylon's h0 (Ghosta Coasta 2.04 against 4, Scatty Batty 3.5 against 5, Escape Velocity 12.04 against 30, Candy Coaster 7.09 against 10) stands on pylon end nodes raised to h0, exactly as the loft clamp t = clamp((h − h0)/(h1 − h0), 0, 1) says, so S0 and S2 sit above the middle node by h0 − h; whether the original's pylon pose and the terrain height under the pylon's cell leave it so has not been traced
	public void SetStation( (int X, int Y) depart, (int X, int Y) @return, CoasterStationLayout layout )
	{
		if ( depart == @return )
			throw new ArgumentException( "The station ends must differ.", nameof( @return ) );
		ArgumentNullException.ThrowIfNull( layout );
		DepartCell = depart;
		ReturnCell = @return;
		nodes.Clear();
		nodes.Add( StationPylon( layout.End, CoasterNodeFlags.StationHead | CoasterNodeFlags.Winch | CoasterNodeFlags.NoPreviousLink ) );
		nodes.Add( new CoasterNode( CellOf( layout.Mid.X ), CellOf( layout.Mid.Z ), 0, 0, false, 0, true, ExactPosition: new SVector3( layout.Mid.X, layout.Mid.Y, layout.Mid.Z ) )
		{ Flags = CoasterNodeFlags.StationTail | CoasterNodeFlags.StationHead | CoasterNodeFlags.Winch } );
		nodes.Add( StationPylon( layout.Start, CoasterNodeFlags.StationTail | CoasterNodeFlags.NoNextLink ) );
		IsComplete = IsClosed = false;
		Version++;
	}

	private CoasterNode StationPylon( CoasterStationPoint point, CoasterNodeFlags flags )
	{
		var (height, t) = StationHeight( Definition.StandardPylon, point.Y );
		return new CoasterNode( CellOf( point.X ), CellOf( point.Z ), height, t, false, 0, true ) { Flags = flags };
	}

	/// <summary>
	/// The height of a station pylon and its parameter t = clamp((h − h0)/(h1 − h0), 0, 1): the requested height is taken as it comes, without the
	/// fMinHeight/fMaxHeight clamp (a station node's creation parameter has bit 4).
	/// </summary>
	// [BIN:TPW-EXE:0x434060 node creation parameters] bit 4 of the creation parameters goes to 0x433bd0, which skips the fMinHeight/fMaxHeight clamp of 0x433790; the loft parameter is then clamp((h − h0)/(h1 − h0), 0, 1)
	public static (float Height, float T) StationHeight( CoasterPylonControl pylon, float requested )
	{
		var span = pylon.MeshHeightHigh - pylon.MeshHeightLow;
		var t = span == 0 ? 0 : Math.Clamp( (requested - pylon.MeshHeightLow) / span, 0f, 1f );
		return (PylonHeight( pylon, t ), t);
	}

	/// <summary>The pylon control that validates a link after <paramref name="previous"/> (test 3).</summary>
	// [BIN:STP-PPC:0x10036BA4 placement validator] the LoopPylon control applies after a node with flag 0x40 or while the loop edit mode bit 0x40 is set; otherwise the StdPylon
	public CoasterPylonControl ControlAfter( CoasterNode previous, bool loopMode ) =>
		Definition.Pylon( loopMode || (previous.Flags & CoasterNodeFlags.Loop) != 0 );

	/// <summary>
	/// The turn angle at <paramref name="previous"/> between the directions to <paramref name="next"/> and to
	/// <paramref name="before"/> (<c>0x36a50</c>): π means straight on. Single precision.
	/// </summary>
	// [BIN:STP-PPC:0x10036A50 turn angle] θ = acos(clamp(dot(û_new, û_before), −1, 1)) with both directions normalised in single precision
	public static float TurnAngle( (int X, int Y) next, (int X, int Y) previous, (int X, int Y) before )
	{
		float ax = next.X - previous.X, ay = next.Y - previous.Y;
		float bx = before.X - previous.X, by = before.Y - previous.Y;
		var la = MathF.Sqrt( ax * ax + ay * ay );
		var lb = MathF.Sqrt( bx * bx + by * by );
		var dot = MathF.FusedMultiplyAdd( ax / la, bx / lb, ay / la * (by / lb) );
		return MathF.Acos( Math.Clamp( dot, -1f, 1f ) );
	}

	/// <summary>
	/// The turn window at distance <paramref name="distance"/> (cells): t = clamp((d − min)/(max − min), 0, 1),
	/// lo = A(1 − t) + Bt and hi = C(1 − t) + Dt from the entrance (after the station node) or the normal limits.
	/// </summary>
	// [BIN:STP-PPC:0x10036F28 turn window] limits interpolated by the distance fraction t (clamped by data 0x3d058/0x3d05c) as fmadds(A, 1 − t, B·t) (0x10036FB4/B8, 0x10036FE4/E8); entrance limits (+116..+128) when the previous node is the station node, else +132..+144
	public static (float Low, float High) AngleWindow( CoasterPylonControl pylon, float distance, bool entrance )
	{
		var span = (float)(pylon.MaxPlaceDistance - pylon.MinPlaceDistance);
		var t = span == 0 ? 0 : Math.Clamp( (distance - pylon.MinPlaceDistance) / span, 0f, 1f );
		var (a, b, c, d) = entrance
			? (pylon.EntranceMinAngleAtMinDistance, pylon.EntranceMinAngleAtMaxDistance, pylon.EntranceMaxAngleAtMinDistance, pylon.EntranceMaxAngleAtMaxDistance)
			: (pylon.MinAngleAtMinDistance, pylon.MinAngleAtMaxDistance, pylon.MaxAngleAtMinDistance, pylon.MaxAngleAtMaxDistance);
		var rest = 1 - t;
		return (MathF.FusedMultiplyAdd( a, rest, b * t ), MathF.FusedMultiplyAdd( c, rest, d * t ));
	}

	/// <summary>The distance and turn tests (4–6) of one link from <paramref name="previous"/> to <paramref name="next"/>.</summary>
	// [BIN:STP-PPC:0x10036BA4 placement validator] d² = dx² + dy² in cells must lie in [min², max²] (+156/+160); the node is then flagged when |θ| < lo or |θ| > hi
	public static CoasterPlaceResult CheckLink( CoasterPylonControl pylon, (int X, int Y) next, (int X, int Y) previous, (int X, int Y)? before, bool entrance )
	{
		if ( next == previous )
			return CoasterPlaceResult.SameCell;
		var dx = next.X - previous.X;
		var dy = next.Y - previous.Y;
		var squared = dx * dx + dy * dy;
		// [BIN:TPW-EXE:0x4349a5 distance waiver] a link outside [min², max²] is rejected when the previous node's ordinal ([ebp]) is 2 and otherwise waived when its descriptor has flag 2 (0x4349af..0x4349b9): the waiver exempts the links that leave the station's internal nodes S0 and S1 (ordinals 0 and 1, created by 0x436ac0), which OpenTPW never validates; every link after S2 (ordinal 2) and after a pylon is checked (COAST-007 retired, COAST-029)
		if ( squared < pylon.MinPlaceDistanceSquared )
			return CoasterPlaceResult.TooClose;
		if ( squared > pylon.MaxPlaceDistanceSquared )
			return CoasterPlaceResult.TooFar;
		if ( before is not { } behind )
			return CoasterPlaceResult.Ok;
		var angle = MathF.Abs( TurnAngle( next, previous, behind ) );
		var (low, high) = AngleWindow( pylon, MathF.Sqrt( squared ), entrance );
		if ( angle < low )
			return CoasterPlaceResult.TurnTooSharp;
		if ( angle > high )
			return CoasterPlaceResult.TurnTooWide;
		return CoasterPlaceResult.Ok;
	}

	/// <summary>The point before node <paramref name="index"/> for its turn angle: the cell of the node before it (the first station node has none).</summary>
	// [APPROX:COAST-030] the original skips the turn test when the nodes carry 0x10000 together with 0x20000 in move mode (0x4348af..0x4348d8, the cut ends of a reopened circuit, set by 0x434060 on a node added to an open circuit), flags OpenTPW never sets — evidence needed: a capture of a cut and reconnected circuit
	private (int X, int Y)? Before( int index ) => index >= 1 ? (nodes[index - 1].X, nodes[index - 1].Y) : null;

	/// <summary>
	/// The validator <c>0x36ba4</c> for a new pylon after the last node (<paramref name="moving"/> null) or for node
	/// <paramref name="moving"/> moved to (x, y): bounds, no-ground cell (new pylons only), the pylon control, same
	/// cell, distance, turn window, stacking; moving also re-validates the next two links. The build tool marks the
	/// node with the result; it does not refuse it.
	/// </summary>
	// [BIN:STP-PPC:0x10094474 slope and deviance] slope (fMaxHeightDiffPerGSquareDist, pylon +112), loop rotation deviance (+152) and loop height difference (+172) are loaded but never read by the coaster code; fMaxRotnAngleDeviance (+148) only bounds the rotate slider 0x10046508
	public CoasterPlaceResult CheckPlacement( CoasterSite site, CoasterWorld? world, CoasterRide? owner, int x, int y, bool loopMode, int? moving = null )
	{
		if ( DepartCell == null || nodes.Count < StationNodeCount || (((IsComplete && !IsCut) || IsClosed) && moving == null) )
			return CoasterPlaceResult.NotEditable;
		if ( moving is int index && (index < StationNodeCount || index >= nodes.Count || !nodes[index].IsPlayerPylon) )
			return CoasterPlaceResult.NotEditable;
		if ( loopMode && !Definition.HasLoopPylon )
			return CoasterPlaceResult.NoLoopPylon;
		// [BIN:STP-PPC:0x10036BA4 placement validator] test 1: x < map width (+24) and y < map height (+28)
		if ( x < 0 || y < 0 || x >= site.Width || y >= site.Height )
			return CoasterPlaceResult.OutsideMap;
		if ( moving == null && site.IsBlocked( x, y ) )
			return CoasterPlaceResult.Blocked;
		var position = moving ?? InsertIndex;
		var previous = nodes[position - 1];
		var pylon = ControlAfter( previous, loopMode );
		var link = CheckLink( pylon, (x, y), (previous.X, previous.Y), Before( position - 1 ), entrance: previous.IsStation );
		if ( link != CoasterPlaceResult.Ok )
			return link;
		// [BIN:TPW-EXE:0x439ab0 track collision] the original tests the track against itself only (CoasterCollision, run on every rebuild): no terrain or scenery query exists in 0x439ab0, 0x439c40 or 0x43a2b0, so a pylon placement is never refused for what stands under or beside the track
		var stack = CheckStack( world, owner, x, y, loopMode, moving );
		if ( stack != CoasterPlaceResult.Ok )
			return stack;
		if ( moving is int moved )
		{
			// [BIN:STP-PPC:0x10037318 move re-validation] moving a node re-validates the following node and the one after it with the moved position (0x10037318, 0x100373B4)
			for ( var next = moved + 1; next <= moved + 2 && next < nodes.Count; next++ )
			{
				if ( !nodes[next].IsPlayerPylon )
					break;
				var after = nodes[next - 1];
				var from = next - 1 == moved ? (x, y) : (after.X, after.Y);
				var behind = next - 2 == moved ? (x, y) : next - 2 >= 0 ? (nodes[next - 2].X, nodes[next - 2].Y) : ((int X, int Y)?)null;
				var control = Definition.Pylon( next - 1 == moved ? loopMode : (after.Flags & CoasterNodeFlags.Loop) != 0 );
				if ( CheckLink( control, (nodes[next].X, nodes[next].Y), from, behind, entrance: after.IsStation ) != CoasterPlaceResult.Ok )
					return CoasterPlaceResult.BreaksNextLink;
			}
		}
		return CoasterPlaceResult.Ok;
	}

	/// <summary>Test 7: a cell that already holds pylons takes another only from the same coaster, with the same pylon control, below the stack limit.</summary>
	// [BIN:STP-PPC:0x10037104 pylon stacking] an occupying pylon (found by walking every coaster's descriptor list, 0x10056BAC) must belong to the same coaster (+20), use the same pylon control (+24) and have stack height (+48) < uiMaxStackedPylons (+204), with no object flags 0x600
	// [BIN:TPW-EXE:0x434d34 stacking on station nodes] the stacking test reads node flags 0x200 and 0x400 of the bottom node of the stack (0x434d34..0x434d3d, 0x434da8..0x434db1), which only the station nodes carry (creation parameters 8 and 0x10): S0 (0x400), S2 (0x200) and S1 (both); no pylon stacks on a station pylon, a placement there is refused as a full stack
	private CoasterPlaceResult CheckStack( CoasterWorld? world, CoasterRide? owner, int x, int y, bool loopMode, int? moving )
	{
		var count = 0;
		foreach ( var (ride, node) in PylonsAt( world, owner, x, y ) )
		{
			if ( moving is int index && ride == owner && ReferenceEquals( node, nodes[index] ) )
				continue;
			if ( ride != owner )
				return CoasterPlaceResult.OtherCoaster;
			if ( (node.Flags & (CoasterNodeFlags.StationTail | CoasterNodeFlags.StationHead)) != 0 )
				return CoasterPlaceResult.StackFull;
			if ( node.LoopPylon != loopMode )
				return CoasterPlaceResult.OtherPylonKind;
			if ( moving is int at && ride == owner && nodes.IndexOf( node ) > at )
				continue;
			count++;
		}
		return count >= Definition.Pylon( loopMode ).MaxStackedPylons ? CoasterPlaceResult.StackFull : CoasterPlaceResult.Ok;
	}

	private IEnumerable<(CoasterRide? Ride, CoasterNode Node)> PylonsAt( CoasterWorld? world, CoasterRide? owner, int x, int y )
	{
		if ( world != null )
			return world.PylonsAt( x, y );
		return nodes.Where( node => !node.IsPylonless && node.X == x && node.Y == y ).Select( node => (owner, node) );
	}

	/// <summary>
	/// Re-runs the validator over every link and sets the invalid flag (0x4000) on both ends of each failing link,
	/// clearing it elsewhere; a closed circuit also checks the link from the last pylon into the station.
	/// </summary>
	// [BIN:STP-PPC:0x10048334 invalid node] a failed validation sets node flag 0x4000 on the node and its predecessor (0x10048334..0x10048368); a passing one clears it (0x100482F8..0x1004832C); the node stays where it is
	public void Revalidate( CoasterSite site, CoasterWorld? world, CoasterRide? owner )
	{
		foreach ( var node in nodes )
			node.Flags &= ~CoasterNodeFlags.Invalid;
		for ( var index = StationNodeCount; index < nodes.Count; index++ )
		{
			var node = nodes[index];
			if ( !node.IsPlayerPylon )
				continue;
			var previous = nodes[index - 1];
			// The pair across the cut is no link: the node after the cut is the chain start the circuit closes on, and the closing check validates it.
			var acrossCut = CutAfter is int cut && index == cut + 1;
			var failed = site.IsBlocked( node.X, node.Y )
				|| (!acrossCut && CheckLink( ControlAfter( previous, node.LoopPylon ), (node.X, node.Y), (previous.X, previous.Y), Before( index - 1 ), previous.IsStation ) != CoasterPlaceResult.Ok)
				|| CheckStack( world, owner, node.X, node.Y, node.LoopPylon, index ) != CoasterPlaceResult.Ok;
			if ( failed )
			{
				node.Flags |= CoasterNodeFlags.Invalid;
				if ( !acrossCut )
					previous.Flags |= CoasterNodeFlags.Invalid;
			}
		}
		if ( IsClosed && CheckClosure() != CoasterPlaceResult.Ok && LastPylonIndex is int last )
			nodes[last].Flags |= CoasterNodeFlags.Invalid;
		Version++;
	}

	private int? LastPylonIndex
	{
		get
		{
			for ( var index = LastIndex; index >= StationNodeCount; index-- )
			{
				if ( nodes[index].IsPlayerPylon )
					return index;
			}
			return null;
		}
	}

	/// <summary>
	/// The requested height clamped to [max(fMinHeight, h0), fMaxHeight] (+104/+108) and its parameter
	/// t = clamp((h − h0)/(h1 − h0), 0, 1) (node <c>+232</c>). Heights are clamped, never refused.
	/// </summary>
	// [BIN:STP-PPC:0x10034D90 pylon builder] the height is clamped to [fMinHeight, fMaxHeight] (fMinHeight first raised to the mesh height h0 by 0x10030F20) and t = clamp((h − h0)/(h1 − h0), 0, 1) by 0x10036990 (clamps data 0x3d040/0x3d044)
	public static (float Height, float T) ClampHeight( CoasterPylonControl pylon, float requested )
	{
		var low = MathF.Max( pylon.MinHeight, pylon.MeshHeightLow );
		var height = Math.Clamp( requested, low, MathF.Max( low, pylon.MaxHeight ) );
		var span = pylon.MeshHeightHigh - pylon.MeshHeightLow;
		var t = span == 0 ? 0 : Math.Clamp( (height - pylon.MeshHeightLow) / span, 0f, 1f );
		return (height, t);
	}

	/// <summary>The pylon height of a stored parameter: h0 + t(h1 − h0) (<c>0x369bc</c>).</summary>
	// [BIN:STP-PPC:0x100369BC pylon height] height = fmadds(t, h1 − h0, h0) from the pylon mesh heights +188/+192 (0x100369C8)
	public static float PylonHeight( CoasterPylonControl pylon, float t ) => MathF.FusedMultiplyAdd( t, pylon.MeshHeightHigh - pylon.MeshHeightLow, pylon.MeshHeightLow );

	/// <summary>
	/// Adds a pylon after the last node of an open chain; returns it. A loop pylon comes in pairs: <paramref name="second"/> is the second of the pair
	/// (loop rotate 0.5, flags 0x60) and the first has loop rotate 0 and flags 0xe0 (<c>0x434060</c>).
	/// </summary>
	// [BIN:TPW-EXE:0x434153 loop pair] a node whose pylon control has flag 0x4000 gets node flags |0xe0 and loop rotate (+0xf8) 0 when it is the first of the pair, |0x60 and 0.5 when it is the second (the pair counter 0x79c664 is 2, then 1); a trough pylon (0x8000) sets neither
	internal CoasterNode Append( CoasterWorld? world, CoasterRide? owner, int x, int y, float requestedHeight, bool loop, bool second = false )
	{
		var pylon = Definition.Pylon( loop );
		var (height, t) = ClampHeight( pylon, requestedHeight );
		var stack = PylonsAt( world, owner, x, y ).Count();
		var flags = loop ? CoasterNodeFlags.Loop : CoasterNodeFlags.None;
		if ( loop && pylon.IsLoop )
			flags |= CoasterNodeFlags.LoopMark | (second ? CoasterNodeFlags.None : CoasterNodeFlags.LoopFirst);
		var node = new CoasterNode( x, y, height, t, loop, stack, false ) { Flags = flags, LoopRotate = loop && pylon.IsLoop && !second ? 0f : DefaultLoopRotate };
		// [BIN:TPW-EXE:0x436190 link cursor node] a new node is linked after the last node (the build reference, +0x60 while the circuit is cut open) and before the chain start the cut left (+0x64)
		var at = InsertIndex;
		nodes.Insert( at, node );
		if ( CutAfter != null )
		{
			CutAfter = at;
			SyncCutMarkers();
		}
		Version++;
		return node;
	}

	internal void Replace( int index, CoasterNode node )
	{
		nodes[index] = node;
		Version++;
	}

	/// <summary>
	/// Moves node <paramref name="index"/> to cell (x, y) and keeps the cells' stacks whole: the pieces above it in the cell it leaves come down one, and it takes the next stack index of the cell it
	/// enters (<paramref name="stackAtTarget"/>), or <paramref name="restore"/> and the pieces from that index up go up one when it returns to the cell it started in.
	/// </summary>
	// [BIN:TPW-EXE:0x442d70 leave stack] taking a node out of its cell's descriptor closes the gap in the stack array (0x442f40 → 0x442d70) and the node enters the new cell's descriptor on top (0x433bd0)
	internal void Relocate( int index, int x, int y, int stackAtTarget, int? restore )
	{
		var node = nodes[index];
		for ( var other = 0; other < nodes.Count; other++ )
		{
			if ( other != index && !nodes[other].IsPylonless && nodes[other].X == node.X && nodes[other].Y == node.Y && nodes[other].StackIndex > node.StackIndex )
				nodes[other] = nodes[other] with { StackIndex = nodes[other].StackIndex - 1 };
		}
		var stack = stackAtTarget;
		if ( restore is int start )
		{
			stack = start;
			for ( var other = 0; other < nodes.Count; other++ )
			{
				if ( other != index && !nodes[other].IsPylonless && nodes[other].X == x && nodes[other].Y == y && nodes[other].StackIndex >= start )
					nodes[other] = nodes[other] with { StackIndex = nodes[other].StackIndex + 1 };
			}
		}
		nodes[index] = node with { X = x, Y = y, StackIndex = stack };
		Version++;
	}

	/// <summary>The loft parameter range of the height drag (<c>0x441a00</c>): from the pylon's raised minimum height to the maximum height left over what the cell's stack above the ground already uses.</summary>
	// [BIN:TPW-EXE:0x441a00 height range] low = max(0, (fMinHeight − h0)/(h1 − h0)) (0x434650 with pylon +0x68, h0 +0xbc and h1 +0xc0); high = t + (fMaxHeight (+0x6c) − the height of the stack's top node (0x433280: the sum over the nodes below of h0 + t(h1 − h0) + stack offset, then its own h0 + t(h1 − h0))) / (h1 − h0) (0x434680)
	public (float Low, float High) LoftRange( CoasterSite site, int index )
	{
		var node = nodes[index];
		var pylon = Definition.Pylon( node.LoopPylon );
		var span = pylon.MeshHeightHigh - pylon.MeshHeightLow;
		if ( span == 0 )
			return (node.T, node.T);
		var low = MathF.Max( 0f, (pylon.MinHeight - pylon.MeshHeightLow) / span );
		var top = nodes.Where( other => !other.IsPylonless && other.X == node.X && other.Y == node.Y ).MaxBy( other => other.StackIndex ) ?? node;
		var topHeight = NodePosition( site, top ).Y - Definition.TrackHeightAdjust - site.GroundHeight( node.X, node.Y );
		return (low, node.T + (pylon.MaxHeight - topHeight) / span);
	}

	/// <summary>
	/// Swaps a node with the one above or below it in its cell's stack (<c>0x4423f0</c>, <c>0x4425c0</c> → <c>0x4424e0</c>): the two nodes exchange their stack index and loft parameter, so the
	/// piece of track that was higher is now lower. Returns false when the node is a station node, has no neighbour that way, or is pylon-less.
	/// </summary>
	// [BIN:TPW-EXE:0x4424e0 swap stack nodes] the stack indices, the descriptor's node array entries, the model's stack base height (+0x44) and the loft parameters (+0xE8) of the two nodes are exchanged; the chain order is untouched; the track is rebuilt (0x4346c0) and the stack's pylon models posed again (0x432d50, 0x432df0)
	public bool SwapStack( int index, bool up )
	{
		if ( index < StationNodeCount || index >= nodes.Count || !nodes[index].IsPlayerPylon )
			return false;
		var node = nodes[index];
		var wanted = node.StackIndex + (up ? 1 : -1);
		var otherIndex = nodes.FindIndex( other => other.IsPlayerPylon && other.X == node.X && other.Y == node.Y && other.StackIndex == wanted );
		if ( otherIndex < 0 )
			return false;
		var other = nodes[otherIndex];
		var nodeHeight = PylonHeight( Definition.Pylon( node.LoopPylon ), other.T );
		var otherHeight = PylonHeight( Definition.Pylon( other.LoopPylon ), node.T );
		nodes[index] = node with { StackIndex = other.StackIndex, T = other.T, Height = nodeHeight };
		nodes[otherIndex] = other with { StackIndex = node.StackIndex, T = node.T, Height = otherHeight };
		Version++;
		return true;
	}

	/// <summary>Deletes the last pylon of an incomplete track; a closed circuit opens again (the cut ends are marked open).</summary>
	internal bool RemoveLast()
	{
		if ( CutAfter is int cut )
		{
			// [BIN:TPW-EXE:0x444f30 delete last] a cut circuit deletes the node before the cut (+0x60) and moves the open-circuit reference back; the station nodes (descriptor flag 2) stay
			if ( cut < StationNodeCount || !nodes[cut].IsPlayerPylon )
				return false;
			nodes.RemoveAt( cut );
			CutAfter = cut - 1;
			SyncCutMarkers();
			Version++;
			return true;
		}
		if ( IsComplete || nodes.Count <= StationNodeCount )
			return false;
		IsClosed = false;
		nodes.RemoveAt( nodes.Count - 1 );
		Version++;
		return true;
	}

	/// <summary>Removes every pylon (the station's three nodes stay) and opens the track.</summary>
	internal void Clear()
	{
		if ( nodes.Count > StationNodeCount )
			nodes.RemoveRange( StationNodeCount, nodes.Count - StationNodeCount );
		IsComplete = IsClosed = false;
		CutAfter = null;
		SyncCutMarkers();
		Version++;
	}

	/// <summary>Whether the last pylon joins the head of the chain, the cell of S0 (distance window, turn, and the arrival turn along the platform).</summary>
	// [APPROX:COAST-030] OpenTPW's closure check: the closing link to the station's first node S0 and the arrival turn (entrance window at S0, toward S1) are validated and Revalidate marks the last pylon invalid when they fail; the original validates the open start of a reopened circuit through the validator's re-validation (0x434d6b, 0x434e5a) and a fresh circuit is closed by completion
	public CoasterPlaceResult CheckClosure()
	{
		if ( nodes.Count < StationNodeCount )
			return CoasterPlaceResult.NotEditable;
		int lastIndex;
		if ( CutAfter is int cut )
			lastIndex = cut;
		else if ( LastPylonIndex is int lastPylon )
			lastIndex = lastPylon;
		else
			return CoasterPlaceResult.NotEditable;
		// the chain start the circuit closes on: S0, or the node after the cut
		var startIndex = CutAfter is int ? CutAfter.Value + 1 : 0;
		if ( startIndex + 1 >= nodes.Count )
			return CoasterPlaceResult.NotEditable;
		var head = (nodes[startIndex].X, nodes[startIndex].Y);
		var second = (nodes[startIndex + 1].X, nodes[startIndex + 1].Y);
		var last = nodes[lastIndex];
		var pylon = ControlAfter( last, loopMode: false );
		var link = CheckLink( pylon, head, (last.X, last.Y), (nodes[lastIndex - 1].X, nodes[lastIndex - 1].Y), entrance: false );
		if ( link != CoasterPlaceResult.Ok )
			return link;
		var dx = head.X - last.X;
		var dy = head.Y - last.Y;
		var angle = MathF.Abs( TurnAngle( second, head, (last.X, last.Y) ) );
		// the arrival turn into the platform uses the entrance window; into any other node the ordinary one
		var (low, high) = AngleWindow( pylon, MathF.Sqrt( dx * dx + dy * dy ), entrance: startIndex == 0 );
		return angle < low ? CoasterPlaceResult.TurnTooSharp : angle > high ? CoasterPlaceResult.TurnTooWide : CoasterPlaceResult.Ok;
	}

	/// <summary>Whether a click on (x, y) is on the chain's open start: the cell of the head S0 (COAST-030), or a cell of the station itself where the track begins and ends.</summary>
	// [BIN:TPW-EXE:0x435d50 closing target] the cell that holds the head node (S0) of a fresh track is the closing target (the closing flag, and the open-circuit close test of 0x435020)
	// [EXT:coaster-station-end-click] a click on the station's '<' or '>' cell closes the circuit too, as it did when the station's ends were the track's ends; the original's own completion click takes any ride cell (types 4, 9, 10), see CoasterBuilder.Complete
	public bool IsChainStart( int x, int y ) => CutAfter is int cut
		? cut + 1 < nodes.Count && (x, y) == (nodes[cut + 1].X, nodes[cut + 1].Y)
		: (x, y) == DepartCell || (x, y) == ReturnCell || (nodes.Count > 0 && (x, y) == (nodes[0].X, nodes[0].Y));

	/// <summary>Closes the circuit (<c>0x470c4</c>): no node is added, the chain's ends are linked and "circuit open" clears.</summary>
	// [BIN:STP-PPC:0x100470C4 close circuit] placing on the cell of the chain's open start node closes the circuit instead of adding a node: the end nodes become adjacent and controller +60 bit 1 clears
	internal bool Close()
	{
		if ( CutAfter != null )
		{
			// [BIN:TPW-EXE:0x442c70 close circuit] closing a reopened circuit on its chain start joins the new nodes to the nodes after the cut and the track is complete again (state 2)
			CutAfter = null;
			SyncCutMarkers();
			IsClosed = true;
			Version++;
			return true;
		}
		if ( IsClosed || IsComplete || PylonCount < 2 )
			return false;
		IsClosed = true;
		Version++;
		return true;
	}

	/// <summary>The node flags of the station end node completion appends: no previous link and the second node as its next one. The call that appends it passes the creation parameters 0x144, which the node constructor maps to node flags 0x80000 and 0x100000, not to 0x144 (<c>0x435570</c>, <c>0x4343e6</c>).</summary>
	// [BIN:TPW-EXE:0x434060 node creation parameters] the constructor maps its parameter bits to node flags: 2 → 0x2000, 8 → 0x200, 0x10 → 0x400, 0x20 → 0x1800, 0x40 → 0x80000, 0x80 → 0x40000, 0x100 → 0x100000 (0x4343a6..0x4343eb), bit 0 clear → 0x200000 on the node and its predecessor, bit 4 goes to 0x433bd0; the end node's 0x144 is therefore 0x80000 | 0x100000, no loop flag 0x40, no 0x4 and no 0x100. The Mac call site 0x10037E34 passes the same 0x144; its node constructor could not be compared (no Mac binary here)
	public const CoasterNodeFlags StationEndFlags = CoasterNodeFlags.NoPreviousLink | CoasterNodeFlags.EndNextIsSecond;

	/// <summary>
	/// Completes a closed track (<c>0x37ef0</c>): sets controller <c>+60</c> bit 0 and appends one pylon-less station end
	/// node at the knot of S0 (creation parameters 0x144): the track returns onto the head of the platform.
	/// </summary>
	// [BIN:STP-PPC:0x10037EF0 complete track] completion sets controller +60 bit 0 and appends the station end node (0x10037CC8); then track flags 1 and 0x1000 (rebuild, re-rate, recount trains)
	// [BIN:STP-PPC:0x10037CC8 station end nodes] definition bit 0x20 set → one end node with flags 0x144 (0x10037CF4, 0x10037E34), clear → two with flags 4; the loader always ORs 0xA0 into the definition flags (0x100301F4) and nothing clears 0x20, so the original always appends one end node with 0x144 whatever bVisibleEndPylon says
	// [BIN:TPW-EXE:0x435570 completion] with definition flag 0x20 one end node is built: a pylon-less descriptor (0x13) at the world position of the first station node's pylon track dummy (head node +0x100 → +0x18 → +0x2c, instance +0x30..+0x38 through 0x435710), creation parameters 0x144 = node flags 0x80000 | 0x100000 with bit 4; then controller flags 0x1001 and the state setter 0x435730 with state 2; its tangent has no previous neighbour and its next is S1
	internal CoasterPlaceResult Complete()
	{
		if ( IsComplete )
			return CoasterPlaceResult.NotEditable;
		if ( !IsClosed )
			return CoasterPlaceResult.NotClosed;
		nodes.Add( StationEnd() );
		IsComplete = true;
		Version++;
		return CoasterPlaceResult.Ok;
	}

	/// <summary>The station end node: pylon-less, on the knot of S0 (its position is read from there), tangent along S0 toward S1.</summary>
	private CoasterNode StationEnd() => new( nodes[0].X, nodes[0].Y, nodes[0].Height, 0, false, 0, false, IsStationEnd: true ) { Flags = StationEndFlags };

	/// <summary>Reopens a completed track: removes the station end nodes and opens the circuit.</summary>
	// [BIN:STP-PPC:0x10037E8C reopen track] reopening clears controller +60 bits 0 and 1 (cut ends marked open, 0x10047170..0x10047508)
	internal void Reopen()
	{
		nodes.RemoveAll( node => node.IsStationEnd );
		IsComplete = IsClosed = false;
		CutAfter = null;
		SyncCutMarkers();
		Version++;
	}

	/// <summary>The cut markers follow the cut: only the node before it carries <see cref="CoasterNodeFlags.CutEnd"/> and only the node after it <see cref="CoasterNodeFlags.CutStart"/>.</summary>
	private void SyncCutMarkers()
	{
		foreach ( var node in nodes )
			node.Flags &= ~(CoasterNodeFlags.CutEnd | CoasterNodeFlags.CutStart);
		if ( CutAfter is int cut && cut >= 0 && cut + 1 < nodes.Count )
		{
			nodes[cut].Flags |= CoasterNodeFlags.CutEnd;
			nodes[cut + 1].Flags |= CoasterNodeFlags.CutStart;
		}
	}

	/// <summary>The outcome of cutting a range out of a finished circuit.</summary>
	public enum CutResult
	{
		/// <summary>The range cannot be cut (the circuit is not finished, or an end is not a pylon node).</summary>
		Refused,
		/// <summary>The cut reached the end of the chain: the track is an incomplete one again and building continues after the node before the cut.</summary>
		Reopened,
		/// <summary>The nodes between the ends are gone and the circuit is cut open in the middle.</summary>
		Cut
	}

	/// <summary>
	/// Cuts the nodes between two clicked nodes out of a finished circuit (the demolish button, <c>0x446740</c>): the ends are ordered (the first node S0 stands for the last node, the station end),
	/// a loop pair is never split, the nodes between them are removed and the circuit is cut open. A cut that reaches the end of the chain leaves an incomplete track again.
	/// </summary>
	// [BIN:TPW-EXE:0x446740 cut range] ordinal 0 (S0) of either end is replaced by the last node (0x446787..0x4467b9); the ends are ordered; a range that is one loop pair (the first with 0x40|0x80 next to the second with 0x40) widens to the nodes around it; equal ends widen to the nodes around the node; a node after the lower end that is the second of a pair moves the lower end back; a node before the upper end that is the first of a pair moves the upper end on; nodes between are deleted from the upper end down (0x444cb0); the node before the cut gets 0x50000, the one after 0xa0000; a range that reaches the last node clears controller flags 1 and 2 (0x435530)
	public CutResult Cut( int first, int second )
	{
		if ( !IsComplete || !IsClosed || IsCut || nodes.Count <= StationNodeCount + 1 )
			return CutResult.Refused;
		var last = nodes.Count - 1;
		bool Valid( int index ) => index >= 0 && index <= last && !nodes[index].IsPylonless;
		if ( !Valid( first ) || !Valid( second ) )
			return CutResult.Refused;
		// [BIN:TPW-EXE:0x44678c same station node] the second release on the very node the range started on is no cut when that node is a station node (descriptor flag 2: S0 or S2): 0x44678c..0x4467a0 clear the cut flag and leave the circuit as it is
		if ( first == second && nodes[first].IsStation )
			return CutResult.Refused;
		var reachesEnd = false;
		if ( first == 0 )
		{
			first = last;
			reachesEnd = true;
		}
		else if ( second == 0 )
		{
			second = last;
			reachesEnd = true;
		}
		var low = Math.Min( first, second );
		var high = Math.Max( first, second );
		// a pair's pylons carry the pair mark (0xe0 and 0x60): a flume's trough pylons carry the loop flag alone and are not pairs
		bool IsLoopNode( int index ) => index >= 0 && index < nodes.Count && (nodes[index].Flags & (CoasterNodeFlags.Loop | CoasterNodeFlags.LoopMark)) == (CoasterNodeFlags.Loop | CoasterNodeFlags.LoopMark);
		bool IsLoopFirst( int index ) => IsLoopNode( index ) && (nodes[index].Flags & CoasterNodeFlags.LoopFirst) != 0;
		if ( low + 1 == high && IsLoopFirst( low ) && IsLoopNode( high ) )
		{
			if ( low > 0 )
				low--;
			if ( high < last )
				high++;
		}
		if ( low == high )
		{
			low--;
			high++;
		}
		// the station's own nodes S0 and S1 stay: a cut starts at S2 (index 2) or later
		if ( low < StationNodeCount - 1 || high > last )
			return CutResult.Refused;
		if ( low + 1 != high )
		{
			if ( IsLoopNode( low + 1 ) && !IsLoopFirst( low + 1 ) && low > 0 )
				low--;
			if ( IsLoopFirst( high - 1 ) && high < last )
				high++;
		}
		if ( high == last )
			reachesEnd = true;
		// the ends must carry a pylon: a pylon-less node (S1, the station end) cannot border a cut
		if ( low < 0 || nodes[low].IsPylonless || nodes[high].IsPylonless && high != last )
			return CutResult.Refused;
		if ( reachesEnd )
		{
			nodes.RemoveRange( low + 1, nodes.Count - low - 1 );
			IsComplete = IsClosed = false;
			CutAfter = null;
			SyncCutMarkers();
			Version++;
			return CutResult.Reopened;
		}
		nodes.RemoveRange( low + 1, high - low - 1 );
		IsClosed = false;
		CutAfter = low;
		SyncCutMarkers();
		Version++;
		return CutResult.Cut;
	}

	/// <summary>
	/// Restores a saved track verbatim (the inverse of the capture; park-resume restore): the station end cells, every
	/// node with its flags, bank and stack index exactly as saved, the circuit flags and the edit version. Nothing is
	/// re-derived here — the stack index and the invalid/winch marks are state the build edits left, and the curve is
	/// the controller's to refit (<see cref="CoasterRide.Rebuild"/>). A save of the older shape, whose station was one node at the departing end
	/// (and whose completed track ended in a station end node at the return end), keeps the three station nodes the placement built and takes only its
	/// pylons over; the end node of a completed track is made again at the head of the new station. Returns true for such a track, whose old samples,
	/// train positions and the rest of what the old geometry derived are no longer valid.
	/// </summary>
	internal bool RestoreSnapshot( CoasterTrackSaveData saved )
	{
		ArgumentNullException.ThrowIfNull( saved );
		if ( saved.Nodes.Count > 0 && saved.Nodes.Count( node => node.IsStation ) == 1 )
			return MigrateTwoNodeStation( saved );
		DepartCell = saved.DepartCell;
		ReturnCell = saved.ReturnCell;
		CutAfter = null;
		nodes.Clear();
		foreach ( var savedNode in saved.Nodes )
		{
			var node = new CoasterNode( savedNode.X, savedNode.Y, savedNode.Height, savedNode.T, savedNode.LoopPylon, savedNode.StackIndex, savedNode.IsStation, savedNode.IsStationEnd,
				savedNode.ExactX is { } exactX && savedNode.ExactY is { } exactY && savedNode.ExactZ is { } exactZ ? new SVector3( exactX, exactY, exactZ ) : null )
			{
				// A save from before the PC creation parameters carried 0x144 on the station end node as node flags (loop, no auto-bank, zero bank); the node is restored as a freshly completed one.
				Flags = savedNode.IsStationEnd ? (CoasterNodeFlags)savedNode.Flags & (CoasterNodeFlags.Winch | CoasterNodeFlags.Invalid) | StationEndFlags : (CoasterNodeFlags)savedNode.Flags,
				Bank = savedNode.Bank,
				LoopRotate = savedNode.LoopRotate ?? DefaultLoopRotate,
				Yaw = savedNode.Yaw ?? 0,
				Incline = savedNode.Incline ?? 0
			};
			nodes.Add( node );
		}
		IsClosed = saved.IsClosed;
		IsComplete = saved.IsComplete;
		CutAfter = saved.CutAfter;
		Version = saved.Version;
		return false;
	}

	/// <summary>The migration of a track saved with the old station (COAST-029): the new station stays, the saved pylons follow it, the end node returns to the head.</summary>
	// [EXT:coaster-save-migration] OpenTPW's own old-save rule: a track saved before the three-node station keeps its pylons, circuit flags and node marks; the station nodes are the ones the placement built from the station model, the end node of a completed track stands at S0 again, and the controller re-samples the whole circuit (the saved train placement belongs to the old sampling and is dropped by CoasterRide.RestoreSnapshot)
	private bool MigrateTwoNodeStation( CoasterTrackSaveData saved )
	{
		if ( nodes.Count < StationNodeCount || DepartCell == null )
			throw new InvalidDataException( "A coaster track saved with the older station needs its station placed before it is restored." );
		nodes.RemoveRange( StationNodeCount, nodes.Count - StationNodeCount );
		CutAfter = null;
		foreach ( var savedNode in saved.Nodes.Where( node => !node.IsStation && !node.IsStationEnd ) )
		{
			nodes.Add( new CoasterNode( savedNode.X, savedNode.Y, savedNode.Height, savedNode.T, savedNode.LoopPylon, savedNode.StackIndex, false )
			{
				Flags = (CoasterNodeFlags)savedNode.Flags,
				Bank = savedNode.Bank,
				LoopRotate = savedNode.LoopRotate ?? DefaultLoopRotate
			} );
		}
		IsClosed = saved.IsClosed;
		IsComplete = saved.IsComplete;
		if ( IsComplete )
			nodes.Add( StationEnd() );
		Version = saved.Version;
		return true;
	}

	// ---- Cost -------------------------------------------------------------------------------------------

	/// <summary>
	/// The cost of one pylon (<c>0x4b168</c>) in single precision: CostPerUnit + CostPer2DLength × (horizontal
	/// distance to the previous node / 10) + CostPerHeight × (pylon height / 10); distances in world units.
	/// </summary>
	// [BIN:STP-PPC:0x1004B168 pylon cost] [CostPerUnit] + CostPer2DLength × (2D length / 10) + CostPerHeight × (height / 10), fused single multiply-adds, height h0 + t(h1 − h0) (0x100369BC) of the node's own pylon; the unit term is the own pylon's, the length and height rates those of <paramref name="rates"/> (default: the own pylon)
	public static float PylonCost( CoasterPylonControl pylon, float length2D, float height, bool chargeUnit = true, CoasterPylonControl? rates = null )
	{
		rates ??= pylon;
		var total = chargeUnit ? pylon.CostPerUnit : 0f;
		total = MathF.FusedMultiplyAdd( rates.CostPer2DLength, length2D / CostUnitDivisor, total );
		return MathF.FusedMultiplyAdd( rates.CostPerHeight, height / CostUnitDivisor, total );
	}

	/// <summary>
	/// The pylon whose length and height rates a node pays (<c>0x4b294..0x4b2d4</c>): a loop or trough pylon pays the
	/// StdPylon rates unless it stands on a loop or trough pylon, and a loop pylon (0x4000) pays them unless the
	/// previous node has flag 0x80, which OpenTPW never sets; so only a trough pylon stacked on a trough pylon keeps
	/// its own rates.
	/// </summary>
	// [BIN:STP-PPC:0x1004B294 cost rates] pylon flags & 0xC000 with no descriptor below (+44) or a below pylon without 0xC000 → StdPylon (0x1004B294..0x1004B2B4); then pylon flag 0x4000 with node flag 0x80 clear on the previous node → StdPylon (0x1004B2B8..0x1004B2D4)
	private CoasterPylonControl RatePylon( CoasterNode node, CoasterNode previous, CoasterNode? below )
	{
		const CoasterPylonFlags Special = CoasterPylonFlags.Loop | CoasterPylonFlags.Trough;
		var rates = Definition.Pylon( node.LoopPylon );
		if ( (rates.Flags & Special) != 0 && (below is null || (Definition.Pylon( below.LoopPylon ).Flags & Special) == 0) )
			rates = Definition.StandardPylon;
		if ( rates.IsLoop && ((uint)previous.Flags & 0x80) == 0 )
			rates = Definition.StandardPylon;
		return rates;
	}

	/// <summary>Rounds a cost up like <c>0x41aa4</c>: the integer part of <c>modf</c>, plus one when the fraction is nonzero.</summary>
	// [BIN:STP-PPC:0x10041AA4 round up] modf; any nonzero fractional part adds 1 to the integer part
	public static long RoundUp( float value )
	{
		var whole = MathF.Truncate( value );
		return (long)whole + (value - whole != 0 ? 1 : 0);
	}

	/// <summary>Truncates a cost to whole money like <c>0x1c3fbc</c>; negative gives 0.</summary>
	// [BIN:STP-PPC:0x101C3FBC float to unsigned] truncation; a negative value gives 0
	public static long Truncate( float value ) => value < 0 || float.IsNaN( value ) ? 0 : (long)value;

	/// <summary>
	/// The float cost of node <paramref name="index"/> (0 for pylon-less nodes). <paramref name="chargeUnit"/> adds
	/// CostPerUnit: only the place path asks for it; pick-up, drop and the height slider do not.
	/// </summary>
	// [BIN:STP-PPC:0x1004B168 pylon cost] the unit term is gated by r4 (0x1004B188, 0x1004B284): the place path passes 1 (0x10037864), the pick-up (0x1004A3DC, 0x1004A3F8), drop (0x1004A5DC, 0x1004A610, 0x1004A690) and height slider (0x10046268) pass 0
	public float NodeCost( int index, bool chargeUnit = true )
	{
		if ( index < StationNodeCount || index >= nodes.Count || !nodes[index].IsPlayerPylon )
			return 0;
		return LinkCost( nodes[index], nodes[index - 1], StackBelow( nodes[index] ), chargeUnit );
	}

	// [BIN:STP-PPC:0x1004B244 cost length] the 2D length is sqrt(fmadd(dx, dx, dz·dz)) in double, rounded to single
	private float LinkCost( CoasterNode node, CoasterNode previous, CoasterNode? below, bool chargeUnit = true )
	{
		float dx = (node.X - previous.X) * CellUnits, dy = (node.Y - previous.Y) * CellUnits;
		var pylon = Definition.Pylon( node.LoopPylon );
		var length = (float)Math.Sqrt( (double)dx * dx + dy * dy );
		return PylonCost( pylon, length, PylonHeight( pylon, node.T ), chargeUnit, RatePylon( node, previous, below ) );
	}

	/// <summary>The float cost a pylon at (x, y) with the requested height would have after the last node.</summary>
	public float PreviewCost( int x, int y, float requestedHeight, bool loop )
	{
		if ( nodes.Count == 0 )
			return 0;
		var (_, t) = ClampHeight( Definition.Pylon( loop ), requestedHeight );
		var stack = nodes.Count( node => !node.IsPylonless && node.X == x && node.Y == y );
		var candidate = new CoasterNode( x, y, 0, t, loop, stack, false );
		return LinkCost( candidate, nodes[LastIndex], StackBelow( candidate ) );
	}

	/// <summary>The build-cost display: the rounded-up cost of every pylon.</summary>
	public long BuildCost => Enumerable.Range( 1, Math.Max( 0, nodes.Count - 1 ) ).Sum( index => RoundUp( NodeCost( index ) ) );

	// ---- Geometry ---------------------------------------------------------------------------------------

	/// <summary>
	/// World position of a node (<c>0x33398</c>): the cell centre X = 10x + 5, Z = 10y + 5; Y = ground + h(t) at stack
	/// level 0, the node below + h(t) when stacked; pylon-less nodes stand at their loft height above the ground.
	/// </summary>
	// [BIN:STP-PPC:0x10033398 node position] descriptor +28 = 10x, +36 = 10y, +32 = terrain height; the node is at the cell centre (+5.0, 0x100333EC); Y = ground + h(t), or Y below + pylon +196 + h(t) when stacked
	// [BIN:TPW-EXE:0x434060 pylon-less knot] a pylon-less node's knot is its descriptor's exact position (node +0x4c..+0x54); the station end node's descriptor holds the world position of S0's pylon track dummy (0x435570), so the end node stands on the knot of S0
	// [APPROX:COAST-015] knots are the node positions (one knot per node), a pylon's knot axis is the node forward tangent and its roll the node bank; the pylon mesh track tags behind 0x1019B298 and the incline and bank clips are not read — evidence needed: the tag table builder and the tag data in the StdPylon/LoopPylon meshes
	public SVector3 NodePosition( CoasterSite site, CoasterNode node )
	{
		if ( node.IsStationEnd )
			return nodes.Count > 0 ? NodePosition( site, nodes[0] ) : default;
		if ( node.ExactPosition is { } exact )
			return new SVector3( exact.X, exact.Y + Definition.TrackHeightAdjust, exact.Z );
		var x = node.X * CellUnits + HalfCell;
		var z = node.Y * CellUnits + HalfCell;
		var lift = PylonHeight( Definition.Pylon( node.LoopPylon ), node.T );
		var below = node.StackIndex > 0 ? StackBelow( node ) : null;
		// [BIN:TPW-EXE:0x433280 stacked height] the height of a stacked node is the sum over the nodes below it of (h0 + t(h1 − h0) + stack offset) of the node's own pylon, then its own h0 + t(h1 − h0): each level adds the pylon's stack offset (+0xc4)
		var y = below is { } under ? NodePosition( site, under ).Y - Definition.TrackHeightAdjust + Definition.Pylon( node.LoopPylon ).StackOffset + lift : site.GroundHeight( node.X, node.Y ) + lift;
		return new SVector3( x, y + Definition.TrackHeightAdjust, z );
	}

	private CoasterNode? StackBelow( CoasterNode node ) =>
		nodes.FirstOrDefault( other => !other.IsPylonless && other.X == node.X && other.Y == node.Y && other.StackIndex == node.StackIndex - 1 );

	/// <summary>World position of the track point at a cell: the cell centre at ground + height + fTrackHeightAdjust.</summary>
	public SVector3 CellPoint( CoasterSite site, int x, int y, float height ) =>
		new( x * CellUnits + HalfCell, site.GroundHeight( x, y ) + height + Definition.TrackHeightAdjust, y * CellUnits + HalfCell );

	/// <summary>The signed XZ turn angle at a node (node <c>+148</c>): ±acos(â_xz·b̂_xz), negative when â.z·b̂.x − â.x·b̂.z &lt; 0.</summary>
	// [BIN:STP-PPC:0x10031984 turn angle XZ] node +148 = ±acos(clamp(â_xz · b̂_xz, −1, 1)), negative when the cross term is below 0
	public static float TurnAngleXZ( SVector3 previous, SVector3 position, SVector3 next )
	{
		var a = previous - position;
		var b = next - position;
		var la = MathF.Sqrt( a.X * a.X + a.Z * a.Z );
		var lb = MathF.Sqrt( b.X * b.X + b.Z * b.Z );
		float ax = a.X / la, az = a.Z / la, bx = b.X / lb, bz = b.Z / lb;
		var turn = MathF.Acos( Math.Clamp( ax * bx + az * bz, -1f, 1f ) );
		return az * bx - ax * bz < 0 ? -turn : turn;
	}

	/// <summary>
	/// The forward tangent of a node (<c>0x31450</c>, node <c>+52</c>) from its neighbours: the straight case below the
	/// pylon's straight-dot threshold (−0.999 without a pylon) or above 0.999 (on a loop pylon the half difference of the
	/// unit directions, else b/SD), the loop-pylon case away from both neighbours, otherwise perpendicular to the
	/// bisector in the plane of the turn; scaled by fCurviness or fStraightDivisor. A non-loop pylon next to a loop pylon
	/// gets a level tangent (Y = 0). Node flag 0x80 (which would reverse the half difference) is never set here.
	/// </summary>
	// [BIN:STP-PPC:0x10031450 node tangents] straight: the half difference only on a loop pylon (pylon flag 0x4000, 0x10031A74..0x10031A7C), T+ = |b|·½(â − b̂)/SD with node flag 0x80 clear (0x10031B18; 0x80 set gives ½(b̂ − â), 0x10031A8C), else T+ = b/SD (0x10031BA4); loop pylon curve T+ = −(â + b̂)|b|C; curve T+ = −(ŝ × n̂)|b|C with n = a × b, ŝ the unit bisector; after the plain straight and the curve case a non-loop pylon with a loop-pylon neighbour gets T± Y = 0 (0x10031BE0..0x10031C54, 0x10031EAC..0x10031F20)
	// [APPROX:COAST-032] the lengths, unit vectors and dot product are single precision with a rounding per step; the original forms the unit vectors with double fmul/fadd (0x1003182C..0x100318D8), the lengths with fmadd (0x100318F0, 0x10031918) and the dot product with fused steps ending in fmadds (0x10031958, 0x1003196C) — evidence needed: an isolated oracle run of 0x10031450 (it calls the sqrt and acos imports)
	public static SVector3 ForwardTangent( SVector3 previous, SVector3 position, SVector3 next, float curviness, float straightDivisor, float? straightDot, bool loopPylon, bool loopNeighbour = false, bool reversed = false )
	{
		var a = previous - position;
		var b = next - position;
		var la = a.Length();
		var lb = b.Length();
		var ua = a / la;
		var ub = b / lb;
		var dot = ua.X * ub.X + ua.Y * ub.Y + ua.Z * ub.Z;
		var straight = dot < (straightDot ?? -0.999f);
		SVector3 tangent;
		if ( straight || dot > 0.999f )
		{
			if ( straightDot != null && loopPylon )
				return (reversed ? ub - ua : ua - ub) * 0.5f / straightDivisor * lb;
			tangent = b / straightDivisor;
		}
		else
		{
			var bisector = ua + ub;
			if ( loopPylon )
				return -bisector * (lb * curviness);
			var n = SVector3.Cross( a, b );
			tangent = -SVector3.Cross( SVector3.Normalize( bisector ), SVector3.Normalize( n ) ) * (lb * curviness);
		}
		if ( straightDot != null && loopNeighbour )
			tangent.Y = 0;
		return tangent;
	}

	/// <summary>
	/// Whether a node's pylon is turned half a turn (node flag <c>0x10000000</c>, <c>0x431660</c>): on a suspended coaster, a node without flag 0x40 whose
	/// pylon is not a loop pylon and whose turn is a curve (neither straight case) gets it when b.x·a.z − b.z·a.x &lt; 0 with a and b the vectors to its
	/// previous and next node, so the pylon stands on the outside of the turn; every other curve node loses it.
	/// </summary>
	// [BIN:TPW-EXE:0x431f06 suspended pylon side] in the curve branch of the tangent routine, definition flag 8 set, node flag 0x40 clear and the Y component of b × a below 0 set node flag 0x10000000 (0x431f29), else it is cleared (0x431f34); the straight and loop-pylon branches leave it as it was
	// [APPROX:COAST-033] a straight or loop-pylon node keeps the flag from its last curve pass in the original; here it is clear there
	public static bool PylonTurnedBack( SVector3 previous, SVector3 position, SVector3 next, float? straightDot, bool loopPylon, bool suspended, bool loopNode )
	{
		if ( !suspended || loopNode || loopPylon )
			return false;
		var a = previous - position;
		var b = next - position;
		var ua = SVector3.Normalize( a );
		var ub = SVector3.Normalize( b );
		var dot = ua.X * ub.X + ua.Y * ub.Y + ua.Z * ub.Z;
		if ( dot < (straightDot ?? -0.999f) || dot > 0.999f )
			return false;
		return b.X * a.Z - b.Z * a.X < 0;
	}

	/// <summary>The <see cref="PylonTurnedBack"/> flag of every node of a chain whose neighbours its tangent uses (the flag-masked previous and next node).</summary>
	internal bool[] PylonsTurnedBack( IReadOnlyList<CoasterNode> chain, IReadOnlyList<CoasterKnot> knots )
	{
		var count = chain.Count;
		var result = new bool[count];
		for ( var index = 0; index < count; index++ )
		{
			var node = chain[index];
			if ( node.IsPylonless || index == 0 || (node.Flags & (CoasterNodeFlags.NoPreviousLink | CoasterNodeFlags.CutStart)) != 0 )
				continue;
			int? next = (node.Flags & (CoasterNodeFlags.NoNextLink | CoasterNodeFlags.CutEnd)) != 0 ? null
				: (node.Flags & CoasterNodeFlags.EndNextIsSecond) != 0 ? (count > 1 ? 1 : null)
				: index + 1 < count ? index + 1 : null;
			if ( next is not int after )
				continue;
			result[index] = PylonTurnedBack( knots[index - 1].Position, knots[index].Position, knots[after].Position, Definition.Pylon( node.LoopPylon ).StraightDotThreshold,
				IsLoopPylon( node ), Definition.IsSuspended, (node.Flags & CoasterNodeFlags.Loop) != 0 );
		}
		return result;
	}

	/// <summary>
	/// The node bank of auto-banking (<c>0x3f548</c>): v(±π − θ)/4/(|prev − P| + |next − P|), +π when θ ≥ 0, 0 when θ = 0.
	/// </summary>
	// [BIN:STP-PPC:0x1003F548 auto-bank] bank = v × (±π − θ) / definition +16 (4.0) / (|prev − P| + |next − P|), sign of π from θ, 0 when θ = 0; stored in node +216
	public static float AutoBank( float speed, float turn, float backLength, float forwardLength )
	{
		if ( turn == 0 )
			return 0;
		var pi = turn >= 0 ? MathF.PI : -MathF.PI;
		return speed * (pi - turn) / AutoBankDivisor / (backLength + forwardLength);
	}

	/// <summary>The running speed estimate along the nodes (<c>0x3f36c</c>): max(mod·(PerHeight·(y − y_next)) + v, floor).</summary>
	// [BIN:STP-PPC:0x1003F36C node speed estimate] max(fmadds(mod, PerHeight × (y − y_next), v), floor), uphill modifier when the next node is higher
	public static float SpeedEstimate( CoasterDefinition definition, float speed, float floor, float y, float yNext )
	{
		var modifier = yNext > y ? definition.UphillAccelModifier : definition.DownhillAccelModifier;
		var value = MathF.FusedMultiplyAdd( modifier, definition.AccelerationPerHeight * (y - yNext), speed );
		return value < floor ? floor : value;
	}

	/// <summary>
	/// The knots of a closed track (<c>0x33398</c>, <c>0x32cf0</c>): node positions, node banks (auto-banking with a
	/// running speed estimate from 0, when the definition banks), and per knot the unit direction (the node forward
	/// tangent; vertical on a pylon-less loop node) and, on loop pylons, the loop factor K.
	/// </summary>
	// [BIN:STP-PPC:0x10033630 auto-bank pass] nodes with a next node are banked in order when definition flag 0x10 is set and node flag 0x4 is clear (0x1003360C); node flag 0x100 forces bank 0 (0x1003F584); the speed estimate starts at 0 and is updated after each banked node with floor fMinSpeed
	// [BIN:STP-PPC:0x10032DD8 knot direction] a knot without a pylon takes (0, −1, 0) when its node has flag 0x40 and flag 0x80 is clear (0x10032DFC), (0, 1, 0) when both are set (never: OpenTPW sets no node flag 0x80), else the node tangent; the station end node (flags 0x80000 | 0x100000) and the middle station node carry no 0x40 and take their tangent
	public IReadOnlyList<CoasterKnot> BuildKnots( CoasterSite site ) => BuildKnots( site, nodes, writeBanks: true );

	/// <summary>
	/// The yaw a node's pylon would take with no manual rotation (node <c>+0xD4</c>): the heading of its knot direction. The rotate drag keeps the manual yaw within
	/// fMaxRotnAngleDeviance of it.
	/// </summary>
	// [BIN:TPW-EXE:0x441e80 rotate range] the drag range is the node's automatic yaw (+0xD4) ± the pylon control's +0x94 (fMaxRotnAngleDeviance)
	public float AutomaticYaw( CoasterSite site, int index )
	{
		var knots = BuildKnots( site, nodes, writeBanks: false, applyManual: false );
		var direction = knots[index].Direction;
		return MathF.Atan2( direction.X, direction.Z );
	}

	/// <summary>
	/// The knots of <paramref name="chain"/>: the nodes of the finished track (ending in the station end node) or of a track under
	/// construction (ending in the build cursor's node). The neighbours of a knot follow the node flags: S0 and the station end node have no
	/// previous node, S2 no next one, and the station end node's next is S1. <paramref name="writeBanks"/> stores the auto-banks on the
	/// nodes (the controller's rebuild); a preview leaves the nodes untouched.
	/// </summary>
	// [BIN:STP-PPC:0x10031450 node tangents] an end node without a previous node takes T+ = b/SD along the next node, one without a next node T+ = −a/SD away from the previous node; the chord to the missing neighbour is 1.0 (0x10032990)
	// [BIN:TPW-EXE:0x431677 node neighbours] the tangent routine drops the previous neighbour on node flag 0x80000 and the next on 0x40000, and on 0x100000 takes the head node's next node (S1): S0 leaves along S1 − S0, S2 along S2 − S1 whatever the first pylon, and the station end node along S1 − S0 as well
	// [BIN:TPW-EXE:0x43eb70 track rebuild] the mesh rebuild walks the node list of an open chain exactly like a finished one: it pairs every node with its next node until there is none, so a track under construction is drawn as the curve through the placed nodes
	internal IReadOnlyList<CoasterKnot> BuildKnots( CoasterSite site, IReadOnlyList<CoasterNode> chain, bool writeBanks, bool applyManual = true )
	{
		var count = chain.Count;
		var positions = chain.Select( node => NodePosition( site, node ) ).ToArray();
		// the cut of a reopened circuit breaks the chain: the node before it has no next neighbour, the one after it no previous one (0x446740 marks them 0x50000 and 0xa0000)
		int? Previous( int index ) => index > 0 && (chain[index].Flags & (CoasterNodeFlags.NoPreviousLink | CoasterNodeFlags.CutStart)) == 0 ? index - 1 : null;
		int? Next( int index ) => (chain[index].Flags & (CoasterNodeFlags.NoNextLink | CoasterNodeFlags.CutEnd)) != 0 ? null
			: (chain[index].Flags & CoasterNodeFlags.EndNextIsSecond) != 0 ? (count > 1 ? 1 : null)
			: index + 1 < count ? index + 1 : null;
		var banks = new float[count];
		var speed = 0f;
		for ( var index = 0; index < count; index++ )
		{
			var node = chain[index];
			// [BIN:TPW-EXE:0x4310c8 auto-bank] a node banks unless it carries node flag 0x4 (or 0x100 forces bank 0) and only when it has a next node in the list (the test of the raw link, not the flag-masked neighbour); the PC build path never sets 0x4 and the station end node's 0x144 are creation parameters (0x434060)
			// [APPROX:COAST-015] S0 has no previous node and is not banked (its angle needs both neighbours); S2, whose next link the tangent ignores but the bank pass does not, banks from S1 and the first pylon like any node; the speed estimate runs through the banked nodes only
			// [BIN:TPW-EXE:0x442010 manual bank] a node the tilt button banked by hand carries flag 4: the auto-bank pass skips it and its +0xD8 keeps the hand-set value
			if ( (node.Flags & (CoasterNodeFlags.NoAutoBank | CoasterNodeFlags.ZeroBank)) == CoasterNodeFlags.NoAutoBank && !node.IsPylonless )
			{
				banks[index] = node.Bank;
				continue;
			}
			if ( !Definition.AutoBanks || (node.Flags & (CoasterNodeFlags.NoAutoBank | CoasterNodeFlags.ZeroBank)) != 0 || index == 0 || index == count - 1 )
				continue;
			var previous = positions[index - 1];
			var next = positions[index + 1];
			var turn = TurnAngleXZ( previous, positions[index], next );
			banks[index] = AutoBank( speed, turn, (previous - positions[index]).Length(), (next - positions[index]).Length() );
			speed = SpeedEstimate( Definition, speed, Definition.MinSpeed, positions[index].Y, next.Y );
		}
		if ( writeBanks )
		{
			for ( var index = 0; index < count; index++ )
				chain[index].Bank = banks[index];
		}
		var knots = new CoasterKnot[count];
		for ( var index = 0; index < count; index++ )
		{
			var node = chain[index];
			var before = Previous( index ) is int back ? chain[back] : null;
			var after = Next( index ) is int forward ? chain[forward] : null;
			var pylon = node.IsPylonless ? null : Definition.Pylon( node.LoopPylon );
			SVector3 tangent;
			if ( Previous( index ) is not int previousIndex )
				tangent = Next( index ) is int nextOnly ? positions[nextOnly] - positions[index] : SVector3.Zero;
			else if ( Next( index ) is not int nextIndex )
				tangent = positions[index] - positions[previousIndex];
			else
			{
				// The tangent rule tests pylon flag 0x4000: a flume's trough pylon (0x8000) is not a loop pylon there.
				var loopNeighbour = IsLoopPylon( before! ) || IsLoopPylon( after! );
				tangent = ForwardTangent( positions[previousIndex], positions[index], positions[nextIndex], Definition.Curviness, Definition.StraightDivisor, pylon?.StraightDotThreshold, IsLoopPylon( node ), loopNeighbour, (node.Flags & CoasterNodeFlags.LoopFirst) != 0 );
			}
			var direction = node.IsPylonless && (node.Flags & CoasterNodeFlags.Loop) != 0 ? new SVector3( 0, -1, 0 )
				: tangent.LengthSquared() > 0 ? SVector3.Normalize( tangent ) : SVector3.UnitZ;
			if ( applyManual && !node.IsPylonless )
				direction = ApplyManualPose( node, direction );
			var factor = 0f;
			if ( node.LoopPylon )
			{
				var heading = Next( index ) is int towards ? new SVector3( positions[towards].X - positions[index].X, 0, positions[towards].Z - positions[index].Z ) : SVector3.Zero;
				var axis = new SVector3( direction.X, 0, direction.Z );
				var side = heading.LengthSquared() > 0 && axis.LengthSquared() > 0 ? SVector3.Dot( SVector3.Normalize( heading ), SVector3.Normalize( axis ) ) : 0;
				factor = HermiteCoasterCurve.LoopKnotFactor( Definition.Curviness, side, node.LoopRotate );
			}
			knots[index] = new CoasterKnot( positions[index], direction, banks[index], index, node.LoopPylon, factor, after != null && (after.Flags & CoasterNodeFlags.Loop) != 0,
				(node.Flags & CoasterNodeFlags.LoopFirst) != 0, before != null && (before.Flags & CoasterNodeFlags.Loop) != 0 );
		}
		return knots;
	}

	/// <summary>
	/// A pylon node's direction with its hand-set yaw and incline: the pylon animation turns the track tag with them. The yaw replaces the heading of the direction; the incline pitches it.
	/// </summary>
	// [BIN:TPW-EXE:0x430590 pylon clips] a pylon's animations by type: 3 loft (pylon flag 0x20), 10 rotate (0x40), 2 incline (0x80), 9 bank (0x100), 10 second (0x200); the pylon pose feeds node +0xE0 (yaw), +0xDC (incline) and +0xD8 (bank) to them and the track tag's pose is the knot's direction
	// [APPROX:COAST-028] the hand-set yaw replaces the heading of the knot direction and the incline pitches it by half its value (±π maps to ±90°); in the original the knot direction is the posed tag's forward axis (COAST-015): the yaw turns the rotate clip by yaw/2π and the incline the incline clip by pitch/2π through fInclineAnimMultiplier (0x430ed0, 0x430ca0; CoasterPylonRig poses both), so a PC tag tilts about 36° to 60° at the clip's ends, a tilt the knots do not take here
	private static SVector3 ApplyManualPose( CoasterNode node, SVector3 direction )
	{
		if ( (node.Flags & (CoasterNodeFlags.ManualYaw | CoasterNodeFlags.ManualIncline)) == 0 )
			return direction;
		var heading = MathF.Sqrt( direction.X * direction.X + direction.Z * direction.Z );
		var yaw = (node.Flags & CoasterNodeFlags.ManualYaw) != 0 ? node.Yaw : MathF.Atan2( direction.X, direction.Z );
		var pitch = MathF.Atan2( direction.Y, heading );
		if ( (node.Flags & CoasterNodeFlags.ManualIncline) != 0 )
			pitch = Math.Clamp( pitch + node.Incline * 0.5f, -1.5f, 1.5f );
		var level = MathF.Cos( pitch );
		return new SVector3( MathF.Sin( yaw ) * level, MathF.Sin( pitch ), MathF.Cos( yaw ) * level );
	}

	/// <summary>
	/// The chain the editor draws while the track is being built: the placed nodes, plus either a ghost pylon at the build cursor (open
	/// chain) or the station end node the closed circuit will end in (closed, not yet completed). Null while fewer than two nodes exist.
	/// </summary>
	// [BIN:TPW-EXE:0x435d50 build cursor] every frame the edited coaster (data 0x790fec, flag 4) runs this edit step: for a hovered cell within 300 units of the last node it fills the coaster's spare cursor node (controller +0x68, 0x436190, 0x436230) with the cell and links it after the last node, then rebuilds the track (0x444700) — the cursor node is a regular node of the chain, so the curve is drawn through it before the pylon is placed; a hovered cell that already holds a node of the chain marks the chain's start as closing (flag 0x8) instead
	// [APPROX:COAST-035] the ghost node takes the tool's current height and loop mode and carries the cursor flag (0x8000); the renderer draws the link to it as a translucent blue or red ribbon without a pylon (CoasterRenderer.BuildRibbon); the original's consumers of the node flags 0x8000/0x4000 and of uiInvalidCrossSection are not found — evidence needed: those consumers
	public (IReadOnlyList<CoasterNode> Chain, bool Closed)? BuildPreviewChain( (int X, int Y)? cursor, float height, bool loop )
	{
		if ( (IsComplete && !IsCut) || nodes.Count < StationNodeCount || DepartCell == null )
			return null;
		var chain = new List<CoasterNode>( nodes );
		if ( IsClosed )
		{
			chain.Add( StationEnd() );
			return (chain, true);
		}
		if ( cursor is { } cell && !IsChainStart( cell.X, cell.Y ) )
		{
			var pylon = Definition.Pylon( loop );
			var (clamped, t) = ClampHeight( pylon, height );
			var stack = nodes.Count( node => !node.IsPylonless && node.X == cell.X && node.Y == cell.Y );
			var last = nodes[LastIndex];
			if ( last.X != cell.X || last.Y != cell.Y )
			{
				var ghost = new CoasterNode( cell.X, cell.Y, clamped, t, loop, stack, false ) { Flags = (loop ? CoasterNodeFlags.Loop : CoasterNodeFlags.None) | CoasterNodeFlags.Cursor };
				chain.Insert( InsertIndex, ghost );
				if ( IsCut )
				{
					// the cursor node takes over the cut: the node before it ends one part of the chain and the node after it starts the next
					chain[InsertIndex - 1] = chain[InsertIndex - 1] with { };
					chain[InsertIndex - 1].Flags &= ~CoasterNodeFlags.CutEnd;
					ghost.Flags |= CoasterNodeFlags.CutEnd;
				}
			}
		}
		return chain.Count < 2 ? null : (chain, false);
	}

	private bool IsLoopPylon( CoasterNode node ) => !node.IsPylonless && Definition.Pylon( node.LoopPylon ).IsLoop;

	internal void AddCanonicalState( StateHasher hash )
	{
		hash.Add( DepartCell is { } depart ? depart.X : -1 );
		hash.Add( DepartCell is { } departCell ? departCell.Y : -1 );
		hash.Add( ReturnCell is { } back ? back.X : -1 );
		hash.Add( ReturnCell is { } backCell ? backCell.Y : -1 );
		hash.Add( IsClosed );
		hash.Add( IsComplete );
		if ( CutAfter is int cutAfter )
			hash.Add( cutAfter );
		hash.Add( nodes.Count );
		foreach ( var node in nodes )
		{
			hash.Add( node.X );
			hash.Add( node.Y );
			hash.Add( node.Height );
			hash.Add( node.T );
			hash.Add( node.LoopPylon );
			hash.Add( node.StackIndex );
			hash.Add( node.IsStationEnd );
			hash.Add( (long)node.Flags );
			hash.Add( node.Bank );
			hash.Add( node.LoopRotate );
			if ( (node.Flags & (CoasterNodeFlags.ManualYaw | CoasterNodeFlags.ManualIncline)) != 0 )
			{
				hash.Add( node.Yaw );
				hash.Add( node.Incline );
			}
			hash.Add( node.ExactPosition is { } exact ? exact.X : float.NaN );
			hash.Add( node.ExactPosition is { } exactY ? exactY.Y : float.NaN );
			hash.Add( node.ExactPosition is { } exactZ ? exactZ.Z : float.NaN );
		}
	}
}

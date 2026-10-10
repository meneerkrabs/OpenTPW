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
	/// <summary>A loop node: the next link uses the LoopPylon limits.</summary>
	Loop = 0x40,
	/// <summary>A chain-lift (winch) section, set by the rating pass.</summary>
	Winch = 0x800,
	/// <summary>The node or the link after it failed the validator (shown as invalid; the edit is kept).</summary>
	Invalid = 0x4000
}

/// <summary>
/// One node of a coaster track (COAST-plan §3.3, §13.1): the station node first, then the pylons in build order, then
/// the station end nodes completion appends. <see cref="Height"/> is the pylon height above the ground after the clamp
/// (the loft height of pylon-less nodes), <see cref="T"/> the stored height parameter (node <c>+232</c>),
/// <see cref="StackIndex"/> the position in the cell's pylon stack, <see cref="Bank"/> the auto-bank (node <c>+216</c>).
/// </summary>
public sealed record CoasterNode( int X, int Y, float Height, float T, bool LoopPylon, int StackIndex, bool IsStation, bool IsStationEnd = false )
{
	public CoasterNodeFlags Flags { get; set; }
	public float Bank { get; set; }
	/// <summary>A pylon-less node (descriptor flag bit 0): the station node and the station end nodes.</summary>
	public bool IsPylonless => IsStation || IsStationEnd;
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
		return new CoasterSite( grid.Width, grid.Height, ( x, y ) => blocked[y * width + x], ( x, y ) => grid.GetCellHeight( x, y ) / ObjectPlacement.ModelScale );
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
	/// <summary>The cost divisor (constant <c>-13300(r2)</c>): rates are per 10 world units.</summary>
	public const float CostUnitDivisor = 10f;
	/// <summary>Definition <c>+16</c>: the auto-bank divisor (fAutoBankDivisor is never loaded).</summary>
	// [BIN:STP-PPC:0x1002D2D0 coaster defaults] definition +16 = 4.0, the auto-bank divisor; no code reads settings offset 15060 (fAutoBankDivisor)
	public const float AutoBankDivisor = 4f;
	/// <summary>The loop-rotate parameter (node <c>+248</c>) a new loop pylon is taken to have (COAST-015).</summary>
	public const float DefaultLoopRotate = 0.5f;

	private readonly List<CoasterNode> nodes = new();

	public CoasterTrack( CoasterDefinition definition ) => Definition = definition ?? throw new ArgumentNullException( nameof( definition ) );

	public CoasterDefinition Definition { get; }
	/// <summary>The station end the track leaves from (shape '&gt;'): the station node's cell.</summary>
	public (int X, int Y)? DepartCell { get; private set; }
	/// <summary>The station end the track returns to (shape '&lt;').</summary>
	public (int X, int Y)? ReturnCell { get; private set; }
	public IReadOnlyList<CoasterNode> Nodes => nodes;
	/// <summary>Pylons placed after the station node.</summary>
	public int PylonCount => nodes.Count( node => !node.IsPylonless );
	/// <summary>Controller <c>+60</c> bit 1 clear: the chain's ends are linked.</summary>
	public bool IsClosed { get; private set; }
	/// <summary>Controller <c>+60</c> bit 0: completed (station end nodes appended).</summary>
	public bool IsComplete { get; private set; }
	/// <summary>A coaster runs only when complete and closed (<c>0x45da4</c>).</summary>
	public bool IsRunnable => IsComplete && IsClosed;
	public bool HasInvalidNodes => nodes.Any( node => (node.Flags & CoasterNodeFlags.Invalid) != 0 );
	/// <summary>Incremented by every edit.</summary>
	public int Version { get; private set; }

	/// <summary>Sets the station ends and the station node (descriptor flag 2) at the depart cell, removing any pylons.</summary>
	public void SetStation( (int X, int Y) depart, (int X, int Y) @return )
	{
		if ( depart == @return )
			throw new ArgumentException( "The station ends must differ.", nameof( @return ) );
		DepartCell = depart;
		ReturnCell = @return;
		nodes.Clear();
		nodes.Add( new CoasterNode( depart.X, depart.Y, Definition.InitialPylonLoftHeight, 0, false, 0, true ) );
		IsComplete = IsClosed = false;
		Version++;
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
	// [BIN:STP-PPC:0x10036F28 turn window] limits interpolated by the distance fraction t (clamped by data 0x3d058/0x3d05c); entrance limits (+116..+128) when the previous node is the station node, else +132..+144
	public static (float Low, float High) AngleWindow( CoasterPylonControl pylon, float distance, bool entrance )
	{
		var span = (float)(pylon.MaxPlaceDistance - pylon.MinPlaceDistance);
		var t = span == 0 ? 0 : Math.Clamp( (distance - pylon.MinPlaceDistance) / span, 0f, 1f );
		var (a, b, c, d) = entrance
			? (pylon.EntranceMinAngleAtMinDistance, pylon.EntranceMinAngleAtMaxDistance, pylon.EntranceMaxAngleAtMinDistance, pylon.EntranceMaxAngleAtMaxDistance)
			: (pylon.MinAngleAtMinDistance, pylon.MinAngleAtMaxDistance, pylon.MaxAngleAtMinDistance, pylon.MaxAngleAtMaxDistance);
		return (a * (1 - t) + b * t, c * (1 - t) + d * t);
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
		// [APPROX:COAST-007] the distance waiver (previous descriptor flag 2 with an ordinal other than 2) is not applied — evidence needed: the purpose of the waiver at 0x10036BA4 and which nodes carry descriptor flag 2 with ordinal ≠ 2
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

	/// <summary>The point before node <paramref name="index"/> for its turn angle: the node before it, or the return end before the station node.</summary>
	private (int X, int Y)? Before( int index ) => index >= 1 ? (nodes[index - 1].X, nodes[index - 1].Y) : ReturnCell;

	/// <summary>
	/// The validator <c>0x36ba4</c> for a new pylon after the last node (<paramref name="moving"/> null) or for node
	/// <paramref name="moving"/> moved to (x, y): bounds, no-ground cell (new pylons only), the pylon control, same
	/// cell, distance, turn window, stacking; moving also re-validates the next two links. The build tool marks the
	/// node with the result; it does not refuse it.
	/// </summary>
	// [BIN:STP-PPC:0x10094474 slope and deviance] slope (fMaxHeightDiffPerGSquareDist, pylon +112), loop rotation deviance (+152) and loop height difference (+172) are loaded but never read by the coaster code; fMaxRotnAngleDeviance (+148) only bounds the rotate slider 0x10046508
	public CoasterPlaceResult CheckPlacement( CoasterSite site, CoasterWorld? world, CoasterRide? owner, int x, int y, bool loopMode, int? moving = null )
	{
		if ( DepartCell == null || nodes.Count == 0 || ((IsComplete || IsClosed) && moving == null) )
			return CoasterPlaceResult.NotEditable;
		if ( moving is int index && (index <= 0 || index >= nodes.Count || nodes[index].IsPylonless) )
			return CoasterPlaceResult.NotEditable;
		if ( loopMode && !Definition.HasLoopPylon )
			return CoasterPlaceResult.NoLoopPylon;
		// [BIN:STP-PPC:0x10036BA4 placement validator] test 1: x < map width (+24) and y < map height (+28)
		if ( x < 0 || y < 0 || x >= site.Width || y >= site.Height )
			return CoasterPlaceResult.OutsideMap;
		if ( moving == null && site.IsBlocked( x, y ) )
			return CoasterPlaceResult.Blocked;
		var position = moving ?? nodes.Count;
		var previous = nodes[position - 1];
		var pylon = ControlAfter( previous, loopMode );
		var link = CheckLink( pylon, (x, y), (previous.X, previous.Y), Before( position - 1 ), entrance: previous.IsStation );
		if ( link != CoasterPlaceResult.Ok )
			return link;
		// [APPROX:COAST-004] the track is not tested against terrain or scenery (asTrackCollisionPoints, fCollisionPossibleRadius) — evidence needed: the track collision routine and its callers
		var stack = CheckStack( world, owner, x, y, loopMode, moving );
		if ( stack != CoasterPlaceResult.Ok )
			return stack;
		if ( moving is int moved )
		{
			// [BIN:STP-PPC:0x10037318 move re-validation] moving a node re-validates the following node and the one after it with the moved position (0x10037318, 0x100373B4)
			for ( var next = moved + 1; next <= moved + 2 && next < nodes.Count; next++ )
			{
				if ( nodes[next].IsPylonless )
					break;
				var after = nodes[next - 1];
				var from = next - 1 == moved ? (x, y) : (after.X, after.Y);
				var behind = next - 2 == moved ? (x, y) : next - 2 >= 0 ? (nodes[next - 2].X, nodes[next - 2].Y) : ReturnCell;
				var control = Definition.Pylon( next - 1 == moved ? loopMode : (after.Flags & CoasterNodeFlags.Loop) != 0 );
				if ( CheckLink( control, (nodes[next].X, nodes[next].Y), from, behind, entrance: after.IsStation ) != CoasterPlaceResult.Ok )
					return CoasterPlaceResult.BreaksNextLink;
			}
		}
		return CoasterPlaceResult.Ok;
	}

	/// <summary>Test 7: a cell that already holds pylons takes another only from the same coaster, with the same pylon control, below the stack limit.</summary>
	// [BIN:STP-PPC:0x10037104 pylon stacking] an occupying pylon (found by walking every coaster's descriptor list, 0x10056BAC) must belong to the same coaster (+20), use the same pylon control (+24) and have stack height (+48) < uiMaxStackedPylons (+204), with no object flags 0x600
	private CoasterPlaceResult CheckStack( CoasterWorld? world, CoasterRide? owner, int x, int y, bool loopMode, int? moving )
	{
		var count = 0;
		foreach ( var (ride, node) in PylonsAt( world, owner, x, y ) )
		{
			if ( moving is int index && ride == owner && ReferenceEquals( node, nodes[index] ) )
				continue;
			if ( ride != owner )
				return CoasterPlaceResult.OtherCoaster;
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
		for ( var index = 1; index < nodes.Count; index++ )
		{
			var node = nodes[index];
			if ( node.IsPylonless )
				continue;
			var previous = nodes[index - 1];
			var failed = site.IsBlocked( node.X, node.Y )
				|| CheckLink( ControlAfter( previous, node.LoopPylon ), (node.X, node.Y), (previous.X, previous.Y), Before( index - 1 ), previous.IsStation ) != CoasterPlaceResult.Ok
				|| CheckStack( world, owner, node.X, node.Y, node.LoopPylon, index ) != CoasterPlaceResult.Ok;
			if ( failed )
			{
				node.Flags |= CoasterNodeFlags.Invalid;
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
			for ( var index = nodes.Count - 1; index >= 1; index-- )
			{
				if ( !nodes[index].IsPylonless )
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
	// [BIN:STP-PPC:0x100369BC pylon height] height = h0 + t × (h1 − h0) from the pylon mesh heights +188/+192
	public static float PylonHeight( CoasterPylonControl pylon, float t ) => pylon.MeshHeightLow + t * (pylon.MeshHeightHigh - pylon.MeshHeightLow);

	/// <summary>Adds a pylon after the last node of an open chain; returns it.</summary>
	internal CoasterNode Append( CoasterWorld? world, CoasterRide? owner, int x, int y, float requestedHeight, bool loop )
	{
		var pylon = Definition.Pylon( loop );
		var (height, t) = ClampHeight( pylon, requestedHeight );
		var stack = PylonsAt( world, owner, x, y ).Count();
		var node = new CoasterNode( x, y, height, t, loop, stack, false ) { Flags = loop ? CoasterNodeFlags.Loop : CoasterNodeFlags.None };
		nodes.Add( node );
		Version++;
		return node;
	}

	internal void Replace( int index, CoasterNode node )
	{
		nodes[index] = node;
		Version++;
	}

	/// <summary>Deletes the last pylon of an incomplete track; a closed circuit opens again (the cut ends are marked open).</summary>
	internal bool RemoveLast()
	{
		if ( IsComplete || nodes.Count <= 1 )
			return false;
		IsClosed = false;
		nodes.RemoveAt( nodes.Count - 1 );
		Version++;
		return true;
	}

	/// <summary>Removes every pylon (the station node stays) and opens the track.</summary>
	internal void Clear()
	{
		if ( nodes.Count > 1 )
			nodes.RemoveRange( 1, nodes.Count - 1 );
		IsComplete = IsClosed = false;
		Version++;
	}

	/// <summary>Whether the last pylon joins the return end (distance window, turn, and the arrival turn into the station).</summary>
	public CoasterPlaceResult CheckClosure()
	{
		if ( ReturnCell is not { } back || DepartCell is not { } depart || LastPylonIndex is not int lastIndex )
			return CoasterPlaceResult.NotEditable;
		var last = nodes[lastIndex];
		var pylon = ControlAfter( last, loopMode: false );
		var link = CheckLink( pylon, back, (last.X, last.Y), (nodes[lastIndex - 1].X, nodes[lastIndex - 1].Y), entrance: false );
		if ( link != CoasterPlaceResult.Ok )
			return link;
		var dx = back.X - last.X;
		var dy = back.Y - last.Y;
		var angle = MathF.Abs( TurnAngle( depart, back, (last.X, last.Y) ) );
		var (low, high) = AngleWindow( pylon, MathF.Sqrt( dx * dx + dy * dy ), entrance: true );
		return angle < low ? CoasterPlaceResult.TurnTooSharp : angle > high ? CoasterPlaceResult.TurnTooWide : CoasterPlaceResult.Ok;
	}

	/// <summary>Whether a click on (x, y) is on the chain's open start (COAST-016: the station's end cells).</summary>
	public bool IsChainStart( int x, int y ) => (x, y) == DepartCell || (x, y) == ReturnCell;

	/// <summary>Closes the circuit (<c>0x470c4</c>): no node is added, the chain's ends are linked and "circuit open" clears.</summary>
	// [BIN:STP-PPC:0x100470C4 close circuit] placing on the cell of the chain's open start node closes the circuit instead of adding a node: the end nodes become adjacent and controller +60 bit 1 clears
	internal bool Close()
	{
		if ( IsClosed || IsComplete || PylonCount < 2 )
			return false;
		IsClosed = true;
		Version++;
		return true;
	}

	/// <summary>
	/// Completes a closed track (<c>0x37ef0</c>): sets controller <c>+60</c> bit 0 and appends the station end node(s), one
	/// with <c>bVisibleEndPylon</c> and two otherwise, pylon-less.
	/// </summary>
	// [BIN:STP-PPC:0x10037EF0 complete track] completion sets controller +60 bit 0 and appends the station end nodes (0x10037CC8): one with bVisibleEndPylon (definition 0x20), else two (the first with node flags 0x144); then track flags 1 and 0x1000 (rebuild, re-rate, recount trains)
	// [APPROX:COAST-029] the station end nodes stand at the station's '<' cell (and, for two, the next cell toward '>') at the station track height fInitialPylonLoftHeight, without the 0x144 flags, and their count follows the bVisibleEndPylon setting (the loader's always-set 0xA0 already contains 0x20); the station node stands at '>', so trains leave toward '>' and return at '<'; the station mesh tags that place these nodes are not read — evidence needed: the station mesh tag lookup of 0x10037CC8 and the station node placement
	internal CoasterPlaceResult Complete()
	{
		if ( IsComplete )
			return CoasterPlaceResult.NotEditable;
		if ( !IsClosed )
			return CoasterPlaceResult.NotClosed;
		var back = ReturnCell!.Value;
		var depart = DepartCell!.Value;
		var height = Definition.InitialPylonLoftHeight;
		nodes.Add( new CoasterNode( back.X, back.Y, height, 0, false, 0, false, IsStationEnd: true ) );
		if ( !Definition.VisibleEndPylon )
			nodes.Add( new CoasterNode( back.X + Math.Sign( depart.X - back.X ), back.Y + Math.Sign( depart.Y - back.Y ), height, 0, false, 0, false, IsStationEnd: true ) );
		IsComplete = true;
		Version++;
		return CoasterPlaceResult.Ok;
	}

	/// <summary>Reopens a completed track: removes the station end nodes and opens the circuit.</summary>
	// [BIN:STP-PPC:0x10037E8C reopen track] reopening clears controller +60 bits 0 and 1 (cut ends marked open, 0x10047170..0x10047508)
	internal void Reopen()
	{
		nodes.RemoveAll( node => node.IsStationEnd );
		IsComplete = IsClosed = false;
		Version++;
	}

	// ---- Cost -------------------------------------------------------------------------------------------

	/// <summary>
	/// The cost of one pylon (<c>0x4b168</c>) in single precision: CostPerUnit + CostPer2DLength × (horizontal
	/// distance to the previous node / 10) + CostPerHeight × (pylon height / 10); distances in world units.
	/// </summary>
	// [BIN:STP-PPC:0x1004B168 pylon cost] [CostPerUnit] + CostPer2DLength × (2D length / 10) + CostPerHeight × (height / 10), fused single multiply-adds, height h0 + t(h1 − h0) (0x100369BC)
	// [APPROX:COAST-020] loop and trough pylons use their own length and height rates; the conditions under which 0x1004B294..0x1004B2D4 fall back to the StdPylon rates are not traced — evidence needed: the branch conditions at 0x1004B294..0x1004B2D4
	public static float PylonCost( CoasterPylonControl pylon, float length2D, float height, bool chargeUnit = true )
	{
		var total = chargeUnit ? pylon.CostPerUnit : 0f;
		total = MathF.FusedMultiplyAdd( pylon.CostPer2DLength, length2D / CostUnitDivisor, total );
		return MathF.FusedMultiplyAdd( pylon.CostPerHeight, height / CostUnitDivisor, total );
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

	/// <summary>The float cost of node <paramref name="index"/> (0 for pylon-less nodes).</summary>
	public float NodeCost( int index )
	{
		if ( index <= 0 || index >= nodes.Count || nodes[index].IsPylonless )
			return 0;
		return LinkCost( nodes[index], nodes[index - 1] );
	}

	private float LinkCost( CoasterNode node, CoasterNode previous )
	{
		float dx = (node.X - previous.X) * CellUnits, dy = (node.Y - previous.Y) * CellUnits;
		var pylon = Definition.Pylon( node.LoopPylon );
		return PylonCost( pylon, MathF.Sqrt( dx * dx + dy * dy ), PylonHeight( pylon, node.T ) );
	}

	/// <summary>The float cost a pylon at (x, y) with the requested height would have after the last node.</summary>
	public float PreviewCost( int x, int y, float requestedHeight, bool loop )
	{
		if ( nodes.Count == 0 )
			return 0;
		var (_, t) = ClampHeight( Definition.Pylon( loop ), requestedHeight );
		return LinkCost( new CoasterNode( x, y, 0, t, loop, 0, false ), nodes[^1] );
	}

	/// <summary>The build-cost display: the rounded-up cost of every pylon.</summary>
	public long BuildCost => Enumerable.Range( 1, Math.Max( 0, nodes.Count - 1 ) ).Sum( index => RoundUp( NodeCost( index ) ) );

	// ---- Geometry ---------------------------------------------------------------------------------------

	/// <summary>
	/// World position of a node (<c>0x33398</c>): the cell centre X = 10x + 5, Z = 10y + 5; Y = ground + h(t) at stack
	/// level 0, the node below + h(t) when stacked; pylon-less nodes stand at their loft height above the ground.
	/// </summary>
	// [BIN:STP-PPC:0x10033398 node position] descriptor +28 = 10x, +36 = 10y, +32 = terrain height; the node is at the cell centre (+5.0, 0x100333EC); Y = ground + h(t), or Y below + pylon +196 + h(t) when stacked
	// [APPROX:COAST-015] knots are the node positions (one knot per node), a pylon's knot axis is the node forward tangent and its roll the node bank, the pylon mesh heights h0/h1 are fMinHeight/fMaxHeight, a stacked pylon's offset (+196) is 0 and a loop pylon's loop-rotate parameter is 0.5; the pylon mesh track tags and the loft/rotate/incline/bank animation that frames them are not read — evidence needed: the tag table builder behind 0x1019B298 and the tag data in the StdPylon/LoopPylon meshes
	public SVector3 NodePosition( CoasterSite site, CoasterNode node )
	{
		var x = node.X * CellUnits + HalfCell;
		var z = node.Y * CellUnits + HalfCell;
		var lift = node.IsPylonless ? node.Height : PylonHeight( Definition.Pylon( node.LoopPylon ), node.T );
		var below = node.StackIndex > 0 ? StackBelow( node ) : null;
		var y = below is { } under ? NodePosition( site, under ).Y - Definition.TrackHeightAdjust + lift : site.GroundHeight( node.X, node.Y ) + lift;
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
	/// pylon's straight-dot threshold (−0.999 without a pylon) or above 0.999, the loop-pylon case away from both
	/// neighbours, otherwise perpendicular to the bisector in the plane of the turn; scaled by fCurviness or
	/// fStraightDivisor.
	/// </summary>
	// [BIN:STP-PPC:0x10031450 node tangents] straight: non-loop pylon T+ = |b|·½(b̂ − â)/SD, else T+ = b/SD; loop pylon T+ = −(â + b̂)|b|C; curve T+ = −(ŝ × n̂)|b|C with n = a × b, ŝ the unit bisector
	public static SVector3 ForwardTangent( SVector3 previous, SVector3 position, SVector3 next, float curviness, float straightDivisor, float? straightDot, bool loopPylon )
	{
		var a = previous - position;
		var b = next - position;
		var la = a.Length();
		var lb = b.Length();
		var ua = a / la;
		var ub = b / lb;
		var dot = ua.X * ub.X + ua.Y * ub.Y + ua.Z * ub.Z;
		var straight = dot < (straightDot ?? -0.999f);
		if ( straight || dot > 0.999f )
		{
			if ( straightDot != null && !loopPylon )
				return -((ua - ub) * 0.5f / straightDivisor) * lb;
			return b / straightDivisor;
		}
		var bisector = ua + ub;
		if ( loopPylon )
			return -bisector * (lb * curviness);
		var n = SVector3.Cross( a, b );
		var tangent = SVector3.Cross( SVector3.Normalize( bisector ), SVector3.Normalize( n ) );
		return -tangent * (lb * curviness);
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
	/// tangent) and, on loop pylons, the loop factor K.
	/// </summary>
	// [BIN:STP-PPC:0x10033630 auto-bank pass] nodes with a next node are banked in order when definition flag 0x10 is set; the speed estimate starts at 0 and is updated after each banked node with floor fMinSpeed
	public IReadOnlyList<CoasterKnot> BuildKnots( CoasterSite site )
	{
		var count = nodes.Count;
		var positions = nodes.Select( node => NodePosition( site, node ) ).ToArray();
		var speed = 0f;
		for ( var index = 0; index < count; index++ )
		{
			var node = nodes[index];
			node.Bank = 0;
			if ( !Definition.AutoBanks || node.IsPylonless )
				continue;
			var previous = positions[(index - 1 + count) % count];
			var next = positions[(index + 1) % count];
			var turn = TurnAngleXZ( previous, positions[index], next );
			node.Bank = AutoBank( speed, turn, (previous - positions[index]).Length(), (next - positions[index]).Length() );
			speed = SpeedEstimate( Definition, speed, Definition.MinSpeed, positions[index].Y, next.Y );
		}
		var knots = new CoasterKnot[count];
		for ( var index = 0; index < count; index++ )
		{
			var node = nodes[index];
			var previous = positions[(index - 1 + count) % count];
			var next = positions[(index + 1) % count];
			var pylon = node.IsPylonless ? null : Definition.Pylon( node.LoopPylon );
			var tangent = ForwardTangent( previous, positions[index], next, Definition.Curviness, Definition.StraightDivisor, pylon?.StraightDotThreshold, node.LoopPylon );
			var direction = tangent.LengthSquared() > 0 ? SVector3.Normalize( tangent ) : SVector3.UnitZ;
			var factor = 0f;
			if ( node.LoopPylon )
			{
				var heading = new SVector3( next.X - positions[index].X, 0, next.Z - positions[index].Z );
				var axis = new SVector3( direction.X, 0, direction.Z );
				var side = heading.LengthSquared() > 0 && axis.LengthSquared() > 0 ? SVector3.Dot( SVector3.Normalize( heading ), SVector3.Normalize( axis ) ) : 0;
				factor = HermiteCoasterCurve.LoopKnotFactor( Definition.Curviness, side, DefaultLoopRotate );
			}
			knots[index] = new CoasterKnot( positions[index], direction, node.Bank, index, node.LoopPylon, factor );
		}
		return knots;
	}

	internal void AddCanonicalState( StateHasher hash )
	{
		hash.Add( DepartCell is { } depart ? depart.X : -1 );
		hash.Add( DepartCell is { } departCell ? departCell.Y : -1 );
		hash.Add( ReturnCell is { } back ? back.X : -1 );
		hash.Add( ReturnCell is { } backCell ? backCell.Y : -1 );
		hash.Add( IsClosed );
		hash.Add( IsComplete );
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
		}
	}
}

namespace OpenTPW;

/// <summary>What the track edit tool's pick test answers for a clicked tile (tp.exe <c>0x539760</c> in the edit sub-modes 0xE and 0x13).</summary>
public enum TrackEditPick
{
	/// <summary>The click picks nothing and changes nothing: the cell is no track tile, or a station tile (<c>0x53ADB0</c>).</summary>
	None,
	/// <summary>Answer 1: the tile is refused (a junction, a tile of another ride). A first click that is refused is dropped, a second one leaves the tool.</summary>
	Rejected,
	/// <summary>Answer 9: the tile belongs to the ride and has fewer than three arms, so it can end a cut.</summary>
	Accepted
}

/// <summary>Where the circuit of a ride ends when it is open, and the corner points on the way (<see cref="TrackEditor.FindOpenEnd"/>).</summary>
/// <param name="End">The tile the track tool goes on from (the original's line start <c>0x81EDE4/E8</c>).</param>
/// <param name="Corners">The recorded corner points, the start tile first and the end last, at most <see cref="TrackEditor.CornerCapacity"/> (<c>0x81B740</c>).</param>
/// <param name="ReachedStation">The walk stopped at a station tile whose arm points back (the circuit leads home).</param>
public sealed record TrackOpenEnd( (int X, int Y) End, IReadOnlyList<(int X, int Y)> Corners, bool ReachedStation );

/// <summary>The result of a cut between two picked tiles (<see cref="TrackEditor.Cut"/>).</summary>
/// <param name="Failed">The walk between the tiles left the track (<c>0x52B400</c> returned −1).</param>
/// <param name="Path">The tiles from the first pick to the second along the flow, both included.</param>
/// <param name="Cleared">The tiles the cut removed, in order (a tile another pass still uses stays, with one pass less).</param>
public sealed record TrackCutResult( bool Failed, IReadOnlyList<(int X, int Y)> Path, IReadOnlyList<(int X, int Y)> Cleared );

/// <summary>What the editor tells the level about a cut: a tile cleared (it refunds) and an add-on object standing on one (it is demolished).</summary>
public sealed class TrackEditHooks
{
	/// <summary>A tile was cleared for good (the original's <c>0x53B5FE</c> credit: the track cell cost × the ride's scrap percentage / 100).</summary>
	public Action<(int X, int Y)>? TileCleared { get; init; }
	/// <summary>The add-on object on a cleared tile goes with it (<c>0x527EE0</c> from <c>0x53B6E5</c>).</summary>
	public Action<(int X, int Y)>? RemoveAddOn { get; init; }
}

/// <summary>
/// The track edit sub-modes of the ride window's track button (tp.exe tool 0xE for karts, 0x13 for water): the pick test of the clicked tile, the cut of a closed circuit between
/// two picked tiles with the clearing of the tiles between them and the repairs at the cut's ends, the station repair that follows it and the walk to the open end of a broken circuit
/// where the line tool goes on (docs/TRACKS.md, "The track edit sub-modes"). The tiles are the level's own <see cref="TrackCellLayer"/> records, whose step numbers
/// (<see cref="TrackCell.Segment"/>) the last circuit walk wrote.
/// </summary>
public sealed class TrackEditor
{
	/// <summary>Corner points the open-end walk records (<c>0x81B740</c>: it stops recording at 0x401 entries).</summary>
	// [BIN:TPW-EXE:0x00530830 corner stack] the stack at 0x81B740 takes a point only while its count (0x820A8C) is at most 0x400, so it holds 0x401 points
	public const int CornerCapacity = 0x401;

	private readonly TrackBuilder builder;
	private readonly TrackStation station;
	private readonly TrackEditHooks hooks;

	/// <param name="builder">The ride's family builder over the level's track layer.</param>
	/// <param name="station">The ride's station: its first tile (<c>0x7639E0/E4</c>) and the tile before it (<c>0x7639E8/EC</c>) are the tiles a clear spares.</param>
	/// <param name="hooks">Refunds and add-on removals; null for a headless cut.</param>
	public TrackEditor( TrackBuilder builder, TrackStation station, TrackEditHooks? hooks = null )
	{
		this.builder = builder ?? throw new ArgumentNullException( nameof( builder ) );
		this.station = station;
		this.hooks = hooks ?? new TrackEditHooks();
	}

	private TrackCellLayer Tracks => builder.Tracks;
	/// <summary>The ride the edit works on (<see cref="TrackBuilder.OwnerOf"/>).</summary>
	public int Owner => builder.OwnerOf( station );

	// ---- The small tests of the original ---------------------------------------------------------------

	/// <summary>A cell with exactly two perpendicular arms (<c>0x53AE00</c>).</summary>
	// [BIN:TPW-EXE:0x0053AE00 corner test] two cardinal arms (0x522810) of which one is ±Y and one ±X: the mask, else 0
	internal static bool IsCorner( byte mask ) => TrackHeading.Count( mask ) == 2 && (mask & 0x11) != 0 && (mask & 0x44) != 0;

	/// <summary>A cell with exactly two opposite arms (<c>0x53AE50</c>).</summary>
	// [BIN:TPW-EXE:0x0053AE50 straight test] two cardinal arms of which both are ±Y or both ±X: the mask, else 0
	internal static bool IsStraight( byte mask ) => TrackHeading.Count( mask ) == 2 && ((mask & 0x11) == 0x11 || (mask & 0x44) == 0x44);

	/// <summary>A cell with three or four arms (<c>0x53AE90</c>: the mask when the cardinal arm count exceeds two).</summary>
	// [BIN:TPW-EXE:0x0053AE90 junction test] (arm count ≤ 2) − 1 ANDed with the mask: nonzero for three or four arms
	internal static bool IsJunction( byte mask ) => TrackHeading.Count( mask ) > 2 && mask != 0;

	private static byte Opposite( byte heading ) => BlueprintGhosts.Opposite( heading );

	private static (int X, int Y) Step( (int X, int Y) at, byte heading ) =>
		(at.X + TrackStation.TileCells * TrackHeading.Dx( heading ), at.Y + TrackStation.TileCells * TrackHeading.Dy( heading ));

	private (int X, int Y)? Origin( (int X, int Y) at ) => builder.OriginOf( at.X, at.Y );

	private bool IsStationTile( (int X, int Y) at ) => Origin( at ) is { } origin && TrackCellTypes.IsStation( Tracks.Get( origin.X, origin.Y ).Type );

	/// <summary>The tile two cells from <paramref name="at"/> toward <paramref name="heading"/>, when it holds a track tile of this ride (the original's <c>0x50C3E0(heading, 2)</c>).</summary>
	private (int X, int Y)? Neighbour( (int X, int Y) at, byte heading )
	{
		if ( Origin( Step( at, heading ) ) is not { } origin )
			return null;
		var cell = Tracks.Get( origin.X, origin.Y );
		return cell.IsOrigin && (cell.Owner == Owner || cell.Owner == 0 || Owner == 0) ? origin : null;
	}

	private void Update( (int X, int Y) at, Func<TrackCell, TrackCell> change )
	{
		var cell = Tracks.Get( at.X, at.Y );
		if ( !cell.IsOrigin )
			return;
		Tracks.Set( at.X, at.Y, change( cell ) );
	}

	private void RemoveArm( (int X, int Y) at, byte arms ) => Update( at, cell => { cell.Mask &= (byte)~arms; return cell; } );
	private void AddArm( (int X, int Y) at, byte arms ) => Update( at, cell => { cell.Mask |= arms; return cell; } );
	private void SetLink( (int X, int Y) at, byte link ) => Update( at, cell => { cell.Link = link; return cell; } );
	private void Repick( (int X, int Y) at ) => builder.Refresh( at.X, at.Y );

	// ---- The pick test --------------------------------------------------------------------------------

	/// <summary>
	/// The tile a click on (<paramref name="x"/>, <paramref name="y"/>) picks and the test's answer: an extension cell takes its origin (<c>0x53AD90</c>), a station tile is refused
	/// without a trace (<c>0x53ADB0</c>), a tile of this ride with fewer than three arms is answer 9, every other tile answer 1.
	/// </summary>
	// [BIN:TPW-EXE:0x00526830 first pick] the click takes the cell record, its origin for an extension cell (0x53AD90), and leaves a station tile alone (0x53ADB0 → 0x526C78); the step is read through 0x53AEC0 and the pair tests 0x539760(0xE or 0x13)
	// [BIN:TPW-EXE:0x0053A363 edit pick test] with the tool mode 0xE or 0x13 a tile of type 0xB or 0x10 whose ride word (+0x10) is the selected ride's answers 9 when its arm count (0x522810) is below three (0x53A37E: ((count < 3) − 1) & mask is zero); a junction falls through to the tail, where the mode differs from the tile type and the answer is 1 (0x53A4D8); any other cell ends in the same answer 1
	public (TrackEditPick Result, (int X, int Y)? Tile) Pick( int x, int y )
	{
		if ( Origin( (x, y) ) is not { } origin )
			return (TrackEditPick.None, null);
		var cell = Tracks.Get( origin.X, origin.Y );
		if ( !cell.IsOrigin || TrackCellTypes.IsStation( cell.Type ) )
			return (TrackEditPick.None, null);
		var ours = cell.Owner == Owner || cell.Owner == 0;
		return (ours && TrackHeading.Count( cell.Mask ) < 3 ? TrackEditPick.Accepted : TrackEditPick.Rejected, origin);
	}

	/// <summary>The step number of a tile (<c>0x53AEC0</c>: the word at +0x24, the origin's for an extension cell).</summary>
	public short StepOf( (int X, int Y) tile ) => Origin( tile ) is { } origin ? (short)Tracks.Get( origin.X, origin.Y ).Segment : (short)0;

	// ---- The open end ---------------------------------------------------------------------------------

	/// <summary>
	/// Walks the track from the tile after the station along the flow to where it ends and collects the corner points: the tile the line tool goes on from when the circuit is open.
	/// A corner turns by the reversed heading, a tile with no arm ahead ends the walk, and so does a station tile whose arm points back. The walk also sets the overlap word of every
	/// tile it passes (1 at corners and junctions, else 0), as the original's does.
	/// </summary>
	// [BIN:TPW-EXE:0x00530830 corner collector] from the station's exit cell (0x52A3A0: the attraction cell plus the rotation's offset, heading 4, 1, 0x40 or 0x10) over the tiles of type 0xB or 0x10 (0x53AD70): a corner (0x53AE00) turns to mask ^ the reversed heading (0x4D8C00, no fallback) and is pushed (≤ 0x401 points, 0x81B740), every tile gets its overlap word (0x5228A0) set to 1 for a corner or junction and to 0 else, a non-corner without the heading in its mask ends the walk (0x5309C0), and so does a station tile of the same ride (0x53ADB0, +0x10) whose mask holds the reversed heading, the walk then standing on it (0x530A05); the end (0x81EDE4/E8) is the last position and is pushed too (0x530A87)
	// [EXT:track-open-end-guard] a cell that is no track tile ends the walk at the last tile (at the exit cell when the track is empty) where the original would walk on over the empty cells for as long as the grid goes
	public TrackOpenEnd FindOpenEnd()
	{
		var corners = new List<(int X, int Y)>();
		var heading = station.Heading;
		var position = station.Exit;
		var last = position;
		var reached = false;
		var limit = Tracks.Count + 16;
		for ( var steps = 0; steps < limit; steps++ )
		{
			var cell = Tracks.Get( position.X, position.Y );
			if ( !IsLaidTile( cell ) )
				break;
			last = position;
			if ( steps == 0 && corners.Count < CornerCapacity )
				corners.Add( position );
			var stop = false;
			if ( IsCorner( cell.Mask ) )
			{
				heading = (byte)(cell.Mask ^ Opposite( heading ));
				if ( corners.Count < CornerCapacity )
					corners.Add( position );
				cell.Overlap = 1;
			}
			else
			{
				cell.Overlap = IsJunction( cell.Mask ) ? (short)1 : (short)0;
				if ( (heading & cell.Mask) == 0 )
					stop = true;
			}
			Tracks.Set( position.X, position.Y, cell );
			var next = Step( position, heading );
			if ( heading != 0 && Origin( next ) is { } nextOrigin )
			{
				var ahead = Tracks.Get( nextOrigin.X, nextOrigin.Y );
				if ( TrackCellTypes.IsStation( ahead.Type ) && ahead.Owner == cell.Owner && (ahead.Mask & Opposite( heading )) != 0 )
				{
					stop = true;
					reached = true;
					position = nextOrigin;
					last = position;
					break;
				}
			}
			if ( stop || heading == 0 )
				break;
			position = next;
		}
		if ( corners.Count < CornerCapacity )
			corners.Add( last );
		return new TrackOpenEnd( last, corners, reached );
	}

	/// <summary>
	/// Enters the line tool at the open end with the collected corner stack. Old and newly drawn segments use the same directional clear,
	/// refund and add-on hooks. Re-entering recomputes the stack from the remaining track rather than retaining stale edit history.
	/// </summary>
	public void EnterLineTool( CellBuildTool tool, Action? changed = null )
	{
		ArgumentNullException.ThrowIfNull( tool );
		var open = FindOpenEnd();
		var corners = open.Corners;
		// The shared tool has a smaller safe capacity than the original collector. If the collector overflowed, no captured point names the
		// actual end; do not manufacture a straight segment across unrecorded corners. Otherwise retain the most recent complete segments.
		if ( corners.Count == 0 || corners[^1] != open.End )
			corners = new[] { open.End };
		else if ( corners.Count > CellBuildTool.VertexCapacity )
			corners = corners.Skip( corners.Count - CellBuildTool.VertexCapacity ).ToArray();
		var segments = corners.Skip( 1 ).Select( (end, index) =>
			new SegmentResult( end, Array.Empty<(int X, int Y)>(), 0, CellBuildResult.Ok, false ) { Start = corners[index] } ).ToArray();
		tool.Enter( new TrackLineWriter( builder, changed, Owner, this ), open.End, corners, segments );
	}

	/// <summary>
	/// Takes a line back from its end to its start, using the directional form of the tile clear. A shared crossing retains its other
	/// flow and a shared corner becomes the new open endpoint. Only a tile actually cleared refunds and removes its add-on.
	/// </summary>
	// [BIN:TPW-EXE:0x0052FE50 Back] pop the last corner, click in mode 0x35 at it and the new stack top (0x52FF3A, 0x52FFB2); mode 0x35 runs line op 0x32 (0x526001), which calls 0x53B280 with the reverse line's step (0x538CCE)
	// [BIN:TPW-EXE:0x0053B393 directional clear] decrement the pass word; when the flow link has two or more bits (0x522860), clear the bit opposite the replay step (0x53B3FF..0x53B437) and both mask arms of its axis (0x53B437..0x53B477); otherwise spare the protected ends, remove only that opposite arm and, for a former corner left with one arm, reverse its flow link (0x53B47C..0x53B543)
	// [APPROX:TRACK-018] the recorded-corner replay and directional clear are built; the tool's 1024-point cap, existing station-layout approximation and full original replay/restore equivalence still lack executable-oracle coverage (TRACK-004/007 remain open)
	public void UndoLine( SegmentResult segment )
	{
		// Only axis-aligned, whole-tile segments belong to this writer. A stale or foreign segment must not clear an arbitrary rectangle.
		if ( !Tracks.InBounds( segment.Start.X, segment.Start.Y ) || !Tracks.InBounds( segment.SnappedEnd.X, segment.SnappedEnd.Y )
			|| segment.Start.X != segment.SnappedEnd.X && segment.Start.Y != segment.SnappedEnd.Y
			|| (segment.SnappedEnd.X - segment.Start.X) % TrackStation.TileCells != 0
			|| (segment.SnappedEnd.Y - segment.Start.Y) % TrackStation.TileCells != 0 )
			return;
		var line = TrackLineWriter.LayLine( segment.SnappedEnd, segment.Start );
		var back = line.Count > 1 ? TrackBuilder.HeadingBetween( line[0], line[1] ) : (byte)0;
		foreach ( var at in line )
		{
			var cell = Tracks.Get( at.X, at.Y );
			if ( !IsLaidTile( cell ) || cell.Type != builder.OriginType )
				continue;
			// The zero-step replay forces a clear; an ordinary reverse step consumes one of the collector's passes.
			cell.Overlap = back == 0 ? (short)-1 : (short)(cell.Overlap - 1);
			if ( back != 0 )
			{
				var forward = Opposite( back );
				if ( TrackHeading.Count( cell.Link ) >= 2 )
				{
					cell.Link &= (byte)~forward;
					cell.Mask &= (byte)~(back | forward);
				}
				else if ( at != station.Entry )
				{
					var corner = IsCorner( cell.Mask );
					cell.Mask &= (byte)~forward;
					if ( corner && TrackHeading.Count( cell.Mask ) == 1 )
						cell.Link = Opposite( cell.Mask );
				}
			}
			Tracks.Set( at.X, at.Y, cell );
			if ( cell.Overlap < 0 && at != station.Entry )
				ClearOrigin( at, cell );
			else
				Repick( at );
		}
		RepairStation();
	}

	/// <summary>A tile of the kart or water origin type of this ride (<c>0x53AD70</c>: type 0xB or 0x10).</summary>
	private bool IsLaidTile( TrackCell cell ) =>
		cell.IsOrigin && !TrackCellTypes.IsStation( cell.Type ) && cell.Type != TrackCellTypes.WaterAlternate && (cell.Owner == Owner || cell.Owner == 0 || Owner == 0);

	private static bool IsTrackType( TrackCell cell ) => TrackCellTypes.IsWalkable( cell.Type );

	// ---- The demolition replay ------------------------------------------------------------------------

	/// <summary>
	/// The clearing of a demolished ride's track (the two replays of <c>0x527EE0</c>): the walk from the tile after the station along the flow to the open end (<see cref="FindOpenEnd"/>) and then
	/// the walk from the tile before the station against the flow, each clearing the tiles it passes with the replay flag set (<c>0x81B46C</c>): a tile whose pass counter goes below zero
	/// is cleared and refunds, a junction loses one pass only, and a tile that no walk reaches (a branch, a detached piece) stays and refunds nothing. Returns the tiles it cleared.
	/// </summary>
	// [BIN:TPW-EXE:0x00527EE0 demolition replays] for a ride with a queue and track type 1 or 2: 0x530830 (the forward corner collector, which resets the pass counters along the chain: 1 at a corner or junction, else 0, 0x5228A0), the tool mode 0xB or 0x10, the flag 0x81B46C = 1 and 0x52FE50 until it answers 0 (each pop replays the click handler in mode 0x35 over the line between two corner points, clearing it with 0x53B280), then 0x530B70 (the same from the tile before the station, against the flow) and the same replay; 0x52FB60 or 0x52FB80 gives the cell cost the refund and the one charge use
	// [EXT:track-open-end-guard] a walk stops at the last laid tile when the next cell holds no track, where the original would walk on over empty cells; a chain that leaves a closed circuit's cleared tiles behind it simply ends
	public IReadOnlyList<(int X, int Y)> Demolish()
	{
		var cleared = new List<(int X, int Y)>();
		Replay( station.Exit, station.Heading, cleared );
		Replay( station.Entry, Opposite( station.Heading ), cleared );
		return cleared;
	}

	private void Replay( (int X, int Y) start, byte heading, List<(int X, int Y)> cleared )
	{
		// the walk that collects the corners also resets the pass counters (corner or junction 1, any other tile 0) before the lines are replayed
		var chain = new List<((int X, int Y) Tile, byte Heading, bool Crossing)>();
		var position = start;
		var limit = Tracks.Count + 16;
		for ( var steps = 0; steps < limit; steps++ )
		{
			var cell = Tracks.Get( position.X, position.Y );
			if ( !IsLaidTile( cell ) )
				break;
			var stop = false;
			if ( IsCorner( cell.Mask ) )
			{
				heading = (byte)(cell.Mask ^ Opposite( heading ));
				cell.Overlap = 1;
			}
			else
			{
				cell.Overlap = IsJunction( cell.Mask ) ? (short)1 : (short)0;
				stop = (heading & cell.Mask) == 0;
			}
			Tracks.Set( position.X, position.Y, cell );
			chain.Add( (position, heading, cell.Mask == 0x55) );
			if ( heading == 0 || stop )
				break;
			var next = Step( position, heading );
			if ( Origin( next ) is { } ahead && Tracks.Get( ahead.X, ahead.Y ) is var after && TrackCellTypes.IsStation( after.Type ) && (after.Mask & Opposite( heading )) != 0 )
				break;
			position = next;
		}
		foreach ( var (tile, traversed, crossing) in chain )
		{
			var cell = Tracks.Get( tile.X, tile.Y );
			if ( !cell.IsOrigin )
				continue;
			// Preserve the other flow of a supported two-line crossing. Other replay shapes and contrary laid-flow directions keep the existing policy.
			// [BIN:TPW-EXE:0x53B280 line step] with two or more link bits the line step clears dx -2 → 0x04, dx +2 → 0x40, dy -2 → 0x10, dy +2 → 0x01 from the link (0x5227f0) and both arms
			// of the step's axis from the mask (0x522730), the bit opposite to the step (the per-tile driver 0x53aac0 steps from the anchor to the click, and 0x5370e0 maps dx -2 to heading 0x40);
			// the replay 0x52FE50 pops the corner points the collector pushed in walk order (0x530830, 0x530B70: stack 0x81B740, count 0x820A8C) and runs each line from the later point
			// back to the earlier one (mode 0x35: anchor = popped point, click = the point below it), so the cleared bit is the chain's own heading, the traversed heading here
			var remaining = (byte)(cell.Link & ~traversed);
			if ( crossing && IsJunction( cell.Mask ) && cell.Overlap == 1 && (traversed & ~0x55) == 0 && TrackHeading.Count( traversed ) == 1
				&& (cell.Link & ~0x55) == 0 && TrackHeading.Count( cell.Link ) == 2 && (cell.Link & traversed) != 0
				&& remaining != Opposite( traversed ) )
			{
				cell.Link = remaining;
				Tracks.Set( tile.X, tile.Y, cell );
			}
			// a corner is an end of two replayed lines, so it is visited twice and its counter of 1 runs out; a plain tile is cleared on its one visit
			if ( ClearCell( tile, pass: IsJunction( cell.Mask ), replay: true ) )
				cleared.Add( tile );
		}
	}

	// ---- The cut --------------------------------------------------------------------------------------

	/// <summary>
	/// The cut between two picked tiles, <paramref name="first"/> the one with the lower step number: walks the flow from <paramref name="first"/> to <paramref name="second"/> and, with
	/// <paramref name="commit"/>, clears every tile between them (<see cref="ClearCell"/>), repairs the ends and returns the tiles it removed. Without it the walk only reports the
	/// tiles it passes, as the hover does.
	/// </summary>
	// [BIN:TPW-EXE:0x0052B400 cut] the first walk (heading = the first tile's flow link, a corner turning to mask ^ the reversed heading from the second tile on) ends at the second pick and, committing, writes the heading as each tile's flow link (ORed on a junction) and finds the cut's ends by equal step number (a junction before the step matches stands in for it); the second walk clears the tiles from the first end to the second (a junction loses one pass: 0x53B280(−1, −1), any other straight, corner or stub is cleared: 0x53B280(0, 0)); the tail re-links the ends (0x53BC60) when the first tile survived as a straight or corner
	// [EXT:track-walk-step-limit] the walks of the cut are cut off after as many tiles as the layer holds plus the station, where the original has no limit and would not leave a flow that never reaches the second tile
	public TrackCutResult Cut( (int X, int Y) first, (int X, int Y) second, bool commit )
	{
		var path = new List<(int X, int Y)>();
		var cleared = new List<(int X, int Y)>();
		if ( Origin( first ) is not { } a || Origin( second ) is not { } b )
			return new TrackCutResult( true, path, cleared );
		var stepA = StepOf( a );
		var stepB = StepOf( b );
		if ( commit && stepB - stepA < 0 )
			return new TrackCutResult( false, path, cleared );

		(int X, int Y)? endA = null, endB = null;
		var endAJunction = false;
		var endBJunction = false;
		var heading = Tracks.Get( a.X, a.Y ).Link;
		var position = a;
		var limit = Tracks.Count + 16;
		var count = 0;
		bool last;
		do
		{
			if ( count > limit || Origin( position ) is not { } at )
				return new TrackCutResult( true, path, cleared );
			var cell = Tracks.Get( at.X, at.Y );
			if ( !IsTrackType( cell ) )
				return new TrackCutResult( true, path, cleared );
			var step = (short)cell.Segment;
			if ( commit )
			{
				if ( endA == null )
				{
					if ( step == stepA )
					{
						endAJunction = false;
						endA = at;
					}
					if ( endA == null && IsJunction( cell.Mask ) )
					{
						endAJunction = true;
						endA = at;
					}
				}
				if ( step == stepB )
				{
					endBJunction = false;
					endB = at;
				}
				if ( endB == null && IsJunction( cell.Mask ) )
				{
					endBJunction = true;
					endB = at;
				}
			}
			if ( IsCorner( cell.Mask ) && count != 0 )
			{
				var back = Opposite( heading );
				if ( (back & cell.Mask) == 0 )
					back = heading;
				heading = (byte)(cell.Mask ^ back);
			}
			path.Add( at );
			last = at == b;
			if ( commit )
			{
				cell.Link = IsJunction( cell.Mask ) ? (byte)(cell.Link | heading) : heading;
				Tracks.Set( at.X, at.Y, cell );
			}
			count++;
			position = Step( at, heading );
			if ( heading == 0 )
				return new TrackCutResult( true, path, cleared );
		}
		while ( !last );
		if ( !commit )
			return new TrackCutResult( false, path, cleared );

		// the second walk clears from the first end to the second
		if ( endA is { } clearFrom && endB is { } clearTo && clearFrom != clearTo )
		{
			var startCell = Tracks.Get( clearFrom.X, clearFrom.Y );
			var along = IsJunction( startCell.Mask ) ? Tracks.Get( a.X, a.Y ).Link : startCell.Link;
			position = clearFrom;
			var stop = false;
			count = 0;
			do
			{
				if ( count++ > limit )
					break;
				var at = Origin( position );
				if ( at is not { } tile )
					break;
				var cell = Tracks.Get( tile.X, tile.Y );
				if ( !IsTrackType( cell ) )
					stop = true;
				else if ( TrackHeading.Count( cell.Link ) == 1 )
					along = cell.Link;
				else
				{
					cell.Link = (byte)(cell.Link & ~along);
					Tracks.Set( tile.X, tile.Y, cell );
				}
				if ( tile == clearTo )
					stop = true;
				if ( IsJunction( cell.Mask ) )
				{
					if ( ClearCell( tile, pass: true ) )
						cleared.Add( tile );
				}
				else if ( IsStraight( cell.Mask ) || IsCorner( cell.Mask ) || TrackHeading.Count( cell.Mask ) == 1 )
				{
					if ( ClearCell( tile, pass: false ) )
						cleared.Add( tile );
				}
				position = Step( tile, along );
				if ( along == 0 )
					break;
			}
			while ( !stop );
		}

		// the tail: the cell the first pick stands on, when it survived as a straight or a corner, takes the cut's re-links
		var firstCell = Tracks.Get( a.X, a.Y );
		var survived = firstCell.IsOrigin && (IsStraight( firstCell.Mask ) || IsCorner( firstCell.Mask ));
		if ( stepB - stepA < 2 && survived )
			Relink( a, 0 );
		if ( endAJunction && survived )
			Relink( a, 0 );
		if ( endBJunction && survived )
			Relink( b, Opposite( firstCell.Link ) );
		if ( endA is { } sameA && endB is { } sameB && sameA == sameB && survived && ClearCell( sameA, pass: false ) )
			cleared.Add( sameA );
		return new TrackCutResult( false, path, cleared );
	}

	// ---- Clearing a tile ------------------------------------------------------------------------------

	/// <summary>
	/// The clear of one tile for a cut (<c>0x53B280</c> with the cut's arguments): a pass of a junction (<paramref name="pass"/>, arguments −1, −1) takes one pass off the overlap
	/// counter and removes the arms of the pass that the tile's flow link no longer names, any other tile is marked for clearing outright (arguments 0, 0). A tile whose counter
	/// went below zero is then cleared and refunds, except the first station tile and the tile before it, which stay. Returns whether the tile was cleared.
	/// </summary>
	// [BIN:TPW-EXE:0x0053B280 clear cell] the forced clear (0, 0) writes the overlap word 0xFFFF (0x53B388) unless the tile is the first station tile; a pass (−1, −1) decrements it (0x53B393), removes the arms across the remaining link (0x53B3A7: link 1 or 0x10 takes 4 and 0x40, link 4 or 0x40 takes 1 and 0x10) and chooses the piece again (0x53B54A); a counter below zero clears the tile (0x53B54A..0x53B8BB) when it is neither the first station tile (0x7639E0/E4) nor the tile before it (0x7639E8/EC), or when the replay flag 0x81B46C is set: the refund (0x53B622), the arms of the tile and the arms of its neighbours pointing at it (0x53B634..0x53B6D9), the add-on object (0x53B6E5), the link, flags and step word (0x53B70B..0x53B73E) and the three extension cells (0x53B751..0x53B8BB)
	internal bool ClearCell( (int X, int Y) at, bool pass, bool replay = false )
	{
		var cell = Tracks.Get( at.X, at.Y );
		if ( !cell.IsOrigin )
			return false;
		var isFirst = at == station.First;
		var isEntry = at == station.Entry;
		if ( !pass && !isFirst )
		{
			cell.Overlap = -1;
			Tracks.Set( at.X, at.Y, cell );
		}
		else
		{
			cell.Overlap--;
			Tracks.Set( at.X, at.Y, cell );
			if ( pass )
			{
				switch ( cell.Link )
				{
					case TrackHeading.NegY:
					case TrackHeading.PosY:
						RemoveArm( at, (byte)(TrackHeading.PosX | TrackHeading.NegX) );
						break;
					case TrackHeading.PosX:
					case TrackHeading.NegX:
						RemoveArm( at, (byte)(TrackHeading.NegY | TrackHeading.PosY) );
						break;
				}
			}
			Repick( at );
		}
		cell = Tracks.Get( at.X, at.Y );
		if ( cell.Overlap >= 0 || (!replay && (isFirst || isEntry)) )
			return false;
		ClearOrigin( at, cell );
		return true;
	}

	private void ClearOrigin( (int X, int Y) at, TrackCell cell )
	{
		hooks.TileCleared?.Invoke( at );
		var arms = cell.Mask;
		foreach ( var heading in TrackHeading.All )
		{
			if ( (arms & heading) == 0 )
				continue;
			RemoveArm( at, heading );
			if ( Neighbour( at, heading ) is not { } near )
				continue;
			RemoveArm( near, Opposite( heading ) );
			Repick( near );
			var other = Tracks.Get( near.X, near.Y );
			if ( TrackHeading.Count( other.Mask ) == 1 && other.Link != other.Mask )
			{
				var across = Opposite( other.Mask );
				if ( other.Link != across )
					SetLink( near, across );
			}
		}
		if ( (cell.Flags & TrackCell.FlagAddOn) != 0 )
			hooks.RemoveAddOn?.Invoke( at );
		builder.EraseTile( at.X, at.Y );
	}

	// ---- The ends of a cut ----------------------------------------------------------------------------

	/// <summary>
	/// The re-link of a cut's end (<c>0x53BC60</c>): the tile before the station loses the arms its flow link does not run along, any other tile the arm toward
	/// <paramref name="heading"/> (its flow link when 0), each with the neighbour's arm back; a tile next to the first station tile gives that tile the arm toward its station partner.
	/// </summary>
	// [BIN:TPW-EXE:0x0053BC60 end relink] at the tile before the station (0x7639E8/EC) the arms outside the link go (0x53BCC3..0x53BD1E), elsewhere the given heading or the link (0x53BD2B..0x53BD85); the overlap word counts down when it is not zero (0x53BCE2, 0x53BD52); next to the first station tile (0x7639E0/E4) the arms toward its station partner come back (0x53BD8E..0x53BF1F)
	private void Relink( (int X, int Y) at, byte heading )
	{
		var cell = Tracks.Get( at.X, at.Y );
		if ( !cell.IsOrigin )
			return;
		var link = cell.Link;
		if ( at == station.Entry )
		{
			var drop = (byte)(cell.Mask & ~link);
			if ( drop == 0 )
				return;
			RemoveArm( at, drop );
			Repick( at );
			Overlap( at );
			if ( Neighbour( at, drop ) is { } near )
			{
				RemoveArm( near, Opposite( drop ) );
				Repick( near );
			}
			return;
		}
		if ( heading != 0 )
			link = heading;
		if ( link != 0 )
		{
			RemoveArm( at, link );
			Repick( at );
			Overlap( at );
			if ( Neighbour( at, link ) is { } near )
			{
				RemoveArm( near, Opposite( link ) );
				Repick( near );
			}
		}
		var gap = TrackStation.TileCells;
		var home = station.First;
		var adjacent = (at.X + gap == home.X && at.Y == home.Y) || (at.X == home.X && at.Y + gap == home.Y) || (at.X - gap == home.X && at.Y == home.Y) || (at.X == home.X && at.Y - gap == home.Y);
		if ( !adjacent )
			return;
		foreach ( var direction in TrackHeading.All )
		{
			var partner = Step( home, direction );
			if ( Origin( partner ) is { } other && TrackCellTypes.IsStation( Tracks.Get( other.X, other.Y ).Type ) )
			{
				AddArm( home, direction );
				Repick( home );
			}
		}
	}

	private void Overlap( (int X, int Y) at ) => Update( at, cell =>
	{
		if ( cell.Overlap != 0 )
			cell.Overlap--;
		return cell;
	} );

	/// <summary>
	/// The repair of the first station tile after an edit (<c>0x52FC80</c>): an arm toward a station tile of the ride that the tile lacks comes back with the flow link
	/// turned to it, an arm toward a cell that holds no track, or a track whose mask does not point back, goes.
	/// </summary>
	// [BIN:TPW-EXE:0x0052FC80 station repair] for each of the four headings from 1 (0x4D8C20 turns by 90 degrees): without the arm, a station tile of the same ride (+0x10) two cells on gets it, with the flow link turned back (0x5227E0) and the piece chosen again (0x52FD73); with it, a cell that is no track tile and no station tile, or a track tile whose mask lacks the reversed heading, loses it (0x52FD6B)
	public void RepairStation()
	{
		var home = station.First;
		if ( !Tracks.InBounds( home.X, home.Y ) || !TrackCellTypes.IsWalkable( Tracks.Get( home.X, home.Y ).Type ) )
			return;
		foreach ( var heading in TrackHeading.All )
		{
			var cell = Tracks.Get( home.X, home.Y );
			var next = Step( home, heading );
			var neighbour = Origin( next );
			if ( (cell.Mask & heading) == 0 )
			{
				if ( neighbour is { } other )
				{
					var theirs = Tracks.Get( other.X, other.Y );
					if ( TrackCellTypes.IsStation( theirs.Type ) && theirs.Owner == cell.Owner )
					{
						SetLink( home, Opposite( heading ) );
						AddArm( home, heading );
						Repick( home );
					}
				}
				continue;
			}
			var remove = true;
			if ( neighbour is { } found )
			{
				var theirs = Tracks.Get( found.X, found.Y );
				remove = !TrackCellTypes.IsWalkable( theirs.Type ) && !TrackCellTypes.IsStation( theirs.Type )
					|| TrackCellTypes.IsWalkable( theirs.Type ) && (Opposite( heading ) & theirs.Mask) == 0;
			}
			if ( remove )
			{
				RemoveArm( home, heading );
				Repick( home );
			}
		}
	}
}

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

	/// <summary>
	/// The tile the original's globals <c>0x7639E0/E4</c> name, which the clear op, its exit tail, the end re-link and the station repair treat as "the exit". The globals are the track tool's: a ride's
	/// select <c>0x5276D0</c> writes the ride's exit tile there (through <c>0x52A3A0</c>) and its entry tile into <c>0x7639E8/EC</c> (<c>0x52A420</c>), the line tool's start does the same, and the edit
	/// tool's hover (<c>0x527960</c> at <c>0x527A17</c>, every frame) writes the ride's raw anchor cell as the exit instead, so during a cut no tile is the exit (<see cref="UseEditToolExit"/>). They keep
	/// what the last of those wrote (the level's <see cref="TrackToolGlobals"/>), whichever ride that was.
	/// </summary>
	// [BIN:TPW-EXE:0x00527A17 hover exit] the hover copies the ride anchor 0x7639A4/A8 into 0x7639E0/E4 and 0x7639E8/EC without the 0x52A3A0/0x52A420 mapping (only its local copies go through them, 0x527A41, and the entry 0x7639E8 is set right at 0x527A5F); the line tool's tool start (0x5276D0 at 0x52788A, the placement at 0x5255F0 then 0x52A3A0) leaves the exit tile there
	public (int X, int Y) ToolExit => globals.Exit;

	/// <summary>The tile the original's globals <c>0x7639E8/EC</c> name: the entry tile the clear op spares and the end re-link works at.</summary>
	public (int X, int Y) ToolEntry => globals.Entry;

	private readonly TrackToolGlobals globals;

	/// <summary>The edit tool's hover is running: <see cref="ToolExit"/> is the ride's anchor cell, which is no track tile (a cut may clear the exit tile, the exit tail never runs, the repair after it finds no track).</summary>
	public void UseEditToolExit()
	{
		globals.Exit = station.Anchor;
		globals.Entry = station.Entry;
	}

	/// <summary>The line tool and Backspace: the tool's start wrote the ride's exit and entry tiles (<see cref="ToolExit"/>, <see cref="ToolEntry"/>).</summary>
	public void UseLineToolExit()
	{
		globals.Exit = station.Exit;
		globals.Entry = station.Entry;
	}

	/// <param name="builder">The ride's family builder over the level's track layer.</param>
	/// <param name="station">The ride's station.</param>
	/// <param name="hooks">Refunds and add-on removals; null for a headless cut.</param>
	/// <param name="shared">The track tool's globals the level keeps (<see cref="TrackToolGlobals"/>); null gives the editor globals of its own that start at the ride's exit and entry tiles (a headless cut, a prediction).</param>
	public TrackEditor( TrackBuilder builder, TrackStation station, TrackEditHooks? hooks = null, TrackToolGlobals? shared = null )
	{
		this.builder = builder ?? throw new ArgumentNullException( nameof( builder ) );
		this.station = station;
		this.hooks = hooks ?? new TrackEditHooks();
		globals = shared ?? new TrackToolGlobals( station.Exit, station.Entry );
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

	/// <summary>
	/// The symbol the edit tool's ghost draws on a tile (<c>0x539760</c> with the tool mode 0xE or 0x13, and with 0xF for the cut's hover): <see cref="BlueprintMarker.Break"/> (the chooser's answer 9) for a
	/// tile the pick test accepts, <see cref="BlueprintMarker.Erase"/> (0xD) for a tile of the cut, which is a tile of this ride; null where the answer is 1 (the refused red).
	/// </summary>
	// [BIN:TPW-EXE:0x0053A36C chooser answer 9] the tool mode 0xE or 0x13 on a tile of type 0xB (0x53A36C..0x53A3A6) or 0x10 (0x53A3B5..0x53A3F4) whose ride word is the selected ride's with at most two arms answers 9, the texture m_break (the dynamic table 0x763B3C: 0 blue, 1 red, 2 orange, 3 m_enter, 4 m_exit, 5 m_direct, 6 m_front, 7 m_inout, 8 m_link, 9 m_break, 10 m_cross, 11 m_end, 12 m_nocash, 13 m_erase)
	// [BIN:TPW-EXE:0x0053A326 chooser mode 0xF] the cut's ghost op 0x10F (0x52B400 at 0x52B687, the pair's lines between the corner points) asks with the mode 0xF: a tile of type 0xB or 0x10 of the selected ride answers 0xD (0x53A32B..0x53A342), the texture m_erase; any other type but 0x19 answers 1 (0x53A351..0x53A35A), the red face
	public BlueprintMarker? GhostSymbol( (int X, int Y) tile, bool cut )
	{
		if ( Origin( tile ) is not { } origin )
			return null;
		var cell = Tracks.Get( origin.X, origin.Y );
		var ours = cell.IsOrigin && !TrackCellTypes.IsStation( cell.Type ) && (cell.Owner == Owner || cell.Owner == 0 || Owner == 0);
		if ( cut )
			return ours ? BlueprintMarker.Erase : cell.Type == TrackCellTypes.WaterAlternate ? null : BlueprintMarker.RefusedTile;
		if ( !ours )
			return null;
		return TrackHeading.Count( cell.Mask ) < 3 ? BlueprintMarker.Break : null;
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
		// the stack stays as the collector leaves it: a walk that ends at the exit leaves the exit twice, and Backspace takes the zero-length line back with the step (0, 0) (UndoLine)
		var (corners, last, reached) = CollectForward();
		return new TrackOpenEnd( last, corners, reached );
	}

	/// <summary>
	/// [EXT:online-coop-claims] The tiles the open-end collector passes, the station tile it stops on included (C5-3, a ride's protected track: <see cref="TrackProvenance"/>). It runs as
	/// <see cref="FindOpenEnd"/> runs, writing the overlap words it writes, so it runs on a throwaway copy of the track layer.
	/// </summary>
	internal IReadOnlyList<(int X, int Y)> CollectorTiles()
	{
		var visited = new List<(int X, int Y)>();
		CollectForward( visited );
		return visited;
	}

	/// <summary>
	/// The corner collector of <c>0x530830</c> as the original leaves its stack: the exit (when it is a laid tile), every corner and the end of the walk, each pushed while the stack holds
	/// at most <see cref="CornerCapacity"/> points (so a walk that ends at the exit leaves the exit twice). The walk also writes the overlap word of every tile it passes, and hands each
	/// to <paramref name="visited"/>.
	/// </summary>
	private (List<(int X, int Y)> Corners, (int X, int Y) Last, bool Reached) CollectForward( List<(int X, int Y)>? visited = null )
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
			visited?.Add( position );
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
					visited?.Add( position );
					break;
				}
			}
			if ( stop || heading == 0 )
				break;
			position = next;
		}
		if ( corners.Count < CornerCapacity )
			corners.Add( last );
		return (corners, last, reached);
	}

	/// <summary>
	/// The corner collector of <c>0x530B70</c>, which the demolition runs from the tile before the station against the flow: the entry (when it is a laid tile), every corner and the end of the
	/// walk, which stops on the last laid tile before a station tile of the ride whose arm points back. It also writes the overlap word of every tile it passes (1 at a corner or junction, else 0,
	/// and 0 on the exit tile).
	/// </summary>
	// [BIN:TPW-EXE:0x00530B70 backward collector] from the entry cell (0x52A420: the tile before the station, heading against the flow) over the tiles of type 0xB or 0x10 (0x53AD70): a corner turns to mask ^ the reversed heading and is pushed (≤ 0x401 points), the overlap word is 1 at a corner or junction and 0 else (0x5228A0), the exit tile's is 0 (0x530D26..0x530D34: the cell equal to 0x7639E0/E4), a non-corner without the heading in its mask ends the walk, and so does a station tile of the ride (0x53ADB0, +0x10) two cells ahead whose mask holds the reversed heading, the walk then standing on the last laid tile (unlike 0x530830, which steps onto the station tile); the end is pushed too
	// [EXT:track-open-end-guard] a cell that is no laid tile ends the walk where the original would walk on over the empty cells
	private List<(int X, int Y)> CollectBackward()
	{
		var stack = new List<(int X, int Y)>();
		var heading = Opposite( station.Heading );
		var position = station.Entry;
		if ( IsLaidTile( Tracks.Get( position.X, position.Y ) ) && stack.Count < CornerCapacity )
			stack.Add( position );
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
				if ( stack.Count < CornerCapacity )
					stack.Add( position );
				cell.Overlap = 1;
			}
			else
			{
				cell.Overlap = IsJunction( cell.Mask ) ? (short)1 : (short)0;
				if ( (heading & cell.Mask) == 0 )
					stop = true;
			}
			if ( position == ToolExit )
				cell.Overlap = 0;
			Tracks.Set( position.X, position.Y, cell );
			if ( heading != 0 && Origin( Step( position, heading ) ) is { } ahead
				&& Tracks.Get( ahead.X, ahead.Y ) is var after && TrackCellTypes.IsStation( after.Type ) && after.Owner == cell.Owner && (after.Mask & Opposite( heading )) != 0 )
				stop = true;
			if ( stop || heading == 0 )
				break;
			position = Step( position, heading );
		}
		if ( stack.Count < CornerCapacity )
			stack.Add( position );
		return stack;
	}

	/// <summary>
	/// Enters the line tool at the open end with the collected corner stack. Old and newly drawn segments use the same directional clear,
	/// refund and add-on hooks. Re-entering recomputes the stack from the remaining track rather than retaining stale edit history.
	/// </summary>
	/// <param name="wrap">Wraps the line writer the tool holds ([EXT:online-coop-commands]: the level's commands, <see cref="ParkCommandTrackWriter"/>); null hands the tool the writer itself.</param>
	/// <param name="walked">The open end a command's walk found (<see cref="OpenTrackLineToolCommand"/>, <see cref="CutTrackCommand"/>): the walk writes the tiles' overlap words, so it runs in the
	/// command on every peer and the tool only takes its corners; null walks here.</param>
	public void EnterLineTool( CellBuildTool tool, Action? changed = null, Func<ICellLineWriter, ICellLineWriter>? wrap = null, TrackOpenEnd? walked = null )
	{
		ArgumentNullException.ThrowIfNull( tool );
		UseLineToolExit();
		var open = walked ?? FindOpenEnd();
		// [BIN:TPW-EXE:0x00530A87 collector end] the end of the walk is pushed only while the stack has room (a count of at most 0x400), so a walk that recorded 0x401 corner points leaves the stack without it and the tool starts at the end all the same (0x81EDE4/E8): the stack is the collector's, as the original keeps it
		var corners = open.Corners.Count == 0 ? new[] { open.End } : open.Corners;
		var segments = corners.Skip( 1 ).Select( (end, index) =>
			new SegmentResult( end, Array.Empty<(int X, int Y)>(), 0, CellBuildResult.Ok, false ) { Start = corners[index] } ).ToArray();
		ICellLineWriter writer = new TrackLineWriter( builder, changed, Owner, this );
		tool.Enter( wrap?.Invoke( writer ) ?? writer, open.End, corners, segments );
	}

	/// <summary>
	/// Takes a line back from its end to its start, using the directional form of the tile clear. A shared crossing retains its other
	/// flow and a shared corner becomes the new open endpoint. Only a tile actually cleared refunds and removes its add-on.
	/// </summary>
	// [BIN:TPW-EXE:0x0052FE50 Back] pop the last corner, click in mode 0x35 at it and the new stack top (0x52FF3A, 0x52FFB2): the first click only sets the line start (0x52F200 reset it), the second runs line op 0x32 (0x526001: 0x53AAC0 over the line between the two points, every tile of it, both ends included) whose per-tile handler calls 0x53B280 with the line's step (0x5370E0, 0x538CCE); a zero-length line clears with (0, 0)
	// [BIN:TPW-EXE:0x0053B280 line step clear] ClearStep: a step of (±2, 0) or (0, ±2) takes the line's direction out of the tile as the cell op does
	public void UndoLine( SegmentResult segment )
	{
		// Only axis-aligned, whole-tile segments belong to this writer. A stale or foreign segment must not clear an arbitrary rectangle.
		if ( !Tracks.InBounds( segment.Start.X, segment.Start.Y ) || !Tracks.InBounds( segment.SnappedEnd.X, segment.SnappedEnd.Y )
			|| segment.Start.X != segment.SnappedEnd.X && segment.Start.Y != segment.SnappedEnd.Y
			|| (segment.SnappedEnd.X - segment.Start.X) % TrackStation.TileCells != 0
			|| (segment.SnappedEnd.Y - segment.Start.Y) % TrackStation.TileCells != 0 )
			return;
		UseLineToolExit();
		ReplayLine( segment.SnappedEnd, segment.Start, replay: false, cleared: null );
		// [BIN:TPW-EXE:0x00530005 repair after a pop] every pop repairs the exit tile (0x52FC80), with the flag set when the stack holds one point after the pop (0x52FF73)
		RepairStation( segment.IsFirstLine );
	}

	/// <summary>
	/// The replay of one line from <paramref name="from"/> (the later point) to <paramref name="to"/> (the earlier one) through the clear op: every tile of the line, both ends included, takes the line's
	/// step (<c>0x53AAC0</c> calling <c>0x53B280</c>), and a zero-length line clears its one tile with the step (0, 0). Only tiles of this ride (and its station tiles) are touched.
	/// </summary>
	private void ReplayLine( (int X, int Y) from, (int X, int Y) to, bool replay, List<(int X, int Y)>? cleared )
	{
		var line = TrackLineWriter.LayLine( from, to );
		var back = line.Count > 1 ? TrackBuilder.HeadingBetween( line[0], line[1] ) : (byte)0;
		var (dx, dy) = (TrackStation.TileCells * TrackHeading.Dx( back ), TrackStation.TileCells * TrackHeading.Dy( back ));
		foreach ( var at in line )
		{
			var cell = Tracks.Get( at.X, at.Y );
			if ( !cell.IsOrigin || cell.Type != builder.OriginType && !TrackCellTypes.IsStation( cell.Type ) || !(cell.Owner == Owner || cell.Owner == 0 || Owner == 0) )
				continue;
			if ( ClearCell( at, dx, dy, replay ) )
				cleared?.Add( at );
		}
	}

	/// <summary>A tile of the kart or water origin type of this ride (<c>0x53AD70</c>: type 0xB or 0x10).</summary>
	private bool IsLaidTile( TrackCell cell ) =>
		cell.IsOrigin && !TrackCellTypes.IsStation( cell.Type ) && cell.Type != TrackCellTypes.WaterAlternate && (cell.Owner == Owner || cell.Owner == 0 || Owner == 0);

	private static bool IsTrackType( TrackCell cell ) => TrackCellTypes.IsWalkable( cell.Type );

	// ---- The demolition replay ------------------------------------------------------------------------

	/// <summary>
	/// The clearing of a demolished ride's track (the two replays of <c>0x527EE0</c>): the corner points of the walk from the tile after the station along the flow to the open end
	/// (<c>0x530830</c>) and then of the walk from the tile before the station against the flow (<c>0x530B70</c>), each replayed as the lines between consecutive corner points, latest
	/// first (<c>0x52FE50</c>), through the clear op with the replay flag set (<c>0x81B46C</c>): a tile whose pass counter goes below zero is cleared and refunds, a junction loses one
	/// pass only, and a tile that no walk reaches (a branch, a detached piece) stays and refunds nothing. Returns the tiles it cleared.
	/// </summary>
	// [BIN:TPW-EXE:0x00527EE0 demolition replays] for a ride with a queue and track type 1 or 2: 0x530830 (the forward corner collector, which resets the pass counters along the chain: 1 at a corner or junction, else 0, 0x5228A0), the tool mode 0xB or 0x10, the flag 0x81B46C = 1 and 0x52FE50 until it answers 0 (each pop replays the click handler in mode 0x35 over the line between two corner points, clearing it with 0x53B280), then 0x530B70 (the same from the tile before the station, against the flow) and the same replay; 0x52FB60 or 0x52FB80 gives the cell cost the refund and the one charge use
	// [BIN:TPW-EXE:0x0052FE50 replay pops] the stack 0x81B740 (count 0x820A8C) pops its top point (0x52FEA1..0x52FEC9), the click handler takes it as the line start and the new top as the end (0x52FF3A, 0x52FFB2): a stack of n points replays the n − 1 lines n → n − 1, …, 2 → 1, each tile of a line (both ends) cleared with the line's step, so a corner is cleared twice (its pass counter is 1), a straight once; the bottom point is no line of its own, and with one point or none nothing is replayed
	// [EXT:track-open-end-guard] a walk stops at the last laid tile when the next cell holds no track, where the original would walk on over empty cells; a chain that leaves a closed circuit's cleared tiles behind it simply ends
	public IReadOnlyList<(int X, int Y)> Demolish()
	{
		var cleared = new List<(int X, int Y)>();
		// the demolition does not select the ride (0x527EE0 calls neither 0x5276D0 nor a tool start): the globals are whatever the last tool or select left
		ReplayStack( CollectForward().Corners, cleared );
		ReplayStack( CollectBackward(), cleared );
		return cleared;
	}

	// [BIN:TPW-EXE:0x0052FE50 repair per pop] every call of the replay pop (0x52FE50) runs the station repair 0x52FC80 afterwards (0x530005) with the flag set for the pop that leaves one point; the call that finds no point to pop (the stack ends with one point) repairs once more without the flag (0x530062) and answers 0
	private void ReplayStack( List<(int X, int Y)> stack, List<(int X, int Y)> cleared )
	{
		for ( var top = stack.Count; top >= 2; top-- )
		{
			ReplayLine( stack[top - 1], stack[top - 2], replay: true, cleared );
			RepairStation( top == 2 );
		}
		RepairStation( false );
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
	/// The clear of one tile for a cut (<c>0x53B280</c> with the cut's arguments): a pass of a junction (<paramref name="pass"/>, arguments −1, −1) or any other tile marked for clearing
	/// outright (arguments 0, 0); see <see cref="ClearCell(ValueTuple{int, int}, int, int, bool)"/>. Returns whether the tile was cleared.
	/// </summary>
	internal bool ClearCell( (int X, int Y) at, bool pass, bool replay = false ) => ClearCell( at, pass ? -1 : 0, pass ? -1 : 0, replay );

	/// <summary>
	/// The clear op of one tile, <c>0x53B280</c>: <paramref name="dx"/>, <paramref name="dy"/> are its two arguments, (0, 0) for a forced clear, (−1, −1) for a pass of a junction (a cut) and
	/// (±2, 0) or (0, ±2) for the step of a replayed line. The tile's overlap word (+0x20) goes to −1 for a forced clear (a tile that is not the exit) and else counts down; the arms and the
	/// flow link follow the arguments; a tile whose counter went below zero is then cleared and refunds, except the exit and entry tiles, which stay unless <paramref name="replay"/> (the
	/// demolition's flag <c>0x81B46C</c>); a surviving exit tile takes back the arm toward the station tile beside it (<see cref="RestoreExit"/>). Returns whether the tile was cleared.
	/// </summary>
	// [BIN:TPW-EXE:0x0053B280 clear cell] with (0, 0) the overlap word is written 0xFFFF (0x53B388) unless the tile is the exit tile (0x7639E0/E4), which counts down like every other call (0x53B393); (−1, −1) removes the arms across the remaining link (0x53B3A7: link 1 or 0x10 takes 4 and 0x40, link 4 or 0x40 takes 1 and 0x10, by 0x522850 and 0x522730); any other step, with at most one link bit (0x522860 < 2), spares the exit and entry tiles (0x53B3FA..0x53B46E, 0x53B54A), removes the arm opposite to the step (dx −2 → 4, +2 → 0x40, dy −2 → 0x10, +2 → 1) and, for a former corner left with one arm, points the link away from it (0x53B4AA..0x53B543); with two or more link bits it takes the opposite bit out of the link (0x5227F0) and both arms of the step's axis (0x522730); the piece is chosen again (0x53AFA0, 0x53B54A) whenever the arms were looked at
	// [BIN:TPW-EXE:0x0053B54A clear test] a counter below zero clears the tile (0x53B57F) when it is neither the exit tile nor the entry tile (0x7639E8/EC), or when the replay flag 0x81B46C is set; the flag 0x81D7A8 of the queue demolition (0x527FAC, 0x528015) clears regardless and is never set while a track tool runs
	// [BIN:TPW-EXE:0x0053B8BB exit tail] a surviving exit tile (not during a replay) looks at its four sides (0x4D8C20 turns from 1): a side without an arm whose neighbour two cells on is a station tile with the same ride word (+0x10) gets the arm and the flow link turned to it (0x5227E0, 0x522700, 0x53AFA0); then, with exactly one arm (0x522790 = 1), the link is cleared (0x5227F0) and set to the reverse of each arm that faces such a station tile
	internal bool ClearCell( (int X, int Y) at, int dx, int dy, bool replay = false )
	{
		var cell = Tracks.Get( at.X, at.Y );
		if ( !cell.IsOrigin )
			return false;
		var isExit = at == ToolExit;
		var isEntry = at == ToolEntry;
		if ( dx == 0 && dy == 0 && !isExit )
		{
			cell.Overlap = -1;
			Tracks.Set( at.X, at.Y, cell );
		}
		else
		{
			cell.Overlap--;
			Tracks.Set( at.X, at.Y, cell );
			var repick = true;
			if ( dx == -1 && dy == -1 )
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
			else if ( TrackHeading.Count( cell.Link ) < 2 )
			{
				if ( isExit || isEntry )
				{
					repick = false;
				}
				else
				{
					var corner = IsCorner( cell.Mask );
					if ( dx == -2 )
						RemoveArm( at, TrackHeading.PosX );
					if ( dx == 2 )
						RemoveArm( at, TrackHeading.NegX );
					if ( dy == -2 )
						RemoveArm( at, TrackHeading.PosY );
					if ( dy == 2 )
						RemoveArm( at, TrackHeading.NegY );
					if ( corner && Tracks.Get( at.X, at.Y ) is var left && TrackHeading.Count( left.Mask ) == 1 )
						SetLink( at, Opposite( left.Mask ) );
				}
			}
			else
			{
				if ( dx == -2 )
					ClearLink( at, TrackHeading.PosX );
				if ( dx == 2 )
					ClearLink( at, TrackHeading.NegX );
				if ( dy == -2 )
					ClearLink( at, TrackHeading.PosY );
				if ( dy == 2 )
					ClearLink( at, TrackHeading.NegY );
				if ( Math.Abs( dx ) == 2 )
					RemoveArm( at, (byte)(TrackHeading.PosX | TrackHeading.NegX) );
				if ( Math.Abs( dy ) == 2 )
					RemoveArm( at, (byte)(TrackHeading.NegY | TrackHeading.PosY) );
			}
			if ( repick )
				Repick( at );
		}
		cell = Tracks.Get( at.X, at.Y );
		if ( cell.Overlap < 0 && (replay || !isExit && !isEntry) )
		{
			ClearOrigin( at, cell );
			return true;
		}
		if ( isExit && !replay )
			RestoreExit( at );
		return false;
	}

	/// <summary>
	/// The record clear of the demolition's second pass on the record under one mask cell (<c>0x53B280(0, 0, 1)</c>, called by <c>0x527EE0</c> for every cell of the demolished object's shape): an extension cell
	/// hands the clear to its origin with the same arguments, a tile that is no track record is left alone, and the origin goes through <see cref="ClearCell(ValueTuple{int, int}, int, int, bool)"/> as a forced clear (0, 0)
	/// with the flag 0x81B46C clear, which no replay sets there. A surviving tile (the exit and the entry tile of the tool, <see cref="ToolExit"/>) loses one count of its overlap word for each mask cell that reaches it.
	/// Returns whether a tile was cleared.
	/// </summary>
	// [BIN:TPW-EXE:0x0052852D record clear] pass 2 calls 0x53B280(0, 0, 1) (0x528525..0x52852D) on the record 0x4D0AF0 finds for the mask cell when the object is not the add-on a cleared tile takes with it (0x527EE0's third argument, 1 only from 0x53B703), else only the flag 0x10 of the record (0x528518..0x52851E); the third argument chooses the piece again for the neighbour tiles (0x53B7B4..0x53B83B), which ClearOrigin always does; 0x81B46C is 0 there: only the replays at 0x528059..0x5280BB set it, and the nested demolition runs the flag clear alone
	internal bool ClearUnderDemolition( (int X, int Y) at )
	{
		var cell = Tracks.Get( at.X, at.Y );
		if ( cell.IsEmpty )
			return false;
		var origin = cell.IsExtension && cell.Parent >= 0 ? Tracks.CellOf( cell.Parent ) : at;
		return ClearCell( origin, 0, 0 );
	}

	private void ClearLink( (int X, int Y) at, byte bits ) => Update( at, cell => { cell.Link &= (byte)~bits; return cell; } );

	/// <summary>The cell <paramref name="heading"/> two cells from <paramref name="at"/> as the original's <c>0x50C3E0(heading, 2)</c> answers it: any cell of the grid (the raw cell, an extension cell as it is), none outside.</summary>
	private (int X, int Y)? RawNeighbour( (int X, int Y) at, byte heading ) => Step( at, heading ) is var next && Tracks.InBounds( next.X, next.Y ) ? next : null;

	/// <summary>A cell the original's <c>0x53ADB0</c> calls a station tile: a station origin (type 0xD or 0x12) or an extension cell (0xC or 0x11) whose parent is one.</summary>
	private bool IsStationCell( TrackCell cell ) =>
		TrackCellTypes.IsStation( cell.Type ) || cell.IsExtension && Tracks.Get( Tracks.CellOf( cell.Parent ).X, Tracks.CellOf( cell.Parent ).Y ) is var parent && TrackCellTypes.IsStation( parent.Type );

	/// <summary>The neighbour's ride word (+0x10) equals the tile's: only an origin cell can match, an extension cell holds its parent's cell index there.</summary>
	// [EXT:track-tile-owner] the ride word (+0x10) of the original's tests is the tile's owner field here; the owner an extension cell carries (its origin's) is not what the original finds at its +0x10 (the parent's cell index, never a ride word), so only origin cells match
	private static bool SameRide( TrackCell tile, TrackCell neighbour ) => !neighbour.IsExtension && neighbour.Owner == tile.Owner;

	/// <summary>The tail of <c>0x53B280</c> for the exit tile that is still there: the arm toward a station tile beside it comes back with the flow link turned to it, a side whose neighbour is no track goes, and a lone arm sets the link.</summary>
	private void RestoreExit( (int X, int Y) at )
	{
		foreach ( var heading in TrackHeading.All )
		{
			if ( (Tracks.Get( at.X, at.Y ).Mask & heading) != 0 || RawNeighbour( at, heading ) is not { } next )
				continue;
			var beside = Tracks.Get( next.X, next.Y );
			if ( IsStationCell( beside ) && SameRide( Tracks.Get( at.X, at.Y ), beside ) )
			{
				SetLink( at, Opposite( heading ) );
				AddArm( at, heading );
				Repick( at );
			}
			// [BIN:TPW-EXE:0x0053B98C exit tail removal] a neighbour that is no track type (0x53AD30: not 0xB, 0xD, 0x10, 0x12 or 0x19) loses the side's arm, which is absent here already, and the piece is chosen again
			if ( !IsTrackType( beside ) )
			{
				RemoveArm( at, heading );
				Repick( at );
			}
		}
		if ( TrackHeading.Count( Tracks.Get( at.X, at.Y ).Mask ) != 1 )
			return;
		ClearLink( at, 0xFF );
		foreach ( var heading in TrackHeading.All )
		{
			if ( (Tracks.Get( at.X, at.Y ).Mask & heading) != 0 && RawNeighbour( at, heading ) is { } next
				&& Tracks.Get( next.X, next.Y ) is var beside && IsStationCell( beside ) && SameRide( Tracks.Get( at.X, at.Y ), beside ) )
				SetLink( at, Opposite( heading ) );
		}
	}

	// [BIN:TPW-EXE:0x0053B57F tile clear] the overlap word is 0 (0x53B582); a tile of type 0xB or 0x10 refunds (0x53B5C6..0x53B627: a station tile, type 0xD or 0x12, has no cell cost) unless the type is 0x19 or the credit is off (0x785A2C); then for each arm (0x53B634..0x53B6CD) the arm goes with the neighbour's arm back (0x522730, 0x50C3E0(heading, 2)) and a neighbour left with one arm whose flow link is not that arm's reverse takes it (0x522790, 0x522850, 0x4D8C00, 0x5227E0), the add-on object goes (0x53B6E5 → 0x527EE0), the link, flags, type and step word (0xFFFF) are written (0x53B70B..0x53B73E) and the three extension cells are cleared too (0x53B751..0x53B8BB)
	private void ClearOrigin( (int X, int Y) at, TrackCell cell )
	{
		if ( cell.Type is TrackCellTypes.KartOrigin or TrackCellTypes.WaterOrigin )
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
		if ( at == ToolEntry )
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
		// [BIN:TPW-EXE:0x0053BD8E relink tail] a tile two cells from the tool's exit (0x7639E0/E4) on an axis, with that cell on the grid (0x4D8300): every side whose neighbour is a station tile (type 0xD or 0x12, by type alone, no ride test) gives the exit cell that arm and the piece is chosen again; with the exit an anchor (the edit tool) the cell is no track tile, so its arm byte is never read and nothing is written (only a rotation 270 ride has a station tile two cells from its anchor)
		var gap = TrackStation.TileCells;
		var home = ToolExit;
		var adjacent = (at.X + gap == home.X && at.Y == home.Y) || (at.X == home.X && at.Y + gap == home.Y) || (at.X - gap == home.X && at.Y == home.Y) || (at.X == home.X && at.Y - gap == home.Y);
		if ( !adjacent || !Tracks.InBounds( home.X, home.Y ) )
			return;
		foreach ( var direction in TrackHeading.All )
		{
			if ( RawNeighbour( home, direction ) is { } partner && TrackCellTypes.IsStation( Tracks.Get( partner.X, partner.Y ).Type ) )
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
	/// The repair of the tool's exit tile after an edit (<c>0x52FC80</c>, <see cref="ToolExit"/>): an arm toward a station tile of the ride that the tile lacks comes back with the flow link
	/// turned to it, an arm toward a cell that holds no track, or a track whose mask does not point back, goes. With <paramref name="last"/> (Backspace's last line, the pop that leaves one
	/// point) a corner tile also drops every arm toward a cell that is no station tile together with that cell's arm back, and an arm toward a station tile sets the link to its reverse.
	/// Nothing happens unless the exit lies on the grid (<c>0x4D8300</c>) and holds a track type (<c>0x53AD30</c>), so after an edit tool's cut, whose exit is the anchor, it does nothing.
	/// </summary>
	// [BIN:TPW-EXE:0x0052FC80 station repair] for each of the four headings from 1 (0x4D8C20 turns by 90 degrees): without the arm, a station tile of the same ride (+0x10) two cells on gets it, with the flow link turned back (0x5227E0) and the piece chosen again (0x52FD73); with it, a cell that is no track tile and no station tile, or a track tile whose mask lacks the reversed heading, loses it (0x52FD6B)
	// [BIN:TPW-EXE:0x0052FD94 repair flag tail] with the flag set (the argument, from 0x52FF73) and the tile a corner (0x53AE00, tested once): each arm toward a cell on the grid that is no station tile (0x53ADB0) goes with the neighbour's arm back and both pieces are chosen again (0x52FDDD..0x52FDFF); an arm toward a station tile clears the link (0x5227F0(0xFF)) and sets it to the arm's reverse (0x5227E0, 0x52FE06..0x52FE1E)
	public void RepairStation( bool last = false )
	{
		var home = ToolExit;
		if ( !Tracks.InBounds( home.X, home.Y ) || !IsTrackType( Tracks.Get( home.X, home.Y ) ) )
			return;
		foreach ( var heading in TrackHeading.All )
		{
			var cell = Tracks.Get( home.X, home.Y );
			var neighbour = RawNeighbour( home, heading );
			if ( (cell.Mask & heading) == 0 )
			{
				if ( neighbour is { } other )
				{
					var theirs = Tracks.Get( other.X, other.Y );
					if ( IsStationCell( theirs ) && SameRide( cell, theirs ) )
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
				remove = IsTrackType( theirs ) ? (Opposite( heading ) & theirs.Mask) == 0 : !IsStationCell( theirs );
			}
			if ( remove )
			{
				RemoveArm( home, heading );
				Repick( home );
			}
		}
		if ( !last || !IsCorner( Tracks.Get( home.X, home.Y ).Mask ) )
			return;
		foreach ( var heading in TrackHeading.All )
		{
			if ( (Tracks.Get( home.X, home.Y ).Mask & heading) == 0 || RawNeighbour( home, heading ) is not { } next )
				continue;
			if ( !IsStationCell( Tracks.Get( next.X, next.Y ) ) )
			{
				RemoveArm( home, heading );
				Repick( home );
				RemoveArm( next, Opposite( heading ) );
				Repick( next );
			}
			else
			{
				ClearLink( home, 0xFF );
				SetLink( home, Opposite( heading ) );
			}
		}
	}
}

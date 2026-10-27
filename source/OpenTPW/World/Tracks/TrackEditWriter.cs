namespace OpenTPW;

/// <summary>
/// The track edit tool of a ride with a closed circuit (tool 0xE for karts, 0x13 for water, the ride window's track button): the first click picks a tile, the second picks another and
/// cuts the track between them (<see cref="TrackEditor.Cut"/>). It runs inside <see cref="CellBuildTool"/> as a writer whose clicks are not snapped to a line; the hover marks the tiles the
/// cut would take (the original's preview <c>0x52B400</c> with the last argument 0) and the level continues the tool as the line tool when the cut leaves the circuit open.
/// </summary>
public sealed class TrackEditWriter : ICellLineWriter
{
	private readonly TrackEditor editor;
	private readonly TrackFamily family;
	private readonly Action<(int X, int Y)> cutDone;
	private readonly Action refused;
	private readonly Func<(int X, int Y), (int X, int Y), TrackCutSent> cut;
	private (int X, int Y)? first;

	/// <param name="editor">The editor of the ride's track.</param>
	/// <param name="family">Kart or water: the tool mode.</param>
	/// <param name="cutDone">Called after a committed cut with the tile of the second click (the original's new line start, <c>0x81EDE4/E8</c>).</param>
	/// <param name="refused">Called when the second click is no tile the cut can end on: the tool ends (<c>0x52F200</c>).</param>
	/// <param name="cut">Makes the cut between the two picked tiles and answers it, or null when it is refused (the tool then ends); null cuts through <paramref name="editor"/> at once
	/// (<see cref="Cut(TrackEditor, ValueTuple{int, int}, ValueTuple{int, int})"/>). [EXT:online-coop-commands] The level's tool hands it to its commands (<see cref="CutTrackCommand"/>).</param>
	public TrackEditWriter( TrackEditor editor, TrackFamily family, Action<(int X, int Y)> cutDone, Action refused, Func<(int X, int Y), (int X, int Y), TrackCutResult?>? cut = null )
		: this( editor, family, cutDone, refused, cut == null ? ( a, b ) => new TrackCutSent( Cut( editor, a, b ) ) : ( a, b ) => new TrackCutSent( cut( a, b ) ) )
	{
	}

	/// <param name="send">[EXT:online-coop-commands] Sends the cut between the two picked tiles: done at once, or a guest's cut that waits for the host (C3c-1b), whose outcome finishes it.</param>
	internal TrackEditWriter( TrackEditor editor, TrackFamily family, Action<(int X, int Y)> cutDone, Action refused, Func<(int X, int Y), (int X, int Y), TrackCutSent> send )
	{
		this.editor = editor ?? throw new ArgumentNullException( nameof( editor ) );
		this.family = family;
		this.cutDone = cutDone ?? throw new ArgumentNullException( nameof( cutDone ) );
		this.refused = refused ?? throw new ArgumentNullException( nameof( refused ) );
		cut = send ?? throw new ArgumentNullException( nameof( send ) );
	}

	public CellToolMode Mode => family == TrackFamily.Water ? CellToolMode.WaterEdit : CellToolMode.KartEdit;
	public bool SnapsToLine => false;
	/// <summary>The tile the first click picked, or null before it.</summary>
	public (int X, int Y)? FirstPick => first;

	/// <summary>
	/// The first click: a tile the test accepts is picked, a station tile or a cell without track changes nothing (a refused tile never gets here: its hover set the refused-ghost latch, <see cref="RefusesFirstClick"/>).
	/// </summary>
	// [BIN:TPW-EXE:0x0052692E first pick] the pick's step number goes to 0x7639B0 and the cell to 0x818694, then 0x539760(0xE) answering 1 clears both again (0x526935, 0x52692E)
	public bool CanStart( (int X, int Y) cell )
	{
		var (result, tile) = editor.Pick( cell.X, cell.Y );
		if ( result != TrackEditPick.Accepted || tile is not { } picked )
			return false;
		first = picked;
		return true;
	}

	public bool RefusesFirstClick( (int X, int Y) cell ) => first == null && editor.Pick( cell.X, cell.Y ).Result == TrackEditPick.Rejected;

	/// <summary>The marks of a hover: the picked tile alone before the first click, afterwards the tiles the cut between the picked tile and the one under the pointer would clear.</summary>
	// [BIN:TPW-EXE:0x00527960 edit hover] before the first pick the hovered tile is drawn through the ghost op of the tool mode with the chooser's answer (9, the m_break symbol over the tile, or 1, the red face); after it the second pick is reset to the first unless the chooser answers 9 for it and the cut's walk 0x52B400(.., 0) draws the lines between its corner points with the op 0x10F, each tile of the ride answering 0xD (m_erase)
	public SegmentResult Preview( (int X, int Y) start, (int X, int Y) cursor )
	{
		var (result, tile) = editor.Pick( cursor.X, cursor.Y );
		if ( first is not { } picked )
		{
			var line = tile is { } hovered ? TrackStation.TileCellsOf( hovered.X, hovered.Y ).ToArray() : new[] { cursor };
			var accepted = result == TrackEditPick.Accepted && tile is { };
			return Ghost( cursor, line, accepted ? null : tile ?? cursor, accepted ? CellBuildResult.Ok : CellBuildResult.Occupied, accepted ? new[] { (tile!.Value, BlueprintMarker.Break) } : null );
		}
		if ( result != TrackEditPick.Accepted || tile is not { } second )
		{
			// a second tile the test refuses draws as the first alone, the hovered tile red (0x527960 resets the second pick to the first)
			var line = TrackStation.TileCellsOf( picked.X, picked.Y ).Concat( tile is { } other ? TrackStation.TileCellsOf( other.X, other.Y ) : new[] { cursor } ).ToArray();
			return Ghost( cursor, line, tile ?? cursor, CellBuildResult.Occupied, SymbolsOf( new[] { picked } ) );
		}
		var (low, high) = Ordered( picked, second );
		var path = editor.Cut( low, high, commit: false );
		var cells = path.Path.SelectMany( step => TrackStation.TileCellsOf( step.X, step.Y ) ).ToArray();
		return Ghost( cursor, cells, path.Failed ? second : null, path.Failed ? CellBuildResult.Occupied : CellBuildResult.Ok, path.Failed ? null : SymbolsOf( path.Path ) );
	}

	// [APPROX:TRACK-018] the hover marks the pick (m_break) and the cut path (m_erase) as 0x10F of 0x52B400 does, but not the pick hover's glide, walls and neighbour raise or the ghost's blink (BLUEPRINT-003); whether 0x52FC80 repairs the exit rather than the first station tile is not traced
	/// <summary>The cut's tiles as symbol faces: m_erase on each tile of the ride (<see cref="TrackEditor.GhostSymbol"/>).</summary>
	private ((int X, int Y) Origin, BlueprintMarker Symbol)[] SymbolsOf( IEnumerable<(int X, int Y)> tiles ) =>
		tiles.Select( tile => (Tile: tile, Symbol: editor.GhostSymbol( tile, cut: true )) ).Where( item => item.Symbol is not null ).Select( item => (item.Tile, item.Symbol!.Value) ).ToArray();

	/// <summary>The second click: the cut between the two picked tiles, or the end of the tool when the clicked tile is refused.</summary>
	// [BIN:TPW-EXE:0x00526ADA second pick] a second click whose test does not answer 9 leaves the tool (0x52F200(0, 0) at 0x526ADC); otherwise the pair, the lower step first (0x526AF2), goes to 0x52B400 with the last argument 1 (0x526B5E) and the station repair 0x52FC80 follows (0x526B67)
	public SegmentResult Commit( (int X, int Y) start, (int X, int Y) end )
	{
		var picked = first;
		first = null;
		var (result, tile) = editor.Pick( end.X, end.Y );
		if ( picked is not { } low || result != TrackEditPick.Accepted || tile is not { } second )
			return Finish( start, end, null, null );
		var sent = cut( low, second );
		// [EXT:online-coop-commands] a guest's cut went to the host (C3c-1b): the first pick stays while the tool waits, so its ghost is the cut's, and the outcome finishes the cut as this
		// click would have; a request the host could not take leaves the first pick for the next click
		if ( sent.Waiting is { } waiting )
		{
			first = low;
			return new SegmentResult( end, Array.Empty<(int X, int Y)>(), 0, CellBuildResult.Ok, false )
			{
				Start = start,
				Line = Preview( start, end ).Line,
				Waiting = new WaitingLineCommit( waiting.Request, outcome =>
				{
					first = null;
					return Finish( start, end, second, waiting.Complete( outcome ) );
				} )
			};
		}
		return Finish( start, end, second, sent.Cut );
	}

	/// <summary>The second click's end: the cut <paramref name="done"/> between the picks (the tool then goes on from <paramref name="second"/>), or, without one, the end of the tool.</summary>
	private SegmentResult Finish( (int X, int Y) start, (int X, int Y) end, (int X, int Y)? second, TrackCutResult? done )
	{
		if ( second is not { } tile || done == null )
		{
			refused();
			return Ghost( end, Array.Empty<(int X, int Y)>(), null, CellBuildResult.Ok );
		}
		cutDone( tile );
		return new SegmentResult( end, done.Cleared, 0, CellBuildResult.Ok, false )
		{
			Start = start,
			Line = done.Path.SelectMany( step => TrackStation.TileCellsOf( step.X, step.Y ) ).ToArray()
		};
	}

	/// <summary>
	/// The cut between two tiles the edit tool's test accepts (<see cref="TrackEditor.Pick"/>), the lower step first (<c>0x526AF2</c>), and the station repair after it (<c>0x52FC80</c>); null, and nothing
	/// changed, when the test refuses either tile. The tool's commit and the level's <see cref="CutTrackCommand"/> both cut this way.
	/// </summary>
	internal static TrackCutResult? Cut( TrackEditor editor, (int X, int Y) first, (int X, int Y) second )
	{
		if ( editor.Pick( first.X, first.Y ) is not (TrackEditPick.Accepted, { } a) || editor.Pick( second.X, second.Y ) is not (TrackEditPick.Accepted, { } b) )
			return null;
		var (low, high) = editor.StepOf( b ) < editor.StepOf( a ) ? (b, a) : (a, b);
		var done = editor.Cut( low, high, commit: true );
		editor.RepairStation();
		return done;
	}

	public void Undo( SegmentResult segment )
	{
	}

	/// <summary>The two tiles with the lower step number first.</summary>
	private (int X, int Y) FirstOf( (int X, int Y) a, (int X, int Y) b ) => editor.StepOf( b ) < editor.StepOf( a ) ? b : a;

	private ((int X, int Y) Low, (int X, int Y) High) Ordered( (int X, int Y) a, (int X, int Y) b ) => FirstOf( a, b ) == a ? (a, b) : (b, a);

	private static SegmentResult Ghost( (int X, int Y) end, IReadOnlyList<(int X, int Y)> line, (int X, int Y)? stoppedAt, CellBuildResult stoppedBy,
		IReadOnlyList<((int X, int Y) Origin, BlueprintMarker Symbol)>? symbols = null ) =>
		new( end, Array.Empty<(int X, int Y)>(), 0, stoppedBy, false ) { Start = end, Line = line, StoppedAt = stoppedAt, Symbols = symbols ?? Array.Empty<((int X, int Y) Origin, BlueprintMarker Symbol)>() };
}

/// <summary>
/// [EXT:online-coop-commands] What the level made of the edit tool's cut (<see cref="TrackEditWriter"/>): the cut at once (<see cref="Cut"/>, null when it was refused, which ends the tool), or a
/// guest's cut that went to the host (<see cref="Waiting"/>, C3c-1b).
/// </summary>
internal readonly record struct TrackCutSent( TrackCutResult? Cut, WaitingTrackCut? Waiting = null );

/// <summary>[EXT:online-coop-commands] A guest's cut that waits for the host (C3c-1b): its request, and what finishes the edit from the request's outcome once and gives the cut, null when refused.</summary>
internal sealed record WaitingTrackCut( CoopGuestCommands.Request Request, Func<ParkCommandResult, TrackCutResult?> Complete );

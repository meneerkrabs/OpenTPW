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
	private readonly Func<(int X, int Y), (int X, int Y), TrackCutResult?> cut;
	private (int X, int Y)? first;

	/// <param name="editor">The editor of the ride's track.</param>
	/// <param name="family">Kart or water: the tool mode.</param>
	/// <param name="cutDone">Called after a committed cut with the tile of the second click (the original's new line start, <c>0x81EDE4/E8</c>).</param>
	/// <param name="refused">Called when the second click is no tile the cut can end on: the tool ends (<c>0x52F200</c>).</param>
	/// <param name="cut">Makes the cut between the two picked tiles and answers it, or null when it is refused (the tool then ends); null cuts through <paramref name="editor"/> at once
	/// (<see cref="Cut(TrackEditor, ValueTuple{int, int}, ValueTuple{int, int})"/>). [EXT:online-coop-commands] The level's tool hands it to its commands (<see cref="CutTrackCommand"/>).</param>
	public TrackEditWriter( TrackEditor editor, TrackFamily family, Action<(int X, int Y)> cutDone, Action refused, Func<(int X, int Y), (int X, int Y), TrackCutResult?>? cut = null )
	{
		this.editor = editor ?? throw new ArgumentNullException( nameof( editor ) );
		this.family = family;
		this.cutDone = cutDone ?? throw new ArgumentNullException( nameof( cutDone ) );
		this.refused = refused ?? throw new ArgumentNullException( nameof( refused ) );
		this.cut = cut ?? (( a, b ) => Cut( editor, a, b ));
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
	public SegmentResult Preview( (int X, int Y) start, (int X, int Y) cursor )
	{
		var (result, tile) = editor.Pick( cursor.X, cursor.Y );
		if ( first is not { } picked )
		{
			var line = tile is { } hovered ? TrackStation.TileCellsOf( hovered.X, hovered.Y ).ToArray() : new[] { cursor };
			return Ghost( cursor, line, result == TrackEditPick.Accepted ? null : tile ?? cursor, result == TrackEditPick.Accepted ? CellBuildResult.Ok : CellBuildResult.Occupied );
		}
		if ( result != TrackEditPick.Accepted || tile is not { } second )
		{
			// a second tile the test refuses draws as the first alone, the hovered tile red (0x527960 resets the second pick to the first)
			var line = TrackStation.TileCellsOf( picked.X, picked.Y ).Concat( tile is { } other ? TrackStation.TileCellsOf( other.X, other.Y ) : new[] { cursor } ).ToArray();
			return Ghost( cursor, line, tile ?? cursor, CellBuildResult.Occupied );
		}
		var (low, high) = Ordered( picked, second );
		var path = editor.Cut( low, high, commit: false );
		var cells = path.Path.SelectMany( step => TrackStation.TileCellsOf( step.X, step.Y ) ).ToArray();
		return Ghost( cursor, cells, path.Failed ? second : null, path.Failed ? CellBuildResult.Occupied : CellBuildResult.Ok );
	}

	/// <summary>The second click: the cut between the two picked tiles, or the end of the tool when the clicked tile is refused.</summary>
	// [BIN:TPW-EXE:0x00526ADA second pick] a second click whose test does not answer 9 leaves the tool (0x52F200(0, 0) at 0x526ADC); otherwise the pair, the lower step first (0x526AF2), goes to 0x52B400 with the last argument 1 (0x526B5E) and the station repair 0x52FC80 follows (0x526B67)
	public SegmentResult Commit( (int X, int Y) start, (int X, int Y) end )
	{
		var picked = first;
		first = null;
		var (result, tile) = editor.Pick( end.X, end.Y );
		if ( picked is not { } low || result != TrackEditPick.Accepted || tile is not { } second || cut( low, second ) is not { } done )
		{
			refused();
			return Ghost( end, Array.Empty<(int X, int Y)>(), null, CellBuildResult.Ok );
		}
		cutDone( second );
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

	private static SegmentResult Ghost( (int X, int Y) end, IReadOnlyList<(int X, int Y)> line, (int X, int Y)? stoppedAt, CellBuildResult stoppedBy ) =>
		new( end, Array.Empty<(int X, int Y)>(), 0, stoppedBy, false ) { Start = end, Line = line, StoppedAt = stoppedAt };
}

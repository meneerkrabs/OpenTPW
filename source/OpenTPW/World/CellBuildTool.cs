namespace OpenTPW;

/// <summary>The build tool mode (global <c>data:0x84b04</c>): the mode is the cell type the drag writes.</summary>
public enum CellToolMode : byte
{
	None = 0,
	Path = 1,
	Queue = 3,
	/// <summary>The kart track tool: the mode is the origin cell type the line writes (docs/TRACKS.md).</summary>
	KartTrack = 11,
	/// <summary>The kart track edit tool (tool 0xE, the ride window's track button for a kart ride): two clicks cut a closed circuit (<see cref="TrackEditWriter"/>).</summary>
	KartEdit = 14,
	/// <summary>The water track tool (origin cell type 16).</summary>
	WaterTrack = 16,
	/// <summary>The water track edit tool (tool 0x13, the track button for a water ride).</summary>
	WaterEdit = 19,
	/// <summary>[EXT:pools-tool] The pool paint tool of the <c>opentpw.pools</c> extension: the mode is the cell type it writes, as for the other tools (docs/POOLS.md).</summary>
	Pool = ParkCellTypes.PoolWater
}

/// <summary>Lays snapped straight lines of one cell type for <see cref="CellBuildTool"/>.</summary>
public interface ICellLineWriter
{
	CellToolMode Mode { get; }
	/// <summary>The ghost of the snapped line from <paramref name="start"/> toward <paramref name="cursor"/> (no writes).</summary>
	SegmentResult Preview( (int X, int Y) start, (int X, int Y) cursor );
	/// <summary>Writes the snapped line from <paramref name="start"/> toward <paramref name="end"/>, charging per cell.</summary>
	SegmentResult Commit( (int X, int Y) start, (int X, int Y) end );
	/// <summary>Takes back a committed segment.</summary>
	void Undo( SegmentResult segment );
	/// <summary>The start check of a click without a start (tp.exe <c>0x535670(mode, 0)</c>): false when it refuses the cell; a writer without one accepts every cell.</summary>
	bool CanStart( (int X, int Y) cell ) => true;
	/// <summary>Whether a click is snapped to the line's axis (the line tools); the track edit tools take the clicked cell as it is.</summary>
	bool SnapsToLine => true;
	/// <summary>Discard issuer-local active-segment state when a tool is closed or entered anew.</summary>
	void ResetToolSession() { }
}

/// <summary>
/// The path and queue build tool (PATH-plan §4.3, §9.4), CPU-only and shared by both modes: a start cell, the
/// 1024-entry vertex stack, a ghost preview on cursor moves (the <c>mode | 0x100</c> LayLine) and a commit on
/// clicks (the raw-mode LayLine). A click whose ghost has a refused cell (laying nothing), whose snapped end is the
/// start (laying nothing once a line exists), or whose line ends on an existing path or queue ends the tool;
/// otherwise the end becomes the next start. The HUD and tests drive it.
/// </summary>
public sealed class CellBuildTool
{
	/// <summary>Capacity of the vertex stack (<c>0x7bcf4</c>, count <c>data:0x84ab8</c>).</summary>
	// [BIN:STP-PPC:0x1007BCF4 vertex push] the path/queue vertex stack holds up to 1024 points
	public const int VertexCapacity = 1024;

	private readonly List<(int X, int Y)> vertices = new();
	private readonly List<SegmentResult> segments = new();

	public CellToolMode Mode { get; private set; }
	/// <summary>The writer of the current mode, or null when the tool is off.</summary>
	public ICellLineWriter? Writer { get; private set; }
	/// <summary>The start of the next line (the original's start, −1 when unset).</summary>
	public (int X, int Y)? Start { get; private set; }
	public IReadOnlyList<(int X, int Y)> Vertices => vertices;
	/// <summary>The current ghost (last <see cref="Hover"/>), cleared by a commit.</summary>
	public SegmentResult? Ghost { get; private set; }
	/// <summary>The last committed segment.</summary>
	public SegmentResult? LastCommit { get; private set; }
	public bool IsActive => Mode != CellToolMode.None;
	/// <summary>Committed segments still on the undo stack.</summary>
	public int SegmentCount => segments.Count;
	/// <summary>Raised when the tool turns off (end of line, refusal, cancel).</summary>
	public event Action? Ended;

	/// <summary>Starts <paramref name="writer"/>'s mode with <paramref name="start"/> as the first vertex, or without a start (null): the next click sets it.</summary>
	// [BIN:STP-PPC:0x1007B320 SetMode] tool mode 1 = path, 3 = queue; the drag writes the cell type equal to the mode
	// [BIN:TPW-EXE:0x0052F200 SetMode] a new mode stores the mode (0x81AE2C) and sets the start to −1 (0x52F340)
	public void Enter( ICellLineWriter writer, (int X, int Y)? start ) => Enter( writer, start, null, null );

	/// <summary>
	/// <see cref="Enter(ICellLineWriter, ValueTuple{int, int}?)"/> with the stack of corner points and the segments between them already filled, as the
	/// queue tool's entry fills them from the ride's queue (<see cref="QueuePaths.CornerStack"/>, tp.exe <c>0x530120</c>): <see cref="Undo"/> then takes the
	/// existing queue back one segment at a time. <paramref name="segmentsBetween"/> has one segment per point after the first.
	/// </summary>
	// [BIN:TPW-EXE:0x0052FE50 Back] the undo pops the stack's last point and clears the line from it to the new last point; a stack of one point has nothing to undo (0x52FE6F..0x52FE8C)
	public void Enter( ICellLineWriter writer, (int X, int Y)? start, IReadOnlyList<(int X, int Y)>? stack, IReadOnlyList<SegmentResult>? segmentsBetween )
	{
		ArgumentNullException.ThrowIfNull( writer );
		if ( writer.Mode == CellToolMode.None )
			throw new ArgumentException( "A writer lays path or queue cells.", nameof( writer ) );
		if ( stack != null && (segmentsBetween == null || stack.Count == 0 || segmentsBetween.Count != stack.Count - 1) )
			throw new ArgumentException( "A stack of corner points has one segment between each two points.", nameof( segmentsBetween ) );
		vertices.Clear();
		segments.Clear();
		Writer?.ResetToolSession();
		writer.ResetToolSession();
		Writer = writer;
		Mode = writer.Mode;
		Start = start;
		if ( stack != null )
		{
			vertices.AddRange( stack.Take( VertexCapacity ) );
			segments.AddRange( segmentsBetween!.Take( Math.Max( 0, vertices.Count - 1 ) ) );
		}
		else if ( start is { } first )
			vertices.Add( first );
		Ghost = null;
		LastCommit = null;
	}

	/// <summary>Cursor move: the snapped ghost line from the start, or a single cell ghost with no start.</summary>
	// [BIN:STP-PPC:0x1006F380 path preview] in mode 1 or 3, snap the cursor and LayLine(mode | 0x100, start, end); without a start, preview one cell
	public SegmentResult? Hover( (int X, int Y) cursor )
	{
		if ( Writer == null )
			return Ghost = null;
		return Ghost = Writer.Preview( Start ?? cursor, cursor );
	}

	/// <summary>
	/// Click: snap; in the path and queue tools a line whose ghost has a refused cell ends the tool and lays nothing
	/// (the refused ghost is returned); a click on the start with more than one vertex stored ends the tool and lays
	/// nothing (null); otherwise push the end, commit the line, then continue from the end or end the tool. Returns
	/// the committed segment, the refused ghost, or null when off or when the click only ended the tool.
	/// </summary>
	// [BIN:STP-PPC:0x10070F84 path commit] push and LayLine(mode, start, end) unless the end equals the start with more than one vertex stored (0x7107c..0x71084 bf eq to 0x71174: then nothing is laid); end the tool (start −1, SetMode(0)) when end == start
	public SegmentResult? Click( (int X, int Y) cell )
	{
		if ( Writer == null )
			return null;
		if ( Start is not { } start )
			return ClickWithoutStart( cell );
		var end = Writer.SnapsToLine ? ParkPathBuilder.SnapEnd( start, cell ) : cell;
		// [BIN:TPW-EXE:0x524A63 refused ghost] a click while the ghost of the line has a refused cell (0x816D48, set at 0x533933 when the preview's validator 0x535670 refuses a cell) ends the path or queue tool and lays nothing (mode table 0x52766C: SetMode(0, 0) at 0x5266A3)
		// A ghost stopped only by the back-of-queue walk's bound is not a refusal of the original's validator: the click commits up to the cap
		if ( Mode is CellToolMode.Path or CellToolMode.Queue && Writer.Preview( start, end ) is { Completed: false, StoppedByOwnLimit: false } refused )
		{
			End();
			return refused with { GhostRefused = true };
		}
		if ( end == start && vertices.Count != 1 )
		{
			End();
			return null;
		}
		// The original ignores the push's result (0x7bcf4, whose guard is count > 1024, one past the buffer: REVIEW-PATH N2); a full stack ending the tool is an OpenTPW guard.
		var pushed = false;
		if ( vertices.Count < VertexCapacity )
		{
			vertices.Add( end );
			pushed = true;
		}
		var writer = Writer;
		var result = writer.Commit( start, end );
		// A commit that finished the work ended, replaced or restarted the tool without a start.
		// Even a same-writer restart owns fresh stacks; do not restore this old commit into them.
		if ( !ReferenceEquals( Writer, writer ) || Start == null )
			return result;
		// A writer that steps in tiles (the track tools) snaps the end further; the tool continues from where the line really ended.
		if ( result.SnappedEnd != end )
		{
			end = result.SnappedEnd;
			if ( pushed )
				vertices[^1] = end;
		}
		Ghost = null;
		LastCommit = result;
		if ( pushed )
			segments.Add( result );
		if ( Mode is CellToolMode.Path or CellToolMode.Queue )
		{
			// [BIN:TPW-EXE:0x527517 end flag] after the commit LayLine (0x527429) the pass 0x81 returns 0 only when the commit reached the line's last cell and that cell was a path or queue cell before the write (SetCell's write branch sets 0x820ABC at 0x5345AD, case 0x81 reads it at 0x5340C1); then the tool ends (0x5275B1), as when the end is the start; otherwise the snapped end is the next start (0x527650), even after a refused commit
			var reachedLast = result.Completed || (result.Line.Count > 0 && result.StoppedAt == result.Line[^1]);
			if ( end == start || (result.QueueFinalEndsTool ?? (result.EndedOnExisting && reachedLast)) || !pushed )
				End();
			else
				Start = end;
			return result;
		}
		if ( end == start || !result.Completed || result.EndedOnExisting || !pushed )
			End();
		else
			Start = end;
		return result;
	}

	/// <summary>
	/// A click while the tool has no start (the click that entered it, or the kart or water line tool's click after an exactly-Ctrl close): a refused single-cell
	/// or single-tile ghost ends the tool, as after a start; otherwise the cell becomes the start when the writer's start check accepts it, else the tool stays on
	/// without one. Returns the refused ghost when it ended the tool, else null.
	/// </summary>
	// [BIN:TPW-EXE:0x00524A63 refused ghost] the click handler 0x524960 tests the latch 0x816D48 before the mode dispatch whether or not a start is set: a single-cell ghost the preview 0x5234D0 refused (LayLine(mode | 0x100) from the cursor) ends the path or queue tool (0x5266A3: SetMode(0, 0)); while game+36 (0x785A2C) is set the click first previews its own cell again (0x5249B6), which this re-preview always does
	// [BIN:TPW-EXE:0x0052453D line tool ghost] without a start the kart and water line modes preview the tile under the cursor alone (0x53AAC0 with mode | 0x100 from the cursor to itself), whose ghost routine 0x5370E0 sets the latch for a refused tile (0x537A1B → 0x527940(1)); the mode table 0x52766C sends 0xB and 0x10 to the same 0x5266A3, so such a click ends the tool and never reaches the start's store and its help 0xCC (0x526CB3)
	// [BIN:TPW-EXE:0x00527222 first click] with the start at −1 (0x81EDE4/0x81EDE8) the path mode runs the start check 0x535670(1, 0) and the queue mode 0x535670(3, 0) on the clicked cell: a refusal (1) leaves the tool on without a start (0x52765B), anything else (0, 8, 0xB) makes the cell the start (0x5272B3) and the first vertex (0x52FBD0, 0x52FBF0)
	private SegmentResult? ClickWithoutStart( (int X, int Y) cell )
	{
		if ( Mode is CellToolMode.Path or CellToolMode.Queue or CellToolMode.KartTrack or CellToolMode.WaterTrack && Writer!.Preview( cell, cell ) is { Completed: false, StoppedByOwnLimit: false } refused )
		{
			End();
			return refused with { GhostRefused = true };
		}
		if ( !Writer!.CanStart( cell ) )
			return null;
		Start = cell;
		vertices.Add( cell );
		Ghost = null;
		return null;
	}

	/// <summary>Backspace: takes back the last committed segment and continues from its start.</summary>
	public bool Undo()
	{
		if ( Writer == null || segments.Count == 0 )
			return false;
		var segment = segments[^1];
		segments.RemoveAt( segments.Count - 1 );
		vertices.RemoveAt( vertices.Count - 1 );
		Writer.Undo( segment );
		Start = vertices.Count > 0 ? vertices[^1] : segment.Start;
		Ghost = null;
		return true;
	}

	/// <summary>
	/// The park-view help line (UIHELPTEXT) for the cell under the cursor: inside the tool 443 (path) or 445
	/// (queue); outside it 441 over an empty cell a path may go on, 442 over a path cell, 444 over a queue cell,
	/// else none. <paramref name="paths"/> null (no builder) gives none outside the tool.
	/// </summary>
	// [BIN:STP-PPC:0x10139C64 park-view hover] the cell under the cursor picks the help line: empty (0x85258) 441, type 1 442, type 3 444; inside the tools 443 and 445
	public int? HoverHelpId( ParkPathBuilder? paths, int x, int y )
	{
		if ( Mode == CellToolMode.Path )
			return 443;
		if ( Mode == CellToolMode.Queue )
			return 445;
		if ( paths == null || !paths.Cells.InBounds( x, y ) )
			return null;
		return paths.Cells.TypeAt( x, y ) switch
		{
			ParkCellType.Path => 442,
			ParkCellType.Queue => 444,
			ParkCellType.Empty when paths.CheckCell( x, y ) is CellBuildResult.Ok or CellBuildResult.NotEnoughMoney => 441,
			_ => null
		};
	}

	/// <summary>Right click, Escape or Back: ends the tool without writing.</summary>
	public void Cancel() => End();

	private void End()
	{
		var wasActive = IsActive;
		Mode = CellToolMode.None;
		Writer?.ResetToolSession();
		Writer = null;
		Start = null;
		Ghost = null;
		if ( LastCommit?.QueueReceipt != null ) LastCommit = LastCommit with { QueueReceipt = null };
		vertices.Clear();
		segments.Clear();
		if ( wasActive )
			Ended?.Invoke();
	}
}

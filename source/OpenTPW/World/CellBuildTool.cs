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
	/// <summary>Whether the hover ghost of a click without a start sets the refused-ghost latch for <paramref name="cell"/> beyond the line modes' own test (the track edit tools: a refused first tile), so the release plays the refusal cue and ends the tool.</summary>
	bool RefusesFirstClick( (int X, int Y) cell ) => false;
	/// <summary>Whether a click is snapped to the line's axis (the line tools); the track edit tools take the clicked cell as it is.</summary>
	bool SnapsToLine => true;
	/// <summary>Discard issuer-local active-segment state when a tool is closed or entered anew.</summary>
	void ResetToolSession() { }
	/// <summary>
	/// How many points the tool's vertex stack holds: the path and queue tools' 1024 (<see cref="CellBuildTool.VertexCapacity"/>), the kart and water tools' 1025 (the PC pusher <c>0x52FBF0</c> stops at
	/// a count above 0x400, so the stack holds 0x401 points, <see cref="TrackEditor.CornerCapacity"/>).
	/// </summary>
	int VertexCapacity => CellBuildTool.VertexCapacity;
	/// <summary>Whether the tool's Backspace may take a committed segment back ([EXT:online-coop-commands] not a guest's path or queue line, C3a-2).</summary>
	bool CanUndo => true;
}

/// <summary>
/// [EXT:online-coop-commands] A line writer whose undo a guest sends to the host first (the track tools under their ride's claim, C3c-1b): the tool pops nothing until the outcome comes back
/// (<see cref="CellBuildTool.WaitingUndo"/>).
/// </summary>
internal interface IGuestUndoWriter
{
	/// <summary>Whether this writer's undo goes to the host as a request now (a guest's).</summary>
	bool UndoesByRequest { get; }
	/// <summary>Sends the undo of <paramref name="segment"/>: its request, or null when it was refused before it went (the session says why).</summary>
	CoopGuestCommands.Request? SendUndo( SegmentResult segment );
	/// <summary>
	/// The undo's <paramref name="outcome"/> (its record's result in the guest's park): when it took the line back, <paramref name="pop"/> takes the segment off the tool's stacks, as the host's
	/// undo did, and the tool moves on as the walk once moved it. True when it took the line back.
	/// </summary>
	bool CompleteUndo( ParkCommandResult outcome, Action pop );
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

	/// <summary>
	/// [EXT:online-coop-commands] A guest's line that waits for the host (C3a-2): its command's request, and the start and the snapped end of the click. While it waits the tool takes
	/// no click and no undo, and its ghost stays the clicked line's; <see cref="CompleteWaiting"/> runs the click's continuation once with the line's segment. Whatever ends or enters the tool
	/// drops it, and the request's outcome is then only a message.
	/// </summary>
	internal sealed record WaitingLine( WaitingLineCommit Commit, (int X, int Y) Start, (int X, int Y) End );

	/// <summary>[EXT:online-coop-commands] The guest's line that waits for the host, or null (<see cref="WaitingLine"/>).</summary>
	internal WaitingLine? Waiting { get; private set; }

	/// <summary>
	/// [EXT:online-coop-commands] A guest's Backspace that waits for the host (C3c-1b, <see cref="IGuestUndoWriter"/>): its request and the segment it takes back. Nothing is popped until its outcome
	/// (<see cref="CompleteUndo"/>); while it waits the tool takes no click and no other undo, and has no ghost. Whatever ends or enters the tool drops it, and its outcome is then only a message.
	/// </summary>
	internal sealed record WaitingUndoLine( CoopGuestCommands.Request Request, SegmentResult Segment );

	/// <summary>[EXT:online-coop-commands] The guest's Backspace that waits for the host, or null (<see cref="WaitingUndoLine"/>).</summary>
	internal WaitingUndoLine? WaitingUndo { get; private set; }

	/// <summary>[EXT:online-coop-commands] Whether a guest's line or Backspace of this tool waits for the host: the tool takes no click and no undo meanwhile.</summary>
	internal bool IsWaiting => Waiting != null || WaitingUndo != null;

	/// <summary>
	/// The tool's generation: each entry and each end starts a new one. A click whose commit, or an outcome whose continuation, ended, replaced or restarted the tool stops there, also when the
	/// same writer was entered again.
	/// </summary>
	private int generation;

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
		Waiting = null;
		WaitingUndo = null;
		generation++;
		Writer?.ResetToolSession();
		writer.ResetToolSession();
		Writer = writer;
		Mode = writer.Mode;
		Start = start;
		if ( stack != null )
		{
			vertices.AddRange( stack.Take( writer.VertexCapacity ) );
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
		// [EXT:online-coop-commands] a guest's line that waits for the host keeps its ghost: the clicked line, frozen until its outcome; a Backspace that waits has none (C3c-1b)
		if ( IsWaiting )
			return Ghost;
		return Ghost = Writer.Preview( Start ?? cursor, cursor );
	}

	/// <summary>
	/// Click: snap; in the path, queue, kart and water line tools a line whose ghost has a refused cell or tile ends the tool and lays nothing
	/// (the refused ghost is returned); a click on the start with more than one vertex stored ends the tool and lays
	/// nothing (null); otherwise push the end, commit the line, then continue from the end or end the tool. Returns
	/// the committed segment, the refused ghost, or null when off or when the click only ended the tool.
	/// </summary>
	// [BIN:STP-PPC:0x10070F84 path commit] push and LayLine(mode, start, end) unless the end equals the start with more than one vertex stored (0x7107c..0x71084 bf eq to 0x71174: then nothing is laid); end the tool (start −1, SetMode(0)) when end == start
	public SegmentResult? Click( (int X, int Y) cell )
	{
		// [EXT:online-coop-commands] a guest's line or Backspace that waits for the host takes no other click (C3a-2, C3c-1b)
		if ( Writer == null || IsWaiting )
			return null;
		if ( Start is not { } start )
			return ClickWithoutStart( cell );
		var end = Writer.SnapsToLine ? ParkPathBuilder.SnapEnd( start, cell ) : cell;
		// [BIN:TPW-EXE:0x524A63 refused ghost] a click while the ghost of the line has a refused cell (0x816D48, set at 0x533933 when the preview's validator 0x535670 refuses a cell) ends the path or queue tool and lays nothing (mode table 0x52766C: SetMode(0, 0) at 0x5266A3)
		// [BIN:TPW-EXE:0x005244B8 line tool ghost] the kart and water line modes preview the line from the start (0x53AAC0 with mode | 0x100, 0x52452E), which walks it tile by tile and stops at the first tile the ghost routine 0x5370E0 refuses (its chooser 0x539760 returns 1, the money test at 0x539F95 included), setting the same latch (0x537A1B → 0x527940(1)); the mode table sends 0xB and 0x10 to the same 0x5266A3, so the click lays none of the line and ends the tool
		// A ghost stopped only by the back-of-queue walk's bound is not a refusal of the original's validator: the click commits up to the cap
		if ( Mode is CellToolMode.Path or CellToolMode.Queue or CellToolMode.KartTrack or CellToolMode.WaterTrack && Writer.Preview( start, end ) is { Completed: false, StoppedByOwnLimit: false } refused )
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
		if ( vertices.Count < Writer.VertexCapacity )
		{
			vertices.Add( end );
			pushed = true;
		}
		var writer = Writer;
		var entered = generation;
		var result = writer.Commit( start, end );
		// A commit that finished the work ended, replaced or restarted the tool (without a start, or the same writer anew).
		// Even a same-writer restart owns fresh stacks; do not restore this old commit into them.
		if ( entered != generation || !ReferenceEquals( Writer, writer ) || Start == null )
			return result;
		// [EXT:online-coop-commands] a guest's line went to the host (C3a-2): the tool waits for its outcome with the clicked line as its ghost, nothing pushed, laid or stacked yet
		if ( result.Waiting is { } waiting )
		{
			if ( pushed )
				vertices.RemoveAt( vertices.Count - 1 );
			Waiting = new WaitingLine( waiting, start, end );
			Ghost = writer.Preview( start, end );
			return result;
		}
		return Continue( start, end, result, pushed );
	}

	/// <summary>
	/// [EXT:online-coop-commands] The outcome of the guest's line that waits for the host (C3a-2), once: <paramref name="outcome"/> is its command's result in the guest's park (its
	/// record applied there), and the click's continuation runs with the segment it laid, as for a line laid at once (a refusal included, which lays nothing). A line its record cut
	/// short at a refused cell (the park changed between the click, whose ghost had none, and the record) ends the tool with what it laid, as the click would have ended it on the
	/// refused ghost it then had. Returns that segment, or null when no line waits.
	/// </summary>
	internal SegmentResult? CompleteWaiting( ParkCommandResult outcome )
	{
		ArgumentNullException.ThrowIfNull( outcome );
		if ( Waiting is not { } waiting || Writer == null )
			return null;
		Waiting = null;
		var writer = Writer;
		var entered = generation;
		var result = waiting.Commit.Complete( outcome );
		// [EXT:online-coop-commands] an outcome whose continuation ended, replaced or restarted the tool (a track line that closed its circuit, a cut, C3c-1b) is done there, as a click's commit
		if ( entered != generation || !ReferenceEquals( Writer, writer ) )
			return result;
		Ghost = null;
		if ( result is { Completed: false, StoppedByOwnLimit: false, CommandRefused: false } )
		{
			LastCommit = result;
			End();
			return result;
		}
		var pushed = false;
		if ( vertices.Count < Writer.VertexCapacity )
		{
			vertices.Add( waiting.End );
			pushed = true;
		}
		return Continue( waiting.Start, waiting.End, result, pushed );
	}

	/// <summary>
	/// [EXT:online-coop-commands] Drops the guest's line that waits for the host without laying it (C3a-2): the host took no record of it, so the click is as if never made, and the tool
	/// goes on from its start.
	/// </summary>
	internal void DropWaiting()
	{
		if ( Waiting == null )
			return;
		Waiting = null;
		Ghost = null;
	}

	/// <summary>The commit's continuation: the stacks, the ghost and the next start or the tool's end after a line from <paramref name="start"/> toward <paramref name="end"/> laid <paramref name="result"/>.</summary>
	private SegmentResult Continue( (int X, int Y) start, (int X, int Y) end, SegmentResult result, bool pushed )
	{
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
		// [BIN:TPW-EXE:0x005252D9 line tool push] the kart and water line click pushes its end through 0x52FBF0 and goes on whatever the answer: a full stack (0x401 points) takes no point and the tool continues from the new end (0x81EDE4/E8), the line laid and no segment recorded; the other tools end on it (an OpenTPW guard)
		var fullStackEnds = !pushed && Mode is not (CellToolMode.KartTrack or CellToolMode.WaterTrack);
		if ( end == start || !result.Completed || result.EndedOnExisting || fullStackEnds )
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
		// [BIN:TPW-EXE:0x527B00 edit first-tile latch] in the track edit modes 0xE and 0x13 the hover (0x527960 -> 0x53AAC0(mode | 0x100), 0x5370E0 -> 0x539760 == 1) sets the latch 0x816D48 at 0x537A30 for a refused first tile while no tile is picked (0x7639B0 == -1; a picked tile clears it, 0x527C48): the release plays 0xAF and the mode table ends the tool (0x5266A3)
		if ( Writer!.RefusesFirstClick( cell ) )
		{
			var ghost = Writer.Preview( cell, cell );
			End();
			return ghost with { GhostRefused = true };
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
		// [EXT:online-coop-commands] a guest's Backspace in the path or queue tool takes nothing back: its segments are the host's to undo, and a line or Backspace that waits for the host
		// is not done yet (C3a-2)
		if ( Writer == null || segments.Count == 0 || IsWaiting || !Writer.CanUndo )
			return false;
		var segment = segments[^1];
		// [EXT:online-coop-commands] a guest's Backspace in its track tool goes to the host first, under the ride's claim (C3c-1b): the segment stays until the outcome takes it back
		if ( Writer is IGuestUndoWriter { UndoesByRequest: true } sender )
		{
			if ( sender.SendUndo( segment ) is not { } request )
				return false;
			WaitingUndo = new WaitingUndoLine( request, segment );
			Ghost = null;
			return true;
		}
		segments.RemoveAt( segments.Count - 1 );
		vertices.RemoveAt( vertices.Count - 1 );
		Writer.Undo( segment );
		Start = vertices.Count > 0 ? vertices[^1] : segment.Start;
		Ghost = null;
		return true;
	}

	/// <summary>
	/// [EXT:online-coop-commands] The outcome of the guest's Backspace that waits for the host (C3c-1b), once: <paramref name="outcome"/> is its command's result in the guest's park (its record
	/// applied there), or null when the host took no record of it or its outcome is not known. A Backspace that took the line back pops it as the host's undo does and continues from the
	/// segment's start, and the tool moves on when the walk found the circuit valid; any other takes nothing back. True when it took the line back.
	/// </summary>
	internal bool CompleteUndo( ParkCommandResult? outcome )
	{
		if ( WaitingUndo is not { } waiting || Writer is not IGuestUndoWriter writer )
			return false;
		WaitingUndo = null;
		// nothing else ran while it waited: its segment is still the stack's last
		if ( outcome == null || segments.Count == 0 || !ReferenceEquals( segments[^1], waiting.Segment ) )
			return false;
		return writer.CompleteUndo( outcome, () =>
		{
			segments.RemoveAt( segments.Count - 1 );
			vertices.RemoveAt( vertices.Count - 1 );
			Start = vertices.Count > 0 ? vertices[^1] : waiting.Segment.Start;
			Ghost = null;
		} );
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
		Waiting = null;
		WaitingUndo = null;
		generation++;
		if ( LastCommit?.QueueReceipt != null ) LastCommit = LastCommit with { QueueReceipt = null };
		vertices.Clear();
		segments.Clear();
		if ( wasActive )
			Ended?.Invoke();
	}
}

namespace OpenTPW;

/// <summary>The build tool mode (global <c>data:0x84b04</c>): the mode is the cell type the drag writes.</summary>
public enum CellToolMode : byte
{
	None = 0,
	Path = 1,
	Queue = 3
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
}

/// <summary>
/// The path and queue build tool (PATH-plan §4.3, §9.4), CPU-only and shared by both modes: a start cell, the
/// 1024-entry vertex stack, a ghost preview on cursor moves (the <c>mode | 0x100</c> LayLine) and a commit on
/// clicks (the raw-mode LayLine). A click whose snapped end is the start, a refused cell, or a line ending on an
/// existing path or queue ends the tool; otherwise the end becomes the next start. The HUD and tests drive it.
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

	/// <summary>Starts <paramref name="writer"/>'s mode with <paramref name="start"/> as the first vertex.</summary>
	// [BIN:STP-PPC:0x1007B320 SetMode] tool mode 1 = path, 3 = queue; the drag writes the cell type equal to the mode
	public void Enter( ICellLineWriter writer, (int X, int Y) start )
	{
		ArgumentNullException.ThrowIfNull( writer );
		if ( writer.Mode == CellToolMode.None )
			throw new ArgumentException( "A writer lays path or queue cells.", nameof( writer ) );
		vertices.Clear();
		segments.Clear();
		Writer = writer;
		Mode = writer.Mode;
		Start = start;
		vertices.Add( start );
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
	/// Click: snap, push the end when it differs from the start (or when only the first vertex is stored), commit
	/// the line, then continue from the end or end the tool. Returns the committed segment, or null when off.
	/// </summary>
	// [BIN:STP-PPC:0x10070F84 path commit] push the snapped end unless it equals the start with more than one vertex stored, LayLine(mode, start, end), then end the tool (start −1, SetMode(0)) when end == start or LayLine failed, else continue from the end
	public SegmentResult? Click( (int X, int Y) cell )
	{
		if ( Writer == null || Start is not { } start )
			return null;
		var end = ParkPathBuilder.SnapEnd( start, cell );
		var pushed = false;
		if ( (end != start || vertices.Count == 1) && vertices.Count < VertexCapacity )
		{
			vertices.Add( end );
			pushed = true;
		}
		var result = Writer.Commit( start, end );
		Ghost = null;
		LastCommit = result;
		if ( pushed )
			segments.Add( result );
		// [APPROX:PATH-003] a line whose last cell was already a path or queue cell ends the tool (SetCell raises data:0x84b34 there; its consumer is not traced, the help text 443 says such a click completes the path) — evidence needed: the reader of data 0x84b34
		if ( end == start || !result.Completed || result.EndedOnExisting || !pushed )
			End();
		else
			Start = end;
		return result;
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
		Writer = null;
		Start = null;
		Ghost = null;
		vertices.Clear();
		segments.Clear();
		if ( wasActive )
			Ended?.Invoke();
	}
}

/// <summary>
/// The queue mode's line writer (QUEUE-I's queue rules through the shared tool): each line cell becomes the
/// next queue cell of <see cref="Ride"/> through <c>build</c> (which checks, charges <c>Costs.QueueCell</c> and
/// links the cell back toward the entrance); cells already in the ride's queue are passed over.
/// </summary>
public sealed class QueueLineWriter : ICellLineWriter
{
	private readonly GuestPathGrid grid;
	private readonly Func<int, int, QueueBuildResult> check;
	private readonly Func<int, int, QueueBuildResult> build;
	private readonly Func<int, int, int> remove;
	private readonly Func<long> cellCost;

	/// <param name="check">Validates (x, y) as the ride's next queue cell without writing.</param>
	/// <param name="build">Lays and charges (x, y) as the ride's next queue cell.</param>
	/// <param name="remove">Removes a queue cell and every cell behind it (refunding as the queue rules say).</param>
	public QueueLineWriter( GuestPathGrid grid, RideVisitorBridge ride, Func<int, int, QueueBuildResult> check, Func<int, int, QueueBuildResult> build, Func<int, int, int> remove, Func<long> cellCost )
	{
		this.grid = grid;
		Ride = ride;
		this.check = check;
		this.build = build;
		this.remove = remove;
		this.cellCost = cellCost;
	}

	public RideVisitorBridge Ride { get; }
	public CellToolMode Mode => CellToolMode.Queue;

	public SegmentResult Preview( (int X, int Y) start, (int X, int Y) cursor ) => Run( start, ParkPathBuilder.SnapEnd( start, cursor ), write: false );
	public SegmentResult Commit( (int X, int Y) start, (int X, int Y) end ) => Run( start, ParkPathBuilder.SnapEnd( start, end ), write: true );

	public void Undo( SegmentResult segment )
	{
		if ( segment.Built.Count > 0 )
			remove( segment.Built[0].X, segment.Built[0].Y );
	}

	private SegmentResult Run( (int X, int Y) start, (int X, int Y) end, bool write )
	{
		var line = ParkPathBuilder.LayLine( start, end );
		var built = new List<(int X, int Y)>();
		var existing = new List<(int X, int Y)>();
		var stoppedBy = CellBuildResult.Ok;
		(int X, int Y)? stoppedAt = null;
		var last = line[^1];
		var endedOnExisting = grid.IsWalkable( last.X, last.Y ) || (grid.IsQueue( last.X, last.Y ) && !Ride.QueueCells.Contains( last ));
		foreach ( var (x, y) in line )
		{
			if ( grid.IsQueue( x, y ) && Ride.QueueCells.Contains( (x, y) ) )
			{
				existing.Add( (x, y) );
				continue;
			}
			// The preview checks the first new cell against the queue's back; later ghost cells continue the line.
			var result = write ? build( x, y ) : built.Count == 0 ? check( x, y ) : grid.Cells.TypeAt( x, y ) == ParkCellType.Empty ? QueueBuildResult.Ok : QueueBuildResult.Blocked;
			if ( result != QueueBuildResult.Ok )
			{
				stoppedBy = result switch
				{
					QueueBuildResult.OutOfBounds => CellBuildResult.OutsideTerrain,
					QueueBuildResult.Refused => CellBuildResult.NotEnoughMoney,
					_ => CellBuildResult.Occupied
				};
				stoppedAt = (x, y);
				break;
			}
			built.Add( (x, y) );
		}
		return new SegmentResult( end, built, built.Count * cellCost(), stoppedBy, endedOnExisting )
		{
			Start = start,
			Line = line,
			Existing = existing,
			StoppedAt = stoppedAt
		};
	}
}

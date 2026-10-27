namespace OpenTPW;

/// <summary>
/// The queue mode's line writer (QUEUE-I's queue rules through the shared tool): each line cell becomes the
/// next queue cell of <see cref="Ride"/> through <c>build</c> (which checks, charges <c>Costs.QueueCell</c> and
/// links the cell back toward the entrance); cells already in the ride's queue are passed over.
/// </summary>
public sealed class QueueLineWriter : ICellLineWriter
{
	private readonly GuestPathGrid grid;
	private readonly Func<int, int, IReadOnlyList<(int X, int Y)>, QueueBuildResult> check;
	private readonly Func<int, int, QueueBuildResult> build;
	private readonly Func<int, int, int> remove;
	private readonly Func<long> cellCost;

	/// <param name="check">Validates (x, y) as the ride's next queue cell after the ghost's earlier new cells (as if laid), without writing; the commit's <paramref name="build"/> must refuse nothing this accepts.</param>
	/// <param name="build">Lays and charges (x, y) as the ride's next queue cell.</param>
	/// <param name="remove">Removes a queue cell and every cell behind it (refunding as the queue rules say).</param>
	public QueueLineWriter( GuestPathGrid grid, RideVisitorBridge ride, Func<int, int, IReadOnlyList<(int X, int Y)>, QueueBuildResult> check, Func<int, int, QueueBuildResult> build, Func<int, int, int> remove, Func<long> cellCost )
	{
		this.grid = grid;
		Ride = ride;
		this.check = check;
		this.build = build;
		this.remove = remove;
		this.cellCost = cellCost;
	}

	private QueueSegmentReducer? native;
	private bool nativeActive;
	internal QueueLineWriter WithNativeReducer( QueueSegmentReducer reducer ) { native = reducer; return this; }
	internal bool SupportsNativeStart( (int X, int Y) start ) => grid.Cells.InBounds( start.X, start.Y )
		&& (grid.IsQueue( start.X, start.Y ) && (grid.Cells.OwnerAt( start.X, start.Y ) == 0 || grid.Cells.OwnerAt( start.X, start.Y ) == Ride.OwnerWord)
			|| grid.Cells.HasQueuePassResidue( start.X, start.Y ) && grid.Cells.OwnerAt( start.X, start.Y ) == Ride.OwnerWord && grid.Cells.QueueLinkAt( start.X, start.Y ) != 0);
	internal bool CanUseNative( (int X, int Y) start, (int X, int Y) end ) => native != null && native.SupportsAdmission( start, end ) && SupportsNativeStart( start )
		&& (nativeActive || grid.IsQueue( start.X, start.Y ));
	public void ResetToolSession() => nativeActive = false;
	internal void RememberNativeResult( SegmentResult result ) => nativeActive = result.QueueReceipt != null;
	internal SegmentResult CommitNative( (int X, int Y) start, (int X, int Y) end )
	{
		var result = native!.Commit( start, end ); RememberNativeResult( result ); return result;
	}
	internal bool SupportsNative( (int X, int Y) start, (int X, int Y) end ) => native?.Supports( start, end ) == true;
	internal bool SupportsNativeUndo( (int X, int Y) start, (int X, int Y) end ) => start != ParkPathBuilder.SnapEnd( start, end ) && native?.SupportsUndo( start, end ) == true;
	internal void UndoNative( QueueSegmentReceipt receipt ) => native!.Undo( receipt );
	internal SegmentResult CommitLegacy( (int X, int Y) start, (int X, int Y) end )
	{ nativeActive = false; return Run( start, ParkPathBuilder.SnapEnd( start, end ), write: true ); }

	public RideVisitorBridge Ride { get; }
	public CellToolMode Mode => CellToolMode.Queue;

	public SegmentResult Preview( (int X, int Y) start, (int X, int Y) cursor ) => CanUseNative( start, cursor ) ? native!.Preview( start, cursor ) : Run( start, ParkPathBuilder.SnapEnd( start, cursor ), write: false );
	public SegmentResult Commit( (int X, int Y) start, (int X, int Y) end ) => CanUseNative( start, end ) ? CommitNative( start, end ) : CommitLegacy( start, end );

	public void Undo( SegmentResult segment )
	{
		if ( segment.QueueReceipt is { } receipt && native != null ) { native.Undo( receipt ); return; }
		if ( segment.Built.Count > 0 )
			remove( segment.Built[0].X, segment.Built[0].Y );
	}

	private SegmentResult Run( (int X, int Y) start, (int X, int Y) end, bool write )
	{
		var line = ParkPathBuilder.LayLine( start, end );
		var built = new List<(int X, int Y)>();
		var existing = new List<(int X, int Y)>();
		var stoppedBy = CellBuildResult.Ok;
		var ownLimit = false;
		var linkPointer = false;
		(int X, int Y)? stoppedAt = null;
		var last = line[^1];
		// [BIN:TPW-EXE:0x5345A1 end flag] the last cell ends the tool when it held a path or any queue cell (type 1 or 3) before the write, this ride's own queue included
		var endedOnExisting = grid.IsWalkable( last.X, last.Y ) || grid.IsQueue( last.X, last.Y );
		foreach ( var (x, y) in line )
		{
			if ( grid.IsQueue( x, y ) && Ride.QueueCells.Contains( (x, y) ) )
			{
				existing.Add( (x, y) );
				continue;
			}
			// The preview validates every new cell as the commit would, with the ghost's earlier new cells taken as laid.
			var result = write ? build( x, y ) : check( x, y, built );
			// [BIN:STP-PPC:0x84154 queue line validator] a line whose last cell is a path cell returns code 8, the queue joining the path (it sets the flag 0x7C500 the commit's cat_ui 139 reads): the path cell is not laid, and the line is not refused
			// [BIN:TPW-EXE:0x00535C7E queue line validator] queue over a type-1 cell on the line's last cell (0x820AB8, while 0x816D50 is clear) returns 8; the commit's link pass then links that path cell to the queue cell before it (QueuePaths.Connect)
			if ( result != QueueBuildResult.Ok && (x, y) == last && grid.IsWalkable( x, y ) )
			{
				linkPointer = !write;
				if ( write && line.Count > 1 )
					QueuePaths.Connect( grid, Ride, line[^2], (x, y) );
				break;
			}
			if ( result != QueueBuildResult.Ok )
			{
				stoppedBy = result switch
				{
					QueueBuildResult.OutOfBounds => CellBuildResult.OutsideTerrain,
					QueueBuildResult.Refused => CellBuildResult.NotEnoughMoney,
					_ => CellBuildResult.Occupied
				};
				stoppedAt = (x, y);
				ownLimit = result == QueueBuildResult.TooLong;
				break;
			}
			built.Add( (x, y) );
		}
		return new SegmentResult( end, built, built.Count * cellCost(), stoppedBy, endedOnExisting )
		{
			Start = start,
			Line = line,
			Existing = existing,
			StoppedAt = stoppedAt,
			StoppedByOwnLimit = ownLimit,
			LinkPointer = linkPointer
		};
	}
}

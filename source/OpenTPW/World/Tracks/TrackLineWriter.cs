namespace OpenTPW;

/// <summary>
/// The kart and water track mode of <see cref="CellBuildTool"/> (tool mode 11 or 16, the cell type the drag writes): the line snaps to
/// the dominant axis in steps of two cells, every tile of the line is charged <c>Costs.*TrackCell</c> once, the path cells under a tile
/// are cleared, and consecutive tiles are connected. The level re-walks the circuit through <paramref name="changed"/> after each commit.
/// </summary>
public sealed class TrackLineWriter : ICellLineWriter
{
	private readonly TrackBuilder builder;
	private readonly Action? changed;

	public TrackLineWriter( TrackBuilder builder, Action? changed = null )
	{
		this.builder = builder ?? throw new ArgumentNullException( nameof( builder ) );
		this.changed = changed;
	}

	public TrackBuilder Builder => builder;
	public CellToolMode Mode => builder.Family == TrackFamily.Water ? CellToolMode.WaterTrack : CellToolMode.KartTrack;

	/// <summary>
	/// The end of a track line from <paramref name="start"/> toward <paramref name="cursor"/>: the dominant axis keeps the cursor's
	/// coordinate in whole two-cell steps (rounded toward the start), the other axis keeps the start's. A tie keeps X.
	/// </summary>
	// [BIN:TPW-EXE:0x0053AAC0 track line snap] the line tool snaps to the dominant axis and steps two cells per tile (a tile is 2 × 2 cells)
	// [APPROX:TRACK-009] the rounding of a cursor that lies between two tile steps is toward the start; the original's rounding rule is not traced — evidence needed: the snap routine 0x0053AAC0 for odd distances
	public static (int X, int Y) SnapEnd( (int X, int Y) start, (int X, int Y) cursor )
	{
		var end = ParkPathBuilder.SnapEnd( start, cursor );
		var step = TrackStation.TileCells;
		return (start.X + (end.X - start.X) / step * step, start.Y + (end.Y - start.Y) / step * step);
	}

	/// <summary>The tile origins of the line from <paramref name="start"/> to <paramref name="end"/>, both included, two cells apart.</summary>
	public static IReadOnlyList<(int X, int Y)> LayLine( (int X, int Y) start, (int X, int Y) end )
	{
		var line = new List<(int X, int Y)>();
		var step = TrackStation.TileCells;
		var dx = Math.Sign( end.X - start.X );
		var dy = Math.Sign( end.Y - start.Y );
		var count = Math.Max( Math.Abs( end.X - start.X ), Math.Abs( end.Y - start.Y ) ) / step;
		for ( var index = 0; index <= count; index++ )
			line.Add( (start.X + dx * step * index, start.Y + dy * step * index) );
		return line;
	}

	public SegmentResult Preview( (int X, int Y) start, (int X, int Y) cursor ) => Run( start, SnapEnd( start, cursor ), write: false );

	public SegmentResult Commit( (int X, int Y) start, (int X, int Y) end )
	{
		var result = Run( start, SnapEnd( start, end ), write: true );
		if ( result.Built.Count > 0 || result.Existing.Count > 0 )
			changed?.Invoke();
		return result;
	}

	public void Undo( SegmentResult segment )
	{
		for ( var index = segment.Built.Count - 1; index >= 0; index-- )
			builder.RemoveTile( segment.Built[index].X, segment.Built[index].Y );
		foreach ( var (x, y) in segment.Existing )
		{
			var tile = builder.Tracks.Get( x, y );
			if ( tile.IsOrigin && tile.Overlap > 0 )
			{
				tile.Overlap--;
				builder.Tracks.Set( x, y, tile );
			}
		}
		changed?.Invoke();
	}

	private SegmentResult Run( (int X, int Y) start, (int X, int Y) end, bool write )
	{
		var line = LayLine( start, end );
		var built = new List<(int X, int Y)>();
		var existing = new List<(int X, int Y)>();
		long charged = 0;
		var stoppedBy = CellBuildResult.Ok;
		(int X, int Y)? stoppedAt = null;
		var last = line[^1];
		var endedOnExisting = builder.IsTile( last.X, last.Y );
		for ( var index = 0; index < line.Count; index++ )
		{
			var (x, y) = line[index];
			var cost = builder.TileCost;
			var result = write ? builder.PlaceTile( x, y ) : builder.CheckTile( x, y, charged );
			if ( result == CellBuildResult.Existing )
			{
				existing.Add( (x, y) );
			}
			else if ( result != CellBuildResult.Ok )
			{
				stoppedBy = result;
				stoppedAt = (x, y);
				break;
			}
			else
			{
				built.Add( (x, y) );
				charged += cost;
			}
			if ( write && index > 0 )
				builder.Link( line[index - 1], line[index] );
		}
		return new SegmentResult( end, built, charged, stoppedBy, endedOnExisting )
		{
			Start = start,
			// the preview marks every cell of every tile, the origin first
			Line = line.SelectMany( tile => TrackStation.TileCellsOf( tile.X, tile.Y ) ).ToArray(),
			Existing = existing,
			StoppedAt = stoppedAt
		};
	}
}

namespace OpenTPW;

/// <summary>
/// The kart and water track mode of <see cref="CellBuildTool"/> (tool mode 11 or 16, the cell type the drag writes): the line snaps to
/// the axis of the larger offset in steps of two cells, every tile of the line is charged <c>Costs.*TrackCell</c> once, the path cells under the line's last tile
/// are cleared (a path under any other tile stays and the tile bridges it), and consecutive tiles are connected. The level re-walks the circuit through <paramref name="changed"/> after each commit.
/// </summary>
public sealed class TrackLineWriter : ICellLineWriter
{
	private readonly TrackBuilder builder;
	private readonly Action? changed;
	private readonly int owner;
	private readonly TrackEditor? editor;

	/// <param name="editor">The live ride editor for directional Backspace replay, including its refund and add-on hooks; null retains standalone writer undo.</param>
	/// <param name="owner">The ride the tiles are laid for (<see cref="TrackBuilder.OwnerOf"/>), or 0 for none.</param>
	public TrackLineWriter( TrackBuilder builder, Action? changed = null, int owner = 0, TrackEditor? editor = null )
	{
		this.owner = owner;
		this.editor = editor;
		this.builder = builder ?? throw new ArgumentNullException( nameof( builder ) );
		this.changed = changed;
	}

	public TrackBuilder Builder => builder;
	public CellToolMode Mode => builder.Family == TrackFamily.Water ? CellToolMode.WaterTrack : CellToolMode.KartTrack;

	/// <summary>The kart and water tools' stack holds the corner collector's 0x401 points (the pusher <c>0x52FBF0</c>).</summary>
	// [BIN:TPW-EXE:0x0052FBF0 vertex push] the push answers 0 and stores nothing when the count 0x820A8C is above 0x400, so the stack at 0x81B740 holds 0x401 = 1025 points, the collectors' 0x530830 and 0x530B70 bound (the same test at 0x530C4A)
	public int VertexCapacity => TrackEditor.CornerCapacity;

	/// <summary>
	/// The end of a track line from <paramref name="start"/> toward <paramref name="cursor"/>: the axis is X only when |dx| &gt; |dy| (a tie goes to Y),
	/// the end keeps the start's other coordinate and lies on the axis at start + 2 × floor((cursor − start) / 2), so an odd distance rounds toward the
	/// start in the + direction and away from it in the − direction.
	/// </summary>
	// [BIN:TPW-EXE:0x0053AAC0 track line snap] instruction level 0x53AAC0..0x53AB21: the axis is X only when |dx| > |dy| (the jle at 0x53AB02 sends a tie to Y); with u = start − cursor on that axis an odd positive u is incremented and the end is start − 2 × trunc(u / 2), which is start + 2 × floor((cursor − start) / 2); the walk steps ±2 along the axis at the start's other coordinate
	public static (int X, int Y) SnapEnd( (int X, int Y) start, (int X, int Y) cursor )
	{
		var dx = cursor.X - start.X;
		var dy = cursor.Y - start.Y;
		return Math.Abs( dx ) > Math.Abs( dy )
			? (start.X + 2 * FloorDiv( dx, 2 ), start.Y)
			: (start.X, start.Y + 2 * FloorDiv( dy, 2 ));
	}

	private static int FloorDiv( int value, int divisor ) => (int)Math.Floor( (double)value / divisor );

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
		if ( editor != null )
		{
			editor.UndoLine( segment );
			changed?.Invoke();
			return;
		}
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
		var processed = 0;
		var last = line[^1];
		var endedOnExisting = builder.IsTile( last.X, last.Y );
		for ( var index = 0; index < line.Count; index++ )
		{
			var (x, y) = line[index];
			var cost = builder.TileCost;
			// the type op's last step (0x820AD0): only the line's end tile removes the paths under it
			var result = write ? builder.PlaceTileInLine( x, y, owner, last: index == line.Count - 1 ) : builder.CheckTile( x, y, charged, owner );
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
			processed = index + 1;
		}
		if ( write && line.Count > 1 )
		{
			// the line's link op: every tile the line reached takes the line's direction as its flow link (a tile with more than two arms adds it)
			var direction = TrackBuilder.HeadingBetween( line[0], line[1] );
			for ( var index = 0; index < processed; index++ )
				builder.ApplyLineLink( line[index], direction );
		}
		if ( write && processed > 0 )
			builder.RunBridgeOps( line.Take( processed ).ToArray() );
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

namespace OpenTPW;

/// <summary>The map the tile ghost reads: the cells, the second-layer records of its tiles and the ground under them.</summary>
internal interface ITileGhostMap
{
	bool InBounds( int x, int y );
	/// <summary>The map cell (x, y): its type, flags and owner.</summary>
	TileCellFacts CellAt( int x, int y );
	/// <summary>The second-layer record of the cell (x, y), with the type 0 where it holds none.</summary>
	TileRecordFacts RecordAt( int x, int y );
	/// <summary>The record of the cell (x, y) as the chooser's neighbour tests read it: an extension replaced by its parent.</summary>
	TileNeighbourFacts NeighbourAt( int x, int y );
	/// <summary>The cell lookup <c>0x452AE0(x, y, 0)</c>: the ground of the cell, in original units.</summary>
	float Ground( int x, int y );
}

/// <summary>One face of the tile ghost as the original queues it (0x53C8D0): the tile's origin cell, the face code (0 the floor, 1, 4, 0x10 and 0x40 the walls), the texture, the height step, the direction byte and two flags.</summary>
internal readonly record struct TileFace( int X, int Y, int Code, int Texture, long Step, int Direction, int Flag, int Tile );

/// <summary>The outcome of the tile ghost: the faces in the order they are queued, each tile's answer, and the chooser's words after the last tile.</summary>
internal sealed record TileLineGhostResult( List<TileFace> Faces, List<((int X, int Y) Origin, int Answer)> Tiles, TileLatches Latches );

/// <summary>
/// The kart and water track line tools' and the fence tool's preview (<c>0x53AAC0</c> with the ghost tile routine <c>0x5370E0</c>): one 2 x 2 face for each tile along the line, chosen by <see cref="TrackTileChooser"/>
/// (blue, red, or <c>m_end</c> where the line meets a tile of its own kind), raised to the tile's ground and walled where a neighbouring tile stands lower.
/// </summary>
internal static class TileLineGhost
{
	/// <summary>Quantizes a ground height to the marker raise: 10 x ((int)H + 9) / 10 with both divisions truncating.</summary>
	private static float? Quantize( float height ) => BlueprintHeights.Quantize( height );

	/// <summary>
	/// The raise of a tile's marker: the origin's record (0xB, 0x10, 0x19, 7, 4, 9 or 10) sets it from the ground, then the map cells of the tile in the order origin, +Y, +X, diagonal each set it again when their type is one
	/// of those (the last one wins, it is no maximum).
	/// </summary>
	// [BIN:TPW-EXE:0x00536CA0 tile raise] the origin cell, then the neighbours 0x7CDBA8 (+Y), 0x7CDBC8 (+X) and 0x7CDBB8 (+X+Y): a cell of map type 0xB, 0x10, 0x19, 7, 4, 9 or 10 replaces the running value by 10 x ((int)H + 9) / 10 of its ground (0x452AE0, 0x67A830); the value starts as the origin record's own (0x5370E0: record type 0xB, 0x10, 0x19, 7, 4, 9 or 10)
	public static float? TileRaise( ITileGhostMap map, int x, int y )
	{
		static bool Raises( byte type ) => type is 0xB or 0x10 or 0x19 or 7 or 4 or 9 or 10;
		var value = 0f;
		if ( Raises( map.RecordAt( x, y ).Type ) )
		{
			if ( Quantize( map.Ground( x, y ) ) is not { } record )
				return null;
			value = record;
		}
		foreach ( var (dx, dy) in new[] { (0, 0), (0, 1), (1, 0), (1, 1) } )
		{
			var (cx, cy) = (x + dx, y + dy);
			if ( !map.InBounds( cx, cy ) || !Raises( map.CellAt( cx, cy ).Type ) )
				continue;
			if ( Quantize( map.Ground( cx, cy ) ) is not { } cell )
				return null;
			value = cell;
		}
		return value;
	}

	/// <summary>The wall faces of a tile: on each side in the order -Y, +X, +Y, -X, one face for every 10 units from the neighbouring tile's raise (two cells away) up to the tile's, where the neighbour exists and stands lower.</summary>
	// [BIN:TPW-EXE:0x005373CE tile walls] each side reads the record two cells on (0x50C320 with 0x7CDBA0, 0x7CDBC8, 0x7CDBA8, 0x7CDBD0), computes its raise as the tile's own and compares the integers: a lower neighbour gets a wall face (0x53C8D0, y = step + 1.5) every 10 units
	public static IEnumerable<(int Side, long Step)> WallSteps( ITileGhostMap map, int x, int y, float current )
	{
		foreach ( var (dx, dy, side) in new[] { (0, -2, 1), (2, 0, 4), (0, 2, 0x10), (-2, 0, 0x40) } )
		{
			if ( !map.InBounds( x + dx, y + dy ) || TileRaise( map, x + dx, y + dy ) is not { } neighbour )
				continue;
			for ( var step = (long)neighbour; step < (long)current; step += 10 )
				yield return (side, step);
		}
	}

	/// <summary>The line walker's tiles: the snapped end, the step and the tiles in order (the start, then every second cell to the end).</summary>
	// [BIN:TPW-EXE:0x0053AAC0 tile walker] the dominant axis is walked in steps of 2 from the start to the end snapped to an even distance (a positive odd distance counts one more); the last tile is the only one with the last word 0x820AD0, the first in the map the only one with the first word 0x820AC8
	public static (List<(int X, int Y)> Tiles, (int X, int Y) End, int StepX, int StepY) Walk( (int X, int Y) start, (int X, int Y) end )
	{
		static int Half( int value ) => value / 2;
		var tiles = new List<(int X, int Y)>();
		if ( Math.Abs( end.X - start.X ) > Math.Abs( end.Y - start.Y ) )
		{
			var distance = start.X - end.X;
			if ( distance > 0 && distance % 2 != 0 )
				distance++;
			var snapped = start.X - Half( distance ) * 2;
			var step = start.X > snapped ? -2 : start.X == snapped ? 0 : 2;
			for ( var x = start.X; x != snapped; x += step )
				tiles.Add( (x, start.Y) );
			tiles.Add( (snapped, start.Y) );
			return (tiles, (snapped, start.Y), step, 0);
		}
		else
		{
			var distance = start.Y - end.Y;
			if ( distance > 0 && distance % 2 != 0 )
				distance++;
			var snapped = start.Y - Half( distance ) * 2;
			var step = start.Y > snapped ? -2 : start.Y == snapped ? 0 : 2;
			for ( var y = start.Y; y != snapped; y += step )
				tiles.Add( (start.X, y) );
			tiles.Add( (start.X, snapped) );
			return (tiles, (start.X, snapped), 0, step);
		}
	}

	/// <summary>The walker's passes of the Buy Land fence ghost: the hovered post alone before the first click, else the four edges of the rectangle between the corner and the hovered post.</summary>
	// [BIN:TPW-EXE:0x00523A50 fence passes] top edge (minX, +2 when minX != maxX, minY) to (maxX, -2, minY); right edge (maxX, minY) to (maxX, maxY); bottom edge as the top at maxY; left edge (minX, maxY) back up to (minX, minY)
	public static List<((int X, int Y) From, (int X, int Y) To)> FencePasses( (int X, int Y)? corner, (int X, int Y) post )
	{
		if ( corner is not { } start )
			return [(post, post)];
		var (minX, maxX) = (Math.Min( start.X, post.X ), Math.Max( start.X, post.X ));
		var (minY, maxY) = (Math.Min( start.Y, post.Y ), Math.Max( start.Y, post.Y ));
		var inner = minX != maxX ? 2 : 0;
		return [((minX + inner, minY), (maxX - inner, minY)), ((maxX, minY), (maxX, maxY)), ((minX + inner, maxY), (maxX - inner, maxY)), ((minX, maxY), (minX, minY))];
	}

	/// <summary>Runs the preview over the tiles of the line from <paramref name="start"/> to <paramref name="end"/>.</summary>
	// [BIN:TPW-EXE:0x005372AD tile ghost] the chooser answers (mode, 0, direction byte): 1 refuses (a second call with 1, the refusal word 0x527940, then the red floor and red walls), 0 takes the default texture 0 and any other answer is the texture (0xB m_end); the floor is queued at the tile's raise + 1.5 (size flag 1), the walls after it
	public static TileLineGhostResult Run( ITileGhostMap map, int mode, (int X, int Y) start, (int X, int Y) end, in LineGhostTileTool tool, TileLatches latches )
	{
		var faces = new List<TileFace>();
		var tiles = new List<((int X, int Y) Origin, int Answer)>();
		var (line, _, stepX, stepY) = Walk( start, end );
		// 0x820AC8 stays set until a tile in the map went through the ghost routine
		var first = true;
		for ( var index = 0; index < line.Count; index++ )
		{
			var (x, y) = line[index];
			if ( !map.InBounds( x, y ) )
				continue;
			byte direction = stepX == -2 ? (byte)0x40 : stepX == 2 ? (byte)4 : stepY == -2 ? (byte)1 : stepY == 2 ? (byte)0x10 : (byte)0;
			var record = map.RecordAt( x, y );
			var chooser = new TileTool( mode, false, direction, first, index == line.Count - 1, tool.HoverIsStart, tool.ToolOwner, tool.ToolPriority, tool.Price, (x, y), tool.GateA, tool.GateB );
			var answer = TrackTileChooser.Choose( chooser, map.CellAt( x, y ), map.CellAt( x + 1, y ), map.CellAt( x + 1, y + 1 ), map.CellAt( x, y + 1 ),
				new TileRecordFacts( record.Type, record.Links, record.Direction, record.Flags, record.Owner ), map.NeighbourAt( x + 1, y ), map.NeighbourAt( x + 1, y + 1 ), map.NeighbourAt( x, y + 1 ), ref latches );
			first = false;
			var raise = TileRaise( map, x, y ) ?? 0f;
			var refused = answer == 1;
			if ( refused )
			{
				TrackTileChooser.Choose( chooser with { Second = true }, map.CellAt( x, y ), map.CellAt( x + 1, y ), map.CellAt( x + 1, y + 1 ), map.CellAt( x, y + 1 ),
					new TileRecordFacts( record.Type, record.Links, record.Direction, record.Flags, record.Owner ), map.NeighbourAt( x + 1, y ), map.NeighbourAt( x + 1, y + 1 ), map.NeighbourAt( x, y + 1 ), ref latches );
				latches.Refused = true;
			}
			var texture = refused ? 1 : answer == 0 ? 0 : answer;
			faces.Add( new TileFace( x, y, 0, texture, (long)raise, 1, 0, 1 ) );
			foreach ( var (side, step) in WallSteps( map, x, y, raise ) )
				faces.Add( new TileFace( x, y, side, refused ? 1 : 0, step, 0, !refused && answer == 0xD ? 1 : 0, 1 ) );
			tiles.Add( ((x, y), answer) );
		}
		return new TileLineGhostResult( faces, tiles, latches );
	}
}

/// <summary>The tool state the tile ghost takes from the hover: the ride under the tool, its priority and price, the hovered start and the fence tool's gate posts.</summary>
internal readonly record struct LineGhostTileTool( ushort ToolOwner, int ToolPriority, int Price, bool HoverIsStart, (int X, int Y) GateA, (int X, int Y) GateB );

/// <summary>The tile ghost's view of a typed park: the cells, the second layer's records (a kart or water track piece, a fence post) and the ground the cell lookup 0x452AE0 gives.</summary>
internal sealed class ParkTileGhostMap( ParkCellMap cells, Func<int, int, int?> ownerPriority, Func<int, int, float> ground ) : ITileGhostMap
{
	public bool InBounds( int x, int y ) => cells.InBounds( x, y );

	public TileCellFacts CellAt( int x, int y ) => new( cells.PrimaryTypeAt( x, y ), (ushort)cells.FlagsAt( x, y ), cells.OwnerAt( x, y ), ownerPriority( x, y ) );

	// [BIN:TPW-EXE:0x00539760 tile record] the chooser reads the record of the tile's origin cell (+8 type, +0xC links, +0xD direction, +0xE flags, +0x10 owner); a map cell of type 2, 7 or 30 stands in the record layer as type 7 (the world's no-terrain marker)
	public TileRecordFacts RecordAt( int x, int y )
	{
		if ( !cells.InBounds( x, y ) )
			return default;
		var track = cells.Tracks.Get( x, y );
		if ( !track.IsEmpty )
			return new TileRecordFacts( track.IsExtension ? (byte)0xC : track.Type, track.Mask, track.Link, track.Flags, unchecked( (ushort)track.Owner ) );
		var land = cells.Land?.LayerFor( cells.Width, cells.Height ).TypeAt( x, y ) ?? 0;
		if ( land != 0 )
			return new TileRecordFacts( land, 0, 0, 0, 0 );
		return cells.PrimaryTypeAt( x, y ) is 2 or 7 or 30 ? new TileRecordFacts( 7, 0, 0, 0, 0 ) : default;
	}

	public TileNeighbourFacts NeighbourAt( int x, int y )
	{
		var own = RecordAt( x, y );
		if ( own.Type == 0 )
			return default;
		if ( own.Type is 0xC or 0x11 && cells.Tracks.Get( x, y ) is { IsExtension: true, Parent: >= 0 } extension )
		{
			var (px, py) = cells.Tracks.CellOf( extension.Parent );
			var parent = RecordAt( px, py );
			return new TileNeighbourFacts( true, parent.Type, parent.Owner );
		}
		return new TileNeighbourFacts( true, own.Type, own.Owner );
	}

	public float Ground( int x, int y ) => ground( x, y );
}

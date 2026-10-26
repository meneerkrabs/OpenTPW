namespace OpenTPW;

/// <summary>The size-classed sounds of a placement and of a demolition (tp.exe <c>0x528a70</c> and <c>0x527ee0</c>).</summary>
public sealed partial class Level
{
	/// <summary>The <c>cat_ui</c> cue of a placement by the number of footprint cells of shape type 4: under 4 cells 142, 4-7 141, 8-15 143, more 144 (the original's own order: 0x8E, 0x8D, 0x8F, 0x90).</summary>
	// [BIN:TPW-EXE:0x528e30 placement cue] the commit pass of 0x528a70 counts the shape cells whose type word is 4 (0x528b..: the loop over the shape, 0x5361c0's bounds test 0x4d8300 per cell) and, when no layout replays (0x785a2c is 0), submits cat_ui (0x803a2c) 0x8E below 4 cells (0x528e61), 0x8D below 8 (0x528e9d), 0x8F below 16 (0x528ed8), else 0x90 (0x528efb)
	public static uint PlacementCueEvent( int cells ) => cells < 4 ? 142u : cells < 8 ? 141u : cells < 16 ? 143u : 144u;

	/// <summary>The <c>cat_ui</c> cue of a demolition by the number of footprint cells whose map type is 4: under 4 cells 150, 4-7 151, 8-15 152, more 153 (0x96-0x99).</summary>
	// [BIN:TPW-EXE:0x5285a2 demolition cue] the object demolition 0x527ee0 counts the footprint cells whose map type is 4 (0x536380) before clearing them and, when no layout replays (0x785a2c is 0), submits cat_ui 0x96 below 4 cells (0x5285cd), 0x97 below 8 (0x528604), 0x98 below 16 (0x528649), else 0x99 (0x528679)
	public static uint DemolitionCueEvent( int cells ) => cells < 4 ? 150u : cells < 8 ? 151u : cells < 16 ? 152u : 153u;

	/// <summary>The unit tp.exe multiplies a cell coordinate by for these cues (<c>0x700cd8</c>: 10.0, a cell is ten sound units).</summary>
	private const float CueCellUnits = 10f;

	/// <summary>
	/// Where a placement's cue plays: the shape's anchor cell for the click (the click less half the shape, truncated: <see cref="GetCentredAnchor(ObjectShape, int, int, int)"/> at rotation 0)
	/// times ten, at height 0.
	/// </summary>
	// [BIN:TPW-EXE:0x528e39 placement cue place] the cue's x and z are __ftol of the clicked cell's x and y, as 0x528b44-0x528b68 left them after taking half the shape off each, times 10.0 (0x700cd8); y is 0
	internal static Vector3 PlacementCuePlace( ObjectShape shape, int clickX, int clickY )
	{
		var anchor = GetCentredAnchor( shape, clickX, clickY, 0 );
		return new Vector3( anchor.X * CueCellUnits, 0, anchor.Y * CueCellUnits );
	}

	/// <summary>
	/// Where a demolition's cue plays: the shape's width and height in cells times ten, at height 0. The original reads the counters its footprint loops left in the argument slots
	/// (0x528560: rows, and the width the inner loop ended on) rather than the object's cell, so the cue is audible only near the map's corner.
	/// </summary>
	// [BIN:TPW-EXE:0x5285a2 demolition cue place] x is __ftol of the inner loop's final column count (the shape's width, +0x48 of the frame) times 10.0, z of the final row count (the shape's height, 0x52856c stores it to +0x4c), y is 0
	internal static Vector3 DemolitionCuePlace( ObjectShape shape ) => new( shape.Width * CueCellUnits, 0, shape.Height * CueCellUnits );

	/// <summary>The footprint cells of <paramref name="shape"/> placed at anchor (<paramref name="anchorX"/>, <paramref name="anchorY"/>) that a placement counts: shape type 4 and on the map.</summary>
	internal static int PlacementCueCells( ParkCellMap? cells, ObjectShape shape, int anchorX, int anchorY, int rotation )
	{
		var count = 0;
		foreach ( var (x, y, cell) in ObjectFootprint.GetCells( shape, anchorX, anchorY, rotation ) )
			if ( cell.CellType == ObjectShapeSymbols.TypeOccupied && (cells == null || cells.InBounds( x, y )) )
				count++;
		return count;
	}

	/// <summary>The footprint cells of <paramref name="item"/> whose map type is 4 right now (a demolition counts them before it clears them).</summary>
	internal static int DemolitionCueCells( ParkCellMap? cells, IEnumerable<(int X, int Y, ObjectShapeCell Cell)> footprint )
	{
		var count = 0;
		foreach ( var (x, y, cell) in footprint )
		{
			if ( cells == null )
			{
				if ( ParkCellMap.FootprintType( cell.CellType ) == ParkCellTypes.Object )
					count++;
			}
			else if ( cells.InBounds( x, y ) && cells.RawTypeAt( x, y ) == ParkCellTypes.Object )
				count++;
		}
		return count;
	}

	/// <summary>The cue of a placement just committed on the build click (<paramref name="clickX"/>, <paramref name="clickY"/>).</summary>
	private void PlayPlacementCue( ObjectCatalogEntry entry, int clickX, int clickY, int rotation )
	{
		// [EXT:online-coop-commands] a placement's cue is its issuer's: a peer applying another player's placement plays none (C3a-2)
		if ( Commands is { IsLocalIssuer: false } )
			return;
		var anchor = GetCentredAnchor( entry, clickX, clickY, rotation );
		GameAudio.PlayUiAt( PlacementCueEvent( PlacementCueCells( Guests?.Grid.Cells, entry.Shape, anchor.X, anchor.Y, rotation ) ), PlacementCuePlace( entry.Shape, clickX, clickY ) );
	}

	/// <summary>The cue of an object about to be demolished (the removal event, raised before the footprint is cleared).</summary>
	private void PlayDemolitionCue( OriginalObject item )
	{
		// [EXT:online-coop-commands] a demolition's cue is its issuer's: a peer applying another player's removal plays none (C3a-2)
		if ( Commands is { IsLocalIssuer: false } )
			return;
		GameAudio.PlayUiAt( DemolitionCueEvent( DemolitionCueCells( Guests?.Grid.Cells, item.Cells ) ), DemolitionCuePlace( item.Entry.Shape ) );
	}
}

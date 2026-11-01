namespace OpenTPW;

public partial class Level
{
	/// <summary>
	/// The path or queue tool's preview line as the original's ghost marks it (<see cref="BlueprintLineGhost"/>): each cell of the hover's line takes the validator's answer, with the money of the park, the ride under
	/// the queue tool and the heights of the typed map. Null for the other tools and for a world that types no cells (a read-only visit, a Theme Park Inc layout, a harness world), whose ghosts are the host's own line.
	/// </summary>
	// [EXT:untyped-harness-world] a world that does not type its cells has nothing for the validator to read: its ghost is the host's preview line (cells before its first refusal blue, the rest red)
	internal LineGhostResult? PathLineGhost( SegmentResult ghost )
	{
		if ( !TypesFootprints || Guests?.Grid.Cells is not { } cells || ghost.Line.Count == 0 )
			return null;
		var queue = ParkCommandLineWriter.Unwrap( CellTool.Writer ) as QueueLineWriter;
		var mode = CellTool.Mode switch { CellToolMode.Path => 1, CellToolMode.Queue when queue != null => 3, _ => 0 };
		if ( mode == 0 )
			return null;
		var economy = Park?.Economy;
		var price = economy == null ? 0 : (int)economy.CellCost( mode == 1 ? CellPurchase.Path : CellPurchase.Queue );
		// [BIN:TPW-EXE:0x00524724 hover flag] the hover sets 0x818688 when the line has a start and the snapped end is that cell
		var map = new ParkCellGhostMap( cells, mode, OwnerPriorityAt, BlueprintHeightOffset );
		return BlueprintLineGhost.Run( map, ghost.Line, new LineGhostTool( mode, queue?.Ride.OwnerWord ?? 0, CellTool.Start == ghost.SnappedEnd, price, economy == null ? null : unchecked( (int)economy.Balance ) ) );
	}

	/// <summary>The kart and water line tool's preview as the original's tile ghost marks it (<see cref="TileLineGhost"/>); null for the other tools and for a world that types no cells.</summary>
	// [EXT:untyped-harness-world] a world that does not type its cells has no records for the tile chooser to read: its ghost is the host's own line
	internal TileLineGhostResult? TileLineGhostFor( SegmentResult ghost )
	{
		if ( !TypesFootprints || Guests?.Grid.Cells is not { } cells || OriginalPark?.Heightfield is not { } field || ghost.Symbols.Count > 0 )
			return null;
		if ( ParkCommandLineWriter.Unwrap( CellTool.Writer ) is not TrackLineWriter writer )
			return null;
		var mode = writer.Mode == CellToolMode.WaterTrack ? 0x10 : 0xB;
		var economy = Park?.Economy;
		var price = economy == null ? 0 : (int)economy.CellCost( mode == 0xB ? CellPurchase.KartTrack : CellPurchase.WaterTrack );
		var map = new ParkTileGhostMap( cells, OwnerPriorityAt, ( x, y ) => BlueprintGround( cells, field, x, y ) );
		// [BIN:TPW-EXE:0x00524724 hover flag] the hover sets 0x818688 when the line has a start and the snapped end is that cell
		var tool = new LineGhostTileTool( unchecked( (ushort)writer.Owner ), 3, price, CellTool.Start == ghost.SnappedEnd, (0, 0), (0, 0) );
		var latches = new TileLatches { Bank = economy == null ? null : unchecked( (int)economy.Balance ) };
		return TileLineGhost.Run( map, mode, ghost.Start, ghost.SnappedEnd, tool, latches );
	}

	/// <summary>The raise of a tile's marker, <see cref="TileLineGhost.TileRaise"/> over the typed map; null in a world that types no cells.</summary>
	private float? BlueprintTileRaise( int x, int y )
	{
		if ( !TypesFootprints || Guests?.Grid.Cells is not { } cells || OriginalPark?.Heightfield is not { } field )
			return null;
		return TileLineGhost.TileRaise( new ParkTileGhostMap( cells, OwnerPriorityAt, ( cx, cy ) => BlueprintGround( cells, field, cx, cy ) ), x, y );
	}
}

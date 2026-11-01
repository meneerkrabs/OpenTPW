namespace OpenTPW;

/// <summary>What the line ghost reads of one cell: the cell record and the structure record under it (docs/reverse/BLUEPRINT.md).</summary>
/// <param name="Type">The cell's map type (+8).</param>
/// <param name="Flags">The cell's flag word (+0xE).</param>
/// <param name="Links">The cell's link byte (+0xC).</param>
/// <param name="Owner">The cell's owner word (+0x10).</param>
/// <param name="StructureType">The type of the cell's structure record, an extension replaced by its parent's (<see cref="QueueCellValidator.StructureTypeAt"/>).</param>
/// <param name="StructureLinks">The link byte of that record, which tells a corner or a junction.</param>
/// <param name="OwnerPriority">The overwrite priority of the class-3 thing that owns an object cell, or null.</param>
/// <param name="NeighbourType">For a queue cell with one link, the type of the cell that link points to (the path tool's test of a queue end).</param>
internal readonly record struct GhostCell( byte Type, ushort Flags, byte Links, ushort Owner, byte StructureType, byte StructureLinks, int? OwnerPriority, byte NeighbourType );

/// <summary>The map the line ghost reads: its cells and the raise of their markers.</summary>
internal interface ILineGhostMap
{
	bool InBounds( int x, int y );
	GhostCell CellAt( int x, int y );
	/// <summary>The raise of the cell's marker (<see cref="BlueprintHeights.Sample"/>), or null where the world has none.</summary>
	float? RaiseAt( int x, int y );
}

/// <summary>The state the path or queue tool's hover hands to the ghost (the globals the original keeps for the tool).</summary>
/// <param name="Mode">1 for the path tool, 3 for the queue tool.</param>
/// <param name="ToolOwner">The owner word of the ride under the queue tool (0x818698), 0 for the path tool.</param>
/// <param name="HoverIsStart">The line has a start and the snapped end is that cell (0x818688).</param>
/// <param name="Price">The price of one path (0x81B6DC) or queue (0x81EA14) cell, in the original's 32 bits.</param>
/// <param name="Bank">The park's money in the original's signed 32-bit word, or null while no money is kept (free building).</param>
internal readonly record struct LineGhostTool( int Mode, ushort ToolOwner, bool HoverIsStart, int Price, int? Bank );

/// <summary>
/// The outcome of the line ghost: the marked cells, with the walls' cells the same marker, and the validator's sticky words after the last cell, which the pointer and the click read.
/// </summary>
internal sealed record LineGhostResult( List<(int X, int Y, BlueprintMarker Marker)> Cells, CellValidatorLatches Latches, bool Refused );

/// <summary>
/// The path and queue tools' preview line as the original draws it (<c>LayLine(mode | 0x100)</c>, <c>0x536100</c>, calling the ghost <c>SetCell</c> <c>0x532FC0</c> for each cell): the validator <c>0x535670</c> answers each cell
/// in turn with its sticky words, and the marker texture is the answer (0 blue, 1 red, 8 <c>m_link</c>, 0xB <c>m_end</c>); a cell that stands higher than a neighbour gets walls, which clear the refusal words.
/// </summary>
internal static class BlueprintLineGhost
{
	private static readonly (int Dx, int Dy)[] Neighbours = [(0, -1), (1, 0), (0, 1), (-1, 0)];

	/// <summary>The cells of <paramref name="line"/> marked in the original's order.</summary>
	// [BIN:TPW-EXE:0x00536100 LayLine] the line's first cell is the only one with the line-start word 0x820AA4 set (cleared after its SetCell) and its last cell the only one with the last-cell word 0x820AB8 (set before it), so a one-cell line is both
	// [BIN:TPW-EXE:0x005331B5 ghost texture] the ghost asks the validator (0x535670(mode, 0)): 1 refuses (the red face 0x533927, after a second call with the argument 1 and the refusal word 0x527940), 0 takes the tool's own texture (0, blue) and any other answer is the texture (8 m_link, 0xB m_end)
	// [BIN:TPW-EXE:0x005333BD wall latch reset] each wall face of an allowed cell is added after the refusal words 0x816D58 and 0x816D50 are cleared; the refused cell's walls (0x533B24..) clear nothing
	public static LineGhostResult Run( ILineGhostMap map, IReadOnlyList<(int X, int Y)> line, in LineGhostTool tool )
	{
		var latches = new CellValidatorLatches { Bank = tool.Bank };
		var cells = new List<(int X, int Y, BlueprintMarker Marker)>( line.Count );
		var refused = false;
		// [BIN:TPW-EXE:0x00524571 tool priority] the hover writes the overwrite priority 0x818C10: 2 for the path tool (0x524571) and 3 for the queue tool (0x524588, where ESI is the 3 loaded at 0x523546); the click writes the same (0x527200, 0x527218)
		var toolPriority = tool.Mode == 3 ? 3 : 2;
		// 0x820AA4 stays set until a cell was in the map and went through SetCell
		var inside = true;
		for ( var index = 0; index < line.Count; index++ )
		{
			var (x, y) = line[index];
			if ( !map.InBounds( x, y ) )
				continue;
			var cell = map.CellAt( x, y );
			var input = new CellValidatorInput( tool.Mode, cell.Type, cell.Flags, cell.StructureType, cell.StructureLinks, cell.Links, cell.Owner == tool.ToolOwner, index == line.Count - 1,
				inside && tool.HoverIsStart, cell.NeighbourType, cell.OwnerPriority, toolPriority )
			{ Price = tool.Mode == 1 || tool.Mode == 3 ? tool.Price : 0 };
			inside = false;
			var answer = QueueCellValidator.Check( input, ref latches );
			if ( answer == 1 )
			{
				QueueCellValidator.Check( input with { Full = true }, ref latches );
				refused = true;
				cells.Add( (x, y, BlueprintMarker.Refused) );
				continue;
			}
			cells.Add( (x, y, answer switch { 8 => BlueprintMarker.Link, 0xB => BlueprintMarker.End, _ => BlueprintMarker.Allowed }) );
			if ( HasWalls( map, x, y ) )
			{
				latches.Pending = false;
				latches.Refuse = false;
			}
		}
		return new LineGhostResult( cells, latches, refused );
	}

	/// <summary>Whether the marker of the cell gets at least one wall face: a neighbour on the map stands lower than the cell (<see cref="BlueprintMarkers"/> draws them).</summary>
	internal static bool HasWalls( ILineGhostMap map, int x, int y )
	{
		if ( map.RaiseAt( x, y ) is not { } current )
			return false;
		foreach ( var (dx, dy) in Neighbours )
			if ( map.InBounds( x + dx, y + dy ) && map.RaiseAt( x + dx, y + dy ) is { } neighbour && (long)neighbour < (long)current )
				return true;
		return false;
	}
}

/// <summary>The line ghost's map over the park's cell map.</summary>
internal sealed class ParkCellGhostMap( ParkCellMap cells, int mode, Func<int, int, int?> ownerPriority, Func<int, int, float?> raise ) : ILineGhostMap
{
	public bool InBounds( int x, int y ) => cells.InBounds( x, y );

	public GhostCell CellAt( int x, int y )
	{
		var track = cells.Tracks.Get( x, y );
		var origin = track;
		if ( track.IsExtension && track.Parent >= 0 )
		{
			var (px, py) = cells.Tracks.CellOf( track.Parent );
			origin = cells.Tracks.Get( px, py );
		}
		var type = cells.PrimaryTypeAt( x, y );
		var links = cells.LinksAt( x, y );
		byte neighbour = 0;
		if ( mode == 1 && type == 3 && System.Numerics.BitOperations.PopCount( links ) == 1 )
		{
			var (dx, dy) = PathTiles.Offsets[System.Numerics.BitOperations.TrailingZeroCount( links )];
			neighbour = cells.RawTypeAt( x + dx, y + dy );
		}
		return new GhostCell( type, (ushort)cells.FlagsAt( x, y ), links, cells.OwnerAt( x, y ), QueueCellValidator.StructureTypeAt( cells, x, y ), origin.IsEmpty ? (byte)0 : origin.Mask, ownerPriority( x, y ), neighbour );
	}

	public float? RaiseAt( int x, int y ) => raise( x, y );
}

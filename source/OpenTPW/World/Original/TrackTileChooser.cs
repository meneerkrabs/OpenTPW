namespace OpenTPW;

/// <summary>What the tile chooser reads of one of the four map cells of a tile.</summary>
/// <param name="Type">The cell's map type (+8).</param>
/// <param name="Flags">The cell's flag word (+0xE): 0x20 NoModify, 0x40 land the park does not own.</param>
/// <param name="Owner">The cell's owner word (+0x10).</param>
/// <param name="OwnerPriority">For a type 4, 9 or 10 cell, the overwrite priority of the class-3 thing its owner word names, or null when there is none (the pass's priority then stays as it was).</param>
internal readonly record struct TileCellFacts( byte Type, ushort Flags, ushort Owner, int? OwnerPriority );

/// <summary>What the tile chooser reads of the second-layer record at the tile's origin cell.</summary>
/// <param name="Type">The record type (+8): 0 none, 0xB and 0x10 a kart or water track tile, 0xD and 0x12 a station, 0x19 a fence post, 7 no terrain.</param>
/// <param name="Links">The record's link byte (+0xC).</param>
/// <param name="Direction">The record's direction byte (+0xD).</param>
/// <param name="Flags">The record's flag word (+0xE): the low nibble the bridge bits, 0x10 an add-on, 0x40 unowned.</param>
/// <param name="Owner">The record's owner word (+0x10), the ride whose track it is.</param>
internal readonly record struct TileRecordFacts( byte Type, byte Links, byte Direction, ushort Flags, ushort Owner );

/// <summary>One of the three other second-layer records of the tile (cells +X, +X+Y and +Y), an extension already followed to its parent.</summary>
/// <param name="Present">The record's own type is not 0.</param>
/// <param name="Type">The type of the record after an extension (0xC, 0x11) was replaced by its parent.</param>
/// <param name="Owner">The owner word of that record.</param>
internal readonly record struct TileNeighbourFacts( bool Present, byte Type, ushort Owner );

/// <summary>The sticky words and the cost the tile chooser keeps for a pass over a line of tiles.</summary>
internal struct TileLatches
{
	/// <summary>0x816D50: refuses every later tile of the pass.</summary>
	public bool Refuse;
	/// <summary>0x816D58: set by a refused tile, promoted to <see cref="Refuse"/> at the next call.</summary>
	public bool Pending;
	/// <summary>0x816D54.</summary>
	public bool L54;
	/// <summary>0x816D5C: the cash pointer's flag.</summary>
	public bool Cash;
	/// <summary>0x816D4C: the line meets a tile of its own kind (the end pointer).</summary>
	public bool MeetsOwn;
	/// <summary>0x816D48: the refusal word 0x527940 sets.</summary>
	public bool Refused;
	/// <summary>0x81AF4C: the cost added up by the pass.</summary>
	public int Cost;
	/// <summary>The park's money, or null while the free-building word 0x785A2C is set.</summary>
	public int? Bank;
	/// <summary>0x818C14: the overwrite priority of the last class-3 owner a tile cell met.</summary>
	public int CellPriority;
}

/// <summary>What the tile chooser reads of the tool and the line besides the tile.</summary>
/// <param name="Mode">The tool mode (0xB kart line, 0x10 water line, 0xE and 0x13 the edit tools, 0xF the cut, 0x19 the fence).</param>
/// <param name="Second">The second argument of the chooser: the ghost's second call of a refused tile, which adds no latch.</param>
/// <param name="Direction">The step's direction byte (1, 4, 0x10 or 0x40; 0 for none).</param>
/// <param name="First">The line's first tile (0x820AC8).</param>
/// <param name="Last">The line's last tile (0x820AD0).</param>
/// <param name="HoverIsStart">The snapped end is the line's start (0x818688).</param>
/// <param name="ToolOwner">The owner word of the ride under the tool (0x818698).</param>
/// <param name="ToolPriority">The tool's overwrite priority (0x818C10).</param>
/// <param name="Price">The price of one tile of the tool (0x81AEBC kart, 0x81B43C water).</param>
/// <param name="Origin">The tile's origin cell.</param>
/// <param name="Gates">The fence gates' posts (0x7639F0..0x7639FC) for the mode 0x19.</param>
internal readonly record struct TileTool( int Mode, bool Second, byte Direction, bool First, bool Last, bool HoverIsStart, ushort ToolOwner, int ToolPriority, int Price, (int X, int Y) Origin,
	(int X, int Y) GateA, (int X, int Y) GateB )
{
	/// <summary>0x8186D4: the placement is being paid for. A pass over a line has it clear.</summary>
	public bool PlacementPaid { get; init; }
	/// <summary>0x8186D0: never set by anything in the executable but cleared by the queue tool's end.</summary>
	public bool CornerGate { get; init; }
}

/// <summary>
/// The track tile chooser <c>0x539760</c>: the validator of a 2 x 2 tile for the kart and water line tools, their edit tools and the fence (Buy Land) tool, the twin of the cell validator <c>0x535670</c> for the
/// tiles of the second layer. Answers 0 (the tile may be laid), 1 (refused), 9 (the edit tool's pick, <c>m_break</c>), 0xB (the line meets a tile of its own kind, <c>m_end</c>) and 0xD (the cut's tile, <c>m_erase</c>).
/// </summary>
// [BIN:TPW-EXE:0x00539760 tile chooser] ported from the executable's own answers: tools/ppc-analysis/oracle_x86/ghost_tile_golden.py runs the real routine on random tiles (cells, records, line state, latches, money) and TrackTileChooserTests replays every case; the routine reads no terrain
internal static class TrackTileChooser
{
	private static byte Opposite( byte direction ) => BlueprintGhosts.Opposite( direction );

	private static int CardinalCount( byte links ) => ((links & 1) != 0 ? 1 : 0) + ((links & 4) != 0 ? 1 : 0) + ((links & 0x10) != 0 ? 1 : 0) + ((links & 0x40) != 0 ? 1 : 0);

	private static bool Straight( byte links ) => CardinalCount( links ) == 2 && ((links & 1) != 0 && (links & 0x10) != 0 || (links & 4) != 0 && (links & 0x40) != 0);

	private static bool Corner( byte links ) => CardinalCount( links ) == 2 && ((links & 1) != 0 && (links & 4) != 0 || (links & 1) != 0 && (links & 0x40) != 0 || (links & 0x10) != 0 && (links & 4) != 0 || (links & 0x10) != 0 && (links & 0x40) != 0);

	private static int RefusePending( bool second, ref TileLatches latches )
	{
		if ( !second )
			latches.Pending = true;
		return 1;
	}

	private static int RefuseSticky( bool second, ref TileLatches latches )
	{
		if ( !second )
			latches.Refuse = true;
		return 1;
	}

	private static bool IsObjectCell( TileCellFacts cell ) => cell.Type is 4 or 7 or 0x1E or 10 or 9 or 0x15;

	private static bool Touches( TileCellFacts cell ) => cell.Type is 3 or 9 || (cell.Flags & 0x20) != 0;

	/// <summary>The answer for one tile; <paramref name="latches"/> carry the pass's state in and out.</summary>
	public static int Choose( in TileTool tool, in TileCellFacts a, in TileCellFacts b, in TileCellFacts c, in TileCellFacts d, in TileRecordFacts record,
		in TileNeighbourFacts right, in TileNeighbourFacts diagonal, in TileNeighbourFacts down, ref TileLatches latches )
	{
		var mode = tool.Mode;
		var second = tool.Second;
		if ( mode == 0x39 )
			return latches.Refuse ? 1 : 0;
		var occupied = IsObjectCell( a ) || IsObjectCell( b ) || IsObjectCell( c ) || IsObjectCell( d );
		// [BIN:TPW-EXE:0x0053991B unowned] any cell of land the park does not own refuses the tile at once, with no latch
		if ( ((a.Flags | b.Flags | c.Flags | d.Flags) & 0x40) != 0 )
			return 1;
		// [BIN:TPW-EXE:0x00539961 overwritable object] the four priority tests run on the cells A, B, C and C again (the executable's copy repeats the third); an object cell whose owner's priority is below the tool's lets the whole tile through
		for ( var index = 0; index < 4; index++ )
		{
			var cell = index == 0 ? a : index == 1 ? b : c;
			if ( cell.Type is not (4 or 9 or 10) )
				continue;
			if ( cell.OwnerPriority is { } found )
				latches.CellPriority = found;
			if ( tool.ToolPriority > latches.CellPriority )
				return 0;
		}
		// [BIN:TPW-EXE:0x00539C27 track modes] the three other records of the tile: a track tile or post of another ride makes the tile occupied and refuses everything after it
		if ( mode is 0xB or 0x10 )
		{
			for ( var index = 0; index < 3; index++ )
			{
				var neighbour = index == 0 ? right : index == 1 ? diagonal : down;
				if ( neighbour.Present && neighbour.Type is 0xB or 0xD or 0x10 or 0x12 or 0x19 && neighbour.Owner != tool.ToolOwner )
				{
					occupied = true;
					latches.Refuse = true;
				}
			}
		}
		if ( latches.Pending )
			latches.Refuse = true;
		if ( !second && latches.Refuse )
			return 1;
		var type = record.Type;
		// [BIN:TPW-EXE:0x00539DF4 fence tool] the fence tool (0x19) on a post's record answers by the gate posts' cells; on any other record 0 or 7 is free and anything else refuses and sets the sticky word
		if ( type == 0x19 )
		{
			if ( mode == 0x19 )
			{
				if ( tool.Origin == tool.GateA )
					return 1;
				if ( tool.Origin != tool.GateB )
					return 0;
				return 1;
			}
		}
		else if ( mode == 0x19 )
		{
			if ( type is 0 or 7 )
				return 0;
			latches.Refuse = true;
			return 1;
		}
		if ( mode == type && record.Owner == tool.ToolOwner )
			latches.L54 = true;
		// [BIN:TPW-EXE:0x00539EC9 middle of a line] a tile of the tool's own kind in the middle of the line must be a straight or a stub
		if ( mode == type && !tool.First && !tool.Last && !Straight( record.Links ) && System.Numerics.BitOperations.PopCount( record.Links ) != 1 )
			return RefusePending( second, ref latches );
		// [BIN:TPW-EXE:0x00539F4F cost] a tile whose record is a post, no terrain, unowned, none or of the tool's own type adds the tool's price unless it is the tool's own type
		if ( (type is 0x19 or 7 || (record.Flags & 0x40) != 0 || mode == 0 || type == 0 || mode == type) && type != mode && !tool.PlacementPaid )
			latches.Cost = unchecked( latches.Cost + tool.Price );
		// [BIN:TPW-EXE:0x00539F95 money] outside the edit tools (0x539F72 exempts the modes 0xE and 0x13) a cost above the park's money refuses the tile and sets the cash word 0x816D5C (0x539FB6)
		if ( latches.Bank is { } bank && mode is not (0xE or 0x13) && latches.Cost != 0 && latches.Cost > bank )
		{
			if ( !second )
			{
				latches.Refuse = true;
				latches.Refused = true;
			}
			latches.Cash = true;
			return 1;
		}
		// [BIN:TPW-EXE:0x00539FC5 last tile] the last tile of a line of a track tool that touches a queue or entrance cell or a NoModify cell is refused
		if ( tool.Last && type is not (0xB or 0x10) && mode is 0xB or 0x10 )
		{
			if ( Touches( a ) || Touches( b ) || Touches( c ) || Touches( d ) )
				return RefusePending( second, ref latches );
		}
		// [BIN:TPW-EXE:0x0053A070 free tile] a tile with no record and no object, outside the edit tools, is free
		if ( type == 0 && mode is not (0xE or 0x13) && !occupied )
		{
			if ( !second )
				latches.L54 = false;
			return 0;
		}
		var direction = tool.Direction;
		// [BIN:TPW-EXE:0x0053A0A1 own tile] a tile of the tool's own kind (or its station) where the line starts on its own start or ends
		if ( mode == type || mode == 0xB && type == 0xD || mode == 0x10 && type == 0x12 )
		{
			if ( tool.First && tool.HoverIsStart || tool.Last )
			{
				if ( type is 0xB or 0x10 )
				{
					if ( direction != record.Direction && System.Numerics.BitOperations.PopCount( record.Links ) == 2 )
						return RefusePending( second, ref latches );
					if ( Corner( record.Links ) )
						return RefusePending( second, ref latches );
				}
				if ( type is 0xD or 0x12 && direction != record.Direction )
					return RefusePending( second, ref latches );
				if ( (record.Flags & 0xF) != 0 && direction != 0 && direction != record.Direction && (tool.Last || tool.First) )
					return RefusePending( second, ref latches );
				// [BIN:TPW-EXE:0x0053A1C6 end] the line ends against the tile's opposite direction, or on a tile of another ride, or on an add-on tile in its own direction: the answer 0xB sets the end word
				if ( direction != Opposite( record.Direction ) && record.Owner == tool.ToolOwner && (record.Flags & 0x10) != 0 && direction != record.Direction )
					return RefusePending( second, ref latches );
				latches.MeetsOwn = true;
				return 0xB;
			}
			if ( (record.Flags & 0xF) != 0 && direction != 0 && direction != record.Direction && tool.First )
				return RefusePending( second, ref latches );
		}
		if ( type == 0xB && mode == 0xB || type == 0x10 && mode == 0x10 )
		{
			if ( direction != Opposite( record.Direction ) && record.Owner == tool.ToolOwner )
			{
				if ( (record.Flags & 0x10) != 0 && direction != record.Direction )
					return RefusePending( second, ref latches );
				if ( (record.Flags & 0xF) == 0 || direction == record.Direction )
					return 0;
				return RefusePending( second, ref latches );
			}
		}
		if ( type == 0x12 && mode == 0x10 && direction == record.Direction )
		{
			if ( second )
				return 0;
			latches.Pending = true;
			return 0;
		}
		if ( type == 0xD && mode == 0xB && direction == record.Direction )
		{
			if ( second )
				return 0;
			latches.Pending = true;
			return 0;
		}
		if ( occupied && (record.Flags & 0x10) == 0 )
			return RefusePending( second, ref latches );
		if ( mode == 0xF )
			return type is 0xB or 0x10 && record.Owner == tool.ToolOwner ? 0xD : type == 0x19 ? 0xD : 1;
		if ( type is 0xB or 0x10 && mode is 0xE or 0x13 && record.Owner == tool.ToolOwner && !(CardinalCount( record.Links ) > 2 && record.Links != 0) )
			return 9;
		if ( Corner( record.Links ) && !tool.CornerGate )
			return RefuseSticky( second, ref latches );
		if ( (record.Flags & 0xF) != 0 )
		{
			if ( tool.First )
			{
				if ( direction != record.Direction && direction != Opposite( record.Direction ) )
					return RefuseSticky( second, ref latches );
			}
			else
				return RefuseSticky( second, ref latches );
		}
		if ( System.Numerics.BitOperations.PopCount( record.Links ) == 2 && direction != record.Direction && direction != Opposite( record.Direction ) )
			latches.L54 = false;
		if ( mode == type && record.Owner == tool.ToolOwner && System.Numerics.BitOperations.PopCount( record.Links ) == 3 )
			return 0;
		return 1;
	}
}

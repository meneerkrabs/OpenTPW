namespace OpenTPW;

/// <summary>The sticky latches of the path and queue cell validator (tp.exe <c>0x816D50</c>, <c>0x816D58</c>, <c>0x816D60</c> and <c>0x816D4C</c>): they live for one drag of the tool.</summary>
internal struct CellValidatorLatches
{
	/// <summary>0x816D50: refuses every later cell of the drag.</summary>
	public bool Refuse;
	/// <summary>0x816D58: set by a refused cell, promoted to <see cref="Refuse"/> at the next cell.</summary>
	public bool Pending;
	/// <summary>0x816D60: the line ended on a path (mode 3) or a queue cell (mode 1).</summary>
	public bool EndsOnPath;
	/// <summary>0x816D4C: the line meets an existing cell of its own kind.</summary>
	public bool MeetsOwn;
	/// <summary>0x818C14: the overwrite priority of the last class-3 owner an object cell met (0 at the start of a pass); a cell whose owner is no class-3 thing leaves it as it was.</summary>
	public int OwnerPriority;
	/// <summary>0x81AF4C: the cost the pass has added up (each chargeable cell adds its price, 0x52FBA0).</summary>
	public int Cost;
	/// <summary>The park's money the added cost is tested against (0x6AD810 behind 0x5195D0), or null while the free-building word 0x785A2C is set or no money is kept: the test then never refuses.</summary>
	public int? Bank;
	/// <summary>0x816D5C: the cash pointer's flag, set by the cell that takes the cost over the money.</summary>
	public bool Cash;
}

/// <summary>What the validator reads of one cell (the cell record, its structure record and the line state).</summary>
/// <param name="Mode">The tool's cell type: 1 for the path tool, 3 for the queue tool.</param>
/// <param name="Type">The cell's map type +8.</param>
/// <param name="Flags">The cell's flag word +0xE.</param>
/// <param name="StructureType">The type of the cell's structure record, with an extension (0xC, 0x11) replaced by its parent's.</param>
/// <param name="StructureLinks">The link byte of that structure record (+0xC), which tells a corner or a junction of a track tile.</param>
/// <param name="Links">The cell's link byte +0xC.</param>
/// <param name="OwnedByTool">The cell's owner word +0x10 is the origin cell of the ride under the tool (0x818698).</param>
/// <param name="LastCell">The cell is the last one of the line (0x820AB8).</param>
/// <param name="InsideLine">The line has started and its ride is known (0x820AA4 and 0x818688).</param>
/// <param name="NeighbourType">For a queue cell with one link under the path tool, the type of the cell that link points to.</param>
/// <param name="OwnerPriority">The overwrite priority of the class-3 thing that owns the cell (the type record's +0x54, which the validator stores in 0x818C14), for the types 4, 9 and 10; null when no class-3 thing owns it, which leaves 0x818C14 as the pass had it (<see cref="CellValidatorLatches.OwnerPriority"/>).</param>
/// <param name="ToolPriority">The tool's overwrite priority (0x818C10): 2 for the path tool, 3 for the queue tool.</param>
internal readonly record struct CellValidatorInput( int Mode, byte Type, ushort Flags, byte StructureType, byte StructureLinks, byte Links, bool OwnedByTool, bool LastCell, bool InsideLine,
	byte NeighbourType, int? OwnerPriority, int ToolPriority )
{
	/// <summary>The price a chargeable cell adds to the pass's cost: the path cost 0x81B6DC for the path tool (0x52FB20), the queue cost 0x81EA14 for the queue tool (0x52FB40); 0 for the others and where no money is kept.</summary>
	public int Price { get; init; }

	/// <summary>
	/// The second argument of the validator: 0 (a cell that finds the refusal latch set is refused at once) or not (the ghost's second call of a refused cell, which runs the whole decision for its latch writes
	/// and whose answer it ignores).
	/// </summary>
	// [BIN:TPW-EXE:0x0053589C second argument] with the argument 0 and the refusal latch 0x816D50 set the validator returns 1 at 0x5358A9; any other argument goes on to the cell's own tests
	public bool Full { get; init; }

	/// <summary>
	/// The placement is being paid for (0x8186D4): the chargeable-cell test skips the cost add while it is set. The path and queue line hover runs with it clear, so the line tools never set it.
	/// </summary>
	// [BIN:TPW-EXE:0x005358DF placement paid] the cost block reads the byte 0x8186D4 and skips the 0x52FBA0 add when it is nonzero. All seven writers (0x5235D4, 0x523967, 0x5239FC, 0x5249C2, 0x524B4A, 0x5253E9, 0x525A06) are in the hover 0x5234D0 and the click handler 0x524960. A path walk from each setter (0x5235D4 and 0x523967 in the hover, 0x524B4A and 0x5253E9 in the click handler), following every direct jump and the hover's switch at 0x5237D0, reaches only that function's single return (0x524739 in the hover, 0x525A1B in the click handler) and only after a clear (0x5239FC, 0x525A06); the hover is also called at 0x5249B6 with the clear at 0x5249C2. So the byte is 0 whenever code outside those two routines runs, and a path or queue line hover reads 0 (exceptions that unwind past a return are not modelled)
	public bool PlacementPaid { get; init; }
}

/// <summary>
/// The decision order of tp.exe's cell validator <c>0x00535670</c> for the path and queue tools without the park's money (the money test only adds a refusal when the pending cost exceeds the bank). The answers:
/// 1 refuses the cell, 0 accepts it (empty or replaceable), 0xB accepts it where the line meets an existing cell of its own kind, 8 accepts it where the line ends on a path (queue) or a queue (path).
/// </summary>
// [BIN:TPW-EXE:0x00535670 cell validator] ported from the executable's own answers: tools/ppc-analysis/oracle_x86/validator_golden.py runs the real routine on every combination of mode, cell type, flags, structure record, links, owner and latch state and QueueCellValidatorTests replays all 14,256 of them; the routine reads no MAP terrain bit (the terrain answer is the cell's type alone, which the map init 0x536490 wrote), and it tests the flag 0x40 first
internal static class QueueCellValidator
{
	private const int Refused = 1;

	/// <summary>The answer for one cell; <paramref name="latches"/> carry the drag's state in and out (the first cell of a drag starts with none set).</summary>
	public static int Check( in CellValidatorInput cell, ref CellValidatorLatches latches )
	{
		var mode = cell.Mode;
		var type = cell.Type;
		// [BIN:TPW-EXE:0x0053570E owner priority] before anything else a type 4, 9 or 10 cell looks up the thing on its owner word's cell (0x4D99E0, 0x7CFB90): a thing of class 3 (byte +2) stores its type record's +0x54 in 0x818C14 (0x535788); any other owner leaves 0x818C14 as it was, which the hover resets to 0 (0x523522) and the passes carry from cell to cell
		if (type is 4 or 9 or 10 && cell.OwnerPriority is { } found)
			latches.OwnerPriority = found;
		// the cell is not the player's land
		if ((cell.Flags & 0x40) != 0)
			return Refused;
		// an object's cell, the entrance and the exit: acceptable only when the owner's priority is below the tool's, for the first cell that is refused or meets no refusal before
		if (type is 4 or 9 or 10)
		{
			if (latches.OwnerPriority < cell.ToolPriority && !latches.Pending && !latches.Refuse)
				return 0;
			latches.Refuse = true;
			return Refused;
		}
		if (latches.Pending)
			latches.Refuse = true;
		if (latches.Refuse && !cell.Full)
			return Refused;
		// a fence post, or an extension of one
		if (cell.StructureType == 0x19)
		{
			latches.Refuse = true;
			return Refused;
		}
		// [BIN:TPW-EXE:0x005358CC cost] a chargeable cell (0x535600) whose type is not the tool's and that is no path under the queue tool adds the tool's price to the pass's cost (0x52FBA0); a cost above the park's money refuses the cell and every later one and sets the cash flag (0x535905..0x535945)
		if (cell.Price != 0 && !cell.PlacementPaid && Chargeable( mode, type, cell.LastCell ) && type != mode && !(mode == 3 && type == 1))
			latches.Cost = unchecked( latches.Cost + cell.Price );
		// [BIN:TPW-EXE:0x0053592B money refusal] the refusal also calls 0x527940(1), which stores the refusal word 0x816D48; its only reader 0x527950 is the pointer switch's no-place test (UI-100), and 0x533933 stores the same word after every refusal of the ghost cell routine, so the word is the ghost's Refused flag and needs no input of its own
		if (latches.Bank is { } bank && latches.Cost != 0 && latches.Cost > bank)
		{
			latches.Refuse = true;
			latches.Cash = true;
			return Refused;
		}
		if (mode == 3 && type == 3)
		{
			if (System.Numerics.BitOperations.PopCount( cell.Links ) == 2 || !cell.OwnedByTool)
			{
				latches.Refuse = true;
				return Refused;
			}
			if (!cell.LastCell)
				return 0;
			latches.MeetsOwn = true;
			return 0xB;
		}
		if (type == mode && ((cell.InsideLine) || cell.LastCell))
		{
			latches.MeetsOwn = true;
			return 0xB;
		}
		if (type == 0x15)
		{
			latches.Pending = true;
			return Refused;
		}
		if (IsCornerOrJunction( cell.StructureLinks ))
		{
			latches.Pending = true;
			return Refused;
		}
		if (mode == 1 && type == 1)
			return 0;
		if (type == 0)
			return 0;
		if (mode == 3)
		{
			if (type == 1)
			{
				if (cell.LastCell && !latches.Refuse)
				{
					latches.EndsOnPath = true;
					latches.Pending = true;
					return 8;
				}
				if ((cell.Flags & 0x20) == 0)
					return 0;
				latches.Pending = true;
				return Refused;
			}
			if (type == 4)
			{
				latches.Pending = true;
				return Refused;
			}
			return Refused;
		}
		// the path tool
		if (type is 4 or 9)
		{
			latches.Pending = true;
			return Refused;
		}
		if (type == 3)
		{
			if (System.Numerics.BitOperations.PopCount( cell.Links ) == 1 && cell.NeighbourType == 3)
			{
				latches.EndsOnPath = true;
				return 8;
			}
			latches.Refuse = true;
			return Refused;
		}
		return Refused;
	}

	/// <summary>The cell predicate <c>0x535600</c> behind the cost: whether a cell of this type costs the tool's price when the tool lays over it.</summary>
	// [BIN:TPW-EXE:0x00535600 chargeable cell] 1 for tool 0, an equal type, an empty cell, a queue cell under the path tool, a path cell under the placement and (not on the line's last cell) a path cell under the queue tool; 0 for an object cell under the placement and for every other pair
	internal static bool Chargeable( int mode, byte type, bool last )
	{
		if (mode == 0)
			return true;
		if (mode == 4 && type == 4)
			return false;
		if (mode == 0x15 && type == 0x15)
			return true;
		if (mode == 1 && type == 3)
			return true;
		if (mode == type || type == 0)
			return true;
		if (mode == 4 && type == 1)
			return true;
		return mode == 3 && type == 1 && !last;
	}

	/// <summary>Whether a track tile's links make a corner (two cardinal links at a right angle, 0x53AE00) or a junction (three or more, 0x53AE90).</summary>
	public static bool IsCornerOrJunction( byte structureLinks )
	{
		var cardinal = structureLinks & 0x55;
		var count = System.Numerics.BitOperations.PopCount( (uint)cardinal );
		if (count > 2)
			return true;
		return count == 2 && (((cardinal & 0x01) != 0 && (cardinal & 0x44) != 0) || ((cardinal & 0x10) != 0 && (cardinal & 0x44) != 0));
	}

	/// <summary>
	/// The type of the structure record under a cell as the validator reads it: 0x19 for a fence post and for the three cells beside it (the walk types them as its extensions, which the validator follows to
	/// the parent), the track tile's type otherwise (an extension replaced by its origin's), 0 for a cell without a structure.
	/// </summary>
	// [BIN:TPW-EXE:0x00535670 structure record] the validator takes the cell's structure record, follows an extension (0xC, 0x11) to its parent through +0x10 and refuses a parent of type 0x19 whoever owns the land; it refuses the four cells of a corner or junction track tile and lets a straight piece or a stub through
	public static byte StructureTypeAt( ParkCellMap cells, int x, int y )
	{
		ArgumentNullException.ThrowIfNull( cells );
		if ( cells.Land?.CountsAsStructure( x, y, cells.Width, cells.Height ) == true )
			return FenceLayer.PostType;
		var track = cells.Tracks.Get( x, y );
		if ( track.IsEmpty )
			return 0;
		if ( track.IsExtension && track.Parent >= 0 )
		{
			var (px, py) = cells.Tracks.CellOf( track.Parent );
			return cells.Tracks.Get( px, py ).Type;
		}
		return track.Type;
	}

	/// <summary>
	/// Whether the validator refuses the cell for the tool in <paramref name="mode"/> with the latches of a fresh pass (<see cref="Refuses(ParkCellMap, int, int, int, int?, ref CellValidatorLatches, bool, bool)"/>): the per-cell routes use it
	/// as a refusal on top of their own rules and identify a terminal cell explicitly. The cell's type is its own (<see cref="ParkCellMap.PrimaryTypeAt"/>: a track tile lies beneath it, or shows its type while the cell is empty).
	/// </summary>
	/// <param name="ownerPriority">The overwrite priority of the class-3 thing that owns an object cell (types 4, 9 and 10), or null when there is none.</param>
	/// <param name="lastCell">The last cell of the line: a queue may end on a path before its NoModify flag is tested.</param>
	public static bool Refuses( ParkCellMap cells, int mode, int x, int y, int? ownerPriority, bool lastCell = false )
	{
		var latches = new CellValidatorLatches();
		return Refuses( cells, mode, x, y, ownerPriority, ref latches, lastCell );
	}

	/// <summary>
	/// <see cref="Refuses(ParkCellMap, int, int, int, int?, bool)"/> in the middle of a pass: the latches and the owner priority 0x818C14 carry from cell to cell of one pass over a line, as the original keeps them
	/// until the next reset (the hover 0x5234D0 at 0x5234F2, 0x5234F8 and 0x523522, the commit's pass set-up 0x527C30 at 0x527C52 and 0x527C5C for the two refusal latches, the tool's mode change 0x52F11B for all of them).
	/// </summary>
	/// <param name="insideLine">The line has started and its ride is known (0x820AA4 and 0x818688).</param>
	// [BIN:TPW-EXE:0x005234F2 latch reset] the hover handler clears the drag state 0x816D48, 0x816D50 (refuse), 0x816D58 (pending), 0x816D54, 0x816D60 (ends on a path), 0x816D5C and 0x816D4C (meets its own kind) and then the tool priority 0x818C10 and the owner priority 0x818C14 (0x523522); the commit's pass set-up 0x527C30 clears 0x816D48, 0x816D50 and 0x816D58 alone before its LayLine, and the mode change 0x52F11B clears 0x816D50, 0x816D58, 0x816D60, 0x816D5C and 0x816D4C: no cell of a drag resets them, so every pass over a line starts clean and a refusal marks the rest of that pass
	public static bool Refuses( ParkCellMap cells, int mode, int x, int y, int? ownerPriority, ref CellValidatorLatches latches, bool lastCell = false, bool insideLine = false )
	{
		ArgumentNullException.ThrowIfNull( cells );
		if ( !cells.InBounds( x, y ) )
			return true;
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
		var toolPriority = mode == 3 ? 3 : 2;
		var input = new CellValidatorInput( mode, type, (ushort)cells.FlagsAt( x, y ), StructureTypeAt( cells, x, y ), origin.IsEmpty ? (byte)0 : origin.Mask, links, false, lastCell, insideLine, neighbour,
			ownerPriority, toolPriority );
		return Check( input, ref latches ) == 1;
	}
}

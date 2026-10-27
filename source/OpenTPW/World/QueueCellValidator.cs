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
/// <param name="OwnerPriority">The overwrite priority of the object the cell belongs to (0x818C14), for the types 4, 9 and 10.</param>
/// <param name="ToolPriority">The tool's overwrite priority (0x818C10): 2 for the path tool, 3 for the queue tool.</param>
internal readonly record struct CellValidatorInput( int Mode, byte Type, ushort Flags, byte StructureType, byte StructureLinks, byte Links, bool OwnedByTool, bool LastCell, bool InsideLine,
	byte NeighbourType, int OwnerPriority, int ToolPriority );

/// <summary>
/// The decision order of tp.exe's cell validator <c>0x00535670</c> for the path and queue tools without the park's money (the money test only adds a refusal when the pending cost exceeds the bank). The answers:
/// 1 refuses the cell, 0 accepts it (empty or replaceable), 0xB accepts it where the line meets an existing cell of its own kind, 8 accepts it where the line ends on a path (queue) or a queue (path).
/// </summary>
// [BIN:TPW-EXE:0x00535670 cell validator] ported from the executable's own answers: tools/ppc-analysis/oracle_x86/validator_golden.py runs the real routine on every combination of mode, cell type, flags, structure record, links, owner and latch state and QueueCellValidatorTests replays a sample of them; the routine reads no MAP terrain bit (the terrain answer is the cell's type alone, which the map init 0x536490 wrote), and it tests the flag 0x40 first
internal static class QueueCellValidator
{
	private const int Refused = 1;

	/// <summary>The answer for one cell; <paramref name="latches"/> carry the drag's state in and out (the first cell of a drag starts with none set).</summary>
	public static int Check( in CellValidatorInput cell, ref CellValidatorLatches latches )
	{
		var mode = cell.Mode;
		var type = cell.Type;
		// the cell is not the player's land
		if ((cell.Flags & 0x40) != 0)
			return Refused;
		// an object's cell, the entrance and the exit: acceptable only when the owner's priority is below the tool's, for the first cell that is refused or meets no refusal before
		if (type is 4 or 9 or 10)
		{
			if (cell.OwnerPriority < cell.ToolPriority && !latches.Pending && !latches.Refuse)
				return 0;
			latches.Refuse = true;
			return Refused;
		}
		if (latches.Pending)
			latches.Refuse = true;
		if (latches.Refuse)
			return Refused;
		// a fence post, or an extension of one
		if (cell.StructureType == 0x19)
		{
			latches.Refuse = true;
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
}

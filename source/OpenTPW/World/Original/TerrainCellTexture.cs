namespace OpenTPW;

/// <summary>
/// How the terrain renderer lays a ground texture on a heightfield cell: the cell word's mirror and turn bits permute which
/// of the four texture corners each cell corner takes.
/// </summary>
internal static class TerrainCellTexture
{
	/// <summary>Cell-word bit 0x40: mirrors the texture (swaps the corners next to the first one) before any turn.</summary>
	public const uint MirrorBit = 0x40;
	/// <summary>Cell-word bits 0x8, 0x10 and 0x20: the texture turned by one, two or three corner steps (the first set one wins).</summary>
	public const uint TurnOneBit = 0x8, TurnTwoBit = 0x10, TurnThreeBit = 0x20;

	/// <summary>The cell's corners in the renderer's order: (x, z), (x, z + 1), (x + 1, z + 1), (x + 1, z).</summary>
	public const int Cell00 = 0, Cell01 = 1, Cell11 = 2, Cell10 = 3;

	// [BIN:TPW-EXE:0x56F4F0 texture turn] the byte array 0,1,2,3 (set at 0x56FA0B) is permuted from the cell word: bit 0x40 swaps entries 1 and 3; then bit 0x8 makes it (3,0,1,2), else bit 0x10 (2,3,0,1), else bit 0x20 (1,2,3,0); the cell word's other bits do not matter
	// [BIN:TPW-EXE:0x56F670 cell corners] each of the cell's four vertices takes the texture corner its array entry names, in both of the renderer's two vertex orders (cell bit 0x2 set or clear): the corner (x, z) the entry at 0, (x, z + 1) at 1, (x + 1, z + 1) at 2 and (x + 1, z) at 3; the texture corners are (0,0), (0,1), (1,1) and (1,0) from the stack table at 0x56F6CF..0x56F6EF (u, v)
	/// <summary>The texture corner (0..3, going around the quad from (0, 0)) that a cell corner takes.</summary>
	public static int Corner( int cellCorner, uint cellWord )
	{
		var entries = new[] { 0, 1, 2, 3 };
		if ( (cellWord & MirrorBit) != 0 )
			(entries[1], entries[3]) = (entries[3], entries[1]);
		if ( (cellWord & TurnOneBit) != 0 )
			entries = new[] { entries[3], entries[0], entries[1], entries[2] };
		else if ( (cellWord & TurnTwoBit) != 0 )
			entries = new[] { entries[2], entries[3], entries[0], entries[1] };
		else if ( (cellWord & TurnThreeBit) != 0 )
			entries = new[] { entries[1], entries[2], entries[3], entries[0] };
		return entries[cellCorner];
	}

	/// <summary>The texture coordinate (0..1) of a cell corner for a cell word; the identity word gives u along x and v along z.</summary>
	public static (float U, float V) Coordinate( int cellCorner, uint cellWord ) => Corner( cellCorner, cellWord ) switch
	{
		0 => (0f, 0f),
		1 => (0f, 1f),
		2 => (1f, 1f),
		_ => (1f, 0f),
	};
}

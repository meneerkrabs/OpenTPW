namespace OpenTPW;

/// <summary>
/// The landscape word a path cell takes when the cell refresh writes its tile (docs/PATHS.md, "Path textures"): the original does not draw a path as a layer over the ground, it
/// gives the terrain cell the path texture's slot, the turn bits of the tile's angle and the art flag, and the terrain draws that cell like any other (static slot texture, turned
/// by the word) or, with the procedural terrain textures on, composites the path texture over the cell's own procedural texture (<see cref="ProceduralTerrainTexturer.Compose"/>).
/// </summary>
/// <remarks>Presentation only: nothing in the simulation, its hash or a save reads these words.</remarks>
internal static class PathCellWord
{
	/// <summary>Bit 0x80 (the cell has art: the texture of its slot is composited over the procedural texture) and bit 0x100 (the cell changed and its texture is built again).</summary>
	public const uint ArtBits = 0x180;

	/// <summary>The turn and mirror bits 0x08, 0x10, 0x20 and 0x40 that a tile write clears before it sets the bit of its angle.</summary>
	public const uint TurnMask = 0x78;

	/// <summary>
	/// The turn bit of a tile's angle (the clockwise degrees of the chooser's table and the save): 90 is 0x20, 180 is 0x10 and 270 is 0x08; 0 and every other value add none.
	/// </summary>
	// [BIN:TPW-EXE:0x0046E0E2 tile angle bits] the writer compares the angle argument with 0x5A, 0xB4 and 0x10E and ORs 0x20, 0x10 and 0x08 into the low byte of the word; any other angle leaves the turn bits clear
	public static uint OrientationBits( int rotation ) => rotation switch { 90 => 0x20u, 180 => 0x10u, 270 => 0x08u, _ => 0u };

	/// <summary>
	/// The word of a cell that took the path tile of <paramref name="slot"/> turned by <paramref name="rotation"/> over <paramref name="oldWord"/>: the slot in the high half, and in the low half the
	/// old bits less the turn and mirror bits 0x78 and the hole bit 0x01, plus 0x180 and the angle's turn bit.
	/// </summary>
	// [BIN:TPW-EXE:0x0046DF50 tile word] the generic branch of the writer (group != 0, not the hole code 8 and the restore code 0x37): the slot of the tile's table row (its short at +8) goes to byte 2 and 3 of the word, then the low half is ANDed with 0xFF87 and 0xFFFE and ORed with 0x180, and the angle sets one turn bit; the other bits of the old word (0x02, 0x04, 0x200 and 0x400) stay
	public static uint Word( uint oldWord, int slot, int rotation )
	{
		var low = (oldWord & 0xFFFF & 0xFF87 & 0xFFFE) | ArtBits | OrientationBits( rotation );
		return ((uint)slot << 16) | low;
	}

	/// <summary>Whether the word names art to composite (bit 0x80).</summary>
	public static bool HasArt( uint word ) => (word & 0x80) != 0;
}

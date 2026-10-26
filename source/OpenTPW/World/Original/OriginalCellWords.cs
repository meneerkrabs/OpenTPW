namespace OpenTPW;

/// <summary>
/// The words of the original's landscape cells that placed objects write while the procedural terrain textures are on (docs/OBJECTS.md, "Floors"): a skirt cell of a placed object takes
/// <c>(word &amp; 6) | orientation | 0x281</c> with the skirt entry's slot in the high half (tp.exe <c>0x46336C</c> to <c>0x463580</c>), and the removal of the object restores the cell's backup word
/// (<c>0x46DF50</c> mode 0x37). The heightfield stays as the file has it (the backup, <c>[0x79FC54]</c>); this keeps only the cells an object changed, so a level that is left drops them with it.
/// </summary>
/// <remarks>
/// Presentation only: nothing in the simulation, its hash or a save reads these words. The terrain's holes come from the cell map (<see cref="ObjectCellHoles"/>), which holds for every footprint
/// cell; the words carry what the composite texture of a skirt cell needs (its orientation and slot) and what a neighbour's cell inside another footprint box does to that ride's composites.
/// </remarks>
internal sealed class OriginalCellWords
{
	/// <summary>The bits the skirt word always has: the hole bit 0x01, the "has art" bit 0x80 and the "skirt" bit 0x200.</summary>
	public const uint SkirtBits = 0x281;

	private readonly ModelHeightfield field;
	private readonly Dictionary<int, uint> written = new();

	public OriginalCellWords( ModelHeightfield field ) => this.field = field;

	/// <summary>The cells an object's words currently change.</summary>
	public int WrittenCells => written.Count;

	private int Index( int x, int z ) => z * field.CellCountX + x;

	private bool Inside( int x, int z ) => (uint)x < (uint)field.CellCountX && (uint)z < (uint)field.CellCountZ;

	/// <summary>The backup word of a cell (the file's); 0 outside the grid.</summary>
	public uint Backup( int x, int z ) => Inside( x, z ) ? field.GetCellWord( x, z ) : 0;

	/// <summary>The cell's word now: what an object wrote, else the backup. A cell outside the grid has no word (the original has no bounds test; placement never reaches outside).</summary>
	public uint Word( int x, int z ) => Inside( x, z ) ? (written.TryGetValue( Index( x, z ), out var word ) ? word : field.GetCellWord( x, z )) : 0;

	/// <summary>Whether an object's word holds the cell.</summary>
	public bool IsWritten( int x, int z ) => Inside( x, z ) && written.ContainsKey( Index( x, z ) );

	/// <summary>Writes a cell's word (the skirt word: <see cref="SkirtWord"/>).</summary>
	public void Write( int x, int z, uint word )
	{
		if ( !Inside( x, z ) )
			return;
		written[Index( x, z )] = word;
	}

	/// <summary>
	/// The word a cell takes when its map type is a hole type and the cell refresh runs (<c>0x46DF50</c> mode 8): bits 0x101 set and 0x80 cleared. A skirt cell then no longer has the "has art" bit
	/// that the composite scan <c>0x57ACE0</c> asks for, so an object placed after the refresh does not compose it.
	/// </summary>
	public static uint HoleWord( uint word ) => (word | 0x101u) & ~0x80u;

	/// <summary>Applies <see cref="HoleWord"/> to a cell (the refresh of a footprint cell).</summary>
	// [BIN:TPW-EXE:0x0046DF50 hole word] mode 8: |= 0x101 and &= 0xFF7F of the landscape word
	public void MarkHole( int x, int z ) => Write( x, z, HoleWord( Word( x, z ) ) );

	/// <summary>
	/// The cell's backup word again, as the removal of its occupant leaves it (<c>0x46DF50</c> mode 0x37 copies the backup word and sets the dirty bit 0x100, which the procedural terrain turns into
	/// a rebuild of the cell's texture, <see cref="ProceduralTerrain"/>; with the texturing on it also clears the turn and mirror bits, which nothing here reads: the procedural terrain ignores them).
	/// </summary>
	// [BIN:TPW-EXE:0x0046DF50 word restore] mode 0x37 (the cleared cell, reached from 0x5365D0 for a cell of type 0 or 5): the high and low halves are copied from the backup landscape [0x79FC54], byte 1 |= 1 (the dirty bit), and the turn and mirror bits (0xFF87 mask) are cleared when [0x7A1A8C] has 0x4000
	public bool Restore( int x, int z )
	{
		if ( !Inside( x, z ) || !written.Remove( Index( x, z ) ) )
			return false;
		return true;
	}

	/// <summary>
	/// The word a skirt cell takes (<c>0x463060</c> at 0x46351A to 0x463533): the old word's bits 2 and 4, the orientation of the entry after the instance's turn, the entry's flip bit 0x40, and 0x281;
	/// the entry's slot goes in the high half.
	/// </summary>
	// [BIN:TPW-EXE:0x0046351A skirt word] word = (old & 6) | orientation | 0x281 and the entry's slot in the high half; the orientation is the entry's bits 0x38 turned by the rotation (0x46395C, 0x463994, 0x4639CC) and the flip bit 0x40 is kept
	public static uint SkirtWord( uint oldWord, ObjectSkirtEntry entry, int rotation )
		=> (uint)((oldWord & 6) | (uint)(entry.Code & 0x40) | (uint)TurnedOrientation( entry.Code & 0x38, rotation ) | SkirtBits) | ((uint)entry.Slot << 16);

	/// <summary>
	/// The orientation bits of an entry (0, 0x08, 0x10, 0x20) after the instance's turn: unchanged at 0 degrees; at 90 each step goes on (0 to 8 to 0x10 to 0x20 to 0), at 180 0 and 0x10 and 8 and 0x20
	/// swap, at 270 each step goes back. Other values (0x18, 0x28 and higher) give no bit.
	/// </summary>
	// [BIN:TPW-EXE:0x0046395C orientation tables] jump tables for 90 (0x46395C), 270 (0x463994) and 180 (0x4639CC) indexed by the bytes at 0x463970, 0x4639A8 and 0x4639E0 (0, 1, 2, 4... for 0, 8, 0x10, 0x20 and the rest)
	public static int TurnedOrientation( int orientation, int rotation )
	{
		if ( rotation == 0 )
			return orientation & 0x38;
		return rotation switch
		{
			90 => orientation switch { 0 => 0x08, 0x08 => 0x10, 0x10 => 0x20, _ => 0 },
			180 => orientation switch { 0 => 0x10, 0x08 => 0x20, 0x20 => 0x08, _ => 0 },
			270 => orientation switch { 0 => 0x20, 0x08 => 0, 0x10 => 0x08, 0x20 => 0x10, _ => 0 },
			_ => 0
		};
	}
}

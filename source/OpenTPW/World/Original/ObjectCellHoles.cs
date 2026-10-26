namespace OpenTPW;

/// <summary>
/// The terrain cells the original draws no ground on because the cell map says an object (or the cell's lack of terrain) is there (docs/OBJECTS.md, "Floors"): the cell refresh
/// (tp.exe <c>0x5365D0</c> through the chooser <c>0x535DD0</c>, which answers 8 for these cell types, and <c>0x46DF50</c> with that mode) writes the hole word <c>|= 0x101</c> and clears bit 0x80, and
/// the terrain draw (<c>0x56F090</c>, the cell list of the static and the procedural ground alike) leaves a cell with bit 0x01 out. A cell that goes back to type 0 (the occupant removed, <c>0x5367A0</c>
/// then the refresh) takes its backup word again, so the ground is drawn there from the next frame (and, with the procedural textures, rebuilt once: the restore sets the dirty bit 0x100).
/// </summary>
/// <remarks>
/// So the ground under a placed object's footprint is not drawn at all: a floor that stands on it brings its own cells (the interior) and its skirt (the edge, whose slots draw opaque), and with the
/// procedural terrain textures on the skirt's base is the composite of the floor art over the cell's procedural texture (<see cref="ObjectFloorBase"/>). Presentation only; the cell map itself, the
/// heightfield, the placement rules and the ground height read nothing of it.
/// </remarks>
internal static class ObjectCellHoles
{
	/// <summary>
	/// Whether the ground skips the cell for its map type: water (2), no terrain (7), a fixed walkway (30), an object, entrance or exit cell (4, 9, 10) and a coaster track connection (0x15).
	/// Water, no terrain and the walkway are the cells without terrain, which have no ground to skip anyway.
	/// </summary>
	// [BIN:TPW-EXE:0x00535DD0 hole cell types] the chooser answers 8 for the (absolute) cell types 2, 7, 0x1E, 4, 9, 10 and 0x15, and the refresh 0x5365D0 hands that to 0x450470 -> 0x46DF50, whose mode 8 sets bits 0x101 of the landscape word and clears 0x80; the terrain cell list 0x56F090 skips a cell whose word has bit 0x01 (also when the procedural textures are off, where it draws the static ground texture of the other cells)
	public static bool IsHoleType( byte type ) => type is 2 or 7 or 0x1E or 4 or 9 or 10 or 0x15;

	/// <summary>Whether the ground skips cell (<paramref name="x"/>, <paramref name="y"/>) of the level's cell map.</summary>
	public static bool IsHole( ParkCellMap cells, int x, int y ) => cells.InBounds( x, y ) && (IsHoleType( cells.RawTypeAt( x, y ) ) || cells.RawTypeAt( x, y ) == QueueType);

	/// <summary>The queue cell's type: its refresh writes the hole itself, after placing the queue model (<c>0x5365D0</c> class 2: <c>0x5229E0</c>, then <c>0x450470(8)</c> → <c>0x46DF50</c>).</summary>
	// [BIN:TPW-EXE:0x005365D0 queue hole] the class-2 branch places the queue model and then writes the tile (0, 8, 0) through 0x450470 → 0x46DF50, whose mode 8 ORs 0x101 into the cell word and clears 0x80: the cell is a hole of the ground (docs/PATHS.md, "Queue models on the terrain")
	public const byte QueueType = 3;
}

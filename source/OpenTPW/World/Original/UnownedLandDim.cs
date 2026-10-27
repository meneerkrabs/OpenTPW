namespace OpenTPW;

/// <summary>
/// The darkening of the land outside the park (tp.exe <c>0x450D10</c> and the terrain draw <c>0x56F670</c>): the land refresh sets bit 0x400 of a terrain cell's landscape word for a cell whose map flags
/// have 0x40 (unowned) and not 0x80 (the entrance area), and the draw halves the vertex colour of the cell's four corners (<c>shr 1, and 0x7F7F7F</c>) unless the render flags <c>[0x8BCBC8]</c> have 0x800,
/// which the LIGHTING option sets to off (<c>0x44DC28</c>). The cell's own texture, procedural or static, is drawn as before, only darker.
/// </summary>
/// <remarks>Presentation only: the cell map, the economy and every save field read the flags as before.</remarks>
internal static class UnownedLandDim
{
	/// <summary>The landscape word bit the land refresh sets.</summary>
	public const uint WordBit = 0x400;

	/// <summary>Whether a cell with the map flags <paramref name="flags"/> takes the bit: unowned and not in the entrance area.</summary>
	// [BIN:TPW-EXE:0x00450D96 land refresh] the cell's flag word (+0xE of the map record, 0x535DB0) is tested for 0x40: clear leaves the cell's bit 0x400 cleared (0x450DAC); set tests 0x80, which clears the bit too (0x450DAC), and only unowned cells outside the entrance area get 'or byte [word + 1], 4' (0x450DA6)
	public static bool Dims( ParkCellFlags flags ) => (flags & ParkCellFlags.Unowned) != 0 && (flags & ParkCellFlags.EntranceArea) == 0;

	/// <summary>
	/// Whether the render flags let the draw halve the colours: the LIGHTING option 1 clears 0x1800 and 0 sets it, so lighting on dims; any other value leaves the flags as the start left them, which
	/// has bit 0x800 clear.
	/// </summary>
	// [BIN:TPW-EXE:0x0044DC28 lighting option] [0x7858B8] equal to 0 ORs 0x1800 into [0x8BCBC8] (0x44DC36), equal to 1 ANDs 0xE7 into its second byte (0x44DC49: clears 0x1800); the draw tests bit 0x800 at 0x56FB09 and 0x56FB9C
	public static bool Enabled( int lighting ) => lighting != 0;

	/// <summary>
	/// Whether the terrain is drawn unlit: LIGHTING 0 sets bit 0x800, and with it the terrain colour table fill <c>0x574530</c> copies one constant (white, 0xFFFFFFFF) into every colour instead of summing the
	/// lights (the sun, the ambient and, here, the point lights); the same bit stops the dimming (<see cref="Enabled"/>).
	/// </summary>
	// [BIN:TPW-EXE:0x0057453A terrain colour table] 'test ah, 8' on [0x8BCBC8] (0x574534-0x57453A) picks the branch: bit set copies [light object + 0x2C] (0xFFFFFFFF from its creation, 0x574013; the other writers, 0x577120 and 0x57AB61, are debug highlights restored afterwards) to each of the colours (0x574558-0x574561), clear calls 0x5741B0 for the light sum and writes the clamped fistp colours
	public static bool Unlit( int lighting ) => lighting == 0;

	/// <summary>The unlit terrain colour: white, each channel 255 (the light object's +0x2C).</summary>
	public const uint UnlitColour = 0xFFFFFFFF;

	/// <summary>The map cell the land refresh reads for the heightfield cell (<paramref name="x"/>, <paramref name="z"/>): negative indexes become 0 and an index past the map the last cell.</summary>
	// [BIN:TPW-EXE:0x00450D3D land refresh cell] the column and the row are clamped to 0 below and to [0x7A0314] - 1 and [0x7A0374] - 1 above before the map record is addressed
	public static (int X, int Z) MapCell( int x, int z, int mapWidth, int mapHeight ) => (Math.Clamp( x, 0, Math.Max( 0, mapWidth - 1 ) ), Math.Clamp( z, 0, Math.Max( 0, mapHeight - 1 ) ));

	/// <summary>
	/// The dimmed level (0..127) of a lighting channel the way the original makes it: the colour table fills each channel with fistp( light * 255.0 ) clamped to 0..255 and the draw halves it with <c>shr 1</c>
	/// (and <c>0x7F7F7F</c>, which only drops the bits the shift carries across channels). <c>test.shader</c> does the same per channel.
	/// </summary>
	// [BIN:TPW-EXE:0x0056FB0E terrain colour] fistp uses the FPU rounding mode, round to nearest even (the project's x87 control word 0x7F), so a half level goes to the even one
	public static int Level( float light ) => Math.Clamp( (int)MathF.Round( light * 255f, MidpointRounding.ToEven ), 0, 255 ) >> 1;
}

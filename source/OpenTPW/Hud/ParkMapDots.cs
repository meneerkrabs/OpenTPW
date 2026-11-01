using Veldrid;

namespace OpenTPW.Hud;

/// <summary>
/// One item of the map window's list (tp.exe <c>0x5F4370</c>): a sprite over the surface that stands where a guest, staff member, piece of litter or camera was when the window opened. The item
/// shows while its filter holds: <see cref="Mode"/> 1 tests the layer word with <see cref="Value"/>, 2 compares the staff role the map shows, 3 the value mode.
/// </summary>
internal sealed class MapDot
{
	/// <summary>The position in map cells (a thing's cell byte + its sub-cell byte / 256; a litter cell's or a camera's cell + 0.5).</summary>
	public float X { get; init; }
	public float Y { get; init; }
	/// <summary>The sprite image: 0 vsprite, 1 rsprite, 2 ssprite, 3 lsprite, 4 esprite, 5 gsprite (<see cref="ParkMapDots.Images"/>).</summary>
	public int Image { get; init; }
	/// <summary>The colour kind (<c>[item+0x14]</c>): 0 the guest's happiness ramp, 1 black, 2 red, 3 and 5 white, 4 pale green.</summary>
	public int Kind { get; init; }
	public int Mode { get; init; }
	public int Value { get; init; }
	/// <summary>The item scale (<c>[item+0x24]</c>): 0.5 times the number the item names, 1 for a person and 0.5 for litter and the effect radii.</summary>
	public float Scale { get; init; } = 1f;
	/// <summary>The guest a kind 0 item reads its happiness from.</summary>
	public Guest? Guest { get; init; }
	/// <summary>The staff member of a kind 2 item, whose patrol area the map draws.</summary>
	public StaffMember? Staff { get; init; }
	/// <summary>The colour of the last render.</summary>
	public RgbaByte Color { get; set; } = ParkMapPalette.DotWhite;
}

/// <summary>The map's dot sprites (tp.exe <c>0x5F3940</c> creates the items, <c>0x5F44D0</c> colours them, <c>0x5F4180</c> places them).</summary>
internal static class ParkMapDots
{
	/// <summary>The six images <c>.\data\2dmap\*.tga</c> in the order the table at <c>0xF86D48</c> loads them (map <c>+0x20068..+0x2007C</c>).</summary>
	// [BIN:TPW-EXE:0x5f3680 map sprite names] six strings from 0xf86d40, one per 0x14 bytes: vsprite (0x77537c), rsprite (0x775360), ssprite (0x775344), lsprite (0x775328), esprite (0x77530c), gsprite (0x7752f0); the loop 0x5f3876 stores the handles in that order
	public static readonly string[] Images =
	{
		"/2dmap/vsprite.tga", "/2dmap/rsprite.tga", "/2dmap/ssprite.tga", "/2dmap/lsprite.tga", "/2dmap/esprite.tga", "/2dmap/gsprite.tga"
	};

	/// <summary>The side of each image in pixels: the three solid dots and the ring are 16, the three glows 64 (the files' headers).</summary>
	public static readonly int[] ImageSize = { 16, 16, 16, 64, 64, 64 };

	/// <summary>Whether an item shows for the layer word, staff role and value mode (<c>0x5F44D0</c>: the three filters at <c>0x5F44DB..0x5F4513</c>).</summary>
	public static bool Visible( MapDot dot, uint layers, int staff, int valueMode ) => dot.Mode switch
	{
		1 => (layers & (uint)dot.Value) != 0,
		2 => staff == dot.Value,
		3 => valueMode == dot.Value,
		_ => false
	};

	/// <summary>
	/// The colour of an item for its kind (<c>0x5F4565</c>, the jump table at <c>0x5F4684</c>): the happiness ramp of the guest's float <c>+0x19C</c> for kind 0 (the low byte of its truncation,
	/// blue to orange to yellow), black, red, white for the kinds the table leaves alone, and pale green for kind 4.
	/// </summary>
	// [BIN:TPW-EXE:0x5f456c dot colours] kind 0 on a guest (thing type 1) is 0x5f09a0 of (__ftol(+0x19c) & 0xff) over the records 0xf86c44, 0xf86c68, 0xf86c8c; kind 1 is 0xf86cb0, 2 0xf86cd4, 4 0xf86cf8; the others keep the default record 0xf86a04 (white)
	public static RgbaByte ColorOf( MapDot dot ) => dot.Kind switch
	{
		0 when dot.Guest is { } guest && ParkMapPalette.RampByte( guest.Happiness ) is { } value => ParkMapPalette.DotRamp( value ),
		1 => ParkMapPalette.DotBlack,
		2 => ParkMapPalette.DotRed,
		4 => ParkMapPalette.DotPaleGreen,
		_ => ParkMapPalette.DotWhite
	};

	/// <summary>
	/// The side of an item's sprite in surface pixels. The original draws the sprite square, its height the image width × the item scale × 0.0078125 of the display height; at the default
	/// display mode (480 high) that is 0.375 × the image width × the scale surface pixels, whatever the zoom.
	/// </summary>
	// [BIN:TPW-EXE:0x5f4282 dot size] the record's +0x2c is the image width word × (item scale × zoom × 0.5) × 0.0625; the quad is that × 0.05 × the display height / 2 high and, with the aspect word, as wide (0x589d9c..0x5890e0, the record's flag 0x40000 selects this path)
	public static float SurfaceSize( MapDot dot ) => 0.375f * ImageSize[dot.Image] * dot.Scale;

	/// <summary>Whether a dot is drawn behind the others: the kinds above 2 are placed at depth 0.9999, the others at 0.99 (<c>0x5F4244..0x5F4253</c>).</summary>
	public static bool IsBehind( MapDot dot ) => dot.Kind > 2;
}

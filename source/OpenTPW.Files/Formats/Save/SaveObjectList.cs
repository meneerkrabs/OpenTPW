namespace OpenTPW;

/// <summary>
/// A placed-object record of the save's RideSystem block, decoded by <see cref="SaveRideSystemBlock"/>
/// (tp.exe writer 0x464140, loader 0x4647a0): little-endian <c>u32</c> Info.Id, X, Y, width, height, kind
/// and index, and a <c>u32</c> rotation in degrees. <see cref="InfoId"/> matches the <c>Info.Id</c> of the
/// object's <c>.sam</c> in the level archives. The record's embedded nodes, node states and animation time
/// controls are carried by the block's typed records.
/// </summary>
public readonly record struct SaveObject( int Offset, int InfoId, int X, int Y, int Width, int Height, int Kind, int Index, int Rotation )
{
	/// <summary>Kind of the gates, traffic lights and bus: fixed items placed from MapInfo.FixedItemOrigin.</summary>
	public const int FixedItemKind = 865;
	/// <summary>Kind of every buildable ride, shop, sideshow and feature in Easymode.</summary>
	public const int PlacedObjectKind = 815;
	/// <summary>The largest footprint the corpus and saves hold (16 = 4×4 cells).</summary>
	public const int MaximumFootprint = 16;

	public bool IsFixedItem => Kind == FixedItemKind;

	/// <summary>
	/// Footprint cells for the cases verified against the save cell grid: rotation 0 covers
	/// [X, X+Width) × [Y, Y+Height); rotation 90 of a square covers [X, X+Width) × (Y-Height, Y];
	/// any rotation of a 1×1 object covers its cell. Other combinations are unverified and return false.
	/// </summary>
	public bool TryGetFootprint( out int minX, out int minY, out int maxX, out int maxY )
	{
		minX = maxX = minY = maxY = 0;
		// A footprint needs positive sizes and endpoints that do not overflow (the fields are raw saved words).
		if ( Width <= 0 || Height <= 0 )
			return false;
		var lastX = (long)X + Width - 1;
		if ( lastX > int.MaxValue )
			return false;
		if ( Rotation == 0 || (Width == 1 && Height == 1) )
		{
			var lastY = (long)Y + Height - 1;
			if ( lastY > int.MaxValue )
				return false;
			(minX, maxX, minY, maxY) = (X, (int)lastX, Y, (int)lastY);
			return true;
		}
		if ( Rotation == 90 && Width == Height )
		{
			var firstY = (long)Y - Height + 1;
			if ( firstY < int.MinValue )
				return false;
			(minX, maxX, minY, maxY) = (X, (int)lastX, (int)firstY, Y);
			return true;
		}
		return false;
	}

	/// <summary>
	/// The semantic check a saved scene record (kind 815 or 865) must pass before it is routed as an object: the
	/// Info.Id and index fit 16 bits and are not 0, the cell lies inside the <paramref name="countX"/> ×
	/// <paramref name="countY"/> grid, each size is 1 to <see cref="MaximumFootprint"/> cells, and the rotation is
	/// 0, 90, 180 or 270 degrees. The record's words are raw u32 values, so they are tested as unsigned (a word
	/// above 2^31 is far out of range, not a negative number). Returns null when valid, else the reason.
	/// </summary>
	public string? Validate( int countX, int countY )
	{
		var (id, x, y, width, height, index, rotation) = ((uint)InfoId, (uint)X, (uint)Y, (uint)Width, (uint)Height, (uint)Index, (uint)Rotation);
		if ( id == 0 || id > ushort.MaxValue )
			return $"Info.Id {id} is outside 1..{ushort.MaxValue}";
		if ( x >= (uint)countX || y >= (uint)countY )
			return $"cell ({x}, {y}) is outside the {countX}x{countY} grid";
		if ( width == 0 || width > MaximumFootprint || height == 0 || height > MaximumFootprint )
			return $"size {width}x{height} is outside 1..{MaximumFootprint}";
		if ( index == 0 || index > ushort.MaxValue )
			return $"index {index} is outside 1..{ushort.MaxValue}";
		if ( rotation >= 360 || rotation % 90 != 0 )
			return $"rotation {rotation} is not 0, 90, 180 or 270";
		return null;
	}
}

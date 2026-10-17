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
		minX = X;
		maxX = X + Width - 1;
		if ( Rotation == 0 || (Width == 1 && Height == 1) )
		{
			minY = Y;
			maxY = Y + Height - 1;
			return true;
		}
		if ( Rotation == 90 && Width == Height )
		{
			minY = Y - Height + 1;
			maxY = Y;
			return true;
		}
		minY = maxY = 0;
		return false;
	}
}

namespace OpenTPW;

public partial class Level
{
	/// <summary>
	/// The hover cell, the cell the cursor ray last hit in the park overview (tp.exe's word <c>0x7B05CC</c>, a cell index of the 128-wide map plus one): null is the original's 0, a cursor ray off the map. A park starts on cell (0, 0).
	/// </summary>
	// [BIN:TPW-EXE:0x46c0b0 hover cell start] the word is set to 1 by the cursor object's constructor, the first cell (0, 0)
	// [BIN:TPW-EXE:0x45d5f7 hover cell write] the only other writer is 0x45d560: the hit point of the cursor pick 0x45bf90 (0x7A1C70) divided by the cell size, clamped, becomes y * 128 + x + 1, or 0 when the map's bounds test 0x4d8300 refuses it (0x45d601)
	// [BIN:TPW-EXE:0x54e2b8 hover cell refresh] the park loop 0x54e2b0 runs the pick and 0x45d560 every frame only while [0x790ab0] & 0x16 is clear, that is in the park overview: in the ride view and the walk the word keeps the overview's last value
	internal (int X, int Y)? HoverCell { get; private set; } = (0, 0);

	/// <summary>
	/// The overview frame's refresh of <see cref="HoverCell"/> from the pointer: the cell of the cursor ray's nearest hit, or none when the hit lies off the map. A frame without a hit leaves it as it was; the eye views never call this.
	/// </summary>
	internal void RefreshHoverCell( System.Numerics.Vector2 mousePosition, System.Numerics.Vector2 viewportSize )
	{
		if ( !TryGetGroundHit( mousePosition, viewportSize, out var ground ) )
			return;
		HoverCell = TryGetGridCell( ground.X, ground.Y, out var x, out var y ) ? (x, y) : null;
	}
}

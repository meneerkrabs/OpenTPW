namespace OpenTPW;

public partial class Level
{
	/// <summary>
	/// The hover cell, the cell the cursor ray last hit in the park overview (tp.exe's word <c>0x7B05CC</c>, a cell index of the 128-wide map plus one): null is the original's word 0, which only an empty grid or a non-finite hit gives here. A park starts on cell (0, 0).
	/// </summary>
	// [BIN:TPW-EXE:0x46c0b0 hover cell start] the word is set to 1 by the cursor object's constructor, the first cell (0, 0)
	// [BIN:TPW-EXE:0x45d5f7 hover cell write] the only other writer is 0x45d560: the hit point of the cursor pick 0x45bf90 (0x7A1C70) divided by the cell size, truncated and clamped to the heightfield (0x45d59c-0x45d5c8) and then to the map (0x45d5ca-0x45d5df), becomes y * 128 + x + 1: an off-grid hit lands on the edge cell, and the bounds test 0x4d8300 (0x45d601 writes 0) refuses nothing then
	// [BIN:TPW-EXE:0x54e2b8 hover cell refresh] the park loop 0x54e2b0 runs the pick and 0x45d560 every frame only while [0x790ab0] & 0x16 is clear, that is in the park overview: in the ride view and the walk the word keeps the overview's last value
	internal (int X, int Y)? HoverCell { get; private set; } = (0, 0);

	/// <summary>
	/// The overview frame's refresh of <see cref="HoverCell"/> from the pointer: the cell of the cursor ray's nearest hit, clamped to the edge cell when the hit lies off the grid, or none for an empty grid or a non-finite hit. A frame without a hit leaves it as it was; the eye views never call this.
	/// </summary>
	internal void RefreshHoverCell( System.Numerics.Vector2 mousePosition, System.Numerics.Vector2 viewportSize )
	{
		if ( !TryGetGroundHit( mousePosition, viewportSize, out var ground ) )
			return;
		var grid = Objects?.Grid;
		HoverCell = grid == null ? null : HoverCellOf( ground.X, ground.Y, grid.Origin.X, grid.Origin.Y, grid.Width, grid.Height );
	}

	/// <summary>The cell of a hit point: floored and clamped to the grid, so an off-grid hit is the edge cell; null for an empty grid or a non-finite point.</summary>
	internal static (int X, int Y)? HoverCellOf( float x, float y, float originX, float originY, int width, int height )
	{
		if ( width <= 0 || height <= 0 || !float.IsFinite( x ) || !float.IsFinite( y ) )
			return null;
		var cx = MathF.Floor( (x - originX) / ObjectPlacement.CellSize );
		var cy = MathF.Floor( (y - originY) / ObjectPlacement.CellSize );
		return ((int)Math.Clamp( cx, 0, width - 1 ), (int)Math.Clamp( cy, 0, height - 1 ));
	}
}

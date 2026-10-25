namespace OpenTPW;

/// <summary>
/// The original's landscape height sampler 0x004527F0, the one the object-height modes use (0x4511A0: terrain at the probe plus the object's +0xA0):
/// a point in model units from the grid's corner (0, 0), 10 per cell, answers the height of the cell's triangle it lies in, and 0 outside the grid.
/// </summary>
public static class LandscapeSampler
{
	/// <summary>Model units of one cell (the sampler's bounds are written with this literal, 0x4527F0).</summary>
	public const float CellUnits = 10;

	/// <summary>
	/// The cell's runtime flag 0x800, set when the landscape is loaded (0x4504C0 at 0x45075D): the cell's two corner normals (the cross products of the unit edge
	/// vectors from corner (x, z) and from corner (x+1, z+1)) have a dot product below 0.9999. A cell without it is sampled as one plane.
	/// </summary>
	// [BIN:TPW-EXE:0x004504C0 landscape load] edges (10, b − a, 0) and (0, c − a, 10) from the first corner, (−10, c − d, 0) and (0, b − d, −10) from the last, each normalised; the dot product of the two cross products is compared with the single-precision constant 0.9999 (0x6FE39C, 0.99989998...)
	public static bool IsNonPlanar( float a, float b, float c, float d )
	{
		static (float X, float Y, float Z) Cross( (float X, float Y, float Z) first, (float X, float Y, float Z) second ) =>
			(second.Z * first.Y - first.Z * second.Y, second.X * first.Z - first.X * second.Z, first.X * second.Y - second.X * first.Y);
		static (float X, float Y, float Z) Unit( float x, float y, float z )
		{
			var length = MathF.Sqrt( z * z + y * y + x * x );
			return (x / length, y / length, z / length);
		}
		var firstX = Unit( CellUnits, b - a, 0 );
		var firstZ = Unit( 0, c - a, CellUnits );
		var lastX = Unit( -CellUnits, c - d, 0 );
		var lastZ = Unit( 0, b - d, -CellUnits );
		var first = Cross( firstZ, firstX );
		var last = Cross( lastZ, lastX );
		return (double)first.X * last.X + (double)last.Z * first.Z + (double)last.Y * first.Y < (double)0.9999f;
	}

	/// <summary>The height at the fractions (<paramref name="fx"/>, <paramref name="fz"/>) of a cell with corner heights a = (x, z), b = (x+1, z), c = (x, z+1), d = (x+1, z+1) (0x4527F0).</summary>
	public static float InCell( float a, float b, float c, float d, float fx, float fz, bool nonPlanar, bool antiDiagonal )
	{
		double x = fx, z = fz;
		if ( !nonPlanar )
			return (float)((c - a) * z + (1 - x) * a + b * x);
		if ( !antiDiagonal )
		{
			return fz <= fx
				? (float)((d - b) * z + x * b + (1 - x) * a)
				: (float)((1 - z) * -(c - a) + (1 - x) * c + d * x);
		}
		return 1 - z <= x
			? (float)(-(d - b) * (1 - z) + x * d + (1 - x) * c)
			: (float)((c - a) * z + (1 - x) * a + b * x);
	}

	/// <summary>
	/// The landscape height (engine units) under a point of the grid in model units, 0 off the map (<paramref name="cellsX"/> × <paramref name="cellsZ"/> cells; the bounds use
	/// 10 per cell, the fractions the heightfield's cell size). <paramref name="corner"/> reads a corner's height, <paramref name="antiDiagonal"/> the cell word's bit 0x4.
	/// </summary>
	// [BIN:TPW-EXE:0x004527F0 landscape sampler] x/cell size split into integer and fraction (modf, truncating), the four corners of that cell, the plane or the diagonal's triangle by the cell flags 0x800 and 0x4
	public static float Sample( int cellsX, int cellsZ, float cellSizeX, float cellSizeZ, Func<int, int, float> corner, Func<int, int, bool> antiDiagonal, float x, float y )
	{
		if ( x < 0 || y < 0 || cellsX * CellUnits <= x || cellsZ * CellUnits <= y )
			return 0;
		var (cx, cz) = (x / cellSizeX, y / cellSizeZ);
		var (ix, iz) = ((int)cx, (int)cz);
		var (fx, fz) = (cx - ix, cz - iz);
		if ( ix >= cellsX || iz >= cellsZ )
			return 0;
		var (a, b, c, d) = (corner( ix, iz ), corner( ix + 1, iz ), corner( ix, iz + 1 ), corner( ix + 1, iz + 1 ));
		return InCell( a, b, c, d, fx, fz, IsNonPlanar( a, b, c, d ), antiDiagonal( ix, iz ) );
	}

	/// <summary>
	/// The height the model drape gives a vertex of a draped mesh: the landscape triangle under the point (model units of the grid, 10 per cell), always split along the
	/// cell's diagonal chosen by cell-word bit 0x4, with no 0x800 planar shortcut and no bounds test (a negative coordinate reads cell 0). The fractions and the corner
	/// order are those of <see cref="InCell"/>; a point on or past the far edge reads the last cell at its far side, which is the value the original's index arithmetic
	/// gives there (the far corner row weighs 0).
	/// </summary>
	// [BIN:TPW-EXE:0x00469A80 mesh drape] for a node with flag 0x20000 each vertex is moved to the model height + the landscape under it (the triangle by the cell flag 0x4; a = (x, z), b = (x+1, z), c = (x, z+1), d = (x+1, z+1); flag clear: fz <= fx takes (d - b) z + x b + (1 - x) a, else -(c - a)(1 - z) + (1 - x) c + x d; flag set: 1 - fz <= fx takes -(d - b)(1 - z) + x d + (1 - x) c, else (c - a) z + x b + (1 - x) a) minus the instance height; there is no 0x800 test and no bounds test, only the negative clamp to 0
	public static float SampleDrape( int cellsX, int cellsZ, float cellSizeX, float cellSizeZ, Func<int, int, float> corner, Func<int, int, bool> antiDiagonal, float x, float y )
	{
		var (cx, cz) = (MathF.Max( 0, x / cellSizeX ), MathF.Max( 0, y / cellSizeZ ));
		var (ix, iz) = ((int)cx, (int)cz);
		var (fx, fz) = ((double)(cx - ix), (double)(cz - iz));
		if ( ix >= cellsX )
			(ix, fx) = (cellsX - 1, 1);
		if ( iz >= cellsZ )
			(iz, fz) = (cellsZ - 1, 1);
		var (a, b, c, d) = (corner( ix, iz ), corner( ix + 1, iz ), corner( ix, iz + 1 ), corner( ix + 1, iz + 1 ));
		return InCell( a, b, c, d, (float)fx, (float)fz, true, antiDiagonal( ix, iz ) );
	}
}

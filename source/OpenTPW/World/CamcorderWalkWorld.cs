namespace OpenTPW;

/// <summary>The park a camcorder walk moves in: the heightfield's ground and size, the walk edge test and the ride entrance lookup the level supplies.</summary>
internal sealed class CamcorderWalkWorld : ICamcorderWalkWorld
{
	private readonly ModelHeightfield field;
	private readonly Func<int, int, int, bool> blocked;
	private readonly Func<int, int, object?> rideAt;

	public CamcorderWalkWorld( ModelHeightfield field, Func<int, int, int, bool> blocked, Func<int, int, object?> rideAt )
	{
		this.field = field;
		this.blocked = blocked;
		this.rideAt = rideAt;
		Width = field.CellCountX * field.CellSizeX;
		Depth = field.CellCountZ * field.CellSizeZ;
	}

	public float Width { get; }

	public float Depth { get; }

	/// <summary>The ground's height under a point of original units, 0 outside the heightfield, with the original's single-precision arithmetic.</summary>
	// [BIN:TPW-EXE:0x452560 ground height] a point below 0 or at or beyond the width or depth times 10 answers 0.0; the cell is the integer part of the point over the cell size (modf), the fractions fx and fz
	// pick the triangle (flag 4 of the cell word splits it along the other diagonal) and the height is the corner heights' plane: (1 - fx) x h(x, z) + fx x h(x + 1, z) + fz x (h(x, z + 1) - h(x, z)) for the
	// triangle below the diagonal, (1 - fx) x h(x, z + 1) + fx x h(x + 1, z + 1) - (1 - fz) x (h(x + 1, z + 1) - h(x + 1, z)) for the one above, in the order the x87 adds them (0x4527B5, 0x452785); the camera's
	// call has the third argument 1, which skips the hole test (a hole answers 3.4028235e38 with 0)
	public float Height( float x, float z )
	{
		if ( x < 0f || z < 0f || Width <= x || Depth <= z )
			return 0f;
		var cellsX = x / field.CellSizeX;
		var cellsZ = z / field.CellSizeZ;
		if ( cellsX < 0f || field.CellCountX < cellsX || cellsZ < 0f || field.CellCountZ < cellsZ )
			return 0f;
		var integerX = Math.Truncate( (double)cellsX );
		var integerZ = Math.Truncate( (double)cellsZ );
		var fx = (float)((double)cellsX - integerX);
		var fz = (float)((double)cellsZ - integerZ);
		var cellX = Math.Min( (int)integerX, field.CellCountX - 1 );
		var cellZ = Math.Min( (int)integerZ, field.CellCountZ - 1 );
		float h00 = field.GetCornerHeight( cellX, cellZ ), h10 = field.GetCornerHeight( cellX + 1, cellZ );
		float h01 = field.GetCornerHeight( cellX, cellZ + 1 ), h11 = field.GetCornerHeight( cellX + 1, cellZ + 1 );
		bool upperDiagonal, upperPlane;
		if ( !field.SplitsAlongAntiDiagonal( cellX, cellZ ) )
		{
			upperPlane = fx < fz;
			upperDiagonal = !upperPlane;
		}
		else
		{
			var nearer = fx < 1f - fz;
			upperDiagonal = !nearer;
			upperPlane = !nearer;
		}
		var difference = upperDiagonal ? h11 - h10 : h01 - h00;
		return upperPlane
			? (1f - fx) * h01 + h11 * fx + (1f - fz) * -difference
			: (1f - fx) * h00 + h10 * fx + fz * difference;
	}

	public bool Blocked( int cellX, int cellZ, int direction ) => blocked( cellX, cellZ, direction );

	public object? RideAt( int cellX, int cellZ ) => rideAt( cellX, cellZ );
}

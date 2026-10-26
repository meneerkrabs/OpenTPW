using Matrix4x4 = System.Numerics.Matrix4x4;

namespace OpenTPW;

/// <summary>
/// The drape of a placed model onto the landscape in the original's own arithmetic. tp.exe's thread runs the x87 at the 24-bit control word 0x7F (COAST-032), so every operation below is one
/// single-precision operation, in the order the original does them. The frame is the MD2's (x, y up, z) and the grid's units (the landscape heights' units, 10 per cell); the matrices are
/// row-vector matrices as <see cref="Matrix4x4"/>. The queue models use it (<see cref="QueueModelDrape"/>); the kart and water track pieces' <see cref="TrackDrape"/> is the same routine
/// seen as a lift of each piece's own vertices (<see cref="TrackDrape.Lift"/> computes the same height in engine units from the tile's own matrix, which has no node chain to go through).
/// </summary>
// [BIN:TPW-EXE:0x00469A80 mesh drape] for every vertex of a node with flag 0x20000: the vertex through the node's chain matrix into the grid's frame (0x46FC00), the landscape triangle under its x and z (LandscapeSampler.SampleDrape) minus the instance height plus the vertex's height there, then back through the inverse of the chain (0x470040); x and z are the vertex's own
public static class ModelDrape
{
	/// <summary>The chain matrix of a node (<paramref name="node"/> then <paramref name="parent"/>), tp.exe 0x4702D0: <see cref="ObjectFloorMath.Chain"/> on <see cref="Matrix4x4"/>s.</summary>
	public static Matrix4x4 Chain( Matrix4x4 node, Matrix4x4 parent ) =>
		ObjectFloorMath.ToMatrix( ObjectFloorMath.Chain( ObjectFloorMath.ToArray( node ), ObjectFloorMath.ToArray( parent ) ) );

	private const int TableSize = 4096;
	private const float Radians = 0.017453292f, Quarter = -1.5707964f, Turn = 6.2831855f, PerTurn = 0.15915494f;

	/// <summary>An entry of the sine table the original fills at start-up: the single sin(single(i / 4096) * 2 pi); the entry at 2048 is -8.74e-8, not 0.</summary>
	// [BIN:TPW-EXE:0x004708D0 sine table] N = 4096 (0x449EB0, 0x44A1D0): entry i of 0x7B0968 = FSIN(float(i) / float(N) * [0x6FEBA0] 6.2831855), the scale 0x7B095C = float(N) * [0x6FEB9C] 0.15915494, the mask 0x7B0960 = N - 1
	public static float SineTable( int index )
	{
		float angle = (float)index / TableSize;
		angle *= Turn;
		return (float)Math.Sin( angle ); // [DET:presentation] the drawn queue models' yaw, from the original's sine table
	}

	/// <summary>The sine and cosine the placement uses for an angle in degrees: both from the table, at the indices round(a * scale) and round((a + 1.5707964) * scale) modulo 4096 for the single a = degrees * 0.017453292.</summary>
	// [BIN:TPW-EXE:0x00467030 placement angle] the angle is a = float(rotation) * [0x6FE774]; the sine index is FISTP(a * scale), the cosine index FISTP((a - [0x6FE778] -1.5707964) * scale), each & 0xFFF, nearest-even
	public static (float Sin, float Cos) YawSinCos( int degrees )
	{
		float scale = TableSize * PerTurn;
		float angle = degrees * Radians;
		float sineAt = scale * angle;
		float shifted = angle - Quarter;
		float cosineAt = shifted * scale;
		return (SineTable( (int)MathF.Round( sineAt, MidpointRounding.ToEven ) & (TableSize - 1) ), SineTable( (int)MathF.Round( cosineAt, MidpointRounding.ToEven ) & (TableSize - 1) ));
	}

	/// <summary>
	/// A matrix with its rows turned about the vertical axis: in each of the three rotation rows x' = x cos + z sin and z' = z cos - x sin (elements 0 / 2, 4 / 6, 8 / 10), the translation untouched. The node's own matrix first,
	/// then the yaw.
	/// </summary>
	// [BIN:TPW-EXE:0x0046F650 yaw rotation] multiplies the yaw into the matrix it is given (the root node's own matrix), one single store per element
	public static Matrix4x4 RotateYaw( Matrix4x4 matrix, float cos, float sin )
	{
		static (float X, float Z) Turn( float x, float z, float cos, float sin )
		{
			float first = x * cos;
			float second = z * sin;
			float third = z * cos;
			float fourth = x * sin;
			return (first + second, third - fourth);
		}
		(matrix.M11, matrix.M13) = Turn( matrix.M11, matrix.M13, cos, sin );
		(matrix.M21, matrix.M23) = Turn( matrix.M21, matrix.M23, cos, sin );
		(matrix.M31, matrix.M33) = Turn( matrix.M31, matrix.M33, cos, sin );
		return matrix;
	}

	/// <summary>A point through a chain matrix, tp.exe 0x46FC00: <see cref="ObjectFloorMath.TransformPoint"/>.</summary>
	public static System.Numerics.Vector3 Transform( Matrix4x4 matrix, System.Numerics.Vector3 point ) => ObjectFloorMath.TransformPoint( ObjectFloorMath.ToArray( matrix ), point );

	/// <summary>
	/// The height a vertex takes in the grid's frame: the landscape under it minus the instance height, plus the height it had there (which already holds the instance's own height through the chain).
	/// </summary>
	// [BIN:TPW-EXE:0x00469A80 drape height] y' = ((landscape - base height) + y) for a node with flag 0x20000 (the branch with the node's own height flag clear), in single precision
	public static float Height( float gridHeight, float landscape, float baseHeight )
	{
		float lift = landscape - baseHeight;
		return lift + gridHeight;
	}

	/// <summary>
	/// A vertex of a drape node in the grid's frame once the drape has moved it: through <paramref name="chain"/>, then its height replaced by <see cref="Height"/> over the landscape that
	/// <paramref name="landscape"/> answers at the vertex's x and z (grid units). Its x and z are the transformed ones.
	/// </summary>
	public static System.Numerics.Vector3 Drape( Matrix4x4 chain, System.Numerics.Vector3 vertex, float baseHeight, Func<float, float, float> landscape )
	{
		var placed = Transform( chain, vertex );
		return new System.Numerics.Vector3( placed.X, Height( placed.Y, landscape( placed.X, placed.Z ), baseHeight ), placed.Z );
	}
}

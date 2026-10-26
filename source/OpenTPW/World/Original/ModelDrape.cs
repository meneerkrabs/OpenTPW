using Matrix4x4 = System.Numerics.Matrix4x4;

namespace OpenTPW;

/// <summary>
/// The drape of a placed model onto the landscape in the original's own arithmetic. tp.exe's thread runs the x87 at the 24-bit control word 0x7F (COAST-032), so every operation below is one
/// single-precision operation, in the order the original does them. The frame is the MD2's (x, y up, z) and the grid's units (the landscape heights' units, 10 per cell); the matrices are
/// row-vector matrices as <see cref="Matrix4x4"/>. The queue models use it (<see cref="QueueModelDrape"/>); the kart and water track pieces' <see cref="TrackDrape"/> is the same routine
/// seen as a lift of each piece's own vertices.
/// </summary>
// [BIN:TPW-EXE:0x00469A80 mesh drape] for every vertex of a node with flag 0x20000: the vertex through the node's chain matrix into the grid's frame (0x46FC00), the landscape triangle under its x and z (LandscapeSampler.SampleDrape) minus the instance height plus the vertex's height there, then back through the inverse of the chain (0x470040); x and z are the vertex's own
public static class ModelDrape
{
	/// <summary>
	/// The chain matrix of a node: <paramref name="node"/> then <paramref name="parent"/> (the parent's chain; the identity above the root). Each element is the parent's row 2 times the node's column 2
	/// plus the parent's row 1 times the node's column 1, plus the parent's row 0 times the node's column 0 (the translation row adds the parent's translation last), one single store per element.
	/// The fourth column is the parent's.
	/// </summary>
	// [BIN:TPW-EXE:0x004702D0 matrix chain] 0x469A80 calls it first with the node's matrix (+0x10) and the chain it was given: the result replaces the chain for the node's vertices and its children
	public static Matrix4x4 Chain( Matrix4x4 node, Matrix4x4 parent )
	{
		static float Element( float p2, float p1, float p0, float n2, float n1, float n0 )
		{
			float total = p2 * n2 + p1 * n1;
			total += p0 * n0;
			return total;
		}
		var result = parent;
		// element (a, b): row a of the node, column b of the parent
		result.M11 = Element( parent.M31, parent.M21, parent.M11, node.M13, node.M12, node.M11 );
		result.M12 = Element( parent.M32, parent.M22, parent.M12, node.M13, node.M12, node.M11 );
		result.M13 = Element( parent.M33, parent.M23, parent.M13, node.M13, node.M12, node.M11 );
		result.M21 = Element( parent.M31, parent.M21, parent.M11, node.M23, node.M22, node.M21 );
		result.M22 = Element( parent.M32, parent.M22, parent.M12, node.M23, node.M22, node.M21 );
		result.M23 = Element( parent.M33, parent.M23, parent.M13, node.M23, node.M22, node.M21 );
		result.M31 = Element( parent.M31, parent.M21, parent.M11, node.M33, node.M32, node.M31 );
		result.M32 = Element( parent.M32, parent.M22, parent.M12, node.M33, node.M32, node.M31 );
		result.M33 = Element( parent.M33, parent.M23, parent.M13, node.M33, node.M32, node.M31 );
		result.M41 = Element( parent.M31, parent.M21, parent.M11, node.M43, node.M42, node.M41 ) + parent.M41;
		result.M42 = Element( parent.M32, parent.M22, parent.M12, node.M43, node.M42, node.M41 ) + parent.M42;
		result.M43 = Element( parent.M33, parent.M23, parent.M13, node.M43, node.M42, node.M41 ) + parent.M43;
		return result;
	}

	/// <summary>A point through a chain matrix, each coordinate rounded to a single once per operation in the original's order.</summary>
	// [BIN:TPW-EXE:0x0046FC00 point transform] x' = ((x m0 + m8 z) + m4 y) + m12, y' = ((m9 z + m5 y) + m1 x) + m13, z' = ((m10 z + m6 y) + m2 x) + m14 (m[4 row + column])
	public static System.Numerics.Vector3 Transform( Matrix4x4 matrix, System.Numerics.Vector3 point )
	{
		float x = point.X * matrix.M11;
		x += matrix.M31 * point.Z;
		x += matrix.M21 * point.Y;
		x += matrix.M41;
		float y = matrix.M32 * point.Z;
		y += matrix.M22 * point.Y;
		y += matrix.M12 * point.X;
		y += matrix.M42;
		float z = matrix.M33 * point.Z;
		z += matrix.M23 * point.Y;
		z += matrix.M13 * point.X;
		z += matrix.M43;
		return new System.Numerics.Vector3( x, y, z );
	}

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

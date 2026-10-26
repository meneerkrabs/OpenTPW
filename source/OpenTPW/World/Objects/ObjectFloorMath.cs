using NVector3 = System.Numerics.Vector3;
using Matrix4x4 = System.Numerics.Matrix4x4;

namespace OpenTPW;

/// <summary>
/// The matrix and point arithmetic of the type loader's floor routines as tp.exe's x87 code does it: matrices are 16 floats, row-major, a point is a row vector (the fourth row is the
/// translation), every intermediate sum runs in double precision (the 53-bit mode the game sets, so one double operation is one x87 operation) and every store to memory rounds to a float.
/// The skirt reader (<see cref="ObjectSkirt"/>) and the instance's edge conform (<see cref="ObjectGroundConform"/>) are built on it.
/// </summary>
internal static class ObjectFloorMath
{
	public static readonly float[] Identity = { 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1 };

	/// <summary>A node's stored matrix as the 16 floats the original's code reads.</summary>
	public static float[] ToArray( Matrix4x4 m ) => new[] { m.M11, m.M12, m.M13, m.M14, m.M21, m.M22, m.M23, m.M24, m.M31, m.M32, m.M33, m.M34, m.M41, m.M42, m.M43, m.M44 };

	public static Matrix4x4 ToMatrix( float[] m ) => new( m[0], m[1], m[2], m[3], m[4], m[5], m[6], m[7], m[8], m[9], m[10], m[11], m[12], m[13], m[14], m[15] );

	/// <summary>
	/// <c>a · b</c> for the rotation rows with the translation row of <paramref name="a"/> carried through (tp.exe <c>0x004700F0</c>): each element is the sum of three products
	/// taken in double precision, in the order the code adds them, rounded to a float once; elements 3, 7, 11 and 15 stay those of <paramref name="a"/>.
	/// </summary>
	// [BIN:TPW-EXE:0x004700F0 matrix multiply] a row (l0, l1, l2) of a gives ((l1·b4 + l2·b8) + l0·b0), ((l0·b1 + l2·b9) + l1·b5) and ((l0·b2 + l2·b10) + l1·b6); the translation row adds b12, b13, b14 last; one float store per element
	public static float[] Multiply( float[] a, float[] b )
	{
		var result = (float[])a.Clone();
		for ( var row = 0; row <= 8; row += 4 )
		{
			double l0 = a[row], l1 = a[row + 1], l2 = a[row + 2];
			result[row] = (float)(l1 * b[4] + l2 * b[8] + l0 * b[0]);
			result[row + 1] = (float)(l0 * b[1] + l2 * b[9] + l1 * b[5]);
			result[row + 2] = (float)(l0 * b[2] + l2 * b[10] + l1 * b[6]);
		}
		{
			double l0 = a[12], l1 = a[13], l2 = a[14];
			result[12] = (float)(l1 * b[4] + l2 * b[8] + l0 * b[0] + b[12]);
			result[13] = (float)(l0 * b[1] + l2 * b[9] + l1 * b[5] + b[13]);
			result[14] = (float)(l0 * b[2] + l2 * b[10] + l1 * b[6] + b[14]);
		}
		return result;
	}

	/// <summary>A node's matrix times each of its parents' (tp.exe <c>0x004628B0</c>, nearest parent first): the matrix its vertices go through to the root.</summary>
	public static float[] WorldMatrix( float[] node, IEnumerable<float[]> parents )
	{
		var matrix = (float[])node.Clone();
		foreach ( var parent in parents )
			matrix = Multiply( matrix, parent );
		return matrix;
	}

	/// <summary>A vertex through the world matrix (the tail of <c>0x004628B0</c>: <c>x' = ((m8·z + m4·y) + m0·x) + m12</c> and the like, one float store per axis).</summary>
	public static NVector3 TransformVertex( float[] m, NVector3 p )
	{
		double x = p.X, y = p.Y, z = p.Z;
		return new NVector3(
			(float)(m[8] * z + m[4] * y + m[0] * x + m[12]),
			(float)(m[9] * z + m[5] * y + m[1] * x + m[13]),
			(float)(m[10] * z + m[6] * y + m[2] * x + m[14]) );
	}

	/// <summary>
	/// The matrix of a node in the instance's tree (tp.exe <c>0x004702D0</c>): the node's own matrix <paramref name="node"/> times its parent's <paramref name="parent"/>, a vertex going through the node and then the parents.
	/// </summary>
	// [BIN:TPW-EXE:0x004702D0 matrix chain] element [4a + b] = ((P[8 + b]·N[4a + 2] + P[4 + b]·N[4a + 1]) + P[b]·N[4a]) for the three rotation rows, the translation row adds P[12 + b] last (P the parent, N the node), one float store per element; elements 3, 7, 11 and 15 stay the parent's
	public static float[] Chain( float[] node, float[] parent )
	{
		var result = (float[])parent.Clone();
		for ( var a = 0; a < 4; a++ )
		{
			for ( var b = 0; b < 3; b++ )
			{
				var total = (double)parent[8 + b] * node[4 * a + 2] + (double)parent[4 + b] * node[4 * a + 1];
				total += (double)parent[b] * node[4 * a];
				if ( a == 3 )
					total += parent[12 + b];
				result[4 * a + b] = (float)total;
			}
		}
		return result;
	}

	/// <summary>A point through a matrix (tp.exe <c>0x0046FC00</c>): <c>x' = ((x·m0 + m8·z) + m4·y) + m12</c>, <c>y' = ((m9·z + m5·y) + m1·x) + m13</c>, <c>z' = ((m10·z + m6·y) + m2·x) + m14</c>, a float store per axis.</summary>
	public static NVector3 TransformPoint( float[] m, NVector3 p )
	{
		double x = p.X, y = p.Y, z = p.Z;
		return new NVector3(
			(float)(x * m[0] + m[8] * z + m[4] * y + m[12]),
			(float)(m[9] * z + m[5] * y + m[1] * x + m[13]),
			(float)(m[10] * z + m[6] * y + m[2] * x + m[14]) );
	}

	/// <summary>
	/// The inverse of a matrix by the original's LU decomposition with implicit scaling and partial pivoting in float storage (tp.exe <c>0x00470040</c> with <c>0x0046FE20</c> and <c>0x0046FD40</c>): one
	/// column per unit vector. A plain inverse differs from it in the last bits.
	/// </summary>
	// [BIN:TPW-EXE:0x00470040 matrix inverse] 0x0046FE20 decomposes in place: the row scale is the float of 1 / the largest element, sums run in double and each stored element is the float of its sum, the pivot is the row with the largest |sum|·scale (>= wins, so the last of equals), a zero pivot becomes 1e-20, the column below the pivot is multiplied by the float of 1 / pivot; 0x0046FD40 solves by forward and back substitution
	public static float[] Inverse( float[] matrix )
	{
		var a = new float[4, 4];
		for ( var i = 0; i < 4; i++ )
			for ( var j = 0; j < 4; j++ )
				a[i, j] = matrix[4 * i + j];
		var scale = new float[4];
		for ( var i = 0; i < 4; i++ )
		{
			double largest = 0;
			for ( var j = 0; j < 4; j++ )
				if (Math.Abs( (double)a[i, j] ) > largest)
					largest = Math.Abs( (double)a[i, j] );
			scale[i] = (float)(1.0 / largest);
		}
		var index = new int[4];
		for ( var j = 0; j < 4; j++ )
		{
			for ( var i = 0; i < j; i++ )
			{
				double total = a[i, j];
				for ( var k = 0; k < i; k++ )
					total -= (double)a[k, j] * a[i, k];
				a[i, j] = (float)total;
			}
			double best = 0;
			var pivot = 0;
			for ( var i = j; i < 4; i++ )
			{
				double total = a[i, j];
				for ( var k = 0; k < j; k++ )
					total -= (double)a[k, j] * a[i, k];
				a[i, j] = (float)total;
				var value = Math.Abs( total ) * scale[i];
				if ( value >= best )
				{
					best = (float)value;
					pivot = i;
				}
			}
			if ( j != pivot )
			{
				for ( var k = 0; k < 4; k++ )
					(a[pivot, k], a[j, k]) = (a[j, k], a[pivot, k]);
				scale[pivot] = scale[j];
			}
			index[j] = pivot;
			if ( a[j, j] == 0f )
				a[j, j] = 1e-20f;
			if ( j != 3 )
			{
				var reciprocal = (float)(1.0 / a[j, j]);
				for ( var i = j + 1; i < 4; i++ )
					a[i, j] = (float)((double)reciprocal * a[i, j]);
			}
		}
		var result = new float[16];
		for ( var column = 0; column < 4; column++ )
		{
			var b = new float[4];
			b[column] = 1f;
			var first = -1;
			for ( var i = 0; i < 4; i++ )
			{
				double total = b[index[i]];
				b[index[i]] = b[i];
				if ( first >= 0 )
				{
					for ( var j = first; j < i; j++ )
						total -= (double)b[j] * a[i, j];
				}
				else if ( total != 0 )
					first = i;
				b[i] = (float)total;
			}
			for ( var i = 3; i >= 0; i-- )
			{
				double total = b[i];
				for ( var j = i + 1; j < 4; j++ )
					total -= (double)a[i, j] * b[j];
				b[i] = (float)(total / a[i, i]);
			}
			for ( var row = 0; row < 4; row++ )
				result[row * 4 + column] = b[row];
		}
		return result;
	}
}

namespace OpenTPW;

/// <summary>
/// The arithmetic of the original's head placement (tp.exe <c>0x0044B510</c>, run for every head node) step by step as its x87 code does it: single-precision operations in the order the routine runs them
/// (the shipped D3DIM device leaves the thread at a 24-bit significand, so every operation rounds as a float does), the engine's own matrix inverse (<c>0x00470040</c>: an LU decomposition with implicit pivot
/// scaling and its back substitution, <c>0x0046FE20</c> and <c>0x0046FD40</c>), its matrix product <c>0x004702D0</c>, the frame table walked by rotating a vector through the PC sine table, and the roll
/// <c>0x0046F200</c> (<c>fpatan</c>). Matrices are 16 floats, row major, the translation in the fourth row.
/// </summary>
// [BIN:TPW-EXE:0x0044B510 head placement] checked bit for bit against the routine run in the Unicorn sandbox (tools/ppc-analysis/oracle_x86/head_golden.py, HeadPlacementGolden)
internal static class OriginalHeadMath
{
	/// <summary>The sine table's scale word (<c>0x007B095C</c>: 4096 / 2 pi as a float) and index mask (<c>0x007B0960</c>).</summary>
	private const float TableScale = OriginalSineTable.Size * 0.15915494f;
	private const int TableMask = OriginalSineTable.Size - 1;

	/// <summary>The sprite register 8 value the head sets: the scale 0.685 (<c>0x0074CED0</c>).</summary>
	public const float HeadScale = 0.685f;

	/// <summary>The table's entry for an angle already multiplied by the scale (<c>fistp</c> rounds to nearest even; the entry is read at the masked index).</summary>
	private static float TableAt( float scaledAngle ) => OriginalSineTable.Entries[(int)MathF.Round( scaledAngle ) & TableMask];

	// The x87 registers hold a 24-bit significand (the control word 0x7F) with the full extended exponent range, so the chains that stay in registers (the sums and products of the decomposition, the product of
	// two matrices, the length of the camera offset) do not overflow where a float would; each operation here is a double operation rounded to 24 significant bits (a double has more than twice the bits, so
	// the rounding is the same as the x87's single rounding), and only a store to memory narrows to a float.
	private static double Round24( double value )
	{
		var bits = BitConverter.DoubleToInt64Bits( value );
		var magnitude = bits & long.MaxValue;
		if ( magnitude >= 0x7FF0000000000000L )
			return value;
		magnitude += (1L << 28) - 1 + ((magnitude >> 29) & 1);
		magnitude &= ~((1L << 29) - 1);
		return BitConverter.Int64BitsToDouble( (bits & long.MinValue) | magnitude );
	}

	private static double Mul( double left, double right ) => Round24( left * right );
	private static double Add( double left, double right ) => Round24( left + right );
	private static double Sub( double left, double right ) => Round24( left - right );
	private static double Div( double left, double right ) => Round24( left / right );

	/// <summary>
	/// The matrix inverse <c>0x00470040</c>: LU decomposition with partial pivoting by implicit row scaling (<c>0x0046FE20</c>, a zero pivot becomes 1e-20), then one back substitution
	/// per column of the identity (<c>0x0046FD40</c>).
	/// </summary>
	// [BIN:TPW-EXE:0x00470040 matrix inverse] copies the source (arg 2), decomposes it, solves for the four unit columns (column j of the result is the solution for the unit vector j, 0x004700A3), and copies the result to arg 1
	public static void Invert( ReadOnlySpan<float> source, Span<float> result )
	{
		Span<float> a = stackalloc float[16];
		source.CopyTo( a );
		Span<int> indx = stackalloc int[4];
		LuDecompose( a, indx );
		Span<float> b = stackalloc float[4];
		for ( var column = 0; column < 4; column++ )
		{
			b.Clear();
			b[column] = 1f;
			LuSolve( a, indx, b );
			for ( var row = 0; row < 4; row++ )
				result[row * 4 + column] = b[row];
		}
	}

	// [BIN:TPW-EXE:0x0046FE20 LU decomposition] the Crout method of the Numerical Recipes (ludcmp): vv[i] = 1 / max |a[i][j]|; each column j first finishes the upper part a[i][j] -= sum a[i][k] a[k][j], then the lower part,
	// picks the row of the largest vv[i] |sum| (a tie takes the later row), swaps it into place, and divides the column below the pivot by it; a pivot of exactly 0 (or a NaN, which the compare reads as equal) is replaced
	// by 0x1E3CE508; every compare of the x87 code is an ordered one, so a NaN never wins
	private static void LuDecompose( Span<float> a, Span<int> indx )
	{
		Span<float> vv = stackalloc float[4];
		for ( var i = 0; i < 4; i++ )
		{
			double big = 0;
			for ( var j = 0; j < 4; j++ )
			{
				double temp = MathF.Abs( a[i * 4 + j] );
				if ( temp > big )
					big = temp;
			}
			vv[i] = (float)Div( 1.0, big );
		}
		// the pivot row is a local the routine never resets (0x0046FF43 writes it only when a candidate wins), so a column whose candidates all compare unordered reuses the previous column's row
		var imax = 0;
		for ( var j = 0; j < 4; j++ )
		{
			for ( var i = 0; i < j; i++ )
			{
				double sum = a[i * 4 + j];
				for ( var k = 0; k < i; k++ )
					sum = Sub( sum, Mul( a[k * 4 + j], a[i * 4 + k] ) );
				a[i * 4 + j] = (float)sum;
			}
			var biggest = 0f;
			for ( var i = j; i < 4; i++ )
			{
				double sum = a[i * 4 + j];
				for ( var k = 0; k < j; k++ )
					sum = Sub( sum, Mul( a[k * 4 + j], a[i * 4 + k] ) );
				a[i * 4 + j] = (float)sum;
				var figure = Mul( Math.Abs( sum ), vv[i] );
				if ( figure >= biggest )
				{
					biggest = (float)figure;
					imax = i;
				}
			}
			if ( j != imax )
			{
				for ( var k = 0; k < 4; k++ )
					(a[imax * 4 + k], a[j * 4 + k]) = (a[j * 4 + k], a[imax * 4 + k]);
				vv[imax] = vv[j];
			}
			indx[j] = imax;
			if ( a[j * 4 + j] == 0f || float.IsNaN( a[j * 4 + j] ) )
				a[j * 4 + j] = BitConverter.Int32BitsToSingle( 0x1E3CE508 );
			if ( j != 3 )
			{
				var scale = (float)Div( 1.0, a[j * 4 + j] );
				for ( var i = j + 1; i < 4; i++ )
					a[i * 4 + j] = (float)Mul( scale, a[i * 4 + j] );
			}
		}
	}

	// [BIN:TPW-EXE:0x0046FD40 back substitution] lubksb: the forward pass swaps b[indx[i]] into place and subtracts a[i][j] b[j] from the first nonzero entry on; the backward pass subtracts a[i][j] b[j] for j > i and divides by a[i][i]
	private static void LuSolve( ReadOnlySpan<float> a, ReadOnlySpan<int> indx, Span<float> b )
	{
		var first = -1;
		for ( var i = 0; i < 4; i++ )
		{
			var ip = indx[i];
			double sum = b[ip];
			b[ip] = b[i];
			if ( first >= 0 )
			{
				for ( var j = first; j <= i - 1; j++ )
					sum = Sub( sum, Mul( b[j], a[i * 4 + j] ) );
			}
			else if ( sum != 0 && !double.IsNaN( sum ) )
				first = i;
			b[i] = (float)sum;
		}
		for ( var i = 3; i >= 0; i-- )
		{
			double sum = b[i];
			for ( var j = i + 1; j < 4; j++ )
				sum = Sub( sum, Mul( a[i * 4 + j], b[j] ) );
			b[i] = (float)Div( sum, a[i * 4 + i] );
		}
	}

	/// <summary>
	/// The matrix product <c>0x004702D0</c>(first, second): <paramref name="second"/> becomes first x second, the affine form: the three columns of the result, the fourth row adding the second matrix's translation, and
	/// the fourth column of <paramref name="second"/> left as it was.
	/// </summary>
	// [BIN:TPW-EXE:0x004702D0 matrix product] out[i][j] = (second[2][j] first[i][2] + second[1][j] first[i][1]) + second[0][j] first[i][0] for j below 3, plus second[3][j] on row 3; the fourth column (words 3, 7, 11, 15) is not written
	public static void Multiply( ReadOnlySpan<float> first, Span<float> second )
	{
		Span<float> old = stackalloc float[16];
		second.CopyTo( old );
		for ( var i = 0; i < 4; i++ )
		{
			for ( var j = 0; j < 3; j++ )
			{
				var sum = Add( Mul( old[8 + j], first[i * 4 + 2] ), Mul( old[4 + j], first[i * 4 + 1] ) );
				sum = Add( sum, Mul( old[j], first[i * 4] ) );
				if ( i == 3 )
					sum = Add( sum, old[12 + j] );
				second[i * 4 + j] = (float)sum;
			}
		}
	}

	/// <summary>
	/// The head's frame and roll for a node world matrix and the camera matrix: false (the frame 0 and no roll, the original's stack words undefined there) when the camera's offset in the node's frame is not
	/// a finite direction.
	/// </summary>
	// [BIN:TPW-EXE:0x0044B5E6 head frame] the node matrix with its first and third rows negated for the mirrored flag 0x400 is inverted and multiplied from the camera (0x44B637, 0x44B65C); the translation row is normalised
	// (the sum of squares in the order x, y, z, a square root and three divisions; a NaN or infinity ends the search at frame 0, 0x44B6D4); each of the 7 rows by 8 columns of the walk takes the vector (0, 1, 0), steps 8 times by 45 degrees and
	// then once by 30 degrees through the sine table; the angle offsets are subtracted from the negative word 0x6FE2A4, so they add a quarter turn (0x44B734..0x44B814); a direction replaces the best one when its dot product is greater (0x44B70E), rows 0 and 6 take column 0 (0x44B81C)
	public static bool TryFrame( ReadOnlySpan<float> nodeWorld, ReadOnlySpan<float> camera, out int row, out int column, out float roll )
	{
		Span<float> cameraInNode = stackalloc float[16];
		Invert( nodeWorld, cameraInNode );
		Multiply( camera, cameraInNode );
		row = 0;
		column = 0;
		roll = 0;
		var x = cameraInNode[12];
		var y = cameraInNode[13];
		var z = cameraInNode[14];
		var length = Round24( Math.Sqrt( Add( Add( Mul( x, x ), Mul( y, y ) ), Mul( z, z ) ) ) );
		var nx = (float)Div( x, length );
		var ny = (float)Div( y, length );
		var nz = (float)Div( z, length );
		// [EXT:original-undefined-reads] the search ends here at frame 0 with the best direction (stack words [ebp-0x1C..-0x14]) never written, and the original goes on to build the roll from them: undefined; the host stops with roll 0
		if ( !float.IsFinite( nx ) )
			return false;
		// the rotation constants: the table's values at -45, +45, 30 and 120 degrees (0x6FE2BC, 0x6FE2C0, 0x6FE2C4, 0x6FE2C8)
		var minusEighth = TableAt( TableScale * -0.7853982f );
		var plusEighth = TableAt( TableScale * 0.7853982f );
		var sixth = TableAt( TableScale * 0.5235988f );
		var third = TableAt( TableScale * 2.0943952f );
		float vx = 0f, vy = 1f, vz = 0f;
		float best = 0f, bx = 0f, by = 0f, bz = 0f;
		var bestRow = 0;
		var bestColumn = 0;
		for ( var r = 0; r < 7; r++ )
		{
			var heightTerm = ny * vy;
			for ( var c = 0; c < 8; c++ )
			{
				var dot = nx * vx + nz * vz + heightTerm;
				if ( dot > best )
				{
					best = dot;
					bestRow = r;
					bestColumn = c;
					(bx, by, bz) = (vx, vy, vz);
				}
				(vx, vz) = (plusEighth * vx + minusEighth * vz, plusEighth * vz - vx * minusEighth);
			}
			(vy, vz) = (third * vy - sixth * vz, third * vz + vy * sixth);
		}
		if ( bestRow == 0 || bestRow == 6 )
			bestColumn = 0;
		row = bestRow;
		column = bestColumn;
		// the chosen frame's up vector: (0, 0, -1) turned by the row angle and then by the column angle (0x44B82F..0x44B93x), its right vector the cross product up x direction
		float ux = 0f, uy = 0f, uz = -1f;
		var elevation = bestRow * 0.5235988f;
		var rowSin = TableAt( TableScale * elevation );
		var rowCos = TableAt( (elevation + 1.5707964f) * TableScale );
		(uy, uz) = (rowCos * uy - rowSin * uz, rowCos * uz + uy * rowSin);
		var azimuth = bestColumn * -0.7853982f;
		var columnSin = TableAt( TableScale * azimuth );
		var columnCos = TableAt( (azimuth + 1.5707964f) * TableScale );
		(ux, uz) = (columnCos * ux + columnSin * uz, columnCos * uz - ux * columnSin);
		var rx = bz * uy - by * uz;
		var ry = bx * uz - bz * ux;
		var rz = by * ux - bx * uy;
		Span<float> basis = stackalloc float[16];
		basis.Clear();
		basis[0] = rx; basis[1] = ry; basis[2] = rz;
		basis[4] = ux; basis[5] = uy; basis[6] = uz;
		basis[8] = bx; basis[9] = by; basis[10] = bz;
		basis[15] = 1f;
		Span<float> inverseBasis = stackalloc float[16];
		Invert( basis, inverseBasis );
		Multiply( cameraInNode, inverseBasis );
		// 0x0046F200: fpatan(-m[1][0], m[1][1]), scaled to turns by 1 / 2 pi (0x6FE2CC)
		roll = DetMathF.Atan2( -inverseBasis[4], inverseBasis[5] ) * 0.15915494f;
		return true;
	}
}

namespace OpenTPW;

/// <summary>
/// The x87 as the self-intersection pass runs it (tp.exe <c>0x439ab0</c>): the pass sets the rounding control to truncate while the thread keeps its 24-bit precision control
/// (<c>0x439ac4</c>: control word 0x7F becomes 0xC7F), so every add, subtract, multiply, divide and square root of the pass, and every store of a register to a single, rounds toward
/// zero at 24 bits. Each operation here is one such step on exact operands: the result is the largest single not beyond the exact value in magnitude.
/// </summary>
// [BIN:TPW-EXE:0x00439ac4 truncating rounding] fnstcw, or ah 0xc, fldcw: round toward zero, the precision bits unchanged (24 at the thread's word 0x7F, [BIN:TPW-EXE:0x00563914 control word]); the old word is restored at 0x439c30
internal static class Trunc24
{
	/// <summary>The sum of two exact values (doubles holding singles, or an extended acos result held in a double) rounded toward zero to a single.</summary>
	public static float Add( double a, double b )
	{
		var sum = a + b;
		if ( !double.IsFinite( sum ) )
			return (float)sum;
		// the exact sum is sum + error (Knuth's two-sum): the error decides on which side of the nearest single it lies
		var virtualB = sum - a;
		var error = a - (sum - virtualB) + (b - virtualB);
		return TowardZero( sum, error );
	}

	public static float Sub( double a, double b ) => Add( a, -b );

	/// <summary>The product of two singles is exact in a double; rounded toward zero to a single.</summary>
	public static float Mul( float a, float b )
	{
		var product = (double)a * b;
		return double.IsFinite( product ) ? TowardZero( product, 0d ) : (float)product;
	}

	public static float Div( float a, float b )
	{
		var quotient = (double)a / b;
		if ( !double.IsFinite( quotient ) || b == 0f || quotient == 0d )
			return (float)quotient;
		var candidate = (float)quotient;
		// the sign of a − candidate · b says on which side of the exact quotient the candidate lies (the product is exact, the fused difference keeps its sign)
		var remainder = Math.FusedMultiplyAdd( -(double)candidate, b, a );
		var sign = Math.Sign( remainder ) * Math.Sign( b );
		return Correct( candidate, sign );
	}

	public static float Sqrt( float a )
	{
		var root = MathF.Sqrt( a );
		if ( !float.IsFinite( root ) || root == 0f )
			return root;
		// the root is correctly rounded: when its square lies above the radicand it is one step too large
		return (double)root * root > a ? MathF.BitDecrement( root ) : root;
	}

	/// <summary><c>fistp</c> with the truncating rounding control: the integer part of a single.</summary>
	public static int ToInt( float value ) => (int)value;

	/// <summary>The nearest single of hi + lo moved toward zero when it lies beyond the exact value.</summary>
	private static float TowardZero( double hi, double lo )
	{
		var candidate = (float)hi;
		if ( candidate == 0f || !float.IsFinite( candidate ) )
			return candidate;
		var gap = hi - candidate; // exact: the candidate is the nearest single to hi
		var sign = gap == 0d ? Math.Sign( lo ) : lo == 0d ? Math.Sign( gap ) : Math.Sign( gap ) == Math.Sign( lo ) ? Math.Sign( gap ) : Math.Abs( gap ) > Math.Abs( lo ) ? Math.Sign( gap ) : Math.Abs( gap ) < Math.Abs( lo ) ? Math.Sign( lo ) : 0;
		return Correct( candidate, sign );
	}

	/// <summary>Steps a single toward zero when the exact value (on the side <paramref name="sign"/> of it) is smaller in magnitude.</summary>
	private static float Correct( float candidate, int sign )
	{
		if ( candidate > 0f && sign < 0 )
			return MathF.BitDecrement( candidate );
		if ( candidate < 0f && sign > 0 )
			return MathF.BitIncrement( candidate );
		return candidate;
	}
}

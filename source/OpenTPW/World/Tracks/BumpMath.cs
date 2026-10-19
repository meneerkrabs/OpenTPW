namespace OpenTPW;

/// <summary>
/// The integer helpers of the bump-ride physics (docs/TRACKS.md): the 512-entry sine table, headings in 512ths of a turn, the scaled
/// integer distance and the truncating shifts the original uses. A heading is measured clockwise from +Y, so a vehicle at heading h
/// moves along (sin h, cos h): 0 is +Y, 128 is +X, 256 is −Y, 384 is −X.
/// </summary>
public static class BumpMath
{
	public const int Turn = 512;
	public const int QuarterTurn = 128;
	private const double InversePi = 0.31831015504887655;

	/// <summary>The sine table: 256 × sin(2·i·0.00613591796875), truncated toward zero, 512 entries.</summary>
	// [BIN:TPW-EXE:0x005442E0 sine table init] the table (0x877358, 512 ints) is filled at start-up from fsin(2i · 0.00613591796875) × 256 through the truncating float-to-int conversion 0x0067A830 (control word round-to-zero); verified here by regenerating it, the table itself is not in the file image
	public static readonly int[] Sine = BuildSine();

	private static int[] BuildSine()
	{
		var table = new int[Turn];
		for ( var index = 0; index < Turn; index++ )
			table[index] = (int)(DetMath.Sin( index * 2 * 0.00613591796875 ) * 256.0);
		return table;
	}

	public static int Sin( int heading ) => Sine[heading & 0x1FF];
	public static int Cos( int heading ) => Sine[(heading + QuarterTurn) & 0x1FF];

	/// <summary><c>x · 4 / 1024</c> style scaling: the value divided by 1024, truncating toward zero (the original adds 0x3FF to negative values before the arithmetic shift).</summary>
	public static int Div1024( int value ) => (value + ((value >> 31) & 0x3FF)) >> 10;

	/// <summary>The heading of the vector (<paramref name="dx"/>, <paramref name="dy"/>): 256 − atan2(dx, −dy) in 512ths of a turn, truncated.</summary>
	// [EXT:deterministic-math] the x87's fpatan in IEEE double by DetMath, the same bits on every runtime, not in x87 extended precision (docs/DETERMINISM.md, "Browser and native")
	public static int HeadingOf( int dx, int dy ) => (int)(256.0 - DetMath.Atan2( dx, -dy ) * InversePi * 256.0 );

	/// <summary>The steering angle atan2(dx, −dy) in 512ths of a turn less 256, truncated: the negative of <see cref="HeadingOf"/>, as the steering code uses it.</summary>
	// [EXT:deterministic-math] the x87's fpatan in IEEE double by DetMath, the same bits on every runtime, not in x87 extended precision (docs/DETERMINISM.md, "Browser and native")
	public static int SteerAngle( int dx, int dy ) => (int)(DetMath.Atan2( dx, -dy ) * InversePi * 256.0 + -256.0);

	/// <summary>−256 − atan2(dx, −dy) term of the radial angle in the wall reflection: <c>(−256) − ψ · (−256)</c>, truncated.</summary>
	// [EXT:deterministic-math] the x87's fpatan in IEEE double by DetMath, the same bits on every runtime, not in x87 extended precision (docs/DETERMINISM.md, "Browser and native")
	public static int ReflectAngle( int dx, int dy ) => (int)(-256.0 - DetMath.Atan2( dx, -dy ) * InversePi * -256.0 );

	/// <summary>
	/// The original's distance: halve both components until |dx| ≤ 0x8000 and |dy| ≤ 0x7FFF, take the integer square root of the sum of squares
	/// (through a float square root, truncated) and shift the result back.
	/// </summary>
	public static int Distance( int dx, int dy )
	{
		var shift = 0;
		var a = Math.Abs( dx );
		var b = Math.Abs( dy );
		while ( a > 0x8000 || b > 0x7FFF )
		{
			shift++;
			a >>= 1;
			b >>= 1;
		}
		return (int)Math.Sqrt( (double)(a * a + b * b) ) << shift;
	}

	/// <summary>The plain integer length √(x² + y²), truncated, for velocities.</summary>
	public static int Length( int x, int y ) => (int)Math.Sqrt( (double)((long)x * x + (long)y * y) );
}

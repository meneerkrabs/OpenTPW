using SVector3 = System.Numerics.Vector3;

namespace OpenTPW;

/// <summary>
/// The scalar steps tp.exe's coaster code takes at the thread's 24-bit x87 precision (COAST-032): every operation here is one single-precision operation in the order the
/// executable performs it. The vector types of System.Numerics are not used for these (their length, dot product and normalisation may associate the sums differently).
/// </summary>
internal static class CoasterX87
{
	/// <summary>The length the coaster routines take: fsqrt of ((x · x + y · y) + z · z), the squares and the sums rounded to singles in that order.</summary>
	// [BIN:TPW-EXE:0x00432840 knot chord] (dx² + dy²) + dz² then fsqrt, e.g. 0x4328e6..0x43292f, 0x440f3a..0x440f5a (the sampler's step) and 0x44153f..0x44154f (the projection)
	public static float Length( float x, float y, float z )
	{
		var xx = x * x;
		var yy = y * y;
		var zz = z * z;
		return MathF.Sqrt( xx + yy + zz );
	}

	public static float Length( SVector3 v ) => Length( v.X, v.Y, v.Z );

	/// <summary>The vector divided by its length (<c>0x432231</c>, <c>0x43232b</c>): each component over the single-stored length.</summary>
	public static SVector3 Normalize( SVector3 v )
	{
		var length = Length( v );
		return new SVector3( v.X / length, v.Y / length, v.Z / length );
	}

	/// <summary>The vector divided by the length of its x and z part (<c>0x4387a0</c>): fsqrt of (x · x + z · z) stored as a single, then each component divided by it.</summary>
	// [BIN:TPW-EXE:0x004387a0 x/z normalise] the horizontal length is sqrt(x² + z²) (the height does not enter it) and all three components, the height too, are divided by it
	public static SVector3 NormalizeXz( SVector3 v )
	{
		var xx = v.X * v.X;
		var zz = v.Z * v.Z;
		var length = MathF.Sqrt( xx + zz );
		return new SVector3( v.X / length, v.Y / length, v.Z / length );
	}

	/// <summary>The C library's acos (<c>0x67b4e0</c>) at the thread's 24-bit precision: fpatan of sqrt((1 + x) · (1 − x)) over x, the sum, the difference, their product and the root each rounded to a single.</summary>
	// [BIN:TPW-EXE:0x0067b518 acos] for |x| below 1: s = 1 + x, t = 1 − x, p = s · t, q = fsqrt p (each a single at the 24-bit word, which the routine keeps: 0x686185 changes only the exception masks), then fpatan(q, x); 1 gives 0 and −1 gives π (0x67b545..0x67b566)
	public static float Acos( float x )
	{
		if ( x >= 1f )
			return x == 1f ? 0f : float.NaN;
		if ( x <= -1f )
			return x == -1f ? MathF.PI : float.NaN;
		var sum = 1f + x;
		var difference = 1f - x;
		var product = sum * difference;
		var root = MathF.Sqrt( product );
		// [EXT:deterministic-math] fpatan in IEEE double by DetMath, the same bits on every runtime, not in x87 extended precision (docs/DETERMINISM.md, "Browser and native")
		return DetMathF.Atan2( root, x );
	}

	/// <summary>The running speed estimate of the bank pass (<c>0x43bfb0</c>): ((y − y_next) · perHeight) · modifier + speed, the floor when the value is not above it.</summary>
	// [BIN:TPW-EXE:0x0043bfb0 speed estimate] the height difference times definition +0x24, times +0x28 when the next node is higher and +0x2c otherwise, plus the speed, each step a single (the thread's 24-bit precision); the floor replaces a value that is not above it (0x43bfd5..0x43bfff); the Mac fused the last two steps
	public static float SpeedEstimate( float perHeight, float uphill, float downhill, float speed, float floor, float y, float yNext )
	{
		var modifier = yNext > y ? uphill : downhill;
		var value = (y - yNext) * perHeight * modifier + speed;
		return value > floor ? value : floor;
	}
}

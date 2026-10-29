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
}

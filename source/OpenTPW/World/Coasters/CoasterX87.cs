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

	/// <summary>The node field +0xEC of the knot pass: the fractional part of the node's yaw times f32(1/2π), plus 1 when it is negative.</summary>
	// [BIN:TPW-EXE:0x0043110c node turn fraction] st0 = yaw · f32(1/2π) (0x6fdea8), modf (0x67b280), the fraction plus 1.0 (fsub −1.0) when it is below 0, stored as a single in +0xEC (0x431112..0x431151)
	public static float TurnFraction( float yaw )
	{
		var turns = yaw * 0.15915494f;
		var fraction = turns - MathF.Truncate( turns );
		return fraction < 0f ? fraction + 1f : fraction;
	}

	/// <summary>
	/// The side term of a loop pylon's knot factor: the sum of the table sine and cosine of the node's turn fraction (negated) with the unit direction of the node's horizontal link, before
	/// its absolute value and clamp (<see cref="HermiteCoasterCurve.LoopKnotFactor"/>).
	/// </summary>
	// [BIN:TPW-EXE:0x004324a0 loop knot side] the link is the raw previous node (+0x68), or the next one (+0x64) with node flag 0x80; (dx, dz) is its position minus the node's (+0x4c, +0x54), (0, 1) without a link; u = (dx, dz)/sqrt(dx² + dz²) with each component a single, (0, 1) when the quotient is NaN (0x4324be..0x432516); i1 = round(f32(f32(turn · 2π) · scale)) and i2 = round(f32(f32(f32(turn · 2π) − (−π/2)) · scale)) index the PC table (0x432519..0x43255c), the side is ((−T[i1]) · u.x) + ((−T[i2]) · u.z) (0x43256e..0x432582)
	public static float LoopSide( float turnFraction, float dx, float dz, bool hasLink )
	{
		if ( !hasLink )
			(dx, dz) = (0f, 1f);
		var length = MathF.Sqrt( dx * dx + dz * dz );
		float ux = dx / length, uz = dz / length;
		if ( float.IsNaN( ux ) )
			(ux, uz) = (0f, 1f);
		var angle = turnFraction * 6.2831855f;
		var first = (int)MathF.Round( angle * CoasterTrig.PcScale ) & (CoasterTrig.Size - 1);
		var second = (int)MathF.Round( (angle - -1.5707964f) * CoasterTrig.PcScale ) & (CoasterTrig.Size - 1);
		return CoasterTrig.PcEntry( first ) * -1f * ux + CoasterTrig.PcEntry( second ) * -1f * uz;
	}

	/// <summary>
	/// The C library's acos (<c>0x67b4e0</c>) called from the self-intersection pass: its operations round to nearest, as in <see cref="Acos"/> (the routine sets the control word to its own
	/// exceptions-masked value with the rounding control cleared, 0x686185, and restores the pass's word after the fpatan); the extended result is returned unrounded, held in a double, because the
	/// pass adds it to its running sum without storing it.
	/// </summary>
	// [BIN:TPW-EXE:0x00686185 acos rounding] edx = (control word & 0x300) | 0x7f: the precision stays at 24 bits and the rounding control becomes nearest for the routine's own 1 + x, 1 − x, product and square root (0x67b518..0x67b522); the pass's truncating word returns at 0x68620e
	public static double AcosTruncated( float x )
	{
		if ( x >= 1f )
			return x == 1f ? 0d : double.NaN;
		if ( x <= -1f )
			return x == -1f ? Math.PI : double.NaN;
		var root = MathF.Sqrt( (1f + x) * (1f - x) );
		// [EXT:deterministic-math] fpatan in IEEE double by DetMath, the same bits on every runtime, not in x87 extended precision (docs/DETERMINISM.md, "Browser and native")
		return DetMath.Atan2( root, x );
	}
}

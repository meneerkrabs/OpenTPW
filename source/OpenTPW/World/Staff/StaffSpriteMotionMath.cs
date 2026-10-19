namespace OpenTPW;

/// <summary>Pure staff movement observations. These helpers never update a person, source, clone, clock or random stream.</summary>
public static class StaffSpriteMotionMath
{
	// Generated from the mathematical definition, never loaded or copied from an executable table.
	private static readonly int[] octantAngles = CreateOctantAngles();

	private static int[] CreateOctantAngles()
	{
		var angles = new int[257];
		for ( var i = 1; i < 256; i++ )
			angles[i] = (int)Math.Floor( Math.Atan( i / 256d ) * 1024d / Math.PI );
		angles[0] = 0;
		angles[256] = 256;
		return angles;
	}

	/// <summary>
	/// Observes an actual continuing movement branch. Zero displacement succeeds with angle zero;
	/// arrived and stuck early returns must not invoke this helper. The returned angle is not a published heading.
	/// False names the owner's InvalidAngleArithmetic case: preserve the previous person angle and ignore the out value.
	/// Other movement requests still proceed. Only the two minimum-int axis differences can return false.
	/// </summary>
	public static bool TryMovementAngle( int beforeX, int beforeY, int afterX, int afterY, out int angle )
	{
		var a = unchecked( beforeX - afterX );
		var b = unchecked( afterY - beforeY );
		angle = 0;
		if ( a == 0 && b == 0 )
			return true;

		var negativeA = a < 0;
		var negativeB = b < 0;
		var magnitudeA = negativeA ? unchecked( -a ) : a;
		var magnitudeB = negativeB ? unchecked( -b ) : b;
		// The comparison is signed: wrapped negation leaves minimum-int negative.
		var first = magnitudeA >= magnitudeB;
		var low = first ? magnitudeB : magnitudeA;
		var high = first ? magnitudeA : magnitudeB;
		var numerator = unchecked( (uint)low << 8 );
		var denominator = unchecked( (uint)high );
		// [APPROX:UI-092] the two native integer-divide errors become a named owner diagnostic, with no angle write.
		if ( denominator == 0 )
			return false;

		// Shifting before any widening is intentional. All supported input pairs produce an index in 0..256.
		var q = octantAngles[(int)(numerator / denominator)];
		angle = (negativeA, negativeB, first) switch
		{
			(false, false, true) => 512 + q,
			(false, false, false) => 1024 - q,
			(false, true, true) => 512 - q,
			(false, true, false) => q,
			(true, false, true) => 1536 - q,
			(true, false, false) => 1024 + q,
			(true, true, true) => 1536 + q,
			(true, true, false) => 2048 - q
		};
		angle &= 2047;
		return true;
	}

	/// <summary>Publishes a semantic person angle as an eight-way source sector, without mutating any sprite state.</summary>
	public static int Heading( int angle ) => (unchecked( angle - 0x380 ) & 0x7ff) >> 8;

	/// <summary>Uses the already-consumed strike random word; unsigned saturation is deliberately not angle wrapping.</summary>
	public static int StrikeAngle( int angle, uint random )
	{
		if ( (random & 7) != 0 )
			return angle;
		var adjusted = unchecked( angle + (int)(random & 0xff) - 128 );
		return unchecked( (uint)adjusted ) > 0x7ff ? 0x7ff : adjusted;
	}

	/// <summary>
	/// Shares the unchanged guest interval calculation: wrapped signed squared distance, double sqrt,
	/// binary32 speed (then optional doubling), double quotient, truncation and a 250 cap.
	/// Nonpositive wrapped sums return safe zero, the source wrapper's no-request sentinel.
	/// </summary>
	// [APPROX:UI-092] final intervals select nearest p53/p64-compatible arithmetic; native floating environment is unproven.
	// Safe zero for invalid/zero sums agrees with the native conversion only under masked floating exceptions.
	public static int IntervalForStep( int beforeX, int beforeY, int afterX, int afterY, bool run ) =>
		GuestSimulation.BodyIntervalForStep( beforeX, beforeY, afterX, afterY, run );
}

namespace OpenTPW;

/// <summary>The cost of goods a shop books for one sale (tp.exe <c>0x004E1B40</c>).</summary>
public static class ShopGoodsCost
{
	private const double Center = 50.0;
	private const double StepPerPoint = (double)0.005f;

	/// <summary>
	/// The value the use routine 0x004FE1E0 books for a shop sale: the debit 0x004D01F0, the object's cost rings (+0xF8, +0x184) and the challenge types 12 and 13
	/// all take it. It is <paramref name="costOfGoods"/> (+0x188, read as an unsigned 64-bit integer) times 1 plus two terms, each (byte − 50) × 0.005 as a single
	/// clamped to ±0.5: the quality byte (+0x18C) and the special-ingredient amount byte (+0x198), the second with its sign flipped when the type's special
	/// ingredient (+0x158) is 1 or 3; the product is truncated toward zero.
	/// </summary>
	// [BIN:TPW-EXE:0x004E1B40 shop cost of goods] x87 sequence: both bytes minus 50.0 (0x7005B4), times 0.005f (0x7005CC), each product stored as a single and clamped (below -0.5, above 0.5); ingredient 1 or 3 multiplies its term by -1.0 (0x700588); the result is cost × ((quality + ingredient) − (−1.0)) through __ftol (0x67A830)
	// [BIN:TPW-EXE:0x004E1A10 sideshow cost] a sideshow (WhichUIType 2) books mCostOfGoods as it stands
	// [APPROX:ECON-059] the sum, the factor and the product are rounded to a single after each step, as the x87 does with the precision control of DDSCL_FPUSETUP (single, the flags 0x851 and 0x808 of the SetCooperativeLevel calls 0x563914 and 0x563B7A), but the control word the simulation thread runs with is not captured (the renderer 0x576EC0 alone sets the precision field: single through 0x591220, the old word back through 0x591240, or through 0x591250 with extended precision when bit 1 of the render flags word 0x8BD508 is set; a control-word guard class 0x62F980 / 0x62F9A0 is untraced; DirectDraw's own effect is the system's) — evidence needed: the control word of the original's simulation thread, or a captured cost of goods with a quality or ingredient byte other than 50, what sets bit 1 of 0x8BD508 and the guard's masks
	public static int Of( int costOfGoods, int quality, int ingredientAmount, int specialIngredient )
	{
		// (byte − 50) is an exact integer and 0.005f has 24 bits, so the product is exact in a double and rounds once to the single the x87 stores
		var qualityTerm = Clamp( (float)(((quality & 0xFF) - Center) * StepPerPoint) );
		var ingredientTerm = Clamp( (float)(((ingredientAmount & 0xFF) - Center) * StepPerPoint) );
		if ( specialIngredient is 1 or 3 )
			ingredientTerm = -ingredientTerm;
		// two singles add exactly in a double and round once; the same holds for the + 1
		var sum = (float)((double)qualityTerm + ingredientTerm);
		var factor = (float)((double)sum + 1.0);
		return unchecked( (int)(long)MultiplyAsSingle( (uint)costOfGoods, factor ) );
	}

	private static float Clamp( float term ) => term < -0.5f ? -0.5f : term <= 0.5f ? term : 0.5f;

	/// <summary>cost × factor rounded to a single (nearest even), the cost taken as the exact integer the x87 loads; the factor is positive.</summary>
	private static float MultiplyAsSingle( uint cost, float factor )
	{
		var bits = BitConverter.SingleToInt32Bits( factor );
		var significand = (ulong)((bits & 0x7FFFFF) | 0x800000);
		var exponent = ((bits >> 23) & 0xFF) - 150;
		var product = (UInt128)cost * significand;
		var length = 128 - (int)UInt128.LeadingZeroCount( product );
		var shift = Math.Max( length - 24, 0 );
		var kept = (ulong)(product >> shift);
		if ( shift > 0 )
		{
			var rest = product & ((UInt128.One << shift) - 1);
			var half = UInt128.One << (shift - 1);
			if ( rest > half || rest == half && (kept & 1) == 1 )
				kept++;
		}
		return (float)Math.ScaleB( kept, exponent + shift );
	}
}

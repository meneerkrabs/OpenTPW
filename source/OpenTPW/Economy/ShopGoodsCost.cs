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
	// [BIN:TPW-EXE:0x00576EC0 render precision] the renderer saves the control word and clears the precision field at its entry (0x00591220, the only caller), and its normal exit (0x005772E2) puts the saved word back through 0x00591240, or through 0x00591250 (the word with the precision bits 0x300 set) when bit 1 of 0x008BD508 & 0x3FFFFFFF is set, while the exit at 0x00577433 (a failed piece 0x00576A00) leaves the field cleared; a call from another thread than the render thread (the id 0x008BD518) returns before touching it
	// [BIN:TPW-EXE:0x00563914 control word] the simulation runs on the main thread (the only thread starters are _beginthread 0x67E050 and _beginthreadex 0x67F780, called from the library helpers 0x5C8B70, 0x5FBC4E and 0x632639, none of them the economy), the thread DirectDraw's SetCooperativeLevel with DDSCL_FPUSETUP (0x851 at 0x563914, 0x808 at 0x563B7A) and the shipped D3DIM device creation leave at the control word 0x7F, so every x87 step of this routine rounds to a single (24 bits, COAST-032, X87Precision)
	// [BIN:TPW-EXE:0x008BD508 render-state word] nothing sets bit 1 of the word: its initial value is 0x3000141 (0x583555) and the writers only OR 0x80000000, 0x40000000, 0x10000000, 0x8000000, 0x40, 0x1 (0x54E851, EBX = 1) or 0x100, or clear bits, so the renderer's normal exit always restores the saved word (0x591240, never the extended-precision restore 0x591250), and its failure exit 0x577433 leaves the same 24-bit precision; no path leaves the thread at another precision
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

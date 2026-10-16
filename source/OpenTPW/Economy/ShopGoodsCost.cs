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
	// [BIN:TPW-EXE:0x004E1B40 shop cost of goods] x87 sequence: both bytes minus 50.0 (0x7005B4), times 0.005f (0x7005CC), the quality product stored as a single and clamped (below -0.5 on the unrounded product, above 0.5 on the single), the ingredient product stored as a single and clamped on the single; ingredient 1 or 3 multiplies its term by -1.0 (0x700588); the result is cost × (quality + ingredient − (−1.0)) through __ftol (0x67A830)
	// [BIN:TPW-EXE:0x004E1A10 sideshow cost] a sideshow (WhichUIType 2) books mCostOfGoods as it stands
	public static int Of( int costOfGoods, int quality, int ingredientAmount, int specialIngredient )
	{
		var qualityProduct = ((quality & 0xFF) - Center) * StepPerPoint;
		var qualityTerm = (float)qualityProduct;
		var ingredientTerm = (float)(((ingredientAmount & 0xFF) - Center) * StepPerPoint);
		qualityTerm = qualityProduct < -0.5 ? -0.5f : qualityTerm <= 0.5f ? qualityTerm : 0.5f;
		ingredientTerm = ingredientTerm < -0.5f ? -0.5f : ingredientTerm <= 0.5f ? ingredientTerm : 0.5f;
		var ingredient = specialIngredient is 1 or 3 ? -1.0 * ingredientTerm : ingredientTerm;
		return unchecked( (int)(long)((double)(uint)costOfGoods * (qualityTerm + ingredient + 1.0)) );
	}
}

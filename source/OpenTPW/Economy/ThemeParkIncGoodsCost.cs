namespace OpenTPW;

/// <summary>The cost of goods a Theme Park Inc shop books for one sale (Game.exe <c>0x5E63E0</c> through <c>0x5E6260</c>).</summary>
public static class ThemeParkIncGoodsCost
{
	/// <summary>The thing's third slider byte (<c>+0x9C</c>): 50 when the thing is built (<c>0x5DB29E</c>), and nothing in OpenTPW changes it.</summary>
	public const int BuiltThirdByte = 50;

	private const double Center = 50.0;
	private static readonly double StepPerPoint = 0.005f;
	private static readonly double ThirdStep = 0.01f;

	/// <summary>
	/// The value a shop sale books (thing flag kind 1): the cost of goods (<c>+0x88</c>) times 1 plus two terms, each (byte - 50) x 0.005 as a single clamped to +-0.5, the quality byte (<c>+0x8C</c>) and the
	/// special-ingredient amount byte (<c>+0x98</c>, its term negated when the type's special ingredient (<c>+0x218</c>) is 1 or 3), times a share: 1 for a shop type (<c>+0x224</c>) of 1, 5, 6, 7 or 8, else
	/// the third byte (<c>+0x9C</c>) x 0.01 as a single; the product is truncated toward zero and a result of 0 becomes 1.
	/// </summary>
	// [BIN:TPI-EXE:0x5E6260 shop cost] x87 sequence checked against the executable in Unicorn on 400 random inputs at both precisions: both bytes minus 50.0 (0x93B89C) times 0.005f (0x93AB28), each stored as a single and clamped to -0.5 (0x93E3A4) and 0.5 (0x93AA78); the share from a negative third argument is the double 1.0 (0x93B858) stored as a single, else the byte times 0.01f (0x93AD04) stored as a single; the ingredient term times -1.0 (0x93ABC8) for the special ingredient 1 or 3 (0x5E6340..0x5E6347); ((term + term) + 1.0 (0x93A034)) x the integer cost x the share through _ftol (0x86FAB0); a 0 becomes 1 (0x5E637A..0x5E637F, unsigned above)
	// [BIN:TPI-EXE:0x5E63E0 shop cost arguments] the arguments are the quality byte, the ingredient byte and the third byte; a type whose +0x224 is 1, 5, 6, 7 or 8 passes -1.0 for the third (0x5E6407..0x5E6418)
	// [BIN:TPI-EXE:0x5BCD60 precision] the economy's arithmetic is read at 53 bits, as the oracle reads the arrival group's (X7); the routine's 400 random inputs agree at 24 bits as well
	public static int Of( int costOfGoods, int quality, int ingredient, int third, int specialIngredient, int shopType )
	{
		var qualityTerm = Clamp( (float)(((quality & 0xFF) - Center) * StepPerPoint) );
		var ingredientTerm = Clamp( (float)(((ingredient & 0xFF) - Center) * StepPerPoint) );
		var share = shopType is 1 or 5 or 6 or 7 or 8 ? 1.0f : (float)(third * ThirdStep);
		if ( specialIngredient is 1 or 3 )
			ingredientTerm = -ingredientTerm;
		var product = (((double)ingredientTerm + qualityTerm) + 1.0) * costOfGoods * share;
		var value = unchecked( (int)(long)product );
		return value == 0 ? 1 : value;
	}

	private static float Clamp( float term ) => term < -0.5f ? -0.5f : term <= 0.5f ? term : 0.5f;
}

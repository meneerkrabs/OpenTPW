namespace OpenTPW;

/// <summary>The values a new guest starts with.</summary>
/// <param name="Type">The <c>PeepTypes</c> row (guest byte <c>+496</c>).</param>
/// <param name="Money">The starting cash (<c>+416</c>).</param>
/// <param name="Happiness">The happiness meter (<c>+412</c>).</param>
/// <param name="Thirst">The thirst meter (<c>+420</c>).</param>
/// <param name="Hunger">The hunger meter (<c>+424</c>).</param>
/// <param name="Toilet">The toilet meter (<c>+428</c>).</param>
/// <param name="Illness">The illness meter (<c>+432</c>).</param>
/// <param name="Litter">The litter meter (<c>+436</c>).</param>
/// <param name="ExitLevel">The exit counter (<c>+444</c>), counted down once per needs phase (about one second).</param>
public readonly record struct GuestStartValues( int Type, int Money, float Happiness, float Thirst, float Hunger, float Toilet, float Illness, float Litter, int ExitLevel );

/// <summary>
/// The starting values of a guest (the person constructor <c>0xe7644</c>, docs/reverse/ORACLE.md F25): happiness 50, thirst and hunger a draw modulo 50, toilet a
/// draw modulo 30, illness and litter 0, the exit counter <c>PeepInfo.ExitLevel</c> plus a draw modulo twice <c>ExitLevelVar</c> minus <c>ExitLevelVar</c>, the
/// type a draw modulo the type count and the cash the type's <c>StartingCash</c> scaled by 100 plus a draw modulo <c>2 · StartingCashVarPc + 1</c> minus
/// <c>StartingCashVarPc</c> percent. The original takes the draws, in this order, from the world generator (<c>0x105328</c>); OpenTPW feeds words from the
/// guest stream (DET-014). The original also draws a prankery flag (the eighth draw) which OpenTPW does not model.
/// </summary>
public static class GuestStart
{
	/// <summary>The starting happiness (the float at TOC −10864, stored at <c>0xe7820</c>).</summary>
	public const float StartingHappiness = 50f;
	/// <summary>Thirst and hunger start at a draw modulo this (<c>0xe7844</c>, <c>0xe7870</c>).</summary>
	public const uint ThirstHungerModulus = 50;
	/// <summary>Toilet starts at a draw modulo this (<c>0xe78a4</c>).</summary>
	public const uint ToiletModulus = 30;
	/// <summary>The constructor takes this many draws; the seventh is dropped and the eighth picks the prankery flag.</summary>
	public const int Draws = 8;

	/// <summary>
	/// The starting values from <paramref name="draw"/>, which must give non-negative words (the world generator returns the absolute value of its state).
	/// An <paramref name="exitLevelVariation"/> of 0 is the original's division by zero (undefined); here it adds nothing.
	/// </summary>
	// [BIN:STP-PPC:0x100E7644 guest constructor] executed by the PowerPC oracle (guest-constructor, 517 inputs; C# agrees on the compared values)
	// [APPROX:DET-014] the draws come from OpenTPW's guest stream, not the original's world generator reseeded per guest — evidence needed: DET-I2 port of WorldRng
	// [APPROX:GUEST-007] the eighth draw's prankery flag (guest byte +448: 100 + id modulo 3 with PrankeryLikelihood percent) is not modelled. Its reader is the state-6 handler (tp.exe 0x004FEC90): a guest whose happiness byte is below 15 and whose flag beats rand % 101 pranks. Flag 100 drops litter of type 8 with a sound when rand % 100 is below a settings word (0x007850E0), 101 drops litter of a random type 1..5 (0x004D93B0, "Littering cell"), and 102 picks on another guest on its cell that carries a +0x210 object (0x004FE950). A prank records event 15 and raises happiness by 1. OpenTPW has no separate state 6 (GUEST-010) and keeps litter park-wide (ECON-020) — evidence needed: the settings key behind 0x007850E0 and what 0x004FE950 does to the other guest
	public static GuestStartValues FromDraws( IReadOnlyList<GuestType> types, int exitLevel, int exitLevelVariation, int startingCashVariationPercent, Func<uint> draw )
	{
		var exitDraw = draw();
		var typeDraw = draw();
		var cashDraw = draw();
		var thirstDraw = draw();
		var hungerDraw = draw();
		var toiletDraw = draw();
		draw();
		draw();
		var span = 2 * exitLevelVariation;
		var exit = unchecked( exitLevel + (span == 0 ? 0 : (int)exitDraw % span) - exitLevelVariation );
		var type = (int)typeDraw % types.Count;
		var delta = (int)cashDraw % (2 * startingCashVariationPercent + 1) - startingCashVariationPercent;
		var money = Math.Max( 0, unchecked( types[type].StartingCash * (100 + delta) ) / 100 );
		return new GuestStartValues( type, money, StartingHappiness, (float)(thirstDraw % ThirstHungerModulus), (float)(hungerDraw % ThirstHungerModulus),
			(float)(toiletDraw % ToiletModulus), 0f, 0f, exit );
	}
}

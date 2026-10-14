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
/// <param name="PrankFlag">The prankery flag (<c>+448</c>, tp.exe <c>+0x1C0</c>): 0, or 100 + the thing id modulo 3 for a prankster.</param>
public readonly record struct GuestStartValues( int Type, int Money, float Happiness, float Thirst, float Hunger, float Toilet, float Illness, float Litter, int ExitLevel, int PrankFlag = 0 );

/// <summary>
/// The starting values of a guest (the person constructor <c>0xe7644</c>, docs/reverse/ORACLE.md F25): happiness 50, thirst and hunger a draw modulo 50, toilet a
/// draw modulo 30, illness and litter 0, the exit counter <c>PeepInfo.ExitLevel</c> plus a draw modulo twice <c>ExitLevelVar</c> minus <c>ExitLevelVar</c>, the
/// type a draw modulo the type count and the cash the type's <c>StartingCash</c> scaled by 100 plus a draw modulo <c>2 · StartingCashVarPc + 1</c> minus
/// <c>StartingCashVarPc</c> percent. The original takes the draws, in this order, from the world generator (<c>0x105328</c>); OpenTPW feeds words from the
/// guest stream (DET-014). The eighth draw picks the prankery flag.
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
	// [BIN:TPW-EXE:0x004FB114 prankery flag] the eighth draw: +0x1C0 = 100 when draw % 100 is below PrankeryLikelihood (0x007850DC), else 0; a 100 then adds the thing id (0x0050B350) modulo 3, both unsigned
	public static GuestStartValues FromDraws( IReadOnlyList<GuestType> types, int exitLevel, int exitLevelVariation, int startingCashVariationPercent, Func<uint> draw,
		int prankeryLikelihood = 0, int thingId = 0 )
	{
		var exitDraw = draw();
		var typeDraw = draw();
		var cashDraw = draw();
		var thirstDraw = draw();
		var hungerDraw = draw();
		var toiletDraw = draw();
		draw();
		var prankDraw = draw();
		var span = 2 * exitLevelVariation;
		var exit = unchecked( exitLevel + (span == 0 ? 0 : (int)exitDraw % span) - exitLevelVariation );
		var type = (int)typeDraw % types.Count;
		var delta = (int)cashDraw % (2 * startingCashVariationPercent + 1) - startingCashVariationPercent;
		var money = Math.Max( 0, unchecked( types[type].StartingCash * (100 + delta) ) / 100 );
		return new GuestStartValues( type, money, StartingHappiness, (float)(thirstDraw % ThirstHungerModulus), (float)(hungerDraw % ThirstHungerModulus),
			(float)(toiletDraw % ToiletModulus), 0f, 0f, exit, PrankFlag( prankDraw, prankeryLikelihood, thingId ) );
	}

	/// <summary>The prankery flag of the eighth draw: 100, 101 or 102 (stinkbombs, litter, balloons) for a prankster, else 0.</summary>
	public static int PrankFlag( uint draw, int prankeryLikelihood, int thingId )
	{
		if ( draw % 100 >= unchecked( (uint)prankeryLikelihood ) )
			return 0;
		return 100 + (int)(unchecked( (uint)(ushort)thingId ) % 3);
	}
}

namespace OpenTPW;

/// <summary>The guest meters' arithmetic: single precision, clamped to 0..100 the way every need and happiness write of the original is.</summary>
public static class MeterMath
{
	/// <summary>
	/// The clamp of <c>0xea3c0..0xea3ec</c> and its copies: above 100 gives 100, below 0 gives 0, anything else (a NaN too) is stored as it is.
	/// </summary>
	public static float Clamp( float value ) => value > 100f ? 100f : value < 0f ? 0f : value;

	/// <summary><c>clamp(meter + delta)</c> with the sum rounded to a single like the original's <c>fadds</c>.</summary>
	public static float Add( float meter, float delta ) => Clamp( (float)(meter + delta) );

	/// <summary>The byte of the meter truncated to an integer (<c>fctiwz</c>, then the low byte): how the original tests its meters.</summary>
	public static int Byte( float meter ) => QueueJoinRules.Truncate( meter ) & 0xFF;
}

/// <summary>
/// What using an attraction does to the guest's happiness and illness (<c>0xea318</c>, docs/reverse/ORACLE.md F24). Nothing happens when the
/// attraction's live excitement byte is 0. Otherwise the difference between the guest's preferred and that excitement picks the happiness
/// gain (<c>PeepInfo.PerfectRide</c> below 5, <c>GoodRide</c> below 15, <c>OKRide</c> below 40, nothing from 40 on: there is no penalty), and the
/// illness rises by <c>trunc(excitement / RideVomitDivisor) · trunc((100 − byte(hunger)) / 20)</c>, so a guest with a full stomach is sick more
/// readily than a hungry one (hunger 81 and up: not at all).
/// </summary>
public static class RideOutcome
{
	/// <summary>
	/// The new happiness and illness. Throws <see cref="DivideByZeroException"/> for a divisor of 0 (the original's <c>divw</c> is undefined there;
	/// <see cref="GuestSettings.RideVomitDivisor"/> is at least 1).
	/// </summary>
	// [BIN:STP-PPC:0x100EA318 ride outcome] bands: cmpwi 5, 15, 40 with bc 4,0 (strictly below); weights +36/+40/+44 and +48 of the settings block (the PeepInfo keys in file order); no branch for a larger difference
	public static (float Happiness, float Illness) Apply( float happiness, float hunger, float illness, int preferredExcitement, int excitement,
		int perfectRide, int goodRide, int okRide, int vomitDivisor )
	{
		var live = excitement & 0xFF;
		if ( live == 0 )
			return (happiness, illness);
		var difference = Math.Abs( (preferredExcitement & 0xFF) - live );
		if ( difference < 5 )
			happiness = MeterMath.Add( happiness, perfectRide );
		else if ( difference < 15 )
			happiness = MeterMath.Add( happiness, goodRide );
		else if ( difference < 40 )
			happiness = MeterMath.Add( happiness, okRide );
		var fed = (100 - MeterMath.Byte( hunger )) / 20;
		var gain = unchecked( live / vomitDivisor * fed );
		return (happiness, MeterMath.Add( illness, gain ));
	}
}

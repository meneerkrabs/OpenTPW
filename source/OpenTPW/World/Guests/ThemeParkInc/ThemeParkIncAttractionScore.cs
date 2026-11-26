namespace OpenTPW;

/// <summary>
/// The words of an attraction's type that Theme Park Inc's score reads besides Theme Park World's (the type record through <c>0x5DF6D0</c>; the keys follow the key table at <c>0x9A4520</c>):
/// <c>Info.GenderSpecific</c> (<c>+0x78</c>), <c>UsageInfo.AppearanceEffect</c> (<c>+0x21C</c>: 1 a balloon shop, 3 an umbrella shop), <c>UsageInfo.IsInfoDesk</c> (<c>+0x244</c>), the
/// <c>WeatherModifier.Snow/Rain/Storm/HeatWavePercentage</c> words (<c>+0xF0..+0xFC</c>) and the <c>GenerationModifier.Kid/Parent/GrandparentScorePercentage</c> words (<c>+0x110..+0x118</c>).
/// </summary>
public readonly record struct ThemeParkIncScoreTerms( int GenderSpecific, int AppearanceEffect, bool IsInfoDesk, int SnowPercentage, int RainPercentage, int StormPercentage,
	int HeatWavePercentage, int KidPercentage, int ParentPercentage, int GrandparentPercentage );

/// <summary>
/// Everything Theme Park Inc's attraction score <c>0x6101E0</c> reads, in the game's own units. The guest's cell and the back-of-queue cell are cell bytes; the meters are the guest's floats; the
/// weights and decision variables are the <c>PeepInfo</c> words (<c>0xA01AA4..0xA01ADC</c>).
/// </summary>
/// <param name="Special">The thing is a special ride awaiting its activation (<c>0x6242F0</c> does not free it, the test at <c>0x6101FD</c>): the score is 0.</param>
/// <param name="Female">The guest's sprite bank is female (<c>0x68E7B0</c>): a <c>GenderSpecific</c> type of 2 takes women only, 1 men only.</param>
/// <param name="SameTypeAsLastUsed">The thing in used-history slot 0 (<c>+0x20A</c>) exists and has this thing's type (<c>+0x16</c>): the score is 0.</param>
/// <param name="UmbrellaState">The guest's umbrella state <c>+0x238</c>.</param>
/// <param name="Weather">The weather word <c>+0x70</c> of <c>0x64EB80</c> (1 heat wave, 2 rain, 3 storm, 4 snow).</param>
/// <param name="HoldsBalloon">The balloon object <c>+0x294</c> is set.</param>
/// <param name="BalloonCountdown">The balloon countdown <c>+0x298</c>.</param>
/// <param name="CellModifier">The signed word <c>+8</c> of the region record at the back cell (<c>0x5D7420</c>, the fireworks' RegionFX row); 0 divides nothing.</param>
/// <param name="QueueCount">The queue's length (<c>0x5E0260</c>).</param>
/// <param name="QueueCells">The queue size in cells (thing <c>+0x50</c>); 0 counts as 1.</param>
/// <param name="Rating">The attraction's rating byte (<c>0x5E41D0(0)</c>).</param>
/// <param name="RatingGate">The low byte of the type's <c>ExcitementLevel</c> is nonzero (<c>0x5E41D0(1)</c>): the excitement weight counts only then.</param>
/// <param name="PreferredExcitement">The guest's preferred excitement byte <c>+0x2A8</c>.</param>
/// <param name="Relief">The thing flag <c>+0x42</c> bit 1 (<c>UsageInfo.ProvidesRelief</c>): the toilet and illness matches count only then.</param>
/// <param name="AgeDays">The days since the thing was bought (<c>0x5DF8D0</c>).</param>
/// <param name="GoldenTicketCost">Type <c>UsageInfo.GoldenTicketCost</c> (<c>+0x174</c>).</param>
/// <param name="UpgradeCost">Type <c>+0x2AC</c> (<c>Upgrades[0].CostOfUpgrade</c>).</param>
/// <param name="Generation">The guest's generation <c>+0x2A4</c>.</param>
/// <param name="DiscardBalloonPercentage">The word <c>0xA01B68</c> (<c>PeepInfo.DiscardBalloonPctModifier</c>).</param>
/// <param name="ThingId">The thing id the histories hold.</param>
public readonly record struct ThemeParkIncAttractionScoreInput( bool Special, bool Female, bool SameTypeAsLastUsed, ThemeParkIncScoreTerms Terms, int UmbrellaState, int Weather,
	bool HoldsBalloon, uint BalloonCountdown, int GuestX, int GuestY, int BackX, int BackY, int CellModifier, int QueueCount, int QueueCells, int Rating, bool RatingGate,
	int PreferredExcitement, float Thirst, float Hunger, float Toilet, float Illness, int ThirstEffect, int HungerEffect, bool Relief, uint AgeDays, int GoldenTicketCost, int UpgradeCost,
	int Generation, uint DiscardBalloonPercentage, int ThingId, IReadOnlyList<int> UsedHistory, IReadOnlyList<int> TemporaryHistory );

/// <summary>
/// Theme Park Inc's attraction score <c>0x6101E0</c> (docs/reverse/TPI-GUESTS.md, phase P3a): Theme Park World's seven matches and history divisions (<see cref="AttractionScore"/>, tp.exe
/// <c>0x004FCC30</c>) with Theme Park Inc's own gates (special rides, a gender, the balloon and umbrella shops), weights, golden-ticket and upgrade scales in the game thread's 24-bit precision,
/// the weather and generation percentages and no indoor-rain rule (<c>DecisionVariable3</c> is unread). Run on the executable by the native oracle.
/// </summary>
public static class ThemeParkIncAttractionScore
{
	/// <summary>The balloon factor's divisor, the byte <c>0x9D0610</c> (255).</summary>
	// [BIN:TPI-EXE:0x9D0610 balloon divisor] the dword 0x000000FF, read by divl at 0x610B27
	public const uint BalloonDivisor = 255;

	/// <summary>
	/// The score, 0 when a gate refuses the attraction or the weights add up to 0 (the original's <c>divl</c> by 0 would fault: the shipped weights never do). An information desk is not scored here:
	/// the caller scores the object it names (<c>0x5E8FE0</c>), or refuses it.
	/// </summary>
	// [BIN:TPI-EXE:0x6101E0 attraction score] the gates 0x6101FD (special), 0x61021C (GenderSpecific: female banks 2, male 1), 0x610348 (used slot 0 of the same type +0x16), 0x610387 (an umbrella shop with
	// any umbrella state, or in weather other than 2, 3 and 4), 0x6103EC (a balloon shop with a balloon or countdown, or an umbrella state 3 or 4); then 0x610435 the squared distance d, 100 − min(100, 100d / 450)
	// (signed) divided by the region word +8 (unsigned) when nonzero; d at most 8 (signed): the queue 100 − 100 × count / (4 × max(+0x50, 1)) (unsigned) with the weight 0xA01AC8 (32 bits), else 0 and 0;
	// excitement 100 − 2 × min(50, |+0x2A8 − rating|) with 0xA01ACC & 0xFFFF when the gate holds; thirst and hunger the table 0x93E5E8 at 11 × (effect / 10) + byte / 10; toilet and illness the table
	// 0x93E664 at (byte + 4) / 5 for a relief thing (else 0); the sum of weight × match over the sum of the weights (unsigned, the six other weights & 0xFFFF)
	// [BIN:TPI-EXE:0x6108FA score scales] age (0x5DF8D0) at most 0xA01AA4 (unsigned): × 0xA01AA8; GoldenTicketCost above 0 (signed): _ftol(((cost + 1) × 0.1 + 1.0) × score); else a nonzero +0x2AC:
	// q = cost × (1/3000)f, above 1.0: _ftol((q × 0.1f + 1.0f) × score); each x87 step rounded to 24 bits; then the weather word of the weather (+0xF4 rain, +0xF0 snow, +0xFC heat wave, +0xF8 storm)
	// when nonzero, score × w / 100 (signed); an umbrella shop with a balloon or countdown and a nonzero countdown: f = 100 − 100 × countdown / 255 (unsigned), f × 0xA01B68 / 100 when that is
	// nonzero, score × f / 100 (unsigned); the generation word (+0x110 kid, +0x114 parent, +0x118 grandparent), score × w / 100 (signed); the history divisions as tp.exe's
	public static uint Compute( in ThemeParkIncAttractionScoreInput input, DecisionWeights weights, DecisionVariables variables )
	{
		var terms = input.Terms;
		if ( input.Special )
			return 0;
		if ( terms.GenderSpecific != 0 && terms.GenderSpecific != (input.Female ? 2 : 1) )
			return 0;
		if ( input.SameTypeAsLastUsed )
			return 0;
		if ( terms.AppearanceEffect == 3 && (input.UmbrellaState != 0 || input.Weather is not (2 or 3 or 4)) )
			return 0;
		if ( terms.AppearanceEffect == 1 && (input.HoldsBalloon || input.BalloonCountdown > 0 || input.UmbrellaState is 3 or 4) )
			return 0;
		unchecked
		{
			var dx = input.GuestX - input.BackX;
			var dy = input.GuestY - input.BackY;
			var squared = dx * dx + dy * dy;
			var distance = (uint)(100 - Math.Min( 100u, (uint)(squared * 100 / AttractionScore.DistanceDivisor) ));
			if ( input.CellModifier != 0 )
				distance /= (uint)input.CellModifier;
			uint queueMatch = 0, queueWeight = 0;
			if ( squared <= AttractionScore.QueueRangeSquared )
			{
				var cells = input.QueueCells == 0 ? 1u : (uint)input.QueueCells;
				queueMatch = 100u - (uint)input.QueueCount * 100u / (cells * 4u);
				queueWeight = weights.Queue;
			}
			var difference = Math.Clamp( Math.Abs( (input.PreferredExcitement & 0xFF) - (input.Rating & 0xFF) ), 0, 50 );
			var excitementMatch = (uint)(100 - 2 * difference);
			var excitementWeight = input.RatingGate ? weights.Excitement & 0xFFFF : 0u;
			var thirstMatch = (uint)AttractionScore.NeedMatch( input.Thirst, input.ThirstEffect );
			var hungerMatch = (uint)AttractionScore.NeedMatch( input.Hunger, input.HungerEffect );
			var toiletMatch = (uint)AttractionScore.ReliefMatch( input.Toilet, input.Relief );
			var illnessMatch = (uint)AttractionScore.ReliefMatch( input.Illness, input.Relief );
			var distanceWeight = weights.Distance & 0xFFFF;
			var thirstWeight = weights.Thirst & 0xFFFF;
			var hungerWeight = weights.Hunger & 0xFFFF;
			var toiletWeight = weights.Toilet & 0xFFFF;
			var illnessWeight = weights.Illness & 0xFFFF;
			var numerator = distanceWeight * distance + excitementWeight * excitementMatch + queueWeight * queueMatch + illnessWeight * illnessMatch + toiletWeight * toiletMatch
				+ hungerWeight * hungerMatch + thirstWeight * thirstMatch;
			var denominator = distanceWeight + thirstWeight + hungerWeight + toiletWeight + illnessWeight + excitementWeight + queueWeight;
			if ( denominator == 0 )
				return 0;
			var score = numerator / denominator;
			if ( input.AgeDays <= variables.NewRideDays )
				score *= variables.NewRideMultiplier;
			if ( input.GoldenTicketCost > 0 )
				score = (uint)ThemeParkIncX87Single.Truncate( (float)(GoldenTicketFactor( input.GoldenTicketCost ) * (double)(int)score) );
			else if ( input.UpgradeCost != 0 )
			{
				var q = (float)input.UpgradeCost * UpgradeCostScale;
				if ( q > 1.0f )
					score = (uint)ThemeParkIncX87Single.Truncate( (float)((float)(q * 0.1f + 1.0f) * (double)(int)score) );
			}
			var weatherPercentage = input.Weather switch
			{
				2 => terms.RainPercentage,
				4 => terms.SnowPercentage,
				1 => terms.HeatWavePercentage,
				3 => terms.StormPercentage,
				_ => 0
			};
			if ( weatherPercentage != 0 )
				score = (uint)(weatherPercentage * (int)score / 100);
			if ( terms.AppearanceEffect == 3 && (input.HoldsBalloon || input.BalloonCountdown > 0) && input.BalloonCountdown != 0 )
			{
				var factor = 100u - input.BalloonCountdown * 100u / BalloonDivisor;
				if ( factor != 0 )
				{
					if ( input.DiscardBalloonPercentage != 0 )
						factor = input.DiscardBalloonPercentage * factor / 100u;
					score = score * factor / 100u;
				}
			}
			var generationPercentage = input.Generation switch { 0 => terms.KidPercentage, 1 => terms.ParentPercentage, 2 => terms.GrandparentPercentage, _ => (int?)null };
			if ( generationPercentage is { } percentage )
				score = (uint)(percentage * (int)score / 100);
			return AttractionScore.DivideByHistory( score, input.ThingId, input.UsedHistory, input.TemporaryHistory );
		}
	}

	/// <summary>The upgrade cost's scale, the single <c>0x93E6A4</c> (1/3000).</summary>
	// [BIN:TPI-EXE:0x93E6A4 upgrade scale] the bytes of 0.00033333333f
	private const float UpgradeCostScale = 0.00033333333f;

	/// <summary>The golden-ticket factor <c>(cost + 1) × 0.1 + 1.0</c>: the double <c>0.1</c> (<c>0x93E398</c>) times the exact integer, then plus the double 1.0, each rounded to the 24-bit significand.</summary>
	// [BIN:TPI-EXE:0x610949 golden ticket] fildl, fmull 0x93E398 (the double 0.1), faddl 0x93B858 (the double 1.0), fimull the score: at control word 0x7F every product and sum rounds to 24 bits
	private static float GoldenTicketFactor( int cost ) => (float)(ThemeParkIncX87Single.Multiply( unchecked( cost + 1 ), 0.1 ) + 1.0);
}

/// <summary>
/// Theme Park Inc's game thread runs its x87 at a 24-bit significand (control word <c>0x7F</c>): every product and sum of extended operands rounds once to a single. These helpers give the single rounding
/// where C#'s double arithmetic would round twice.
/// </summary>
// [EXT:hardware-d3d-path] the game thread's precision control (Direct3D sets 24 bits): C# rounds each step to a single where the x87 does, once
public static class ThemeParkIncX87Single
{
	/// <summary>The exact product of an integer and a double, rounded once to a single (round to nearest even), as <c>fild</c> then <c>fmull</c> at 24 bits.</summary>
	public static float Multiply( long integer, double value )
	{
		if ( integer == 0 || value == 0 || double.IsNaN( value ) || double.IsInfinity( value ) )
			return (float)(integer * value);
		var bits = BitConverter.DoubleToInt64Bits( value );
		var exponent = (int)((bits >> 52) & 0x7FF);
		var mantissa = bits & 0xFFFFFFFFFFFFFL;
		if ( exponent == 0 )
			exponent = 1;
		else
			mantissa |= 1L << 52;
		var negative = (bits < 0) ^ (integer < 0);
		var magnitude = (UInt128)(ulong)Math.Abs( integer ) * (ulong)mantissa;
		// value = magnitude × 2^(exponent − 1075); round magnitude to 24 significant bits (nearest even)
		var length = 128 - (int)UInt128.LeadingZeroCount( magnitude );
		var shift = length - 24;
		var scale = exponent - 1075;
		if ( shift > 0 )
		{
			var half = (UInt128)1 << (shift - 1);
			var remainder = magnitude & (((UInt128)1 << shift) - 1);
			magnitude >>= shift;
			if ( remainder > half || remainder == half && (magnitude & 1) != 0 )
				magnitude++;
			scale += shift;
		}
		var result = Math.ScaleB( (double)(ulong)magnitude, scale );
		return (float)(negative ? -result : result);
	}

	/// <summary>
	/// <c>_ftol</c> (<c>0x86FAB0</c>): truncation toward zero of the x87 value, as a 32-bit integer: the low dword of <c>fistp qword</c>, whose invalid result (a NaN, an infinity or a value beyond 2^63) is the
	/// integer indefinite 0x8000000000000000, so 0.
	/// </summary>
	public static int Truncate( double value ) =>
		double.IsNaN( value ) || Math.Abs( value ) >= 9.2233720368547758E18 ? 0 : unchecked( (int)(long)Math.Truncate( value ) );
}

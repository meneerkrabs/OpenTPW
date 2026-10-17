namespace OpenTPW;

/// <summary>
/// The seven <c>PeepInfo.DecisionVar*Weight</c> values of the attraction score, in the order of the settings block words
/// <c>+112..+136</c> (the keys follow each other in the file's schema and are stored four bytes apart from word <c>+12</c>:
/// <c>DecisionVarDistWeight</c>, <c>Queue</c>, <c>Excitement</c>, <c>Thirst</c>, <c>Hunger</c>, <c>Toilet</c>, <c>Illness</c>).
/// </summary>
/// <remarks>The original reads each as a word and uses the low 16 bits (the queue weight whole), as the oracle target pins.</remarks>
public readonly record struct DecisionWeights( uint Distance, uint Queue, uint Excitement, uint Thirst, uint Hunger, uint Toilet, uint Illness )
{
	/// <summary>The shipped <c>Standard.sam</c> values (1, 1, 1, 2, 2, 2, 2).</summary>
	public static readonly DecisionWeights Shipped = new( 1, 1, 1, 2, 2, 2, 2 );
}

/// <summary>
/// <c>PeepInfo.DecisionVariable1..3</c> (settings words <c>+80..+88</c>): the age in days below which a ride counts as new, the score
/// multiplier for a new ride, and the multiplier for indoor rides while it rains.
/// </summary>
public readonly record struct DecisionVariables( uint NewRideDays, uint NewRideMultiplier, uint IndoorRainMultiplier )
{
	/// <summary>The shipped <c>Standard.sam</c> values (7 days, 5, 5).</summary>
	public static readonly DecisionVariables Shipped = new( 7, 5, 5 );
}

/// <summary>Everything the attraction score reads, in the original's own units (cells, words, floats, bytes).</summary>
/// <param name="GuestX">The guest's cell column (guest byte <c>+4</c>).</param>
/// <param name="GuestY">The guest's cell row (guest byte <c>+6</c>).</param>
/// <param name="RideX">The column of the attraction's back-of-queue cell (<c>0xdd3f4</c>).</param>
/// <param name="RideY">The row of the same cell.</param>
/// <param name="CellModifier">The signed 16-bit component 4 of the environment record at that cell (<c>+8</c>); 0 divides nothing.</param>
/// <param name="QueueCount">Guests listed in the queue (<c>0xdd118</c>).</param>
/// <param name="QueueCells">The queue length in cells (ride <c>+60</c>).</param>
/// <param name="PreferredExcitement">The guest's preferred excitement (<c>0xeb7e4</c>, a byte).</param>
/// <param name="Excitement">The attraction's live excitement (<c>0xe02ec(ride, 0)</c>, a byte).</param>
/// <param name="ExcitementGated">The low byte of the type's <c>UsageInfo.ExcitementLevel</c> is nonzero (<c>0xe02ec(ride, 1)</c>): the excitement weight counts only then.</param>
/// <param name="Thirst">The guest's thirst meter (<c>+420</c>).</param>
/// <param name="Hunger">The guest's hunger meter (<c>+424</c>).</param>
/// <param name="Toilet">The guest's toilet meter (<c>+428</c>).</param>
/// <param name="Illness">The guest's illness meter (<c>+432</c>).</param>
/// <param name="ThirstEffect">Type <c>UsageInfo.ThirstEffect</c> (<c>+324</c>, its low byte).</param>
/// <param name="HungerEffect">Type <c>UsageInfo.HungerEffect</c> (<c>+328</c>, its low byte).</param>
/// <param name="Relief">The attraction is a relief object (thing <c>+46</c> bit 0; <c>UsageInfo.ProvidesRelief</c>).</param>
/// <param name="AgeDays">Days since the attraction was bought (<c>0xdc520</c>).</param>
/// <param name="Raining">Drops are falling (the weather's <c>mCurrentDrops</c> above 0).</param>
/// <param name="Indoors">Type <c>UsageInfo.ISIndoors</c> (<c>+276</c>) is nonzero.</param>
/// <param name="GoldenTicketCost">Type <c>UsageInfo.GoldenTicketCost</c> (<c>+196</c>).</param>
/// <param name="UpgradeCost">Type <c>+440</c>, <c>Upgrades[0].CostOfUpgrade</c>.</param>
/// <param name="AttractionId">The attraction's thing id, as the history arrays hold it.</param>
/// <param name="SameTypeAsLastUsed">The thing the guest used last (history slot 0) still exists and is of the same type as this one.</param>
/// <param name="UsedHistory">The guest's used history, newest first (guest <c>+480..+486</c>).</param>
/// <param name="TemporaryHistory">The guest's temporary history, newest first (guest <c>+488..+494</c>).</param>
/// <param name="QueueMatchOverride">
/// OpenTPW's own extension (docs/SMARTER-GUESTS.md, phase 3): a queue match (0..100) that replaces the original's near-only queue match and counts at any distance. Null (the original) leaves the score as it is.
/// </param>
public readonly record struct AttractionScoreInput( int GuestX, int GuestY, int RideX, int RideY, int CellModifier, int QueueCount, int QueueCells,
	int PreferredExcitement, int Excitement, bool ExcitementGated, float Thirst, float Hunger, float Toilet, float Illness, int ThirstEffect, int HungerEffect,
	bool Relief, uint AgeDays, bool Raining, bool Indoors, int GoldenTicketCost, int UpgradeCost, int AttractionId, bool SameTypeAsLastUsed,
	IReadOnlyList<int> UsedHistory, IReadOnlyList<int> TemporaryHistory, uint? QueueMatchOverride = null);

/// <summary>
/// The original's attraction score (<c>0xe9164</c>, docs/reverse/ORACLE.md F24, docs/reverse/PPC-guests.md): seven matches, each 0..100,
/// combined as <c>sum(weight · match) / sum(weights)</c> in unsigned 32-bit arithmetic, then scaled for new rides, indoor rides in rain,
/// golden-ticket rides and costly upgrades, and divided down for each attraction the guest used or turned away from recently.
/// It replaces the additive formula that GUESTS.md had as an approximation.
/// </summary>
public static class AttractionScore
{
	/// <summary>A score below this is not a candidate (<c>0xe90a8</c>, <c>cmplwi 10</c>, unsigned).</summary>
	public const uint CandidateFloor = 10;

	/// <summary>Squared cell distance up to which the queue counts (<c>0xe93c4</c>, <c>cmpwi 8</c>).</summary>
	public const int QueueRangeSquared = 8;

	/// <summary>Squared cell distance that maps to a distance match of 0 (<c>100 · d / 450</c> reaches 100).</summary>
	public const int DistanceDivisor = 450;

	/// <summary>
	/// The toilet and illness match curve (<c>0xe95e0</c>, indexed by <c>(meter + 4) / 5</c>, meter taken as a byte): the original's table of 21
	/// bytes, a tuning curve that has no formula. Only a relief object uses it.
	/// </summary>
	// [BIN:STP-PPC:0x100E95E0 relief match] the 21 bytes at data +0x4129B (TOC -27100); read by the oracle from the binary, written here as the gameplay constants they are
	private static readonly byte[] ReliefMatches = { 0, 0, 0, 0, 0, 0, 0, 0, 0, 1, 2, 5, 10, 20, 30, 40, 50, 60, 75, 90, 100 };

	/// <summary>The distance match: <c>100 − min(100, 100 · (dx² + dy²) / 450)</c>, divided by the cell modifier (unsigned) when it is nonzero.</summary>
	// [BIN:STP-PPC:0x100E9320 distance match] squared displacement, not path length; the multiply-high sequence is a division by 450 over the grid's domain
	public static uint DistanceMatch( int guestX, int guestY, int rideX, int rideY, int cellModifier )
	{
		var squared = (guestX - rideX) * (guestX - rideX) + (guestY - rideY) * (guestY - rideY);
		var match = (uint)(100 - Math.Min( 100, squared * 100 / DistanceDivisor ));
		return cellModifier == 0 ? match : match / (uint)cellModifier;
	}

	/// <summary>
	/// The queue match <c>100 − 100 · count / (4 · max(cells, 1))</c> in unsigned 32-bit arithmetic: not clamped, so a queue above four per cell
	/// wraps (<c>0xe93e4..0xe9400</c>).
	/// </summary>
	public static uint QueueMatch( int queueCount, int queueCells )
	{
		var size = queueCells == 0 ? 1u : (uint)queueCells;
		return 100u - unchecked( (uint)queueCount * 100u ) / unchecked( size * 4u );
	}

	/// <summary>The excitement match <c>2 · (50 − min(50, |preferred − excitement|))</c> over bytes (<c>0xe9440..0xe9470</c>).</summary>
	public static int ExcitementMatch( int preferred, int excitement ) =>
		2 * (50 - Math.Min( 50, Math.Abs( (preferred & 0xFF) - (excitement & 0xFF) ) ));

	/// <summary>
	/// A thirst or hunger match: <c>trunc(sqrt(effect / 10) · need²)</c> with the effect (its low byte) and the need in tens. The original indexes an
	/// 11 × 11 byte table at <c>11 · (effect / 10) + need / 10</c>; the table is exactly this formula (checked against all 121 entries). Meters and
	/// effects stay in 0..100, so the index stays inside it.
	/// </summary>
	// [BIN:STP-PPC:0x100E94B8 thirst match] [BIN:STP-PPC:0x100E9528 hunger match] data +0x41222 (TOC -27104): row effect / 10, column need / 10, each quotient a byte of mulhwu(0xCCCCCCCD, x) >> 3
	public static int NeedMatch( float need, int effect )
	{
		var needTens = Math.Clamp( (int)(ConvertToWord( need ) / 10u & 0xFF), 0, 10 );
		var effectTens = Math.Clamp( (effect & 0xFF) / 10, 0, 10 );
		return (int)(needTens * needTens * Math.Sqrt( effectTens / 10.0 ));
	}

	/// <summary>The toilet or illness match, from the curve at <c>(byte(meter) + 4) / 5</c>; 0 when the attraction is not a relief object.</summary>
	// [BIN:STP-PPC:0x100E9588 relief match] the flag is thing +46 bit 0; 0xe95b8 gives 0 without it
	public static int ReliefMatch( float need, bool relief )
	{
		if ( !relief )
			return 0;
		var index = ((int)(ConvertToWord( need ) & 0xFF) + 4) / 5;
		// a meter byte above 100 (the original keeps them in 0..100) would read past the 21 entries; the bytes behind the curve are 0
		return index < ReliefMatches.Length ? ReliefMatches[index] : 0;
	}

	/// <summary><c>fctiwz</c> as a word: truncation that saturates, with NaN giving 0x80000000.</summary>
	private static uint ConvertToWord( float value ) => unchecked( (uint)QueueJoinRules.Truncate( value ) );

	/// <summary>
	/// The whole score. False when the weights add up to 0 (the original's <c>divwu</c> by zero is undefined: the shipped weights never do).
	/// A used attraction of the same type as the last one scores 0 outright (<c>0xe92d4..0xe931c</c>).
	/// </summary>
	public static bool TryCompute( in AttractionScoreInput input, DecisionWeights weights, DecisionVariables variables, out uint score )
	{
		score = 0;
		if ( input.SameTypeAsLastUsed )
			return true;
		var distanceMatch = DistanceMatch( input.GuestX, input.GuestY, input.RideX, input.RideY, input.CellModifier );
		var squared = (input.GuestX - input.RideX) * (input.GuestX - input.RideX) + (input.GuestY - input.RideY) * (input.GuestY - input.RideY);
		var near = squared <= QueueRangeSquared;
		var queueMatch = near ? QueueMatch( input.QueueCount, input.QueueCells ) : 0u;
		var queueWeight = near ? weights.Queue : 0u;
		// [EXT:smarter-guests-spread] the smarter policy's expected-wait match counts at any distance
		if ( input.QueueMatchOverride is { } queueOverride )
			(queueMatch, queueWeight) = (queueOverride, weights.Queue);
		var excitementMatch = (uint)ExcitementMatch( input.PreferredExcitement, input.Excitement );
		var excitementWeight = input.ExcitementGated ? weights.Excitement & 0xFFFF : 0u;
		var distanceWeight = weights.Distance & 0xFFFF;
		var thirstWeight = weights.Thirst & 0xFFFF;
		var hungerWeight = weights.Hunger & 0xFFFF;
		var toiletWeight = weights.Toilet & 0xFFFF;
		var illnessWeight = weights.Illness & 0xFFFF;
		unchecked
		{
			var numerator = queueWeight * queueMatch + excitementWeight * excitementMatch
				+ thirstWeight * (uint)NeedMatch( input.Thirst, input.ThirstEffect ) + hungerWeight * (uint)NeedMatch( input.Hunger, input.HungerEffect )
				+ toiletWeight * (uint)ReliefMatch( input.Toilet, input.Relief ) + illnessWeight * (uint)ReliefMatch( input.Illness, input.Relief )
				+ distanceWeight * distanceMatch;
			var denominator = queueWeight + excitementWeight + thirstWeight + hungerWeight + toiletWeight + illnessWeight + distanceWeight;
			if ( denominator == 0 )
				return false;
			score = numerator / denominator;
			if ( input.AgeDays <= variables.NewRideDays )
				score *= variables.NewRideMultiplier;
			if ( input.Raining && input.Indoors )
				score *= variables.IndoorRainMultiplier;
			if ( input.GoldenTicketCost > 0 )
				score = GoldenTicketScale( score, input.GoldenTicketCost );
			else if ( input.UpgradeCost != 0 )
				score = UpgradeCostScale( score, input.UpgradeCost );
			score = DivideByHistory( score, input.AttractionId, input.UsedHistory, input.TemporaryHistory );
		}
		return true;
	}

	/// <summary>The score, 0 when the weights add up to 0.</summary>
	public static uint Compute( in AttractionScoreInput input, DecisionWeights weights, DecisionVariables variables ) =>
		TryCompute( input, weights, variables, out var score ) ? score : 0;

	/// <summary>A golden-ticket ride (<c>+196</c> above 0): the score (as a signed word) times <c>1 + (cost + 1) / 10</c>, in double precision, truncated.</summary>
	// [BIN:STP-PPC:0x100E97B8 golden ticket scale] TOC -10896/-10904/-10912: the 2^52 conversion constant, 1.0 and 10.0
	public static uint GoldenTicketScale( uint score, int goldenTicketCost )
	{
		var scaled = (double)unchecked( (int)score ) * (1.0 + (double)unchecked( goldenTicketCost + 1 ) / 10.0);
		return unchecked( (uint)QueueJoinRules.Truncate( scaled ) );
	}

	/// <summary>
	/// A nonzero <c>+440</c> (<c>Upgrades[0].CostOfUpgrade</c>) on a ride without a golden-ticket cost: <c>q = cost / 3000f</c> (single precision);
	/// above 1 the score (as a single) is multiplied by <c>1 + q / 10</c>, in single precision, and truncated.
	/// </summary>
	// [BIN:STP-PPC:0x100E9830 upgrade cost scale] TOC -10920 (3000.0f), -10888 (10.0f), -10924 (1.0f); the comparison is with the double 1.0 (TOC -10904)
	public static uint UpgradeCostScale( uint score, int upgradeCost )
	{
		var q = (float)(float)upgradeCost / 3000f;
		if ( !(q > 1.0) )
			return score;
		var factor = 1f + q / 10f;
		return unchecked( (uint)QueueJoinRules.Truncate( (float)(float)unchecked( (int)score ) * factor ) );
	}

	/// <summary>
	/// The history divisions (<c>0xe98cc..0xe9a34</c>), signed and truncating toward zero: the used history stops at its first match (slot 0 divides by 5,
	/// 1 by 4, 2 by 3, 3 by 2), the temporary history applies every matching slot in order.
	/// </summary>
	public static uint DivideByHistory( uint score, int attractionId, IReadOnlyList<int> used, IReadOnlyList<int> temporary )
	{
		var divisors = new[] { 5, 4, 3, 2 };
		var value = unchecked( (int)score );
		for ( var slot = 0; slot < 4 && slot < used.Count; slot++ )
		{
			if ( used[slot] != attractionId )
				continue;
			value = DivideTowardZero( value, divisors[slot] );
			break;
		}
		for ( var slot = 0; slot < 4 && slot < temporary.Count; slot++ )
		{
			if ( temporary[slot] == attractionId )
				value = DivideTowardZero( value, divisors[slot] );
		}
		return unchecked( (uint)value );
	}

	private static int DivideTowardZero( int value, int divisor ) => value / divisor;
}

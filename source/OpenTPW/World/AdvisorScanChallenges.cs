namespace OpenTPW;

/// <summary>
/// What the advisor's challenge, golden ticket, tutorial and gate callbacks read beyond <see cref="IAdvisorParkState"/>, as the original's helpers return it. Like the park state, every member is a pure read of the park:
/// the advisor stays outside the simulation hash.
/// </summary>
internal interface IAdvisorChallengeState
{
	/// <summary>The guests in state 3 standing on the two ticket booth cells (<c>0x004C91C0</c>).</summary>
	int GuestsAtGate { get; }
	/// <summary>The park value, the net worth the original caches (<c>0x004C7DB0</c>), as a reader of <paramref name="maximumAge"/> sees it (the cache is read, not written).</summary>
	int ParkValue( uint maximumAge );
	/// <summary>The share, in percent, of the object types with a golden ticket cost that have been built at least once (<c>0x004CA740</c>, signed integer division, 0 without such a type).</summary>
	int TicketItemBuiltPercent { get; }
	/// <summary>The lifetime visitor counter (<c>0x004C94C0</c>, park <c>+0x21C08</c>).</summary>
	int VisitorsEver { get; }
	/// <summary>The visitors golden ticket's threshold (<c>0x785790</c>, <c>GoldenTicketLocal.Visitors</c>).</summary>
	int VisitorsTarget { get; }
	/// <summary>The guests-in-the-park golden ticket's threshold (<c>0x785794</c>, <c>GoldenTicketLocal.PeopleInPark</c>).</summary>
	int PeopleTarget { get; }
	/// <summary>The happiness golden ticket's average-happiness threshold (<c>0x785798</c>, <c>GoldenTicketLocal.Happiness</c>).</summary>
	int HappinessTarget { get; }
	/// <summary>The happiness golden ticket's guest count (<c>0x78579C</c>, <c>GoldenTicketLocal.AtLeastThisManyHappyPeople</c>).</summary>
	int HappyPeopleTarget { get; }
	/// <summary>Whether this park holds a local golden ticket (<c>0x005AF7F0</c>, the byte of the ticket's index in the park's record).</summary>
	bool TicketWon( GoldenTicketKind kind );
	/// <summary>The golden tickets the player holds, the four global ones and every park's local ones but not the secret ones (<c>0x004D4AC0</c> through <c>0x005AF4A0</c>).</summary>
	int TicketsHeld { get; }
	/// <summary>The sizes of the last arrival groups, at most five, the oldest first (the ring at park <c>+0x216D8</c>).</summary>
	IReadOnlyList<int> ArrivalWaves { get; }
}

/// <summary>
/// The advisor's record of the last five arrival group sizes: the ring the arrival update (<c>0x004CF3E0</c>) pushes into when it starts a group, which the advisor's callbacks read (<c>0x59F220</c>, <c>0x59F440</c>, <c>0x5A0780</c>).
/// </summary>
internal sealed class AdvisorArrivalRing
{
	/// <summary>The ring's capacity, the word the park init stores at <c>+0x216F4</c>.</summary>
	// [BIN:TPW-EXE:0x004C5B2C arrival ring] the park init stores the capacity 5 (+0x216F4), clears the filled flag (+0x216F8) and leaves the index below 0 (+0x216F0)
	public const int Capacity = 5;

	private readonly List<int> sizes = new();

	/// <summary>The recorded sizes, the oldest first: once the ring is full the newest five.</summary>
	public IReadOnlyList<int> Waves => sizes;

	/// <summary>Records a group's size: the index steps on, wrapping to 0 (and marking the ring full) when it reaches the capacity, and the size is stored there.</summary>
	// [BIN:TPW-EXE:0x004CF42A arrival ring push] index + 1, set to 0 with the filled flag raised once it is not below the capacity, then the group's size (+0xC of the arrival object) stored at +4 × index: the oldest entry is overwritten, so the five newest remain
	public void Push( int size )
	{
		sizes.Add( size );
		if ( sizes.Count > Capacity )
			sizes.RemoveAt( 0 );
	}

	/// <summary>Replaces the record with a saved one (the newest <see cref="Capacity"/> sizes are kept).</summary>
	public void Restore( IEnumerable<int> saved )
	{
		sizes.Clear();
		foreach ( var size in saved )
			Push( size );
	}

	/// <summary>
	/// A stand-in for a ring that was never recorded, not a port of the original's filled flag (<c>+0x216F8</c>): an OpenTPW save from before the ring was saved has no sizes, and a park with admissions has had arrival groups,
	/// so the ring is taken as full with zero-size stand-ins. Neither the real group count nor its sizes can be reconstructed from admissions: exactly-one-group advice (281) can differ if the missing history held exactly one group,
	/// and hype (124) or big-busload (140) scores can differ until real arrivals replace the relevant unknown values. At attachment, validated original-save rings replace this stand-in, including a known empty ring
	/// (<see cref="SaveArrivalRing"/>, <see cref="SaveThemeParkIncArrivalRing"/>). The separate advisor snapshot restore policy still applies this fallback to null or explicitly empty histories when admissions exist.
	/// </summary>
	// [EXT:legacy-save-arrival-waves] (user-approved 2026-10-30, ADVISOR-028's legacy part: the situation exists only in OpenTPW, which the original's saves never had) the ring of a legacy OpenTPW save is unknown: full when the park has had admissions, where tp.exe's saved ring holds the real sizes (0x4CE660)
	public void AssumeFilled()
	{
		while ( sizes.Count < Capacity )
			sizes.Insert( 0, 0 );
	}

	public void Clear() => sizes.Clear();
}

/// <summary>
/// What the player's golden ticket ledger tells the advisor as a ticket is won (the sums of <c>0x005AF4A0</c>, <c>0x005AF530</c> and the key rule of <c>0x005AF810</c>, <c>0x005AF940</c> and <c>0x005AFB00</c>).
/// </summary>
/// <param name="Earned">Every golden ticket the player has, the secret ones included (<c>0x005AF530</c>).</param>
/// <param name="EarnedWithoutSecret">The tickets without the secret ones (<c>0x005AF4A0</c>).</param>
/// <param name="ExtraKeys">The keys the player started with, which the earned tickets' keys add to (<c>+0x20</c> of the ledger).</param>
/// <param name="ThemeKeyCosts">What each park of the player costs to enter in keys (<c>Keys.CostToEnter</c>).</param>
internal sealed record AdvisorPlayerTickets( int Earned, int EarnedWithoutSecret, int ExtraKeys, IReadOnlyList<int> ThemeKeyCosts )
{
	/// <summary>The player's golden keys: a key for every third earned ticket, secret ones included, and the starting keys.</summary>
	public int Keys => ExtraKeys + Earned / PlayerProgress.TicketsPerKey;

	/// <summary>
	/// The tickets of a park without a player ledger: only this park's own are known, and the player has the one starting key. The advice of a ticket win still follows the original's key rule over these.
	/// </summary>
	public static AdvisorPlayerTickets ForPark( IReadOnlyCollection<GoldenTicketKind> tickets ) =>
		new( tickets.Count, tickets.Count( kind => !IsSecret( kind ) ), PlayerProgress.StartingKeys, Array.Empty<int>() );

	/// <summary>The secret tickets (the camera ticket and the all-land one), which the sum of <c>0x005AF4A0</c> leaves out.</summary>
	public static bool IsSecret( GoldenTicketKind kind ) => kind is GoldenTicketKind.CamerasEverywhere or GoldenTicketKind.AllLandOwned;
}

/// <summary>
/// The advice game code posts when a golden ticket is won (<c>0x004D4FA0</c> local, <c>0x004D5270</c> global, <c>0x004D5870</c> secret, each followed by <c>0x004D5B30</c>): which message of the kind's three, and the response its ticket picks.
/// </summary>
internal static class AdvisorTicketAdvice
{
	/// <summary>The first of the three messages of a local ticket's win (215 to 217).</summary>
	public const int FirstLocalMessage = 215;
	/// <summary>The first of the three messages of a global ticket's win (220 to 222).</summary>
	public const int FirstGlobalMessage = 220;
	/// <summary>The first of the three messages of a secret ticket's win (225 to 227).</summary>
	public const int FirstSecretMessage = 225;
	/// <summary>The message after the player's first golden ticket (<c>0x137</c>).</summary>
	public const int FirstTicketMessage = 311;
	/// <summary>The clues for the remaining tickets, said right after <see cref="FirstTicketMessage"/> (<c>0x13B</c>).</summary>
	public const int CluesMessage = 315;

	/// <summary>
	/// The outcome of a win, 1 to 3: 2 when the tickets now make a whole number of keys (the sum is a multiple of 3) and the player has a key, 3 when the keys are exactly what one of the player's parks costs to enter, else 1.
	/// </summary>
	// [BIN:TPW-EXE:0x005AF810 ticket outcome] the sum of all tickets (0x5AF4A0 plus the secret ones) / 3 plus the extra keys (+0x20) must be above 0 and the sum a multiple of 3 for the key outcomes, which walk the player's parks for one whose cost to enter (0x5B11C0, the record's +0x14) equals those keys: 3 when found, 2 when none; any other state is 1 (the same rule closes 0x5AF940 for a global ticket and 0x5AFB00 for a secret one)
	public static int Outcome( AdvisorPlayerTickets tickets ) =>
		tickets.Keys > 0 && tickets.Earned % PlayerProgress.TicketsPerKey == 0 ? tickets.ThemeKeyCosts.Contains( tickets.Keys ) ? 3 : 2 : 1;

	/// <summary>The message and response variant of a win: the kind's own message for the outcome and the ticket's index as the response, or null for a ticket the original never awards (the all-land one).</summary>
	// [BIN:TPW-EXE:0x004D4FA0 local ticket won] outcome 1, 2 or 3 queues 0xD7, 0xD8 or 0xD9 (215-217) with the ticket's type (0 to 5) as the response variant (the record's +8, the fourth argument of 0x59B590); the global 0x4D5270 queues 0xDC to 0xDE (220-222) with the global type 0 to 3 and the secret 0x4D5870 0xE1 to 0xE3 (225-227) with the secret type
	public static (int Message, int Variant)? For( GoldenTicketKind kind, int outcome )
	{
		var step = Math.Clamp( outcome, 1, 3 ) - 1;
		return kind switch
		{
			<= GoldenTicketKind.RecentVisitors => (FirstLocalMessage + step, (int)kind),
			>= GoldenTicketKind.CoasterHeight and <= GoldenTicketKind.BigPark => (FirstGlobalMessage + step, kind - GoldenTicketKind.CoasterHeight),
			GoldenTicketKind.CamerasEverywhere => (FirstSecretMessage + step, 0),
			_ => null,
		};
	}
}

/// <summary>
/// The advice of the golden ticket and challenge group that tp.exe posts as events or scores from the background scan, as the descriptors the controller binds: the rows <see cref="AdvisorTables"/> adds to its own lists.
/// </summary>
internal static class AdvisorChallengeMessages
{
	/// <summary>The win messages of the three ticket kinds (215-217, 220-222 and 225-227); the settings file's group 2 is read when it has it, like the global ticket moves'.</summary>
	public static readonly IReadOnlyList<int> TicketWonMessages = new[] { 215, 216, 217, 220, 221, 222, 225, 226, 227 };

	/// <summary>The offer, the win without a follow-up and the loss of a challenge (270, 272, 273), said by the challenge system.</summary>
	public const int OfferedMessage = 270, WonMessage = 272, LostMessage = 273;

	/// <summary>The first-offer line of the challenge system (the help descriptor 344, posted by the challenge offer instead of 270).</summary>
	public const int FirstOfferMessage = 344;

	/// <summary>The posted messages with the key of the score their getter returns (<c>0x59E890</c>, <c>0x5A0520</c>, <c>0x5A0590</c>, <c>0x5A0A30</c>).</summary>
	// [BIN:TPW-EXE:0x0059E890 ticket score getters] 215-227 return the word at +0x31C, GeneralWonGoldenTicket.Score; 311 +0x5B4 (WonFirstGoldenTicket.Score, 0x5A0520), 315 +0x5C0 (GoldTicketCluesForRemainder.Score, 0x5A0590) and 270, 272, 273 +0x62C (Challenge.Score, 0x5A0A30) like 271
	public static readonly IReadOnlyList<(int MessageId, string ScoreKey, int? FixedScore)> EventScoreKeys = TicketWonMessages.Select( id => (id, "GeneralWonGoldenTicket.Score", (int?)null) )
		.Concat( new (int, string, int?)[]
		{
			(OfferedMessage, AdvisorTables.ChallengeScoreKey, null), (WonMessage, AdvisorTables.ChallengeScoreKey, null), (LostMessage, AdvisorTables.ChallengeScoreKey, null),
			(AdvisorTicketAdvice.FirstTicketMessage, "WonFirstGoldenTicket.Score", null), (AdvisorTicketAdvice.CluesMessage, "GoldTicketCluesForRemainder.Score", null),
		} ).ToArray();

	/// <summary>The scanned messages of this group with the key of the flat score of their callback (the row of a computed score names its multiplier).</summary>
	public static readonly IReadOnlyList<(int MessageId, string ScoreKey, int? FixedScore)> ScanScoreKeys = new (int, string, int?)[]
	{
		(1, "OpenPark.ScorePerWaitingPerson", null), (2, "ClosePark.Score", null), (124, "HypeNewpark.Score", null), (140, "BigBusloadArrived.Score", null),
		(156, "CongratPublishPark.Score", null), (157, "CongratSendPostcard.Score", null), (185, "TutorialUIPanel.Score", null),
		(274, "GTAmountVisitorsA.Score", null), (275, "GTAmountVisitorsB.Score", null), (276, "GTAmountVisitorsC.Score", null),
		(278, "GTAllResearchedA.Score", null), (279, "GTAllResearchedB.Score", null), (280, "GTAllResearchedC.Score", null),
		(281, "GoldTicketExplanation.Score", null), (282, "TutorialExplainTooltips.Score", null),
		(314, "GoldTicketNearToFirstOne.Score", null), (316, "GoldTicketNearToXPeeps.Score", null), (317, "GoldTicketNearToXHappiness.Score", null),
	};
}

/// <summary>
/// The advisor's callbacks of the park-opening, gate, tutorial, congratulation, challenge and golden ticket group, ported from <c>tp.exe</c>, by the address of the original's callback. Each reads the park through
/// <see cref="IAdvisorParkState"/> and <see cref="IAdvisorChallengeState"/> and its thresholds through <see cref="AdvisorScanContext.Parameter"/>; a threshold the settings file lacks, or a park without
/// <see cref="IAdvisorChallengeState"/>, leaves its callback at 0.
/// </summary>
internal static class AdvisorChallengeCallbacks
{
	/// <summary>The integer thresholds these callbacks read (words of the <c>Advisor.sam</c> object).</summary>
	public static readonly IReadOnlyList<string> IntegerKeys = new[]
	{
		"OpenPark.ScorePerWaitingPerson",
		"HypeNewpark.MaxMonths", "HypeNewpark.DroppedPercent", "BigBusloadArrived.HowBigIsBig",
		"CongratPublishPark.ValueBetterThan", "CongratPublishPark.HappinessGreaterThan", "CongratPublishPark.VisitorsMoreThan",
		"CongratSendPostcard.ValueBetterThan", "CongratSendPostcard.HappinessGreaterThan", "CongratSendPostcard.VisitorsMoreThan",
		"TutorialUIPanel.AfterTime", "TutorialUIPanel.CutoffTime", "TutorialExplainTooltips.AfterTime", "TutorialExplainTooltips.CutoffTime",
		"GTAmountVisitorsA.HigherPctThanThis", "GTAmountVisitorsB.HigherPctThanThis", "GTAmountVisitorsC.HigherPctThanThis",
		"GTAllResearchedA.HigherPctThanThis", "GTAllResearchedB.HigherPctThanThis", "GTAllResearchedC.HigherPctThanThis",
	};

	/// <summary>The single-precision multipliers these callbacks read (<c>fmul dword</c>).</summary>
	public static readonly IReadOnlyList<string> FloatKeys = new[]
	{
		"GoldTicketNearToFirstOne.PeopleMultiplier", "GoldTicketNearToXPeeps.PeopleMultiplier", "GoldTicketNearToXHappiness.HappinessMultiplier",
	};

	public const uint OpenPark = 0x59C8D0;
	public const uint ClosePark = 0x59C960;
	public const uint HypeNewPark = 0x59F220;
	public const uint BigBusload = 0x59F440;
	public const uint CongratPublishPark = 0x59F860;
	public const uint CongratSendPostcard = 0x59F8D0;
	public const uint NearToFirstOne = 0x5A0530;
	public const uint NearToXPeeps = 0x5A05A0;
	public const uint NearToXHappiness = 0x5A05F0;
	public const uint TutorialUiPanel = 0x5A06C0;
	public const uint TutorialExplainTooltips = 0x5A0720;
	public const uint GoldTicketExplanation = 0x5A0780;
	public const uint AllResearchedA = 0x5A07D0;
	public const uint AllResearchedB = 0x5A0820;
	public const uint AllResearchedC = 0x5A0870;
	public const uint AmountVisitorsA = 0x5A08B0;
	public const uint AmountVisitorsB = 0x5A0930;
	public const uint AmountVisitorsC = 0x5A09B0;

	private const int Rides = 0;
	private const int Sideshows = 2;

	/// <summary>The challenge group's callbacks by the address of the original's callback (the descriptor's word +0x10).</summary>
	public static readonly IReadOnlyDictionary<uint, AdvisorScoreCallback> Callbacks = new Dictionary<uint, AdvisorScoreCallback>
	{
		[OpenPark] = OpenParkScore,
		[ClosePark] = CloseParkScore,
		[HypeNewPark] = HypeNewParkScore,
		[BigBusload] = BigBusloadScore,
		[CongratPublishPark] = context => Congratulation( context, "CongratPublishPark" ),
		[CongratSendPostcard] = context => Congratulation( context, "CongratSendPostcard" ),
		[TutorialUiPanel] = context => Tutorial( context, "TutorialUIPanel" ),
		[TutorialExplainTooltips] = context => Tutorial( context, "TutorialExplainTooltips" ),
		[AmountVisitorsA] = context => AmountOfVisitors( context, "GTAmountVisitorsA" ),
		[AmountVisitorsB] = context => AmountOfVisitors( context, "GTAmountVisitorsB" ),
		[AmountVisitorsC] = context => AmountOfVisitors( context, "GTAmountVisitorsC" ),
		[AllResearchedA] = context => AllResearched( context, "GTAllResearchedA", "GTAllResearchedB" ),
		[AllResearchedB] = context => AllResearched( context, "GTAllResearchedB", "GTAllResearchedC" ),
		[AllResearchedC] = context => AllResearched( context, "GTAllResearchedC", null ),
		[GoldTicketExplanation] = GoldTicketExplanationScore,
		[NearToFirstOne] = NearToFirstOneScore,
		[NearToXPeeps] = NearToXPeepsScore,
		[NearToXHappiness] = NearToXHappinessScore,
	};

	/// <summary>The park of a callback that reads the challenge state; false (the callback scores 0) without one.</summary>
	private static bool Park( AdvisorScanContext context, out IAdvisorParkState park, out IAdvisorChallengeState state )
	{
		park = context.Park!;
		state = (context.Park as IAdvisorChallengeState)!;
		return context.Park is IAdvisorChallengeState;
	}

	/// <summary>Reads thresholds as the words they are; false when one key is missing.</summary>
	private static bool Read( AdvisorScanContext context, out int[] values, params string[] keys )
	{
		values = new int[keys.Length];
		for ( var index = 0; index < keys.Length; index++ )
		{
			if ( context.Parameter( keys[index] ) is not { } value )
				return false;
			values[index] = (int)value;
		}
		return true;
	}

	/// <summary>A threshold times a ticket's target as the x87 does at 24 bits: the product (<c>fild</c> then <c>fmul dword</c>) is rounded to a single and truncated (<c>0x0067A830</c>).</summary>
	private static int Scaled( int target, double multiplier ) => (int)(float)(target * (double)(float)multiplier);

	// [BIN:TPW-EXE:0x0059C8D0 open park] the park open gives 0; without a ride (0x4CA8E0 of kind 0, level 0) and without a sideshow (kind 2) it is 0 as well; else the guests waiting at the gate (0x4C91C0) times the word at +0xA4
	private static int OpenParkScore( AdvisorScanContext context )
	{
		if ( !Park( context, out var park, out var state ) || !Read( context, out var v, "OpenPark.ScorePerWaitingPerson" ) )
			return 0;
		if ( park.ParkOpen )
			return 0;
		if ( park.ObjectsOfKind( Rides, 0 ) <= 0 && park.ObjectsOfKind( Sideshows, 0 ) <= 0 )
			return 0;
		return unchecked( state.GuestsAtGate * v[0] );
	}

	// [BIN:TPW-EXE:0x0059C960 close park] only with the park open (0x4C9050), and with neither a ride nor a sideshow built: the word at +0xA8
	private static int CloseParkScore( AdvisorScanContext context )
	{
		if ( context.Park is not { } park )
			return 0;
		if ( !park.ParkOpen || park.ObjectsOfKind( Rides, 0 ) > 0 || park.ObjectsOfKind( Sideshows, 0 ) > 0 )
			return 0;
		return context.Score( "ClosePark.Score" );
	}

	// [BIN:TPW-EXE:0x0059F220 hype new park] the park's age in whole 30-day months (0x4F8800 / 0x1792F8648000, signed) below the word at +0x3C8; at least two arrival groups in the ring (+0x216D8: the count is the capacity once the filled flag is set, else the index + 1); the previous group nonzero and above the newest; and 100 minus the newest x 100 / the previous (signed) above the word at +0x3CC: the word at +0x3D0
	private static int HypeNewParkScore( AdvisorScanContext context )
	{
		if ( !Park( context, out _, out var state ) || context.Money is not { } money || !Read( context, out var v, "HypeNewpark.MaxMonths", "HypeNewpark.DroppedPercent" ) )
			return 0;
		var waves = state.ArrivalWaves;
		if ( waves.Count <= 1 || money.ParkMonths >= v[0] )
			return 0;
		int latest = waves[^1], previous = waves[^2];
		if ( previous == 0 || latest >= previous )
			return 0;
		var dropped = 100 - unchecked( latest * 100 ) / previous;
		return dropped > v[1] ? context.Score( "HypeNewpark.Score" ) : 0;
	}

	// [BIN:TPW-EXE:0x0059F440 big busload] at least one arrival group in the ring and the newest above the word at +0x410: the word at +0x414 (the callback also stores the cell 0x4D8650 names in the object's +8, which nothing here reads)
	private static int BigBusloadScore( AdvisorScanContext context )
	{
		if ( !Park( context, out _, out var state ) || !Read( context, out var v, "BigBusloadArrived.HowBigIsBig" ) )
			return 0;
		var waves = state.ArrivalWaves;
		return waves.Count > 0 && waves[^1] > v[0] ? context.Score( "BigBusloadArrived.Score" ) : 0;
	}

	// [BIN:TPW-EXE:0x0059F860 congratulations] the park value (0x4C7DB0 with age 10) above the word at +0x480, the guests in the park (0x4C9130) above the word at +0x488 and the mean happiness (0x4C7BB0) above the word at +0x484 (the integer loaded with fild, compared as the x87 does, the happiness strictly above): the word at +0x48C; 0x59F8D0 is the same with +0x490, +0x498, +0x494 and +0x49C
	private static int Congratulation( AdvisorScanContext context, string group )
	{
		if ( !Park( context, out var park, out var state ) || !Read( context, out var v, $"{group}.ValueBetterThan", $"{group}.VisitorsMoreThan", $"{group}.HappinessGreaterThan" ) )
			return 0;
		if ( state.ParkValue( 10 ) <= v[0] || park.GuestsInPark <= v[1] )
			return 0;
		return (double)v[2] < park.AverageHappiness ? context.Score( $"{group}.Score" ) : 0;
	}

	// [BIN:TPW-EXE:0x005A06C0 tutorial days] the park's age in whole days (0x4F8800 / 0xC92A69C000) above the word at +0x5DC and below the word at +0x5E0 (signed): the word at +0x5E4; 0x5A0720 is the same with +0x5E8, +0x5EC and +0x5F0
	private static int Tutorial( AdvisorScanContext context, string group )
	{
		if ( context.Park is not { } park || !Read( context, out var v, $"{group}.AfterTime", $"{group}.CutoffTime" ) )
			return 0;
		var days = park.DaysSinceStart;
		return days > v[0] && days < v[1] ? context.Score( $"{group}.Score" ) : 0;
	}

	// [BIN:TPW-EXE:0x005A08B0 amount of visitors] the park open (0x51A280 clear) and the people ticket's threshold (0x785794) nonzero: the guests in the park x 100 / that threshold (signed), cut to 100, above the word at +0x614: the word at +0x618; 0x5A0930 is +0x61C and +0x620, 0x5A09B0 +0x624 and +0x628
	private static int AmountOfVisitors( AdvisorScanContext context, string group )
	{
		if ( !Park( context, out var park, out var state ) || !Read( context, out var v, $"{group}.HigherPctThanThis" ) )
			return 0;
		if ( !park.ParkOpen || state.PeopleTarget == 0 )
			return 0;
		var percent = unchecked( park.GuestsInPark * 100 ) / state.PeopleTarget;
		if ( percent > 100 )
			percent = 100;
		return percent > v[0] ? context.Score( $"{group}.Score" ) : 0;
	}

	// [BIN:TPW-EXE:0x005A07D0 all researched] the park open and the share of the object types with a golden ticket cost that were built (0x4CA740) above the word at +0x5FC and not above the word at +0x604 (the next group's): the word at +0x600; 0x5A0820 is +0x604, +0x60C and +0x608, 0x5A0870 +0x60C and +0x610 with no upper bound
	private static int AllResearched( AdvisorScanContext context, string group, string? next )
	{
		if ( !Park( context, out var park, out var state ) || !Read( context, out var v, next == null ? new[] { $"{group}.HigherPctThanThis" } : new[] { $"{group}.HigherPctThanThis", $"{next}.HigherPctThanThis" } ) )
			return 0;
		if ( !park.ParkOpen )
			return 0;
		var percent = state.TicketItemBuiltPercent;
		if ( percent <= v[0] || next != null && percent > v[1] )
			return 0;
		return context.Score( $"{group}.Score" );
	}

	// [BIN:TPW-EXE:0x005A0780 gold ticket explanation] the ring of arrival groups (+0x216D8) holds exactly one: the word at +0x5F4
	private static int GoldTicketExplanationScore( AdvisorScanContext context ) =>
		Park( context, out _, out var state ) && state.ArrivalWaves.Count == 1 ? context.Score( "GoldTicketExplanation.Score" ) : 0;

	// [BIN:TPW-EXE:0x005A0530 near to the first ticket] the player holds no ticket (0x4D4AC0) and the lifetime visitors (0x4C94C0) are above the visitors threshold (0x785790) x the single at +0x5B8, truncated: the word at +0x5BC
	private static int NearToFirstOneScore( AdvisorScanContext context )
	{
		if ( !Park( context, out _, out var state ) || context.Parameter( "GoldTicketNearToFirstOne.PeopleMultiplier" ) is not { } multiplier )
			return 0;
		if ( state.TicketsHeld > 0 )
			return 0;
		return state.VisitorsEver > Scaled( state.VisitorsTarget, multiplier ) ? context.Score( "GoldTicketNearToFirstOne.Score" ) : 0;
	}

	// [BIN:TPW-EXE:0x005A05A0 near to the visitors ticket] the lifetime visitors above the visitors threshold x the single at +0x5C4, truncated, and the visitors ticket (0x5AF7F0 of 0) not won: the word at +0x5C8
	private static int NearToXPeepsScore( AdvisorScanContext context )
	{
		if ( !Park( context, out _, out var state ) || context.Parameter( "GoldTicketNearToXPeeps.PeopleMultiplier" ) is not { } multiplier )
			return 0;
		if ( state.VisitorsEver <= Scaled( state.VisitorsTarget, multiplier ) || state.TicketWon( GoldenTicketKind.Visitors ) )
			return 0;
		return context.Score( "GoldTicketNearToXPeeps.Score" );
	}

	// [BIN:TPW-EXE:0x005A05F0 near to the happiness ticket] the mean happiness (0x4C7BB0) above the happiness threshold (0x785798) x the single at +0x5CC truncated to an integer, the guests in the park (0x4C9130) above 0x78579C and the happiness ticket (index 2) not won: the word at +0x5D0
	private static int NearToXHappinessScore( AdvisorScanContext context )
	{
		if ( !Park( context, out var park, out var state ) || context.Parameter( "GoldTicketNearToXHappiness.HappinessMultiplier" ) is not { } multiplier )
			return 0;
		if ( !((double)Scaled( state.HappinessTarget, multiplier ) < park.AverageHappiness) || park.GuestsInPark <= state.HappyPeopleTarget || state.TicketWon( GoldenTicketKind.Happiness ) )
			return 0;
		return context.Score( "GoldTicketNearToXHappiness.Score" );
	}
}

internal sealed partial class EconomyParkState : IAdvisorChallengeState
{
	/// <summary>The advisor's record of the last arrival group sizes, oldest first (<see cref="AdvisorArrivalRing"/>); empty without one.</summary>
	public IReadOnlyList<int> ArrivalWaves { get; init; } = Array.Empty<int>();

	/// <summary>The player's ticket ledger as the advisor reads it, given this park's own tickets; null without a ledger (the park's own tickets then stand for the player's).</summary>
	public Func<IReadOnlyCollection<GoldenTicketKind>, AdvisorPlayerTickets?>? PlayerTickets { get; init; }

	// [BIN:TPW-EXE:0x004C91C0 guests at the gate] the guests waiting at the booth (state 3) on the two ticket booth cells
	public int GuestsAtGate => economy.GuestStatistics.GuestsAtGate;

	// [BIN:TPW-EXE:0x004C7DB0 park value] the net worth with the reader's age: the cached count while it is that fresh, else a fresh one; read here without stamping the cache, which the original's reader does write
	public int ParkValue( uint maximumAge ) => economy.PeekNetWorth( maximumAge );

	// [BIN:TPW-EXE:0x004CA740 ticket items built] over the type records whose golden ticket cost (+0xC4) is above 0: the share, times 100 over their number (signed), of those whose control's built count (+0x18, never lowered) is above 0; 0 without such a type
	public int TicketItemBuiltPercent
	{
		get
		{
			var types = economy.Catalog.Objects.Where( info => info.GoldenTicketCost > 0 ).ToList();
			if ( types.Count == 0 )
				return 0;
			var built = types.Count( info => economy.Counters[ParkCounters.Built( info.InfoId )] > 0 );
			return built * 100 / types.Count;
		}
	}

	// [BIN:TPW-EXE:0x004C94C0 visitors ever] the lifetime visitor counter (+0x21C08), raised per admission (0x004C7530)

	// [BIN:TPW-EXE:0x004D4BC0 ticket thresholds] the words 0x785790, 0x785794, 0x785798 and 0x78579C the local ticket check compares with are GoldenTicketLocal.Visitors, PeopleInPark, Happiness and AtLeastThisManyHappyPeople
	public int VisitorsTarget => economy.Settings.GoldenTickets.Visitors;
	public int PeopleTarget => economy.Settings.GoldenTickets.PeopleInPark;
	public int HappinessTarget => economy.Settings.GoldenTickets.Happiness;
	public int HappyPeopleTarget => economy.Settings.GoldenTickets.AtLeastThisManyHappyPeople;

	// [BIN:TPW-EXE:0x005AF7F0 ticket won] the byte of the ticket's index in the park's record (the local tickets 0 to 5)
	public bool TicketWon( GoldenTicketKind kind ) => economy.Objectives.GoldenTickets.Contains( kind );

	// [BIN:TPW-EXE:0x004D4AC0 tickets held] the global flags and every park's local flags (0x5AF4A0), the secret ones left out
	public int TicketsHeld => (PlayerTickets?.Invoke( economy.Objectives.GoldenTickets ) ?? AdvisorPlayerTickets.ForPark( economy.Objectives.GoldenTickets )).EarnedWithoutSecret;
}

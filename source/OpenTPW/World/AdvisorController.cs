using System.Text;

namespace OpenTPW;

/// <summary>
/// <c>CMsgEvent</c> game-event IDs the advisor's event handler turns into advice. These are a
/// separate namespace from advice message IDs, response IDs and speech sample numbers.
/// </summary>
internal enum AdvisorGameEvent
{
	LevelStarted = 0,
	Bankrupted = 2,
	ParkNowOpen = 3,
	ParkNowClosed = 4,
	ResetForEasyMode = 10,
}

/// <summary>
/// A scored advice message: descriptor fields plus the Advisor.sam key of its configured score. <paramref name="GameMode"/> is the
/// descriptor's game-mode selector (0: full simulation only, 1: Instant Action only, 2: always).
/// </summary>
/// <param name="FixedScore">The score of a callback that returns a constant (the original's <c>mov eax, imm</c> callbacks), else null: the score is the Advisor.sam value of <paramref name="ScoreKey"/>.</param>
internal readonly record struct AdvisorMessageDefinition( int MessageId, int FirstResponseId, int ResponseCount, byte Group, byte MaximumPendingDuplicates, string ScoreKey, byte GameMode = 2, int? FixedScore = null );

/// <summary>What one scan update did: the row it scored, and the advice it queued with the queue's verdict (both null when nothing passed).</summary>
internal readonly record struct AdvisorScanOutcome( int ScoredRow, AdvisorPendingAdvice? Posted, AdvisorQueueAdmission? Admission );

/// <summary>
/// The rows of the original 351-record descriptor table reached by the wired game events
/// (docs/reverse/PPC-advisor.md, "Actual CMsgEvent producers"). Only these rows are bound; every
/// other message is absent rather than guessed. Response IDs resolve through the shipped
/// <see cref="AdvisorResponses"/> table (responses 1, 274/275, 308–311 and 587).
/// </summary>
internal static class AdvisorTables
{
	/// <summary>The Advisor.sam keys of the three prank messages' scores; optional, a settings file without them leaves the pranks silent.</summary>
	public static readonly string[] PrankScoreKeys = { "PrankSbomb.Score", "PrankLitter.Score", "PrankBalloon.Score" };

	// [BIN:STP-PPC:0x1000D468 descriptor accessors] rows of the 351-record descriptor table (data section 0x1F2B4): message, first response (+32, 0x1000D468), response count (+36, 0x1000D5EC), group (+8), pending-duplicate limit (+28 low byte, 0x1000D770); all use cyclic mode 2 and game-mode selector 2 (always eligible)
	public static readonly AdvisorMessageDefinition[] Messages =
	{
		new( 0, 1, 1, 0, 1, "Welcome.Score" ),
		new( 106, 274, 2, 0, 1, "Bankrupted.Score" ),
		new( 128, 308, 2, 0, 1, "ParkNowOpen.Score" ),
		new( 129, 310, 2, 0, 1, "ParkNowClosed.Score" ),
		new( 323, 587, 1, 1, 1, "PrebuiltPark.Score" ),
		// [BIN:TPW-EXE:0x0076E300 prank descriptors] rows 0x7D..0x7F of the Windows table (filled by 0x5A0A50): responses 302/304/306, two each, group 0, game mode 2, pending duplicates 255, 1 and 255; their score callbacks 0x59F360/0x59F370/0x59F380 return the Advisor.sam fields +0x3D4/+0x3D8/+0x3DC, the schema's PrankSbomb, PrankLitter and PrankBalloon scores (0x0072E124, just before ParkNowOpen at +0x3E0)
		new( 125, 302, 2, 0, 255, PrankScoreKeys[0] ),
		new( 126, 304, 2, 0, 1, PrankScoreKeys[1] ),
		new( 127, 306, 2, 0, 255, PrankScoreKeys[2] ),
		// [BIN:TPW-EXE:0x0076E300 challenge descriptor] row 0x10F (271) of the Windows table: responses 523 to 525, group 0, one pending copy, game mode 0 (full simulation only), the score callback 0x5A0A30 (+0x62C of the Advisor.sam object, Challenge.Score: the Challenge group follows GTAmountVisitorsC in the descriptor records 0x739560, after PrebuiltPark.Score at +0x5F8); the challenge system posts it as the advice 0x10F of its short-time-left warning (0x4D21FD)
		new( ChallengeTimeShortMessage, 523, 3, 0, 1, ChallengeScoreKey, 0 ),
		// [BIN:TPW-EXE:0x0076E300 mechanic descriptor] row 0x46 (70) of the Windows table: responses 204 to 206, group 0, 255 pending copies, game mode 2, the score callback 0x59DC90 (+0x254 of the Advisor.sam object, RidesBreakdownMechanicOnIt.Score: the fourth key of the RidesBreakdown run whose first getter 0x59DC60 is message 66)
		new( MechanicOnTheWayMessage, 204, 3, 0, 255, MechanicOnTheWayScoreKey ),
	};

	/// <summary>The advice of a mechanic on his way to a broken-down ride (0x46).</summary>
	public const int MechanicOnTheWayMessage = 70;

	/// <summary>The Advisor.sam key of the mechanic message's score; optional, a settings file without it leaves the message silent.</summary>
	public const string MechanicOnTheWayScoreKey = "RidesBreakdownMechanicOnIt.Score";

	/// <summary>The advice of a challenge's short-time-left warning (0x10F).</summary>
	public const int ChallengeTimeShortMessage = 271;

	/// <summary>The Advisor.sam key of the challenge messages' score; optional, a settings file without it leaves the warning silent.</summary>
	public const string ChallengeScoreKey = "Challenge.Score";

	/// <summary>The first of the three prank messages (0x7D).</summary>
	public const int FirstPrankMessage = 125;

	/// <summary>
	/// The advice a prank message constructs: 0x7D, 0x7E or 0x7F by the message's kind word, which the prank step fills with the prankster's
	/// thing id modulo 3, not with the prank (a stinkbomb, litter or a burst balloon).
	/// </summary>
	// [BIN:TPW-EXE:0x004FF2EA prank message kind] the message (vtable 0x007007A8, type 0xE) takes the prankster's id (0x0050B350) at +8 and, at +0xC, the word at esp+0x1C after the id was stored there (0x004FF2D6), divided by 3 (0x004FF2F8): the remainder; the bus 0x0040FB10 delivers it at once to the advisor and the staff HQ
	// [BIN:TPW-EXE:0x0059B060 advisor messages] type 0xE: kind 0 queues 0x7D, 1 queues 0x7E, 2 queues 0x7F (0x0059A940), each record with the prankster's id and cell, no variant (-1) and the descriptor's score (0x0059BF20)
	public static int PrankAdvice( int pranksterId ) => FirstPrankMessage + (int)((uint)(ushort)pranksterId % 3);

	/// <summary>The Advisor.sam key of the score every help message carries.</summary>
	// [BIN:TPW-EXE:0x0059E8C0 help score] the score callback of the 82 help descriptors returns the balance field at +0x328, the Advisor.sam key HelpMessage.Score (1000 shipped); 81 of them are bound here (HelpMessages): message 291 names the absent response 551, so advisor-help-messages.toml leaves it out (docs/reverse/ADVISOR-SCAN.md)
	// [BIN:TPW-EXE:0x0072E124 advisor.sam schema] the schema's int fields laid out in order from +0x18 (GeneralAdvisor +0x18/+0x1C/+0x20, read at 0x59B6C3 as +0x20) put HelpMessage.Score at +0x328 and HelpMessage.ContextHelpScore at +0x32C once the 4-byte gap after the MessageGroups array is counted, which the accessors confirm: four Score getters at +0x31C..+0x328 (0x59E890..0x59E8C0, GeneralWonGoldenTicket, AwardGiven, AwardMoved, HelpMessage) and the money test 0x59E8E6 against +0x330 (WealthHigh.MoreMoneyThan)
	public const string HelpScoreKey = "HelpMessage.Score";
	/// <summary>The Advisor.sam key of the score F1 gives the replayed last tutorial advice (<see cref="AdvisorController.ReplayLastTutorial"/>).</summary>
	// [BIN:TPW-EXE:0x0059AB8E replay score] the only reader of the balance field +0x32C (0xFB386C), HelpMessage.ContextHelpScore (1000 shipped)
	public const string ContextHelpScoreKey = "HelpMessage.ContextHelpScore";

	private static IReadOnlyList<AdvisorMessageDefinition>? help;

	/// <summary>The help messages (<see cref="AdvisorHelpTable"/>) as definitions with the shared help score.</summary>
	public static IReadOnlyList<AdvisorMessageDefinition> HelpMessages => help ??= AdvisorHelpTable.Messages
		.Select( message => new AdvisorMessageDefinition( message.MessageId, message.FirstResponseId, message.ResponseCount, message.Group, message.MaximumPendingDuplicates, HelpScoreKey, message.GameMode ) )
		.ToArray();

	/// <summary>The bound game-event messages, the help messages, the advice game code posts with a getter score and the messages the scan produces.</summary>
	public static IEnumerable<AdvisorMessageDefinition> AllMessages => Messages.Concat( HelpMessages ).Concat( EventMessages ).Concat( ScanMessages );

	/// <summary>The first of the 25 staff messages (33 + 5 × kind + the role's position), the <see cref="StrikeMessage"/> TAG_SYSTEM index less 5.</summary>
	public const int FirstStaffMessage = 33;

	/// <summary>The staff message of a strike message: the kind's block of five and the role's place in it (mechanic, handyman, guard, entertainer, researcher).</summary>
	// [BIN:TPW-EXE:0x0059AFC0 staff info] a message of type 0x17 with the kind in +0xC and the role in +8 queues 0x21 + 5 × kind + 1 for a role of 0 (handyman), + 0 for 1 (mechanic), + 3 for 2 (entertainer), + 2 for 3 (guard) and + 4 for 4 (researcher), the order of the StaffType enum; the staff manager's month routine (0x509031-0x5092AA) sends kinds 0 (once), 2 (four sites) and 3 (once)
	public static int StaffMessage( StrikeMessage message ) => message.TagSystemIndex - StrikeMessage.FirstTagSystemIndex + FirstStaffMessage;

	private static readonly string[] StaffMessageFamilies = { "StaffUnhappy", "StaffVeryUnhappy", "StaffStriking", "StaffHappier", "StrikeEndBad" };
	private static readonly string[] StaffMessageRoles = { "Mechanics", "Handymen", "Guards", "Entertainers", "Researchers" };

	/// <summary>The messages a global golden ticket moving to this park says (0xEA-0xED: coaster height, go-kart excitement, water length, big park); they belong to group 2.</summary>
	public static readonly IReadOnlyList<int> AwardMovedMessages = new[] { 234, 235, 236, 237 };

	/// <summary>Whether a posted message's group controls are read only when the settings file has them (the global ticket moves' and the ticket wins' group 2): a reduced file then leaves the message without a descriptor instead of failing.</summary>
	public static bool GroupIsOptional( int messageId ) => AwardMovedMessages.Contains( messageId ) || AdvisorChallengeMessages.TicketWonMessages.Contains( messageId );

	/// <summary>The advice of a condemned ride (0x102, from the ride's message type 8).</summary>
	public const int RideCondemnedMessage = 258;

	/// <summary>The Advisor.sam key of the score of those four messages.</summary>
	public const string AwardMovedScoreKey = "AwardMoved.Score";

	/// <summary>
	/// The advice game code posts, with the score key of its callback (or the constant it returns): each callback is a getter of one Advisor.sam score, at the object's
	/// offset of the key in the schema 0x72E124 (fields from +0xA0 in schema order), so the staff messages 33-57 read +0x1BC-+0x21C.
	/// </summary>
	// [BIN:TPW-EXE:0x0059D800 staff score getters] the callbacks of the descriptors 33-57 return the fields +0x1BC to +0x21C, StaffUnhappy, StaffVeryUnhappy, StaffStriking, StaffHappier and StrikeEnd(Bad) for the five roles in turn
	// [BIN:TPW-EXE:0x0059DC60 breakdown score getters] 66, 68 and 69 return +0x248, +0x24C and +0x250 (RidesBreakdownImminent, NoMechanics, BusyMechanics; 70 is +0x254)
	// [BIN:TPW-EXE:0x0059E7F0 research score getters] 93-97 return +0x300 to +0x310 (the ride, add-on, shop, sideshow and feature researched scores), 133-136 +0x3F4 to +0x400 (NewResearchGroup ride, shop, sideshow, feature) and 167 the constant 0x2710
	public static readonly IReadOnlyList<(int MessageId, string ScoreKey, int? FixedScore)> EventScoreKeys = BuildEventScoreKeys();

	private static IReadOnlyList<(int, string, int?)> BuildEventScoreKeys()
	{
		var rows = new List<(int, string, int?)>();
		for ( var family = 0; family < StaffMessageFamilies.Length; family++ )
			for ( var role = 0; role < StaffMessageRoles.Length; role++ )
				rows.Add( (FirstStaffMessage + 5 * family + role, $"{StaffMessageFamilies[family]}{StaffMessageRoles[role]}.Score", null) );
		rows.Add( (66, "RidesBreakdownImminent.Score", null) );
		rows.Add( (68, "RidesBreakdownNoMechanics.Score", null) );
		rows.Add( (69, "RidesBreakdownBusyMechanics.Score", null) );
		// [BIN:TPW-EXE:0x005A02E0 ride condemned] the callback returns the word at +0x578, RideCondemned.Score
		rows.Add( (RideCondemnedMessage, "RideCondemned.Score", null) );
		rows.Add( (93, "ResearchRideResearched.Score", null) );
		rows.Add( (94, "ResearchAddonResearched.Score", null) );
		rows.Add( (95, "ResearchShopResearched.Score", null) );
		rows.Add( (96, "ResearchSideshowResearched.Score", null) );
		rows.Add( (97, "ResearchFeatureResearched.Score", null) );
		rows.Add( (133, "NewResearchGroupRide.Score", null) );
		rows.Add( (134, "NewResearchGroupShop.Score", null) );
		rows.Add( (135, "NewResearchGroupSideshow.Score", null) );
		rows.Add( (136, "NewResearchGroupFeature.Score", null) );
		rows.Add( (167, "", 0x2710) );
		// [BIN:TPW-EXE:0x004D5270 global ticket moved] the four rows 0xEA-0xED share the callback 0x59E8B0, the word at +0x324 of the Advisor.sam object, AwardMoved.Score, which sits just before HelpMessage.Score at +0x328; AutomaticAdvisor.GlobalTicketMovedMessage posts them as a global ticket moves to this park
		foreach ( var message in AwardMovedMessages )
			rows.Add( (message, AwardMovedScoreKey, null) );
		// the golden ticket wins, the first-ticket lines and the challenge offers, wins and losses (AdvisorChallengeMessages)
		rows.AddRange( AdvisorChallengeMessages.EventScoreKeys );
		return rows;
	}

	/// <summary>
	/// The messages the background scan (<see cref="AdvisorScan"/>) produces, with the key of the flat score their ported callback returns. They are defined here so the
	/// queue knows their descriptors; the scan posts them, nothing else does.
	/// </summary>
	// [BIN:TPW-EXE:0x0059E8D0 money callbacks] 100 returns +0x334 (WealthHigh.Score), 101 +0x33C (WealthHighRepayLoan.Score), 102 +0x340 (TakeLoan.Score), 103 +0x344 (InTheRedSixMonths.Score), 104 +0x348 (InTheRedThreeMonths.Score), 105 +0x34C (InTheRedMonthLeft.Score) and 107 +0x35C (WagesHigh.Score)
	public static readonly IReadOnlyList<(int MessageId, string ScoreKey, int? FixedScore)> ScanScoreKeys = new (int, string, int?)[]
	{
		(100, "WealthHigh.Score", null),
		(101, "WealthHighRepayLoan.Score", null),
		(102, "TakeLoan.Score", null),
		(103, "InTheRedSixMonths.Score", null),
		(104, "InTheRedThreeMonths.Score", null),
		(105, "InTheRedMonthLeft.Score", null),
		(107, "WagesHigh.Score", null),
		// [BIN:TPW-EXE:0x0059C9D0 park callbacks] the rides, cleanliness and guest mood rows (AdvisorParkCallbacks): a computed score has no flat key, so its row names the key of its multiplier; the rows with a flat score name that score
		(3, "VisitorsThirsty.ScorePerThirstyPerson", null), (4, "VisitorsHungry.ScorePerHungryPerson", null),
		(5, "StaffHireMechanics1.ScorePerWornRide", null), (6, "StaffHireMechanics2.ScorePerBrokenRide", null), (7, "StaffHireMechanics3.ScorePerExtraRide", null),
		(8, "StaffHireHandymen1.ScorePerLitterPoint", null), (9, "StaffHireHandymen2.ScorePerLitterPoint", null), (10, "StaffHireHandymen3.ScorePerExtra10Cells", null),
		(60, "RidesPoorSelection.Score", null), (62, "RidesBuildRides.Score", null),
		(71, "ShopsPoorSelection.Score", null), (73, "ShopsBuildShops.Score", null), (74, "ShopsBuildMoreShops.Score", null),
		(75, "SideshowsPoorSelection.Score", null), (77, "SideshowsBuildSideshows.Score", null), (78, "SideshowsBuildMoreSideshows.Score", null),
		(79, "FeaturesPoorSelection.Score", null), (81, "FeaturesBuildFeatures.Score", null),
		(82, "FeaturesNoToilets.Score", null),
		// [BIN:TPW-EXE:0x0059E420 toilet figure] the callback returns 0 on every path
		(85, "", 0),
		(86, "FeaturesDirtyToilets1.PointsPerFilthPct", null), (87, "FeaturesDirtyToilets1.PointsPerFilthPct", null),
		(88, "FeaturesDirtyToilets3.PointsPerFilthPct", null), (89, "FeaturesDirtyToilets4.PointsPerFilthPct", null),
		(145, "CongratVisitorsHappy.Score", null), (158, "CongratBalloons.Score", null), (159, "CongratCostumes.Score", null),
		(259, "RidesClosed.Score", null), (260, "ShopsClosed.Score", null), (261, "SideshowsClosed.Score", null), (262, "ToiletsClosed.Score", null),
		// [BIN:TPW-EXE:0x0059CEA0 queues, prices and research callbacks] the researchers, research-more, upgrade, ticket price and shop value rows (AdvisorQueuesCallbacks); a computed score names the key of its multiplier
		(15, "StaffHireResearchers1.PointsPerExtraPct", null), (16, "StaffHireResearchers2.Score", null), (17, "StaffHireResearchers3.Score", null),
		(61, "RidesResearchMore.Score", null), (63, "RidesNoUpgrades.Score", null), (64, "RidesMoreUpgrades.Score", null),
		(72, "ShopsResearchMore.Score", null), (76, "SideshowsResearchMore.Score", null), (80, "FeaturesResearchMore.Score", null),
		(91, "PricingTicketTooExpensive.Score", null), (92, "PricingTicketTooCheap.Score", null),
		(238, "FoodPoorValue.ScorePerPoint", null), (239, "ShopsPoorValue.ScorePerPoint", null), (240, "RestaurantPoorValue.ScorePerPoint", null), (241, "DrinksPoorValue.ScorePerPoint", null), (242, "SideshowsPoorValue.ScorePerPoint", null),
		(243, "FoodPoorSatisfaction.ScorePerPoint", null), (244, "ShopsPoorSatisfaction.ScorePerPoint", null), (245, "RestaurantPoorSatisfaction.ScorePerPoint", null), (246, "DrinksPoorSatisfaction.ScorePerPoint", null), (247, "SideshowsPoorSatisfaction.ScorePerPoint", null),
		(248, "FoodGreatValue.ScorePerPoint", null), (249, "ShopsGreatValue.ScorePerPoint", null), (250, "RestaurantGreatValue.ScorePerPoint", null), (251, "DrinksGreatValue.ScorePerPoint", null), (252, "SideshowsGreatValue.ScorePerPoint", null),
		(253, "FoodGreatSatisfaction.ScorePerPoint", null), (254, "ShopsGreatSatisfaction.ScorePerPoint", null), (255, "RestaurantGreatSatisfaction.ScorePerPoint", null), (256, "DrinksGreatSatisfaction.ScorePerPoint", null), (257, "SideshowsGreatSatisfaction.ScorePerPoint", null),
	}.Concat( AdvisorStaffCallbacks.ScoreKeys ).Concat( AdvisorChallengeMessages.ScanScoreKeys ).ToArray();

	private static IReadOnlyList<AdvisorMessageDefinition>? events;

	/// <summary>The definitions of <see cref="EventScoreKeys"/>: the response, group, pending-duplicate limit and game mode of each from the shipped descriptor table.</summary>
	public static IReadOnlyList<AdvisorMessageDefinition> EventMessages => events ??= BuildEventMessages();

	private static IReadOnlyList<AdvisorMessageDefinition> BuildEventMessages()
	{
		return Definitions( EventScoreKeys );
	}

	private static AdvisorMessageDefinition[] Definitions( IEnumerable<(int MessageId, string ScoreKey, int? FixedScore)> keys )
	{
		var rows = AdvisorDescriptorTable.Rows;
		return keys.Where( entry => entry.MessageId < rows.Count )
			.Select( entry =>
			{
				var row = rows[entry.MessageId];
				return new AdvisorMessageDefinition( entry.MessageId, row.FirstResponseId, row.ResponseCount, row.Group, row.MaximumPendingDuplicates, entry.ScoreKey, row.GameMode, entry.FixedScore );
			} )
			.ToArray();
	}

	private static IReadOnlyList<AdvisorMessageDefinition>? scanned;

	/// <summary>The definitions of <see cref="ScanScoreKeys"/>: the messages the background scan posts, with the controls of their descriptors.</summary>
	public static IReadOnlyList<AdvisorMessageDefinition> ScanMessages => scanned ??= Definitions( ScanScoreKeys );

	private static IReadOnlyList<AdvisorMessageDefinition>? unproduced;

	/// <summary>
	/// The other descriptors of the original's table (<see cref="AdvisorDescriptorTable"/>): messages nothing in OpenTPW produces yet (their score
	/// callbacks are computations of the park's state, ADVISOR-009). They have no <see cref="AdvisorMessageDefinition.ScoreKey"/>; an original save's
	/// pending advice and history of them are imported with their saved scores.
	/// </summary>
	public static IReadOnlyList<AdvisorMessageDefinition> UnproducedMessages => unproduced ??= BuildUnproducedMessages();

	private static AdvisorMessageDefinition[] BuildUnproducedMessages()
	{
		// the ids of every produced message in a set: one lookup per descriptor row instead of a scan of all messages
		var produced = AllMessages.Select( message => message.MessageId ).ToHashSet();
		return AdvisorDescriptorTable.Rows
			.Where( row => !produced.Contains( row.MessageId ) )
			.Select( row => new AdvisorMessageDefinition( row.MessageId, row.FirstResponseId, row.ResponseCount, row.Group, row.MaximumPendingDuplicates, string.Empty, row.GameMode ) )
			.ToArray();
	}

	/// <summary>
	/// Makes the message tables the advisor's settings read (<see cref="AdvisorBalance.Parse"/>), one per step, for a park built over frames
	/// (docs/BROWSER-PERFORMANCE.md, "Build steps"). Only a head start: a table that fails here is built again, and fails again, where it is read.
	/// </summary>
	internal static IEnumerable<bool> WarmSteps()
	{
		foreach ( var part in WarmParts )
		{
			try { part(); }
			catch ( Exception ) { /* only a head start */ }
			yield return true;
		}
	}

	/// <summary>The steps of <see cref="WarmSteps"/>.</summary>
	internal static int WarmStepCount => WarmParts.Length;

	private static readonly Action[] WarmParts = { () => _ = EventMessages, () => _ = ScanMessages, () => _ = HelpMessages, () => _ = UnproducedMessages };

	/// <summary>Whether a message's game-mode selector admits the game type (2 = Instant Action, else the full simulation).</summary>
	// [BIN:TPW-EXE:0x0059C0A0 descriptor score] selector 2 always passes, 0 passes only while the game-mode global 0xFB3B7C is 0, 1 only while it is 2; any other state scores -1 and nothing is said
	public static bool GameModeAccepts( byte selector, int gameType ) => selector switch
	{
		0 => gameType == 0,
		1 => gameType == 2,
		_ => true,
	};

	/// <summary>
	/// The advice a game event constructs, in construction order. <paramref name="gameType"/> is the
	/// original game type (2 = Instant Action).
	/// </summary>
	public static IReadOnlyList<int> AdviceFor( AdvisorGameEvent gameEvent, int gameType ) => gameEvent switch
	{
		// [BIN:STP-PPC:0x10009548 advisor event 0] advice 0 (Welcome); then advice 323 (PrebuiltPark) when the game type at TOC −30136 is 2 (0x100096F0–0x10009720; the player selector 0x1013781C writes 2 there for Instant Action)
		AdvisorGameEvent.LevelStarted => gameType == 2 ? new[] { 0, 323 } : new[] { 0 },
		// [BIN:STP-PPC:0x100098D4 advisor event 2] advice 106 (Bankrupted)
		AdvisorGameEvent.Bankrupted => new[] { 106 },
		// [BIN:STP-PPC:0x10009A88 advisor event 3] advice 128 (ParkNowOpen)
		AdvisorGameEvent.ParkNowOpen => new[] { 128 },
		// [BIN:STP-PPC:0x10009C3C advisor event 4] advice 129 (ParkNowClosed)
		AdvisorGameEvent.ParkNowClosed => new[] { 129 },
		_ => Array.Empty<int>(),
	};
}

/// <summary>
/// The scoring controls of <c>Advisor/Advisor.sam</c> used by the bound messages: the general
/// minimum score and intervals, the per-group repeat/once/slap controls and the configured scores.
/// </summary>
internal sealed class AdvisorBalance
{
	// [DATA:Advisor/Advisor.sam] loaded by the scoring data loader (STP-PPC 0x1000CFD4)
	public const string Path = "/Advisor/Advisor.sam";
	/// <summary>Upper bound for the settings file (the shipped one is about 20 KB).</summary>
	public const int MaximumBytes = 1 << 20;

	public required int MinimumScore { get; init; }
	public required int MinimumTimeAnyMessage { get; init; }
	public required int MinimumTimeSameMessage { get; init; }
	public required IReadOnlyDictionary<int, (uint RepeatGroups, bool SayOnlyOnce, uint DiscardAfterSlaps)> Groups { get; init; }
	public required IReadOnlyDictionary<string, int> Scores { get; init; }
	/// <summary><c>HelpMessage.ContextHelpScore</c>: the score F1 gives the replayed last tutorial advice (0 when the file lacks it, which leaves the replay silent).</summary>
	public int ContextHelpScore { get; init; }

	public static AdvisorBalance Load() => Parse( FileSystem.ReadAllBytes( Path ), Path );

	public static AdvisorBalance Parse( byte[] bytes, string source )
	{
		ArgumentNullException.ThrowIfNull( bytes );
		if ( bytes.Length > MaximumBytes )
			throw new InvalidDataException( $"{source} is {bytes.Length} bytes; advisor settings are limited to {MaximumBytes}." );
		var settings = new SamSettings( new[] { SamDocument.Parse( Encoding.Latin1.GetString( bytes ), source ) } );
		var groups = new Dictionary<int, (uint, bool, uint)>();
		// the global-ticket messages' group is read when the file has it, like the unproduced messages' (the shipped file has it)
		var scanned = AdvisorTables.ScanMessages.Select( message => message.MessageId ).ToHashSet();
		foreach ( var group in AdvisorTables.AllMessages.Where( message => !AdvisorTables.GroupIsOptional( message.MessageId ) && !scanned.Contains( message.MessageId ) ).Select( message => (int)message.Group ).Distinct() )
		{
			// [BIN:STP-PPC:0x10016F4C advisor balance] MessageGroups[g] fields at +36/+40/+44 + 12g
			var prefix = $"MessageGroups[{group}].";
			groups[group] = (checked((uint)settings.GetInt( prefix + "MinTimeSameMessage" )), settings.GetInt( prefix + "SayOnlyOnce" ) != 0,
				checked((uint)settings.GetInt( prefix + "DiscardAfterSlaps" )));
		}
		// The groups only the unproduced messages use are read when the file has them (the shipped one has groups 0 to 5 and 9); without a group's controls
		// the messages of that group have no descriptor.
		foreach ( var group in AdvisorTables.UnproducedMessages.Concat( AdvisorTables.ScanMessages ).Concat( AdvisorTables.EventMessages.Where( message => AdvisorTables.GroupIsOptional( message.MessageId ) ) ).Select( message => (int)message.Group ).Distinct().Where( group => !groups.ContainsKey( group ) ) )
		{
			var prefix = $"MessageGroups[{group}].";
			const int missing = int.MinValue;
			var minimum = settings.GetInt( prefix + "MinTimeSameMessage", missing, optional: true );
			var once = settings.GetInt( prefix + "SayOnlyOnce", missing, optional: true );
			var slaps = settings.GetInt( prefix + "DiscardAfterSlaps", missing, optional: true );
			if ( minimum is not missing && once is not missing && slaps is not missing )
				groups[group] = (checked((uint)minimum), once != 0, checked((uint)slaps));
		}
		return new AdvisorBalance
		{
			// [BIN:STP-PPC:0x10016F4C advisor balance] GeneralAdvisor.MinTimeAnyMessage +24, MinTimeSameMessage +28, MinScoreForConsideration +32
			MinimumScore = settings.GetInt( "GeneralAdvisor.MinScoreForConsideration" ),
			// [BIN:TPW-EXE:0x0059BEA0 advisor balance] absence result (medium-high confidence): no code reads fields +0x18/+0x1C (MinTimeAnyMessage/MinTimeSameMessage) of the Advisor.sam object (static 0xFB3540, built by 0x59BEA0); +0x20 (MinScore) is read at 0x59B6C3. They are loaded and not applied
			MinimumTimeAnyMessage = settings.GetInt( "GeneralAdvisor.MinTimeAnyMessage" ),
			MinimumTimeSameMessage = settings.GetInt( "GeneralAdvisor.MinTimeSameMessage" ),
			Groups = groups,
			// The help score is optional: a settings file without it leaves the help silent instead of disabling the advisor.
			ContextHelpScore = settings.GetInt( AdvisorTables.ContextHelpScoreKey, 0, optional: true ),
			ScanParameters = ReadScanParameters( settings ),
			Restored = RestoredAdviceSettings.Read( settings ),
			Scores = AdvisorTables.AllMessages.Where( message => message.FixedScore == null ).Select( message => message.ScoreKey ).Distinct( StringComparer.OrdinalIgnoreCase )
				.ToDictionary( key => key, key => key == AdvisorTables.HelpScoreKey || key == AdvisorTables.ChallengeScoreKey || key == AdvisorTables.MechanicOnTheWayScoreKey || AdvisorTables.PrankScoreKeys.Contains( key, StringComparer.OrdinalIgnoreCase )
					|| AdvisorTables.EventScoreKeys.Concat( AdvisorTables.ScanScoreKeys ).Any( entry => string.Equals( entry.ScoreKey, key, StringComparison.OrdinalIgnoreCase ) )
					? settings.GetInt( key, 0, optional: true ) : settings.GetInt( key ), StringComparer.OrdinalIgnoreCase ),
		};
	}

	/// <summary>[EXT:restored-advice] The optional keys of the restored messages' callbacks (<see cref="RestoredAdviceSettings"/>); empty when the file has none.</summary>
	public RestoredAdviceSettings Restored { get; init; } = RestoredAdviceSettings.Empty;

	/// <summary>The thresholds the ported scan callbacks read besides their scores (<see cref="AdvisorScan.ParameterKeys"/>); a key the file lacks is absent and leaves its callback silent.</summary>
	public IReadOnlyDictionary<string, double> ScanParameters { get; init; } = new Dictionary<string, double>( StringComparer.OrdinalIgnoreCase );

	// [BIN:TPW-EXE:0x0072E124 advisor.sam schema] WealthHigh.MoreMoneyThan (+0x330) and WagesHigh.AverageOverMonths (+0x358) are words; WealthHighRepayLoan.Margin (+0x338) and WagesHigh.WagesTimesHigherThanIncome (+0x354) are singles (the callbacks load them with fmul dword), so a value like 1.2 is read as a 32-bit float
	private static Dictionary<string, double> ReadScanParameters( SamSettings settings )
	{
		var values = new Dictionary<string, double>( StringComparer.OrdinalIgnoreCase );
		foreach ( var key in AdvisorScan.IntegerParameterKeys.Where( settings.Contains ) )
			values[key] = settings.GetInt( key );
		foreach ( var key in AdvisorScan.FloatParameterKeys.Where( settings.Contains ) )
			values[key] = (float)settings.GetDouble( key );
		return values;
	}

	public IEnumerable<AdvisorMessageDescriptor> Descriptors() => AdvisorTables.AllMessages.Concat( AdvisorTables.UnproducedMessages ).Where( message => Groups.ContainsKey( message.Group ) ).Select( message =>
	{
		var group = Groups[message.Group];
		return new AdvisorMessageDescriptor( message.MessageId, message.FirstResponseId, message.ResponseCount, message.Group,
			group.RepeatGroups, group.SayOnlyOnce, group.DiscardAfterSlaps, message.MaximumPendingDuplicates );
	} );

	public int ScoreOf( int messageId )
	{
		var message = AdvisorTables.AllMessages.Single( item => item.MessageId == messageId );
		return message.FixedScore ?? Scores[message.ScoreKey];
	}
}

/// <summary>
/// The advisor controller for the wired game events: the event handler builds pending advice with
/// its configured score, and <see cref="Update"/> picks, consumes and completes one response at a
/// time through <see cref="AdvisorScoreQueue"/>. Playback is a callback taking the response ID and
/// returning the action span (null when the response could not be played, e.g. absent from the table;
/// the attempt still succeeds, with span 0, as in the original wrapper).
/// </summary>
internal sealed class AdvisorController
{
	/// <param name="balance">The advisor's settings.</param>
	/// <param name="scanCallbacks">The score callbacks the scan runs (tests substitute their own); null uses the ported ones (<see cref="AdvisorScanCallbacks.Implemented"/>).</param>
	public AdvisorController( AdvisorBalance balance, IReadOnlyDictionary<uint, AdvisorScoreCallback>? scanCallbacks = null )
	{
		Balance = balance ?? throw new ArgumentNullException( nameof( balance ) );
		Queue = new AdvisorScoreQueue( balance.Descriptors(), new Dictionary<int, AdvisorMessageHistory>(), balance.MinimumScore );
		Scan = new AdvisorScan( AdvisorDescriptorTable.Rows, scanCallbacks ?? AdvisorScanCallbacks.Implemented, RestoredAdviceScan.CallbackFor );
	}

	/// <summary>The background scan and its cursor (the controller's only state besides the queue).</summary>
	public AdvisorScan Scan { get; }

	public AdvisorBalance Balance { get; }
	public AdvisorScoreQueue Queue { get; }

	/// <summary>Handles one <c>CMsgEvent</c>; returns the admissions of the advice it constructed.</summary>
	public IReadOnlyList<(int MessageId, AdvisorQueueAdmission Admission)> HandleGameEvent( AdvisorGameEvent gameEvent, uint liveGameTick, int gameType, bool tutorialEnabled )
	{
		if ( gameEvent == AdvisorGameEvent.ResetForEasyMode )
		{
			Queue.ClearHistory();
			return Array.Empty<(int, AdvisorQueueAdmission)>();
		}
		var result = new List<(int, AdvisorQueueAdmission)>();
		foreach ( var messageId in AdvisorTables.AdviceFor( gameEvent, gameType ) )
			result.Add( (messageId, Queue.Enqueue( new AdvisorPendingAdvice( messageId, Balance.ScoreOf( messageId ) ), liveGameTick, tutorialEnabled )) );
		return result;
	}

	/// <summary>
	/// A window or tool asks for its help (the original's 0x486B00): the message becomes pending advice with the help score, subject to
	/// the queue's admission rules (tutorial option, once-only, slaps, duplicates, repeat interval) and to the descriptor's game-mode
	/// selector. Returns null for a message the controller has no descriptor for, or one the game mode excludes.
	/// </summary>
	public AdvisorQueueAdmission? Advise( int messageId, uint liveGameTick, int gameType, bool tutorialEnabled, int variant = -1 )
	{
		var definition = AdvisorTables.AllMessages.FirstOrDefault( message => message.MessageId == messageId );
		if ( definition.ScoreKey != null && !Queue.HasDescriptor( messageId ) )
		{
			Log.Trace( $"Advisor advice {messageId} not posted: the settings file has no controls for its group {definition.Group}." );
			return null;
		}
		if ( definition.ScoreKey == null || !AdvisorTables.GameModeAccepts( definition.GameMode, gameType ) )
			return null;
		return Queue.Enqueue( new AdvisorPendingAdvice( messageId, Balance.ScoreOf( messageId ), variant ), liveGameTick, tutorialEnabled );
	}

	/// <summary>
	/// One advisor update's scan (<c>0x0059A550</c> calling <c>0x0059C680</c> with a count of 1): scores the next scanned descriptor from the park state and, when the
	/// score is above <c>MinScoreForConsideration</c>, builds the record with that score and queues it. <paramref name="money"/> is null without a park.
	/// </summary>
	// [BIN:TPW-EXE:0x0059A58B scan and post] the result row is scored again (0x59BF20: the same callback on the same state), only a score above the object's +0x20 (MinScoreForConsideration, 0xFB3560, signed) builds a record (0x59B590) and queues it (0x59A940)
	public AdvisorScanOutcome ScanOnce( IAdvisorMoneyState? money, uint liveGameTick, int gameType, bool tutorialEnabled, IAdvisorParkState? park = null, IRestoredAdviceState? restored = null )
	{
		var context = new AdvisorScanContext( Balance, money, park ) { Restored = restored };
		var result = Scan.Step( context, gameType, ( messageId, score ) =>
			Queue.HasDescriptor( messageId ) && RestoredAdvice.RepeatAllows( Queue, messageId, liveGameTick ) && Queue.Eligibility( new AdvisorPendingAdvice( messageId, score ), liveGameTick, tutorialEnabled ) == AdvisorEligibility.Eligible );
		// the original has no test for "no message" here: with a minimum below -1 the -1 of the empty result would be queued; the empty result is not posted (the shipped minimum is 25)
		if ( result.MessageId == AdvisorScan.NoMessage || result.Score <= Queue.MinimumScore )
			return new AdvisorScanOutcome( result.ScoredRow, null, null );
		var admission = Queue.Enqueue( new AdvisorPendingAdvice( result.MessageId, result.Score ), liveGameTick, tutorialEnabled );
		return new AdvisorScanOutcome( result.ScoredRow, new AdvisorPendingAdvice( result.MessageId, result.Score ), admission );
	}

	/// <summary>
	/// [EXT:restored-advice] Posts a restored message the way game code posts advice (<c>0x59B590</c> and <c>0x59A940</c>): with the score of <see cref="RestoredAdvice.ScoreOf"/> (null leaves it silent), subject to the queue's admission
	/// rules (tutorial option, once-only, slaps, duplicates, repeat interval) and the descriptor's game-mode selector. Null for a message the extension does not restore, one without a descriptor or a score, one the game mode excludes or one its own repeat rule holds back (<see cref="RestoredAdviceEntry.RepeatGroups"/>).
	/// </summary>
	/// <param name="variant">The response to say (the caption of the matching ticket, say); -1 takes the descriptor's next response.</param>
	public AdvisorQueueAdmission? AdviseRestored( int messageId, uint liveGameTick, int gameType, bool tutorialEnabled, int variant = -1 )
	{
		if ( RestoredAdvice.ScoreOf( Balance, messageId ) is not { } score || !Queue.HasDescriptor( messageId ) || messageId >= AdvisorDescriptorTable.Rows.Count
			|| !AdvisorTables.GameModeAccepts( AdvisorDescriptorTable.Rows[messageId].GameMode, gameType ) || !RestoredAdvice.RepeatAllows( Queue, messageId, liveGameTick ) )
			return null;
		return Queue.Enqueue( new AdvisorPendingAdvice( messageId, score, variant ), liveGameTick, tutorialEnabled );
	}

	/// <summary>
	/// F1 in a HUD window (the default window procedure 0x488BA0, key 0x70 without modifiers): the last tutorial advice the queue kept is
	/// offered again at <c>HelpMessage.ContextHelpScore</c>, bypassing its once-only and slap checks. Null when none is kept.
	/// </summary>
	// [BIN:TPW-EXE:0x00488C02 F1 replay] message 0x1000B with key 0x70 and no modifier byte calls the controller getter 0x5194D0 and 0x59AB50
	public AdvisorQueueAdmission? ReplayLastTutorial( uint liveGameTick, bool tutorialEnabled ) =>
		Queue.ReplayLastTutorial( Balance.ContextHelpScore, liveGameTick, tutorialEnabled );

	/// <summary>The window or tool no longer needs its help (the original's 0x486B40): see <see cref="AdvisorScoreQueue.Withdraw"/>.</summary>
	public AdvisorWithdrawal Withdraw( int messageId, uint advisorClock ) => Queue.Withdraw( messageId, advisorClock );

	/// <summary>
	/// The interface mode changed: the help of the tools and modes is withdrawn (<see cref="AdvisorUiHelp.ModeHelpMessages"/>); true when
	/// one of them was being spoken.
	/// </summary>
	public bool WithdrawModeHelp( uint advisorClock )
	{
		var cancelled = false;
		foreach ( var messageId in AdvisorUiHelp.ModeHelpMessages )
			cancelled |= Queue.Withdraw( messageId, advisorClock ).SpeechCancelled;
		return cancelled;
	}

	/// <summary>
	/// The controller's own-format state for a park save: its queue's pending records, history and action
	/// reservation (an <see cref="ImportOriginal"/> import takes this path too).
	/// </summary>
	/// <param name="Queue">The queue's pending records, history and reservation.</param>
	/// <param name="ScanCursor">The background scan's cursor; a snapshot from before the scan existed restores it at 0.</param>
	internal sealed record AdvisorControllerSnapshot( AdvisorQueueSnapshot Queue, int ScanCursor = 0 );

	/// <summary>The controller's state for a park save; fails explicitly while a playback attempt is outstanding.</summary>
	public AdvisorControllerSnapshot CaptureSnapshot() => new( Queue.CaptureSnapshot(), Scan.Cursor );

	/// <summary>Checks a snapshot against this controller's bound messages without touching it.</summary>
	public void ValidateSnapshot( AdvisorControllerSnapshot snapshot )
	{
		ArgumentNullException.ThrowIfNull( snapshot );
		if ( snapshot.ScanCursor is < 0 or > AdvisorScan.DescriptorCount )
			throw new InvalidDataException( $"Advisor controller snapshot is inconsistent: the scan cursor {snapshot.ScanCursor} is outside 0 to {AdvisorScan.DescriptorCount}." );
		Queue.ValidateSnapshot( snapshot.Queue );
	}

	/// <summary>
	/// Continues this controller (fresh, with the same balance) from a validated snapshot. Restore after the
	/// level-start game events of <see cref="AutomaticAdvisor.AttachLevel"/> so the saved pending advice and
	/// history replace the freshly raised welcome advice.
	/// </summary>
	public void RestoreSnapshot( AdvisorControllerSnapshot snapshot )
	{
		ValidateSnapshot( snapshot );
		Queue.RestoreSnapshot( snapshot.Queue );
		Scan.RestoreCursor( snapshot.ScanCursor );
	}

	/// <summary>
	/// Takes the pending records and per-message history of an original park save's advisor controller (model 11).
	/// Free (invalid) records stay free; records and non-initial history of messages without a descriptor are
	/// dropped and counted. Returns a report line for the log.
	/// </summary>
	// [BIN:STP-PPC:0x100082E0 advisor controller serializer] eight 21-byte pending records (0x1000BC10) from controller +20, then 351 16-byte history entries (tick, variant, played, slaps) from +224, indexed by message ID
	public string ImportOriginal( SaveAdvisorController saved )
	{
		ArgumentNullException.ThrowIfNull( saved );
		var pending = new AdvisorPendingAdvice?[AdvisorScoreQueue.Capacity];
		var imported = 0;
		var dropped = new List<uint>();
		var fields = new List<string>();
		for ( var index = 0; index < pending.Length; index++ )
		{
			var record = saved.Pending[index];
			if ( !record.Valid )
				continue;
			if ( record.MessageId > int.MaxValue || !Queue.HasDescriptor( (int)record.MessageId ) )
			{
				dropped.Add( record.MessageId );
				continue;
			}
			pending[index] = new AdvisorPendingAdvice( (int)record.MessageId, record.Score, record.ResponseNo, record.OverrideOnlyOnce != 0 );
			fields.Add( $"message {record.MessageId} thing {record.ThingId} secondary {record.Secondary} score overridden {record.ScoreOverridden}" );
			imported++;
		}
		var history = new Dictionary<int, AdvisorMessageHistory>();
		var droppedHistory = 0;
		for ( var messageId = 0; messageId < saved.History.Count; messageId++ )
		{
			var entry = saved.History[messageId];
			if ( !Queue.HasDescriptor( messageId ) )
			{
				if ( !entry.IsInitial )
					droppedHistory++;
				continue;
			}
			history[messageId] = new AdvisorMessageHistory( entry.SavedGameTick, entry.PreviousVariant, entry.HasBeenPlayed, entry.SlapCount );
		}
		Queue.Restore( pending, history );
		// [BIN:TPW-EXE:0x0059BA30 record serializer] the controller's save and load pass the record at +0x16D0 through it (0x59A1C6, 0x59A49D), so the save's current record is the last tutorial advice
		var current = saved.Current;
		var lastTutorial = current.Valid && current.MessageId <= int.MaxValue && Queue.HasDescriptor( (int)current.MessageId ) && Queue.IsTutorial( (int)current.MessageId )
			? new AdvisorPendingAdvice( (int)current.MessageId, current.Score, current.ResponseNo, current.OverrideOnlyOnce != 0 ) : null;
		Queue.RestoreLastTutorial( lastTutorial );
		// [BIN:TPW-EXE:0x0076E300 descriptor table] the original's 351 descriptors (message = record index; filled by 0x5A0A50) are all bound (AdvisorDescriptorTable), so every message an original save can name keeps its pending advice and history; only an ID past the table, or a message of a group the settings file lacks, has no descriptor
		return $"Advisor controller imported: {imported} pending advice, history of {history.Count} messages; dropped {dropped.Count} pending advice of messages without a descriptor ({string.Join( ", ", dropped )}) and {droppedHistory} history entries without one; last tutorial advice {(lastTutorial != null ? $"message {lastTutorial.MessageId} kept" : current.Valid ? $"message {current.MessageId} dropped (no descriptor or not a tutorial)" : "none")}; the imported records' thing, secondary and score-overridden fields are not kept ({string.Join( "; ", fields )}); the controller's other values are reported under SAVE-010.";
	}

	/// <summary>
	/// One controller update: when not busy, selects the best pending advice, consumes it, plays its
	/// response through <paramref name="play"/> and records the outcome. Returns the attempted selection.
	/// </summary>
	public AdvisorResponseSelection? Update( Func<uint> advisorClock, Func<uint> liveGameTick, Func<int, uint?> play )
	{
		ArgumentNullException.ThrowIfNull( advisorClock );
		ArgumentNullException.ThrowIfNull( liveGameTick );
		ArgumentNullException.ThrowIfNull( play );
		if ( Queue.SelectNext( advisorClock() ) is not { } selection )
			return null;
		Queue.BeginPlaybackAttempt( selection );
		uint? span = null;
		try
		{
			span = play( selection.ResponseId );
		}
		finally
		{
			// [BIN:STP-PPC:0x1000BBF0 controller playback wrapper] the wrapper (sole caller 0x100089FC) fails only for an invalid record or the missing-descriptor response 614, which the bound rows cannot produce; the player's result (0x10006B7C, stored at 0x1000BB64) is kept only as the span, so an unplayed response still records the history and reserves span + 1000 with span 0
			Queue.CompletePlaybackAttempt( selection, playbackSucceeded: true, advisorClock(), liveGameTick(), span ?? 0 );
		}
		return selection;
	}
}

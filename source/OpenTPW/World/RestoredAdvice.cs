namespace OpenTPW;

/// <summary>How the restored advice extension makes a message reach the advisor's queue.</summary>
internal enum RestoredTrigger
{
	/// <summary>The row is a scan-zero row of the descriptor table: the background scan scores it, through the extension's hook in place of the constant-zero stub <c>0x005D60C0</c>.</summary>
	Scan,
	/// <summary>The row is skipped by the scan (game code posts it) and nothing in tp.exe does: the extension looks at the park state once per scan call and posts it when it holds.</summary>
	Poll,
	/// <summary>A game event posts it (a ticket won, an upgrade bought, a vehicle arriving).</summary>
	Event,
	/// <summary>A window, tool or screen of the player posts it as a help message (the descriptor already carries the help score).</summary>
	Help,
}

/// <summary>
/// One message the extension restores. <see cref="ScoreKey"/> is the <c>Advisor.sam</c> key of the score the message's own descriptor callback reads (the callbacks of the
/// unused rows are getters of one key each); a message whose callback is the constant-zero stub has none, and <see cref="ExtScore"/> is the extension's own choice
/// (<c>[EXT:restored-advice-topic]</c>, documented in docs/reverse/ADVISOR-SCAN.md). <see cref="RepeatGroups"/> is the extension's own repeat rule on top of the descriptor group's (0: none, <see cref="RestoredAdvice.Once"/>:
/// once per park, else groups of four turns before the message may be said again; <see cref="AfterMessage"/>: the message is only said after that original line has been, as the follow-up of it): a condition that holds for a long time must not be said every two minutes.
/// </summary>
internal sealed record RestoredAdviceEntry( int MessageId, string Topic, RestoredTrigger Trigger, string? ScoreKey, int ExtScore, string Reason, int RepeatGroups = 0, int AfterMessage = 0 )
{
	/// <summary>The fidelity label of the trigger.</summary>
	public string Label => $"[EXT:restored-advice-{Topic}]";
}

/// <summary>
/// The extension's optional <c>Advisor.sam</c> keys: the scores and thresholds of the restored messages' own callbacks and of their siblings. A key the settings file lacks is absent and
/// leaves its message silent, as for every other optional score; the shipped file has all of them.
/// </summary>
internal sealed class RestoredAdviceSettings
{
	/// <summary>The integer keys the restored callbacks read.</summary>
	public static readonly IReadOnlyList<string> IntegerKeys = new[]
	{
		"StaffSetGuardArea.MonthsIntoGameForConsideration", "StaffSetGuardArea.AtLeastThisMany", "StaffGuardCoverage.Score", "StaffGuardCoverage.ScorePerNonCoveredPercent",
		"RidesNoUpgrades.MonthsIntoGameForConsideration", "RidesNoUpgrades.Score", "RideExitBlocked.Score", "RideCondemned.Score",
		"FeaturesNoToilets.Score", "FeaturesNoToilets.NeedingMoreThan", "FeaturesNoToilets.MorePeopleThanThisNeeding", "FeaturesFarToToilets.Score",
		"GoldTicketNearToXProfit.Score", "GeneralWonGoldenTicket.Score", "AwardGiven.Score",
		"UpgradeHireMechanic.Score", "UpgradeStikingMechanics.Score", "NewUpgradeNoMechanic.Score",
		"BusArrived.Score", "PlaneArrived.Score", "BoatArrived.Score",
	};

	/// <summary>The single-precision keys the restored callbacks read.</summary>
	public static readonly IReadOnlyList<string> FloatKeys = new[] { "GoldTicketNearToXProfit.ProfitMultiplier" };

	/// <summary>No keys: every restored message that needs one is silent.</summary>
	public static RestoredAdviceSettings Empty { get; } = new( new Dictionary<string, double>() );

	private readonly IReadOnlyDictionary<string, double> values;

	public RestoredAdviceSettings( IReadOnlyDictionary<string, double> values ) =>
		this.values = new Dictionary<string, double>( values ?? throw new ArgumentNullException( nameof( values ) ), StringComparer.OrdinalIgnoreCase );

	/// <summary>Reads the keys the settings file has.</summary>
	public static RestoredAdviceSettings Read( SamSettings settings )
	{
		ArgumentNullException.ThrowIfNull( settings );
		var values = new Dictionary<string, double>( StringComparer.OrdinalIgnoreCase );
		foreach ( var key in IntegerKeys.Where( settings.Contains ) )
			values[key] = settings.GetInt( key );
		foreach ( var key in FloatKeys.Where( settings.Contains ) )
			values[key] = (float)settings.GetDouble( key );
		return new RestoredAdviceSettings( values );
	}

	public bool Has( string key ) => values.ContainsKey( key );

	/// <summary>An integer key's value; null when the settings file lacks it.</summary>
	public int? Int( string key ) => values.TryGetValue( key, out var value ) ? (int)value : null;

	/// <summary>A single-precision key's value; null when the settings file lacks it.</summary>
	public double? Float( string key ) => values.TryGetValue( key, out var value ) ? value : null;
}

/// <summary>
/// The restored advice extension (<c>opentpw.restored-advice</c>, docs/reverse/ADVISOR-SCAN.md "Restored advice"): triggers for advisor messages the shipped data has (speech, often a
/// caption) but that tp.exe v2.0 never plays. The catalog is the single list of what the extension wires; <c>content/data/advisor-producers.toml</c> marks the same rows with a
/// <c>restored</c> topic and <c>AdvisorRestoredAdviceTests</c> keeps the two equal.
/// </summary>
/// <remarks>
/// The extension only reads park state and posts advice through the advisor's own queue (<see cref="AdvisorController.AdviseRestored"/>, the scan hook, the help messages' <see cref="AdvisorController.Advise"/>). The
/// advisor is outside the world state hash and the controller's snapshot has the same shape with or without it, so a save made on one side loads on the other (docs/MODS.md).
/// </remarks>
// [EXT:restored-advice] OpenTPW's own triggers; tp.exe v2.0 posts none of these messages
internal static class RestoredAdvice
{
	/// <summary>Whether the extension is on for this run (on by default, <see cref="FeatureGates.RestoredAdvice"/>).</summary>
	public static bool Enabled => FeatureGates.RestoredAdvice;

	/// <summary>[EXT:restored-advice] The extension's repeat rules (<see cref="RestoredAdviceEntry.RepeatGroups"/>, groups of four turns): once per park; a long interval (5400 groups, about 1.5 real hours at normal speed) for a condition that stays true;
	/// a moderate one (900, about 15 minutes) for what a player can fix at once.</summary>
	public const int Once = -1, LongRepeatGroups = 5400, ModerateRepeatGroups = 900;

	/// <summary>[EXT:restored-advice-security] the cover lines (24, 25) never outrank the originals' warnings (the shipped money, breakdown and mechanic lines score 50 to 110).</summary>
	public const int SecurityScoreCap = 60;

	// ---- the extension's own scores, for the messages whose callback is the constant-zero stub and so names no Advisor.sam key ----
	/// <summary>[EXT:restored-advice-queues] the queue lines (58, 59): above <c>MinScoreForConsideration</c> (25 shipped), below the mechanic and money warnings.</summary>
	public const int QueueScore = 40, QueueWorseScore = 45;
	/// <summary>[EXT:restored-advice-fame] the congratulations about a record-sized attraction (151-154) are good news: a low score, said when nothing urgent waits.</summary>
	public const int FameScore = 30;
	/// <summary>[EXT:restored-advice-shops] the souvenir remark (160), good news.</summary>
	public const int SouvenirScore = 30;
	/// <summary>[EXT:restored-advice-land] the little-room line (173), a hint.</summary>
	public const int LandScore = 35;
	/// <summary>[EXT:restored-advice-money] the low-balance lines (347, 348), above the early in-the-red warning 104 (50), below the take-a-loan line 102 (71) of the same park.</summary>
	public const int LowBalanceScore = 45, LowBalanceLoanScore = 50;
	/// <summary>[EXT:restored-advice-tickets] a ticket near the profit target (277): the near-ticket key is used for 318, this one has none.</summary>
	public const int ProfitHintScore = 30;

	// ---- thresholds the original's callbacks do not have (they are the extension's own, labelled where they are used) ----
	/// <summary>[EXT:restored-advice-queues] a queue of at least this many guests is long (a queue of 20 is routine in a busy park).</summary>
	public const int LongQueueGuests = 30;
	/// <summary>[EXT:restored-advice-queues] the worse line needs this many long queues, or half of the open rides when that is fewer (at least two).</summary>
	public const int WorseQueueRides = 3;
	/// <summary>[EXT:restored-advice-fame] a record of at least this percent of its golden ticket's threshold is "big".</summary>
	public const int BigRecordPercent = 75;
	/// <summary>[EXT:restored-advice-shops] the souvenir line needs this many retail sales in the last two months, and as many as half the visitors of the same two months.</summary>
	public const int SouvenirMinimumSales = 30;
	/// <summary>[EXT:restored-advice-land] free owned cells below this percent of the owned cells.</summary>
	public const int LandFreePercent = 8;
	/// <summary>[EXT:restored-advice-toilets] a guest this many cells (Manhattan) from the nearest toilet is far from one.</summary>
	public const int FarToiletCells = 24;
	/// <summary>[EXT:restored-advice-tickets] the profit near-ticket line (277) starts at this percent of the ticket's threshold; the original's near-ticket multiplier (318) takes over above its own.</summary>
	public const int ProfitHintPercent = 50;

	/// <summary>The window the player opens that restored help messages ride on, by <see cref="UI.Original.UiScreen.Name"/> (<see cref="AdvisorUiHelp.Windows"/> has the original's).</summary>
	public static readonly IReadOnlyDictionary<string, int> WindowHelp = new Dictionary<string, int>( StringComparer.Ordinal )
	{
		["staffCosts"] = 196,
		["rideUpgrade"] = 211,
	};

	/// <summary>The restored help messages a tool or a hook posts through <see cref="Hud.ParkHud"/>'s help channel.</summary>
	public const int RotateBlueprintHelp = 202, TrackRideHelp = 204, CamcorderHelp = 306, MessageIconHelp = 322, PopUpHelpOffHelp = 283;

	/// <summary>The online screens' help messages (<see cref="OnlineScreenHelp"/>).</summary>
	public const int PublishHelp = 328, ParkDetailsHelp = 329, OtherParksHelp = 331, FindPlayerHelp = 332, OutboxHelp = 333, WorldHelp = 334;

	private static RestoredAdviceEntry Scan( int message, string topic, string? key, int ext, string reason, int repeat = LongRepeatGroups ) => new( message, topic, RestoredTrigger.Scan, key, ext, reason, repeat );
	private static RestoredAdviceEntry Poll( int message, string topic, string? key, int ext, string reason, int repeat = LongRepeatGroups, int after = 0 ) => new( message, topic, RestoredTrigger.Poll, key, ext, reason, repeat, after );
	private static RestoredAdviceEntry Event( int message, string topic, string? key, int ext, string reason, int repeat = 0 ) => new( message, topic, RestoredTrigger.Event, key, ext, reason, repeat );
	private static RestoredAdviceEntry Help( int message, string topic, string reason ) => new( message, topic, RestoredTrigger.Help, AdvisorTables.HelpScoreKey, 0, reason );

	private static readonly string ParkCovered = "path cells inside a guard patrol area or with a security level (camera or guard)";

	/// <summary>Every message the extension wires, with its trigger and the reason it fires.</summary>
	public static IReadOnlyList<RestoredAdviceEntry> Entries { get; } = new[]
	{
		// ---- the scan-zero rows (the callback hook) ----
		Scan( 24, "security", "StaffGuardCoverage.Score", 0, $"some security exists (a guard or a camera) but {ParkCovered} leave 9 percent or more of the park's path cells uncovered; the siblings' formula (the uncovered percent x ScorePerNonCoveredPercent, at least Score) after StaffSetGuardArea's months" ),
		Scan( 25, "security", "StaffGuardCoverage.ScorePerNonCoveredPercent", 0, "no guard and no camera at all in a park of 30 path cells or more, after StaffSetGuardArea's months: the score is capped" ),
		Scan( 58, "queues", null, QueueScore, $"an open ride's queue holds {LongQueueGuests} guests or more and the worse line (59) does not hold" ),
		Scan( 59, "queues", null, QueueWorseScore, $"{WorseQueueRides} open rides (or half of them, at least 2) have queues of {LongQueueGuests} or more; caption only, its speech sample is a silent stub" ),
		Scan( 65, "upgrades", "RidesNoUpgrades.Score", 0, "after RidesNoUpgrades' months: a built ride has a researched upgrade to buy and no upgrade has ever been bought" ),
		Scan( 151, "fame", null, FameScore, $"the park's best roller coaster height record is {BigRecordPercent} percent or more of the height golden ticket's threshold but the ticket is not won", Once ),
		Scan( 152, "fame", null, FameScore, "the park holds the roller coaster height golden ticket", Once ),
		Scan( 153, "fame", null, FameScore, $"the park's best go-kart excitement record is {BigRecordPercent} percent or more of the golden ticket's threshold", Once ),
		Scan( 154, "fame", null, FameScore, $"the park's longest water ride record is {BigRecordPercent} percent or more of the golden ticket's threshold", Once ),
		Scan( 160, "shops", null, SouvenirScore, $"retail shops sold {SouvenirMinimumSales} or more items in the last two months, as many as half the visitors of those months", Once ),
		Scan( 173, "land", null, LandScore, $"free owned cells are under {LandFreePercent} percent of the owned terrain cells" ),
		Scan( 265, "exits", "RideExitBlocked.Score", 0, "a ride's exit cell has no path in front of it (the new exit-blocked tracking)" ),
		Scan( 347, "money", null, LowBalanceScore, "the balance is not below 0 but below twice the monthly staff wages, and 348 does not hold" ),
		Scan( 348, "money", null, LowBalanceLoanScore, "the balance is not below 0 but below the monthly staff wages, and a loan can be taken" ),
		Scan( 349, "stranded", "RideExitBlocked.Score", 0, "guests are aboard a broken-down ride (the new riders-stuck tracking)", ModerateRepeatGroups ),
		Scan( 350, "stranded", "RideCondemned.Score", 0, "guests are aboard a condemned ride (the new riders-stuck tracking)", ModerateRepeatGroups ),
		// ---- the rows nothing posts and the scan skips (the poll) ----
		Poll( 83, "toilets", "FeaturesNoToilets.Score", 0, "the escalation of the scan\'s 82 (no toilet, the needy share above MorePeopleThanThisNeeding percent): said only after 82 has been, when still no toilet is built and more than twice MorePeopleThanThisNeeding guests have a toilet need above NeedingMoreThan", LongRepeatGroups, 82 ),
		Poll( 84, "toilets", "FeaturesFarToToilets.Score", 0, $"toilets exist but more than FeaturesNoToilets.MorePeopleThanThisNeeding needy guests are {FarToiletCells} cells or more from the nearest one" ),
		Poll( 277, "tickets", null, ProfitHintScore, $"the year's profit is {ProfitHintPercent} percent or more of the profit ticket's threshold, below the near-ticket multiplier, and the ticket is not won" ),
		Poll( 318, "tickets", "GoldTicketNearToXProfit.Score", 0, "the year's profit is at or above GoldTicketNearToXProfit.ProfitMultiplier x the profit ticket's threshold and the ticket is not won" ),
		// ---- game events ----
		Event( 230, "awards", "AwardGiven.Score", 0, "the park earns the coaster height award (a global ticket nobody held)" ),
		Event( 231, "awards", "AwardGiven.Score", 0, "the park earns the go-kart excitement award" ),
		Event( 232, "awards", "AwardGiven.Score", 0, "the park earns the water ride length award" ),
		Event( 233, "awards", "AwardGiven.Score", 0, "the park earns the big park award" ),
		Event( 130, "upgrades", "UpgradeHireMechanic.Score", 0, "an upgrade is bought and no mechanic is hired", ModerateRepeatGroups ),
		Event( 131, "upgrades", "UpgradeStikingMechanics.Score", 0, "an upgrade is bought while the mechanics are on strike", ModerateRepeatGroups ),
		Event( 132, "upgrades", "NewUpgradeNoMechanic.Score", 0, "an upgrade is researched and no mechanic is hired", ModerateRepeatGroups ),
		Event( 137, "arrivals", "BusArrived.Score", 0, "the bus starts an arrival group", Once ),
		Event( 138, "arrivals", "PlaneArrived.Score", 0, "the seaplane starts an arrival group", Once ),
		Event( 139, "arrivals", "BoatArrived.Score", 0, "the Ferry starts an arrival group", Once ),
		// ---- the tutorial and help messages ----
		Help( 196, "tutorial", "the training budget window opens (screen staffCosts)" ),
		Help( 202, "tutorial", "an object's blueprint is chosen to place" ),
		Help( 204, "tutorial", "the roller coaster tool starts" ),
		Help( 211, "tutorial", "the ride upgrade window opens (screen rideUpgrade)" ),
		Help( 283, "tutorial", "a park starts with the pop-up help option off" ),
		Help( 306, "tutorial", "the camcorder is armed" ),
		Help( 322, "tutorial", "the first message reaches the message tube" ),
		// ---- the online screens (the opt-in online extension's own screens) ----
		Help( 328, "online", "the Publish Park screen opens" ),
		Help( 329, "online", "a park's details open" ),
		Help( 331, "online", "the other parks of a city open" ),
		Help( 332, "online", "the Find Parks screen opens" ),
		Help( 333, "online", "the unsent postcards (outbox) open" ),
		Help( 334, "online", "the online world opens" ),
	};

	private static IReadOnlyDictionary<int, RestoredAdviceEntry>? byMessage;

	/// <summary>The entry of a message, null when the extension does not wire it.</summary>
	public static RestoredAdviceEntry? Find( int messageId ) => (byMessage ??= Entries.ToDictionary( entry => entry.MessageId )).GetValueOrDefault( messageId );

	/// <summary>Whether the message is one the extension wires (a restored help message must not be posted while it is off).</summary>
	public static bool IsRestored( int messageId ) => Find( messageId ) != null;

	/// <summary>
	/// The score a restored message queues with: its callback's key from the settings file, or the extension's own constant. Null when the key is missing, which leaves the message
	/// silent (as every other score key the settings file lacks does).
	/// </summary>
	public static int? ScoreOf( AdvisorBalance balance, int messageId )
	{
		ArgumentNullException.ThrowIfNull( balance );
		if ( Find( messageId ) is not { } entry )
			return null;
		return entry.ScoreKey == null ? entry.ExtScore : balance.Restored.Int( entry.ScoreKey );
	}

	/// <summary>
	/// The extension's own repeat rule for a message (<see cref="RestoredAdviceEntry.RepeatGroups"/>), from the advisor's own history, which a park save already carries (nothing extra is saved, so a save loads the same with the extension
	/// on or off). Once per park: the message has been played (the history's played flag, cleared when a park starts). Else the groups of four turns since it was last said must reach the interval.
	/// </summary>
	public static bool RepeatAllows( AdvisorScoreQueue queue, int messageId, uint liveGameTick )
	{
		if ( Find( messageId ) is not { } entry || entry is { RepeatGroups: 0, AfterMessage: 0 } )
			return true;
		// a follow-up is said only once the original line it follows has been said (and so never repeats it)
		if ( entry.AfterMessage != 0 && !queue.GetHistory( entry.AfterMessage ).HasBeenPlayed )
			return false;
		if ( entry.RepeatGroups == 0 )
			return true;
		var history = queue.GetHistory( messageId );
		if ( entry.RepeatGroups == Once )
			return !history.HasBeenPlayed;
		return history.SavedGameTick >> 2 == 0 || AdvisorScoreQueue.HistoryElapsed( liveGameTick, history.SavedGameTick ) >= (uint)entry.RepeatGroups;
	}

	/// <summary>The restored help messages the interface mode change withdraws (the tool they belong to has ended): the blueprint and roller coaster hints.</summary>
	public static readonly IReadOnlyList<int> ModeHelp = new[] { RotateBlueprintHelp, TrackRideHelp };

	/// <summary>The award message of a global ticket (230 coaster height, 231 go-kart excitement, 232 water length, 233 big park); none for the others.</summary>
	// [BIN:TPW-EXE:0x0076E300 award descriptors] 230 to 233 return AwardGiven.Score (0x59E8A0), the responses 470 to 473 are the height of the coaster, the excitement of the go-kart track, the length of the water ride and the size of the park
	public static int? AwardMessage( GoldenTicketKind kind ) => kind is >= GoldenTicketKind.CoasterHeight and <= GoldenTicketKind.BigPark ? 230 + (kind - GoldenTicketKind.CoasterHeight) : null;
}

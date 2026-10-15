using System;
using System.Collections.Generic;

namespace OpenTPW.Evidence.Scenarios;

/// <summary>
/// Player mode, progression and ticket-eligibility rules recovered from the identified Feral
/// PowerPC application. This is an evidence contract, not OpenTPW's game flow: nothing in the
/// production projects calls it, and it does not define phase rules or a game-flow mode.
/// </summary>
/// <remarks>
/// SimThemePark.data SHA256 04809cd4ccee5433c7fb0b7c93d32f6a7aa629c1849181c0b7906415e5e295f5.
/// Addresses and interpretation: docs/reverse/PPC-scenarios.md, ../scenario_evidence.py,
/// ../followup_evidence.py and ../progression_evidence.py. All counters, statistics and balance
/// values are supplied by the caller; no original table or asset value is embedded. Mac facts
/// only: PC TP.EXE and Patch 2 equivalence is not established.
/// </remarks>
public static class OriginalProgressionRules
{
	/// <summary>Ticket checks run when the unsigned tick counter is a multiple of this (code 0xd6838..0xd6850).</summary>
	public const uint TicketCheckIntervalTicks = 100;

	/// <summary>Highest research group a category can open (unsigned test at 0xf1688).</summary>
	public const byte MaximumResearchGroup = 7;

	/// <summary>Thirty-day months in the red before the bankruptcy event (0xcc454).</summary>
	public const int BankruptcyMonthsInRed = 6;

	/// <summary>Earned tickets: player-wide globals and secrets once, locals per theme whose settings load (0x128b60).</summary>
	public static int EarnedTickets( OriginalPlayerTickets player )
	{
		var earned = Count( player.Global );
		foreach ( var theme in player.Themes )
		{
			if ( theme.SettingsLoaded )
				earned = unchecked(earned + Count( theme.Local ));
		}
		return unchecked(earned + Count( player.Secret ));
	}

	/// <summary>Keys = mExtraKeys + earned / 3, truncating, with no starting constant (0x128c6c..0x128c88).</summary>
	public static int Keys( OriginalPlayerTickets player ) => unchecked(player.ExtraKeys + EarnedTickets( player ) / 3);

	/// <summary>Available tickets = earned - mSpentTickets; spending never changes keys (0x128a2c).</summary>
	public static int AvailableTickets( OriginalPlayerTickets player ) => unchecked(EarnedTickets( player ) - player.SpentTickets);

	/// <summary>
	/// Award code after a ticket flag has been set (0x129cdc). Theme costs are compared in list order
	/// with signed equality; the caller supplies CostToEnter, including -1 for an unloaded theme.
	/// </summary>
	public static OriginalTicketAward AwardAfterEarning( OriginalPlayerTickets player )
	{
		var keys = Keys( player );
		if ( keys <= 0 || EarnedTickets( player ) % 3 != 0 )
			return OriginalTicketAward.Ticket;
		foreach ( var theme in player.Themes )
		{
			if ( theme.CostToEnter == keys )
				return OriginalTicketAward.TicketKeyAndNewTheme;
		}
		return OriginalTicketAward.TicketAndKey;
	}

	/// <summary>Lobby entry (0x964c4): Instant Action skips the key test; otherwise a signed cost &gt; keys refuses. Keys are never consumed.</summary>
	public static bool CanEnterTheme( OriginalGameType gameType, int costToEnter, int keys ) =>
		gameType == OriginalGameType.InstantAction || costToEnter <= keys;

	/// <summary>Golden-ticket checks: Full Simulation only, on unsigned mGameTick multiples of 100 (0xd67f0, 0xd2f1c).</summary>
	public static bool TicketChecksRun( OriginalGameType gameType, uint gameTick ) =>
		gameType == OriginalGameType.FullSimulation && gameTick % TicketCheckIntervalTicks == 0;

	/// <summary>
	/// Local ticket predicate (0xd3230..0xd34a8). All integer tests are signed and strictly greater.
	/// The happiness ticket's second test uses the same in-park guest count as ticket 1, not a count
	/// of happy guests, despite the AtLeastThisManyHappyPeople key name.
	/// </summary>
	public static bool LocalTicketWon( int index, in OriginalLocalTicketStats stats, in OriginalLocalTicketThresholds thresholds ) => index switch
	{
		0 => stats.CumulativeVisitors > thresholds.Visitors,
		1 => stats.GuestsInPark > thresholds.PeopleInPark,
		2 => stats.MeanGuestHappiness > (float)thresholds.Happiness && stats.GuestsInPark > thresholds.AtLeastThisManyHappyPeople,
		3 => stats.AllResearchedAndBuilt,
		4 => stats.ProfitYearToDate > thresholds.ProfitYear,
		5 => stats.VisitorsInRecentMonths > thresholds.RecentVisitors && stats.ParkAgeMonths > thresholds.RecentVisitorMonths,
		_ => throw new ArgumentOutOfRangeException( nameof( index ), "Local tickets are 0..5." ),
	};

	/// <summary>Global ticket predicate (0xd34e8..0xd3594): signed, strictly greater. MinCellsOwned is never read.</summary>
	public static bool GlobalTicketWon( int index, in OriginalGlobalTicketStats stats, in OriginalGlobalTicketThresholds thresholds ) => index switch
	{
		0 => stats.CoasterHeight > thresholds.CoasterHeight,
		1 => stats.GokartExcitement > thresholds.GokartExcitement,
		2 => stats.WaterLength > thresholds.WaterLength,
		3 => stats.BigParkCells > thresholds.MinCellsCovered,
		_ => throw new ArgumentOutOfRangeException( nameof( index ), "Global tickets are 0..3." ),
	};

	/// <summary>Secret ticket 0: camera coverage exactly 100 percent (0xd362c..0xd3640).</summary>
	public static bool CameraSecretWon( int coveragePercent ) => coveragePercent == 100;

	/// <summary>Secret ticket 1 ("own all the possible land") has no award path in the Mac binary; only a loaded save can set it.</summary>
	public const bool OwnAllLandSecretAwardable = false;

	/// <summary>Cells whose class-1 occupants count as guests in the park and in the mean happiness (0xe6c6c).</summary>
	public static bool CountsForGuestStatistics( int cellType ) => cellType is 0 or 1 or 3 or 9 or 10;

	/// <summary>Cells that count toward the big-park global ticket.</summary>
	public static bool CountsForBigPark( int cellType, bool objectOccupied ) => objectOccupied || cellType is 4 or 9 or 10 or 21;

	/// <summary>Cells that the camera secret requires to be covered.</summary>
	public static bool RequiresCameraCoverage( int cellType ) => cellType is not (2 or 7 or 30);

	/// <summary>
	/// Local ticket 3 (0xc5510): no unresearched lab item in any category, and every catalog item
	/// whose kind is not 4 and whose ticket cost is 0 has a non-zero park record +24.
	/// </summary>
	public static bool AllResearchedAndBuilt( int unresearchedItems, IEnumerable<OriginalCatalogItemState> items )
	{
		if ( unresearchedItems > 0 )
			return false;
		foreach ( var item in items )
		{
			if ( item.Kind != 4 && item.ParkRecord24 == 0 && item.TicketCost == 0 )
				return false;
		}
		return true;
	}

	/// <summary>Challenges (0xcfef4): Full Simulation only; once on they stay on; first switch-on needs an open park and age in days &gt;= the threshold.</summary>
	public static bool ChallengesActive( OriginalGameType gameType, bool alreadyOn, bool parkOpen, int parkAgeDays, int daysUntilFirstChallenge ) =>
		gameType == OriginalGameType.FullSimulation && (alreadyOn || (parkOpen && parkAgeDays >= daysUntilFirstChallenge));

	/// <summary>
	/// Research group opening (0xf15b4) for one category. <paramref name="cumulativeCounts"/> returns
	/// researched and total items of the category with group &lt;= g. The threshold for leaving group g is
	/// ResearchTech[g + 1].PercentageForThisTech (balance 1280 + 4g); percentage and compare are unsigned;
	/// a zero total gives 0 percent.
	/// </summary>
	public static byte OpenResearchGroups( byte currentGroup, Func<byte, (uint Researched, uint Total)> cumulativeCounts, ReadOnlySpan<uint> percentageForThisTech )
	{
		if ( percentageForThisTech.Length < MaximumResearchGroup + 2 )
			throw new ArgumentException( "Thresholds through ResearchTech[8] are required.", nameof( percentageForThisTech ) );
		var group = currentGroup;
		while ( true )
		{
			var (researched, total) = cumulativeCounts( group );
			var percent = total == 0 ? 0u : unchecked(researched * 100) / total;
			if ( group + 1 >= percentageForThisTech.Length || percent < percentageForThisTech[group + 1] || group >= MaximumResearchGroup )
				return group;
			group++;
		}
	}

	/// <summary>Monthly wage (0xf46bc): low 32 bits of PayMultiplier[type] * BaseWage[grade]. Dismissal debits one more wage.</summary>
	public static int MonthlyWage( int payMultiplier, int baseWage ) => unchecked(payMultiplier * baseWage);

	/// <summary>Wage type index from the thing class byte +2 (0xf46d8): 5,4,6,7,8 map to handyman..researcher; anything else is generic.</summary>
	public static OriginalStaffType StaffTypeForThingClass( byte thingClass ) => thingClass switch
	{
		5 => OriginalStaffType.Handyman,
		4 => OriginalStaffType.Mechanic,
		6 => OriginalStaffType.Entertainer,
		7 => OriginalStaffType.Guard,
		8 => OriginalStaffType.Researcher,
		_ => OriginalStaffType.Generic,
	};

	/// <summary>Month-end bankruptcy event (0xcc3d0..0xcc464): signed money below zero for at least six thirty-day months.</summary>
	public static bool BankruptcyEventDue( int money, int thirtyDayMonthsInRed ) => money < 0 && thirtyDayMonthsInRed >= BankruptcyMonthsInRed;

	/// <summary>Bankruptcy handler (0x1059e0): world state becomes 4 unless it already is 4 or the unresolved id gate (0x105c6c) refuses.</summary>
	public static OriginalWorldState AfterBankruptcyEvent( OriginalWorldState current, bool idGateMatches ) =>
		current.Raw == OriginalWorldState.Bankrupt || !idGateMatches ? current : new OriginalWorldState( OriginalWorldState.Bankrupt );

	/// <summary>
	/// Object placement charge (0xda874). A ticket-cost item not yet uncovered is bought with tickets
	/// (no money); once uncovered every further copy costs its money price. Online uncovering succeeds
	/// without recording the item, so such placements stay free.
	/// </summary>
	public static OriginalPlacementCharge PlacementCharge( OriginalGameType gameType, int ticketCost, bool alreadyUncovered )
	{
		if ( ticketCost <= 0 || alreadyUncovered )
			return OriginalPlacementCharge.MoneyPrice;
		return gameType == OriginalGameType.Online ? OriginalPlacementCharge.FreeOnline : OriginalPlacementCharge.TicketPurchase;
	}

	/// <summary>
	/// Mystery uncovering (0xd3000): online succeeds without recording; otherwise a signed cost above the
	/// available tickets refuses, and a new id adds its cost to mSpentTickets once. The placement routine
	/// ignores this result, so affordability must be enforced before placement (caller not traced).
	/// </summary>
	public static bool TryUncoverMysteryItem( OriginalPlayerTickets player, OriginalGameType gameType, ushort itemId, int ticketCost )
	{
		if ( gameType == OriginalGameType.Online )
			return true;
		if ( ticketCost > AvailableTickets( player ) )
			return false;
		if ( !player.UncoveredMysteryItems.Add( itemId ) )
			return false;
		player.SpentTickets = unchecked(player.SpentTickets + ticketCost);
		return true;
	}

	/// <summary>Feature gates bound to the GameType tests listed in docs/reverse/PPC-scenarios.md.</summary>
	public static OriginalAvailability Availability( OriginalGameType gameType, OriginalFeature feature )
	{
		var full = gameType == OriginalGameType.FullSimulation;
		var instant = gameType == OriginalGameType.InstantAction;
		return feature switch
		{
			OriginalFeature.GoldenTickets or OriginalFeature.Challenges => full ? OriginalAvailability.Available : OriginalAvailability.Unavailable,
			OriginalFeature.MysteryItemsCostTickets => gameType == OriginalGameType.Online ? OriginalAvailability.Unavailable : OriginalAvailability.Available,
			OriginalFeature.ThemeKeyCheck or OriginalFeature.ResearchPanel or OriginalFeature.BankPanel
				or OriginalFeature.LoanButtons or OriginalFeature.RideUpgradeList =>
				instant ? OriginalAvailability.Unavailable : full ? OriginalAvailability.Available : OriginalAvailability.NotTraced,
			_ => throw new ArgumentOutOfRangeException( nameof( feature ) ),
		};
	}

	private static int Count( bool[] flags )
	{
		var count = 0;
		foreach ( var flag in flags )
			count += flag ? 1 : 0;
		return count;
	}
}

/// <summary>Runtime GameType (data 0x53d98). Not serialized: player load derives 0 or 2 from mEasyModeUser.</summary>
public enum OriginalGameType
{
	FullSimulation = 0,
	Online = 1,
	InstantAction = 2,
}

/// <summary>Front-end exit code (*(data 0x84b80) + 20); a separate variable from GameType.</summary>
public enum OriginalFrontEndExitCode
{
	Running = 1,
	EnterPark = 2,
	Quit = 3,
}

/// <summary>Raw world-state word (world + 0x1E0000 - 22728). Only value 4, the bankruptcy target, is bound.</summary>
public readonly record struct OriginalWorldState( int Raw )
{
	public const int Bankrupt = 4;
}

public enum OriginalTicketAward
{
	Ticket = 1,
	TicketAndKey = 2,
	TicketKeyAndNewTheme = 3,
	GlobalTicketMovedHere = 4,
}

public enum OriginalPlacementCharge
{
	MoneyPrice,
	TicketPurchase,
	FreeOnline,
}

public enum OriginalStaffType
{
	Handyman = 0,
	Mechanic = 1,
	Entertainer = 2,
	Guard = 3,
	Researcher = 4,
	Generic = 5,
}

public enum OriginalAvailability
{
	Available,
	Unavailable,
	NotTraced,
}

public enum OriginalFeature
{
	GoldenTickets,
	Challenges,
	ThemeKeyCheck,
	ResearchPanel,
	BankPanel,
	LoanButtons,
	RideUpgradeList,
	MysteryItemsCostTickets,
}

/// <summary>Caller-held player progression record (the Mac 76-byte singleton's ticket fields).</summary>
public sealed class OriginalPlayerTickets
{
	public bool[] Global { get; } = new bool[4];
	public bool[] Secret { get; } = new bool[2];
	public List<OriginalThemeTickets> Themes { get; } = new();
	public int SpentTickets { get; set; }
	public int ExtraKeys { get; set; }
	public bool EasyModeUser { get; set; }
	public HashSet<ushort> UncoveredMysteryItems { get; } = new();

	/// <summary>Player load (0x13781c): GameType from the persisted easy flag unless online is already active.</summary>
	public OriginalGameType GameTypeOnLoad( OriginalGameType current ) =>
		current == OriginalGameType.Online ? current : EasyModeUser ? OriginalGameType.InstantAction : OriginalGameType.FullSimulation;
}

/// <summary>Per-theme record: six local tickets; CostToEnter as returned by the theme's settings (-1 when unloaded).</summary>
public sealed class OriginalThemeTickets
{
	public OriginalThemeTickets( string name, int costToEnter, bool settingsLoaded = true )
	{
		Name = name;
		CostToEnter = costToEnter;
		SettingsLoaded = settingsLoaded;
	}

	public string Name { get; }
	public int CostToEnter { get; set; }
	public bool SettingsLoaded { get; set; }
	public bool[] Local { get; } = new bool[6];
}

public readonly record struct OriginalLocalTicketStats(
	int CumulativeVisitors, int GuestsInPark, float MeanGuestHappiness, bool AllResearchedAndBuilt,
	int ProfitYearToDate, int VisitorsInRecentMonths, int ParkAgeMonths );

/// <summary>GoldenTicketLocal.* values (balance 1872..1896), supplied by the caller.</summary>
public readonly record struct OriginalLocalTicketThresholds(
	int Visitors, int PeopleInPark, int Happiness, int AtLeastThisManyHappyPeople,
	int ProfitYear, int RecentVisitors, int RecentVisitorMonths );

public readonly record struct OriginalGlobalTicketStats( int CoasterHeight, int GokartExcitement, int WaterLength, int BigParkCells );

/// <summary>GoldenTicketGlobal.* values read by the checks (balance 1900..1908 and 1916), supplied by the caller.</summary>
public readonly record struct OriginalGlobalTicketThresholds( int CoasterHeight, int GokartExcitement, int WaterLength, int MinCellsCovered );

public readonly record struct OriginalCatalogItemState( int Kind, int TicketCost, int ParkRecord24 );

/// <summary>Lobby response ids queued by the new-player award (0x15cd38).</summary>
public enum OriginalLobbyResponse
{
	None = 0,
	FirstEntryFullSimulation = 393,
	FirstEntryInstantAction = 394,
}

/// <summary>
/// Front-end player screen and the new-player extra key (0x15c828, 0x15cd38, 0x15d074). Every award
/// call first closes the player window; message 5 and slot selection come only from that window, which
/// only front-end init recreates, recomputing the flag (0 once any slot is named).
/// </summary>
public sealed class OriginalFrontEndSession
{
	public bool NewPlayerFlag { get; private set; }
	public bool PlayerWindowOpen { get; private set; }

	/// <summary>Front-end init: the flag is 1 only when no slot has a name; the player window is created.</summary>
	public void Init( bool anySlotNamed )
	{
		NewPlayerFlag = !anySlotNamed;
		PlayerWindowOpen = true;
	}

	/// <summary>Player creation dialog confirmed: flag = 1, then the award with argument 0.</summary>
	public OriginalLobbyResponse CreatePlayer( OriginalPlayerTickets player, OriginalGameType gameType )
	{
		RequireWindow();
		NewPlayerFlag = true;
		return Award( 0, playerLoaded: true, gameType, player );
	}

	/// <summary>Continue (message 5) or slot selection on a named slot.</summary>
	public OriginalLobbyResponse Continue( OriginalPlayerTickets player, bool playerLoaded, OriginalGameType gameType )
	{
		RequireWindow();
		return Award( 0, playerLoaded, gameType, player );
	}

	/// <summary>Quit handler: argument 1 closes the window without an award.</summary>
	public void Quit()
	{
		RequireWindow();
		PlayerWindowOpen = false;
	}

	private OriginalLobbyResponse Award( int argument, bool playerLoaded, OriginalGameType gameType, OriginalPlayerTickets player )
	{
		PlayerWindowOpen = false;
		if ( argument != 0 || !playerLoaded || !NewPlayerFlag )
			return OriginalLobbyResponse.None;
		if ( gameType == OriginalGameType.InstantAction )
			return OriginalLobbyResponse.FirstEntryInstantAction;
		player.ExtraKeys = unchecked(player.ExtraKeys + 1);
		return OriginalLobbyResponse.FirstEntryFullSimulation;
	}

	private void RequireWindow()
	{
		if ( !PlayerWindowOpen )
			throw new InvalidOperationException( "The original player window is closed; this event cannot occur before front-end init." );
	}
}

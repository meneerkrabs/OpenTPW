using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace OpenTPW.Tests;

/// <summary>Synthetic balance settings and objects shaped like the original files (no original data).</summary>
internal static class EconomyTestData
{
	private static readonly string[] Plurals = { "Handymen", "Mechanics", "Entertainers", "Guards", "Researchers" };
	private static readonly string[] Singulars = { "Handyman", "Mechanic", "Entertainer", "Guard", "Researcher" };

	public static string StandardText( int initialCash = 50000, int apr = 20, string extra = "" )
	{
		var text = new StringBuilder();
		text.Append( "#Theme Park 2 Standard Balance file\n" );
		text.Append( $"BankAccountInfo.InitialCash\t\t{initialCash}\nBankAccountInfo.InitialAdmissionFee\t20\n" );
		var loans = new[] { (100000, 36), (50000, 36), (18000, 24) };
		for ( var index = 0; index < loans.Length; index++ )
			text.Append( $"LoanInfo[{index}].LoanAmount\t{loans[index].Item1}\nLoanInfo[{index}].APRInPercent\t{apr}\nLoanInfo[{index}].RepaymentPeriodInMonths\t{loans[index].Item2}\nLoanInfo[{index}].Lendername\t{index}\n" );
		var wages = new[] { 4, 5, 6, 8, 12 };
		var ability = new[] { 2, 3, 4, 5, 6 };
		var tech = new[] { 0, 0, 80, 85, 85 };
		for ( var grade = 0; grade < 5; grade++ )
		{
			text.Append( $"PerGradeStaffConsts[{grade}].BaseWage\t{wages[grade]}\t\t\t// Grade {grade}\n" );
			text.Append( $"ResearcherConstsPerGrade[{grade}].ResearchAbility\t{ability[grade]}\n" );
			text.Append( $"ResearchTech[{grade}].PercentageForThisTech\t{tech[grade]}\n" );
		}
		var effort = new[] { 100, 15, 30, 15, 10 };
		for ( var category = 0; category < 5; category++ )
			text.Append( $"ResearchCategories[{category}].Effort\t{effort[category]}\t\t#comment\n" );
		text.Append( "Research.StartingWorkLoad 85\nStaffPoolInfo.BaseCostPerStaff 2000\nStaffPoolInfo.CostPerQualityLevel 100\n" );
		text.Append( "StaffPoolInfo.TimeBetweenStaffUpdates 90\nStaffPoolInfo.MaxNumberOfStaffPerUpdate 10\nStaffPoolInfo.StaffTimeoutTime 120\n" );
		var pay = new[] { 10, 30, 15, 20, 35 };
		var durations = new[] { new[] { 40, 30, 20, 10, 5 }, new[] { 80, 60, 40, 30, 20 }, new[] { 10, 20, 30, 50, 75 }, new[] { 10, 20, 30, 50, 75 }, new[] { 10, 20, 30, 40, 50 } };
		var training = new[] { 5, 8, 12, 15, 0 };
		for ( var type = 0; type < 5; type++ )
		{
			var plural = Plurals[type];
			text.Append( $"StaffPoolInfo.ChanceToGetGreat{plural} 10\nStaffPoolInfo.AvgGradeOf{plural} 1\nStaffPoolInfo.BeginningNumberOf{plural} 3\n" );
			text.Append( $"StaffPoolInfo.Max{plural} 5\nStaffPoolInfo.Min{plural}InPool 1\nStaffPoolInfo.Max{plural}InPark 10\n" );
			text.Append( $"PerTypeStaffConsts[{type}].PayMultiplier\t{pay[type]}\t\t// {Singulars[type]}\n" );
			for ( var grade = 0; grade < 5; grade++ )
				text.Append( $"{Singulars[type]}ConstsPerGrade[{grade}].WorkDuration {durations[type][grade]}\n{Singulars[type]}ConstsPerGrade[{grade}].PoundsPerTrainingPoint {training[grade]}\n" );
		}
		text.Append( "Costs.QueueCell 75\nCosts.PathCell 20\nCosts.KartTrackCell 400\nCosts.WaterTrackCell 500\nCosts.MapCell 100\n" );
		text.Append( "PeepInfo.ExcitementToCostDivisor 4 Scaling factor\nPeepInfo.MinimumEntryFee 20\nPeepInfo.CheapPriceMultiplier 0.75\nPeepInfo.AveragePriceMultiplier 1.25\nPeepInfo.ExpensivePriceMultiplier 2.0\n" );
		text.Append( "GoldenTicketLocal.Visitors 100\nGoldenTicketLocal.PeopleInPark 200\nGoldenTicketLocal.Happiness 75\nGoldenTicketLocal.AtLeastThisManyHappyPeople 150\n" );
		text.Append( "GoldenTicketLocal.ProfitYear 15000\nGoldenTicketLocal.RecentVisitors 350\nGoldenTicketLocal.RecentVisitorMonths 6\n" );
		text.Append( "GoldenTicketGlobal.CoasterHeight 105\nGoldenTicketGlobal.GokartExcitement 90\nGoldenTicketGlobal.WaterLength 50\nGoldenTicketGlobal.MinCellsOwned 3000\nGoldenTicketGlobal.MinCellsCovered 2000\n" );
		text.Append( "Challenges.DaysAfterCompletedChallenge 270\nChallenges.DaysAfterDeclinedChallenge 270\nChallenges.DaysUntilFirstChallenge 540\nChallenges.DeclinesToForfeit 2\nChallenges.ShortTimeLeftWarningAt 20\n" );
		text.Append( "ChallengesInThisLevel[0].ChallengeType 1\nChallengesInThisLevel[1].ChallengeType 7\nChallengesInThisLevel[2].ChallengeType 8\n" );
		text.Append( extra );
		return text.ToString();
	}

	public const string ChallengesText =
		"# Sell 30 Drinks in 60 days\nChallenges[1].Type 3\nChallenges[1].FollowupType 0\nChallenges[1].TargetTime 60\nChallenges[1].TargetVal 30\nChallenges[1].TargetObj 0\nChallenges[1].TargetObj2 0\nChallenges[1].TargetStaffType 0\nChallenges[1].Prize 5000\nChallenges[1].CheckAtEndOnly 0\nChallenges[1].Independent 1\n"
		+ "Challenges[7].Type 18\nChallenges[7].FollowupType 20\nChallenges[7].TargetTime 60\nChallenges[7].TargetVal 1\nChallenges[7].TargetObj 1180\nChallenges[7].TargetObj2 0\nChallenges[7].TargetStaffType 0\nChallenges[7].Prize 7500\nChallenges[7].CheckAtEndOnly 0\nChallenges[7].Independent 1\n"
		+ "Challenges[8].Type 20\nChallenges[8].FollowupType 0\nChallenges[8].TargetTime 180\nChallenges[8].TargetVal 2\nChallenges[8].TargetObj 1180\nChallenges[8].TargetObj2 0\nChallenges[8].TargetStaffType 0\nChallenges[8].Prize 30000\nChallenges[8].CheckAtEndOnly 0\nChallenges[8].Independent 0\n";

	public static BalanceSettings Settings( int initialCash = 50000, int apr = 20, string extra = "" ) => new(
		new SamSettings( new[] { SamDocument.Parse( StandardText( initialCash, apr, extra ), "Standard.sam" ) } ),
		new SamSettings( new[] { SamDocument.Parse( ChallengesText, "Challenges.sam" ) } ),
		new SamSettings( new[] { SamDocument.Parse( "Tickets.CanEarnTicketsInTheme 1\nTickets.CanSpendTicketsInTheme 1\nKeys.CostToEnter 1\n", "global.sam" ) } ),
		"jungle", false );

	private static IReadOnlyList<int> Scrap => new[] { 50, 30, 20, 10 };

	private static UpgradeLevelInfo Level( int level, long cost, int research, int wear = 0, int duration = 0 ) => new( level, cost, research, wear, duration, Scrap );

	public static EconomyObjectCatalog Catalog() => new( new[]
	{
		new EconomyObjectInfo( 1100, "Belly Bounce", ParkObjectKind.Ride, ResearchCategory.Ride, 0, new[] { Level( 0, 500, 0, 5 ), Level( 1, 400, 250, 3, 2 ), Level( 2, 500, 300, 2, 4 ) }, null, null, null, 0, 0, 0, 0, 0, 25, "rides/bouncy.wad" ),
		new EconomyObjectInfo( 1180, "Temple Of Gloom", ParkObjectKind.Ride, ResearchCategory.Ride, 1, new[] { Level( 0, 10000, 800, 1 ) }, null, null, null, 0, 0, 0, 0, 0, 25, "rides/minecart.wad" ),
		new EconomyObjectInfo( 1106, "Sun God", ParkObjectKind.Ride, ResearchCategory.Ride, 2, new[] { Level( 0, 1500, 550, 5 ) }, null, null, null, 0, 0, 0, 0, 0, 20, "rides/incagod.wad" ),
		new EconomyObjectInfo( 1203, "Drinks Shop", ParkObjectKind.Shop, ResearchCategory.Shop, 0, new[] { Level( 0, 650, 0 ) }, 30, 20, null, 4, 3, 0, 0, 50, 0, "shops/coconut.wad" ),
		new EconomyObjectInfo( 1303, "Jungle Spray", ParkObjectKind.Sideshow, ResearchCategory.Sideshow, 0, new[] { Level( 0, 1750, 0 ) }, 20, 50, 70, 0, 0, 0, 0, 0, 0, "sideshow/junspray.wad" ),
		new EconomyObjectInfo( 1112, "Eruption", ParkObjectKind.Ride, ResearchCategory.Ride, 0, new[] { Level( 0, 2500, 0, 5 ) }, null, null, null, 0, 0, 3, 0, 0, 25, "rides/volcano.wad" ),
		new EconomyObjectInfo( 1402, "Small Toilet", ParkObjectKind.Feature, ResearchCategory.Feature, 0, new[] { Level( 0, 100, 0 ) }, null, null, null, 0, 0, 0, 0, 0, 10, "features/toilet.wad" ),
		new EconomyObjectInfo( 1601, "Gates", ParkObjectKind.FixedItem, ResearchCategory.Ride, 0, new[] { Level( 0, 0, 0 ) }, null, null, null, 0, 0, 0, 0, 0, 0, "features/gates.wad" ),
		new EconomyObjectInfo( 1500, "Dino Karts Jump", ParkObjectKind.Upgrade, ResearchCategory.Upgrade, 0, new[] { Level( 0, 750, 1000 ) }, null, null, null, 0, 0, 0, 1180, 0, 0, "upgrades/lavajump.wad" )
	} );

	public static ParkEconomy Park( int initialCash = 50000, int apr = 20, ulong seed = 7, ParkGameMode mode = ParkGameMode.FullSimulation ) =>
		new( Settings( initialCash, apr ), Catalog(), mode, seed );

	public static StaffMember HireBest( ParkEconomy park, StaffType type ) =>
		park.Hire( park.Staff.Candidates.Where( candidate => candidate.Type == type ).OrderByDescending( candidate => candidate.Grade ).ThenBy( candidate => candidate.Id ).First().Id );
}

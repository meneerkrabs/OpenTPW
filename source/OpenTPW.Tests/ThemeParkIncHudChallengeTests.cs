using System;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTPW.UI.Original;

namespace OpenTPW.Tests;

/// <summary>The experimental Theme Park Inc HUD's challenge panel: the slots listed by state with their titles, and the accept enabled only in state 1 with the park open.</summary>
[TestClass]
public class ThemeParkIncHudChallengeTests
{
	private const string Standard =
		"BankAccountInfo.InitialCash\t50000\nBankAccountInfo.InitialAdmissionFee\t25\n"
		+ "LoanInfo[0].LoanAmount\t10000\nLoanInfo[0].APRInPercent\t20\nLoanInfo[0].RepaymentPeriodInMonths\t36\nLoanInfo[0].Lendername\t0\n"
		+ "Costs.QueueCell 25\nCosts.PathCell 10\nCosts.KartTrackCell 40\nCosts.WaterTrackCell 50\nCosts.MapCell 75\n"
		+ "PeepInfo.ExcitementToCostDivisor 6\nPeepInfo.MinimumEntryFee 20\nPeepInfo.CheapPriceMultiplier 0.75\nPeepInfo.AveragePriceMultiplier 1.25\nPeepInfo.ExpensivePriceMultiplier 2.0\n"
		+ "Challenges.DaysBetweenOffers 20\nChallengesInThisLevel[0].ChallengeType 2\nChallengesInThisLevel[1].ChallengeType 18\n";

	private const string ChallengesSam =
		"Challenges[2].Type 2\nChallenges[2].TargetTime 60\nChallenges[2].TargetVal 30\nChallenges[2].ScriptIndex 2\n"
		+ "Challenges[18].Type 41\nChallenges[18].TargetTime 90\nChallenges[18].TriggerData 3\nChallenges[18].CheckAtEndOnly 1\nChallenges[18].GracePeriodLimit 31\nChallenges[18].ScriptIndex 18\n";

	private static ParkEconomy Economy()
	{
		var settings = BalanceSettings.ForThemeParkInc( new SamSettings( new[] { SamDocument.Parse( Standard, "Standard.sam" ) } ), "science",
			new SamSettings( new[] { SamDocument.Parse( ChallengesSam, "Challenges.sam" ) } ) );
		return new ParkEconomy( settings, new EconomyObjectCatalog( Array.Empty<EconomyObjectInfo>() ), ParkGameMode.FullSimulation, 7, EconomyRules.ThemeParkIncMoney );
	}

	private static UiStringTable Strings() => new( "English", new string[1], new[] { "" }, new string[1], new string[1],
		challengeTitles: Enumerable.Range( 0, 30 ).Select( index => $"Title{index}" ).ToArray() );

	private static ThemeParkIncHud BuildHud( ParkEconomy economy )
	{
		var catalog = (ObjectCatalog)Activator.CreateInstance( typeof( ObjectCatalog ), BindingFlags.NonPublic | BindingFlags.Instance, null,
			new object[] { "science", Array.Empty<ObjectCatalogEntry>() }, null )!;
		var level = (Level)RuntimeHelpers.GetUninitializedObject( typeof( Level ) );
		Set( level, "Objects", new ParkObjects( catalog, new SandboxParkGrid(), new WorldSeed( 1 ) ) );
		Set( level, "CellTool", new CellBuildTool() );
		var runtime = (ParkEconomyRuntime)RuntimeHelpers.GetUninitializedObject( typeof( ParkEconomyRuntime ) );
		Set( runtime, "Economy", economy );
		Set( level, "Park", runtime );
		return new ThemeParkIncHud( level, Strings(), new ThemeParkIncHudHost() );
	}

	private static void Set( object target, string property, object value ) =>
		target.GetType().GetField( "<" + property + ">k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic )!.SetValue( target, value );

	private GameEdition previousEdition;

	[TestInitialize]
	public void Initialize() { previousEdition = GameEditions.Current; GameEditions.Select( GameEdition.ThemeParkInc ); }

	[TestCleanup]
	public void Cleanup() => GameEditions.Select( previousEdition );

	[TestMethod]
	public void ThePanelListsTheOfferedChallengeByItsTitleAndAcceptsItOnlyWithTheParkOpen()
	{
		var economy = Economy();
		var hud = BuildHud( economy );
		CollectionAssert.AreEqual( new[] { SupplementaryStrings.Get( OpenTpwText.TpiChallengesNone, "English" ) }, hud.ChallengeRows().ToArray(), "idle slots are not listed" );
		Assert.AreEqual( 1, economy.DebugOfferThemeParkIncChallenge( 1 ) );
		var rows = hud.ChallengeRows();
		Assert.AreEqual( 1, rows.Count );
		StringAssert.StartsWith( rows[0], "Title18 - on offer", "the row names the slot by CHALLENGE_TITLES.str at its ScriptIndex" );
		Assert.IsFalse( economy.IsParkOpen );
		hud.ActivateChallengeRow( 0 );
		Assert.AreEqual( ThemeParkIncChallengeStatus.Offered, economy.TpiChallenges!.Slots[1].Status, "a closed park keeps the accept disabled" );
		Assert.AreEqual( SupplementaryStrings.Get( OpenTpwText.TpiChallengeOpenPark, "English" ), hud.Messages[0] );
		economy.OpenPark();
		hud.ActivateChallengeRow( 0 );
		Assert.AreEqual( ThemeParkIncChallengeStatus.Grace, economy.TpiChallenges.Slots[1].Status, "accepting starts its 31-day grace period" );
		StringAssert.StartsWith( hud.ChallengeRows()[0], "Title18 - accepted, starts in 31 days" );
		hud.ActivateChallengeRow( 0 );
		Assert.AreEqual( ThemeParkIncChallengeStatus.Grace, economy.TpiChallenges.Slots[1].Status, "an accepted row accepts nothing again" );
		Assert.IsFalse( economy.CanAcceptThemeParkIncChallenge( 1 ) );
		Assert.AreEqual( "Challenges you may still fail: 5 - golden tickets: 0", string.Format( SupplementaryStrings.Get( OpenTpwText.TpiChallengesCareer, "English" ),
			economy.TpiChallenges.FiringLine, economy.TpiChallenges.GoldenTickets ) );
	}
}

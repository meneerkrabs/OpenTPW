using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTPW.Mods;
using OpenTPW.UI.Original;

namespace OpenTPW.Tests;

/// <summary>
/// The restored advice extension (<c>opentpw.restored-advice</c>, docs/reverse/ADVISOR-SCAN.md "Restored advice"): the extension's registration (on by default, never a simulation extension), its catalog, every trigger
/// through the real advisor queue in a constructed park state, the admission rules, off meaning silent, the three new trackings and the save and hash safety. GPU-free; the settings are synthetic.
/// </summary>
[TestClass]
[DoNotParallelize]
public class AdvisorRestoredAdviceTests
{
	// Synthetic settings in the Advisor.sam layout (values chosen for the tests, not copied from the original).
	internal const string SamText = """
		GeneralAdvisor.MinTimeAnyMessage	5
		GeneralAdvisor.MinTimeSameMessage	120
		GeneralAdvisor.MinScoreForConsideration	25
		MessageGroups[0].MinTimeSameMessage		10
		MessageGroups[0].SayOnlyOnce			0
		MessageGroups[0].DiscardAfterSlaps		0
		MessageGroups[1].MinTimeSameMessage		0
		MessageGroups[1].SayOnlyOnce			1
		MessageGroups[1].DiscardAfterSlaps		2
		MessageGroups[2].MinTimeSameMessage		0
		MessageGroups[2].SayOnlyOnce			0
		MessageGroups[2].DiscardAfterSlaps		0
		MessageGroups[3].MinTimeSameMessage		30
		MessageGroups[3].SayOnlyOnce			0
		MessageGroups[3].DiscardAfterSlaps		0
		MessageGroups[4].MinTimeSameMessage		20
		MessageGroups[4].SayOnlyOnce			0
		MessageGroups[4].DiscardAfterSlaps		0
		MessageGroups[5].MinTimeSameMessage		15
		MessageGroups[5].SayOnlyOnce			0
		MessageGroups[5].DiscardAfterSlaps		0
		MessageGroups[9].MinTimeSameMessage		0
		MessageGroups[9].SayOnlyOnce			1
		MessageGroups[9].DiscardAfterSlaps		0
		Welcome.Score 9000
		Bankrupted.Score	8000
		ParkNowOpen.Score	20
		ParkNowClosed.Score	20
		PrebuiltPark.Score	700
		HelpMessage.Score	1000
		HelpMessage.ContextHelpScore	1500
		StaffSetGuardArea.MonthsIntoGameForConsideration	6
		StaffSetGuardArea.AtLeastThisMany	2
		StaffGuardCoverage.Score	12
		StaffGuardCoverage.ScorePerNonCoveredPercent	2
		RidesNoUpgrades.MonthsIntoGameForConsideration	3
		RidesNoUpgrades.Score	64
		RideExitBlocked.Score	66
		RideCondemned.Score	77
		FeaturesNoToilets.Score	68
		FeaturesNoToilets.NeedingMoreThan	50
		FeaturesNoToilets.MorePeopleThanThisNeeding	4
		FeaturesFarToToilets.Score	52
		GoldTicketNearToXProfit.ProfitMultiplier	0.8
		GoldTicketNearToXProfit.Score	73
		GeneralWonGoldenTicket.Score	9100
		AwardGiven.Score	480
		Challenge.Score	130
		UpgradeHireMechanic.Score	88
		UpgradeStikingMechanics.Score	89
		NewUpgradeNoMechanic.Score	56
		BusArrived.Score	71
		PlaneArrived.Score	72
		BoatArrived.Score	73
		""";

	private const string Sam = SamText;

	[TestInitialize]
	public void Setup() => Log ??= new();

	private static AdvisorBalance Balance( string text = Sam ) => AdvisorBalance.Parse( Encoding.ASCII.GetBytes( text ), "test.sam" );
	private static AdvisorController Controller( string text = Sam ) => new( Balance( text ) );

	private static ModSet Off() => ModSet.Resolve( Array.Empty<DiscoveredMod>(), new ModSettings { Enabled = new Dictionary<string, bool> { [OfficialExtensions.RestoredAdvice.Id] = false } }, null,
		GameEdition.ThemeParkWorld, OfficialExtensions.All );

	private static IReadOnlyList<int> Pending( AdvisorController controller ) =>
		Enumerable.Range( 0, AdvisorScoreQueue.Capacity ).Select( controller.Queue.GetSlot ).Where( slot => slot != null ).Select( slot => slot!.MessageId ).ToArray();

	private static AdvisorPendingAdvice PendingOf( AdvisorController controller, int message ) =>
		Enumerable.Range( 0, AdvisorScoreQueue.Capacity ).Select( controller.Queue.GetSlot ).Single( slot => slot?.MessageId == message )!;

	internal sealed class MoneyFake : IAdvisorMoneyState
	{
		public int Balance { get; set; }
		public IReadOnlyList<AdvisorLoanSlot> LoanSlots { get; } = new AdvisorLoanSlot[AdvisorLoanSlot.Count];
		public bool LoanAvailable { get; set; }
		public int MonthsInRed => 0;
		public int ParkMonths => 12;
		public IReadOnlyList<int> WageMonths { get; } = Array.Empty<int>();
		public IReadOnlyList<int> IncomeMonths { get; } = Array.Empty<int>();
	}

	/// <summary>A busy park in which every restored condition holds all the time (the worst case for nagging).</summary>
	internal static IRestoredAdviceState TypicalState() => new State
	{
		ParkMonths = 40, HiredGuards = 4, GuardsWithPatrol = 1, SecurityCoverage = new RestoredSecurityCoverage( 200, 150, 0 ),
		Rides = new[] { Ride( queue: 25 ), Ride( queue: 22 ), Ride( queue: 30 ), Ride( queue: 3 ) },
		UpgradesAvailable = 2, Land = new RestoredLand( 400, 20 ), Toilets = 3, MonthlyWages = 100,
	};

	/// <summary>A park of 40 months with no guard and no camera.</summary>
	internal static IRestoredAdviceState NoSecurityState() => new State { ParkMonths = 40, SecurityCoverage = new RestoredSecurityCoverage( 200, 0, 0 ) };

	/// <summary>A park the tests describe fact by fact.</summary>
	internal sealed class State : IRestoredAdviceState
	{
		public int ParkMonths { get; set; } = 12;
		public int HiredGuards { get; set; }
		public int GuardsWithPatrol { get; set; }
		public int HiredMechanics { get; set; }
		public bool MechanicsOnStrike { get; set; }
		public bool UpgradesPossible { get; set; } = true;
		public RestoredSecurityCoverage SecurityCoverage { get; set; }
		public IReadOnlyList<RestoredRide> Rides { get; set; } = Array.Empty<RestoredRide>();
		public int UpgradesAvailable { get; set; }
		public int UpgradesBought { get; set; }
		public int Toilets { get; set; }
		public int NeedingToilet { get; set; }
		public int FarFromToilets { get; set; }
		public int GuestsNeedingToilet( int above ) => NeedingToilet;
		public int GuestsFarFromToilets( int above, int cells ) => FarFromToilets;
		public long RetailSalesTwoMonths { get; set; }
		public long VisitorsTwoMonths { get; set; }
		public RestoredLand Land { get; set; }
		public Dictionary<ParkRecordKind, long> Records { get; } = new();
		public Dictionary<GoldenTicketKind, long> Thresholds { get; } = new();
		public HashSet<GoldenTicketKind> Held { get; } = new();
		public long Record( ParkRecordKind kind ) => Records.GetValueOrDefault( kind );
		public long TicketThreshold( GoldenTicketKind kind ) => Thresholds.GetValueOrDefault( kind );
		public bool HoldsTicket( GoldenTicketKind kind ) => Held.Contains( kind );
		public long ProfitThisYear { get; set; }
		public long MonthlyWages { get; set; }
	}

	private static RestoredRide Ride( bool open = true, int queue = 0, bool broken = false, bool condemned = false, int riders = 0, bool exitBlocked = false ) =>
		new( 1, open, broken, condemned, riders, queue, exitBlocked );

	/// <summary>The park state (and money) that makes each scan-zero row's restored callback hold.</summary>
	private static readonly IReadOnlyDictionary<int, Action<State, MoneyFake>> ScanScenarios = new Dictionary<int, Action<State, MoneyFake>>
	{
		[24] = ( state, _ ) => { state.HiredGuards = 2; state.GuardsWithPatrol = 1; state.SecurityCoverage = new RestoredSecurityCoverage( 100, 60, 0 ); },
		[25] = ( state, _ ) => state.SecurityCoverage = new RestoredSecurityCoverage( 100, 0, 0 ),
		[58] = ( state, _ ) => state.Rides = new[] { Ride( queue: 35 ), Ride( queue: 3 ) },
		[59] = ( state, _ ) => state.Rides = new[] { Ride( queue: 35 ), Ride( queue: 40 ), Ride( queue: 2 ) },
		[65] = ( state, _ ) => { state.UpgradesAvailable = 1; state.UpgradesBought = 0; },
		[151] = ( state, _ ) => { state.Thresholds[GoldenTicketKind.CoasterHeight] = 100; state.Records[ParkRecordKind.CoasterHeight] = 80; },
		[152] = ( state, _ ) => state.Held.Add( GoldenTicketKind.CoasterHeight ),
		[153] = ( state, _ ) => { state.Thresholds[GoldenTicketKind.GokartExcitement] = 100; state.Records[ParkRecordKind.GokartExcitement] = 76; },
		[154] = ( state, _ ) => { state.Thresholds[GoldenTicketKind.WaterLength] = 100; state.Records[ParkRecordKind.WaterLength] = 90; },
		[160] = ( state, _ ) => { state.RetailSalesTwoMonths = 40; state.VisitorsTwoMonths = 60; },
		[173] = ( state, _ ) => state.Land = new RestoredLand( 100, 5 ),
		[265] = ( state, _ ) => state.Rides = new[] { Ride( exitBlocked: true ) },
		[347] = ( state, money ) => { state.MonthlyWages = 100; money.Balance = 150; },
		[348] = ( state, money ) => { state.MonthlyWages = 100; money.Balance = 90; money.LoanAvailable = true; },
		[349] = ( state, _ ) => state.Rides = new[] { Ride( broken: true, riders: 3 ) },
		[350] = ( state, _ ) => state.Rides = new[] { Ride( broken: true, condemned: true, riders: 2 ) },
	};

	/// <summary>The park state that makes each polled row hold.</summary>
	private static readonly IReadOnlyDictionary<int, Action<State>> PollScenarios = new Dictionary<int, Action<State>>
	{
		[83] = state => { state.Toilets = 0; state.NeedingToilet = 9; },
		[84] = state => { state.Toilets = 2; state.FarFromToilets = 5; },
		[277] = state => { state.Thresholds[GoldenTicketKind.ProfitYear] = 1000; state.ProfitThisYear = 600; },
		[318] = state => { state.Thresholds[GoldenTicketKind.ProfitYear] = 1000; state.ProfitThisYear = 900; },
	};

	private static IReadOnlyList<int> Scan( AdvisorController controller, IRestoredAdviceState? state, MoneyFake? money = null, int steps = 160, uint tick = 100, int gameType = 0, bool tutorial = true )
	{
		var posted = new List<int>();
		for ( var step = 0; step < steps; step++ )
			if ( controller.ScanOnce( money, tick, gameType, tutorial, restored: state ).Posted is { } advice )
				posted.Add( advice.MessageId );
		return posted;
	}

	private static RestoredAdviceDriver Driver( AdvisorController controller, int gameType = 0, bool tutorial = true ) => new( controller, () => 100u, () => gameType, () => tutorial );

	// ------------------------------------------------------------------ the extension

	[TestMethod]
	public void TheExtensionIsOnByDefaultAndNeverAffectsTheSimulation()
	{
		var extension = OfficialExtensions.RestoredAdvice;
		Assert.AreEqual( "opentpw.restored-advice", extension.Id );
		Assert.AreEqual( new ModVersion( 1, 0, 0 ), extension.Version );
		Assert.IsTrue( extension.DefaultEnabled );
		Assert.IsFalse( extension.AffectsSimulation, "on by default: a simulation extension would refuse every save made without it" );
		Assert.IsTrue( extension.PlayerSelectable );
		CollectionAssert.Contains( OfficialExtensions.All.ToArray(), extension );
		Assert.AreSame( extension, OfficialExtensions.Find( extension.Id ) );
		var defaults = ModSet.Defaults( OfficialExtensions.All );
		Assert.IsTrue( defaults.IsEnabled( extension.Id ) );
		Assert.IsFalse( defaults.SimulationMods.Any( mod => mod.Id == extension.Id ), "it is not recorded in park saves" );
		Assert.IsTrue( FeatureGates.RestoredAdvice, "the process default" );
		using ( OpenTpwExtensions.Use( Off() ) )
		{
			Assert.IsFalse( FeatureGates.RestoredAdvice );
			Assert.IsFalse( RestoredAdvice.Enabled );
		}
		Assert.IsTrue( FeatureGates.RestoredAdvice );
	}

	[TestMethod]
	public void TheExtensionIsListedWithItsStringsInEveryLanguage()
	{
		foreach ( var key in new[] { OpenTpwText.ExtensionRestoredAdviceName, OpenTpwText.ExtensionRestoredAdviceDescription } )
		{
			var texts = SupplementaryStrings.SupportedLanguages.Select( language => SupplementaryStrings.Get( key, language ) ).ToList();
			Assert.AreEqual( SupplementaryStrings.SupportedLanguages.Count, texts.Count );
			Assert.IsTrue( texts.All( text => !string.IsNullOrWhiteSpace( text ) ) );
			Assert.AreEqual( texts.Count, texts.Distinct().Count(), $"{key}: every language has its own text" );
		}
	}

	// ------------------------------------------------------------------ the catalog

	[TestMethod]
	public void TheCatalogMatchesTheDescriptorTable()
	{
		var rows = AdvisorDescriptorTable.Rows;
		var helpDefined = AdvisorTables.HelpMessages.Select( message => message.MessageId ).ToHashSet();
		Assert.AreEqual( RestoredAdvice.Entries.Count, RestoredAdvice.Entries.Select( entry => entry.MessageId ).Distinct().Count(), "each message once" );
		foreach ( var entry in RestoredAdvice.Entries )
		{
			var row = rows[entry.MessageId];
			var id = $"message {entry.MessageId}";
			StringAssert.StartsWith( entry.Label, "[EXT:restored-advice-", id );
			Assert.IsTrue( entry.Reason.Length > 10, $"{id} says why it fires" );
			switch ( entry.Trigger )
			{
				case RestoredTrigger.Scan:
					Assert.IsTrue( row.Scan && row.Callback == RestoredAdviceScan.ConstantZeroCallback, $"{id} is a scan-zero row" );
					Assert.IsTrue( RestoredAdviceScan.Callbacks.ContainsKey( entry.MessageId ), id );
					break;
				case RestoredTrigger.Poll:
					Assert.IsFalse( row.Scan, $"{id}: the scan skips it" );
					Assert.IsTrue( RestoredAdviceScan.PollRules.ContainsKey( entry.MessageId ), id );
					break;
				case RestoredTrigger.Event:
					Assert.IsFalse( row.Scan, id );
					Assert.IsFalse( RestoredAdviceScan.Callbacks.ContainsKey( entry.MessageId ) || RestoredAdviceScan.PollRules.ContainsKey( entry.MessageId ), id );
					break;
				case RestoredTrigger.Help:
					Assert.IsTrue( helpDefined.Contains( entry.MessageId ) && row.Callback == 0x59E8C0, $"{id} is a help message with the help score" );
					break;
			}
			// a score comes from a key of the settings (optional keys the extension reads) or from the extension's own constant, never from both
			if ( entry.ScoreKey != null && entry.Trigger != RestoredTrigger.Help )
				Assert.IsTrue( RestoredAdviceSettings.IntegerKeys.Contains( entry.ScoreKey ), $"{id}: {entry.ScoreKey} is read" );
			else if ( entry.Trigger != RestoredTrigger.Help )
				Assert.IsTrue( entry.ExtScore > 25, $"{id}: its own score is above the minimum the shipped file has" );
		}
		Assert.AreEqual( 16, RestoredAdvice.Entries.Count( entry => entry.Trigger == RestoredTrigger.Scan ) );
		CollectionAssert.AreEquivalent( RestoredAdvice.Entries.Where( entry => entry.Trigger == RestoredTrigger.Scan ).Select( entry => entry.MessageId ).ToArray(), ScanScenarios.Keys.ToArray(), "a scenario for every scan row" );
		CollectionAssert.AreEquivalent( RestoredAdvice.Entries.Where( entry => entry.Trigger == RestoredTrigger.Poll ).Select( entry => entry.MessageId ).ToArray(), PollScenarios.Keys.ToArray() );
	}

	[TestMethod]
	public void NoSilentStubPlaceholderOrDuplicateWelcomeIsRestored()
	{
		// the placeholder sample 638, the silent stubs and the duplicate welcome rows are skipped on purpose (docs/reverse/ADVISOR-SCAN.md "Restored advice")
		var responses = AdvisorResponses.Parse( File.ReadAllText( Shipped( AdvisorResponses.RelativePath ) ) );
		foreach ( var entry in RestoredAdvice.Entries )
		{
			var row = AdvisorDescriptorTable.Rows[entry.MessageId];
			Assert.AreNotEqual( 638, responses[row.FirstResponseId].Sample, $"message {entry.MessageId} is not the z_error placeholder" );
		}
		foreach ( var skipped in new[] { 161, 162, 163, 166, 168, 180, 181, 182, 183, 210, 267, 268, 269, 290, 291, 312, 313, 321, 324, 325, 326, 327, 335 } )
			Assert.IsFalse( RestoredAdvice.IsRestored( skipped ), $"message {skipped} stays skipped" );
	}

	private static string Shipped( string relative )
	{
		for ( var directory = new DirectoryInfo( AppContext.BaseDirectory ); directory != null; directory = directory.Parent )
			if ( File.Exists( Path.Combine( directory.FullName, relative ) ) )
				return Path.Combine( directory.FullName, relative );
		Assert.Fail( $"{relative} not found above the test directory." );
		return "";
	}

	// ------------------------------------------------------------------ the scan-zero rows through the real queue

	[TestMethod]
	public void EveryScanZeroTriggerFiresThroughTheScanAndTheQueue()
	{
		foreach ( var (message, scenario) in ScanScenarios )
		{
			var controller = Controller();
			var state = new State();
			var money = new MoneyFake { Balance = 5000 };
			scenario( state, money );
			var posted = Scan( controller, state, money );
			CollectionAssert.Contains( posted.ToArray(), message, $"message {message} is posted by the scan" );
			var advice = PendingOf( controller, message );
			Assert.IsTrue( advice.Score > controller.Queue.MinimumScore, $"message {message} scores above MinScoreForConsideration" );
			// the queue serves it: the response is one of the descriptor's own
			var row = AdvisorDescriptorTable.Rows[message];
			var played = new List<int>();
			for ( var clock = 1000u; Pending( controller ).Contains( message ) && clock < 100_000u; clock += 10_000u )
				controller.Update( () => clock, () => 100u, response => { played.Add( response ); return 1000u; } );
			Assert.IsTrue( played.Any( response => response >= row.FirstResponseId && response < row.FirstResponseId + row.ResponseCount ), $"message {message} says one of its own responses" );
		}
	}

	[TestMethod]
	public void TheScanScoresFollowTheirKeysOrTheExtensionsOwnConstants()
	{
		AdvisorPendingAdvice Posted( int message )
		{
			var controller = Controller();
			var state = new State();
			var money = new MoneyFake { Balance = 5000 };
			ScanScenarios[message]( state, money );
			Scan( controller, state, money );
			return PendingOf( controller, message );
		}
		Assert.AreEqual( 66, Posted( 265 ).Score, "RideExitBlocked.Score" );
		Assert.AreEqual( 66, Posted( 349 ).Score, "riders stuck use the stranded-people key" );
		Assert.AreEqual( 77, Posted( 350 ).Score, "RideCondemned.Score" );
		Assert.AreEqual( 64, Posted( 65 ).Score, "RidesNoUpgrades.Score" );
		Assert.AreEqual( RestoredAdvice.SecurityScoreCap, Posted( 24 ).Score, "40 percent uncovered x ScorePerNonCoveredPercent 2 is 80, capped below the originals' warnings" );
		Assert.AreEqual( RestoredAdvice.SecurityScoreCap, Posted( 25 ).Score, "100 percent uncovered x 2 is 200, capped" );
		Assert.AreEqual( RestoredAdvice.QueueScore, Posted( 58 ).Score );
		Assert.AreEqual( RestoredAdvice.QueueWorseScore, Posted( 59 ).Score );
		Assert.AreEqual( RestoredAdvice.LowBalanceScore, Posted( 347 ).Score );
		Assert.AreEqual( RestoredAdvice.LowBalanceLoanScore, Posted( 348 ).Score );
		Assert.AreEqual( RestoredAdvice.FameScore, Posted( 152 ).Score );
		Assert.AreEqual( RestoredAdvice.SouvenirScore, Posted( 160 ).Score );
		Assert.AreEqual( RestoredAdvice.LandScore, Posted( 173 ).Score );
	}

	[TestMethod]
	public void NothingFiresWithoutItsConditionAndTheOriginalsScanStaysUntouched()
	{
		// an empty park: no restored message, in a whole pass
		var controller = Controller();
		Assert.AreEqual( 0, Scan( controller, new State(), new MoneyFake { Balance = 100_000 }, steps: 400 ).Count( RestoredAdvice.IsRestored ) );
		// the conditions' edges
		var edges = new (int Message, Action<State, MoneyFake> Almost)[]
		{
			( 24, ( state, _ ) => { state.HiredGuards = 1; state.SecurityCoverage = new RestoredSecurityCoverage( 100, 95, 0 ); } ),
			( 24, ( state, _ ) => { state.ParkMonths = 2; state.HiredGuards = 1; state.SecurityCoverage = new RestoredSecurityCoverage( 100, 10, 0 ); } ),
			( 24, ( state, _ ) => state.SecurityCoverage = new RestoredSecurityCoverage( 100, 10, 0 ) ),
			( 25, ( state, _ ) => { state.HiredGuards = 1; state.SecurityCoverage = new RestoredSecurityCoverage( 100, 0, 0 ); } ),
			( 25, ( state, _ ) => state.SecurityCoverage = new RestoredSecurityCoverage( 0, 0, 0 ) ),
			( 58, ( state, _ ) => state.Rides = new[] { Ride( queue: 19 ), Ride( open: false, queue: 50 ) } ),
			( 59, ( state, _ ) => state.Rides = new[] { Ride( queue: 25 ), Ride( queue: 1 ), Ride( queue: 1 ) } ),
			( 65, ( state, _ ) => { state.UpgradesAvailable = 1; state.UpgradesBought = 1; } ),
			( 65, ( state, _ ) => { state.UpgradesAvailable = 1; state.UpgradesPossible = false; } ),
			( 151, ( state, _ ) => { state.Thresholds[GoldenTicketKind.CoasterHeight] = 100; state.Records[ParkRecordKind.CoasterHeight] = 74; } ),
			( 151, ( state, _ ) => { state.Thresholds[GoldenTicketKind.CoasterHeight] = 100; state.Records[ParkRecordKind.CoasterHeight] = 150; state.Held.Add( GoldenTicketKind.CoasterHeight ); } ),
			( 160, ( state, _ ) => { state.RetailSalesTwoMonths = 29; state.VisitorsTwoMonths = 40; } ),
			( 160, ( state, _ ) => { state.RetailSalesTwoMonths = 40; state.VisitorsTwoMonths = 90; } ),
			( 173, ( state, _ ) => state.Land = new RestoredLand( 100, 8 ) ),
			( 265, ( state, _ ) => state.Rides = new[] { Ride( broken: true, riders: 2 ) } ),
			( 347, ( state, money ) => { state.MonthlyWages = 100; money.Balance = -5; } ),
			( 347, ( state, money ) => { state.MonthlyWages = 100; money.Balance = 200; } ),
			( 348, ( state, money ) => { state.MonthlyWages = 100; money.Balance = 90; money.LoanAvailable = false; } ),
			( 349, ( state, _ ) => state.Rides = new[] { Ride( broken: true, riders: 0 ), Ride( riders: 4 ) } ),
			( 349, ( state, _ ) => state.Rides = new[] { Ride( broken: true, condemned: true, riders: 4 ) } ),
			( 350, ( state, _ ) => state.Rides = new[] { Ride( broken: true, riders: 4 ) } ),
		};
		foreach ( var (message, almost) in edges )
		{
			var state = new State();
			var money = new MoneyFake { Balance = 100_000 };
			almost( state, money );
			Assert.IsFalse( Scan( Controller(), state, money ).Contains( message ), $"message {message} needs more than its edge case" );
		}
	}

	[TestMethod]
	public void ASettingsFileWithoutAKeyLeavesItsMessageSilent()
	{
		var withoutKeys = string.Join( "\n", Sam.Split( '\n' ).Where( line => !line.Contains( "RideExitBlocked" ) && !line.Contains( "StaffGuardCoverage" ) ) );
		var state = new State { Rides = new[] { Ride( exitBlocked: true ) }, HiredGuards = 1, SecurityCoverage = new RestoredSecurityCoverage( 100, 10, 0 ) };
		var posted = Scan( Controller( withoutKeys ), state );
		Assert.IsFalse( posted.Contains( 265 ) || posted.Contains( 24 ) || posted.Contains( 25 ) );
	}

	// ------------------------------------------------------------------ off means silent

	[TestMethod]
	public void OffMeansSilentEverywhere()
	{
		using var off = OpenTpwExtensions.Use( Off() );
		foreach ( var (message, scenario) in ScanScenarios )
		{
			var controller = Controller();
			var state = new State();
			var money = new MoneyFake { Balance = 5000 };
			scenario( state, money );
			Assert.IsFalse( Scan( controller, state, money ).Contains( message ), $"message {message} stays silent while the extension is off" );
		}
		var poll = Controller();
		var pollDriver = Driver( poll );
		foreach ( var (message, scenario) in PollScenarios )
		{
			var state = new State();
			scenario( state );
			for ( var step = 0; step < 4; step++ )
				pollDriver.Poll( state );
		}
		Assert.AreEqual( 0, Pending( poll ).Count );
		// the events, the vehicles, the help and the online screens are silent too
		var events = Controller();
		var driver = Driver( events );
		var economy = EconomyTestData.Park();
		Win( economy, GoldenTicketKind.Visitors );
		driver.OnParkEvent( Event( ParkEventKind.GoldenTicketWon, (long)GoldenTicketKind.Visitors ), economy );
		driver.OnParkEvent( Event( ParkEventKind.UpgradeBought, 400, 1, 1100 ), economy );
		driver.OnParkEvent( Event( ParkEventKind.ChallengeOffered ), economy );
		driver.ObserveVehicle( -1 );
		driver.ObserveVehicle( ArrivalVehicleKind.Bus );
		driver.OnlineScreen( RestoredAdvice.PublishHelp );
		driver.Attached( popUpHelp: false );
		Assert.AreEqual( 0, Pending( events ).Count );
	}

	[TestMethod]
	public void TheScanHookGivesNoCallbackWhileOffAndTheCursorIsTheSameEitherWay()
	{
		Assert.IsNotNull( RestoredAdviceScan.CallbackFor( 58 ) );
		Assert.IsNull( RestoredAdviceScan.CallbackFor( 100 ), "a row the extension does not restore" );
		int onCursor, offCursor;
		var state = new State { Rides = new[] { Ride( queue: 30 ) }, Land = new RestoredLand( 100, 1 ) };
		var on = Controller();
		Scan( on, state, steps: 200 );
		onCursor = on.Scan.Cursor;
		using ( OpenTpwExtensions.Use( Off() ) )
		{
			Assert.IsNull( RestoredAdviceScan.CallbackFor( 58 ) );
			var off = Controller();
			Scan( off, state, steps: 200 );
			offCursor = off.Scan.Cursor;
		}
		Assert.AreEqual( onCursor, offCursor, "the scan walks the same rows with or without the extension (the saved cursor is the same)" );
	}

	// ------------------------------------------------------------------ the polled rows and the events through the real queue

	[TestMethod]
	public void EveryPolledTriggerFiresThroughTheQueue()
	{
		foreach ( var (message, scenario) in PollScenarios )
		{
			var controller = Controller();
			var state = new State();
			scenario( state );
			if ( message == 83 )
				controller.Queue.Restore( new AdvisorPendingAdvice?[AdvisorScoreQueue.Capacity], new Dictionary<int, AdvisorMessageHistory> { [82] = new AdvisorMessageHistory( 4, 0, true, 0 ) } );
			var driver = Driver( controller );
			for ( var step = 0; step < 4; step++ )
				driver.Poll( state );
			CollectionAssert.Contains( Pending( controller ).ToArray(), message, $"message {message} is posted by the poll" );
		}
		// the poll steps one rule at a time (like the scan): one call posts at most one message
		var one = Controller();
		var all = new State { Toilets = 0, NeedingToilet = 9 };
		PollScenarios[318]( all );
		Driver( one ).Poll( all );
		Assert.IsTrue( Pending( one ).Count <= 1 );
	}

	[TestMethod]
	public void NoToiletsEscalatesTheScansLineAndNeverRepeatsIt()
	{
		var state = new State();
		PollScenarios[83]( state );
		var controller = Controller();
		var driver = Driver( controller );
		for ( var step = 0; step < 4; step++ )
			driver.Poll( state );
		Assert.IsFalse( Pending( controller ).Contains( 83 ), "82 has not been said: the scan's own line comes first" );
		controller.Queue.Restore( new AdvisorPendingAdvice?[AdvisorScoreQueue.Capacity], new Dictionary<int, AdvisorMessageHistory> { [82] = new AdvisorMessageHistory( 4, 0, true, 0 ) } );
		for ( var step = 0; step < 4; step++ )
			driver.Poll( state );
		Assert.IsTrue( Pending( controller ).Contains( 83 ), "after 82, still no toilet" );
		Assert.IsFalse( RestoredAdvice.IsRestored( 67 ), "67 stays with the ported 66" );
	}

	[TestMethod]
	public void ThePollNeverPostsAnythingForAnEmptyPark()
	{
		var controller = Controller();
		var driver = Driver( controller );
		for ( var step = 0; step < 40; step++ )
			driver.Poll( new State() );
		Assert.AreEqual( 0, Pending( controller ).Count );
	}

	private static ParkEvent Event( ParkEventKind kind, long amount = 0, int instance = 0, int info = 0, string detail = "" ) => new( 0, kind, instance, info, amount, detail );

	private static void Win( ParkEconomy economy, GoldenTicketKind kind )
	{
		var counters = new ParkCounters();
		long profit = 0;
		var guests = new Guests();
		switch ( kind )
		{
			case GoldenTicketKind.Visitors: counters.Add( ParkCounters.Admissions, 100_000 ); break;
			case GoldenTicketKind.PeopleInPark: guests.People = 300; break;
			case GoldenTicketKind.Happiness: guests.People = 160; guests.Happiness = 100; break;
			case GoldenTicketKind.ProfitYear: profit = 1_000_000; break;
			case GoldenTicketKind.CoasterHeight: counters.Max( ParkCounters.Record( ParkRecordKind.CoasterHeight, 0 ), 100_000 ); break;
			case GoldenTicketKind.GokartExcitement: counters.Max( ParkCounters.Record( ParkRecordKind.GokartExcitement, 0 ), 100_000 ); break;
			case GoldenTicketKind.WaterLength: counters.Max( ParkCounters.Record( ParkRecordKind.WaterLength, 0 ), 100_000 ); break;
			default: throw new NotSupportedException( kind.ToString() );
		}
		Assert.IsTrue( economy.Objectives.CheckGoldenTickets( counters, guests, economy.Research, profit, new long[7], parkSeconds: 0 ).Contains( kind ), $"the test economy awards {kind}" );
	}

	private sealed class Guests : IParkGuestStatistics
	{
		public int People { get; set; }
		public float Happiness { get; set; }
		public int PeopleInPark => People;
		public float AverageHappiness => Happiness;
		public int CountHappierThan( int happiness ) => 0;
		public int KidsWithBalloonsPercent => 0;
		public int KidsWithCostumesPercent => 0;
	}

	// the ticket wins and the first challenge offer are the original's own advice (215-227, 344: AutomaticAdvisor.Challenges), so the extension stands down for them and only adds the global tickets' award lines

	private static readonly int[] StoodDownTicketLines = { 99, 164, 165, 218, 219, 223, 224, 228, 229 };

	private static AutomaticAdvisor AttachedAdvisor( ParkEconomy economy )
	{
		var advisor = new AutomaticAdvisor( Controller(), () => 0 );
		advisor.AttachEconomy( economy );
		return advisor;
	}

	private static IReadOnlyList<int> PendingOf( AutomaticAdvisor advisor ) =>
		advisor.Controller.CaptureSnapshot().Queue.Slots.Where( slot => slot != null ).Select( slot => slot!.MessageId ).ToArray();

	[TestMethod]
	public void ATicketWinSaysTheOriginalsAdviceOnceAndNoRestoredTicketLineWhetherTheExtensionIsOnOrOff()
	{
		foreach ( var on in new[] { true, false } )
		{
			using var scope = on ? null : OpenTpwExtensions.Use( Off() );
			Assert.AreEqual( on, RestoredAdvice.Enabled );
			var economy = EconomyTestData.Park();
			var advisor = AttachedAdvisor( economy );
			Win( economy, GoldenTicketKind.Visitors );
			advisor.OnParkEvent( Event( ParkEventKind.GoldenTicketWon, (long)GoldenTicketKind.Visitors ) );
			var extension = Driver( advisor.Controller );
			extension.OnParkEvent( Event( ParkEventKind.GoldenTicketWon, (long)GoldenTicketKind.Visitors ), economy );
			var pending = PendingOf( advisor );
			Assert.AreEqual( 1, pending.Count( message => message == 215 ), $"the original's local ticket line, once (extension {(on ? "on" : "off")})" );
			Assert.IsFalse( pending.Intersect( StoodDownTicketLines ).Any(), "no restored ticket line" );
			Win( economy, GoldenTicketKind.CoasterHeight );
			advisor.OnParkEvent( Event( ParkEventKind.GoldenTicketWon, (long)GoldenTicketKind.CoasterHeight ) );
			extension.OnParkEvent( Event( ParkEventKind.GoldenTicketWon, (long)GoldenTicketKind.CoasterHeight ), economy );
			pending = PendingOf( advisor );
			Assert.AreEqual( 1, pending.Count( message => message == 220 ), "the original's global ticket line" );
			Assert.IsFalse( pending.Intersect( StoodDownTicketLines ).Any() );
			Assert.AreEqual( on, pending.Contains( 230 ), "the award line is the extension's alone" );
		}
	}

	[TestMethod]
	public void TheExtensionKeepsOnlyTheAwardsOfTheTicketLines()
	{
		CollectionAssert.AreEquivalent( new[] { 230, 231, 232, 233 }, RestoredAdvice.Entries.Where( entry => entry.Topic is "awards" ).Select( entry => entry.MessageId ).ToArray() );
		Assert.IsFalse( RestoredAdvice.Entries.Any( entry => StoodDownTicketLines.Contains( entry.MessageId ) || entry.MessageId is 344 or 319 or 320 ), "those lines are the original's now" );
		Assert.AreEqual( 233, RestoredAdvice.AwardMessage( GoldenTicketKind.BigPark ) );
		Assert.IsNull( RestoredAdvice.AwardMessage( GoldenTicketKind.Visitors ) );
		Assert.IsNull( AdvisorTicketAdvice.For( GoldenTicketKind.AllLandOwned, 1 ), "the one ticket the original never awards or advises is never won here either" );
	}

	[TestMethod]
	public void AFirstChallengeOfferSays344OnceWhetherTheExtensionIsOnOrOff()
	{
		foreach ( var on in new[] { true, false } )
		{
			using var scope = on ? null : OpenTpwExtensions.Use( Off() );
			var economy = EconomyTestData.Park();
			economy.OpenPark();
			var advisor = AttachedAdvisor( economy );
			economy.UpdateChallenges( 540 * ParkCalendar.SecondsPerDay / ParkCalendar.SecondsPerTurn + 1 );
			Assert.IsNotNull( economy.Objectives.Current );
			CollectionAssert.AreEqual( new[] { 344 }, PendingOf( advisor ).ToArray(), $"the first offer says 344 and nothing else, once (extension {(on ? "on" : "off")})" );
		}
	}

	[TestMethod]
	public void UpgradesAndChallengesAndArrivalsPostTheirLines()
	{
		var economy = EconomyTestData.Park();
		var controller = Controller();
		var driver = Driver( controller );
		// an upgrade bought or researched with no mechanic, then with a mechanic on strike
		driver.OnParkEvent( Event( ParkEventKind.UpgradeBought, 400, 1, 1100 ), economy );
		driver.OnParkEvent( Event( ParkEventKind.ItemResearched, 1, 1, 1100, "Belly Bounce" ), economy );
		driver.OnParkEvent( Event( ParkEventKind.ItemResearched, 0, 1, 1100, "a ride" ), economy );
		CollectionAssert.AreEquivalent( new[] { 130, 132 }, Pending( controller ).ToArray(), "a ride research (amount 0) is not an upgrade" );
		Assert.AreEqual( 88, PendingOf( controller, 130 ).Score );
		Assert.AreEqual( 56, PendingOf( controller, 132 ).Score );
		var hired = Controller();
		var hiredDriver = Driver( hired );
		EconomyTestData.HireBest( economy, StaffType.Mechanic );
		hiredDriver.OnParkEvent( Event( ParkEventKind.UpgradeBought, 400, 1, 1100 ), economy );
		hiredDriver.OnParkEvent( Event( ParkEventKind.ItemResearched, 1, 1, 1100, "Belly Bounce" ), economy );
		Assert.AreEqual( 0, Pending( hired ).Count, "with a mechanic hired and no strike nothing is said" );
		// arrivals: the vehicle at work is taken as the start, a vehicle that starts a group says its line
		var arrivals = Controller();
		var arrivalDriver = Driver( arrivals );
		arrivalDriver.ObserveVehicle( ArrivalVehicleKind.Bus );
		arrivalDriver.ObserveVehicle( ArrivalVehicleKind.Bus );
		Assert.AreEqual( 0, Pending( arrivals ).Count, "the first observation is the start, and a vehicle that stays is no new group" );
		// the simulation puts the vehicle back to none after each group: the same kind again is not a change
		arrivalDriver.ObserveVehicle( -1 );
		arrivalDriver.ObserveVehicle( ArrivalVehicleKind.Bus );
		Assert.AreEqual( 0, Pending( arrivals ).Count, "another busload is no news" );
		arrivalDriver.ObserveVehicle( -1 );
		arrivalDriver.ObserveVehicle( ArrivalVehicleKind.Seaplane );
		arrivalDriver.ObserveVehicle( -1 );
		arrivalDriver.ObserveVehicle( ArrivalVehicleKind.Ferry );
		CollectionAssert.AreEquivalent( new[] { 138, 139 }, Pending( arrivals ).ToArray(), "a vehicle of another kind is" );
		// and each is told once per park, whatever the order of the vehicles
		arrivals.Queue.Restore( new AdvisorPendingAdvice?[AdvisorScoreQueue.Capacity], new Dictionary<int, AdvisorMessageHistory> { [138] = new AdvisorMessageHistory( 50, 0, true, 0 ) } );
		arrivalDriver.ObserveVehicle( -1 );
		arrivalDriver.ObserveVehicle( ArrivalVehicleKind.Seaplane );
		arrivalDriver.ObserveVehicle( -1 );
		arrivalDriver.ObserveVehicle( ArrivalVehicleKind.Bus );
		arrivalDriver.ObserveVehicle( -1 );
		arrivalDriver.ObserveVehicle( ArrivalVehicleKind.Seaplane );
		CollectionAssert.AreEquivalent( new[] { 137 }, Pending( arrivals ).ToArray(), "138 has been said in this park" );
	}

	[TestMethod]
	public void TheRestoredExtensionStandsDownForTheChallengeFollowUpTheOriginalAdvisesItself()
	{
		// 319 and 320 (double or nothing) told the event the original's own advice tells (270 offered, 272 won): the extension no longer restores them
		Assert.IsFalse( RestoredAdvice.IsRestored( 319 ) );
		Assert.IsFalse( RestoredAdvice.IsRestored( 320 ) );
		var economy = EconomyTestData.Park();
		var controller = Controller();
		var driver = Driver( controller );
		var current = typeof( ParkObjectives ).GetProperty( nameof( ParkObjectives.Current ) )!;
		current.GetSetMethod( nonPublic: true )!.Invoke( economy.Objectives, new object?[] { new ActiveChallenge( 7, 1, false, 0, 0, 0 ) } );
		driver.OnParkEvent( Event( ParkEventKind.ChallengeCompleted, 0, 0, 7 ), economy );
		driver.OnParkEvent( Event( ParkEventKind.ChallengeCompleted, 0, 0, 8 ), economy );
		Assert.AreEqual( 0, Pending( controller ).Count, "a met challenge with a follow-up posts nothing from the extension" );
	}

	private const long ChallengeDay = ParkCalendar.SecondsPerDay;
	private const long ChallengeStart = 540 * ChallengeDay;

	private static long ChallengeTurn( long seconds )
	{
		var turn = seconds / ParkCalendar.SecondsPerTurn + 1;
		while ( (turn & 3) == 0 )
			turn++;
		return turn;
	}

	[TestMethod]
	public void AMetChallengeIsToldOnceWithTheExtensionOnThroughTheRealAdvisor()
	{
		Assert.IsTrue( RestoredAdvice.Enabled, "the extension is on by default" );
		foreach ( var nextFollowup in new[] { 0, 13 } )
		{
			var settings = EconomyTestData.Settings( 500000 );
			settings.AdoptChallenges( new[] { new ChallengeDefinition( 0, 11, 12, 10, 1000, 0, 1100, 0, 700, false, true ), new ChallengeDefinition( 0, 12, nextFollowup, 10, 1000, 0, 1100, 0, 700, false, true ) } );
			var economy = new ParkEconomy( settings, EconomyTestData.Catalog(), ParkGameMode.FullSimulation, 7 );
			economy.Objectives.Update( ChallengeStart, 1, _ => 0 ).ToList();
			economy.Objectives.AcceptAt( ChallengeStart, 0 );
			var advisor = new AutomaticAdvisor( Controller(), () => 0L );
			advisor.AttachLevel( Live( economy ) );
			economy.Counters.Add( ParkCounters.Admissions, 1000 );
			economy.UpdateChallenges( ChallengeTurn( ChallengeStart + ChallengeDay ) );
			Assert.IsTrue( economy.Objectives.Current is { Accepted: false }, "the follow-up is offered" );
			var told = Pending( advisor.Controller ).Where( message => message != 0 ).ToArray();
			CollectionAssert.AreEqual( nextFollowup == 0 ? new[] { 270, 272 } : new[] { 270 }, told, "the original's lines only, one announcement per event" );
			CollectionAssert.DoesNotContain( told, 319 );
			CollectionAssert.DoesNotContain( told, 320 );
		}
	}

	[TestMethod]
	public void TheTutorialAndOnlineHelpIsPostedWithTheHelpScoreAndTheTutorialRules()
	{
		foreach ( var entry in RestoredAdvice.Entries.Where( entry => entry.Trigger == RestoredTrigger.Help ) )
		{
			var controller = Controller();
			Driver( controller ).Help( entry.MessageId, "test" );
			Assert.AreEqual( new[] { entry.MessageId }.Length, Pending( controller ).Count, $"help {entry.MessageId}" );
			Assert.AreEqual( 1000, PendingOf( controller, entry.MessageId ).Score, "HelpMessage.Score" );
			// the tutorial option off refuses it (the help is the tutorial group's)
			var refused = Controller();
			Driver( refused, tutorial: false ).Help( entry.MessageId, "test" );
			Assert.AreEqual( 0, Pending( refused ).Count, $"help {entry.MessageId} is tutorial" );
		}
		// the online screens' help
		var online = Controller();
		var driver = Driver( online );
		driver.OnlineScreen( RestoredAdvice.WorldHelp );
		driver.OnlineScreen( 100 );
		CollectionAssert.AreEqual( new[] { 334 }, Pending( online ).ToArray() );
		// a help message that is not restored is not the extension's to post
		var other = Controller();
		Driver( other ).Help( 188, "the buy window" );
		Assert.AreEqual( 0, Pending( other ).Count );
		Assert.AreEqual( 196, RestoredAdvice.WindowHelp["staffCosts"] );
		Assert.AreEqual( 211, RestoredAdvice.WindowHelp["rideUpgrade"] );
		foreach ( var window in RestoredAdvice.WindowHelp.Keys )
			Assert.IsNull( AdvisorUiHelp.MessageFor( window ), $"{window} has no original help" );
	}

	// ------------------------------------------------------------------ admission rules

	[TestMethod]
	public void TheQueuesAdmissionRulesApplyToRestoredAdvice()
	{
		var controller = Controller();
		// the descriptor's duplicate limit (message 58 allows one pending copy, 230 up to 255)
		Assert.IsTrue( controller.AdviseRestored( 58, 100, 0, true )!.Value.Stored );
		Assert.AreEqual( AdvisorEligibility.DuplicateLimit, controller.AdviseRestored( 58, 100, 0, true )!.Value.Eligibility );
		Assert.IsTrue( controller.AdviseRestored( 230, 100, 0, true )!.Value.Stored );
		Assert.IsTrue( controller.AdviseRestored( 230, 100, 0, true )!.Value.Stored );
		// the game-mode selector: 130 is for the full simulation only
		Assert.IsNull( controller.AdviseRestored( 130, 100, 2, true ), "not said in Instant Action" );
		Assert.IsTrue( controller.AdviseRestored( 130, 100, 0, true )!.Value.Stored );
		// the repeat interval of its group (message 160 is a congratulation, group 3), from the history a played message leaves
		var repeat = Controller();
		Assert.IsTrue( repeat.AdviseRestored( 160, 100, 0, true )!.Value.Stored );
		var played = new List<int>();
		repeat.Update( () => 1000u, () => 100u, response => { played.Add( response ); return 1000u; } );
		Assert.AreEqual( 1, played.Count );
		Assert.IsNull( repeat.AdviseRestored( 160, 105, 0, true ), "the extension's own rule: the congratulation is said once per park" );
		Assert.IsNull( repeat.AdviseRestored( 160, 100_000, 0, true ), "however long after" );
		// the original group rule still holds for a message with the extension's default (message 58: group 0, 120 groups of four turns, long repeat on top)
		// (the award lines carry no repeat rule of their own, so only their descriptor's group interval applies: group 2, given an interval here)
		var group = Controller( Sam.Replace( "MessageGroups[2].MinTimeSameMessage\t\t0", "MessageGroups[2].MinTimeSameMessage\t\t30" ) );
		Assert.IsTrue( group.AdviseRestored( 230, 100, 0, true )!.Value.Stored );
		group.Update( () => 1000u, () => 100u, response => 1000u );
		Assert.AreEqual( AdvisorEligibility.RepeatDelay, group.AdviseRestored( 230, 105, 0, true )!.Value.Eligibility, "the descriptor's group interval applies to an event line" );
		// a long repeat (message 173) lets the line come back after its interval, and not before
		var longRepeat = Controller();
		Assert.IsTrue( longRepeat.AdviseRestored( 173, 100, 0, true )!.Value.Stored );
		longRepeat.Update( () => 1000u, () => 100u, response => 1000u );
		Assert.IsNull( longRepeat.AdviseRestored( 173, 100 + 4 * 400, 0, true ), "past the group's interval (120 groups) but inside the extension's" );
		Assert.IsNull( longRepeat.AdviseRestored( 173, 100 + 4 * (RestoredAdvice.LongRepeatGroups - 1), 0, true ) );
		Assert.IsTrue( longRepeat.AdviseRestored( 173, 100 + 4 * (RestoredAdvice.LongRepeatGroups + 1), 0, true )!.Value.Stored );
		// the tutorial group says its message once
		var tutorial = Controller();
		Driver( tutorial ).Help( 196, "test" );
		tutorial.Update( () => 1000u, () => 100u, response => 1000u );
		Driver( tutorial ).Help( 196, "test again" );
		Assert.AreEqual( 0, Pending( tutorial ).Count, "once only" );
		// MinScoreForConsideration: a minimum above the scores serves none of it
		var strict = Controller( Sam.Replace( "GeneralAdvisor.MinScoreForConsideration	25", "GeneralAdvisor.MinScoreForConsideration	500" ) );
		var state = new State { Rides = new[] { Ride( queue: 25 ) }, Land = new RestoredLand( 100, 1 ) };
		Assert.IsFalse( Scan( strict, state ).Any( RestoredAdvice.IsRestored ), "scores of 65 or 70 are not above 500" );
		// a message the extension does not restore, or one without a score key in the settings, is not posted
		Assert.IsNull( controller.AdviseRestored( 100, 100, 0, true ) );
		Assert.IsNull( Controller( Sam.Replace( "UpgradeHireMechanic.Score	88", "" ) ).AdviseRestored( 130, 100, 0, true ) );
	}

	[TestMethod]
	public void RestoredResponsesCarryTheirCaptionsLikeEveryOtherAdvice()
	{
		// the captions of the captioned rows the extension restores come from the shipped caption table: entries of TAG_SYSTEM.str by id
		var captioned = new[] { 58, 59, 130, 137, 138, 139, 151, 152, 153, 154, 160 };
		foreach ( var message in captioned )
		{
			var row = AdvisorDescriptorTable.Rows[message];
			Assert.IsTrue( Enumerable.Range( row.FirstResponseId, row.ResponseCount ).Any( response => AdvisorCaptionTable.For( response ) != null ), $"message {message} has a caption on at least one response" );
		}
		// the original's ticket lines carry the ticket's own caption as the response variant
		var first = AdvisorDescriptorTable.Rows[215].FirstResponseId;
		var captions = Enumerable.Range( 0, 6 ).Select( variant => AdvisorCaptionTable.For( first + variant )?.TextEntry ).ToArray();
		CollectionAssert.AreEqual( new int?[] { 180, 181, 182, 183, 184, 185 }, captions, "the six local tickets, in the order of GoldenTicketKind" );
		var global = AdvisorDescriptorTable.Rows[220].FirstResponseId;
		CollectionAssert.AreEqual( new int?[] { 186, 187, 188, 189 }, Enumerable.Range( 0, 4 ).Select( variant => AdvisorCaptionTable.For( global + variant )?.TextEntry ).ToArray() );
	}

	// ------------------------------------------------------------------ the new tracking over a real economy

	private static ParkEconomy ParkWithPaths( out ParkCellMap cells )
	{
		var park = EconomyTestData.Park();
		cells = new ParkCellMap( 10, 4 );
		for ( var x = 0; x < 10; x++ )
			cells.SetType( x, 1, ParkCellType.Path );
		park.Litter.Cells = cells;
		park.Litter.Environment.Effects[RegionEffect.Camera] = new RegionEffect( 0, 0, 0, 0, 10, 0 );
		return park;
	}

	[TestMethod]
	public void TheSecurityCoverageJoinsCamerasGuardsAndPatrolAreasOverThePathCells()
	{
		var park = ParkWithPaths( out var cells );
		var state = new ParkRestoredAdviceState( park, null );
		Assert.AreEqual( new RestoredSecurityCoverage( 10, 0, 0 ), state.SecurityCoverage, "ten path cells, none covered" );
		Assert.AreEqual( 100, state.SecurityCoverage.UncoveredPercent );
		// a camera's region effect covers its cell
		park.Litter.Environment.Apply( RegionEffect.Camera, 2, 1, add: true );
		Assert.AreEqual( 1, state.SecurityCoverage.CoveredCells );
		Assert.AreEqual( 90, state.SecurityCoverage.UncoveredPercent );
		// a guard's patrol area covers the path cells inside it, and only his who has one set
		var guard = EconomyTestData.HireBest( park, StaffType.Guard );
		Assert.AreEqual( 1, new ParkRestoredAdviceState( park, null ).HiredGuards );
		Assert.AreEqual( 1, state.SecurityCoverage.CoveredCells, "a guard without a patrol area covers nothing by it" );
		guard.SetPatrolArea( (4, 0), (7, 2) );
		Assert.AreEqual( 5, state.SecurityCoverage.CoveredCells, "the camera's cell and the path cells 4 to 7" );
		Assert.AreEqual( 50, state.SecurityCoverage.UncoveredPercent );
		// a patrol area over the same cell as the camera counts it once
		guard.SetPatrolArea( (1, 0), (3, 2) );
		Assert.AreEqual( 3, state.SecurityCoverage.CoveredCells );
		// cells that are no path do not count
		cells.SetType( 8, 3, ParkCellType.Empty );
		Assert.AreEqual( 10, state.SecurityCoverage.PathCells );
		cells.SetType( 9, 1, ParkCellType.Queue );
		Assert.AreEqual( 10, state.SecurityCoverage.PathCells, "a queue cell is a path cell of the park, already counted as one here" );
		Assert.AreEqual( default, new ParkRestoredAdviceState( EconomyTestData.Park(), null ).SecurityCoverage, "without a cell map there is no cover to speak of" );
	}

	[TestMethod]
	public void PairedLinesNeverPlayBackToBack()
	{
		// both queue lines hold (59's condition includes 58's): only the stronger is posted
		var state = new State();
		ScanScenarios[59]( state, new MoneyFake() );
		var posted = Scan( Controller(), state );
		Assert.IsTrue( posted.Contains( 59 ) && !posted.Contains( 58 ) );
		// both balance lines hold (under the wage bill with a loan): 348 only
		var money = new MoneyFake { Balance = 50, LoanAvailable = true };
		var poor = new State { MonthlyWages = 100 };
		posted = Scan( Controller(), poor, money );
		Assert.IsTrue( posted.Contains( 348 ) && !posted.Contains( 347 ) );
		money.LoanAvailable = false;
		posted = Scan( Controller(), poor, money );
		Assert.IsTrue( posted.Contains( 347 ) && !posted.Contains( 348 ) );
	}

	[TestMethod]
	public void TheBlueprintHintEndsWithItsModeAndTheLineToolsHelpDoesNot()
	{
		var advisor = Attached( out _ );
		advisor.PostHelp( RestoredAdvice.RotateBlueprintHelp );
		advisor.PostHelp( AdvisorToolHelp.TrackLineStart );
		advisor.PostHelp( RestoredAdvice.CamcorderHelp );
		CollectionAssert.AreEquivalent( new[] { 202, 204, 306 }, Pending( advisor.Controller ).ToArray() );
		advisor.OnModeChanged();
		CollectionAssert.AreEquivalent( new[] { 204, 306 }, Pending( advisor.Controller ).ToArray(),
			"the blueprint hint goes with its tool; the camcorder's stays, and so does the line tool's 204, which neither SetMode withdraws (0x52F245..0x52F326, 0x52F5BF..0x52F6A0)" );
	}

	[TestMethod]
	public void TheLineToolsHelpIsTheOriginalsAndIsPostedWithTheExtensionOff()
	{
		Assert.IsFalse( RestoredAdvice.IsRestored( AdvisorToolHelp.TrackLineStart ), "204 is the kart and water line tool's own help now (0x526D41), no longer the coaster tool's restored hint" );
		using var scope = OpenTpwExtensions.Use( Off() );
		Assert.IsFalse( RestoredAdvice.Enabled );
		var advisor = Attached( out _ );
		advisor.PostHelp( AdvisorToolHelp.TrackLineStart );
		advisor.PostHelp( RestoredAdvice.RotateBlueprintHelp );
		CollectionAssert.AreEqual( new[] { 204 }, Pending( advisor.Controller ).ToArray(), "the original's help is posted whatever the extension; the restored hint only while it is on" );
	}

	[TestMethod]
	public void TheSecurityLinesFireThroughTheRealStateOfAPark()
	{
		var park = EconomyTestData.Park();
		var cells = new ParkCellMap( 40, 2 );
		for ( var x = 0; x < 40; x++ )
			cells.SetType( x, 1, ParkCellType.Path );
		park.Litter.Cells = cells;
		EconomyTestData.AdvanceToTurn( park, ParkCalendar.Turn( ParkCalendar.TickAtMonth( 7 ) ) );
		var state = new ParkRestoredAdviceState( park, null );
		Assert.IsTrue( state.ParkMonths >= 6 );
		Assert.IsTrue( Scan( Controller(), state ).Contains( 25 ), "no guard and no camera in a park of 40 path cells" );
		// two guards, one with a patrol area over a quarter of the path: the siblings' gates hold (2 guards, one patrol area set), 25 no longer
		var first = EconomyTestData.HireBest( park, StaffType.Guard );
		var second = EconomyTestData.HireBest( park, StaffType.Guard );
		Assert.IsFalse( Scan( Controller(), state ).Contains( 24 ), "no patrol area is set: the cover row's own gate" );
		first.SetPatrolArea( (0, 0), (9, 1) );
		var posted = Scan( Controller(), state );
		Assert.IsTrue( posted.Contains( 24 ) && !posted.Contains( 25 ), "a guard with a patrol area over ten of forty path cells" );
		var oneGuard = new State { ParkMonths = 12, HiredGuards = 1, GuardsWithPatrol = 1, SecurityCoverage = new RestoredSecurityCoverage( 100, 10, 0 ) };
		Assert.IsFalse( Scan( Controller(), oneGuard ).Contains( 24 ), "AtLeastThisMany guards" );
		first.SetPatrolArea( (0, 0), (39, 1) );
		Assert.IsFalse( Scan( Controller(), state ).Contains( 24 ), "everything patrolled" );
		Assert.IsNotNull( second );
	}

	[TestMethod]
	public void ARideExitWithoutAPathIsBlocked()
	{
		var grid = new GuestPathGrid( 24, 24 );
		for ( var x = 0; x < 16; x++ )
			grid.SetPath( x, 0, true );
		var shape = new ObjectShape( new[] { "*2S" } );
		var points = ObjectFootprint.GetAccessPoints( shape, 4, 8, 0 ).ToList();
		var owner = ParkCellMap.CellWord( 4, 8 );
		grid.Cells.WriteFootprint( ObjectFootprint.GetCells( shape, 4, 8, 0 ).ToList(), owner, 0 );
		ObjectAccessCells.Lay( grid.Cells, points, owner, hasQueue: true );
		var ride = new RideVisitorBridge( 7, "Queue ride", RideVisitorKind.Ride, 5, 55, 30 ) { OwnerWord = owner, Parameters = new QueueParameters( true, false, false, 130, 60, 60, 5, 30 ), HasCells = true };
		var exit = points.Single( point => point.Kind == ObjectCellKind.Exit );
		Assert.IsTrue( grid.IsWalkable( exit.OutsideX, exit.OutsideY ), "the placement laid the exit's path" );
		Assert.IsFalse( ParkRestoredAdviceState.ExitBlocked( ride, points, grid ) );
		// the player takes the path in front of the exit away
		grid.SetPath( exit.OutsideX, exit.OutsideY, false );
		Assert.IsFalse( ParkRestoredAdviceState.ExitBlocked( ride, points, grid ), "guests still resolve the exit to the queue and path cells near it (Level.WalkableNear): no false alarm" );
		// nothing walkable anywhere near: the guests' exit cell does not resolve
		var bare = new GuestPathGrid( 24, 24 );
		Assert.IsTrue( ParkRestoredAdviceState.ExitBlocked( ride, points, bare ), "riders could not get off" );
		// only a ride with registered cells, and a grid
		Assert.IsFalse( ParkRestoredAdviceState.ExitBlocked( new RideVisitorBridge( 9, "No cells", RideVisitorKind.Ride, 5, 55, 30 ) { Parameters = ride.Parameters }, points, grid ) );
		Assert.IsFalse( ParkRestoredAdviceState.ExitBlocked( ride, points, null ) );
		Assert.IsFalse( ParkRestoredAdviceState.ExitBlocked( ride, points.Where( point => point.Kind != ObjectCellKind.Exit ), grid ), "a ride without an exit point" );
	}

	[TestMethod]
	public void RidersStuckAreTheGuestsAboardABrokenOrCondemnedRide()
	{
		var park = EconomyTestData.Park();
		var ride = park.TryBuild( 1100, out var built ) == ParkEconomy.PurchaseResult.Ok ? built! : throw new InvalidOperationException( "the test park builds a ride" );
		var state = new ParkRestoredAdviceState( park, null );
		Assert.AreEqual( new RestoredRide( ride.Id, true, false, false, 0, 0, false ), state.Rides.Single() );
		ride.IsBrokenDown = true;
		ride.LifeGauge = 50;
		var broken = state.Rides.Single();
		Assert.IsTrue( broken.BrokenDown && !broken.Condemned );
		ride.LifeGauge = 0;
		ride.StateWord.Set( RideObjectStates.Condemned ); // the ride update enters state 4 when the gauge's byte is 0
		var condemned = state.Rides.Single();
		Assert.IsTrue( !condemned.BrokenDown && condemned.Condemned, "a life gauge of 0 is state 4" );
		ride.IsOpen = false;
		Assert.IsFalse( state.Rides.Single().Open );
		// the riders aboard come from the level's visitor bridges (RideVisitorBridge.RidersOnBoard, Riders): without a level there are none
		Assert.AreEqual( 0, state.Rides.Single().RidersAboard );
	}

	[TestMethod]
	public void ARidesFactsJoinTheEconomysStateWithTheBridgesRidersQueueAndExit()
	{
		var park = EconomyTestData.Park();
		var item = park.TryBuild( 1100, out var built ) == ParkEconomy.PurchaseResult.Ok ? built! : throw new InvalidOperationException();
		var grid = new GuestPathGrid( 24, 24 );
		var shape = new ObjectShape( new[] { "*2S" } );
		var points = ObjectFootprint.GetAccessPoints( shape, 4, 8, 0 ).ToList();
		var owner = ParkCellMap.CellWord( 4, 8 );
		grid.Cells.WriteFootprint( ObjectFootprint.GetCells( shape, 4, 8, 0 ).ToList(), owner, 0 );
		ObjectAccessCells.Lay( grid.Cells, points, owner, hasQueue: true );
		var bridge = new RideVisitorBridge( 7, "Ride", RideVisitorKind.Ride, 5, 55, 30 ) { OwnerWord = owner, Parameters = new QueueParameters( true, false, false, 130, 60, 60, 5, 30 ), HasCells = true };
		Assert.AreEqual( new RestoredRide( item.Id, true, false, false, 0, 0, false ), ParkRestoredAdviceState.RideFacts( item, bridge, points, grid ) );
		// guests aboard (the bridge's rider list) while the ride is broken down, then condemned
		var riders = (List<int>)typeof( RideVisitorBridge ).GetField( "onRide", BindingFlags.Instance | BindingFlags.NonPublic )!.GetValue( bridge )!;
		riders.AddRange( new[] { 11, 12, 13 } );
		item.IsBrokenDown = true;
		item.LifeGauge = 40;
		Assert.AreEqual( new RestoredRide( item.Id, true, true, false, 3, 0, false ), ParkRestoredAdviceState.RideFacts( item, bridge, points, grid ), "three riders stuck in a broken-down ride" );
		item.LifeGauge = 0;
		item.StateWord.Set( RideObjectStates.Condemned );
		Assert.AreEqual( new RestoredRide( item.Id, true, false, true, 3, 0, false ), ParkRestoredAdviceState.RideFacts( item, bridge, points, grid ), "and in a condemned one" );
		var exit = points.Single( point => point.Kind == ObjectCellKind.Exit );
		grid.SetPath( exit.OutsideX, exit.OutsideY, false );
		Assert.IsTrue( ParkRestoredAdviceState.RideFacts( item, bridge, points, grid ).ExitBlocked );
		// through the scan: the same facts post 350, and 265 for the blocked exit
		var controller = Controller();
		var posted = Scan( controller, new State { Rides = new[] { ParkRestoredAdviceState.RideFacts( item, bridge, points, grid ) } } );
		CollectionAssert.IsSubsetOf( new[] { 265, 350 }, posted.ToArray() );
		Assert.AreEqual( new RestoredRide( item.Id, true, false, true, 0, 0, false ), ParkRestoredAdviceState.RideFacts( item, null, null, null ), "a ride the level has no bridge for" );
	}

	[TestMethod]
	public void TheParkStateReadsTheEconomyWithoutChangingIt()
	{
		var park = ParkWithPaths( out var cells );
		cells.SetType( 3, 3, ParkCellType.Empty );
		var shop = park.TryBuild( 1203, out var drinks ) == ParkEconomy.PurchaseResult.Ok ? drinks! : throw new InvalidOperationException();
		var toilet = park.TryBuild( 1402, out var relief ) == ParkEconomy.PurchaseResult.Ok ? relief! : throw new InvalidOperationException();
		toilet.EffectCell = (2, 2);
		var state = new ParkRestoredAdviceState( park, null );
		Assert.AreEqual( 1, state.Toilets );
		Assert.AreEqual( 0, state.GuestsNeedingToilet( 50 ), "no level, no guests" );
		Assert.AreEqual( 0, state.RetailSalesTwoMonths, "a drinks shop is not a gift shop" );
		Assert.AreEqual( 0, state.UpgradesAvailable, "no upgrade is researched" );
		Assert.AreEqual( 0, state.UpgradesBought );
		Assert.IsTrue( state.UpgradesPossible );
		Assert.IsFalse( new ParkRestoredAdviceState( EconomyTestData.Park( mode: ParkGameMode.InstantAction ), null ).UpgradesPossible );
		Assert.AreEqual( 15000, state.TicketThreshold( GoldenTicketKind.ProfitYear ) );
		Assert.AreEqual( 105, state.TicketThreshold( GoldenTicketKind.CoasterHeight ) );
		Assert.AreEqual( 0, state.Record( ParkRecordKind.CoasterHeight ) );
		park.ReportRecord( ParkRecordKind.CoasterHeight, 5, 77 );
		Assert.AreEqual( 77, state.Record( ParkRecordKind.CoasterHeight ) );
		Assert.AreEqual( 0, state.VisitorsTwoMonths );
		Assert.AreEqual( new RestoredLand( 40, 30 ), state.Land, "40 cells, the ten of the path row occupied and the others free" );
		Assert.IsNotNull( shop );
		// reading it again changes nothing the simulation hash sees
		var before = WorldStateHash.Compute( new WorldStateSources { Economy = park } );
		_ = state.SecurityCoverage;
		_ = state.Rides;
		_ = state.Land;
		_ = state.UpgradesAvailable;
		Assert.AreEqual( before, WorldStateHash.Compute( new WorldStateSources { Economy = park } ) );
	}

	// ------------------------------------------------------------------ the automatic advisor end to end

	private static void Set( object target, string name, object? value ) => target.GetType().GetField( name, BindingFlags.Instance | BindingFlags.NonPublic )!.SetValue( target, value );

	private static Level Live( ParkEconomy economy )
	{
		var level = (Level)RuntimeHelpers.GetUninitializedObject( typeof( Level ) );
		Set( level, "<LevelName>k__BackingField", "jungle" );
		Set( level, "<Park>k__BackingField", ParkEconomyRuntime.ForResume( economy, ParkStartKind.FullSimulation ) );
		return level;
	}

	[TestMethod]
	public void ARunningAdvisorScansTheRealParkAndSavesTheSameWithTheExtensionToggledEitherWay()
	{
		var tutorial = GameOptions.Current.Tutorial;
		try
		{
			// the park has no room left to build in (a cell map of paths only): message 173 for the extension, nothing for the original
			AutomaticAdvisor Run( ParkEconomy economy, out AutomaticAdvisor.AutomaticAdvisorSnapshot snapshot )
			{
				var advisor = new AutomaticAdvisor( Controller(), () => 0L );
				advisor.AttachLevel( Live( economy ) );
				for ( var turn = 1; turn <= 170; turn++ )
				{
					EconomyTestData.AdvanceToTurn( economy, turn );
					advisor.ScanPark( economy, gamePaused: false );
				}
				snapshot = advisor.CaptureSnapshot();
				return advisor;
			}
			ParkEconomy Tight()
			{
				var economy = EconomyTestData.Park();
				var cells = new ParkCellMap( 6, 6 );
				for ( var x = 0; x < 6; x++ )
					for ( var y = 0; y < 6; y++ )
						cells.SetType( x, y, ParkCellType.Path );
				economy.Litter.Cells = cells;
				return economy;
			}
			var onAdvisor = Run( Tight(), out var on );
			CollectionAssert.Contains( Pending( onAdvisor.Controller ).ToArray(), 173, "the extension says the park is full" );
			AutomaticAdvisor.AutomaticAdvisorSnapshot off;
			AutomaticAdvisor offAdvisor;
			using ( OpenTpwExtensions.Use( Off() ) )
				offAdvisor = Run( Tight(), out off );
			Assert.IsFalse( Pending( offAdvisor.Controller ).Contains( 173 ), "the original's scan scores the row 0" );
			Assert.AreEqual( on.Controller.ScanCursor, off.Controller.ScanCursor, "the scan cursor is the same" );
			// the saves of both: JSON round trips, and each loads where the other came from
			var onBytes = ParkResumeJson.Serialize( on );
			var offBytes = ParkResumeJson.Serialize( off );
			var onBack = ParkResumeJson.Deserialize<AutomaticAdvisor.AutomaticAdvisorSnapshot>( onBytes );
			var offBack = ParkResumeJson.Deserialize<AutomaticAdvisor.AutomaticAdvisorSnapshot>( offBytes );
			Assert.AreEqual( onBytes.Length > 0, true );
			foreach ( var enabled in new[] { true, false } )
			{
				using var scope = enabled ? null : OpenTpwExtensions.Use( Off() );
				foreach ( var saved in new[] { onBack, offBack } )
				{
					var destination = new AutomaticAdvisor( Controller(), () => 0L );
					destination.ValidateSnapshot( saved );
					destination.RestoreSnapshot( saved );
					CollectionAssert.AreEqual( ParkResumeJson.Serialize( saved ), ParkResumeJson.Serialize( destination.CaptureSnapshot() ), $"a save round-trips with the extension {(enabled ? "on" : "off")}" );
				}
			}
		}
		finally
		{
			GameOptions.Current.Tutorial = tutorial;
		}
	}

	[TestMethod]
	public void RestoredAdviceChangesNeitherTheWorldStateHashNorTheModRecords()
	{
		ulong Hash( bool extensionOn )
		{
			using var scope = extensionOn ? null : OpenTpwExtensions.Use( Off() );
			var economy = EconomyTestData.Park();
			EconomyTestData.HireBest( economy, StaffType.Guard );
			var cells = new ParkCellMap( 4, 4 );
			for ( var x = 0; x < 4; x++ )
				cells.SetType( x, 0, ParkCellType.Path );
			economy.Litter.Cells = cells;
			var advisor = new AutomaticAdvisor( Controller(), () => 0L );
			advisor.AttachLevel( Live( economy ) );
			for ( var turn = 1; turn <= 40; turn++ )
			{
				EconomyTestData.AdvanceToTurn( economy, turn );
				advisor.ScanPark( economy, gamePaused: false );
			}
			return WorldStateHash.Compute( new WorldStateSources { Economy = economy } );
		}
		Assert.AreEqual( Hash( extensionOn: false ), Hash( extensionOn: true ), "the same park, run with and without the extension, hashes the same" );
		Assert.IsNull( ParkResumeMods.Capture( ModSet.Defaults( OfficialExtensions.All ) ), "a save records no mod for it, on" );
		Assert.IsNull( ParkResumeMods.Capture( Off() ), "or off" );
		ParkResumeMods.Require( null, ModSet.Defaults( OfficialExtensions.All ) );
		ParkResumeMods.Require( null, Off() );
	}

	[TestMethod]
	public void ASnapshotWithRestoredAdviceLoadsWhereTheExtensionIsOffAndBack()
	{
		var controller = Controller();
		Assert.IsTrue( controller.AdviseRestored( 58, 100, 0, true )!.Value.Stored );
		Assert.IsTrue( controller.AdviseRestored( 230, 100, 0, true )!.Value.Stored );
		var played = new List<int>();
		controller.Update( () => 1000u, () => 100u, response => { played.Add( response ); return 1000u; } );
		var snapshot = controller.CaptureSnapshot();
		Assert.IsTrue( snapshot.Queue.History.Count > 0, "the history of the message that was said is in the save" );
		foreach ( var enabled in new[] { false, true } )
		{
			using var scope = enabled ? null : OpenTpwExtensions.Use( Off() );
			var destination = Controller();
			destination.ValidateSnapshot( snapshot );
			destination.RestoreSnapshot( snapshot );
			Assert.AreEqual( snapshot.Queue.Slots.Count( slot => slot != null ), Pending( destination ).Count );
			CollectionAssert.AreEqual( ParkResumeJson.Serialize( snapshot ), ParkResumeJson.Serialize( destination.CaptureSnapshot() ) );
		}
		// a snapshot from before the extension (a pending original advice only) loads with it on
		var original = Controller();
		original.HandleGameEvent( AdvisorGameEvent.ParkNowClosed, 100, 0, true );
		original.RestoreSnapshot( original.CaptureSnapshot() );
		Assert.AreEqual( 1, Pending( original ).Count );
	}

	private static AutomaticAdvisor Attached( out List<int> captions ) => Attached( out captions, out _ );

	private static AutomaticAdvisor Attached( out List<int> captions, out ParkEconomy economy )
	{
		var advisor = new AutomaticAdvisor( Controller(), () => 0L );
		economy = EconomyTestData.Park();
		advisor.AttachLevel( Live( economy ) );
		advisor.Controller.Queue.Restore( new AdvisorPendingAdvice?[AdvisorScoreQueue.Capacity], new Dictionary<int, AdvisorMessageHistory>() );
		var list = new List<int>();
		advisor.CaptionSink = ( entry, _ ) => list.Add( entry );
		captions = list;
		return advisor;
	}

	[TestMethod]
	public void WithTheAdvisorOptionOffARestoredLineStillGoesToTheTubeAsACaption()
	{
		var option = GameOptions.Current.Advisor;
		try
		{
			GameOptions.Current.Advisor = false;
			var advisor = Attached( out var captions, out var economy );
			advisor.Update();   // the first update takes the park's turn as the start
			Assert.IsTrue( advisor.Controller.AdviseRestored( 59, 100, 0, true )!.Value.Stored );
			economy.Advance( ParkCalendar.TickOfTurn( 1 ) );
			advisor.Update();   // the queue is served once per park turn (0x59A550)
			Assert.AreEqual( 0, Pending( advisor.Controller ).Count, "the line is picked and consumed silently" );
			CollectionAssert.AreEqual( new[] { AdvisorCaptionTable.For( AdvisorDescriptorTable.Rows[59].FirstResponseId )!.Value.TextEntry }, captions, "its caption reaches the tube" );
		}
		finally
		{
			GameOptions.Current.Advisor = option;
		}
	}

	[TestMethod]
	public void TheWindowsAndHooksOfARunningAdvisorPostTheTutorialHelpOnlyWhileTheExtensionIsOn()
	{
		var advisor = Attached( out _ );
		advisor.OnWindowOpened( "staffCosts" );
		CollectionAssert.AreEqual( new[] { 196 }, Pending( advisor.Controller ).ToArray(), "the training budget window" );
		advisor.OnWindowClosed( "staffCosts" );
		Assert.AreEqual( 0, Pending( advisor.Controller ).Count, "closing it withdraws the help" );
		advisor.OnWindowOpened( "rideUpgrade" );
		advisor.PostHelp( RestoredAdvice.CamcorderHelp );
		advisor.OnOnlineScreenOpened( RestoredAdvice.PublishHelp );
		CollectionAssert.AreEquivalent( new[] { 211, 306, 328 }, Pending( advisor.Controller ).ToArray() );
		// an original window's help is the original's, on or off
		advisor.OnWindowOpened( "buy" );
		Assert.IsTrue( Pending( advisor.Controller ).Contains( 188 ) );
		using var off = OpenTpwExtensions.Use( Off() );
		var silent = Attached( out _ );
		silent.OnWindowOpened( "staffCosts" );
		silent.OnWindowOpened( "rideUpgrade" );
		silent.PostHelp( RestoredAdvice.CamcorderHelp );
		silent.PostHelp( RestoredAdvice.MessageIconHelp );
		silent.OnOnlineScreenOpened( RestoredAdvice.PublishHelp );
		Assert.AreEqual( 0, Pending( silent.Controller ).Count, "off means silent" );
		silent.OnWindowOpened( "buy" );
		CollectionAssert.AreEqual( new[] { 188 }, Pending( silent.Controller ).ToArray(), "the original's help is not the extension's" );
	}

	[TestMethod]
	public void AParkStartedWithThePopUpHelpOffSaysSoOnce()
	{
		var popUp = GameOptions.Current.PopupHelp;
		try
		{
			GameOptions.Current.PopupHelp = false;
			var advisor = new AutomaticAdvisor( Controller(), () => 0L );
			advisor.AttachLevel( Live( EconomyTestData.Park() ) );
			CollectionAssert.Contains( Pending( advisor.Controller ).ToArray(), 283 );
			using var off = OpenTpwExtensions.Use( Off() );
			var silent = new AutomaticAdvisor( Controller(), () => 0L );
			silent.AttachLevel( Live( EconomyTestData.Park() ) );
			Assert.IsFalse( Pending( silent.Controller ).Contains( 283 ) );
		}
		finally
		{
			GameOptions.Current.PopupHelp = popUp;
		}
	}

	// ------------------------------------------------------------------ the shipped settings

	[TestMethod]
	public void EveryRestoredMessageHasItsKeysAndItsGroupInTheShippedAdvisorSettings()
	{
		var gamePath = Environment.GetEnvironmentVariable( "OPENTPW_GAME_PATH" );
		var sam = gamePath == null ? null : Path.Combine( gamePath, "Data", "Advisor", "Advisor.sam" );
		if ( sam == null || !File.Exists( sam ) )
			Assert.Inconclusive( "Set OPENTPW_GAME_PATH for the original Advisor.sam." );
		var balance = AdvisorBalance.Parse( File.ReadAllBytes( sam ), sam );
		foreach ( var key in RestoredAdviceSettings.IntegerKeys.Concat( RestoredAdviceSettings.FloatKeys ) )
			Assert.IsTrue( balance.Restored.Has( key ), $"{key} is a key of Advisor.sam" );
		var controller = new AdvisorController( balance );
		foreach ( var entry in RestoredAdvice.Entries )
		{
			Assert.IsTrue( controller.Queue.HasDescriptor( entry.MessageId ), $"message {entry.MessageId} has its group's controls in the shipped file" );
			if ( entry.Trigger != RestoredTrigger.Help )
				Assert.IsTrue( RestoredAdvice.ScoreOf( balance, entry.MessageId ) > balance.MinimumScore || entry.MessageId is 24 or 25, $"message {entry.MessageId} scores above MinScoreForConsideration" );
		}
		Assert.IsTrue( balance.Restored.Float( "GoldTicketNearToXProfit.ProfitMultiplier" ) is > 0 and < 1, "the near-ticket multiplier is a fraction" );
		Assert.IsTrue( balance.Restored.Int( "StaffGuardCoverage.ScorePerNonCoveredPercent" ) > 0 );
	}
}

using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// [EXT:online-coop-commands] The setting commands against a level with placed rides (docs/COOP.md, "Settings"; the level ParkCommandLevelTests assembles without a GPU or game
/// data): the attraction commands act on the ride they name and nothing else, and a recorded session of settings replays on a twin park with the same hash after every tick.
/// </summary>
[TestClass]
[DoNotParallelize]
public class ParkSettingCommandLevelTests
{
	private AutomaticCheckpointCpuRendererScope? renderer;
	private UpgradeCatalogueDefaultsTests? fixture;

	internal sealed record Park( Level Level, ParkEconomy Economy, OriginalObject One, OriginalObject Two, ParkObjectState State );

	[TestInitialize]
	public void Initialize()
	{
		renderer = new AutomaticCheckpointCpuRendererScope();
		fixture = new UpgradeCatalogueDefaultsTests();
		fixture.Setup();
	}

	[TestCleanup]
	public void Cleanup()
	{
		fixture?.Cleanup();
		renderer?.Dispose();
	}

	private Park NewPark() => NewPark( fixture! );

	/// <summary>A level of <paramref name="fixture"/>'s with two placed rides, its staff, gate and paths set up, money in the bank, a mechanic and a full map's land (the tool commands' tests use it too).</summary>
	/// <param name="extra">More of the rides' type settings (a coaster's <c>Bumper.WhichTrackType 3</c>).</param>
	internal static Park NewPark( UpgradeCatalogueDefaultsTests fixture, string extra = "" )
	{
		var type = typeof( UpgradeCatalogueDefaultsTests );
		// the rides' speed slider runs from 10 to 100 (the fixture's type names no range of its own)
		var world = ((Level Level, ParkEconomy Economy, OriginalObject One, OriginalObject Two, ParkObjectState State))type.GetMethod( "World", BindingFlags.Instance | BindingFlags.NonPublic )!.Invoke( fixture,
			new object[] { "UsageInfo.MinSpeed 10\nUsageInfo.MaxSpeed 100\n" + extra, 0, false } )!;
		type.GetMethod( "PrepareCapture", BindingFlags.Static | BindingFlags.NonPublic )!.Invoke( null, new object[] { world } );
		typeof( Level ).GetMethod( "SetupStaff", BindingFlags.Instance | BindingFlags.NonPublic )!.Invoke( world.Level, null );
		typeof( Level ).GetMethod( "SetupParkGate", BindingFlags.Instance | BindingFlags.NonPublic )!.Invoke( world.Level, null );
		typeof( Level ).GetMethod( "SetupPaths", BindingFlags.Instance | BindingFlags.NonPublic )!.Invoke( world.Level, null );
		world.Level.ConfigureObjectHooks();
		// an uninitialised level has no field initialisers run: the line tool the park view holds
		typeof( Level ).GetField( "<CellTool>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic )!.SetValue( world.Level, new CellBuildTool() );
		typeof( Level ).GetField( "<PathVariants>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic )!.SetValue( world.Level, new PathTileVariants() );
		world.Economy.Ledger.Post( LedgerCategory.OtherIncome, 1_000_000 );
		// a mechanic, whom a maintenance call needs (both twins hire the same applicant), and a full map's land, whose net worth a loan's credit test needs
		EconomyTestData.HireBest( world.Economy, StaffType.Mechanic );
		world.Economy.Litter.Cells ??= new ParkCellMap( 128, 128 );
		return new Park( world.Level, world.Economy, world.One, world.Two, world.State );
	}

	private static void Run( Level level, int ticks, List<ulong> hashes )
	{
		for ( var tick = 0; tick < ticks; tick++ )
		{
			level.AdvanceSimulation( FixedStepClock.TickDuration );
			hashes.Add( level.ComputeStateHash() );
		}
	}

	[TestMethod]
	public void TheAttractionCommandsActOnTheRideTheyName()
	{
		var park = NewPark();
		var level = park.Level;
		var one = park.One.Runtime.AttractionId;
		Assert.IsTrue( park.One.IsOpen && park.Two.IsOpen );
		Assert.IsTrue( level.Commands.Issue( new SetAttractionOpenCommand( one, false ) ).Accepted );
		Assert.IsFalse( park.One.IsOpen, "the named ride closes" );
		Assert.IsTrue( park.Two.IsOpen, "the other stays open" );
		Assert.IsTrue( level.Commands.Issue( new SetAttractionOpenCommand( one, true ) ).Accepted );
		Assert.IsTrue( park.One.IsOpen );
		var before = park.Two.Runtime.Settings;
		Assert.IsTrue( level.Commands.Issue( new SetRideSettingsCommand( one, 60, 4, 30 ) ).Accepted );
		Assert.AreEqual( 60, park.One.Runtime.Settings.Speed );
		Assert.AreEqual( before, park.Two.Runtime.Settings, "the other ride keeps its settings" );
		// the speed keeps to the slider's range, as the window's load holds it (a peer's or a log's value past it)
		Assert.IsTrue( level.Commands.Issue( new SetRideSettingsCommand( one, 150, 4, 30 ) ).Accepted );
		Assert.AreEqual( 100, park.One.Runtime.Settings.Speed );
		Assert.IsTrue( level.Commands.Issue( new SetRideSettingsCommand( one, -5, 4, 30 ) ).Accepted );
		Assert.AreEqual( 10, park.One.Runtime.Settings.Speed );
		Assert.IsTrue( level.Commands.Issue( new RequestMaintenanceCommand( one, true ) ).Accepted );
		Assert.IsTrue( park.State.MaintenanceRequested );
		Assert.IsTrue( level.Commands.Issue( new RequestMaintenanceCommand( one, false ) ).Accepted );
		Assert.IsFalse( park.State.MaintenanceRequested );
		// a ride has no price, and an attraction the park does not hold takes no setting
		var hash = level.ComputeStateHash();
		Assert.IsFalse( level.Commands.Issue( new SetPriceCommand( one, 10 ) ).Accepted );
		Assert.IsFalse( level.Commands.Issue( new SetShopSettingsCommand( one, 50, 50 ) ).Accepted );
		Assert.IsFalse( level.Commands.Issue( new SetAttractionOpenCommand( 9999, false ) ).Accepted );
		Assert.IsFalse( level.Commands.Issue( new CancelUpgradeCommand( one ) ).Accepted, "no upgrade waits" );
		Assert.AreEqual( hash, level.ComputeStateHash() );
	}

	[TestMethod]
	public void ACondemnedRideRefusesItsDoorItsCallAndItsUpgrades()
	{
		var park = NewPark();
		var level = park.Level;
		var one = park.One.Runtime.AttractionId;
		// a breakdown with a life gauge of 0 is a condemned ride (ParkEconomy.ObjectState)
		park.State.IsBrokenDown = true;
		park.State.LifeGauge = 0;
		Assert.AreEqual( RideObjectStates.Condemned, ParkEconomy.ObjectState( park.State ) );
		var hash = level.ComputeStateHash();
		Assert.IsFalse( level.Commands.Issue( new SetAttractionOpenCommand( one, !park.One.IsOpen ) ).Accepted, "its window hides the door (0x4ad4e0)" );
		Assert.IsFalse( level.Commands.Issue( new RequestMaintenanceCommand( one, true ) ).Accepted, "and the call button" );
		Assert.IsFalse( level.Commands.Issue( new BuyUpgradeCommand( one ) ).Accepted, "and the upgrade button" );
		Assert.IsFalse( level.Commands.Issue( new CancelUpgradeCommand( one ) ).Accepted );
		Assert.AreEqual( hash, level.ComputeStateHash() );
		Assert.IsTrue( level.Commands.Issue( new SetAttractionOpenCommand( park.Two.Runtime.AttractionId, false ) ).Accepted, "the other ride's door still works" );
	}

	[TestMethod]
	public void AMaintenanceCallNeedsAnEmployeeWhoIsNotOnStrike()
	{
		var park = NewPark();
		var level = park.Level;
		var one = park.One.Runtime.AttractionId;
		park.Economy.Fire( park.Economy.Staff.OfType( StaffType.Mechanic ).Single().Id );
		Assert.IsFalse( level.Commands.Issue( new RequestMaintenanceCommand( one, true ) ).Accepted, "no mechanic is hired (UIHELPTEXT 574)" );
		Assert.IsFalse( park.State.MaintenanceRequested );
		Assert.IsFalse( level.Commands.Issue( new RequestMaintenanceCommand( 9999, true ) ).Accepted );
	}

	[TestMethod]
	public void AnUpgradeIsBoughtAndCancelledThroughItsCommands()
	{
		var park = NewPark();
		var level = park.Level;
		var one = park.One.Runtime.AttractionId;
		Assert.IsFalse( level.Commands.Issue( new BuyUpgradeCommand( one ) ).Accepted, "the next level is not researched yet" );
		// as UpgradeCatalogueDefaultsTests researches the next level: a researcher, nothing on the rides' own research
		EconomyTestData.HireBest( park.Economy, StaffType.Researcher );
		park.Economy.SetResearchEffort( ResearchCategory.Ride, 0 );
		for ( var day = 0; day < 100 && !park.Economy.Research.IsAvailable( 1100, 1 ); day++ )
			park.Economy.AdvanceDays( 1 );
		Assert.IsTrue( park.Economy.Research.IsAvailable( 1100, 1 ) );
		var bought = level.Commands.Issue( new BuyUpgradeCommand( one ) );
		Assert.IsTrue( bought.Accepted, bought.Reason );
		Assert.AreEqual( 1, park.State.PendingLevel );
		Assert.IsFalse( park.One.IsOpen, "the bought upgrade closes the ride for the mechanic" );
		Assert.IsTrue( park.Two.IsOpen );
		Assert.IsTrue( level.Commands.Issue( new CancelUpgradeCommand( one ) ).Accepted );
		Assert.AreEqual( 0, park.State.PendingLevel );
	}

	[TestMethod]
	public void ARecordedSettingsSessionReplaysOnATwinParkWithTheSameHashAfterEveryTick()
	{
		var recorded = NewPark();
		var twin = NewPark();
		Assert.AreEqual( recorded.Level.ComputeStateHash(), twin.Level.ComputeStateHash(), "the twin starts where the recording starts" );
		var start = recorded.Level.ComputeStateHash();
		var level = recorded.Level;
		var one = recorded.One.Runtime.AttractionId;
		var two = recorded.Two.Runtime.AttractionId;
		var hashes = new List<ulong>();
		var issued = 0;
		void Issue( params ParkCommand[] commands )
		{
			foreach ( var command in commands )
			{
				level.Commands.Issue( command );
				issued++;
			}
		}
		Run( level, 20, hashes );
		Issue( new SetParkOpenCommand( true ), new SetEntranceFeeCommand( 25 ) );
		Run( level, 20, hashes );
		Issue( new SetAttractionOpenCommand( one, false ), new SetRideSettingsCommand( two, 70, 3, 40 ) );
		Run( level, 30, hashes );
		Issue( new SetAttractionOpenCommand( one, true ), new RequestMaintenanceCommand( two, true ) );
		Run( level, 30, hashes );
		Issue( new SetTrainingBudgetCommand( StaffType.Mechanic, 200 ), new SetResearchWorkloadCommand( 60 ), new SetResearchEffortCommand( ResearchCategory.Ride, 40 ) );
		Run( level, 20, hashes );
		Issue( new RefreshLoanOffersCommand(), new TakeLoanCommand( 2, 18000 ), new SetGameSpeedCommand( GameSpeed.Fastest ) );
		Assert.IsTrue( recorded.Economy.Loans.Any(), "the loan was taken" );
		Run( level, 40, hashes );
		// refusals log too: an upgrade the park has not researched, a price on a ride, a challenge nobody offered, a loan never taken
		Issue( new BuyUpgradeCommand( one ), new SetPriceCommand( two, 5 ), new AnswerChallengeCommand( 0, 0, true ), new RepayLoanCommand( 1, 100 ) );
		Run( level, 20, hashes );
		Issue( new RepayLoanCommand( 2, recorded.Economy.Loans.Single().RemainingBalance ), new SetGameSpeedCommand( GameSpeed.Normal ), new RequestMaintenanceCommand( two, false ) );
		Assert.IsFalse( recorded.Economy.Loans.Any(), "the loan was paid off" );
		Run( level, 60, hashes );
		Assert.AreNotEqual( start, hashes[^1], "the session changed the park" );
		Assert.AreEqual( issued, level.Commands.Log.Count );
		var replayed = new List<ulong>();
		ParkCommandReplay.Run( twin.Level.Commands, level.Commands.Log, () => twin.Level.Commands.NextTick,
			() => twin.Level.AdvanceSimulation( FixedStepClock.TickDuration ), level.Commands.NextTick, _ => replayed.Add( twin.Level.ComputeStateHash() ) );
		Assert.AreEqual( hashes.Count, replayed.Count );
		for ( var tick = 0; tick < hashes.Count; tick++ )
			Assert.AreEqual( hashes[tick], replayed[tick], $"tick {tick + 1}" );
		CollectionAssert.AreEqual( level.Commands.Log.ToArray(), twin.Level.Commands.Log.ToArray(), "the replay applied the same commands before the same ticks" );
		Assert.AreEqual( recorded.Economy.Balance, twin.Economy.Balance );
		Assert.AreEqual( recorded.Two.Runtime.Settings, twin.Two.Runtime.Settings );
	}
}

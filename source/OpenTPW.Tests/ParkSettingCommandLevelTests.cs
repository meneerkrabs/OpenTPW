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

	private sealed record Park( Level Level, ParkEconomy Economy, OriginalObject One, OriginalObject Two, ParkObjectState State );

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

	private Park NewPark()
	{
		var type = typeof( UpgradeCatalogueDefaultsTests );
		var world = ((Level Level, ParkEconomy Economy, OriginalObject One, OriginalObject Two, ParkObjectState State))type.GetMethod( "World", BindingFlags.Instance | BindingFlags.NonPublic )!.Invoke( fixture, new object[] { "", 0, false } )!;
		type.GetMethod( "PrepareCapture", BindingFlags.Static | BindingFlags.NonPublic )!.Invoke( null, new object[] { world } );
		typeof( Level ).GetMethod( "SetupStaff", BindingFlags.Instance | BindingFlags.NonPublic )!.Invoke( world.Level, null );
		typeof( Level ).GetMethod( "SetupParkGate", BindingFlags.Instance | BindingFlags.NonPublic )!.Invoke( world.Level, null );
		typeof( Level ).GetMethod( "SetupPaths", BindingFlags.Instance | BindingFlags.NonPublic )!.Invoke( world.Level, null );
		world.Level.ConfigureObjectHooks();
		// an uninitialised level has no field initialisers run: the line tool the park view holds
		typeof( Level ).GetField( "<CellTool>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic )!.SetValue( world.Level, new CellBuildTool() );
		typeof( Level ).GetField( "<PathVariants>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic )!.SetValue( world.Level, new PathTileVariants() );
		world.Economy.Ledger.Post( LedgerCategory.OtherIncome, 1_000_000 );
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
		void Issue( params ParkCommand[] commands )
		{
			foreach ( var command in commands )
				level.Commands.Issue( command );
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
		Issue( new TakeLoanCommand( 0 ), new SetGameSpeedCommand( GameSpeed.Fastest ) );
		Run( level, 40, hashes );
		// refusals log too: an upgrade the park has not researched, a price on a ride, a challenge nobody offered, a gone loan
		Issue( new BuyUpgradeCommand( one ), new SetPriceCommand( two, 5 ), new AnswerChallengeCommand( true ), new RepayLoanCommand( 2 ) );
		Run( level, 20, hashes );
		Issue( new RepayLoanCommand( 0 ), new SetGameSpeedCommand( GameSpeed.Normal ), new RequestMaintenanceCommand( two, false ) );
		Run( level, 60, hashes );
		Assert.AreNotEqual( start, hashes[^1], "the session changed the park" );
		Assert.AreEqual( 18, level.Commands.Log.Count );
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

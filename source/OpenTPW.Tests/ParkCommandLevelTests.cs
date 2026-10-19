using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// [EXT:online-coop-commands] The command layer against a real <see cref="Level"/> (docs/COOP.md, "Recording and replay"): a build session made of the tools' own commands, recorded
/// once and replayed on a twin park from the same start, gives the same world hash after every tick; refused and malformed commands change nothing. The level is the one the
/// pools level tests assemble without a GPU or game data.
/// </summary>
[TestClass]
[DoNotParallelize]
public class ParkCommandLevelTests
{
	private AutomaticCheckpointCpuRendererScope? renderer;
	private UpgradeCatalogueDefaultsTests? fixture;

	private sealed record Park( Level Level, ParkEconomy Economy, OriginalObject One, OriginalObject Two );

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
		return new Park( world.Level, world.Economy, world.One, world.Two );
	}

	private static void Run( Level level, int ticks, List<ulong>? hashes )
	{
		for ( var tick = 0; tick < ticks; tick++ )
		{
			var before = level.Commands.NextTick;
			level.AdvanceSimulation( FixedStepClock.TickDuration );
			Assert.AreEqual( before + 1, level.Commands.NextTick, "one tick per step" );
			hashes?.Add( level.ComputeStateHash() );
		}
	}

	/// <summary>The scripted session: objects, the path tool (lines, undo, cancel), removals, the Move tool, a sale, and refused commands; the hash after every tick.</summary>
	private static List<ulong> BuildSession( Park park )
	{
		var level = park.Level;
		var hashes = new List<ulong>();
		Run( level, 30, hashes );
		var placed = level.Commands.Issue( level.BuildCommand( park.One.Entry, 10, 10, 0 ) ).As<OriginalObject>();
		Assert.IsNotNull( placed, level.LastActionMessage );
		Assert.IsNotNull( level.Commands.Issue( level.BuildCommand( park.One.Entry, 20, 12, 90 ) ).As<OriginalObject>(), level.LastActionMessage );
		Run( level, 20, hashes );
		// the path tool: its first click sets the start, each later click commits a line, Backspace takes the last back
		Assert.IsTrue( level.EnterPathTool( 5, 20 ) );
		Assert.IsTrue( level.CellTool.Click( (12, 20) ) is { Completed: true, Built.Count: 8 }, "the first line" );
		Run( level, 3, hashes );
		Assert.IsTrue( level.CellTool.Click( (12, 26) ) is { Completed: true }, "the second line" );
		Assert.IsTrue( level.CellTool.Undo(), "Backspace in the tool" );
		Assert.IsTrue( level.CellTool.Click( (16, 20) ) is { Completed: true }, "the third line" );
		level.CellTool.Cancel();
		Run( level, 15, hashes );
		Assert.IsTrue( level.Commands.Issue( new RemovePathCellCommand( 6, 20 ) ).Accepted );
		Assert.IsTrue( level.Commands.Issue( new BacktrackCommand( 16, 20 ) ).Accepted );
		Run( level, 10, hashes );
		// the Move tool: the pick-up is a command, the tool arms locally, the placement is the Move tool's
		Assert.IsTrue( level.BeginMove( placed! ) );
		Assert.IsTrue( level.IsMoving );
		Assert.IsNotNull( level.Commands.Issue( level.BuildCommand( level.BuildEntry!, 14, 14, level.BuildRotation ) ).As<OriginalObject>(), level.LastActionMessage );
		level.BuildEntry = null;
		Run( level, 10, hashes );
		Assert.IsTrue( level.Commands.Issue( new RemoveAttractionCommand( park.Two.Runtime.AttractionId ) ).Accepted, "the sell button" );
		Assert.IsTrue( level.Commands.Issue( new RemoveAtCommand( 20, 12 ) ).Accepted, "the remove tool" );
		Run( level, 5, hashes );
		// the park's settings: the door, the fee, a loan, an employee hired and dismissed, the lab, the rides' state, the speed and its pause
		var economy = park.Economy;
		Assert.IsTrue( level.Commands.Issue( new SetEntranceFeeCommand( 30 ) ).Accepted );
		Assert.IsTrue( level.Commands.Issue( new SetParkOpenCommand( true ) ).Accepted );
		Assert.IsTrue( economy.IsParkOpen );
		if ( economy.Features.Loans && economy.AvailableLoans.FirstOrDefault() is { } offer )
			Assert.IsTrue( level.Commands.Issue( new TakeLoanCommand( offer.Index ) ).Accepted );
		var candidate = economy.Staff.Candidates.First( item => economy.Staff.CanHire( item.Type ) );
		var hired = level.Commands.Issue( new HireStaffCommand( candidate.Id ) ).As<StaffMember>();
		Assert.IsNotNull( hired );
		Assert.IsTrue( level.Commands.Issue( new RenameStaffCommand( hired!.Id, "Ada" ) ).Accepted );
		Run( level, 20, hashes );
		Assert.IsTrue( level.Commands.Issue( new FireStaffCommand( hired.Id ) ).Accepted );
		Assert.IsFalse( level.Commands.Issue( new FireStaffCommand( hired.Id ) ).Accepted, "dismissed already" );
		if ( economy.Features.ResearchPanel )
		{
			Assert.IsTrue( level.Commands.Issue( new SetResearchEffortCommand( ResearchCategory.Ride, 70 ) ).Accepted );
			Assert.IsTrue( level.Commands.Issue( new SetResearchWorkloadCommand( 40 ) ).Accepted );
		}
		Assert.IsTrue( level.Commands.Issue( new SetRideOpenCommand( park.One.Runtime.AttractionId, !park.One.IsOpen ) ).Accepted );
		Assert.IsTrue( level.Commands.Issue( new SetSpeedCommand( GameSpeed.Paused ) ).Accepted );
		Run( level, 10, hashes );
		Assert.IsTrue( level.Commands.Issue( new SetSpeedCommand( GameSpeed.Normal ) ).Accepted );
		Run( level, 10, hashes );
		// refusals change nothing: an unknown entry, a placed object again on a taken cell, a move with nothing carried, a gone attraction
		var before = level.ComputeStateHash();
		Assert.IsFalse( level.Commands.Issue( new PlaceObjectCommand( "data:/levels/jungle/rides/none", 2, 2, 0 ) ).Accepted );
		Assert.IsFalse( level.Commands.Issue( level.BuildCommand( park.One.Entry, 14, 14, 0 ) ).Accepted, "the cell is taken" );
		Assert.IsFalse( level.Commands.Apply( level.BuildCommand( park.One.Entry, 25, 25, 0 ) with { Moving = true, Player = 2 } ).Accepted, "player 2 carries nothing" );
		Assert.IsFalse( level.Commands.Issue( new RemoveAttractionCommand( park.Two.Runtime.AttractionId ) ).Accepted );
		Assert.IsFalse( level.Commands.Issue( new PlaceObjectCommand( ParkCommandCodec.EntryKey( park.One.Entry ), 2, 2, 45 ) ).Accepted, "out of range" );
		Assert.IsFalse( level.Commands.Issue( new RepayLoanCommand( 99 ) ).Accepted, "no such loan" );
		Assert.IsFalse( level.Commands.Issue( new HireStaffCommand( int.MaxValue ) ).Accepted, "no such applicant" );
		Assert.IsFalse( level.Commands.Issue( new SetPricesCommand( 99999, 5 ) ).Accepted, "no such shop" );
		Assert.IsFalse( level.Commands.Issue( new RequestMaintenanceCommand( 99999, true ) ).Accepted );
		Assert.IsFalse( level.Commands.Issue( new SetRideOpenCommand( 99999, true ) ).Accepted );
		Assert.IsFalse( level.Commands.Issue( new AnswerChallengeCommand( true ) ).Accepted, "no challenge waits" );
		Assert.AreEqual( before, level.ComputeStateHash() );
		Run( level, 60, hashes );
		return hashes;
	}

	[TestMethod]
	public void ARecordedBuildSessionReplaysOnATwinParkWithTheSameHashAfterEveryTick()
	{
		var recorded = NewPark();
		var twin = NewPark();
		Assert.AreEqual( recorded.Level.ComputeStateHash(), twin.Level.ComputeStateHash(), "the twin starts where the recording starts" );
		var start = recorded.Level.ComputeStateHash();
		var hashes = BuildSession( recorded );
		Assert.AreNotEqual( start, hashes[^1], "the session changed the park" );
		Assert.IsTrue( recorded.Level.Commands.Log.Count >= 30, $"{recorded.Level.Commands.Log.Count} commands" );
		var replayed = new List<ulong>();
		ParkCommandReplay.Run( twin.Level.Commands, recorded.Level.Commands.Log, () => twin.Level.Commands.NextTick,
			() => twin.Level.AdvanceSimulation( FixedStepClock.TickDuration ), recorded.Level.Commands.NextTick, _ => replayed.Add( twin.Level.ComputeStateHash() ) );
		Assert.AreEqual( hashes.Count, replayed.Count );
		for ( var tick = 0; tick < hashes.Count; tick++ )
			Assert.AreEqual( hashes[tick], replayed[tick], $"tick {tick + 1}" );
		CollectionAssert.AreEqual( recorded.Level.Commands.Log.ToArray(), twin.Level.Commands.Log.ToArray(), "the replay applied the same commands before the same ticks" );
		Assert.AreEqual( recorded.Economy.Balance, twin.Economy.Balance );
		Assert.AreEqual( recorded.Level.Objects.Objects.Count, twin.Level.Objects.Objects.Count );
	}

	[TestMethod]
	public void ARecordingSurvivesItsFileAndItsCheckpointsProveTheReplay()
	{
		var recorded = NewPark();
		var twin = NewPark();
		recorded.Level.StartCommandRecording();
		BuildSession( recorded );
		var log = ParkCommandLogFile.Deserialize( recorded.Level.CommandLog()!.Serialize() );
		Assert.AreEqual( "jungle", log.Theme );
		Assert.AreEqual( twin.Level.ComputeStateHash(), log.StartHash );
		Assert.IsTrue( log.Checkpoints.Count >= 2, $"{log.Checkpoints.Count} checkpoints" );
		var hashes = new Dictionary<long, ulong>();
		ParkCommandReplay.Run( twin.Level.Commands, log.Entries, () => twin.Level.Commands.NextTick,
			() => twin.Level.AdvanceSimulation( FixedStepClock.TickDuration ), log.Checkpoints[^1].Ticks, ticks => hashes[ticks] = twin.Level.ComputeStateHash() );
		Assert.IsNull( ParkCommandReplay.FirstMismatch( log, hashes ) );
		// a log whose commands differ is caught by its checkpoints
		var other = NewPark();
		var wrong = new Dictionary<long, ulong>();
		ParkCommandReplay.Run( other.Level.Commands, log.Entries.Where( entry => entry.Command is not RemoveAttractionCommand ).ToArray(), () => other.Level.Commands.NextTick,
			() => other.Level.AdvanceSimulation( FixedStepClock.TickDuration ), log.Checkpoints[^1].Ticks, ticks => wrong[ticks] = other.Level.ComputeStateHash() );
		Assert.IsNotNull( ParkCommandReplay.FirstMismatch( log, wrong ) );
	}

	[TestMethod]
	public void AReadOnlyVisitRefusesEveryCommandAndKeepsItsHash()
	{
		var park = NewPark();
		typeof( Level ).GetField( "<Visit>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic )!.SetValue( park.Level, RuntimeHelpers.GetUninitializedObject( typeof( ParkVisitInfo ) ) );
		var before = park.Level.ComputeStateHash();
		var balance = park.Economy.Balance;
		foreach ( var command in new ParkCommand[]
		{
			park.Level.BuildCommand( park.One.Entry, 10, 10, 0 ),
			new RemoveAttractionCommand( park.One.Runtime.AttractionId ),
			new PickUpObjectCommand( park.Two.Runtime.AttractionId ),
			new LayLineCommand( CellToolMode.Path, -1, 5, 20, 12, 20 ),
			new RemoveAtCommand( 1, 1 ),
			new SetParkOpenCommand( true ),
			new SetEntranceFeeCommand( 99 ),
			new SetSpeedCommand( GameSpeed.Paused ),
			new SetRideOpenCommand( park.One.Runtime.AttractionId, !park.One.IsOpen ),
		} )
			Assert.IsFalse( park.Level.Commands.Issue( command ).Accepted, command.Kind.ToString() );
		Assert.IsFalse( park.Level.BeginMove( park.One ) );
		Assert.AreEqual( before, park.Level.ComputeStateHash() );
		Assert.AreEqual( balance, park.Economy.Balance );
		Assert.IsFalse( park.One.IsDeleted );
	}

	[TestMethod]
	public void ARefusedLineLaysNothingAndTheToolSeesItStopped()
	{
		var park = NewPark();
		park.Level.Commands.Rights = new NoLines();
		Assert.IsTrue( park.Level.EnterPathTool( 5, 20 ) );
		var before = park.Level.ComputeStateHash();
		var refused = park.Level.CellTool.Click( (12, 20) );
		Assert.IsNotNull( refused );
		Assert.IsFalse( refused!.Completed );
		Assert.AreEqual( 0, refused.Built.Count );
		Assert.AreEqual( before, park.Level.ComputeStateHash() );
	}

	private sealed class NoLines : IParkCommandRights
	{
		public bool Allows( int player, ParkCommandKind kind ) => kind != ParkCommandKind.LayLine;
	}
}

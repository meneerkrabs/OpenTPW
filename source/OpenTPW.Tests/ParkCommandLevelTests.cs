using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTPW.Mods;

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

	[TestMethod]
	public void NativeQueueDispatchRejectsFreshStartsAndNoQueueButReplaysTypedFailedContinuation()
	{
		var park = NewPark(); var level = park.Level; var map = level.Guests!.Grid.Cells;
		var ride = park.One.Visitors; ride.OwnerWord = Level.OwnerWord( park.One );
		ParkCommandResult Decoded( ParkCommand command ) => level.Commands.Issue( ParkCommandCodec.Decode( ParkCommandCodec.Encode( command ) ) );
		var balance = park.Economy.Balance; var version = map.Version;
		Assert.IsFalse( Decoded( new QueueSegmentCommand( ride.AttractionId, 10, 20, 13, 20 ) ).Accepted );
		Assert.AreEqual( version, map.Version ); Assert.AreEqual( balance, park.Economy.Balance );
		map.SetType( 10, 20, ParkCellType.Path ); version = map.Version;
		Assert.IsFalse( Decoded( new QueueSegmentCommand( ride.AttractionId, 10, 20, 13, 20 ) ).Accepted );
		Assert.AreEqual( version, map.Version );
		map.SetType( 10, 20, ParkCellType.Queue ); map.SetOwner( 10, 20, ride.OwnerWord );
		var parameters = ride.Parameters; ride.Parameters = parameters with { HasQueue = false }; version = map.Version;
		Assert.IsFalse( Decoded( new QueueSegmentCommand( ride.AttractionId, 10, 20, 13, 20 ) ).Accepted );
		Assert.AreEqual( version, map.Version ); Assert.AreEqual( balance, park.Economy.Balance );
		ride.Parameters = parameters;
		Assert.IsFalse( Decoded( new UndoQueueSegmentCommand( ride.AttractionId, 10, 20, 10, 20 ) ).Accepted, "no active receipt exists for a singleton" );
		Assert.AreEqual( version, map.Version ); Assert.AreEqual( balance, park.Economy.Balance );
		park.Economy.Ledger.Post( LedgerCategory.OtherCosts, park.Economy.Balance );
		var failed = Decoded( new QueueSegmentCommand( ride.AttractionId, 10, 20, 13, 20 ) );
		Assert.IsTrue( failed.Accepted, failed.Reason );
		Assert.AreEqual( CellBuildResult.NotEnoughMoney, failed.As<SegmentResult>()!.StoppedBy );
		Assert.IsTrue( map.HasQueuePassResidue( 13, 20 ) );
		Assert.AreEqual( ride.OwnerWord, map.OwnerAt( 13, 20 ) );
		Assert.IsTrue( Decoded( new QueueSegmentCommand( ride.AttractionId, 13, 20, 13, 22 ) ).Accepted,
			"typed same-owner residue admits failed-endpoint continuation without UI memory" );
		var count = map.PlacementCountAt( 10, 20 );
		Assert.IsTrue( Decoded( new LayLineCommand( CellToolMode.Queue, ride.AttractionId, 10, 20, 11, 20 ) ).Accepted );
		Assert.AreEqual( count, map.PlacementCountAt( 10, 20 ), "legacy kind retains old per-cell behavior inside mixed execution" );
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
		// refusals change nothing: an unknown entry, a placed object again on a taken cell, a move with nothing carried, a gone attraction
		var before = level.ComputeStateHash();
		Assert.IsFalse( level.Commands.Issue( new PlaceObjectCommand( "data:/levels/jungle/rides/none", 2, 2, 0 ) ).Accepted );
		Assert.IsFalse( level.Commands.Issue( level.BuildCommand( park.One.Entry, 14, 14, 0 ) ).Accepted, "the cell is taken" );
		Assert.IsFalse( level.Commands.Apply( level.BuildCommand( park.One.Entry, 25, 25, 0 ) with { Moving = true, Player = 2 } ).Accepted, "player 2 carries nothing" );
		Assert.IsFalse( level.Commands.Issue( new RemoveAttractionCommand( park.Two.Runtime.AttractionId ) ).Accepted );
		Assert.IsFalse( level.Commands.Issue( new PlaceObjectCommand( ParkCommandCodec.EntryKey( park.One.Entry ), 2, 2, 45 ) ).Accepted, "out of range" );
		Assert.AreEqual( before, level.ComputeStateHash() );
		Run( level, 60, hashes );
		return hashes;
	}

	/// <summary>The pool Backspace cases of the scripted session: water and a bridge laid, then taken with the key's command (a bridge, the bare water under it, a lone water cell), and refused presses.</summary>
	private static List<ulong> PoolBackspaceSession( Park park )
	{
		var level = park.Level;
		var hashes = new List<ulong>();
		Run( level, 5, hashes );
		var water = PoolBuilder.Rectangle( (12, 10), (14, 20) ).ToArray();
		Assert.IsTrue( level.Commands.Issue( new PaintPoolCellCommand( 12, 10, false ) ).Accepted );
		foreach ( var (x, y) in water.Skip( 1 ) )
			Assert.IsTrue( level.Commands.Issue( new PaintPoolCellCommand( x, y, false ) ).Accepted );
		Run( level, 3, hashes );
		Assert.IsTrue( level.Commands.Issue( new PlacePoolBridgeCommand( 10, 15, 1 ) ).Accepted, "a bridge across the pool" );
		Run( level, 3, hashes );
		Assert.IsTrue( level.Commands.Issue( new BacktrackCommand( 13, 15 ) ).Accepted, "Backspace over the water under the bridge takes the bridge" );
		Assert.IsTrue( level.Commands.Issue( new BacktrackCommand( 13, 15 ) ).Accepted, "and then the bare water cell" );
		Assert.IsTrue( level.Commands.Issue( new BacktrackCommand( 12, 11 ) ).Accepted, "a lone water cell" );
		Assert.IsFalse( level.Commands.Issue( new BacktrackCommand( 13, 15 ) ).Accepted, "the emptied cell: nothing to take" );
		Run( level, 10, hashes );
		return hashes;
	}

	[TestMethod]
	public void PoolBackspaceCommandsReplayOnATwinParkWithTheSameHashAfterEveryTick()
	{
		using var scope = OpenTpwExtensions.Use( PoolsTests.PoolsSet( true ) );
		var recorded = NewPark();
		var twin = NewPark();
		var start = recorded.Level.ComputeStateHash();
		var hashes = PoolBackspaceSession( recorded );
		Assert.AreNotEqual( start, hashes[^1] );
		var replayed = new List<ulong>();
		ParkCommandReplay.Run( twin.Level.Commands, recorded.Level.Commands.Log, () => twin.Level.Commands.NextTick,
			() => twin.Level.AdvanceSimulation( FixedStepClock.TickDuration ), recorded.Level.Commands.NextTick, _ => replayed.Add( twin.Level.ComputeStateHash() ) );
		Assert.AreEqual( hashes.Count, replayed.Count );
		for ( var tick = 0; tick < hashes.Count; tick++ )
			Assert.AreEqual( hashes[tick], replayed[tick], $"tick {tick + 1}" );
		Assert.AreEqual( recorded.Economy.Balance, twin.Economy.Balance );
		Assert.AreEqual( 0, twin.Level.PoolBridges!.Count );
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
		Assert.IsTrue( recorded.Level.Commands.Log.Count >= 14, $"{recorded.Level.Commands.Log.Count} commands" );
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

	/// <summary>
	/// The Move tool's end is a command (<see cref="EndMoveCommand"/>), whatever ends the tool: after it the player carries the picked-up type no more, so a moving placement of the type, which
	/// skips research and the balance test, is refused. While the tool stays (a placing click holding exactly Ctrl), another moving placement of the type is accepted, as the original's mode
	/// 0x3b takes it; a pick-up while carrying ends the old move first, so the new type is the one carried.
	/// </summary>
	[TestMethod]
	public void TheMoveToolsEndLeavesItsPlayerCarryingNothing()
	{
		var park = NewPark();
		var level = park.Level;
		var placed = level.Commands.Issue( level.BuildCommand( park.One.Entry, 10, 10, 0 ) ).As<OriginalObject>();
		Assert.IsNotNull( placed, level.LastActionMessage );
		Assert.IsTrue( level.BeginMove( placed! ) );
		var entry = level.BuildEntry!;
		var moved = level.Commands.Issue( level.BuildCommand( entry, 14, 14, level.BuildRotation ) ).As<OriginalObject>();
		Assert.IsNotNull( moved, level.LastActionMessage );
		// the tool stays armed after a placement holding exactly Ctrl: the next of the type is the Move tool's too, and charged as the first
		Assert.IsTrue( park.Economy.Catalog.TryGet( entry.InfoId, out var info ) && info.PurchaseCost > 0 );
		var balance = park.Economy.Balance;
		var again = level.Commands.Issue( level.BuildCommand( entry, 18, 14, level.BuildRotation ) ).As<OriginalObject>();
		Assert.IsNotNull( again, level.LastActionMessage );
		Assert.AreEqual( balance - info.PurchaseCost, park.Economy.Balance, "a move costs the whole price again" );
		Assert.IsTrue( level.IsMoving );
		// any other end of the tool (a placement without Ctrl, a cancel, another tool) leaves the build tool, and the command layer hears it
		level.BuildEntry = null;
		Assert.IsFalse( level.IsMoving );
		Assert.IsInstanceOfType( level.Commands.Log[^1].Command, typeof( EndMoveCommand ), "the tool's end is logged" );
		var before = level.ComputeStateHash();
		var refused = level.Commands.Issue( new PlaceObjectCommand( ParkCommandCodec.EntryKey( entry ), 22, 14, 0, Moving: true ) );
		Assert.IsFalse( refused.Accepted, "the ended tool's type is carried no more" );
		StringAssert.Contains( refused.Reason, "carries no", "refused by the carried test, not the cell" );
		Assert.IsFalse( level.Commands.Issue( new EndMoveCommand() ).Accepted, "nothing is carried" );
		Assert.AreEqual( before, level.ComputeStateHash() );
		// a pick-up while the tool carries ends the old move before the new pick-up (the fixture has one type, so the order of the log is the proof), and the move goes on
		Assert.IsTrue( level.BeginMove( again! ) );
		Assert.IsTrue( level.BeginMove( moved! ) );
		CollectionAssert.AreEqual( new[] { ParkCommandKind.PickUpObject, ParkCommandKind.EndMove, ParkCommandKind.PickUpObject },
			level.Commands.Log.TakeLast( 3 ).Select( item => item.Command.Kind ).ToArray() );
		Assert.IsNotNull( level.Commands.Issue( level.BuildCommand( level.BuildEntry!, 22, 14, level.BuildRotation ) ).As<OriginalObject>(), level.LastActionMessage );
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

	/// <summary>
	/// The executor's fuzz test: a seeded stream of commands of every kind with cells off the map or outside the codec's range, unknown and negative ids, negative players,
	/// rotations off the build angles, line modes no tool has and non-finite or far-off prototype positions. No command throws, and every refused one leaves the world hash
	/// unchanged; the accepted ones (a valid draw) change the park as any edit does, so later draws meet a changing park.
	/// </summary>
	[TestMethod]
	public void RandomMalformedCommandsNeverThrowAndARefusalLeavesTheWorldHashUnchanged()
	{
		var park = NewPark();
		var level = park.Level;
		// the no-data level's original park has no terrain; the prototype's placement check reads it, so it gets a flat one (32 by 32 cells)
		var size = 32;
		var terrain = (ModelHeightfield)Activator.CreateInstance( typeof( ModelHeightfield ), BindingFlags.Instance | BindingFlags.NonPublic, null,
			new object[] { size, size, 10f, 10f, 0f, 0f, Array.Empty<uint>(), new float[(size + 1) * (size + 1)], new uint[size * size] }, null )!;
		typeof( OriginalPark ).GetField( "<Heightfield>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic )!.SetValue( level.OriginalPark, terrain );
		var random = new SplitMix64( 0x0C00_F022_C1F0_0001UL );
		T Pick<T>( params T[] values ) => values[random.Below( values.Length )];
		int Coordinate() => random.Below( 3 ) == 0 ? random.Below( 40 ) : Pick( -1, -4096, int.MinValue, int.MaxValue, 0, 63, 64, 127, 128, 255, 256, 1000, ParkCommandCodec.MaximumCoordinate - 1, ParkCommandCodec.MaximumCoordinate );
		int Id() => Pick( -1, -2, int.MinValue, int.MaxValue, 0, 1, 99_999, park.One.Runtime.AttractionId, park.Two.Runtime.AttractionId, random.Below( 64 ) );
		int Rotation() => Pick( 0, 90, 180, 270, 45, -90, 360, int.MinValue );
		CellToolMode Mode() => (CellToolMode)Pick<byte>( 0, 1, 2, 3, 11, 14, 16, 19, 200, 255 );
		string Entry() => Pick( ParkCommandCodec.EntryKey( park.One.Entry ), ParkCommandCodec.EntryKey( park.Two.Entry ), "data:/levels/jungle/rides/none", "x", "", "mod:/../../etc" );
		// the prototype's Totem needs the game's catalogue, so its positions here are the ones it is refused for: non-finite, or off the terrain
		float Position() => Pick( float.NaN, float.PositiveInfinity, float.NegativeInfinity, -1e9f, 1e9f, float.MaxValue, float.MinValue );
		IReadOnlyList<(int X, int Y)> Cells() => Enumerable.Range( 0, random.Below( 5 ) ).Select( _ => (Coordinate(), Coordinate()) ).ToArray();
		// the settings' values: the windows' bounds, one past them, and the integers' ends
		int Value() => Pick( -1, int.MinValue, int.MaxValue, 0, 1, 50, 100, 101, 255, 256, 500, 501, 10_000, 10_001, short.MaxValue + 1 );
		long Amount() => Pick( -1L, long.MinValue, long.MaxValue, 0L, 100L, 10_000L, 10_001L, 18_000L );
		bool Flag() => random.Below( 2 ) == 0;
		string Text() => Pick( "Ann", "", "A\u0007B", "\ud800", new string( 'a', 600 ), "Kevin the", "Cosmic Cow" );
		ParkCommand Next() => random.Below( 72 ) switch
		{
			0 => new PlaceObjectCommand( Entry(), Coordinate(), Coordinate(), Rotation(), random.Below( 4 ) == 0, Pick( -1, -2, Id() ) ),
			1 => new RemoveAtCommand( Coordinate(), Coordinate() ),
			2 => new RemoveObjectAtCommand( Coordinate(), Coordinate() ),
			3 => new RemoveAttractionCommand( Id() ),
			4 => new PickUpObjectCommand( Id() ),
			5 => new LayLineCommand( Mode(), Pick( -1, Id() ), Coordinate(), Coordinate(), Coordinate(), Coordinate() ),
			6 => new UndoLineCommand( Mode(), Pick( -1, Id() ), Coordinate(), Coordinate(), Cells(), Cells() ),
			7 => new RemovePathCellCommand( Coordinate(), Coordinate() ),
			8 => new RemoveQueueCellCommand( Coordinate(), Coordinate() ),
			9 => new BacktrackCommand( Coordinate(), Coordinate() ),
			10 => new EditQueueCommand( Coordinate(), Coordinate() ),
			11 => new PlacePrototypeCommand( Position(), Position(), Position() ),
			12 => new RemovePrototypeCommand(),
			13 => new SetPrototypeRunningCommand( random.Below( 2 ) == 0 ),
			14 => new DismissFinaleCommand(),
			// the park's settings (C1 part 2)
			15 => new SetParkOpenCommand( Flag() ),
			16 => new SetEntranceFeeCommand( Value() ),
			17 => new SetAttractionOpenCommand( Id(), Flag() ),
			18 => new SetRideSettingsCommand( Id(), Value(), Value(), Value() ),
			19 => new SetPriceCommand( Id(), Value() ),
			20 => new SetGamePrizeCommand( Id(), Value(), Value(), Value() ),
			21 => new SetShopSettingsCommand( Id(), Value(), Value() ),
			22 => new SetTrainingBudgetCommand( (StaffType)Pick( -1, 0, 1, 4, 5, 99 ), Amount() ),
			23 => new SetResearchEffortCommand( (ResearchCategory)Pick( -1, 0, 4, 5 ), Value() ),
			24 => new SetResearchWorkloadCommand( Value() ),
			25 => new MoveResearchCommand( (ResearchCategory)Pick( -1, 0, 4, 5 ), Pick( -2, -1, 0, 1, 2 ) ),
			26 => new TakeLoanCommand( Pick( -1, 0, 1, 2, 7, 99 ), Amount() ),
			27 => new RepayLoanCommand( Pick( -1, 0, 1, 2, 99 ), Amount() ),
			28 => new BuyUpgradeCommand( Id() ),
			29 => new CancelUpgradeCommand( Id() ),
			30 => new RequestMaintenanceCommand( Id(), Flag() ),
			31 => new AnswerChallengeCommand( Pick( -1, 0, 1, 99 ), Amount(), Flag() ),
			32 => new RenameStaffCommand( Id(), Text() ),
			33 => new FireStaffCommand( Id() ),
			34 => new SetPatrolAreaCommand( Id(), Coordinate(), Coordinate(), Coordinate(), Coordinate(), random.Below( 3 ) == 0 ),
			35 => new RenameParkCommand( Text(), Text() ),
			36 => new SetGameSpeedCommand( (GameSpeed)Pick( -1, 0, 1, 2, 3, 4, 5 ) ),
			37 => new QueueCheatCommand( (CheatKind)Pick( -1, 0, 1, 2, 3, 99 ), Pick( -1, 0, 1, 9, 10, int.MaxValue ) ),
			38 => new RefreshLoanOffersCommand(),
			39 => new SetAllObjectsOpenCommand( Flag() ),
			// the park's tools (C1 part 3)
			40 => new BuyLandCommand( Coordinate(), Coordinate(), Coordinate(), Coordinate() ),
			41 => new BeginStaffHireCommand( Id() ),
			42 => new CancelStaffHireCommand(),
			43 => new PlaceStaffCommand( Id(), Coordinate(), Coordinate() ),
			44 => new PickUpStaffCommand( Id() ),
			45 => new DropStaffCommand( Id(), Coordinate(), Coordinate() ),
			46 => new OpenTrackLineToolCommand( Id() ),
			47 => new PaintPoolCellCommand( Coordinate(), Coordinate(), Flag() ),
			48 => new ErasePoolCellCommand( Coordinate(), Coordinate() ),
			49 => new UndoPoolStrokeCommand( Flag(), Cells(), random.Below( 3 ) == 0 ? Cells() : Array.Empty<(int X, int Y)>() ),
			50 => new SetPoolWavesCommand( Id(), Flag(), Pick( -1, 0, 1, 2, 3, 4 ), Pick( -1, 0, 1, 2, 3 ) ),
			51 => new LayPoolSlidePieceCommand( Coordinate(), Coordinate(), Coordinate(), Coordinate() ),
			52 => new UndoPoolSlidePieceCommand( Coordinate(), Coordinate() ),
			53 => new PlacePoolBridgeCommand( Coordinate(), Coordinate(), Pick( -1, 0, 1, 2, 3, 4 ) ),
			54 => new RemovePoolBridgeCommand( Coordinate(), Coordinate() ),
			55 => new LayTrackLineCommand( Id(), Coordinate(), Coordinate(), Coordinate(), Coordinate() ),
			56 => new UndoTrackLineCommand( Id(), Coordinate(), Coordinate(), Coordinate(), Coordinate() ),
			57 => new CutTrackCommand( Id(), Coordinate(), Coordinate(), Coordinate(), Coordinate() ),
			// the native queue segments
			58 => new QueueSegmentCommand( Id(), Coordinate(), Coordinate(), Coordinate(), Coordinate() ),
			59 => new UndoQueueSegmentCommand( Id(), Coordinate(), Coordinate(), Coordinate(), Coordinate() ),
			// the coaster tool (C1 part 3b)
			60 => new BeginCoasterToolCommand( Id() ),
			61 => new EndCoasterToolCommand( Id() ),
			62 => new CoasterClickCommand( Id(), Coordinate(), Coordinate(), Pick( 10f, -5f, float.NaN, 1e9f ), Flag(), Pick( -1, 0, 1, 2, 3 ) ),
			63 => new RemoveLastCoasterPylonCommand( Id() ),
			64 => new BeginCoasterNodeEditCommand( Id(), Pick( -1, 0, 2, 3, 5, 99 ), (CoasterDrag)Pick<byte>( 0, 1, 2, 3, 4, 5, 255 ) ),
			65 => new EditCoasterNodeCommand( Id(), (CoasterNodeField)Pick<byte>( 0, 1, 2, 3, 4, 5, 6, 255 ), Pick( 0f, 0.5f, -3f, 1e9f, float.NaN ), Coordinate(), Coordinate() ),
			66 => new EndCoasterNodeEditCommand( Id() ),
			67 => new SwapCoasterStackCommand( Id(), Pick( -1, 0, 3, 5 ), Flag() ),
			68 => new CutCoasterCommand( Id(), Pick( -1, 0, 3, 5 ), Pick( -1, 0, 4, 9 ) ),
			// a co-op session's clock (C2b)
			69 => new SetSessionClockCommand( Pick( -1L, long.MinValue, long.MaxValue, 0L, 63928191600L ) ),
			// the Move tool's end
			70 => new EndMoveCommand(),
			_ => new RemoveAttractionCommand( Pick( -1, int.MinValue, 99_999 ) ),
		};
		CollectionAssert.IsSubsetOf( Enum.GetValues<ParkCommandKind>(), Enumerable.Range( 0, 1024 ).Select( _ => Next().Kind ).Distinct().ToArray(), "the stream draws every kind" );
		var accepted = 0;
		var refused = 0;
		for ( var draw = 0; draw < 3000; draw++ )
		{
			var command = Next() with { Player = random.Below( 6 ) == 0 ? Pick( -1, int.MinValue, 1, 3 ) : ParkCommands.LocalPlayer };
			var before = level.ComputeStateHash();
			ParkCommandResult result;
			try
			{
				result = level.Commands.Apply( command );
			}
			catch ( Exception error )
			{
				Assert.Fail( $"draw {draw}: {command} threw {error}" );
				return;
			}
			// the funnel turns a throw into a logged rejection: the fuzz still fails on one
			Assert.IsNull( result.Error, $"draw {draw}: {command} threw {result.Error}" );
			if ( result.Accepted )
				accepted++;
			else
			{
				refused++;
				Assert.AreEqual( before, level.ComputeStateHash(), $"draw {draw}: the refused {command} ({result.Reason}) changed the park" );
			}
			if ( draw % 50 == 49 )
				Run( level, 1, null );
		}
		Assert.IsFalse( level.Commands.IsExecuting );
		Assert.IsTrue( refused > 2000, $"{refused} refused of 3000" );
		Assert.IsTrue( accepted > 0, "some draws are valid edits" );
	}

	/// <summary>The fuzz test's own generator (SplitMix64): the same stream on every runtime, unlike a seeded <see cref="Random"/> whose algorithm the runtime may change.</summary>
	private sealed class SplitMix64
	{
		private ulong state;

		public SplitMix64( ulong seed ) => state = seed;

		public ulong Next()
		{
			var z = state += 0x9E37_79B9_7F4A_7C15UL;
			z = (z ^ (z >> 30)) * 0xBF58_476D_1CE4_E5B9UL;
			z = (z ^ (z >> 27)) * 0x94D0_49BB_1331_11EBUL;
			return z ^ (z >> 31);
		}

		public int Below( int bound ) => (int)(Next() % (ulong)bound);
	}

	private sealed class NoLines : IParkCommandRights
	{
		public bool Allows( int player, ParkCommandKind kind ) => kind != ParkCommandKind.LayLine;
	}
}

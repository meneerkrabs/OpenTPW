using System;
using System.Buffers.Binary;
using System.Linq;
using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// Synthetic coverage of the wander from linked entrance and exit cells (GUEST-010, retired in round 10): the footprint and saved-link producers, an imported
/// deciding guest, and the shared staff caller. The candidate rule itself is checked against tp.exe in <see cref="WanderGoldenTests"/>.
/// </summary>
[TestClass]
public class LinkedAccessWanderTests
{
	private static readonly (int X, int Y) Start = (4, 4);
	private static readonly (int X, int Y) Outside = (5, 4);
	// The first draw is 511214250 (% 5 == 0), then 1511043788 and 1206099100 (sub-cell bytes 76 and 28).
	private const uint OneStepState = 2583822549u;

	[TestInitialize]
	public void Setup() => Log ??= new();

	private sealed class ScriptedDraws
	{
		private readonly uint[] values;
		public int Count { get; private set; }

		public ScriptedDraws( params uint[] values ) => this.values = values;

		public uint Next()
		{
			Assert.IsTrue( Count < values.Length, "drew more than scripted" );
			return values[Count++];
		}

		public void AssertExhausted() => Assert.AreEqual( values.Length, Count, "every scripted draw was consumed, with no extra draws" );
	}

	/// <summary>
	/// The entrance/exit is never made with a raw type/link setter: placement types the footprint, then the import copies the actual
	/// parsed saved link byte whose type and owner match it. The path corridor has explicit reciprocal links, as a saved path can.
	/// </summary>
	private static GuestPathGrid AccessGrid( string symbol, byte links = 0x04, bool backlink = true )
	{
		var grid = new GuestPathGrid( 16, 16 );
		var shape = new ObjectShape( new[] { symbol } );
		var footprint = ObjectFootprint.GetCells( shape, Start.X, Start.Y, 0 ).ToList();
		var access = ObjectFootprint.GetAccessPoints( shape, Start.X, Start.Y, 0 ).Single();
		Assert.AreEqual( Outside, (access.OutsideX, access.OutsideY), "the saved link points out of the actual shape" );
		var owner = ParkCellMap.CellWord( Start.X, Start.Y );
		grid.Cells.WriteFootprint( footprint, owner, 0 );
		var type = (byte)shape.Cells.Single().CellType;
		Assert.AreEqual( type, grid.Cells.RawTypeAt( Start.X, Start.Y ), "WriteFootprint produced the access type" );
		Assert.AreEqual( owner, grid.Cells.OwnerAt( Start.X, Start.Y ) );
		Assert.AreEqual( (byte)0, grid.LinksAt( Start.X, Start.Y ), "the footprint alone has no saved links" );

		var payload = new byte[grid.CountX * grid.CountY * SaveCell.RecordBytes];
		for ( var offset = 0; offset < payload.Length; offset += SaveCell.RecordBytes )
		{
			payload[offset] = 3;
			payload[offset + SaveCell.RecordBytes - 2] = 0xFF;
			payload[offset + SaveCell.RecordBytes - 1] = 0xFF;
		}
		var record = payload.AsSpan( (Start.Y * grid.CountX + Start.X) * SaveCell.RecordBytes, SaveCell.RecordBytes );
		record[SaveCell.PathConnectionsOffset] = links;
		BinaryPrimitives.WriteUInt16LittleEndian( record[SaveCell.ParentIdOffset..], owner );
		BinaryPrimitives.WriteUInt32LittleEndian( record[SaveCell.MapTypeOffset..], type );
		var saved = SaveCellGrid.ParseAt( payload, 0, payload.Length, grid.CountX, grid.CountY );
		grid.Cells.TakeSavedAccessLinks( footprint, owner, saved );
		Assert.AreEqual( links, grid.LinksAt( Start.X, Start.Y ), "TakeSavedAccessLinks copied the parsed record" );

		for ( var x = Outside.X; x <= 12; x++ )
			grid.SetPath( x, Outside.Y, true, connections: 0b1010 );
		if ( !backlink )
			grid.SetPath( Outside.X, Outside.Y, true, connections: 0b0010 );
		// No detour through grass can rescue a missing backlink in the route tests.
		for ( var y = 0; y < grid.CountY; y++ )
			for ( var x = 0; x < grid.CountX; x++ )
				if ( grid.Cells.RawTypeAt( x, y ) == 0 )
					grid.Cells.SetType( x, y, ParkCellTypes.Water );
		return grid;
	}

	private static (GuestSimulation Simulation, Guest Guest) ImportDecidingGuest( GuestPathGrid grid )
	{
		var settings = GuestTests.CreateSettings();
		settings.ExitLevelSeconds = 10_000;
		settings.SmallHappinessChange = 0;
		var simulation = new GuestSimulation( grid, settings, 5, OriginalGuestDecisions.Instance ) { ArrivalsEnabled = false };
		var body = SaveTestPayloads.Guest( new SaveTestPayloads.GuestFields
		{
			Position = ((ushort)(Start.X * 256 + 128), (ushort)(Start.Y * 256 + 128)),
			State = 6, PersonType = 0, Litter = 0, Illness = 0, ExitLevel = 10_000
		} );
		var payload = new byte[8 + body.Length];
		body.CopyTo( payload, 8 );
		var saved = SaveGuest.Read( payload, new SaveThing( 40, SaveThingModel.Guest, 0, 0, 8, body.Length ) );
		var random = simulation.RandomState;
		var report = simulation.ImportOriginal( new[] { saved }, 755, _ => 0, _ => 0 );
		var guest = simulation.Guests.Single();
		Assert.AreEqual( random, simulation.RandomState, "import takes no world draw" );
		Assert.AreEqual( (GuestState.DoingNothing, Start, Start), (guest.State, guest.Cell, guest.Navigation.Cell) );
		Assert.IsTrue( report.Any( line => line.Contains( $"guest 40 state 6 imported off the paths at (4, 4) (cell type {grid.Cells.RawTypeAt( 4, 4 )})", StringComparison.Ordinal ) ), string.Join( "\n", report ) );
		Assert.IsFalse( guest.HasWaypoint );
		return (simulation, guest);
	}

	private static bool Wander( GuestSimulation simulation, Guest guest ) =>
		(bool)typeof( GuestSimulation ).GetMethod( "Wander", BindingFlags.Instance | BindingFlags.NonPublic )!.Invoke( simulation, new object[] { guest } )!;

	private static bool Patrol( StaffWorldSimulation world, ParkEconomy park, StaffMember member ) =>
		(bool)typeof( StaffWorldSimulation ).GetMethod( "Patrol", BindingFlags.Instance | BindingFlags.NonPublic )!.Invoke( world, new object[] { park, member, Start } )!;

	private static uint AfterDraws( uint state, int count )
	{
		var random = new WorldRng( state );
		for ( var index = 0; index < count; index++ )
			random.NextWord();
		return random.State;
	}

	[DataTestMethod]
	[DataRow( "4" )]
	[DataRow( "E" )]
	public void SavedLinkedAccessStartsTakeOneStepWithoutAnIndexDraw( string symbol )
	{
		var grid = AccessGrid( symbol );
		Assert.IsFalse( grid.IsWalkable( Start.X, Start.Y ) || grid.IsQueue( Start.X, Start.Y ) );
		var draws = new ScriptedDraws( 0, 200, 2 );
		Span<(int X, int Y)> trail = stackalloc (int X, int Y)[GuestWander.TrailCapacity];
		Assert.AreEqual( new GuestWander.Destination( Outside, 72, 5 ), GuestWander.Pick( grid, Start, draws.Next, null, out var areaBlocked, trail, out var count ) );
		Assert.IsFalse( areaBlocked );
		Assert.AreEqual( 1, count );
		Assert.AreEqual( Outside, trail[0] );
		draws.AssertExhausted();
	}

	[DataTestMethod]
	[DataRow( "4" )]
	[DataRow( "E" )]
	public void LaterPathStepsDoNotReturnIntoTheAccessCellAndKeepTheirDrawOrder( string symbol )
	{
		var grid = AccessGrid( symbol );
		// Five steps: access -> path -> path take no index draw. Each of the final three steps has two path candidates;
		// index 3 points back, so the cyclic search skips it and continues east. The path's link into 9/10 stays excluded.
		var draws = new ScriptedDraws( 4, 3, 3, 3, 127, 0 );
		Span<(int X, int Y)> trail = stackalloc (int X, int Y)[GuestWander.TrailCapacity];
		Assert.AreEqual( new GuestWander.Destination( (9, 4), 123, 5 ), GuestWander.Pick( grid, Start, draws.Next, null, out var blocked, trail, out var count ) );
		Assert.IsFalse( blocked );
		CollectionAssert.AreEqual( new[] { (5, 4), (6, 4), (7, 4), (8, 4), (9, 4) }, trail[..count].ToArray() );
		draws.AssertExhausted();
	}

	[DataTestMethod]
	[DataRow( "4" )]
	[DataRow( "E" )]
	public void AccessStartsKeepTheirLinksToQueueCellsAndDropTheExitTargetsWhenChoosingABranch( string symbol )
	{
		// [BIN:TPW-EXE:0x004F9733 removal 2] the queue test removes the links to queue cells from a type-1 cell only: an entrance or exit start keeps them; [0x004F97E4 removal 4] every cell drops the links to type-10 cells
		var grid = AccessGrid( symbol, links: 0x55 );
		grid.SetPath( 4, 3, true );
		grid.SetQueue( 4, 5, 1 );
		grid.Cells.SetType( 3, 4, ParkCellTypes.Exit );
		// +Y is a queue (kept), +X and -Y are paths and -X an exit (dropped): three candidates, so one index draw, which picks the first in order, +Y
		var draws = new ScriptedDraws( 0, 0, 9, 11 );
		Assert.AreEqual( new GuestWander.Destination( (4, 5), 9, 11 ), GuestWander.Pick( grid, Start, draws.Next ) );
		draws.AssertExhausted();
		// the same grid from the path cell beside it: the path cell drops the queue link and the exit
		var path = new GuestPathGrid( 16, 16 );
		path.SetPath( 4, 4, true );
		path.SetPath( 5, 4, true );
		path.SetQueue( 4, 5, 1 );
		path.Cells.SetLinks( 4, 4, 0x55 );
		path.Cells.SetType( 3, 4, ParkCellTypes.Exit );
		Assert.AreEqual( 2, GuestWander.OriginalCandidates( path, (4, 4) ), "the path cell at +X and the link to bare ground at -Y are left: only queue and exit targets are dropped" );
	}

	[DataTestMethod]
	[DataRow( "4", (byte)0 )]
	[DataRow( "E", (byte)0 )]
	[DataRow( "4", (byte)0xAA )]
	[DataRow( "E", (byte)0xAA )]
	public void AccessWithoutCardinalLinksKeepsTheExistingOffPathRecovery( string symbol, byte links )
	{
		var grid = AccessGrid( symbol, links );
		var draws = new ScriptedDraws( 3 );
		Assert.IsNull( GuestWander.Pick( grid, Start, draws.Next, null, out var blocked ) );
		Assert.IsFalse( blocked );
		draws.AssertExhausted();

		var (simulation, guest) = ImportDecidingGuest( grid );
		simulation.RestoreRandomState( OneStepState );
		Assert.IsTrue( Wander( simulation, guest ), "the caller still uses OffPath after its one step-count draw" );
		Assert.AreEqual( (ulong)AfterDraws( OneStepState, 1 ), simulation.RandomState );
		Assert.AreEqual( GuestNavigation.BytePoint( Outside, 128, 128 ), (guest.Navigation.TargetX, guest.Navigation.TargetY) );
		Assert.IsTrue( guest.Navigation.Original && !guest.Navigation.Stuck && guest.HasWaypoint );
	}

	[DataTestMethod]
	[DataRow( "4" )]
	[DataRow( "E" )]
	public void LinkedAccessStepsOntoAnyTargetTypeButAnExit( string symbol )
	{
		foreach ( var type in new byte[] { 0, 2, 3, 4, 9, 10 } )
		{
			var grid = AccessGrid( symbol );
			grid.Cells.SetType( Outside.X, Outside.Y, type );
			// a step count of one (the first draw is 0 % 5), then the two sub-cell bytes
			var draws = new ScriptedDraws( 0, 7, 9 );
			var picked = GuestWander.Pick( grid, Start, draws.Next, null, out var stranded );
			if ( type == ParkCellTypes.Exit )
			{
				Assert.IsNull( picked, "the link to an exit cell is removed" );
				Assert.IsTrue( stranded, "no candidate is left" );
				Assert.AreEqual( 1, draws.Count );
				continue;
			}
			Assert.AreEqual( new GuestWander.Destination( Outside, 7, 9 ), picked, $"target type {type}" );
			Assert.IsFalse( stranded );
			draws.AssertExhausted();
		}
	}

	[TestMethod]
	public void LinkedStartsOfAnyTypeWalkTheirLinks()
	{
		// [BIN:TPW-EXE:0x004F95B9 start without links] only the start cell's link count decides: whatever its type, a cell with a link walks it
		foreach ( var type in new byte[] { 0, 2, 4, 5, 7, 30 } )
		{
			var grid = new GuestPathGrid( 16, 16 );
			grid.Cells.SetType( Start.X, Start.Y, type );
			grid.Cells.SetLinks( Start.X, Start.Y, 0x04 );
			grid.SetPath( Outside.X, Outside.Y, true );
			var draws = new ScriptedDraws( 0, 7, 9 );
			Assert.AreEqual( new GuestWander.Destination( Outside, 7, 9 ), GuestWander.Pick( grid, Start, draws.Next ), $"linked start type {type}" );
			draws.AssertExhausted();
		}
	}

	[DataTestMethod]
	[DataRow( "4" )]
	[DataRow( "E" )]
	public void ImportedDecidingGuestUsesWanderAndNavigateToWalkOut( string symbol )
	{
		var grid = AccessGrid( symbol );
		var (simulation, guest) = ImportDecidingGuest( grid );
		// First turn is 1, not guest 40's needs phase. Seed 37 chooses wander (% 3 == 1), then one step (% 5 == 0).
		simulation.RestoreRandomState( 37 );
		for ( var tick = 0; tick < 60 && simulation.ParkTurn == 0; tick++ )
			simulation.Tick( GuestSnapshotTests.Tick );
		Assert.AreEqual( 1L, simulation.ParkTurn );
		Assert.AreEqual( GuestState.WalkingAround, guest.State, "the real state-6 handler accepted Wander" );
		Assert.AreEqual( (ulong)AfterDraws( 37, 4 ), simulation.RandomState, "decision, step count and two sub-cell bytes; Navigate draws nothing" );
		Assert.AreEqual( GuestNavigation.BytePoint( Outside, 76, 28 ), (guest.Navigation.TargetX, guest.Navigation.TargetY) );
		Assert.IsTrue( guest.HasWaypoint && guest.Navigation.Original && !guest.Navigation.Stuck );
		Assert.IsTrue( guest.Navigation.PathCount > 0, "Navigate found an actual walk, not only the path-grid bookkeeping route" );
		for ( var tick = 0; tick < 600 && !grid.IsWalkable( guest.Navigation.Cell.X, guest.Navigation.Cell.Y ); tick++ )
			simulation.Tick( GuestSnapshotTests.Tick );
		Assert.AreEqual( Outside, guest.Navigation.Cell, "the steering step crossed onto the adjacent path" );
		Assert.IsFalse( guest.Navigation.Stuck );
		Assert.AreEqual( (byte)(symbol == "4" ? 9 : 10), grid.Cells.RawTypeAt( Start.X, Start.Y ), "recovery did not change the access cell into a path" );
	}

	[DataTestMethod]
	[DataRow( "4", false )]
	[DataRow( "E", true )]
	public void PickingAPathDoesNotBypassTheRoutesAsymmetricBacklinkRule( string symbol, bool reachesPath )
	{
		var grid = AccessGrid( symbol, backlink: false );
		var draws = new ScriptedDraws( 0, 76, 28 );
		Assert.AreEqual( new GuestWander.Destination( Outside, 76, 28 ), GuestWander.Pick( grid, Start, draws.Next ), "the picker reads its source link" );
		draws.AssertExhausted();
		Assert.AreEqual( !reachesPath, WalkEdges.Blocked( grid.Cells, Start.X, Start.Y, 1, 0 ), "only a type-9 source is a queue for the route edge test" );
		var (simulation, guest) = ImportDecidingGuest( grid );
		simulation.RestoreRandomState( OneStepState );
		Assert.AreEqual( reachesPath, Wander( simulation, guest ), "Wander returns Navigate's result" );
		Assert.AreEqual( (ulong)AfterDraws( OneStepState, 3 ), simulation.RandomState );
		Assert.IsTrue( guest.Navigation.Original );
		Assert.AreEqual( !reachesPath, guest.Navigation.Stuck );
		Assert.AreEqual( reachesPath, guest.HasWaypoint );
		Assert.AreEqual( GuestNavigation.BytePoint( Outside, 76, 28 ), (guest.Navigation.TargetX, guest.Navigation.TargetY) );
	}

	[DataTestMethod]
	[DataRow( "4" )]
	[DataRow( "E" )]
	public void PatrolAreaFiltersAccessCandidatesBeforeTakingAnIndexDraw( string symbol )
	{
		var grid = AccessGrid( symbol, links: 0x05 );
		grid.SetPath( 4, 3, true );
		var allowed = new ScriptedDraws( 0, 7, 9 );
		Assert.AreEqual( new GuestWander.Destination( Outside, 7, 9 ), GuestWander.Pick( grid, Start, allowed.Next, ( x, y ) => y == 4, out var blocked ) );
		Assert.IsFalse( blocked );
		allowed.AssertExhausted();
		var rejected = new ScriptedDraws( 0 );
		Assert.IsNull( GuestWander.Pick( grid, Start, rejected.Next, ( x, y ) => (x, y) == Start, out blocked ) );
		Assert.IsTrue( blocked, "both existing path candidates lie outside the patrol area" );
		rejected.AssertExhausted();
	}

	[DataTestMethod]
	[DataRow( "4" )]
	[DataRow( "E" )]
	public void StaffPatrolUsesTheSharedPickerAndRealNavigationFromAccessCells( string symbol )
	{
		var grid = AccessGrid( symbol );
		var park = EconomyTestData.Park();
		var member = EconomyTestData.HireBest( park, StaffType.Entertainer );
		var world = new StaffWorldSimulation( grid, () => park.Staff.Members, _ => null, Outside );
		park.StaffWorld = world;
		(member.CellX, member.CellY) = (4.5f, 4.5f);
		member.PatrolArea = (4, 4, 5, 4);
		Assert.IsNull( member.Navigation );
		// The first public update runs Place, including its grade-dependent speed initialization. At turn 0 an idle
		// entertainer has not reached the idle timeout, so it stays on the access cell without starting a patrol.
		world.UpdateStaffMember( park, member, 0 );
		Assert.IsNotNull( member.Navigation );
		Assert.AreEqual( Start, member.Navigation!.Cell );
		Assert.AreEqual( (int)GuestSpeed.BaseSpeeds[member.Grade < 3 ? 0 : 2], member.BaseSpeed );
		Assert.AreEqual( StaffJob.Idle, member.Job );
		Assert.AreEqual( 100f, member.Energy );
		park.World.Restore( OneStepState );
		Assert.IsTrue( Patrol( world, park, member ), "the actual shared staff caller routes from the access cell" );
		Assert.AreEqual( AfterDraws( OneStepState, 3 ), park.World.State );
		Assert.AreEqual( GuestNavigation.BytePoint( Outside, 76, 28 ), (member.Navigation.TargetX, member.Navigation.TargetY) );
		Assert.IsTrue( member.Navigation.Original && !member.Navigation.Stuck && member.Navigation.PathCount > 0 );
		member.Job = StaffJob.Patrol;
		for ( var turn = 1; turn <= 100 && member.Navigation.Cell == Start; turn++ )
			world.UpdateStaffMember( park, member, turn );
		Assert.AreEqual( Outside, member.Navigation.Cell, "the real staff walk crossed onto the path" );
	}
}

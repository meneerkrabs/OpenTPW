using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// Litter by cell (docs/reverse/WANDER-plan.md §6): the cell store (tp.exe <c>0x004D93B0</c> / <c>0x004D9620</c>), the guest's litter meter
/// and its state-6 step (<c>0x004FEC90</c>), the bin search (<c>0x00500DC0</c>) and the walk to the bin (state 9, <c>0x004FFF20</c>).
/// </summary>
[TestClass]
public class LitterTests
{
	private const float Tick = GuestSnapshotTests.Tick;

	[TestMethod]
	public void ACellHoldsOneItemUntilItIsRemoved()
	{
		var litter = new ParkLitter();
		var changes = new List<(int, int, LitterType)>();
		litter.Changed += ( x, y, type ) => changes.Add( (x, y, type) );
		Assert.IsTrue( litter.Drop( 4, 6, LitterType.Vomit ) );
		Assert.IsFalse( litter.Drop( 4, 6, (LitterType)2 ), "an occupied cell keeps its litter; the new item is lost" );
		Assert.IsTrue( litter.Drop( 4, 2, (LitterType)3 ) );
		Assert.IsTrue( litter.Drop( 1, 9, (LitterType)1 ) );
		Assert.IsFalse( litter.Drop( 128, 0, (LitterType)1 ), "off the map" );
		Assert.AreEqual( 3, litter.Count );
		CollectionAssert.AreEqual( new[] { (1, 9), (4, 2), (4, 6) }, litter.Littered().ToArray(), "x outer, y inner (the finder 0x004C8ED0)" );
		litter.Claim( 4, 6, 7, 120 );
		Assert.AreEqual( (7, 120L), (litter.CollectorAt( 4, 6 ), litter.ClaimTurnAt( 4, 6 )) );
		Assert.IsTrue( litter.Remove( 4, 6 ) );
		Assert.AreEqual( (LitterType.None, 0), (litter.TypeAt( 4, 6 ), litter.CollectorAt( 4, 6 )) );
		Assert.IsFalse( litter.Remove( 4, 6 ) );
		CollectionAssert.AreEqual( new[] { (4, 6, LitterType.Vomit), (4, 2, (LitterType)3), (1, 9, (LitterType)1), (4, 6, LitterType.None) }, changes );
		var copy = new ParkLitter();
		copy.Restore( litter.Snapshot() );
		CollectionAssert.AreEqual( litter.Snapshot().ToArray(), copy.Snapshot().ToArray() );
		litter.Disabled = true;
		Assert.IsFalse( litter.Drop( 9, 9, (LitterType)1 ), "an online visit drops none (game type 1)" );
	}

	[TestMethod]
	public void TheSpriteOffsetFollowsTheCellIdReseed()
	{
		// The world generator reseeded with y·128 + x + 1 and drawn twice (tp.exe 0x004D94E2), each draw % 9 + 1.
		Assert.AreEqual( (8, 1), ParkLitter.SpriteOffset( 0, 0 ) );
		Assert.AreEqual( (2, 9), ParkLitter.SpriteOffset( 20, 30 ) );
		Assert.AreEqual( (5, 6), ParkLitter.SpriteOffset( 127, 127 ) );
	}

	[TestMethod]
	public void TheLitterShareCountsPathAndQueueCells()
	{
		var grid = new GuestPathGrid( 12, 12 );
		for ( var x = 0; x < 10; x++ )
			grid.SetPath( x, 0, true );
		grid.SetQueue( 2, 1, 0 );
		var litter = new ParkLitter();
		Assert.IsNull( litter.StatisticAt( 0 ), "no map attached: no share" );
		litter.Cells = grid.Cells;
		litter.Drop( 3, 0, (LitterType)1 );
		litter.Drop( 5, 0, (LitterType)1 );
		Assert.AreEqual( new LitterStatistic( 11, 2 ), litter.StatisticAt( 0 ) );
		Assert.IsFalse( StaffStrikeBoard.Grievance( StaffType.Handyman, new[] { new StaffVital( 100, 100 ) }, litter: litter.StatisticAt( 0 ) ), "2/11 is not above 0.2" );
		litter.Drop( 7, 0, (LitterType)1 );
		// [BIN:TPW-EXE:0x4c8ba0 block litter] the strike reads the blocks with age 1: within (turn >> 2) - (stamp >> 2) <= 1 the counts stay
		Assert.AreEqual( new LitterStatistic( 11, 2 ), litter.StatisticAt( 7 ), "7 turns on (quarter-turn age 1): the cached counts" );
		Assert.IsTrue( StaffStrikeBoard.Grievance( StaffType.Handyman, new[] { new StaffVital( 100, 100 ) }, litter: litter.StatisticAt( 8 ) ), "at turn 8 (age 2) the blocks count again: 3/11 is above 0.2 (tp.exe 0x00509360)" );
	}

	[TestMethod]
	public void TheThoughtReadsItsBlockWithAgeFour()
	{
		var grid = new GuestPathGrid( 12, 12 );
		for ( var x = 0; x < 4; x++ )
			grid.SetPath( x, 0, true );
		var litter = new ParkLitter { Cells = grid.Cells };
		var block = ParkLitter.BlockOf( 2, 0 );
		Assert.AreEqual( 0, block );
		Assert.AreEqual( 0, litter.LitteredInBlock( block, 4, 100 ) );
		litter.Drop( 1, 0, (LitterType)1 );
		Assert.AreEqual( 0, litter.LitteredInBlock( block, 4, 119 ), "(119 >> 2) - (100 >> 2) = 4: still served" );
		Assert.AreEqual( 1, litter.LitteredInBlock( block, 4, 120 ), "age 5 counts again" );
		Assert.AreEqual( 4, litter.PathCellsInBlock( block, 4, 120 ) );
		litter.Restore( litter.Snapshot().ToList() );
		litter.Remove( 1, 0 );
		Assert.AreEqual( 0, litter.LitteredInBlock( block, 4, 121 ), "a load forgets the counts" );
	}

	[TestMethod]
	public void ALoadedBlockCacheCarriesTheConstructorsStampZero()
	{
		// [BIN:TPW-EXE:0x004C58C0 analyser constructor] the stamps start at 0 and the serializer keeps none, so a loaded valid cache is stamp 0
		var grid = new GuestPathGrid( 12, 12 );
		for ( var x = 0; x < 4; x++ )
			grid.SetPath( x, 0, true );
		var litter = new ParkLitter { Cells = grid.Cells };
		var block = ParkLitter.BlockOf( 2, 0 );
		litter.RestoreBlockCaches( new[] { (block, 7, 0L) }, new[] { (block, 9, 0L) } );
		Assert.AreEqual( 7, litter.LitteredInBlock( block, 1, 7 ), "(7 >> 2) - 0 = 1 is not above the age 1: the saved count is served" );
		Assert.AreEqual( 0, litter.LitteredInBlock( block, 1, 8 ), "(8 >> 2) - 0 = 2 is above the age: counted again" );
		Assert.AreEqual( 9, litter.PathCellsInBlock( block, 4, 19 ), "(19 >> 2) - 0 = 4 is not above the age 4: the saved count is served" );
		Assert.AreEqual( 4, litter.PathCellsInBlock( block, 4, 20 ), "from turn 20 the age 4 reader counts again" );
		// a park whose caches were left empty by the import counts at its first read: the same numbers once the turn is past the age
		var imported = new ParkLitter { Cells = grid.Cells };
		Assert.AreEqual( 4, imported.PathCellsInBlock( block, 4, 20 ) );
		Assert.AreEqual( 0, imported.LitteredInBlock( block, 1, 8 ) );
	}

	private static (ParkEconomy Park, StaffMember Handyman) Sweeping()
	{
		var park = EconomyTestData.Park();
		park.OpenPark();
		park.Litter.Drop( 3, 1, (LitterType)2 );
		park.Litter.Drop( 6, 1, (LitterType)4 );
		var handyman = EconomyTestData.HireBest( park, StaffType.Handyman );
		park.Advance( ParkCalendar.TickOfTurn( park.Turn + 1 ) - park.Tick );
		Assert.AreEqual( (3, 1), handyman.LitterCell );
		return (park, handyman);
	}

	[TestMethod]
	public void ASweepSurvivesASaveAndADismissalFreesItsCell()
	{
		var (park, handyman) = Sweeping();
		var json = ParkSaveFile.Serialize( park );
		var restored = ParkSaveFile.Restore( ParkSaveFile.Deserialize( System.Text.Encoding.UTF8.GetBytes( json ) ), park.Settings, park.Catalog );
		var member = restored.Staff.Members.Single( item => item.Id == handyman.Id );
		Assert.AreEqual( (3, 1), member.LitterCell );
		Assert.AreEqual( (member.Id, park.Litter.ClaimTurnAt( 3, 1 )), (restored.Litter.CollectorAt( 3, 1 ), restored.Litter.ClaimTurnAt( 3, 1 )) );
		Assert.AreEqual( json, ParkSaveFile.Serialize( restored ) );

		park.Fire( handyman.Id );
		Assert.AreEqual( (0, 0L), (park.Litter.CollectorAt( 3, 1 ), park.Litter.ClaimTurnAt( 3, 1 )), "the next handyman can take it" );
		Assert.AreEqual( (LitterType)2, park.Litter.TypeAt( 3, 1 ) );
	}

	[TestMethod]
	public void ASaveWhoseLitterIsClaimedByNobodySweepingItIsRefused()
	{
		var (park, handyman) = Sweeping();
		var json = ParkSaveFile.Serialize( park ).Replace( $"\"Collector\": {handyman.Id}", "\"Collector\": 99" );
		Assert.IsTrue( json.Contains( "\"Collector\": 99" ) );
		Assert.ThrowsException<System.IO.InvalidDataException>( () => ParkSaveFile.Deserialize( System.Text.Encoding.UTF8.GetBytes( json ) ) );
		// A sweep whose time has run out cannot hold a claim either: nothing would end it.
		var idle = System.Text.RegularExpressions.Regex.Replace( ParkSaveFile.Serialize( park ), "\"BusyTurns\": \\d+", "\"BusyTurns\": 0" );
		Assert.ThrowsException<System.IO.InvalidDataException>( () => ParkSaveFile.Deserialize( System.Text.Encoding.UTF8.GetBytes( idle ) ) );
	}

	[TestMethod]
	public void ALegacyParkWideLitterCountLoadsAsACleanParkAndIsWrittenAsZero()
	{
		Log = new(); // the dropped count is logged through the static logger
		var park = EconomyTestData.Park();
		var json = ParkSaveFile.Serialize( park );
		var legacy = System.Text.RegularExpressions.Regex.Replace( json, "\"LitterScaled\": 0", "\"LitterScaled\": 450" );
		Assert.AreNotEqual( json, legacy );
		var restored = ParkSaveFile.Restore( ParkSaveFile.Deserialize( System.Text.Encoding.UTF8.GetBytes( legacy ) ), park.Settings, park.Catalog );
		Assert.AreEqual( 0, restored.Litter.Count );
		Assert.AreEqual( json, ParkSaveFile.Serialize( restored ) );
	}

	[TestMethod]
	public void ASweptCellForgetsItsClaim()
	{
		var litter = new ParkLitter();
		litter.Drop( 2, 2, (LitterType)1 );
		litter.Claim( 2, 2, 5, 77 );
		litter.Remove( 2, 2 );
		litter.Drop( 2, 2, (LitterType)1 );
		Assert.AreEqual( (0, 0L), (litter.CollectorAt( 2, 2 ), litter.ClaimTurnAt( 2, 2 )), "a new item starts unclaimed, as a restored save has it" );
	}

	private static (GuestDecision.Outcome, string) Decide( float litter, bool bin, uint draw = 1 )
	{
		var calls = "";
		var input = new GuestDecision.Input( 500, 450, 400, 50, 0, litter, 50, false, draw );
		var outcome = GuestDecision.Decide( input, () => { calls += "B"; return bin; }, () => { calls += "L"; return true; }, ( _, _ ) => { calls += "P"; return true; }, () => { calls += "C"; return true; }, () => { calls += "W"; return true; } );
		return (outcome, calls);
	}

	[TestMethod]
	public void AFullLitterMeterComesBeforeTheLeavingGateAndTheChoice()
	{
		Assert.AreEqual( (GuestDecision.Outcome.Wander, "W"), Decide( 89.99f, bin: true ), "a byte of 89 is not full" );
		Assert.AreEqual( (GuestDecision.Outcome.ToBin, "B"), Decide( 90, bin: true ) );
		Assert.AreEqual( (GuestDecision.Outcome.Wander, "BW"), Decide( 100, bin: false ), "without a bin the litter falls and the tests go on" );
	}

	private static GuestSimulation Row( out GuestPathGrid grid, ParkLitter litter )
	{
		grid = new GuestPathGrid( 16, 16 );
		for ( var x = 0; x < 12; x++ )
			grid.SetPath( x, 5, true );
		var settings = GuestTests.CreateSettings();
		settings.ExitLevelSeconds = 10_000;
		settings.SmallHappinessChange = 0;
		litter.Cells = grid.Cells;
		return new GuestSimulation( grid, settings, 3 ) { ArrivalsEnabled = false, Litter = litter };
	}

	/// <summary>Runs until the guest's litter meter empties; false when it never does.</summary>
	private static bool RunUntilEmpty( GuestSimulation simulation, Guest guest, Action<Guest>? each = null )
	{
		for ( var tick = 0; tick < 120 * 60; tick++ )
		{
			simulation.Tick( Tick );
			each?.Invoke( guest );
			if ( guest.Litter == 0 )
				return true;
		}
		return false;
	}

	[TestMethod]
	public void WithoutABinInReachTheLitterFallsOnTheGuestsCell()
	{
		var litter = new ParkLitter();
		var simulation = Row( out _, litter );
		simulation.Bins = () => new[] { new LitterBin( 9, (11, 6), (11, 5) ) };   // 9 cells away
		var guest = simulation.SpawnInPark( 2, 5 );
		guest.Litter = 95;
		Assert.IsTrue( RunUntilEmpty( simulation, guest ) );
		Assert.AreEqual( 1, litter.Count );
		var cell = litter.Littered().Single();
		Assert.IsTrue( (int)litter.TypeAt( cell.X, cell.Y ) is >= 1 and <= ParkLitter.OrdinaryKinds, "1 + rand % 5" );
		Assert.AreEqual( 5, cell.Y );
		Assert.AreEqual( 0, guest.BinId );
	}

	[TestMethod]
	public void ABinInReachTakesTheLitter()
	{
		var litter = new ParkLitter();
		var simulation = Row( out _, litter );
		var bins = new[] { new LitterBin( 9, (4, 6), (4, 5) ), new LitterBin( 8, (3, 4), (3, 5) ) };
		simulation.Bins = () => bins;
		var binned = new List<int>();
		simulation.LitterBinned += ( _, bin ) => binned.Add( bin );
		var guest = simulation.SpawnInPark( 2, 5 );
		guest.Litter = 90;
		var walked = false;
		Assert.IsTrue( RunUntilEmpty( simulation, guest, g => walked |= g.State == GuestState.ThrowingAwayLitter ) );
		Assert.IsTrue( walked, "state 9" );
		CollectionAssert.AreEqual( new[] { 8 }, binned, "the nearer bin: (3, 4) is 2 away squared, (4, 6) is 5" );
		Assert.AreEqual( 0, litter.Count );
		// [BIN:TPW-EXE:0x004FFF20 state 9 end] the walk ends with the meter 0 and state 6 and leaves +0x1DC: the bin is the guest's target until its next major choice
		Assert.AreEqual( (GuestState.DoingNothing, 8), (guest.State, guest.BinId) );
	}

	[TestMethod]
	public void ACloserBinTestedAfterTheReachableOneThatFailsItsRouteLeavesTheWalkStuck()
	{
		// tp.exe 0x00500DC0: every candidate closer than the best makes its own route request, and a failed one (0x0050F8E0) leaves the walk stuck
		var litter = new ParkLitter();
		var simulation = Row( out _, litter );
		var bins = new[] { new LitterBin( 9, (4, 6), (4, 5) ), new LitterBin( 8, (3, 4), (3, 3) ) };   // the second is closer, its access cell is no path
		simulation.Bins = () => bins;
		var binned = new List<int>();
		simulation.LitterBinned += ( _, bin ) => binned.Add( bin );
		var guest = simulation.SpawnInPark( 2, 5 );
		guest.Litter = 90;
		Assert.IsTrue( RunUntilEmpty( simulation, guest ) );
		Assert.AreEqual( 0, binned.Count, "the stuck walk bins nothing" );
		Assert.AreEqual( 1, litter.Count, "state 9 drops the litter on the cell" );
	}

	[TestMethod]
	public void AClosestBinThatIsTestedFirstDoesNotSpoilALaterReachableOne()
	{
		var litter = new ParkLitter();
		var simulation = Row( out _, litter );
		var bins = new[] { new LitterBin( 8, (3, 4), (3, 3) ), new LitterBin( 9, (4, 6), (4, 5) ) };
		simulation.Bins = () => bins;
		var binned = new List<int>();
		simulation.LitterBinned += ( _, bin ) => binned.Add( bin );
		var guest = simulation.SpawnInPark( 2, 5 );
		guest.Litter = 90;
		Assert.IsTrue( RunUntilEmpty( simulation, guest ) );
		CollectionAssert.AreEqual( new[] { 9 }, binned );
	}

	[TestMethod]
	public void ARouteRequestOfAStrandedGuestRefusesAtOnceAndLeavesTheWalkAlone()
	{
		// tp.exe 0x004FA530: a mark +0x198 still held by 0x004FA770 returns 0 before anything is written; a mark the cell map has stamped past clears and the request goes on
		var simulation = Row( out var grid, new ParkLitter() );
		var guest = simulation.SpawnInPark( 2, 5 );
		var route = typeof( GuestSimulation ).GetMethod( "RouteToCell", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance )!;
		guest.StrandedMark = grid.Cells.StampCounter + 1;   // what the wander stores when it finds no way on
		var before = (guest.Navigation.TargetX, guest.Navigation.TargetY, guest.HasWaypoint);
		Assert.IsFalse( (bool)route.Invoke( simulation, [guest, (6, 5)] )!, "still stranded" );
		Assert.AreEqual( before, (guest.Navigation.TargetX, guest.Navigation.TargetY, guest.HasWaypoint), "the walk is untouched" );
		Assert.AreNotEqual( 0, guest.StrandedMark, "the mark stays" );
		grid.Cells.StampBlockAt( 2, 5 );   // an edit beside the guest stamps its block at the mark
		Assert.IsTrue( (bool)route.Invoke( simulation, [guest, (6, 5)] )!, "a stamped block frees the guest" );
		Assert.AreEqual( 0, guest.StrandedMark );
	}

	[TestMethod]
	public void AWalkToABinThatLosesItsRouteDropsTheLitter()
	{
		var litter = new ParkLitter();
		var simulation = Row( out var grid, litter );
		simulation.Bins = () => new[] { new LitterBin( 9, (4, 4), (4, 5) ) };
		var guest = simulation.SpawnInPark( 2, 5 );
		guest.Litter = 90;
		for ( var tick = 0; tick < 60 * 60 && guest.State != GuestState.ThrowingAwayLitter; tick++ )
			simulation.Tick( Tick );
		Assert.AreEqual( GuestState.ThrowingAwayLitter, guest.State );
		grid.SetPath( 4, 5, false );
		Assert.IsTrue( RunUntilEmpty( simulation, guest ) );
		Assert.AreEqual( 1, litter.Count, "stuck: the litter falls where the guest stands" );
	}

	[TestMethod]
	public void VomitingLeavesVomitOnTheCell()
	{
		var litter = new ParkLitter();
		var simulation = Row( out _, litter );
		var guest = simulation.SpawnInPark( 6, 5 );
		var vomited = false;
		for ( var tick = 0; tick < 120 * 60 && !vomited; tick++ )
		{
			if ( guest.State == GuestState.DoingNothing && guest.Activity == GuestActivity.None )
				guest.Nausea = 100;
			simulation.Tick( Tick );
			vomited = guest.Activity == GuestActivity.Vomiting;
		}
		Assert.IsTrue( vomited );
		Assert.AreEqual( LitterType.Vomit, litter.TypeAt( guest.Cell.X, guest.Cell.Y ) );
		Assert.AreEqual( 0f, guest.Nausea );
	}

	[TestMethod]
	public void AShopVisitFillsTheLitterMeter()
	{
		var rig = new QueueTests.Rig( satisfies: GuestNeeds.Thirst, takesGuests: true, capacity: 1 );
		rig.Ride.LitterEffect = 50;
		var guest = rig.Send();
		var deadline = rig.Simulation.ParkTurn + 400;
		while ( guest.Litter == 0 && rig.Simulation.ParkTurn < deadline )
			rig.Step();
		Assert.AreEqual( 0, guest.RidesTaken, "a facility (Info.WhichUIType 3) counts as no ride, shop or sideshow (0x4FD9B1)" );
		Assert.AreEqual( 50f, guest.Litter, "tp.exe 0x004FE1E0 adds LitterEffect (+0x154), clamped to 0..100" );
	}

	[TestMethod]
	public void EveryThemesBinScriptTakesItsTriggerAsVariableZero()
	{
		var gamePath = Environment.GetEnvironmentVariable( "OPENTPW_GAME_PATH" );
		if ( string.IsNullOrWhiteSpace( gamePath ) || !System.IO.Directory.Exists( gamePath ) )
			Assert.Inconclusive( "Set OPENTPW_GAME_PATH for the original bin scripts." );
		var previous = FileSystem;
		FileSystem = new BaseFileSystem( OriginalParkImportTests.OriginalDataPath() );
		FileSystem.RegisterArchiveHandler<WadArchive>( ".wad" );
		Log = new();
		try
		{
			foreach ( var theme in ObjectCatalog.Themes )
			{
				var bins = ObjectCatalog.Load( theme ).Entries.Where( entry => entry.Settings.GetBool( "UsageInfo.HoldsLitter" ) ).ToList();
				Assert.AreEqual( 1, bins.Count, $"{theme} has one litter bin" );
				var runtime = new OriginalObjectRuntime( bins[0] );
				Assert.IsNotNull( runtime.Script, $"{theme}'s bin has a script" );
				// tp.exe 0x0055A0B0 writes variable 0: the script's only variable, VAR_TRIGGER
				Assert.AreEqual( 0, runtime.Script!.GetVariableIndex( Level.BinTriggerVariable ), theme );
				Assert.AreEqual( 1, runtime.Script.VariableNames.Count, theme );
			}
		}
		finally
		{
			FileSystem = previous;
		}
	}

	private sealed class RecordingPool : IAdvisorParticlePool
	{
		public readonly List<(int Id, (int X, int Y, int Z) At)> Created = new();
		public readonly List<object> Killed = new();
		public object? Create( int effectId, (int X, int Y, int Z) native ) { Created.Add( (effectId, native) ); return Created.Count; }
		public void Move( object handle, (int X, int Y, int Z) native ) { }
		public void Kill( object handle ) => Killed.Add( handle );
	}

	[TestMethod]
	public void AStinkbombStartsEffect50AtItsSpriteAndEndsItWhenSwept()
	{
		// tp.exe 0x4D95BD: effect 0x32 at the sprite's position in original units times 1024; 0x4D9620 ends it with the sprite
		var litter = new ParkLitter();
		var pool = new RecordingPool();
		var effects = new ParkLitterEffects( litter, pool );
		Assert.IsTrue( litter.Drop( 5, 7, LitterType.Stinkbomb ) );
		Assert.AreEqual( 1, pool.Created.Count );
		Assert.AreEqual( 50, pool.Created[0].Id );
		var (dx, dz) = ParkLitter.SpriteOffset( 5, 7 );
		Assert.AreEqual( ((5 * 10 + dx) * 1024, 0, (7 * 10 + dz) * 1024), pool.Created[0].At );
		Assert.IsTrue( dx is >= 1 and <= 9 && dz is >= 1 and <= 9 );
		Assert.AreEqual( 1, effects.Count );
		Assert.IsTrue( litter.Drop( 6, 7, LitterType.Vomit ), "vomit has no particle" );
		Assert.AreEqual( 1, pool.Created.Count );
		Assert.IsFalse( litter.Drop( 5, 7, LitterType.Stinkbomb ), "a cell that holds litter keeps it" );
		Assert.AreEqual( 1, pool.Created.Count );
		Assert.IsTrue( litter.Remove( 6, 7 ) );
		Assert.AreEqual( 0, pool.Killed.Count );
		Assert.IsTrue( litter.Remove( 5, 7 ) );
		CollectionAssert.AreEqual( new object[] { 1 }, pool.Killed );
		Assert.AreEqual( 0, effects.Count );
	}
}

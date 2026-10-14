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
		Assert.IsNull( litter.Statistic, "no map attached: no share" );
		litter.Cells = grid.Cells;
		litter.Drop( 3, 0, (LitterType)1 );
		litter.Drop( 5, 0, (LitterType)1 );
		Assert.AreEqual( new LitterStatistic( 11, 2 ), litter.Statistic );
		Assert.IsFalse( StaffStrikeBoard.Grievance( StaffType.Handyman, new[] { new StaffVital( 100, 100 ) }, litter: litter.Statistic ), "2/11 is not above 0.2" );
		litter.Drop( 7, 0, (LitterType)1 );
		Assert.IsTrue( StaffStrikeBoard.Grievance( StaffType.Handyman, new[] { new StaffVital( 100, 100 ) }, litter: litter.Statistic ), "3/11 is (tp.exe 0x00509360)" );
	}

	private static (GuestDecision.Outcome, string) Decide( float litter, bool bin, uint draw = 1 )
	{
		var calls = "";
		var input = new GuestDecision.Input( 500, 450, 400, 50, 0, litter, 50, false, draw );
		var outcome = GuestDecision.Decide( input, () => { calls += "B"; return bin; }, () => { calls += "L"; return true; }, () => { calls += "C"; return true; }, () => { calls += "W"; return true; } );
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
		Assert.AreEqual( (GuestState.DoingNothing, 0), (guest.State, guest.BinId) );
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
		while ( guest.RidesTaken == 0 && rig.Simulation.ParkTurn < deadline )
			rig.Step();
		Assert.AreEqual( 1, guest.RidesTaken );
		Assert.AreEqual( 50f, guest.Litter, "tp.exe 0x004FE1E0 adds LitterEffect (+0x154), clamped to 0..100" );
	}
}

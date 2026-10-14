using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// The handymen's state machine in a world (tp.exe <c>0x004D73C0</c>, docs/reverse/WANDER-plan.md §4): patrol, the litter finder
/// (<c>0x004C8ED0</c>), the walk to litter and the sweep, the toilet search (<c>0x004D7880</c>) and the clean, timed from arrival.
/// </summary>
[TestClass]
public class HandymanTests
{
	private sealed class Fixture
	{
		public readonly GuestPathGrid Grid = new( 20, 20 );
		public readonly ParkEconomy Park = EconomyTestData.Park();
		public readonly StaffWorldSimulation World;
		public readonly List<ToiletSite> Toilets = new();
		public readonly StaffMember Handyman;

		public Fixture()
		{
			for ( var x = 0; x < 16; x++ )
				Grid.SetPath( x, 5, true );
			for ( var y = 0; y < 11; y++ )
				Grid.SetPath( 10, y, true );
			Park.OpenPark();
			Park.Litter.Cells = Grid.Cells;
			World = new StaffWorldSimulation( Grid, () => Park.Staff.Members, _ => null, (0, 5) ) { Toilets = () => Toilets };
			Park.HandymanWorld = World;
			Handyman = EconomyTestData.HireBest( Park, StaffType.Handyman );
			World.Synchronize();
		}

		public int Work => Park.Settings[StaffType.Handyman].WorkDuration[Handyman.Grade];

		public void Turns( int turns, Action? each = null )
		{
			for ( var turn = 0; turn < turns; turn++ )
			{
				EconomyTestData.AdvanceToTurn( Park, Park.Turn + 1 );
				each?.Invoke();
			}
		}

		public (int X, int Y) Cell => ((int)Handyman.CellX!.Value, (int)Handyman.CellY!.Value);
	}

	[TestMethod]
	public void TheLitterFinderTakesTheNearestUnclaimedCellFirstInScanOrder()
	{
		var litter = new ParkLitter();
		foreach ( var cell in new[] { (12, 10), (8, 10), (10, 12), (10, 7) } )
			litter.Drop( cell.Item1, cell.Item2, (LitterType)1 );
		Assert.AreEqual( (8, 10), StaffWorldSimulation.FindLitter( litter, (10, 10), 3, 2 ), "three at distance 2: x outer, the first wins" );
		litter.Claim( 8, 10, 4, 1 );
		Assert.AreEqual( (10, 12), StaffWorldSimulation.FindLitter( litter, (10, 10), 3, 2 ), "a cell claimed by another is skipped" );
		Assert.AreEqual( (8, 10), StaffWorldSimulation.FindLitter( litter, (10, 10), 4, 2 ), "his own claim is his" );
		Assert.IsNull( StaffWorldSimulation.FindLitter( litter, (10, 10), 3, 1 ), "nothing within one cell" );
		Assert.AreEqual( (10, 12), StaffWorldSimulation.FindLitter( litter, (10, 10), 3, 3 ), "(10, 7) at 3 comes first in the scan, (10, 12) at 2 replaces it" );
	}

	[TestMethod]
	public void AnIdleHandymanPatrolsThePaths()
	{
		var f = new Fixture();
		var cells = new HashSet<(int, int)>();
		f.Turns( 400, () => cells.Add( f.Cell ) );
		Assert.IsTrue( cells.Count > 4, $"he walked over {cells.Count} cells" );
		Assert.IsTrue( cells.All( cell => f.Grid.IsWalkable( cell.Item1, cell.Item2 ) ), "on the paths" );
		Assert.IsTrue( f.Handyman.Energy < 100, "patrolling drains" );
	}

	[TestMethod]
	public void HeWalksToLitterInRangeAndSweepsItForWorkDurationAfterArriving()
	{
		var f = new Fixture();
		f.Park.Litter.Drop( 2, 5, (LitterType)3 );
		long arrived = -1, removed = -1;
		f.Turns( 300, () =>
		{
			if ( arrived < 0 && f.Handyman.Job == HandymanJob.Sweeping )
				arrived = f.Park.Turn;
			if ( removed < 0 && f.Park.Litter.Count == 0 )
				removed = f.Park.Turn;
		} );
		Assert.IsTrue( arrived > 0, "he reached the litter" );
		Assert.AreEqual( arrived + f.Work + 1, removed, "the sweep ends when the turn passes the arrival stamp + WorkDuration" );
	}

	[TestMethod]
	public void LitterOutOfRangeWaitsAndLitterComesBeforeToilets()
	{
		var f = new Fixture();
		f.Park.Litter.Drop( 15, 5, (LitterType)1 );   // 15 cells from the gate: beyond the range
		f.Turns( 3 );
		Assert.AreEqual( 0, f.Park.Litter.CollectorAt( 15, 5 ), "not seen from the gate" );
		Assert.AreEqual( ParkEconomy.PurchaseResult.Ok, f.Park.TryBuild( 1402, out var toilet ) );
		toilet!.Repair = 10;
		f.Toilets.Add( new ToiletSite( toilet.Id, (2, 6), (2, 5) ) );
		f.Park.Litter.Drop( 2, 5, (LitterType)1 );
		long litterGone = -1, cleaned = -1;
		f.Turns( 600, () =>
		{
			if ( litterGone < 0 && f.Park.Litter.TypeAt( 2, 5 ) == LitterType.None )
				litterGone = f.Park.Turn;
			if ( cleaned < 0 && toilet.Repair == 100 )
				cleaned = f.Park.Turn;
		} );
		Assert.IsTrue( litterGone > 0 && cleaned > litterGone, $"litter first ({litterGone}), then the toilet ({cleaned})" );
		Assert.AreEqual( 0, toilet.MechanicId, "the clean frees the toilet" );
	}

	[TestMethod]
	public void AWalkingHandymanResumesFromASaveExactly()
	{
		var f = new Fixture();
		f.Park.Litter.Drop( 2, 5, (LitterType)2 );
		f.Turns( 3 );
		Assert.AreEqual( HandymanJob.ToLitter, f.Handyman.Job );
		var json = ParkSaveFile.Serialize( f.Park );
		var restored = ParkSaveFile.Restore( ParkSaveFile.Deserialize( Encoding.UTF8.GetBytes( json ) ), f.Park.Settings, f.Park.Catalog );
		Assert.AreEqual( json, ParkSaveFile.Serialize( restored ) );
		var world = new StaffWorldSimulation( f.Grid, () => restored.Staff.Members, _ => null, (0, 5) );
		restored.HandymanWorld = world;
		restored.Litter.Cells = f.Grid.Cells;
		for ( var turn = 0; turn < 60; turn++ )
		{
			f.Turns( 1 );
			EconomyTestData.AdvanceToTurn( restored, restored.Turn + 1 );
		}
		Assert.AreEqual( ParkSaveFile.Serialize( f.Park ), ParkSaveFile.Serialize( restored ) );
	}

	[TestMethod]
	public void DismissingAHandymanFreesEveryClaim()
	{
		var f = new Fixture();
		f.Park.Litter.Drop( 2, 5, (LitterType)1 );
		f.Turns( 2 );
		Assert.AreEqual( f.Handyman.Id, f.Park.Litter.CollectorAt( 2, 5 ) );
		f.Park.Fire( f.Handyman.Id );
		Assert.AreEqual( 0, f.Park.Litter.CollectorAt( 2, 5 ) );
	}
}

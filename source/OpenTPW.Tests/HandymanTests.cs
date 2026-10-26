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
			Park.StaffWorld = World;
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
			if ( arrived < 0 && f.Handyman.Job == StaffJob.Sweeping )
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
		Assert.AreEqual( StaffJob.ToLitter, f.Handyman.Job );
		var json = ParkSaveFile.Serialize( f.Park );
		var restored = ParkSaveFile.Restore( ParkSaveFile.Deserialize( Encoding.UTF8.GetBytes( json ) ), f.Park.Settings, f.Park.Catalog );
		Assert.AreEqual( json, ParkSaveFile.Serialize( restored ) );
		var world = new StaffWorldSimulation( f.Grid, () => restored.Staff.Members, _ => null, (0, 5) );
		restored.StaffWorld = world;
		restored.Litter.Cells = f.Grid.Cells;
		for ( var turn = 0; turn < 60; turn++ )
		{
			f.Turns( 1 );
			EconomyTestData.AdvanceToTurn( restored, restored.Turn + 1 );
		}
		Assert.AreEqual( ParkSaveFile.Serialize( f.Park ), ParkSaveFile.Serialize( restored ) );
	}

	[TestMethod]
	public void SellingTheToiletHeWalksToSendsHimSearchingAndKeepsTheParkSaveable()
	{
		var f = new Fixture();
		Assert.AreEqual( ParkEconomy.PurchaseResult.Ok, f.Park.TryBuild( 1402, out var toilet ) );
		toilet!.Repair = 10;
		f.Toilets.Add( new ToiletSite( toilet.Id, (2, 6), (2, 5) ) );
		// his first search comes once his grade's idle time has passed
		for ( var turn = 0; turn < 50 && f.Handyman.ToiletId == 0; turn++ )
			f.Turns( 1 );
		Assert.AreEqual( toilet.Id, f.Handyman.ToiletId );
		f.Park.Sell( toilet.Id );
		Assert.AreEqual( 0, f.Handyman.ToiletId );
		Assert.AreEqual( StaffJob.Idle, f.Handyman.Job );
		var json = ParkSaveFile.Serialize( f.Park );
		ParkSaveFile.Restore( ParkSaveFile.Deserialize( Encoding.UTF8.GetBytes( json ) ), f.Park.Settings, f.Park.Catalog );
		var patrolled = false;
		// the next search comes once his grade's idle time (at most 40 turns here) has passed
		f.Turns( 60, () => patrolled |= f.Handyman.Job == StaffJob.Patrol );
		Assert.IsTrue( patrolled, "the next search finds nothing and patrols" );
	}

	[TestMethod]
	public void AJobOfTheHeadlessDispatchEndsWhenTheWorldTakesHimOver()
	{
		var park = EconomyTestData.Park();
		park.OpenPark();
		var handyman = EconomyTestData.HireBest( park, StaffType.Handyman );
		park.Litter.Drop( 15, 5, (LitterType)1 );
		EconomyTestData.AdvanceToTurn( park, park.Turn + 1 );
		Assert.AreEqual( handyman.Id, park.Litter.CollectorAt( 15, 5 ), "the headless dispatch claims it park-wide" );
		Assert.IsTrue( handyman.BusyTicks > 0 );

		var f = new Fixture();
		var json = ParkSaveFile.Serialize( park );
		var restored = ParkSaveFile.Restore( ParkSaveFile.Deserialize( Encoding.UTF8.GetBytes( json ) ), park.Settings, park.Catalog );
		restored.Litter.Cells = f.Grid.Cells;
		restored.StaffWorld = new StaffWorldSimulation( f.Grid, () => restored.Staff.Members, _ => null, (0, 5) );
		var member = restored.Staff.OfType( StaffType.Handyman ).Single();
		member.CellX = 0;
		member.CellY = 5;
		for ( var turn = 0; turn < 100; turn++ )
			EconomyTestData.AdvanceToTurn( restored, restored.Turn + 1 );
		Assert.AreEqual( 0, member.BusyTicks );
		Assert.IsNull( member.LitterCell );
		Assert.AreEqual( (LitterType)1, restored.Litter.TypeAt( 15, 5 ), "out of his range: the timer did not sweep it" );
		Assert.AreEqual( 0, restored.Litter.CollectorAt( 15, 5 ) );
	}

	[TestMethod]
	public void VoicesWaitForTheAudioAcrossTurns()
	{
		var f = new Fixture();
		f.Turns( 400 );
		var voices = f.World.TakeVoices();
		Assert.IsTrue( voices.Count is > 1 and <= 64, $"{voices.Count} voices kept for one take" );
		Assert.AreEqual( 0, f.World.TakeVoices().Count );
	}

	[TestMethod]
	public void ASavedWalkWithACursorPastTheRouteIsRejected()
	{
		var f = new Fixture();
		f.Park.Litter.Drop( 2, 5, (LitterType)2 );
		f.Turns( 3 );
		var json = ParkSaveFile.Serialize( f.Park );
		var broken = System.Text.RegularExpressions.Regex.Replace( json, "\"Cursor\":\\s*\\d+", "\"Cursor\": 2147483647" );
		Assert.AreNotEqual( json, broken );
		Assert.ThrowsException<System.IO.InvalidDataException>( () =>
			ParkSaveFile.Restore( ParkSaveFile.Deserialize( Encoding.UTF8.GetBytes( broken ) ), f.Park.Settings, f.Park.Catalog ) );
	}

	[TestMethod]
	public void ACalledHandymanCleansAToiletBeyondHisRange()
	{
		var f = new Fixture();
		Assert.AreEqual( ParkEconomy.PurchaseResult.Ok, f.Park.TryBuild( 1402, out var toilet ) );
		toilet!.Repair = 90;
		f.Toilets.Add( new ToiletSite( toilet.Id, (15, 6), (15, 5) ) );
		f.Turns( 20 );
		Assert.AreEqual( 0, toilet.MechanicId, "clean and far: nobody comes" );
		f.Park.RequestMaintenance( toilet.Id, true );
		var answered = false;
		f.Park.EventRaised += item => answered |= item.Kind == ParkEventKind.ToiletCleaned && item.Amount == 1;
		var cleaned = false;
		f.Turns( 600, () => cleaned |= toilet.Repair == 100 && !toilet.MaintenanceRequested );
		Assert.IsTrue( cleaned, "the call brings a handyman at any distance (0x004D7880) and his clean clears it" );
		Assert.IsTrue( answered, "the clean reports that it answered the call (the level reopens the toilet)" );
	}

	[TestMethod]
	public void AHandymanWalksIntoHisPatrolAreaAndStaysThere()
	{
		var f = new Fixture();
		f.Handyman.SetPatrolArea( (12, 7), (8, 0) );
		Assert.AreEqual( (8, 0, 12, 7), f.Handyman.PatrolArea, "the corners are ordered" );
		var inside = 0;
		var outsideAfterEntry = 0;
		f.Turns( 400, () =>
		{
			var (x, y) = f.Cell;
			var isInside = x is >= 8 and <= 12 && y is >= 0 and <= 7;
			if ( isInside )
				inside++;
			else if ( inside > 0 && f.Handyman.Job == StaffJob.Patrol )
				outsideAfterEntry++;
		} );
		Assert.IsTrue( inside > 100, $"he walked in ({inside} turns inside)" );
		Assert.AreEqual( 0, outsideAfterEntry, "and patrolled only inside" );
		f.Handyman.ClearPatrolArea();
		Assert.IsFalse( f.Handyman.HasPatrolArea );
	}

	[TestMethod]
	public void LitterOutsideHisAreaIsStillSweptAndThenHeGoesBackIn()
	{
		// [BIN:TPW-EXE:0x004D7100 handyman job search] the litter finder 0x004C8ED0 asks nothing of the area: 0x00506ED0 has one caller, the wander 0x004F9490
		var f = new Fixture();
		f.Handyman.SetPatrolArea( (10, 0), (12, 3) );
		f.Park.Litter.Drop( 2, 5, (LitterType)3 );
		var swept = false;
		var outsideWhilePatrolling = 0;
		var insideAfterwards = 0;
		f.Turns( 600, () =>
		{
			swept |= f.Handyman.Job == StaffJob.Sweeping;
			var (x, y) = f.Cell;
			var inside = x is >= 10 and <= 12 && y is >= 0 and <= 3;
			if ( swept && f.Park.Litter.Count == 0 )
			{
				if ( inside )
					insideAfterwards++;
				else if ( f.Handyman.Job == StaffJob.Patrol && insideAfterwards > 0 )
					outsideWhilePatrolling++;
			}
		} );
		Assert.IsTrue( swept, "he swept the litter two cells from the path he stands on, outside his area" );
		Assert.AreEqual( 0, f.Park.Litter.Count );
		Assert.IsTrue( insideAfterwards > 100, $"afterwards he walked into his area and stayed there ({insideAfterwards} turns inside)" );
		Assert.AreEqual( 0, outsideWhilePatrolling );
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

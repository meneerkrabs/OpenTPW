using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// Pranks (GUEST-007, docs/reverse/WANDER-plan.md §7): the constructor's prankery flag (tp.exe <c>0x004FB114</c>), the prank step of state 6
/// (<c>0x004FF0F5</c>) and the environment grid that stinkbombs and vomit spread (<c>0x004D8480</c>).
/// </summary>
[TestClass]
public class PrankTests
{
	[TestMethod]
	public void ThePrankeryFlagComesFromTheEighthDrawAndTheThingId()
	{
		Assert.AreEqual( 101, GuestStart.PrankFlag( 4, 5, 7 ), "4 % 100 < 5: a prankster, 100 + 7 % 3" );
		Assert.AreEqual( 0, GuestStart.PrankFlag( 105, 5, 7 ), "105 % 100 = 5 is not below 5" );
		Assert.AreEqual( 100, GuestStart.PrankFlag( 0, 1, 3 ) );
		Assert.AreEqual( 102, GuestStart.PrankFlag( 99, 100, 65_538 ), "the thing id is a 16-bit word: 100 + 2 % 3" );
		Assert.AreEqual( 0, GuestStart.PrankFlag( 0, 0, 3 ), "no pranksters at a likelihood of 0" );
		var types = new List<GuestType> { new( 50, 50, 100 ) };
		var draws = new Queue<uint>( new uint[] { 0, 0, 0, 0, 0, 0, 0, 2 } );
		Assert.AreEqual( 102, GuestStart.FromDraws( types, 100, 10, 15, () => draws.Dequeue(), prankeryLikelihood: 5, thingId: 2 ).PrankFlag );
	}

	private static (GuestDecision.Outcome Outcome, string Calls) Decide( float happiness, int flag, uint draw, int stinkbomb = 25 )
	{
		var calls = "";
		var input = new GuestDecision.Input( 500, 450, 400, happiness, 0, 0, 50, false, draw, flag, stinkbomb );
		var outcome = GuestDecision.Decide( input, () => { calls += "B"; return false; }, () => { calls += "L"; return false; },
			( prank, type ) => { calls += $"P{prank}:{(int)type}"; return true; }, () => { calls += "C"; return true; }, () => { calls += "W"; return true; } );
		return (outcome, calls);
	}

	[TestMethod]
	public void AnUnhappyPranksterPranksAndTheDecisionGoesOn()
	{
		// R = 4: R % 101 = 4, R % 100 = 4, R % 5 = 4, R % 3 = 1 (a wander follows).
		Assert.AreEqual( (GuestDecision.Outcome.Wander, "PStinkbomb:8W"), Decide( 14.99f, 100, 4 ) );
		Assert.AreEqual( (GuestDecision.Outcome.Wander, "PLitter:5W"), Decide( 14.99f, 101, 4 ) );
		Assert.AreEqual( (GuestDecision.Outcome.Wander, "PBalloon:0W"), Decide( 14.99f, 102, 4 ) );
		Assert.AreEqual( (GuestDecision.Outcome.Wander, "W"), Decide( 15f, 101, 4 ), "a happiness byte of 15 does not prank" );
		Assert.AreEqual( (GuestDecision.Outcome.Wander, "W"), Decide( 14f, 100, 4, stinkbomb: 4 ), "4 % 100 is not below a likelihood of 4" );
		Assert.AreEqual( (GuestDecision.Outcome.Wander, "W"), Decide( 14f, 0, 4 ), "no flag" );
		// R = 100: R % 101 = 100, which flag 100 does not exceed; flag 101 does.
		Assert.AreEqual( (GuestDecision.Outcome.Wander, "W"), Decide( 14f, 100, 100 ) );
		Assert.AreEqual( (GuestDecision.Outcome.Wander, "PLitter:1W"), Decide( 14f, 101, 100 ) );
	}

	[TestMethod]
	public void APrankDropsOnTheCellAndCheersThePrankster()
	{
		var grid = new GuestPathGrid( 16, 16 );
		for ( var x = 0; x < 12; x++ )
			grid.SetPath( x, 5, true );
		var settings = GuestTests.CreateSettings();
		settings.ExitLevelSeconds = 10_000;
		settings.SmallHappinessChange = 0;
		settings.StinkbombLikelihood = 100;
		var litter = new ParkLitter();
		var simulation = new GuestSimulation( grid, settings, 5 ) { ArrivalsEnabled = false, Litter = litter };
		var guest = simulation.SpawnInPark( 6, 5 );
		guest.PrankFlag = 100;
		var pranks = new List<GuestDecision.Prank>();
		simulation.Pranked += ( _, prank ) => pranks.Add( prank );
		float before = 0;
		for ( var tick = 0; tick < 300 * 60 && pranks.Count == 0; tick++ )
		{
			if ( guest.State == GuestState.DoingNothing )
				guest.Happiness = 10;
			before = guest.Happiness;
			simulation.Tick( GuestSnapshotTests.Tick );
		}
		CollectionAssert.AreEqual( new[] { GuestDecision.Prank.Stinkbomb }, pranks );
		Assert.AreEqual( before + 1, guest.Happiness, 0.0001f, "a prank adds 1 to happiness" );
		Assert.AreEqual( LitterType.Stinkbomb, litter.TypeAt( guest.Cell.X, guest.Cell.Y ) );
	}

	[TestMethod]
	public void ARegionEffectSpreadsBySquareRadiusAndManhattanDistance()
	{
		var environment = new ParkEnvironment { Effects = new RegionEffect[RegionEffect.Count] };
		environment.Effects[RegionEffect.Stinkbomb] = new RegionEffect( 3, -3, 3, -1, 0, 7 );
		environment.Apply( RegionEffect.Stinkbomb, 1, 10, add: true );
		Assert.AreEqual( ((short)-3, (short)3, (short)-1, (short)0, (short)7), environment.At( 1, 10 ) );
		Assert.AreEqual( ((short)-1, (short)1, (short)0, (short)0, (short)3), environment.At( 0, 10 ), "a distance of 1 divides by 2, truncating toward zero" );
		Assert.AreEqual( ((short)-1, (short)1, (short)0, (short)0, (short)2), environment.At( 3, 10 ) );
		Assert.AreEqual( ((short)0, (short)0, (short)0, (short)0, (short)1), environment.At( 4, 13 ), "the square's corner: a distance of 6" );
		Assert.AreEqual( default, environment.At( 5, 10 ), "outside the square" );
		environment.Apply( RegionEffect.Stinkbomb, 1, 10, add: true );
		environment.Apply( RegionEffect.Stinkbomb, 1, 10, add: false );
		Assert.AreEqual( ((short)-3, (short)3, (short)-1, (short)0, (short)7), environment.At( 1, 10 ), "a removal cancels its add" );
	}

	[TestMethod]
	public void VomitAndStinkbombsSpreadWhileTheyLieAndNeedsReadThem()
	{
		var litter = new ParkLitter();
		litter.Environment.Effects = new RegionEffect[RegionEffect.Count];
		litter.Environment.Effects[RegionEffect.Vomit] = new RegionEffect( 1, -1, 1, -1, 0, 0 );
		litter.Drop( 6, 5, LitterType.Vomit );
		Assert.AreEqual( ((short)-1, (short)1, (short)-1, (short)0, (short)0), litter.Environment.At( 6, 5 ) );
		Assert.AreEqual( default, litter.Environment.At( 7, 5 ), "−1 / 2 truncates to 0 beside it" );
		var copy = new ParkLitter();
		copy.Environment.Effects = litter.Environment.Effects;
		copy.Restore( litter.Snapshot() );
		Assert.AreEqual( litter.Environment.At( 6, 5 ), copy.Environment.At( 6, 5 ), "a load rebuilds the grid from the litter" );
		litter.Remove( 6, 5 );
		Assert.AreEqual( default, litter.Environment.At( 6, 5 ) );

		// A guest's needs phase adds its cell's first three words (NeedsUpdate, ORACLE.md F25).
		var result = NeedsUpdate.Apply( new GuestMeters( 50, 10, 10, 10, 10 ), 100, 4, 8, -1, 1, -1 );
		Assert.AreEqual( new GuestMeters( 49, 10, 9, 10, 11 ), result.Meters );
	}
}

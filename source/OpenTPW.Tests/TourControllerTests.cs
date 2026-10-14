using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>The tour ride's flying-car controller and its TOUR commands (docs/TOURS.md), on synthetic data.</summary>
[TestClass]
public class TourControllerTests
{
	private static Func<uint> Random( uint seed )
	{
		var state = seed;
		return () => state = unchecked( state * 1664525u + 1013904223u );
	}

	private static TourController Create( TourWorld? world = null, uint seed = 1, int rotation = 0, int small = 0 ) =>
		new( TourStation.Default with { RotationDegrees = rotation }, Random( seed ), world ?? new TourWorld(), small );

	private static void Run( TourController controller, int ticks )
	{
		for ( var tick = 0; tick < ticks; tick++ )
			controller.Tick();
	}

	private static bool RunUntil( TourController controller, Func<bool> condition, int limit )
	{
		for ( var tick = 0; tick < limit; tick++ )
		{
			if ( condition() )
				return true;
			controller.Tick();
		}
		return condition();
	}

	[TestMethod]
	public void TheSineTableHasTheTracedValues()
	{
		Assert.AreEqual( 0, TourWorld.Sin( 0 ) );
		Assert.AreEqual( 1024, TourWorld.Sin( 1024 ), "a quarter turn is 1024 (0.5 + 1024, truncated)" );
		Assert.AreEqual( 1024, TourWorld.Cos( 0 ) );
		Assert.AreEqual( -1023, TourWorld.Sin( 3072 ), "0.5 − 1024 = −1023.5 truncates towards zero" );
		Assert.AreEqual( 0, TourWorld.Cos( 1024 ) );
		Assert.AreEqual( TourWorld.Sin( 5 ), TourWorld.Sin( 5 + 4096 ), "angles wrap at 4096" );
		Assert.AreEqual( 1024, TourWorld.Cos( 0x1000 ) );
	}

	[TestMethod]
	public void TheIntegerHelpersTruncateLikeTheCompiler()
	{
		Assert.AreEqual( -1, TourMath.Div1024( -1024 ) );
		Assert.AreEqual( 0, TourMath.Div1024( -1023 ), "towards zero, not floor" );
		Assert.AreEqual( -3, TourMath.Div2( -7 ) );
		Assert.AreEqual( -1, TourMath.Div8( -15 ) );
		Assert.AreEqual( 0, TourMath.Distance32( 31, 31 ) );
		Assert.AreEqual( 5, TourMath.Distance32( 3 * 32, 4 * 32 ) );
		Assert.AreEqual( -20000, TourMath.Lattice( -1 ), "a negative value goes one lattice step lower" );
		Assert.AreEqual( 20000, TourMath.Lattice( 20001 ) );
	}

	[TestMethod]
	public void CreationParksTheFirstCarReadyAtTheStation()
	{
		var world = new TourWorld();
		var controller = Create( world, rotation: 90 );
		Assert.AreEqual( 1, controller.CarCount );
		Assert.AreEqual( TourCar.StateDocked, controller.Cars[0].State );
		Assert.IsTrue( controller.Ready );
		Assert.AreEqual( 1, controller.Command( TourController.CommandReady, 0 ), "TOUR 10: a car is ready" );
		Assert.AreEqual( 0x400 - 1024, controller.Heading, "the heading word is 0x400 minus the placement angle in 4096ths" );
		Assert.AreEqual( controller.StationX, controller.Cars[0].X );
		Assert.AreEqual( controller.Heading, controller.Cars[0].Heading );
		Assert.AreEqual( 5000, controller.ApproachRadius );
		Assert.AreEqual( 2500, Create( small: 1 ).ApproachRadius );
		CollectionAssert.AreEqual( new[] { controller }, world.Controllers.ToArray() );
		Assert.AreEqual( 0, controller.Command( TourController.CommandRunning, 0 ), "an idle ride with one car at the station is not running" );
	}

	[TestMethod]
	public void RidersBoardTheDockedCarAndLeaveInReverseOrderAfterTheFlight()
	{
		var controller = Create();
		controller.Command( TourController.CommandDuration, 20 );
		Assert.AreEqual( 20 * 1000 / 64, controller.Duration, "TOUR 8: the operand × 1000 / 64 ticks" );
		foreach ( var guest in new[] { 11, 12, 13 } )
			Assert.AreEqual( 1, controller.Command( TourController.CommandBoard, guest ), "TOUR 3 boards into the car at the station" );
		Assert.AreEqual( 3, controller.Riders );
		Assert.AreEqual( 0, controller.Command( TourController.CommandUnboard, 0 ), "nobody leaves while the car is loading" );
		Assert.AreEqual( 1, controller.Command( TourController.CommandDepart, 0 ), "TOUR 11 sends the car" );
		Assert.IsFalse( controller.Ready );
		controller.Tick();
		Assert.AreEqual( 0, controller.Command( TourController.CommandBoard, 14 ), "no car stands at the station any more" );
		Assert.AreEqual( 1, controller.Command( TourController.CommandRunning, 0 ) );
		Assert.IsTrue( RunUntil( controller, () => controller.Cars[0].State == TourCar.StateTour, 1000 ), "the car rises and flies the tour" );
		Assert.IsTrue( controller.Cars[0].Y >= 14000, "at least 14000 up" );
		var peak = 0;
		Assert.IsTrue( RunUntil( controller, () => { peak = Math.Max( peak, controller.Cars[0].Y ); return controller.Ready; }, 10_000 ), "the car came back and landed" );
		var car = controller.Cars[0];
		Assert.AreEqual( TourCar.StateDocked, car.State );
		Assert.AreEqual( controller.StationY, car.Y );
		Assert.AreEqual( -3, car.RiderCount, "the riders start leaving" );
		CollectionAssert.AreEqual( new[] { 13, 12, 11 }, new[] { controller.Command( 4, 0 ), controller.Command( 4, 0 ), controller.Command( 4, 0 ) }, "TOUR 4 hands the newest seat out first" );
		Assert.AreEqual( 0, controller.Command( TourController.CommandUnboard, 0 ) );
		Assert.AreEqual( 0, controller.Riders );
		Assert.AreEqual( (3, 3, 1), (controller.Boarded, controller.Released, controller.Departures) );
		Assert.AreEqual( 0, controller.Command( TourController.CommandRunning, 0 ) );
	}

	[TestMethod]
	public void ADockedCarRefusesATenthRiderAndAnUnloadingCarRefusesAll()
	{
		var controller = Create();
		for ( var rider = 1; rider <= TourCar.MaximumRiders; rider++ )
			Assert.IsTrue( controller.Board( rider ) );
		Assert.IsFalse( controller.Board( 99 ), "a full car refuses (the original would overwrite its leaving flag)" );
		Assert.IsFalse( controller.CanBoard );
		controller.Cars[0].RiderCount = -controller.Cars[0].RiderCount;
		Assert.IsFalse( controller.Board( 99 ), "the car is unloading" );
	}

	[TestMethod]
	public void TheDepartureNeedsAnOpenRideOrRidersOrSeveralCars()
	{
		var controller = Create();
		controller.Command( TourController.CommandOpen, 0 );
		Assert.IsFalse( controller.IsOpen );
		Assert.AreEqual( 0, controller.Command( TourController.CommandDepart, 0 ), "closed, empty, one car: nothing to send" );
		Assert.IsTrue( controller.Ready );
		controller.Board( 5 );
		Assert.AreEqual( 1, controller.Command( TourController.CommandDepart, 0 ), "a rider is aboard" );
		Assert.AreEqual( 0, controller.Command( TourController.CommandDepart, 0 ), "no car is ready any more" );
	}

	[TestMethod]
	public void ClosingTheRideSendsTheFlyingCarsHomeAndUnloadsTheStationCar()
	{
		var controller = Create();
		controller.Command( TourController.CommandCars, 2 );
		Assert.IsTrue( RunUntil( controller, () => controller.CarCount == 2, 10 ), "the second car appears" );
		Assert.AreEqual( TourCar.StatePatrol, controller.Cars.First( car => car.State != TourCar.StateDocked && car.IsLive ).State, "a new car appears at its own target outside the flight area and starts circling the station" );
		controller.Board( 7 );
		controller.Board( 8 );
		controller.Command( TourController.CommandOpen, 0 );
		Assert.AreEqual( -2, controller.Cars[0].RiderCount, "a closed ride unloads the car at the station" );
		Assert.AreEqual( 8, controller.Command( TourController.CommandUnboard, 0 ) );
		Assert.AreEqual( 7, controller.Command( TourController.CommandUnboard, 0 ) );
		Assert.IsTrue( RunUntil( controller, () => controller.CarCount == 1, 20_000 ), "the extra car leaves the park" );
		Assert.IsTrue( controller.Cars[0].IsLive );
		Command15AndConditionCases( controller );
	}

	private static void Command15AndConditionCases( TourController controller )
	{
		Assert.AreEqual( 0, controller.Command( TourController.CommandBrokenAtStation, 0 ) );
		controller.Command( TourController.CommandOpen, 1 );
		controller.Command( TourController.CommandCondition, 3 );
		Assert.IsTrue( controller.Cars[0].Broken );
		Assert.AreEqual( 1, controller.Command( TourController.CommandBrokenAtStation, 0 ), "the broken car stands at the station" );
		controller.Command( TourController.CommandCondition, 1 );
		Assert.IsFalse( controller.Cars[0].Broken );
		Assert.AreEqual( 10, controller.Cars[0].Animation, "repaired" );
	}

	[TestMethod]
	public void ASevereBreakdownThrowsTheFlyingRidersOffAndTheStationHandsThemOut()
	{
		var controller = Create();
		controller.Command( TourController.CommandDuration, 40 );
		controller.Board( 1 );
		controller.Board( 2 );
		controller.Command( TourController.CommandDepart, 0 );
		Assert.IsTrue( RunUntil( controller, () => controller.Cars[0].State == TourCar.StateTour, 1000 ) );
		controller.Command( TourController.CommandCondition, 4 );
		Assert.AreEqual( 0, controller.Riders );
		CollectionAssert.AreEquivalent( new[] { 1, 2 }, controller.Ejected );
		Assert.IsTrue( RunUntil( controller, () => controller.Ready, 20_000 ), "the broken car still comes home" );
		var released = new List<int>();
		for ( var guest = controller.Command( TourController.CommandUnboard, 0 ); guest != 0; guest = controller.Command( TourController.CommandUnboard, 0 ) )
			released.Add( guest );
		CollectionAssert.AreEquivalent( new[] { 1, 2 }, released );
		Assert.AreEqual( controller.Boarded, controller.Released );
	}

	[TestMethod]
	public void TheCarsAvoidGroundAboveTheirHeight()
	{
		var world = new TourWorld { CellHeight = ( x, z ) => 100f };
		Assert.AreEqual( 30000, world.GroundClamped( 100000, 100000 ), "100 model units × 300" );
		Assert.AreEqual( 0, world.GroundClamped( 0, 0 ), "outside the flight area" );
		var controller = Create( world );
		controller.Command( TourController.CommandDepart, 0 );
		Run( controller, 1 );
		Assert.AreEqual( 35000, controller.Cars[0].TargetY, "the take-off target clears the ground by 5000" );
		Assert.AreEqual( 100000, world.Clearance( TourWorld.MaximumX + 20000, 0 ), "far outside the area nothing is flyable" );
		Assert.AreEqual( 30000, world.Clearance( controller.StationX + 100, controller.StationZ ), "above a station 30000 is the bar" );
	}

	[TestMethod]
	public void ManyFlightsKeepTheCarsInsideTheirRules()
	{
		var world = new TourWorld { CellHeight = ( x, z ) => (x * 7 + z * 3) % 60 };
		var controller = Create( world, seed: 77 );
		controller.Command( TourController.CommandCars, 3 );
		controller.Command( TourController.CommandDuration, 15 );
		var next = 100;
		var boarded = new HashSet<int>();
		var released = new HashSet<int>();
		for ( var tick = 0; tick < 60_000; tick++ )
		{
			controller.Tick();
			foreach ( var car in controller.Cars.Where( car => car.IsLive ) )
			{
				Assert.IsTrue( Math.Abs( car.X ) < 400_000 && Math.Abs( car.Z ) < 400_000 && car.Y is > -50_000 and < 300_000, $"car at {car.X},{car.Y},{car.Z} in state {car.State}" );
				Assert.IsTrue( car.Heading is >= 0 and < 4096 );
			}
			if ( controller.CanBoard && tick % 20 == 0 && boarded.Count - released.Count < 12 )
			{
				boarded.Add( next );
				Assert.IsTrue( controller.Board( next++ ) );
			}
			if ( tick % 7 == 0 && controller.Command( TourController.CommandUnboard, 0 ) is var rider and not 0 )
				Assert.IsTrue( released.Add( rider ), "a rider is handed out once" );
			if ( controller.Ready && controller.Cars.Any( car => car.State == TourCar.StateDocked && car.RiderCount > 0 ) && tick % 11 == 0 )
				controller.Command( TourController.CommandDepart, 0 );
		}
		Assert.IsTrue( controller.Departures > 5, $"{controller.Departures} departures" );
		Assert.IsTrue( released.Count > 0 );
		Assert.IsTrue( released.IsSubsetOf( boarded ) );
	}

	[TestMethod]
	public void TheStateSurvivesASaveRoundTrip()
	{
		var world = new TourWorld { CellHeight = ( x, z ) => 20f };
		var controller = Create( world, seed: 5 );
		controller.Command( TourController.CommandCars, 2 );
		controller.Command( TourController.CommandDuration, 10 );
		controller.Board( 31 );
		controller.Board( 32 );
		controller.Command( TourController.CommandDepart, 0 );
		Run( controller, 900 );
		controller.Advance( 0.0123 );
		var saved = TourSave.Capture( controller );
		var json = System.Text.Json.JsonSerializer.Serialize( saved );
		var loaded = System.Text.Json.JsonSerializer.Deserialize<TourControllerSaveData>( json )!;
		TourSave.Validate( loaded, guest => guest is 31 or 32, new HashSet<int>() );
		var copyWorld = new TourWorld { CellHeight = world.CellHeight };
		var copy = new TourController( loaded, Random( 12345 ), copyWorld );
		Assert.AreEqual( json, System.Text.Json.JsonSerializer.Serialize( TourSave.Capture( copy ) ) );
		// the restored controller continues exactly as one with the same state and generator would
		var hashA = new StateHasher();
		var hashB = new StateHasher();
		controller.AddCanonicalState( hashA );
		copy.AddCanonicalState( hashB );
		Assert.AreEqual( hashA.Value, hashB.Value );
	}

	[TestMethod]
	public void ValidationRefusesInconsistentSaves()
	{
		var controller = Create();
		controller.Board( 3 );
		var good = TourSave.Capture( controller );
		TourSave.Validate( good, guest => guest == 3, new HashSet<int>() );
		Assert.ThrowsException<System.IO.InvalidDataException>( () => TourSave.Validate( good, guest => false, new HashSet<int>() ), "a rider the park does not hold" );
		Assert.ThrowsException<System.IO.InvalidDataException>( () => TourSave.Validate( good with { Riders = 2 }, guest => true, new HashSet<int>() ), "the rider total" );
		Assert.ThrowsException<System.IO.InvalidDataException>( () => TourSave.Validate( good with { Cars = good.Cars.Select( car => car with { State = 12 } ).ToList() }, guest => true, new HashSet<int>() ), "a bad state" );
		Assert.ThrowsException<System.IO.InvalidDataException>( () => TourSave.Validate( good, guest => true, new HashSet<int> { 3 } ), "a guest listed twice" );
	}

	[TestMethod]
	public void RemovedControllersLeaveTheModuleAndOnlyLiveOnesJoinTheHash()
	{
		var world = new TourWorld();
		var a = Create( world );
		var b = Create( world, seed: 2 );
		Assert.AreEqual( 2, world.Controllers.Count );
		var hash = new StateHasher();
		world.AddCanonicalState( hash );
		var before = hash.Value;
		b.Release();
		Assert.AreEqual( 1, world.Controllers.Count );
		var after = new StateHasher();
		world.AddCanonicalState( after );
		Assert.AreNotEqual( before, after.Value );
		a.Release();
		Assert.AreEqual( 0, world.Controllers.Count );
	}
}

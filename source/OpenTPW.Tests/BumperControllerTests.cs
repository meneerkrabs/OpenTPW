using System;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// Asset-free tests of the bumper cars (docs/TRACKS.md, "Bumper cars"): the templates and the arena, the car lifecycle that the ride scripts drive with BUMP, the session
/// countdown and the hand-over of the riders. The golden values freeze this implementation of the traced rules; they are not captures of the original game.
/// </summary>
[TestClass]
public class BumperControllerTests
{
	private static BumpController Create( int type = -1, uint seed = 99 )
	{
		var state = seed;
		var controller = new BumpController( BumperTemplates.Get( type ), () => { state = unchecked( state * 1664525u + 1013904223u ); return state >> 8; }, new BumpWorld() );
		controller.SetStation( 5 * 0xC00, 5 * 0xC00, TrackHeading.PosY );
		return controller;
	}

	private static void Run( BumpController controller, int ticks )
	{
		for ( var tick = 0; tick < ticks; tick++ )
			controller.Tick();
	}

	/// <summary>The script's loading: a guest per car through BUMP 1 and BUMP 12, then the duration and the start.</summary>
	private static void LoadAndStart( BumpController controller, int cars, int seconds )
	{
		controller.Command( 7, 0 );
		for ( var car = 0; car < cars; car++ )
			Assert.AreEqual( 1, controller.Command( 4, 0 ), "BUMP 4 makes a car" );
		for ( var guest = 0; guest < cars; guest++ )
		{
			controller.Command( 1, 100 + guest );
			Assert.AreEqual( 1, controller.Command( 12, 0 ), "BUMP 12 loads the guest" );
		}
		controller.Command( 13, seconds );
		controller.Command( 3, 0 );
	}

	[TestMethod]
	public void RidesOfTheBumperTypesGetABumperTemplate()
	{
		foreach ( var type in new[] { -1, -6, -11, -14 } )
		{
			var template = BumperTemplates.ForRide( 0, type );
			Assert.IsNotNull( template, $"type {type}" );
			Assert.IsTrue( template!.IsBumper );
			Assert.IsFalse( template.IsWater || template.IsKart );
		}
		Assert.IsNull( BumperTemplates.ForRide( 0, 0 ), "a ride without a bumper type has no controller" );
		Assert.IsNull( BumperTemplates.ForRide( 0, -4 ), "a kart type needs the kart track type" );
		Assert.IsNull( BumperTemplates.ForRide( 1, -1 ), "a bumper type on a track ride is not a bumper ride" );
	}

	[TestMethod]
	public void ArenaIsOneCircleAndAnEightBuoyRing()
	{
		var radii = new[] { (-1, 0x1200, 0xC00), (-6, 0x1600, 0x1000), (-11, 0x1100, 0xB00), (-14, 0x1600, 0x1000) };
		foreach ( var (type, arena, ring) in radii )
		{
			var controller = Create( type );
			Assert.IsFalse( controller.HasTrack );
			Assert.IsTrue( controller.IsTrackValid, "a bumper ride needs no circuit" );
			Assert.AreEqual( 1, controller.Objects.Count, $"type {type}" );
			Assert.AreEqual( arena, controller.Objects[0].Extent );
			Assert.IsTrue( controller.Objects[0].IsFull );
			Assert.AreEqual( 8, controller.Buoys.Count );
			Assert.AreEqual( 0, controller.Sections.Count );
			Assert.AreEqual( (5 * 0xC00, 5 * 0xC00 + ring), (controller.Buoys[0].X, controller.Buoys[0].Y), "the ring starts straight ahead" );
			foreach ( var buoy in controller.Buoys )
			{
				var distance = BumpMath.Distance( buoy.X - 5 * 0xC00, buoy.Y - 5 * 0xC00 );
				Assert.IsTrue( Math.Abs( distance - ring ) < ring / 40, $"type {type} buoy {buoy.Index} lies {distance} from the centre, expected about {ring}" );
				Assert.IsFalse( buoy.IsFinish || buoy.IsSkipped );
			}
		}
	}

	[TestMethod]
	public void CarsDropIntoTheArenaWithoutOverlap()
	{
		var controller = Create();
		controller.Command( 7, 0 );
		for ( var car = 0; car < 8; car++ )
			Assert.AreEqual( 1, controller.Command( 4, 0 ) );
		Assert.AreEqual( 0, controller.Command( 4, 0 ), "a ninth car is refused" );
		Assert.AreEqual( 8, controller.Command( 11, 0 ) );
		foreach ( var car in controller.Vehicles )
		{
			Assert.IsTrue( BumpMath.Distance( car.X - 5 * 0xC00, car.Y - 5 * 0xC00 ) <= 0x1200, "inside the arena" );
			Assert.AreEqual( 0, car.Flags & BumpVehicle.Steerable, "an empty car does not carry" );
			Assert.AreEqual( -1, car.WallHits, "bumper cars never retarget for wall contacts" );
		}
		Assert.IsTrue( controller.Vehicles.Select( car => (car.X, car.Y) ).Distinct().Count() == 8 );
	}

	[TestMethod]
	public void SessionRunsForTheDurationAndHandsEveryRiderBack()
	{
		var controller = Create();
		LoadAndStart( controller, 4, 20 );
		Assert.AreEqual( BumpController.PhaseRunning, controller.Phase );
		Assert.AreEqual( 4, controller.RiderCount );
		Run( controller, 20 * 30 - 2 );
		Assert.AreEqual( 0, controller.Command( 2, 0 ), "nobody leaves before the duration is over" );
		Assert.AreEqual( BumpController.PhaseRunning, controller.Phase );
		Run( controller, 4 );
		Assert.AreEqual( BumpController.PhaseLoading, controller.Phase, "the ride loads again once the last rider left" );
		Assert.AreEqual( 0, controller.RiderCount );
		var released = Enumerable.Range( 0, 6 ).Select( _ => controller.Command( 2, 0 ) ).ToArray();
		CollectionAssert.AreEquivalent( new[] { 100, 101, 102, 103 }, released.Where( guest => guest != 0 ).ToArray(), "BUMP 2 returns each guest once" );
		Assert.AreEqual( 0, released[4], "and then 0" );
		Assert.AreEqual( 4, controller.VehicleCount, "the cars stay for the next session" );
		Assert.IsTrue( controller.Vehicles.All( car => car.Riders.Count == 0 && (car.Flags & (BumpVehicle.ToUnload | BumpVehicle.LapMode)) == 0 ) );
	}

	[TestMethod]
	public void BrokenRidePausesTheCountdown()
	{
		var controller = Create();
		LoadAndStart( controller, 2, 10 );
		Run( controller, 100 );
		controller.Command( 8, 1 );
		Run( controller, 1000 );
		Assert.AreEqual( 2, controller.RiderCount, "a broken ride holds its riders" );
		controller.Command( 8, 0 );
		Run( controller, 300 );
		Assert.AreEqual( 0, controller.RiderCount, "repaired, the session runs out" );
		Assert.AreEqual( 2, controller.Leaving.Count );
	}

	[TestMethod]
	public void ClosingEndsTheSessionAtOnce()
	{
		var controller = Create();
		LoadAndStart( controller, 3, 60 );
		Run( controller, 30 );
		controller.Command( 6, 0 );
		Assert.AreEqual( BumpController.PhaseClosed, controller.Phase );
		Run( controller, 2 );
		Assert.AreEqual( 0, controller.RiderCount, "the cars hand their riders over on their next step" );
		Assert.AreEqual( 3, controller.Leaving.Count );
		Assert.AreEqual( BumpController.PhaseLoading, controller.Phase, "the unload resets the phase, as traced (the script closes again)" );
	}

	[TestMethod]
	public void PendingGuestsGoToTheLeavingListWhenTheRideCloses()
	{
		var controller = Create();
		controller.Command( 7, 0 );
		controller.Command( 4, 0 );
		controller.Command( 1, 7 );
		controller.Command( 6, 0 );
		Assert.AreEqual( 7, controller.Command( 2, 0 ) );
	}

	[TestMethod]
	public void UnloadingAnEmptyCarKeepsTheLoadedOnes()
	{
		var controller = Create();
		controller.Command( 7, 0 );
		controller.Command( 4, 0 );
		controller.Command( 4, 0 );
		controller.Command( 1, 55 );
		controller.Command( 12, 0 );
		Assert.AreEqual( 1, controller.Command( 16, 0 ) );
		Assert.AreEqual( 1, controller.VehicleCount );
		Assert.AreEqual( 55, controller.Vehicles[0].Riders.Single(), "BUMP 16 removes the empty car, not the loaded one" );
	}

	[TestMethod]
	public void CarsWanderBetweenBuoysAndStayInsideTheArena()
	{
		var controller = Create();
		LoadAndStart( controller, 6, 120 );
		var moved = controller.Vehicles.ToDictionary( car => car.Slot, car => (car.X, car.Y) );
		var maxDistance = 0;
		for ( var tick = 0; tick < 1500; tick++ )
		{
			controller.Tick();
			foreach ( var car in controller.Vehicles )
				maxDistance = Math.Max( maxDistance, BumpMath.Distance( car.X - 5 * 0xC00, car.Y - 5 * 0xC00 ) );
		}
		Assert.IsTrue( controller.Vehicles.All( car => (car.X, car.Y) != moved[car.Slot] ), "every loaded car moved" );
		Assert.IsTrue( maxDistance <= 0x1200 + 0x200, $"cars stay within the arena wall (farthest {maxDistance})" );
	}

	[TestMethod]
	public void CarsChaseEachOtherAndGiveUp()
	{
		var chased = 0;
		var sawTarget = false;
		for ( uint seed = 1; seed <= 12; seed++ )
		{
			var controller = Create( -6, seed );
			LoadAndStart( controller, 6, 60 );
			for ( var tick = 0; tick < 1200; tick++ )
			{
				controller.Tick();
				foreach ( var car in controller.Vehicles.Where( car => (car.Flags & BumpVehicle.Chasing) != 0 ) )
				{
					chased++;
					var target = controller.Vehicles.SingleOrDefault( other => other.Slot == car.ChaseTarget );
					sawTarget |= target != null && target != car;
					Assert.IsTrue( car.BuoyCountdown <= 0x5A, "a chase lasts at most 90 ticks" );
				}
			}
		}
		Assert.IsTrue( chased > 0, "cars chase now and then" );
		Assert.IsTrue( sawTarget, "a chasing car names another car" );
	}

	[TestMethod]
	public void CrashCodeFollowsTheType()
	{
		foreach ( var (type, expected) in new[] { (-6, BumpEffects.SoundCrunch), (-11, BumpEffects.SoundCrunch), (-14, BumpEffects.SoundCrunch), (-1, -1) } )
		{
			var controller = Create( type );
			var sounds = 0;
			controller.Effect += effect => { if ( effect.Kind == BumpEffectKind.GlobalSound && effect.Id == BumpEffects.SoundCrunch ) sounds++; };
			LoadAndStart( controller, 8, 120 );
			// two cars head-on at speed: the impulse is above the 0x82 threshold
			var a = controller.Vehicles[0];
			var b = controller.Vehicles[1];
			a.X = 5 * 0xC00; a.Y = 5 * 0xC00; a.VX = 600; a.VY = 0; a.VX0 = 600;
			b.X = 5 * 0xC00 + 500; b.Y = 5 * 0xC00; b.VX = -600; b.VY = 0; b.VX0 = -600;
			Run( controller, 3 );
			if ( expected < 0 )
				Assert.AreEqual( 0, sounds, "the jungle bumper makes no crash sound" );
			else
				Assert.IsTrue( sounds > 0, $"type {type} crunches" );
		}
	}

	[TestMethod]
	public void FirstCarryingCarHoldsTheEngineUntilItsRiderLeaves()
	{
		var controller = Create();
		var starts = 0;
		var halts = 0;
		controller.Effect += effect => { if ( effect.Kind == BumpEffectKind.EngineStart ) starts++; if ( effect.Kind == BumpEffectKind.EngineHalt ) halts++; };
		LoadAndStart( controller, 3, 5 );
		Assert.AreEqual( 1, starts, "one engine voice for the first car" );
		Run( controller, 200 );
		Assert.AreEqual( 1, halts, "the voice stops when the car hands its rider over" );
		Assert.IsFalse( controller.Vehicles.Any( car => car.Engine ) );
	}

	[TestMethod]
	public void SaveRoundTripKeepsASessionRunning()
	{
		var original = Create( -14 );
		LoadAndStart( original, 4, 30 );
		Run( original, 300 );
		var data = TrackSave.CaptureController( original, 7 );
		var restored = Create( -14 );
		restored.Restore( data );
		var hashA = new StateHasher();
		var hashB = new StateHasher();
		original.AddCanonicalState( hashA );
		restored.AddCanonicalState( hashB );
		Assert.AreEqual( hashA.ToString(), hashB.ToString(), "the restored controller hashes like the original" );
		// the generators are not part of the state, so the continuations differ in detail; the restored session still runs out and hands everybody back
		Run( restored, 600 );
		Assert.AreEqual( 0, restored.RiderCount );
		Assert.AreEqual( 4, restored.Leaving.Count );
		Assert.AreEqual( BumpController.PhaseLoading, restored.Phase );
	}

	[TestMethod]
	public void SameSeedGivesTheSameSession()
	{
		static int[] Trace( uint seed )
		{
			var controller = Create( -1, seed );
			LoadAndStart( controller, 4, 30 );
			Run( controller, 400 );
			return controller.Vehicles.SelectMany( car => new[] { car.X, car.Y, car.Heading } ).ToArray();
		}
		CollectionAssert.AreEqual( Trace( 5 ), Trace( 5 ) );
		CollectionAssert.AreNotEqual( Trace( 5 ), Trace( 6 ) );
	}
}

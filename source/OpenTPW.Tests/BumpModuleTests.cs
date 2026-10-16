using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// The bump module is shared by every ride of a park (tp.exe 0x00546C80): one frame counter for the 32-tick gates, one vehicle pool stepped and collided in pool
/// order across the rides, one collision-object pool every search walks, and the pool limits (256 vehicles, 2048 objects, 1024 buoys) counted over all rides.
/// Synthetic controllers.
/// </summary>
[TestClass]
public class BumpModuleTests
{
	private static Func<uint> Random( uint seed )
	{
		var state = seed;
		return () => { state = unchecked( state * 1664525u + 1013904223u ); return state >> 8; };
	}

	private static BumpController Bumper( BumpWorld world, int x, int y, uint seed = 7, int type = -6 )
	{
		var controller = new BumpController( BumperTemplates.Get( type ), Random( seed ), world );
		controller.SetStation( x, y, TrackHeading.PosY );
		return controller;
	}

	private static void Start( BumpController controller, int cars, int guestBase )
	{
		controller.Command( 7, 0 );
		for ( var car = 0; car < cars; car++ )
			Assert.AreEqual( 1, controller.Command( 4, 0 ) );
		for ( var guest = 0; guest < cars; guest++ )
		{
			controller.Command( 1, guestBase + guest );
			controller.Command( 12, 0 );
		}
		controller.Command( 13, 600 );
		controller.Command( 3, 0 );
	}

	[TestMethod]
	public void EveryControllerReadsTheModuleFrameCounter()
	{
		var world = new BumpWorld();
		var first = Bumper( world, 5 * 0xC00, 5 * 0xC00 );
		for ( var tick = 0; tick < 13; tick++ )
			first.Tick();
		var second = Bumper( world, 20 * 0xC00, 5 * 0xC00 );
		second.Tick();
		Assert.AreEqual( 14, world.FrameCount, "one counter for the module (0x877B5C)" );
		Assert.AreEqual( 14, first.TickCount );
		Assert.AreEqual( 1, second.TickCount, "a controller counts the ticks it ran" );
		// one tick of either controller is one module tick: both advance together
		first.Tick();
		Assert.AreEqual( (15L, 2L), (world.FrameCount, second.TickCount) );
		// the clock: only the first controller drives it, so two rides advancing in one world step tick the module once
		first.Advance( BumpController.TickSeconds );
		second.Advance( BumpController.TickSeconds );
		Assert.AreEqual( 16, world.FrameCount );
	}

	[TestMethod]
	public void VehiclesShareOnePoolAndItsOrderAcrossTheRides()
	{
		var world = new BumpWorld();
		var a = Bumper( world, 5 * 0xC00, 5 * 0xC00 );
		var b = Bumper( world, 20 * 0xC00, 5 * 0xC00 );
		a.Command( 7, 0 );
		b.Command( 7, 0 );
		Assert.AreEqual( 1, a.Command( 4, 0 ) );
		Assert.AreEqual( 1, b.Command( 4, 0 ) );
		Assert.AreEqual( 1, a.Command( 4, 0 ) );
		CollectionAssert.AreEqual( new[] { 0, 2 }, a.Vehicles.Select( car => car.Slot ).ToArray(), "first free slot of the shared pool (0x549DB0)" );
		CollectionAssert.AreEqual( new[] { 1 }, b.Vehicles.Select( car => car.Slot ).ToArray() );
		Assert.AreSame( b, world.VehicleAt( 1 )!.Controller );
		b.Retire( b.Vehicles[0] );
		Assert.AreEqual( 1, a.Command( 4, 0 ) );
		Assert.AreEqual( 1, a.Vehicles.Last( car => car.Slot == 1 ).Slot, "a freed slot goes to the next vehicle, whichever ride makes it" );
	}

	[TestMethod]
	public void ThePoolOf256VehiclesCountsEveryRide()
	{
		var world = new BumpWorld();
		var controllers = Enumerable.Range( 0, 5 ).Select( index => Bumper( world, (5 + index * 20) * 0xC00, 5 * 0xC00, type: -6 ) ).ToList();
		var made = 0;
		foreach ( var controller in controllers )
		{
			controller.Command( 7, 0 );
			while ( controller.Command( 4, 0 ) == 1 )
				made++;
		}
		var perRide = controllers[0].VehicleCount;
		Assert.IsTrue( perRide * controllers.Count > BumpWorld.VehiclePool || made == perRide * controllers.Count, "the rides' own limits" );
		Assert.IsTrue( made <= BumpWorld.VehiclePool, $"{made} vehicles in a pool of 256" );
		Assert.AreEqual( Enumerable.Range( 0, made ).ToHashSet().Count, controllers.SelectMany( controller => controller.Vehicles ).Select( car => car.Slot ).Distinct().Count() );
	}

	[TestMethod]
	public void CarsOfOverlappingArenasPushEachOtherAndFindTheOtherRidesObject()
	{
		// two bumper rides on one spot: in one module the second ride's cars collide with the first ride's and its cars' object search finds the first ride's arena (pool slot 0)
		var world = new BumpWorld();
		var a = Bumper( world, 5 * 0xC00, 5 * 0xC00, seed: 3 );
		var b = Bumper( world, 5 * 0xC00, 5 * 0xC00, seed: 4 );
		Assert.AreEqual( 0, a.Objects[0].ModuleSlot );
		Assert.AreEqual( 1, b.Objects[0].ModuleSlot );
		Start( a, 3, 100 );
		Start( b, 3, 200 );
		Assert.IsTrue( b.Vehicles.All( car => car.ObjectOwner == a ), "the module search finds the first ride's arena first (0x549070 walks the pool)" );
		var crossed = 0;
		for ( var tick = 0; tick < 400; tick++ )
		{
			a.Tick();
			foreach ( var car in a.Vehicles )
			{
				if ( b.Vehicles.Any( other => BumpMath.Distance( other.X - car.X, other.Y - car.Y ) < other.Radius + car.Radius + 64 ) )
					crossed++;
			}
		}
		Assert.IsTrue( crossed > 0, "the rides' cars meet" );
		// apart, the same rides never touch each other's cars
		var other = new BumpWorld();
		var d = Bumper( other, 5 * 0xC00, 5 * 0xC00, seed: 4 );
		Start( d, 3, 200 );
		Assert.IsTrue( d.Vehicles.All( car => car.ObjectOwner == null ), "a ride alone in its module only finds its own objects" );
	}

	[TestMethod]
	public void ASharedModuleSurvivesASaveRoundTrip()
	{
		// one world generator per park (its state is the park's, restored with it)
		var generators = new List<uint[]>();
		BumpWorld Build( out BumpController a, out BumpController b )
		{
			var world = new BumpWorld();
			var state = new uint[] { 3 };
			generators.Add( state );
			uint Next() { state[0] = unchecked( state[0] * 1664525u + 1013904223u ); return state[0] >> 8; }
			a = new BumpController( BumperTemplates.Get( -6 ), Next, world );
			a.SetStation( 5 * 0xC00, 5 * 0xC00, TrackHeading.PosY );
			b = new BumpController( BumperTemplates.Get( -6 ), Next, world );
			b.SetStation( 5 * 0xC00, 5 * 0xC00, TrackHeading.PosY );
			a.AttractionId = 11;
			b.AttractionId = 12;
			return world;
		}
		var world = Build( out var a, out var b );
		Start( a, 3, 100 );
		Start( b, 3, 200 );
		for ( var tick = 0; tick < 77; tick++ )
			a.Tick();
		a.Retire( a.Vehicles[0] );
		var savedA = TrackSave.CaptureController( a, 11 );
		var savedB = TrackSave.CaptureController( b, 12 );
		var json = System.Text.Json.JsonSerializer.Serialize( new[] { savedA, savedB } );
		var loaded = System.Text.Json.JsonSerializer.Deserialize<BumpControllerSaveData[]>( json )!;
		var copy = Build( out var a2, out var b2 );
		a2.Restore( loaded[0] );
		b2.Restore( loaded[1] );
		copy.CosmeticState = world.CosmeticState;
		generators[1][0] = generators[0][0];
		copy.FinishRestore( world.FrameCount, world.TickRemainder, new (BumpController, IReadOnlyList<int>?)[] { (a2, loaded[0].ObjectSlots), (b2, loaded[1].ObjectSlots) } );
		string Digest( BumpWorld module )
		{
			var hash = new StateHasher();
			module.AddCanonicalState( hash );
			return hash.Value.ToString();
		}
		Assert.AreEqual( Digest( world ), Digest( copy ), "pool slots, foreign objects and the frame counter come back" );
		for ( var tick = 0; tick < 200; tick++ )
		{
			a.Tick();
			a2.Tick();
		}
		Assert.AreEqual( Digest( world ), Digest( copy ), "and the two continue alike" );
		// a save from before the shared module: no frame counter, no object slots, no foreign object: the first controller's clock is taken
		var legacy = Build( out var a3, out var b3 );
		a3.Restore( loaded[0] with { ObjectSlots = null } );
		b3.Restore( loaded[1] with { ObjectSlots = null, Vehicles = loaded[1].Vehicles.Select( car => car with { ObjectAttraction = -1, ObjectIndex = 0 } ).ToList() } );
		legacy.FinishRestore( null, null, new (BumpController, IReadOnlyList<int>?)[] { (a3, null), (b3, null) } );
		Assert.AreEqual( loaded[0].TickCount, legacy.FrameCount );
	}

	[TestMethod]
	public void TheModuleClockOutlivesItsLastRideInTheSave()
	{
		var world = new BumpWorld();
		var ride = Bumper( world, 5 * 0xC00, 5 * 0xC00 );
		for ( var tick = 0; tick < 5; tick++ )
			ride.Tick();
		world.Remove( ride );
		var saved = TrackSave.Capture( new ParkCellMap( 8, 8 ), world, Array.Empty<(int, BumpController)>() );
		Assert.IsNotNull( saved, "an empty module with a running clock still writes its part" );
		Assert.AreEqual( 5L, saved!.FrameCount );
		Assert.IsNull( TrackSave.Capture( new ParkCellMap( 8, 8 ), new BumpWorld(), Array.Empty<(int, BumpController)>() ), "a park that never had a ride writes none" );
	}

	private sealed class FlatGrid : IParkGrid
	{
		public int Width => 40;
		public int Height => 40;
		public System.Numerics.Vector2 Origin => default;
		public OriginalPlacementResult CheckTerrain( int x, int y ) => OriginalPlacementResult.Allowed;
		public float GetCellHeight( int x, int y ) => 0;
	}

	/// <summary>The eight-tile water loop of the circuit tests, walked on its own cell map: station (10, 10), corners (14, 10), (14, 12), (8, 12), (8, 10).</summary>
	private static (TrackStation Station, TrackCircuit Circuit) WaterLoop()
	{
		var builder = new TrackBuilder( new ParkCellMap( 40, 40 ), TrackFamily.Water, () => null, new FlatGrid() );
		var station = TrackStation.FromPlacement( 10, 10, 0 );
		builder.PlaceStation( station );
		var writer = new TrackLineWriter( builder );
		writer.Commit( (14, 10), (14, 12) );
		writer.Commit( (14, 12), (8, 12) );
		writer.Commit( (8, 12), (8, 10) );
		return (station, TrackCircuitWalker.WalkAndRefresh( builder, station ));
	}

	private static BumpController Water( BumpWorld world, TrackStation station )
	{
		var controller = new BumpController( BumperTemplates.Get( -5 ), () => 0u, world );
		var centre = TrackStation.Centre( station.First );
		controller.SetStation( centre.X, centre.Y, station.Heading );
		return controller;
	}

	private static string Digest( BumpWorld module )
	{
		var hash = new StateHasher();
		module.AddCanonicalState( hash );
		return hash.Value.ToString();
	}

	[TestMethod]
	public void ARestoreKeepsTheSavedPoolVerdictsNearTheLimit()
	{
		// 128 arenas of 8 buoys fill the module's 1024 buoys: the 129th arena and a water ride built after them are refused, though the water circuit walks valid
		var world = new BumpWorld();
		var arenas = Enumerable.Range( 0, 129 ).Select( index => Bumper( world, (5 + index % 16) * 0xC00, (5 + index / 16) * 0xC00 ) ).ToList();
		for ( var index = 0; index < arenas.Count; index++ )
			arenas[index].AttractionId = index + 1;
		Assert.AreEqual( BumpWorld.BuoyPool, world.BuoyCount );
		Assert.AreEqual( 0, arenas[128].Objects.Count, "the 129th arena does not fit the buoy pool" );
		var (station, circuit) = WaterLoop();
		Assert.IsTrue( circuit.IsValid );
		var water = Water( world, station );
		water.AttractionId = 500;
		water.Rebuild( circuit );
		Assert.IsFalse( water.IsTrackValid, "refused by the shared pools" );
		Assert.AreEqual( 0, water.Objects.Count );
		// ten arenas go: the refused rides would fit now, but they are not rebuilt
		foreach ( var gone in arenas.Take( 10 ) )
			world.Remove( gone );
		var live = arenas.Skip( 10 ).Append( water ).ToList();
		var saved = TrackSave.Capture( new ParkCellMap( 8, 8 ), world, live.Select( ride => (ride.AttractionId, ride) ) )!;
		saved = System.Text.Json.JsonSerializer.Deserialize<TrackWorldSaveData>( System.Text.Json.JsonSerializer.Serialize( saved ) )!;

		// the restore builds the rides afresh in their order: now everything fits, so replaying the pools' checks would give other verdicts
		var copy = new BumpWorld();
		var restored = arenas.Skip( 10 ).Select( ( arena, index ) => Bumper( copy, arena.StationX, arena.StationY ) ).ToList();
		for ( var index = 0; index < restored.Count; index++ )
			restored[index].AttractionId = index + 11;
		var (station2, circuit2) = WaterLoop();
		var water2 = Water( copy, station2 );
		water2.AttractionId = 500;
		Assert.AreEqual( 1, restored[^1].Objects.Count, "the 129th arena fits a fresh module" );
		var probe = new BumpWorld();
		var replay = Water( probe, WaterLoop().Station );
		replay.Rebuild( WaterLoop().Circuit );
		Assert.IsTrue( replay.IsTrackValid, "and the water circuit fits too: the verdict depends on the build order" );

		var rides = restored.Append( water2 ).ToList();
		copy.SharedSaveRestore = true;
		try
		{
			foreach ( var (ride, entry) in rides.Zip( saved.Controllers ) )
			{
				Assert.AreEqual( ride.AttractionId, entry.AttractionId );
				if ( ride.Template.IsBumper )
					ride.RestoreArena( entry.ObjectSlots is { Count: > 0 }, entry.BuoyCount );
				else
					ride.RestoreCircuit( circuit2, entry.IsTrackValid, entry.ObjectSlots!.Count, entry.BuoyCount );
				ride.Restore( entry );
			}
		}
		finally
		{
			copy.SharedSaveRestore = false;
		}
		TrackSave.RestoreModule( copy, saved, rides.Zip( saved.Controllers, ( ride, entry ) => (ride, (IReadOnlyList<int>?)entry.ObjectSlots) ).ToList() );
		Assert.AreEqual( 0, restored[^1].Objects.Count, "the refused arena stays refused" );
		Assert.IsFalse( water2.IsTrackValid, "the refused water ride keeps its saved verdict" );
		Assert.AreEqual( 0, water2.Objects.Count );
		CollectionAssert.AreEqual( water.Sections.Select( section => (section.Kind, section.X, section.Y) ).ToArray(), water2.Sections.Select( section => (section.Kind, section.X, section.Y) ).ToArray() );
		Assert.AreEqual( world.BuoyCount, copy.BuoyCount );
		Assert.AreEqual( Digest( world ), Digest( copy ), "the restored module is the saved one: slots, verdicts and objects" );
	}

	[TestMethod]
	public void ASharedModuleRestoreRefusesAVehicleSlotAnotherRideHolds()
	{
		var world = new BumpWorld();
		var a = Bumper( world, 5 * 0xC00, 5 * 0xC00 );
		var b = Bumper( world, 20 * 0xC00, 5 * 0xC00 );
		Start( a, 2, 100 );
		var saved = TrackSave.CaptureController( a, 11 );
		var copy = new BumpWorld();
		var a2 = Bumper( copy, 5 * 0xC00, 5 * 0xC00 );
		var b2 = Bumper( copy, 20 * 0xC00, 5 * 0xC00 );
		Start( b2, 1, 200 );
		Assert.AreEqual( 0, b2.Vehicles[0].Slot );
		copy.SharedSaveRestore = true;
		try
		{
			Assert.ThrowsException<System.IO.InvalidDataException>( () => a2.Restore( saved ), "a save of the shared module names its slots exactly" );
		}
		finally
		{
			copy.SharedSaveRestore = false;
		}
		// a save from before the shared pool takes the first free slot instead
		a2.Restore( saved );
		Assert.IsTrue( a2.Vehicles.All( car => car.Slot != 0 ) );
		_ = b;
	}

	[TestMethod]
	public void ALegacyChaseFollowsItsCarToItsNewSlot()
	{
		var world = new BumpWorld();
		var a = Bumper( world, 5 * 0xC00, 5 * 0xC00, seed: 3 );
		var b = Bumper( world, 20 * 0xC00, 5 * 0xC00, seed: 4 );
		Start( a, 2, 100 );
		Start( b, 2, 200 );
		var saved = TrackSave.CaptureController( b, 12 );
		// a save from before the shared pool: b's cars at its own slots 0 and 1, one chasing the other
		var legacy = saved with { Vehicles = saved.Vehicles.Select( ( car, index ) => car with { Slot = index, ChaseTarget = index == 0 ? 1 : -1 } ).ToList() };
		var world2 = new BumpWorld();
		var a2 = Bumper( world2, 5 * 0xC00, 5 * 0xC00, seed: 3 );
		var b2 = Bumper( world2, 20 * 0xC00, 5 * 0xC00, seed: 4 );
		Start( a2, 2, 100 );
		b2.Restore( legacy );
		var chaser = b2.Vehicles.First( car => car.ChaseTarget >= 0 );
		Assert.AreSame( b2, world2.VehicleAt( chaser.ChaseTarget )!.Controller, "the chase still names the ride's own car" );
		Assert.AreNotEqual( 1, chaser.ChaseTarget, "slot 1 belongs to the other ride now" );
	}
}

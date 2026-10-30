using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// The vehicles, riders and lists of an original save's TrackRides block (TRACK-032, loader <c>0x543560</c> chunks 5, 7, 8 and 9). No save of the corpus holds a kart, water or bumper ride, so the chunks are built from
/// the saver's shapes (<c>0x5428E0</c>: the 0xAC-byte record, the fallback place of its object, the key of its waypoint, one chunk per rider and per listed guest) and the loader's reads are checked word by word.
/// </summary>
[TestClass]
public class OriginalTrackVehiclesTests
{
	private const uint Handle = 0xFFFFFB03;
	private const uint Foreign = 0xFFFFF905;

	private static byte[] Words( params uint[] words ) => words.SelectMany( BitConverter.GetBytes ).ToArray();

	private static byte[] Chunk( uint type, params uint[] payload ) => Words( type, 0, (uint)(12 + payload.Length * 4) ).Concat( Words( payload ) ).ToArray();

	/// <summary>The 43 words of a record the saver wrote for a vehicle of <paramref name="bump"/>, with the words the loader replaces holding junk (pointers and handles of the saved game).</summary>
	internal static uint[] Record( BumpController bump, BumpVehicle vehicle )
	{
		var w = new uint[43];
		var water = bump.IsWater;
		w[0] = (uint)vehicle.Flags; w[1] = (uint)vehicle.Variant; w[2] = 0xAAAA; w[3] = 0xBBBB; w[4] = (uint)vehicle.Animation; w[5] = (uint)vehicle.EffectRequest; w[6] = (uint)vehicle.EffectLatch;
		w[8] = vehicle.Engine && !bump.Template.IsKart ? 0x77u : 0; w[9] = w[10] = 0xFFFFFFFF; w[11] = vehicle.Smoke ? 5u : 0xFFFFFFFF; w[12] = 0x12345678;
		w[13] = (uint)vehicle.X; w[14] = (uint)vehicle.Y; w[15] = (uint)vehicle.VX; w[16] = (uint)vehicle.VY; w[17] = (uint)vehicle.VX0; w[18] = (uint)vehicle.VY0; w[19] = (uint)vehicle.Speed;
		w[20] = (uint)vehicle.Heading; w[21] = (uint)(water ? 0x1A : vehicle.VisualHeading); w[22] = (uint)vehicle.Spin; w[23] = (uint)(water ? vehicle.Roll : vehicle.YawRate); w[24] = (uint)vehicle.YawDelta;
		w[25] = (uint)vehicle.Radius; w[26] = (uint)vehicle.AccelBonus; w[27] = (uint)vehicle.TargetX; w[28] = (uint)vehicle.TargetY; w[29] = (uint)vehicle.JitterX; w[30] = (uint)vehicle.JitterY;
		w[31] = 0x9999; w[32] = (uint)vehicle.ChaseTarget; w[34] = (uint)vehicle.Laps; w[35] = (uint)vehicle.BuoyCountdown; w[36] = (uint)vehicle.Age;
		w[37] = 0x55; w[38] = 0xDEAD0001; w[39] = 0xDEAD0002; w[40] = BitConverter.SingleToUInt32Bits( vehicle.Height ); w[41] = (uint)vehicle.WallHits; w[42] = (uint)vehicle.WallWindow;
		return w;
	}

	/// <summary>One ride's chunks as the saver writes them: the ride chunk, its vehicles with their riders (chunk 9 from the list's head), the leaving and the joining lists and the reference-waypoint request.</summary>
	internal static SaveTrackRideBlock Block( BumpController bump, uint handle, uint? phase = null, uint? first = null, uint? count = null, bool sections = true )
	{
		var chunks = new List<byte>();
		chunks.AddRange( Words( 2, 0, 32 ).Concat( System.Text.Encoding.ASCII.GetBytes( "15:25:02Mar 17 2000\0" ) ) );
		chunks.AddRange( Chunk( 3, handle, (uint)bump.StationX, (uint)bump.StationY, (uint)bump.Orientation, 12, (uint)bump.Performance, first ?? (uint)bump.ModelBase, count ?? (uint)bump.ModelCount, unchecked( (uint)bump.Laps ), phase ?? (uint)bump.Phase ) );
		if ( sections )
			foreach ( var section in bump.Sections )
				chunks.AddRange( Chunk( 4, handle, (uint)section.KindWord, (uint)section.X, (uint)section.Y ) );
		foreach ( var vehicle in bump.Vehicles )
		{
			var record = Record( bump, vehicle );
			var home = BumpController.ObjectOf( vehicle );
			var target = vehicle.TargetBuoy >= 0 ? bump.Buoys[vehicle.TargetBuoy] : null;
			chunks.AddRange( Chunk( 5, new[] { handle }.Concat( record ).Concat( new[] { (uint)(home?.X ?? 0), (uint)(home?.Y ?? 0), target == null ? 0 : handle, (uint)(target?.X ?? 0), (uint)(target?.Y ?? 0) } ).ToArray() ) );
			for ( var index = 0; index < vehicle.Riders.Count; index++ )
				chunks.AddRange( Chunk( 9, handle, (uint)vehicle.Riders[index], (uint)(vehicle.SeatOf( index ) + 1) ) );
		}
		foreach ( var guest in bump.Leaving )
			chunks.AddRange( Chunk( 7, handle, (uint)guest ) );
		foreach ( var guest in bump.Pending )
			chunks.AddRange( Chunk( 8, handle, (uint)guest ) );
		chunks.AddRange( Chunk( 6, handle ) );
		var body = chunks.ToArray();
		return SaveTrackRideBlock.ParseContainer( Words( 1, 0, (uint)(12 + body.Length) ).Concat( body ).ToArray() );
	}

	/// <summary>The eight-tile loop of the circuit tests laid in the family of a controller type, with a controller of that type on it (a module of its own).</summary>
	private sealed class Loop
	{
		public readonly BumpController Controller;
		public readonly TrackCircuit Circuit;
		public readonly TrackStation Station = TrackStation.FromPlacement( 10, 9, 0 );
		public int Draws;
		private uint rng = 4242;

		public Loop( int type, BumpWorld? world = null )
		{
			var family = BumperTemplates.Get( type ).IsKart ? TrackFamily.Kart : TrackFamily.Water;
			var builder = new TrackBuilder( new ParkCellMap( 40, 40 ), family, () => null, new BumpControllerTests.TestGrid() );
			builder.PlaceStation( Station );
			var writer = new TrackLineWriter( builder );
			writer.Commit( (14, 10), (14, 12) );
			writer.Commit( (14, 12), (8, 12) );
			writer.Commit( (8, 12), (8, 10) );
			Circuit = TrackCircuitWalker.WalkAndRefresh( builder, Station );
			Controller = new BumpController( BumperTemplates.Get( type ), () => { Draws++; rng = unchecked( rng * 1664525u + 1013904223u ); return rng >> 8; }, world );
			var centre = Station.Reference;
			Controller.SetStation( centre.X, centre.Y, Station.Heading );
			Controller.Rebuild( Circuit );
		}
	}

	private static Loop Ride( int type, out uint handle )
	{
		handle = unchecked( (uint)(type << 8 | 3) );
		return new Loop( type );
	}

	/// <summary>The twin that takes the chunks: the same loop, nothing launched.</summary>
	private static string? Import( BumpController target, SaveTrackRideBlock block, out BumpController.OriginalVehicleImport? result, Func<CollisionObject, bool>? visible = null )
	{
		var read = OriginalTrackVehicles.Read( block );
		Assert.IsNull( read.Problem );
		return target.ImportOriginalVehicles( read.Rides.Single(), visible ?? ( _ => true ), out result );
	}

	private static BumpVehicle Launch( BumpController bump, params int[] riders )
	{
		foreach ( var guest in riders.Reverse() )
			Assert.IsTrue( bump.AddGuest( guest ) );
		Assert.IsTrue( bump.Launch() );
		return bump.Vehicles[^1];
	}

	[TestMethod]
	public void ARaceInProgressComesBackWithItsVehiclesRidersListsAndRawPhase()
	{
		var source = Ride( -4, out var handle ).Controller;
		source.Open();
		Launch( source, 41, 42, 43 );
		Launch( source, 44 );
		Launch( source );
		source.Start();
		source.Pending.AddRange( new[] { 51, 52 } );
		source.Leaving.AddRange( new[] { 61, 62, 63 } );
		for ( var tick = 0; tick < 40; tick++ )
			source.Module.Tick();
		Assert.AreEqual( BumpController.PhaseRunning, source.Phase );

		var target = Ride( -4, out _ ).Controller;
		var block = Block( source, handle );
		Assert.IsNull( Import( target, block, out var result ) );

		Assert.AreEqual( new BumpController.OriginalVehicleImport( 3, 4, 2, 3 ), result );
		Assert.AreEqual( BumpController.PhaseRunning, target.Phase, "a racing kart ride loads racing: the loader reads the phase raw" );
		Assert.AreEqual( 3, target.VehicleCount );
		Assert.AreEqual( 4, target.RiderCount );
		CollectionAssert.AreEqual( new[] { 0, 1, 2 }, target.Vehicles.Select( vehicle => vehicle.Slot ).ToArray(), "the first free pool records, in the file's order" );
		for ( var index = 0; index < 3; index++ )
		{
			var (a, b) = (source.Vehicles[index], target.Vehicles[index]);
			Assert.AreEqual( (a.Flags, a.Variant, a.X, a.Y, a.VX, a.VY, a.VX0, a.VY0, a.Speed, a.Heading, a.VisualHeading, a.YawRate, a.YawDelta), (b.Flags, b.Variant, b.X, b.Y, b.VX, b.VY, b.VX0, b.VY0, b.Speed, b.Heading, b.VisualHeading, b.YawRate, b.YawDelta), $"vehicle {index} motion" );
			Assert.AreEqual( (a.Radius, a.AccelBonus, a.TargetX, a.TargetY, a.JitterX, a.JitterY, a.TargetBuoy, a.Laps, a.BuoyCountdown, a.Age, a.ObjectIndex, a.WallHits, a.WallWindow, a.Animation, a.EffectRequest, a.EffectLatch),
				(b.Radius, b.AccelBonus, b.TargetX, b.TargetY, b.JitterX, b.JitterY, b.TargetBuoy, b.Laps, b.BuoyCountdown, b.Age, b.ObjectIndex, b.WallHits, b.WallWindow, b.Animation, b.EffectRequest, b.EffectLatch), $"vehicle {index} aim" );
		}
		// every list is pushed on its head node by node, so it comes back the other way round; the riders keep their seat numbers
		CollectionAssert.AreEqual( new[] { 43, 42, 41 }, target.Vehicles[0].Riders.ToArray() );
		CollectionAssert.AreEqual( new[] { 3, 2, 1 }, target.Vehicles[0].SeatNumbers );
		CollectionAssert.AreEqual( new[] { 0, 1, 2 }, new[] { 41, 42, 43 }.Select( rider => target.Vehicles[0].SeatOf( target.Vehicles[0].Riders.IndexOf( rider ) ) ).ToArray(), "rider 41 still sits in seat 0" );
		CollectionAssert.AreEqual( new[] { 44 }, target.Vehicles[1].Riders.ToArray() );
		CollectionAssert.AreEqual( new[] { 52, 51 }, target.Pending.ToArray() );
		CollectionAssert.AreEqual( new[] { 63, 62, 61 }, target.Leaving.ToArray() );

		// a second save and load brings every list back in the order it was first written
		var again = Ride( -4, out _ ).Controller;
		Assert.IsNull( Import( again, Block( target, handle ), out _ ) );
		CollectionAssert.AreEqual( new[] { 41, 42, 43 }, again.Vehicles[0].Riders.ToArray() );
		CollectionAssert.AreEqual( new[] { 51, 52 }, again.Pending.ToArray() );
		CollectionAssert.AreEqual( new[] { 61, 62, 63 }, again.Leaving.ToArray() );
		CollectionAssert.AreEqual( new[] { 1, 2, 3 }, again.Vehicles[0].SeatNumbers );
	}

	[TestMethod]
	public void TheLoaderDrawsNothingFromTheWorldGeneratorAndHangsNoHead()
	{
		var source = Ride( -4, out var handle ).Controller;
		source.Open();
		Launch( source, 41, 42 );
		source.Pending.Add( 51 );
		var twin = Ride( -4, out _ );
		var before = twin.Draws;
		Assert.IsNull( Import( twin.Controller, Block( source, handle ), out var result ) );
		Assert.AreEqual( 3, result!.Riders + result.Joining, "the riders and the joining guest came in" );
		Assert.AreEqual( before, twin.Draws, "the vehicle load and the rider load draw nothing: no launch set-up (0x54A040) and no head hang (0x44B410) run in the loader" );
	}

	[TestMethod]
	public void EveryRecordWordLandsInTheFieldTheOriginalReadsItFrom()
	{
		var bump = Ride( -1, out _ ).Controller;
		bump.Open();
		var car = Launch( bump, 41 );
		// a bumper car: the arena has one object and the ring its buoys
		var words = Record( bump, car );
		words[0] = 0x4000001 | 0x4 | 0x4000 | 0x400000; words[1] = 1; words[4] = 5; words[5] = 2; words[6] = 3; words[8] = 0x77; words[11] = 9;
		words[13] = 100; words[14] = 200; words[15] = unchecked( (uint)-7 ); words[16] = 8; words[17] = 9; words[18] = unchecked( (uint)-10 ); words[19] = 12; words[20] = 300; words[21] = 301;
		words[23] = unchecked( (uint)-5 ); words[24] = 6; words[25] = 896; words[26] = 3; words[27] = 4000; words[28] = 5000; words[29] = unchecked( (uint)-12 ); words[30] = 13;
		words[32] = 4; words[34] = 3; words[35] = 77; words[36] = 91; words[40] = BitConverter.SingleToUInt32Bits( float.NaN ); words[41] = 2; words[42] = 17;
		var fallback = bump.Objects[0];
		var chunk = Chunk( 5, new[] { Handle }.Concat( words ).Concat( new[] { (uint)fallback.X, (uint)fallback.Y, 0u, 0u, 0u } ).ToArray() );
		var block = SaveTrackRideBlock.ParseBody( Chunk( 3, Handle, (uint)bump.StationX, (uint)bump.StationY, (uint)bump.Orientation, 12, 50, (uint)bump.ModelBase, (uint)bump.ModelCount, 150, 2 ).Concat( chunk ).ToArray() );
		var target = Ride( -1, out _ ).Controller;
		var refusal = Import( target, block, out _ );
		Assert.IsNull( refusal, refusal );
		var v = target.Vehicles.Single();
		Assert.AreEqual( 0x4000001 | 0x4 | 0x4000 | 0x400000, v.Flags );
		Assert.AreEqual( (1, 5, 2, 3), (v.Variant, v.Animation, v.EffectRequest, v.EffectLatch) );
		Assert.IsTrue( v.Engine && v.Smoke, "+0x20 and +0x2C hold a voice and an emitter" );
		Assert.AreEqual( (100, 200, -7, 8, 9, -10, 12, 300, 301), (v.X, v.Y, v.VX, v.VY, v.VX0, v.VY0, v.Speed, v.Heading, v.VisualHeading) );
		Assert.AreEqual( (-5, 0, 896, 3, 4000, 5000, -12, 13), (v.YawRate, v.YawDelta, v.Radius, v.AccelBonus, v.TargetX, v.TargetY, v.JitterX, v.JitterY), "+0x60 is only written by a kart's step, a bumper car's stays 0" );
		Assert.AreEqual( 0, v.Roll, "+0x5C is a boat's roll and a car's yaw rate: one word" );
		Assert.AreEqual( 4, v.ChaseTarget );
		Assert.AreEqual( (3, 91, 2, 17), (v.Laps, v.Age, v.WallHits, v.WallWindow) );
		Assert.AreEqual( 0, v.BuoyCountdown, "a chasing car (flag 4) loads with its countdown zeroed (0x543E4F)" );
		Assert.AreEqual( 0f, v.Height, "a NaN height is cleared (0x67B410)" );
		Assert.AreEqual( -1, v.TargetBuoy, "a key without an owner means none" );
		Assert.AreEqual( 0, v.ObjectIndex );
		Assert.AreEqual( 0, v.Riders.Count, "the loader zeroes the rider list; chunks 9 fill it" );
		Assert.AreEqual( BumpController.PhaseRunning, target.Phase );
	}

	[TestMethod]
	public void ABoatTakesItsSpinAndRollFromTheWordsAndNeverItsDrawnHeading()
	{
		var source = Ride( -5, out var handle ).Controller;
		source.Open();
		var boat = Launch( source, 41 );
		boat.Spin = -300;
		boat.Roll = 9;
		var target = Ride( -5, out _ ).Controller;
		Assert.IsNull( Import( target, Block( source, handle ), out _ ) );
		var loaded = target.Vehicles.Single();
		Assert.AreEqual( (-300, 9, 0), (loaded.Spin, loaded.Roll, loaded.VisualHeading), "+0x54 only ever follows the roll and nothing reads it" );
		Assert.IsTrue( loaded.Engine, "every boat holds its engine voice" );
		Assert.AreEqual( BumpController.PhaseRunning, target.Phase, "a water ride keeps its running phase" );
	}

	[TestMethod]
	public void TheWaypointKeyIsResolvedAgainstTheRidesChainAndAMissingNodeRefusesTheRide()
	{
		var source = Ride( -4, out var handle ).Controller;
		source.Open();
		var car = Launch( source, 41 );
		var buoy = source.Buoys.First( item => !item.IsSkipped && !item.IsFinish );
		car.TargetBuoy = buoy.Index;
		var target = Ride( -4, out _ ).Controller;
		Assert.IsNull( Import( target, Block( source, handle ), out _ ) );
		Assert.AreEqual( buoy.Index, target.Vehicles.Single().TargetBuoy, "the first node with the saved corner" );

		// the same record with a waypoint of another ride, and with a corner no node has
		var foreign = Block( source, handle ).Chunks;
		string Refusal( uint owner, int x, int y )
		{
			var rewritten = foreign.SelectMany( chunk => chunk.RawRecord is { } record
				? Chunk( 5, new[] { handle }.Concat( Enumerable.Range( 0, 43 ).Select( index => BitConverter.ToUInt32( record.Bytes[(index * 4)..] ) ) ).Concat( new[] { (uint)record.FallbackTileCoordinate0, (uint)record.FallbackTileCoordinate1, owner, (uint)x, (uint)y } ).ToArray() )
				: Words( chunk.Type, chunk.HeaderWord, chunk.Size ).Concat( chunk.Payload.ToArray() ).ToArray() ).ToArray();
			var again = Ride( -4, out _ ).Controller;
			var refusal = Import( again, SaveTrackRideBlock.ParseBody( rewritten ), out var result );
			Assert.IsNull( result );
			Assert.AreEqual( 0, again.VehicleCount, "nothing changes when a ride is refused" );
			return refusal!;
		}
		StringAssert.Contains( Refusal( Foreign, buoy.X, buoy.Y ), "not of its own ride" );
		StringAssert.Contains( Refusal( handle, buoy.X + 1, buoy.Y ), "is not on the walked circuit's chain" );
	}

	[TestMethod]
	public void AVehicleFindsItsObjectFromItsPlaceOrFromTheFallbackPlaceAndNeedsOne()
	{
		var source = Ride( -4, out var handle ).Controller;
		source.Open();
		var car = Launch( source, 41 );
		var home = BumpController.ObjectOf( car )!;
		var block = Block( source, handle );
		// the vehicle stands outside every object but its fallback place is in one
		car.X = int.MaxValue / 4;
		car.Y = int.MaxValue / 4;
		var target = Ride( -4, out _ ).Controller;
		Assert.IsNull( Import( target, Block( source, handle ), out _ ), "the fallback place holds the vehicle's object" );
		Assert.AreEqual( home.Index, target.Vehicles.Single().ObjectIndex );

		// with no object at either place, or only the objects of rides the loader has not made yet, the ride is refused: the original reads the object unchecked
		var refused = Ride( -4, out _ ).Controller;
		StringAssert.Contains( Import( refused, block, out _, visible: _ => false ) ?? "", "stands in no collision object" );
		Assert.AreEqual( 0, refused.VehicleCount );
	}

	[TestMethod]
	public void AJoiningGuestOfAClosedRideIsDroppedAndTheLeavingListNeedsNoPhase()
	{
		var source = Ride( -4, out var handle ).Controller;
		source.Open();
		source.Pending.Add( 51 );
		source.Leaving.AddRange( new[] { 61, 62 } );
		var block = Block( source, handle, phase: 0 );
		var read = OriginalTrackVehicles.Read( block );
		var ride = read.Rides.Single();
		Assert.AreEqual( (1, 0), (ride.DroppedJoining, ride.Joining.Count), "0x54AA80 answers 0 for a ride in phase 0" );
		var target = Ride( -4, out _ ).Controller;
		Assert.IsNull( target.ImportOriginalVehicles( ride, _ => true, out var result ) );
		Assert.AreEqual( new BumpController.OriginalVehicleImport( 0, 0, 0, 2 ), result );
		Assert.AreEqual( BumpController.PhaseClosed, target.Phase );
		CollectionAssert.AreEqual( new[] { 62, 61 }, target.Leaving.ToArray() );
	}

	[TestMethod]
	public void ARideOverOrRacingWithoutVehiclesKeepsItsRawPhaseAndTheModelWordsAreTheChunks()
	{
		var source = Ride( -4, out var handle ).Controller;
		var target = Ride( -4, out _ ).Controller;
		Assert.IsNull( Import( target, Block( source, handle, phase: 3, first: 1, count: 2 ), out _ ) );
		Assert.AreEqual( (BumpController.PhaseOver, 1, 2), (target.Phase, target.ModelBase, target.ModelCount) );
		Assert.IsFalse( target.HasTemplateModelWords );
		// a count of 0 divides by zero at the next launch, and a phase beyond 3 is no phase
		StringAssert.Contains( Import( Ride( -4, out _ ).Controller, Block( source, handle, count: 0 ), out _ ) ?? "", "no range of vehicle models" );
		StringAssert.Contains( Import( Ride( -4, out _ ).Controller, Block( source, handle, phase: 9 ), out _ ) ?? "", "none of the ride's phases" );
	}

	[TestMethod]
	public void AGuestListedTwiceOrAVehicleOutsideItsModelsOrAnUnknownEffectRefusesTheRide()
	{
		var source = Ride( -4, out var handle ).Controller;
		source.Open();
		Launch( source, 41 );
		source.Leaving.Add( 41 );
		StringAssert.Contains( Import( Ride( -4, out _ ).Controller, Block( source, handle ), out _ ) ?? "", "named twice" );
		source.Leaving.Clear();
		var car = source.Vehicles[0];
		car.Variant = 7;
		StringAssert.Contains( Import( Ride( -4, out _ ).Controller, Block( source, handle ), out _ ) ?? "", "outside the ride's models" );
		car.Variant = 0;
		car.EffectLatch = 9;
		StringAssert.Contains( Import( Ride( -4, out _ ).Controller, Block( source, handle ), out _ ) ?? "", "effect words" );
	}

	[TestMethod]
	public void TheReaderTakesAVehicleForEveryChunkWhoseHandleNamesARideAndHangsRidersOnTheLast()
	{
		var chunks = new List<byte>();
		var handle = Handle;
		uint[] Vehicle( uint who ) => new[] { who }.Concat( Enumerable.Range( 0, 43 ).Select( index => index == 0 ? 1u : 0u ) ).Concat( new uint[] { 0, 0, 0, 0, 0 } ).ToArray();
		chunks.AddRange( Chunk( 3, handle, 1, 2, 3, 4, 5, 6, 7, 8, 2 ) );
		chunks.AddRange( Chunk( 5, Vehicle( handle ) ) );
		chunks.AddRange( Chunk( 9, handle, 41, 1 ) );
		chunks.AddRange( Chunk( 9, handle, 42, 2 ) );
		chunks.AddRange( Chunk( 5, Vehicle( handle ) ) );
		chunks.AddRange( Chunk( 9, handle, 43, 1 ) );
		chunks.AddRange( Chunk( 7, handle, 61 ) );
		chunks.AddRange( Chunk( 8, handle, 51 ) );
		chunks.AddRange( Chunk( 5, Vehicle( Foreign ) ) );
		var read = OriginalTrackVehicles.Read( SaveTrackRideBlock.ParseBody( chunks.ToArray() ) );
		Assert.IsNull( read.Problem );
		Assert.AreEqual( 1, read.OrphanChunks, "a chunk whose handle names no ride is read by nobody" );
		var ride = read.Rides.Single();
		Assert.AreEqual( 2, ride.Vehicles.Count );
		CollectionAssert.AreEqual( new[] { (41, 1), (42, 2) }, ride.Vehicles[0].Seats );
		CollectionAssert.AreEqual( new[] { (43, 1) }, ride.Vehicles[1].Seats );
		CollectionAssert.AreEqual( new[] { 61 }, ride.Leaving );
		CollectionAssert.AreEqual( new[] { 51 }, ride.Joining );
	}

	[TestMethod]
	public void TheVehiclesOfSeveralRidesTakeTheFirstFreeRecordsOfTheSharedPoolInTheFilesOrder()
	{
		var first = Ride( -4, out var firstHandle ).Controller;
		var second = Ride( -5, out var secondHandle ).Controller;
		first.Open();
		second.Open();
		Launch( first, 41 );
		Launch( first, 42 );
		Launch( second, 43 );
		// one block with the two rides' chunks, one after the other, as the saver writes them
		var body = Block( first, firstHandle ).Chunks.Concat( Block( second, secondHandle ).Chunks.Where( chunk => chunk.Type != 2 ) )
			.SelectMany( chunk => Words( chunk.Type, chunk.HeaderWord, chunk.Size ).Concat( chunk.Payload.ToArray() ) ).ToArray();
		var read = OriginalTrackVehicles.Read( SaveTrackRideBlock.ParseBody( body ) );
		Assert.IsNull( read.Problem );
		CollectionAssert.AreEqual( new[] { 0, 1 }, read.Rides.Select( ride => ride.Ordinal ).ToArray() );

		var world = new BumpWorld();
		var (a, b) = (new Loop( -4, world ).Controller, new Loop( -5, world ).Controller);
		Assert.IsNull( a.ImportOriginalVehicles( read.Rides[0], _ => true, out _ ) );
		Assert.IsNull( b.ImportOriginalVehicles( read.Rides[1], _ => true, out _ ) );
		CollectionAssert.AreEqual( new[] { 0, 1 }, a.Vehicles.Select( vehicle => vehicle.Slot ).ToArray() );
		CollectionAssert.AreEqual( new[] { 2 }, b.Vehicles.Select( vehicle => vehicle.Slot ).ToArray() );
		Assert.AreEqual( 3, world.LiveVehicles.Count() );
	}

	[TestMethod]
	public void ARiderChunkBeforeAnyVehicleAndChunksOfARideInTwoRunsAreNotTheSaversShapes()
	{
		var chunks = new List<byte>();
		chunks.AddRange( Chunk( 3, Handle, 1, 2, 3, 4, 5, 6, 7, 8, 2 ) );
		chunks.AddRange( Chunk( 9, Handle, 41, 1 ) );
		Assert.IsNotNull( OriginalTrackVehicles.Read( SaveTrackRideBlock.ParseBody( chunks.ToArray() ) ).Rides.Single().Refusal );
		chunks.AddRange( Chunk( 3, Foreign, 1, 2, 3, 4, 5, 6, 7, 8, 2 ) );
		chunks.AddRange( Chunk( 7, Handle, 61 ) );
		StringAssert.Contains( OriginalTrackVehicles.Read( SaveTrackRideBlock.ParseBody( chunks.ToArray() ) ).Problem ?? "", "not in one run" );
	}

	[TestMethod]
	public void ThePeepListHoldsOneThousandAndTwentyFourNodesForEveryRide()
	{
		Assert.AreEqual( 1024, OriginalTrackVehicles.PeepNodes, "0x544360 allocates 0x5000 bytes of 0x14-byte nodes" );
		Assert.AreEqual( 256, OriginalTrackVehicles.VehiclePool, "0xAC00 bytes of 0xAC-byte records" );
		var chunks = new List<byte>( Chunk( 3, Handle, 1, 2, 3, 4, 5, 6, 7, 8, 2 ) );
		for ( var guest = 1; guest <= OriginalTrackVehicles.PeepNodes + 3; guest++ )
			chunks.AddRange( Chunk( 7, Handle, (uint)guest ) );
		var ride = OriginalTrackVehicles.Read( SaveTrackRideBlock.ParseBody( chunks.ToArray() ) ).Rides.Single();
		Assert.AreEqual( (OriginalTrackVehicles.PeepNodes, 3), (ride.Leaving.Count, ride.DroppedNodes), "\"Peep List full\": the guest is dropped" );
	}

	[TestMethod]
	public void TheLevelPlacesTheVehiclesAfterTheGuestImportAndTheReconcile()
	{
		var root = System.IO.Directory.GetCurrentDirectory();
		while ( root != null && !System.IO.File.Exists( System.IO.Path.Combine( root, "source/OpenTPW/World/Level.cs" ) ) )
			root = System.IO.Path.GetDirectoryName( root );
		Assert.IsNotNull( root, "the repository root" );
		var level = System.IO.File.ReadAllText( System.IO.Path.Combine( root, "source/OpenTPW/World/Level.cs" ) );
		// whatever the line ends: the riders the vehicles carry are the guests of state 16 the guest import restored
		StringAssert.Matches( level, new System.Text.RegularExpressions.Regex( @"ImportOriginalGuests\(\);\s*ReconcileOriginalAdmissions\(\);(\s*//[^\r\n]*)*\s*ImportOriginalTrackRiders\(\);" ) );
	}

	[TestMethod]
	public void SeatNumbersAndModelWordsSurviveAParkResumeSaveAndAnOldSaveLoadsWithoutThem()
	{
		var source = Ride( -4, out var handle ).Controller;
		source.Open();
		Launch( source, 41, 42 );
		var target = Ride( -4, out _ ).Controller;
		Assert.IsNull( Import( target, Block( source, handle, first: 1, count: 3 ), out _ ) );
		var saved = TrackSave.CaptureController( target, 5 );
		Assert.AreEqual( (1, 3), (saved.ModelBase, saved.ModelCount) );
		CollectionAssert.AreEqual( new[] { 2, 1 }, saved.Vehicles[0].SeatNumbers );
		var json = System.Text.Json.JsonSerializer.Serialize( saved );
		var restored = Ride( -4, out _ ).Controller;
		restored.Restore( System.Text.Json.JsonSerializer.Deserialize<BumpControllerSaveData>( json )! );
		CollectionAssert.AreEqual( new[] { 2, 1 }, restored.Vehicles[0].SeatNumbers );
		Assert.AreEqual( (1, 3), (restored.ModelBase, restored.ModelCount) );
		Assert.AreEqual( 1, restored.Vehicles[0].SeatOf( restored.Vehicles[0].Riders.IndexOf( 42 ) ), "rider 42 sits in seat 1 (number 2)" );

		// a save of an ordinary ride writes neither field
		var plain = TrackSave.CaptureController( source, 5 );
		Assert.IsNull( plain.ModelBase );
		Assert.IsNull( plain.Vehicles[0].SeatNumbers );
		StringAssert.DoesNotMatch( System.Text.Json.JsonSerializer.Serialize( plain ), new System.Text.RegularExpressions.Regex( "SeatNumbers|ModelBase|ModelCount" ) );
		// and a save that mismatches them is refused
		Assert.ThrowsException<System.IO.InvalidDataException>( () => Ride( -4, out _ ).Controller.Restore( saved with { ModelCount = null } ) );
		Assert.ThrowsException<System.IO.InvalidDataException>( () => Ride( -4, out _ ).Controller.Restore( saved with { Vehicles = new List<BumpVehicleSaveData> { saved.Vehicles[0] with { SeatNumbers = new List<int> { 1, 1 } } } } ) );
	}
}

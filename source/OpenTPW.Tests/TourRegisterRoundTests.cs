using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

using Vector3 = System.Numerics.Vector3;
using Vector2 = System.Numerics.Vector2;

/// <summary>The tour register round (docs/TOURS.md): the 62 ms tick, stale car slots, the car's drawn pose and head nodes, and the object height maps the cars clear.</summary>
[TestClass]
public class TourRegisterRoundTests
{
	private static Func<uint> Random( uint seed )
	{
		var state = seed;
		return () => state = unchecked( state * 1664525u + 1013904223u );
	}

	private static TourController Create( TourWorld? world = null, uint seed = 1 ) => new( TourStation.Default, Random( seed ), world ?? new TourWorld() );

	private static string Digest( TourController controller )
	{
		var hash = new StateHasher();
		controller.AddCanonicalState( hash );
		return hash.Value.ToString();
	}

	private static TourControllerSaveData RoundTrip( TourController controller ) =>
		System.Text.Json.JsonSerializer.Deserialize<TourControllerSaveData>( System.Text.Json.JsonSerializer.Serialize( TourSave.Capture( controller ) ) )!;

	// ---- TOUR-001: the tick ---------------------------------------------------------------------

	[TestMethod]
	public void TheTourTicksOnceEverySecondWorldStep()
	{
		Assert.AreEqual( 0.062, TourController.TickSeconds, "two 31 ms world steps" );
		var controller = Create();
		controller.Advance( 0.061 );
		Assert.AreEqual( 0, controller.TickCount );
		controller.Advance( 0.001001 );
		Assert.AreEqual( 1, controller.TickCount );
		controller.Advance( 0.062 * 10 + 0.0001 );
		Assert.AreEqual( 11, controller.TickCount );
	}

	[TestMethod]
	public void ASaveFromTheThirtyOneMillisecondClockStillRestores()
	{
		var controller = Create();
		var saved = RoundTrip( controller ) with { TickRemainder = 0.0309 };
		TourSave.Validate( saved, guest => true, new HashSet<int>() );
		var restored = new TourController( saved, Random( 1 ), new TourWorld() );
		restored.Advance( 0.0312 );
		Assert.AreEqual( 1, restored.TickCount, "the old remainder is elapsed seconds: it runs off into the first 62 ms tick" );
		Assert.ThrowsException<InvalidDataException>( () => TourSave.Validate( saved with { TickRemainder = TourController.MaximumSavedRemainder }, guest => true, new HashSet<int>() ) );
		Assert.ThrowsException<InvalidDataException>( () => TourSave.Validate( saved with { TickRemainder = -0.001 }, guest => true, new HashSet<int>() ) );
	}

	// ---- TOUR-009: stale slot words ------------------------------------------------------------

	[TestMethod]
	public void ANewCarKeepsTheStaleWordsOfItsSlot()
	{
		var controller = Create();
		var slot = controller.Cars[1];
		slot.Heading = 1234;
		slot.Corner = 2;
		slot.ApproachHeading = 77;
		slot.Riders[0] = 55;
		slot.Riders[8] = 66;
		controller.Command( TourController.CommandCars, 2 );
		controller.Tick();
		Assert.IsTrue( slot.IsLive, "the second car took the free slot" );
		// the new car's first tick already steers it (a tenth of its bearing error) and may pass a patrol corner, so the heading and corner are only close to the stale ones
		Assert.IsTrue( Math.Abs( slot.Heading - 1234 ) < 100 && slot.Corner is 2 or 3, $"creation leaves the heading and corner alone ({slot.Heading}, {slot.Corner})" );
		Assert.AreEqual( (77, 55, 66), (slot.ApproachHeading, slot.Riders[0], slot.Riders[8]), "creation leaves the approach heading and the seat ids alone" );
		Assert.AreEqual( 0, slot.RiderCount, "the rider count is reset" );
	}

	[TestMethod]
	public void TheStaleWordsOfAFreeSlotAreSavedHashedAndRestored()
	{
		var controller = Create();
		controller.Cars[3].Heading = 900;
		controller.Cars[3].Corner = 3;
		controller.Cars[3].ApproachHeading = -40;
		controller.Cars[3].Riders[2] = 12;
		var saved = RoundTrip( controller );
		TourSave.Validate( saved, guest => true, new HashSet<int>() );
		Assert.AreEqual( 1, saved.FreeSlots!.Count );
		var copy = new TourController( saved, Random( 1 ), new TourWorld() );
		Assert.AreEqual( (900, 3, -40, 12), (copy.Cars[3].Heading, copy.Cars[3].Corner, copy.Cars[3].ApproachHeading, copy.Cars[3].Riders[2]) );
		Assert.AreEqual( Digest( controller ), Digest( copy ) );
		controller.Cars[3].Corner = 1;
		Assert.AreNotEqual( Digest( controller ), Digest( copy ), "a stale word is part of the digest" );
		Assert.AreEqual( 0, TourSave.Capture( Create() ).FreeSlots?.Count ?? 0, "a fresh controller saves no free slot" );
		Assert.ThrowsException<InvalidDataException>( () => TourSave.Validate( saved with { FreeSlots = saved.FreeSlots!.Select( free => free with { Slot = 0 } ).ToList() }, guest => true, new HashSet<int>() ), "a free slot may not name a slot a car holds" );
		Assert.ThrowsException<InvalidDataException>( () => TourSave.Validate( saved with { FreeSlots = saved.FreeSlots!.Select( free => free with { Corner = 9 } ).ToList() }, guest => true, new HashSet<int>() ) );
	}

	private sealed class Lcg
	{
		public uint State;
		public uint Next() => State = unchecked( State * 1664525u + 1013904223u );
	}

	[TestMethod]
	public void ASavedAndAnUninterruptedRunAgreeAcrossCarSlotReuse()
	{
		var world = new TourWorld { CellHeight = ( x, z ) => 8f };
		var first = new Lcg { State = 9 };
		var controller = new TourController( TourStation.Default with { RotationDegrees = 90 }, first.Next, world );
		controller.Command( TourController.CommandCars, 3 );
		controller.Command( TourController.CommandDuration, 12 );
		for ( var tick = 0; tick < 5000; tick++ )
		{
			controller.Tick();
			if ( tick == 2000 )
				controller.Command( TourController.CommandCars, 1 );
			if ( tick == 3500 )
				controller.Command( TourController.CommandCars, 3 );
		}
		controller.Command( TourController.CommandCars, 1 );
		for ( var tick = 0; tick < 60_000 && controller.CarCount > 1; tick++ )
			controller.Tick();
		Assert.AreEqual( 1, controller.CarCount, "the extra cars left" );
		var saved = RoundTrip( controller );
		TourSave.Validate( saved, guest => true, new HashSet<int>() );
		Assert.IsNotNull( saved.FreeSlots, "cars came and went, so free slots hold their last heading" );
		var second = new Lcg { State = first.State };
		var copy = new TourController( saved, second.Next, new TourWorld { CellHeight = world.CellHeight } );
		Assert.AreEqual( Digest( controller ), Digest( copy ) );
		for ( var tick = 0; tick < 4000; tick++ )
		{
			controller.Tick();
			copy.Tick();
			if ( tick == 100 )
			{
				controller.Command( TourController.CommandCars, 1 );
				copy.Command( TourController.CommandCars, 1 );
			}
			if ( tick == 1500 )
			{
				controller.Command( TourController.CommandCars, 3 );
				copy.Command( TourController.CommandCars, 3 );
			}
		}
		Assert.AreEqual( Digest( controller ), Digest( copy ), "cars created in reused slots continue from the same stale words" );
	}

	[TestMethod]
	public void ASaveHoldingOnlyTheRidersAboardStillValidates()
	{
		var controller = Create();
		controller.Board( 3 );
		var saved = RoundTrip( controller );
		var legacy = saved with { Cars = saved.Cars.Select( car => car with { Riders = car.Riders.Take( Math.Abs( car.RiderCount ) ).ToList() } ).ToList() };
		TourSave.Validate( legacy, guest => guest == 3, new HashSet<int>() );
		var restored = new TourController( legacy, Random( 1 ), new TourWorld() );
		Assert.AreEqual( 3, restored.Cars[0].Riders[0] );
		Assert.ThrowsException<InvalidDataException>( () => TourSave.Validate( saved with { Cars = saved.Cars.Select( car => car with { Riders = car.Riders.Take( 4 ).ToList() } ).ToList() }, guest => guest == 3, new HashSet<int>() ), "neither the riders aboard nor every seat" );
	}

	// ---- TOUR-008: the drawn pose ---------------------------------------------------------------

	private static Vector3 Forward( int heading, int pitch = 0, int roll = 0, Vector3? axis = null ) =>
		Vector3.Transform( axis ?? Vector3.UnitZ, TourPose.Rotation( new TourDrawPose { Heading = heading, Pitch = pitch, Roll = roll } ) );

	private static void AssertNear( Vector3 expected, Vector3 actual, string message ) =>
		Assert.IsTrue( Vector3.Distance( expected, actual ) < 1e-4f, $"{message}: expected {expected}, got {actual}" );

	[TestMethod]
	public void TheModelsPlusZAxisPointsAlongTheCarsVelocity()
	{
		// the controller moves a car by (cos heading, sin heading) on the module's two ground axes, the model frame's x and z
		foreach ( var heading in new[] { 0, 512, 1024, 2048, 3072, 777 } )
		{
			var angle = heading * 2 * MathF.PI / 4096;
			AssertNear( new Vector3( MathF.Cos( angle ), 0, MathF.Sin( angle ) ), Forward( heading ), $"heading {heading}" );
		}
		var controller = Create();
		var driven = controller.Cars[0];
		driven.Heading = 600;
		driven.Speed = 5000;
		var before = (driven.X, driven.Z);
		controller.Tick();
		var moved = new Vector3( driven.X - before.X, 0, driven.Z - before.Z );
		Assert.IsTrue( Vector3.Dot( Vector3.Normalize( moved ), Forward( driven.Heading ) ) > 0.99f, "the car moves the way the model's +Z axis points" );
	}

	[TestMethod]
	public void PitchCountsInThousandAndTwentyFourthsAndRollInverts()
	{
		// heading 1024 makes the yaw 0, leaving the roll about Z and the pitch about X
		AssertNear( new Vector3( 0, 0, -1 ), Forward( 1024, pitch: 256, axis: Vector3.UnitY ), "a quarter turn of pitch (256, not 1024) tips the model's up axis to −Z" );
		AssertNear( new Vector3( 0, 0, 1 ), Forward( 1024, pitch: -256, axis: Vector3.UnitY ), "a negative pitch tips it the other way" );
		AssertNear( new Vector3( 0, -1, 0 ), Forward( 1024, roll: 1024, axis: Vector3.UnitX ), "a quarter turn of positive roll turns the model's x axis to −Y (the roll is negated)" );
		AssertNear( new Vector3( 0, 1, 0 ), Forward( 1024, roll: 3072, axis: Vector3.UnitX ), "three quarters of roll" );
		// roll first, then pitch, then yaw: the pitch about X sees the rolled axis (pitch first would leave the x axis at +X and the roll then give −Y)
		AssertNear( new Vector3( 0, 0, 1 ), Forward( 1024, pitch: 256, roll: 1024, axis: Vector3.UnitX ), "roll, then pitch" );
	}

	[TestMethod]
	public void TheFirstDrawSnapsAndLaterDrawsEaseAQuarterOfTheWay()
	{
		var car = new TourCar { X = 3000, Y = 6000, Z = 9000, PreviousX = 3000, PreviousY = 6000, PreviousZ = 9000, Heading = 400, PreviousHeading = 400, Roll = 40, PreviousRoll = 40, Pitch = 400, PreviousPitch = 400 };
		var snapped = TourPose.Snap( car );
		Assert.AreEqual( (10f, 20f, 30f, 400, 40, 400), (snapped.X, snapped.Y, snapped.Z, snapped.Heading, snapped.Roll, snapped.Pitch) );
		var drawn = new TourDrawPose();
		drawn = TourPose.Smooth( car, 1f, drawn );
		Assert.AreEqual( 2.5f, drawn.X, 1e-4f );
		Assert.AreEqual( 5f, drawn.Y, 1e-4f );
		Assert.AreEqual( 7.5f, drawn.Z, 1e-4f );
		Assert.AreEqual( (100, 10, 50), (drawn.Heading, drawn.Roll, drawn.Pitch), "(target + 3 × drawn) / 4 for heading and roll, (target + 7 × drawn) / 8 for pitch" );
		for ( var frame = 0; frame < 60; frame++ )
			drawn = TourPose.Smooth( car, 1f, drawn );
		Assert.AreEqual( 10f, drawn.X, 0.01f );
		Assert.IsTrue( drawn.Heading is >= 396 and <= 400, $"the heading settles at the car's ({drawn.Heading})" );
	}

	[TestMethod]
	public void TheDrawnPoseInterpolatesAndTakesTheShortWayRound()
	{
		var car = new TourCar { X = 6000, PreviousX = 3000, Heading = 200, PreviousHeading = 100 };
		var halfway = TourPose.Smooth( car, 0.5f, TourPose.Snap( new TourCar() ) );
		Assert.AreEqual( 0.25f * (15f + 3 * 0f), halfway.X, 1e-4f, "the position target is the previous-to-current midpoint, 15 model units" );
		Assert.AreEqual( (150 + 3 * 0) / 4, halfway.Heading, "the heading target is the midpoint 150" );
		var wrapping = new TourCar { Heading = 10, PreviousHeading = 10 };
		var east = TourPose.Smooth( wrapping, 1f, new TourDrawPose { Heading = 4090 } );
		Assert.AreEqual( 4094, east.Heading, "from 4090 towards 10 the shortest way is up through the wrap" );
		var west = TourPose.Smooth( new TourCar { Heading = 4090, PreviousHeading = 4090 }, 1f, new TourDrawPose { Heading = 10 } );
		Assert.AreEqual( 6, west.Heading, "and from 10 towards 4090 it is down through it" );
		var clamped = TourPose.Smooth( car, 99f, new TourDrawPose() );
		var twice = TourPose.Smooth( car, 2f, new TourDrawPose() );
		Assert.AreEqual( twice.X, clamped.X, "the draw fraction is clamped to 2" );
	}

	[TestMethod]
	public void TheDrawFractionIsTheTickClockOverSixtyTwoMilliseconds()
	{
		var controller = Create();
		controller.Advance( 0.031 );
		Assert.AreEqual( 0.5f, TourPose.Fraction( controller ), 1e-3f );
		controller.Advance( 0.031 );
		Assert.IsTrue( TourPose.Fraction( controller ) < 0.05f, "a tick ran: the fraction starts over" );
	}

	[TestMethod]
	public void RidersTakeShuffledHeadNodesFromTheCarsOwnRandomWord()
	{
		Assert.AreEqual( 6, TourPose.HeadNode( 5, 0, 9 ) );
		Assert.AreEqual( 0, TourPose.HeadNode( 5, 3, 9 ), "(5 + 3 + 1) mod 9" );
		Assert.AreEqual( 1, TourPose.HeadNode( 0, 0, 9 ), "a car with random 0 puts its first rider on node 2: no seat order" );
		var nodes = Enumerable.Range( 0, 9 ).Select( rider => TourPose.HeadNode( 123456, rider, 9 ) ).ToList();
		CollectionAssert.AreEquivalent( Enumerable.Range( 0, 9 ).ToList(), nodes, "nine riders of nine nodes take every node once" );
		Assert.AreEqual( 3, Enumerable.Range( 0, 9 ).Select( rider => TourPose.HeadNode( 7, rider, 3 ) ).Distinct().Count(), "more riders than nodes share nodes, as the original does" );
		Assert.AreEqual( -1, TourPose.HeadNode( 7, 0, 0 ), "a car mesh without head nodes carries no heads" );
		Assert.AreEqual( TourPose.HeadNode( int.MaxValue, 8, 9 ), TourPose.HeadNode( int.MaxValue, 8, 9 ), "deterministic, and no random draw is made" );
		// the shuffle draws nothing from the simulation: creating cars is the only place the word is drawn
		var controller = Create();
		controller.Board( 1 );
		var draws = 0;
		var counted = new TourController( TourStation.Default, () => { draws++; return 1; }, new TourWorld() );
		var afterCreation = draws;
		counted.Board( 1 );
		_ = TourPose.HeadNode( counted.Cars[0].Random, 0, 9 );
		Assert.AreEqual( afterCreation, draws );
	}

	// ---- TOUR-005: the object ground -----------------------------------------------------------

	private static HmpFile Hmp( int width, int height )
	{
		var profile = width * 5 * height * 5;
		var cells = width * height;
		var data = new byte[48 + profile + cells * 2];
		BinaryPrimitives.WriteUInt16LittleEndian( data.AsSpan( 0 ), 3 );
		BinaryPrimitives.WriteUInt16LittleEndian( data.AsSpan( 2 ), 0xAB1E );
		BinaryPrimitives.WriteUInt16LittleEndian( data.AsSpan( 4 ), 5 );
		BinaryPrimitives.WriteUInt16LittleEndian( data.AsSpan( 6 ), 100 );
		BinaryPrimitives.WriteUInt16LittleEndian( data.AsSpan( 8 ), (ushort)width );
		BinaryPrimitives.WriteUInt16LittleEndian( data.AsSpan( 10 ), (ushort)height );
		BinaryPrimitives.WriteUInt32LittleEndian( data.AsSpan( 12 ), 48 );
		BinaryPrimitives.WriteUInt32LittleEndian( data.AsSpan( 16 ), (uint)(48 + profile) );
		BinaryPrimitives.WriteUInt32LittleEndian( data.AsSpan( 20 ), (uint)(48 + profile + cells) );
		for ( var index = 0; index < 6; index++ )
			BinaryPrimitives.WriteSingleLittleEndian( data.AsSpan( 24 + index * 4 ), index < 3 ? index : 20f );
		for ( var index = 0; index < cells; index++ )
			data[48 + profile + index] = (byte)(index + 1);
		return new HmpFile( new MemoryStream( data ) );
	}

	/// <summary>Every cell of the map lands on exactly the world cell that OpenTPW's model-to-engine transform puts it on, for every rotation.</summary>
	[TestMethod]
	public void TheMapIndexFollowsThePlacementsRotation()
	{
		const int width = 4, height = 3;
		var map = Hmp( width, height );
		foreach ( var rotation in new[] { 0, 90, 180, 270 } )
		{
			var placement = new ObjectPlacement( 12, 20, rotation, Vector2.Zero, 0 );
			var (originX, originZ) = ObjectHeightMaps.InstanceOrigin( placement );
			var seen = new HashSet<int>();
			for ( var row = 0; row < height; row++ )
			{
				for ( var column = 0; column < width; column++ )
				{
					var engine = Vector3.Transform( new Vector3( column * 10 + 5, row * 10 + 5, 0 ), placement.ModelToEngine );
					var cellX = (int)MathF.Floor( engine.X / ObjectPlacement.CellSize );
					var cellY = (int)MathF.Floor( engine.Y / ObjectPlacement.CellSize );
					var index = ObjectHeightMaps.CellIndex( map, rotation, originX, originZ, cellX, cellY );
					Assert.AreEqual( row * width + column, index, $"rotation {rotation}: model cell ({column}, {row}) stands on world cell ({cellX}, {cellY})" );
					seen.Add( index!.Value );
				}
			}
			Assert.AreEqual( width * height, seen.Count );
			// the cells just outside the footprint are not in the map
			var outside = Vector3.Transform( new Vector3( -5, 5, 0 ), placement.ModelToEngine );
			Assert.IsNull( ObjectHeightMaps.CellIndex( map, rotation, originX, originZ, (int)MathF.Floor( outside.X / ObjectPlacement.CellSize ), (int)MathF.Floor( outside.Y / ObjectPlacement.CellSize ) ), $"rotation {rotation}" );
		}
	}

	[TestMethod]
	public void AnObjectsGroundIsItsMapByteScaledToModelUnits()
	{
		Assert.AreEqual( 100f, 255 * ObjectHeightMaps.ByteScale, 0.01f, "the byte 255 is 100 model units" );
		var world = new TourWorld { CellHeight = ( x, z ) => 5f, ObjectHeight = ( x, z ) => x == 40 ? 24.9f : null };
		Assert.AreEqual( 24 * 300, world.CellGround( 40, 3 ), "an object's height truncated to whole units, times 300" );
		Assert.AreEqual( 5 * 300, world.CellGround( 41, 3 ), "a cell without an object is the terrain" );
	}

	/// <summary>A track tile's 2 x 2 map is indexed from the instance corner with the engine rotation, so its four cells are four different map cells at every rotation (TOUR-005, second layer).</summary>
	[TestMethod]
	public void ATrackTilesFourCellsAreFourMapCellsAtEveryRotation()
	{
		var map = Hmp( 2, 2 );
		foreach ( var rotation in new[] { 0, 90, 180, 270 } )
		{
			var (positionX, positionY) = TrackRenderer.PieceInstanceOrigin( 7, 4, rotation );
			var seen = new HashSet<int>();
			foreach ( var (dx, dy) in new[] { (0, 0), (1, 0), (0, 1), (1, 1) } )
			{
				var index = ObjectHeightMaps.CellIndex( map, ( 360 - rotation ) % 360, positionX, positionY, 7 + dx, 4 + dy );
				Assert.IsNotNull( index, $"rotation {rotation}: tile cell ({dx}, {dy}) lies inside the map" );
				seen.Add( index.Value );
			}
			Assert.AreEqual( 4, seen.Count, $"rotation {rotation}" );
			Assert.IsNull( ObjectHeightMaps.CellIndex( map, ( 360 - rotation ) % 360, positionX, positionY, 9, 4 ), $"rotation {rotation}: the cell beside the tile is outside" );
		}
	}

	[TestMethod]
	public void ATrackPiecesInstanceSitsTwoCellsOverByTheEngineRotation()
	{
		Assert.AreEqual( (70, 40), TrackRenderer.PieceInstanceOrigin( 7, 4, 0 ) );
		Assert.AreEqual( (90, 40), TrackRenderer.PieceInstanceOrigin( 7, 4, 90 ), "engine 270: x + 2" );
		Assert.AreEqual( (90, 60), TrackRenderer.PieceInstanceOrigin( 7, 4, 180 ), "engine 180: x + 2, y + 2" );
		Assert.AreEqual( (70, 60), TrackRenderer.PieceInstanceOrigin( 7, 4, 270 ), "engine 90: y + 2" );
	}
}

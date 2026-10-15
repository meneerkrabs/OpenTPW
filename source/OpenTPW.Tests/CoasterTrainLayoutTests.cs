using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// The train layout of the original (tp.exe <c>0x42fad0</c>, <c>0x430130</c>, <c>0x43bc20</c>, <c>0x43ba90</c>, <c>0x43c1f0</c>, <c>0x43c450</c>): car offsets
/// from the car types' extents around the middle car and the placement by the release time.
/// </summary>
[TestClass]
public class CoasterTrainLayoutTests
{
	private const float Tick = 1f / FixedStepClock.TicksPerSecond;
	private static readonly (int X, int Y) Depart = (20, 20);

	[TestInitialize]
	public void Init() => Log = new();

	/// <summary>A caterpillar train like Fantasy's: a head, centre bodies and a tail, the head and tail without seats.</summary>
	private static Dictionary<string, string> CaterpillarSettings( int cars = 6 )
	{
		var settings = CoasterTests.PcSettings();
		settings["sTrainType.uiMaxCars"] = cars.ToString();
		settings["asCarTypes[0].pcMeshFilename"] = "head";
		settings["asCarTypes[0].fRearDistanceAdd"] = "-2.5";
		settings["asCarTypes[1].pcMeshFilename"] = "body";
		settings["asCarTypes[1].fFrontDistanceAdd"] = "0.5";
		settings["asCarTypes[1].fRearDistanceAdd"] = "0.5";
		settings["asCarTypes[2].pcMeshFilename"] = "tail";
		settings["asCarTypes[2].fFrontDistanceAdd"] = "-0.5";
		settings["sTrainType.uiFrontCarTypeIndex"] = "0";
		settings["sTrainType.uiCentreCarTypeIndex"] = "1";
		settings["sTrainType.uiRearCarTypeIndex"] = "2";
		return settings;
	}

	/// <summary>The caterpillar with metrics as its models would give them (model extents plus the distance adds, head sockets).</summary>
	private static CoasterDefinition Caterpillar( int cars = 6 )
	{
		var definition = CoasterDefinition.FromValues( CaterpillarSettings( cars ) );
		var models = new[] { (2f, 4.8f, 0), (3.9f, 3.8f, 3), (4.5f, 2.9f, 0) };
		definition.ApplyCarMetrics( index =>
		{
			var type = definition.CarTypes.Types[index];
			var (front, rear, heads) = models[index];
			return new CoasterCarMetrics( type.FrontAdd + front, type.RearAdd + rear, heads );
		} );
		return definition;
	}

	private static CoasterRide Built( CoasterDefinition definition )
	{
		var ride = new CoasterRide( definition, new CoasterWorld( 1 ), "Test Coaster" );
		var builder = new CoasterBuilder( ride, new CoasterSite( 64, 64 ) );
		CoasterTests.PlaceStation( builder, Depart );
		builder.Height = 40;
		foreach ( var (dx, dy) in CoasterTests.Circuit )
			Assert.AreEqual( CoasterPlaceResult.Ok, builder.Place( Depart.X + dx, Depart.Y + dy ) );
		Assert.AreEqual( CoasterPlaceResult.Ok, CoasterTests.CloseAndComplete( builder ) );
		ride.Command( 8, 0 );
		return ride;
	}

	[TestMethod]
	public void WithoutCarModelsEveryCarIsSevenUnitsLong()
	{
		var definition = CoasterDefinition.FromValues( CoasterTests.PcSettings() );
		Assert.IsFalse( definition.HasCarModels );
		Assert.AreEqual( 1, definition.ReferenceCar, "(4 − 1) >> 1: the second car (0x43baef)" );
		Assert.AreEqual( (7f, 7f, 7f), definition.CarPairDistances );
		CollectionAssert.AreEqual( new[] { 7f, 0f, -7f, -14f }, definition.CarOffsets.ToArray(), "car 0 is ahead of the reference car, cars 2 and 3 behind it (0x43bc20)" );
	}

	[TestMethod]
	public void TheCarOffsetsAddThePairDistanceOfEachCarsIndexAroundTheMiddleCar()
	{
		var definition = Caterpillar();
		Assert.AreEqual( 2, definition.ReferenceCar, "(6 − 1) >> 1" );
		var (front, centre, rear) = definition.CarPairDistances;
		Assert.AreEqual( (-2.5f + 4.8f) + (0.5f + 3.9f), front, 1e-5f, "+0x30c = the front type's rear + the centre type's front" );
		Assert.AreEqual( (0.5f + 3.8f) + (0.5f + 3.9f), centre, 1e-5f, "+0x310 = the centre type's rear + its front" );
		Assert.AreEqual( (-0.5f + 4.5f) + (0.5f + 3.8f), rear, 1e-5f, "+0x314 = the rear type's front + the centre type's rear" );
		var offsets = definition.CarOffsets;
		Assert.AreEqual( 0f, offsets[2] );
		Assert.AreEqual( centre, offsets[1], 1e-5f, "car 1 adds the centre pair" );
		Assert.AreEqual( centre + front, offsets[0], 1e-5f, "car 0 adds the front pair" );
		Assert.AreEqual( -centre, offsets[3], 1e-5f );
		Assert.AreEqual( -2 * centre, offsets[4], 1e-5f );
		Assert.AreEqual( -2 * centre - rear, offsets[5], 1e-5f, "the last car subtracts the rear pair" );

		// A two-car train measures its one pair against the centre type too, as the original's loader does.
		var two = Caterpillar( 2 );
		Assert.AreEqual( 0, two.ReferenceCar );
		Assert.AreEqual( -two.CarPairDistances.Rear, two.CarOffsets[1], 1e-5f );
	}

	[TestMethod]
	public void TheTrainsArePlacedByTheReleaseTimeFromTrainZerosLastCar()
	{
		var settings = CoasterTests.PcSettings();
		settings["sCoasterType.uiTrainReleaseRate"] = "4000";
		var definition = CoasterDefinition.FromValues( settings );
		var ride = Built( definition );
		Assert.IsTrue( ride.Trains.Count >= 2, $"{ride.Trains.Count} trains" );
		var samples = ride.Samples;
		var n = samples.Count;
		var release = definition.TrainReleaseRate / 1000.0;
		var last = definition.CarCount - 1;
		Assert.AreEqual( 0f, ride.CarCoordinate( ride.Trains[0], last ), 1e-4f, "a first placement starts train 0's last car on the platform start" );
		for ( var index = 1; index < ride.Trains.Count; index++ )
		{
			// The walk of 0x43c1f0, written out: whole samples from the previous last car until the release time is reached.
			var previous = ride.CarCoordinate( ride.Trains[index - 1], last );
			var sample = (int)previous;
			var time = (1 - (previous - sample)) * ride.Spacing / samples[sample].Speed;
			var before = 0.0;
			while ( time < release )
			{
				sample = (sample + 1) % n;
				before = time;
				time += ride.Spacing / samples[sample].Speed;
			}
			var f = (time - release) / (time - before);
			var expected = (((sample - 1) * (1 - f) + sample * f) + n) % n;
			Assert.AreEqual( expected, ride.CarCoordinate( ride.Trains[index], last ), 1e-3, $"train {index}'s last car stands the release time ahead of train {index - 1}'s (i − 1 + f, the original's own interpolation)" );
		}
	}

	[TestMethod]
	public void APlacedTrainRunsAtTheMeanRatedSpeedUnderItsCarsAndItsExtentsComeFromTheCarOffsets()
	{
		var definition = Caterpillar();
		var ride = Built( definition );
		var train = ride.Trains[0];
		var samples = ride.Samples;
		var expected = 0f;
		var coordinate = (double)ride.CarCoordinate( train, definition.CarCount - 1 );
		var coordinates = new double[definition.CarCount];
		for ( var car = definition.CarCount - 1; car >= 0; car-- )
		{
			coordinates[car] = coordinate;
			if ( car > 0 )
				coordinate += (definition.CarMetrics( car ).FrontExtent + definition.CarMetrics( car - 1 ).RearExtent) / (double)ride.Spacing;
		}
		foreach ( var at in coordinates )
		{
			var index = (int)at;
			var fraction = (float)(at - index);
			expected += samples[(index + 1) % samples.Count].Speed * fraction + (1 - fraction) * samples[index % samples.Count].Speed;
		}
		Assert.AreEqual( expected / definition.CarCount, train.Speed, 1e-3f, "train +0x78 = the mean of the sample speeds under the cars (0x43cc8a)" );
		Assert.AreEqual( (float)coordinates[definition.ReferenceCar], train.Position, 1e-3f, "the train's position is its middle car's" );
		var length = (definition.CarOffsets[0] - definition.CarOffsets[^1] + definition.CarMetrics( 0 ).FrontExtent + definition.CarMetrics( definition.CarCount - 1 ).RearExtent) / ride.Spacing;
		Assert.AreEqual( length, train.Front - train.Rear, 1e-3f, "front and rear extents: car 0's offset and front extent, the last car's offset and rear extent" );
	}

	[TestMethod]
	public void TheCarPointAfterTheLastSampleIsItsSlotNCopy()
	{
		var ride = Built( CoasterDefinition.FromValues( CoasterTests.PcSettings() ) );
		var samples = ride.Samples;
		Assert.AreEqual( samples[^1].Position, ride.SampleAt( samples.Count - 0.5f ).Position, "slot n holds a copy of the last sample (0x441783)" );
		Assert.AreNotEqual( samples[^1].Position, ride.SampleAt( samples.Count - 1.5f ).Position );
	}

	[TestMethod]
	public void ARerateRestartsThePlacementFromTrainZerosLastCarAndKeepsTheCount()
	{
		var ride = Built( CoasterDefinition.FromValues( CoasterTests.PcSettings() ) );
		ride.Command( 5, 0 );
		for ( var tick = 0; tick < 20 * 60; tick++ )
			ride.Advance( Tick );
		var last = ride.Definition.CarCount - 1;
		var lead = ride.CarCoordinate( ride.Trains[0], last );
		var count = ride.Trains.Count;
		ride.ApplyScriptSpeed( 51 );
		if ( ride.Trains.Count == count )
			Assert.AreEqual( lead, ride.CarCoordinate( ride.Trains[0], last ), 1e-3f, "train 0 keeps its last car where it stood (0x43c1f0 keeps its +0x30)" );
		ride.ClearTrains();
		Assert.AreEqual( lead, ride.LeadCarCoordinate, 1e-3f, "the idle slot keeps the coordinate" );
	}

	[TestMethod]
	public void ASaveFromBeforeTheCarLayoutHoldsTheFrontCarsCoordinate()
	{
		var ride = Built( CoasterDefinition.FromValues( CoasterTests.PcSettings() ) );
		ride.Command( 5, 0 );
		for ( var tick = 0; tick < 10 * 60; tick++ )
			ride.Advance( Tick );
		var captured = CoasterSave.CaptureRide( ride, 3 );
		Assert.IsTrue( captured.Trains.All( train => train.ReferenceCar == 1 ), "the save names the reference car" );
		var front = ride.CarCoordinate( ride.Trains[0], 0 );
		var old = captured with { Trains = captured.Trains.Select( ( train, index ) => train with { ReferenceCar = null, Position = ride.CarCoordinate( ride.Trains[index], 0 ) } ).ToList() };
		var restored = new CoasterRide( ride.Definition, new CoasterWorld( 1 ), "Restored" );
		restored.RestoreSnapshot( old );
		Assert.AreEqual( ride.Trains[0].Position, restored.Trains[0].Position, 1e-3f, "the front car's coordinate is moved back to the reference car" );
		Assert.AreEqual( front, restored.CarCoordinate( restored.Trains[0], 0 ), 1e-3f );
		var other = new CoasterRide( ride.Definition, new CoasterWorld( 1 ), "Wrong" );
		Assert.ThrowsException<System.IO.InvalidDataException>( () => other.RestoreSnapshot( captured with { Trains = captured.Trains.Select( train => train with { ReferenceCar = 2 } ).ToList() } ) );
	}
}

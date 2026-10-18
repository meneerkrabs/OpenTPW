using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// The train layout of the original (tp.exe <c>0x42fad0</c>, <c>0x430130</c>, <c>0x43bc20</c>, <c>0x43ba90</c>, <c>0x43c1f0</c>, <c>0x43c450</c>): car offsets
/// from the car types' extents around the middle car, the seats on the centre-type cars, COAST 6's bounds and the placement by the release time.
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
	public void WithoutCarModelsEveryCarIsSevenUnitsLongWithFourSeats()
	{
		var definition = CoasterDefinition.FromValues( CoasterTests.PcSettings() );
		Assert.IsFalse( definition.HasCarModels );
		Assert.AreEqual( 1, definition.ReferenceCar, "(4 − 1) >> 1: the second car (0x43baef)" );
		Assert.AreEqual( (7f, 7f, 7f), definition.CarPairDistances );
		CollectionAssert.AreEqual( new[] { 7f, 0f, -7f, -14f }, definition.CarOffsets.ToArray(), "car 0 is ahead of the reference car, cars 2 and 3 behind it (0x43bc20)" );
		Assert.AreEqual( (16, 96, 4), (definition.HeadsPerTrain, definition.FleetHeads, definition.SeatCarCount), "4 cars × 4 sockets, × 6 trains; every car seats" );
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

	/// <summary>The ride view rides car 0 of a train whose front type is its centre type, car 1 behind a front car of another type (<c>0x436f15</c>: train +0x34).</summary>
	[TestMethod]
	public void TheRideViewRidesTheFirstSeatCar()
	{
		Assert.AreEqual( 1, Caterpillar().RideCameraCar, "a head car with no seats: the camera sits in the car behind it" );
		var plain = CoasterDefinition.FromValues( CoasterTests.PcSettings() );
		Assert.AreEqual( 0, plain.RideCameraCar, "the same front and centre type: the front car" );
		var settings = CaterpillarSettings( 1 );
		Assert.AreEqual( 0, CoasterDefinition.FromValues( settings ).RideCameraCar, "a one-car train has no second car" );
	}

	/// <summary>The ride view's train is the one with the smallest position, the earliest of equal ones (<c>0x43d7dd</c>: controller +0x124 takes a train whose +0x74 is smaller).</summary>
	[TestMethod]
	public void TheRideViewRidesTheTrainWithTheSmallestPosition()
	{
		var settings = CoasterTests.PcSettings();
		settings["sCoasterType.uiTrainReleaseRate"] = "4000";
		var ride = Built( CoasterDefinition.FromValues( settings ) );
		Assert.IsTrue( ride.Trains.Count >= 2, "the placement gives several trains" );
		var smallest = 0;
		for ( var index = 1; index < ride.Trains.Count; index++ )
		{
			if ( ride.Trains[index].Position < ride.Trains[smallest].Position )
				smallest = index;
		}
		Assert.AreEqual( smallest, ride.RideViewTrain );
		for ( var index = 0; index < ride.Trains.Count; index++ )
			ride.Trains[index].Position = 100f - index * 10f;
		Assert.AreEqual( ride.Trains.Count - 1, ride.RideViewTrain, "the last train is the least advanced here" );
		for ( var index = 0; index < ride.Trains.Count; index++ )
			ride.Trains[index].Position = 42f;
		Assert.AreEqual( 0, ride.RideViewTrain, "equal positions keep the first train" );
		Assert.IsTrue( ride.TryGetRideView( ride.RideViewTrain, out _, out _, out var roll ) );
		var cameraCoordinate = ride.CarCoordinate( ride.Trains[ride.RideViewTrain], ride.Definition.RideCameraCar );
		Assert.AreEqual( ride.CarPose( cameraCoordinate ).Roll, roll, "the ride view rolls with the camera car's frame, not the sample's second-rail roll" );
	}

	[TestMethod]
	public void OnlyTheCentreTypeCarsSeatAndCoastSixIsBoundedByTheHeadSockets()
	{
		var definition = Caterpillar();
		Assert.AreEqual( (12, 12 * 6, 4), (definition.HeadsPerTrain, definition.FleetHeads, definition.SeatCarCount), "head and tail have no sockets; the seats go over the four bodies" );
		var ride = Built( definition );
		Assert.AreEqual( 12, ride.SeatsPerTrain, "the controller starts with one train's head sockets (0x436a61 → 0x43ba90 with 0)" );
		Assert.AreEqual( (12, 2 * 72), (ride.AdmissionRingSize, ride.LeavingRingSize), "ring sizes +0x2f8 and 2 × +0x318 (0x4369c1)" );
		CollectionAssert.AreEqual( new[] { 0, 3, 3, 3, 3, 0 }, ride.Trains[0].Seats );
		ride.Command( 6, 10 );
		CollectionAssert.AreEqual( new[] { 0, 2, 2, 3, 3, 0 }, ride.Trains[0].Seats, "10 over 4 seat cars: 10/4, 8/3, 6/2, 3/1 (0x43bb7b)" );
		ride.Command( 6, 50 );
		Assert.AreEqual( 12, ride.SeatsPerTrain, "at most one train's head sockets" );
		ride.Command( 6, 0 );
		Assert.AreEqual( 12, ride.SeatsPerTrain, "0 stands for them" );
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

	/// <summary>The requeue of 0x43d48b..0x43d4b9: before the first train whose remaining budget is not above the hopper's, ties included, else at the end.</summary>
	[TestMethod]
	public void AFullHopRequeuesTheTrainBeforeTheFirstWithNoMoreTimeLeft()
	{
		List<int> Requeued( int index, float[] budgets, params int[] queue )
		{
			var list = queue.ToList();
			CoasterRide.Requeue( list, budgets, index );
			return list;
		}
		CollectionAssert.AreEqual( new[] { 1, 2, 0 }, Requeued( 0, new[] { 0.1f, 0.5f, 0.3f }, 1, 2 ), "both others have more time left: it goes to the end" );
		CollectionAssert.AreEqual( new[] { 1, 0, 2 }, Requeued( 0, new[] { 0.3f, 0.5f, 0.3f }, 1, 2 ), "an equal budget counts as not above (fcomp, test ah 0x41): it goes before train 2" );
		CollectionAssert.AreEqual( new[] { 0, 1, 2 }, Requeued( 0, new[] { 0.6f, 0.5f, 0.3f }, 1, 2 ), "more time left than every other train: it hops again first" );
		CollectionAssert.AreEqual( new[] { 1 }, Requeued( 1, new[] { 0f, 0.2f }, Array.Empty<int>() ), "alone it is the queue" );
	}

	/// <summary>
	/// A frame of three trains on a long step: every hop is the queue head's, and a train that hopped fully goes back before the first train whose
	/// remaining budget is not above its own (0x43ced8, 0x43d48b..0x43d4b9), so a train with more time left hops before one that hopped less.
	/// </summary>
	[TestMethod]
	public void TheHopsOfAFrameFollowTheRemainingBudgetQueue()
	{
		var settings = CoasterTests.PcSettings();
		settings["sCoasterType.uiTrainReleaseRate"] = "3000";
		var ride = Built( CoasterDefinition.FromValues( settings ) );
		Assert.IsTrue( ride.Trains.Count >= 3, $"{ride.Trains.Count} trains" );
		var trace = new List<(int Train, float Budget, bool Full)>();
		ride.HopObserved = ( train, budget, full ) => trace.Add( (train, budget, full) );
		const float Step = 0.75f;
		var reorders = 0;
		var hops = 0;
		for ( var frame = 0; frame < 40; frame++ )
		{
			trace.Clear();
			ride.Advance( Step );
			var budgets = Enumerable.Repeat( Step, ride.Trains.Count ).ToArray();
			var queue = Enumerable.Range( 0, ride.Trains.Count ).ToList();
			foreach ( var (train, budget, full) in trace )
			{
				Assert.IsTrue( queue.Count > 0, $"frame {frame}: a hop after the queue emptied" );
				Assert.AreEqual( queue[0], train, $"frame {frame}: the head of the queue hops" );
				queue.RemoveAt( 0 );
				budgets[train] = budget;
				hops++;
				if ( !full )
					continue;
				// Written out: the first train still queued whose remaining time is not above this one's.
				var at = queue.Count;
				for ( var slot = 0; slot < queue.Count; slot++ )
				{
					if ( budgets[queue[slot]] <= budget )
					{
						at = slot;
						break;
					}
				}
				if ( at < queue.Count )
					reorders++;
				queue.Insert( at, train );
			}
			Assert.AreEqual( 0, queue.Count, $"frame {frame}: every train ends its frame with a partial hop" );
		}
		Assert.IsTrue( hops > 40 * ride.Trains.Count, "the long step gives full hops" );
		Assert.IsTrue( reorders > 0, "some train went back ahead of a train with less time left" );
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

	/// <summary>A car stands on the curve itself (0x43c0a0): at t = (c − c₀)/(c₁ − c₀) of the knot pair whose knot coordinates enclose its coordinate.</summary>
	[TestMethod]
	public void ACarStandsOnTheCurveAtItsKnotCoordinateParameter()
	{
		var ride = Built( CoasterDefinition.FromValues( CoasterTests.PcSettings() ) );
		var knots = ride.Track.BuildKnots( ride.Site );
		var coordinates = ride.KnotCoordinates;
		for ( var knot = 1; knot < knots.Count - 1; knot++ )
			Assert.IsTrue( System.Numerics.Vector3.Distance( knots[knot].Position, ride.CarPose( coordinates[knot] ).Position ) < 1e-3f, $"knot {knot} at its coordinate" );
		var pair = 4;
		var c = (coordinates[pair] + coordinates[pair + 1]) / 2;
		var controls = HermiteCoasterCurve.Controls( knots, ride.Definition.Curviness, closed: true );
		var expected = HermiteCoasterCurve.Bezier( knots[pair].Position, controls[pair].Forward, controls[pair + 1].Back, knots[pair + 1].Position, 0.5f );
		Assert.IsTrue( System.Numerics.Vector3.Distance( expected, ride.CarPose( c ).Position ) < 1e-3f, "halfway between two knot coordinates is t = ½ of their pair" );
		var lap = ride.CarPose( c + ride.Samples.Count );
		Assert.IsTrue( System.Numerics.Vector3.Distance( expected, lap.Position ) < 1e-3f, "a coordinate a lap on is the same point" );
		var near = ride.CarPose( ride.Samples.Count - 0.25f );
		Assert.IsTrue( float.IsFinite( near.Position.X ) && float.IsFinite( near.Yaw ), "the closing pair poses too" );
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
	public void IncompleteToBuiltRestartsEveryExistingSlotWithoutReplacingItsTrain()
	{
		var settings = CoasterTests.PcSettings();
		settings["sCoasterType.uiTrainReleaseRate"] = "4000";
		var ride = Built( CoasterDefinition.FromValues( settings ) );
		var slots = ride.Trains.ToArray();
		Assert.IsTrue( slots.Length >= 2, "the reset must visit more than the first slot" );
		ride.Advance( 0.125 );
		Assert.IsTrue( slots.All( train => train.WheelAnimationStartSeconds == 0.0 ) );
		// Arrange the transient state between PlaceTrains and EnterState in Rebuild. Entering Incomplete normally
		// removes the trains, which would only test constructor initialization rather than the state setter's reset loop.
		typeof( CoasterRide ).GetProperty( nameof( CoasterRide.State ) )!.SetValue( ride, CoasterControllerState.Incomplete );
		ride.EnterState( CoasterControllerState.Built );
		Assert.AreEqual( CoasterControllerState.Built, ride.State );
		Assert.AreEqual( slots.Length, ride.Trains.Count );
		for ( var index = 0; index < slots.Length; index++ )
		{
			Assert.AreSame( slots[index], ride.Trains[index], $"slot {index} was reset without replacement" );
			Assert.AreEqual( 0.125, slots[index].WheelAnimationStartSeconds, $"state 1 → 2 restarts slot {index} at the current controller time" );
		}
	}

	[TestMethod]
	public void ReratingAndOrdinaryStateChangesKeepExistingWheelEpochs()
	{
		var settings = CoasterTests.PcSettings();
		settings["sCoasterType.uiMaxTrains"] = "1";
		var ride = Built( CoasterDefinition.FromValues( settings ) );
		var train = ride.Trains[0];
		var start = train.WheelAnimationStartSeconds;
		ride.Advance( 0.125 );
		ride.Rebuild();
		ride.ApplyScriptSpeed( 51 );
		Assert.AreSame( train, ride.Trains[0] );
		Assert.AreEqual( start, train.WheelAnimationStartSeconds, "0x43cad6 only sets up rating slots that were inactive" );
		foreach ( var state in new[] { CoasterControllerState.Open, CoasterControllerState.Closed, CoasterControllerState.Open,
			CoasterControllerState.Built, CoasterControllerState.Closed, CoasterControllerState.Built, CoasterControllerState.Built } )
		{
			ride.Advance( 0.03125 );
			ride.EnterState( state );
			Assert.AreSame( train, ride.Trains[0] );
			Assert.AreEqual( start, train.WheelAnimationStartSeconds, $"state {state} leaves the wheel clip running" );
		}
		ride.Command( 4, 1 );
		ride.Command( 4, 0 );
		ride.Command( 6, 8 );
		Assert.AreEqual( start, train.WheelAnimationStartSeconds, "power and capacity changes do not initialize a train slot" );
	}

	/// <summary>A synthetic flat sampler: at the default speed 5, the rating lap is (count − 2) × 3/5 seconds.</summary>
	private sealed class WheelSlotCurve : ICoasterCurve
	{
		public int Count { get; set; } = 8;
		public CoasterSampling Sample( IReadOnlyList<CoasterKnot> knots, float curviness ) => new(
			Enumerable.Range( 0, Count ).Select( index => new CoasterSample { Position = new System.Numerics.Vector3( 0, 0, index * 3f ) } ).ToArray(), 3f );
	}

	[TestMethod]
	public void RatingStartsOnlyNewSlotsAndRestartsSlotsRemovedAndActivatedAgain()
	{
		var settings = CoasterTests.PcSettings();
		settings["sCoasterType.uiTrainReleaseRate"] = "2000";
		var ride = Built( CoasterDefinition.FromValues( settings ) );
		var curve = new WheelSlotCurve();
		ride.Curve = curve;
		ride.Rebuild();
		Assert.AreEqual( 1, ride.Trains.Count, "3.6-second lap / 2-second release" );
		var retained = ride.Trains[0];
		var start = retained.WheelAnimationStartSeconds;
		ride.Advance( 0.125 );
		curve.Count = 15;
		ride.Rebuild();
		Assert.AreEqual( 3, ride.Trains.Count, "7.8-second lap / 2-second release" );
		Assert.AreSame( retained, ride.Trains[0] );
		Assert.AreEqual( start, retained.WheelAnimationStartSeconds );
		foreach ( var train in ride.Trains.Skip( 1 ) )
			Assert.AreEqual( 0.125, train.WheelAnimationStartSeconds, "a newly activated slot starts now" );
		var removed = ride.Trains[1];
		ride.Advance( 0.125 );
		curve.Count = 8;
		ride.Rebuild();
		Assert.AreEqual( 1, ride.Trains.Count );
		ride.Advance( 0.125 );
		curve.Count = 15;
		ride.Rebuild();
		Assert.AreEqual( 3, ride.Trains.Count );
		Assert.AreNotSame( removed, ride.Trains[1] );
		Assert.AreEqual( start, retained.WheelAnimationStartSeconds );
		foreach ( var train in ride.Trains.Skip( 1 ) )
			Assert.AreEqual( 0.375, train.WheelAnimationStartSeconds, "an inactive slot starts a fresh clip when reused" );
	}

	[TestMethod]
	public void RestoredTrainsStartFreshPresentationWithoutAddingSaveFields()
	{
		var ride = Built( CoasterDefinition.FromValues( CoasterTests.PcSettings() ) );
		ride.Advance( 0.125 );
		var captured = CoasterSave.CaptureRide( ride, 3 );
		var restored = new CoasterRide( ride.Definition, new CoasterWorld( 1 ), "Restored" );
		restored.RestoreSnapshot( captured );
		Assert.AreEqual( ride.Trains.Count, restored.Trains.Count );
		Assert.IsTrue( restored.Trains.All( train => train.WheelAnimationStartSeconds == restored.TimeSeconds ), "restore initializes fresh slots at the restored controller time" );
		Assert.AreEqual( 0.0, ride.Trains[0].WheelAnimationStartSeconds, "capture did not mutate the running clip" );
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

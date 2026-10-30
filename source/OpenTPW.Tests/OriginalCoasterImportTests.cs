using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// The original coaster import (<see cref="OriginalCoasterImport"/>, tp.exe's loader <c>0x437B60</c>) on synthetic Coasters blocks: coasters OpenTPW built, written
/// in the original block layout by the test-only writer that follows the decoded reader (<see cref="OriginalCoasterBlockWriter"/>), parsed by the production reader
/// and imported onto a freshly placed station; plus hand-made blocks for the shapes the import refuses. No real Theme Park World save with a coaster exists, so these
/// are synthetic: the semantics come from the traced loader, the bytes from the writer.
/// </summary>
[TestClass]
public class OriginalCoasterImportTests
{
	private const float Tick = 1f / FixedStepClock.TicksPerSecond;
	private static readonly (int X, int Y) Depart = (20, 20);

	[TestInitialize]
	public void Init() => Log = new();

	private static CoasterDefinition Definition() => CoasterDefinition.FromValues( CoasterTests.PcSettings() );

	/// <summary>A placed station (its three station nodes, no pylon), as the level's placement leaves it for the import.</summary>
	private static (CoasterBuilder Builder, CoasterRide Ride) Station( CoasterDefinition definition, CoasterWorld? world = null )
	{
		var ride = new CoasterRide( definition, world ?? new CoasterWorld( 1 ), "Test Coaster" );
		var builder = new CoasterBuilder( ride, new CoasterSite( 64, 64 ) );
		CoasterTests.PlaceStation( builder, Depart );
		return (builder, ride);
	}

	/// <summary>The shared circuit, closed and completed, with a hand-rotated and hand-tilted pylon; its trains run for a while (no rider boards).</summary>
	private static CoasterRide RunningCircuit( CoasterDefinition definition, int seconds = 20 )
	{
		var (builder, ride) = Station( definition );
		builder.Height = 40;
		foreach ( var (dx, dy) in CoasterTests.Circuit )
			Assert.AreEqual( CoasterPlaceResult.Ok, builder.Place( Depart.X + dx, Depart.Y + dy ) );
		var yaw = builder.BeginEdit( 4 )!;
		builder.EditYaw( yaw, 0.75f );
		builder.EndEdit( yaw );
		var tilt = builder.BeginEdit( 6 )!;
		builder.EditBank( tilt, 0.25f );
		builder.EditIncline( tilt, -0.125f );
		builder.EndEdit( tilt );
		Assert.AreEqual( CoasterPlaceResult.Ok, CoasterTests.CloseAndComplete( builder ) );
		ride.Command( 5, 0 );
		for ( var tick = 0; tick < seconds * FixedStepClock.TicksPerSecond; tick++ )
			ride.Advance( Tick );
		Assert.IsTrue( ride.Trains.Count >= 1, $"the completed track places trains ({ride.Trains.Count})" );
		return ride;
	}

	private static SaveCoaster RoundTrip( CoasterRide ride, ushort stationMesh = 4, Func<int, int, IReadOnlyList<int>>? riders = null )
	{
		var block = OriginalCoasterBlockWriter.Block( 1, 0, 2, OriginalCoasterBlockWriter.Coaster( ride, new( stationMesh ), riders ) );
		return OriginalCoasterBlockWriter.Parse( block, ride.Definition ).Coasters.Single();
	}

	private static (CoasterRide Ride, OriginalCoasterImport.Plan Plan) Import( SaveCoaster saved, CoasterDefinition definition, CoasterWorld? world = null )
	{
		var (_, ride) = Station( definition, world );
		var plan = OriginalCoasterImport.Map( saved, ride );
		ride.ImportOriginal( plan.Track, plan.State, plan.BreakdownState, plan.SeatsPerTrain, plan.Trains );
		return (ride, plan);
	}

	/// <summary>The import as the level runs it: the loader, then the first frame's coaster update after the load (no substep, a motion tick that takes no time).</summary>
	private static (CoasterRide Ride, OriginalCoasterImport.Plan Plan) Load( SaveCoaster saved, CoasterDefinition definition, CoasterWorld? world = null )
	{
		var (ride, plan) = Import( saved, definition, world );
		ride.UpdateFirstFrameAfterLoad();
		return (ride, plan);
	}

	[TestMethod]
	public void ImportedCompletedControllerHasEconomicEligibilityBeforeFirstMotion()
	{
		var definition = Definition();
		var original = RunningCircuit( definition, seconds: 0 );
		var (ride, _) = Import( RoundTrip( original ), definition );
		Assert.IsTrue( ride.Track.IsComplete && ride.Track.IsClosed );
		Assert.AreEqual( 0, ride.IntersectionCount );
		Assert.IsTrue( ride.CountsForParkPoints );
		Assert.IsTrue( ride.Samples.Count >= 3 && ride.AcceptsGuests, "import recomputes geometry; it does not bypass admission safety" );
		Assert.IsFalse( ride.MotionClockStarted );
	}

	private static ulong TrackHash( CoasterTrack track )
	{
		var hash = new StateHasher();
		track.AddCanonicalState( hash );
		return hash.Value;
	}

	private static ulong Hash( CoasterRide ride )
	{
		var hash = new StateHasher();
		ride.AddCanonicalState( hash );
		return hash.Value;
	}

	/// <summary>Everything the import restores or the rebuild re-derives, node by node and train by train.</summary>
	private static void AssertSameCoaster( CoasterRide expected, CoasterRide actual, bool trains = true )
	{
		Assert.AreEqual( expected.Track.Nodes.Count, actual.Track.Nodes.Count, "nodes" );
		for ( var index = 0; index < expected.Track.Nodes.Count; index++ )
		{
			var (a, b) = (expected.Track.Nodes[index], actual.Track.Nodes[index]);
			Assert.AreEqual( (a.X, a.Y, a.Height, a.T, a.LoopPylon, a.StackIndex, a.IsStation, a.IsStationEnd), (b.X, b.Y, b.Height, b.T, b.LoopPylon, b.StackIndex, b.IsStation, b.IsStationEnd), $"node {index}" );
			// the validator's invalid mark is neither saved nor recomputed by the loader, and the winch mark is the rating pass's (a track that does not run keeps the last one's)
			var stale = expected.Track.IsRunnable || (a.Flags & CoasterNodeFlags.WinchKept) != 0 ? CoasterNodeFlags.None : CoasterNodeFlags.Winch;
			Assert.AreEqual( a.Flags & ~(CoasterNodeFlags.Invalid | stale), b.Flags & ~stale, $"node {index} flags" );
			// a station node's bank is the knot pass's, which runs on a runnable track only; the loader builds the station nodes afresh
			if ( !a.IsStation || expected.Track.IsRunnable )
				Assert.AreEqual( a.Bank, b.Bank, $"node {index} bank" );
			Assert.AreEqual( a.LoopRotate, b.LoopRotate, $"node {index} loop rotate" );
			if ( (a.Flags & CoasterNodeFlags.ManualYaw) != 0 )
				Assert.AreEqual( a.Yaw, b.Yaw, $"node {index} yaw" );
			if ( (a.Flags & CoasterNodeFlags.ManualIncline) != 0 )
				Assert.AreEqual( a.Incline, b.Incline, $"node {index} incline" );
		}
		Assert.AreEqual( (expected.Track.IsClosed, expected.Track.IsComplete, expected.Track.CutAfter), (actual.Track.IsClosed, actual.Track.IsComplete, actual.Track.CutAfter), "circuit" );
		Assert.AreEqual( expected.State, actual.State, "state" );
		Assert.AreEqual( expected.BreakdownState, actual.BreakdownState, "ride mode" );
		Assert.AreEqual( expected.SeatsPerTrain, actual.SeatsPerTrain, "seats" );
		Assert.AreEqual( expected.Samples.Count, actual.Samples.Count, "samples" );
		Assert.AreEqual( expected.Rating, actual.Rating, "rating" );
		Assert.AreEqual( expected.IntersectionCount, actual.IntersectionCount, "self-intersections" );
		CollectionAssert.AreEqual( expected.WinchSections.Order().ToList(), actual.WinchSections.Order().ToList(), "winch sections" );
		Assert.AreEqual( expected.CollisionLists.Count, actual.CollisionLists.Count, "collision lists" );
		for ( var index = 0; index < expected.CollisionLists.Count; index++ )
			CollectionAssert.AreEqual( expected.CollisionLists[index].ToList(), actual.CollisionLists[index].ToList(), $"collision list {index}" );
		if ( !trains )
			return;
		Assert.AreEqual( expected.Trains.Count, actual.Trains.Count, "trains" );
		for ( var index = 0; index < expected.Trains.Count; index++ )
		{
			var (a, b) = (expected.Trains[index], actual.Trains[index]);
			Assert.AreEqual( (a.Position, a.Speed, a.Flags), (b.Position, b.Speed, b.Flags), $"train {index}" );
			CollectionAssert.AreEqual( a.Seats, b.Seats, $"train {index} seats" );
		}
	}

	[TestMethod]
	public void ARunningCircuitWrittenInTheOriginalLayoutImportsWithItsTrainsWhereTheyWere()
	{
		var definition = Definition();
		var native = RunningCircuit( definition );
		var saved = RoundTrip( native );
		Assert.AreEqual( native.Track.PylonCount, saved.Sections.Count );
		Assert.AreEqual( native.Trains.Count, saved.TrainsInService );
		var world = new CoasterWorld( 1 );
		var random = world.Random.State;
		var (imported, plan) = Import( saved, definition, world );
		AssertSameCoaster( native, imported );
		Assert.AreEqual( native.SpeedSetting, imported.SpeedSetting, "both at the constructor's 60" );
		Assert.IsFalse( imported.Initialised, "no station script holds the coaster's slot" );
		Assert.AreEqual( 0, plan.Evidence.Count, string.Join( "\n", plan.Evidence ) );
		Assert.AreEqual( 0, OriginalCoasterImport.Compare( saved, imported ).Count );
		Assert.AreEqual( random, world.Random.State, "the import draws nothing from the coaster generator" );
		Assert.AreEqual( TrackHash( native.Track ), TrackHash( imported.Track ) );
	}

	/// <summary>
	/// A loaded coaster continues as the built one does once the built one has taken the same first frame after a load: the loaded controller's first motion tick takes no
	/// time (0x43ce85), which is all that tells the loaded park from the one that kept running.
	/// </summary>
	[TestMethod]
	public void AnImportedCoasterContinuesAsTheBuiltOneDoes()
	{
		var definition = Definition();
		var native = RunningCircuit( definition );
		var (imported, _) = Load( RoundTrip( native ), definition );
		Assert.IsTrue( imported.MotionClockStarted );
		native.UpdateFirstFrameAfterLoad();
		AssertSameCoaster( native, imported );
		for ( var tick = 0; tick < 60 * FixedStepClock.TicksPerSecond; tick++ )
		{
			native.Advance( Tick );
			imported.Advance( Tick );
		}
		AssertSameCoaster( native, imported );
	}

	/// <summary>
	/// The first frame after a load runs the loaded controller's first motion tick, which takes no time (the clock word is set on the tick that finds bit 0x10 clear,
	/// 0x43ce85; the loader leaves the bit clear): the saved trains stay where they were saved, a speed above the cap is capped, one below 2.0 decays by 0.95 and one
	/// below 0.01 stops.
	/// </summary>
	[TestMethod]
	public void TheFirstFrameAfterALoadMovesNoTrainButCapsAndDecaysTheSpeeds()
	{
		var definition = Definition();
		var saved = RoundTrip( RunningCircuit( definition ) );
		Assert.IsTrue( saved.TrainsInService >= 1 );
		foreach ( var speed in new[] { 1.5f, 0.005f, 1000f } )
		{
			var slow = saved with { TrainSlots = saved.TrainSlots!.Select( ( slot, index ) => index == 0 ? slot with { Word78 = BitConverter.SingleToUInt32Bits( speed ) } : slot ).ToList() };
			var (imported, _) = Import( slow, definition );
			Assert.IsFalse( imported.MotionClockStarted, "the loader leaves bit 0x10 clear" );
			var positions = imported.Trains.Select( train => train.Position ).ToList();
			imported.UpdateFirstFrameAfterLoad();
			CollectionAssert.AreEqual( positions, imported.Trains.Select( train => train.Position ).ToList(), $"no train moved (speed {speed})" );
			var expected = speed switch { 1.5f => 1.5f * CoasterRide.Decay, 0.005f => 0f, _ => imported.SpeedCap };
			Assert.AreEqual( expected, imported.Trains[0].Speed, $"speed {speed}: capped, decayed below 2.0, stopped below 0.01" );
			Assert.AreEqual( 0d, imported.TimeSeconds, "the frame adds no simulation time" );
			Assert.IsTrue( imported.MotionClockStarted );
		}
	}

	/// <summary>
	/// The station script's speed word reaches the controller through the script loop's setter at its first pass (0x43B2B0): the track is rated at the script's cap and
	/// the trains are laid out again from train 0's last car, as the rebuild after the setter does once the load's 0x4000 is gone.
	/// </summary>
	[TestMethod]
	public void TheStationScriptsSpeedWordReratesTheImportedTrackAndLaysTheTrainsOutAgain()
	{
		var definition = Definition();
		var native = RunningCircuit( definition );
		var saved = RoundTrip( native );
		var (imported, _) = Load( saved, definition );
		native.UpdateFirstFrameAfterLoad();
		var leadCar = definition.CarCount - 1;
		var lead = imported.CarCoordinate( imported.Trains[0], leadCar );
		imported.BindImportedScript();
		Assert.IsTrue( imported.Initialised );
		Assert.AreEqual( CoasterRide.DefaultSpeedSetting, imported.SpeedSetting, "binding alone applies no speed word: the slice tail after the script's first slice does" );
		// the station script's first slice after the load: its tail passes the speed word (0x5517D7 → 0x43B2B0, OriginalObjectEffects.AfterSlice)
		imported.ApplyScriptSpeed( 50 );
		Assert.AreEqual( 50, imported.SpeedSetting );
		Assert.AreEqual( CoasterRide.SpeedCapFor( definition, 50 ), imported.SpeedCap );
		native.ApplyScriptSpeed( 50 );
		AssertSameCoaster( native, imported );
		Assert.AreEqual( lead, imported.CarCoordinate( imported.Trains[0], leadCar ), 1e-4f, "train 0 keeps its last car where it stood" );
		// The setting is the script's now: the same word again changes nothing.
		var hash = Hash( imported );
		imported.ApplyScriptSpeed( 50 );
		Assert.AreEqual( hash, Hash( imported ) );
	}

	[TestMethod]
	public void AnUnfinishedChainWithALoopPairAndAStackedPylonImportsWithoutTrains()
	{
		var definition = Definition();
		var (builder, native) = Station( definition );
		builder.Height = 30;
		Assert.AreEqual( CoasterPlaceResult.Ok, builder.Place( 24, 20 ) );
		builder.LoopMode = true;
		builder.Place( 24, 26 );
		builder.Place( 20, 28 );
		Assert.IsFalse( builder.LoopMode, "the pair ended the loop mode" );
		builder.Place( 17, 24 );
		builder.Place( 20, 20 );
		builder.Place( 24, 20 );
		var nodes = native.Track.Nodes;
		Assert.AreEqual( CoasterNodeFlags.Loop | CoasterNodeFlags.LoopMark | CoasterNodeFlags.LoopFirst, nodes[4].Flags & ~CoasterNodeFlags.Invalid );
		Assert.AreEqual( CoasterNodeFlags.Loop | CoasterNodeFlags.LoopMark, nodes[5].Flags & ~CoasterNodeFlags.Invalid );
		Assert.AreEqual( 1, nodes[^1].StackIndex, "the last pylon stands on the first one" );
		var edit = builder.BeginEdit( 6 )!;
		builder.EditYaw( edit, -0.5f );
		builder.EndEdit( edit );

		var saved = RoundTrip( native );
		Assert.AreEqual( 0u, saved.Flags, "an unfinished chain: neither complete nor cut" );
		Assert.IsTrue( saved.Sections[1].Wire.HasFlag( SaveCoasterSectionWire.LoopFirst ) && saved.Sections[1].IsLoop );
		Assert.IsTrue( saved.Sections[0].Wire.HasFlag( SaveCoasterSectionWire.ZeroBank ), "the lower node of the stack carries the stack pass's mark" );
		var (imported, plan) = Import( saved, definition );
		AssertSameCoaster( native, imported, trains: false );
		Assert.AreEqual( CoasterControllerState.Incomplete, imported.State );
		Assert.AreEqual( 0, imported.Trains.Count );
		Assert.AreEqual( 0f, imported.Track.Nodes[4].LoopRotate, "the pair's first node rotates 0" );
		Assert.AreEqual( 0.5f, imported.Track.Nodes[5].LoopRotate, "the second 0.5" );
		Assert.AreEqual( 0, plan.Evidence.Count, string.Join( "\n", plan.Evidence ) );
	}

	[TestMethod]
	public void ACoasterThatNeverRanIsSavedWithoutPowerAndImports()
	{
		var definition = Definition();
		var (builder, native) = Station( definition );
		builder.Height = 30;
		Assert.AreEqual( CoasterPlaceResult.Ok, builder.Place( 24, 20 ) );
		builder.Place( 20, 20 );
		// tp.exe's constructor zero-fills the controller and only the first 1->2 transition sets the power factor (0x4368F0, 0x435730), so a track never completed is saved with power 0
		var saved = RoundTrip( native ) with { PowerFactor = 0f };
		Assert.AreEqual( 0x11u, saved.Controller8 & 0x7Fu, "state 1, ride mode 16" );
		var (imported, _) = Import( saved, definition );
		Assert.AreEqual( CoasterControllerState.Incomplete, imported.State );
		Assert.AreEqual( 1f, imported.PowerFactor, "OpenTPW derives the power from the mode; the first run sets 1.0 in the original too" );
	}

	[TestMethod]
	public void ACircuitCutInTheMiddleImportsCutOpen()
	{
		var definition = Definition();
		var native = RunningCircuit( definition, seconds: 0 );
		Assert.AreEqual( CoasterTrack.CutResult.Cut, new CoasterBuilder( native, native.Site ).Cut( 4, 6 ) );
		var saved = RoundTrip( native );
		Assert.AreEqual( 0x3u, saved.Flags );
		Assert.IsTrue( saved.Sections[1].Wire.HasFlag( SaveCoasterSectionWire.CutEnd | SaveCoasterSectionWire.NoNextLink ) );
		Assert.IsTrue( saved.Sections[2].Wire.HasFlag( SaveCoasterSectionWire.CutStart | SaveCoasterSectionWire.NoPreviousLink ) );
		var (imported, _) = Import( saved, definition );
		AssertSameCoaster( native, imported, trains: false );
		Assert.AreEqual( 4, imported.Track.CutAfter );
		Assert.AreEqual( 0, imported.Trains.Count );
	}

	/// <summary>A cut whose first end is the station node S2: S2 is never a section, so the block names it by default (0x437CE6, restored at 0x4382A5), and the loader marks it 0x50000.</summary>
	[TestMethod]
	public void ACutAfterTheStationsLastNodeImportsWithTheDefaultCutEnd()
	{
		var definition = Definition();
		var native = RunningCircuit( definition, seconds: 0 );
		Assert.AreEqual( CoasterTrack.CutResult.Cut, new CoasterBuilder( native, native.Site ).Cut( 2, 5 ) );
		Assert.AreEqual( 2, native.Track.CutAfter );
		var saved = RoundTrip( native );
		Assert.IsFalse( saved.Sections.Any( section => section.Wire.HasFlag( SaveCoasterSectionWire.CutEnd ) ), "no section names the cut's end" );
		var (imported, _) = Import( saved, definition );
		AssertSameCoaster( native, imported, trains: false );
		Assert.IsTrue( imported.Track.Nodes[2].Flags.HasFlag( CoasterNodeFlags.CutEnd | CoasterNodeFlags.NoNextLink ) );
	}

	[TestMethod]
	public void ABrokenDownCoasterImportsInRideMode64WithoutPower()
	{
		var definition = Definition();
		var native = RunningCircuit( definition, seconds: 5 );
		native.Command( 4, 1 );
		var saved = RoundTrip( native );
		Assert.AreEqual( 0x44u, saved.Controller8, "state 4 (open) in ride mode 64" );
		Assert.AreEqual( 0f, saved.PowerFactor );
		var (imported, _) = Import( saved, definition );
		AssertSameCoaster( native, imported );
		Assert.IsTrue( imported.IsBroken );
	}

	[TestMethod]
	public void AnImportedCoasterRoundTripsThroughTheParkResumeSave()
	{
		var definition = Definition();
		var (imported, _) = Import( RoundTrip( RunningCircuit( definition ) ), definition );
		var captured = ParkResumeJson.Deserialize<CoasterRideSaveData>( ParkResumeJson.Serialize( CoasterSave.CaptureRide( imported, attractionId: 7 ) ) );
		var (_, resumed) = Station( definition );
		resumed.RestoreSnapshot( captured );
		Assert.AreEqual( Hash( imported ), Hash( resumed ) );
		for ( var tick = 0; tick < 30 * FixedStepClock.TicksPerSecond; tick++ )
		{
			imported.Advance( Tick );
			resumed.Advance( Tick );
		}
		Assert.AreEqual( Hash( imported ), Hash( resumed ), "the continuation is deterministic" );
	}

	/// <summary>
	/// The fixed run of an imported synthetic coaster (the first frame after the load, the speed word 50, a minute of ticks) repeats exactly and ends where the built coaster
	/// ends that took the same first frame and speed word. Its digest is pinned: the coaster's curve, rating and collision pass call DetMath, not the platform's libm, so the
	/// digest is the same on every runtime (docs/DETERMINISM.md, "Cross-platform coaster pins").
	/// </summary>
	[TestMethod]
	public void AnImportedSyntheticCoasterRunsDeterministically()
	{
		(ulong Imported, ulong Native) Run()
		{
			var definition = Definition();
			var native = RunningCircuit( definition );
			var (imported, _) = Load( RoundTrip( native ), definition );
			imported.BindImportedScript();
			imported.ApplyScriptSpeed( 50 );
			native.UpdateFirstFrameAfterLoad();
			native.ApplyScriptSpeed( 50 );
			for ( var tick = 0; tick < 60 * FixedStepClock.TicksPerSecond; tick++ )
			{
				imported.Advance( Tick );
				native.Advance( Tick );
			}
			AssertSameCoaster( native, imported );
			return (Hash( imported ), Hash( native ));
		}
		var first = Run();
		Assert.AreEqual( first, Run(), "the run repeats" );
		// Pinned on every runtime since the coaster's elementary functions are DetMath's (docs/DETERMINISM.md, "Cross-platform coaster pins").
		Assert.AreEqual( "010C244879E72489", first.Imported.ToString( "X16" ), "the imported coaster's fixed run" );
	}

	[TestMethod]
	public void RidersOnTheSavedTrainsAreReportedAndNotRestored()
	{
		var definition = Definition();
		var native = RunningCircuit( definition );
		var saved = RoundTrip( native, riders: ( train, car ) => train == 0 && car == 1 ? new[] { 501, 502 } : Array.Empty<int>() );
		Assert.AreEqual( 2, saved.TrainSlots![0].Riders );
		var (imported, plan) = Import( saved, definition );
		Assert.AreEqual( 0, imported.RidersOnBoard );
		Assert.IsTrue( plan.Evidence.Any( line => line.Contains( "2 riders" ) && line.Contains( "SAVE-920" ) ), string.Join( "\n", plan.Evidence ) );
	}

	// ---- Hand-made blocks -------------------------------------------------------------------------------

	private sealed class Body
	{
		private readonly MemoryStream stream = new();
		private readonly BinaryWriter writer;
		public Body() => writer = new BinaryWriter( stream );
		public Body U8( byte value ) { writer.Write( value ); return this; }
		public Body U16( ushort value ) { writer.Write( value ); return this; }
		public Body U32( uint value ) { writer.Write( value ); return this; }
		public Body F32( float value ) { writer.Write( value ); return this; }
		public byte[] ToArray() { writer.Flush(); return stream.ToArray(); }
	}

	/// <summary>One coaster: the header, no auxiliary entry, the sections (cell, wire, t, bank, incline, yaw, loop rotate) and the type's train slots (the first <paramref name="inService"/> set up).</summary>
	private static SaveCoaster HandMade( CoasterDefinition definition, uint flags, uint state, float power, IEnumerable<(int X, int Y, uint Wire, float T, float Bank, float Incline, float Yaw, float LoopRotate)> sections,
		ushort inService = 0, ushort seats = 4, Func<int, (uint Flags, byte Cars, float Position, float Speed)>? slot = null )
	{
		var list = sections.ToList();
		var body = new Body().U32( 1 ).U32( 1 ).U32( 0 ).U32( 2 )
			.U32( flags ).U32( state ).U16( 19 ).U16( 4 ).U16( 1 ).U16( 69 ).U16( 65 ).F32( power ).U16( (ushort)list.Count ).U16( inService ).U16( seats ).U32( 0 )
			.U32( 0 );
		var index = 0;
		foreach ( var section in list )
		{
			body.U32( section.Wire ).U32( 1000u + (uint)index++ ).U16( (ushort)section.X ).U16( (ushort)section.Y ).U8( 0 )
				.F32( section.T ).F32( section.Bank ).F32( section.Incline ).F32( section.Yaw ).F32( section.LoopRotate ).U8( 0 );
		}
		for ( var train = 0; train < definition.MaxTrains; train++ )
		{
			var (trainFlags, cars, position, speed) = slot?.Invoke( train ) ?? (train < inService ? 0x7u : 0u, (byte)definition.CarCount, train * 10f, 3f);
			body.U32( trainFlags ).U8( cars ).U32( BitConverter.SingleToUInt32Bits( position ) ).U32( BitConverter.SingleToUInt32Bits( speed ) ).U32( 0 ).U32( 0 ).U32( 0 ).U16( 0 );
			for ( var car = 0; car < definition.CarCount; car++ )
				body.U32( 0 ).U8( 0 ).U8( 0 ).U8( 0 ).U32( 0 );
		}
		return OriginalCoasterBlockWriter.Parse( body.ToArray(), definition ).Coasters.Single();
	}

	private static readonly (int X, int Y, uint Wire, float T, float Bank, float Incline, float Yaw, float LoopRotate)[] Chain =
	{
		(24, 20, 0, 0.5f, 0.1f, 0.2f, 0f, 0f), (27, 24, 0, 0.5f, 0f, 0f, 0f, 0f), (24, 28, 0, 0.5f, 0f, 0f, 0f, 0f),
	};

	private static InvalidDataException Refused( SaveCoaster saved, CoasterDefinition definition ) =>
		Assert.ThrowsException<InvalidDataException>( () => OriginalCoasterImport.Map( saved, Station( definition ).Ride ) );

	[TestMethod]
	public void HandMadeShapesTheOriginalNeverLeavesAreRefused()
	{
		var definition = Definition();
		StringAssert.Contains( Refused( HandMade( definition, 0x2, 0x11, 1f, Chain ), definition ).Message, "without a complete track" );
		StringAssert.Contains( Refused( HandMade( definition, 0, 0x12, 1f, Chain ), definition ).Message, "not runnable" );
		StringAssert.Contains( Refused( HandMade( definition, 0, 0x13, 1f, Chain ), definition ).Message, "not 1, 2, 4 or 8" );
		StringAssert.Contains( Refused( HandMade( definition, 0, 0x31, 1f, Chain ), definition ).Message, "no single ride mode" );
		StringAssert.Contains( Refused( HandMade( definition, 0, 0x41, 1f, Chain ), definition ).Message, "power factor" );
		StringAssert.Contains( Refused( HandMade( definition, 0, 0x11, 1f, Chain, seats: 0 ), definition ).Message, "seats per train" );
		var link = Chain.Select( ( section, index ) => index == 1 ? section with { Wire = (uint)SaveCoasterSectionWire.NoNextLink } : section );
		StringAssert.Contains( Refused( HandMade( definition, 0, 0x11, 1f, link ), definition ).Message, "link bit without its cut marker" );
		// a station whose placement left pylons on its track is no empty controller to bind to
		var (builder, ride) = Station( definition );
		builder.Place( 24, 20 );
		StringAssert.Contains( Assert.ThrowsException<InvalidDataException>( () => OriginalCoasterImport.Map( HandMade( definition, 0, 0x11, 1f, Chain ), ride ) ).Message, "three station nodes" );
	}

	[TestMethod]
	public void TrainSlotsTheOriginalCannotHoldAreRefused()
	{
		var definition = Definition();
		var native = RunningCircuit( definition, seconds: 0 );
		var saved = RoundTrip( native );
		var bad = saved with { TrainSlots = saved.TrainSlots!.Select( ( slot, index ) => index == 0 ? slot with { Flags = 0 } : slot ).ToList() };
		StringAssert.Contains( Refused( bad, definition ).Message, "not set up" );
		bad = saved with { TrainSlots = saved.TrainSlots!.Select( ( slot, index ) => index == 0 ? slot with { CarCount = 1 } : slot ).ToList() };
		StringAssert.Contains( Refused( bad, definition ).Message, "cars" );
		bad = saved with { TrainSlots = saved.TrainSlots!.Take( 2 ).ToList() };
		StringAssert.Contains( Refused( bad, definition ).Message, "train slots" );
		bad = saved with { Controller244 = (ushort)(definition.MaxTrains + 1) };
		StringAssert.Contains( Refused( bad, definition ).Message, "trains in service" );
	}

	/// <summary>
	/// The loop mode starts only on a section with wire 0x8 and only for a coaster type whose LoopPylon control names a mesh (type flag 2, 0x42FAD0); then that node and
	/// the next are loop pylons with the pair's flags. A wire 0x10 node without the loop pair reads its last float as its bank (0x444270 on a node without 0x40).
	/// </summary>
	[TestMethod]
	public void TheLoopModeFollowsTheLoaderAndTheTypesLoopPylonMesh()
	{
		var definition = Definition();
		var pair = Chain.Select( ( section, index ) => index == 0 ? section with { Wire = 0x18, LoopRotate = 0.125f }
			: index == 1 ? section with { Wire = 0x10, LoopRotate = 0.625f } : section ).ToArray();
		var plan = OriginalCoasterImport.Map( HandMade( definition, 0, 0x11, 1f, pair ), Station( definition ).Ride );
		var nodes = plan.Track.Nodes;
		Assert.IsTrue( nodes[3].LoopPylon && nodes[4].LoopPylon && !nodes[5].LoopPylon );
		Assert.AreEqual( (uint)(CoasterNodeFlags.Loop | CoasterNodeFlags.LoopMark | CoasterNodeFlags.LoopFirst), nodes[3].Flags );
		Assert.AreEqual( (uint)(CoasterNodeFlags.Loop | CoasterNodeFlags.LoopMark), nodes[4].Flags );
		Assert.AreEqual( (0.125f, 0f, 0f), (nodes[3].LoopRotate!.Value, nodes[3].Bank, nodes[3].Incline!.Value) );
		Assert.AreEqual( 0.625f, nodes[4].LoopRotate );
		Assert.AreEqual( CoasterTrack.PylonHeight( definition.LoopPylon, 0.5f ), nodes[3].Height );

		var settings = CoasterTests.PcSettings();
		settings.Remove( "asPylonControls[1].pcMeshFilename" );
		var noMesh = CoasterDefinition.FromValues( settings );
		var standard = OriginalCoasterImport.Map( HandMade( noMesh, 0, 0x11, 1f, pair ), Station( noMesh ).Ride ).Track.Nodes;
		Assert.IsFalse( standard[3].LoopPylon || standard[4].LoopPylon, "without type flag 2 the loader never enters the loop mode" );
		Assert.AreEqual( (0u, 0.125f, 0f), (standard[3].Flags, standard[3].Bank, standard[3].Incline!.Value), "a wire 0x10 node without flag 0x40 takes the loop rotate as its bank" );
	}

	[TestMethod]
	public void TheTrackFlagsAndSurplusSetUpSlotsAreReported()
	{
		var definition = Definition();
		var native = RunningCircuit( definition, seconds: 0 );
		var block = OriginalCoasterBlockWriter.Block( 1, 0, 2, OriginalCoasterBlockWriter.Coaster( native, new( 4, TrackFlags: 0x8 ) ) );
		var saved = OriginalCoasterBlockWriter.Parse( block, definition ).Coasters.Single();
		var surplus = saved with { TrainSlots = saved.TrainSlots!.Select( ( slot, index ) => index == definition.MaxTrains - 1 ? slot with { Flags = 0x7 } : slot ).ToList() };
		var (_, plan) = Import( surplus, definition );
		Assert.IsTrue( plan.Evidence.Any( line => line.Contains( "SAVE-921" ) ), string.Join( "\n", plan.Evidence ) );
		Assert.IsTrue( plan.Evidence.Any( line => line.Contains( "still set up" ) ), string.Join( "\n", plan.Evidence ) );
	}
}

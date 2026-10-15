using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SVector3 = System.Numerics.Vector3;

namespace OpenTPW.Tests;

/// <summary>
/// The coaster's original three-node station (COAST-029, tp.exe <c>0x436ac0</c>, <c>0x435570</c>) and the controller states of the coaster tool (COAST-012,
/// <c>0x435730</c>, <c>0x447520</c>), without game data: the node flags and positions, the closing pair of the sampler, the station window, the platform links in
/// the collision pass, and what the ride does with its trains and its guests when the tool takes it out of service.
/// </summary>
[TestClass]
public class CoasterStationTests
{
	private const float Tick = 1f / FixedStepClock.TicksPerSecond;
	private const int First = CoasterTrack.StationNodeCount;

	[TestInitialize]
	public void Init() => Log = new();

	private static (CoasterBuilder Builder, CoasterRide Ride) NewCoaster( Dictionary<string, string>? settings = null, CoasterStationLayout? layout = null )
	{
		var ride = new CoasterRide( CoasterDefinition.FromValues( settings ?? CoasterTests.PcSettings() ), new CoasterWorld( 1 ), "Test Coaster" );
		var builder = new CoasterBuilder( ride, new CoasterSite( 64, 64 ) );
		if ( layout == null )
			CoasterTests.PlaceStation( builder, (20, 20) );
		else
			builder.SetStation( (19, 20), (18, 20), layout );
		return (builder, ride);
	}

	private static (CoasterBuilder Builder, CoasterRide Ride) Finished()
	{
		var (builder, ride) = NewCoaster();
		builder.Height = 40;
		foreach ( var (dx, dy) in CoasterTests.Circuit )
			Assert.AreEqual( CoasterPlaceResult.Ok, builder.Place( 20 + dx, 20 + dy ) );
		Assert.AreEqual( CoasterPlaceResult.Ok, CoasterTests.CloseAndComplete( builder ) );
		return (builder, ride);
	}

	// ---- The station's three nodes -----------------------------------------------------------------------

	[TestMethod]
	public void TheStationIsTwoStdPylonNodesAndAPylonLessOneWithTheOriginalsFlags()
	{
		var (builder, _) = NewCoaster();
		var nodes = builder.Track.Nodes;
		Assert.AreEqual( 3, nodes.Count );
		Assert.IsTrue( nodes.All( node => node.IsStation && !node.IsStationEnd ), "descriptor flag 2 on all three" );
		// S0 stands in the cell beyond the '<' cell, S2 in the cell beyond the '>' cell, S1 on the middle dummy.
		Assert.AreEqual( ((17, 20), (20, 20)), ((nodes[0].X, nodes[0].Y), (nodes[2].X, nodes[2].Y)) );
		Assert.IsFalse( nodes[0].IsPylonless );
		Assert.IsTrue( nodes[1].IsPylonless && nodes[1].ExactPosition is not null );
		Assert.IsFalse( nodes[2].IsPylonless );
		// 0x274: 0x400 | winch | 0x80000; 0x3c: 0x200 | 0x400 | winch; 0x28c: 0x200 | 0x40000.
		Assert.AreEqual( CoasterNodeFlags.StationHead | CoasterNodeFlags.Winch | CoasterNodeFlags.NoPreviousLink, nodes[0].Flags );
		Assert.AreEqual( CoasterNodeFlags.StationHead | CoasterNodeFlags.StationTail | CoasterNodeFlags.Winch, nodes[1].Flags );
		Assert.AreEqual( CoasterNodeFlags.StationTail | CoasterNodeFlags.NoNextLink, nodes[2].Flags );
		Assert.AreEqual( 0, builder.Track.PylonCount, "the station's nodes are not the player's pylons" );
		Assert.AreEqual( 0L, builder.BuildCost, "the station is paid with the station" );
		// The station's nodes cannot be picked up or have their height changed.
		for ( var index = 0; index < First; index++ )
		{
			Assert.AreEqual( CoasterPlaceResult.NotEditable, builder.Move( index, 30, 30 ) );
			Assert.IsFalse( builder.SetHeight( index, 50 ) );
		}
	}

	[TestMethod]
	public void StationPylonHeightsAreTheDummiesHeightsWithoutTheMinimumHeightClamp()
	{
		var pylon = CoasterDefinition.FromValues( CoasterTests.PcSettings() ).StandardPylon;
		pylon.MeshHeightLow = 6;
		pylon.MeshHeightHigh = 96;
		pylon.MinHeight = 10;
		Assert.AreEqual( (10f, MathF.Round( 4f / 90f, 3 )), (CoasterTrack.ClampHeight( pylon, 8 ).Height, MathF.Round( CoasterTrack.ClampHeight( pylon, 8 ).T, 3 )), "a player's pylon is clamped up to fMinHeight (raised to h0 by the model)" );
		var (height, t) = CoasterTrack.StationHeight( pylon, 8 );
		Assert.AreEqual( 8f, height, 1e-4f, "the station's creation parameters have bit 4: no clamp to fMinHeight (0x433790)" );
		Assert.AreEqual( 2f / 90f, t, 1e-5f );
		Assert.AreEqual( (6f, 0f), CoasterTrack.StationHeight( pylon, 2 ), "below h0 the loft parameter stops at 0: the pylon stands at h0" );
		Assert.AreEqual( (96f, 1f), CoasterTrack.StationHeight( pylon, 200 ) );

		// The station pylons of a track take the heights of the dummies' layout.
		var layout = new CoasterStationLayout( new CoasterStationPoint( 175, 205, 30 ), new CoasterStationPoint( 190, 205, 40 ), new CoasterStationPoint( 205, 205, 50 ) );
		var (builder, _) = NewCoaster( layout: layout );
		Assert.AreEqual( 30f, builder.Track.Nodes[0].Height, 1e-3f );
		Assert.AreEqual( 50f, builder.Track.Nodes[2].Height, 1e-3f );
		var knots = builder.Track.BuildKnots( builder.Site, builder.Track.Nodes, writeBanks: false );
		Assert.AreEqual( 30f, knots[0].Position.Y, 1e-3f );
		Assert.AreEqual( 50f, knots[2].Position.Y, 1e-3f );
		Assert.AreEqual( 40f, knots[1].Position.Y, "the middle node's knot is the exact dummy position" );
	}

	[TestMethod]
	public void TheMiddleKnotIsTheExactDummyPositionAndTheEndNodeStandsOnTheFirstKnot()
	{
		var layout = new CoasterStationLayout( new CoasterStationPoint( 175, 205, 20 ), new CoasterStationPoint( 191.5f, 205, 20 ), new CoasterStationPoint( 205, 205, 20 ) );
		var (builder, _) = NewCoaster( layout: layout );
		Assert.AreEqual( new SVector3( 191.5f, 20, 205 ), builder.Track.NodePosition( builder.Site, builder.Track.Nodes[1] ), "x is not snapped to a cell centre" );
		Assert.AreEqual( (19, 20), (builder.Track.Nodes[1].X, builder.Track.Nodes[1].Y), "its cell is ftol(x · 0.1)" );
		builder.Height = 40;
		foreach ( var (dx, dy) in CoasterTests.Circuit )
			Assert.AreEqual( CoasterPlaceResult.Ok, builder.Place( 20 + dx, 20 + dy ) );
		Assert.AreEqual( CoasterPlaceResult.Ok, CoasterTests.CloseAndComplete( builder ) );
		var nodes = builder.Track.Nodes;
		Assert.AreEqual( First + CoasterTests.Circuit.Length + 1, nodes.Count );
		Assert.IsTrue( nodes[^1].IsStationEnd && nodes[^1].IsPylonless );
		Assert.AreEqual( builder.Track.NodePosition( builder.Site, nodes[0] ), builder.Track.NodePosition( builder.Site, nodes[^1] ), "0x435570 puts the end node on the world position of S0's track dummy" );
		var knots = builder.Track.BuildKnots( builder.Site );
		Assert.AreEqual( knots[0].Position, knots[^1].Position );
		Assert.AreEqual( SVector3.Normalize( knots[1].Position - knots[0].Position ), knots[0].Direction, "S0 leaves straight toward S1 (no previous neighbour, 0x80000)" );
		Assert.AreEqual( knots[0].Direction, knots[^1].Direction, "the end node's next is S1 (0x100000), its previous is dropped (0x80000)" );
		Assert.AreEqual( SVector3.Normalize( knots[2].Position - knots[1].Position ), knots[2].Direction, "S2 leaves along the platform whatever the first pylon (no next neighbour, 0x40000)" );
	}

	/// <summary>
	/// The auto-bank pass of the knot pass (<c>0x430ed0</c>): S0 and S2, whose turn the tangent routine leaves at 0 (one neighbour is masked off), bank 0, but the
	/// running speed estimate steps through them to their raw next node, so the first pylon banks with the speed after S0, S1 and S2.
	/// </summary>
	[TestMethod]
	public void TheStationNodesBankZeroButTheSpeedEstimateRunsThroughThem()
	{
		var (builder, ride) = Finished();
		var track = builder.Track;
		var knots = track.BuildKnots( builder.Site );
		Assert.AreEqual( (0f, 0f), (knots[0].Roll, knots[2].Roll), "S0 and S2 (0x431660 one-sided: turn 0)" );
		var positions = track.Nodes.Select( node => track.NodePosition( builder.Site, node ) ).ToArray();
		var definition = ride.Definition;
		var speed = 0f;
		for ( var index = 0; index < First; index++ )
			speed = CoasterTrack.SpeedEstimate( definition, speed, definition.MinSpeed, positions[index].Y, positions[index + 1].Y );
		var turn = CoasterTrack.TurnAngleXZ( positions[First - 1], positions[First], positions[First + 1] );
		var expected = CoasterTrack.AutoBank( speed, turn, (positions[First - 1] - positions[First]).Length(), (positions[First + 1] - positions[First]).Length() );
		Assert.AreNotEqual( 0f, expected, "the first pylon of the test circuit turns" );
		Assert.AreEqual( expected, knots[First].Roll, 1e-6f, "the first pylon banks with the speed estimate after the three station nodes (0x431101)" );
	}

	[TestMethod]
	public void NoPylonStacksOnAStationPylon()
	{
		var (builder, _) = NewCoaster();
		builder.Height = 40;
		foreach ( var (x, y) in new[] { (24, 20), (24, 14), (18, 12), (14, 16) } )
			Assert.AreEqual( CoasterPlaceResult.Ok, builder.Place( x, y ) );
		// The cells of S0 and S2 pass the link and turn tests from here and meet the station flags (0x200 / 0x400) of the node standing there.
		Assert.AreEqual( CoasterPlaceResult.StackFull, builder.Check( 17, 20 ), "S0 carries node flag 0x400" );
		Assert.AreEqual( CoasterPlaceResult.StackFull, builder.Check( 20, 20 ), "S2 carries node flag 0x200" );
		Assert.AreEqual( CoasterPlaceResult.SameCell, builder.Check( 14, 16 ) );
	}

	[TestMethod]
	public void TheClosingClickIsTheHeadsCellAndTheStationEndCells()
	{
		var (builder, _) = NewCoaster();
		Assert.IsTrue( builder.Track.IsChainStart( 17, 20 ), "the cell holding the head node S0 (0x435d50)" );
		Assert.IsTrue( builder.Track.IsChainStart( 18, 20 ) && builder.Track.IsChainStart( 19, 20 ), "the station's own end cells" );
		Assert.IsFalse( builder.Track.IsChainStart( 21, 20 ) );
	}

	// ---- The sampler, the window and the collision pass ----------------------------------------------------

	[TestMethod]
	public void TheClosingPairGetsOneSampleAndTheListStartsOnTheFirstStationNode()
	{
		var (builder, ride) = Finished();
		var samples = ride.Samples;
		var tail = ride.Track.Nodes.Count - 1;
		Assert.AreEqual( 1, samples.Count( sample => sample.Section == tail ), "the closing pair E → S1 emits ONE sample (0x440c10)" );
		Assert.AreEqual( tail, samples[^1].Section );
		var knots = ride.Track.BuildKnots( builder.Site );
		Assert.AreEqual( knots[0].Position, samples[0].Position, "sample 0 is S0's knot, the start of the platform" );
		Assert.IsTrue( SVector3.Distance( samples[^1].Position, samples[0].Position ) < ride.Spacing, "the closing sample lies on S0 → S1 less than one sample past S0" );
		Assert.IsTrue( SVector3.Distance( samples[^1].Position, samples[^2].Position ) is > 2.9f and <= 3.0001f, "the walk placed it a spacing after the sample before" );
		Assert.AreEqual( (samples.Count - 1) - samples[CoasterRide.BrakingSpeedSample( samples.ToArray(), tail )].Speed / ride.Spacing, ride.BrakingStart, 1e-3f, "ctl+0x134 = (n − 1) − v/spacing" );
		// The platform: S0 at coordinate 0, S1 and S2 after it, the window from S0 to S2.
		var coordinates = ride.KnotCoordinates;
		Assert.AreEqual( 0f, coordinates[0] );
		Assert.IsTrue( coordinates[0] < coordinates[1] && coordinates[1] < coordinates[2] && coordinates[2] < coordinates[3] );
		Assert.AreEqual( (0f, coordinates[2]), ride.StationWindow );
		Assert.IsTrue( coordinates[tail] >= samples.Count - 2 && coordinates[tail] < samples.Count - 1, $"the end knot's coordinate {coordinates[tail]} lies between the last sample before it and the closing sample ({samples.Count} samples)" );
		Assert.AreEqual( 0f, ride.CarCoordinate( ride.Trains[0], ride.Trains[0].Cars.Count - 1 ), 1e-4f, "train 0's last car starts at the platform start" );
	}

	[TestMethod]
	public void ThePlatformLinksAreOrdinaryLinksOfTheCollisionPass()
	{
		// A circuit (found by search on the flat site, every pylon at height 10) whose track crosses the platform over S1 → S2.
		var (builder, ride) = NewCoaster();
		builder.Height = 10;
		foreach ( var (dx, dy) in new[] { (4, -6), (7, -7), (10, 0), (5, 4), (-1, 1), (5, -4), (8, -10), (0, -10), (-3, -10), (-5, -5), (-11, -4), (-10, 3) } )
			Assert.AreEqual( CoasterPlaceResult.Ok, builder.Place( 20 + dx, 20 + dy ), $"({dx}, {dy})" );
		Assert.AreEqual( CoasterPlaceResult.Ok, CoasterTests.CloseAndComplete( builder ) );
		Assert.IsTrue( ride.IntersectionCount > 0 );
		CollectionAssert.AreEqual( new[] { 7 }, ride.CollisionLists[1].ToArray(), "S1 → S2 is crossed by the link that leaves the seventh node (the track over the platform)" );
		Assert.IsFalse( ride.AcceptsGuests, "a track that crosses the platform is counted and refused by the guest predicate" );
		// The closing link into S0 is skipped against S0 → S1 (ordinal 0 against a pylon-less end), so an ordinary circuit counts nothing.
		Assert.AreEqual( 0, Finished().Ride.IntersectionCount );
	}

	// ---- Controller states (COAST-012) -------------------------------------------------------------------------

	[TestMethod]
	public void ACompletedCoasterIsBuiltAndItsScriptOpensIt()
	{
		var (_, ride) = NewCoaster();
		Assert.AreEqual( CoasterControllerState.Incomplete, ride.State );
		ride.Command( 5, 0 );
		Assert.AreEqual( CoasterControllerState.Incomplete, ride.State, "an unfinished track takes no open command (state & 0xA is 0)" );
		var (_, finished) = Finished();
		Assert.AreEqual( CoasterControllerState.Built, finished.State, "completion sets state 2 (0x435570)" );
		Assert.IsTrue( finished.Closed );
		Assert.IsTrue( finished.Trains.Count > 0 );
		finished.Command( 5, 1 );
		Assert.AreEqual( CoasterControllerState.Built, finished.State, "a close command needs state 4" );
		finished.Command( 5, 0 );
		Assert.AreEqual( CoasterControllerState.Open, finished.State );
		finished.Command( 5, 1 );
		Assert.AreEqual( CoasterControllerState.Closed, finished.State );
		finished.Command( 5, 0 );
		Assert.AreEqual( CoasterControllerState.Open, finished.State, "an open command needs state 2 or 8" );
	}

	[TestMethod]
	public void TheToolOnAFinishedCoasterUnloadsItsRidersAndLetsTheTrainsRunEmptyWithoutAStationStep()
	{
		var (_, ride) = Finished();
		ride.Command( 8, 0 );
		ride.Command( 5, 0 );
		ride.Command( 6, 6 );
		var guests = new[] { 101, 102, 103, 104 };
		foreach ( var guest in guests )
			ride.Board( guest );
		// Run until a train rolled through the station and seated guests, others still wait on the admission ring.
		var seated = false;
		for ( var tick = 0; tick < 240 * 60 && !seated; tick++ )
		{
			ride.Advance( Tick );
			seated = ride.RidersOnBoard > 0;
		}
		Assert.IsTrue( seated, "a train seated guests" );
		var trains = ride.Trains.Count;
		ride.Board( 105 );
		Assert.IsTrue( ride.RidersOnBoard + ride.AdmissionCount + ride.LeavingCount >= 3 );

		ride.EnterTool();
		Assert.AreEqual( CoasterControllerState.Built, ride.State, "a complete track with its circuit closed goes to state 2 (0x447520)" );
		Assert.AreEqual( 0, ride.RidersOnBoard, "4 → 2 unloads both seat lists of every train" );
		Assert.AreEqual( 0, ride.AdmissionCount, "nobody waits on a ride that does not run" );
		Assert.AreEqual( trains, ride.Trains.Count, "the trains stay on the track" );
		var off = new List<int>();
		for ( var guest = ride.Command( 3, 0 ); guest != 0; guest = ride.Command( 3, 0 ) )
			off.Add( guest );
		CollectionAssert.AreEquivalent( guests.Append( 105 ).ToArray(), off.ToArray(), "every guest the ride took leaves through COAST 3: nobody is stranded" );

		// State 2 runs the motion without the station step: the trains circulate empty and none unloads or seats.
		ride.Board( 106 );
		for ( var tick = 0; tick < 120 * 60; tick++ )
		{
			ride.Advance( Tick );
			Assert.IsTrue( ride.Trains.All( train => train.Speed > 0 && !train.Flags.HasFlag( CoasterTrainFlags.InStation ) && train.Riders == 0 ), "no station step in state 2" );
		}
		Assert.AreEqual( 1, ride.AdmissionCount, "a guest handed over meanwhile waits: nothing seats it before the ride opens" );

		// Leaving the tool opens the ride object again, and the station script's COAST 5 opens the controller.
		ride.Command( 5, 0 );
		Assert.AreEqual( CoasterControllerState.Open, ride.State );
	}

	[TestMethod]
	public void TheToolOnAnUnfinishedTrackRemovesTheTrainsAndAReopenedTrackIsState1()
	{
		var (builder, ride) = Finished();
		ride.Command( 8, 0 );
		ride.Command( 5, 0 );
		ride.Command( 6, 6 );
		ride.Board( 201 );
		Assert.IsTrue( builder.Reopen(), "no rider is on board yet" );
		Assert.AreEqual( CoasterControllerState.Incomplete, ride.State, "2 → 1 and 4 → 1: the trains are removed (0x43bf20)" );
		Assert.AreEqual( 0, ride.Trains.Count );
		Assert.AreEqual( 0, ride.AdmissionCount );
		Assert.AreEqual( 201, ride.Command( 3, 0 ), "the guest on the admission ring leaves with the trains" );
		ride.EnterTool();
		Assert.AreEqual( CoasterControllerState.Incomplete, ride.State );
		Assert.AreEqual( CoasterPlaceResult.Ok, CoasterTests.CloseAndComplete( builder ), "the player finishes the track again" );
		Assert.AreEqual( CoasterControllerState.Built, ride.State, "1 → 2 sets the trains up" );
		Assert.IsTrue( ride.Trains.Count > 0 );
		builder.Delete();
		Assert.AreEqual( CoasterControllerState.Incomplete, ride.State );
		Assert.AreEqual( 0, ride.Trains.Count );
	}

	[TestMethod]
	public void ClosingAnOpenRideMovesTheAdmissionRingAndRebuildingAKeptStateDoesNotChangeIt()
	{
		var (builder, ride) = Finished();
		ride.Command( 8, 0 );
		ride.Command( 5, 0 );
		ride.Command( 6, 6 );
		ride.Board( 301 );
		ride.Command( 5, 1 );
		Assert.AreEqual( (CoasterControllerState.Closed, 0, 1), (ride.State, ride.AdmissionCount, ride.LeavingCount), "4 → 8 runs the closing move (0x43b390)" );
		// Moving a pylon of the finished track re-samples it; the controller stays closed.
		Assert.AreEqual( CoasterPlaceResult.Ok, builder.Move( First, 25, 20 ) );
		Assert.AreEqual( CoasterControllerState.Closed, ride.State );
		ride.EnterTool();
		Assert.AreEqual( CoasterControllerState.Built, ride.State, "8 → 2" );
		Assert.AreEqual( 1, ride.LeavingCount );
	}
}

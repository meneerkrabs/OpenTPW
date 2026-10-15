using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SVector3 = System.Numerics.Vector3;

namespace OpenTPW.Tests;

/// <summary>
/// The track's self-intersection pass (tp.exe <c>0x439ab0</c> → <c>0x439c40</c> → <c>0x43a2b0</c>, COAST-004) without game data: straight links with
/// the original's collision points cross or miss, the count and the per-node lists follow the pass, and the guest predicate <c>0x441970</c> refuses
/// a coaster whose count is not 0.
/// </summary>
[TestClass]
public class CoasterCollisionTests
{
	private const float Tick = 1f / FixedStepClock.TicksPerSecond;

	/// <summary>Samples of a straight link from <paramref name="from"/> to <paramref name="to"/>, 3 units apart, for the node <paramref name="section"/>.</summary>
	private static List<CoasterSample> Straight( SVector3 from, SVector3 to, int section )
	{
		var direction = SVector3.Normalize( to - from );
		var yaw = MathF.Atan2( direction.X, direction.Z );
		var steps = (int)MathF.Ceiling( SVector3.Distance( from, to ) / 3f );
		return Enumerable.Range( 0, steps )
			.Select( step => new CoasterSample { Position = from + direction * (3f * step), Yaw = yaw, Pitch = 0, Bank = 0, Section = section } )
			.ToList();
	}

	/// <summary>Four nodes, three links: link 0 and link 2 are the pair under test, link 1 only joins them.</summary>
	private static (List<CoasterCollision.Node> Nodes, List<CoasterSample> Samples) FourNodes( SVector3[] corners )
	{
		var samples = new List<CoasterSample>();
		var nodes = new List<CoasterCollision.Node>();
		for ( var link = 0; link < 3; link++ )
		{
			nodes.Add( new CoasterCollision.Node( samples.Count, 5f, false ) );
			samples.AddRange( Straight( corners[link], corners[link + 1], link ) );
		}
		nodes.Add( new CoasterCollision.Node( samples.Count, 5f, false ) );
		samples.Add( new CoasterSample { Position = corners[3], Yaw = 0, Section = 3 } );
		return (nodes, samples);
	}

	[TestMethod]
	public void TwoLinksThatCrossAtTheSameHeightAreCountedOncePerNode()
	{
		// Link 0 runs along x through the origin at height 20; link 2 runs along z through it at the same height.
		var (nodes, samples) = FourNodes( new[] { new SVector3( -30, 20, 0 ), new SVector3( 30, 20, 0 ), new SVector3( 30, 20, -40 ), new SVector3( 0, 20, -40 ) } );
		// Link 2 is the segment from (30, -40) to (0, -40): it does not cross link 0. Move its corner so that it does.
		(nodes, samples) = FourNodes( new[] { new SVector3( -30, 20, 0 ), new SVector3( 30, 20, 0 ), new SVector3( 30, 20, -40 ), new SVector3( 0, 20, 40 ) } );
		var crossing = CoasterCollision.Run( nodes, samples );
		Assert.AreEqual( 2, crossing.Count, "one crossing puts one entry in each of the two links' first nodes (0x439fe3, 0x43a01d), the count rising by 2 (0x439ff1, 0x43a02f)" );
		CollectionAssert.AreEqual( new[] { 2 }, crossing.Lists[0].ToArray() );
		CollectionAssert.AreEqual( new[] { 0 }, crossing.Lists[2].ToArray() );
		Assert.AreEqual( 0, crossing.Lists[1].Count );
	}

	[TestMethod]
	public void LinksThatMissEachOtherOrPassAboveOneAnotherAreNotCounted()
	{
		// Far apart in plan.
		var (nodes, samples) = FourNodes( new[] { new SVector3( -30, 20, 0 ), new SVector3( 30, 20, 0 ), new SVector3( 30, 20, 60 ), new SVector3( -30, 20, 60 ) } );
		Assert.AreEqual( 0, CoasterCollision.Run( nodes, samples ).Count );
		// Crossing in plan, 40 units above (profile height 11 and the pylon radius 5 are far from it).
		(nodes, samples) = FourNodes( new[] { new SVector3( -30, 20, 0 ), new SVector3( 30, 20, 0 ), new SVector3( 30, 60, -40 ), new SVector3( 0, 60, 40 ) } );
		Assert.AreEqual( 0, CoasterCollision.Run( nodes, samples ).Count, "a fly-over is not a crossing" );
	}

	[TestMethod]
	public void LinksSharingANodeAreNotTested()
	{
		// Links 0 and 1 share node 1: the test starts two nodes away (0x439b9c, 0x439bc9).
		var (nodes, samples) = FourNodes( new[] { new SVector3( -30, 20, 0 ), new SVector3( 30, 20, 0 ), new SVector3( 0, 20, 50 ), new SVector3( 60, 20, 100 ) } );
		Assert.AreEqual( 0, CoasterCollision.Run( nodes, samples ).Count, "links 0 and 1 meet at node 1 and links 1 and 2 at node 2, which is no crossing" );
		// The same two links as links 0 and 2 (one link between them) do count when they touch.
		(nodes, samples) = FourNodes( new[] { new SVector3( -30, 20, 0 ), new SVector3( 30, 20, 0 ), new SVector3( 0, 20, 50 ), new SVector3( 0, 20, 2 ) } );
		Assert.AreEqual( 2, CoasterCollision.Run( nodes, samples ).Count );
	}

	[TestMethod]
	public void TheListsHoldEightEntriesAndOverflowSetsTheFlag()
	{
		// One long link crossed by ten short ones: the long link's first node fills its eight slots and overflows; each short link records its crossing.
		var samples = new List<CoasterSample>();
		var nodes = new List<CoasterCollision.Node>();
		nodes.Add( new CoasterCollision.Node( 0, 5f, false ) );
		samples.AddRange( Straight( new SVector3( -50, 20, 0 ), new SVector3( 50, 20, 0 ), 0 ) );
		// Connector from the long link's end into the stack of crossers.
		for ( var crosser = 0; crosser < 10; crosser++ )
		{
			var x = -45f + crosser * 10f;
			nodes.Add( new CoasterCollision.Node( samples.Count, 5f, false ) );
			samples.AddRange( Straight( new SVector3( x, 20, 20 ), new SVector3( x, 20, -20 ), nodes.Count - 1 ) );
		}
		nodes.Add( new CoasterCollision.Node( samples.Count, 5f, false ) );
		samples.Add( new CoasterSample { Position = new SVector3( 0, 20, 0 ), Section = nodes.Count - 1 } );
		var result = CoasterCollision.Run( nodes, samples );
		Assert.AreEqual( CoasterCollision.ListCapacity, result.Lists[0].Count, "the long link's first node holds at most eight crossings (0x439fda)" );
		Assert.IsTrue( result.Overflow[0], "the ninth sets node flag 0x400000 (0x43a006)" );
		Assert.IsTrue( result.Count <= nodes.Count * CoasterCollision.ListCapacity );
	}

	[TestMethod]
	public void ThePoseTurnsTheProfileByRollPitchAndYawWithTheTruncatingTable()
	{
		// Zero angles: the profile (x, y, 0) at the sample position.
		var plain = CoasterCollision.Pose( CoasterCollision.DefaultPoints, new CoasterSample { Position = new SVector3( 1, 2, 3 ) }, new CoasterSample { Position = new SVector3( 1, 2, 6 ) } );
		Assert.AreEqual( 10, plain.Length );
		CollectionAssert.AreEqual( new[] { 5f, 10f, 3f }, plain[0], "the first point (4, 8) at the first sample" );
		CollectionAssert.AreEqual( new[] { 5f, 10f, 6f }, plain[5], "and at the second sample" );
		// A yaw of π/2 turns x into −z (x' = x·cos + z·sin, z' = z·cos − x·sin) with the table's truncated indices: sin = T[1024], cos = T[0]-ish.
		var turned = CoasterCollision.Pose( CoasterCollision.DefaultPoints, new CoasterSample { Yaw = MathF.PI / 2 }, new CoasterSample { Yaw = MathF.PI / 2 } );
		Assert.AreEqual( 0f, turned[0][0], 1e-2f );
		Assert.AreEqual( -4f, turned[0][2], 1e-2f );
		Assert.AreEqual( 8f, turned[0][1], 1e-3f );
	}

	[TestMethod]
	public void ADefinitionsDefaultPointsAreTheFiveOfTheOriginal()
	{
		CollectionAssert.AreEqual( new[] { (4f, 8f), (4f, 0f), (0f, -3f), (-4f, -1f), (-4f, 8f) }, CoasterCollision.DefaultPoints.ToArray(), "0x74caa0, five points (0x74cac8)" );
	}

	/// <summary>A valid, completable track that crosses itself (found by search on the flat site; every pylon at height 20).</summary>
	private static readonly (int X, int Y)[] FigureEight = { (5, -2), (0, -6), (-7, -6), (-12, -10), (-9, -14), (-5, -8), (-6, -2) };

	private static (CoasterRide Ride, CoasterBuilder Builder) Build( (int X, int Y)[] circuit, float height )
	{
		var ride = new CoasterRide( CoasterDefinition.FromValues( CoasterTests.PcSettings() ), new CoasterWorld( 1 ), "Test Coaster" );
		var builder = new CoasterBuilder( ride, new CoasterSite( 64, 64 ) );
		CoasterTests.PlaceStation( builder, (20, 20) );
		builder.Height = height;
		foreach ( var (dx, dy) in circuit )
			Assert.AreEqual( CoasterPlaceResult.Ok, builder.Place( 20 + dx, 20 + dy ) );
		Assert.AreEqual( CoasterPlaceResult.Ok, CoasterTests.CloseAndComplete( builder ) );
		return (ride, builder);
	}

	[TestMethod]
	public void ATrackThatCrossesItselfCountsTheCrossingAndGuestsRefuseTheCoaster()
	{
		var (ride, builder) = Build( FigureEight, 20 );
		Assert.AreEqual( 2, ride.IntersectionCount, "one crossing: two entries (0x439c40) counted at controller +0x140" );
		Assert.IsTrue( ride.IsOperational, "the coaster is complete" );
		Assert.IsFalse( ride.AcceptsGuests, "the guest predicate 0x441970 refuses a coaster whose +0x140 is not 0" );
		var hash = new StateHasher();
		ride.AddCanonicalState( hash );
		var plain = Build( CoasterTests.Circuit, 20 ).Ride;
		var other = new StateHasher();
		plain.AddCanonicalState( other );
		Assert.AreNotEqual( other.Value, hash.Value );
		// Taking the track apart clears the count: the crossing is gone with the samples.
		builder.Delete();
		Assert.AreEqual( 0, ride.IntersectionCount );
	}

	[TestMethod]
	public void RaisingOnePylonOverTheCrossingRemovesIt()
	{
		var (ride, builder) = Build( FigureEight, 20 );
		Assert.AreEqual( 2, ride.IntersectionCount );
		// The link that crosses is the one between the third and fourth pylons over the first link; lifting every later pylon by 60 units clears the prisms.
		for ( var index = CoasterTrack.StationNodeCount + 2; index < ride.Track.Nodes.Count - 1; index++ )
			Assert.IsTrue( builder.SetHeight( index, 80 ) );
		Assert.AreEqual( 0, ride.IntersectionCount, "the crossing is recounted on every rebuild" );
		Assert.IsTrue( ride.AcceptsGuests );
	}

	[TestMethod]
	public void AnOrdinaryCircuitHasNoCrossingAndGuestsAcceptIt()
	{
		var settings = CoasterTests.PcSettings();
		var ride = new CoasterRide( CoasterDefinition.FromValues( settings ), new CoasterWorld( 1 ), "Test Coaster" );
		var builder = new CoasterBuilder( ride, new CoasterSite( 64, 64 ) );
		CoasterTests.PlaceStation( builder, (20, 20) );
		builder.Height = 40;
		foreach ( var (dx, dy) in CoasterTests.Circuit )
			Assert.AreEqual( CoasterPlaceResult.Ok, builder.Place( 20 + dx, 20 + dy ) );
		Assert.AreEqual( CoasterPlaceResult.Ok, CoasterTests.CloseAndComplete( builder ) );
		Assert.AreEqual( 0, ride.IntersectionCount );
		Assert.IsTrue( ride.AcceptsGuests );
	}
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

using Vector3 = System.Numerics.Vector3;

/// <summary>
/// The roll of samples, slices and car frames (COAST-017, tp.exe <c>0x440c10</c>, <c>0x43e6b0</c>, <c>0x430ed0</c>): the angle of the second rail's point about the
/// first rail's tangent, plus the lerp of the nodes' tag rolls in the slice and car frames, or the swing's sample rolls on a suspended coaster.
/// </summary>
[TestClass]
public class CoasterRollTests
{
	private static readonly (int X, int Y) Depart = (20, 20);

	[TestInitialize]
	public void Init() => Log = new();

	/// <summary>A pylon model whose tags stand 3 apart along its +Z, the bank clip tilting each tag about its forward row.</summary>
	private sealed class BankedTags : ICoasterTagModel
	{
		public required CoasterPylonControl Pylon { get; init; }
		public CoasterTagTable SingleTable { get; init; } = new( 1, 3, 1 );
		public CoasterTagTable? StackTable { get; init; }

		private Matrix4x4 Frame( CoasterPylonPose pose, float along, float lift = 0 )
		{
			var turn = Matrix4x4.CreateRotationY( pose.Yaw + (pose.TurnedBack ? MathF.PI : 0) );
			var bank = Matrix4x4.CreateRotationZ( pose.Bank );
			var height = CoasterTrack.PylonHeight( Pylon, Math.Clamp( pose.Loft, 0f, 1f ) );
			var offset = Vector3.Transform( new Vector3( 0, 0, along ), turn );
			return bank * turn * Matrix4x4.CreateTranslation( new Vector3( 5, height + lift, 5 ) + offset );
		}

		public Matrix4x4[] TagMatrices( CoasterPylonPose pose, bool stackTable ) =>
			Enumerable.Range( 0, 3 ).Select( k => Frame( pose, (k - 1) * 3f ) ).ToArray();

		public Matrix4x4? TrackDummyMatrix( CoasterPylonPose pose ) => Frame( pose, 0, 0.25f );
	}

	private static (CoasterBuilder Builder, CoasterRide Ride) NewCoaster( Dictionary<string, string>? settings = null, bool tags = true )
	{
		var definition = CoasterDefinition.FromValues( settings ?? CoasterTests.PcSettings() );
		if ( tags )
			definition.StandardPylon.TagModel = new BankedTags { Pylon = definition.StandardPylon };
		var ride = new CoasterRide( definition, new CoasterWorld( 1 ), "Roll Coaster" );
		var builder = new CoasterBuilder( ride, new CoasterSite( 64, 64 ) );
		CoasterTests.PlaceStation( builder, Depart );
		return (builder, ride);
	}

	private static void BuildCircuit( CoasterBuilder builder )
	{
		builder.Height = 40;
		foreach ( var (dx, dy) in CoasterTests.Circuit )
			Assert.AreEqual( CoasterPlaceResult.Ok, builder.Place( Depart.X + dx, Depart.Y + dy ) );
		Assert.AreEqual( CoasterPlaceResult.Ok, CoasterTests.CloseAndComplete( builder ) );
	}

	/// <summary>The second rail's offset turned into the tangent's frame by −yaw about Y and −pitch about X; the roll is atan2(−x, y) of the result.</summary>
	[TestMethod]
	public void TheSecondRailsOffsetGivesTheRollInTheTangentsFrame()
	{
		var first = new Vector3( 4, 7, -2 );
		Assert.AreEqual( 0f, HermiteCoasterCurve.SecondRailRoll( first, first + Vector3.UnitY, 0f, 0f ), 1e-6f, "world up over a level, straight track" );
		Assert.AreEqual( 0f, HermiteCoasterCurve.SecondRailRoll( first, first + Vector3.UnitY, 1.1f, 0f ), 1e-3f, "a heading turns the offset about the same axis" );
		Assert.AreEqual( 0f, HermiteCoasterCurve.SecondRailRoll( first, first + Vector3.UnitY, 0.4f, 0.3f ), 1e-3f, "world up has no sideways part for a pitched track either" );
		var tilted = new Vector3( -MathF.Sin( 0.5f ), MathF.Cos( 0.5f ), 0 );
		Assert.AreEqual( 0.5f, HermiteCoasterCurve.SecondRailRoll( first, first + tilted, 0f, 0f ), 2e-3f, "an offset tilted to the left of a track along +Z rolls it by the angle" );
		var aside = new Vector3( 0, MathF.Cos( 0.5f ), -MathF.Sin( 0.5f ) );
		Assert.AreEqual( -0.5f, HermiteCoasterCurve.SecondRailRoll( first, first + aside, MathF.PI / 2, 0f ), 2e-3f, "the same tilt on a track heading along +X" );
		Assert.AreEqual( MathF.PI, MathF.Abs( HermiteCoasterCurve.SecondRailRoll( first, first - Vector3.UnitY, 0f, 0f ) ), 1e-3f, "an offset below the track is a roll of π" );
	}

	/// <summary>The second rail of a posed non-loop knot is one unit above it and of a posed loop knot two units along the tag's up row.</summary>
	[TestMethod]
	public void TheSecondRailStandsAboveEveryKnotOrAlongTheLoopTagsUpRow()
	{
		var (builder, _) = NewCoaster();
		BuildCircuit( builder );
		var pass = builder.Track.BuildKnotPass( builder.Site, writeBanks: false );
		Assert.IsTrue( pass.Knots.Length > 6 );
		foreach ( var knot in pass.Knots )
			Assert.IsTrue( Vector3.Distance( knot.Position + Vector3.UnitY, knot.SecondRailPosition ) < 1e-5f, "one unit above the knot" );

		// a knot built without a second rail takes the same default
		var bare = new CoasterKnot( new Vector3( 1, 2, 3 ), Vector3.UnitZ, 0f, 0 );
		Assert.AreEqual( new Vector3( 1, 3, 3 ), bare.SecondRailPosition );
	}

	/// <summary>A model-less loop node's second rail is (−sin a, 0, −cos a) from its position with a the node's yaw; every other model-less node's is one unit up, and no model-less node has a tag roll.</summary>
	[TestMethod]
	public void AModelLessLoopNodesSecondRailTurnsWithItsYaw()
	{
		var (builder, _) = NewCoaster( tags: false );
		builder.Height = 30;
		Assert.AreEqual( CoasterPlaceResult.Ok, builder.Place( 24, 20 ) );
		builder.LoopMode = true;
		Assert.AreEqual( CoasterPlaceResult.Ok, builder.Place( 24, 26 ) );
		Assert.AreEqual( CoasterPlaceResult.Ok, builder.Place( 20, 29 ) );
		var track = builder.Track;
		var pass = track.BuildKnotPass( builder.Site, writeBanks: false );
		var loops = 0;
		for ( var index = 0; index < track.Nodes.Count; index++ )
		{
			var node = track.Nodes[index];
			var knot = pass.Knots[pass.FirstKnot[index]];
			Assert.AreEqual( 0f, knot.TagRoll, "a model-less node's +0xe4 is never written" );
			if ( (node.Flags & CoasterNodeFlags.Loop) == 0 )
			{
				Assert.IsTrue( Vector3.Distance( knot.Position + Vector3.UnitY, knot.SecondRailPosition ) < 1e-5f, $"node {index}: one unit up" );
				continue;
			}
			loops++;
			var yaw = pass.AutomaticYaw[index];
			var expected = knot.Position + new Vector3( -CoasterTrig.PcSin( yaw ), 0, -CoasterTrig.PcCos( yaw ) );
			Assert.IsTrue( Vector3.Distance( expected, knot.SecondRailPosition ) < 1e-5f, $"node {index}: (−sin a, 0, −cos a)" );
		}
		Assert.AreEqual( 2, loops );
	}

	/// <summary>The sampler's roll is the second rail's alone: a banked track with world-up second rails has samples that roll only by chord sag, not by the node's bank.</summary>
	[TestMethod]
	public void ASampleRollIsTheSecondRailsAloneNotTheNodesBank()
	{
		var (builder, ride) = NewCoaster();
		BuildCircuit( builder );
		var pass = builder.Track.BuildKnotPass( builder.Site, writeBanks: false );
		Assert.IsTrue( pass.Banks.Any( bank => Math.Abs( bank ) > 0.002f ), $"the track banks at its pylons: {string.Join( ' ', pass.Banks )}" );
		var samples = new HermiteCoasterCurve().Sample( pass.Knots, ride.Definition.Curviness ).Samples;
		Assert.IsTrue( samples.Length > 20 );
		// the sample stands on the chord between two curve points, so the second rail's point sits a little off the sample's own vertical: a small residual roll, never the node's bank
		var largest = samples.Max( sample => Math.Abs( sample.Bank ) );
		Assert.IsTrue( largest < 0.05f, $"a level track under world-up second rails rolls by chord sag alone: {largest}" );
		var knots = pass.Knots;
		var apart = samples.Where( sample => sample.Knot + 1 < knots.Length ).Max( sample =>
			Math.Abs( sample.Bank - (knots[sample.Knot].Roll + (knots[sample.Knot + 1].Roll - knots[sample.Knot].Roll) * sample.T) ) );
		Assert.IsTrue( apart > 0.003f, $"not the interpolated bank of the knots any more: {apart}" );
	}

	/// <summary>
	/// A slice between two tags of one node takes the node's tag roll, a slice on the link to the next node the lerp of the two nodes' tag rolls by t
	/// (<c>0x43e6b0</c>: the fraction of the nodes' ordinals' lerp).
	/// </summary>
	[TestMethod]
	public void ASliceRollLerpsTheNodesTagRolls()
	{
		var (builder, ride) = NewCoaster();
		BuildCircuit( builder );
		var pass = builder.Track.BuildKnotPass( builder.Site, writeBanks: false );
		var slices = HermiteCoasterCurve.Slices( pass.Knots, ride.Definition.Curviness, closed: false );
		var tagRolls = pass.Knots.GroupBy( knot => knot.Section ).ToDictionary( group => group.Key, group => group.First().TagRoll );
		Assert.IsTrue( tagRolls.Values.Any( roll => Math.Abs( roll ) > 0.002f ), $"some node's tags are banked: {string.Join( ' ', tagRolls.Values )}" );
		var inside = 0;
		var links = 0;
		foreach ( var slice in slices )
		{
			if ( slice.InsideNode )
			{
				Assert.AreEqual( tagRolls[slice.Section], slice.Roll, 3e-3f, $"node {slice.Section}: inside a node" );
				inside++;
			}
			else
			{
				// at t = 1 the ordinals' lerp is the whole number of the next node: its fraction is 0 and the roll is the first node's (the last slice of a chain)
				var fraction = slice.T >= 1f ? 0f : slice.T;
				var expected = (1 - fraction) * tagRolls[slice.Section] + fraction * tagRolls[slice.NextSection];
				Assert.AreEqual( expected, slice.Roll, 3e-3f, $"link {slice.Section} to {slice.NextSection} at t {slice.T}" );
				links++;
			}
		}
		Assert.IsTrue( inside > 0 && links > 10 );
	}

	/// <summary>The car's roll is the slice roll of its pair at its parameter (<c>0x43c0a0</c> → <c>0x43e6b0</c>), not the lerp of the nodes' banks.</summary>
	[TestMethod]
	public void ACarFrameTakesTheSliceRoll()
	{
		var (builder, ride) = NewCoaster();
		BuildCircuit( builder );
		var pass = builder.Track.BuildKnotPass( builder.Site, writeBanks: false );
		var tagRolls = pass.Knots.GroupBy( knot => knot.Section ).ToDictionary( group => group.Key, group => group.First().TagRoll );
		var checkedCars = 0;
		for ( var coordinate = 3f; coordinate < ride.Samples.Count - 3; coordinate += 2.7f )
		{
			var pose = ride.CarPose( coordinate );
			var sample = ride.Samples[(int)coordinate];
			var low = Math.Min( tagRolls[sample.Section], tagRolls[Math.Min( sample.Section + 1, tagRolls.Count - 1 )] );
			var high = Math.Max( tagRolls[sample.Section], tagRolls[Math.Min( sample.Section + 1, tagRolls.Count - 1 )] );
			Assert.IsTrue( pose.Roll >= low - 5e-3f && pose.Roll <= high + 5e-3f, $"coordinate {coordinate}: roll {pose.Roll} within the nodes' tag rolls {low}..{high}" );
			checkedCars++;
		}
		Assert.IsTrue( checkedCars > 10 );
	}

	/// <summary>On a suspended coaster the frame roll is the swing's sample rolls at the knots' coordinates lerped by t, plus the second rail's roll.</summary>
	[TestMethod]
	public void ASuspendedFrameRollLerpsTheSampleRolls()
	{
		var samples = Enumerable.Range( 0, 10 ).Select( index => new CoasterSample { Bank = index * 0.1f } ).ToArray();
		var a = new CoasterKnot( Vector3.Zero, Vector3.UnitZ, 0f, 0, TagRoll: 9f );
		var b = new CoasterKnot( new Vector3( 0, 0, 10 ), Vector3.UnitZ, 0f, 1, TagRoll: -9f );
		var roll = HermiteCoasterCurve.FrameRoll( Vector3.Zero, Vector3.UnitY, 0f, 0f, a, b, 0.25f, samples, 2.4f, 6.9f );
		Assert.AreEqual( 0.6f * 0.25f + 0.2f * 0.75f, roll, 1e-5f, "the tag rolls play no part: sample[6]·t + sample[2]·(1 − t), the second rail adding 0" );
		var plain = HermiteCoasterCurve.FrameRoll( Vector3.Zero, Vector3.UnitY, 0f, 0f, a, b, 0.25f );
		Assert.AreEqual( 0.75f * 9f + 0.25f * -9f, plain, 1e-4f, "without the flag the nodes' tag rolls lerp by the fraction of the ordinals' lerp (t on a link)" );
		var inside = HermiteCoasterCurve.FrameRoll( Vector3.Zero, Vector3.UnitY, 0f, 0f, a, a with { Position = Vector3.UnitZ }, 0.6f );
		Assert.AreEqual( 9f, inside, 1e-5f, "a pair inside one node: its ordinal's fraction is 0, so the roll is the node's" );
	}
}

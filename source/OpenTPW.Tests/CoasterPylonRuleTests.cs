using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SVector3 = System.Numerics.Vector3;

namespace OpenTPW.Tests;

/// <summary>
/// The pylon rules of the PC node constructor and the pylon model setup without game data (tp.exe <c>0x434060</c>, <c>0x435020</c>, <c>0x433280</c>,
/// <c>0x430590</c>): loop pylons come in pairs (the first with loop rotate 0 and node flag 0x80, the second with 0.5 and 0x60), a stacked node
/// stands the pylon's stack offset above the node below, the station end node carries no loop flag and banks like a pylon, and an older save's
/// end node flags are cleared on load.
/// </summary>
[TestClass]
public class CoasterPylonRuleTests
{
	[TestInitialize]
	public void Init() => Log = new();

	private static (CoasterBuilder Builder, CoasterRide Ride) NewCoaster( Dictionary<string, string>? settings = null )
	{
		var ride = new CoasterRide( CoasterDefinition.FromValues( settings ?? CoasterTests.PcSettings() ), new CoasterWorld( 1 ), "Test Coaster" );
		var builder = new CoasterBuilder( ride, new CoasterSite( 64, 64 ) );
		builder.SetStation( (20, 20), (17, 20) );
		return (builder, ride);
	}

	[TestMethod]
	public void LoopModePlacesAPairAndLeavesTheModeAfterTheSecondPylon()
	{
		var (builder, _) = NewCoaster();
		builder.Height = 30;
		Assert.AreEqual( CoasterPlaceResult.Ok, builder.Place( 24, 20 ) );
		builder.LoopMode = true;
		Assert.AreEqual( CoasterPlaceResult.Ok, builder.Place( 24, 26 ) );
		Assert.IsTrue( builder.LoopMode, "one of the pair is left to place" );
		Assert.AreEqual( CoasterPlaceResult.Ok, builder.Place( 20, 29 ) );
		Assert.IsFalse( builder.LoopMode, "the counter (0x79c664) reached 0: the mode ends" );
		var first = builder.Track.Nodes[2];
		var second = builder.Track.Nodes[3];
		Assert.AreEqual( (CoasterNodeFlags)0xe0, first.Flags, "the first loop pylon: 0x40 loop, 0x20 and 0x80 (0x43415a)" );
		Assert.AreEqual( (CoasterNodeFlags)0x60, second.Flags, "the second: 0x60 (0x43415f)" );
		Assert.AreEqual( (0f, 0.5f), (first.LoopRotate, second.LoopRotate), "loop rotate 0 then 0.5 (0x4341ec, 0x4341f8)" );
		Assert.AreEqual( CoasterNodeFlags.None, builder.Track.Nodes[1].Flags );
		Assert.IsTrue( first.LoopPylon && second.LoopPylon );
		// A later pylon is a standard one again; entering the mode again starts a new pair.
		builder.LoopMode = true;
		Assert.AreEqual( CoasterPlaceResult.Ok, builder.Place( 17, 26 ) );
		Assert.AreEqual( (CoasterNodeFlags)0xe0, builder.Track.Nodes[4].Flags );
		builder.LoopMode = false;
		Assert.IsFalse( builder.LoopMode );
	}

	[TestMethod]
	public void BothPylonsOfALoopPairPayTheStandardLengthAndHeightRates()
	{
		var (builder, _) = NewCoaster();
		builder.Height = 10;
		builder.LoopMode = true;
		builder.Place( 24, 20 );
		builder.Place( 24, 26 );
		var first = builder.Track.NodeCost( 1 );
		var second = builder.Track.NodeCost( 2 );
		// A loop pylon not stacked on a loop pylon pays the StdPylon length and height rates (0x4b294..0x4b2b4), so the 0x80 test behind it (0x4b2b8) only matters for a stacked one.
		Assert.AreEqual( 60f + 10f * 4f + 10f * 1f, first );
		Assert.AreEqual( 60f + 10f * 6f + 10f * 1f, second );
	}

	[TestMethod]
	public void TheFirstPylonOfALoopPairMirrorsItsTangentAndKnotSides()
	{
		var previous = new SVector3( 0, 0, 0 );
		var position = new SVector3( 10, 0, 0 );
		var next = new SVector3( 20, 0, 0 );
		var plain = CoasterTrack.ForwardTangent( previous, position, next, 1.25f, 1.25f, -0.8f, loopPylon: true );
		var reversed = CoasterTrack.ForwardTangent( previous, position, next, 1.25f, 1.25f, -0.8f, loopPylon: true, loopNeighbour: false, reversed: true );
		Assert.AreEqual( plain, -reversed, "½(â − b̂) turns into ½(b̂ − â) with node flag 0x80 (0x10031A8C)" );

		var (builder, ride) = NewCoaster();
		builder.Height = 30;
		builder.Place( 24, 20 );
		builder.LoopMode = true;
		builder.Place( 24, 26 );
		builder.Place( 20, 29 );
		builder.Place( 15, 24 );
		builder.Place( 15, 18 );
		var knots = builder.Track.BuildKnots( builder.Site, builder.Track.Nodes, closed: false, writeBanks: false );
		Assert.IsTrue( knots[2].Reversed && !knots[3].Reversed && !knots[1].Reversed );
		Assert.IsTrue( knots[3].PreviousIsLoopNode && !knots[2].PreviousIsLoopNode );
	}

	[TestMethod]
	public void ALoopPairCountsAsASectionOfTheRatingsFinal()
	{
		var (builder, ride) = NewCoaster();
		builder.Height = 40;
		var circuit = CoasterTests.Circuit;
		for ( var index = 0; index < circuit.Length; index++ )
		{
			if ( index == 1 )
				builder.LoopMode = true;
			Assert.AreEqual( CoasterPlaceResult.Ok, builder.Place( 20 + circuit[index].X, 20 + circuit[index].Y ), $"pylon {index}" );
		}
		Assert.IsFalse( builder.LoopMode, "pylons 1 and 2 were the pair" );
		Assert.AreEqual( (CoasterNodeFlags)0xe0, builder.Track.Nodes[2].Flags & ~CoasterNodeFlags.Winch );
		Assert.AreEqual( (CoasterNodeFlags)0x60, builder.Track.Nodes[3].Flags & ~CoasterNodeFlags.Winch );
		Assert.AreEqual( CoasterPlaceResult.Ok, CoasterTests.CloseAndComplete( builder ) );
		Assert.AreEqual( 1, ride.Rating!.Value.CountedSections, "nodes with flags 0x40 and 0x80 are the counted sections (ORACLE F8)" );
	}

	[TestMethod]
	public void AStackedNodeStandsTheStackOffsetAboveTheNodeBelow()
	{
		(CoasterBuilder Builder, CoasterRide Ride) Build( float offset )
		{
			var settings = CoasterTests.PcSettings( stacked: 3 );
			var (builder, ride) = NewCoaster( settings );
			ride.Definition.StandardPylon.StackOffset = offset;
			builder.Height = 40;
			foreach ( var (x, y) in new[] { (24, 20), (28, 24), (24, 28), (20, 24), (24, 20) } )
				Assert.AreEqual( CoasterPlaceResult.Ok, builder.Place( x, y ) );
			return (builder, ride);
		}
		var (flat, flatRide) = Build( 0 );
		var (offset, offsetRide) = Build( 6.5f );
		var nodes = flat.Track.Nodes;
		Assert.AreEqual( 1, nodes[^1].StackIndex );
		var below = flat.Track.NodePosition( flat.Site, nodes[1] ).Y;
		var onTop = flat.Track.NodePosition( flat.Site, nodes[^1] ).Y;
		var lift = CoasterTrack.PylonHeight( flatRide.Definition.StandardPylon, nodes[^1].T );
		Assert.AreEqual( below + lift, onTop, 1e-3f, "no offset: the stacked node adds its own height" );
		Assert.AreEqual( below + 6.5f + lift, offset.Track.NodePosition( offset.Site, offset.Track.Nodes[^1] ).Y, 1e-3f, "each level adds the pylon's stack offset (0x4332c3)" );
		Assert.AreEqual( flat.Track.NodePosition( flat.Site, nodes[1] ), offset.Track.NodePosition( offset.Site, offset.Track.Nodes[1] ), "a node on the ground does not move" );
	}

	[TestMethod]
	public void TheLoftKeysMapTheHeightParameterToTheClipLikeTheNodeConstructor()
	{
		var settings = CoasterTests.PcSettings();
		var defaults = CoasterDefinition.FromValues( settings ).StandardPylon;
		Assert.AreEqual( (0f, 1f, 1f, 0f), (defaults.LoftStartCap, defaults.LoftEndCap, defaults.LoftMultiplier, defaults.LoftAdd) );
		settings["asPylonControls[0].fLoftAnimStartCap"] = "0.1";
		settings["asPylonControls[0].fLoftAnimEndCap"] = "0.9";
		settings["asPylonControls[0].fLoftAnimMultiplier"] = "0.5";
		settings["asPylonControls[0].fLoftAnimAdd"] = "0.2";
		var keyed = CoasterDefinition.FromValues( settings ).StandardPylon;
		Assert.AreEqual( (0.1f, 0.9f, 0.5f, 0.2f), (keyed.LoftStartCap, keyed.LoftEndCap, keyed.LoftMultiplier, keyed.LoftAdd), "pylon +0xd0..+0xdc, copied when nonzero (0x42ef05)" );
	}

	[TestMethod]
	public void ASaveWithTheOldEndNodeFlagsLoadsAsAFreshlyCompletedTrack()
	{
		var (builder, ride) = NewCoaster();
		builder.Height = 40;
		foreach ( var (dx, dy) in CoasterTests.Circuit )
			Assert.AreEqual( CoasterPlaceResult.Ok, builder.Place( 20 + dx, 20 + dy ) );
		Assert.AreEqual( CoasterPlaceResult.Ok, CoasterTests.CloseAndComplete( builder ) );
		var captured = CoasterSave.CaptureRide( ride, 5 );
		var oldNodes = captured.Track.Nodes.Select( node => node.IsStationEnd ? node with { Flags = 0x144, LoopRotate = null } : node with { LoopRotate = null } ).ToList();
		var old = captured with { Track = captured.Track with { Nodes = oldNodes } };
		var restored = new CoasterRide( ride.Definition, new CoasterWorld( 1 ), "Test Coaster" );
		restored.RestoreSnapshot( old );
		Assert.AreEqual( CoasterNodeFlags.None, restored.Track.Nodes[^1].Flags & ~CoasterNodeFlags.Winch, "0x144 were creation parameters, not node flags" );
		Assert.IsTrue( restored.Track.Nodes.All( node => node.LoopRotate == CoasterTrack.DefaultLoopRotate ), "no loop rotate saved: the old 0.5" );
		var hash = new StateHasher();
		restored.AddCanonicalState( hash );
		var expected = new StateHasher();
		ride.AddCanonicalState( expected );
		Assert.AreEqual( expected.Value, hash.Value, "the migrated track is the freshly completed one" );
	}
}

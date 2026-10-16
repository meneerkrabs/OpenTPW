using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

using Vector3 = System.Numerics.Vector3;

/// <summary>
/// The knot pass over a pylon model's posed track tags (COAST-015, tp.exe <c>0x430ed0</c>, <c>0x4344c0</c>, <c>0x4321c0</c>, <c>0x432d50</c>): a posed pylon gives the knots of its
/// tag table, the sampler walks them all, and the node's sample index, the station window and the closing pair follow the original's knot choices.
/// </summary>
[TestClass]
public class CoasterTagKnotTests
{
	private const float Tick = 1f / FixedStepClock.TicksPerSecond;
	private static readonly (int X, int Y) Depart = (20, 20);

	[TestInitialize]
	public void Init() => Log = new();

	/// <summary>
	/// A pylon model whose tags stand <see cref="Spacing"/> apart along the model's +Z at the loft height, turned by the rotate clip (the yaw, half a turn on for a
	/// pylon turned back), its track dummy <see cref="DummyLift"/> above the centre tag.
	/// </summary>
	private sealed class FakeTags : ICoasterTagModel
	{
		public required CoasterPylonControl Pylon { get; init; }
		public CoasterTagTable SingleTable { get; init; } = new( 1, 3, 1 );
		public CoasterTagTable? StackTable { get; init; }
		public float Spacing { get; init; } = 3;
		public float DummyLift { get; init; } = 0.25f;
		/// <summary>A bank the tag's up row takes about its forward row (the bank clip).</summary>
		public bool BankTheTags { get; init; }

		private Matrix4x4 Frame( CoasterPylonPose pose, float along, float lift = 0 )
		{
			var turn = Matrix4x4.CreateRotationY( pose.Yaw + (pose.TurnedBack ? MathF.PI : 0) );
			var bank = BankTheTags ? Matrix4x4.CreateRotationZ( pose.Bank ) : Matrix4x4.Identity;
			var height = CoasterTrack.PylonHeight( Pylon, Math.Clamp( pose.Loft, 0f, 1f ) );
			var offset = Vector3.Transform( new Vector3( 0, 0, along ), turn );
			return bank * turn * Matrix4x4.CreateTranslation( new Vector3( 5, height + lift, 5 ) + offset );
		}

		public Matrix4x4[] TagMatrices( CoasterPylonPose pose, bool stackTable )
		{
			var table = stackTable && StackTable is { } stack ? stack : SingleTable;
			return Enumerable.Range( 0, table.Count ).Select( k => Frame( pose, (k - table.Centre) * Spacing ) ).ToArray();
		}

		public Matrix4x4? TrackDummyMatrix( CoasterPylonPose pose ) => Frame( pose, 0, DummyLift );
	}

	private static (CoasterBuilder Builder, CoasterRide Ride, FakeTags Tags) NewCoaster( Dictionary<string, string>? settings = null, Func<CoasterPylonControl, FakeTags>? tags = null )
	{
		var definition = CoasterDefinition.FromValues( settings ?? CoasterTests.PcSettings() );
		var fake = tags?.Invoke( definition.StandardPylon ) ?? new FakeTags { Pylon = definition.StandardPylon };
		definition.StandardPylon.TagModel = fake;
		var ride = new CoasterRide( definition, new CoasterWorld( 1 ), "Tagged Coaster" );
		var builder = new CoasterBuilder( ride, new CoasterSite( 64, 64 ) );
		CoasterTests.PlaceStation( builder, Depart );
		return (builder, ride, fake);
	}

	private static void BuildCircuit( CoasterBuilder builder )
	{
		builder.Height = 40;
		foreach ( var (dx, dy) in CoasterTests.Circuit )
			Assert.AreEqual( CoasterPlaceResult.Ok, builder.Place( Depart.X + dx, Depart.Y + dy ) );
		Assert.AreEqual( CoasterPlaceResult.Ok, CoasterTests.CloseAndComplete( builder ) );
	}

	private static void AreClose( Vector3 expected, Vector3 actual, string message, float tolerance = 1e-3f ) =>
		Assert.IsTrue( Vector3.Distance( expected, actual ) <= tolerance, $"{message}: expected {expected}, got {actual}" );

	/// <summary>
	/// A posed pylon's knots are its tags (<c>0x430ed0</c>, <c>0x4321c0</c>): three knots per pylon node from the table's In, Centre and Out tags, the centre on the
	/// node's position, the others along the tag's forward row, every one running along that row; a pylon-less node stays one knot, and the station end node's knot
	/// is S0's posed track dummy (<c>0x435570</c>).
	/// </summary>
	[TestMethod]
	public void APosedPylonGivesTheKnotsOfItsTags()
	{
		var (builder, _, tags) = NewCoaster();
		BuildCircuit( builder );
		var track = builder.Track;
		var pass = track.BuildKnotPass( builder.Site, writeBanks: false );
		Assert.AreEqual( track.Nodes.Count, pass.FirstKnot.Length );
		var expectedKnots = track.Nodes.Sum( node => node.IsPylonless ? 1 : 3 );
		Assert.AreEqual( expectedKnots, pass.Knots.Length, "S1 and the end node are one knot each, every pylon node three" );
		for ( var index = 0; index < track.Nodes.Count; index++ )
		{
			var node = track.Nodes[index];
			Assert.IsTrue( pass.Knots.Skip( pass.FirstKnot[index] ).Take( pass.KnotCount[index] ).All( knot => knot.Section == index ), $"node {index}'s knots name it" );
			if ( node.IsPylonless )
			{
				Assert.AreEqual( 1, pass.KnotCount[index] );
				Assert.IsNull( pass.Poses[index] );
				continue;
			}
			Assert.AreEqual( 3, pass.KnotCount[index] );
			Assert.AreEqual( pass.FirstKnot[index] + 1, pass.CentreKnot[index], "the table's centre is the track dummy's tag" );
			var yaw = pass.AutomaticYaw[index];
			var forward = new Vector3( MathF.Sin( yaw ), 0, MathF.Cos( yaw ) );
			var centre = pass.Knots[pass.CentreKnot[index]];
			AreClose( pass.Positions[index], centre.Position, $"node {index}: the centre tag stands on the node" );
			AreClose( centre.Position - forward * tags.Spacing, pass.Knots[pass.FirstKnot[index]].Position, $"node {index}: the In tag" );
			AreClose( centre.Position + forward * tags.Spacing, pass.Knots[pass.LastKnot( index )].Position, $"node {index}: the Out tag" );
			foreach ( var knot in pass.Knots.Skip( pass.FirstKnot[index] ).Take( 3 ) )
				AreClose( forward, knot.Direction, $"node {index}: the knot runs along the tag's forward row" );
		}
		var end = track.Nodes.Count - 1;
		Assert.IsTrue( track.Nodes[end].IsStationEnd );
		AreClose( pass.Positions[0] + new Vector3( 0, tags.DummyLift, 0 ), pass.Knots[^1].Position, "the end node stands on S0's posed track dummy" );
		Assert.AreEqual( pass.FirstKnot[1], HermiteCoasterCurve.ClosingKnot( pass.Knots ), "the closing pair runs to S1's first knot" );
	}

	/// <summary>
	/// The sampler walks every knot (<c>0x440c10</c>, <c>0x440ad0</c>): a coordinate per knot, the station window from S2's first knot's coordinate, the node sample
	/// index from the coordinate of the node's centre knot, the samples naming the knot that starts their pair, and the car standing on the curve between them.
	/// </summary>
	[TestMethod]
	public void TheSamplerWalksEveryTagKnot()
	{
		var (builder, ride, _) = NewCoaster();
		BuildCircuit( builder );
		var pass = builder.Track.BuildKnotPass( builder.Site, writeBanks: false );
		var coordinates = ride.KnotCoordinates;
		Assert.AreEqual( pass.Knots.Length, coordinates.Count, "one coordinate per knot" );
		for ( var knot = 1; knot < coordinates.Count; knot++ )
			Assert.IsTrue( coordinates[knot] > coordinates[knot - 1], $"knot {knot} lies after knot {knot - 1}" );
		var samples = ride.Samples;
		Assert.IsTrue( samples.All( sample => pass.Knots[sample.Knot].Section == sample.Section ), "a sample's knot belongs to its section" );
		Assert.IsTrue( samples.Select( sample => sample.Knot ).Distinct().Count() > builder.Track.Nodes.Count, "the samples run on the pairs inside the pylons too" );
		var a = coordinates[pass.FirstKnot[2]];
		var k = ride.Definition.EntranceSizeInterval;
		Assert.AreEqual( (1f - k) * a + k * (a * 0.5f), ride.StationWindow.End, 1e-4f, "the window ends at S2's first knot ([[node+0x90]+4]+8)" );
		// The car's parameter runs linearly in the coordinate between the knots of its pair (0x43c0a0): at a knot's coordinate the car stands on that knot.
		for ( var knot = pass.FirstKnot[3]; knot < pass.Knots.Length - 2; knot++ )
			AreClose( pass.Knots[knot].Position, ride.CarPose( coordinates[knot] ).Position, $"the car at knot {knot}'s coordinate", 2e-3f );
		for ( var sample = 2; sample < samples.Count - 2; sample += 7 )
			AreClose( samples[sample].Position, ride.CarPose( sample ).Position, $"the car at sample {sample} stands near it on the curve (within one spacing, as on a track without tags)", HermiteCoasterCurve.Spacing );
	}

	/// <summary>
	/// A pylon the suspended coaster turns half a turn (node flag <c>0x10000000</c>, its rotate clip half a turn on) still gives knots that run along the track: the knot pass turns
	/// each tag matrix back by −π about Y before reading the direction (<c>0x4315f0</c>), which leaves the position where the turned model put it.
	/// </summary>
	[TestMethod]
	public void ATurnedBackPylonsTagsAreTurnedBack()
	{
		var settings = CoasterTests.PcSettings();
		settings["sCoasterType.bIsSuspendedCoaster"] = "1";
		var (builder, _, tags) = NewCoaster( settings );
		builder.Height = 40;
		// A wiggle: the pylon at (24, 20) turns one way, the one at (28, 24) the other.
		foreach ( var (x, y) in new[] { (24, 20), (28, 24), (32, 20), (36, 24) } )
			Assert.AreEqual( CoasterPlaceResult.Ok, builder.Place( x, y ) );
		Assert.IsTrue( builder.Ride.Definition.IsSuspended );
		var pass = builder.Track.BuildKnotPass( builder.Site, writeBanks: false );
		var turned = Enumerable.Range( 0, pass.TurnedBack.Length ).Where( index => pass.TurnedBack[index] ).ToArray();
		Assert.IsTrue( turned.Length > 0 && turned.Length < pass.TurnedBack.Length - 3, "the wiggle turns both ways: some pylons stand on the outside of a turn" );
		foreach ( var index in turned )
		{
			var yaw = pass.AutomaticYaw[index];
			var forward = new Vector3( MathF.Sin( yaw ), 0, MathF.Cos( yaw ) );
			for ( var knot = pass.FirstKnot[index]; knot <= pass.LastKnot( index ); knot++ )
				AreClose( forward, pass.Knots[knot].Direction, $"node {index}: the turned tag runs along the track" );
			AreClose( pass.Positions[index] + forward * tags.Spacing, pass.Knots[pass.FirstKnot[index]].Position, $"node {index}: the half turn put the In tag ahead" );
			Assert.IsTrue( pass.Poses[index]!.Value.TurnedBack );
		}
	}

	/// <summary>
	/// The stack pass (<c>0x432d50</c>): in a cell of two pylons the lower one is marked 0x100, banks 0 and, with a model of two tag tables (pylon flag 8), takes the stack table
	/// (pylon <c>+0x44</c>: three tags), the top one the single table (<c>+0x30</c>: one tag).
	/// </summary>
	[TestMethod]
	public void TheLowerPylonOfAStackTakesTheStackTableAndNoBank()
	{
		var (builder, _, _) = NewCoaster( CoasterTests.PcSettings( stacked: 3 ), pylon => new FakeTags { Pylon = pylon, SingleTable = new( 0, 1, 2 ), StackTable = new( 1, 3, 1 ) } );
		builder.Height = 40;
		foreach ( var (x, y) in new[] { (24, 20), (28, 24), (24, 28), (20, 24) } )
			Assert.AreEqual( CoasterPlaceResult.Ok, builder.Place( x, y ) );
		Assert.AreEqual( CoasterPlaceResult.Ok, builder.Place( 24, 20 ) );
		var track = builder.Track;
		var lower = track.Nodes.ToList().FindIndex( node => node.X == 24 && node.Y == 20 && node.StackIndex == 0 );
		var upper = track.Nodes.ToList().FindIndex( node => node.X == 24 && node.Y == 20 && node.StackIndex == 1 );
		var pass = track.BuildKnotPass( builder.Site, writeBanks: false );
		Assert.IsTrue( pass.StackTable[lower] );
		Assert.IsFalse( pass.StackTable[upper] );
		Assert.AreEqual( 3, pass.KnotCount[lower], "the stack table's In, Centre and Out tags" );
		Assert.AreEqual( 1, pass.KnotCount[upper], "the single table's centre tag" );
		Assert.AreEqual( pass.FirstKnot[lower] + 1, pass.CentreKnot[lower] );
		Assert.AreEqual( 0f, pass.Banks[lower], "node flag 0x100: the lower node of a stack does not bank" );
		Assert.AreNotEqual( 0f, pass.Banks[track.Nodes.ToList().FindIndex( node => node.X == 28 && node.Y == 24 )], "a turning pylon on its own banks" );
	}

	/// <summary>The node's tag roll (<c>+0xe4</c>): the up row of a tag the bank clip tilted, with the yaw and pitch taken out (<c>0x431570</c>, <c>0x4314f0</c>, <c>0x46f230</c>).</summary>
	[TestMethod]
	public void TheTagRollIsTheBankOfTheTagsUpRow()
	{
		var (builder, _, _) = NewCoaster( tags: pylon => new FakeTags { Pylon = pylon, BankTheTags = true } );
		BuildCircuit( builder );
		var pass = builder.Track.BuildKnotPass( builder.Site, writeBanks: false );
		var banked = 0;
		for ( var index = 0; index < pass.Banks.Length; index++ )
		{
			if ( pass.Poses[index] is not { } pose )
				continue;
			foreach ( var knot in pass.Knots.Skip( pass.FirstKnot[index] ).Take( pass.KnotCount[index] ) )
			{
				Assert.AreEqual( pose.Bank, knot.TagRoll, 2e-3f, $"node {index}: the up row's roll" );
				Assert.AreEqual( pass.Banks[index], knot.Roll, "the knot's roll stays the node bank" );
			}
			banked += pose.Bank != 0 ? 1 : 0;
		}
		Assert.IsTrue( banked > 0 );
	}

	/// <summary>
	/// A save records the knot pass its trains stood on; a save from before the tag knots (no <c>KnotPass</c>) of a coaster whose pylon has its model stands its trains again
	/// (the riders leave), one of a later pass is refused, and a coaster without the model keeps its trains.
	/// </summary>
	[TestMethod]
	public void ASaveFromBeforeTheTagKnotsLoadsWithItsTrainsPlacedAgain()
	{
		var (builder, ride, _) = NewCoaster();
		BuildCircuit( builder );
		ride.Command( 8, 0 );
		ride.Command( 5, 0 );
		ride.Board( 101 );
		for ( var tick = 0; tick < 20 * 60 && ride.RidersOnBoard == 0; tick++ )
			ride.Advance( Tick );
		Assert.AreEqual( 1, ride.RidersOnBoard );
		var captured = CoasterSave.CaptureRide( ride, attractionId: 7 );
		Assert.AreEqual( CoasterRide.KnotPassVersion, captured.KnotPass );
		CoasterRide Fresh( CoasterDefinition definition )
		{
			var fresh = new CoasterRide( definition, new CoasterWorld( 1 ), "Tagged Coaster" );
			CoasterTests.PlaceStation( new CoasterBuilder( fresh, new CoasterSite( 64, 64 ) ), Depart );
			return fresh;
		}
		var same = Fresh( ride.Definition );
		same.RestoreSnapshot( ParkResumeJson.Deserialize<CoasterRideSaveData>( ParkResumeJson.Serialize( captured ) ) );
		Assert.AreEqual( 1, same.RidersOnBoard, "a save of this pass keeps its riders aboard" );
		var old = Fresh( ride.Definition );
		old.RestoreSnapshot( captured with { KnotPass = null } );
		Assert.AreEqual( 0, old.RidersOnBoard );
		Assert.AreEqual( 1, old.LeavingCount, "the rider of the older curve leaves" );
		Assert.ThrowsException<InvalidDataException>( () => Fresh( ride.Definition ).RestoreSnapshot( captured with { KnotPass = CoasterRide.KnotPassVersion + 1 } ) );
		var plain = CoasterDefinition.FromValues( CoasterTests.PcSettings() );
		var (plainBuilder, plainRide) = (new CoasterBuilder( new CoasterRide( plain, new CoasterWorld( 1 ), "Plain" ), new CoasterSite( 64, 64 ) ), (CoasterRide?)null);
		plainRide = plainBuilder.Ride;
		CoasterTests.PlaceStation( plainBuilder, Depart );
		BuildCircuit( plainBuilder );
		var plainCapture = CoasterSave.CaptureRide( plainRide, attractionId: 8 ) with { KnotPass = null };
		var plainResumed = Fresh( plain );
		plainResumed.RestoreSnapshot( plainCapture );
		Assert.AreEqual( plainRide.Trains.Count, plainResumed.Trains.Count );
		Assert.AreEqual( plainRide.Trains[0].Position, plainResumed.Trains[0].Position, "without the model the curve did not move: the trains stay" );
	}
}

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SVector3 = System.Numerics.Vector3;

namespace OpenTPW.Tests;

/// <summary>
/// The walking bodies of the script's walk table (TOUR-011): the walk update 0x00557D80 draws each entry through the body routine 0x005580A0 (docs/TOURS.md, "The walking body").
/// </summary>
[TestClass]
[DoNotParallelize]
public class WalkerBodyTests
{
	[TestMethod]
	public void AHeadingKeepsItsSignAndMasksItsMagnitudeToThreeBits()
	{
		CollectionAssert.AreEqual( new[] { 0, 1, 7, 0, 1, -1, -7, 0, -1 }, new[] { 0, 1, 7, 8, 9, -1, -7, -8, -9 }.Select( WalkerBodies.SignedLow3 ).ToArray() );
		Assert.AreEqual( 7, WalkerBodies.SectorOf( -1 ), "a negative heading wraps through the sprite pass's low three bits" );
		Assert.AreEqual( 6, WalkerBodies.SectorOf( -2 ) );
	}

	[TestMethod]
	public void TheHeadingIsTheAngleTurnedIntoAnEighthOfATurnSector()
	{
		// trunc(0.5 + 4/pi (2.5 pi - angle)) & 7: along x (angle 0) is 10 & 7 = 2, along z (pi/2) is 8 & 7 = 0, against x (pi) is 6, along -z (-pi/2) is 12 & 7 = 4
		Assert.AreEqual( 2, WalkerBodies.HeadingOf( 10, 0 ), "along x" );
		Assert.AreEqual( 0, WalkerBodies.HeadingOf( 0, 10 ), "along z" );
		Assert.AreEqual( 6, WalkerBodies.HeadingOf( -10, 0.001f ), "against x" );
		Assert.AreEqual( 4, WalkerBodies.HeadingOf( 0, -10 ), "along -z" );
		Assert.AreEqual( 1, WalkerBodies.HeadingOf( 10, 10 ), "a diagonal (angle pi/4: 9 & 7)" );
	}

	[TestMethod]
	public void TheShareIsTheElapsedThousandthsAndAboveAThousandItIsOne()
	{
		Assert.AreEqual( 0u, WalkerBodies.Share( 100, 100, 600 ) );
		Assert.AreEqual( 500u, WalkerBodies.Share( 350, 100, 600 ) );
		Assert.AreEqual( 1000u, WalkerBodies.Share( 600, 100, 600 ) );
		Assert.AreEqual( 1200u, WalkerBodies.Share( 700, 100, 600 ) );
		Assert.AreEqual( 500, WalkerBodies.BodyShare( 500 ) );
		Assert.AreEqual( 1000, WalkerBodies.BodyShare( 1200 ), "the body routine clamps a share above 1000 to 1000" );
		Assert.AreEqual( -1000, WalkerBodies.BodyShare( unchecked( (uint)-1000 ) ), "a share that wrapped below zero is signed and not clamped" );
		Assert.AreEqual( 1000u, WalkerBodies.Share( 5, 5, 5 ), "a walk of no length is done" );
	}

	[TestMethod]
	public void TheBodyMovesAlongTheNodesByTheShare()
	{
		var from = new SVector3( 10, 20, 30 );
		var to = new SVector3( 110, -80, 30 );
		Assert.AreEqual( from.X, WalkerBodies.Between( from, to, 0 ).X, 1e-4f );
		var half = WalkerBodies.Between( from, to, 500 );
		Assert.AreEqual( 60f, half.X, 1e-3f );
		Assert.AreEqual( -30f, half.Y, 1e-3f );
		Assert.AreEqual( 30f, half.Z, 1e-3f );
		Assert.AreEqual( to.X, WalkerBodies.Between( from, to, 1000 ).X, 1e-3f );
	}

	[TestMethod]
	public void TheBodyOnBoardFollowsTheWalkAction()
	{
		foreach ( var hidden in new[] { 1, 2, 4 } )
			Assert.IsTrue( WalkerBodies.HidesOnBoard( hidden ) );
		Assert.AreEqual( (3, 8, 5, 0, 0), (WalkerBodies.Template( 3 ), WalkerBodies.Template( 5 ), WalkerBodies.Template( 6 ), WalkerBodies.Template( 0 ), WalkerBodies.Template( 7 )) );
		Assert.AreEqual( (1, 2, 0, 12, 4), (WalkerBodies.SlotOf( 1 ), WalkerBodies.SlotOf( 2 ), WalkerBodies.SlotOf( 3 ), WalkerBodies.SlotOf( 5 ), WalkerBodies.SlotOf( 8 )) );
	}

	[TestMethod]
	public void AFloatingBodyStartsAtItsNodeAndSwingsFromThere()
	{
		var (x, z, up) = WalkerBodies.FloatOffset( 42, 100000, 100000, 5000, (1000) | (2000 << 16), 0 );
		Assert.AreEqual( (0f, 0f, 0f), (x, z, up), "the envelope is 0 at the float's start" );
		var (mx, mz, mup) = WalkerBodies.FloatOffset( 42, 102500, 100000, 5000, (1000) | (2000 << 16), 0 );
		Assert.IsTrue( Math.Abs( mx ) + Math.Abs( mz ) > 0.5f, "the swing is out at half the duration" );
		Assert.IsTrue( mup > 0f, "the height is lifted by 12 + 3 sin times the envelope" );
		Assert.IsTrue( mup <= (12f + 3f) * 2f + 1e-3f, "bounded by (12 + 3) x the height word (2.0)" );
		var other = WalkerBodies.FloatOffset( 43, 102500, 100000, 5000, (1000) | (2000 << 16), 0 );
		Assert.AreNotEqual( (mx, mz), (other.X, other.Z), "each rider swings by its own id" );
	}

	[TestMethod]
	public void AFloatingHeadingDriftsByTheClockAndKeepsThreeBits()
	{
		foreach ( var clock in new uint[] { 0, 1, 299, 300, 123456, 4000000000 } )
			foreach ( var heading in new[] { -3, 0, 3, 7 } )
				Assert.IsTrue( Math.Abs( WalkerBodies.FloatHeading( 5, clock, heading ) ) <= 7 );
		Assert.AreEqual( 0, WalkerBodies.FloatHeading( 0, 0, 0 ) );
	}

	/// <summary>A placed Mayan Spinner (WALKON action 6): its table entries are drawn in each state at the nodes the script names, in the templates of the original's walk update.</summary>
	[TestMethod]
	public void ThePlacedRidesTableEntriesAreDrawnInEachStateAtTheirNodes()
	{
		if ( ObjectCatalogCorpusTests.UseOriginalData( out var previous ) == null )
			Assert.Inconclusive( "Set OPENTPW_GAME_PATH to the original game for the walk corpus." );
		try
		{
			ObjectCatalog.BonusDataRoot = null;
			var entry = ObjectCatalog.Load( "jungle" ).Entries.Single( x => x.DisplayName == "Mayan Spinner" );
			var script = new RideScriptFile( entry.FileSystem.OpenRead( entry.ScriptPath! ) );
			var walkOn = script.Instructions.First( x => x.Opcode == (ushort)Opcode.WALKON );
			int Literal( int index ) => (int)walkOn.Operands[index].Value;
			var (u1, u2, u3, u4, action) = (Literal( 1 ), Literal( 2 ), Literal( 3 ), Literal( 4 ), Literal( 5 ));
			Assert.AreEqual( 6, action );
			var runtime = new OriginalObjectRuntime( entry, new RideScriptWorld(), 1 );
			SVector3 NodeAt( int number ) => NodeWorld( runtime, runtime.FindDummyNode( 0x800, number )!.Value ).Translation;
			var table = (List<RideVisitorBridge.WalkRecord?>)typeof( RideVisitorBridge ).GetField( "walkTable", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic )!.GetValue( runtime.Visitors )!;
			var vm = runtime.Script!;
			var now = vm.TimeMilliseconds;
			var scratch = new List<RideVisitorBridge.WalkerView>();
			var poses = new Dictionary<int, BounceRiderPose>();

			void Pose( int state, double began, double ends ) 
			{
				table.Clear();
				table.Add( new RideVisitorBridge.WalkRecord( 21, u1, u2, u3, u4, action, 1, state, began, ends ) );
				poses.Clear();
				FlatRideBounceRiders.CollectWalkers( poses, runtime, node => NodeWorld( runtime, node ), scratch );
			}

			// walking on: halfway through the walk-on the body is halfway between the two nodes, in the walking template
			Pose( 1, now - 500, now + 500 );
			Assert.AreEqual( WalkerBodies.Between( NodeAt( u1 ), NodeAt( u2 ), 500 ).X, poses[21].Feet.X, 1e-3f );
			Assert.AreEqual( GuestSpriteAtlas.WalkSlot, poses[21].Slot );
			// on board: action 6 stands in the cheering template at the second node
			Pose( 2, now, now );
			AssertNear( NodeAt( u2 ), poses[21].Feet );
			Assert.AreEqual( WalkerBodies.SlotOf( 5 ), poses[21].Slot );
			// walking off: between the third and the fourth node
			Pose( 3, now - 250, now + 250 );
			Assert.AreEqual( WalkerBodies.Between( NodeAt( u3 ), NodeAt( u4 ), 500 ).X, poses[21].Feet.X, 1e-3f );
			// ready: at the fourth node
			Pose( 4, now, now );
			AssertNear( NodeAt( u4 ), poses[21].Feet );
			Assert.AreEqual( GuestSpriteAtlas.WalkSlot, poses[21].Slot );
			// a head action's body on board is not drawn
			table.Clear();
			table.Add( new RideVisitorBridge.WalkRecord( 22, u1, u2, u3, u4, 4, 1, 2, now, now ) );
			poses.Clear();
			FlatRideBounceRiders.CollectWalkers( poses, runtime, node => NodeWorld( runtime, node ), scratch );
			Assert.AreEqual( 0, poses.Count );
			runtime.Stop();
		}
		finally
		{
			FileSystem = previous!;
			ObjectCatalog.BonusDataRoot = null;
		}
	}

	private static void AssertNear( SVector3 expected, SVector3 actual ) => Assert.IsTrue( SVector3.Distance( expected, actual ) < 1e-3f, $"{expected} vs {actual}" );

	private static System.Numerics.Matrix4x4 NodeWorld( OriginalObjectRuntime runtime, int node ) => ObjectPlacement.SwapAxes( runtime.Animator.NodeTransforms[node] );
}

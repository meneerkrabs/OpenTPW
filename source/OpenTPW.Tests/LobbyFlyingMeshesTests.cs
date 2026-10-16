using System.Linq;
using NVector3 = System.Numerics.Vector3;
using OpenTPW.FrontEnd;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

[TestClass]
public class LobbyFlyingMeshesTests
{
	private static readonly LobbyFlyingMeshInfo Bats = new( "data\\lobby\\terrain", "bat", 50, new NVector3( 200, 100, 200 ), 2.5f );

	[TestMethod]
	public void TheThemeFileGivesEveryFlyingMeshItsCountBoxAndSpeed()
	{
		var definition = new LobbyDefinition();
		var island = definition.ParseTheme( "jungle", "ISLAND(0,\"data\\lobby\\terrain\",\"jun_isle\",\"jun_gate\",\"Lost Kingdom\",90.0,12.5)\r\n"
			+ "FLYINGMESH(\"data\\lobby\\terrain\",\"bfly_PINK\",10,200.0,100.0,200.0,1.5)\r\nFLYINGMESH(\"data\\lobby\\terrain\",\"bfly_YELL\",10,200.0,100.0,200.0,1.5)\r\n" )!;
		Assert.AreEqual( 2, island.Flights.Count );
		var pink = island.Flights[0];
		Assert.AreEqual( "bfly_PINK", pink.Mesh );
		Assert.AreEqual( "data\\lobby\\terrain", pink.Directory );
		Assert.AreEqual( 10, pink.Count );
		Assert.AreEqual( new NVector3( 200, 100, 200 ), pink.Extent );
		Assert.AreEqual( 1.5f, pink.Speed );
		Assert.AreEqual( "bfly_YELL", island.Flights[1].Mesh );
		CollectionAssert.AreEqual( new[] { "bfly_PINK", "bfly_YELL" }, island.FlyingMeshes.ToArray() );
	}

	[TestMethod]
	public void AGroupFliesInABoxFiftyUnitsAboveTheIslandTarget()
	{
		uint state = 99;
		var meshes = new LobbyFlyingMeshes( () => state = LobbyEffectsTests.NextDraw( state ) );
		var group = meshes.Add( Bats, new NVector3( 600, 38, 600 ) );
		Assert.AreEqual( new NVector3( 600, 88, 600 ), group.Centre );
		Assert.AreEqual( 50, group.Count );
		for ( var frame = 0; frame < 2000; frame++ )
			meshes.Step( 1.6f );
		Assert.AreEqual( 50, group.Instances.Count, "all instances spawn within the first frames" );
		foreach ( var instance in group.Instances )
		{
			Assert.AreEqual( 1f, instance.Direction.Length(), 1e-4f );
			Assert.IsTrue( System.Math.Abs( instance.Target.X - 600 ) <= 100 && System.Math.Abs( instance.Target.Y - 88 ) <= 50 && System.Math.Abs( instance.Target.Z - 600 ) <= 100, $"targets stay in the box: {instance.Target}" );
			Assert.IsTrue( NVector3.Distance( instance.Position, group.Centre ) < 400, $"flyers stay near their island: {instance.Position}" );
		}
	}

	[TestMethod]
	public void SpawningStopsForTheFrameOnADrawEndingInOne()
	{
		uint state = 5;
		var draws = 0;
		var meshes = new LobbyFlyingMeshes( () => { draws++; return state = LobbyEffectsTests.NextDraw( state ); } );
		var group = meshes.Add( Bats, NVector3.Zero );
		// Reference: replay the generator by hand.
		uint reference = 5;
		uint Next() => reference = LobbyEffectsTests.NextDraw( reference );
		var expected = 0;
		while ( expected < 50 )
		{
			for ( var axis = 0; axis < 6; axis++ )
				Next();
			expected++;
			if ( expected >= 50 || (Next() & 7) == 1 )
				break;
		}
		meshes.Step( 0f );
		Assert.AreEqual( expected, group.Instances.Count );
		Assert.AreEqual( reference, state, "every spawn takes six draws and each further spawn one more" );
	}

	[TestMethod]
	public void AnInstanceTurnsATenthOfTheWayPerStepAndMovesAtItsSpeed()
	{
		var queue = new System.Collections.Generic.Queue<uint>();
		// Position (0.25, 0.5, 0.5) of the box and target (0.75, 0.5, 0.5): along +x from x = −50 to x = 50.
		foreach ( var fraction in new[] { 0.25, 0.5, 0.5, 0.75, 0.5, 0.5 } )
			queue.Enqueue( (uint)(fraction * (1 << 30)) );
		var meshes = new LobbyFlyingMeshes( () => queue.Count > 0 ? queue.Dequeue() : 1 );
		var group = meshes.Add( Bats with { Count = 1 }, new NVector3( 0, -50, 0 ) );
		meshes.Step( 1f );
		var instance = group.Instances.Single();
		Assert.AreEqual( new NVector3( 1, 0, 0 ), instance.Direction );
		Assert.AreEqual( -50f + 2.5f, instance.Position.X, 1e-4f, "speed × step along the direction" );
		Assert.AreEqual( new NVector3( 50, 0, 0 ), instance.Target );
		instance.Target = new NVector3( instance.Position.X, 100, 0 );
		meshes.Step( 2f );
		// wanted (0, 1, 0); direction (1, 0, 0) + ((0, 1, 0) − (1, 0, 0)) × 0.2 = (0.8, 0.2, 0), normalised.
		var expected = NVector3.Normalize( new NVector3( 0.8f, 0.2f, 0 ) );
		Assert.AreEqual( expected.X, instance.Direction.X, 1e-5f );
		Assert.AreEqual( expected.Y, instance.Direction.Y, 1e-5f );
	}

	[TestMethod]
	public void AnInstanceWithinRootFiveHundredOfItsPointPicksANewOne()
	{
		var queue = new System.Collections.Generic.Queue<uint>();
		foreach ( var fraction in new[] { 0.5, 0.5, 0.5, 0.52, 0.5, 0.5, 0.0, 0.0, 0.0 } )
			queue.Enqueue( (uint)(fraction * (1 << 30)) );
		var meshes = new LobbyFlyingMeshes( () => queue.Count > 0 ? queue.Dequeue() : 1 );
		var group = meshes.Add( Bats with { Count = 1 }, new NVector3( 0, -50, 0 ) );
		meshes.Step( 1f );
		var instance = group.Instances.Single();
		Assert.AreEqual( new NVector3( -100, -50, -100 ), instance.Target, "4 units away (16 < 500): the next point is drawn at once" );
	}

	[TestMethod]
	public void TheInstanceMatrixFacesItsDirectionWithYUp()
	{
		var instance = new LobbyFlyingMeshes.Instance { Position = new NVector3( 1, 2, 3 ), Direction = new NVector3( 0, 0, 1 ) };
		var matrix = instance.Matrix;
		Assert.AreEqual( new NVector3( 1, 0, 0 ), NVector3.TransformNormal( NVector3.UnitX, matrix ) );
		Assert.AreEqual( new NVector3( 0, 1, 0 ), NVector3.TransformNormal( NVector3.UnitY, matrix ) );
		Assert.AreEqual( new NVector3( 0, 0, 1 ), NVector3.TransformNormal( NVector3.UnitZ, matrix ) );
		Assert.AreEqual( new NVector3( 1, 2, 3 ), NVector3.Transform( NVector3.Zero, matrix ) );
		instance.Direction = new NVector3( 1, 0, 0 );
		Assert.AreEqual( new NVector3( 1, 0, 0 ), NVector3.TransformNormal( NVector3.UnitZ, instance.Matrix ), "the model's +z follows the flight" );
		Assert.AreEqual( 1f, NVector3.TransformNormal( NVector3.UnitY, instance.Matrix ).Y, 1e-6f );
	}
}

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SVector3 = System.Numerics.Vector3;

namespace OpenTPW.Tests;

/// <summary>Asset-free host topology contracts, not evidence for the untraced native cut-chain sampler or descriptor skips.</summary>
[TestClass]
public class CoasterCollisionTopologyTests
{
	// The known crossing fixture in CoasterCollisionTests: only links 0 and 2 are non-adjacent.
	private static (List<CoasterCollision.Node> Nodes, List<CoasterSample> Samples) Crossing()
	{
		var corners = new[] { new SVector3( -30, 20, 0 ), new SVector3( 30, 20, 0 ), new SVector3( 30, 20, -40 ), new SVector3( 0, 20, 40 ) };
		var nodes = new List<CoasterCollision.Node>();
		var samples = new List<CoasterSample>();
		for ( var link = 0; link < 3; link++ )
		{
			nodes.Add( new CoasterCollision.Node( samples.Count, 5f, false ) );
			var direction = SVector3.Normalize( corners[link + 1] - corners[link] );
			var steps = (int)MathF.Ceiling( SVector3.Distance( corners[link], corners[link + 1] ) / 3f );
			for ( var step = 0; step < steps; step++ )
				samples.Add( new CoasterSample { Position = corners[link] + direction * (3f * step), Yaw = MathF.Atan2( direction.X, direction.Z ), Section = link } );
		}
		nodes.Add( new CoasterCollision.Node( samples.Count, 5f, false ) );
		samples.Add( new CoasterSample { Position = corners[3], Section = 3 } );
		return (nodes, samples);
	}

	[DataTestMethod]
	[DataRow( 0, 2 )]
	[DataRow( 1, 0 )]
	[DataRow( 2, 2 )]
	[DataRow( 3, 0 )]
	[DataRow( 4, 0 )]
	[DataRow( 5, 0 )]
	[DataRow( 6, 0 )]
	[DataRow( 7, 0 )]
	[DataRow( 8, 2 )]
	[DataRow( 9, 0 )]
	[DataRow( 10, 2 )]
	[DataRow( 11, 0 )]
	[DataRow( 12, 0 )]
	[DataRow( 13, 0 )]
	[DataRow( 14, 0 )]
	[DataRow( 15, 0 )]
	public void CutEndSuppressesOnlyTheLinkStartingAtThatNode( int cutMask, int expectedCount )
	{
		var (nodes, samples) = Crossing();
		// Establish the same geometry is a hit before applying topology flags.
		AssertCrossing( CoasterCollision.Run( nodes, samples, maxPasses: 1 ), 2 );
		for ( var node = 0; node < nodes.Count; node++ )
			nodes[node] = nodes[node] with { CutEnd = (cutMask & (1 << node)) != 0 };
		foreach ( var passes in new[] { 1, CoasterCollision.MaxPasses } )
			AssertCrossing( CoasterCollision.Run( nodes, samples, maxPasses: passes ), expectedCount );
	}

	[DataTestMethod]
	[DataRow( 0, 2 )]
	[DataRow( 1, 2 )]
	[DataRow( 2, 2 )]
	[DataRow( 3, 0 )]
	public void PylonlessClosingEndpointSkipsItsLinkAgainstTheFirstLink( int pylonlessNode, int expectedCount )
	{
		var (nodes, samples) = Crossing();
		AssertCrossing( CoasterCollision.Run( nodes, samples, maxPasses: 1 ), 2 );
		// Keep radii and samples unchanged: this isolates IsPylonless from radius fallback and sampling.
		nodes[pylonlessNode] = nodes[pylonlessNode] with { IsPylonless = true };
		foreach ( var passes in new[] { 1, CoasterCollision.MaxPasses } )
			AssertCrossing( CoasterCollision.Run( nodes, samples, maxPasses: passes ), expectedCount );
	}

	private static void AssertCrossing( CoasterCollision.Result result, int expectedCount )
	{
		Assert.AreEqual( expectedCount, result.Count );
		Assert.AreEqual( 4, result.Lists.Count );
		Assert.AreEqual( 4, result.Overflow.Count );
		CollectionAssert.AreEqual( expectedCount == 0 ? Array.Empty<int>() : new[] { 2 }, result.Lists[0].ToArray() );
		CollectionAssert.AreEqual( Array.Empty<int>(), result.Lists[1].ToArray() );
		CollectionAssert.AreEqual( expectedCount == 0 ? Array.Empty<int>() : new[] { 0 }, result.Lists[2].ToArray() );
		CollectionAssert.AreEqual( Array.Empty<int>(), result.Lists[3].ToArray() );
		Assert.AreEqual( result.Lists.Sum( list => list.Count ), result.Count );
		Assert.IsFalse( result.Overflow.Any( value => value ) );
	}
}

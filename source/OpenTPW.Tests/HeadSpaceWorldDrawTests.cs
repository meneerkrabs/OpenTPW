using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>The rider heads' slot draw is the original's ADDHEAD draw of the world generator (HEADS-001 retired, DET-013).</summary>
[TestClass]
public class HeadSpaceWorldDrawTests
{
	// ---- Rider heads ---------------------------------------------------------------------------------

	/// <summary>ADDHEAD takes labs(R >>> 1) % length from a world draw and draws again until it finds a free slot (tp.exe 0x00554CB5); the head space keeps no generator of its own.</summary>
	[TestMethod]
	public void AHeadSpaceOfTheWorldGeneratorTakesTheOriginalsSlotDraws()
	{
		var rng = new WorldRng( 4 );
		var mirror = new WorldRng( 4 );
		var space = RideHeadSpace.Of( new[] { 10, 11, 12 }, () => rng.NextWord() );
		var taken = new HashSet<int>();
		for ( var visitor = 1; visitor <= 3; visitor++ )
		{
			int expected;
			do
				expected = (int)((mirror.NextWord() >> 1) % 3);
			while ( taken.Contains( expected ) );
			taken.Add( expected );
			Assert.AreEqual( expected, space.Add( visitor ), $"visitor {visitor}" );
		}
		Assert.AreEqual( mirror.State, rng.State );
		Assert.AreEqual( -1, space.Add( 4 ), "no free slot draws nothing" );
		Assert.AreEqual( mirror.State, rng.State );
		var snapshot = space.CaptureSnapshot();
		Assert.IsNull( snapshot.RandomState, "the world saves the generator" );
		var again = RideHeadSpace.Of( new[] { 10, 11, 12 }, () => rng.NextWord() );
		again.RestoreSnapshot( snapshot );
		Assert.AreEqual( 3, Enumerable.Range( 0, 3 ).Count( slot => again.VisitorAt( slot ) != 0 ) );
	}

	/// <summary>An older snapshot carries the head space's own generator: a head space of the world generator takes the slot table and drops the generator.</summary>
	[TestMethod]
	public void AnOlderHeadSnapshotRestoresIntoAWorldHeadSpace()
	{
		var seeded = RideHeadSpace.Of( new[] { 10, 11 }, seed: 7 );
		seeded.Add( 5 );
		var older = seeded.CaptureSnapshot();
		Assert.IsNotNull( older.RandomState );
		var rng = new WorldRng( 1 );
		var space = RideHeadSpace.Of( new[] { 10, 11 }, () => rng.NextWord() );
		space.RestoreSnapshot( older );
		Assert.AreEqual( seeded.VisitorAt( 0 ), space.VisitorAt( 0 ) );
		Assert.AreEqual( seeded.VisitorAt( 1 ), space.VisitorAt( 1 ) );
		Assert.AreEqual( 1u, rng.State, "restoring draws nothing" );
	}
}

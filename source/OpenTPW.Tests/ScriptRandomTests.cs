using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// Equivalence proof of <see cref="ScriptRandom"/> with the seeded .NET <c>System.Random</c>: the park-resume
/// slice swaps the per-VM <c>System.Random</c> for the owned generator, so every draw sequence — across seeds,
/// bounds and mixed call shapes, and across a capture/restore — must stay bit-identical. This test is the
/// evidence the swap changes no RNG outcome; it must keep passing on every runtime change.
/// </summary>
[TestClass]
public class ScriptRandomTests
{
	/// <summary>Deterministic extra seeds: a small LCG so the set is stable across runs and machines.</summary>
	private static IEnumerable<int> Seeds()
	{
		int[] pinned = { int.MinValue, int.MinValue + 1, -987654321, -161803398, -1, 0, 1, 2, 42, 12345, 65535, 123456789, 161803398, 2147483646, int.MaxValue };
		foreach ( var seed in pinned )
			yield return seed;
		uint state = 0x20261012u;
		for ( var index = 0; index < 40; index++ )
		{
			state = state * 1664525u + 1013904223u;
			yield return (int)state;
		}
	}

	private static int[] Bounds() => new[] { 1, 2, 3, 7, 55, 1000, 65536, int.MaxValue };

	[TestMethod]
	public void NextWithoutBoundMatchesSystemRandomAcrossSeeds()
	{
		foreach ( var seed in Seeds() )
		{
			var mine = new ScriptRandom( seed );
			var theirs = new Random( seed );
			for ( var draw = 0; draw < 20_000; draw++ )
				if ( mine.Next() != theirs.Next() )
					Assert.Fail( $"Next() diverged for seed {seed} at draw {draw}." );
		}
	}

	[TestMethod]
	public void NextWithBoundMatchesSystemRandomAcrossSeedsAndBounds()
	{
		foreach ( var seed in Seeds() )
			foreach ( var bound in Bounds() )
			{
				var mine = new ScriptRandom( seed );
				var theirs = new Random( seed );
				for ( var draw = 0; draw < 5_000; draw++ )
					if ( mine.Next( bound ) != theirs.Next( bound ) )
						Assert.Fail( $"Next({bound}) diverged for seed {seed} at draw {draw}." );
			}
	}

	[TestMethod]
	public void NextWithRangeMatchesSystemRandomAcrossSeedsAndRanges()
	{
		var ranges = new (int Min, int Max)[] { (0, 1), (0, 3), (-5, 7), (-1, 1), (0, int.MaxValue), (int.MinValue, int.MinValue + 5), (int.MinValue, int.MaxValue), (int.MaxValue - 3, int.MaxValue) };
		foreach ( var seed in Seeds() )
			foreach ( var (min, max) in ranges )
			{
				var mine = new ScriptRandom( seed );
				var theirs = new Random( seed );
				for ( var draw = 0; draw < 5_000; draw++ )
					if ( mine.Next( min, max ) != theirs.Next( min, max ) )
						Assert.Fail( $"Next({min}, {max}) diverged for seed {seed} at draw {draw}." );
			}
	}

	/// <summary>The shapes the VM actually draws: NextRandom's inclusive range, FINDSCRIPTRAND's count and SPAWNCHILD's seed.</summary>
	[TestMethod]
	public void MixedVmDrawShapesMatchSystemRandomInterleaved()
	{
		foreach ( var seed in Seeds().Take( 12 ) )
		{
			var mine = new ScriptRandom( seed );
			var theirs = new Random( seed );
			var shaper = new Random( (int)((uint)seed ^ 0x5DEECE66Du) );
			for ( var draw = 0; draw < 50_000; draw++ )
			{
				int actual, expected;
				switch ( shaper.Next( 4 ) )
				{
					case 0:
						actual = mine.Next();
						expected = theirs.Next();
						break;
					case 1:
						var maximumInclusive = shaper.Next( 0, 1000 );
						actual = mine.Next( 0, maximumInclusive + 1 );
						expected = theirs.Next( 0, maximumInclusive + 1 );
						break;
					case 2:
						var scripts = shaper.Next( 1, 8 );
						actual = mine.Next( scripts );
						expected = theirs.Next( scripts );
						break;
					default:
						var min = shaper.Next( -50, 0 );
						var max = shaper.Next( 1, 60 );
						actual = mine.Next( min, max );
						expected = theirs.Next( min, max );
						break;
				}
				if ( actual != expected )
					Assert.Fail( $"mixed draws diverged for seed {seed} at draw {draw}." );
			}
		}
	}

	[TestMethod]
	public void CapturedStateContinuesTheExactUninterruptedSequence()
	{
		foreach ( var seed in Seeds().Take( 12 ) )
		{
			var interrupted = new ScriptRandom( seed );
			var reference = new Random( seed );
			for ( var draw = 0; draw < 777; draw++ )
			{
				interrupted.Next();
				reference.Next();
			}
			var state = interrupted.CaptureState();
			// The capturing generator is untouched by the copy, and a fresh generator with the same seed continues from the state.
			var resumed = new ScriptRandom( seed );
			resumed.RestoreState( state );
			for ( var draw = 0; draw < 5_000; draw++ )
			{
				var actual = resumed.Next( 0, 11 );
				var fromCaptured = interrupted.Next( 0, 11 );
				var expected = reference.Next( 0, 11 );
				if ( actual != expected || fromCaptured != expected )
					Assert.Fail( $"restored state diverged for seed {seed} at draw {draw}." );
			}
		}
	}

	[TestMethod]
	public void RestoreStateRejectsCorruptStatesWithoutChangingTheGenerator()
	{
		var generator = new ScriptRandom( 4242 );
		var good = generator.CaptureState();
		var corrupt = new[]
		{
			good with { Table = new int[55] },
			good with { Table = good.Table.Select( ( value, index ) => index == 1 ? -1 : value ).ToArray() },
			good with { Table = good.Table.Select( ( value, index ) => index == 1 ? int.MaxValue : value ).ToArray() },
			good with { Table = good.Table.Select( ( value, index ) => index == 0 ? 7 : value ).ToArray() },
			good with { Inext = 56 },
			good with { Inext = -1 },
			good with { Inextp = 99 }
		};
		foreach ( var state in corrupt )
			Assert.ThrowsException<InvalidDataException>( () => generator.RestoreState( state ), state.ToString() );
		// The failed restores left the generator on its own sequence.
		var reference = new Random( 4242 );
		for ( var draw = 0; draw < 1_000; draw++ )
			Assert.AreEqual( reference.Next(), generator.Next() );
	}

	[TestMethod]
	public void ArgumentChecksMirrorSystemRandom()
	{
		var mine = new ScriptRandom( 1 );
		Assert.ThrowsException<ArgumentOutOfRangeException>( () => mine.Next( -1 ) );
		Assert.ThrowsException<ArgumentOutOfRangeException>( () => mine.Next( 5, 3 ) );
		Assert.ThrowsException<ArgumentNullException>( () => mine.RestoreState( null! ) );
		// Degenerate but legal: a one-value range never draws surprises.
		Assert.AreEqual( 7, mine.Next( 7, 8 ) );
	}

	[TestMethod]
	public void ScriptRandomStateValidatesItsOwnShape()
	{
		var state = new ScriptRandom( 5 ).CaptureState();
		state.Validate();
		// A wrong length is rejected; an all-zero table of the right shape is structurally valid (unreachable from a
		// real generator, but the validator checks shape, not seeding).
		Assert.ThrowsException<InvalidDataException>( () => ( state with { Table = new int[7] } ).Validate() );
		var wordZero = state.Table.ToArray();
		wordZero[3] = -5;
		Assert.ThrowsException<InvalidDataException>( () => ( state with { Table = wordZero } ).Validate() );
		var deepCopy = state.DeepCopy();
		deepCopy.Table[3] = -5;
		state.Validate();
		Assert.ThrowsException<InvalidDataException>( () => deepCopy.Validate() );
	}
}

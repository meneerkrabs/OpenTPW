using System.Linq;
using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>HEADS-004: the head placement arithmetic of tp.exe 0x0044B510 replayed bit for bit on the cases the Unicorn oracle recorded (tools/ppc-analysis/oracle_x86/head_golden.py).</summary>
[TestClass]
public class HeadPlacementGoldenTests
{
	private static float[] Floats( uint[] words )
	{
		var floats = new float[words.Length];
		for ( var index = 0; index < words.Length; index++ )
			floats[index] = BitConverter.UInt32BitsToSingle( words[index] );
		return floats;
	}

	[TestMethod]
	public void TheSineTableIsTheOnesTheOracleBuilt()
	{
		var bytes = new byte[OriginalSineTable.Size * 4];
		for ( var index = 0; index < OriginalSineTable.Size; index++ )
			BitConverter.TryWriteBytes( bytes.AsSpan( index * 4 ), OriginalSineTable.Entries[index] );
		var hash = 0x811C9DC5u;
		foreach ( var value in bytes )
			hash = unchecked( (hash ^ value) * 0x01000193u );
		Assert.AreEqual( HeadPlacementGolden.TableHash, hash );
	}

	[TestMethod]
	public void EveryOracleCaseIsReproducedBitForBit()
	{
		var failures = new System.Collections.Generic.List<string>();
		var frames = 0;
		var skipped = 0;
		for ( var index = 0; index < HeadPlacementGolden.Cases.Length; index++ )
		{
			var test = HeadPlacementGolden.Cases[index];
			if ( (test.Flags & HeadPresentation.HiddenFlag) != 0 )
			{
				Assert.AreEqual( -1, test.Frame, $"case {index}" );
				continue;
			}
			var node = Floats( test.Node );
			if ( (test.Flags & HeadPresentation.MirroredFlag) != 0 )
				foreach ( var word in new[] { 0, 1, 2, 8, 9, 10 } )
					node[word] = -node[word];
			var ok = OriginalHeadMath.TryFrame( node, Floats( test.Camera ), out _, out var row, out var column, out var roll );
			if ( !ok )
			{
				// the offset is not a finite direction: the routine leaves frame 0 and takes the roll from stack words it never wrote (undefined in the original)
				skipped++;
				if ( test.Frame != 0 )
					failures.Add( $"case {index}: the search was skipped, the oracle gave frame {test.Frame}" );
				continue;
			}
			frames++;
			var rollBits = BitConverter.SingleToUInt32Bits( roll );
			if ( row * 8 + column != test.Frame || rollBits != test.Roll )
				failures.Add( $"case {index}: frame {row * 8 + column} roll {rollBits:X8}, the oracle gave frame {test.Frame} roll {test.Roll:X8}" );
		}
		Assert.IsTrue( skipped is > 0 and < 10, $"{skipped} skipped searches" );
		Assert.IsTrue( frames > 400, $"only {frames} frames were checked" );
		Assert.AreEqual( 0, failures.Count, string.Join( "\n", failures.Take( 12 ) ) + $"\n{failures.Count} of {frames} differ" );
	}
}

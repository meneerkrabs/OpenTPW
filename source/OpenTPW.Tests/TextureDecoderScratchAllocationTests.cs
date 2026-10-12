using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// Locks the quadratic scratch sizing of <see cref="TextureFile" />'s ImageDecodeState. The
/// buffers used to be sized size*cubed, which for the corpus's 256px textures allocated two
/// 64 MB lists per texture; the decode only ever touches a quadratic prefix of them. These
/// tests prove that claim by enumerating every index the decoder computes, and pin the
/// shrunk allocation with a measured byte bound (not a timing assertion).
/// </summary>
[TestClass]
public class TextureDecoderScratchAllocationTests
{
	/// <summary>
	/// Enumerates every scratch index DecodeChannel computes for a full Y/Cb/Cr/alpha decode
	/// (channel sizes S, S/2, S/2, S) and checks them against the quadratic capacities
	/// size*size and 2*size*size, mirroring the loop structure of TextureFile.Decode.cs:
	/// dequant pairs at stride 2, row sources at stride 2 (halved for half scale), row
	/// destinations at stride 2 (stride 1 for half scale), column sources with the wavelet
	/// plane offset, column destinations two rows apart.
	/// </summary>
	[TestMethod]
	public void ScratchIndicesStayWithinTheQuadraticCapacities()
	{
		foreach ( var size in new[] { 8, 16, 32, 64, 128, 256, 512 } )
		{
			foreach ( var isHalfScale in new[] { false, true } )
			{
				var dequantCapacity = size * size;
				var rowCapacity = size * size * 2;

				// Track every scratch entry any channel writes, per channel, so that
				// reads can be checked against the writing channel (no stale reuse).
				var writtenByAnyChannel = new HashSet<int>();
				var maxDequantIndex = -1;
				var maxDequantReadIndex = -1;
				var maxRowWriteIndex = -1;
				var maxRowReadIndex = -1;

				// Channel order and sizes as in TextureFile.ReadFromStream: Y (S),
				// Cb/Cr (S/2), then alpha (S, present or not - it uses the same
				// indices, so checking it unconditionally covers both files).
				foreach ( var (channelSize, isAlpha) in new[] {
					(size, false), (size / 2, false), (size / 2, false), (size, true) } )
				{
					var n = channelSize;
					var writtenByThisChannel = new HashSet<int>();

					void DequantWrite( int index )
					{
						maxDequantIndex = Math.Max( maxDequantIndex, index );
						Assert.IsTrue( index < dequantCapacity,
							$"dequant write {index} exceeds size^2={dequantCapacity} (size={size}, channel={n})" );
					}

					void DequantRead( int index )
					{
						maxDequantIndex = Math.Max( maxDequantIndex, index );
						maxDequantReadIndex = Math.Max( maxDequantReadIndex, index );
						Assert.IsTrue( index < dequantCapacity,
							$"dequant read {index} exceeds size^2={dequantCapacity} (size={size}, channel={n})" );
					}

					void RowWrite( int index )
					{
						writtenByThisChannel.Add( index );
						writtenByAnyChannel.Add( index );
						maxRowWriteIndex = Math.Max( maxRowWriteIndex, index );
						Assert.IsTrue( index < rowCapacity,
							$"row write {index} exceeds 2*size^2={rowCapacity} (size={size}, channel={n})" );
					}

					void RowRead( int index )
					{
						maxRowReadIndex = Math.Max( maxRowReadIndex, index );
						Assert.IsTrue( index < rowCapacity,
							$"row read {index} exceeds 2*size^2={rowCapacity} (size={size}, channel={n})" );
						// Every read either hits an entry this channel wrote or lands
						// in the never-written tail past size^2 whose initial zeros
						// the half-scale alpha plane depends on. Reading another
						// channel's leftovers would couple the channels' output.
						Assert.IsTrue( writtenByThisChannel.Contains( index ) || !writtenByAnyChannel.Contains( index ),
							$"row read {index} would observe another channel's leftover (size={size}, channel={n}, alpha={isAlpha})" );
					}

					// Step 1: dequant count = n*(n/2) pairs at stride 2 - exactly [0, n*n).
					var count = n * (n / 2);
					for ( var i = 0; i < count; i++ )
					{
						DequantWrite( i * 2 );
						DequantWrite( i * 2 + 1 );
					}

					// Step 2: one row transform per row, size rowTransformSize.
					var rowTransformSize = isHalfScale ? n : n / 2;
					for ( var row = 0; row < n; row++ )
					{
						var offset = row * n;
						for ( var i = 0; i < rowTransformSize; i++ )
						{
							var prev = (i == 0) ? rowTransformSize - 1 : i - 1;
							var srcPrev = isHalfScale ? prev / 2 : prev * 2;
							var srcCur = isHalfScale ? i / 2 : i * 2;
							var dst = isHalfScale ? i : i * 2;

							DequantRead( srcPrev + offset );
							DequantRead( srcPrev + 1 + offset );
							DequantRead( srcCur + offset );
							DequantRead( srcCur + 1 + offset );

							RowWrite( dst + offset );
							RowWrite( dst + 1 + offset );
						}
					}

					// Step 3: column transforms; reads come from the row buffer.
					var wOffset = isAlpha && isHalfScale ? n * n : n * (n / 2);
					for ( var col = 0; col < n; col++ )
					{
						var columnCount = n / 2;
						for ( var i = 0; i < columnCount; i++ )
						{
							var prev = (i == 0) ? columnCount - 1 : i - 1;
							var srcPrev = prev * n + col;
							var srcCur = i * n + col;

							RowRead( srcPrev );
							RowRead( srcPrev + wOffset );
							RowRead( srcCur );
							RowRead( srcCur + wOffset );
						}
					}
				}

				// The extremes the capacities must dominate, and that the sweep is
				// tight enough to matter. Step 1 always writes dequant up to
				// size*size - 1; reads reach size*size - 1 at full scale but the
				// halved source index caps them at size*size - size/2 at half
				// scale. Rows write index size*size - 1 at full scale and index
				// size*size inclusive at half scale (stride-1 destinations), and
				// the alpha wavelet plane reads up to size*size + size*size/2 - 1
				// at half scale - the index range that must stay zeros.
				Assert.AreEqual( size * size - 1, maxDequantIndex, $"dequant high-water mark for size={size}, half={isHalfScale}" );
				Assert.AreEqual( isHalfScale ? size * size - size / 2 : size * size - 1, maxDequantReadIndex,
					$"dequant read high-water mark for size={size}, half={isHalfScale}" );
				Assert.AreEqual( isHalfScale ? size * size : size * size - 1, maxRowWriteIndex,
					$"row write high-water mark for size={size}, half={isHalfScale}" );
				Assert.AreEqual( isHalfScale ? size * size + size * (size / 2) - 1 : size * size - 1, maxRowReadIndex,
					$"row read high-water mark for size={size}, half={isHalfScale}" );
			}
		}
	}

	/// <summary>
	/// Constructing the corpus's largest textures must allocate quadratic scratch. With the
	/// original size^3 lists a 256px texture allocated two 64 MB scratch buffers (about
	/// 131 MB in total); with the quadratic sizing the whole construction stays a few MB.
	/// The bound sits far from both so it cannot flake: measured allocations are exact, no
	/// timing is involved.
	/// </summary>
	[TestMethod]
	public void LargestTexturesDecodeWithinQuadraticAllocation()
	{
		AssertAllocatedBytesBelow( "square128-half-a", 4 * 1024 * 1024 );
		AssertAllocatedBytesBelow( "square256-half", 16 * 1024 * 1024 );
		AssertAllocatedBytesBelow( "square256-full", 16 * 1024 * 1024 );
		AssertAllocatedBytesBelow( "square256-half-a", 16 * 1024 * 1024 );
	}

	/// <summary>
	/// Decodes one of the pinned synthetic cases (via the shared factory, so the payload is
	/// a known-decodable corpus case) and asserts the bytes allocated on this thread during
	/// the whole TextureFile construction. Warm-up decodes first so JIT-time allocations
	/// don't pollute the measurement, then take the minimum of three runs.
	/// </summary>
	private static void AssertAllocatedBytesBelow( string caseName, long maxBytes )
	{
		var index = Array.FindIndex( TextureDecoderIndexingSyntheticTests.SynthCases, c => c.Name == caseName );
		Assert.IsTrue( index >= 0, $"{caseName} is not a synthetic corpus case" );
		var c = TextureDecoderIndexingSyntheticTests.SynthCases[index];
		var wct = TextureDecoderIndexingSyntheticTests.BuildWct( c.Flags, c.Alpha, c.Bpp, c.Ver, c.W, c.H, c.Qy, c.Qcb, c.Qcr, c.Qa,
			c.ColorLen, c.AlphaLen, 0xC0FFEEu + (uint)index * 7919u, 0x5EED00u + (uint)index * 104729u );

		TextureData Decode()
		{
			using var ms = new MemoryStream( wct );
			return new TextureFile( ms ).Data;
		}

		Decode();

		long best = long.MaxValue;
		for ( var run = 0; run < 3; run++ )
		{
			var before = GC.GetAllocatedBytesForCurrentThread();
			var data = Decode();
			var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
			best = Math.Min( best, allocated );

			Assert.AreEqual( c.W, data.Width, $"{caseName} width" );
			Assert.AreEqual( c.H, data.Height, $"{caseName} height" );
		}

		Assert.IsTrue( best < maxBytes,
			$"{caseName} allocated {best:N0} bytes during construction, above the {maxBytes:N0} byte quadratic bound " +
			"(the size^3 scratch of the original decoder allocates about 131 MB here)" );
	}
}

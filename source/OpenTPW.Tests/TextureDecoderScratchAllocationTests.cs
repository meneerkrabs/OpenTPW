using System;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// Locks the quadratic scratch sizing of <see cref="TextureFile" />'s ImageDecodeState. The
/// buffers used to be sized size*cubed, which for the corpus's 256px textures allocated two
/// 64 MB lists per texture; the decode only ever touches a quadratic prefix of them. The
/// actual decoder construction is checked with a measured byte bound, not a timing assertion.
/// </summary>
[TestClass]
public class TextureDecoderScratchAllocationTests
{
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

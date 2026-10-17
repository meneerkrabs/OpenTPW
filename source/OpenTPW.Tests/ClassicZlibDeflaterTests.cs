using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using ICSharpCode.SharpZipLib.Zip.Compression.Streams;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// The managed zlib 1.1.3 deflater (level 6, memLevel 9, windowBits 15) must reproduce the reference encoder's bytes: the expected hashes below come from the
/// reference zlib (level 6, memLevel 9) over the deterministic inputs generated here, and the original saves' compressed streams are reproduced in
/// <see cref="OriginalSaveWriterCorpusTests"/>.
/// </summary>
[TestClass]
public class ClassicZlibDeflaterTests
{
	private static readonly byte[][] Words =
	{
		"the "u8.ToArray(), "park "u8.ToArray(), "ride "u8.ToArray(), "guest "u8.ToArray(), "coaster "u8.ToArray(), "balloon "u8.ToArray(),
		new byte[] { 0, 0, 0, 0 }, new byte[] { 0xFF, 0xFF }, "queue "u8.ToArray(), "TPWS"u8.ToArray(),
	};

	/// <summary>A compressible mix of literals, back-references, runs and words from a linear congruential generator (the same one the reference script uses).</summary>
	internal static byte[] Generate( int length, int seed )
	{
		var output = new List<byte>( length + 400 );
		long x = seed;
		long Next()
		{
			x = (x * 1103515245 + 12345) & 0x7fffffff;
			return x;
		}
		while ( output.Count < length )
		{
			var value = Next();
			switch ( (value >> 16) % 4 )
			{
				case 0:
					output.Add( (byte)((value >> 8) & 0xFF) );
					break;
				case 1 when output.Count > 0:
					var distance = 1 + (int)((value >> 4) % Math.Min( output.Count, 40000 ));
					var run = 3 + (int)((value >> 20) % 60);
					for ( var index = 0; index < run; index++ )
						output.Add( output[^distance] );
					break;
				case 2:
					output.AddRange( Enumerable.Repeat( (byte)((value >> 8) & 0xFF), 4 + (int)((value >> 20) % 40) ) );
					break;
				default:
					output.AddRange( Words[(int)((value >> 8) % Words.Length)] );
					break;
			}
		}
		return output.Take( length ).ToArray();
	}

	internal static byte[] Random( int length, int seed )
	{
		var output = new byte[length];
		long x = seed;
		for ( var index = 0; index < length; index++ )
		{
			x = (x * 1103515245 + 12345) & 0x7fffffff;
			output[index] = (byte)((x >> 16) & 0xFF);
		}
		return output;
	}

	[TestMethod]
	[DataRow( "t0", 0, 1, false, 8, "aca503def43dc086a5aaa5c3d6f86bcbbc2aa6a80007ddea4a81bce2b376fceb" )]
	[DataRow( "t1", 1, 2, false, 9, "cf7a6b79503d65a736f80456bd86359ff61146cac51bed33dd8ad9dec60dfe5b" )]
	[DataRow( "t100", 100, 3, false, 28, "9ed3c5c8732b6828099e1fd2094ae809ecbc2cf05d2181dcf496be6d431fb8dc" )]
	[DataRow( "t70k", 70000, 4, false, 10136, "7e8267b39288ebf3da3ed3f282e87bf127e79bbdb88bb6fe866e6e266d075232" )]
	[DataRow( "t300k", 300000, 5, false, 45711, "66ad5e15ef4f6a24a3369b042809c94bd45330dd61f0ca12bfeaa184dfe2ba8c" )]
	[DataRow( "t1m", 1000000, 6, false, 153965, "05cad87f5a3855ef5422430482e1231321073bc2fc38988713c6b9e090f22b5d" )]
	[DataRow( "r100k", 100000, 7, true, 100026, "e2d5d39c9bd9ec1b5caaf100ea7ab098a7fb1f7ec8e36a91184a8b9a342e6b2a" )]
	[DataRow( "r3", 3, 8, true, 11, "d6369703abb759cf0b2e9ba3da8c8d8a261aaa2ce61c1b48f9d6d32622cb05f8" )]
	public void CompressesLikeTheReferenceEncoder( string name, int length, int seed, bool random, int expectedLength, string expectedHash )
	{
		var data = random ? Random( length, seed ) : Generate( length, seed );
		var compressed = ClassicZlibDeflater.Compress( data );
		Assert.AreEqual( expectedLength, compressed.Length, name );
		Assert.AreEqual( expectedHash, Convert.ToHexString( SHA256.HashData( compressed ) ).ToLowerInvariant(), name );
		using var inflater = new InflaterInputStream( new MemoryStream( compressed ) );
		using var output = new MemoryStream();
		inflater.CopyTo( output );
		CollectionAssert.AreEqual( data, output.ToArray(), name );
	}
}

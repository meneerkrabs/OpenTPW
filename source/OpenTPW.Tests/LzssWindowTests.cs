using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

[TestClass]
public class LzssWindowTests
{
	private static byte[] Decode( byte[] compressed )
	{
		// Exercise the production internal codec without adding a public format API for tests.
		var codec = typeof( TextureFile ).Assembly.GetType( "OpenTPW.LZSS" )!;
		return (byte[])codec.GetMethod( "Decompress", BindingFlags.Public | BindingFlags.Static )!
			.Invoke( null, new object[] { new BitReader( compressed ) } )!;
	}

	private static byte[] Encode( byte[] seed, params (int Offset, int Length)[] copies )
	{
		var bits = new List<bool>();
		void Put( int value, int count )
		{
			for ( var bit = 0; bit < count; bit++ ) bits.Add( (value & (1 << bit)) != 0 );
		}
		foreach ( var value in seed ) { Put( 0, 1 ); Put( value, 8 ); }
		foreach ( var copy in copies ) { Put( 1, 1 ); Put( copy.Offset, 12 ); Put( copy.Length - 1, 7 ); }
		Put( 1, 1 ); Put( 0, 12 ); // End marker; BitReader fetches complete 16-bit words.
		var result = new byte[((bits.Count + 15) / 16) * 2];
		for ( var bit = 0; bit < bits.Count; bit++ ) if ( bits[bit] ) result[bit / 8] |= (byte)(1 << (bit % 8));
		return result;
	}

	[TestMethod]
	public void LiteralsAndAnUnalignedEndMarkerPreserveAllBytes()
	{
		var seed = new byte[] { 0, 255, 1, 128, 127, 4, 90 };
		CollectionAssert.AreEqual( seed, Decode( Encode( seed ) ) );
		var largeSeed = Enumerable.Range( 0, 5000 ).Select( i => (byte)i ).ToArray();
		CollectionAssert.AreEqual( largeSeed, Decode( Encode( largeSeed ) ) );
		CollectionAssert.AreEqual( Array.Empty<byte>(), Decode( Encode( Array.Empty<byte>() ) ) );
	}

	[TestMethod]
	public void CopiesPreserveLiteralBytesAndOverlappingRepetition()
	{
		var seed = Enumerable.Range( 0, 4095 ).Select( i => (byte)(i * 37) ).ToArray();
		var expected = seed.ToList();
		var copies = new List<(int Offset, int Length)>();
		foreach ( var offset in new[] { 1, 2, 3, 7, 31, 127, 128, 129, 255, 2047, 4095 } )
			foreach ( var length in new[] { 1, 2, 7, 127, 128 } )
			{
				copies.Add( (offset, length) );
				for ( var index = 0; index < length; index++ ) expected.Add( expected[expected.Count - offset] );
			}
		CollectionAssert.AreEqual( expected.ToArray(), Decode( Encode( seed, copies.ToArray() ) ) );
	}

	[TestMethod]
	public void ShortCopiesNeedNoAllocationPerReferenceAndGrowTheOutput()
	{
		var seed = Enumerable.Range( 0, 4095 ).Select( i => (byte)i ).ToArray();
		var compressed = Encode( seed, Enumerable.Repeat( (1, 1), 50000 ).ToArray() );
		Decode( compressed ); // Exclude reflection/JIT setup from the allocation measurement.
		var before = GC.GetAllocatedBytesForCurrentThread();
		var decoded = Decode( compressed );
		var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
		CollectionAssert.AreEqual( seed.Concat( Enumerable.Repeat( seed[^1], 50000 ) ).ToArray(), decoded );
		Assert.IsTrue( allocated < 1024 * 1024, $"50,000 references allocated {allocated} bytes; only output growth should allocate." );
	}

	[TestMethod]
	public void OffsetPastTheDecodedStreamKeepsItsFailure()
	{
		var exception = Assert.ThrowsException<TargetInvocationException>( () => Decode( Encode( new byte[] { 1, 2, 3 }, (4, 1) ) ) );
		Assert.AreEqual( "Offset > stream length", exception.InnerException!.Message );
	}
}

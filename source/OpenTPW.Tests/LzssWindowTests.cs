using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

[TestClass]
public class LzssWindowTests
{
	private sealed class CountingStream : MemoryStream
	{
		public int BytesRead { get; private set; }
		public override int ReadByte() { BytesRead++; return base.ReadByte(); }
	}

	private static CountingStream Decode( byte[] compressed )
	{
		var output = new CountingStream();
		using var writer = new BinaryWriter( output, System.Text.Encoding.UTF8, leaveOpen: true );
		// Exercise the production internal codec without adding a public format API for tests.
		var codec = typeof( TextureFile ).Assembly.GetType( "OpenTPW.LZSS" )!;
		codec.GetMethod( "Decompress", BindingFlags.Public | BindingFlags.Static )!
			.Invoke( null, new object[] { new BitReader( compressed ), writer } );
		return output;
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
		using var decoded = Decode( Encode( seed, copies.ToArray() ) );
		CollectionAssert.AreEqual( expected.ToArray(), decoded.ToArray() );
		Assert.AreEqual( decoded.Length, decoded.Position );
	}

	[TestMethod]
	public void ShortCopyReadsOnlyTheReferencedPrefixOfTheWindow()
	{
		var seed = Enumerable.Range( 0, 4095 ).Select( i => (byte)i ).ToArray();
		using var decoded = Decode( Encode( seed, (4095, 3) ) );
		CollectionAssert.AreEqual( seed.Concat( seed.Take( 3 ) ).ToArray(), decoded.ToArray() );
		Assert.AreEqual( 3, decoded.BytesRead, "A three-byte copy must not read the entire 4095-byte lookback window." );
	}

	[TestMethod]
	public void OffsetPastTheDecodedStreamKeepsItsFailure()
	{
		var exception = Assert.ThrowsException<TargetInvocationException>( () => Decode( Encode( new byte[] { 1, 2, 3 }, (4, 1) ) ) );
		Assert.AreEqual( "Offset > stream length", exception.InnerException!.Message );
	}
}

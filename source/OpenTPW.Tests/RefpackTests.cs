using System;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// The rewritten RefPack decoder against the former one (<see cref="LegacyRefpack"/>, kept verbatim): the same bytes, or the same
/// failure, for crafted command streams, random streams and every compressed member of the original archives.
/// </summary>
[TestClass]
public class RefpackTests
{
	private static (byte[]? Bytes, Type? Failure) Run( Func<byte[]> decode )
	{
		try
		{
			return (decode(), null);
		}
		catch ( Exception exception )
		{
			return (null, exception.GetType());
		}
	}

	private static void AssertSame( byte[] input, string what )
	{
		var expected = Run( () => new LegacyRefpack( input ).Decompress().ToArray() );
		var actual = Run( () => new Refpack( input ).Decompress() );
		Assert.AreEqual( expected.Failure, actual.Failure, what );
		if ( expected.Bytes != null )
			CollectionAssert.AreEqual( expected.Bytes, actual.Bytes, what );
	}

	[TestMethod]
	public void CraftedCommandsDecodeAsBefore()
	{
		// "abc" as a literal run, then each command type copying back, overlapping copies, a short literal at the end of the
		// input, a copy reaching before the output, and the stop byte with literals.
		var body = new byte[]
		{
			0xE0, (byte)'a', (byte)'b', (byte)'c', (byte)'d',   // run of 4 literals
			0x01, 0x00, (byte)'e',                               // two bytes: 1 literal, copy 3 from distance 1 (overlap)
			0x82, 0x40, 0x03, (byte)'f',                         // three bytes: 1 literal, copy 6 from distance 4
			0xC0, 0x00, 0x05, 0x02,                              // four bytes: copy 7 from distance 6
			0x7C, 0xFF,                                          // two bytes: copy reaching before the output (distance 0x300)
			0xFD, (byte)'g',                                     // stop with 1 literal
		};
		AssertSame( new byte[] { 0x10, 0xFB, 0, 0, 0 }.Concat( body ).ToArray(), "crafted stream" );
		AssertSame( new byte[] { 0x10, 0xFB, 0, 0, 0, 0xE3, 1, 2 }, "a literal run past the end stops short" );
		AssertSame( new byte[] { 0x10, 0xFB, 0, 0, 0, 0x80, 0x01 }, "a three-byte command cut off by the end" );
		AssertSame( new byte[] { 0x10, 0xFB, 0, 0, 0, 0xE0 }, "an opcode in the last byte is not executed" );
		AssertSame( new byte[] { 0x10, 0xFB, 0 }, "a header without a body" );
		AssertSame( new byte[] { 0xFB, 0x10, 0, 0, 0 }, "a wrong header fails the same way" );
		AssertSame( Array.Empty<byte>(), "an empty input fails the same way" );
		// A long input that stops at once (its size hint would overflow a 32-bit product): both return nothing.
		var large = new byte[16_777_216];
		large[0] = 0x10;
		large[1] = 0xFB;
		large[2] = 0xFF;
		large[5] = 0xFC;
		AssertSame( large, "a 16 MiB input with an early stop" );
	}

	[TestMethod]
	public void RandomStreamsDecodeAsBefore()
	{
		var random = new Random( 2026 );
		for ( var index = 0; index < 4000; index++ )
		{
			var body = new byte[random.Next( 0, 64 )];
			random.NextBytes( body );
			AssertSame( new byte[] { 0x10, 0xFB, (byte)random.Next( 256 ), (byte)random.Next( 256 ), (byte)random.Next( 256 ) }.Concat( body ).ToArray(), $"random stream {index}" );
		}
	}

	[TestMethod]
	public void EveryCompressedMemberOfTheOriginalArchivesDecodesAsBefore()
	{
		var game = Environment.GetEnvironmentVariable( "OPENTPW_GAME_PATH" );
		if ( string.IsNullOrEmpty( game ) || !Directory.Exists( game ) )
			Assert.Inconclusive( "Set OPENTPW_GAME_PATH to the original game to compare the archives." );
		var compared = 0;
		var options = new EnumerationOptions { RecurseSubdirectories = true, MatchCasing = MatchCasing.CaseInsensitive };
		foreach ( var path in Directory.EnumerateFiles( game, "*.wad", options ) )
		{
			var archive = new WadArchive( path );
			foreach ( var file in Members( archive.Root ).Where( file => file.Compressed ) )
			{
				AssertSame( archive.GetData( file.OffsetInArchive, file.SizeInArchive ), $"{path}: {file.Name}" );
				// The directory's declared size is the decoded length (the original allocates exactly that much).
				Assert.AreEqual( file.Size, new Refpack( archive.GetData( file.OffsetInArchive, file.SizeInArchive ) ).Decompress().LongLength, $"{path}: {file.Name}" );
				compared++;
			}
		}
		Assert.IsTrue( compared > 1000, $"only {compared} compressed members compared" );
	}

	private static System.Collections.Generic.IEnumerable<WadArchiveFile> Members( ArchiveDirectory directory )
	{
		foreach ( var child in directory.Children )
		{
			if ( child is WadArchiveFile file )
				yield return file;
			else if ( child is ArchiveDirectory nested )
				foreach ( var inner in Members( nested ) )
					yield return inner;
		}
	}
}

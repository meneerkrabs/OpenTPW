using System;
using System.Buffers.Binary;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

[TestClass]
public class FontFileTests
{
	private static byte[] CreateFont( byte encoding, ushort width, ushort height, byte[] samples )
	{
		var data = new byte[36 + samples.Length];
		"F4FB"u8.CopyTo( data );
		data[4] = 1;
		data[5] = 11;
		BinaryPrimitives.WriteUInt16LittleEndian( data.AsSpan( 6 ), 1 );
		BinaryPrimitives.WriteUInt32LittleEndian( data.AsSpan( 8 ), 12 );
		BinaryPrimitives.WriteUInt16LittleEndian( data.AsSpan( 12 ), 'A' );
		BinaryPrimitives.WriteUInt32LittleEndian( data.AsSpan( 16 ), (uint)samples.Length );
		BinaryPrimitives.WriteUInt32LittleEndian( data.AsSpan( 20 ), (uint)(((long)width * height + 1) / 2) );
		data[24] = encoding;
		BinaryPrimitives.WriteUInt16LittleEndian( data.AsSpan( 28 ), width );
		BinaryPrimitives.WriteUInt16LittleEndian( data.AsSpan( 30 ), height );
		data[32] = 255;
		data[33] = 2;
		BinaryPrimitives.WriteInt16LittleEndian( data.AsSpan( 34 ), 7 );
		samples.CopyTo( data, 36 );
		return data;
	}

	[TestMethod]
	public void DecodesContinuousFourBitSamplesAndSignedMetrics()
	{
		using var stream = new MemoryStream( CreateFont( 0, 3, 2, new byte[] { 0x12, 0x34, 0x5f } ) );
		var font = new FontFile( stream );
		Assert.IsTrue( stream.CanRead );
		Assert.AreEqual( (byte)1, font.HeaderWidthHint );
		var glyph = font.Glyphs.Single();
		Assert.AreEqual( 'A', glyph.Character );
		Assert.AreEqual( 3, glyph.Width );
		Assert.AreEqual( 2, glyph.Height );
		Assert.AreEqual( (sbyte)-1, glyph.OffsetX );
		Assert.AreEqual( (sbyte)2, glyph.OffsetY );
		Assert.AreEqual( (short)7, glyph.Advance );
		CollectionAssert.AreEqual( new byte[] { 1, 2, 3, 4, 5, 15 }, glyph.Coverage );
	}

	[TestMethod]
	public void DecodesMonochromeHighBitFirstAcrossRowBoundaries()
	{
		var font = new FontFile( new MemoryStream( CreateFont( 2, 3, 3, new byte[] { 0xa5, 0x80 } ) ) );
		CollectionAssert.AreEqual( new byte[] { 15, 0, 15, 0, 0, 15, 0, 15, 15 }, font.Glyphs.Single().Coverage );
	}

	[TestMethod]
	public void DecodesRleLiteralRunAndTerminator()
	{
		var font = new FontFile( new MemoryStream( CreateFont( 1, 3, 2, new byte[] { 0x10, 0x4f, 0x20, 0x00 } ) ) );
		CollectionAssert.AreEqual( new byte[] { 1, 15, 15, 15, 15, 2 }, font.Glyphs.Single().Coverage );
	}

	[TestMethod]
	public void RleSupportsZeroOddPaddingAndImplicitLastZero()
	{
		var padded = new FontFile( new MemoryStream( CreateFont( 1, 3, 1, new byte[] { 0x12, 0x30, 0x10, 0x00 } ) ) );
		CollectionAssert.AreEqual( new byte[] { 1, 2, 3 }, padded.Glyphs.Single().Coverage );
		var implicitZero = new FontFile( new MemoryStream( CreateFont( 1, 2, 1, new byte[] { 0x10, 0x00 } ) ) );
		CollectionAssert.AreEqual( new byte[] { 1, 0 }, implicitZero.Glyphs.Single().Coverage );
	}

	[TestMethod]
	public void EmptyGlyphAndEmptyFontAreValid()
	{
		var glyph = new FontFile( new MemoryStream( CreateFont( 0, 0, 0, Array.Empty<byte>() ) ) ).Glyphs.Single();
		Assert.AreEqual( 0, glyph.Coverage.Length );
		var empty = new byte[8];
		"F4FB"u8.CopyTo( empty );
		Assert.AreEqual( 0, new FontFile( new MemoryStream( empty ) ).Glyphs.Count );
	}

	[DataTestMethod]
	[DataRow( 0 )]
	[DataRow( 7 )]
	[DataRow( 10 )]
	[DataRow( 35 )]
	public void RejectsTruncatedFile( int length )
	{
		var data = CreateFont( 0, 2, 1, new byte[] { 0x12 } );
		Array.Resize( ref data, length );
		Assert.ThrowsException<InvalidDataException>( () => new FontFile( new MemoryStream( data ) ) );
	}

	[DataTestMethod]
	[DataRow( 0u )]
	[DataRow( 8u )]
	[DataRow( 13u )]
	[DataRow( uint.MaxValue )]
	public void RejectsInvalidGlyphOffsets( uint offset )
	{
		var data = CreateFont( 0, 2, 1, new byte[] { 0x12 } );
		BinaryPrimitives.WriteUInt32LittleEndian( data.AsSpan( 8 ), offset );
		Assert.ThrowsException<InvalidDataException>( () => new FontFile( new MemoryStream( data ) ) );
	}

	[DataTestMethod]
	[DataRow( 16 )]
	[DataRow( 20 )]
	public void RejectsInvalidEncodedAndDecodedSizes( int offset )
	{
		var data = CreateFont( 0, 2, 1, new byte[] { 0x12 } );
		BinaryPrimitives.WriteUInt32LittleEndian( data.AsSpan( offset ), uint.MaxValue );
		Assert.ThrowsException<InvalidDataException>( () => new FontFile( new MemoryStream( data ) ) );
	}

	[TestMethod]
	public void RejectsUnknownEncodingAndBadMagic()
	{
		var data = CreateFont( 3, 1, 1, new byte[] { 0x10 } );
		Assert.ThrowsException<NotSupportedException>( () => new FontFile( new MemoryStream( data ) ) );
		data[0] = 0;
		Assert.ThrowsException<InvalidDataException>( () => new FontFile( new MemoryStream( data ) ) );
	}

	[TestMethod]
	public void RejectsGlyphAndTableLimitsBeforeAllocatingPixels()
	{
		var data = CreateFont( 0, ushort.MaxValue, ushort.MaxValue, Array.Empty<byte>() );
		Assert.ThrowsException<InvalidDataException>( () => new FontFile( new MemoryStream( data ) ) );
		BinaryPrimitives.WriteUInt16LittleEndian( data.AsSpan( 6 ), ushort.MaxValue );
		Assert.ThrowsException<InvalidDataException>( () => new FontFile( new MemoryStream( data ) ) );
	}

	[TestMethod]
	public void RejectsTotalPixelsLimitEvenForAliasedOffsets()
	{
		var original = CreateFont( 0, 1024, 1024, new byte[1024 * 512] );
		var data = new byte[original.Length + 16 * 4];
		original.AsSpan( 0, 8 ).CopyTo( data );
		BinaryPrimitives.WriteUInt16LittleEndian( data.AsSpan( 6 ), 17 );
		for ( var index = 0; index < 17; index++ )
			BinaryPrimitives.WriteUInt32LittleEndian( data.AsSpan( 8 + index * 4 ), 76 );
		original.AsSpan( 12 ).CopyTo( data.AsSpan( 76 ) );
		Assert.ThrowsException<InvalidDataException>( () => new FontFile( new MemoryStream( data ) ) );
	}

	[TestMethod]
	public void ReadsNonseekableShortReadsWithoutClosingInput()
	{
		using var stream = new ShortReadStream( CreateFont( 0, 2, 1, new byte[] { 0x12 } ) );
		CollectionAssert.AreEqual( new byte[] { 1, 2 }, new FontFile( stream ).Glyphs.Single().Coverage );
		Assert.IsTrue( stream.CanRead );
	}

	[TestMethod]
	public void RejectsOversizedInputWithoutClosingIt()
	{
		using var stream = new MemoryStream( new byte[FontFile.MaximumFileBytes + 1] );
		Assert.ThrowsException<InvalidDataException>( () => new FontFile( stream ) );
		Assert.IsTrue( stream.CanRead );
	}

	private sealed class ShortReadStream : MemoryStream
	{
		public ShortReadStream( byte[] data ) : base( data ) { }
		public override bool CanSeek => false;
		public override long Length => throw new NotSupportedException();
		public override int Read( byte[] readBuffer, int offset, int count ) => base.Read( readBuffer, offset, Math.Min( count, 3 ) );
	}

	[DataTestMethod]
	[DataRow( (byte)0 )]
	[DataRow( (byte)2 )]
	public void RejectsTruncatedRawSamples( byte encoding )
	{
		Assert.ThrowsException<InvalidDataException>( () => new FontFile( new MemoryStream( CreateFont( encoding, 8, 8, new byte[] { 0 } ) ) ) );
	}

	[DataTestMethod]
	[DataRow( "1" )]
	[DataRow( "10" )]
	[DataRow( "1f" )]
	[DataRow( "0700" )]
	[DataRow( "00" )]
	[DataRow( "123400" )]
	public void RejectsInvalidRle( string hex )
	{
		var paddedHex = hex.Length % 2 == 0 ? hex : hex + "0";
		Assert.ThrowsException<InvalidDataException>( () => new FontFile( new MemoryStream( CreateFont( 1, 3, 1, Convert.FromHexString( paddedHex ) ) ) ) );
	}

	[DataTestMethod]
	[DataRow( "GAME8.bf4", "A52C765C2C652F20EEA95DD25F19FE595300B7E6B89E88ABCF526889E226C8EA", 6, 8, 7, "0447875B4A01045703C3B76DBBD3C10A282FAEE3C139F204929368E906951547" )]
	[DataRow( "GAME8AA.bf4", "15EB42CD95898A7CD87F7B4E15B4FABDFB0F321C092BE257DF13A23F71DD3FED", 7, 8, 8, "116F1F31DCB27AF9DBEF89ED5669592008EA01EF66735ED7797A9406E48F79AA" )]
	[DataRow( "SESHMED.bf4", "429DB5D5503E725E6783D49BD9524DD8CB41AE030ECE16ED2A85C77CDDD0A6A2", 12, 19, 12, "D79CDEE8540308A668FA2734B742BAD0082FAB889838EFEA043F665CCFCFF08F" )]
	public void OriginalFontMatchesIdentityMetricsAndIndependentCoverage( string name, string fileHash, int width, int height, int advance, string coverageHash )
	{
		var directory = OriginalFontDirectory();
		var data = File.ReadAllBytes( Path.Combine( directory, name ) );
		Assert.AreEqual( fileHash, Convert.ToHexString( SHA256.HashData( data ) ) );
		var font = new FontFile( new MemoryStream( data ) );
		Assert.AreEqual( 249, font.Glyphs.Count );
		Assert.AreEqual( 58, font.Glyphs.Count( glyph => glyph.Character == ' ' ) );
		var glyph = font.Glyphs.Single( glyph => glyph.Character == 'A' );
		Assert.AreEqual( width, glyph.Width );
		Assert.AreEqual( height, glyph.Height );
		Assert.AreEqual( advance, (int)glyph.Advance );
		Assert.AreEqual( coverageHash, Convert.ToHexString( SHA256.HashData( glyph.Coverage ) ) );
	}

	[TestMethod]
	public void AllSelectedOriginalEnglishFontsDecodeEveryGlyph()
	{
		var files = Directory.GetFiles( OriginalFontDirectory() ).Where( file => Path.GetExtension( file ).Equals( ".bf4", StringComparison.OrdinalIgnoreCase ) ).ToArray();
		Assert.AreEqual( 33, files.Length );
		var glyphCount = 0;
		foreach ( var filename in files )
		{
			using var stream = File.OpenRead( filename );
			var font = new FontFile( stream );
			foreach ( var glyph in font.Glyphs )
			{
				Assert.AreEqual( glyph.Width * glyph.Height, glyph.Coverage.Length, filename );
				Assert.IsTrue( glyph.Coverage.All( value => value <= 15 ), filename );
			}
			glyphCount += font.Glyphs.Count;
		}
		Assert.AreEqual( 8217, glyphCount );
	}

	private static string OriginalFontDirectory()
	{
		var gamePath = Environment.GetEnvironmentVariable( "OPENTPW_GAME_PATH" );
		if ( string.IsNullOrWhiteSpace( gamePath ) || !Directory.Exists( gamePath ) )
			Assert.Inconclusive( "Set OPENTPW_GAME_PATH for the selected original English BF4 corpus." );
		var dataPath = Directory.EnumerateDirectories( gamePath! ).FirstOrDefault( directory => string.Equals( Path.GetFileName( directory ), "data", StringComparison.OrdinalIgnoreCase ) ) ?? gamePath!;
		var directory = Path.Combine( dataPath, "Language", "English" );
		if ( !Directory.Exists( directory ) )
			Assert.Inconclusive( "The selected English BF4 corpus is missing." );
		return directory;
	}
}

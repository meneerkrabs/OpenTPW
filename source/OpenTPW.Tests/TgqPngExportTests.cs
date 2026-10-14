using System;
using System.Buffers.Binary;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using StbImageSharp;

namespace OpenTPW.Tests;

[TestClass]
[DoNotParallelize]
public class TgqPngExportTests
{
	private string root = null!;
	private Logger previousLog = null!;
	[TestInitialize]
	public void Setup() { previousLog = Log; Log = new Logger(); root = Directory.CreateTempSubdirectory( "opentpw-tgq-export-" ).FullName; }
	[TestCleanup]
	public void Cleanup() { Log = previousLog; Directory.Delete( root, true ); }

	private static byte[] Chunk( string tag, byte[] payload )
	{
		var bytes = new byte[8 + payload.Length]; Encoding.ASCII.GetBytes( tag ).CopyTo( bytes, 0 );
		BinaryPrimitives.WriteUInt32LittleEndian( bytes.AsSpan( 4 ), (uint)bytes.Length ); payload.CopyTo( bytes, 8 ); return bytes;
	}
	private static byte[] Frame( bool bright, ushort height = 32, bool malformed = false )
	{
		var bits = "";
		for ( var macro = 0; macro < height / 16; macro++ )
		{
			bits += bright && macro == 0 ? "111110110010010" : "10010"; // first luma DC +100 or unchanged, then EOB
			bits += "100101001010010"; // three unchanged luma blocks
			bits += macro == 0 ? "111111101000000010111111101000000010" : "00100010"; // neutral Cb/Cr DC128, then unchanged
		}
		var bytes = new byte[8 + (malformed ? 0 : (bits.Length + 31) / 32 * 4)];
		BinaryPrimitives.WriteUInt16LittleEndian( bytes, 16 ); BinaryPrimitives.WriteUInt16LittleEndian( bytes.AsSpan( 2 ), height ); bytes[4] = 99;
		if ( !malformed ) for ( var word = 0; word < (bytes.Length - 8) / 4; word++ )
		{
			uint value = 0;
			for ( var bit = 0; bit < 32; bit++ ) { var index = word * 32 + bit; value = (value << 1) | (index < bits.Length && bits[index] == '1' ? 1u : 0u); }
			BinaryPrimitives.WriteUInt32LittleEndian( bytes.AsSpan( 8 + word * 4 ), value );
		}
		return bytes;
	}
	private string Input( ushort height = 32, int malformedFrame = -1, bool badUnselectedHeader = false )
	{
		var patch = new byte[] { (byte)'P', (byte)'T', 0, 0, 0x1b, 1, 30, 0xfd, 0x85, 1, 28, 0x82, 1, 2, 0x83, 1, 7, 0x8a, 4, 0, 0, 0, 0, 0xff };
		var first = Frame( false, height, malformedFrame == 0 ); if ( badUnselectedHeader ) first[4] = 108;
		var bytes = Chunk( "SCHl", patch ).Concat( Chunk( "pIQT", first ) ).Concat( Chunk( "pIQT", Frame( true, height, malformedFrame == 1 ) ) )
			.Concat( Chunk( "SCCl", new byte[] { 1, 0, 0, 0 } ) ).Concat( Chunk( "SCDl", new byte[] { 0 } ) ) // structurally valid chunk; deliberately undecodable audio
			.Concat( Chunk( "SCEl", Array.Empty<byte>() ) ).ToArray();
		var path = Path.Combine( root, "source.tgq" ); File.WriteAllBytes( path, bytes ); return path;
	}
	private static void Export( string input, int index, string output ) => TgqPngExport.Run( input, index, output );

	[DataTestMethod]
	[DataRow( 0, 16 )]
	[DataRow( 1, 16 )]
	[DataRow( 0, 32 )]
	[DataRow( 1, 32 )]
	public void SelectedFirstOrLastFrameExportsEncodedRasterAndCurrentOpaquePixels( int index, int height )
	{
		var input = Input( (ushort)height ); var original = File.ReadAllBytes( input ); var movie = new TgqMovieFile( new MemoryStream( original ) );
		var expected = new byte[16 * height * 4]; movie.DecodeVideoFrame( index ).WriteRgba32( expected );
		Assert.AreEqual( (byte)(index == 0 ? 0 : 100), expected[0] ); Assert.IsTrue( expected.Where( ( _, offset ) => offset % 4 == 3 ).All( value => value == 255 ) );
		var output = Path.Combine( root, "nested", "selected.png" ); Export( input, index, output );
		var png = ImageResult.FromMemory( File.ReadAllBytes( output ), ColorComponents.RedGreenBlueAlpha );
		Assert.AreEqual( (16, height), (png.Width, png.Height) ); CollectionAssert.AreEqual( expected, png.Data );
		CollectionAssert.AreEqual( original, File.ReadAllBytes( input ) ); Assert.AreEqual( 0, Directory.GetFiles( root, "*.tmp", SearchOption.AllDirectories ).Length );
	}
	[TestMethod]
	public void OnlySelectedVideoPayloadIsDecodedAndAudioIsNotDecoded()
	{
		var input = Input( malformedFrame: 0 ); var movie = new TgqMovieFile( new MemoryStream( File.ReadAllBytes( input ) ) );
		Assert.ThrowsException<InvalidDataException>( () => movie.DecodeVideoFrame( 0 ) ); Assert.ThrowsException<InvalidDataException>( () => movie.DecodeAudio() );
		var output = Path.Combine( root, "selected.png" ); Export( input, 1, output );
		var png = ImageResult.FromMemory( File.ReadAllBytes( output ), ColorComponents.RedGreenBlueAlpha ); Assert.AreEqual( (byte)100, png.Data[0] );
	}
	[DataTestMethod]
	[DataRow( -1 )]
	[DataRow( 2 )]
	[DataRow( int.MaxValue )]
	public void InvalidIndexFailsBeforeOutputOrParentCreation( int index )
	{
		var input = Input(); var output = Path.Combine( root, "absent", "bad.png" );
		Assert.ThrowsException<ArgumentOutOfRangeException>( () => Export( input, index, output ) ); Assert.IsFalse( Directory.Exists( Path.GetDirectoryName( output ) ) );
	}
	[TestMethod]
	public void MalformedSelectedPayloadAndUnselectedHeaderRetainStrictContainerChecks()
	{
		var output = Path.Combine( root, "absent", "bad.png" ); var input = Input( malformedFrame: 1 );
		Assert.ThrowsException<InvalidDataException>( () => Export( input, 1, output ) ); Assert.IsFalse( Directory.Exists( Path.GetDirectoryName( output ) ) );
		input = Input( badUnselectedHeader: true ); Assert.ThrowsException<InvalidDataException>( () => Export( input, 1, output ) ); Assert.IsFalse( File.Exists( output ) );
	}
	[TestMethod]
	public void ExistingAndForeignOutputsAreNeverOverwrittenAndLateMoveCleansOnlyOwnedTemp()
	{
		var input = Input(); var original = File.ReadAllBytes( input ); var existing = Path.Combine( root, "old.png" ); File.WriteAllText( existing, "previous" );
		Assert.ThrowsException<IOException>( () => Export( input, 0, existing ) ); Assert.AreEqual( "previous", File.ReadAllText( existing ) );
		var directory = Path.Combine( root, "directory.png" ); Directory.CreateDirectory( directory ); var foreign = directory + ".foreign.tmp"; File.WriteAllText( foreign, "foreign" );
		var refused = false; try { Export( input, 1, directory ); } catch ( IOException ) { refused = true; } catch ( UnauthorizedAccessException ) { refused = true; }
		Assert.IsTrue( refused ); Assert.IsTrue( Directory.Exists( directory ) ); Assert.AreEqual( "foreign", File.ReadAllText( foreign ) );
		CollectionAssert.AreEqual( new[] { foreign }, Directory.GetFiles( root, "*.tmp" ) ); CollectionAssert.AreEqual( original, File.ReadAllBytes( input ) );
	}
	[TestMethod]
	public void InvalidPathsAndForeignSuffixesFailWithoutChangingInput()
	{
		var input = Input(); var original = File.ReadAllBytes( input );
		Assert.ThrowsException<ArgumentException>( () => Export( input, 0, Path.Combine( root, "native.tgq" ) ) );
		Assert.ThrowsException<ArgumentException>( () => Export( "", 0, Path.Combine( root, "bad.png" ) ) );
		Assert.ThrowsException<FileNotFoundException>( () => Export( Path.Combine( root, "missing.tgq" ), 0, Path.Combine( root, "bad.png" ) ) );
		CollectionAssert.AreEqual( original, File.ReadAllBytes( input ) ); Assert.AreEqual( 1, Directory.GetFiles( root ).Length );
	}
	[TestMethod]
	public void ActualCliExportsBeforeGameAssetsProfileOrWindowInitialization()
	{
		var input = Input(); var output = Path.Combine( root, "cli.png" );
		Game.Run( new[] { "--export-tgq-png", input, "1", output } );
		var png = ImageResult.FromMemory( File.ReadAllBytes( output ), ColorComponents.RedGreenBlueAlpha );
		Assert.AreEqual( (16, 32), (png.Width, png.Height) ); Assert.AreEqual( (byte)100, png.Data[0] );
	}
	[TestMethod]
	public void BadCliArgumentsFailBeforeAnyOutput()
	{
		var input = Input(); var output = Path.Combine( root, "bad.png" );
		foreach ( var args in new[] { new[] { "--export-tgq-png" }, new[] { "--export-tgq-png", input, "1" },
			new[] { "--export-tgq-png", input, "-1", output }, new[] { "--export-tgq-png", input, "one", output },
			new[] { "--export-tgq-png", input, "2147483648", output }, new[] { "--export-tgq-png", input, "0", "--output" } } )
			Assert.ThrowsException<ArgumentException>( () => Game.Run( args ) );
		Assert.IsFalse( File.Exists( output ) ); Assert.AreEqual( 1, Directory.GetFiles( root ).Length );
	}

}

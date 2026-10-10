using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using StbImageSharp;

namespace OpenTPW.Tests;

[TestClass]
public class FshFileTests
{
	private sealed record Record( byte Code, byte[] Body );

	private sealed record Image( string Tag, Record[] Records );

	private static Record ImageRecord( int width, int height, byte[] indices, bool compressed = false, byte[]? trailing = null )
	{
		var body = new byte[12 + (compressed ? 0 : indices.Length)];
		BinaryPrimitives.WriteUInt16LittleEndian( body, (ushort)width );
		BinaryPrimitives.WriteUInt16LittleEndian( body.AsSpan( 2 ), (ushort)height );
		if ( !compressed )
			indices.CopyTo( body, 12 );
		else
			body = body.Concat( RefPack( indices, (indices.Length + 15) & ~15 ) ).ToArray();
		return new( compressed ? (byte)0xFB : (byte)0x7B, body.Concat( trailing ?? Array.Empty<byte>() ).ToArray() );
	}

	private static Record Palette( byte code, int entries, byte[] colors, int height = 1 )
	{
		var body = new byte[12];
		BinaryPrimitives.WriteUInt16LittleEndian( body, (ushort)entries );
		BinaryPrimitives.WriteUInt16LittleEndian( body.AsSpan( 2 ), (ushort)height );
		BinaryPrimitives.WriteUInt16LittleEndian( body.AsSpan( 4 ), (ushort)entries );
		return new( code, body.Concat( colors ).ToArray() );
	}

	private static Record Name( string name ) => new( 0x70, Encoding.ASCII.GetBytes( name + "\0" ) );

	/// <summary>
	/// Literal-only RefPack with the corpus header (<c>10 FB</c>, 24-bit big-endian size). The
	/// declared size is the padded size; the pixels are zero-extended to it.
	/// </summary>
	private static byte[] RefPack( byte[] pixels, int declaredSize )
	{
		var source = pixels.Concat( new byte[declaredSize - pixels.Length] ).ToArray();
		var output = new List<byte> { 0x10, 0xFB, (byte)(declaredSize >> 16), (byte)(declaredSize >> 8), (byte)declaredSize };
		var position = 0;
		while ( source.Length - position >= 4 )
		{
			var run = Math.Min( 112, (source.Length - position) & ~3 );
			output.Add( (byte)(0xE0 + run / 4 - 1) );
			output.AddRange( source.Skip( position ).Take( run ) );
			position += run;
		}
		output.Add( (byte)(0xFC + source.Length - position) );
		output.AddRange( source.Skip( position ) );
		return output.ToArray();
	}

	/// <summary>Builds an SHPI file with a "Buy ERTS" gap before the first image, records aligned to 16 bytes.</summary>
	private static byte[] Build( params Image[] images )
	{
		var output = new List<byte>();
		output.AddRange( "SHPI"u8.ToArray() );
		output.AddRange( new byte[4] );
		output.AddRange( BitConverter.GetBytes( images.Length ) );
		output.AddRange( "G231"u8.ToArray() );
		var directory = output.Count;
		output.AddRange( new byte[8 * images.Length] );
		output.AddRange( "Buy ERTS"u8.ToArray() );
		while ( output.Count % 16 != 0 )
			output.Add( 0 );
		for ( var index = 0; index < images.Length; index++ )
		{
			var tag = Encoding.ASCII.GetBytes( images[index].Tag.PadRight( 4, '\0' ) );
			for ( var b = 0; b < 4; b++ )
				output[directory + 8 * index + b] = tag[b];
			var start = output.Count;
			BinaryPrimitives.WriteInt32LittleEndian( CollectionsMarshalSpan( output, directory + 8 * index + 4 ), start );
			var records = images[index].Records;
			for ( var r = 0; r < records.Length; r++ )
			{
				var recordStart = output.Count;
				var length = 4 + records[r].Body.Length;
				var padded = (length + 15) & ~15;
				var next = r == records.Length - 1 ? 0 : padded;
				output.AddRange( new[] { records[r].Code, (byte)next, (byte)(next >> 8), (byte)(next >> 16) } );
				output.AddRange( records[r].Body );
				if ( r < records.Length - 1 )
					output.AddRange( new byte[padded - length] );
			}
		}
		var data = output.ToArray();
		BinaryPrimitives.WriteInt32LittleEndian( data.AsSpan( 4 ), data.Length );
		return data;
	}

	private static Span<byte> CollectionsMarshalSpan( List<byte> list, int offset ) =>
		System.Runtime.InteropServices.CollectionsMarshal.AsSpan( list )[offset..];

	private static byte[] SampleRgb( int entries ) => Enumerable.Range( 0, entries ).SelectMany( index => new[] { (byte)(index * 10), (byte)(index * 10 + 1), (byte)(index * 10 + 2) } ).ToArray();

	private static byte[] Simple( bool compressed = false ) =>
		Build( new Image( "img", new[] { ImageRecord( 4, 4, Enumerable.Range( 0, 16 ).Select( index => (byte)(index % 3) ).ToArray(), compressed ), Palette( 0x24, 3, SampleRgb( 3 ) ), Name( "img" ) } ) );

	private static FshFile Read( byte[] data ) => new( new MemoryStream( data ) );

	[TestMethod]
	public void ReadsRawImageWithRgbPaletteAndNameWithoutClosingInput()
	{
		using var stream = new MemoryStream( Simple() );
		var fsh = new FshFile( stream );
		Assert.IsTrue( stream.CanRead );
		Assert.AreEqual( "G231", fsh.Id );
		var image = fsh.Images.Single();
		Assert.AreEqual( "img", image.Tag );
		Assert.AreEqual( "img", image.Name );
		Assert.IsFalse( image.Compressed );
		Assert.AreEqual( (4, 4), (image.Width, image.Height) );
		Assert.AreEqual( FshPaletteFormat.Rgb24, image.PaletteFormat );
		Assert.AreEqual( 3, image.PaletteEntries );
		Assert.AreEqual( (0, 0, 0, 0), (image.CenterX, image.CenterY, image.PositionX, image.PositionY) );
		CollectionAssert.AreEqual( new byte[] { 0, 1, 2, 255, 10, 11, 12, 255, 20, 21, 22, 255 }, image.Palette );
		CollectionAssert.AreEqual( new byte[] { 10, 11, 12, 255 }, image.Rgba.AsSpan( 4, 4 ).ToArray() );
		CollectionAssert.AreEqual( new byte[] { 0, 1, 2, 255 }, image.Rgba.AsSpan( 12, 4 ).ToArray() );
		Assert.AreEqual( 64, image.Rgba.Length );
	}

	[TestMethod]
	public void CompressedImageDecodesPaddedRefPackAndIgnoresTrailingAlignmentBytes()
	{
		// 5x5 = 25 pixels: the RefPack size is padded to 32, as in the corpus; trailing bytes after the stop are not zero.
		var indices = Enumerable.Range( 0, 25 ).Select( index => (byte)(index % 2) ).ToArray();
		var bgra = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 };
		var data = Build( new Image( "c", new[] { ImageRecord( 5, 5, indices, compressed: true, trailing: new byte[] { 0xAA, 0x55 } ), Palette( 0x2A, 2, bgra ) } ) );
		var image = Read( data ).Images.Single();
		Assert.IsTrue( image.Compressed );
		Assert.IsNull( image.Name );
		Assert.AreEqual( FshPaletteFormat.Bgra32, image.PaletteFormat );
		CollectionAssert.AreEqual( indices, image.Indices );
		CollectionAssert.AreEqual( new byte[] { 3, 2, 1, 4, 7, 6, 5, 8 }, image.Palette );
		CollectionAssert.AreEqual( new byte[] { 7, 6, 5, 8 }, image.Rgba.AsSpan( 4, 4 ).ToArray() );
	}

	[TestMethod]
	public void CompressedImageSupportsBackReferences()
	{
		// Two-byte command: literals 0, 1, then copy 6 bytes from distance 2; an 8-byte literal run; stop.
		var body = new byte[12];
		BinaryPrimitives.WriteUInt16LittleEndian( body, 4 );
		BinaryPrimitives.WriteUInt16LittleEndian( body.AsSpan( 2 ), 4 );
		var stream = new byte[] { 0x10, 0xFB, 0, 0, 16, (0x06 - 3) << 2 | 2, 1, 0, 1, 0xE1, 0, 1, 0, 1, 0, 1, 0, 1, 0xFC };
		var data = Build( new Image( "b", new[] { new Record( 0xFB, body.Concat( stream ).ToArray() ), Palette( 0x24, 2, SampleRgb( 2 ) ) } ) );
		CollectionAssert.AreEqual( Enumerable.Range( 0, 16 ).Select( index => (byte)(index % 2) ).ToArray(), Read( data ).Images.Single().Indices );
	}

	[TestMethod]
	public void Argb1555PaletteExpandsFiveBitChannelsAndAlphaBit()
	{
		var colors = new byte[4];
		BinaryPrimitives.WriteUInt16LittleEndian( colors, 0x8000 | 31 << 10 | 16 << 5 | 1 );
		BinaryPrimitives.WriteUInt16LittleEndian( colors.AsSpan( 2 ), 0x03E0 );
		var data = Build( new Image( "p", new[] { ImageRecord( 2, 1, new byte[] { 0, 1 } ), Palette( 0x2D, 2, colors ) } ) );
		var image = Read( data ).Images.Single();
		Assert.AreEqual( FshPaletteFormat.Argb1555, image.PaletteFormat );
		CollectionAssert.AreEqual( new byte[] { 255, 132, 8, 255, 0, 255, 0, 0 }, image.Rgba );
	}

	[TestMethod]
	public void ReadsMultipleImagesEachBoundedByTheNext()
	{
		var first = new Image( "a", new[] { ImageRecord( 1, 1, new byte[] { 0 } ), Palette( 0x24, 1, SampleRgb( 1 ) ), Name( "first" ) } );
		var second = new Image( "b", new[] { ImageRecord( 2, 1, new byte[] { 1, 0 } ), Palette( 0x24, 2, SampleRgb( 2 ) ) } );
		var fsh = Read( Build( first, second ) );
		CollectionAssert.AreEqual( new[] { "a", "b" }, fsh.Images.Select( image => image.Tag ).ToArray() );
		Assert.AreEqual( "first", fsh.Images[0].Name );
		CollectionAssert.AreEqual( new byte[] { 10, 11, 12, 255, 0, 1, 2, 255 }, fsh.Images[1].Rgba );
	}

	[TestMethod]
	public void ReadsShortNonSeekableStreams()
	{
		var data = Simple( compressed: true );
		var fsh = new FshFile( new TrickleStream( data ) );
		CollectionAssert.AreEqual( Read( data ).Images.Single().Rgba, fsh.Images.Single().Rgba );
	}

	[TestMethod]
	public void RejectsBadMagicSizeAndDirectory()
	{
		var data = Simple();
		var badMagic = (byte[])data.Clone();
		badMagic[3] = (byte)'S';
		Assert.ThrowsException<InvalidDataException>( () => Read( badMagic ) );
		Assert.ThrowsException<InvalidDataException>( () => Read( data[..12] ) );
		Assert.ThrowsException<InvalidDataException>( () => Read( data[..^1] ) );
		var count = (byte[])data.Clone();
		BinaryPrimitives.WriteInt32LittleEndian( count.AsSpan( 8 ), FshFile.MaximumImages + 1 );
		Assert.ThrowsException<InvalidDataException>( () => Read( count ) );
		BinaryPrimitives.WriteInt32LittleEndian( count.AsSpan( 8 ), 100 );
		Assert.ThrowsException<InvalidDataException>( () => Read( count ) );
		foreach ( var offset in new[] { 0, 20, data.Length - 15, int.MaxValue } )
		{
			var badOffset = (byte[])data.Clone();
			BinaryPrimitives.WriteInt32LittleEndian( badOffset.AsSpan( 20 ), offset );
			Assert.ThrowsException<InvalidDataException>( () => Read( badOffset ), $"offset {offset}" );
		}
		var image = new Image( "a", new[] { ImageRecord( 1, 1, new byte[] { 0 } ), Palette( 0x24, 1, SampleRgb( 1 ) ) } );
		var twice = Build( image, image );
		BinaryPrimitives.WriteInt32LittleEndian( twice.AsSpan( 28 ), BinaryPrimitives.ReadInt32LittleEndian( twice.AsSpan( 20 ) ) );
		Assert.ThrowsException<InvalidDataException>( () => Read( twice ) );
	}

	[TestMethod]
	public void RejectsInvalidNextRecordOffsetsAndTruncatedRecords()
	{
		var data = Simple();
		var imageOffset = BinaryPrimitives.ReadInt32LittleEndian( data.AsSpan( 20 ) );
		foreach ( var next in new[] { 8, data.Length } )
		{
			var bad = (byte[])data.Clone();
			bad[imageOffset + 1] = (byte)next;
			bad[imageOffset + 2] = (byte)(next >> 8);
			bad[imageOffset + 3] = (byte)(next >> 16);
			Assert.ThrowsException<InvalidDataException>( () => Read( bad ), $"next {next}" );
		}
		// The last record (name) is bounded by the end of the file; cutting it removes the terminator.
		var cut = data[..^1];
		BinaryPrimitives.WriteInt32LittleEndian( cut.AsSpan( 4 ), cut.Length );
		Assert.ThrowsException<InvalidDataException>( () => Read( cut ) );
		// A raw pixel block shorter than width*height.
		var shortRaw = Build( new Image( "s", new[] { new Record( 0x7B, ImageRecord( 8, 8, new byte[64] ).Body[..32] ), Palette( 0x24, 1, SampleRgb( 1 ) ) } ) );
		Assert.ThrowsException<InvalidDataException>( () => Read( shortRaw ) );
		// A palette record that ends before its entries.
		var shortPalette = Build( new Image( "s", new[] { ImageRecord( 1, 1, new byte[1] ), Palette( 0x2A, 4, new byte[8] ) } ) );
		Assert.ThrowsException<InvalidDataException>( () => Read( shortPalette ) );
	}

	[TestMethod]
	public void RejectsOversizeInputDimensionsAndAggregatePixels()
	{
		Assert.ThrowsException<InvalidDataException>( () => new FshFile( new MemoryStream( new byte[FshFile.MaximumFileBytes + 1] ) ) );
		foreach ( var (width, height) in new[] { (0, 4), (4, 0), (FshFile.MaximumDimension + 1, 1), (1, FshFile.MaximumDimension + 1) } )
		{
			var data = Build( new Image( "d", new[] { new Record( 0x7B, ImageRecord( 1, 1, new byte[1] ).Body ), Palette( 0x24, 1, SampleRgb( 1 ) ) } ) );
			var offset = BinaryPrimitives.ReadInt32LittleEndian( data.AsSpan( 20 ) );
			BinaryPrimitives.WriteUInt16LittleEndian( data.AsSpan( offset + 4 ), (ushort)width );
			BinaryPrimitives.WriteUInt16LittleEndian( data.AsSpan( offset + 6 ), (ushort)height );
			Assert.ThrowsException<InvalidDataException>( () => Read( data ), $"{width}x{height}" );
		}
		// Two maximum-size headers exceed the aggregate budget before any pixel block is read.
		var big = new Image( "big", new[] { new Record( 0xFB, ImageRecord( 1, 1, new byte[1] ).Body ), Palette( 0x24, 1, SampleRgb( 1 ) ) } );
		var aggregate = Build( big, big with { Tag = "big2" } );
		foreach ( var entry in new[] { 20, 28 } )
		{
			var offset = BinaryPrimitives.ReadInt32LittleEndian( aggregate.AsSpan( entry ) );
			BinaryPrimitives.WriteUInt16LittleEndian( aggregate.AsSpan( offset + 4 ), FshFile.MaximumDimension );
			BinaryPrimitives.WriteUInt16LittleEndian( aggregate.AsSpan( offset + 6 ), FshFile.MaximumDimension );
		}
		var exception = Assert.ThrowsException<InvalidDataException>( () => Read( aggregate ) );
		StringAssert.Contains( exception.Message, "pixel limit" );
	}

	[TestMethod]
	public void RejectsUnsupportedRecordCodesAndPaletteVariants()
	{
		var image = ImageRecord( 1, 1, new byte[1] );
		Assert.ThrowsException<NotSupportedException>( () => Read( Build( new Image( "x", new[] { image with { Code = 0x7D }, Palette( 0x24, 1, SampleRgb( 1 ) ) } ) ) ) );
		foreach ( var code in new byte[] { 0x22, 0x23, 0x29, 0x6F } )
			Assert.ThrowsException<NotSupportedException>( () => Read( Build( new Image( "x", new[] { image, Palette( code, 1, new byte[4] ) } ) ) ), $"0x{code:X2}" );
		Assert.ThrowsException<NotSupportedException>( () => Read( Build( new Image( "x", new[] { image } ) ) ) );
		Assert.ThrowsException<NotSupportedException>( () => Read( Build( new Image( "x", new[] { image, Name( "only" ) } ) ) ) );
		Assert.ThrowsException<InvalidDataException>( () => Read( Build( new Image( "x", new[] { image, Palette( 0x24, 1, SampleRgb( 1 ) ), Palette( 0x24, 1, SampleRgb( 1 ) ) } ) ) ) );
		Assert.ThrowsException<InvalidDataException>( () => Read( Build( new Image( "x", new[] { image, Palette( 0x24, 1, SampleRgb( 1 ) ), Name( "a" ), Name( "b" ) } ) ) ) );
		Assert.ThrowsException<InvalidDataException>( () => Read( Build( new Image( "x", new[] { image, Palette( 0x24, 0, Array.Empty<byte>() ) } ) ) ) );
		Assert.ThrowsException<InvalidDataException>( () => Read( Build( new Image( "x", new[] { image, Palette( 0x24, 257, SampleRgb( 257 ) ) } ) ) ) );
		Assert.ThrowsException<InvalidDataException>( () => Read( Build( new Image( "x", new[] { image, Palette( 0x24, 1, SampleRgb( 1 ), height: 2 ) } ) ) ) );
		// Index 2 of a two-entry palette.
		Assert.ThrowsException<InvalidDataException>( () => Read( Build( new Image( "x", new[] { ImageRecord( 1, 1, new byte[] { 2 } ), Palette( 0x24, 2, SampleRgb( 2 ) ) } ) ) ) );
		// Name without NUL inside its bounded record.
		Assert.ThrowsException<InvalidDataException>( () => Read( Build( new Image( "x", new[] { image, Palette( 0x24, 1, SampleRgb( 1 ) ), new Record( 0x70, "abc"u8.ToArray() ) } ) ) ) );
	}

	[TestMethod]
	public void RejectsInvalidRefPackStreams()
	{
		var valid = ImageRecord( 4, 4, new byte[16], compressed: true );
		Record With( Action<byte[]> edit )
		{
			var body = (byte[])valid.Body.Clone();
			edit( body );
			return valid with { Body = body };
		}
		Image Wrap( Record record ) => new( "r", new[] { record, Palette( 0x24, 1, SampleRgb( 1 ) ) } );
		Assert.AreEqual( 16, Read( Build( Wrap( valid ) ) ).Images.Single().Indices.Length );
		Assert.ThrowsException<NotSupportedException>( () => Read( Build( Wrap( With( body => body[12] = 0x11 ) ) ) ) );
		Assert.ThrowsException<InvalidDataException>( () => Read( Build( Wrap( With( body => body[13] = 0xFA ) ) ) ) );
		// Declared size must be the padded pixel count (16 here).
		Assert.ThrowsException<InvalidDataException>( () => Read( Build( Wrap( With( body => body[16] = 32 ) ) ) ) );
		// Back-reference before the start of the output.
		var reference = new byte[12];
		BinaryPrimitives.WriteUInt16LittleEndian( reference, 4 );
		BinaryPrimitives.WriteUInt16LittleEndian( reference.AsSpan( 2 ), 4 );
		var backwards = reference.Concat( new byte[] { 0x10, 0xFB, 0, 0, 16, 0x00, 5 } ).ToArray();
		Assert.ThrowsException<InvalidDataException>( () => Read( Build( Wrap( new Record( 0xFB, backwards ) ) ) ) );
		// Stream that ends inside a literal run (the record's alignment padding is too short to complete it).
		Assert.ThrowsException<InvalidDataException>( () => Read( Build( Wrap( new Record( 0xFB, valid.Body[..18] ) ) ) ) );
		// Output that would overflow the declared size.
		var overflow = reference.Concat( new byte[] { 0x10, 0xFB, 0, 0, 16, 0xE4 } ).Concat( new byte[20] ).Concat( new byte[] { 0xFC } ).ToArray();
		Assert.ThrowsException<InvalidDataException>( () => Read( Build( Wrap( new Record( 0xFB, overflow ) ) ) ) );
	}

	[TestMethod]
	public void EveryTpiFshFileAndWadMemberDecodes()
	{
		var stats = new SortedDictionary<string, int>( StringComparer.Ordinal );
		void Add( string key ) => stats[key] = stats.GetValueOrDefault( key ) + 1;
		var failures = new List<string>();
		foreach ( var (label, data) in Corpus( TpiDataPath() ) )
		{
			Add( label.Contains( ".wad/", StringComparison.OrdinalIgnoreCase ) ? "member" : "loose" );
			try
			{
				var fsh = new FshFile( new MemoryStream( data ) );
				Add( $"id {fsh.Id} images {fsh.Images.Count}" );
				var gap = "Buy ERTS"u8.ToArray().Concat( new byte[80] ).ToArray();
				Add( BinaryPrimitives.ReadUInt32LittleEndian( data.AsSpan( 20 ) ) == 112 && data.AsSpan( 24, 88 ).SequenceEqual( gap ) ? "image at 112 after Buy ERTS gap" : "other directory gap" );
				var stem = Path.GetFileNameWithoutExtension( label );
				Add( string.Equals( fsh.Images[0].Tag, stem[..Math.Min( 4, stem.Length )], StringComparison.OrdinalIgnoreCase ) ? "tag is name prefix" : "tag differs from name" );
				foreach ( var image in fsh.Images )
				{
					Add( image.Compressed ? "code 0xFB" : "code 0x7B" );
					Add( $"palette {image.PaletteFormat}" );
					Add( image.PaletteEntries == 256 ? $"palette {image.PaletteFormat} 256 entries" : $"palette {image.PaletteFormat} fewer entries" );
					Add( image.Width * image.Height % 16 == 0 ? $"code {(image.Compressed ? "0xFB" : "0x7B")} aligned" : $"code {(image.Compressed ? "0xFB" : "0x7B")} padded" );
					Add( image.Name != null ? "named" : "unnamed" );
					Add( (image.CenterX | image.CenterY | image.PositionX | image.PositionY) == 0 ? "position words zero" : "position words set" );
					Add( $"size {image.Width}x{image.Height}" );
				}
			}
			catch ( Exception exception ) when ( exception is InvalidDataException or NotSupportedException )
			{
				failures.Add( $"{label}: {exception.Message}" );
			}
		}
		Assert.AreEqual( 0, failures.Count, string.Join( Environment.NewLine, failures.Take( 20 ) ) );
		var expected = new Dictionary<string, int>
		{
			["loose"] = 162,
			["member"] = 7121,
			["id G231 images 1"] = 7283,
			["code 0x7B"] = 547,
			["code 0xFB"] = 6736,
			["code 0x7B aligned"] = 547,
			["code 0xFB aligned"] = 6601,
			["code 0xFB padded"] = 135,
			["palette Rgb24"] = 4308,
			["palette Bgra32"] = 2974,
			["palette Argb1555"] = 1,
			["palette Rgb24 256 entries"] = 4308,
			["palette Bgra32 256 entries"] = 414,
			["palette Bgra32 fewer entries"] = 2560,
			["palette Argb1555 256 entries"] = 1,
			["image at 112 after Buy ERTS gap"] = 7283,
			["tag is name prefix"] = 7101,
			["tag differs from name"] = 182,
			["named"] = 6649,
			["unnamed"] = 634,
			["position words zero"] = 7283,
			["size 32x32"] = 3001,
			["size 128x128"] = 2009,
			["size 64x64"] = 1840,
		};
		foreach ( var (key, count) in expected )
			Assert.AreEqual( count, stats.GetValueOrDefault( key ), key );
		Assert.AreEqual( 26, stats.Keys.Count( key => key.StartsWith( "size " ) ), "distinct sizes" );
	}

	[TestMethod]
	[DataRow( "generic/shadow/alphkid.fsh", FshPaletteFormat.Bgra32, 64, 64, true,
		"3d9aa0ad6957cc5558066418de7bdd8061fa77ce7d1588aa3fb1400996822122", "d9adb81b8d57c9a4f34c4ea0592e6f47b04e9eae4736a6eabacccddfbe907bd9" )]
	[DataRow( "levels/water/rides/snowtrac.wad/stexture/icewall1.fsh", FshPaletteFormat.Argb1555, 32, 32, true,
		"1a3ecb2f79537ad8d23091fa049e64d98c40ea8aff746ef258a48359f02f6dd0", "aefb6b56c12487f023746a8b057edda235ac5ce59189af41c50bea2b8eb57b4e" )]
	[DataRow( "ui.wad/stexture/tb_camera.fsh", FshPaletteFormat.Bgra32, 38, 38, true,
		"1bad3bed574e7f823d0166e70a17d89f6c9f11a51b819f3b4395734bf302957d", "75d874e1d3fc656e115c7626e796ac8b94a948963f5d4244be277434f5c3d9e7" )]
	[DataRow( "levels/arabian/Sharetex.wad/an_g07.fsh", FshPaletteFormat.Rgb24, 128, 128, false,
		"6246087bd4c5803d728d5b2c1dbe4b13a8d5d6e84862a7d3effd5ea9cfc1a057", "35175b6940013fd3c99f1494e5741d1f839c6d7735f0637a99919fc4ca409baa" )]
	[DataRow( "global/Advisor/textures/Mutant_Eye.fsh", FshPaletteFormat.Rgb24, 64, 64, true,
		"3ee747db7a0f9bb0a80a2c3ffd3ac0591782e884b8511c07c202c6222b0eb91c", "1f0bd89426b4cf12b72316f1b1c3967821355c70d6e4ecb806219111dfaf3b1a" )]
	public void PinnedTpiFixturesDecodeToKnownRgba( string label, FshPaletteFormat format, int width, int height, bool compressed, string fileSha256, string rgbaSha256 )
	{
		byte[]? data = Corpus( TpiDataPath(), label ).SingleOrDefault().Data;
		if ( data == null )
		{
			Assert.Inconclusive( $"TPI fixture is missing: {label}" );
			return;
		}
		var image = new FshFile( new MemoryStream( data ) ).Images.Single();
		Assert.AreEqual( $"{label} {fileSha256} {format} {width}x{height} {compressed} {rgbaSha256}",
			$"{label} {Convert.ToHexString( SHA256.HashData( data ) ).ToLowerInvariant()} {image.PaletteFormat} {image.Width}x{image.Height} {image.Compressed} {Convert.ToHexString( SHA256.HashData( image.Rgba ) ).ToLowerInvariant()}" );
	}

	/// <summary>
	/// Loose textures that ship next to a small TGA of the same name (decoded by StbImageSharp, an existing
	/// dependency): the decoded FSH, box-filtered to the TGA size, must match it far better than with red and
	/// blue swapped, and alpha must match for 32-bit TGAs. This fixes the palette byte orders and 0x2A alpha.
	/// </summary>
	[TestMethod]
	public void LooseTexturesMatchTheirShippedTgaThumbnails()
	{
		var root = TpiDataPath();
		var pairs = 0;
		var error = new Dictionary<string, double[]>();
		var count = new Dictionary<string, int>();
		foreach ( var path in Directory.EnumerateFiles( root, "*.*", SearchOption.AllDirectories ).Where( file => file.EndsWith( ".fsh", StringComparison.OrdinalIgnoreCase ) ).Order( StringComparer.Ordinal ) )
		{
			var tga = Directory.EnumerateFiles( Path.GetDirectoryName( path )! ).FirstOrDefault( file =>
				string.Equals( Path.GetFileName( file ), Path.GetFileNameWithoutExtension( path ) + ".tga", StringComparison.OrdinalIgnoreCase ) );
			if ( tga == null )
				continue;
			var image = new FshFile( new MemoryStream( File.ReadAllBytes( path ) ) ).Images.Single();
			var thumbnail = ImageResult.FromMemory( File.ReadAllBytes( tga ), ColorComponents.RedGreenBlueAlpha );
			Assert.AreEqual( 0, image.Width % thumbnail.Width, path );
			Assert.AreEqual( 0, image.Height % thumbnail.Height, path );
			var scaleX = image.Width / thumbnail.Width;
			var scaleY = image.Height / thumbnail.Height;
			var key = $"{image.PaletteFormat} tga{(thumbnail.SourceComp == ColorComponents.RedGreenBlueAlpha ? 32 : 24)}";
			var sums = error.TryGetValue( key, out var existing ) ? existing : error[key] = new double[5];
			for ( var ty = 0; ty < thumbnail.Height; ty++ )
				for ( var tx = 0; tx < thumbnail.Width; tx++ )
				{
					var mean = new double[4];
					for ( var y = ty * scaleY; y < (ty + 1) * scaleY; y++ )
						for ( var x = tx * scaleX; x < (tx + 1) * scaleX; x++ )
							for ( var channel = 0; channel < 4; channel++ )
								mean[channel] += image.Rgba[(y * image.Width + x) * 4 + channel] / (double)(scaleX * scaleY);
					var target = thumbnail.Data.AsSpan( (ty * thumbnail.Width + tx) * 4, 4 );
					var pixels = (double)thumbnail.Width * thumbnail.Height;
					sums[0] += Math.Abs( mean[0] - target[0] ) / pixels;
					sums[1] += Math.Abs( mean[1] - target[1] ) / pixels;
					sums[2] += Math.Abs( mean[2] - target[2] ) / pixels;
					sums[3] += Math.Abs( mean[3] - target[3] ) / pixels;
					sums[4] += (Math.Abs( mean[2] - target[0] ) + Math.Abs( mean[0] - target[2] )) / 2 / pixels;
				}
			count[key] = count.GetValueOrDefault( key ) + 1;
			pairs++;
		}
		Assert.AreEqual( 70, pairs );
		double Mean( string key, int column ) => error[key][column] / count[key];
		// 0x2A: colour and alpha within a few levels; swapping red and blue is two orders of magnitude worse.
		Assert.AreEqual( 23, count["Bgra32 tga32"] );
		for ( var channel = 0; channel < 4; channel++ )
			Assert.IsTrue( Mean( "Bgra32 tga32", channel ) < 3, $"0x2A channel {channel}: {Mean( "Bgra32 tga32", channel )}" );
		Assert.IsTrue( Mean( "Bgra32 tga32", 4 ) > 50 );
		// 0x24: colour within about ten levels (coarser thumbnails); swapped red/blue is far worse.
		Assert.AreEqual( 43, count["Rgb24 tga24"] );
		for ( var channel = 0; channel < 3; channel++ )
			Assert.IsTrue( Mean( "Rgb24 tga24", channel ) < 12, $"0x24 channel {channel}: {Mean( "Rgb24 tga24", channel )}" );
		Assert.IsTrue( Mean( "Rgb24 tga24", 4 ) > 50 );
		// Four grey 0x24 badges have 32-bit thumbnails whose alpha is 0 everywhere, while OpenTPW decodes
		// 0x24 as opaque (approximation COMPAT-014); this pins that the thumbnails do not support the choice.
		Assert.AreEqual( 4, count["Rgb24 tga32"] );
		Assert.AreEqual( 255, Mean( "Rgb24 tga32", 3 ), 1e-9 );
		for ( var channel = 0; channel < 3; channel++ )
			Assert.IsTrue( Mean( "Rgb24 tga32", channel ) < 12 );
		Assert.AreEqual( 3, count.Count );
	}

	/// <summary>
	/// The same texture often ships twice in one folder tree (<c>stexture/</c> and <c>textures/</c>) with
	/// different palette record codes and often a different size. Their decoded mean colours must agree, and
	/// agree better than with red and blue swapped in one of them.
	/// </summary>
	[TestMethod]
	public void SameTextureWithDifferentPaletteCodesDecodesToTheSameColours()
	{
		var images = new Dictionary<string, List<FshImage>>( StringComparer.OrdinalIgnoreCase );
		foreach ( var (label, data) in Corpus( TpiDataPath() ) )
		{
			var parts = label.Split( '/' );
			if ( parts.Length < 2 || !(parts[^2].Equals( "stexture", StringComparison.OrdinalIgnoreCase ) || parts[^2].Equals( "textures", StringComparison.OrdinalIgnoreCase )) )
				continue;
			var key = string.Join( '/', parts[..^2].Append( parts[^1] ) );
			var image = new FshFile( new MemoryStream( data ) ).Images.Single();
			(images.TryGetValue( key, out var list ) ? list : images[key] = new()).Add( image );
		}
		// Colours under transparent texels are not visible, so only pairs whose 0x2A copy is mostly opaque
		// (mean alpha at least 200) are compared; the others hide part of the picture in one copy only.
		var compared = new List<(double Same, double Swapped)>();
		foreach ( var group in images.Values.Where( list => list.Count == 2 && list[0].PaletteFormat != list[1].PaletteFormat ) )
		{
			if ( group.Any( image => MeanChannel( image, 3 ) < 200 ) )
				continue;
			double Difference( int a, int b ) => Math.Abs( MeanChannel( group[0], a ) - MeanChannel( group[1], b ) );
			compared.Add( (new[] { Difference( 0, 0 ), Difference( 1, 1 ), Difference( 2, 2 ) }.Max(), new[] { Difference( 0, 2 ), Difference( 1, 1 ), Difference( 2, 0 ) }.Max()) );
		}
		Assert.AreEqual( 12, compared.Count );
		Assert.IsTrue( compared.All( pair => pair.Same < 2 ), string.Join( ", ", compared ) );
		Assert.IsTrue( compared.All( pair => pair.Swapped > pair.Same ), string.Join( ", ", compared ) );
		Assert.AreEqual( 11, compared.Count( pair => pair.Swapped > 5 ) );
	}

	private static double MeanChannel( FshImage image, int channel )
	{
		var sum = 0L;
		for ( var index = channel; index < image.Rgba.Length; index += 4 )
			sum += image.Rgba[index];
		return sum / (double)(image.Width * image.Height);
	}

	/// <summary>Every loose <c>.fsh</c> file and every <c>.fsh</c> WAD member under the TPI data folder, labelled by relative path (<c>archive.wad/member</c>).</summary>
	private static IEnumerable<(string Label, byte[] Data)> Corpus( string root, string? only = null )
	{
		foreach ( var file in Directory.EnumerateFiles( root, "*", SearchOption.AllDirectories ).Order( StringComparer.Ordinal ) )
		{
			var relative = Path.GetRelativePath( root, file ).Replace( '\\', '/' );
			if ( file.EndsWith( ".fsh", StringComparison.OrdinalIgnoreCase ) )
			{
				if ( only == null || relative == only )
					yield return (relative, File.ReadAllBytes( file ));
			}
			else if ( file.EndsWith( ".wad", StringComparison.OrdinalIgnoreCase ) && (only == null || only.StartsWith( relative + "/", StringComparison.Ordinal )) )
			{
				using var archive = new WadArchive( file );
				foreach ( var (name, member) in WadMembers( archive.Root, "" ) )
				{
					if ( !name.EndsWith( ".fsh", StringComparison.OrdinalIgnoreCase ) || (only != null && $"{relative}/{name}" != only) )
						continue;
					var data = member.GetData();
					member.Free();
					yield return ($"{relative}/{name}", data);
				}
			}
		}
	}

	private static IEnumerable<(string Name, WadArchiveFile File)> WadMembers( ArchiveDirectory directory, string prefix )
	{
		foreach ( var child in directory.Children )
		{
			if ( child is ArchiveDirectory subdirectory )
			{
				foreach ( var entry in WadMembers( subdirectory, $"{prefix}{subdirectory.Name}/" ) )
					yield return entry;
			}
			else if ( child is WadArchiveFile file )
			{
				yield return ($"{prefix}{file.Name}", file);
			}
		}
	}

	/// <summary>
	/// <c>OPENTPW_TPI_PATH</c>: a Theme Park Inc installation (or its extracted <c>Data</c> folder). Read in place;
	/// nothing is copied.
	/// </summary>
	private static string TpiDataPath()
	{
		var path = Environment.GetEnvironmentVariable( "OPENTPW_TPI_PATH" );
		if ( string.IsNullOrWhiteSpace( path ) || !Directory.Exists( path ) )
			Assert.Inconclusive( "Set OPENTPW_TPI_PATH to a Theme Park Inc installation or its Data folder for the FSH corpus." );
		return Directory.EnumerateDirectories( path! ).FirstOrDefault( directory => string.Equals( Path.GetFileName( directory ), "data", StringComparison.OrdinalIgnoreCase ) ) ?? path!;
	}

	private sealed class TrickleStream( byte[] data ) : Stream
	{
		private int position;
		public override bool CanRead => true;
		public override bool CanSeek => false;
		public override bool CanWrite => false;
		public override long Length => throw new NotSupportedException();
		public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
		public override void Flush() { }
		public override int Read( byte[] buffer, int offset, int count )
		{
			var read = Math.Min( Math.Min( count, 3 ), data.Length - position );
			Array.Copy( data, position, buffer, offset, read );
			position += read;
			return read;
		}
		public override long Seek( long offset, SeekOrigin origin ) => throw new NotSupportedException();
		public override void SetLength( long value ) => throw new NotSupportedException();
		public override void Write( byte[] buffer, int offset, int count ) => throw new NotSupportedException();
	}
}

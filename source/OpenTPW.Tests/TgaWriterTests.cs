using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using StbImageSharp;

namespace OpenTPW.Tests;

/// <summary>The TGA writer of the content kit (docs/CONTENT-KIT.md, "Textures"): the variant every shipped .tga uses, read back with stb, and against the corpus.</summary>
[TestClass]
public class TgaWriterTests
{
	/// <summary>A 3 x 2 image with a different colour in every pixel; row 0 is the top row.</summary>
	private static byte[] Pixels( byte alpha = 255 ) => new byte[]
	{
		255, 0, 0, alpha, 0, 255, 0, alpha, 0, 0, 255, alpha,
		10, 20, 30, alpha, 40, 50, 60, alpha, 70, 80, 90, alpha,
	};

	[TestMethod]
	public void AnOpaqueImageIsWrittenAs24BitWithTheCorpusHeaderAndFooter()
	{
		var tga = TgaWriter.Write( 3, 2, Pixels() );
		Assert.AreEqual( 18 + 3 * 2 * 3 + 26, tga.Length );
		CollectionAssert.AreEqual( new byte[] { 0, 0, 2, 0, 0, 0, 0, 0, 0, 0, 0, 0, 3, 0, 2, 0, 24, 0 }, tga[..18], "no id, no colour map, type 2, origin 0, 3 x 2, 24 bits, descriptor 0 (bottom-left)" );
		Assert.AreEqual( "TRUEVISION-XFILE.\0", Encoding.ASCII.GetString( tga, tga.Length - 18, 18 ) );
		CollectionAssert.AreEqual( new byte[8], tga[^26..^18], "no extension area and no developer area" );
	}

	[TestMethod]
	public void RowsAreStoredBottomToTopInBgrOrder()
	{
		var tga = TgaWriter.Write( 3, 2, Pixels() );
		// the first stored pixel is the bottom-left one (10, 20, 30), as B, G, R
		CollectionAssert.AreEqual( new byte[] { 30, 20, 10 }, tga[18..21] );
		// the first pixel of the second stored row is the top-left one (255, 0, 0)
		CollectionAssert.AreEqual( new byte[] { 0, 0, 255 }, tga[27..30] );
	}

	[TestMethod]
	public void AnImageWithAlphaIsWrittenAs32BitWithEightAlphaBits()
	{
		var rgba = Pixels();
		rgba[3] = 128;
		var tga = TgaWriter.Write( 3, 2, rgba );
		Assert.AreEqual( 18 + 3 * 2 * 4 + 26, tga.Length );
		Assert.AreEqual( 32, tga[16] );
		Assert.AreEqual( 0x08, tga[17] );
	}

	[TestMethod]
	public void StbReadsBackWhatWasWritten()
	{
		foreach ( var alpha in new byte[] { 255, 100 } )
		{
			var written = Pixels( alpha );
			var image = ImageResult.FromMemory( TgaWriter.Write( 3, 2, written ), ColorComponents.RedGreenBlueAlpha );
			Assert.AreEqual( (3, 2), (image.Width, image.Height) );
			CollectionAssert.AreEqual( written, image.Data, $"alpha {alpha}: top row first, straight alpha" );
		}
	}

	[TestMethod]
	public void BadSizesAreRefused()
	{
		Assert.ThrowsException<ArgumentOutOfRangeException>( () => TgaWriter.Write( 0, 1, Array.Empty<byte>() ) );
		Assert.ThrowsException<ArgumentOutOfRangeException>( () => TgaWriter.Write( TgaWriter.MaximumDimension + 1, 1, new byte[( TgaWriter.MaximumDimension + 1 ) * 4] ) );
		Assert.ThrowsException<ArgumentException>( () => TgaWriter.Write( 2, 2, new byte[15] ) );
	}

	/// <summary>
	/// Every shipped uncompressed true-colour TGA without an extension area, decoded by stb and written again, is the original byte for byte (a 32-bit file with
	/// no translucent pixel is written as 24-bit, so it is skipped): the variant is the game's own, not just one stb happens to read.
	/// </summary>
	[TestMethod]
	public void EveryShippedTrueColourTgaIsWrittenBackByteForByte()
	{
		var data = Md2CorpusTests.OriginalDataPath();
		var members = new List<(string Label, byte[] Bytes)>();
		foreach ( var file in Directory.EnumerateFiles( data, "*.tga", SearchOption.AllDirectories ) )
			members.Add( (Path.GetRelativePath( data, file ), File.ReadAllBytes( file )) );
		foreach ( var wad in Directory.EnumerateFiles( data, "*.wad", SearchOption.AllDirectories ) )
		{
			using var archive = new WadArchive( wad );
			foreach ( var (name, file) in Members( archive.Root, "" ) )
			{
				members.Add( ($"{Path.GetRelativePath( data, wad )}/{name}", file.GetData()) );
				file.Free();
			}
		}
		int compared = 0, identical = 0, skipped = 0;
		var different = new List<string>();
		foreach ( var (label, bytes) in members )
		{
			var width = bytes[12] | bytes[13] << 8;
			var height = bytes[14] | bytes[15] << 8;
			var depth = bytes[16] / 8;
			if ( bytes[2] != 2 || bytes[0] != 0 || bytes.Length != 18 + width * height * depth + 26 )
			{
				skipped++;
				continue;
			}
			var image = ImageResult.FromMemory( bytes, ColorComponents.RedGreenBlueAlpha );
			if ( depth == 4 && !TgaWriter.HasAlpha( image.Data ) )
			{
				skipped++;
				continue;
			}
			compared++;
			if ( TgaWriter.Write( image.Width, image.Height, image.Data ).AsSpan().SequenceEqual( bytes ) )
				identical++;
			else
				different.Add( label );
		}
		Console.WriteLine( $"TGA writer: {identical} of {compared} shipped true-colour files written back byte for byte ({skipped} skipped: paletted, extension area or opaque 32-bit)" );
		Assert.IsTrue( compared > 100, "the corpus has 156 files of this shape" );
		Assert.AreEqual( 0, different.Count, string.Join( Environment.NewLine, different.Take( 10 ) ) );
	}

	private static IEnumerable<(string Name, WadArchiveFile File)> Members( ArchiveDirectory directory, string prefix )
	{
		foreach ( var child in directory.Children )
		{
			if ( child is ArchiveDirectory subdirectory )
			{
				foreach ( var entry in Members( subdirectory, $"{prefix}{subdirectory.Name}/" ) )
					yield return entry;
			}
			else if ( child is WadArchiveFile file && file.Name!.EndsWith( ".tga", StringComparison.OrdinalIgnoreCase ) )
				yield return ($"{prefix}{file.Name}", file);
		}
	}
}

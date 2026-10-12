using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTPW.UI.Original;
using StbImageSharp;

namespace OpenTPW.Tests;

/// <summary>
/// The predecoded store for the shipped hero interface art (Render/Assets/PredecodedUiArt.cs, docs/WEB.md):
/// round-trips through the production encode/load pair, the PNG-vs-sibling behaviour of TexturePack.Find and its
/// consumers, and the strict malformed-input handling of the loader.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class PredecodedUiArtTests
{
	private static string RepositoryRoot()
	{
		var directory = new DirectoryInfo( AppContext.BaseDirectory );
		while ( directory != null && !(Directory.Exists( Path.Combine( directory.FullName, "content", "hero-art" ) ) && Directory.Exists( Path.Combine( directory.FullName, "source" ) )) )
			directory = directory.Parent;
		Assert.IsNotNull( directory, "The repository checkout must be available for this test." );
		return directory!.FullName;
	}

	private static string HeroArtDirectory() => Path.Combine( RepositoryRoot(), "content", "hero-art", "textures", "ui", "textures" );

	/// <summary>The whole shipped set: every PNG decodes, encodes and loads back to the exact StbImageSharp bytes the PNG path produces.</summary>
	[TestMethod]
	public void EveryShippedHeroArtPngRoundTripsThroughThePredecodedStore()
	{
		var files = Directory.GetFiles( HeroArtDirectory(), "*.wct.png" ).Order().ToArray();
		Assert.AreEqual( 55, files.Length, "The shipped hero art set changed; update this pin when it does." );
		foreach ( var file in files )
		{
			var png = File.ReadAllBytes( file );
			var expected = ImageResult.FromMemory( png, ColorComponents.RedGreenBlueAlpha );
			var (width, height, rgba) = PredecodedUiArt.Load( PredecodedUiArt.EncodePng( png ) );
			Assert.AreEqual( expected.Width, width, $"{Path.GetFileName( file )} width" );
			Assert.AreEqual( expected.Height, height, $"{Path.GetFileName( file )} height" );
			CollectionAssert.AreEqual( expected.Data, rgba, $"{Path.GetFileName( file )} pixels" );
		}
	}

	/// <summary>The store is pre-key because no shipped texel is pink; this pins that fact, which the unconditional KeyOutPink relies on.</summary>
	[TestMethod]
	public void ShippedHeroArtCarriesNoPinkTexels()
	{
		foreach ( var file in Directory.GetFiles( HeroArtDirectory(), "*.wct.png" ).Order() )
		{
			var decoded = ImageResult.FromMemory( File.ReadAllBytes( file ), ColorComponents.RedGreenBlueAlpha ).Data;
			for ( var pixel = 0; pixel < decoded.Length; pixel += 4 )
				Assert.IsFalse( decoded[pixel] == 255 && decoded[pixel + 1] == 0 && decoded[pixel + 2] == 255,
					$"{Path.GetFileName( file )} has a pink texel at byte {pixel}; the pre-keyed store would no longer match keyed loads." );
		}
	}

	/// <summary>Encoding the same input twice must produce the same file, so unchanged builds stage byte-stable assets.</summary>
	[TestMethod]
	public void EncodingTheSamePngTwiceIsByteIdentical()
	{
		var png = File.ReadAllBytes( Path.Combine( HeroArtDirectory(), "b_okay.wct.png" ) );
		var first = PredecodedUiArt.EncodePng( png );
		var second = PredecodedUiArt.EncodePng( png );
		CollectionAssert.AreEqual( first, second );
	}

	/// <summary>Pins the on-disk layout the generator writes and the web build embeds.</summary>
	[TestMethod]
	public void EncodedAssetsCarryThePinnedHeader()
	{
		var file = PredecodedUiArt.Encode( new byte[4 * 3 * 2], 3, 2 );
		Assert.AreEqual( "OPRG", Encoding.ASCII.GetString( file, 0, 4 ) );
		Assert.AreEqual( PredecodedUiArt.FormatVersion, BinaryPrimitives.ReadUInt16LittleEndian( file.AsSpan( 4 ) ) );
		Assert.AreEqual( PredecodedUiArt.CodecZlib, BinaryPrimitives.ReadUInt16LittleEndian( file.AsSpan( 6 ) ) );
		Assert.AreEqual( 3u, BinaryPrimitives.ReadUInt32LittleEndian( file.AsSpan( 8 ) ) );
		Assert.AreEqual( 2u, BinaryPrimitives.ReadUInt32LittleEndian( file.AsSpan( 12 ) ) );
		Assert.AreEqual( (uint)(file.Length - PredecodedUiArt.HeaderSize), BinaryPrimitives.ReadUInt32LittleEndian( file.AsSpan( 16 ) ) );
	}

	/// <summary>Transparent texels keep their stored colour (no premultiplication anywhere), and pink stays unkeyed in the raw store.</summary>
	[TestMethod]
	public void TransparentRgbAndPinkSurviveTheStoreExactly()
	{
		var pixels = new byte[4 * 4];
		(pixels[0], pixels[1], pixels[2], pixels[3]) = ((byte)200, (byte)100, (byte)50, (byte)0);
		(pixels[4], pixels[5], pixels[6], pixels[7]) = ((byte)10, (byte)20, (byte)30, (byte)255);
		(pixels[8], pixels[9], pixels[10], pixels[11]) = ((byte)255, (byte)0, (byte)255, (byte)255);
		(pixels[12], pixels[13], pixels[14], pixels[15]) = ((byte)255, (byte)0, (byte)255, (byte)0);
		using var png = new MemoryStream();
		PngWriter.Write( png, pixels, 4, 1 );
		var crafted = png.ToArray();

		var expected = ImageResult.FromMemory( crafted, ColorComponents.RedGreenBlueAlpha ).Data;
		var (_, _, rgba) = PredecodedUiArt.Load( PredecodedUiArt.EncodePng( crafted ) );
		CollectionAssert.AreEqual( expected, rgba );
		CollectionAssert.AreEqual( new byte[] { 200, 100, 50, 0, 10, 20, 30, 255, 255, 0, 255, 255, 255, 0, 255, 0 }, rgba );
	}

	/// <summary>The strict malformed matrix: every bad header, dimension or length is an InvalidDataException, like other failed UI images.</summary>
	[TestMethod]
	public void MalformedPredecodedAssetsAreRejected()
	{
		var valid = PredecodedUiArt.Encode( Enumerable.Range( 0, 4 * 3 * 2 ).Select( value => (byte)value ).ToArray(), 3, 2 );
		byte[] Mutant( Action<byte[]> patch )
		{
			var mutant = (byte[])valid.Clone();
			patch( mutant );
			return mutant;
		}
		var cases = new List<(string Name, byte[] File)>
		{
			( "empty file", Array.Empty<byte>() ),
			( "header truncated", valid[..(PredecodedUiArt.HeaderSize - 1)] ),
			( "bad magic", Mutant( mutant => mutant[0] = (byte)'X' ) ),
			( "future version", Mutant( mutant => BinaryPrimitives.WriteUInt16LittleEndian( mutant.AsSpan( 4 ), (ushort)(PredecodedUiArt.FormatVersion + 1) ) ) ),
			( "unknown codec", Mutant( mutant => BinaryPrimitives.WriteUInt16LittleEndian( mutant.AsSpan( 6 ), (ushort)(PredecodedUiArt.CodecZlib + 1) ) ) ),
			( "zero width", Mutant( mutant => BinaryPrimitives.WriteUInt32LittleEndian( mutant.AsSpan( 8 ), 0 ) ) ),
			( "zero height", Mutant( mutant => BinaryPrimitives.WriteUInt32LittleEndian( mutant.AsSpan( 12 ), 0 ) ) ),
			( "width over bound", Mutant( mutant => BinaryPrimitives.WriteUInt32LittleEndian( mutant.AsSpan( 8 ), (uint)(PredecodedUiArt.MaxDimension + 1) ) ) ),
			( "height over bound", Mutant( mutant => BinaryPrimitives.WriteUInt32LittleEndian( mutant.AsSpan( 12 ), (uint)(PredecodedUiArt.MaxDimension + 1) ) ) ),
			( "texel count over bound", Mutant( mutant => { BinaryPrimitives.WriteUInt32LittleEndian( mutant.AsSpan( 8 ), 8192 ); BinaryPrimitives.WriteUInt32LittleEndian( mutant.AsSpan( 12 ), 2049 ); } ) ),
			( "declared length over file", Mutant( mutant => BinaryPrimitives.WriteUInt32LittleEndian( mutant.AsSpan( 16 ), (uint)(valid.Length - PredecodedUiArt.HeaderSize + 1) ) ) ),
			( "declared length under file", Mutant( mutant => BinaryPrimitives.WriteUInt32LittleEndian( mutant.AsSpan( 16 ), (uint)(valid.Length - PredecodedUiArt.HeaderSize - 1) ) ) ),
			( "trailing byte", [.. valid, 0] ),
			// The declared length matches the shorter file, so the defect is the deflate data ending before the pixels do.
			( "deflate stream cut short", Mutant( mutant => BinaryPrimitives.WriteUInt32LittleEndian( mutant.AsSpan( 16 ), (uint)(valid.Length - PredecodedUiArt.HeaderSize - 12) ) )[..^12] ),
			// The payload inflates past the declared dimensions.
			( "smaller dims, long payload", Mutant( mutant => BinaryPrimitives.WriteUInt32LittleEndian( mutant.AsSpan( 12 ), 1 ) ) ),
			( "corrupt deflate middle", Mutant( mutant => mutant[PredecodedUiArt.HeaderSize + 8] = (byte)~mutant[PredecodedUiArt.HeaderSize + 8] ) ),
		};
		foreach ( var (name, mutant) in cases )
			Assert.ThrowsException<InvalidDataException>( () => PredecodedUiArt.Load( mutant ), name );
	}

	/// <summary>The generator's own bounds: dimensions and pixel counts are refused before anything is compressed.</summary>
	[TestMethod]
	public void EncodingRejectsOutOfBoundsAndMismatchedBuffers()
	{
		Assert.ThrowsException<ArgumentOutOfRangeException>( () => PredecodedUiArt.Encode( new byte[4], 0, 1 ) );
		Assert.ThrowsException<ArgumentOutOfRangeException>( () => PredecodedUiArt.Encode( new byte[4], 1, 0 ) );
		Assert.ThrowsException<ArgumentOutOfRangeException>( () => PredecodedUiArt.Encode( new byte[4], PredecodedUiArt.MaxDimension + 1, 1 ) );
		Assert.ThrowsException<ArgumentOutOfRangeException>( () => PredecodedUiArt.Encode( new byte[16], 8192, 2049 ) );
		Assert.ThrowsException<ArgumentException>( () => PredecodedUiArt.Encode( new byte[5], 1, 1 ) );
	}

	/// <summary>Find keeps its precedence: the PNG wins over a predecoded sibling, the sibling only when the PNG is absent, packs and flags unchanged.</summary>
	[TestMethod]
	public void FindUsesThePredecodedSiblingOnlyWhenThePngIsAbsent()
	{
		var root = Path.Combine( Path.GetTempPath(), "opentpw-predecoded-find-" + Guid.NewGuid().ToString( "N" ) );
		var previousShipped = TexturePack.ShippedDirectory;
		try
		{
			var shipped = Path.Combine( root, "shipped", "textures" );
			Directory.CreateDirectory( Path.Combine( shipped, "ui", "textures" ) );
			var png = Path.Combine( shipped, "ui", "textures", "b_buy.wct.png" );
			var sibling = Path.Combine( shipped, "ui", "textures", "b_buy.wct.rgba" );
			TexturePack.ShippedDirectory = shipped;
			TexturePack.Activate( "", new List<string>() );
			File.WriteAllBytes( png, new byte[1] );
			File.WriteAllBytes( sibling, new byte[1] );
			Assert.AreEqual( png, TexturePack.Find( "/ui/textures/b_buy.wct" ) );
			File.Delete( png );
			Assert.AreEqual( sibling, TexturePack.Find( "/ui/textures/b_buy.wct" ) );
			File.Delete( sibling );
			Assert.IsNull( TexturePack.Find( "/ui/textures/b_buy.wct" ) );
			// With Original HUD/icons, interface art uses neither the PNG nor the sibling.
			File.WriteAllBytes( sibling, new byte[1] );
			TexturePack.Activate( "", new List<string>(), enhancedInterface: false );
			Assert.IsNull( TexturePack.Find( "/ui/textures/b_buy.wct" ) );
			// An active default pack keeps its precedence: the pack PNG wins over the shipped sibling.
			TexturePack.Activate( "", new List<string>() );
			var packs = Path.Combine( root, "packs" );
			var pack = Path.Combine( packs, TexturePack.DefaultName );
			Directory.CreateDirectory( Path.Combine( pack, "textures", "ui", "textures" ) );
			File.WriteAllText( Path.Combine( pack, "pack.json" ), "{\"Format\":1}" );
			var packed = Path.Combine( pack, "textures", "ui", "textures", "b_buy.wct.png" );
			File.WriteAllBytes( packed, new byte[1] );
			TexturePack.Activate( TexturePack.DefaultName, new List<string>(), packs, packs );
			Assert.AreEqual( packed, TexturePack.Find( "/ui/textures/b_buy.wct" ) );
		}
		finally
		{
			TexturePack.ShippedDirectory = previousShipped;
			TexturePack.Activate( "", new List<string>() );
			Directory.Delete( root, true );
		}
	}

	/// <summary>UiImages.Load (pink key included) and LoadKeyed (black key) return the same bytes for a predecoded sibling as for the PNG it replaces.</summary>
	[TestMethod]
	public void UiImagesLoadsPredecodedSiblingsLikeThePngsTheyReplace()
	{
		var root = Path.Combine( Path.GetTempPath(), "opentpw-predecoded-uiimages-" + Guid.NewGuid().ToString( "N" ) );
		var previousShipped = TexturePack.ShippedDirectory;
		try
		{
			// One opaque crafted image exercises the #black-key path (the shipped hero art is not opaque everywhere).
			const int width = 9, height = 7;
			var pixels = new byte[width * height * 4];
			for ( var texel = 0; texel < width * height; texel++ )
			{
				pixels[texel * 4] = (byte)(texel * 3 % 251);
				pixels[texel * 4 + 1] = (byte)(texel % 239);
				pixels[texel * 4 + 2] = 30;
				pixels[texel * 4 + 3] = 255;
			}
			using var crafted = new MemoryStream();
			PngWriter.Write( crafted, pixels, width, height );
			var sources = new List<(string Name, byte[] Png)> { ("crafted.wct.png", crafted.ToArray()) };
			foreach ( var name in new[] { "b_buy.wct.png", "b_okay.wct.png", "b_info.wct.png" } )
				sources.Add( (name, File.ReadAllBytes( Path.Combine( HeroArtDirectory(), name ) )) );

			TexturePack.Activate( "", new List<string>() );
			foreach ( var (name, png) in sources )
			{
				var pngShipped = Path.Combine( root, "png", "textures" );
				var rgbaShipped = Path.Combine( root, "rgba", "textures" );
				var pngPath = Path.Combine( pngShipped, "ui", "textures", name );
				var rgbaPath = Path.Combine( rgbaShipped, "ui", "textures", PredecodedUiArt.SiblingPath( name )! );
				Directory.CreateDirectory( Path.GetDirectoryName( pngPath )! );
				Directory.CreateDirectory( Path.GetDirectoryName( rgbaPath )! );
				File.WriteAllBytes( pngPath, png );
				File.WriteAllBytes( rgbaPath, PredecodedUiArt.EncodePng( png ) );

				var gamePath = $"ui/textures/{name[..^".png".Length]}";
				TexturePack.ShippedDirectory = pngShipped;
				var (pngWidth, pngHeight, pngData) = UiImages.Load( gamePath );
				var (keyedWidth, keyedHeight, keyedData) = UiImages.LoadKeyed( gamePath + UiImages.BlackKeySuffix );
				TexturePack.ShippedDirectory = rgbaShipped;
				var (rgbaWidth, rgbaHeight, rgbaData) = UiImages.Load( gamePath );
				var (rgbaKeyedWidth, rgbaKeyedHeight, rgbaKeyedData) = UiImages.LoadKeyed( gamePath + UiImages.BlackKeySuffix );

				Assert.AreEqual( pngWidth, rgbaWidth, $"{name} width" );
				Assert.AreEqual( pngHeight, rgbaHeight, $"{name} height" );
				CollectionAssert.AreEqual( pngData, rgbaData, $"{name} pixels through UiImages.Load" );
				Assert.AreEqual( keyedWidth, rgbaKeyedWidth, $"{name} keyed width" );
				Assert.AreEqual( keyedHeight, rgbaKeyedHeight, $"{name} keyed height" );
				CollectionAssert.AreEqual( keyedData, rgbaKeyedData, $"{name} pixels through UiImages.LoadKeyed" );
			}
			// The crafted image was fully opaque, so the black key actually ran on both paths.
			TexturePack.ShippedDirectory = Path.Combine( root, "png", "textures" );
			var (_, _, keyed) = UiImages.LoadKeyed( "ui/textures/crafted.wct#black-key" );
			Assert.IsTrue( keyed.Any( alpha => alpha != 255 ), "the crafted opaque image must be black-keyed" );
		}
		finally
		{
			TexturePack.ShippedDirectory = previousShipped;
			TexturePack.Activate( "", new List<string>() );
			Directory.Delete( root, true );
		}
	}

	/// <summary>Texture.DecodeWct reads a predecoded replacement exactly like the PNG it replaces (no keying on either path).</summary>
	[TestMethod]
	public void DecodeWctMatchesForPredecodedAndPngReplacements()
	{
		var root = Path.Combine( Path.GetTempPath(), "opentpw-predecode-wct-" + Guid.NewGuid().ToString( "N" ) );
		var previousShipped = TexturePack.ShippedDirectory;
		try
		{
			var png = File.ReadAllBytes( Path.Combine( HeroArtDirectory(), "b_exit.wct.png" ) );
			var pngShipped = Path.Combine( root, "png", "textures" );
			var rgbaShipped = Path.Combine( root, "rgba", "textures" );
			Directory.CreateDirectory( Path.Combine( pngShipped, "ui", "textures" ) );
			Directory.CreateDirectory( Path.Combine( rgbaShipped, "ui", "textures" ) );
			File.WriteAllBytes( Path.Combine( pngShipped, "ui", "textures", "b_exit.wct.png" ), png );
			File.WriteAllBytes( Path.Combine( rgbaShipped, "ui", "textures", "b_exit.wct.rgba" ), PredecodedUiArt.EncodePng( png ) );

			TexturePack.Activate( "", new List<string>() );
			TexturePack.ShippedDirectory = pngShipped;
			var (pngData, pngWidth, pngHeight) = Texture.DecodeWct( "ui/textures/b_exit.wct" );
			TexturePack.ShippedDirectory = rgbaShipped;
			var (rgbaData, rgbaWidth, rgbaHeight) = Texture.DecodeWct( "ui/textures/b_exit.wct" );
			Assert.AreEqual( pngWidth, rgbaWidth );
			Assert.AreEqual( pngHeight, rgbaHeight );
			CollectionAssert.AreEqual( pngData, rgbaData );
		}
		finally
		{
			TexturePack.ShippedDirectory = previousShipped;
			TexturePack.Activate( "", new List<string>() );
			Directory.Delete( root, true );
		}
	}

	/// <summary>The no-keyed-texels shortcut: unchanged bytes and exactly one key callback per texel, on both paths.</summary>
	[TestMethod]
	public void KeyOutSkipsTheNeighbourPassWhenNothingMatches()
	{
		var (width, height) = (17, 13);
		var plain = new byte[width * height * 4];
		var pinked = new byte[width * height * 4];
		for ( var pixel = 0; pixel < width * height; pixel++ )
		{
			plain[pixel * 4] = (byte)(pixel % 253);
			plain[pixel * 4 + 1] = 90;
			plain[pixel * 4 + 2] = (byte)(pixel % 199);
			plain[pixel * 4 + 3] = 255;
			pinked.AsSpan( pixel * 4, 4 ).Fill( 60 );
			pinked[pixel * 4 + 3] = 255;
		}
		// One pink texel in the middle, with opaque neighbours to borrow colour from.
		pinked[(width + 1) * 4] = 255;
		pinked[(width + 1) * 4 + 1] = 0;
		pinked[(width + 1) * 4 + 2] = 255;
		var pristine = (byte[])plain.Clone();
		var pinkPristine = (byte[])pinked.Clone();

		var calls = 0;
		UiImages.KeyOut( plain, width, height, ( r, g, b ) => { calls++; return false; } );
		Assert.AreEqual( width * height, calls, "the key runs exactly once per texel" );
		CollectionAssert.AreEqual( pristine, plain, "nothing to key" );

		var pinkCalls = 0;
		UiImages.KeyOut( pinked, width, height, ( r, g, b ) => { pinkCalls++; return r == 255 && g == 0 && b == 255; } );
		Assert.AreEqual( width * height, pinkCalls, "the key still runs exactly once per texel when something matches" );
		Assert.AreEqual( 0, pinked[(width + 1) * 4 + 3], "the pink texel became transparent" );
		Assert.AreNotEqual( pinkPristine[(width + 1) * 4], pinked[(width + 1) * 4], "the transparent texel borrowed a neighbour's colour" );

		// The shipped transform itself: a pink-free buffer passes through byte for byte.
		var shipped = ImageResult.FromMemory( File.ReadAllBytes( Path.Combine( HeroArtDirectory(), "b_okay.wct.png" ) ), ColorComponents.RedGreenBlueAlpha );
		var before = (byte[])shipped.Data.Clone();
		UiImages.KeyOutPink( shipped.Data, shipped.Width, shipped.Height );
		CollectionAssert.AreEqual( before, shipped.Data, "KeyOutPink is a no-op on pink-free shipped art" );
	}
}

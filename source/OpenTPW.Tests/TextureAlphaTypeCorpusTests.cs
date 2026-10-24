using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// The alpha type that decides a world texture's pass (<see cref="TextureAlphaType"/>, tp.exe 0x564C00 / 0x590C40) against the header of every <c>.wct</c> of the original data. Inconclusive without OPENTPW_GAME_PATH.
/// </summary>
[TestClass]
public class TextureAlphaTypeCorpusTests
{
	private static IEnumerable<(string Name, WadArchiveFile File)> WctMembers( ArchiveDirectory directory, string prefix )
	{
		foreach ( var child in directory.Children )
		{
			if ( child is ArchiveDirectory subdirectory )
			{
				foreach ( var entry in WctMembers( subdirectory, $"{prefix}{subdirectory.Name}/" ) )
					yield return entry;
			}
			else if ( child is WadArchiveFile file && file.Name!.EndsWith( ".wct", StringComparison.OrdinalIgnoreCase ) )
				yield return ($"{prefix}{file.Name}", file);
		}
	}

	[TestMethod]
	public void EveryTextureOfTheCorpusClassifiesByItsHeaderAndTheNamedWorldTexturesLandInTheExpectedPass()
	{
		var dataPath = Md2CorpusTests.OriginalDataPath();
		var counts = new Dictionary<TextureAlphaType, int>();
		var byName = new Dictionary<string, TextureAlphaType>( StringComparer.OrdinalIgnoreCase );
		int checkedAgainstDecoder = 0, seen = 0;
		foreach ( var wad in Directory.EnumerateFiles( dataPath, "*", SearchOption.AllDirectories ).Where( file => file.EndsWith( ".wad", StringComparison.OrdinalIgnoreCase ) ) )
		{
			using var archive = new WadArchive( wad );
			foreach ( var (name, file) in WctMembers( archive.Root, "" ) )
			{
				var data = file.GetData();
				file.Free();
				if ( data.Length < 0x1C )
					continue;
				var type = TextureFile.ReadAlphaType( new MemoryStream( data ) );
				// the rule, read straight off the header bytes: an alpha plane (32 bits per pixel) with a zero alpha byte is a cut-out, with another a translucent texture
				var expected = data[0] is 0x12 or 0x13 && data[2] == 0x20 ? ( data[1] != 0 ? TextureAlphaType.Translucent : TextureAlphaType.Cutout ) : TextureAlphaType.Opaque;
				Assert.AreEqual( expected, type, $"{Path.GetFileName( wad )}/{name}" );
				counts[type] = counts.GetValueOrDefault( type ) + 1;
				byName[$"{Path.GetFileName( wad )}/{name}"] = type;
				// the decoder reports the same type as the header reader (a sample: decoding every texture is slow)
				if ( checkedAgainstDecoder < 40 && seen++ % 97 == 0 )
				{
					Assert.AreEqual( type, new TextureFile( new MemoryStream( data ) ).AlphaType, $"{Path.GetFileName( wad )}/{name}" );
					checkedAgainstDecoder++;
				}
			}
		}
		Console.WriteLine( $"WCT alpha types: {counts.GetValueOrDefault( TextureAlphaType.Opaque )} opaque, {counts.GetValueOrDefault( TextureAlphaType.Cutout )} cut-out, {counts.GetValueOrDefault( TextureAlphaType.Translucent )} translucent" );
		Assert.IsTrue( counts.GetValueOrDefault( TextureAlphaType.Opaque ) > 1000 );
		Assert.IsTrue( counts.GetValueOrDefault( TextureAlphaType.Cutout ) > 100 );
		Assert.IsTrue( counts.GetValueOrDefault( TextureAlphaType.Translucent ) > 1000 );

		// the river's water and the harbour's glass roof are translucent, a bank with an alpha plane but a zero alpha byte is a cut-out, a plain roof is opaque; the low-detail copies have no alpha plane
		Assert.AreEqual( TextureAlphaType.Translucent, byName["terrain.wad/textures/wr_water3.wct"] );
		Assert.AreEqual( TextureAlphaType.Translucent, byName["terrain.wad/textures/gte_rof2.wct"] );
		Assert.AreEqual( TextureAlphaType.Cutout, byName["terrain.wad/textures/jri_bnk4.wct"] );
		Assert.AreEqual( TextureAlphaType.Opaque, byName["terrain.wad/textures/gte_rof1.wct"] );
		Assert.AreEqual( TextureAlphaType.Opaque, byName["terrain.wad/stexture/wr_water3.wct"] );
	}
}

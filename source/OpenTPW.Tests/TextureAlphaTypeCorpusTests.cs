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
		var cutoutArchives = new SortedDictionary<string, int>();
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
				if ( type == TextureAlphaType.Cutout && Path.GetFileName( wad ) is "ui.wad" or "lobby.wad" )
					Console.WriteLine( $"Cut-out {Path.GetFileName( wad )}/{name}" );
				if ( type == TextureAlphaType.Cutout )
					cutoutArchives[Path.GetRelativePath( dataPath, wad )] = cutoutArchives.GetValueOrDefault( Path.GetRelativePath( dataPath, wad ) ) + 1;
				// the decoder reports the same type as the header reader (a sample: decoding every texture is slow)
				if ( checkedAgainstDecoder < 40 && seen++ % 97 == 0 )
				{
					Assert.AreEqual( type, new TextureFile( new MemoryStream( data ) ).AlphaType, $"{Path.GetFileName( wad )}/{name}" );
					checkedAgainstDecoder++;
				}
			}
		}
		Console.WriteLine( $"WCT alpha types: {counts.GetValueOrDefault( TextureAlphaType.Opaque )} opaque, {counts.GetValueOrDefault( TextureAlphaType.Cutout )} cut-out, {counts.GetValueOrDefault( TextureAlphaType.Translucent )} translucent" );
		Console.WriteLine( "Cut-outs by archive: " + string.Join( ", ", cutoutArchives.Select( pair => $"{pair.Key}={pair.Value}" ) ) );
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

	[TestMethod]
	public void CutOutTexturesDecodeTheirAlphaPlaneAndTheOddMemberDecodesOpaque()
	{
		var dataPath = Md2CorpusTests.OriginalDataPath();
		var plain = Path.Combine( dataPath, "levels/hallow/rides/bumper.wad" );
		var terrain = Directory.EnumerateFiles( dataPath, "terrain.wad", SearchOption.AllDirectories ).FirstOrDefault( path => path.Contains( "jungle", StringComparison.OrdinalIgnoreCase ) );
		if ( !File.Exists( plain ) || terrain == null )
			Assert.Inconclusive( "The Jungle terrain and the bumper archive are needed." );
		// the 0x13/0x01/0x18 member: an alpha byte without an alpha plane decodes as an opaque texture
		using ( var archive = new WadArchive( plain ) )
		{
			var texture = new TextureFile( new MemoryStream( archive.GetFile( "stexture/b_NEWwin.wct" ).GetData() ) );
			Assert.AreEqual( TextureAlphaType.Opaque, texture.AlphaType );
			Assert.IsTrue( Enumerable.Range( 0, texture.Data.Data.Length / 4 ).All( index => texture.Data.Data[index * 4 + 3] == 255 ) );
		}
		// jri_bnk4 is a cut-out (alpha byte 0, an alpha plane): its plane is decoded, with texels the engine's alpha test at 0xF0/255 removes
		using ( var archive = new WadArchive( terrain ) )
		{
			var texture = new TextureFile( new MemoryStream( archive.GetFile( "textures/jri_bnk4.wct" ).GetData() ) );
			Assert.AreEqual( TextureAlphaType.Cutout, texture.AlphaType );
			var below = Enumerable.Range( 0, texture.Data.Data.Length / 4 ).Count( index => texture.Data.Data[index * 4 + 3] < 0xF0 );
			Console.WriteLine( $"jri_bnk4: {below} of {texture.Data.Data.Length / 4} texels below 0xF0" );
			Assert.IsTrue( below > 0, "the alpha plane of a cut-out texture is decoded" );
		}
	}
}

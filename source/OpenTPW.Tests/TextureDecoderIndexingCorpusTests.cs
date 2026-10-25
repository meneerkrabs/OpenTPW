using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// Pins the RGBA bytes of a representative spread of original WCT textures (all four themes plus
/// UI, lobby, global and generic data; every dimension class the corpus contains; half-scale and
/// full-scale; with and without alpha). The hashes were recorded from the original Func-based
/// D4InverseTransform before TextureFile.Decode switched to direct integer indexing, so any byte
/// change in the wavelet decode fails here. Requires OPENTPW_GAME_PATH.
/// </summary>
[TestClass]
public class TextureDecoderIndexingCorpusTests
{
	private readonly record struct CorpusPin( string Wad, string Entry, int Width, int Height, string Sha );

	private static readonly CorpusPin[] PinnedWadTextures =
	{
		new( "ui.wad", "stexture/button_text1.wct", 128, 128, "6BDA7C5DC1697DEB21FC3FB3235BD2057C8C5BE12AEA3C519145277E41C381CE" ),
		new( "ui.wad", "stexture/Cash_alpha.wct", 64, 64, "51AD21417AE2A065071D838C51192E9B41679DDE65F18F56AF72BEC291E2C1EC" ),
		new( "ui.wad", "stexture/I_Mail.wct", 32, 32, "695BCF4D27C608E8658D7B9DDC7A5D6C2C2B1D3ACC41DFB879D880EC8C2DB3CE" ),
		new( "ui.wad", "stexture/b_1person.wct", 51, 51, "35B8BA586EE747038288E90605C610D87946889256C630C0FC2D9AE48F7D58E9" ),
		new( "ui.wad", "stexture/b_allstaff.wct", 26, 26, "8E1EFA41C8425C4B7AC21D530ABE030FA0845E33F9292AC50EB3BC562FCA3FB8" ),
		new( "lobby.wad", "terrain/textures/sgr_top2.wct", 128, 128, "7176E6E1BA50809089F771442D8AA6644F6B0C28EEB608B6A964E64CB18A4C8F" ),
		new( "lobby.wad", "terrain/stexture/GK_top.wct", 8, 8, "B11C00E0553A56EEDF74184BD14E8C2FA20D03E39010E04D310D1025E872EFDB" ),
		new( "levels/hallow/features/gates.wad", "textures/sign.wct", 256, 128, "0067E340A79308AC967D3D58D7D95FCBA39CF7F072D3C233B8A1FF9804F4982B" ),
		new( "levels/hallow/features/gates.wad", "stexture/sign.wct", 64, 32, "5BF3BAA8C269DD304D170B801839950BD93FB0EE13F579D019A000F98A8E09C7" ),
		new( "levels/hallow/sideshow/arcade.wad", "stexture/ha_horn.wct", 12, 12, "325FCF4A1CA4B9D7E1BE89FB4219CFE8B0216EDD0B5E590E78DC8D41FAE76E52" ),
		new( "levels/hallow/sideshow/arcade.wad", "textures/ha_horn.wct", 48, 48, "FC373E9F3D9D2608EFA7D6507FBD01399CCE99A04C5F279E52462C830410BF64" ),
		new( "levels/jungle/rides/bumper.wad", "textures/boat.wct", 128, 128, "03891A7CF3FAA6B7A4E2DB113D31032753F967A9D338D9AEFC66692D6C328278" ),
		new( "levels/jungle/queue.wad", "textures/jpa_que1.wct", 128, 128, "DF7B7FE77BAB7DB6BD2A2BB0AD3127167B6DCF3FADB29FCA023C4AD73673ACEB" ),
		new( "levels/jungle/terrain.wad", "textures/Ftest03.wct", 128, 128, "8DD5D897A157ABF313950E25B090183CC42F29C8047395302B09E3910FB18F76" ),
		new( "levels/fantasy/shops/drinks.wad", "textures/FDrink_BigLeaf.wct", 128, 128, "2CF4282A4701070335EAF60318D976B838A86986E82F82DADB476814B4FCF3CA" ),
		new( "levels/fantasy/queue.wad", "textures/jpa_que1.wct", 128, 128, "4DAF955FE40B71D1661AEE27B079F41B7D628AC921154AA1025381BE0FCEC85D" ),
		new( "levels/fantasy/queue.wad", "stexture/fire0.wct", 16, 16, "E7685606CD4EE6133D4AA88DD82D2C098FA439A0D367CF9327491E1AD0C2BC65" ),
		new( "levels/fantasy/terrain.wad", "textures/Ftest03.wct", 128, 128, "8DD5D897A157ABF313950E25B090183CC42F29C8047395302B09E3910FB18F76" ),
		new( "levels/hallow/queue.wad", "textures/jpa_que1.wct", 128, 128, "1E9CC1D25D8E9EF0A139D4CFCC45DA02428A6123E1DFA19496B7E834E33299C9" ),
		new( "levels/space/queue.wad", "textures/jpa_que1.wct", 128, 128, "21D9F9F3BE8338C600F60F7EC5A9CF9EBFCF6F2BB6BC5103B67129EF17FB98CD" ),
		new( "levels/space/rides/scentro.wad", "textures/Sp_Botplate1b.wct", 127, 128, "F3F94915E7C37818C9EF9C221570FB85C443A0032ECB53F7484D2854A387D28E" ), // re-recorded when the chroma planes took each other's header scale (the only pinned texture whose two scales differ)
		// Pinned with the chroma planes taking each other's header scale; the two scales differ in these (about a quarter of the corpus), so a swap back changes the bytes.
		new( "ui.wad", "textures/pipe.wct", 64, 64, "C10119EE78140FDF7E4EBB42F1D658458D97205D139CDFA9C0285F7A03FAAA66" ),
		new( "ui.wad", "textures/b_1person.wct", 64, 64, "1C552E6EF7865B41CCFA3BBEDD93503B4A5265F03E4CE09D99F50A102C77DB12" ),
		new( "ui.wad", "stexture/ridestatbar.wct", 32, 32, "62BF4812B9735FBA3B0D92A6BA18096D1B60618C629544590CC862C964A0A296" ),
		new( "global/advisor.wad", "textures/Gold_Ticket.wct", 128, 128, "5C141CDE99579DAB5AD8B891AF4B635FCCF7C8B3B0DA6CB2B14CC7934E643DE7" ),
	};

	private readonly record struct LoosePin( string Path, int Width, int Height, string Sha );

	private static readonly LoosePin[] PinnedLooseFiles =
	{
		new( "generic/dynamic/textures/green.wct", 16, 16, "AD686A39D85BC0BC9C737E407F6C4B851A62115F3AD943E7D9F30AED98283BEA" ),
		new( "generic/shadow/alphkid.wct", 64, 64, "4A84DDB98EACDB1768B5B368A9FB5155E9578BDFDFDC2D1093600EB2D298FE36" ),
	};

	private readonly record struct FailurePin( string Wad, string Entry, string ErrorType, string? ErrorMessage );

	private static readonly FailurePin[] PinnedFailures =
	{
		new( "global/advisor.wad", "stexture/Bowler_Btie.wct", "Exception", "Offset > stream length" ),
		// The bounds audit (docs/WCT-ROBUSTNESS.md) turned this crash into an explicit malformed-data refusal. (bumper.wad's stexture/b_NEWwin.wct, an alpha byte of 1 with 24 bits per pixel and no alpha chunk,
		// used to be refused as well: the loader 0x564C00 reads an alpha plane only at 32 bits per pixel, so the file is a plain opaque texture and decodes; TextureAlphaTypeCorpusTests pins that.)
		new( "levels/fantasy/advisor.wad", "stexture/Fan_stalk.wct", "InvalidDataException", "Truncated WCT channel coefficients." ),
		new( "levels/space/rides/gokarts.wad", "stexture/s_disc.wct", "Exception", "Offset > stream length" ),
	};

	[TestMethod]
	public void OriginalCorpusTexturesDecodeToOriginalRgba()
	{
		var dataPath = OriginalParkImportTests.OriginalDataPath();

		foreach ( var pin in PinnedWadTextures )
		{
			var wadPath = Path.Combine( dataPath, pin.Wad );
			if ( !File.Exists( wadPath ) )
				Assert.Inconclusive( "Original game data is not installed." );

			using var archive = new WadArchive( wadPath );
			var texture = new TextureFile( new MemoryStream( archive.GetFile( pin.Entry ).GetData() ) ).Data;
			Assert.AreEqual( pin.Width, texture.Width, $"{pin.Wad}!{pin.Entry} width" );
			Assert.AreEqual( pin.Height, texture.Height, $"{pin.Wad}!{pin.Entry} height" );
			Assert.AreEqual( pin.Sha, Convert.ToHexString( SHA256.HashData( texture.Data ) ), $"{pin.Wad}!{pin.Entry} RGBA bytes changed against the pre-rewrite decoder" );
		}

		foreach ( var pin in PinnedLooseFiles )
		{
			var path = Path.Combine( dataPath, pin.Path );
			if ( !File.Exists( path ) )
				Assert.Inconclusive( "Original game data is not installed." );

			using var looseStream = new MemoryStream( File.ReadAllBytes( path ) );
			var texture = new TextureFile( looseStream ).Data;
			Assert.AreEqual( pin.Width, texture.Width, $"{pin.Path} width" );
			Assert.AreEqual( pin.Height, texture.Height, $"{pin.Path} height" );
			Assert.AreEqual( pin.Sha, Convert.ToHexString( SHA256.HashData( texture.Data ) ), $"{pin.Path} RGBA bytes changed against the pre-rewrite decoder" );
		}
	}

	/// <summary>
	/// Every WCT the original data ships, in every archive and loose, decodes except the four pinned malformed
	/// ones. Synthetic bounds tests alone let a hardening refuse most of the retail corpus (odd-length LZSS chunks,
	/// BILZ stream sizes that count their header); this walk catches that class of regression.
	/// </summary>
	[TestMethod]
	public void EveryOriginalTextureDecodesExceptThePinnedFailures()
	{
		var dataPath = OriginalParkImportTests.OriginalDataPath();
		var expected = PinnedFailures.Select( pin => $"{pin.Wad}!{pin.Entry}" ).ToHashSet( StringComparer.OrdinalIgnoreCase );
		var failures = new List<string>();
		var decoded = 0;
		void Decode( string name, byte[] bytes )
		{
			try
			{
				_ = new TextureFile( new MemoryStream( bytes ) ).Data;
				decoded++;
			}
			catch ( Exception exception ) when ( !expected.Contains( name ) )
			{
				failures.Add( $"{name}: {exception.GetType().Name} {exception.Message}" );
			}
			catch ( Exception )
			{
			}
		}
		foreach ( var wadPath in Directory.EnumerateFiles( dataPath, "*.wad", SearchOption.AllDirectories ) )
		{
			var wad = Path.GetRelativePath( dataPath, wadPath ).Replace( '\\', '/' );
			using var archive = new WadArchive( wadPath );
			void Walk( string directory )
			{
				foreach ( var file in archive.GetFiles( directory ).Where( file => file.EndsWith( ".wct", StringComparison.OrdinalIgnoreCase ) ) )
				{
					var entry = directory.Length == 0 ? file : $"{directory}/{file}";
					Decode( $"{wad}!{entry}", archive.GetFile( entry ).GetData() );
				}
				foreach ( var child in archive.GetDirectories( directory ) )
					Walk( directory.Length == 0 ? child : $"{directory}/{child}" );
			}
			Walk( "" );
		}
		foreach ( var loose in Directory.EnumerateFiles( dataPath, "*.wct", SearchOption.AllDirectories ) )
			Decode( Path.GetRelativePath( dataPath, loose ).Replace( '\\', '/' ), File.ReadAllBytes( loose ) );
		if ( decoded == 0 )
			Assert.Inconclusive( "Original game data has no WCT textures." );
		Assert.AreEqual( 0, failures.Count, $"{failures.Count} original textures no longer decode ({decoded} did):\n{string.Join( "\n", failures.Take( 20 ) )}" );
	}

	[TestMethod]
	public void OriginalCorpusMalformedTexturesKeepTheirFailureModes()
	{
		var dataPath = OriginalParkImportTests.OriginalDataPath();

		foreach ( var pin in PinnedFailures )
		{
			var wadPath = Path.Combine( dataPath, pin.Wad );
			if ( !File.Exists( wadPath ) )
				Assert.Inconclusive( "Original game data is not installed." );

			using var archive = new WadArchive( wadPath );
			var bytes = archive.GetFile( pin.Entry ).GetData();
			Exception? caught = null;
			try
			{
				new TextureFile( new MemoryStream( bytes ) );
			}
			catch ( Exception ex )
			{
				caught = ex;
			}

			Assert.IsNotNull( caught, $"{pin.Wad}!{pin.Entry} used to throw {pin.ErrorType}" );
			Assert.AreEqual( pin.ErrorType, caught!.GetType().Name, $"{pin.Wad}!{pin.Entry} failure mode changed" );
			if ( pin.ErrorMessage != null )
				Assert.AreEqual( pin.ErrorMessage, caught.Message, $"{pin.Wad}!{pin.Entry} failure message changed" );
		}
	}
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

[TestClass]
[DoNotParallelize]
public class CompatibilityTests
{
	private static string TemporaryDirectory()
	{
		var path = Path.Combine( Path.GetTempPath(), "opentpw-compat-" + Guid.NewGuid().ToString( "N" ) );
		Directory.CreateDirectory( path );
		return path;
	}

	private static void Write( string root, string relativePath, byte[] data )
	{
		var path = Path.Combine( root, relativePath.Replace( '/', Path.DirectorySeparatorChar ) );
		Directory.CreateDirectory( Path.GetDirectoryName( path )! );
		File.WriteAllBytes( path, data );
	}

	private const string HighSam = "#\n#\tTheme Park World Settings\n#\nGraphicalOptions.TEXTUREQUALITY\t3\nGraphicalOptions.TEXTUREFILTERING\t2\nGraphicalOptions.MIPMAP\t1\n" +
		"GraphicalOptions.FIRSTPERSONVIEWDISTANCE\t2\nGameOptions.COASTERSMOOTHNESS\t6\nGameOptions.PARTICLEDENSITY\t1500\nGameOptions.TOTALPARTICLES\t2000\nGameOptions.NUMKIDS\t2\nGameOptions.WEATHER\t1\n";

	private static Stream? SyntheticPresets( string path ) => path switch
	{
		"/high.sam" => new MemoryStream( Encoding.ASCII.GetBytes( HighSam ) ),
		"/low.sam" => new MemoryStream( Encoding.ASCII.GetBytes( HighSam.Replace( "TEXTUREFILTERING\t2", "TEXTUREFILTERING\t0" ).Replace( "NUMKIDS\t2", "NUMKIDS\t0" ) ) ),
		_ => null
	};

	// ---- Graphics presets ----

	[TestMethod]
	public void ReadsDetailFilesAndReportsMissingKeys()
	{
		var diagnostics = new List<string>();
		var detail = OriginalDetailSettings.FromSam( new SettingsFile( new MemoryStream( Encoding.ASCII.GetBytes( HighSam ) ) ), diagnostics );
		Assert.AreEqual( (3, 2, 1, 2), (detail.TextureQuality, detail.TextureFiltering, detail.Mipmap, detail.NumKids) );
		Assert.AreEqual( OriginalDetailSettings.Fields.Count - 9, diagnostics.Count, "every absent key is reported" );
		Assert.AreEqual( 26, OriginalDetailSettings.Fields.Count );
		Assert.AreEqual( 1500, detail.Get( "gameoptions.particledensity" ) );
		Assert.AreEqual( 7, detail.With( "GameOptions.NUMKIDS", 7 ).NumKids );
	}

	[TestMethod]
	public void MapsFilteringMipmapsAndExtensionsToRenderQuality()
	{
		var detail = new OriginalDetailSettings();
		Assert.AreEqual( new RenderQuality( TextureFilterMode.Point, 1, false, 1 ), GraphicsPresets.ToRenderQuality( detail, 16, 1 ) );
		Assert.AreEqual( TextureFilterMode.Bilinear, GraphicsPresets.ToRenderQuality( detail with { TextureFiltering = 1 }, 16, 1 ).Filter );
		Assert.AreEqual( new RenderQuality( TextureFilterMode.Trilinear, 1, true, 1 ), GraphicsPresets.ToRenderQuality( detail with { TextureFiltering = 2, Mipmap = 1 }, 16, 1 ) );
		Assert.AreEqual( new RenderQuality( TextureFilterMode.Anisotropic, 8, false, 4 ), GraphicsPresets.ToRenderQuality( detail with { TextureFiltering = 3 }, 8, 9 ) );
		var enhanced = GraphicsPresets.Enhance( detail with { NumKids = 2, ParticleDensity = 1500 }, simulationOptions: false );
		Assert.AreEqual( (3, 3, 8, 4), (enhanced.TextureFiltering, enhanced.FirstPersonViewDistance, enhanced.CoasterSmoothness, enhanced.SkyQuality) );
		Assert.AreEqual( (2, 1500), (enhanced.NumKids, enhanced.ParticleDensity), "simulation options stay original without the fix" );
		var simulation = GraphicsPresets.Enhance( detail, simulationOptions: true );
		Assert.AreEqual( (3, 2000), (simulation.NumKids, simulation.ParticleDensity) );
		foreach ( var (key, _, _) in OriginalDetailSettings.Fields )
			Assert.IsTrue( GraphicsPresets.Support.ContainsKey( key ), $"{key} has a support note" );
	}

	[TestMethod]
	public void GraphicsServiceOffersAvailablePresetsAndGatesSimulationOptions()
	{
		var flags = CompatibilityFlags.Original;
		var directory = TemporaryDirectory();
		var path = Path.Combine( directory, GraphicsSettings.FileName );
		var service = new GraphicsSettingsService( SyntheticPresets, GraphicsSettings.Default, path, () => flags );
		CollectionAssert.AreEqual( new[] { GraphicsPreset.Low, GraphicsPreset.High, GraphicsPreset.Enhanced, GraphicsPreset.Custom }, service.Presets.ToArray() );
		StringAssert.Contains( string.Join( "\n", service.Diagnostics ), "/med.sam is missing" );
		Assert.AreEqual( GraphicsPreset.High, service.Current.Preset, "default preset" );
		Assert.AreEqual( new RenderQuality( TextureFilterMode.Trilinear, 1, true, 1 ), service.Effective );
		Assert.AreEqual( 0, service.SimulationDeviations().Count );

		service.Apply( service.Current with { Preset = GraphicsPreset.Enhanced } );
		Assert.AreEqual( new RenderQuality( TextureFilterMode.Anisotropic, 16, true, 2 ), service.Effective );
		Assert.IsTrue( service.RestartRequired );
		Assert.AreEqual( 2, service.Detail.NumKids, "Enhanced keeps NUMKIDS without the enhanced-game-options fix" );
		Assert.AreEqual( GraphicsPreset.Enhanced, GraphicsSettings.Load( path, new List<string>() ).Preset, "persisted" );

		flags = CompatibilityFlags.Custom( new[] { CompatibilityFixes.EnhancedGameOptions } );
		Assert.AreEqual( 3, service.Detail.NumKids );
		CollectionAssert.AreEqual( new[] { "GameOptions.NUMKIDS=3 (original High 2)", "GameOptions.PARTICLEDENSITY=2000 (original High 1500)" }, service.SimulationDeviations().ToArray() );

		var custom = new OriginalDetailSettings { TextureFiltering = 1, NumKids = 3 };
		service.Apply( new GraphicsSettings { Preset = GraphicsPreset.Custom, Detail = custom, ViewDistanceScale = 1.5f } );
		Assert.AreEqual( 3, service.Detail.NumKids );
		flags = CompatibilityFlags.Original;
		Assert.AreEqual( 2, service.Detail.NumKids, "custom simulation values fall back to the original High values" );
		Assert.AreEqual( new RenderQuality( TextureFilterMode.Bilinear, 1, false, 1.5f ), service.Effective );
		Directory.Delete( directory, true );
	}

	[TestMethod]
	public void GraphicsSettingsValidateAndRoundTripJson()
	{
		var diagnostics = new List<string>();
		var settings = GraphicsSettings.FromJson( "{ \"Preset\": \"Custom\", \"Anisotropy\": 64, \"ViewDistanceScale\": 99 }", diagnostics );
		Assert.AreEqual( (GraphicsPreset.High, 16, 1f), (settings.Preset, settings.Anisotropy, settings.ViewDistanceScale) );
		Assert.AreEqual( 3, diagnostics.Count );
		var original = new GraphicsSettings { Preset = GraphicsPreset.Custom, Detail = new OriginalDetailSettings { CoasterSmoothness = 5 } };
		var copy = GraphicsSettings.FromJson( original.ToJson(), diagnostics );
		Assert.AreEqual( 5, copy.Detail!.CoasterSmoothness );
		Assert.AreEqual( GraphicsSettings.Default, GraphicsSettings.FromJson( "not json", diagnostics ) );
	}

	[TestMethod]
	public void OriginalPresetFilesArePinned()
	{
		var data = StringTableTests.OriginalDataDirectory();
		var service = new GraphicsSettingsService( path => File.Exists( Path.Combine( data, path.TrimStart( '/' ) ) ) ? File.OpenRead( Path.Combine( data, path.TrimStart( '/' ) ) ) : null,
			GraphicsSettings.Default, null, () => CompatibilityFlags.Original );
		Assert.AreEqual( 0, service.Diagnostics.Count, string.Join( "; ", service.Diagnostics ) );
		string Values( GraphicsPreset preset ) => string.Join( ",", OriginalDetailSettings.Fields.Select( field => field.Get( service.GetPresetValues( preset )! ) ) );
		// [DATA:low.sam/med.sam/high.sam] in file order (TEXTUREQUALITY .. LOBBYOBJECTS).
		Assert.AreEqual( "1,0,0,0,0,1,0,0,0,0,0,1,1,0,0,0,0,0,0,1,0,500,1000,0,0,10", Values( GraphicsPreset.Low ) );
		Assert.AreEqual( "2,1,2,1,1,2,1,0,0,1,0,1,1,0,0,0,1,1,0,4,1,1000,3200,2,1,70", Values( GraphicsPreset.Medium ) );
		Assert.AreEqual( "3,2,2,1,1,4,1,0,1,2,1,1,1,0,0,0,2,1,0,6,1,1500,2000,2,1,100", Values( GraphicsPreset.High ) );
		Assert.AreEqual( "3,3,3,1,1,4,1,0,1,3,1,1,1,1,1,0,3,1,0,8,1,1500,2000,2,1,100", Values( GraphicsPreset.Enhanced ) );
		Assert.AreEqual( TextureFilterMode.Point, GraphicsPresets.ToRenderQuality( service.GetPresetValues( GraphicsPreset.Low )!, 16, 1 ).Filter );
	}

	// ---- Compatibility profile ----

	[TestMethod]
	public void DefaultProfileIsOriginalAndRecommendedIsPresentationOnly()
	{
		var diagnostics = new List<string>();
		Assert.AreEqual( CompatibilityFlags.Original, CompatibilitySettings.Default.Resolve( diagnostics ) );
		Assert.AreEqual( 0, CompatibilityFlags.Original.EnabledFixes.Count );
		CollectionAssert.AreEqual( new[] { CompatibilityFixes.SignFontSubstitution }, CompatibilityFlags.Recommended.EnabledFixes.ToArray() );
		Assert.IsTrue( CompatibilityFlags.Recommended.IsOriginalSimulation );
		Assert.IsTrue( CompatibilityFixes.All.Where( fix => fix.Kind == CompatibilityFixKind.Simulation ).All( fix => !fix.InRecommended ) );
		Assert.AreEqual( CompatibilityFixes.All.Count, CompatibilityFixes.All.Select( fix => fix.Id ).Distinct().Count() );

		var custom = new CompatibilitySettings { Profile = CompatibilityProfile.Custom, Fixes = new() { [CompatibilityFixes.EnhancedGameOptions] = true, ["no-such-fix"] = true, [CompatibilityFixes.SignFontSubstitution] = false } };
		var flags = custom.Resolve( diagnostics );
		CollectionAssert.AreEqual( new[] { CompatibilityFixes.EnhancedGameOptions }, flags.EnabledFixes.ToArray() );
		Assert.IsFalse( flags.IsOriginalSimulation );
		StringAssert.Contains( diagnostics.Single(), "no-such-fix" );
		Assert.AreEqual( CompatibilityProfile.Custom, CompatibilitySettings.FromJson( custom.ToJson(), diagnostics ).Profile );
	}

	[TestMethod]
	public void CompatibilityFlagsSerializeForSaveMetadata()
	{
		var flags = CompatibilityFlags.Custom( new[] { CompatibilityFixes.SignFontSubstitution, CompatibilityFixes.EnhancedGameOptions, CompatibilityFixes.SignFontSubstitution } );
		var metadata = flags.ToMetadata();
		Assert.AreEqual( "opentpw-compat/1;profile=Custom;fixes=enhanced-game-options,sign-font-substitution;simulation=enhanced-game-options", metadata );
		var parsed = CompatibilityFlags.ParseMetadata( metadata );
		Assert.AreEqual( flags.Profile, parsed.Profile );
		CollectionAssert.AreEqual( flags.EnabledFixes.ToArray(), parsed.EnabledFixes.ToArray() );
		Assert.AreEqual( "opentpw-compat/1;profile=Original;fixes=;simulation=", CompatibilityFlags.Original.ToMetadata() );
		var future = CompatibilityFlags.ParseMetadata( "opentpw-compat/1;profile=Custom;fixes=future-fix;simulation=future-fix;extra=1" );
		CollectionAssert.AreEqual( new[] { "future-fix" }, future.SimulationFixes.ToArray(), "unknown ids are kept and treated as simulation-affecting" );
		Assert.ThrowsException<FormatException>( () => CompatibilityFlags.ParseMetadata( "profile=Original" ) );
		Assert.ThrowsException<FormatException>( () => CompatibilityFlags.ParseMetadata( "opentpw-compat/1;profile=Nope" ) );
	}

	[TestMethod]
	public void CommandLineSelectsProfilesAndFixes()
	{
		var settings = CompatibilityStartup.ApplyCommandLine( CompatibilitySettings.Default, new[] { "--compat-profile", "recommended" } );
		Assert.AreEqual( CompatibilityProfile.Recommended, settings.Profile );
		settings = CompatibilityStartup.ApplyCommandLine( CompatibilitySettings.Default, new[] { "--compat-fix", "enhanced-game-options", "--compat-fix", "sign-font-substitution=off" } );
		Assert.AreEqual( CompatibilityProfile.Custom, settings.Profile );
		Assert.IsTrue( settings.Fixes["enhanced-game-options"] );
		Assert.IsFalse( settings.Fixes["sign-font-substitution"] );
		Assert.ThrowsException<ArgumentException>( () => CompatibilityStartup.ApplyCommandLine( CompatibilitySettings.Default, new[] { "--compat-fix", "bogus" } ) );
		Assert.ThrowsException<ArgumentException>( () => CompatibilityStartup.ApplyCommandLine( CompatibilitySettings.Default, new[] { "--compat-profile", "1" } ) );
	}

	// ---- Data corrections ----

	[TestMethod]
	public void DataCorrectionsMatchPathAndHashAndOnlyApplyWhenEnabled()
	{
		Assert.AreEqual( "/levels/space/rides/megacost/megacost.sgn", DataCorrections.NormalizePath( "levels\\Space\\rides\\megacost.wad\\MEGACOST.SGN" ) );
		var bytes = SignFileTests.CreateSign( (1, "EggIt AOE", "EGGITAOE.TTF", 100, 0, -100), (2, "EggIt Italic", "EGGII___.TTF", 100, 128, -90) );
		var log = new List<string>();
		var unchanged = DataCorrections.Apply( "/levels/space/rides/megacost/megacost.sgn", bytes, _ => true, log.Add );
		Assert.AreSame( bytes, unchanged, "a different hash is never corrected" );
		StringAssert.Contains( log.Single(), "differs from the known original" );
		var patched = new SignFile( DataCorrections.ReplaceSlotFont( (byte[])bytes.Clone(), 1, "EggIt AOE", "EGGITAOE.TTF" ) );
		Assert.AreEqual( ("EGGITAOE.TTF", "EggIt AOE", "EggIt AOE"), (patched.Slots[1].FontFileName, patched.Slots[1].FaceName, patched.Slots[1].LogFont.FaceName) );
		Assert.AreSame( bytes, DataCorrections.Apply( "/other.sgn", bytes, _ => true ) );
	}

	[TestMethod]
	public void OriginalMegacostSignIsCorrectedOnlyWithTheFix()
	{
		var data = StringTableTests.OriginalDataDirectory();
		var member = SignFileTests.OriginalSignMembers( data ).Single( entry => entry.Path == "levels/space/rides/megacost.wad/megacost.sgn" ).Data;
		using var stream = File.OpenRead( Path.Combine( data, "fonts.wad" ) );
		var library = SignFontLibrary.Load( stream );
		var original = new SignFile( DataCorrections.Apply( "/levels/space/rides/megacost/megacost.sgn", member, _ => false ) );
		Assert.IsNull( library.Find( original.Slots[1].FontFileName ) );
		var corrected = new SignFile( DataCorrections.Apply( "/levels/space/rides/megacost/megacost.sgn", member, CompatibilityFlags.Recommended.IsEnabled ) );
		Assert.AreSame( library.Get( "EGGITAOE.TTF" ), library.Find( corrected.Slots[1].FontFileName ) );
		Assert.AreEqual( original.Slots[1].LogFont.Height, corrected.Slots[1].LogFont.Height );
	}

	// ---- Media fallback (synthetic minimal install) ----

	[TestMethod]
	public void CdDataOverlayFillsMissingMediaCaseInsensitively()
	{
		var root = TemporaryDirectory();
		var install = Path.Combine( root, "install", "Data" );
		Write( install, "levels/jungle/global.sam", Encoding.ASCII.GetBytes( "ParkName.GateObjectId 1601\n" ) );
		Write( install, "Movies/bf.tgq", new byte[] { 1 } );
		var cd = Path.Combine( root, "cd" );
		Write( cd, "DATA/movies/JUG.TGQ", new byte[] { 2, 2 } );
		Write( cd, "DATA/movies/bf.tgq", new byte[] { 9 } );
		Write( cd, "data/LEVELS/Jungle/music/MUSICHD.SDT", new byte[] { 3 } );
		Write( cd, "data/levels/jungle/global.sam", new byte[] { 4 } );
		var originalFileSystem = FileSystem;
		var originalFallbacks = MovieLibrary.FallbackDataDirectories;
		try
		{
			FileSystem = new BaseFileSystem( install );
			var roots = new DataRoots( install );
			Assert.ThrowsException<DirectoryNotFoundException>( () => roots.Add( "CD data", Path.Combine( root, "nothing" ), DataOverlayRole.CdFallback ) );
			var overlay = roots.Add( "CD data", cd, DataOverlayRole.CdFallback );
			Assert.AreEqual( Path.Combine( cd, "DATA" ), overlay.DataDirectory );

			Assert.AreEqual( "movies/JUG.TGQ", roots.Resolve( "Movies/jug.tgq" )!.Value.RelativePath );
			Assert.AreSame( overlay, roots.Resolve( "Movies/jug.tgq" )!.Value.Overlay );
			Assert.IsNull( roots.Resolve( "Movies/bf.tgq" )!.Value.Overlay, "the install wins over the overlay" );
			Assert.AreEqual( "LEVELS/Jungle/music/MUSICHD.SDT/level4c.mp2", DataRoots.ResolveIn( overlay.DataDirectory, "levels/jungle/Music/MusicHD.sdt/level4c.mp2" ) );
			Assert.IsNull( roots.Resolve( "Movies/roll.tgq" ) );
			Assert.IsNull( DataRoots.ResolveIn( install, "../install/Data/Movies/bf.tgq" ) );

			var media = OptionalMedia.Check( roots );
			Assert.AreEqual( 14, media.Count );
			Assert.AreEqual( "install", media.Single( status => status.RelativePath == "Movies/bf.tgq" ).FoundIn );
			Assert.AreEqual( "CD data", media.Single( status => status.RelativePath == "Movies/jug.tgq" ).FoundIn );
			Assert.AreEqual( "CD data", media.Single( status => status.RelativePath == "levels/jungle/Music/MusicHD.sdt" ).FoundIn );
			var lines = OptionalMedia.Describe( media );
			Assert.AreEqual( 13, lines.Count );
			Assert.AreEqual( 11, lines.Count( line => line.StartsWith( "Optional media missing" ) ) );

			CompatibilityStartup.Mount( roots );
			CollectionAssert.AreEqual( new byte[] { 2, 2 }, FileSystem.ReadAllBytes( "/Movies/jug.tgq" ) );
			Assert.IsTrue( FileSystem.FileExists( "/levels/jungle/Music/MusicHD.sdt" ) );
			Assert.AreEqual( 1L, FileSystem.GetSize( "/levels/jungle/Music/MusicHD.sdt" ) );
			Assert.AreEqual( "ParkName.GateObjectId 1601\n", FileSystem.ReadAllText( "/levels/jungle/global.sam" ), "existing install files are never replaced" );
			Assert.IsFalse( FileSystem.FileExists( "/Movies/roll.tgq" ) );
			Assert.AreEqual( Path.Combine( overlay.DataDirectory, "movies", "JUG.TGQ" ), MovieLibrary.Resolve( install, "jug" ) );
			CollectionAssert.AreEqual( new[] { "bf", "JUG" }, MovieLibrary.List( install ).ToArray() );
			Assert.IsFalse( File.Exists( Path.Combine( install, "Movies", "jug.tgq" ) ), "nothing is copied into the install" );
		}
		finally
		{
			FileSystem = originalFileSystem;
			MovieLibrary.FallbackDataDirectories = originalFallbacks;
			Directory.Delete( root, true );
		}
	}

	// ---- Missing strings ----

	[TestMethod]
	public void MissingStringsFallBackToEnglishThenTheInternalName()
	{
		var table = new LocalizedStringTable( "UITEXT.str", "German", new[] { "", "Online gehen", "" }, new[] { "", "Go Online", "Go Offline", "Load" } );
		Assert.AreEqual( "Online gehen", table.Get( 1, "GoOnline" ) );
		Assert.AreEqual( "Go Offline", table.Get( 2, "GoOffline" ) );
		Assert.AreEqual( "Load", table.Get( 3, "Load" ) );
		Assert.AreEqual( "", table.Get( 0, "Blank" ), "entries blank in every language stay blank" );
		Assert.AreEqual( "Save", table.Get( 4, "Save" ) );
		Assert.AreEqual( "Save", table.Get( -1, "Save" ) );
		Assert.AreEqual( 4, table.Diagnostics.Count );
		table.Get( 2, "GoOffline" );
		Assert.AreEqual( 4, table.Diagnostics.Count, "each fallback is reported once" );
		Assert.AreEqual( "Load", new LocalizedStringTable( "UITEXT.str", "German", null, null ).Get( 3, "Load" ) );
	}

	[DataTestMethod]
	[DynamicData( nameof( LanguageTests.ShippedLanguages ), typeof( LanguageTests ) )]
	public void EveryUiStringResolvesInEveryLanguage( string name )
	{
		var language = LanguageTests.OriginalLanguagePublic( name );
		var table = LocalizedStringTable.Load( language, GameLanguage.TryResolveEnglish( language ), "UITEXT.str" );
		// Corrected UIStrings IDs refer to real entries; only the designated Blank entry is universally empty.
		var blank = Enum.GetValues<UIStrings>().Where( id => table.Get( (int)id, id.ToString() ).Length == 0 ).ToArray();
		Assert.AreEqual( "Blank", string.Join( ", ", blank ) );
		// The corrected IDs fit the shipped tables. French/German intentionally leave currency
		// prefixes blank, which this fallback wrapper reports and fills from English.
		var expected = new List<string>();
		if ( name is "French" or "German" )
			expected.Add( $"{language.Name} UITEXT.str[448] (Dollar) is missing; using the English text." );
		if ( name == "French" )
			expected.Add( $"{language.Name} UITEXT.str[457] (CashDollar) is missing; using the English text." );
		CollectionAssert.AreEqual( expected, table.Diagnostics.ToList(), string.Join( " | ", table.Diagnostics ) );
	}

	// ---- SDT robustness ----

	private static byte[] CreateBank( params (string Name, byte[] Data)[] entries )
	{
		var output = new MemoryStream();
		var writer = new BinaryWriter( output );
		writer.Write( entries.Length );
		var offset = 4 + entries.Length * 4;
		foreach ( var entry in entries )
		{
			writer.Write( offset );
			offset += 40 + entry.Data.Length;
		}
		foreach ( var entry in entries )
		{
			writer.Write( 40 );
			writer.Write( entry.Data.Length );
			var name = new byte[16];
			Encoding.ASCII.GetBytes( entry.Name ).CopyTo( name, 0 );
			writer.Write( name );
			writer.Write( 22050 );
			writer.Write( 16 );
			writer.Write( 2 );
			writer.Write( 0 );
			writer.Write( entry.Data );
		}
		return output.ToArray();
	}

	[TestMethod]
	public void DamagedSdtEntriesAreSkippedWithoutSilencingTheBank()
	{
		var bank = CreateBank( ("first.wav", new byte[] { 1, 2, 3 }), ("second.wav", new byte[] { 4, 5 }), ("third.wav", new byte[] { 6 }) );
		// Damage the second entry: its data size now runs past the end of the bank.
		var secondOffset = BitConverter.ToInt32( bank, 8 );
		BitConverter.TryWriteBytes( bank.AsSpan( secondOffset + 4 ), 100000 );
		var messages = new List<string>();
		var previous = SdtArchive.Diagnostic;
		SdtArchive.Diagnostic = messages.Add;
		try
		{
			var archive = new SdtArchive( new MemoryStream( bank ) );
			CollectionAssert.AreEqual( new[] { "first.mp2", "third.mp2" }, archive.GetFiles( "" ) );
			Assert.AreEqual( 1, archive.SkippedEntries.Count );
			StringAssert.Contains( messages.Single(), "entry 1" );
			Assert.ThrowsException<FileNotFoundException>( () => archive.GetFile( "second" ) );

			var badOffset = CreateBank( ("only.wav", new byte[] { 1 }) );
			BitConverter.TryWriteBytes( badOffset.AsSpan( 4 ), 1 << 20 );
			Assert.AreEqual( 0, new SdtArchive( new MemoryStream( badOffset ) ).soundFiles.Count );
			var badCount = new SdtArchive( new MemoryStream( new byte[] { 0xFF, 0xFF, 0xFF, 0x7F } ) );
			Assert.AreEqual( 1, badCount.SkippedEntries.Count );
		}
		finally
		{
			SdtArchive.Diagnostic = previous;
		}
	}

	[TestMethod]
	public void EveryOriginalSdtBankParsesWithoutSkippedEntries()
	{
		var data = StringTableTests.OriginalDataDirectory();
		var banks = Directory.EnumerateFiles( data, "*.sdt", new EnumerationOptions { RecurseSubdirectories = true, MatchCasing = MatchCasing.CaseInsensitive } ).ToArray();
		Assert.AreEqual( 47, banks.Length );
		var entries = 0;
		foreach ( var bank in banks )
		{
			var archive = new SdtArchive( bank );
			Assert.AreEqual( 0, archive.SkippedEntries.Count, $"{bank}: {string.Join( "; ", archive.SkippedEntries )}" );
			entries += archive.soundFiles.Count;
		}
		Assert.IsTrue( entries > 0 );
	}
}

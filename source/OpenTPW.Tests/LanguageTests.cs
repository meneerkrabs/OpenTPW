using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

[TestClass]
[DoNotParallelize]
public class LanguageTests
{
	private string root = "";

	[TestInitialize]
	public void CreateRoot()
	{
		root = Path.Combine( Path.GetTempPath(), $"opentpw-language-{Guid.NewGuid():N}" );
		Directory.CreateDirectory( root );
	}

	[TestCleanup]
	public void DeleteRoot() => Directory.Delete( root, true );

	private string CreateLanguage( string dataDirectory, string folder, string characters = "abc" )
	{
		var directory = Path.Combine( root, dataDirectory, "Language", folder );
		Directory.CreateDirectory( directory );
		File.WriteAllBytes( Path.Combine( directory, "MBToUni.dat" ), StringTableTests.CreateCharacterTable( characters ) );
		File.WriteAllBytes( Path.Combine( directory, "UITEXT.str" ), StringTableTests.CreateStringTable( new byte[] { 1, 2, 3 } ) );
		return directory;
	}

	[TestMethod]
	public void DefaultsToEnglishWhenSeveralLanguagesAreInstalled()
	{
		CreateLanguage( "Data", "German" );
		CreateLanguage( "Data", "English" );
		var language = GameLanguage.Resolve( Path.Combine( root, "Data" ), null, null );
		Assert.AreEqual( "English", language.Name );
		Assert.IsNull( language.OverlayDataDirectory );
		CollectionAssert.AreEqual( new[] { "English", "German" }, GameLanguage.FindLanguages( Path.Combine( root, "Data" ), null ).ToArray() );
	}

	[TestMethod]
	public void GameFolderCopiedFromTheCdFindsItsBannersBesideData()
	{
		CreateLanguage( "Data", "English" );
		Assert.IsNull( GameLanguage.Resolve( Path.Combine( root, "Data" ), null, null ).FindFile( "bankrupt.md2" ), "no banners before the CD folder exists" );
		var meshes = Path.Combine( root, "english", "Meshes", "ENGLISH" );
		Directory.CreateDirectory( meshes );
		File.WriteAllBytes( Path.Combine( meshes, "bankrupt.MD2" ), new byte[] { 1 } );
		Assert.AreEqual( Path.Combine( meshes, "bankrupt.MD2" ), GameLanguage.Resolve( Path.Combine( root, "Data" ), null, null ).FindFile( "BANKRUPT.md2" ) );
	}

	[TestMethod]
	public void DefaultsToTheOnlyInstalledLanguage()
	{
		CreateLanguage( "Data", "danish", "xyz" );
		var language = GameLanguage.Resolve( Path.Combine( root, "Data" ), "", null );
		Assert.AreEqual( "Danish", language.Name );
		Assert.AreEqual( "xyz", language.LoadStrings( "uitext.STR" )[0] );
	}

	[TestMethod]
	public void DefaultsToFirstLanguageWithoutEnglish()
	{
		CreateLanguage( "Data", "Swedish" );
		CreateLanguage( "Data", "German" );
		Assert.AreEqual( "German", GameLanguage.Resolve( Path.Combine( root, "Data" ), null, null ).Name );
	}

	[TestMethod]
	public void RequestedLanguageIgnoresCaseAndMissingLanguageIsReported()
	{
		CreateLanguage( "Data", "English" );
		CreateLanguage( "Data", "German", "cba" );
		var german = GameLanguage.Resolve( Path.Combine( root, "Data" ), "gErMaN", null );
		Assert.AreEqual( "German", german.Name );
		Assert.AreEqual( "cba", german.LoadStrings( "UITEXT.str" )[0] );
		var error = Assert.ThrowsException<DirectoryNotFoundException>( () => GameLanguage.Resolve( Path.Combine( root, "Data" ), "French", null ) );
		StringAssert.Contains( error.Message, "English, German" );
		StringAssert.Contains( error.Message, "--language-data" );
		Assert.ThrowsException<DirectoryNotFoundException>( () => GameLanguage.Resolve( Path.Combine( root, "Data" ), null, Path.Combine( root, "missing" ) ) );
	}

	[TestMethod]
	public void TheChosenCdStaysChoosableWhenTheInstalledLanguageIsSelected()
	{
		CreateLanguage( "Data", "English" );
		CreateLanguage( Path.Combine( "cd", "Danish", "data" ), "danish", "xyz" );
		CreateLanguage( Path.Combine( "cd", "German", "data" ), "German", "zyx" );
		var overlay = Path.Combine( root, "cd" );
		var previous = (GameLanguage.IsSelected ? GameLanguage.Current : null, GameLanguage.AvailableOverlay);
		try
		{
			GameLanguage.Current = GameLanguage.Resolve( Path.Combine( root, "Data" ), null, overlay );
			Assert.IsNull( GameLanguage.Current.OverlayDataDirectory, "English comes from the installation" );
			GameLanguage.AvailableOverlay = overlay;
			CollectionAssert.AreEqual( new[] { "English", "Danish", "German" }, GameLanguage.Choosable().ToArray(), "the CD's languages stay on offer" );
			GameLanguage.AvailableOverlay = null;
			CollectionAssert.AreEqual( new[] { "English" }, GameLanguage.Choosable().ToArray() );
		}
		finally
		{
			if ( previous.Item1 != null )
				GameLanguage.Current = previous.Item1;
			GameLanguage.AvailableOverlay = previous.AvailableOverlay;
		}
	}

	[TestMethod]
	public void CdStyleOverlayRootProvidesEveryLanguage()
	{
		CreateLanguage( "Data", "English" );
		CreateLanguage( Path.Combine( "cd", "Danish", "data" ), "danish", "xyz" );
		CreateLanguage( Path.Combine( "cd", "German", "data" ), "German", "zyx" );
		var meshes = Path.Combine( root, "cd", "German", "Meshes", "German" );
		Directory.CreateDirectory( meshes );
		File.WriteAllText( Path.Combine( meshes, "paused.MD2" ), "banner" );
		var speech = Path.Combine( root, "cd", "German", "data", "levels", "space", "speech", "lips" );
		Directory.CreateDirectory( speech );
		File.WriteAllText( Path.Combine( speech, "sp_001.lip" ), "lip" );
		var baseSpeech = Path.Combine( root, "Data", "levels", "jungle", "Speech", "lips" );
		Directory.CreateDirectory( baseSpeech );
		File.WriteAllText( Path.Combine( baseSpeech, "sp_001.LIP" ), "english" );

		var overlay = Path.Combine( root, "cd" );
		CollectionAssert.AreEqual( new[] { "English", "Danish", "German" }, GameLanguage.FindLanguages( Path.Combine( root, "Data" ), overlay ).ToArray() );
		Assert.AreEqual( "English", GameLanguage.Resolve( Path.Combine( root, "Data" ), null, overlay ).Name, "A multi-language overlay keeps the installed default." );

		var danish = GameLanguage.Resolve( Path.Combine( root, "Data" ), "Danish", overlay );
		Assert.AreEqual( "xyz", danish.LoadStrings( "UITEXT.str" )[0] );
		Assert.AreEqual( Path.Combine( root, "cd", "Danish", "data" ), danish.OverlayDataDirectory );

		var german = GameLanguage.Resolve( Path.Combine( root, "Data" ), "german", overlay );
		Assert.AreEqual( "zyx", german.LoadStrings( "UITEXT.str" )[0] );
		Assert.AreEqual( Path.Combine( meshes, "paused.MD2" ), german.FindFile( "PAUSED.md2" ) );
		Assert.AreEqual( Path.Combine( speech, "sp_001.lip" ), german.ResolveDataFile( "Levels/Space/Speech/Lips/SP_001.LIP" ) );
		Assert.AreEqual( Path.Combine( baseSpeech, "sp_001.LIP" ), german.ResolveDataFile( "levels/jungle/speech/lips/sp_001.lip" ), "Files missing from the overlay fall back to base data." );
		Assert.IsNull( german.ResolveDataFile( "levels/hallow/speech/lips/sp_001.lip" ) );
		Assert.ThrowsException<ArgumentException>( () => german.ResolveDataFile( "../Data/x" ) );
	}

	[TestMethod]
	public void SingleLanguageDataOverlayIsSelectedByDefault()
	{
		CreateLanguage( "Data", "English" );
		CreateLanguage( Path.Combine( "Swedish", "data" ), "Swedish", "cab" );
		var meshes = Path.Combine( root, "Swedish", "Meshes", "Swedish" );
		Directory.CreateDirectory( meshes );
		File.WriteAllText( Path.Combine( meshes, "congrats.MD2" ), "banner" );
		var swedish = GameLanguage.Resolve( Path.Combine( root, "Data" ), null, Path.Combine( root, "Swedish", "data" ) );
		Assert.AreEqual( "Swedish", swedish.Name );
		Assert.AreEqual( "cab", swedish.LoadStrings( "UITEXT.str" )[0] );
		Assert.AreEqual( Path.Combine( meshes, "congrats.MD2" ), swedish.FindFile( "congrats.md2" ) );
		Assert.AreEqual( "English", GameLanguage.Resolve( Path.Combine( root, "Data" ), "English", Path.Combine( root, "Swedish", "data" ) ).Name );
	}

	[TestMethod]
	public void CurrentDefaultsToTheFileSystemRoot()
	{
		CreateLanguage( "Data", "French" );
		var originalFileSystem = FileSystem;
		var original = GameLanguage.IsSelected ? GameLanguage.Current : null;
		try
		{
			FileSystem = new BaseFileSystem( Path.Combine( root, "Data" ) );
			GameLanguage.Current = null!;
			Assert.AreEqual( "French", GameLanguage.Current.Name );
		}
		finally
		{
			FileSystem = originalFileSystem;
			GameLanguage.Current = original!;
		}
	}

	// Private data: OPENTPW_GAME_PATH (installed English) and OPENTPW_LANGUAGE_DATA (a directory
	// holding the CD's <Lang>/data and <Lang>/Meshes trees for Danish, French, German, Swedish
	// from the European CD and Dutch from the Benelux CD).

	public static IEnumerable<object[]> ShippedLanguages => GameLanguage.ShippedLanguages.Select( name => new object[] { name } );

	internal static GameLanguage OriginalLanguagePublic( string name ) => OriginalLanguage( name );

	private static GameLanguage OriginalLanguage( string name )
	{
		var data = StringTableTests.OriginalDataDirectory();
		var overlay = Environment.GetEnvironmentVariable( "OPENTPW_LANGUAGE_DATA" );
		if ( name != "English" && (string.IsNullOrWhiteSpace( overlay ) || !Directory.Exists( overlay )) )
			Assert.Inconclusive( "Set OPENTPW_LANGUAGE_DATA to the CD's extracted language folders." );
		if ( !GameLanguage.FindLanguages( data, name == "English" ? null : overlay ).Contains( name ) )
			Assert.Inconclusive( $"{name} language data is missing." );
		return GameLanguage.Resolve( data, name, name == "English" ? null : overlay );
	}

	private static Dictionary<string, string[]> LoadAllStrings( GameLanguage language ) =>
		language.EnumerateFiles().Where( file => Path.GetExtension( file ).Equals( ".str", StringComparison.OrdinalIgnoreCase ) )
			.ToDictionary( file => Path.GetFileName( file ).ToUpperInvariant(), file => language.LoadStrings( Path.GetFileName( file ), asStored: true ).Entries );

	[DataTestMethod]
	[DynamicData( nameof( ShippedLanguages ) )]
	public void EveryOriginalStringTableDecodes( string name )
	{
		var language = OriginalLanguage( name );
		var tables = LoadAllStrings( language );
		Assert.AreEqual( 21, tables.Count );
		// Two editions: 2,358 entries, or 2,365 with UITEXT's extra entry 207 and six more CHAT_COMMANDS.
		CollectionAssert.Contains( new[] { 2358, 2365 }, tables.Values.Sum( entries => entries.Length ) );
		foreach ( var file in language.EnumerateFiles().Where( file => Path.GetExtension( file ).Equals( ".str", StringComparison.OrdinalIgnoreCase ) ) )
			StringTableTests.AssertLengthsMatchLayout( File.ReadAllBytes( file ), tables[Path.GetFileName( file ).ToUpperInvariant()] );
		var characters = tables.Values.SelectMany( entries => entries ).SelectMany( entry => entry ).Where( c => c > '~' ).Distinct().OrderBy( c => c );
		Assert.AreEqual( ExpectedNonAscii[name], string.Concat( characters ) );
	}

	private static readonly Dictionary<string, string> ExpectedNonAscii = new()
	{
		["English"] = "©é’",
		["Danish"] = "ÅÆØåæéø",
		["Dutch"] = "ºéëï",
		["French"] = "°Çàâçèéêëîïôùûœ",
		["German"] = "ÄÖÜßäéöüš",
		["Swedish"] = "°ÄÅÖäåéö",
	};

	[DataTestMethod]
	[DataRow( "English", "Go Online", "Excitement", "Reliability", "Totem" )]
	[DataRow( "Dutch", "On-line gaan", "Spanning", "Betrouwbaarheid", "Totempaal" )]
	[DataRow( "Danish", "Gå Online", "Spænding", "Pålidelighed", "Totempæl" )]
	[DataRow( "French", "Se connecter", "Excitation", "Solidité", "Inca" )]
	[DataRow( "German", "Online gehen", "Spaßfaktor", "Zuverlässigkeit", "Totemfall" )]
	[DataRow( "Swedish", "Koppla upp", "Spänning", "Tillförlitlighet", "Pålen" )]
	public void OriginalSampleStringsArePinned( string name, string goOnline, string excitement, string reliability, string totem )
	{
		var language = OriginalLanguage( name );
		var uiText = language.LoadStrings( "UITEXT.str" );
		Assert.AreEqual( GameLanguage.UiTextEntries, uiText.Entries.Length );
		Assert.AreEqual( goOnline, uiText[(int)UIStrings.GoOnline] );
		// After entry 207 (the 474-entry edition's extra message), a language-independent anchor.
		Assert.AreEqual( " 640 x 480", uiText[(int)UIStrings.Resolution640x480] );
		Assert.AreEqual( excitement, uiText[(int)UIStrings.Excitement] );
		Assert.AreEqual( reliability, uiText[(int)UIStrings.Reliability] );
		Assert.AreEqual( totem, language.LoadStrings( "OBJECT_NAMES.str" )[29] );
		Assert.IsTrue( uiText[416].Length > 255, "The longest UITEXT entry needs the 24-bit length." );
		if ( name == "French" )
			StringAssert.Contains( language.LoadStrings( "TAG_SYSTEM.str" )[135], "d'œuvre" );
		if ( name == "German" )
			Assert.AreEqual( "Unfuhg Gibsniš", language.LoadStrings( "GUARD_NAMES.str" )[25] );
		if ( name == "Dutch" )
			StringAssert.StartsWith( uiText[472], "Er zijn ansichtkaarten in je out-box" );
		if ( name == "Swedish" )
			StringAssert.StartsWith( uiText[472], "Du har ett vykort i din utkorg.\n\n Koppla upp" );
	}

	[DataTestMethod]
	[DynamicData( nameof( ShippedLanguages ) )]
	public void EveryUsedCharacterHasAGlyphInEveryFontOfItsLanguage( string name )
	{
		var language = OriginalLanguage( name );
		var used = LoadAllStrings( language ).Values.SelectMany( entries => entries ).SelectMany( entry => entry ).Where( c => c != '\n' ).ToHashSet();
		var fonts = language.EnumerateFiles().Where( file => Path.GetExtension( file ).Equals( ".bf4", StringComparison.OrdinalIgnoreCase ) ).ToArray();
		// 33 fonts, or 37 in the 474-entry UITEXT edition, which adds DATEBIG/DATEMED/DATESMALL/DATETINY.
		CollectionAssert.Contains( new[] { 33, 37 }, fonts.Length );
		foreach ( var font in fonts )
		{
			var glyphs = language.LoadFont( Path.GetFileName( font ) ).Glyphs.Select( glyph => glyph.Character ).ToHashSet();
			var missing = used.Where( c => !glyphs.Contains( c ) ).OrderBy( c => c ).ToArray();
			Assert.AreEqual( 0, missing.Length, $"{Path.GetFileName( font )} lacks {string.Concat( missing )}" );
		}

		// The sandbox text panel's strings lay out without fallback glyphs.
		var uiText = language.LoadStrings( "UITEXT.str" );
		var game8 = new FontAtlas( language.LoadFont( "GAME8AA.bf4" ) );
		var sesh = new FontAtlas( language.LoadFont( "SESHMED.bf4" ) );
		Assert.AreEqual( 0, TextLayout.Create( sesh, language.LoadStrings( "OBJECT_NAMES.str" )[29] ).MissingCharacters.Count );
		foreach ( var entry in uiText.Entries )
			Assert.AreEqual( 0, TextLayout.Create( game8, entry ).MissingCharacters.Count, entry );
	}

	[DataTestMethod]
	[DynamicData( nameof( ShippedLanguages ) )]
	public void LanguageSpeechLipsAndBannersResolve( string name )
	{
		var language = OriginalLanguage( name );
		foreach ( var level in new[] { "jungle", "fantasy", "hallow", "space" } )
		{
			var lip = language.ResolveDataFile( $"levels/{level}/speech/lips/sp_001.lip" );
			if ( lip == null && name == "English" )
				continue; // the installed English data only carries the levels it was installed with
			Assert.IsNotNull( lip, $"{name} {level} sp_001.lip" );
			if ( name != "English" )
				Assert.IsTrue( lip!.StartsWith( language.OverlayDataDirectory!, StringComparison.Ordinal ), lip );
			using var stream = File.OpenRead( lip! );
			Assert.IsTrue( new LipSyncFile( stream ).Marks.Count > 0, lip );
		}
		Assert.IsNotNull( language.ResolveDataFile( "global/speech/lips.wad" ) );
		Assert.IsNotNull( language.ResolveDataFile( "global/speech/speechHD.sdt" ) );
		foreach ( var banner in new[] { "bankrupt", "congrats", "paused" } )
			Assert.IsNotNull( language.FindFile( banner + ".md2" ), $"{name} {banner}.MD2" );
	}
}

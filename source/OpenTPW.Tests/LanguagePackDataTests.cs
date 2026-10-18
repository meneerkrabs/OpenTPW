using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTPW.UI.Original;

namespace OpenTPW.Tests;

/// <summary>
/// Private-data tests of the localisation pack (ModDB "Sim Theme Park World Localization Files", docs/LANGUAGES.md), read from the folder
/// <c>OPENTPW_LANGUAGE_PACK</c> names: <c>&lt;Lang&gt;/data/...</c> for English, USEnglish (tp.exe's American), Danish, Dutch, French, German, Hebrew,
/// Italian, Korean, Polish, Spanish, Swedish and the fan Russian (which replaces American). The tests are inconclusive without the variable; with
/// <c>OPENTPW_GAME_PATH</c> the pack goes over that game, else over an empty data folder. Only data files are read; nothing of the pack is run.
/// </summary>
[TestClass]
[DoNotParallelize]
public class LanguagePackDataTests
{
	private static string Pack()
	{
		var path = Environment.GetEnvironmentVariable( "OPENTPW_LANGUAGE_PACK" );
		if ( string.IsNullOrWhiteSpace( path ) || !Directory.Exists( path ) )
			Assert.Inconclusive( "Set OPENTPW_LANGUAGE_PACK to the extracted localisation pack." );
		return path!;
	}

	private static string GameData()
	{
		var game = Environment.GetEnvironmentVariable( "OPENTPW_GAME_PATH" );
		if ( !string.IsNullOrWhiteSpace( game ) && GameLanguage.FindEntry( game, "Data", true ) is { } data )
			return data;
		var empty = Path.Combine( Path.GetTempPath(), "opentpw-no-game-data" );
		Directory.CreateDirectory( empty );
		return empty;
	}

	private static GameLanguage Open( string name ) => GameLanguage.Resolve( GameData(), name, Pack() );

	private static string PackFolder( string folder ) => GameLanguage.FindEntry( Pack(), folder, true ) ?? throw new AssertFailedException( $"The pack has no {folder} folder." );

	public static IEnumerable<object[]> NewLanguages => new[] { new object[] { "American" }, new object[] { "Hebrew" }, new object[] { "Italian" }, new object[] { "Russian" } };

	private static Dictionary<string, StringFile> LoadAll( GameLanguage language ) =>
		language.EnumerateFiles().Where( file => file.EndsWith( ".str", StringComparison.OrdinalIgnoreCase ) )
			.ToDictionary( file => Path.GetFileName( file ).ToUpperInvariant(), file => language.LoadStrings( Path.GetFileName( file ), asStored: true ) );

	/// <summary>The fan Russian keeps Windows-1251 bytes in the Latin-1 table: its strings read as Russian once the characters are taken as those bytes.</summary>
	private static string AsCyrillic( string text )
	{
		Encoding.RegisterProvider( CodePagesEncodingProvider.Instance );
		return Encoding.GetEncoding( 1251 ).GetString( Encoding.Latin1.GetBytes( text ) );
	}

	[TestMethod]
	public void ThePackOffersEveryLanguageUnderItsOwnName()
	{
		var pack = Pack();
		var expected = new[] { "American", "Danish", "Dutch", "English", "French", "German", "Hebrew", "Italian", "Korean", "Polish", "Russian", "Spanish", "Swedish" };
		CollectionAssert.IsSubsetOf( expected, GameLanguage.FindLanguages( GameData(), pack ).ToArray() );
		CollectionAssert.IsSubsetOf( expected, GameLanguage.Readable( GameData(), pack ).ToArray() );
		var american = Open( "American" );
		Assert.AreEqual( ("American", 0x409, false), (american.OriginalName, american.LanguageId, american.IsFanTranslation) );
		StringAssert.EndsWith( american.Directory, Path.Combine( "USEnglish", "data", "language", "american" ) );
		var russian = Open( "Russian" );
		Assert.AreEqual( ("American", 0x409, true), (russian.OriginalName, russian.LanguageId, russian.IsFanTranslation) );
		StringAssert.EndsWith( russian.Directory, Path.Combine( "Russian", "data", "language", "American" ) );
		var hebrew = Open( "Hebrew" );
		Assert.AreEqual( (0x40D, true), (hebrew.LanguageId, hebrew.IsRightToLeft) );
		Assert.IsFalse( Open( "Italian" ).IsRightToLeft );
		// Pointing at one folder of the pack (or its data folder) selects that folder's language.
		foreach ( var (folder, name) in new[] { ("USEnglish", "American"), ("Russian", "Russian"), ("Hebrew", "Hebrew"), ("Italian", "Italian") } )
			foreach ( var overlay in new[] { PackFolder( folder ), Path.Combine( PackFolder( folder ), "data" ) } )
				Assert.AreEqual( name, GameLanguage.Resolve( GameData(), null, overlay ).Name, overlay );
	}

	[DataTestMethod]
	[DynamicData( nameof( NewLanguages ) )]
	public void EveryStringTableDecodesWithNoUnknownCode( string name )
	{
		var language = Open( name );
		var tables = LoadAll( language );
		Assert.AreEqual( 21, tables.Count );
		// The 474-entry UITEXT and the six later CHAT_COMMANDS: the edition of the European CD.
		Assert.AreEqual( 2365, tables.Values.Sum( table => table.Entries.Length ) );
		foreach ( var file in language.EnumerateFiles().Where( file => file.EndsWith( ".str", StringComparison.OrdinalIgnoreCase ) ) )
			StringTableTests.AssertLengthsMatchLayout( File.ReadAllBytes( file ), tables[Path.GetFileName( file ).ToUpperInvariant()].Entries );
		var characters = string.Concat( tables.Values.SelectMany( table => table.Entries ).SelectMany( entry => entry ).Where( c => c > '~' ).Distinct().OrderBy( c => c ) );
		var expected = name switch
		{
			"American" => "é",
			"Hebrew" => "אבגדהוזחטיךכלםמןנסעףפץצקרשת",
			"Italian" => "°àèéìòù",
			// the fan Russian: Windows-1251 Cyrillic stored as Latin-1 (Ё ё Ъ and Ã ã are unused)
			_ => "ÀÁÂÄÅÆÇÈÉÊËÌÍÎÏÐÑÒÓÔÕÖ×ØÙÛÜÝÞßàáâãäåæçèéêëìíîïðñòóôõö÷øùúûüýþÿ",
		};
		Assert.AreEqual( expected, characters );
		Assert.IsTrue( tables.Values.SelectMany( table => table.Entries ).All( entry => entry.All( c => c != '�' && !char.IsControl( c ) || c is '\n' or '\t' or '\a' or '\v' ) ) );
	}

	[DataTestMethod]
	[DataRow( "American", "Go Online", "Lost Kingdom", "Totem" )]
	[DataRow( "Hebrew", "התחבר לרשת", "ממלכה אבודה", "אינקא" )]
	[DataRow( "Italian", "Online", "Lost Kingdom", "Inca" )]
	public void SampleStringsArePinned( string name, string goOnline, string lostKingdom, string totem )
	{
		var language = Open( name );
		var uiText = language.LoadStrings( "UITEXT.str" );
		Assert.AreEqual( GameLanguage.UiTextEntries, uiText.Entries.Length );
		Assert.AreEqual( goOnline, uiText[(int)UIStrings.GoOnline] );
		Assert.AreEqual( "640 x 480", uiText[(int)UIStrings.Resolution640x480].Trim(), "a language-independent anchor after the renumbered entry" );
		Assert.AreEqual( lostKingdom, language.LoadStrings( "THEMENAMES.str" )[0] );
		Assert.AreEqual( totem, language.LoadStrings( "OBJECT_NAMES.str" )[29] );
		Assert.IsTrue( uiText[416].Length > 150, "a long UITEXT entry decodes whole" );
		Assert.AreEqual( name, UiStringTable.Load( language ).Language );
	}

	[TestMethod]
	public void TheFanRussianIsWindows1251InTheLatinTable()
	{
		var russian = Open( "Russian" );
		var american = Open( "American" );
		// Same character table as US English; the interface table (UITEXT) is left in English, the names, objects and messages are Russian.
		CollectionAssert.AreEqual( american.CharacterTable.Characters.ToArray(), russian.CharacterTable.Characters.ToArray() );
		Assert.AreEqual( "Go Online", russian.LoadStrings( "UITEXT.str" )[(int)UIStrings.GoOnline] );
		Assert.AreEqual( "Потеряное Королевство", AsCyrillic( russian.LoadStrings( "THEMENAMES.str" )[0] ) );
		Assert.AreEqual( "Тотема", AsCyrillic( russian.LoadStrings( "OBJECT_NAMES.str" )[29] ) );
		Assert.AreEqual( "Уборщик", AsCyrillic( russian.LoadStrings( "STAFF_TYPES.str" )[0] ) );
	}

	[DataTestMethod]
	[DynamicData( nameof( NewLanguages ) )]
	public void KeyboardShortcutsDecode( string name )
	{
		var keyboard = Open( name ).LoadStrings( "KEYBOARD.str" ).Entries;
		Assert.AreEqual( 21, keyboard.Length );
		Assert.AreEqual( name == "Italian" ? "BHIASVFLTGRCSVCHKO,.P" : "BHIASVFLTGRCSVPHCO,.P", string.Concat( keyboard ) );
	}

	[DataTestMethod]
	[DynamicData( nameof( NewLanguages ) )]
	public void TheCharacterTableAndUniToMBRoundTripEveryStringAndCharacter( string name )
	{
		var language = Open( name );
		var codec = GameTextCodec.Load( language );
		var characters = language.CharacterTable.Characters.Distinct().ToArray();
		foreach ( var character in characters )
			Assert.IsTrue( codec.CanEncode( character ), $"{name}: U+{(int)character:X4}" );
		Assert.AreEqual( characters.Length, codec.Repertoire.Count );
		foreach ( var (file, strings) in LoadAll( language ) )
			foreach ( var entry in strings.Entries )
			{
				var unmappable = new List<char>();
				Assert.AreEqual( entry, codec.Decode( codec.Encode( entry, unmappable ) ), $"{name} {file}" );
				Assert.AreEqual( 0, unmappable.Count );
			}
	}

	[TestMethod]
	public void TheHebrewTableIsTheOneLeadByteLayoutOfLatin1AndCodePage1255()
	{
		var hebrew = Open( "Hebrew" );
		var table = hebrew.CharacterTable;
		Assert.AreEqual( (1, 254, 0xFF, 329), (table.LeadByteCount, table.SingleByteCount, table.FirstLeadByte, table.Characters.Count) );
		// The repertoire is U+0001..U+00FF and every other character of code page 1255: the Hebrew points and letters, the sheqel sign, the
		// direction marks and the Windows punctuation. The codes are Bullfrog's indices, not Windows-1255 bytes.
		var expected = Enumerable.Range( 1, 255 ).Select( code => (char)code ).ToHashSet();
		expected.UnionWith( new[] { 0x192, 0x2C6, 0x2DC, 0x200E, 0x200F, 0x2013, 0x2014, 0x2018, 0x2019, 0x201A, 0x201C, 0x201D, 0x201E, 0x2020, 0x2021, 0x2022, 0x2026,
			0x2030, 0x2039, 0x203A, 0x20AA, 0x20AC, 0x2122 }.Select( code => (char)code ) );
		expected.UnionWith( Enumerable.Range( 0x5B0, 0x5C4 - 0x5B0 ).Where( code => code != 0x5BA ).Concat( Enumerable.Range( 0x5D0, 27 ) ).Concat( Enumerable.Range( 0x5F0, 5 ) ).Select( code => (char)code ) );
		CollectionAssert.AreEquivalent( expected.ToArray(), table.Characters.ToArray() );
		Assert.IsTrue( table.Characters.Contains( '‏' ) && table.Characters.Contains( '₪' ) );
		// Hebrew letters are single bytes; the ASCII capitals sit behind the lead byte, as in the Polish table.
		Assert.IsTrue( table.Characters.ToList().IndexOf( 'א' ) + 1 <= table.SingleByteCount );
		Assert.IsTrue( table.Characters.ToList().IndexOf( 'A' ) + 1 > table.SingleByteCount );
		// The tables keep Hebrew in logical order (the first letter first): "Go Online" starts with he.
		Assert.AreEqual( 'ה', hebrew.LoadStrings( "UITEXT.str" )[(int)UIStrings.GoOnline][0] );
	}

	[DataTestMethod]
	[DynamicData( nameof( NewLanguages ) )]
	public void EveryUsedCharacterHasAGlyphInEveryFontOfItsLanguage( string name )
	{
		var language = Open( name );
		var used = LoadAll( language ).Values.SelectMany( table => table.Entries ).SelectMany( entry => entry ).Where( c => c > ' ' ).ToHashSet();
		used.UnionWith( SupplementaryStrings.AllTexts( name, SupplementaryStrings.DrawableCharacters( language ).Contains ).SelectMany( text => text ).Where( c => c > ' ' ) );
		var fonts = language.EnumerateFiles().Where( file => file.EndsWith( ".bf4", StringComparison.OrdinalIgnoreCase ) ).ToArray();
		// 37 fonts (the Patch 2 set with the four DATE fonts); the Hebrew folder lacks GAMEBOLD9/10/12 and MATISSE18/36.
		Assert.AreEqual( name == "Hebrew" ? 32 : 37, fonts.Length );
		var latinOnly = new List<string>();
		foreach ( var path in fonts )
		{
			var fileName = Path.GetFileName( path );
			var font = language.LoadFont( fileName );
			var glyphs = font.Glyphs.Select( glyph => glyph.Character ).ToHashSet();
			// The Hebrew CASH, DATE, SESH and console fonts have no Hebrew letter (UiFonts gives such a font's roles to the label font); they still draw ASCII.
			var hebrewLess = name == "Hebrew" && !glyphs.Any( TextVisualOrder.IsHebrew );
			if ( hebrewLess )
				latinOnly.Add( Path.GetFileNameWithoutExtension( fileName ).ToUpperInvariant() );
			var missing = used.Where( c => !glyphs.Contains( c ) && !(hebrewLess && c > '~') ).OrderBy( c => c ).ToArray();
			Assert.AreEqual( 0, missing.Length, $"{name} {fileName} has no glyph for {string.Concat( missing )}" );
		}
		if ( name == "Hebrew" )
			CollectionAssert.AreEquivalent( new[] { "CASHBIG", "CASHMED", "CASHSMALL", "CONSOLE6", "COURIER8", "DATEBIG", "DATEMED", "DATESMALL", "DATETINY", "SESHBIG", "SESHMED", "SESHSMALL" }, latinOnly );
	}

	[DataTestMethod]
	[DynamicData( nameof( NewLanguages ) )]
	public void TheUiFontsLoadInEveryTierAndEveryUiStringLaysOut( string name )
	{
		var language = Open( name );
		var strings = UiStringTable.Load( language );
		foreach ( var tier in Enum.GetValues<UiFontTier>() )
		{
			var fonts = UiFonts.Load( language, tier );
			var texts = Enum.GetValues<UIStrings>().Select( id => strings[id] ).Where( text => text.Length > 0 ).Concat( SupplementaryStrings.AllTexts( name, SupplementaryStrings.DrawableCharacters( language ).Contains ) ).ToArray();
			foreach ( var atlas in new[] { fonts.Small, fonts.Label, fonts.Menu, fonts.Cash, fonts.Heading } )
				foreach ( var text in texts )
					Assert.AreEqual( 0, TextLayout.Create( atlas, text ).MissingCharacters.Count( c => !char.IsWhiteSpace( c ) ), $"{name} {tier}: {text}" );
			var date = string.Format( strings.Extra( OpenTpwText.DateFormat ), 12, 6, 3 );
			Assert.AreEqual( 0, TextLayout.Create( fonts.Date, date ).MissingCharacters.Count, $"{name} {tier} date {date}" );
		}
	}

	public static IEnumerable<object[]> PackLanguages => new[] { "English", "American", "Danish", "Dutch", "French", "German", "Hebrew", "Italian", "Korean", "Polish", "Russian", "Spanish", "Swedish" }
		.Select( name => new object[] { name } );

	[DataTestMethod]
	[DynamicData( nameof( PackLanguages ) )]
	public void TheLanguageListIsDrawnInEveryLanguage( string name )
	{
		// The options' language row shows each language by its own name where the interface font draws it, else by its English name, and the fan
		// Russian with its label; in every language of the pack each of those lays out in the label font without a missing glyph.
		var language = Open( name );
		var drawable = SupplementaryStrings.DrawableCharacters( language );
		var fonts = UiFonts.Load( language );
		var fanLabel = UiStringTable.Load( language ).Extra( OpenTpwText.FanTranslation );
		var names = SupplementaryStrings.LanguageNames.Keys.Select( key => GameLanguage.IsFanTranslationName( key )
			? string.Format( fanLabel, SupplementaryStrings.DisplayName( key, drawable.Contains ) ) : SupplementaryStrings.DisplayName( key, drawable.Contains ) ).ToArray();
		foreach ( var text in names )
			Assert.AreEqual( 0, TextLayout.Create( fonts.Label, text ).MissingCharacters.Count, $"{name}: {text}" );
		if ( name == "Hebrew" )
			CollectionAssert.IsSubsetOf( new[] { "עברית", "French", "Russian - תרגום מעריצים" }, names );
		if ( name == "Korean" )
			CollectionAssert.IsSubsetOf( new[] { "한국어", "Français", "Русский (팬 번역)" }, names );
		if ( name is "American" or "English" or "Russian" )
			CollectionAssert.IsSubsetOf( new[] { "English (US)", "Italiano", "Hebrew", "Russian (fan translation)" }, names );
	}

	[TestMethod]
	public void HebrewTextIsLaidOutRightToLeftAndRightAligned()
	{
		var hebrew = Open( "Hebrew" );
		var strings = UiStringTable.Load( hebrew );
		var fonts = UiFonts.Load( hebrew );
		string Drawn( string text ) => string.Concat( TextLayout.Create( fonts.Label, text ).Glyphs.OrderBy( glyph => glyph.X ).Select( glyph => glyph.Character ) );
		// "Go Online" (logical התחבר לרשת) is drawn from its last letter on the left to its first on the right.
		Assert.AreEqual( "תשרלרבחתה", Drawn( strings[UIStrings.GoOnline] ) );
		// A message with a Latin word keeps that word's own order.
		var error = hebrew.LoadStrings( "ERRORMSG.str" ).Entries.First( entry => entry.Contains( "DirectX" ) && TextVisualOrder.HasHebrew( entry ) );
		StringAssert.Contains( Drawn( error ), "DirectX" );
		StringAssert.Contains( TextVisualOrder.Reorder( error ), "DirectX" );
		// The interface swaps left and right alignment in Hebrew.
		var context = new UiContext( strings, fonts, new UiModels( file => throw new FileNotFoundException( file ) ) ) { Canvas = new UiCanvas( 1024, 768 ) };
		Assert.IsTrue( context.RightToLeft );
		Assert.AreEqual( UiAlign.Right, context.Drawn( UiAlign.Left ) );
		Assert.IsFalse( new UiContext( UiStringTable.Load( Open( "Italian" ) ), fonts, context.Models ).RightToLeft );
	}

	// ---- The files beside the text: ui.wad, Init and postcard ----

	[TestMethod]
	public void USEnglishBringsItsOwnInterfaceArchiveInitPicturesAndPostcard()
	{
		var american = Open( "American" );
		var archive = american.OwnInterfaceArchive;
		Assert.IsNotNull( archive );
		StringAssert.EndsWith( archive, Path.Combine( "USEnglish", "data", "ui.wad" ) );
		using var us = new WadArchive( archive! );
		using var uk = new WadArchive( Path.Combine( PackFolder( "English" ), "data", "ui.wad" ) );
		// The two archives differ only in the logo (Sim Theme Park in the US, Theme Park World elsewhere), at both texture sizes.
		var different = new List<string>();
		foreach ( var folder in new[] { "textures", "stexture" } )
		{
			var names = uk.GetFiles( folder ).Select( Path.GetFileName ).ToArray();
			Assert.IsTrue( names.Length > 300, $"{folder}: {names.Length}" );
			different.AddRange( names.Where( member => !uk.GetFile( Path.Combine( folder, member! ) ).GetData().AsSpan().SequenceEqual( us.GetFile( Path.Combine( folder, member! ) ).GetData() ) )
				.Select( member => $"{folder}/{member!.ToLowerInvariant()}" ) );
		}
		CollectionAssert.AreEqual( new[] { "textures/tpw_logo.wct", "stexture/tpw_logo.wct" }, different );
		Assert.AreEqual( uk.GetFiles( "" ).Count(), us.GetFiles( "" ).Count() );

		// Init: the US splash and legal notice carry tp.exe's name for the language (American); there is no welcome_American, so later loading screens show
		// the background alone.
		StringAssert.EndsWith( american.ResolveInitImage( GameLanguage.InitImage.Splash, 640 ), Path.Combine( "USEnglish", "data", "Init", "640", "Splash_American.tga" ) );
		StringAssert.EndsWith( american.ResolveInitImage( GameLanguage.InitImage.Legal, 1024 ), Path.Combine( "USEnglish", "data", "Init", "1024", "Legal_American.tga" ) );
		StringAssert.EndsWith( american.ResolveInitImage( GameLanguage.InitImage.Background, 800 ), Path.Combine( "USEnglish", "data", "Init", "800", "Welcome.tga" ) );
		Assert.IsNull( american.ResolveInitImage( GameLanguage.InitImage.Welcome, 640 ) );
		// The postcard logo is the language's own (the US one differs from the others).
		var postcard = american.ResolvePostcardFile( "tpwlogo.tga" );
		StringAssert.EndsWith( postcard, Path.Combine( "USEnglish", "data", "postcard", "tpwlogo.tga" ) );
		CollectionAssert.AreNotEqual( File.ReadAllBytes( Path.Combine( PackFolder( "English" ), "data", "postcard", "tpwlogo.tga" ) ), File.ReadAllBytes( postcard! ) );

		// Read through the game's file system, ui/textures/tpw_logo.wct comes from the language's archive.
		var data = Path.Combine( Path.GetTempPath(), "opentpw-pack-ui-" + Guid.NewGuid().ToString( "N" ) );
		Directory.CreateDirectory( Path.Combine( data, "Language", "English" ) );
		try
		{
			File.Copy( Path.Combine( Open( "English" ).Directory, "MBToUni.dat" ), Path.Combine( data, "Language", "English", "MBToUni.dat" ) );
			File.Copy( Path.Combine( PackFolder( "English" ), "data", "ui.wad" ), Path.Combine( data, "ui.wad" ) );
			var files = new BaseFileSystem( data ) { CacheLookups = true };
			files.RegisterArchiveHandler<WadArchive>( ".wad" );
			var before = files.ReadAllBytes( "/ui/textures/tpw_logo.wct" );
			LanguageInterfaceArchive.Apply( files, GameLanguage.Resolve( data, "American", Pack() ) );
			var after = files.ReadAllBytes( "/ui/textures/tpw_logo.wct" );
			CollectionAssert.AreNotEqual( before, after );
			CollectionAssert.AreEqual( us.GetFile( Path.Combine( "textures", "tpw_logo.wct" ) ).GetData(), after );
		}
		finally
		{
			LanguageInterfaceArchive.Apply( new BaseFileSystem( data ), GameLanguage.Resolve( data, "English", null ) );
			Directory.Delete( data, true );
		}
	}

	[TestMethod]
	public void InitPicturesFollowTheLanguagesTableName()
	{
		var hebrew = Open( "Hebrew" );
		// The Hebrew pack has only welcome_Hebrew and the background: tp.exe shows no splash and no legal notice then.
		Assert.IsNull( hebrew.ResolveInitImage( GameLanguage.InitImage.Splash, 640 ) );
		Assert.IsNull( hebrew.ResolveInitImage( GameLanguage.InitImage.Legal, 640 ) );
		StringAssert.EndsWith( hebrew.ResolveInitImage( GameLanguage.InitImage.Welcome, 640 ), Path.Combine( "Hebrew", "data", "Init", "640", "Welcome_Hebrew.tga" ) );
		var italian = Open( "Italian" );
		foreach ( var width in GameLanguage.InitWidths )
			foreach ( var image in Enum.GetValues<GameLanguage.InitImage>() )
				Assert.IsNotNull( italian.ResolveInitImage( image, width ), $"Italian {image} {width}" );
		// The fan Russian keeps the American names (it replaces that folder).
		StringAssert.EndsWith( Open( "Russian" ).ResolveInitImage( GameLanguage.InitImage.Splash, 640 ), Path.Combine( "Russian", "data", "Init", "640", "Splash_American.tga" ) );
		// The Korean pack folder has no Init folder.
		Assert.IsNull( Open( "Korean" ).ResolveInitImage( GameLanguage.InitImage.Welcome, 640 ) );
	}

	[TestMethod]
	public void TheOfficialCdsCarryEveryLanguagesInitPicturesInTheGamesDataFolder()
	{
		var game = Environment.GetEnvironmentVariable( "OPENTPW_GAME_PATH" );
		if ( string.IsNullOrWhiteSpace( game ) || GameLanguage.FindEntry( game, "Data", true ) is not { } data )
		{
			Assert.Inconclusive( "Set OPENTPW_GAME_PATH for the game whose Init folder holds the CD's languages." );
			return;
		}
		// The Benelux CD's Data/Init holds Splash, Legal and Welcome pictures for its languages next to English; the language overlays (Dutch/data) have
		// no Init, ui.wad or postcard of their own, so the game's folder serves them, as in tp.exe's one data folder.
		var english = GameLanguage.Resolve( data, "English", null );
		Assert.IsNotNull( english.ResolveInitImage( GameLanguage.InitImage.Splash, 640 ) );
		if ( GameLanguage.FindEntry( data, "Init/640/Splash_Dutch.tga", false ) == null )
			Assert.Inconclusive( "This game folder's Init has no Dutch pictures (not the Benelux CD)." );
		var overlay = Path.GetDirectoryName( data )!;
		var dutch = GameLanguage.Resolve( data, "Dutch", overlay );
		Assert.IsNull( dutch.OwnInterfaceArchive );
		StringAssert.EndsWith( dutch.ResolveInitImage( GameLanguage.InitImage.Splash, 640 ), "Splash_Dutch.tga" );
		StringAssert.StartsWith( dutch.ResolveInitImage( GameLanguage.InitImage.Splash, 640 ), data );
	}

	// ---- Speech ----

	[DataTestMethod]
	[DynamicData( nameof( NewLanguages ) )]
	public void LanguageSpeechAndLipsResolve( string name )
	{
		var language = Open( name );
		var lipsPath = language.ResolveDataFile( "global/speech/lips.wad" );
		var speechPath = language.ResolveDataFile( "global/speech/speechHD.sdt" );
		Assert.IsNotNull( lipsPath );
		Assert.IsNotNull( speechPath );
		StringAssert.StartsWith( speechPath, language.OverlayDataDirectory! );
		foreach ( var level in new[] { "jungle", "fantasy", "hallow", "space" } )
		{
			var lip = language.ResolveDataFile( $"levels/{level}/speech/lips/sp_001.lip" );
			Assert.IsNotNull( lip, $"{name} {level} sp_001.lip" );
			using var stream = File.OpenRead( lip! );
			Assert.IsTrue( new LipSyncFile( stream ).Marks.Count > 0, lip );
		}
		using var lipsStream = File.OpenRead( lipsPath! );
		Assert.AreEqual( 637, new WadArchive( lipsStream ).GetFiles( "" ).Count() );
		using var speech = new SdtArchive( File.OpenRead( speechPath! ) );
		Assert.IsTrue( speech.GetFiles( "" ).Count() is >= 637 and <= 641 );
	}

	[TestMethod]
	public void USEnglishHasItsOwnVoice()
	{
		// The US advisor (Terry McGovern) is another recording than the UK one (Lewis Macleod): the speech banks differ, the fan Russian has its own too.
		var american = File.ReadAllBytes( Open( "American" ).ResolveDataFile( "global/speech/speechHD.sdt" )! );
		var english = File.ReadAllBytes( Path.Combine( PackFolder( "English" ), "data", "global", "speech", "speechHD.SDT" ) );
		var russian = File.ReadAllBytes( Open( "Russian" ).ResolveDataFile( "global/speech/speechHD.sdt" )! );
		CollectionAssert.AreNotEqual( english, american );
		CollectionAssert.AreNotEqual( american, russian );
	}
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTPW.UI.Original;

namespace OpenTPW.Tests;

/// <summary>
/// Private-data tests of the Spanish CD, the Korean EA Slim Pack CD and the Polish CD (docs/LANGUAGES.md). Each CD is read from its own
/// extracted folder: <c>OPENTPW_ES_PATH</c> (the Spanish CD's root, with <c>Data</c> and <c>Spanish</c>), <c>OPENTPW_KR_PATH</c> (the Korean CD's
/// root, with <c>Data</c> and <c>Korean</c>) and <c>OPENTPW_PL_PATH</c> (a folder with <c>Polish/data</c>, such as the Benelux CD's root).
/// The tests are inconclusive without their variable. Only data files are read; no executable of a CD is opened.
/// </summary>
[TestClass]
[DoNotParallelize]
public class NewLanguageDataTests
{
	private static string RequireFolder( string variable )
	{
		var path = Environment.GetEnvironmentVariable( variable );
		if ( string.IsNullOrWhiteSpace( path ) || !Directory.Exists( path ) )
			Assert.Inconclusive( $"Set {variable} to the extracted CD folder." );
		return path!;
	}

	private static string CdData( string variable ) =>
		GameLanguage.FindEntry( RequireFolder( variable ), "Data", true ) ?? throw new AssertFailedException( $"{variable} has no Data folder." );

	/// <summary>The CD's language as the player gets it: Spanish and Korean as the game folder, Polish as an overlay over an installed game.</summary>
	private static GameLanguage Open( string name ) => name switch
	{
		"Spanish" => GameLanguage.Resolve( CdData( "OPENTPW_ES_PATH" ), "Spanish", null ),
		"Korean" => GameLanguage.Resolve( CdData( "OPENTPW_KR_PATH" ), "Korean", null ),
		_ => PolishLanguage()
	};

	private static GameLanguage PolishLanguage()
	{
		var overlay = RequireFolder( "OPENTPW_PL_PATH" );
		var game = Path.Combine( Path.GetTempPath(), "opentpw-no-game-data" );
		return GameLanguage.Resolve( GameLanguage.FindEntry( overlay, "Data", true ) ?? game, "Polish", overlay );
	}

	public static IEnumerable<object[]> Languages => new[] { new object[] { "Spanish" }, new object[] { "Korean" }, new object[] { "Polish" } };

	private static Dictionary<string, StringFile> LoadAll( GameLanguage language ) =>
		language.EnumerateFiles().Where( file => file.EndsWith( ".str", StringComparison.OrdinalIgnoreCase ) )
			.ToDictionary( file => Path.GetFileName( file ).ToUpperInvariant(), file => language.LoadStrings( Path.GetFileName( file ), asStored: true ) );

	[DataTestMethod]
	[DynamicData( nameof( Languages ) )]
	public void EveryStringTableDecodesWithNoUnknownCode( string name )
	{
		var language = Open( name );
		var tables = LoadAll( language );
		Assert.AreEqual( 21, tables.Count );
		// 2,358 entries, or 2,365 with the 474-entry UITEXT and six more CHAT_COMMANDS (the Korean CD).
		Assert.AreEqual( name == "Korean" ? 2365 : 2358, tables.Values.Sum( table => table.Entries.Length ) );
		foreach ( var table in tables.Values )
			Assert.AreEqual( table.Entries.Length, table.Templates.Length );
		var characters = string.Concat( tables.Values.SelectMany( table => table.Entries ).SelectMany( entry => entry ).Where( c => c > '~' ).Distinct().OrderBy( c => c ) );
		switch ( name )
		{
			case "Spanish":
				Assert.AreEqual( "¡º¿ÁÍÓáéíñóú", characters );
				break;
			case "Polish":
				Assert.AreEqual( "ÓóĄąćĘęŁłŃńŚśźŻż", characters );
				break;
			default:
				// Korean text is Hangul only (697 syllables) plus ASCII: the Latin letters themselves are two-byte codes in this table.
				Assert.AreEqual( 697, characters.Length );
				Assert.IsTrue( characters.All( c => c is >= '가' and <= '힣' ) );
				break;
		}
		Assert.IsTrue( tables.Values.SelectMany( table => table.Entries ).All( entry => entry.All( c => c != '�' && !char.IsControl( c ) || c is '\n' or '\t' or '\a' or '\v' ) ) );
	}

	[DataTestMethod]
	[DataRow( "Spanish", "Ir En Línea", "Emoción", "Fiabilidad", "Inca", "El Reino Perdido" )]
	[DataRow( "Korean", "온라인으로", "흥미도", "안정성", "토템", "잃어버린 왕국" )]
	[DataRow( "Polish", "Z połączeniem", "Ocena wrażeń", "Niezawodność", "Inki", "Zaginione Królestwo" )]
	public void SampleStringsArePinned( string name, string goOnline, string excitement, string reliability, string totem, string lostKingdom )
	{
		var language = Open( name );
		var uiText = language.LoadStrings( "UITEXT.str" );
		Assert.AreEqual( GameLanguage.UiTextEntries, uiText.Entries.Length );
		Assert.AreEqual( goOnline, uiText[(int)UIStrings.GoOnline] );
		Assert.AreEqual( "640 x 480", uiText[(int)UIStrings.Resolution640x480].Trim(), "a language-independent anchor after the renumbered entry" );
		Assert.AreEqual( excitement, uiText[(int)UIStrings.Excitement] );
		Assert.AreEqual( reliability, uiText[(int)UIStrings.Reliability] );
		Assert.AreEqual( totem, language.LoadStrings( "OBJECT_NAMES.str" )[29] );
		Assert.AreEqual( lostKingdom, language.LoadStrings( "THEMENAMES.str" )[0] );
		Assert.IsTrue( uiText[416].Length > 150, "a long UITEXT entry decodes whole" );
		Assert.AreEqual( name, UiStringTable.Load( language ).Language );
	}

	[DataTestMethod]
	[DynamicData( nameof( Languages ) )]
	public void KeyboardShortcutsDecode( string name )
	{
		// KEYBOARD.str holds 21 one-letter shortcuts; in the Polish and Korean tables even an ASCII letter is a two-byte code.
		var keyboard = Open( name ).LoadStrings( "KEYBOARD.str" ).Entries;
		Assert.AreEqual( 21, keyboard.Length );
		Assert.AreEqual( "BHIASVFLTGRCSVPHCO,.P", string.Concat( keyboard ) );
		Assert.IsTrue( keyboard.All( entry => entry.Length == 1 ) );
	}

	[DataTestMethod]
	[DynamicData( nameof( Languages ) )]
	public void TheCharacterTableAndUniToMBRoundTripEveryStringAndCharacter( string name )
	{
		var language = Open( name );
		var table = language.CharacterTable;
		var codec = GameTextCodec.Load( language );
		var characters = table.Characters.Distinct().ToArray();
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
		var euro = new List<char>();
		Assert.AreEqual( "?", codec.Decode( codec.Encode( "☃", euro ) ), "unrepresentable characters become '?'" );
		CollectionAssert.AreEqual( new[] { '☃' }, euro );
	}

	[TestMethod]
	public void TheKoreanTableIsTheSixtySevenLeadByteLayout()
	{
		var language = Open( "Korean" );
		var table = language.CharacterTable;
		Assert.AreEqual( (67, 188, 0xBD, 17270), (table.LeadByteCount, table.SingleByteCount, table.FirstLeadByte, table.Characters.Count) );
		// The Latin letters are behind lead bytes: 'A' is index 17225, the second-to-last lead byte's range.
		var index = table.Characters.ToList().IndexOf( 'A' ) + 1;
		Assert.AreEqual( 17225, index );
		Assert.IsTrue( index > table.SingleByteCount );
		// Hangul syllables in descending order from U+D7A3; KS X 1001 hanja before them.
		Assert.AreEqual( '힣', table.Characters.First( ch => ch is >= '가' and <= '힣' ) );
		Assert.AreEqual( 11172, table.Characters.Count( ch => ch is >= '가' and <= '힣' ) );
		// UniToMB.dat stores the same (lead, trail) pair for every two-byte character.
		var bfum = new BFUMReader( language.OpenRead( "UniToMB.dat" ) );
		Assert.AreEqual( 17270, bfum.Entries.Count );
		foreach ( var (character, value) in bfum.Entries.Where( pair => pair.Value > 0xFF ).Take( 5000 ) )
		{
			var tableIndex = table.TwoByteIndex( (byte)(value & 0xFF), (byte)(value >> 8) );
			Assert.AreEqual( character, table.Characters[tableIndex - 1], $"U+{(int)character:X4} -> {value:X4}" );
		}
	}

	[TestMethod]
	public void ThePolishTableIsTheOneLeadByteLayout()
	{
		var language = Open( "Polish" );
		var table = language.CharacterTable;
		Assert.AreEqual( (1, 254, 0xFF, 329), (table.LeadByteCount, table.SingleByteCount, table.FirstLeadByte, table.Characters.Count) );
		// The Polish letters (Ą ą Ć ć Ę ę Ł ł Ń ń Ó ó Ś ś Ź ź Ż ż) are single bytes; the ASCII capitals, digits' neighbours and punctuation from ^ down to
		// the control characters sit behind the one lead byte (FF xx), which is why even a KEYBOARD.str letter is a two-byte code.
		foreach ( var letter in "ĄąĆćĘęŁłŃńÓóŚśŹźŻż" )
			Assert.IsTrue( table.Characters.ToList().IndexOf( letter ) + 1 <= table.SingleByteCount, $"{letter} is a single-byte code" );
		foreach ( var letter in "AMZ:+" )
			Assert.IsTrue( table.Characters.ToList().IndexOf( letter ) + 1 > table.SingleByteCount, $"{letter} is a two-byte code" );
		Assert.AreEqual( "Zaginione Królestwo", language.LoadStrings( "THEMENAMES.str" )[0] );
		StringAssert.Contains( string.Concat( language.LoadStrings( "UITEXT.str" ).Entries ), "ł" );
	}

	[DataTestMethod]
	[DynamicData( nameof( Languages ) )]
	public void EveryUsedCharacterHasADrawnGlyphInEveryFontOfItsLanguage( string name )
	{
		var language = Open( name );
		var used = LoadAll( language ).Values.SelectMany( table => table.Entries ).SelectMany( entry => entry ).Where( c => c != '\n' && c != '\t' ).ToHashSet();
		var repertoire = language.CharacterTable.Characters.ToHashSet();
		used.UnionWith( SupplementaryStrings.AllTexts( name, repertoire.Contains ).SelectMany( text => text ).Where( c => c != '\n' && c != '\t' ) );
		var fonts = language.EnumerateFiles().Where( file => file.EndsWith( ".bf4", StringComparison.OrdinalIgnoreCase ) ).ToArray();
		// 33 fonts; the Korean language folder has 32 (it lacks GAMEBOLD9/10/12 and MATISSE18/36 but has the four DATE fonts).
		Assert.AreEqual( name == "Korean" ? 32 : 33, fonts.Length );
		var expectedGlyphs = name switch { "Korean" => 17270, "Polish" => 329, _ => 248 };
		foreach ( var path in fonts )
		{
			var fileName = Path.GetFileName( path );
			var font = language.LoadFont( fileName );
			// The Polish GAMEBOLD and MATISSE fonts are the exception: 262 glyphs, for the letters of titles that the 329-entry table needs only in part.
			if ( !(name == "Polish" && (fileName.StartsWith( "GAMEBOLD", StringComparison.OrdinalIgnoreCase ) || fileName.StartsWith( "MATISSE", StringComparison.OrdinalIgnoreCase ))) )
				Assert.AreEqual( expectedGlyphs, font.Glyphs.Count, fileName );
			// The Korean DATE fonts draw only Latin, Greek and Cyrillic (a date written in Hangul uses the label font, UiFonts).
			if ( name == "Korean" && fileName.StartsWith( "DATE", StringComparison.OrdinalIgnoreCase ) )
				continue;
			var drawn = font.Glyphs.Where( glyph => glyph.Width > 0 && glyph.Height > 0 ).Select( glyph => glyph.Character ).ToHashSet();
			var glyphs = font.Glyphs.Select( glyph => glyph.Character ).ToHashSet();
			// The Korean SESH and CASH fonts and the console fonts draw Latin only (UiFonts gives such a font's roles to the label font).
			var latinOnly = name == "Korean" && drawn.All( c => c < '가' ) && drawn.Count( c => c > '~' ) < 300 && !drawn.Any( c => c is >= '가' and <= '힣' );
			var text = used.Where( c => c > ' ' && (!latinOnly || c <= '~') && !(name == "Polish" && fileName.StartsWith( "GAMEBOLD", StringComparison.OrdinalIgnoreCase ) && !glyphs.Contains( c )) ).ToArray();
			// A character needs a glyph record; in the Korean fonts, where most records are empty placeholders, it also needs a drawn one.
			var missing = text.Where( c => !(name == "Korean" ? drawn : glyphs).Contains( c ) ).OrderBy( c => c ).ToArray();
			Assert.AreEqual( 0, missing.Length, $"{name} {fileName} has no glyph for {string.Concat( missing )}" );
		}
	}

	[DataTestMethod]
	[DynamicData( nameof( Languages ) )]
	public void TheUiFontsLoadInEveryTierAndEveryUiStringLaysOut( string name )
	{
		var language = Open( name );
		var strings = UiStringTable.Load( language );
		foreach ( var tier in Enum.GetValues<UiFontTier>() )
		{
			var fonts = UiFonts.Load( language, tier );
			foreach ( var atlas in fonts.All.Append( fonts.Balance ).Append( fonts.Date ) )
				Assert.IsTrue( atlas.Width <= FontAtlas.MaximumSize && atlas.Glyphs.Count > 150, $"{name} {tier}" );
			var texts = Enum.GetValues<UIStrings>().Select( id => strings[id] ).Where( text => text.Length > 0 ).Concat( SupplementaryStrings.AllTexts( name, language.CharacterTable.Characters.ToHashSet().Contains ) ).ToArray();
			foreach ( var atlas in new[] { fonts.Small, fonts.Label, fonts.Menu } )
				foreach ( var text in texts )
					Assert.AreEqual( 0, TextLayout.Create( atlas, text ).MissingCharacters.Count( c => !char.IsWhiteSpace( c ) ), $"{name} {tier}: {text}" );
			// The date shows in a font that draws every character of the date format in the language.
			var date = string.Format( strings.Extra( OpenTpwText.DateFormat ), 12, 6, 3 );
			Assert.AreEqual( 0, TextLayout.Create( fonts.Date, date ).MissingCharacters.Count, $"{name} {tier} date {date}" );
			Assert.IsTrue( TextLayout.Create( fonts.Date, date ).Glyphs.Count >= date.Count( char.IsLetterOrDigit ), $"{name} {tier} date {date} is drawn" );
		}
	}

	[DataTestMethod]
	[DynamicData( nameof( Languages ) )]
	public void LanguageSpeechLipsAndBannersResolve( string name )
	{
		var language = Open( name );
		Assert.IsNotNull( language.ResolveDataFile( "global/speech/lips.wad" ) );
		Assert.IsNotNull( language.ResolveDataFile( "global/speech/speechHD.sdt" ) );
		foreach ( var level in new[] { "jungle", "fantasy", "hallow", "space" } )
		{
			var lip = language.ResolveDataFile( $"levels/{level}/speech/lips/sp_001.lip" );
			Assert.IsNotNull( lip, $"{name} {level} sp_001.lip" );
			using var stream = File.OpenRead( lip! );
			Assert.IsTrue( new LipSyncFile( stream ).Marks.Count > 0, lip );
		}
		foreach ( var banner in new[] { "bankrupt", "congrats", "paused" } )
			Assert.IsNotNull( language.FindFile( banner + ".md2" ), $"{name} {banner}.MD2" );

		// The advisor's lines are language specific: the global lips.WAD holds one lip-sync file per line (637 of them), the speech bank one sound each.
		using var lipsStream = File.OpenRead( language.ResolveDataFile( "global/speech/lips.wad" )! );
		var lips = new WadArchive( lipsStream );
		var names = lips.GetFiles( "" ).ToArray();
		Assert.AreEqual( 637, names.Length );
		var marks = names.Sum( file => new LipSyncFile( new MemoryStream( lips.GetFile( file ).GetData() ) ).Marks.Count );
		Assert.AreEqual( name switch { "Spanish" => 2771, "Korean" => 4423, _ => 2881 }, marks, $"{name} lip-sync marks" );
		using var speech = new SdtArchive( File.OpenRead( language.ResolveDataFile( "global/speech/speechHD.sdt" )! ) );
		Assert.IsTrue( speech.GetFiles( "" ).Count() is >= 637 and <= 641 );
	}

	[DataTestMethod]
	[DataRow( "Spanish", "OPENTPW_ES_PATH", "Spanish" )]
	[DataRow( "Korean", "OPENTPW_KR_PATH", "Korean" )]
	public void EditionDetectionRecognisesTheCdByItsLanguage( string name, string variable, string expected )
	{
		var data = CdData( variable );
		var report = GameEditions.Detect( data );
		Assert.AreEqual( GameEdition.ThemeParkWorld, report.Edition );
		Assert.AreEqual( (expected, GameLanguageEvidence.AutorunTre), (report.CdLanguage, report.CdLanguageEvidence), name );
		Assert.IsTrue( report.LocaleLanguages.Contains( expected ), string.Join( ", ", report.LocaleLanguages ) );
		if ( name == "Korean" )
			CollectionAssert.IsSubsetOf( new[] { "Chinese (Traditional)", "Chinese (Simplified)", "Danish", "German", "French", "Swedish" }, report.LocaleLanguages.ToArray(), "the Slim Pack CD carries the other locales too" );
		// As the game folder: the CD's own language is the default, English stays choosable, and the installation check lists both.
		Assert.AreEqual( expected, GameLanguage.Resolve( data, null, null ).Name );
		CollectionAssert.AreEquivalent( new[] { "English", expected }, GameLanguage.FindLanguages( data, null ).ToArray() );
		CollectionAssert.AreEquivalent( new[] { "English", expected }, GameLanguage.Readable( data, null ).ToArray() );
		var installation = GameInstallation.Inspect( Path.GetDirectoryName( data )! );
		Assert.IsTrue( installation.IsUsable, string.Join( "; ", installation.Problems ) );
		CollectionAssert.AreEquivalent( new[] { "English", expected }, installation.Languages.ToArray() );
		Assert.AreEqual( expected, installation.CdLanguage );
	}

	[DataTestMethod]
	[DataRow( "Spanish", "OPENTPW_ES_PATH" )]
	[DataRow( "Korean", "OPENTPW_KR_PATH" )]
	public void TheCdAlsoServesAsALanguageOverlayOverAnotherGame( string name, string variable )
	{
		var cd = RequireFolder( variable );
		var game = Environment.GetEnvironmentVariable( "OPENTPW_GAME_PATH" );
		if ( string.IsNullOrWhiteSpace( game ) )
			Assert.Inconclusive( "Set OPENTPW_GAME_PATH for the installed game the overlay goes over." );
		var gameData = StringTableTests.OriginalDataDirectory();
		// The CD's root and its Data folder both work as the overlay; the CD's English is not offered (it is the installed game's own).
		foreach ( var overlay in new[] { cd, CdData( variable ) } )
		{
			Assert.IsTrue( GameLanguage.FindLanguages( gameData, overlay ).Contains( name ), overlay );
			CollectionAssert.Contains( GameLanguage.Readable( gameData, overlay ).ToArray(), name, overlay );
			var language = GameLanguage.Resolve( gameData, name, overlay );
			Assert.AreEqual( name, language.Name );
			Assert.IsNotNull( language.OverlayDataDirectory );
			Assert.AreEqual( Open( name ).LoadStrings( "UITEXT.str" )[(int)UIStrings.GoOnline], language.LoadStrings( "UITEXT.str" )[(int)UIStrings.GoOnline] );
			Assert.IsNotNull( language.ResolveDataFile( "global/speech/speechHD.sdt" )?.StartsWith( language.OverlayDataDirectory!, StringComparison.Ordinal ) );
			Assert.IsNull( GameLanguage.Resolve( gameData, "English", overlay ).OverlayDataDirectory, "English stays the installed game's own" );
		}
	}

	[TestMethod]
	public void ThePolishCdIsAnOverlayLanguageNowThatItsTableIsRead()
	{
		var overlay = RequireFolder( "OPENTPW_PL_PATH" );
		var game = GameLanguage.FindEntry( overlay, "Data", true ) ?? Path.Combine( Path.GetTempPath(), "opentpw-no-game-data" );
		CollectionAssert.Contains( GameLanguage.Readable( game, overlay ).ToArray(), "Polish" );
		Assert.AreEqual( "Polish", GameLanguage.Resolve( game, "polish", overlay ).Name );
	}

	// ---- Korean signs: every sign of the Korean CD names gulim.ttc ----

	[TestMethod]
	public void TheKoreanFontsWadHoldsGulimAndEverySignNamesIt()
	{
		var data = CdData( "OPENTPW_KR_PATH" );
		using var stream = File.OpenRead( Path.Combine( data, "fonts.wad" ) );
		var library = SignFontLibrary.Load( stream );
		Assert.AreEqual( 0, library.Diagnostics.Count, string.Join( "; ", library.Diagnostics ) );
		Assert.AreEqual( SignFontLibrary.ShippedFontCount + 1, library.FileNames.Count, "the 17 shipped fonts and gulim.ttc" );
		var members = library.Collection( "GULIM.TTC" );
		CollectionAssert.AreEqual( new[] { "Gulim", "GulimChe", "Dotum", "DotumChe" }, members.Select( member => member.FamilyName ).ToArray() );
		CollectionAssert.AreEqual( new[] { "굴림", "굴림체", "돋움", "돋움체" }, members.Select( member => member.FamilyNames.Last() ).ToArray() );
		Assert.IsTrue( members.All( member => member.HasGlyph( '한' ) && member.HasGlyph( '글' ) && member.HasGlyph( 'A' ) && member.GlyphCount > 20000 ) );

		var seen = 0;
		var faces = new SortedSet<string>();
		foreach ( var wadPath in Directory.EnumerateFiles( Path.Combine( data, "levels" ), "*.wad", SearchOption.AllDirectories ) )
		{
			using var wadStream = File.OpenRead( wadPath );
			var wad = new WadArchive( wadStream );
			foreach ( var member in wad.GetFiles( "" ).Where( file => file.EndsWith( ".sgn", StringComparison.OrdinalIgnoreCase ) ) )
			{
				var sign = new SignFile( wad.GetFile( member ).GetData() );
				seen++;
				foreach ( var slot in sign.Slots )
				{
					faces.Add( $"{slot.FontFileName}/{slot.FaceName}" );
					var font = library.Find( slot.FontFileName, slot.FaceName );
					Assert.IsNotNull( font, $"{wadPath}:{member} {slot.FontFileName}" );
					if ( slot.FontFileName.EndsWith( ".ttc", StringComparison.OrdinalIgnoreCase ) )
						Assert.IsTrue( members.Contains( font ) && (slot.FaceName == "±¼¸²" ? font == members[0] : font == members[1]), $"{slot.FaceName}" );
				}
			}
		}
		Assert.IsTrue( seen >= 75, $"{seen} signs" );
		CollectionAssert.IsSubsetOf( new[] { "gulim.ttc/±¼¸²", "gulim.ttc/±¼¸²Ã¼" }, faces.ToArray() );
	}

	[TestMethod]
	public void AKoreanSignRendersHangulText()
	{
		var data = CdData( "OPENTPW_KR_PATH" );
		using var fontStream = File.OpenRead( Path.Combine( data, "fonts.wad" ) );
		var library = SignFontLibrary.Load( fontStream );
		using var wadStream = File.OpenRead( Path.Combine( data, "levels", "jungle", "features", "gates.wad" ) );
		var wad = new WadArchive( wadStream );
		var sign = new SignFile( wad.GetFile( wad.GetFiles( "" ).First( file => file.EndsWith( ".sgn", StringComparison.OrdinalIgnoreCase ) ) ).GetData() );
		Assert.AreEqual( "gulim.ttc", sign.Slots[0].FontFileName );
		var diagnostics = new List<string>();
		var composed = SignSurfaceComposer.Compose( sign, library, new string?[] { "잃어버린 왕국", "테마 파크" }, diagnostics );
		var (left, right, pixels) = (composed.Left, composed.Right, composed.TextPixelCount);
		Assert.IsTrue( pixels > 1000, $"{pixels} text pixels" );
		Assert.IsTrue( left.Concat( right ).Any( value => value != 0 ) );
		Assert.IsFalse( diagnostics.Any( line => line.Contains( "missing", StringComparison.OrdinalIgnoreCase ) || line.Contains( "notdef", StringComparison.OrdinalIgnoreCase ) ), string.Join( "; ", diagnostics ) );
		// Every Hangul syllable of the theme names and the object names is a glyph of the face a sign names.
		var language = Open( "Korean" );
		var text = new[] { "OBJECT_NAMES.str", "THEMENAMES.str" }.SelectMany( file => language.LoadStrings( file ).Entries ).ToArray();
		foreach ( var member in library.Collection( "gulim.ttc" ).Take( 2 ) )
			Assert.AreEqual( 0, text.SelectMany( entry => SignTextLayout.FindMissing( member, entry ) ).Distinct().Count(), member.FamilyName );
		// The sign's own text differs by what it says: the same sign with Latin text draws a different amount.
		var latin = SignSurfaceComposer.Compose( sign, library, new string?[] { "LOST KINGDOM", "THEME PARK" }, new List<string>() );
		Assert.AreNotEqual( pixels, latin.TextPixelCount );
	}

	[TestMethod]
	public void TheKoreanBitmapFontsPackIntoAtlases()
	{
		var language = Open( "Korean" );
		foreach ( var name in new[] { "GAME8AA.bf4", "GAME12AA.bf4", "TITLEBIG.bf4", "MENUBIG.bf4", "POSTCARD.bf4" } )
		{
			var atlas = new FontAtlas( language.LoadFont( name ) );
			Assert.IsTrue( atlas.Width <= FontAtlas.MaximumSize && atlas.Glyphs.Count > 17000, $"{name}: {atlas.Width}x{atlas.Height}, {atlas.Glyphs.Count} glyphs" );
			var layout = TextLayout.Create( atlas, "한글 공원 Theme Park 123" );
			Assert.AreEqual( 0, layout.MissingCharacters.Count );
			Assert.IsTrue( layout.Glyphs.Count >= 14, name );
		}
	}
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTPW.UI.Original;
using Veldrid;

namespace OpenTPW.Tests;

/// <summary>
/// Synthetic tests (no game data) of what the localisation pack added (docs/LANGUAGES.md): tp.exe's language table, the right-to-left pass of its
/// text library and the Hebrew alignment and keyboard, the loading-screen picture names, how the pack's folders are named (USEnglish holds
/// American, the fan Russian replaces American) and a language's own ui.wad in place of the game's.
/// </summary>
[TestClass]
[DoNotParallelize]
public class LanguagePackTests
{
	// ---- tp.exe's language table (0x4193d0 names, 0x5c0d00 registry switch) ----

	[TestMethod]
	public void TheOriginalLanguageTableMapsRegistryValuesToFolders()
	{
		CollectionAssert.AreEqual( new[] { "English", "French", "German", "Spanish", "Swedish", "Italian", "Dutch", "Polish", "Danish", "Japanese",
			"TraditionalChinese", "SimplifiedChinese", "Thai", "Korean", "Hebrew", "American" }, GameLanguage.OriginalLanguageTable.Select( entry => entry.Name ).ToArray() );
		// The registry values of the localisation pack's .reg files: full ids for some languages, primary language ids for others.
		var packValues = new Dictionary<int, string>
		{
			[0x809] = "English", [0x409] = "American", [0x40C] = "French", [0x07] = "German", [0x0A] = "Spanish", [0x1D] = "Swedish", [0x10] = "Italian",
			[0x13] = "Dutch", [0x06] = "Danish", [0x412] = "Korean", [0x40D] = "Hebrew", [0x404] = "TraditionalChinese",
		};
		foreach ( var (id, name) in packValues )
			Assert.AreEqual( name, GameLanguage.OriginalLanguageName( id ), $"0x{id:X}" );
		Assert.AreEqual( "Polish", GameLanguage.OriginalLanguageName( 0x15 ) );
		Assert.AreEqual( "Japanese", GameLanguage.OriginalLanguageName( 0x11 ) );
		Assert.AreEqual( "SimplifiedChinese", GameLanguage.OriginalLanguageName( 0x804 ) );
		Assert.AreEqual( "Thai", GameLanguage.OriginalLanguageName( 0x41E ) );
		// Any other value (0x419 Russian, the full 0x410 Italian id) selects English: the switch stores 0x809 and its lowest bit is English's.
		Assert.AreEqual( "English", GameLanguage.OriginalLanguageName( 0x419 ) );
		Assert.AreEqual( "English", GameLanguage.OriginalLanguageName( 0x410 ) );
		Assert.AreEqual( 0x40D, GameLanguage.OriginalLanguageId( "hebrew" ) );
		Assert.IsNull( GameLanguage.OriginalLanguageId( "Russian" ), "the fan translation has no entry of its own" );
	}

	[TestMethod]
	public void LanguageNamesIncludeThePackLanguages()
	{
		Assert.AreEqual( "American", GameLanguage.CanonicalName( "american" ) );
		Assert.AreEqual( "American", GameLanguage.CanonicalName( "USEnglish" ) );
		Assert.AreEqual( "American", GameLanguage.CanonicalName( "English (US)" ) );
		Assert.AreEqual( "Hebrew", GameLanguage.CanonicalName( "HEBREW" ) );
		Assert.AreEqual( "Italian", GameLanguage.CanonicalName( "italian" ) );
		Assert.AreEqual( "Russian", GameLanguage.CanonicalName( "russian" ) );
		Assert.IsTrue( GameLanguage.IsFanTranslationName( "Russian" ) );
		Assert.IsFalse( GameLanguage.IsFanTranslationName( "American" ) );
	}

	// ---- The right-to-left pass (0x6ae4b0) ----

	[DataTestMethod]
	// pure Hebrew: the line reversed ("Go online")
	[DataRow( "התחבר לרשת", "תשרל רבחתה" )]
	// a Latin word keeps its own order, the colon stays at the end of its Hebrew word
	[DataRow( "שגיאה: בדיקת התקנת DirectX נכשלה", "הלשכנ DirectX תנקתה תקידב :האיגש" )]
	// two Latin words trade places: words are not regrouped
	[DataRow( "לא ניתן להפעיל את Direct Draw", "Draw Direct תא ליעפהל ןתינ אל" )]
	// a Latin word in parentheses is a word without Hebrew: reversed back whole
	[DataRow( "הצג את כל הצוות (S)", "(S) תווצה לכ תא גצה" )]
	// a Hebrew word in parentheses: after the reversal it starts with ')', which is swapped with its partner
	[DataRow( "פארק (חדש)", "(שדח) קראפ" )]
	// numbers read left to right; a comma separates words like a space
	[DataRow( "יום 12,חודש 3", "3 שדוח,12 םוי" )]
	// trailing spaces of a line that is not wrapped move to its start
	[DataRow( "טען ", " ןעט" )]
	public void HebrewLinesAreDrawnInTheOriginalsVisualOrder( string logical, string visual ) =>
		Assert.AreEqual( visual, TextVisualOrder.Reorder( logical ) );

	[DataTestMethod]
	[DataRow( "Go Online" )]
	[DataRow( "Kontrollér (V) 1,5" )]
	[DataRow( "한국어 (S)" )]
	[DataRow( "" )]
	public void LinesWithoutHebrewAreLeftAlone( string text ) => Assert.AreEqual( text, TextVisualOrder.Reorder( text ) );

	[TestMethod]
	public void OnlyTheLettersAndLaterPointsTriggerThePass()
	{
		// 0x6ae410 compares above 0x5b0 and below 0x5eb: sheva (U+05B0) and the ligatures (U+05F0...) do not count
		Assert.IsFalse( TextVisualOrder.IsHebrew( 'ְ' ) );
		Assert.IsTrue( TextVisualOrder.IsHebrew( 'ֱ' ) );
		Assert.IsTrue( TextVisualOrder.IsHebrew( 'א' ) );
		Assert.IsTrue( TextVisualOrder.IsHebrew( 'ת' ) );
		Assert.IsFalse( TextVisualOrder.IsHebrew( 'װ' ) );
		Assert.AreEqual( "abְ", TextVisualOrder.Reorder( "abְ" ) );
	}

	private static FontAtlas HebrewAtlas() => new( FontAtlasTests.CreateFont( 10,
		new FontAtlasTests.SyntheticGlyph( 'א', 4, 5, 0, 2, 5 ),
		new FontAtlasTests.SyntheticGlyph( 'ב', 4, 5, 0, 2, 6 ),
		new FontAtlasTests.SyntheticGlyph( 'ג', 3, 5, 0, 2, 4 ),
		new FontAtlasTests.SyntheticGlyph( 'A', 4, 5, 0, 2, 5 ),
		new FontAtlasTests.SyntheticGlyph( 'B', 4, 5, 0, 2, 5 ),
		new FontAtlasTests.SyntheticGlyph( '?', 2, 5, 0, 2, 3 ),
		new FontAtlasTests.SyntheticGlyph( ' ', 0, 0, 0, 0, 3 ) ) );

	[TestMethod]
	public void TheLayoutPlacesHebrewGlyphsFromRightToLeftAndKeepsTheLineWidth()
	{
		var atlas = HebrewAtlas();
		var logical = TextLayout.Create( atlas, "אבג AB" );
		var latin = TextLayout.Create( atlas, "AB AB" );
		// visual order "AB גבא": the Latin word first, then gimel, bet, aleph
		Assert.AreEqual( "ABגבא", string.Concat( logical.Glyphs.OrderBy( glyph => glyph.X ).Select( glyph => glyph.Character ) ) );
		Assert.AreEqual( 5 + 6 + 4 + 3 + 5 + 5, logical.Width );
		Assert.AreEqual( "ABAB", string.Concat( latin.Glyphs.OrderBy( glyph => glyph.X ).Select( glyph => glyph.Character ) ) );
		// each wrapped line is reordered on its own: "אבג" then "בא"
		var wrapped = TextLayout.Create( atlas, "אבג בא", 16 );
		Assert.AreEqual( 2, wrapped.LineCount );
		Assert.AreEqual( "גבא", string.Concat( wrapped.Glyphs.Where( glyph => glyph.Line == 0 ).OrderBy( glyph => glyph.X ).Select( glyph => glyph.Character ) ) );
		Assert.AreEqual( "אב", string.Concat( wrapped.Glyphs.Where( glyph => glyph.Line == 1 ).OrderBy( glyph => glyph.X ).Select( glyph => glyph.Character ) ) );
		// a character without a glyph is classed by itself, not by the fallback glyph that stands in for it
		var missing = TextLayout.Create( atlas, "דA" );
		CollectionAssert.AreEqual( new[] { 'ד' }, missing.MissingCharacters.ToArray() );
		Assert.AreEqual( "A?", string.Concat( missing.Glyphs.OrderBy( glyph => glyph.X ).Select( glyph => glyph.Character ) ) );
	}

	[TestMethod]
	public void HebrewSwapsLeftAndRightAlignment()
	{
		var models = new UiModels( name => throw new FileNotFoundException( name ) );
		var hebrew = new UiContext( OriginalUiTests.FakeStrings( "Hebrew" ), null!, models ) { Canvas = new UiCanvas( 1024, 768 ) };
		var english = new UiContext( OriginalUiTests.FakeStrings(), null!, models ) { Canvas = new UiCanvas( 1024, 768 ) };
		Assert.IsTrue( hebrew.RightToLeft );
		Assert.IsFalse( english.RightToLeft );
		Assert.AreEqual( UiAlign.Right, hebrew.Drawn( UiAlign.Left ) );
		Assert.AreEqual( UiAlign.Left, hebrew.Drawn( UiAlign.Right ) );
		Assert.AreEqual( UiAlign.Center, hebrew.Drawn( UiAlign.Center ) );
		Assert.AreEqual( UiAlign.Left, english.Drawn( UiAlign.Left ) );
		var atlas = HebrewAtlas();
		var rect = new UiRect( 100, 100, 400, 40 );
		hebrew.DrawText( atlas, "אבג", rect, RgbaByte.White, UiAlign.Left );
		english.DrawText( atlas, "אבג", rect, RgbaByte.White, UiAlign.Left );
		Assert.IsTrue( hebrew.Batch.Glyphs.Min( glyph => glyph.X ) > 400, "a left-aligned label is drawn at the right in Hebrew" );
		Assert.IsTrue( english.Batch.Glyphs.Max( glyph => glyph.X ) < 200 );
	}

	// ---- The Hebrew keyboard (0x46b460) ----

	[TestMethod]
	public void TheHebrewKeyboardIsTheOriginalsTable()
	{
		var expected = new Dictionary<char, int>
		{
			['\''] = 0x2C, [','] = 0x5EA, ['.'] = 0x5E5, ['/'] = 0x2E, [';'] = 0x5E3, ['`'] = 0x3B, ['a'] = 0x5E9, ['b'] = 0x5E0, ['c'] = 0x5D1, ['d'] = 0x5D2,
			['e'] = 0x5E7, ['f'] = 0x5DB, ['g'] = 0x5E2, ['h'] = 0x5D9, ['i'] = 0x5DF, ['j'] = 0x5D7, ['k'] = 0x5DC, ['l'] = 0x5DA, ['m'] = 0x5E6, ['n'] = 0x5DE,
			['o'] = 0x5DD, ['p'] = 0x5E4, ['q'] = 0x2F, ['r'] = 0x5E8, ['s'] = 0x5D3, ['t'] = 0x5D0, ['u'] = 0x5D5, ['v'] = 0x5D4, ['w'] = 0x27, ['x'] = 0x5E1,
			['y'] = 0x5D8, ['z'] = 0x5D6,
		};
		foreach ( var (key, value) in expected )
			Assert.AreEqual( value, (int)HebrewKeyboard.Map( key ), $"'{key}'" );
		foreach ( var unchanged in "AZ09 -=[]\\\b\r" )
			Assert.AreEqual( unchanged, HebrewKeyboard.Map( unchanged ) );
		Assert.AreEqual( "שלום", HebrewKeyboard.Map( "akuo" ) );
	}

	// ---- The loading-screen pictures (0x587380) ----

	[TestMethod]
	public void InitPicturesFollowTheScreenWidthAndTheTableName()
	{
		Assert.AreEqual( "init/640/splash_American.tga", GameLanguage.InitImagePath( GameLanguage.InitImage.Splash, 640, "American" ) );
		Assert.AreEqual( "init/1024/legal_Hebrew.tga", GameLanguage.InitImagePath( GameLanguage.InitImage.Legal, 1024, "Hebrew" ) );
		Assert.AreEqual( "init/400/welcome_Dutch.tga", GameLanguage.InitImagePath( GameLanguage.InitImage.Welcome, 400, "Dutch" ) );
		Assert.AreEqual( "init/512/welcome.tga", GameLanguage.InitImagePath( GameLanguage.InitImage.Background, 512, "Dutch" ) );
		// any other width uses the 640 folder
		Assert.AreEqual( "init/640/splash_English.tga", GameLanguage.InitImagePath( GameLanguage.InitImage.Splash, 1920, "English" ) );
		Assert.AreEqual( "init/640/splash_English.tga", GameLanguage.InitImagePath( GameLanguage.InitImage.Splash, 320, "English" ) );
	}

	// ---- The pack's folders ----

	private static string Temporary()
	{
		var path = Path.Combine( Path.GetTempPath(), "opentpw-pack-" + Guid.NewGuid().ToString( "N" ) );
		Directory.CreateDirectory( path );
		return path;
	}

	private static void Language( string folder )
	{
		Directory.CreateDirectory( folder );
		File.WriteAllBytes( Path.Combine( folder, "MBToUni.dat" ), StringTableTests.CreateCharacterTable( "abc" ) );
	}

	[TestMethod]
	public void ThePacksFoldersAreNamedByTheirLanguage()
	{
		var root = Temporary();
		try
		{
			var game = Path.Combine( root, "game", "Data" );
			Language( Path.Combine( game, "Language", "English" ) );
			var pack = Path.Combine( root, "pack" );
			Language( Path.Combine( pack, "USEnglish", "data", "language", "american" ) );
			Language( Path.Combine( pack, "Russian", "data", "language", "American" ) );
			Language( Path.Combine( pack, "Hebrew", "data", "Language", "Hebrew" ) );
			Language( Path.Combine( pack, "Italian", "data", "language", "Italian" ) );
			File.WriteAllText( Path.Combine( pack, "TraditionalChinese.reg" ), "" );

			CollectionAssert.AreEquivalent( new[] { "English", "American", "Hebrew", "Italian", "Russian" }, GameLanguage.Readable( game, pack ).ToArray() );
			var american = GameLanguage.Resolve( game, "american", pack );
			Assert.AreEqual( ("American", "American", 0x409, false, false), (american.Name, american.OriginalName, american.LanguageId, american.IsRightToLeft, american.IsFanTranslation) );
			var russian = GameLanguage.Resolve( game, "Russian", pack );
			Assert.AreEqual( ("Russian", "American", true), (russian.Name, russian.OriginalName, russian.IsFanTranslation) );
			StringAssert.EndsWith( russian.Directory, Path.Combine( "Russian", "data", "language", "American" ) );
			var hebrew = GameLanguage.Resolve( game, "Hebrew", pack );
			Assert.AreEqual( (0x40D, true), (hebrew.LanguageId, hebrew.IsRightToLeft) );
			Assert.IsFalse( GameLanguage.Resolve( game, "English", pack ).IsRightToLeft );

			// One folder of the pack, or its data folder, offers its own language under its own name.
			foreach ( var (folder, name) in new[] { ("USEnglish", "American"), ("Russian", "Russian"), ("Hebrew", "Hebrew") } )
			{
				foreach ( var overlay in new[] { Path.Combine( pack, folder ), Path.Combine( pack, folder, "data" ) } )
				{
					CollectionAssert.Contains( GameLanguage.FindLanguages( game, overlay ).ToArray(), name, overlay );
					Assert.AreEqual( name, GameLanguage.Resolve( game, null, overlay ).Name, $"{overlay} selects its only language" );
				}
			}
		}
		finally
		{
			Directory.Delete( root, true );
		}
	}

	// ---- A language's own ui.wad ----

	[TestMethod]
	public void ALanguagesOwnInterfaceArchiveReplacesTheGamesAndSwitchesBack()
	{
		var root = Temporary();
		try
		{
			var game = Path.Combine( root, "Data" );
			Language( Path.Combine( game, "Language", "English" ) );
			File.WriteAllBytes( Path.Combine( game, "ui.wad" ), BuildWad( ("b_okay.MD2", Encoding.ASCII.GetBytes( "model" )), ("textures\\tpw_logo.wct", Encoding.ASCII.GetBytes( "UK" )) ) );
			var pack = Path.Combine( root, "pack" );
			Language( Path.Combine( pack, "USEnglish", "data", "language", "american" ) );
			File.WriteAllBytes( Path.Combine( pack, "USEnglish", "data", "ui.wad" ), BuildWad( ("b_okay.MD2", Encoding.ASCII.GetBytes( "model" )), ("textures\\tpw_logo.wct", Encoding.ASCII.GetBytes( "US" )) ) );
			Language( Path.Combine( pack, "Italian", "data", "language", "Italian" ) );

			var files = new BaseFileSystem( game ) { CacheLookups = true };
			files.RegisterArchiveHandler<WadArchive>( ".wad" );
			string Logo() => Encoding.ASCII.GetString( files.ReadAllBytes( "/ui/textures/tpw_logo.wct" ) );
			Assert.AreEqual( "UK", Logo() );

			var american = GameLanguage.Resolve( game, "American", pack );
			StringAssert.EndsWith( american.OwnInterfaceArchive, Path.Combine( "USEnglish", "data", "ui.wad" ) );
			var changed = 0;
			void Count() => changed++;
			LanguageInterfaceArchive.Changed += Count;
			try
			{
				LanguageInterfaceArchive.Apply( files, american );
				Assert.AreEqual( "US", Logo() );
				Assert.AreEqual( "model", Encoding.ASCII.GetString( files.ReadAllBytes( "/ui/b_okay.MD2" ) ) );
				// A language without its own archive (Italian of the pack has none here, English is the game's) goes back to the game's.
				var italian = GameLanguage.Resolve( game, "Italian", pack );
				Assert.IsNull( italian.OwnInterfaceArchive );
				LanguageInterfaceArchive.Apply( files, italian );
				Assert.AreEqual( "UK", Logo() );
				Assert.IsNull( LanguageInterfaceArchive.Current );
				LanguageInterfaceArchive.Apply( files, italian );
				Assert.AreEqual( 2, changed, "applying the same archive again changes nothing" );
			}
			finally
			{
				LanguageInterfaceArchive.Changed -= Count;
			}
		}
		finally
		{
			// Back to no archive of a language's own, as other tests expect.
			LanguageInterfaceArchive.Apply( new BaseFileSystem( Path.Combine( root, "Data" ) ), GameLanguage.Resolve( Path.Combine( root, "Data" ), "English", null ) );
			Directory.Delete( root, true );
		}
	}

	/// <summary>A DWFB archive with uncompressed members (the layout of ParkResumeAssetManifestTests.BuildWad).</summary>
	private static byte[] BuildWad( params (string Name, byte[] Data)[] members )
	{
		using var buffer = new MemoryStream();
		void UInt32( uint value ) => buffer.Write( BitConverter.GetBytes( value ), 0, 4 );
		buffer.Write( Encoding.ASCII.GetBytes( "DWFB" ), 0, 4 );
		UInt32( 1 );
		buffer.Write( new byte[64], 0, 64 );
		UInt32( (uint)members.Length );
		UInt32( 0 );
		UInt32( 0 );
		UInt32( 0 );
		var dataOffset = (uint)buffer.Position + (uint)(members.Length * 40);
		var nameOffset = dataOffset + (uint)members.Sum( member => member.Data.Length );
		for ( var member = 0; member < members.Length; member++ )
		{
			UInt32( 0 );
			UInt32( (uint)(nameOffset + members.Take( member ).Sum( previous => previous.Name.Length + 1 )) );
			UInt32( (uint)(members[member].Name.Length + 1) );
			UInt32( (uint)(dataOffset + members.Take( member ).Sum( previous => previous.Data.Length )) );
			UInt32( (uint)members[member].Data.Length );
			UInt32( 0 );
			UInt32( (uint)members[member].Data.Length );
			buffer.Write( new byte[12], 0, 12 );
		}
		foreach ( var (_, data) in members )
			buffer.Write( data, 0, data.Length );
		foreach ( var (name, _) in members )
		{
			var bytes = Encoding.ASCII.GetBytes( name );
			buffer.Write( bytes, 0, bytes.Length );
			buffer.WriteByte( 0 );
		}
		var wad = buffer.ToArray();
		BitConverter.GetBytes( nameOffset ).CopyTo( wad, 76 );
		BitConverter.GetBytes( (uint)members.Sum( member => member.Name.Length + 1 ) ).CopyTo( wad, 80 );
		return wad;
	}
}

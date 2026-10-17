using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// Synthetic tests (no game data) of the two-byte string tables (Polish and Korean: <see cref="BFMUReader"/>'s lead/trail layout),
/// the Spanish single-byte table, the TrueType collection the Korean CD's signs name, and how CD folders that hold one language
/// (the Spanish and Korean CDs) are recognised.
/// </summary>
[TestClass]
[DoNotParallelize]
public class MultiByteLanguageTests
{
	/// <summary>
	/// A two-byte BFMU table: <paramref name="leads"/> lead bytes ending at 0xFF, the last of which has <paramref name="lastTrails"/> trail
	/// bytes, so 255 - leads single-byte characters, 255 trail bytes for every other lead. Character <c>n</c> (1-based) is U+4E00 + n.
	/// </summary>
	internal static byte[] TwoByteTable( int leads, int lastTrails, out string characters )
	{
		var count = (255 - leads) + (leads - 1) * 255 + lastTrails;
		characters = string.Concat( Enumerable.Range( 1, count ).Select( index => (char)(0x4E00 + index) ) );
		var data = new List<byte> { (byte)'B', (byte)'F', (byte)'M', (byte)'U', (byte)leads, (byte)(leads >> 8), 0xFF, (byte)lastTrails };
		foreach ( var character in characters )
			data.AddRange( BitConverter.GetBytes( character ) );
		return data.ToArray();
	}

	private static BFMUReader Read( byte[] table ) => new( new MemoryStream( table ) );

	[TestMethod]
	public void TwoByteTableLayoutMatchesThePolishAndKoreanCounts()
	{
		// Polish: 1 lead byte (0xFF), 75 trails, 254 + 75 = 329 characters.
		var polish = Read( TwoByteTable( 1, 75, out var polishCharacters ) );
		Assert.AreEqual( 329, polish.Characters.Count );
		Assert.AreEqual( (1, 254, 0xFF, true), (polish.LeadByteCount, polish.SingleByteCount, polish.FirstLeadByte, polish.IsMultiByte) );
		Assert.AreEqual( polishCharacters, string.Concat( polish.Characters ) );
		// Korean: 67 lead bytes (0xBD..0xFF), 252 trails on the last, 188 + 66 * 255 + 252 = 17,270 characters.
		var korean = Read( TwoByteTable( 67, 252, out _ ) );
		Assert.AreEqual( 17270, korean.Characters.Count );
		Assert.AreEqual( (67, 188, 0xBD), (korean.LeadByteCount, korean.SingleByteCount, korean.FirstLeadByte) );
		// A single-byte table (any of the six European languages and Spanish) has no lead byte.
		var single = Read( StringTableTests.CreateCharacterTable( "abc" ) );
		Assert.AreEqual( (0, 3, 256, false), (single.LeadByteCount, single.SingleByteCount, single.FirstLeadByte, single.IsMultiByte) );
		Assert.IsFalse( single.IsLeadByte( 0xFF ) );
	}

	[TestMethod]
	public void LeadAndTrailBytesSelectTheCharacterInTableOrder()
	{
		var table = Read( TwoByteTable( 67, 252, out _ ) );
		char At( int index ) => (char)(0x4E00 + index);
		// Single bytes 1..188 are indices 1..188.
		Assert.AreEqual( 1, table.Decode( new byte[] { 1 }, 0, out var character ) );
		Assert.AreEqual( At( 1 ), character );
		Assert.AreEqual( 1, table.Decode( new byte[] { 188 }, 0, out character ) );
		Assert.AreEqual( At( 188 ), character );
		// Lead 0xBD (the first), trail 1 is index 189; trail 255 is index 188 + 255; lead 0xBE starts at 188 + 255 + 1.
		Assert.AreEqual( 2, table.Decode( new byte[] { 0xBD, 1 }, 0, out character ) );
		Assert.AreEqual( At( 189 ), character );
		table.Decode( new byte[] { 0xBD, 255 }, 0, out character );
		Assert.AreEqual( At( 188 + 255 ), character );
		table.Decode( new byte[] { 0xBE, 1 }, 0, out character );
		Assert.AreEqual( At( 188 + 256 ), character );
		Assert.AreEqual( 188 + 256, table.TwoByteIndex( 0xBE, 1 ) );
		// The last lead (0xFF) has 252 trails: the last character.
		table.Decode( new byte[] { 0xFF, 252 }, 0, out character );
		Assert.AreEqual( At( 17270 ), character );
		// Decoding continues at the byte after a code: one single byte, one pair, one single byte.
		var position = 0;
		var text = new System.Text.StringBuilder();
		var bytes = new byte[] { 5, 0xBD, 1, 7 };
		while ( position < bytes.Length )
		{
			position += table.Decode( bytes, position, out character );
			text.Append( character );
		}
		Assert.AreEqual( $"{At( 5 )}{At( 189 )}{At( 7 )}", text.ToString() );
	}

	[TestMethod]
	public void MalformedTwoByteCodesAreRefused()
	{
		var table = Read( TwoByteTable( 67, 252, out _ ) );
		Assert.ThrowsException<InvalidDataException>( () => table.Decode( new byte[] { 0xBD }, 0, out _ ), "a lead byte at the end of the string has no trail byte" );
		Assert.ThrowsException<InvalidDataException>( () => table.Decode( new byte[] { 0xBD, 0 }, 0, out _ ), "trail byte 0 is not a code" );
		Assert.ThrowsException<InvalidDataException>( () => table.Decode( new byte[] { 0xFF, 253 }, 0, out _ ), "the last lead byte stops at its last trail" );
		Assert.ThrowsException<InvalidDataException>( () => table.Decode( new byte[] { 0 }, 0, out _ ), "byte 0 is not a character" );
		// A header that does not describe a table: more leads than bytes, no trails, or fewer characters than it declares.
		var truncated = TwoByteTable( 67, 252, out _ )[..100];
		Assert.ThrowsException<InvalidDataException>( () => Read( truncated ) );
		var noTrails = TwoByteTable( 3, 5, out _ );
		noTrails[7] = 0;
		Assert.ThrowsException<InvalidDataException>( () => Read( noTrails ) );
		var tooManyLeads = TwoByteTable( 3, 5, out _ );
		tooManyLeads[6] = 2;
		Assert.ThrowsException<InvalidDataException>( () => Read( tooManyLeads ) );
	}

	[TestMethod]
	public void StringTablesDecodeTwoByteCodesAndCountBytesInTheLength()
	{
		var table = Read( TwoByteTable( 67, 252, out _ ) );
		char At( int index ) => (char)(0x4E00 + index);
		// "single, pair, single, pair" is 6 bytes long but 4 characters.
		var data = StringTableTests.CreateStringTable( new byte[] { 12, 0xBD, 1, 14, 0xC0, 9 }, new byte[] { 3 } );
		var file = new StringFile( new MemoryStream( data ), table );
		Assert.AreEqual( $"{At( 12 )}{At( 189 )}{At( 14 )}{At( 188 + 3 * 255 + 9 )}", file[0] );
		Assert.AreEqual( 4, file[0].Length );
		Assert.AreEqual( At( 3 ).ToString(), file[1] );
		// A pair cut off by the record length is an error, not a silent character.
		var cut = StringTableTests.CreateStringTable( new byte[] { 12, 0xBD } );
		Assert.ThrowsException<InvalidDataException>( () => new StringFile( new MemoryStream( cut ), table ) );
	}

	[TestMethod]
	public void TheCodecEncodesTwoByteCodesFromTheLeadAndTrailValuesOfUniToMB()
	{
		var decode = Read( TwoByteTable( 67, 252, out _ ) );
		char At( int index ) => (char)(0x4E00 + index);
		// BFUM entries: a value up to 0xFF is the single-byte index; above, the lead byte is the low byte and the trail byte the high byte.
		var ranges = new List<byte> { (byte)'B', (byte)'F', (byte)'U', (byte)'M' };
		void Range( char first, params ushort[] values )
		{
			ranges.AddRange( BitConverter.GetBytes( (ushort)first ) );
			ranges.AddRange( new byte[2] );
			ranges.AddRange( BitConverter.GetBytes( (ushort)(first + values.Length - 1) ) );
			ranges.AddRange( new byte[2] );
			foreach ( var value in values )
				ranges.AddRange( BitConverter.GetBytes( value ) );
		}
		Range( At( 12 ), 12 );
		Range( At( 189 ), 0x01BD );               // lead 0xBD, trail 1
		Range( At( 188 + 3 * 255 + 9 ), 0x09C0 ); // lead 0xC0, trail 9
		Range( 'x', 0x01BD );                     // a filler: decodes to another character
		var codec = new GameTextCodec( decode, new BFUMReader( new MemoryStream( ranges.ToArray() ) ) );
		Assert.IsTrue( codec.CanEncode( At( 189 ) ) );
		Assert.IsFalse( codec.CanEncode( 'x' ), "filler entries do not round-trip" );
		var text = $"{At( 12 )}{At( 189 )}{At( 188 + 3 * 255 + 9 )}";
		var bytes = codec.Encode( text );
		CollectionAssert.AreEqual( new byte[] { 12, 0xBD, 1, 0xC0, 9 }, bytes );
		Assert.AreEqual( text, codec.Decode( bytes ) );
		Assert.AreEqual( 3, codec.Repertoire.Count );
	}

	[TestMethod]
	public void ASpanishStyleSingleByteTableStillDecodesWithoutLeadBytes()
	{
		// Spanish: the same layout as English, 248 characters (Latin-1 accents and the inverted marks, no typographic apostrophe).
		var table = Read( StringTableTests.CreateCharacterTable( "*1234567890 .,$£-ÿþýüûúùø÷öõôóòñðïîíìëêéèçæåäãâáàßÞÝÜÛÚÙØ×ÖÕÔÓÒÑÐÏÎÍÌËÊÉÈÇÆÅÄÃÂÁÀ¿¾½¼»º¹¸·¶µ´³²±°¯®­¬«ª©¨§¦¥¤¢¡" ) );
		Assert.IsFalse( table.IsMultiByte );
		var text = "¿Seguro que quieres salir? ñ Ñ ¡Sí!";
		var data = StringTableTests.CreateStringTable( text.Select( c => (byte)(table.Characters.ToList().IndexOf( c ) + 1) ).Where( b => b > 0 ).ToArray() );
		var decoded = new StringFile( new MemoryStream( data ), table )[0];
		StringAssert.Contains( decoded, "¿" );
		StringAssert.Contains( decoded, "ñ" );
		StringAssert.Contains( decoded, "Ñ" );
		StringAssert.Contains( decoded, "¡" );
	}

	// ---- CD folders that hold one language ----

	private string root = "";

	[TestInitialize]
	public void CreateRoot()
	{
		root = Path.Combine( Path.GetTempPath(), $"opentpw-onelanguage-{Guid.NewGuid():N}" );
		Directory.CreateDirectory( root );
	}

	[TestCleanup]
	public void DeleteRoot() => Directory.Delete( root, true );

	/// <summary>The layout of the Spanish and Korean CDs: a complete <c>Data</c> with English and the CD's language, <c>&lt;Lang&gt;/Meshes/&lt;Lang&gt;</c>, <c>Autorun/&lt;lang&gt;.tre</c> and locale files.</summary>
	private string CreateSingleLanguageCd( string cd, string language, string folder, params string[] localeFiles )
	{
		var data = Path.Combine( root, cd, "Data" );
		foreach ( var (name, characters) in new[] { ("English", "abc"), (folder, "xyz") } )
		{
			var directory = Path.Combine( data, "Language", name );
			Directory.CreateDirectory( directory );
			File.WriteAllBytes( Path.Combine( directory, "MBToUni.dat" ), StringTableTests.CreateCharacterTable( characters ) );
			File.WriteAllBytes( Path.Combine( directory, "UITEXT.str" ), StringTableTests.CreateStringTable( new byte[] { 1, 2, 3 } ) );
		}
		Directory.CreateDirectory( Path.Combine( data, "levels" ) );
		Directory.CreateDirectory( Path.Combine( data, "global", "Speech" ) );
		File.WriteAllBytes( Path.Combine( data, "global", "Speech", "speechHD.SDT" ), new byte[] { 1 } );
		Directory.CreateDirectory( Path.Combine( root, cd, language, "Meshes", folder ) );
		File.WriteAllBytes( Path.Combine( root, cd, language, "Meshes", folder, "paused.MD2" ), new byte[] { 1 } );
		Directory.CreateDirectory( Path.Combine( root, cd, "Autorun" ) );
		foreach ( var tre in new[] { "autorun.tre", "general.tre", language + ".tre" } )
			File.WriteAllBytes( Path.Combine( root, cd, "Autorun", tre ), new byte[] { 1 } );
		foreach ( var locale in localeFiles )
			File.WriteAllBytes( Path.Combine( root, cd, locale ), new byte[] { 1 } );
		return data;
	}

	[TestMethod]
	public void AGameFolderCopiedFromASingleLanguageCdDefaultsToTheCdsLanguage()
	{
		var data = CreateSingleLanguageCd( "spanish-cd", "Spanish", "spanish", "00000c0a.016", "00000c0a.256" );
		CollectionAssert.AreEqual( new[] { "English", "Spanish" }, GameLanguage.FindLanguages( data, null ).ToArray() );
		var language = GameLanguage.Resolve( data, null, null );
		Assert.AreEqual( "Spanish", language.Name );
		Assert.IsNotNull( language.FindFile( "paused.md2" ), "the banner meshes beside Data" );
		Assert.AreEqual( "English", GameLanguage.Resolve( data, "english", null ).Name, "a chosen language wins" );
		CollectionAssert.AreEqual( new[] { "English", "Spanish" }, GameLanguage.Readable( data, null ).ToArray() );
	}

	[TestMethod]
	public void ASingleLanguageCdOffersOnlyItsOwnLanguageAsAnOverlay()
	{
		var cd = CreateSingleLanguageCd( "korean-cd", "Korean", "korean" );
		var cdData = Path.Combine( cd, "..", "Data" );
		var game = Path.Combine( root, "Game", "Data" );
		Directory.CreateDirectory( Path.Combine( game, "Language", "English" ) );
		File.WriteAllBytes( Path.Combine( game, "Language", "English", "MBToUni.dat" ), StringTableTests.CreateCharacterTable( "abc" ) );
		// The CD's root and its Data folder both serve as the overlay; the English folder that stays in the CD's Data is not offered.
		foreach ( var overlay in new[] { Path.GetFullPath( Path.Combine( cd, ".." ) ), Path.GetFullPath( cd ) } )
		{
			CollectionAssert.AreEqual( new[] { "English", "Korean" }, GameLanguage.FindLanguages( game, overlay ).ToArray(), overlay );
			var korean = GameLanguage.Resolve( game, "korean", overlay );
			Assert.AreEqual( "Korean", korean.Name );
			Assert.AreEqual( Path.GetFullPath( cd ), korean.OverlayDataDirectory, "its speech comes from the CD's Data" );
			Assert.IsNotNull( korean.FindFile( "paused.md2" ), $"the CD's banner meshes ({overlay})" );
			Assert.IsNotNull( korean.ResolveDataFile( "global/speech/speechHD.sdt" ) );
			Assert.IsNull( GameLanguage.Resolve( game, "English", overlay ).OverlayDataDirectory, "English stays the installed game's own" );
		}
		_ = cdData;
	}

	[TestMethod]
	public void EditionDetectionNamesTheLanguageOfASingleLanguageCd()
	{
		var spanish = CreateSingleLanguageCd( "spanish-cd", "Spanish", "spanish", "00000c0a.016", "00000c0a.256" );
		var report = GameEditions.Detect( spanish );
		Assert.AreEqual( (GameEdition.ThemeParkWorld, "Spanish", GameLanguageEvidence.AutorunTre), (report.Edition, report.CdLanguage, report.CdLanguageEvidence) );
		CollectionAssert.AreEqual( new[] { "Spanish" }, report.LocaleLanguages.ToArray() );

		// The Korean Slim Pack CD: one language tre, and locale files for five more languages and two Chinese ones.
		var korean = CreateSingleLanguageCd( "korean-cd", "Korean", "korean", "00000404.016", "00000406.016", "00000407.016", "0000040c.016", "00000412.016", "0000041d.016", "00000809.016", "00001004.016" );
		report = GameEditions.Detect( korean );
		Assert.AreEqual( ("Korean", GameLanguageEvidence.AutorunTre), (report.CdLanguage, report.CdLanguageEvidence) );
		CollectionAssert.AreEqual( new[] { "Chinese (Traditional)", "Danish", "German", "French", "Korean", "Swedish", "English", "Chinese (Simplified)" }, report.LocaleLanguages.ToArray() );

		// Without the autorun cabinet names the language folder beside Data is the evidence, then the only non-English locale file.
		File.Delete( Path.Combine( root, "korean-cd", "Autorun", "Korean.tre" ) );
		Assert.AreEqual( ("Korean", GameLanguageEvidence.LanguageFolder), (GameEditions.Detect( korean ).CdLanguage, GameEditions.Detect( korean ).CdLanguageEvidence) );
		Directory.Delete( Path.Combine( root, "spanish-cd", "Spanish" ), true );
		File.Delete( Path.Combine( root, "spanish-cd", "Autorun", "Spanish.tre" ) );
		report = GameEditions.Detect( spanish );
		Assert.AreEqual( ("Spanish", GameLanguageEvidence.LocaleFile), (report.CdLanguage, report.CdLanguageEvidence) );

		// An installed game (no marks beside Data) has no CD language, and neither does a folder that cannot be read.
		var installed = Path.Combine( root, "Installed", "Data" );
		Directory.CreateDirectory( Path.Combine( installed, "Language", "English" ) );
		Assert.AreEqual( (null, GameLanguageEvidence.None), (GameEditions.Detect( installed ).CdLanguage, GameEditions.Detect( installed ).CdLanguageEvidence) );
		Assert.IsNull( GameEditions.Detect( Path.Combine( root, "missing", "Data" ) ).CdLanguage );
	}

	// ---- The Korean CD's signs: gulim.ttc ----

	[TestMethod]
	public void ATrueTypeCollectionParsesEveryMemberAndSignFacesPickTheMember()
	{
		var gulim = new TrueTypeFontTests.FontBuilder();
		gulim.AddSimple( 600, 'A', TrueTypeFontTests.Square( 0, 0, 500, 500 ) );
		gulim.AddSimple( 1000, '한', TrueTypeFontTests.Square( 0, 0, 900, 900 ) );
		gulim.AddFamily( 0x0409, "Gulim" );
		gulim.AddFamily( 0x0412, "굴림" );
		var gulimChe = new TrueTypeFontTests.FontBuilder();
		gulimChe.AddSimple( 500, 'A', TrueTypeFontTests.Square( 0, 0, 400, 400 ) );
		gulimChe.AddFamily( 0x0409, "GulimChe" );
		gulimChe.AddFamily( 0x0412, "굴림체" );
		var bytes = TrueTypeFontTests.Collection( gulim, gulimChe );

		Assert.IsTrue( TrueTypeFont.IsCollection( bytes ) );
		var fonts = TrueTypeFont.ParseCollection( bytes, "gulim.ttc" );
		Assert.AreEqual( 2, fonts.Count );
		CollectionAssert.AreEqual( new[] { "Gulim", "굴림" }, fonts[0].FamilyNames.ToArray() );
		Assert.AreEqual( 600, fonts[0].GetMetrics( fonts[0].GetGlyphIndex( 'A' ) ).AdvanceWidth );
		Assert.AreEqual( 500, fonts[1].GetMetrics( fonts[1].GetGlyphIndex( 'A' ) ).AdvanceWidth );
		Assert.IsTrue( fonts[0].HasGlyph( '한' ) );
		Assert.IsFalse( fonts[1].HasGlyph( '한' ) );
		Assert.ThrowsException<InvalidDataException>( () => TrueTypeFont.Parse( bytes, "plain" ), "a collection is not a plain font" );
		Assert.ThrowsException<InvalidDataException>( () => TrueTypeFont.ParseCollection( bytes[..20] ) );
		Assert.AreEqual( 1, TrueTypeFont.ParseCollection( TrueTypeFontTests.Collection( gulim )[..^0], "one" ).Count );

		var library = SignFontLibrary.FromFiles( new[] { ("GULIM.TTC", bytes) } );
		Assert.AreEqual( 2, library.Collection( "gulim.ttc" ).Count );
		// A sign's LOGFONT face is the code page 949 bytes of the Korean family name, read as Latin-1: "굴림" is B1 BC B8 B2.
		Assert.AreSame( library.Collection( "gulim.ttc" )[0], library.Find( "gulim.ttc", "±¼¸²" ) );
		Assert.AreSame( library.Collection( "gulim.ttc" )[1], library.Find( "gulim.ttc", "±¼¸²Ã¼" ), "굴림체 is GulimChe" );
		Assert.AreSame( library.Collection( "gulim.ttc" )[1], library.Find( "GULIM.TTC", "GulimChe" ) );
		Assert.AreSame( library.Collection( "gulim.ttc" )[0], library.Find( "gulim.ttc", "Unknown" ), "an unknown face keeps the first member" );
		Assert.AreSame( library.Find( "gulim.ttc" ), library.Collection( "gulim.ttc" )[0] );
		Assert.AreEqual( 0, library.Collection( "none.ttf" ).Count );

		// Plain .ttf files keep the old lookup: by file name, then by face name.
		var plain = new TrueTypeFontTests.FontBuilder();
		plain.AddSimple( 600, 'A', TrueTypeFontTests.Square( 0, 0, 500, 500 ) );
		plain.AddFamily( 0x0409, "Young Itch AOE" );
		var mixed = SignFontLibrary.FromFiles( new[] { ("YOUNIA__.TTF", plain.Build()) } );
		Assert.AreSame( mixed.Find( "YOUNIA__.TTF" ), mixed.Find( "YOUNIA__.TTF", "other" ) );
		Assert.AreSame( mixed.Find( "YOUNIA__.TTF" ), mixed.Find( "missing.ttf", "Young Itch AOE" ) );
	}
}

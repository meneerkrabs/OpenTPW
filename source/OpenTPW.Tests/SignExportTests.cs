using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using StbImageSharp;

namespace OpenTPW.Tests;

/// <summary>
/// The debug sign export (<c>--export-signs</c>, <see cref="SignExport"/>): file naming, path containment, the output-directory rules and the index
/// without data; with <c>OPENTPW_GAME_PATH</c> the English export's gate lines and its pixels against the composers the level and the lobby use.
/// </summary>
[TestClass]
[DoNotParallelize]
public class SignExportTests
{
	private const int HalfBytes = SignSurfaceComposer.HalfWidth * SignSurfaceComposer.Height * 4;
	private string root = null!;
	private Logger previousLog = null!;

	[TestInitialize]
	public void Setup()
	{
		previousLog = Log;
		Log = new Logger();
		root = Directory.CreateTempSubdirectory( "opentpw-sign-export-" ).FullName;
	}

	[TestCleanup]
	public void Cleanup()
	{
		Log = previousLog;
		Directory.Delete( root, true );
	}

	private static SignExport.ComposedSign Sign( string theme, string name, string kind = "ride", byte fill = 1, string line1 = "Inca", string line2 = "Totem" ) =>
		new( theme, kind, name, $"/levels/{theme}/rides/{name}", $"/levels/{theme}/rides/{name}/{name}.sgn", 1102, new[] { 28, 29 }, new[] { line1, line2 },
			$"{line1} {line2}", line1, line2, Enumerable.Repeat( fill, HalfBytes ).ToArray(), Enumerable.Repeat( (byte)(fill + 1), HalfBytes ).ToArray() );

	[TestMethod]
	public void FilesAreNamedByThemeObjectAndTexture()
	{
		Assert.AreEqual( "jungle/gates-1.png", SignExport.FileName( "jungle", "gates", 1 ) );
		Assert.AreEqual( "jungle/gates-2.png", SignExport.FileName( "jungle", "gates", 2 ) );
		Assert.AreEqual( "jungle/gates.png", SignExport.FileName( "jungle", "gates", 0 ) );
		Assert.ThrowsException<ArgumentOutOfRangeException>( () => SignExport.FileName( "jungle", "gates", 3 ) );

		var files = SignExport.Plan( new[] { Sign( "jungle", "totem" ), Sign( "space", "megacost" ) } );
		CollectionAssert.AreEqual( new[] { "jungle/totem-1.png", "jungle/totem-2.png", "jungle/totem.png", "space/megacost-1.png", "space/megacost-2.png", "space/megacost.png" },
			files.Select( file => file.RelativePath ).ToArray() );
		CollectionAssert.AreEqual( new[] { 1, 2, 0, 1, 2, 0 }, files.Select( file => file.Half ).ToArray() );
		Assert.IsTrue( files.All( file => file.Height == 128 && file.Width == (file.Half == 0 ? 256 : 128) ) );
		Assert.AreEqual( Convert.ToHexString( SHA256.HashData( Enumerable.Repeat( (byte)1, HalfBytes ).ToArray() ) ).ToLowerInvariant(), files[0].RgbaSha256 );
	}

	[TestMethod]
	public void TakenNamesGetTheirKindAndThenANumber()
	{
		var files = SignExport.Plan( new[] { Sign( "jungle", "gates", "gate" ), Sign( "jungle", "gates", "feature" ), Sign( "jungle", "gates", "feature" ), Sign( "hallow", "gates", "gate" ) } );
		CollectionAssert.AreEqual( new[] { "jungle/gates-1.png", "jungle/gates_feature-1.png", "jungle/gates_feature_2-1.png", "hallow/gates-1.png" },
			files.Where( file => file.Half == 1 ).Select( file => file.RelativePath ).ToArray() );
	}

	[TestMethod]
	public void AnObjectNamedLikeAnotherObjectsFileGetsItsKind()
	{
		// "foo-1" writes foo-1-1.png, foo-1-2.png and foo-1.png; "foo" would write foo-1.png again
		var files = SignExport.Plan( new[] { Sign( "jungle", "foo-1" ), Sign( "jungle", "foo" ) } );
		CollectionAssert.AreEqual( new[] { "jungle/foo-1-1.png", "jungle/foo-1-2.png", "jungle/foo-1.png", "jungle/foo_ride-1.png", "jungle/foo_ride-2.png", "jungle/foo_ride.png" },
			files.Select( file => file.RelativePath ).ToArray() );
		Assert.AreEqual( files.Count, files.Select( file => file.RelativePath ).Distinct( StringComparer.OrdinalIgnoreCase ).Count() );
	}

	[TestMethod]
	public void TheIndexNamesTheLanguageFolderWithoutTheUsersPath()
	{
		var game = Path.Combine( root, "Theme Park World" );
		Assert.AreEqual( "Data/Language/English", SignExport.DescribeLanguageFolder( Path.Combine( game, "Data", "Language", "English" ), game ) );
		Assert.AreEqual( "Language/spanish", SignExport.DescribeLanguageFolder( Path.Combine( root, "es-lang", "Data", "Language", "spanish" ), game ) );
		Assert.AreEqual( "spanish", SignExport.DescribeLanguageFolder( Path.Combine( root, "spanish" ), null ) );
	}

	[TestMethod]
	public void LinksAreResolvedBeforeTheOutputIsCompared()
	{
		var data = Path.Combine( root, "lang", "Data" );
		Directory.CreateDirectory( data );
		var link = Path.Combine( root, "link" );
		Directory.CreateSymbolicLink( link, data );
		Assert.AreEqual( SignExport.ResolveLinks( data ), SignExport.ResolveLinks( link ) );
		Assert.AreEqual( Path.Combine( SignExport.ResolveLinks( data ), "new" ), SignExport.ResolveLinks( Path.Combine( link, "new" ) ) );
		Assert.ThrowsException<IOException>( () => SignExport.PrepareOutputDirectory( Path.Combine( link, "signs" ), new[] { data } ) );
		Assert.ThrowsException<IOException>( () => SignExport.PrepareOutputDirectory( Path.Combine( data, "signs" ), new[] { link } ) );
		Assert.IsFalse( Directory.Exists( Path.Combine( data, "signs" ) ) );
	}

	[TestMethod]
	public void LanguageDataAndTheCdFolderAreProtectedWhenTheLanguageComesFromTheInstallation()
	{
		var data = Path.Combine( root, "game", "Data" );
		Directory.CreateDirectory( Path.Combine( data, "Language", "English" ) );
		var overlay = Path.Combine( root, "es-lang", "Data" );
		Directory.CreateDirectory( Path.Combine( overlay, "Language", "spanish" ) );
		var cd = Path.Combine( root, "cd" );
		Directory.CreateDirectory( cd );
		var link = Path.Combine( root, "overlay-link" );
		Directory.CreateSymbolicLink( link, overlay );
		var previousLanguage = GameLanguage.IsSelected ? GameLanguage.Current : null;
		var previousOverlay = GameLanguage.AvailableOverlay;
		try
		{
			foreach ( var output in new[] { Path.Combine( overlay, "x" ), Path.Combine( link, "x" ), Path.Combine( cd, "x" ) } )
			{
				Assert.ThrowsException<IOException>( () => SignExport.RunCommand( new[] { "--export-signs", "English", output }, data, overlay, cd ), output );
				Assert.AreEqual( "English", GameLanguage.Current.Name, "the exported language comes from the installation" );
				Assert.IsFalse( Directory.Exists( output ), output );
			}
			// the overlay offered beside an installed language is protected without being passed again
			GameLanguage.AvailableOverlay = overlay;
			Assert.ThrowsException<IOException>( () => SignExport.Run( Path.Combine( overlay, "y" ), data ) );
			Assert.AreEqual( 1, Directory.EnumerateFileSystemEntries( overlay ).Count() );
		}
		finally
		{
			GameLanguage.Current = previousLanguage!;
			GameLanguage.AvailableOverlay = previousOverlay;
		}
	}

	[TestMethod]
	public void NamesFromDataCannotLeaveTheOutputDirectory()
	{
		Assert.AreEqual( "_evil", SignExport.SafeSegment( "../evil" ) );
		Assert.AreEqual( "_", SignExport.SafeSegment( ".." ) );
		Assert.AreEqual( "_", SignExport.SafeSegment( "" ) );
		Assert.AreEqual( "a_b_c", SignExport.SafeSegment( "a b\\c" ) );
		Assert.AreEqual( "Jun_isle", SignExport.SafeSegment( "Jun_isle" ) );
		Assert.AreEqual( "_etc_passwd", SignExport.SafeSegment( "/etc/passwd" ) );
		Assert.AreEqual( "lobby/_-1.png", SignExport.Plan( new[] { Sign( "lobby", ".." ) } )[0].RelativePath );

		Assert.AreEqual( Path.Combine( root, "jungle", "gates-1.png" ), SignExport.ResolveInside( root, "jungle/gates-1.png" ) );
		Assert.ThrowsException<ArgumentException>( () => SignExport.ResolveInside( root, "../outside.png" ) );
		Assert.ThrowsException<ArgumentException>( () => SignExport.ResolveInside( root, "jungle/../../outside.png" ) );
		Assert.ThrowsException<ArgumentException>( () => SignExport.ResolveInside( root, Path.Combine( root, "x.png" ) ) );
	}

	[TestMethod]
	public void TheOutputIsNewOrEmptyAndOutsideTheData()
	{
		var data = Path.Combine( root, "game", "Data" );
		Directory.CreateDirectory( data );
		var fresh = Path.Combine( root, "out" );
		Assert.AreEqual( fresh, SignExport.PrepareOutputDirectory( fresh, new[] { data } ) );
		Directory.CreateDirectory( fresh );
		Assert.AreEqual( fresh, SignExport.PrepareOutputDirectory( fresh + Path.DirectorySeparatorChar, new[] { data } ) );
		File.WriteAllText( Path.Combine( fresh, "keep.txt" ), "x" );
		Assert.ThrowsException<IOException>( () => SignExport.PrepareOutputDirectory( fresh, new[] { data } ) );
		Assert.ThrowsException<IOException>( () => SignExport.PrepareOutputDirectory( Path.Combine( fresh, "keep.txt" ), Array.Empty<string>() ) );
		Assert.ThrowsException<IOException>( () => SignExport.PrepareOutputDirectory( Path.Combine( data, "signs" ), new[] { data } ) );
		Assert.ThrowsException<IOException>( () => SignExport.PrepareOutputDirectory( data, new[] { data } ) );
		Assert.ThrowsException<IOException>( () => SignExport.PrepareOutputDirectory( Path.Combine( root, "game" ), new[] { data } ) );
		Assert.AreEqual( "x", File.ReadAllText( Path.Combine( fresh, "keep.txt" ) ) );
	}

	[TestMethod]
	public void ThePreviewPutsTheTwoTexturesSideBySide()
	{
		var left = new byte[HalfBytes];
		var right = new byte[HalfBytes];
		left[0] = 10; // row 0, first pixel of sign1
		right[0] = 20; // row 0, first pixel of sign2
		right[HalfBytes - 1] = 30; // last row, last pixel of sign2
		var whole = SignExport.SideBySide( left, right );
		Assert.AreEqual( HalfBytes * 2, whole.Length );
		Assert.AreEqual( 10, whole[0] );
		Assert.AreEqual( 20, whole[SignSurfaceComposer.HalfWidth * 4] );
		Assert.AreEqual( 30, whole[^1] );
		Assert.ThrowsException<InvalidDataException>( () => SignExport.SideBySide( new byte[4], right ) );
	}

	[TestMethod]
	public void TheIndexListsSourceStringsTextAndFile()
	{
		var files = SignExport.Plan( new[] { Sign( "jungle", "gates", "gate", line1: "El Reino", line2: "Perdido" ) with { StringIds = new[] { 85, 86 }, StringTexts = new[] { "El Reino", "Perdido" } } } );
		using var json = JsonDocument.Parse( SignExport.IndexJson( "Spanish", "/data/Language/spanish", files, "opentpw-compat/1;profile=Original;fixes=;simulation=" ) );
		var index = json.RootElement;
		Assert.AreEqual( "Spanish", index.GetProperty( "language" ).GetString() );
		Assert.AreEqual( "OBJECT_NAMES.str", index.GetProperty( "stringTable" ).GetString() );
		Assert.AreEqual( 3, index.GetProperty( "count" ).GetInt32() );
		var first = index.GetProperty( "files" )[0];
		Assert.AreEqual( "jungle", first.GetProperty( "theme" ).GetString() );
		Assert.AreEqual( "gate", first.GetProperty( "kind" ).GetString() );
		Assert.AreEqual( "gates", first.GetProperty( "object" ).GetString() );
		Assert.AreEqual( "/levels/jungle/rides/gates/gates.sgn", first.GetProperty( "sgn" ).GetString() );
		CollectionAssert.AreEqual( new[] { 85, 86 }, first.GetProperty( "stringIds" ).EnumerateArray().Select( id => id.GetInt32() ).ToArray() );
		CollectionAssert.AreEqual( new[] { "El Reino", "Perdido" }, first.GetProperty( "strings" ).EnumerateArray().Select( text => text.GetString() ).ToArray() );
		Assert.AreEqual( "El Reino", first.GetProperty( "line1" ).GetString() );
		Assert.AreEqual( "Perdido", first.GetProperty( "line2" ).GetString() );
		Assert.AreEqual( "sign1", first.GetProperty( "texture" ).GetString() );
		Assert.AreEqual( "jungle/gates-1.png", first.GetProperty( "file" ).GetString() );
		Assert.AreEqual( 128, first.GetProperty( "width" ).GetInt32() );
		Assert.AreEqual( "preview", index.GetProperty( "files" )[2].GetProperty( "texture" ).GetString() );
		// non-ASCII text stays readable in the file
		var text = SignExport.IndexJson( "Spanish", "/x", SignExport.Plan( new[] { Sign( "jungle", "x", line1: "¿Dónde?", line2: "Año" ) } ) );
		StringAssert.Contains( text, "¿Dónde?" );
	}

	[TestMethod]
	public void TheCommandNeedsALanguageAndAnOutputDirectoryAndRunsHeadless()
	{
		Assert.AreEqual( ("Spanish", "/tmp/out"), SignExport.ParseArguments( new[] { "--export-signs", "Spanish", "/tmp/out" } ) );
		Assert.ThrowsException<ArgumentException>( () => SignExport.ParseArguments( new[] { "--export-signs", "Spanish" } ) );
		Assert.ThrowsException<ArgumentException>( () => SignExport.ParseArguments( new[] { "--export-signs", "Spanish", "--game-path", "/x" } ) );
		Assert.ThrowsException<ArgumentException>( () => SignExport.ParseArguments( new[] { "--export-signs", "--language", "Spanish" } ) );
		var args = new[] { "--export-signs", "Spanish", "/tmp/out" };
		Assert.IsTrue( Game.IsAutomatedRun( args ) );
		Assert.IsTrue( Game.IsOffscreenRun( args, null ) );
		Assert.IsTrue( Game.IsSilentRun( args, null ) );
		Assert.IsFalse( GamePathResolution.IsInteractive( args ) );
	}

	[TestMethod]
	public void AMissingLanguageFailsBeforeAnythingIsWritten()
	{
		var data = Path.Combine( root, "game", "Data" );
		Directory.CreateDirectory( Path.Combine( data, "Language", "English" ) );
		var output = Path.Combine( root, "out" );
		var previousLanguage = GameLanguage.IsSelected ? GameLanguage.Current : null;
		var previousOverlay = GameLanguage.AvailableOverlay;
		try
		{
			var error = Assert.ThrowsException<DirectoryNotFoundException>( () => SignExport.RunCommand( new[] { "--export-signs", "Klingon", output }, data, null, null ) );
			StringAssert.Contains( error.Message, "Klingon" );
			StringAssert.Contains( error.Message, "English" );
			Assert.IsFalse( Directory.Exists( output ) );
		}
		finally
		{
			GameLanguage.Current = previousLanguage!;
			GameLanguage.AvailableOverlay = previousOverlay;
		}
	}

	[TestMethod]
	public void BonusObjectsAreExportedOnlyWhenAskedFor()
	{
		Assert.IsNull( SignExport.BonusRoot( new[] { "--export-signs", "Spanish", "/tmp/out" }, () => throw new AssertFailedException( "the saved bonus folder is not read without --with-bonus" ) ) );
		Assert.ThrowsException<ArgumentException>( () => SignExport.BonusRoot( new[] { "--export-signs", "Spanish", "/tmp/out", "--bonus-data" } ) );
		Assert.ThrowsException<ArgumentException>( () => SignExport.BonusRoot( new[] { "--bonus-data", "--with-bonus" } ) );
		var empty = Path.Combine( root, "empty" );
		Directory.CreateDirectory( empty );
		Assert.ThrowsException<DirectoryNotFoundException>( () => SignExport.BonusRoot( new[] { "--bonus-data", empty } ) );
		// a levels tree without bonus archives (the __MACOSX copy of an unpacked zip) is refused rather than exporting no bonus sign
		var macosx = Path.Combine( root, "__MACOSX" );
		Directory.CreateDirectory( Path.Combine( macosx, "Bonus content", "levels", "jungle", "rides" ) );
		File.WriteAllText( Path.Combine( macosx, "Bonus content", "levels", "jungle", "rides", "._snake_1.wad" ), "" );
		var noArchives = Assert.ThrowsException<DirectoryNotFoundException>( () => SignExport.BonusRoot( new[] { "--bonus-data", macosx } ) );
		StringAssert.Contains( noArchives.Message, "no official bonus archives" );
		var bonus = Path.Combine( root, "Bonus Stuff" );
		Directory.CreateDirectory( Path.Combine( bonus, "Bonus content", "levels", "jungle", "rides" ) );
		File.WriteAllText( Path.Combine( bonus, "Bonus content", "levels", "jungle", "rides", "_snake_1.wad" ), "" );
		Assert.AreEqual( bonus, SignExport.BonusRoot( new[] { "--bonus-data", bonus } ) );
		Assert.AreEqual( bonus, SignExport.BonusRoot( new[] { "--with-bonus" }, () => bonus ) );
		Assert.ThrowsException<DirectoryNotFoundException>( () => SignExport.BonusRoot( new[] { "--with-bonus" }, () => null ) );
		// the index records where a bonus sign's lines come from
		var files = SignExport.Plan( new[] { Sign( "jungle", "_dizzyd_8", line1: "Dinos", line2: "Mareados" ) with { StringIds = Array.Empty<int>(), StringTexts = Array.Empty<string>(), TextSource = "Spanish.txt SIGNA/SIGNB", IsBonus = true } } );
		using var json = JsonDocument.Parse( SignExport.IndexJson( "Spanish", "Language/spanish", files ) );
		var first = json.RootElement.GetProperty( "files" )[0];
		Assert.AreEqual( "Spanish.txt SIGNA/SIGNB", first.GetProperty( "textSource" ).GetString() );
		Assert.IsTrue( first.GetProperty( "bonus" ).GetBoolean() );
		Assert.AreEqual( "jungle/_dizzyd_8-1.png", first.GetProperty( "file" ).GetString() );
	}

	[TestMethod]
	public void TheBonusRidesSignsAreWhatTheGameDrawsForThem()
	{
		var bonusRoot = Environment.GetEnvironmentVariable( "OPENTPW_BONUS_DATA" );
		if ( string.IsNullOrWhiteSpace( bonusRoot ) || ObjectCatalog.FindBonusLevelsParent( bonusRoot ) == null )
			Assert.Inconclusive( "Set OPENTPW_BONUS_DATA to the official bonus content for the bonus sign export." );
		if ( ObjectCatalogCorpusTests.UseOriginalData( out var previousFileSystem ) == null )
			Assert.Inconclusive( "Set OPENTPW_GAME_PATH to the original game for the sign export." );
		var previousLanguage = GameLanguage.IsSelected ? GameLanguage.Current : null;
		var previousEdition = GameEditions.Current;
		var previousBonus = ObjectCatalog.BonusDataRoot;
		try
		{
			GameEditions.Select( GameEdition.ThemeParkWorld );
			var data = FileSystem.GetAbsolutePath( "/" );
			GameLanguage.Current = GameLanguage.Resolve( data, "English", null );
			ObjectCatalog.BonusDataRoot = null;
			var plain = SignExport.Run( Path.Combine( root, "plain" ), data );
			ObjectCatalog.BonusDataRoot = bonusRoot;
			var output = Path.Combine( root, "bonus" );
			var files = SignExport.Run( output, data, bonusRoot );

			// the shipped signs are the same with and without the bonus objects
			CollectionAssert.AreEqual( plain.Select( file => (file.RelativePath, file.PngSha256) ).ToArray(), files.Where( file => !file.Sign.IsBonus ).Select( file => (file.RelativePath, file.PngSha256) ).ToArray() );

			var bonus = files.Where( file => file.Sign.IsBonus && file.Half != 0 ).ToList();
			var rides = bonus.Where( file => file.Sign.Kind == "ride" ).Select( file => file.Sign.Name ).Distinct().ToList();
			Assert.AreEqual( 14, rides.Count, string.Join( ", ", rides ) );
			Assert.IsTrue( bonus.All( file => string.Equals( file.Sign.TextSource, "English.txt SIGNA/SIGNB", StringComparison.OrdinalIgnoreCase ) ), "the bonus archives carry English.txt" );
			var dinos = bonus.First( file => file.Sign.Name == "_dizzyd_8" ).Sign;
			Assert.AreEqual( ("Dizzy", "Dinos"), (dinos.Line1, dinos.Line2) );
			Assert.AreEqual( ("jungle", "/levels/jungle/rides/_dizzyd_8"), (dinos.Theme, dinos.ArchivePath.TrimEnd( '/' )) );
			foreach ( var file in bonus )
			{
				var entry = ObjectCatalog.Load( file.Sign.Theme ).Entries.Single( entry => entry.IsBonus && entry.ArchivePath == file.Sign.ArchivePath );
				// the game's lines for a bonus object: its language file's SIGNA/SIGNB
				Assert.AreEqual( BonusNames.ReadSignLines( entry ), (file.Sign.Line1, file.Sign.Line2), file.RelativePath );
				var images = ObjectSigns.Get( entry )!;
				var decoded = ImageResult.FromMemory( File.ReadAllBytes( Path.Combine( output, file.RelativePath ) ), ColorComponents.RedGreenBlueAlpha );
				CollectionAssert.AreEqual( file.Half == 1 ? images.Left : images.Right, decoded.Data, file.RelativePath );
			}
			// the bonus folder is protected like the game data
			Assert.ThrowsException<IOException>( () => SignExport.Run( Path.Combine( bonusRoot, "signs" ), data, bonusRoot ) );
		}
		finally
		{
			FileSystem = previousFileSystem;
			GameLanguage.Current = previousLanguage!;
			GameEditions.Select( previousEdition );
			ObjectCatalog.BonusDataRoot = previousBonus;
		}
	}

	/// <summary>The fixture of the override tests: two explicit lines for the jungle gate, one name for the Inca Totem, two lines for the lobby's jungle island.</summary>
	internal const string OverrideFixture = """
		{
		  "description": "test fixture",
		  "signs": [
		    { "theme": "jungle", "object": "gates", "lines": [ "El Reino", "Perdido" ] },
		    { "theme": "Jungle", "object": "TOTEM", "name": "  El Tótem Inca  " },
		    { "theme": "lobby", "object": "jun_isle", "lines": [ "Reino", "" ] }
		  ]
		}
		""";

	private static SignTextOverrides Overrides( string json ) => SignTextOverrides.Parse( System.Text.Encoding.UTF8.GetBytes( json ), "texts.json" );

	[TestMethod]
	public void OverrideFilesGiveTwoLinesOrOneNameSplitAsTheGameSplitsAName()
	{
		var overrides = Overrides( OverrideFixture );
		Assert.AreEqual( "texts.json", overrides.FileName );
		Assert.AreEqual( 3, overrides.Entries.Count );
		Assert.AreEqual( new SignTextOverrides.Replacement( "El Reino", "Perdido", "El Reino Perdido" ), overrides.Entries[new( "jungle", "gates" )] );
		// one name: trimmed and split at the space nearest its middle, the game's rule for a sign with only a display name (ObjectSigns.Lines)
		Assert.AreEqual( new SignTextOverrides.Replacement( "El Tótem", "Inca", "  El Tótem Inca  " ), overrides.Entries.Single( pair => pair.Key.Object == "TOTEM" ).Value );
		Assert.AreEqual( SignCanvas.SplitAtMiddleSpace( "El Tótem Inca" ), SignTextOverrides.SplitName( "  El Tótem Inca  " ) );
		Assert.AreEqual( ("Reptador", ""), SignTextOverrides.SplitName( "Reptador" ) );
		Assert.AreEqual( ("El País de", "las Maravillas"), SignTextOverrides.SplitName( "El País de las Maravillas" ) );
		Assert.AreEqual( new SignTextOverrides.Replacement( "Reino", "", "Reino" ), overrides.Entries[new( "lobby", "jun_isle" )] );
		// keys are matched without regard to case
		Assert.IsTrue( overrides.TryGet( Sign( "jungle", "totem" ), out var totem ) && totem.Line1 == "El Tótem" );
		Assert.IsFalse( overrides.TryGet( Sign( "hallow", "totem" ), out _ ) );
	}

	[DataTestMethod]
	[DataRow( "[]", "top level" )]
	[DataRow( "{ \"sign\": [] }", "unknown top-level member" )]
	[DataRow( "{ }", "\"signs\" array is required" )]
	[DataRow( "{ \"signs\": [ { \"theme\": \"jungle\", \"object\": \"totem\" } ] }", "exactly one of" )]
	[DataRow( "{ \"signs\": [ { \"theme\": \"jungle\", \"object\": \"totem\", \"name\": \"A\", \"lines\": [ \"A\", \"B\" ] } ] }", "exactly one of" )]
	[DataRow( "{ \"signs\": [ { \"theme\": \"jungle\", \"object\": \"totem\", \"lines\": [ \"A\", \"B\", \"C\" ] } ] }", "exactly two strings" )]
	[DataRow( "{ \"signs\": [ { \"theme\": \"jungle\", \"object\": \"totem\", \"lines\": [ \"A\", 1 ] } ] }", "exactly two strings" )]
	[DataRow( "{ \"signs\": [ { \"theme\": \"jungle\", \"object\": \"totem\", \"name\": \"A\", \"line1\": \"A\" } ] }", "unknown member" )]
	[DataRow( "{ \"signs\": [ { \"object\": \"totem\", \"name\": \"A\" } ] }", "\"theme\" must be" )]
	[DataRow( "{ \"signs\": [ { \"theme\": \"jungle\", \"object\": \"totem\", \"name\": \"A\\nB\" } ] }", "control characters" )]
	[DataRow( "{ \"signs\": [ { \"theme\": \"jungle\", \"object\": \"totem\", \"name\": \"A\" }, { \"theme\": \"JUNGLE\", \"object\": \"Totem\", \"name\": \"B\" } ] }", "listed twice" )]
	[DataRow( "{ \"signs\": [ ], }", "not valid JSON" )]
	public void MalformedOverrideFilesAreRejected( string json, string message )
	{
		var error = Assert.ThrowsException<InvalidDataException>( () => Overrides( json ) );
		StringAssert.Contains( error.Message, message );
	}

	[TestMethod]
	public void UnknownOverrideKeysAreAnErrorListingThem()
	{
		var signs = new[] { Sign( "jungle", "totem" ), Sign( "jungle", "gates", "gate" ) };
		var overrides = Overrides( """{ "signs": [ { "theme": "jungle", "object": "totem", "name": "A" }, { "theme": "jungle", "object": "totum", "name": "B" }, { "theme": "mars", "object": "gates", "name": "C" } ] }""" );
		var error = Assert.ThrowsException<InvalidDataException>( () => SignExport.ApplyOverrides( signs, overrides ) );
		StringAssert.Contains( error.Message, "jungle/totum" );
		StringAssert.Contains( error.Message, "mars/gates" );
		Assert.IsFalse( error.Message.Contains( "jungle/totem," ) );
		Assert.ThrowsException<FileNotFoundException>( () => SignTextOverrides.Load( Path.Combine( root, "missing.json" ) ) );
		Assert.AreEqual( Path.Combine( root, "texts.json" ), SignExport.SignTextsPath( new[] { "--sign-texts", Path.Combine( root, "texts.json" ) } ) );
		Assert.ThrowsException<ArgumentException>( () => SignExport.SignTextsPath( new[] { "--sign-texts", "--bonus-data" } ) );
	}

	[TestMethod]
	public void OverridesChangeExactlyTheNamedSignsWithTheGamesComposer()
	{
		if ( ObjectCatalogCorpusTests.UseOriginalData( out var previousFileSystem ) == null )
			Assert.Inconclusive( "Set OPENTPW_GAME_PATH to the original game for the sign export." );
		var previousLanguage = GameLanguage.IsSelected ? GameLanguage.Current : null;
		var previousEdition = GameEditions.Current;
		var previousBonus = ObjectCatalog.BonusDataRoot;
		try
		{
			ObjectCatalog.BonusDataRoot = null;
			GameEditions.Select( GameEdition.ThemeParkWorld );
			var data = FileSystem.GetAbsolutePath( "/" );
			GameLanguage.Current = GameLanguage.Resolve( data, "English", null );
			var fixture = Path.Combine( root, "texts.json" );
			File.WriteAllText( fixture, OverrideFixture );
			var fixtureBytes = File.ReadAllBytes( fixture );
			var plain = SignExport.Run( Path.Combine( root, "plain" ), data );
			var output = Path.Combine( root, "override" );
			var files = SignExport.Run( output, SignTextOverrides.Load( fixture ), data );
			CollectionAssert.AreEqual( fixtureBytes, File.ReadAllBytes( fixture ), "the override file is only read" );

			// the same files, and only the three named signs differ
			CollectionAssert.AreEqual( plain.Select( file => file.RelativePath ).ToArray(), files.Select( file => file.RelativePath ).ToArray() );
			var changed = files.Zip( plain ).Where( pair => pair.First.PngSha256 != pair.Second.PngSha256 ).Select( pair => pair.First.Sign.Theme + "/" + pair.First.Sign.Name ).Distinct().ToArray();
			CollectionAssert.AreEquivalent( new[] { "jungle/gates", "jungle/totem", "lobby/jun_isle" }, changed );
			Assert.IsTrue( files.Where( file => !changed.Contains( file.Sign.Theme + "/" + file.Sign.Name ) ).All( file => file.Sign.Original == null && !file.Sign.TextSource.StartsWith( "override" ) ) );

			var gate = files.First( file => file.Sign.Theme == "jungle" && file.Sign.Name == "gates" ).Sign;
			Assert.AreEqual( ("El Reino", "Perdido", "override (texts.json)"), (gate.Line1, gate.Line2, gate.TextSource) );
			Assert.AreEqual( ("LOST", "Kingdom"), (gate.Original!.Line1, gate.Original.Line2) );
			var totem = files.First( file => file.Sign.Theme == "jungle" && file.Sign.Name == "totem" ).Sign;
			Assert.AreEqual( ("El Tótem", "Inca"), (totem.Line1, totem.Line2) );

			// every overridden texture is what the game's composer draws from that .sgn with those lines
			foreach ( var file in files.Where( file => file.Sign.Original != null && file.Half != 0 ) )
			{
				var sign = file.Sign;
				var signFile = sign.Theme == SignExport.LobbyFolder
					? new SignFile( FileSystem.ReadAllBytes( sign.SignPath ) )
					: SignTextRenderer.LoadSignFile( sign.ArchivePath )!;
				var composed = SignTextRenderer.Shared.ComposeSign( signFile, new[] { sign.Line1, sign.Line2 }, default );
				var decoded = ImageResult.FromMemory( File.ReadAllBytes( Path.Combine( output, file.RelativePath ) ), ColorComponents.RedGreenBlueAlpha );
				CollectionAssert.AreEqual( file.Half == 1 ? composed.Left : composed.Right, decoded.Data, file.RelativePath );
			}

			// the index records the override and the game's own text
			using var json = JsonDocument.Parse( File.ReadAllText( Path.Combine( output, SignExport.IndexFileName ) ) );
			var row = json.RootElement.GetProperty( "files" ).EnumerateArray().First( row => row.GetProperty( "file" ).GetString() == "jungle/gates-1.png" );
			Assert.AreEqual( "override (texts.json)", row.GetProperty( "textSource" ).GetString() );
			Assert.AreEqual( "El Reino", row.GetProperty( "line1" ).GetString() );
			Assert.AreEqual( "LOST", row.GetProperty( "originalLine1" ).GetString() );
			Assert.AreEqual( "Kingdom", row.GetProperty( "originalLine2" ).GetString() );
			Assert.AreEqual( "OBJECT_NAMES.str gate row", row.GetProperty( "originalTextSource" ).GetString() );
			Assert.IsFalse( json.RootElement.GetProperty( "files" ).EnumerateArray().Where( row => row.GetProperty( "textSource" ).GetString()!.StartsWith( "override" ) == false ).Any( row => row.TryGetProperty( "originalText", out _ ) ) );

			// the name rule is the game's: every shipped object's own lines are its OBJECT_NAMES name split this way
			foreach ( var theme in ObjectCatalog.Themes )
				foreach ( var entry in ObjectCatalog.Load( theme ).Entries.Where( entry => !entry.IsExternal && entry.SignName != null ) )
					Assert.AreEqual( ObjectSigns.Lines( entry ), SignTextOverrides.SplitName( entry.SignName! ), entry.ArchivePath );

			// a key the export does not have is an error before anything is written
			var unknown = Path.Combine( root, "unknown.json" );
			File.WriteAllText( unknown, """{ "signs": [ { "theme": "jungle", "object": "nosuchride", "name": "X" } ] }""" );
			var bad = Path.Combine( root, "bad" );
			var error = Assert.ThrowsException<InvalidDataException>( () => SignExport.Run( bad, SignTextOverrides.Load( unknown ), data ) );
			StringAssert.Contains( error.Message, "jungle/nosuchride" );
			Assert.IsFalse( Directory.Exists( bad ) );
		}
		finally
		{
			FileSystem = previousFileSystem;
			GameLanguage.Current = previousLanguage!;
			GameEditions.Select( previousEdition );
			ObjectCatalog.BonusDataRoot = previousBonus;
		}
	}

	[TestMethod]
	public void TheEnglishExportMatchesTheSignsTheLevelAndTheLobbyUse()
	{
		if ( ObjectCatalogCorpusTests.UseOriginalData( out var previousFileSystem ) == null )
			Assert.Inconclusive( "Set OPENTPW_GAME_PATH to the original game for the sign export." );
		var previousLanguage = GameLanguage.IsSelected ? GameLanguage.Current : null;
		var previousEdition = GameEditions.Current;
		var previousBonus = ObjectCatalog.BonusDataRoot;
		try
		{
			ObjectCatalog.BonusDataRoot = null;
			GameEditions.Select( GameEdition.ThemeParkWorld );
			var data = FileSystem.GetAbsolutePath( "/" );
			GameLanguage.Current = GameLanguage.Resolve( data, "English", null );
			var output = Path.Combine( root, "english" );
			var files = SignExport.Run( output, data );

			using var json = JsonDocument.Parse( File.ReadAllText( Path.Combine( output, SignExport.IndexFileName ) ) );
			var rows = json.RootElement.GetProperty( "files" ).EnumerateArray().ToList();
			Assert.AreEqual( files.Count, rows.Count );
			Assert.AreEqual( files.Count, Directory.EnumerateFiles( output, "*.png", SearchOption.AllDirectories ).Count() );
			Assert.AreEqual( "English", json.RootElement.GetProperty( "language" ).GetString() );
			// the index is meant to be shared: no absolute path of the user's machine
			Assert.IsFalse( Path.IsPathRooted( json.RootElement.GetProperty( "languageFolder" ).GetString() ) );
			Assert.IsFalse( File.ReadAllText( Path.Combine( output, SignExport.IndexFileName ) ).Contains( Path.GetDirectoryName( data.TrimEnd( '/' ) )!, StringComparison.OrdinalIgnoreCase ) );

			// the jungle gate: OBJECT_NAMES 85 and 86, "LOST" over "Kingdom", as a new park's gate starts
			var gate = rows.Where( row => row.GetProperty( "theme" ).GetString() == "jungle" && row.GetProperty( "kind" ).GetString() == "gate" ).ToList();
			CollectionAssert.AreEqual( new[] { "jungle/gates-1.png", "jungle/gates-2.png", "jungle/gates.png" }, gate.Select( row => row.GetProperty( "file" ).GetString() ).ToArray() );
			Assert.IsTrue( gate.All( row => row.GetProperty( "line1" ).GetString() == "LOST" && row.GetProperty( "line2" ).GetString() == "Kingdom" ) );
			CollectionAssert.AreEqual( new[] { 85, 86 }, gate[0].GetProperty( "stringIds" ).EnumerateArray().Select( id => id.GetInt32() ).ToArray() );
			CollectionAssert.AreEqual( new[] { "LOST", "Kingdom" }, gate[0].GetProperty( "strings" ).EnumerateArray().Select( text => text.GetString() ).ToArray() );
			foreach ( var theme in new[] { "jungle", "hallow", "fantasy", "space", SignExport.LobbyFolder } )
				Assert.IsTrue( files.Any( file => file.Sign.Theme == theme ), $"{theme} has signs" );

			// every texture is byte for byte what the game composes for it
			var levelSigns = 0;
			var lobbySigns = 0;
			foreach ( var file in files.Where( file => file.Half != 0 ) )
			{
				var sign = file.Sign;
				byte[] expected;
				if ( sign.Theme == SignExport.LobbyFolder )
				{
					var island = OpenTPW.FrontEnd.LobbyDefinition.Load().Islands.First( island => string.Equals( island.IslandModel, sign.Name, StringComparison.OrdinalIgnoreCase ) || string.Equals( island.GateModel, sign.Name, StringComparison.OrdinalIgnoreCase ) );
					var images = LobbySigns.Compose( sign.SignPath, LobbySigns.Lines( island, LobbySigns.LoadObjectNames() ) )!;
					expected = file.Half == 1 ? images.Left : images.Right;
					lobbySigns++;
				}
				else
				{
					var entry = ObjectCatalog.Load( sign.Theme ).Get( sign.InfoId!.Value );
					if ( sign.Kind == "gate" )
					{
						// the level's gate (OriginalGateSign.TryCreate): its .sgn with the default lines, composed as they are
						var lines = OriginalGateSign.DefaultLines( sign.Theme, entry.InfoId );
						var composed = SignTextRenderer.Shared.ComposeSign( SignTextRenderer.LoadSignFile( entry.ArchivePath )!, new[] { lines.Line1, lines.Line2 }, default );
						expected = file.Half == 1 ? composed.Left : composed.Right;
					}
					else
					{
						var images = ObjectSigns.Get( entry )!;
						Assert.AreEqual( (images.Line1, images.Line2), (sign.Line1, sign.Line2), sign.SignPath );
						expected = file.Half == 1 ? images.Left : images.Right;
					}
					levelSigns++;
				}
				var decoded = ImageResult.FromMemory( File.ReadAllBytes( Path.Combine( output, file.RelativePath ) ), ColorComponents.RedGreenBlueAlpha );
				Assert.AreEqual( (128, 128), (decoded.Width, decoded.Height), file.RelativePath );
				CollectionAssert.AreEqual( expected, decoded.Data, file.RelativePath );
			}
			Assert.IsTrue( levelSigns >= 2 * 75, $"{levelSigns} level sign textures" );
			Assert.AreEqual( 8, lobbySigns, "the lobby's four sign models" );
			// without --bonus-data only the shipped data: no bonus row, and every shipped object's lines come from OBJECT_NAMES
			Assert.IsFalse( files.Any( file => file.Sign.IsBonus || file.Sign.Name.StartsWith( '_' ) ), "no bonus object without --bonus-data" );
			Assert.IsFalse( rows.Any( row => row.TryGetProperty( "bonus", out _ ) ) );
			Assert.IsTrue( rows.Where( row => row.GetProperty( "kind" ).GetString() is "ride" or "shop" ).All( row => row.GetProperty( "textSource" ).GetString() == "OBJECT_NAMES.str ids" ) );

			// a second export into the same, now full, directory is refused and changes nothing
			var before = File.ReadAllBytes( Path.Combine( output, "jungle", "gates-1.png" ) );
			Assert.ThrowsException<IOException>( () => SignExport.Run( output, data ) );
			CollectionAssert.AreEqual( before, File.ReadAllBytes( Path.Combine( output, "jungle", "gates-1.png" ) ) );
		}
		finally
		{
			FileSystem = previousFileSystem;
			GameLanguage.Current = previousLanguage!;
			GameEditions.Select( previousEdition );
			ObjectCatalog.BonusDataRoot = previousBonus;
		}
	}
}

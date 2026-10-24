using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTPW.Mods;

namespace OpenTPW.Tests;

/// <summary>
/// The Theme Park Inc CD's own languages (Dutch, English, French, German, Swedish under <c>Data/Language</c>) are read by the same
/// <see cref="GameLanguage"/> and <c>MBToUni.dat</c> path as Theme Park World's, in the experimental slice and for the objects the
/// cross-edition mod brings into Theme Park World (docs/LANGUAGES.md, "Theme Park Inc"). Data tests use <c>OPENTPW_TPI_PATH</c>.
/// </summary>
[TestClass]
[DoNotParallelize]
public class ThemeParkIncLanguageTests
{
	private static readonly string[] TpiLanguages = { "Dutch", "English", "French", "German", "Swedish" };

	private BaseFileSystem? previousFileSystem;
	private GameLanguage? previousLanguage;
	private string? previousOverlay;
	private string root = "";
	private IReadOnlyDictionary<BaseFileSystem, SoundRoot>? previousSoundRoots;

	[TestInitialize]
	public void Remember()
	{
		previousFileSystem = FileSystem;
		previousLanguage = GameLanguage.IsSelected ? GameLanguage.Current : null;
		previousOverlay = GameLanguage.AvailableOverlay;
		previousSoundRoots = ContentSoundRoots.Snapshot();
		root = Path.Combine( Path.GetTempPath(), $"opentpw-tpi-language-{Guid.NewGuid():N}" );
		Directory.CreateDirectory( root );
	}

	[TestCleanup]
	public void Restore()
	{
		FileSystem = previousFileSystem!;
		GameLanguage.Current = previousLanguage!;
		GameLanguage.AvailableOverlay = previousOverlay;
		ContentSoundRoots.Restore( previousSoundRoots! );
		GameEditions.Select( GameEdition.ThemeParkWorld );
		ObjectCatalog.BonusDataRoot = null;
		Directory.Delete( root, true );
	}

	// ---- Synthetic (no game data) ----

	private static byte[] Names( string characters, params string[] names ) =>
		StringTableTests.CreateStringTable( names.Select( name => name.Select( c => (byte)(characters.IndexOf( c ) + 1) ).ToArray() ).ToArray() );

	private string CreateInstall( string folder, params (string Language, string[] Names)[] languages )
	{
		var data = Path.Combine( root, folder, "Data" );
		const string characters = "abcdefghijklmnopqrstuvwxyz -";
		foreach ( var (language, names) in languages )
		{
			var directory = Path.Combine( data, "Language", language );
			Directory.CreateDirectory( directory );
			File.WriteAllBytes( Path.Combine( directory, "MBToUni.dat" ), StringTableTests.CreateCharacterTable( characters ) );
			File.WriteAllBytes( Path.Combine( directory, "OBJECT_NAMES.str" ), Names( characters, names ) );
			File.WriteAllBytes( Path.Combine( directory, "UITEXT.str" ), Names( characters, "a" ) );
		}
		return data;
	}

	private static ObjectCatalogEntry ModEntry( BaseFileSystem fileSystem, string ownName, string objectsJsonName = "json name" ) =>
		new( "jungle", ObjectCategory.Ride, "/levels/science/rides/x", new ObjectSettings( Array.Empty<ObjectSettingsFile>() ), ObjectShape.Single,
			"/m.MD2", null, null, Array.Empty<string>(), Array.Empty<ObjectAnimationFile>(), Array.Empty<string>(), Array.Empty<string>(), fileSystem, null, ContentRoot.Mod( "community.tpi-rides" ) )
		{
			ModDefinition = new ModObjectDefinition( "levels/science/rides/x", "tpi", "jungle", ObjectCategory.Ride, 40001, new Dictionary<string, string> { ["English"] = objectsJsonName } ),
			OwnSettingsName = ownName,
			DisplayName = objectsJsonName
		};

	[TestMethod]
	public void ModObjectsTakeTheirNamesFromTheThemeParkIncStringTablesInTheSelectedLanguage()
	{
		// Index 0 and 1 are the empty "no second part" entries; the entry names are Theme Park Inc's own (two-part names join with a space).
		var tpi = CreateInstall( "tpi", ("English", new[] { "", "", "photo kiosk", "", "miniature", "dreadnaughts" } ), ("Dutch", new[] { "", "", "fotostalletje", "", "miniracer", "" } ) );
		var fileSystem = new BaseFileSystem( tpi );
		ContentSoundRoots.Clear();
		ContentSoundRoots.Register( fileSystem );
		var game = CreateInstall( "game", ("English", new[] { "" } ), ("Dutch", new[] { "" } ) );
		GameLanguage.Current = GameLanguage.Resolve( game, "Dutch", null );

		var kiosk = ModEntry( fileSystem, "Photo Kiosk" );
		var dreadnaught = ModEntry( fileSystem, "Miniature Dreadnaughts" );
		var unknown = ModEntry( fileSystem, "Nothing Like It" );
		ObjectNames.ApplyThemeParkIncNames( new[] { kiosk, dreadnaught, unknown } );
		Assert.AreEqual( "fotostalletje", kiosk.DisplayName );
		Assert.AreEqual( "miniracer", dreadnaught.DisplayName, "the Dutch table's empty second part is dropped" );
		Assert.AreEqual( "json name", unknown.DisplayName, "an object the table does not hold keeps the name objects.json gives" );

		// In English the install's English names apply; a language the install lacks (Theme Park Inc has five) falls back to its English.
		GameLanguage.Current = GameLanguage.Resolve( CreateInstall( "game-sv", ("English", new[] { "" } ), ("Swedish", new[] { "" } ) ), "Swedish", null );
		var swedish = ModEntry( fileSystem, "Photo Kiosk" );
		ObjectNames.ApplyThemeParkIncNames( new[] { swedish } );
		Assert.AreEqual( "photo kiosk", swedish.DisplayName );

		// An input that is not a registered Theme Park Inc root (the extension off, a wrong folder) and an object without its own name change nothing.
		ContentSoundRoots.Clear();
		var unregistered = ModEntry( fileSystem, "Photo Kiosk" );
		GameLanguage.Current = GameLanguage.Resolve( game, "Dutch", null );
		ObjectNames.ApplyThemeParkIncNames( new[] { unregistered } );
		Assert.AreEqual( "json name", unregistered.DisplayName );
		ContentSoundRoots.Register( fileSystem );
		var withoutName = ModEntry( fileSystem, "Photo Kiosk" );
		ObjectNames.ApplyThemeParkIncNames( new[] { new ObjectCatalogEntry( "jungle", ObjectCategory.Ride, "/a", new ObjectSettings( Array.Empty<ObjectSettingsFile>() ), ObjectShape.Single,
			"/m.MD2", null, null, Array.Empty<string>(), Array.Empty<ObjectAnimationFile>(), Array.Empty<string>(), Array.Empty<string>(), fileSystem, null ) } );
		ObjectNames.ApplyThemeParkIncNames( new[] { withoutName } );
		Assert.AreEqual( "fotostalletje", withoutName.DisplayName );
	}

	[TestMethod]
	public void ALanguageFolderOfAThemeParkIncInstallIsOfferedLikeAnyOther()
	{
		var tpi = CreateInstall( "tpi", ("English", new[] { "" } ), ("Dutch", new[] { "" } ), ("French", new[] { "" } ), ("German", new[] { "" } ), ("Swedish", new[] { "" } ) );
		CollectionAssert.AreEqual( TpiLanguages, GameLanguage.FindLanguages( tpi, null ).ToArray() );
		CollectionAssert.AreEqual( TpiLanguages, GameLanguage.Readable( tpi, null ).ToArray() );
		Assert.AreEqual( "English", GameLanguage.Resolve( tpi, null, null ).Name );
		Assert.AreEqual( "Dutch", GameLanguage.Resolve( tpi, "dutch", null ).Name );
	}

	// ---- Original data (OPENTPW_TPI_PATH) ----

	private string TpiData()
	{
		var path = Environment.GetEnvironmentVariable( "OPENTPW_TPI_PATH" );
		if ( string.IsNullOrWhiteSpace( path ) || !Directory.Exists( path ) )
			Assert.Inconclusive( "Set OPENTPW_TPI_PATH to a Theme Park Inc installation or its Data folder." );
		return GameLanguage.FindEntry( path!, "Data", true ) ?? path!;
	}

	[TestMethod]
	public void EveryThemeParkIncStringTableDecodesInEveryLanguageOfTheCd()
	{
		var data = TpiData();
		CollectionAssert.AreEqual( TpiLanguages, GameLanguage.FindLanguages( data, null ).ToArray() );
		CollectionAssert.AreEqual( TpiLanguages, GameLanguage.Readable( data, null ).ToArray() );
		Assert.AreEqual( "English", GameLanguage.Resolve( data, null, null ).Name );
		var tables = 0;
		foreach ( var name in TpiLanguages )
		{
			var language = GameLanguage.Resolve( data, name, null );
			Assert.IsFalse( language.CharacterTable.IsMultiByte );
			var total = 0;
			foreach ( var file in language.EnumerateFiles().Where( file => file.EndsWith( ".str", StringComparison.OrdinalIgnoreCase ) ) )
			{
				total += language.LoadStrings( Path.GetFileName( file ), asStored: true ).Entries.Length;
				tables++;
			}
			Assert.AreEqual( 3775, total, name );
			// every character the tables use has a glyph in the label font of the language
			var font = language.LoadFont( "GAME8AA.bf4" ).Glyphs.Select( glyph => glyph.Character ).ToHashSet();
			var used = language.EnumerateFiles().Where( file => file.EndsWith( ".str", StringComparison.OrdinalIgnoreCase ) )
				.SelectMany( file => language.LoadStrings( Path.GetFileName( file ), asStored: true ).Entries ).SelectMany( entry => entry ).Where( c => c > ' ' ).ToHashSet();
			Assert.AreEqual( 0, used.Count( c => !font.Contains( c ) ), $"{name} GAME8AA lacks a character of its tables" );
		}
		Assert.AreEqual( 37 * 5, tables );
	}

	[DataTestMethod]
	[DataRow( "English", "Quit Game", "Photo Kiosk" )]
	[DataRow( "Dutch", "Spel afsluiten", "Fotostalletje" )]
	[DataRow( "French", "Quitter la partie", "Kiosque photo" )]
	[DataRow( "German", "Spiel verlassen", "Fotokiosk" )]
	[DataRow( "Swedish", "Sluta spela", "Fotohytt" )]
	public void ADutchOrOtherThemeParkIncStringShowsUp( string name, string quit, string kiosk )
	{
		var language = GameLanguage.Resolve( TpiData(), name, null );
		Assert.AreEqual( quit, language.LoadStrings( "UITEXT.str", asStored: true )[30] );
		Assert.AreEqual( kiosk, language.LoadStrings( "OBJECT_NAMES.str" )[2] );
		Assert.AreEqual( name, UiStringTableLanguage( language ) );
	}

	private static string UiStringTableLanguage( GameLanguage language ) => OpenTPW.UI.Original.UiStringTable.Load( language ).Language;

	[TestMethod]
	public void TheSliceNamesItsObjectsInTheSelectedLanguageAndTheModTakesTheSameNames()
	{
		var data = TpiData();
		FileSystem = new BaseFileSystem( data );
		FileSystem.RegisterArchiveHandler<WadArchive>( ".wad" );
		GameEditions.Select( GameEdition.ThemeParkInc );
		ObjectCatalog.BonusDataRoot = null;
		GameLanguage.Current = GameLanguage.Resolve( data, "English", null );
		var english = ObjectCatalog.Load( "arabian" ).Entries.ToDictionary( entry => entry.ArchivePath, entry => entry.DisplayName );
		GameLanguage.Current = GameLanguage.Resolve( data, "Dutch", null );
		var dutchCatalog = ObjectCatalog.Load( "arabian" );
		var dutch = dutchCatalog.Entries.ToDictionary( entry => entry.ArchivePath, entry => entry.DisplayName );
		Assert.AreEqual( english.Count, dutch.Count );
		var changed = dutch.Where( pair => english[pair.Key] != pair.Value ).ToArray();
		Assert.IsTrue( changed.Length >= 10, $"{changed.Length} of {dutch.Count} names differ in Dutch" );

		// The cross-edition mod: the same TPI archives as mod objects (their own .sam names replaced by objects.json's English ones) get the Dutch table's names.
		var fileSystem = new BaseFileSystem( data );
		fileSystem.RegisterArchiveHandler<WadArchive>( ".wad" );
		ContentSoundRoots.Clear();
		ContentSoundRoots.Register( fileSystem );
		var mod = dutchCatalog.Entries.Where( entry => english[entry.ArchivePath] != dutch[entry.ArchivePath] ).Take( 10 )
			.Select( entry => ModEntry( fileSystem, entry.SettingsName, "json: " + entry.SettingsName ) ).ToArray();
		ObjectNames.ApplyThemeParkIncNames( mod );
		Assert.AreEqual( 10, mod.Length );
		foreach ( var entry in mod )
			Assert.AreNotEqual( "json: " + entry.OwnSettingsName, entry.DisplayName, entry.OwnSettingsName );
		Assert.IsTrue( mod.Select( entry => entry.DisplayName ).All( name => dutch.Values.Contains( name ) ), "every mod name is a Dutch name of the slice" );
	}
}

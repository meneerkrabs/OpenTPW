using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

[TestClass]
public class SetupTests
{
	private string root = null!;

	[TestInitialize]
	public void CreateRoot()
	{
		root = Path.Combine( Path.GetTempPath(), "opentpw-setup-" + Guid.NewGuid().ToString( "N" ) );
		Directory.CreateDirectory( root );
	}

	[TestCleanup]
	public void DeleteRoot() => Directory.Delete( root, true );

	/// <summary>The minimum an installation needs: Data/levels, Data/global and one language.</summary>
	private string CreateGame( string name, bool complete = true, string dataSpelling = "Data" )
	{
		var game = Path.Combine( root, name );
		var data = Path.Combine( game, dataSpelling );
		Directory.CreateDirectory( Path.Combine( data, "levels", "jungle" ) );
		Directory.CreateDirectory( Path.Combine( data, "global" ) );
		Directory.CreateDirectory( Path.Combine( data, "Movies" ) );
		var language = Path.Combine( data, "Language", "English" );
		Directory.CreateDirectory( language );
		if ( complete )
			foreach ( var file in new[] { "bankrupt.MD2", "congrats.MD2", "paused.MD2", "swears.txt" } )
				File.WriteAllText( Path.Combine( language, file ), "" );
		return game;
	}

	[TestMethod]
	public void CompleteInstallationIsUsableWithoutWarnings()
	{
		var report = GameInstallation.Inspect( CreateGame( "Theme Park World" ) );
		Assert.IsTrue( report.IsUsable, string.Join( " | ", report.Problems ) );
		CollectionAssert.AreEqual( new[] { "English" }, report.Languages.ToArray() );
		Assert.AreEqual( 0, report.Warnings.Count, string.Join( " | ", report.Warnings ) );
	}

	[TestMethod]
	public void ChoosingTheDataFolderUsesItsParent()
	{
		var game = CreateGame( "Theme Park World" );
		var report = GameInstallation.Inspect( Path.Combine( game, "Data" ) );
		Assert.IsTrue( report.IsUsable );
		Assert.AreEqual( Path.GetFullPath( game ), report.Path );
	}

	[TestMethod]
	public void MissingPartsAreProblemsAndOptionalPartsAreWarnings()
	{
		Assert.IsFalse( GameInstallation.Inspect( "" ).IsUsable );
		Assert.IsFalse( GameInstallation.Inspect( Path.Combine( root, "nowhere" ) ).IsUsable );

		var empty = Path.Combine( root, "empty" );
		Directory.CreateDirectory( empty );
		var noData = GameInstallation.Inspect( empty );
		Assert.IsFalse( noData.IsUsable );
		StringAssert.Contains( noData.Problems[0], "No Data folder" );

		var partial = CreateGame( "partial" );
		Directory.Delete( Path.Combine( partial, "Data", "global" ) );
		Assert.IsTrue( GameInstallation.Inspect( partial ).Problems.Any( problem => problem.Contains( "Data/global" ) ) );

		// Like the CD's own Data/Language: no banners or word filter, and no movies here.
		var cdLike = CreateGame( "cd", complete: false );
		Directory.Delete( Path.Combine( cdLike, "Data", "Movies" ) );
		var report = GameInstallation.Inspect( cdLike );
		Assert.IsTrue( report.IsUsable );
		Assert.AreEqual( 2, report.Warnings.Count, string.Join( " | ", report.Warnings ) );
	}

	[TestMethod]
	public void UnreadableFoldersAreReportedNotThrown()
	{
		if ( OperatingSystem.IsWindows() )
			Assert.Inconclusive( "Uses Unix permissions." );
		var locked = CreateGame( "locked" );
		File.SetUnixFileMode( locked, UnixFileMode.None );
		try
		{
			var report = GameInstallation.Inspect( locked );
			Assert.IsFalse( report.IsUsable );
		}
		finally
		{
			File.SetUnixFileMode( locked, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute );
		}
	}

	[TestMethod]
	public void KnownUnsupportedSimCoasterIdentityIsRejectedWithoutAnEditionAllowlist()
	{
		var tpiRoot = Environment.GetEnvironmentVariable( "OPENTPW_TPI_GAME_PATH" );
		if ( string.IsNullOrWhiteSpace( tpiRoot ) || !Directory.Exists( tpiRoot ) )
			Assert.Inconclusive( "Set OPENTPW_TPI_GAME_PATH for the identified Sim Coaster retail fixture." );
		var report = InstallationDiscovery.InspectAsync( tpiRoot! ).GetAwaiter().GetResult().Reports.Single();
		Assert.IsFalse( report.IsUsable );
		Assert.IsTrue( report.Problems.Any( problem => problem.Contains( "Sim Coaster retail edition" ) ) );
		// Unknown edits are not classified as TPI by their folder tree alone.
		var unknown = CreateGame( "modified" );
		File.WriteAllText( Path.Combine( unknown, "Data", "levels", "Standard.sam" ), "modified settings" );
		Assert.IsTrue( GameInstallation.Inspect( unknown ).IsUsable );
	}

	[TestMethod]
	public void DataFolderSpellingDoesNotMatter()
	{
		Assert.IsTrue( GameInstallation.Inspect( CreateGame( "cd-mount", dataSpelling: "DATA" ) ).IsUsable );
	}

	[TestMethod]
	public void SetupSettingsRoundTripAndTolerateBrokenFiles()
	{
		var path = Path.Combine( root, "config", SetupSettings.FileName );
		Assert.AreEqual( new SetupSettings( null, null ), SetupSettings.Load( path ) );
		new SetupSettings( "/games/tpw", "/media/cd" ).Save( path );
		Assert.AreEqual( new SetupSettings( "/games/tpw", "/media/cd" ), SetupSettings.Load( path ) );
		StringAssert.Contains( File.ReadAllText( path ), "\"gamePath\"" );
		File.WriteAllText( path, "{ not json" );
		Assert.AreEqual( new SetupSettings( null, null ), SetupSettings.Load( path ) );
	}

	[TestMethod]
	public void ResolutionPrefersOverridesThenSavedThenLegacyThenDetected()
	{
		var saved = CreateGame( "saved" );
		var legacy = CreateGame( "legacy" );
		var detected = CreateGame( "detected" );
		var none = new SetupSettings( null, null );
		var detectCalls = 0;
		string[] Detect() { detectCalls++; return new[] { Path.Combine( root, "missing" ), detected }; }

		// Developer overrides win and are not inspected, so a wrong path still fails loudly later.
		Assert.AreEqual( (Path.GetFullPath( "/elsewhere" ), GamePathSource.CommandLine), GamePathResolution.Resolve( "/elsewhere", saved, new SetupSettings( saved, null ), legacy, Detect ) );
		Assert.AreEqual( (Path.GetFullPath( saved ), GamePathSource.Environment), GamePathResolution.Resolve( null, saved, none, legacy, Detect ) );
		Assert.AreEqual( (Path.GetFullPath( saved ), GamePathSource.Saved), GamePathResolution.Resolve( null, null, new SetupSettings( saved, null ), legacy, Detect ) );
		Assert.AreEqual( 0, detectCalls, "Detection only runs when nothing else applies." );

		// A saved folder that moved falls through to the next source.
		Assert.AreEqual( (Path.GetFullPath( legacy ), GamePathSource.Legacy), GamePathResolution.Resolve( null, null, new SetupSettings( Path.Combine( root, "moved" ), null ), legacy, Detect ) );
		Assert.AreEqual( (Path.GetFullPath( detected ), GamePathSource.Detected), GamePathResolution.Resolve( null, null, none, @"C:\Program Files (x86)\Bullfrog\Theme Park World", Detect ) );
		Assert.IsNull( GamePathResolution.Resolve( null, null, none, null, () => Array.Empty<string>() ) );
	}

	[TestMethod]
	public void ToolModesNeverOpenTheSetupWindow()
	{
		Assert.IsTrue( GamePathResolution.IsInteractive( Array.Empty<string>() ) );
		Assert.IsTrue( GamePathResolution.IsInteractive( new[] { "--sandbox" } ) );
		foreach ( var tool in new[] { "--smoke-test", "--validate-assets", "--inspect-model", "--headless", "--build-texture-pack", "--capture-world", "--export-park", "--import-park" } )
			Assert.IsFalse( GamePathResolution.IsInteractive( new[] { tool } ), tool );
	}

	[TestMethod]
	public void BonusPathIsOptionalInSetupFiles()
	{
		var path = Path.Combine( root, "config", SetupSettings.FileName );
		Directory.CreateDirectory( Path.GetDirectoryName( path )! );
		// A setup.json written before bonus content existed still loads, with no bonus path.
		File.WriteAllText( path, "{ \"gamePath\": \"/games/tpw\", \"cdPath\": null }" );
		Assert.AreEqual( new SetupSettings( "/games/tpw", null, null ), SetupSettings.Load( path ) );
		new SetupSettings( "/games/tpw", null, "/media/bonus" ).Save( path );
		StringAssert.Contains( File.ReadAllText( path ), "\"bonusPath\"" );
		Assert.AreEqual( new SetupSettings( "/games/tpw", null, "/media/bonus" ), SetupSettings.Load( path ) );
		new SetupSettings( "/games/tpw", null, "" ).Save( path );
		Assert.AreEqual( "", SetupSettings.Load( path ).BonusPath );
	}

	/// <summary>A bonus root as the game expects it: a folder with a levels directory.</summary>
	private string CreateBonusRoot( string name )
	{
		var bonus = Path.Combine( root, name );
		Directory.CreateDirectory( Path.Combine( bonus, "levels", "jungle", "rides" ) );
		return bonus;
	}

	[TestMethod]
	public void BonusRootPrecedenceOptionThenEnvironmentThenSavedThenConfigFolder()
	{
		var config = Path.Combine( root, "config" );
		var saved = CreateBonusRoot( "saved" );
		var env = CreateBonusRoot( "env" );
		var option = CreateBonusRoot( "option" );
		var imported = Path.Combine( config, BonusContent.FolderName );
		Directory.CreateDirectory( Path.Combine( imported, "levels", "jungle" ) );

		Assert.AreEqual( (Path.GetFullPath( option ), BonusPathSource.CommandLine), BonusContent.ResolveRoot( option, env, saved, config ) );
		Assert.AreEqual( (Path.GetFullPath( env ), BonusPathSource.Environment), BonusContent.ResolveRoot( null, env, saved, config ) );
		Assert.AreEqual( (Path.GetFullPath( saved ), BonusPathSource.Saved), BonusContent.ResolveRoot( null, " ", saved, config ) );
		Assert.AreEqual( (Path.GetFullPath( imported ), BonusPathSource.ConfigFolder), BonusContent.ResolveRoot( null, null, null, config ) );

		// A saved folder that moved, or has no levels folder, falls through to the configuration folder.
		Assert.AreEqual( BonusPathSource.ConfigFolder, BonusContent.ResolveRoot( null, null, Path.Combine( root, "moved" ), config )!.Value.Source );
		var empty = Path.Combine( root, "empty" );
		Directory.CreateDirectory( empty );
		Assert.AreEqual( BonusPathSource.ConfigFolder, BonusContent.ResolveRoot( null, null, empty, config )!.Value.Source );

		// An empty saved path is the player's Remove: neither the saved nor the imported copy is used.
		Assert.IsNull( BonusContent.ResolveRoot( null, null, "", config ) );
		Assert.IsNull( BonusContent.ResolveRoot( null, null, null, Path.Combine( root, "no-config" ) ) );
		Assert.AreEqual( (Path.GetFullPath( saved ), BonusPathSource.Saved), BonusContent.ResolveRoot( null, null, saved, Path.Combine( root, "no-config" ) ) );
	}

	[TestMethod]
	public void ZipImportKeepsOnlyBonusWadsAndCountsThem()
	{
		var zip = Path.Combine( root, "bonus.zip" );
		var prefix = "Theme Park World Bonus Stuff/Bonus content/";
		WriteZip( zip,
			prefix + "levels/jungle/rides/_snake_1.wad",
			prefix + "levels/hallow/features/_tentacl_14.wad",
			prefix + "levels/jungle/rides/readme.txt",
			prefix + "levels/jungle/rides/_nested/_deep_2.wad",
			prefix + "levels/jungle/rides/_notes.txt",
			"__MACOSX/" + prefix + "levels/jungle/rides/._snake_1.wad",
			"Theme Park World Bonus Stuff/Readme.txt" );
		var config = Path.Combine( root, "config" );

		Assert.AreEqual( 2, BonusContent.Import( zip, config ) );
		var bonus = Path.Combine( config, BonusContent.FolderName );
		Assert.IsTrue( File.Exists( Path.Combine( bonus, "levels", "jungle", "rides", "_snake_1.wad" ) ) );
		Assert.IsTrue( File.Exists( Path.Combine( bonus, "levels", "hallow", "features", "_tentacl_14.wad" ) ) );
		Assert.AreEqual( 2, BonusContent.Validate( bonus ) );
		// Nothing else is copied, and the staging folder is gone.
		CollectionAssert.AreEquivalent( new[] { "levels" }, Directory.GetFileSystemEntries( bonus ).Select( Path.GetFileName ).ToArray() );
		Assert.AreEqual( 1, Directory.GetFileSystemEntries( config ).Length );
	}

	[TestMethod]
	public void ZipSlipIsRefusedAndThePreviousImportSurvives()
	{
		var config = Path.Combine( root, "config" );
		var good = Path.Combine( root, "good.zip" );
		WriteZip( good, "levels/jungle/rides/_snake_1.wad" );
		Assert.AreEqual( 1, BonusContent.Import( good, config ) );

		var evil = Path.Combine( root, "evil.zip" );
		WriteZip( evil, "levels/jungle/rides/_ok_3.wad", "levels/../../../escape/_evil_1.wad" );
		Assert.ThrowsException<InvalidDataException>( () => BonusContent.Import( evil, config ) );
		Assert.IsFalse( File.Exists( Path.Combine( root, "escape", "_evil_1.wad" ) ) );
		Assert.IsFalse( Directory.GetFileSystemEntries( config, "*", SearchOption.AllDirectories ).Any( path => path.EndsWith( "_ok_3.wad", StringComparison.Ordinal ) ) );
		// The earlier import is still there, and no staging folder was left behind.
		Assert.IsTrue( File.Exists( Path.Combine( config, BonusContent.FolderName, "levels", "jungle", "rides", "_snake_1.wad" ) ) );
		Assert.AreEqual( 1, Directory.GetFileSystemEntries( config ).Length );
	}

	[TestMethod]
	public void FolderImportReplacesThePreviousImportOnlyOnSuccess()
	{
		var config = Path.Combine( root, "config" );
		var first = Path.Combine( root, "first" );
		var second = Path.Combine( root, "second" );
		var none = Path.Combine( root, "none" );
		foreach ( var file in new[] { "levels/jungle/rides/_snake_1.wad", "levels/hallow/features/_tentacl_14.wad" } )
			WriteFile( Path.Combine( first, file ) );
		WriteFile( Path.Combine( second, "levels/space/rides/_x_40.wad" ) );
		WriteFile( Path.Combine( none, "levels/space/rides/notes.txt" ) );

		Assert.AreEqual( 2, BonusContent.Import( first, config ) );
		Assert.AreEqual( 1, BonusContent.Import( second, config ) );
		var bonus = Path.Combine( config, BonusContent.FolderName );
		Assert.AreEqual( 1, BonusContent.Validate( bonus ) );
		Assert.IsTrue( File.Exists( Path.Combine( bonus, "levels", "space", "rides", "_x_40.wad" ) ) );
		Assert.IsFalse( File.Exists( Path.Combine( bonus, "levels", "jungle", "rides", "_snake_1.wad" ) ) );

		// Nothing matches: nothing is swapped in.
		Assert.AreEqual( 0, BonusContent.Import( none, config ) );
		Assert.AreEqual( 1, BonusContent.Validate( bonus ) );
		Assert.AreEqual( 0, BonusContent.Validate( Path.Combine( root, "missing" ) ) );
		Assert.AreEqual( 1, Directory.GetFileSystemEntries( config ).Length );
	}

	private static void WriteFile( string path )
	{
		Directory.CreateDirectory( Path.GetDirectoryName( path )! );
		File.WriteAllText( path, "wad" );
	}

	private static void WriteZip( string path, params string[] names )
	{
		using var archive = ZipFile.Open( path, ZipArchiveMode.Create );
		foreach ( var name in names )
		{
			using var writer = new StreamWriter( archive.CreateEntry( name ).Open() );
			writer.Write( "wad" );
		}
	}
}

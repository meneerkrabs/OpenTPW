using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTPW.Mods;
using OpenTPW.UI.Original;

namespace OpenTPW.Tests;

/// <summary>
/// Mods in the browser build (docs/WEB.md, "Mods in the browser"): the shipped mod is found in the program folder, the page's copy of the player's
/// Theme Park Inc files satisfies its input, and the Extensions and mods screen chooses that copy through the page. The page side
/// (<c>opentpw-inputs.js</c>) is covered by <c>tools/test_web_inputs.mjs</c>; these tests run the shared game code, with the page played by the test.
/// </summary>
[TestClass]
[DoNotParallelize]
public class ModsInBrowserTests
{
	private string root = null!;
	private string previousRoot = null!;

	[TestInitialize]
	public void Initialize()
	{
		Log ??= new();
		root = Directory.CreateTempSubdirectory( "opentpw-mods-browser-" ).FullName;
		previousRoot = BrowserModInputs.RootDirectory;
		BrowserModInputs.RootDirectory = Path.Combine( root, "inputs" );
		BrowserModInputs.Reset();
	}

	[TestCleanup]
	public void Cleanup()
	{
		BrowserModInputs.RootDirectory = previousRoot;
		BrowserModInputs.Choose = null;
		BrowserModInputs.Reset();
		OpenTpwExtensions.Activate( ModSet.Defaults( OfficialExtensions.All ) );
		ModContentRoots.Activate( ModSet.Defaults( OfficialExtensions.All ), _ => { } );
		ObjectCatalog.SetContentRoots( Array.Empty<CatalogContentRoot>() ); // the default set's pools root must not leak into the other tests, which expect the base catalog
		Directory.Delete( root, true );
	}

	/// <summary>The least a Theme Park Inc copy holds for the mod: the identity file, per world a ride archive and the shared textures, the global sounds.</summary>
	private static readonly string[] Data =
	{
		"Data/Ver.txt", "Data/levels/arabian/rides/x/x.sam", "Data/levels/science/rides/y/y.sam", "Data/levels/water/rides/z/z.sam",
		"Data/levels/arabian/Sharetex.wad", "Data/levels/science/sharetex.wad", "Data/levels/water/sharetex.wad", "Data/global/sound/cat.sdt",
	};

	/// <summary>What the page does after the player chose a Theme Park Inc folder: stages the files, publishes the copy, reports success.</summary>
	private static void PageCopies( params string[] files )
	{
		BrowserModInputs.Begin( BrowserModInputs.ThemeParkInc );
		foreach ( var file in files )
			BrowserModInputs.AddFile( BrowserModInputs.ThemeParkInc, file, new byte[] { 1, 2, 3 } );
		BrowserModInputs.Publish( BrowserModInputs.ThemeParkInc, files.Length );
		BrowserModInputs.Changed( "" );
	}

	private string ProgramFolder() => TpiRidesFixture.Write( Path.Combine( root, "program" ) );

	// ---- The tree -----------------------------------------------------------------------------------

	[TestMethod]
	public void OnlyDataFilesInsideTheTreeAreAccepted()
	{
		BrowserModInputs.Begin( "theme-park-inc" );
		BrowserModInputs.AddFile( "theme-park-inc", "Data/levels/science/rides/robot.wad", new byte[] { 7 } );
		Assert.AreEqual( 1, File.ReadAllBytes( Path.Combine( BrowserModInputs.RootDirectory, ".staging", "theme-park-inc", "Data", "levels", "science", "rides", "robot.wad" ) ).Length, "files are staged, not live" );
		Assert.IsFalse( Directory.Exists( Path.Combine( BrowserModInputs.RootDirectory, "theme-park-inc" ) ) );
		foreach ( var bad in new[] { "", "Data", "data/Ver.txt", "Data/../x", "Data/./x", "/Data/x", "Data//x", "Data\\x", "../Data/x", "Other/x", "Data/c:/x", new string( 'a', 300 ), "Data/a\nb" } )
			Assert.ThrowsException<ArgumentException>( () => BrowserModInputs.AddFile( "theme-park-inc", bad, new byte[] { 1 } ), $"'{bad}'" );
		Assert.ThrowsException<ArgumentException>( () => BrowserModInputs.AddFile( "theme-park-world", "Data/x", new byte[] { 1 } ) );
		Assert.ThrowsException<ArgumentException>( () => BrowserModInputs.Begin( "../x" ) );
		Assert.IsNull( BrowserModInputs.FolderFor( "theme-park-world" ) );
		Assert.IsNull( BrowserModInputs.FolderFor( new ModInput( "tpw", "original-data", "theme-park-world" ) ) );
		Assert.IsNull( BrowserModInputs.FolderFor( new ModInput( "x", "other", "theme-park-inc" ) ) );
		Assert.AreEqual( Path.Combine( BrowserModInputs.RootDirectory, "theme-park-inc" ), BrowserModInputs.FolderFor( new ModInput( "tpi", "original-data", "theme-park-inc" ) ) );
	}

	[TestMethod]
	public void ANewImportReplacesTheTreeAndTheCountsStayBounded()
	{
		PageCopies( Data.Append( "Data/old.txt" ).ToArray() );
		Assert.AreEqual( Data.Length + 1, BrowserModInputs.Count( "theme-park-inc" ).Files );
		PageCopies( Data );
		Assert.AreEqual( Data.Length, BrowserModInputs.Count( "theme-park-inc" ).Files );
		Assert.IsFalse( File.Exists( Path.Combine( BrowserModInputs.RootDirectory, "theme-park-inc", "Data", "old.txt" ) ), "two installs never mix" );
		Assert.AreEqual( 2, BrowserModInputs.Revision );
		BrowserModInputs.Changed( "that went wrong" );
		Assert.AreEqual( "that went wrong", BrowserModInputs.Message );
		BrowserModInputs.Changed( "" );
		Assert.IsNull( BrowserModInputs.Message );
	}

	[TestMethod]
	public void AHalfCopiedTreeIsNeverLiveAndAFailedImportLeavesNothing()
	{
		var folder = BrowserModInputs.FolderFor( "theme-park-inc" )!;
		PageCopies( Data );
		Assert.IsTrue( BrowserModInputs.IsComplete( "theme-park-inc" ) );
		// a new import takes the live copy away at once and stages beside it: until it is published nothing is live, and the defaults see nothing
		BrowserModInputs.Begin( "theme-park-inc" );
		BrowserModInputs.AddFile( "theme-park-inc", Data[0], new byte[] { 1 } );
		Assert.IsFalse( Directory.Exists( folder ), "the partial copy is not at the live path" );
		Assert.IsFalse( BrowserModInputs.IsComplete( "theme-park-inc" ) );
		var discovered = TpiRidesFixture.Discover( ProgramFolder() );
		Assert.IsNull( BrowserModInputs.WithDefaults( ModSettings.Default, discovered ).InputPath( TpiRidesFixture.Id, "tpi" ) );
		// publishing too early is refused and changes nothing
		var early = Assert.ThrowsException<InvalidDataException>( () => BrowserModInputs.Publish( "theme-park-inc", Data.Length ) );
		StringAssert.Contains( early.Message, "incomplete" );
		Assert.IsFalse( Directory.Exists( folder ) );
		BrowserModInputs.Abort( "theme-park-inc" );
		Assert.IsFalse( Directory.Exists( Path.Combine( BrowserModInputs.RootDirectory, ".staging" ) ) || Directory.Exists( folder ), "an aborted import leaves neither tree" );
		Assert.ThrowsException<InvalidOperationException>( () => BrowserModInputs.AddFile( "theme-park-inc", Data[0], new byte[] { 1 } ), "no import is open" );
	}

	[TestMethod]
	public void OnlyACompleteThemeParkIncCopyIsPublished()
	{
		void Publishes( string[] files, string problem )
		{
			BrowserModInputs.Begin( "theme-park-inc" );
			foreach ( var file in files )
				BrowserModInputs.AddFile( "theme-park-inc", file, new byte[] { 1 } );
			var exception = Assert.ThrowsException<InvalidDataException>( () => BrowserModInputs.Publish( "theme-park-inc", files.Length ) );
			StringAssert.Contains( exception.Message, problem );
			Assert.IsFalse( BrowserModInputs.IsComplete( "theme-park-inc" ) );
			BrowserModInputs.Abort( "theme-park-inc" );
		}
		// not Theme Park Inc data at all, or Theme Park World's
		Publishes( new[] { "Data/ui.wad" }, "not recognised" );
		Publishes( Data.Append( "Data/levels/jungle/terrain.wad" ).ToArray(), "not recognised" );
		// recognised by structure, but short of what the mod reads
		Publishes( new[] { "Data/Ver.txt", "Data/levels/arabian/rides/x/x.sam" }, "arabian's shared textures" );
		Publishes( Data.Where( file => !file.Contains( "water/sharetex" ) ).ToArray(), "water's shared textures" );
		Publishes( Data.Where( file => !file.Contains( "global/sound" ) ).ToArray(), "global sounds" );
		// the right count with the wrong files, and a count that does not match what arrived
		BrowserModInputs.Begin( "theme-park-inc" );
		BrowserModInputs.AddFile( "theme-park-inc", "Data/Ver.txt", new byte[] { 1 } );
		Assert.ThrowsException<InvalidDataException>( () => BrowserModInputs.Publish( "theme-park-inc", 2 ) );
		BrowserModInputs.Abort( "theme-park-inc" );
		PageCopies( Data );
		Assert.IsTrue( File.Exists( Path.Combine( BrowserModInputs.FolderFor( "theme-park-inc" )!, BrowserModInputs.CompleteMarker ) ) );
		Assert.IsFalse( Directory.Exists( Path.Combine( BrowserModInputs.RootDirectory, ".staging", "theme-park-inc" ) ) );
	}

	[TestMethod]
	public void AnInputWithoutASavedFolderUsesTheEditionsTreeOnlyWhenItExists()
	{
		var discovered = TpiRidesFixture.Discover( ProgramFolder() );
		Assert.IsNull( BrowserModInputs.WithDefaults( ModSettings.Default, discovered ).InputPath( TpiRidesFixture.Id, "tpi" ), "no tree yet" );
		PageCopies( Data );
		var settings = BrowserModInputs.WithDefaults( ModSettings.Default, discovered );
		Assert.AreEqual( Path.Combine( BrowserModInputs.RootDirectory, "theme-park-inc" ), settings.InputPath( TpiRidesFixture.Id, "tpi" ) );
		var chosen = ModSettings.Default.WithInput( TpiRidesFixture.Id, "tpi", "/elsewhere" );
		Assert.AreEqual( "/elsewhere", BrowserModInputs.WithDefaults( chosen, discovered ).InputPath( TpiRidesFixture.Id, "tpi" ), "a saved choice stays" );
	}

	// ---- Startup ------------------------------------------------------------------------------------

	[TestMethod]
	public void TheBrowserFindsTheShippedModAndItIsOffUntilSwitchedOn()
	{
		var program = ProgramFolder();
		var settingsPath = Path.Combine( root, "config", "mods.json" );
		var set = ModStartup.InitializeBrowser( _ => { }, message => Assert.Fail( message ), program, settingsPath );
		Assert.AreEqual( 0, set.Mods.Count );
		Assert.IsTrue( ModStartup.Discovered.Any( mod => mod.Id == TpiRidesFixture.Id && mod.IsValid && mod.Source == ModSource.Program ) );
		Assert.IsFalse( set.IsEnabled( OfficialExtensions.CrossEditionObjects.Id ) );
		// pools is on by default, so its own content root (the changing rooms, embedded in the browser build) is the only root
		Assert.IsTrue( ObjectCatalog.ContentRoots.All( contentRoot => contentRoot.ModId == OfficialExtensions.Pools.Id || contentRoot.ModId == OfficialExtensions.Umbrellas.Id || contentRoot.ModId == OfficialExtensions.Winter.Id || contentRoot.ModId == OfficialExtensions.Evening.Id ) );
		Assert.IsTrue( set.IsEnabled( OfficialExtensions.Pools.Id ), "pools is on in the browser with no saved choice" );
		Assert.IsTrue( ObjectCatalog.ContentRoots.Sum( contentRoot => contentRoot.Objects.Count ) >= 4, "the pools objects (changing rooms and extras) are in the catalogs" );
	}

	[TestMethod]
	public void SavedChoicesAndThePagesFilesSwitchTheShippedModOn()
	{
		var program = ProgramFolder();
		var settingsPath = Path.Combine( root, "config", "mods.json" );
		Directory.CreateDirectory( Path.GetDirectoryName( settingsPath )! );
		new ModSettings { Enabled = new Dictionary<string, bool> { [OfficialExtensions.CrossEditionObjects.Id] = true, [TpiRidesFixture.Id] = true } }.Save( settingsPath );

		// no files from the page: refused for its input, said in the log
		var warnings = new List<string>();
		var refused = ModStartup.InitializeBrowser( _ => { }, warnings.Add, program, settingsPath );
		Assert.AreEqual( 0, refused.Mods.Count );
		Assert.AreEqual( ModRefusalKind.MissingInput, refused.Refused.Single( refusal => refusal.Id == TpiRidesFixture.Id ).Kind );

		// the page copied the player's Theme Park Inc files: the input is satisfied without a saved folder
		PageCopies( Data );
		var set = ModStartup.InitializeBrowser( _ => { }, message => Assert.Fail( message ), program, settingsPath );
		Assert.AreEqual( 0, set.Refused.Count, string.Join( "; ", set.Refused.Select( refusal => refusal.Reason ) ) );
		var mod = set.Mods.Single();
		Assert.AreEqual( TpiRidesFixture.Id, mod.Id );
		Assert.AreEqual( Path.Combine( BrowserModInputs.RootDirectory, "theme-park-inc", "Data" ), mod.InputDirectories["tpi"] );
		Assert.IsTrue( BrowserModInputs.IsLocked, "an active mod reads the tree: it cannot be replaced while the game runs" );
		Assert.ThrowsException<InvalidOperationException>( () => BrowserModInputs.Begin( "theme-park-inc" ) );
		Assert.ThrowsException<InvalidOperationException>( () => BrowserModInputs.AddFile( "theme-park-inc", "Data/x", new byte[] { 1 } ) );
		Assert.AreEqual( 183, ObjectCatalog.ContentRoots.Where( contentRoot => contentRoot.ModId != OfficialExtensions.Pools.Id && contentRoot.ModId != OfficialExtensions.Umbrellas.Id && contentRoot.ModId != OfficialExtensions.Winter.Id && contentRoot.ModId != OfficialExtensions.Evening.Id ).Sum( contentRoot => contentRoot.Objects.Count ), "the shipped objects reach the catalogs" );
	}

	// ---- The Extensions and mods screen --------------------------------------------------------------

	private ModsServices Services( string program, bool browser = true ) => new()
	{
		Discovered = () => TpiRidesFixture.Discover( program ),
		SettingsPath = Path.Combine( root, "config", "mods.json" ),
		Active = () => ModSet.Defaults( OfficialExtensions.All ),
		OpenTpwVersion = null,
		Edition = () => GameEdition.ThemeParkWorld,
		Browser = browser,
		Inspect = ( path, _ ) => Task.FromResult( new InstallationDiscoveryResult( new[] { GameInstallation.Inspect( path ) }, false, null ) ),
	};

	private static UiContext Context() => new( OriginalUiTests.FakeStrings(), null!, new UiModels( name => throw new FileNotFoundException( name ) ) ) { Canvas = new UiCanvas( 1024, 768 ) };

	private static void Pump( UiScreen screen, Func<bool>? done = null )
	{
		for ( var attempt = 0; attempt < 400; attempt++ )
		{
			screen.Updating!( Context() );
			if ( done == null || done() )
				return;
			Thread.Sleep( 5 );
		}
		Assert.Fail( "The screen did not settle." );
	}

	private static string Trailing( UiScreen screen, string list, int index ) => ((UiScrollList)screen.Find( list )!).Trailing!( index );
	private static string Label( UiScreen screen, string id ) => ((UiLabel)screen.Find( id )!).Text();
	private static void Click( UiScreen screen, string id ) => ((UiButton)screen.Find( id )!).Clicked!();
	private static string Text( OpenTpwText key ) => OriginalUiTests.FakeStrings().Extra( key );

	[TestMethod]
	public void TheScreenListsTheShippedModAndTakesTheFilesTheGivenPageCopies()
	{
		var program = ProgramFolder();
		var services = Services( program );
		var asked = new List<string>();
		BrowserModInputs.Choose = edition =>
		{
			asked.Add( edition );
			PageCopies( Data );
		};
		var stack = new UiScreenStack();
		var screen = ModsScreen.Create( stack, OriginalUiTests.FakeStrings(), services );
		stack.Push( screen );
		var mods = (UiScrollList)screen.Find( "mods" )!;
		CollectionAssert.AreEqual( new[] { "Theme Park Inc rides  1.0.0" }, mods.Rows().ToArray() );
		Assert.AreEqual( Text( OpenTpwText.ModOff ), Trailing( screen, "mods", 0 ) );
		// switch the extension on first: the mod needs it
		((UiScrollList)screen.Find( "extensions" )!).Select( 1 );
		Click( screen, "extensionsOn" );
		mods.Select( 0 );
		Pump( screen );
		StringAssert.Contains( Label( screen, "details" ), "opentpw.tpi-rides" );
		StringAssert.Contains( Label( screen, "details" ), "opentpw.cross-edition-objects" );
		Assert.IsTrue( screen.Find( "inputChange" )!.Visible );
		Assert.IsFalse( screen.Find( "inputType" )!.Visible, "the browser has no typed paths" );
		Assert.IsFalse( screen.Find( "inputRemove" )!.Visible );

		// switch the mod on: it needs its input
		Click( screen, "modsOn" );
		Pump( screen, () => Trailing( screen, "mods", 0 ) == Text( OpenTpwText.ModMissingInput ) );

		// "Change..." asks the page; the page copies and reports; the screen takes the tree as the input and checks it
		Click( screen, "inputChange" );
		CollectionAssert.AreEqual( new[] { "theme-park-inc" }, asked );
		Pump( screen, () => Trailing( screen, "mods", 0 ) == Text( OpenTpwText.ModOn ) );
		Assert.AreEqual( Text( OpenTpwText.ModInputFoundTpiStructure ), Label( screen, "inputStatus" ) );
		StringAssert.Contains( Label( screen, "inputPath" ), Path.Combine( BrowserModInputs.RootDirectory, "theme-park-inc" ) );

		Click( screen, "ok" );
		var saved = ModSettings.Load( services.SettingsPath, new List<string>() );
		Assert.IsTrue( saved.IsEnabled( TpiRidesFixture.Id, false ) );
		Assert.IsTrue( saved.IsEnabled( OfficialExtensions.CrossEditionObjects.Id, false ) );
		Assert.AreEqual( Path.Combine( BrowserModInputs.RootDirectory, "theme-park-inc" ), saved.InputPath( TpiRidesFixture.Id, "tpi" ) );
		stack.Clear();
	}

	[TestMethod]
	public void AFailedCopyShowsThePagesMessageAndAnInUseTreeIsNotReplaced()
	{
		var program = ProgramFolder();
		var services = Services( program );
		BrowserModInputs.Choose = _ => BrowserModInputs.Changed( "No Theme Park Inc data in that folder." );
		var stack = new UiScreenStack();
		var screen = ModsScreen.Create( stack, OriginalUiTests.FakeStrings(), services );
		stack.Push( screen );
		((UiScrollList)screen.Find( "mods" )!).Select( 0 );
		Pump( screen );
		Click( screen, "inputChange" );
		Pump( screen, () => Label( screen, "inputStatus" ).Contains( "No Theme Park Inc data" ) );
		StringAssert.EndsWith( Label( screen, "inputPath" ), Text( OpenTpwText.NoFolder ), "nothing was chosen" );

		// a tree an active mod reads cannot be replaced while the game runs: the screen says to reload the page and does not ask it
		PageCopies( Data );
		var set = TpiRidesFixture.Resolve( TpiRidesFixture.Settings( Path.Combine( BrowserModInputs.RootDirectory, "theme-park-inc" ) ), program );
		BrowserModInputs.LockInUse( set );
		var asked = 0;
		BrowserModInputs.Choose = _ => asked++;
		Pump( screen );
		Click( screen, "inputChange" );
		Assert.AreEqual( 0, asked );
		Assert.AreEqual( Text( OpenTpwText.ModInputBrowserLocked ), Label( screen, "inputStatus" ) );
		stack.Clear();
	}

	[TestMethod]
	public void TheDesktopScreenStillOffersTypedPathsAndRemove()
	{
		var program = ProgramFolder();
		var stack = new UiScreenStack();
		var screen = ModsScreen.Create( stack, OriginalUiTests.FakeStrings(), Services( program, browser: false ) );
		stack.Push( screen );
		((UiScrollList)screen.Find( "mods" )!).Select( 0 );
		Pump( screen );
		Assert.IsTrue( screen.Find( "inputType" )!.Visible );
		Assert.IsTrue( screen.Find( "inputRemove" )!.Visible );
		stack.Clear();
	}
}

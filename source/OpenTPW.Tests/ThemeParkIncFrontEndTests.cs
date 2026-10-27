using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTPW.FrontEnd;
using OpenTPW.UI.Original;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using N = System.Numerics;

namespace OpenTPW.Tests;

/// <summary>
/// The Theme Park Inc front end, phase 1 (docs/THEME-PARK-INC.md): the generated Personnel File and quit-confirm layout tables, the
/// string-id map, the capability flag and start-up routing on synthetic data, and over the retail corpus when
/// <c>OPENTPW_TPI_PATH</c> names it: every model of the tables is in Theme Park Inc's <c>ui.wad</c>, its strings and fonts resolve,
/// and the screen builds and draws headless. Nothing here compares with how the original game looks.
/// </summary>
[TestClass]
[DoNotParallelize]
public class ThemeParkIncFrontEndTests
{
	private BaseFileSystem? previous;

	[TestInitialize]
	public void Initialize() => previous = FileSystem;

	[TestCleanup]
	public void Cleanup()
	{
		FileSystem = previous!;
		GameEditions.Select( GameEdition.ThemeParkWorld );
		ResetTextureListings();
	}

	/// <summary>The texture directory listing is cached for the process; another test's data folder must not leak into this one's (or the reverse).</summary>
	private static void ResetTextureListings() => UiImages.ResetListings();

	private string UseTpiData()
	{
		var path = Environment.GetEnvironmentVariable( "OPENTPW_TPI_PATH" );
		if ( string.IsNullOrWhiteSpace( path ) || !Directory.Exists( path ) )
			Assert.Inconclusive( "Set OPENTPW_TPI_PATH to a Theme Park Inc installation or its Data folder." );
		var data = Directory.EnumerateDirectories( path! ).FirstOrDefault( directory => string.Equals( Path.GetFileName( directory ), "data", StringComparison.OrdinalIgnoreCase ) ) ?? path!;
		FileSystem = new BaseFileSystem( data );
		FileSystem.RegisterArchiveHandler<WadArchive>( ".wad" );
		GameEditions.Select( GameEdition.ThemeParkInc );
		ResetTextureListings();
		return data;
	}

	private static UiContext FakeContext( UiStringTable strings, int width = 1920, int height = 1080 )
	{
		var font = new FontAtlas( FontAtlasTests.CreateFont( 8, new FontAtlasTests.SyntheticGlyph( 'A', 2, 2, 0, 0, 3 ) ) );
		return new UiContext( strings, new UiFonts( _ => font ), new UiModels( name => throw new FileNotFoundException( name ) ) ) { Canvas = new UiCanvas( width, height ), ResolveTexture = _ => null };
	}

	private sealed class Calls
	{
		public readonly List<string> Worlds = new();
		public int Quits;
		public ThemeParkIncFrontEndHost Host => new() { EnterWorld = world => Worlds.Add( world ), Quit = () => Quits++ };
	}

	private static UiElement Element( ThemeParkIncFrontEnd frontEnd, string id ) => frontEnd.Screen.Find( id ) ?? throw new AssertFailedException( $"The Personnel File has no element {id}." );

	private static void Click( ThemeParkIncFrontEnd frontEnd, UiContext context, string id )
	{
		var element = frontEnd.Stack.Top!.Find( id ) ?? throw new AssertFailedException( $"The top screen has no element {id}." );
		var centre = element.ScreenRect( context.Canvas ).Center;
		frontEnd.Update( context, UiInput.Idle( centre ) );
		frontEnd.Update( context, UiInput.Click( centre ) );
	}

	// ---- Generated layout tables (synthetic: the tables hold derived rectangles, no game data) ----------------

	[TestMethod]
	public void ThePersonnelFileTableIsTheStartMenuWindowWithItsControls()
	{
		var table = ThemeParkIncLayoutTables.PersonnelFile;
		Assert.AreEqual( 0x9C0FBC, table.Address );
		Assert.AreEqual( 39, table.All.Count() );
		Assert.AreEqual( (0xC738u, "!window_s", new UiRect( 273, 72, 1502, 1392 )), (table.Root.Id, table.Root.Model, table.Root.Rect) );
		Assert.AreEqual( 4, table.All.Count( node => node.Model == "filetab" ), "four player slots" );
		var enter = table.Control( 0xC754 );
		Assert.AreEqual( ("filebut", new UiRect( 440, 1001, 380, 102 ), 287), (enter.Model, enter.Rect, enter.Help) );
		Assert.AreEqual( 288, table.Control( 0xC755 ).Help, "Delete Player" );
		Assert.AreEqual( new UiRect( 850, 1001, 380, 102 ), table.Control( 0xC759 ).Rect, "Load A Game" );
		Assert.AreEqual( 289, table.Control( 0xC75B ).Help, "Quit Game" );
		Assert.AreEqual( 290, table.Control( 0xC73A ).Help, "Coaster Design Kit" );
		Assert.AreEqual( ("filebox", "filebutok"), (table.Control( 0xC756 ).Model, table.Control( 0xC757 ).Model) );
		Assert.AreEqual( 5, table.Control( 0xC758 ).Type, "the name field is a text field (type 5)" );
		Assert.AreEqual( "mugshot", table.Control( 0xC75E ).Model );
	}

	[TestMethod]
	public void TheQuitConfirmTableHasTheWindowAndTwoButtonsWithoutModels()
	{
		var table = ThemeParkIncLayoutTables.QuitConfirm;
		Assert.AreEqual( 0x9B18BC, table.Address );
		Assert.AreEqual( (0xEA60u, "!window", new UiRect( 499, 232, 1019, 598 )), (table.Root.Id, table.Root.Model, table.Root.Rect) );
		Assert.IsNull( table.Control( 0xEA61 ).Model, "the caller's mode word chooses b_cancel" );
		Assert.IsNull( table.Control( 0xEA62 ).Model, "the caller's mode word chooses b_okay" );
		Assert.AreEqual( (3, 0), (table.Control( 0xEA61 ).Help, table.Control( 0xEA62 ).Help) );
		Assert.AreEqual( "b_close", table.Control( 0xEA63 ).Model );
		Assert.IsTrue( table.Control( 0xEA62 ).Rect.X < table.Control( 0xEA61 ).Rect.X, "the okay button is the left one" );
	}

	// ---- String ids and capability policy ---------------------------------------------------------------------

	[TestMethod]
	public void StringIdsAreTheFilesOwnNumbering()
	{
		var strings = OriginalUiTests.FakeStrings();
		Assert.AreEqual( "ui19", strings.Entry( ThemeParkIncText.PersonnelFileTitle ) );
		Assert.AreEqual( "ui34", strings.Entry( ThemeParkIncText.EnterGame ) );
		Assert.AreEqual( "", strings.Entry( 99999 ) );
		CollectionAssert.AreEqual( new[] { 19, 20, 21, 22, 23, 24, 25, 26, 28, 29, 30, 31, 32, 33, 34, 35, 773 },
			new[] { ThemeParkIncText.PersonnelFileTitle, ThemeParkIncText.NewPlayer, ThemeParkIncText.Money, ThemeParkIncText.GoldenTickets, ThemeParkIncText.ShareholdingPercent,
				ThemeParkIncText.SharesOwned, ThemeParkIncText.SharePrice, ThemeParkIncText.PortfolioValue, ThemeParkIncText.ZoneNotYetOpened, ThemeParkIncText.CurrentObjective,
				ThemeParkIncText.QuitGame, ThemeParkIncText.CoasterDesignKit, ThemeParkIncText.EnterNameHere, ThemeParkIncText.DeletePlayer, ThemeParkIncText.EnterGame,
				ThemeParkIncText.LoadGame, ThemeParkIncText.QuitConfirmation } );
		Assert.AreEqual( (0, 1, 3, -1), (ThemeParkIncText.ThemeNameIndex( "science" ), ThemeParkIncText.ThemeNameIndex( "water" ), ThemeParkIncText.ThemeNameIndex( "ARABIAN" ), ThemeParkIncText.ThemeNameIndex( "jungle" )) );
	}

	[TestMethod]
	public void TheFrontEndCapabilityBelongsToTheSliceAndLeavesTheTpwLobbyRefused()
	{
		Assert.IsFalse( EditionCapabilities.ForThemeParkWorld().PersonnelFileFrontEnd, "Theme Park World has its own lobby front end" );
		var slice = EditionCapabilities.ForThemeParkIncSlice();
		Assert.IsTrue( slice.PersonnelFileFrontEnd );
		Assert.IsFalse( slice.FrontEnd, "the original-style lobby stays refused for Theme Park Inc" );
		GameEditions.Select( GameEdition.ThemeParkInc );
		Assert.IsTrue( GameEditions.Capabilities.PersonnelFileFrontEnd );
		GameEditions.Select( GameEdition.ThemeParkWorld );
		Assert.IsFalse( GameEditions.Capabilities.PersonnelFileFrontEnd );
	}

	[TestMethod]
	public void AStartWithoutAWorldOpensThePersonnelFileOnlyForPlainOptions()
	{
		GameEditions.Select( GameEdition.ThemeParkInc );
		foreach ( var accepted in new[] { Array.Empty<string>(), new[] { "--front-end" }, new[] { "--game-path", "x", "--silent", "--mute", "--no-intro" }, new[] { "--front-end", "--language", "Dutch" },
			new[] { "--front-end", "--windowed", "--resolution", "1280x720", "--upscale", "linear", "--render-scale", "67", "--ui-scale", "2", "--no-autorun" },
			new[] { "--front-end", "--fullscreen" }, new[] { "--front-end", "--fullscreen-exclusive" } } )
			Assert.IsNull( GameEditions.ViewerRefusal( accepted ), string.Join( " ", accepted ) );
		foreach ( var refused in new[]
		{
			new[] { "--front-end", "--smoke-test" }, new[] { "--front-end", "--load-original-level", "science" }, new[] { "--capture-world", "a.png" }, new[] { "--sandbox" },
			new[] { "--m3-gate" }, new[] { "--setup" }, new[] { "--play-movie", "intro" }, new[] { "--build-texture-pack" }, new[] { "--tpi-smoke-test" }
		} )
			Assert.IsNotNull( GameEditions.ViewerRefusal( refused ), string.Join( " ", refused ) );
		Assert.IsNull( GameEditions.ViewerRefusal( new[] { "--load-original-level", "water" } ), "a world still starts the slice directly" );
		GameEditions.Select( GameEdition.ThemeParkWorld );
		Assert.IsNotNull( GameEditions.ViewerRefusal( Array.Empty<string>() ), "the capability is the slice's: nothing opens it for other data" );
	}

	// ---- The screen, headless ----------------------------------------------------------------------------------

	[TestMethod]
	public void EnterGameStartsScienceByDefault()
	{
		var calls = new Calls();
		var frontEnd = new ThemeParkIncFrontEnd( OriginalUiTests.FakeStrings(), calls.Host );
		var context = FakeContext( OriginalUiTests.FakeStrings() );
		Assert.AreEqual( "science", frontEnd.Zone );
		Assert.AreEqual( "enterGame", frontEnd.Screen.Focused!.Id );
		frontEnd.Update( context, UiInput.Key( UiKeys.Accept ) );
		CollectionAssert.AreEqual( new[] { "science" }, calls.Worlds );
		Click( frontEnd, context, "enterGame" );
		CollectionAssert.AreEqual( new[] { "science", "science" }, calls.Worlds );
	}

	[DataTestMethod]
	[DataRow( "water" )]
	[DataRow( "arabian" )]
	[DataRow( "science" )]
	public void TheZoneChooserRoutesEnterGameToTheChosenZone( string zone )
	{
		var calls = new Calls();
		var frontEnd = new ThemeParkIncFrontEnd( OriginalUiTests.FakeStrings(), calls.Host );
		var context = FakeContext( OriginalUiTests.FakeStrings() );
		Click( frontEnd, context, "zone_" + zone );
		Assert.AreEqual( zone, frontEnd.Zone );
		Assert.IsTrue( ((UiButton)Element( frontEnd, "zone_" + zone )).Selected!() );
		foreach ( var other in ThemeParkIncFrontEnd.Zones.Where( name => name != zone ) )
			Assert.IsFalse( ((UiButton)Element( frontEnd, "zone_" + other )).Selected!() );
		Click( frontEnd, context, "enterGame" );
		CollectionAssert.AreEqual( new[] { zone }, calls.Worlds );
		Assert.AreEqual( 0, calls.Quits );
	}

	[TestMethod]
	public void TheTitleAndTheQuitCaptionCarryTheTpiCopyAlpha()
	{
		var frontEnd = new ThemeParkIncFrontEnd( OriginalUiTests.FakeStrings(), new Calls().Host );
		var title = (UiLabel)Element( frontEnd, "title" );
		Assert.IsTrue( title.Shadow );
		Assert.AreEqual( (byte)0xC8, title.ShadowAlpha, "0x515fe0: the copy's colour alpha" );
		frontEnd.RequestQuit();
		var caption = (UiLabel)frontEnd.Stack.Screens.Select( screen => screen.Find( "caption" ) ).OfType<UiLabel>().Single();
		Assert.IsTrue( caption.Shadow );
		Assert.AreEqual( (byte)0xC8, caption.ShadowAlpha );
	}

	[TestMethod]
	public void ZonesOutsideTheChooserAreRefused()
	{
		var frontEnd = new ThemeParkIncFrontEnd( OriginalUiTests.FakeStrings(), new Calls().Host );
		Assert.ThrowsException<ArgumentException>( () => frontEnd.SelectZone( "jungle" ) );
		Assert.ThrowsException<ArgumentException>( () => frontEnd.SelectZone( "wintro" ) );
		Assert.AreEqual( "science", frontEnd.Zone );
	}

	[TestMethod]
	public void LoadDeleteDesignKitAndTheNameBoxStayInactive()
	{
		var calls = new Calls();
		var frontEnd = new ThemeParkIncFrontEnd( OriginalUiTests.FakeStrings(), calls.Host );
		var context = FakeContext( OriginalUiTests.FakeStrings() );
		foreach ( var id in new[] { "loadGame", "deletePlayer", "designKit", "nameOk", "slot2", "slot3", "slot4" } )
		{
			var button = (UiButton)Element( frontEnd, id );
			Assert.IsFalse( button.Enabled, id );
			Assert.IsFalse( button.Focusable, id );
			Click( frontEnd, context, id );
		}
		Assert.AreEqual( 0, calls.Worlds.Count + calls.Quits );
		Assert.AreEqual( 1, frontEnd.Stack.Screens.Count, "no dialog opened" );
		Click( frontEnd, context, "slot1" );
		var slot = (UiButton)Element( frontEnd, "slot1" );
		Assert.IsTrue( slot.Selected!(), "the one OpenTPW player is the shown slot" );
		// the frame that is drawn: the selected player's slot shows its down frame, the free ones the disabled frame, and neither can be activated
		Assert.AreEqual( UiButton.DownFrame, slot.StateFrame( focused: false, pressed: false ) );
		Assert.AreEqual( UiButton.DownFrame, slot.StateFrame( focused: true, pressed: false ), "no hover highlight on an inert slot" );
		Assert.AreEqual( UiButton.DisabledFrame, ((UiButton)Element( frontEnd, "slot2" )).StateFrame( focused: false, pressed: false ) );
		Assert.AreNotEqual( slot, frontEnd.Screen.Focused );
	}

	[TestMethod]
	public void QuitGameAsksFirstAndOnlyTheTickQuits()
	{
		var calls = new Calls();
		var strings = OriginalUiTests.FakeStrings();
		var frontEnd = new ThemeParkIncFrontEnd( strings, calls.Host );
		var context = FakeContext( strings );
		Click( frontEnd, context, "quit" );
		Assert.AreEqual( "tpiQuit", frontEnd.Stack.Top!.Name );
		Assert.AreEqual( 0, calls.Quits, "the confirmation comes first" );
		frontEnd.RequestQuit();
		Assert.AreEqual( 2, frontEnd.Stack.Screens.Count, "a second request does not stack another dialog" );
		Assert.AreEqual( "cancel", frontEnd.Stack.Top.Focused!.Id, "the safe button has the focus" );
		var table = ThemeParkIncLayoutTables.QuitConfirm;
		Assert.AreEqual( table.Control( 0xEA62 ).Rect, frontEnd.Stack.Top.Find( "ok" )!.Bounds );
		Assert.AreEqual( table.Control( 0xEA61 ).Rect, frontEnd.Stack.Top.Find( "cancel" )!.Bounds );
		Click( frontEnd, context, "cancel" );
		Assert.AreEqual( (1, 0), (frontEnd.Stack.Screens.Count, calls.Quits) );
		frontEnd.RequestQuit();
		frontEnd.Update( context, UiInput.Key( UiKeys.Back ) );
		Assert.AreEqual( (1, 0), (frontEnd.Stack.Screens.Count, calls.Quits), "Escape leaves the dialog" );
		frontEnd.Update( context, UiInput.Key( UiKeys.Back ) );
		Assert.AreEqual( "tpiQuit", frontEnd.Stack.Top!.Name, "Escape on the page asks the same question" );
		Click( frontEnd, context, "ok" );
		Assert.AreEqual( (1, 1), (frontEnd.Stack.Screens.Count, calls.Quits) );
	}

	[TestMethod]
	public void TheQuitTextSplitsAtItsBlankLine()
	{
		var (caption, body) = UiDialogs.SplitCaption( "QUIT GAME\n\nAre you sure you want to quit the game and return to Windows?" );
		Assert.AreEqual( ("QUIT GAME", "Are you sure you want to quit the game and return to Windows?"), (caption, body) );
	}

	[TestMethod]
	public void ThePageDrawsHeadlessWithoutModelsOrFonts()
	{
		var strings = OriginalUiTests.FakeStrings();
		var frontEnd = new ThemeParkIncFrontEnd( strings, new Calls().Host );
		var context = FakeContext( strings );
		frontEnd.Update( context, UiInput.Idle( new N.Vector2( 960, 540 ) ) );
		context.Batch.Clear();
		frontEnd.Draw( context );
		frontEnd.RequestQuit();
		frontEnd.Draw( context );
		Assert.IsTrue( context.Batch.Draws.Count > 0, "the fallback rectangles and text were drawn" );
	}

	// ---- Over the retail corpus -------------------------------------------------------------------------------

	private static IEnumerable<string> ModelNames() => ThemeParkIncLayoutTables.PersonnelFile.All.Concat( ThemeParkIncLayoutTables.QuitConfirm.All )
		.SelectMany( node => new[] { node.Model, node.Model2 } ).Where( name => name != null ).Select( name => name! )
		.Concat( new[] { "b_okay", "b_cancel" } ).Distinct( StringComparer.Ordinal );

	[TestMethod]
	public void EveryTableModelIsATpiUiWadRootNameAndLoads()
	{
		UseTpiData();
		var models = new UiModels();
		var names = ModelNames().ToArray();
		Assert.IsTrue( names.Length >= 14, "the tables name their models" );
		foreach ( var name in names )
		{
			var byRoot = models.GetByRootName( name );
			Assert.AreEqual( name, byRoot.RootNodeName, name );
			Assert.AreEqual( UiModels.RootNameKey( name ), byRoot.DrawingKey, name );
			var drawable = models.Get( name );
			Assert.IsNotNull( drawable, $"UiModel {name} loads" );
			Assert.IsTrue( drawable!.Frames.Count >= 1 && drawable.Frames[0].Parts.Count >= 1, $"{name} has a drawable frame" );
		}
	}

	[TestMethod]
	public void TheFrameTexturesOfTheTableModelsResolveInTheTpiUiWad()
	{
		UseTpiData();
		var models = new UiModels();
		var missing = new List<string>();
		foreach ( var name in ModelNames() )
		{
			var model = models.Get( name )!;
			for ( var frame = 0; frame < Math.Min( model.Frames.Count, UiButton.DownFrame + 1 ); frame++ )
				foreach ( var part in model.Frames[frame].Parts.Where( part => part.TextureName.Length > 0 ) )
					if ( UiImages.Resolve( part.TextureName ) == null )
						missing.Add( $"{name}[{frame}] {part.TextureName}" );
		}
		Assert.AreEqual( 0, missing.Count, "textures the data lacks: " + string.Join( ", ", missing ) );
	}

	[DataTestMethod]
	[DataRow( "English", "TPi Personnel File", "New Player", "Enter Game", "Load A Game", "Delete Player", "Quit Game", "Enter name here", "Coaster Design Kit", "Land of Invention", "Polar Zone", "Arabian Nights" )]
	[DataRow( "Dutch", "TPi personeelsdossier", "Nieuwe speler", "Spel starten", "Een spel laden", "Speler verwijderen", "Spel afsluiten", "Voer hier een naam in", "Achtbaan ontwerppakket", "Land van Ontdekkingen", "Poolzone", "1001 Nacht" )]
	public void TpiStringIdsResolveToTheExpectedText( string language, string title, string newPlayer, string enter, string load, string delete, string quit, string prompt, string kit, string science, string water, string arabian )
	{
		var data = UseTpiData();
		var strings = UiStringTable.Load( GameLanguage.Resolve( data, language, null ) );
		Assert.AreEqual( title, strings.Entry( ThemeParkIncText.PersonnelFileTitle ) );
		Assert.AreEqual( newPlayer, strings.Entry( ThemeParkIncText.NewPlayer ) );
		Assert.AreEqual( enter, strings.Entry( ThemeParkIncText.EnterGame ) );
		Assert.AreEqual( load, strings.Entry( ThemeParkIncText.LoadGame ) );
		Assert.AreEqual( delete, strings.Entry( ThemeParkIncText.DeletePlayer ) );
		Assert.AreEqual( quit, strings.Entry( ThemeParkIncText.QuitGame ) );
		Assert.AreEqual( prompt, strings.Entry( ThemeParkIncText.EnterNameHere ) );
		Assert.AreEqual( kit, strings.Entry( ThemeParkIncText.CoasterDesignKit ) );
		Assert.AreEqual( science, strings.Theme( ThemeParkIncText.ThemeNameIndex( "science" ) ) );
		Assert.AreEqual( water, strings.Theme( ThemeParkIncText.ThemeNameIndex( "water" ) ) );
		Assert.AreEqual( arabian, strings.Theme( ThemeParkIncText.ThemeNameIndex( "arabian" ) ) );
		foreach ( var id in new[] { ThemeParkIncText.Money, ThemeParkIncText.GoldenTickets, ThemeParkIncText.ShareholdingPercent, ThemeParkIncText.SharesOwned, ThemeParkIncText.SharePrice,
			ThemeParkIncText.PortfolioValue, ThemeParkIncText.ZoneNotYetOpened, ThemeParkIncText.CurrentObjective } )
			Assert.IsTrue( strings.Entry( id ).Trim().Length > 0, $"UITEXT {id}" );
		var (caption, body) = UiDialogs.SplitCaption( strings.Entry( ThemeParkIncText.QuitConfirmation ) );
		Assert.IsTrue( caption.Length > 0 && body.Length > 0, "the quit text has a caption and a question" );
		Assert.AreEqual( caption.ToUpperInvariant(), caption, "the caption is the upper-case line" );
		var table = ThemeParkIncLayoutTables.PersonnelFile;
		foreach ( var id in new uint[] { 0xC754, 0xC759, 0xC755, 0xC75B, 0xC73A } )
			Assert.IsTrue( strings.Help( table.Control( id ).Help ).Length > 0, $"help {table.Control( id ).Help}" );
		Assert.AreEqual( "Resume this game", GameLanguage.Resolve( data, "English", null ).LoadStrings( "UIHELPTEXT.str" ).Entries[287] );
	}

	[TestMethod]
	public void ThePersonnelFileFontsLoad()
	{
		var data = UseTpiData();
		var language = GameLanguage.Resolve( data, "English", null );
		var loader = UiFonts.CachedLoader( language );
		foreach ( var name in new[] { "PERSONNELFILEBIG", "PERSONNELFILEMED", "PERSONNELFILESMALL" } )
			Assert.IsTrue( loader( name + ".bf4" ).LineHeight > 0, name );
	}

	[TestMethod]
	public void ThePageBuildsAndDrawsOverTheRealModelsStringsAndFonts()
	{
		var data = UseTpiData();
		var language = GameLanguage.Resolve( data, "English", null );
		var strings = UiStringTable.Load( language );
		var loader = UiFonts.CachedLoader( language );
		var fonts = new UiFonts( _ => loader( "PERSONNELFILEMED.bf4" ) );
		var calls = new Calls();
		var frontEnd = new ThemeParkIncFrontEnd( strings, calls.Host, loader );
		foreach ( var (width, height) in new[] { (1280, 720), (1920, 1080), (2560, 1440) } )
		{
			var context = new UiContext( strings, fonts, new UiModels() ) { Canvas = new UiCanvas( width, height ) };
			frontEnd.Update( context, UiInput.Idle( new N.Vector2( width / 2f, height / 2f ) ) );
			context.Batch.Clear();
			frontEnd.Draw( context );
			Assert.IsTrue( context.Batch.Draws.Count > 20, $"the page draws many quads at {width}x{height}" );
			frontEnd.RequestQuit();
			frontEnd.Draw( context );
			frontEnd.Stack.Pop();
		}
		Click( frontEnd, new UiContext( strings, fonts, new UiModels() ) { Canvas = new UiCanvas( 1920, 1080 ) }, "zone_water" );
		frontEnd.EnterGame();
		CollectionAssert.AreEqual( new[] { "water" }, calls.Worlds );
	}
}

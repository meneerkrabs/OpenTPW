using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTPW.FrontEnd;
using OpenTPW.Hud;
using OpenTPW.UI.Original;

namespace OpenTPW.Tests;

/// <summary>
/// Original-data tests of the UI slice: ui.wad/lobby.wad evidence, the assets every screen uses,
/// and string/glyph binding per language (OPENTPW_GAME_PATH, plus OPENTPW_LANGUAGE_DATA for the
/// five non-English languages).
/// </summary>
[TestClass]
[DoNotParallelize]
public class OriginalUiAssetTests
{
	private BaseFileSystem? originalFileSystem;
	private bool initialized;

	/// <summary>Every original UI model a screen draws.</summary>
	public static readonly string[] UsedModels =
	{
		"mainpanel", "gauge", "date", "b_buy", "b_info", "b_money", "b_resrch", "b_map", "panel", "b_srides", "b_sshop", "b_sshow", "b_sfeature",
		"b_retract", "b_door", "b_erase", "f_tagl", "f_tagm", "f_tagr", "islandlobby", "f_lobbutbg", "b_lobleft", "b_entpark", "b_lobright",
		"tpwlogo", "w_med", "w_dialog", "f_optpanel2", "b_sleft", "b_sright", "b_okay",
		"w_big", "!frame", "!slider", "f_buyitem", "b_up", "b_down", "b_scroller", "b_exit", "b_allstaff", "i_dollar", "cashtrend", "gkey", "gtick"
	};

	/// <summary>UITEXT entries drawn by the screens.</summary>
	public static readonly UIStrings[] UsedUiText =
	{
		UIStrings.Load, UIStrings.Save, UIStrings.Options, UIStrings.ResumeGame, UIStrings.QuitGame, UIStrings.ConfirmQuit, UIStrings.ExitToLobby,
		UIStrings.Excitement, UIStrings.Reliability, UIStrings.StateOfRepair, UIStrings.RemainingLife, UIStrings.BuyRide, UIStrings.BuyShop,
		UIStrings.BuySideshow, UIStrings.BuyMiscItems, UIStrings.LoadPark, UIStrings.GameMode, UIStrings.InstantAction, UIStrings.FullSimulation,
		UIStrings.GameOptions, UIStrings.ScreenResolution, UIStrings.SoundEffectsVolume, UIStrings.MusicVolume, UIStrings.SpeechVolume,
		UIStrings.MovieVolume, UIStrings.PopupHelp, UIStrings.No, UIStrings.Yes, UIStrings.Custom, UIStrings.Resolution512x384,
		UIStrings.Resolution640x480, UIStrings.Resolution800x600, UIStrings.Resolution1024x768, UIStrings.Resolution1280x1024,
		UIStrings.Resolution1600x1200, UIStrings.Resolution400x300, UIStrings.ChangeScreenResolution, UIStrings.ChangeScreenResolutionRestored,
		UIStrings.RestartGame, UIStrings.Paused, UIStrings.ThemeParkWorld, UIStrings.Dollar, UIStrings.NegativeDollar
	};

	/// <summary>UIHELPTEXT entries used as popup help or messages.</summary>
	public static readonly int[] UsedHelpText = { 2, 12, 15, 140, 141, 142, 143, 151, 152, 153, 318, 343, 344, 345, 358, 359, 400, 440, 465, 469, 470, 471, 472, 473, 477, 478, 481, 521, 522, 523, 524 };

	[TestInitialize]
	public void Initialize()
	{
		var data = StringTableTests.OriginalDataDirectory();
		if ( !File.Exists( Path.Combine( data, "ui.wad" ) ) || !File.Exists( Path.Combine( data, "lobby.wad" ) ) )
			Assert.Inconclusive( "Original ui.wad/lobby.wad are not installed." );
		originalFileSystem = FileSystem;
		FileSystem = new BaseFileSystem( data );
		FileSystem.RegisterArchiveHandler<WadArchive>( ".wad" );
		initialized = true;
	}

	[TestCleanup]
	public void Cleanup()
	{
		if ( initialized )
			FileSystem = originalFileSystem!;
	}

	[DataTestMethod]
	[DataRow( "jungle" )]
	[DataRow( "hallow" )]
	[DataRow( "fantasy" )]
	[DataRow( "space" )]
	public void BuildMenuContainsEveryBuildableThemeObjectWithItsOriginalData( string theme )
	{
		var original = ObjectCatalog.Load( theme );
		var menu = new OriginalBuildCatalog( original );
		var items = Enum.GetValues<BuildCategory>().SelectMany( menu.GetItems ).ToArray();
		CollectionAssert.AreEquivalent( original.Buildable.Select( entry => entry.InfoId ).ToArray(), items.Select( item => item.InfoId ).ToArray() );
		Assert.IsTrue( items.Length > 3, "the menu must expose more than its first three slots" );
		CollectionAssert.AllItemsAreUnique( items.Select( item => item.Id ).ToArray() );
		foreach ( var item in items )
		{
			var entry = original.Get( item.InfoId );
			Assert.AreSame( entry, item.Entry );
			Assert.AreEqual( entry.WhichUIType, (int)item.Category );
			Assert.AreEqual( (long)entry.BuildCost, item.Cost );
			Assert.AreEqual( entry.ObjectNameIndex ?? -1, item.ObjectNameIndex );
			Assert.AreEqual( entry.IsBonus ? null : entry.PreviewModelPath ?? entry.ModelPath, item.PreviewModel );
			Assert.IsFalse( entry.IsFixedItem || entry.IsTool || entry.Category == ObjectCategory.Upgrade );
		}
	}

	[TestMethod]
	public void EveryOriginalModelHasItsStoredRootDrawingKey()
	{
		var models = new UiModels();
		Assert.AreEqual( 278, models.Assets.Count );
		Assert.AreEqual( 277, models.BindingCount, "the original ordinary loader explicitly excludes w_small_shadow.MD2" );
		foreach ( var asset in models.Assets )
		{
			var source = new ModelFile( FileSystem.GetFiles( "/ui" ).Single( path => Path.GetFileNameWithoutExtension( path ) == asset.Name
				&& path.EndsWith( ".md2", StringComparison.OrdinalIgnoreCase ) ) );
			Assert.AreEqual( source.Nodes[source.RootNodeIndex].Name, asset.RootNodeName, asset.Name );
			Assert.AreEqual( UiModels.RootNameKey( asset.RootNodeName ), asset.DrawingKey, asset.Name );
			Assert.AreSame( asset, models.GetAsset( asset.Name ), asset.Name );
			if ( !UiModels.IsSpecialShadowAsset( asset ) )
				Assert.AreSame( asset, models.GetByDrawingKey( asset.DrawingKey ), asset.Name );
		}
		Assert.AreEqual( "base", models.GetAsset( "mainpanel" ).RootNodeName );
		Assert.AreEqual( "pan_money", models.GetAsset( "panel" ).RootNodeName );
		Assert.AreEqual( "optpanel2", models.GetAsset( "f_optpanel2" ).RootNodeName );
		Assert.AreEqual( "shadow1", models.GetByRootName( "wshadow1" ).Name );
	}

	[TestMethod]
	public void EveryUiWadModelFlattensIntoTheAuthoredCanvas()
	{
		var files = FileSystem.GetFiles( "/ui" ).Where( file => file.EndsWith( ".md2", StringComparison.OrdinalIgnoreCase ) ).ToArray();
		Assert.AreEqual( 278, files.Length );
		var models = files.Select( file => new UiModel( new ModelFile( file ), Path.GetFileNameWithoutExtension( file ) ) ).ToArray();
		Assert.AreEqual( 278, models.Length );
		Assert.IsTrue( models.All( model => model.Frames.Count >= 1 ) );
		// Full-screen frames prove the 2048×1536 authoring space.
		foreach ( var name in new[] { "f_chat", "w_map" } )
		{
			var frame = models.Single( model => model.Name == name ).Frames[0];
			Assert.AreEqual( 0, frame.MinX, 1, name );
			Assert.AreEqual( 0, frame.MinY, 1, name );
			Assert.AreEqual( 2048, frame.MaxX, 1, name );
			Assert.AreEqual( 1536, frame.MaxY, 1, name );
		}
		var placed = models.Count( model => !model.IsPositionedByCode );
		Assert.IsTrue( placed > 100 && placed < 278, $"{placed} models carry their on-screen position" );
		var mainPanel = models.Single( model => model.Name == "mainpanel" );
		Assert.AreEqual( 238.0f, mainPanel.AuthoredCenter.X, 0.1f );
		Assert.AreEqual( 1245.7f, mainPanel.AuthoredCenter.Y, 0.1f );
	}

	[TestMethod]
	public void ButtonStateFramesAreDrawnOverTheRootMesh()
	{
		var buy = UiModel.Load( "b_buy" );
		Assert.AreEqual( 6, buy.Frames.Count );
		CollectionAssert.AreEqual( new[] { "b_buy", "disable", "hilite", "heldown", "hidown", "down" }, buy.Frames.Select( frame => frame.NodeName ).ToArray() );
		foreach ( var frame in buy.Frames )
		{
			Assert.AreEqual( 229.3f, frame.Center.X, 0.2f );
			Assert.AreEqual( 1181.2f, frame.Center.Y, 0.2f );
			Assert.AreEqual( 118.4f, frame.Width, 0.2f );
			Assert.AreEqual( 118.4f, frame.Height, 0.2f );
		}
		// Normal/disabled/highlight use the plain art, the down states the "d" art; V is flipped so the
		// button occupies the top-left of its texture.
		CollectionAssert.AreEqual( new[] { "b_buy", "b_buy", "b_buy", "b_buyd", "b_buyd", "b_buyd" }, buy.Frames.Select( frame => frame.Parts.Single().TextureName ).ToArray() );
		var uvs = buy.Frames[0].Parts[0].Vertices.Select( vertex => vertex.TexCoords ).ToArray();
		Assert.AreEqual( 0, uvs.Min( uv => uv.Y ), 0.01f );
		Assert.AreEqual( 0.58f, uvs.Max( uv => uv.Y ), 0.01f );
	}

	[TestMethod]
	public void EveryModelAndTextureTheScreensUseExists()
	{
		foreach ( var name in UsedModels )
		{
			var model = UiModel.Load( name );
			foreach ( var part in model.Frames.SelectMany( frame => frame.Parts ).Where( part => part.TextureName.Length > 0 ) )
				Assert.IsNotNull( UiImages.Resolve( part.TextureName ), $"{name} texture {part.TextureName}" );
		}
		var (width, height, rgba) = UiImages.Load( UiImages.Resolve( "purple_button" )! );
		Assert.AreEqual( (128, 128), (width, height) );
		Assert.IsTrue( Enumerable.Range( 0, width * height ).Any( index => rgba[index * 4 + 3] == 0 ), "pink key becomes transparent" );
	}

	[TestMethod]
	public void LobbyDefinitionHasTheFourThemeIslands()
	{
		var lobby = LobbyDefinition.Load();
		CollectionAssert.AreEqual( new[] { "jungle", "fantasy", "hallow", "space" }, lobby.Islands.Select( island => island.Level ).ToArray() );
		CollectionAssert.AreEqual( new[] { 0, 2, 1, 3 }, lobby.Islands.Select( island => island.ThemeNameIndex ).ToArray() );
		Assert.AreEqual( (400f, 400f), lobby.PositionOf( lobby.Islands[0] ) );
		Assert.AreEqual( (400f, 600f), lobby.PositionOf( lobby.Islands[3] ) );
		Assert.AreEqual( 70f, lobby.SpinRadius );
		Assert.AreEqual( 20f, lobby.VerticalOffset );
		foreach ( var island in lobby.Islands )
		{
			foreach ( var model in new[] { island.IslandModel, island.GateModel } )
				Assert.IsTrue( FileSystem.GetFiles( "/lobby/terrain" ).Any( file => string.Equals( Path.GetFileNameWithoutExtension( file ), model, StringComparison.OrdinalIgnoreCase ) ), model );
		}
		Assert.IsNotNull( new ModelFile( "/lobby/terrain/Base.MD2" ).Heightfield );
	}

	[TestMethod]
	public void TotemCatalogAndStartingCashComeFromOriginalSettings()
	{
		var item = new TotemBuildCatalog().GetItems( BuildCategory.Rides ).Single();
		Assert.AreEqual( 3250, item.Cost );
		Assert.AreEqual( 70, item.DefaultExcitement, "Totem.sam UsageInfo.ExcitementLevel" );
		Assert.AreEqual( TotemBuildCatalog.ObjectNameIndex, item.ObjectNameIndex );
		Assert.AreEqual( 0, new TotemBuildCatalog().GetItems( BuildCategory.Shops ).Count );
		var unresolved = new List<string>();
		var icon = new PreviewIcon( new ModelFile( item.PreviewModel! ), name =>
		{
			var path = item.TextureDirectories.Select( directory => UiImages.Resolve( name, directory ) ).FirstOrDefault( found => found != null );
			if ( path == null )
				unresolved.Add( name );
			return path;
		} );
		Assert.IsTrue( icon.TriangleCount > 100 );
		CollectionAssert.AreEqual( Array.Empty<string>(), unresolved.Distinct().ToArray() );
		var batch = new UiBatch();
		icon.Draw( batch, new UiRect( 0, 0, 100, 100 ), 0.5f );
		var points = batch.Draws.SelectMany( draw => draw.Vertices ).Select( vertex => vertex.Position ).ToArray();
		Assert.IsTrue( points.All( point => point.X >= -0.5f && point.X <= 100.5f && point.Y >= -0.5f && point.Y <= 100.5f ), "icon fits its slot" );
	}

	/// <summary>
	/// Jungle rides without a P model (bouncy, bumper, incagod, monkey, mystery, tourride, volcano) fall back to their main
	/// model, as the original buy window does, and that model draws inside its slot with every texture resolved.
	/// </summary>
	[TestMethod]
	public void BuildItemsWithoutAPreviewModelUseTheirMainModel()
	{
		var original = ObjectCatalog.Load( "jungle" );
		var items = Enum.GetValues<BuildCategory>().SelectMany( new OriginalBuildCatalog( original ).GetItems ).ToArray();
		var fallbacks = items.Where( item => item.Entry is { IsBonus: false, PreviewModelPath: null } ).ToArray();
		Assert.IsTrue( fallbacks.Length > 0, "the jungle has rides without a P model" );
		Assert.IsTrue( items.Where( item => item.Entry is { IsBonus: false } ).All( item => item.PreviewModel != null ), "every non-bonus item has an icon model" );
		foreach ( var item in fallbacks )
		{
			Assert.AreEqual( item.Entry!.ModelPath, item.PreviewModel, item.Id );
			// Textures are checked through the entry's own file systems: the catalogue cache is keyed by data root, so a
			// catalogue cached by an earlier test refers to that test's file system instance.
			var unresolved = new List<string>();
			var icon = new PreviewIcon( new ModelFile( item.PreviewModel! ), name =>
			{
				if ( ObjectAssets.ResolveTexture( item.Entry, name ) == null )
					unresolved.Add( name );
				return null;
			} );
			Assert.IsTrue( icon.TriangleCount > 0, item.Id );
			CollectionAssert.AreEqual( Array.Empty<string>(), unresolved.Distinct().ToArray(), $"{item.Id}: {string.Join( ", ", unresolved.Distinct() )}" );
			var batch = new UiBatch();
			icon.Draw( batch, new UiRect( 0, 0, 100, 100 ), 0.5f );
			var points = batch.Draws.SelectMany( draw => draw.Vertices ).Select( vertex => vertex.Position ).ToArray();
			Assert.IsTrue( points.Length > 0 && points.All( point => point.X >= -0.5f && point.X <= 100.5f && point.Y >= -0.5f && point.Y <= 100.5f ), $"{item.Id} fits its slot" );
		}
	}

	[TestMethod]
	public void UiStringIdsMatchTheEnglishTable()
	{
		var strings = UiStringTable.Load( GameLanguage.Resolve( StringTableTests.OriginalDataDirectory(), "English", null ) );
		Assert.AreEqual( "Game Options", strings[UIStrings.GameOptions] );
		Assert.AreEqual( "Screen resolution:", strings[UIStrings.ScreenResolution] );
		Assert.AreEqual( " 640 x 480", strings[UIStrings.Resolution640x480] );
		Assert.AreEqual( " Custom", strings[UIStrings.Custom] );
		Assert.AreEqual( "PAUSED", strings[UIStrings.Paused] );
		Assert.AreEqual( "Game Mode", strings[UIStrings.GameMode] );
		Assert.AreEqual( "Instant Action", strings[UIStrings.InstantAction] );
		Assert.AreEqual( "Theme Park World", strings[UIStrings.ThemeParkWorld] );
		Assert.AreEqual( "Load Park", strings[UIStrings.LoadPark] );
		Assert.AreEqual( "Buy Ride", strings[UIStrings.BuyRide] );
		Assert.AreEqual( "$ ", strings[UIStrings.Dollar] );
		Assert.AreEqual( "Return To Park", strings[UIStrings.ReturnToPark] );
		Assert.AreEqual( "Create New Player", strings[UIStrings.CreateNewPlayer] );
		StringAssert.StartsWith( strings[UIStrings.ChangeScreenResolution], "CHANGE SCREEN RESOLUTION\n\nDo you want to keep" );
		StringAssert.StartsWith( strings[UIStrings.ChangeScreenResolutionRestored], "CHANGE SCREEN RESOLUTION\n\nYour original setting" );
		StringAssert.StartsWith( strings[UIStrings.RestartGame], "RESTART GAME" );
		Assert.AreEqual( "Left-click to enter this park", strings.Help( 345 ) );
		Assert.AreEqual( "Lost Kingdom", strings.Theme( 0 ) );
		Assert.AreEqual( "Totem", strings.Object( TotemBuildCatalog.ObjectNameIndex ) );
	}

	public static IEnumerable<object[]> Languages => GameLanguage.ShippedLanguages.Select( name => new object[] { name } );

	[DataTestMethod]
	[DynamicData( nameof( Languages ) )]
	public void EveryUiStringHasGlyphsInEveryUiFontOfItsLanguage( string name )
	{
		var data = StringTableTests.OriginalDataDirectory();
		var overlay = Environment.GetEnvironmentVariable( "OPENTPW_LANGUAGE_DATA" );
		if ( name != "English" && (string.IsNullOrWhiteSpace( overlay ) || !Directory.Exists( overlay )) )
			Assert.Inconclusive( "Set OPENTPW_LANGUAGE_DATA to the CD's extracted language folders." );
		if ( !GameLanguage.FindLanguages( data, name == "English" ? null : overlay ).Contains( name ) )
			Assert.Inconclusive( $"{name} language data is missing." );
		var language = GameLanguage.Resolve( data, name, name == "English" ? null : overlay );
		var strings = UiStringTable.Load( language );
		var texts = UsedUiText.Select( id => strings[id] ).Concat( UsedHelpText.Select( strings.Help ) )
			.Concat( Enumerable.Range( 0, 4 ).Select( strings.Theme ) ).Append( strings.Object( TotemBuildCatalog.ObjectNameIndex ) )
			.Concat( SupplementaryStrings.AllTexts( name ) ).Concat( new[] { "|| > >> >>> 0123456789 x %,.-:" } ).ToArray();
		// French and German leave the "$ " money prefix (UITEXT 448) empty; every other used string exists.
		foreach ( var id in UsedUiText.Where( id => id != UIStrings.Dollar || name is not ("French" or "German") ) )
			Assert.IsFalse( string.IsNullOrWhiteSpace( strings[id] ), $"{name} UITEXT {id}" );
		foreach ( var index in UsedHelpText )
			Assert.IsFalse( string.IsNullOrWhiteSpace( strings.Help( index ) ), $"{name} UIHELPTEXT {index}" );
		var load = UiFonts.CachedLoader( language );
		foreach ( var font in UiFonts.FileNames )
		{
			var atlas = load( font );
			foreach ( var text in texts )
			{
				var missing = TextLayout.Create( atlas, text ).MissingCharacters;
				Assert.AreEqual( 0, missing.Count, $"{name} {font} lacks '{string.Concat( missing.Distinct() )}' for \"{text}\"" );
			}
		}
		Assert.AreEqual( name, strings.Language );
	}
}

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
		"w_big", "!frame", "!slider", "f_buyitem", "b_up", "b_down", "b_scroller", "b_exit", "b_allstaff", "i_dollar", "cashtrend", "gkey", "gtick",
		"list_all", "b_svisit", "b_gopeep", "b_pick"
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
		UIStrings.RestartGame, UIStrings.Paused, UIStrings.ThemeParkWorld, UIStrings.Dollar, UIStrings.NegativeDollar,
		// The overview windows (UI-905..910): category titles, detail labels, statuses and delete confirmations.
		UIStrings.CurrentStatus, UIStrings.AllRides, UIStrings.AllShops, UIStrings.AllSideshows, UIStrings.AllMiscItems,
		UIStrings.UsersLastMonthAttr, UIStrings.StateOfRepairAttr, UIStrings.RemainingLifeAttr,
		UIStrings.CustomersLastMonthShop, UIStrings.ProfitLastMonthShop, UIStrings.TotalProfitShop, UIStrings.CustomerSatisfactionShop,
		UIStrings.CustomersLastMonthGame, UIStrings.ExcitementGame, UIStrings.ProfitLastMonthGame, UIStrings.TotalProfitGame, UIStrings.CustomerSatisfactionGame,
		UIStrings.AllVisitors, UIStrings.VisitorNumber, UIStrings.CashRemainingVisitor, UIStrings.TimeInParkVisitor, UIStrings.RidesRiddenVisitor,
		UIStrings.HappinessVisitor, UIStrings.NameAttr, UIStrings.Closed, UIStrings.BrokenDown,
		UIStrings.DeleteRide, UIStrings.DeleteShop, UIStrings.DeleteSideshow, UIStrings.DeleteItem
	};

	/// <summary>UIHELPTEXT entries used as popup help or messages.</summary>
	public static readonly int[] UsedHelpText = { 2, 12, 15, 140, 141, 142, 143, 151, 152, 153, 88, 318, 343, 344, 345, 358, 359, 400, 440, 465, 469, 470, 471, 472, 473, 477, 478, 481, 521, 522, 523, 524 };

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
			Assert.AreEqual( entry.PreviewModelPath ?? entry.ModelPath, item.PreviewModel, "objects outside the game data (bonus content, mods) have their model too" );
			Assert.IsFalse( entry.IsFixedItem || entry.IsTool || entry.Category == ObjectCategory.Upgrade );
		}
	}

	/// <summary>
	/// The state frames whose texture ui.wad lacks: the disable frames of the call and upgrade buttons, which UiButton draws as the
	/// normal frame greyed instead of untextured white ([EXT:missing-state-texture]), and the pressed frames of the unused b_loantog.
	/// </summary>
	[TestMethod]
	public void OnlyTheCallUpgradeAndLoanToggleStateFramesNameMissingTextures()
	{
		bool Resolves( UiModel model, int frame ) => model.GetFrame( frame ).Parts.All( part => part.TextureName.Length == 0 || UiImages.Resolve( part.TextureName ) != null );
		// models whose normal frame draws from /ui/textures, with a state frame that does not
		var missing = new UiModels().Assets.Where( model => Resolves( model, 0 ) )
			.SelectMany( model => Enumerable.Range( 1, Math.Max( 0, model.Frames.Count - 1 ) ).Where( frame => !Resolves( model, frame ) ).Select( frame => $"{model.Name}:{frame}" ) )
			.OrderBy( item => item, StringComparer.Ordinal ).ToArray();
		Console.WriteLine( string.Join( ", ", missing ) );
		// b_loantog (no OpenTPW control draws it) also lacks the art of its three pressed frames
		CollectionAssert.AreEqual( new[] { "b_callhandy:1", "b_callmech:1", "b_loantog:3", "b_loantog:4", "b_loantog:5", "b_upgrade:1" }, missing );
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

	/// <summary>
	/// The chroma planes take their scales from the header's two chroma shorts crosswise, so saturated reds and oranges decode: the research
	/// lab's tube caps (pipe.wct) are orange and the red end of the gauge bar (ridestatbar.wct) is red, where the swapped scales had made them yellow-green.
	/// </summary>
	[TestMethod]
	public void SaturatedChromaOfTheTubeCapsAndTheGaugeBarDecodes()
	{
		(byte R, byte G, byte B, byte A) Texel( string path, int x, int y )
		{
			var texture = new TextureFile( path ).Data;
			var index = (y * texture.Width + x) * 4;
			return (texture.Data[index], texture.Data[index + 1], texture.Data[index + 2], texture.Data[index + 3]);
		}
		var cap = Texel( "/ui/textures/pipe.wct", 8, 8 );
		Assert.IsTrue( cap.A >= 240 && cap.R > 200 && cap.G is > 120 and < 200 && cap.B < 20, $"the tube cap is orange, not {cap}" );
		var barStart = Texel( "/ui/textures/ridestatbar.wct", 4, 4 );
		Assert.IsTrue( barStart.R > 220 && barStart.G < 140 && barStart.B < 20, $"the gauge bar starts red-orange, not {barStart}" );
		var barEnd = Texel( "/ui/textures/ridestatbar.wct", 28, 4 );
		Assert.IsTrue( barEnd.G > 240 && barEnd.R < 100, $"the gauge bar ends green, not {barEnd}" );
	}

	/// <summary>The chat overlay's frame keeps clicks off the park only with its panels, not over the opening the park shows through.</summary>
	[TestMethod]
	public void TheChatFrameCoversTheParkOnlyWithItsPanels()
	{
		var panel = new OpenTPW.UI.ChatPanel( OriginalUiTests.FakeStrings(), new UiScreenStack(), () => null );
		var frame = (UiModelImage)panel.Elements.Single( element => element.Id == "chatFrame" );
		foreach ( var canvas in new[] { new UiCanvas( 2048, 1536 ), new UiCanvas( 1280, 720 ) } )
		{
			var context = new UiContext( OriginalUiTests.FakeStrings(), null!, new UiModels() ) { Canvas = canvas };
			frame.Draw( context, false, false );
			System.Numerics.Vector2 At( float x, float y ) => canvas.Map( new UiRect( x, y, 0, 0 ), UiAnchor.TopLeft ).Center;
			// [DATA:ui.wad:f_chat] the root mesh's parts: f_chat1 the left column (x 0..543, y 0..1150), f_chat2 under it (y 1150..1536) and f_chat3 the
			// strip along the bottom (x 543..2048, y 1150..1536); the opening x 543..2048, y 0..1150 has no triangle
			Assert.IsTrue( frame.CoversPoint( canvas, At( 300, 600 ) ), "the left column" );
			Assert.IsTrue( frame.CoversPoint( canvas, At( 1200, 1400 ) ), "the bottom strip" );
			Assert.IsTrue( frame.CoversPoint( canvas, At( 2040, 1160 ) ), "the strip's far corner" );
			Assert.IsFalse( frame.CoversPoint( canvas, At( 1200, 500 ) ), "the park opening" );
			Assert.IsFalse( frame.CoversPoint( canvas, At( 560, 1130 ) ), "the opening's corner by the panels" );
		}
	}

	/// <summary>The research rows' tube art (res_pipe) is authored at the column the lab's rows draw it in.</summary>
	[TestMethod]
	public void TheResearchTubeIsAuthoredInTheRowColumn()
	{
		var pipe = UiModel.Load( "res_pipe" );
		Assert.AreEqual( "pipe", pipe.RootNodeName );
		var frame = pipe.Frames[0];
		Assert.AreEqual( "pipe", frame.Parts.Single().TextureName );
		// [BIN:TPW-EXE:0x35299c research panel control table] the pipe is the 307 x 119 child at x 1497..1804 whose top ParkHud.CreateResearchScreen places 17 units under each panel's top (row 0: 175)
		Assert.AreEqual( 1497f, frame.MinX, 1f );
		Assert.AreEqual( 1804f, frame.MaxX, 1f );
		Assert.AreEqual( 119f, frame.Height, 1f );
	}

	[TestMethod]
	public void TheResearchFluidHasTheSixtyFourFramesItsAnimationSteps()
	{
		// tp.exe 0x4a9fc0 / 0x4a95b0 step the fluid child through frames 0..31 (loop), 32..47 (drain, idle at 48) and 48..63 (fill)
		var fluid = UiModel.Load( "res_fluid" );
		Assert.AreEqual( "fluid", fluid.RootNodeName );
		Assert.AreEqual( ResearchFluid.FrameCount, fluid.Frames.Count );
		var expected = new[] { "fluid" }.Concat( Enumerable.Range( 2, 31 ).Select( index => $"fluid{index:00}" ) )
			.Concat( Enumerable.Range( 1, 32 ).Select( index => $"empty{index:00}" ) ).ToArray();
		CollectionAssert.AreEqual( expected, fluid.Frames.Select( frame => frame.NodeName ).ToArray() );
		Assert.IsTrue( fluid.Frames[ResearchFluid.IdleFrame].Parts.All( part => part.TextureName == "fluidempty" ), "the idle frame shows the empty fluid only" );
	}

	[TestMethod]
	public void ButtonStateFramesAreDrawnOverTheRootMesh()
	{
		var buy = UiModel.Load( "b_buy" );
		Assert.AreEqual( 6, buy.Frames.Count );
		CollectionAssert.AreEqual( new[] { "b_buy", "disable", "hilite", "heldown", "hidown", "down" }, buy.Frames.Select( frame => frame.NodeName ).ToArray() );
		// 0x57a530 composes the ancestors, so the chained state frames sit at their own world offsets; the selected-mesh fit (0x476fd3) cancels that common
		// translation of geometry and endpoints, so every state draws over the root mesh's place.
		UiRect rect = new( 100, 200, 118.4f, 118.4f );
		System.Numerics.Vector2[] Drawn( UiModelFrame frame )
		{
			var context = OriginalUiTests.FakeContext();
			context.DrawFrame( frame, rect );
			return context.Batch.Draws.SelectMany( draw => draw.Vertices ).Select( vertex => vertex.Position ).ToArray();
		}
		var rootDrawn = Drawn( buy.Frames[0] );
		Assert.AreEqual( 229.3f, buy.Frames[0].Center.X, 0.2f );
		Assert.AreEqual( 1181.2f, buy.Frames[0].Center.Y, 0.2f );
		foreach ( var frame in buy.Frames )
		{
			Assert.AreEqual( 118.4f, frame.Width, 0.2f );
			Assert.AreEqual( 118.4f, frame.Height, 0.2f );
			var drawn = Drawn( frame );
			Assert.AreEqual( rootDrawn.Length, drawn.Length, frame.NodeName );
			for ( var index = 0; index < drawn.Length; index++ )
				Assert.AreEqual( 0f, System.Numerics.Vector2.Distance( rootDrawn[index], drawn[index] ), 0.05f, $"{frame.NodeName} vertex {index}" );
		}
		// Normal/disabled/highlight use the plain art, the down states the "d" art; V is flipped so the
		// button occupies the top-left of its texture.
		CollectionAssert.AreEqual( new[] { "b_buy", "b_buy", "b_buy", "b_buyd", "b_buyd", "b_buyd" }, buy.Frames.Select( frame => frame.Parts.Single().TextureName ).ToArray() );
		var uvs = buy.Frames[0].Parts[0].Vertices.Select( vertex => vertex.TexCoords ).ToArray();
		Assert.AreEqual( 0, uvs.Min( uv => uv.Y ), 0.01f );
		Assert.AreEqual( 0.58f, uvs.Max( uv => uv.Y ), 0.01f );
	}

	/// <summary>The icon models of the per-employee and All Visitors windows have a frame per status and per thought.</summary>
	[TestMethod]
	public void TheStatusAndThoughtIconsHaveTheirFrames()
	{
		var models = new UiModels();
		// the root names the per-employee and All Visitors windows draw (tables 0x756f48 and 0x7506c8)
		foreach ( var name in OriginalLayoutTables.EmployeeWindow.All.Concat( OriginalLayoutTables.VisitorsWindow.All ).Select( node => node.Model ).OfType<string>() )
			Assert.IsNotNull( models.Get( name ), name );
		var thoughts = models.Get( "ti_hungry" )!.Frames.Count;
		var statuses = models.Get( "staffstat" )!.Frames.Count;
		Assert.AreEqual( (17, 4), (thoughts, statuses), "ti_hungry: one frame per THOUGHTS entry 1-17 (frame = thought - 1); staffstat: frames 0-3 of 0x485ec0" );
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

	/// <summary>
	/// The evidence behind UI-909's stand-in buttons: ui.wad ships the b_allthinginfo and b_goto models without
	/// their textures (so the overview windows cannot use that art), while the art they do draw resolves, among it the
	/// rides tab icon the All Attractions entry buttons wear instead of a text label too wide for their round slot.
	/// </summary>
	[TestMethod]
	public void TheOverviewWindowsButtonArtEvidenceHolds()
	{
		foreach ( var name in new[] { "b_allthinginfo", "b_goto" } )
		{
			var model = UiModel.Load( name );
			var textures = model.Frames.SelectMany( frame => frame.Parts ).Select( part => part.TextureName )
				.Where( texture => texture.Length > 0 ).Distinct().ToArray();
			Assert.IsTrue( textures.Length > 0, $"{name} ships a model that references textures" );
			foreach ( var texture in textures )
				Assert.IsNull( UiImages.Resolve( texture ),
					$"{name}'s texture '{texture}' resolves now — UI-909's stand-in buttons can switch to the shipped art" );
		}
		foreach ( var name in new[] { "list_all", "b_svisit", "b_gopeep", "b_erase", ParkHud.AllAttractionsModel } )
		{
			var model = UiModel.Load( name );
			var textures = model.Frames.SelectMany( frame => frame.Parts ).Select( part => part.TextureName )
				.Where( texture => texture.Length > 0 ).Distinct().ToArray();
			Assert.IsTrue( textures.Length > 0, $"{name} draws textured parts" );
			foreach ( var texture in textures )
				Assert.IsNotNull( UiImages.Resolve( texture ), $"{name} texture {texture}" );
		}
	}

	/// <summary>The info arm's call buttons (ParkHud.Maintenance) draw the shipped art.</summary>
	[TestMethod]
	public void TheCallButtonArtResolves()
	{
		foreach ( var name in new[] { "b_callmech", "b_callhandy" } )
		{
			var model = UiModel.Load( name );
			var textures = model.Frames.SelectMany( frame => frame.Parts ).Select( part => part.TextureName )
				.Where( texture => texture.Length > 0 ).Distinct().ToArray();
			Assert.IsTrue( textures.Length > 0, $"{name} draws textured parts" );
			// the normal (b_callmech), pressed (…d) and disabled (…X) art resolves; the highlight (…g) is absent from ui.wad, so the button keeps its normal frame when focused
			foreach ( var texture in textures )
				Assert.AreEqual( !texture.EndsWith( 'g' ), UiImages.Resolve( texture ) != null, $"{name} texture {texture}" );
		}
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
		Assert.AreEqual( 0.02f, lobby.SpinSpeed );
		Assert.AreEqual( 100f, lobby.FieldOfView );
		foreach ( var island in lobby.Islands )
		{
			foreach ( var model in new[] { island.IslandModel, island.GateModel } )
				Assert.IsTrue( FileSystem.GetFiles( "/lobby/terrain" ).Any( file => string.Equals( Path.GetFileNameWithoutExtension( file ), model, StringComparison.OrdinalIgnoreCase ) ), model );
		}
		Assert.IsNotNull( new ModelFile( "/lobby/terrain/Base.MD2" ).Heightfield );
	}

	[TestMethod]
	public void LobbyDefinesEveryIslandCameraPositionSlot()
	{
		var lobby = LobbyDefinition.Load();
		for ( var slot = 0; slot < 10; slot++ )
			Assert.IsTrue( lobby.IslandPositions.ContainsKey( slot ), $"lobby.txt ISLANDCAMERAPOSITION slot {slot}: the position fallback is unreachable" );
	}

	[TestMethod]
	public void EveryLoanLenderNameIsAnEntryOfLoanNames()
	{
		var names = GameLanguage.Current.LoadStrings( "LOANNAMES.str" ).Entries;
		Assert.AreEqual( 18, names.Length, "LOANNAMES.str" );
		foreach ( var theme in new[] { "jungle", "fantasy", "hallow", "space" } )
			foreach ( var easy in new[] { false, true } )
				foreach ( var loan in BalanceSettings.Load( theme, easy ).Loans )
				{
					Assert.IsTrue( loan.LenderNameIndex >= 0 && loan.LenderNameIndex < names.Length, $"{theme} easy={easy} loan {loan.Index} Lendername {loan.LenderNameIndex}" );
					Assert.IsFalse( string.IsNullOrWhiteSpace( names[loan.LenderNameIndex] ), $"LOANNAMES.str[{loan.LenderNameIndex}]" );
				}
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
	/// The buy window shows an unbought golden-ticket row as the Mystery ride (tp.exe 0x4ab1b0 previews type 100): every theme's catalogue
	/// offers that tool as a preview item, with a model that draws with every texture resolved.
	/// </summary>
	[DataTestMethod]
	[DataRow( "jungle" )]
	[DataRow( "hallow" )]
	[DataRow( "fantasy" )]
	[DataRow( "space" )]
	public void EveryThemeHasTheMysteryRidePreviewForTicketRows( string theme )
	{
		var original = ObjectCatalog.Load( theme );
		var mystery = new OriginalBuildCatalog( original ).MysteryPreview;
		Assert.IsNotNull( mystery, theme );
		Assert.AreEqual( OriginalBuildCatalog.MysteryRideInfoId, mystery.InfoId );
		Assert.IsTrue( mystery.Entry!.IsTool, "the Mystery ride is a tool, not a listed object" );
		var unresolved = new List<string>();
		var icon = new PreviewIcon( new ModelFile( mystery.PreviewModel! ), name =>
		{
			if ( ObjectAssets.ResolveTexture( mystery.Entry, name ) == null )
				unresolved.Add( name );
			return null;
		} );
		CollectionAssert.AreEqual( Array.Empty<string>(), unresolved.Distinct().ToArray() );
		var batch = new UiBatch();
		icon.Draw( batch, new UiRect( 0, 0, 100, 100 ), 0.5f );
		Assert.IsTrue( batch.Draws.Count > 0, "the Mystery model draws" );
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

	public static IEnumerable<object[]> Languages => LanguageTests.EuropeanLanguages.Select( name => new object[] { name } );

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
			.Concat( SupplementaryStrings.AllTexts( name, language.CharacterTable.Characters.ToHashSet().Contains ) ).Concat( new[] { "|| > >> >>> 0123456789 x %,.-:" } ).ToArray();
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

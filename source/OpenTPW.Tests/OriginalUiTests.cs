using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTPW.FrontEnd;
using OpenTPW.Hud;
using OpenTPW.UI.Original;
using NVector2 = System.Numerics.Vector2;

namespace OpenTPW.Tests;

/// <summary>Layout, navigation, options and string-binding tests of the original-style UI (no GPU).</summary>
[TestClass]
[DoNotParallelize]
public class OriginalUiTests
{
	internal static UiStringTable FakeStrings( string language = "English" ) => new( language,
		Enumerable.Range( 0, 473 ).Select( index => $"ui{index}" ).ToArray(),
		Enumerable.Range( 0, 589 ).Select( index => $"help{index}" ).ToArray(),
		new[] { "Lost Kingdom", "Halloween World", "Wonder Land", "Space Zone" },
		Enumerable.Range( 0, 374 ).Select( index => $"object{index}" ).ToArray() );

	private static UiContext FakeContext( int width = 1024, int height = 768 ) => new( FakeStrings(), null!, new UiModels( name => throw new FileNotFoundException( name ) ) )
	{
		Canvas = new UiCanvas( width, height )
	};

	// ---- Canvas ------------------------------------------------------------------------------

	[TestMethod]
	public void FourByThreeOutputsReproduceTheAuthoredLayoutAtEveryAnchor()
	{
		var canvas = new UiCanvas( 1024, 768 );
		Assert.AreEqual( 0.5f, canvas.Scale );
		var point = new NVector2( 238, 1245.7f );
		foreach ( var anchor in Enum.GetValues<UiAnchor>() )
		{
			var mapped = canvas.Map( point, anchor );
			Assert.AreEqual( 119f, mapped.X, 1e-3f, anchor.ToString() );
			Assert.AreEqual( 622.85f, mapped.Y, 1e-3f, anchor.ToString() );
		}
	}

	[TestMethod]
	public void WideOutputsKeepAnchoredElementsAtTheirEdges()
	{
		var canvas = new UiCanvas( 1920, 1080 );
		Assert.AreEqual( 1080 / 1536f, canvas.Scale, 1e-6f );
		var mainPanel = canvas.Map( new UiRect( 37.1f, 984.2f, 401.7f, 523.1f ), UiAnchor.BottomLeft );
		Assert.AreEqual( 37.1f * canvas.Scale, mainPanel.X, 1e-3f );
		Assert.AreEqual( 1080 - (1536 - 984.2f) * canvas.Scale, mainPanel.Y, 1e-3f );
		var topRight = canvas.Map( new NVector2( 2048, 0 ), UiAnchor.TopRight );
		Assert.AreEqual( 1920f, topRight.X, 1e-3f );
		var center = canvas.Map( new NVector2( 1024, 768 ), UiAnchor.Center );
		Assert.AreEqual( new NVector2( 960, 540 ), center );
		Assert.AreEqual( UiAnchor.BottomLeft, UiCanvas.AnchorFor( new NVector2( 238, 1245 ) ) );
		Assert.AreEqual( UiAnchor.TopRight, UiCanvas.AnchorFor( new NVector2( 1966, 100 ) ) );
	}

	[DataTestMethod]
	[DataRow( 640, 480, 1, UiFontTier.Small )]
	[DataRow( 1024, 768, 1, UiFontTier.Medium )]
	[DataRow( 1280, 720, 1, UiFontTier.Medium )]
	[DataRow( 1920, 1080, 1, UiFontTier.Big )]
	[DataRow( 2560, 1440, 2, UiFontTier.Medium )]
	[DataRow( 3840, 2160, 3, UiFontTier.Medium )]
	[DataRow( 1280, 720, 2, UiFontTier.Medium )]
	public void FontTierFollowsTheLogicalSizeAndTextUsesTheDisplayUiScale( int width, int height, int uiScale, UiFontTier tier )
	{
		var canvas = new UiCanvas( width, height, uiScale );
		Assert.AreEqual( tier, canvas.FontTier );
		Assert.AreEqual( Math.Min( uiScale, UiScaling.Automatic( new Point2( width, height ) ) ), canvas.TextScale, "BF4 text stays integer-scaled within the display reference layout fit" );
	}

	[DataTestMethod]
	[DataRow( 1280, 720, 2, 1 )]
	[DataRow( 1920, 932, 2, 1 )]
	[DataRow( 2560, 1440, 3, 2 )]
	[DataRow( 3840, 2160, 8, 3 )]
	public void OversizedInterfaceScaleKeepsTheReferenceLayoutWithinTheDrawable( int width, int height, int requested, int fitted )
	{
		var canvas = new UiCanvas( width, height, requested );
		Assert.AreEqual( requested, canvas.UiScale, "the request is preserved so the fallback can be reported" );
		Assert.AreEqual( fitted, canvas.TextScale );
		Assert.IsTrue( width / canvas.TextScale >= UiScaling.ReferenceWidth && height / canvas.TextScale >= UiScaling.ReferenceHeight );
		Assert.AreEqual( new UiCanvas( width, height, fitted ).FontTier, canvas.FontTier );
	}

	[TestMethod]
	public void HiDpiOutputsKeepTheLayoutProportionAndDoubleTheText()
	{
		var logical = new UiCanvas( 1280, 720, 1 );
		var hiDpi = new UiCanvas( 2560, 1440, 2 );
		Assert.AreEqual( 2 * logical.Scale, hiDpi.Scale, 1e-6f );
		Assert.AreEqual( logical.FontTier, hiDpi.FontTier );
		var rect = new UiRect( 37.1f, 984.2f, 401.7f, 523.1f );
		var a = logical.Map( rect, UiAnchor.BottomLeft );
		var b = hiDpi.Map( rect, UiAnchor.BottomLeft );
		Assert.AreEqual( 2 * a.X, b.X, 1e-3f );
		Assert.AreEqual( 2 * a.Y, b.Y, 1e-3f );
		Assert.AreEqual( 1, new UiCanvas( 800, 600, 0 ).TextScale, "UI scale is at least 1" );
	}

	// ---- Screens and navigation ------------------------------------------------------------

	[TestMethod]
	public void MouseClickActivatesTheButtonUnderThePointer()
	{
		var context = FakeContext();
		var screen = new UiScreen( "test" );
		var clicks = new List<string>();
		var first = screen.Add( new UiButton { Id = "a", Bounds = new UiRect( 100, 100, 200, 100 ), Clicked = () => clicks.Add( "a" ) } );
		screen.Add( new UiButton { Id = "b", Bounds = new UiRect( 100, 300, 200, 100 ), Clicked = () => clicks.Add( "b" ) } );
		var center = first.ScreenRect( context.Canvas ).Center;
		screen.Update( context, new UiInput( center, true, true, false, false, UiKeys.None ) );
		screen.Update( context, new UiInput( center, false, false, true, false, UiKeys.None ) );
		CollectionAssert.AreEqual( new[] { "a" }, clicks );
		Assert.AreSame( first, screen.Focused );
		// Press on one button and release on another: no click.
		var other = screen.Find( "b" )!.ScreenRect( context.Canvas ).Center;
		screen.Update( context, new UiInput( center, true, true, false, false, UiKeys.None ) );
		screen.Update( context, new UiInput( other, false, false, true, false, UiKeys.None ) );
		CollectionAssert.AreEqual( new[] { "a" }, clicks );
	}

	[TestMethod]
	public void KeyboardMovesFocusActivatesAndGoesBack()
	{
		var context = FakeContext();
		var screen = new UiScreen( "test" );
		var log = new List<string>();
		screen.Add( new UiLabel { Bounds = new UiRect( 0, 0, 10, 10 ) } );
		screen.Add( new UiButton { Id = "a", Bounds = new UiRect( 0, 100, 10, 10 ), Clicked = () => log.Add( "a" ) } );
		screen.Add( new UiButton { Id = "disabled", Enabled = false, Bounds = new UiRect( 0, 200, 10, 10 ), Clicked = () => log.Add( "x" ) } );
		screen.Add( new UiButton { Id = "b", Bounds = new UiRect( 0, 300, 10, 10 ), Clicked = () => log.Add( "b" ), Adjusted = direction => log.Add( $"adjust{direction}" ) } );
		screen.Back = () => log.Add( "back" );
		screen.Update( context, UiInput.Key( UiKeys.Down ) );
		Assert.AreEqual( "a", screen.Focused!.Id );
		screen.Update( context, UiInput.Key( UiKeys.Down ) );
		Assert.AreEqual( "b", screen.Focused!.Id, "labels and disabled buttons are skipped" );
		screen.Update( context, UiInput.Key( UiKeys.Down ) );
		Assert.AreEqual( "a", screen.Focused!.Id, "focus wraps" );
		screen.Update( context, UiInput.Key( UiKeys.Up ) );
		screen.Update( context, UiInput.Key( UiKeys.Accept ) );
		screen.Update( context, UiInput.Key( UiKeys.Right ) );
		screen.Update( context, UiInput.Key( UiKeys.Back ) );
		CollectionAssert.AreEqual( new[] { "b", "adjust1", "back" }, log );
	}

	[TestMethod]
	public void OptionRowArrowsAndWheelChangeTheValue()
	{
		var context = FakeContext();
		var screen = new UiScreen( "test" );
		var value = 5;
		var row = screen.Add( new UiOptionRow { Id = "row", Bounds = new UiRect( 200, 200, 1400, 80 ), Changed = direction => value += direction } );
		var (left, right) = row.ArrowRects( context.Canvas );
		screen.Update( context, UiInput.Click( left.Center ) );
		Assert.AreEqual( 4, value );
		screen.Update( context, UiInput.Click( right.Center ) );
		screen.Update( context, UiInput.Click( row.ScreenRect( context.Canvas ).Center ) );
		Assert.AreEqual( 6, value );
		screen.Update( context, new UiInput( right.Center, false, false, false, false, UiKeys.None, -1 ) );
		Assert.AreEqual( 5, value );
		Assert.IsTrue( left.X > row.ScreenRect( context.Canvas ).X && right.Right < row.ScreenRect( context.Canvas ).Right );
	}

	[TestMethod]
	public void ModalScreensCoverTheParkAndNonModalOnlyTheirElements()
	{
		var canvas = new UiCanvas( 1024, 768 );
		var stack = new UiScreenStack();
		var hud = new UiScreen( "hud" ) { Modal = false };
		hud.Add( new UiLabel { Bounds = new UiRect( 0, 0, 100, 100 ), Anchor = UiAnchor.TopLeft } );
		stack.Push( hud );
		Assert.IsTrue( stack.Covers( canvas, new NVector2( 10, 10 ) ) );
		Assert.IsFalse( stack.Covers( canvas, new NVector2( 500, 500 ) ) );
		stack.Push( new UiScreen( "pause" ) );
		Assert.IsTrue( stack.Covers( canvas, new NVector2( 500, 500 ) ) );
		stack.Pop();
		Assert.AreSame( hud, stack.Top );
	}

	[TestMethod]
	public void FrontEndMenuNavigatesIslandsGameModeAndQuit()
	{
		var islands = new[] { "jungle", "fantasy", "hallow", "space" }.Select( ( level, index ) =>
			new LobbyIslandInfo( index, level, "data\\lobby\\terrain", $"{level[..3]}_isle", $"{level[..3]}_gate", level, 0, 10, (0, 0, 0), Array.Empty<string>(), false, 0 ) ).ToArray();
		var started = new List<(string, GameMode)>();
		var selected = new List<string>();
		var quit = 0;
		var menu = new FrontEndMenu( FakeStrings(), islands, new FrontEndActions
		{
			StartPark = ( island, mode ) => started.Add( (island.Level, mode) ),
			IslandSelected = island => selected.Add( island.Level ),
			Quit = () => quit++,
		} );
		var context = FakeContext();
		Assert.AreEqual( "enterPark", menu.Main.Focused!.Id );
		Assert.AreEqual( "Lost Kingdom", menu.SelectedName );
		menu.Stack.Update( context, UiInput.Key( UiKeys.Left ) );
		Assert.AreEqual( "space", menu.Selected.Level, "left from the first island wraps to the last" );
		Assert.AreEqual( "Space Zone", menu.SelectedName );
		menu.Stack.Update( context, UiInput.Click( menu.Main.Find( "nextIsland" )!.ScreenRect( context.Canvas ).Center ) );
		Assert.AreEqual( "jungle", menu.Selected.Level );
		CollectionAssert.AreEqual( new[] { "space", "jungle" }, selected );
		Assert.AreEqual( "help344", menu.Main.Find( "nextIsland" )!.Help, "original tooltip UIHELPTEXT 344" );

		menu.Main.Focus( menu.Main.Find( "enterPark" ) );
		menu.Stack.Update( context, UiInput.Key( UiKeys.Accept ) );
		Assert.AreEqual( "gameMode", menu.Stack.Top!.Name );
		menu.Stack.Update( context, UiInput.Key( UiKeys.Back ) );
		Assert.AreEqual( "lobby", menu.Stack.Top!.Name );
		menu.ShowGameMode();
		var full = menu.Stack.Top!.Find( "fullSimulation" )!;
		menu.Stack.Update( context, UiInput.Click( full.ScreenRect( context.Canvas ).Center ) );
		CollectionAssert.AreEqual( new[] { ("jungle", GameMode.FullSimulation) }, started );

		menu.Stack.Clear();
		menu.Stack.Push( menu.Main );
		menu.Stack.Update( context, UiInput.Key( UiKeys.Back ) );
		Assert.AreEqual( "confirmQuit", menu.Stack.Top!.Name );
		Assert.AreEqual( "ui9", ((UiLabel)menu.Stack.Top.Find( "message" )!).Text(), "original QUIT GAME confirmation" );
		menu.Stack.Update( context, UiInput.Key( UiKeys.Accept ) );
		Assert.AreEqual( 1, quit );
	}

	[TestMethod]
	public void LoadScreenListsParksWithOriginalAndSupplementaryLabels()
	{
		var stack = new UiScreenStack();
		var loaded = new List<ParkLoadEntry>();
		var entries = new[] { new ParkLoadEntry( "jungle", 0, true ), new ParkLoadEntry( "jungle", 0, false ) };
		var screen = FrontEndMenu.CreateLoadScreen( stack, FakeStrings(), entries, loaded.Add );
		stack.Push( screen );
		Assert.AreEqual( "ui202", ((UiLabel)screen.Find( "title" )!).Text() );
		Assert.AreEqual( "OpenTPW sandbox park", ((UiListItem)screen.Find( "load:sandbox" )!).Text() );
		Assert.AreEqual( "Lost Kingdom - original park (read-only)", ((UiListItem)screen.Find( "load:jungle" )!).Text() );
		stack.Update( FakeContext(), UiInput.Key( UiKeys.Accept ) );
		Assert.AreSame( entries[0], loaded.Single() );
		var empty = FrontEndMenu.CreateLoadScreen( stack, FakeStrings(), Array.Empty<ParkLoadEntry>(), loaded.Add );
		Assert.AreEqual( "No saved parks", ((UiLabel)empty.Find( "empty" )!).Text() );
	}

	[TestMethod]
	public void LobbyFilesParseIntoIslandsAndCamera()
	{
		var definition = new LobbyDefinition();
		definition.ParseLobby( "ISLANDFOV(100)\r\n\r\nSPINSPEED(0.02)\r\nSPINRADIUS(70)\r\nVERTICALOFFSET(20)\r\nISLANDCAMERAPOSITION(0,400,400);\r\nISLANDCAMERAPOSITION(1,600,400);\r\n" );
		var island = definition.ParseTheme( "jungle", "ISLAND(0,\"data\\lobby\\terrain\",\"jun_isle\",\"jun_gate\",\"Lost Kingdom\",90.0,12.5)\r\nFLYINGMESH(\"data\\lobby\\terrain\",\"bfly_PINK\",10,200.0,100.0,200.0,1.5)\r\nSKYCOLOUR(243,203,191)\r\nRAINY(1)\r\nLIGHTNING(63)\r\n" )!;
		Assert.AreEqual( 100f, definition.FieldOfView );
		Assert.AreEqual( 0.02f, definition.SpinSpeed );
		Assert.AreEqual( 70f, definition.SpinRadius );
		Assert.AreEqual( 20f, definition.VerticalOffset );
		Assert.AreEqual( (400f, 400f), definition.PositionOf( island ) );
		Assert.AreEqual( "jun_isle", island.IslandModel );
		Assert.AreEqual( "jun_gate", island.GateModel );
		Assert.AreEqual( "Lost Kingdom", island.Name );
		Assert.AreEqual( 90f, island.Angle );
		Assert.AreEqual( 12.5f, island.Height );
		Assert.AreEqual( ((byte)243, (byte)203, (byte)191), island.SkyColour );
		CollectionAssert.AreEqual( new[] { "bfly_PINK" }, island.FlyingMeshes.ToArray() );
		Assert.IsTrue( island.Rainy );
		Assert.AreEqual( 63, island.Lightning );
		Assert.AreEqual( 0, island.ThemeNameIndex );
		Assert.AreEqual( 1, island with { Level = "hallow" } is { ThemeNameIndex: 1 } ? 1 : 0 );
	}

	// ---- Options ---------------------------------------------------------------------------

	[TestMethod]
	public void OptionLabelsUseOriginalStringsWhereTheyExist()
	{
		var strings = FakeStrings();
		var display = new StubDisplaySettings();
		Assert.AreEqual( "ui341", OptionsScreen.ResolutionLabel( strings, new Point2( 640, 480 ) ) );
		Assert.AreEqual( "ui346", OptionsScreen.ResolutionLabel( strings, new Point2( 400, 300 ) ) );
		Assert.AreEqual( " 2560 x 1440", OptionsScreen.ResolutionLabel( strings, new Point2( 2560, 1440 ) ) );
		Assert.AreEqual( " Native", OptionsScreen.RenderScaleLabel( strings, display, 100 ) );
		Assert.AreEqual( " 77%", OptionsScreen.RenderScaleLabel( strings, display, 77 ) );
		Assert.AreEqual( "ui339 85%", OptionsScreen.RenderScaleLabel( strings, display, 85 ) );
		Assert.AreEqual( " Automatic", OptionsScreen.UiScaleLabel( strings, 0 ) );
		Assert.AreEqual( " 2x", OptionsScreen.UiScaleLabel( strings, 2 ) );
		Assert.AreEqual( " Full screen", OptionsScreen.WindowModeLabel( strings, WindowMode.Exclusive ) );
		Assert.AreEqual( " Nearest neighbour", OptionsScreen.UpscaleLabel( strings, UpscaleMode.Nearest ) );
		Assert.AreEqual( 2, OptionsScreen.Cycle( new[] { 1, 2, 3 }, 1, 1 ) );
		Assert.AreEqual( 3, OptionsScreen.Cycle( new[] { 1, 2, 3 }, 3, 1 ), "cycling stops at the ends" );
		Assert.AreEqual( 1, OptionsScreen.Cycle( new[] { 1, 2, 3 }, 9, -1 ) );
	}

	[TestMethod]
	public void OptionsReportsTheFittedInterfaceScaleWithoutChangingTheRequest()
	{
		var display = new StubDisplaySettings( DisplaySettings.Default with { Width = 1280, Height = 720, UiScale = 2 } );
		var screen = OptionsScreen.Create( new UiScreenStack(), FakeStrings(), new OptionsServices
		{
			Display = display, Options = new GameOptions(), Languages = new[] { "English" }, CurrentLanguage = "English"
		}, () => { } );
		StringAssert.Contains( ((UiLabel)screen.Find( "effective" )!).Text(), "Interface scale: 2x -> 1x" );
		Assert.AreEqual( 2, display.Current.UiScale );
	}

	[TestMethod]
	public void OptionsCancelDiscardsAndAcceptUsesTheDisplayKeepOrRevertFlow()
	{
		var context = FakeContext();
		var stack = new UiScreenStack();
		var options = new GameOptions();
		var display = new StubDisplaySettings( DisplaySettings.Default with { Width = 1280, Height = 720 } );
		var saves = 0;
		var languages = new List<string>();
		var closed = 0;
		OptionsServices Services() => new()
		{
			Display = display, Options = options, Languages = new[] { "Dutch", "English" }, CurrentLanguage = "English",
			SaveOptions = () => saves++, SaveLanguage = languages.Add
		};
		var screen = OptionsScreen.Create( stack, FakeStrings(), Services(), () => closed++ );
		stack.Push( screen );
		Assert.AreEqual( "ui314", ((UiLabel)screen.Find( "title" )!).Text() );
		Assert.AreEqual( "ui318", ((UiOptionRow)screen.Find( "resolution" )!).Label() );
		Assert.AreEqual( "Upscaling:", ((UiOptionRow)screen.Find( "upscaling" )!).Label() );
		((UiOptionRow)screen.Find( "effects" )!).Adjust( 1 );
		((UiOptionRow)screen.Find( "resolution" )!).Adjust( 1 );
		Assert.AreEqual( 9, options.SoundEffectsVolume );
		stack.Update( context, UiInput.Key( UiKeys.Back ) );
		Assert.AreEqual( 8, options.SoundEffectsVolume, "cancel restores the volume" );
		Assert.AreEqual( (0, 0, 1), (saves, display.Applies, closed), "cancel applies nothing" );

		// Upscaling, render scale and UI scale apply directly.
		screen = OptionsScreen.Create( stack, FakeStrings(), Services(), () => closed++ );
		stack.Push( screen );
		((UiOptionRow)screen.Find( "upscaling" )!).Adjust( 1 );
		((UiOptionRow)screen.Find( "renderScale" )!).Adjust( 1 );
		((UiOptionRow)screen.Find( "uiScale" )!).Adjust( 1 );
		Assert.AreEqual( "ui339 70%", ((UiOptionRow)screen.Find( "renderScale" )!).Value() );
		screen.Find( "ok" )!.Activate();
		Assert.AreEqual( (UpscaleMode.Linear, 70, 1, false), (display.Current.Upscale, display.Current.RenderScale, display.Current.UiScale, display.IsConfirmationPending) );
		Assert.AreEqual( 2, closed );

		// A new size asks to keep it (UITEXT 400); "No" reverts and reports UITEXT 401.
		screen = OptionsScreen.Create( stack, FakeStrings(), Services(), () => closed++ );
		stack.Push( screen );
		((UiOptionRow)screen.Find( "resolution" )!).Adjust( 1 );
		var sizes = display.GetResolutions( WindowMode.Windowed ).ToList();
		var chosen = sizes[sizes.FindIndex( size => size.X == 1280 && size.Y == 720 ) + 1];
		Assert.AreEqual( OptionsScreen.ResolutionLabel( FakeStrings(), chosen ), ((UiOptionRow)screen.Find( "resolution" )!).Value() );
		screen.Find( "ok" )!.Activate();
		Assert.IsTrue( display.IsConfirmationPending );
		Assert.AreEqual( (chosen.X, chosen.Y), (display.Current.Width, display.Current.Height) );
		Assert.AreEqual( "confirmDisplay", stack.Top!.Name );
		StringAssert.StartsWith( ((UiLabel)stack.Top.Find( "message" )!).Text(), "ui400" );
		stack.Update( context, UiInput.Key( UiKeys.Back ) );
		Assert.AreEqual( (1280, 720), (display.Current.Width, display.Current.Height) );
		Assert.AreEqual( "restored", stack.Top!.Name );
		Assert.AreEqual( "ui401", ((UiLabel)stack.Top.Find( "message" )!).Text() );
		stack.Update( context, UiInput.Key( UiKeys.Accept ) );
		Assert.AreEqual( 3, closed );

		// "Yes" keeps; the display's own timeout also leads to UITEXT 401.
		screen = OptionsScreen.Create( stack, FakeStrings(), Services(), () => closed++ );
		stack.Push( screen );
		((UiOptionRow)screen.Find( "windowMode" )!).Adjust( 1 );
		screen.Find( "ok" )!.Activate();
		stack.Update( context, UiInput.Key( UiKeys.Accept ) );
		Assert.AreEqual( (WindowMode.Borderless, false), (display.Current.Mode, display.IsConfirmationPending) );
		screen = OptionsScreen.Create( stack, FakeStrings(), Services(), () => closed++ );
		stack.Push( screen );
		((UiOptionRow)screen.Find( "windowMode" )!).Adjust( -1 );
		screen.Find( "ok" )!.Activate();
		display.ExpireConfirmation();
		stack.Update( context, UiInput.Idle( new NVector2( -1, -1 ) ) );
		Assert.AreEqual( "restored", stack.Top!.Name );
		Assert.AreEqual( WindowMode.Borderless, display.Current.Mode );
		stack.Pop();

		// Language: stored for the next start, original RESTART GAME message (UITEXT 402).
		screen = OptionsScreen.Create( stack, FakeStrings(), Services(), () => closed++ );
		stack.Push( screen );
		((UiOptionRow)screen.Find( "language" )!).Adjust( -1 );
		Assert.AreEqual( " Nederlands", ((UiOptionRow)screen.Find( "language" )!).Value() );
		screen.Find( "ok" )!.Activate();
		CollectionAssert.AreEqual( new[] { "Dutch" }, languages );
		Assert.AreEqual( "restart", stack.Top!.Name );
		Assert.AreEqual( "ui402", ((UiLabel)stack.Top.Find( "message" )!).Text(), "original RESTART GAME message" );
	}

	[TestMethod]
	public void GameOptionsRoundTripAndClamp()
	{
		var path = Path.Combine( Path.GetTempPath(), $"opentpw-options-{Guid.NewGuid():N}.json" );
		try
		{
			new GameOptions { MusicVolume = 3, SoundEffectsVolume = 42, PopupHelp = false }.Save( path );
			var loaded = GameOptions.Load( path );
			Assert.AreEqual( (3, 10, false), (loaded.MusicVolume, loaded.SoundEffectsVolume, loaded.PopupHelp) );
			Assert.AreEqual( 0.3f, GameOptions.Gain( 3 ), 1e-6f );
			File.WriteAllText( path, "{ not json" );
			Assert.AreEqual( 8, GameOptions.Load( path ).MusicVolume, "corrupt files fall back to defaults" );
			Assert.AreEqual( 8, GameOptions.Load( path + ".missing" ).MusicVolume );
		}
		finally
		{
			File.Delete( path );
		}
	}

	// ---- Supplementary strings and HUD stubs ----------------------------------------------

	[TestMethod]
	public void SupplementaryStringsAreTranslatedForAllSixLanguagesWithMatchingPlaceholders()
	{
		CollectionAssert.AreEquivalent( GameLanguage.ShippedLanguages.ToArray(), SupplementaryStrings.SupportedLanguages.ToArray() );
		foreach ( var key in Enum.GetValues<OpenTpwText>() )
		{
			var english = SupplementaryStrings.Get( key, "English" );
			var placeholders = System.Text.RegularExpressions.Regex.Matches( english, @"\{\d\}" ).Select( match => match.Value ).OrderBy( value => value ).ToArray();
			foreach ( var language in SupplementaryStrings.SupportedLanguages )
			{
				var text = SupplementaryStrings.Get( key, language );
				Assert.IsFalse( string.IsNullOrWhiteSpace( text ), $"{key} {language}" );
				CollectionAssert.AreEqual( placeholders, System.Text.RegularExpressions.Regex.Matches( text, @"\{\d\}" ).Select( match => match.Value ).OrderBy( value => value ).ToArray(), $"{key} {language}" );
				if ( language != "English" && key is not (OpenTpwText.UpscaleLinear or OpenTpwText.UpscaleNative) )
					Assert.AreNotEqual( english, text, $"{key} is translated to {language}" );
			}
		}
		Assert.AreEqual( "Taal:", SupplementaryStrings.Get( OpenTpwText.Language, "dutch" ) );
		Assert.AreEqual( "Language:", SupplementaryStrings.Get( OpenTpwText.Language, "Klingon" ) );
	}

	[TestMethod]
	public void ApproximationRegisterIdsAreUniqueAndDescribed()
	{
		var ids = UiApproximations.All.Select( entry => entry.Id ).ToArray();
		CollectionAssert.AllItemsAreUnique( ids );
		Assert.IsTrue( UiApproximations.All.All( entry => entry.Id.StartsWith( "UI-" ) && entry.Assumption.Length > 10 && entry.EvidenceNeeded.Length > 3 ) );
	}

	[TestMethod]
	public void HudStatusFollowsTheParkEconomyAndItsReplacement()
	{
		var first = EconomyTestData.Park( initialCash: 50000 );
		ParkEconomy? current = first;
		var status = new EconomyParkStatus( () => current );
		Assert.IsTrue( status.HasEconomy );
		Assert.AreEqual( first.Balance, status.Money );
		Assert.AreEqual( first.Date, status.Date );
		status.Speed = GameSpeed.Paused;
		Assert.AreEqual( GameSpeed.Paused, first.Speed );
		Assert.AreEqual( 0f, status.TimeScale );
		status.Speed = GameSpeed.Fastest;
		Assert.AreEqual( 1f, status.TimeScale, "the economy speeds up its own clock; rides keep normal speed" );
		first.AdvanceDays( 31 );
		Assert.AreEqual( first.Date, status.Date );
		var item = new BuildItem( "shop", 1203, BuildCategory.Shops, 0, 999, null, Array.Empty<string>() );
		Assert.IsTrue( first.Catalog.TryGet( 1203, out var info ) );
		Assert.AreEqual( info.PurchaseCost, status.PriceOf( item ), "price from the economy catalogue" );
		Assert.IsTrue( status.IsAvailable( item ) );
		var locked = item with { InfoId = 1180 };
		Assert.IsFalse( status.IsAvailable( locked ), "unresearched catalogue items cannot be placed" );
		first.Research.Restore( first.Research.Completed.Append( (1180, 0) ).Distinct().ToArray(), first.Research.ProgressEntries.ToArray(), first.Research.Effort );
		Assert.IsTrue( status.IsAvailable( locked ), "the HUD follows completed research" );
		Assert.IsFalse( status.IsAvailable( item with { InfoId = -1 } ), "objects outside the theme catalogue cannot be placed" );
		// Loading a park save replaces the economy object: the HUD follows the new one.
		var loaded = EconomyTestData.Park( initialCash: 1234 );
		current = loaded;
		Assert.AreEqual( 1234, status.Money );
		Assert.IsFalse( status.IsAvailable( locked ), "research follows the loaded economy too" );
		current = null;
		Assert.IsFalse( status.HasEconomy );
	}

	[TestMethod]
	public void LevelsWithoutEconomyHideMoneyAndOnlyPause()
	{
		var status = new NoEconomyStatus();
		Assert.IsFalse( status.HasEconomy );
		Assert.IsNull( status.PriceOf( new BuildItem( "x", 1, BuildCategory.Rides, 0, 5, null, Array.Empty<string>() ) ) );
		status.Speed = GameSpeed.Fast;
		Assert.AreEqual( 1f, status.TimeScale );
		status.Speed = GameSpeed.Paused;
		Assert.AreEqual( 0f, status.TimeScale );
	}

	[TestMethod]
	public void UiImagePinkKeyBecomesTransparentWithoutPinkFringes()
	{
		var rgba = new byte[] { 255, 0, 255, 255, 10, 20, 30, 255, 255, 0, 255, 255, 50, 60, 70, 255 };
		UiImages.KeyOutPink( rgba, 2, 2 );
		Assert.AreEqual( 0, rgba[3] );
		Assert.AreEqual( 0, rgba[11] );
		Assert.AreEqual( 255, rgba[7] );
		CollectionAssert.AreEqual( new byte[] { 30, 40, 50 }, rgba[..3], "keyed texels take the mean neighbour colour" );
	}

	[TestMethod]
	public void BatchTextRecordsGlyphsAtIntegerScale()
	{
		var font = new FontAtlas( FontAtlasTests.CreateFont( 4, new FontAtlasTests.SyntheticGlyph( 'A', 2, 3, 0, 0, 3, Enumerable.Repeat( (byte)15, 6 ).ToArray() ) ) );
		var batch = new UiBatch();
		batch.AddText( font, TextLayout.Create( font, "AA" ), 10, 20, Veldrid.RgbaByte.White, 2, "AA" );
		Assert.AreEqual( 2, batch.Glyphs.Count );
		Assert.AreEqual( (10, 20, 4, 6, 2), (batch.Glyphs[0].X, batch.Glyphs[0].Y, batch.Glyphs[0].Width, batch.Glyphs[0].Height, batch.Glyphs[0].Scale) );
		Assert.AreEqual( 16, batch.Glyphs[1].X );
		Assert.AreEqual( "AA", batch.Texts.Single().Text );
		Assert.AreEqual( 12, batch.Draws.Single().Vertices.Count );
	}
}

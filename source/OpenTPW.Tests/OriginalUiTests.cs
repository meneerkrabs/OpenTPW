using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
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
	public void RetinaCanvasMatchesAOneTimesCanvasOfTheSameLogicalSize()
	{
		var retina = new UiCanvas( 2432, 1368, 2, PixelDensity: 2 );
		var standard = new UiCanvas( 1216, 684, 1 );
		Assert.AreEqual( 2, retina.TextScale, "below 1280x720 points a Retina window keeps 2x text" );
		Assert.AreEqual( standard.FontTier, retina.FontTier );
		Assert.AreEqual( standard.LogicalScale, retina.LogicalScale, 1e-6f );
		Assert.AreEqual( 1, new UiCanvas( 2432, 1368, 2 ).TextScale, "without the density the pixel-only fit still applies" );
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
	public void TheWheelAdjustsOnlyButtonsThatOptIn()
	{
		var context = FakeContext();
		var screen = new UiScreen( "test" );
		var lobby = 0;
		var cycle = 0;
		var island = screen.Add( new UiButton { Id = "options", Bounds = new UiRect( 200, 200, 400, 100 ), Adjusted = direction => lobby += direction } );
		var row = screen.Add( new UiButton { Id = "cycle", Bounds = new UiRect( 200, 400, 400, 100 ), Adjusted = direction => cycle += direction, WheelAdjusts = true } );
		screen.Update( context, new UiInput( island.ScreenRect( context.Canvas ).Center, false, false, false, false, UiKeys.None, 1 ) );
		screen.Update( context, new UiInput( row.ScreenRect( context.Canvas ).Center, false, false, false, false, UiKeys.None, -1 ) );
		Assert.AreEqual( (0, -1), (lobby, cycle), "the wheel over a lobby button does not turn the island" );
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

	private static OptionsServices OptionsServicesFor( GameOptions options, StubDisplaySettings display, Action? saved = null, ICollection<string>? languages = null, IGraphicsSettings? graphics = null ) => new()
	{
		Display = display, Options = options, Languages = new[] { "Dutch", "English" }, CurrentLanguage = "English", Graphics = graphics,
		SaveOptions = saved ?? ( () => { } ), SaveLanguage = language => languages?.Add( language )
	};

	/// <summary>Framebuffer position of an authored point on the 1024x768 test canvas (centre anchor).</summary>
	private static NVector2 Pixel( UiCanvas canvas, float x, float y ) => canvas.Map( new NVector2( x, y ), UiAnchor.Center );

	[TestMethod]
	public void OptionsScreenFollowsTheOriginalLayoutAndLabels()
	{
		var display = new StubDisplaySettings( DisplaySettings.Default with { Width = 800, Height = 600 } );
		var screen = OptionsScreen.Create( new UiScreenStack(), FakeStrings(), OptionsServicesFor( new GameOptions(), display ), () => { } );
		Assert.AreEqual( "ui314", ((UiLabel)screen.Find( "title" )!).Text() );
		Assert.AreEqual( new UiRect( 0, 0, 2048, 1536 ), screen.Find( "window" )!.Bounds, "f_screen covers the authored canvas" );
		Assert.AreEqual( "ui315", ((UiLabel)screen.Find( "gpuLabel" )!).Text() );
		Assert.AreEqual( "ui347ui348", ((UiLabel)screen.Find( "videocardLabel" )!).Text() );
		Assert.AreEqual( "ui318ui342", ((UiLabel)screen.Find( "resolutionLabel" )!).Text() );
		Assert.AreEqual( "ui319 100 %", ((UiLabel)screen.Find( "audioLabel" )!).Text() );
		Assert.AreEqual( "ui320 80 %", ((UiLabel)screen.Find( "effectsLabel" )!).Text() );
		Assert.AreEqual( "ui324ui334", ((UiLabel)screen.Find( "advisorLabel" )!).Text() );
		Assert.AreEqual( "ui350ui352", ((UiLabel)screen.Find( "rotationLabel" )!).Text(), "90 degs is the default" );
		Assert.AreEqual( "ui353ui354", ((UiLabel)screen.Find( "scrollLabel" )!).Text() );
		foreach ( var id in new[] { "gpu", "videocard" } )
			Assert.IsFalse( screen.Find( id )!.Enabled, id );
		Assert.IsFalse( screen.Find( "audio" )!.Enabled );
		Assert.AreEqual( new UiRect( 962, 331, 319, 135 ), screen.Find( "resolution" )!.Bounds, "slider hit region of the table" );
		Assert.IsNull( screen.Find( "windowMode" ), "OpenTPW rows moved to the OpenTPW page" );
		foreach ( var id in new[] { "ok", "cancel", "openTpw" } )
			Assert.IsNotNull( screen.Find( id ), id );
	}

	[TestMethod]
	public void SliderMapsKnobAndMouseLinearly()
	{
		var value = 0;
		var slider = new UiSlider { Track = new UiRect( 990, 361, 258, 71 ), KnobSize = new NVector2( 67, 67 ), KnobTop = 364, Bounds = new UiRect( 962, 331, 319, 135 ), Steps = () => 11, Value = () => value, Changed = v => value = v };
		Assert.AreEqual( 990f, slider.KnobRect( 0 ).X );
		Assert.AreEqual( 990f + 258 - 67, slider.KnobRect( 10 ).X );
		Assert.AreEqual( 990f + (258 - 67) / 2f, slider.KnobRect( 5 ).X, 1e-3f );
		Assert.AreEqual( 0, slider.ValueAt( 0 ), "left of the track clamps" );
		Assert.AreEqual( 10, slider.ValueAt( 5000 ), "right of the track clamps" );
		Assert.AreEqual( 5, slider.ValueAt( 990 + 67 / 2f + (258 - 67) / 2f ) );
		slider.Adjust( 1 );
		slider.Adjust( 1 );
		Assert.AreEqual( 2, value );
		slider.Adjust( -5 );
		Assert.AreEqual( 0, value, "stepping clamps at the ends" );
		slider.Enabled = false;
		slider.Adjust( 1 );
		Assert.AreEqual( 0, value, "disabled sliders ignore input" );
	}

	[TestMethod]
	public void SliderFollowsClicksDragsWheelAndKeys()
	{
		var context = FakeContext();
		var options = new GameOptions();
		var stack = new UiScreenStack();
		var screen = OptionsScreen.Create( stack, FakeStrings(), OptionsServicesFor( options, new StubDisplaySettings() ), () => { } );
		stack.Push( screen );
		var slider = (UiSlider)screen.Find( "music" )!;
		var track = slider.Track;
		var y = slider.Bounds.Y + slider.Bounds.Height / 2;
		float XOf( int step ) => track.X + slider.KnobSize.X / 2 + (track.Width - slider.KnobSize.X) * step / GameOptions.MaximumVolume;

		stack.Update( context, UiInput.Click( Pixel( context.Canvas, XOf( 3 ), y ) ) );
		Assert.AreEqual( 3, options.MusicVolume, "a click sets the value under the mouse" );

		// Press, drag outside the hit region, release: the slider follows the mouse in x.
		var down = new UiInput( Pixel( context.Canvas, XOf( 6 ), y ), true, true, false, false, UiKeys.None );
		stack.Update( context, down );
		Assert.AreEqual( 6, options.MusicVolume );
		stack.Update( context, new UiInput( Pixel( context.Canvas, XOf( 9 ), y + 400 ), true, false, false, false, UiKeys.None ) );
		Assert.AreEqual( 9, options.MusicVolume, "dragging past the hit region keeps following" );
		stack.Update( context, new UiInput( Pixel( context.Canvas, XOf( 10 ) + 500, y ), false, false, true, false, UiKeys.None ) );
		Assert.AreEqual( 10, options.MusicVolume );

		stack.Update( context, new UiInput( Pixel( context.Canvas, XOf( 4 ), y ), false, false, false, false, UiKeys.None, 1 ) );
		Assert.AreEqual( 10, options.MusicVolume, "wheel up steps up (clamped here)" );
		stack.Update( context, new UiInput( Pixel( context.Canvas, XOf( 4 ), y ), false, false, false, false, UiKeys.None, -1 ) );
		Assert.AreEqual( 9, options.MusicVolume );
		stack.Update( context, UiInput.Key( UiKeys.Left ) );
		Assert.AreEqual( 8, options.MusicVolume, "Left steps the focused slider" );

		// A disabled slider (audio quality) takes no input and is not hit.
		var audio = (UiSlider)screen.Find( "audio" )!;
		stack.Update( context, UiInput.Click( Pixel( context.Canvas, audio.Track.X, audio.Bounds.Y + 10 ) ) );
		Assert.AreEqual( 10, audio.Value() );
	}

	[TestMethod]
	public void OptionsCancelDiscardsEveryEditedOptionAndOkSavesThem()
	{
		var context = FakeContext();
		var stack = new UiScreenStack();
		var options = new GameOptions();
		var display = new StubDisplaySettings( DisplaySettings.Default with { Width = 1280, Height = 720 } );
		var saves = 0;
		var closed = 0;
		UiScreen Open()
		{
			var screen = OptionsScreen.Create( stack, FakeStrings(), OptionsServicesFor( options, display, () => saves++ ), () => closed++ );
			stack.Push( screen );
			return screen;
		}
		var screen = Open();
		((UiSlider)screen.Find( "effects" )!).Adjust( 1 );
		screen.Find( "musicOn" )!.Activate();
		screen.Find( "advisor" )!.Activate();
		screen.Find( "tutorial" )!.Activate();
		screen.Find( "popupHelp" )!.Activate();
		screen.Find( "confirmations" )!.Activate();
		screen.Find( "rmbCancel" )!.Activate();
		screen.Find( "rotation" )!.Activate();
		screen.Find( "scroll" )!.Activate();
		((UiSlider)screen.Find( "resolution" )!).Adjust( 1 );
		Assert.AreEqual( (9, false, false, false, false, false, false, RotationMode.Smooth, ScrollMode.RightButton),
			(options.SoundEffectsVolume, options.MusicOn, options.Advisor, options.Tutorial, options.PopupHelp, options.Confirmations, options.RmbCancel, options.Rotation, options.Scroll) );
		Assert.AreEqual( "ui320 90 %", ((UiLabel)screen.Find( "effectsLabel" )!).Text() );
		Assert.AreEqual( "ui324ui333", ((UiLabel)screen.Find( "advisorLabel" )!).Text() );
		stack.Update( context, UiInput.Key( UiKeys.Back ) );
		var defaults = new GameOptions();
		Assert.AreEqual( JsonSerializer.Serialize( defaults ), JsonSerializer.Serialize( options ), "Escape restores every field" );
		Assert.AreEqual( (0, 0, 1, 0), (saves, display.Applies, closed, stack.Screens.Count) );

		screen = Open();
		screen.Find( "cancel" )!.Activate();
		Assert.AreEqual( (0, 2, 0), (saves, closed, stack.Screens.Count) );

		screen = Open();
		screen.Find( "movieOn" )!.Activate();
		screen.Find( "ok" )!.Activate();
		Assert.AreEqual( (1, false, 3, 0), (saves, options.MovieOn, closed, stack.Screens.Count) );
		Assert.AreEqual( 0f, options.MovieGain, "the mute toggle silences the movie volume" );
	}

	[TestMethod]
	public void OptionsOkUsesTheDisplayKeepOrRevertFlowAndTheOpenTpwPageEditsTheSamePendingState()
	{
		var context = FakeContext();
		var stack = new UiScreenStack();
		var options = new GameOptions();
		var display = new StubDisplaySettings( DisplaySettings.Default with { Width = 1280, Height = 720 } );
		var languages = new List<string>();
		var closed = 0;
		UiScreen Open()
		{
			var screen = OptionsScreen.Create( stack, FakeStrings(), OptionsServicesFor( options, display, languages: languages ), () => closed++ );
			stack.Push( screen );
			return screen;
		}
		var main = Open();
		// The OpenTPW page: display rows apply directly (no confirmation); Back keeps them pending.
		main.Find( "openTpw" )!.Activate();
		Assert.AreEqual( "openTpwOptions", stack.Top!.Name );
		var page = stack.Top;
		Assert.AreEqual( "Upscaling: Native", ((UiLabel)page.Find( "upscalingLabel" )!).Text() );
		((UiButton)page.Find( "upscaling" )!).Activate();
		((UiButton)page.Find( "renderScale" )!).Adjust( 1 );
		((UiButton)page.Find( "uiScale" )!).Adjust( 1 );
		Assert.AreEqual( "Render scale:ui339 70%", ((UiLabel)page.Find( "renderScaleLabel" )!).Text() );
		Assert.AreEqual( 0, display.Applies, "nothing applied before OK" );
		stack.Update( context, UiInput.Key( UiKeys.Back ) );
		Assert.AreSame( main, stack.Top, "Back returns to the original page" );
		Assert.AreEqual( 0, display.Applies );
		main.Find( "ok" )!.Activate();
		Assert.AreEqual( (UpscaleMode.Linear, 70, 1, false), (display.Current.Upscale, display.Current.RenderScale, display.Current.UiScale, display.IsConfirmationPending) );
		Assert.AreEqual( 1, closed );

		// A new size asks to keep it (UITEXT 400); "No" reverts and reports UITEXT 401.
		main = Open();
		((UiSlider)main.Find( "resolution" )!).Adjust( 1 );
		var sizes = display.GetResolutions( WindowMode.Windowed ).ToList();
		var chosen = sizes[sizes.FindIndex( size => size.X == 1280 && size.Y == 720 ) + 1];
		Assert.AreEqual( "ui318" + OptionsScreen.ResolutionLabel( FakeStrings(), chosen ), ((UiLabel)main.Find( "resolutionLabel" )!).Text() );
		main.Find( "ok" )!.Activate();
		Assert.IsTrue( display.IsConfirmationPending );
		Assert.AreEqual( (chosen.X, chosen.Y), (display.Current.Width, display.Current.Height) );
		Assert.AreEqual( "confirmDisplay", stack.Top!.Name );
		StringAssert.StartsWith( ((UiLabel)stack.Top.Find( "message" )!).Text(), "ui400" );
		stack.Update( context, UiInput.Key( UiKeys.Back ) );
		Assert.AreEqual( (1280, 720), (display.Current.Width, display.Current.Height) );
		Assert.AreEqual( "restored", stack.Top!.Name );
		Assert.AreEqual( "ui401", ((UiLabel)stack.Top.Find( "message" )!).Text() );
		stack.Update( context, UiInput.Key( UiKeys.Accept ) );
		Assert.AreEqual( 2, closed );

		// "Yes" keeps; the display's own timeout also leads to UITEXT 401. Window mode lives on the OpenTPW page.
		main = Open();
		main.Find( "openTpw" )!.Activate();
		((UiButton)stack.Top!.Find( "windowMode" )!).Activate();
		stack.Update( context, UiInput.Key( UiKeys.Back ) );
		main.Find( "ok" )!.Activate();
		stack.Update( context, UiInput.Key( UiKeys.Accept ) );
		Assert.AreEqual( (WindowMode.Borderless, false), (display.Current.Mode, display.IsConfirmationPending) );
		main = Open();
		main.Find( "openTpw" )!.Activate();
		((UiButton)stack.Top!.Find( "windowMode" )!).Adjust( -1 );
		stack.Update( context, UiInput.Key( UiKeys.Back ) );
		main.Find( "ok" )!.Activate();
		display.ExpireConfirmation();
		stack.Update( context, UiInput.Idle( new NVector2( -1, -1 ) ) );
		Assert.AreEqual( "restored", stack.Top!.Name );
		Assert.AreEqual( WindowMode.Borderless, display.Current.Mode );
		stack.Pop();

		// Language: stored for the next start, original RESTART GAME message (UITEXT 402).
		main = Open();
		main.Find( "openTpw" )!.Activate();
		var row = (UiButton)stack.Top!.Find( "language" )!;
		row.Adjust( -1 );
		Assert.AreEqual( "Language: Nederlands", ((UiLabel)stack.Top!.Find( "languageLabel" )!).Text() );
		stack.Update( context, UiInput.Key( UiKeys.Back ) );
		main.Find( "ok" )!.Activate();
		CollectionAssert.AreEqual( new[] { "Dutch" }, languages );
		Assert.AreEqual( "restart", stack.Top!.Name );
		Assert.AreEqual( "ui402", ((UiLabel)stack.Top.Find( "message" )!).Text(), "original RESTART GAME message" );
	}

	[TestMethod]
	public void OpenTpwPageButtonsCycleBothWaysWithWrapAndShareOneLabelGroup()
	{
		Assert.AreEqual( 1, OptionsScreen.CycleWrap( new[] { 1, 2, 3 }, 3, 1 ) );
		Assert.AreEqual( 3, OptionsScreen.CycleWrap( new[] { 1, 2, 3 }, 1, -1 ) );
		var context = FakeContext();
		var stack = new UiScreenStack();
		var main = OptionsScreen.Create( stack, FakeStrings(), OptionsServicesFor( new GameOptions(), new StubDisplaySettings() ), () => { } );
		stack.Push( main );
		main.Find( "openTpw" )!.Activate();
		var page = stack.Top!;
		var mode = (UiLabel)page.Find( "windowModeLabel" )!;
		var button = (UiButton)page.Find( "windowMode" )!;
		Assert.AreEqual( "Window mode: Windowed", mode.Text() );
		var center = button.ScreenRect( context.Canvas ).Center;
		stack.Update( context, new UiInput( center, false, false, false, false, UiKeys.None, 1 ) );
		Assert.AreEqual( "Window mode: Borderless", mode.Text(), "wheel up cycles forward" );
		stack.Update( context, new UiInput( center, false, false, false, false, UiKeys.None, -1 ) );
		stack.Update( context, new UiInput( center, false, false, false, false, UiKeys.None, -1 ) );
		Assert.AreEqual( "Window mode: Full screen", mode.Text(), "wheel down cycles back and wraps" );
		stack.Update( context, UiInput.Click( center ) );
		Assert.AreEqual( "Window mode: Windowed", mode.Text(), "a click cycles forward and wraps" );
		Assert.IsTrue( page.Elements.OfType<UiLabel>().Where( label => label.Id.EndsWith( "Label" ) ).All( label => label.Group != null && label.Shadow == false ), "dark labels without shadow in one group" );
		Assert.AreSame( ((UiLabel)page.Find( "languageLabel" )!).Group, mode.Group );
	}

	[TestMethod]
	public void OpenTpwPageReportsTheFittedInterfaceScaleWithoutChangingTheRequest()
	{
		var display = new StubDisplaySettings( DisplaySettings.Default with { Width = 1280, Height = 720, UiScale = 2 } );
		var stack = new UiScreenStack();
		var main = OptionsScreen.Create( stack, FakeStrings(), OptionsServicesFor( new GameOptions(), display ), () => { } );
		stack.Push( main );
		main.Find( "openTpw" )!.Activate();
		StringAssert.Contains( ((UiLabel)stack.Top!.Find( "effective" )!).Text(), "Interface scale: 2x -> 1x" );
		Assert.AreEqual( 2, display.Current.UiScale );
	}

	[TestMethod]
	public void GraphicsQualitySliderStepsThroughThePresetsAndAppliesOnOk()
	{
		var graphics = new GraphicsSettingsService( _ => null, GraphicsSettings.Default with { Preset = GraphicsPreset.Medium }, null, () => CompatibilityFlags.Original );
		var steps = OptionsScreen.QualitySteps( graphics );
		CollectionAssert.DoesNotContain( steps.ToList(), GraphicsPreset.Custom, "Custom only shows while it is current" );
		var stack = new UiScreenStack();
		var screen = OptionsScreen.Create( stack, FakeStrings(), OptionsServicesFor( new GameOptions(), new StubDisplaySettings(), graphics: graphics ), () => { } );
		stack.Push( screen );
		Assert.AreEqual( "ui317ui336", ((UiLabel)screen.Find( "qualityLabel" )!).Text() );
		Assert.AreEqual( "ui336", OptionsScreen.QualityLabel( FakeStrings(), GraphicsPreset.Medium ) );
		Assert.AreEqual( GraphicsPreset.Medium, graphics.Current.Preset, "nothing applied before OK" );
	}

	[TestMethod]
	public void GameOptionsRoundTripClampAndDefaultNewFields()
	{
		var path = Path.Combine( Path.GetTempPath(), $"opentpw-options-{Guid.NewGuid():N}.json" );
		try
		{
			new GameOptions { MusicVolume = 3, SoundEffectsVolume = 42, PopupHelp = false, SpeechOn = false, Rotation = RotationMode.Smooth, Scroll = ScrollMode.RightButton, Tutorial = false }.Save( path );
			var loaded = GameOptions.Load( path );
			Assert.AreEqual( (3, 10, false), (loaded.MusicVolume, loaded.SoundEffectsVolume, loaded.PopupHelp) );
			Assert.AreEqual( (false, RotationMode.Smooth, ScrollMode.RightButton, false), (loaded.SpeechOn, loaded.Rotation, loaded.Scroll, loaded.Tutorial) );
			Assert.AreEqual( 0.3f, GameOptions.Gain( 3 ), 1e-6f );
			Assert.AreEqual( 0f, loaded.SpeechGain, "a muted sound has no gain whatever its volume" );
			Assert.AreEqual( 0.3f, loaded.MusicGain, 1e-6f );
			Assert.AreEqual( 0f, GameOptions.Gain( 8, false ) );

			// Files written before the mute toggles and gameplay switches existed get the defaults.
			File.WriteAllText( path, "{ \"MusicVolume\": 5, \"PopupHelp\": false }" );
			var old = GameOptions.Load( path );
			Assert.AreEqual( (5, false), (old.MusicVolume, old.PopupHelp) );
			Assert.IsTrue( old.SoundEffectsOn && old.MusicOn && old.SpeechOn && old.MovieOn && old.Advisor && old.Tutorial && old.Confirmations && old.RmbCancel );
			Assert.AreEqual( (RotationMode.Ninety, ScrollMode.Pushscroll), (old.Rotation, old.Scroll) );
			Assert.AreEqual( 0.5f, old.MusicGain, 1e-6f );

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
				if ( language != "English" && key is not (OpenTpwText.UpscaleLinear or OpenTpwText.UpscaleNative or OpenTpwText.OpenTpwPage) )
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
	public void UiImageBlackKeyFadesTheDarkEdgeOfArtDrawnOnBlack()
	{
		// A row of the ipan disc's edge: black background, the dark anti-aliased fade, then the disc.
		var rgba = new byte[] { 0, 0, 4, 255, 0, 2, 21, 255, 0, 11, 45, 255, 10, 28, 72, 255 };
		UiImages.KeyOutBlackSoft( rgba, 4, 1 );
		Assert.AreEqual( 0, rgba[3], "near-black background is transparent" );
		Assert.AreEqual( 255, rgba[15], "the disc stays opaque" );
		Assert.IsTrue( rgba[7] > 0 && rgba[7] < rgba[11] && rgba[11] < 255, "the fade becomes partly transparent, more so further out" );
		Assert.AreEqual( 21 * 255 / rgba[7], rgba[6], 2, "fade colours are un-premultiplied against black" );
		CollectionAssert.AreEqual( new byte[] { 10, 28, 72, 255 }, rgba[12..], "opaque texels keep their colour" );
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

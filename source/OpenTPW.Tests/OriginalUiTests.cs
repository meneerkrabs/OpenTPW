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
	[DataRow( 640, 480, UiFontTier.Small, 1 )]
	[DataRow( 1024, 768, UiFontTier.Medium, 1 )]
	[DataRow( 1280, 720, UiFontTier.Medium, 1 )]
	[DataRow( 1920, 1080, UiFontTier.Big, 1 )]
	[DataRow( 2560, 1440, UiFontTier.Big, 1 )]
	[DataRow( 3840, 2160, UiFontTier.Big, 2 )]
	public void FontTierAndTextScaleFollowTheOutputSize( int width, int height, UiFontTier tier, int textScale )
	{
		var canvas = new UiCanvas( width, height );
		Assert.AreEqual( tier, canvas.FontTier );
		Assert.AreEqual( textScale, canvas.TextScale );
	}

	[TestMethod]
	public void UserUiScaleEnlargesTheLayoutWithinBounds()
	{
		Assert.AreEqual( 2 * new UiCanvas( 1024, 768 ).Scale, new UiCanvas( 1024, 768, 2f ).Scale, 1e-6f );
		Assert.AreEqual( new UiCanvas( 1024, 768, 3f ).Scale, new UiCanvas( 1024, 768, 10f ).Scale, 1e-6f );
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

	private sealed class FakeDisplay : IDisplaySettings
	{
		public DisplayApplyResult Result { get; set; } = DisplayApplyResult.NeedsConfirmation;
		public int Applies, Confirms, Reverts;
		public IReadOnlyList<DisplayResolution> AvailableResolutions { get; } = new DisplayResolution[] { new( 640, 480 ), new( 1280, 720 ), new( 2560, 1440 ) };
		public DisplayResolution Resolution { get; set; } = new( 1280, 720 );
		public DisplayWindowMode WindowMode { get; set; }
		public DisplayUpscaleMethod UpscaleMethod { get; set; }
		public IReadOnlyList<int> RenderScalePresets { get; } = new[] { 77, 67, 59, 50 };
		public int RenderScalePercent { get; set; } = 100;
		public float UiScale { get; set; } = 1;
		public DisplayResolution EffectiveInternalSize => new( 1280, 720 );
		public DisplayResolution OutputSize => new( 1280, 720 );
		public string? FallbackReason => null;
		public DisplayApplyResult Apply() { Applies++; return Result; }
		public void Confirm() => Confirms++;
		public void Revert() { Reverts++; Resolution = new( 1280, 720 ); }
	}

	[TestMethod]
	public void OptionLabelsUseOriginalStringsWhereTheyExist()
	{
		var strings = FakeStrings();
		var display = new FakeDisplay();
		Assert.AreEqual( "ui341", OptionsScreen.ResolutionLabel( strings, new DisplayResolution( 640, 480 ) ) );
		Assert.AreEqual( "ui346", OptionsScreen.ResolutionLabel( strings, new DisplayResolution( 400, 300 ) ) );
		Assert.AreEqual( " 2560 x 1440", OptionsScreen.ResolutionLabel( strings, new DisplayResolution( 2560, 1440 ) ) );
		Assert.AreEqual( " Native", OptionsScreen.RenderScaleLabel( strings, display, 100 ) );
		Assert.AreEqual( " 77%", OptionsScreen.RenderScaleLabel( strings, display, 77 ) );
		Assert.AreEqual( "ui339 85%", OptionsScreen.RenderScaleLabel( strings, display, 85 ) );
		Assert.AreEqual( 2, OptionsScreen.Cycle( new[] { 1, 2, 3 }, 1, 1 ) );
		Assert.AreEqual( 3, OptionsScreen.Cycle( new[] { 1, 2, 3 }, 3, 1 ), "cycling stops at the ends" );
		Assert.AreEqual( 1, OptionsScreen.Cycle( new[] { 1, 2, 3 }, 9, -1 ) );
	}

	[TestMethod]
	public void OptionsCancelRestoresAndAcceptConfirmsOrRevertsDisplayChanges()
	{
		var context = FakeContext();
		var stack = new UiScreenStack();
		var options = new GameOptions();
		var display = new FakeDisplay();
		var saves = 0;
		var languages = new List<string>();
		var closed = 0;
		OptionsServices Services() => new()
		{
			Display = display, Options = options, Languages = new[] { "Dutch", "English" }, CurrentLanguage = "English",
			SaveOptions = () => saves++, SaveLanguage = languages.Add, ConfirmSeconds = 1
		};
		var screen = OptionsScreen.Create( stack, FakeStrings(), Services(), () => closed++ );
		stack.Push( screen );
		Assert.AreEqual( "ui314", ((UiLabel)screen.Find( "title" )!).Text() );
		Assert.AreEqual( "ui318", ((UiOptionRow)screen.Find( "resolution" )!).Label() );
		Assert.AreEqual( "Upscaling:", ((UiOptionRow)screen.Find( "upscaling" )!).Label() );
		((UiOptionRow)screen.Find( "effects" )!).Adjust( 1 );
		Assert.AreEqual( 9, options.SoundEffectsVolume );
		stack.Update( context, UiInput.Key( UiKeys.Back ) );
		Assert.AreEqual( 8, options.SoundEffectsVolume, "cancel restores the volume" );
		Assert.AreEqual( (0, 1, 1), (saves, display.Reverts, closed) );

		screen = OptionsScreen.Create( stack, FakeStrings(), Services(), () => closed++ );
		stack.Push( screen );
		((UiOptionRow)screen.Find( "resolution" )!).Adjust( 1 );
		Assert.AreEqual( new DisplayResolution( 2560, 1440 ), display.Resolution );
		screen.Find( "ok" )!.Activate();
		Assert.AreEqual( (1, 1), (saves, display.Applies) );
		Assert.AreEqual( "confirmDisplay", stack.Top!.Name );
		StringAssert.StartsWith( ((UiLabel)stack.Top.Find( "message" )!).Text(), "ui400" );
		// No answer within the timeout: revert and report with UITEXT 401.
		context.Delta = 2;
		stack.Update( context, UiInput.Idle( new NVector2( -1, -1 ) ) );
		Assert.AreEqual( 2, display.Reverts );
		Assert.AreEqual( "restored", stack.Top!.Name );
		Assert.AreEqual( "ui401", ((UiLabel)stack.Top.Find( "message" )!).Text() );
		stack.Update( context, UiInput.Key( UiKeys.Accept ) );
		Assert.AreEqual( 2, closed );

		display.Result = DisplayApplyResult.Unchanged;
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
	public void StubDisplayStoresResolutionForTheNextStart()
	{
		var options = new GameOptions();
		var stored = new List<DisplayResolution>();
		var display = new StubDisplaySettings( options, new DisplayResolution( 1280, 720 ), new DisplayResolution( 1280, 720 ), stored.Add );
		CollectionAssert.IsSubsetOf( StubDisplaySettings.OriginalResolutions, display.AvailableResolutions.ToArray() );
		Assert.AreEqual( DisplayApplyResult.Unchanged, display.Apply() );
		display.Resolution = new DisplayResolution( 1024, 768 );
		display.UpscaleMethod = DisplayUpscaleMethod.Linear;
		display.RenderScalePercent = 50;
		Assert.AreEqual( new DisplayResolution( 640, 360 ), display.EffectiveInternalSize );
		Assert.IsNotNull( display.FallbackReason );
		Assert.AreEqual( DisplayApplyResult.RestartRequired, display.Apply() );
		CollectionAssert.AreEqual( new[] { new DisplayResolution( 1024, 768 ) }, stored );
		display.Resolution = new DisplayResolution( 640, 480 );
		display.UiScale = 9;
		Assert.AreEqual( 2f, display.UiScale );
		display.Revert();
		Assert.AreEqual( new DisplayResolution( 1024, 768 ), display.Resolution );
		Assert.AreEqual( 1f, display.UiScale );
	}

	[TestMethod]
	public void GameOptionsRoundTripAndClamp()
	{
		var path = Path.Combine( Path.GetTempPath(), $"opentpw-options-{Guid.NewGuid():N}.json" );
		try
		{
			new GameOptions { MusicVolume = 3, SoundEffectsVolume = 42, PopupHelp = false, RenderScalePercent = 10, UiScale = 1.5f }.Save( path );
			var loaded = GameOptions.Load( path );
			Assert.AreEqual( (3, 10, false, 50, 1.5f), (loaded.MusicVolume, loaded.SoundEffectsVolume, loaded.PopupHelp, loaded.RenderScalePercent, loaded.UiScale) );
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
	public void StubParkStatusKeepsMoneyAndAnApproximateCalendar()
	{
		var status = new StubParkStatus( 100000 );
		Assert.AreEqual( new ParkDate( 1, 1, 1 ), status.Date );
		Assert.IsFalse( status.TrySpend( 100001 ) );
		Assert.IsTrue( status.TrySpend( 3250 ) );
		Assert.AreEqual( 96750, status.Money );
		status.Update( StubParkStatus.SecondsPerDay * 30 );
		Assert.AreEqual( new ParkDate( 1, 2, 1 ), status.Date );
		status.Speed = GameSpeed.Paused;
		Assert.AreEqual( 0f, status.TimeScale );
		status.Update( 1000 );
		Assert.AreEqual( new ParkDate( 1, 2, 1 ), status.Date );
		status.Speed = GameSpeed.Fastest;
		status.Update( StubParkStatus.SecondsPerDay * 330 / 4 );
		Assert.AreEqual( new ParkDate( 2, 1, 1 ), status.Date );
		status.Refund( 3250 );
		Assert.AreEqual( 100000, status.Money );
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

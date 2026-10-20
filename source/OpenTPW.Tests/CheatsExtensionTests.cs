using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTPW.Mods;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace OpenTPW.Tests;

/// <summary>
/// The cheats extension's own cheats (docs/CHEATS.md, phase K3): an arrival by a chosen vehicle, the weather now, their refusal without the extension, the recording of the extension, and the
/// pinned run with a scripted sequence of cheats (<see cref="CheatsFixedRunHash"/>).
/// </summary>
[TestClass]
[DoNotParallelize]
public class CheatsExtensionTests
{
	/// <summary>A host for a park without a level: the guests' arrivals and a weather the test steps.</summary>
	private sealed class Host( GuestSimulation guests, ParkWeather? weather ) : IParkCheatHost
	{
		public bool TryStartArrival( int vehicleKind ) => guests.TryStartCheatArrival( vehicleKind );
		public bool TryApplyWeather( CheatWeather kind, long tick ) => weather?.ApplyCheat( kind, tick ) == true;
		public bool CanRaiseRideVehicles( int attractionId ) => false;
		public bool TryRaiseRideVehicles( int attractionId ) => false;
	}

	private static long NextTurn( ParkEconomy park )
	{
		var turn = park.Turn;
		while ( park.Turn == turn )
			park.Advance( 1 );
		return park.Turn;
	}

	private static GuestSimulation Guests() => new( GuestTests.CreateCross(), GuestTests.CreateSettings(), 7 );

	// ---- The extension's gate -------------------------------------------------------------------------

	[TestMethod]
	public void TheExtensionsOwnCheatsNeedTheExtensionAndALevel()
	{
		var park = EconomyTestData.Park();
		var weather = new CheatAction( CheatKind.Weather, (int)CheatWeather.Rain );
		var arrival = new CheatAction( CheatKind.Arrival, ArrivalVehicleKind.Ferry );
		Assert.IsFalse( park.QueueCheat( weather ), "extension off" );
		using ( OpenTpwExtensions.Use( CheatsTests.On() ) )
		{
			Assert.IsFalse( park.QueueCheat( weather ), "no level to act on" );
			park.CheatHost = new Host( Guests(), null );
			Assert.IsTrue( park.QueueCheat( weather ) );
			Assert.IsTrue( park.QueueCheat( arrival ) );
			Assert.IsFalse( park.QueueCheat( arrival ), "one arrival may wait at a time" );
			Assert.IsFalse( park.QueueCheat( new CheatAction( CheatKind.Weather, 7 ) ) );
			Assert.IsFalse( park.QueueCheat( new CheatAction( CheatKind.Arrival, 3 ) ) );
			Assert.IsTrue( park.Cheats.UsesExtension, "a waiting cheat of the extension is recorded in a save" );
		}
		Assert.IsTrue( OfficialExtensions.Cheats.PlayerSelectable, "selectable from K3 on" );
		Assert.IsFalse( OfficialExtensions.Cheats.DefaultEnabled );
		Assert.IsTrue( park.QueueCheat( new CheatAction( CheatKind.Money ) ), "the original's cheats need no extension" );
	}

	// ---- Arrivals -----------------------------------------------------------------------------------------

	[TestMethod]
	public void AnArrivalCheatSizesTheGroupForItsVehicle()
	{
		Assert.AreEqual( (1, 35), GuestSimulation.CheatArrivalBand( ArrivalVehicleKind.Bus ) );
		Assert.AreEqual( (36, 60), GuestSimulation.CheatArrivalBand( ArrivalVehicleKind.Seaplane ) );
		Assert.AreEqual( (61, int.MaxValue), GuestSimulation.CheatArrivalBand( ArrivalVehicleKind.Ferry ) );
		foreach ( var (kind, low, high) in new[] { (ArrivalVehicleKind.Bus, 1, 35), (ArrivalVehicleKind.Seaplane, 36, 60), (ArrivalVehicleKind.Ferry, 61, 1500) } )
		{
			var guests = Guests();
			var natural = guests.ArrivalGroupSize();
			var sizes = new List<int>();
			guests.ArrivalGroupStarted += sizes.Add;
			Assert.IsTrue( guests.TryStartCheatArrival( kind ) );
			Assert.AreEqual( Math.Clamp( natural, low, high ), sizes.Single(), $"vehicle {kind}: the natural {natural} moved into the band" );
			Assert.IsFalse( guests.TryStartCheatArrival( kind ), "a group is being delivered: the cheat waits" );
		}
		// the park limit cuts a cheat group as it cuts every group
		var full = Guests();
		full.ParkPoints = () => 100000;
		Assert.IsTrue( full.ArrivalGroupSize() > 61 );
		var started = new List<int>();
		full.ArrivalGroupStarted += started.Add;
		full.ArrivalsHalted = () => true;
		Assert.IsTrue( full.TryStartCheatArrival( ArrivalVehicleKind.Ferry ) );
		Assert.AreEqual( 0, started.Single(), "a lost park's group is empty" );
	}

	[TestMethod]
	public void ABoatRequestStartsAGroupOfAtLeast61AtTheNextTurnAndARequestDuringAGroupWaits()
	{
		using var scope = OpenTpwExtensions.Use( CheatsTests.On() );
		var park = EconomyTestData.Park();
		var guests = Guests();
		park.CheatHost = new Host( guests, null );
		var sizes = new List<int>();
		guests.ArrivalGroupStarted += sizes.Add;
		Assert.IsTrue( guests.TryStartCheatArrival( ArrivalVehicleKind.Bus ), "a group already on its way" );
		Assert.IsTrue( park.QueueCheat( new CheatAction( CheatKind.Arrival, ArrivalVehicleKind.Ferry ) ) );
		NextTurn( park );
		Assert.AreEqual( 1, sizes.Count, "the boat waits while the bus's group is delivered" );
		Assert.IsTrue( park.Cheats.ArrivalPending );
		Assert.AreEqual( 0, park.Cheats.Log.Count, "nothing applied, nothing recorded" );
		Assert.IsFalse( park.Cheats.Mode );
		// the bus's group steps off, one guest a turn, and the turn that finds it empty restarts the timer
		for ( var tick = 0; tick < 20000 && park.Cheats.ArrivalPending; tick++ )
		{
			guests.Tick( 1f / FixedStepClock.TicksPerSecond );
			park.Advance( 1 );
		}
		Assert.IsFalse( park.Cheats.ArrivalPending, "applied once the group was delivered" );
		Assert.IsTrue( sizes[1] >= 61, $"a Ferry group of {sizes[1]}" );
		Assert.AreEqual( CheatKind.Arrival, park.Cheats.Log.Single().Kind );
		Assert.IsTrue( park.Cheats.Mode && park.Cheats.Extension && park.Cheats.UsesExtension );
	}

	[TestMethod]
	public void TheWeatherAndArrivalCheatsLeaveANoteAndASaveHoldsOneWaitingArrival()
	{
		var strings = new OpenTPW.UI.Original.UiStringTable( "English", new string[1], new[] { "" }, new string[1], new string[1] );
		ParkEvent Applied( CheatKind kind ) => new( 0, ParkEventKind.CheatApplied, 0, (int)kind, 0, "" );
		Assert.AreEqual( "Cheat: the weather changes", OpenTPW.Hud.ParkHud.CheatNote( strings, Applied( CheatKind.Weather ) ) );
		Assert.AreEqual( "Cheat: visitors on their way", OpenTPW.Hud.ParkHud.CheatNote( strings, Applied( CheatKind.Arrival ) ) );
		// the queue refuses a second waiting arrival, and the load refuses a save that holds two
		ParkSaveFile.CheatActionData Arrival( int kind ) => new() { Kind = CheatKind.Arrival, Argument = kind };
		var one = new ParkSaveFile.CheatsData { Mode = true, Extension = true, Log = new(), Pending = new() { Arrival( ArrivalVehicleKind.Bus ) } };
		Assert.IsNull( ParkCheats.Problem( one, 10 ) );
		var two = new ParkSaveFile.CheatsData { Mode = true, Extension = true, Log = new(), Pending = new() { Arrival( ArrivalVehicleKind.Bus ), Arrival( ArrivalVehicleKind.Ferry ) } };
		Assert.IsNotNull( ParkCheats.Problem( two, 10 ) );
	}

	// ---- Weather -------------------------------------------------------------------------------------------

	[TestMethod]
	public void AWeatherCheatAppliesAtOnceWithItsKindInEveryThemeAndTheNaturalScheduleGoesOn()
	{
		var weather = new ParkWeather( ParkWeatherSnapshotTests.Jungle(), 21, 10, 10 );
		Assert.IsFalse( weather.ApplyCheat( CheatWeather.Rain, 0 ), "the weather has not started" );
		weather.AdvanceTo( 0 );
		var tick = ParkCalendar.TickOfTurn( 3 );
		weather.AdvanceTo( tick );
		Assert.IsTrue( weather.ApplyCheat( CheatWeather.Snow, tick ) );
		Assert.IsTrue( weather.Snow && !weather.Rain && !weather.Lightning, "snow in the jungle, whose own band never snows" );
		Assert.AreEqual( 20, weather.Quality, "the jungle has no snow band: the fixed quality" );
		Assert.AreEqual( 300, weather.TargetDrops );
		Assert.IsNull( weather.Forecast, "the pending forecast is replaced" );
		var drops = weather.CurrentDrops;
		weather.AdvanceTo( ParkCalendar.TickOfTurn( 4 ) );
		Assert.AreEqual( drops + 20, weather.CurrentDrops, "the drops ramp at SpeedOfChange a turn" );
		Assert.IsTrue( weather.ApplyCheat( CheatWeather.Thunderstorm, ParkCalendar.TickOfTurn( 4 ) ) );
		Assert.IsTrue( weather.Rain && weather.Lightning && !weather.Snow );
		Assert.AreEqual( 8, weather.Quality, "the middle of the lightning band 0..15 (from 1)" );
		Assert.IsTrue( weather.ApplyCheat( CheatWeather.Rain, ParkCalendar.TickOfTurn( 4 ) ) );
		Assert.AreEqual( (20, true, false, 300), (weather.Quality, weather.Rain, weather.Lightning, weather.TargetDrops) );
		Assert.IsTrue( weather.ApplyCheat( CheatWeather.Clear, ParkCalendar.TickOfTurn( 4 ) ) );
		Assert.AreEqual( (100, false, false, false, 0), (weather.Quality, weather.Snow, weather.Rain, weather.Lightning, weather.TargetDrops) );
		// the next roll comes DaysBetweenChanges - DaysOfWarning days after the change, then applies DaysOfWarning days later
		var change = ParkCalendar.Seconds( ParkCalendar.TickOfTurn( 4 ) );
		var rollTurn = (change + 3 * ParkCalendar.SecondsPerDay) / ParkCalendar.SecondsPerTurn;
		weather.AdvanceTo( ParkCalendar.TickOfTurn( rollTurn - 30 ) );
		Assert.IsNull( weather.Forecast, "no roll before the schedule" );
		weather.AdvanceTo( ParkCalendar.TickOfTurn( rollTurn + 30 ) );
		Assert.IsNotNull( weather.Forecast, "the natural roll comes on its schedule" );
		weather.ValidateSnapshot( weather.CaptureSnapshot() );
		// the Halloween park's own bands: the middle of the snow band
		var hallow = new ParkWeather( new SamSettings( new[] { SamDocument.Parse( "WeatherEffects.QualityForSnowLow\t0\nWeatherEffects.QualityForSnowHigh\t25\nWeatherEffects.MaxRaindrops\t600\n", "hallow" ) } ), 3, 10, 10 );
		hallow.AdvanceTo( 0 );
		Assert.IsTrue( hallow.ApplyCheat( CheatWeather.Snow, 0 ) );
		Assert.AreEqual( 13, hallow.Quality );
	}

	// ---- The window ------------------------------------------------------------------------------------------

	[TestMethod]
	public void TheCheatsWindowQueuesItsCheatsOnlyOnceTheCheatModeIsOnAndAltCOpensIt()
	{
		using var scope = OpenTpwExtensions.Use( CheatsTests.On() );
		var previousCamera = Camera.Mode;
		var previousEdition = GameEditions.Current;
		GameEditions.Select( GameEdition.ThemeParkWorld );
		Camera.Mode = new ParkCameraMode();
		Input.Keyboard = new Input.KeyboardInfo();
		Input.KeysDown.Clear(); Input.LastKeysDown.Clear(); Input.TextEntryActive = false;
		try
		{
			var hud = VisitorWorldSelectionTests.Fixture( out var guests, out _ );
			var economy = EconomyTestData.Park();
			economy.CheatHost = new Host( guests, null );
			hud.GetType().GetField( "<Status>k__BackingField", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic )!.SetValue( hud, new OpenTPW.Hud.EconomyParkStatus( () => economy ) );
			CommandTestLevel.AttachPark( (Level)typeof( OpenTPW.Hud.ParkHud ).GetField( "level", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic )!.GetValue( hud )!, economy );
			Assert.IsTrue( hud.CheatsWindowAvailable );
			var window = hud.CreateCheatsWindow();
			void Click( string id ) => ((OpenTPW.UI.Original.UiButton)window.Find( id )!).Clicked!();
			Click( "money" );
			Click( "arrival" );
			Assert.AreEqual( 0, economy.Cheats.Pending.Count, "nothing acts before the cheat mode is on" );
			Click( "cheatMode" );
			Click( "money" );
			Click( "research" );
			Click( "vehicle" );
			Click( "vehicle" );
			Click( "arrival" );
			Click( "weatherKind" );
			Click( "weather" );
			Click( "ticketKind" );
			Click( "ticket" );
			CollectionAssert.AreEqual( new[]
			{
				new CheatAction( CheatKind.Mode ), new CheatAction( CheatKind.Money ), new CheatAction( CheatKind.Research ), new CheatAction( CheatKind.Arrival, ArrivalVehicleKind.Ferry ),
				new CheatAction( CheatKind.Weather, (int)CheatWeather.Rain ), new CheatAction( CheatKind.Ticket, (int)GoldenTicketKind.PeopleInPark )
			}, economy.Cheats.Pending.ToArray() );
			Assert.AreEqual( " (Alt+G)", OpenTPW.Hud.ParkHud.CheatKeyHint( new CheatAction( CheatKind.Ticket, (int)GoldenTicketKind.Visitors ) ) );
			Assert.AreEqual( " (Alt+Num9)", OpenTPW.Hud.ParkHud.CheatKeyHint( new CheatAction( CheatKind.Ticket, (int)GoldenTicketKind.BigPark ) ) );
			Assert.AreEqual( " (Alt+1)", OpenTPW.Hud.ParkHud.CheatKeyHint( new CheatAction( CheatKind.Money ) ) );
			Assert.AreEqual( " (Alt+R)", OpenTPW.Hud.ParkHud.CheatKeyHint( new CheatAction( CheatKind.Research ) ) );
			Assert.AreEqual( "", OpenTPW.Hud.ParkHud.CheatKeyHint( new CheatAction( CheatKind.Arrival ) ) );
			Assert.AreEqual( 10, OpenTPW.Hud.ParkHud.CheatTickets.Count );
			Assert.AreEqual( OpenTPW.UI.Original.OpenTpwText.CheatTicketBigPark, OpenTPW.Hud.ParkHud.TicketText( GoldenTicketKind.BigPark ) );
			// Alt+C opens it through the real HUD frame; without the extension it does not exist
			var font = new FontAtlas( FontAtlasTests.CreateFont( 8, new FontAtlasTests.SyntheticGlyph( 'A', 2, 2, 0, 0, 3 ) ) );
			var context = new OpenTPW.UI.Original.UiContext( OriginalUiTests.FakeStrings(), new OpenTPW.UI.Original.UiFonts( _ => font ), new OpenTPW.UI.Original.UiModels( name => throw new FileNotFoundException( name ) ) ) { Canvas = new OpenTPW.UI.Original.UiCanvas( 1000, 1000 ) };
			void Frame( params Veldrid.Key[] held )
			{
				Input.Keyboard = new Input.KeyboardInfo( held.ToList() );
				Input.UpdateButtons();
				hud.Update( context, OpenTPW.UI.Original.UiInput.Idle( new( -1, -1 ) ), false, false, false );
			}
			Frame( Veldrid.Key.C );
			Frame();
			Assert.AreNotEqual( OpenTPW.Hud.ParkHud.CheatsWindowName, hud.Stack.Top?.Name, "C alone is no window key" );
			// in a real park C arms the camcorder; Alt+C must not (the original matches the exact modifier mask, 0x40C900)
			var level = (Level)hud.GetType().GetField( "level", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic )!.GetValue( hud )!;
			typeof( Level ).GetField( "<OriginalPark>k__BackingField", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic )!
				.SetValue( level, System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject( typeof( OriginalPark ) ) );
			Frame( Veldrid.Key.AltLeft, Veldrid.Key.C );
			Assert.AreEqual( OpenTPW.Hud.ParkHud.CheatsWindowName, hud.Stack.Top?.Name );
			Assert.IsFalse( hud.CamcorderArmed, "Alt+C opens the cheats window and leaves the camcorder alone" );
			hud.Stack.Pop();
			Frame();
			using ( OpenTpwExtensions.Use( ModSet.Defaults( OfficialExtensions.All ) ) )
			{
				Assert.IsFalse( hud.CheatsWindowAvailable );
				Frame( Veldrid.Key.AltLeft, Veldrid.Key.C );
				Assert.AreNotEqual( OpenTPW.Hud.ParkHud.CheatsWindowName, hud.Stack.Top?.Name, "the extension is off: no window" );
			}
		}
		finally
		{
			Camera.Mode = previousCamera;
			GameEditions.Select( previousEdition );
			Input.Keyboard = new Input.KeyboardInfo();
			Input.KeysDown.Clear(); Input.LastKeysDown.Clear(); Input.TextEntryActive = false;
		}
	}

	/// <summary>
	/// Every label of the cheats window, with every choice it can show, fits its row at the page's size in every UI language, with the language's own fonts (the localisation pack
	/// <c>OPENTPW_LANGUAGE_PACK</c> over <c>OPENTPW_GAME_PATH</c>); a label that only fits a smaller size would be drawn smaller than its neighbours.
	/// </summary>
	[TestMethod]
	public void TheCheatsWindowsLabelsFitTheirRowsAtThePageSizeInEveryLanguage()
	{
		var pack = Environment.GetEnvironmentVariable( "OPENTPW_LANGUAGE_PACK" );
		var game = Environment.GetEnvironmentVariable( "OPENTPW_GAME_PATH" );
		var data = string.IsNullOrWhiteSpace( game ) ? null : GameLanguage.FindEntry( game, "Data", true );
		if ( string.IsNullOrWhiteSpace( pack ) || !Directory.Exists( pack ) || data == null )
			Assert.Inconclusive( "Set OPENTPW_LANGUAGE_PACK and OPENTPW_GAME_PATH for the languages' own fonts." );
		var failures = new List<string>();
		foreach ( var name in OpenTPW.UI.Original.SupplementaryStrings.SupportedLanguages )
		{
			var language = GameLanguage.Resolve( data!, name, pack );
			var uiText = language.LoadStrings( "UITEXT.str" );
			var objectNames = language.LoadStrings( "OBJECT_NAMES.str" ).Entries;
			string Text( OpenTPW.UI.Original.OpenTpwText key ) => OpenTPW.UI.Original.SupplementaryStrings.Get( key, name );
			// the window's rows (ParkHud.CreateCheatsWindow) with every text each can show
			var rows = new Dictionary<string, string[]>
			{
				["cheatMode"] = new[] { UIStrings.On, UIStrings.Off }.Select( state => Text( OpenTPW.UI.Original.OpenTpwText.CheatModeLabel ) + uiText[(int)state] ).ToArray(),
				["vehicle"] = new[] { ArrivalVehicleKind.Bus, ArrivalVehicleKind.Seaplane, ArrivalVehicleKind.Ferry }.Select( kind => Text( OpenTPW.UI.Original.OpenTpwText.CheatArrivalLabel ) + Text( OpenTPW.Hud.ParkHud.VehicleText( kind ) ) ).ToArray(),
				["arrival"] = new[] { Text( OpenTPW.UI.Original.OpenTpwText.CheatSendVisitors ) },
				["weatherKind"] = Enum.GetValues<CheatWeather>().Select( weather => Text( OpenTPW.UI.Original.OpenTpwText.CheatWeatherLabel ) + Text( OpenTPW.Hud.ParkHud.WeatherText( weather ) ) ).ToArray(),
				["weather"] = new[] { Text( OpenTPW.UI.Original.OpenTpwText.CheatChangeWeather ) },
				["ticketKind"] = OpenTPW.Hud.ParkHud.CheatTickets.Select( kind => Text( OpenTPW.UI.Original.OpenTpwText.CheatTicketLabel ) + Text( OpenTPW.Hud.ParkHud.TicketText( kind ) ) ).ToArray(),
				["ticket"] = OpenTPW.Hud.ParkHud.CheatTickets.Select( kind => Text( OpenTPW.UI.Original.OpenTpwText.CheatAwardTicket ) + OpenTPW.Hud.ParkHud.CheatKeyHint( new CheatAction( CheatKind.Ticket, (int)kind ) ) ).ToArray(),
				["money"] = new[] { Text( OpenTPW.UI.Original.OpenTpwText.CheatAddMoney ) + OpenTPW.Hud.ParkHud.CheatKeyHint( new CheatAction( CheatKind.Money ) ) },
				["research"] = new[] { Text( OpenTPW.UI.Original.OpenTpwText.CheatBuildEverything ) + OpenTPW.Hud.ParkHud.CheatKeyHint( new CheatAction( CheatKind.Research ) ) },
				// the ride row with every shipped bumper, kart and tour ride's name in the language's object names (OBJECT_NAMES, as the catalogue binds it), numbered as a second ride of its kind
				["ride"] = new[] { 1140, 2140, 4140, 3140, 1150, 2150, 4150, 3150, 1170, 2170, 4170, 3170 }
					.Select( infoId => ObjectNameTable.TryGet( infoId, out var first, out var second )
						? ObjectNames.Join( objectNames.Skip( first ).Take( second == ObjectNameTable.EmptyIndex ? 1 : 2 ) ) : "" )
					.Where( rideName => rideName.Length > 0 )
					.Select( rideName => Text( OpenTPW.UI.Original.OpenTpwText.CheatRideLabel ) + " " + rideName + " 2" )
					.Append( Text( OpenTPW.UI.Original.OpenTpwText.CheatRideLabel ) + Text( OpenTPW.UI.Original.OpenTpwText.CheatRideNone ) ).ToArray(),
				["rideVehicles"] = new[] { OpenTPW.UI.Original.OpenTpwText.CheatRaiseVehicles, OpenTPW.UI.Original.OpenTpwText.CheatVehiclesRaised }
					.SelectMany( key => new[] { BumpController.RaisedVehicleLimit, TourController.RaisedCarSlots }.Select( count => string.Format( Text( key ), count ) ) ).ToArray(),
			};
			var fonts = new OpenTPW.UI.Original.UiFonts( file => new FontAtlas( language.LoadFont( file ) ) );
			// the window at the reference size, a 1080p and a 1440p screen, and a Retina laptop window (density 2), each at its automatic interface scale
			foreach ( var (width, height, density) in new[] { (1280, 720, 1), (1920, 1080, 1), (2560, 1440, 1), (3024, 1686, 2) } )
			{
				var scale = UiScaling.Automatic( new Point2( width, height ), density );
				var context = new OpenTPW.UI.Original.UiContext( OriginalUiTests.FakeStrings(), fonts, new OpenTPW.UI.Original.UiModels( model => throw new FileNotFoundException( model ) ) ) { Canvas = new OpenTPW.UI.Original.UiCanvas( width, height, scale, density ) };
				var screen = new OpenTPW.UI.Original.UiScreen( "fit" );
				var page = new OpenTPW.OptionsScreen.PageBuilder( screen );
				// the window's label rectangle (a row at the left column's top)
				var rect = OpenTPW.OptionsScreen.PageBuilder.Rect( 57 + 70, 167 + 50, 57 + 470, 167 + 95 );
				page.Label( "reference", rect, () => "x" );
				foreach ( var (id, texts) in rows )
					page.Label( id, rect, () => texts[0], () => texts );
				var group = page.Group;
				// the fonts are compared by size: the smaller sizes of a family are loaded afresh for every choice
				(int, int) Size( OpenTPW.UI.Original.UiLabel label ) => context.ChooseUniform( group, label ) is var (font, factor) ? (font.LineHeight, factor) : default;
				var size = context.ChooseUniform( group, group.Members[0] );
				foreach ( var label in group.Members.Skip( 1 ) )
					if ( Size( label ) != Size( group.Members[0] ) )
						failures.Add( $"{name} at {width}x{height}: {label.Id} '{label.Variants?.Invoke().OrderByDescending( text => context.Measure( size.Font, text ).Width ).First()}' ({label.Variants?.Invoke().Max( text => context.Measure( size.Font, text ).Width )} px of {label.ScreenRect( context.Canvas ).Width:0})" );
			}
		}
		Assert.AreEqual( 0, failures.Count, string.Join( "\n", failures ) );
	}

	// ---- The pinned run -------------------------------------------------------------------------------------

	/// <summary>
	/// The turns of the scripted sequence of <see cref="CheatsFixedRunHash"/>: money at turn 100, research at 200, the Visitors ticket at 300, a boat at 400 and rain at 500 (the plan's
	/// sequence; the bump ride's vehicle limit of the plan's turn 600 has no ride in this park, docs/DETERMINISM.md), and a bus asked for at turn 402, which waits while the boat's group
	/// is delivered and comes at a later turn.
	/// </summary>
	private static readonly (long Turn, CheatAction Action)[] Sequence =
	{
		(100, new CheatAction( CheatKind.Money )),
		(200, new CheatAction( CheatKind.Research )),
		(300, new CheatAction( CheatKind.Ticket, (int)GoldenTicketKind.Visitors )),
		(400, new CheatAction( CheatKind.Arrival, ArrivalVehicleKind.Ferry )),
		(402, new CheatAction( CheatKind.Arrival, ArrivalVehicleKind.Bus )),
		(500, new CheatAction( CheatKind.Weather, (int)CheatWeather.Rain )),
	};

	/// <summary>
	/// The fixed synthetic run (the park, shop, scripts and 14,400 ticks of <c>FixedRunHash</c>) with the cheats extension on, a jungle weather (seed 21) the guests' arrival factor reads, and
	/// <see cref="Sequence"/> queued at the turn before each one's turn, so each applies at the start of its turn. With <paramref name="saveAt"/> the economy is saved after that tick and
	/// loaded again (the legacy economy save, as <c>DeterminismTests</c> does), mid-sequence with the boat's group on its way and the rain still to come.
	/// </summary>
	internal static (ulong Hash, DeterminismTests.SyntheticPark Park) RunFixedParkWithCheats( int? saveAt = null )
	{
		using var scope = OpenTpwExtensions.Use( CheatsTests.On() );
		var weather = new ParkWeather( ParkWeatherSnapshotTests.Jungle(), 21, 10, 10 );
		var park = new DeterminismTests.SyntheticPark( WorldSeed.Default, prepareGuests: guests => guests.WeatherDropsFalling = () => weather.CurrentDrops > 0 );
		// the runtime's wiring (ParkEconomyRuntime.AttachGuests): a waiting arrival cheat holds the natural groups; the economy a load brings in is read each time
		park.Guests.ArrivalHeldForCheat = () => park.Economy.Cheats.ArrivalPending;
		weather.AdvanceTo( park.Economy.Tick );
		var queued = new HashSet<long>();
		var path = saveAt == null ? null : Path.Combine( Path.GetTempPath(), $"opentpw-cheats-run-{Guid.NewGuid():N}.json" );
		try
		{
			for ( var tick = 0; tick < DeterminismTests.PinnedRunTicks; tick++ )
			{
				park.Economy.CheatHost ??= new Host( park.Guests, weather );
				foreach ( var (turn, action) in Sequence )
					if ( park.Economy.Turn == turn - 1 && queued.Add( turn ) )
						Assert.IsTrue( park.Economy.QueueCheat( action ), $"turn {turn}: {action}" );
				park.Run( 1 );
				weather.AdvanceTo( park.Economy.Tick );
				if ( tick + 1 == saveAt )
				{
					park.Save( path! );
					park.Load( path! );
				}
			}
		}
		finally
		{
			if ( path != null && File.Exists( path ) )
				File.Delete( path );
		}
		return (park.Hash, park);
	}

	/// <summary>
	/// [EXT:cheats-turn-boundary] The pinned hash of <see cref="RunFixedParkWithCheats"/> (docs/DETERMINISM.md, "Cheats"). A deliberate change of a cheat's effect, of the order of the kinds, of the
	/// arrival band or of the weather cheat's values changes this value.
	/// </summary>
	internal const ulong CheatsFixedRunHash = 0x0FAA0BA10A2E2B43UL;

	[TestMethod]
	public void CheatsFixedRunHashIsTheSameInEveryProcessAndEveryCheatOfTheSequenceApplied()
	{
		var (hash, park) = RunFixedParkWithCheats();
		var log = park.Economy.Cheats.Log;
		CollectionAssert.AreEquivalent( Sequence.Select( entry => entry.Action.Kind ).ToArray(), log.Select( entry => entry.Kind ).ToArray() );
		Assert.IsTrue( log.Take( 4 ).Zip( Sequence.Take( 4 ) ).All( pair => pair.First.Turn == pair.Second.Turn && pair.First.Kind == pair.Second.Action.Kind ), "the original's cheats and the boat apply at their turns" );
		var bus = log.Single( entry => entry is { Kind: CheatKind.Arrival, Argument: ArrivalVehicleKind.Bus } );
		Assert.IsTrue( bus.Turn > 410, $"the bus waited for the boat's group (applied at turn {bus.Turn})" );
		Assert.AreEqual( 500, log.Single( entry => entry.Kind == CheatKind.Weather ).Turn );
		Assert.IsTrue( park.Economy.Objectives.GoldenTickets.Contains( GoldenTicketKind.Visitors ) );
		Assert.AreEqual( hash, RunFixedParkWithCheats().Hash, "repeatable in the process" );
		Assert.AreNotEqual( DeterminismTests.FixedRunHash, hash, "the cheats change the run" );
		Assert.AreEqual( CheatsFixedRunHash, hash, $"0x{hash:X16} (log {string.Join( ", ", log.Select( entry => $"{entry.Kind}@{entry.Turn}" ) )})" );
	}

	[TestMethod]
	public void AParkSavedMidSequenceContinuesIdentically()
	{
		var uninterrupted = RunFixedParkWithCheats().Hash;
		// before the boat with it queued; at turn 405 with the bus waiting while the boat's group is delivered (a cheat deferred across the save); and after the bus, the rain still to come
		var (_, park) = RunFixedParkWithCheats();
		var bus = park.Economy.Cheats.Log.Single( entry => entry is { Kind: CheatKind.Arrival, Argument: ArrivalVehicleKind.Bus } ).Turn;
		Assert.IsTrue( bus > 405 && bus < 500, $"the bus applies between the saves (turn {bus})" );
		foreach ( var saveAt in new[] { (int)ParkCalendar.TickOfTurn( 399 ) + 1, (int)ParkCalendar.TickOfTurn( 405 ), (int)ParkCalendar.TickOfTurn( bus ) + 1 } )
			Assert.AreEqual( uninterrupted, RunFixedParkWithCheats( saveAt ).Hash, $"saved at tick {saveAt}" );
	}
}

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
			Frame( Veldrid.Key.AltLeft, Veldrid.Key.C );
			Assert.AreEqual( OpenTPW.Hud.ParkHud.CheatsWindowName, hud.Stack.Top?.Name );
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

	// ---- The pinned run -------------------------------------------------------------------------------------

	/// <summary>
	/// The turns of the scripted sequence of <see cref="CheatsFixedRunHash"/>: money at turn 100, research at 200, the Visitors ticket at 300, a boat at 400 and rain at 500 (the plan's
	/// sequence; the bump ride's vehicle limit of the plan's turn 600 has no ride in this park, docs/DETERMINISM.md).
	/// </summary>
	private static readonly (long Turn, CheatAction Action)[] Sequence =
	{
		(100, new CheatAction( CheatKind.Money )),
		(200, new CheatAction( CheatKind.Research )),
		(300, new CheatAction( CheatKind.Ticket, (int)GoldenTicketKind.Visitors )),
		(400, new CheatAction( CheatKind.Arrival, ArrivalVehicleKind.Ferry )),
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
	internal const ulong CheatsFixedRunHash = 0x1020E490C9B2E247UL;

	[TestMethod]
	public void CheatsFixedRunHashIsTheSameInEveryProcessAndEveryCheatOfTheSequenceApplied()
	{
		var (hash, park) = RunFixedParkWithCheats();
		var log = park.Economy.Cheats.Log;
		CollectionAssert.AreEqual( Sequence.Select( entry => entry.Action.Kind ).ToArray(), log.Select( entry => entry.Kind ).ToArray() );
		Assert.IsTrue( log.Take( 3 ).Zip( Sequence.Take( 3 ) ).All( pair => pair.First.Turn == pair.Second.Turn ), "the original's cheats apply at their turns" );
		Assert.IsTrue( log[3].Turn >= 400, "the boat came once no vehicle was at work" );
		Assert.IsTrue( park.Economy.Objectives.GoldenTickets.Contains( GoldenTicketKind.Visitors ) );
		Assert.AreEqual( hash, RunFixedParkWithCheats().Hash, "repeatable in the process" );
		Assert.AreNotEqual( DeterminismTests.FixedRunHash, hash, "the cheats change the run" );
		Assert.AreEqual( CheatsFixedRunHash, hash, $"0x{hash:X16} (log {string.Join( ", ", log.Select( entry => $"{entry.Kind}@{entry.Turn}" ) )})" );
	}

	[TestMethod]
	public void AParkSavedMidSequenceContinuesIdentically()
	{
		var uninterrupted = RunFixedParkWithCheats().Hash;
		// after turn 450: the boat applied, its group on its way or done, the rain still to come; and before the boat with it queued
		foreach ( var saveAt in new[] { (int)ParkCalendar.TickOfTurn( 399 ) + 1, (int)ParkCalendar.TickOfTurn( 450 ) } )
			Assert.AreEqual( uninterrupted, RunFixedParkWithCheats( saveAt ).Hash, $"saved at tick {saveAt}" );
	}
}

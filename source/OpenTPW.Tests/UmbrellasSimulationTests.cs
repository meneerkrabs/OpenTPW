using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// The umbrellas extension in the guest simulation (docs/UMBRELLAS.md, phase U2): the brolly shop's score through the host, a purchase, the balloon trade, the five states over rain and clear weather on qualifying days,
/// a ride that closes the umbrella, and the saved state. A headless park, no original game data.
/// </summary>
// [BIN:TPI-EXE:0x611050 umbrella weather rule] the behaviour under test is the Theme Park Inc trace (docs/UMBRELLAS.md); the guests are Theme Park World's
[TestClass]
public class UmbrellasSimulationTests
{
	private static readonly UmbrellaShopTerms Terms = new( 200, 200, 150, 5, 100 );
	private const double TurnSeconds = ParkCalendar.TurnMilliseconds / 1000.0;

	/// <summary>A park with a path row and a settable weather; the attractions are what a test registers.</summary>
	private sealed class Rig
	{
		public readonly GuestPathGrid Grid = new( 16, 16 );
		public readonly GuestSimulation Simulation;
		public int Weather;

		public Rig( ulong seed = 5, bool crossPark = false, Action<GuestSettings>? settings = null )
		{
			var guestSettings = GuestTests.CreateSettings();
			if ( crossPark )
				Grid = GuestTests.CreateCross();
			else
				for ( var x = 0; x < 12; x++ )
					Grid.SetPath( x, 5, true );
			guestSettings.ExitLevelSeconds = 10_000;
			settings?.Invoke( guestSettings );
			Simulation = new GuestSimulation( Grid, guestSettings, seed ) { ArrivalsEnabled = false };
			Simulation.UmbrellaWeatherState = () => Weather;
		}

		public IRideVisitorHost Host => Simulation;

		public static RideVisitorBridge Brolly( int id = 1, UmbrellaShopTerms? terms = null ) =>
			new( id, "Brolly Shop", RideVisitorKind.Shop, 1, 60, 30 ) { AppearanceEffect = UmbrellaRules.ShopAppearanceEffect, UmbrellaTerms = terms ?? Terms, ExitCell = (6, 5) };

		public static RideVisitorBridge Plain( int id, int appearance = 0 ) =>
			new( id, "Shop", RideVisitorKind.Shop, 1, 60, 30 ) { AppearanceEffect = appearance, ExitCell = (6, 5) };

		/// <summary>One visit: boarded (state 16), then released (the use routine, then state 15).</summary>
		public void Visit( Guest guest, RideVisitorBridge shop )
		{
			Host.OnVisitorBoarded( shop, guest.Id );
			Host.OnVisitorReleased( shop, guest.Id );
		}

		/// <summary>Runs whole park turns.</summary>
		public void RunTurns( int turns )
		{
			var until = Simulation.ParkTurn + turns;
			for ( var tick = 0; Simulation.ParkTurn < until; tick++ )
			{
				Assert.IsTrue( tick < 60 * 60, "the turns pass" );
				Simulation.Tick( GuestSnapshotTests.Tick );
			}
		}

		/// <summary>Sets the guest's age so that its weather rule runs (the start of the fifth game day) or not (the second).</summary>
		public static void SetAge( Guest guest, bool gateOpen ) => guest.TimeInParkSeconds = (gateOpen ? 96.5 : 40.5) * TurnSeconds;
	}

	/// <summary>A park with the brolly shop and an ordinary shop registered on a cross park, as the score tests need a join cell.</summary>
	private static (Rig Rig, RideVisitorBridge Shop, Guest Guest) ScoringPark( RideVisitorBridge? shop = null )
	{
		var rig = new Rig( crossPark: true, settings: settings => settings.Types = new() { new GuestType( 60, 300, 1000 ) } );
		shop ??= new RideVisitorBridge( 1, "Brolly Shop", RideVisitorKind.Shop, 1, 60, 30 ) { AppearanceEffect = UmbrellaRules.ShopAppearanceEffect, UmbrellaTerms = Terms, EntranceCell = (10, 9), ExitCell = (10, 9) };
		rig.Simulation.Register( shop );
		var guest = rig.Simulation.SpawnInPark( shop.JoinCell!.Value.X, shop.JoinCell!.Value.Y );
		return (rig, shop, guest);
	}

	// ---- The shop's score --------------------------------------------------------------------------------------------

	[TestMethod]
	public void ABrollyShopScoresTheGamesScoreTimesTheWeatherPercentageInRainStormAndSnow()
	{
		var (rig, shop, guest) = ScoringPark();
		rig.Weather = UmbrellaRules.WeatherNone;
		Assert.AreEqual( 0u, rig.Simulation.ScoreAttraction( guest, shop ), "clear weather: the shop scores 0" );
		// the same shop without the extension's terms is the game's own score (27 for a guest beside an empty queue with matching excitement)
		var plainShop = new RideVisitorBridge( 2, "Shop", RideVisitorKind.Shop, 1, 60, 30 ) { EntranceCell = (10, 9), ExitCell = (10, 9) };
		var reference = ScoringPark( plainShop );
		Assert.AreEqual( 27u, reference.Rig.Simulation.ScoreAttraction( reference.Guest, plainShop ) );
		rig.Weather = UmbrellaRules.WeatherRain;
		Assert.AreEqual( 54u, rig.Simulation.ScoreAttraction( guest, shop ), "rain: 27 x 200 / 100" );
		rig.Weather = UmbrellaRules.WeatherStorm;
		Assert.AreEqual( 54u, rig.Simulation.ScoreAttraction( guest, shop ) );
		rig.Weather = UmbrellaRules.WeatherSnow;
		Assert.AreEqual( 40u, rig.Simulation.ScoreAttraction( guest, shop ), "snow: 27 x 150 / 100 = 40" );
		rig.Weather = UmbrellaRules.WeatherHeatWave;
		Assert.AreEqual( 0u, rig.Simulation.ScoreAttraction( guest, shop ), "a heat wave is not bad weather, whatever its 5 %" );
		rig.Simulation.UmbrellaWeatherState = null;
		Assert.AreEqual( 0u, rig.Simulation.ScoreAttraction( guest, shop ), "no weather source: none" );
	}

	[TestMethod]
	public void AGuestWithAnUmbrellaOfAnyStateDoesNotWantAnotherAndTheGenerationPercentageScalesLast()
	{
		var (rig, shop, guest) = ScoringPark();
		rig.Weather = UmbrellaRules.WeatherRain;
		foreach ( var state in new[] { 1, 2, 3, 4 } )
		{
			guest.UmbrellaState = state;
			Assert.AreEqual( 0u, rig.Simulation.ScoreAttraction( guest, shop ), $"umbrella state {state}" );
		}
		guest.UmbrellaState = 0;
		Assert.AreEqual( 54u, rig.Simulation.ScoreAttraction( guest, shop ) );
		var kids = ScoringPark( new RideVisitorBridge( 1, "Brolly Shop", RideVisitorKind.Shop, 1, 60, 30 ) { AppearanceEffect = 3, UmbrellaTerms = Terms with { KidScorePercentage = 75 }, EntranceCell = (10, 9), ExitCell = (10, 9) } );
		kids.Rig.Weather = UmbrellaRules.WeatherRain;
		Assert.AreEqual( 40u, kids.Rig.Simulation.ScoreAttraction( kids.Guest, kids.Shop ), "54 x 75 / 100: the generation word applies after the weather" );
	}

	[TestMethod]
	public void AHeldBalloonLowersABrollyShopsScoreAndAFullBalloonDoesNot()
	{
		var (rig, shop, guest) = ScoringPark();
		rig.Weather = UmbrellaRules.WeatherRain;
		guest.HasBalloon = true;
		guest.BalloonLife = 127;
		Assert.AreEqual( 27u, rig.Simulation.ScoreAttraction( guest, shop ), "54 x (100 - 49) / 100" );
		guest.HasBalloon = false;
		Assert.AreEqual( 27u, rig.Simulation.ScoreAttraction( guest, shop ), "put away on a ride, the life still counts" );
		guest.HasBalloon = true;
		guest.BalloonLife = 255;
		Assert.AreEqual( 54u, rig.Simulation.ScoreAttraction( guest, shop ), "the quirk of the original: a full-life balloon skips the cut" );
		guest.BalloonLife = 254;
		Assert.AreEqual( 0u, rig.Simulation.ScoreAttraction( guest, shop ), "54 x 1 / 100" );
	}

	[TestMethod]
	public void AnObjectWithoutTheExtensionsTermsKeepsItsScoreInTheRain()
	{
		var plain = new RideVisitorBridge( 1, "Shop", RideVisitorKind.Shop, 1, 60, 30 ) { EntranceCell = (10, 9), ExitCell = (10, 9) };
		var (rig, _, guest) = ScoringPark( plain );
		var calm = rig.Simulation.ScoreAttraction( guest, plain );
		rig.Weather = UmbrellaRules.WeatherRain;
		Assert.AreEqual( 27u, calm );
		Assert.AreEqual( calm, rig.Simulation.ScoreAttraction( guest, plain ), "the weather and generation words are read for the extension's own objects only" );
		var appearance3 = new RideVisitorBridge( 1, "Shop", RideVisitorKind.Shop, 1, 60, 30 ) { AppearanceEffect = 3, EntranceCell = (10, 9), ExitCell = (10, 9) };
		var other = ScoringPark( appearance3 );
		other.Rig.Weather = UmbrellaRules.WeatherNone;
		Assert.AreEqual( 27u, other.Rig.Simulation.ScoreAttraction( other.Guest, appearance3 ), "an object that merely says AppearanceEffect 3 is no brolly shop: no gate" );
	}

	[TestMethod]
	public void ABalloonShopRefusesABalloonHolderAndAnOpenUmbrellaOnlyInAParkThatUsesUmbrellas()
	{
		var balloons = new RideVisitorBridge( 2, "Balloons", RideVisitorKind.Shop, 1, 60, 30 ) { AppearanceEffect = GuestAppearance.Balloon, EntranceCell = (10, 9), ExitCell = (10, 9) };
		var (rig, _, guest) = ScoringPark( balloons );
		Assert.AreEqual( 27u, rig.Simulation.ScoreAttraction( guest, balloons ) );
		guest.HasBalloon = true;
		guest.BalloonLife = 100;
		Assert.AreEqual( 27u, rig.Simulation.ScoreAttraction( guest, balloons ), "a park without a brolly shop keeps its balloon shops as they were: tp.exe's score reads no balloon" );
		// the same shop in a park that has a brolly shop
		var park = new Rig( crossPark: true, settings: settings => settings.Types = new() { new GuestType( 60, 300, 1000 ) } );
		var brolly = new RideVisitorBridge( 1, "Brolly Shop", RideVisitorKind.Shop, 1, 60, 30 ) { AppearanceEffect = 3, UmbrellaTerms = Terms, EntranceCell = (4, 6), ExitCell = (4, 6) };
		var twin = new RideVisitorBridge( 2, "Balloons", RideVisitorKind.Shop, 1, 60, 30 ) { AppearanceEffect = GuestAppearance.Balloon, EntranceCell = (10, 9), ExitCell = (10, 9) };
		park.Simulation.Register( brolly );
		park.Simulation.Register( twin );
		var holder = park.Simulation.SpawnInPark( twin.JoinCell!.Value.X, twin.JoinCell!.Value.Y );
		Assert.AreEqual( 27u, park.Simulation.ScoreAttraction( holder, twin ), "no balloon, no umbrella: the shop scores as the game does" );
		holder.HasBalloon = true;
		holder.BalloonLife = 100;
		Assert.AreEqual( 0u, park.Simulation.ScoreAttraction( holder, twin ), "a balloon holder: 0 (0x6103F5 checks +0x294 and +0x298)" );
		holder.HasBalloon = false;
		holder.BalloonLife = 0;
		foreach ( var state in new[] { 1, 2, 3, 4 } )
		{
			park.Simulation.SetUmbrellaStateForTest( holder, state );
			Assert.AreEqual( state is 3 or 4 ? 0u : 27u, park.Simulation.ScoreAttraction( holder, twin ), $"umbrella state {state}: refused only while open or opening" );
		}
		park.Simulation.Unregister( brolly );
		park.Simulation.SetUmbrellaStateForTest( holder, 0 );
		holder.HasBalloon = true;
		holder.BalloonLife = 100;
		Assert.AreEqual( 27u, park.Simulation.ScoreAttraction( holder, twin ), "the brolly shop gone and no umbrella left: the balloon shop is the game's again" );
		park.Simulation.SetUmbrellaStateForTest( holder, 2 );
		holder.HasBalloon = false;
		holder.BalloonLife = 0;
		park.Simulation.SetUmbrellaStateForTest( holder, 4 );
		Assert.AreEqual( 0u, park.Simulation.ScoreAttraction( holder, twin ), "a guest who still has an umbrella keeps the rule even when the shop is gone" );
	}

	// ---- The purchase and the balloon trade --------------------------------------------------------------------------

	[TestMethod]
	public void APurchaseOpensTheUmbrellaAtOnceInAnyWeatherWithExactlyOneWorldDraw()
	{
		foreach ( var weather in new[] { UmbrellaRules.WeatherNone, UmbrellaRules.WeatherRain } )
		{
			var rig = new Rig();
			rig.Weather = weather;
			var guest = rig.Simulation.SpawnInPark( 6, 5 );
			var shop = Rig.Brolly();
			rig.Host.OnVisitorBoarded( shop, guest.Id );
			var before = rig.Simulation.RandomState;
			rig.Host.OnVisitorReleased( shop, guest.Id );
			var shadow = new WorldRng( (uint)before );
			var colour = UmbrellaRules.Colour( shadow.NextWord() );
			Assert.AreEqual( (UmbrellaRules.StateOpen, colour, 0L), (guest.UmbrellaState, guest.UmbrellaType, guest.UmbrellaDueTurn), $"weather {weather}: open, colour {colour}" );
			Assert.AreEqual( (ulong)shadow.State, rig.Simulation.RandomState, $"weather {weather}: the colour is the purchase's one draw from the shared generator, nothing else" );
		}
	}

	[TestMethod]
	public void EveryColourOfTheSetIsDrawnAndAnObjectThatIsNoBrollyShopGivesNoUmbrella()
	{
		// a guest's creation reseeds the world generator with its thing id (tp.exe 0x00516370), so the draw after it varies with the guest's id, not with the seed
		var colours = new HashSet<int>();
		var seen = new List<int>();
		var rig = new Rig();
		for ( var count = 0; count < 60 && colours.Count < 3; count++ )
		{
			var guest = rig.Simulation.SpawnInPark( 6, 5 );
			rig.Visit( guest, Rig.Brolly() );
			colours.Add( guest.UmbrellaType );
			seen.Add( guest.UmbrellaType );
		}
		CollectionAssert.AreEquivalent( new[] { 0, 1, 2 }, colours.ToArray(), string.Join( ",", seen ) );
		var park = new Rig();
		var other = park.Simulation.SpawnInPark( 6, 5 );
		var notBrolly = Rig.Plain( 3, UmbrellaRules.ShopAppearanceEffect );
		var draws = park.Simulation.RandomState;
		park.Visit( other, notBrolly );
		Assert.AreEqual( 0, other.UmbrellaState, "AppearanceEffect 3 without the extension's terms is no umbrella" );
		Assert.AreEqual( draws, park.Simulation.RandomState, "and takes no draw" );
	}

	[TestMethod]
	public void APurchaseWritesState3AndOnlyOpensItWhenTheGuestHasNoSpriteYet()
	{
		// [BIN:TPI-EXE:0x612ea9] state 3 first, then the sprite and state 4 only when +0x240 is 0 (cmp [esi+0x240],0; jne 0x612f0a): states 1 and 4 have a sprite, so a second purchase leaves them in 3
		foreach ( var (before, expected) in new[] { (0, 4), (2, 4), (3, 4), (4, 3), (1, 3) } )
		{
			var rig = new Rig();
			var guest = rig.Simulation.SpawnInPark( 6, 5 );
			rig.Simulation.SetUmbrellaStateForTest( guest, before );
			guest.UmbrellaDueTurn = before == 1 ? 400 : 0;
			rig.Simulation.GiveUmbrella( guest );
			Assert.AreEqual( (expected, before == 1 ? 400L : 0L), (guest.UmbrellaState, guest.UmbrellaDueTurn), $"bought from state {before}" );
		}
		// in play entering the shop closes a drawn umbrella first (0x61a7b5 -> 0x6142e0), so a purchase ends in 4 from any state
		foreach ( var before in new[] { 1, 2, 3, 4 } )
		{
			var rig = new Rig();
			var guest = rig.Simulation.SpawnInPark( 6, 5 );
			rig.Simulation.SetUmbrellaStateForTest( guest, before );
			guest.UmbrellaDueTurn = before == 1 ? 400 : 0;
			rig.Visit( guest, Rig.Brolly() );
			Assert.AreEqual( (4, 0L), (guest.UmbrellaState, guest.UmbrellaDueTurn), $"visit from state {before}" );
		}
	}

	[TestMethod]
	public void TheBalloonGateIsPerParkAndFollowsTheHoldersThroughDepartureAndRestore()
	{
		// [EXT:umbrellas-balloon-gate] a park uses umbrellas while a brolly shop is registered or any guest holds an umbrella, whichever guest is being scored
		var rig = new Rig( crossPark: true, settings: settings => settings.Types = new() { new GuestType( 60, 300, 1000 ) } );
		var balloons = new RideVisitorBridge( 2, "Balloons", RideVisitorKind.Shop, 1, 60, 30 ) { AppearanceEffect = GuestAppearance.Balloon, EntranceCell = (10, 9), ExitCell = (10, 9) };
		rig.Simulation.Register( balloons );
		var holder = rig.Simulation.SpawnInPark( balloons.JoinCell!.Value.X, balloons.JoinCell!.Value.Y );
		var other = rig.Simulation.SpawnInPark( balloons.JoinCell!.Value.X, balloons.JoinCell!.Value.Y );
		holder.HasBalloon = true;
		holder.BalloonLife = 100;
		Assert.AreEqual( 27u, rig.Simulation.ScoreAttraction( holder, balloons ), "no brolly shop, no umbrella in the park" );
		rig.Simulation.SetUmbrellaStateForTest( other, UmbrellaRules.StateClosed );
		Assert.AreEqual( 0u, rig.Simulation.ScoreAttraction( holder, balloons ), "another guest's umbrella makes it an umbrella park for the balloon holder too" );
		rig.Simulation.SetUmbrellaStateForTest( other, UmbrellaRules.StateNone );
		Assert.AreEqual( 27u, rig.Simulation.ScoreAttraction( holder, balloons ) );
	}

	[TestMethod]
	public void EnteringABrollyShopWithABalloonReleasesItForTheMediumHappinessStep()
	{
		var rig = new Rig();
		var guest = rig.Simulation.SpawnInPark( 6, 5 );
		guest.HasBalloon = true;
		guest.BalloonLife = 127;
		guest.Happiness = 70;
		rig.Host.OnVisitorBoarded( Rig.Brolly(), guest.Id );
		Assert.AreEqual( (false, 0, 55f), (guest.HasBalloon, guest.BalloonLife, guest.Happiness), "the balloon goes, -15 (MediumHappinessChange) and the life is cleared: 0x613D00" );
		// a balloon put away on an earlier ride (life, no model) goes the same way
		var second = rig.Simulation.SpawnInPark( 6, 5 );
		second.BalloonLife = 40;
		second.Happiness = 10;
		rig.Host.OnVisitorBoarded( Rig.Brolly( 2 ), second.Id );
		Assert.AreEqual( (0, 0f), (second.BalloonLife, second.Happiness), "the happiness is clamped at 0" );
		// another shop only puts the balloon away: the life and the happiness stay
		var third = rig.Simulation.SpawnInPark( 6, 5 );
		third.HasBalloon = true;
		third.BalloonLife = 90;
		third.Happiness = 70;
		rig.Host.OnVisitorBoarded( Rig.Plain( 3 ), third.Id );
		Assert.AreEqual( (false, 90, 70f), (third.HasBalloon, third.BalloonLife, third.Happiness) );
	}

	[TestMethod]
	public void ThePurchaseDoesNotTouchABalloonThatAnotherPathLeftTheGuest()
	{
		// the trade is the entering step and the pending open (0x614010); the purchase itself leaves a life that is somehow still there (a loaded save) as it is
		var rig = new Rig();
		var guest = rig.Simulation.SpawnInPark( 6, 5 );
		var shop = Rig.Brolly();
		rig.Host.OnVisitorBoarded( shop, guest.Id );
		guest.BalloonLife = 30;
		rig.Host.OnVisitorReleased( shop, guest.Id );
		Assert.AreEqual( 30, guest.BalloonLife );
		Assert.AreEqual( UmbrellaRules.StateOpen, guest.UmbrellaState );
	}

	// ---- The states ---------------------------------------------------------------------------------------------------

	[TestMethod]
	public void AKidBuysAnUmbrellaInTheRainKeepsItThroughARideClosedAndOpensItAgainOnAQualifyingDay()
	{
		var rig = new Rig();
		rig.Weather = UmbrellaRules.WeatherRain;
		var guest = rig.Simulation.SpawnInPark( 6, 5 );
		guest.HasBalloon = true;
		guest.BalloonLife = 127;
		guest.Happiness = 70;
		var brolly = Rig.Brolly();
		var ride = Rig.Plain( 2 );
		rig.Host.OnVisitorBoarded( brolly, guest.Id );
		Assert.AreEqual( 55f, guest.Happiness, "the balloon is released with -15 as the guest steps in" );
		rig.Host.OnVisitorReleased( brolly, guest.Id );
		Assert.AreEqual( (UmbrellaRules.StateOpen, 0), (guest.UmbrellaState, guest.BalloonLife), "the umbrella opens at once; the balloon stays gone" );
		// a ride: the umbrella is closed inside
		Rig.SetAge( guest, gateOpen: true );
		rig.Host.OnVisitorBoarded( ride, guest.Id );
		Assert.AreEqual( UmbrellaRules.StateClosed, guest.UmbrellaState, "entering any attraction closes it (0x6142E0)" );
		rig.RunTurns( 12 );
		Assert.AreEqual( (GuestState.Using, UmbrellaRules.StateClosed, 0L), (guest.State, guest.UmbrellaState, guest.UmbrellaDueTurn), "in the rain on a qualifying day, but inside: nothing is scheduled" );
		rig.Host.OnVisitorReleased( ride, guest.Id );
		Assert.AreEqual( UmbrellaRules.StateClosed, guest.UmbrellaState, "leaving does not reopen it" );
		// the weather rule does, on a qualifying day: closed -> opening -> open
		var states = new List<int>();
		Rig.SetAge( guest, gateOpen: true );
		for ( var turn = 0; turn < 17 && guest.UmbrellaState != UmbrellaRules.StateOpen; turn++ )
		{
			rig.RunTurns( 1 );
			if ( states.LastOrDefault() != guest.UmbrellaState )
				states.Add( guest.UmbrellaState );
		}
		Assert.AreEqual( UmbrellaRules.StateOpen, guest.UmbrellaState, string.Join( ", ", states ) );
		CollectionAssert.IsSubsetOf( new[] { 3 }, states, "it went through the pending open" );
		Assert.AreEqual( 0L, guest.UmbrellaDueTurn );
		// the rain stops: an open umbrella gets a close scheduled and is closed when it is due
		rig.Weather = UmbrellaRules.WeatherNone;
		states.Clear();
		Rig.SetAge( guest, gateOpen: true );
		for ( var turn = 0; turn < 17 && guest.UmbrellaState != UmbrellaRules.StateClosed; turn++ )
		{
			rig.RunTurns( 1 );
			if ( states.LastOrDefault() != guest.UmbrellaState )
				states.Add( guest.UmbrellaState );
		}
		Assert.AreEqual( UmbrellaRules.StateClosed, guest.UmbrellaState, string.Join( ", ", states ) );
		CollectionAssert.IsSubsetOf( new[] { 1 }, states, "it went through the pending close" );
	}

	[TestMethod]
	public void TheWeatherRuleRunsOnlyOnTheFirstGameDayAndOneDayInFour()
	{
		var rig = new Rig();
		rig.Weather = UmbrellaRules.WeatherRain;
		var guest = rig.Simulation.SpawnInPark( 6, 5 );
		guest.UmbrellaState = UmbrellaRules.StateClosed;
		Rig.SetAge( guest, gateOpen: false );
		for ( var turn = 0; turn < 10; turn++ )
		{
			// the age stays in the second day while the guest's own clock runs on a few turns
			Rig.SetAge( guest, gateOpen: false );
			rig.RunTurns( 1 );
		}
		Assert.AreEqual( UmbrellaRules.StateClosed, guest.UmbrellaState, "day 1 to 3 of its visit: nothing happens in the rain" );
		Rig.SetAge( guest, gateOpen: true );
		rig.RunTurns( 16 );
		Assert.AreEqual( UmbrellaRules.StateOpen, guest.UmbrellaState, "the fifth day: the closed umbrella opens" );
	}

	[TestMethod]
	public void APendingOpenLetsAHeldBalloonGoAndAPendingChangeWaitsOutsideTheAttractions()
	{
		var rig = new Rig();
		rig.Weather = UmbrellaRules.WeatherRain;
		var guest = rig.Simulation.SpawnInPark( 6, 5 );
		guest.UmbrellaState = UmbrellaRules.StateOpening;
		guest.UmbrellaDueTurn = 0;
		guest.HasBalloon = true;
		guest.BalloonLife = 200;
		guest.Happiness = 80;
		guest.State = GuestState.Using;
		Rig.SetAge( guest, gateOpen: true );
		rig.RunTurns( 8 );
		Assert.AreEqual( UmbrellaRules.StateOpening, guest.UmbrellaState, "due, but the guest is inside an attraction (0x614010 tests +0x38 and +0x3C first)" );
		guest.State = GuestState.DoingNothing;
		Rig.SetAge( guest, gateOpen: true );
		rig.RunTurns( 5 );
		Assert.AreEqual( UmbrellaRules.StateOpen, guest.UmbrellaState );
		Assert.AreEqual( (false, 0), (guest.HasBalloon, guest.BalloonLife), "opening the umbrella lets the balloon go" );
		Assert.IsTrue( guest.Happiness <= 65.5f && guest.Happiness >= 64f, $"-15 for the balloon (and a little for the turn's needs): {guest.Happiness}" );
	}

	[TestMethod]
	public void APendingOpenIsCancelledWhenTheWeatherClearsAndAPendingCloseWhenItRainsAgain()
	{
		var rig = new Rig();
		var guest = rig.Simulation.SpawnInPark( 6, 5 );
		guest.UmbrellaState = UmbrellaRules.StateOpening;
		guest.UmbrellaDueTurn = 400;
		rig.Weather = UmbrellaRules.WeatherNone;
		Rig.SetAge( guest, gateOpen: true );
		rig.RunTurns( 5 );
		Assert.AreEqual( (UmbrellaRules.StateClosed, 0L), (guest.UmbrellaState, guest.UmbrellaDueTurn), "clear weather cancels a pending open: closed" );
		guest.UmbrellaState = UmbrellaRules.StateClosing;
		guest.UmbrellaDueTurn = 400;
		rig.Weather = UmbrellaRules.WeatherSnow;
		Rig.SetAge( guest, gateOpen: true );
		rig.RunTurns( 5 );
		Assert.AreEqual( (UmbrellaRules.StateOpen, 0L), (guest.UmbrellaState, guest.UmbrellaDueTurn), "bad weather cancels a pending close: open" );
	}

	[TestMethod]
	public void AScheduledChangeIsDueWithinTheSlowestResponseAndTakesNoStreamDraw()
	{
		var rig = new Rig();
		rig.Weather = UmbrellaRules.WeatherNone;
		var guest = rig.Simulation.SpawnInPark( 6, 5 );
		guest.UmbrellaState = UmbrellaRules.StateOpen;
		Rig.SetAge( guest, gateOpen: true );
		var scheduledAt = -1L;
		for ( var tick = 0; tick < 60 * 6 && scheduledAt < 0; tick++ )
		{
			rig.Simulation.Tick( GuestSnapshotTests.Tick );
			if ( guest.UmbrellaState == UmbrellaRules.StateClosing )
				scheduledAt = rig.Simulation.ParkTurn;
		}
		Assert.IsTrue( scheduledAt >= 0, "the close was scheduled on the guest's own phase" );
		Assert.IsTrue( NeedsUpdate.IsPhase( (uint)scheduledAt, guest.Id ) );
		var delay = (guest.UmbrellaDueTurn >> 2) - (scheduledAt >> 2);
		Assert.IsTrue( delay is >= 0 and < UmbrellaRules.SlowestWeatherChangeResponse, $"due {guest.UmbrellaDueTurn}, scheduled {scheduledAt}: {delay} updates" );
		Assert.AreEqual( UmbrellaRules.DueTurn( scheduledAt, UmbrellaRules.Delay( guest.Id, scheduledAt, false, 0 ) ), guest.UmbrellaDueTurn, "the delay is the per-guest hash of the guest and the turn" );
	}

	[TestMethod]
	public void AGuestOnATrackCellNeitherSchedulesNorCarriesOutAChangeAndHasItsShownUmbrellaClosed()
	{
		// [BIN:TPI-EXE:0x613ea0 track-cell stand-down] the scheduling and the carrying out stand down on a kart or water track cell; [BIN:TPI-EXE:0x60aedc] the placement closes a shown umbrella there
		var rig = new Rig();
		rig.Weather = UmbrellaRules.WeatherRain;
		var guest = rig.Simulation.SpawnInPark( 6, 5 );
		rig.Simulation.UmbrellaOnTrackCell = ( x, y ) => (x, y) == guest.Cell;
		rig.Simulation.SetUmbrellaStateForTest( guest, UmbrellaRules.StateClosed );
		Rig.SetAge( guest, gateOpen: true );
		rig.RunTurns( 12 );
		Assert.AreEqual( (UmbrellaRules.StateClosed, 0L), (guest.UmbrellaState, guest.UmbrellaDueTurn), "rain would schedule an opening off the track" );
		rig.Simulation.SetUmbrellaStateForTest( guest, UmbrellaRules.StateOpening );
		guest.UmbrellaDueTurn = 1;
		Rig.SetAge( guest, gateOpen: true );
		rig.RunTurns( 12 );
		Assert.AreEqual( UmbrellaRules.StateOpening, guest.UmbrellaState, "a due opening is not carried out on a track cell" );
		rig.Simulation.SetUmbrellaStateForTest( guest, UmbrellaRules.StateOpen );
		rig.RunTurns( 2 );
		Assert.AreEqual( (UmbrellaRules.StateClosed, 0L), (guest.UmbrellaState, guest.UmbrellaDueTurn), "the placement closes a shown umbrella on a track cell (0x60AF32)" );
		rig.Simulation.SetUmbrellaStateForTest( guest, UmbrellaRules.StateOpening );
		guest.UmbrellaDueTurn = 1;
		rig.Simulation.UmbrellaOnTrackCell = null;
		Rig.SetAge( guest, gateOpen: true );
		rig.RunTurns( 12 );
		Assert.AreEqual( UmbrellaRules.StateOpen, guest.UmbrellaState, "off the track cell the same due opening is carried out" );
	}

	[TestMethod]
	public void AGuestWithoutAnUmbrellaIsNeverTouchedByTheWeatherRule()
	{
		var rig = new Rig();
		var guest = rig.Simulation.SpawnInPark( 6, 5 );
		foreach ( var weather in new[] { 0, 2, 3, 4 } )
		{
			rig.Weather = weather;
			Rig.SetAge( guest, gateOpen: true );
			rig.RunTurns( 8 );
			Assert.AreEqual( (0, 0, 0L), (guest.UmbrellaState, guest.UmbrellaType, guest.UmbrellaDueTurn), $"weather {weather}" );
		}
	}

	// ---- The saved state ---------------------------------------------------------------------------------------------

	[TestMethod]
	public void TheUmbrellaStateRoundTripsThroughTheGuestSnapshotAndOlderSavesLoadWithoutIt()
	{
		var rig = new Rig();
		var guests = Enumerable.Range( 0, 5 ).Select( _ => rig.Simulation.SpawnInPark( 6, 5 ) ).ToArray();
		var plain = guests[0];
		(guests[1].UmbrellaState, guests[1].UmbrellaType) = (UmbrellaRules.StateClosed, 1);
		(guests[2].UmbrellaState, guests[2].UmbrellaType, guests[2].UmbrellaDueTurn) = (UmbrellaRules.StateClosing, 2, 120);
		(guests[3].UmbrellaState, guests[3].UmbrellaType, guests[3].UmbrellaDueTurn) = (UmbrellaRules.StateOpening, 0, 8);
		(guests[4].UmbrellaState, guests[4].UmbrellaType) = (UmbrellaRules.StateOpen, 2);
		var snapshot = rig.Simulation.CaptureSnapshot();
		Assert.IsTrue( rig.Simulation.ValidateSnapshot( snapshot ).IsValid );
		// written only while the guest has an umbrella (a plain guest's JSON has no such field: older bytes stay)
		var plainJson = JsonSerializer.Serialize( snapshot.Guests.Single( guest => guest.Id == plain.Id ) );
		Assert.IsFalse( plainJson.Contains( "Umbrella", StringComparison.Ordinal ), plainJson );
		var closedJson = JsonSerializer.Serialize( snapshot.Guests.Single( guest => guest.Id == guests[1].Id ) );
		StringAssert.Contains( closedJson, "\"UmbrellaState\":2" );
		StringAssert.Contains( closedJson, "\"UmbrellaType\":1" );
		Assert.IsFalse( closedJson.Contains( "UmbrellaDueTurn", StringComparison.Ordinal ), "no due turn outside the pending states" );
		var restored = new Rig();
		restored.Simulation.RestoreSnapshot( snapshot );
		foreach ( var guest in guests )
		{
			var again = restored.Simulation.Guests.Single( other => other.Id == guest.Id );
			Assert.AreEqual( (guest.UmbrellaState, guest.UmbrellaType, guest.UmbrellaDueTurn), (again.UmbrellaState, again.UmbrellaType, again.UmbrellaDueTurn), $"guest {guest.Id}" );
		}
		Assert.AreEqual( JsonSerializer.Serialize( snapshot ), JsonSerializer.Serialize( restored.Simulation.CaptureSnapshot() ), "re-capturing the restored guests gives the same snapshot" );
		// an older save has no umbrella field and loads as no umbrella
		var oldJson = JsonSerializer.Serialize( snapshot.Guests.Single( guest => guest.Id == guests[4].Id ) );
		var older = System.Text.RegularExpressions.Regex.Replace( oldJson, "\"Umbrella(State|Type|DueTurn)\":\\d+,?", "" ).Replace( ",}", "}" );
		var olderGuest = JsonSerializer.Deserialize<GuestSnapshot>( older )!;
		Assert.AreEqual( (0, 0, 0L), (olderGuest.UmbrellaState, olderGuest.UmbrellaType, olderGuest.UmbrellaDueTurn) );
	}

	[TestMethod]
	public void TheUmbrellaFieldsAreValidatedAndAnInvalidSnapshotDoesNotRestore()
	{
		var rig = new Rig();
		var guest = rig.Simulation.SpawnInPark( 6, 5 );
		guest.UmbrellaState = UmbrellaRules.StateClosing;
		guest.UmbrellaType = 1;
		guest.UmbrellaDueTurn = 64;
		var snapshot = rig.Simulation.CaptureSnapshot();
		Assert.IsTrue( rig.Simulation.ValidateSnapshot( snapshot ).IsValid );
		GuestSimulationSnapshot With( Func<GuestSnapshot, GuestSnapshot> change ) => snapshot with { Guests = snapshot.Guests.Select( change ).ToArray() };
		var invalid = new (string Name, GuestSimulationSnapshot Snapshot)[]
		{
			( "state 5", With( g => g with { UmbrellaState = 5 } ) ),
			( "state -1", With( g => g with { UmbrellaState = -1 } ) ),
			( "colour 3", With( g => g with { UmbrellaType = 3 } ) ),
			( "colour without an umbrella", With( g => g with { UmbrellaState = 0, UmbrellaDueTurn = 0, UmbrellaType = 1 } ) ),
			( "due turn without a pending change", With( g => g with { UmbrellaState = UmbrellaRules.StateOpen } ) ),
			( "negative due turn", With( g => g with { UmbrellaDueTurn = -4 } ) ),
			( "due turn not a multiple of four", With( g => g with { UmbrellaDueTurn = 65 } ) )
		};
		var target = new Rig();
		foreach ( var (name, bad) in invalid )
		{
			Assert.IsFalse( target.Simulation.ValidateSnapshot( bad ).IsValid, $"{name} must not validate" );
			Assert.ThrowsException<InvalidDataException>( () => target.Simulation.RestoreSnapshot( bad ), $"{name} must not restore" );
		}
		Assert.IsTrue( target.Simulation.ValidateSnapshot( With( g => g with { UmbrellaState = UmbrellaRules.StateOpening, UmbrellaDueTurn = 0 } ) ).IsValid, "a pending open may be due at 0" );
	}

	[TestMethod]
	public void TheUmbrellaJoinsTheStateHashOnlyWhileAGuestHasOne()
	{
		ulong Hash( Action<Guest>? change )
		{
			var rig = new Rig( 9 );
			var guest = rig.Simulation.SpawnInPark( 6, 5 );
			change?.Invoke( guest );
			return rig.Simulation.ComputeStateHash();
		}
		Assert.AreEqual( Hash( null ), Hash( guest => (guest.UmbrellaState, guest.UmbrellaType, guest.UmbrellaDueTurn) = (0, 0, 0) ), "no umbrella: the digest has nothing of it" );
		var open = Hash( guest => (guest.UmbrellaState, guest.UmbrellaType) = (UmbrellaRules.StateOpen, 1) );
		Assert.AreNotEqual( Hash( null ), open );
		Assert.AreNotEqual( open, Hash( guest => (guest.UmbrellaState, guest.UmbrellaType) = (UmbrellaRules.StateOpen, 2) ), "the colour counts" );
		Assert.AreNotEqual( open, Hash( guest => (guest.UmbrellaState, guest.UmbrellaType) = (UmbrellaRules.StateClosed, 1) ), "the state counts" );
	}

	[TestMethod]
	public void AParkSavedMidRainWithUmbrellasInEveryStateContinuesIdenticallyAfterAResume()
	{
		// the weather depends on the park's own clock, so a resumed park meets the same weather at the same tick
		static void Weather( Rig rig ) => rig.Simulation.UmbrellaWeatherState = () => rig.Simulation.TickCount < 900 ? UmbrellaRules.WeatherRain : rig.Simulation.TickCount < 1500 ? UmbrellaRules.WeatherNone : UmbrellaRules.WeatherSnow;
		var park = new Rig( 11 );
		Weather( park );
		var guests = Enumerable.Range( 0, 8 ).Select( _ => park.Simulation.SpawnInPark( 4, 5 ) ).ToArray();
		var states = new[] { 0, 1, 2, 3, 4, 2, 4, 3 };
		for ( var index = 0; index < guests.Length; index++ )
		{
			guests[index].UmbrellaState = states[index];
			guests[index].UmbrellaType = states[index] == 0 ? 0 : index % 3;
			guests[index].UmbrellaDueTurn = states[index] is 1 or 3 ? 60 + 8 * index : 0;
			guests[index].TimeInParkSeconds = (96 + index) * TurnSeconds;
		}
		for ( var tick = 0; tick < 300; tick++ )
			park.Simulation.Tick( GuestSnapshotTests.Tick );
		Assert.IsTrue( park.Simulation.Guests.Any( guest => guest.UmbrellaState != 0 ), "the park holds umbrellas at the snapshot moment" );
		var snapshot = park.Simulation.CaptureSnapshot();
		Assert.IsTrue( park.Simulation.ValidateSnapshot( snapshot ).IsValid );
		var resumed = new Rig( 11 );
		Weather( resumed );
		resumed.Simulation.RestoreSnapshot( snapshot );
		Assert.AreEqual( park.Simulation.ComputeStateHash(), resumed.Simulation.ComputeStateHash(), "the restored park hashes the same as the saved one" );
		var seen = new HashSet<int>( park.Simulation.Guests.Select( guest => guest.UmbrellaState ) );
		for ( var tick = 0; tick < 2400; tick++ )
		{
			park.Simulation.Tick( GuestSnapshotTests.Tick );
			resumed.Simulation.Tick( GuestSnapshotTests.Tick );
			if ( tick % 40 == 0 )
			{
				Assert.AreEqual( park.Simulation.ComputeStateHash(), resumed.Simulation.ComputeStateHash(), $"the parks diverged at tick {tick}" );
				foreach ( var guest in park.Simulation.Guests )
					seen.Add( guest.UmbrellaState );
			}
		}
		Assert.AreEqual( JsonSerializer.Serialize( park.Simulation.CaptureSnapshot() ), JsonSerializer.Serialize( resumed.Simulation.CaptureSnapshot() ) );
		CollectionAssert.IsSubsetOf( new[] { 1, 2, 3, 4 }, seen.ToArray(), "the umbrellas moved through the states during the continuation (rain, then clear, then snow)" );
	}

	[TestMethod]
	public void AGuestWithAnOpenOrPendingUmbrellaGetsNoSpeedTermFromItsToiletOrAReliefVisit()
	{
		var rig = new Rig();
		var guest = rig.Simulation.SpawnInPark( 6, 5 );
		guest.Toilet = 90;
		guest.UmbrellaState = UmbrellaRules.StateOpen;
		guest.ToiletBonus = 0;
		rig.Weather = UmbrellaRules.WeatherRain;
		rig.RunTurns( 8 );
		Assert.AreEqual( 0, guest.ToiletBonus, "a full toilet speeds a guest up by 25 (GuestSpeed.ToiletBonus), but not while its umbrella is open" );
		rig.Weather = UmbrellaRules.WeatherNone;
		guest.UmbrellaState = UmbrellaRules.StateClosed;
		rig.RunTurns( 8 );
		Assert.AreEqual( GuestSpeed.ToiletBonus, guest.ToiletBonus, "a closed umbrella gives it back on the guest's next needs phase" );
		// a relief visit sets the term to 25 for anyone else (0x004FE7FB) and leaves 0 for an open umbrella
		var relief = new RideVisitorBridge( 5, "Toilet", RideVisitorKind.Facility, 1, 0, 0, GuestNeeds.Toilet ) { ExitCell = (6, 5) };
		var holder = rig.Simulation.SpawnInPark( 6, 5 );
		holder.UmbrellaState = UmbrellaRules.StateOpening;
		var other = rig.Simulation.SpawnInPark( 6, 5 );
		holder.ToiletBonus = other.ToiletBonus = 0;
		rig.Visit( holder, relief );
		rig.Visit( other, relief );
		Assert.AreEqual( (0, GuestSpeed.ToiletBonus), (holder.ToiletBonus, other.ToiletBonus) );
	}
}

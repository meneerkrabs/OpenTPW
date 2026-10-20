using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// The winter extension in the guest simulation (docs/WINTER.md, phase W3): the kiosk's score (it sells all year, a guest with full slots scores 0), the glühwein stall's percentages, a purchase (a head item, then mittens, no random draw), the colours, the saved items and
/// the state hash. A headless park, no original game data. Nothing of it is Theme Park World's behaviour, so every rule under test is the extension's own (<c>[EXT:winter-*]</c>).
/// </summary>
// [EXT:winter-extension] OpenTPW's own extension; Theme Park World has neither the shops nor the items
[TestClass]
public class WinterSimulationTests
{
	private static readonly WinterShopTerms KioskTerms = new( 0, 0, 200, 150, 60, 20, 60 );
	private static readonly WinterShopTerms StallTerms = new( 130, 130, 250, 150, 60, 20, 60 );
	private const double TurnSeconds = ParkCalendar.TurnMilliseconds / 1000.0;

	/// <summary>A park with a path row and a settable weather and month; the attractions are what a test registers.</summary>
	private sealed class Rig
	{
		public readonly GuestPathGrid Grid = new( 16, 16 );
		public readonly GuestSimulation Simulation;
		public int Weather;
		public int Month = 12;

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
			Simulation.WinterMonth = () => Month;
		}

		public IRideVisitorHost Host => Simulation;

		public static RideVisitorBridge Kiosk( int id = 1, WinterShopTerms? terms = null ) =>
			new( id, "Winter Kiosk", RideVisitorKind.Shop, 1, 60, 30 ) { AppearanceEffect = WinterRules.KioskAppearanceEffect, WinterTerms = terms ?? KioskTerms, ExitCell = (6, 5) };

		public static RideVisitorBridge Plain( int id, int appearance = 0 ) =>
			new( id, "Shop", RideVisitorKind.Shop, 1, 60, 30 ) { AppearanceEffect = appearance, ExitCell = (6, 5) };

		/// <summary>One visit: boarded (state 16), then released (the use routine, then state 15).</summary>
		public void Visit( Guest guest, RideVisitorBridge shop )
		{
			Host.OnVisitorBoarded( shop, guest.Id );
			Host.OnVisitorReleased( shop, guest.Id );
		}
	}

	/// <summary>A park with one winter shop registered on a cross park, as the score tests need a join cell.</summary>
	private static (Rig Rig, RideVisitorBridge Shop, Guest Guest) ScoringPark( RideVisitorBridge? shop = null )
	{
		var rig = new Rig( crossPark: true, settings: settings => settings.Types = new() { new GuestType( 60, 300, 1000 ) } );
		shop ??= new RideVisitorBridge( 1, "Winter Kiosk", RideVisitorKind.Shop, 1, 60, 30 ) { AppearanceEffect = WinterRules.KioskAppearanceEffect, WinterTerms = KioskTerms, EntranceCell = (10, 9), ExitCell = (10, 9) };
		rig.Simulation.Register( shop );
		var guest = rig.Simulation.SpawnInPark( shop.JoinCell!.Value.X, shop.JoinCell!.Value.Y );
		return (rig, shop, guest);
	}

	private static RideVisitorBridge Stall( int id = 2 ) =>
		new( id, "Gluehwein Stall", RideVisitorKind.Shop, 1, 60, 30, GuestNeeds.Thirst ) { WinterTerms = StallTerms, SpecialIngredient = 3, EntranceCell = (10, 9), ExitCell = (10, 9) };

	// ---- The pure rules ----------------------------------------------------------------------------------------------

	[TestMethod]
	public void TheSeasonsAreTheMonthsOfThePlan()
	{
		var expected = new[] { WinterSeason.None, WinterSeason.Winter, WinterSeason.Winter, WinterSeason.Spring, WinterSeason.Spring, WinterSeason.Spring, WinterSeason.Summer, WinterSeason.Summer, WinterSeason.Summer,
			WinterSeason.Autumn, WinterSeason.Autumn, WinterSeason.Autumn, WinterSeason.Winter, WinterSeason.None };
		for ( var month = 0; month <= 13; month++ )
			Assert.AreEqual( expected[month], WinterRules.SeasonOf( month ), $"month {month}" );
		CollectionAssert.AreEqual( new[] { 1, 2, 12 }, Enumerable.Range( 0, 14 ).Where( month => WinterRules.SeasonOf( month ) == WinterSeason.Winter ).OrderBy( month => month ).ToArray() );
		var terms = new WinterShopTerms( 1, 2, 3, 40, 50, 60, 70 );
		CollectionAssert.AreEqual( new[] { 0, 40, 40, 50, 60, 70, 0 }, new[] { 0, 1, 2, 4, 7, 10, 13 }.Select( terms.SeasonPercentOf ).ToArray(), "month 0 and 13 are no month; January and February winter, April spring, July summer, October autumn" );
		Assert.AreEqual( (0, 1, 2, 3), (terms.WeatherPercentOf( UmbrellaRules.WeatherNone ), terms.WeatherPercentOf( UmbrellaRules.WeatherRain ), terms.WeatherPercentOf( UmbrellaRules.WeatherStorm ), terms.WeatherPercentOf( UmbrellaRules.WeatherSnow )) );
		Assert.AreEqual( 0, terms.WeatherPercentOf( UmbrellaRules.WeatherHeatWave ), "a heat wave has no percentage here" );
	}

	[TestMethod]
	public void TheKioskScoresZeroOnlyForAGuestWithFullSlotsAndHasNoSeasonOrWeatherGate()
	{
		// [EXT:winter-kiosk] the user's decision of 2026-10-18/19 ("Het hele jaar, vraag via seizoen"): the kiosk sells all year; only a guest with a head item and mittens has nothing left to buy
		Assert.IsFalse( WinterRules.KioskScoresZero( 0 ), "an empty guest: the kiosk scores in every month and weather" );
		Assert.IsFalse( WinterRules.KioskScoresZero( WinterRules.Hat ), "a head item alone leaves the mittens to buy" );
		Assert.IsFalse( WinterRules.KioskScoresZero( WinterRules.Earmuffs ) );
		Assert.IsFalse( WinterRules.KioskScoresZero( WinterRules.Mittens ), "mittens alone leave the head to dress" );
		Assert.IsTrue( WinterRules.KioskScoresZero( WinterRules.Hat | WinterRules.Mittens ) );
		Assert.IsTrue( WinterRules.KioskScoresZero( WinterRules.Earmuffs | WinterRules.Mittens ) );
	}

	[TestMethod]
	public void TheWinterShopsReadSnowAsSnowEvenWithLightningWhereTheUmbrellasReadAStorm()
	{
		// Halloween's snow band equals its lightning band, so its snowfall has both flags
		Assert.AreEqual( UmbrellaRules.WeatherStorm, UmbrellaRules.WeatherStateOf( false, true, true ), "the umbrellas' and the rain mood's map is unchanged" );
		Assert.AreEqual( UmbrellaRules.WeatherSnow, WinterRules.WeatherStateOf( false, true, true ), "the winter shops see the snow" );
		Assert.AreEqual( UmbrellaRules.WeatherSnow, WinterRules.WeatherStateOf( false, false, true ) );
		Assert.AreEqual( UmbrellaRules.WeatherStorm, WinterRules.WeatherStateOf( true, true, false ) );
		Assert.AreEqual( UmbrellaRules.WeatherStorm, WinterRules.WeatherStateOf( false, true, false ) );
		Assert.AreEqual( UmbrellaRules.WeatherRain, WinterRules.WeatherStateOf( true, false, false ) );
		Assert.AreEqual( UmbrellaRules.WeatherNone, WinterRules.WeatherStateOf( false, false, false ) );
		foreach ( var rain in new[] { false, true } )
			foreach ( var lightning in new[] { false, true } )
				Assert.AreEqual( UmbrellaRules.WeatherStateOf( rain, lightning, false ), WinterRules.WeatherStateOf( rain, lightning, false ), "without snow the two maps agree" );
		foreach ( var rain in new[] { false, true } )
			Assert.AreEqual( UmbrellaRules.WeatherStateOf( rain, false, true ), WinterRules.WeatherStateOf( rain, false, true ), "snow without lightning (a wintry December) is the same in both" );
	}

	[TestMethod]
	public void TheTermsMultiplyTheScoreByTheWeatherThenTheSeasonAndAZeroLeavesItAlone()
	{
		Assert.AreEqual( 100u, WinterRules.ApplyTerms( 27, UmbrellaRules.WeatherSnow, 12, StallTerms ), "27 x 250 / 100 = 67, x 150 / 100 = 100 (each step truncated)" );
		Assert.AreEqual( 21u, WinterRules.ApplyTerms( 27, UmbrellaRules.WeatherRain, 4, StallTerms ), "27 x 130 / 100 = 35, x 60 / 100 = 21" );
		Assert.AreEqual( 21u, WinterRules.ApplyTerms( 27, UmbrellaRules.WeatherStorm, 10, StallTerms ), "storm like rain; autumn 60" );
		Assert.AreEqual( 5u, WinterRules.ApplyTerms( 27, UmbrellaRules.WeatherNone, 7, StallTerms ), "27 x 20 / 100 = 5: summer" );
		Assert.AreEqual( 27u, WinterRules.ApplyTerms( 27, UmbrellaRules.WeatherNone, 0, StallTerms ), "no weather and no month: no change" );
		Assert.AreEqual( 10u, WinterRules.ApplyTerms( 27, UmbrellaRules.WeatherSnow, 7, KioskTerms ), "snow 200 = 54, summer 20 = 10" );
		Assert.AreEqual( 40u, WinterRules.ApplyTerms( 27, UmbrellaRules.WeatherNone, 12, KioskTerms ), "winter 150 = 40" );
		Assert.AreEqual( 5u, WinterRules.ApplyTerms( 27, UmbrellaRules.WeatherRain, 7, KioskTerms ), "the kiosk's rain percentage is 0: only the season, summer 20" );
	}

	[TestMethod]
	public void AHeadItemComesFirstThenMittensAndNothingOnceBothSlotsAreFull()
	{
		for ( var id = 1; id <= 50; id++ )
		{
			var head = WinterRules.HeadItemOf( id );
			Assert.IsTrue( head is WinterRules.Hat or WinterRules.Earmuffs, $"guest {id}" );
			Assert.AreEqual( head, WinterRules.NextItem( 0, id ), "the first purchase is the head item" );
			Assert.AreEqual( WinterRules.Mittens, WinterRules.NextItem( head, id ), "the second is mittens" );
			Assert.AreEqual( 0, WinterRules.NextItem( head | WinterRules.Mittens, id ), "the third is nothing" );
			Assert.AreEqual( head, WinterRules.NextItem( WinterRules.Mittens, id ), "mittens bought some other way: the head item is still to come" );
			Assert.AreEqual( WinterRules.Mittens, WinterRules.NextItem( WinterRules.Earmuffs, id ), "a head item of either kind fills the head slot" );
		}
	}

	[TestMethod]
	public void TheHeadItemIsAboutTwoHatsToOneEarmuffsAndTheColoursAreAFunctionOfGuestAndItem()
	{
		var hats = Enumerable.Range( 1, 3000 ).Count( id => WinterRules.HeadItemOf( id ) == WinterRules.Hat );
		Assert.IsTrue( hats is > 1800 and < 2200, $"{hats} hats in 3000 (about 2 : 1)" );
		foreach ( var (item, colours) in new[] { (WinterRules.Hat, 3), (WinterRules.Earmuffs, 2), (WinterRules.Mittens, 3) } )
		{
			Assert.AreEqual( colours, WinterRules.ColoursOf( item ) );
			var seen = new HashSet<int>();
			for ( var id = 1; id <= 400; id++ )
			{
				var colour = WinterRules.ColourOf( id, item );
				Assert.IsTrue( colour >= 0 && colour < colours, $"item {item}, guest {id}: colour {colour}" );
				Assert.AreEqual( colour, WinterRules.ColourOf( id, item ), "a pure function: the same whenever it is asked" );
				seen.Add( colour );
			}
			Assert.AreEqual( colours, seen.Count, $"every colour of item {item} occurs" );
		}
		Assert.IsTrue( Enumerable.Range( 1, 200 ).Any( id => WinterRules.ColourOf( id, WinterRules.Hat ) != WinterRules.ColourOf( id, WinterRules.Mittens ) ), "the item is part of the hash" );
		Assert.AreEqual( 0, WinterRules.ColoursOf( 3 ), "an item that is not one item has no colours" );
		Assert.AreEqual( 0, WinterRules.ColourOf( 7, 0 ) );
	}

	[TestMethod]
	public void TheSpriteSlotsFollowTheItemBankOfTheContent()
	{
		Assert.AreEqual( (0, 3, 5), (WinterRules.FirstSlotOf( WinterRules.Hat ), WinterRules.FirstSlotOf( WinterRules.Earmuffs ), WinterRules.FirstSlotOf( WinterRules.Mittens )) );
		var slots = new HashSet<int>();
		for ( var id = 1; id <= 300; id++ )
			foreach ( var item in new[] { WinterRules.Hat, WinterRules.Earmuffs, WinterRules.Mittens } )
				slots.Add( WinterRules.SpriteSlotOf( id, item ) );
		CollectionAssert.AreEquivalent( Enumerable.Range( 0, 8 ).ToArray(), slots.ToArray(), "the eight slots of the bank, and nothing outside it" );
		var guest = new Rig().Simulation.SpawnInPark( 6, 5 );
		guest.WarmItems = WinterRules.Earmuffs | WinterRules.Mittens;
		Assert.AreEqual( (WinterRules.Earmuffs, true), (guest.HeadItem, guest.HasMittens) );
		guest.WarmItems = WinterRules.Hat;
		Assert.AreEqual( (WinterRules.Hat, false), (guest.HeadItem, guest.HasMittens) );
	}

	[TestMethod]
	public void OnlyTheDefinedItemCombinationsAreValid()
	{
		CollectionAssert.AreEqual( new[] { 0, 1, 2, 4, 5, 6 }, Enumerable.Range( -2, 14 ).Where( WinterRules.IsValidItems ).ToArray(), "no hat with earmuffs, nothing above bit 4" );
	}

	// ---- The shops' score --------------------------------------------------------------------------------------------

	[TestMethod]
	public void TheKioskSellsAllYearItsDemandFollowsTheSeasonAndTheSnowAndAGuestWithFullSlotsScoresZero()
	{
		var (rig, shop, guest) = ScoringPark();
		var plain = ScoringPark( new RideVisitorBridge( 2, "Shop", RideVisitorKind.Shop, 1, 60, 30 ) { EntranceCell = (10, 9), ExitCell = (10, 9) } );
		Assert.AreEqual( 27u, plain.Rig.Simulation.ScoreAttraction( plain.Guest, plain.Shop ), "the game's own score of a guest beside an empty queue" );
		rig.Weather = UmbrellaRules.WeatherNone;
		rig.Month = 6;
		Assert.AreEqual( 5u, rig.Simulation.ScoreAttraction( guest, shop ), "a clear day in June: it still sells, at the summer percentage of 20 (27 x 20 / 100)" );
		rig.Weather = UmbrellaRules.WeatherRain;
		Assert.AreEqual( 5u, rig.Simulation.ScoreAttraction( guest, shop ), "the kiosk's rain percentage is 0, so rain leaves the season's score" );
		rig.Weather = UmbrellaRules.WeatherSnow;
		Assert.AreEqual( 10u, rig.Simulation.ScoreAttraction( guest, shop ), "snow in June: 27 x 200 / 100 = 54, x 20 / 100 = 10" );
		rig.Weather = UmbrellaRules.WeatherNone;
		foreach ( var month in new[] { 12, 1, 2 } )
		{
			rig.Month = month;
			Assert.AreEqual( 40u, rig.Simulation.ScoreAttraction( guest, shop ), $"month {month}: the winter percentage of 150" );
		}
		foreach ( var month in new[] { 3, 4, 5, 9, 10, 11 } )
		{
			rig.Month = month;
			Assert.AreEqual( 16u, rig.Simulation.ScoreAttraction( guest, shop ), $"month {month}: spring or autumn, 60" );
		}
		rig.Simulation.WinterMonth = null;
		Assert.AreEqual( 27u, rig.Simulation.ScoreAttraction( guest, shop ), "no month source: no season percentage, and no gate either" );
		rig.Weather = UmbrellaRules.WeatherSnow;
		Assert.AreEqual( 54u, rig.Simulation.ScoreAttraction( guest, shop ) );
		rig.Simulation.UmbrellaWeatherState = null;
		Assert.AreEqual( 27u, rig.Simulation.ScoreAttraction( guest, shop ), "no weather source and no month: the game's score" );
		rig.Simulation.UmbrellaWeatherState = () => rig.Weather;
		rig.Simulation.WinterMonth = () => rig.Month;
		rig.Weather = UmbrellaRules.WeatherSnow;
		rig.Month = 12;
		Assert.AreEqual( 81u, rig.Simulation.ScoreAttraction( guest, shop ), "snow in December: 54 x 150 / 100" );
		rig.Simulation.SetWarmItemsForTest( guest, WinterRules.Hat );
		Assert.AreEqual( 81u, rig.Simulation.ScoreAttraction( guest, shop ), "a head item alone: mittens are still to buy" );
		rig.Simulation.SetWarmItemsForTest( guest, WinterRules.Hat | WinterRules.Mittens );
		Assert.AreEqual( 0u, rig.Simulation.ScoreAttraction( guest, shop ), "full slots: nothing left to buy" );
		rig.Month = 7;
		rig.Weather = UmbrellaRules.WeatherNone;
		Assert.AreEqual( 0u, rig.Simulation.ScoreAttraction( guest, shop ), "in every month and weather" );
	}

	[TestMethod]
	public void TheShopsTakeTheirOwnSnowSignalAndTheUmbrellaMapIsLeftToTheUmbrellas()
	{
		var (rig, shop, guest) = ScoringPark();
		rig.Month = 6;
		// Halloween's snowfall: lightning and snow at once. The umbrella map reads a storm; the winter signal reads the snow
		rig.Weather = UmbrellaRules.WeatherStorm;
		rig.Simulation.WinterWeatherState = () => UmbrellaRules.WeatherSnow;
		Assert.AreEqual( 10u, rig.Simulation.ScoreAttraction( guest, shop ), "the kiosk's snow percentage of 200 applies although the umbrella map says storm: 54 x 20 / 100" );
		rig.Simulation.WinterWeatherState = null;
		Assert.AreEqual( 5u, rig.Simulation.ScoreAttraction( guest, shop ), "without the winter signal the umbrella map is used (kiosk storm 0)" );
		var stall = ScoringPark( Stall() );
		stall.Rig.Month = 12;
		stall.Rig.Weather = UmbrellaRules.WeatherStorm;
		Assert.AreEqual( 52u, stall.Rig.Simulation.ScoreAttraction( stall.Guest, stall.Shop ), "the stall reads the storm: 27 x 130 / 100 = 35, x 150 / 100 = 52" );
		stall.Rig.Simulation.WinterWeatherState = () => UmbrellaRules.WeatherSnow;
		Assert.AreEqual( 100u, stall.Rig.Simulation.ScoreAttraction( stall.Guest, stall.Shop ), "and the snow when the winter signal says snow: 27 x 250 / 100 = 67, x 150 / 100 = 100" );
	}

	[TestMethod]
	public void TheStallHasNoGateAndTakesTheWeatherAndSeasonPercentages()
	{
		var (rig, stall, guest) = ScoringPark( Stall() );
		foreach ( var (weather, month, expected) in new[]
		{
			(UmbrellaRules.WeatherNone, 7, 5u), (UmbrellaRules.WeatherNone, 12, 40u), (UmbrellaRules.WeatherNone, 4, 16u), (UmbrellaRules.WeatherNone, 10, 16u),
			(UmbrellaRules.WeatherRain, 4, 21u), (UmbrellaRules.WeatherStorm, 1, 52u), (UmbrellaRules.WeatherSnow, 12, 100u), (UmbrellaRules.WeatherSnow, 7, 13u) } )
		{
			rig.Weather = weather;
			rig.Month = month;
			Assert.AreEqual( expected, rig.Simulation.ScoreAttraction( guest, stall ), $"weather {weather}, month {month}" );
		}
		rig.Simulation.SetWarmItemsForTest( guest, WinterRules.AllItems & ~WinterRules.Earmuffs );
		rig.Weather = UmbrellaRules.WeatherSnow;
		rig.Month = 12;
		Assert.AreEqual( 100u, rig.Simulation.ScoreAttraction( guest, stall ), "the stall sells to a guest in full winter wear: its demand is thirst" );
	}

	[TestMethod]
	public void AnObjectWithoutTheExtensionsTermsKeepsItsScoreWhateverItsAppearanceEffectSays()
	{
		var imposter = new RideVisitorBridge( 1, "Shop", RideVisitorKind.Shop, 1, 60, 30 ) { AppearanceEffect = WinterRules.KioskAppearanceEffect, EntranceCell = (10, 9), ExitCell = (10, 9) };
		var (rig, _, guest) = ScoringPark( imposter );
		rig.Weather = UmbrellaRules.WeatherNone;
		rig.Month = 6;
		Assert.AreEqual( 27u, rig.Simulation.ScoreAttraction( guest, imposter ), "an object that merely says AppearanceEffect 4 is no winter kiosk: the game's score" );
		rig.Weather = UmbrellaRules.WeatherSnow;
		rig.Month = 12;
		Assert.AreEqual( 27u, rig.Simulation.ScoreAttraction( guest, imposter ), "and no percentages: the keys are read for the extension's objects only" );
		Assert.IsFalse( ((IRideVisitorBridge)imposter).IsWinterKiosk );
		Assert.IsTrue( ((IRideVisitorBridge)Rig.Kiosk()).IsWinterKiosk );
		Assert.IsFalse( ((IRideVisitorBridge)Stall()).IsWinterKiosk, "the stall is a winter shop but gives no item" );
	}

	// ---- The purchase ------------------------------------------------------------------------------------------------

	[TestMethod]
	public void AKidBuysAHeadItemThenMittensAndIsGivenNothingATThirdVisitWithoutAnyRandomDraw()
	{
		var rig = new Rig();
		var guest = rig.Simulation.SpawnInPark( 6, 5 );
		var kiosk = Rig.Kiosk();
		var other = new Rig();
		var twin = other.Simulation.SpawnInPark( 6, 5 );
		Assert.AreEqual( rig.Simulation.RandomState, other.Simulation.RandomState );
		rig.Visit( guest, kiosk );
		other.Visit( twin, Rig.Plain( 1 ) );
		Assert.AreEqual( other.Simulation.RandomState, rig.Simulation.RandomState, "a kiosk visit draws exactly what an ordinary shop's visit does: the head item and the colours are hashes of the guest id" );
		var head = WinterRules.HeadItemOf( guest.Id );
		Assert.AreEqual( head, guest.WarmItems, "the head item first" );
		Assert.IsTrue( rig.Simulation.UsesWinter );
		rig.Visit( guest, kiosk );
		Assert.AreEqual( head | WinterRules.Mittens, guest.WarmItems, "then mittens" );
		var state = rig.Simulation.RandomState;
		rig.Visit( guest, kiosk );
		Assert.AreEqual( head | WinterRules.Mittens, guest.WarmItems, "a third purchase gives nothing" );
		Assert.AreEqual( WinterRules.HeadItemOf( guest.Id ), guest.WarmItems & WinterRules.HeadItems, "and never a second head item" );
		Assert.IsTrue( WinterRules.IsValidItems( guest.WarmItems ) );
		Assert.AreEqual( 0, new Rig().Simulation.SpawnInPark( 6, 5 ).WarmItems, "a new guest wears nothing" );
		GC.KeepAlive( state );
	}

	[TestMethod]
	public void OnlyTheWinterKioskGivesItemsAndTheStallAndEveryOtherShopDoNot()
	{
		var rig = new Rig();
		var guest = rig.Simulation.SpawnInPark( 6, 5 );
		rig.Visit( guest, Rig.Plain( 2, WinterRules.KioskAppearanceEffect ) );
		rig.Visit( guest, Rig.Plain( 3, UmbrellaRules.ShopAppearanceEffect ) );
		rig.Visit( guest, Rig.Plain( 4, GuestAppearance.Balloon ) );
		var stall = Stall();
		stall.ExitCell = (6, 5);
		rig.Visit( guest, stall );
		Assert.AreEqual( 0, guest.WarmItems, "no item without the kiosk's terms and effect" );
		Assert.IsFalse( rig.Simulation.UsesWinter, "plain shops and a guest without an item: the park does not use the extension" );
		rig.Simulation.Register( stall );
		Assert.IsTrue( rig.Simulation.UsesWinter, "a winter shop registered: it does" );
		rig.Simulation.Unregister( stall );
		Assert.IsFalse( rig.Simulation.UsesWinter );
	}

	[TestMethod]
	public void TheSimulationsOwnWinterUseFollowsPurchaseAndRestore()
	{
		var rig = new Rig();
		var guest = rig.Simulation.SpawnInPark( 6, 5 );
		Assert.IsFalse( rig.Simulation.UsesWinter );
		rig.Visit( guest, Rig.Kiosk() );
		Assert.IsTrue( rig.Simulation.UsesWinter, "a guest who bought" );
		var snapshot = rig.Simulation.CaptureSnapshot();
		var restored = new Rig();
		Assert.IsFalse( restored.Simulation.UsesWinter );
		restored.Simulation.RestoreSnapshot( snapshot );
		Assert.IsTrue( restored.Simulation.UsesWinter, "the restore counts the holders again" );
		restored.Simulation.RestoreSnapshot( rig.Simulation.CaptureSnapshot() with { Guests = Array.Empty<GuestSnapshot>() } );
		Assert.IsFalse( restored.Simulation.UsesWinter, "and none after a restore without guests" );
		rig.Simulation.SetWarmItemsForTest( guest, 0 );
		Assert.IsFalse( rig.Simulation.UsesWinter );
	}

	// ---- Saves ---------------------------------------------------------------------------------------------------------

	[TestMethod]
	public void TheWarmItemsRoundTripThroughTheGuestSnapshotAndOlderSavesLoadWithoutThem()
	{
		var rig = new Rig();
		var guests = Enumerable.Range( 0, 5 ).Select( _ => rig.Simulation.SpawnInPark( 6, 5 ) ).ToArray();
		var items = new[] { 0, WinterRules.Hat, WinterRules.Earmuffs, WinterRules.Mittens, WinterRules.Earmuffs | WinterRules.Mittens };
		for ( var index = 0; index < guests.Length; index++ )
			rig.Simulation.SetWarmItemsForTest( guests[index], items[index] );
		var snapshot = rig.Simulation.CaptureSnapshot();
		Assert.IsTrue( rig.Simulation.ValidateSnapshot( snapshot ).IsValid );
		// written only while the guest wears an item (a plain guest's JSON has no such field: older bytes stay)
		var plainJson = JsonSerializer.Serialize( snapshot.Guests.Single( guest => guest.Id == guests[0].Id ) );
		Assert.IsFalse( plainJson.Contains( "WarmItems", StringComparison.Ordinal ), plainJson );
		StringAssert.Contains( JsonSerializer.Serialize( snapshot.Guests.Single( guest => guest.Id == guests[4].Id ) ), "\"WarmItems\":6" );
		var restored = new Rig();
		restored.Simulation.RestoreSnapshot( snapshot );
		foreach ( var guest in guests )
			Assert.AreEqual( guest.WarmItems, restored.Simulation.Guests.Single( other => other.Id == guest.Id ).WarmItems, $"guest {guest.Id}" );
		Assert.AreEqual( JsonSerializer.Serialize( snapshot ), JsonSerializer.Serialize( restored.Simulation.CaptureSnapshot() ), "re-capturing the restored guests gives the same snapshot" );
		var oldJson = JsonSerializer.Serialize( snapshot.Guests.Single( guest => guest.Id == guests[4].Id ) );
		var older = System.Text.RegularExpressions.Regex.Replace( oldJson, "\"WarmItems\":\\d+,?", "" ).Replace( ",}", "}" );
		Assert.AreEqual( 0, JsonSerializer.Deserialize<GuestSnapshot>( older )!.WarmItems, "an older save has no item field and loads as no items" );
	}

	[TestMethod]
	public void TheWarmItemsAreValidatedAndAnInvalidSnapshotDoesNotRestore()
	{
		var rig = new Rig();
		var guest = rig.Simulation.SpawnInPark( 6, 5 );
		rig.Simulation.SetWarmItemsForTest( guest, WinterRules.Hat | WinterRules.Mittens );
		var snapshot = rig.Simulation.CaptureSnapshot();
		Assert.IsTrue( rig.Simulation.ValidateSnapshot( snapshot ).IsValid );
		GuestSimulationSnapshot With( Func<GuestSnapshot, GuestSnapshot> change ) => snapshot with { Guests = snapshot.Guests.Select( change ).ToArray() };
		var invalid = new (string Name, GuestSimulationSnapshot Snapshot)[]
		{
			( "bit 8", With( g => g with { WarmItems = 8 } ) ),
			( "negative", With( g => g with { WarmItems = -1 } ) ),
			( "hat and earmuffs", With( g => g with { WarmItems = 3 } ) ),
			( "all bits", With( g => g with { WarmItems = 7 } ) )
		};
		var target = new Rig();
		foreach ( var (name, bad) in invalid )
		{
			Assert.IsFalse( target.Simulation.ValidateSnapshot( bad ).IsValid, $"{name} must not validate" );
			Assert.ThrowsException<InvalidDataException>( () => target.Simulation.RestoreSnapshot( bad ), $"{name} must not restore" );
		}
		foreach ( var good in new[] { 0, 1, 2, 4, 5, 6 } )
			Assert.IsTrue( target.Simulation.ValidateSnapshot( With( g => g with { WarmItems = good } ) ).IsValid, $"items {good}" );
	}

	[TestMethod]
	public void TheItemsJoinTheStateHashOnlyWhileAGuestWearsOne()
	{
		ulong Hash( Action<Guest>? change )
		{
			var rig = new Rig( 9 );
			var guest = rig.Simulation.SpawnInPark( 6, 5 );
			change?.Invoke( guest );
			return rig.Simulation.ComputeStateHash();
		}
		Assert.AreEqual( Hash( null ), Hash( guest => guest.WarmItems = 0 ), "no item: the digest has nothing of it" );
		var hat = Hash( guest => guest.WarmItems = WinterRules.Hat );
		Assert.AreNotEqual( Hash( null ), hat );
		Assert.AreNotEqual( hat, Hash( guest => guest.WarmItems = WinterRules.Earmuffs ), "the item counts" );
		Assert.AreNotEqual( hat, Hash( guest => guest.WarmItems = WinterRules.Hat | WinterRules.Mittens ), "the mittens count" );
	}

	[TestMethod]
	public void AParkSavedMidSnowWithItemsContinuesIdenticallyAfterAResume()
	{
		// the weather and the month depend on the park's own clock, so a resumed park meets the same sky at the same tick
		static void Sky( Rig rig )
		{
			rig.Simulation.UmbrellaWeatherState = () => rig.Simulation.TickCount < 900 ? UmbrellaRules.WeatherSnow : rig.Simulation.TickCount < 1500 ? UmbrellaRules.WeatherNone : UmbrellaRules.WeatherSnow;
			rig.Simulation.WinterMonth = () => rig.Simulation.TickCount < 1800 ? 12 : 5;
		}
		var park = new Rig( 11 );
		Sky( park );
		var guests = Enumerable.Range( 0, 8 ).Select( _ => park.Simulation.SpawnInPark( 4, 5 ) ).ToArray();
		var items = new[] { 0, 1, 2, 4, 5, 6, 0, 2 };
		for ( var index = 0; index < guests.Length; index++ )
		{
			park.Simulation.SetWarmItemsForTest( guests[index], items[index] );
			guests[index].TimeInParkSeconds = (96 + index) * TurnSeconds;
		}
		for ( var tick = 0; tick < 300; tick++ )
			park.Simulation.Tick( GuestSnapshotTests.Tick );
		Assert.IsTrue( park.Simulation.Guests.Any( guest => guest.WarmItems != 0 ), "the park holds items at the snapshot moment" );
		var snapshot = park.Simulation.CaptureSnapshot();
		Assert.IsTrue( park.Simulation.ValidateSnapshot( snapshot ).IsValid );
		var resumed = new Rig( 11 );
		Sky( resumed );
		resumed.Simulation.RestoreSnapshot( snapshot );
		Assert.AreEqual( park.Simulation.ComputeStateHash(), resumed.Simulation.ComputeStateHash(), "the restored park hashes the same as the saved one" );
		for ( var tick = 0; tick < 2400; tick++ )
		{
			park.Simulation.Tick( GuestSnapshotTests.Tick );
			resumed.Simulation.Tick( GuestSnapshotTests.Tick );
			if ( tick % 40 == 0 )
				Assert.AreEqual( park.Simulation.ComputeStateHash(), resumed.Simulation.ComputeStateHash(), $"the parks diverged at tick {tick}" );
		}
		Assert.AreEqual( JsonSerializer.Serialize( park.Simulation.CaptureSnapshot() ), JsonSerializer.Serialize( resumed.Simulation.CaptureSnapshot() ) );
		Assert.IsTrue( park.Simulation.Guests.Where( guest => guest.WarmItems != 0 ).All( guest => WinterRules.IsValidItems( guest.WarmItems ) ), "the items are never taken off" );
	}

	[TestMethod]
	public void ASeedlessRunWithoutWinterShopsOrItemsIsTheSameWhateverTheMonthAndSnowSources()
	{
		string Run( bool sources )
		{
			var rig = new Rig( 13, crossPark: true );
			if ( !sources )
			{
				rig.Simulation.WinterMonth = null;
				rig.Simulation.UmbrellaWeatherState = null;
			}
			else
			{
				rig.Weather = UmbrellaRules.WeatherSnow;
				rig.Month = 12;
			}
			var guests = Enumerable.Range( 0, 6 ).Select( _ => rig.Simulation.SpawnInPark( 10, 5 ) ).ToArray();
			for ( var tick = 0; tick < 1500; tick++ )
				rig.Simulation.Tick( GuestSnapshotTests.Tick );
			return $"{rig.Simulation.ComputeStateHash():X16}|{guests.Sum( guest => guest.WarmItems )}";
		}
		Assert.AreEqual( Run( false ), Run( true ), "the winter hooks read nothing for a park without winter shops or items" );
	}
}

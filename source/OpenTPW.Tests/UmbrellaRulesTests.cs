using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// The pure umbrella rules (<see cref="UmbrellaRules"/>, docs/UMBRELLAS.md, phase U2): the weather mapping, the brolly shop's score gate and multipliers, the balloon factor with its zero quirk, the day gate, the transition table,
/// the reaction delay and the colour draw. Each expectation is the arithmetic of the traced Theme Park Inc routines (the addresses are in the rules' comments), not the simulation's.
/// </summary>
// [BIN:TPI-EXE:0x611050 umbrella weather rule] the rules under test follow the Theme Park Inc no-CD Game.exe, traced statically (docs/UMBRELLAS.md)
[TestClass]
public class UmbrellaRulesTests
{
	private static readonly UmbrellaShopTerms Brolly = new( 200, 200, 150, 5, 100 );

	[TestMethod]
	public void TheParkWeatherMapsToTheStatesOfTheTraceAndHasNoHeatWave()
	{
		Assert.AreEqual( UmbrellaRules.WeatherRain, UmbrellaRules.WeatherStateOf( rain: true, lightning: false, snow: false ), "rain without lightning is rain" );
		Assert.AreEqual( UmbrellaRules.WeatherStorm, UmbrellaRules.WeatherStateOf( rain: true, lightning: true, snow: false ), "lightning is a storm" );
		Assert.AreEqual( UmbrellaRules.WeatherStorm, UmbrellaRules.WeatherStateOf( rain: false, lightning: true, snow: false ), "a storm needs no rain flag" );
		Assert.AreEqual( UmbrellaRules.WeatherSnow, UmbrellaRules.WeatherStateOf( rain: false, lightning: false, snow: true ) );
		Assert.AreEqual( UmbrellaRules.WeatherNone, UmbrellaRules.WeatherStateOf( false, false, false ) );
		Assert.AreEqual( (2, 3, 4, 1), (UmbrellaRules.WeatherRain, UmbrellaRules.WeatherStorm, UmbrellaRules.WeatherSnow, UmbrellaRules.WeatherHeatWave), "the weather thing's word +0x70 of Theme Park Inc" );
		Assert.IsTrue( new[] { 2, 3, 4 }.All( UmbrellaRules.IsBadWeather ) );
		Assert.IsFalse( new[] { 0, 1, 5, -1 }.Any( UmbrellaRules.IsBadWeather ), "none and a heat wave are not bad weather" );
	}

	[TestMethod]
	public void ABrollyShopScoresZeroWithAnUmbrellaOrOutsideBadWeather()
	{
		foreach ( var weather in new[] { 0, 1, 2, 3, 4, 5 } )
			foreach ( var state in new[] { 0, 1, 2, 3, 4 } )
				Assert.AreEqual( state != 0 || weather is not (2 or 3 or 4), UmbrellaRules.ShopScoresZero( weather, state ), $"weather {weather}, umbrella state {state}" );
	}

	[TestMethod]
	public void ABalloonShopScoresZeroForABalloonHolderAndForAnOpenOrOpeningUmbrella()
	{
		Assert.IsFalse( UmbrellaRules.BalloonShopScoresZero( false, 0, 0 ) );
		Assert.IsTrue( UmbrellaRules.BalloonShopScoresZero( true, 0, 0 ), "the balloon's model (+0x294)" );
		Assert.IsTrue( UmbrellaRules.BalloonShopScoresZero( false, 40, 0 ), "its life (+0x298), the model put away on a ride" );
		foreach ( var state in new[] { 0, 1, 2, 3, 4 } )
			Assert.AreEqual( state is 3 or 4, UmbrellaRules.BalloonShopScoresZero( false, 0, state ), $"umbrella state {state}: only 3 and 4 (the shop is checked at 0x61041b against 4 and 3)" );
	}

	[TestMethod]
	public void TheWeatherAndGenerationPercentagesScaleTheScoreInSignedIntegers()
	{
		Assert.AreEqual( 54u, UmbrellaRules.ApplyWeather( 27, UmbrellaRules.WeatherRain, Brolly ), "rain 200 %" );
		Assert.AreEqual( 54u, UmbrellaRules.ApplyWeather( 27, UmbrellaRules.WeatherStorm, Brolly ), "storm 200 %" );
		Assert.AreEqual( 61u, UmbrellaRules.ApplyWeather( 41, UmbrellaRules.WeatherSnow, Brolly ), "snow 150 %: 6150 / 100 truncated" );
		Assert.AreEqual( 5u, UmbrellaRules.ApplyWeather( 100, UmbrellaRules.WeatherHeatWave, Brolly ), "heat wave 5 %" );
		Assert.AreEqual( 27u, UmbrellaRules.ApplyWeather( 27, UmbrellaRules.WeatherNone, Brolly ), "no weather: unchanged" );
		Assert.AreEqual( 27u, UmbrellaRules.ApplyWeather( 27, UmbrellaRules.WeatherRain, new UmbrellaShopTerms( 0, 0, 0, 0, 100 ) ), "a zero percentage leaves the score (the nonzero test before each multiply)" );
		Assert.AreEqual( 20u, UmbrellaRules.Percent( 27, 75 ), "the generation percentage of Theme Park Inc's kids: 2025 / 100" );
		Assert.AreEqual( 27u, UmbrellaRules.Percent( 27, 100 ) );
		Assert.AreEqual( 0u, UmbrellaRules.Percent( 27, 0 ), "a generation word of 0 gives a score of 0 (no nonzero test there)" );
	}

	[DataTestMethod]
	[DataRow( 255, 100, null )]   // a full-life balloon: 100 - 100 = 0 skips the cut (Theme Park Inc's quirk)
	[DataRow( 0, 100, null )]     // no life
	[DataRow( 127, 100, 51 )]     // 127 x 100 / 255 = 49, 100 - 49
	[DataRow( 1, 100, 100 )]      // 1 x 100 / 255 = 0: no share
	[DataRow( 254, 100, 1 )]      // 99 % of life
	[DataRow( 127, 50, 25 )]      // the discard percent halves the factor: 51 x 50 / 100
	[DataRow( 127, 0, 51 )]       // a modifier of 0 is not applied
	[DataRow( 128, 200, 100 )]    // 128 x 100 / 255 = 50, 100 - 50 = 50, 50 x 200 / 100 = 100
	public void TheBalloonFactorIsTheShareOfLifeLeftAndSkipsAFullBalloon( int life, int discard, int? expected )
	{
		Assert.AreEqual( expected is null ? null : (int?)expected, UmbrellaRules.BalloonFactor( true, life, discard ) );
	}

	[TestMethod]
	public void TheBalloonCutScalesTheScoreOnlyWhileTheGuestHoldsABalloonWithLife()
	{
		Assert.AreEqual( 200u, UmbrellaRules.ApplyBalloonFactor( 200, false, 0 ), "no balloon, no cut" );
		Assert.AreEqual( 102u, UmbrellaRules.ApplyBalloonFactor( 200, true, 127 ), "200 x 51 / 100" );
		Assert.AreEqual( 102u, UmbrellaRules.ApplyBalloonFactor( 200, false, 127 ), "the life alone is enough: the model is put away on a ride while the life stays (+0x298 above 0)" );
		Assert.AreEqual( 200u, UmbrellaRules.ApplyBalloonFactor( 200, true, 0 ), "the model without life (+0x298 of 0) skips the cut" );
		Assert.AreEqual( 200u, UmbrellaRules.ApplyBalloonFactor( 200, true, 255 ), "the quirk: a full-life balloon does not lower the shop's score at all, while a nearly full one cuts it to a hundredth" );
		Assert.AreEqual( 2u, UmbrellaRules.ApplyBalloonFactor( 200, true, 254 ) );
		Assert.AreEqual( 7050327u, UmbrellaRules.ApplyBalloonFactor( 100_000_000, true, 128 ), "factor 50 x 100000000 wraps at 32 bits before the division by 100, as the original's imul does" );
	}

	[DataTestMethod]
	[DataRow( 0L, true )]
	[DataRow( 20L, true )]     // 20 turns of 3750 s: 0.87 days
	[DataRow( 23L, true )]     // 23 turns: cut to 20, still day 0
	[DataRow( 24L, false )]    // 24 turns: 90000 s, day 1
	[DataRow( 60L, false )]
	[DataRow( 91L, false )]    // cut to 88: 330000 s, day 3
	[DataRow( 96L, true )]     // 96 turns: 360000 s, day 4 (a multiple of the frequency)
	[DataRow( 115L, true )]    // cut to 112: 420000 s, day 4
	[DataRow( 116L, false )]   // 435000 s, day 5
	[DataRow( 192L, true )]    // 720000 s = day 8
	public void TheWeatherRuleRunsOnTheFirstGameDayAndThenOneDayInFour( long ageTurns, bool open )
	{
		Assert.AreEqual( open, UmbrellaRules.DayGateOpen( ageTurns ), $"age {ageTurns} turns" );
	}

	[TestMethod]
	public void TheDayGateCutsTheAgeToWholeUpdatesAndTakesTheFrequencyFromTheSettings()
	{
		Assert.AreEqual( UmbrellaRules.DayGateOpen( 96 ), UmbrellaRules.DayGateOpen( 99 ), "the age is cut to a multiple of four turns first (>> 2 << 2)" );
		Assert.IsTrue( UmbrellaRules.DayGateOpen( 24, frequency: 1 ), "every day with a frequency of 1" );
		Assert.IsFalse( UmbrellaRules.DayGateOpen( 24, frequency: 2 ) );
		Assert.IsTrue( UmbrellaRules.DayGateOpen( 48, frequency: 2 ), "day 2" );
		Assert.IsTrue( UmbrellaRules.DayGateOpen( 500, frequency: 0 ), "no frequency, no gate" );
	}

	[TestMethod]
	public void TheTransitionTableIsTheTraceOfTheWeatherRule()
	{
		var table = new Dictionary<(int State, bool Bad), UmbrellaAction>
		{
			[(0, true)] = UmbrellaAction.None,
			[(0, false)] = UmbrellaAction.None,
			[(1, true)] = UmbrellaAction.CancelClose,
			[(1, false)] = UmbrellaAction.CarryOut,
			[(2, true)] = UmbrellaAction.ScheduleOpen,
			[(2, false)] = UmbrellaAction.None,
			[(3, true)] = UmbrellaAction.CarryOut,
			[(3, false)] = UmbrellaAction.CancelOpen,
			[(4, true)] = UmbrellaAction.None,
			[(4, false)] = UmbrellaAction.ScheduleClose
		};
		foreach ( var ((state, bad), action) in table )
			Assert.AreEqual( action, UmbrellaRules.WeatherAction( state, bad ), $"state {state}, bad weather {bad}" );
		Assert.AreEqual( UmbrellaAction.None, UmbrellaRules.WeatherAction( 9, true ), "a state outside 0..4 does nothing (the jump table's ja)" );
	}

	[TestMethod]
	public void EnteringAnAttractionClosesAnUmbrellaThatHasASpriteAndNothingElse()
	{
		Assert.AreEqual( 0, UmbrellaRules.StateAfterEntering( 0 ) );
		Assert.AreEqual( 2, UmbrellaRules.StateAfterEntering( 1 ), "open until a due close: closed" );
		Assert.AreEqual( 2, UmbrellaRules.StateAfterEntering( 2 ), "closed stays closed" );
		Assert.AreEqual( 3, UmbrellaRules.StateAfterEntering( 3 ), "a pending open has no sprite yet: 0x6142e0 does nothing without one" );
		Assert.AreEqual( 2, UmbrellaRules.StateAfterEntering( 4 ), "open: closed" );
	}

	[TestMethod]
	public void TheReactionDelayIsBelowTheSlowestResponseAndTakesNoDrawFromAStream()
	{
		var seen = new HashSet<int>();
		for ( var guest = 13; guest < 400; guest++ )
			for ( long turn = 0; turn < 40; turn++ )
			{
				var delay = UmbrellaRules.Delay( guest, turn, false, 0 );
				Assert.IsTrue( delay is >= 0 and < UmbrellaRules.SlowestWeatherChangeResponse, $"guest {guest}, turn {turn}: {delay}" );
				Assert.AreEqual( delay, UmbrellaRules.Delay( guest, turn, false, 0 ), "a pure function of the guest and the turn" );
				seen.Add( delay );
			}
		CollectionAssert.AreEquivalent( new[] { 0, 1, 2 }, seen.ToArray(), "every delay below the bound occurs" );
		Assert.IsTrue( Enumerable.Range( 0, 64 ).Select( guest => UmbrellaRules.Delay( 20 + guest, 100, false, 0 ) ).Distinct().Count() > 1, "different guests react at different delays" );
	}

	[TestMethod]
	public void ABalloonLengthensTheDelayByItsShareOfLifeAndAFullBalloonDoublesIt()
	{
		for ( var guest = 13; guest < 200; guest++ )
		{
			var plain = UmbrellaRules.Delay( guest, 80, false, 0 );
			Assert.AreEqual( plain * 2, UmbrellaRules.Delay( guest, 80, true, 255 ), "a full balloon: the delay plus 100 % of itself" );
			Assert.AreEqual( plain, UmbrellaRules.Delay( guest, 80, true, 0 ), "a model without life adds nothing" );
			Assert.AreEqual( plain, UmbrellaRules.Delay( guest, 80, false, 2 ), "a share of 0 (life 2 is 0 percent) adds nothing" );
		}
		// a larger bound shows the share: the same hash modulo 100, 127 x 100 / 255 = 49 percent more
		var delay = UmbrellaRules.Delay( 33, 5, false, 0, slowest: 100 );
		Assert.AreEqual( delay + delay * 49 / 100, UmbrellaRules.Delay( 33, 5, true, 127, slowest: 100 ) );
		Assert.AreEqual( delay + delay * 24 / 100, UmbrellaRules.Delay( 33, 5, true, 127, slowest: 100, discardPercent: 50 ), "scaled by DiscardBalloonPctModifier / 100" );
		Assert.AreEqual( 0, UmbrellaRules.Delay( 33, 5, false, 0, slowest: 0 ) );
	}

	[TestMethod]
	public void ADueTurnIsAWholeUpdateAheadAndTheTestReadsTheUpdateIndex()
	{
		Assert.AreEqual( 96L, UmbrellaRules.DueTurn( 97, 0 ), "the update index 97 >> 2 = 24 plus a delay of 0, as the turn 24 << 2" );
		Assert.AreEqual( 104L, UmbrellaRules.DueTurn( 97, 2 ) );
		Assert.IsFalse( UmbrellaRules.IsDue( 99, 104 ), "update 24 against 26" );
		Assert.IsFalse( UmbrellaRules.IsDue( 103, 104 ) );
		Assert.IsTrue( UmbrellaRules.IsDue( 104, 104 ) );
		Assert.IsTrue( UmbrellaRules.IsDue( 107, 104 ) );
		Assert.IsTrue( UmbrellaRules.IsDue( 500, 104 ) );
	}

	[TestMethod]
	public void TheColourIsTheWorldDrawShiftedRightByTwoModuloTheThreeSlots()
	{
		Assert.AreEqual( 3, UmbrellaRules.Colours );
		CollectionAssert.AreEqual( new[] { 0, 1, 2, 0, 1 }, new[] { 0u, 4u, 8u, 12u, 16u }.Select( UmbrellaRules.Colour ).ToArray() );
		Assert.AreEqual( UmbrellaRules.Colour( 8 ), UmbrellaRules.Colour( 11 ), "the low two bits are dropped" );
		var rng = new WorldRng( 12345 );
		for ( var index = 0; index < 500; index++ )
		{
			var draw = rng.NextWord();
			Assert.AreEqual( (int)((draw >> 2) % 3), UmbrellaRules.Colour( draw ) );
		}
	}

	[TestMethod]
	public void AnOpenOrPendingUmbrellaTakesTheWalkingSpeedTermAway()
	{
		foreach ( var state in new[] { 0, 1, 2, 3, 4 } )
			foreach ( var term in new[] { 0, 25, 50 } )
				Assert.AreEqual( state is 1 or 3 or 4 ? 0 : term, UmbrellaRules.SpeedTerm( state, term ), $"state {state}, term {term}" );
	}

	[TestMethod]
	public void OnlyAKartOrWaterTrackRecordWhoseHeadIsATrackCellStopsTheReaction()
	{
		// [BIN:TPI-EXE:0x613ea0 track-cell stand-down] 0x685680 accepts the record types 0xB, 0xC, 0x10, 0x11 and 0x67F890 ends on the track predicate 0x6856D0 (0xB, 0xD, 0x10, 0x12, 0x19), through the head for an extension
		for ( var type = 0; type < 256; type++ )
		{
			var recordTypeOk = type is 0xB or 0xC or 0x10 or 0x11;
			var isExtension = type is 0xC or 0x11;
			foreach ( var head in new byte[] { 0, 0xB, 0xC, 0xD, 0x10, 0x12, 0x19, 0x1E } )
			{
				var effective = isExtension ? head : (byte)type;
				var expected = recordTypeOk && effective is 0xB or 0xD or 0x10 or 0x12 or 0x19;
				Assert.AreEqual( expected, UmbrellaRules.OnTrackCell( (byte)type, head ), $"record {type:X}, head {head:X}" );
			}
		}
		Assert.IsTrue( UmbrellaRules.OnTrackCell( TrackCellTypes.KartOrigin, 0 ), "a kart origin" );
		Assert.IsTrue( UmbrellaRules.OnTrackCell( TrackCellTypes.Extension, TrackCellTypes.WaterOrigin ), "an extension of a water origin" );
		Assert.IsFalse( UmbrellaRules.OnTrackCell( TrackCellTypes.Extension, 0 ), "an extension without a head" );
		Assert.IsFalse( UmbrellaRules.OnTrackCell( TrackCellTypes.KartStart, TrackCellTypes.KartStart ), "a station record is not in the first test's list" );
	}

	[TestMethod]
	public void TheTrackLayerTestFollowsAnExtensionToItsOrigin()
	{
		var tracks = new TrackCellLayer( 8, 8 );
		tracks.Set( 2, 2, new TrackCell { Type = TrackCellTypes.KartOrigin, Parent = -1 } );
		tracks.Set( 3, 2, new TrackCell { Type = TrackCellTypes.Extension, Parent = tracks.IndexOf( 2, 2 ) } );
		tracks.Set( 5, 5, new TrackCell { Type = TrackCellTypes.Extension, Parent = tracks.IndexOf( 6, 6 ) } );
		Assert.IsTrue( UmbrellaRules.OnTrackCell( tracks, 2, 2 ) );
		Assert.IsTrue( UmbrellaRules.OnTrackCell( tracks, 3, 2 ) );
		Assert.IsFalse( UmbrellaRules.OnTrackCell( tracks, 5, 5 ), "an extension whose origin cell holds no track" );
		Assert.IsFalse( UmbrellaRules.OnTrackCell( tracks, 4, 4 ), "an empty cell" );
		Assert.IsFalse( UmbrellaRules.OnTrackCell( tracks, 20, 20 ), "outside the map" );
	}
}

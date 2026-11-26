using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// The Standard words Theme Park Inc's native guest states read by address (<see cref="ThemeParkIncGuestSettings"/>): every address against the key the SAM binding gives it, and the shipped Science
/// values with OPENTPW_TPI_PATH.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class ThemeParkIncGuestSettingsTests
{
	private BaseFileSystem? previous;
	private GameEdition previousEdition;

	[TestInitialize]
	public void Initialize()
	{
		previous = FileSystem;
		previousEdition = GameEditions.Current;
		Log ??= new();
	}

	[TestCleanup]
	public void Cleanup()
	{
		FileSystem = previous!;
		GameEditions.Select( previousEdition );
	}

	/// <summary>Each key a distinct value in its record's range; the settings must find every value at its address.</summary>
	[TestMethod]
	public void EveryAddressHoldsTheKeyTheBindingGivesIt()
	{
		var lines = new List<string>
		{
			"PeepInfo.SmallHappinessChange 11", "PeepInfo.MediumHappinessChange 12", "PeepInfo.BigHappinessChange 13", "PeepInfo.ExcitementToCostDivisor 14", "PeepInfo.MinimumEntryFee 15",
			"PeepInfo.CheapPriceMultiplier 0.5", "PeepInfo.AveragePriceMultiplier 1.5", "PeepInfo.ExpensivePriceMultiplier 2.5",
			"PeepInfo.OpinionForMuchTooHigh 21", "PeepInfo.OpinionForTooHigh 22", "PeepInfo.OpinionForTooLow 23", "PeepInfo.OpinionForAboutRight 24",
			"PeepInfo.StinkbombLikelihood 25", "PeepInfo.PrankHappinessThreshold 26", "PeepInfo.PrankLikelihood 27",
			"PeepInfo.UmbrellaModifier 31", "PeepInfo.WeatherModifierFrequency 32", "PeepInfo.SlowestWeatherChangeResponse 33", "PeepInfo.DiscardBalloonPctModifier 34",
			"PeepInfo.ThoughtBubbleHungry 41", "PeepInfo.ThoughtBubbleThirsty 42", "PeepInfo.ThoughtBubbleNeedTheLoo 43", "PeepInfo.ThoughtBubbleSick 44", "PeepInfo.ThoughtBubbleMess 0.25",
			"PeepInfo.ThoughtBubbleHappy 45", "PeepInfo.ThoughtBubbleUnhappy 46",
			"FixedItemInfo.NumTicketBooths 2", "TicketBooth[0].PosX 51", "TicketBooth[0].PosY 52", "TicketBooth[0].Angle 53", "TicketBooth[1].PosX 54", "TicketBooth[1].PosY 55", "TicketBooth[1].Angle 56",
			"FixedItemInfo.BusStopAPosX 61", "FixedItemInfo.BusStopAPosY 62", "FixedItemInfo.BusStopBPosX 63", "FixedItemInfo.BusStopBPosY 64", "FixedItemInfo.BusStopCPosX 65",
			"FixedItemInfo.BusStopCPosY 66", "FixedItemInfo.EntranceAPosX 67", "FixedItemInfo.EntranceAPosY 68", "FixedItemInfo.EntranceBPosX 69", "FixedItemInfo.EntranceBPosY 70",
			"FirstHour.Minutes 77",
			"PeepInfo.DecisionVariable1 81", "PeepInfo.DecisionVariable2 82", "PeepInfo.DecisionVariable3 83", "PeepInfo.DecisionVarDistWeight 84", "PeepInfo.DecisionVarQueueWeight 85",
			"PeepInfo.DecisionVarExcitementWeight 86", "PeepInfo.DecisionVarThirstWeight 87", "PeepInfo.DecisionVarHungerWeight 88", "PeepInfo.DecisionVarToiletWeight 89",
			"PeepInfo.DecisionVarIllnessWeight 90"
		};
		var columns = new[] { "Heatwave", "Rain", "Storm", "Snow" };
		foreach ( var (array, offset) in new[] { ("PeepHappinessWeatherEffects", 100), ("PeepThirstWeatherEffects", 200), ("PeepHungerWeatherEffects", 300) } )
			for ( var generation = 0; generation < 3; generation++ )
				for ( var column = 0; column < 4; column++ )
					lines.Add( $"{array}[{generation}].{columns[column]} {offset + 10 * generation + column}.5" );
		var standard = new ThemeParkIncSamObject( ThemeParkIncStandard.Schema );
		Assert.IsTrue( standard.Parse( Encoding.ASCII.GetBytes( string.Join( "\n", lines ) + "\n" ), overlay: false ), "every line parses" );

		var settings = ThemeParkIncGuestSettings.FromStandard( standard );
		Assert.AreEqual( (11, 12, 13), (settings.SmallHappinessChange, settings.MediumHappinessChange, settings.BigHappinessChange) );
		Assert.AreEqual( (14u, 15u), (settings.ExcitementToCostDivisor, settings.MinimumEntryFee) );
		Assert.AreEqual( (0.5f, 1.5f, 2.5f), (settings.CheapPriceMultiplier, settings.AveragePriceMultiplier, settings.ExpensivePriceMultiplier) );
		Assert.AreEqual( (21, 22, 23, 24), (settings.OpinionForMuchTooHigh, settings.OpinionForTooHigh, settings.OpinionForTooLow, settings.OpinionForAboutRight) );
		Assert.AreEqual( (25, 26, 27), (settings.StinkbombLikelihood, settings.PrankHappinessThreshold, settings.PrankLikelihood), "0xA01B00, 0xA01B04, 0xA01B08" );
		Assert.AreEqual( (31, 32, 33, 34), (settings.UmbrellaModifier, settings.WeatherModifierFrequency, settings.SlowestWeatherChangeResponse, settings.DiscardBalloonPctModifier) );
		Assert.AreEqual( (41, 42, 43, 44), (settings.ThoughtBubbleHungry, settings.ThoughtBubbleThirsty, settings.ThoughtBubbleNeedTheLoo, settings.ThoughtBubbleSick) );
		Assert.AreEqual( (0.25f, 45, 46), (settings.ThoughtBubbleMess, settings.ThoughtBubbleHappy, settings.ThoughtBubbleUnhappy) );
		for ( var generation = 0; generation < 3; generation++ )
		{
			Assert.AreEqual( new ThemeParkIncWeatherRow( 100.5f + 10 * generation, 101.5f + 10 * generation, 102.5f + 10 * generation, 103.5f + 10 * generation ), settings.HappinessWeather[generation] );
			Assert.AreEqual( new ThemeParkIncWeatherRow( 200.5f + 10 * generation, 201.5f + 10 * generation, 202.5f + 10 * generation, 203.5f + 10 * generation ), settings.ThirstWeather[generation] );
			Assert.AreEqual( new ThemeParkIncWeatherRow( 300.5f + 10 * generation, 301.5f + 10 * generation, 302.5f + 10 * generation, 303.5f + 10 * generation ), settings.HungerWeather[generation] );
		}
		Assert.AreEqual( 2, settings.NumTicketBooths );
		Assert.AreEqual( (51, 52, 53), settings.TicketBooths[0] );
		Assert.AreEqual( (54, 55, 56), settings.TicketBooths[1] );
		Assert.AreEqual( ((61, 62), (63, 64), (65, 66)), (settings.BusStopA, settings.BusStopB, settings.BusStopC) );
		Assert.AreEqual( ((67, 68), (69, 70)), (settings.EntranceA, settings.EntranceB) );
		Assert.AreEqual( 77, settings.FirstHourMinutes, "FirstHour.Minutes at 0xA034D4 (0x650890)" );
		Assert.AreEqual( new DecisionVariables( 81, 82, 0 ), settings.DecisionVariables, "0xA01AA4, 0xA01AA8; DecisionVariable3 is unread" );
		Assert.AreEqual( new DecisionWeights( 84, 85, 86, 87, 88, 89, 90 ), settings.DecisionWeights, "0xA01AC4..0xA01ADC" );
		Assert.AreEqual( ThemeParkIncGuestSettings.CellWord( 51, 52 ), settings.BoothCell( 0 ), "0x5D79B0: (PosY << 7) + PosX + 1" );
		Assert.AreEqual( (61, 62), settings.StopCell( -1 ) );
		Assert.AreEqual( (63, 64), settings.StopCell( 1 ) );
		Assert.AreEqual( (65, 66), settings.StopCell( 2 ) );
		Assert.AreEqual( (69, 70), settings.EntranceCell( 1 ) );
	}

	[TestMethod]
	public void TheShippedScienceValuesAreRead()
	{
		ThemeParkIncObstacleTests.UseTpiData();
		var settings = ThemeParkIncGuestSettings.Load( ParkEconomy.GameCareerFiles.Instance.Read, "science" )!;
		// data\levels\Standard.sam and levels\science\Standard.sam
		Assert.AreEqual( (5, 15, 25), (settings.SmallHappinessChange, settings.MediumHappinessChange, settings.BigHappinessChange) );
		Assert.AreEqual( (6u, 20u), (settings.ExcitementToCostDivisor, settings.MinimumEntryFee) );
		Assert.AreEqual( (0.75f, 1.25f, 2f), (settings.CheapPriceMultiplier, settings.AveragePriceMultiplier, settings.ExpensivePriceMultiplier) );
		Assert.AreEqual( (0, 20, 100, 50), (settings.OpinionForMuchTooHigh, settings.OpinionForTooHigh, settings.OpinionForTooLow, settings.OpinionForAboutRight) );
		Assert.AreEqual( (25, 15, 10), (settings.StinkbombLikelihood, settings.PrankHappinessThreshold, settings.PrankLikelihood) );
		Assert.AreEqual( (1, 4, 3, 100), (settings.UmbrellaModifier, settings.WeatherModifierFrequency, settings.SlowestWeatherChangeResponse, settings.DiscardBalloonPctModifier) );
		Assert.AreEqual( (80, 80, 80, 80, 0.3f, 80, 20), (settings.ThoughtBubbleHungry, settings.ThoughtBubbleThirsty, settings.ThoughtBubbleNeedTheLoo, settings.ThoughtBubbleSick,
			settings.ThoughtBubbleMess, settings.ThoughtBubbleHappy, settings.ThoughtBubbleUnhappy) );
		Assert.AreEqual( new ThemeParkIncWeatherRow( 1, -1, -1, 1 ), settings.HappinessWeather[0] );
		Assert.AreEqual( new ThemeParkIncWeatherRow( 1, -1, -1.5f, -2 ), settings.HappinessWeather[1] );
		Assert.AreEqual( 2, settings.NumTicketBooths );
		Assert.AreEqual( (45, 11, 512), settings.TicketBooths[0] );
		Assert.AreEqual( (45, 8, 512), settings.TicketBooths[1] );
		Assert.AreEqual( ((50, 2), (41, 2), (61, 7)), (settings.BusStopA, settings.BusStopB, settings.BusStopC) );
		Assert.AreEqual( ((47, 17), (48, 17)), (settings.EntranceA, settings.EntranceB) );
		Assert.AreEqual( 120, settings.FirstHourMinutes );
	}
}

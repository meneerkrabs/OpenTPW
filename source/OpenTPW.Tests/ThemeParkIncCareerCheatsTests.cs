using System;
using System.Linq;
using System.Text;
using OpenTPW.Mods;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>Theme Park Inc's debug-dialog cheats in the cheats extension (docs/CHEATS.md, "Theme Park Inc"): Skip Mission and Get Promoted.</summary>
[TestClass]
public class ThemeParkIncCareerCheatsTests
{
	private static void NextTurn( ParkEconomy economy ) => economy.Advance( ParkCalendar.TickOfTurn( economy.Turn + 1 ) - economy.Tick );

	private static ParkEconomy Economy()
	{
		var economy = ThemeParkIncChallengeTests.Economy();
		var files = new TestCareerFiles()
			.With( "/missions/Miss00.sam", "Info.Number 0\nObstacle.GoldenTickets 1\n" )
			.With( "/missions/Miss01.sam", "Info.Number 1\nCompletion.EndOfGame 1\n" )
			// the completion tick starts the next mission's statistics, whose tech level step reads a zone record (0x74F4B0): the zones' global.sam
			.With( "/levels/arabian/global.sam", "ParkInfo.AvailableFromStart 0\n" )
			.With( "/levels/science/global.sam", "ParkInfo.AvailableFromStart 1\n" )
			.With( "/levels/water/global.sam", "ParkInfo.AvailableFromStart 0\n" );
		economy.AttachThemeParkIncCareer( new ThemeParkIncCareer( files, TestCareerFiles.ShippedFiringLines ) );
		return economy;
	}

	[TestMethod]
	public void TheCheatsNeedTheExtensionAndAThemeParkIncPark()
	{
		Assert.IsFalse( Economy().QueueCheat( new CheatAction( CheatKind.SkipMission ) ), "the extension is off" );
		using ( OpenTpwExtensions.Use( CheatsTests.On() ) )
		{
			var economy = Economy();
			Assert.IsTrue( economy.QueueCheat( new CheatAction( CheatKind.SkipMission ) ) );
			Assert.IsTrue( economy.QueueCheat( new CheatAction( CheatKind.GetPromoted ) ) );
			Assert.IsFalse( economy.QueueCheat( new CheatAction( CheatKind.GetPromoted, 1 ) ), "no argument" );
			Assert.IsFalse( economy.QueueCheat( new CheatAction( CheatKind.Money ) ), "Theme Park World's cheats stay off in a Theme Park Inc park" );
			var world = new ParkEconomy( EconomyTestData.Settings(), EconomyTestData.Catalog(), ParkGameMode.FullSimulation, 7 );
			Assert.IsFalse( world.QueueCheat( new CheatAction( CheatKind.SkipMission ) ), "a Theme Park World park has no mission" );
		}
	}

	[TestMethod]
	public void SkipMissionRunsTheCompletionTickAndGetPromotedRaisesTheRank()
	{
		using var extensions = OpenTpwExtensions.Use( CheatsTests.On() );
		var economy = Economy();
		var career = economy.TpiCareer!;
		var outcomes = new System.Collections.Generic.List<string>();
		economy.EventRaised += parkEvent =>
		{
			if ( parkEvent.Kind == ParkEventKind.CheatApplied )
				outcomes.Add( parkEvent.Detail! );
		};
		Assert.IsTrue( economy.QueueCheat( new CheatAction( CheatKind.SkipMission ) ) );
		Assert.IsTrue( economy.QueueCheat( new CheatAction( CheatKind.GetPromoted ) ) );
		NextTurn( economy );
		Assert.AreEqual( (1, 2, 5, 0), (career.MissionCounter, career.Rank, career.FiringLine, career.GoldenTickets), "the tick advances without the ticket it could not spend; rank 2's limit is 5" );
		Assert.AreEqual( 1, career.Regions[13], "the advance opens the counter's region" );
		CollectionAssert.AreEqual( new[] { "1", "2" }, outcomes );
		Assert.IsTrue( economy.Cheats.Mode && economy.Cheats.Extension );
		economy.QueueCheat( new CheatAction( CheatKind.SkipMission ) );
		NextTurn( economy );
		Assert.AreEqual( (true, 1), (career.GameFinished, career.MissionCounter), "the end-of-game mission finishes the game instead of advancing" );
		economy.QueueCheat( new CheatAction( CheatKind.SkipMission ) );
		NextTurn( economy );
		Assert.AreEqual( "finished", outcomes.Last(), "a finished game's tick does nothing" );
		for ( var rank = 3; rank <= 6; rank++ )
		{
			economy.QueueCheat( new CheatAction( CheatKind.GetPromoted ) );
			NextTurn( economy );
		}
		Assert.AreEqual( (6, "6"), (career.Rank, outcomes.Last()) );
		economy.QueueCheat( new CheatAction( CheatKind.GetPromoted ) );
		NextTurn( economy );
		Assert.AreEqual( (7, "7 (unread limit)", 6), (career.Rank, outcomes.Last(), career.FiringLine), "the test table answers no word for rank 7: the rank is set, the line kept (rank 6 raised it to the count dword, 6)" );
	}

	[TestMethod]
	public void ACheatedThemeParkIncParkSavesAndLoads()
	{
		using var extensions = OpenTpwExtensions.Use( CheatsTests.On() );
		var economy = Economy();
		economy.QueueCheat( new CheatAction( CheatKind.GetPromoted ) );
		NextTurn( economy );
		var json = ParkSaveFile.Serialize( economy );
		var restored = ParkSaveFile.Restore( ParkSaveFile.Deserialize( Encoding.UTF8.GetBytes( json ) ), economy.Settings, economy.Catalog );
		Assert.AreEqual( (2, true), (restored.TpiCareer!.Rank, restored.Cheats.Mode) );
		Assert.AreEqual( CheatKind.GetPromoted, restored.Cheats.Log.Single().Kind );
	}
}

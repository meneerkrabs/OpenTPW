using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
namespace OpenTPW.Tests;

[TestClass]
public class ImportedInitialCalendarTests
{
	[TestInitialize]
	public void Setup() => Log ??= new();

	private static ParkEconomy Import( uint turn = 0, int day = -1, int rate = 15000 )
	{
		var settings = OriginalSaveImportTests.Settings();
		var park = new ParkEconomy( settings, EconomyTestData.Catalog(), ParkGameMode.FullSimulation, 1 );
		var save = OriginalSaveImportTests.Park( settings, gameTick: turn,
			calendar: SaveTestPayloads.CalendarAndArrivals( rate: rate, month: 0, day: day ) );
		OriginalEconomyImport.Apply( park, save );
		return park;
	}
	private sealed class RecordingSweep( Action action ) : IThingSweep
	{
		public void Run( ParkEconomy economy, long turn, bool skipThings ) => action();
	}

	private static ParkEconomy Roundtrip( ParkEconomy park ) => ParkSaveFile.Restore(
		ParkSaveFile.Deserialize( Encoding.UTF8.GetBytes( ParkSaveFile.Serialize( park ) ) ), park.Settings, EconomyTestData.Catalog() );

	[TestMethod]
	public void ImportedRestartDispatchesOneDayOnItsFirstActualTurnThenAtMidnight()
	{
		var park = Import();
		var events = new List<ParkEventKind>();
		park.EventRaised += item => events.Add( item.Kind );
		Assert.IsTrue( park.PendingInitialDay );
		park.Advance( ParkCalendar.TickOfTurn( 1 ) - 1 );
		Assert.IsTrue( park.PendingInitialDay );
		Assert.IsFalse( events.Contains( ParkEventKind.DayEnded ) );
		park.Advance( 1 );
		Assert.IsFalse( park.PendingInitialDay );
		Assert.AreEqual( 1, events.Count( kind => kind == ParkEventKind.DayEnded ) );
		Assert.IsFalse( events.Contains( ParkEventKind.MonthEnded ) );
		Assert.IsFalse( events.Contains( ParkEventKind.YearEnded ) );
		park.Advance( ParkCalendar.TickAtDay( 1 ) - park.Tick - 1 );
		Assert.AreEqual( 1, events.Count( kind => kind == ParkEventKind.DayEnded ) );
		park.Advance( 1 );
		Assert.AreEqual( 2, events.Count( kind => kind == ParkEventKind.DayEnded ) );
	}
	[TestMethod]
	public void FirstDayRollsObjectHistoriesAndAccumulatesDailyChallenge()
	{
		var park = Import();
		var item = park.RegisterExisting( 1303 );
		var rings = new[] { item.CostsRing, item.TakingsRing, item.CustomersRing, item.WinnersRing, item.SatisfactionRing };
		foreach ( var ring in rings ) ring.Today = 7;
		item.WalkAwaysToday = 9;
		park.Settings.RestoreAdoptedChallenges( new[] { new ChallengeDefinition( 9, 24, 0, 60, 9999, 0, 0, 0, 1000, true, true ) } );
		park.RestoreOriginalChallenges( 600, 0, 9, 600, 1234, null, new Dictionary<int, int>(), Array.Empty<int>() );
		var swept = false;
		park.ThingSweep = new RecordingSweep( () =>
		{
			Assert.IsTrue( park.PendingInitialDay );
			swept = true;
			item.CustomersRing.Today++;
		} );
		park.Advance( ParkCalendar.TickOfTurn( 1 ) );
		Assert.IsTrue( swept );
		foreach ( var ring in rings )
		{
			Assert.AreEqual( 0, ring.Head ); Assert.AreEqual( 0, ring.Today );
			Assert.IsTrue( ring.TryGet( 0, out var value ) ); Assert.AreEqual( ring == item.CustomersRing ? 8 : 7, value );
		}
		Assert.AreEqual( 0L, item.WalkAwaysToday );
		CollectionAssert.AreEqual( new[] { 9L }, item.WalkAwayDays.ToArray() );
		Assert.AreEqual( 1334L, park.Objectives.Current!.Baseline, "type 24 without toilets adds 100 on the day message" );
	}
	[DataTestMethod]
	[DataRow( 0L )]
	[DataRow( 7L )]
	public void PendingDaySurvivesOwnSaveBeforeFirstTurnAndStaysConsumedAfterward( long tick )
	{
		var park = Import(); park.Advance( tick );
		var restored = Roundtrip( park );
		Assert.IsTrue( restored.PendingInitialDay );
		Assert.AreEqual( ParkSaveFile.Serialize( park ), ParkSaveFile.Serialize( restored ) );
		var remaining = ParkCalendar.TickOfTurn( 1 ) - tick;
		park.Advance( remaining ); restored.Advance( remaining );
		Assert.AreEqual( ParkSaveFile.Serialize( park ), ParkSaveFile.Serialize( restored ) );
		var after = Roundtrip( restored ); Assert.IsFalse( after.PendingInitialDay );
		var days = 0; after.EventRaised += item => { if ( item.Kind == ParkEventKind.DayEnded ) days++; };
		after.Advance( ParkCalendar.TickOfTurn( 2 ) - after.Tick ); Assert.AreEqual( 0, days );
	}
	[TestMethod]
	public void PendingStateChangesSaveHashAndOldSavesRemainSynchronized()
	{
		var park = Import(); var pending = ParkSaveFile.Serialize( park );
		var pendingHash = WorldStateHash.Compute( new WorldStateSources { Economy = park } );
		Assert.AreEqual( pendingHash, WorldStateHash.Compute( new WorldStateSources { Economy = Roundtrip( park ) } ) );
		park.PendingInitialDay = false; var synchronized = ParkSaveFile.Serialize( park );
		Assert.IsFalse( synchronized.Contains( "PendingInitialDay" ) );
		Assert.IsTrue( pending.Contains( "\"PendingInitialDay\": true" ) );
		Assert.AreNotEqual( pendingHash, WorldStateHash.Compute( new WorldStateSources { Economy = park } ) );
		Assert.IsFalse( Roundtrip( park ).PendingInitialDay );
	}
	[TestMethod]
	public void PendingStateAfterFirstTurnIsRejectedByParseAndRestore()
	{
		var park = Import(); park.Advance( ParkCalendar.TickOfTurn( 1 ) );
		park.PendingInitialDay = true; // deliberately malformed save, never produced by normal play
		Assert.ThrowsException<InvalidDataException>( () => ParkSaveFile.Deserialize( Encoding.UTF8.GetBytes( ParkSaveFile.Serialize( park ) ) ) );
		Assert.ThrowsException<InvalidDataException>( () => ParkSaveFile.Restore( ParkSaveFile.Capture( park ), park.Settings, EconomyTestData.Catalog() ) );
	}
	[TestMethod]
	public void UpdatedAndUnsupportedCalendarsDoNotAcquireInitialDispatch()
	{
		foreach ( var park in new[] { Import( day: 1 ), Import( rate: 16000, day: 1 ), Import( turn: 1 ) } )
		{
			Assert.IsFalse( park.PendingInitialDay ); var days = 0;
			park.EventRaised += item => { if ( item.Kind == ParkEventKind.DayEnded ) days++; };
			park.Advance( ParkCalendar.TickOfTurn( 2 ) - park.Tick ); Assert.AreEqual( 0, days );
		}
	}

	/// <summary>ECON-063: a new park owes the day message the calendar constructor's day cache -1 makes its first update post (0x4F7E80, 0x4F8260), once, whatever the date.</summary>
	[TestMethod]
	public void ANewParkPostsOneDayMessageOnItsFirstActualTurn()
	{
		var park = EconomyTestData.Park();
		Assert.IsTrue( park.PendingInitialDay );
		var days = 0;
		park.EventRaised += item => { if ( item.Kind == ParkEventKind.DayEnded ) days++; };
		park.Advance( ParkCalendar.TickOfTurn( 1 ) - 1 );
		Assert.AreEqual( 0, days );
		Assert.IsTrue( park.PendingInitialDay, "no turn has run yet" );
		park.Advance( 1 );
		Assert.AreEqual( 1, days );
		Assert.IsFalse( park.PendingInitialDay );
		park.Advance( ParkCalendar.TickOfTurn( 2 ) - park.Tick );
		Assert.AreEqual( 1, days, "the second turn is no day change" );
	}

	/// <summary>Theme Park Inc has no traced calendar of its own: its park keeps the synchronised start.</summary>
	[TestMethod]
	public void ANewThemeParkIncParkKeepsTheSynchronisedStart()
	{
		var park = new ParkEconomy( OriginalSaveImportTests.Settings(), EconomyTestData.Catalog(), ParkGameMode.FullSimulation, 1, EconomyRules.ThemeParkIncMoney );
		Assert.IsFalse( park.PendingInitialDay );
	}
}

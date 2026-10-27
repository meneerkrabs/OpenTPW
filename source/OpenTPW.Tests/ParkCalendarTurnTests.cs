using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// Pins the park turn length against the Mac binary evidence (docs/M3-GATE.md, m3-gate-minute
/// paragraph: one park turn is 248 ms of scaled time and advances the park clock 3,750 s)
/// and checks that whole numbers of turns survive the tick conversions unchanged.
/// </summary>
[TestClass]
public class ParkCalendarTurnTests
{
	[TestMethod]
	public void ParkTurnLengthMatchesMacBinaryEvidence()
	{
		// BIN:STP-PPC:0x101C22E0 scheduler: eight 31 ms substeps make one park turn.
		Assert.AreEqual( 248, ParkCalendar.TurnMilliseconds );
		// BIN:STP-PPC:0x100E4394 calendar conversion: seconds = turn x mFunnySecsPerRealSec (15000) / 4.
		Assert.AreEqual( 3750L, ParkCalendar.SecondsPerTurn );
		// The 2000-01-01 00:00 calendar start the date conversion counts from.
		Assert.AreEqual( new DateTime( 2000, 1, 1, 0, 0, 0, DateTimeKind.Unspecified ), ParkCalendar.Epoch );
		// Turns are sampled from the fixed 60 Hz clock (EXT:fixed-60hz-clock).
		Assert.AreEqual( FixedStepClock.TicksPerSecond, ParkCalendar.TicksPerSecond );
	}

	[TestMethod]
	public void ThePhasedTurnsLatestIsTheTurnOrOneMoreUnlessATurnStartsAtTheTick()
	{
		// a clock whose turns are cut a phase apart (the guests' after AlignTurns): over every phase the latest turn it can be on at a tick of its own;
		// 25 turns repeat every 372 ticks, so the phases 0..371 are all there are
		for ( long tick = 0; tick < 3 * 372; tick++ )
		{
			var latest = Enumerable.Range( 0, 372 ).Max( phase => ParkCalendar.Turn( tick + phase ) - ParkCalendar.Turn( phase ) );
			Assert.AreEqual( latest, ParkCalendar.LatestPhasedTurn( tick ), $"tick {tick}" );
		}
		Assert.AreEqual( ParkCalendar.Turn( 372 ), ParkCalendar.LatestPhasedTurn( 372 ), "a turn that starts at the tick leads under no phase" );
		Assert.AreEqual( ParkCalendar.Turn( 373 ) + 1, ParkCalendar.LatestPhasedTurn( 373 ) );
	}

	[TestMethod]
	public void CatchUpCapNeverReachesTheOriginalsThreeTurnsPerFrameLimit()
	{
		// [EXT:fixed-catch-up-cap] the original runs at most three park turns a frame (0x54E360 case 10); OpenTPW's clock runs at most
		// MaximumCatchUpTicks ticks a frame, which span at most two turns wherever the frame starts
		for ( long tick = 0; tick < 5000; tick++ )
			Assert.IsTrue( ParkCalendar.Turn( tick + FixedStepClock.MaximumCatchUpTicks ) - ParkCalendar.Turn( tick ) <= 2, $"tick {tick}" );
	}

	[DataTestMethod]
	[DataRow( 0L )]
	[DataRow( 1L )]
	[DataRow( 2L )]
	[DataRow( 25L )]
	[DataRow( 7258L )]
	[DataRow( 100000L )]
	public void WholeTurnsConvertToTicksAndBackUnchanged( long turn )
	{
		var tick = ParkCalendar.TickOfTurn( turn );
		// TickOfTurn is the first tick of the turn, so it maps back to the same turn...
		Assert.AreEqual( turn, ParkCalendar.Turn( tick ) );
		// ...and the tick before it still belongs to the previous turn.
		if ( turn > 0 )
			Assert.AreEqual( turn - 1, ParkCalendar.Turn( tick - 1 ) );
		// Each whole turn advances the park clock by exactly SecondsPerTurn from the epoch.
		Assert.AreEqual( turn * ParkCalendar.SecondsPerTurn, ParkCalendar.Seconds( tick ) );
		Assert.AreEqual( ParkCalendar.Epoch.AddSeconds( turn * ParkCalendar.SecondsPerTurn ), ParkCalendar.DateTimeAt( tick ) );
	}

	// ---------------------------------------------------------------- the date of a turn and the calendar update (ORACLE.md F30)

	[DataTestMethod]
	[DataRow( 0L, 2000, 1, 1, 0 )]
	[DataRow( 23L, 2000, 1, 1, 23 )]    // 23 turns = 86,250 s: the last hour of the first day
	[DataRow( 24L, 2000, 1, 2, 1 )]     // 24 turns = 90,000 s: day 2, 01:00
	[DataRow( 714L, 2000, 1, 31, 23 )]  // 2,677,500 s
	[DataRow( 715L, 2000, 2, 1, 0 )]    // 2,681,250 s: February begins in the first turn after 31 civil days
	public void TheDateOfATurnIsTheEpochPlus3750SecondsPerTurn( long turn, int year, int month, int day, int hour )
	{
		var date = ParkCalendar.ToDate( ParkCalendar.TickOfTurn( turn ) );
		Assert.AreEqual( (year - 2000 + 1, month, day, hour), (date.Year, date.Month, date.Day, date.Hour) );
		Assert.AreEqual( new DateTime( year, month, day, hour, 0, 0 ).AddSeconds( turn * 3750 % 3600 ), ParkCalendar.DateAtTurn( turn ) );
	}

	[TestMethod]
	public void CivilLeapDaysAreRespected()
	{
		// 2000 is a leap year, 2100 is not, 2400 is (Gregorian rule of the Mac OS conversion).
		foreach ( var (year, leap) in new[] { (2000, true), (2004, true), (2100, false), (2400, true) } )
		{
			var firstOfMarch = (new DateTime( year, 3, 1 ) - ParkCalendar.Epoch).TotalSeconds;
			var lastTurnOfFebruary = (long)(firstOfMarch / ParkCalendar.SecondsPerTurn) - (firstOfMarch % ParkCalendar.SecondsPerTurn == 0 ? 1 : 0);
			var last = ParkCalendar.DateAtTurn( lastTurnOfFebruary );
			Assert.AreEqual( 2, last.Month, year.ToString() );
			Assert.AreEqual( leap ? 29 : 28, DateTime.DaysInMonth( year, 2 ), year.ToString() );
			Assert.IsTrue( last.Day >= 28 );
			Assert.AreEqual( 3, ParkCalendar.DateAtTurn( lastTurnOfFebruary + 1 ).Month, year.ToString() );
		}
	}

	[TestMethod]
	public void TheCalendarUpdateReportsTheFieldsThatChangedSinceTheLastTurn()
	{
		static CalendarChange At( long turn )
		{
			var stored = ParkCalendar.FieldsAtTurn( turn - 1 );
			return ParkCalendar.Update( ref stored, turn );
		}
		Assert.AreEqual( CalendarChange.None, At( 1 ) );
		Assert.AreEqual( CalendarChange.None, At( 23 ) );
		Assert.AreEqual( CalendarChange.Day, At( 24 ) );
		Assert.AreEqual( CalendarChange.None, At( 714 ) );
		Assert.AreEqual( CalendarChange.Day | CalendarChange.Month, At( 715 ) );                // 31 January -> 1 February: day 31 -> 1 is a change
		// 366 days of 2000 = 31,622,400 s = 8,432.64 turns: turn 8,433 is the first turn of 2001, and the first of its year.
		Assert.AreEqual( CalendarChange.None, At( 8432 ) );
		Assert.AreEqual( CalendarChange.Day | CalendarChange.Month | CalendarChange.Year, At( 8433 ) );
		Assert.AreEqual( CalendarChange.None, At( 8434 ) );
	}

	[TestMethod]
	public void TheStoredFieldsAreTheOnesTheLastUpdateSaw()
	{
		var stored = ParkCalendar.FieldsAtTurn( 0 );
		Assert.AreEqual( new CalendarFields( 0, 1, 2000 ), stored );
		Assert.AreEqual( CalendarChange.Day, ParkCalendar.Update( ref stored, 24 ) );
		Assert.AreEqual( new CalendarFields( 0, 2, 2000 ), stored );
		Assert.AreEqual( CalendarChange.None, ParkCalendar.Update( ref stored, 24 ), "an update with nothing new reports nothing" );
	}

	[TestMethod]
	public void TheParkRaisesDayMonthAndYearEventsOnTheTurnTheDateChanges()
	{
		var park = EconomyTestData.Park( initialCash: 100_000_000 );
		var events = new List<(long Turn, ParkEventKind Kind)>();
		park.EventRaised += item =>
		{
			if ( item.Kind is ParkEventKind.DayEnded or ParkEventKind.MonthEnded or ParkEventKind.YearEnded )
				events.Add( (park.Turn, item.Kind) );
		};
		park.Advance( ParkCalendar.TickOfTurn( 8433 ) );
		Assert.AreEqual( 8433, park.Turn );
		// ECON-063: the new park's first turn posts the constructor's pending day message (day cache -1, 0x4F8260), then 366 midnights and 12 month starts of the leap year 2000, then the year: all on the turn the date changed.
		Assert.AreEqual( 367, events.Count( e => e.Kind == ParkEventKind.DayEnded ) );
		Assert.AreEqual( (1L, ParkEventKind.DayEnded), events[0] );
		Assert.AreEqual( 12, events.Count( e => e.Kind == ParkEventKind.MonthEnded ) );
		Assert.AreEqual( 1, events.Count( e => e.Kind == ParkEventKind.YearEnded ) );
		Assert.AreEqual( (24L, ParkEventKind.DayEnded), events.Where( e => e.Kind == ParkEventKind.DayEnded ).ElementAt( 1 ) );
		Assert.AreEqual( 715L, events.First( e => e.Kind == ParkEventKind.MonthEnded ).Turn );
		CollectionAssert.AreEqual( new[] { (8433L, ParkEventKind.DayEnded), (8433L, ParkEventKind.MonthEnded), (8433L, ParkEventKind.YearEnded) },
			events.TakeLast( 3 ).ToArray(), "the day, month and year messages of one update come in that order" );
	}

	[TestMethod]
	public void ARealTimeDayTakesAboutSixSecondsAtNormalSpeed()
	{
		// 3,750 s per 248 ms turn: 23.04 turns = 5.714 s a day, so January (31 days, 714.24 turns) takes 177.3 s (the first whole turn
		// of February is turn 715) and the leap year 2000 (366 days) 34.9 minutes.
		var secondsOfJanuary = ParkCalendar.TickAtMonth( 1 ) / (double)ParkCalendar.TicksPerSecond;
		Assert.AreEqual( 177.3, secondsOfJanuary, 0.1 );
		var secondsOfYear = ParkCalendar.TickAtMonth( 12 ) / (double)ParkCalendar.TicksPerSecond;
		Assert.AreEqual( 2091.4, secondsOfYear, 0.1 );
		Assert.AreEqual( 5.714, 86400d / ParkCalendar.SecondsPerTurn * ParkCalendar.TurnMilliseconds / 1000, 0.001 );
	}

	[DataTestMethod]
	[DataRow( GameSpeed.Paused, 0 )]
	[DataRow( GameSpeed.Normal, 1 )]
	[DataRow( GameSpeed.Fast, 2 )]
	[DataRow( GameSpeed.Fastest, 4 )]
	public void TheSpeedScalesTheGuestsAndTheEconomyTogether( GameSpeed speed, int factor )
	{
		// Level.Update feeds the fixed clock Time.Delta x the HUD's time scale and each fixed tick advances the guests and the economy once
		// (Level.AdvanceSimulation): at a scale of 2 both get twice the ticks per real second and so stay on the same park turn. Before F32
		// the economy alone ran two or four ticks per fixed tick and its turn ran ahead of the guests'.
		var park = EconomyTestData.Park();
		park.Speed = speed;
		var guests = new GuestSimulation( GuestTests.CreateCross(), GuestTests.CreateSettings(), 1 );
		var clock = new FixedStepClock();
		var scale = GameSpeedScale.Of( speed );
		Assert.AreEqual( factor, scale );
		for ( var frame = 0; frame < 600; frame++ )   // ten real seconds at 60 frames a second
			clock.Advance( FixedStepClock.TickDuration * scale, delta =>
			{
				guests.Tick( delta );
				park.AdvanceFixedTick();
			} );
		Assert.AreEqual( 600L * factor, clock.TickCount );
		Assert.AreEqual( clock.TickCount, guests.TickCount );
		Assert.AreEqual( clock.TickCount, park.Tick );
		Assert.AreEqual( guests.ParkTurn, park.Turn, "the guests' turn is the economy's" );
		// 4.03 park turns a second at the default scale, as many times faster as the scale is.
		Assert.AreEqual( 10 * 1000 / 248.0 * factor, park.Turn, 1.01 );
	}
}

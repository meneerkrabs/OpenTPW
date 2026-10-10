using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// Pins the park turn length against the Mac binary evidence (docs/M3-GATE.md, GATE-001
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
		// Turns are sampled from the fixed 60 Hz clock (APPROX:ECON-001).
		Assert.AreEqual( FixedStepClock.TicksPerSecond, ParkCalendar.TicksPerSecond );
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
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>The month a ledger counts its closes from (the finance ring's head): derived for native parks and old saves, written only when it differs.</summary>
[TestClass]
public class LedgerStartTests
{
	private static ParkEconomy ImportedPark( long start )
	{
		var park = RecentResearchTests.Park();
		park.StartAtOriginalTurn( ParkCalendar.Turn( ParkCalendar.TickAtMonth( 200 ) ) );
		Assert.AreEqual( 200, ParkCalendar.MonthIndex( park.Tick ) );
		var totals = new Dictionary<LedgerCategory, long>();
		var months = Enumerable.Range( 56, 144 ).Select( index => new LedgerMonth( index, totals, 0, -5, 1, 1 ) ).ToArray();
		park.Ledger.Restore( -5, -5, 200, totals, months, start );
		park.PendingInitialDay = false;
		return park;
	}

	[TestMethod]
	public void ANativeParkAndAnOldSaveWriteNoStartAndDeriveIt()
	{
		var park = RecentResearchTests.Park();
		StringAssert.DoesNotMatch( ParkSaveFile.Serialize( park ), new System.Text.RegularExpressions.Regex( "StartMonthIndex" ) );
		Assert.AreEqual( 0, park.Ledger.StartMonthIndex );
		// a full history without a stored start is taken to begin at month 0; a short one began that many closes back
		Assert.AreEqual( 0, ParkLedger.DerivedStartMonthIndex( 500, 144 ) );
		Assert.AreEqual( 497, ParkLedger.DerivedStartMonthIndex( 500, 3 ) );
	}

	[TestMethod]
	public void AnImportedParksStartSurvivesASaveAndALoad()
	{
		var park = ImportedPark( 20 );
		Assert.AreEqual( 180, park.Ledger.ClosesSinceStart );
		var json = ParkSaveFile.Serialize( park );
		StringAssert.Contains( json, "\"StartMonthIndex\": 20" );
		var restored = ParkSaveFile.Restore( ParkSaveFile.Deserialize( Encoding.UTF8.GetBytes( json ) ), park.Settings, park.Catalog );
		Assert.AreEqual( 20, restored.Ledger.StartMonthIndex );
		Assert.AreEqual( 180, restored.Ledger.ClosesSinceStart );
		StringAssert.Contains( ParkSaveFile.Serialize( restored ), "\"StartMonthIndex\": 20", "and it is written again" );
	}

	[TestMethod]
	public void AStoredStartThatTheHistoryCannotHaveIsRefused()
	{
		var good = ParkSaveFile.Serialize( ImportedPark( 20 ) );
		// a full history began at least 144 closes back: 200 - 144 = 56 at the latest
		var late = good.Replace( "\"StartMonthIndex\": 20", "\"StartMonthIndex\": 150", StringComparison.Ordinal );
		Assert.ThrowsException<InvalidDataException>( () => ParkSaveFile.Deserialize( Encoding.UTF8.GetBytes( late ) ) );
		var negative = good.Replace( "\"StartMonthIndex\": 20", "\"StartMonthIndex\": -1", StringComparison.Ordinal );
		Assert.ThrowsException<InvalidDataException>( () => ParkSaveFile.Deserialize( Encoding.UTF8.GetBytes( negative ) ) );
		// a short history lost nothing, so its start is the one its length gives
		var park = RecentResearchTests.Park();
		var json = ParkSaveFile.Serialize( park ).Replace( "\"ProfitThisYear\": 0", "\"ProfitThisYear\": 0,\n      \"StartMonthIndex\": 5", StringComparison.Ordinal );
		StringAssert.Contains( json, "StartMonthIndex" );
		Assert.ThrowsException<InvalidDataException>( () => ParkSaveFile.Deserialize( Encoding.UTF8.GetBytes( json ) ) );
	}
}

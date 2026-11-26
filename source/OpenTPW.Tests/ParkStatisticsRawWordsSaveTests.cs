using System;
using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// A host save keeps the statistics history whenever it differs from a fresh park's, raw slot words included. An imported original analyser that has
/// run no month has no entries, yet its unread slots keep the saved words (Theme Park Inc's prebuilt Science analyser holds the 0xCDCDCDCD fill),
/// and the ring loader keeps them (0x4C5D70 reads every slot raw). Dropping the history from the save restored zeros in their place, so a continued
/// park's save differed from the uninterrupted park's once anything else made the history non-empty. The Science test needs OPENTPW_TPI_PATH.
/// </summary>
[TestClass, DoNotParallelize]
public class ParkStatisticsRawWordsSaveTests : ThemeParkIncScienceTestBase
{
	private const int Fill = unchecked( (int)0xCDCDCDCD );

	private static string State( ParkStatisticsHistory history ) => JsonSerializer.Serialize( history.Capture() );

	private static ParkEconomy RoundTrip( ParkEconomy park ) =>
		ParkSaveFile.Restore( ParkSaveFile.Deserialize( Encoding.UTF8.GetBytes( ParkSaveFile.Serialize( park ) ) ), park.Settings, park.Catalog );

	/// <summary>A TPW analyser that has run no month (every ring's head -1, not wrapped) whose physical slot words all hold <see cref="Fill"/>.</summary>
	private static NativeParkStatistics FilledEmptyAnalyser()
	{
		var body = SaveNewParkThings.ParkAnalyser( 0, 0 );
		for ( var ring = 0; ring < SaveParkStatistics.MonthlyCount; ring++ )
			for ( var slot = 0; slot < SaveParkStatistics.MonthlyCapacity; slot++ )
				BinaryPrimitives.WriteInt32LittleEndian( body.AsSpan( SaveParkStatistics.MonthlyOffset + ring * SaveParkStatistics.MonthlyBytes + 13 + slot * 4, 4 ), Fill );
		for ( var slot = 0; slot < SaveParkStatistics.RecentStayCapacity; slot++ )
			BinaryPrimitives.WriteInt32LittleEndian( body.AsSpan( SaveParkStatistics.RecentStaysOffset + 13 + slot * 4, 4 ), Fill );
		return SaveParkStatistics.Read( body, new SaveThing( 5, SaveThingModel.ParkAnalyser, 0, 0, 0, body.Length ) );
	}

	[TestMethod]
	public void AFreshParkStillLeavesTheHistoryOutOfItsSave()
	{
		var park = RecentResearchTests.Park();
		Assert.IsTrue( park.StatisticsHistory.IsEmpty );
		Assert.IsTrue( park.StatisticsHistory.IsFresh );
		Assert.IsNull( ParkSaveFile.Capture( park ).StatisticsHistory, "a park that never had history keeps its save bytes and run hash" );
		// an imported analyser whose slots are all zero is a fresh park's history too
		park.StatisticsHistory.RestoreOriginal( SaveParkStatistics.Read( SaveNewParkThings.ParkAnalyser( 0, 0 ),
			new SaveThing( 5, SaveThingModel.ParkAnalyser, 0, 0, 0, SaveNewParkThings.ParkAnalyser( 0, 0 ).Length ) ) );
		Assert.IsTrue( park.StatisticsHistory.IsFresh );
		Assert.IsNull( ParkSaveFile.Capture( park ).StatisticsHistory );
	}

	[TestMethod]
	public void AnEntrylessImportedHistoryKeepsItsRawSlotWordsThroughASave()
	{
		var park = RecentResearchTests.Park();
		park.StatisticsHistory.RestoreOriginal( FilledEmptyAnalyser() );
		Assert.IsTrue( park.StatisticsHistory.IsEmpty, "no month has run" );
		Assert.IsFalse( park.StatisticsHistory.IsFresh, "the unread slots keep the saved words" );
		Assert.AreEqual( Fill, park.StatisticsHistory.People.Raw( 0 ) );
		Assert.AreEqual( (double)BitConverter.Int32BitsToSingle( Fill ), park.StatisticsHistory.Happiness.Raw( 0 ) );
		Assert.IsNotNull( ParkSaveFile.Capture( park ).StatisticsHistory );
		var restored = RoundTrip( park );
		Assert.AreEqual( State( park.StatisticsHistory ), State( restored.StatisticsHistory ) );
		Assert.AreEqual( ParkSaveFile.Serialize( park ), ParkSaveFile.Serialize( restored ) );
		// the smoke's failure: an arrival makes both histories non-empty, and the continued park must still match the uninterrupted one
		park.StatisticsHistory.RecordArrival();
		restored.StatisticsHistory.RecordArrival();
		Assert.AreEqual( ParkSaveFile.Serialize( park ), ParkSaveFile.Serialize( restored ) );
	}

	[TestMethod]
	public void TheImportedScienceParkKeepsItsStatisticsRawSlotWordsThroughASave()
	{
		var economy = ScienceEconomy();
		var history = economy.StatisticsHistory;
		Assert.IsTrue( history.IsEmpty, "the prebuilt Science analyser has run no month" );
		Assert.IsFalse( history.IsFresh, "its unread slots keep the prebuilt's 0xCDCDCDCD fill" );
		foreach ( var ring in new[] { history.Arrivals, history.Stay, history.People, history.Rating, history.RecentStays } )
			for ( var slot = 0; slot < ring.Size; slot++ )
				Assert.AreEqual( Fill, ring.Raw( slot ) );
		Assert.AreEqual( (double)BitConverter.Int32BitsToSingle( Fill ), history.Happiness.Raw( 0 ) );
		var restored = RoundTrip( economy );
		Assert.AreEqual( State( history ), State( restored.StatisticsHistory ), "the save keeps the raw slot words" );
		Assert.AreEqual( ParkSaveFile.Serialize( economy ), ParkSaveFile.Serialize( restored ) );
		economy.StatisticsHistory.RecordArrival();
		restored.StatisticsHistory.RecordArrival();
		Assert.AreEqual( ParkSaveFile.Serialize( economy ), ParkSaveFile.Serialize( restored ), "after an arrival the continued park matches the uninterrupted one" );
	}
}

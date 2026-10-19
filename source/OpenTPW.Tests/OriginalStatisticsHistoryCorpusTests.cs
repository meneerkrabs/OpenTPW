using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>Optional private fixture checks; no original software is executed or save bytes redistributed.</summary>
[TestClass]
public class OriginalStatisticsHistoryCorpusTests
{
	[TestMethod]
	public void ShippedJungleEasyModeHasOneNativeMonthAndNoRecentStays()
	{
		var payload = OriginalParkImportTests.ReadEasymodePayload();
		var map = new MapFile( new MemoryStream( OriginalParkImportTests.ReadArchiveMember( "levels/jungle/terrain.wad", "base.map" ) ) );
		var imported = OriginalParkImport.Import( payload, map );
		var statistics = imported.Statistics!;
		Assert.IsNotNull( statistics );
		var history = new ParkStatisticsHistory();
		history.RestoreOriginal( statistics );
		foreach ( var (ring, expected) in new[] { (history.People, 0d), (history.Arrivals, 13d), (history.Happiness, 0d), (history.Stay, 0d), (history.Rating, 18d) } )
		{
			Assert.AreEqual( (0, false, 1), (ring.Head, ring.Wrapped, ring.Count) );
			CollectionAssert.AreEqual( new[] { expected }, ring.Oldest().ToArray() );
		}
		Assert.AreEqual( 0, history.ArrivalsThisMonth );
		Assert.AreEqual( (-1, false, 0), (history.RecentStays.Head, history.RecentStays.Wrapped, history.RecentStays.Count) );
	}
}

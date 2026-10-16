using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// The camera golden ticket (tp.exe 0x004D4A00): won when 0x004CD720 reports exactly 100 % of the cells without type 2, 7 or 30 carrying a security level (the
/// environment word +6 above 0), and no all-land ticket exists (ECON-038).
/// </summary>
[TestClass]
public class SecurityCoverageTicketTests
{
	private static ParkEconomy ParkWithCells( out ParkCellMap cells )
	{
		var park = EconomyTestData.Park();
		cells = new ParkCellMap( 4, 2 );
		park.Litter.Cells = cells;
		park.Litter.Environment.Effects[RegionEffect.Camera] = new RegionEffect( 0, 0, 0, 0, 10, 0 );
		return park;
	}

	[TestMethod]
	public void TheCoverageIsTheShareOfTerrainCellsWithASecurityLevel()
	{
		var park = EconomyTestData.Park();
		Assert.IsNull( park.SecurityCoveragePercent(), "no cell map" );
		park = ParkWithCells( out var cells );
		cells.SetType( 3, 1, 7 );
		cells.SetType( 2, 1, 2 );
		Assert.AreEqual( 0, park.SecurityCoveragePercent() );
		park.Litter.Environment.Apply( RegionEffect.Camera, 0, 0, add: true );
		Assert.AreEqual( 100 / 6, park.SecurityCoveragePercent(), "one of the six cells without type 2 or 7, truncated" );
		for ( var x = 0; x < 4; x++ )
			for ( var y = 0; y < 2; y++ )
				if ( (x, y) != (0, 0) )
					park.Litter.Environment.Apply( RegionEffect.Camera, x, y, add: true );
		Assert.AreEqual( 100, park.SecurityCoveragePercent(), "the cells of type 2 and 7 need no cover" );
		park.Litter.Environment.Apply( RegionEffect.Camera, 1, 0, add: false );
		Assert.AreEqual( 83, park.SecurityCoveragePercent(), "5 of 6, truncated: not yet the ticket" );
	}

	[TestMethod]
	public void OnlyExactlyOneHundredPercentWinsTheCameraTicketAndNoAllLandTicketExists()
	{
		var park = EconomyTestData.Park();
		bool Won( GoldenTicketKind kind, int? percent ) => park.Objectives.CheckGoldenTickets( new ParkCounters(), new Guests(), park.Research, 0, new long[7], securityCoveragePercent: percent ).Contains( kind );
		Assert.IsFalse( Won( GoldenTicketKind.CamerasEverywhere, 99 ) );
		Assert.IsFalse( Won( GoldenTicketKind.CamerasEverywhere, null ), "no cell map" );
		Assert.IsTrue( Won( GoldenTicketKind.CamerasEverywhere, 100 ) );
		var counters = new ParkCounters();
		counters.Max( ParkCounters.Record( ParkRecordKind.OwnsAllLand, 0 ), 1 );
		counters.Max( ParkCounters.Record( ParkRecordKind.CellsCoveredByCameras, 0 ), 100000 );
		var other = EconomyTestData.Park();
		var tickets = other.Objectives.CheckGoldenTickets( counters, new Guests(), other.Research, 0, new long[7], securityCoveragePercent: 50 );
		Assert.IsFalse( tickets.Contains( GoldenTicketKind.AllLandOwned ) || tickets.Contains( GoldenTicketKind.CamerasEverywhere ), "the records are not read" );
	}

	private sealed class Guests : IParkGuestStatistics
	{
		public int PeopleInPark => 0;
		public float AverageHappiness => 0;
		public int CountHappierThan( int happiness ) => 0;
		public int KidsWithBalloonsPercent => 0;
		public int KidsWithCostumesPercent => 0;
	}
}

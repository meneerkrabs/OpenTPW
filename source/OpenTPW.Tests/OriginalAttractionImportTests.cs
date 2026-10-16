using System;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// The attraction things of an original save (tp.exe attraction serializer <c>0x004DB7D0</c>): the state word (SetState <c>0x004E0E60</c>), the chance
/// of winning, the total profit and the maintenance call are imported; the rest is reported.
/// </summary>
[TestClass]
public class OriginalAttractionImportTests
{
	[TestInitialize]
	public void Setup() => Log ??= new();

	private static readonly SaveTestPayloads.AttractionFields Bouncy = new() { PositionX = 1 * 256 + 128, PositionY = 1 * 256 + 128, Angle = 0, Price = 30, Level = 0, Repair = 80, LifeGauge = 90 };

	private static (ParkEconomy Economy, OriginalEconomyImport Import, ParkObjectState Ride) Import( SaveTestPayloads.AttractionFields fields )
	{
		var settings = OriginalSaveImportTests.Settings();
		var save = OriginalSaveImportTests.Park( settings, bellyBounce: SaveTestPayloads.Attraction( fields ) );
		var economy = new ParkEconomy( settings, EconomyTestData.Catalog(), ParkGameMode.FullSimulation, 1 );
		var import = OriginalEconomyImport.Apply( economy, save );
		return (economy, import, economy.Objects.Single( item => item.InfoId == 1100 ));
	}

	[TestMethod]
	public void TheSerializerFieldsBetweenTheStateAndTheRingsDecode()
	{
		var settings = OriginalSaveImportTests.Settings();
		var save = OriginalSaveImportTests.Park( settings, bellyBounce: SaveTestPayloads.Attraction( Bouncy with { State = 1, AssignedStaff = 26, CanLoad = 0, RequestedService = 1, MarkedTurn = 700, RingToday = 3 } ) );
		var saved = save.Attractions.Single( item => item.InfoId == 1100 );
		Assert.AreEqual( ((ushort)26, 0u, 1u, 700u), (saved.AssignedStaffMember, saved.CanLoad, saved.RequestedService, saved.TimeMarkedForMaintenance) );
		Assert.AreEqual( SaveAttraction.HistoryBuffers, saved.Histories.Count );
		Assert.IsTrue( saved.Histories.All( ring => ring.Current == 3 && ring.Entries.Count == 30 && ring.WrappedAround ) );
	}

	[TestMethod]
	public void ImportsTheStateChanceOfWinningTotalProfitAndMaintenanceCall()
	{
		var (economy, import, ride) = Import( Bouncy with { State = 1, Chance = 30, Takings = 900, Costs = 150, RequestedService = 1 } );
		Assert.IsTrue( ride.IsBrokenDown, "state 1: broken down (SetState 0x004E0E60)" );
		Assert.AreEqual( RideObjectStates.BrokenDown, ParkEconomy.ObjectState( ride ) );
		Assert.AreEqual( 70, ride.ChanceOfLosingPercent, "the win byte is 100 − InitChanceOfLoosing (0x004DB3A1)" );
		Assert.AreEqual( 750L, ride.TotalProfit, "takings − costs (0x004E1C60)" );
		Assert.IsTrue( ride.MaintenanceRequested );
		Assert.IsTrue( economy.NeedsMechanic( ride.Id ) );
		Assert.IsFalse( import.Evidence.Any( line => line.StartsWith( "Attraction value adjusted", StringComparison.Ordinal ) ) );
		// the economy's own save keeps what was imported
		var restored = ParkSaveFile.Restore( ParkSaveFile.Deserialize( Encoding.UTF8.GetBytes( ParkSaveFile.Serialize( economy ) ) ), economy.Settings, economy.Catalog );
		var loaded = restored.Objects.Single( item => item.InfoId == 1100 );
		Assert.AreEqual( (true, 70, 750L, true), (loaded.IsBrokenDown, loaded.ChanceOfLosingPercent, loaded.TotalProfit, loaded.MaintenanceRequested) );
	}

	[TestMethod]
	public void ARequestedUpgradeWaitsForAMechanicAndACondemnedRideStaysBroken()
	{
		var (_, _, upgrading) = Import( Bouncy with { State = 2 } );
		Assert.AreEqual( (1, true, RideObjectStates.Upgrading), (upgrading.PendingLevel, upgrading.UpgradeCostDue, ParkEconomy.ObjectState( upgrading )),
			"state 2: the next level waits for a mechanic, its cost debited when he installs it (0x004E0050, 0x004DF8F0)" );
		var (_, _, condemned) = Import( Bouncy with { State = 4, LifeGauge = 0 } );
		Assert.AreEqual( (true, RideObjectStates.Condemned), (condemned.IsBrokenDown, ParkEconomy.ObjectState( condemned )) );
	}

	[TestMethod]
	public void AStateOrChanceASaveOfTheOriginalCannotHoldIsReported()
	{
		var (_, import, ride) = Import( Bouncy with { State = 3, Chance = 120 } );
		Assert.AreEqual( (false, RideObjectStates.Idle, 0), (ride.IsBrokenDown, ParkEconomy.ObjectState( ride ), ride.ChanceOfLosingPercent) );
		var adjusted = import.Evidence.Where( line => line.StartsWith( "Attraction value adjusted on import:", StringComparison.Ordinal ) ).ToArray();
		Assert.IsTrue( adjusted.Any( line => line.Contains( "chance of winning 120 is outside 0-100" ) ) );
		Assert.IsTrue( adjusted.Any( line => line.Contains( "state 3 does not fit a choosable type" ) ), "state 3 is the constructor's state of a type that is not choosable" );
		var (_, broken, _) = Import( Bouncy with { State = 4, LifeGauge = 60 } );
		StringAssert.Contains( broken.Evidence.Single( line => line.StartsWith( "Attraction value adjusted", StringComparison.Ordinal ) ), "OpenTPW reads it as state 1" );
	}

	[TestMethod]
	public void OriginalJungleEasymodeAttractionsImportTheirStatesAndKeepTheirInitOperatingValues()
	{
		var dataPath = OriginalParkImportTests.OriginalDataPath();
		if ( !File.Exists( Path.Combine( dataPath, "levels", "Standard.sam" ) ) )
			Assert.Inconclusive( "Original levels/Standard.sam is missing." );
		var original = FileSystem;
		FileSystem = new BaseFileSystem( dataPath );
		FileSystem.RegisterArchiveHandler<WadArchive>( ".wad" );
		try
		{
			var level = OriginalPark.Load( "jungle" );
			var runtime = ParkEconomyRuntime.ForOriginalLevel( level, ParkStartKind.InstantAction );
			var economy = runtime.Economy;
			var save = level.Save!;
			Assert.IsFalse( runtime.Import!.Evidence.Any( line => line.StartsWith( "Attraction value adjusted", StringComparison.Ordinal ) ), "states 0 and 3 fit the types' IsChoosable" );
			Assert.IsTrue( economy.Objects.All( item => !item.IsBrokenDown && item.PendingLevel == 0 && !item.MaintenanceRequested && item.TotalProfit == 0 ) );
			var spray = economy.Objects.Single( item => item.InfoId == 1303 );
			Assert.AreEqual( 75, spray.ChanceOfLosingPercent, "the Jungle Spray's saved chance of winning 25" );
			// the operating values OpenTPW does not import are its tiers' Init values in the shipped save
			var catalog = ObjectCatalog.Load( "jungle" );
			foreach ( var attraction in save.Attractions.Where( item => catalog.Find( item.InfoId ) is { Category: ObjectCategory.Ride } ) )
			{
				var row = catalog.Find( attraction.InfoId )!.Upgrades.Single( upgrade => upgrade.Level == attraction.UpgradeLevel );
				Assert.AreEqual( (row.GetInt( "InitCapacity" ), row.GetInt( "InitDuration" ), (long)row.GetInt( "InitSpeed" )),
					(attraction.OperatingCapacity, attraction.OperatingDuration, (long)attraction.OperatingSpeed), $"Info.Id {attraction.InfoId}" );
			}
			Assert.IsTrue( save.Attractions.All( item => item.QualityOfGoods == GuestAppearance.BuiltQualityOfGoods ), "every shipped quality of goods is the built 50" );
			Assert.IsTrue( save.Attractions.All( item => item.CanLoad == 1 && item.AssignedStaffMember == 0 && item.FirstInQueue == 0 && item.PersonBeingLoaded == 0 ),
				"every shipped attraction is open, unclaimed and has an empty queue: nothing the import leaves out differs from the new park" );
		}
		finally
		{
			FileSystem = original;
		}
	}
}

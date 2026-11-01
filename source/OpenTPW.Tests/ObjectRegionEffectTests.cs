using System.Linq;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// The placed objects' <c>RegionFX</c> rows (GUEST-006, tp.exe constructor <c>0x004DB090</c>, destructor <c>0x004DD0A0</c>): a toilet's clean row 1 that
/// turns into the dirty row 6 below a gauge of 25 and back when a handyman cleans it, a camera's row 4 and a fireworks object's row 7 until its show runs.
/// </summary>
[TestClass]
public class ObjectRegionEffectTests
{
	// The shipped Standard.sam rows 1, 4, 6 and 7.
	private const string Rows = """
		RegionFX[1].Radius 3
		RegionFX[1].Illness 1
		RegionFX[1].Hunger -1
		RegionFX[4].Radius 5
		RegionFX[4].Security 20
		RegionFX[6].Radius 3
		RegionFX[6].Illness 2
		RegionFX[6].Hunger -2
		RegionFX[7].Radius 6
		RegionFX[7].Happiness 2
		RegionFX[7].Attraction 10

		""";

	private static ParkEconomy Park()
	{
		var objects = EconomyTestData.Catalog().Objects.Concat( new[]
		{
			new EconomyObjectInfo( 1413, "Security Camera", ParkObjectKind.Feature, ResearchCategory.Feature, 0, new[] { new UpgradeLevelInfo( 0, 300, 0, 0, 0, new[] { 50 } ) },
				null, null, null, 0, 0, 0, 0, 0, 0, "features/camera.wad", ProvidesSecurity: true ),
			new EconomyObjectInfo( 2415, "Firework Show", ParkObjectKind.Feature, ResearchCategory.Feature, 0, new[] { new UpgradeLevelInfo( 0, 300, 0, 0, 0, new[] { 50 } ) },
				null, null, null, 0, 0, 0, 0, 0, 0, "features/firework.wad", IsFireworks: true )
		} );
		return new ParkEconomy( EconomyTestData.Settings( extra: Rows ), new EconomyObjectCatalog( objects ), ParkGameMode.FullSimulation, 7 );
	}

	[TestMethod]
	public void AToiletSpreadsItsCleanRowTurnsDirtyAndIsCleanedBack()
	{
		var park = Park();
		var toilet = park.RegisterExisting( 1402 );
		park.PlaceObjectEffects( toilet.Id, (10, 10) );
		Assert.AreEqual( ((short)0, (short)1, (short)-1, (short)0, (short)0), park.Litter.Environment.At( 10, 10 ), "the clean row 1" );
		park.PlaceObjectEffects( toilet.Id, (10, 10) );
		Assert.AreEqual( (short)1, park.Litter.Environment.At( 10, 10 ).Illness, "placed once" );
		toilet.Repair = 25.5;
		Assert.IsTrue( park.UseRelief( toilet.Id, 20 ), "25.5 − 1 is below 25" );
		Assert.AreEqual( ((short)0, (short)2, (short)-2, (short)0, (short)0), park.Litter.Environment.At( 10, 10 ), "the dirty row 6" );
		Assert.AreEqual( (short)1, park.Litter.Environment.At( 11, 10 ).Illness, "2 / 2 beside it" );
		Assert.AreEqual( (short)0, park.Litter.Environment.At( 12, 10 ).Illness, "2 / 3 truncates to 0" );

		var handyman = EconomyTestData.HireBest( park, StaffType.Handyman );
		handyman.ToiletId = toilet.Id;
		toilet.MechanicId = handyman.Id;
		park.FinishToiletClean( handyman );
		Assert.AreEqual( ((short)0, (short)1, (short)-1, (short)0, (short)0), park.Litter.Environment.At( 10, 10 ), "clean again" );
		park.Sell( toilet.Id );
		Assert.AreEqual( default, park.Litter.Environment.At( 10, 10 ), "selling takes the row off" );
	}

	[TestMethod]
	public void ACameraGuardsItsSquareAndFireworksStopOnceTheyHaveRun()
	{
		var park = Park();
		var camera = park.RegisterExisting( 1413 );
		park.PlaceObjectEffects( camera.Id, (20, 20) );
		Assert.AreEqual( (short)20, park.Litter.Environment.At( 20, 20 ).Security );
		Assert.AreEqual( (short)4, park.Litter.Environment.At( 22, 22 ).Security, "20 / 5" );
		Assert.AreEqual( (short)0, park.Litter.Environment.At( 26, 20 ).Security, "outside the radius 5" );

		var show = park.RegisterExisting( 2415 );
		park.PlaceObjectEffects( show.Id, (40, 40) );
		Assert.AreEqual( ((short)2, (short)0, (short)0, (short)0, (short)10), park.Litter.Environment.At( 40, 40 ) );
		var json = ParkSaveFile.Serialize( park );
		Assert.IsFalse( json.Contains( "FireworksSpent" ), "absent until the show runs" );
		Assert.IsTrue( park.FireworksFired( show.Id ) );
		Assert.IsFalse( park.FireworksFired( show.Id ), "once" );
		Assert.AreEqual( default, park.Litter.Environment.At( 40, 40 ) );

		var restored = ParkSaveFile.Restore( ParkSaveFile.Deserialize( Encoding.UTF8.GetBytes( ParkSaveFile.Serialize( park ) ) ), park.Settings, park.Catalog );
		Assert.IsTrue( restored.TryGetObject( show.Id, out var copy ) && copy.FireworksSpent, "saved" );
		restored.PlaceObjectEffects( show.Id, (40, 40) );
		restored.PlaceObjectEffects( camera.Id, (20, 20) );
		Assert.AreEqual( default, restored.Litter.Environment.At( 40, 40 ), "a spent show spreads nothing after a load" );
		Assert.AreEqual( (short)20, restored.Litter.Environment.At( 20, 20 ).Security, "the camera's row is back" );
	}

	/// <summary>
	/// [BIN:TPW-EXE:0x004DD0A0] the destructor takes row 7 off a show only while its script variable 0 is below 2: a park whose show already ran (variable 2) has no row to take off, so selling the show
	/// leaves the cells as the saved grid holds them.
	/// </summary>
	[TestMethod]
	public void AShowThatRanBeforeTheSaveTakesNoRowOffWhenItIsSold()
	{
		var park = Park();
		var show = park.RegisterExisting( 2415 );
		park.PlaceObjectEffects( show.Id, (40, 40) );
		// the saved grid of an original park made after 0x004E0B90 took the row off
		park.Litter.Environment.Apply( RegionEffect.Fireworks, 40, 40, add: false );
		Assert.AreEqual( default, park.Litter.Environment.At( 40, 40 ) );
		park.MarkFireworksRemoved( show.Id );
		park.Sell( show.Id );
		Assert.AreEqual( default, park.Litter.Environment.At( 40, 40 ), "no second subtraction" );
		Assert.AreEqual( default, park.Litter.Environment.At( 42, 41 ), "nor around it" );
		Assert.IsFalse( park.FireworksFired( show.Id ), "a spent show never fires again" );
	}

	[TestMethod]
	public void AHeadlessHandymanCleaningADirtyToiletPutsTheCleanRowBack()
	{
		var park = Park();
		park.OpenPark();
		var toilet = park.RegisterExisting( 1402 );
		park.PlaceObjectEffects( toilet.Id, (10, 10) );
		toilet.Repair = 25.5;
		park.UseRelief( toilet.Id, 20 );
		Assert.AreEqual( (short)2, park.Litter.Environment.At( 10, 10 ).Illness, "dirty" );
		EconomyTestData.HireBest( park, StaffType.Handyman );
		var cleaned = false;
		park.EventRaised += item => cleaned |= item.Kind == ParkEventKind.ToiletCleaned;
		for ( var turn = 0; turn < 500 && !cleaned; turn++ )
			EconomyTestData.AdvanceToTurn( park, park.Turn + 1 );
		Assert.IsTrue( cleaned, "the handyman of a headless economy cleans it (FinishJob)" );
		Assert.AreEqual( (short)1, park.Litter.Environment.At( 10, 10 ).Illness, "the clean row is back" );
		park.Sell( toilet.Id );
		Assert.AreEqual( default, park.Litter.Environment.At( 10, 10 ), "and nothing is left after the sale" );
	}
}

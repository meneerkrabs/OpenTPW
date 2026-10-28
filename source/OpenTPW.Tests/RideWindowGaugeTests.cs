using System;
using System.Linq;
using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTPW.Hud;
using OpenTPW.UI.Original;

namespace OpenTPW.Tests;

/// <summary>The buy window's and the ride window's gauge values (tp.exe 0x4abb00, 0x4ade40).</summary>
[TestClass, DoNotParallelize]
public class RideWindowGaugeTests
{
	private RideUpgradeIngressTests fixture = null!;
	private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
	private const string RideData = "UsageInfo.MinSpeed 1\nUsageInfo.MaxSpeed 100\nUsageInfo.MinCapacity 1\nUsageInfo.MaxCapacity 10\nUsageInfo.MinDuration 5\nUsageInfo.MaxDuration 60\nUsageInfo.ExcitementLevel 60\nUpgrades[0].InitSpeed 60\nUpgrades[0].RedLineSpeed 80\nUpgrades[0].RedLineCapacity 5\nUpgrades[0].WearRate 5\nInfo.DurationUnit 1\n";

	[TestInitialize]
	public void Setup() { fixture = new(); fixture.Setup(); }
	[TestCleanup]
	public void Cleanup() => fixture.Cleanup();

	private (ParkHud Hud, Level Level, ParkEconomy Economy, OriginalObject Owner) World( BuildCategory category, string extra )
	{
		object?[] args = { category, null, null, null, null, null, ParkGameMode.FullSimulation, extra };
		var hud = (ParkHud)typeof( RideUpgradeIngressTests ).GetMethod( "Fixture", Private )!.Invoke( fixture, args )!;
		return (hud, (Level)args[1]!, (ParkEconomy)args[2]!, (OriginalObject)args[3]!);
	}
	private static UiContext Context() => (UiContext)typeof( RideUpgradeIngressTests ).GetMethod( "Context", BindingFlags.Static | BindingFlags.NonPublic )!.Invoke( null, null )!;
	private static void Frame( ParkHud hud, UiContext context ) => hud.Update( context, UiInput.Idle( new System.Numerics.Vector2( 5, 5 ) ), false, false, false );
	private static void Drag( ParkHud hud, string id, int value, int minimum ) => ((UiSlider)hud.Screen.Find( id )!).Changed!( value - minimum );

	private static ObjectCatalogEntry Entry( string sam, ObjectCategory category = ObjectCategory.Ride ) =>
		new( "jungle", category, "/synthetic", new ObjectSettings( new[] { new ObjectSettingsFile( sam, "synthetic.sam" ) } ), new ObjectShape( new[] { "*" } ), "/model.MD2", null, "/script.RSE",
			Array.Empty<string>(), Array.Empty<ObjectAnimationFile>(), Array.Empty<string>(), Array.Empty<string>(), new BaseFileSystem( System.IO.Path.GetTempPath() ), null );

	[TestMethod]
	public void GaugeValuesAreTheFieldTimes1024OverAHundredInIntegerSteps()
	{
		// 0x4abb00: (+0x13c << 10) / 100; 0x66d885 clamps the control's value to 0..0x400
		Assert.AreEqual( 716 / 1024f, UiGauge.OfPercent( 70 ), 1e-7f );
		Assert.AreEqual( 51 / 1024f, UiGauge.OfPercent( 5 ), 1e-7f );
		Assert.AreEqual( 0f, UiGauge.OfPercent( 0 ) );
		Assert.AreEqual( 1f, UiGauge.OfPercent( 100 ) );
		Assert.AreEqual( 1f, UiGauge.OfPercent( 250 ), "above 0x400 the gauge keeps 0x400" );
		Assert.AreEqual( 0f, UiGauge.OfPercent( -3 ), "below 0 it keeps 0" );
	}

	[TestMethod]
	public void AGaugesBarIsItsCellLessTenCanvasUnitsTopAndBottom()
	{
		// 0x485e30(gauge, 0, 10) deflates the control's rectangle by 10 units vertically; the bar's model fills it
		var cell = new UiRect( 722, 685, 235, 45 );
		var bar = UiGauge.BarRect( cell, new UiCanvas( 2048, 1536 ) );
		Assert.AreEqual( (722f, 695f, 235f, 25f), (bar.X, bar.Y, bar.Width, bar.Height) );
		var small = UiGauge.BarRect( new UiRect( 361, 342.5f, 117.5f, 22.5f ), new UiCanvas( 1024, 768 ) );
		Assert.AreEqual( (347.5f, 12.5f), (small.Y, small.Height), "half the scale, half the inset" );
	}

	[TestMethod]
	public void TheBuyWindowShowsTheRecordsExcitementRedLineCapacityAndWearRate()
	{
		var entry = Entry( "Info.Id 1100\nInfo.Name synthetic\nUsageInfo.ExcitementLevel 60\nUpgrades[0].InitCapacity 2\nUpgrades[0].RedLineCapacity 5\nUpgrades[0].WearRate 5\nUpgrades[1].WearRate 3\nUpgrades[1].RedLineCapacity 7\n" );
		var item = new BuildItem( "x", 1100, BuildCategory.Rides, 0, 0, null, Array.Empty<string>(), 60, entry );
		var stats = ParkHud.RideBuyStats( item, "0" );
		Assert.AreEqual( 5, stats.Count );
		Assert.AreEqual( 614 / 1024f, stats[1].Gauge!.Value, 1e-7f, "0x1ee: ExcitementLevel 60 -> 614" );
		Assert.AreEqual( "5", stats[2].Text, "0x1f2 shows +0x19c, Upgrades[0].RedLineCapacity, not InitCapacity" );
		Assert.AreEqual( 51 / 1024f, stats[3].Gauge!.Value, 1e-7f, "0x1ef: +0x1b0, Upgrades[0].WearRate 5 -> 51, a short stub (video 1:50)" );
		Assert.AreEqual( 100 / 1024f, stats[4].Gauge!.Value, 1e-7f, "0x1f0 keeps the 100 the window was built with" );
	}

	[TestMethod]
	public void ARideWithoutARedLineStillShowsItsCapacityCell()
	{
		var item = new BuildItem( "x", 1100, BuildCategory.Rides, 0, 0, null, Array.Empty<string>(), null, Entry( "Info.Id 1100\nInfo.Name synthetic\n" ) );
		var stats = ParkHud.RideBuyStats( item, "0" );
		Assert.AreEqual( "0", stats[2].Text, "the cell formats the field with %d whatever it holds" );
		Assert.IsNull( stats[1].Gauge );
	}

	[DataTestMethod]
	[DataRow( 100, 0, 100, 100 )]
	[DataRow( 75, 50, 50, 20 + 20 )]
	[DataRow( 80, 55, 0, 20 )]
	[DataRow( 10, 40, 100, 20 )]
	public void ASideshowsExcitementIs20PlusTheWinChanceTimesTheRootOfThePrizeMargin( int prize, int price, int chance, int expected )
	{
		// 0x4e0560 (type record +0x4ac == 2): 20 - ftol(chance × sqrt(clamp(prize - price, 0, 100)) × 0.1 × -0.8f)
		Assert.AreEqual( expected, RideWindowGauges.SideshowExcitement( prize, price, chance ) );
	}

	[TestMethod]
	public void ARidesExcitementScalesTheBaseByTheClampedSpeedAndDurationRatios()
	{
		Assert.AreEqual( 60, RideWindowGauges.RideExcitement( 60, 75, 75, 10, 10 ), "both ratios 1" );
		Assert.AreEqual( 56, RideWindowGauges.RideExcitement( 60, 100, 75, 5, 10 ), "speed ratio 1.33 clamps to 1.25, duration 0.5 to 0.75: 60 × 0.9375" );
		Assert.AreEqual( 75, RideWindowGauges.RideExcitement( 100, 0, 0, 10, 10 ), "0 / 0 is NaN, which the compare takes to 0.75" );
		Assert.AreEqual( 156, RideWindowGauges.RideExcitement( 100, 200, 100, 30, 10 ), "both clamp to 1.25 and the base is 100: the byte holds 156 (the gauge then keeps 1024)" );
	}

	[TestMethod]
	public void TheProductIsTruncatedInExtendedPrecisionNotRoundedToSingle()
	{
		// 0.9f is a little below 0.9, so 50 × 0.9f is 44.9999994: __ftol after an x87 multiply gives 44 where a float multiply would round to 45
		Assert.AreEqual( 44, RideWindowGauges.RideExcitement( 50, 90, 100, 10, 10 ) );
	}

	[TestMethod]
	public void AReliabilityIs100LessNineTimesTheWindowsWear()
	{
		// 0x4df640: speed 60 = the red line: s = 0.9 × 0.6 = 0.54, (s + 0.1 + 1) × 0.5 = 0.82; capacity 5 of 10: c = 0.45 + 0.1 = 0.55 (the red line 2 is above riders 0 × 0.1);
		// wear = 5 × (0.55 + 0.82) × 0.5 = 3.425, × 9 = 30.8: 100 - 30 = 70
		Assert.AreEqual( 70, RideWindowGauges.Reliability( 100, 10, 60, 2, 5, 60, 5, 0 ) );
		// 20 riders (clamped to the maximum 10): the red line 2 <= 10 × 0.1 = 1 is false, so unchanged; with a red line of 1 the capacity term is (0.55 + 1) × 0.5 = 0.775
		Assert.AreEqual( 100 - (int)(5 * (0.775 + 0.82) * 0.5 * 9), RideWindowGauges.Reliability( 100, 10, 60, 1, 5, 60, 5, 20 ) );
		Assert.AreEqual( 100, RideWindowGauges.Reliability( 100, 10, 60, 2, 0, 60, 5, 0 ), "a ride that does not wear is fully reliable" );
	}

	[TestMethod]
	public void TheRideWindowsExcitementAndReliabilityGaugesFollowItsSliders()
	{
		// 0x4ade40 calls 0x4e0560 and 0x4df640 with the window's own values (+0x2c speed, +0x30 capacity, +0x34 duration), so a drag moves the gauges before the window applies it
		var w = World( BuildCategory.Rides, RideData ); var context = Context(); Frame( w.Hud, context );
		var stats = w.Hud.SelectedInfo!.Stats;
		Assert.AreEqual( UiGauge.OfPercent( 60 ), stats[2].Gauge!.Value, 1e-7f, "speed 60 = InitSpeed 60, duration 10 = InitDuration 10: the level 60 unscaled" );
		Assert.AreEqual( UiGauge.OfPercent( RideWindowGauges.Reliability( 100, 10, 80, 5, 5, 60, 1, 0 ) ), stats[3].Gauge!.Value, 1e-7f );
		Drag( w.Hud, "rideSpeed", 90, 1 ); Frame( w.Hud, context );
		stats = w.Hud.SelectedInfo!.Stats;
		Assert.AreEqual( UiGauge.OfPercent( 75 ), stats[2].Gauge!.Value, 1e-7f, "speed 90 / 60 = 1.5 clamps to 1.25: 60 × 1.25" );
		Assert.AreEqual( UiGauge.OfPercent( RideWindowGauges.Reliability( 100, 10, 80, 5, 5, 90, 1, 0 ) ), stats[3].Gauge!.Value, 1e-7f, "at or above the red line 80 the speed term is (s + 1.1) × 0.5" );
		Assert.AreEqual( 60, w.Owner.Runtime.Settings.Speed, "the window has not applied it yet" );
		Drag( w.Hud, "rideCapacity", 9, 1 ); Frame( w.Hud, context );
		Assert.AreNotEqual( stats[3].Gauge!.Value, w.Hud.SelectedInfo!.Stats[3].Gauge!.Value, "the capacity slider moves the reliability too" );
	}

	[TestMethod]
	public void TheRepairAndLifeGaugesAreTheTruncatedBytesTimes1024Over100()
	{
		// 0x3e19 is the float at +0x44 and 0x3e17 the float at +0x48 through ftol, the byte, (byte << 10) / 100
		var w = World( BuildCategory.Rides, RideData ); var context = Context(); Frame( w.Hud, context );
		Assert.IsTrue( w.Economy.TryGetObject( w.Level.GetEconomyInstance( w.Owner )!.Value, out var state ) );
		state.Repair = 66.9; state.LifeGauge = 33.2;
		var stats = w.Hud.SelectedInfo!.Stats;
		Assert.AreEqual( 675 / 1024f, stats[4].Gauge!.Value, 1e-7f, "66 × 1024 / 100 = 675" );
		Assert.AreEqual( 337 / 1024f, stats[5].Gauge!.Value, 1e-7f, "33 × 1024 / 100 = 337" );
	}

	[TestMethod]
	public void TheSideshowWindowsExcitementGaugeFollowsItsChanceAndPrizeBoxes()
	{
		var w = World( BuildCategory.Sideshows, "UsageInfo.ExcitementLevel 50\n" ); var context = Context(); Frame( w.Hud, context );
		var row = ParkHud.RowKinds( (int)BuildCategory.Sideshows ).ToList().IndexOf( ParkHud.ObjectRowKind.Excitement );
		var pending = w.Hud.PendingSettings;
		Assert.AreEqual( UiGauge.OfPercent( RideWindowGauges.SideshowExcitement( pending.Prize, pending.Price, pending.WinChance ) ), w.Hud.SelectedInfo!.Stats[row].Gauge!.Value, 1e-7f );
		Drag( w.Hud, "sideshowChance", 100, 0 ); ((UiButton)w.Hud.Screen.Find( "prizePlus" )!).Activate(); Frame( w.Hud, context );
		Assert.AreEqual( UiGauge.OfPercent( RideWindowGauges.SideshowExcitement( pending.Prize, pending.Price, 100 ) ), w.Hud.SelectedInfo!.Stats[row].Gauge!.Value, 1e-7f, "the gauge reads the window's chance and prize before it applies them" );
	}

	[TestMethod]
	public void TheLocalHappinessIsTheAverageHappinessByteOfTheGuestsOnTheThreeByThreeCells()
	{
		// 0x4cd880(cell, 1): each guest's float +0x19c truncated to a byte, summed over the cells x-1..x+1, y-1..y+1 and divided by the count
		var guests = new[] { (10, 10, 80.9f), (11, 9, 40f), (9, 11, 61f), (12, 10, 100f), (10, 8, 100f) };
		Assert.AreEqual( (80 + 40 + 61) / 3, ParkHud.LocalHappiness( guests, 10, 10 ), "the guests two cells away do not count; 181 / 3 truncates to 60" );
		Assert.AreEqual( 0, ParkHud.LocalHappiness( guests, 50, 50 ), "nobody around: 0" );
		Assert.AreEqual( 100, ParkHud.LocalHappiness( new[] { (127, 127, 100f), (128, 127, 0f) }, 127, 127 ), "the walk stops at cell 0x7f" );
		Assert.AreEqual( 251, ParkHud.LocalHappiness( new[] { (0, 0, -5f) }, 0, 0 ), "ftol -5 is -5, whose low byte is 251" );
	}

	[TestMethod]
	public void TheLocalHappinessWalkWrapsItsLowColumnOntoTheRowAbove()
	{
		// 0x4cd8fd: the cell id ((y << 7) + x + 1) & 0xFFFF of column x = -1 on row y is y * 128, which is cell (127, y - 1)
		var guests = new[] { (127, 4, 60f), (127, 5, 90f), (127, 6, 30f), (0, 5, 10f), (1, 6, 10f), (127, 3, 100f), (127, 7, 100f) };
		Assert.AreEqual( (100 + 60 + 90 + 10 + 10) / 5, ParkHud.LocalHappiness( guests, 0, 5 ), "the east edge cells of rows 3 to 5 (the row above each walked row) count for an object in column 0 of row 5" );
		Assert.AreEqual( 50, ParkHud.LocalHappiness( new[] { (127, 0, 50f), (127, 1, 70f), (127, 127, 99f) }, 0, 0 ), "row 0: y = -1 gives ids 0xFF7F and up and x = -1 on row 0 id 0, outside the table (nothing counted; and unreachable, no object stands on row 0); x = -1 on row 1 is id 128, cell (127, 0)" );
		Assert.AreEqual( 50, ParkHud.LocalHappiness( new[] { (127, 0, 50f) }, 0, 1 ), "column x = -1 on row 1 reads id 128, which is cell (127, 0)" );
		Assert.AreEqual( (10 + 70 + 20) / 3, ParkHud.LocalHappiness( new[] { (127, 126, 10f), (0, 126, 70f), (127, 125, 20f), (127, 127, 99f) }, 0, 127 ), "rows 126 and 127 (the high row clamps at 0x7f): the wrapped column reads (127, 125) and (127, 126), not (127, 127)" );
	}

	[TestMethod]
	public void ASliderWithoutAnInnerRectangleRunsItsBallAlongItsWholeRectangle()
	{
		// 0x66ac00: attribute 0x40 copies the control's own rectangle into the track; the ride sliders have the layout command 3, the shop's and sideshow's do not
		var shop = World( BuildCategory.Shops, "UsageInfo.SpecialIngredient 4\n" ); var context = Context(); Frame( shop.Hud, context );
		var quality = (UiSlider)shop.Hud.Screen.Find( "shopQuality" )!;
		Assert.AreEqual( quality.Bounds, quality.Track, "the track is the slider's rectangle" );
		var ride = World( BuildCategory.Rides, RideData ); Frame( ride.Hud, Context() );
		var speed = (UiSlider)ride.Hud.Screen.Find( "rideSpeed" )!;
		Assert.AreEqual( new UiRect( 348, 643, 370, 58 ), speed.Track, "the ride speed slider keeps its command 3 rectangle (348,643)-(718,701)" );
	}
}

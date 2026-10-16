using System;
using System.Linq;
using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTPW.Hud;
using OpenTPW.UI.Original;

namespace OpenTPW.Tests;

/// <summary>
/// The object windows' settings controls: the window holds its own values, loaded from the object and applied when the window closes or steps
/// (tp.exe 0x48d200 → the ride apply 0x4af440, the shop apply 0x4b0aa0, the sideshow apply 0x4b20b0) and to every object of the type by apply-all.
/// </summary>
[TestClass, DoNotParallelize]
public class ObjectWindowSettingsTests
{
	private RideUpgradeIngressTests fixture = null!;
	private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
	private const string RideData = "UsageInfo.MinSpeed 1\nUsageInfo.MaxSpeed 100\nUsageInfo.MinCapacity 1\nUsageInfo.MaxCapacity 10\nUsageInfo.MinDuration 5\nUsageInfo.MaxDuration 60\nUpgrades[0].InitSpeed 60\n";

	[TestInitialize]
	public void Setup() { fixture = new(); fixture.Setup(); }
	[TestCleanup]
	public void Cleanup() => fixture.Cleanup();

	private (ParkHud Hud, Level Level, ParkEconomy Economy, OriginalObject Owner, OriginalObject Other) World( BuildCategory category, string extra )
	{
		object?[] args = { category, null, null, null, null, null, ParkGameMode.FullSimulation, extra };
		var hud = (ParkHud)typeof( RideUpgradeIngressTests ).GetMethod( "Fixture", Private )!.Invoke( fixture, args )!;
		return (hud, (Level)args[1]!, (ParkEconomy)args[2]!, (OriginalObject)args[3]!, (OriginalObject)args[4]!);
	}

	private static UiContext Context() => (UiContext)typeof( RideUpgradeIngressTests ).GetMethod( "Context", BindingFlags.Static | BindingFlags.NonPublic )!.Invoke( null, null )!;
	private static void Frame( ParkHud hud, UiContext context ) => hud.Update( context, UiInput.Idle( new System.Numerics.Vector2( 5, 5 ) ), false, false, false );
	private static UiSlider Slider( ParkHud hud, string id ) => (UiSlider)hud.Screen.Find( id )!;
	private static void Drag( UiSlider slider, int value, int minimum ) => slider.Changed!( value - minimum );
	private static ParkObjectState State( ParkEconomy economy, Level level, OriginalObject item ) { Assert.IsTrue( economy.TryGetObject( level.GetEconomyInstance( item )!.Value, out var state ) ); return state; }

	[TestMethod]
	public void TheRideWindowAppliesItsSlidersWhenItCloses()
	{
		var w = World( BuildCategory.Rides, RideData + "Info.DurationUnit 1\n" ); var context = Context(); Frame( w.Hud, context );
		Assert.IsTrue( Slider( w.Hud, "rideSpeed" ).Visible && Slider( w.Hud, "rideCapacity" ).Visible && Slider( w.Hud, "rideDuration" ).Visible );
		Assert.AreEqual( new RideSettingWords( 60, 1, 10 ), w.Hud.PendingSettings.Ride, "the window loads the ride's SPEED, CAPACITY and DURATION words" );
		Assert.AreEqual( "ui42560", ((UiLabel)w.Hud.Screen.Find( "rideSpeedLabel" )!).Text(), "UITEXT 425 \"Speed : \" and the value (1-based 0x1aa)" );
		Drag( Slider( w.Hud, "rideSpeed" ), 90, 1 ); Drag( Slider( w.Hud, "rideCapacity" ), 7, 1 ); Drag( Slider( w.Hud, "rideDuration" ), 40, 5 );
		Frame( w.Hud, context );
		Assert.AreEqual( 60, w.Owner.Runtime.Script!.SpeedBias, "the sliders change only the window until it applies" );
		w.Hud.SetInfoArm( false ); Frame( w.Hud, context );
		Assert.AreEqual( new RideSettingWords( 90, 7, 40 ), w.Owner.Runtime.Settings );
		Assert.AreEqual( (90, 7, 40), (w.Owner.Runtime.Script.SpeedBias, w.Owner.Runtime.GetVariable( RideVariables.VAR_CAPACITY ), w.Owner.Runtime.GetVariable( RideVariables.VAR_DURATION )) );
		Assert.AreEqual( RideVM.DefaultSpeedBias, w.Other.Runtime.Script!.SpeedBias, "the other ride (built without InitSpeed) keeps its speed" );
	}

	[TestMethod]
	public void TheRideWindowHidesTheDurationWithoutAUnitAndClampsLoadedWordsToTheSliderRange()
	{
		var w = World( BuildCategory.Rides, RideData ); var context = Context();
		w.Owner.Runtime.SetSpeed( 150 );
		Frame( w.Hud, context );
		Assert.IsFalse( Slider( w.Hud, "rideDuration" ).Visible, "Info.DurationUnit 0 hides the duration slider (0x4af0dc)" );
		Assert.AreEqual( 100, w.Hud.PendingSettings.Ride.Speed, "the slider keeps a position within MinSpeed..MaxSpeed (0x66b088)" );
		w.Hud.SetInfoArm( false ); Frame( w.Hud, context );
		Assert.AreEqual( 100, w.Owner.Runtime.Settings.Speed, "closing the window applies the clamped position" );
	}

	[TestMethod]
	public void TheStepArrowsApplyTheWindowBeforeShowingTheNextRide()
	{
		var w = World( BuildCategory.Rides, RideData ); var context = Context(); Frame( w.Hud, context );
		Drag( Slider( w.Hud, "rideSpeed" ), 20, 1 );
		w.Hud.CycleSelectedObject( 1 ); Frame( w.Hud, context );
		Assert.AreEqual( 20, w.Owner.Runtime.Settings.Speed );
		Assert.AreSame( w.Other, w.Hud.SelectedObject );
	}

	[TestMethod]
	public void TheShopWindowAppliesQualityIngredientAndPrice()
	{
		var w = World( BuildCategory.Shops, "UsageInfo.SpecialIngredient 4\n" ); var context = Context(); Frame( w.Hud, context );
		var state = State( w.Economy, w.Level, w.Owner ); var other = State( w.Economy, w.Level, w.Other );
		Assert.IsTrue( Slider( w.Hud, "shopQuality" ).Visible && Slider( w.Hud, "shopIngredient" ).Visible );
		Assert.AreEqual( (1, 50), (w.Hud.PendingSettings.QualityStep, w.Hud.PendingSettings.Ingredient), "quality 50 is step 1 (0x4b09c4: / 50)" );
		Assert.AreEqual( 1, ((UiModelImage)w.Hud.Screen.Find( "qualityLamp1" )!).Frame() );
		Drag( Slider( w.Hud, "shopQuality" ), 2, 0 ); Drag( Slider( w.Hud, "shopIngredient" ), 75, 0 );
		((UiButton)w.Hud.Screen.Find( "pricePlus" )!).Activate();
		w.Hud.SetInfoArm( false ); Frame( w.Hud, context );
		Assert.AreEqual( (100, 75, 31), (state.QualityOfGoods, state.IngredientAmount, state.Price), "0x4b0aa0: amount, quality 50 × step, price" );
		Assert.AreEqual( (50, 50, 30), (other.QualityOfGoods, other.IngredientAmount, other.Price) );
	}

	[TestMethod]
	public void ApplyAllWritesTheWindowsSettingsToEveryShopOfTheType()
	{
		var w = World( BuildCategory.Shops, "UsageInfo.SpecialIngredient 4\n" ); var context = Context(); Frame( w.Hud, context );
		Drag( Slider( w.Hud, "shopQuality" ), 0, 0 ); Drag( Slider( w.Hud, "shopIngredient" ), 10, 0 );
		((UiButton)w.Hud.Screen.Find( "applyAll" )!).Activate();
		foreach ( var item in new[] { w.Owner, w.Other } )
			Assert.AreEqual( (0, 10), (State( w.Economy, w.Level, item ).QualityOfGoods, State( w.Economy, w.Level, item ).IngredientAmount) );
	}

	[TestMethod]
	public void AShopWithoutAnIngredientHasNoIngredientSlider()
	{
		var w = World( BuildCategory.Shops, "" ); var context = Context(); Frame( w.Hud, context );
		Assert.IsFalse( Slider( w.Hud, "shopIngredient" ).Visible );
		Assert.IsTrue( Slider( w.Hud, "shopQuality" ).Visible );
	}

	[TestMethod]
	public void TheSideshowWindowAppliesTheWinChanceAndThePrize()
	{
		var w = World( BuildCategory.Sideshows, "" ); var context = Context(); Frame( w.Hud, context );
		var state = State( w.Economy, w.Level, w.Owner ); var prize = state.CostOfGoods;
		Assert.AreEqual( 100 - state.ChanceOfLosingPercent, w.Hud.PendingSettings.WinChance, "the win byte is 100 less the chance of losing (0x4db3a1)" );
		Assert.IsTrue( Slider( w.Hud, "sideshowChance" ).Visible );
		Assert.AreEqual( "ui51", ((UiLabel)w.Hud.Screen.Find( "priceLabel" )!).Text() );
		Drag( Slider( w.Hud, "sideshowChance" ), 30, 0 ); ((UiButton)w.Hud.Screen.Find( "prizePlus" )!).Activate();
		w.Hud.SetInfoArm( false ); Frame( w.Hud, context );
		Assert.AreEqual( (70, prize + 1), (state.ChanceOfLosingPercent, state.CostOfGoods) );
	}

	[TestMethod]
	public void ANonWinnableSideshowHasOnlyAnEntryPrice()
	{
		var w = World( BuildCategory.Sideshows, "UsageInfo.NonWinnableSideshow 1\n" ); var context = Context(); Frame( w.Hud, context );
		var state = State( w.Economy, w.Level, w.Owner ); var chance = state.ChanceOfLosingPercent; var prize = state.CostOfGoods;
		Assert.IsFalse( Slider( w.Hud, "sideshowChance" ).Visible );
		Assert.IsFalse( w.Hud.Screen.Find( "prizeBox" )!.Visible );
		Assert.AreEqual( "ui52", ((UiLabel)w.Hud.Screen.Find( "priceLabel" )!).Text(), "UITEXT 0x34, price of entry" );
		w.Hud.PendingSettings.WinChance = 5; w.Hud.PendingSettings.Prize = 9;
		w.Hud.SetInfoArm( false ); Frame( w.Hud, context );
		Assert.AreEqual( (chance, prize), (state.ChanceOfLosingPercent, state.CostOfGoods), "0x4b20b0 sets only the price for UsageInfo.NonWinnableSideshow" );
	}

	[TestMethod]
	public void SliderLabelsFollowTheTrackTypeAndTheDurationUnit()
	{
		Assert.AreEqual( UIStrings.Capacity, ParkHud.CapacityLabel( 0 ) );
		Assert.AreEqual( 460, (int)ParkHud.CapacityLabel( 2 ), "0x1cd, 1-based" );
		Assert.AreEqual( 462, (int)ParkHud.CapacityLabel( 3 ), "0x1cf, 1-based" );
		Assert.IsNull( ParkHud.DurationLabel( 0, 10 ) );
		Assert.AreEqual( (UIStrings.Duration, UIStrings.Durations, UIStrings.Laps, UIStrings.Cycles, UIStrings.Repetitions),
			(ParkHud.DurationLabel( 1, 1 ), ParkHud.DurationLabel( 1, 2 ), ParkHud.DurationLabel( 2, 2 ), ParkHud.DurationLabel( 3, 2 ), ParkHud.DurationLabel( 4, 2 )) );
		Assert.AreEqual( 0.5f, ParkHud.RedLineFraction( 50, 0, 100 ), 1e-3f, "(50 << 10) / 100 = 512 of 1024" );
		Assert.AreEqual( 620 / 1024f, ParkHud.RedLineFraction( 60, 1, 100 ), "(60 << 10) / 99 = 620: the red line is not offset by the minimum (0x4af0b4)" );
		Assert.AreEqual( 10, ParkHud.SliderPosition( 12, 1, 10 ) );
		Assert.AreEqual( 1, ParkHud.SliderPosition( 12, 1, 0 ), "the minimum is applied last (0x66b088)" );
	}
}

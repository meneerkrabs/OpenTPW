using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTPW.Hud;
using OpenTPW.UI.Original;

namespace OpenTPW.Tests;

/// <summary>
/// The object window's status banner and door (tp.exe 0x485f60, 0x4ad4e0): the banner shows the object's status, none for an open object with nothing to
/// report; the ride window greys the door while the reopening guard refuses a closed ride and hides a condemned ride's buttons; the shop window does neither.
/// </summary>
[TestClass, DoNotParallelize]
public class ObjectWindowStatusTests
{
	private RideUpgradeIngressTests fixture = null!;

	[TestInitialize]
	public void Setup() { fixture = new(); fixture.Setup(); }
	[TestCleanup]
	public void Cleanup() => fixture.Cleanup();

	/// <summary>A staff world that drives nobody: its members' world state is whatever a test sets.</summary>
	private sealed class StillWorld : IStaffWorld
	{
		public void UpdateStaff( ParkEconomy economy, long turn ) { }
	}

	private static void Frame( ParkHud hud, UiContext context ) => hud.Update( context, UiInput.Idle( new System.Numerics.Vector2( 5, 5 ) ), false, false, false );
	private static string Text( UIStrings status ) => $"ui{(int)status}";
	private static ParkObjectState Record( Level level, ParkEconomy economy, OriginalObject item )
	{
		Assert.IsTrue( economy.TryGetObject( level.GetEconomyInstance( item )!.Value, out var state ) );
		return state;
	}

	[TestMethod]
	public void TheRideWindowShowsTheRidesStatusAndGreysTheDoorTheGuardRefuses()
	{
		var hud = fixture.Fixture( BuildCategory.Rides, out var level, out var economy, out var ride, out _, out _ );
		var context = RideUpgradeIngressTests.Context();
		Frame( hud, context );
		var banner = (ParkHud.StatusBanner)hud.Screen.Find( "infoStatus" )!;
		var door = (UiButton)hud.Screen.Find( "openRide" )!;
		Assert.IsFalse( banner.Visible, "an open ride with nothing to report shows no banner" );
		Assert.IsTrue( door.Visible && door.Enabled );
		Assert.AreEqual( new UiRect( 300, 275, 528, 187 ), banner.Bounds, "the ride table's chev control 0x3E25" );
		// closed: CLOSED in grey, and the guard lets it open
		ride.Close();
		Frame( hud, context );
		Assert.IsTrue( banner.Visible );
		Assert.AreEqual( Text( UIStrings.Closed ), banner.Status );
		Assert.AreEqual( ParkHud.StatusColour( UIStrings.Closed ), banner.Colour );
		Assert.IsTrue( door.Enabled, "the guard lets the closed ride open" );
		// a mechanic called while none is hired: the repair status, and the call's maintenance flag makes the guard refuse the door
		var state = Record( level, economy, ride );
		economy.RequestMaintenance( state.Id, true );
		Frame( hud, context );
		Assert.AreEqual( Text( UIStrings.RepairNoMechanics ), banner.Status );
		Assert.AreEqual( ParkHud.StatusColour( UIStrings.RepairNoMechanics ), banner.Colour );
		Assert.IsFalse( door.Enabled, "a closed ride the guard refuses greys its door" );
		// cancelling the call reopens the ride when the guard allows it (0x4dfe30 off), so the banner goes and the door is live again
		economy.RequestMaintenance( state.Id, false );
		Frame( hud, context );
		Assert.IsTrue( ride.IsOpen, "the cancelled call reopened the ride" );
		Assert.IsFalse( banner.Visible );
		Assert.IsTrue( door.Enabled );
	}

	[TestMethod]
	public void TheBannerFollowsAWorldMechanicFromTheCallToTheWork()
	{
		var hud = fixture.Fixture( BuildCategory.Rides, out var level, out var economy, out var ride, out _, out _ );
		var context = RideUpgradeIngressTests.Context();
		var banner = (ParkHud.StatusBanner)hud.Screen.Find( "infoStatus" )!;
		var mechanic = EconomyTestData.HireBest( economy, StaffType.Mechanic );
		economy.StaffWorld = new StillWorld();
		var state = Record( level, economy, ride );
		economy.RequestMaintenance( state.Id, true );
		Frame( hud, context );
		Assert.AreEqual( Text( UIStrings.RepairWaitingForMechanic ), banner.Status, "nobody claimed the call yet" );
		economy.Claim( state, mechanic );
		mechanic.Job = StaffJob.ToRide;
		mechanic.RideId = state.Id;
		Frame( hud, context );
		Assert.AreEqual( Text( UIStrings.RepairMechanicOnWay ), banner.Status );
		// a world mechanic works on the ride in state 13 with the ride as his target (0x5065d0), whatever the headless job fields say
		mechanic.Job = StaffJob.Repairing;
		Frame( hud, context );
		Assert.AreEqual( Text( UIStrings.RepairsInProgress ), banner.Status );
		mechanic.RideId = 0;
		Frame( hud, context );
		Assert.AreEqual( Text( UIStrings.RepairMechanicOnWay ), banner.Status, "repairing another ride is not working on this one" );
	}

	[TestMethod]
	public void TheRideWindowHidesACondemnedRidesButtons()
	{
		var hud = fixture.Fixture( BuildCategory.Rides, out var level, out var economy, out var ride, out _, out _ );
		var context = RideUpgradeIngressTests.Context();
		var state = Record( level, economy, ride );
		state.LifeGauge = 0;
		state.IsBrokenDown = true;
		Frame( hud, context );
		var banner = (ParkHud.StatusBanner)hud.Screen.Find( "infoStatus" )!;
		Assert.AreEqual( Text( UIStrings.Condemned ), banner.Status );
		Assert.AreEqual( ParkHud.StatusColour( UIStrings.Condemned ), banner.Colour );
		foreach ( var id in new[] { "openRide", "callStaff", "rideUpgrade", "moveRide", "editQueue", "editTrack", "rideView" } )
			Assert.IsFalse( hud.Screen.Find( id )?.Visible == true, $"{id} is hidden for a condemned ride (0x4ad4e0)" );
		Assert.IsTrue( hud.Screen.Find( "deleteRide" )!.Visible, "the condemned ride can still be sold" );
	}

	[TestMethod]
	public void TheShopWindowKeepsACondemnedShopsDoor()
	{
		var hud = fixture.Fixture( BuildCategory.Shops, out var level, out var economy, out var shop, out _, out _ );
		var context = RideUpgradeIngressTests.Context();
		var state = Record( level, economy, shop );
		state.LifeGauge = 0;
		state.IsBrokenDown = true;
		Frame( hud, context );
		Assert.AreEqual( Text( UIStrings.Condemned ), ((ParkHud.StatusBanner)hud.Screen.Find( "infoStatus" )!).Status );
		Assert.IsTrue( hud.Screen.Find( "openRide" )!.Visible, "the shop window's refresh (0x4b01d0) tests no status" );
	}

	[TestMethod]
	public void TheShopWindowShowsTheBannerButKeepsItsDoor()
	{
		var hud = fixture.Fixture( BuildCategory.Shops, out var level, out var economy, out var shop, out _, out _ );
		var context = RideUpgradeIngressTests.Context();
		shop.Close();
		Frame( hud, context );
		var banner = (ParkHud.StatusBanner)hud.Screen.Find( "infoStatus" )!;
		Assert.IsTrue( banner.Visible );
		Assert.AreEqual( Text( UIStrings.Closed ), banner.Status );
		Assert.AreEqual( new UiRect( 290, 286, 529, 186 ), banner.Bounds, "the shop table's chev control 0xC06D" );
		// a call makes the guard refuse the shop, and the shop window's door stays live all the same (0x4b01d0 has no door logic)
		economy.RequestMaintenance( Record( level, economy, shop ).Id, true );
		Frame( hud, context );
		Assert.IsFalse( level.Gate!.MayOpen( shop.Runtime ), "the guard refuses the called shop" );
		Assert.AreEqual( Text( UIStrings.RepairNoMechanics ), banner.Status );
		Assert.IsTrue( ((UiButton)hud.Screen.Find( "openRide" )!).Enabled );
	}

	/// <summary>
	/// The toilet's own window (tp.exe <c>0x497640</c>, refresh <c>0x4973A0</c>) enables its door exactly while the reopening guard <c>0x4DF290</c> allows, open or closed,
	/// where the shop window's door ignores the guard.
	/// </summary>
	[TestMethod]
	public void TheToiletWindowGreysItsDoorWhileTheGuardRefuses()
	{
		var hud = fixture.Fixture( BuildCategory.Shops, out var level, out var economy, out var toilet, out _, out _ );
		var context = RideUpgradeIngressTests.Context();
		// the object stands for a small toilet: a relief record in the economy
		var record = economy.RegisterExisting( 1402 );
		( (System.Collections.Generic.Dictionary<OriginalObject, int>)typeof( Level ).GetField( "economyInstances", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic )!.GetValue( level )! )[toilet] = record.Id;
		Assert.IsTrue( economy.Catalog.TryGet( record.InfoId, out var info ) && info.ProvidesRelief );
		var door = (UiButton)hud.Screen.Find( "openRide" )!;
		Frame( hud, context );
		Assert.IsTrue( level.Gate!.MayOpen( toilet.Runtime ), "nothing refuses the toilet yet" );
		Assert.IsTrue( door.Visible && door.Enabled, "the guard allows: the door is live" );
		// a call closes it and makes the guard refuse (+0x64): the door greys, open or closed
		economy.RequestMaintenance( record.Id, true );
		toilet.Close();
		Frame( hud, context );
		Assert.IsFalse( level.Gate.MayOpen( toilet.Runtime ), "the guard refuses the called toilet" );
		Assert.IsFalse( door.Enabled, "the toilet window greys its door (0x4973A0)" );
		toilet.Open();
		Frame( hud, context );
		Assert.IsFalse( door.Enabled, "also while the toilet is open: the test is the guard alone" );
		// cancelling the call lifts the refusal
		economy.RequestMaintenance( record.Id, false );
		Frame( hud, context );
		Assert.IsTrue( door.Enabled );
	}
}

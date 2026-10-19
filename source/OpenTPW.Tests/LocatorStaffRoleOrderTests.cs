using System;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTPW.Hud;
using OpenTPW.UI.Original;

namespace OpenTPW.Tests;

/// <summary>Real radio/employee callbacks over synthetic independent roster, role and global-pool orders.</summary>
[TestClass, DoNotParallelize]
public class LocatorStaffRoleOrderTests
{
	private CameraMode previousCamera = null!;
	private System.Numerics.Matrix4x4 previousView, previousProjection;
	private Level previousLevel = null!;
	private Logger previousLog = null!;
	private (Vector3 Position, Vector3 Forward, Vector3 Front, Vector3 Up) previousListener;

	[TestInitialize]
	public void Setup()
	{
		previousCamera = Camera.Mode;
		previousView = Camera.ViewMatrix;
		previousProjection = Camera.ProjMatrix;
		previousLevel = Level.Current;
		previousLog = Log;
		previousListener = (AudioListener.Position, AudioListener.Forward, AudioListener.Front, AudioListener.Up);
		Camera.Mode = new CameraMode();
		Level.Current = null!;
	}

	[TestCleanup]
	public void Cleanup()
	{
		Camera.Mode = previousCamera;
		Camera.ViewMatrix = previousView;
		Camera.ProjMatrix = previousProjection;
		Level.Current = previousLevel;
		Log = previousLog;
		AudioListener.RestoreCaptured( previousListener.Position, previousListener.Forward );
		AudioListener.RestoreCapturedAxes( previousListener.Front, previousListener.Up );
	}

	private static void Invoke( ParkHud hud, string name ) => typeof( ParkHud ).GetMethod( name, BindingFlags.Instance | BindingFlags.NonPublic )!.Invoke( hud, null );
	private static void Update( ParkHud hud ) => Invoke( hud, "UpdateLocatorPanels" );
	private static void Press( ParkHud hud, StaffType type ) => ((UiButton)hud.Screen.Find( $"locatorRole{LocatorModel.RoleOf( type )}" )!).Clicked!();

	private static ParkHud Fixture( out ParkEconomy economy, out StaffMember[] handymen, out StaffMember guard )
	{
		economy = EconomyTestData.Park();
		var park = economy;
		handymen = Enumerable.Range( 0, 3 ).Select( _ => EconomyTestData.HireBest( park, StaffType.Handyman ) ).ToArray();
		guard = EconomyTestData.HireBest( economy, StaffType.Guard );
		for ( var index = 0; index < handymen.Length; index++ ) handymen[index].ThingId = new[] { 7, 2, 9 }[index];
		guard.ThingId = 14;
		var pool = new ThingPool();
		pool.Restore( new[]
		{
			new ThingSlot( 7, SaveThingModel.Handyman ), new ThingSlot( 90, SaveThingModel.Attraction ),
			new ThingSlot( 14, SaveThingModel.Guard ), new ThingSlot( 2, SaveThingModel.Handyman ), new ThingSlot( 9, SaveThingModel.Handyman )
		} );
		economy.Staff.AttachThings( pool );
		var order = economy.Staff.CaptureRoleOrder();
		order[(int)StaffType.Handyman] = new[] { handymen[1].Id, handymen[2].Id, handymen[0].Id }; // ThingIds 2,9,7, unlike both roster and pool.
		economy.Staff.RestoreRoleOrder( order );
		var hud = StaffWindowTests.Hud( economy );
		Invoke( hud, "BuildLocatorPanels" );
		hud.OpenStaffLocator();
		return hud;
	}

	[TestMethod]
	public void ActualRadiosFollowRoleIdentityForCarriedAndPositionlessStaffWithoutChangingState()
	{
		var hud = Fixture( out var economy, out var staff, out var guard );
		staff[1].State = StaffState.PickedUp;
		staff[1].Job = StaffJob.PickedUp;
		Assert.IsTrue( staff.All( member => member.CellX == null && member.CellY == null ) );
		var roster = economy.Staff.Members.Select( member => member.Id ).ToArray();
		var pool = JsonSerializer.Serialize( economy.Staff.Things!.CaptureSnapshot() );
		var save = JsonSerializer.Serialize( ParkSaveFile.Capture( economy ) );
		var random = economy.World.State;
		var hash = WorldStateHash.Compute( new WorldStateSources { Economy = economy } );
		foreach ( var expected in new[] { staff[1], staff[2], staff[0], staff[1] } )
		{
			Press( hud, StaffType.Handyman ); Update( hud );
			Assert.AreEqual( (expected.Id, true, true), (hud.Locator.ActorId, hud.Locator.StripShown, hud.Locator.Following) );
			Assert.IsTrue( hud.Screen.Find( "locatorName" )!.Visible );
		}
		Press( hud, StaffType.Guard ); Assert.AreEqual( guard.Id, hud.Locator.ActorId );
		Press( hud, StaffType.Guard ); Assert.AreEqual( guard.Id, hud.Locator.ActorId, "singleton wraps" );
		Press( hud, StaffType.Mechanic ); Update( hud );
		Assert.AreEqual( (0, false, false), (hud.Locator.ActorId, hud.Locator.StripShown, hud.Locator.Following) );
		Press( hud, StaffType.Handyman ); Assert.AreEqual( staff[1].Id, hud.Locator.ActorId, "role switch starts from its head" );
		hud.CloseLocator(); hud.OpenStaffLocator(); Press( hud, StaffType.Handyman );
		Assert.AreEqual( staff[1].Id, hud.Locator.ActorId, "reopening preserves the exact owner order" );
		CollectionAssert.AreEqual( roster, economy.Staff.Members.Select( member => member.Id ).ToArray() );
		Assert.AreEqual( pool, JsonSerializer.Serialize( economy.Staff.Things.CaptureSnapshot() ) );
		Assert.AreEqual( save, JsonSerializer.Serialize( ParkSaveFile.Capture( economy ) ) );
		Assert.AreEqual( random, economy.World.State );
		Assert.AreEqual( hash, WorldStateHash.Compute( new WorldStateSources { Economy = economy } ) );
	}

	[TestMethod]
	public void RemovalStaleSelectionAndRepeatedOpeningUseTheSurvivingHead()
	{
		var hud = Fixture( out var economy, out var staff, out _ );
		Press( hud, StaffType.Handyman ); Assert.AreEqual( staff[1].Id, hud.Locator.ActorId );
		economy.Staff.Fire( staff[1].Id );
		Press( hud, StaffType.Handyman ); Assert.AreEqual( staff[2].Id, hud.Locator.ActorId, "removed before update wraps from a stale identity" );
		economy.Staff.Fire( staff[2].Id ); Update( hud );
		Assert.AreEqual( (0, false, false), (hud.Locator.ActorId, hud.Locator.StripShown, hud.Locator.Following) );
		Press( hud, StaffType.Handyman ); Assert.AreEqual( staff[0].Id, hud.Locator.ActorId );
		Press( hud, StaffType.Handyman ); Assert.AreEqual( staff[0].Id, hud.Locator.ActorId );
		economy.Staff.Fire( staff[0].Id );
		hud.CloseLocator(); hud.OpenStaffLocator(); Press( hud, StaffType.Handyman ); Update( hud );
		Assert.AreEqual( (0, false, false), (hud.Locator.ActorId, hud.Locator.StripShown, hud.Locator.Following) );
	}

	[TestMethod]
	public void MissingStaffPositionKeepsIdentityAndNeverMovesTheParkCamera()
	{
		var camera = new ParkCameraMode(); Camera.Mode = camera;
		camera.Focus( new Vector3( 5, 6, 0 ) );
		var hud = Fixture( out _, out var staff, out _ );
		staff[1].State = StaffState.PickedUp;
		var before = camera.Target;
		Press( hud, StaffType.Handyman ); Update( hud ); Update( hud );
		Assert.AreEqual( (staff[1].Id, true, true), (hud.Locator.ActorId, hud.Locator.StripShown, hud.Locator.Following) );
		Assert.AreEqual( before, camera.Target, "no cursor, zero or unavailable position is sent to the camera" );
	}

	[TestMethod]
	public void EmployeeArrowCallbacksKeepGlobalOrderWhenTheLocatorRoleOrderDiffers()
	{
		var hud = Fixture( out var economy, out var staff, out var guard );
		var roleOrder = JsonSerializer.Serialize( economy.Staff.CaptureRoleOrder() );
		hud.OpenEmployeeWindow( staff[0] );
		foreach ( var expected in new[] { guard, staff[1], staff[2], staff[0] } )
		{
			hud.Stack.Top!.Find( "nextEmployee" )!.Activate();
			Assert.AreSame( expected, hud.Employee, "all-staff arrows follow global 7,14,2,9 rather than role 2,9,7" );
		}
		hud.Stack.Top!.Find( "previousEmployee" )!.Activate(); Assert.AreSame( staff[2], hud.Employee );
		hud.CloseEmployeeWindow();
		hud.CloseLocator(); hud.OpenStaffLocator(); Press( hud, StaffType.Handyman );
		Assert.AreEqual( staff[1].Id, hud.Locator.ActorId );
		Assert.AreEqual( roleOrder, JsonSerializer.Serialize( economy.Staff.CaptureRoleOrder() ) );
	}
}

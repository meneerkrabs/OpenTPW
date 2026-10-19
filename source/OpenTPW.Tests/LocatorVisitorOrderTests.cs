using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTPW.Hud;
using OpenTPW.UI.Original;

namespace OpenTPW.Tests;

/// <summary>Actual locator radio callbacks over synthetic live guest/pool data; no game assets or graphics device.</summary>
[TestClass, DoNotParallelize]
public class LocatorVisitorOrderTests
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

	private static void Set( object owner, string name, object? value ) => owner.GetType().GetField( name, BindingFlags.Instance | BindingFlags.NonPublic )!.SetValue( owner, value );
	private static Level LevelOf( ParkHud hud ) => (Level)typeof( ParkHud ).GetField( "level", BindingFlags.Instance | BindingFlags.NonPublic )!.GetValue( hud )!;
	private static void Invoke( ParkHud hud, string name ) => typeof( ParkHud ).GetMethod( name, BindingFlags.Instance | BindingFlags.NonPublic )!.Invoke( hud, null );
	private static void Update( ParkHud hud ) => Invoke( hud, "UpdateLocatorPanels" );
	private static void Press( ParkHud hud, int role = LocatorModel.VisitorRole ) => ((UiButton)hud.Screen.Find( $"locatorRole{role}" )!).Clicked!();
	private static Guest Guest( int id, GuestState state = GuestState.WalkingAround )
	{
		var guest = new Guest( id, 0 ) { State = state };
		guest.Navigation.X = 2 * Fixed16.One;
		guest.Navigation.Y = 3 * Fixed16.One;
		return guest;
	}
	private static void AttachPark( ParkHud hud, float height = 0 )
	{
		var park = (OriginalPark)RuntimeHelpers.GetUninitializedObject( typeof( OriginalPark ) );
		Set( park, "<Heightfield>k__BackingField", new ModelHeightfield( 10, 10, 10, 10, 0, 0, Array.Empty<uint>(), Enumerable.Repeat( height, 121 ).ToArray(), new uint[100] ) );
		Set( LevelOf( hud ), "<OriginalPark>k__BackingField", park );
	}
	private static ParkHud Fixture( out GuestSimulation simulation, out List<Guest> guests, bool park = true )
	{
		var hud = VisitorWorldSelectionTests.Fixture( out simulation, out _ );
		guests = (List<Guest>)simulation.Guests;
		guests.AddRange( new[] { Guest( 7 ), Guest( 2 ), Guest( 9 ) } );
		simulation.Things.Restore( new[]
		{
			new ThingSlot( 2, SaveThingModel.Guest ), new ThingSlot( 90, SaveThingModel.Attraction ), new ThingSlot( 9, SaveThingModel.Guest ),
			new ThingSlot( 14, SaveThingModel.Handyman ), new ThingSlot( 7, SaveThingModel.Guest )
		} );
		Invoke( hud, "BuildLocatorPanels" );
		if ( park ) AttachPark( hud );
		hud.OpenVisitorLocator();
		return hud;
	}
	private static void Cycle( ParkHud hud, params int[] ids )
	{
		foreach ( var id in ids )
		{
			Press( hud );
			Assert.AreEqual( id, hud.Locator.ActorId );
		}
	}

	[TestMethod]
	public void RadioFollowsNonmonotonicRestoredChainAndNeverMutatesSimulationOrSave()
	{
		var hud = Fixture( out var simulation, out var guests );
		var before = JsonSerializer.Serialize( simulation.CaptureSnapshot() );
		var roster = guests.ToArray();
		var pool = JsonSerializer.Serialize( simulation.Things.CaptureSnapshot() );
		var rng = simulation.RandomState;
		Cycle( hud, 2, 9, 7, 2, 9, 7, 2 );
		Assert.AreEqual( 0, LocatorOrder.NextVisitor( guests, simulation.Things, 7 ), "tail delegates wrap to LocatorModel" );
		Assert.AreEqual( 0, LocatorOrder.NextVisitor( guests, simulation.Things, 999 ), "stale current never compares ID magnitudes" );
		hud.Locator.Open( LocatorMode.Visitor, LocatorModel.VisitorRole, 999 ); Cycle( hud, 2 );
		Press( hud, 1 ); Assert.AreEqual( 0, hud.Locator.ActorId ); Cycle( hud, 2 );
		hud.CloseLocator(); Update( hud ); Assert.IsFalse( hud.Screen.Find( "locatorStrip" )!.Visible );
		hud.OpenVisitorLocator(); Cycle( hud, 2 );
		var restored = new ThingPool(); restored.RestoreSnapshot( simulation.Things.CaptureSnapshot() );
		Assert.AreEqual( 2, LocatorOrder.NextVisitor( guests, restored, 0 ) );
		Assert.AreEqual( 9, LocatorOrder.NextVisitor( guests, restored, 2 ) );
		Assert.AreEqual( 7, LocatorOrder.NextVisitor( guests, restored, 9 ) );
		simulation.Things.RestoreSnapshot( restored.CaptureSnapshot() );
		hud.CloseLocator(); hud.OpenVisitorLocator(); Cycle( hud, 2, 9, 7, 2 );
		CollectionAssert.AreEqual( roster, guests );
		Assert.AreEqual( pool, JsonSerializer.Serialize( simulation.Things.CaptureSnapshot() ) );
		Assert.AreEqual( rng, simulation.RandomState );
		Assert.AreEqual( before, JsonSerializer.Serialize( simulation.CaptureSnapshot() ) );
	}

	[TestMethod]
	public void RidingAndGuardHeldVisitorsKeepTheirIdentityStripAndRetainedPosition()
	{
		var camera = new ParkCameraMode(); Camera.Mode = camera;
		var hud = Fixture( out _, out var guests );
		var selected = guests.Single( guest => guest.Id == 2 );
		Cycle( hud, 2 ); Update( hud );
		var target = camera.Target;
		foreach ( var state in new[] { GuestState.Using, GuestState.WalkingAround, GuestState.HeldByGuard, GuestState.ChangingOut } )
		{
			selected.State = state;
			Update( hud );
			Assert.AreEqual( (2, true, true), (hud.Locator.ActorId, hud.Locator.StripShown, hud.Locator.Following) );
			Assert.IsTrue( hud.Screen.Find( "locatorStrip" )!.Visible );
			Assert.AreEqual( target, camera.Target );
		}
		selected.State = GuestState.Using;
		guests.Single( guest => guest.Id == 9 ).State = GuestState.HeldByGuard;
		hud.CloseLocator(); hud.OpenVisitorLocator(); Cycle( hud, 2, 9, 7, 2 );
	}

	[TestMethod]
	public void RemovedCurrentWrapsFromHeadAndAReusedLowerSlotBecomesNewest()
	{
		var hud = Fixture( out var simulation, out var guests );
		Cycle( hud, 2, 9 );
		guests.RemoveAll( guest => guest.Id == 9 ); simulation.Things.QueueDestroy( 9 );
		Cycle( hud, 2 ); // Removed before the next update: a stale current starts at the actual head.
		guests.RemoveAll( guest => guest.Id == 2 ); simulation.Things.QueueDestroy( 2 ); Update( hud );
		Assert.AreEqual( (0, false, false), (hud.Locator.ActorId, hud.Locator.StripShown, hud.Locator.Following) );
		Cycle( hud, 7 );
		simulation.Things.DestroyQueued();
		// Explicitly release another low slot last, so this allocation proves slot reuse rather than ID chronology.
		Assert.IsTrue( simulation.Things.Occupy( 1, SaveThingModel.Attraction ) );
		Assert.IsTrue( simulation.Things.Release( 1 ) );
		var reused = simulation.Things.Allocate( SaveThingModel.Guest ); Assert.AreEqual( 1, reused );
		guests.Add( Guest( reused ) );
		hud.CloseLocator(); hud.OpenVisitorLocator(); Cycle( hud, 1, 7, 1 );
		guests.RemoveAll( guest => guest.Id == 7 ); simulation.Things.QueueDestroy( 7 );
		Cycle( hud, 1, 1 );
		guests.Clear(); Press( hud ); Update( hud );
		Assert.AreEqual( (0, false, false), (hud.Locator.ActorId, hud.Locator.StripShown, hud.Locator.Following) );
	}

	[TestMethod]
	public void UnsupportedFixturesFollowPooledGuestsOnceAndCannotResurrectRejectedClaims()
	{
		var hud = Fixture( out var simulation, out var guests );
		guests.Clear();
		guests.AddRange( new[] { Guest( 40 ), Guest( 2 ), Guest( 9 ), Guest( 7, GuestState.Gone ), Guest( 7 ), Guest( 90 ), Guest( 14 ),
			Guest( 50 ), Guest( 40 ), Guest( 30, GuestState.Gone ), Guest( 30 ), Guest( 0 ), Guest( -1 ), Guest( 80 ) } );
		simulation.Things.QueueDestroy( 9 ); simulation.Things.Hold( 2 );
		Assert.IsTrue( simulation.Things.Occupy( 60, SaveThingModel.Guest ), "a live pool slot without a roster object is not selectable" );
		// 14/90 are wrong models; 9 queued; 2 placeholder; first claim for 7/30 Gone; 80/40/50 pool-free.
		Cycle( hud, 40, 50, 80, 40 );
		Assert.IsTrue( simulation.Things.Occupy( 80, SaveThingModel.Guest ) );
		guests.Add( Guest( 80 ) );
		hud.CloseLocator(); hud.OpenVisitorLocator(); Cycle( hud, 80, 40, 50, 80 );
		Assert.AreEqual( 0, LocatorOrder.NextVisitor( guests, null, 30 ), "stale current returns zero even in pool-free fallback" );
		Assert.AreEqual( 2, LocatorOrder.NextVisitor( guests, null, 0 ), "pool-free order remains explicitly ascending" );
		Assert.AreEqual( 9, LocatorOrder.NextVisitor( guests, null, 2 ) );
		Assert.AreEqual( 50, LocatorOrder.NextVisitor( guests, null, 40 ), "duplicate claims appear only once" );
	}

	[DataTestMethod]
	[DataRow( false )]
	[DataRow( true )]
	public void UnavailablePositionNeverMovesCameraAndRetainsTheExistingVisitorLossPolicy( bool invalidHeight )
	{
		var camera = new ParkCameraMode(); Camera.Mode = camera;
		camera.Focus( new Vector3( 5, 6, 0 ) );
		var hud = Fixture( out _, out _, park: false );
		if ( invalidHeight ) AttachPark( hud, float.NaN );
		var before = camera.Target;
		Cycle( hud, 2 ); Assert.AreEqual( before, camera.Target );
		Assert.IsTrue( hud.Locator.Following );
		Update( hud );
		Assert.AreEqual( (0, false, false), (hud.Locator.ActorId, hud.Locator.StripShown, hud.Locator.Following) );
		Assert.AreEqual( before, camera.Target );
	}

	[TestMethod]
	public void NonParkCameraDoesNotPreventSelectionOrStealTheCameraMode()
	{
		var mode = new CameraMode(); Camera.Mode = mode;
		var hud = Fixture( out _, out _ ); Cycle( hud, 2 ); Update( hud );
		Assert.AreSame( mode, Camera.Mode );
		Assert.AreEqual( (2, true, true), (hud.Locator.ActorId, hud.Locator.StripShown, hud.Locator.Following) );
	}
}

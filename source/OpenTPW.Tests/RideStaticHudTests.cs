using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTPW.Hud;
using OpenTPW.UI.Original;
using N = System.Numerics;

namespace OpenTPW.Tests;

/// <summary>Real HUD input/screen/Draw paths over synthetic CPU fixtures; no original assets or GPU are needed.</summary>
[TestClass, DoNotParallelize]
public class RideStaticHudTests
{
	private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
	private CameraMode previousCamera = null!;
	private N.Matrix4x4 previousView, previousProjection;
	private GameEdition previousEdition;
	private Action restoreInput = null!;
	private static void Set( object target, string field, object? value ) => target.GetType().GetField( field, Private )!.SetValue( target, value );
	private sealed class Clock
	{
		public double Now;
		public int Samples;
		public double Read() { Samples++; return Now; }
	}
	private sealed record World( ParkHud Hud, Level Level, GuestSimulation Guests, PrototypeRide Ride, UiContext Context, Clock Clock );
	private sealed class EmptyCatalog : IBuildCatalog
	{
		public IReadOnlyList<BuildItem> GetItems( BuildCategory category ) => Array.Empty<BuildItem>();
	}

	[TestInitialize]
	public void Setup()
	{
		previousCamera = Camera.Mode; previousView = Camera.ViewMatrix; previousProjection = Camera.ProjMatrix; previousEdition = GameEditions.Current;
		var keyboard = Input.Keyboard; var down = Input.KeysDown; var last = Input.LastKeysDown; var text = Input.TextEntryActive;
		restoreInput = () => { Input.Keyboard = keyboard; Input.UpdateButtons(); Input.KeysDown = down; Input.LastKeysDown = last; Input.TextEntryActive = text; };
		Input.Keyboard = new(); Input.KeysDown = new(); Input.LastKeysDown = new(); Input.TextEntryActive = false; Input.UpdateButtons();
		GameEditions.Select( GameEdition.ThemeParkWorld ); Camera.Mode = new ParkCameraMode(); Camera.ViewMatrix = Camera.ProjMatrix = N.Matrix4x4.Identity;
	}
	[TestCleanup]
	public void Cleanup()
	{
		restoreInput(); Camera.Mode = previousCamera; Camera.ViewMatrix = previousView; Camera.ProjMatrix = previousProjection; GameEditions.Select( previousEdition );
	}
	private static World Create( bool fullHud = true )
	{
		var fixture = VisitorWorldSelectionTests.Fixture( out var guests, out _ );
		var level = (Level)typeof( ParkHud ).GetField( "level", Private )!.GetValue( fixture )!;
		var park = (OriginalPark)RuntimeHelpers.GetUninitializedObject( typeof( OriginalPark ) );
		Set( park, "<Heightfield>k__BackingField", new ModelHeightfield( 10, 10, 10, 10, 0, 0, Array.Empty<uint>(), new float[121], new uint[100] ) );
		Set( level, "<OriginalPark>k__BackingField", park );
		var ride = (PrototypeRide)RuntimeHelpers.GetUninitializedObject( typeof( PrototypeRide ) ); Set( level, "<PlacedRide>k__BackingField", ride );
		var hud = fullHud ? new ParkHud( level, OriginalUiTests.FakeStrings(), new NoEconomyStatus(), new EmptyCatalog(), new HudHost() ) : fixture;
		var clock = new Clock(); hud.RideStaticClock = clock.Read;
		return new( hud, level, guests, ride, Context(), clock );
	}
	private static UiContext Context()
	{
		var font = new FontAtlas( FontAtlasTests.CreateFont( 8, new FontAtlasTests.SyntheticGlyph( 'A', 2, 2, 0, 0, 3 ) ) );
		return new( OriginalUiTests.FakeStrings(), new UiFonts( _ => font ), new UiModels( name => throw new FileNotFoundException( name ) ) )
			{ Canvas = new UiCanvas( 320, 240 ), PopupHelp = false, ResolveTexture = _ => null };
	}
	private static void Update( World w, double now, UiInput? input = null )
	{
		w.Clock.Now = now;
		w.Hud.Update( w.Context, input ?? UiInput.Idle( new N.Vector2( -1 ) ), false, false, false );
	}
	private static UiVertex[] Draw( World w, bool overlayOnly = false )
	{
		w.Context.Batch.Clear();
		if ( overlayOnly ) w.Hud.Stack.Top!.DrawOverlay!( w.Context ); else w.Hud.Draw( w.Context );
		return w.Context.Batch.Draws.SelectMany( draw => draw.Vertices ).ToArray();
	}
	private static EyeModeStaticTransition State( ParkHud hud ) => (EyeModeStaticTransition)typeof( ParkHud ).GetField( "rideStatic", Private )!.GetValue( hud )!;

	[TestMethod]
	public void ShortcutEntryAndScreenBackObserveTheFinalModeExactlyOnce()
	{
		// Reuse the existing asset-free selection fixture: the real update and ride-screen callbacks need no object-info art/runtime.
		var w = Create( fullHud: false ); w.Hud.SelectObject( w.Ride );
		Input.KeysDown.Add( InputButton.RideView ); Update( w, 100 );
		Assert.IsNotNull( w.Level.RideView ); Assert.IsTrue( w.Hud.RideScreenOpen ); Assert.IsTrue( w.Hud.RideStaticActive );
		Assert.AreEqual( .3f, w.Hud.RideStaticLeft ); Assert.AreEqual( 1, w.Clock.Samples );
		Input.KeysDown.Clear(); Update( w, 200 ); Assert.AreEqual( .2f, w.Hud.RideStaticLeft );
		Update( w, 250, UiInput.Key( UiKeys.Back ) );
		Assert.IsNull( w.Level.RideView ); Assert.IsFalse( w.Hud.RideScreenOpen ); Assert.AreEqual( "hud", w.Hud.Stack.Top!.Name );
		Assert.AreEqual( .3f, w.Hud.RideStaticLeft, "the screen's callback exits between the two screen synchronizations" );
		Assert.AreEqual( 3, w.Clock.Samples, "screen sync at both ends never charges the timer twice" );
	}

	[TestMethod]
	public void ArmingIsInactiveAndWalkRideWalkChangesKeepOneBooleanTransition()
	{
		var w = Create(); w.Hud.ArmCamcorder(); Update( w, 0 );
		Assert.IsTrue( w.Hud.CamcorderArmed ); Assert.IsFalse( w.Hud.RideStaticActive );
		// Complete the armed action through the real click path with an identity pick transform.
		Update( w, 100, new UiInput( new N.Vector2( 160, 120 ), true, true, false, false, UiKeys.None ) );
		Assert.IsNotNull( w.Level.FreeWalk ); Assert.IsFalse( w.Hud.CamcorderArmed ); Assert.AreEqual( .3f, w.Hud.RideStaticLeft );
		var walk = w.Level.FreeWalk;
		Assert.IsTrue( w.Level.EnterRideView( w.Ride ) ); Update( w, 150 );
		Assert.IsTrue( w.Hud.RideScreenOpen ); Assert.AreEqual( .25f, w.Hud.RideStaticLeft, "walk -> ride stays true" );
		Update( w, 200, UiInput.Key( UiKeys.Back ) );
		Assert.AreSame( walk, Camera.Mode ); Assert.IsFalse( w.Hud.RideScreenOpen ); Assert.AreEqual( .2f, w.Hud.RideStaticLeft, "ride -> walk stays true" );
		Update( w, 250, UiInput.Key( UiKeys.Back ) );
		Assert.IsNull( w.Level.FreeWalk ); Assert.AreEqual( .3f, w.Hud.RideStaticLeft, "walk -> park is an edge" );
	}

	[TestMethod]
	public void UnobservedLeaveReentryAndStaleOwnersDoNotManufactureEdges()
	{
		var w = Create(); Assert.IsTrue( w.Level.EnterRideView( w.Ride ) ); Update( w, 100 );
		w.Level.ExitRideView(); Assert.IsTrue( w.Level.EnterRideView( w.Ride ) ); Update( w, 200 );
		Assert.AreEqual( .2f, w.Hud.RideStaticLeft, "intermediate park mode was never observed" );
		Camera.Mode = new ParkCameraMode(); Update( w, 250 );
		Assert.IsNotNull( w.Level.RideView ); Assert.AreEqual( .3f, w.Hud.RideStaticLeft, "a retained owner is not the current camera" );
		w.Level.ExitRideView(); Update( w, 300 ); Assert.AreEqual( .25f, w.Hud.RideStaticLeft, "cleaning up the retired owner is not another edge" );
	}

	[TestMethod]
	public void PausedZeroDeltaUpdateStillChargesWallTimeAfterLatchingTheLastFrame()
	{
		var w = Create(); Assert.IsTrue( w.Level.EnterRideView( w.Ride ) ); Update( w, 100 );
		w.Hud.Stack.Push( new UiScreen( "modal" ) ); Assert.IsTrue( w.Hud.Paused ); w.Context.Delta = 0;
		Update( w, 5100 );
		Assert.IsTrue( w.Hud.RideStaticActive ); Assert.AreEqual( -4.7f, w.Hud.RideStaticLeft );
		Assert.IsTrue( Draw( w ).Length > 0 );
		Update( w, 5100 ); Assert.IsFalse( w.Hud.RideStaticActive ); Assert.AreEqual( -4.7f, w.Hud.RideStaticLeft );
	}

	[TestMethod]
	public void RepeatedActualDrawingPreservesPreparedNoiseTimerAndAuthoritativeState()
	{
		var w = Create(); Assert.IsTrue( w.Level.EnterRideView( w.Ride ) ); w.Context.Time = 1; Update( w, 100 );
		Update( w, 1100 ); Assert.IsTrue( w.Hud.RideStaticActive ); Assert.IsTrue( w.Hud.RideStaticLeft < 0 );
		var timer = State( w.Hud ); var samples = w.Clock.Samples;
		var snapshot = JsonSerializer.Serialize( w.Guests.CaptureSnapshot() ); var random = w.Guests.RandomState;
		var hash = WorldStateHash.Compute( new WorldStateSources { Guests = w.Guests } );
		var first = Draw( w ); Assert.IsTrue( first.Length > 0 ); CollectionAssert.AreEqual( first, Draw( w ) );
		var staticOnly = Draw( w, true ); var expected = Context(); ParkHud.DrawStatic( expected, 30 );
		CollectionAssert.AreEqual( expected.Batch.Draws.SelectMany( draw => draw.Vertices ).ToArray(), staticOnly );
		w.Context.Time = 20; w.Clock.Now = 9000;
		CollectionAssert.AreEqual( staticOnly, Draw( w, true ), "even another draw time cannot select a different prepared noise image" );
		Assert.AreEqual( timer, State( w.Hud ) ); Assert.AreEqual( samples, w.Clock.Samples, "Draw never samples time" );
		Assert.AreEqual( snapshot, JsonSerializer.Serialize( w.Guests.CaptureSnapshot() ) ); Assert.AreEqual( random, w.Guests.RandomState );
		Assert.AreEqual( hash, WorldStateHash.Compute( new WorldStateSources { Guests = w.Guests } ) );
		Update( w, 9000 ); Assert.IsFalse( w.Hud.RideStaticActive ); Assert.AreEqual( 0, Draw( w, true ).Length );
	}

	[TestMethod]
	public void FinaleCleanupSkipsObservationAndNewHudSnapshotsItsCurrentCamera()
	{
		var w = Create(); Assert.IsTrue( w.Level.EnterRideView( w.Ride ) ); Update( w, 100 );
		var replacement = new ParkHud( w.Level, OriginalUiTests.FakeStrings(), new NoEconomyStatus(), new EmptyCatalog(), new HudHost() );
		replacement.RideStaticClock = w.Clock.Read; replacement.Update( w.Context, UiInput.Idle( new N.Vector2( -1 ) ), false, false, false );
		Assert.IsFalse( replacement.RideStaticActive, "creating a HUD in an active camera does not invent entry" );
		Assert.AreEqual( .3f, w.Hud.RideStaticLeft, "the other HUD's preparation cannot consume this HUD's budget" );
		w.Level.ExitRideView();
		var finale = new UiScreen( "researchFinale" ) { HidesBelow = true }; Set( w.Hud, "finaleScreen", finale ); w.Hud.Stack.Push( finale );
		var samples = w.Clock.Samples; Update( w, 5000 );
		Assert.IsFalse( w.Hud.RideStaticActive ); Assert.AreEqual( .3f, w.Hud.RideStaticLeft ); Assert.AreEqual( samples, w.Clock.Samples );
		Update( w, 5100 ); Assert.IsTrue( w.Hud.RideStaticActive ); Assert.AreEqual( .3f, w.Hud.RideStaticLeft, "the newly observed exit resets instead of charging skipped time" );
	}
}

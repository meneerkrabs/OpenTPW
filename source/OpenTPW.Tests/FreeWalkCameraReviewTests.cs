using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTPW.Hud;
using OpenTPW.UI.Original;
using Veldrid;
using N = System.Numerics;

namespace OpenTPW.Tests;

[TestClass]
[DoNotParallelize]
public class FreeWalkCameraReviewTests
{
	private static CameraControlInput Frame() => new() { HasFocus = true, LogicalSize = new Point2( 1000, 1000 ), MousePosition = new Vector2( 500, 500 ), DeltaSeconds = .1f, RightButton = true };
	private static ModelHeightfield Surface( bool hole = false ) => new( 2, 2, 10, 10, 0, 100, Array.Empty<uint>(),
		new float[] { 0, 10, 100, 20, 40, 100, 100, 100, 100 }, new uint[] { hole ? ModelHeightfield.HoleCellWord : 0, 0, 0, 0 } );
	private static void Set( object item, string name, object value ) => item.GetType().GetField( name, BindingFlags.Instance | BindingFlags.NonPublic )!.SetValue( item, value );

	[TestMethod]
	public void NonplanarTerrainSamplesRawCornersAndRefusesWalkingIntoAHole()
	{
		var field = Surface();
		Assert.IsTrue( FreeWalkCameraMode.TryGround( field, -1.5f, -.5f, out var ground ) );
		Assert.AreEqual( -1.5f, ground.X, .0001f );
		Assert.AreEqual( -.5f, ground.Y, .0001f );
		Assert.AreEqual( 4f, ground.Z, .0001f, "The cell's ground triangle (ModelHeightfield.GetHeight, 0x452560) gives 20 model units, engine Z 4." );
		var camera = new FreeWalkCameraMode( field, ground, Vector3.Forward );
		Assert.AreEqual( 5.6f, camera.Position.Z, .0001f );
		Assert.IsFalse( FreeWalkCameraMode.TryGround( Surface( true ), -1.5f, -.5f, out _ ) );
		var hole = Surface( true );
		Assert.IsTrue( FreeWalkCameraMode.TryGround( hole, .1f, -.5f, out ground ) );
		camera = new FreeWalkCameraMode( hole, ground, -Vector3.Forward );
		var before = camera.Position;
		camera.Update( Frame() with { Forward = 1 } );
		Assert.AreEqual( before, camera.Position, "Crossing from the adjacent surface into the hole is refused." );
	}

	[DataTestMethod]
	[DataRow( "mouse" )]
	[DataRow( "text" )]
	[DataRow( "modal" )]
	[DataRow( "focus" )]
	public void CapturedLookResetsDragBeforeAStillHeldButtonCanLookAgain( string gate )
	{
		var field = Surface();
		FreeWalkCameraMode.TryGround( field, 0, 0, out var ground );
		var camera = new FreeWalkCameraMode( field, ground, Vector3.Forward );
		camera.Update( Frame() );
		var moving = Frame() with { MouseDelta = new Vector2( -100, 100 ) };
		var blocked = moving with { CaptureMouse = gate == "mouse", TextEntry = gate == "text", Modal = gate == "modal", HasFocus = gate != "focus" };
		var pose = camera.Rotation;
		camera.Update( blocked );
		Assert.AreEqual( pose, camera.Rotation );
		camera.Update( moving );
		Assert.AreEqual( pose, camera.Rotation, "Clearing capture cannot reuse a stale drag on this frame." );
		camera.Update( moving );
		Assert.AreNotEqual( pose, camera.Rotation );
		Assert.IsTrue( camera.Rotation.Forward.Y < 0 );
		Assert.IsTrue( camera.Rotation.Forward.Z > 0 );
	}

	[TestMethod]
	public void RawFrameSnapshotStaysStableUntilRebuildAndDoesNotWidenBindings()
	{
		using var state = new Globals();
		Input.TextEntryActive = true;
		Input.Keyboard.KeysDown.Add( Key.C );
		Input.UpdateButtons();
		Input.Keyboard.KeysDown.Clear();
		Assert.IsTrue( Input.IsFrameKeyDown( Key.C ), "The current frame remains a snapshot despite later keyboard mutation." );
		Assert.IsFalse( Input.Down( InputButton.CamcorderMode ) );
		Input.UpdateButtons();
		Assert.IsFalse( Input.IsFrameKeyDown( Key.C ) );
		Input.TextEntryActive = false;
		using ( Input.InjectKeyDown( Key.ControlRight, Key.C ) ) Input.UpdateButtons();
		Assert.IsTrue( Input.Down( InputButton.CamcorderMode ) );
		Assert.IsFalse( Input.Down( InputButton.ClosePark ), "Right Ctrl does not widen the existing LeftCtrl-only close binding." );
		Input.UpdateButtons();
		using ( Input.InjectKeyDown( Key.ControlLeft, Key.C ) ) Input.UpdateButtons();
		Assert.IsTrue( Input.Down( InputButton.ClosePark ) );
		Input.UpdateButtons();
		Assert.IsFalse( Input.IsFrameKeyDown( Key.C ) );
	}

	[DataTestMethod]
	[DataRow( false )]
	[DataRow( true )]
	public void ActualControlCClosesTheOwningParkAndRespectsReadOnlyWithoutArming( bool readOnly )
	{
		using var state = new Globals();
		var hud = Fixture( out var level, out var simulation );
		var economy = new ParkEconomy( EconomyTestData.Settings(), EconomyTestData.Catalog(), ParkGameMode.FullSimulation, 7 );
		economy.OpenPark();
		Set( hud, "<Status>k__BackingField", new EconomyParkStatus( () => economy ) );
		CommandTestLevel.AttachPark( level, economy );
		if ( readOnly ) Set( level, "<Visit>k__BackingField", RuntimeHelpers.GetUninitializedObject( typeof( ParkVisitInfo ) ) );
		var hash = Hash( simulation );
		var balance = economy.Balance;
		Input.Keyboard.KeysDown.AddRange( new[] { Key.ControlLeft, Key.C } );
		Input.UpdateButtons();
		hud.Update( Context(), UiInput.Idle( new N.Vector2( 500 ) ), false, false, false );
		Assert.AreEqual( readOnly, economy.IsParkOpen );
		Assert.IsFalse( hud.CamcorderArmed );
		Assert.IsNull( level.FreeWalk );
		Assert.AreEqual( balance, economy.Balance );
		Assert.AreEqual( hash, Hash( simulation ) );
	}

	[TestMethod]
	public void MissedEntryKeepsArmedAndSuccessfulClickAndReturnCannotSelectAVisitor()
	{
		using var state = new Globals();
		var hud = Fixture( out var level, out var simulation );
		var guest = new Guest( 7, 0 ) { State = GuestState.WalkingAround, DrawX = 5, DrawY = 5 };
		guest.Navigation.X = 1 << 16; guest.Navigation.Y = 1 << 16; // the picked visitor must stand near the cursor's terrain cell (0x532bd0)
		Set( simulation, "guests", new List<Guest> { guest } );
		var hash = Hash( simulation );
		Input.Keyboard.KeysDown.Add( Key.C ); Input.UpdateButtons();
		hud.Update( Context(), UiInput.Idle( new N.Vector2( 500, 400 ) ), false, false, false );
		Assert.IsTrue( hud.CamcorderArmed );
		var park = Camera.Mode;
		var miss = new UiInput( new N.Vector2( float.NaN ), true, true, false, false, UiKeys.None );
		hud.Update( Context(), miss, false, false, false );
		Assert.IsTrue( hud.CamcorderArmed ); Assert.AreSame( park, Camera.Mode );
		hud.Update( Context(), UiInput.Idle( new N.Vector2( 500, 400 ) ), false, false, false );
		var click = new UiInput( new N.Vector2( 500, 400 ), true, true, false, false, UiKeys.None );
		Assert.IsTrue( hud.Update( Context(), click, false, false, false ) );
		Assert.IsNotNull( level.FreeWalk ); Assert.AreEqual( 0, hud.SelectedVisitorId );
		Input.Keyboard.KeysDown.Clear(); Input.UpdateButtons();
		hud.Update( Context(), UiInput.Idle( click.Mouse ), false, false, false );
		Input.Keyboard.KeysDown.Add( Key.C ); Input.UpdateButtons();
		hud.Update( Context(), click with { LeftDown = false, LeftPressed = false, LeftReleased = true }, false, false, false );
		Assert.IsNull( level.FreeWalk ); Assert.AreSame( park, Camera.Mode );
		Assert.AreEqual( 0, hud.SelectedVisitorId ); Assert.IsFalse( level.CellTool.IsActive );
		Assert.AreEqual( hash, Hash( simulation ) );
		Input.Keyboard.KeysDown.Clear(); Input.UpdateButtons();
		hud.Update( Context(), UiInput.Idle( click.Mouse ), false, false, false );
		// [BIN:TPW-EXE:0x487013 hover mode 7] a visitor press needs Shift alone (or the visitor locator panel out)
		using ( Input.InjectKeyDown( Key.ShiftLeft ) )
		{
			Input.UpdateButtons();
			hud.Update( Context(), click, false, false, false );
		}
		Assert.AreEqual( guest.Id, hud.InfoVisitor?.Id, "A fresh park press recovers the existing visitor route." );
	}

	private static ulong Hash( GuestSimulation simulation ) { var hash = new StateHasher(); simulation.AddCanonicalState( hash ); return hash.Value; }
	private static ParkHud Fixture( out Level level, out GuestSimulation simulation )
	{
		var hud = VisitorWorldSelectionTests.Fixture( out simulation, out _ );
		level = (Level)typeof( ParkHud ).GetField( "level", BindingFlags.Instance | BindingFlags.NonPublic )!.GetValue( hud )!;
		var park = (OriginalPark)RuntimeHelpers.GetUninitializedObject( typeof( OriginalPark ) );
		Set( park, "<Heightfield>k__BackingField", Surface() ); Set( level, "<OriginalPark>k__BackingField", park );
		return hud;
	}
	private static UiContext Context()
	{
		var font = new FontAtlas( FontAtlasTests.CreateFont( 8, new FontAtlasTests.SyntheticGlyph( 'A', 2, 2, 0, 0, 3 ) ) );
		return new UiContext( OriginalUiTests.FakeStrings(), new UiFonts( _ => font ), new UiModels( name => throw new FileNotFoundException( name ) ) ) { Canvas = new UiCanvas( 1000, 1000 ) };
	}
	private sealed class Globals : IDisposable
	{
		private readonly CameraMode camera = Camera.Mode;
		private readonly N.Matrix4x4 view = Camera.ViewMatrix, projection = Camera.ProjMatrix;
		private readonly GameEdition edition = GameEditions.Current;
		private readonly Input.KeyboardInfo keyboard = Input.Keyboard;
		private readonly List<InputButton> down = Input.KeysDown, last = Input.LastKeysDown;
		private readonly bool text = Input.TextEntryActive;
		private readonly Key[] frame = ((HashSet<Key>)typeof( Input ).GetField( "frameKeys", BindingFlags.Static | BindingFlags.NonPublic )!.GetValue( null )!).ToArray();
		public Globals()
		{
			Log ??= new(); GameEditions.Select( GameEdition.ThemeParkWorld ); Camera.Mode = new ParkCameraMode(); Camera.ViewMatrix = Camera.ProjMatrix = N.Matrix4x4.Identity;
			Input.Keyboard = new(); Input.KeysDown = new(); Input.LastKeysDown = new(); Input.TextEntryActive = false; Input.UpdateButtons();
		}
		public void Dispose()
		{
			Input.UpdateButtons(); Input.Keyboard = keyboard; Input.KeysDown = down; Input.LastKeysDown = last; Input.TextEntryActive = text;
			var snapshot = (HashSet<Key>)typeof( Input ).GetField( "frameKeys", BindingFlags.Static | BindingFlags.NonPublic )!.GetValue( null )!;
			snapshot.Clear(); snapshot.UnionWith( frame );
			Camera.Mode = camera; Camera.ViewMatrix = view; Camera.ProjMatrix = projection; GameEditions.Select( edition );
		}
	}
}

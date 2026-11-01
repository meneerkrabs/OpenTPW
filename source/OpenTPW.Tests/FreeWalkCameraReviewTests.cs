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
	public void NonplanarTerrainSamplesTheCornersTheWayTheOriginalsHeightLookupDoes()
	{
		var field = Surface();
		Assert.IsTrue( FreeWalkCameraMode.TryGround( field, -1.5f, -.5f, out var ground ) );
		Assert.AreEqual( -1.5f, ground.X, .0001f );
		Assert.AreEqual( -.5f, ground.Y, .0001f );
		Assert.AreEqual( 4f, ground.Z, .0001f, "The cell's ground triangle (ModelHeightfield.GetHeight, 0x452560) gives 20 model units, engine Z 4." );
		var camera = new FreeWalkCameraMode( field, ground, Vector3.Forward );
		Assert.AreEqual( 5f, camera.Position.Z, .0001f, "the eye is 5 units above that ground (0x42C71D), 25 units, engine Z 5" );
		Assert.IsFalse( FreeWalkCameraMode.TryGround( Surface( true ), -1.5f, -.5f, out _ ), "the host's entry point refuses a hole" );
	}

	[DataTestMethod]
	[DataRow( "mouse" )]
	[DataRow( "text" )]
	[DataRow( "modal" )]
	[DataRow( "focus" )]
	public void CapturedPointerAndKeysLeaveTheWalkerWhereItStands( string gate )
	{
		var field = Surface();
		FreeWalkCameraMode.TryGround( field, 0, 0, out var ground );
		var camera = new FreeWalkCameraMode( field, ground, Vector3.Forward );
		var pose = (camera.Position, camera.Rotation);
		var held = Frame() with { Zoom = 1, MousePosition = new Vector2( 900, 900 ) };
		camera.Update( held with { CaptureMouse = gate == "mouse", CaptureKeyboard = gate == "mouse", TextEntry = gate == "text", Modal = gate == "modal", HasFocus = gate != "focus", RightButton = false } );
		Assert.AreEqual( pose, (camera.Position, camera.Rotation), "no key or pointer input reaches the walk" );
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

	[DataTestMethod]
	[DataRow( "owned", false, true, true )]
	[DataRow( "unowned", true, false, false )]
	[DataRow( "entranceArea walkway", true, true, true )]
	[DataRow( "water", false, true, false )]
	[DataRow( "path", false, true, true )]
	public void TheCamcorderToolsClickFollowsTheOriginalsCellRules( string cell, bool unowned, bool used, bool walks )
	{
		using var state = new Globals();
		Fixture( out var level, out _ );
		var cells = level.Guests!.Grid.Cells;
		var mouse = new N.Vector2( 500, 400 );
		var viewport = new N.Vector2( 1000, 1000 );
		Assert.IsTrue( level.TryGetGroundHit( mouse, viewport, out var hit ) );
		Assert.IsTrue( OriginalParkPlacement.TryGetCell( level.OriginalPark!.Heightfield, hit.X, hit.Y, out var x, out var y ) );
		// [BIN:TPW-EXE:0x46D0D0 camcorder click] owned (flag 0x40 clear) or flag 0x80 on a type 30 cell; then 0x42AE70 walks on empty ground (0), a path (1), a queue or entrance (3, 9) or a walkway (30) only
		cells.SetFlags( x, y, (unowned ? ParkCellFlags.Unowned : ParkCellFlags.None) | (cell.StartsWith( "entranceArea" ) ? ParkCellFlags.EntranceArea : ParkCellFlags.None) );
		cells.SetType( x, y, cell switch { "water" => ParkCellTypes.Water, "path" => (byte)1, _ when cell.EndsWith( "walkway" ) => ParkCellTypes.FixedWalkway, _ => (byte)0 } );
		Assert.AreEqual( used ? Level.CamcorderClick.Used : Level.CamcorderClick.Refused, level.UseCamcorderTool( mouse, viewport ) );
		Assert.AreEqual( walks, level.FreeWalk != null );
	}

	[TestMethod]
	public void AFenceAtTheCellRefusesTheCamcorderToolsClick()
	{
		using var state = new Globals();
		Fixture( out var level, out _ );
		var cells = level.Guests!.Grid.Cells;
		var mouse = new N.Vector2( 500, 400 );
		var viewport = new N.Vector2( 1000, 1000 );
		Assert.IsTrue( level.TryGetGroundHit( mouse, viewport, out var hit ) );
		Assert.IsTrue( OriginalParkPlacement.TryGetCell( level.OriginalPark!.Heightfield, hit.X, hit.Y, out var x, out var y ) );
		// [BIN:TPW-EXE:0x53AD60 fence post] the cell's structure record of type 0x19 stops the click (0x46D0D0 answers the refusal pointer 8 and keeps the tool)
		cells.Tracks.Set( x, y, new TrackCell { Type = ParkCellTypes.Hoarding } );
		Assert.AreEqual( Level.CamcorderClick.Refused, level.UseCamcorderTool( mouse, viewport ) );
		Assert.IsNull( level.FreeWalk );
	}

	[TestMethod]
	public void TheWalkHidesTheInterfaceLikeTheRide()
	{
		using var state = new Globals();
		Fixture( out var level, out _ );
		Assert.IsFalse( level.HidesInterface );
		Assert.IsTrue( level.EnterFreeWalk( Vector3.Zero ) );
		Assert.IsTrue( level.HidesInterface, "0x42AB20 hides the HUD window and shows the camcorder window for the walk too" );
		level.ExitFreeWalk();
		Assert.IsFalse( level.HidesInterface );
	}

	[DataTestMethod]
	[DataRow( true )]
	[DataRow( false )]
	public void ARightClickLeavesTheWalkOnlyWithRmbCancelAndOnItsRelease( bool pressOnly )
	{
		using var state = new Globals();
		var hud = Fixture( out var level, out _ );
		var options = GameOptions.Current;
		var before = options.RmbCancel;
		try
		{
			options.RmbCancel = true;
			Assert.IsTrue( level.EnterFreeWalk( Vector3.Zero ) );
			var press = new UiInput( new N.Vector2( 500 ), false, false, false, true, UiKeys.None );
			hud.Update( Context(), press, false, false, false );
			Assert.IsNotNull( level.FreeWalk, "the press only walks (the right button held, 0x42B925); 0x488A00 acts on the click message of the release" );
			if ( pressOnly )
				return;
			hud.Update( Context(), press with { RightPressed = false, RightReleased = true, RightClicked = true }, false, false, false );
			Assert.IsNull( level.FreeWalk, "the quick click leaves" );
			Assert.IsTrue( level.EnterFreeWalk( Vector3.Zero ) );
			options.RmbCancel = false;
			hud.Update( Context(), press with { RightPressed = false, RightReleased = true, RightClicked = true }, false, false, false );
			Assert.IsNotNull( level.FreeWalk, "with the option off (0x78D911) the click message is ignored" );
		}
		finally { options.RmbCancel = before; }
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

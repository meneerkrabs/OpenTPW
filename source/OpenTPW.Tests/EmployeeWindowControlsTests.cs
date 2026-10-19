using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTPW.Hud;
using OpenTPW.UI.Original;

namespace OpenTPW.Tests;

[TestClass, DoNotParallelize]
public class EmployeeWindowControlsTests
{
	private bool previousTextEntryActive;
	private Logger previousLog = null!;

	[TestInitialize]
	public void Setup()
	{
		previousTextEntryActive = Input.TextEntryActive;
		previousLog = Log;
		// Direct UiScreen updates bypass GameFlow's per-frame reset of text capture.
		Input.TextEntryActive = false;
	}

	[TestCleanup]
	public void Cleanup()
	{
		Input.TextEntryActive = previousTextEntryActive;
		Log = previousLog;
	}

	private static void Set( object owner, string field, object? value ) => owner.GetType().GetField( field, BindingFlags.Instance | BindingFlags.NonPublic )!.SetValue( owner, value );
	private static Level LevelOf( ParkHud hud ) => (Level)typeof( ParkHud ).GetField( "level", BindingFlags.Instance | BindingFlags.NonPublic )!.GetValue( hud )!;
	private static UiContext Context() => new( OriginalUiTests.FakeStrings(), null!, new UiModels( name => throw new FileNotFoundException( name ) ) ) { Canvas = new UiCanvas( 2048, 1536 ) };
	private static (ParkHud Hud, ParkEconomy Economy, StaffMember Member) Fixture()
	{
		var economy = EconomyTestData.Park();
		var member = EconomyTestData.HireBest( economy, StaffType.Handyman );
		var hud = StaffWindowTests.Hud( economy );
		Set( hud, "lastLocatedStaff", new Dictionary<StaffType, int>() );
		hud.OpenEmployeeWindow( member );
		return (hud, economy, member);
	}
	private static UiScreen Rename( ParkHud hud )
	{
		hud.Stack.Top!.Find( "title" )!.Activate();
		Assert.AreEqual( "employeeRename", hud.Stack.Top!.Name );
		return hud.Stack.Top;
	}
	private static StaffMember Member( int id, int thing, StaffType type = StaffType.Handyman ) => new() { Id = id, ThingId = thing, Type = type, NameIndex = 0, Grade = 1 };

	[DataTestMethod]
	[DataRow( false )]
	[DataRow( true )]
	public void FixtureCleanupRestoresTextCaptureWithARenameDialogStillOpen( bool initiallyCaptured )
	{
		var before = Input.TextEntryActive;
		try
		{
			Input.TextEntryActive = initiallyCaptured;
			var nested = new EmployeeWindowControlsTests();
			nested.Setup();
			try
			{
				Assert.IsFalse( Input.TextEntryActive, "each direct-screen test starts without inherited text capture" );
				var (hud, _, _) = Fixture();
				Rename( hud ).Update( Context(), UiInput.Type( "Typing" ) );
				Assert.IsTrue( Input.TextEntryActive, "UiScreen publishes capture while its rename field owns the keys" );
				Assert.AreEqual( "employeeRename", hud.Stack.Top!.Name );
			}
			finally { nested.Cleanup(); }
			Assert.AreEqual( initiallyCaptured, Input.TextEntryActive, "cleanup restores rather than unconditionally clearing the caller's state" );
		}
		finally { Input.TextEntryActive = before; }
	}

	[TestMethod]
	public void ArrowsUseNonmonotonicPoolOrderAndResumeWithoutReorderingTheRoster()
	{
		var members = new[] { Member( 10, 7 ), Member( 80, 2 ), Member( 4, 9 ) };
		var pool = new ThingPool();
		pool.Restore( new[] { new ThingSlot( 7, SaveThingModel.Handyman ), new ThingSlot( 3, SaveThingModel.Guest ),
			new ThingSlot( 2, SaveThingModel.Handyman ), new ThingSlot( 9, SaveThingModel.Handyman ) } );
		var resume = new ThingPool(); resume.RestoreSnapshot( pool.CaptureSnapshot() );
		foreach ( var things in new[] { pool, resume } )
		{
			Assert.AreSame( members[1], EmployeeWindowRules.Cycle( members, 10, true, things ) );
			Assert.AreSame( members[2], EmployeeWindowRules.Cycle( members, 80, true, things ) );
			Assert.AreSame( members[0], EmployeeWindowRules.Cycle( members, 4, true, things ) );
			Assert.AreSame( members[2], EmployeeWindowRules.Cycle( members, 10, false, things ) );
			Assert.AreSame( members[0], EmployeeWindowRules.Cycle( members, 80, false, things ) );
			CollectionAssert.AreEqual( new[] { 10, 80, 4 }, members.Select( member => member.Id ).ToArray() );
			CollectionAssert.AreEqual( new[] { 7, 3, 2, 9 }, things.UsedNewestFirst().Select( slot => slot.Id ).ToArray() );
		}
	}

	[TestMethod]
	public void PoolNavigationOmitsQueuedAndPlaceholderSlotsAndFallsBackOnlyForUnsupportedMembers()
	{
		var pool = new ThingPool();
		pool.Restore( Enumerable.Range( 1, 6 ).Select( id => new ThingSlot( id, SaveThingModel.Handyman ) ) );
		pool.QueueDestroy( 2 ); pool.Hold( 3 );
		var members = new[] { Member( 10, 1 ), Member( 20, 2 ), Member( 30, 3 ), Member( 40, 0 ), Member( 50, 5, StaffType.Guard ) };
		Assert.AreSame( members[4], EmployeeWindowRules.Cycle( members, 10, true, pool ), "wrong-role/unpooled claims use reverse-roster fallback, never slot ids" );
		Assert.AreSame( members[3], EmployeeWindowRules.Cycle( members, 50, true, pool ) );
		Assert.AreSame( members[0], EmployeeWindowRules.Cycle( members, 40, true, pool ) );
		Assert.AreSame( members[0], EmployeeWindowRules.Cycle( new[] { members[0] }, 10, true, pool ) );
		Assert.IsNull( EmployeeWindowRules.Cycle( Array.Empty<StaffMember>(), 0, true, pool ) );
		Assert.IsNull( EmployeeWindowRules.Cycle( new[] { members[1], members[2] }, 20, true, pool ) );
		Assert.AreSame( members[4], EmployeeWindowRules.Cycle( members, 10, true ), "without a pool retain reverse roster order" );
	}

	[TestMethod]
	public void ReusingALowerThingSlotMakesTheNewHireFirst()
	{
		var pool = new ThingPool();
		var first = Member( 40, pool.Allocate( SaveThingModel.Handyman ) );
		var second = Member( 10, pool.Allocate( SaveThingModel.Handyman ) );
		pool.QueueDestroy( first.ThingId ); pool.DestroyQueued();
		var replacement = Member( 3, pool.Allocate( SaveThingModel.Handyman ) );
		Assert.AreEqual( 1, replacement.ThingId );
		var members = new[] { second, replacement };
		Assert.AreSame( replacement, EmployeeWindowRules.Cycle( members, 0, true, pool ) );
		Assert.AreSame( second, EmployeeWindowRules.Cycle( members, replacement.Id, true, pool ) );
	}

	[TestMethod]
	public void RenameUsesTheOriginalTableCurrentNameAndOneFocusedSelectedField()
	{
		var (hud, economy, member) = Fixture();
		var title = (UiLabel)hud.Stack.Top!.Find( "title" )!;
		var originalName = title.Text();
		var hash = WorldStateHash.Compute( new WorldStateSources { Economy = economy } );
		var screen = Rename( hud );
		var table = OriginalLayoutTables.EmployeeRenameDialog;
		Assert.AreEqual( 0x7572E8, table.Address );
		Assert.AreEqual( new UiRect( 500, 233, 1016, 597 ), screen.Find( "window" )!.Bounds );
		Assert.AreEqual( "wdialogw", ((UiModelImage)screen.Find( "window" )!).Model );
		Assert.AreEqual( "ui195", ((UiLabel)screen.Find( "title" )!).Text() );
		Assert.AreEqual( table.Control( 0x5DAA ).Rect, screen.Find( "title" )!.Bounds );
		var field = (UiTextField)screen.Find( "name" )!;
		Assert.AreEqual( (originalName, 32, "!frame", true), (field.Text, field.MaximumLength, field.Model, field.AllSelected) );
		Assert.AreSame( field, screen.Focused );
		Assert.AreEqual( table.Control( 0x5DAB ).Rect, field.Bounds );
		foreach ( var (id, model) in new[] { ("apply", "b_okay"), ("cancel", "b_exit") } )
		{
			Assert.AreEqual( model, ((UiButton)screen.Find( id )!).Model );
			Assert.AreEqual( table.Model( model ).Rect, screen.Find( id )!.Bounds );
		}
		title.Activate(); hud.OpenEmployeeRenameDialog();
		Assert.AreEqual( 3, hud.Stack.Screens.Count, "repeated title activation cannot stack child dialogs" );
		Assert.AreEqual( hash, WorldStateHash.Compute( new WorldStateSources { Economy = economy } ) );
		screen.Update( Context(), UiInput.Type( "New name" ) );
		Assert.AreEqual( "New name", field.Text, "first typing replaces the selected name" );
		var identity = (member.Id, member.ThingId, member.NameIndex, member.Variant, member.Grade, member.Happiness, member.Energy);
		screen.Update( Context(), UiInput.Key( UiKeys.Accept ) );
		Assert.AreEqual( "New name", member.Name );
		Assert.AreEqual( "New name", title.Text() );
		Assert.AreEqual( identity, (member.Id, member.ThingId, member.NameIndex, member.Variant, member.Grade, member.Happiness, member.Energy) );
		Assert.AreNotEqual( hash, WorldStateHash.Compute( new WorldStateSources { Economy = economy } ) );
		Assert.AreEqual( "New name", ((UiTextField)Rename( hud ).Find( "name" )!).Text );
	}

	[TestMethod]
	public void PointerTitleAndApplyUseTheScreenRouteAndPersistExactly32Units()
	{
		var (hud, economy, member) = Fixture();
		var context = Context();
		void Click( UiScreen screen, string id )
		{
			var rect = screen.Find( id )!.ScreenRect( context.Canvas );
			var point = new System.Numerics.Vector2( rect.X + rect.Width / 2, rect.Y + rect.Height / 2 );
			screen.Update( context, new UiInput( point, true, true, false, false, UiKeys.None ) );
			screen.Update( context, UiInput.Click( point ) );
		}
		Click( hud.Stack.Top!, "title" );
		Assert.AreEqual( "employeeRename", hud.Stack.Top!.Name );
		var dialog = hud.Stack.Top;
		var name = new string( 'x', 32 );
		dialog.Update( context, UiInput.Type( name + "y" ) );
		Assert.AreEqual( name, ((UiTextField)dialog.Find( "name" )!).Text );
		Click( dialog, "apply" ); Assert.AreEqual( name, member.Name );
		var restored = ParkSaveFile.Restore( ParkSaveFile.Deserialize( Encoding.UTF8.GetBytes( ParkSaveFile.Serialize( economy ) ) ), economy.Settings, economy.Catalog );
		Assert.AreEqual( name, restored.Staff.Members.Single().Name );
		hud.Stack.Top!.Focus( hud.Stack.Top.Find( "title" ) );
		hud.Stack.Top.Update( context, UiInput.Key( UiKeys.Accept ) );
		Assert.AreEqual( "employeeRename", hud.Stack.Top.Name, "the title is keyboard accessible too" );
	}

	[DataTestMethod]
	[DataRow( "cancel" )]
	[DataRow( "escape" )]
	[DataRow( "right" )]
	public void CancelOwnsEvenASimultaneousSubmitAndKeepsTheGameplayHash( string kind )
	{
		var previous = GameOptions.Current;
		try
		{
			GameOptions.Current = new GameOptions { RmbCancel = true };
			var (hud, economy, member) = Fixture();
			var other = EconomyTestData.HireBest( economy, StaffType.Guard );
			var hash = WorldStateHash.Compute( new WorldStateSources { Economy = economy } );
			Assert.IsTrue( hud.CycleEmployee( true ) ); Assert.IsTrue( hud.CycleEmployee( false ) );
			var screen = Rename( hud ); screen.Update( Context(), UiInput.Type( "Discarded" ) );
			if ( kind == "cancel" ) screen.Find( "cancel" )!.Activate();
			else screen.Update( Context(), UiInput.Key( UiKeys.Accept | (kind == "escape" ? UiKeys.Back : UiKeys.None) ) with { RightPressed = kind == "right" } );
			screen.Find( "apply" )!.Activate();
			Assert.AreEqual( "employee", hud.Stack.Top!.Name );
			Assert.IsNull( member.Name ); Assert.IsNull( other.Name );
			Assert.AreEqual( hash, WorldStateHash.Compute( new WorldStateSources { Economy = economy } ) );
		}
		finally { GameOptions.Current = previous; }
	}

	[TestMethod]
	public void ApplyKeepsItsCapturedTargetAfterSelectionChangesAndCannotApplyTwice()
	{
		var (hud, economy, member) = Fixture();
		var other = EconomyTestData.HireBest( economy, StaffType.Guard );
		var screen = Rename( hud );
		screen.Update( Context(), UiInput.Type( "Captured" ) );
		hud.CycleEmployee( true ); Assert.AreSame( other, hud.Employee );
		screen.Find( "apply" )!.Activate();
		Assert.AreEqual( "Captured", member.Name ); Assert.IsNull( other.Name );
		var next = Rename( hud );
		((UiTextField)screen.Find( "name" )!).Text = "Stale"; screen.Find( "apply" )!.Activate();
		Assert.AreSame( next, hud.Stack.Top ); Assert.AreEqual( "Captured", member.Name ); Assert.IsNull( other.Name );
	}

	[DataTestMethod]
	[DataRow( "readonly" )]
	[DataRow( "fired" )]
	[DataRow( "replaced" )]
	[DataRow( "thingId" )]
	[DataRow( "parent" )]
	[DataRow( "removedParent" )]
	public void StaleRenameCallbacksCannotMutateOrRetarget( string change )
	{
		var (hud, economy, member) = Fixture();
		var parent = hud.Stack.Top!;
		var screen = Rename( hud ); screen.Update( Context(), UiInput.Type( "Invalid" ) );
		if ( change == "readonly" ) Set( LevelOf( hud ), "<Visit>k__BackingField", RuntimeHelpers.GetUninitializedObject( typeof( ParkVisitInfo ) ) );
		if ( change == "fired" ) economy.Fire( member.Id );
		if ( change == "replaced" )
		{
			var roster = (List<StaffMember>)economy.Staff.Members; roster.Clear(); roster.Add( Member( member.Id, member.ThingId ) );
		}
		if ( change == "thingId" ) member.ThingId++;
		if ( change == "parent" ) hud.CloseEmployeeWindow();
		if ( change == "removedParent" ) hud.Stack.RemoveWhere( candidate => ReferenceEquals( candidate, parent ) );
		screen.Find( "apply" )!.Activate();
		Assert.IsNull( member.Name ); Assert.IsTrue( economy.Staff.Members.All( item => item.Name == null ) );
		Assert.IsFalse( hud.Stack.Screens.Contains( screen ) );
	}

	[TestMethod]
	public void FiringCancelsTheModalOnItsNextFrameAndReadonlyBlocksTitleOpening()
	{
		var (hud, economy, member) = Fixture();
		var title = hud.Stack.Top!.Find( "title" )!;
		Set( LevelOf( hud ), "<Visit>k__BackingField", RuntimeHelpers.GetUninitializedObject( typeof( ParkVisitInfo ) ) );
		title.Activate(); Assert.AreEqual( "employee", hud.Stack.Top.Name );
		hud.Stack.Top.Update( Context(), UiInput.Idle( new( -1 ) ) ); Assert.IsFalse( title.Enabled );
		Set( LevelOf( hud ), "<Visit>k__BackingField", null );
		hud.Stack.Top.Update( Context(), UiInput.Idle( new( -1 ) ) );
		var screen = Rename( hud ); economy.Fire( member.Id );
		screen.Update( Context(), UiInput.Idle( new( -1 ) ) );
		Assert.IsFalse( hud.EmployeeWindowOpen ); Assert.AreEqual( 1, hud.Stack.Screens.Count );
	}

	[TestMethod]
	public void TextEntryOptInSelectionPreservesDefaultEditingAndUnicodeBoundaries()
	{
		var label = new UiLabel(); Assert.IsFalse( label.Focusable ); label.Activate();
		var field = new UiTextField { Text = "A", MaximumLength = 32 };
		field.Type( "B", 0 ); Assert.AreEqual( "AB", field.Text );
		field.SelectAll(); field.Type( "\0\n\ud800\udc00", 0 ); Assert.AreEqual( "\ud800\udc00", field.Text );
		field.Text = "Keep"; field.SelectAll(); field.Type( "\0\n\ud800", 0 );
		Assert.AreEqual( "Keep", field.Text ); Assert.IsTrue( field.AllSelected );
		field.MaximumLength = 0; field.Type( "A", 0 ); Assert.AreEqual( "Keep", field.Text ); Assert.IsTrue( field.AllSelected );
		field.MaximumLength = 1; field.Type( "😀", 0 ); Assert.AreEqual( "Keep", field.Text ); Assert.IsTrue( field.AllSelected );
		field.MaximumLength = 32; field.Type( "", 1 ); Assert.AreEqual( "", field.Text ); Assert.IsFalse( field.AllSelected );
		field.Type( new string( 'a', 30 ) + "😀X", 0 ); Assert.AreEqual( 32, field.Text.Length ); Assert.IsTrue( field.Text.EndsWith( "😀" ) );
		field.Type( "", 1 ); Assert.AreEqual( 30, field.Text.Length );
		field.Type( "b😀c", 0 ); Assert.AreEqual( new string( 'a', 30 ) + "bc", field.Text, "a pair at unit 32 cannot be split" );
	}

	[DataTestMethod]
	[DataRow( "" )]
	[DataRow( "   " )]
	[DataRow( "Thirty-two UTF-16 units exactly!" )]
	public void ExplicitNamesSurviveSaveAndResume( string name )
	{
		var (hud, economy, member) = Fixture();
		var screen = Rename( hud );
		screen.Update( Context(), UiInput.Type( "", 1 ) ); screen.Update( Context(), UiInput.Type( name ) );
		screen.Find( "apply" )!.Activate(); Assert.AreEqual( name, member.Name );
		var restored = ParkSaveFile.Restore( ParkSaveFile.Deserialize( Encoding.UTF8.GetBytes( ParkSaveFile.Serialize( economy ) ) ), economy.Settings, economy.Catalog );
		Assert.AreEqual( name, restored.Staff.Members.Single().Name );
		Assert.IsFalse( economy.Staff.Rename( member, new string( 'a', 33 ) ) );
		Assert.IsFalse( economy.Staff.Rename( member, "bad\0" ) );
		Assert.IsFalse( economy.Staff.Rename( member, "bad\ud800" ) );
	}
	private static void LocatorFrame( ParkHud hud ) => typeof( ParkHud ).GetMethod( "UpdateLocatorPanels", BindingFlags.Instance | BindingFlags.NonPublic )!.Invoke( hud, null );
	private static void AttachStaffWorld( ParkHud hud, ParkEconomy economy )
	{
		var level = LevelOf( hud );
		var grid = level.Guests!.Grid;
		var world = new StaffWorldSimulation( grid, () => economy.Staff.Members, _ => null );
		Set( level, "<StaffWorld>k__BackingField", world );
		var park = (OriginalPark)RuntimeHelpers.GetUninitializedObject( typeof( OriginalPark ) );
		Set( park, "<Heightfield>k__BackingField", new ModelHeightfield( 10, 10, 10, 10, 0, 0, Array.Empty<uint>(), new float[121], new uint[100] ) );
		Set( level, "<OriginalPark>k__BackingField", park );
		typeof( ParkHud ).GetMethod( "BuildLocatorPanels", BindingFlags.Instance | BindingFlags.NonPublic )!.Invoke( hud, null );
	}

	[DataTestMethod]
	[DataRow( false )]
	[DataRow( true )]
	public void AnUnpositionedEmployeeKeepsTheLocatorUntilItLeavesAndFollowingResumes( bool shortcut )
	{
		var previous = Camera.Mode;
		try
		{
			var camera = new ParkCameraMode(); Camera.Mode = camera;
			var (hud, economy, member) = Fixture();
			member.State = StaffState.PickedUp;
			AttachStaffWorld( hud, economy );
			var oldTarget = camera.Target;
			Set( hud, "locatorCameraTarget", oldTarget + new Vector3( 100, 100, 100 ) );
			hud.Stack.Top!.Update( Context(), UiInput.Idle( new( -1 ) ) );
			Assert.IsTrue( hud.Stack.Top.Find( "locateEmployee" )!.Enabled );
			if ( shortcut ) hud.OpenStaffLocator(); else Assert.IsTrue( hud.LocateEmployee() );
			Assert.IsFalse( hud.EmployeeWindowOpen );
			for ( var frame = 0; frame < 3; frame++ ) LocatorFrame( hud );
			Assert.AreEqual( (member.Id, LocatorModel.RoleOf( member.Type ), true, true), (hud.Locator.ActorId, hud.Locator.Role, hud.Locator.StripShown, hud.Locator.Following) );
			Assert.IsTrue( hud.Screen.Find( "locatorStrip" )!.Visible );
			Assert.AreEqual( oldTarget, camera.Target );
			Assert.IsNull( member.CellX ); Assert.IsNull( member.CellY );
			member.CellX = 2.5f; member.CellY = 3.5f;
			Assert.IsTrue( LevelOf( hud ).TryGetStaffWorldPosition( member.Id, out var position ) );
			LocatorFrame( hud ); Assert.AreEqual( position, camera.Target ); Assert.IsTrue( hud.Locator.Following );
			camera.Focus( position + new Vector3( 5, 0, 0 ) ); LocatorFrame( hud );
			Assert.IsFalse( hud.Locator.Following, "user camera movement stops following" );
			economy.Fire( member.Id ); LocatorFrame( hud );
			Assert.AreEqual( (0, false, false), (hud.Locator.ActorId, hud.Locator.StripShown, hud.Locator.Following) );
		}
		finally { Camera.Mode = previous; }
	}

	[TestMethod]
	public void RepeatedLocateAndSwitchingToUnpositionedStaffClearThePreviousCameraComparison()
	{
		var previous = Camera.Mode;
		try
		{
			var camera = new ParkCameraMode(); Camera.Mode = camera;
			var (hud, economy, member) = Fixture(); AttachStaffWorld( hud, economy );
			member.CellX = 2.5f; member.CellY = 3.5f;
			var placement = new StaffPlacement( () => economy, () => LevelOf( hud ).Guests!.Grid.Cells, (_, _) => false );
			Assert.IsTrue( placement.PickUp( member ) ); placement.SetCursor( 8, 9 );
			Assert.IsTrue( hud.LocateEmployee() ); LocatorFrame( hud );
			Assert.IsTrue( LevelOf( hud ).TryGetStaffWorldPosition( member.Id, out var retained ) );
			Assert.AreEqual( retained, camera.Target, "carry keeps its last world coordinates, never the cursor position" );
			var other = EconomyTestData.HireBest( economy, StaffType.Guard ); other.State = StaffState.PickedUp;
			for ( var attempt = 0; attempt < 2; attempt++ )
			{
				camera.Focus( retained + new Vector3( 10 + attempt, 0, 0 ) );
				hud.OpenEmployeeWindow( other ); Assert.IsTrue( hud.LocateEmployee() ); LocatorFrame( hud );
				Assert.AreEqual( other.Id, hud.Locator.ActorId ); Assert.IsTrue( hud.Locator.Following );
			}
		}
		finally { Camera.Mode = previous; }
	}

	[TestMethod]
	public void VisitorPositionLossStillClearsTheActorAndStaffNamesRefreshEverySurface()
	{
		var previous = Camera.Mode;
		try
		{
			Camera.Mode = new ParkCameraMode();
			var (hud, economy, member) = Fixture(); AttachStaffWorld( hud, economy );
			member.Name = "Imported employee";
			var screen = Rename( hud );
			Assert.AreEqual( "Imported employee", ((UiTextField)screen.Find( "name" )!).Text );
			screen.Update( Context(), UiInput.Type( "Changed employee" ) ); screen.Find( "apply" )!.Activate();
			Assert.AreEqual( "Changed employee", ((UiLabel)hud.Stack.Top!.Find( "title" )!).Text() );
			hud.ShowAllStaffOfEmployee();
			Assert.AreEqual( "Changed employee", typeof( ParkHud ).GetMethod( "ListName", BindingFlags.Instance | BindingFlags.NonPublic )!.Invoke( hud, new object[] { 0 } ) );
			hud.OpenEmployeeWindow( member ); hud.OpenStaffLocator(); LocatorFrame( hud );
			Assert.AreEqual( "Changed employee", ((UiLabel)hud.Screen.Find( "locatorName" )!).Text() );
			var guest = new Guest( 900, 0 ) { State = GuestState.WalkingAround };
			Set( LevelOf( hud ).Guests!, "guests", new List<Guest> { guest } );
			hud.Locator.Open( LocatorMode.Visitor, LocatorModel.VisitorRole, guest.Id ); LocatorFrame( hud );
			Assert.AreEqual( guest.Id, hud.Locator.ActorId );
			Set( LevelOf( hud ), "<OriginalPark>k__BackingField", null ); LocatorFrame( hud );
			Assert.AreEqual( (0, false, false), (hud.Locator.ActorId, hud.Locator.StripShown, hud.Locator.Following) );
		}
		finally { Camera.Mode = previous; }
	}

}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTPW.Hud;
using OpenTPW.UI.Original;

namespace OpenTPW.Tests;

/// <summary>The staff and visitor locator panels (tp.exe 0x4b3f60, 0x4b4280, 0x4b3790, tables 0x756950 / 0x756ad8).</summary>
[TestClass, DoNotParallelize]
public class LocatorPanelTests
{
	private RideUpgradeIngressTests? fixture;
	// a visitor row click plays the visitor click, which logs
	[TestInitialize]
	public void Init() => Log ??= new();
	[TestCleanup]
	public void Cleanup() => fixture?.Cleanup();

	private (ParkHud Hud, UiContext Context, OriginalObject Owner) Hud()
	{
		fixture = new(); fixture.Setup();
		const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
		object?[] args = { BuildCategory.Shops, null, null, null, null, null, ParkGameMode.FullSimulation, "" };
		var hud = (ParkHud)typeof( RideUpgradeIngressTests ).GetMethod( "Fixture", Private )!.Invoke( fixture, args )!;
		var context = (UiContext)typeof( RideUpgradeIngressTests ).GetMethod( "Context", BindingFlags.Static | BindingFlags.NonPublic )!.Invoke( null, null )!;
		return (hud, context, (OriginalObject)args[3]!);
	}

	private static void Frame( ParkHud hud, UiContext context ) => hud.Update( context, UiInput.Idle( new System.Numerics.Vector2( -1 ) ), false, false, false );

	[TestMethod]
	public void TheKeysOpenTheArmPanelsAndTheArmsOtherContentsPutThemAway()
	{
		var (hud, context, owner) = Hud();
		hud.OpenStaffLocator(); Frame( hud, context );
		Assert.IsTrue( hud.Screen.Find( "locatorStaffPanel" )!.Visible && hud.Screen.Find( "locatorRole1" )!.Visible && !hud.Screen.Find( "locatorRole6" )!.Visible );
		Assert.IsFalse( hud.Screen.Find( "locatorStrip" )!.Visible, "no actor yet, no strip" );
		Assert.IsTrue( hud.Screen.Find( "messageClose" )!.Visible, "the arm's red X puts the panel away" );
		((UiButton)hud.Screen.Find( "messageClose" )!).Clicked!(); Frame( hud, context );
		Assert.AreEqual( LocatorMode.None, hud.Locator.Mode ); Assert.IsFalse( hud.Screen.Find( "locatorStaffPanel" )!.Visible );
		hud.OpenVisitorLocator(); Frame( hud, context );
		Assert.IsTrue( hud.Screen.Find( "locatorVisitorPanel" )!.Visible && !hud.Screen.Find( "locatorStaffPanel" )!.Visible );
		Assert.AreEqual( (LocatorModel.VisitorRole, false), (hud.Locator.Role, hud.Locator.RadioChosen) );
		hud.SelectObject( owner ); Frame( hud, context );
		Assert.AreEqual( LocatorMode.None, hud.Locator.Mode, "the info arm takes the arm's slot" );
		hud.OpenStaffLocator(); Assert.IsFalse( hud.InfoArmOpen, "the locator puts the info arm away" );
		hud.ToggleCameraPanel(); Frame( hud, context ); Assert.AreEqual( LocatorMode.None, hud.Locator.Mode, "the camera panel puts the locator away" );
	}

	// two roles: handymen 30, 20, 10 (newest first) and guards 7
	private static int Next( int role, int current )
	{
		var actors = role switch { 1 => new[] { 30, 20, 10 }, 3 => new[] { 7 }, _ => Array.Empty<int>() };
		return current == 0 ? actors.FirstOrDefault() : actors.SkipWhile( actor => actor != current ).Skip( 1 ).FirstOrDefault();
	}

	[TestMethod]
	public void TheStaffKeyOpensThePanelWithNoRoleAndTheRadiosCycle()
	{
		var locator = new LocatorModel();
		locator.Open( LocatorMode.Staff, 0, 0 );
		Assert.AreEqual( (LocatorMode.Staff, 0, 0, false, false, false), (locator.Mode, locator.Role, locator.ActorId, locator.RadioChosen, locator.StripShown, locator.Following) );
		locator.Press( 1, Next );
		Assert.AreEqual( (1, 30, true, true, true), (locator.Role, locator.ActorId, locator.RadioChosen, locator.StripShown, locator.Following) );
		locator.Press( 1, Next ); Assert.AreEqual( 20, locator.ActorId, "the chosen radio again steps to the next actor" );
		locator.Press( 1, Next ); Assert.AreEqual( 10, locator.ActorId );
		locator.Press( 1, Next ); Assert.AreEqual( 30, locator.ActorId, "past the last actor the cycle starts again" );
		locator.Press( 3, Next ); Assert.AreEqual( (3, 7), (locator.Role, locator.ActorId), "another role starts from its own first actor" );
		locator.StopFollowing(); Assert.IsFalse( locator.Following ); Assert.IsTrue( locator.StripShown, "moving the camera keeps the strip" );
		locator.Press( 2, Next );
		Assert.AreEqual( (2, 0, false, false), (locator.Role, locator.ActorId, locator.StripShown, locator.Following), "a role without actors hides the strip" );
		locator.Close();
		Assert.AreEqual( (LocatorMode.None, 0, false), (locator.Mode, locator.ActorId, locator.StripShown) );
		locator.Press( 1, Next ); Assert.AreEqual( 0, locator.ActorId, "a closed panel ignores presses" );
	}

	[TestMethod]
	public void AnActorOpensItsRoleAndTheVisitorKeyWaitsForItsRadio()
	{
		var locator = new LocatorModel();
		locator.Open( LocatorMode.Staff, LocatorModel.RoleOf( StaffType.Guard ), 7 );
		Assert.AreEqual( (3, 7, true, true, true), (locator.Role, locator.ActorId, locator.RadioChosen, locator.StripShown, locator.Following) );
		locator.OpenVisitors();
		Assert.AreEqual( (LocatorMode.Visitor, LocatorModel.VisitorRole, 0, false), (locator.Mode, locator.Role, locator.ActorId, locator.RadioChosen) );
		static int Visitors( int role, int current ) => role == LocatorModel.VisitorRole ? current == 0 ? 4 : current == 4 ? 9 : 0 : 0;
		locator.Press( LocatorModel.VisitorRole, Visitors ); Assert.AreEqual( (4, true), (locator.ActorId, locator.StripShown) );
		locator.Press( LocatorModel.VisitorRole, Visitors ); Assert.AreEqual( 9, locator.ActorId );
		locator.LoseActor(); Assert.AreEqual( (0, false, false), (locator.ActorId, locator.StripShown, locator.Following) );
	}

	[TestMethod]
	public void TheKeysWithoutAnActorKeepTheStripWithItsLastName()
	{
		// tp.exe 0x4b3f60 without an actor and 0x4b4280 clear the actor and role but leave the strip window, its text and the follow flag
		var locator = new LocatorModel();
		locator.Open( LocatorMode.Staff, 3, 7 );
		locator.Open( LocatorMode.Staff, 0, 0 );
		Assert.AreEqual( (0, 0, false, true, true), (locator.Role, locator.ActorId, locator.RadioChosen, locator.StripShown, locator.Following) );
		Assert.AreEqual( (7, 3), (locator.StripActorId, locator.StripRole), "the strip keeps the last located name" );
		locator.OpenVisitors();
		Assert.AreEqual( (LocatorModel.VisitorRole, 0, true, 7, 3), (locator.Role, locator.ActorId, locator.StripShown, locator.StripActorId, locator.StripRole) );
		static int Visitors( int role, int current ) => role == LocatorModel.VisitorRole && current == 0 ? 4 : 0;
		locator.Press( LocatorModel.VisitorRole, Visitors );
		Assert.AreEqual( (4, LocatorModel.VisitorRole), (locator.StripActorId, locator.StripRole), "a located actor rewrites the strip" );
		locator.Close();
		Assert.AreEqual( (0, 0, false), (locator.StripActorId, locator.StripRole, locator.StripShown) );
	}

	[TestMethod]
	public void TheVisitorKeyLocatesTheVisitorWhoseInfoIsOpen()
	{
		var hud = VisitorWorldSelectionTests.Fixture( out var simulation, out _ );
		var first = new Guest( 1, 0 ) { State = GuestState.WalkingAround };
		var second = new Guest( 8, 0 ) { State = GuestState.WalkingAround };
		typeof( GuestSimulation ).GetField( "guests", BindingFlags.Instance | BindingFlags.NonPublic )!.SetValue( simulation, new List<Guest> { first, second } );
		hud.OpenVisitorWindow();
		Assert.AreEqual( 1, hud.SelectedVisitorId );
		hud.OpenVisitorLocator();
		Assert.AreEqual( (LocatorMode.Visitor, 0), (hud.Locator.Mode, hud.Locator.ActorId), "a selected row without its info open is not the original's info window (0x4b4280)" );
		hud.CloseLocator();
		hud.OpenVisitorWindow();
		((IUiRowList)hud.Stack.Top!.Find( "visitorList" )!).RowClicked( 1, true );
		Assert.IsTrue( hud.VisitorInfoWindowOpen && !hud.VisitorWindowOpen, "a row click opens the visitor window, which gives the list way" );
		hud.OpenVisitorLocator();
		Assert.AreEqual( (LocatorMode.Visitor, 8, true), (hud.Locator.Mode, hud.Locator.ActorId, hud.Locator.RadioChosen),
			"the key takes the visitor whose window a row click opened ([0x7c2658])" );
		Assert.IsFalse( hud.VisitorWindowOpen );
		hud.CloseLocator();
		Assert.IsTrue( hud.SelectWorldVisitor( first ) );
		hud.OpenVisitorLocator();
		Assert.AreEqual( 1, hud.Locator.ActorId, "a visitor picked in the park opens its info too" );
	}

	[TestMethod]
	public void RolesMapAsTheOriginalsRadiosAndTheStateIconFollowsTheDisplayState()
	{
		foreach ( var type in Enum.GetValues<StaffType>() )
			Assert.AreEqual( type, LocatorModel.StaffTypeOf( LocatorModel.RoleOf( type ) ) );
		CollectionAssert.AreEqual( new[] { 1, 2, 3, 4, 5 }, new[] { StaffType.Handyman, StaffType.Mechanic, StaffType.Guard, StaffType.Entertainer, StaffType.Researcher }
			.Select( LocatorModel.RoleOf ).ToArray() );
		// tp.exe 0x485ec0
		CollectionAssert.AreEqual( new[] { 0, 0, 1, 2, 3, 9 }, new[] { StaffState.Idle, StaffState.Patrolling, StaffState.Working, StaffState.Resting, StaffState.OnStrike, StaffState.PickedUp }
			.Select( ParkHud.StaffStateFrame ).ToArray() );
	}


	[TestMethod]
	public void ThePanelsComeFromTheOriginalTables()
	{
		var panels = OriginalLayoutTables.LocatorPanels;
		Assert.AreEqual( (0x95Cu, 0x963u), (panels.Roots[0].Id, panels.Roots[1].Id) );
		CollectionAssert.AreEqual( new[] { ("b_shandy", 482), ("b_smech", 483), ("b_sresrcher", 486), ("b_senter", 484), ("b_sguard", 485) },
			panels.Roots[0].Descendants().Where( node => node.Type == 2 ).Select( node => (node.Model!, node.Help) ).ToArray() );
		Assert.AreEqual( ("b_skids", 489), panels.Roots[1].Descendants().Where( node => node.Type == 2 ).Select( node => (node.Model!, node.Help) ).Single() );
		var strip = OriginalLayoutTables.LocatorStrip.Root;
		Assert.AreEqual( ("!frame", 552), (strip.Model, strip.Help) );
		CollectionAssert.AreEqual( new[] { "staffstat", "ti_hungry" }, strip.Children.Where( node => node.Type == 6 ).Select( node => node.Model ).ToArray() );
	}
}

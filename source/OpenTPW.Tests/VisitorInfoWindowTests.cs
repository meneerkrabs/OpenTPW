using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTPW.Hud;
using OpenTPW.UI.Original;

namespace OpenTPW.Tests;

/// <summary>UI-910: the per-visitor window (tp.exe table 0x7575e0, opener 0x4b79b0, handler 0x4b7720, refresh 0x4b72a0).</summary>
[TestClass]
[DoNotParallelize]
public class VisitorInfoWindowTests
{
	private static void SetGuests( GuestSimulation simulation, params Guest[] guests ) =>
		typeof( GuestSimulation ).GetField( "guests", BindingFlags.Instance | BindingFlags.NonPublic )!.SetValue( simulation, guests.ToList() );

	private static UiContext Context()
	{
		var font = new FontAtlas( FontAtlasTests.CreateFont( 8, new FontAtlasTests.SyntheticGlyph( 'A', 2, 2, 0, 0, 3 ) ) );
		return new UiContext( OriginalUiTests.FakeStrings(), new UiFonts( _ => font ), new UiModels( name => throw new FileNotFoundException( name ) ) ) { Canvas = new UiCanvas( 1000, 1000 ) };
	}

	[TestMethod]
	public void TheTableGivesTheFramesLabelsAndButtonsTheirRectanglesAndHelp()
	{
		var table = OriginalLayoutTables.VisitorInfoWindow;
		Assert.AreEqual( new UiRect( 328, 130, 1392, 771 ), table.Root.Rect );
		Assert.AreEqual( "window1", table.Root.Model );
		Assert.AreEqual( (98, 97, 99, 100), (table.Model( "b_arup" ).Help, table.Model( "b_ardown" ).Help, table.Model( "b_locate" ).Help, table.Model( "b_kids" ).Help) );
		Assert.AreEqual( new UiRect( 1170, 589, 328, 45 ), table.Control( 0x3732 ).Rect, "the happiness bar (a type-9 gauge)" );
		Assert.AreEqual( 9, table.Control( 0x3732 ).Type );
		Assert.AreEqual( new UiRect( 817, 760, 497, 45 ), table.Control( 0x3734 ).Inner );
		Assert.AreEqual( 95, table.Control( 0x3734 ).Help );
		Assert.AreEqual( 0xFFFFFFFEu, table.Model( "b_okay" ).Id, "the tick is control -2" );
	}

	[TestMethod]
	public void TheTitleIsTheVisitorsNumberOrNewVisitor()
	{
		var strings = OriginalUiTests.FakeStrings();
		Assert.AreEqual( strings[UIStrings.NewVisitor], VisitorInfoRules.Title( strings, 0 ) );
		Assert.AreEqual( strings.Format( UIStrings.VisitorCount, "42" ), VisitorInfoRules.Title( strings, 42 ) );
	}

	[TestMethod]
	public void TheArrowsPreserveProjectedOrderAndWrapWithHostOnlyAbsentCurrentEndpoints()
	{
		var ids = new[] { 2, 9, 7 };
		Assert.AreEqual( 9, VisitorInfoRules.Cycle( ids, 2, forward: true ) );
		Assert.AreEqual( 7, VisitorInfoRules.Cycle( ids, 9, forward: true ) );
		Assert.AreEqual( 2, VisitorInfoRules.Cycle( ids, 7, forward: true ), "next wraps to the projected head" );
		Assert.AreEqual( 7, VisitorInfoRules.Cycle( ids, 2, forward: false ), "previous wraps to the projected tail" );
		Assert.AreEqual( 9, VisitorInfoRules.Cycle( ids, 7, forward: false ) );
		Assert.AreEqual( 2, VisitorInfoRules.Cycle( ids, 9, forward: false ) );
		Assert.AreEqual( 2, VisitorInfoRules.Cycle( ids, 77, forward: true ), "host-only helper fallback; the HUD closes a missing selection" );
		Assert.AreEqual( 7, VisitorInfoRules.Cycle( ids, 77, forward: false ) );
		foreach ( var forward in new[] { false, true } )
		{
			Assert.IsNull( VisitorInfoRules.Cycle( Array.Empty<int>(), 1, forward ) );
			Assert.AreEqual( 2, VisitorInfoRules.Cycle( new[] { 2 }, 2, forward ) );
		}
		CollectionAssert.AreEqual( new[] { 2, 9, 7 }, ids );
	}

	[TestMethod]
	public void TheWindowShowsTheVisitorsCountersCashBarAndThoughtAndFollowsTheArrows()
	{
		var hud = VisitorWorldSelectionTests.Fixture( out var simulation, out _ );
		var first = new Guest( 1, 0 ) { VisitorNumber = 1, State = GuestState.WalkingAround, Money = 250, RidesTaken = 3, ShopsVisited = 2, SideshowsVisited = 1, SideshowsWon = 1, Happiness = 50, Thought = GuestThought.Thirsty };
		var second = new Guest( 8, 0 ) { VisitorNumber = 8, State = GuestState.Queueing, Money = 40 };
		SetGuests( simulation, first, second );

		Assert.IsTrue( hud.OpenVisitorInfoWindow( first ) );
		Assert.IsTrue( hud.VisitorInfoWindowOpen );
		Assert.AreSame( first, hud.InfoVisitor );
		var shown = hud.VisitorInfoShown;
		Assert.AreEqual( (250, 3, 2, 1, 1), (shown.Cash, shown.Rides, shown.Purchases, shown.SideshowsPlayed, shown.SideshowsWon) );
		Assert.AreEqual( 512, shown.HappinessGauge, "50 x 1024 / 100, the low byte of the truncated happiness" );
		Assert.AreEqual( 1, shown.ThoughtFrame, "the thought less 1 (Thirsty is 2); none is no frame" );
		Assert.AreEqual( VisitorInfoRules.Title( OriginalUiTests.FakeStrings(), 1 ), hud.VisitorInfoTitle );

		Assert.IsTrue( hud.CycleVisitorInfo( forward: false ), "pool-free host fallback wraps from the first to the last visitor" );
		Assert.AreSame( second, hud.InfoVisitor );
		Assert.AreEqual( 40, hud.VisitorInfoShown.Cash );
		Assert.AreEqual( -1, hud.VisitorInfoShown.ThoughtFrame );
		Assert.IsTrue( hud.CycleVisitorInfo( forward: true ) );
		Assert.AreSame( first, hud.InfoVisitor );
	}

	[TestMethod]
	public void EveryValueIsLeftAlignedAsTheBuilderLeavesTheCellConstructorsAlignment()
	{
		// tp.exe 0x4b6c20 makes the money, duration and count cells (0x490250, 0x490e60, 0x48fde0), whose constructors store alignment 0 at +0xd8
		// (0x490298, 0x490ea8, 0x48fe28), and sends none of them the alignment call 0x65c428, unlike the All Visitors row factory 0x4939d0
		var hud = VisitorWorldSelectionTests.Fixture( out var simulation, out _ );
		var guest = new Guest( 1, 0 ) { VisitorNumber = 1, State = GuestState.WalkingAround, Money = 250 };
		SetGuests( simulation, guest );
		Assert.IsTrue( hud.OpenVisitorInfoWindow( guest ) );
		foreach ( var id in new[] { "cash", "timeInPark", "rides", "purchases", "sideshowsPlayed", "sideshowsWon", "status" } )
			Assert.AreEqual( UiAlign.Left, ((UiLabel)hud.Stack.Top!.Find( id )!).Align, id );
	}

	[TestMethod]
	public void TheWindowGivesWayToTheListAndTheListButtonReturnsToIt()
	{
		var hud = VisitorWorldSelectionTests.Fixture( out var simulation, out _ );
		var first = new Guest( 1, 0 ) { State = GuestState.WalkingAround };
		var second = new Guest( 8, 0 ) { State = GuestState.WalkingAround };
		SetGuests( simulation, first, second );
		hud.OpenVisitorWindow();
		Assert.IsTrue( hud.OpenVisitorInfoWindow( second ), "0x4b79b0 closes the window that is open (0x485b40): the list" );
		Assert.IsFalse( hud.VisitorWindowOpen );
		hud.ShowAllVisitorsOfInfo();
		Assert.IsTrue( hud.VisitorWindowOpen && !hud.VisitorInfoWindowOpen );
		Assert.AreEqual( 8, hud.SelectedVisitorId, "b_kids (0x3738) opens the All Visitors window on the visitor" );
	}

	[TestMethod]
	public void AVisitorWhoLeavesClosesTheWindowAndAGoneVisitorCannotOpenIt()
	{
		var hud = VisitorWorldSelectionTests.Fixture( out var simulation, out _ );
		var guest = new Guest( 4, 0 ) { State = GuestState.WalkingAround };
		SetGuests( simulation, guest );
		Assert.IsTrue( hud.OpenVisitorInfoWindow( guest ) );
		guest.State = GuestState.Gone;
		Assert.IsNull( hud.InfoVisitor );
		hud.Stack.Top!.Update( Context(), UiInput.Idle( new System.Numerics.Vector2( 1, 1 ) ) );
		Assert.IsFalse( hud.VisitorInfoWindowOpen, "0x4b72a0 closes the window when the selected thing is gone" );
		Assert.IsFalse( hud.OpenVisitorInfoWindow( guest ) );
		Assert.IsFalse( hud.OpenVisitorInfoWindow( new Guest( 5, 0 ) { State = GuestState.WalkingAround } ), "a visitor the simulation does not hold" );
	}

	[TestMethod]
	public void TheStatusAndThoughtTextsComeFromKidStatesAndThoughtsWhenTheGameDataIsPresent()
	{
		var strings = UiStringTable.Load( LanguageTests.OriginalLanguagePublic( "English" ) );
		Assert.AreEqual( "Walking around", strings.KidState( (int)GuestState.WalkingAround ) );
		Assert.AreEqual( "Queueing for a ride", strings.KidState( (int)GuestState.MovingUpQueue ) );
		Assert.AreEqual( "Using: Big Wheel", strings.KidState( (int)GuestState.Using, "Big Wheel" ), "state 16 takes the used attraction's name (0x4b72a0: parameter 0)" );
		Assert.AreEqual( "", strings.KidState( 99 ) );
		Assert.AreEqual( "Hungry", strings.Thought( (int)GuestThought.Hungry ) );
		Assert.AreEqual( "Confused...", strings.Thought( (int)GuestThought.Confused ) );
		Assert.AreEqual( "", strings.Thought( 0 ) );
	}
}

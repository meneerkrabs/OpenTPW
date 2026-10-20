using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTPW.Hud;
using OpenTPW.UI.Original;
using NVector2 = System.Numerics.Vector2;

namespace OpenTPW.Tests;

/// <summary>
/// cat_ui 0xBD: the hook 0x485780 plays it when the parent that receives 0x100 has style bit 0x10 (the choice at 0x4857cb).
/// A held scroll arrow (ctor 0x6688b3: 500 ms, then 125 ms; timer 0x668dd9) plays nothing on the release after repeating starts (0x668eff).
/// </summary>
[TestClass]
[DoNotParallelize]
public class SelectClickTests
{
	private static IEnumerable<LayoutTable> Tables() =>
		typeof( OriginalLayoutTables ).GetFields( BindingFlags.Public | BindingFlags.Static )
			.Where( field => field.FieldType == typeof( LayoutTable ) )
			.Select( field => (LayoutTable)field.GetValue( null )! );

	[TestMethod]
	public void TheClickFollowsTheStyleBit0x10OfConcreteParents()
	{
		var visitors = OriginalLayoutTables.VisitorsWindow;
		Assert.AreEqual( GameAudio.SelectClickEvent, visitors.ClickSoundOf( visitors.All.Single( node => node.Model == "b_up" ) ), "a scroll arrow's parent is the vertical slider (type 3, style 0x10)" );
		Assert.AreEqual( GameAudio.ClickEvent, visitors.ClickSoundOf( visitors.Model( "b_okay" ) ), "the tick's parent is the window" );
		// The thumb is a type-2 button (style 1) under the slider, so the helper reports 0xBD, but its procedure 0x66bb9b replaces the button class 0x668f9c and its release (0x10004) sends 0x802 to the slider, never 0x100: a still click plays nothing, and no button is built for it.
		Assert.AreEqual( GameAudio.SelectClickEvent, visitors.ClickSoundOf( visitors.Model( "b_scroller" ) ) );
		var visitorList = visitors.Control( 0x1E497 );
		Assert.AreEqual( 7, visitorList.Type );
		Assert.AreEqual( 0x291u, visitorList.Attributes );
		Assert.AreEqual( GameAudio.SelectClickEvent, visitors.ClickSoundOf( visitorList.Children.First( node => node.Type == 2 ) ), "a column header of a sortable list (type 7, style 0x291)" );
		var loans = OriginalLayoutTables.LoansWindow;
		var loanList = loans.All.Single( node => node.Type == 7 );
		Assert.AreEqual( 0x91u, loanList.Attributes );
		Assert.AreEqual( GameAudio.SelectClickEvent, loans.ClickSoundOf( loanList.Children.First( node => node.Type == 2 ) ), "a column header of a sortable list (type 7, style 0x91)" );
		Assert.AreEqual( GameAudio.SelectClickEvent, loans.ClickSoundOf( loans.Control( 0x12C4BF ) ), "a toggle tab of a radio group (type 8, style 0x11)" );
		var parkInfo = OriginalLayoutTables.ParkInfoWindow;
		Assert.AreEqual( GameAudio.SelectClickEvent, parkInfo.ClickSoundOf( parkInfo.Control( 0x4A68C3 ) ), "a period toggle's parent is the radio group" );
		Assert.AreEqual( GameAudio.ClickEvent, parkInfo.ClickSoundOf( parkInfo.Control( 0x4A68BF ) ), "a series toggle's parent is the graph panel" );
		var locator = OriginalLayoutTables.LocatorPanels;
		var staffGroup = locator.Control( 0x95D );
		Assert.AreEqual( 8, staffGroup.Type );
		Assert.AreEqual( 0x1u, staffGroup.Attributes );
		Assert.AreEqual( GameAudio.ClickEvent, locator.ClickSoundOf( locator.Control( 0x95E ) ), "a radio group without the bit (type 8, style 0x1) keeps 0x1F" );
	}

	private static ParkHud Hud() => (ParkHud)RuntimeHelpers.GetUninitializedObject( typeof( ParkHud ) );

	private static void Set( object target, string field, object value ) =>
		target.GetType().GetField( field, BindingFlags.Instance | BindingFlags.NonPublic )!.SetValue( target, value );

	private static BuyListModel ModelOf( ParkHud hud ) =>
		(BuyListModel)typeof( ParkHud ).GetField( "buy", BindingFlags.Instance | BindingFlags.NonPublic )!.GetValue( hud )!;

	private sealed class FakeCatalog : IBuildCatalog
	{
		public IReadOnlyList<BuildItem> GetItems( BuildCategory category ) => category == BuildCategory.Rides
			? Enumerable.Range( 0, 14 ).Select( index => new BuildItem( $"t/{index}", 100 + index, BuildCategory.Rides, index, 1000 - index, null, Array.Empty<string>() ) ).ToArray()
			: Array.Empty<BuildItem>();
	}

	private static UiScreen BuyScreen( ParkHud hud )
	{
		Set( hud, "strings", OriginalUiTests.FakeStrings() );
		Set( hud, "buy", new BuyListModel( new FakeCatalog(), item => $"Item {item.InfoId}", item => item.Cost ) );
		return (UiScreen)typeof( ParkHud ).GetMethod( "CreateBuyScreen", BindingFlags.Instance | BindingFlags.NonPublic )!.Invoke( hud, null )!;
	}

	private static UiScreen VisitorScreen( ParkHud hud )
	{
		Set( hud, "strings", OriginalUiTests.FakeStrings() );
		return (UiScreen)typeof( ParkHud ).GetMethod( "CreateVisitorScreen", BindingFlags.Instance | BindingFlags.NonPublic )!.Invoke( hud, null )!;
	}

	/// <summary>The centre of the buy window's scroll-down arrow on a 2048×1536 canvas (authored (1742,809)-(1803,869)).</summary>
	private static readonly NVector2 ScrollDown = new( 1772, 839 );

	private static void Frame( UiScreen screen, float time, bool down = false, bool pressed = false, bool released = false )
	{
		var font = new FontAtlas( FontAtlasTests.CreateFont( 8, new FontAtlasTests.SyntheticGlyph( 'A', 2, 2, 0, 0, 3 ) ) );
		var context = new UiContext( OriginalUiTests.FakeStrings(), new UiFonts( _ => font ), new UiModels( name => throw new FileNotFoundException( name ) ) )
			{ Canvas = new UiCanvas( 2048, 1536 ), Time = time, ResolveTexture = _ => null };
		screen.Update( context, new UiInput( ScrollDown, down || pressed, pressed, released, false, UiKeys.None ) );
	}

	[TestMethod]
	public void BuyScrollUpPlays0xBDAndTheVisitorsTickPlays0x1F()
	{
		using var probe = new AudioTriggerProbe();
		var hud = Hud();
		var buy = BuyScreen( hud );
		var up = (UiButton)buy.Find( "scrollUp" )!;
		Assert.AreEqual( GameAudio.SelectClickEvent, up.ClickSound );
		Assert.AreEqual( 0.5f, up.Repeat!.Delay, "the arrow waits 500 ms" );
		Assert.AreEqual( 0.125f, up.Repeat.Interval, "then it steps every 125 ms" );
		up.Activate();
		var exit = (UiButton)VisitorScreen( hud ).Find( "exit" )!;
		Assert.AreEqual( GameAudio.ClickEvent, exit.ClickSound );
		exit.Activate();
		CollectionAssert.AreEqual( new[] { GameAudio.SelectClickEvent, GameAudio.ClickEvent }, probe.Events( "cat_ui" ) );
	}

	[TestMethod]
	public void ACategoryTabPlays0xBDWhenAlreadySelectedAndAHeaderPlays0xBD()
	{
		using var probe = new AudioTriggerProbe();
		var hud = Hud();
		var buy = BuyScreen( hud );
		var tab = (UiButton)buy.Find( "categoryRides" )!;
		Assert.AreEqual( GameAudio.SelectClickEvent, tab.ClickSound );
		Assert.AreEqual( BuildCategory.Rides, ModelOf( hud ).Category );
		tab.Activate();
		Assert.AreEqual( BuildCategory.Rides, ModelOf( hud ).Category, "a tab that is already selected still activates" );
		buy.Find( "headerName" )!.Activate();
		CollectionAssert.AreEqual( new[] { GameAudio.SelectClickEvent, GameAudio.SelectClickEvent }, probe.Events( "cat_ui" ) );
	}

	[TestMethod]
	public void APlainArrowClickPlays0xBDAndScrollsOneStep()
	{
		using var probe = new AudioTriggerProbe();
		var hud = Hud();
		var screen = BuyScreen( hud );
		Frame( screen, 0, pressed: true );
		Frame( screen, 0.1f, down: true );
		Frame( screen, 0.2f, released: true );
		Assert.AreEqual( 1, ModelOf( hud ).FirstRow, "a click before 500 ms scrolls once" );
		CollectionAssert.AreEqual( new[] { GameAudio.SelectClickEvent }, probe.Events( "cat_ui" ), "and plays the ordinary 0xBD" );
		Assert.AreEqual( 0, ((UiButton)screen.Find( "scrollDown" )!).HoldState );
	}

	[TestMethod]
	public void AHeldArrowStepsAt500MsThenEvery125MsAndTheReleasePlaysNothing()
	{
		using var probe = new AudioTriggerProbe();
		var hud = Hud();
		var screen = BuyScreen( hud );
		var button = (UiButton)screen.Find( "scrollDown" )!;
		Frame( screen, 0, pressed: true );
		Frame( screen, 0.49f, down: true );
		Assert.AreEqual( 0, ModelOf( hud ).FirstRow, "nothing before the 500 ms delay" );
		Assert.AreEqual( 0, probe.Events( "cat_ui" ).Length );
		Frame( screen, 0.5f, down: true );
		Assert.AreEqual( 1, ModelOf( hud ).FirstRow, "the first repeat scrolls one step" );
		Assert.AreEqual( 0, probe.Events( "cat_ui" ).Length, "a repeat plays no sound" );
		Frame( screen, 0.625f, down: true );
		Assert.AreEqual( 1, ModelOf( hud ).FirstRow, "exactly 125 ms is not another step" );
		Frame( screen, 0.626f, down: true );
		Assert.AreEqual( 2, ModelOf( hud ).FirstRow, "the next step is just after 125 ms" );
		Assert.AreEqual( 0, probe.Events( "cat_ui" ).Length );
		Frame( screen, 0.7f, released: true );
		Assert.AreEqual( 2, ModelOf( hud ).FirstRow, "the release after repeating has started does not scroll" );
		Assert.AreEqual( 0, probe.Events( "cat_ui" ).Length, "and it plays nothing at all" );
		Assert.AreEqual( 0, button.HoldState );
		button.HoldState = 2;
		button.Activate();
		Assert.AreEqual( 3, ModelOf( hud ).FirstRow, "a direct activate during the repeat still steps" );
		Assert.AreEqual( 0, probe.Events( "cat_ui" ).Length, "and still plays no sound" );
	}
}

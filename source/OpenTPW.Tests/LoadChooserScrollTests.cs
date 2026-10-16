using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTPW.FrontEnd;
using OpenTPW.Hud;
using OpenTPW.UI.Original;

namespace OpenTPW.Tests;

[TestClass]
public class LoadChooserScrollTests
{
	private static UiContext Context() => new( OriginalUiTests.FakeStrings(), null!, new UiModels( name => throw new FileNotFoundException( name ) ) )
	{
		Canvas = new UiCanvas( 1024, 768 )
	};

	private static ParkLoadEntry[] Entries( int count ) => Enumerable.Range( 0, count )
		.Select( index => new ParkLoadEntry( "theme" + index, -1, false, "/save/park" + index + ".json", "Park " + index ) ).ToArray();

	[TestMethod]
	public void KeyboardCanReachAndActivateAnEntryBeyondEightVisibleRows()
	{
		var entries = Entries( 11 );
		var loaded = new List<ParkLoadEntry>();
		var stack = new UiScreenStack();
		stack.Push( FrontEndMenu.CreateLoadScreen( stack, OriginalUiTests.FakeStrings(), entries, loaded.Add ) );
		for ( var index = 1; index < entries.Length; index++ )
			stack.Update( Context(), UiInput.Key( UiKeys.Right ) );
		Assert.AreEqual( 0, loaded.Count, "navigation alone must not load a park" );
		stack.Update( Context(), UiInput.Key( UiKeys.Accept ) );
		Assert.AreSame( entries[^1], loaded.Single(), "the actual screen must activate the final supplied entry" );
	}

	[DataTestMethod]
	[DataRow( 0 )]
	[DataRow( 1 )]
	[DataRow( 8 )]
	[DataRow( 9 )]
	[DataRow( 13 )]
	[DataRow( 17 )]
	public void TheViewportDoesNotLimitTheSuppliedEntryCount( int count )
	{
		var entries = Entries( count );
		var loaded = new List<ParkLoadEntry>();
		var screen = FrontEndMenu.CreateLoadScreen( new UiScreenStack(), OriginalUiTests.FakeStrings(), entries, loaded.Add );
		var list = (UiScrollList)screen.Find( "parks" )!;
		Assert.AreEqual( count, list.Rows().Count );
		// tp.exe's load list (table 0x7523f0, content 229..733) at the shared slot-7 row pitch: (733 - 229) / 41.4 = 12 rows
		Assert.AreEqual( 12, list.VisibleRows );
		if ( count == 0 )
		{
			Assert.IsNotNull( screen.Find( "empty" ) );
			Assert.IsFalse( screen.Find( "loadSelected" )!.Enabled );
			Assert.IsFalse( list.Visible, "the empty list must not draw over the empty-state label" );
			screen.Find( "loadSelected" )!.Activate();
			list.Activate();
			Assert.AreEqual( 0, loaded.Count );
			Assert.AreEqual( "back", screen.Focused!.Id );
			return;
		}
		Assert.AreEqual( 0, list.Selected );
		list.Select( int.MaxValue );
		Assert.AreEqual( count - 1, list.Selected );
		list.Select( -1 );
		Assert.AreEqual( 0, list.Selected );
		screen.Find( "loadSelected" )!.Activate();
		Assert.AreSame( entries[0], loaded.Single() );
	}

	[TestMethod]
	public void WheelScrollAndMouseSelectionLoadExactlyTheSelectedEntry()
	{
		var entries = Entries( 17 );
		var loaded = new List<ParkLoadEntry>();
		var stack = new UiScreenStack();
		var screen = FrontEndMenu.CreateLoadScreen( stack, OriginalUiTests.FakeStrings(), entries, loaded.Add );
		stack.Push( screen );
		var list = (UiScrollList)screen.Find( "parks" )!;
		var context = Context();
		var point = list.ScreenRect( context.Canvas ).Center;
		for ( var index = 0; index < 20; index++ )
			stack.Update( context, new UiInput( point, false, false, false, false, UiKeys.None, -1 ) );
		var rect = list.ScreenRect( context.Canvas );
		var lastRow = new System.Numerics.Vector2( rect.Center.X, rect.Bottom - 20 * rect.Height / list.Bounds.Height );
		Assert.AreEqual( 16, list.RowAt( context.Canvas, lastRow ) );
		stack.Update( context, UiInput.Click( lastRow ) );
		Assert.AreEqual( 16, list.Selected );
		Assert.AreEqual( 0, loaded.Count, "a first click selects rather than loading a different park" );
		stack.Update( context, UiInput.Click( screen.Find( "loadSelected" )!.ScreenRect( context.Canvas ).Center ) );
		Assert.AreSame( entries[16], loaded.Single() );
		Assert.AreEqual( entries[16].OwnSavePath, loaded[0].OwnSavePath );
	}

	[TestMethod]
	public void OriginalSandboxAndOwnEntriesKeepTheirLabelsAndIdentityBeyondTheFirstViewport()
	{
		var special = new[] { new ParkLoadEntry( "jungle", 0, true ), new ParkLoadEntry( "space", 3, false ), new ParkLoadEntry( "space", -1, false, "/save/own.json", "Named own park" ) };
		var entries = Entries( 8 ).Concat( special ).ToArray();
		var loaded = new List<ParkLoadEntry>();
		var stack = new UiScreenStack();
		var screen = FrontEndMenu.CreateLoadScreen( stack, OriginalUiTests.FakeStrings(), entries, loaded.Add );
		stack.Push( screen );
		var list = (UiScrollList)screen.Find( "parks" )!;
		CollectionAssert.AreEqual( new[] { "OpenTPW sandbox park", "Space Zone - original park (read-only)", "Named own park" }, list.Rows().Skip( 8 ).ToArray() );
		for ( var index = 8; index < entries.Length; index++ )
		{
			list.Select( index );
			screen.Focus( list );
			stack.Update( Context(), UiInput.Key( UiKeys.Accept ) );
			Assert.AreSame( entries[index], loaded[^1] );
		}
	}

	[TestMethod]
	public void BackLoadsNothingAndAFreshChooserStartsWithFreshEntriesAndSelection()
	{
		var loaded = new List<ParkLoadEntry>();
		var stack = new UiScreenStack();
		var parent = new UiScreen( "pause" );
		stack.Push( parent );
		var screen = FrontEndMenu.CreateLoadScreen( stack, OriginalUiTests.FakeStrings(), Entries( 17 ), loaded.Add );
		stack.Push( screen );
		((UiScrollList)screen.Find( "parks" )!).Select( 16 );
		stack.Update( Context(), UiInput.Key( UiKeys.Back ) );
		Assert.AreSame( parent, stack.Top );
		Assert.AreEqual( 0, loaded.Count );
		var fresh = Entries( 2 );
		screen = FrontEndMenu.CreateLoadScreen( stack, OriginalUiTests.FakeStrings(), fresh, loaded.Add );
		stack.Push( screen );
		Assert.AreEqual( 0, ((UiScrollList)screen.Find( "parks" )!).Selected );
		stack.Update( Context(), UiInput.Key( UiKeys.Accept ) );
		Assert.AreSame( fresh[0], loaded.Single() );
	}

	[TestMethod]
	public void MouseBackClosesTheChooserWithoutLoadingTheScrolledSelection()
	{
		var stack = new UiScreenStack();
		var parent = new UiScreen( "lobby" );
		stack.Push( parent );
		var loaded = new List<ParkLoadEntry>();
		var screen = FrontEndMenu.CreateLoadScreen( stack, OriginalUiTests.FakeStrings( "French" ), Entries( 17 ), loaded.Add );
		stack.Push( screen );
		((UiScrollList)screen.Find( "parks" )!).Select( 16 );
		Assert.AreEqual( "b_exit", ((UiButton)screen.Find( "back" )!).Model );
		Assert.AreEqual( "ui202", ((UiButton)screen.Find( "loadSelected" )!).Text!() );
		stack.Update( Context(), UiInput.Click( screen.Find( "back" )!.ScreenRect( Context().Canvas ).Center ) );
		Assert.AreSame( parent, stack.Top );
		Assert.AreEqual( 0, loaded.Count );
	}

	[TestMethod]
	public void AnOpenChooserKeepsItsEntrySnapshotWhileAReopenedChooserUsesFreshData()
	{
		var entries = Entries( 9 ).ToList();
		var expected = entries[^1];
		var loaded = new List<ParkLoadEntry>();
		var screen = FrontEndMenu.CreateLoadScreen( new UiScreenStack(), OriginalUiTests.FakeStrings(), entries, loaded.Add );
		entries.Clear();
		var list = (UiScrollList)screen.Find( "parks" )!;
		list.Select( 8 );
		list.Activate();
		Assert.AreSame( expected, loaded.Single() );
		var fresh = FrontEndMenu.CreateLoadScreen( new UiScreenStack(), OriginalUiTests.FakeStrings(), entries, loaded.Add );
		Assert.IsNotNull( fresh.Find( "empty" ) );
		Assert.IsFalse( fresh.Find( "loadSelected" )!.Enabled );
	}

	[TestMethod]
	public void TheActualHudPauseLoadRouteCanActivateTheLastEntryAndBackReturnsToPause()
	{
		var entries = Entries( 11 );
		var loaded = new List<ParkLoadEntry>();
		var hud = VisitorWorldSelectionTests.Fixture( out _, out _ );
		typeof( ParkHud ).GetField( "host", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic )!.SetValue( hud, new HudHost
		{
			CreateLoad = stack => FrontEndMenu.CreateLoadScreen( stack, OriginalUiTests.FakeStrings(), entries, loaded.Add )
		} );
		hud.OpenPauseMenu();
		var pause = hud.Stack.Top!;
		hud.Stack.Update( Context(), UiInput.Click( pause.Find( "Load" )!.ScreenRect( Context().Canvas ).Center ) );
		Assert.AreEqual( "load", hud.Stack.Top!.Name );
		for ( var index = 1; index < entries.Length; index++ )
			hud.Stack.Update( Context(), UiInput.Key( UiKeys.Right ) );
		hud.Stack.Update( Context(), UiInput.Key( UiKeys.Accept ) );
		Assert.AreSame( entries[^1], loaded.Single() );
		hud.Stack.Update( Context(), UiInput.Key( UiKeys.Back ) );
		Assert.AreSame( pause, hud.Stack.Top );
	}
}

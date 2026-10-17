using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTPW.Hud;
using OpenTPW.UI.Original;

namespace OpenTPW.Tests;

/// <summary>
/// The buy list's pointer rules (tp.exe list control, attribute 0x91 includes 0x80): a pointer move over a row selects it (0x6656a0 → 0x66525c), the stats follow at once and the
/// preview 500 ms later (0x4ac9e8, 0x4ac42e), leaving the list keeps the selection, and a left click posts 0x400, which buys (0x6655a2, 0x4ac4f9).
/// </summary>
[TestClass]
[DoNotParallelize]
public class BuyListHoverTests
{
	private sealed class CountingStatus : IHudParkStatus
	{
		public int AvailabilityChecks;
		public bool HasEconomy => false;
		public long Money => 0;
		public ParkDate Date => default;
		public GameSpeed Speed { get; set; } = GameSpeed.Normal;
		public float TimeScale => 1;
		public void Update( float realSeconds ) { }
		public long? PriceOf( BuildItem item ) => null;
		public bool IsAvailable( BuildItem item ) { AvailabilityChecks++; return true; }
	}

	private sealed class Catalog( int count ) : IBuildCatalog
	{
		public IReadOnlyList<BuildItem> GetItems( BuildCategory category ) => category == BuildCategory.Rides
			? Enumerable.Range( 1, count ).Select( id => new BuildItem( $"r{id:00}", id, BuildCategory.Rides, id, id * 100, null, Array.Empty<string>() ) ).ToArray()
			: Array.Empty<BuildItem>();
	}

	private static void Set( object o, string field, object? value ) => o.GetType().GetField( field, BindingFlags.Instance | BindingFlags.NonPublic )!.SetValue( o, value );

	private static (ParkHud Hud, UiScreen Screen, UiElement List, UiContext Context, CountingStatus Status) Build( int items )
	{
		var hud = VisitorWorldSelectionTests.Fixture( out _, out _ );
		var status = new CountingStatus();
		Set( hud, "<Status>k__BackingField", status );
		Set( hud, "buy", new BuyListModel( new Catalog( items ), item => item.Id, item => item.Cost ) );
		var type = typeof( ParkHud ).GetNestedType( "BuyItemList", BindingFlags.NonPublic )!;
		var list = (UiElement)Activator.CreateInstance( type, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, new object[] { hud }, null )!;
		list.Bounds = new UiRect( 1009, 179, 804, 723 );
		list.Anchor = UiAnchor.Center;
		var screen = new UiScreen( "buy" );
		screen.Add( list );
		var context = new UiContext( OriginalUiTests.FakeStrings(), null!, new UiModels( name => throw new System.IO.FileNotFoundException( name ) ) ) { Canvas = new UiCanvas( 1024, 768 ) };
		hud.SelectBuyItem( 0 );
		return (hud, screen, list, context, status);
	}

	/// <summary>The centre of the visible row <paramref name="slot"/> of the list's content region.</summary>
	private static Vector2 Row( UiContext context, UiElement list, int slot )
	{
		var content = context.Canvas.Map( new UiRect( 1037, 427, 690, 444 ), list.Anchor );
		return new Vector2( content.X + content.Width / 2, content.Y + (slot + 0.5f) * ParkHud.BuyRowHeight * context.Canvas.Scale );
	}

	[TestMethod]
	public void HoveringARowSelectsItAndTheStatsFrameFollowsAtOnce()
	{
		var (hud, screen, list, context, _) = Build( 6 );
		screen.Update( context, UiInput.Idle( new Vector2( 5, 5 ) ) );
		screen.Update( context, UiInput.Idle( Row( context, list, 3 ) ) );
		Assert.AreEqual( 3, hud.SelectedBuyIndex, "the pointer selects the row under it" );
		Assert.AreEqual( "r04", hud.SelectedBuyItem!.Id, "the stats frame reads the selection, so it follows at once" );
		screen.Update( context, UiInput.Idle( Row( context, list, 1 ) ) );
		Assert.AreEqual( 1, hud.SelectedBuyIndex );
	}

	[TestMethod]
	public void ThePreviewFollowsTheSelectionOnlyAfterItHasRestedFor500Milliseconds()
	{
		var (hud, _, _, _, _) = Build( 6 );
		hud.UpdateBuyPreview( 10f );
		Assert.IsNull( hud.BuyPreviewItem, "the preview object is made when the selection has rested, not at once" );
		hud.UpdateBuyPreview( 10.4f );
		Assert.IsNull( hud.BuyPreviewItem );
		hud.UpdateBuyPreview( 10.6f );
		Assert.AreEqual( "r01", hud.BuyPreviewItem!.Id );
		hud.SelectBuyItem( 4 );
		hud.UpdateBuyPreview( 11f );
		Assert.AreEqual( "r01", hud.BuyPreviewItem!.Id, "the old preview keeps turning while the new selection settles" );
		hud.SelectBuyItem( 2 );
		hud.UpdateBuyPreview( 11.4f );
		hud.UpdateBuyPreview( 11.8f );
		Assert.AreEqual( "r01", hud.BuyPreviewItem!.Id, "a change restarts the wait" );
		hud.UpdateBuyPreview( 12f );
		Assert.AreEqual( "r03", hud.BuyPreviewItem!.Id );
	}

	[TestMethod]
	public void LeavingTheListOrHoveringBelowTheLastRowKeepsTheSelection()
	{
		var (hud, screen, list, context, _) = Build( 4 );
		screen.Update( context, UiInput.Idle( new Vector2( 5, 5 ) ) );
		screen.Update( context, UiInput.Idle( Row( context, list, 2 ) ) );
		Assert.AreEqual( 2, hud.SelectedBuyIndex );
		screen.Update( context, UiInput.Idle( Row( context, list, 7 ) ) );
		Assert.AreEqual( 2, hud.SelectedBuyIndex, "a row past the last one selects nothing" );
		screen.Update( context, UiInput.Idle( new Vector2( 5, 5 ) ) );
		Assert.AreEqual( 2, hud.SelectedBuyIndex, "the selection stays on the last hovered row when the pointer leaves" );
		screen.Update( context, UiInput.Idle( new Vector2( -1, -1 ) ) );
		Assert.AreEqual( 2, hud.SelectedBuyIndex );
	}

	[TestMethod]
	public void TheArrowKeysKeepTheirSelectionUnderARestingPointer()
	{
		var (hud, screen, list, context, _) = Build( 15 );
		var pointer = Row( context, list, 9 );
		screen.Update( context, UiInput.Idle( new Vector2( 5, 5 ) ) );
		screen.Update( context, UiInput.Idle( pointer ) );
		Assert.AreEqual( 9, hud.SelectedBuyIndex );
		screen.Update( context, UiInput.Idle( pointer ) with { Pressed = UiKeys.Down } );
		screen.Update( context, UiInput.Idle( pointer ) with { Pressed = UiKeys.Down } );
		Assert.AreEqual( 11, hud.SelectedBuyIndex, "the keys move the selection, and the rows that scroll under the pointer do not take it back" );
		screen.Update( context, UiInput.Idle( pointer ) );
		Assert.AreEqual( 11, hud.SelectedBuyIndex );
		screen.Update( context, UiInput.Idle( pointer + new Vector2( 1, 0 ) ) );
		Assert.AreEqual( hud.BuyFirstRow + 9, hud.SelectedBuyIndex, "any pointer move, also within the same row, selects the row under it again" );
		screen.Update( context, UiInput.Idle( Row( context, list, 4 ) ) );
		Assert.AreEqual( hud.BuyFirstRow + 4, hud.SelectedBuyIndex, "moving to another row selects it" );
	}

	[TestMethod]
	public void ALeftClickBuysTheRowWithoutAPriorHoverAndARightClickDoesNot()
	{
		var (hud, screen, list, context, status) = Build( 5 );
		var point = Row( context, list, 2 );
		screen.Update( context, new UiInput( point, true, true, false, false, UiKeys.None ) );
		screen.Update( context, UiInput.Click( point ) );
		Assert.AreEqual( 2, hud.SelectedBuyIndex, "the click selects the row" );
		Assert.AreEqual( 1, status.AvailabilityChecks, "and starts the purchase at once (0x400), without a second click" );
		var rows = (IUiRowList)list;
		Assert.IsFalse( rows.RowClicked( 3, left: false ), "the other button posts 0x402, which the window ignores, so the screen backs out" );
		Assert.AreEqual( 1, status.AvailabilityChecks );
	}

	[TestMethod]
	public void TheLoansListSelectsOnHoverToo()
	{
		var economy = EconomyTestData.Park( initialCash: 100_000_000 );
		var hud = StaffWindowTests.Hud( economy );
		foreach ( var offer in economy.Settings.Loans.Take( 2 ) )
			Assert.IsNotNull( economy.TakeLoan( offer.Index ) );
		hud.OpenLoansWindow();
		typeof( ParkHud ).GetMethod( "SelectLoanTab", BindingFlags.Instance | BindingFlags.NonPublic )!.Invoke( hud, new object[] { true } );
		Assert.IsTrue( hud.LoansWindowOpen );
		var screen = hud.Stack.Top!;
		var list = (UiElement)screen.Find( "loanList" )!;
		var rows = (IUiRowList)list;
		var context = new UiContext( OriginalUiTests.FakeStrings(), null!, new UiModels( name => throw new System.IO.FileNotFoundException( name ) ) ) { Canvas = new UiCanvas( 1024, 768 ) };
		var content = list.ScreenRect( context.Canvas );
		var second = new Vector2( content.X + 20, content.Y + 1.5f * ParkHud.BuyRowHeight * context.Canvas.Scale );
		screen.Update( context, UiInput.Idle( new Vector2( 5, 5 ) ) );
		screen.Update( context, UiInput.Idle( second ) );
		Assert.AreEqual( 1, rows.Selected, "the pointer selects the loan row under it" );
		screen.Update( context, UiInput.Idle( new Vector2( 5, 5 ) ) );
		Assert.AreEqual( 1, rows.Selected, "leaving keeps it" );
	}
}

using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTPW.Hud;
using OpenTPW.UI.Original;

namespace OpenTPW.Tests;

/// <summary>The loans window of tp.exe's table 0x7525a0 (built by 0x49fb30, list rows 0x4a02b0, list class 0x663718 / 0x66403b).</summary>
[TestClass]
public class LoansWindowTests
{
	private static ParkHud.LoanRow Row( int slot, int lender, long amount = 1000, int months = 12 ) =>
		new( slot, lender, amount, 10, months, 100, 1200 );

	[TestMethod]
	public void TheListSharesTheBuyListPitchAndShowsTheRowsThatFit()
	{
		Assert.AreEqual( ParkHud.BuyRowHeight, 41.4f );
		// (704 - 387) / 41.4 = 7.66: seven whole rows, as (704 - 387) / 41 at 640x480
		Assert.AreEqual( 7, ParkHud.LoanVisibleRows );
		var list = OriginalLayoutTables.LoansWindow.Control( 0x12C4BD );
		Assert.AreEqual( new UiRect( 326, 387, 1378, 317 ), list.Inner );
		Assert.AreEqual( 0x91u, list.Attributes, "header sort (0x10) like the buy list" );
	}

	[TestMethod]
	public void RowsSortByLenderNameOrdinallyAndKeepSlotOrderForEqualKeys()
	{
		var names = new[] { "beta", "Alpha", "beta", "alpha" };
		var rows = Enumerable.Range( 0, 4 ).Select( slot => Row( slot, slot ) ).ToArray();
		string Lender( ParkHud.LoanRow row ) => names[row.LenderNameIndex];
		var ascending = ParkHud.SortLoanRows( rows, 0, false, Lender ).Select( row => row.OfferIndex ).ToArray();
		CollectionAssert.AreEqual( new[] { 1, 3, 0, 2 }, ascending, "wcscmp order: upper case first, the two 'beta' in slot order" );
		var descending = ParkHud.SortLoanRows( rows, 0, true, Lender ).Select( row => row.OfferIndex ).ToArray();
		CollectionAssert.AreEqual( new[] { 0, 2, 3, 1 }, descending, "equal keys keep slot order in either direction" );
	}

	[TestMethod]
	public void NumericColumnsSortAsNumbersWithStableTies()
	{
		var rows = new[] { Row( 0, 0, 5000, 24 ), Row( 1, 1, 1000, 12 ), Row( 2, 2, 5000, 12 ) };
		CollectionAssert.AreEqual( new[] { 1, 0, 2 }, ParkHud.SortLoanRows( rows, 1, false, _ => "" ).Select( row => row.OfferIndex ).ToArray() );
		CollectionAssert.AreEqual( new[] { 0, 2, 1 }, ParkHud.SortLoanRows( rows, 1, true, _ => "" ).Select( row => row.OfferIndex ).ToArray() );
		CollectionAssert.AreEqual( new[] { 1, 2, 0 }, ParkHud.SortLoanRows( rows, 2, false, _ => "" ).Select( row => row.OfferIndex ).ToArray() );
	}

	[TestMethod]
	public void SummaryRowsFollowTheTableOrder()
	{
		var table = OriginalLayoutTables.LoansWindow;
		CollectionAssert.AreEqual( new[] { UIStrings.CashIn, UIStrings.LoanPayments, UIStrings.OtherCosts, UIStrings.Balance },
			ParkHud.LoanSummaryRows.Select( row => row.Text ).ToArray() );
		var tops = ParkHud.LoanSummaryRows.Select( row => table.Control( row.Label ).Rect.Y ).ToArray();
		CollectionAssert.AreEqual( new[] { 748f, 799f, 850f, 902f }, tops );
		foreach ( var row in ParkHud.LoanSummaryRows )
			Assert.AreEqual( table.Control( row.Label ).Rect.Y, table.Control( row.Value ).Rect.Y, "label and value share a line" );
		var summary = (CashIn: 10L, OtherCosts: 20L, LoanPayments: 30L, Balance: 40L);
		CollectionAssert.AreEqual( new[] { 10L, 30L, 20L, 40L },
			ParkHud.LoanSummaryRows.Select( row => ParkHud.LoanSummaryValue( summary, row.Text ) ).ToArray() );
	}

	[TestMethod]
	public void TheTableGivesTheButtonsAndTheirHelp()
	{
		var table = OriginalLayoutTables.LoansWindow;
		Assert.AreEqual( "window4", table.Root.Model );
		Assert.AreEqual( 220, table.Model( "b_sloan" ).Help );
		Assert.AreEqual( 221, table.Model( "b_spayup" ).Help );
		Assert.AreEqual( 235, table.Model( "b_finance" ).Help );
		Assert.AreEqual( 236, table.Model( "b_entry" ).Help );
		Assert.AreEqual( 237, table.Model( "b_staffcost" ).Help );
		Assert.AreEqual( new UiRect( 1730, 562, 67, 67 ), table.Model( "b_scroller" ).Rect );
	}

	[TestMethod]
	public void TheScrollBallUsesTheSliderRule()
	{
		var track = OriginalLayoutTables.LoansWindow.All.First( node => node.Model2 == "!slider" ).Rect;
		var knob = OriginalLayoutTables.LoansWindow.Model( "b_scroller" ).Rect;
		Assert.AreEqual( 335f, ParkHud.ListScrollKnobRect( track, knob, 0, 3 ).Y );
		Assert.AreEqual( 335f + 227 * 2 / 3, ParkHud.ListScrollKnobRect( track, knob, 2, 3 ).Y, "integer table units" );
		Assert.AreEqual( 335f + 227, ParkHud.ListScrollKnobRect( track, knob, 3, 3 ).Y );
		Assert.IsFalse( ParkHud.BuyScrollShown( 0 ), "a list that fits hides its scroller (0x664495)" );
	}
}

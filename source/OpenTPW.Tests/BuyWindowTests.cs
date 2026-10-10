using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTPW.Hud;

namespace OpenTPW.Tests;

/// <summary>The buy window's list state and the HUD cash change (the window itself is exercised by the native front-end smoke test).</summary>
[TestClass]
public class BuyWindowTests
{
	private sealed class FakeCatalog : IBuildCatalog
	{
		private readonly Dictionary<BuildCategory, BuildItem[]> items = new();

		public FakeCatalog()
		{
			items[BuildCategory.Rides] = Enumerable.Range( 0, 14 ).Select( index => Item( 100 + index, BuildCategory.Rides, 1000 - index * 10 ) ).ToArray();
			items[BuildCategory.Shops] = new[] { Item( 300, BuildCategory.Shops, 650 ), Item( 301, BuildCategory.Shops, 500 ) };
		}

		public IReadOnlyList<BuildItem> GetItems( BuildCategory category ) => items.TryGetValue( category, out var list ) ? list : Array.Empty<BuildItem>();

		private static BuildItem Item( int id, BuildCategory category, long cost ) => new( $"t/{id}", id, category, id, cost, null, Array.Empty<string>() );
	}

	private static BuyListModel Model() => new( new FakeCatalog(), item => $"Item {item.InfoId % 7}-{item.InfoId}", item => item.Cost );

	[TestMethod]
	public void CategoriesListTheirItemsAndSelectTheFirst()
	{
		var model = Model();
		model.EnsureSelection();
		Assert.AreEqual( BuildCategory.Rides, model.Category );
		Assert.AreEqual( 14, model.Items.Count );
		// The list starts in the original's insert order: by name, ordinal ("Item 0-105" first).
		var names = model.Items.Select( item => $"Item {item.InfoId % 7}-{item.InfoId}" ).ToArray();
		CollectionAssert.AreEqual( names.OrderBy( name => name, StringComparer.Ordinal ).ToArray(), names );
		Assert.AreEqual( 105, model.SelectedItem!.InfoId );
		model.SelectCategory( BuildCategory.Shops );
		Assert.AreEqual( 2, model.Items.Count );
		Assert.AreEqual( 301, model.SelectedItem!.InfoId );
		model.SelectCategory( BuildCategory.Features );
		Assert.AreEqual( 0, model.Items.Count );
		Assert.IsNull( model.SelectedItem );
		Assert.AreEqual( -1, model.SelectedIndex );
	}

	[TestMethod]
	public void ScrollingIsClampedAndSelectionStaysVisible()
	{
		var model = Model();
		model.EnsureSelection();
		Assert.AreEqual( 4, model.MaxFirstRow );
		model.Scroll( 100 );
		Assert.AreEqual( 4, model.FirstRow );
		model.Scroll( -100 );
		Assert.AreEqual( 0, model.FirstRow );
		model.Select( 12 );
		Assert.AreEqual( 12, model.SelectedIndex );
		Assert.AreEqual( 12 - BuyListModel.VisibleRows + 1, model.FirstRow, "selecting below the view scrolls it into view" );
		model.Select( 1 );
		Assert.AreEqual( 1, model.FirstRow );
		model.Select( 999 );
		Assert.AreEqual( 13, model.SelectedIndex );
		model.SelectCategory( BuildCategory.Shops );
		Assert.AreEqual( 0, model.FirstRow );
		Assert.AreEqual( 0, model.MaxFirstRow );
	}

	[TestMethod]
	public void HeaderSortsByNameAndPriceAndReverses()
	{
		var model = Model();
		model.EnsureSelection();
		model.SortBy( BuySortColumn.Price );
		CollectionAssert.AreEqual( model.Items.Select( item => item.Cost ).OrderBy( cost => cost ).ToArray(), model.Items.Select( item => item.Cost ).ToArray() );
		model.SortBy( BuySortColumn.Price );
		Assert.IsTrue( model.SortDescending );
		Assert.AreEqual( 1000, model.Items[0].Cost );
		model.SortBy( BuySortColumn.Name );
		Assert.IsFalse( model.SortDescending );
		var names = model.Items.Select( item => $"Item {item.InfoId % 7}-{item.InfoId}" ).ToArray();
		CollectionAssert.AreEqual( names.OrderBy( name => name, StringComparer.Ordinal ).ToArray(), names );
		Assert.IsNotNull( model.SelectedItem, "sorting keeps a selection" );
	}

	[TestMethod]
	public void EnsureSelectionKeepsAListedItem()
	{
		var model = Model();
		model.EnsureSelection();
		model.Select( 5 );
		var chosen = model.SelectedItem;
		model.EnsureSelection();
		Assert.AreSame( chosen, model.SelectedItem );
	}

	[TestMethod]
	public void CashChangeSumsWithinItsWindowAndExpires()
	{
		var tracker = new CashChangeTracker( 1000 );
		tracker.Update( 1000, 1 );
		Assert.AreEqual( 0, tracker.Change, "no change at the start" );
		tracker.Update( 1250, 0.1f );
		Assert.AreEqual( 250, tracker.Change );
		tracker.Update( 1200, 1 );
		Assert.AreEqual( 200, tracker.Change, "changes inside the window are summed (250 - 50)" );
		tracker.Update( 1200, CashChangeTracker.Seconds - 0.5f );
		Assert.AreEqual( 200, tracker.Change );
		tracker.Update( 1200, 1 );
		Assert.AreEqual( 0, tracker.Change, "the change disappears after its time" );
		tracker.Update( 1000, 0.1f );
		Assert.AreEqual( -200, tracker.Change, "a decrease is negative and starts a new sum" );
	}
}

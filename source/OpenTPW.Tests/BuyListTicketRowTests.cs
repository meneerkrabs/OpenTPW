using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTPW.Hud;
using OpenTPW.UI.Original;

namespace OpenTPW.Tests;

/// <summary>UI-105: the buy list's golden-ticket rows (tp.exe 0x4ab034 row filler, 0x490650 price cell, 0x4d4b70 owned set).</summary>
[TestClass]
public class BuyListTicketRowTests
{
	private static ParkEconomy ParkWithTicketShop( out int infoId )
	{
		var shop = EconomyTestData.Catalog().Objects.First( info => info.Kind == ParkObjectKind.Shop );
		infoId = shop.InfoId;
		var catalog = new EconomyObjectCatalog( EconomyTestData.Catalog().Objects.Select( info => info.InfoId == shop.InfoId ? info with { GoldenTicketCost = 2 } : info ) );
		return new ParkEconomy( EconomyTestData.Settings(), catalog, ParkGameMode.FullSimulation, 7 );
	}

	[TestMethod]
	public void ARowShowsTicketsUntilTheTypeIsBoughtWithThem()
	{
		var park = ParkWithTicketShop( out var infoId );
		Assert.IsTrue( park.Settings.CanSpendTickets );
		Assert.AreEqual( 2, park.ListedTicketCost( infoId ), "a type with GoldenTicketCost 2 that was not bought with tickets lists its ticket cost" );
		Assert.IsNull( park.ListedTicketCost( EconomyTestData.Catalog().Objects.First( info => info.InfoId != infoId ).InfoId ), "a cash type lists no tickets" );
		park.RestoreTicketItems( new[] { infoId } );
		Assert.IsNull( park.ListedTicketCost( infoId ), "once the set holds the type the row falls back to its name and cash price (0x4d4b70)" );
	}

	[TestMethod]
	public void TheTicketPriceReadsAsGoldenTicketsAndSortsBelowEveryCashPrice()
	{
		var strings = new UiStringTable( "test", new string[460], new string[1], new string[1], new string[1],
			uiTemplates: Enumerable.Range( 0, 460 ).Select( index => index == UiTextRecord.GoldenTicketPrice
				? new StringTemplate( new[] { new StringTemplatePart( "", -1 ), new StringTemplatePart( "", 23 ), new StringTemplatePart( " gt", -1 ) } )
				: new StringTemplate( new[] { new StringTemplatePart( "", -1 ) } ) ).ToArray() );
		Assert.AreEqual( "3 gt", ParkHud.TicketPriceText( strings, 3 ) );
	}

	private sealed class TicketStatus( int ticketRowId ) : IHudParkStatus
	{
		public bool HasEconomy => false;
		public long Money => 0;
		public ParkDate Date => default;
		public GameSpeed Speed { get; set; } = GameSpeed.Normal;
		public float TimeScale => 1;
		public void Update( float realSeconds ) { }
		public long? PriceOf( BuildItem item ) => null;
		public int? TicketCostOf( BuildItem item ) => item.InfoId == ticketRowId ? 3 : null;
		public bool IsAvailable( BuildItem item ) => true;
	}

	private static readonly BuildItem Mystery = new( "jungle/100", OriginalBuildCatalog.MysteryRideInfoId, (BuildCategory)4, -1, 0, "/levels/jungle/rides/mystery/Mystery.MD2", Array.Empty<string>() );

	private sealed class TicketCatalog : IBuildCatalog
	{
		public IReadOnlyList<BuildItem> GetItems( BuildCategory category ) => category == BuildCategory.Rides
			? new[] { new BuildItem( "r1", 1, BuildCategory.Rides, 1, 100, "/r1.MD2", Array.Empty<string>() ), new BuildItem( "r2", 2, BuildCategory.Rides, 2, 200, "/r2.MD2", Array.Empty<string>() ) }
			: Array.Empty<BuildItem>();
		public BuildItem? MysteryPreview => Mystery;
	}

	private static void Set( object o, string field, object? value ) => o.GetType().GetField( field, BindingFlags.Instance | BindingFlags.NonPublic )!.SetValue( o, value );

	[TestMethod]
	public void AnUnboughtTicketRowPreviewsTheMysteryRideAndNotTheRideItHides()
	{
		var hud = VisitorWorldSelectionTests.Fixture( out _, out _ );
		Set( hud, "<Status>k__BackingField", new TicketStatus( 2 ) );
		var catalog = new TicketCatalog();
		Set( hud, "<Catalog>k__BackingField", catalog );
		Set( hud, "buy", new BuyListModel( catalog, item => item.Id, item => item.Cost ) );

		hud.SelectBuyItem( 0 );
		hud.UpdateBuyPreview( 1f );
		hud.UpdateBuyPreview( 2f );
		Assert.AreEqual( "r1", hud.BuyPreviewItem!.Id );
		Assert.IsFalse( hud.IsMysteryRow( hud.BuyPreviewItem ) );
		Assert.AreSame( hud.BuyPreviewItem, hud.BuyPreviewModelItem, "a cash row turns its own model" );

		hud.SelectBuyItem( 1 );
		hud.UpdateBuyPreview( 3f );
		hud.UpdateBuyPreview( 4f );
		Assert.AreEqual( "r2", hud.BuyPreviewItem!.Id );
		Assert.IsTrue( hud.IsMysteryRow( hud.BuyPreviewItem ), "a row with a ticket cost the player has not paid is a mystery row (0x4ab1b0)" );
		Assert.AreSame( Mystery, hud.BuyPreviewModelItem, "the preview turns the type-100 Mystery ride, so the surprise stays hidden" );
	}
}

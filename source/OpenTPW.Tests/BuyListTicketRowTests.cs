using System.Linq;
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
}

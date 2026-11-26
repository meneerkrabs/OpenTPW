namespace OpenTPW;

// The economy's side of a native Theme Park Inc guest's use of an attraction (docs/reverse/TPI-GUESTS.md, phase P3b): the bookings the use routine 0x6118D0 and its outcome 0x612740 make one by
// one, in their order, where the Theme Park World rules' boarding settles a visit in one step (SettleVisit).
public sealed partial class ParkEconomy
{
	/// <summary>The thing words a native guest's use reads: the price <c>+0x94</c>, the cost of goods <c>+0x88</c>, the quality byte <c>+0x8C</c>, the win byte <c>+0x90</c> and the amount byte <c>+0x98</c>.</summary>
	// [BIN:TPI-EXE:0x5DB55D thing words] the constructor stores type +0x1FC (InitCostOfGoods) at +0x88, 50 at +0x8C and +0x98, 100 - type +0x19C (InitChanceOfLoosing) at +0x90 and type +0x194
	// (InitPricePerUse) at +0x94; the shop and sideshow windows' settings change them (ParkObjectState)
	internal ThemeParkIncThingWords? ThemeParkIncThingWordsOf( int instance ) =>
		objects.TryGetValue( instance, out var item )
			? new ThemeParkIncThingWords( item.Price, item.CostOfGoods, item.QualityOfGoods, (100 - item.ChanceOfLosingPercent) & 0xFF, item.IngredientAmount )
			: null;

	/// <summary>The use's income booking (<c>0x5E5DE0</c>, from the payment step <c>0x6126D0</c>): the bank takes the fee at once and the object's takings rise; a shop and a sideshow post their sale events.</summary>
	// [BIN:TPI-EXE:0x5E5DE0 income booking] the bank 0x64EBE0 -> 0x5C0760 (balance += fee), the history object's takings +0x0 and +0x6E8; a shop (+0x600 1) adds it to the analyser's +0x20398 and
	// posts message 0xC and the shop-type events (PostThemeParkIncTakingsEvents)
	internal void ThemeParkIncTakings( int instance, int fee )
	{
		if ( !objects.TryGetValue( instance, out var item ) || fee == 0 )
			return;
		switch ( item.Kind )
		{
			case ParkObjectKind.Shop:
				Post( LedgerCategory.ShopTakings, fee );
				if ( Catalog.TryGet( item.InfoId, out var info ) )
					Counters.Add( ParkCounters.Sold( info.ShopType, info.SpecialIngredient ), 1 );
				Counters.Add( ParkCounters.ShopProfit, fee );
				break;
			case ParkObjectKind.Sideshow:
				Post( LedgerCategory.SideshowTakings, fee );
				Counters.Add( ParkCounters.SideshowProfit, fee );
				break;
			default:
				BookRideFare( item, fee );
				return;
		}
		item.TakingsThisMonth += fee;
		item.TakingsRing.Today += fee;
		item.TotalProfit += fee;
		PostThemeParkIncTakingsEvents( item, fee );
		Raise( ParkEventKind.GuestPaid, fee, item.Id, item.InfoId, item.Kind == ParkObjectKind.Shop ? nameof( LedgerCategory.ShopTakings ) : nameof( LedgerCategory.SideshowTakings ) );
	}

	/// <summary>The cost booking <c>0x5E60E0</c> of a use's goods (a shop, <c>0x5E63E0</c>) or prize (a won sideshow, <c>+0x88</c>): the daily cost ring and the month accumulator, no debit (ECON-060).</summary>
	// [BIN:TPI-EXE:0x5E60E0 cost booking] raises the daily cost ring (+0x88) and the month accumulator (+0x938) and posts the negated cost as message 0xC (a shop) or 0xD (a sideshow)
	internal void ThemeParkIncCostBooked( int instance, int amount )
	{
		if ( !objects.TryGetValue( instance, out var item ) )
			return;
		item.CostsRing.Today += amount;
		item.PendingMonthlyCosts += amount;
		Counters.Add( item.Kind == ParkObjectKind.Sideshow ? ParkCounters.SideshowProfit : ParkCounters.ShopProfit, -amount );
		PostThemeParkIncCostEvent( item, amount );
	}

	/// <summary>A shop's cost of goods for one sale (<c>0x5E63E0</c>, <see cref="ThemeParkIncGoodsCost"/>); 0 for an unknown object.</summary>
	internal int ThemeParkIncGoodsCostOf( int instance ) =>
		objects.TryGetValue( instance, out var item ) && Catalog.TryGet( item.InfoId, out var info )
			? ThemeParkIncGoodsCost.Of( item.CostOfGoods, item.QualityOfGoods, item.IngredientAmount, ThemeParkIncGoodsCost.BuiltThirdByte, info.SpecialIngredient, info.ShopType )
			: 0;
}

/// <summary>The words of an attraction thing a native Theme Park Inc guest's use reads (<see cref="ParkEconomy.ThemeParkIncThingWordsOf"/>).</summary>
public readonly record struct ThemeParkIncThingWords( int Price, int CostOfGoods, int Quality, int WinByte, int Amount );

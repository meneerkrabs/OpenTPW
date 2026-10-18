namespace OpenTPW;

// The host side of the winter extension (docs/WINTER.md, phase W3): the winter kiosk's and the glühwein stall's score, and a purchase at the kiosk. Every rule starts from a winter shop or a guest who wears a warm item, so a park
// without either runs exactly as it did without the extension (nothing here draws from a random stream or changes a hash then). The cold rule (W5) and the drawing of the items (W4) are not part of this.
public sealed partial class GuestSimulation
{
	/// <summary>
	/// [EXT:winter-kiosk-gate] The calendar month (1 to 12) of the park's date, for the winter shops' season terms and the kiosk's gate (<see cref="WinterRules"/>); 0 (the default, and no source) is no month at all, so the kiosk
	/// sells only in snow and the season percentages do not apply. The level reads it from the park's clock; nothing but the winter shops reads it. The weather comes from <see cref="UmbrellaWeatherState"/>.
	/// </summary>
	public Func<int>? WinterMonth { get; set; }

	/// <summary>The winter shops registered (maintained by <see cref="Register"/> and <see cref="Unregister"/>): the park uses the winter extension while there is one.</summary>
	private int winterShops;

	/// <summary>Whether the park uses the winter extension: it has a winter shop or a guest wearing a warm item or still warmed by a glühwein (a save then records the extension, <see cref="ParkResumeMods.WinterInUse"/>). A scan of the guests, made when a save is written.</summary>
	internal bool UsesWinter => winterShops > 0 || Guests.Any( guest => guest.HoldsWinter );

	/// <summary>Sets a guest's warm items the way a test needs them.</summary>
	internal void SetWarmItemsForTest( Guest guest, int items ) => guest.WarmItems = items;

	/// <summary>
	/// What the host does to the policy's score of a winter shop for <paramref name="guest"/> (the decision interface is untouched): the kiosk scores 0 for a guest whose two slots are full and outside snow and the months December to
	/// February (<see cref="WinterRules.KioskScoresZero"/>); both shops then take the weather's and the season's percentage of their .sam (<see cref="WinterRules.ApplyTerms"/>). The stall has no gate: thirst drives it as any drink.
	/// </summary>
	// [EXT:winter-kiosk-gate] the kiosk sells only in snow or from December to February, and not to a guest who has both a head item and mittens
	// [EXT:winter-season-terms] the weather percentages (Theme Park Inc's names) and the extension's own season percentages of the object's .sam, for the extension's objects only
	private uint WinterHostScore( Guest guest, IRideVisitorBridge attraction, uint score, in WinterShopTerms terms )
	{
		var weather = UmbrellaWeatherState?.Invoke() ?? UmbrellaRules.WeatherNone;
		var month = WinterMonth?.Invoke() ?? 0;
		if ( attraction.IsWinterKiosk && WinterRules.KioskScoresZero( guest.WarmItems, weather, month ) )
			return 0;
		return WinterRules.ApplyTerms( score, weather, month, terms );
	}

	/// <summary>
	/// A purchase at a winter kiosk: the guest gets its head item (a hat or earmuffs, by a hash of its id) first and mittens next; a guest whose slots are full (it bought in the queue's time, the score gate only keeps it from
	/// choosing a shop) gets nothing more. The colours are a function of the guest id and the item, so a purchase takes no draw from any stream.
	/// </summary>
	// [EXT:winter-items] one kiosk that gives a head item first, then mittens (decision of October 18, 2026); [EXT:winter-item-colour] no random draw: the head item and the colours are hashes of the guest id
	internal void GiveWarmItem( Guest guest )
	{
		var item = WinterRules.NextItem( guest.WarmItems, guest.Id );
		if ( item == 0 )
			return;
		guest.WarmItems |= item;
	}
}

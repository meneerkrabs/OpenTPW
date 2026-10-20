namespace OpenTPW;

// The host side of the winter extension (docs/WINTER.md, phase W3): the winter kiosk's and the glühwein stall's score, and a purchase at the kiosk. Every rule starts from a winter shop or a guest who wears a warm item, so a park
// without either runs exactly as it did without the extension (nothing here draws from a random stream or changes a hash then). The cold rule (W5) and the drawing of the items (W4) are not part of this.
public sealed partial class GuestSimulation
{
	/// <summary>
	/// [EXT:winter-season-terms] The calendar month (1 to 12) of the park's date, for the winter shops' season terms (<see cref="WinterRules.ApplyTerms"/>); 0 (the default, and no source) is no month at all, so the season percentages
	/// do not apply. The level reads it from the park's clock; nothing but the winter shops reads it.
	/// </summary>
	public Func<int>? WinterMonth { get; set; }

	/// <summary>
	/// [EXT:winter-always] Whether the always winter option is on: the winter shops' season terms read winter whatever the calendar month (<see cref="WinterRules.DemandMonthOf"/>). The level sets it from
	/// <see cref="FeatureGates.WinterAlways"/>; off (the default) the month is <see cref="WinterMonth"/>'s.
	/// </summary>
	public bool WinterAlways { get; set; }

	/// <summary>
	/// [EXT:winter-season-terms] The weather as the winter shops read it (<see cref="WinterRules.WeatherStateOf"/>: 2 rain, 3 storm, 4 snow, anything else, or null, none). Unlike <see cref="UmbrellaWeatherState"/> it reads snow as snow
	/// even while lightning flashes, so a Halloween snowfall (whose snow band is its lightning band) is snow for the shops. The umbrellas and the rain mood keep the shared map. Null falls back to <see cref="UmbrellaWeatherState"/>.
	/// </summary>
	public Func<int>? WinterWeatherState { get; set; }

	/// <summary>Test hook: the winter shops registered (maintained by <see cref="Register"/> and <see cref="Unregister"/>); nothing in the game reads it, the save rule is <see cref="ParkResumeMods.WinterInUse"/>.</summary>
	private int winterShops;

	/// <summary>Test hook: whether the park uses the winter extension by the guest simulation's own count, a winter shop or a guest wearing a warm item or still warmed by a glühwein. The game's save rule is <see cref="ParkResumeMods.WinterInUse"/>, which reads the placed objects.</summary>
	internal bool UsesWinter => winterShops > 0 || Guests.Any( guest => guest.HoldsWinter );

	/// <summary>Test hook: whether a winter kiosk stops counting as recently used for a guest with a free slot (<see cref="WinterKioskReturn"/>); true always, a test sets it false to measure the game's own history rules as they were.</summary>
	internal bool WinterKioskReturnEnabled { get; set; } = true;

	/// <summary>
	/// The score of <paramref name="attraction"/> for <paramref name="guest"/>: the policy's score, then the host's winter terms (<see cref="HostScore"/>). For a winter kiosk and a guest whose slots are not full and who could pay
	/// the kiosk's price now, the guest's own views do not count a winter kiosk as recently used (<see cref="WinterKioskReturn"/>).
	/// </summary>
	// [EXT:winter-kiosk-return] user-approved 2026-10-19 ("Laten terugkomen"): a guest who still lacks an item does not find the kiosk "used recently" (the same-type-as-last rule that scores it 0, the used-history division by 5, 4, 3 and 2);
	// a guest the stand-point price test would turn away (its willingness or its money below the price) keeps the game's history rules, so it is not drawn back to be refused again and again. The test is the pure
	// QueueJoinRules.TooExpensive: a score records no price statistic (TooExpensive( Guest, ... ) at the stand point stays the only place that does)
	internal uint WinterScoreOf( in DecisionContext context, Guest guest, in GuestView view, IRideVisitorBridge attraction, in AttractionView candidate )
	{
		if ( WinterKioskReturnEnabled && attraction.IsWinterKiosk && !WinterRules.SlotsFull( guest.WarmItems )
			&& !QueueJoinRules.TooExpensive( PriceTermsOf( attraction ), guest.Happiness, guest.Thirst, guest.Hunger, guest.Nausea, guest.Money ) )
		{
			var (returnView, returnCandidate) = WinterKioskReturn( guest, view, candidate );
			return HostScore( guest, attraction, decisions.ScoreAttraction( context, returnView, returnCandidate ) );
		}
		return HostScore( guest, attraction, decisions.ScoreAttraction( context, view, candidate ) );
	}

	/// <summary>
	/// The views of a guest who lacks a winter item as they score the winter kiosk: every winter kiosk in the used history counts as an empty slot (so it neither divides the score nor is "the last thing used"), and the
	/// candidate is not of the type of the last thing used when that was a winter kiosk. The temporary history (a refusal at a queue that was too long or too exciting) and every other attraction's score are untouched.
	/// </summary>
	private (GuestView View, AttractionView Candidate) WinterKioskReturn( Guest guest, in GuestView view, in AttractionView candidate )
	{
		var returnView = view;
		var returnCandidate = candidate;
		int[]? used = null;
		for ( var slot = 0; slot < guest.UsedHistory.Length; slot++ )
		{
			if ( guest.UsedHistory[slot] == 0 || FindAttraction( guest.UsedHistory[slot] ) is not { IsWinterKiosk: true } )
				continue;
			used ??= (int[])guest.UsedHistory.Clone();
			used[slot] = 0;
			if ( slot == 0 )
				returnCandidate = returnCandidate with { SameTypeAsLastUsed = false };
		}
		if ( used != null )
			returnView = view with { UsedHistory = used };
		return (returnView, returnCandidate);
	}

	/// <summary>Sets a guest's warm items the way a test needs them.</summary>
	internal void SetWarmItemsForTest( Guest guest, int items ) => guest.WarmItems = items;

	/// <summary>
	/// What the host does to the policy's score of a winter shop for <paramref name="guest"/> (the decision interface is untouched): the kiosk scores 0 for a guest whose two slots are full (<see cref="WinterRules.KioskScoresZero"/>),
	/// and nothing else gates it (it sells all year); both shops then take the weather's and the season's percentage of their .sam (<see cref="WinterRules.ApplyTerms"/>). The stall has no gate: thirst drives it as any drink.
	/// </summary>
	// [EXT:winter-kiosk] the kiosk sells all year and its demand follows the season and the snow (the user's decision of 2026-10-18/19, "Het hele jaar, vraag via seizoen"); a guest with both a head item and mittens has nothing left to buy
	// [EXT:winter-season-terms] the weather percentages (Theme Park Inc's names) and the extension's own season percentages of the object's .sam, for the extension's objects only
	private uint WinterHostScore( Guest guest, IRideVisitorBridge attraction, uint score, in WinterShopTerms terms )
	{
		var weather = (WinterWeatherState ?? UmbrellaWeatherState)?.Invoke() ?? UmbrellaRules.WeatherNone;
		var month = WinterRules.DemandMonthOf( WinterMonth?.Invoke() ?? 0, WinterAlways );
		if ( attraction.IsWinterKiosk && WinterRules.KioskScoresZero( guest.WarmItems ) )
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

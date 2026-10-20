namespace OpenTPW;

/// <summary>
/// The multipliers of a winter shop's attraction score, read from its .sam for the winter extension's own objects only (docs/WINTER.md, "Demand"): Theme Park Inc's names for the weather,
/// <c>WeatherModifier.Rain/Storm/Snow</c> (0: no change), and the extension's own <c>SeasonModifier.Winter/Spring/Summer/Autumn</c> (0: no change), months December to February, March to May, June to August and September to November.
/// </summary>
// [EXT:winter-season-terms] OpenTPW's own season keys; the weather keys are Theme Park Inc's names (WeatherModifier.*Percentage, 0x6101e0) that tp.exe does not read
public readonly record struct WinterShopTerms( int RainPercentage, int StormPercentage, int SnowPercentage, int WinterPercentage, int SpringPercentage, int SummerPercentage, int AutumnPercentage )
{
	/// <summary>The percentage of weather state <paramref name="weather"/> (<see cref="UmbrellaRules.WeatherRain"/> to <see cref="UmbrellaRules.WeatherSnow"/>); 0 for none.</summary>
	public int WeatherPercentOf( int weather ) => weather switch
	{
		UmbrellaRules.WeatherRain => RainPercentage,
		UmbrellaRules.WeatherStorm => StormPercentage,
		UmbrellaRules.WeatherSnow => SnowPercentage,
		_ => 0
	};

	/// <summary>The percentage of the season of calendar month <paramref name="month"/> (1 to 12); 0 for none, and for a month outside 1 to 12.</summary>
	public int SeasonPercentOf( int month ) => WinterRules.SeasonOf( month ) switch
	{
		WinterSeason.Winter => WinterPercentage,
		WinterSeason.Spring => SpringPercentage,
		WinterSeason.Summer => SummerPercentage,
		WinterSeason.Autumn => AutumnPercentage,
		_ => 0
	};
}

/// <summary>The four seasons of the winter extension's demand terms (<see cref="WinterRules.SeasonOf"/>).</summary>
public enum WinterSeason
{
	/// <summary>Not a calendar month.</summary>
	None,
	Winter,
	Spring,
	Summer,
	Autumn
}

/// <summary>
/// The winter extension's rules as pure functions (docs/WINTER.md): the kiosk's one rule (a full guest scores 0) and the head item first, then mittens, the item colours and the percentages of both shops' scores. None of it is Theme Park World's behaviour
/// (it has neither the shops nor the items), so every rule here is labelled <c>[EXT:winter-*]</c>; the guest simulation applies them (<see cref="GuestSimulation"/>). Nothing here draws from any random stream.
/// </summary>
public static class WinterRules
{
	/// <summary><c>UsageInfo.AppearanceEffect</c> of a winter kiosk: the guest gets a warm item (1 is a balloon, 2 a costume and 3 an umbrella).</summary>
	// [EXT:winter-items] 1 and 2 are tp.exe's (0x4fe1e0 switch), 3 is the umbrellas extension's, 4 is this extension's
	public const int KioskAppearanceEffect = 4;

	/// <summary>The warm items of <see cref="Guest.WarmItems"/>: bits 1 hat, 2 earmuffs, 4 mittens. A guest has a head item (a hat or earmuffs, never both) and mittens.</summary>
	public const int Hat = 1, Earmuffs = 2, Mittens = 4;
	/// <summary>The two head items' bits.</summary>
	public const int HeadItems = Hat | Earmuffs;
	/// <summary>Every bit of <see cref="Guest.WarmItems"/> that is defined.</summary>
	public const int AllItems = Hat | Earmuffs | Mittens;

	/// <summary>The colours of each item, the sprite slots of the item bank (<c>content/winter/esprites</c>: slots 0 to 2 bobble hats, 3 and 4 earmuffs, 5 to 7 mittens).</summary>
	public const int HatColours = 3, EarmuffColours = 2, MittenColours = 3;

	// ---- Seasons ---------------------------------------------------------------------------------------------------

	/// <summary>The season of a calendar month (1 to 12): December to February winter, March to May spring, June to August summer, September to November autumn.</summary>
	// [EXT:winter-season-terms] the plan's months, OpenTPW's own grouping (tp.exe's own seasons are month / 3 with December a fifth row, ParkWeather.Roll)
	public static WinterSeason SeasonOf( int month ) => month switch
	{
		12 or 1 or 2 => WinterSeason.Winter,
		>= 3 and <= 5 => WinterSeason.Spring,
		>= 6 and <= 8 => WinterSeason.Summer,
		>= 9 and <= 11 => WinterSeason.Autumn,
		_ => WinterSeason.None
	};

	/// <summary>The month the always winter option makes every month for the winter shops' season terms: December, the month whose weather row the option's rolls use (<see cref="ParkWeather"/>).</summary>
	public const int AlwaysWinterMonth = 12;

	/// <summary>
	/// The month the winter shops' season terms read: the calendar <paramref name="month"/>, or <see cref="AlwaysWinterMonth"/> while the always winter option is on, so the kiosk and the stall sell at their winter demand all year.
	/// </summary>
	// [EXT:winter-always] user-approved 2026-10-19 ("Winterweer hele jaar"): the shops' season terms are winter's all year; their weather terms still follow the weather
	public static int DemandMonthOf( int month, bool alwaysWinter ) => alwaysWinter ? AlwaysWinterMonth : month;

	/// <summary>
	/// The weather as the winter shops read it: snow is snow whether or not lightning flashes, then lightning is a storm, then rain. It differs from <see cref="UmbrellaRules.WeatherStateOf"/> only for snow with lightning (the Halloween
	/// park, whose snow band is its lightning band), which the umbrellas and the rain mood read as a storm and the winter shops as the snowfall it is.
	/// </summary>
	// [EXT:winter-season-terms] the shops' own weather signal; the umbrellas' and the rain mood's map ([EXT:umbrellas-weather-map]) stays as it is
	public static int WeatherStateOf( bool rain, bool lightning, bool snow ) =>
		snow ? UmbrellaRules.WeatherSnow : lightning ? UmbrellaRules.WeatherStorm : rain ? UmbrellaRules.WeatherRain : UmbrellaRules.WeatherNone;

	// ---- The score -------------------------------------------------------------------------------------------------

	/// <summary>Whether a guest with <paramref name="items"/> has both slots filled: a head item and mittens.</summary>
	public static bool SlotsFull( int items ) => (items & HeadItems) != 0 && (items & Mittens) != 0;

	/// <summary>
	/// Whether the winter kiosk scores 0 for a guest: both of its slots are full, so it has nothing left to buy. This is the kiosk's only gate: it is no season or weather gate, the kiosk sells all year and its demand follows the
	/// <see cref="WinterShopTerms"/> (<see cref="ApplyTerms"/>).
	/// </summary>
	// [EXT:winter-kiosk] a guest whose slots are full has nothing left to buy; the earlier snow-or-December-to-February gate is retired: the user decided on 2026-10-18/19 that the kiosk sells all year and its demand follows the season ("Het hele jaar, vraag via seizoen")
	public static bool KioskScoresZero( int items ) => SlotsFull( items );

	/// <summary>
	/// The score after a winter shop's percentages: times the weather's percentage of the current weather when that is nonzero, then times the season's percentage of the calendar month when that is nonzero
	/// (<see cref="UmbrellaRules.Percent"/>'s signed 32-bit arithmetic, truncated toward zero, one step at a time).
	/// </summary>
	// [EXT:winter-season-terms] the weather step is Theme Park Inc's (0x6109d0: a zero percentage leaves the score), the season step is the extension's own twin of it
	public static uint ApplyTerms( uint score, int weather, int month, in WinterShopTerms terms )
	{
		var weatherPercent = terms.WeatherPercentOf( weather );
		if ( weatherPercent != 0 )
			score = UmbrellaRules.Percent( score, weatherPercent );
		var seasonPercent = terms.SeasonPercentOf( month );
		return seasonPercent == 0 ? score : UmbrellaRules.Percent( score, seasonPercent );
	}

	// ---- The items -------------------------------------------------------------------------------------------------

	/// <summary>The head item of a guest, from a hash of its id: a hat for two guests in three, earmuffs for the third.</summary>
	// [EXT:winter-items] about two hats to one pair of earmuffs; a pure function of the guest id, so a purchase takes no draw from the world or guest streams
	public static int HeadItemOf( int guestId ) => Mix( Seed( guestId, 0x48454144 ) ) % 3 < 2 ? Hat : Earmuffs;

	/// <summary>
	/// The item a purchase gives a guest who holds <paramref name="items"/>: the head item (<see cref="HeadItemOf"/>) first, then mittens; 0 once both slots are full (a guest who bought in the queue's time, after another shop
	/// filled the slots, gets nothing more).
	/// </summary>
	// [EXT:winter-items] one kiosk that gives a head item first, then mittens (decision of October 18, 2026); a guest never has two head items
	public static int NextItem( int items, int guestId ) =>
		(items & HeadItems) == 0 ? HeadItemOf( guestId ) : (items & Mittens) == 0 ? Mittens : 0;

	/// <summary>How many colours <paramref name="item"/> (<see cref="Hat"/>, <see cref="Earmuffs"/> or <see cref="Mittens"/>) has in the item bank; 0 for anything else.</summary>
	public static int ColoursOf( int item ) => item switch { Hat => HatColours, Earmuffs => EarmuffColours, Mittens => MittenColours, _ => 0 };

	/// <summary>
	/// The colour of a guest's <paramref name="item"/>, 0 to <see cref="ColoursOf"/> - 1: a hash of the guest id and the item. Derived, never saved, and the same whenever the item is drawn.
	/// </summary>
	// [EXT:winter-item-colour] a pure function of guest id and item (as the balloon's colour is of the guest id), so a purchase takes no extra random draw
	public static int ColourOf( int guestId, int item ) => ColoursOf( item ) is var colours and > 0 ? (int)(Mix( Seed( guestId, (uint)item ) ) % (uint)colours) : 0;

	/// <summary>The first sprite slot of each item in the item bank (<c>content/winter/esprites/Winter/SPR_WI</c>): bobble hats 0 to 2, earmuffs 3 and 4, mittens 5 to 7. 0 for anything that is not one item.</summary>
	// [EXT:winter-content] the slot order of tools/blender/opentpw_winter_items.py (docs/WINTER.md, "Content")
	public static int FirstSlotOf( int item ) => item switch { Hat => 0, Earmuffs => HatColours, Mittens => HatColours + EarmuffColours, _ => 0 };

	/// <summary>The sprite slot of a guest's <paramref name="item"/>: <see cref="FirstSlotOf"/> plus <see cref="ColourOf"/>. Frame 0 of a mitten slot is the right mitten and frame 1 the left.</summary>
	public static int SpriteSlotOf( int guestId, int item ) => FirstSlotOf( item ) + ColourOf( guestId, item );

	/// <summary>Whether <paramref name="items"/> is a valid <see cref="Guest.WarmItems"/>: only the defined bits, and at most one head item.</summary>
	public static bool IsValidItems( int items ) => items is >= 0 and <= AllItems && (items & HeadItems) != HeadItems;

	private static ulong Seed( int guestId, uint salt ) => (ulong)(uint)guestId << 32 ^ salt;

	private static int Mix( ulong value )
	{
		value += 0x9E3779B97F4A7C15UL;
		value = (value ^ (value >> 30)) * 0xBF58476D1CE4E5B9UL;
		value = (value ^ (value >> 27)) * 0x94D049BB133111EBUL;
		return (int)((value ^ (value >> 31)) >> 33);
	}
}

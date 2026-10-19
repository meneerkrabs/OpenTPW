namespace OpenTPW;

// The host side of the cold rule (docs/WINTER.md, phase W5): in snow a guest loses happiness once per qualifying day unless it is dressed warmly. Off by default: every guest's meters are then the needs step's alone.
public sealed partial class GuestSimulation
{
	/// <summary>
	/// [EXT:winter-cold] The cold rule's settings while the <c>opentpw.winter-cold</c> extension is on; null (the default) leaves every guest's happiness to the other rules. The level sets it when it builds the park
	/// (<see cref="FeatureGates.WinterCold"/>); the weather comes from the winter shops' own signal (<see cref="WinterWeatherState"/>, falling back to <see cref="UmbrellaWeatherState"/>).
	/// </summary>
	public WinterColdSettings? WinterCold { get; set; }

	/// <summary>[EXT:winter-cold] The park's calendar day (<see cref="ParkCalendar.DayIndex"/>) for the glühwein's warmth window; 0 (the default, and no source) is the first day. The level reads it from the park's clock.</summary>
	public Func<long>? WinterDay { get; set; }

	/// <summary>
	/// The cold step of one guest on its own update (every fourth park turn, after the rain mood step): in snow, on the first update of a qualifying day, the guest's happiness changes by the cold delta
	/// (<see cref="WinterColdRules.Apply"/>), softened by its head item, its mittens and a recent glühwein. Every guest is touched, queued or riding ones too, as the rain mood is.
	/// </summary>
	// [EXT:winter-season-terms] the cold reads the winter shops' weather (WinterRules.WeatherStateOf): snow is snow even with lightning, which is every Halloween snowfall, so the rule applies where the snow is
	// [EXT:winter-cold] same phase, same age and day gate as the rain mood (RainMoodAgeTurnsOf, UmbrellaRules.DayGateOpen), its own once-per-day mark (ColdMoodDay) so it runs with or without the rain mood
	private void UpdateWinterCold( Guest guest, WinterColdSettings settings )
	{
		var weather = (WinterWeatherState ?? UmbrellaWeatherState)?.Invoke() ?? UmbrellaRules.WeatherNone;
		var age = RainMoodAgeTurnsOf( guest );
		if ( !WinterColdRules.Applies( age, weather, guest.ColdMoodDay ) )
			return;
		var warmed = WinterColdRules.IsWarmed( guest.WarmUntilDay, WinterDay?.Invoke() ?? 0 );
		guest.Happiness = WinterColdRules.Apply( guest.Happiness, guest.WarmItems, warmed, settings );
		guest.ColdMoodDay = RainMoodRules.DayOf( age ) + 1;
	}

	/// <summary>A glühwein is drunk with the cold rule on: the guest counts as warmed for the rest of the day and the next <see cref="WinterColdSettings.WarmEffectDays"/> (<see cref="WinterColdRules.WarmUntilDayOf"/>). No random draw.</summary>
	// [EXT:winter-cold] "a recent glühwein"; a second drink extends the window, it never shortens it
	private void WarmByGluehwein( Guest guest )
	{
		var until = WinterColdRules.WarmUntilDayOf( WinterDay?.Invoke() ?? 0, WinterCold ?? WinterColdSettings.Default );
		guest.WarmUntilDay = Math.Max( guest.WarmUntilDay, until );
	}
}

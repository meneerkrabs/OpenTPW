namespace OpenTPW;

// The host side of the rain mood extension (docs/UMBRELLAS.md, phase U4): Theme Park Inc's weather rule for happiness, thirst and hunger, applied once per qualifying day. The umbrella states of the same TPI rule stay in
// GuestSimulation.Umbrellas.cs and run whether this is on or off.
public sealed partial class GuestSimulation
{
	/// <summary>
	/// [EXT:rain-mood-extension] The rain mood settings while the <c>opentpw.rain-mood</c> extension is on; null (the default) leaves every guest's meters to the needs step alone. The level sets it when it builds the park
	/// (<see cref="FeatureGates.RainMood"/>); the weather comes from <see cref="UmbrellaWeatherState"/>.
	/// </summary>
	public RainMoodSettings? RainMood { get; set; }

	/// <summary>
	/// The rain mood step of one guest on its own update (every fourth park turn, before the needs step): in rain, storm or snow, on the first update of a qualifying day, the guest's happiness, thirst and hunger get the
	/// generation row's deltas and an umbrella that was already open when the step began softens a happiness loss (<see cref="RainMoodRules.Apply"/>). Every guest is touched, queued or riding ones too.
	/// </summary>
	// [BIN:TPI-EXE:0x611050 weather rule] called from the guest update 0x619B50 for every guest, wherever it is; the day gate, the weather word +0x70 and the clamped sums are the original's
	// [EXT:winter-cold-replaces-snow-cheer] with opentpw.winter-cold on and the winter signal saying snow, the rain mood's happiness of that day is left to the cold rule, whichever row it would use: snow's +1 (user decision 2026-10-18, "Alleen de humeur-+1") or
	// Halloween's storm -1, as every Halloween snowfall has lightning (user decision 2026-10-19, "Kou vervangt storm-humeur"); thirst and hunger stay. With cold off nothing here changes.
	// [EXT:rain-mood-once-per-day] once per qualifying day instead of on every update of it: the original writes its day (+0x2E8) and never reads it, so the rule hits on every update of those days (about six a game day)
	// [BIN:TPI-EXE:0x611334 modifier] the umbrella modifier (0x6111BE to 0x6111D3) belongs to the state the guest has when the rule starts (the switch's case 4), so umbrellaWasOpen is read before the umbrella step turns 1 into 4 or a due 3 into 4
	private void UpdateRainMood( Guest guest, RainMoodSettings settings, bool umbrellaWasOpen )
	{
		var weather = UmbrellaWeatherState?.Invoke() ?? UmbrellaRules.WeatherNone;
		var age = RainMoodAgeTurnsOf( guest );
		if ( !RainMoodRules.Applies( age, weather, guest.RainMoodDay ) )
			return;
		(guest.Happiness, guest.Thirst, guest.Hunger) = RainMoodRules.Apply( guest.Happiness, guest.Thirst, guest.Hunger, weather, umbrellaWasOpen, settings, coldReplacesHappiness: ColdOwnsHappinessToday() );
		guest.RainMoodDay = RainMoodRules.DayOf( age ) + 1;
	}

	/// <summary>Whether the cold rule owns the happiness of this day's snow: the extension is on and the winter shops' signal reads snow (<see cref="WinterWeatherState"/>, falling back to <see cref="UmbrellaWeatherState"/>), even where the umbrellas' map reads a Halloween storm.</summary>
	private bool ColdOwnsHappinessToday() =>
		WinterCold != null && ((WinterWeatherState ?? UmbrellaWeatherState)?.Invoke() ?? UmbrellaRules.WeatherNone) == UmbrellaRules.WeatherSnow;

	/// <summary>
	/// The guest's age in park turns for the rain mood's day gate: its time in the park, or, for an imported original guest without one, the turns since the import (<see cref="Guest.RainMoodAgeStartTurn"/>), so the rule applies to it
	/// normally from then on. A guest with neither counts as new (age 0).
	/// </summary>
	// [EXT:rain-mood-imported-age] user-approved 2026-10-18 ("Tellen vanaf het laden"): an imported original guest's age counts from the load (the import turn), so the day gate then opens every fourth day instead of staying at age 0 for good
	internal long RainMoodAgeTurnsOf( Guest guest ) =>
		guest.TimeInParkSeconds != null || guest.RainMoodAgeStartTurn is not { } start ? AgeTurnsOf( guest ) : Math.Max( 0, ParkTurn - start );
}

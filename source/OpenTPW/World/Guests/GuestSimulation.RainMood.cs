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
	/// generation row's deltas and an open umbrella softens a happiness loss (<see cref="RainMoodRules.Apply"/>). Every guest is touched, queued or riding ones too.
	/// </summary>
	// [BIN:TPI-EXE:0x611050 weather rule] called from the guest update 0x619B50 for every guest, wherever it is; the day gate, the weather word +0x70 and the clamped sums are the original's
	// [EXT:rain-mood-once-per-day] once per qualifying day instead of on every update of it: the original writes its day (+0x2E8) and never reads it, so the rule hits on every update of those days (about six a game day)
	// [EXT:rain-mood-imported-age] a guest without a known time in the park (an imported original guest) counts as new, so its day is 0 for good and the rule applies once to it
	private void UpdateRainMood( Guest guest, RainMoodSettings settings )
	{
		var weather = UmbrellaWeatherState?.Invoke() ?? UmbrellaRules.WeatherNone;
		var age = AgeTurnsOf( guest );
		if ( !RainMoodRules.Applies( age, weather, guest.RainMoodDay ) )
			return;
		(guest.Happiness, guest.Thirst, guest.Hunger) = RainMoodRules.Apply( guest.Happiness, guest.Thirst, guest.Hunger, weather, guest.UmbrellaState == UmbrellaRules.StateOpen, settings );
		guest.RainMoodDay = RainMoodRules.DayOf( age ) + 1;
	}
}

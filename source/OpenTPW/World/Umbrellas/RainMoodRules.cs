namespace OpenTPW;

/// <summary>
/// One meter's per-weather deltas of the rain mood rule (<c>opentpw.rain-mood</c>, docs/UMBRELLAS.md): Theme Park Inc's four floats per generation row, in its key order (heat wave, rain, storm, snow).
/// </summary>
// [BIN:TPI-EXE:0x611050 weather rows] the rows are 16 bytes per age index (the guest's +0x2A4): happiness at 0xA01B88, thirst at 0xA01BBC, hunger at 0xA01BF0; the word picked is the weather state minus 1 (heat wave, rain, storm, snow)
public readonly record struct RainMoodDeltas( float HeatWave, float Rain, float Storm, float Snow )
{
	/// <summary>The delta of weather state <paramref name="weather"/> (<see cref="UmbrellaRules.WeatherHeatWave"/> to <see cref="UmbrellaRules.WeatherSnow"/>); 0 for none.</summary>
	public float Of( int weather ) => weather switch
	{
		UmbrellaRules.WeatherHeatWave => HeatWave,
		UmbrellaRules.WeatherRain => Rain,
		UmbrellaRules.WeatherStorm => Storm,
		UmbrellaRules.WeatherSnow => Snow,
		_ => 0f
	};
}

/// <summary>The three meters' deltas of one generation (<see cref="RainMoodSettings.Kid"/>).</summary>
public readonly record struct RainMoodRow( RainMoodDeltas Happiness, RainMoodDeltas Thirst, RainMoodDeltas Hunger );

/// <summary>
/// The rain mood extension's settings: the generation row every Theme Park World guest uses (they are all kids) and <c>UmbrellaModifier</c>. The defaults are Theme Park Inc's shipped numbers; a TPI-style park could
/// carry other rows (the parents' and the elderly's are worse: elderly rain is -2).
/// </summary>
// [BIN:TPI-EXE:0x611050 weather rule] UmbrellaModifier 0xA01B5C (an integer, 1) is added to a negative happiness delta while the umbrella is open; the shipped kid row is read from the TPI settings block (docs/UMBRELLAS.md)
public sealed record RainMoodSettings( RainMoodRow Kid, int UmbrellaModifier )
{
	/// <summary>Theme Park Inc's shipped numbers: kid rain -1, storm -1, snow +1 happiness, snow thirst -1 and hunger +1, and an umbrella modifier of 1. The heat wave rows never apply in Theme Park World.</summary>
	public static RainMoodSettings Default { get; } = new(
		new RainMoodRow(
			Happiness: new RainMoodDeltas( HeatWave: 1f, Rain: -1f, Storm: -1f, Snow: 1f ),
			Thirst: new RainMoodDeltas( HeatWave: 2f, Rain: 0f, Storm: 0f, Snow: -1f ),
			Hunger: new RainMoodDeltas( HeatWave: -1f, Rain: 0f, Storm: 0f, Snow: 1f ) ),
		UmbrellaModifier: 1 );
}

/// <summary>
/// The rain mood rule as pure functions (docs/UMBRELLAS.md, "The rain mood rule (phase U4)"): Theme Park Inc's weather rule changes a guest's happiness, thirst and hunger in rain, storm and snow, and an open umbrella softens a
/// happiness loss. Theme Park Inc applies it on every update of a qualifying day; this extension applies it once per qualifying day per guest.
/// </summary>
public static class RainMoodRules
{
	/// <summary>
	/// The game day of a guest of <paramref name="ageTurns"/> park turns: the same count <see cref="UmbrellaRules.DayGateOpen"/> divides by the weather frequency (turns cut to a multiple of four, as the original cuts its clock).
	/// </summary>
	// [BIN:TPI-EXE:0x611050 day count] ((clock - creation stamp +0x1FC) >> 2 << 2) through the clock's time and divided by a day in 100 ns; the quotient mod WeatherModifierFrequency decides the gate
	public static long DayOf( long ageTurns ) => (ageTurns >> 2 << 2) * ParkCalendar.SecondsPerTurn / ParkCalendar.SecondsPerDay;

	/// <summary>
	/// Whether the rule applies at this update: the weather is rain, storm or snow (the weather words the rule reads; TPW has no heat wave), the guest's day qualifies (<see cref="UmbrellaRules.DayGateOpen"/>) and the rule has
	/// not yet been applied to it on that day (<paramref name="lastAppliedDay"/> is the day plus one of the last application, 0 for never).
	/// </summary>
	// [EXT:rain-mood-once-per-day] Theme Park Inc stores the day (+0x2E8) at every qualifying update but never reads it, so its rule applies on every update of a qualifying day; the extension applies it once per qualifying day
	public static bool Applies( long ageTurns, int weather, long lastAppliedDay ) =>
		UmbrellaRules.IsBadWeather( weather ) && UmbrellaRules.DayGateOpen( ageTurns ) && lastAppliedDay != DayOf( ageTurns ) + 1;

	/// <summary>
	/// The guest's meters after the rule: the row's deltas for <paramref name="weather"/>, an open umbrella (<paramref name="umbrellaOpen"/>, state 4 only) adding <see cref="RainMoodSettings.UmbrellaModifier"/> to a negative
	/// happiness delta, each meter clamped to 0..100 as a float. With <paramref name="coldReplacesHappiness"/> (<c>opentpw.winter-cold</c> is on and the winter signal says snow) the happiness delta of the row used is left out, whichever row that is (snow's +1, or Halloween's storm -1 when the snowfall flashes lightning): the cold rule replaces it, thirst and hunger stay.
	/// </summary>
	// [BIN:TPI-EXE:0x611050 weather deltas] happiness delta below 0 with the umbrella state 4: UmbrellaModifier (fild of 0xA01B5C) is added to it; then each meter is the clamped sum meter + delta (happiness +0x1BC, hunger +0x1CC, thirst +0x1C8)
	public static (float Happiness, float Thirst, float Hunger) Apply( float happiness, float thirst, float hunger, int weather, bool umbrellaOpen, RainMoodSettings settings, bool coldReplacesHappiness = false )
	{
		var row = settings.Kid;
		var happinessDelta = coldReplacesHappiness ? 0f : row.Happiness.Of( weather );
		if ( umbrellaOpen && happinessDelta < 0f )
			happinessDelta += settings.UmbrellaModifier;
		return (MeterMath.Add( happiness, happinessDelta ), MeterMath.Add( thirst, row.Thirst.Of( weather ) ), MeterMath.Add( hunger, row.Hunger.Of( weather ) ));
	}
}

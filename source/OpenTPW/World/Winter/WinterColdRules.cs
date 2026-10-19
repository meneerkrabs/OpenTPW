namespace OpenTPW;

/// <summary>
/// The cold rule's numbers (<c>opentpw.winter-cold</c>, docs/WINTER.md, "Cold mood"): what snow costs a guest's happiness and what gives it back. They are the extension's code defaults, versioned by the extension version.
/// </summary>
// [EXT:winter-cold] OpenTPW's own rule and numbers (user decision 2026-10-18: -2 in snow, +1 back each for a head item, mittens and a recent glühwein, capped at 0); Theme Park World has no cold, and Theme Park Inc's snow is +1 for kids
public sealed record WinterColdSettings( int ColdHappiness, int HeadItemBonus, int MittenBonus, int WarmBonus, int WarmEffectDays )
{
	/// <summary>The decided numbers: -2 happiness, +1 for a head item, +1 for mittens, +1 while warmed by a glühwein, and a glühwein warms for the rest of its day and one more day.</summary>
	public static WinterColdSettings Default { get; } = new( ColdHappiness: -2, HeadItemBonus: 1, MittenBonus: 1, WarmBonus: 1, WarmEffectDays: 1 );
}

/// <summary>
/// The cold rule as pure functions (docs/WINTER.md): in snow, once per qualifying day (the rain mood's day gate and day count), a guest's happiness changes by the cold delta, which its warm items and a recent glühwein soften, never above 0.
/// Nothing here draws from any random stream.
/// </summary>
public static class WinterColdRules
{
	/// <summary>
	/// Whether the rule applies at this update: the weather is snow, the guest's day qualifies (<see cref="UmbrellaRules.DayGateOpen"/>, the rain mood's gate) and the rule has not yet been applied to it on that day
	/// (<paramref name="lastAppliedDay"/> is the day plus one of the last application, 0 for never).
	/// </summary>
	// [EXT:winter-cold] the day gate and the once-per-day rule are the rain mood's (RainMoodRules.Applies), only for snow
	public static bool Applies( long ageTurns, int weather, long lastAppliedDay ) =>
		weather == UmbrellaRules.WeatherSnow && UmbrellaRules.DayGateOpen( ageTurns ) && lastAppliedDay != RainMoodRules.DayOf( ageTurns ) + 1;

	/// <summary>
	/// The happiness change of one application for a guest wearing <paramref name="warmItems"/> (<see cref="WinterRules"/> bits) and warmed or not: the cold delta plus one bonus for a head item, one for mittens and one while warmed, capped at 0.
	/// </summary>
	// [EXT:winter-cold] "capped at 0": a fully protected guest is neither cooled nor cheered (the rain mood's snow +1 is replaced, not kept)
	public static int Delta( int warmItems, bool warmed, WinterColdSettings settings )
	{
		var delta = settings.ColdHappiness;
		if ( (warmItems & WinterRules.HeadItems) != 0 )
			delta += settings.HeadItemBonus;
		if ( (warmItems & WinterRules.Mittens) != 0 )
			delta += settings.MittenBonus;
		if ( warmed )
			delta += settings.WarmBonus;
		return Math.Min( 0, delta );
	}

	/// <summary>The guest's happiness after the rule, clamped to 0..100 as a float.</summary>
	public static float Apply( float happiness, int warmItems, bool warmed, WinterColdSettings settings ) =>
		MeterMath.Add( happiness, Delta( warmItems, warmed, settings ) );

	/// <summary>
	/// The park day (<see cref="ParkCalendar.DayIndex"/>) a glühwein drunk on <paramref name="day"/> warms until, exclusive: the rest of that day and <see cref="WinterColdSettings.WarmEffectDays"/> more.
	/// </summary>
	// [EXT:winter-cold] "recent": the day of the drink and the next WarmEffectDays days, so a drink late in a day still counts for the next qualifying update
	public static long WarmUntilDayOf( long day, WinterColdSettings settings ) => day + 1 + Math.Max( 0, settings.WarmEffectDays );

	/// <summary>Whether a guest whose glühwein warms until <paramref name="warmUntilDay"/> is warmed on park day <paramref name="day"/>.</summary>
	public static bool IsWarmed( long warmUntilDay, long day ) => day < warmUntilDay;
}

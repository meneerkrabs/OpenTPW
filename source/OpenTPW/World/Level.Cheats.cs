namespace OpenTPW;

/// <summary>The level's side of the park's cheats (docs/CHEATS.md): the arrivals and the weather, which the economy does not hold.</summary>
public partial class Level : IParkCheatHost
{
	// [EXT:cheats-arrival] the guest simulation starts the group and the level's arrival vehicles bring it
	bool IParkCheatHost.TryStartArrival( int vehicleKind ) => !IsReadOnlyVisit && Guests?.TryStartCheatArrival( vehicleKind ) == true;

	// [EXT:cheats-weather] the park's own weather, which the guests read
	bool IParkCheatHost.TryApplyWeather( CheatWeather weather, long tick ) => !IsReadOnlyVisit && Weather?.ApplyCheat( weather, tick ) == true;

	/// <summary>Gives the park's economy (whichever the runtime holds now: a load replaces it) the level as its cheat host.</summary>
	private void AttachCheatHost()
	{
		if ( Park != null && !ReferenceEquals( Park.Economy.CheatHost, this ) )
			Park.Economy.CheatHost = this;
	}
}

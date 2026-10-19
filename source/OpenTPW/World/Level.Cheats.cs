namespace OpenTPW;

/// <summary>The level's side of the park's cheats (docs/CHEATS.md): the arrivals and the weather, which the economy does not hold.</summary>
public partial class Level : IParkCheatHost
{
	// [EXT:cheats-arrival] the guest simulation starts the group and the level's arrival vehicles bring it
	bool IParkCheatHost.TryStartArrival( int vehicleKind ) => !IsReadOnlyVisit && Guests?.TryStartCheatArrival( vehicleKind ) == true;

	// [EXT:cheats-weather] the park's own weather, which the guests read
	bool IParkCheatHost.TryApplyWeather( CheatWeather weather, long tick ) => !IsReadOnlyVisit && Weather?.ApplyCheat( weather, tick ) == true;

	// [EXT:cheats-ride-vehicles] the placed ride's runtime raises its controllers
	bool IParkCheatHost.CanRaiseRideVehicles( int attractionId ) => !IsReadOnlyVisit && CheatRide( attractionId ) is { Runtime: { CanRaiseVehicles: true, VehiclesRaised: false } };

	bool IParkCheatHost.TryRaiseRideVehicles( int attractionId ) => !IsReadOnlyVisit && CheatRide( attractionId )?.Runtime.RaiseVehicleLimit() == true;

	/// <summary>The placed, not deleted object with the attraction id <paramref name="attractionId"/>, or null.</summary>
	private OriginalObject? CheatRide( int attractionId ) => Objects.Objects.FirstOrDefault( item => !item.IsDeleted && item.Runtime.AttractionId == attractionId );

	/// <summary>[EXT:cheats-ride-vehicles] The placed rides the cheat could raise or has raised, in attraction order (the cheats window's ride row).</summary>
	internal IReadOnlyList<OriginalObject> CheatRides => Objects.Objects.Where( item => !item.IsDeleted && (item.Runtime.CanRaiseVehicles || item.Runtime.VehiclesRaised) ).OrderBy( item => item.Runtime.AttractionId ).ToArray();

	/// <summary>[EXT:cheats-ride-vehicles] How many placed rides the cheat raised.</summary>
	internal int RaisedRideCount => Objects.Objects.Count( item => !item.IsDeleted && item.Runtime.VehiclesRaised );

	/// <summary>
	/// [EXT:cheats-ride-vehicles] How many placed rides the cheat raised and run above their type's original capacity maximum: the original-format export refuses such a park
	/// (<see cref="OriginalParkExportOptions.RidesAboveOriginalLimits"/>).
	/// </summary>
	internal int RidesAboveOriginalLimits => Objects.Objects.Count( item => !item.IsDeleted && item.Runtime.VehiclesRaised && item.Runtime.Settings.Capacity > item.Entry.Settings.GetInt( "UsageInfo.MaxCapacity" ) );

	/// <summary>Gives the park's economy (whichever the runtime holds now: a load replaces it) the level as its cheat host.</summary>
	private void AttachCheatHost()
	{
		if ( Park != null && !ReferenceEquals( Park.Economy.CheatHost, this ) )
			Park.Economy.CheatHost = this;
	}
}

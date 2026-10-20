namespace OpenTPW;

/// <summary>The ride vehicle cheat of the <c>opentpw.cheats</c> extension (docs/CHEATS.md, phase K4): a bumper, kart or tour ride whose vehicle limit the cheats window raised.</summary>
// [EXT:cheats-ride-vehicles] OpenTPW's own cheat (the user's decision of 2026-10-19): the original has no way past a ride's UsageInfo.MaxCapacity or its controller's vehicle limit
public sealed partial class OriginalObjectRuntime
{
	/// <summary>The cars every shipped tour ride flies at its capacity maximum: ceil(27 / 9) for the three tours, ceil(24 / 8) for Star Tours (docs/CHEATS.md, "Ride vehicles").</summary>
	internal const int TourCarsAtShippedMaximum = 3;

	/// <summary>The largest capacity a raised ride may hold in a save (a tour's 27 × 40 / 3 = 360 is the largest the shipped rides reach).</summary>
	internal const int MaximumRaisedCapacity = 4095;

	/// <summary>Whether the cheat raised this ride's vehicle limit. Sticky: the ride keeps it until it is removed.</summary>
	public bool VehiclesRaised { get; private set; }

	/// <summary>Whether the cheat can raise this ride's vehicle limit: a bumper car or kart ride (not a water ride, whose boats the module's 64 already limit) or a tour ride.</summary>
	public bool CanRaiseVehicles => !stopped && (Bump?.CanRaiseVehicleLimit == true || Bump == null && UsesTour);

	/// <summary>The vehicle limit the cheat raises the ride to: <see cref="BumpController.RaisedVehicleLimit"/> cars or karts, or <see cref="TourController.RaisedCarSlots"/> tour cars; 0 for any other ride.</summary>
	public int RaisedVehicleCount => Bump?.CanRaiseVehicleLimit == true ? BumpController.RaisedVehicleLimit : Bump == null && UsesTour ? TourController.RaisedCarSlots : 0;

	/// <summary>
	/// The ride's capacity maximum: <c>UsageInfo.MaxCapacity</c>, or, once raised, the capacity that fills the raised vehicles: the shipped maximum times raised vehicles over shipped vehicles
	/// (a bumper ride 8 → 32, CryptKarts 16 → 64, a tour 27 → 360, Star Tours 24 → 320). The ride window's capacity slider and the capacity setter use it.
	/// </summary>
	public int MaximumCapacity => CapacityMaximum( VehiclesRaised );

	/// <summary>The capacity maximum the ride has unraised (<c>UsageInfo.MaxCapacity</c>) or raised.</summary>
	private int CapacityMaximum( bool raised )
	{
		var shipped = Entry.Settings.GetInt( "UsageInfo.MaxCapacity" );
		if ( !raised )
			return shipped;
		return Bump is { } bump ? shipped * BumpController.RaisedVehicleLimit / Math.Max( 1, bump.Template.MaxVehicles ) : shipped * TourController.RaisedCarSlots / TourCarsAtShippedMaximum;
	}

	/// <summary>
	/// Raises the ride's vehicle limit (the cheat at a turn boundary): the bump controller launches up to 32 cars or karts, a tour controller gets 40 car slots (now, or when the script creates
	/// it), and the capacity maximum grows to match; the capacity itself stays as it is until the player moves the ride window's slider. False for a ride the cheat cannot raise or one already raised.
	/// </summary>
	internal bool RaiseVehicleLimit()
	{
		if ( !CanRaiseVehicles || VehiclesRaised )
			return false;
		VehiclesRaised = true;
		Bump?.RaiseVehicleLimit();
		Tour?.RaiseCarSlots();
		return true;
	}

	/// <summary>
	/// Park resume: a raised snapshot must belong to a ride the cheat can raise, and its capacity must stay within the ride's maximum (the raised one, else the original's byte). The controllers
	/// bring their own raised limits (<see cref="BumpControllerSaveData.VehicleLimit"/>, <see cref="TourControllerSaveData.CarSlots"/>).
	/// </summary>
	private void CheckRaisedSnapshot( OriginalObjectRuntimeSnapshot snapshot )
	{
		var raised = snapshot.VehiclesRaised == true;
		if ( raised && !(Bump?.CanRaiseVehicleLimit == true || Bump == null && UsesTour) )
			throw new InvalidDataException( $"'{Entry.ArchivePath}': the snapshot raised the vehicle limit of a ride the cheat cannot raise." );
		var maximum = raised ? CapacityMaximum( true ) : byte.MaxValue;
		if ( snapshot.RideCapacity > maximum )
			throw new InvalidDataException( $"'{Entry.ArchivePath}': the saved capacity {snapshot.RideCapacity} is above the ride's maximum {maximum}." );
	}
}

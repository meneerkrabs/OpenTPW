namespace OpenTPW;

/// <summary>One flying car in a park-resume save (every field of <see cref="TourCar"/> that the simulation reads).</summary>
internal sealed record TourCarSaveData(
	int Slot, int State, int X, int Y, int Z, int Heading, int Pitch, int Roll,
	int PreviousX, int PreviousY, int PreviousZ, int PreviousHeading, int PreviousPitch, int PreviousRoll,
	int TargetX, int TargetY, int TargetZ, int PreviousTargetX, int PreviousTargetY, int PreviousTargetZ,
	int VelocityX, int VelocityZ, int Turn, int VerticalVelocity, int Speed, int Thrust, int Timer, int Corner,
	int RiderCount, List<int> Riders, bool Leaving, bool Broken, int ApproachHeading, int Random, int Animation, bool AnimationLoops, int Emitter = 0 );

/// <summary>
/// The stale words of a free car slot (TOUR-009): the original's car creation keeps the heading, patrol corner, seat ids and approach heading of the slot's last car,
/// and its save writes every slot raw. Only free slots that hold a non-zero word are saved (every other word is the zero of a fresh controller).
/// </summary>
internal sealed record TourFreeSlotSaveData( int Slot, int Heading, int Corner, int ApproachHeading, List<int> Riders );

/// <summary>One tour controller in a park-resume save: its words, its cars and the riders a breakdown threw off.</summary>
internal sealed record TourControllerSaveData(
	int AttractionId, int StationX, int StationY, int StationZ, int RotationDegrees, bool Small,
	int Wanted, int Requested, int CarCount, int Duration, int AvoidanceScale, int BankScale, bool LandingReserved, int Condition, bool IsOpen, bool Ready,
	int ApproachMode, int Riders, long TickCount, double TickRemainder, int Boarded, int Released, int Departures,
	List<int> Ejected, List<TourCarSaveData> Cars, List<TourFreeSlotSaveData>? FreeSlots = null,
	[property: System.Text.Json.Serialization.JsonIgnore( Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull )] int? CarSlots = null );

/// <summary>The park-resume tour part: every live tour ride's controller (<see cref="ParkResumeSnapshot.Tours"/>), joined by attraction id.</summary>
internal sealed record TourWorldSaveData
{
	public required List<TourControllerSaveData> Controllers { get; init; }
}

/// <summary>Capture and restore of the tour controllers for the park-resume envelope (docs/TOURS.md).</summary>
internal static class TourSave
{
	public const int MaximumGuests = 4096;
	/// <summary>The bound on every saved coordinate and velocity: the flight area plus its margins is far inside it.</summary>
	public const int CoordinateLimit = 2_000_000;

	public static TourControllerSaveData Capture( TourController controller )
	{
		var cars = new List<TourCarSaveData>();
		var free = new List<TourFreeSlotSaveData>();
		for ( var slot = 0; slot < controller.Cars.Count; slot++ )
		{
			var car = controller.Cars[slot];
			if ( car.State == TourCar.StateFree )
			{
				if ( car.Heading != 0 || car.Corner != 0 || car.ApproachHeading != 0 || car.Riders.Any( rider => rider != 0 ) )
					free.Add( new TourFreeSlotSaveData( slot, car.Heading, car.Corner, car.ApproachHeading, car.Riders.ToList() ) );
				continue;
			}
			cars.Add( new TourCarSaveData( slot, car.State, car.X, car.Y, car.Z, car.Heading, car.Pitch, car.Roll,
				car.PreviousX, car.PreviousY, car.PreviousZ, car.PreviousHeading, car.PreviousPitch, car.PreviousRoll,
				car.TargetX, car.TargetY, car.TargetZ, car.PreviousTargetX, car.PreviousTargetY, car.PreviousTargetZ,
				car.VelocityX, car.VelocityZ, car.Turn, car.VerticalVelocity, car.Speed, car.Thrust, car.Timer, car.Corner,
				car.RiderCount, car.Riders.ToList(), car.Leaving, car.Broken, car.ApproachHeading, car.Random, car.Animation, car.AnimationLoops, car.Emitter ) );
		}
		return new TourControllerSaveData( controller.AttractionId, controller.StationX, controller.StationY, controller.StationZ, controller.RotationDegrees, controller.Small,
			controller.Wanted, controller.Requested, controller.CarCount, controller.Duration, controller.AvoidanceScale, controller.BankScale, controller.LandingReserved, controller.Condition,
			controller.IsOpen, controller.Ready, controller.ApproachMode, controller.Riders, controller.TickCount, controller.TickRemainder, controller.Boarded, controller.Released, controller.Departures,
			controller.Ejected.ToList(), cars, free.Count == 0 ? null : free,
			// [EXT:cheats-ride-vehicles] written only for a raised controller, so every other save keeps its bytes
			controller.SlotCount != TourController.CarSlots ? controller.SlotCount : null );
	}

	/// <summary>Whether a saved controller is internally consistent: bounded words, distinct slots, riders matching the counts. Throws <see cref="InvalidDataException"/> otherwise.</summary>
	public static void Validate( TourControllerSaveData saved, Func<int, bool> guestExists, ISet<int> listedGuests )
	{
		void Require( bool condition, string message )
		{
			if ( !condition )
				throw new InvalidDataException( $"World save is inconsistent: tour controller {saved.AttractionId}: {message}." );
		}
		bool Small( int value ) => value >= -CoordinateLimit && value <= CoordinateLimit;
		Require( saved.Cars != null && saved.Ejected != null, "it holds no car or ejected list" );
		// [EXT:cheats-ride-vehicles] a controller the cheat raised has 40 slots; every other one 20
		Require( saved.CarSlots is null or TourController.RaisedCarSlots, $"it has {saved.CarSlots} car slots" );
		var slotCount = saved.CarSlots ?? TourController.CarSlots;
		Require( saved.Cars!.Count <= slotCount, $"it holds {saved.Cars.Count} cars" );
		Require( saved.Wanted >= 0 && saved.Wanted <= slotCount && saved.Requested is >= -1000 and <= 1000 && saved.CarCount == saved.Cars.Count, "a car count is out of range" );
		Require( saved.Condition is >= 0 and <= 4 && saved.ApproachMode is >= -1000 and <= 1000, "the condition or approach mode is out of range" );
		Require( Small( saved.StationX ) && Small( saved.StationY ) && Small( saved.StationZ ) && saved.RotationDegrees is 0 or 90 or 180 or 270, "the station is out of range" );
		Require( saved.Duration is >= -1_000_000 and <= 1_000_000 && saved.AvoidanceScale is >= 0 and <= 1_000_000 && saved.BankScale is >= -1_000_000 and <= 1_000_000, "a scale is out of range" );
		Require( saved.TickCount >= 0 && double.IsFinite( saved.TickRemainder ) && saved.TickRemainder >= 0 && saved.TickRemainder < TourController.MaximumSavedRemainder, "the tick clock is bad" );
		Require( saved.Boarded >= 0 && saved.Released >= 0 && saved.Departures >= 0, "a statistic is negative" );
		var slots = new HashSet<int>();
		var riders = 0;
		foreach ( var car in saved.Cars )
		{
			Require( car != null && car.Slot >= 0 && car.Slot < slotCount && slots.Add( car.Slot ), "a car has a bad or repeated slot" );
			Require( car.State is >= TourCar.StateDocked and <= TourCar.StateLeaving, $"car {car.Slot} has state {car.State}" );
			Require( Small( car.X ) && Small( car.Y ) && Small( car.Z ) && Small( car.PreviousX ) && Small( car.PreviousY ) && Small( car.PreviousZ )
				&& Small( car.TargetX ) && Small( car.TargetY ) && Small( car.TargetZ ) && Small( car.PreviousTargetX ) && Small( car.PreviousTargetY ) && Small( car.PreviousTargetZ ), $"car {car.Slot} lies outside the module's range" );
			Require( car.Heading is >= 0 and < 4096 && car.PreviousHeading is >= 0 and < 4096 && car.Roll is >= 0 and < 4096 && car.PreviousRoll is >= 0 and < 4096, $"car {car.Slot} has an angle out of range" );
			Require( Small( car.Pitch ) && Small( car.PreviousPitch ) && Small( car.VelocityX ) && Small( car.VelocityZ ) && Small( car.Turn ) && Small( car.VerticalVelocity ) && Small( car.Speed ) && Small( car.Thrust ) && Small( car.Timer )
				&& car.Corner is >= 0 and < 4 && Small( car.ApproachHeading ) && car.Animation is >= 0 and <= 10, $"car {car.Slot} has a scalar out of range" );
			Require( car.Emitter is 0 or TourEffects.ParticleFlySmoke or TourEffects.ParticleFlyFire, $"car {car.Slot} holds an unknown emitter {car.Emitter}" );
			// the seat words: every seat (the stale ones after the riders aboard included), or only the riders aboard in a save from before the stale words were kept
			Require( Math.Abs( car.RiderCount ) <= TourCar.MaximumRiders && car.Riders != null && (car.Riders.Count == Math.Abs( car.RiderCount ) || car.Riders.Count == TourCar.MaximumRiders), $"car {car.Slot}'s riders do not match its count" );
			riders += Math.Abs( car.RiderCount );
			for ( var seat = 0; seat < car.Riders!.Count; seat++ )
			{
				if ( seat < Math.Abs( car.RiderCount ) )
					Require( guestExists( car.Riders[seat] ) && listedGuests.Add( car.Riders[seat] ), $"car {car.Slot} carries a guest the park does not hold, or one listed twice" );
				else
					Require( Small( car.Riders[seat] ), $"car {car.Slot} has a stale seat id out of range" );
			}
		}
		foreach ( var slot in saved.FreeSlots ?? new List<TourFreeSlotSaveData>() )
		{
			Require( slot != null && slot.Slot >= 0 && slot.Slot < slotCount && slots.Add( slot.Slot ), "a free slot has a bad slot or one a car holds" );
			Require( slot.Heading is >= 0 and < 4096 && slot.Corner is >= 0 and < 4 && Small( slot.ApproachHeading ), $"free slot {slot.Slot} has a stale heading, corner or approach out of range" );
			Require( slot.Riders != null && slot.Riders.Count == TourCar.MaximumRiders && slot.Riders.All( Small ), $"free slot {slot.Slot} has a bad seat list" );
		}
		foreach ( var guest in saved.Ejected! )
			Require( guestExists( guest ) && listedGuests.Add( guest ), "an ejected rider is a guest the park does not hold, or one listed twice" );
		Require( saved.Riders == riders, $"the rider total {saved.Riders} is not the {riders} aboard its cars" );
		// the landing slot is held by a car that stands at the station, is lifting off, or is on its approach; with no such car a flag left set would keep every waiting car circling for ever
		Require( !saved.LandingReserved || saved.Cars.Any( car => car.State is TourCar.StateDocked or TourCar.StateTakeoff or TourCar.StateStaging or TourCar.StateApproach or TourCar.StateLanding ), "the landing slot is reserved but no car holds it" );
	}
}

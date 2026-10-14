namespace OpenTPW;

/// <summary>One flying car in a park-resume save (every field of <see cref="TourCar"/> that the simulation reads).</summary>
internal sealed record TourCarSaveData(
	int Slot, int State, int X, int Y, int Z, int Heading, int Pitch, int Roll,
	int PreviousX, int PreviousY, int PreviousZ, int PreviousHeading, int PreviousPitch, int PreviousRoll,
	int TargetX, int TargetY, int TargetZ, int PreviousTargetX, int PreviousTargetY, int PreviousTargetZ,
	int VelocityX, int VelocityZ, int Turn, int VerticalVelocity, int Speed, int Thrust, int Timer, int Corner,
	int RiderCount, List<int> Riders, bool Leaving, bool Broken, int ApproachHeading, int Random, int Animation, bool AnimationLoops );

/// <summary>One tour controller in a park-resume save: its words, its cars and the riders a breakdown threw off.</summary>
internal sealed record TourControllerSaveData(
	int AttractionId, int StationX, int StationY, int StationZ, int RotationDegrees, bool Small,
	int Wanted, int Requested, int CarCount, int Duration, int AvoidanceScale, int BankScale, bool LandingReserved, int Condition, bool IsOpen, bool Ready,
	int ApproachMode, int Riders, long TickCount, double TickRemainder, int Boarded, int Released, int Departures,
	List<int> Ejected, List<TourCarSaveData> Cars );

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
		for ( var slot = 0; slot < controller.Cars.Count; slot++ )
		{
			var car = controller.Cars[slot];
			if ( car.State == TourCar.StateFree )
				continue;
			cars.Add( new TourCarSaveData( slot, car.State, car.X, car.Y, car.Z, car.Heading, car.Pitch, car.Roll,
				car.PreviousX, car.PreviousY, car.PreviousZ, car.PreviousHeading, car.PreviousPitch, car.PreviousRoll,
				car.TargetX, car.TargetY, car.TargetZ, car.PreviousTargetX, car.PreviousTargetY, car.PreviousTargetZ,
				car.VelocityX, car.VelocityZ, car.Turn, car.VerticalVelocity, car.Speed, car.Thrust, car.Timer, car.Corner,
				car.RiderCount, car.Riders.Take( Math.Min( Math.Abs( car.RiderCount ), TourCar.MaximumRiders ) ).ToList(), car.Leaving, car.Broken, car.ApproachHeading, car.Random, car.Animation, car.AnimationLoops ) );
		}
		return new TourControllerSaveData( controller.AttractionId, controller.StationX, controller.StationY, controller.StationZ, controller.RotationDegrees, controller.Small,
			controller.Wanted, controller.Requested, controller.CarCount, controller.Duration, controller.AvoidanceScale, controller.BankScale, controller.LandingReserved, controller.Condition,
			controller.IsOpen, controller.Ready, controller.ApproachMode, controller.Riders, controller.TickCount, controller.TickRemainder, controller.Boarded, controller.Released, controller.Departures,
			controller.Ejected.ToList(), cars );
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
		Require( saved.Cars!.Count <= TourController.CarSlots, $"it holds {saved.Cars.Count} cars" );
		Require( saved.Wanted is >= 0 and <= TourController.CarSlots && saved.Requested is >= -1000 and <= 1000 && saved.CarCount == saved.Cars.Count, "a car count is out of range" );
		Require( saved.Condition is >= 0 and <= 4 && saved.ApproachMode is >= -1000 and <= 1000, "the condition or approach mode is out of range" );
		Require( Small( saved.StationX ) && Small( saved.StationY ) && Small( saved.StationZ ) && saved.RotationDegrees is 0 or 90 or 180 or 270, "the station is out of range" );
		Require( saved.Duration is >= -1_000_000 and <= 1_000_000 && saved.AvoidanceScale is >= 0 and <= 1_000_000 && saved.BankScale is >= -1_000_000 and <= 1_000_000, "a scale is out of range" );
		Require( saved.TickCount >= 0 && double.IsFinite( saved.TickRemainder ) && saved.TickRemainder >= 0 && saved.TickRemainder < TourController.TickSeconds, "the tick clock is bad" );
		Require( saved.Boarded >= 0 && saved.Released >= 0 && saved.Departures >= 0, "a statistic is negative" );
		var slots = new HashSet<int>();
		var riders = 0;
		foreach ( var car in saved.Cars )
		{
			Require( car != null && car.Slot is >= 0 and < TourController.CarSlots && slots.Add( car.Slot ), "a car has a bad or repeated slot" );
			Require( car.State is >= TourCar.StateDocked and <= TourCar.StateLeaving, $"car {car.Slot} has state {car.State}" );
			Require( Small( car.X ) && Small( car.Y ) && Small( car.Z ) && Small( car.PreviousX ) && Small( car.PreviousY ) && Small( car.PreviousZ )
				&& Small( car.TargetX ) && Small( car.TargetY ) && Small( car.TargetZ ) && Small( car.PreviousTargetX ) && Small( car.PreviousTargetY ) && Small( car.PreviousTargetZ ), $"car {car.Slot} lies outside the module's range" );
			Require( car.Heading is >= 0 and < 4096 && car.PreviousHeading is >= 0 and < 4096 && car.Roll is >= 0 and < 4096 && car.PreviousRoll is >= 0 and < 4096, $"car {car.Slot} has an angle out of range" );
			Require( Small( car.Pitch ) && Small( car.PreviousPitch ) && Small( car.VelocityX ) && Small( car.VelocityZ ) && Small( car.Turn ) && Small( car.VerticalVelocity ) && Small( car.Speed ) && Small( car.Thrust ) && Small( car.Timer )
				&& car.Corner is >= 0 and < 4 && Small( car.ApproachHeading ) && car.Animation is >= 0 and <= 10, $"car {car.Slot} has a scalar out of range" );
			Require( Math.Abs( car.RiderCount ) <= TourCar.MaximumRiders && car.Riders != null && car.Riders.Count == Math.Abs( car.RiderCount ), $"car {car.Slot}'s riders do not match its count" );
			riders += Math.Abs( car.RiderCount );
			foreach ( var guest in car.Riders! )
				Require( guestExists( guest ) && listedGuests.Add( guest ), $"car {car.Slot} carries a guest the park does not hold, or one listed twice" );
		}
		foreach ( var guest in saved.Ejected! )
			Require( guestExists( guest ) && listedGuests.Add( guest ), "an ejected rider is a guest the park does not hold, or one listed twice" );
		Require( saved.Riders == riders, $"the rider total {saved.Riders} is not the {riders} aboard its cars" );
		// the landing slot is held by a car that stands at the station, is lifting off, or is on its approach; with no such car a flag left set would keep every waiting car circling for ever
		Require( !saved.LandingReserved || saved.Cars.Any( car => car.State is TourCar.StateDocked or TourCar.StateTakeoff or TourCar.StateStaging or TourCar.StateApproach or TourCar.StateLanding ), "the landing slot is reserved but no car holds it" );
	}
}

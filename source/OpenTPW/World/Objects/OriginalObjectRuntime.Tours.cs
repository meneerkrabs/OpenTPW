namespace OpenTPW;

public sealed partial class OriginalObjectRuntime
{
	private Func<TourStation>? tourStation;

	/// <summary>The tour controller of a tour ride (the four original tour rides), created by the script's <c>TOUR 1</c>; null before that and for every other ride (docs/TOURS.md).</summary>
	public TourController? Tour { get; private set; }

	/// <summary><see cref="Tour"/> while the ride is live: not stopped and still in the park's module list. The park-resume capture and restore use this one rule.</summary>
	public TourController? LiveTour => Tour != null && !stopped && scriptWorld.Tours.Controllers.Contains( Tour ) ? Tour : null;

	/// <summary>Whether the ride was stopped (removed or faulted).</summary>
	internal bool IsStopped => stopped;

	/// <summary>Whether the ride's script drives a tour controller (it contains the <c>TOUR</c> opcode).</summary>
	public bool UsesTour => Script?.Script.Instructions.Any( instruction => instruction.Opcode == (ushort)Opcode.TOUR ) == true;

	/// <summary>Gives the runtime the launch pad of the ride (model node 99 and the placement rotation); a headless runtime keeps the module's default pad.</summary>
	internal void SetTourStation( Func<TourStation> station ) => tourStation = station;

	/// <summary>The launch pad a controller created now would use.</summary>
	internal TourStation CurrentTourStation => tourStation?.Invoke() ?? TourStation.Default;

	/// <summary>
	/// Routes a <c>TOUR</c> command: 1 creates the controller (at most one per ride; a second creation keeps the first), 2 destroys it, everything else
	/// goes to it. A command before the creation or after the destruction does nothing.
	/// </summary>
	// [BIN:TPW-EXE:0x005542FE TOUR 1] creation reads the ride model's node 99 (class 0x800) for the pad, converts the placement rotation to 4096ths of a turn and allocates a controller from the module's 99 slots, which also creates the first car
	// [BIN:TPW-EXE:0x0055D3D0 TOUR 2] destruction frees the cars' things and sounds and the controller
	internal int PerformTour( RideEffectCall call )
	{
		var command = call.Argument( 0 );
		switch ( command )
		{
			case TourController.CommandCreate:
				CreateTour( call.OperandCount > 1 ? call.Argument( 1 ) : 0 );
				return 0;
			case TourController.CommandDestroy:
				ReleaseTour();
				Tour = null;
				return 0;
		}
		return Tour?.Command( command, call.OperandCount > 1 ? call.Argument( 1 ) : 0 ) ?? 0;
	}

	/// <summary>Creates the controller (and its first car) if the ride has none; the park-resume restore also uses it for saves from before the tour controllers (TOUR-006).</summary>
	internal TourController? CreateTour( int small = 0 )
	{
		if ( Tour != null || stopped || scriptWorld.Tours.Controllers.Count >= TourWorld.MaximumControllers )
			return Tour;
		Tour = new TourController( CurrentTourStation, scriptWorld.NextWorldDraw, scriptWorld.Tours, small ) { AttractionId = AttractionId };
		// [EXT:cheats-ride-vehicles] a ride the cheat raised before its script created the controller
		if ( VehiclesRaised )
			Tour.RaiseCarSlots();
		Tour.Effect += OnTourEffect;
		// the controller's first car was created inside its constructor, before anyone listened: its engine starts now
		ResumeTourEffects();
		return Tour;
	}

	/// <summary>Park resume: rebuilds the controller of a saved tour ride from its saved state (the ride has none yet).</summary>
	internal void RestoreTour( TourControllerSaveData saved )
	{
		if ( Tour != null )
			throw new InvalidDataException( $"'{Entry.ArchivePath}': the tour controller exists before the save restored it." );
		Tour = new TourController( saved, scriptWorld.NextWorldDraw, scriptWorld.Tours );
		Tour.Effect += OnTourEffect;
		ResumeTourEffects();
	}

	/// <summary>The controller leaves the module (a removed or faulted ride); its riders go with <see cref="Visitors"/>.</summary>
	private void ReleaseTour()
	{
		StopTourEffects();
		Tour?.Release();
	}
}

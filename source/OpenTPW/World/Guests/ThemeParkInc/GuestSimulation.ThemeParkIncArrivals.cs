namespace OpenTPW;

// Theme Park Inc's side of the arrival update for native guests (docs/reverse/TPI-GUESTS.md, "Arrival"): the update 0x5BCB10 is Theme Park World's 0x004CF3E0 step for step (UpdateArrivalVehicle),
// with three differences: the guest is built at the vehicle kind's stop (0x5BCF90) and walks straight to a ticket booth (the constructor's tail), the dispatch 0x64FB90 destroys every cached
// vehicle and builds a new one, and the pick-up test 0x6503F0 reads the BusStopA cell alone.
public sealed partial class GuestSimulation
{
	/// <summary>
	/// One guest of the arrival vehicle (<c>0x5BCF90</c>): built at the stop of the vehicle kind at work (BusStopA for the bus or none, BusStopB for the heli, BusStopC for the mono) and placed at the
	/// cell's centre; the constructor's tail then decides its first state.
	/// </summary>
	// [BIN:TPI-EXE:0x5BCF90 arrival guest] kind = 0x6508F0 (0 bus, 1 heli, 2 mono, -1 none: the slot of the vehicle at work), cell = 0x5D79F0(kind), the allocation 0x638120(0x2F0) and the constructor
	// 0x60C3F0(cell) at 0x5BCFF4, which puts the thing at the cell's centre; no world draw before the constructor (guest-states-p0.md §2)
	private Guest? SpawnThemeParkIncArrival( ThemeParkIncGuestSettings settings )
	{
		var cell = settings.StopCell( arrivalVehicle );
		if ( !Grid.InBounds( cell.X, cell.Y ) )
			return null;
		var guest = Create();
		if ( guest == null )
			return null;
		PlaceAtCentre( guest, cell );
		ThemeParkIncConstructorTail( guest, guest.Tpi!, settings );
		return guest;
	}

	/// <summary>Whether a leaving guest waits at the stop (<c>0x6503F0</c>): the head of the BusStopA cell's thing chain is a guest in state 30.</summary>
	// [BIN:TPI-EXE:0x6503F0 pick-up test] four cell words are computed (A, A + 1, A − 0x100, A − 0xFF) but the loop bound is 1: only the head of cell A (world +0x440 + 72 × word) is read; it must be a
	// thing of kind 1 whose +0x2C0 is 0x1E
	private bool ThemeParkIncLeavingGuestWaits()
	{
		var stop = NativeTpi!.BusStopA;
		return Grid.InBounds( stop.X, stop.Y ) && ChainHead( stop ).Guest is { Tpi.State: ThemeParkIncGuestStates.WaitingToBePickedUp };
	}

	/// <summary>
	/// The vehicle dispatch of Theme Park Inc (<c>0x64FB90</c>) with no vehicle at work: every cached vehicle is destroyed (the bus, the heli, then the mono), then size 1, 2 or 3 picks the bus, the
	/// heli or the mono and size 0 one of them with a world draw; a new vehicle of that kind is built with its status 1. A kind the level lacks is tried again after the others (mono, heli, bus),
	/// the destroys run again before each try.
	/// </summary>
	// [BIN:TPI-EXE:0x64FB90 vehicle dispatch] game mode 3 (0xC65B80, the intro world) returns at once; +0x1EAB5E nonzero only sets its VAR 0 = 1 (0x64FF61); else 0x637C40 on +0x1EAB60, +0x1EAB62
	// and +0x1EAB64 (each zeroed), kind = size − 1, or for size 0 the inline step of the world generator (0x64FCBB: the state times 0x19660D plus 0x3C6EF35F, rotated right 13, its absolute value)
	// modulo 3; the feature named bus, heli or mono (0xAD42A8, 0xAD3BB8, 0xAA0FC0) in the feature list 0xA09AC8 is built (0x638120(0x148), 0x5DB1B0), its VAR 1 = 1 and it becomes +0x1EAB5E;
	// a missing one logs "Could not find the 'vehicle_name was here' feature" and goes back to 0x64FC2C with the untried kinds in the order mono, heli, bus (bits 4, 2, 1); all three tried leaves none
	// at work. Because the slots are cleared first, the cached branch 0x64FF17 is unreachable
	private void DispatchThemeParkIncVehicle( IArrivalVehicleHost host, int size )
	{
		if ( IntroWorld )
			return;
		var tried = 0;
		while ( true )
		{
			for ( var cached = 0; cached < ThemeParkIncVehicleNames.Count; cached++ )
				if ( host.Exists( cached ) )
					host.Destroy( cached );
			int kind;
			if ( tried == 0 )
				kind = size == 0 ? (int)(random.NextWord() % 3) : size - 1;
			else if ( (tried & 4) == 0 )
				kind = 2;
			else if ( (tried & 2) == 0 )
				kind = 1;
			else if ( (tried & 1) == 0 )
				kind = 0;
			else
			{
				Log?.Warning( "No arrival vehicle: the level has neither the bus, the heli nor the mono." );
				return;
			}
			tried |= 1 << kind;
			if ( !host.Create( kind ) )
				continue;
			host.SetVariable( kind, ArrivalVehicleKind.StatusVariable, 1 );
			arrivalVehicle = kind;
			return;
		}
	}
}

namespace OpenTPW;

/// <summary>
/// The arrival walk of tp.exe's guests, from the vehicle to the park (states 0 to 5): a guest is made at the vehicle's stop cell and walks the cell map to the
/// bus-side crossing cell (state 0), waits there for the vehicle to let it go (state 1, <c>0x004FF520</c>), walks to a ticket booth (state 2, <c>0x004FF730</c>), is
/// weighed at the booth (state 4, <c>0x004FF9D0</c>), waits as the head of its cell for the gate (state 3, <c>0x004FF7F0</c>) and walks on to the entrance cell (state 5,
/// <c>0x004FFB20</c>). Every walk is the original's route search and edge test over the cell map (<see cref="GuestNavigation.Navigate"/>); the lanes of the settings
/// only name the level's fixed cells (<c>FixedItemInfo</c>).
/// </summary>
public sealed partial class GuestSimulation
{
	/// <summary>The most turns state 3 holds a paid guest or a guest of a closed park: <c>R % 150 + 200</c> (<c>0x00501DB0</c> case 3).</summary>
	public const int GateWaitMaximum = 200 + 149;

	/// <summary>The settings' arrival lane of a side (0: A, 1: B) as the arrival chain reads it (the five fixed cells), the other side's when the level lacks it; null without one.</summary>
	private (int X, int Y)[]? ArrivalLane( int side )
	{
		var own = ValidLane( side == 0 ? Settings.ArrivalLaneA : Settings.ArrivalLaneB );
		if ( own is { Length: >= 5 } )
			return own;
		var other = ValidLane( side == 0 ? Settings.ArrivalLaneB : Settings.ArrivalLaneA );
		return other is { Length: >= 5 } ? other : null;
	}

	/// <summary>Whether the level has the fixed cells the arrival chain walks between.</summary>
	private bool HasArrivalLane => ArrivalLane( 0 ) != null;

	/// <summary>The stop cell a created guest stands on: the bus stop B cell for the bus, two rows up from it for the seaplane and the Ferry (tp.exe <c>0x004CF720</c>).</summary>
	// [BIN:TPW-EXE:0x004CF720 arrival guest cell] 0x0051AAD0 = 0 (the bus or none at work): the cell of 0x004D8650(1), FixedItemInfo.BusStopB (0x007855B4, 0x007855B8); else that id − 0x100; 0x004FAEC0 builds the guest there (0x004F8940 puts its position at the cell's centre, 0x005100E0 with 0x8000)
	private (int X, int Y)? ArrivalStopCell()
	{
		if ( ArrivalLane( 1 ) is not { } lane )
			return null;
		var (x, y) = lane[0];
		var cell = NonBusVehicleAtWork() ? (x, y - 2) : (x, y);
		return Grid.InBounds( cell.Item1, cell.Item2 ) ? cell : null;
	}

	/// <summary>The bus-side crossing cell a created guest walks to (tp.exe <c>0x004D8710</c>): the X of side A's or B's cell with side A's row.</summary>
	// [BIN:TPW-EXE:0x004D8710 bus-side crossing] the side-0 cell is (CrossingBSSideAPosX 0x007855DC, CrossingBSSideAPosY 0x007855E0), the side-1 cell (CrossingBSSideBPosX 0x007855E4, the same row)
	private (int X, int Y)? BusSideCrossingCell( int side )
	{
		if ( ArrivalLane( side ) is not { } lane || ArrivalLane( 0 ) is not { } rowLane )
			return null;
		return (lane[1].X, rowLane[1].Y);
	}

	/// <summary>The ticket booth cell of a side (tp.exe <c>0x004D8610</c>: <c>FixedItemInfo.TicketBoothA</c> or <c>B</c>).</summary>
	// [BIN:TPW-EXE:0x004D8610 ticket booth cell] side 0: (0x0078559C, 0x007855A0), else (0x007855A4, 0x007855A8), the cell id y × 128 + x + 1
	private (int X, int Y)? BoothCell( int side ) => ArrivalLane( side ) is { } lane ? lane[3] : null;

	/// <summary>The guests in state 3 (waiting at the booth) standing on the cell of either ticket booth, counted once per side as the original walks the two cells.</summary>
	// [BIN:TPW-EXE:0x004C91C0 guests at the gate] for each of the two sides 0 and 1 it takes the booth cell (0x004D8610) and counts the things of type 1 on it whose state word (+0x220) is 3, adds both counts and caches the sum (park +0x1F0D4, stamp +0x1F0D0, never read back)
	public int GuestsWaitingAtBooths()
	{
		var count = 0;
		for ( var side = 0; side < 2; side++ )
			if ( BoothCell( side ) is { } booth )
				count += Guests.Count( guest => guest.State == GuestState.WaitingAtBooth && guest.Navigation.Cell == booth );
		return count;
	}

	/// <summary>The entrance cell of a side (tp.exe <c>0x004D8690</c>: <c>FixedItemInfo.EntranceA</c> or <c>B</c>).</summary>
	// [BIN:TPW-EXE:0x004D8690 entrance cell] side 0: (0x007855BC, 0x007855C0), else (0x007855C4, 0x007855C8)
	private (int X, int Y)? ParkEntranceCell( int side ) => ArrivalLane( side ) is { } lane ? lane[4] : null;

	/// <summary>Puts a guest at the centre of a cell on the cell map (the thing constructor's position, tp.exe <c>0x004F8940</c>: <c>0x005100E0</c> with the cell's high half and 0x8000).</summary>
	private void PlaceAtCentre( Guest guest, (int X, int Y) cell )
	{
		guest.OnGrid = true;
		guest.Lane = null;
		(guest.CellX, guest.CellY) = cell;
		guest.PreviousCellX = -1;
		guest.PreviousCellY = -1;
		var navigation = guest.Navigation;
		navigation.OffGrid = false;
		(navigation.X, navigation.Y) = GuestNavigation.Centre( cell );
		navigation.SetDestination( navigation.X, navigation.Y, [] );
		guest.HasWaypoint = false;
		(guest.StepFromX, guest.StepFromY, guest.StepFromDistance, guest.StepTick) = (guest.X, guest.Y, guest.DistanceWalked, TickCount);
		UpdatePresentation( guest );
		Occupy( guest );
	}

	/// <summary>Spawns a guest of the arrival vehicle: made at the vehicle's stop cell, it walks to the bus-side crossing cell of the side a world draw picks.</summary>
	// [BIN:TPW-EXE:0x004FB201 arrival entrance] a guest whose cell is not a park cell (0x004FA990 false, every guest of the arrival vehicle) takes two draws after its reseed: R & 1 picks the crossing cell (0x004D8710) and the low byte of R is the sideways point in it (0x004FB21C), the y byte being 0xC8; 0x004FA5F0 asks for the walk there: a route makes state 0, none state 6 (0x004FB254); a level without arrival cells has no vehicle stop, and OpenTPW starts the guest on the park's first path cell
	public Guest? SpawnArrival()
	{
		if ( !HasArrivalLane || ArrivalStopCell() is not { } stop )
		{
			var start = FindSpawnCell();
			return start == null ? null : SpawnInPark( start.Value.X, start.Value.Y );
		}
		var guest = Create();
		if ( guest == null )
			return null;
		var side = (int)(random.NextWord() & 1);
		var sideways = (int)(random.NextWord() & 0xFF);
		PlaceAtCentre( guest, stop );
		var crossing = BusSideCrossingCell( side )!.Value;
		EnterGuestState( guest, RouteToPoint( guest, crossing, sideways, ArrivalRowByte ) ? GuestState.CrossingRoad : GuestState.DoingNothing );
		return guest;
	}

	/// <summary>The y byte of the point a created guest walks to in the crossing cell (0xC8).</summary>
	private const int ArrivalRowByte = 0xC8;

	/// <summary>A route request to the point of a cell with the given sub-cell bytes (tp.exe <c>0x004FA5F0</c>); false when the walk search fails and the walk stands arrived and stuck.</summary>
	private bool RouteToPoint( Guest guest, (int X, int Y) cell, int byteX, int byteY )
	{
		var (x, y) = GuestNavigation.BytePoint( cell, byteX, byteY );
		var found = guest.Navigation.Navigate( Grid.Cells, x, y );
		guest.HasWaypoint = true;
		return found;
	}

	/// <summary>
	/// State 0 (tp.exe <c>0x00501A0F</c>): the walk from the vehicle's stop to the bus-side crossing cell. Arriving makes state 1 (the heading <c>+0x1C</c> = 0x400 is not modelled);
	/// a walk that ends stuck stays in state 0, as <c>0x00501A32</c> leaves it.
	/// </summary>
	// [BIN:TPW-EXE:0x00501A0F state 0] the walk step 0x004FA2A0: 0 sets the heading +0x1C = 0x400 and SetState(1); 2 logs "Big problem - no way this should happen" and stays; 1 keeps walking
	private void UpdateStepOff( Guest guest )
	{
		if ( !guest.HasWaypoint )
		{
			EnterGuestState( guest, GuestState.CrossingRoadToPark );
			return;
		}
		var result = MoveAlongStep( guest );
		if ( result == WalkResult.Arrived )
			EnterGuestState( guest, GuestState.CrossingRoadToPark );
		else if ( result == WalkResult.Stuck )
			guest.HasWaypoint = true;
	}

	/// <summary>
	/// State 1 (tp.exe <c>0x004FF520</c>): once the vehicle lets the guests walk (<see cref="StopWalkAllowed"/>, <c>0x0051A760</c>) a world draw picks the side of the ticket booth and two more the point
	/// in its cell; the route request is state 2's walk.
	/// </summary>
	// [BIN:TPW-EXE:0x004FF520 state 1] 0x0051A760 nonzero: R & 1 → 0x004D8610 (the booth cell), the x byte R, the y byte R, 0x004FA5F0 (its result is not read), SetState(2)
	private void UpdateWaitingToWalk( Guest guest )
	{
		if ( !turnStarted || !StopWalkAllowed() )
			return;
		var side = (int)(random.NextWord() & 1);
		var byteX = (int)(random.NextWord() & 0xFF);
		var byteY = (int)(random.NextWord() & 0xFF);
		if ( BoothCell( side ) is not { } booth )
		{
			EnterGuestState( guest, GuestState.DoingNothing );
			return;
		}
		RouteToPoint( guest, booth, byteX, byteY );
		EnterGuestState( guest, GuestState.GoingToTicketBooth );
	}

	/// <summary>
	/// State 2 (tp.exe <c>0x004FF730</c>): the speed term, then the walk to the booth. Arriving is state 4 (the booth) while the park is open, state 3 while it is closed; a walk that ends stuck
	/// stays in state 2.
	/// </summary>
	// [BIN:TPW-EXE:0x004FF730 state 2] the purpose speed +0xC2 (BoothWalkSpeedTerm), the walk step 0x004FA2A0: 0 is SetState(4) with the park open (0x0051A280 = 0), SetState(3) with it closed; 2 logs "Big problem" and stays
	private void UpdateWalkToBooth( Guest guest )
	{
		if ( turnStarted )
			guest.ToiletBonus = BoothWalkSpeedTerm( guest );
		if ( !guest.HasWaypoint )
		{
			ArriveAtBooth( guest );
			return;
		}
		var result = MoveAlongStep( guest );
		if ( result == WalkResult.Arrived )
			ArriveAtBooth( guest );
		else if ( result == WalkResult.Stuck )
			guest.HasWaypoint = true;
	}

	private void ArriveAtBooth( Guest guest )
	{
		if ( Payments?.ParkClosed == true )
		{
			EnterWaitingAtBooth( guest );
			return;
		}
		// [BIN:TPW-EXE:0x004FF730 state 2 arrival] the walk's result 0 with the park open is SetState(4) (0x00501DB0 case 4: the animation 3 and, on the booth cell 0x004D8610(0), the facing 0x200); the verdict is the next update's
		EnterGuestState( guest, GuestState.AtTicketBooth );
		guest.StateTimer = 0;
	}

	/// <summary>SetState(3) (tp.exe <c>0x00501DB0</c> case 3): the wait <c>+0x200</c> is <c>R % 150 + 200</c>.</summary>
	// [BIN:TPW-EXE:0x00501DB0 state 3 entry] the animation 3 (0x004217F0), then a world draw: +0x200 = R % 0x96 + 200
	private void EnterWaitingAtBooth( Guest guest )
	{
		EnterGuestState( guest, GuestState.WaitingAtBooth );
		guest.BoothWait = (int)(random.NextWord() % 150) + 200;
	}

	/// <summary>
	/// State 3 (tp.exe <c>0x004FF7F0</c>), at the booth. A closed park or a gate whose script variable 1 is not 1 (<see cref="EntranceGateOpen"/>) counts the wait down and then sends the guest
	/// home; otherwise a guest that has not paid returns to the booth, and one that has walks on to the entrance cell of a side a world draw picks once it heads its cell's list.
	/// </summary>
	// [BIN:TPW-EXE:0x004FF7F0 state 3] 0x0051A280 or 0x0051A290 != 1: +0x200 nonzero counts down and returns, else +0x188 = 1, R & 1 → 0x004D86D0, 0x004FA530, SetState(0x12), +0x1BC = 0; paid (+0x204): the cell's list head (cell +0x20 − cell record base 0x44 × id) must be this thing, else R & 1 → 0x004D8690, 0x004FA530, SetState(5); unpaid: the cell of 0x004D8610(0), else of (1), else R & 1 picks the booth, 0x004FA530, +0x200 = 0, SetState(2)
	private void UpdateWaitingAtBooth( Guest guest )
	{
		if ( !turnStarted )
			return;
		if ( Payments?.ParkClosed == true || EntranceGateOpen?.Invoke() == false )
		{
			if ( guest.BoothWait != 0 )
			{
				guest.BoothWait--;
				return;
			}
			SendHome( guest, DepartureCause.ParkClosed );
			return;
		}
		if ( !guest.Paid )
		{
			var here = guest.Navigation.Cell;
			var target = BoothCell( 0 ) == here ? BoothCell( 0 ) : BoothCell( 1 ) == here ? BoothCell( 1 ) : BoothCell( (int)(random.NextWord() & 1) );
			if ( target is { } booth )
				RouteToCell( guest, booth );
			guest.BoothWait = 0;
			EnterGuestState( guest, GuestState.GoingToTicketBooth );
			return;
		}
		if ( !HeadsItsCell( guest ) )
			return;
		if ( ParkEntranceCell( (int)(random.NextWord() & 1) ) is { } entrance )
			RouteToCell( guest, entrance );
		EnterGuestState( guest, GuestState.EnteringPark );
	}

	/// <summary>Whether the guest heads the thing list of the cell it stands on (the first of the cell's things in the original, a staff member that entered the cell later heading it instead; WALK-003).</summary>
	private bool HeadsItsCell( Guest guest ) => ChainHead( guest.Navigation.Cell ).Guest == guest;

	/// <summary>
	/// State 5 (tp.exe <c>0x004FFB20</c>): the walk to the entrance cell. A closed park or a shut gate sends the guest back to a booth (state 2); arriving numbers the visitor
	/// (<c>0x0051AAF0</c>) and makes state 6; a walk that ends stuck stays in state 5.
	/// </summary>
	// [BIN:TPW-EXE:0x004FFB20 state 5] 0x0051A280 or 0x0051A290 != 1: R & 1 → 0x004D8610, 0x004FA530, SetState(2); else the walk step 0x004FA2A0: 0 is +0x1D8 = 0x0051AAF0 (the park's visitor count + 1) and SetState(6), 2 logs "Big problem" and stays
	private void UpdateEnteringPark( Guest guest )
	{
		if ( !turnStarted )
			return;
		if ( Payments?.ParkClosed == true || EntranceGateOpen?.Invoke() == false )
		{
			if ( BoothCell( (int)(random.NextWord() & 1) ) is { } booth )
				RouteToCell( guest, booth );
			EnterGuestState( guest, GuestState.GoingToTicketBooth );
			return;
		}
		var arrived = !guest.HasWaypoint;
		if ( !arrived )
		{
			var result = MoveAlongStep( guest );
			if ( result == WalkResult.Walking )
				return;
			if ( result == WalkResult.Stuck )
			{
				guest.HasWaypoint = true;
				return;
			}
		}
		guest.VisitorNumber = ++VisitorCount;
		EnterGuestState( guest, GuestState.DoingNothing );
	}

	/// <summary>
	/// The way home from the booth and from state 3 (tp.exe <c>0x004FF9D0</c> case 0, <c>0x004FF7F0</c>): the walk mask is 1, a world draw picks one of the gate's two crossing cells
	/// (<c>0x004D86D0</c>) and the route request to its centre is state 18's walk, with the exit counter 0 (no recall).
	/// </summary>
	// [BIN:TPW-EXE:0x004FF9D0 home from the booth] +0x188 = 1, R & 1 → 0x004D86D0, 0x004FA530 (its result is not read), SetState(0x12) and +0x1BC = 0; the happiness-0 branch of the expensive verdict (0x004FFA9D) takes SetState(0x12) and +0x1BC = 0 alone, without a mask or a route
	private void SendHome( Guest guest, DepartureCause cause )
	{
		guest.Navigation.Mask = 1;
		var side = (int)(random.NextWord() & 1);
		if ( GateCell( side ) is { } gate )
		{
			RouteToCell( guest, gate );
			guest.ExitSide = side;
		}
		BeginLeaving( guest, cause );
	}

	/// <summary>SetState(0x12) with the exit counter zeroed (<c>+0x1BC</c>): state 18's recall needs a counter above 0, so a guest sent home is not recalled.</summary>
	private void BeginLeaving( Guest guest, DepartureCause cause )
	{
		guest.ExitLevel = 0;
		EnterGuestState( guest, GuestState.LeavingPark );
		Observer?.Leaving( guest, cause );
	}

	/// <summary>The park's visitor count (game <c>+0x1DA714</c>), raised by every guest numbered at the entrance (<c>0x0051AAF0</c>).</summary>
	public int VisitorCount { get; private set; }

	/// <summary>
	/// A guest saved before schema 9 on an arrival lane, which walked the lane's cells off the grid, continues on the cell map in the state its lane position stood for: the walk to
	/// the crossing cell, the wait there, the walk to the booth, the booth and the walk to the entrance, or on its way home from the booth the walk to its side's gate cell
	/// (state 18, mask 1, no recall: the lane walk never recalled) and a guest waiting at the bus stop in state 21. No draw is taken: the routes end at the guest's own sub-cell point.
	/// </summary>
	// [EXT:legacy-save-arrival-lane] a lane position has no counterpart in the original, whose guests walk the cell map from their creation (0x004FB201); the conversion keeps a guest of an OpenTPW save made before the arrival walk where it stands and gives it the state its lane position stood for, instead of dropping the guest or redrawing its walk
	private void ConvertLegacyLane( Guest guest )
	{
		var lane = guest.Lane!;
		var reversed = guest.LaneReversed;
		var index = Math.Clamp( guest.LaneIndex, 0, lane.Length - 1 );
		var side = ValidLane( Settings.ArrivalLaneB ) is { } b && b.SequenceEqual( lane ) ? 1 : 0;
		guest.Lane = null;
		guest.LaneIndex = 0;
		guest.LaneReversed = false;
		guest.OnGrid = true;
		guest.Navigation.OffGrid = false;
		(guest.CellX, guest.CellY) = guest.Navigation.Cell;
		var bytes = OffsetBytes( guest );
		bool WalkTo( (int X, int Y)? cell ) => cell is { } target && RouteToPoint( guest, target, bytes.X, bytes.Y ) | true;
		if ( !reversed )
		{
			switch ( guest.State )
			{
				case GuestState.CrossingRoad:
					WalkTo( lane.Length > 1 ? lane[1] : null );
					break;
				case GuestState.GoingToTicketBooth:
					WalkTo( BoothCell( side ) );
					break;
				case GuestState.EnteringPark:
					// the lane walk to the entrance came after the booth took the fee, so the guest has paid (a closed park turning it back must not charge it again)
					guest.Paid = true;
					WalkTo( ParkEntranceCell( side ) );
					break;
				default:
					guest.HasWaypoint = false;
					break;
			}
			return;
		}
		guest.Navigation.Mask = 1;
		if ( guest.State == GuestState.WaitingToGoHome )
		{
			guest.HasWaypoint = false;
			return;
		}
		guest.ExitLevel = 0;
		guest.State = GuestState.LeavingPark;
		// a guest saved on its way home from the booth: the booth turned it back
		Observer?.Leaving( guest, DepartureCause.BoothRefused );
		WalkTo( GateCell( side ) );
	}
}

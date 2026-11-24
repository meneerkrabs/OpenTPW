namespace OpenTPW;

// Theme Park Inc's native guest states, phase P3a (docs/reverse/TPI-GUESTS.md): the walk to a ride (state 13, 0x616AC0), standing in its queue (state 14, 0x6176C0), the queue step (state 15,
// 0x61A1DB) and the queue functions they call (0x619530, 0x619700, 0x5E0280, 0x5E0100, 0x5DFF50, 0x5DFF10, 0x5DFDD0, 0x5E0520, 0x60B7F0). The queue list is the ride bridge's: Theme Park Inc's list
// through the guest words +0x2CC/+0x2CE is Theme Park World's through +0x228/+0x22A (tp.exe 0x004DDB90, 0x004DDD20, 0x004DDF50) instruction for instruction, the field offsets aside. The boarding
// from state 16 on is phase P3b's: a guest the ride calls to its stand point is handed to Theme Park World's boarding walk (the bridged state 16) and comes back as an idle guest.
public sealed partial class GuestSimulation
{
	/// <summary>Whether this simulation hosts <paramref name="attraction"/> (the ride bridges registered for its guests).</summary>
	internal bool Hosts( IRideVisitorBridge attraction ) => attractions.Contains( attraction );

	/// <summary>The attraction thing <paramref name="thingId"/> as the native guests read it (the slot <c>0xAA18B0 + 20 × id</c>), or null when no hosted attraction holds it.</summary>
	private ThemeParkIncAttractionView? TpiAttraction( int thingId )
	{
		if ( thingId == 0 || ThemeParkIncWorld?.Attractions() is not { Count: > 0 } list )
			return null;
		for ( var index = 0; index < list.Count; index++ )
			if ( list[index].ThingId == thingId )
				return list[index];
		return null;
	}

	/// <summary>The attraction view whose ride bridge is <paramref name="ride"/>, or null.</summary>
	private ThemeParkIncAttractionView? TpiAttractionOf( IRideVisitorBridge ride )
	{
		if ( ThemeParkIncWorld?.Attractions() is not { Count: > 0 } list )
			return null;
		for ( var index = 0; index < list.Count; index++ )
			if ( ReferenceEquals( list[index].Ride, ride ) )
				return list[index];
		return null;
	}

	/// <summary>The back-of-queue cell word (<c>0x5E0520</c>: the cached back <c>+0x4A</c>, Theme Park World's <c>0x004DE110</c>), 0 without one.</summary>
	// [BIN:TPI-EXE:0x5E0520 back cell] 0x5E0550 returns the cached word +0x4A, or walks the queue (0x5E03C0) to its back and caches it: tp.exe 0x004DE110/0x004DE130 (RideVisitorBridge.BackCell)
	private static ushort TpiBackWord( IRideVisitorBridge ride ) =>
		ride.BackCell is { } back ? ThemeParkIncGuestSettings.CellWord( back.X, back.Y ) : (ushort)0;

	// ---- The queue list ----------------------------------------------------------------------------------------------------------

	/// <summary>
	/// The place of <paramref name="guestId"/> in the ride's queue (<c>0x5E0280</c>), −1 when the list does not reach it; with id 0, the list's length. The walk from the head stops at a guest outside
	/// the queue states (<c>0x61AC40</c>), which it still counts.
	/// </summary>
	// [BIN:TPI-EXE:0x5E0280 queue place] head +0x4C; equal to the id: the count so far; else count + 1, the thing's next +0x2CC when 0x61AC40 holds, 0 otherwise; at 0 the length for id 0, else "Could not find
	// person %d in the queue" and −1. tp.exe's 0x004DDF50 with its predicate 0x00502430
	internal int TpiQueuePosition( RideVisitorBridge ride, int guestId )
	{
		var index = 0;
		for ( var current = ride.QueueHead; current != 0; current = ride.NextInQueue( current ), index++ )
		{
			if ( current == guestId )
				return index;
			if ( !(Find( current ) is { Tpi: { } tpi } && ThemeParkIncGuestStates.InQueueOrLoading( tpi.State, tpi.Saved )) )
				return guestId == 0 ? index + 1 : -1;
		}
		return guestId == 0 ? index : -1;
	}

	/// <summary>The queue has room (<c>0x5DFDD0</c>): its length below four places a cell (<c>+0x50</c>).</summary>
	// [BIN:TPI-EXE:0x5DFDD0 queue room] 0x5E0280(0) < 4 × [+0x50], unsigned: tp.exe's 0x004DDA20 over +0x40
	private bool TpiQueueHasRoom( RideVisitorBridge ride ) => (uint)TpiQueuePosition( ride, 0 ) < (uint)(RideVisitorBridge.PositionsPerCell * ride.QueueSizeInCells);

	/// <summary>
	/// The queue limit <c>0x5DFE00</c>: 100 for a thing without a queue (<c>+0x42</c> bit 8, <c>Info.HasQueue</c>), else <c>_ftol(max(QWTC × capacity × g / duration, 4.0))</c> with g the speed over the
	/// level's InitSpeed (1 at speed 0), in the game thread's 24-bit precision and in Theme Park Inc's order (Theme Park World's <see cref="RideVisitorBridge.ComputeQueueLimit"/> multiplies g × capacity first).
	/// </summary>
	// [BIN:TPI-EXE:0x5DFE00 queue limit] +0x42 bit 8 clear: 100; else speed +0x6C above 0: fildll(speed) / fidivl(type +0x29C + 72 × level) stored as a single (0xA9D3D4), else 1.0f; flds type +0x2A8 (QWTC),
	// fimull the byte +0x71 (capacity), fmuls g, fidivl the byte +0x70 (duration); not above 4.0 (or unordered) gives 4.0; _ftol
	internal static uint ThemeParkIncQueueLimit( QueueParameters parameters )
	{
		if ( !parameters.HasQueue )
			return RideVisitorBridge.NoQueueLimit;
		var speed = (uint)parameters.Speed;
		var g = speed == 0 ? 1f : (float)((double)speed / parameters.InitSpeed);
		var value = (float)((float)(parameters.QueueWaitTimeConstant * (double)(parameters.Capacity & 0xFF)) * g);
		value = (float)((double)value / (parameters.Duration & 0xFF));
		if ( !(value > 4f) )
			value = 4f;
		return unchecked( (uint)ThemeParkIncX87Single.Truncate( value ) );
	}

	/// <summary>The queue is too long (<c>0x5DFF10</c>): its length at least the limit.</summary>
	// [BIN:TPI-EXE:0x5DFF10 queue too long] 0x5E0280(0) against 0x5DFE00, unsigned: 1 when not below
	private bool TpiQueueTooLong( RideVisitorBridge ride ) => (uint)TpiQueuePosition( ride, 0 ) >= ThemeParkIncQueueLimit( ride.Parameters );

	/// <summary>The guest joins the back of the list (<c>0x5DFF50</c>).</summary>
	// [BIN:TPI-EXE:0x5DFF50 add to queue] "Object %d adding person %d to queue": an empty list takes it as its head +0x4C; else the tail (the first thing whose +0x2CC is 0) links it; its +0x2CE is the old tail
	// (0 for a new head) and its +0x2CC is 0: tp.exe's 0x004DDB90
	private void TpiAddToQueue( RideVisitorBridge ride, Guest guest )
	{
		ride.AppendToQueue( guest.Id );
		SyncThemeParkIncQueueLinks( ride );
	}

	/// <summary>The guest leaves the list (<c>0x5E0100</c>); the ride's script forgets it in VAR_LETMEON.</summary>
	// [BIN:TPI-EXE:0x5E0100 remove from queue] "Object %d removing person %d from queue": VAR 0 (VAR_LETMEON) equal to the id is set to 0 (0x6B0780); its +0x2CE (or the head when 0) takes its +0x2CC
	// and back; its own words are cleared: tp.exe's 0x004DDD20 (RideVisitorBridge.LeaveQueue)
	private void TpiRemoveFromQueue( RideVisitorBridge ride, Guest guest )
	{
		ride.LeaveQueue( guest.Id );
		if ( guest.Tpi is { } tpi )
			(tpi.QueueLink, tpi.QueueSlot) = (0, 0);
		SyncThemeParkIncQueueLinks( ride );
	}

	/// <summary>Writes the list's links into the native guests' words <c>+0x2CC</c> (next, <see cref="ThemeParkIncGuestRecord.QueueLink"/>) and <c>+0x2CE</c> (previous, <see cref="ThemeParkIncGuestRecord.QueueSlot"/>).</summary>
	private void SyncThemeParkIncQueueLinks( RideVisitorBridge ride )
	{
		for ( var current = ride.QueueHead; current != 0; current = ride.NextInQueue( current ) )
		{
			if ( Find( current ) is { Tpi: { } tpi } )
				(tpi.QueueSlot, tpi.QueueLink) = (ride.PreviousInQueue( current ), ride.NextInQueue( current ));
		}
	}

	/// <summary>
	/// FindQueueDestination (<c>0x619530</c>): the guest's place in the list goes into <c>+0x21B</c>, the place's queue slot (one world draw for its sideways byte) is the walk's target and a walk sets state 15.
	/// False with no change but the place when the list does not hold the guest or no route leads to the slot.
	/// </summary>
	// [BIN:TPI-EXE:0x619530 find queue destination] 0x5E0280 −1 returns 0; +0x21B = the place (a byte); 0x5E0C70 (the slot: tp.exe's 0x004DE7E0/0x004DE840 with the world draw R % 28 + 0x72 for the sideways
	// byte, QueueSlotPoint); 0x60B8E0 to the slot: nonzero sets 15 and returns 1, zero "QQQ - FindQueueDestination SetDest failed, so I'm standing in queue" and returns 0
	private bool TpiFindQueueDestination( Guest guest, ThemeParkIncGuestRecord tpi, RideVisitorBridge ride )
	{
		var position = TpiQueuePosition( ride, guest.Id );
		if ( position < 0 )
			return false;
		tpi.Byte21B = position & 0xFF;
		guest.QueuePosition = tpi.Byte21B;
		var (_, x, y) = QueueSlotPoint( ride, position, NextLateral() );
		if ( !RouteToTpiPoint( guest, (x >> 16, y >> 16), (x >> 8) & 0xFF, (y >> 8) & 0xFF ) )
			return false;
		SetThemeParkIncState( guest, tpi, ThemeParkIncGuestStates.TakingQueueStep );
		return true;
	}

	/// <summary>
	/// The dismissal <c>0x619700</c>: unless <paramref name="quietly"/>, the sound 0x80 for one thing id in eight and <c>MediumHappinessChange</c> lost; then the queue words, the weather wait, the call, the
	/// target and the place are cleared and the guest is idle (state 6). The list itself is the caller's.
	/// </summary>
	// [BIN:TPI-EXE:0x619700 dismissal] "Person %d: dismissed from queue on ride %d"; the argument 0: (id & 7) == 0 plays 0x80 (0x60BE90 with [0xAE011C]) and 0x614460(1); then +0x2CE, +0x2CC, +0x28C,
	// +0x290, +0x220, +0x204 and +0x21B = 0 and setter(6). tp.exe's 0x005012F0 has no argument and always takes the step
	private void TpiDismissFromQueue( Guest guest, ThemeParkIncGuestRecord tpi, bool quietly )
	{
		if ( !quietly )
		{
			if ( (guest.Id & 7) == 0 )
				Voiced?.Invoke( guest, DismissalVoiceEvent );
			guest.Happiness = MeterMath.Add( guest.Happiness, -HappinessChange( NativeTpi!, 1 ) );
		}
		(tpi.QueueSlot, tpi.QueueLink, tpi.Byte28C, tpi.Word290, tpi.Word220, tpi.Target, tpi.Byte21B) = (0, 0, 0, 0, 0, 0, 0);
		ClearTpiQueueMirror( guest );
		SetThemeParkIncState( guest, tpi, ThemeParkIncGuestStates.Idle );
	}

	/// <summary>The fields of the shared record the ride bridge reads for a native guest in a queue (<see cref="IRideVisitorHost.IsStandingAtFront"/>), cleared when it leaves.</summary>
	private static void ClearTpiQueueMirror( Guest guest )
	{
		guest.AttractionId = 0;
		ClearQueueState( guest );
	}

	/// <summary>The ride counts a guest who gave up its queue (<c>0x5E5D70</c>): its walk-away counter and the daily walk-away ring.</summary>
	// [BIN:TPI-EXE:0x5E5D70 walk away] +0xA4 += 1 and the history object's (+0x130) ring +0x198 += 1: tp.exe's 0x004E1670 (mNumWalkAways +0x1A4 and +0x230)
	private void TpiWalkedAway( RideVisitorBridge ride ) => Payments?.WalkedAway( ride );

	/// <summary>The temporary history takes a word (<c>0x611D20</c>, bug-compatible: <c>[a, b, c, d]</c> becomes <c>[w, a, b, b]</c>, see <see cref="ShiftThemeParkIncHistory"/>).</summary>
	private static void PushThemeParkIncHistory( int[] history, int word )
	{
		ShiftThemeParkIncHistory( history );
		history[0] = word & 0xFFFF;
	}

	/// <summary>The common exit of states 13 and 14: out of the list, dismissed (with the happiness step), the ride's walk-away count and idle.</summary>
	// [BIN:TPI-EXE:0x617CBE queue exit] 0x5E0100, 0x619700(0), 0x5E5D70 and setter(6)
	private void TpiLeaveQueue( Guest guest, ThemeParkIncGuestRecord tpi, RideVisitorBridge ride, bool quietly = false )
	{
		TpiRemoveFromQueue( ride, guest );
		TpiDismissFromQueue( guest, tpi, quietly );
		TpiWalkedAway( ride );
		SetThemeParkIncState( guest, tpi, ThemeParkIncGuestStates.Idle );
	}

	// ---- State 13 ----------------------------------------------------------------------------------------------------------------

	/// <summary>
	/// State 13 (<c>0x616AC0</c>), the walk to the chosen ride: the walk step first; a closed ride or a stuck walk give up (<c>BigHappinessChange</c>); on arrival at the back of the queue the guest checks the
	/// room, the excitement and the length and joins (state 15); while walking a closed park gives up, an umbrella holder drops a balloon shop and fair weather an umbrella shop, and every twelfth update makes
	/// a minor decision.
	/// </summary>
	// [BIN:TPI-EXE:0x616AC0 state 13] the target slot empty: +0x204 = 0, setter(6); 0x60B440; [ride+0x7C] (the open word) 0 or the walk 2: "stuck on their way", 0x614460(2), +0x204 = 0, setter(6);
	// 0: the arrival; 1: the walking branch 0x616D74. tp.exe's state 10 (0x004FFBC0) reads neither the open word nor a missing thing
	internal void UpdateTpiWalkingToMajor( Guest guest, ThemeParkIncGuestRecord tpi )
	{
		var settings = NativeTpi!;
		if ( TpiAttraction( tpi.Target ) is not { } view )
		{
			// [EXT:original-undefined-reads] the removal message (0x60D120) clears every walker's target before the thing goes, so an empty slot does not occur; a target no hosted attraction holds (a
			// shop of another kind) is read as the empty slot, as 0x616ADE reads it
			tpi.Target = 0;
			ClearTpiQueueMirror( guest );
			SetThemeParkIncState( guest, tpi, ThemeParkIncGuestStates.Idle );
			return;
		}
		var ride = view.Ride;
		var walk = TpiWalk( guest );
		if ( !ride.OpenWord || walk == WalkResult.Stuck )
		{
			TpiGiveUpRide( guest, tpi, HappinessChange( settings, 2 ) );
			return;
		}
		if ( walk == WalkResult.Arrived )
		{
			ArriveAtTpiQueue( guest, tpi, settings, view );
			return;
		}
		// [BIN:TPI-EXE:0x616D74 walking] 0x64FA30 (the park closed): "The park has closed underneath me!", the stuck exit
		if ( Payments?.ParkClosed == true )
		{
			TpiGiveUpRide( guest, tpi, HappinessChange( settings, 2 ) );
			return;
		}
		// [BIN:TPI-EXE:0x616D8A shop gates] the target's AppearanceEffect (type +0x21C) 1, a balloon shop, with the umbrella state 4 or 3, or 3, an umbrella shop, in weather other than 2, 3 and 4: +0x204 = 0
		// and setter(6), no happiness step
		var weather = ArrivalWeatherState?.Invoke() ?? UmbrellaRules.WeatherNone;
		if ( view.Terms.AppearanceEffect == 1 && guest.UmbrellaState is UmbrellaRules.StateOpen or UmbrellaRules.StateOpening
			|| view.Terms.AppearanceEffect == 3 && weather is not (UmbrellaRules.WeatherRain or UmbrellaRules.WeatherStorm or UmbrellaRules.WeatherSnow) )
		{
			TpiGiveUpRide( guest, tpi, 0 );
			return;
		}
		// [BIN:TPI-EXE:0x616E02 walk count] the byte +0x40 counts the updates; above 11 it is 0 again and the minor decision 0x6114B0 runs: tp.exe's +0x2C and 0x004FD570
		if ( ++guest.RideWalkCount > 11 )
		{
			guest.RideWalkCount = 0;
			ThemeParkIncMinorDecision( guest, tpi );
		}
	}

	/// <summary>State 13's give-up exits: the happiness step (none for the shop gates), the target cleared and idle.</summary>
	private void TpiGiveUpRide( Guest guest, ThemeParkIncGuestRecord tpi, float loss )
	{
		guest.Happiness = MeterMath.Add( guest.Happiness, -loss );
		tpi.Target = 0;
		ClearTpiQueueMirror( guest );
		SetThemeParkIncState( guest, tpi, ThemeParkIncGuestStates.Idle );
	}

	/// <summary>
	/// State 13's arrival (<c>0x616AFC</c>): the back of the queue must be the guest's own cell (else the walk there again); then the room, the excitement refusal (thoughts 12 and 15), the length (thought 16)
	/// and the join: the happiness at the join, the list and FindQueueDestination.
	/// </summary>
	// [BIN:TPI-EXE:0x616AFC arrival] the cell word 1 + x + (y << 7) against 0x5E0520: different, "The back of the queue has moved", 0x5E0520 again, 0 sets 6, else 0x60B7F0 keeps walking or sets 6 (the target
	// kept); 0x5DFDD0 zero sets 6 (the target kept); e = 0x611410 (the difference, 100 for a worn relief object), 0x5E41D0(1) nonzero and |e| at least 45: thought 12 for e below 0, else 15, the history
	// word 0x611D20(+0x204), +0x204 = 0, +0x224 = 0, setter(6); 0x5DFF10: "queue is too long", 0x611D20, thought 16, +0x204 = 0, setter(6), 0x5E5D70 and the ring +0x550 of the history object; else
	// +0x234 = +0x1BC, 0x5DFF50, 0x619530 (state 15) or "Couldn't get to my place in the queue": 0x5E0100, 0x619700(0), setter(6)
	private void ArriveAtTpiQueue( Guest guest, ThemeParkIncGuestRecord tpi, ThemeParkIncGuestSettings settings, ThemeParkIncAttractionView view )
	{
		var ride = view.Ride;
		var back = TpiBackWord( ride );
		if ( back != TpiCellWord( guest ) )
		{
			back = TpiBackWord( ride );
			if ( back == 0 || !RouteToTpiCellWord( guest, back ) )
				SetThemeParkIncState( guest, tpi, ThemeParkIncGuestStates.Idle );
			return;
		}
		if ( !TpiQueueHasRoom( ride ) )
		{
			SetThemeParkIncState( guest, tpi, ThemeParkIncGuestStates.Idle );
			return;
		}
		var difference = ride.JoinDifference( tpi.PreferredExcitement & 0xFF );
		if ( (ride.StaticExcitementLevel & 0xFF) != 0 && Math.Abs( difference ) >= QueueJoinRules.Gate )
		{
			Think( guest, difference < 0 ? GuestThought.Bored : GuestThought.Scared );
			PushThemeParkIncHistory( guest.TemporaryHistory, tpi.Target );
			tpi.Target = 0;
			tpi.Word224 = 0;
			ClearTpiQueueMirror( guest );
			SetThemeParkIncState( guest, tpi, ThemeParkIncGuestStates.Idle );
			return;
		}
		if ( TpiQueueTooLong( ride ) )
		{
			PushThemeParkIncHistory( guest.TemporaryHistory, tpi.Target );
			Think( guest, GuestThought.QueueTooLong );
			tpi.Target = 0;
			ClearTpiQueueMirror( guest );
			SetThemeParkIncState( guest, tpi, ThemeParkIncGuestStates.Idle );
			TpiWalkedAway( ride );
			// [EXT:tpi-capability-gates] the history object's ring +0x550 (ring 10, ThemeParkIncAttractionRing.DailyNeedChecksFailed) is not kept: OpenTPW holds only the three price rings and the
			// walk-away count of Theme Park Inc's attraction history, and nothing reads ring 10 (SAVE-940); it draws nothing
			return;
		}
		tpi.Word234 = BitConverter.SingleToInt32Bits( guest.Happiness );
		// [BIN:TPI-EXE:0x6119A6 join happiness] the use routine's fare term reads +0x234 (byte(+0x1BC) − byte(+0x234)) × 3, as tp.exe's does from its own copy: the bridged boarding's FinishUse reads
		// the shared record's QueueJoinHappiness, so the join mirrors +0x234 there
		guest.QueueJoinHappiness = guest.Happiness;
		guest.AttractionId = ride.AttractionId;
		guest.QueueJoinTurn = ParkTurn;
		TpiAddToQueue( ride, guest );
		if ( TpiFindQueueDestination( guest, tpi, ride ) )
			return;
		TpiRemoveFromQueue( ride, guest );
		TpiDismissFromQueue( guest, tpi, quietly: false );
		SetThemeParkIncState( guest, tpi, ThemeParkIncGuestStates.Idle );
	}

	// ---- State 14 ----------------------------------------------------------------------------------------------------------------

	/// <summary>
	/// State 14 (<c>0x6176C0</c>), standing in the queue: the call to the stand point; a worn relief object; the umbrella shop's weather (the delayed reaction <c>0x61795E</c>); the place, its move-up and the
	/// limit; a refusing coaster or track; then the needs window (more than 30 turns past the last spot animation) or the boredom and the turn of the head.
	/// </summary>
	// [BIN:TPI-EXE:0x6176C0 state 14] +0x204 0: "WARNING: Person standing in queue with no MajorDest", setter(6); the place +0x21B 0 with the call +0x220: 0x5E4670 (the ride's loading slot +0x80 is the
	// guest) gives +0x220 = 0, the stand point 0x5E1440(0) and 0x60B8E0: a walk sets 16 ("being loaded"), none "the player has removed the path", 0x5E46A0 and the exit; the slot naming another: the exit
	// with no thought (0x617871); then 0x61788B
	internal void UpdateTpiStandingInQueue( Guest guest, ThemeParkIncGuestRecord tpi )
	{
		if ( tpi.Target == 0 )
		{
			SetThemeParkIncState( guest, tpi, ThemeParkIncGuestStates.Idle );
			return;
		}
		if ( TpiAttraction( tpi.Target ) is not { } view )
		{
			// [EXT:original-undefined-reads] as in state 13: the removal message clears the target first; a target no hosted attraction holds is read as no target
			ClearTpiQueueMirror( guest );
			SetThemeParkIncState( guest, tpi, ThemeParkIncGuestStates.Idle );
			return;
		}
		var ride = view.Ride;
		if ( tpi.Byte21B == 0 && tpi.Word220 != 0 )
		{
			if ( ride.CalledGuest == guest.Id )
			{
				tpi.Word220 = 0;
				guest.QueueCalled = false;
				var (x, y) = StandPoint( ride );
				if ( RouteToTpiPoint( guest, (x >> 16, y >> 16), (x >> 8) & 0xFF, (y >> 8) & 0xFF ) )
				{
					BeginTpiBridgedBoarding( guest, tpi, ride );
					return;
				}
			}
			// [BIN:TPI-EXE:0x6177B7 path removed] 0x5E46A0 (the ride lets go of the admitted guest, tp.exe's 0x004E0AC0) and the exit; the bridge's LeaveQueue releases its loading slot
			TpiLeaveQueue( guest, tpi, ride );
			return;
		}
		var settings = NativeTpi!;
		// [BIN:TPI-EXE:0x61788B worn relief] 0x5E3990: a relief thing (+0x42 bit 1) whose repair byte (+0x54) is below 25.0 (also a full or burning bin and a dry plant, which take no queue): thought 14 and
		// the exit; tp.exe's 0x004E0390
		if ( ride.ReliefWornOut )
		{
			Think( guest, GuestThought.FeelingSick );
			TpiLeaveQueue( guest, tpi, ride );
			return;
		}
		if ( UpdateTpiQueueWeather( guest, tpi, settings, view ) )
			return;
		var position = TpiQueuePosition( ride, guest.Id );
		if ( position < 0 )
		{
			// [BIN:TPI-EXE:0x6179A8 lost place] "Problem with a queue - shouldn't be fatal": the exit
			TpiLeaveQueue( guest, tpi, ride );
			return;
		}
		if ( position != tpi.Byte21B )
		{
			// [BIN:TPI-EXE:0x617A2C move-up] +0x21C above 0 (unsigned) and the gap +0x21B − place at most 2 (unsigned): +0x21C −= 1; else 0x5E3950 (the ride's state 1, broken down) holds; else 0x619530, or
			// "Couldn't get to my intended queue position" and the exit; each goes on to the needs window 0x617A44 (tp.exe 0x005002C2)
			if ( tpi.Word21C != 0 && unchecked( (uint)(tpi.Byte21B - position) ) <= 2 )
				tpi.Word21C--;
			else if ( !ride.HoldsQueuedGuests && !TpiFindQueueDestination( guest, tpi, ride ) )
			{
				TpiLeaveQueue( guest, tpi, ride );
				return;
			}
		}
		else
		{
			// [BIN:TPI-EXE:0x617AE2 limit] the place above the limit 0x5DFE00 (unsigned): thought 16, 0x5E0100, 0x619700(0), 0x5E5D70, setter(6) and the history object's ring +0x550
			if ( (uint)tpi.Byte21B > ThemeParkIncQueueLimit( ride.Parameters ) )
			{
				Think( guest, GuestThought.QueueTooLong );
				TpiLeaveQueue( guest, tpi, ride );
				// [EXT:tpi-capability-gates] ring 10 (+0x550) is not kept, as for state 13's refusal
				return;
			}
			// [BIN:TPI-EXE:0x617B5E coaster or track] a coaster (type +0x14C 3, 0x5E80D0) that 0x485BB0 refuses, or a track ride of type 1 whose word +0x34 is 0: thought 13 and the exit (tp.exe 0x00500612)
			if ( ride.TrackRefusesGuests )
			{
				Think( guest, GuestThought.Angry );
				TpiLeaveQueue( guest, tpi, ride );
				return;
			}
		}
		UpdateTpiQueueNeeds( guest, tpi, ride );
	}

	/// <summary>
	/// The umbrella shop's weather in the queue (<c>0x6178BE</c>): in a queue for an umbrella shop with no rain, storm or snow, the heat wave sends the guest away at once (quietly); any other weather arms the
	/// delayed reaction once (<c>+0x28C</c>, <c>+0x290</c>: the CRT draw <c>0x61795E</c>) and sends it away quietly when the reaction falls due. True when the guest left.
	/// </summary>
	// [BIN:TPI-EXE:0x6178BE weather in the queue] type +0x21C (AppearanceEffect) 3, the weather +0x70 of 0x64EB80 not 2, 3 or 4: 1 is 0x5E0100, 0x619700(1), 0x5E5D70 and setter(6) (0x617924); else +0x28C 0:
	// +0x290 = rand() % [0xA01B64] (signed idiv) + (turn >> 2), +0x28C = 1, and on; +0x28C set: (turn >> 2) at least +0x290 (unsigned) is the same exit (0x6179EC), else on
	// [EXT:tpi-staff-crt-rand] user-approved 2026-11-22 (the four guest sites, 0x61795E among them): the CRT rand() (0x870D38, seeded from the clock) is the park's world generator, rand() % N as world.Next(N)
	private bool UpdateTpiQueueWeather( Guest guest, ThemeParkIncGuestRecord tpi, ThemeParkIncGuestSettings settings, ThemeParkIncAttractionView view )
	{
		if ( view.Terms.AppearanceEffect != 3 )
			return false;
		var weather = ArrivalWeatherState?.Invoke() ?? UmbrellaRules.WeatherNone;
		if ( weather is UmbrellaRules.WeatherRain or UmbrellaRules.WeatherStorm or UmbrellaRules.WeatherSnow )
			return false;
		var quarter = TpiTurn >> 2;
		if ( weather != UmbrellaRules.WeatherHeatWave )
		{
			if ( tpi.Byte28C == 0 )
			{
				tpi.Word290 = unchecked( random.Next( settings.SlowestWeatherChangeResponse ) + (int)quarter );
				tpi.Byte28C = 1;
				return false;
			}
			if ( quarter < unchecked( (uint)tpi.Word290 ) )
				return false;
		}
		TpiLeaveQueue( guest, tpi, view.Ride, quietly: true );
		return true;
	}

	/// <summary>
	/// The needs window of state 14 (<c>0x617A44</c>): more than 30 turns past the last spot animation (<c>+0x230</c>) the happiness bands decide (above 80 the mood animation, below 10 thought 11 and the
	/// exit, below 20 the can't-decide animation, else a toilet byte above 80 thinks 4 and leaves unless the ride gives relief); within it, 100 turns standing (<c>+0x224</c>) leave bored with thought 12, else
	/// one world draw in ten turns the head by a second.
	/// </summary>
	// [BIN:TPI-EXE:0x617A44 needs window] turn − +0x230 above 30 (unsigned): the happiness byte above 80: umbrella state 0 or 2, 0x60CDD0 and 0x60FB30; below 10: thought 11 and the exit; below 20: umbrella
	// state 0 or 2, 0x60FB30(4); else the toilet byte +0x1D0 above 80: thought 4, then [ride+0x42] bit 1 (relief) stays, else the exit. Within 30: turn above +0x224 + 100: "bored and leaving the queue",
	// thought 12, the exit; else R1 % 10 nonzero returns, R2: +0x2C = +0x2C + R2 % 800 − 400, wrapped into 0..0x7FF. tp.exe's 0x00500311..0x0050051A with 0x004FC800(5) and (4) for the animations
	private void UpdateTpiQueueNeeds( Guest guest, ThemeParkIncGuestRecord tpi, RideVisitorBridge ride )
	{
		var turn = TpiTurn;
		var umbrellaDown = guest.UmbrellaState is UmbrellaRules.StateNone or UmbrellaRules.StateClosed;
		if ( unchecked( turn - (uint)tpi.SpotTurn ) > 30 )
		{
			var happiness = MeterMath.Byte( guest.Happiness );
			if ( happiness > 80 )
			{
				if ( umbrellaDown )
					StartThemeParkIncSpotAnimation( guest, tpi, ThemeParkIncMoodAnimation( tpi.Generation, ThemeParkIncSpriteIsFemale( guest ) ) );
				return;
			}
			if ( happiness < 10 )
			{
				Think( guest, GuestThought.Unhappy );
				TpiLeaveQueue( guest, tpi, ride );
				return;
			}
			if ( happiness < 20 )
			{
				if ( umbrellaDown )
					StartThemeParkIncSpotAnimation( guest, tpi, ThemeParkIncCantDecideAnimation );
				return;
			}
			if ( MeterMath.Byte( guest.Toilet ) <= 80 )
				return;
			Think( guest, GuestThought.NeedToilet );
			if ( !ride.ProvidesRelief )
				TpiLeaveQueue( guest, tpi, ride );
			return;
		}
		if ( turn > unchecked( (uint)tpi.Word224 + 100 ) )
		{
			Think( guest, GuestThought.Bored );
			TpiLeaveQueue( guest, tpi, ride );
			return;
		}
		if ( random.NextWord() % 10 != 0 )
			return;
		var heading = unchecked( tpi.Heading + (int)(random.NextWord() % 800) - 400 );
		tpi.Heading = heading < 0 ? heading + 0x800 : heading >= 0x800 ? heading - 0x800 : heading;
	}

	// ---- State 15 ----------------------------------------------------------------------------------------------------------------

	/// <summary>State 15 (<c>0x61A1DB</c>), the queue step: the walk; arrived or stuck, the guest stands again (state 14).</summary>
	// [BIN:TPI-EXE:0x61A1DB state 15] 0x60B440: 0 (0x61A1E6) and 2 (0x61A1F6) set 14; any other result returns (0x61A3A0)
	internal void UpdateTpiTakingQueueStep( Guest guest, ThemeParkIncGuestRecord tpi )
	{
		if ( TpiWalk( guest ) != WalkResult.Walking )
			SetThemeParkIncState( guest, tpi, ThemeParkIncGuestStates.StandingInQueue );
	}

	// ---- The ride's side ---------------------------------------------------------------------------------------------------------

	/// <summary>The ride calls a native guest at the head of its queue (<c>0x619670</c>, OnAdmittance): the call word <c>+0x220</c>, which state 14 answers.</summary>
	// [BIN:TPI-EXE:0x619670 on admittance] "Person %d: OnAdmittance": +0x220 = 1 and +0x1B4 = the ride's price word +0x94 (0x5E6200), the fee the exit charges (0x6126D0)
	// [EXT:tpi-capability-gates] the fee word +0x1B4 is not kept: the bridged boarding of phase P3a books the fee by Theme Park World's boarding instead (IGuestPayments.TryPayVisit, ECON-062)
	private static void OfferThemeParkIncGuest( Guest guest, ThemeParkIncGuestRecord tpi )
	{
		tpi.Word220 = 1;
		guest.QueueCalled = true;
	}

	/// <summary>The head of a queue as the ride's admission sees it (<c>0x619640</c>): state 14 at place 0.</summary>
	// [BIN:TPI-EXE:0x619640 standing at the front] +0x2C0 == 0xE and +0x21B == 0
	private static bool ThemeParkIncStandsAtFront( ThemeParkIncGuestRecord tpi ) => tpi.State == ThemeParkIncGuestStates.StandingInQueue && tpi.Byte21B == 0;

	/// <summary>
	/// The ride's head step sends a native guest away (a closed or broken ride, <c>0x5E3CC0</c>; or OpenTPW's teardown of a ride whose script stopped): the bridge has unlinked it, and the dismissal
	/// <c>0x619700(0)</c> makes it idle. A guest of the bridged boarding leaves Theme Park World's boarding the same way.
	/// </summary>
	// [BIN:TPI-EXE:0x5E3CC0 head step] VAR 0 not the head and the head in state 17: "script admitted person %d but he doesn't know yet, forcing him onto ride" (0x6182A0); else a head: 0x5E0100 and 0x619700(0)
	private void DismissThemeParkIncGuest( Guest guest, ThemeParkIncGuestRecord tpi )
	{
		NoteQueueEnd( guest, guest.AttractionId, QueueEndReason.RideBroken );
		TpiDismissFromQueue( guest, tpi, quietly: false );
	}

	/// <summary>
	/// The queue was edited under a native guest (<c>0x6197F0</c>, from the ride's re-evaluation): a place beyond the room the edit left, outside state 17, sends it away (thought 13 for one thing id in three).
	/// </summary>
	// [BIN:TPI-EXE:0x6197F0 queue edited] "Queue edited underneath me": 0x5E0280 at least 4 × [+0x50] (unsigned) and the state not 0x11: "The queue was shortened", the id % 3 == 0 thinks 13, 0x5E0100,
	// 0x619700(0), +0x204 = 0 and setter(6). tp.exe's 0x00501390
	private void ThemeParkIncQueueEdited( Guest guest, ThemeParkIncGuestRecord tpi, RideVisitorBridge ride, int room )
	{
		var position = TpiQueuePosition( ride, guest.Id );
		if ( unchecked( (uint)position ) < (uint)room || tpi.State == ThemeParkIncGuestStates.WaitingToBeAdmitted )
			return;
		NoteQueueEnd( guest, ride.AttractionId, QueueEndReason.QueueShortened );
		if ( (ushort)guest.Id % 3 == 0 )
			Think( guest, GuestThought.Angry );
		TpiRemoveFromQueue( ride, guest );
		TpiDismissFromQueue( guest, tpi, quietly: false );
	}

	// ---- The bridged boarding ------------------------------------------------------------------------------------------------------

	/// <summary>
	/// State 14 sets 16 ("being loaded", the walk to the stand point already requested): phase P3b ports 16, 17, 19 and 20. Until then the guest boards, rides and leaves by Theme Park World's
	/// boarding, as the default path's Theme Park Inc guests do (<c>[EXT:tpi-twp-guest-rules]</c>): the state word is 16 throughout, the shared record carries the walk, and the guest is idle again when it is
	/// done. Every draw up to the setter of 16 is the original's and state 16 itself draws nothing; the differences after that (17's missing entry roll <c>0x5E7A00</c>, Theme Park World's balloon
	/// reseeds, sideshow tail and small-portion draws) are listed in TPI-GUESTS.md, "The bridged boarding".
	/// </summary>
	// [BIN:TPI-EXE:0x6177AB being loaded] "Person %d being loaded to ride %d" and setter(0x10): the entry's mode 1 only (0x61A9E2)
	// [EXT:tpi-twp-guest-rules] the boarding from state 16 on (0x617F80, 0x61A21D, 0x61A77E, 0x618380, 0x619980: the price test, the admission, 17's roll 0x5E7A00, the ride's control and the unloading) runs
	// Theme Park World's walk to the stand point, AdmitPerson, boarding, ride and exit (UpdateQueueWalk, RideVisitorBridge) until phase P3b ports them
	private void BeginTpiBridgedBoarding( Guest guest, ThemeParkIncGuestRecord tpi, RideVisitorBridge ride )
	{
		SetThemeParkIncState( guest, tpi, ThemeParkIncGuestStates.BeingLoaded );
		guest.AttractionId = ride.AttractionId;
		guest.QueuePosition = 0;
		guest.QueueCalled = false;
		guest.QueueTargetIndex = 0;
		guest.InQueueInterlude = false;
		guest.HasWaypoint = true;
	}

	/// <summary>The Theme Park World states of the bridged boarding (<see cref="BeginTpiBridgedBoarding"/>).</summary>
	private static bool IsBridgedBoardingState( GuestState state ) =>
		state is GuestState.Boarding or GuestState.WaitingToBoard or GuestState.Using or GuestState.ExitingRide;

	/// <summary>
	/// One update of a bridged guest (state 16): Theme Park World's boarding state handler; when it ends (the guest idle, refused or sent back to its place), the guest returns to Theme Park Inc's states: back
	/// to its queue place (state 15) when the ride refused it at the stand point, else idle with no target.
	/// </summary>
	private void UpdateTpiBridgedBoarding( Guest guest, ThemeParkIncGuestRecord tpi, float dt )
	{
		if ( IsBridgedBoardingState( guest.State ) )
			UpdateState( guest, dt );
		if ( tpi.State != ThemeParkIncGuestStates.BeingLoaded || IsBridgedBoardingState( guest.State ) || guest.State == GuestState.Gone )
			return;
		EndTpiBridgedBoarding( guest, tpi );
	}

	/// <summary>The bridged boarding hands the guest back to Theme Park Inc's states: still in the list (refused at the stand point, walking back to its place) state 15, else idle with no target.</summary>
	private void EndTpiBridgedBoarding( Guest guest, ThemeParkIncGuestRecord tpi )
	{
		if ( guest.State is GuestState.MovingUpQueue or GuestState.Queueing && FindAttraction( guest.AttractionId ) is RideVisitorBridge ride && TpiQueuePosition( ride, guest.Id ) >= 0 )
		{
			tpi.Byte21B = guest.QueuePosition & 0xFF;
			SetThemeParkIncState( guest, tpi, ThemeParkIncGuestStates.TakingQueueStep );
			return;
		}
		(tpi.QueueSlot, tpi.QueueLink, tpi.Byte28C, tpi.Word290, tpi.Word220, tpi.Target, tpi.Byte21B) = (0, 0, 0, 0, 0, 0, 0);
		guest.SavedMajorAttractionId = 0;
		ClearTpiQueueMirror( guest );
		SetThemeParkIncState( guest, tpi, ThemeParkIncGuestStates.Idle );
	}
}

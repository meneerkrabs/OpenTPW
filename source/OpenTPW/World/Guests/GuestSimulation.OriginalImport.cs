namespace OpenTPW;

/// <summary>
/// The guests of an original save (thing model 1; tp.exe's guest serializer <c>0x004FB530</c> after the person block <c>0x004F8B10</c>). Each restored
/// guest keeps its thing id as its guest id, its type, cash, meters, exit counter, prankery flag, balloon, costume, histories, walk speed and the turn
/// stamps of its decisions, and walks on to its accurate destination as the original's loader routes it again (<c>0x004FA5F0</c> at <c>0x004F9407</c>).
/// </summary>
public sealed partial class GuestSimulation
{
	/// <summary>
	/// Attaches a saved TPW scheduler and the vehicle at work. The world's cached handles (world <c>+0x1DA72C/E/30</c>: the bus, the seaplane, the Ferry) must name the park's own vehicle
	/// objects by thing id (<see cref="IArrivalVehicleHost.ThingId"/>), and the handle at work (<c>+0x1DA72A</c>) one of them; a vehicle at work is then at its saved script state (the
	/// scripts are imported before this runs). Validation precedes every scheduler write; importing does not dispatch, draw randomness or emit a group-history event.
	/// </summary>
	// [BIN:TPW-EXE:0x005179C0 world loader] the words +0x1DA72C, +0x1DA72E, +0x1DA730 (the cached bus, seaplane and Ferry handles), +0x1DA72A (the vehicle at work) are read raw as 16-bit values (0x00517A56..0x00517B8A); nothing checks them afterwards
	// [BIN:TPW-EXE:0x0051A2F0 vehicle dispatch] a handle is the vehicle thing's id: the kind created at 0x0051A59B stores its handle at +0x1DA72C + 2 × kind and at +0x1DA72A, and the status read 0x0051A690 goes through the thing; the kind of the vehicle at work is the cached handle it equals (0x0051AAD0 compares it with the bus's)
	internal string ImportOriginalArrivals( SaveWorld world )
	{
		ArgumentNullException.ThrowIfNull( world );
		var variables = world.Variables;
		var saved = world.ArrivalState;
		string? reason = null;
		var cached = new[] { (int)variables.ArrivalVehicleSize1, variables.ArrivalVehicleSize2, variables.ArrivalVehicleSize3 };
		var current = -1;
		if ( OnlineVisit || ArrivalClock == null || ArrivalClock() != variables.GameTick )
			reason = "the attached arrival clock is not the saved park turn";
		else if ( saved.TimerStart > variables.GameTick )
			reason = "the saved timer is ahead of the park turn";
		else if ( ArrivalVehicles == null )
			reason = "no vehicle host is attached";
		else
		{
			for ( var kind = 0; kind < cached.Length && reason == null; kind++ )
			{
				var thing = ArrivalVehicles.Exists( kind ) ? ArrivalVehicles.ThingId( kind ) : 0;
				if ( cached[kind] != thing )
					reason = cached[kind] == 0 ? $"the park holds a {ArrivalVehicleKind.Names[kind]} the saved handle does not name"
						: $"the saved {ArrivalVehicleKind.Names[kind]} handle {cached[kind]} is not the park's {ArrivalVehicleKind.Names[kind]} (thing {thing})";
			}
			if ( reason == null && variables.CurrentArrivalVehicle != 0 )
			{
				current = Array.IndexOf( cached, (int)variables.CurrentArrivalVehicle );
				if ( current < 0 )
					reason = $"the vehicle at work, handle {variables.CurrentArrivalVehicle}, is none of the cached vehicles";
			}
		}
		if ( reason != null )
			return $"Saved arrival scheduler unchanged: {reason}.";
		arrivalTimerStart = saved.TimerStart;
		pendingArrivals = saved.PeopleOnBus;
		deliveringArrivals = saved.Offloading;
		arrivalVehicle = current;
		var atWork = current < 0 ? "no vehicle at work" : $"the {ArrivalVehicleKind.Names[current]} (thing {variables.CurrentArrivalVehicle}) at work";
		return $"Saved arrival scheduler imported with {atWork}: timer {saved.TimerStart}, signed group {saved.PeopleOnBus}, active {saved.Offloading}; the cached vehicle handles {string.Join( "/", cached )} name the park's vehicles. Words not applied: rate {saved.ArrivalRate}, target capacity {saved.TargetVehicleCapacity} and gates-open {saved.GatesOpen} (no reader in the update 0x004CF3E0, the group size 0x004CF5B0 or the guest maker 0x004CF720; the object holds the timer at +4, mTargetVehicleCapacity +8, mPeopleOnBus +0xC, mOffloading +0x10, mGatesOpen +0x11).";
	}

	/// <summary>
	/// Whether the import restores a guest in this saved state (guest <c>+0x220</c>, the <c>KIDSTATES</c> index): the arrival and departure states
	/// 0–5, 18, 19 and 21, the park states 6–10 (on a path or off the paths), a guest leaving a ride (15) and one held by a guard (17, with its escorting guard's state 18 restored by the
	/// staff import). The queue states 11–14 are restored through their attraction's saved queue (<see cref="IsOriginalQueueMember"/>); a guest using an attraction (16) is a rider, restored
	/// through the ride's saved script state when the attraction's script run state was imported (<see cref="RideVisitorBridge.SavedRunStateImported"/>).
	/// </summary>
	public static bool ImportsOriginalState( uint state ) => state is <= 10 or 15 or 17 or 18 or 19 or 21;

	/// <summary>
	/// Adds the saved guests to an empty simulation without drawing from its random stream. <paramref name="parkTurn"/> is the park turn of the save,
	/// <paramref name="attraction"/> maps an attraction thing id to the attraction id of its bridge and <paramref name="bin"/> one to the economy id of
	/// its litter bin (0: none). Returns the evidence lines of the import.
	/// </summary>
	// [BIN:TPW-EXE:0x004FB530 guest serializer] the person, then mArrivalDate, mArrivalIndex, mBalloonScript (+0x210), mBeenAdmitted, mCash (+0x1A0), mExitLevel (+0x1BC), happiness (+0x19C) and hunger (+0x1A8) bytes,
	// mLastPosX/Y, the litter byte (+0x1B4), mMajorDest (+0x1DC), the ride, shop, sideshow and win counts, mPaidAdmission, mParkOpeningWaitingTime (+0x200), mPersonType (+0x1F0), mPrankeryIndex (+0x1C0),
	// mPreviousRides[i] / mPreviousTemporaryRides[i] interleaved (+0x1E0 / +0x1E8), mQNext, mQPrev, mQueueMoveDelay (+0x1F4), mQueuePos (+0x1F1), mRemainingBalloonLife (+0x214), mSavedMajorDest,
	// mSavedState (+0x224), mState (+0x220), the thirst byte (+0x1A4), mTimeOfLastSpotAnim (+0x208), mTimeStartedIdling (+0x1FC), and the bytes of +0x1B8, toilet (+0x1AC) and illness (+0x1B0)
	internal IReadOnlyList<string> ImportOriginal( IReadOnlyList<SaveGuest> saved, long parkTurn, Func<int, int> attraction, Func<int, int> bin, uint visitorsToDate = 0, IReadOnlyList<SaveAttraction>? attractions = null, SavedSpriteScripts? sprites = null )
	{
		ArgumentNullException.ThrowIfNull( saved );
		if ( guests.Count != 0 )
			throw new InvalidOperationException( "Original guests are imported into an empty guest simulation only." );
		var evidence = new List<string>();
		BodySpriteClockMilliseconds = BodySpriteSampleAtTick( ParkCalendar.TickOfTurn( parkTurn ) );
		displayTurnPhaseTicks = (int)((ParkCalendar.TickOfTurn( parkTurn ) % DisplayTurnPhasePeriodTicks - TickCount % DisplayTurnPhasePeriodTicks + DisplayTurnPhasePeriodTicks) % DisplayTurnPhasePeriodTicks);
		// [BIN:TPW-EXE:0x00516C80 world variables] mNumberOfVisitorsToDate (+0x1DA714) is the park's visitor count, which 0x0051AAF0 counts up for each guest that walks into the park
		VisitorCount = (int)Math.Min( visitorsToDate, int.MaxValue );
		var spriteNotes = new List<string>();
		var adjusted = new List<string>();
		var dropped = new List<string>();
		var states = new List<string>();
		var recovery = new List<string>();
		var stranded = new List<string>();
		var keptTargets = 0;
		var savedMajors = 0;
		long Relative( uint stamp ) => Math.Max( 0, stamp - parkTurn );
		var (queuePlans, queueNotes) = PlanOriginalQueues( saved, attractions, attraction );
		var riders = new Dictionary<int, List<int>>();
		foreach ( var save in saved.OrderBy( item => item.Id ) )
		{
			var person = save.Person;
			var what = $"guest {save.Id}";
			var queued = IsOriginalQueueMember( save );
			// [BIN:TPW-EXE:0x00501A08 state 16] the loader reads mState straight in and a guest in state 16 names its ride in mMajorDest (+0x1DC, the object table lookup 0x004CD300): the ride's script holds the rest
			var riding = save.State == 16 && attraction( save.MajorDest ) is var rideOf and not 0 && FindAttraction( rideOf ) is RideVisitorBridge { SavedRunStateImported: true };
			var rideId = riding ? attraction( save.MajorDest ) : 0;
			// a guest of a refused or absent queue is dismissed to DoingNothing at its cell, as the original's state-11 update ends a queue its ride does not hold (0x00500136, "Problem with a
			// queue"; the interlude returns to 11 at 0x004FC890 and is dismissed the same way): that is the state an import without a queue gave a state-8 guest, and the one every queue guest gets now
			var orphan = queued && !queuePlans.ContainsKey( save.Id ) && (attractions != null || save.State == 8);
			if ( queued ? !queuePlans.ContainsKey( save.Id ) && !orphan : !ImportsOriginalState( save.State ) && !riding )
			{
				dropped.Add( $"{what} (state {save.State})" );
				ReleaseDroppedThing( save.Id );
				continue;
			}
			if ( save.PersonType >= Settings.Types.Count || byId.ContainsKey( save.Id ) )
			{
				dropped.Add( $"{what} (type {save.PersonType} of {Settings.Types.Count})" );
				ReleaseDroppedThing( save.Id );
				continue;
			}
			var position = (X: (int)person.Base.X, Y: (int)person.Base.Y);
			var cell = (X: position.X >> 8, Y: position.Y >> 8);
			if ( !Grid.InBounds( cell.X, cell.Y ) )
			{
				dropped.Add( $"{what} (state {save.State} outside the map at ({cell.X}, {cell.Y}))" );
				ReleaseDroppedThing( save.Id );
				continue;
			}
			// [BIN:TPW-EXE:0x004FB530 off-path guest] the load reads the position and mState straight in and nothing moves a park guest standing off the paths (a path deleted
			// under it, an object built over it, a wander across grass): it stays where it stands and its own state handlers recover it, the wander's off-path search 0x004F9490 included
			// [BIN:TPW-EXE:0x004FEB10 held guest] a guest held by a guard (state 17) is off the map and stands nowhere: its saved cell only has to be on the map
			var inPark = save.State is >= 6 and <= 17 || save.State == 18 && Grid.IsWalkable( cell.X, cell.Y );
			if ( inPark && save.State is not (15 or 16 or 17 or 18) && !Grid.IsWalkable( cell.X, cell.Y ) )
				recovery.Add( $"guest {save.Id} state {save.State} imported off the paths at ({cell.X}, {cell.Y}) (cell type {Grid.Cells.RawTypeAt( cell.X, cell.Y )}); it recovers through its state handler (0x004F9490 off-path search)" );
			if ( !inPark && !HasArrivalLane )
			{
				dropped.Add( $"{what} (state {save.State}, no arrival lane)" );
				ReleaseDroppedThing( save.Id );
				continue;
			}
			// the guest keeps its thing id: the saved thing's slot in the pool (the park's pool holds it already when the level restored the saved chain)
			if ( save.Id <= ThingPool.Capacity && !(things.IsUsed( save.Id ) && things.ModelOf( save.Id ) == SaveThingModel.Guest) && !things.Occupy( save.Id, SaveThingModel.Guest ) )
			{
				dropped.Add( $"{what} (its thing id is taken by a {things.ModelOf( save.Id )})" );
				continue;
			}
			var guest = new Guest( save.Id, save.PersonType );
			// [BIN:TPW-EXE:0x004FB530 saved major destination] mSavedMajorDest (+0x1DE) is saved with the person and nothing in the loader touches it: it names the attraction a minor decision set aside, whatever the state; one that names no attraction OpenTPW holds is 0
			guest.SavedMajorAttractionId = attraction( save.SavedMajorDest );
			if ( guest.SavedMajorAttractionId != 0 )
				savedMajors++;
			float Meter( float value, string name )
			{
				// the meters are singles the guest code clamps to 0..100 (MeterMath); a save of the original holds no other value
				var clamped = Math.Clamp( value, 0f, 100f );
				if ( clamped != value )
					adjusted.Add( $"{what} {name} {value} imported as {clamped}" );
				return clamped;
			}
			guest.Happiness = Meter( save.Happiness, "happiness" );
			guest.Hunger = Meter( save.Hunger, "hunger" );
			guest.Thirst = Meter( save.Thirst, "thirst" );
			guest.Toilet = Meter( save.Toilet, "toilet" );
			guest.Nausea = Meter( save.Illness, "illness" );
			guest.Litter = Meter( save.Litter, "litter" );
			guest.Energy = Meter( save.Meter1B8, "meter +0x1B8" );
			// [BIN:TPW-EXE:0x0051861C queue-join happiness] the thing-list loader stores 0 in +0x20C right after the constructor and the guest serializer does not save it, so the happiness at joining a queue (read by the use satisfaction 0x004FDA1B) is 0 until the guest joins again
			guest.QueueJoinHappiness = 0;
			guest.Money = Math.Max( 0, save.Cash );
			if ( guest.Money != save.Cash )
				adjusted.Add( $"{what} cash {save.Cash} imported as 0" );
			guest.ExitLevel = save.ExitLevel;
			guest.PrankFlag = save.PrankeryIndex is 0 or 100 or 101 or 102 ? save.PrankeryIndex : 0;
			if ( guest.PrankFlag != save.PrankeryIndex )
				adjusted.Add( $"{what} prankery index {save.PrankeryIndex} imported as 0" );
			guest.BalloonLife = Math.Clamp( save.RemainingBalloonLife, 0, GuestAppearance.MaximumBalloonLife );
			// [BIN:TPW-EXE:0x004FE1E0 balloon] the guest holds its balloon while mBalloonScript (+0x210) names the balloon model; a costume is sprite category 2 (person +0x24, mESPSprite)
			guest.HasBalloon = save.BalloonScript != 0 && guest.BalloonLife > 0;
			guest.InCostume = person.EspSprite == 2;
			// [BIN:TPW-EXE:0x004F93AA saved sprite bank] the person serializer saves the sprite bank (+0x20) and reduces it by the loaded count of the person's own category (+0x24, 0x00541F60); a guest in costume holds the costume bank there, and the kid set it returns to is the draw its thing id gives again (0x004FE6A1)
			if ( !guest.InCostume )
				guest.KidSetDraw = ReduceKidDraw( person.SpriteId );
			var baseSpeed = GuestSpeed.BaseSpeeds.Contains( person.BaseSpeed ) ? person.BaseSpeed : GuestSpeed.BaseSpeeds[0];
			if ( baseSpeed != person.BaseSpeed )
				adjusted.Add( $"{what} base speed {person.BaseSpeed} imported as {baseSpeed}" );
			// [BIN:TPW-EXE:0x004F8B10 person serializer] mBaseSpeed (+0xC0), mPurposeSpeed (+0xC2, the toilet term), mAdjustorSpeed (+0xC4, the boost) and mPreviousSpeed (+0xC8, the smoothed single)
			guest.BaseSpeed = baseSpeed;
			guest.ToiletBonus = person.PurposeSpeed;
			guest.Boost = person.AdjustorSpeed;
			var smoothed = BitConverter.Int32BitsToSingle( unchecked( (int)person.PreviousSpeed ) );
			guest.SmoothedSpeed = float.IsFinite( smoothed ) && smoothed >= 0 ? smoothed : 0;
			// [BIN:TPW-EXE:0x004FB530 visitor number] mArrivalIndex (+0x1D8) is the number 0x0051AAF0 gave the guest at the entrance (0 for one not yet numbered)
			guest.VisitorNumber = (int)Math.Min( save.ArrivalIndex, int.MaxValue );
			guest.QueueMoveDelay = (int)Math.Min( save.QueueMoveDelay, int.MaxValue );
			guest.RidesTaken = (int)Math.Min( save.NumRides, int.MaxValue );
			// [BIN:TPW-EXE:0x4FB530 guest serializer] mNumShops (+0x1C8), mNumSideshows (+0x1CC) and mNumSideshowsWon (+0x1D0) follow mNumRides as saved
			guest.ShopsVisited = (int)Math.Min( save.NumShops, int.MaxValue );
			guest.SideshowsVisited = (int)Math.Min( save.NumSideshows, int.MaxValue );
			guest.SideshowsWon = (int)Math.Min( save.NumSideshowsWon, int.MaxValue );
			for ( var slot = 0; slot < Guest.HistorySlots; slot++ )
			{
				guest.UsedHistory[slot] = attraction( save.PreviousRides[slot] );
				guest.TemporaryHistory[slot] = attraction( save.PreviousTemporaryRides[slot] );
			}
			// the guest clock starts with the simulation: a stamp from before the save's turn counts from the import
			guest.QueueStandingSinceTurn = Relative( save.TimeStartedIdling );
			guest.InterludeTurn = Relative( save.TimeOfLastSpotAnim );
			// [BIN:TPW-EXE:0x0050BE80 thought] a thought is stored at the thought object's +0 (mLastThought) and its bubble script at +0x84 (mThoughtScript), stamped with mTimeBubbleShown
			if ( person.LastThought < 256 && Enum.IsDefined( (GuestThought)person.LastThought ) )
			{
				guest.Thought = (GuestThought)person.LastThought;
				if ( person.ThoughtScript != 0 )
					(guest.BubbleThought, guest.BubbleTurn) = (guest.Thought, Math.Min( Relative( person.TimeBubbleShown ), ParkTurn ));
			}
			var destination = (X: (int)person.AccurateDestX, Y: (int)person.AccurateDestY);
			// the walk's sub-cell point is the saved destination's, so the first waypoint is the one the original routes to
			guest.OffsetX = (destination.X & 0xFF) / 256f - 0.5f;
			guest.OffsetY = (destination.Y & 0xFF) / 256f - 0.5f;
			var navigation = guest.Navigation;
			(navigation.X, navigation.Y) = (position.X << 8, position.Y << 8);
			navigation.SetDestination( navigation.X, navigation.Y, [] );
			// [BIN:TPW-EXE:0x0050D150 nav_mode] the walk mask +0xB4 (person +0x188) is saved in the navigation state: the constructor 0x0050FFE0 zeroes it and the reader, called at 0x004F925E with ECX = person + 0xD4, loads it as saved
			var navMode = person.NavigationState.NavMode;
			navigation.Mask = navMode is >= 0 and <= 2 ? navMode : 0;
			// [BIN:TPW-EXE:0x004FEC90 walk mask writers] tp.exe writes only 0 and 1 to person +0x188: the set-down 0x004FEBAB, the leave search 0x004FEF04/0x004FEF13 (EAX is the failed route's 0), the
			// state-3 and booth sends home 0x004FF970/0x004FFA04, 0x004D6977/0x004D6ACB, 0x004F7C0F, the recall 0x00500AB9 and the staff 0x0050571B/0x00506A89; the walk's +0xB4 is written only by the
			// constructor 0x0051009F (0) and the reader 0x0050D150, so another saved value comes from a corrupt save and is reported like an out-of-range meter
			if ( navigation.Mask != navMode )
				adjusted.Add( $"{what} walk mask {navMode} imported as 0" );
			// [BIN:TPW-EXE:0x004FA5F0 stranded mark on load] mStrandedTime (+0x198, read at 0x004F92F0) is compared with the route counter 0x007CDB98, which is not saved and restarts with the process (0x004D8C50 is its only reader and writer): the load's re-route clears a mark above the counter, so a fresh load imports it as 0; a mark at or below the loading process's count of route requests (one set in the saving session's first requests) is kept only while the stranded test 0x004FA770 holds, and any walk step clears +0x198 anyway (0x004FA30B in 0x004FA2A0), so a walking guest loses it at its first step
			guest.StrandedMark = 0;
			if ( person.StrandedTime != 0 )
				stranded.Add( $"guest {save.Id} saved stranded mark {person.StrandedTime} cleared as a fresh load clears it" );
			// [EXT:smarter-guests] an imported guest of a running extension gets its id at once; before the first tick the extension's first tick gives it
			if ( SmarterGuests is { Bootstrapped: true } smarter )
				guest.BirthId = smarter.AllocateBirthId();
			guests.Add( guest );
			byId.Add( guest.Id, guest );
			if ( inPark )
			{
				ImportInPark( guest, save, cell, destination, attraction, bin, adjusted, recovery, queuePlans, parkTurn, rideId );
				if ( rideId != 0 )
				{
					if ( !riders.TryGetValue( rideId, out var aboard ) )
						riders.Add( rideId, aboard = new List<int>() );
					aboard.Add( guest.Id );
				}
				if ( (guest.AttractionId != 0 || guest.BinId != 0) && guest.State is not (GuestState.GoingToRide or GuestState.ThrowingAwayLitter) && !guest.IsInQueue )
					keptTargets++;
				// [BIN:TPW-EXE:0x4FD950 time in park] mArrivalDate (+0x1D4) is the world's game tick at the stamp and the hours the visitor lists show are (tick − stamp) >> 2 turns (the roster's cell); the saved stamp gives the elapsed park turns, a stamp after the save's own turn is not a date
				if ( save.ArrivalDate <= parkTurn )
				{
					guest.TimeInParkSeconds = ((parkTurn - save.ArrivalDate) * ParkCalendar.TurnMilliseconds + 0.5) / 1000.0; // retained unchanged for statistics, weather gates and legacy duration consumers
					guest.DisplayTimeInParkTurns = unchecked((uint)(parkTurn - save.ArrivalDate));
				}
			}
			else
				ImportOnMap( guest, save, cell, destination, adjusted );
			// [EXT:rain-mood-imported-age] a guest the save gives no time in the park counts its age for the rain mood from this turn (user-approved 2026-10-18, "Tellen vanaf het laden"); recorded only while the extension is on, so other saves keep their bytes
			if ( (RainMood != null || WinterCold != null) && guest.TimeInParkSeconds == null )
				guest.RainMoodAgeStartTurn = ParkTurn;
			(guest.StepFromX, guest.StepFromY, guest.StepFromDistance, guest.StepTick) = (guest.X, guest.Y, guest.DistanceWalked, TickCount);
			UpdatePresentation( guest );
			Occupy( guest );
			ImportBodySprite( guest, save, sprites, spriteNotes );
			if ( guest.HasBalloon ) ImportBalloon( guest, save, sprites, spriteNotes );
			balloonHistoryReconstructed = true;
			states.Add( $"{guest.Id}:{save.State}→{(int)guest.State}" );
		}
		if ( sprites != null )
			evidence.Add( $"Guest sprite scripts imported (SAVE-013): {spriteNotes.Count( note => note.StartsWith( "ok", StringComparison.Ordinal ) )} saved body and balloon sprites applied, {spriteNotes.Count( note => !note.StartsWith( "ok", StringComparison.Ordinal ) )} rebuilt: {string.Join( "; ", spriteNotes.Where( note => !note.StartsWith( "ok", StringComparison.Ordinal ) ).Take( 6 ) )}" );
		evidence.Add( $"Guest destinations imported: {keptTargets} kept targets (mMajorDest, +0x1DC: an attraction or a litter bin, of guests deciding or wandering) and {savedMajors} saved major destinations (mSavedMajorDest, +0x1DE)." );
		var queues = RestoreOriginalQueues( queuePlans );
		// the riders and the walk, bounce and head tables of every ride whose script state was imported (the tables were staged by the script import)
		var rideNotes = new List<string>();
		var aboardTotal = 0;
		foreach ( var bridge in this.attractions.OfType<RideVisitorBridge>().Where( bridge => bridge.SavedRunStateImported ).ToList() )
		{
			var aboard = riders.TryGetValue( bridge.AttractionId, out var list ) ? list : new List<int>();
			aboardTotal += aboard.Count;
			foreach ( var note in bridge.ImportSavedRiders( aboard ) )
				rideNotes.Add( $"[APPROX:SAVE-002] Saved {note}." );
		}
		if ( aboardTotal > 0 )
			evidence.Add( $"Riders imported: {aboardTotal} guests in state 16, aboard the attractions that name them (mMajorDest, +0x1DC), with the walk, bounce and head tables of their ride scripts." );
		evidence.AddRange( rideNotes );
		if ( queuePlans.Count > 0 )
			evidence.Add( $"Attraction queues imported: {queues} saved queues ({queuePlans.Count} guests in states 11–14) with their saved head, links and called guest (mFirstInQ, mQNext, mQPrev, mPersonBeingLoaded)." );
		foreach ( var note in queueNotes )
			evidence.Add( $"[APPROX:SAVE-002] Saved {note}." );
		evidence.Add( $"Guests imported: {guests.Count} of {saved.Count} ({(states.Count == 0 ? "none" : string.Join( ", ", states ))}, saved state → state)." );
		foreach ( var line in adjusted )
			evidence.Add( $"Guest value adjusted on import: {line}." );
		foreach ( var line in recovery.Concat( stranded ) )
			evidence.Add( $"[BIN:TPW-EXE:0x004F8B10] {line}." );
		// [APPROX:SAVE-002] a rider (16) is imported when its ride's script run state was imported (the heap, limbo, walk, bounce and head tables are the ride script's, RideVisitorBridge.ImportSavedRiders), and is dropped otherwise; a guest held by a guard (17) is imported held, and kept only while a guard in state 18 holds it as its perpetrator, as its first update (0x00501C94) decides, and a guest in a queue (states 11–14, or its idle interlude, state 8 with mSavedState 11) is imported only when its attraction's saved queue (mFirstInQ, mQNext, mQPrev, mPersonBeingLoaded) reads as the loader's chain of guests of that ride; an imported guest in an activity (state 8) decides again at once, one at the booth (4) that was not yet weighed waits OpenTPW's booth time, a turn stamp before the save's turn counts from the import, and the saved state (mSavedState) outside the queue interlude and the admission flag (mBeenAdmitted) of a guest that is not in state 11 or in its interlude are not imported (the event ring is [EXT:guest-event-ring]) — evidence needed: the resume of a state-8 activity outside a queue at its saved state (0x004FC890)
		// [EXT:guest-event-ring] the person's 32-slot event ring (AddEvent 0x50C100) is not imported: the ring is debug-only and OpenTPW keeps none (user-approved 2026-10-16)
		if ( dropped.Count > 0 )
			evidence.Add( $"[APPROX:SAVE-002] Guests not imported: {string.Join( ", ", dropped )}." );
		evidence.Add( $"[APPROX:SAVE-002] Guest values not imported: saved states {string.Join( "/", saved.Select( item => item.SavedState ) )}, queue links of guests outside an imported queue {string.Join( "/", saved.Where( item => !queuePlans.ContainsKey( item.Id ) ).Select( item => $"{item.QPrev}-{item.QNext}@{item.QueuePos}" ) )}." );
		return evidence;
	}

	/// <summary>
	/// A guest in the park: decide (6, 8), wander on (7), walk on to its bin (9) or its ride (10), or leave (18). It stands where it was saved, on a path or off the paths,
	/// and a walking state is routed again to its accurate destination with the original's walk set-up, which draws nothing.
	/// </summary>
	// [BIN:TPW-EXE:0x004F9407 load re-route] the person loader calls 0x004FA5F0 with the accurate destination (+0x18, +0x1A): SetDest 0x00510100 → the walk set-up 0x0050F8E0, whose search starts from any cell (the edge test 0x004D8750 opens every step out of a cell that is neither path nor queue); a failed search leaves the walk finished with mCantReachDest set and only logs "Couldn't renav to my accurate dest!!", so the first walk step answers 2 (0x004FA2A0) and the state handler recovers (7: a draw and the wander, 9: the litter drop, 10: "stuck on the way")
	/// <summary>The saved target +0x1DC of a guest the import keeps in state 6, 7 or 8: the attraction or the litter bin the thing id names (one id, so at most one), 0 when OpenTPW holds neither.</summary>
	private static void KeepTarget( Guest guest, int thingId, Func<int, int> attraction, Func<int, int> bin )
	{
		guest.AttractionId = attraction( thingId );
		if ( guest.AttractionId == 0 )
			guest.BinId = bin( thingId );
	}

	private void ImportInPark( Guest guest, SaveGuest save, (int X, int Y) cell, (int X, int Y) destination, Func<int, int> attraction, Func<int, int> bin, List<string> adjusted, List<string> recovery, Dictionary<int, OriginalQueuePlan> queuePlans, long parkTurn, int rideId = 0 )
	{
		guest.OnGrid = true;
		guest.Lane = null;
		guest.Navigation.OffGrid = false;
		(guest.CellX, guest.CellY) = cell;
		guest.TimeInParkSeconds = null;
		void WalkOn()
		{
			var target = (destination.X >> 8, destination.Y >> 8);
			if ( !Grid.InBounds( target.Item1, target.Item2 ) )
				return;
			// the route cells are the M3 gate's bookkeeping (empty from a start off the paths); the walk is the original set-up's search
			var route = Grid.FindPath( cell.X, cell.Y, target.Item1, target.Item2 );
			if ( !SetDestination( guest, GuestNavigation.BytePoint( target, destination.X & 0xFF, destination.Y & 0xFF ), route ) )
				recovery.Add( $"guest {guest.Id} could not renav to its accurate destination ({target.Item1}, {target.Item2}); it recovers on its first update (0x004F9407)" );
		}
		switch ( save.State )
		{
			case 7:
				// [BIN:TPW-EXE:0x004FB530 kept target] mMajorDest (+0x1DC) is the target the guest keeps until its next major choice (0x004FCB21) whatever the state (0x00500900, 0x004FFEDA leave it): a wanderer keeps the attraction it last chose or the bin it last used (0x004FFF20 writes no +0x1DC)
				guest.State = GuestState.WalkingAround;
				KeepTarget( guest, save.MajorDest, attraction, bin );
				WalkOn();
				break;
			case 9 when bin( save.MajorDest ) is var found and not 0:
				guest.State = GuestState.ThrowingAwayLitter;
				guest.BinId = found;
				WalkOn();
				break;
			case 10 when attraction( save.MajorDest ) is var ride and not 0:
				// the walk to the saved accurate destination, as the loader routes every person (0x004F9407); arrived on another cell than the queue's back cell, state 10 asks for the back cell again (0x004FFBC0)
				guest.State = GuestState.GoingToRide;
				guest.AttractionId = ride;
				WalkOn();
				break;
			case 15:
				// [BIN:TPW-EXE:0x004F9407 load re-route] a guest leaving a ride walks on to its saved accurate destination, the cell beyond the exit (ExitRide 0x005014E0 asked the route that set it), whatever it stands on; mMajorDest (+0x1DC) keeps the ride it left through the walk (0x005014E0 writes no +0x1DC), which the removal of the attraction still reaches
				guest.State = GuestState.ExitingRide;
				guest.AttractionId = attraction( save.MajorDest );
				guest.StateTimer = 0;
				WalkOn();
				break;
			case 18:
				guest.State = GuestState.LeavingPark;
				break;
			case 16 when rideId != 0:
				// [BIN:TPW-EXE:0x00502156 state 16 entry] the entry's effects are in the saved words: the balloon's model is gone and +0x210 clear (0x00475550), the thought bubble gone (0x0050C0D0) and the body kept or taken off the map by the ride's flag (0x004D4170, +0x28): the guest stands as the ride took it, and its update does nothing (0x00501A08 case 0x10)
				guest.State = GuestState.Using;
				guest.AttractionId = rideId;
				if ( guest.HasBalloon )
				{
					adjusted.Add( $"guest {guest.Id} on a ride saved with a balloon script, imported without the balloon the boarding takes away" );
					guest.HasBalloon = false;
				}
				guest.BubbleThought = GuestThought.None;
				guest.Activity = GuestActivity.None;
				// [BIN:TPW-EXE:0x004FDA0D sideshow win byte] a sideshow stores the outcome of its win draw in +0x1F1 at the state-14 entry (0x004E2670) and the end of the use reads it (a lost play, 0x004FDA0D): the saved byte
				if ( FindAttraction( rideId ) is { Kind: RideVisitorKind.Sideshow } )
					guest.SideshowWon = save.QueuePos != 0;
				break;
			case 17:
				// [BIN:TPW-EXE:0x004FEB10 held guest] the state setter's entry for 17 (0x005022DA) only runs on a change of state, and a load reads mState straight in: the guest stands as the hold left it, balloon gone (0x004FE950 clears
				// mBalloonScript, whose life stays), model off the map (0x004D4170), thought bubble gone (0x0050C0D0), no walk and no ride or bin; its first update (0x00501C94) keeps it only while a guard in state 18 holds it as its perpetrator (+0x212, 0x004D6350), else it is destroyed (0x0050B780)
				guest.State = GuestState.HeldByGuard;
				if ( guest.HasBalloon )
				{
					adjusted.Add( $"guest {guest.Id} held by a guard saved with a balloon script, imported without the balloon the hold takes away" );
					guest.HasBalloon = false;
				}
				guest.BubbleThought = GuestThought.None;
				guest.Activity = GuestActivity.None;
				break;
			case >= 11 and <= 14 or 8 when queuePlans.TryGetValue( save.Id, out var queue ):
				ImportQueued( guest, save, cell, destination, queue, recovery, parkTurn );
				break;
			default:
				if ( save.State is 9 or 10 )
					adjusted.Add( $"guest {guest.Id} state {save.State} target thing {save.MajorDest} is not imported, imported deciding (state 6)" );
				else if ( IsOriginalQueueMember( save ) )
					adjusted.Add( $"guest {guest.Id} (state {save.State}) is in no saved queue this import accepts, imported deciding (state 6)" );
				guest.State = GuestState.DoingNothing;
				// states 6 and 8 (an activity, decided again at once) keep the target too
				if ( save.State is 6 or 8 )
					KeepTarget( guest, save.MajorDest, attraction, bin );
				break;
		}
	}

	/// <summary>
	/// Puts off an attraction the riders an original save's import seated aboard it, when the ride has no carrier state they could ride on (<see cref="RideVisitorBridge.RidersRefusal"/>): the guests leave the
	/// simulation, their thing slots stay held as the dropped saved guests' do, and the ride's rider tables are cleared. Returns the guests put off.
	/// </summary>
	internal IReadOnlyList<int> DropImportedRiders( RideVisitorBridge bridge )
	{
		ArgumentNullException.ThrowIfNull( bridge );
		var ids = bridge.Riders.ToList();
		bridge.ClearImportedRiders();
		foreach ( var id in ids )
		{
			if ( !byId.TryGetValue( id, out var guest ) )
				continue;
			RemoveBalloonPlayer( guest );
			guest.BodySprite.ResetForNewPerson();
			LeaveCellLists( guest );
			byId.Remove( id );
			guests.Remove( guest );
			ReleaseDroppedThing( id );
		}
		return ids;
	}

	/// <summary>
	/// A saved guest the import does not restore is not in the park, but the original keeps it: its slot of the pool (held by the saved chain) stays used as a placeholder, so the
	/// next thing built gets the id the original hands out.
	/// </summary>
	// [BIN:TPW-EXE:0x00518D43 free list rebuild] a load keeps every saved thing in its slot, so the unused slots (and with them the next ids) are those the save leaves free
	private void ReleaseDroppedThing( int id )
	{
		if ( things.IsUsed( id ) && things.ModelOf( id ) == SaveThingModel.Guest && !byId.ContainsKey( id ) )
			things.Hold( id );
	}

	/// <summary>A guest on its way in or out of the park, in the state it was saved in: the arrival walk's states 0 to 5, state 18 off the paths and the gate's states 19 and 21 stand on the cell map as they did.</summary>
	// [BIN:TPW-EXE:0x004FB530 arrival states] mState (+0x220) is kept as it was saved, with the walk's accurate destination (+0x18, +0x1A, routed again as the loader does, 0x004FA5F0) in the walking states 0, 2, 5 and 18, mPaidAdmission (+0x204) and the wait mParkOpeningWaitingTime (+0x200) in states 3 and 4; the walk mask +0x188 is the saved nav_mode in every state, 19 and 21 included (1 for a guest sent home by 0x004FF9D0 or 0x004FF7F0, 0 for one whose first gate route in 0x004FEC90 succeeded with mask 0)
	private void ImportOnMap( Guest guest, SaveGuest save, (int X, int Y) cell, (int X, int Y) destination, List<string> adjusted )
	{
		guest.OnGrid = true;
		guest.Lane = null;
		guest.Navigation.OffGrid = false;
		(guest.CellX, guest.CellY) = cell;
		var target = (X: destination.X >> 8, Y: destination.Y >> 8);
		void WalkOn()
		{
			if ( Grid.InBounds( target.X, target.Y ) )
				RouteToPoint( guest, (target.X, target.Y), destination.X & 0xFF, destination.Y & 0xFF );
		}
		switch ( save.State )
		{
			case 0:
				guest.State = GuestState.CrossingRoad;
				WalkOn();
				break;
			case 1:
				guest.State = GuestState.CrossingRoadToPark;
				break;
			case 2:
				guest.State = GuestState.GoingToTicketBooth;
				WalkOn();
				break;
			case 3:
				guest.State = GuestState.WaitingAtBooth;
				guest.Paid = save.PaidAdmission != 0;
				guest.BoothWait = (int)Math.Min( save.ParkOpeningWaitingTime, GateWaitMaximum );
				break;
			case 4:
				guest.State = GuestState.AtTicketBooth;
				guest.Paid = save.PaidAdmission != 0;
				// The host counts down through the verdict turn; imported positive counters exclude that final turn.
				// Keep the existing input clamp, then translate once. Zero still means the next update.
				var boothWait = (int)Math.Min( save.ParkOpeningWaitingTime, 2 * BoothVerdict.ExpensiveWaitBase );
				guest.BoothWait = boothWait > 0 ? boothWait + 1 : 0;
				guest.StateTimer = 0;
				break;
			case 5:
				guest.State = GuestState.EnteringPark;
				WalkOn();
				break;
			case 18:
				guest.State = GuestState.LeavingPark;
				WalkOn();
				break;
			case 19:
				guest.State = GuestState.CrossingRoadHome;
				break;
			default:
				guest.State = GuestState.WaitingToGoHome;
				guest.StateTimer = WaitForBusSeconds;
				break;
		}
	}
}

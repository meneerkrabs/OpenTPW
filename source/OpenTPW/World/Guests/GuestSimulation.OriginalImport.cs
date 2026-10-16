namespace OpenTPW;

/// <summary>
/// The guests of an original save (thing model 1; tp.exe's guest serializer <c>0x004FB530</c> after the person block <c>0x004F8B10</c>). Each restored
/// guest keeps its thing id as its guest id, its type, cash, meters, exit counter, prankery flag, balloon, costume, histories, walk speed and the turn
/// stamps of its decisions, and walks on to its accurate destination as the original's loader routes it again (<c>0x004FA5F0</c> at <c>0x004F9407</c>).
/// </summary>
public sealed partial class GuestSimulation
{
	/// <summary>
	/// Whether the import restores a guest in this saved state (guest <c>+0x220</c>, the <c>KIDSTATES</c> index): the arrival and departure states
	/// 0–5, 18, 19 and 21, and the park states 6–10. The queue states 11–14, a guest leaving a ride (15), using an attraction (16) and one held by a
	/// guard (17) are not restored.
	/// </summary>
	public static bool ImportsOriginalState( uint state ) => state is <= 10 or 18 or 19 or 21;

	/// <summary>
	/// Adds the saved guests to an empty simulation without drawing from its random stream. <paramref name="parkTurn"/> is the park turn of the save,
	/// <paramref name="attraction"/> maps an attraction thing id to the attraction id of its bridge and <paramref name="bin"/> one to the economy id of
	/// its litter bin (0: none). Returns the evidence lines of the import.
	/// </summary>
	// [BIN:TPW-EXE:0x004FB530 guest serializer] the person, then mArrivalDate, mArrivalIndex, mBalloonScript (+0x210), mBeenAdmitted, mCash (+0x1A0), mExitLevel (+0x1BC), happiness (+0x19C) and hunger (+0x1A8) bytes,
	// mLastPosX/Y, the litter byte (+0x1B4), mMajorDest (+0x1DC), the ride, shop, sideshow and win counts, mPaidAdmission, mParkOpeningWaitingTime (+0x200), mPersonType (+0x1F0), mPrankeryIndex (+0x1C0),
	// mPreviousRides[i] / mPreviousTemporaryRides[i] interleaved (+0x1E0 / +0x1E8), mQNext, mQPrev, mQueueMoveDelay (+0x1F4), mQueuePos (+0x1F1), mRemainingBalloonLife (+0x214), mSavedMajorDest,
	// mSavedState (+0x224), mState (+0x220), the thirst byte (+0x1A4), mTimeOfLastSpotAnim (+0x208), mTimeStartedIdling (+0x1FC), and the bytes of +0x1B8, toilet (+0x1AC) and illness (+0x1B0)
	internal IReadOnlyList<string> ImportOriginal( IReadOnlyList<SaveGuest> saved, long parkTurn, Func<int, int> attraction, Func<int, int> bin )
	{
		ArgumentNullException.ThrowIfNull( saved );
		if ( guests.Count != 0 )
			throw new InvalidOperationException( "Original guests are imported into an empty guest simulation only." );
		var evidence = new List<string>();
		var adjusted = new List<string>();
		var dropped = new List<string>();
		var states = new List<string>();
		long Relative( uint stamp ) => Math.Max( 0, stamp - parkTurn );
		foreach ( var save in saved.OrderBy( item => item.Id ) )
		{
			var person = save.Person;
			var what = $"guest {save.Id}";
			if ( !ImportsOriginalState( save.State ) )
			{
				dropped.Add( $"{what} (state {save.State})" );
				continue;
			}
			if ( save.PersonType >= Settings.Types.Count || byId.ContainsKey( save.Id ) )
			{
				dropped.Add( $"{what} (type {save.PersonType} of {Settings.Types.Count})" );
				continue;
			}
			var position = (X: (int)person.Base.X, Y: (int)person.Base.Y);
			var cell = (X: position.X >> 8, Y: position.Y >> 8);
			if ( !Grid.InBounds( cell.X, cell.Y ) )
			{
				dropped.Add( $"{what} (state {save.State} outside the map at ({cell.X}, {cell.Y}))" );
				continue;
			}
			var inPark = save.State is >= 6 and <= 10 || save.State == 18 && Grid.IsWalkable( cell.X, cell.Y );
			if ( inPark && !Grid.IsWalkable( cell.X, cell.Y ) )
			{
				dropped.Add( $"{what} (state {save.State} off the paths at ({cell.X}, {cell.Y}))" );
				continue;
			}
			var lane = inPark ? null : NearestLane( cell );
			if ( !inPark && lane == null )
			{
				dropped.Add( $"{what} (state {save.State}, no arrival lane)" );
				continue;
			}
			var guest = new Guest( save.Id, save.PersonType );
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
			guest.QueueJoinHappiness = guest.Happiness;
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
			var baseSpeed = GuestSpeed.BaseSpeeds.Contains( person.BaseSpeed ) ? person.BaseSpeed : GuestSpeed.BaseSpeeds[0];
			if ( baseSpeed != person.BaseSpeed )
				adjusted.Add( $"{what} base speed {person.BaseSpeed} imported as {baseSpeed}" );
			// [BIN:TPW-EXE:0x004F8B10 person serializer] mBaseSpeed (+0xC0), mPurposeSpeed (+0xC2, the toilet term), mAdjustorSpeed (+0xC4, the boost) and mPreviousSpeed (+0xC8, the smoothed single)
			guest.BaseSpeed = baseSpeed;
			guest.ToiletBonus = person.PurposeSpeed;
			guest.Boost = person.AdjustorSpeed;
			var smoothed = BitConverter.Int32BitsToSingle( unchecked( (int)person.PreviousSpeed ) );
			guest.SmoothedSpeed = float.IsFinite( smoothed ) && smoothed >= 0 ? smoothed : 0;
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
			guests.Add( guest );
			byId.Add( guest.Id, guest );
			if ( inPark )
				ImportInPark( guest, save, cell, destination, attraction, bin, adjusted );
			else
				ImportOnLane( guest, save.State, save.PaidAdmission != 0, lane!, cell, adjusted );
			(guest.StepFromX, guest.StepFromY, guest.StepFromDistance, guest.StepTick) = (guest.X, guest.Y, guest.DistanceWalked, TickCount);
			UpdatePresentation( guest );
			Occupy( guest );
			states.Add( $"{guest.Id}:{save.State}→{(int)guest.State}" );
		}
		if ( guests.Count > 0 )
			nextId = Math.Max( nextId, guests.Max( guest => guest.Id ) + 1 );
		evidence.Add( $"Guests imported: {guests.Count} of {saved.Count} ({(states.Count == 0 ? "none" : string.Join( ", ", states ))}, saved state → state)." );
		foreach ( var line in adjusted )
			evidence.Add( $"Guest value adjusted on import: {line}." );
		// [APPROX:SAVE-002] a saved guest in a queue or boarding (states 11–14), leaving a ride (15), using an attraction (16) or held by a guard (17) is not imported, nor is a park guest off the paths (a guard chasing one gives the chase up); an imported guest in an activity (state 8) decides again at once, one waiting at the booth (4) waits OpenTPW's booth time rather than its saved wait (+0x200), a paid guest waiting for the gate (3) walks in at once (GUEST-011), an arriving or departing guest walks OpenTPW's nearest arrival lane from where it stands, a turn stamp before the save's turn counts from the import, and the arrival date and index, the admission flags, the event ring and the queue links are not imported — evidence needed: an OpenTPW queue and rider restore for original guests, the booth's and gate's waits in OpenTPW's arrival model
		if ( dropped.Count > 0 )
			evidence.Add( $"[APPROX:SAVE-002] Guests not imported: {string.Join( ", ", dropped )}." );
		evidence.Add( $"[APPROX:SAVE-002] Guest values not imported: arrival dates {string.Join( "/", saved.Select( item => item.ArrivalDate ) )}, waits {string.Join( "/", saved.Select( item => item.ParkOpeningWaitingTime ) )}, paid {string.Join( "/", saved.Select( item => item.PaidAdmission ) )}, queue links {string.Join( "/", saved.Select( item => $"{item.QPrev}-{item.QNext}@{item.QueuePos}" ) )}, saved states {string.Join( "/", saved.Select( item => item.SavedState ) )}." );
		return evidence;
	}

	/// <summary>The valid arrival lane with a cell nearest <paramref name="cell"/> (squared distance, lane A on a tie); null without one.</summary>
	private (int X, int Y)[]? NearestLane( (int X, int Y) cell )
	{
		(int X, int Y)[]? best = null;
		var bestDistance = long.MaxValue;
		foreach ( var lane in new[] { ValidLane( Settings.ArrivalLaneA ), ValidLane( Settings.ArrivalLaneB ) } )
		{
			if ( lane == null )
				continue;
			var distance = lane.Min( point => (long)(point.X - cell.X) * (point.X - cell.X) + (long)(point.Y - cell.Y) * (point.Y - cell.Y) );
			if ( distance < bestDistance )
				(best, bestDistance) = (lane, distance);
		}
		return best;
	}

	/// <summary>A guest in the park: decide (6, 8), wander on (7), walk on to its bin (9) or its ride (10), or leave (18).</summary>
	private void ImportInPark( Guest guest, SaveGuest save, (int X, int Y) cell, (int X, int Y) destination, Func<int, int> attraction, Func<int, int> bin, List<string> adjusted )
	{
		guest.OnGrid = true;
		guest.Lane = null;
		(guest.CellX, guest.CellY) = cell;
		guest.TimeInParkSeconds = null;
		void WalkOn()
		{
			var target = (destination.X >> 8, destination.Y >> 8);
			if ( !Grid.InBounds( target.Item1, target.Item2 ) )
				return;
			var route = Grid.FindPath( cell.X, cell.Y, target.Item1, target.Item2 );
			if ( route.Count > 0 )
				SetDestination( guest, GuestNavigation.BytePoint( target, destination.X & 0xFF, destination.Y & 0xFF ), route );
		}
		switch ( save.State )
		{
			case 7:
				guest.State = GuestState.WalkingAround;
				WalkOn();
				break;
			case 9 when bin( save.MajorDest ) is var found and not 0:
				guest.State = GuestState.ThrowingAwayLitter;
				guest.BinId = found;
				WalkOn();
				break;
			case 10 when attraction( save.MajorDest ) is var ride and not 0:
				// state 10 asks for its route to the ride's back cell on its next update (the original's saved destination is not decoded)
				guest.State = GuestState.GoingToRide;
				guest.AttractionId = ride;
				break;
			case 18:
				guest.State = GuestState.LeavingPark;
				break;
			default:
				if ( save.State is 9 or 10 )
					adjusted.Add( $"guest {guest.Id} state {save.State} target thing {save.MajorDest} is not imported, imported deciding (state 6)" );
				guest.State = GuestState.DoingNothing;
				break;
		}
	}

	/// <summary>A guest on its way in or out: it walks on along the lane, from the point its state has reached.</summary>
	// [BIN:TPW-EXE:0x004FF7D9 booth arrival] state 3 is a guest at the booth while the park is closed (unpaid) or one that has paid (+0x204 = 1, 0x004FF9D0) and waits for the gate: a closed park sends it home once +0x200 has counted down
	private void ImportOnLane( Guest guest, uint state, bool paid, (int X, int Y)[] lane, (int X, int Y) cell, List<string> adjusted )
	{
		guest.OnGrid = false;
		guest.Navigation.OffGrid = true;
		guest.Lane = lane;
		(guest.CellX, guest.CellY) = cell;
		var booth = lane.Length - 2;
		switch ( state )
		{
			case 0:
				(guest.LaneIndex, guest.State) = (1, GuestState.CrossingRoad);
				break;
			case 1:
				(guest.LaneIndex, guest.State) = (Math.Min( 2, booth ), GuestState.CrossingRoadToPark);
				break;
			case 2:
				(guest.LaneIndex, guest.State) = (booth, GuestState.GoingToTicketBooth);
				break;
			case 4:
				(guest.LaneIndex, guest.State, guest.StateTimer) = (Math.Max( 0, booth ), GuestState.AtTicketBooth, TicketBoothSeconds);
				return;
			case 3 when !paid:
				// OpenTPW sends a guest that reaches the booth of a closed park home at once
				adjusted.Add( $"guest {guest.Id} state 3 (unpaid, at the booth of a closed park) imported leaving (state 18)" );
				guest.LaneReversed = true;
				(guest.LaneIndex, guest.State) = (Math.Max( 0, booth - 1 ), booth - 1 <= 1 ? GuestState.CrossingRoadHome : GuestState.LeavingPark);
				break;
			case 3 or 5:
				if ( state == 3 )
					adjusted.Add( $"guest {guest.Id} state 3 (paid, waiting for the gate) imported entering the park (state 5)" );
				(guest.LaneIndex, guest.State) = (lane.Length - 1, GuestState.EnteringPark);
				break;
			case 18:
				guest.LaneReversed = true;
				(guest.LaneIndex, guest.State) = (Math.Max( 2, booth - 1 ), GuestState.LeavingPark);
				break;
			case 19:
				guest.LaneReversed = true;
				(guest.LaneIndex, guest.State) = (1, GuestState.CrossingRoadHome);
				break;
			default:
				guest.LaneReversed = true;
				(guest.LaneIndex, guest.State, guest.StateTimer) = (0, GuestState.WaitingToGoHome, WaitForBusSeconds);
				return;
		}
		// a lane shorter than OpenTPW's five points ([bus stop, two crossing points, booth, entrance]) keeps every index on it
		guest.LaneIndex = Math.Clamp( guest.LaneIndex, 0, lane.Length - 1 );
		SetLaneWaypoint( guest );
	}
}

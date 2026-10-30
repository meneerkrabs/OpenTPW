namespace OpenTPW;

public sealed partial class GuestSimulation
{
	/// <summary>The queue of one saved attraction an import accepted: its ride's bridge id, the guests front first and the called and waiting heads.</summary>
	internal sealed record OriginalQueuePlan( int AttractionThing, int RideId, IReadOnlyList<int> Chain, int Called, bool WaitingForScript );

	/// <summary>
	/// Whether a saved guest stands in the queue states 11 to 14, or in the idle interlude of state 11 (state 8 with <c>mSavedState</c> 11): the guests an attraction's saved queue can hold
	/// (<see cref="PlanOriginalQueues"/>).
	/// </summary>
	// [BIN:TPW-EXE:0x004FC800 spot animation] the idle interludes copy the current state to mSavedState (+0x224), stamp mTimeOfLastSpotAnim (+0x208) with the game tick and enter state 8; the state-11 update calls it at 0x00500324 and 0x0050038B, so a queued guest in state 8 has the saved state 11 and keeps its place in the list
	public static bool IsOriginalQueueMember( SaveGuest guest ) => guest.State is >= 11 and <= 14 || guest.State == 8 && guest.SavedState == 11;

	/// <summary>
	/// Reads the saved queues: for each attraction whose head <c>mFirstInQ</c> (+0x3C) is set, the guests along the <c>mQNext</c> links (+0x228), checked against the links the guests carry back
	/// (<c>mQPrev</c>, +0x22A), their states (11 to 14: standing, moving up, called, handed to the script), the attraction they name (<c>mMajorDest</c>, +0x1DC) and the called guest
	/// (<c>mPersonBeingLoaded</c>, +0x6C). A queue that does not read like that, or whose attraction this park does not place or whose queue the park does not hold, is refused with its reason and its guests
	/// are not imported.
	/// </summary>
	// [BIN:TPW-EXE:0x004DB7D0 attraction serializer] the head (mFirstInQ) and the called guest (mPersonBeingLoaded) are read as saved, the guests' links the same way (0x004FB530): the loader relinks nothing, so a list that is not a chain of guests of this ride in the queue states is a corrupt save
	// [BIN:TPW-EXE:0x00500136 state 11 ride] the queue states read the ride from mMajorDest (+0x1DC), through the object table 0x007CFB90, and the guest's place from the ride's list (0x004DDF50); a guest the list does not hold ends "Problem with a queue - shouldn't be fatal" in the dismissal
	internal (Dictionary<int, OriginalQueuePlan> Plans, List<string> Notes) PlanOriginalQueues( IReadOnlyList<SaveGuest> saved, IReadOnlyList<SaveAttraction>? attractions, Func<int, int> attraction )
	{
		var plans = new Dictionary<int, OriginalQueuePlan>();
		var notes = new List<string>();
		if ( attractions == null )
			return (plans, notes);
		var byThing = saved.ToDictionary( guest => guest.Id );
		var claimed = new HashSet<int>();
		foreach ( var ride in attractions.Where( item => item.FirstInQueue != 0 || item.PersonBeingLoaded != 0 ).OrderBy( item => item.Id ) )
		{
			string? Refuse()
			{
				if ( ride.FirstInQueue == 0 )
					return $"the called guest {ride.PersonBeingLoaded} has no queue";
				var rideId = attraction( ride.Id );
				if ( rideId == 0 || FindAttraction( rideId ) is not RideVisitorBridge bridge )
					return "the park does not place the attraction";
				if ( bridge.QueueCells.Count == 0 )
					return "the park holds no queue for the attraction";
				var chain = new List<int>();
				var seen = new HashSet<int>();
				var previous = 0;
				for ( var id = (int)ride.FirstInQueue; id != 0; )
				{
					if ( !byThing.TryGetValue( id, out var guest ) )
						return $"guest {id} is not a saved guest";
					if ( !seen.Add( id ) )
						return $"the links loop at guest {id}";
					if ( !IsOriginalQueueMember( guest ) )
						return $"guest {id} is in state {guest.State}";
					if ( guest.MajorDest != ride.Id )
						return $"guest {id} names attraction thing {guest.MajorDest}";
					if ( guest.QPrev != previous )
						return $"guest {id} links back to {guest.QPrev}, not {previous}";
					if ( claimed.Contains( id ) )
						return $"guest {id} is in another queue";
					if ( chain.Count != 0 && guest.State is 13 or 14 )
						return $"guest {id} is called forward (state {guest.State}) behind the head";
					if ( guest.PersonType >= Settings.Types.Count )
						return $"guest {id} has type {guest.PersonType} of {Settings.Types.Count}";
					var cell = ((int)guest.Person.Base.X >> 8, (int)guest.Person.Base.Y >> 8);
					if ( !Grid.InBounds( cell.Item1, cell.Item2 ) )
						return $"guest {id} stands outside the map";
					chain.Add( id );
					previous = id;
					id = guest.QNext;
					if ( chain.Count > byThing.Count )
						return "the links loop";
				}
				if ( ride.PersonBeingLoaded != 0 && ride.PersonBeingLoaded != chain[0] )
					return $"the called guest {ride.PersonBeingLoaded} is not the head {chain[0]}";
				var waiting = byThing[chain[0]].State == 14;
				foreach ( var id in chain )
					claimed.Add( id );
				var plan = new OriginalQueuePlan( ride.Id, rideId, chain, ride.PersonBeingLoaded, waiting );
				foreach ( var id in chain )
					plans[id] = plan;
				return null;
			}
			if ( Refuse() is { } reason )
				notes.Add( $"queue of attraction thing {ride.Id} not imported: {reason}" );
		}
		return (plans, notes);
	}

	/// <summary>
	/// Hands each accepted queue to its attraction once the guests are in: the saved chain front first (a guest the import did not keep leaves its place and the order of the others stays), the
	/// called guest and the head the script holds. Returns how many queues went in.
	/// </summary>
	private int RestoreOriginalQueues( Dictionary<int, OriginalQueuePlan> plans )
	{
		var restored = 0;
		foreach ( var plan in plans.Values.Distinct() )
		{
			// the plan took its bridge from this registry before a single guest went in
			var ride = (RideVisitorBridge)FindAttraction( plan.RideId )!;
			var kept = plan.Chain.Where( id => byId.ContainsKey( id ) ).ToList();
			var waiting = plan.WaitingForScript && kept.Count > 0 && kept[0] == plan.Chain[0];
			var called = plan.Called != 0 && kept.Count > 0 && kept[0] == plan.Called ? plan.Called : 0;
			ride.ImportSavedQueue( kept, called, waiting );
			restored++;
		}
		return restored;
	}

	/// <summary>
	/// Run after the scripts are imported: a head imported as handed to the script (state 14) whose script does not hold it in <c>VAR_LETMEON</c> is demoted to a called head at the stand point
	/// (state 13), as it was not admitted. Returns the report lines.
	/// </summary>
	// [APPROX:SAVE-002] the demotion to a called head (state 13) stands in for the state 16 tp.exe's test 0x004E0A70 enters at once, only where the ride's script run state was not imported; a ride whose state was imported boards the head at the first host step (RideVisitorBridge.ReconcileImportedAdmission)
	internal List<string> ReconcileOriginalAdmissions()
	{
		var notes = new List<string>();
		foreach ( var ride in attractions.OfType<RideVisitorBridge>().ToList() )
		{
			var demoted = ride.ReconcileImportedAdmission();
			if ( demoted == 0 || !byId.TryGetValue( demoted, out var guest ) || guest.State != GuestState.WaitingToBoard )
				continue;
			guest.State = GuestState.Boarding;
			guest.QueueCalled = false;
			guest.QueueTargetIndex = 0;
			notes.Add( $"guest {demoted} (queue state 14) is called but not admitted: the script of attraction {ride.AttractionId} does not hold it in VAR_LETMEON, so it presents itself at the stand point again" );
		}
		return notes;
	}

	/// <summary>
	/// A guest of an accepted saved queue: it stays at its saved place with the saved position, move-up delay and admission flag; a guest moving up (12) or called forward (13) walks on to its
	/// saved accurate destination along the queue cells, as the loader routes every person again (<c>0x004FA5F0</c>), and one handed to the script (14) waits for it.
	/// </summary>
	// [BIN:TPW-EXE:0x004F9407 load re-route] the person loader routes to the saved accurate destination in every state: the slot of the queue place in state 12, the stand point in state 13
	// [BIN:TPW-EXE:0x00501B6E state 14] the state-14 update (the dispatch 0x00501A08, table 0x00501D4C) reads the ride from +0x1DC, asks 0x004E0A70 (the script's first variable against mFirstInQ), unlinks the guest (0x004DDD20) and enters state 16
	private void ImportQueued( Guest guest, SaveGuest save, (int X, int Y) cell, (int X, int Y) destination, OriginalQueuePlan queue, List<string> recovery, long parkTurn )
	{
		var ride = FindAttraction( queue.RideId )!;
		var cells = ride.QueueCells;
		guest.AttractionId = queue.RideId;
		guest.QueuePosition = save.QueuePos;
		// the join stamp is a statistic of this host (the wait of a queue the guest joined before the save counts from the import)
		guest.QueueJoinTurn = ParkTurn;
		guest.LastQueueWaitTurns = -1;
		guest.InQueueInterlude = save.State == 8;
		if ( save.State == 8 )
			guest.InterludeTurn = Math.Max( 0, (long)save.TimeOfLastSpotAnim - parkTurn );
		// mBeenAdmitted (+0x1F8) is the call the state-11 update answers with its walk to the stand point (0x004FFFF0, 0x005001A4)
		guest.QueueCalled = save.State is 11 or 8 && save.BeenAdmitted != 0;
		var onQueue = IndexOfCell( cells, cell.X, cell.Y );
		guest.QueueCellIndex = onQueue < 0 ? cells.Count : onQueue;
		var target = (X: destination.X >> 8, Y: destination.Y >> 8);
		var point = GuestNavigation.BytePoint( target, destination.X & 0xFF, destination.Y & 0xFF );
		var targetIndex = IndexOfCell( cells, target.X, target.Y );
		if ( targetIndex < 0 )
			targetIndex = QueueSlot( save.QueuePos, ride.QueueSizeInCells ).CellIndex;
		guest.QueueTargetIndex = save.State == 13 ? 0 : targetIndex;
		guest.QueueTargetX = point.X / (float)Fixed16.One;
		guest.QueueTargetY = point.Y / (float)Fixed16.One;
		switch ( save.State )
		{
			case 8 or 11:
				guest.State = GuestState.Queueing;
				guest.LastQueueUpdateTurn = ParkTurn;
				break;
			case 12:
				guest.State = GuestState.MovingUpQueue;
				if ( !SetQueueDestination( guest, ride, targetIndex, point, toEntrance: false ) )
					recovery.Add( $"guest {guest.Id} (queue state 12) could not renav to its queue place at ({target.X}, {target.Y}); it recovers through the queue update (0x004F9407)" );
				break;
			case 13:
				guest.State = GuestState.Boarding;
				if ( !SetQueueDestination( guest, ride, 0, point, toEntrance: true ) )
					recovery.Add( $"guest {guest.Id} (queue state 13) could not renav to the stand point at ({target.X}, {target.Y}); it presents itself where it stands (0x004F9407)" );
				break;
			default:
				guest.State = GuestState.WaitingToBoard;
				break;
		}
	}
}

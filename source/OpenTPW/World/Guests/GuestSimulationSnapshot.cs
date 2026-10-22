namespace OpenTPW;

/// <summary>Problems found while validating a guest snapshot against a park; empty when the snapshot restores.</summary>
public sealed class GuestSnapshotValidation
{
	private readonly List<string> errors;

	internal GuestSnapshotValidation( List<string> errors ) => this.errors = errors;

	/// <summary>Every problem found, in validation order; empty when the snapshot is valid.</summary>
	public IReadOnlyList<string> Errors => errors;

	/// <summary>True when the snapshot has no problems and may be restored.</summary>
	public bool IsValid => errors.Count == 0;
}

/// <summary>
/// Typed deep copy of one guest's whole simulation state: needs, money, queue bookkeeping, walk speed, navigation
/// buffers and timers, exactly as <see cref="Guest"/> holds them between ticks. Captured by
/// <see cref="GuestSimulation.CaptureSnapshot"/> and restored only into a fresh <see cref="Guest"/> object; arrays are
/// owned by the snapshot and copied again on restore.
/// </summary>
public sealed record GuestSnapshot
{
	public required int Id { get; init; }
	public required int Type { get; init; }
	public required GuestState State { get; init; }

	// Movement and presentation (Draw*/StepFrom* are presentation-only but captured for continuity).
	public required float HeadingX { get; init; }
	public required float HeadingY { get; init; }
	public required bool IsMoving { get; init; }
	public required float DistanceWalked { get; init; }
	public required float DrawX { get; init; }
	public required float DrawY { get; init; }
	public required float DrawDistanceWalked { get; init; }
	public required float StepFromX { get; init; }
	public required float StepFromY { get; init; }
	public required float StepFromDistance { get; init; }
	public required long StepTick { get; init; }
	/// <summary>Optional persistent body history. Absent in older/original saves, which reconstruct a fresh body at load.</summary>
	[System.Text.Json.Serialization.JsonIgnore( Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull )]
	public VisitorSpriteStateSnapshot? BodySprite { get; init; }
	public bool BodySpriteInCostume { get; init; }
	public bool BodySpriteHistoryReconstructed { get; init; }

	// Needs, money and thoughts.
	public required float Hunger { get; init; }
	public required float Thirst { get; init; }
	public required float Toilet { get; init; }
	public required float Energy { get; init; }
	public required float Nausea { get; init; }
	public required float Happiness { get; init; }
	public required int Money { get; init; }
	/// <summary>The exit counter as the guest holds it (an integer count). Held as a double because saves written by
	/// schema 1 store the former seconds-based value as a fraction; the restore truncates it toward zero, and the two
	/// units nearly agree (one count per 0.992 s, ORACLE.md F26).</summary>
	public required double ExitLevel { get; init; }
	/// <summary>Optional display duration in simulated seconds. Older saves omit it; null stays unknown.</summary>
	public double? TimeInParkSeconds { get; init; }
	/// <summary>Optional exact display age, 0..uint.MaxValue turns. Requires known seconds and a saved display phase; absent in older saves, which retain their seconds fallback.</summary>
	[System.Text.Json.Serialization.JsonIgnore( Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull )]
	public long? DisplayTimeInParkTurns { get; init; }
	public required float SecondsSinceRide { get; init; }
	public required int RidesTaken { get; init; }
	/// <summary>The shops visited. Absent in older saves (0).</summary>
	public int ShopsVisited { get; init; }
	/// <summary>The sideshows played, won or lost. Absent in older saves (0).</summary>
	public int SideshowsVisited { get; init; }
	/// <summary>The sideshows won. Absent in older saves (0).</summary>
	public int SideshowsWon { get; init; }
	public required GuestThought Thought { get; init; }
	/// <summary>The thought the bubble shows (None: no bubble). Absent before schema 7.</summary>
	public GuestThought BubbleThought { get; init; }
	/// <summary>The park turn of the last bubble. Absent before schema 7 (0).</summary>
	public long BubbleTurn { get; init; }
	/// <summary>A sideshow player's result (null otherwise). Absent before schema 7.</summary>
	public bool? SideshowWon { get; init; }
	public required int AttractionId { get; init; }
	/// <summary>May name an attraction that is no longer registered (a demolished ride the guest remembers).</summary>
	public required int LastAttractionId { get; init; }
	/// <summary>The four attractions the guest used last, newest first (guest <c>+480..+486</c>); 0 = empty. Absent in
	/// saves written by schema 1, which then start empty.</summary>
	public int[]? UsedHistory { get; init; }
	/// <summary>The four attractions the guest recently turned away from, newest first (guest <c>+488..+494</c>); 0 = empty.
	/// Absent in saves written by schema 1, which then start empty.</summary>
	public int[]? TemporaryHistory { get; init; }

	// Queue bookkeeping (docs/reverse/QUEUE-plan.md §3.2).
	public required int QueuePosition { get; init; }
	public required long QueueJoinTurn { get; init; }
	public required long LastQueueWaitTurns { get; init; }
	public required int QueueMoveDelay { get; init; }
	public required bool QueueCalled { get; init; }
	public required long QueueStandingSinceTurn { get; init; }
	/// <summary>The count of state 10's periodic call (<see cref="Guest.RideWalkCount"/>). Absent in older saves, which restart it at 0.</summary>
	public int RideWalkCount { get; init; }
	/// <summary>The park turns left of the booth's wait (<see cref="Guest.BoothWait"/>). Absent in older saves, which hold none.</summary>
	public int BoothWait { get; init; }
	/// <summary>Whether the guest has paid its admission (<see cref="Guest.Paid"/>). Absent before schema 9: a guest at the booth had not.</summary>
	[System.Text.Json.Serialization.JsonIgnore( Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault )]
	public bool Paid { get; init; }
	/// <summary>The visitor number (<see cref="Guest.VisitorNumber"/>, 0 for none). Absent before schema 9: 0.</summary>
	[System.Text.Json.Serialization.JsonIgnore( Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault )]
	public int VisitorNumber { get; init; }
	/// <summary>The stranded mark (<see cref="Guest.StrandedMark"/>). Absent in older saves, which hold none.</summary>
	public int StrandedMark { get; init; }
	/// <summary>The number the guest took when it entered its cell's list (<see cref="Guest.CellSerial"/>), which orders the cell lists; 0 in older saves, whose lists start in guest order.</summary>
	[System.Text.Json.Serialization.JsonIgnore( Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault )]
	public long CellSerial { get; init; }

	// [EXT:pools-swim] the swim session (docs/POOLS.md); every field is absent (zero) outside it and in saves made before the pools extension, which restore with no swimmers
	/// <summary>The swimmer's heading, 0..63 (<see cref="Guest.SwimHeading"/>).</summary>
	[System.Text.Json.Serialization.JsonIgnore( Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault )]
	public int SwimHeading { get; init; }
	/// <summary>The park turns left of the swim or of the changing back (<see cref="Guest.SwimTurns"/>).</summary>
	[System.Text.Json.Serialization.JsonIgnore( Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault )]
	public int SwimTurns { get; init; }
	/// <summary>The stroke, 0..2 (<see cref="Guest.SwimStroke"/>).</summary>
	[System.Text.Json.Serialization.JsonIgnore( Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault )]
	public int SwimStroke { get; init; }
	/// <summary>The swimmer's goal in 16.16 cells (<see cref="Guest.SwimGoalX"/>, <see cref="Guest.SwimGoalY"/>).</summary>
	[System.Text.Json.Serialization.JsonIgnore( Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault )]
	public int SwimGoalX { get; init; }
	[System.Text.Json.Serialization.JsonIgnore( Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault )]
	public int SwimGoalY { get; init; }
	/// <summary>Whether the swimmer has a goal (<see cref="Guest.SwimHasGoal"/>).</summary>
	[System.Text.Json.Serialization.JsonIgnore( Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault )]
	public bool SwimHasGoal { get; init; }
	/// <summary>The swimmer's own random stream (<see cref="Guest.SwimRandom"/>).</summary>
	[System.Text.Json.Serialization.JsonIgnore( Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault )]
	public uint SwimRandom { get; init; }
	// [EXT:pools-activities] the pool activity of a swim session (docs/POOLS.md, "Activities"); absent (zero) outside state 25 and in saves made before it
	/// <summary>The id of the activity (<see cref="Guest.PoolActivityId"/>).</summary>
	[System.Text.Json.Serialization.JsonIgnore( Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault )]
	public string? PoolActivityId { get; init; }
	/// <summary>The offered target (<see cref="Guest.PoolActivityTarget"/>).</summary>
	[System.Text.Json.Serialization.JsonIgnore( Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault )]
	public int PoolActivityTarget { get; init; }
	/// <summary>The activity's phase (<see cref="Guest.PoolActivityPhase"/>).</summary>
	[System.Text.Json.Serialization.JsonIgnore( Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault )]
	public int PoolActivityPhase { get; init; }
	/// <summary>The activity's turn count (<see cref="Guest.PoolActivityTurns"/>).</summary>
	[System.Text.Json.Serialization.JsonIgnore( Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault )]
	public int PoolActivityTurns { get; init; }
	/// <summary>The activity's point in 16.16 cells (<see cref="Guest.PoolActivityX"/>, <see cref="Guest.PoolActivityY"/>).</summary>
	[System.Text.Json.Serialization.JsonIgnore( Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault )]
	public int PoolActivityX { get; init; }
	[System.Text.Json.Serialization.JsonIgnore( Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault )]
	public int PoolActivityY { get; init; }
	public required long InterludeTurn { get; init; }
	public required bool InQueueInterlude { get; init; }
	/// <summary>The state-6 activity (the original's state 8), <see cref="GuestActivity.None"/> when there is none. Absent in saves
	/// written before schema 3, which then hold none.</summary>
	public GuestActivity Activity { get; init; }
	/// <summary>The litter meter (0..100). Absent in saves written before schema 4, which then hold an empty meter.</summary>
	public float Litter { get; init; }
	/// <summary>The bin a guest in state 9 walks to (0: none). Absent before schema 4.</summary>
	public int BinId { get; init; }
	/// <summary>The prankery flag (0, 100, 101 or 102). Absent before schema 5: no pranksters.</summary>
	public int PrankFlag { get; init; }
	/// <summary>The way out a guard's set-down drew (<see cref="Guest.ExitSide"/>); absent for every other guest and in older saves.</summary>
	[System.Text.Json.Serialization.JsonIgnore( Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull )]
	public int? ExitSide { get; init; }
	/// <summary>The balloon's remaining life (0..255). Absent before schema 6, which then holds no balloons.</summary>
	public int BalloonLife { get; init; }
	/// <summary>Whether the guest holds its balloon. Absent before schema 6.</summary>
	public bool HasBalloon { get; init; }
	/// <summary>Whether the guest wears a costume. Absent before schema 6.</summary>
	public bool InCostume { get; init; }
	/// <summary>[EXT:umbrellas-save] The umbrella state (<see cref="Guest.UmbrellaState"/>, 0 to 4); written only while the guest has an umbrella, so a save without one has no such field and every older save loads as before.</summary>
	[System.Text.Json.Serialization.JsonIgnore( Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault )]
	public int UmbrellaState { get; init; }
	/// <summary>[EXT:umbrellas-save] The umbrella's colour (0 to 2); absent for colour 0 and without an umbrella.</summary>
	[System.Text.Json.Serialization.JsonIgnore( Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault )]
	public int UmbrellaType { get; init; }
	/// <summary>[EXT:umbrellas-save] The park turn a pending open or close is due (<see cref="Guest.UmbrellaDueTurn"/>); only in states 1 and 3.</summary>
	[System.Text.Json.Serialization.JsonIgnore( Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault )]
	public long UmbrellaDueTurn { get; init; }
	/// <summary>[EXT:rain-mood-once-per-day] The game day plus one of the rain mood rule's last application to the guest (<see cref="Guest.RainMoodDay"/>); written only once it has applied, so a save without the extension has no such field.</summary>
	[System.Text.Json.Serialization.JsonIgnore( Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault )]
	public long RainMoodDay { get; init; }
	/// <summary>[EXT:winter-cold] The game day plus one of the cold rule's last application to the guest (<see cref="Guest.ColdMoodDay"/>); written only once it has applied, so a save without the extension has no such field.</summary>
	[System.Text.Json.Serialization.JsonIgnore( Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault )]
	public long ColdMoodDay { get; init; }
	/// <summary>[EXT:winter-save] The warm items the guest wears (<see cref="Guest.WarmItems"/>, bits 1 hat, 2 earmuffs, 4 mittens); written only while the guest has one, so a save without the winter extension has no such field and every older save loads as before.</summary>
	[System.Text.Json.Serialization.JsonIgnore( Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault )]
	public int WarmItems { get; init; }
	/// <summary>[EXT:winter-save] The park day a glühwein's warmth lasts until (<see cref="Guest.WarmUntilDay"/>); written only while set (a glühwein drunk while <c>opentpw.winter-cold</c> is on), so every other save has no such field.</summary>
	[System.Text.Json.Serialization.JsonIgnore( Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault )]
	public long WarmUntilDay { get; init; }
	/// <summary>[EXT:rain-mood-imported-age] The park turn an imported guest without a known time in the park was loaded at (<see cref="Guest.RainMoodAgeStartTurn"/>); written only for such a guest while the extension is on, so every other save has no such field.</summary>
	[System.Text.Json.Serialization.JsonIgnore( Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull )]
	public long? RainMoodAgeStartTurn { get; init; }
	public required float QueueJoinHappiness { get; init; }
	public required int QueueCellIndex { get; init; }
	public required int QueueTargetIndex { get; init; }
	public required float QueueTargetX { get; init; }
	public required float QueueTargetY { get; init; }
	public required long LastQueueUpdateTurn { get; init; }

	// Walk speed.
	public required int BaseSpeed { get; init; }
	public required int ToiletBonus { get; init; }
	public required ushort Boost { get; init; }
	public required float SmoothedSpeed { get; init; }
	public required int MaxSpeed { get; init; }
	public required int MaxForce { get; init; }

	/// <summary>The steering state (+212), including its route and progress history.</summary>
	public required GuestNavigationSnapshot Navigation { get; init; }

	// Cells, arrival lane and timers.
	public required int CellX { get; init; }
	public required int CellY { get; init; }
	public required int PreviousCellX { get; init; }
	public required int PreviousCellY { get; init; }
	public required bool OnGrid { get; init; }
	public required float OffsetX { get; init; }
	public required float OffsetY { get; init; }
	public required bool HasWaypoint { get; init; }
	/// <summary>The arrival lane's cells (one of the settings' lanes, copied) of a guest saved before schema 9 that walked it; null for every guest on the cell map (all of schema 9).</summary>
	public required (int X, int Y)[]? Lane { get; init; }
	public required int LaneIndex { get; init; }
	public required bool LaneReversed { get; init; }
	public required float StateTimer { get; init; }
	/// <summary>The former seconds-based decision timer, written by schemas 1 and 2 only. State 6 now decides once per park turn
	/// (docs/reverse/WANDER-plan.md), so the restore ignores it.</summary>
	public float? DecisionTimer { get; init; }
}

/// <summary>Every field of <see cref="GuestNavigation"/> the next steering step reads. The waypoint buffer itself is
/// derived (Route[Cursor..] refilled five at a time), so only Count is carried and the buffer is rebuilt on restore.</summary>
public sealed record GuestNavigationSnapshot
{
	public required int X { get; init; }
	public required int Y { get; init; }
	public required int VelocityX { get; init; }
	public required int VelocityY { get; init; }
	public required int TargetX { get; init; }
	public required int TargetY { get; init; }
	public required bool Arrived { get; init; }
	public required int Index { get; init; }
	public required int Count { get; init; }
	public required (int X, int Y)[] Route { get; init; }
	public required int Cursor { get; init; }
	public required int WalkCrossings { get; init; }
	public required (int X, int Y)[] AlsoOpen { get; init; }
	public required int History { get; init; }
	public required int PreviousRemaining { get; init; }
	public required bool OffGrid { get; init; }
	public required int Reroutes { get; init; }
	/// <summary>
	/// The fields of a walk of the original kind (<see cref="GuestNavigation.Original"/>: the tp.exe search and edge test); null for a
	/// walk along an explicit route and in saves made before, which restore as route walks (the route above).
	/// </summary>
	[System.Text.Json.Serialization.JsonIgnore( Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull )]
	public OriginalWalkSnapshot? OriginalWalk { get; init; }
	/// <summary>
	/// All five waypoint slots (<see cref="GuestNavigation.Buffer"/>, tp.exe <c>+0x64</c>), the ones past <see cref="Count"/> included: a failed
	/// set-up of the original walk keeps the old slots, and separation reads the first. Null in saves made before, which rebuild the
	/// buffered ones from the route.
	/// </summary>
	[System.Text.Json.Serialization.JsonIgnore( Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull )]
	public (int X, int Y)[]? Waypoints { get; init; }
	/// <summary>The walk mask (<see cref="GuestNavigation.Mask"/>, guest <c>+0x188</c>); 0, the default, is not written and older saves have none.</summary>
	[System.Text.Json.Serialization.JsonIgnore( Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault )]
	public int Mask { get; init; }

	/// <summary>
	/// Walk-structure problems of this snapshot (both kinds), for the save validators; <paramref name="inBounds"/> tells a cell of the map.
	/// </summary>
	internal IEnumerable<string> Problems( int guestId, Func<int, int, bool> inBounds )
	{
		if ( Route == null )
			yield return $"guest {guestId} has no route";
		if ( AlsoOpen == null )
			yield return $"guest {guestId} has no extra-open-cell list";
		if ( Route == null || AlsoOpen == null )
			yield break;
		if ( OriginalWalk is not { } walk )
		{
			if ( Cursor < 0 || Cursor > Route.Length )
				yield return $"guest {guestId} navigation cursor {Cursor} is outside its {Route.Length}-cell route";
			else
			{
				var buffered = Math.Min( GuestNavigation.BufferedWaypoints, Route.Length - Cursor );
				if ( Count != buffered )
					yield return $"guest {guestId} buffers {Count} waypoints, but its route from cursor {Cursor} gives {buffered}";
				if ( Count == 0 ? Index != 0 : (uint)Index >= (uint)Count )
					yield return $"guest {guestId} waypoint index {Index} is outside its {Count} buffered waypoints";
				// A route walk buffers its route from the cursor: the saved slots must say the same.
				if ( Waypoints is { Length: GuestNavigation.BufferedWaypoints } && Count == buffered
					&& !Waypoints.AsSpan( 0, Count ).SequenceEqual( Route.AsSpan( Cursor, Count ) ) )
					yield return $"guest {guestId} buffered waypoints do not match its route from cursor {Cursor}";
			}
		}
		else
		{
			// An original walk buffers the search's waypoints, not its bookkeeping route: its route cursor stays 0.
			if ( Cursor != 0 )
				yield return $"guest {guestId} navigation cursor {Cursor} is set on an original walk";
			if ( Waypoints == null )
				yield return $"guest {guestId} original walk does not carry its waypoint slots";
			if ( walk.PathCount is < 0 or > 0xFC || Count != Math.Min( walk.PathCount, GuestNavigation.BufferedWaypoints ) )
				yield return $"guest {guestId} buffers {Count} of {walk.PathCount} waypoints";
			if ( Count == 0 ? Index != 0 : (uint)Index >= (uint)Count )
				yield return $"guest {guestId} waypoint index {Index} is outside its {Count} buffered waypoints";
			if ( walk.Stamp < 0 || walk.Total < 0 || walk.Ahead < 0 || walk.Beyond < 0 )
				yield return $"guest {guestId} original walk lengths or stamp are negative";
			if ( walk.Progress > Fixed16.One )
				yield return $"guest {guestId} walk progress {walk.Progress} is above ONE";
			if ( OffGrid )
				yield return $"guest {guestId} walks off the grid with an original walk";
		}
		if ( PreviousRemaining < -1 )
			yield return $"guest {guestId} previous remaining distance {PreviousRemaining} is negative";
		if ( Mask is < 0 or > 2 )
			yield return $"guest {guestId} walk mask {Mask} is not 0, 1 or 2";
		if ( Waypoints != null )
		{
			if ( Waypoints.Length != GuestNavigation.BufferedWaypoints )
				yield return $"guest {guestId} carries {Waypoints.Length} waypoint slots, not {GuestNavigation.BufferedWaypoints}";
			foreach ( var (x, y) in Waypoints )
				if ( !inBounds( x, y ) )
					yield return $"guest {guestId} waypoint ({x}, {y}) is outside the map";
		}
		if ( History is < 0 or > 0x7fff )
			yield return $"guest {guestId} progress history {History} is outside its 15-bit window";
		if ( Reroutes < 0 || WalkCrossings < 0 )
			yield return $"guest {guestId} navigation counters ({Reroutes} reroutes, {WalkCrossings} crossings) are negative";
		foreach ( var (x, y) in Route )
			if ( !inBounds( x, y ) )
				yield return $"guest {guestId} route cell ({x}, {y}) is outside the map";
		foreach ( var (x, y) in AlsoOpen )
			if ( !inBounds( x, y ) )
				yield return $"guest {guestId} extra open cell ({x}, {y}) is outside the map";
	}
}

/// <summary>The fields only a walk of the original kind has (tp.exe walk <c>+0x48</c> .. <c>+0xB8</c>, <see cref="GuestNavigation"/>).</summary>
public sealed record OriginalWalkSnapshot
{
	public required bool Stuck { get; init; }
	public required int Stamp { get; init; }
	public required int PathCount { get; init; }
	public required int Total { get; init; }
	public required int Ahead { get; init; }
	public required int Beyond { get; init; }
	public required int Progress { get; init; }
}

/// <summary>One used entry of a script's walk table (<see cref="RideVisitorBridgeSnapshot.WalkTable"/>).</summary>
public sealed record WalkEntrySnapshot( int Slot, int Guest, int Via, int Target, int From, int To, int Mode, int Extra, int State, double Began, double Ends );

/// <summary>
/// Queue and rider bookkeeping of one <see cref="RideVisitorBridge"/>: the guest-id linked list front first, the
/// script-held guests and their timers. The script, the open delegate and the placement's cells metadata are owned by
/// other slices and re-attached around the restore; the computed queue geometry (cells, join, edit count) is captured
/// here because the bridge owns it.
/// </summary>
public sealed record RideVisitorBridgeSnapshot
{
	public required int AttractionId { get; init; }
	/// <summary>Guests in the queue, front first; the linked list is rebuilt from this order.</summary>
	public required int[] Queue { get; init; }
	/// <summary>Guests the script holds, in boarding order.</summary>
	public required int[] OnRide { get; init; }
	/// <summary>WALKOFF guests waiting for WALKGET, in FIFO order.</summary>
	public required int[] WalkingOff { get; init; }
	/// <summary>The script times at which each walking-off guest may be handed out, parallel to <see cref="WalkingOff"/>; null for a ride whose WALKOFF completes at once (every ride but the tour rides).</summary>
	[System.Text.Json.Serialization.JsonIgnore( Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull )]
	public double[]? WalkingOffReady { get; init; }
	/// <summary>The slot nodes and walk action of each rider that WALKON put on a tour ride or with the head action 4 and WALKOFF has not taken off yet (guest, start node, end node, action); null when there are none.</summary>
	[System.Text.Json.Serialization.JsonIgnore( Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull )]
	public (int Guest, int From, int To, int Mode)[]? WalkSlots { get; init; }
	/// <summary>
	/// The script's walk table entry by entry (WALKON, WALKOFF, WALKGET): the table slot, the rider, the walk-on nodes, WALKOFF's nodes, the walk action, the last operand, the state (1 walking on, 2 on board,
	/// 3 walking off, 4 ready) and the script times of the state's start and end; null when no entry is in use. A save written before it restores its entries from <see cref="WalkingOff"/> and <see cref="WalkSlots"/>.
	/// </summary>
	[System.Text.Json.Serialization.JsonIgnore( Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull )]
	public WalkEntrySnapshot[]? WalkTable { get; init; }
	/// <summary>Always empty in a new snapshot: HOP pops the script's own heap (RideVM), not a bridge set. Kept for saves written before; whatever an old save stores here is validated and ignored.</summary>
	public required int[] Hopped { get; init; }
	/// <summary>True in a snapshot written after HUSH/HOP and the limbo table moved into the script VM. A snapshot without it (older save) has its riders, hopped set and limbo list migrated into the restored script on load.</summary>
	[System.Text.Json.Serialization.JsonIgnore( Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault )]
	public bool VmOwnsHeapAndLimbo { get; init; }
	/// <summary>Always empty in a new snapshot: the limbo table is part of the script's state (RideVM, <see cref="RideVMSnapshot.LimboValues"/>). Kept for saves written before; whatever an old save stores here is validated and ignored.</summary>
	public required (int Guest, double Until)[] Limbo { get; init; }
	/// <summary>BOUNCE slot table in index order; interior free slots (guest 0) keep their place.</summary>
	public required (int Guest, double Until, double Start)[] Bounce { get; init; }
	/// <summary>The node base BOUNCESETNODE set (script +0x70); null or absent while it is the creation value 1. Presentation only: not part of the canonical hash.</summary>
	[System.Text.Json.Serialization.JsonIgnore( Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull )]
	public int? BounceNodeBase { get; init; }
	/// <summary>The base height BOUNCESETBASE set (script +0x6E); null or absent while it is the creation value 0. Presentation only: not part of the canonical hash.</summary>
	[System.Text.Json.Serialization.JsonIgnore( Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull )]
	public short? BounceBaseHeight { get; init; }
	/// <summary>The pending visitor (ride +104), 0 when none; stays in <see cref="Queue"/> until the script takes it.</summary>
	public required int CalledGuest { get; init; }
	/// <summary>True when the called guest's id is written to the script's VAR_LETMEON.</summary>
	public required bool LetMeOnWritten { get; init; }
	public required long LastAdmissionTurn { get; init; }
	/// <summary>The ride is in state 1 (<see cref="RideVisitorBridge.IsBrokenDown"/>); absent in older saves, which held no state: the ride update reads VAR_BROKEN again.</summary>
	public bool BrokenDown { get; init; }
	/// <summary>The ride is in state 4 (condemned: the life gauge was 0 when the ride update entered the state, <c>0x004E14E0</c>); null or absent in older saves and for state 1.</summary>
	public bool? Condemned { get; init; }
	public required long CalledTurn { get; init; }
	/// <summary>Computed queue cells from the front (index 0) to the back.</summary>
	public required (int X, int Y)[] QueueCells { get; init; }
	/// <summary>Optional paired live typed-entry caches; absent in legacy snapshots. Preserve pre-sync link-edit history exactly.</summary>
	[System.Text.Json.Serialization.JsonIgnore( Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull )]
	public (int X, int Y)? TypedQueueFront { get; init; }
	[System.Text.Json.Serialization.JsonIgnore( Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull )]
	public int? TypedQueueDirection { get; init; }
	public required int QueueEditCount { get; init; }
	public required (int X, int Y)? JoinCell { get; init; }
	/// <summary>
	/// The cells the guests enter and leave the attraction by, as they stand: a placement resolves them once (<c>Level.ResolveVisitorCells</c>, the nearest walkable cell when nothing laid
	/// the access cell), and only the removal of a cell a placement laid or a queue cell they use resolves them again (<c>Level.RefreshVisitorCells</c>), so a path laid nearer later leaves
	/// them where they were. The resume puts them back before it finds the queues from them, rather than resolve them on the restored grid. Absent in older saves, which take the cells the
	/// restore resolves.
	/// </summary>
	[System.Text.Json.Serialization.JsonIgnore( Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull )]
	public (int X, int Y)? EntranceCell { get; init; }
	[System.Text.Json.Serialization.JsonIgnore( Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull )]
	public (int X, int Y)? ExitCell { get; init; }
	public required long HeadNotReadyStreak { get; init; }
	public required long MaximumHeadNotReadyStreak { get; init; }
	public required long CalledAgeTurns { get; init; }
	public required long MaximumCalledAgeTurns { get; init; }
	public required AdmissionCheck LastAdmissionCheck { get; init; }
	public required int BoardedTotal { get; init; }
	public required int ReleasedTotal { get; init; }
}

/// <summary>
/// Typed deep copy of the whole guest system between ticks: the explicit <see cref="GuestRandom"/> state, tick and
/// counters, the arrival stream, every guest in list order, and per-attraction queue state (plus the simulation's
/// seen queue-edit counters). Economy bindings (<see cref="GuestSimulation.Payments"/>, the arrival clock and points
/// delegates) are live references the park re-attaches after the restore; they are not captured.
/// </summary>
public sealed record GuestSimulationSnapshot
{
	/// <summary>Bumped whenever the DTO fields or their meaning change; restore refuses other versions. Version 1
	/// (before the histories and the integer exit counter) is still accepted and converted: the missing histories start
	/// empty and the former seconds-based <see cref="GuestSnapshot.ExitLevel"/> is truncated toward zero. Versions 1 and 2
	/// (before the state-6 decision, WANDER-plan) restore with no activity, and their decision timer is dropped. Versions 1 to 3 (before
	/// per-cell litter) restore with empty litter meters, versions 1 to 4 (before pranks) without pranksters, versions 1 to 5 (before balloons and costumes)
	/// without either, versions 1 to 6 (before thought bubbles) without a bubble, versions 1 to 7 (before the thing pool) with guest ids that are a simulation's own counter,
	/// versions 1 to 8 (before the arrival walk on the cell map) with guests that walk an arrival lane off the grid, which the restore turns into guests on the map.</summary>
	public const int CurrentSchemaVersion = 9;
	/// <summary>The first schema whose guest ids are slots of the park's thing pool (tp.exe 0x00516270): earlier saves count their guests, and keep those ids.</summary>
	public const int ThingPoolSchemaVersion = 8;
	/// <summary>The first schema whose guests all walk the cell map, the arrival walk's states 0 to 5 included (tp.exe 0x004FF520 and the states around it): earlier saves may hold guests on an arrival lane.</summary>
	public const int ArrivalWalkSchemaVersion = 9;
	/// <summary>The versions <see cref="ParkResumeSaveFile"/> accepts: 9 as written now, 8 before the arrival walk on the cell map, 7 before the thing pool, 6 before thought bubbles, 5 before balloons and costumes, 4 before pranks, 3 before the litter meter, 2 before the state-6 decision, 1 before F24–F26.</summary>
	public static int[] AcceptedSchemaVersions { get; } = { 1, 2, 3, 4, 5, 6, 7, 8, 9 };

	public required int SchemaVersion { get; init; }
	/// <summary>The seed the simulation was constructed with; informational (the restored <see cref="RandomState"/> supersedes it).</summary>
	public required ulong Seed { get; init; }
	/// <summary>The world generator's state as <see cref="GuestSimulation.RandomState"/> (the park's one stream the guests draw from; docs/DETERMINISM.md).</summary>
	public required ulong RandomState { get; init; }
	/// <summary>
	/// Non-null in a snapshot written with the world generator: the generator state (equal to <see cref="RandomState"/>). Older snapshots hold the guests'
	/// own SplitMix64 stream in <see cref="RandomState"/> instead, which the restore ignores (the generator then keeps the state it was built with).
	/// </summary>
	[System.Text.Json.Serialization.JsonIgnore( Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull )]
	public uint? WorldGenerator { get; init; }
	public required long TickCount { get; init; }
	/// <summary>Optional display-clock phase offset, 0..371 fixed ticks. Keeps exact ages on their saved boundaries before host reattachment; not a gameplay clock.</summary>
	[System.Text.Json.Serialization.JsonIgnore( Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull )]
	public int? DisplayTurnPhaseTicks { get; init; }
	public required double TimeSeconds { get; init; }
	/// <summary>Presentation-only absolute int32 sample in the host fixed-clock epoch. Required when per-guest BodySprite history is present.</summary>
	[System.Text.Json.Serialization.JsonIgnore( Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull )]
	public int? BodySpriteClockMilliseconds { get; init; }
	/// <summary>Optional versioned presentation history, excluded from gameplay hashes. Null means old/native reconstruction.</summary>
	[System.Text.Json.Serialization.JsonIgnore( Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull )]
	public BalloonPresentationSnapshot? Balloons { get; init; }
	/// <summary>The id after the highest guest id (before the thing pool: the next guest id the counter handed out; with it the next id is the pool's, <see cref="Things"/>).</summary>
	public required int NextGuestId { get; init; }
	/// <summary>
	/// The simulation's own thing pool, in a snapshot of a simulation that owns one (a standalone simulation); null when the park's pool is shared (the park's save keeps it, see
	/// <see cref="ParkResumeSnapshot"/>) and in saves from before the pool.
	/// </summary>
	[System.Text.Json.Serialization.JsonIgnore( Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull )]
	public ThingPoolSnapshot? Things { get; init; }
	public required long Admissions { get; init; }
	public required int Departed { get; init; }
	/// <summary>The park's visitor count (<see cref="GuestSimulation.VisitorCount"/>). Absent before schema 9: 0.</summary>
	[System.Text.Json.Serialization.JsonIgnore( Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault )]
	public int VisitorCount { get; init; }

	// Arrival stream.
	public required long ArrivalTimerStart { get; init; }
	public required bool DeliveringArrivals { get; init; }
	public required float SpawnTimer { get; init; }
	public required int PendingArrivals { get; init; }
	/// <summary>The arrival vehicle at work (<see cref="ArrivalVehicleKind"/>); null for none and in saves from before the vehicles.</summary>
	public int? ArrivalVehicle { get; init; }
	public required int LaneToggle { get; init; }
	public required bool ArrivalsEnabled { get; init; }

	/// <summary>The grid version the queues last synced with.</summary>
	public required int SeenGridVersion { get; init; }
	/// <summary>Every guest, in the simulation's update order (the order they were built in).</summary>
	public required GuestSnapshot[] Guests { get; init; }
	/// <summary>Every registered attraction's queue state, in registration order (attraction id ascending).</summary>
	public required RideVisitorBridgeSnapshot[] Attractions { get; init; }
	/// <summary>The simulation's seen queue-edit counters, one per attraction in <see cref="Attractions"/> order.</summary>
	public required (int AttractionId, int SeenEditCount)[] SeenQueueEdits { get; init; }
}

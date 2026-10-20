namespace OpenTPW;

/// <summary>
/// One node of a saved track (the persistence twin of <see cref="CoasterNode"/>): the placement record's cells and
/// heights, the stored height parameter, the pylon kind, the stack position, and the derived-but-persisted node flags
/// (winch marks, invalid marks, station flags) and bank. Restored verbatim — the stack index and flags are state the
/// build edits produced, not values the restore re-derives.
/// </summary>
/// <param name="Flags">The <see cref="CoasterNodeFlags"/> as their raw uint (only <see cref="CoasterSave.KnownNodeFlags"/> bits are accepted).</param>
/// <param name="Bank">The auto-bank the knots pass stores on the node (node <c>+216</c>).</param>
/// <param name="LoopRotate">The loop-rotate parameter (node <c>+0xf8</c>); null in a save from before the loop pair (the old 0.5).</param>
/// <param name="ExactX">The exact world position of a pylon-less station node (the middle node S1), null for every other node.</param>
/// <param name="Yaw">The yaw the rotate button set (node <c>+0xE0</c>); null in a save from before the coaster tool panel (no manual yaw).</param>
/// <param name="Incline">The incline the tilt button set (node <c>+0xDC</c>); null in a save from before the coaster tool panel.</param>
internal sealed record CoasterNodeSaveData( int X, int Y, float Height, float T, bool LoopPylon, int StackIndex, bool IsStation, bool IsStationEnd, uint Flags, float Bank, float? LoopRotate = null,
	float? ExactX = null, float? ExactY = null, float? ExactZ = null, float? Yaw = null, float? Incline = null );

/// <summary>
/// One saved train: its front's position and speed, the riders of each car on the lap and, with the original's station rule,
/// its flags, boarding span and the guests seated at the station in each car. A save made before that rule has no flags
/// (<see cref="Flags"/> null): its <see cref="State"/> (0 boarding, 1 running, 2 waiting before the station, 3 unloading) and
/// boarding clock are the old stop-and-dispatch model's, and <see cref="CoasterRide.RestoreSnapshot"/> migrates them.
/// </summary>
/// <param name="State">The old model's train state as its int value; written as 1 by this version and read only to migrate a save without flags.</param>
/// <param name="BoardingSince">The old model's boarding clock; written as 0 and ignored.</param>
/// <param name="Flags">The <see cref="CoasterTrainFlags"/> as their raw int; null in a save from before the station rule.</param>
/// <param name="BoardingCars">The guests seated at the station in each car (they ride when the train leaves); null in an older save.</param>
/// <param name="ReferenceCar">The car whose coordinate <paramref name="Position"/> is (the middle car, train <c>+0x2c</c>); null in a save from before the car layout, whose position is the front car's.</param>
internal sealed record CoasterTrainSaveData( float Position, float Speed, int State, double BoardingSince, List<List<int>> Cars,
	int? Flags = null, int FirstBoardingCar = 0, int LastBoardingCar = 0, List<List<int>>? BoardingCars = null, int? ReferenceCar = null );

/// <summary>
/// A saved track: the station end cells, the ordered nodes (the station's three nodes first, then the pylons in build order,
/// then the station end node a completed track appends), the circuit flags and the edit version. A track saved before the
/// three-node station (COAST-029) holds one station node and, when completed, an end node at the return end;
/// <see cref="CoasterTrack.RestoreSnapshot"/> migrates it. A station without a
/// shaped track (a coaster station whose placement never set its ends, or whose track was deleted) saves an empty node
/// list and null end cells.
/// </summary>
/// <param name="CutAfter">The index of the node before the cut of a circuit the demolish button opened in the middle; null for a whole or end-reopened circuit and in a save from before the coaster tool panel.</param>
internal sealed record CoasterTrackSaveData( (int X, int Y)? DepartCell, (int X, int Y)? ReturnCell, bool IsClosed, bool IsComplete, int Version, List<CoasterNodeSaveData> Nodes, int? CutAfter = null );

/// <summary>
/// One saved coaster controller joined to its station by attraction id: its track, the controller scalars the station
/// script set (<c>COAST</c> commands) and the money and clocks, its trains with every rider, and the riders waiting to
/// leave through <c>COAST 3</c>. The curve samples, rating, winch sections and braking point are re-derived from the
/// restored track on load (the rebuild is the same deterministic pass completion ran), so they are never saved.
/// </summary>
/// <param name="LastDispatch">The old model's last departure time; written as null and ignored (trains no longer depart on a decision).</param>
/// <param name="Admission">The guests <c>COAST 1</c> handed over that wait for a train in the station to seat them; null in a save from before the admission ring.</param>
/// <param name="State">The controller state (1 unfinished, 2 built or out of service, 4 open, 8 closed); null in a save from before it, which held only <paramref name="Closed"/>.</param>
/// <param name="LeadCarCoordinate">Where train 0's last car stood when the trains were last taken off (<see cref="CoasterRide.LeadCarCoordinate"/>); null in an older save (0).</param>
/// <param name="KnotPass">The knot pass the saved trains stood on (<see cref="CoasterRide.KnotPassVersion"/>: 2 = the pylon models' posed track tags); null in a save from before it, whose curve had one knot per node.</param>
/// <param name="MotionClockStarted">Whether the controller's motion tick has run (<see cref="CoasterRide.MotionClockStarted"/>); null in a save from before it, which counts it started on a track that runs.</param>
/// <param name="MotionClockLagSeconds">The simulation time since the last motion tick (<see cref="CoasterRide.MotionClockLagSeconds"/>); null in a save from before the catch-up (0).</param>
/// <param name="SpeedSetByReplay">Controller bit 0x8000 (<see cref="CoasterRide.SpeedSetByReplay"/>); null in a save from before it (clear).</param>
internal sealed record CoasterRideSaveData( int AttractionId, CoasterTrackSaveData Track, bool Initialised, bool Closed, int BreakdownState, int SpeedSetting, int SeatsPerTrain, float SpeedCap, long PaidCost, double TimeSeconds, double? LastDispatch, List<CoasterTrainSaveData> Trains, List<int> Leaving,
	List<int>? Admission = null, int? State = null, float? LeadCarCoordinate = null, int? KnotPass = null, bool? MotionClockStarted = null, double? MotionClockLagSeconds = null, bool? SpeedSetByReplay = null );

/// <summary>The coasters of one saved park (the park-resume envelope's optional coaster part): every live coaster in creation order, which is attraction-id order.</summary>
internal sealed record CoasterWorldSaveData
{
	public required List<CoasterRideSaveData> Rides { get; init; }
}

/// <summary>
/// Capture and restore of the coaster part of a park-resume envelope (docs/PARK-SAVES.md): every live coaster station's
/// controller is joined to its attraction id, and a save with no coaster holds no part at all. The DTOs are deep typed
/// copies (no live objects, no renderer references); the shared <see cref="CoasterRandom"/> state is not here — the
/// script-world snapshot already carries it, so it crosses a save exactly once.
/// </summary>
internal static class CoasterSave
{
	/// <summary>Bound on saved coasters: more stations than any park can place, small enough that no parse is unbounded.</summary>
	public const int MaximumRides = 256;
	/// <summary>Bound on one track's nodes (the station node, every pylon and the station end node).</summary>
	public const int MaximumNodes = 16384;
	/// <summary>The loft parameter a saved node may hold: the height drag may take it above 1 up to what the pylon's maximum height leaves (a coaster whose fMaxHeight is far above its model's top).</summary>
	public const float MaximumLoft = 8f;
	/// <summary>Bound on one coaster's trains in the saved part (the catalog check is exact: the definition's <c>uiMaxTrains</c>, 30 for the largest shipped coaster).</summary>
	public const int MaximumTrains = 64;
	/// <summary>Bound on one train's cars in the saved part (the catalog check is exact: the definition's <c>uiMaxCars</c>, 6 for the largest shipped train).</summary>
	public const int MaximumCarsPerTrain = 32;
	/// <summary>The node flag bits a save may carry; anything else is a format this code does not know, and load refuses it.</summary>
	public const uint KnownNodeFlags = (uint)(CoasterNodeFlags.NoAutoBank | CoasterNodeFlags.LoopMark | CoasterNodeFlags.Loop | CoasterNodeFlags.LoopFirst | CoasterNodeFlags.ZeroBank | CoasterNodeFlags.Winch | CoasterNodeFlags.WinchKept | CoasterNodeFlags.Invalid
		| CoasterNodeFlags.StationTail | CoasterNodeFlags.StationHead | CoasterNodeFlags.NoPreviousLink | CoasterNodeFlags.NoNextLink | CoasterNodeFlags.EndNextIsSecond
		| CoasterNodeFlags.ManualYaw | CoasterNodeFlags.ManualIncline | CoasterNodeFlags.CutEnd | CoasterNodeFlags.CutStart | CoasterNodeFlags.TurnedBack);

	/// <summary>
	/// Captures the park's coasters: one entry per live coaster station in placement (attraction-id) order, or null when
	/// the park has none — a coasterless park writes no coaster part, so pre-coaster saves stay exactly what they were.
	/// </summary>
	public static CoasterWorldSaveData? Capture( CoasterWorld world, ParkObjects objects )
	{
		ArgumentNullException.ThrowIfNull( world );
		ArgumentNullException.ThrowIfNull( objects );
		return Capture( world, objects.Objects
			.Where( item => item.Runtime.Coaster is not null )
			.Select( item => (item.Runtime.AttractionId, item.Runtime.Coaster!) ) );
	}

	/// <summary>
	/// The capture core over the live stations directly (the level's park joins them from its placed objects; a
	/// headless park joins its runtimes the same way): every station's controller in attraction-id order, and every
	/// live controller claimed — a controller without a station means the park is inconsistent and nothing is saved.
	/// </summary>
	public static CoasterWorldSaveData? Capture( CoasterWorld world, IEnumerable<(int AttractionId, CoasterRide Ride)> stations )
	{
		ArgumentNullException.ThrowIfNull( world );
		ArgumentNullException.ThrowIfNull( stations );
		var live = world.Rides.ToHashSet();
		var rides = stations.OrderBy( station => station.AttractionId )
			.Where( station => live.Contains( station.Ride ) )
			.Select( station => CaptureRide( station.Ride, station.AttractionId ) )
			.ToList();
		if ( rides.Count != live.Count )
			throw new InvalidOperationException( $"The park holds {live.Count} coaster controllers but {rides.Count} placed coaster stations; a controller lost its station." );
		return rides.Count == 0 ? null : new CoasterWorldSaveData { Rides = rides };
	}

	/// <summary>The deep copy of one controller: its track, its script-set scalars, its trains with riders, and its leaving queue.</summary>
	internal static CoasterRideSaveData CaptureRide( CoasterRide ride, int attractionId )
	{
		ArgumentNullException.ThrowIfNull( ride );
		var track = ride.Track;
		var nodes = track.Nodes.Select( node => new CoasterNodeSaveData( node.X, node.Y, node.Height, node.T, node.LoopPylon, node.StackIndex, node.IsStation, node.IsStationEnd, (uint)node.Flags, node.Bank, node.LoopRotate,
			node.ExactPosition?.X, node.ExactPosition?.Y, node.ExactPosition?.Z,
			(node.Flags & CoasterNodeFlags.ManualYaw) != 0 ? node.Yaw : null, (node.Flags & CoasterNodeFlags.ManualIncline) != 0 ? node.Incline : null ) ).ToList();
		var trains = ride.Trains.Select( train => new CoasterTrainSaveData( train.Position, train.Speed, CoasterRide.LegacyRunningState, 0, train.Cars.Select( car => car.ToList() ).ToList(),
			(int)train.Flags, train.FirstBoardingCar, train.LastBoardingCar, train.BoardingCars.Select( car => car.ToList() ).ToList(), ride.Definition.ReferenceCar ) ).ToList();
		return new CoasterRideSaveData( attractionId,
			new CoasterTrackSaveData( track.DepartCell, track.ReturnCell, track.IsClosed, track.IsComplete, track.Version, nodes, track.CutAfter ),
			ride.Initialised, ride.Closed, ride.BreakdownState, ride.SpeedSetting, ride.SeatsPerTrain, ride.SpeedCap, ride.PaidCost, ride.TimeSeconds,
			null,
			trains, ride.LeavingRiders.ToList(), ride.AdmissionGuests.ToList(), (int)ride.State, ride.LeadCarCoordinate, CoasterRide.KnotPassVersion, ride.MotionClockStarted, ride.MotionClockLagSeconds, ride.SpeedSetByReplay );
	}
}

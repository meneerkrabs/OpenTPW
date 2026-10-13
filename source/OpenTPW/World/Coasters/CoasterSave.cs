namespace OpenTPW;

/// <summary>
/// One node of a saved track (the persistence twin of <see cref="CoasterNode"/>): the placement record's cells and
/// heights, the stored height parameter, the pylon kind, the stack position, and the derived-but-persisted node flags
/// (winch marks, invalid marks, station flags) and bank. Restored verbatim — the stack index and flags are state the
/// build edits produced, not values the restore re-derives.
/// </summary>
/// <param name="Flags">The <see cref="CoasterNodeFlags"/> as their raw uint (only <see cref="CoasterSave.KnownNodeFlags"/> bits are accepted).</param>
/// <param name="Bank">The auto-bank the knots pass stores on the node (node <c>+216</c>).</param>
internal sealed record CoasterNodeSaveData( int X, int Y, float Height, float T, bool LoopPylon, int StackIndex, bool IsStation, bool IsStationEnd, uint Flags, float Bank );

/// <summary>One saved train: its front's position and speed, its state, its boarding clock and the riders of each car.</summary>
/// <param name="State">The <see cref="CoasterTrainState"/> as its int value.</param>
internal sealed record CoasterTrainSaveData( float Position, float Speed, int State, double BoardingSince, List<List<int>> Cars );

/// <summary>
/// A saved track: the station end cells, the ordered nodes (the station node first, then the pylons in build order,
/// then the station end node a completed track appends), the circuit flags and the edit version. A station without a
/// shaped track (a coaster station whose placement never set its ends, or whose track was deleted) saves an empty node
/// list and null end cells.
/// </summary>
internal sealed record CoasterTrackSaveData( (int X, int Y)? DepartCell, (int X, int Y)? ReturnCell, bool IsClosed, bool IsComplete, int Version, List<CoasterNodeSaveData> Nodes );

/// <summary>
/// One saved coaster controller joined to its station by attraction id: its track, the controller scalars the station
/// script set (<c>COAST</c> commands) and the money and clocks, its trains with every rider, and the riders waiting to
/// leave through <c>COAST 3</c>. The curve samples, rating, winch sections and braking point are re-derived from the
/// restored track on load (the rebuild is the same deterministic pass completion ran), so they are never saved.
/// </summary>
/// <param name="LastDispatch">Park time of the last dispatch, or null while no train has ever been dispatched (the live <c>-∞</c> initial value, which JSON cannot carry).</param>
internal sealed record CoasterRideSaveData( int AttractionId, CoasterTrackSaveData Track, bool Initialised, bool Closed, int BreakdownState, int SpeedSetting, int SeatsPerTrain, float SpeedCap, long PaidCost, double TimeSeconds, double? LastDispatch, List<CoasterTrainSaveData> Trains, List<int> Leaving );

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
	/// <summary>The node flag bits a save may carry; anything else is a format this code does not know, and load refuses it.</summary>
	public const uint KnownNodeFlags = (uint)(CoasterNodeFlags.NoAutoBank | CoasterNodeFlags.Loop | CoasterNodeFlags.ZeroBank | CoasterNodeFlags.Winch | CoasterNodeFlags.Invalid);

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
		var nodes = track.Nodes.Select( node => new CoasterNodeSaveData( node.X, node.Y, node.Height, node.T, node.LoopPylon, node.StackIndex, node.IsStation, node.IsStationEnd, (uint)node.Flags, node.Bank ) ).ToList();
		var trains = ride.Trains.Select( train => new CoasterTrainSaveData( train.Position, train.Speed, (int)train.State, train.BoardingSince, train.Cars.Select( car => car.ToList() ).ToList() ) ).ToList();
		return new CoasterRideSaveData( attractionId,
			new CoasterTrackSaveData( track.DepartCell, track.ReturnCell, track.IsClosed, track.IsComplete, track.Version, nodes ),
			ride.Initialised, ride.Closed, ride.BreakdownState, ride.SpeedSetting, ride.SeatsPerTrain, ride.SpeedCap, ride.PaidCost, ride.TimeSeconds,
			double.IsNegativeInfinity( ride.LastDispatch ) ? null : ride.LastDispatch,
			trains, ride.LeavingRiders.ToList() );
	}
}

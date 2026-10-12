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
	public required float SecondsSinceRide { get; init; }
	public required int RidesTaken { get; init; }
	public required GuestThought Thought { get; init; }
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
	public required long InterludeTurn { get; init; }
	public required bool InQueueInterlude { get; init; }
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
	/// <summary>The arrival lane's cells (one of the settings' lanes, copied); null while the guest is on the grid.</summary>
	public required (int X, int Y)[]? Lane { get; init; }
	public required int LaneIndex { get; init; }
	public required bool LaneReversed { get; init; }
	public required float StateTimer { get; init; }
	public required float DecisionTimer { get; init; }
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
}

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
	/// <summary>Guests already picked by HOP, sorted.</summary>
	public required int[] Hopped { get; init; }
	/// <summary>LIMBO entries in slot order.</summary>
	public required (int Guest, double Until)[] Limbo { get; init; }
	/// <summary>BOUNCE slot table in index order; interior free slots (guest 0) keep their place.</summary>
	public required (int Guest, double Until, double Start)[] Bounce { get; init; }
	/// <summary>The pending visitor (ride +104), 0 when none; stays in <see cref="Queue"/> until the script takes it.</summary>
	public required int CalledGuest { get; init; }
	/// <summary>True when the called guest's id is written to the script's VAR_LETMEON.</summary>
	public required bool LetMeOnWritten { get; init; }
	public required long LastAdmissionTurn { get; init; }
	public required long CalledTurn { get; init; }
	/// <summary>Computed queue cells from the front (index 0) to the back.</summary>
	public required (int X, int Y)[] QueueCells { get; init; }
	public required int QueueEditCount { get; init; }
	public required (int X, int Y)? JoinCell { get; init; }
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
	/// empty and the former seconds-based <see cref="GuestSnapshot.ExitLevel"/> is truncated toward zero.</summary>
	public const int CurrentSchemaVersion = 2;
	/// <summary>The versions <see cref="ParkResumeSaveFile"/> accepts: 2 as written now, 1 as written before F24–F26.</summary>
	public static int[] AcceptedSchemaVersions { get; } = { 1, 2 };

	public required int SchemaVersion { get; init; }
	/// <summary>The seed the simulation was constructed with; informational (the restored <see cref="RandomState"/> supersedes it).</summary>
	public required ulong Seed { get; init; }
	public required ulong RandomState { get; init; }
	public required long TickCount { get; init; }
	public required double TimeSeconds { get; init; }
	/// <summary>The next guest id the allocator hands out.</summary>
	public required int NextGuestId { get; init; }
	public required long Admissions { get; init; }
	public required int Departed { get; init; }

	// Arrival stream.
	public required long ArrivalTimerStart { get; init; }
	public required bool DeliveringArrivals { get; init; }
	public required float SpawnTimer { get; init; }
	public required int PendingArrivals { get; init; }
	public required int LaneToggle { get; init; }
	public required bool ArrivalsEnabled { get; init; }

	/// <summary>The grid version the queues last synced with.</summary>
	public required int SeenGridVersion { get; init; }
	/// <summary>Every guest, in the simulation's update order (id ascending in live state).</summary>
	public required GuestSnapshot[] Guests { get; init; }
	/// <summary>Every registered attraction's queue state, in registration order (attraction id ascending).</summary>
	public required RideVisitorBridgeSnapshot[] Attractions { get; init; }
	/// <summary>The simulation's seen queue-edit counters, one per attraction in <see cref="Attractions"/> order.</summary>
	public required (int AttractionId, int SeenEditCount)[] SeenQueueEdits { get; init; }
}

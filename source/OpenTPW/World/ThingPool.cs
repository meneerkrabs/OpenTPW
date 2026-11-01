namespace OpenTPW;

/// <summary>One used slot of the thing pool: the thing's id and its model (the class byte, <see cref="SaveThingModel"/>).</summary>
public readonly record struct ThingSlot( int Id, SaveThingModel Model );

/// <summary>One used slot of a <see cref="ThingPoolSnapshot"/>: the thing's id and its model number.</summary>
public sealed record ThingSlotData( int Id, int Model );

/// <summary>
/// What a park-resume save keeps of the thing pool: the used list newest first (the original's save keeps the chain from its head, with each thing's id and model), the
/// head of the free list in its exact order, the destroy requests that wait and the placeholder slots. The original's load rebuilds the free list ascending (0x00518D43); OpenTPW's own
/// save keeps the order the live pool had, so a resumed park hands out the ids a continuing one would.
/// </summary>
public sealed record ThingPoolSnapshot
{
	/// <summary>The used things, the one built last first.</summary>
	public required IReadOnlyList<ThingSlotData> Used { get; init; }
	/// <summary>The free ids that come before the ascending run of the others, in allocation order (empty for a pool rebuilt by a load).</summary>
	public IReadOnlyList<int> FreeFirst { get; init; } = Array.Empty<int>();
	/// <summary>
	/// The ids whose destruction was asked for and not yet carried out (a thing demolished or dismissed since the turn's last end), in the order they were asked: they stay in the used list
	/// until the turn's end, so no thing names them. Absent when none waits.
	/// </summary>
	[System.Text.Json.Serialization.JsonIgnore( Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull )]
	public IReadOnlyList<int>? Pending { get; init; }
	/// <summary>
	/// The used slots that hold a placeholder: a thing an imported original save lists and OpenTPW does not rebuild. The slot stays used, so later things take the ids the original would
	/// hand out, and no OpenTPW thing names it. Absent when none is held.
	/// </summary>
	[System.Text.Json.Serialization.JsonIgnore( Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull )]
	public IReadOnlyList<int>? Held { get; init; }
}

/// <summary>
/// The park's one thing pool: the ids of every thing the world holds (guests, staff, the attractions, the fixed items and the vehicles, and the world's own
/// singletons) come from one table of <see cref="Capacity"/> slots, as tp.exe's allocator does. A thing's id is a pool slot, so it depends on the whole
/// history of what was built and destroyed before it: ids 1, 2, 3, ... on a fresh park, a destroyed thing's slot taken again by the next thing built
/// (last in, first out), and a thing's destruction deferred to the end of the park turn.
/// </summary>
/// <remarks>
/// The pool also keeps the used list newest first (the original's walk order of the thing sweep, 0x516380), which a save stores as the chain's head and each
/// thing's link and which a load restores.
/// </remarks>
// [APPROX:WALK-010] thing ids are slots of one pool for every kind of thing, taken in the order the park builds them (see WalkApproximations); the guests' histories, queues and the economy hold the script world's attraction ids, not the objects' thing ids
// [EXT:developer-prototype] [EXT:online-visit-presence] [EXT:online-coop-presence] what the original builds that OpenTPW does not takes no slot: the developer prototype ride (OpenTPW only) and the online persons and avatars (drawn as presence figures, user-approved 2026-10-16); [EXT:resume-exact-thing-pool] an imported save's not-yet-rebuilt things hold a placeholder slot, see Hold (user-approved 2026-10-17: the original's thing would run on and be destroyed in time, and what each placeholder stands for is tracked by the SAVE IDs)
// [EXT:resume-exact-thing-pool] OpenTPW's own park-resume save keeps the free list's exact order, the pending destroy requests and the placeholder slots (ThingPoolSnapshot), where the original's load rebuilds the free list ascending (0x518D43): a resumed park then continues exactly as the live one would have, which the original's save format cannot do, and an original save is imported with the original's own rebuild
// [BIN:TPW-EXE:0x00515F30 pool init] 10239 slots of 0x14 bytes from 0x7CFB90 (slot 0 is the empty sentinel), each slot's id its index, chained into the free list in ascending order (slot 1 first, 0x515F5B..0x515F75; the last id is 0x27FF = 10239, 0x515F8F); the used list (head 0x7CF56C) is empty
// [BIN:TPW-EXE:0x00516270 allocate] the free list's head is taken (0x516271), a full pool logs "FATAL ERROR - Thing Array is full!" and gives id 0 (0x516279..0x516290); the slot becomes the new head of the used list (its older link +8 the old head, 0x5162BB..0x5162D4) and the id is the slot's index (0x5162FA)
// [BIN:TPW-EXE:0x00516200 free] the slot leaves the used list (0x516216..0x516238) and becomes the new head of the free list (0x51624B..0x516254): the next allocation takes the slot freed last
// [BIN:TPW-EXE:0x00516310 deferred destroy] a destroy request (0x50B780) puts the id at the head of a queue (0x51AD40); the park turn's end (0x516641..0x516691) takes the head again and again, freeing each slot (0x516200), so a turn's destroys are freed last request first
// [BIN:TPW-EXE:0x00518D43 free list rebuild] a load keeps the saved things in their slots and rebuilds the free list from the unused slots in ascending order (0x518D50..0x518D7A)
public sealed class ThingPool
{
	/// <summary>The number of slots, so the highest id (0x27FF).</summary>
	public const int Capacity = 10239;

	private readonly SaveThingModel[] model = new SaveThingModel[Capacity + 1];
	private readonly bool[] used = new bool[Capacity + 1];
	// the used list: newest = head; older[id] is the thing allocated before it, newer[id] the one after it (0: none)
	private readonly int[] older = new int[Capacity + 1];
	private readonly int[] newer = new int[Capacity + 1];
	private readonly int[] nextFree = new int[Capacity + 1];
	private readonly bool[] queued = new bool[Capacity + 1];
	private readonly bool[] held = new bool[Capacity + 1];
	private readonly List<int> destroyQueue = new();
	private int freeHead;
	private int usedHead;
	private int usedCount;

	public ThingPool() => ResetFreeList();

	/// <summary>The number of used slots.</summary>
	public int Count => usedCount;

	/// <summary>The id the next <see cref="Allocate"/> gives (the free list's head), 0 when the pool is full.</summary>
	public int NextId => freeHead;

	/// <summary>The number of destroy requests waiting for the end of the turn.</summary>
	public int QueuedCount => destroyQueue.Count;

	/// <summary>The number of placeholder slots (<see cref="Hold"/>).</summary>
	public int HeldCount => held.Count( flag => flag );

	/// <summary>The head of the used list: the thing allocated last, 0 when none.</summary>
	public int Newest => usedHead;

	public bool IsUsed( int id ) => id is >= 1 and <= Capacity && used[id];

	/// <summary>Whether a used slot's destruction has been asked for and waits for the turn's end: the park turn's thing sweep skips it (0x516380 tests the thing's destroyed byte +3).</summary>
	// [BIN:TPW-EXE:0x0050B780 destroy request] the request sets the thing's byte +3 to 1 (0x0050B8E9), queues the id (0x0050B8FD), then sets its type byte to 2 (0x0050B906) before returning; the thing sweep dispatches only things whose byte +3 is 0 (0x005163F0..0x005163F7), so a thing asked to go earlier in the turn is not updated again
	public bool IsQueued( int id ) => id is >= 1 and <= Capacity && used[id] && queued[id];

	/// <summary>Whether a used slot holds a placeholder (<see cref="Hold"/>).</summary>
	public bool IsHeld( int id ) => id is >= 1 and <= Capacity && held[id];

	/// <summary>The placeholder slots in ascending order.</summary>
	public IEnumerable<int> HeldIds()
	{
		for ( var id = 1; id <= Capacity; id++ )
			if ( held[id] )
				yield return id;
	}

	/// <summary>
	/// Keeps a used slot for a thing of an imported original save that OpenTPW does not rebuild: the original still has that thing, so its id is not handed out again and later things
	/// get the ids the original gives them. The slot keeps its place in the used list and its model; nothing in OpenTPW names it, and it is never destroyed by a request. False for a slot
	/// that is not in use or already waits for destruction. Holding a slot cannot cancel an existing destroy request.
	/// </summary>
	public bool Hold( int id )
	{
		if ( !IsUsed( id ) || queued[id] )
			return false;
		held[id] = true;
		return true;
	}

	/// <summary>The model of a used slot; throws for a free or out-of-range one.</summary>
	public SaveThingModel ModelOf( int id ) => IsUsed( id ) ? model[id] : throw new ArgumentOutOfRangeException( nameof( id ), $"Thing {id} is not in use." );

	/// <summary>The used things newest first, as the original walks its thing list (head pointer 0x7CF56C read at 0x5163A4, slots indexed from the table at 0x7CFB90, each slot's older link +8).</summary>
	public IEnumerable<ThingSlot> UsedNewestFirst()
	{
		for ( var id = usedHead; id != 0; id = older[id] )
			yield return new ThingSlot( id, model[id] );
	}

	/// <summary>The free ids in the order the allocations would take them.</summary>
	public IEnumerable<int> FreeInOrder()
	{
		for ( var id = freeHead; id != 0; id = nextFree[id] )
			yield return id;
	}

	/// <summary>
	/// Builds a thing: the slot at the free list's head becomes the newest of the used list. Returns its id, or 0 when the pool is full (the original logs a fatal
	/// error and carries on with id 0; the callers here refuse to build the thing).
	/// </summary>
	public int Allocate( SaveThingModel thingModel )
	{
		ValidateModel( thingModel );
		var id = freeHead;
		if ( id == 0 )
			return 0;
		freeHead = nextFree[id];
		nextFree[id] = 0;
		Link( id, thingModel );
		return id;
	}

	/// <summary>
	/// Asks for a thing's destruction: it stays in use until <see cref="DestroyQueued"/> runs at the turn's end. Asking again for the same thing, or for a free
	/// slot or a placeholder, changes nothing.
	/// </summary>
	public void QueueDestroy( int id )
	{
		if ( !IsUsed( id ) || held[id] || queued[id] )
			return;
		queued[id] = true;
		destroyQueue.Add( id );
	}

	/// <summary>
	/// The turn's end: the queue's head is taken until it is empty (the request made last first) and each slot is freed. Returns the ids in the order they were freed.
	/// </summary>
	public IReadOnlyList<int> DestroyQueued()
	{
		var freed = new List<int>( destroyQueue.Count );
		while ( destroyQueue.Count > 0 )
		{
			var id = destroyQueue[^1];
			destroyQueue.RemoveAt( destroyQueue.Count - 1 );
			queued[id] = false;
			Release( id );
			freed.Add( id );
		}
		return freed;
	}

	/// <summary>Frees a slot at once: it leaves the used list and becomes the head of the free list. False for a slot that is not in use.</summary>
	public bool Release( int id )
	{
		if ( !IsUsed( id ) )
			return false;
		Unlink( id );
		used[id] = false;
		held[id] = false;
		model[id] = 0;
		nextFree[id] = freeHead;
		freeHead = id;
		usedCount--;
		if ( queued[id] )
		{
			queued[id] = false;
			destroyQueue.Remove( id );
		}
		return true;
	}

	/// <summary>
	/// The things a new park builds in its world setup, in the constructor order of tp.exe's 0x5156A0: the staff HQ, the mechanic HQ, the advisor controller, the tag
	/// system, the park analyser, the research lab, the weather, the bank, the UI message receiver and the challenge system take ids 1 to 10; the gates and the
	/// lights (attractions) follow as the park's objects.
	/// </summary>
	// [BIN:TPW-EXE:0x005156A0 world setup] the constructors run in this order: 0x5089C0 (model 9), 0x4DA920 (10), 0x599E70 (11), 0x509B40 (12), 0x4C58C0 (13), 0x502D20 (14), 0x511F70 (15), 0x4CF7C0 (16), 0x50C9B0 (17), 0x4D1BD0 (19), then the gates and the lights through 0x4DB090 (0x515BE1, 0x515C94), each through the base constructor 0x50AFE0 and the allocator; the jungle level's shipped save lists them as thing ids 1 to 10 with these models and ids 11 and 12 as attractions
	public static IReadOnlyList<SaveThingModel> WorldSetupModels { get; } = new[]
	{
		SaveThingModel.StaffHq, SaveThingModel.MechanicHq, SaveThingModel.AdvisorController, SaveThingModel.TagSystem, SaveThingModel.ParkAnalyser,
		SaveThingModel.ResearchLab, SaveThingModel.Weather, SaveThingModel.Bank, SaveThingModel.UIMsgReceiver, SaveThingModel.ChallengeSystem
	};

	/// <summary>Builds the world's singletons (<see cref="WorldSetupModels"/>) and returns their ids in order.</summary>
	public IReadOnlyList<int> AllocateWorldSetup()
	{
		var ids = new List<int>( WorldSetupModels.Count );
		foreach ( var singleton in WorldSetupModels )
			ids.Add( Allocate( singleton ) );
		return ids;
	}

	/// <summary>The pool of a load: the things in their used-list order (newest first) in their own slots, and the free list rebuilt from the unused slots in ascending order.</summary>
	/// <exception cref="InvalidDataException">An id is out of range or repeated, or a model is not a thing model.</exception>
	public void Restore( IEnumerable<ThingSlot> newestFirst )
	{
		ArgumentNullException.ThrowIfNull( newestFirst );
		var slots = newestFirst.ToList();
		var seen = new HashSet<int>();
		foreach ( var slot in slots )
		{
			if ( slot.Id is < 1 or > Capacity )
				throw new InvalidDataException( $"Thing id {slot.Id} is outside the pool's 1..{Capacity}." );
			if ( !IsThingModel( slot.Model ) )
				throw new InvalidDataException( $"Thing {slot.Id} has model {(int)slot.Model}, which is not a thing model." );
			if ( !seen.Add( slot.Id ) )
				throw new InvalidDataException( $"Thing id {slot.Id} appears more than once." );
		}
		Clear();
		// the list is given newest first: link from the oldest up so that each becomes the head in turn
		for ( var index = slots.Count - 1; index >= 0; index-- )
			Link( slots[index].Id, slots[index].Model );
		RebuildFreeList();
	}

	/// <summary>Empties the pool: every slot free in ascending order, no request waiting.</summary>
	public void Clear()
	{
		Array.Clear( model );
		Array.Clear( used );
		Array.Clear( older );
		Array.Clear( newer );
		Array.Clear( queued );
		Array.Clear( held );
		destroyQueue.Clear();
		usedHead = 0;
		usedCount = 0;
		ResetFreeList();
	}

	/// <summary>
	/// Takes one specific slot as the newest of the used list: for a thing that must keep an id it was saved with (a guest or an object of an older save, a thing a
	/// load rebuilds one at a time). The free list keeps its order without the slot. False when the slot is out of range or in use.
	/// </summary>
	public bool Occupy( int id, SaveThingModel thingModel )
	{
		if ( id is < 1 or > Capacity || used[id] || !IsThingModel( thingModel ) )
			return false;
		var previous = 0;
		for ( var free = freeHead; free != 0; previous = free, free = nextFree[free] )
		{
			if ( free != id )
				continue;
			if ( previous == 0 )
				freeHead = nextFree[free];
			else
				nextFree[previous] = nextFree[free];
			break;
		}
		nextFree[id] = 0;
		Link( id, thingModel );
		return true;
	}

	/// <summary>A copy of the used list newest first.</summary>
	public ThingSlot[] Capture() => UsedNewestFirst().ToArray();

	/// <summary>What a save keeps of the pool: the used list and the free list's head in order (the longest ascending tail of the free list is left out, a restore rebuilds it).</summary>
	public ThingPoolSnapshot CaptureSnapshot()
	{
		var free = FreeInOrder().ToList();
		var tail = free.Count;
		while ( tail > 0 && (tail == free.Count || free[tail - 1] < free[tail]) )
			tail--;
		return new ThingPoolSnapshot
		{
			Used = UsedNewestFirst().Select( slot => new ThingSlotData( slot.Id, (int)slot.Model ) ).ToArray(),
			FreeFirst = free.Take( tail ).ToArray(),
			Pending = destroyQueue.Count == 0 ? null : destroyQueue.ToArray(),
			Held = HeldCount == 0 ? null : HeldIds().ToArray()
		};
	}

	/// <summary>The problems that stop <paramref name="snapshot"/> from restoring (an id outside the pool or repeated, an unknown model, a free id that is in use, a placeholder or a waiting destroy that is not a used slot); empty when it restores.</summary>
	public static IReadOnlyList<string> Problems( ThingPoolSnapshot? snapshot )
	{
		var problems = new List<string>();
		if ( snapshot?.Used == null || snapshot.FreeFirst == null )
		{
			problems.Add( "the thing pool snapshot has no used list or free list" );
			return problems;
		}
		var used = new HashSet<int>();
		foreach ( var slot in snapshot.Used )
		{
			if ( slot == null )
				problems.Add( "a used thing slot is missing" );
			else if ( slot.Id is < 1 or > Capacity )
				problems.Add( $"thing id {slot.Id} is outside the pool's 1..{Capacity}" );
			else if ( slot.Model is < 1 or > 19 )
				problems.Add( $"thing {slot.Id} has model {slot.Model}, which is not a thing model" );
			else if ( !used.Add( slot.Id ) )
				problems.Add( $"thing id {slot.Id} appears more than once" );
		}
		var first = new HashSet<int>();
		foreach ( var id in snapshot.FreeFirst )
		{
			if ( id is < 1 or > Capacity )
				problems.Add( $"free thing id {id} is outside the pool's 1..{Capacity}" );
			else if ( used.Contains( id ) )
				problems.Add( $"free thing id {id} is in use" );
			else if ( !first.Add( id ) )
				problems.Add( $"free thing id {id} appears more than once" );
		}
		var pending = new HashSet<int>();
		foreach ( var id in snapshot.Pending ?? Array.Empty<int>() )
		{
			if ( !used.Contains( id ) )
				problems.Add( $"thing id {id} waits for its destruction but is not in use" );
			else if ( !pending.Add( id ) )
				problems.Add( $"thing id {id} waits for its destruction twice" );
		}
		var placeholders = new HashSet<int>();
		foreach ( var id in snapshot.Held ?? Array.Empty<int>() )
		{
			if ( !used.Contains( id ) )
				problems.Add( $"thing id {id} is a placeholder but is not in use" );
			else if ( !placeholders.Add( id ) )
				problems.Add( $"thing id {id} is a placeholder twice" );
			else if ( pending.Contains( id ) )
				problems.Add( $"thing id {id} is a placeholder and waits for its destruction" );
		}
		return problems;
	}

	/// <summary>Restores a <see cref="CaptureSnapshot"/>: the used list as saved and the free list as saved (its head, then the others ascending).</summary>
	/// <exception cref="InvalidDataException">The snapshot has <see cref="Problems"/>.</exception>
	public void RestoreSnapshot( ThingPoolSnapshot snapshot )
	{
		var problems = Problems( snapshot );
		if ( problems.Count != 0 )
			throw new InvalidDataException( problems[0] );
		Restore( snapshot.Used.Select( slot => new ThingSlot( slot.Id, (SaveThingModel)slot.Model ) ) );
		if ( snapshot.FreeFirst.Count != 0 )
		{
			// the saved head first, then the unused ids that are not in it, ascending
			var first = snapshot.FreeFirst.ToHashSet();
			var order = snapshot.FreeFirst.Concat( FreeInOrder().Where( id => !first.Contains( id ) ) ).ToList();
			for ( var index = 0; index < order.Count; index++ )
				nextFree[order[index]] = index + 1 < order.Count ? order[index + 1] : 0;
			freeHead = order[0];
		}
		foreach ( var id in snapshot.Held ?? Array.Empty<int>() )
			Hold( id );
		foreach ( var id in snapshot.Pending ?? Array.Empty<int>() )
			QueueDestroy( id );
	}

	private void Link( int id, SaveThingModel thingModel )
	{
		model[id] = thingModel;
		used[id] = true;
		older[id] = usedHead;
		newer[id] = 0;
		if ( usedHead != 0 )
			newer[usedHead] = id;
		usedHead = id;
		usedCount++;
	}

	private void Unlink( int id )
	{
		var before = older[id];
		var after = newer[id];
		if ( after != 0 )
			older[after] = before;
		else
			usedHead = before;
		if ( before != 0 )
			newer[before] = after;
		older[id] = 0;
		newer[id] = 0;
	}

	private void ResetFreeList()
	{
		for ( var id = 1; id <= Capacity; id++ )
			nextFree[id] = id < Capacity ? id + 1 : 0;
		freeHead = 1;
	}

	private void RebuildFreeList()
	{
		freeHead = 0;
		var last = 0;
		for ( var id = 1; id <= Capacity; id++ )
		{
			nextFree[id] = 0;
			if ( used[id] )
				continue;
			if ( last == 0 )
				freeHead = id;
			else
				nextFree[last] = id;
			last = id;
		}
	}

	private static bool IsThingModel( SaveThingModel value ) => (int)value is >= 1 and <= 19;

	private static void ValidateModel( SaveThingModel value )
	{
		if ( !IsThingModel( value ) )
			throw new ArgumentOutOfRangeException( nameof( value ), $"Model {(int)value} is not a thing model." );
	}
}

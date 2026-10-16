namespace OpenTPW;

/// <summary>One used slot of the thing pool: the thing's id and its model (the class byte, <see cref="SaveThingModel"/>).</summary>
public readonly record struct ThingSlot( int Id, SaveThingModel Model );

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

	/// <summary>The head of the used list: the thing allocated last, 0 when none.</summary>
	public int Newest => usedHead;

	public bool IsUsed( int id ) => id is >= 1 and <= Capacity && used[id];

	/// <summary>The model of a used slot; throws for a free or out-of-range one.</summary>
	public SaveThingModel ModelOf( int id ) => IsUsed( id ) ? model[id] : throw new ArgumentOutOfRangeException( nameof( id ), $"Thing {id} is not in use." );

	/// <summary>The used things newest first, as the original walks its thing list (head 0x7CF56C, each slot's older link +8).</summary>
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
	/// slot, changes nothing.
	/// </summary>
	public void QueueDestroy( int id )
	{
		if ( !IsUsed( id ) || queued[id] )
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

	/// <summary>A copy of the used list newest first (what a save keeps of the pool).</summary>
	public ThingSlot[] Capture() => UsedNewestFirst().ToArray();

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

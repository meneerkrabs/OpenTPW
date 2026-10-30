namespace OpenTPW;

/// <summary>
/// The head space of one ride model and the slot table the ride's root script keeps for it (ADDHEAD / DELHEAD).
/// A head id is the <see cref="ModelDummyAttribute.Value"/> of a dummy attribute record whose type has flag 0x80; the
/// ids that run from 1 give the table length. Slot <c>n</c> holds the visitor on head id <c>n + 1</c>, 0 when free.
/// </summary>
// [BIN:STP-PPC:0x100B0E9C ADDHEAD] the table at script +48 (null: nothing), its length at +76, then a random slot drawn until one holds 0 (none free: nothing), the visitor stored there, and the head hung on head id slot + 1 (-1 from the lookup keeps the visitor without a head)
// [BIN:STP-PPC:0x100B0F90 DELHEAD] every slot holding the visitor is zeroed, and a head hung on it is taken down
// [BIN:STP-PPC:0x1019B298 head lookup] the 20-byte lookup records of the model's lookup state: a record matches head id k when its +4 word is k and its flags have 0x80
// [BIN:STP-PPC:0x1019B56C head hang] the matched record gets flag 0x2 and a sprite handle, and the lookup state counts one more attached head
// [BIN:STP-PPC:0x100B2BA0 script init] counts head ids from 1 until the lookup returns -1, allocates 4 bytes per id at script +0x30 and stores the count at +0x4C [0x100B32D4..0x100B3368]
// [BIN:TPW-EXE:0x00558D2B script init] only the root script gets a table; a child or sound script is created with thing id 0, which skips the lookup and the allocation, so its ADDHEAD and DELHEAD do nothing
// [BIN:STP-PPC:0x1019AE64 lookup construction] attribute record i belongs to node header+0x46 (Unknown46) + i; below the mesh count (+0x44) it is a mesh record, else a dummy record [0x1019AE64, 0x1019B7CC]
// [BIN:STP-PPC:0x1019B298 head id lookup] a head id is the FIRST attribute record with type flag 0x80 whose value equals the id; every shipped head record has type 0xb1/0xf1, so it has flag 0x30 and a world matrix
public sealed class RideHeadSpace
{
	/// <summary>Flag bit 0x80 of a dummy attribute type: a head record ([BIN:STP-PPC:0x1019B298]).</summary>
	public const uint HeadFlag = 0x80;

	private readonly int[] nodes;
	private readonly int[] visitors;
	// A park's head space draws its slot from the world generator (the delegate, the script world's shared stream, which the park saves); a head space made with a seed owns a seeded
	// .NET-compatible generator whose full state the park-resume snapshot stores (ScriptRandomTests proves its draws equal a seeded System.Random's): the test seam and standalone use.
	private readonly ScriptRandom? random;
	private readonly Func<uint>? worldDraw;

	/// <summary>Called with the visitor each time a head is hung on this table (<see cref="RideScriptWorld.HeadHung"/>); null where no world takes the draw.</summary>
	internal Action<int>? HeadHung { get; set; }

	private RideHeadSpace( int[] nodes, int seed )
	{
		this.nodes = nodes;
		visitors = new int[nodes.Length];
		random = new ScriptRandom( seed );
	}

	private RideHeadSpace( int[] nodes, Func<uint> worldDraw )
	{
		this.nodes = nodes;
		visitors = new int[nodes.Length];
		this.worldDraw = worldDraw;
	}

	/// <summary>A head space with the given node for each slot (test seam; <see cref="Create"/> reads the model).</summary>
	internal static RideHeadSpace Of( int[] nodes, int seed ) => new( (int[])nodes.Clone(), seed );

	/// <summary>A head space with the given node for each slot that draws its slots from the world generator.</summary>
	internal static RideHeadSpace Of( int[] nodes, Func<uint> worldDraw ) => new( (int[])nodes.Clone(), worldDraw );

	/// <summary>Length of the slot table (the run of head ids from 1).</summary>
	public int Count => visitors.Length;

	/// <summary>
	/// The head space of <paramref name="model"/>, or null when it has no head id 1 (the original's table is null then, so
	/// ADDHEAD does nothing). <paramref name="seed"/> drives the slot draw; the original draws from its global generator.
	/// </summary>
	// [BIN:TPW-EXE:0x00554CB5 ADDHEAD slot draw] the PC loop draws labs(world() >>> 1) % [+0x4C] from the global world generator 0x516330 until it finds a free slot (0x554CAF–0x554CCB)
	public static RideHeadSpace? Create( ModelFile model, int seed ) => Create( model, seed, null );

	/// <summary>The head space of <paramref name="model"/> whose slot draw is the world generator's (<paramref name="worldDraw"/>), as the original's ADDHEAD takes it.</summary>
	public static RideHeadSpace? Create( ModelFile model, Func<uint> worldDraw ) => Create( model, 0, worldDraw );

	private static RideHeadSpace? Create( ModelFile model, int seed, Func<uint>? worldDraw )
	{
		ArgumentNullException.ThrowIfNull( model );
		var nodes = new List<int>();
		while ( true )
		{
			var node = NodeForHead( model, nodes.Count + 1 );
			if ( node == NoRecord )
				break;
			nodes.Add( node );
		}
		return nodes.Count == 0 ? null : worldDraw != null ? new RideHeadSpace( nodes.ToArray(), worldDraw ) : new RideHeadSpace( nodes.ToArray(), seed );
	}

	/// <summary>The lookup found no record for a head id (the original's -1).</summary>
	public const int NoRecord = -2;

	/// <summary>
	/// The model node of head id <paramref name="id"/>: the node header+0x46 + i of the first attribute record i with flag
	/// 0x80 and value id. <see cref="NoRecord"/> when no record has the id; -1 when the record has no usable node (no
	/// 0x30 flag, so no matrix, or a node past the model), so the head is counted but not drawn.
	/// </summary>
	// [BIN:STP-PPC:0x1019B298 head id lookup] first record with flag 0x80 and value == id
	// [BIN:STP-PPC:0x1019AE64 lookup construction] record i belongs to node Unknown46 + i
	public static int NodeForHead( ModelFile model, int id )
	{
		for ( var index = 0; index < model.DummyAttributes.Count; index++ )
		{
			var attribute = model.DummyAttributes[index];
			if ( (attribute.Type & HeadFlag) == 0 || attribute.Value != (uint)id )
				continue;
			var node = model.Counts.Unknown46 + index;
			return node < model.Nodes.Count && (attribute.Type & 0x30) != 0 ? node : -1;
		}
		return NoRecord;
	}

	/// <summary>The visitor on slot <paramref name="slot"/>, 0 when free.</summary>
	public int VisitorAt( int slot ) => visitors[slot];

	/// <summary>The model node of slot <paramref name="slot"/>, -1 when the model names none.</summary>
	public int NodeAt( int slot ) => nodes[slot];

	/// <summary>Visitors with a head up and the model node each hangs from (a head without a node is left out).</summary>
	public IEnumerable<(int Visitor, int Node)> Hung()
	{
		for ( var slot = 0; slot < visitors.Length; slot++ )
		{
			if ( visitors[slot] != 0 && nodes[slot] >= 0 )
				yield return (visitors[slot], nodes[slot]);
		}
	}

	/// <summary>True when any slot holds a visitor.</summary>
	internal bool HasVisitors => Array.Exists( visitors, visitor => visitor != 0 );

	/// <summary>Appends <see cref="Hung"/> to a caller-owned list.</summary>
	internal void AppendHung( List<(int Visitor, int Node)> into )
	{
		for ( var slot = 0; slot < visitors.Length; slot++ )
			if ( visitors[slot] != 0 && nodes[slot] >= 0 )
				into.Add( (visitors[slot], nodes[slot]) );
	}

	/// <summary>ADDHEAD: a random free slot takes the visitor. Returns the slot, or -1 when the visitor is 0 or no slot is free.</summary>
	public int Add( int visitor )
	{
		if ( visitor == 0 || Array.IndexOf( visitors, 0 ) < 0 )
			return -1;
		int slot;
		do
		{
			slot = DrawSlot();
		} while ( visitors[slot] != 0 );
		visitors[slot] = visitor;
		// [BIN:TPW-EXE:0x00554D13 ADDHEAD hang] the head record of the slot's id exists (the table is built from them), so the routine 0x44B410 runs with the visitor
		HeadHung?.Invoke( visitor );
		return slot;
	}

	/// <summary>
	/// WALKON with the head action 4: the visitor's head hangs on head id <paramref name="id"/>, not on a drawn slot. Returns false when the model has no such id in its run from 1
	/// (the original's lookup then returns -1 and hangs nothing). A head already on that id is replaced.
	/// </summary>
	// [BIN:TPW-EXE:0x00557D80 WALKON walk state 1 -> 2] when the walk-on is over and the action is 4, the head record of head id u2 (lookup 0x0044B220 with class 0x80) gets the visitor's head (0x0044B410); WALKOFF takes it down again (0x0044B4C0, 0x005571A0)
	public bool Hang( int id, int visitor )
	{
		if ( visitor == 0 || id < 1 || id > visitors.Length )
			return false;
		visitors[id - 1] = visitor;
		// [BIN:TPW-EXE:0x00557EB9 WALKON hang] the lookup of the head id succeeded: the routine 0x44B410 runs with the visitor
		HeadHung?.Invoke( visitor );
		return true;
	}

	/// <summary>labs(worldRand() >>> 1) % length: one draw of the world generator (a seeded head space makes a signed 32-bit value from two draws of its own generator).</summary>
	private int DrawSlot()
	{
		if ( worldDraw != null )
			// [BIN:TPW-EXE:0x00554CB5 ADDHEAD slot draw] shr eax, 1; abs; idiv [+0x4C]; the result of the signed remainder indexes the table (the remainder of a non-negative value)
			return (int)((worldDraw() >> 1) % (uint)visitors.Length);
		var raw = unchecked( (int)(((uint)random!.Next( 1 << 16 ) << 16) | (uint)random.Next( 1 << 16 )) );
		return (int)(Math.Abs( (long)(raw >> 1) ) % visitors.Length);
	}

	/// <summary>Clears host-owned presentation slots when the ride releases its riders or is torn down.</summary>
	internal void Clear() => Array.Clear( visitors );

	/// <summary>DELHEAD: every slot holding the visitor is freed. Returns whether any was.</summary>
	public bool Remove( int visitor )
	{
		var removed = false;
		for ( var slot = 0; slot < visitors.Length; slot++ )
		{
			if ( visitors[slot] != visitor )
				continue;
			visitors[slot] = 0;
			removed = true;
		}
		return removed;
	}

	// ---- Park-resume snapshot ----

	/// <summary>Explicit state of the slot table and its draw generator (see <see cref="RideHeadSpaceSnapshot"/>).</summary>
	public RideHeadSpaceSnapshot CaptureSnapshot() => new()
	{
		Visitors = (int[])visitors.Clone(),
		RandomState = random?.CaptureState()
	};

	/// <summary>
	/// Restores into this head space: the slot table length must match (the model fixed it), the generator state is
	/// validated first. Throws <see cref="InvalidDataException"/> without changing anything on a violation.
	/// </summary>
	public void RestoreSnapshot( RideHeadSpaceSnapshot snapshot )
	{
		ArgumentNullException.ThrowIfNull( snapshot );
		snapshot.ValidateSnapshot();
		if ( snapshot.Visitors.Length != visitors.Length )
			throw new InvalidDataException( $"head space has {visitors.Length} slots, the snapshot stores {snapshot.Visitors.Length}." );
		Array.Copy( snapshot.Visitors, visitors, visitors.Length );
		// a snapshot of a world-generator head space holds no generator of its own; one of a seeded head space carries its state. An older snapshot's own generator is dropped
		// by a head space of the world generator (its slots now come from the shared stream, which the world restores).
		if ( snapshot.RandomState != null && random != null )
			random.RestoreState( snapshot.RandomState );
	}
}

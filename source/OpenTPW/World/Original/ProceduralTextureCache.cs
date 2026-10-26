namespace OpenTPW;

/// <summary>
/// The original's visible-cell cache of procedural terrain textures (option PROCEDURALTEXTURECACHE, docs/LND.md): a fixed number of texture slots (1500, 2000 or 2500) that the visible cells
/// of a frame are matched against, a miss taking the least recently used slot. Only the slot bookkeeping is here; building a cell's pixels (<see cref="ProceduralTerrainTexturer"/>) and holding
/// them (<see cref="ProceduralTerrainAtlas"/>) are the caller's. The structure is the original's: a 128 × 128 table from a cell key to its node and a ring of nodes, one per slot, that
/// moves a node when a cell hits and takes the node before the head when a cell misses.
/// </summary>
// [BIN:TPW-EXE:0x00572110 cache init] one node per slot linked as a ring (previous, next, the table entry that owns it, the texture), the head at the middle node, the key table cleared
// [BIN:TPW-EXE:0x00572440 cache lookup] per key: a miss moves the head to the node before it and takes that node (clearing the table entry of its previous owner); a hit moves the node to
// just before the node that was the head when the call began (the head follows when it was that node), so the cells of one frame are the newest nodes
public sealed class ProceduralTextureCache
{
	/// <summary>Columns and rows of the key table; a cell's key is <c>(z - y0) &lt;&lt; 8 | (x - x0)</c> and the table is indexed <c>row * 128 + column</c>.</summary>
	public const int TableSide = 128;

	/// <summary>The most visible cells the original records in one frame (0x56F090: 0x9C4); further cells are not drawn.</summary>
	public const int MaximumVisibleCells = 2500;

	private readonly int[] previous, next, owner;
	private readonly int[] table = new int[TableSide * TableSide];
	private int head;

	/// <summary>A cache of <paramref name="capacity"/> slots.</summary>
	public ProceduralTextureCache( int capacity )
	{
		if ( capacity < 1 )
			throw new ArgumentOutOfRangeException( nameof( capacity ), "A cache needs at least one slot." );
		Capacity = capacity;
		previous = new int[capacity];
		next = new int[capacity];
		owner = new int[capacity];
		Array.Fill( table, -1 );
		for ( var node = 0; node < capacity; node++ )
		{
			previous[node] = node == 0 ? capacity - 1 : node - 1;
			next[node] = node == capacity - 1 ? 0 : node + 1;
			owner[node] = -1;
		}
		head = capacity >> 1;
	}

	/// <summary>Texture slots, 0 up to <see cref="Capacity"/> - 1.</summary>
	public int Capacity { get; }

	/// <summary>Cache lookups that took a new slot (the cell's texture must be built into it) since the cache was made.</summary>
	public long Misses { get; private set; }

	/// <summary>Cache lookups that found their slot since the cache was made.</summary>
	public long Hits { get; private set; }

	/// <summary>The table index of a cell relative to the procedural rectangle's corner, or -1 when it does not fit the 128 × 128 table.</summary>
	public static int KeyOf( int cellX, int cellZ, int originX, int originZ )
	{
		var column = cellX - originX;
		var row = cellZ - originZ;
		return (uint)column < 0x100 && (uint)row < TableSide ? (row << 8) + column : -1;
	}

	/// <summary>
	/// The slot of each key of one frame's visible cells, in order. <paramref name="build"/> is set for a key that took a new slot (and, with <paramref name="dirty"/>, for a hit whose cell is
	/// dirty: the original rebuilds a hit when the recorded cell word has bit 0x100). A slot taken by an earlier key of the same call can be taken again when a frame lists more cells than the
	/// cache holds, as in the original.
	/// </summary>
	public void Resolve( ReadOnlySpan<int> keys, Span<int> slots, Span<bool> build, ReadOnlySpan<bool> dirty = default )
	{
		if ( slots.Length < keys.Length || build.Length < keys.Length )
			throw new ArgumentException( "The outputs are shorter than the keys." );
		var frameStart = head;
		for ( var index = 0; index < keys.Length; index++ )
		{
			var key = keys[index];
			var entry = ((key >> 8) << 7) + (key & 0xFF);
			var node = table[entry];
			if ( node < 0 )
			{
				var taken = previous[head];
				head = taken;
				if ( owner[taken] >= 0 )
					table[owner[taken]] = -1;
				table[entry] = taken;
				owner[taken] = entry;
				slots[index] = taken;
				build[index] = true;
				Misses++;
				continue;
			}
			slots[index] = node;
			build[index] = !dirty.IsEmpty && dirty[index];
			Hits++;
			if ( node == frameStart )
			{
				frameStart = next[frameStart];
				continue;
			}
			next[previous[node]] = next[node];
			previous[next[node]] = previous[node];
			next[previous[frameStart]] = node;
			previous[node] = previous[frameStart];
			next[node] = frameStart;
			previous[frameStart] = node;
			if ( frameStart == head )
				head = previous[head];
		}
	}

	/// <summary>The slots in ring order from the head: the order the next misses take them in, backwards (the head's previous node first).</summary>
	public int[] RingFromHead()
	{
		var order = new int[Capacity];
		var node = head;
		for ( var index = 0; index < order.Length; index++ )
		{
			order[index] = node;
			node = next[node];
		}
		return order;
	}

	/// <summary>The slot a key currently owns, or -1.</summary>
	public int SlotOf( int key ) => table[((key >> 8) << 7) + (key & 0xFF)];
}

namespace OpenTPW;

/// <summary>
/// The staff's share of the per-cell thing chains (WALK-003). The original keeps ONE chain per map cell for every thing on it: a thing that enters a cell goes to the chain's head,
/// whatever its kind, so guests and staff are interleaved by the time each entered. OpenTPW's guests keep their own per-cell lists (<see cref="GuestSimulation"/>) and the staff keep
/// theirs here; both take their entry numbers from the one counter, so reading the two lists by number, newest first, is the original's single chain.
/// </summary>
// [BIN:TPW-EXE:0x004D91F0 thing chain insert] a thing enters the chain of its cell at the head (cell +0x24 = the id; the thing's next link +0x0A, previous +0x08), the staff things (kinds 4 to 8) exactly as the guests (kind 1)
// [BIN:TPW-EXE:0x0050B6A0 thing move] a move to another cell unlinks the thing from the old chain (0x004D9280) and inserts it at the new head (0x004D91F0); a move inside the cell only rewrites the position words +4/+6; the destroy request (0x0050B780) queues it and the turn's deferred destroys unlink it (0x00516641 → 0x0050B980 → 0x004D9280)
public sealed class StaffCellChain
{
	/// <summary>One staff thing in a cell chain.</summary>
	public sealed class Entry
	{
		public int Id;
		public GuestNavigation Walk = null!;
		public (int X, int Y) Cell;
		/// <summary>The entry number the thing took when it entered the chain of <see cref="Cell"/> (the same counter as <see cref="Guest.CellSerial"/>).</summary>
		public long Serial;
	}

	private readonly Dictionary<int, Entry> byId = new();
	private readonly Dictionary<(int X, int Y), List<Entry>> cells = new();
	private readonly Func<long> nextSerial;
	private readonly List<int> stale = new();

	/// <param name="nextSerial">The owner's entry counter, one number per thing entering a cell chain.</param>
	public StaffCellChain( Func<long> nextSerial ) => this.nextSerial = nextSerial;

	/// <summary>The things of a staff chain, oldest entry first (the head is the last), or null for an empty cell.</summary>
	internal IReadOnlyList<Entry>? At( (int X, int Y) cell ) => cells.TryGetValue( cell, out var list ) && list.Count > 0 ? list : null;

	/// <summary>The entry of a staff member's thing, or null when it is in no chain.</summary>
	internal Entry? Of( int id ) => byId.GetValueOrDefault( id );

	/// <summary>The number of staff things in a chain.</summary>
	public int Count => byId.Count;

	/// <summary>
	/// Follows a member's thing: it enters the chain when it first has a walk, and goes to the head of another cell's chain when the walk's cell changed (the walk step and the teleport
	/// write the position, 0x004FA2A0 and 0x004FA930, and both end in 0x0050B6A0). A member without a walk (in the player's hand) keeps its place: the thing stays on the cell it was lifted from
	/// until the drop's teleport (0x00505EA0) writes the new position.
	/// </summary>
	/// <param name="serial">The entry number a restored thing already holds (a save), or 0 for the next number.</param>
	public void Follow( int id, GuestNavigation? walk, long serial = 0 )
	{
		if ( walk == null )
			return;
		var cell = walk.Cell;
		if ( !byId.TryGetValue( id, out var entry ) )
		{
			entry = new Entry { Id = id, Walk = walk, Cell = cell, Serial = serial != 0 ? serial : nextSerial() };
			byId[id] = entry;
			Insert( entry );
			return;
		}
		entry.Walk = walk;
		if ( entry.Cell == cell )
			return;
		Unlink( entry );
		entry.Cell = cell;
		entry.Serial = nextSerial();
		Insert( entry );
	}

	/// <summary>A thing leaves its chain (the destroy request, 0x0050B980).</summary>
	public void Remove( int id )
	{
		if ( !byId.Remove( id, out var entry ) )
			return;
		Unlink( entry );
	}

	/// <summary>Drops the things of members that no longer exist (a fired employee, a replaced economy).</summary>
	public void KeepOnly( ISet<int> ids )
	{
		stale.Clear();
		foreach ( var id in byId.Keys )
			if ( !ids.Contains( id ) )
				stale.Add( id );
		foreach ( var id in stale )
			Remove( id );
	}

	/// <summary>Empties every chain.</summary>
	public void Clear()
	{
		byId.Clear();
		cells.Clear();
	}

	private void Insert( Entry entry )
	{
		if ( !cells.TryGetValue( entry.Cell, out var list ) )
			cells[entry.Cell] = list = new List<Entry>();
		// the list is ordered by entry number, oldest first; a restored number can be older than the chain's newest
		var index = list.Count;
		while ( index > 0 && list[index - 1].Serial > entry.Serial )
			index--;
		list.Insert( index, entry );
	}

	private void Unlink( Entry entry )
	{
		if ( cells.TryGetValue( entry.Cell, out var list ) )
			list.Remove( entry );
	}
}

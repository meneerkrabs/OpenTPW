namespace OpenTPW;

/// <summary>
/// The rider tables of one script record of an original save's RSSE block, read against the ride's own script header and put on the script's clock: the walk table, the bounce table
/// (with the node base and the base height) and the head table (tp.exe loader <c>0x005597A0</c>). A record that does not read like the writer's shapes is refused with its reason and
/// none of its tables is kept. The tables name riders by guest id; <see cref="RideVisitorBridge.ImportSavedRiders"/> checks the names against the guests the import restored.
/// </summary>
// [BIN:TPW-EXE:0x005597A0 RSSE loader] the walk (+0x2C, header count +0x7C, 32 bytes each), bounce (+0x28, +0x64, 16 bytes each), head (+0x30, +0x4C words) and limbo (+0x24, +0x58, 8 bytes each) buffers are read raw over fresh allocations whose sizes come from the saved words, and nothing relinks or clears an entry
// [BIN:TPW-EXE:0x00402D70 game clock] every time in an entry is the game clock (0x00785970) at the opcode: the script clock of an imported script counts from the saved clock word (RideVM.ClockOriginMilliseconds), so a saved time is carried as its signed difference to that word
internal sealed class SavedRideTables
{
	/// <summary>One used walk entry: its table slot and the entry as the walk opcodes keep it (the times on the script's clock).</summary>
	internal sealed record WalkSlot( int Slot, int Guest, int Node1, int Node2, int Node3, int Node4, int Mode, int Extra, int State, int Heading, double Began, double Ends );

	/// <summary>One used bounce slot: the slot's index, the rider, the node it stored, and the deadline and start on the script's clock.</summary>
	internal sealed record BounceSlot( int Slot, int Guest, int Node, double Deadline, double Start );

	private SavedRideTables( IReadOnlyList<WalkSlot> walk, IReadOnlyList<BounceSlot> bounce, int bounceSlots, int bounceNodeBase, short bounceBaseHeight, IReadOnlyList<int> heads )
	{
		Walk = walk;
		Bounce = bounce;
		BounceSlots = bounceSlots;
		BounceNodeBase = bounceNodeBase;
		BounceBaseHeight = bounceBaseHeight;
		Heads = heads;
	}

	public IReadOnlyList<WalkSlot> Walk { get; }
	public IReadOnlyList<BounceSlot> Bounce { get; }
	/// <summary>The size of the bounce table (the header's bounce size).</summary>
	public int BounceSlots { get; }
	/// <summary>The node base BOUNCESETNODE left at script +0x70, as the raw word.</summary>
	public int BounceNodeBase { get; }
	/// <summary>The base height BOUNCESETBASE left at script +0x6E.</summary>
	public short BounceBaseHeight { get; }
	/// <summary>The rider on each head id's slot (slot n is head id n + 1), 0 when free.</summary>
	public IReadOnlyList<int> Heads { get; }

	/// <summary>Every guest the tables name, riders of the ride by the writer's shapes.</summary>
	public IEnumerable<int> Guests => Walk.Select( entry => entry.Guest ).Concat( Bounce.Select( entry => entry.Guest ) ).Concat( Heads.Where( visitor => visitor != 0 ) );

	/// <summary>
	/// Reads the tables of <paramref name="record"/> for a script whose header declares <paramref name="walkSize"/> walk and <paramref name="bounceSize"/> bounce entries and whose model has
	/// <paramref name="headSlots"/> head ids; <paramref name="clockWord"/> is the saved game clock. Null with the reason when the record does not read like the writer's.
	/// </summary>
	internal static SavedRideTables? Read( SaveRsseRecord record, int walkSize, int bounceSize, int headSlots, uint clockWord, out string? refusal )
	{
		ArgumentNullException.ThrowIfNull( record );
		refusal = null;
		int Relative( uint saved ) => unchecked( (int)(saved - clockWord) );
		if ( record.WalkSize != walkSize || record.Walk.Length != walkSize * SaveRsseRecord.WalkEntryBytes )
			return Refuse( $"the saved walk table holds {record.Walk.Length / SaveRsseRecord.WalkEntryBytes} entries (header word {record.WalkSize}), this install's header declares {walkSize}", out refusal );
		if ( record.BounceSize != bounceSize || record.Bounce.Length != bounceSize * SaveRsseRecord.BounceEntryBytes )
			return Refuse( $"the saved bounce table holds {record.Bounce.Length / SaveRsseRecord.BounceEntryBytes} entries (header word {record.BounceSize}), this install's header declares {bounceSize}", out refusal );
		if ( record.NodeCount != record.Nodes.Length / 4 || record.Nodes.Length / 4 != headSlots )
			return Refuse( $"the saved head table holds {record.Nodes.Length / 4} slots (count word {record.NodeCount}), this install's model has {headSlots} head ids", out refusal );

		var walk = new List<WalkSlot>();
		var entries = record.WalkEntries;
		for ( var slot = 0; slot < entries.Count; slot++ )
		{
			var entry = entries[slot];
			if ( entry.IsFree )
				continue;
			// [BIN:TPW-EXE:0x00556F40 WALKON states] a used entry is in state 1 (walking on), 2 (on board), 3 (walking off) or 4 (ready for WALKGET): the writers store nothing else (0x005570EB, the update 0x00557D80, WALKOFF, WALKGET)
			if ( entry.State is < 1 or > 4 )
				return Refuse( $"walk entry {slot} is in state {entry.State}", out refusal );
			if ( entry.Guest is 0 or > int.MaxValue )
				return Refuse( $"walk entry {slot} in state {entry.State} names the rider {entry.Guest}", out refusal );
			walk.Add( new WalkSlot( slot, (int)entry.Guest, (short)entry.Node1, (short)entry.Node2, (short)entry.Node3, (short)entry.Node4, (short)entry.Mode, (short)entry.Extra, entry.State, (short)entry.Heading,
				Relative( entry.Began ), Relative( entry.Ends ) ) );
		}

		var bounce = new List<BounceSlot>();
		var bounceEntries = record.BounceEntries;
			for ( var slot = 0; slot < bounceEntries.Count; slot++ )
		{
			var entry = bounceEntries[slot];
			if ( entry.IsFree )
				continue;
			// [BIN:TPW-EXE:0x00555674 BOUNCE] the handler takes the first free entry and tests neither a duplicate rider nor that the rider is aboard, so a saved table may hold one guest in two entries
			if ( entry.Guest > int.MaxValue )
				return Refuse( $"bounce entry {slot} names the rider {entry.Guest}", out refusal );
			bounce.Add( new BounceSlot( slot, (int)entry.Guest, unchecked( (int)entry.Node ), Relative( entry.Deadline ), Relative( entry.Start ) ) );
		}
		// [BIN:TPW-EXE:0x00555933 BOUNCING] the opcode returns the counter +0x6C that BOUNCE counts up (0x0055577C) and UNBOUNCE down (0x005558C6), not the entries in use
		if ( record.BounceCount != bounce.Count )
			return Refuse( $"the bounce counter is {record.BounceCount} with {bounce.Count} entries in use", out refusal );

		// [BIN:TPW-EXE:0x00554C80 ADDHEAD] the handler does not test that the rider has a head already, so a table may hold one rider twice
		var heads = new List<int>();
		var slots = record.HeadSlots;
		for ( var slot = 0; slot < slots.Count; slot++ )
		{
			if ( slots[slot] > int.MaxValue )
				return Refuse( $"head slot {slot} names the rider {slots[slot]}", out refusal );
			heads.Add( (int)slots[slot] );
		}
		return new SavedRideTables( walk, bounce, bounceSize, unchecked( (int)record.BounceNode ), record.BounceBase, heads );
	}

	private static SavedRideTables? Refuse( string reason, out string? refusal )
	{
		refusal = reason;
		return null;
	}
}

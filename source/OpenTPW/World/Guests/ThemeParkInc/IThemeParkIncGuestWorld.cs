namespace OpenTPW;

/// <summary>
/// An attraction thing as Theme Park Inc's idle guest reads it (docs/reverse/TPI-GUESTS.md, phase P2): its thing id, its thing cell (<c>+5</c>, <c>+7</c>), its entrance cell word (<c>+0x46</c>), its angle
/// (<c>+0x18</c>: 0, 90, 180 or 270), its type's <c>UsageInfo.EntryCellStandPosX/Y</c> (type <c>+0x184</c>, <c>+0x188</c>) and its script's variables (the live array <c>0x6B0C90</c> and <c>0x6B0780</c>
/// read and write; null for a thing no script runs, whose variables read 0 and drop every write).
/// </summary>
public readonly record struct ThemeParkIncThingView( ushort ThingId, (int X, int Y) Cell, ushort EntranceWord, int Angle, float StandX, float StandY, int[]? Variables )
{
	/// <summary>A script variable (<c>0x6B0740</c>, <c>0x6B0C90</c>): 0 without a script or for an index outside its variables.</summary>
	// [BIN:TPI-EXE:0x6B0C90 script variable] no subsystem, no instance or an index outside 0..+0x8C answers 0; else the instance's array (+0x1C) at the index
	public int Variable( int index ) => Variables is { } variables && (uint)index < (uint)variables.Length ? variables[index] : 0;

	/// <summary>Writes a script variable (<c>0x6B0780</c>): dropped without a script or for an index outside its variables.</summary>
	// [BIN:TPI-EXE:0x6B0780 script variable setter] nothing without the subsystem, without an instance or for an index outside 0..+0x8C; else the instance's array (+0x1C) takes the value
	public void SetVariable( int index, int value )
	{
		if ( Variables is { } variables && (uint)index < (uint)variables.Length )
			variables[index] = value;
	}
}

/// <summary>A placed Theme Park Inc entertainer as the guests' finder <c>0x619100</c> reads him: his thing id, his cell and whether his state word <c>+0x1B4</c> is 0x17 (performing).</summary>
public readonly record struct ThemeParkIncEntertainerView( ushort ThingId, (int X, int Y) Cell, bool Performing );

/// <summary>
/// An attraction thing as Theme Park Inc's ride choice and queues read it (phase P3a): its thing id, its thing cell (<c>+5</c>, <c>+7</c>, the cell whose thing chain the minor decision walks), the
/// ride bridge that holds its queue list, its open word and its admission (<see cref="IRideVisitorBridge"/>), its type's score words and whether the park's special-ride table names it (<c>0x6242F0</c>).
/// </summary>
public readonly record struct ThemeParkIncAttractionView( ushort ThingId, (int X, int Y) Cell, RideVisitorBridge Ride, ThemeParkIncScoreTerms Terms, bool Special );

/// <summary>
/// What Theme Park Inc's native guests read of the park outside the guest simulation (phase P2 of the guest states, docs/reverse/TPI-GUESTS.md): the object list's litter bins and shows, a thing by
/// its id, the entertainers' list and the customers of a bin. The level supplies it; a headless simulation without one finds no bin, no show and no entertainer.
/// </summary>
public interface IThemeParkIncGuestWorld
{
	/// <summary>The object list (world <c>+0x1EAB88</c>, newest first) filtered to the things with the flag <c>+0x42</c> bit 0x40 (<c>UsageInfo.HoldsLitter</c>): the litter bins.</summary>
	IReadOnlyList<ThemeParkIncThingView> Bins();

	/// <summary>The object list filtered to the things with the flag <c>+0x42</c> bit 0x80 (<c>UsageInfo.IsFireworks</c>, type <c>+0x1CC</c>): the shows.</summary>
	IReadOnlyList<ThemeParkIncThingView> Shows();

	/// <summary>The attraction thing with the id <paramref name="thingId"/>, or null when no attraction holds it (a bin state 9 walks to that has gone).</summary>
	ThemeParkIncThingView? Thing( ushort thingId );

	/// <summary>The placed entertainers in the entertainers' list order (world <c>+0x1EAB82</c> through <c>+0x5D0</c>, newest first).</summary>
	IReadOnlyList<ThemeParkIncEntertainerView> Entertainers();

	/// <summary>A guest used the thing (<c>0x5E5DA0</c>): its daily and monthly customers rise.</summary>
	void RecordCustomer( ushort thingId );

	/// <summary>
	/// The object list's choosable attractions (the thing flag <c>+0x42</c> bit 4, <c>Info.IsChoosable</c>) whose ride bridge the guests host, newest first, as the major decision <c>0x610080</c> walks them
	/// (phase P3a). None by default: a world without rides offers no choice.
	/// </summary>
	IReadOnlyList<ThemeParkIncAttractionView> Attractions() => [];
}

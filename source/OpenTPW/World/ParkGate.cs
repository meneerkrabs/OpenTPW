namespace OpenTPW;

/// <summary>
/// The park door of an original level: the entrance gate's script command, the open/close walk over
/// the park's attractions, and the deferred closure retry.
/// </summary>
/// <remarks>
/// <para>
/// The original's door routine is the Feral Mac function at <c>0x108ee4</c> (the maexah fork's PC
/// <c>FUN_00519ef0</c>): with the requested state in its first argument it writes the world's
/// <c>mParkClosed</c> (decoded from the Mac routine: the word at <c>world+0x1DA710</c>; the fork names PC
/// <c>+0x1da710</c>), then
/// commands the gate's script, then walks the park's thing chain and closes or opens every thing a
/// guest may be offered (thing <c>+0x2E</c> bit 4; the walk calls <c>0xde704</c> to close and
/// <c>0xde7e4</c> to open). [BIN:STP-PPC:0x108ee4 door routine: prologue, second-argument split, world field
/// load <c>addis 0x1E0000</c> + <c>-0x58F0</c>] Closing commands the gate only when the gate reports itself open and the
/// guest census <c>0xc3684</c> answers zero; opening always writes the open command.
/// </para>
/// <para>
/// The census counts guests (thing kind 1) standing on a cell of type 0, 1, 3, 9 or 10 (Mac
/// predicates <c>0x85190</c>–<c>0x85258</c>): empty park ground, path, queue, entrance and exit
/// cells — not the type-30 approach road outside the gate. OpenTPW applies these predicates to its reduced
/// imported guest grid, whose missing cell types remain qualified by ECON-050. The world sweep routine at Mac
/// <c>0x10536c</c> (PC <c>FUN_00516380</c>) increments <c>mGameTick</c> and retries a close every 30
/// park turns, additionally waiting for live staff (kinds 4–8) to be inside the park.
/// </para>
/// <para>
/// The gate itself is the level's <c>Gates</c> fixed item; its script declares <c>VAR_COMMAND</c>
/// (0 idles, 1 opens, 2 is the end-of-park sequence) and reports <c>VAR_STATUS</c> (1 = open).
/// [EXT:maexah/OpenTPW park-gate.md] led this port; the Mac addresses above were verified in
/// <c>SimThemePark.data</c>.
/// </para>
/// </remarks>
public sealed class ParkGate
{
	/// <summary>The gate script's command variable ([DATA:gates.RSE VAR_COMMAND]; written 1 to open, 0 to close).</summary>
	public const string CommandVariable = "VAR_COMMAND";

	/// <summary>The gate script's state variable ([DATA:gates.RSE VAR_STATUS]; 1 while the gate stands open).</summary>
	public const string StatusVariable = "VAR_STATUS";

	/// <summary>What <see cref="StatusVariable"/> reports while the gate stands open.</summary>
	public const int GateIsOpen = 1;

	/// <summary>The deferred-close retry period, in 248 ms park turns (Mac <c>0x105398</c>–<c>0x1053a0</c>: <c>mGameTick++</c>, retried at turn % 30).</summary>
	public const long RetryPeriodTurns = 30;

	private readonly ParkEconomy economy;
	private readonly Func<OriginalObjectRuntime?> gate;
	private readonly Func<IEnumerable<OriginalObjectRuntime>> attractions;
	private readonly GuestSimulation? guests;
	private readonly Func<IEnumerable<StaffMember>>? staff;
	private readonly Func<OriginalObjectRuntime, ParkObjectState?>? economyOf;
	private long lastObservedTurn;

	/// <param name="gate">The level's entrance gate runtime (its <c>Gates</c> fixed item), or null when the level has none.</param>
	/// <param name="attractions">The park's placed objects; those guests may be offered take the open/close walk.</param>
	/// <param name="guests">The park's visitors (the close census); null counts nobody.</param>
	/// <param name="staff">The park's live staff (the retry's outside-staff guard, kinds 4–8 of the original).</param>
	/// <param name="economyOf">The economy record of a placed object (its breakdown and pending upgrade feed <see cref="MayOpen"/>), or null.</param>
	public ParkGate( ParkEconomy economy, Func<OriginalObjectRuntime?> gate, Func<IEnumerable<OriginalObjectRuntime>> attractions, GuestSimulation? guests = null, Func<IEnumerable<StaffMember>>? staff = null,
		Func<OriginalObjectRuntime, ParkObjectState?>? economyOf = null )
	{
		this.economy = economy;
		this.gate = gate;
		this.attractions = attractions;
		this.guests = guests;
		this.staff = staff;
		this.economyOf = economyOf;
		lastObservedTurn = economy.Turn;
	}

	/// <summary>The gate of a running level: its <c>Gates</c> fixed item, every placed object and its separately owned prototype ride.</summary>
	/// <param name="prototype">Reads the current prototype runtime when the door moves, including a prototype placed after gate setup.</param>
	/// <param name="economyOf">The economy record of a placed object or the prototype ride, or null.</param>
	public static ParkGate ForLevel( ParkEconomy economy, ParkObjects objects, GuestSimulation? guests = null, Func<IEnumerable<StaffMember>>? staff = null, Func<OriginalObjectRuntime?>? prototype = null,
		Func<OriginalObjectRuntime, ParkObjectState?>? economyOf = null )
		=> new( economy,
			() => objects.Objects.FirstOrDefault( item => item.Entry.SettingsName.Equals( "Gates", StringComparison.OrdinalIgnoreCase ) )?.Runtime,
			() => WithPrototype( objects.Objects.Select( item => item.Runtime ), prototype?.Invoke() ),
			guests,
			staff,
			economyOf );

	private static IEnumerable<OriginalObjectRuntime> WithPrototype( IEnumerable<OriginalObjectRuntime> placed, OriginalObjectRuntime? prototype )
	{
		foreach ( var runtime in placed )
			yield return runtime;
		if ( prototype != null )
			yield return prototype;
	}

	/// <summary>The current attractions, with aliased runtimes visited once.</summary>
	internal IEnumerable<OriginalObjectRuntime> Attractions => attractions().Distinct().Where( item => item.IsAttraction );

	/// <summary>The entrance gate's runtime, or null when the level has none.</summary>
	public OriginalObjectRuntime? Gate => gate();

	/// <summary>The gate's open state as its own script reports it; true when there is no gate script (a gate without a script cannot report closed).</summary>
	public bool GateStatusIsOpen
	{
		get
		{
			var gate = Gate;
			if ( gate?.Script == null )
				return true;
			return gate.GetVariableByName( StatusVariable ) == GateIsOpen;
		}
	}

	/// <summary>
	/// Sets the gate's command once from the park's saved open state, the way the original's world setup
	/// records the gate beside <c>mParkClosed</c> (Mac <c>0x104628</c>–<c>0x104638</c>): an open park's gate
	/// stands open, a closed park's closed. A later <see cref="DoorMoved"/> runs the ordinary command path.
	/// </summary>
	public void InitializeFromParkState()
	{
		var gate = Gate;
		if ( gate?.Script == null )
			return;
		gate.SetVariable( CommandVariable, economy.IsParkOpen ? 1 : 0 );
	}

	/// <summary>
	/// The door moved: command the gate (Mac <c>0x108ee4</c>), then close or open every attraction the
	/// way the original's thing-chain walk does. Closing rides is unconditional; opening asks
	/// <see cref="MayOpen"/> first.
	/// </summary>
	public void DoorMoved( bool closed )
	{
		CommandGate( closed );

		foreach ( var item in Attractions )
		{
			if ( closed )
			{
				// [BIN:STP-PPC:0x1090b8 close walk] every visitable thing is closed through 0xde704
				if ( item.IsOpen )
					item.Close();
			}
			else if ( !item.IsOpen && MayOpen( item ) )
			{
				// [BIN:STP-PPC:0x108f64 open walk] opening asks the may-open guard first (PC FUN_004df290 at 0x0051a013)
				item.Open();
			}
		}
	}

	/// <summary>The gate's command of the end-of-park sequence ([DATA:gates.RSE VAR_COMMAND] 2).</summary>
	public const int EndOfParkCommand = 2;

	/// <summary>
	/// The park is lost (tp.exe <c>0x005168F0</c>, ECON-030): every attraction is asked to break down (its script's <c>VAR_BREAKSTAT</c> becomes 1, the request
	/// <c>0x004E0DB0</c>) and the gate gets the end-of-park command (<c>0x00519EF0</c> with its second argument set). Nothing is closed and the park-closed
	/// word keeps its value.
	/// </summary>
	// [BIN:TPW-EXE:0x005168F0 park lost] the thing walk calls 0x004E0DB0 (0x0055A0B0(script, 4, 1): variable 4, VAR_BREAKSTAT) for each live thing of type 3 with +0x32 bit 4, then 0x00519EF0(0, 1)
	// [BIN:TPW-EXE:0x00519EF0 door routine] with the second argument set it only writes 2 to the gate script's variable 0 (0x0055A0B0(script, 0, 2)) and returns
	public void ParkLost()
	{
		foreach ( var item in Attractions )
			if ( item.Script != null )
				item.SetVariable( nameof( RideVariables.VAR_BREAKSTAT ), 1 );
		if ( Gate is { Script: not null } gate )
			gate.SetVariable( CommandVariable, EndOfParkCommand );
	}

	/// <summary>
	/// The deferred closure retry (Mac <c>0x10536c</c> / PC <c>FUN_00516380</c>): every 30 park turns (7.44 s of scaled time),
	/// a closed park whose gate still stands open commands the gate shut once the guest census is zero
	/// and no live staff stands outside the counted cell types.
	/// </summary>
	public void Tick()
	{
		var turn = economy.Turn;
		var previous = lastObservedTurn;
		lastObservedTurn = turn;
		// Sample crossed multiples, rather than every fixed tick in a qualifying turn. This also handles
		// faster speeds, skipped updates and a restored clock moving backwards without duplicate retries.
		if ( turn > previous && turn / RetryPeriodTurns > previous / RetryPeriodTurns
			&& !economy.IsParkOpen && GateStatusIsOpen && GuestCensus == 0 && StaffOutside == 0 )
			CommandGate( closed: true );
	}

	/// <summary>
	/// Writes the gate's command variable: 1 to open (Mac <c>0x10923c</c>), and 0 to close only while the
	/// gate reports itself open and no guest stands inside (Mac <c>0xc3684</c> census, <c>0x1091b8</c> status).
	/// </summary>
	private void CommandGate( bool closed )
	{
		var gate = Gate;
		if ( gate?.Script == null )
			return;
		if ( !closed || (GateStatusIsOpen && GuestCensus == 0) )
			gate.SetVariable( CommandVariable, closed ? 0 : 1 );
		Log.Trace( $"Park door: gate {(closed ? "close" : "open")} command; guests inside {GuestCensus}, staff outside {StaffOutside}." );
	}

	/// <summary>
	/// Guests standing on the grid's counted cell types:
	/// type 0, 1, 3, 9 or 10 (Mac <c>0xc3684</c>); [EXT:pools-swim] a guest in a swim session (swimming in the water, climbing out, changing back) counts too, though the pool water is no counted type. Preserved approach-road and outside types do not count; a rider
	/// counts at their stored cell (not because they are riding), and a guest that has left is not a
	/// thing on the map at all.
	/// </summary>
	public int GuestCensus => guests == null ? 0 : CountCensus( guests );

	/// <summary>The guests of <paramref name="guests"/> standing on a counted cell (<see cref="GuestCensus"/>).</summary>
	// [BIN:TPW-EXE:0x004C9130 census] every thing of type 1 (a guest) on a cell the predicate 0x004FA990 accepts (types 1, 3, 9, 10 and 0) counts; 0x004CCEB0 (the rating) and 0x004C7FA0 (the cached count) walk the same way
	public static int CountCensus( GuestSimulation guests ) => guests.Guests.Count( guest => CountedInCensus( guests, guest ) );

	/// <summary>Whether a guest is one of the census (<see cref="CountCensus"/>): the guests the statistics of tp.exe <c>0x004C9130</c>, <c>0x004C9290</c>-<c>0x004C95D0</c> and <c>0x004C7BB0</c> all walk.</summary>
	// [BIN:TPW-EXE:0x004FA990 census predicate] a thing of type 1 (a guest) on a cell the predicate accepts; the same test in every guest statistic
	internal static bool CountedInCensus( GuestSimulation guests, Guest guest ) =>
		guest.State != GuestState.Gone && (guest.InSwimSession || CountsCellOf( guests.Grid.Cells, guest.Cell.X, guest.Cell.Y ));

	/// <summary>
	/// Live staff standing outside the counted cell types (the retry's blocker scan: thing kinds 4–8 with
	/// <c>+3 == 0</c>, Mac <c>0x10536c</c> staff arm). A staff member without a cell stands nowhere, so it
	/// does not block the close.
	/// </summary>
	public int StaffOutside => staff == null || guests == null
		? 0
		: staff().Count( member => member.CellX is { } x && member.CellY is { } y && !CountsCellOf( x, y ) );

	internal bool Counts( Guest guest ) => guest.InSwimSession || CountsCellOf( guests!.Grid.Cells, guest.Cell.X, guest.Cell.Y );

	/// <summary>Whether the cell at a guest's or staff member's position has a census-counted type.</summary>
	// [BIN:STP-PPC:0xc3684 cell predicates 0x85190–0x85258] types 0, 1, 3, 9 and 10 count; the grid holds the world init's and the placements' types (ParkCellMap.MapStartCell, WriteFootprint)
	private bool CountsCellOf( float cellX, float cellY ) => CountsCellOf( guests!.Grid.Cells, cellX, cellY );

	private static bool CountsCellOf( ParkCellMap cells, float cellX, float cellY )
	{
		if ( !float.IsFinite( cellX ) || !float.IsFinite( cellY ) || cellX < 0 || cellY < 0 || cellX >= cells.Width || cellY >= cells.Height )
			return false;
		return CountsCellType( cells.RawTypeAt( (int)MathF.Floor( cellX ), (int)MathF.Floor( cellY ) ) );
	}

	internal static bool CountsCellType( byte type ) => type is 0 or 1 or 3 or 9 or 10;

	/// <summary>
	/// Whether a closed attraction may be opened again: the original's <c>0xde654</c> (PC <c>FUN_004df290</c>), the guard of the door's opening
	/// walk and of the open buttons (ORACLE.md F27, ECON-049). It reads the ride's state: not broken down, condemned or upgrading, no maintenance
	/// flag, the back of its queue connected to a path (<see cref="QueueConnected"/>), and a coaster with a complete circuit.
	/// </summary>
	// [BIN:STP-PPC:0x100DE654 reopen guard] state 1, 4 and 2 (the script's VAR_BROKEN or the economy's breakdown; a purchased upgrade not yet installed), maintenance flag +96, back of the queue 0xdd744, coaster circuit 0x45eac; executed by the oracle target ride-may-open
	internal bool MayOpen( OriginalObjectRuntime item )
	{
		var record = economyOf?.Invoke( item );
		var state = item.GetVariable( RideVariables.VAR_BROKEN ) != 0 || record is { IsBrokenDown: true } ? RideObjectStates.BrokenDown
			: record is { } && record.PendingLevel > record.Level ? RideObjectStates.Upgrading : RideObjectStates.Idle;
		var coaster = item.Coaster != null;
		// [BIN:TPW-EXE:0x441970 coaster guard] the guard's coaster test is the guests' predicate: complete, no self-intersection (+0x140 == 0) and not the coaster the tool edits
		return RideOpening.MayOpen( state, maintenanceFlag: record?.MaintenanceRequested == true, queueConnected: guests == null || QueueConnected( guests.Grid, item.Visitors ), coaster ? RideOpening.CoasterTrackType : 0,
			coasterComplete: !coaster || item.Coaster!.AcceptsGuests );
	}

	/// <summary>
	/// The back-of-queue link test (tp.exe <c>0x004DE4A0</c>): a ride with <c>Info.HasQueue</c> is connected when the link byte of its
	/// back queue cell differs from its queue link, i.e. the back links to more than the cell before it (a path attached by the queue
	/// tool, <see cref="QueuePaths.Connect"/>).
	/// </summary>
	// [BIN:TPW-EXE:0x004DE4A0 queue link test] thing flag +0x32 bit 8 (Info.HasQueue, type record +0x40, set at 0x4DB425): the back cell (0x4DE130, the cached +0x3A) must exist and its +0x0C (0x522770) differ from its +0x0D (0x522850); without the flag the entrance cell's link toward the cell in front (by the rotation +0x10: 0 → 16, 90 → 4, 180 → 1, 270 → 64) and that type-1 cell's link back are both needed
	// [APPROX:ECON-049] typed, owned TPW no-queue entrances use reciprocal entrance/path links. Untyped or unmatched geometry, prototypes and TPI retain the permissive fallback, as do queue rides without real queue cells. Exit connectivity is still not represented by the object window; these checks never close an already-open ride.
	internal static bool QueueConnected( GuestPathGrid grid, RideVisitorBridge ride )
	{
		if ( !ride.HasQueue )
			return NoQueueEntranceConnected( grid, ride );
		if ( ride.QueueFrontCell is not { } front || !grid.IsQueue( front.X, front.Y ) )
			return true;
		var back = ride.QueueBackCell;
		return grid.Cells.LinksAt( back.X, back.Y ) != grid.Cells.QueueLinkAt( back.X, back.Y );
	}

	private static bool NoQueueEntranceConnected( GuestPathGrid grid, RideVisitorBridge ride )
	{
		// Restrict the test to the placed geometry we can identify; EntranceCell itself may be a
		// nearest-path fallback, so derive the actual stand cell from the outside cell and direction.
		if ( GameEditions.IsThemeParkInc || ride.OwnerWord == 0 || ride.QueueFrontCell is not { } outside
			|| ride.QueueEntranceDirection is < 0 or > 3 || !grid.InBounds( outside.X, outside.Y ) )
			return true;
		var toward = ride.Rotation switch { 0 => 16, 90 => 4, 180 => 1, 270 => 64, _ => 0 };
		if ( toward == 0 || GuestPathGrid.LinkValue( ride.QueueEntranceDirection ) != toward )
			return true;
		var delta = GuestPathGrid.Directions[ride.QueueEntranceDirection];
		var stand = (X: outside.X + delta.DX, Y: outside.Y + delta.DY);
		var cells = grid.Cells;
		if ( !cells.InBounds( stand.X, stand.Y ) || cells.RawTypeAt( stand.X, stand.Y ) != ParkCellTypes.Entrance
			|| cells.OwnerAt( stand.X, stand.Y ) != ride.OwnerWord )
			return true;
		return cells.RawTypeAt( outside.X, outside.Y ) == (byte)ParkCellType.Path
			&& (cells.LinksAt( outside.X, outside.Y ) & toward) != 0
			&& (cells.LinksAt( stand.X, stand.Y ) & PathLinkWriter.Opposite( (byte)toward )) != 0;
	}

	/// <summary>
	/// The gate's own-format state for a park save: the park turn whose retry crossing was last observed.
	/// The commanded open/close state itself lives in the gate script's variables (VM lane) and the park's
	/// open flag in the economy, so this record carries only the retry bookkeeping.
	/// </summary>
	public sealed record ParkGateSnapshot( long LastObservedTurn );

	/// <summary>The gate's retry bookkeeping for a park save.</summary>
	public ParkGateSnapshot CaptureSnapshot() => new( lastObservedTurn );

	/// <summary>Checks a snapshot's bounds without touching this gate: turns are non-negative.</summary>
	public void ValidateSnapshot( ParkGateSnapshot snapshot )
	{
		ArgumentNullException.ThrowIfNull( snapshot );
		if ( snapshot.LastObservedTurn < 0 )
			throw new InvalidDataException( "Park gate snapshot is inconsistent: negative last observed turn." );
	}

	/// <summary>
	/// Continues this gate (built on the restored economy) from a validated snapshot, so the deferred-close
	/// retry crosses the same turn multiples it would have without the restart. Restore after the economy's
	/// tick, so a saved turn behind the restored one only means the retry waits for the next crossing.
	/// </summary>
	public void RestoreSnapshot( ParkGateSnapshot snapshot )
	{
		ValidateSnapshot( snapshot );
		lastObservedTurn = snapshot.LastObservedTurn;
	}
}

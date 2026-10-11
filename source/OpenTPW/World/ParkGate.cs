namespace OpenTPW;

/// <summary>
/// The park door of an original level: the entrance gate's script command, the open/close walk over
/// the park's attractions, and the deferred closure retry.
/// </summary>
/// <remarks>
/// <para>
/// The original's door routine is the Feral Mac function at <c>0x108ee4</c> (the maexah fork's PC
/// <c>FUN_00519ef0</c>): with the requested state in its first argument it writes the world's
/// <c>mParkClosed</c> (Mac <c>world+0x1DA730</c>, the field the getter <c>0x1091ac</c> reads), then
/// commands the gate's script, then walks the park's thing chain and closes or opens every thing a
/// guest may be offered (thing <c>+0x2E</c> bit 4; the walk calls <c>0xde704</c> to close and
/// <c>0xde7e4</c> to open). Closing commands the gate only when the gate reports itself open and the
/// guest census <c>0xc3684</c> answers zero; opening always writes the open command.
/// </para>
/// <para>
/// The census counts guests (thing kind 1) standing on a cell of type 0, 1, 3, 9 or 10 (Mac
/// predicates <c>0x85190</c>–<c>0x85258</c>): empty park ground, path, queue, entrance and exit
/// cells — not the type-30 approach road outside the gate. The world sweep routine at Mac
/// <c>0x10536c</c> (PC <c>FUN_00516380</c>) increments <c>mGameTick</c> and retries a close every 30
/// ticks, additionally waiting for live staff (kinds 4–8) to be inside the park.
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

	/// <summary>The deferred-close retry period, in economy ticks (Mac <c>0x105398</c>–<c>0x1053a0</c>: <c>mGameTick++</c>, retried at tick % 30).</summary>
	public const long RetryPeriodTicks = 30;

	private readonly ParkEconomy economy;
	private readonly Func<OriginalObjectRuntime?> gate;
	private readonly Func<IEnumerable<OriginalObjectRuntime>> attractions;
	private readonly GuestSimulation? guests;
	private readonly Func<IEnumerable<StaffMember>>? staff;

	/// <param name="gate">The level's entrance gate runtime (its <c>Gates</c> fixed item), or null when the level has none.</param>
	/// <param name="attractions">The park's placed objects; those guests may be offered take the open/close walk.</param>
	/// <param name="guests">The park's visitors (the close census); null counts nobody.</param>
	/// <param name="staff">The park's live staff (the retry's outside-staff guard, kinds 4–8 of the original).</param>
	public ParkGate( ParkEconomy economy, Func<OriginalObjectRuntime?> gate, Func<IEnumerable<OriginalObjectRuntime>> attractions, GuestSimulation? guests = null, Func<IEnumerable<StaffMember>>? staff = null )
	{
		this.economy = economy;
		this.gate = gate;
		this.attractions = attractions;
		this.guests = guests;
		this.staff = staff;
	}

	/// <summary>The gate of a running level: its <c>Gates</c> fixed item and every placed object.</summary>
	public static ParkGate ForLevel( ParkEconomy economy, ParkObjects objects, GuestSimulation? guests = null, Func<IEnumerable<StaffMember>>? staff = null )
		=> new( economy,
			() => objects.Objects.FirstOrDefault( item => item.Entry.SettingsName.Equals( "Gates", StringComparison.OrdinalIgnoreCase ) )?.Runtime,
			() => objects.Objects.Select( item => item.Runtime ),
			guests,
			staff );

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

		foreach ( var item in attractions().Where( item => item.IsAttraction ) )
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

	/// <summary>
	/// The deferred closure retry (Mac <c>0x10536c</c> / PC <c>FUN_00516380</c>): every 30 economy ticks,
	/// a closed park whose gate still stands open commands the gate shut once the guest census is zero
	/// and no live staff stands outside the counted cell types.
	/// </summary>
	public void Tick()
	{
		if ( !economy.IsParkOpen && economy.Tick % RetryPeriodTicks == 0 && GateStatusIsOpen && GuestCensus == 0 && StaffOutside == 0 )
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
	/// Guests standing where the original's census counts them: on a cell of type 0, 1, 3, 9 or 10
	/// (Mac <c>0xc3684</c>). Guests on the approach road or outside the park do not count, a rider
	/// counts at their stored cell (not because they are riding), and a guest that has left is not a
	/// thing on the map at all.
	/// </summary>
	public int GuestCensus => guests?.Guests.Count( guest => guest.State != GuestState.Gone && Counts( guest ) ) ?? 0;

	/// <summary>
	/// Live staff standing outside the counted cell types (the retry's blocker scan: thing kinds 4–8 with
	/// <c>+3 == 0</c>, Mac <c>0x10536c</c> staff arm). A staff member without a cell stands nowhere, so it
	/// does not block the close.
	/// </summary>
	public int StaffOutside => staff == null || guests == null
		? 0
		: staff().Count( member => member.CellX is { } x && member.CellY is { } y && !CountsCellOf( x, y ) );

	internal bool Counts( Guest guest ) => CountsCellOf( guest.Cell.X, guest.Cell.Y );

	/// <summary>Whether the cell at a guest's or staff member's position has a census-counted type.</summary>
	// [BIN:STP-PPC:0xc3684 cell predicates 0x85190–0x85258] types 0, 1, 3, 9 and 10 count; OpenTPW only writes 0, 1 and 3 today.
	private bool CountsCellOf( float cellX, float cellY ) => CountsCellType( guests!.Grid.Cells.RawTypeAt( (int)cellX, (int)cellY ) );

	internal static bool CountsCellType( byte type ) => type is 0 or 1 or 3 or 9 or 10;

	/// <summary>
	/// Whether a closed attraction may be opened again — the original's <c>FUN_004df290</c>: not broken down,
	/// condemned or waiting for an upgrade, no mechanic called, and the back of its queue still connected to a path.
	/// </summary>
	// [APPROX:GATE-002] only the broken-down guard (VAR_BROKEN) is modelled; the upgrade/condemned states and the back-of-queue link test (PC FUN_004de4a0) are not — evidence needed: those ride states in the original
	private static bool MayOpen( OriginalObjectRuntime item ) => item.GetVariableByName( nameof( RideVariables.VAR_BROKEN ) ) == 0;
}

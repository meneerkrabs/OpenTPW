namespace OpenTPW;

public partial class Level
{
	public StaffWorldSimulation? StaffWorld { get; private set; }
	/// <summary>The hire and pick-up tools of the staff (docs/STAFF-PLACEMENT.md); null without a park economy.</summary>
	public StaffPlacement? StaffPlacement { get; private set; }
	/// <summary>[EXT:online-coop-commands] What the staff commands act on, with the employee each player holds in hand.</summary>
	public StaffHands? StaffHands { get; private set; }
	/// <summary>The patrol area tool of the employee view (tp.exe mouse mode 0x36); null without a park economy.</summary>
	public PatrolAreaTool? PatrolTool { get; private set; }
	/// <summary>Draws the placed staff and the tool's dangling sprite.</summary>
	public StaffRenderer? StaffRenderer { get; private set; }
	/// <summary>Theme Park Inc's staff in the park (null outside a Theme Park Inc park with its staff economy).</summary>
	public ThemeParkIncStaffWorld? TpiStaffWorld { get; private set; }
	/// <summary>The pool slot whose Theme Park Inc candidate the player is placing; -1 for none.</summary>
	public int TpiPlacingSlot { get; private set; } = -1;

	// kept beside its steps for the tests that call it by reflection (FrameBatchingTests, RestartParkSnapshotTests and others)
	private void SetupStaff()
	{
		foreach ( var _ in SetupStaffSteps() ) { }
	}

	/// <summary><see cref="SetupStaff"/> in steps: the staff's world and renderer; each value is a point where a frame may end (docs/BROWSER-PERFORMANCE.md, "Build steps").</summary>
	private IEnumerable<bool> SetupStaffSteps()
	{
		if ( Park == null || Guests == null ) yield break;
		var lane = Guests.Settings.ArrivalLaneA ?? Guests.Settings.ArrivalLaneB;
		StaffWorld = new StaffWorldSimulation( Guests.Grid, () => Park.Economy.Staff.Members, StaffJobAccess,
			lane is { Length: > 0 } ? lane[^1] : null );
		StaffWorld.Toilets = ToiletSites;
		BindStaffThoughtPresentation( StaffWorld );
		// the guards' pranksters and the entertainers' audience; a prank reaches the staff HQ's guard choice at once (StaffWorldSimulation.ReportPrank)
		StaffWorld.Guests = Guests;
		// the rest areas, the gates a striker walks out through, and the staff room's count of resting staff
		StaffWorld.RestAreas = RestSites;
		StaffWorld.GatesOpen = () => Gate?.GateStatusIsOpen == true;
		StaffWorld.RestingStaff = SetRestingStaff;
		// the mechanics' nearest-ride test measures to the ride's own cell (its thing's position)
		StaffWorld.RideCells = instance => economyInstances.FirstOrDefault( pair => pair.Value == instance && !pair.Key.IsDeleted ).Key is { } ride
			? (ride.Placement.X, ride.Placement.Y) : null;
		Guests.IsEscorted = StaffWorld.IsEscorting;
		// state 6 turns a guest toward a show object (fireworks and laser shows whose script variable 0 is 0) or a performing entertainer (tp.exe 0x501020, 0x500F00)
		Guests.ShowCells = () => Objects.Objects.Where( item => !item.IsDeleted && item.Entry.Settings.GetBool( "UsageInfo.IsFireworks" )
			&& item.Runtime.GetVariableByName( FireworksStatusVariable ) == 0 ).Reverse().Select( item => (item.Placement.X, item.Placement.Y) );
		Guests.StaffOnMapView = () => Park!.Economy.Staff.Members.Where( member => member.Navigation != null && member.Job is not (StaffJob.PickedUp or StaffJob.Resting) && member.State != StaffState.PickedUp )
			.OrderByDescending( member => member.Id ).Select( member => new GuestSimulation.StaffOnMap( member.Id, member.Type == StaffType.Entertainer,
				member.Job == StaffJob.Performing, member.Navigation!.Cell, member.Navigation ) );
		Guests.Pranked += ( guest, _ ) =>
		{
			if ( Park != null )
				StaffWorld?.ReportPrank( Park.Economy, guest.Id );
		};
		AttachStaffWorld();
		AttachAdvisorProbes();
		StaffHands = new StaffHands( () => Park?.Economy, () => Guests?.Grid.Cells, IsStaffDropBlocked, () => StaffWorld );
		StaffPlacement = new StaffPlacement( () => Park?.Economy, () => Guests?.Grid.Cells, IsStaffDropBlocked, command => Commands.Issue( command ), StaffHands.IsCarried, StaffHands.Of,
			() => Commands.Player, GuestStaffExit );
		PatrolTool = new PatrolAreaTool( () => Park?.Economy, IssuePatrolArea, () => Guests?.Grid.Cells );
		// the staff sprites, then their renderer's atlases, are two long steps
		NextStepIsLong = true;
		yield return true;
		if ( OriginalPark != null )
		{
			StaffSpriteSet sprites;
			using ( LoadProfile.Measure( "staff-sprites" ) )
				sprites = StaffSpriteSet.Load( LevelName, StaffSpriteRules.BankLimitOf( IGraphicsSettings.Instance ) );
			// the sources resolve a performance template against these banks (tp.exe 0x00475B80 reads the bank of the sprite's kind and variant) and the stored sprite draws take their loaded count (0x004F93AA)
			staffSpriteBanks = sprites;
			Park?.Economy.Staff.AttachSpriteBanks( sprites );
			NextStepIsLong = true;
			yield return true;
			StaffRenderer = new StaffRenderer( () => Park.Economy.Staff.Members, OriginalPark.Heightfield, sprites, StaffPlacement )
			{ FeetHeight = ( x, y ) => PoolBridges?.At( (int)x, (int)y )?.HeightAt( x, y ) };
		}
	}

	/// <summary>
	/// [EXT:online-coop-commands] A guest's exit from its staff tool (C4c-2, <see cref="StaffPlacement.Cancel"/>): the session's staff controller lets go, and the seat's state is cleaned up
	/// by its rule after any open request (the hire's cancel or the drop back, one request at a time). False where the tool sends them itself (a host, a single player, a session that ended).
	/// </summary>
	private bool GuestStaffExit()
	{
		if ( !Commands.SendsRequests || CoopSession?.GuestStaff is not { } staff )
			return false;
		staff.LetGo();
		return true;
	}

	/// <summary>
	/// [EXT:online-coop-commands] Where the patrol tool's area goes on a guest (C4c-2): the HUD's management queue, after the session's rights, so the employee window draws it as asked until
	/// it comes back. Null sends it to the park's commands, as a host's and a single player's.
	/// </summary>
	internal Func<SetPatrolAreaCommand, bool>? GuestPatrolArea { get; set; }

	/// <summary>The patrol tool's second click: its area through <see cref="GuestPatrolArea"/> on a guest, else the park's commands.</summary>
	private ParkCommandResult IssuePatrolArea( ParkCommand command ) => command is SetPatrolAreaCommand patrol && Commands.SendsRequests && GuestPatrolArea is { } guest
		? guest( patrol ) ? ParkCommandResult.Ok() : ParkCommandResult.Rejected( "The patrol area was not queued." )
		: Commands.Issue( command );

	/// <summary>[EXT:online-coop-permissions] The guest seats that hold staff state, in order (C4c): an employee in hand or a pending hire, which their release frees.</summary>
	internal IEnumerable<int> StaffHoldingGuestSeats() =>
		(StaffHands?.All.Select( hand => hand.Player ) ?? Enumerable.Empty<int>()).Concat( Park?.Economy.Staff.PendingHires.Keys ?? Enumerable.Empty<int>() )
			.Where( seat => seat > 0 ).Distinct().Order().ToArray();

	/// <summary>[EXT:online-coop-permissions] Whether seat <paramref name="seat"/> holds staff state (C4c): an employee in hand or a pending hire.</summary>
	internal bool HoldsStaff( int seat ) => StaffHands?.Of( seat ) != null || Park?.Economy.Staff.PendingHireOf( seat ) > 0;

	/// <summary>
	/// [EXT:online-coop-permissions] Frees what seat <paramref name="seat"/> holds of the staff (C4c: its release, <see cref="ReleaseSeatCommand"/>, and its staff right taken back): its pending
	/// hire's placement ends, its employee in hand drops back where it was picked up. True when it held either.
	/// </summary>
	internal bool ReleaseStaffOf( int seat )
	{
		var held = false;
		if ( Park?.Economy.Staff is { } staff && staff.PendingHireOf( seat ) > 0 )
		{
			staff.EndPlacement( seat, placed: false );
			held = true;
		}
		if ( StaffHands?.Of( seat ) != null )
		{
			StaffHands.ReturnToOrigin( seat );
			held = true;
		}
		return held;
	}

	/// <summary>
	/// [EXT:online-coop-permissions] Frees every guest seat's staff state outside the commands (C4c): a session's teardown, after the host's log took its last checkpoint, or the guest's own park
	/// leaving its session, as their carries go.
	/// </summary>
	internal void ClearGuestStaff()
	{
		foreach ( var seat in StaffHoldingGuestSeats() )
			ReleaseStaffOf( seat );
	}

	/// <summary>
	/// The cells a staff member may not be put on besides what the cell types refuse (<see cref="StaffPlacementRules.IsValidDropCell"/>): where the placed objects type their footprint cells
	/// (4, 9, 10), the types are the whole rule; a world that keeps the cells it was given (a read-only visit) refuses every cell under an object.
	/// </summary>
	// [BIN:TPW-EXE:0x0046CC40 drop cells] the drop reads the cell's type (0, 1, 3 or 9 pass, 0x536390, 0x536310, 0x536320) and its flags, never an object list: a ride's footprint cells are refused as the types 4 and 10 the placement writes (ParkCellMap.WriteFootprint, 0x00528A70), its queue-end entrance cell (9) takes a drop
	// [EXT:developer-prototype] the Sandbox Totem, pool slides' reserved land and pool bridges' reserved cells have no cell types
	private bool IsStaffDropBlocked( int x, int y ) =>
		TypesFootprints ? PoolBridges?.Reserves( x, y ) == true || IsReservedByPrototype( x, y ) || (Guests != null && PoolSlides?.ReservesLand( x, y, Guests.Grid.Cells ) == true) : IsPathBlocked( x, y );

	/// <summary>One park tool at a time: ends the path, queue, track, coaster, placing and remove tools before a staff tool starts.</summary>
	public void EndBuildTools()
	{
		// [EXT:online-coop-commands] a guest's queue edit or Move pick-up that waits for the host starts no tool after another tool's start (C3b)
		DropToolStartWait();
		PatrolTool?.Cancel();
		BuyLandTool?.Cancel();
		AreaClearTool?.Cancel();
		PoolTool?.Cancel();
		CellTool.Cancel();
		ExitCoasterTool();
		BuildEntry = null;
		IsPlacing = false;
		IsRemovingObjects = false;
		ResetBuildRotation();
		BuildToolsEnded?.Invoke();
	}

	/// <summary>Raised by <see cref="EndBuildTools"/> so the HUD drops the item it had armed.</summary>
	public event Action? BuildToolsEnded;

	/// <summary>The economy's staff worlds; a park load replaces the economy, so it runs again then.</summary>
	private void AttachStaffWorld()
	{
		if ( Park == null || StaffWorld == null )
			return;
		Park.Economy.MechanicJobWorld = StaffWorld;
		Park.Economy.StaffWorld = StaffWorld;
		// a load replaced the economy: its staff take the banks this level loaded, whose count reduces their stored sprite draws again
		Park.Economy.Staff.AttachSpriteBanks( staffSpriteBanks );
	}

	/// <summary>
	/// Theme Park Inc's staff in the park (a park whose economy holds Theme Park Inc's staff; Theme Park World's staff world is refused there with the staff capability): their world, the
	/// members' things in the pool and on the cell chains of the members a continuation restored.
	/// </summary>
	private void SetupThemeParkIncStaff()
	{
		if ( Park?.Economy.TpiStaff == null || Guests == null )
			return;
		TpiStaffWorld = new ThemeParkIncStaffWorld( Guests.Grid ) { Guests = Guests, ObjectCells = TpiObjectCells, ObjectThings = TpiObjectThing, ThemeIndex = ThemeParkIncStaffWorld.ThemeIndexOf( LevelName ) };
		AttachThemeParkIncRooms();
		AdoptTpiStaffThings();
		// the members' sprites: Theme Park Inc's esprites.wad holds the handymen's bank under the folder Theme Park World's loader reads (Generic\Handymen, Spr_ha), found by name
		if ( OriginalPark != null )
		{
			StaffSpriteSet sprites;
			using ( LoadProfile.Measure( "tpi-staff-sprites" ) )
				sprites = StaffSpriteSet.Load( LevelName, StaffSpriteRules.BankLimitOf( IGraphicsSettings.Instance ) );
			TpiStaffWorld.SpriteBanks = sprites;
			StaffRenderer = new StaffRenderer( () => Park?.Economy.TpiStaff?.Members ?? (IReadOnlyList<ThemeParkIncStaffMember>)Array.Empty<ThemeParkIncStaffMember>(),
				OriginalPark.Heightfield, sprites ) { FeetHeight = ( x, y ) => PoolBridges?.At( (int)x, (int)y )?.HeightAt( x, y ) };
		}
		// a continuation files its members with their saved entry numbers (Level.Resume, RestoreChain); the turn's sweep files any other placed member
	}

	/// <summary>
	/// The Theme Park Inc members' things in the level's pool: a placed member keeps the slot its save holds (taken now when the pool does not hold it yet).
	/// </summary>
	/// <exception cref="InvalidDataException">A member's thing id is held by a thing of another kind.</exception>
	private void AdoptTpiStaffThings()
	{
		if ( Park?.Economy.TpiStaff is not { } staff )
			return;
		foreach ( var member in staff.Members )
		{
			if ( member.Navigation == null )
				continue;
			var model = ThemeParkIncStaffWorld.ThingModelOf( member.Role );
			if ( Things.IsUsed( member.ThingId ) ? Things.ModelOf( member.ThingId ) != model : !Things.Occupy( member.ThingId, model ) )
				throw new InvalidDataException( $"Theme Park Inc staff member {member.Id}'s thing id {member.ThingId} is not a {model} slot of the thing pool." );
		}
	}

	/// <summary>
	/// Starts placing the Theme Park Inc candidate in pool slot <paramref name="slot"/> (the hire list's choice): every role goes to the hand until a drop or a cancel. Null when the hire starts,
	/// else the refusal's reason.
	/// </summary>
	// [BIN:TPI-EXE:0x630D10 hire] the slot is marked as being placed and the park maximum check runs; the placement tool 0x93B61C then holds the candidate (its enter 0x4D4C20 makes the dangling sprite)
	public string? BeginTpiHire( int slot )
	{
		if ( Park?.Economy.TpiStaff is not { } staff )
			return "unavailable";
		if ( slot < 0 || slot >= ThemeParkIncStaff.PoolSlots || staff.Slots[slot] is not { } candidate )
			return "no candidate";
		if ( !ThemeParkIncStaffMember.IsPlacedRole( candidate.Role ) )
			return "role not ported";
		if ( TpiStaffWorld == null )
			return "unavailable";
		CancelTpiHire();
		EndBuildTools();
		if ( !staff.BeginHire( slot ) )
			return "no candidate";
		TpiPlacingSlot = slot;
		return null;
	}

	/// <summary>Cancels the Theme Park Inc placement in progress: the candidate goes back to the pool.</summary>
	// [BIN:TPI-EXE:0x4D4E40 placement exit] a tool switch exits the placement tool with placed 0: 0x6319E0(1) clears the being-placed byte and keeps the candidate
	public void CancelTpiHire()
	{
		if ( TpiPlacingSlot < 0 )
			return;
		Park?.Economy.TpiStaff?.EndHire( TpiPlacingSlot, placed: false );
		TpiPlacingSlot = -1;
	}

	/// <summary>
	/// Ends the staff tools before a save: Theme Park World's placement (<see cref="StaffPlacement.PrepareForSave"/>) and a Theme Park Inc candidate in the hand, who goes back to the
	/// pool. The tools are not saved, so a saved being-placed byte would keep the candidate out of the pool after a load.
	/// </summary>
	// [BIN:TPI-EXE:0x6319E0 placing flags] the placement tool's exit with placed 0 (0x6319E0(1)) clears the being-placed bytes and keeps the candidates
	internal void PrepareStaffForSave()
	{
		StaffPlacement?.PrepareForSave();
		CancelTpiHire();
	}

	/// <summary>
	/// Drops the Theme Park Inc candidate being placed on cell (<paramref name="x"/>, <paramref name="y"/>): a valid cell, or the cell an object on it redirects to, makes the member there (its
	/// thing, its start and its first search); a refused one keeps the placement going. The member placed, or null when the drop refused it (<see cref="LastTpiDropSilent"/>: without a note).
	/// </summary>
	// [BIN:TPI-EXE:0x4D4EB0 staff drop] a refused cell prints "Cannot place staff member here" and the tool stays (ThemeParkIncStaffWorld.Drop); a valid one constructs the member at the drop's word
	// (ThemeParkIncStaffWorld.Construct), sets placed and
	// switches the tool, whose exit runs 0x6319E0(0): the candidate leaves the pool
	public ThemeParkIncStaffMember? DropTpiStaff( int x, int y )
	{
		if ( TpiPlacingSlot < 0 || Park?.Economy is not { TpiStaff: { } staff } economy || TpiStaffWorld is not { } world || Guests == null )
			return null;
		var drop = ThemeParkIncStaffWorld.Drop( Guests.Grid.Cells, x, y, TpiDropObjectAt( x, y ) );
		LastTpiDropSilent = drop.Silent;
		if ( drop.Cell is not { } cell )
			return null;
		var member = staff.EndHire( TpiPlacingSlot, placed: true );
		TpiPlacingSlot = -1;
		if ( member == null )
			return null;
		world.Construct( economy, member, cell, Things );
		return member;
	}

	/// <summary>Whether the last refused Theme Park Inc drop ended silently (an object's redirect found no cell), so the HUD shows no refusal note.</summary>
	public bool LastTpiDropSilent { get; private set; }

	/// <summary>
	/// The object whose thing the cell's chain holds, head first, for the drop's gate: the newest placed object whose thing cell (<see cref="OriginalObject.ThingCell"/>) is the cell.
	/// </summary>
	// [BIN:TPI-EXE:0x5DB1D5 object thing] the object's builder makes its thing through 0x637100 with its first argument, the cell its thing is filed under (kind 3), and a thing enters the head of
	// its cell's chain (0x5D9560 walks it from +0x28 by +0xA): the other cells of a footprint hold no kind-3 thing, so the gate sees an object only on its own cell
	private ThemeParkIncStaffWorld.DropObject? TpiDropObjectAt( int x, int y )
	{
		for ( var index = Objects.Objects.Count - 1; index >= 0; index-- )
		{
			var item = Objects.Objects[index];
			if ( item.IsDeleted || item.ThingCell != (x, y) )
				continue;
			var settings = item.Entry.Settings;
			// [DATA:<object>.sam:Info.WhichUIType] type record +0x70, copied into +0x600 (0x43D014); [DATA:<object>.sam:Info.MapOffsetX] +0x34, +0x38 (0 in every Theme Park Inc object)
			return new( settings.GetInt( "Info.WhichUIType" ), item.ThingCell, item.Placement.Rotation, item.Entry.Shape,
				settings.GetInt( "Info.MapOffsetX" ), settings.GetInt( "Info.MapOffsetY" ) );
		}
		return null;
	}

	/// <summary>
	/// An economy object's cells for the Theme Park Inc mechanic's search: its thing cell (the finder's distance) and its entrance cell (the thing's word +0x46, the search's walk target), from
	/// the placed object linked to the instance.
	/// </summary>
	// [BIN:TPI-EXE:0x5DB4C1 object cell words] +0x46 = +0x44 + the type's +0x5E8 turned by the angle (ThemeParkIncStaffWorld.EntranceWord, the drop's); a cell word of 0 or off the map has no cell
	private ((int X, int Y) Thing, (int X, int Y)? Entrance)? TpiObjectCells( int instance )
	{
		if ( TpiObjectThing( instance ) is not { } thing )
			return null;
		var word = ThemeParkIncStaffWorld.EntranceWord( thing );
		var entrance = ThemeParkIncStaffWorld.CellOfWord( word );
		return (thing.ThingCell, word != 0 && Guests != null && Guests.Grid.Cells.InBounds( entrance.X, entrance.Y ) ? entrance : null);
	}

	/// <summary>
	/// An economy object's thing for the Theme Park Inc gardener's chooser: its thing cell (the distance) and what the drop's fallback <c>0x5E9320</c> reads of it (its shape, angle and map offset),
	/// from the placed object linked to the instance; null for an object the level does not place.
	/// </summary>
	// [BIN:TPI-EXE:0x5D2F4C chooser fallback] the chooser hands the plant thing and the gardener's cell word to 0x5E9320, which reads the thing's +0x44 anchor, the type's shape and map offset and
	// the angle (ThemeParkIncStaffWorld.FallbackDropWord, the drop's)
	private ThemeParkIncStaffWorld.DropObject? TpiObjectThing( int instance )
	{
		if ( ObjectOfInstance( instance ) is not { IsDeleted: false } item )
			return null;
		var settings = item.Entry.Settings;
		return new ThemeParkIncStaffWorld.DropObject( settings.GetInt( "Info.WhichUIType" ), item.ThingCell, item.Placement.Rotation, item.Entry.Shape,
			settings.GetInt( "Info.MapOffsetX" ), settings.GetInt( "Info.MapOffsetY" ) );
	}

	/// <summary>
	/// A park built from a Theme Park Inc save takes the saved object list's order (<see cref="ParkEconomy.ThemeParkIncObjectList"/>): from the World variable <c>FirstObject</c> along each
	/// attraction thing's <c>mNext</c>, an object with an economy record by its economy instance and any other thing by its negated thing id. A continuation restores its own list.
	/// </summary>
	// [BIN:TPI-EXE:0x5DAAD0 ride finder] the finder walks the list from world +0x1EAB88 through each object's +0x14, the loader's FirstObject and mNext (0x64BC00, 0x5DC430); in the prebuilt Science
	// park the list holds 24 things, newest first, of which 17 have an economy record (their record order is not the list's order) and 7 have none: the obstacles, lake, gate and camera path of
	// Info.Id 1700, 1701, 1702, 1703, 1750 and 1798 (not in the economy catalogue) and the fixed item 1710 (a tool entry, not imported)
	private void SeedThemeParkIncObjectList()
	{
		if ( resumeState != null || Park?.Economy is not { TpiStaff: not null } economy || OriginalPark?.Save?.ThemeParkIncWorld is not { } world )
			return;
		var instances = new Dictionary<int, int>();
		foreach ( var (item, instance) in economyInstances )
			if ( Objects.SavedThemeParkIncThingOf( item ) is var thing and not 0 )
				instances[thing] = instance;
		var list = ThemeParkIncObjectListOf( world, instances );
		economy.RestoreThemeParkIncObjectList( list );
		// a thing of the list that no object of OpenTPW's is (the tool entry 1710 of the prebuilt) is still a thing of the original's park: its slot is held, so its id is not handed out again
		// while the list names it
		// [BIN:TPI-EXE:0x64D759 free list rebuild] after the thing list's ENOD the World loader rebuilds the free list from the slots no saved thing occupies (0xAA18C4 on, stride 20, the head
		// 0xAD39A4), as tp.exe's 0x518D43, so every saved thing keeps its slot
		// partial: Occupy puts the held slot at the newest place of the used list, not at the saved thing's place, and only the list's attraction things are held here (other saved things the
		// slice does not rebuild keep the new world's slots; docs/reverse/TPI-STAFF.md)
		var built = Objects.Objects.Select( item => item.ThingId ).ToHashSet();
		foreach ( var key in list )
		{
			if ( key > 0 || built.Contains( -key ) )
				continue;
			if ( Objects.Things is not { } pool || !pool.Occupy( -key, SaveThingModel.Attraction ) || !pool.Hold( -key ) )
				throw new InvalidDataException( $"The Theme Park Inc save's object list names thing {-key}, which no object rebuilds and whose slot of the thing pool cannot be held." );
		}
	}

	/// <summary>
	/// The saved object list of a Theme Park Inc World, newest first: the thing an imported object was built from as its economy instance (<paramref name="instances"/>, by saved thing id),
	/// any other thing as its negated thing id. Refused when the list does not read or an imported object's thing is not on it.
	/// </summary>
	internal static List<int> ThemeParkIncObjectListOf( ThemeParkIncSaveWorld world, IReadOnlyDictionary<int, int> instances )
	{
		var attractions = world.Attractions.ToDictionary( attraction => attraction.Id );
		var list = new List<int>();
		var seen = new HashSet<int>();
		for ( int thing = world.Variables.FirstObject; thing != 0; thing = attractions[thing].Next )
		{
			if ( !attractions.ContainsKey( thing ) || !seen.Add( thing ) )
				throw new InvalidDataException( $"The Theme Park Inc save's object list reaches thing {thing}, which is {(seen.Contains( thing ) ? "met twice" : "no attraction")}." );
			list.Add( instances.TryGetValue( thing, out var instance ) ? instance : -thing );
		}
		if ( instances.Keys.FirstOrDefault( thing => !seen.Contains( thing ) ) is var missing and not 0 )
			throw new InvalidDataException( $"The Theme Park Inc save's object list does not hold thing {missing}, which an imported object was built from." );
		return list;
	}

	/// <summary>The staff sprites this level loaded (null until it did, and in a level without an original park).</summary>
	private StaffSpriteSet? staffSpriteBanks;

	/// <summary>The staff's rest areas: objects with <c>UsageInfo.ChillsYouOut</c> and an economy instance, their cell and the path cell in front of them.</summary>
	// [DATA:<object>.sam:UsageInfo.ChillsYouOut] type record +0xFC, the object flag +0x32 bit 1 the rest finder 0x00506910 tests (the Staff Room of each theme)
	private IReadOnlyList<RestSite> RestSites()
	{
		var sites = new List<RestSite>();
		if ( Guests == null )
			return sites;
		foreach ( var item in Objects.Objects )
		{
			if ( item.IsDeleted || !item.Entry.Settings.GetBool( "UsageInfo.ChillsYouOut" ) || !economyInstances.TryGetValue( item, out var instance ) )
				continue;
			if ( AccessCell( item, Guests.Grid ) is { } access )
				sites.Add( new RestSite( instance, (item.Placement.X, item.Placement.Y), access ) );
		}
		return sites;
	}

	/// <summary>The staff room's <c>VAR_STAFFIN</c> (its script's only variable, index 0 in every theme): the members resting in it.</summary>
	// [BIN:TPW-EXE:0x00505FE0 rest area count] the arrival adds 1 to the rest area's script variable 0 (0x0055A0B0), the rest's end (0x00506D10) takes 1 away
	private void SetRestingStaff( IReadOnlyDictionary<int, int> counts )
	{
		foreach ( var (item, id) in economyInstances )
			if ( !item.IsDeleted && item.Entry.Settings.GetBool( "UsageInfo.ChillsYouOut" ) )
				item.Runtime.SetVariable( "VAR_STAFFIN", counts.GetValueOrDefault( id ) );
	}

	/// <summary>The toilets for the handymen: relief objects with an economy instance, their cell and the path cell in front of them.</summary>
	private IReadOnlyList<ToiletSite> ToiletSites()
	{
		var sites = new List<ToiletSite>();
		foreach ( var (item, instance) in economyInstances )
		{
			if ( item.IsDeleted || !item.Runtime.Visitors.ProvidesRelief || !item.Runtime.Visitors.HasCells )
				continue;
			var access = item.Runtime.Visitors.JoinCell ?? item.Runtime.Visitors.EntranceCell;
			// Approximation (ECON-022, registered at StaffWorldSimulation.UpdateStaff): a toilet's access cell (thing +0x36) is its queue's join cell or the path cell in front of its entrance, as for the mechanics' arrival
			if ( Guests!.Grid.IsWalkable( access.X, access.Y ) )
				sites.Add( new ToiletSite( instance, (item.Placement.X, item.Placement.Y), access ) );
		}
		return sites.OrderBy( site => site.Id ).ToList();
	}

	private (int X, int Y)? StaffJobAccess( int instance )
	{
		var item = economyInstances.FirstOrDefault( entry => entry.Value == instance ).Key;
		var bridge = item?.Visitors ?? (prototypeInstance == instance ? PlacedRide?.Visitors : null);
		if ( bridge == null || !bridge.HasCells ) return null;
		var cell = bridge.JoinCell ?? bridge.EntranceCell;
		return Guests!.Grid.IsWalkable( cell.X, cell.Y ) ? cell : null;
	}

	/// <summary>
	/// Actual staff actor position; false when there is no actor or no terrain. Read only: a member hired since the last tick has no position until the tick's
	/// <see cref="StaffWorldSimulation.Synchronize"/> puts it on the gate path (the HUD that asks must not move the park between two ticks).
	/// </summary>
	public bool TryGetStaffWorldPosition( int id, out Vector3 position )
	{
		position = default;
		if ( StaffWorld == null || OriginalPark == null ) return false;
		foreach ( var actor in StaffWorld.Snapshots )
		{
			if ( actor.Id != id ) continue;
			var field = OriginalPark.Heightfield;
			var x = (int)MathF.Floor( actor.CellX );
			var y = (int)MathF.Floor( actor.CellY );
			if ( x < 0 || y < 0 || x >= field.CellCountX || y >= field.CellCountZ ) return false;
			var center = OriginalParkPlacement.GetCellCenter( field, x, y );
			position = new Vector3( center.X + (actor.CellX - x - 0.5f) * field.CellSizeX * OriginalParkPlacement.ModelScale,
				center.Y + (actor.CellY - y - 0.5f) * field.CellSizeZ * OriginalParkPlacement.ModelScale, center.Z );
			return true;
		}
		return false;
	}
}

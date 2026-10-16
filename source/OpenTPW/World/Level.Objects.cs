namespace OpenTPW;

/// <summary>Original objects of the level: catalog, placed objects, build/remove tools.</summary>
public partial class Level
{
	/// <summary>Placed original objects (imported or built) with their build rules.</summary>
	public ParkObjects Objects { get; private set; } = null!;
	/// <summary>Catalog entry the build tool places, or null.</summary>
	private ObjectCatalogEntry? buildEntry;
	public ObjectCatalogEntry? BuildEntry
	{
		get => buildEntry;
		set
		{
			if ( value != null )
				StaffPlacement?.Cancel();
			buildEntry = value;
		}
	}
	/// <summary>Rotation in degrees (0/90/180/270) for the next built object.</summary>
	public int BuildRotation { get; private set; }
	/// <summary>Clicking removes the object under the cursor.</summary>
	private bool isRemovingObjects;
	public bool IsRemovingObjects
	{
		get => isRemovingObjects;
		set
		{
			if ( value )
				StaffPlacement?.Cancel();
			isRemovingObjects = value;
		}
	}

	/// <summary>Creates the park's objects; each value is a point where a frame may end, with the share of the objects placed.</summary>
	private IEnumerable<float> SetupObjects()
	{
		var theme = OriginalPark?.LevelName ?? "jungle";
		IParkGrid grid = OriginalPark == null ? new SandboxParkGrid() : new OriginalParkGrid( OriginalPark );
		var easy = Park?.Economy.Settings.IsEasy ?? false;
		ObjectCatalog? catalog = null;
		var archives = 0;
		foreach ( var loaded in ObjectCatalog.LoadSteps( theme, easy ) )
		{
			if ( loaded == null )
				yield return Math.Min( 0.25f, 0.004f * ++archives );
			else
				catalog = loaded;
		}
		// Objects keep off built path, queue and track cells; imported path cells are refused by the terrain rule as before. Object cells
		// (types 4, 9 and 10) are left to ParkObjects, which lets a higher overwrite priority replace their owner.
		// [BIN:TPW-EXE:0x535BB3 track cells] for an object cell (type 4) the validator refuses a second-layer track cell (0x53AD30: types 0xB, 0xD, 0x10, 0x12, 0x19; the extensions 0xC and 0x11 through their head, 0x535811) and any other map type but 0, 1 and the object types (0x535C4C..0x535D5D)
		Objects = new ParkObjects( catalog!, grid, Seed )
		{
			IsReserved = ( x, y ) => IsReservedByPrototype( x, y ) || Guests?.Grid.Cells.RawTypeAt( x, y ) is { } type
				&& type is not (0 or ParkCellTypes.Object or ParkCellTypes.Entrance or ParkCellTypes.Exit),
			IsUnowned = ( x, y ) => Guests?.Grid.Cells is { } cells && cells.InBounds( x, y ) && cells.FlagsAt( x, y ).HasFlag( ParkCellFlags.Unowned )
		};
		// [BIN:TPW-EXE:0x5590f7 demolish effect] the type's DestroyParticleEffect starts at the footprint before the script goes, for every removal but a park load or clear
		Objects.ObjectRemoving += item => item.Runtime.StartObjectEffect( ObjectParticleEffect.Destroy );
		Objects.ObjectRemoved += item =>
		{
			if ( ReferenceEquals( RideView?.Ride, item ) )
				ExitRideView();
		};
		if ( OriginalPark == null )
			yield break;
		yield return 0.3f;
		int count = 0;
		string? skipped = null;
		if (incLayout != null)
		{
			incLayoutCells = incLayout.RestoreInto(Objects, ParkResumeAssetManifest.Capture(Objects.Catalog, theme, false),
				OriginalPark.Map.CellCountX, OriginalPark.Map.CellCountY);
			count = Objects.Objects.Count;
		}
		else if ( resumeState != null )
			count = ParkLayoutSnapshot.RestoreObjects( resumeState.Layout, Objects ).Count;
		else if ( visitLayout != null )
		{
			// A version 2 visit places exactly the owner's objects, at their published anchors and open state.
			var (placed, missing) = SharedParkLayout.PlaceObjects( Objects, visitLayout.Objects );
			visitedObjects = placed;
			count = placed.Count( item => item != null );
			if ( missing.Count > 0 )
				skipped = string.Join( ", ", missing );
		}
		else
		{
			foreach ( var (placed, share) in OriginalPark.Save != null ? Objects.ImportOriginalSteps( OriginalPark.Save ) : Objects.AddDefaultFixedItemSteps() )
			{
				count = placed;
				yield return 0.3f + 0.7f * share;
			}
		}
		Log.Trace( $"Original {theme} objects: {count} placed with their original models and scripts ({Objects.Catalog.Entries.Count} catalog entries)"
			+ (skipped == null ? "" : $"; skipped (not in the local catalog): {skipped}") + "." );
	}

	/// <summary>Every placed object that guests may choose becomes a live attraction; new and removed objects follow.</summary>
	private void ConnectObjectsToGuests()
	{
		if ( Guests == null )
			return;
		Guests.Bins = LitterBins;
		Guests.LitterBinned += ( _, bin ) => TriggerBin( bin );
		// The placed objects type their footprint cells (4, 9, 10: ParkCellMap.WriteFootprint, tp.exe 0x00528A70); a visit, a Theme Park Inc layout and
		// Theme Park Inc itself (whose placement is not traced) keep the cells they were given
		var typesFootprints = !IsReadOnlyVisit && incLayoutCells == null && !GameEditions.IsThemeParkInc;
		if ( typesFootprints )
		{
			var cells = Guests.Grid.Cells;
			foreach ( var item in Objects.Objects )
				cells.WriteFootprint( item.Cells );
		}
		if ( typesFootprints )
		{
			Objects.ObjectPlaced += item => Guests?.Grid.Cells.WriteFootprint( item.Cells );
			Objects.ObjectRemoved += item => Guests?.Grid.Cells.ClearFootprint( item.Cells );
		}
		foreach ( var item in Objects.Objects )
			RegisterObjectWithGuests( item );
		Objects.ObjectPlaced += RegisterObjectWithGuests;
		Objects.ObjectRemoved += item => Guests?.Unregister( item.Visitors );
		// The arrival update sends the level's bus, seaplane or Ferry (tp.exe 0x004CF3E0); a Theme Park Inc world keeps the vehicle-less step-off.
		if ( !GameEditions.IsThemeParkInc && ArrivalVehicles.Available( Objects.Catalog ) )
		{
			Guests.ArrivalVehicles = new ArrivalVehicles( Objects );
		}
	}

	/// <summary>
	/// The queue's front cell is the cell outside the object's entrance (shape '2'); guests reappear outside its
	/// exit (shape 'S'/'N'/'E', else the entrance). Until a queue path is built there (and while that cell is
	/// not a walkable path), the nearest walkable cell serves as a one-cell queue.
	/// </summary>
	private void RegisterObjectWithGuests( OriginalObject item )
	{
		if ( Guests != null )
			RegisterWithGuests( item.Runtime, item.AccessPoints, Guests, item.Placement.Rotation );
	}

	/// <summary>
	/// The litter bins for the guests' bin search: placed objects with <c>UsageInfo.HoldsLitter</c> (thing <c>+0x32</c> bit 0x40) that have an
	/// economy instance, in placement order, each with the path cell guests walk to (its entrance's outside cell, else a path neighbour).
	/// </summary>
	// [DATA:<object>.sam:UsageInfo.HoldsLitter] type record +0xF8, which the object setup (tp.exe 0x004DB3FB) turns into flag +0x32 bit 0x40 (the Litter Bin of each theme)
	private IReadOnlyList<LitterBin> LitterBins()
	{
		var bins = new List<LitterBin>();
		if ( Guests == null )
			return bins;
		var grid = Guests.Grid;
		foreach ( var item in Objects.Objects )
		{
			if ( item.IsDeleted || !item.Entry.Settings.GetBool( "UsageInfo.HoldsLitter" ) || !economyInstances.TryGetValue( item, out var instance ) )
				continue;
			if ( AccessCell( item, grid ) is { } found )
				bins.Add( new LitterBin( instance, (item.Placement.X, item.Placement.Y), found ) );
		}
		return bins;
	}

	/// <summary>An object's access cell (thing <c>+0x36</c>) for bins and rest areas: its entrance's outside cell on a path, else its first path neighbour.</summary>
	// [APPROX:GUEST-005] a bin's or rest area's access cell (thing +0x36) is its entrance's outside cell or its first path neighbour in direction order; tp.exe's constructor 0x004DB090 stores the shape's first entrance cell itself (0x004DB33C, the shape cell 0x00413410 records at +0x494; no walkability test) — evidence needed: whether the original's route finder reaches a goal cell inside a footprint
	private static (int X, int Y)? AccessCell( OriginalObject item, GuestPathGrid grid )
	{
		var cell = (item.Placement.X, item.Placement.Y);
		foreach ( var point in item.AccessPoints )
			if ( grid.IsWalkable( point.OutsideX, point.OutsideY ) )
				return (point.OutsideX, point.OutsideY);
		for ( var direction = 0; direction < 4; direction++ )
		{
			var (dx, dy) = GuestPathGrid.Directions[direction];
			if ( grid.IsWalkable( cell.X + dx, cell.Y + dy ) )
				return (cell.X + dx, cell.Y + dy);
		}
		return null;
	}

	/// <summary>The bin's script variable a guest's litter sets (each theme's Litter Bin script declares only <c>VAR_TRIGGER</c>).</summary>
	public const string BinTriggerVariable = "VAR_TRIGGER";

	/// <summary>
	/// A guest threw its litter into the bin with economy instance <paramref name="instance"/>: the bin's script variable 0 becomes 1, and the
	/// script plays the bin's sound and animation.
	/// </summary>
	// [BIN:TPW-EXE:0x004FFF20 bin trigger] on arrival with a bin, state 9 looks up the bin thing's script (+0x24) through 0x0055A070 and sets its variable 0 to 1 through 0x0055A0B0 ("RSSE: Bad variable reference" when out of range)
	// [DATA:<theme> Litter Bin .rse] VAR_TRIGGER is the script's only variable (index 0) in all four themes
	private void TriggerBin( int instance )
	{
		foreach ( var (item, id) in economyInstances )
			if ( id == instance && !item.IsDeleted )
				item.Runtime.SetVariable( BinTriggerVariable, 1 );
	}

	/// <summary>
	/// Registers a placed object's visitor bridge with <paramref name="guests"/> the way the build flow does
	/// (also used by the headless M3 gate): attractions only, cells from <see cref="ResolveVisitorCells"/>.
	/// </summary>
	internal static bool RegisterWithGuests( OriginalObjectRuntime runtime, IEnumerable<ObjectAccessPoint> accessPoints, GuestSimulation guests, int rotation = 0 )
	{
		if ( !runtime.IsAttraction || ResolveVisitorCells( accessPoints, guests.Grid ) is not { } cells )
			return false;
		runtime.Visitors.EntranceCell = cells.Entrance;
		runtime.Visitors.ExitCell = cells.Exit;
		runtime.Visitors.QueueFrontCell = cells.QueueFront;
		runtime.Visitors.QueueEntranceDirection = cells.QueueEntranceDirection;
		runtime.Visitors.Rotation = rotation;
		runtime.Visitors.HasCells = true;
		guests.Register( runtime.Visitors );
		return true;
	}

	/// <summary>
	/// Walkable entrance and exit cells for an object's access points, plus the entrance's outside cell (the queue's
	/// front cell) and the direction from it into the entrance; null when it has no entrance or the grid no path.
	/// </summary>
	internal static ((int X, int Y) Entrance, (int X, int Y) Exit, (int X, int Y) QueueFront, int QueueEntranceDirection)? ResolveVisitorCells( IEnumerable<ObjectAccessPoint> accessPoints, GuestPathGrid grid )
	{
		var points = accessPoints.ToArray();
		var entrance = points.FirstOrDefault( point => point.Kind == ObjectCellKind.Entrance );
		if ( entrance == default )
			return null;
		var exit = points.FirstOrDefault( point => point.Kind == ObjectCellKind.Exit );
		if ( exit == default )
			exit = entrance;
		var entranceCell = WalkableNear( grid, entrance.OutsideX, entrance.OutsideY );
		var exitCell = WalkableNear( grid, exit.OutsideX, exit.OutsideY );
		if ( entranceCell == null || exitCell == null )
			return null;
		return (entranceCell.Value, exitCell.Value, (entrance.OutsideX, entrance.OutsideY),
			GuestPathGrid.DirectionBetween( entrance.OutsideX, entrance.OutsideY, entrance.X, entrance.Y ));
	}

	/// <summary>The headless path builder over the level's cell map (original levels; null in the generic sandbox).</summary>
	public ParkPathBuilder? Paths { get; private set; }
	/// <summary>The path tiles' texture rolls (presentation; <see cref="PathTileVariants"/>).</summary>
	public PathTileVariants PathVariants { get; } = new();

	/// <summary>
	/// The tile refresh of a cell (tp.exe <c>0x5365D0</c>): its chooser runs, and with it the texture roll, for a path or queue cell; other
	/// cells (type 0 here) skip the chooser.
	/// </summary>
	internal void RefreshTile( int x, int y )
	{
		if ( Guests?.Grid.Cells is { } cells && cells.TypeAt( x, y ) is ParkCellType.Path or ParkCellType.Queue )
			PathVariants.Refresh( x, y );
	}

	/// <summary>The area clear tool (the bulldozer: Clear Land and the Clear key); null without paths or on a read-only visit.</summary>
	public AreaClearTool? AreaClearTool { get; private set; }
	/// <summary>The path and queue build tool the park view drives (PATH-plan §9.4).</summary>
	public CellBuildTool CellTool { get; } = new();

	/// <summary>The attraction whose queue the build tool lays (set after building a ride with <c>Info.HasQueue</c>).</summary>
	public RideVisitorBridge? QueueToolRide
	{
		get => CellTool.Writer is QueueLineWriter queue ? queue.Ride : null;
		set
		{
			if ( value == null )
			{
				if ( CellTool.Mode == CellToolMode.Queue )
					CellTool.Cancel();
			}
			else
				EnterQueueTool( value );
		}
	}

	private void SetupPaths()
	{
		if ( Guests == null )
			return;
		Paths = new ParkPathBuilder( Guests.Grid.Cells, Guests.Grid, () => Park?.Economy, Objects.Grid, IsPathBlocked )
		{
			// [BIN:TPW-EXE:0x00534A91 queue neighbour sound] the writer plays cat_ui 139 only while game+36 (0x785A2C, the park's money) is clear: here, while a park economy runs
			Effects = new LinkEffects( RefreshTile, ( x, y ) =>
			{
				if ( Park != null )
					GameAudio.PlayUiAt( GameAudio.QueueJoinEvent, GameAudio.QueueNeighbourSoundPosition( x, y ) );
			} )
		};
		AreaClearTool = IsReadOnlyVisit ? null : new AreaClearTool( ( x, y ) => Objects.FindAt( x, y ), RemoveObjectAt, () => Guests?.Grid.Cells, RemovePathCell );
	}

	// Objects, fixed items (the gate and entrance included) and the sandbox Totem block paths like their original cell types do.
	private bool IsPathBlocked( int x, int y ) => Objects.FindAt( x, y ) != null || IsReservedByPrototype( x, y );

	/// <summary>
	/// A click in the park view on an empty owned cell or a path cell enters the path tool without a start, then the same click is the
	/// tool's first click (<see cref="CellBuildTool.Click"/>): a refused cell ends the tool again (the refused ghost is returned), an
	/// accepted one becomes the start. False when no path tool can run (a read-only visit, no builder).
	/// </summary>
	// [BIN:STP-PPC:0x10139C64 park-view hover] empty cell: hover state 2, help 441 "build path"; path (type 1): state 1, help 442 "extend this path"; queue (type 3): help 444
	// [BIN:TPW-EXE:0x004879D0 park click] hover states 1 and 2 build the tool object 0x46C580(1), whose enter 0x46D5A0 calls SetMode(1) (0x52F200: the start −1); the click handler 0x524960 then takes the click as the tool's first click (CellBuildTool.ClickWithoutStart); OpenTPW's HUD hands it the click it reports, the press, as it does every tool click
	public bool EnterPathTool( int x, int y ) => EnterPathTool( x, y, out _ );

	/// <summary><see cref="EnterPathTool(int, int)"/>, with the refused ghost when the click ended the tool again.</summary>
	public bool EnterPathTool( int x, int y, out SegmentResult? refused )
	{
		refused = null;
		if ( IsReadOnlyVisit || Paths == null )
			return false;
		StaffPlacement?.Cancel();
		CellTool.Enter( Paths, null );
		CellTool.Hover( (x, y) );
		refused = CellTool.Click( (x, y) );
		if ( refused != null )
			LastActionMessage = $"Cannot build a path here: {refused.StoppedBy}.";
		return true;
	}

	/// <summary>Starts the queue tool for <paramref name="ride"/> at its entrance's outside cell (or the queue's back cell).</summary>
	// [BIN:STP-PPC:0x1007497C ride placement] a placed HasQueue ride enters tool mode 3, the queue tool (PATH-plan §8); it shares LayLine, the vertex stack and the preview/commit code with the path tool
	public bool EnterQueueTool( RideVisitorBridge ride )
	{
		if ( IsReadOnlyVisit || Guests == null || ride.QueueFrontCell is not { } front )
			return false;
		StaffPlacement?.Cancel();
		ride.RecomputeQueue( Guests.Grid );
		var start = Guests.Grid.IsQueue( front.X, front.Y ) ? ride.QueueBackCell : front;
		CellTool.Enter( new QueueLineWriter( Guests.Grid, ride, ( x, y, pending ) => CheckQueueCell( ride, x, y, pending ),
			( x, y ) => BuildQueueCell( ride, x, y ), RemoveQueueCell, () => Park?.Economy.CellCost( CellPurchase.Queue ) ?? 0 ), start );
		return true;
	}

	/// <summary>
	/// Starts the queue tool on the queue that holds (x, y): the original's hover for a queue cell reads
	/// "Left-click to edit this queue" (help 444), and the tool then starts at that queue's back cell
	/// (<see cref="EnterQueueTool"/>). False when the cell is not a queue cell of a HasQueue ride.
	/// </summary>
	// [DATA:UIHELPTEXT 444 "Left-click to edit this queue", 445 "Left-click to build the queue, click again to stop building / Click onto path to connect the queue to it"] [BIN:STP-PPC:0x139c64 park-view hover] queue cell → hover state 3 (PATH-plan §4.1)
	// [APPROX:QUEUE-021] a left click on a queue cell starts the queue tool at once at the ride's back cell (EnterQueueTool); tp.exe's park-view click 0x004879D0 (hover state 3: a type-3 cell with an owning ride) builds tool 0x14, whose enter 0x0046D5A0 moves the cursor to the clicked cell and calls SetMode(0x14, 1) (0x0046D5F6); the next cell click in 0x00524960 (mode 0x14, 0x005260F7) walks the queue from the entrance into the vertex list (0x00530120), runs the queue edit 0x004DE1F0 and enters mode 3 (0x0052F580) — evidence needed: where mode 3 goes on laying after 0x00530120's list
	public bool EditQueueAt( int x, int y )
	{
		if ( IsReadOnlyVisit || Guests == null || QueueRideAt( Guests.Grid, Guests.Attractions, x, y ) is not { } ride )
			return false;
		return EnterQueueTool( ride );
	}

	/// <summary>The HasQueue ride whose queue holds (x, y) (the owner of a queue cell), or null.</summary>
	internal static RideVisitorBridge? QueueRideAt( GuestPathGrid grid, IEnumerable<IRideVisitorBridge> attractions, int x, int y )
	{
		if ( !grid.IsQueue( x, y ) )
			return null;
		return attractions.OfType<RideVisitorBridge>().FirstOrDefault( item => item.HasQueue && item.QueueCells.Contains( (x, y) ) );
	}

	/// <summary>
	/// Lays the snapped straight path from <paramref name="from"/> toward <paramref name="to"/> (headless API; the
	/// path tool commits through the same builder). Read-only visits and the generic sandbox lay nothing.
	/// </summary>
	public SegmentResult? BuildPath( (int X, int Y) from, (int X, int Y) to )
	{
		if ( IsReadOnlyVisit || Paths == null )
		{
			LastActionMessage = "Read-only visit: path changes are disabled.";
			return null;
		}
		var result = Paths.BuildSegment( from, to );
		LastActionMessage = result.Completed
			? $"Built {result.Built.Count} path cells for ${result.Charged}."
			: $"Cannot build a path here: {result.StoppedBy}.";
		return result;
	}

	/// <summary>Where a cell's sounds play: the original submits them at (x*10, 0, z*10), a cell is ten units at height 0.</summary>
	internal Vector3 CellSoundPosition( int x, int y ) => GameAudio.CellSoundPosition( x, y );

	/// <summary>
	/// Backspace in the park view with no build tool on (<see cref="GameAudio.BacktrackEvent"/>): over a path cell it plays the
	/// backtrack sound, then takes one placement off that cell, which goes only when it was laid once; the sound plays even
	/// when the cell stays (a fixed or a re-laid cell). False when the cell under the cursor is not a path cell, so the key
	/// does nothing there.
	/// </summary>
	// [BIN:TPW-EXE:0x40BE8B backtrack click] the key handler clicks the cell in mode 0x32, whose one-cell LayLine (0x536100) calls SetCell(0x32, 0, 1) and so ClearCell 0x5367A0 with (0, 1): the counted removal, not the forced one
	public bool BacktrackAt( int x, int y )
	{
		if ( IsReadOnlyVisit || Paths == null || Paths.Cells.TypeAt( x, y ) != ParkCellType.Path )
			return false;
		GameAudio.PlayUiAt( GameAudio.BacktrackEvent, CellSoundPosition( x, y ) );
		if ( Paths.RemoveOnce( x, y ) == CellBuildResult.Ok )
			LastActionMessage = "Removed a path cell.";
		return true;
	}

	/// <summary>
	/// Before a queue tool click: the cell where the queue line will end, when it lies on a path cell and the park's money is
	/// on (<see cref="GameAudio.QueueJoinEvent"/> then plays there after the click). The tool's preview for the cursor is the
	/// line the click commits; the ending click (on the start) previews the start, which repeats the last commit's test.
	/// Read before the commit, since the queue may be written over that cell.
	/// </summary>
	public (int X, int Y)? QueueJoinCell( CellBuildTool tool ) => Paths == null ? null : QueueJoinCell( tool, Paths.Cells, moneyOn: Park != null );

	/// <summary><see cref="QueueJoinCell(CellBuildTool)"/> over a cell map; <paramref name="moneyOn"/> is the original's money flag (here: a park economy runs).</summary>
	internal static (int X, int Y)? QueueJoinCell( CellBuildTool tool, ParkCellMap cells, bool moneyOn )
	{
		if ( tool.Mode != CellToolMode.Queue || !moneyOn || tool.Ghost?.SnappedEnd is not { } end )
			return null;
		return cells.TypeAt( end.X, end.Y ) == ParkCellType.Path ? end : null;
	}

	/// <summary>Plays <see cref="GameAudio.QueueJoinEvent"/> at the cell <see cref="QueueJoinCell"/> found before the click.</summary>
	public void PlayQueueJoin( (int X, int Y)? cell )
	{
		if ( cell is { } end )
			GameAudio.PlayUiAt( GameAudio.QueueJoinEvent, CellSoundPosition( end.X, end.Y ) );
	}

	/// <summary>Removes the path cell at (x, y) (forced, no refund; fixed InitialPath cells stay).</summary>
	public CellBuildResult RemovePathCell( int x, int y )
	{
		if ( IsReadOnlyVisit || Paths == null )
			return CellBuildResult.NotPath;
		var result = Paths.Remove( x, y );
		if ( result == CellBuildResult.Ok )
			LastActionMessage = "Removed a path cell.";
		return result;
	}

	/// <summary>
	/// Adds one queue cell for an attraction (headless API; the queue tool calls it per click). The park
	/// economy charges <c>Costs.QueueCell</c>; read-only visits and object/terrain-blocked cells are refused.
	/// </summary>
	public QueueBuildResult BuildQueueCell( RideVisitorBridge ride, int x, int y )
	{
		if ( IsReadOnlyVisit )
		{
			LastActionMessage = "Read-only visit: queue changes are disabled.";
			return QueueBuildResult.Refused;
		}
		if ( Guests == null )
			return QueueBuildResult.Refused;
		var result = BuildQueueCell( Guests.Grid, Park?.Economy, ride, x, y, IsQueueBlocked, out var message );
		LastActionMessage = message;
		// The queue line's refresh pass (0x81) runs the chooser for the laid cell.
		if ( result == QueueBuildResult.Ok )
			RefreshTile( x, y );
		return result;
	}

	/// <summary>
	/// The queue tool's per-cell build (also used by the headless M3 gate): recompute the queue,
	/// <c>CheckQueueCell</c> (static), then <c>Costs.QueueCell</c> charged through <paramref name="economy"/>
	/// (none: free), then <see cref="QueuePaths.TryExtend(GuestPathGrid, RideVisitorBridge, int, int, Func{int, int, bool}?)"/>.
	/// </summary>
	internal static QueueBuildResult BuildQueueCell( GuestPathGrid grid, ParkEconomy? economy, RideVisitorBridge ride, int x, int y, Func<int, int, bool> isBlocked, out string message )
	{
		// Bring the queue up to date before charging: a grid edit since the last recompute would otherwise pass the
		// check here, be charged, and then be refused by TryExtend's own recompute.
		ride.RecomputeQueue( grid );
		// The commit's money test is SetCellType's alone (TrySpendCell below); the validator's preview test does not run on a commit (PATH-plan §3.3).
		var check = CheckQueueCell( grid, null, ride, x, y, Array.Empty<(int X, int Y)>(), isBlocked );
		if ( check is not (QueueBuildResult.Ok or QueueBuildResult.Refused) )
		{
			message = $"Cannot build a queue here: {check}.";
			return check;
		}
		// [DATA:Standard.sam:Costs.QueueCell] charged per cell when written (ParkEconomy.TrySpendCell, PATH-plan §3.2)
		if ( check == QueueBuildResult.Refused || (economy != null && economy.TrySpendCell( CellPurchase.Queue ) != ParkEconomy.PurchaseResult.Ok) )
		{
			message = "Cannot build a queue: not enough money.";
			return QueueBuildResult.Refused;
		}
		var result = QueuePaths.TryExtend( grid, ride, x, y, isBlocked );
		message = $"{ride.Name}: queue is now {ride.QueueSizeInCells} cells long.";
		return result;
	}

	/// <summary>
	/// Validates (x, y) as <paramref name="ride"/>'s next queue cell after <paramref name="pending"/> (earlier cells
	/// of the same line, taken as laid), with the placement validator's money test (<see cref="ParkEconomy.CanPreviewCells"/>);
	/// no writes. <see cref="QueueBuildResult.Refused"/> means the line's pending cost, this cell included, is greater than the balance.
	/// </summary>
	public QueueBuildResult CheckQueueCell( RideVisitorBridge ride, int x, int y, IReadOnlyList<(int X, int Y)> pending )
	{
		if ( IsReadOnlyVisit || Guests == null )
			return QueueBuildResult.Refused;
		return CheckQueueCell( Guests.Grid, Park?.Economy, ride, x, y, pending, IsQueueBlocked );
	}

	/// <summary>
	/// The queue tool's per-cell check (also used by the static <c>BuildQueueCell</c>):
	/// <see cref="QueuePaths.CheckExtend"/> after <paramref name="pending"/>, then the preview's money test against
	/// <paramref name="economy"/> (none: no money test; the commit passes none and relies on <see cref="ParkEconomy.TrySpendCell"/>).
	/// </summary>
	internal static QueueBuildResult CheckQueueCell( GuestPathGrid grid, ParkEconomy? economy, RideVisitorBridge ride, int x, int y, IReadOnlyList<(int X, int Y)> pending, Func<int, int, bool> isBlocked )
	{
		var check = QueuePaths.CheckExtend( grid, ride, x, y, pending, isBlocked );
		if ( check != QueueBuildResult.Ok )
			return check;
		// [BIN:STP-PPC:0x10084550 placement validator] the preview refuses when the 32-bit pending line cost (the pending cells and this one) is greater than the balance; a zero sum passes
		if ( economy != null && !economy.CanPreviewCells( CellPurchase.Queue, pending.Count + 1 ) )
			return QueueBuildResult.Refused;
		return QueueBuildResult.Ok;
	}

	/// <summary>
	/// Removes the queue cell at (x, y) and every cell behind it. Each removed cell refunds
	/// <c>Costs.QueueCell</c> × the ride's scrap percentage / 100 (<see cref="ParkEconomy.RefundQueueCell"/>).
	/// </summary>
	public int RemoveQueueCell( int x, int y )
	{
		if ( IsReadOnlyVisit || Guests == null )
			return 0;
		var ride = QueueRideAt( Guests.Grid, Guests.Attractions, x, y );
		if ( ride == null )
			return 0;
		var removed = QueuePaths.RemoveFrom( Guests.Grid, ride, x, y );
		var owner = Objects.Objects.FirstOrDefault( item => item.Visitors == ride );
		if ( Park != null && owner != null && GetEconomyInstance( owner ) is int instance )
		{
			for ( var cell = 0; cell < removed; cell++ )
				Park.Economy.RefundQueueCell( instance );
		}
		return removed;
	}

	// [APPROX:QUEUE-012] queue cells need terrain the object build rule allows (Objects.Grid.CheckTerrain), no object footprint and owned land; tp.exe's cell validator 0x00535670 for the queue tool refuses a cell whose flag byte +0x0E has bit 0x40 (the park's land, ParkCellMap.Land) and object cells by their type, allows empty cells, lets a queue replace a path cell unless NoModify (+0x0E bit 0x20), and reads no MAP terrain bit (map setup turns inside water, blocked and walkway cells into type 0) — evidence needed: the ride-cell count test (0x00818C10)
	// [BIN:TPW-EXE:0x5357c7 placement validator] a cell with flag 0x40 is refused for a queue as for a path or an object
	private bool IsQueueBlocked( int x, int y ) =>
		Objects.Grid.CheckTerrain( x, y ) != OriginalPlacementResult.Allowed || Objects.FindAt( x, y ) != null || IsReservedByPrototype( x, y )
		|| (Guests?.Grid.Cells is { } cells && cells.InBounds( x, y ) && cells.FlagsAt( x, y ).HasFlag( ParkCellFlags.Unowned ));

	// [APPROX:RIDES-028] A non-walkable outside cell (queue area) is replaced by the nearest walkable path cell. The original placement 0x528A70 lays the access cells itself: on commit with 0x52FAB0 true (option 0x81B0CC, default 1 at 0x52F139, and 0x8187F8 clear) the entrance's outside cell becomes a queue cell linked to the entrance (0x5297BD–0x529890) and the exit's outside cell a path cell linked to its path neighbours (0x529A58–0x529B1A); with it false the entrance's outside cell becomes a path cell and the exit gets none; an outside cell off the map refuses the placement (0x529744, 0x5299E2), and validation runs ops 0x204/0x201 on it — evidence needed: the cell ops 0x81/0x85/0x86 that link the laid cells, and a model of the laid cells' cost
	private static (int X, int Y)? WalkableNear( GuestPathGrid grid, int x, int y ) =>
		grid.IsWalkable( x, y ) ? (x, y) : FindRideEntrance( grid, x, y, x, y );

	public void RotateBuild() => BuildRotation = (BuildRotation + 90) % 360;

	/// <summary>Returns the next built object to rotation 0, as leaving the build tool does.</summary>
	// [BIN:TPW-EXE:0x52f3b6 leave build tool] leaving the tool through 0x52f200 (0x52f227: ebp = 0) stores 0 in the build angle (0x81d7a4)
	public void ResetBuildRotation() => BuildRotation = 0;

	private int clickModifierMask;

	/// <summary>
	/// True while the park click that is placing the build object holds exactly Ctrl: the HUD keeps the build tool armed instead of ending it.
	/// </summary>
	public bool KeepBuildToolOnPlace { get; private set; }

	/// <summary>Whether a placing click with this modifier mask keeps the build tool armed: exactly Ctrl (mask 0xc), no Shift or Alt.</summary>
	// [BIN:TPW-EXE:0x5253b8 placement dispatcher] after a placement without an entrance `cmp di,0xc; je 0x525a06` keeps the tool armed (the same test is `cmp word ptr [esp+0x3c],0xc` at 0x5259e2 and 0x5259f4, where the tool is left otherwise and the angle reset by 0x52f1b0(0)) (param 3 is the modifier mask of 0x486aa0, 0xc = Ctrl); otherwise 0x52f580(0,0) returns the tool to mode 0
	internal static bool KeepsBuildTool( int modifierMask ) => modifierMask == 0xc;

	/// <summary>Grid cell under the cursor (sandbox tiles or original cells).</summary>
	public bool TryGetGridCell( System.Numerics.Vector2 mousePosition, System.Numerics.Vector2 viewportSize, out int x, out int y )
	{
		x = y = -1;
		if ( !ParkPlacement.TryGetGroundPoint( mousePosition, viewportSize, Camera.ViewMatrix, Camera.ProjMatrix, out var ground ) )
			return false;
		return TryGetGridCell( ground.X, ground.Y, out x, out y );
	}

	/// <summary>Screen position of cell (x, y)'s centre on the Z = 0 plane the cursor picks on (for build ghosts).</summary>
	public bool TryProjectCell( int x, int y, System.Numerics.Vector2 viewportSize, out System.Numerics.Vector2 screen )
	{
		var origin = Objects.Grid.Origin;
		var point = new System.Numerics.Vector4( origin.X + (x + 0.5f) * ObjectPlacement.CellSize, origin.Y + (y + 0.5f) * ObjectPlacement.CellSize, 0, 1 );
		var clip = System.Numerics.Vector4.Transform( point, Camera.ViewMatrix * Camera.ProjMatrix );
		screen = default;
		if ( clip.W <= 0 )
			return false;
		screen = new System.Numerics.Vector2( (clip.X / clip.W + 1) / 2 * viewportSize.X, (1 - clip.Y / clip.W) / 2 * viewportSize.Y );
		return float.IsFinite( screen.X ) && float.IsFinite( screen.Y );
	}

	public bool TryGetGridCell( float engineX, float engineY, out int x, out int y )
	{
		var origin = Objects.Grid.Origin;
		x = (int)MathF.Floor( (engineX - origin.X) / ObjectPlacement.CellSize );
		y = (int)MathF.Floor( (engineY - origin.Y) / ObjectPlacement.CellSize );
		return float.IsFinite( engineX ) && float.IsFinite( engineY ) && x >= 0 && y >= 0 && x < Objects.Grid.Width && y < Objects.Grid.Height;
	}

	/// <summary>
	/// Builds <paramref name="entry"/> with its footprint centred on cell (x, y) (<see cref="GetCentredAnchor(ObjectShape, int, int, int)"/>). In original levels the park
	/// economy must allow the purchase (research, golden tickets, money; ParkEconomy.TryBuild) and charges
	/// <c>Upgrades[0].CostOfUpgrade</c>; the generic sandbox has no economy and builds for free.
	/// </summary>
	// [APPROX:RIDES-021] the cursor ray hits the Z = 0 plane to pick the clicked cell; the original's mouse-to-cell picking (the cell the build tool hands to 0x528A70) is not traced — evidence needed: the build tool's cursor pick (0x524960 → 0x528A70)
	public OriginalObject? PlaceObject( ObjectCatalogEntry entry, int x, int y, int rotation )
	{
		if ( IsReadOnlyVisit )
		{
			LastActionMessage = "Read-only visit: object changes are disabled.";
			return null;
		}
		var anchor = GetCentredAnchor( entry, x, y, rotation );
		var result = Objects.Check( entry, anchor.X, anchor.Y, rotation );
		if ( result != OriginalPlacementResult.Allowed )
		{
			LastActionMessage = $"Cannot build {entry.DisplayName} here: {result}.";
			return null;
		}
		ParkObjectState? bought = null;
		if ( Park != null )
		{
			var purchase = Park.Economy.TryBuild( entry.InfoId, out bought );
			if ( purchase != ParkEconomy.PurchaseResult.Ok )
			{
				LastActionMessage = $"Cannot build {entry.DisplayName}: {purchase}.";
				return null;
			}
		}
		var placed = Objects.TryPlace( entry, anchor.X, anchor.Y, rotation, out _ )!;
		// [BIN:TPW-EXE:0x558e7c build effect] the type's CreateParticleEffect starts when the new object's script attaches, not when a park loads
		placed.Runtime.StartObjectEffect( ObjectParticleEffect.Create );
		if ( bought != null )
			LinkEconomy( placed, bought.Id );
		LastActionMessage = bought != null
			? $"Built {entry.DisplayName} for ${bought.TotalSpent}; it runs its original script."
			: $"Built {entry.DisplayName} (sandbox: no economy, not charged); it runs its original script.";
		return placed;
	}

	/// <summary>Anchor that centres the footprint on cell (x, y).</summary>
	public static (int X, int Y) GetCentredAnchor( ObjectCatalogEntry entry, int x, int y, int rotation ) => GetCentredAnchor( entry.Shape, x, y, rotation );

	/// <summary>
	/// The anchor (the grid cell of local (0, 0)) the original placement takes for a build click on cell (x, y): the cell less half the
	/// shape (truncated), then the rotation's offsets inside a 5 × 5 window, so the clicked cell is local (W/2, H/2) in every rotation
	/// when each side is odd or at most 5 cells. A wider even side moves with the rotation: the only shipped case, space 3104 Orbiter (6 × 3),
	/// is clicked on local (3, 1) at rotation 0 and (2, 2) at 90.
	/// </summary>
	// [BIN:TPW-EXE:0x00528B44 build click anchor] the clicked cell less (W/2, H/2) (signed halves, 0x528B0F–0x528B68), with a = (5 − W)/2 and b = (5 − H)/2 truncated toward zero (0x528B31–0x528B4D): rotation 0 keeps it, 90 adds (b − a, 4 − a − b), 180 adds (4 − 2a, 4 − 2b), 270 adds (4 − a − b, a − b) (0x528D88–0x528E12); the other branch (0x81AF7C set) is dead, its only store writes 0 (0x52F074)
	public static (int X, int Y) GetCentredAnchor( ObjectShape shape, int x, int y, int rotation )
	{
		ArgumentNullException.ThrowIfNull( shape );
		int left = x - shape.Width / 2, bottom = y - shape.Height / 2;
		int a = (5 - shape.Width) / 2, b = (5 - shape.Height) / 2;
		return rotation switch
		{
			0 => (left, bottom),
			90 => (left + b - a, bottom + 4 - a - b),
			180 => (left + 4 - 2 * a, bottom + 4 - 2 * b),
			270 => (left + 4 - a - b, bottom + a - b),
			_ => throw new ArgumentOutOfRangeException( nameof( rotation ), "Rotations are multiples of 90 degrees." )
		};
	}

	/// <summary>The build click that places <paramref name="entry"/> at anchor (x, y): the inverse of <see cref="GetCentredAnchor(ObjectShape, int, int, int)"/>, a translation per rotation.</summary>
	public static (int X, int Y) GetClickCell( ObjectCatalogEntry entry, int anchorX, int anchorY, int rotation )
	{
		var offset = GetCentredAnchor( entry.Shape, 0, 0, rotation );
		return (anchorX - offset.X, anchorY - offset.Y);
	}

	/// <summary>Build rule for <paramref name="entry"/> centred on cell (x, y), without buying it.</summary>
	public OriginalPlacementResult CheckObject( ObjectCatalogEntry entry, int x, int y, int rotation ) => CheckObject( entry, x, y, rotation, out _ );

	/// <summary><see cref="CheckObject(ObjectCatalogEntry, int, int, int)"/> for a site that demolishes nothing (<see cref="ParkObjects.CheckFree"/>).</summary>
	public OriginalPlacementResult CheckFreeObject( ObjectCatalogEntry entry, int x, int y, int rotation )
	{
		var anchor = GetCentredAnchor( entry, x, y, rotation );
		return Objects.CheckFree( entry, anchor.X, anchor.Y, rotation );
	}

	/// <summary><see cref="CheckObject(ObjectCatalogEntry, int, int, int)"/> with the lower-priority objects the build would demolish.</summary>
	public OriginalPlacementResult CheckObject( ObjectCatalogEntry entry, int x, int y, int rotation, out IReadOnlyList<OriginalObject> overwritten )
	{
		var anchor = GetCentredAnchor( entry, x, y, rotation );
		return Objects.Check( entry, anchor.X, anchor.Y, rotation, out overwritten );
	}

	public bool RemoveObjectAt( int x, int y )
	{
		if ( IsReadOnlyVisit )
		{
			LastActionMessage = "Read-only visit: object changes are disabled.";
			return false;
		}
		var item = Objects.FindAt( x, y );
		if ( item == null )
			return false;
		LastActionMessage = $"Removed {item.Entry.DisplayName}.";
		Objects.Remove( item );
		return true;
	}

	private void HandleObjectClick( System.Numerics.Vector2 mousePosition, System.Numerics.Vector2 viewportSize )
	{
		if ( IsReadOnlyVisit )
			return;
		if ( (BuildEntry == null && !IsRemovingObjects && !CellTool.IsActive && !IsCoasterToolActive) || !TryGetGridCell( mousePosition, viewportSize, out var x, out var y ) )
			return;
		// The remove tool comes first, so a path or queue cell under it is removed rather than built on (ParkHud stands aside for it too).
		if ( IsRemovingObjects )
			RemoveAt( x, y );
		else if ( IsCoasterToolActive && BuildEntry == null )
			CoasterClick( x, y );
		else if ( CellTool.IsActive && BuildEntry == null )
			CellTool.Click( (x, y) );
		else
		{
			KeepBuildToolOnPlace = KeepsBuildTool( clickModifierMask );
			try
			{
				PlaceObject( BuildEntry!, x, y, BuildRotation );
			}
			finally
			{
				KeepBuildToolOnPlace = false;
			}
		}
	}

	/// <summary>The remove tool: the object under the cursor, else a queue cell, else a path cell.</summary>
	public bool RemoveAt( int x, int y ) =>
		RemoveObjectAt( x, y ) || RemoveQueueCell( x, y ) > 0 || RemovePathCell( x, y ) == CellBuildResult.Ok;

	/// <summary>Cells covered by the prototype Totem (its 6×8-unit model centred on its position).</summary>
	private bool IsReservedByPrototype( int x, int y ) => PlacedRide != null && PrototypeCovers( PlacedRide.Position, x, y );

	// [EXT:developer-prototype] Sandbox Totem blocks cells whose centres lie within its 6×8-unit model box (no original counterpart)
	private bool PrototypeCovers( Vector3 position, int x, int y )
	{
		var origin = Objects.Grid.Origin;
		var centreX = origin.X + (x + 0.5f) * ObjectPlacement.CellSize;
		var centreY = origin.Y + (y + 0.5f) * ObjectPlacement.CellSize;
		return Math.Abs( centreX - position.X ) < 3 && Math.Abs( centreY - position.Y ) < 4;
	}

	private bool PrototypeOverlapsObjects( Vector3 position )
	{
		foreach ( var item in Objects.Objects )
		{
			if ( item.Cells.Any( cell => PrototypeCovers( position, cell.X, cell.Y ) ) )
				return true;
		}
		return false;
	}

	private readonly Dictionary<OriginalObject, int> economyInstances = new();
	private int? prototypeInstance;
	private bool prototypeBought;

	/// <summary>Economy instance id of a placed object, when the level has an economy.</summary>
	public int? GetEconomyInstance( OriginalObject item ) => economyInstances.TryGetValue( item, out var id ) ? id : null;

	/// <summary>
	/// Matches the imported objects to the uncharged economy records OriginalEconomyImport created (same
	/// Info.Id, in save order), links their guest payments, and sells objects when they are removed.
	/// </summary>
	private void ConnectObjectsToEconomy()
	{
		if ( Park == null )
			return;
		AttachCoasterChallenges();
		if ( resumeState != null )
		{
			var items = Objects.Objects.ToDictionary( item => item.Visitors.AttractionId );
			foreach ( var binding in resumeState.EconomyBindings )
			{
				if ( !items.TryGetValue( binding.AttractionId, out var item )
					|| !Park.Economy.TryGetObject( binding.InstanceId, out var state ) || state.InfoId != item.Entry.InfoId )
					throw new InvalidDataException( "A saved attraction does not match its economy instance." );
				LinkEconomy( item, binding.InstanceId );
			}
		}
		else
		{
			var free = Park.Economy.Objects.Where( state => state.Imported ).OrderBy( state => state.Id ).ToList();
			foreach ( var item in Objects.Objects )
			{
				var state = free.FirstOrDefault( candidate => candidate.InfoId == item.Entry.InfoId );
				if ( state == null && Park.Economy.Catalog.TryGet( item.Entry.InfoId, out _ ) )
					state = Park.Economy.RegisterExisting( item.Entry.InfoId );
				if ( state == null ) continue;
				free.Remove( state );
				LinkEconomy( item, state.Id );
			}
		}
		Objects.ObjectRemoved += item =>
		{
			var linked = economyInstances.Remove( item, out var instance );
			// a guest walking to a removed bin gets the removal message (tp.exe 0x004FB360); bins are known by their economy instance
			if ( linked && item.Entry.Settings.GetBool( "UsageInfo.HoldsLitter" ) )
				Guests?.BinRemoved( instance );
			if ( SellRemovedObject( Park, item.Visitors.AttractionId, linked ? instance : null ) is { } credit )
				LastActionMessage = $"Removed {item.Entry.DisplayName}; scrap value ${credit} credited.";
		};
	}

	/// <summary>
	/// Restores the shipped save's guests into the new guest simulation (<see cref="GuestSimulation.ImportOriginal"/>): their remembered
	/// attractions and bins are the reloaded objects the economy import paired with the saved attraction things. Only a park started from the
	/// shipped save does this; a resumed park restores its own guests.
	/// </summary>
	private void ImportOriginalGuests()
	{
		if ( resumeState != null || Guests == null || Park?.Import is not { } import || OriginalPark?.Save is not { } save )
			return;
		var bridges = economyInstances.ToDictionary( pair => pair.Value, pair => pair.Key.Visitors.AttractionId );
		var bins = LitterBins().Select( bin => bin.Id ).ToHashSet();
		int Attraction( int thing ) => import.AttractionInstances.TryGetValue( thing, out var instance ) && bridges.TryGetValue( instance, out var id ) ? id : 0;
		int Bin( int thing ) => import.AttractionInstances.TryGetValue( thing, out var instance ) && bins.Contains( instance ) ? instance : 0;
		foreach ( var line in Guests.ImportOriginal( save.Guests, save.World.Variables.GameTick, Attraction, Bin ) )
		{
			import.Report( line );
			Log.Trace( $"Park economy import: {line}" );
		}
	}

	/// <summary>
	/// The economy side of removing a placed object (the remove tool; also used by the headless M4 gate): unlinks its guest
	/// payments and sells its economy record for <see cref="ParkEconomy.ScrapValue"/>. Null when it had no record.
	/// </summary>
	internal static long? SellRemovedObject( ParkEconomyRuntime park, int attractionId, int? instance )
	{
		park.Guests?.Unlink( attractionId );
		if ( instance is not int id || !park.Economy.TryGetObject( id, out _ ) )
			return null;
		return park.Economy.Sell( id );
	}

	private void LinkEconomy( OriginalObject item, int instance )
	{
		InitializeCatalogueBinding( item, instance, economyInstances.ContainsKey( item ) );
		economyInstances[item] = instance;
		Park?.Guests?.Link( item.Visitors.AttractionId, instance );
		// a toilet, a camera or a fireworks object spreads its RegionFX row where it stands (its thing's cell)
		Park?.Economy.PlaceObjectEffects( instance, (item.Placement.X, item.Placement.Y) );
	}

	/// <summary>The fireworks' script variable 0 (<c>VAR_STATUS</c> in Hallow's Firework Show and Space's Laser Show).</summary>
	public const string FireworksStatusVariable = "VAR_STATUS";

	/// <summary>Mirrors ordinary objects and the live prototype between their scripts, visitor bridges and linked economy records.</summary>
	private void SyncObjectEconomy()
	{
		if ( Park == null )
			return;
		foreach ( var (item, instance) in economyInstances )
			SyncEconomyObject( item, instance );
		if ( PlacedRide is { IsDeleted: false } prototype && prototypeInstance is int prototypeId )
			SyncEconomyObject( prototype, prototypeId );
	}

	private void SyncEconomyObject( OriginalObject item, int instance )
	{
		if ( Park!.Economy.TryGetObject( instance, out var state ) )
		{
			// [BIN:TPW-EXE:0x004E0B90 fireworks] a show whose script variable 0 reached 1 loses its RegionFX row, and the variable becomes 2
			if ( item.Runtime.GetVariableByName( FireworksStatusVariable ) == 1 && Park.Economy.FireworksFired( instance ) )
				item.Runtime.SetVariable( FireworksStatusVariable, 2 );
			state.IsOpen = item.IsOpen;
			// VAR_BROKEN (script variable 7) is what the original's ride update reads to set ride state 1 or 4 (0xe1864), which the park points counts-test rejects.
			state.ScriptBroken = item.Runtime.GetVariable( RideVariables.VAR_BROKEN ) != 0;
			// the breakdown goes through the script: the economy writes VAR_BREAKSTAT, the script reports VAR_BROKEN and the ride update's state 1 breaks the object down (ECON-023)
			state.ScriptHosted = item.Runtime.Script != null;
			state.ScriptBreakStat = item.Runtime.GetVariable( RideVariables.VAR_BREAKSTAT );
			state.ScriptWorn = item.Runtime.GetVariable( RideVariables.VAR_WORN );
			Park.Economy.ObserveScriptState( state, item.Visitors.InBrokenState );
			// a kart or water ride's circuit walk writes the attraction's track word (+0x2C on the PC), which the counts-test reads (docs/TRACKS.md)
			state.TrackValid = item.Runtime.Bump?.IsTrackValid ?? true;
			// a coaster counts only while its controller accepts guests (0x00441970); without a controller the original's lookup fails
			state.CoasterAcceptsGuests = item.Runtime.Coaster?.AcceptsGuests ?? false;
			// the park points counts-test needs real queue cells from the entrance and room in them (0x004DE130, 0x004DDF50)
			if ( Guests is { } guests )
				state.QueueHasRoom = QueueJoinRules.QueueCountsForParkPoints( item.Visitors, guests.Grid );
			// The join gate reads the live sideshow words (0xdff70) and the wear gauge ride +64 (0xdfe34); both live in the economy.
			item.Visitors.Sideshow = new SideshowTerms( state.CostOfGoods, state.Price, state.ChanceOfLosingPercent );
			// the shop sale reads the amount of the special ingredient of the ride (0x4fe5f8, +0x198)
			item.Visitors.IngredientAmount = state.IngredientAmount;
			// the balloon's life reads the shop's quality of goods (0x004FE615, +0x18C)
			item.Visitors.QualityOfGoods = state.QualityOfGoods;
			item.Visitors.WearGauge = state.Repair;
			// the ride update's state 1 or 4 reads the life gauge when it reads VAR_BROKEN (0x004E14E0)
			item.Visitors.LifeGauge = state.LifeGauge;
			// the attraction score's "new ride" test reads the object's own age in days (0xdc520)
			item.Visitors.AgeDays = Park.Economy.ObjectAgeDays( state );
		}
	}

	/// <summary>The prototype Totem is bought through the catalogue like any object (Info.Id 1110).</summary>
	private void LinkPrototypeToEconomy( PrototypeRide ride )
	{
		if ( Park == null || !Park.Economy.Catalog.TryGet( PrototypeRide.InfoId, out _ ) )
			return;
		var purchase = Park.Economy.TryBuild( PrototypeRide.InfoId, out var bought );
		prototypeBought = purchase == ParkEconomy.PurchaseResult.Ok;
		// [EXT:developer-prototype] When the economy refuses the Totem (e.g. Research.Group 4 not yet researched), the developer prototype is registered uncharged (no original counterpart)
		var state = prototypeBought ? bought! : Park.Economy.RegisterExisting( PrototypeRide.InfoId );
		prototypeInstance = state.Id;
		Park.Guests?.Link( ride.Visitors.AttractionId, state.Id );
		Log.Trace( prototypeBought ? $"Prototype Totem bought for ${state.TotalSpent}." : $"Prototype Totem registered uncharged ({purchase})." );
	}

	private void RemovePrototypeFromEconomy( PrototypeRide ride )
	{
		if ( Park == null || prototypeInstance is not int instance )
			return;
		Park.Guests?.Unlink( ride.Visitors.AttractionId );
		if ( Park.Economy.TryGetObject( instance, out _ ) )
		{
			if ( prototypeBought )
				Park.Economy.Sell( instance );
			else
				Park.Economy.Remove( instance );
		}
		prototypeInstance = null;
	}
}

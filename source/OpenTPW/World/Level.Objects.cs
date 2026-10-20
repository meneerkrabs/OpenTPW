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
			// any other way of arming or leaving the build tool ends the Move tool (interface mode 0x3b) and the add-on mode; BeginMove and BeginAddOn set theirs after they armed the entry
			IsMoving = false;
			buildAddOnRide = null;
		}
	}

	/// <summary>
	/// Whether the armed <see cref="BuildEntry"/> is the Move tool's (interface mode 0x3b: an object picked up from its window, placed again) rather than the build tool's (mode 4).
	/// </summary>
	// [BIN:TPW-EXE:0x5253d5 move placement] a click in mode 0x3b runs the same placement as mode 4 (0x528a70 at 0x525417: type 4 and the angle 0x52f1f0 gives, commit flag 1) and the same follow-ups (0x52fab0 queue option, the entrance, exit and queue sub-modes, the Ctrl test 0xc); the hover test 0x5234d0 does the same in mode 0x3b as in mode 4 (0x523940) but keeps no cash flag of its own: the purchase is the constructor's alone (0x4db4e2..0x4db4f6 charges the type's price +0x1b8 through 0x4d01f0 unless a golden-ticket type (+0xc4 above 0) is not yet bought, which 0x4d4ad0 pays by ticket), so a move costs the whole price again
	public bool IsMoving { get; private set; }

	/// <summary>Sets the build angle, as the Move tool's pick-up does with the picked-up object's own (<c>0x52f1b0(thing +0x10)</c>).</summary>
	internal void SetBuildRotation( int rotation ) => BuildRotation = ((rotation % 360) + 360) % 360;
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
		// Objects keep off queue and track cells. Path cells are the cell map's to judge (PathAt: a plain path is cleared by the placement, a
		// NoModify one refuses), and object cells (types 4, 9 and 10) are left to ParkObjects, which lets a higher overwrite priority replace their owner.
		// [BIN:TPW-EXE:0x535BB3 track cells] for an object cell (type 4) the validator refuses a second-layer track cell (0x53AD30: types 0xB, 0xD, 0x10, 0x12, 0x19; the extensions 0xC and 0x11 through their head, 0x535811) and any other map type but 0, 1 and the object types (0x535C4C..0x535D5D)
		// A path cell without NoModify gives way to an object (the validator's 0x535D21 rule, the commit clearing the path), and the entrance's and exit's outside cells are
		// validated as well (ParkObjects.CheckAccessCells): the outside cell by the path rule takes empty ground and path cells only.
		// [BIN:TPW-EXE:0x00535D21 object over path] an object (type 4) over a type-1 cell is allowed (0x535D21 returns 0) unless the cell carries NoModify, which the validator refuses first for every object type (0x5356A6..0x5356B3); the commit clears the path (0x5290F6..0x52910C: 0x87, then 0x5367A0(0, 0))
		Objects = new ParkObjects( catalog!, grid, Seed );
		ConfigureObjectHooks();
		PrepareThings();
		// [BIN:TPW-EXE:0x5590f7 demolish effect] the type's DestroyParticleEffect starts at the footprint before the script goes, for every removal but a park load or clear
		Objects.ObjectRemoving += item => item.Runtime.StartObjectEffect( ObjectParticleEffect.Destroy );
		// [BIN:TPW-EXE:0x5285a2 demolition cue] the demolition 0x527ee0 plays its size-classed cat_ui cue after the footprint count, for every removal that is not a park load (0x785a2c)
		Objects.ObjectRemoving += PlayDemolitionCue;
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

	/// <summary>Set while <see cref="PlaceObject"/> places an object: the access cells are laid for it.</summary>
	private bool layingAccessCells;

	/// <summary>Whether the placed objects type their footprint cells in the guest grid (not in a visit, a Theme Park Inc layout or Theme Park Inc itself).</summary>
	// [APPROX:RIDES-031] a Theme Park Inc world keeps the cells its layout was given and types none of its footprints, so the road signs' '@' cells ("*@*", "*@@*") are plain occupied cells; tp.exe's commit stamps '@' as a type-1 NoModify path cell linked to its path neighbours (0x529173–0x529208) and 'Q' as a queue cell (0x529119), which ParkCellMap.WriteFootprint does for Theme Park World (whose shapes have neither) — evidence needed: Theme Park Inc's own placement (its executable), which is not traced
	private bool TypesFootprints => !IsReadOnlyVisit && incLayoutCells == null && !GameEditions.IsThemeParkInc;

	/// <summary>The owner word the placement stores on an object's cells: the cell word of its anchor (<see cref="ParkCellMap.OwnerAt"/>).</summary>
	internal static ushort OwnerWord( OriginalObject item ) => ParkCellMap.CellWord( item.Placement.X, item.Placement.Y );

	/// <summary>Every placed object that guests may choose becomes a live attraction; new and removed objects follow.</summary>
	private void ConnectObjectsToGuests()
	{
		if ( Guests == null )
			return;
		Guests.Bins = LitterBins;
		Guests.LitterBinned += ( _, bin ) => TriggerBin( bin );
		// The placed objects type their footprint cells (4, 9, 10: ParkCellMap.WriteFootprint, tp.exe 0x00528A70); a visit, a Theme Park Inc layout and
		// Theme Park Inc itself (whose placement is not traced) keep the cells they were given
		var typesFootprints = TypesFootprints;
		if ( typesFootprints )
		{
			var cells = Guests.Grid.Cells;
			foreach ( var item in Objects.Objects )
			{
				cells.WriteFootprint( item.Cells, OwnerWord( item ), item.Placement.Rotation );
				// an imported park's entrance and exit cells carry the link byte its save holds, as the original's load reads it (a resumed park's cells come from its own snapshot)
				if ( resumeState == null && visitLayout == null && OriginalPark?.Save is { } save )
					cells.TakeSavedAccessLinks( item.Cells, OwnerWord( item ), save.Cells );
				// a park resumed from an OpenTPW save made before that import gets the byte its entrance and exit cells lack, from the writers the placement uses
				else if ( resumeState != null )
					ObjectAccessCells.RepairMissingLinks( cells, item.AccessPoints, item.Entry.HasQueue, new LinkEffects( RefreshTile ) );
			}
		}
		if ( typesFootprints )
		{
			Objects.ObjectPlaced += item => Guests?.Grid.Cells.WriteFootprint( item.Cells, OwnerWord( item ), item.Placement.Rotation );
			// The cells outside the entrance and exit are laid by a build (PlaceObject), not by an import or a restore, and before the object registers with the guests, which read them.
			Objects.ObjectPlaced += item =>
			{
				// [BIN:TPW-EXE:0x00534767 laid cells' money test] the cell setter refuses a path or queue type when the park's money is on and its balance is below zero, even though the placement's cells are free (0x5346D0 with 0x8186D4 set)
				if ( layingAccessCells && Guests?.Grid.Cells is { } laid && (Park == null || Park.Economy.Balance >= 0) )
				{
					ObjectAccessCells.Lay( laid, item.AccessPoints, OwnerWord( item ), item.Entry.HasQueue, new LinkEffects( RefreshTile ) );
					// the entrance's outside cell becomes a queue cell, over a plain path the player built: guests walking there step off
					Guests?.SetDownOffCells( item.AccessPoints.Select( point => (point.OutsideX, point.OutsideY) ).ToHashSet() );
				}
			};
			Objects.ObjectRemoved += item =>
			{
				if ( Guests?.Grid.Cells is not { } removed )
					return;
				removed.ClearFootprint( item.Cells );
				ObjectAccessCells.ClearQueue( removed, OwnerWord( item ) );
			};
		}
		// A placement over path cells clears them (ParkObjects.ClearPath): guests standing there step off
		Objects.ObjectPlaced += item => Guests?.SetDownOffCells( item.Cells.Select( cell => (cell.X, cell.Y) ).ToHashSet() );
		foreach ( var item in Objects.Objects )
			RegisterObjectWithGuests( item );
		Objects.ObjectPlaced += RegisterObjectWithGuests;
		Objects.ObjectRemoved += item => Guests?.Unregister( item.Visitors );
		// [EXT:pools-attractions] the water attractions are derived from the placed objects; a new wave machine gives its pool the default wave settings
		Objects.ObjectPlaced += _ => PoolSites.Invalidate();
		Objects.ObjectRemoved += item =>
		{
			// whoever is on a removed rope swing, under removed jets or in a removed spa leaves the water at once, as the swimmers of a demolished changing room do
			if ( PoolAttractions.KindOf( item.Entry ) != PoolAttractionKind.None && item.ThingId != 0 )
				Guests?.AbortPoolActivitiesOf( item.ThingId );
			PoolSites.Invalidate();
		};
		// A ride with queue cells takes them with it: the refunds are settled while the cells are laid, the cells go once its guests are set down
		Objects.ObjectRemoving += SettleDemolishedQueue;
		Objects.ObjectRemoved += ClearDemolishedQueue;
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
		if ( Guests != null && RegisterWithGuests( item.Runtime, item.AccessPoints, Guests, item.Placement.Rotation, OwnerWord( item ), ownExitCell: TypesFootprints, queueEntryResolver: TypesFootprints && item.Entry.HasQueue
			? () => ResolveQueueEntrance( Objects, Guests.Grid.Cells, OwnerWord( item ) ) : null ) )
		{
			item.Runtime.Visitors.QueueEdited += () => ReevaluateQueuedRide( item.Runtime );
			// [EXT:pools-changing-room] a changing room's pool-side cells follow from its footprint (geometry only: the water is read when a guest swims)
			if ( item.Runtime.Visitors.IsChangingRoom )
				item.Runtime.Visitors.PoolAccessCandidates = PoolChangingRooms.AccessCandidates( item.Entry.Shape, item.Placement.X, item.Placement.Y, item.Placement.Rotation );
		}
	}

	/// <summary>
	/// The queue re-evaluation after an edit of a ride's cells (tp.exe <c>0x004DE1F0</c>): a closed ride whose state is not 1, 2 or 4 (broken down, upgrading,
	/// condemned), without a maintenance call, whose queue is connected (<c>0x004DE4A0</c>), a coaster that passes <c>0x00441970</c> and a kart or water ride
	/// with a valid track (the track word <c>+0x2C</c>) is opened.
	/// </summary>
	// [BIN:TPW-EXE:0x004DE1F0 queue re-evaluation] the ride is skipped when its open word +0x68 is set or its state +0x19C is 1 or 4, +0x64 (the call) is set, state 2, or 0x4DE4A0 reports the queue unconnected; a type-3 ride (coaster) must pass 0x441970 and a kind 1, 2 or 3 ride needs its track word +0x2C; then +0x68 = 1, the script variable 6 (VAR_RIDECLOSED) = 0 and SetState(0) (0x4E0E60); +0x5E is cleared on every path
	// [APPROX:QUEUE-023] the model call 0x4547C0 on the type's table entry 0x7A4610 that the opening makes (the open sound is not among the traced effects) is not made
	private void ReevaluateQueuedRide( OriginalObjectRuntime runtime )
	{
		if ( runtime.IsOpen || Gate?.MayOpen( runtime ) != true )
			return;
		// a HasQueue ride without queue cells has no back cell, which the original's connection test 0x004DE4A0 reports as not connected (ECON-049 takes it as connected)
		if ( runtime.Visitors.HasQueue && (runtime.Visitors.QueueFrontCell is not { } front || Guests?.Grid.IsQueue( front.X, front.Y ) != true) )
			return;
		if ( runtime.Coaster is { AcceptsGuests: false } || runtime.Bump is { HasTrack: true, IsTrackValid: false } )
			return;
		runtime.Open();
		SyncOpenState( runtime );
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
				SetBinTrigger( item.Runtime );
	}

	/// <summary>Sets the script variable at index 0 of a litter bin to 1: <see cref="BinTriggerVariable"/> in Theme Park World's bins, <c>VAR_USE</c> in the Theme Park Inc bin a mod brings.</summary>
	// [EXT:cross-edition-objects] the original sets variable 0 by index (0x0055A0B0); a Theme Park Inc litter bin names it VAR_USE, so the index, not the Theme Park World name, is used
	internal static void SetBinTrigger( OriginalObjectRuntime runtime )
	{
		var names = runtime.Script?.Script.VariableNames;
		runtime.SetVariable( names is { Count: > 0 } ? names[0] : BinTriggerVariable, 1 );
	}

	/// <summary>
	/// Registers a placed object's visitor bridge with <paramref name="guests"/> the way the build flow does
	/// (also used by the headless M3 gate): attractions only, cells from <see cref="ResolveVisitorCells"/>.
	/// </summary>
	/// <param name="ownExitCell">The object's own exit cell holds the link its placement wrote (a Theme Park World level, which types the footprints): the guests leave by it (ExitRide). Without one (the gates,
	/// test harnesses, the Theme Park Inc slice and online visits, whose cell maps carry no access-cell links) the object lets off in the cell it takes its guests in and names <see cref="RideVisitorBridge.ExitCell"/> as the cell beyond.</param>
	internal static bool RegisterWithGuests( OriginalObjectRuntime runtime, IEnumerable<ObjectAccessPoint> accessPoints, GuestSimulation guests, int rotation = 0, ushort ownerWord = 0, bool ownExitCell = false, Func<(int X, int Y)?>? queueEntryResolver = null )
	{
		if ( !runtime.IsAttraction || ResolveVisitorCells( accessPoints, guests.Grid ) is not { } cells )
			return false;
		runtime.Visitors.OwnerWord = ownerWord;
		runtime.Visitors.EntranceCell = cells.Entrance;
		runtime.Visitors.ExitCell = cells.Exit;
		runtime.Visitors.ExitAccessCell = ownExitCell ? ExitAccessCellOf( accessPoints ) : null;
		runtime.Visitors.QueueFrontCell = cells.QueueFront;
		runtime.Visitors.QueueEntranceDirection = cells.QueueEntranceDirection;
		runtime.Visitors.QueueEntryResolver = queueEntryResolver;
		// Select before registration repairs legacy queue links, so that repair never starts at a stale shape front.
		runtime.Visitors.RefreshTypedQueueFront( guests.Grid );
		runtime.Visitors.Rotation = rotation;
		runtime.Visitors.HasCells = true;
		guests.Register( runtime.Visitors );
		return true;
	}

	/// <summary>
	/// Walkable entrance and exit cells for an object's access points, plus the entrance's outside cell (the queue's
	/// front cell) and the direction from it into the entrance; null when it has no entrance or the grid no path.
	/// </summary>
	/// <summary>The object's own exit cell (thing +0x38): the last exit cell of the shape the placement loop meets, else the last entrance cell; null without either.</summary>
	// [BIN:TPW-EXE:0x005014E0 exit cell] ExitRide takes the exit cell index +0x38 and compares it with the entrance cell +0x36: an object without an exit has the same cell in both (0x00501587)
	internal static (int X, int Y)? ExitAccessCellOf( IEnumerable<ObjectAccessPoint> accessPoints )
	{
		ObjectAccessPoint? entrance = null, exit = null;
		foreach ( var point in accessPoints )
			if ( point.Kind == ObjectCellKind.Exit )
				exit = point;
			else if ( point.Kind == ObjectCellKind.Entrance )
				entrance = point;
		return (exit ?? entrance) is { } cell ? (cell.X, cell.Y) : null;
	}

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

	/// <summary>
	/// Wires the object build rules to the level's cell map: the cell types that reserve a cell (everything but empty ground, path and the object types, pool water included), unowned land, path
	/// cells and the path rule of access cells. Called once the level has its objects; tests call it on a level they assembled.
	/// </summary>
	internal void ConfigureObjectHooks()
	{
		Objects.IsReserved = ( x, y ) => PoolBridges?.Reserves( x, y ) == true || IsReservedByPrototype( x, y ) || Guests?.Grid.Cells.RawTypeAt( x, y ) is { } type && ReservesCell( type ) || (Guests != null && PoolSlides?.ReservesLand( x, y, Guests.Grid.Cells ) == true);
		Objects.IsUnowned = ( x, y ) => Guests?.Grid.Cells is { } cells && cells.InBounds( x, y ) && cells.FlagsAt( x, y ).HasFlag( ParkCellFlags.Unowned );
		Objects.PathAt = ( x, y ) => Guests?.Grid.Cells is { } cells ? ObjectPathAt( cells, x, y ) : null;
		Objects.ClearPath = ( x, y ) => Paths?.Remove( x, y );
		Objects.PathRuleAllows = ( x, y ) => Paths?.CheckPathRule( x, y ) is { } rule ? rule is CellBuildResult.Ok or CellBuildResult.Existing : null;
		// [EXT:pools-changing-room] a changing room needs pool water beside its back
		// [EXT:pools-extras] and a pool extra needs its spot beside the water (or the room)
		// [EXT:pools-extras] a swimmer in an activity at an erased or demolished extra leaves the session at once (the activity lets go first); the extras the swim session sees are read again after any placement or removal
		Objects.ObjectRemoving += item =>
		{
			if ( PoolExtras.KindOf( item.Entry ) != PoolExtraKind.None )
				Guests?.AbortPoolActivitiesOf( item.ThingId );
		};
		Objects.ObjectPlaced += _ => poolExtraSites = null;
		Objects.ObjectRemoved += _ => poolExtraSites = null;
		Objects.PlacementRule = ( entry, x, y, rotation ) => PoolChangingRooms.PlacementRule( entry, Guests?.Grid.Cells, x, y, rotation ) ?? PoolSlideTowers.PlacementRule( entry, Guests?.Grid.Cells, x, y, rotation ) // [EXT:pools-slides] a slide tower needs pool water beside its far side
			?? PoolExtras.PlacementRule( entry, Guests?.Grid.Cells, Objects, x, y, rotation )
			// [EXT:pools-attractions] a rope swing, jets, wave machine or spa needs pool water on its water side
			?? PoolAttractions.PlacementRule( entry, Guests?.Grid.Cells, x, y, rotation );
	}

	/// <summary>Whether a cell of this type keeps objects off it: any type but empty ground, a path and the object, entrance and exit types ([EXT:pools-cell-type]: pool water is one).</summary>
	internal static bool ReservesCell( byte type ) => type is not (0 or (byte)ParkCellType.Path or ParkCellTypes.Object or ParkCellTypes.Entrance or ParkCellTypes.Exit);

	/// <summary>The headless path builder over the level's cell map (original levels; null in the generic sandbox).</summary>
	public ParkPathBuilder? Paths { get; private set; }
	/// <summary>
	/// [EXT:pools-tool] Whether a guest or an employee stands on the cell (the cell their position falls in): water is not painted under them. Guests inside a ride's queue or on a ride keep
	/// their last cell too, which is no pool cell, so only the ones in the open matter.
	/// </summary>
	internal bool IsStoodOn( int x, int y )
	{
		if ( Guests != null && Guests.Guests.Any( guest => guest.State != GuestState.Gone && guest.Cell == (x, y) ) )
			return true;
		return Park?.Economy.Staff.Members.Any( member => member.CellX is { } staffX && member.CellY is { } staffY && (int)MathF.Floor( (float)staffX ) == x && (int)MathF.Floor( (float)staffY ) == y ) == true;
	}

	/// <summary>
	/// [EXT:debug-pool] Smoke-only: builds the theme's changing room through the level's own placement (it is charged, and the pool at its back is the placement rule) at the first anchor and rotation,
	/// within four cells of <paramref name="pool"/>, that the rules allow; null when the extension is off, the theme lists no changing room or nothing fits.
	/// </summary>
	internal OriginalObject? BuildChangingRoomBeside( IReadOnlyList<(int X, int Y)> pool )
	{
		var entry = Objects.Catalog.Entries.FirstOrDefault( PoolChangingRooms.IsChangingRoom );
		if ( entry == null || pool.Count == 0 )
			return null;
		var (minX, maxX, minY, maxY) = (pool.Min( cell => cell.X ) - 4, pool.Max( cell => cell.X ) + 4, pool.Min( cell => cell.Y ) - 4, pool.Max( cell => cell.Y ) + 4);
		foreach ( var rotation in new[] { 0, 90, 180, 270 } )
			for ( var y = minY; y <= maxY; y++ )
				for ( var x = minX; x <= maxX; x++ )
					if ( CheckObject( entry, x, y, rotation ) == OriginalPlacementResult.Allowed && PlaceObject( entry, x, y, rotation ) is { } placed )
						return placed;
		return null;
	}

	/// <summary>
	/// [EXT:debug-pool] Smoke-only (<c>--debug-extras</c>): builds one of each pool extra of the theme through the level's own placement (charged, with its rule) at the first anchor and rotation, within
	/// four cells of <paramref name="pool"/>, that the rules allow, in the order ladder, diving board, shower, lounger, lamp, underwater light. Returns what was placed.
	/// </summary>
	internal List<OriginalObject> BuildPoolExtrasBeside( IReadOnlyList<(int X, int Y)> pool )
	{
		var placed = new List<OriginalObject>();
		if ( pool.Count == 0 )
			return placed;
		RegisterPoolActivities();
		var (minX, maxX, minY, maxY) = (pool.Min( cell => cell.X ) - 4, pool.Max( cell => cell.X ) + 4, pool.Min( cell => cell.Y ) - 4, pool.Max( cell => cell.Y ) + 4);
		foreach ( var kind in new[] { PoolExtraKind.Ladder, PoolExtraKind.DivingBoard, PoolExtraKind.Shower, PoolExtraKind.Lounger, PoolExtraKind.Lamp, PoolExtraKind.UnderwaterLight } )
		{
			var entry = Objects.Catalog.Entries.FirstOrDefault( item => PoolExtras.KindOf( item ) == kind );
			if ( entry == null )
				continue;
			var done = false;
			for ( var y = minY; y <= maxY && !done; y++ )
				for ( var x = minX; x <= maxX && !done; x++ )
					foreach ( var rotation in new[] { 0, 90, 180, 270 } )
						if ( CheckObject( entry, x, y, rotation ) == OriginalPlacementResult.Allowed && PlaceObject( entry, x, y, rotation ) is { } item )
						{
							placed.Add( item );
							done = true;
							break;
						}
		}
		return placed;
	}

	/// <summary>
	/// [EXT:pools-swim] Why a pool cell cannot be erased: a swimmer is in it, or it lies beside the back of a changing room (the room needs its pool, and a save that holds a changing room
	/// records the extension). Null when it can go.
	/// </summary>
	internal PoolBuildResult? PoolEraseBlock( int x, int y )
	{
		if ( PoolBridges?.Covers( x, y ) == true ) return PoolBuildResult.Occupied;
		if ( Guests?.IsSwimmerIn( x, y ) == true )
			return PoolBuildResult.Swimmers;
		foreach ( var item in Objects.Objects )
			if ( !item.IsDeleted && item.Visitors.IsChangingRoom && item.Visitors.PoolAccessCandidates.Contains( (x, y) ) )
				return PoolBuildResult.ChangingRoomAccess;
		// [EXT:pools-slides] a built slide keeps its splash cell and its tower's pool-side cells, and guests on it their cells
		if ( SlideEraseBlock( x, y ) is { } slideBlock )
			return slideBlock;
		// [EXT:pools-extras] the water a ladder, board, lounger, lamp or light stands beside stays
		if ( PoolExtras.NeedsWater( PoolExtras.Sites( Objects ), x, y ) )
			return PoolBuildResult.ExtraNeedsWater;
		// [EXT:pools-attractions] the last water cell of a water attraction's water side stays, and so does its current access cell while somebody is using it
		if ( PoolSites.IsAccessCell( x, y ) )
			return PoolBuildResult.AttractionAccess;
		foreach ( var site in PoolSites.All )
			if ( site.Access == (x, y) && Guests?.Guests.Any( guest => guest.InPoolActivity && guest.PoolActivityTarget == site.ObjectId ) == true )
				return PoolBuildResult.Swimmers;
		return null;
	}

	/// <summary>[EXT:pools-slides] Whether the last refused placement was a slide tower beside a pool on the wrong side (the HUD's wording).</summary>
	public bool LastRefusalTowerFacesAway { get; private set; }

	/// <summary>[EXT:pools-tool] The headless pool builder over the level's cell map (original levels, not a read-only visit); null otherwise (docs/POOLS.md).</summary>
	public PoolBuilder? PoolBuilder { get; private set; }
	/// <summary>[EXT:pools-tool] The pool paint tool the park view drives while the <c>opentpw.pools</c> extension is on.</summary>
	public PoolTool? PoolTool { get; private set; }
	/// <summary>[EXT:pools-render] Draws the park's pool cells; created when the first pool cell exists.</summary>
	public PoolRenderer? PoolRenderer { get; private set; }

	/// <summary>
	/// [EXT:pools-render] Keeps the pool drawing in step with the cell map: the renderer appears with the first pool cell (a painted one or a resumed save's) and rebuilds
	/// its mesh whenever the set of pool cells changes. Runs every frame; costs nothing while the park has no pool cell.
	/// </summary>
	internal void RefreshPools()
	{
		if ( Guests == null || OriginalPark == null || !FeatureGates.Pools )
			return;
		if ( PoolRenderer == null )
		{
			if ( Guests.Grid.Cells.CountOf( ParkCellType.PoolWater ) == 0 )
				return;
			PoolRenderer = new PoolRenderer( Guests.Grid.Cells, Objects.Grid, PoolStyles.ForTheme( OriginalPark.LevelName ) );
		}
		// [EXT:pools-slides] the slides are drawn with the pool, before the swimmers and the riders (entities draw in the order they are made)
		if ( PoolSlideRenderer == null && PoolSlides != null && PoolRenderer != null )
			PoolSlideRenderer = new PoolSlideRenderer( PoolSlides, SlideSite, Guests.Grid.Cells, Objects.Grid.Origin, PoolStyles.ForTheme( OriginalPark.LevelName ) );
		// [EXT:pools-swim] the swimmers are drawn after the water (entities draw in the order they are made), with the swim sprites loaded at level setup
		// the sets are looked for once per level: a missing or broken folder is one warning, not a scan every frame
		switch ( NextSwimSpriteStep( SwimmerRenderer != null, swimSpritesTried, PoolRenderer != null, PoolSpriteDelivery.IsPending( PoolSpriteDelivery.SwimGroup ) ) )
		{
			case SwimSpriteStep.Wait:
				// browser: the swim sets are still being downloaded, so the swimmers use the kid sprite for now and the renderer is made the frame the last file is in (or the one failed line of CreateSwimmerRenderer)
				PoolSpriteDelivery.Ready( PoolSpriteDelivery.SwimGroup, "swimmers use the kid sprite until they arrive" );
				break;
			case SwimSpriteStep.Create:
				swimSpritesTried = true;
				SwimmerRenderer = CreateSwimmerRenderer( Guests, OriginalPark );
				break;
		}
		RefreshPoolEffects();
	}

	/// <summary>What <see cref="RefreshPools"/> does about the swimmers' renderer this frame.</summary>
	internal enum SwimSpriteStep { Done, Wait, Create }

	/// <summary>The renderer is made once, after the water exists and once the swim sets are not pending any more (complete, failed or never delivered); until then the swimmers use the kid sprite.</summary>
	internal static SwimSpriteStep NextSwimSpriteStep( bool hasSwimmerRenderer, bool tried, bool hasPoolRenderer, bool setsPending )
		=> hasSwimmerRenderer || tried || !hasPoolRenderer ? SwimSpriteStep.Done : setsPending ? SwimSpriteStep.Wait : SwimSpriteStep.Create;

	/// <summary>[EXT:pools-activities] Registers the pool activities with the simulation while the extension is on and lets it read the placed extras; called once when the level's guests are made, so no path runs with an empty registry.</summary>
	internal void RegisterPoolActivities()
	{
		if ( Guests == null || !FeatureGates.Pools )
			return;
		// [EXT:pools-extras] the swim session reads the placed extras through the level's objects: rebuilt once per park turn and whenever an object is placed or removed (SetupObjects)
		Guests.PoolExtraSites ??= () =>
		{
			if ( poolExtraSitesTurn != Guests.ParkTurn || poolExtraSites == null )
				(poolExtraSites, poolExtraSitesTurn) = (PoolExtras.Sites( Objects ), Guests.ParkTurn);
			return poolExtraSites;
		};
		foreach ( var activity in PoolActivityRegistry.Create( PoolSites ) )
			Guests.RegisterPoolActivity( activity );
	}

	private bool swimSpritesTried;
	private bool swimSpritesPreloaded;
	/// <summary>[EXT:pools-swim] The swim sets decoded ahead on a worker (desktop only; null until <see cref="PreloadSwimSprites"/> ran).</summary>
	private List<GuestSpriteAtlas>? swimSpriteSets;

	/// <summary>
	/// [EXT:pools-swim] Loads the swim sets and decodes them on a worker so that the first pool frame does not stall: at level setup for a park with pool water, else when the first pool cell is painted. Once per level; with the
	/// extension off, or in the browser (<see cref="PoolSpriteDelivery.Delivered"/>: single-threaded, and the files may not be there yet) it does nothing, and the swimmer renderer loads and decodes when it is created.
	/// </summary>
	internal void PreloadSwimSprites()
	{
		if ( swimSpritesPreloaded || !FeatureGates.Pools || PoolSpriteDelivery.Delivered )
			return;
		swimSpritesPreloaded = true;
		var sets = swimSpriteSets = PoolSwimSprites.Load();
		System.Threading.Tasks.Task.Run( () => { foreach ( var atlas in sets ) _ = atlas.Pixels; } );
	}
	private List<PoolExtraSite>? poolExtraSites;
	private long poolExtraSitesTurn = -1;

	private GuestRenderer? CreateSwimmerRenderer( GuestSimulation guests, OriginalPark park )
	{
		var swim = swimSpriteSets is { Count: > 0 } ? swimSpriteSets : PoolSwimSprites.Load();
		if ( swim.Count == 0 )
		{
			// without swim sprites the swimmers are drawn by the normal guest renderer with their kid sprite (Level.cs: Draws), standing in the water
			Log.Warning( $"No swim sprite sets found in '{PoolSwimSprites.Directory}': swimmers are drawn with the normal guest sprites." );
			return null;
		}
		return new GuestRenderer( guests, park.Heightfield, swim )
		{
			// [EXT:pools-slides] a rider inside a closed tube is not drawn (presentation only)
			Draws = guest => (guest.IsSwimming || guest.InPoolActivity) && !PoolSlideActivity.IsInsideTube( guests, guest ),
			ExactDirections = true,
			SwimSlotFallback = true,
			BottomAnchoredSlots = PoolSwimSlots.BottomAnchored,
			RotatedSlots = PoolSwimSlots.Rotated,
			DepthPulledSlots = PoolSwimSlots.FeetAnchored.Contains,
			SlotChooser = guest => guest.InPoolActivity
				? ActivitySlot( guests.PoolActivityOf( guest )?.SpriteSlot( guest ) ?? PoolSwimSlots.TreadWater, guests.TimeSeconds, guest.Id )
				: (PoolSwimming.SpriteSlot( guest.SwimStroke ), (int)(guests.TimeSeconds * (guest.SwimStroke == 2 ? 3 : 6)) + guest.Id),
			// [EXT:pools-slides] a rider lying in a flume is drawn pulled towards the camera by the trough's depth, so the curved bed does not cut its lower body (presentation only)
			// [EXT:pools-extras] and a guest lying on a lounger, so the lounger's raised head end and the front of its seat do not hide it (presentation only)
			DepthPullChooser = guest => PoolSlideActivity.DepthPull( guests, guest ) + LoungerActivity.DepthPull( guests, guest ),
			FeetChooser = guest =>
			{
				var feet = GuestRenderer.CellToEngine( park.Heightfield, guest.DrawX, guest.DrawY );
				var height = guests.Grid.Cells.RawTypeAt( guest.CellX, guest.CellY ) == ParkCellTypes.PoolWater && PoolRenderer?.WaterLevelAt( guest.CellX, guest.CellY ) is { } level ? level : feet.Z;
				// [EXT:pools-waves] a swimmer bobs on the waves of its pool (the drawn height only; presentation only)
				if ( !guest.InPoolActivity )
					return new System.Numerics.Vector3( feet.X, feet.Y, height + PoolWaterEffects.Bob( guests, guest, PoolSites ) );
				// [EXT:pools-activities] an activity may raise or set the drawn height (a board, a slide, a ladder between the water and the deck); presentation only
				var region = guests.SwimRoomOf( guest ) is { } room ? guests.SwimRegionOf( room ) : null;
				var water = region != null && PoolRenderer?.WaterLevelAt( region.Access.X, region.Access.Y ) is { } surface ? surface : height;
				// [EXT:pools-waves] the water level rides the swell the surface is drawn with, so a swimmer in an activity floats on it too (the deck and a slide's track do not move)
				var rise = PoolWaterEffects.SurfaceRise( guests, guest, region, PoolSites );
				var inWater = guests.Grid.Cells.RawTypeAt( guest.CellX, guest.CellY ) == ParkCellTypes.PoolWater;
				return new System.Numerics.Vector3( feet.X, feet.Y, guests.PoolFeetHeight( guest, new PoolFeetHeights( inWater ? height + rise : height, water + rise, feet.Z ) ) );
			}
		};
	}

	/// <summary>[EXT:pools-render] A pool activity's slot and its animation step (<see cref="PoolSwimSlots.ActivityStep"/>); presentation only.</summary>
	private static (int Slot, int Step) ActivitySlot( int slot, double seconds, int guestId ) => (slot, PoolSwimSlots.ActivityStep( slot, seconds, guestId ));

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
		get => ParkCommandLineWriter.Unwrap( CellTool.Writer ) is QueueLineWriter queue ? queue.Ride : null;
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
		if ( Objects.Grid is OriginalParkGrid originalGrid )
			originalGrid.LiveCells = () => Guests?.Grid.Cells;
		Paths = new ParkPathBuilder( Guests.Grid.Cells, Guests.Grid, () => Park?.Economy, Objects.Grid, IsPathBlocked )
		{
			// [BIN:TPW-EXE:0x00534A91 queue neighbour sound] the writer plays cat_ui 139 only while game+36 (0x785A2C, the park's money) is clear: here, while a park economy runs
			Effects = new LinkEffects( RefreshTile, ( x, y ) =>
			{
				if ( Park != null )
					GameAudio.PlayUiAt( GameAudio.QueueJoinEvent, GameAudio.QueueNeighbourSoundPosition( x, y ) );
			} ),
			ObjectGivesWay = ( x, y ) => Objects.GivesWay( x, y, ParkObjects.PathOverwritePriority ),
			DemolishObject = DemolishObjectAt
		};
		// [BIN:TPW-EXE:0x0053481F queue edit] a path laid over a queue cell edits the queue of the ride that owns the cell (owner word): its queue is recomputed, its back cell stamped and the re-evaluation 0x004DE1F0 run
		Paths.QueueCellPaved += owner =>
		{
			if ( Guests is { } guests && guests.Attractions.OfType<RideVisitorBridge>().FirstOrDefault( ride => ride.HasQueue && ride.OwnerWord == owner ) is { } ride )
			{
				ride.RecomputeQueue( guests.Grid );
				ride.NotifyQueueEdited( guests.Grid );
			}
		};
		// [EXT:pools-tool] the pool builder and its paint tool share the cell map; they refuse every cell while the extension is off (FeatureGates.Pools)
		if ( !IsReadOnlyVisit )
		{
			PoolBuilder = new PoolBuilder( Guests.Grid.Cells, () => Park?.Economy, Objects.Grid, IsPathBlocked, IsStoodOn, PoolEraseBlock );
			PoolTool = new PoolTool( PoolBuilder );
			PoolBuilder.Painted += PreloadSwimSprites;
		}
		SetupPoolSlides();
		SetupPoolBridges();
		// [EXT:online-coop-commands] the bulldozer's drag clears each object and path cell through a command of its own
		AreaClearTool = IsReadOnlyVisit ? null : new AreaClearTool( ( x, y ) => Commands.Issue( new RemoveObjectAtCommand( x, y ) ).Accepted, () => Guests?.Grid.Cells,
			( x, y ) => Commands.Issue( new RemovePathCellCommand( x, y ) ) is { Value: CellBuildResult result } ? result : CellBuildResult.NotPath, TypesFootprints ? null : ( x, y ) => Objects.FindAt( x, y ) );
	}

	// Objects, fixed items (the gate and entrance included) and the sandbox Totem block paths like their original cell types do; an object of a lower
	// overwrite priority than the path's gives way instead (ParkPathBuilder.ObjectGivesWay).
	private bool IsPathBlocked( int x, int y ) => PoolBridges?.Reserves( x, y ) == true || Objects.FindAt( x, y ) != null || IsReservedByPrototype( x, y ) || (Guests != null && PoolSlides?.ReservesLand( x, y, Guests.Grid.Cells ) == true);

	/// <summary>How the cell map holds a path for the object build rule: none, a plain path (cleared by the placement) or a NoModify one (refused).</summary>
	internal static ObjectPathCell? ObjectPathAt( ParkCellMap cells, int x, int y )
	{
		if ( !cells.InBounds( x, y ) )
			return null;
		if ( cells.TypeAt( x, y ) != ParkCellType.Path )
			return ObjectPathCell.None;
		return cells.FlagsAt( x, y ).HasFlag( ParkCellFlags.NoModify ) ? ObjectPathCell.FixedPath : ObjectPathCell.Path;
	}

	/// <summary>Demolishes the object at (x, y) a path or queue cell is laid over (tp.exe's cell op 0x87: the whole object, sold for its scrap value).</summary>
	private void DemolishObjectAt( int x, int y )
	{
		if ( Objects.FindAt( x, y ) is { } item )
			Objects.Remove( item );
	}

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
		// [EXT:online-coop-commands] the tool's previews read the path builder; each committed line is a command
		CellTool.Enter( new ParkCommandLineWriter( Paths, Commands, -1 ), null );
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
		// [BIN:TPW-EXE:0x00530120 corner walk] the tool's entry walks the queue into the stack of corner points (0x81B740) and sets the line start to its last point; the tool's undo then takes the queue back segment by segment (Back 0x52FE50)
		// a queue of its front cell alone (a ride just placed) keeps the one-point stack the placement leaves, so its first click lays from the front cell
		var stack = Guests.Grid.IsQueue( front.X, front.Y ) && ride.QueueCells.Count >= 2 ? QueuePaths.CornerStack( Guests.Grid, ride ) : null;
		if ( stack is not { Count: >= 2 } || stack[^1] != start )
			stack = null;
		// [EXT:online-coop-commands] the tool's previews read the ride's queue writer; each committed line is a command
		CellTool.Enter( new ParkCommandLineWriter( CreateQueueWriter( ride ), Commands, ride.AttractionId ), start,
			stack, stack == null ? null : QueuePaths.StackSegments( ride, stack ) );
		return true;
	}

	/// <summary>The queue tool's line writer for <paramref name="ride"/>: its checks, its per-cell build with <c>Costs.QueueCell</c> and its removal.</summary>
	private QueueLineWriter CreateQueueWriter( RideVisitorBridge ride ) =>
		new QueueLineWriter( Guests!.Grid, ride, ( x, y, pending ) => CheckQueueCell( ride, x, y, pending ),
			( x, y ) => BuildQueueCell( ride, x, y ), RemoveQueueCell, () => Park?.Economy.CellCost( CellPurchase.Queue ) ?? 0 )
		.WithNativeReducer( new QueueSegmentReducer( Guests!.Grid.Cells, ride.OwnerWord,
			( x, y ) => IsReadOnlyVisit || IsQueueBlocked( x, y ) || Objects.FindAt( x, y ) != null,
			() => Park == null || Park.Economy.TrySpendCell( CellPurchase.Queue ) == ParkEconomy.PurchaseResult.Ok,
			pending => Park == null || Park.Economy.CanPreviewLine( pending ),
			() => Park?.Economy.CellCost( CellPurchase.Queue ) ?? 0,
			RefundNativeQueueOwner,
			() => { ride.RecomputeQueue( Guests.Grid ); ride.NotifyQueueEdited( Guests.Grid ); },
			new LinkEffects( RefreshTile ), removed => { Guests.SetDownOffQueueCells( removed, ride ); AfterAccessCellRemoved( removed ); },
			() => ResolveQueueEntrance( Objects, Guests.Grid.Cells, ride.OwnerWord ) ) );

	/// <summary>Live shape reconstruction of native mEntryPos (+0x36); never the bridge's outside-cell adaptation.</summary>
	internal static (int X, int Y)? ResolveQueueEntrance( ParkObjects objects, ParkCellMap cells, ushort owner )
	{
		if ( owner == 0 ) return null;
		var item = objects.Objects.FirstOrDefault( item => !item.IsDeleted && OwnerWord( item ) == owner && item.Entry.HasQueue );
		if ( item == null ) return null;
		ObjectAccessPoint? entrance = null;
		foreach ( var point in item.AccessPoints )
			if ( point.Kind == ObjectCellKind.Entrance ) entrance = point;
		return entrance is { } entry && cells.InBounds( entry.X, entry.Y )
			&& cells.RawTypeAt( entry.X, entry.Y ) == ParkCellTypes.Entrance && cells.OwnerAt( entry.X, entry.Y ) == owner
			? (entry.X, entry.Y) : null;
	}

	private void RefundNativeQueueOwner( ushort owner )
	{
		if ( owner == 0 || Guests == null || Park == null ) return;
		var x = (owner - 1) % 128; var y = (owner - 1) / 128;
		if ( !Guests.Grid.Cells.InBounds( x, y ) || Guests.Grid.Cells.RawTypeAt( x, y ) == 0 ) return;
		var item = Objects.Objects.FirstOrDefault( item => OwnerWord( item ) == owner );
		if ( item != null && GetEconomyInstance( item ) is int instance ) Park.Economy.RefundQueueCellNative( instance );
	}


	/// <summary>
	/// Removes the queue cell at (x, y): with its owner (<see cref="QueueRideAt"/>) that cell and every cell behind it
	/// (<see cref="QueuePaths.RemoveFrom"/>); a queue cell no ride's queue reaches (a stray piece, such as one an older save left)
	/// goes alone. Returns the cells removed; <paramref name="owner"/> is the ride whose queue lost them, null for a stray cell.
	/// </summary>
	// [BIN:TPW-EXE:0x005367A0 ClearCell queue case] the original clears a queue cell by itself, whatever ride owns it; OpenTPW's queue is the line its ride walks from the entrance, so an owned cell takes the cells behind it along and a stray cell, outside every such line, goes alone
	internal static int RemoveQueueCellAt( GuestPathGrid grid, IEnumerable<IRideVisitorBridge> attractions, int x, int y, out RideVisitorBridge? owner, bool removeFixed = false ) =>
		RemoveQueueCellAt( grid, attractions, x, y, out owner, out _, removeFixed );

	/// <summary>
	/// <see cref="RemoveQueueCellAt(GuestPathGrid, IEnumerable{IRideVisitorBridge}, int, int, out RideVisitorBridge?, bool)"/> that also reports the cells it emptied. With
	/// <paramref name="removeFixed"/> a NoModify queue cell (the front a placement laid) goes too, as [EXT:removable-access-cells] allows.
	/// </summary>
	internal static int RemoveQueueCellAt( GuestPathGrid grid, IEnumerable<IRideVisitorBridge> attractions, int x, int y, out RideVisitorBridge? owner, out IReadOnlySet<(int X, int Y)> removedCells, bool removeFixed = false )
	{
		removedCells = new HashSet<(int X, int Y)>();
		owner = QueueRideAt( grid, attractions, x, y );
		if ( owner == null && grid.IsQueue( x, y ) )
		{
			// The rides' queue lines are cached: walk them again before a cell counts as stray.
			foreach ( var ride in attractions.OfType<RideVisitorBridge>().Where( item => item.HasQueue ) )
			{
				if ( ride.RecomputeQueue( grid ) )
					ride.NotifyQueueEdited();
			}
			owner = QueueRideAt( grid, attractions, x, y );
		}
		if ( owner != null )
		{
			// the cells RemoveFrom takes: the cell and the chain behind it, or a free-standing cell alone
			owner.RecomputeQueue( grid );
			var taken = owner.QueueCells.Contains( (x, y) ) ? owner.QueueCells.SkipWhile( cell => cell != (x, y) ).ToHashSet() : new HashSet<(int X, int Y)> { (x, y) };
			var count = QueuePaths.RemoveFrom( grid, owner, x, y, removeFixed );
			if ( count > 0 )
				removedCells = taken;
			return count;
		}
		if ( !grid.IsQueue( x, y ) )
			return 0;
		grid.ClearQueue( x, y );
		grid.Cells.SetFlags( x, y, grid.Cells.FlagsAt( x, y ) & ~ParkCellFlags.NoModify );
		removedCells = new HashSet<(int X, int Y)> { (x, y) };
		return 1;
	}

	/// <summary>The HasQueue ride whose queue holds (x, y) (the owner of a queue cell), or null: the ride whose chain reaches the cell, else the ride the cell's owner word names (a free-standing cell).</summary>
	internal static RideVisitorBridge? QueueRideAt( GuestPathGrid grid, IEnumerable<IRideVisitorBridge> attractions, int x, int y )
	{
		if ( !grid.IsQueue( x, y ) )
			return null;
		var rides = attractions.OfType<RideVisitorBridge>().Where( item => item.HasQueue ).ToArray();
		var owner = grid.Cells.OwnerAt( x, y );
		return rides.FirstOrDefault( item => item.QueueCells.Contains( (x, y) ) ) ?? (owner == 0 ? null : rides.FirstOrDefault( item => item.OwnerWord == owner ));
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

	/// <summary>What Backspace with no build tool on takes off a cell (<see cref="Level.BacktrackAt"/>).</summary>
	internal enum BacktrackTarget { None, Path, Queue }

	/// <summary>
	/// Backspace's target at (x, y): a path cell (the original's backtrack), a queue cell (OpenTPW's extension), else none.
	/// </summary>
	internal static BacktrackTarget BacktrackTargetAt( ParkCellMap cells, int x, int y ) => cells.TypeAt( x, y ) switch
	{
		ParkCellType.Path => BacktrackTarget.Path,
		ParkCellType.Queue => BacktrackTarget.Queue,
		_ => BacktrackTarget.None
	};

	/// <summary>
	/// Backspace in the park view with no build tool on (<see cref="GameAudio.BacktrackEvent"/>): over a path cell it plays the
	/// backtrack sound, then takes one placement off that cell, which goes only when it was laid once; the sound plays even
	/// when the cell stays (a fixed or a re-laid cell). Over a queue cell it plays the same sound and removes that cell and
	/// every cell behind it, with the remove tool's refund (<see cref="RemoveQueueCell"/>). False on any other cell, so the
	/// key does nothing there.
	/// </summary>
	// [BIN:TPW-EXE:0x40BE8B backtrack click] the key handler clicks the cell in mode 0x32, whose one-cell LayLine (0x536100) calls SetCell(0x32, 0, 1) and so ClearCell 0x5367A0 with (0, 1): the counted removal, not the forced one
	// [EXT:queue-backtrack] tp.exe's key handler backtracks only a type-1 (path) cell: it tests the tile's +8 through 0x536310 at 0x40BE3C and does nothing over a queue cell, whose cells the original takes back only with Backspace inside the queue tool (Back 0x52FE50, the segment just laid); OpenTPW also takes a queue back from the cursor, as the remove tool does (QueuePaths.RemoveFrom, so the queue stays one line from the ride)
	public bool BacktrackAt( int x, int y )
	{
		if ( IsReadOnlyVisit || Paths == null )
			return false;
		var target = BacktrackTargetAt( Paths.Cells, x, y );
		if ( target == BacktrackTarget.None )
			return false;
		GameAudio.PlayUiAt( GameAudio.BacktrackEvent, CellSoundPosition( x, y ) );
		if ( target == BacktrackTarget.Path )
		{
			if ( Paths.RemoveOnce( x, y ) == CellBuildResult.Ok )
				LastActionMessage = "Removed a path cell.";
		}
		else if ( RemoveQueueCell( x, y ) is var removed and > 0 )
			LastActionMessage = removed == 1 ? "Removed a queue cell." : $"Removed {removed} queue cells.";
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
		if ( PoolBridges?.Reserves( x, y ) == true ) return CellBuildResult.Occupied;
		// [EXT:removable-access-cells] the path cell the placement laid outside an entrance or exit (flag 0x20) can be removed, where tp.exe's cell clear keeps a NoModify cell that has neighbours (the forced clear 0x5367A0(0, 0) refuses it with "Removing path cell with no neighbours but NOMODIFY set" otherwise); the map start's initial path stays fixed
		var access = IsAccessCell( x, y );
		var result = access ? Paths.RemoveFixed( x, y ) : Paths.Remove( x, y );
		if ( result == CellBuildResult.Ok )
		{
			LastActionMessage = "Removed a path cell.";
			if ( access )
				AfterAccessCellRemoved( new HashSet<(int X, int Y)> { (x, y) } );
		}
		return result;
	}

	/// <summary>Whether (x, y) is a cell outside the entrance or exit of a placed object (the cells its placement lays, <see cref="ObjectAccessCells"/>).</summary>
	private bool IsAccessCell( int x, int y ) =>
		Objects.Objects.Any( item => ObjectAccessCells.IsOutsideCell( item.AccessPoints, x, y ) );

	/// <summary>
	/// After cells a placement laid were removed: guests standing on them are set down, and each ride whose entrance or exit resolved to one resolves it again, which falls back to the
	/// nearest walkable path as for a cell nothing laid (<see cref="ResolveVisitorCells"/>).
	/// </summary>
	private void AfterAccessCellRemoved( IReadOnlySet<(int X, int Y)> removed )
	{
		if ( Guests == null )
			return;
		Guests.SetDownOffCells( removed );
		RefreshVisitorCells( Objects.Objects.Where( item => item.Runtime.IsAttraction ).Select( item => (item.AccessPoints, item.Runtime.Visitors) ), Guests.Grid, removed );
	}

	/// <summary>
	/// Resolves the entrance and exit cells again of each ride that used one of the <paramref name="removed"/> cells (as its entrance, exit or outside cell), and recomputes its queue:
	/// the entrance falls back to the nearest walkable path as for a ride nothing laid cells for. A ride the grid has no path for keeps its cells (the guests' own checks drop a goal they cannot reach).
	/// </summary>
	internal static void RefreshVisitorCells( IEnumerable<(IEnumerable<ObjectAccessPoint> Points, RideVisitorBridge Bridge)> rides, GuestPathGrid grid, IReadOnlySet<(int X, int Y)> removed )
	{
		foreach ( var (points, bridge) in rides )
		{
			var access = points.ToArray();
			if ( !bridge.HasCells )
				continue;
			var affected = removed.Contains( bridge.EntranceCell ) || removed.Contains( bridge.ExitCell ) || access.Any( point => removed.Contains( (point.OutsideX, point.OutsideY) ) );
			if ( !affected || ResolveVisitorCells( access, grid ) is not { } cells )
				continue;
			bridge.EntranceCell = cells.Entrance;
			bridge.ExitCell = cells.Exit;
			bridge.RecomputeQueue( grid );
		}
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
		var result = BuildQueueCell( Guests.Grid, Park?.Economy, ride, x, y, IsQueueBlocked, out var message, QueueGivesWay, DemolishObjectAt );
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
	/// <param name="givesWay">An object the queue may cover (tp.exe's overwrite priority below the queue tool's 3); null covers none.</param>
	/// <param name="demolish">Demolishes that object when the cell is laid.</param>
	internal static QueueBuildResult BuildQueueCell( GuestPathGrid grid, ParkEconomy? economy, RideVisitorBridge ride, int x, int y, Func<int, int, bool> isBlocked, out string message,
		Func<int, int, bool>? givesWay = null, Action<int, int>? demolish = null )
	{
		// Bring the queue up to date before charging: a grid edit since the last recompute would otherwise pass the
		// check here, be charged, and then be refused by TryExtend's own recompute.
		ride.RecomputeQueue( grid );
		// The commit's money test is SetCellType's alone (TrySpendCell below); the validator's preview test does not run on a commit (PATH-plan §3.3).
		var check = CheckQueueCell( grid, null, ride, x, y, Array.Empty<(int X, int Y)>(), isBlocked, givesWay );
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
		var result = QueuePaths.TryExtend( grid, ride, x, y, isBlocked, givesWay, demolish );
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
		return CheckQueueCell( Guests.Grid, Park?.Economy, ride, x, y, pending, IsQueueBlocked, QueueGivesWay );
	}

	/// <summary>
	/// The queue tool's per-cell check (also used by the static <c>BuildQueueCell</c>):
	/// <see cref="QueuePaths.CheckExtend"/> after <paramref name="pending"/>, then the preview's money test against
	/// <paramref name="economy"/> (none: no money test; the commit passes none and relies on <see cref="ParkEconomy.TrySpendCell"/>).
	/// </summary>
	internal static QueueBuildResult CheckQueueCell( GuestPathGrid grid, ParkEconomy? economy, RideVisitorBridge ride, int x, int y, IReadOnlyList<(int X, int Y)> pending, Func<int, int, bool> isBlocked,
		Func<int, int, bool>? givesWay = null )
	{
		var check = QueuePaths.CheckExtend( grid, ride, x, y, pending, isBlocked, givesWay );
		if ( check != QueueBuildResult.Ok )
			return check;
		// [BIN:STP-PPC:0x10084550 placement validator] the preview refuses when the 32-bit pending line cost (the pending cells and this one) is greater than the balance; a zero sum passes
		if ( economy != null && !economy.CanPreviewCells( CellPurchase.Queue, pending.Count + 1 ) )
			return QueueBuildResult.Refused;
		return QueueBuildResult.Ok;
	}

	/// <summary>
	/// Removes the queue cell at (x, y) and every cell behind it (a stray queue cell alone, <see cref="RemoveQueueCellAt"/>). Each
	/// removed cell of a ride's queue refunds <c>Costs.QueueCell</c> × the ride's scrap percentage / 100 (<see cref="ParkEconomy.RefundQueueCell"/>); a stray cell refunds nothing.
	/// </summary>
	public int RemoveQueueCell( int x, int y )
	{
		if ( IsReadOnlyVisit || Guests == null )
			return 0;
		// [EXT:removable-access-cells] the queue front the placement laid (flag 0x20) goes with the queue behind it, where tp.exe's cell clear keeps a NoModify cell unless it is forced (0x5367D4..0x5367E0)
		var removed = RemoveQueueCellAt( Guests.Grid, Guests.Attractions, x, y, out var ride, out var removedCells, removeFixed: true );
		if ( removed > 0 )
		{
			// the guests standing on the emptied cells leave the queue on the nearest path; the ride's entrance resolves again when it used one of them
			Guests.SetDownOffQueueCells( removedCells, ride );
			AfterAccessCellRemoved( removedCells );
		}
		if ( ride == null )
			return removed;
		var owner = Objects.Objects.FirstOrDefault( item => item.Visitors == ride );
		if ( Park != null && owner != null && GetEconomyInstance( owner ) is int instance )
		{
			for ( var cell = 0; cell < removed; cell++ )
				Park.Economy.RefundQueueCell( instance );
		}
		return removed;
	}

	private readonly HashSet<OriginalObject> demolishedQueues = new();

	/// <summary>
	/// The money side of a ride's demolition for its queue, while the cells are still laid: each queue cell refunds <c>Costs.QueueCell</c> × the ride's scrap percentage
	/// / 100 and the demolition then debits one such amount (<see cref="ParkEconomy.ChargeQueueDemolition"/>). A park load or clear removes nothing through here.
	/// </summary>
	// [BIN:TPW-EXE:0x00527F87 ride demolition queue] a type with a queue (Info.HasQueue, def +0x40, 0x527F87..0x527F93) fills the corner stack with 0x530120 (0x527F9A), enters mode 3 (0x527FA5) with the forced-clear flag 0x81D7A8 set (0x527FAA) and, when the ride's own word +0x40 is above 0 (0x527FB4..0x527FBC; +0x40 is the count of the chain cells the queue re-evaluation 0x4DE130 visits from the entrance's outside cell, that cell included whatever its type, so it is 1 for a ride whose front cell is a path cell, a case in which the original still debits one cell and OpenTPW charges nothing, QUEUE-015), calls Back 0x52FE50 until it returns 0 (0x527FBE..0x527FDA): every cell goes and credits its refund, and 0x527FDC..0x528007 then debits one cell's refund; the ride's entrance cell is not part of the stack
	private void SettleDemolishedQueue( OriginalObject item )
	{
		if ( IsReadOnlyVisit || Guests == null || !item.Entry.HasQueue || item.Visitors is not RideVisitorBridge ride )
			return;
		ride.RecomputeQueue( Guests.Grid );
		if ( ride.QueueFrontCell is not { } front || !Guests.Grid.IsQueue( front.X, front.Y ) )
			return;
		demolishedQueues.Add( item );
		if ( Park?.Economy is not { } economy || GetEconomyInstance( item ) is not int instance )
			return;
		for ( var cell = 0; cell < ride.QueueCells.Count; cell++ )
			economy.RefundQueueCell( instance );
		economy.ChargeQueueDemolition( instance );
	}

	/// <summary>Takes the queue cells of a demolished ride off the map (<see cref="QueuePaths.ClearAll"/>), once its guests have been set down.</summary>
	private void ClearDemolishedQueue( OriginalObject item )
	{
		if ( demolishedQueues.Remove( item ) && Guests != null && item.Visitors is RideVisitorBridge ride )
			QueuePaths.ClearAll( Guests.Grid, ride );
	}

	// [APPROX:QUEUE-012] queue cells need terrain the object build rule allows (Objects.Grid.CheckTerrain: the map start's cell types and the Easymode save's occupied cells), no object footprint of the queue tool's overwrite priority 3 or more (a lower one is demolished, QueueGivesWay) and owned land; tp.exe's cell validator 0x00535670 for the queue tool refuses a cell whose flag byte +0x0E has bit 0x40 (the park's land, ParkCellMap.Land), compares an object cell's owner priority with 0x818C10 = 3 (0x535D66), allows empty cells, lets a queue replace a path cell unless NoModify (+0x0E bit 0x20) and reads no MAP terrain bit (map setup turns inside water, blocked and walkway cells into type 0) — evidence needed: none for the validator; the save's occupied cells stand in for its cell types (ECON-050)
	// [BIN:TPW-EXE:0x5357c7 placement validator] a cell with flag 0x40 is refused for a queue as for a path or an object
	private bool IsQueueBlocked( int x, int y ) =>
		!QueueTerrainAllows( x, y ) || (Objects.FindAt( x, y ) != null && !QueueGivesWay( x, y )) || IsReservedByPrototype( x, y )
		|| (Guests?.Grid.Cells is { } cells && cells.InBounds( x, y ) && cells.FlagsAt( x, y ).HasFlag( ParkCellFlags.Unowned ))
		// [EXT:pools-slides] a slide's track keeps its land cells for its pylons
		|| (Guests != null && PoolSlides?.ReservesLand( x, y, Guests.Grid.Cells ) == true);

	/// <summary>The terrain rule for a queue cell; its path cells (the save's and the map start's) are the live cell map's to judge, as for objects.</summary>
	private bool QueueTerrainAllows( int x, int y ) => Objects.Grid.CheckTerrain( x, y ) switch
	{
		OriginalPlacementResult.Allowed => true,
		OriginalPlacementResult.Path => Guests?.Grid.Cells is { } cells && ObjectPathAt( cells, x, y ) == ObjectPathCell.None,
		_ => false
	};

	/// <summary>An object a queue cell may cover and demolish: its overwrite priority is below the queue tool's (tp.exe <c>0x818C10</c> = 3).</summary>
	private bool QueueGivesWay( int x, int y ) => Objects.GivesWay( x, y, ParkObjects.QueueOverwritePriority );

	// [BIN:TPW-EXE:0x0052977F access cells] the placement lays the cells outside the entrance and exit itself (ObjectAccessCells: the entrance's queue cell for a type with Info.HasQueue, else a path cell; the exit's path cell), free of charge and NoModify; Easymode's eight rides carry exactly these cells (18 cells compared)
	// [BIN:TPW-EXE:0x0052FAB0 queue option] the option 0x81B0CC is set by the build tool from the type's +0x40 (0x52FA90 at 0x527986 and 0x46D644) and is 1 after SetMode (0x52F139); 0x8187F8 is the upgrade selection (0x52F880), which no OpenTPW build uses
	// [BIN:TPW-EXE:0x00534115 bridge ops] the refresh ops after the laid cell run 0x81 (the tile refresh 0x5365D0, RefreshTile) and 0x85 and 0x86, which act on a track cell of the second layer (0x53AD30 first) and so do nothing for a cell that holds none
	// A cell nothing laid (an imported or restored park, Theme Park Inc) keeps the nearest walkable cell as the guests' entrance, which their route graph needs to be a path cell (QUEUE-003, WALK-005).
	internal static (int X, int Y)? WalkableNear( GuestPathGrid grid, int x, int y ) =>
		grid.IsWalkable( x, y ) || grid.IsQueue( x, y ) ? (x, y) : FindRideEntrance( grid, x, y, x, y );

	public void RotateBuild() => BuildRotation = (BuildRotation + 90) % 360;

	/// <summary>
	/// Whether the Move tool may pick <paramref name="item"/> up: always, except for a kart or water ride (type track word 1 or 2), whose first track tiles beside the station must each hold exactly one arm.
	/// </summary>
	// [BIN:TPW-EXE:0x52c2b0 move enabled test] the object window's refresh (0x4ad6df) enables b_move with this test: 1 unless the type's track word +0x9c is 1 or 2; then the track-layer cells at the thing's cell plus (4, 1), (1, -5), (-5, -2) or (-2, 4) (the station's exit tile, 0x52c2e8..0x52c35c) and plus (-2, 1), (1, 1), (1, -2) or (-2, -2) (its entry tile, 0x52c392..0x52c405) by the angle 0, 90, 180 or 270 must each answer 1 to 0x522790, the count of the set bits of the cell's connection byte (+0xC); the calls of 0x4d8c00 between them do not change the answer (its result is unused)
	public bool CanMove( OriginalObject item )
	{
		var trackType = item.Entry.Settings.GetInt( "Bumper.WhichTrackType", 0 );
		if ( trackType is not (1 or 2) || BuilderOf( item ) is not { } builder )
			return true;
		return MoveEnabled( trackType, item.Placement.X, item.Placement.Y, item.Placement.Rotation, ( x, y ) => System.Numerics.BitOperations.PopCount( builder.Tracks.Get( x, y ).Mask ) );
	}

	/// <summary>The enabled test of the Move tool for a type with track word <paramref name="trackType"/> placed at (<paramref name="x"/>, <paramref name="y"/>) turned <paramref name="rotation"/>: <paramref name="armsAt"/> gives a track cell's set connection bits.</summary>
	internal static bool MoveEnabled( int trackType, int x, int y, int rotation, Func<int, int, int> armsAt )
	{
		if ( trackType is not (1 or 2) )
			return true;
		var ((exitX, exitY), (entryX, entryY)) = rotation switch
		{
			0 => ((4, 1), (-2, 1)),
			90 => ((1, -5), (1, 1)),
			180 => ((-5, -2), (1, -2)),
			270 => ((-2, 4), (-2, -2)),
			_ => ((0, 0), (0, 0))
		};
		return armsAt( x + exitX, y + exitY ) == 1 && armsAt( x + entryX, y + entryY ) == 1;
	}


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
		if ( !TryGetGroundHit( mousePosition, viewportSize, out var ground ) )
			return false;
		return TryGetGridCell( ground.X, ground.Y, out x, out y );
	}

	/// <summary>
	/// The point under the cursor: on an original level the ray's nearest hit on the terrain heightfield (<see cref="TerrainRayPick"/>), in the generic sandbox the Z = 0 plane.
	/// </summary>
	// [BIN:TPW-EXE:0x45d560 hover cell] the cell under the cursor is the ray's hit point divided by the cell size (x, z) and clamped to the grid; the frame loop 0x54e2b0 refreshes it every frame from the pick 0x45bf90
	// [EXT:build-cursor-ground-plane] the generic sandbox has no heightfield: its cursor ray hits the Z = 0 plane
	public bool TryGetGroundHit( System.Numerics.Vector2 mousePosition, System.Numerics.Vector2 viewportSize, out Vector3 point ) =>
		TryGetGroundHit( mousePosition, viewportSize, Camera.ViewMatrix, Camera.ProjMatrix, out point );

	internal bool TryGetGroundHit( System.Numerics.Vector2 mousePosition, System.Numerics.Vector2 viewportSize, System.Numerics.Matrix4x4 view, System.Numerics.Matrix4x4 projection, out Vector3 point )
	{
		// the HUD, the tools and the visitor pick ask for the same point several times a frame: the last answer stands while the frame, the ray and the objects are the same
		var key = (mousePosition, viewportSize, view, projection, Time.Now, Objects?.Version ?? -1, PickShapeOf);
		if ( lastGroundHit is { } last && last.Key.Equals( key ) )
		{
			point = last.Point;
			return last.Found;
		}
		var found = ComputeGroundHit( mousePosition, viewportSize, view, projection, out point );
		lastGroundHit = (key, found, point);
		return found;
	}

	private ((System.Numerics.Vector2, System.Numerics.Vector2, System.Numerics.Matrix4x4, System.Numerics.Matrix4x4, float, int, Func<OriginalObject, IObjectPickShape?>?) Key, bool Found, Vector3 Point)? lastGroundHit;

	private bool ComputeGroundHit( System.Numerics.Vector2 mousePosition, System.Numerics.Vector2 viewportSize, System.Numerics.Matrix4x4 view, System.Numerics.Matrix4x4 projection, out Vector3 point )
	{
		point = default;
		if ( !ParkPlacement.TryGetMouseRay( mousePosition, viewportSize, view, projection, out var origin, out var direction ) )
			return false;
		System.Numerics.Vector3? terrain = null;
		if ( OriginalPark is not { } park )
		{
			if ( ParkPlacement.TryGetGroundPoint( mousePosition, viewportSize, view, projection, out var plane ) )
				terrain = plane.GetSystemVector3();
		}
		else if ( TerrainRayPick.TryPick( park.Heightfield, origin, direction, out var hit ) )
			terrain = hit;
		// the object half of the pick: the first object model the ray meets from the camera takes the point when it is nearer than the terrain (ObjectRayPick)
		// [EXT:build-cursor-ground-plane] the generic sandbox's Z = 0 plane stands in for the terrain hit there
		if ( TryPickObject( origin, direction, view, terrain, out var objectHit ) )
		{
			point = objectHit;
			return true;
		}
		if ( terrain is not { } ground )
			return false;
		point = ground;
		return true;
	}

	/// <summary>The cursor point on an object's model (<see cref="ObjectRayPick"/>) when the ray from the camera meets one before the terrain point.</summary>
	private bool TryPickObject( System.Numerics.Vector3 origin, System.Numerics.Vector3 direction, System.Numerics.Matrix4x4 view, System.Numerics.Vector3? terrain, out System.Numerics.Vector3 hit )
	{
		hit = default;
		if ( Objects is not { } objects || objects.Objects.Count == 0 )
			return false;
		// the walk starts at the camera's eye (tp.exe 0x790aa0), the view's inverse translation
		if ( !System.Numerics.Matrix4x4.Invert( view, out var inverse ) )
			return false;
		var eye = inverse.Translation;
		var grid = new ObjectRayPick.Grid( objects.Grid.Origin, ObjectPlacement.CellSize, objects.Grid.Width, objects.Grid.Height );
		return ObjectRayPick.TryPick( eye, direction, terrain, grid, PickOwnerAt, out hit, out _ );
	}

	/// <summary>
	/// The owner the cell lookup names for the cursor pick's walk (tp.exe 0x452ae0): none on a track tile's second-layer piece (its piece model is the instance
	/// there), else the placed object on the cell when it has a height map, with the height the map gives it there.
	/// </summary>
	private ObjectRayPick.Owner? PickOwnerAt( int x, int y )
	{
		if ( Objects.FindAt( x, y ) is not { IsDeleted: false } item || HasTrackPieceInstance( x, y ) || ObjectGround( item, x, y ) is not { } top )
			return null;
		var shape = PickShapeOf?.Invoke( item ) ?? item;
		return new ObjectRayPick.Owner( shape, top * ObjectPlacement.ModelScale );
	}

	/// <summary>Whether a kart or water track tile's piece instance stands on the cell (the second layer 0x53bf30 asks first): an origin tile with a piece that is no add-on's.</summary>
	// [BIN:TPW-EXE:0x00452A60 add-on surface] a track tile whose tile holds no piece instance (an add-on's tile) leaves the cell to its owner thing
	private bool HasTrackPieceInstance( int x, int y )
	{
		if ( waterTracks?.Tracks is not { } layer || !layer.InBounds( x, y ) || layer.Get( x, y ) is not { Type: not 0 } cell )
			return false;
		var (originX, originY) = cell.IsExtension ? layer.CellOf( cell.Parent ) : (x, y);
		var tile = layer.Get( originX, originY );
		return tile.IsOrigin && tile.Piece != 0 && (tile.Flags & TrackCell.FlagAddOn) == 0;
	}

	/// <summary>The model the cursor pick tests for an object; null: the object's own (tests stand in a shape for an object without drawn geometry).</summary>
	internal Func<OriginalObject, IObjectPickShape?>? PickShapeOf { get; set; }

	/// <summary>
	/// Screen position of cell (x, y)'s centre on the surface the cursor picks (the terrain's mean corner height on an original level, Z = 0 in the sandbox; for build ghosts).
	/// False for a cell outside the object grid, which has no ground to project (the guest grid can be larger than the level's heightfield).
	/// </summary>
	public bool TryProjectCell( int x, int y, System.Numerics.Vector2 viewportSize, out System.Numerics.Vector2 screen )
	{
		screen = default;
		if ( x < 0 || y < 0 || x >= Objects.Grid.Width || y >= Objects.Grid.Height )
			return false;
		var origin = Objects.Grid.Origin;
		var point = new System.Numerics.Vector4( origin.X + (x + 0.5f) * ObjectPlacement.CellSize, origin.Y + (y + 0.5f) * ObjectPlacement.CellSize, Objects.Grid.GetCellHeight( x, y ), 1 );
		var clip = System.Numerics.Vector4.Transform( point, Camera.ViewMatrix * Camera.ProjMatrix );
		if ( clip.W <= 0 )
			return false;
		screen = new System.Numerics.Vector2( (clip.X / clip.W + 1) / 2 * viewportSize.X, (1 - clip.Y / clip.W) / 2 * viewportSize.Y );
		return float.IsFinite( screen.X ) && float.IsFinite( screen.Y );
	}

	/// <summary>
	/// The continuous cell coordinates of the terrain point under the pointer (cell units, 0.5 = a cell's centre), clamped to the grid; false when the ray misses the ground.
	/// Unlike <see cref="TryGetGridCell(System.Numerics.Vector2, System.Numerics.Vector2, out int, out int)"/> it does not snap to a cell and does not need a cell under the pointer.
	/// </summary>
	// [BIN:TPW-EXE:0x45d610 mouse ground point] the staff tools read the exact world point under the mouse (0x7a1c90: x, y, z floats), not the hover cell
	public bool TryGetGroundCell( System.Numerics.Vector2 mousePosition, System.Numerics.Vector2 viewportSize, out float cellX, out float cellY )
	{
		cellX = cellY = 0;
		if ( !TryGetGroundHit( mousePosition, viewportSize, out var ground ) || !float.IsFinite( ground.X ) || !float.IsFinite( ground.Y ) )
			return false;
		var origin = Objects.Grid.Origin;
		cellX = Math.Clamp( (ground.X - origin.X) / ObjectPlacement.CellSize, 0f, Objects.Grid.Width - 0.001f );
		cellY = Math.Clamp( (ground.Y - origin.Y) / ObjectPlacement.CellSize, 0f, Objects.Grid.Height - 0.001f );
		return true;
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
	// RIDES-021 (tagged at ObjectRayPick): the clicked cell is the cell of the cursor ray's nearest hit, the terrain's or the first object model's (ObjectRayPick); the walk's height window, the track tiles'
	// second-layer piece instances and the instances' sub-instances are approximated or left out (see the register)
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
			// [EXT:pools-slides] a slide tower turned away from the pool beside it gets its own message
			LastRefusalTowerFacesAway = result == OriginalPlacementResult.NeedsPool && PoolSlideTowers.IsSlideTower( entry ) && Guests?.Grid.Cells is { } placeCells
				&& PoolSlideTowers.WaterBesideOtherSide( entry.Shape, placeCells, anchor.X, anchor.Y, rotation );
			LastActionMessage = $"Cannot build {entry.DisplayName} here: {result}.";
			return null;
		}
		ParkObjectState? bought = null;
		if ( Park != null )
		{
			// a command's placement says itself whether it is the Move tool's ([EXT:online-coop-commands]: the executor reads no tool state)
			var purchase = Park.Economy.TryBuild( entry.InfoId, out bought, commandPlacementMoving ?? IsMoving );
			if ( purchase != ParkEconomy.PurchaseResult.Ok )
			{
				LastActionMessage = $"Cannot build {entry.DisplayName}: {purchase}.";
				return null;
			}
		}
		// [BIN:TPW-EXE:0x004DB712 built ride closes] the constructor opens the object (open word +0x68 = 1, 0x4DB3B9) and then, for a type with Info.HasQueue (flag 8 at 0x4DB420), logs "Object %d Closing", clears the open word and writes VAR_RIDECLOSED = 1 (script variable 6, 0x55A070/0x55A0B0), so a built ride with a queue waits closed until the player opens it
		layingAccessCells = true;
		OriginalObject placed;
		// the object is in the park from Register on, and so are the economy record bought for it: whatever throws after that (an ObjectPlaced handler, the effect, the cue) must not leave the object unbound
		OriginalObject? registered = null;
		try
		{
			try
			{
				placed = Objects.TryPlace( entry, anchor.X, anchor.Y, rotation, out _, open: BuiltObjectStartsOpen( entry ), registered: item => registered = item )!;
			}
			finally
			{
				layingAccessCells = false;
			}
			// [BIN:TPW-EXE:0x558e7c build effect] the type's CreateParticleEffect starts when the new object's script attaches, not when a park loads
			placed.Runtime.StartObjectEffect( ObjectParticleEffect.Create );
			// [BIN:TPW-EXE:0x528e30 placement cue] the commit pass of the placement plays its size-classed cat_ui cue
			PlayPlacementCue( entry, x, y, rotation );
		}
		catch ( Exception error )
		{
			if ( bought != null && registered is { IsDeleted: false } stranded && !economyInstances.ContainsKey( stranded ) )
			{
				Log.Warning( $"Building {entry.DisplayName} failed after it was placed ({error.GetType().Name}: {error.Message}); binding it to the economy record it was bought with." );
				try { LinkEconomy( stranded, bought.Id ); }
				catch ( Exception linkError ) { Log.Warning( $"Binding {entry.DisplayName} to its economy record failed too: {linkError.Message}" ); }
			}
			throw;
		}
		if ( bought != null )
			LinkEconomy( placed, bought.Id );
		LastActionMessage = bought != null
			? $"Built {entry.DisplayName} for ${bought.TotalSpent}; it runs its original script."
			: $"Built {entry.DisplayName} (sandbox: no economy, not charged); it runs its original script.";
		return placed;
	}

	/// <summary>Whether a newly built object starts open: the constructor opens it and then closes one whose type has a queue (<c>Info.HasQueue</c>, 0x004DB712).</summary>
	internal static bool BuiltObjectStartsOpen( ObjectCatalogEntry entry ) => !entry.HasQueue;

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

	/// <summary><see cref="CheckObject(ObjectCatalogEntry, int, int, int)"/> with the objects it would demolish and the path cells it would clear.</summary>
	public OriginalPlacementResult CheckObject( ObjectCatalogEntry entry, int x, int y, int rotation, out ObjectPlacementPlan plan )
	{
		var anchor = GetCentredAnchor( entry, x, y, rotation );
		return Objects.Check( entry, anchor.X, anchor.Y, rotation, out plan );
	}

	/// <summary>
	/// Pure object-preview presentation shared by both HUDs, using their already-computed placement and colour decisions.
	/// Add-on mode keeps its legacy fallback; this neither revalidates placement nor checks or spends money.
	/// </summary>
	public (List<(int X, int Y, BlueprintMarker Marker)> Cells, Dictionary<(int X, int Y), byte> Directions) ObjectBlueprint(
		ObjectCatalogEntry entry, int anchorX, int anchorY, int rotation, OriginalPlacementResult placementResult, bool allowed ) =>
		BlueprintGhosts.Footprint( entry.Shape, anchorX, anchorY, rotation, allowed, Objects.Grid,
			entry.HasQueue && !entry.IsTool && BuildAddOnRide == null && !TrackAddOns.IsAddOn( entry ), placementResult );

	/// <summary><see cref="CheckObject(ObjectCatalogEntry, int, int, int)"/> for a site that demolishes nothing and clears no path (<see cref="ParkObjects.CheckFree"/>).</summary>
	public OriginalPlacementResult CheckFreeObject( ObjectCatalogEntry entry, int x, int y, int rotation )
	{
		var anchor = GetCentredAnchor( entry, x, y, rotation );
		return Objects.CheckFree( entry, anchor.X, anchor.Y, rotation );
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


	/// <summary>The remove tool: the object under the cursor, else a queue cell, else a path cell.</summary>
	// [EXT:dev-one-click-remove] the developer panel's one-click remove tool takes whatever stands on the clicked cell (an object, else a queue cell, else a path cell); the original removes each through its own tool (the bulldozer 0x52BDD0, the queue and path tools' undo, the sale of a ride) and has no combined click
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
		foreach ( var line in Guests.ImportOriginal( save.Guests, save.World.Variables.GameTick, Attraction, Bin, save.World.Variables.NumberOfVisitorsToDate ) )
		{
			import.Report( line );
			Log.Trace( $"Park economy import: {line}" );
		}
	}

	/// <summary>Attaches the supported original arrival scheduler after objects and their scripts have been imported.</summary>
	private void ImportOriginalArrivals()
	{
		if ( resumeState != null || GameEditions.IsThemeParkInc || visitLayout != null || Guests == null || OriginalPark?.Save is not { } save )
			return;
		var line = Guests.ImportOriginalArrivals( save.World );
		Park?.Import?.Report( line );
		Log.Trace( $"Park arrival import: {line}" );
	}

	/// <summary>
	/// An original save's RSSE block gives the imported objects' scripts their saved ids, list order and pass counter (<see cref="ParkObjects.ImportOriginalScripts"/>, DET-013): the
	/// one slice per eight passes then falls on the passes the saved park ran it on. Only a park started from an original save does this; a resumed park restores its own scripts.
	/// </summary>
	private void ImportOriginalScripts()
	{
		if ( resumeState != null || OriginalPark?.Save is not { Rsse: { } rsse } save )
			return;
		foreach ( var line in Objects.ImportOriginalScripts( rsse, save.RuntimeBlocks?.ClockWords[0] ) )
		{
			Park?.Import?.Report( line );
			Log.Trace( $"Park script import: {line}" );
		}
	}

	/// <summary>
	/// An original save's Particles block gives the level's particle pool its saved emitters and effectors, generator and counters (SAVE-013, tp.exe loader 0x51F7A0). Only a park started from an
	/// original save does this; a resumed park restores its own pool.
	/// </summary>
	private void ImportOriginalParticles()
	{
		if ( resumeState != null || OriginalPark?.Save is not { Particles: { On: true } block } )
			return;
		ParticlesImported?.Invoke( this, block );
		Park?.Import?.Report( $"Particles imported: {block.ActiveEmitterOrder.Count} emitters and {block.ActiveEffectorOrder.Count} effectors restored with the saved generator and counters; the post-load pass hides the screen-space ones (0x5200B0)." );
	}

	/// <summary>
	/// An original save's operating words (speed, capacity and duration, SAVE-006) become the linked ride runtimes' words where they differ from the tier's values. Only a park started
	/// from the shipped save does this; a resumed park restores its own words (<see cref="OriginalObjectRuntimeSnapshot"/>).
	/// </summary>
	private void ImportOriginalRideSettings()
	{
		if ( resumeState != null || Park?.Import is not { } import || OriginalPark?.Save is not { } save )
			return;
		var runtimes = economyInstances.ToDictionary( pair => pair.Value, pair => pair.Key.Runtime );
		foreach ( var attraction in save.Attractions )
		{
			if ( !import.AttractionInstances.TryGetValue( attraction.Id, out var instance ) || !runtimes.TryGetValue( instance, out var runtime ) )
				continue;
			var taken = runtime.RestoreOriginalSettingWords( attraction.OperatingSpeed, attraction.OperatingCapacity, attraction.OperatingDuration );
			// [BIN:TPW-EXE:0x004E0E60 SetState] the open word (mCanLoad, +0x68) is cleared by states 1, 2 and 4 (their import closes the ride already) and by the player's close or a maintenance call; an idle attraction saved with it clear stays closed until the door opens the park
			if ( Park!.Economy.TryGetObject( instance, out var objectState ) && RideOpening.ClosedByImportedOpenWord( attraction.CanLoad, ParkEconomy.ObjectState( objectState ) ) && runtime.IsOpen )
			{
				runtime.Close();
				SyncOpenState( runtime );
				taken = taken.Concat( new[] { "closed (the saved open word is 0)" } ).ToList();
			}
			if ( taken.Count == 0 )
				continue;
			var line = $"Ride settings imported for attraction thing {attraction.Id} (Info.Id {attraction.InfoId}): {string.Join( ", ", taken )}.";
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
			// Use the native shared predicate for park points, not the additional host admission sample guard; a missing controller fails lookup.
			state.CoasterAcceptsGuests = item.Runtime.Coaster?.CountsForParkPoints ?? false;
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

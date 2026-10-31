namespace OpenTPW;

/// <summary>Player-built roller coasters (docs/COASTERS.md): the coaster tool, the builders of the level's stations and the track renderer.</summary>
public partial class Level
{
	private readonly Dictionary<OriginalObject, CoasterBuilder> coasterBuilders = new();
	private bool coasterCellsReady;
	private CoasterRenderer? coasterRenderer;

	/// <summary>Every coaster controller of the park.</summary>
	public IReadOnlyList<CoasterRide> Coasters => Objects.ScriptWorld.Coasters.Rides;
	/// <summary>The station whose track the coaster tool edits, or null when the tool is off.</summary>
	public OriginalObject? CoasterToolStation { get; private set; }

	/// <summary>
	/// The coaster-being-edited mark: the tool's entry sets it and its exit clears it, through their commands (<see cref="BeginCoasterToolCommand"/>, <see cref="EndCoasterToolCommand"/>), so every peer
	/// keeps guests off the coaster a player edits.
	/// </summary>
	// [BIN:TPW-EXE:0x00790FEC coaster being edited] the global names the controller the tool edits (written by 0x00430AC0 from the tool's entry and exit 0x00447520); the guest predicate 0x00441970 refuses it
	private static void SetBeingEdited( OriginalObject station, bool editing )
	{
		if ( station.Runtime?.Coaster is { } ride )
			ride.BeingEdited = editing;
	}

	private CoasterEdits? coasterEdits;
	/// <summary>[EXT:online-coop-commands] The pylon each player is editing in a coaster panel drag.</summary>
	internal CoasterEdits CoasterEdits => coasterEdits ??= new();
	/// <summary>The builder the coaster tool drives, or null.</summary>
	public CoasterBuilder? CoasterTool => CoasterToolStation is { } station ? GetCoasterBuilder( station ) : null;
	public bool IsCoasterToolActive => CoasterToolStation != null;
	/// <summary>The coaster tool's panel (the nine buttons and their sub-modes), or null when the tool is off.</summary>
	public CoasterToolPanel? CoasterPanel { get; private set; }
	/// <summary>Triangles the coaster renderer drew in the last frame.</summary>
	public int CoasterTrianglesDrawn => coasterRenderer?.DrawnTriangles ?? 0;

	private void SetupCoasters()
	{
		Objects.ObjectPlaced += OnCoasterStationPlaced;
		Objects.ObjectRemoved += OnCoasterStationRemoved;
		foreach ( var item in Objects.Objects )
			OnCoasterStationPlaced( item );
		coasterSite = CoasterSite.FromGrid( Objects.Grid );
		coasterRenderer = new CoasterRenderer( () => Coasters, () => Objects.Grid.Origin, ObjectPlacement.ModelScale, CoasterEntry,
			ride => CoasterToolStation?.Runtime.Coaster == ride ? DemoPreview ?? CoasterPreview : null,
			ride => CoasterStation( ride )?.Runtime.AttractionId ?? 0, () => SceneClock.Milliseconds );
	}

	/// <summary>The riders of the coasters as head sources, hung from the head sockets of the car models.</summary>
	internal void AddCoasterHeads( RideHeadRenderer heads )
	{
		if ( coasterRenderer is { } renderer )
			heads.AddSource( new CoasterHeadSources( renderer ) );
	}

	/// <summary>The build cursor the coaster tool shows: the track is drawn through a ghost pylon there. Set by the HUD each frame, null when the tool is off.</summary>
	public CoasterPreview? CoasterPreview { get; set; }

	/// <summary>A build cursor the debug demo holds (it wins over the HUD's cursor, so a capture does not depend on the mouse).</summary>
	internal CoasterPreview? DemoPreview { get; set; }

	/// <summary>The pylon cells of the demo circuit, as offsets from the station's '&gt;' cell (the circuit of the native smoke test, which passes every window).</summary>
	private static readonly (int X, int Y)[] DemoCircuit = { (4, 0), (7, 4), (4, 8), (-4, 8), (-8, 4), (-7, 0) };

	/// <summary>
	/// Places a coaster station with a small circuit near the park's paths and points the park camera at it (the debug flag <c>--debug-coaster</c>, for captures):
	/// <paramref name="finished"/> closes and finishes the track, otherwise it stays open after the fourth pylon with the build cursor on the fifth cell.
	/// <paramref name="infoId"/> picks the coaster (its <c>Info.Id</c>), the first buildable one by default. Null when no site has room.
	/// </summary>
	// [EXT:debug-coaster] developer/screenshot helper, not original behaviour: it builds the track for the player without the tool and without charging money
	internal OriginalObject? BuildDemoCoaster( bool finished, int? infoId, int height = 40, bool enterTool = false )
	{
		if ( IsReadOnlyVisit || Guests == null || OriginalPark == null )
			return null;
		var entries = Objects.Catalog.Buildable.Where( item => CoasterDefinition.IsCoaster( item.Settings ) ).OrderBy( item => item.InfoId ).ToArray();
		var entry = infoId is int id ? entries.FirstOrDefault( item => item.InfoId == id ) : entries.FirstOrDefault();
		if ( entry == null )
			return null;
		var grid = Objects.Grid;
		bool Free( int x, int y ) => x >= 0 && y >= 0 && x < grid.Width && y < grid.Height && Objects.FindAt( x, y ) == null
			&& grid.CheckTerrain( x, y ) is OriginalPlacementResult.Allowed && Guests.Grid.Cells.RawTypeAt( x, y ) == 0 && !Guests.Grid.Cells.FlagsAt( x, y ).HasFlag( ParkCellFlags.Unowned );
		var near = (X: Guests.Grid.CountX / 2, Y: Guests.Grid.CountY / 2);
		var path = Enumerable.Range( 0, Guests.Grid.CountX * Guests.Grid.CountY ).Select( index => (X: index % Guests.Grid.CountX, Y: index / Guests.Grid.CountX) ).Where( cell => Guests.Grid.IsWalkable( cell.X, cell.Y ) ).ToArray();
		if ( path.Length > 0 )
			near = (path.Sum( cell => cell.X ) / path.Length, path.Sum( cell => cell.Y ) / path.Length);
		foreach ( var cell in Enumerable.Range( 0, grid.Width * grid.Height ).Select( index => (X: index % grid.Width, Y: index / grid.Width) )
			.OrderBy( cell => Math.Abs( cell.X - near.X ) + Math.Abs( cell.Y - near.Y ) ).ThenBy( cell => cell.Y ).ThenBy( cell => cell.X ) )
		{
			if ( Objects.CheckFree( entry, cell.X, cell.Y, 0 ) != OriginalPlacementResult.Allowed || CoasterBuilder.StationEnds( entry.Shape, cell.X, cell.Y, 0 ) is not { } ends )
				continue;
			var cells = DemoCircuit.Select( offset => (X: ends.Depart.X + offset.X, Y: ends.Depart.Y + offset.Y) ).Append( ends.Return ).ToArray();
			if ( !cells.All( target => Free( target.X, target.Y ) ) )
				continue;
			var station = Objects.TryPlace( entry, cell.X, cell.Y, 0, out _ );
			if ( station?.Runtime.Coaster == null || GetCoasterBuilder( station ) is not { } builder )
				return null;
			builder.Height = height;
			var count = finished ? DemoCircuit.Length : 4;
			for ( var index = 0; index < count; index++ )
				builder.Place( ends.Depart.X + DemoCircuit[index].X, ends.Depart.Y + DemoCircuit[index].Y );
			if ( finished )
			{
				builder.Place( ends.Return.X, ends.Return.Y );
				builder.Complete();
				// with the tool: the finished coaster is taken into the tool, so its panel shows on the height button (capture helper)
				if ( enterTool )
					EnterCoasterTool( station );
			}
			else
			{
				EnterCoasterTool( station );
				DemoPreview = new CoasterPreview( ends.Depart.X + DemoCircuit[4].X, ends.Depart.Y + DemoCircuit[4].Y, true, height, false );
			}
			var centre = OriginalParkPlacement.GetCellCenter( OriginalPark.Heightfield, ends.Depart.X - 2, ends.Depart.Y + 4 );
			(Camera.Mode as ParkCameraMode)?.SetPose( new Vector3( centre.X, centre.Y, 0 ), ParkCameraMode.MaximumHeight, -20 );
			Log.Trace( $"--debug-coaster: {entry.DisplayName} at ({cell.X}, {cell.Y}), {(finished ? "finished" : "open")} with {builder.Track.PylonCount} pylons; camera on engine ({centre.X:0.#}, {centre.Y:0.#})." );
			return station;
		}
		return null;
	}

	/// <summary>The camera's yaw as the original keeps it (0x790a38, radians): the tilt drag turns by it minus the pylon's yaw (<c>0x443340</c>).</summary>
	// [BIN:TPW-EXE:0x443371 tilt drag yaw] the drag update reads the camera yaw global 0x790a38 and subtracts the selected node's yaw (+0xe0); the park camera's yaw maps onto that global by ParkCameraMode.OriginalYaw (the camera block of the saves, 0x42cec0), which is the atan2(x, z) heading of the forward direction in track coordinates
	internal float CoasterCameraYaw
	{
		get
		{
			if ( Camera.Mode is ParkCameraMode park )
				return ParkCameraMode.OriginalYaw( park.Yaw );
			var forward = Camera.Rotation.Forward;
			return MathF.Atan2( forward.X, forward.Y ); // [DET:presentation] the camera yaw for the tilt drag, an input of the coaster tool
		}
	}

	/// <summary>Whether a cell passes the cell test of the coaster tool's placement area (<see cref="IsCoasterAreaCell"/>).</summary>
	private bool IsCoasterCellFree( int x, int y ) => Guests != null && IsCoasterAreaCell( Guests.Grid.Cells, x, y );

	/// <summary>
	/// The cell test of the placement area besides the validator (tool mode 0x15, <c>0x5234d0</c>): the cell is unowned-clear (flag 0x40), holds no fence post or track tile,
	/// is none of the types 9 and 10 (object entrance and exit), 3 (queue), 4 (object footprint), 7 (outside the park) and 0x1e (the approach road), and is not a path with flag 0x20.
	/// </summary>
	// [BIN:TPW-EXE:0x5243a6 area cell test] 0x535db0(0x40) on the cell flags, then the second layer's cell (0x4d0af0, or the owner of a type 0xc/0x11 extension): 0x53ad60 (type 0x19) and 0x53ad30 (types 0xb, 0xd, 0x10, 0x12, 0x19), then the cell's own type against 9, 0xa, 3, 4, 7 (0x536340, 0x536350, 0x536320, 0x536380, 0x536360) and 0x1e, and type 1 with flag 0x20 (0x5243f6..0x524445)
	// [BIN:TPW-EXE:0x536320 cell predicates] 0x536310 type 1; 0x536320 types 3 and 9; 0x536340 type 9; 0x536350 type 10; 0x536360 types 7 and 0x1e; 0x536380 type 4: the cell type is the word at cell +8, the flags the word at +0xe
	internal static bool IsCoasterAreaCell( ParkCellMap cells, int x, int y )
	{
		if ( !cells.InBounds( x, y ) || cells.PoolBridges?.Reserves( x, y ) == true )
			return false;
		var type = cells.RawTypeAt( x, y );
		var flags = cells.FlagsAt( x, y );
		if ( (flags & ParkCellFlags.Unowned) != 0 )
			return false;
		// the second layer: a track tile or fence post on the cell (an extension cell stands for its head tile, 0x536400)
		if ( ParkCellTypes.IsTrackCell( type ) || (ParkCellTypes.IsTrackExtension( type ) && cells.OwnerAt( x, y ) != 0) )
			return false;
		// [EXT:pools-cell-type] a pool water cell is occupied ground like an object's cell; the original has no pools
		if ( type is ParkCellTypes.Entrance or ParkCellTypes.Exit or (byte)ParkCellType.Queue or ParkCellTypes.Object or ParkCellTypes.NoTerrain or ParkCellTypes.FixedWalkway or ParkCellTypes.PoolWater )
			return false;
		return !(type == (byte)ParkCellType.Path && (flags & ParkCellFlags.NoModify) != 0);
	}

	/// <summary>
	/// Screen position of a coaster-space point (original units: X, height, Z of the track), for picking the pylon piece under the pointer. False when the point is behind the camera.
	/// </summary>
	public bool TryProjectCoasterPoint( System.Numerics.Vector3 world, System.Numerics.Vector2 viewportSize, out System.Numerics.Vector2 screen )
	{
		var origin = Objects.Grid.Origin;
		var scale = ObjectPlacement.ModelScale;
		var point = new System.Numerics.Vector4( origin.X + world.X * scale, origin.Y + world.Z * scale, world.Y * scale, 1 );
		var clip = System.Numerics.Vector4.Transform( point, Camera.ViewMatrix * Camera.ProjMatrix );
		screen = default;
		if ( clip.W <= 0 )
			return false;
		screen = new System.Numerics.Vector2( (clip.X / clip.W + 1) / 2 * viewportSize.X, (1 - clip.Y / clip.W) / 2 * viewportSize.Y );
		return float.IsFinite( screen.X ) && float.IsFinite( screen.Y );
	}

	/// <summary>
	/// The pylon under the pointer for the coaster tool: the pylon whose model the cursor ray meets first, unless the terrain or another object's model is met before it. <c>Modelled</c> is false when the ride
	/// has no pylon models to test (no game data), where the caller picks by the cell.
	/// </summary>
	// [BIN:TPW-EXE:0x443ee0 model pick] the picker 0x45bf90 hands the model under the pointer (the first owner the ray's walk meets, a nearer terrain point or object wins) and 0x443ee0 maps that handle through the table 0x7a4610 to the coaster node whose pylon it is: a pointer beside the pylon's model picks nothing
	internal (bool Modelled, int? Node) PickCoasterPylon( CoasterRide ride, System.Numerics.Vector2 mouse, System.Numerics.Vector2 viewportSize )
	{
		if ( coasterRenderer is not { } renderer || !ParkPlacement.TryGetMouseRay( mouse, viewportSize, Camera.ViewMatrix, Camera.ProjMatrix, out var origin, out var direction )
			|| !System.Numerics.Matrix4x4.Invert( Camera.ViewMatrix, out var inverse ) )
			return (false, null);
		if ( !renderer.TryPickPylon( ride, origin, direction, out var node, out var distance, out var modelled ) )
			return (modelled, null);
		var eye = inverse.Translation;
		var pylonHit = origin + direction * (distance / Math.Max( direction.Length(), 1e-9f ));
		var pylonDistance = System.Numerics.Vector3.Distance( eye, pylonHit );
		var terrain = TerrainHit( mouse, viewportSize, Camera.ViewMatrix, Camera.ProjMatrix, origin, direction );
		// a pylon's ground node lies on the terrain, so a hit on it ties with the terrain point: the terrain wins only when it is clearly nearer
		if ( terrain is { } ground && System.Numerics.Vector3.Distance( eye, ground ) + 0.05f < pylonDistance )
			return (true, null);
		if ( TryPickObject( origin, direction, Camera.ViewMatrix, terrain, out var objectHit ) && System.Numerics.Vector3.Distance( eye, objectHit ) < pylonDistance )
			return (true, null);
		return (true, node);
	}

	/// <summary>The catalogue entry (archive) of a coaster's station object, or null when the station left.</summary>
	private ObjectCatalogEntry? CoasterEntry( CoasterRide ride ) => CoasterStation( ride )?.Entry;

	/// <summary>Challenge type 18's measure (<see cref="ParkEconomy.CoasterCountedSections"/>): the most counted sections of the coasters whose station is <paramref name="infoId"/>, any for 0.</summary>
	// [BIN:TPW-EXE:0x4cc810 best coaster] every ride of track type 3 with the id (any when 0) is read through 0x43e0e0's fourth output, the controller's +0x108; an unrated coaster counts 0
	private long CoasterCountedSections( int infoId )
	{
		long best = 0;
		foreach ( var ride in Coasters )
		{
			if ( CoasterStation( ride ) is { } station && (infoId == 0 || station.Entry.InfoId == infoId) )
				best = Math.Max( best, ride.Rating?.CountedSections ?? 0 );
		}
		return best;
	}

	/// <summary>The economy instance of the coaster a check of challenge types 14 and 18 hands on (<see cref="ParkEconomy.CoasterBestObject"/>), 0 when none.</summary>
	// [BIN:TPW-EXE:0x4cc810 best coaster] the walk starts at the head of the thing list, the newest object, takes the first matching coaster whatever its count and replaces it only by a strictly higher count (an unrated coaster counts 0)
	private int CoasterBestObject( int infoId )
	{
		OriginalObject? best = null;
		long bestSections = 0;
		for ( var index = Objects.Objects.Count - 1; index >= 0; index-- )
		{
			var item = Objects.Objects[index];
			if ( item.IsDeleted || item.Runtime.Coaster is not { } ride || infoId != 0 && item.Entry.InfoId != infoId )
				continue;
			var sections = ride.Rating?.CountedSections ?? 0;
			if ( best == null || bestSections < sections )
				(best, bestSections) = (item, sections);
		}
		return best != null && GetEconomyInstance( best ) is int instance ? instance : 0;
	}

	/// <summary>The kart rides' challenge measure (<see cref="ParkEconomy.KartBestCrossings"/>): the most crossings of any kart ride now and the economy instance of the one that has them.</summary>
	// [BIN:TPW-EXE:0x4cc4b0 best kart] the walk starts at the head of the thing list, the newest object; a ride counts when its type is a ride (WhichUIType 0) with the car track type (+0x9C 1: karts only, the bumper cars' WhichTrackType is 0) and its crossings are the track analysis's (0x545310, the sections of kind 11, halved for its doubled pass); the first match is taken whatever its count and a strictly higher count replaces it
	private (long Crossings, int Instance) BestKartCrossings()
	{
		OriginalObject? best = null;
		long bestCrossings = 0;
		for ( var index = Objects.Objects.Count - 1; index >= 0; index-- )
		{
			var item = Objects.Objects[index];
			if ( item.IsDeleted || item.Runtime.Bump is not { } bump || bump.Template.Family is not TrackFamily.Kart )
				continue;
			var crossings = bump.SectionStatistics().Crossings;
			if ( best == null || bestCrossings < crossings )
				(best, bestCrossings) = (item, crossings);
		}
		return best != null && GetEconomyInstance( best ) is int instance ? (bestCrossings, instance) : (0, 0);
	}

	/// <summary>The track rides' challenge measure (<see cref="ParkEconomy.TrackRideSections"/>): the most sections of the kart and water rides of an Info.Id, any for 0.</summary>
	// [BIN:TPW-EXE:0x4cc5d0 best track ride] the rides with a car or water track type (0x4e2b80 also admits the coaster type, whose thing holds no track analysis and counts 0), the Info.Id when not 0, the sections 0x545310 returns (halved for its doubled pass)
	private long TrackRideSections( int infoId )
	{
		long best = 0;
		foreach ( var item in Objects.Objects )
		{
			if ( !item.IsDeleted && item.Runtime.Bump is { } bump && bump.Template.Family is TrackFamily.Kart or TrackFamily.Water && (infoId == 0 || item.Entry.InfoId == infoId) )
				best = Math.Max( best, bump.SectionStatistics().Sections );
		}
		return best;
	}

	/// <summary>Points the current economy's challenge types 15, 18 and 21 at the park's coasters, karts and track rides; a park load replaces the economy, so it runs again then.</summary>
	private void AttachCoasterChallenges()
	{
		if ( Park != null )
		{
			Park.Economy.CoasterCountedSections = CoasterCountedSections;
			Park.Economy.CoasterBestObject = CoasterBestObject;
			Park.Economy.KartBestCrossings = BestKartCrossings;
			Park.Economy.TrackRideSections = TrackRideSections;
		}
	}

	/// <summary>The station object a coaster belongs to (a plain loop: the renderer asks every frame).</summary>
	private OriginalObject? CoasterStation( CoasterRide ride )
	{
		foreach ( var item in Objects.Objects )
		{
			if ( !item.IsDeleted && ReferenceEquals( item.Runtime.Coaster, ride ) )
				return item;
		}
		return null;
	}

	private void OnCoasterStationPlaced( OriginalObject item )
	{
		if ( item.Runtime.Coaster == null || GetCoasterBuilder( item ) is not { } builder )
			return;
		if ( CoasterBuilder.StationEnds( item.Entry.Shape, item.Placement.X, item.Placement.Y, item.Placement.Rotation ) is { } ends )
			builder.SetStation( ends.Depart, ends.Return, item.ComputeCoasterStationLayout() );
	}

	/// <summary>Derived marker tokens are attached only after the map exists, and after both track layers restore on resume.</summary>
	private void BindCoasterCells( OriginalObject station, CoasterBuilder builder )
	{
		if ( !TypesFootprints || !coasterCellsReady || Guests == null || builder.Track.CellMarkers != null ) return;
		BindCoasterCellsToMap( station, builder );
	}

	private void BindCoasterCellsToMap( OriginalObject station, CoasterBuilder builder )
	{
		if ( Guests == null || !Coasters.Contains( builder.Ride ) ) return;
		var ends = station.Cells.Where( entry => entry.Cell.CellType == ObjectShapeSymbols.TypeTrackEnd )
			.Select( entry => (entry.X, entry.Y, Direction: GuestPathGrid.LinkDirection( ObjectFootprint.RotateDirectionByte( entry.Cell.Direction, station.Placement.Rotation ) )) )
			.Where( entry => entry.Direction >= 0 )
			.Select( entry => (entry.X + GuestPathGrid.Directions[entry.Direction].DX, entry.Y + GuestPathGrid.Directions[entry.Direction].DY) );
		// [EXT:online-coop-snapshot-join] a join takes the host's live binding over from the cells, without claiming anything the host's binding does not hold
		if ( joinRestore is { } join )
			builder.Track.AdoptCells( Guests.Grid.Cells, OwnerWord( station ), ends, join.CoasterUnheldCells.Where( cell => cell.Station == station.Runtime.AttractionId )
				.Select( cell => (cell.X, cell.Y, cell.Displaced) ).ToArray() );
		else
			builder.Track.BindCells( Guests.Grid.Cells, OwnerWord( station ), ends );
	}

	private void BindCoasterCellsAfterLoad()
	{
		if ( !TypesFootprints || Guests == null ) return;
		coasterCellsReady = true;
		// Non-coaster parks need no binding. Asset-free Level fixtures can intentionally omit coaster setup;
		// a live coaster without the normal constructor/setup state remains an error, not silently unbound.
		if ( Coasters.Count == 0 ) return;
		if ( coasterBuilders == null ) throw new InvalidOperationException( "Live coasters require initialized coaster builders before cell binding." );
		foreach ( var (station, builder) in coasterBuilders )
			if ( !station.IsDeleted && builder.Track.CellMarkers == null ) BindCoasterCellsToMap( station, builder );
	}

	private void OnCoasterStationRemoved( OriginalObject item )
	{
		// [EXT:online-coop-commands] the drags on the station's track end with it, unsettled, as the track goes without a refund; before this machine's panel closes, whose close ends its drag through
		// commands that would otherwise run inside a peer's removal on this machine alone
		coasterEdits?.Drop( item.Runtime.AttractionId );
		if ( CoasterToolStation == item )
		{
			// the tool's full clean-up for a station that goes (the ride object is deleted, so it is not opened again)
			CoasterPanel?.Close();
			CoasterPanel = null;
			CoasterPreview = null;
			DemoPreview = null;
			CoasterToolStation = null;
			SetBeingEdited( item, false );
		}
		if ( coasterBuilders.Remove( item, out var builder ) )
			builder.Delete();
	}

	/// <summary>The headless build API of a coaster station of this level (null for other objects).</summary>
	public CoasterBuilder? GetCoasterBuilder( OriginalObject station )
	{
		if ( station.Runtime.Coaster is not { } ride || station.IsDeleted )
			return null;
		if ( !coasterBuilders.TryGetValue( station, out var builder ) )
		{
			builder = new CoasterBuilder( ride, CoasterSite, () => Park?.Economy, () => GetEconomyInstance( station ) );
			coasterBuilders[station] = builder;
		}
		return builder;
	}

	/// <summary>The level's cells for coaster tracks: grid bounds, the no-ground cells as they were when the park started, ground heights in original units.</summary>
	public CoasterSite CoasterSite => coasterSite ??= CoasterSite.FromGrid( Objects.Grid );
	private CoasterSite? coasterSite;

	/// <summary>
	/// Starts the coaster tool on a station: park clicks place pylons, a click on a station end closes the circuit, the next one completes the track. The ride is closed
	/// (its riders leave, no guest is stranded) and its controller goes to state 2 when the track is finished, so the trains run empty without a station step, or to state 1
	/// (no trains) when it is not.
	/// </summary>
	// [BIN:TPW-EXE:0x447520 coaster tool entry] with no coaster being edited the ride object is closed (0x4df300) and the controller set to state 2 (a complete track with its circuit closed) or 1 (0x435730); round 2 labelled 0x447520 the tool's exit, it is its entry when called with param 1
	public bool EnterCoasterTool( OriginalObject station ) => BeginCoasterToolEntry( station ) == TrackToolEntry.Entered;

	/// <summary>
	/// The coaster tool's entry on <paramref name="station"/> (<see cref="EnterCoasterTool"/>, and the tool a placed coaster opens): it started (or was on this coaster already), or
	/// [EXT:online-coop-claims] a guest's claim went to the host and the tool starts from its outcomes (C3c-2, <see cref="CompleteTrackToolStart"/>), or a claim refused it (another player's, the
	/// session's rights, or a guest's coaster commands still to send), or nothing started. One tool at a time: every other tool ends first.
	/// </summary>
	internal TrackToolEntry BeginCoasterToolEntry( OriginalObject station )
	{
		if ( IsReadOnlyVisit || GetCoasterBuilder( station ) is not { } builder )
			return TrackToolEntry.None;
		// [EXT:online-coop-claims] a coaster another player claims starts no tool, and the tool on stays (C3c-2)
		if ( CoopSession != null && ClaimedAgainst( Commands.Player, station.Runtime.AttractionId ) )
		{
			NoteSessionRefusal( CoopRefusal.Claimed );
			return TrackToolEntry.Refused;
		}
		// the tool on this coaster already: nothing starts again (the original's entry closes only a coaster no tool edits)
		if ( CoasterToolStation == station && CoasterPanel != null )
			return TrackToolEntry.Entered;
		// [EXT:online-coop-commands] a guest's coaster commands still to send go first: a claim sent now would come before them (C3c-2)
		if ( !CoasterCommandsSentBeforeClaim() )
			return TrackToolEntry.Refused;
		// [EXT:online-coop-commands] one tool at a time: the other tools end first, as the staff tool's entry ends them, and a guest's queue edit, Move pick-up or track entry that waits for the
		// host starts no tool once it is back (C3b, C3c-2)
		EndBuildTools();
		StaffPlacement?.Cancel();
		// [EXT:online-coop-claims] a guest's tool claims its coaster through the host, then the entry's writes go as a second request; the panel opens from its outcome (C3c-2). The coaster
		// tool that ended sends its last commands first.
		if ( CoopSession != null && Commands.SendsRequests )
		{
			if ( !CoasterCommandsSentBeforeClaim() )
				return TrackToolEntry.Refused;
			if ( Commands.Issue( new ClaimRideCommand( station.Runtime.AttractionId ) ).Request is not { } claim )
				return TrackToolEntry.Refused;
			WaitingToolStart = claim;
			pendingTrackStart = new PendingTrackStart( claim, station.Runtime.AttractionId, Placed: false );
			return TrackToolEntry.Pending;
		}
		// [EXT:online-coop-claims] the host's tool claims its coaster once the tool it replaces is gone (C3c-2); the claim moves from a ride the replaced tool held
		if ( !ClaimForTrackTool( station ) )
			return TrackToolEntry.Refused;
		// [EXT:online-coop-commands] the entry's writes are a command; the tool and its panel are the issuer's
		if ( !Commands.Issue( new BeginCoasterToolCommand( station.Runtime.AttractionId ) ).Accepted )
			return TrackToolEntry.None;
		OpenCoasterPanel( station, builder );
		return TrackToolEntry.Entered;
	}

	/// <summary>
	/// The coaster tool's panel on <paramref name="station"/>; [EXT:online-coop-commands] a guest's goes through the level's queue to the host (C3c-2), and its completing click finishes the tool here
	/// (<see cref="FinishGuestCoasterTool"/>).
	/// </summary>
	private void OpenCoasterPanel( OriginalObject station, CoasterBuilder builder )
	{
		CoasterToolStation = station;
		var attraction = station.Runtime.AttractionId;
		CoasterToolPanel? panel = null;
		panel = new CoasterToolPanel( builder, IsCoasterCellFree, attraction, command => Commands.Issue( command ) )
		{
			GuestRequests = CoopSession != null ? CoasterRequests : null,
			SendsRequests = () => Commands.SendsRequests,
			HeldEdit = () => CoasterEdits.Of( Commands.Player ) is { } held && held.Station == attraction ? held : null,
			Completed = () => FinishGuestCoasterTool( station, panel! ),
			// [EXT:online-coop-undo] in a shared park Backspace takes back the player's own last pylon (C5-3)
			InSharedPark = () => Commands.SessionRules || Commands.SendsRequests
		};
		CoasterPanel = panel;
	}

	/// <summary>
	/// [EXT:online-coop-commands] A guest's coaster commands on their way to the host, in order (C3c-2): the level's, so a panel's last commands go after it closed. Null until the first.
	/// </summary>
	private CoasterRequestQueue? coasterRequests;

	/// <summary>[EXT:online-coop-commands] The guest's coaster command queue (<see cref="CoasterRequestQueue"/>).</summary>
	internal CoasterRequestQueue CoasterRequests => coasterRequests ??= new();

	/// <summary>[EXT:online-coop-commands] How many tool requests of a guest may be open at once: the pool tool's batches and the coaster tool's commands share it (C3b-3, C3c-2).</summary>
	internal const int OpenToolRequestLimit = PoolTool.MaximumOpenRequests;

	/// <summary>
	/// [EXT:online-coop-commands] How many of this guest's tool requests are open in the session's requests, from this park or one a resync replaced: the pool tool's batches and the coaster tool's
	/// commands, which share <see cref="OpenToolRequestLimit"/> (C3c-2).
	/// </summary>
	internal int OpenToolRequests => CoopSession?.GuestCommands?.Open.Count( request => request.Command is PoolToolCommand or CoasterPanelCommand or EndCoasterToolCommand ) ?? 0;

	/// <summary>
	/// [EXT:online-coop-commands] The HUD's frame sends what the guest's coaster queue holds (C3c-2), before the pool tool's sender, so the coaster's last commands take a freed slot first: the pool
	/// tool sees its budget full while anything of the queue is unsent.
	/// </summary>
	internal void SendCoasterRequests( double now )
	{
		coasterSendClock = now;
		if ( coasterRequests is { } queue && Commands.SendsRequests )
			queue.Send( IssueCoasterRequest, () => OpenToolRequests, OpenToolRequestLimit, now );
	}

	/// <summary>One command of the guest's coaster queue goes to the host; <paramref name="completes"/>: its outcome has a completion of the panel's, which says a refusal itself.</summary>
	private ParkCommandResult IssueCoasterRequest( ParkCommand command, bool completes )
	{
		SendingCompletedCoasterCommand = completes;
		try
		{
			return Commands.Issue( command );
		}
		finally
		{
			SendingCompletedCoasterCommand = false;
		}
	}

	/// <summary>
	/// [EXT:online-coop-commands] Set while the guest's coaster queue sends a command whose outcome its panel completes (a click, a Backspace, a swap, a cut, a drag's press and end): a refusal by
	/// the rights here is the panel's to say, not the session's (C3c-2), as a pool stroke's batch is its stroke's (<see cref="PoolTool.SendingGuestBatch"/>).
	/// </summary>
	internal bool SendingCompletedCoasterCommand { get; private set; }

	/// <summary>
	/// [EXT:online-coop-commands] A guest request resolved (the HUD's drain): when it is one of the guest's coaster commands, its completion runs (C3c-2). True when it was one of them.
	/// </summary>
	internal bool CompleteCoasterRequest( CoopGuestCommands.Request request ) => coasterRequests?.Complete( request ) == true;

	/// <summary>[EXT:online-coop-commands] The guest's coaster tool actions that came back since the last call, oldest first (C3c-2).</summary>
	internal IReadOnlyList<CoasterGuestReport> TakeCoasterReports() => coasterRequests?.TakeReports() ?? Array.Empty<CoasterGuestReport>();

	/// <summary>[EXT:online-coop-commands] The HUD's clock at its last send (<see cref="SendCoasterRequests"/>): an entry's own send paces a drag's step by it.</summary>
	private double coasterSendClock = double.NegativeInfinity;

	/// <summary>
	/// [EXT:online-coop-commands] Whether a guest's track or coaster tool entry may send its claim (C3c-2): the coaster queue's commands go first, so the host applies a coaster tool's last commands
	/// before the claim moves. What the requests' budget lets go goes now; while anything still waits, the entry notes a waiting click and starts nothing (the player enters again once the queue
	/// drained, within a few frames). Always true for the host and in single player.
	/// </summary>
	private bool CoasterCommandsSentBeforeClaim()
	{
		if ( !Commands.SendsRequests || coasterRequests is not { HasUnsent: true } queue )
			return true;
		SendCoasterRequests( coasterSendClock );
		if ( !queue.HasUnsent )
			return true;
		NoteWaitingClick();
		return false;
	}

	/// <summary>
	/// [EXT:online-coop-commands] A guest's build click in the coaster tool (C3c-2): it goes to the host with the panel's tool state, and the panel waits for its outcome; a click while a request
	/// of the panel waits notes a waiting click. True when it went.
	/// </summary>
	internal bool SendCoasterClick( int x, int y, bool closing )
	{
		if ( CoasterTool is not { } builder || CoasterToolStation is not { } station || CoasterPanel is not { } panel )
			return false;
		var state = builder.ToolState;
		if ( panel.SendClick( new CoasterClickCommand( station.Runtime.AttractionId, x, y, state.Height, state.LoopMode, state.LoopPylonsLeft ), closing ) )
			return true;
		NoteWaitingClick();
		return false;
	}

	/// <summary>
	/// [EXT:online-coop-commands] A guest's Backspace in the coaster tool (C3c-2), [EXT:online-coop-undo] the take-back of its own last pylon (C5-3): one at a time, not during a drag, when a waiting
	/// click is noted; none goes when the panel placed no pylon of its own. What it did.
	/// </summary>
	internal CoasterBackspace SendCoasterBackspace()
	{
		if ( CoasterPanel is not { } panel )
			return CoasterBackspace.None;
		var sent = panel.SendBackspace();
		if ( sent == CoasterBackspace.Waits )
			NoteWaitingClick();
		return sent;
	}

	/// <summary>
	/// [EXT:online-coop-claims] A guest's completing click came back (C3c-2): the tool finishes as the host's does (the queue tool follows) only while <paramref name="panel"/> is still the open
	/// one, its station stands and the park's claims, every record applied so far, give the coaster to this seat; otherwise the panel closes without a command, and a lost claim says so.
	/// </summary>
	private void FinishGuestCoasterTool( OriginalObject station, CoasterToolPanel panel )
	{
		if ( !ReferenceEquals( CoasterPanel, panel ) )
			return;
		// The host's answer ends this tool and may start the queue tool: a press made before the handover gives the next tool no release click.
		HostToolStarts++;
		if ( station.IsDeleted || ClaimOf( station.Runtime.AttractionId ) != Commands.Player )
		{
			CloseCoasterToolLocally();
			if ( !station.IsDeleted )
				NoteSessionRefusal( CoopRefusal.ClaimLost );
			return;
		}
		FinishCoasterTool( station );
	}

	/// <summary>
	/// [EXT:online-coop-claims] The guest's coaster tool closes without a command (C3c-2): the session ended, or the claim's end left the coaster's tool state already (its release ran
	/// <see cref="LeaveCoasterToolState"/>). A drag's last commands still go, through the queue, unless the session ended.
	/// </summary>
	internal void CloseCoasterToolLocally()
	{
		CoasterPanel?.Close();
		CoasterPanel = null;
		CoasterToolStation = null;
		CoasterPreview = null;
	}

	/// <summary>
	/// [EXT:online-coop-commands] One tool at a time (C3c-2): the HUD's frame start ends a coaster tool that is on together with another tool, which then started after it through an entry that did
	/// not end it. True when it ended one.
	/// </summary>
	internal bool RetireCoasterToolForOtherTool()
	{
		if ( !IsCoasterToolActive || !(CellTool.IsActive || BuildEntry != null || IsRemovingObjects || IsPlacing || BuyLandTool is { IsActive: true } || AreaClearTool is { IsActive: true }
			|| PoolTool is { IsActive: true } || StaffPlacement is { IsActive: true } || PatrolTool is { IsActive: true }) )
			return false;
		ExitCoasterTool();
		return true;
	}

	/// <summary>
	/// Runs a <see cref="BeginCoasterToolCommand"/>: the station's ride closes (its riders leave, no guest is stranded), its controller takes the tool's state, a closed circuit whose closing link
	/// fails opens again, and the coaster is marked as being edited.
	/// </summary>
	internal ParkCommandResult ExecuteBeginCoasterTool( int attraction )
	{
		if ( ResolveCommandObject( attraction ) is not { } station || GetCoasterBuilder( station ) is not { } builder )
			return ParkCommandResult.Rejected( $"Attraction {attraction} is no coaster station." );
		station.Close();
		builder.Ride.EnterTool();
		builder.OpenFailingClosure();
		SetBeingEdited( station, true );
		return ParkCommandResult.Ok();
	}

	/// <summary>Ends the coaster tool (the track keeps its pylons; an incomplete track takes no riders). A ride the tool took out of service (state 2) opens again.</summary>
	// [BIN:TPW-EXE:0x447520 coaster tool exit] leaving the tool (param 0) opens the ride object again (0x4df390) when the controller's state is 2; the station script's COAST 5 then sets state 4 or 8; a ride left in state 1 stays closed
	public void ExitCoasterTool()
	{
		var station = CoasterToolStation;
		CoasterPanel?.Close();
		CoasterPanel = null;
		CoasterToolStation = null;
		CoasterPreview = null;
		if ( station is not { IsDeleted: false } )
			return;
		// [EXT:online-coop-commands] the exit's writes (the being-edited mark, the ride's door) are a command; [EXT:online-coop-claims] a guest's goes behind its panel's last commands, and only
		// while its seat still holds the claim (a lost claim's release left the tool state already, C3c-2)
		if ( Commands.SendsRequests && CoopSession != null )
		{
			if ( ClaimOf( station.Runtime.AttractionId ) == Commands.Player )
				CoasterRequests.Enqueue( new EndCoasterToolCommand( station.Runtime.AttractionId ) );
			return;
		}
		Commands.Issue( new EndCoasterToolCommand( station.Runtime.AttractionId ) );
	}

	/// <summary>
	/// Runs an <see cref="EndCoasterToolCommand"/>: the pylon edits on the coaster end where they stand, the coaster is no longer being edited, and a ride the tool took out of service (state 2) opens
	/// again.
	/// </summary>
	internal ParkCommandResult ExecuteEndCoasterTool( int attraction )
	{
		if ( ResolveCommandObject( attraction ) is not { Runtime.Coaster: not null } station )
			return ParkCommandResult.Rejected( $"Attraction {attraction} is no coaster station." );
		// [EXT:online-coop-claims] the panel's own close releases its drag first (CoasterToolPanel.Close); an edit the command finds is a peer's, which the being-edited mark the exit clears ends too
		// (it is the coaster's, not the player's: docs/COOP.md, "Known limits")
		LeaveCoasterToolState( station );
		return ParkCommandResult.Ok();
	}

	/// <summary>
	/// The coaster of <paramref name="station"/> leaves the tool (<see cref="EndCoasterToolCommand"/>, and [EXT:online-coop-claims] the end of its claim, C3c-2): every pylon edit on it is released,
	/// it is no longer being edited, and a ride the tool took out of service opens again.
	/// </summary>
	internal void LeaveCoasterToolState( OriginalObject station )
	{
		if ( station.Runtime.Coaster is not { } ride )
			return;
		CoasterEdits.ReleaseAll( station.Runtime.AttractionId );
		SetBeingEdited( station, false );
		if ( ride.State == CoasterControllerState.Built )
			station.Open();
	}

	/// <summary>
	/// A park click in the coaster tool: on the station's return end it completes the track, otherwise it places a
	/// pylon at the tool's height.
	/// </summary>
	public CoasterPlaceResult CoasterClick( int x, int y )
	{
		if ( CoasterTool is not { } builder || CoasterToolStation is not { } station )
			return CoasterPlaceResult.NotEditable;
		// [EXT:online-coop-commands] the click is a command with the panel's height and loop edit mode; the builder here takes the tool state the placement left, and a completion ends the tool, on
		// the issuer's machine
		var state = builder.ToolState;
		var click = Commands.Issue( new CoasterClickCommand( station.Runtime.AttractionId, x, y, state.Height, state.LoopMode, state.LoopPylonsLeft ) );
		if ( click.Value is not CoasterClickDone done )
			return CoasterPlaceResult.NotEditable;
		// [EXT:online-coop-undo] in a shared park the host's panel keeps the record of each pylon its click placed, which its Backspace takes back (C5-3)
		if ( done.Placed && click.Accepted && CoasterPanel is { TakesBack: true } panel && UndoJournal?.LastRecord is { } record )
			panel.PlacedOwnPylon( record );
		builder.SetToolState( done.Next );
		if ( done.Completed )
			FinishCoasterTool( station );
		return done.Result;
	}

	/// <summary>The tool's end after a completed track, on the issuer's machine: the panel closes and the queue tool follows a ride with a queue (the ride's door opened in the command).</summary>
	private void FinishCoasterTool( OriginalObject station )
	{
		CoasterPanel?.Close();
		CoasterPanel = null;
		CoasterToolStation = null;
		CoasterPreview = null;
		if ( station.Entry.HasQueue && station.Runtime.IsAttraction && station.Visitors.HasCells )
		{
			// [BIN:TPW-EXE:0x00526680 queue start] where the coaster tool's click (mode 0x15) hands over to the queue tool it takes the pending queue start (0x52656E tests it, 0x526680 zeroes it),
			// and with none pending it ends the tool (0x5266BC); OpenTPW's coaster completion enters the queue tool either way (UI-031)
			placedQueueStart = null;
			EnterQueueTool( station.Visitors );
		}
	}

	/// <summary>
	/// The level's part of a <see cref="CoasterClickCommand"/>, on every peer: the action message, and after a completion the tool's exit writes (the coaster is no longer being edited, the ride opens
	/// when its controller is built).
	/// </summary>
	internal void AfterCoasterClick( OriginalObject station, CoasterClickDone done )
	{
		LastActionMessage = done.Message;
		if ( !done.Completed )
			return;
		SetBeingEdited( station, false );
		if ( station.Runtime.Coaster is { State: CoasterControllerState.Built } )
			station.Open();
	}

	/// <summary>Removes the track of a station (no refund); the station stays.</summary>
	public bool DeleteCoasterTrack( OriginalObject station )
	{
		if ( IsReadOnlyVisit || GetCoasterBuilder( station ) is not { } builder )
			return false;
		builder.Delete();
		LastActionMessage = "Removed the coaster track.";
		return true;
	}

	// ---- Original save import --------------------------------------------------------------------------

	/// <summary>
	/// Restores the coasters of an original save's Coasters block (<see cref="OriginalCoasterImport"/>), after the stations were placed (their three station nodes built)
	/// and the scripts took their saved run state. Each block binds to its station by the station's mesh instance id, as tp.exe's loader does; a block whose station
	/// is not a placed coaster station refuses the park (the original dereferences a null controller there). Only a park started from an original save does this.
	/// </summary>
	// [BIN:TPW-EXE:0x437CBF coaster bind] per saved coaster the loader walks the controller list 0x790FE0 for the one whose station mesh id (+0x10) is the header's +0xA; the RideSystem loader 0x4647A0 made that controller when it recreated the station's mesh instance (0x463060 → 0x4368F0 with the load flag), and with none matching the loader writes through a null pointer (0x437CD5)
	private void ImportOriginalCoasters()
	{
		if ( resumeState != null || OriginalPark?.Save is not { CoasterBlock: { Count: > 0 } block } save )
			return;
		var scripts = save.Rsse?.Records.Where( record => record.ParentScriptId == 0 ).ToList() ?? new List<SaveRsseRecord>();
		foreach ( var saved in block.Coasters )
		{
			if ( save.CoasterStation( saved ) is not { } attraction )
				throw new InvalidDataException( $"The save's coaster of station mesh {saved.StationMeshInstanceId} binds to no single saved attraction; the original would crash here (0x437CD5)." );
			var station = Objects.Objects.FirstOrDefault( item => !item.IsDeleted && item.ThingId == attraction.Id );
			if ( station?.Runtime.Coaster is not { } ride )
				throw new InvalidDataException( $"The save's coaster of station mesh {saved.StationMeshInstanceId} binds to attraction thing {attraction.Id} (Info.Id {attraction.InfoId}), which this park does not place as a coaster station; the original would crash here (0x437CD5)." );
			var plan = OriginalCoasterImport.Map( saved, ride );
			// the guests were imported before the coasters (Level import order): a train whose car list names a guest who is no rider of the ride loses its lists (reported)
			var (checkedPlan, riderLines) = OriginalCoasterImport.CheckRiders( plan, station.Runtime.Visitors.Riders, station.Runtime.Visitors.SavedRunStateImported, $"The saved coaster of attraction thing {attraction.Id}" );
			plan = checkedPlan;
			ride.ImportOriginal( plan.Track, plan.State, plan.BreakdownState, plan.SeatsPerTrain, plan.Trains );
			// the first frame after the load: no substep runs (0x415140), its coaster update rebuilds keeping the saved trains and runs the controller's first motion tick, which takes no time
			ride.UpdateFirstFrameAfterLoad();
			var lines = plan.Evidence.Concat( riderLines ).Concat( OriginalCoasterImport.Compare( saved, ride ) ).ToList();
			// [BIN:TPW-EXE:0x468760 coaster slot] the header's +0xC goes to the slot allocator as the preferred slot, which stores the controller in 0x790BD0[slot]: a station script whose +0xE0 holds that slot reaches this controller
			var owners = scripts.Where( record => record.ThingId == station.ThingId ).ToList();
			if ( owners.Count == 1 && owners[0].Coast != 0 )
			{
				if ( owners[0].Coast != saved.Slot )
					throw new InvalidDataException( $"The save's station script {owners[0].ScriptId} of attraction thing {attraction.Id} holds coaster slot {owners[0].Coast}, but its coaster's block holds slot {saved.Slot}." );
				ride.BindImportedScript();
			}
			lines.Insert( 0, $"Coaster imported for attraction thing {attraction.Id} (Info.Id {attraction.InfoId}): {ride.Track.PylonCount} pylons, {(ride.Track.IsComplete ? ride.Track.IsCut ? "complete, cut open" : "complete" : "incomplete")}, state {(int)ride.State}, {ride.Trains.Count} trains, speed setting {ride.SpeedSetting}." );
			foreach ( var line in lines )
			{
				Park?.Import?.Report( line );
				Log.Trace( $"Park coaster import: {line}" );
			}
		}
	}

	// ---- Park-resume snapshot -----------------------------------------------------------------------

	/// <summary>
	/// The park-resume coaster part: every live coaster station's controller joined to its attraction id, or null when
	/// the park holds none (such a save carries no coaster part). Call between simulation ticks.
	/// </summary>
	internal CoasterWorldSaveData? CaptureCoastersSnapshot() => CoasterSave.Capture( Objects.ScriptWorld.Coasters, Objects );

	/// <summary>
	/// Restores the saved coasters of a resume snapshot into this candidate level, after the objects were placed (each
	/// station's controller exists, reset to its station node) and their runtimes restored: one entry per live station,
	/// matched by attraction id, in saved (creation) order. A stopped station holds no entry — its controller was torn
	/// down at capture and the runtime restore already replayed that. Throws <see cref="InvalidDataException"/> and
	/// leaves the candidate unrestored on any mismatch.
	/// </summary>
	internal void RestoreCoastersSnapshot( CoasterWorldSaveData? saved )
	{
		if ( saved == null )
			return;
		var live = Coasters.ToHashSet();
		foreach ( var entry in saved.Rides )
		{
			var station = Objects.Objects.FirstOrDefault( item => !item.IsDeleted && item.Runtime.AttractionId == entry.AttractionId );
			if ( station?.Runtime.Coaster is not { } ride )
				throw new InvalidDataException( $"The save holds a coaster for attraction {entry.AttractionId}, which this park does not place as a coaster station." );
			if ( !live.Contains( ride ) )
				throw new InvalidDataException( $"Attraction {entry.AttractionId}'s coaster controller is not live; a stopped station holds no coaster state." );
			ride.RestoreSnapshot( entry );
			// [EXT:coaster-resume-reopen] a save taken while the coaster tool was open holds the ride out of service (state 2, its object closed); the tool is not open after a load, so the ride is let out as leaving the tool would (0x447520 → 0x4df390; the original's save during the tool is not traced)
			// [EXT:online-coop-snapshot-join] not in a join: the host's live coaster stays as it is (its tool open, or closed by the park's door while built)
			if ( joinRestore == null && ride.State == CoasterControllerState.Built && !station.IsOpen )
				station.Open();
		}
		if ( Coasters.Count != saved.Rides.Count )
			throw new InvalidDataException( $"The save holds {saved.Rides.Count} coasters, but this park places {Coasters.Count} live coaster stations." );
	}
}

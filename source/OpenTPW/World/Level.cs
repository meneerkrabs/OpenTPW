using OpenTPW.UI;

namespace OpenTPW;

public partial class Level : IDisposable
{
	internal static Level Current { get; set; }

	public RootPanel Hud { get; set; }
	public Sun SunLight { get; set; }

	public SettingsFile Global { get; private init; }
	private ParkLayout parkLayout = null!;
	internal SandboxTextOverlay TextOverlay { get; private set; } = null!;
	public PrototypeRide? PlacedRide { get; private set; }
	public bool IsPlacing { get; set; }
	public string LastActionMessage { get; private set; } = "";
	/// <summary>Imported original level data; null for the generic sandbox.</summary>
	public OriginalPark? OriginalPark { get; private set; }
	public OriginalTerrain? OriginalTerrain { get; private set; }
	/// <summary>The build tools' blueprint markers (original levels only).</summary>
	public BlueprintMarkers? Blueprint { get; private set; }
	/// <summary>Park management simulation (money, clock, staff, research); original levels only for now.</summary>
	public ParkEconomyRuntime? Park { get; private set; }
	/// <summary>The park's weather, created on the first simulation step of a park; null without a park economy.</summary>
	public ParkWeather? Weather { get; private set; }

	/// <summary>
	/// The park door of an original level: the entrance gate's script and the open/close walk over the
	/// attractions. Null for the sandbox and read-only visits.
	/// </summary>
	public ParkGate? Gate { get; private set; }

	/// <summary>Park name on the gate sign of an imported original level (TrueType sign text demo).</summary>
	public OriginalGateSign? GateSign { get; private set; }
	private bool wasMouseDown;
	private int renderedCellVersion;
	/// <summary>Set each frame by the original HUD when the pointer is over it, so HUD clicks do not build.</summary>
	public bool UiCapturesMouse { get; set; }
	/// <summary>Simulation speed from the HUD speed control (0 = paused).</summary>
	public float SimulationTimeScale { get; set; } = 1f;
	/// <summary>Shows the ImGui developer panel and the sandbox BF4 text panel (off when the original HUD runs alone).</summary>
	public bool ShowDeveloperPanels { get; set; } = true;
	private readonly FixedStepClock simulationClock = new();
	/// <summary>Park visitors (imported original levels only; docs/GUESTS.md).</summary>
	public GuestSimulation? Guests { get; private set; }
	public GuestRenderer? GuestRenderer { get; private set; }
	/// <summary>Heads of riders on rides that have ADDHEAD (docs/GUESTS.md, "Riders' heads").</summary>
	public RideHeadRenderer? RideHeadRenderer { get; private set; }
	/// <summary>The default world seed (<see cref="WorldSeed.DefaultValue"/>); with it the guest stream keeps its old seed.</summary>
	public const ulong GuestSeed = WorldSeed.DefaultValue;
	/// <summary>The one seed every simulation random stream of this level derives from (docs/DETERMINISM.md).</summary>
	public WorldSeed Seed { get; }

	/// <summary>Level directory name, e.g. <c>jungle</c>.</summary>
	public string LevelName { get; }
	/// <summary>
	/// Set when this level shows a shared park read-only (docs/ONLINE.md): building, sandbox saves and
	/// any economy writes must be refused while it is set.
	/// </summary>
	public ParkVisitInfo? Visit { get; }
	public bool IsReadOnlyVisit => Visit != null;
	/// <summary>The version 2 layout a visit applies instead of the shipped level import; null for version 1 packages and own parks.</summary>
	private readonly OpenTPW.Online.Packages.MinimalParkPayload? visitLayout;
	/// <summary>The visit's placed objects, one entry per payload record (null where content was missing); coaster stations are looked up here.</summary>
	private IReadOnlyList<OriginalObject?>? visitedObjects;
	private ParkResumeSnapshot? resumeState;
	// Developer tools only (--sandbox); players use the original-style online screens (OnlineScreens).
	private OnlinePanel? onlinePanel;
	private OnlineFolders onlineFolders = null!;

	/// <param name="start">How a writable original level starts; ignored for the sandbox and for read-only visits (which load the shipped save and run no economy).</param>
	public Level( string levelName, bool loadOriginalLevel = false, ParkVisitInfo? visit = null, OnlineFolders? onlineFolders = null, ParkStartKind start = ParkStartKind.OriginalSaveReference, WorldSeed? seed = null )
		: this( levelName, loadOriginalLevel, visit, onlineFolders, start, seed, null, null ) { }

	internal Level( ParkResumeSnapshot snapshot, ParkEconomy economy, OnlineFolders? onlineFolders = null )
		: this( snapshot.Theme, true, null, onlineFolders, snapshot.StartKind, new WorldSeed( snapshot.Seed ), snapshot, economy ) { }

	private Level( string levelName, bool loadOriginalLevel, ParkVisitInfo? visit, OnlineFolders? onlineFolders,
		ParkStartKind start, WorldSeed? seed, ParkResumeSnapshot? resume, ParkEconomy? resumedEconomy )
	{
		if ( visit != null && visit.Level != levelName )
			throw new ArgumentException( "The visit level must match the loaded level.", nameof( visit ) );
		LevelName = levelName;
		Seed = seed ?? WorldSeed.Default;
		Visit = visit;
		resumeState = resume;
		visitLayout = visit != null && visit.Payload.HasBuiltLayout && !visit.IsSandbox ? visit.Payload : null;
		Global = new SettingsFile( $"/levels/{levelName}/global.sam" );
		Current = this;
		if ( visit != null ? !visit.IsSandbox : loadOriginalLevel )
		{
			OriginalPark = OriginalPark.Load( levelName, readShippedSave: resume == null && (IsReadOnlyVisit || ParkStart.ReadsShippedSave( start )) );
			if ( resume != null && (resume.Layout.Cells.Width != OriginalPark.Map.CellCountX || resume.Layout.Cells.Height != OriginalPark.Map.CellCountY) )
				throw new InvalidDataException( "The saved cell map dimensions do not match the original terrain." );
			// [EXT:tpi-viewer] Theme Park Inc has no economy here: its balance keys differ from Theme Park World's (docs/THEME-PARK-INC.md).
			if ( !IsReadOnlyVisit && !GameEditions.IsThemeParkInc )
				Park = resume == null ? ParkEconomyRuntime.ForOriginalLevel( OriginalPark, start, Seed )
					: ParkEconomyRuntime.ForResume( resumedEconomy ?? throw new InvalidDataException( "The resumed economy is missing." ), start );
		}

		SetupEntities();
		if ( resume != null ) RestoreResumeSimulation( resume );
		SetupHud( onlineFolders );
		resumeState = null;
		if ( visitLayout != null )
			ApplyVisitedLayout();
		if ( visit?.Payload.PrototypeRide is { } ride )
			RestoreVisitedRide( ride );
	}

	/// <summary>
	/// Applies a version 2 shared layout: coaster tracks onto their stations, then a digest check that what this
	/// visit built is the park the owner published (docs/ONLINE.md "Local files and visits"). Cells and objects are
	/// applied inside <see cref="SetupEntities"/> so guests and bridges see them from the start. A package that
	/// publishes no digest cannot be checked and says so instead of reporting a mismatch.
	/// </summary>
	private void ApplyVisitedLayout()
	{
		var skipped = new List<string>();
		if ( visitLayout!.Coasters != null && visitedObjects != null )
		{
			foreach ( var coaster in visitLayout.Coasters )
			{
				var station = coaster.Station < visitedObjects.Count ? visitedObjects[coaster.Station] : null;
				if ( station == null || !SharedParkLayout.RestoreCoaster( this, station, coaster.Nodes, coaster.Closed, coaster.Complete ) )
					skipped.Add( $"coaster at object {coaster.Station}" );
			}
		}
		if ( visitLayout.LayoutDigest == null )
			Log.Warning( "This shared park publishes no layout digest; the built layout cannot be verified against the owner's park." );
		else
		{
			var digest = SharedParkLayout.DigestOf( this );
			if ( digest != visitLayout.LayoutDigest )
			{
				// One first difference of each kind, so a mismatch names a likely cause and not only a hash.
				var builtCells = SharedParkLayout.CaptureCells( Guests?.Grid.Cells ?? new ParkCellMap( 1, 1 ) );
				var publishedCells = visitLayout.Cells ?? Array.Empty<OpenTPW.Online.Packages.ParkCellRecord>();
				var cellDifference = builtCells.Zip( publishedCells ).FirstOrDefault( pair => pair.First != pair.Second );
				var objectDifference = SharedParkLayout.CaptureObjects( this ).Zip( visitLayout.Objects ).FirstOrDefault( pair => pair.First != pair.Second );
				Log.Warning( $"Shared park layout check failed: built {digest}, published {visitLayout.LayoutDigest}; the visit may differ from the owner's park."
					+ (cellDifference.Equals( default ) ? "" : $" First cell difference: built {cellDifference.First}, published {cellDifference.Second}.")
					+ (objectDifference.Equals( default ) ? "" : $" First object difference: built {objectDifference.First}, published {objectDifference.Second}.") );
			}
			else
				Log.Trace( $"Shared park layout applied and verified (digest {digest}: {visitLayout.Cells?.Count ?? 0} cells, {visitLayout.Objects.Count} objects, {visitLayout.Coasters?.Count ?? 0} coasters)." );
		}
		foreach ( var item in skipped )
			Log.Warning( $"Shared park {item} could not be rebuilt and is not shown." );
	}

	/// <summary>Shows the shared park's prototype ride; the visitor cannot move or remove it.</summary>
	private void RestoreVisitedRide( OpenTPW.Online.Packages.PrototypeRideState ride )
	{
		var position = new Vector3( ride.X, ride.Y, 0 );
		var allowed = OriginalPark != null ? CheckOriginalPlacement( position ) == OriginalPlacementResult.Allowed : ParkPlacement.IsWithinBounds( position, PrototypeRide.FootprintRadius );
		if ( !allowed )
		{
			LastActionMessage = "The shared park's ride position is not buildable here; the ride is not shown.";
			return;
		}
		PlacedRide = new PrototypeRide( position, Objects.ScriptWorld );
		RegisterRideWithGuests( PlacedRide );
		if ( ride.Open )
			PlacedRide.Start();
	}

	private void SetupEntities()
	{
		SunLight = new Sun() { Position = new( 0, 100, 100 ) };

		if ( OriginalPark == null )
		{
			_ = new Terrain();
			ParkCameraMode.TargetExtent = ParkCameraMode.SandboxTargetExtent;
			ParkCameraMode.InitialTarget = Vector3.Zero;
		}
		else
		{
			OriginalTerrain = new OriginalTerrain( OriginalPark );
			Blueprint = new BlueprintMarkers( OriginalPark.Heightfield );
			try { GateSign = OriginalGateSign.TryCreate( OriginalPark, Global ); }
			catch ( Exception exception ) when ( exception is IOException or InvalidDataException or KeyNotFoundException )
			{
				Log.Warning( $"Gate sign: {exception.Message}" );
			}
			var origin = OriginalParkPlacement.GetOrigin( OriginalPark.Heightfield );
			ParkCameraMode.TargetExtent = Math.Max( origin.X, origin.Y );
			var save = OriginalPark.Save;
			// Focus the imported paths, else the MAP initial path at the entrance, else the terrain centre.
			var field = OriginalPark.Heightfield;
			var paths = save?.PathCells.ToList() ?? Enumerable.Range( 0, field.CellCountX * field.CellCountZ )
				.Select( index => (X: index % field.CellCountX, Y: index / field.CellCountX) )
				.Where( cell => OriginalPark.Map.GetFlagsAt( cell.X, cell.Y ).HasFlag( MapCellFlags.InitialPath ) ).ToList();
			if ( visitLayout?.PathCells.Count > 0 )
				paths = visitLayout.PathCells.Select( cell => (X: cell.X, Y: cell.Y) ).ToList();
			var focus = paths.Count > 0
				? (X: (int)paths.Average( cell => cell.X ), Y: (int)paths.Average( cell => cell.Y ))
				: (X: field.CellCountX / 2, Y: field.CellCountZ / 2);
			var center = OriginalParkPlacement.GetCellCenter( OriginalPark.Heightfield, focus.X, focus.Y );
			ParkCameraMode.InitialTarget = new Vector3( center.X, center.Y, 0 );
			Log.Trace( $"Original {OriginalPark.LevelName} level: {OriginalTerrain.SurfaceCellCount} heightfield cells, {OriginalTerrain.TerrainMeshCount} terrain meshes"
				+ (save == null ? "; no original save." : $"; Easymode import: {save.PathCells.Count} path cells, {save.PlacedObjects.Count} placed objects, {save.FixedItems.Count} fixed items.")
				+ (visitLayout != null ? $"; shared layout: {visitLayout.Cells?.Count ?? 0} cells, {visitLayout.Objects.Count} objects, {visitLayout.Coasters?.Count ?? 0} coasters." : "") );
		}
		SetupObjects();
		SetupCoasters();
		Camera.SetCameraMode<ParkCameraMode>();
		if ( OriginalPark != null )
		{
			SetupGuests( OriginalPark );
			SetupPaths();
			if ( OriginalTerrain != null && Guests != null )
			{
				var cells = Guests.Grid.Cells;
				OriginalTerrain.PathSource = ( x, y ) => cells.TypeAt( x, y ) == ParkCellType.Path;
				OriginalTerrain.QueueSource = ( x, y ) => cells.TypeAt( x, y ) == ParkCellType.Queue;
				OriginalTerrain.QueueLinkSource = cells.QueueLinkAt;
				if ( resumeState != null ) OriginalTerrain.RefreshPaths();
				renderedCellVersion = cells.Version;
			}
			if ( Park != null && Guests != null )
				Park.AttachGuests( Guests, openParkOnAttach: resumeState == null );
			ConnectObjectsToGuests();
			if ( Guests != null )
				Guests.TrafficLightsPlaced = () => Objects.Objects.Any( item => item.Entry.IsTrafficLights );
			ConnectObjectsToEconomy();
			SetupStaff();
			SetupParkGate();
		}
	}

	private void SetupGuests( OriginalPark park )
	{
		// A version 2 visit walks the shared park's own cell map (the owner's built paths and queues), not the shipped import.
		var grid = resumeState != null ? new GuestPathGrid( ParkLayoutSnapshot.RestoreCellMap( resumeState.Layout ) )
			: visitLayout?.Cells != null
			? new GuestPathGrid( SharedParkLayout.BuildCellMap( park.Map.CellCountX, park.Map.CellCountY, visitLayout.Cells ) )
			: GuestPathGrid.FromOriginal( park.Map, park.Save );
		Guests = new GuestSimulation( grid, GuestSettings.Load( park.LevelName ), Seed.GuestStream ) { OnlineVisit = Visit != null };
		var sprites = GuestSpriteAtlas.LoadKids();
		GuestRenderer = new GuestRenderer( Guests, park.Heightfield, sprites );
		RideHeadRenderer = new RideHeadRenderer( Guests, GuestSpriteAtlas.LoadKidHeads(), Objects );
		Log.Trace( $"Guests: {grid.WalkableCount} walkable path cells, {sprites.Count} original kid sprite sets, {Guests.Settings.Types.Count} .sam peep types." );
	}

	/// <summary>
	/// Wires the park door (docs/GATES.md): the economy's open/close events command the entrance gate's script
	/// and close or open the attractions, and the gate's deferred-close retry runs with the economy tick.
	/// </summary>
	private void SetupParkGate()
	{
		if ( Park == null )
			return;
		Gate = ParkGate.ForLevel( Park.Economy, Objects, Guests, () => Park.Economy.Staff.Members, () => PlacedRide?.Runtime );
		// A park that starts open commands its gate open once at setup, the way the original's gate lookup
		// writes the command from mParkClosed (Mac 0x104500 writes the gate handle and the closed flag together);
		// the initial OpenPark of AttachGuests raises its event before this wiring, so it commands nothing.
		Gate.InitializeFromParkState();
		Park.Economy.EventRaised += item =>
		{
			if ( item.Kind is ParkEventKind.ParkOpened or ParkEventKind.ParkClosed )
				Gate.DoorMoved( item.Kind == ParkEventKind.ParkClosed );
		};
	}

	/// <summary>Walkable path cell next to (or nearest to) a footprint; the prototype Totem has no catalog entrance cell yet.</summary>
	internal static (int X, int Y)? FindRideEntrance( GuestPathGrid grid, int minX, int minY, int maxX, int maxY )
	{
		(int X, int Y)? best = null;
		var bestDistance = int.MaxValue;
		for ( var y = 0; y < grid.CountY; y++ )
		{
			for ( var x = 0; x < grid.CountX; x++ )
			{
				if ( !grid.IsWalkable( x, y ) )
					continue;
				var distance = Math.Max( 0, Math.Max( minX - x, x - maxX ) ) + Math.Max( 0, Math.Max( minY - y, y - maxY ) );
				if ( distance < bestDistance )
				{
					bestDistance = distance;
					best = (x, y);
				}
			}
		}
		return best;
	}

	private void RegisterRideWithGuests( PrototypeRide ride )
	{
		if ( Guests == null || OriginalPark == null || !OriginalParkPlacement.TryGetCell( OriginalPark.Heightfield, ride.Position.X, ride.Position.Y, out var x, out var y ) )
			return;
		const int radius = OriginalRideFootprintRadiusCells;
		var entrance = FindRideEntrance( Guests.Grid, x - radius, y - radius, x + radius, y + radius );
		if ( entrance == null )
			return;
		ride.Visitors.EntranceCell = entrance.Value;
		ride.Visitors.ExitCell = entrance.Value;
		ride.Visitors.HasCells = true;
		Guests.Register( ride.Visitors );
		LinkPrototypeToEconomy( ride );
		Log.Trace( $"{ride.Name}: guests queue and exit at path cell {entrance.Value} (nearest path cell; the prototype has no catalog entrance)." );
	}

	/// <summary>Footprint of the prototype ride in original cells: a square around the clicked cell.</summary>
	public const int OriginalRideFootprintRadiusCells = 2;

	/// <summary>
	/// The published record of a placed object during a version 2 visit (open state, price, state of repair as
	/// the owner left them), matched by Info.Id, anchor and rotation; null outside visits or for unknown objects.
	/// </summary>
	public OpenTPW.Online.Packages.ParkObjectRecord? PublishedObjectState( OriginalObject item )
	{
		if ( Visit?.Payload.Objects == null || item.Entry.IsFixedItem )
			return null;
		return Visit.Payload.Objects.FirstOrDefault( record =>
			record.InfoId == item.Entry.InfoId && record.X == item.Placement.X && record.Y == item.Placement.Y && record.Rotation == item.Placement.Rotation );
	}

	/// <summary>The park's published statistics of a version 2 package, for read-only display; null otherwise.</summary>
	public OpenTPW.Online.Packages.ParkStatsRecord? PublishedParkStats => Visit?.Payload.Stats;

	public bool TryGetPlacementPosition( Vector2 mousePosition, Vector2 viewportSize, out Vector3 position )
	{
		position = Vector3.Zero;
		if ( OriginalPark == null )
		{
			var found = ParkPlacement.TryGetPosition( mousePosition, viewportSize, Camera.ViewMatrix, Camera.ProjMatrix, out var sandboxPosition );
			position = sandboxPosition;
			return found;
		}
		if ( !ParkPlacement.TryGetGroundPoint( mousePosition, viewportSize, Camera.ViewMatrix, Camera.ProjMatrix, out var ground ) )
			return false;
		if ( !OriginalParkPlacement.TryGetCell( OriginalPark.Heightfield, ground.X, ground.Y, out var x, out var y ) )
			return false;
		position = OriginalParkPlacement.GetCellCenter( OriginalPark.Heightfield, x, y );
		return true;
	}

	public OriginalPlacementResult CheckOriginalPlacement( Vector3 position )
	{
		if ( OriginalPark == null || !OriginalParkPlacement.TryGetCell( OriginalPark.Heightfield, position.X, position.Y, out var x, out var y ) )
			return OriginalPlacementResult.OutsideTerrain;
		const int radius = OriginalRideFootprintRadiusCells;
		return OriginalParkPlacement.Check( OriginalPark, x - radius, y - radius, x + radius, y + radius );
	}

	private void SetupHud( OnlineFolders? folders )
	{
		Hud = new();

		parkLayout = new ParkLayout( this );
		onlineFolders = folders ?? OnlineFolders.FromEnvironment();
		TextOverlay = new SandboxTextOverlay();
		// BF4 UI draws at output size after the world blit, not through the world upscaler.
		global::Global.Render.OnOverlayRender += DrawTextOverlay;
	}

	private void DrawTextOverlay()
	{
		if ( ShowDeveloperPanels )
			TextOverlay.Draw();
	}

	/// <summary>Stops drawing this level's overlay (the front end replaces the level).</summary>
	internal void DetachOverlay() => global::Global.Render.OnOverlayRender -= DrawTextOverlay;

	public void Update()
	{
		StaffWorld?.Synchronize();
		if ( RideView?.Ride.IsDeleted == true )
			ExitRideView();
		Camera.Update();
		if ( ShowDeveloperPanels )
			parkLayout.Draw();
		if ( ShowDeveloperPanels )
		{
			onlinePanel ??= new OnlinePanel( this, onlineFolders );
			onlinePanel.Draw();
		}
		UpdateWorldInput( Input.Mouse.Left, UiCapturesMouse || ImGuiNET.ImGui.GetIO().WantCaptureMouse,
			Input.Mouse.Position, new Vector2( Screen.Size.X, Screen.Size.Y ) );
		if ( OriginalTerrain != null && Guests != null && Guests.Grid.Cells.Version != renderedCellVersion )
		{
			renderedCellVersion = Guests.Grid.Cells.Version;
			OriginalTerrain.RefreshPaths();
		}
		AdvanceSimulation( Time.Delta * SimulationTimeScale );
		foreach ( var entity in Entity.All.ToArray() )
			entity.Update();
		// Riding follows the frame just simulated; park-camera input is consumed only by the earlier update.
		if ( RideView != null && ReferenceEquals( Camera.Mode, RideView ) )
			Camera.Update();
	}

	internal enum WorldClickAction { None, PlacePrototype, UseTool }

	/// <summary>World input stays suspended in the ride view, including tools armed through the HUD.</summary>
	internal static WorldClickAction WorldClick( bool riding, bool leftDown, bool previousLeftDown, bool mouseCaptured, bool placing ) =>
		riding || !leftDown || previousLeftDown || mouseCaptured ? WorldClickAction.None
			: placing ? WorldClickAction.PlacePrototype : WorldClickAction.UseTool;

	internal void UpdateWorldInput( bool leftDown, bool mouseCaptured, Vector2 mousePosition, Vector2 viewportSize )
	{
		switch ( WorldClick( RideView != null, leftDown, wasMouseDown, mouseCaptured, IsPlacing ) )
		{
			case WorldClickAction.PlacePrototype:
				if ( TryGetPlacementPosition( mousePosition, viewportSize, out var position ) )
					PlaceRide( position );
				break;
			case WorldClickAction.UseTool:
				HandleObjectClick( mousePosition, viewportSize );
				break;
		}
		// Track the button in both cameras: a held click must not build when the player leaves a ride.
		wasMouseDown = leftDown;
	}

	public bool PlaceRide( Vector3 position )
	{
		if ( IsReadOnlyVisit )
		{
			LastActionMessage = "Read-only visit: building is disabled.";
			return false;
		}
		if ( PlacedRide != null )
			return false;
		if ( OriginalPark != null )
		{
			var result = CheckOriginalPlacement( position );
			if ( result != OriginalPlacementResult.Allowed )
			{
				LastActionMessage = $"Cannot build here: {result}.";
				return false;
			}
		}
		else if ( !ParkPlacement.IsWithinBounds( position, PrototypeRide.FootprintRadius ) )
			return false;
		if ( PrototypeOverlapsObjects( position ) )
		{
			LastActionMessage = "Cannot build here: Occupied.";
			return false;
		}
		PlacedRide = new PrototypeRide( position, Objects.ScriptWorld );
		RegisterRideWithGuests( PlacedRide );
		IsPlacing = false;
		Log.Trace( $"Placed prototype ride at {position}; it runs its original Totem.RSE script." );
		return true;
	}

	public void RemoveRide()
	{
		if ( IsReadOnlyVisit )
			return;
		if ( ReferenceEquals( RideView?.Ride, PlacedRide ) && RideView != null )
			ExitRideView();
		PlacedRide?.Delete();
		if ( PlacedRide != null )
		{
			Guests?.Unregister( PlacedRide.Visitors );
			RemovePrototypeFromEconomy( PlacedRide );
		}
		PlacedRide = null;
		IsPlacing = false;
	}

	public void SaveSandbox()
	{
		RequireSandbox();
		var state = PlacedRide == null ? SandboxSave.CreateEmpty() : new SandboxSave(
			SandboxSave.CurrentVersion, PlacedRide.Position.X, PlacedRide.Position.Y, true, PlacedRide.IsOpen );
		SandboxSave.Save( SaveFileSystem.GetAbsolutePath( "opentpw-sandbox.json" ), state );
		LastActionMessage = "Sandbox saved. This is not an original TPWS save.";
	}

	public void LoadSandbox()
	{
		RequireSandbox();
		var state = SandboxSave.Load( SaveFileSystem.GetAbsolutePath( "opentpw-sandbox.json" ) );
		var position = new Vector3( state.RideX, state.RideY, 0 );
		if ( state.HasRide && !ParkPlacement.IsWithinBounds( position, PrototypeRide.FootprintRadius ) )
			throw new InvalidDataException( "Saved ride footprint is outside the park." );
		var replacement = state.HasRide ? new PrototypeRide( position, Objects.ScriptWorld ) : null;
		if ( state.IsRunning )
			replacement!.Start();
		RemoveRide();
		PlacedRide = replacement;
		simulationClock.Reset();
		LastActionMessage = "Sandbox loaded; the ride script restarts from its first instruction.";
	}

	/// <summary>What the park save, load and canonical hash read from this level.</summary>
	public ParkWorldStreams Streams => new( Seed, Park, Guests, Objects.ScriptWorld, GameAudio.Events );

	/// <summary>The random streams besides the economy's, for the park save.</summary>
	public WorldRandomState CaptureRandomState() => Streams.CaptureRandomState();

	/// <summary>Continues the streams of a park save.</summary>
	public void RestoreRandomState( WorldRandomState state ) => Streams.RestoreRandomState( state );

	/// <summary>Canonical state hash of this level (<see cref="WorldStateHash"/>; the sound seed is not part of it).</summary>
	public ulong ComputeStateHash() => Streams.ComputeStateHash();

	/// <summary>Writes the park economy and every random stream to an OpenTPW park save.</summary>
	public void SavePark( string path ) => Streams.SavePark( path );

	/// <summary>Loads an OpenTPW park save: the economy and, when the save has them, the random streams. Guests and object scripts keep running (DET-016).</summary>
	public void LoadPark( string path )
	{
		Streams.LoadPark( path );
		// The saved open state goes back into the running scripts; SyncObjectEconomy would otherwise overwrite it with the live one on the next tick (park points count only open rides, ARRIVAL-003).
		foreach ( var (item, instance) in economyInstances )
		{
			if ( Park == null || !Park.Economy.TryGetObject( instance, out var state ) || state.IsOpen == item.IsOpen )
				continue;
			if ( state.IsOpen )
				item.Open();
			else
				item.Close();
		}
	}

	private void RequireSandbox()
	{
		if ( IsReadOnlyVisit )
			throw new InvalidOperationException( "Saving and loading are disabled while visiting a shared park." );
		if ( OriginalPark != null )
			throw new InvalidOperationException( "Sandbox saves are disabled for imported original levels; original saves are read-only." );
	}

	public void Dispose()
	{
		ExitRideView();
		onlinePanel?.Dispose();
		DetachOverlay();
		TextOverlay?.Dispose();
	}

	public void Render()
	{
		foreach ( var entity in Entity.All.ToArray() )
			entity.Render();
	}
}

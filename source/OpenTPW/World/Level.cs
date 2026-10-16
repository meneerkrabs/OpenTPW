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
	private bool isPlacing;
	/// <summary>The prototype ride is being placed; starting it ends a staff tool (one park tool at a time).</summary>
	public bool IsPlacing
	{
		get => isPlacing;
		set
		{
			if ( value )
			{
				StaffPlacement?.Cancel();
				PatrolTool?.Cancel();
				BuyLandTool?.Cancel();
			}
			isPlacing = value;
		}
	}
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

	/// <summary>The park's name: the gate's two sign lines (saved with the park), or null for a level without a gate sign.</summary>
	public ParkNameLines? ParkName { get; private set; }

	/// <summary>
	/// The park name and the gate sign of an original level: a resumed park keeps its saved lines, which the sign draws as the
	/// original's rebuild after loading does (0x468500); a new park takes the gate's default lines.
	/// </summary>
	private void SetupParkName()
	{
		var savedName = resumeState?.ParkName is { } saved ? new ParkNameLines( saved.Line1, saved.Line2 ) : null;
		if ( OriginalPark != null )
		{
			try { GateSign = OriginalGateSign.TryCreate( OriginalPark, Global, savedName, reloaded: savedName != null ); }
			catch ( Exception exception ) when ( exception is IOException or InvalidDataException or KeyNotFoundException )
			{
				Log.Warning( $"Gate sign: {exception.Message}" );
			}
		}
		ParkName = savedName ?? GateSign?.Lines;
	}

	/// <summary>
	/// Renames the park (the ticket price window's rename dialog): stores the two lines and redraws the gate sign. False for
	/// a read-only visit, a level without a park name, or lines a save could not hold (<see cref="ParkNameLines.IsValid"/>).
	/// </summary>
	// [APPROX:COMPAT-007] the rename stores the lines and redraws the gate (0x4993E0); the handler's copies to the online park record (0x409410, 0x5B0940/0x5B0970) and its secret name ("Kevin the" / "Cosmic Cow" at 0x751A34/0x751A38, calling 0x4053C0) are not built, and gate lines of original saves are not imported — evidence needed: the gate-line field of original saves and what 0x5B0940/0x5B0970 and 0x4053C0 drive
	public bool RenamePark( string line1, string line2 )
	{
		if ( IsReadOnlyVisit || ParkName == null || line1 == null || line2 == null )
			return false;
		var lines = new ParkNameLines( line1, line2 );
		if ( !lines.IsValid )
			return false;
		ParkName = lines;
		GateSign?.Rename( lines );
		return true;
	}
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
	/// <summary>Draws the guests in costume with the theme's costume set (null when the theme ships none: they keep their kid sprite).</summary>
	public GuestRenderer? CostumeRenderer { get; private set; }
	/// <summary>Draws the heads of riders in costume with the theme's costume heads (Jungle and Space only).</summary>
	public RideHeadRenderer? CostumeHeadRenderer { get; private set; }
	/// <summary>Draws the guests' balloons.</summary>
	public BalloonRenderer? BalloonRenderer { get; private set; }
	/// <summary>Draws the guests' and the staff's thought bubbles.</summary>
	public ThoughtRenderer? ThoughtRenderer { get; private set; }
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
	/// <summary>True while this level runs the experimental Theme Park Inc slice: no economy, HUD or advisor of the reference game.</summary>
	public bool IsThemeParkIncSlice => GameEditions.IsThemeParkInc && OriginalPark != null;
	/// <summary>The version 2 layout a visit applies instead of the shipped level import; null for version 1 packages and own parks.</summary>
	private readonly OpenTPW.Online.Packages.MinimalParkPayload? visitLayout;
	/// <summary>The visit's placed objects, one entry per payload record (null where content was missing); coaster stations are looked up here.</summary>
	private IReadOnlyList<OriginalObject?>? visitedObjects;
	private ParkResumeSnapshot? resumeState;
	// Developer tools only (--sandbox); players use the original-style online screens (OnlineScreens).
	private OnlinePanel? onlinePanel;
	/// <summary>The developer online panel's folder; null while the online extension is off (no panel then).</summary>
	private OnlineFolders? onlineFolders;

	/// <param name="start">How a writable original level starts; ignored for the sandbox and for read-only visits (which load the shipped save and run no economy).</param>
	private static readonly List<WeakReference<Level>> created = new();

	/// <summary>Levels still reachable (the current one included): after a full collection, more than one means an old park is kept alive.</summary>
	public static int AliveCount
	{
		get
		{
			lock ( created )
			{
				created.RemoveAll( reference => !reference.TryGetTarget( out _ ) );
				return created.Count;
			}
		}
	}

	public Level( string levelName, bool loadOriginalLevel = false, ParkVisitInfo? visit = null, OnlineFolders? onlineFolders = null, ParkStartKind start = ParkStartKind.OriginalSaveReference, WorldSeed? seed = null )
		: this( levelName, loadOriginalLevel, visit, onlineFolders, start, seed, null, null, overFrames: false ) { }

	internal Level( ParkResumeSnapshot snapshot, ParkEconomy economy, OnlineFolders? onlineFolders = null )
		: this( snapshot.Theme, true, null, onlineFolders, snapshot.StartKind, new WorldSeed( snapshot.Seed ), snapshot, economy, overFrames: false ) { }

	/// <summary>
	/// A level whose construction runs step by step through <see cref="Step"/>, so a game frame can build part of it and draw
	/// (docs/BROWSER-PERFORMANCE.md, "Building a park over frames"). The steps and their order are those of the constructor:
	/// the finished level is the same. Each step expects the level's scene to be the current one (<see cref="EntityConstructionScope"/>).
	/// </summary>
	internal static Level BeginOverFrames( string levelName, bool loadOriginalLevel, ParkVisitInfo? visit, OnlineFolders? onlineFolders, ParkStartKind start ) =>
		new( levelName, loadOriginalLevel, visit, onlineFolders, start, null, null, null, overFrames: true );

	private Level( string levelName, bool loadOriginalLevel, ParkVisitInfo? visit, OnlineFolders? onlineFolders,
		ParkStartKind start, WorldSeed? seed, ParkResumeSnapshot? resume, ParkEconomy? resumedEconomy, bool overFrames, ThemeParkIncLayoutCheckpoint? checkpoint = null )
	{
		if (checkpoint != null && !GameEditions.IsThemeParkInc)
			throw new NotSupportedException("TPI layout checkpoints require Theme Park Inc data.");
		incLayout = checkpoint;
		if ( visit != null && visit.Level != levelName )
			throw new ArgumentException( "The visit level must match the loaded level.", nameof( visit ) );
		LevelName = levelName;
		lock ( created )
		{
			created.RemoveAll( reference => !reference.TryGetTarget( out _ ) );
			created.Add( new WeakReference<Level>( this ) );
		}
		Seed = seed ?? WorldSeed.Default;
		Visit = visit;
		resumeState = resume;
		visitLayout = visit != null && visit.Payload.HasBuiltLayout && !visit.IsSandbox ? visit.Payload : null;
		Global = new SettingsFile( $"/levels/{levelName}/global.sam" );
		Current = this;
		construction = Construct( visit != null ? !visit.IsSandbox : loadOriginalLevel, start, onlineFolders, resume, resumedEconomy ).GetEnumerator();
		if ( !overFrames )
			while ( Step() ) { }
	}

	private IEnumerator<float>? construction;

	/// <summary>How far the construction has come, from 0 to 1 (a rough share of the work, for a progress bar).</summary>
	internal float ConstructionProgress { get; private set; }

	/// <summary>Runs the next construction step; false once the level is complete. A failed step leaves the level to be disposed.</summary>
	internal bool Step()
	{
		if ( construction == null )
			return false;
		if ( construction.MoveNext() )
		{
			ConstructionProgress = construction.Current;
			return true;
		}
		construction.Dispose();
		construction = null;
		ConstructionProgress = 1;
		return false;
	}

	/// <summary>The constructor's work; each value is a point where a frame may end, with the share of the work done.</summary>
	private IEnumerable<float> Construct( bool original, ParkStartKind start, OnlineFolders? onlineFolders, ParkResumeSnapshot? resume, ParkEconomy? resumedEconomy )
	{
		if ( original )
		{
			using ( LoadProfile.Measure( "original-park" ) )
			{
				var parts = 0;
				foreach ( var loaded in OriginalPark.LoadSteps( LevelName, readShippedSave: incLayout == null && resume == null && (IsReadOnlyVisit || ParkStart.ReadsShippedSave( start )) ) )
				{
					if ( loaded == null )
						yield return Math.Min( 0.14f, 0.004f * ++parts );
					else
						OriginalPark = loaded;
				}
			}
			if ( resume != null && (resume.Layout.Cells.Width != OriginalPark.Map.CellCountX || resume.Layout.Cells.Height != OriginalPark.Map.CellCountY) )
				throw new InvalidDataException( "The saved cell map dimensions do not match the original terrain." );
			yield return 0.15f;
			// [EXT:tpi-viewer] the edition capability policy decides the economy: Theme Park Inc's Standard.sam lacks 89 of the
			// balance keys it reads, so its worlds run without one and building stays free (docs/THEME-PARK-INC.md).
			using ( LoadProfile.Measure( "economy" ) )
			{
				if ( !IsReadOnlyVisit && GameEditions.Capabilities.Economy )
					Park = resume == null ? ParkEconomyRuntime.ForOriginalLevel( OriginalPark, start, Seed )
						: ParkEconomyRuntime.ForResume( resumedEconomy ?? throw new InvalidDataException( "The resumed economy is missing." ), start );
			}
			yield return 0.2f;
		}

		using ( LoadProfile.Measure( "entities" ) )
		{
			foreach ( var progress in SetupEntities() )
				yield return progress;
		}
		if ( resume != null ) RestoreResumeSimulation( resume );
		using ( LoadProfile.Measure( "level-hud" ) )
			SetupHud( onlineFolders );
		resumeState = null;
		incLayout = null;
		incLayoutCells = null;
		if ( visitLayout != null )
			ApplyVisitedLayout();
		if ( Visit?.Payload.PrototypeRide is { } ride )
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

	private IEnumerable<float> SetupEntities()
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
			using ( LoadProfile.Measure( "terrain" ) )
			{
				foreach ( var built in OriginalTerrain.BuildSteps( OriginalPark ) )
				{
					if ( built == null )
						yield return 0.3f;
					else
						OriginalTerrain = built;
				}
			}
			yield return 0.35f;
			Blueprint = new BlueprintMarkers( OriginalPark.Heightfield );
			SetupParkName();
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
		yield return 0.4f;
		using ( LoadProfile.Measure( "objects" ) )
		{
			foreach ( var share in SetupObjects() )
				yield return 0.4f + 0.35f * share;
		}
		using ( LoadProfile.Measure( "coasters" ) )
			SetupCoasters();
		Camera.SetCameraMode<ParkCameraMode>();
		yield return 0.8f;
		if ( OriginalPark != null )
		{
			using ( LoadProfile.Measure( "guests" ) )
				SetupGuests( OriginalPark );
			yield return 0.9f;
			using ( LoadProfile.Measure( "paths" ) )
			{
				SetupPaths();
				SetupTracks();
				SetupTours();
			}
			if ( OriginalTerrain != null && Guests != null )
			{
				var cells = Guests.Grid.Cells;
				OriginalTerrain.PathSource = ( x, y ) => cells.TypeAt( x, y ) == ParkCellType.Path;
				OriginalTerrain.QueueSource = ( x, y ) => cells.TypeAt( x, y ) == ParkCellType.Queue;
				OriginalTerrain.QueueLinkSource = cells.QueueLinkAt;
				if ( resumeState != null || incLayout != null ) OriginalTerrain.RefreshPaths();
				renderedCellVersion = cells.Version;
			}
			if ( Park != null && Guests != null )
				Park.AttachGuests( Guests, openParkOnAttach: resumeState == null );
			ConnectObjectsToGuests();
			if ( Guests != null )
				Guests.WeatherDropsFalling = () => Weather is { CurrentDrops: > 0 };
			using var connectProfile = LoadProfile.Measure( "staff-gate-economy" );
			ConnectObjectsToEconomy();
			SetupStaff();
			SetupParkGate();
		}
	}

	private void SetupGuests( OriginalPark park )
	{
		var settings = GuestSettings.Load( park.LevelName );
		var land = LandSetup( park, settings );
		// A version 2 visit walks the shared park's own cell map (the owner's built paths and queues), not the shipped import.
		// A resumed save from before land existed gets the level's land as a new park (or the imported save's) would.
		var grid = incLayoutCells != null ? new GuestPathGrid(incLayoutCells)
			: resumeState != null ? new GuestPathGrid( ParkLayoutSnapshot.RestoreCellMap( resumeState.Layout, land == null ? null : cells => cells.SetUpLand( land, park.Save ) ) )
			: visitLayout?.Cells != null
			? new GuestPathGrid( SharedParkLayout.BuildCellMap( park.Map.CellCountX, park.Map.CellCountY, visitLayout.Cells ) )
			: new GuestPathGrid( ParkCellMap.FromOriginal( park.Map, park.Save, land, park.Heightfield ) );
		BuyLandTool = grid.Cells.Land != null && !IsReadOnlyVisit
			? new BuyLandTool( () => Guests?.Grid.Cells, () => Park?.Economy, ( x, y ) => ParkLand.IsBarrier( park.Map, park.Heightfield, x, y ) )
			: null;
		Guests = new GuestSimulation( grid, settings, Seed.GuestStream ) { OnlineVisit = Visit != null };
		List<GuestSpriteAtlas> sprites;
		using ( LoadProfile.Measure( "guest-sprites" ) )
			sprites = GuestSpriteAtlas.LoadKids();
		// A guest in costume is drawn with the theme's costume set (sprite category 2, tp.exe 0x004FE65E: +0x20 from 0x00541F70(2)), its head on a ride with the costume heads where the theme has them
		var costumes = GuestSpriteAtlas.LoadCostumes( park.LevelName );
		var costumeHeads = GuestSpriteAtlas.LoadCostumeHeads( park.LevelName );
		GuestRenderer = new GuestRenderer( Guests, park.Heightfield, sprites ) { Draws = costumes.Count == 0 ? null : guest => !guest.InCostume };
		if ( costumes.Count > 0 )
			CostumeRenderer = new GuestRenderer( Guests, park.Heightfield, costumes ) { Draws = guest => guest.InCostume };
		RideHeadRenderer = new RideHeadRenderer( Guests, GuestSpriteAtlas.LoadKidHeads(), Objects ) { Draws = costumeHeads.Count == 0 ? null : guest => !guest.InCostume };
		RideHeadRenderer.AddSource( new TrackHeadSources( () => trackRenderers.Values ) );
		RideHeadRenderer.AddSource( new TourHeadSources( () => tourRenderer ) );
		AddCoasterHeads( RideHeadRenderer );
		if ( costumeHeads.Count > 0 )
		{
			CostumeHeadRenderer = new RideHeadRenderer( Guests, costumeHeads, Objects ) { Draws = guest => guest.InCostume };
			CostumeHeadRenderer.AddSource( new TrackHeadSources( () => trackRenderers.Values ) );
			CostumeHeadRenderer.AddSource( new TourHeadSources( () => tourRenderer ) );
			AddCoasterHeads( CostumeHeadRenderer );
		}
		BalloonRenderer = new BalloonRenderer( Guests, park.Heightfield, GuestSpriteAtlas.LoadBalloons() );
		ThoughtRenderer = new ThoughtRenderer( Guests, park.Heightfield, GuestSpriteAtlas.LoadThoughts(),
			() => Park?.Economy.Staff.Members ?? (IReadOnlyList<StaffMember>)Array.Empty<StaffMember>(),
			member => member.State != StaffState.PickedUp && StaffRenderer != null && StaffRenderer.TryGetDrawCell( member, out var x, out var y ) ? (x, y) : null );
		Log.Trace( $"Guests: {grid.WalkableCount} walkable path cells, {sprites.Count} original kid sprite sets, {costumes.Count} costume sets, {Guests.Settings.Types.Count} .sam peep types." );
	}

	/// <summary>The Buy Land tool (tp.exe mouse mode 0x39); null for a level without land (no Hoardings.sam, Theme Park Inc, a visit).</summary>
	public BuyLandTool? BuyLandTool { get; private set; }

	/// <summary>The level's park fence: its gate posts beside the entrance cells and its <c>Hoardings.sam</c>; null without them.</summary>
	private static ParkLandSetup? LandSetup( OriginalPark park, GuestSettings settings )
	{
		if ( GameEditions.IsThemeParkInc )
			return null;
		var path = $"/levels/{park.LevelName}/Hoardings.sam";
		var setup = ParkLandSetup.FromLevel( settings.EntranceA, settings.EntranceB, FileSystem.FileExists( path ) ? FileSystem.ReadAllText( path ) : null );
		if ( setup == null )
			Log.Warning( $"Level {park.LevelName} has no park fence (entrance cells or {path}); every cell counts as owned." );
		return setup;
	}

	/// <summary>The visitor under the mouse, in or out of costume: the nearer of the two renderers' picks.</summary>
	internal Guest? PickVisitor( System.Numerics.Vector2 mouse, System.Numerics.Vector2 viewport, System.Numerics.Matrix4x4 view, System.Numerics.Matrix4x4 projection )
	{
		float kidDepth = float.PositiveInfinity, costumeDepth = float.PositiveInfinity;
		var kid = GuestRenderer?.PickVisitor( mouse, viewport, view, projection, out kidDepth );
		var costumed = CostumeRenderer?.PickVisitor( mouse, viewport, view, projection, out costumeDepth );
		if ( kid == null || costumed == null )
			return kid ?? costumed;
		return costumeDepth < kidDepth || (costumeDepth == kidDepth && costumed.Id < kid.Id) ? costumed : kid;
	}

	/// <summary>
	/// Wires the park door (docs/GATES.md): the economy's open/close events command the entrance gate's script
	/// and close or open the attractions, and the gate's deferred-close retry runs with the economy tick.
	/// </summary>
	private void SetupParkGate()
	{
		if ( Park == null )
			return;
		BuildParkGate();
		// The runtime forwards the events of whichever economy it holds, so the wiring survives an in-place load (LoadPark replaces the economy).
		Park.EventRaised += item =>
		{
			if ( item.Kind is ParkEventKind.ParkOpened or ParkEventKind.ParkClosed )
				Gate?.DoorMoved( item.Kind == ParkEventKind.ParkClosed );
			else if ( item.Kind == ParkEventKind.Bankrupt )
				Gate?.ParkLost();
			else if ( item.Kind == ParkEventKind.RideScriptVariable && Enum.IsDefined( (RideVariables)item.InfoId ) )
				AttractionRuntimeOf( item.InstanceId )?.SetVariable( ((RideVariables)item.InfoId).ToString(), (int)item.Amount );
			else if ( item.Kind == ParkEventKind.RideRepaired )
				OpenAfterRepair( item.InstanceId );
			else if ( item.Kind is ParkEventKind.UpgradeBought or ParkEventKind.MaintenanceRequested )
				CloseForUpgrade( item.InstanceId );
			else if ( item.Kind == ParkEventKind.MaintenanceCancelled )
				OpenAfterCancelledCall( item.InstanceId );
			else if ( item.Kind == ParkEventKind.ToiletCleaned )
				OpenAfterClean( item.InstanceId );
			else if ( item.Kind == ParkEventKind.UpgradeCompleted )
			{
				ApplyCompletedCatalogueTier( item.InstanceId );
				// [BIN:TPW-EXE:0x55A100 upgrade effect] the type's UpgradeParticleEffect starts when the upgrade is installed (0x4df8f0)
				AttractionRuntimeOf( item.InstanceId )?.StartObjectEffect( ObjectParticleEffect.Upgrade );
				OpenAfterRepair( item.InstanceId );
			}
		};
	}

	/// <summary>The park door over the current economy; rebuilt by <see cref="LoadPark"/>, which replaces the economy.</summary>
	private void BuildParkGate()
	{
		Gate = ParkGate.ForLevel( Park!.Economy, Objects, Guests, () => Park.Economy.Staff.Members, () => PlacedRide?.Runtime, EconomyStateOf );
		// A park that starts open commands its gate open once at setup, the way the original's gate lookup
		// writes the command from mParkClosed (Mac 0x104500 writes the gate handle and the closed flag together);
		// the initial OpenPark of AttachGuests (an imported park saved open) raises its event before this wiring, so it commands nothing.
		Gate.InitializeFromParkState();
	}

	/// <summary>
	/// A purchased upgrade puts the ride in state 2 (<c>0xdf928</c> → <c>SetState(2)</c>): it closes until the upgrade is installed, which the reopening guard
	/// enforces too (<see cref="RideOpening.MayOpen"/>). Without this the ride kept taking guests while its wear was stopped for the upgrade.
	/// </summary>
	// [BIN:STP-PPC:0x100DF928 upgrade start] the upgrade start writes state 2, which clears the open word +100 and writes VAR_RIDECLOSED = 1 (0xe0c3c, ORACLE.md F27)
	private void CloseForUpgrade( int instance )
	{
		if ( AttractionRuntimeOf( instance ) is not { } runtime )
			return;
		runtime.Close();
		SyncOpenState( runtime );
	}

	/// <summary>The economy record takes the script's open state at once, so a park save before the next tick keeps it (SyncObjectEconomy does it each tick).</summary>
	private void SyncOpenState( OriginalObjectRuntime runtime )
	{
		if ( EconomyStateOf( runtime ) is { } state )
			state.IsOpen = runtime.IsOpen;
	}

	/// <summary>The attraction runtime linked to an economy instance (a placed object or the prototype ride), or null.</summary>
	private OriginalObjectRuntime? AttractionRuntimeOf( int instance )
	{
		OriginalObjectRuntime? runtime = null;
		foreach ( var (item, linked) in economyInstances )
			if ( linked == instance )
				runtime = item.Runtime;
		runtime ??= prototypeInstance == instance ? PlacedRide?.Runtime : null;
		return runtime is { IsAttraction: true } ? runtime : null;
	}

	/// <summary>The economy record behind a placed object's runtime (or the prototype ride's), or null.</summary>
	private ParkObjectState? EconomyStateOf( OriginalObjectRuntime runtime )
	{
		if ( Park == null )
			return null;
		foreach ( var (item, instance) in economyInstances )
			if ( item.Runtime == runtime )
				return Park.Economy.TryGetObject( instance, out var state ) ? state : null;
		return prototypeInstance is int id && PlacedRide?.Runtime == runtime && Park.Economy.TryGetObject( id, out var prototype ) ? prototype : null;
	}

	/// <summary>
	/// A finished repair opens the ride whatever the reopening guard says (<c>0xdef2c</c>: the open word 1, VAR_RIDECLOSED 0, SetState(0); it only
	/// logs a warning when <c>0xde654</c> refuses), except that a coaster without a complete circuit is closed again (ORACLE.md F27).
	/// </summary>
	// [BIN:STP-PPC:0x100DF4D4 repair opens the ride] 0xdf4a0 open word = 1, 0xdf4c8 VAR_RIDECLOSED = 0, SetState(0); 0xdf4dc closes an incomplete coaster again
	// [BIN:STP-PPC:0x100DF928 upgrade end] the upgrade's end leaves state 2 through SetState(0) like the repair (ORACLE.md F27, static): the same reopening, an incomplete coaster closed again
	/// <summary>A cancelled call for a mechanic reopens the ride unless its state keeps it closed (tp.exe <c>0x004DFE30</c> off, the reopen guard).</summary>
	private void OpenAfterCancelledCall( int instance )
	{
		if ( AttractionRuntimeOf( instance ) is { } runtime && Gate?.MayOpen( runtime ) == true )
		{
			runtime.Open();
			SyncOpenState( runtime );
		}
	}

	/// <summary>
	/// A handyman's finished clean opens the toilet whatever the reopening guard says, called or not, the park open or closed (tp.exe <c>0x004D73C0</c>
	/// state 11: the clean <c>0x004DFD80</c>, then <c>0x004DF390</c>).
	/// </summary>
	// [BIN:TPW-EXE:0x004DF390 open after clean] the guard's refusal (states 1, 4, 2, the call +0x64, the queue link 0x004DE4A0, a broken track) only logs "Opening non-openable" five times; it then writes the open word +0x68 = 1, script variable 6 (VAR_RIDECLOSED) = 0 and SetState(0) (0x004E0E60)
	private void OpenAfterClean( int instance )
	{
		if ( AttractionRuntimeOf( instance ) is not { } runtime )
			return;
		runtime.Open();
		SyncOpenState( runtime );
	}

	private void OpenAfterRepair( int instance )
	{
		if ( AttractionRuntimeOf( instance ) is not { } runtime )
			return;
		var coaster = runtime.Coaster != null;
		if ( RideRepair.OpensAfterRepair( coaster ? RideOpening.CoasterTrackType : 0, !coaster || runtime.Coaster!.IsOperational ) )
			runtime.Open();
		else
			runtime.Close();
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
		// [EXT:extension-online-gate] the online folder and the developer online panel exist only while the online extension is on
		onlineFolders = FeatureGates.Online ? folders ?? OnlineFolders.FromEnvironment() : null;
		TextOverlay = new SandboxTextOverlay();
		// BF4 UI draws at output size after the world blit, not through the world upscaler.
		global::Global.Render.OnOverlayRender += DrawTextOverlay;
	}

	private void DrawTextOverlay()
	{
		// the ride view hides the whole HUD, the developer panels included (the camcorder shows only the viewfinder, the eject button and the advisor)
		if ( ShowDeveloperPanels && !HidesInterface )
			TextOverlay.Draw();
	}

	/// <summary>Stops drawing this level's overlay (the front end replaces the level).</summary>
	internal void DetachOverlay() => global::Global.Render.OnOverlayRender -= DrawTextOverlay;

	public void Update()
	{
		StaffPlacement?.Reconcile();
		StaffWorld?.Synchronize();
		// the ride ends when it is deleted or loses what the view rides on (a coaster's train)
		if ( RideView is { } riding && (riding.Ride.IsDeleted || !RideCameraMode.CanRide( riding.Ride )) )
			ExitRideView();
		Camera.Update();
		if ( ShowDeveloperPanels && !HidesInterface )
			parkLayout.Draw();
		if ( ShowDeveloperPanels && !HidesInterface && onlineFolders != null )
		{
			onlinePanel ??= new OnlinePanel( this, onlineFolders );
			onlinePanel.Draw();
		}
		UpdateWorldInput( Input.Mouse.Left, UiCapturesMouse || ImGuiNET.ImGui.GetIO().WantCaptureMouse,
			Input.Mouse.Position, new Vector2( Screen.Size.X, Screen.Size.Y ), Input.ModifierMaskDown );
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

	/// <summary>World input stays suspended in ride and walking views, including tools armed through the HUD.</summary>
	internal static WorldClickAction WorldClick( bool riding, bool leftDown, bool previousLeftDown, bool mouseCaptured, bool placing ) =>
		riding || !leftDown || previousLeftDown || mouseCaptured ? WorldClickAction.None
			: placing ? WorldClickAction.PlacePrototype : WorldClickAction.UseTool;

	/// <param name="modifierMask">The modifier mask held at the click (<see cref="Input.ModifierMask"/>: Shift 3, Ctrl 0xc, Alt 0x30); exactly Ctrl keeps the build tool armed after a placement.</param>
	internal void UpdateWorldInput( bool leftDown, bool mouseCaptured, Vector2 mousePosition, Vector2 viewportSize, int modifierMask = 0 )
	{
		clickModifierMask = modifierMask;
		switch ( WorldClick( RideView != null || FreeWalk != null, leftDown, wasMouseDown, mouseCaptured, IsPlacing ) )
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

	/// <summary>
	/// The park's live attractions for the All Attractions window: every placed attraction plus the developer
	/// prototype ride, which lives beside <see cref="Objects"/> rather than inside it. The two sources share the
	/// script world's attraction ids, so this is where they merge into one row each without duplicates.
	/// </summary>
	// [EXT:developer-prototype] the original window had no developer prototype to list; the Totem joins the placed rides as one more row
	public IReadOnlyList<OriginalObject> ListAttractions() =>
		Objects.Objects.Where( item => !item.IsDeleted && item.Runtime.IsAttraction )
			.Append( PlacedRide )
			.Where( item => item is { IsDeleted: false, Runtime.IsAttraction: true } )
			.DistinctBy( item => item.Runtime.AttractionId )
			.ToArray();

	/// <summary>
	/// Removes one listed attraction the way it was placed: a catalogue object through the object world (which
	/// sells its economy record and unlinks guest payments), the prototype ride through <see cref="RemoveRide"/>
	/// (ride view, guests, script and economy stay consistent — <see cref="ParkObjects.Remove"/> does not own it).
	/// Read-only visits refuse here and change nothing. Returns true when the park no longer holds the item.
	/// </summary>
	public bool RemoveAttraction( OriginalObject item )
	{
		if ( IsReadOnlyVisit )
			return false;
		if ( PlacedRide != null && ReferenceEquals( item, PlacedRide ) )
		{
			RemoveRide();
			return true;
		}
		return !item.IsDeleted && Objects.Remove( item );
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
	public void SavePark( string path )
	{
		StaffPlacement?.PrepareForSave();
		Streams.SavePark( path );
	}

	/// <summary>Loads an OpenTPW park save: the economy and, when the save has them, the random streams. Guests and object scripts keep running (the legacy economy-only save, [EXT:legacy-economy-save]).</summary>
	public void LoadPark( string path )
	{
		// a load that fails leaves the economy, and so the staff tool and its pending flag, as they were
		Streams.LoadPark( path );
		StaffPlacement?.Abandon();
		// the patrol tool's employee id belongs to the replaced economy
		PatrolTool?.Cancel();
		BuyLandTool?.Cancel();
		AttachStaffWorld();
		AttachCoasterChallenges();
		// The gate holds the economy it was built over; the load replaced it.
		if ( Park != null && Gate != null )
			BuildParkGate();
		// The saved open state goes back into the running scripts; SyncObjectEconomy would otherwise overwrite it with the live one on the next tick (park points count only open rides, ARRIVAL-003).
		foreach ( var (item, instance) in economyInstances )
		{
			// the replaced economy's environment grid holds the litter and the staff rows only: the standing objects put theirs back
			Park?.Economy.PlaceObjectEffects( instance, (item.Placement.X, item.Placement.Y) );
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
		ExitFreeWalk();
		onlinePanel?.Dispose();
		foreach ( var renderer in trackRenderers.Values )
			renderer.Dispose();
		trackRenderers.Clear();
		tourRenderer?.Dispose();
		tourRenderer = null;
		DetachOverlay();
		TextOverlay?.Dispose();
	}

	public void Render()
	{
		foreach ( var entity in Entity.All.ToArray() )
			entity.Render();
	}
}

using System.Text.Json.Nodes;

namespace OpenTPW;

/// <summary>
/// One pass of the M4 scenario (docs/M4-GATE.md): the catalog inventory, a headless park per object for its lifecycle,
/// the track-ride probes, the corpus opcode scan and the per-minute state hashes. A <see cref="RideOpcodeRecorder"/>
/// watches every script the pass runs, for the effect-hook row.
/// </summary>
internal sealed class M4GateRun
{
	private const float Tick = (float)FixedStepClock.TickDuration;
	/// <summary>Path cells laid from the entrance into the park before the object is placed (the M3 gate's spine).</summary>
	private const int SpineLength = 14;
	private static readonly long MinuteTicks = M3Gate.TicksForMinutes( 1 );

	private readonly M4GateOptions options;
	private readonly int runIndex;
	private readonly long window;
	private RideOpcodeRecorder recorder = null!;

	private M4GateRun( M4GateOptions options, int runIndex )
	{
		this.options = options;
		this.runIndex = runIndex;
		// [APPROX:GATEM4-001] a working attraction admits a queued guest within W, releases a boarded guest within W, and after closing releases its riders within W; W = 5 simulated minutes (18,000 fixed ticks) by default — evidence needed: a traced bound on one ride cycle (admission, ride, unload) per ride type
		window = M3Gate.TicksForMinutes( options.UseWindowMinutes );
	}

	public List<M4GateRow> Rows { get; } = new();
	public List<(string Key, ulong Hash)> MinuteHashes { get; } = new();

	public static M4GateRun Execute( M4GateOptions options, int runIndex )
	{
		var run = new M4GateRun( options, runIndex );
		using var recorder = RideOpcodeRecorder.Start();
		run.recorder = recorder;
		run.Measure();
		return run;
	}

	// ---------------------------------------------------------------- the pass

	private sealed record Classified( ObjectCatalogEntry Entry, bool Supported, string Class, string Reason, int TrackType );

	private void Measure()
	{
		var catalogs = options.Themes.ToDictionary( theme => theme, theme => ObjectCatalog.Load( theme ) );
		var classified = catalogs.ToDictionary( pair => pair.Key, pair => pair.Value.Entries.Select( Classify ).ToList() );
		Rows.Add( InventoryRow( classified ) );
		var steps = M4Gate.Steps.ToDictionary( step => step, _ => new List<M4StepResult>() );
		var probes = new List<M4Gate.TrackRideProbe>();
		var fixedRides = new List<ObjectCatalogEntry>();
		foreach ( var theme in options.Themes )
		{
			var park = OriginalPark.Load( theme, readShippedSave: ParkStart.ReadsShippedSave( ParkStartKind.FullSimulation ) );
			var settings = GuestSettings.Load( theme );
			var savePath = WriteLateGameSave( park, theme );
			try
			{
				var themeSteps = M4Gate.Steps.ToDictionary( step => step, _ => new List<M4StepResult>() );
				foreach ( var item in classified[theme].Where( item => item.Supported && Selected( item.Entry ) ).OrderBy( item => item.Entry.InfoId ) )
				{
					foreach ( var (step, result) in Lifecycle( park, settings, catalogs[theme], savePath, item.Entry ) )
						themeSteps[step].Add( result );
					if ( item.Entry.Category == ObjectCategory.Ride )
						fixedRides.Add( item.Entry );
				}
				foreach ( var item in classified[theme].Where( item => item.Class == "track ride" && Selected( item.Entry ) ).OrderBy( item => item.Entry.InfoId ) )
					probes.Add( ProbeTrackRide( park, settings, catalogs[theme], savePath, item.Entry, item.TrackType ) );
				foreach ( var step in M4Gate.Steps )
				{
					steps[step].AddRange( themeSteps[step] );
					Rows.Add( StepRow( theme, step, themeSteps[step] ) );
				}
			}
			finally
			{
				File.Delete( savePath );
			}
		}
		var (histogram, users, scripts) = ScanCorpus();
		Rows.Add( M4Gate.OpcodeRow( histogram, users, scripts, options.OpcodeTestMap ) );
		Rows.Add( M4Gate.EffectHookRow( histogram.Keys.Where( raw => RideScriptAnalysis.IsKnownOpcode( raw ) && RideOpcodes.GetStatus( (Opcode)raw ) == RideOpcodeStatus.Hooked ).Select( raw => (Opcode)raw ),
			ObserveHooks(), options.OpcodeTestMap ) );
		Rows.Add( M4Gate.CoasterRow( probes, "none: OpenTPW has no track segment builder; ParkObjects places only a track ride's station footprint (shape '<'/'>' station ends, RIDES-011), and COAST/BUMP have no controller (RSE-VM.md)" ) );
		Rows.Add( M4Gate.CameraRow( CameraModes() ) );
		var trackRides = classified.Values.SelectMany( list => list ).Where( item => item.Class == "track ride" && Selected( item.Entry ) ).Select( item => item.Entry.ToString() ).ToList();
		var dataRated = fixedRides.Count( entry => entry.Settings.Has( "UsageInfo.ExcitementLevel" )
			&& OriginalObjectRuntime.CreateVisitorBridge( entry, 1 ).ExcitementLevel == entry.Settings.GetInt( "UsageInfo.ExcitementLevel" ) );
		Rows.Add( M4Gate.RatingRow( dataRated, fixedRides.Count, trackRides,
			"rides without a track use their .sam UsageInfo.ExcitementLevel (data) for guest choice; nothing computes an excitement, fear or nausea rating from a built track, because no track can be built" ) );
	}

	private bool Selected( ObjectCatalogEntry entry ) => options.Objects == null || options.Objects.Contains( entry.InfoId );

	private static Classified Classify( ObjectCatalogEntry entry )
	{
		var trackType = entry.Settings.GetInt( "Bumper.WhichTrackType", -1 );
		var (supported, kind, reason) = M4Gate.Classify( entry.Category, entry.IsFixedItem, entry.IsTool, entry.IsBuildable, entry.WhichUIType, trackType );
		return new Classified( entry, supported, kind, reason, trackType );
	}

	private M4GateRow InventoryRow( Dictionary<string, List<Classified>> classified )
	{
		var themes = new JsonObject();
		var unsupported = new JsonArray();
		var problems = new List<string>();
		foreach ( var (theme, items) in classified )
		{
			var duplicates = items.GroupBy( item => item.Entry.InfoId ).Where( group => group.Count() > 1 ).Select( group => group.Key ).ToList();
			if ( duplicates.Count > 0 )
				problems.Add( $"{theme}: duplicate Info.Id {string.Join( ", ", duplicates )}" );
			if ( items.Count == 0 )
				problems.Add( $"{theme}: empty catalog" );
			var counts = new JsonObject { ["entries"] = items.Count, ["supported"] = items.Count( item => item.Supported ) };
			foreach ( var group in items.GroupBy( item => item.Class ).OrderBy( group => group.Key, StringComparer.Ordinal ) )
				counts[group.Key] = group.Count();
			themes[theme] = counts;
			foreach ( var item in items.Where( item => !item.Supported ) )
				unsupported.Add( $"{item.Entry}: {item.Class}: {item.Reason}" );
		}
		var evidence = new JsonObject
		{
			["entries"] = classified.Values.Sum( items => items.Count ),
			["supported"] = classified.Values.Sum( items => items.Count( item => item.Supported ) ),
			["unsupported"] = classified.Values.Sum( items => items.Count( item => !item.Supported ) ),
			["rule"] = "supported = offered by the build panel (ObjectCatalogEntry.IsBuildable, RIDES-017) and no Bumper.WhichTrackType 1/2/3 track; every other entry is listed with its reason",
			["themes"] = themes,
			["unsupportedEntries"] = unsupported,
			["problems"] = problems.Count == 0 ? "none" : string.Join( "; ", problems )
		};
		return new M4GateRow( "attractions.inventory", "attractions", "Every catalog object of the four themes classified supported/unsupported with a reason",
			problems.Count == 0 && classified.Values.All( items => items.Count > 0 ) ? M4GateVerdict.Pass : M4GateVerdict.Fail, evidence );
	}

	private static M4GateRow StepRow( string theme, string step, List<M4StepResult> results )
	{
		var verdict = M4Gate.StepVerdict( results );
		var failing = results.Where( result => result.Verdict == M4GateVerdict.Fail ).Select( result => $"{result.Object}: {result.Detail}" ).ToList();
		var undecided = results.Where( result => result.Verdict == M4GateVerdict.Unresolved ).Select( result => $"{result.Object}: {result.Detail}" ).ToList();
		var objects = new JsonArray();
		foreach ( var result in results )
			objects.Add( result.ToJson() );
		var evidence = new JsonObject
		{
			["objects"] = results.Count,
			["pass"] = results.Count( result => result.Verdict == M4GateVerdict.Pass ),
			["fail"] = failing.Count,
			["unresolved"] = undecided.Count,
			["failing"] = string.Join( "; ", failing ),
			["undecided"] = string.Join( "; ", undecided ),
			["results"] = objects
		};
		var titles = new Dictionary<string, string>
		{
			["load"] = "Load: model, script and animations of every supported object",
			["build"] = "Build: player build rules and purchase, charged",
			["run"] = "Run: the script runs; guests use guest attractions",
			["stop"] = "Stop: closing reaches the script (VAR_RIDECLOSED) and ends use",
			["delete"] = "Delete: removal frees the site and credits the scrap value"
		};
		return new M4GateRow( $"attractions.{theme}.{step}", M4Gate.LifecycleArea, $"{theme}: {titles[step]}", verdict, evidence );
	}

	// ---------------------------------------------------------------- the late-game save every object park loads

	/// <summary>
	/// A Full Simulation save of the theme with every research item completed and every golden ticket earned, written with
	/// <see cref="ParkSaveFile.Save"/> and loaded per object with <see cref="ParkEconomyRuntime.Load"/> (the player's Load
	/// Park path), so research and tickets do not hide objects from the build flow.
	/// </summary>
	private string WriteLateGameSave( OriginalPark park, string theme )
	{
		var runtime = ParkEconomyRuntime.ForOriginalLevel( park, ParkStartKind.FullSimulation, options.Seed );
		var data = ParkSaveFile.Capture( runtime.Economy );
		data.Research.Completed.Clear();
		data.Research.Completed.AddRange( runtime.Economy.Research.Items.Select( item => new[] { item.InfoId, item.Level } ).OrderBy( key => key[0] ).ThenBy( key => key[1] ) );
		data.Research.Progress.Clear();
		data.Objectives.GoldenTickets.Clear();
		data.Objectives.GoldenTickets.AddRange( Enum.GetValues<GoldenTicketKind>() );
		var restored = ParkSaveFile.Restore( data, runtime.Economy.Settings, runtime.Economy.Catalog );
		var path = Path.Combine( Path.GetTempPath(), $"opentpw-m4-gate-{theme}-{Environment.ProcessId}-{runIndex}-{Guid.NewGuid():N}.json" );
		ParkSaveFile.Save( path, restored );
		return path;
	}

	// ---------------------------------------------------------------- one object's park

	/// <summary>
	/// A headless park for one object, composed like <c>Level</c> and the M3 gate: the late-game economy, the level's
	/// entrance (MAP InitialPath, arrival lanes, the Gates/Lights/Bus fixed items) and a 14-cell path from the entrance
	/// laid with <see cref="ParkPathBuilder"/>. Ticks follow <c>Level.Update</c>: open state into the economy, guests,
	/// scripts, economy.
	/// </summary>
	private sealed class Scene
	{
		public required OriginalPark Park;
		public required ParkEconomyRuntime Runtime;
		public required GuestPathGrid Grid;
		public required OriginalParkGrid BuildGrid;
		public required GuestSimulation Guests;
		public required RideScriptWorld World;
		public required WorldSeed Seed;
		public readonly List<OriginalObjectRuntime> FixedItems = new();
		public readonly List<(OriginalObjectRuntime Runtime, int Instance)> EconomyLinks = new();
		public readonly List<OriginalObjectRuntime> Placed = new();
		public readonly HashSet<(int X, int Y)> Occupied = new();
		public readonly List<(int X, int Y)> Spine = new();
		public (int X, int Y) EntranceCell = (-1, -1);
		public SegmentResult? Segment;
		public long PathCharged;
		public long Ticks;
		private int nextSeed = 1;

		public ParkEconomy Economy => Runtime.Economy;

		/// <summary>Object script seeds as <see cref="ParkObjects"/> derives them from the world seed.</summary>
		public int NextSeed() => nextSeed++ ^ Seed.ObjectScriptKey;

		public bool IsBlocked( int x, int y ) => Occupied.Contains( (x, y) ) || Grid.IsWalkable( x, y ) || Grid.IsQueue( x, y );

		public void Step()
		{
			Ticks++;
			foreach ( var (item, instance) in EconomyLinks )
			{
				if ( Economy.TryGetObject( instance, out var state ) )
				{
					state.IsOpen = item.IsOpen;
					state.QueueHasRoom = QueueJoinRules.QueueCountsForParkPoints( item.Visitors, Grid );
					item.Visitors.Sideshow = new SideshowTerms( state.CostOfGoods, state.Price, state.ChanceOfLosingPercent );   // the price test reads the live words, as Level does
					item.Visitors.WearGauge = state.Repair;   // the worn-toilet exit reads the gauge the relief visits wear, as Level does
				}
			}
			Guests.Tick( Tick );
			foreach ( var item in FixedItems )
				item.Simulate( Tick );
			foreach ( var item in Placed )
				item.Simulate( Tick );
			Runtime.FixedTick();
		}

		public ulong Hash() => WorldStateHash.Compute( new WorldStateSources { Seed = Seed, Economy = Economy, Guests = Guests, Scripts = World } );
	}

	private Scene CreateScene( OriginalPark park, GuestSettings settings, ObjectCatalog catalog, string savePath, bool arrivals )
	{
		var runtime = ParkEconomyRuntime.ForOriginalLevel( park, ParkStartKind.FullSimulation, options.Seed );
		runtime.Load( savePath );
		var grid = GuestPathGrid.FromOriginal( park.Map, null );
		var scene = new Scene
		{
			Park = park,
			Runtime = runtime,
			Grid = grid,
			BuildGrid = new OriginalParkGrid( park ),
			Guests = new GuestSimulation( grid, settings, options.Seed.GuestStream ) { ArrivalsEnabled = arrivals },
			// A fixed civil time: Halloween's clock tower reads HOUR, and the gate's hash must not depend on when it runs.
			World = new RideScriptWorld( options.Seed.ScriptStream, RideScriptWorld.FixedClock ),
			Seed = options.Seed
		};
		// [DATA:levels/<theme>/terrain/base.map:InitialPath] [DATA:Standard.sam:FixedItemInfo lanes]
		foreach ( var entry in catalog.Entries.Where( entry => entry.IsFixedItem && entry.SettingsName is "Gates" or "Lights" or "Bus" ) )
			scene.FixedItems.Add( new OriginalObjectRuntime( entry, scene.World, scene.NextSeed(), open: true ) );
		runtime.AttachGuests( scene.Guests );
		// a new park starts closed (tp.exe 0x51562d); the gate run opens it as the player's door button would (0x519ef0)
		runtime.Economy.OpenPark();
		foreach ( var item in scene.FixedItems.Where( item => scene.Economy.Catalog.TryGet( item.Entry.InfoId, out _ ) ) )
		{
			var state = scene.Economy.RegisterExisting( item.Entry.InfoId );
			runtime.Guests?.Link( item.Visitors.AttractionId, state.Id );
			scene.EconomyLinks.Add( (item, state.Id) );
		}
		var lane = settings.ArrivalLaneA ?? settings.ArrivalLaneB;
		if ( lane is { Length: >= 2 } && grid.IsWalkable( lane[^1].X, lane[^1].Y ) )
		{
			scene.EntranceCell = lane[^1];
			var paths = new ParkPathBuilder( grid.Cells, grid, scene.Economy, scene.BuildGrid, ( x, y ) => scene.Occupied.Contains( (x, y) ) );
			var (dx, dy) = (Math.Sign( lane[^1].X - lane[^2].X ), Math.Sign( lane[^1].Y - lane[^2].Y ));
			var (x, y) = scene.EntranceCell;
			while ( grid.IsWalkable( x + dx, y + dy ) )
				(x, y) = (x + dx, y + dy);
			var balance = scene.Economy.Balance;
			scene.Segment = paths.BuildSegment( (x, y), (x + dx * SpineLength, y + dy * SpineLength) );
			scene.PathCharged = balance - scene.Economy.Balance;
			scene.Spine.Add( (x, y) );
			scene.Spine.AddRange( scene.Segment.Built );
		}
		return scene;
	}

	/// <summary>
	/// The clicked cell and rotation that place <paramref name="entry"/> beside the path, through the build flow's rules
	/// (<see cref="Level.GetCentredAnchor"/> + <see cref="ParkObjects.Check(IParkGrid, Func{int, int, bool}, ObjectCatalogEntry, int, int, int)"/>).
	/// An object with an entrance opens onto a path cell (nearest the park entrance first, then the shortest exit snap);
	/// one without touches a path cell. The gate's own choice, like the M3 gate's site.
	/// </summary>
	private static (int AnchorX, int AnchorY, int Rotation, int SpineIndex)? FindSite( Scene scene, ObjectCatalogEntry entry )
	{
		if ( scene.Spine.Count < 2 )
			return null;
		var minX = scene.Spine.Min( cell => cell.X ) - 8;
		var maxX = scene.Spine.Max( cell => cell.X ) + 8;
		var minY = scene.Spine.Min( cell => cell.Y ) - 8;
		var maxY = scene.Spine.Max( cell => cell.Y ) + 8;
		(int, int, int, int)? best = null;
		var bestScore = (int.MaxValue, int.MaxValue);
		foreach ( var rotation in new[] { 0, 90, 180, 270 } )
		{
			for ( var clickY = minY; clickY <= maxY; clickY++ )
			{
				for ( var clickX = minX; clickX <= maxX; clickX++ )
				{
					var (anchorX, anchorY) = Level.GetCentredAnchor( entry, clickX, clickY, rotation );
					if ( ParkObjects.Check( scene.BuildGrid, scene.IsBlocked, entry, anchorX, anchorY, rotation ) != OriginalPlacementResult.Allowed )
						continue;
					var points = ObjectFootprint.GetAccessPoints( entry.Shape, anchorX, anchorY, rotation ).ToArray();
					var entrance = points.FirstOrDefault( point => point.Kind == ObjectCellKind.Entrance );
					(int, int) score;
					if ( entrance != default )
					{
						var index = scene.Spine.IndexOf( (entrance.OutsideX, entrance.OutsideY) );
						if ( index < 1 || Level.ResolveVisitorCells( points, scene.Grid ) is not { } cells )
							continue;
						var exit = points.FirstOrDefault( point => point.Kind == ObjectCellKind.Exit );
						score = (index, exit == default ? 0 : Math.Abs( cells.Exit.X - exit.OutsideX ) + Math.Abs( cells.Exit.Y - exit.OutsideY ));
					}
					else
					{
						var touching = ObjectFootprint.GetCells( entry.Shape, anchorX, anchorY, rotation )
							.SelectMany( cell => GuestPathGrid.Directions.Select( direction => scene.Spine.IndexOf( (cell.X + direction.DX, cell.Y + direction.DY) ) ) )
							.Where( index => index >= 1 ).DefaultIfEmpty( -1 ).Min();
						if ( touching < 1 )
							continue;
						score = (touching, 0);
					}
					if ( score.CompareTo( bestScore ) < 0 )
					{
						bestScore = score;
						best = (anchorX, anchorY, rotation, score.Item1);
					}
				}
			}
		}
		return best;
	}

	private sealed class Placement
	{
		public required OriginalObjectRuntime Runtime;
		public required int AnchorX, AnchorY, Rotation, Instance;
		public required List<(int X, int Y)> Cells;
	}

	/// <summary>Builds <paramref name="entry"/> through the build flow (Level.PlaceObject's rules): site, ParkObjects.Check, ParkEconomy.TryBuild, charged as the data says.</summary>
	private (M4StepResult Result, Placement? Placement) Build( Scene scene, ObjectCatalogEntry entry, OriginalObjectRuntime runtime )
	{
		var name = entry.ToString();
		var evidence = new JsonObject();
		var problems = new List<string>();
		if ( scene.Segment is not { Completed: true } )
			problems.Add( scene.Segment == null ? "no entrance lane to start a path from" : $"path stopped by {scene.Segment.StoppedBy}" );
		var site = FindSite( scene, entry );
		if ( site is not var (anchorX, anchorY, rotation, spineIndex) )
		{
			problems.Add( "no site beside the path passes the build rule" );
			return (new M4StepResult( name, M4GateVerdict.Fail, string.Join( "; ", problems ), evidence ), null);
		}
		evidence["anchor"] = $"({anchorX},{anchorY})";
		evidence["rotation"] = rotation;
		evidence["pathCell"] = spineIndex;
		var check = ParkObjects.Check( scene.BuildGrid, scene.IsBlocked, entry, anchorX, anchorY, rotation );
		evidence["buildCheck"] = check.ToString();
		var economy = scene.Economy;
		var balance = economy.Balance;
		var tickets = economy.TicketsSpent;
		var otherCosts = economy.Ledger.CurrentTotals.GetValueOrDefault( LedgerCategory.OtherCosts );
		ParkEconomy.PurchaseResult purchase;
		ParkObjectState? bought;
		if ( options.Hooks?.Purchase is { } replaced )
			(purchase, bought) = replaced( economy, entry );
		else
			purchase = economy.TryBuild( entry.InfoId, out bought );
		evidence["purchase"] = purchase.ToString();
		if ( check != OriginalPlacementResult.Allowed || purchase != ParkEconomy.PurchaseResult.Ok || bought == null )
		{
			problems.Add( check != OriginalPlacementResult.Allowed ? $"build rule {check}" : $"purchase {purchase}" );
			return (new M4StepResult( name, M4GateVerdict.Fail, string.Join( "; ", problems ), evidence ), null);
		}
		// [DATA:<object>.sam:Upgrades[0].CostOfUpgrade, UsageInfo.GoldenTicketCost] [BIN:STP-PPC:0x100DA874 object purchase] a first purchase of an object with a ticket cost pays tickets, not cash
		var ticketCost = entry.Settings.GetInt( "UsageInfo.GoldenTicketCost" );
		var paysTickets = economy.Settings.CanSpendTickets && ticketCost > 0;
		var expectedCash = paysTickets ? 0 : entry.BuildCost;
		var expectedTickets = paysTickets ? ticketCost : 0;
		var charged = balance - economy.Balance;
		var ledger = economy.Ledger.CurrentTotals.GetValueOrDefault( LedgerCategory.OtherCosts ) - otherCosts;
		evidence["charged"] = charged;
		evidence["expectedCash"] = (long)expectedCash;
		evidence["ticketsSpent"] = economy.TicketsSpent - tickets;
		evidence["expectedTickets"] = expectedTickets;
		if ( charged != expectedCash || ledger != charged || bought.TotalSpent != charged )
			problems.Add( $"charged {charged} (ledger {ledger}, record {bought.TotalSpent}), expected {expectedCash} = Upgrades[0].CostOfUpgrade{(paysTickets ? " paid with tickets" : "")}" );
		if ( economy.TicketsSpent - tickets != expectedTickets )
			problems.Add( $"{economy.TicketsSpent - tickets} golden tickets spent, expected {expectedTickets}" );
		var cells = ObjectFootprint.GetCells( entry.Shape, anchorX, anchorY, rotation ).Select( cell => (cell.X, cell.Y) ).ToList();
		foreach ( var cell in cells )
			scene.Occupied.Add( cell );
		scene.Placed.Add( runtime );
		scene.EconomyLinks.Add( (runtime, bought.Id) );
		scene.Runtime.Guests?.Link( runtime.Visitors.AttractionId, bought.Id );
		if ( runtime.IsAttraction )
		{
			var points = ObjectFootprint.GetAccessPoints( entry.Shape, anchorX, anchorY, rotation ).ToArray();
			var registered = Level.RegisterWithGuests( runtime, points, scene.Guests );
			var reachable = registered && scene.Grid.Distance( scene.EntranceCell.X, scene.EntranceCell.Y, runtime.Visitors.EntranceCell.X, runtime.Visitors.EntranceCell.Y ) >= 0
				&& scene.Grid.Distance( runtime.Visitors.ExitCell.X, runtime.Visitors.ExitCell.Y, scene.EntranceCell.X, scene.EntranceCell.Y ) >= 0;
			evidence["registeredWithGuests"] = registered;
			evidence["entranceCell"] = $"({runtime.Visitors.EntranceCell.X},{runtime.Visitors.EntranceCell.Y})";
			evidence["reachableFromParkEntrance"] = reachable;
			if ( !reachable )
				problems.Add( "entrance or exit not reachable from the park entrance" );
		}
		var placement = new Placement { Runtime = runtime, AnchorX = anchorX, AnchorY = anchorY, Rotation = rotation, Instance = bought.Id, Cells = cells };
		return (new M4StepResult( name, problems.Count == 0 ? M4GateVerdict.Pass : M4GateVerdict.Fail, problems.Count == 0 ? "built and charged" : string.Join( "; ", problems ), evidence ), placement);
	}

	// ---------------------------------------------------------------- lifecycle

	private sealed class UseTrack
	{
		public long Chosen = -1, Queued = -1, Boarded = -1, Released = -1;
		public long Boardings, Releases, MaxQueue;
		public readonly Dictionary<int, GuestState> Previous = new();
	}

	/// <summary>Samples guest use of <paramref name="id"/>: chosen (heading there or queued), queued, boarded (entered Using), released (left Using).</summary>
	private static void SampleUse( Scene scene, int id, UseTrack track, long tick )
	{
		var queue = 0;
		foreach ( var guest in scene.Guests.Guests )
		{
			track.Previous.TryGetValue( guest.Id, out var before );
			var here = guest.AttractionId == id;
			if ( here && (guest.State == GuestState.GoingToRide || guest.IsInQueue) && track.Chosen < 0 )
				track.Chosen = tick;
			if ( here && guest.IsInQueue )
			{
				queue++;
				if ( track.Queued < 0 )
					track.Queued = tick;
			}
			if ( here && guest.State == GuestState.Using && before != GuestState.Using )
			{
				track.Boardings++;
				if ( track.Boarded < 0 )
					track.Boarded = tick;
			}
			if ( before == GuestState.Using && guest.State != GuestState.Using && guest.LastAttractionId == id )
			{
				track.Releases++;
				if ( track.Released < 0 )
					track.Released = tick;
			}
			track.Previous[guest.Id] = guest.State;
		}
		track.MaxQueue = Math.Max( track.MaxQueue, queue );
	}

	private static int CountAt( Scene scene, int id, Func<Guest, bool> state ) => scene.Guests.Guests.Count( guest => guest.AttractionId == id && state( guest ) );

	private void HashMinute( Scene scene, M4GateObjectContext context, string key )
	{
		if ( scene.Ticks % MinuteTicks == 0 )
			Checkpoint( scene, context, $"{key} minute {scene.Ticks / MinuteTicks}" );
	}

	/// <summary>Records the scene's <see cref="WorldStateHash"/> (every simulated minute, and at the end of each lifecycle step).</summary>
	private void Checkpoint( Scene scene, M4GateObjectContext context, string label )
	{
		options.Hooks?.BeforeHash?.Invoke( context, runIndex );
		MinuteHashes.Add( (label, scene.Hash()) );
	}

	private static IEnumerable<RideVM> Tree( RideVM? vm )
	{
		if ( vm == null )
			yield break;
		yield return vm;
		foreach ( var child in Tree( vm.Child ).Concat( Tree( vm.SoundChild ) ) )
			yield return child;
	}

	private static string? ScriptProblem( OriginalObjectRuntime runtime )
	{
		var faulted = Tree( runtime.Script ).FirstOrDefault( vm => vm.State == RideVMState.Faulted );
		if ( faulted != null )
			return $"script faulted: {faulted.FaultMessage}";
		return runtime.Script?.State == RideVMState.Halted ? "script halted while placed" : null;
	}

	private IEnumerable<(string Step, M4StepResult Result)> Lifecycle( OriginalPark park, GuestSettings settings, ObjectCatalog catalog, string savePath, ObjectCatalogEntry entry )
	{
		var name = entry.ToString();
		var results = new List<(string, M4StepResult)>();
		M4StepResult NotReached( string because ) => new( name, M4GateVerdict.Unresolved, $"not reached: {because}", new JsonObject() );
		Scene scene;
		OriginalObjectRuntime runtime;
		var attraction = false;
		try
		{
			scene = CreateScene( park, settings, catalog, savePath, arrivals: entry.IsChoosable && entry.ScriptPath != null );
			runtime = new OriginalObjectRuntime( entry, scene.World, scene.NextSeed(), open: true );
			attraction = runtime.IsAttraction;
			var load = new JsonObject
			{
				["model"] = entry.ModelPath,
				["script"] = entry.ScriptPath ?? "none",
				["scriptParsed"] = runtime.Script != null,
				["childScripts"] = entry.Scripts.Count - (entry.ScriptPath == null ? 0 : 1),
				["animations"] = entry.Animations.Count,
				["guestKind"] = attraction ? runtime.Visitors.Kind.ToString() : "none (not choosable: scenery)"
			};
			var loaded = entry.ScriptPath == null || runtime.Script != null;
			results.Add( ("load", new M4StepResult( name, loaded ? M4GateVerdict.Pass : M4GateVerdict.Fail, loaded ? "model and script loaded" : "script missing", load )) );
		}
		catch ( Exception exception ) when ( exception is not OutOfMemoryException )
		{
			results.Add( ("load", new M4StepResult( name, M4GateVerdict.Fail, $"{exception.GetType().Name}: {exception.Message}", new JsonObject() )) );
			results.AddRange( M4Gate.Steps.Skip( 1 ).Select( step => (step, NotReached( "load failed" )) ) );
			return results;
		}
		var key = $"{entry.Theme}/{entry.InfoId}";
		try
		{
			var (build, placement) = Build( scene, entry, runtime );
			results.Add( ("build", build) );
			if ( placement == null )
			{
				runtime.Stop();
				results.AddRange( M4Gate.Steps.Skip( 2 ).Select( step => (step, NotReached( "build failed" )) ) );
				return results;
			}
			var context = new M4GateObjectContext( entry, runtime, scene.Runtime, scene.Guests, placement.Instance );
			options.Hooks?.AfterBuild?.Invoke( context );
			Checkpoint( scene, context, $"{key} built" );
			results.Add( ("run", RunStep( scene, context, attraction, key )) );
			Checkpoint( scene, context, $"{key} ran (tick {scene.Ticks})" );
			results.Add( ("stop", attraction ? StopStep( scene, context, key ) : new M4StepResult( name, M4GateVerdict.Pass, "not applicable: not a guest attraction (Info.IsChoosable 0), nothing to close", new JsonObject { ["applicable"] = false } )) );
			Checkpoint( scene, context, $"{key} stopped (tick {scene.Ticks})" );
			results.Add( ("delete", DeleteStep( scene, context, placement )) );
			Checkpoint( scene, context, $"{key} deleted" );
		}
		catch ( Exception exception ) when ( exception is not OutOfMemoryException )
		{
			var reached = results.Select( result => result.Item1 ).ToHashSet();
			var failed = M4Gate.Steps.First( step => !reached.Contains( step ) );
			results.Add( (failed, new M4StepResult( name, M4GateVerdict.Fail, $"{exception.GetType().Name} at tick {scene.Ticks}: {exception.Message}", new JsonObject() )) );
			results.AddRange( M4Gate.Steps.SkipWhile( step => step != failed ).Skip( 1 ).Select( step => (step, NotReached( $"{failed} threw" )) ) );
		}
		return results;
	}

	/// <summary>
	/// Run: the script keeps running (no fault, no halt, its clock advancing) for the window; a guest attraction must also be
	/// used: a queued guest admitted within W, a boarded guest released within W. No guest choosing it within W is undecided.
	/// </summary>
	private M4StepResult RunStep( Scene scene, M4GateObjectContext context, bool attraction, string key )
	{
		var runtime = context.Runtime;
		var id = runtime.Visitors.AttractionId;
		var track = new UseTrack();
		string? scriptProblem = null;
		var previousTime = runtime.Script?.TimeMilliseconds ?? 0;
		var start = scene.Ticks;
		while ( true )
		{
			scene.Step();
			var tick = scene.Ticks - start;
			options.Hooks?.RunTick?.Invoke( context, tick );
			if ( runtime.Script != null )
			{
				var time = runtime.Script.TimeMilliseconds;
				if ( !double.IsFinite( time ) || time <= previousTime )
					scriptProblem ??= $"script clock {previousTime} -> {time} at tick {tick}";
				previousTime = time;
			}
			scriptProblem ??= ScriptProblem( runtime ) is { } problem ? $"{problem} (tick {tick})" : null;
			if ( attraction )
				SampleUse( scene, id, track, tick );
			HashMinute( scene, context, key );
			if ( scriptProblem != null )
				break;
			if ( !attraction )
			{
				if ( tick >= window )
					break;
				continue;
			}
			if ( track.Released >= 0 || (track.Queued < 0 && tick >= window) || (track.Boarded < 0 && track.Queued >= 0 && tick - track.Queued >= window)
				|| (track.Boarded >= 0 && tick - track.Boarded >= window) )
				break;
		}
		var evidence = new JsonObject
		{
			["ticks"] = scene.Ticks - start,
			["scriptState"] = runtime.Script?.State.ToString() ?? "no script",
			["scriptMilliseconds"] = Math.Round( runtime.Script?.TimeMilliseconds ?? 0 ),
			["animationsPlayed"] = runtime.AnimationsPlayed,
			["unresolvedAnimations"] = string.Join( " ", runtime.UnresolvedAnimations ),
			["unimplementedEffects"] = string.Join( " ", Tree( runtime.Script ).SelectMany( vm => vm.UnimplementedEffects ).GroupBy( pair => pair.Key ).OrderBy( group => group.Key ).Select( group => $"{group.Key}x{group.Sum( pair => pair.Value )}" ) )
		};
		if ( attraction )
		{
			evidence["kind"] = runtime.Visitors.Kind.ToString();
			evidence["chosenTick"] = track.Chosen;
			evidence["queuedTick"] = track.Queued;
			evidence["boardedTick"] = track.Boarded;
			evidence["releasedTick"] = track.Released;
			evidence["maxQueue"] = track.MaxQueue;
			evidence["letMeOn"] = runtime.GetVariable( RideVariables.VAR_LETMEON );
			evidence["onRide"] = runtime.GetVariable( RideVariables.VAR_ONRIDE );
		}
		var name = context.Entry.ToString();
		if ( scriptProblem != null )
			return new M4StepResult( name, M4GateVerdict.Fail, scriptProblem, evidence );
		if ( !attraction )
			return new M4StepResult( name, M4GateVerdict.Pass, $"script ran {window} ticks (not a guest attraction)", evidence );
		if ( track.Released >= 0 )
			return new M4StepResult( name, M4GateVerdict.Pass, $"a guest used it: boarded at tick {track.Boarded}, released at tick {track.Released}", evidence );
		if ( track.Boarded >= 0 )
			return new M4StepResult( name, M4GateVerdict.Fail, $"a guest boarded at tick {track.Boarded} and was not released within W ({window} ticks)", evidence );
		if ( track.Queued >= 0 )
			return new M4StepResult( name, M4GateVerdict.Fail, $"guests queued from tick {track.Queued} and none was admitted within W ({window} ticks)", evidence );
		return new M4StepResult( name, M4GateVerdict.Unresolved, track.Chosen < 0 ? $"no guest chose it within W ({window} ticks)"
			: $"chosen at tick {track.Chosen}, but no guest reached its queue within W ({window} ticks)", evidence );
	}

	/// <summary>
	/// Stop: the build panel's close button (<see cref="OriginalObject.Close"/> → <see cref="OriginalObjectRuntime.Close"/>,
	/// VAR_RIDECLOSED = 1). The script keeps running, no guest may board after closing, the riders on board must be released
	/// within W and the economy record shows it closed. Whether and when the script reads VAR_RIDECLOSED is recorded as
	/// evidence: original scripts differ (some never name it, some test it only before their running loop).
	/// </summary>
	private M4StepResult StopStep( Scene scene, M4GateObjectContext context, string key )
	{
		var runtime = context.Runtime;
		var name = context.Entry.ToString();
		var id = runtime.Visitors.AttractionId;
		var script = runtime.Script!;
		var closedIndex = script.GetVariableIndex( nameof( RideVariables.VAR_RIDECLOSED ) );
		// Whether the script reads VAR_RIDECLOSED after closing is evidence, not a rule: jungle Toilet.rse and Puzzle.RSE never
		// name it, and space slide.RSE tests it only before its main loop (word 6), so an open, idle slide never reads it again.
		var referencesClosed = closedIndex >= 0 && script.Instructions.Any( instruction => instruction.Operands.Any( operand => operand.IsVariable && operand.Raw == closedIndex ) );
		var evidence = new JsonObject { ["declaresRideClosed"] = closedIndex >= 0, ["scriptReferencesRideClosed"] = referencesClosed };
		var ridersAtClose = CountAt( scene, id, guest => guest.State == GuestState.Using );
		var queuedAtClose = CountAt( scene, id, guest => guest.IsInQueue );
		long readTick = -1;
		long tick = 0;
		var previous = script.InstructionExecuting;
		script.InstructionExecuting = ( vm, instruction ) =>
		{
			previous?.Invoke( vm, instruction );
			if ( readTick < 0 && instruction.Operands.Any( operand => operand.IsVariable && operand.Raw == closedIndex ) )
				readTick = tick;
		};
		runtime.Close();
		var track = new UseTrack();
		foreach ( var guest in scene.Guests.Guests )
			track.Previous[guest.Id] = guest.State;
		string? scriptProblem = null;
		var riders = ridersAtClose;
		var queued = queuedAtClose;
		while ( tick < window )
		{
			tick++;
			scene.Step();
			options.Hooks?.StopTick?.Invoke( context, tick );
			SampleUse( scene, id, track, tick );
			scriptProblem ??= ScriptProblem( runtime ) is { } problem ? $"{problem} (tick {tick} after closing)" : null;
			HashMinute( scene, context, key );
			riders = CountAt( scene, id, guest => guest.State == GuestState.Using );
			queued = CountAt( scene, id, guest => guest.IsInQueue );
			if ( scriptProblem != null || (riders == 0 && queued == 0 && (readTick >= 0 || !referencesClosed || tick >= MinuteTicks)) )
				break;
		}
		script.InstructionExecuting = previous;
		var closedInEconomy = scene.Economy.TryGetObject( context.EconomyInstance, out var state ) && !state.IsOpen;
		evidence["ridersAtClose"] = ridersAtClose;
		evidence["queuedAtClose"] = queuedAtClose;
		evidence["readRideClosedTick"] = readTick;
		evidence["boardedAfterClose"] = track.Boardings;
		evidence["ridersAtEnd"] = riders;
		evidence["queuedAtEnd"] = queued;
		evidence["ticks"] = tick;
		evidence["closedInEconomy"] = closedInEconomy;
		evidence["scriptState"] = script.State.ToString();
		var problems = new List<string>();
		if ( scriptProblem != null )
			problems.Add( scriptProblem );
		// [APPROX:GATEM4-002] closing ends use: no guest boards a closed attraction — evidence needed: a trace of the host's admission test against VAR_RIDECLOSED (QUEUE-plan) and of what happens to riders and queue on closing
		if ( track.Boardings > 0 )
			problems.Add( $"{track.Boardings} guest(s) boarded after closing (first at tick {track.Boarded})" );
		if ( riders > 0 && scriptProblem == null )
			problems.Add( $"{riders} rider(s) not released within W ({window} ticks) after closing" );
		if ( !closedInEconomy )
			problems.Add( "the economy record is still open" );
		return new M4StepResult( name, problems.Count == 0 ? M4GateVerdict.Pass : M4GateVerdict.Fail,
			problems.Count == 0 ? $"closed: riders released, nobody boarded; {(readTick >= 0 ? $"the script read VAR_RIDECLOSED at tick {readTick}" : referencesClosed ? "the script names VAR_RIDECLOSED but did not read it (outside its running loop)" : "the script never names VAR_RIDECLOSED (closing is the host's)")}{(queued > 0 ? $", {queued} still queued (not judged)" : "")}" : string.Join( "; ", problems ), evidence );
	}

	/// <summary>
	/// Delete: the remove tool's removal (<see cref="ParkObjects.Remove"/>: cells freed, <see cref="OriginalObject"/>'s
	/// script stopped; then Level's ObjectRemoved handlers: <see cref="GuestSimulation.Unregister"/> and
	/// <see cref="Level.SellRemovedObject"/>). The credit must equal the scrap rule, the site must pass the build rule
	/// again, the scripts must leave the world and no guest may still use or queue for it.
	/// </summary>
	private M4StepResult DeleteStep( Scene scene, M4GateObjectContext context, Placement placement )
	{
		var runtime = context.Runtime;
		var entry = context.Entry;
		var economy = scene.Economy;
		var balance = economy.Balance;
		var otherIncome = economy.Ledger.CurrentTotals.GetValueOrDefault( LedgerCategory.OtherIncome );
		var builtTick = economy.TryGetObject( placement.Instance, out var record ) ? record.BuiltTick : -1;
		var levelBefore = record?.Level ?? 0;
		var usesBefore = record?.TimesUsed ?? 0;
		var machines = Tree( runtime.Script ).ToList();
		var id = runtime.Visitors.AttractionId;
		long? credit;
		if ( options.Hooks?.Delete is { } delete )
			credit = delete( context );
		else
		{
			foreach ( var cell in placement.Cells )
				scene.Occupied.Remove( cell );
			scene.Placed.Remove( runtime );
			runtime.Stop();
			scene.Guests.Unregister( runtime.Visitors );
			credit = Level.SellRemovedObject( scene.Runtime, id, placement.Instance );
		}
		scene.EconomyLinks.RemoveAll( link => link.Runtime == runtime );
		// [DATA:<object>.sam:Upgrades[0].CostOfUpgrade, Upgrades[n].ScrapValueYear1..4] [BIN:STP-PPC:0x100E25E8 scrap value] scrap = Upgrades[0].CostOfUpgrade × the level's ScrapValueYearN % for the age (365 park-clock days a year), or 100 % for an object nobody used in its first 30 days
		var age = builtTick < 0 ? 0 : ParkCalendar.Seconds( economy.Tick ) - ParkCalendar.Seconds( builtTick );
		var year = (int)Math.Min( 3L, age / (365L * ParkCalendar.SecondsPerDay) );
		int PercentOf( int upgrade, int index ) => entry.Upgrades.FirstOrDefault( level => level.Level == upgrade )?.GetInt( $"ScrapValueYear{index + 1}" ) ?? 0;
		var unusedAtStart = year == 0 && age / (30L * ParkCalendar.SecondsPerDay) == 0 && usesBefore == 0 && PercentOf( 0, 0 ) > 0;
		var percent = unusedAtStart ? 100 : PercentOf( levelBefore, year );
		var expected = (long)entry.BuildCost * percent / 100;
		var problems = new List<string>();
		if ( credit != expected )
			problems.Add( $"credited {credit?.ToString() ?? "nothing"}, expected {expected} = {entry.BuildCost} x {(unusedAtStart ? "the unused first-30-days refund" : $"ScrapValueYear{year + 1} of level {levelBefore}")} {percent}%" );
		if ( economy.Balance - balance != (credit ?? 0) || economy.Ledger.CurrentTotals.GetValueOrDefault( LedgerCategory.OtherIncome ) - otherIncome != (credit ?? 0) )
			problems.Add( $"balance rose {economy.Balance - balance} and OtherIncome {economy.Ledger.CurrentTotals.GetValueOrDefault( LedgerCategory.OtherIncome ) - otherIncome}, not the credit" );
		if ( economy.TryGetObject( placement.Instance, out _ ) )
			problems.Add( "the economy record remains" );
		var free = ParkObjects.Check( scene.BuildGrid, scene.IsBlocked, entry, placement.AnchorX, placement.AnchorY, placement.Rotation );
		if ( free != OriginalPlacementResult.Allowed )
			problems.Add( $"the site is not free again ({free})" );
		var live = machines.Count( vm => scene.World.Scripts.Contains( vm ) || vm.State is RideVMState.Running or RideVMState.Waiting );
		if ( live > 0 )
			problems.Add( $"{live} script(s) still live" );
		scene.Step();
		var stillUsing = CountAt( scene, id, guest => guest.State is GuestState.GoingToRide or GuestState.Using || guest.IsInQueue );
		if ( stillUsing > 0 || scene.Guests.Attractions.Contains( runtime.Visitors ) )
			problems.Add( $"{stillUsing} guest(s) still head for, queue at or use it{(scene.Guests.Attractions.Contains( runtime.Visitors ) ? "; still registered" : "")}" );
		var evidence = new JsonObject
		{
			["credit"] = credit,
			["expectedCredit"] = expected,
			["scrapYear"] = year + 1,
			["scrapPercent"] = percent,
			["siteAfter"] = free.ToString(),
			["scriptsStopped"] = machines.Count - live
		};
		return new M4StepResult( entry.ToString(), problems.Count == 0 ? M4GateVerdict.Pass : M4GateVerdict.Fail,
			problems.Count == 0 ? $"removed; scrap {credit} credited" : string.Join( "; ", problems ), evidence );
	}

	// ---------------------------------------------------------------- track rides

	/// <summary>
	/// A track ride as far as OpenTPW goes: its station is built through the build flow, its script runs for one simulated
	/// minute without guests, and it is removed. No track can be built, so no car runs.
	/// </summary>
	private M4Gate.TrackRideProbe ProbeTrackRide( OriginalPark park, GuestSettings settings, ObjectCatalog catalog, string savePath, ObjectCatalogEntry entry, int trackType )
	{
		var evidence = new JsonObject { ["trackType"] = trackType, ["shape"] = string.Join( "/", entry.Shape.Rows ), ["trackModels"] = entry.AuxiliaryModels.Count };
		try
		{
			var scene = CreateScene( park, settings, catalog, savePath, arrivals: false );
			var runtime = new OriginalObjectRuntime( entry, scene.World, scene.NextSeed(), open: true );
			var (build, placement) = Build( scene, entry, runtime );
			evidence["station"] = build.Detail;
			if ( placement == null )
				return new M4Gate.TrackRideProbe( entry.ToString(), trackType, false, false, false, false, false, false, evidence );
			for ( var tick = 0L; tick < MinuteTicks; tick++ )
				scene.Step();
			var problem = ScriptProblem( runtime );
			evidence["stationScript"] = problem ?? runtime.Script?.State.ToString() ?? "no script";
			evidence["controllerCallsUnimplemented"] = string.Join( " ", Tree( runtime.Script ).SelectMany( vm => vm.UnimplementedEffects )
				.Where( pair => pair.Key is Opcode.COAST or Opcode.BUMP or Opcode.TOUR ).GroupBy( pair => pair.Key ).Select( group => $"{group.Key}x{group.Sum( pair => pair.Value )}" ) );
			var context = new M4GateObjectContext( entry, runtime, scene.Runtime, scene.Guests, placement.Instance );
			var delete = DeleteStep( scene, context, placement );
			evidence["delete"] = delete.Detail;
			return new M4Gate.TrackRideProbe( entry.ToString(), trackType, true, problem == null, false, false, false, delete.Verdict == M4GateVerdict.Pass, evidence );
		}
		catch ( Exception exception ) when ( exception is not OutOfMemoryException )
		{
			evidence["exception"] = $"{exception.GetType().Name}: {exception.Message}";
			return new M4Gate.TrackRideProbe( entry.ToString(), trackType, false, false, false, false, false, false, evidence );
		}
	}

	// ---------------------------------------------------------------- vm

	/// <summary>Every RSE member of every archive under <c>levels</c> (the 308-script corpus of RSE-SCRIPTS.md): opcode histogram and the scripts using each opcode.</summary>
	private static (Dictionary<ushort, int> Histogram, Dictionary<ushort, SortedSet<string>> Users, int Scripts) ScanCorpus()
	{
		var levels = FileSystem.GetAbsolutePath( "/levels" );
		lock ( corpusScans )
		{
			if ( !corpusScans.TryGetValue( levels, out var scan ) )
				corpusScans[levels] = scan = ScanCorpus( levels );
			return scan;
		}
	}

	/// <summary>Corpus scans by levels directory: the files are read-only game data, so a process scans them once.</summary>
	private static readonly Dictionary<string, (Dictionary<ushort, int>, Dictionary<ushort, SortedSet<string>>, int)> corpusScans = new( StringComparer.Ordinal );

	private static (Dictionary<ushort, int> Histogram, Dictionary<ushort, SortedSet<string>> Users, int Scripts) ScanCorpus( string levels )
	{
		var histogram = new Dictionary<ushort, int>();
		var users = new Dictionary<ushort, SortedSet<string>>();
		var scripts = 0;
		foreach ( var wad in Directory.EnumerateFiles( levels, "*", SearchOption.AllDirectories ).Where( path => path.EndsWith( ".wad", StringComparison.OrdinalIgnoreCase ) ).OrderBy( path => path, StringComparer.Ordinal ) )
		{
			using var archive = new WadArchive( wad );
			var relative = Path.GetRelativePath( levels, wad ).Replace( Path.DirectorySeparatorChar, '/' );
			foreach ( var (member, data) in Members( archive.Root, relative ) )
			{
				scripts++;
				var script = new RideScriptFile( new MemoryStream( data ) );
				foreach ( var instruction in script.Instructions )
				{
					histogram[instruction.Opcode] = histogram.GetValueOrDefault( instruction.Opcode ) + 1;
					if ( !users.TryGetValue( instruction.Opcode, out var set ) )
						users[instruction.Opcode] = set = new SortedSet<string>( StringComparer.Ordinal );
					set.Add( member );
				}
			}
		}
		return (histogram, users, scripts);
	}

	private static IEnumerable<(string Member, byte[] Data)> Members( ArchiveDirectory directory, string prefix )
	{
		foreach ( var item in directory.Children )
		{
			if ( item is ArchiveDirectory child )
			{
				foreach ( var member in Members( child, $"{prefix}/{child.Name}" ) )
					yield return member;
			}
			else if ( item is ArchiveFile file && file.Name!.EndsWith( ".rse", StringComparison.OrdinalIgnoreCase ) )
				yield return ($"{prefix}/{file.Name}", file.GetData());
		}
	}

	/// <summary>What this pass's scripts did with each hooked opcode (the recorder saw every script it ran).</summary>
	private Dictionary<Opcode, M4Gate.HookObservation> ObserveHooks()
	{
		var performed = recorder.EffectsPerformedByRuntime();
		var unimplemented = new Dictionary<Opcode, long>();
		var faults = new Dictionary<Opcode, long>();
		foreach ( var vm in recorder.Machines )
		{
			foreach ( var (opcode, count) in vm.UnimplementedEffects )
				unimplemented[opcode] = unimplemented.GetValueOrDefault( opcode ) + count;
			if ( vm.State == RideVMState.Faulted && vm.FaultMessage?.Split( ' ' )[0] is { } word && Enum.TryParse<Opcode>( word, out var faulted ) && RideOpcodes.GetStatus( faulted ) == RideOpcodeStatus.Hooked )
				faults[faulted] = faults.GetValueOrDefault( faulted ) + 1;
		}
		return recorder.Executed.Keys.Union( unimplemented.Keys ).Distinct().ToDictionary( opcode => opcode, opcode => new M4Gate.HookObservation(
			recorder.Executed.GetValueOrDefault( opcode ), performed.Contains( opcode ), unimplemented.GetValueOrDefault( opcode ), faults.GetValueOrDefault( opcode ) ) );
	}

	/// <summary>Every <see cref="CameraMode"/> in the game and whether it takes a placed ride.</summary>
	private static List<(string Mode, bool FollowsARide)> CameraModes()
	{
		var rideTypes = new[] { typeof( OriginalObject ), typeof( OriginalObjectRuntime ), typeof( RideVisitorBridge ), typeof( Ride ) };
		return typeof( CameraMode ).Assembly.GetTypes().Where( type => typeof( CameraMode ).IsAssignableFrom( type ) && !type.IsAbstract ).OrderBy( type => type.Name, StringComparer.Ordinal )
			.Select( type => (type.Name, type.GetConstructors().SelectMany( constructor => constructor.GetParameters() ).Select( parameter => parameter.ParameterType )
				.Concat( type.GetProperties().Select( property => property.PropertyType ) ).Any( parameter => rideTypes.Any( ride => ride.IsAssignableFrom( parameter ) ) )) ).ToList();
	}
}

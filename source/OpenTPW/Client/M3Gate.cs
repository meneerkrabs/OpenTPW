using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace OpenTPW;

/// <summary>Outcome of one M3 gate row (docs/M3-GATE.md).</summary>
public enum M3GateVerdict
{
	Pass,
	Fail,
	/// <summary>Measured, but no original-derived bound exists to decide it.</summary>
	Unresolved
}

/// <summary>One row of the M3 gate report: a build step or an invariant with its evidence.</summary>
public sealed record M3GateRow( string Id, string Area, string Title, M3GateVerdict Verdict, long? FirstViolationTick, JsonObject Evidence );

public sealed record M3GateOptions
{
	public string Level { get; init; } = "jungle";
	public ulong Seed { get; init; } = OpenTPW.Level.GuestSeed;
	/// <summary>Simulated minutes at normal speed (<see cref="M3Gate.TicksForMinutes"/>).</summary>
	public double Minutes { get; init; } = 30;
	/// <summary>Runs the scenario a second time with the same seed and compares state hashes.</summary>
	public bool DeterminismProbe { get; init; } = true;
}

public sealed record M3GateReport( M3GateOptions Options, JsonObject TimeMapping, IReadOnlyList<M3GateRow> Rows, double WallSeconds )
{
	public bool HasFailures => Rows.Any( row => row.Verdict == M3GateVerdict.Fail );

	public bool HasUnresolved => Rows.Any( row => row.Verdict == M3GateVerdict.Unresolved );

	/// <summary>Process exit code: 1 when any row fails, 2 when none fails but one is unresolved, 0 only when every row passes (M3 is accepted only at 0).</summary>
	public int ExitCode => HasFailures ? 1 : HasUnresolved ? 2 : 0;

	public M3GateRow this[string id] => Rows.First( row => row.Id == id );

	public JsonObject ToJsonObject()
	{
		var rows = new JsonArray();
		foreach ( var row in Rows )
		{
			rows.Add( new JsonObject
			{
				["id"] = row.Id,
				["area"] = row.Area,
				["title"] = row.Title,
				["verdict"] = row.Verdict.ToString().ToLowerInvariant(),
				["firstViolationTick"] = row.FirstViolationTick,
				["evidence"] = row.Evidence.DeepClone()
			} );
		}
		return new JsonObject
		{
			["gate"] = "M3",
			["level"] = Options.Level,
			["seed"] = Options.Seed,
			["minutes"] = Options.Minutes,
			["timeMapping"] = TimeMapping.DeepClone(),
			["wallSeconds"] = Math.Round( WallSeconds, 3 ),
			["counts"] = new JsonObject
			{
				["pass"] = Rows.Count( row => row.Verdict == M3GateVerdict.Pass ),
				["fail"] = Rows.Count( row => row.Verdict == M3GateVerdict.Fail ),
				["unresolved"] = Rows.Count( row => row.Verdict == M3GateVerdict.Unresolved )
			},
			["rows"] = rows
		};
	}

	// Plain ASCII punctuation (+, ', <) stays readable in the report; it is written to a file, never into HTML.
	private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
	private static readonly JsonSerializerOptions Compacted = new() { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

	public string ToJson() => ToJsonObject().ToJsonString( Indented ).Replace( "\r\n", "\n" );

	/// <summary>Human summary: one line per row with its verdict and the main evidence.</summary>
	public string ToSummary()
	{
		var text = new StringBuilder();
		text.Append( FormattableString.Invariant( $"M3 gate: {Options.Level}, seed {Options.Seed}, {Options.Minutes} simulated minutes " ) );
		text.Append( FormattableString.Invariant( $"({TimeMapping["ticks"]} ticks, {TimeMapping["parkTurns"]} park turns, {TimeMapping["parkClockDays"]} park-clock days) in {WallSeconds:0.0} s wall time.\n" ) );
		foreach ( var row in Rows )
		{
			var verdict = row.Verdict.ToString().ToUpperInvariant();
			var first = row.FirstViolationTick is { } tick ? $" first violation tick {tick};" : "";
			var evidence = string.Join( ", ", row.Evidence.Select( pair => $"{pair.Key}={Compact( pair.Value )}" ) );
			text.Append( $"{verdict,-10} {row.Id,-28} [{row.Area}] {row.Title}:{first} {evidence}\n" );
		}
		text.Append( $"Totals: {Rows.Count( row => row.Verdict == M3GateVerdict.Pass )} pass, {Rows.Count( row => row.Verdict == M3GateVerdict.Fail )} fail, {Rows.Count( row => row.Verdict == M3GateVerdict.Unresolved )} unresolved.\n" );
		return text.ToString();
	}

	private static string Compact( JsonNode? node ) => node switch
	{
		null => "null",
		JsonValue value => value.ToJsonString( Compacted ).Trim( '"' ),
		_ => node.ToJsonString( Compacted )
	};
}

/// <summary>
/// Milestone M3 evaluator (docs/COMPLETION-PLAN.md §M3, docs/M3-GATE.md): builds a fixed minimal park in an
/// original level headlessly (no window, no GPU), runs the 60 Hz simulation as fast as possible and samples
/// stability invariants every tick. It measures the current runtime; it does not change gameplay.
/// </summary>
public static class M3Gate
{
	/// <summary>
	/// Simulated minutes to fixed ticks. One gate minute is 60 s of normal-speed simulation time, i.e. 3,600
	/// ticks of the 60 Hz <see cref="FixedStepClock"/> that drives guests, scripts and the economy (one economy
	/// tick per fixed tick at <see cref="GameSpeed.Normal"/>).
	/// </summary>
	// [APPROX:GATE-001] an M3 in-game minute is 60 s of normal-speed simulation (3,600 fixed 60 Hz ticks), not a park-clock minute (one 248 ms park turn already advances the park clock 3,750 s) — evidence needed: the original's notion of elapsed play time for the M3 gate
	public static long TicksForMinutes( double minutes ) => (long)Math.Round( minutes * 60 * FixedStepClock.TicksPerSecond );

	public static JsonObject DescribeTimeMapping( long ticks ) => new()
	{
		["ticks"] = ticks,
		["ticksPerSecond"] = FixedStepClock.TicksPerSecond,
		["simulatedSeconds"] = ticks / (double)FixedStepClock.TicksPerSecond,
		["parkTurns"] = ParkCalendar.Turn( ticks ),
		["parkTurnMilliseconds"] = ParkCalendar.TurnMilliseconds,
		["parkClockSecondsPerTurn"] = ParkCalendar.SecondsPerTurn,
		["parkClockDays"] = ParkCalendar.DayIndex( ticks ),
		["parkDateAtEnd"] = ParkCalendar.ToDate( ticks ).ToString(),
		["sources"] = "GATE-001; ParkCalendar (BIN:STP-PPC:0x101C22E0 turn = 248 ms, 0x100E4394 3,750 s per turn); APPROX:ECON-001 (60 Hz ticks sampled into turns)"
	};

	public static M3GateReport Run( M3GateOptions options )
	{
		ArgumentNullException.ThrowIfNull( options );
		if ( !double.IsFinite( options.Minutes ) || options.Minutes <= 0 )
			throw new ArgumentOutOfRangeException( nameof( options ), "Minutes must be positive." );
		var ticks = TicksForMinutes( options.Minutes );
		var watch = Stopwatch.StartNew();
		var first = M3GateRun.Execute( options, ticks );
		var rows = first.Rows.ToList();
		rows.Add( DeterminismRow( options, ticks, first ) );
		return new M3GateReport( options, DescribeTimeMapping( ticks ), rows, watch.Elapsed.TotalSeconds );
	}

	private static M3GateRow DeterminismRow( M3GateOptions options, long ticks, M3GateRun first )
	{
		var evidence = new JsonObject
		{
			["reportOnly"] = "DET node owns fixes",
			["run1GuestHash"] = $"{first.GuestHash:X16}",
			["run1GateHash"] = $"{first.GateHash:X16}"
		};
		if ( !options.DeterminismProbe )
		{
			evidence["skipped"] = true;
			return new M3GateRow( "determinism.same-seed", "determinism", "Same seed gives the same state hash", M3GateVerdict.Unresolved, null, evidence );
		}
		var second = M3GateRun.Execute( options, ticks );
		evidence["run2GuestHash"] = $"{second.GuestHash:X16}";
		evidence["run2GateHash"] = $"{second.GateHash:X16}";
		evidence["guestHashMatches"] = first.GuestHash == second.GuestHash;
		evidence["gateHashMatches"] = first.GateHash == second.GateHash;
		var divergence = Enumerable.Range( 0, Math.Min( first.MinuteHashes.Count, second.MinuteHashes.Count ) )
			.FirstOrDefault( index => first.MinuteHashes[index] != second.MinuteHashes[index], -1 );
		evidence["firstDivergentMinute"] = divergence < 0 ? null : (int?)(divergence + 1);
		evidence["attractionIdsRun1"] = string.Join( " ", first.AttractionIds );
		evidence["attractionIdsRun2"] = string.Join( " ", second.AttractionIds );
		var matches = first.GuestHash == second.GuestHash && first.GateHash == second.GateHash;
		long? firstTick = divergence < 0 ? null : (divergence + 1L) * TicksForMinutes( 1 );
		return new M3GateRow( "determinism.same-seed", "determinism", "Same seed gives the same state hash (report only)",
			matches ? M3GateVerdict.Pass : M3GateVerdict.Fail, matches ? null : firstTick, evidence );
	}

	/// <summary>
	/// <c>--m3-gate [--level jungle] [--seed N] [--minutes 30] [--report path.json] [--no-determinism]</c>.
	/// Prints the summary; returns <see cref="M3GateReport.ExitCode"/>: 1 when any row fails, 2 when no row fails but
	/// one is unresolved, 0 only when every row passes. M3 is accepted only at exit code 0.
	/// </summary>
	public static int RunCommand( string[] args )
	{
		var options = new M3GateOptions();
		if ( Option( args, "--level" ) is { } level )
			options = options with { Level = level };
		if ( Option( args, "--seed" ) is { } seedText )
			options = options with { Seed = ulong.TryParse( seedText, out var seed ) ? seed : throw new ArgumentException( "--seed requires an unsigned integer." ) };
		if ( Option( args, "--minutes" ) is { } minutesText )
		{
			if ( !double.TryParse( minutesText, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var minutes ) || !double.IsFinite( minutes ) || minutes <= 0 )
				throw new ArgumentException( "--minutes requires a positive number." );
			options = options with { Minutes = minutes };
		}
		if ( args.Contains( "--no-determinism" ) )
			options = options with { DeterminismProbe = false };
		var report = Run( options );
		Console.Write( report.ToSummary() );
		if ( Option( args, "--report" ) is { } path )
		{
			File.WriteAllText( path, report.ToJson() + "\n" );
			Console.WriteLine( $"Report: {path}" );
		}
		return report.ExitCode;
	}

	private static string? Option( string[] args, string name )
	{
		var index = Array.IndexOf( args, name );
		if ( index < 0 )
			return null;
		if ( index + 1 >= args.Length || args[index + 1].StartsWith( "--" ) )
			throw new ArgumentException( $"{name} requires a value." );
		return args[index + 1];
	}
}

/// <summary>
/// One headless gate run: the scripted park, the tick loop (the order of <c>Level.Update</c>: object open
/// state into the economy, guests, object scripts, economy) and the per-tick invariant samplers.
/// </summary>
internal sealed class M3GateRun
{
	private const float Tick = (float)FixedStepClock.TickDuration;
	/// <summary>Path cells laid from the entrance into the park before objects are placed.</summary>
	private const int SpineLength = 14;

	private readonly M3GateOptions options;
	private readonly List<M3GateRow> rows = new();
	private readonly List<Placed> placed = new();
	private readonly List<OriginalObjectRuntime> fixedItems = new();
	/// <summary>Every object with an economy record and its instance id (Level.economyInstances): open state is mirrored each tick.</summary>
	private readonly List<(OriginalObjectRuntime Runtime, int Instance)> economyLinks = new();
	private readonly HashSet<(int X, int Y)> occupied = new();
	private readonly RideScriptWorld world = new();
	private int nextSeed = 1;
	private long setupBalance;
	private long setupCost;
	private readonly List<GuestTrack> departedTracks = new();

	private OriginalPark park = null!;
	private ParkEconomyRuntime runtime = null!;
	private ParkEconomy economy = null!;
	private GuestPathGrid grid = null!;
	private OriginalParkGrid buildGrid = null!;
	private GuestSimulation guests = null!;
	private ObjectCatalog catalog = null!;
	private (int X, int Y) entranceCell;
	private readonly List<(int X, int Y)> spine = new();
	private ParkPathBuilder paths = null!;
	private readonly List<SegmentResult> pathSegments = new();
	private readonly HashSet<(int X, int Y)> initialPathCells = new();
	private long pathCharged;
	private int pathRowIndex = -1;
	/// <summary>Queue cells the attraction's site search found for it (front first), laid by <see cref="BuildQueue"/>.</summary>
	private List<(int X, int Y)>? queueRoute;
	/// <summary>Index of the build.queue row; its verdict is final only after the run (guests must stand on the cells and board).</summary>
	private int queueRowIndex = -1;
	private bool queueBuilt;

	private M3GateRun( M3GateOptions options ) => this.options = options;

	public IReadOnlyList<M3GateRow> Rows => rows;
	public ulong GuestHash { get; private set; }
	public ulong GateHash { get; private set; }
	public List<ulong> MinuteHashes { get; } = new();
	public IReadOnlyList<int> AttractionIds => placed.Select( item => item.Runtime.Visitors.AttractionId ).ToList();

	private sealed record Placed( string Role, ObjectCatalogEntry Entry, int AnchorX, int AnchorY, int Rotation, OriginalObjectRuntime Runtime, int EconomyInstance );

	public static M3GateRun Execute( M3GateOptions options, long ticks )
	{
		var run = new M3GateRun( options );
		run.Build();
		run.Simulate( ticks );
		return run;
	}

	// ---------------------------------------------------------------- scripted park

	private void Build()
	{
		park = OriginalPark.Load( options.Level, readShippedSave: ParkStart.ReadsShippedSave( ParkStartKind.FullSimulation ) );
		runtime = ParkEconomyRuntime.ForOriginalLevel( park, ParkStartKind.FullSimulation );
		economy = runtime.Economy;
		catalog = ObjectCatalog.Load( options.Level, economy.Settings.IsEasy );
		buildGrid = new OriginalParkGrid( park );
		grid = GuestPathGrid.FromOriginal( park.Map, null );
		guests = new GuestSimulation( grid, GuestSettings.Load( options.Level ), options.Seed );
		var openingBalance = economy.Balance;
		BuildEntrance();
		runtime.AttachGuests( guests );
		ConnectFixedItemsToEconomy();
		BuildPaths();
		PlaceObject( "attraction", entry => entry.Category == ObjectCategory.Ride && entry.HasQueue );
		// Placing a HasQueue ride switches the HUD to the queue tool (ParkHud, BIN:STP-PPC:0x1007497C), so the queue comes next.
		BuildQueue();
		PlaceObject( "shop", entry => entry.Category == ObjectCategory.Shop && OriginalObjectRuntime.CreateVisitorBridge( entry, 1 ).Satisfies != GuestNeeds.None );
		PlaceObject( "toilet", entry => OriginalObjectRuntime.CreateVisitorBridge( entry, 1 ).Satisfies.HasFlag( GuestNeeds.Toilet ) );
		HireStaff();
		FinishPathRow();
		setupBalance = economy.Balance;
		setupCost = openingBalance - setupBalance;
	}

	/// <summary>The level's own entrance: MAP InitialPath cells, the Standard.sam arrival lanes and the gate fixed items.</summary>
	private void BuildEntrance()
	{
		// [DATA:levels/<theme>/terrain/base.map:InitialPath] [DATA:Standard.sam:FixedItemInfo lanes]
		foreach ( var entry in catalog.Entries.Where( entry => entry.IsFixedItem && entry.SettingsName is "Gates" or "Lights" or "Bus" ) )
			fixedItems.Add( new OriginalObjectRuntime( entry, world, nextSeed++, open: true ) );
		var lane = guests.Settings.ArrivalLaneA ?? guests.Settings.ArrivalLaneB;
		var valid = lane is { Length: >= 2 } && grid.IsWalkable( lane[^1].X, lane[^1].Y );
		entranceCell = valid ? lane![^1] : (-1, -1);
		var evidence = new JsonObject
		{
			["initialPathCells"] = grid.WalkableCount,
			["laneA"] = Cells( guests.Settings.ArrivalLaneA ),
			["laneB"] = Cells( guests.Settings.ArrivalLaneB ),
			["fixedItems"] = string.Join( ", ", fixedItems.Select( item => item.Entry.SettingsName ) ),
			["entranceFee"] = economy.EntranceFee
		};
		AddRow( "build.entrance", "paths", "Entrance: MAP InitialPath, arrival lanes and gate fixed items",
			valid && fixedItems.Any( item => item.Entry.SettingsName == "Gates" ) ? M3GateVerdict.Pass : M3GateVerdict.Fail, null, evidence );
	}

	/// <summary>Level.ConnectObjectsToEconomy for the fixed items: after AttachGuests, registered, linked and open-state synced.</summary>
	private void ConnectFixedItemsToEconomy()
	{
		foreach ( var item in fixedItems.Where( item => economy.Catalog.TryGet( item.Entry.InfoId, out _ ) ) )
		{
			var state = economy.RegisterExisting( item.Entry.InfoId );
			runtime.Guests?.Link( item.Visitors.AttractionId, state.Id );
			economyLinks.Add( (item, state.Id) );
		}
	}

	private static string Cells( (int X, int Y)[]? lane ) => lane == null ? "none" : string.Join( " ", lane.Select( cell => $"({cell.X},{cell.Y})" ) );

	/// <summary>
	/// A straight path from the end of the entrance walkway into the park, laid with the player-facing path builder
	/// (<see cref="ParkPathBuilder.BuildSegment"/>: snapped line, per-cell rules, <c>Costs.PathCell</c> charged per new
	/// cell). The walkway's last cell is the segment start, so it is reported as existing, not built. The row is
	/// decided after the objects are placed (<see cref="FinishPathRow"/>), because it also needs their entrances
	/// reachable over these paths.
	/// </summary>
	private void BuildPaths()
	{
		paths = new ParkPathBuilder( grid.Cells, grid, economy, buildGrid, ( x, y ) => occupied.Contains( (x, y) ) );
		for ( var y = 0; y < grid.Cells.Height; y++ )
		{
			for ( var x = 0; x < grid.Cells.Width; x++ )
			{
				if ( grid.Cells.TypeAt( x, y ) == ParkCellType.Path )
					initialPathCells.Add( (x, y) );
			}
		}
		var balance = economy.Balance;
		if ( entranceCell.X >= 0 )
		{
			var lane = (guests.Settings.ArrivalLaneA ?? guests.Settings.ArrivalLaneB)!;
			var (dx, dy) = (Math.Sign( lane[^1].X - lane[^2].X ), Math.Sign( lane[^1].Y - lane[^2].Y ));
			var (x, y) = entranceCell;
			while ( grid.IsWalkable( x + dx, y + dy ) )
				(x, y) = (x + dx, y + dy);
			var start = (x, y);
			var end = (x + dx * SpineLength, y + dy * SpineLength);
			var segment = paths.BuildSegment( start, end );
			pathSegments.Add( segment );
			spine.Add( start );
			spine.AddRange( segment.Built );
		}
		pathCharged = balance - economy.Balance;
		pathRowIndex = rows.Count;
		AddRow( "build.paths", "paths", "Paths from the entrance, built with the in-game path builder", M3GateVerdict.Fail, null, new JsonObject() );
	}

	/// <summary>
	/// build.paths passes when every segment was laid completely by <see cref="ParkPathBuilder"/>, every built cell was
	/// charged <c>Costs.PathCell</c> (the segment's charge and the balance both), no path cell exists that neither the
	/// level nor the builder laid, and the park entrance reaches every placed object's entrance and the queue's join cell.
	/// </summary>
	private void FinishPathRow()
	{
		var cost = economy.CellCost( CellPurchase.Path );
		var built = pathSegments.SelectMany( segment => segment.Built ).ToHashSet();
		var problems = new List<string>();
		if ( pathSegments.Count == 0 )
			problems.Add( "no segment laid" );
		foreach ( var segment in pathSegments.Where( segment => !segment.Completed ) )
			problems.Add( $"segment from ({segment.Start.X},{segment.Start.Y}) stopped by {segment.StoppedBy} at {Cell( segment.StoppedAt )}" );
		if ( built.Count != SpineLength * pathSegments.Count )
			problems.Add( $"{built.Count} cells built, expected {SpineLength * pathSegments.Count}" );
		var charged = pathSegments.Sum( segment => segment.Charged );
		if ( charged != built.Count * cost || pathCharged != charged )
			problems.Add( $"charged {charged} by the builder and {pathCharged} from the balance, expected {built.Count} x {cost}" );
		var stray = new List<(int X, int Y)>();
		for ( var y = 0; y < grid.Cells.Height; y++ )
		{
			for ( var x = 0; x < grid.Cells.Width; x++ )
			{
				if ( grid.Cells.TypeAt( x, y ) == ParkCellType.Path && !initialPathCells.Contains( (x, y) ) && !built.Contains( (x, y) ) )
					stray.Add( (x, y) );
			}
		}
		if ( stray.Count > 0 )
			problems.Add( $"{stray.Count} path cell(s) not laid by the builder, first {Cell( stray[0] )}" );
		var targets = placed.Select( item => (item.Entry.SettingsName, item.Runtime.Visitors.EntranceCell) ).ToList();
		if ( placed.FirstOrDefault( item => item.Role == "attraction" )?.Runtime.Visitors.JoinCell is { } join )
			targets.Add( ("queue join", join) );
		var unreachable = targets.Where( target => entranceCell.X < 0 || grid.Distance( entranceCell.X, entranceCell.Y, target.Item2.X, target.Item2.Y ) < 0 ).Select( target => target.Item1 ).ToList();
		if ( targets.Count == 0 || unreachable.Count > 0 )
			problems.Add( targets.Count == 0 ? "nothing to reach" : $"not reachable from the park entrance: {string.Join( ", ", unreachable )}" );
		var segments = new JsonArray();
		foreach ( var segment in pathSegments )
		{
			segments.Add( new JsonObject
			{
				["start"] = Cell( segment.Start ),
				["end"] = Cell( segment.SnappedEnd ),
				["built"] = segment.Built.Count,
				["existing"] = segment.Existing.Count,
				["charged"] = segment.Charged,
				["stoppedBy"] = segment.StoppedBy.ToString()
			} );
		}
		var evidence = new JsonObject
		{
			["builder"] = "ParkPathBuilder.BuildSegment (the path tool's code: snapped straight line, PATH-plan per-cell rules, Costs.PathCell per new cell); the HUD click route itself is not exercised (no HUD without a GPU)",
			["segments"] = segments,
			["cellsBuilt"] = built.Count,
			["costPerCell"] = cost,
			["charged"] = charged,
			["balanceCharged"] = pathCharged,
			["strayPathCells"] = stray.Count,
			["reached"] = string.Join( ", ", targets.Select( target => $"{target.Item1} {Cell( target.Item2 )}" ) ),
			["problems"] = problems.Count == 0 ? "none" : string.Join( "; ", problems )
		};
		rows[pathRowIndex] = rows[pathRowIndex] with { Verdict = problems.Count == 0 ? M3GateVerdict.Pass : M3GateVerdict.Fail, Evidence = evidence };
	}

	private static string Cell( (int X, int Y)? cell ) => cell is { } value ? $"({value.X},{value.Y})" : "none";

	/// <summary>Cells the object build rule treats as taken: placed objects and queue cells (Level's ParkObjects.IsReserved), plus the gate's own path cells (stricter than ParkObjects, which does not know them).</summary>
	private bool IsBlocked( int x, int y ) => occupied.Contains( (x, y) ) || grid.IsWalkable( x, y ) || grid.IsQueue( x, y );

	/// <summary>Level.IsQueueBlocked over the gate's park: terrain the object build rule refuses, or an object footprint (QUEUE-012).</summary>
	private bool IsQueueBlocked( int x, int y ) => x < 0 || y < 0 || x >= buildGrid.Width || y >= buildGrid.Height
		|| buildGrid.CheckTerrain( x, y ) != OriginalPlacementResult.Allowed || occupied.Contains( (x, y) );

	/// <summary>
	/// Places the first researched, buyable catalog object matching <paramref name="match"/> beside the spine through
	/// the HUD build flow's rules (Level.PlaceObject): a clicked cell is turned into an anchor with
	/// <see cref="Level.GetCentredAnchor"/>, the site must pass <see cref="ParkObjects.Check(IParkGrid, Func{int, int, bool}, ObjectCatalogEntry, int, int, int)"/>,
	/// the object is bought with <see cref="ParkEconomy.TryBuild"/> and registered with guests by
	/// <see cref="Level.RegisterWithGuests"/> (attractions only; outside cells snapped to the nearest path, RIDES-028).
	/// The gate's own choice is the site: its entrance must open onto a spine cell.
	/// </summary>
	private void PlaceObject( string role, Func<ObjectCatalogEntry, bool> match )
	{
		var evidence = new JsonObject();
		var candidates = catalog.Buildable.Where( entry => entry.ScriptPath != null && entry.IsChoosable && match( entry ) )
			.Where( entry => economy.Research.IsAvailable( entry.InfoId ) && economy.Catalog.TryGet( entry.InfoId, out var info ) && info.IsBuyable )
			.OrderBy( entry => entry.InfoId ).ToList();
		evidence["candidates"] = candidates.Count;
		var area = role == "attraction" ? "rides" : role == "shop" ? "economy" : "guests";
		foreach ( var entry in candidates )
		{
			var queueCells = role == "attraction" && entry.HasQueue ? QueueCellsFor( entry ) : 0;
			var site = FindSite( entry, queueCells );
			if ( site == null )
				continue;
			var (clickX, clickY, rotation, route) = site.Value;
			var (anchorX, anchorY) = Level.GetCentredAnchor( entry, clickX, clickY, rotation );
			var check = ParkObjects.Check( buildGrid, IsBlocked, entry, anchorX, anchorY, rotation );
			evidence["object"] = entry.ToString();
			evidence["buildCheck"] = check.ToString();
			if ( check != OriginalPlacementResult.Allowed )
				break;
			var purchase = economy.TryBuild( entry.InfoId, out var bought );
			evidence["purchase"] = purchase.ToString();
			if ( purchase != ParkEconomy.PurchaseResult.Ok )
				break;
			foreach ( var cell in ObjectFootprint.GetCells( entry.Shape, anchorX, anchorY, rotation ) )
				occupied.Add( (cell.X, cell.Y) );
			var item = new OriginalObjectRuntime( entry, world, nextSeed++, open: true );
			var points = ObjectFootprint.GetAccessPoints( entry.Shape, anchorX, anchorY, rotation ).ToArray();
			var registered = Level.RegisterWithGuests( item, points, guests );
			runtime.Guests!.Link( item.Visitors.AttractionId, bought!.Id );
			economyLinks.Add( (item, bought.Id) );
			placed.Add( new Placed( role, entry, anchorX, anchorY, rotation, item, bought.Id ) );
			if ( queueCells > 0 )
				queueRoute = route;
			var entrance = points.First( point => point.Kind == ObjectCellKind.Entrance );
			var exit = points.FirstOrDefault( point => point.Kind == ObjectCellKind.Exit );
			if ( exit == default )
				exit = entrance;
			evidence["clickedCell"] = $"({clickX},{clickY})";
			evidence["anchor"] = $"({anchorX},{anchorY})";
			evidence["rotation"] = rotation;
			evidence["registeredWithGuests"] = registered;
			evidence["entranceCell"] = $"({item.Visitors.EntranceCell.X},{item.Visitors.EntranceCell.Y})";
			evidence["exitCell"] = $"({item.Visitors.ExitCell.X},{item.Visitors.ExitCell.Y})";
			if ( item.Visitors.ExitCell != (exit.OutsideX, exit.OutsideY) )
				evidence["exitSnappedFrom"] = $"({exit.OutsideX},{exit.OutsideY})";
			if ( item.Visitors.QueueFrontCell is { } front && queueCells > 0 )
				evidence["queueFrontCell"] = $"({front.X},{front.Y})";
			evidence["cost"] = bought.TotalSpent;
			evidence["capacity"] = item.Visitors.Capacity;
			evidence["kind"] = item.Visitors.Kind.ToString();
			evidence["satisfies"] = item.Visitors.Satisfies.ToString();
			evidence["price"] = item.Visitors.Price;
			var reachable = registered && grid.Distance( entranceCell.X, entranceCell.Y, item.Visitors.EntranceCell.X, item.Visitors.EntranceCell.Y ) >= 0
				&& grid.Distance( item.Visitors.ExitCell.X, item.Visitors.ExitCell.Y, entranceCell.X, entranceCell.Y ) >= 0;
			evidence["reachableFromEntrance"] = reachable;
			evidence["placedBy"] = "Level.GetCentredAnchor + ParkObjects.Check + ParkEconomy.TryBuild + Level.RegisterWithGuests (the HUD build flow's rules)";
			AddRow( $"build.{role}", area, $"Place one {role}", reachable ? M3GateVerdict.Pass : M3GateVerdict.Fail, null, evidence );
			return;
		}
		evidence["reason"] = evidence.ContainsKey( "purchase" ) ? "purchase refused" : evidence.ContainsKey( "buildCheck" ) ? "build check refused"
			: candidates.Count == 0 ? "no researched, buyable catalog object of this role" : "no site beside the path";
		AddRow( $"build.{role}", area, $"Place one {role}", M3GateVerdict.Fail, null, evidence );
	}

	/// <summary>
	/// Queue cells laid for a HasQueue ride: enough for its queue limit at four guests per cell, capped at
	/// <see cref="QueuePaths.MaximumCells"/> (QUEUE-013). For a HasQueue ride the limit is 100, so 25 cells, the
	/// longest queue the runtime builds and the one that makes the data limit, not the cells, decide the queue length.
	/// </summary>
	internal static int QueueCellsFor( ObjectCatalogEntry entry )
	{
		var limit = RideVisitorBridge.ComputeQueueLimit( OriginalObjectRuntime.CreateQueueParameters( entry ) );
		return Math.Clamp( (limit + RideVisitorBridge.PositionsPerCell - 1) / RideVisitorBridge.PositionsPerCell, 1, QueuePaths.MaximumCells );
	}

	/// <summary>
	/// The clicked cell and rotation that place <paramref name="entry"/> next to the spine: the build rule allows it.
	/// Without a queue its entrance opens onto a spine cell (nearest the entrance first) and its exit needs the
	/// shortest snap. With <paramref name="queueCells"/> &gt; 0 the entrance's outside cell must be free for a queue
	/// (QueuePaths refuses a path cell there: NoFrontCell) and a run of exactly that many queue cells must lead
	/// from it to a cell beside a path, where guests join; the exit with the shortest snap wins, then the front
	/// nearest a path.
	/// </summary>
	private (int ClickX, int ClickY, int Rotation, List<(int X, int Y)>? Route)? FindSite( ObjectCatalogEntry entry, int queueCells )
	{
		if ( spine.Count < 2 )
			return null;
		var minX = spine.Min( cell => cell.X ) - 8;
		var maxX = spine.Max( cell => cell.X ) + 8;
		var minY = spine.Min( cell => cell.Y ) - 8;
		var maxY = spine.Max( cell => cell.Y ) + 8;
		(int, int, int)? best = null;
		var bestScore = (int.MaxValue, int.MaxValue);
		var queueSites = new List<(int Snap, int Distance, int ClickX, int ClickY, int Rotation, (int X, int Y) Front, HashSet<(int X, int Y)> Footprint)>();
		var goalDistance = queueCells > 0 ? QueueGoalDistances() : null;
		foreach ( var rotation in new[] { 0, 90, 180, 270 } )
		{
			for ( var clickY = minY; clickY <= maxY; clickY++ )
			{
				for ( var clickX = minX; clickX <= maxX; clickX++ )
				{
					var (anchorX, anchorY) = Level.GetCentredAnchor( entry, clickX, clickY, rotation );
					if ( ParkObjects.Check( buildGrid, IsBlocked, entry, anchorX, anchorY, rotation ) != OriginalPlacementResult.Allowed )
						continue;
					var points = ObjectFootprint.GetAccessPoints( entry.Shape, anchorX, anchorY, rotation ).ToArray();
					var entrance = points.FirstOrDefault( point => point.Kind == ObjectCellKind.Entrance );
					if ( entrance == default )
						continue;
					var exit = points.FirstOrDefault( point => point.Kind == ObjectCellKind.Exit );
					if ( queueCells > 0 )
					{
						var front = (entrance.OutsideX, entrance.OutsideY);
						var footprint = ObjectFootprint.GetCells( entry.Shape, anchorX, anchorY, rotation ).Select( cell => (cell.X, cell.Y) ).ToHashSet();
						if ( footprint.Contains( front ) || !IsQueueCellFree( front.OutsideX, front.OutsideY ) )
							continue;
						var distance = goalDistance![front.OutsideY * grid.Cells.Width + front.OutsideX];
						if ( distance < 0 || distance > queueCells - 1 || Level.ResolveVisitorCells( points, grid ) is not { } resolved )
							continue;
						var exitSnap = exit == default ? 0 : Math.Abs( resolved.Exit.X - exit.OutsideX ) + Math.Abs( resolved.Exit.Y - exit.OutsideY );
						queueSites.Add( (exitSnap, distance, clickX, clickY, rotation, front, footprint) );
						continue;
					}
					var spineIndex = spine.IndexOf( (entrance.OutsideX, entrance.OutsideY) );
					if ( spineIndex < 1 || Level.ResolveVisitorCells( points, grid ) is not { } cells )
						continue;
					var snap = exit == default ? 0 : Math.Abs( cells.Exit.X - exit.OutsideX ) + Math.Abs( cells.Exit.Y - exit.OutsideY );
					var score = (spineIndex, snap);
					if ( score.CompareTo( bestScore ) < 0 )
					{
						bestScore = score;
						best = (clickX, clickY, rotation);
					}
				}
			}
		}
		if ( queueCells > 0 )
		{
			// OrderBy is stable: equal scores keep the scan order (rotation, then row, then column).
			foreach ( var site in queueSites.OrderBy( site => site.Snap ).ThenBy( site => site.Distance ) )
			{
				if ( FindQueueRoute( site.Front, queueCells, site.Footprint, goalDistance! ) is { } route )
					return (site.ClickX, site.ClickY, site.Rotation, route);
			}
			return null;
		}
		return best is var (x, y, r) ? (x, y, r, null) : null;
	}

	/// <summary>A cell the queue tool could lay (QueuePaths.CheckExtend's cell rules): an empty, non-path cell the queue build rule does not block.</summary>
	private bool IsQueueCellFree( int x, int y ) => grid.Cells.InBounds( x, y ) && grid.InBounds( x, y ) && grid.Cells.TypeAt( x, y ) == ParkCellType.Empty
		&& !grid.IsWalkable( x, y ) && !IsQueueBlocked( x, y );

	private bool BesidePath( int x, int y ) => Enumerable.Range( 0, 4 ).Any( direction => grid.IsWalkable( x + GuestPathGrid.Directions[direction].DX, y + GuestPathGrid.Directions[direction].DY ) );

	/// <summary>Steps from each free queue cell to the nearest free cell beside a path (−1: none), a lower bound for the route search.</summary>
	private int[] QueueGoalDistances()
	{
		var (width, height) = (grid.Cells.Width, grid.Cells.Height);
		var distance = Enumerable.Repeat( -1, width * height ).ToArray();
		var open = new Queue<(int X, int Y)>();
		for ( var y = 0; y < height; y++ )
		{
			for ( var x = 0; x < width; x++ )
			{
				if ( IsQueueCellFree( x, y ) && BesidePath( x, y ) )
				{
					distance[y * width + x] = 0;
					open.Enqueue( (x, y) );
				}
			}
		}
		while ( open.TryDequeue( out var cell ) )
		{
			foreach ( var (dx, dy) in GuestPathGrid.Directions )
			{
				var (x, y) = (cell.X + dx, cell.Y + dy);
				if ( !IsQueueCellFree( x, y ) || distance[y * width + x] >= 0 )
					continue;
				distance[y * width + x] = distance[cell.Y * width + cell.X] + 1;
				open.Enqueue( (x, y) );
			}
		}
		return distance;
	}

	/// <summary>
	/// A run of exactly <paramref name="length"/> free queue cells from <paramref name="front"/>, each touching the one
	/// before (QueuePaths' rule), whose last cell lies beside a path so guests can join. Depth-first in
	/// <see cref="GuestPathGrid.Directions"/> order, so the route is deterministic; null when none is found.
	/// </summary>
	private List<(int X, int Y)>? FindQueueRoute( (int X, int Y) front, int length, HashSet<(int X, int Y)> footprint, int[] goalDistance )
	{
		var route = new List<(int X, int Y)> { front };
		var used = new HashSet<(int X, int Y)> { front };
		var budget = 100_000;
		bool Extend( (int X, int Y) cell, int remaining )
		{
			if ( remaining == 0 )
				return BesidePath( cell.X, cell.Y );
			if ( --budget < 0 )
				return false;
			foreach ( var (dx, dy) in GuestPathGrid.Directions )
			{
				var next = (X: cell.X + dx, Y: cell.Y + dy);
				if ( used.Contains( next ) || footprint.Contains( next ) || !IsQueueCellFree( next.X, next.Y ) )
					continue;
				var distance = goalDistance[next.Y * grid.Cells.Width + next.X];
				if ( distance < 0 || distance > remaining - 1 )
					continue;
				used.Add( next );
				route.Add( next );
				if ( Extend( next, remaining - 1 ) )
					return true;
				route.RemoveAt( route.Count - 1 );
				used.Remove( next );
			}
			return false;
		}
		return Extend( front, length - 1 ) ? route : null;
	}

	/// <summary>
	/// The attraction's queue, laid cell by cell through the queue tool's code (<see cref="Level.BuildQueueCell(GuestPathGrid, ParkEconomy?, RideVisitorBridge, int, int, Func{int, int, bool}, out string)"/>:
	/// QueuePaths.CheckExtend, <c>Costs.QueueCell</c> charged with ParkEconomy.TrySpendCell, QueuePaths.TryExtend) on
	/// the route the site search found. The front cell, entrance direction and join cell come from
	/// Level.RegisterWithGuests / the ride's queue recompute. The row is decided after the run
	/// (<see cref="FinishQueueRow"/>): guests must also stand on the queue cells and board from them.
	/// </summary>
	private void BuildQueue()
	{
		var ride = placed.FirstOrDefault( item => item.Role == "attraction" );
		var cellCost = economy.CellCost( CellPurchase.Queue );
		var evidence = new JsonObject
		{
			["queueCellCost"] = cellCost,
			["builtBy"] = "Level.BuildQueueCell (the queue tool's per-cell code: QueuePaths.CheckExtend + ParkEconomy.TrySpendCell(Queue) + QueuePaths.TryExtend); the HUD click route itself is not exercised (no HUD without a GPU)"
		};
		var problems = new List<string>();
		if ( ride == null || !ride.Entry.HasQueue || queueRoute == null )
		{
			problems.Add( ride == null ? "no attraction" : !ride.Entry.HasQueue ? "the attraction has no Info.HasQueue" : "no queue route from the entrance to a path" );
			evidence["problems"] = string.Join( "; ", problems );
			queueRowIndex = rows.Count;
			AddRow( "build.queue", "paths", "Place a queue path for the attraction", M3GateVerdict.Fail, null, evidence );
			return;
		}
		var bridge = ride.Runtime.Visitors;
		var balance = economy.Balance;
		var laid = 0;
		var message = "";
		foreach ( var (x, y) in queueRoute )
		{
			var result = Level.BuildQueueCell( grid, economy, bridge, x, y, IsQueueBlocked, out message );
			if ( result != QueueBuildResult.Ok )
			{
				evidence["refused"] = $"({x},{y}) {result}: {message}";
				break;
			}
			laid++;
		}
		var charged = balance - economy.Balance;
		var expectedLength = Math.Min( bridge.QueueLimit, RideVisitorBridge.PositionsPerCell * queueRoute.Count );
		var join = bridge.JoinCell;
		var joinReachable = join is { } cell && grid.Distance( entranceCell.X, entranceCell.Y, cell.X, cell.Y ) >= 0;
		evidence["cellsRequested"] = queueRoute.Count;
		evidence["cellsRequestedRule"] = $"ceil(queue limit {bridge.QueueLimit} / {RideVisitorBridge.PositionsPerCell} per cell), capped at QueuePaths.MaximumCells {QueuePaths.MaximumCells} (QUEUE-013)";
		evidence["cellsLaid"] = laid;
		evidence["charged"] = charged;
		evidence["expectedCharge"] = (long)queueRoute.Count * cellCost;
		evidence["queueSizeInCells"] = bridge.QueueSizeInCells;
		evidence["frontCell"] = bridge.QueueCells.Count > 0 ? $"({bridge.QueueCells[0].X},{bridge.QueueCells[0].Y})" : "none";
		evidence["backCell"] = $"({bridge.QueueBackCell.X},{bridge.QueueBackCell.Y})";
		evidence["entranceDirection"] = bridge.QueueEntranceDirection;
		evidence["joinCell"] = join is { } j ? $"({j.X},{j.Y})" : "none";
		evidence["joinReachableFromEntrance"] = joinReachable;
		evidence["maximumQueueLength"] = bridge.MaximumQueueLength;
		evidence["expectedMaximumQueueLength"] = $"min(limit {bridge.QueueLimit}, 4 x {queueRoute.Count}) = {expectedLength}";
		evidence["lastMessage"] = message;
		if ( laid != queueRoute.Count )
			problems.Add( $"{laid} of {queueRoute.Count} cells laid" );
		if ( charged != (long)queueRoute.Count * cellCost )
			problems.Add( $"charged {charged}, expected {queueRoute.Count} x {cellCost}" );
		if ( bridge.QueueSizeInCells != queueRoute.Count || !bridge.QueueCells.SequenceEqual( queueRoute ) )
			problems.Add( $"the ride's queue is {bridge.QueueSizeInCells} cells, not the {queueRoute.Count} laid" );
		if ( !joinReachable )
			problems.Add( "no join cell reachable from the park entrance" );
		if ( bridge.MaximumQueueLength != expectedLength )
			problems.Add( $"maximum queue length {bridge.MaximumQueueLength}, expected {expectedLength}" );
		queueBuilt = problems.Count == 0;
		evidence["problems"] = problems.Count == 0 ? "none" : string.Join( "; ", problems );
		queueRowIndex = rows.Count;
		AddRow( "build.queue", "paths", "Place a queue path for the attraction", M3GateVerdict.Fail, null, evidence );
	}

	/// <summary>build.queue passes when the queue was built and charged through the queue tool's code and guests stood on its cells and boarded from it.</summary>
	private void FinishQueueRow( int stoodOnQueueCells, int boardedFromQueueCells )
	{
		if ( queueRowIndex < 0 )
			return;
		var row = rows[queueRowIndex];
		row.Evidence["guestsStoodOnQueueCells"] = stoodOnQueueCells;
		row.Evidence["boardedFromQueueCells"] = boardedFromQueueCells;
		var pass = queueBuilt && stoodOnQueueCells > 0 && boardedFromQueueCells > 0;
		if ( queueBuilt && !pass )
			row.Evidence["problems"] = "no guest stood on the queue cells and boarded from them";
		rows[queueRowIndex] = row with { Verdict = pass ? M3GateVerdict.Pass : M3GateVerdict.Fail };
	}

	private void HireStaff()
	{
		var hired = new JsonArray();
		foreach ( var type in new[] { StaffType.Mechanic, StaffType.Handyman } )
		{
			var candidate = economy.Staff.Candidates.Where( item => item.Type == type ).OrderBy( item => item.Id ).FirstOrDefault();
			if ( candidate == null || !economy.Staff.CanHire( type ) )
				continue;
			var member = economy.Hire( candidate.Id );
			hired.Add( $"{member.Type} grade {member.Grade} (${economy.Staff.MonthlyWage( member )}/month)" );
		}
		var evidence = new JsonObject
		{
			["hired"] = hired,
			["monthlyWages"] = economy.Staff.TotalMonthlyWages,
			["worldAgents"] = "none: staff exist only in ParkEconomy (repairs, litter, wages); no staff walk the paths"
		};
		AddRow( "build.staff", "staff", "Hire staff (mechanic, handyman)", hired.Count == 2 ? M3GateVerdict.Pass : M3GateVerdict.Fail, null, evidence );
	}

	// ---------------------------------------------------------------- simulation and samplers

	private sealed class GuestTrack
	{
		public GuestState State;
		public bool Admitted;
		public bool Seen;
		/// <summary>Tick the guest entered its current queue (<see cref="Guest.IsInQueue"/>, walking up included), or −1.</summary>
		public long QueueSince = -1;
		public int QueueAttraction;
		/// <summary>Recorded queue position p on joining (0 = front).</summary>
		public int QueueJoinPosition = -1;
		public bool StoodOnQueueCell;
		public long UnreachableSince = -1;
		public bool UsedRide, UsedShop, UsedToilet;
	}

	private sealed class Violation
	{
		public long Count;
		public long? First;
		public string? FirstDetail;

		public void Add( long tick, string detail )
		{
			Count++;
			if ( First == null )
			{
				First = tick;
				FirstDetail = detail;
			}
		}
	}

	/// <summary>Per placed object: queue waits and the admission progress signals (QUEUE-plan §9a).</summary>
	private sealed class QueueProgress
	{
		public readonly List<(long Turns, int Position)> Waits = new();
		public int MaxQueue;
		public long Evaluations;
		/// <summary>Bound on consecutive evaluations with the gates held and the same head not ready (<see cref="M3GateRun.HeadNotReadyBound"/>).</summary>
		public long HeadBound;
		public int HeadGuest;
		/// <summary><see cref="RideVisitorBridge.HeadNotReadyStreak"/> when the current head took over (the bound is per head).</summary>
		public long HeadStreakBase;
		public long MaxHeadStreak;
		public long BlockedStreak, MaxBlockedStreak;
		public readonly Violation HeadNotReady = new();
		public readonly Violation Blocked = new();
	}

	private void Simulate( long ticks )
	{
		var tracks = new Dictionary<int, GuestTrack>();
		var timeViolation = new Violation();
		var ledgerViolation = new Violation();
		var unreachable = new Violation();
		var openingBalance = economy.Ledger.CurrentOpeningBalance;
		var startTotals = LedgerTotals();
		var previousGuestTime = guests.TimeSeconds;
		long previousEconomyTick = economy.Tick, previousParkSeconds = ParkCalendar.Seconds( economy.Tick );
		var scriptTimes = placed.Select( item => item.Runtime.Script?.TimeMilliseconds ?? 0 ).ToArray();
		long turnedAwayFromQueue = 0, confusedGiveUps = 0, ejected = 0, leftAfterAdmission = 0, turnedBackAtBooth = 0, departed = 0, vanishedFromQueue = 0;
		var progress = placed.Select( item => new QueueProgress { HeadBound = HeadNotReadyBound( item.Runtime.Visitors.QueueSizeInCells, guests.Settings.WalkSpeedCellsPerSecond ).Turns } ).ToArray();
		var queueRide = queueBuilt ? placed.First( item => item.Role == "attraction" ).Runtime.Visitors : null;
		var queueCells = queueRide?.QueueCells.ToHashSet() ?? new HashSet<(int X, int Y)>();
		int stoodOnQueueCells = 0, boardedFromQueueCells = 0;
		long repairs = 0, repairsNeeded = 0, wagesPaid = 0, wagesEvents = 0;
		long litterPeak = economy.LitterScaled, litterCleaned = 0, litterDropped = 0, previousLitter = economy.LitterScaled;
		var repairWindow = RepairWindow();
		var pendingRepairs = new List<(int Instance, long Tick, long Deadline, bool Reported)>();
		long maxRepairTicks = 0;
		var repairOverdue = new Violation();
		var litterNotCleaned = new Violation();
		var faults = new Violation();
		var halted = new Violation();
		long currentTick = 0;
		void OnEvent( ParkEvent item )
		{
			if ( item.Kind == ParkEventKind.RideRepaired )
			{
				repairs++;
				foreach ( var job in pendingRepairs.Where( job => job.Instance == item.InstanceId ) )
					maxRepairTicks = Math.Max( maxRepairTicks, item.Tick - job.Tick );
				pendingRepairs.RemoveAll( job => job.Instance == item.InstanceId );
			}
			// A ride below WornStateOfRepair or broken down is a mechanic job (ParkEconomy.DispatchMechanics).
			if ( item.Kind is ParkEventKind.RideWorn or ParkEventKind.RideBrokeDown )
			{
				repairsNeeded++;
				pendingRepairs.Add( (item.InstanceId, item.Tick, RepairDeadline( item.Tick, repairWindow.Turns ), false) );
			}
			if ( item.Kind == ParkEventKind.WagesPaid )
			{
				wagesEvents++;
				wagesPaid += item.Amount;
			}
		}
		void OnAdmission( RideVisitorBridge bridge, AdmissionCheck check )
		{
			var index = placed.FindIndex( item => item.Runtime.Visitors == bridge );
			if ( index < 0 )
				return;
			var state = progress[index];
			var name = placed[index].Entry.SettingsName;
			state.Evaluations++;
			// (a) The gates hold but the head is not standing at position 0 (RideVisitorBridge.HeadNotReadyStreak): bounded
			// per head by its own walk and wait rules, so the streak restarts when another guest becomes head.
			if ( check.HeadNotReady )
			{
				if ( check.HeadGuest != state.HeadGuest )
					(state.HeadGuest, state.HeadStreakBase) = (check.HeadGuest, bridge.HeadNotReadyStreak - 1);
				var streak = bridge.HeadNotReadyStreak - state.HeadStreakBase;
				state.MaxHeadStreak = Math.Max( state.MaxHeadStreak, streak );
				if ( streak > state.HeadBound )
					state.HeadNotReady.Add( currentTick, $"tick {currentTick}: {name} head guest {check.HeadGuest} not ready for {streak} evaluations (bound {state.HeadBound})" );
			}
			else
				state.HeadGuest = 0;
			// A queue the handshake cannot move: VAR_LETMEON holds a value while nobody is called. Only the bridge writes a
			// guest there (PresentForBoarding, for the called guest) and it is consumed by the script or cleared on withdrawal.
			var letMeOn = placed[index].Runtime.GetVariable( RideVariables.VAR_LETMEON );
			var blocked = check.HeadGuest != 0 && !check.ConditionsHold && bridge.CalledGuest == 0 && letMeOn != 0;
			state.BlockedStreak = blocked ? state.BlockedStreak + 1 : 0;
			state.MaxBlockedStreak = Math.Max( state.MaxBlockedStreak, state.BlockedStreak );
			if ( state.BlockedStreak >= 2 )
				state.Blocked.Add( currentTick, $"tick {currentTick}: {name} admission blocked for {state.BlockedStreak} evaluations with {bridge.QueueLength} queued: VAR_LETMEON {letMeOn} while nobody is called" );
		}
		void OnQueueWait( Guest guest, IRideVisitorBridge ride, long turns )
		{
			var index = placed.FindIndex( item => item.Runtime.Visitors == ride );
			if ( index < 0 )
				return;
			var track = tracks.GetValueOrDefault( guest.Id );
			progress[index].Waits.Add( (turns, track?.QueueJoinPosition ?? -1) );
			if ( ride == queueRide && track?.StoodOnQueueCell == true )
				boardedFromQueueCells++;
		}
		economy.EventRaised += OnEvent;
		guests.QueueWaitCompleted += OnQueueWait;
		foreach ( var item in placed )
			item.Runtime.Visitors.AdmissionChecked += OnAdmission;
		var lanes = new[] { guests.Settings.ArrivalLaneA, guests.Settings.ArrivalLaneB }.Where( lane => lane is { Length: >= 2 } && grid.IsWalkable( lane[^1].X, lane[^1].Y ) ).Select( lane => lane![^1] ).ToArray();
		var minuteTicks = M3Gate.TicksForMinutes( 1 );

		for ( long tick = 1; tick <= ticks; tick++ )
		{
			currentTick = tick;
			// Level.Update order: SyncObjectEconomy, guests, (prototype ride), objects, economy.
			foreach ( var (item, instance) in economyLinks )
			{
				if ( economy.TryGetObject( instance, out var state ) )
					state.IsOpen = item.IsOpen;
			}
			guests.Tick( Tick );
			foreach ( var item in fixedItems )
				item.Simulate( Tick );
			foreach ( var item in placed )
				item.Runtime.Simulate( Tick );
			runtime.FixedTick();

			// Time: strictly increasing simulation clocks; the park calendar advances in whole turns (non-decreasing).
			var guestTime = guests.TimeSeconds;
			var parkSeconds = ParkCalendar.Seconds( economy.Tick );
			if ( !double.IsFinite( guestTime ) || guestTime < 0 || guestTime <= previousGuestTime )
				timeViolation.Add( tick, $"guest time {previousGuestTime} -> {guestTime}" );
			if ( economy.Tick <= previousEconomyTick || economy.Tick < 0 )
				timeViolation.Add( tick, $"economy tick {previousEconomyTick} -> {economy.Tick}" );
			if ( parkSeconds < previousParkSeconds || parkSeconds < 0 )
				timeViolation.Add( tick, $"park clock {previousParkSeconds} -> {parkSeconds}" );
			for ( var index = 0; index < placed.Count; index++ )
			{
				var script = placed[index].Runtime.Script;
				if ( script == null )
					continue;
				var time = script.TimeMilliseconds;
				if ( !double.IsFinite( time ) || time < 0 || time <= scriptTimes[index] )
					timeViolation.Add( tick, $"{placed[index].Entry.SettingsName} script time {scriptTimes[index]} -> {time}" );
				scriptTimes[index] = time;
				if ( script.State == RideVMState.Faulted )
					faults.Add( tick, $"tick {tick}: {placed[index].Entry.SettingsName}: {script.FaultMessage}" );
				// The gate opens every placed object and never closes or removes it, so its script must keep running.
				// RideVM reaches Halted only by running past its last instruction or by Stop() (object removed or
				// level torn down: OriginalObjectRuntime.Stop); closing is VAR_RIDECLOSED and leaves the script running.
				else if ( script.State == RideVMState.Halted )
					halted.Add( tick, $"tick {tick}: {placed[index].Entry.SettingsName} script halted while open" );
			}
			previousGuestTime = guestTime;
			previousEconomyTick = economy.Tick;
			previousParkSeconds = parkSeconds;

			// Ledger: balance = opening balance + income - expenses over every closed and the current month.
			var totals = LedgerTotals();
			var expected = openingBalance + totals.Where( pair => ParkLedger.IsIncome( pair.Key ) ).Sum( pair => pair.Value )
				- totals.Where( pair => !ParkLedger.IsIncome( pair.Key ) ).Sum( pair => pair.Value );
			if ( expected != economy.Balance )
				ledgerViolation.Add( tick, $"balance {economy.Balance} vs ledger {expected}" );

			// Guests: stages, queue membership (walking up included), reachability of their goals.
			foreach ( var track in tracks.Values )
				track.Seen = false;
			foreach ( var guest in guests.Guests )
			{
				if ( !tracks.TryGetValue( guest.Id, out var track ) )
					tracks[guest.Id] = track = new GuestTrack { State = guest.State };
				track.Seen = true;
				if ( guests.IsInPark( guest ) && guest.State != GuestState.LeavingPark )
					track.Admitted = true;
				if ( guest.IsInQueue && track.QueueSince < 0 )
				{
					track.QueueSince = tick;
					track.QueueAttraction = guest.AttractionId;
					track.QueueJoinPosition = guest.QueuePosition;
					track.StoodOnQueueCell = false;
				}
				else if ( !guest.IsInQueue && track.QueueSince >= 0 )
				{
					// Boarding sets Using (GuestSimulation.OnVisitorBoarded); any other exit left the queue without riding.
					if ( guest.State != GuestState.Using )
						turnedAwayFromQueue++;
					track.QueueSince = -1;
				}
				if ( guest.State == GuestState.Queueing && queueRide != null && guest.AttractionId == queueRide.AttractionId && queueCells.Contains( guest.Cell ) && !track.StoodOnQueueCell )
				{
					track.StoodOnQueueCell = true;
					stoodOnQueueCells++;
				}
				if ( guest.State == GuestState.Using && track.State != GuestState.Using )
				{
					switch ( Role( guest.AttractionId ) )
					{
						case "attraction": track.UsedRide = true; break;
						case "shop": track.UsedShop = true; break;
						case "toilet": track.UsedToilet = true; break;
					}
				}
				if ( track.State == GuestState.AtTicketBooth && guest.State is GuestState.CrossingRoadHome or GuestState.LeavingPark )
					turnedBackAtBooth++;
				if ( guest.Thought == GuestThought.Confused && track.State == GuestState.GoingToRide && guest.State == GuestState.WalkingAround )
					confusedGiveUps++;
				if ( guest.State == GuestState.GoingToRide )
				{
					var target = guests.Attractions.FirstOrDefault( attraction => attraction.AttractionId == guest.AttractionId );
					var join = target?.JoinCell ?? target?.EntranceCell;
					var reachable = join is { } cell && grid.Distance( guest.Cell.X, guest.Cell.Y, cell.X, cell.Y ) >= 0;
					if ( !reachable )
					{
						if ( track.UnreachableSince < 0 )
							track.UnreachableSince = tick;
						else
							unreachable.Add( tick, $"guest {guest.Id} at ({guest.Cell.X},{guest.Cell.Y}) heading to unreachable {AttractionName( guest.AttractionId )} since tick {track.UnreachableSince}" );
					}
					else
						track.UnreachableSince = -1;
				}
				else
					track.UnreachableSince = -1;
				if ( guest.State == GuestState.LeavingPark && guest.Lane == null && lanes.All( lane => grid.Distance( guest.Cell.X, guest.Cell.Y, lane.X, lane.Y ) < 0 ) )
					ejected++;
				track.State = guest.State;
			}
			if ( tracks.Count > guests.Guests.Count )
			{
				foreach ( var (id, track) in tracks.Where( pair => !pair.Value.Seen ).ToList() )
				{
					departed++;
					if ( track.Admitted )
						leftAfterAdmission++;
					if ( track.QueueSince >= 0 )
						vanishedFromQueue++;
					departedTracks.Add( track );
					tracks.Remove( id );
				}
			}

			for ( var index = 0; index < placed.Count; index++ )
				progress[index].MaxQueue = Math.Max( progress[index].MaxQueue, placed[index].Runtime.Visitors.QueueLength );

			// Staff: every wear or breakdown is repaired within the mechanic window.
			for ( var index = 0; index < pendingRepairs.Count; index++ )
			{
				var job = pendingRepairs[index];
				if ( job.Reported || tick < job.Deadline )
					continue;
				repairOverdue.Add( tick, $"tick {tick}: {AttractionNameByInstance( job.Instance )} worn or broken at tick {job.Tick} not repaired by tick {job.Deadline}" );
				pendingRepairs[index] = job with { Reported = true };
			}

			litterPeak = Math.Max( litterPeak, economy.LitterScaled );
			// Litter left from an earlier tick must shrink at the next park-hour update (every turn runs at least one).
			if ( previousLitter > 0 && ParkCalendar.Turn( economy.Tick ) != ParkCalendar.Turn( economy.Tick - 1 ) && economy.LitterScaled != 0 && economy.LitterScaled >= previousLitter )
				litterNotCleaned.Add( tick, $"tick {tick}: litter {previousLitter / (double)ParkEconomy.LitterScale} -> {economy.LitterScaled / (double)ParkEconomy.LitterScale} items at the park-hour update of turn {ParkCalendar.Turn( economy.Tick )}" );
			if ( economy.LitterScaled < previousLitter )
				litterCleaned += previousLitter - economy.LitterScaled;
			else if ( economy.LitterScaled > previousLitter )
				litterDropped += economy.LitterScaled - previousLitter;
			previousLitter = economy.LitterScaled;

			if ( tick % minuteTicks == 0 )
				MinuteHashes.Add( ComputeGateHash() );
		}
		economy.EventRaised -= OnEvent;
		guests.QueueWaitCompleted -= OnQueueWait;
		foreach ( var item in placed )
			item.Runtime.Visitors.AdmissionChecked -= OnAdmission;
		var stillQueued = guests.Guests.Where( guest => guest.IsInQueue ).Select( guest => guests.QueueWaitTurns( guest ) ).DefaultIfEmpty( 0 ).Max();
		GuestHash = guests.ComputeStateHash();
		GateHash = ComputeGateHash();

		AddTimeRow( ticks, timeViolation );
		AddLedgerRows( startTotals, ledgerViolation );
		AddGuestFlowRow( tracks, departed, leftAfterAdmission, turnedBackAtBooth );
		AddQueueRow( progress, stillQueued, turnedAwayFromQueue, vanishedFromQueue );
		FinishQueueRow( stoodOnQueueCells, boardedFromQueueCells );
		AddReachabilityRow( unreachable, confusedGiveUps, ejected );
		AddScriptRow( faults, halted );
		AddStaffRow( repairs, repairsNeeded, wagesEvents, wagesPaid, litterPeak, litterDropped, litterCleaned,
			repairWindow, repairOverdue, maxRepairTicks, pendingRepairs.Count, litterNotCleaned );
		foreach ( var item in fixedItems )
			item.Stop();
		foreach ( var item in placed )
			item.Runtime.Stop();
	}

	/// <summary>
	/// Most consecutive admission evaluations the same head may stand with the gates held but not ready, from the
	/// state-11/12 rules (GuestSimulation): two interludes (<c>turn &gt; +520 + 10</c>: 11 turns each; one running when
	/// the guest becomes head, one on arriving at position 0), the move-up wait at gap ≤ 2
	/// (trunc(1.2 × 2) turns, then the move on the next update), and the walk: the join step plus up to
	/// <paramref name="queueCells"/> cells to its recorded position and up to as many back to position 0, plus one
	/// cell of sub-cell offsets, at the slowest walk speed (× 0.7 below 20 energy, GuestSimulation.Speed); plus one
	/// evaluation for the order of the guest and ride updates in a turn.
	/// </summary>
	internal static (long Turns, string Derivation) HeadNotReadyBound( int queueCells, float walkCellsPerSecond )
	{
		const float TiredWalkFactor = 0.7f;
		var cells = 2 * Math.Max( 1, queueCells ) + 2;
		var walkTurns = (long)Math.Ceiling( cells / (walkCellsPerSecond * TiredWalkFactor) * 1000 / ParkCalendar.TurnMilliseconds );
		var moveUpTurns = (long)(GuestSimulation.MoveDelayFactor * GuestSimulation.MoveUpWaitGap) + 1;
		// Interludes start only when standing at the recorded position (state 11 step 7): one may be running when the guest
		// becomes head, one more on arriving at position 0 (the guest updates before the ride in a turn); the next would
		// need another NeedsWindowTurns, and the guest is called first.
		const int Interludes = 2;
		var turns = walkTurns + moveUpTurns + Interludes * (GuestSimulation.InterludeTurns + 1) + 1;
		return (turns, FormattableString.Invariant( $"walk {cells} cells at {walkCellsPerSecond} x {TiredWalkFactor} cells/s = {walkTurns} turns + move-up wait {moveUpTurns} + {Interludes} interludes x {GuestSimulation.InterludeTurns + 1} + 1 update order = {turns} turns" ));
	}

	/// <summary>
	/// Park turns from a wear or breakdown event until the repair must be done, from ParkEconomy's staff timing: the
	/// event is raised at a day end, the next turn's park-hour update dispatches a free mechanic (a turn is 3,750
	/// park-clock seconds, so every turn runs at least one hour update), and a job takes
	/// <c>WorkDuration</c> hours (at least 1). With one job per ride ahead of it, the window is
	/// 1 + rides × the longest WorkDuration of the hired mechanics' grades.
	/// </summary>
	private (long Turns, string Derivation) RepairWindow()
	{
		var role = economy.Settings[StaffType.Mechanic];
		var mechanics = economy.Staff.Members.Where( member => member.Type == StaffType.Mechanic ).ToList();
		var hours = mechanics.Count == 0 ? 1 : mechanics.Max( member => Math.Max( 1, role.WorkDuration[member.Grade] ) );
		var rides = economyLinks.Count( link => economy.TryGetObject( link.Instance, out var state ) && state.Kind == ParkObjectKind.Ride );
		var turns = 1 + Math.Max( 1, rides ) * (long)hours;
		return (turns, $"1 turn to dispatch + {Math.Max( 1, rides )} ride job(s) x WorkDuration {hours} h (>= 1 hour update per turn: {ParkCalendar.SecondsPerTurn} s per turn) = {turns} turns");
	}

	private static long RepairDeadline( long eventTick, long turns ) => ParkCalendar.TickOfTurn( ParkCalendar.Turn( eventTick ) + turns );

	private string AttractionNameByInstance( int instance ) => placed.FirstOrDefault( item => item.EconomyInstance == instance )?.Entry.SettingsName ?? $"instance {instance}";

	private string AttractionName( int id ) => placed.FirstOrDefault( item => item.Runtime.Visitors.AttractionId == id )?.Entry.SettingsName ?? $"attraction {id}";

	private string? Role( int id ) => placed.FirstOrDefault( item => item.Runtime.Visitors.AttractionId == id )?.Role;

	/// <summary>Per-category totals of every closed month plus the open month.</summary>
	private Dictionary<LedgerCategory, long> LedgerTotals()
	{
		var totals = Enum.GetValues<LedgerCategory>().ToDictionary( category => category, category => economy.Ledger.CurrentTotals.GetValueOrDefault( category ) );
		foreach ( var month in economy.Ledger.History )
		{
			foreach ( var category in Enum.GetValues<LedgerCategory>() )
				totals[category] += month[category];
		}
		return totals;
	}

	private void AddTimeRow( long ticks, Violation violation )
	{
		var evidence = new JsonObject
		{
			["ticks"] = ticks,
			["guestSeconds"] = Math.Round( guests.TimeSeconds, 6 ),
			["expectedSeconds"] = ticks / (double)FixedStepClock.TicksPerSecond,
			["economyTick"] = economy.Tick,
			["parkDate"] = economy.Date.ToString(),
			["violations"] = violation.Count,
			["firstViolation"] = violation.FirstDetail
		};
		AddRow( "time.monotonic", "economy", "Time strictly increases; never negative or NaN (park calendar non-decreasing per turn)",
			violation.Count == 0 ? M3GateVerdict.Pass : M3GateVerdict.Fail, violation.First, evidence );
	}

	private void AddLedgerRows( Dictionary<LedgerCategory, long> start, Violation violation )
	{
		var end = LedgerTotals();
		var during = end.ToDictionary( pair => pair.Key, pair => pair.Value - start[pair.Key] );
		var income = during.Where( pair => ParkLedger.IsIncome( pair.Key ) && pair.Value != 0 ).ToDictionary( pair => pair.Key, pair => pair.Value );
		var expenses = during.Where( pair => !ParkLedger.IsIncome( pair.Key ) && pair.Value != 0 ).ToDictionary( pair => pair.Key, pair => pair.Value );
		var evidence = new JsonObject
		{
			["setupCost"] = setupCost,
			["balanceAfterSetup"] = setupBalance,
			["balanceAtEnd"] = economy.Balance,
			["incomeDuringRun"] = Totals( income ),
			["expensesDuringRun"] = Totals( expenses ),
			["monthsClosed"] = economy.Ledger.History.Count
		};
		AddRow( "economy.income-and-expenses", "economy", "Income and expenses both booked during the run",
			income.Count > 0 && expenses.Count > 0 ? M3GateVerdict.Pass : M3GateVerdict.Fail, null, evidence );
		AddRow( "economy.ledger-consistent", "economy", "Balance equals opening balance plus booked income minus expenses",
			violation.Count == 0 ? M3GateVerdict.Pass : M3GateVerdict.Fail, violation.First,
			new JsonObject { ["samples"] = economy.Tick, ["violations"] = violation.Count, ["firstViolation"] = violation.FirstDetail, ["ledgerHistoryMonths"] = economy.Ledger.History.Count } );
	}

	private static string Totals( Dictionary<LedgerCategory, long> totals ) => totals.Count == 0 ? "none" : string.Join( ", ", totals.Select( pair => $"{pair.Key} {pair.Value}" ) );

	private void AddGuestFlowRow( Dictionary<int, GuestTrack> active, long departed, long leftAfterAdmission, long turnedBackAtBooth )
	{
		var all = active.Values.Concat( departedTracks ).ToList();
		var stages = new JsonObject
		{
			["arrived"] = all.Count,
			["admitted"] = guests.Admissions,
			["turnedBackAtBooth"] = turnedBackAtBooth,
			["usedAttraction"] = all.Count( track => track.UsedRide ),
			["usedShop"] = all.Count( track => track.UsedShop ),
			["usedToilet"] = all.Count( track => track.UsedToilet ),
			["departed"] = departed,
			["departedAfterAdmission"] = leftAfterAdmission,
			["inParkAtEnd"] = guests.GetStatistics().InPark
		};
		foreach ( var item in placed )
			stages[$"{item.Role}Boarded"] = item.Runtime.Visitors.BoardedTotal;
		var missing = new List<string>();
		if ( all.Count == 0 ) missing.Add( "arrive" );
		if ( guests.Admissions == 0 ) missing.Add( "admit" );
		if ( !all.Any( track => track.UsedRide ) ) missing.Add( "attraction" );
		if ( !all.Any( track => track.UsedShop ) ) missing.Add( "shop" );
		if ( !all.Any( track => track.UsedToilet ) ) missing.Add( "toilet" );
		if ( leftAfterAdmission == 0 ) missing.Add( "leave" );
		stages["missingStages"] = missing.Count == 0 ? "none" : string.Join( ", ", missing );
		AddRow( "guests.flow", "guests", "Visitors arrive, use the ride, shop and toilet, and leave", missing.Count == 0 ? M3GateVerdict.Pass : M3GateVerdict.Fail, null, stages );
	}

	/// <summary>
	/// The queue row (QUEUE-plan §9). It fails on a derived progress violation: (a) the gates held while the same head
	/// was not ready for longer than <see cref="HeadNotReadyBound"/>, or a queue the admission handshake cannot
	/// move (VAR_LETMEON non-zero with nobody called on two consecutive evaluations: §9a's two-update rule).
	/// Otherwise it stays unresolved: (b) the time from a call to boarding includes the script's consumption of
	/// VAR_LETMEON and the Belly Bounce wait bound needs the boarding latency τ, neither of which is traced
	/// (GATE-003), so both are reported, not judged.
	/// </summary>
	private void AddQueueRow( QueueProgress[] progress, long stillQueuedTurns, long leftWithoutBoarding, long vanished )
	{
		var turnSeconds = ParkCalendar.TurnMilliseconds / 1000.0;
		var evidence = new JsonObject
		{
			["leftQueueWithoutBoarding"] = leftWithoutBoarding,
			["vanishedWhileQueued"] = vanished,
			["stillQueuedAtEndMaxSeconds"] = Math.Round( stillQueuedTurns * turnSeconds, 3 ),
			["rule"] = "FAIL: same head not ready with the gates held beyond its derived bound, or VAR_LETMEON non-zero with nobody called on 2 consecutive evaluations; else UNRESOLVED (call-to-boarding time and tau untraced)"
		};
		for ( var index = 0; index < placed.Count; index++ )
		{
			var item = placed[index];
			var state = progress[index];
			var visitors = item.Runtime.Visitors;
			var waits = state.Waits;
			var entry = new JsonObject
			{
				["maxQueue"] = $"{state.MaxQueue}/{visitors.MaximumQueueLength}",
				["queueCells"] = visitors.QueueSizeInCells,
				["completedWaits"] = waits.Count,
				["maxWaitSeconds"] = waits.Count == 0 ? 0 : Math.Round( waits.Max( wait => wait.Turns ) * turnSeconds, 3 ),
				["meanWaitSeconds"] = waits.Count == 0 ? 0 : Math.Round( waits.Average( wait => wait.Turns ) * turnSeconds, 2 ),
				["admissionEvaluations"] = state.Evaluations,
				["headNotReadyMaxTurns"] = state.MaxHeadStreak,
				["headNotReadyBoundTurns"] = state.HeadBound,
				["headNotReadyBound"] = HeadNotReadyBound( visitors.QueueSizeInCells, guests.Settings.WalkSpeedCellsPerSecond ).Derivation,
				["headNotReadyMaxAcrossHeads"] = visitors.MaximumHeadNotReadyStreak,
				["blockedMaxEvaluations"] = state.MaxBlockedStreak,
				["calledAgeMaxTurns"] = visitors.MaximumCalledAgeTurns,
				["calledAgeAtEndTurns"] = visitors.CalledAgeTurns
			};
			if ( item.Role == "attraction" )
				entry["waitBound"] = WaitBound( visitors, waits, turnSeconds );
			evidence[item.Role] = entry;
		}
		var violations = progress.SelectMany( state => new[] { state.HeadNotReady, state.Blocked } ).Where( violation => violation.Count > 0 ).OrderBy( violation => violation.First ).ToList();
		evidence["violations"] = violations.Sum( violation => violation.Count );
		evidence["firstViolation"] = violations.FirstOrDefault()?.FirstDetail;
		AddRow( "queues.no-stuck-queue", "rides", "Queues make progress: no head stuck beyond its derived bound, no blocked admission",
			violations.Count > 0 ? M3GateVerdict.Fail : M3GateVerdict.Unresolved, violations.FirstOrDefault()?.First, evidence );
	}

	/// <summary>
	/// QUEUE-plan §9b for a BOUNCE ride: a guest joining at position p waits at most (⌊p / CAP⌋ + 1) × (DUR + 1 s + τ),
	/// so W_max = ⌈Qmax / CAP⌉ × (DUR + 1 s + τ). Reported with the τ parameter and the τ the run implies.
	/// </summary>
	// [APPROX:GATE-003] the Belly Bounce wait bound is reported with boarding latency tau = 0 s (a lower bound) next to the tau the run implies; it is evidence, not a pass threshold — evidence needed: the walk speed and the script loop latency between VAR_LETMEON and BOUNCE (QUEUE-plan §9b, §10)
	private static JsonObject WaitBound( RideVisitorBridge ride, List<(long Turns, int Position)> waits, double turnSeconds )
	{
		const double TauParameterSeconds = 0;
		var capacity = Math.Max( 1, ride.Parameters.Capacity );
		var cycle = ride.Parameters.Duration + 1.0;
		var qmax = ride.MaximumQueueLength;
		var cycles = (qmax + capacity - 1) / capacity;
		var implied = waits.Where( wait => wait.Position >= 0 ).Select( wait => (Tau: wait.Turns * turnSeconds / (wait.Position / capacity + 1) - cycle, wait.Turns, wait.Position) ).ToList();
		var measured = implied.Count == 0 ? double.NaN : implied.Max( item => item.Tau );
		var worst = implied.Count == 0 ? default : implied.First( item => item.Tau == measured );
		return new JsonObject
		{
			["capacity"] = capacity,
			["durationSeconds"] = ride.Parameters.Duration,
			["qmax"] = qmax,
			["tauParameterSeconds"] = TauParameterSeconds,
			["wMaxAtTauParameterSeconds"] = Math.Round( cycles * (cycle + TauParameterSeconds), 3 ),
			["tauMeasuredSeconds"] = double.IsNaN( measured ) ? null : Math.Round( measured, 3 ),
			["wMaxAtTauMeasuredSeconds"] = double.IsNaN( measured ) ? null : Math.Round( cycles * (cycle + measured), 3 ),
			["waitsAboveBoundAtTauParameter"] = waits.Count( wait => wait.Position >= 0 && wait.Turns * turnSeconds > (wait.Position / capacity + 1) * (cycle + TauParameterSeconds) ),
			["tauMeasuredFrom"] = implied.Count == 0 ? "no boarding with a recorded join position" : FormattableString.Invariant( $"wait {Math.Round( worst.Turns * turnSeconds, 3 )} s from position {worst.Position}" ),
			["boardingsByJoinPosition"] = implied.Count == 0 ? "none" : FormattableString.Invariant( $"p 0..{implied.Max( item => item.Position )}, mean p {Math.Round( implied.Average( item => item.Position ), 1 )}" ),
			["derivation"] = $"W_max = ceil(Qmax {qmax} / CAP {capacity}) x (DUR {ride.Parameters.Duration} + 1 s + tau); tau measured = max over boardings of W(p) / (floor(p / CAP) + 1) - (DUR + 1 s); Qmax = min(limit {ride.QueueLimit}, 4 x {ride.QueueSizeInCells} cells)"
		};
	}

	private void AddReachabilityRow( Violation violation, long confused, long ejected )
	{
		var unreachableTargets = placed.Where( item => grid.Distance( entranceCell.X, entranceCell.Y, item.Runtime.Visitors.EntranceCell.X, item.Runtime.Visitors.EntranceCell.Y ) < 0
			|| grid.Distance( item.Runtime.Visitors.ExitCell.X, item.Runtime.Visitors.ExitCell.Y, entranceCell.X, entranceCell.Y ) < 0 ).Select( item => item.Entry.SettingsName ).ToList();
		var evidence = new JsonObject
		{
			["unreachableAttractions"] = unreachableTargets.Count == 0 ? "none" : string.Join( ", ", unreachableTargets ),
			["guestsHeadingToUnreachableTarget"] = violation.Count,
			["firstViolation"] = violation.FirstDetail,
			["gaveUpConfused"] = confused,
			["leavingWithoutReachableExitTicks"] = ejected,
			["rule"] = "a guest heading to an attraction must have a path to its join cell (the back of the queue, else the entrance cell), or give up (Confused) on the next tick; leaving guests without a reachable lane vanish (GuestSimulation simplification)"
		};
		AddRow( "paths.no-unreachable-goal", "paths", "Every guest target is reachable, or the guest gives up through an existing rule",
			violation.Count == 0 && unreachableTargets.Count == 0 ? M3GateVerdict.Pass : M3GateVerdict.Fail, violation.First, evidence );
	}

	private void AddScriptRow( Violation faults, Violation halted )
	{
		var notRunning = placed.Where( item => item.Runtime.Script?.State is not (RideVMState.Running or RideVMState.Waiting) ).Select( item => item.Entry.SettingsName ).ToList();
		var evidence = new JsonObject
		{
			["scripts"] = string.Join( ", ", placed.Select( item => $"{item.Entry.SettingsName} {item.Runtime.Script?.State}" ).Concat( fixedItems.Select( item => $"{item.Entry.SettingsName} {item.Script?.State}" ) ) ),
			["attractionCycles"] = placed.FirstOrDefault( item => item.Role == "attraction" )?.Runtime.CompletedCycles ?? 0,
			["firstFault"] = faults.FirstDetail,
			["haltedTicks"] = halted.Count,
			["firstHalt"] = halted.FirstDetail,
			["notRunningAtEnd"] = notRunning.Count == 0 ? "none" : string.Join( ", ", notRunning ),
			["rule"] = "no placed object's script faults or halts while it is open (Running or Waiting every tick); completed cycles are not required (RIDES-023: BOUNCE does not toggle VAR_RUNNING)"
		};
		var first = new[] { faults.First, halted.First }.Where( tick => tick != null ).Min();
		AddRow( "rides.scripts-run", "rides", "Placed objects' original scripts keep running while open, without faults",
			faults.Count == 0 && halted.Count == 0 && notRunning.Count == 0 ? M3GateVerdict.Pass : M3GateVerdict.Fail, first, evidence );
	}

	/// <summary>
	/// Each hired staff type must do its own work for the whole run: every wear or breakdown must be repaired within
	/// the mechanic window (<see cref="RepairWindow"/>; events whose window runs past the end are still pending, not
	/// failures), and litter left from an earlier tick must shrink at every park-hour update (at least one per turn).
	/// A type that was not hired fails the row.
	/// </summary>
	private void AddStaffRow( long repairs, long repairsNeeded, long wageEvents, long wages, long litterPeak, long litterDropped, long litterCleaned,
		(long Turns, string Derivation) repairWindow, Violation repairOverdue, long maxRepairTicks, int pendingRepairs, Violation litterNotCleaned )
	{
		bool Hired( StaffType type ) => economy.Staff.Members.Any( member => member.Type == type );
		var problems = new List<string>();
		if ( wageEvents == 0 )
			problems.Add( "no wages paid" );
		if ( !Hired( StaffType.Mechanic ) )
			problems.Add( "no mechanic hired" );
		else if ( repairsNeeded > 0 && repairs == 0 )
			problems.Add( "mechanic made no repair although a ride needed one" );
		if ( repairOverdue.Count > 0 )
			problems.Add( $"{repairOverdue.Count} wear/breakdown event(s) not repaired within {repairWindow.Turns} turns" );
		if ( !Hired( StaffType.Handyman ) )
			problems.Add( "no handyman hired" );
		else if ( (litterPeak > 0 || litterDropped > 0) && litterCleaned == 0 )
			problems.Add( "handyman cleaned no litter although litter existed" );
		if ( litterNotCleaned.Count > 0 )
			problems.Add( $"litter not reduced at {litterNotCleaned.Count} park-hour update(s)" );
		var evidence = new JsonObject
		{
			["wagePayments"] = wageEvents,
			["wagesPaid"] = wages,
			["mechanics"] = economy.Staff.Members.Count( member => member.Type == StaffType.Mechanic ),
			["handymen"] = economy.Staff.Members.Count( member => member.Type == StaffType.Handyman ),
			["repairsNeeded"] = repairsNeeded,
			["repairs"] = repairs,
			["repairWindowTurns"] = repairWindow.Turns,
			["repairWindow"] = repairWindow.Derivation,
			["longestRepairSeconds"] = Math.Round( maxRepairTicks / (double)FixedStepClock.TicksPerSecond, 3 ),
			["repairsPendingAtEnd"] = pendingRepairs,
			["firstOverdueRepair"] = repairOverdue.FirstDetail,
			["litterPeakItems"] = litterPeak / (double)ParkEconomy.LitterScale,
			["litterDroppedItems"] = litterDropped / (double)ParkEconomy.LitterScale,
			["litterCleanedItems"] = litterCleaned / (double)ParkEconomy.LitterScale,
			["litterWindow"] = $"one park turn: litter present before a turn's park-hour update must shrink there ({ParkCalendar.SecondsPerTurn} s per turn >= {ParkCalendar.SecondsPerHour} s per hour; ParkEconomy.CleanLitter runs every hour)",
			["firstUncleanedLitter"] = litterNotCleaned.FirstDetail,
			["attractionStateOfRepair"] = placed.FirstOrDefault( item => item.Role == "attraction" ) is { } ride && economy.TryGetObject( ride.EconomyInstance, out var state ) ? state.StateOfRepair : null,
			["problems"] = problems.Count == 0 ? "none" : string.Join( "; ", problems )
		};
		var first = new[] { repairOverdue.First, litterNotCleaned.First }.Where( tick => tick != null ).Min();
		AddRow( "staff.work", "staff", "Each hired staff type is paid and keeps doing its work (repairs within the mechanic window, litter shrinking every park hour)",
			problems.Count == 0 ? M3GateVerdict.Pass : M3GateVerdict.Fail, first, evidence );
	}

	/// <summary>
	/// FNV-1a over guest state with attraction ids replaced by their placement index (kept so the hash does not depend
	/// on id allocation), the economy (tick, balance, RNG, ledger, litter, staff) and every script's variables.
	/// </summary>
	private ulong ComputeGateHash()
	{
		var hash = 14695981039346656037UL;
		void Add( long value )
		{
			for ( var shift = 0; shift < 64; shift += 8 )
			{
				hash ^= (byte)(value >> shift);
				hash *= 1099511628211UL;
			}
		}
		void AddFloat( float value ) => Add( BitConverter.SingleToInt32Bits( value ) );
		long Normalised( int id ) => id == 0 ? 0 : placed.FindIndex( item => item.Runtime.Visitors.AttractionId == id ) + 1;
		Add( guests.TickCount );
		Add( guests.Admissions );
		Add( guests.Guests.Count );
		foreach ( var guest in guests.Guests )
		{
			Add( guest.Id );
			Add( guest.Type );
			Add( (int)guest.State );
			AddFloat( guest.X );
			AddFloat( guest.Y );
			AddFloat( guest.Hunger );
			AddFloat( guest.Thirst );
			AddFloat( guest.Toilet );
			AddFloat( guest.Happiness );
			Add( guest.Money );
			Add( Normalised( guest.AttractionId ) );
			Add( Normalised( guest.LastAttractionId ) );
		}
		Add( economy.Tick );
		Add( economy.Balance );
		Add( (long)economy.Random.State );
		Add( economy.LitterScaled );
		Add( economy.Staff.Members.Count );
		foreach ( var category in Enum.GetValues<LedgerCategory>() )
			Add( economy.Ledger.CurrentTotals.GetValueOrDefault( category ) );
		foreach ( var item in fixedItems.Concat( placed.Select( item => item.Runtime ) ) )
		{
			if ( item.Script == null )
				continue;
			Add( BitConverter.DoubleToInt64Bits( item.Script.TimeMilliseconds ) );
			foreach ( var variable in item.Script.Variables )
				Add( variable );
		}
		return hash;
	}

	private void AddRow( string id, string area, string title, M3GateVerdict verdict, long? first, JsonObject evidence ) =>
		rows.Add( new M3GateRow( id, area, title, verdict, first, evidence ) );
}

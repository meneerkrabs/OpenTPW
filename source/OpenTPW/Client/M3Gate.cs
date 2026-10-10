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
	private int pathCellsBuilt;
	private long pathCost;
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
		PlaceObject( "shop", entry => entry.Category == ObjectCategory.Shop && OriginalObjectRuntime.CreateVisitorBridge( entry, 1 ).Satisfies != GuestNeeds.None );
		PlaceObject( "toilet", entry => OriginalObjectRuntime.CreateVisitorBridge( entry, 1 ).Satisfies.HasFlag( GuestNeeds.Toilet ) );
		BuildQueue();
		HireStaff();
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
	/// A straight path from the end of the entrance walkway into the park, bought cell by cell. The runtime has no
	/// path builder a player can use (no HUD path tool; nothing outside this gate calls <see cref="GuestPathGrid.SetPath"/>),
	/// so the row fails: M3's "build paths" is not met. The cells are still laid as a scripted substitute so that the
	/// simulation rows can be measured.
	/// </summary>
	// [APPROX:GATE-002] scripted paths are laid by editing the guest path grid and charging Costs.PathCell per cell (no original path build rules: slope, land ownership, connection limits) — evidence needed: original path-building rules and costs
	private void BuildPaths()
	{
		var refused = ParkEconomy.PurchaseResult.Ok;
		if ( entranceCell.X >= 0 )
		{
			var lane = (guests.Settings.ArrivalLaneA ?? guests.Settings.ArrivalLaneB)!;
			var (dx, dy) = (Math.Sign( lane[^1].X - lane[^2].X ), Math.Sign( lane[^1].Y - lane[^2].Y ));
			var (x, y) = entranceCell;
			while ( grid.IsWalkable( x + dx, y + dy ) )
				(x, y) = (x + dx, y + dy);
			spine.Add( (x, y) );
			for ( var index = 0; index < SpineLength && refused == ParkEconomy.PurchaseResult.Ok; index++ )
			{
				(x, y) = (x + dx, y + dy);
				if ( !CanLayPath( x, y ) )
					break;
				refused = LayPath( x, y );
				if ( refused == ParkEconomy.PurchaseResult.Ok )
					spine.Add( (x, y) );
			}
		}
		var evidence = new JsonObject
		{
			["cellsBuilt"] = pathCellsBuilt,
			["cost"] = pathCost,
			["costPerCell"] = economy.CellCost( CellPurchase.Path ),
			["from"] = spine.Count > 0 ? $"({spine[0].X},{spine[0].Y})" : "none",
			["to"] = spine.Count > 0 ? $"({spine[^1].X},{spine[^1].Y})" : "none",
			["purchase"] = refused.ToString(),
			["inGameBuilder"] = "no in-game path builder: no HUD path tool and no player-facing path API; GuestPathGrid.SetPath has no caller outside the gate",
			["builtBy"] = "scripted substitute (GATE-002): GuestPathGrid.SetPath + ParkEconomy.TryBuyCells(Path)"
		};
		// The verdict is FAIL until a player-facing path builder exists; the gate must then build through it.
		AddRow( "build.paths", "paths", "Paths from the entrance, built with the in-game path builder", M3GateVerdict.Fail, null, evidence );
	}

	private bool CanLayPath( int x, int y ) => grid.InBounds( x, y ) && x < buildGrid.Width && y < buildGrid.Height && !grid.IsWalkable( x, y )
		&& !occupied.Contains( (x, y) ) && buildGrid.CheckTerrain( x, y ) == OriginalPlacementResult.Allowed;

	/// <summary>Cells the object build rule treats as taken: placed objects, plus the gate's own path cells (stricter than ParkObjects, which does not know them).</summary>
	private bool IsBlocked( int x, int y ) => occupied.Contains( (x, y) ) || grid.IsWalkable( x, y );

	private ParkEconomy.PurchaseResult LayPath( int x, int y )
	{
		var result = economy.TryBuyCells( CellPurchase.Path, 1 );
		if ( result != ParkEconomy.PurchaseResult.Ok )
			return result;
		grid.SetPath( x, y, true );
		pathCellsBuilt++;
		pathCost += economy.CellCost( CellPurchase.Path );
		return result;
	}

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
			var site = FindSite( entry );
			if ( site == null )
				continue;
			var (clickX, clickY, rotation) = site.Value;
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
	/// The clicked cell and rotation that place <paramref name="entry"/> next to the spine: the build rule allows it,
	/// its entrance opens onto a spine cell (nearest the entrance first) and its exit needs the shortest snap.
	/// </summary>
	private (int ClickX, int ClickY, int Rotation)? FindSite( ObjectCatalogEntry entry )
	{
		if ( spine.Count < 2 )
			return null;
		var minX = spine.Min( cell => cell.X ) - 8;
		var maxX = spine.Max( cell => cell.X ) + 8;
		var minY = spine.Min( cell => cell.Y ) - 8;
		var maxY = spine.Max( cell => cell.Y ) + 8;
		(int, int, int)? best = null;
		var bestScore = (int.MaxValue, int.MaxValue);
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
					var spineIndex = spine.IndexOf( (entrance.OutsideX, entrance.OutsideY) );
					if ( spineIndex < 1 || Level.ResolveVisitorCells( points, grid ) is not { } cells )
						continue;
					var exit = points.FirstOrDefault( point => point.Kind == ObjectCellKind.Exit );
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
		return best;
	}

	/// <summary>
	/// Queue paths: the economy prices queue cells (<c>Costs.QueueCell</c>) but guests never walk queue cells;
	/// <see cref="GuestSimulation"/> keeps a virtual queue of slots at the entrance cell. Nothing to place.
	/// </summary>
	private void BuildQueue()
	{
		var ride = placed.FirstOrDefault( item => item.Role == "attraction" );
		var evidence = new JsonObject
		{
			["queueCellCost"] = economy.CellCost( CellPurchase.Queue ),
			["placeable"] = false,
			["runtimeQueue"] = "virtual: GuestSimulation.QueueSlot positions at the entrance cell, RideVisitorBridge.MaximumQueueLength",
			["maximumQueueLength"] = ride?.Runtime.Visitors.MaximumQueueLength ?? 0,
			["reason"] = "GuestPathGrid treats queue cells as not walkable and no queue-path builder exists (docs/GUESTS.md: real queue cells are not walked yet)"
		};
		AddRow( "build.queue", "paths", "Place a queue path for the attraction", M3GateVerdict.Fail, null, evidence );
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
		public long QueueSince = -1;
		public int QueueAttraction;
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
		long maxWait = 0, maxOpenWait = 0;
		string maxWaitDetail = "none";
		var waits = new List<long>();
		long turnedAwayFromQueue = 0, confusedGiveUps = 0, ejected = 0, leftAfterAdmission = 0, turnedBackAtBooth = 0, departed = 0;
		var stall = new long[placed.Count];
		var maxStall = new long[placed.Count];
		var lastBoarded = placed.Select( item => item.Runtime.Visitors.BoardedTotal ).ToArray();
		var maxQueue = new int[placed.Count];
		long repairs = 0, repairsNeeded = 0, wagesPaid = 0, wagesEvents = 0;
		long litterPeak = economy.LitterScaled, litterCleaned = 0, litterDropped = 0, previousLitter = economy.LitterScaled;
		var faults = new Violation();
		var halted = new Violation();
		void OnEvent( ParkEvent item )
		{
			if ( item.Kind == ParkEventKind.RideRepaired )
				repairs++;
			// A ride below WornStateOfRepair or broken down is a mechanic job (ParkEconomy.DispatchMechanics).
			if ( item.Kind is ParkEventKind.RideWorn or ParkEventKind.RideBrokeDown )
				repairsNeeded++;
			if ( item.Kind == ParkEventKind.WagesPaid )
			{
				wagesEvents++;
				wagesPaid += item.Amount;
			}
		}
		economy.EventRaised += OnEvent;
		var lanes = new[] { guests.Settings.ArrivalLaneA, guests.Settings.ArrivalLaneB }.Where( lane => lane is { Length: >= 2 } && grid.IsWalkable( lane[^1].X, lane[^1].Y ) ).Select( lane => lane![^1] ).ToArray();
		var minuteTicks = M3Gate.TicksForMinutes( 1 );

		for ( long tick = 1; tick <= ticks; tick++ )
		{
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

			// Guests: stages, queue waits, reachability of their goals.
			foreach ( var track in tracks.Values )
				track.Seen = false;
			foreach ( var guest in guests.Guests )
			{
				if ( !tracks.TryGetValue( guest.Id, out var track ) )
					tracks[guest.Id] = track = new GuestTrack { State = guest.State };
				track.Seen = true;
				if ( guests.IsInPark( guest ) && guest.State != GuestState.LeavingPark )
					track.Admitted = true;
				var queued = guest.State is GuestState.Queueing or GuestState.WaitingToBoard or GuestState.Boarding;
				if ( queued && track.QueueSince < 0 )
				{
					track.QueueSince = tick;
					track.QueueAttraction = guest.AttractionId;
				}
				else if ( !queued && track.QueueSince >= 0 )
				{
					var wait = tick - track.QueueSince;
					waits.Add( wait );
					if ( wait > maxWait )
					{
						maxWait = wait;
						maxWaitDetail = $"guest {guest.Id} at {AttractionName( track.QueueAttraction )} until tick {tick}";
					}
					if ( guest.State != GuestState.Using )
						turnedAwayFromQueue++;
					track.QueueSince = -1;
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
					var reachable = target != null && grid.Distance( guest.Cell.X, guest.Cell.Y, target.EntranceCell.X, target.EntranceCell.Y ) >= 0;
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
						waits.Add( tick - track.QueueSince );
					departedTracks.Add( track );
					tracks.Remove( id );
				}
			}

			// Queues: longest time a non-empty queue went without a boarding.
			for ( var index = 0; index < placed.Count; index++ )
			{
				var visitors = placed[index].Runtime.Visitors;
				maxQueue[index] = Math.Max( maxQueue[index], visitors.QueueLength );
				if ( visitors.BoardedTotal != lastBoarded[index] || visitors.QueueLength == 0 )
					stall[index] = 0;
				else
					stall[index]++;
				lastBoarded[index] = visitors.BoardedTotal;
				maxStall[index] = Math.Max( maxStall[index], stall[index] );
			}

			litterPeak = Math.Max( litterPeak, economy.LitterScaled );
			if ( economy.LitterScaled < previousLitter )
				litterCleaned += previousLitter - economy.LitterScaled;
			else if ( economy.LitterScaled > previousLitter )
				litterDropped += economy.LitterScaled - previousLitter;
			previousLitter = economy.LitterScaled;

			if ( tick % minuteTicks == 0 )
				MinuteHashes.Add( ComputeGateHash() );
		}
		economy.EventRaised -= OnEvent;
		foreach ( var track in tracks.Values.Where( track => track.QueueSince >= 0 ) )
			maxOpenWait = Math.Max( maxOpenWait, ticks + 1 - track.QueueSince );
		GuestHash = guests.ComputeStateHash();
		GateHash = ComputeGateHash();

		AddTimeRow( ticks, timeViolation );
		AddLedgerRows( startTotals, ledgerViolation );
		AddGuestFlowRow( tracks, departed, leftAfterAdmission, turnedBackAtBooth );
		AddQueueRow( maxWait, maxWaitDetail, maxOpenWait, waits, turnedAwayFromQueue, maxStall, maxQueue );
		AddReachabilityRow( unreachable, confusedGiveUps, ejected );
		AddScriptRow( faults, halted );
		AddStaffRow( repairs, repairsNeeded, wagesEvents, wagesPaid, litterPeak, litterDropped, litterCleaned );
		foreach ( var item in fixedItems )
			item.Stop();
		foreach ( var item in placed )
			item.Runtime.Stop();
	}

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
	/// No original-derived bound exists for a queue wait (queue length 4 × capacity and the VAR_DURATION unit are
	/// approximations: GUESTS.md, RIDES-016), so the row reports raw values and stays unresolved.
	/// </summary>
	private void AddQueueRow( long maxWait, string detail, long maxOpenWait, List<long> waits, long turnedAway, long[] maxStall, int[] maxQueue )
	{
		var seconds = (double)FixedStepClock.TicksPerSecond;
		var evidence = new JsonObject
		{
			["completedWaits"] = waits.Count,
			["maxWaitSeconds"] = maxWait / seconds,
			["maxWait"] = detail,
			["meanWaitSeconds"] = waits.Count == 0 ? 0 : Math.Round( waits.Average() / seconds, 2 ),
			["stillQueuedAtEndMaxSeconds"] = maxOpenWait / seconds,
			["leftQueueWithoutBoarding"] = turnedAway,
			["originalBound"] = "none: no original queue-time or cycle-time rule is traced"
		};
		for ( var index = 0; index < placed.Count; index++ )
		{
			var item = placed[index];
			var duration = item.Entry.Upgrades.FirstOrDefault( level => level.Level == 0 )?.GetInt( "InitDuration" ) ?? 0;
			evidence[$"{item.Role}MaxQueue"] = $"{maxQueue[index]}/{item.Runtime.Visitors.MaximumQueueLength}";
			evidence[$"{item.Role}LongestStallSeconds"] = maxStall[index] / seconds;
			if ( item.Role == "attraction" )
			{
				evidence["attractionCapacity"] = item.Runtime.Visitors.Capacity;
				evidence["attractionInitDuration"] = duration;
				// Reference only (not a pass bound): a full queue drains in ceil(max/capacity) + 1 cycles of InitDuration.
				var cycles = (int)Math.Ceiling( item.Runtime.Visitors.MaximumQueueLength / (double)Math.Max( 1, item.Runtime.Visitors.Capacity ) ) + 1;
				evidence["referenceBoundSeconds"] = cycles * duration;
				evidence["referenceBoundDerivation"] = $"(ceil({item.Runtime.Visitors.MaximumQueueLength}/{item.Runtime.Visitors.Capacity}) + 1) cycles x InitDuration {duration} (unit inferred as seconds, RIDES-016; queue length 4 x capacity is an OpenTPW approximation)";
			}
		}
		AddRow( "queues.no-stuck-queue", "rides", "No guest waits in a queue longer than a ride-cycle bound", M3GateVerdict.Unresolved, null, evidence );
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
			["rule"] = "a guest heading to an attraction must have a path to its entrance, or give up (Confused) on the next tick; leaving guests without a reachable lane vanish (GuestSimulation simplification)"
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
	/// Each hired staff type must do its own work: a mechanic must repair when a repair was needed (a ride became
	/// worn or broke down), a handyman must clean when litter existed. A type that was not hired fails the row.
	/// </summary>
	private void AddStaffRow( long repairs, long repairsNeeded, long wageEvents, long wages, long litterPeak, long litterDropped, long litterCleaned )
	{
		bool Hired( StaffType type ) => economy.Staff.Members.Any( member => member.Type == type );
		var problems = new List<string>();
		if ( wageEvents == 0 )
			problems.Add( "no wages paid" );
		if ( !Hired( StaffType.Mechanic ) )
			problems.Add( "no mechanic hired" );
		else if ( repairsNeeded > 0 && repairs == 0 )
			problems.Add( "mechanic made no repair although a ride needed one" );
		if ( !Hired( StaffType.Handyman ) )
			problems.Add( "no handyman hired" );
		else if ( (litterPeak > 0 || litterDropped > 0) && litterCleaned == 0 )
			problems.Add( "handyman cleaned no litter although litter existed" );
		var evidence = new JsonObject
		{
			["wagePayments"] = wageEvents,
			["wagesPaid"] = wages,
			["mechanics"] = economy.Staff.Members.Count( member => member.Type == StaffType.Mechanic ),
			["handymen"] = economy.Staff.Members.Count( member => member.Type == StaffType.Handyman ),
			["repairsNeeded"] = repairsNeeded,
			["repairs"] = repairs,
			["litterPeakItems"] = litterPeak / (double)ParkEconomy.LitterScale,
			["litterDroppedItems"] = litterDropped / (double)ParkEconomy.LitterScale,
			["litterCleanedItems"] = litterCleaned / (double)ParkEconomy.LitterScale,
			["attractionStateOfRepair"] = placed.FirstOrDefault( item => item.Role == "attraction" ) is { } ride && economy.TryGetObject( ride.EconomyInstance, out var state ) ? state.StateOfRepair : null,
			["problems"] = problems.Count == 0 ? "none" : string.Join( "; ", problems )
		};
		AddRow( "staff.work", "staff", "Each hired staff type is paid and does its work (mechanic repairs, handyman cleans)",
			problems.Count == 0 ? M3GateVerdict.Pass : M3GateVerdict.Fail, null, evidence );
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

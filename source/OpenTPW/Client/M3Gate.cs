using System.Diagnostics;
using System.Security.Cryptography;
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
	/// <summary>Arrival.TimeBetweenArrivals in timer units for this run; null keeps the theme's (150, about 2.5 minutes).</summary>
	public int? ArrivalTimeBetween { get; init; }
	/// <summary>
	/// Narrows the objects the run may place as its attraction (the first researched, buyable ride with a queue that fits): the gate of a mod's objects names one (docs/MODS.md).
	/// Null keeps the gate's own choice.
	/// </summary>
	// [EXT:mods] OpenTPW's own mod system; the original game had no mods
	public Func<ObjectCatalogEntry, bool>? AttractionFilter { get; init; }
	/// <summary>Narrows the shops the run may place (the first researched, buyable shop that satisfies a need and fits); null keeps the gate's own choice, the Drinks Shop.</summary>
	public Func<ObjectCatalogEntry, bool>? ShopFilter { get; init; }
	/// <summary>The only rotation (0, 90, 180 or 270) the shop's site search tries; null tries all four.</summary>
	public int? ShopRotation { get; init; }
	/// <summary>The park's world seed (every random stream); null keeps the economy's default seed, so only <see cref="Seed"/> moves the guests' stream as before. The guest report sets it to vary the whole park.</summary>
	public WorldSeed? WorldSeed { get; init; }
	/// <summary>
	/// Runs the park turns as the game does (<c>Level.AdvanceSimulation</c>): the guests take their ids from a thing pool, and the tick that starts a turn runs the ride scripts and then the
	/// turn's thing sweep. The gate's own invariants keep their plain loop (false); the guest report sets it so it measures the guests as the game runs them.
	/// </summary>
	public bool SweptTurns { get; init; }
	/// <summary>A passive measurement view set on the run's guests before the first tick (the guest report, docs/SMARTER-GUESTS.md); null for the gate's own runs.</summary>
	public IGuestObserver? GuestObserver { get; init; }
	/// <summary>The decision layer the gate's guests are built with (the guest report's measurement of some smarter-guests rules); null takes the gate's, as a level does.</summary>
	public IGuestDecisions? GuestDecisions { get; init; }
	/// <summary>Called with the run's guests after the last tick and before the objects are stopped: stopping a ride turns its queue away, which a measurement must not count.</summary>
	public Action<GuestSimulation>? BeforeTeardown { get; init; }
	/// <summary>Called with the run's guests right after they are built, before anything runs (the guest report's litter-bin proxies, docs/SMARTER-GUESTS.md, phase 6b); null for the gate's own runs.</summary>
	public Action<GuestSimulation>? GuestSetup { get; init; }
	/// <summary>
	/// Further objects placed after the gate's own, each the first researched, buyable object its predicate matches, beside the path as the shop is (a measurement's own shops, such as the winter extension's,
	/// docs/WINTER.md, "Price"). Empty, the default, places nothing more, so the gate's park is unchanged.
	/// </summary>
	public IReadOnlyList<(string Role, Func<ObjectCatalogEntry, bool> Match)> ExtraObjects { get; init; } = Array.Empty<(string, Func<ObjectCatalogEntry, bool>)>();
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
	// [EXT:m3-gate-minute] the M3 gate's "in-game minute" is OpenTPW's harness unit: 60 s of normal-speed simulation (3,600 fixed 60 Hz ticks), not a park-clock minute; the original has no gate and paces play by scaled real time (OpenTPW samples its 60 Hz ticks into park turns, EXT:fixed-60hz-clock)
	// [BIN:TPW-EXE:0x0054F668 park turn cadence] the main loop 0x0054E360 adds 31 ms per substep (0x0054F4C4) and runs a park turn when the substep counter 0x00877D34 & 7 == 0, one turn per 248 ms
	// [BIN:TPW-EXE:0x004F8260 park date] the date is the start plus turn × [calendar +0x1C] / 4 seconds (__allmul, __alldiv, × 10,000,000), and the constructor 0x004F7E80 sets the rate to 15,000 (0x004F7EA9), so one turn is 3,750 park-clock seconds
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
		["sources"] = "EXT:m3-gate-minute; ParkCalendar (BIN:TPW-EXE:0x0054F668 turn every 8th 31 ms substep, 0x004F8260 3,750 s per turn; Mac BIN:STP-PPC:0x101C22E0, 0x100E4394); EXT:fixed-60hz-clock (60 Hz ticks sampled into turns)"
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
		evidence["minuteHashesRun1"] = first.MinuteHashes.Count;
		evidence["minuteHashesRun2"] = second.MinuteHashes.Count;
		// A same-seed run that differs at any recorded minute is not deterministic, even when it converges again by the
		// end (review GATE-V3, S4): the final hashes, every minute hash and the number of minute hashes must all match.
		var sameMinutes = divergence < 0 && first.MinuteHashes.Count == second.MinuteHashes.Count;
		var matches = first.GuestHash == second.GuestHash && first.GateHash == second.GateHash && sameMinutes;
		long? firstTick = divergence >= 0 ? (divergence + 1L) * TicksForMinutes( 1 )
			: sameMinutes ? null : (Math.Min( first.MinuteHashes.Count, second.MinuteHashes.Count ) + 1L) * TicksForMinutes( 1 );
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
		GameAudio.Screams.Reset();
		var report = Run( options );
		Console.Write( report.ToSummary() );
		Console.Write( GameAudio.Screams.Summary() );
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
	private readonly ThingPool things = new();
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
	/// <summary>The run's guests, after <see cref="Execute"/> (the guest report reads their attractions).</summary>
	internal GuestSimulation Simulation => guests;
	public ulong GuestHash { get; private set; }
	public ulong GateHash { get; private set; }
	public List<ulong> MinuteHashes { get; } = new();
	public IReadOnlyList<int> AttractionIds => placed.Select( item => item.Runtime.Visitors.AttractionId ).ToList();
	/// <summary>The attraction id of the object placed in <paramref name="role"/>, or null when none was placed.</summary>
	internal int? AttractionIdOf( string role ) => placed.FirstOrDefault( item => item.Role == role )?.Runtime.Visitors.AttractionId;

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
		runtime = ParkEconomyRuntime.ForOriginalLevel( park, ParkStartKind.FullSimulation, options.WorldSeed );
		economy = runtime.Economy;
		catalog = ObjectCatalog.Load( options.Level, economy.Settings.IsEasy );
		buildGrid = new OriginalParkGrid( park );
		grid = GuestPathGrid.FromOriginal( park.Map, null );
		var guestSettings = GuestSettings.Load( options.Level );
		if ( options.ArrivalTimeBetween is int interval )
			guestSettings.ArrivalTimeBetween = interval;
		guests = new GuestSimulation( grid, guestSettings, options.Seed, options.GuestDecisions ) { Observer = options.GuestObserver };
		options.GuestSetup?.Invoke( guests );
		if ( options.SweptTurns )
		{
			// the park's one thing pool as Level builds it: the world's ten singletons, then the gate and the lights, so the first guest is thing 13
			things.AllocateWorldSetup();
			things.Allocate( SaveThingModel.Attraction );
			things.Allocate( SaveThingModel.Attraction );
			guests.AttachThings( things );
		}
		var openingBalance = economy.Balance;
		BuildEntrance();
		runtime.AttachGuests( guests );
		world.AttachWorld( economy.World );
		// a new park starts closed (tp.exe 0x51562d); the gate run opens it as the player's door button would (0x519ef0)
		runtime.Economy.OpenPark();
		ConnectFixedItemsToEconomy();
		BuildPaths();
		PlaceObject( "attraction", entry => entry.Category == ObjectCategory.Ride && entry.HasQueue && (options.AttractionFilter?.Invoke( entry ) ?? true) );
		// Placing a HasQueue ride switches the HUD to the queue tool (ParkHud, BIN:STP-PPC:0x1007497C), so the queue comes next.
		BuildQueue();
		PlaceObject( "shop", entry => entry.Category == ObjectCategory.Shop && OriginalObjectRuntime.CreateVisitorBridge( entry, 1 ).Satisfies != GuestNeeds.None && (options.ShopFilter?.Invoke( entry ) ?? true) );
		PlaceObject( "toilet", entry => OriginalObjectRuntime.CreateVisitorBridge( entry, 1 ).Satisfies.HasFlag( GuestNeeds.Toilet ) );
		foreach ( var (role, match) in options.ExtraObjects )
			PlaceObject( role, match );
		// the gate registers its objects without a placement, so it lays the cell a called guest's search walks into (state 13) as a build lays an entrance
		foreach ( var item in placed )
		{
			var stand = GuestSimulation.StandCellOf( item.Runtime.Visitors );
			ObjectAccessCells.LayStandCell( grid.Cells, stand.X, stand.Y );
		}
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
			fixedItems.Add( new OriginalObjectRuntime( entry, world, seed: null, open: true ) );
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
	/// <see cref="Level.RegisterWithGuests"/> (attractions only; outside cells snapped to the nearest path: the gate does not lay the access cells a build lays).
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
			var site = FindSite( entry, queueCells, role == "shop" ? options.ShopRotation : null );
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
			var item = new OriginalObjectRuntime( entry, world, seed: null, open: true );
			var points = ObjectFootprint.GetAccessPoints( entry.Shape, anchorX, anchorY, rotation ).ToArray();
			var registered = Level.RegisterWithGuests( item, points, guests, rotation );
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
	/// <see cref="QueuePaths.MaximumCells"/>, so that the data limit, not the cells, decides the queue length
	/// whenever the cap allows. A HasQueue ride's limit is its QueueWaitTimeConstant formula: Belly Bounce gets 21, so 6 cells.
	/// </summary>
	internal static int QueueCellsFor( ObjectCatalogEntry entry )
	{
		var limit = RideVisitorBridge.ComputeQueueLimit( OriginalObjectRuntime.CreateQueueParameters( entry ) );
		return (int)Math.Clamp( ((long)limit + RideVisitorBridge.PositionsPerCell - 1) / RideVisitorBridge.PositionsPerCell, 1, QueuePaths.MaximumCells );
	}

	/// <summary>
	/// The clicked cell and rotation that place <paramref name="entry"/> next to the spine: the build rule allows it.
	/// Without a queue its entrance opens onto a spine cell (nearest the entrance first) and its exit needs the
	/// shortest snap. With <paramref name="queueCells"/> &gt; 0 the entrance's outside cell must be free for a queue
	/// (QueuePaths refuses a path cell there: NoFrontCell) and a run of exactly that many queue cells must lead
	/// from it to a cell beside a path, where guests join; the exit with the shortest snap wins, then the front
	/// nearest a path.
	/// </summary>
	private (int ClickX, int ClickY, int Rotation, List<(int X, int Y)>? Route)? FindSite( ObjectCatalogEntry entry, int queueCells, int? onlyRotation = null )
	{
		if ( spine.Count < 2 )
			return null;
		var minX = spine.Min( cell => cell.X ) - 8;
		var maxX = spine.Max( cell => cell.X ) + 8;
		var minY = spine.Min( cell => cell.Y ) - 8;
		var maxY = spine.Max( cell => cell.Y ) + 8;
		(int, int, int)? best = null;
		var bestScore = (int.MaxValue, int.MaxValue);
		var queueSites = new List<(int Snap, int Distance, int ClickX, int ClickY, int Rotation, (int X, int Y) Front, (int DX, int DY) Back, HashSet<(int X, int Y)> Footprint)>();
		var goalDistance = queueCells > 0 ? QueueGoalDistances() : null;
		foreach ( var rotation in onlyRotation is int only ? new[] { only } : new[] { 0, 90, 180, 270 } )
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
						// WALK-plan W3: the cell behind the front continues the line from the ride's entrance cell through the front.
						var back = (DX: entrance.OutsideX - entrance.X, DY: entrance.OutsideY - entrance.Y);
						queueSites.Add( (exitSnap, distance, clickX, clickY, rotation, front, back, footprint) );
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
				if ( FindQueueRoute( site.Front, site.Back, queueCells, site.Footprint, goalDistance! ) is { } route )
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
	/// <see cref="GuestPathGrid.Directions"/> order, so the route is deterministic; null when none is found. The second cell
	/// lies straight behind the front, in direction <paramref name="back"/> away from the ride, so the entrance cell, the
	/// front cell and the next queue cell are in a line: the walk terms of the boarding bound are derived for a straight
	/// front segment (WALK-plan W3). A layout choice of the gate, like the site; the queue tool allows corners there too.
	/// </summary>
	private List<(int X, int Y)>? FindQueueRoute( (int X, int Y) front, (int DX, int DY) back, int length, HashSet<(int X, int Y)> footprint, int[] goalDistance )
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
				if ( used.Contains( next ) || footprint.Contains( next ) || !IsQueueCellFree( next.X, next.Y ) || (cell == front && (dx, dy) != back) )
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
			["builtBy"] = "Level.BuildQueueCell (the queue tool's per-cell code: QueuePaths.CheckExtend + ParkEconomy.TrySpendCell(Queue) + QueuePaths.TryExtend), then QueuePaths.TryConnect (the line ending on the path, code 8); the HUD click route itself is not exercised (no HUD without a GPU)"
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
		// The player ends the queue line on the path beside the back (the validator's code 8), which links the queue to it.
		var connected = laid == queueRoute.Count && QueuePaths.TryConnect( grid, bridge );
		evidence["connected"] = connected;
		var charged = balance - economy.Balance;
		var expectedLength = Math.Min( (long)bridge.QueueLimit, RideVisitorBridge.PositionsPerCell * queueRoute.Count );
		var join = bridge.JoinCell;
		var joinReachable = join is { } cell && grid.Distance( entranceCell.X, entranceCell.Y, cell.X, cell.Y ) >= 0;
		evidence["cellsRequested"] = queueRoute.Count;
		evidence["cellsRequestedRule"] = $"ceil(queue limit {bridge.QueueLimit} / {RideVisitorBridge.PositionsPerCell} per cell), capped at QueuePaths.MaximumCells {QueuePaths.MaximumCells}";
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
		else if ( !connected )
			problems.Add( "the queue's back has no path to connect to" );
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
		/// <summary>Park turn the guest was first seen (the tick it spawned), for WALK-plan's age condition W2.</summary>
		public long FirstSeenTurn;
		/// <summary>The queue walk being timed for walk-stall: 13 (to the stand point), 12 (move-up to slot 0) or 0.</summary>
		public int WalkKind;
		public long WalkSince;
		public bool WalkReported;
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
		/// <summary>Bound on consecutive evaluations with the same head not standing at position 0 (<see cref="M3GateRun.HeadNotReadyBound"/>).</summary>
		public long HeadBound;
		public int HeadGuest;
		/// <summary>Consecutive evaluations the current head has not stood at position 0, whether or not the gates held.</summary>
		public long HeadStreak;
		public long MaxHeadStreak;
		/// <summary>
		/// The current head's rule (a) limit (<see cref="M3GateRun.HeadNotReadyLimit"/>), set at the first evaluation that judges
		/// it, or −1 before. A head younger than <see cref="M3GateRun.MinimumWalkAgeTurns"/> is judged from the turn it reaches
		/// that age (WALK-plan W2, review WV-2): until then its streak does not count (<see cref="HeadDeferred"/>).
		/// </summary>
		public long HeadLimit = -1;
		public bool HeadDeferred;
		public long YoungHeadsDeferred, HeadsOnOwnWalk;
		/// <summary>Largest streak − limit over judged evaluations (0 or more is a violation).</summary>
		public long ClosestToHeadLimit = long.MinValue;
		public long BlockedStreak, MaxBlockedStreak;
		public readonly Violation HeadNotReady = new();
		public readonly Violation Blocked = new();
		/// <summary>The traced boarding bound (BOARD-plan §8), or null when the object is outside its class (<see cref="BoardingReason"/>).</summary>
		public BoardingModel? Boarding;
		public string BoardingReason = "";
		/// <summary>Completed waits with the script's CAP and DUR at boarding, judged at the end of the run.</summary>
		public readonly List<(long Tick, long EndTurn, long Turns, int Position, int Capacity, int Duration)> BoardingWaits = new();
		/// <summary>Park turns the ride was closed or broken, or its CAP or DUR changed (BOARD-plan A3): waits overlapping one are not judged.</summary>
		public readonly SortedSet<long> ExcludedTurns = new();
		public int LastCapacity = -1, LastDuration = -1;
		public readonly Violation WaitAboveBound = new();
		/// <summary>The head the boarding bound times (any head, at the front or not) and the turn it became head.</summary>
		public int BoardingHead;
		public long BoardingHeadSince, MaxHeadToBoarding;
		/// <summary>The current head's own walk beyond H when it became head (<see cref="M3GateRun.HeadWalkTurns"/>), and its maximum.</summary>
		public long BoardingHeadWalk, MaxHeadWalk;
		public readonly HashSet<int> ReportedAges = new();
		/// <summary>Turns whose head was younger than <see cref="M3GateRun.MinimumWalkAgeTurns"/> (WALK-plan W2): waits overlapping one are not judged.</summary>
		public readonly SortedSet<long> YoungHeadTurns = new();
		public readonly Violation WalkStall = new();
		public long MaxStandWalk, MaxMoveUpWalk, StandWalks, MoveUpWalks, StallsOutsideScope;
	}

	/// <summary>
	/// One fixed tick. The gate's plain order: the guests, the object scripts, the economy. With <see cref="M3GateOptions.SweptTurns"/> the order of <c>Level.AdvanceSimulation</c> and
	/// <c>DeterminismTests.SyntheticPark.Run</c>: a tick that starts a park turn of the guests and of the economy runs the substep's scripts and then the economy's turn, whose thing sweep
	/// (<see cref="GateSweep"/>) updates the guests newest first, the economy's things, the objects' admission, the turn's destroys and the arrivals.
	/// </summary>
	private void StepTick()
	{
		if ( !options.SweptTurns )
		{
			guests.Tick( Tick );
			foreach ( var item in fixedItems )
				item.Simulate( Tick );
			foreach ( var item in placed )
				item.Runtime.Simulate( Tick );
			runtime.FixedTick();
			return;
		}
		// Level aligns the guests' turns to the economy's before it decides (Guests.AlignTurns); the gate's park starts both at tick 0, so they must already agree, and a drift is a fault, not a reason to run a plain tick
		if ( guests.NextTickStartsTurn != economy.NextTickStartsTurn )
			throw new InvalidOperationException( $"The guests' and the economy's park turns are not aligned at tick {guests.TickCount + 1} (swept turns need one clock)." );
		var swept = guests.NextTickStartsTurn && economy.NextTickStartsTurn;
		if ( swept )
			guests.BeginSweepTick( Tick );
		else
			guests.Tick( Tick );
		foreach ( var item in fixedItems )
			item.Simulate( Tick );
		foreach ( var item in placed )
		{
			item.Runtime.Visitors.DefersAdmission = swept;
			item.Runtime.Simulate( Tick );
		}
		economy.ThingSweep = swept ? new GateSweep( this ) : null;
		try
		{
			runtime.FixedTick();
		}
		finally
		{
			economy.ThingSweep = null;
		}
	}

	/// <summary>The park turn of Level's sweep with what the gate's park holds: the guests by the pool's list, the economy's own things, the objects' admission and the turn's end.</summary>
	private sealed class GateSweep( M3GateRun run ) : IThingSweep
	{
		public void Run( ParkEconomy economy, long turn, bool lost )
		{
			foreach ( var slot in run.things.Capture() )
				if ( !run.things.IsQueued( slot.Id ) )
					run.guests.SweepGuest( slot.Id );
			run.guests.FinishSweep();
			economy.UpdateThings( turn );
			foreach ( var item in run.placed )
				item.Runtime.Visitors.UpdateAdmission();
			run.things.DestroyQueued();
			run.guests.UpdateTurnEndArrivals();
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
		long turnedAwayFromQueue = 0, confusedGiveUps = 0, ejected = 0, leftAfterAdmission = 0, turnedBackAtBooth = 0, departed = 0, vanishedFromQueue = 0;
		var progress = placed.Select( item => new QueueProgress { HeadBound = HeadNotReadyBound( item.Runtime.Visitors.QueueSizeInCells ).Turns } ).ToArray();
		for ( var index = 0; index < placed.Count; index++ )
			progress[index].Boarding = TraceBoarding( placed[index], out progress[index].BoardingReason );
		var boardingRides = Enumerable.Range( 0, placed.Count ).Where( index => progress[index].Boarding != null ).ToDictionary( index => placed[index].Runtime.Visitors.AttractionId );
		var queueRide = queueBuilt ? placed.First( item => item.Role == "attraction" ).Runtime.Visitors : null;
		var queueCells = queueRide?.QueueCells.ToHashSet() ?? new HashSet<(int X, int Y)>();
		int stoodOnQueueCells = 0, boardedFromQueueCells = 0;
		long repairs = 0, repairsNeeded = 0, wagesPaid = 0, wagesEvents = 0;
		long litterPeak = economy.Litter.Count, litterCleaned = 0, litterDropped = 0;
		var litterWaited = false;
		// Each drop and each sweep, so that both in one tick still count.
		economy.Litter.Changed += ( _, _, type ) =>
		{
			if ( type == LitterType.None )
				litterCleaned++;
			else
				litterDropped++;
		};
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
			// (a) The head is not standing at position 0: bounded per head by its own walk and wait rules, which do not depend
			// on the ride's gates. So every such evaluation counts, whether or not the gates hold (a blocked evaluation does
			// not restart it, review GATE-V3 S2); the streak restarts only when another guest becomes head or the head
			// reaches position 0.
			if ( check.HeadGuest != 0 && !check.HeadAtFront )
			{
				if ( check.HeadGuest != state.HeadGuest )
					(state.HeadGuest, state.HeadStreak, state.HeadLimit, state.HeadDeferred) = (check.HeadGuest, 0, -1, false);
				// WALK-plan W2: the walk terms need a guest at least 15 updates old, so a younger head is judged from the turn it
				// reaches that age, with its walk from then on; a head without a track is judged at once.
				if ( tracks.GetValueOrDefault( check.HeadGuest ) is { } headTrack && guests.ParkTurn - headTrack.FirstSeenTurn < MinimumWalkAgeTurns )
				{
					if ( !state.HeadDeferred )
						state.YoungHeadsDeferred++;
					state.HeadDeferred = true;
				}
				else
				{
					if ( state.HeadLimit < 0 )
					{
						state.HeadLimit = HeadNotReadyLimit( guests.Find( check.HeadGuest ), bridge, state.HeadBound );
						if ( state.HeadLimit < state.HeadBound )
							state.HeadsOnOwnWalk++;
					}
					var streak = ++state.HeadStreak;
					state.MaxHeadStreak = Math.Max( state.MaxHeadStreak, streak );
					state.ClosestToHeadLimit = Math.Max( state.ClosestToHeadLimit, streak - state.HeadLimit );
					if ( streak > state.HeadLimit )
						state.HeadNotReady.Add( currentTick, FormattableString.Invariant( $"tick {currentTick}: {name} head guest {check.HeadGuest} not ready for {streak} evaluations (bound {state.HeadLimit}{(state.HeadLimit < state.HeadBound ? $" from its own walk; queue bound {state.HeadBound}" : "")})" ) );
				}
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
			if ( state.Boarding != null )
				CheckBoardingAges( placed[index], state, check.HeadGuest, tracks, currentTick );
		}
		void OnQueueWait( Guest guest, IRideVisitorBridge ride, long turns )
		{
			var index = placed.FindIndex( item => item.Runtime.Visitors == ride );
			if ( index < 0 )
				return;
			var track = tracks.GetValueOrDefault( guest.Id );
			progress[index].Waits.Add( (turns, track?.QueueJoinPosition ?? -1) );
			var runtime = placed[index].Runtime;
			if ( guest.Id == progress[index].BoardingHead )
				progress[index].MaxHeadToBoarding = Math.Max( progress[index].MaxHeadToBoarding, guests.ParkTurn - progress[index].BoardingHeadSince );
			progress[index].BoardingWaits.Add( (currentTick, guests.ParkTurn, turns, track?.QueueJoinPosition ?? -1,
				runtime.GetVariable( RideVariables.VAR_CAPACITY ), runtime.GetVariable( RideVariables.VAR_DURATION )) );
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
				{
					state.IsOpen = item.IsOpen;
					state.QueueHasRoom = QueueJoinRules.QueueCountsForParkPoints( item.Visitors, grid );
					item.Visitors.Sideshow = new SideshowTerms( state.CostOfGoods, state.Price, state.ChanceOfLosingPercent );   // the price test reads the live words, as Level does
					item.Visitors.WearGauge = state.Repair;   // the worn-toilet exit reads the gauge the relief visits wear, as Level does
				}
			}
			StepTick();

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
					tracks[guest.Id] = track = new GuestTrack { State = guest.State, FirstSeenTurn = guests.ParkTurn };
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
				if ( boardingRides.TryGetValue( guest.AttractionId, out var walkIndex ) )
					CheckWalkStall( placed[walkIndex], progress[walkIndex], guest, track, tick );
				else
					track.WalkKind = 0;
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
				if ( guest.Thought == GuestThought.Confused && track.State == GuestState.GoingToRide && guest.State == GuestState.DoingNothing )
					confusedGiveUps++;
				if ( guest.State == GuestState.GoingToRide )
				{
					var target = guests.Attractions.FirstOrDefault( attraction => attraction.AttractionId == guest.AttractionId );
					var join = target?.JoinCell ?? target?.EntranceCell;
					// state 10 walks the whole way to the back cell (WALK-004): a guest already on one of the ride's queue cells has reached its goal
					// A guest standing on a queue cell (a refused joiner on the back cell, or one the queue let go, QUEUE-010) walks off it by the original search; the path network has no queue cells to measure from.
					var reachable = grid.IsQueue( guest.Cell.X, guest.Cell.Y ) || join is { } cell && grid.Distance( guest.Cell.X, guest.Cell.Y, cell.X, cell.Y ) >= 0;
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
			{
				progress[index].MaxQueue = Math.Max( progress[index].MaxQueue, placed[index].Runtime.Visitors.QueueLength );
				SampleBoardingExclusions( placed[index], progress[index] );
			}

			// Staff: every wear or breakdown is repaired within the mechanic window.
			for ( var index = 0; index < pendingRepairs.Count; index++ )
			{
				var job = pendingRepairs[index];
				if ( job.Reported || tick < job.Deadline )
					continue;
				repairOverdue.Add( tick, $"tick {tick}: {AttractionNameByInstance( job.Instance )} worn or broken at tick {job.Tick} not repaired by tick {job.Deadline}" );
				pendingRepairs[index] = job with { Reported = true };
			}

			var litter = economy.Litter.Count;
			litterPeak = Math.Max( litterPeak, litter );
			// A free handyman claims unclaimed litter at the turn's dispatch: litter left unclaimed beside a free handyman for two turns is a violation.
			if ( ParkCalendar.Turn( economy.Tick ) != ParkCalendar.Turn( economy.Tick - 1 ) )
			{
				var waiting = economy.Litter.Littered().Any( cell => economy.Litter.CollectorAt( cell.X, cell.Y ) == 0 )
					&& economy.Staff.OfType( StaffType.Handyman ).Any( member => member.IsAvailable && !economy.Staff.Strikes.IsActive( member.Type ) );
				if ( waiting && litterWaited )
					litterNotCleaned.Add( tick, $"tick {tick}: unclaimed litter beside a free handyman for two turns (turn {ParkCalendar.Turn( economy.Tick )})" );
				litterWaited = waiting;
			}


			if ( tick % minuteTicks == 0 )
				MinuteHashes.Add( ComputeGateHash() );
		}
		economy.EventRaised -= OnEvent;
		guests.QueueWaitCompleted -= OnQueueWait;
		foreach ( var item in placed )
			item.Runtime.Visitors.AdmissionChecked -= OnAdmission;
		var stillQueued = guests.Guests.Where( guest => guest.IsInQueue ).Select( guest => guests.QueueWaitTurns( guest ) ).DefaultIfEmpty( 0 ).Max();
		var queuedAtEnd = guests.Guests.Where( guest => guest.IsInQueue && guests.QueueWaitTurns( guest ) >= 0 )
			.Select( guest => (guest.AttractionId, Turns: guests.QueueWaitTurns( guest ), Position: tracks.GetValueOrDefault( guest.Id )?.QueueJoinPosition ?? -1) ).ToList();
		GuestHash = guests.ComputeStateHash();
		GateHash = ComputeGateHash();

		AddTimeRow( ticks, timeViolation );
		AddLedgerRows( startTotals, ledgerViolation );
		AddGuestFlowRow( tracks, departed, leftAfterAdmission, turnedBackAtBooth );
		AddQueueRow( progress, stillQueued, queuedAtEnd, ticks, turnedAwayFromQueue, vanishedFromQueue );
		FinishQueueRow( stoodOnQueueCells, boardedFromQueueCells );
		AddReachabilityRow( unreachable, confusedGiveUps, ejected );
		AddScriptRow( faults, halted );
		AddStaffRow( repairs, repairsNeeded, wagesEvents, wagesPaid, litterPeak, litterDropped, litterCleaned,
			repairWindow, repairOverdue, maxRepairTicks, pendingRepairs.Count, litterNotCleaned );
		options.BeforeTeardown?.Invoke( guests );
		foreach ( var item in fixedItems )
			item.Stop();
		foreach ( var item in placed )
			item.Runtime.Stop();
	}

	/// <summary>
	/// Most consecutive admission evaluations the same head may stand away from position 0, from the state-11/12 rules
	/// (GuestSimulation): two interludes (<c>turn &gt; +520 + 10</c>: 11 turns each; one running when the guest becomes
	/// head, one on arriving at position 0), the move-up wait at gap ≤ 2 (trunc(1.2 × 2) turns, then the move on the
	/// next update), and the walk; plus one evaluation for the order of the guest and ride updates in a turn.
	/// The walk only goes forward: the queue position is a list index that only decreases (GetQueuePosition), so after
	/// becoming head a guest makes at most two walks, the rest of the walk it is on and the move-up to position 0, which
	/// together cross at most the N cells from the join cell to the front cell: N × C + 2 × w₂ updates
	/// (<see cref="QueueWalkTurns"/>). Each head is also held to its own remaining walk where that is smaller
	/// (<see cref="HeadNotReadyLimit"/>).
	/// </summary>
	// [APPROX:GATE-006] the walk term N x C + 2 x w2 rests on C = 27 from a bounded enumeration of tp.exe's walk as OpenTPW ports it (WALK-003, WALK-005, WALK-009), not on a proof for the original; it is looser than the retired GATE-005 check (owner decision 2026-10-10) — evidence needed: the neighbour query order and the edge flags of the original traced, then C re-derived
	internal static (long Turns, string Derivation) HeadNotReadyBound( int queueCells )
	{
		var cells = Math.Max( 1, queueCells );
		var walkTurns = cells * QueueCellWalkTurns + 2 * MoveUpWalkTurns;
		var turns = walkTurns + HeadWaitTurns;
		return (turns, FormattableString.Invariant( $"[APPROX:GATE-006] two queue walks over {cells} cells: {cells} x C {QueueCellWalkTurns} + 2 x w2 {MoveUpWalkTurns} = {walkTurns} turns (enumeration of tp.exe's walk as ported, s >= 0.59) + move-up wait {MoveUpWaitTurns} + {HeadInterludes} interludes x {GuestSimulation.InterludeTurns + 1} + 1 update order = {turns} turns; a head's own remaining walk replaces the walk term where smaller" ));
	}

	/// <summary>The move-up wait at gap ≤ 2: trunc(1.2 × 2) turns, then the move on the next update.</summary>
	internal static long MoveUpWaitTurns => (long)(GuestSimulation.MoveDelayFactor * GuestSimulation.MoveUpWaitGap) + 1;

	/// <summary>
	/// Interludes start only when standing at the recorded position (state 11 step 7): one may be running when the guest
	/// becomes head, one more on arriving at position 0 (the guest updates before the ride in a turn); the next would need
	/// another NeedsWindowTurns, and the guest is called first.
	/// </summary>
	internal const int HeadInterludes = 2;

	/// <summary>The head bound without its walk: the move-up wait, two interludes and one evaluation for the update order.</summary>
	internal static long HeadWaitTurns => MoveUpWaitTurns + HeadInterludes * (GuestSimulation.InterludeTurns + 1) + 1;

	/// <summary>
	/// Rule (a)'s limit for one head: <paramref name="queueBound"/> (<see cref="HeadNotReadyBound"/>), or the head's own
	/// remaining walk (<see cref="HeadRemainingWalkTurns"/>) plus <see cref="HeadWaitTurns"/> where that is smaller
	/// (review WV-3: a head standing at position 0 in an interlude gets 0 + 26 turns, not the queue's whole walk).
	/// </summary>
	internal static long HeadNotReadyLimit( Guest? head, IRideVisitorBridge ride, long queueBound )
	{
		var walk = head == null ? -1 : HeadRemainingWalkTurns( head, ride );
		return walk < 0 ? queueBound : Math.Min( queueBound, walk + HeadWaitTurns );
	}

	/// <summary>The traced boarding latency of one BOUNCE ride (docs/reverse/BOARD-plan.md §7.1, §8), in park turns.</summary>
	/// <param name="WalkScope">Empty when WALK-plan's straight-front (W3) and stand-position (W4) conditions hold; otherwise
	/// why the walk terms, and so every wait of this ride, are not judged.</param>
	internal sealed record BoardingModel( string Script, int LoopWaitMilliseconds, long PeriodTurns, long HostTurns, string WalkScope )
	{
		/// <summary>H = H₀ + w + w₂: one boarding's latency once the previous LETMEON was consumed and a slot is free.</summary>
		public long LatencyTurns => HostTurns + StandWalkTurns + MoveUpWalkTurns;
	}

	/// <summary>
	/// The BOUNCE scripts whose open-ride loop BOARD-R traced (BOARD-plan §4, lane <c>tools/ppc-analysis/lanes/board</c>):
	/// SHA-256 of the RSE file, the code word of the loop WAIT that ends the release slice, and the code word of the
	/// BOUNCE in the admission slice. Only these scripts get the wait bound; the WAIT operand is read from the script.
	/// </summary>
	internal static readonly IReadOnlyList<(string Sha256, string Name, int WaitWord, int BounceWord)> TracedBounceScripts = new[]
	{
		("7f32699cb6c511d8a9d80dd3250ae89f0b3991b2645fdfccf5cc13a68f677a35", "jungle Bouncy.RSE", 46, 93),
		("4fd934f796328f25e3702b87bfd67dc2b9a42946cf39d6a025f6fe98e7a8ad87", "space Bouncy.RSE", 46, 93),
		("c0150775537d5076759a4b61f1cd067e91256712a8d15f20e6138d34505865e9", "fantasy Jelly.RSE", 37, 61),
		("cf82eaae1ba3535b96fe79b04ec721f755c087c88f92708caf7491c897deae76", "hallow Brainb.RSE", 43, 100),
	};

	/// <summary>Largest DUR (s) whose slot hold is a property of the traced rules rather than of the exact 248 ms grid (BOARD-plan §5).</summary>
	internal const int MaximumBoundedDuration = 30;

	/// <summary>
	/// Loop period P: admission slice A (ends at CRIT_UNLOCK, which clears the budget at 0xaf5e8), release slice B (sets
	/// the WAIT and yields, 0xb06f8..0xb0708), then the slices until the WAIT resumes at now ≥ set + trunc(operand / speed)
	/// (0xb067c), one per park turn. The caller passes the WAIT already divided by the script speed (0.5 + bias / 100):
	/// at bias 50 Bouncy.RSE gives 1 + ⌈500 / 248⌉ = 4; jungle Bouncy's InitSpeed 60 gives 1 + ⌈454 / 248⌉ = 3.
	/// </summary>
	internal static long BounceLoopPeriodTurns( int waitMilliseconds ) => 1 + (waitMilliseconds + ParkCalendar.TurnMilliseconds - 1) / ParkCalendar.TurnMilliseconds;

	/// <summary>
	/// Slot hold R: turns from the BOUNCE (slice A at turn 0) to the UNBOUNCE that frees its slot. BOUNCE stores the
	/// deadline start + 1000 × DUR (0xadfc8); UNBOUNCE polls in the release slices P·j + 1 turns later and frees a slot once
	/// deadline &lt; now and (now − start) mod 1000 &lt; 200 (0xae088..0xae0bc), on the nominal 248 ms grid (BOARD-plan A1).
	/// This is the rule itself, not its closed form: 4·DUR + 1 for 8 ≤ DUR ≤ 30, but 29 turns for DUR ≤ 7.
	/// </summary>
	internal static long BounceHoldTurns( int durationSeconds, long periodTurns )
	{
		for ( long poll = 0; poll < 100_000; poll++ )
		{
			var turns = periodTurns * poll + 1;
			var elapsed = turns * ParkCalendar.TurnMilliseconds;
			if ( elapsed > 1000L * durationSeconds && elapsed % 1000 / 200 == 0 )
				return turns;
		}
		throw new InvalidOperationException( $"no UNBOUNCE release for DUR {durationSeconds} s" );
	}

	/// <summary>
	/// w: updates of the original's state-13 walk from slot 0 to the stand point, from its traced steering
	/// (docs/reverse/WALK-plan.md §9, <c>tools/ppc-analysis/lanes/walk/walk_evidence.py</c> on the SHA-pinned binary):
	/// the speed floor at 0xffe38 (max speed ≥ 655 = 0.01 cell per update, base speed +192 ∈ 60..140, so s ≥ 0.59 after
	/// 15 updates) and arrival within 0.32 cell (0xfe628). Enumerated with 0 failures: 12 with slot 0 at the entrance edge,
	/// 20 with the slot axis reversed; the link compass is not pinned, so the larger. Holds under W2 (the guest is at least
	/// 15 updates old), W3 (a straight front segment) and W4 (EntryCellStandPos (0.5, 0.5)).
	/// </summary>
	internal const long StandWalkTurns = 20;

	/// <summary>w₂: updates of the original's state-12 move-up from slots 1–3 to slot 0 (WALK-plan §9, same conditions).</summary>
	internal const long MoveUpWalkTurns = 15;

	/// <summary>Updates a guest must have lived for the speed floor s ≥ 0.59 behind w and w₂ (WALK-plan W2).</summary>
	internal const long MinimumWalkAgeTurns = 15;

	/// <summary>
	/// C: updates per queue cell crossed in one queue walk. One walk along the queue that crosses k cells and ends at a
	/// slot takes at most k × C + w₂ updates (<see cref="QueueWalkTurns"/>). C = 27 is the bounded enumeration of tp.exe's
	/// own walk (whole-walk destination 0x510100, search 0x511420, edge test 0x4D8750, the steering of
	/// <see cref="GuestNavigation"/>) in <c>WalkTests.QueueWalkTermFullEnumerationOriginal</c> (OPENTPW_WALK_DERIVE=1; the
	/// reduced grid runs in every test pass as <c>WalkTests.QueueWalkTermHoldsOverTheOriginalWalk</c>): routes of 0–4
	/// crossings with every turn sequence, long straight and cornered routes up to 25 crossings, starts anywhere in the
	/// start cell, stale velocities up to the max speed, slots on both depth axes, separation off and at its maximum
	/// against the walk, s from 0.59 to 2.0. The worst walks are the short cornered ones that the original reroutes around
	/// a neighbour's cell (3 crossings 93 updates, 4 crossings 118: about 26 per cell); long straight queues stay near 14
	/// per cell (25 crossings 358). Under the first enumeration over OpenTPW's former per-cell search C was 9.
	/// Holds under W2 (the guest is at least 15 updates old).
	/// </summary>
	// [APPROX:GATE-006] C = 27 bounds tp.exe's walk as OpenTPW ports it, an enumeration and not a proof: avoid_walls' corner branches are ported (WALK-002 is retired) and the separation's neighbours come in the original's chain order for the guests (WALK-003: head first; the staff's place in the chain is open), but the enumeration's separation is off or at its maximum and puts the neighbours' cells as the only blockers — evidence needed: the staff's entry order and the thing position of riding and held guests (WALK-003), then C re-derived
	internal const long QueueCellWalkTurns = 27;

	/// <summary>One queue walk crossing <paramref name="cells"/> cells to a slot: k × C + w₂ updates.</summary>
	internal static long QueueWalkTurns( int cells ) => Math.Max( 0, cells ) * QueueCellWalkTurns + MoveUpWalkTurns;

	/// <summary>
	/// The join walk of a guest that joined at 0-based position <paramref name="position"/> of a queue of
	/// <paramref name="queueCells"/> cells: from the join cell over the back cell to its slot's cell (WALK-plan §11.7).
	/// </summary>
	internal static long JoinWalkTurns( int position, int queueCells )
	{
		var cells = Math.Max( 1, queueCells );
		return QueueWalkTurns( cells - GuestSimulation.QueueSlot( Math.Max( 0, position ), cells ).CellIndex );
	}

	/// <summary>
	/// An upper bound on the walking a head still does before it stands at position 0's slot, in updates (C and w₂ per
	/// walk, <see cref="QueueWalkTurns"/>), or −1 outside the queue walk states (called, or not queueing). It counts the
	/// walks themselves, not <see cref="Guest.QueueCellIndex"/>: on a folded queue a guest can step across the fold onto a
	/// cell with a smaller index while its walk still follows the long way round (review WV-12).
	/// <list type="bullet">
	/// <item>standing (state 11) at position 0: nothing;</item>
	/// <item>standing elsewhere: the move-up from its own cell i to the front cell, i × C + w₂;</item>
	/// <item>walking (state 12): the whole current walk as set up (<see cref="GuestNavigation.WalkCrossings"/>, kept by the
	/// stuck reroute), and unless it walks to position 0, one more walk to the front cell from wherever it is then: from
	/// at most the largest index among the walk's cells (the join cell counts as N).</item>
	/// </list>
	/// </summary>
	// [APPROX:GATE-006] each walk is charged k x C + w2 from its own start, the enumerated per-walk term (tp.exe's walk as ported, not a proof) — evidence needed: as for C
	internal static long HeadRemainingWalkTurns( Guest head, IRideVisitorBridge ride )
	{
		var cells = ride.QueueCells;
		if ( cells.Count == 0 )
			return -1;
		var entrance = cells[0];
		var (dx, dy) = GuestPathGrid.Directions[ride.DirectionTowardsFront( 0 )];
		entrance = (entrance.X + dx, entrance.Y + dy);
		int IndexOf( (int X, int Y) cell )
		{
			for ( var index = 0; index < cells.Count; index++ )
			{
				if ( cells[index] == cell )
					return index;
			}
			return cell == entrance ? 0 : cells.Count;
		}
		var navigation = head.Navigation;
		switch ( head.State )
		{
			case GuestState.Queueing:
				return head.QueuePosition == 0 ? 0 : QueueWalkTurns( IndexOf( navigation.Cell ) );
			case GuestState.MovingUpQueue:
				var turns = QueueWalkTurns( navigation.WalkCrossings );
				if ( head.QueuePosition == 0 )
					return turns;
				var farthest = 0;
				foreach ( var cell in navigation.Route )
					farthest = Math.Max( farthest, IndexOf( cell ) );
				foreach ( var cell in navigation.AlsoOpen )
					farthest = Math.Max( farthest, IndexOf( cell ) );
				return turns + QueueWalkTurns( farthest );
			default:
				return -1;
		}
	}

	/// <summary>
	/// The head's own walk beyond H's move-up w₂ when it becomes head: its remaining walk (<see cref="HeadRemainingWalkTurns"/>)
	/// less the w₂ that H already holds, none once called (H holds the stand walk w), and N × C + w₂ outside the queue walk
	/// states.
	/// </summary>
	internal static long HeadWalkTurns( Guest head, IRideVisitorBridge ride )
	{
		if ( head.State is GuestState.Boarding or GuestState.WaitingToBoard )
			return 0;
		var walk = HeadRemainingWalkTurns( head, ride );
		return walk < 0 ? QueueWalkTurns( Math.Max( 1, ride.QueueCells.Count ) ) : Math.Max( 0, walk - MoveUpWalkTurns );
	}

	/// <summary>
	/// Host part H₀ of the per-boarding latency (BOARD-plan §7.1): the previous guest leaves the list (1, state 14
	/// 0xef548), the new head's move-up wait at gap ≤ 2 and the move (trunc(1.2 × 2) + 1, 0xed4a4 / 0xed4c0), one
	/// interlude (InterludeTurns + 1, 0xef6d8), the ride calls the ready head (1, 0xe1404), the head notices (1, 0xed2c8)
	/// and the script's next admission slice (P).
	/// </summary>
	internal static long BoardingHostTurns( long periodTurns ) =>
		1 + ((long)(GuestSimulation.MoveDelayFactor * GuestSimulation.MoveUpWaitGap) + 1) + (GuestSimulation.InterludeTurns + 1) + 1 + 1 + periodTurns;

	/// <summary>
	/// BOARD-plan §7.2: a guest that joined at 0-based position p boards within (p + 1)·H + (⌊p / CAP⌋ + 1)·R + 1 turns
	/// once it stands at its slot; its join walk (<paramref name="joinWalkTurns"/>, <see cref="JoinWalkTurns"/>) comes on top.
	/// </summary>
	internal static long BoardingWaitBound( int position, int capacity, long latencyTurns, long holdTurns, long joinWalkTurns = 0 ) =>
		joinWalkTurns + (position + 1L) * latencyTurns + (position / Math.Max( 1, capacity ) + 1L) * holdTurns + 1;

	/// <summary>
	/// The boarding model of a placed object, or null with the reason it is outside the traced class: its script must be
	/// one of <see cref="TracedBounceScripts"/> with the loop WAIT and BOUNCE where BOARD-R found them, and the ride must
	/// run continuously (the RUNNING gate is bypassed). DUR is checked per wait.
	/// </summary>
	private BoardingModel? TraceBoarding( Placed item, out string reason )
	{
		var visitors = item.Runtime.Visitors;
		var file = item.Runtime.Script?.Script;
		if ( file == null || item.Entry.ScriptPath == null )
		{
			reason = "no script";
			return null;
		}
		string sha;
		using ( var stream = item.Entry.FileSystem.OpenRead( item.Entry.ScriptPath ) )
			sha = Convert.ToHexString( SHA256.HashData( stream ) ).ToLowerInvariant();
		var pin = TracedBounceScripts.FirstOrDefault( script => script.Sha256 == sha );
		if ( pin.Sha256 == null )
		{
			reason = $"script {Path.GetFileName( item.Entry.ScriptPath )} (SHA-256 {sha[..8]}) is not a BOUNCE loop traced by BOARD-R";
			return null;
		}
		if ( !visitors.HasQueue || visitors.QueueSizeInCells < 1 )
		{
			reason = $"{pin.Name} without a queue";
			return null;
		}
		if ( !visitors.Parameters.RunsContinuously )
		{
			reason = "RunsContinuously 0: the RUNNING gate batches admission (BOARD-plan §9)";
			return null;
		}
		RideScriptInstruction? At( int word ) => file.GetInstructionIndexAtWord( word ) is var index and >= 0 ? file.Instructions[index] : null;
		if ( At( pin.WaitWord ) is not { Opcode: (ushort)Opcode.WAIT, Operands: [{ Kind: RideScriptOperandKind.Literal } wait, ..] }
			|| At( pin.BounceWord ) is not { Opcode: (ushort)Opcode.BOUNCE } )
		{
			reason = $"{pin.Name}: no WAIT literal at word {pin.WaitWord} or BOUNCE at word {pin.BounceWord}";
			return null;
		}
		var period = BounceLoopPeriodTurns( RideAnimationTiming.TriggerDelay( wait.Value, item.Runtime.Script!.ScriptSpeed ) );
		// WALK-plan W3: the entrance cell E, the front cell Q and the next queue cell B lie in a line (E is Q's neighbour
		// towards the ride). W4: the ride's EntryCellStandPos is (0.5, 0.5).
		var front = visitors.QueueCells[0];
		var (toRideX, toRideY) = GuestPathGrid.Directions[visitors.DirectionTowardsFront( 0 )];
		var behind = visitors.QueueCells.Count > 1 ? visitors.QueueCells[1] : visitors.JoinCell ?? front;
		var straight = (front.X - behind.X, front.Y - behind.Y) == (toRideX, toRideY);
		var settings = item.Entry.Settings;
		var standX = settings.Has( "UsageInfo.EntryCellStandPosX" ) ? settings.GetFloat( "UsageInfo.EntryCellStandPosX" ) : float.NaN;
		var standY = settings.Has( "UsageInfo.EntryCellStandPosY" ) ? settings.GetFloat( "UsageInfo.EntryCellStandPosY" ) : float.NaN;
		var scope = !straight ? $"front segment not straight (B {behind} -> Q {front} -> ride {(toRideX, toRideY)}; WALK-plan W3)"
			: standX != 0.5f || standY != 0.5f ? FormattableString.Invariant( $"EntryCellStandPos ({standX}, {standY}) is not (0.5, 0.5) (WALK-plan W4)" ) : "";
		reason = "";
		return new BoardingModel( pin.Name, wait.Value, period, BoardingHostTurns( period ), scope );
	}


	/// <summary>
	/// The wait bound applied at every admission evaluation, not only to completed waits, so a guest that later gives up
	/// is judged too: (1) every queued guest's age against W(p) for its join position p (BOARD-plan §8, assertion 3, at
	/// each turn); (2) the head against W(0) = H + R + 1 from the turn it became head. (2) is §7.2's induction with J the
	/// turn the guest became head and n = 1: the riders on board then free their slots within R, and the boarding takes
	/// at most H once a slot is free. Turns overlapping an excluded turn, and DUR above 30, are not judged.
	/// </summary>
	private void CheckBoardingAges( Placed item, QueueProgress state, int head, Dictionary<int, GuestTrack> tracks, long tick )
	{
		var model = state.Boarding!;
		var name = item.Entry.SettingsName;
		var now = guests.ParkTurn;
		var capacity = item.Runtime.GetVariable( RideVariables.VAR_CAPACITY );
		var duration = item.Runtime.GetVariable( RideVariables.VAR_DURATION );
		var queueCells = item.Runtime.Visitors.QueueSizeInCells;
		if ( head != state.BoardingHead )
			(state.BoardingHead, state.BoardingHeadSince, state.BoardingHeadWalk) = (head, now, guests.Find( head ) is { } headGuest ? HeadWalkTurns( headGuest, item.Runtime.Visitors ) : 0);
		// WALK-plan W2: the head (the next guest to be called, or the called one) must be at least 15 updates old.
		if ( head != 0 && (tracks.GetValueOrDefault( head ) is not { } headTrack || now - headTrack.FirstSeenTurn < MinimumWalkAgeTurns) )
			state.YoungHeadTurns.Add( now );
		if ( duration is < 0 or > MaximumBoundedDuration || model.WalkScope.Length > 0 )
			return;
		var hold = BounceHoldTurns( duration, model.PeriodTurns );
		bool Excluded( long from ) => state.ExcludedTurns.GetViewBetween( from, now ).Count > 0 || state.YoungHeadTurns.GetViewBetween( from, now ).Count > 0;
		if ( head != 0 )
		{
			var asHead = now - state.BoardingHeadSince;
			var bound = BoardingWaitBound( 0, capacity, model.LatencyTurns, hold, state.BoardingHeadWalk );
			state.MaxHeadWalk = Math.Max( state.MaxHeadWalk, state.BoardingHeadWalk );
			if ( asHead > bound && !Excluded( state.BoardingHeadSince ) && state.ReportedAges.Add( -head ) )
				state.WaitAboveBound.Add( tick, $"tick {tick}: {name} head guest {head} not boarded {asHead} turns after becoming head > bound {bound} (head walk {state.BoardingHeadWalk} + H {model.LatencyTurns} + R {hold} + 1)" );
		}
		foreach ( var id in item.Runtime.Visitors.Queue )
		{
			if ( guests.Find( id ) is not { } guest || tracks.GetValueOrDefault( id ) is not { QueueSince: >= 0, QueueJoinPosition: >= 0 } track )
				continue;
			var age = guests.QueueWaitTurns( guest );
			var bound = BoardingWaitBound( track.QueueJoinPosition, capacity, model.LatencyTurns, hold, JoinWalkTurns( track.QueueJoinPosition, queueCells ) );
			if ( age > bound && !Excluded( now - age ) && state.ReportedAges.Add( id ) )
				state.WaitAboveBound.Add( tick, $"tick {tick}: {name} guest {id} queued {age} turns from position {track.QueueJoinPosition} > bound {bound} (join walk {JoinWalkTurns( track.QueueJoinPosition, queueCells )}, H {model.LatencyTurns}, R {hold}, CAP {capacity})" );
		}
	}

	/// <summary>
	/// walk-stall (WALK-plan §12.4): the traced steering completes the walk to the stand point within w = 20 updates and
	/// the move-up from slots 1–3 to slot 0 within w₂ = 15. A guest of a traced ride in OpenTPW's <see cref="GuestState.Boarding"/>
	/// (state 13), or in <see cref="GuestState.MovingUpQueue"/> (state 12) towards position 0 from inside the front cell,
	/// for more consecutive turns fails the row; outside W2–W4 the stall is counted, not judged.
	/// </summary>
	private void CheckWalkStall( Placed item, QueueProgress state, Guest guest, GuestTrack track, long tick )
	{
		var now = guests.ParkTurn;
		var kind = guest.State == GuestState.Boarding ? 13
			: guest.State == GuestState.MovingUpQueue && guest.QueuePosition == 0 && (track.WalkKind == 12 || guest.QueueCellIndex == 0) ? 12 : 0;
		if ( kind != track.WalkKind )
		{
			(track.WalkKind, track.WalkSince, track.WalkReported) = (kind, now, false);
			if ( kind == 13 )
				state.StandWalks++;
			else if ( kind == 12 )
				state.MoveUpWalks++;
		}
		if ( kind == 0 )
			return;
		var turns = now - track.WalkSince;
		var bound = kind == 13 ? StandWalkTurns : MoveUpWalkTurns;
		if ( kind == 13 )
			state.MaxStandWalk = Math.Max( state.MaxStandWalk, turns );
		else
			state.MaxMoveUpWalk = Math.Max( state.MaxMoveUpWalk, turns );
		if ( turns <= bound || track.WalkReported )
			return;
		track.WalkReported = true;
		if ( state.Boarding!.WalkScope.Length > 0 || track.WalkSince - track.FirstSeenTurn < MinimumWalkAgeTurns )
		{
			state.StallsOutsideScope++;
			return;
		}
		var walk = kind == 13 ? "state 13 walk to the stand point" : "state 12 move-up to slot 0";
		state.WalkStall.Add( tick, $"walk-stall: tick {tick}: {item.Entry.SettingsName} guest {guest.Id} {walk} for {turns} turns > {(kind == 13 ? "w" : "w2")} {bound} (WALK-plan §9)" );
	}

	/// <summary>BOARD-plan A3: marks the turn when the ride is closed or broken, or when its CAP or DUR changes.</summary>
	private void SampleBoardingExclusions( Placed item, QueueProgress state )
	{
		if ( state.Boarding == null )
			return;
		var runtime = item.Runtime;
		var capacity = runtime.GetVariable( RideVariables.VAR_CAPACITY );
		var duration = runtime.GetVariable( RideVariables.VAR_DURATION );
		var changed = state.LastCapacity >= 0 && (capacity != state.LastCapacity || duration != state.LastDuration);
		(state.LastCapacity, state.LastDuration) = (capacity, duration);
		if ( changed || !runtime.Visitors.IsOpen || runtime.Visitors.IsBroken || runtime.GetVariable( RideVariables.VAR_BREAKSTAT ) != 0
			|| runtime.GetVariable( RideVariables.VAR_RIDECLOSED ) != 0 )
			state.ExcludedTurns.Add( guests.ParkTurn );
	}

	/// <summary>
	/// Park turns from a wear or breakdown event until the repair must be done, from ParkEconomy's staff timing: the
	/// event is raised at a day end, the next turn's park-hour update dispatches a free mechanic (a turn is 3,750
	/// park-clock seconds, so every turn runs at least one hour update), and a job takes at most
	/// <c>WorkDuration</c> park turns (a ride at zero repair; <see cref="ParkEconomy.RepairTimer"/>) plus the update that completes it.
	/// With one job per ride ahead of it, the window is 1 + rides × (the longest WorkDuration of the hired mechanics' grades + 1).
	/// </summary>
	private (long Turns, string Derivation) RepairWindow()
	{
		var role = economy.Settings[StaffType.Mechanic];
		var mechanics = economy.Staff.Members.Where( member => member.Type == StaffType.Mechanic ).ToList();
		var work = mechanics.Count == 0 ? 0 : mechanics.Max( member => Math.Max( 0, role.WorkDuration[member.Grade] ) );
		var rides = economyLinks.Count( link => economy.TryGetObject( link.Instance, out var state ) && state.Kind == ParkObjectKind.Ride );
		var turns = 1 + Math.Max( 1, rides ) * (work + 1L);
		return (turns, $"1 turn to dispatch + {Math.Max( 1, rides )} ride job(s) x (WorkDuration {work} turns at zero repair + 1 completing update) = {turns} turns");
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
	/// The queue row (QUEUE-plan §9). It fails on a derived progress violation: (a) the same head did not stand at
	/// position 0 (whether or not the gates held) for longer than <see cref="HeadNotReadyBound"/>; (b) a queue the
	/// admission handshake cannot move (VAR_LETMEON non-zero with nobody called on two consecutive evaluations: §9a's
	/// two-update rule); (c) for a BOUNCE ride of the traced class (BOARD-plan §8), a completed wait, a queued guest's age
	/// at any evaluation, or the head's time since becoming head, above (p + 1)·H + (⌊p / CAP⌋ + 1)·R + 1 turns for its
	/// join position p (p = 0 for the head), with w and w₂ from WALK-plan §9; (d) walk-stall (WALK-plan §12.4). Waits
	/// that overlap a closed or broken turn, a CAP/DUR change or a head younger than 15 turns, and every wait of a ride
	/// outside WALK-plan W3/W4, are counted, not judged. It passes when no rule is violated and at least one wait was
	/// judged; otherwise it stays unresolved. Objects outside the class are judged by (a) and (b) only.
	/// </summary>
	private void AddQueueRow( QueueProgress[] progress, long stillQueuedTurns, List<(int AttractionId, long Turns, int Position)> queuedAtEnd, long ticks,
		long leftWithoutBoarding, long vanished )
	{
		var turnSeconds = ParkCalendar.TurnMilliseconds / 1000.0;
		var evidence = new JsonObject
		{
			["leftQueueWithoutBoarding"] = leftWithoutBoarding,
			["vanishedWhileQueued"] = vanished,
			["stillQueuedAtEndMaxSeconds"] = Math.Round( stillQueuedTurns * turnSeconds, 3 ),
			["rule"] = "FAIL: same head (judged from 15 turns old) not at position 0 (gates held or not) beyond its derived bound, the smaller of the queue bound and its own remaining walk + 26; VAR_LETMEON non-zero with nobody called on 2 consecutive evaluations; or, for a traced BOUNCE ride (DUR <= 30, RunsContinuously), a wait or a still-queued age above J(p) + (p + 1) H + (floor(p / CAP) + 1) R + 1 turns. PASS: none, and at least one wait judged; else UNRESOLVED"
		};
		long judged = 0;
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
				["headNotReadyBound"] = HeadNotReadyBound( visitors.QueueSizeInCells ).Derivation,
				["headNotReadyYoungHeadsDeferred"] = state.YoungHeadsDeferred,
				["headNotReadyHeadsOnOwnWalk"] = state.HeadsOnOwnWalk,
				["headNotReadyClosestToLimitTurns"] = state.ClosestToHeadLimit == long.MinValue ? null : state.ClosestToHeadLimit,
				["headNotReadyGatesHeldMaxAcrossHeads"] = visitors.MaximumHeadNotReadyStreak,
				["headNotReadyViolations"] = state.HeadNotReady.Count,
				["blockedMaxEvaluations"] = state.MaxBlockedStreak,
				["blockedViolations"] = state.Blocked.Count,
				["calledAgeMaxTurns"] = visitors.MaximumCalledAgeTurns,
				["calledAgeAtEndTurns"] = visitors.CalledAgeTurns
			};
			entry["waitBound"] = state.Boarding == null
				? new JsonObject { ["judged"] = false, ["reason"] = state.BoardingReason, ["rule"] = "progress rules (a) and (b) only" }
				: JudgeWaits( item, state, queuedAtEnd.Where( guest => guest.AttractionId == visitors.AttractionId ).ToList(), ticks, turnSeconds, ref judged );
			evidence[item.Role] = entry;
		}
		var violations = progress.SelectMany( state => new[] { state.HeadNotReady, state.Blocked, state.WaitAboveBound, state.WalkStall } ).Where( violation => violation.Count > 0 ).OrderBy( violation => violation.First ).ToList();
		evidence["violations"] = violations.Sum( violation => violation.Count );
		evidence["firstViolation"] = violations.FirstOrDefault()?.FirstDetail;
		evidence["judgedWaits"] = judged;
		var verdict = violations.Count > 0 ? M3GateVerdict.Fail : judged > 0 ? M3GateVerdict.Pass : M3GateVerdict.Unresolved;
		AddRow( "queues.no-stuck-queue", "rides", "Queues make progress: no head stuck beyond its derived bound, no blocked admission, no wait beyond the traced boarding bound",
			verdict, violations.FirstOrDefault()?.First, evidence );
	}

	/// <summary>
	/// BOARD-plan §8 for one traced BOUNCE ride: every completed wait and every still-queued age, unless it overlaps an
	/// excluded turn or its DUR is outside the bounded range, must satisfy W ≤ (p + 1)·H + (⌊p / CAP⌋ + 1)·R + 1. A guest
	/// whose join position was not seen is judged at p = 0, the smallest bound. Reports τ_max = CAP·H + R + 1 − (DUR + 1 s) / T
	/// (QUEUE-plan §9b form) and W_max = W(Qmax − 1), with the τ the run implies as evidence.
	/// </summary>
	private JsonObject JudgeWaits( Placed item, QueueProgress state, List<(int AttractionId, long Turns, int Position)> queuedAtEnd, long ticks, double turnSeconds, ref long judged )
	{
		var model = state.Boarding!;
		var name = item.Entry.SettingsName;
		var latency = model.LatencyTurns;
		var queueCells = item.Runtime.Visitors.QueueSizeInCells;
		long excluded = 0, youngHead = 0, outsideWalkScope = 0, outsideDuration = 0, unknownPosition = 0, judgedHere = 0, worstMargin = long.MinValue;
		void Judge( long tick, long endTurn, long turns, int position, int capacity, int duration, string what )
		{
			if ( model.WalkScope.Length > 0 )
			{
				outsideWalkScope++;
				return;
			}
			if ( state.ExcludedTurns.GetViewBetween( endTurn - turns, endTurn ).Count > 0 )
			{
				excluded++;
				return;
			}
			if ( state.YoungHeadTurns.GetViewBetween( endTurn - turns, endTurn ).Count > 0 )
			{
				youngHead++;
				return;
			}
			if ( duration > MaximumBoundedDuration || duration < 0 )
			{
				outsideDuration++;
				return;
			}
			if ( position < 0 )
				unknownPosition++;
			var p = Math.Max( 0, position );
			var join = JoinWalkTurns( p, queueCells );
			var bound = BoardingWaitBound( p, capacity, latency, BounceHoldTurns( duration, model.PeriodTurns ), join );
			judgedHere++;
			worstMargin = Math.Max( worstMargin, turns - bound );
			if ( turns > bound )
				state.WaitAboveBound.Add( tick, $"tick {tick}: {name} {what} {turns} turns from position {p} > bound {bound} (join walk {join}, H {latency}, R {BounceHoldTurns( duration, model.PeriodTurns )}, CAP {capacity})" );
		}
		foreach ( var wait in state.BoardingWaits )
			Judge( wait.Tick, wait.EndTurn, wait.Turns, wait.Position, wait.Capacity, wait.Duration, "wait" );
		var runtime = item.Runtime;
		var capacityNow = runtime.GetVariable( RideVariables.VAR_CAPACITY );
		var durationNow = runtime.GetVariable( RideVariables.VAR_DURATION );
		var stillQueuedJudgedFrom = judgedHere;
		foreach ( var guest in queuedAtEnd )
			Judge( ticks, guests.ParkTurn, guest.Turns, guest.Position, capacityNow, durationNow, "still queued at the end after" );
		judged += judgedHere;

		var hold = durationNow is >= 0 and <= MaximumBoundedDuration ? BounceHoldTurns( durationNow, model.PeriodTurns ) : -1;
		var capacity = Math.Max( 1, capacityNow );
		var cycleTurns = (durationNow + 1) * 1000.0 / ParkCalendar.TurnMilliseconds;
		var tauMaxTurns = capacity * latency + hold + 1 - cycleTurns;
		var qmax = runtime.Visitors.MaximumQueueLength;
		var wMax = hold < 0 ? -1 : BoardingWaitBound( Math.Max( 0, qmax - 1 ), capacity, latency, hold, JoinWalkTurns( Math.Max( 0, qmax - 1 ), queueCells ) );
		var cycle = durationNow + 1.0;
		var implied = state.Waits.Where( wait => wait.Position >= 0 ).Select( wait => (Tau: wait.Turns * turnSeconds / (wait.Position / capacity + 1) - cycle, wait.Turns, wait.Position) ).ToList();
		var measured = implied.Count == 0 ? double.NaN : implied.Max( entry => entry.Tau );
		var worst = implied.Count == 0 ? default : implied.First( entry => entry.Tau == measured );
		return new JsonObject
		{
			["judged"] = true,
			["script"] = model.Script,
			["capacity"] = capacityNow,
			["durationSeconds"] = durationNow,
			["qmax"] = qmax,
			["loopPeriodTurns"] = model.PeriodTurns,
			["hostLatencyTurns"] = model.HostTurns,
			["standWalkTurns"] = StandWalkTurns,
			["moveUpWalkTurns"] = MoveUpWalkTurns,
			["queueCellWalkTurns"] = QueueCellWalkTurns,
			["joinWalkTurnsAtHeadAndQmax"] = FormattableString.Invariant( $"{JoinWalkTurns( 0, queueCells )} / {JoinWalkTurns( Math.Max( 0, qmax - 1 ), queueCells )}" ),
			["walkScope"] = model.WalkScope.Length == 0 ? "W2-W4 hold (straight front, EntryCellStandPos (0.5, 0.5)); W2 checked per head" : model.WalkScope,
			["latencyTurns"] = latency,
			["holdTurns"] = hold,
			["tauMaxTurns"] = Math.Round( tauMaxTurns, 3 ),
			["tauMaxSeconds"] = Math.Round( tauMaxTurns * turnSeconds, 3 ),
			["wMaxTurns"] = wMax,
			["wMaxSeconds"] = Math.Round( wMax * turnSeconds, 3 ),
			["judgedWaits"] = stillQueuedJudgedFrom,
			["judgedStillQueued"] = judgedHere - stillQueuedJudgedFrom,
			["excludedClosedBrokenOrChanged"] = excluded,
			["excludedTurns"] = state.ExcludedTurns.Count,
			["notJudgedDurationAbove30"] = outsideDuration,
			["notJudgedYoungHead"] = youngHead,
			["notJudgedWalkScope"] = outsideWalkScope,
			["youngHeadTurns"] = state.YoungHeadTurns.Count,
			["walkStalls"] = state.WalkStall.Count,
			["walkStallsOutsideScope"] = state.StallsOutsideScope,
			["standWalks"] = state.StandWalks,
			["standWalkMaxTurns"] = state.MaxStandWalk,
			["moveUpWalksToSlotZero"] = state.MoveUpWalks,
			["moveUpWalkMaxTurns"] = state.MaxMoveUpWalk,
			["judgedAtPositionZeroWithoutJoinPosition"] = unknownPosition,
			["waitsAboveBound"] = state.WaitAboveBound.Count,
			["headToBoardingMaxTurns"] = state.MaxHeadToBoarding,
			["headToBoardingBoundTurns"] = hold < 0 ? -1 : BoardingWaitBound( 0, capacity, latency, hold ),
			["headWalkMaxTurns"] = state.MaxHeadWalk,
			["closestToBoundTurns"] = judgedHere == 0 ? null : worstMargin,
			["tauMeasuredSeconds"] = double.IsNaN( measured ) ? null : Math.Round( measured, 3 ),
			["tauMeasuredFrom"] = implied.Count == 0 ? "no boarding with a recorded join position" : FormattableString.Invariant( $"wait {Math.Round( worst.Turns * turnSeconds, 3 )} s from position {worst.Position}" ),
			["boardingsByJoinPosition"] = implied.Count == 0 ? "none" : FormattableString.Invariant( $"p 0..{implied.Max( entry => entry.Position )}, mean p {Math.Round( implied.Average( entry => entry.Position ), 1 )}" ),
			["derivation"] = FormattableString.Invariant( $"W(p) <= J(p) + (p + 1) H + (floor(p / CAP) + 1) R + 1 turns, J(p) = (N - cell of p) x C {QueueCellWalkTurns} + w2 {MoveUpWalkTurns} (the join walk; WALK-I enumeration); the head: its own walk beyond H (0 when standing in the front cell, else j x C + w2) + H + R + 1; H = H0 {model.HostTurns} (1 removal + 3 move-up + 11 interlude + 1 call + 1 notice + P {model.PeriodTurns} = 1 + ceil(WAIT {model.LoopWaitMilliseconds} / {ParkCalendar.TurnMilliseconds})) + w {StandWalkTurns} + w2 {MoveUpWalkTurns} (WALK-plan section 9: traced steering, walk_evidence.py, under W2-W4); R = UNBOUNCE release for DUR {durationNow}; tau_max = CAP H + R + 1 - (DUR + 1 s) / T; W_max = W(Qmax - 1)" )
		};
	}

	private void AddReachabilityRow( Violation violation, long confused, long ejected )
	{
		// The join cell is where guests walk to join the queue (GuestSimulation.UpdateGoingToRide), so it must be reachable too.
		var unreachableTargets = placed.Where( item => grid.Distance( entranceCell.X, entranceCell.Y, item.Runtime.Visitors.EntranceCell.X, item.Runtime.Visitors.EntranceCell.Y ) < 0
			|| grid.Distance( item.Runtime.Visitors.ExitCell.X, item.Runtime.Visitors.ExitCell.Y, entranceCell.X, entranceCell.Y ) < 0
			|| (item.Runtime.Visitors.JoinCell is { } join && grid.Distance( entranceCell.X, entranceCell.Y, join.X, join.Y ) < 0) ).Select( item => item.Entry.SettingsName ).ToList();
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
			problems.Add( $"litter left unclaimed beside a free handyman at {litterNotCleaned.Count} turn(s)" );
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
			["litterPeakItems"] = litterPeak,
			["litterDroppedItems"] = litterDropped,
			["litterCleanedItems"] = litterCleaned,
			["litterWindow"] = "two park turns: a free handyman claims unclaimed litter at the next turn's dispatch (ParkEconomy.DispatchHandymen) and sweeps it for WorkDuration[grade] turns",
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
		Add( (long)economy.World.State );
		Add( economy.Litter.Count );
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

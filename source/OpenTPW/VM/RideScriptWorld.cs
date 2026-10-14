namespace OpenTPW;

/// <summary>
/// The set of live scripts that can see each other through FINDSCRIPTRAND, GETREMOTEVAR and SETREMOTEVAR.
/// Script IDs are positive; 0 means "no script". One world belongs to one park: it also allocates the park's
/// attraction ids and seeds scripts that are created without an explicit seed, so its
/// allocations and random streams do not depend on what else ran earlier in the process. Host civil-time queries
/// are external inputs and require the same supplied samples for reproducible continuation.
/// </summary>
public sealed class RideScriptWorld
{
	private readonly Dictionary<int, RideVM> scripts = new();
	private readonly Func<DateTime>? civilTime;
	private int nextId = 1;
	private int nextAttractionId = 1;

	/// <param name="seed">Start state of the seed stream for scripts without an explicit seed (<see cref="WorldSeed.ScriptStream"/>).</param>
	/// <summary>
	/// The civil time of headless runs whose hash must not depend on when or where they run (the M4 gate, the browser digest):
	/// noon on 1 January 2000, the park calendar's epoch.
	/// </summary>
	public static readonly DateTime FixedCivilTime = new( 2000, 1, 1, 12, 0, 0 );

	/// <summary>A clock that always reads <see cref="FixedCivilTime"/>.</summary>
	public static Func<DateTime> FixedClock { get; } = () => FixedCivilTime;

	public RideScriptWorld( ulong seed = 0 ) : this( seed, null )
	{
	}

	/// <param name="seed">The world's script and coaster seed.</param>
	/// <param name="clock">Host-local civil time for each executed query; null selects the system host clock. Unavailable on TPI worlds.</param>
	public RideScriptWorld( ulong seed, Func<DateTime>? clock )
	{
		RandomState = seed;
		Coasters = new CoasterWorld( seed );
		Bumpers = new BumpWorld();
		civilTime = GameEditions.Current == GameEdition.ThemeParkWorld ? clock ?? (() => DateTime.Now) : null;
	}

	/// <summary>Samples external host civil time; it is never reconstructed from scheduler or park turns.</summary>
	// [EXT:vm-host-time] the original reads the C runtime's local time on every execution (tp.exe 0x67D3F0/0x67D8E0), so YEAR to SEC are external inputs: managed host-local time here, neither saved nor hashed, while the variable writes and flags that follow are VM state. A replay needs the same samples (an OpenTPW replay design point; the tests and headless runs inject a fixed clock)
	internal bool TryReadCivilTime( out DateTime value )
	{
		value = civilTime == null ? default : civilTime();
		return civilTime != null;
	}

	public IReadOnlyCollection<RideVM> Scripts => scripts.Values;

	/// <summary>The park's coaster controllers and their shared boarding generator.</summary>
	public CoasterWorld Coasters { get; }

	/// <summary>The park's kart and water ride controllers (the bump-ride module) and their cosmetic generator.</summary>
	public BumpWorld Bumpers { get; }

	/// <summary>A draw of the world generator for the bump rides' aiming jitter (the original's 0x516330), from the park's seeded stream.</summary>
	internal uint NextWorldDraw() => (uint)NextScriptSeed();

	/// <summary>SplitMix64 state of the seed stream (saved with the park; see <see cref="WorldRandomState"/>).</summary>
	public ulong RandomState { get; private set; }

	/// <summary>The next attraction id of this park (guests and the economy key attractions by it).</summary>
	public int NextAttractionId => nextAttractionId;

	internal int AllocateAttractionId() => nextAttractionId++;

	/// <summary>
	/// The shared scheduler clock in milliseconds: the latest time any of this park's scripts has been advanced to.
	/// Every script reads it (GETTIME, WAITANIM(_CH) deadlines); a script created later starts at its current value.
	/// </summary>
	public double ClockMilliseconds { get; private set; }

	internal void ObserveClock( double milliseconds ) => ClockMilliseconds = Math.Max( ClockMilliseconds, milliseconds );

	internal void RestoreRandomState( ulong state ) => RandomState = state;

	/// <summary>Seed for a script created without one.</summary>
	// [BIN:STP-PPC:0x100B07E4 RSE RAND] RAND draws from the world LCG (0x10105328 on the world at TOC −0x7580): (|s| >> 1) mod (bound + 1); FINDSCRIPTRAND (0x100B1B8C) picks match (|s| >> 1) mod count, drawing only when there is a match
	// [APPROX:DET-013] scripts without an explicit seed get a per-VM generator of the .NET-seed-compatible ScriptRandom class (draws exactly like a seeded System.Random; ScriptRandomTests proves it), seeded from the park's SplitMix64 stream, instead of the traced world-LCG draws — evidence needed: none; implementation waits for the DET-I2 port of the shared world generator (docs/reverse/DET-plan.md §4.3)
	internal int NextScriptSeed()
	{
		var z = RandomState += 0x9E3779B97F4A7C15UL;
		z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
		z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
		return (int)(z ^ (z >> 31));
	}

	internal int Register( RideVM vm )
	{
		var id = nextId++;
		scripts.Add( id, vm );
		return id;
	}

	internal void Unregister( RideVM vm ) => scripts.Remove( vm.ScriptId );

	/// <summary>Registers <paramref name="vm"/> under a restored id (the snapshot restore rebinds constructor-temporary ids with it).</summary>
	internal void RegisterWithId( RideVM vm, int id )
	{
		scripts.Add( id, vm );
		vm.AdoptScriptId( id );
	}

	/// <summary>Drops the registration under <paramref name="id"/> without touching the VM (restore bookkeeping of constructor temporaries).</summary>
	internal void UnregisterById( int id ) => scripts.Remove( id );

	/// <summary>Moves the attraction-id frontier past <paramref name="id"/> without allocating (restore of a saved attraction id).</summary>
	internal void AdoptAttractionId( int id ) => nextAttractionId = Math.Max( nextAttractionId, id + 1 );

	/// <summary>Restores the allocator frontiers (holes below them included), the shared clock and the seed-stream states.</summary>
	internal void RestoreCounters( int nextScriptId, int nextAttractionIdValue, double clockMilliseconds, ulong randomState, uint coasterRandomState )
	{
		nextId = nextScriptId;
		nextAttractionId = nextAttractionIdValue;
		ClockMilliseconds = clockMilliseconds;
		RandomState = randomState;
		Coasters.Random.Restore( coasterRandomState );
	}

	public RideVM? Find( int scriptId ) => scripts.TryGetValue( scriptId, out var vm ) ? vm : null;

	/// <summary>Picks a random live script whose NAME equals <paramref name="name"/>; returns its ID or 0.</summary>
	internal int FindRandom( string name, ScriptRandom random )
	{
		var matches = scripts.Values.Where( x => x.State != RideVMState.Faulted && string.Equals( x.ScriptName, name, StringComparison.Ordinal ) ).OrderBy( x => x.ScriptId ).ToArray();
		return matches.Length == 0 ? 0 : matches[random.Next( matches.Length )].ScriptId;
	}

	// ---- Park-resume snapshot ----

	/// <summary>
	/// Explicit state of this world: the seed streams (script and coaster), the shared clock, the allocator
	/// frontiers and every live script with its graph. The coaster controllers themselves (tracks, trains, riders)
	/// are the envelope's coaster part (<see cref="CoasterSave"/>); this carries only their shared generator's
	/// state, so it captures a world with or without coasters alike.
	/// </summary>
	public RideScriptWorldSnapshot CaptureSnapshot()
	{
		return new RideScriptWorldSnapshot
		{
			RandomState = RandomState,
			CoasterRandomState = Coasters.Random.State,
			ClockMilliseconds = ClockMilliseconds,
			NextScriptId = nextId,
			NextAttractionId = nextAttractionId,
			Scripts = scripts.Values.OrderBy( vm => vm.ScriptId ).Select( vm => vm.CaptureSnapshot() ).ToArray()
		};
	}

	/// <summary>
	/// Restores a captured world into this fresh world. <paramref name="rootScripts"/> maps each snapshot root
	/// script id to its freshly constructed candidate VM — an <see cref="OriginalObjectRuntime"/> script built by
	/// <see cref="OriginalObjectRuntime.CreateForRestore"/>, or any VM the caller constructed against this world
	/// with the saved seed and source name. Caller order: restore the runtimes first
	/// (<see cref="OriginalObjectRuntime.RestoreSnapshot"/> unregisters stopped scripts), then this.
	///
	/// Pass 0 resolves and verifies every script identity (hash, seed, source name, variable count) through the
	/// resolver its parent hands down — a spawned child inherits its parent's resolver, so the chain closes for
	/// any nesting depth — before anything changes. Pass 1 constructs the child VMs top-down (parents before
	/// children; id order is not assumed to be topology) with their saved seeds (the world's seed stream is never
	/// drawn), releases the constructor-temporary registrations, applies the saved state and rebinds the saved
	/// ids. Pass 2 links the child and sound-child references. Pass 3 restores the world counters. Any violation
	/// throws before or during a pass and aborts the restore — discard this candidate world and its runtimes
	/// then; never resume into a live park.
	/// </summary>
	public void RestoreSnapshot( RideScriptWorldSnapshot snapshot, IReadOnlyDictionary<int, RideVM> rootScripts )
	{
		ArgumentNullException.ThrowIfNull( snapshot );
		ArgumentNullException.ThrowIfNull( rootScripts );
		snapshot.ValidateSnapshot();
		// The coaster controllers the candidate's placed stations created stay live: their state is restored onto
		// them afterwards by the envelope's coaster part, whose shared generator this restore sets below.

		var byId = snapshot.Scripts.ToDictionary( script => script.ScriptId );
		var rootIds = snapshot.Scripts.Where( script => script.ParentScriptId == 0 ).Select( script => script.ScriptId ).ToHashSet();
		var missing = rootIds.Where( id => !rootScripts.ContainsKey( id ) ).OrderBy( id => id ).ToArray();
		if ( missing.Length > 0 )
			throw new InvalidDataException( $"no candidate root script for snapshot id(s) {string.Join( ", ", missing )}; construct every root with its saved seed against this world first." );
		var unknown = rootScripts.Keys.Where( id => !rootIds.Contains( id ) ).OrderBy( id => id ).ToArray();
		if ( unknown.Length > 0 )
			throw new InvalidDataException( $"candidate root script id(s) {string.Join( ", ", unknown )} are not snapshot roots." );

		// Pass 0 — verify the roots, then resolve every child file through the resolver its parent hands
		// down (a spawned child inherits its parent's resolver, so each resolved child can resolve its own
		// children: the work-list closes for any nesting depth of child and sound-child chains).
		var nodes = new Dictionary<int, RideVM>();
		var files = new Dictionary<int, RideScriptFile>();
		var resolvers = new Dictionary<int, Func<string, RideScriptFile>?>();
		foreach ( var id in rootIds.OrderBy( id => id ) )
		{
			var candidate = rootScripts[id];
			var saved = byId[id];
			if ( candidate.World != this )
				throw new InvalidDataException( $"root script {id} candidate was constructed against another world." );
			VerifyCandidate( candidate, saved );
			nodes[id] = candidate;
			resolvers[id] = candidate.Options.ResolveScript;
		}
		var pending = snapshot.Scripts.Where( script => script.ParentScriptId != 0 ).ToList();
		while ( pending.Count > 0 )
		{
			var progressed = false;
			foreach ( var saved in pending.ToArray() )
			{
				if ( !resolvers.TryGetValue( saved.ParentScriptId, out var resolve ) )
					continue; // the parent is itself a pending child; a later sweep has it resolved
				if ( resolve == null )
					throw new InvalidDataException( $"script {saved.ScriptId}: parent {saved.ParentScriptId} has no script resolver to load '{saved.ScriptFileName}'." );
				var file = resolve( saved.ScriptFileName! )
					?? throw new InvalidDataException( $"script {saved.ScriptId}: child script '{saved.ScriptFileName}' was not found by the parent's resolver." );
				if ( RideScriptIdentity.Hash( file ) != saved.ScriptIdentityHash )
					throw new InvalidDataException( $"script {saved.ScriptId}: resolved child '{saved.ScriptFileName}' does not match the saved script identity." );
				if ( file.VariableCount != saved.Variables.Length )
					throw new InvalidDataException( $"script {saved.ScriptId}: resolved child '{saved.ScriptFileName}' has {file.VariableCount} variables, the snapshot stores {saved.Variables.Length}." );
				files[saved.ScriptId] = file;
				resolvers[saved.ScriptId] = resolve;
				pending.Remove( saved );
				progressed = true;
			}
			if ( !progressed )
				throw new InvalidDataException( $"child scripts {string.Join( ", ", pending.Select( script => script.ScriptId ) )} were not reached through their parents." );
		}

		// Pass 1 — construct the children top-down (a parent node exists before its children are built; id
		// order is not assumed to be topology), with each child's seed explicit so the stream is never drawn;
		// then release the temporary registrations and rebind every saved id.
		var construct = snapshot.Scripts.Where( script => script.ParentScriptId != 0 ).ToList();
		while ( construct.Count > 0 )
		{
			var progressed = false;
			foreach ( var saved in construct.ToArray() )
			{
				if ( !nodes.TryGetValue( saved.ParentScriptId, out var parent ) )
					continue; // the parent is itself a child still waiting for its construction turn
				var childOptions = new RideVMOptions
				{
					Effects = parent.Options.Effects,
					World = this,
					ResolveScript = parent.Options.ResolveScript,
					Seed = saved.Seed,
					SourceName = $"{parent.SourceName} > {saved.ScriptFileName}"
				};
				var child = new RideVM( files[saved.ScriptId], childOptions, parent );
				if ( child.SourceName != saved.SourceName )
					throw new InvalidDataException( $"script {saved.ScriptId}: constructed source '{child.SourceName}' does not match the snapshot's '{saved.SourceName}'." );
				nodes[saved.ScriptId] = child;
				construct.Remove( saved );
				progressed = true;
			}
			if ( !progressed )
				throw new InvalidDataException( $"child scripts {string.Join( ", ", construct.Select( script => script.ScriptId ) )} have no constructed parent." );
		}
		foreach ( var vm in nodes.Values )
			UnregisterById( vm.ScriptId );
		foreach ( var saved in snapshot.Scripts )
		{
			var vm = nodes[saved.ScriptId];
			vm.RestoreSnapshot( saved );
			RegisterWithId( vm, saved.ScriptId );
		}

		// Pass 2 — the child and sound-child links (validation proved them consistent both ways).
		foreach ( var saved in snapshot.Scripts )
		{
			if ( saved.ChildScriptId != 0 )
			{
				if ( !nodes.TryGetValue( saved.ChildScriptId, out var child ) )
					throw new InvalidDataException( $"script {saved.ScriptId} links child {saved.ChildScriptId}, which the restore did not construct." );
				nodes[saved.ScriptId].AdoptChild( child, sound: false );
			}
			if ( saved.SoundChildScriptId != 0 )
			{
				if ( !nodes.TryGetValue( saved.SoundChildScriptId, out var soundChild ) )
					throw new InvalidDataException( $"script {saved.ScriptId} links sound child {saved.SoundChildScriptId}, which the restore did not construct." );
				nodes[saved.ScriptId].AdoptChild( soundChild, sound: true );
			}
		}

		// Pass 3 — the world counters, then the post-condition: exactly the saved scripts stay registered.
		RestoreCounters( snapshot.NextScriptId, snapshot.NextAttractionId, snapshot.ClockMilliseconds, snapshot.RandomState, snapshot.CoasterRandomState );
		var live = scripts.Values.Select( vm => vm.ScriptId ).OrderBy( id => id ).ToArray();
		var savedIds = snapshot.Scripts.Select( script => script.ScriptId ).OrderBy( id => id ).ToArray();
		if ( !live.SequenceEqual( savedIds ) )
			throw new InvalidDataException( $"this world holds script ids [{string.Join( ", ", live )}] after the restore; the snapshot describes [{string.Join( ", ", savedIds )}]." );
	}

	private static void VerifyCandidate( RideVM candidate, RideVMSnapshot saved )
	{
		if ( candidate.Parent != null )
			throw new InvalidDataException( $"root script {saved.ScriptId} candidate is a child of script {candidate.Parent.ScriptId}." );
		if ( RideScriptIdentity.Hash( candidate.Script ) != saved.ScriptIdentityHash )
			throw new InvalidDataException( $"root script {saved.ScriptId} candidate '{candidate.SourceName}' does not match the saved script identity." );
		if ( candidate.SourceName != saved.SourceName )
			throw new InvalidDataException( $"root script {saved.ScriptId} candidate source '{candidate.SourceName}' does not match the snapshot's '{saved.SourceName}'." );
		if ( candidate.Seed != saved.Seed )
			throw new InvalidDataException( $"root script {saved.ScriptId} candidate seed {candidate.Seed} does not match the saved seed {saved.Seed}." );
		if ( candidate.SliceBudget != saved.SliceBudget )
			throw new InvalidDataException( $"root script {saved.ScriptId} candidate slice budget {candidate.SliceBudget} does not match the snapshot's {saved.SliceBudget}." );
		if ( candidate.Variables.Length != saved.Variables.Length )
			throw new InvalidDataException( $"root script {saved.ScriptId} candidate has {candidate.Variables.Length} variables, the snapshot stores {saved.Variables.Length}." );
	}
}

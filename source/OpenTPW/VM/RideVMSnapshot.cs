namespace OpenTPW;

/// <summary>
/// Structural identity of a compiled RSE script: a hash over everything <see cref="RideScriptFile"/> parses —
/// the header limits, every instruction word and operand kind/value, the string blob's strings and the variable
/// names. Scripts parsed from equal bytes give equal hashes; a save stores the hash of each live script, and a
/// restore refuses a candidate whose resolved script hashes differently (the original data, not the save, is the
/// source of scripts).
/// </summary>
public static class RideScriptIdentity
{
	public static ulong Hash( RideScriptFile script )
	{
		ArgumentNullException.ThrowIfNull( script );
		var hash = new StateHasher();
		hash.Add( script.VariableCount );
		hash.Add( script.StackSize );
		hash.Add( script.TimeSlice );
		hash.Add( script.LimboSize );
		hash.Add( script.BounceSize );
		hash.Add( script.WalkSize );
		hash.Add( script.CodeWordCount );
		hash.Add( script.StringBlobLength );
		hash.Add( script.Instructions.Count );
		foreach ( var instruction in script.Instructions )
		{
			hash.Add( instruction.WordOffset );
			hash.Add( instruction.Opcode );
			hash.Add( instruction.Operands.Count );
			foreach ( var operand in instruction.Operands )
			{
				hash.Add( (int)operand.Kind );
				hash.Add( operand.Value );
			}
		}
		hash.Add( script.Strings.Count );
		foreach ( var (offset, text) in script.Strings.OrderBy( pair => pair.Key, Comparer<int>.Default ) )
		{
			hash.Add( offset );
			hash.Add( text );
		}
		hash.Add( script.VariableNames.Count );
		foreach ( var name in script.VariableNames )
			hash.Add( name );
		return hash.Value;
	}
}

/// <summary>
/// Explicit state of one <see cref="RideVM"/> (park-resume slice): script identity, the instruction pointer and
/// call stack, variables, flags, slice budget and critical-section state, every clock, timer and animation wait
/// token, the counters, the unimplemented-effect record and the full explicit RNG state. Script IDs refer to the
/// <see cref="RideScriptWorldSnapshot"/> graph; 0 means "none". DTOs own their arrays and dictionaries.
/// Not captured: the <see cref="RideVM.InstructionExecuting"/> diagnostic hook (transient, no simulation effect)
/// and any state inside a custom <see cref="IRideScriptEffects"/> implementation beyond what
/// <see cref="OriginalObjectRuntimeSnapshot"/> stores.
/// </summary>
public sealed record RideVMSnapshot
{
	public const int CurrentSchemaVersion = 1;
	/// <summary>Pure-validation bound for <see cref="ProgramCounter"/>, call-stack entries and counts: the RSE code-word limit.</summary>
	public const int MaximumCodeWords = RideScriptFile.MaximumCodeWords;

	public int SchemaVersion { get; init; } = CurrentSchemaVersion;
	public required int ScriptId { get; init; }
	/// <summary>0 for a root script (one constructed directly); otherwise its parent's <see cref="ScriptId"/>.</summary>
	public required int ParentScriptId { get; init; }
	/// <summary>Child of SPAWNCHILD; 0 = none.</summary>
	public required int ChildScriptId { get; init; }
	/// <summary>Child of SPAWNSOUND; 0 = none. Never equals <see cref="ChildScriptId"/>.</summary>
	public required int SoundChildScriptId { get; init; }
	/// <summary>The SPAWNCHILD/SPAWNSOUND file-name operand this script was resolved by; null for roots.</summary>
	public string? ScriptFileName { get; init; }
	public required string SourceName { get; init; }
	public required string ScriptName { get; init; }
	/// <summary><see cref="RideScriptIdentity.Hash"/> of this script's parsed code.</summary>
	public required ulong ScriptIdentityHash { get; init; }
	/// <summary>The constructor seed (drawn from the world stream or explicit); hashed by the canonical state.</summary>
	public required int Seed { get; init; }
	/// <summary>Full explicit generator state; the seed alone does not continue the draw sequence.</summary>
	public required ScriptRandomState RandomState { get; init; }
	public required long RandomDraws { get; init; }
	public required RideVMState State { get; init; }
	/// <summary>Non-null exactly while <see cref="State"/> is <see cref="RideVMState.Faulted"/>.</summary>
	public string? FaultMessage { get; init; }
	/// <summary>Next instruction index, or the native diagnostic sentinel −10000 (only while faulted); at most the instruction count.</summary>
	public required int ProgramCounter { get; init; }
	/// <summary>Return addresses bottom → top (LIFO order of <see cref="RideVM.CallDepth"/>).</summary>
	public required int[] CallStack { get; init; }
	public required int[] Variables { get; init; }
	public required RideVM.VMFlags Flags { get; init; }
	public required int SliceBudget { get; init; }
	public required bool InCriticalSection { get; init; }
	/// <summary>Slice-yield state inside the current tick (<see cref="RideVM"/> restores it mid-slice).</summary>
	public required bool YieldRequested { get; init; }
	/// <summary>Script clock: milliseconds since this VM was created.</summary>
	public required double TimeMilliseconds { get; init; }
	/// <summary>World scheduler clock at this VM's creation.</summary>
	public required double ClockOriginMilliseconds { get; init; }
	public required double WakeTimeMilliseconds { get; init; }
	public required double TimerEndMilliseconds { get; init; }
	/// <summary>WAIT4ANIM deadline (script +164); 0 = none.</summary>
	public required double Wait4AnimDeadline { get; init; }
	/// <summary>LOOPANIM token (script +168); <see cref="RideAnimationTiming.NoLoopToken"/> at creation.</summary>
	public required int LoopAnimationToken { get; init; }
	/// <summary>TRIGWAITANIM wait token (script +188); 0 = none.</summary>
	public required int TriggerWaitToken { get; init; }
	/// <summary>Script speed bias (script +192); 50 at creation.</summary>
	public int SpeedBias { get; init; } = RideVM.DefaultSpeedBias;
	public required long ExecutedInstructions { get; init; }
	public required long SliceCount { get; init; }
	/// <summary>Hooked opcode → calls its effects implementation reported as unimplemented.</summary>
	public IReadOnlyDictionary<Opcode, int> UnimplementedEffects { get; init; } = new Dictionary<Opcode, int>();

	/// <summary>Deep copy.</summary>
	public RideVMSnapshot DeepCopy() => this with
	{
		CallStack = (int[])CallStack.Clone(),
		Variables = (int[])Variables.Clone(),
		UnimplementedEffects = new Dictionary<Opcode, int>( UnimplementedEffects )
	};

	/// <summary>
	/// Pure structural validation of this script alone (bounds, enum domains, RNG shape); cross-reference checks
	/// against the other scripts of a <see cref="RideScriptWorldSnapshot"/>. Throws
	/// <see cref="InvalidDataException"/> on any violation. Checks that need the resolved script (variable count,
	/// stack size, program-counter range) happen in the restore.
	/// </summary>
	public void ValidateSnapshot()
	{
		if ( SchemaVersion != CurrentSchemaVersion )
			throw new InvalidDataException( $"RideVM snapshot schema version {SchemaVersion} is not {CurrentSchemaVersion}." );
		if ( CallStack == null || Variables == null || RandomState == null || UnimplementedEffects == null )
			throw new InvalidDataException( $"Script {ScriptId} snapshot carries a null array (call stack, variables, RNG state or effect record)." );
		if ( ScriptId < 1 )
			throw new InvalidDataException( $"Script id {ScriptId} is not positive." );
		if ( ParentScriptId < 0 )
			throw new InvalidDataException( $"Parent script id {ParentScriptId} is negative." );
		if ( ChildScriptId < 0 || SoundChildScriptId < 0 )
			throw new InvalidDataException( $"Child links ({ChildScriptId}, {SoundChildScriptId}) are negative." );
		if ( ChildScriptId == ScriptId || SoundChildScriptId == ScriptId )
			throw new InvalidDataException( $"Script {ScriptId} links to itself." );
		if ( ChildScriptId != 0 && ChildScriptId == SoundChildScriptId )
			throw new InvalidDataException( $"Script {ScriptId} has the same child and sound child {ChildScriptId}." );
		if ( ( ParentScriptId == 0 ) != ( ScriptFileName == null ) )
			throw new InvalidDataException( $"Script {ScriptId}: a root has no file name and a child has one (parent {ParentScriptId}, file '{ScriptFileName}')." );
		if ( ScriptFileName != null && ScriptFileName.Length == 0 )
			throw new InvalidDataException( $"Script {ScriptId} has an empty child file name." );
		if ( SourceName == null || ScriptName == null )
			throw new InvalidDataException( $"Script {ScriptId} has a null name." );
		if ( !Enum.IsDefined( State ) )
			throw new InvalidDataException( $"Script {ScriptId} state {State} is not defined." );
		if ( ( FaultMessage != null ) != ( State == RideVMState.Faulted ) )
			throw new InvalidDataException( $"Script {ScriptId} is {State} with fault message '{FaultMessage}'." );
		if ( ProgramCounter < -10000 || ProgramCounter > MaximumCodeWords )
			throw new InvalidDataException( $"Script {ScriptId} program counter {ProgramCounter} is outside [-10000, {MaximumCodeWords}]." );
		if ( ProgramCounter < 0 && ProgramCounter != -10000 )
			throw new InvalidDataException( $"Script {ScriptId} program counter {ProgramCounter} is not the -10000 diagnostic sentinel." );
		if ( ProgramCounter < 0 && State != RideVMState.Faulted )
			throw new InvalidDataException( $"Script {ScriptId} is {State} but carries the faulted-only program counter {ProgramCounter}." );
		if ( CallStack.Length > MaximumCodeWords )
			throw new InvalidDataException( $"Script {ScriptId} call stack has {CallStack.Length} entries, above the code-word limit." );
		foreach ( var address in CallStack )
			if ( address < 0 || address > MaximumCodeWords )
				throw new InvalidDataException( $"Script {ScriptId} call-stack address {address} is outside [0, {MaximumCodeWords}]." );
		if ( Variables.Length > RideScriptFile.MaximumVariables )
			throw new InvalidDataException( $"Script {ScriptId} has {Variables.Length} variables, above the limit." );
		if ( ( Flags & ~RideVM.VMFlags.All ) != 0 )
			throw new InvalidDataException( $"Script {ScriptId} flags {Flags} hold bits besides Sign and Zero." );
		if ( SliceBudget < 1 )
			throw new InvalidDataException( $"Script {ScriptId} slice budget {SliceBudget} is not positive." );
		if ( !double.IsFinite( TimeMilliseconds ) || TimeMilliseconds < 0 )
			throw new InvalidDataException( $"Script {ScriptId} script clock {TimeMilliseconds} is not finite and non-negative." );
		if ( !double.IsFinite( ClockOriginMilliseconds ) || ClockOriginMilliseconds < 0 )
			throw new InvalidDataException( $"Script {ScriptId} clock origin {ClockOriginMilliseconds} is not finite and non-negative." );
		if ( !double.IsFinite( WakeTimeMilliseconds ) || WakeTimeMilliseconds < 0 )
			throw new InvalidDataException( $"Script {ScriptId} wake time {WakeTimeMilliseconds} is not finite and non-negative." );
		if ( !double.IsFinite( TimerEndMilliseconds ) )
			throw new InvalidDataException( $"Script {ScriptId} timer end {TimerEndMilliseconds} is not finite." );
		if ( !double.IsFinite( Wait4AnimDeadline ) || Wait4AnimDeadline < 0 )
			throw new InvalidDataException( $"Script {ScriptId} WAIT4ANIM deadline {Wait4AnimDeadline} is not finite and non-negative." );
		if ( ExecutedInstructions < 0 || SliceCount < 0 || RandomDraws < 0 )
			throw new InvalidDataException( $"Script {ScriptId} counters are negative." );
		RandomState.Validate();
		foreach ( var (opcode, count) in UnimplementedEffects )
		{
			if ( !Enum.IsDefined( opcode ) )
				throw new InvalidDataException( $"Script {ScriptId} unimplemented-effect opcode {opcode} is not defined." );
			if ( count < 1 )
				throw new InvalidDataException( $"Script {ScriptId} unimplemented effect {opcode} has count {count}." );
		}
	}
}

/// <summary>
/// Explicit state of a whole <see cref="RideScriptWorld"/> (park-resume slice): the seed-stream state
/// (<see cref="RideScriptWorld.RandomState"/>), the shared scheduler clock, both allocators' frontiers and every
/// live script with its graph links. Coaster controllers are not part of this slice: capturing a world whose
/// <see cref="CoasterWorld"/> holds rides fails explicitly instead of silently omitting their state.
/// </summary>
public sealed record RideScriptWorldSnapshot
{
	public const int CurrentSchemaVersion = 1;

	public int SchemaVersion { get; init; } = CurrentSchemaVersion;
	/// <summary>SplitMix64 state of the seed stream for scripts created without an explicit seed.</summary>
	public required ulong RandomState { get; init; }
	/// <summary>
	/// The coaster controllers' shared boarding generator (<see cref="CoasterRandom.State"/>), captured with or
	/// without coasters so the stream never restarts; the controllers themselves are not part of this slice (a world
	/// with live coasters refuses to capture at all).
	/// </summary>
	public required uint CoasterRandomState { get; init; }
	/// <summary>The shared scheduler clock (latest time any script was advanced to), in milliseconds.</summary>
	public required double ClockMilliseconds { get; init; }
	/// <summary>Next script id the allocator will hand out (greater than every live id).</summary>
	public required int NextScriptId { get; init; }
	/// <summary>Next attraction id the allocator will hand out.</summary>
	public required int NextAttractionId { get; init; }
	/// <summary>Every live script, ordered by id.</summary>
	public required IReadOnlyList<RideVMSnapshot> Scripts { get; init; }

	/// <summary>Deep copy.</summary>
	public RideScriptWorldSnapshot DeepCopy() => this with { Scripts = Scripts.Select( script => script.DeepCopy() ).ToArray() };

	/// <summary>
	/// Pure validation of the whole graph: every script passes <see cref="RideVMSnapshot.ValidateSnapshot"/>, ids
	/// are unique, parent/child links resolve both ways, the graph is acyclic and the allocator frontier is above
	/// every live id. Throws <see cref="InvalidDataException"/>; touches no live object.
	/// </summary>
	public void ValidateSnapshot()
	{
		if ( SchemaVersion != CurrentSchemaVersion )
			throw new InvalidDataException( $"RideScriptWorld snapshot schema version {SchemaVersion} is not {CurrentSchemaVersion}." );
		if ( Scripts == null )
			throw new InvalidDataException( "The world snapshot carries no script array." );
		if ( !double.IsFinite( ClockMilliseconds ) || ClockMilliseconds < 0 )
			throw new InvalidDataException( $"World clock {ClockMilliseconds} is not finite and non-negative." );
		if ( NextScriptId < 1 || NextAttractionId < 1 )
			throw new InvalidDataException( $"World allocators ({NextScriptId}, {NextAttractionId}) are not positive." );
		var byId = new Dictionary<int, RideVMSnapshot>();
		for ( var index = 0; index < Scripts.Count; index++ )
		{
			var script = Scripts[index];
			if ( script == null )
				throw new InvalidDataException( $"The world snapshot's script entry {index} is null." );
			script.ValidateSnapshot();
			if ( !byId.TryAdd( script.ScriptId, script ) )
				throw new InvalidDataException( $"Script id {script.ScriptId} appears twice." );
			if ( script.ScriptId >= NextScriptId )
				throw new InvalidDataException( $"Script id {script.ScriptId} is not below the allocator frontier {NextScriptId}." );
		}
		foreach ( var script in Scripts )
		{
			if ( script.ParentScriptId != 0 )
			{
				if ( !byId.TryGetValue( script.ParentScriptId, out var parent ) )
					throw new InvalidDataException( $"Script {script.ScriptId} has parent {script.ParentScriptId}, which the snapshot does not contain." );
				if ( parent.ChildScriptId != script.ScriptId && parent.SoundChildScriptId != script.ScriptId )
					throw new InvalidDataException( $"Script {script.ScriptId} names parent {script.ParentScriptId}, which links to ({parent.ChildScriptId}, {parent.SoundChildScriptId})." );
			}
			foreach ( var (link, kind) in new[] { (script.ChildScriptId, "child"), (script.SoundChildScriptId, "sound child") } )
				if ( link != 0 && !byId.ContainsKey( link ) )
					throw new InvalidDataException( $"Script {script.ScriptId} links {kind} {link}, which the snapshot does not contain." );
		}
		// The child graph is a forest of depth-one links per node; walk it to refuse cycles before restoring.
		var visited = new HashSet<int>();
		var stack = new Stack<int>();
		foreach ( var script in Scripts )
		{
			visited.Clear();
			stack.Push( script.ScriptId );
			while ( stack.Count > 0 )
			{
				var id = stack.Pop();
				if ( !visited.Add( id ) )
					throw new InvalidDataException( $"Script graph reaches script {id} twice from {script.ScriptId} (cycle)." );
				var node = byId[id];
				if ( node.ChildScriptId != 0 )
					stack.Push( node.ChildScriptId );
				if ( node.SoundChildScriptId != 0 )
					stack.Push( node.SoundChildScriptId );
			}
		}
		// Links must agree both ways: a linked child names its linker as parent, so a root can never be
		// adopted as someone's child and a child link cannot point at another script's child.
		foreach ( var script in Scripts )
			foreach ( var (link, kind) in new[] { (script.ChildScriptId, "child"), (script.SoundChildScriptId, "sound child") } )
				if ( link != 0 && byId[link].ParentScriptId != script.ScriptId )
					throw new InvalidDataException( $"Script {script.ScriptId} links {kind} {link}, whose parent is {byId[link].ParentScriptId}, not {script.ScriptId}." );
	}
}

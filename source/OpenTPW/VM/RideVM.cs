namespace OpenTPW;

public enum RideVMState
{
	/// <summary>Executes its next slice on the next <see cref="RideVM.Advance"/>.</summary>
	Running,

	/// <summary>Blocked by WAIT/WAITANIM/WAIT4ANIM/TRIGWAITANIM until <see cref="RideVM.WakeTimeMilliseconds"/>.</summary>
	Waiting,

	/// <summary>Ran past its last instruction.</summary>
	Halted,

	/// <summary>Stopped by an error; see <see cref="RideVM.FaultMessage"/>.</summary>
	Faulted
}

public sealed class RideVMOptions
{
	/// <summary>Game effects of hooked opcodes. Default: <see cref="UnimplementedRideScriptEffects"/>.</summary>
	public IRideScriptEffects? Effects { get; init; }

	/// <summary>Scripts visible to FINDSCRIPTRAND / remote variables. Default: a new world for this script tree.</summary>
	public RideScriptWorld? World { get; init; }

	/// <summary>Loads a SPAWNCHILD/SPAWNSOUND script by its file name (resolved in the parent's archive). Null if missing.</summary>
	public Func<string, RideScriptFile?>? ResolveScript { get; init; }

	/// <summary>RAND / FINDSCRIPTRAND seed; null draws one from the <see cref="RideScriptWorld"/>'s seed stream.</summary>
	public int? Seed { get; init; }

	/// <summary>Instructions per slice. Default: the script header's time slice (50 in every corpus script).</summary>
	public int? SliceBudget { get; init; }

	/// <summary>Diagnostic name (archive member path).</summary>
	public string? SourceName { get; init; }
}

/// <summary>
/// Interpreter for compiled RSE scripts.
///
/// Scheduling (not verified against the original engine, see docs/RSE-VM.md): every call to
/// <see cref="Advance"/> — one <see cref="FixedStepClock"/> tick in the game — adds the elapsed time to the
/// script clock and runs one slice. A slice runs until the script yields (ENDSLICE or a wait) or until
/// it has executed <see cref="SliceBudget"/> instructions outside a CRIT_LOCK section; the budget is
/// what lets the corpus's busy-wait loops (e.g. Totem's passenger loop) give time back to the game.
/// Times (WAIT, GETTIME, SETTIMER) are milliseconds.
/// </summary>
public sealed class RideVM
{
	/// <summary>Stops a slice that never leaves a critical section.</summary>
	public const int MaximumInstructionsPerSlice = 100_000;

	private readonly Instruction[] instructions;
	private readonly Stack<int> callStack = new();
	// An owned seeded .NET-compatible generator whose full state the park-resume snapshot can store; its draws
	// are exactly a seeded System.Random's (ScriptRandomTests proves it). The canonical hash still takes the seed
	// plus the number of RNG calls, unchanged.
	private readonly ScriptRandom random;
	private readonly int seed;
	private long randomDraws;
	private readonly RideVMOptions options;
	private readonly Dictionary<Opcode, int> unimplementedEffects = new();
	private bool yieldRequested;
	private double timerEndMilliseconds;

	public RideScriptFile Script { get; }
	public string SourceName { get; }
	public string ScriptName { get; internal set; } = "Unnamed";
	public int ScriptId { get; internal set; }
	/// <summary>The constructor options (effects, world, resolver, seed) — children copy them when spawned and the restore reads them.</summary>
	internal RideVMOptions Options => options;
	/// <summary>The constructor seed (explicit or drawn from the world stream); hashed by the canonical state.</summary>
	internal int Seed => seed;
	public RideScriptWorld World { get; }
	public IRideScriptEffects Effects { get; }
	public RideVM? Parent { get; }
	public RideVM? Child { get; private set; }
	public RideVM? SoundChild { get; private set; }

	public RideVMState State { get; private set; } = RideVMState.Running;
	public string? FaultMessage { get; private set; }

	/// <summary>Index of the next instruction to execute, or -10000 after a native diagnostic abort.</summary>
	public int ProgramCounter { get; private set; }
	public IReadOnlyList<Instruction> Instructions => instructions;
	public int[] Variables { get; }
	public IReadOnlyList<string> VariableNames => Script.VariableNames;
	public RideVM.VMFlags Flags { get; internal set; } = VMFlags.None;
	public int SliceBudget { get; }
	public bool InCriticalSection { get; internal set; }
	public int CallDepth => callStack.Count;

	/// <summary>Script clock: milliseconds since this VM was created (waits, timers and their deadlines).</summary>
	public double TimeMilliseconds { get; private set; }

	/// <summary>The world's scheduler clock (<see cref="RideScriptWorld.ClockMilliseconds"/>) when this VM was created. The snapshot restore re-applies the saved origin.</summary>
	public double ClockOriginMilliseconds { get; internal set; }

	/// <summary>
	/// The shared scheduler clock as this script reads it: <see cref="ClockOriginMilliseconds"/> plus the script clock.
	/// The original's GETTIME and WAITANIM(_CH) call 0x10e844 on the global r30 (loaded at 0xaf55c) and subtract no
	/// creation timestamp.
	/// </summary>
	// [APPROX:RIDES-044] The scheduler clock starts at 0 when the park's script world is created and advances with the script ticks (60 Hz fixed steps); the PC clock is the app-lifetime scaled timer 0x785970 (started once at 0x54dcf0, read by 0x402d70 → 0x4031e0 plus +0x44/+0x48, paused and resumed by 0x402ed0/0x402ea0, set only by the save writer 0x4164c0 and loader 0x415270, never reset for a new park), so a new park's first script sees the time since program start, including the front end; the two differ only in absolute GETTIME values, as every deadline is relative — evidence needed: GETTIME's PC handler (to show it is used only for differences)
	public double SchedulerMilliseconds => ClockOriginMilliseconds + TimeMilliseconds;

	/// <summary>The scheduler clock as the original's 32-bit millisecond word (truncated, wrapping after about 49.7 days).</summary>
	internal uint ClockMilliseconds32 => unchecked((uint)(ulong)SchedulerMilliseconds);
	public double WakeTimeMilliseconds { get; private set; }
	public long ExecutedInstructions { get; private set; }
	public long SliceCount { get; private set; }

	/// <summary>Hooked opcode → number of calls the effects implementation reported as unimplemented.</summary>
	public IReadOnlyDictionary<Opcode, int> UnimplementedEffects => unimplementedEffects;

	/// <summary>Called before each instruction executes (diagnostics and traces).</summary>
	public Action<RideVM, Instruction>? InstructionExecuting { get; set; }

	[Flags]
	public enum VMFlags
	{
		None,

		/// <summary>Last flag-setting result was negative.</summary>
		Sign = 1,

		/// <summary>Last flag-setting result was zero.</summary>
		Zero = 2,

		All = Sign | Zero
	}

	public RideVM( Stream stream, RideVMOptions? options = null ) : this( new RideScriptFile( stream ), options )
	{
	}

	public RideVM( RideScriptFile script, RideVMOptions? options = null ) : this( script, options ?? new RideVMOptions(), null )
	{
	}

	/// <summary>Called by the public constructors, by <see cref="SpawnChild"/> and by the snapshot restore (which passes an explicit parent).</summary>
	internal RideVM( RideScriptFile script, RideVMOptions options, RideVM? parent )
	{
		ArgumentNullException.ThrowIfNull( script );
		Script = script;
		this.options = options;
		Parent = parent;
		if ( parent != null )
			SpeedBias = parent.SpeedBias;
		SourceName = options.SourceName ?? "script";
		Effects = options.Effects ?? UnimplementedRideScriptEffects.Instance;
		World = options.World ?? parent?.World ?? new RideScriptWorld();
		seed = options.Seed ?? World.NextScriptSeed();
		ClockOriginMilliseconds = parent?.SchedulerMilliseconds ?? World.ClockMilliseconds;
		random = new ScriptRandom( seed );
		SliceBudget = options.SliceBudget ?? script.TimeSlice;
		if ( SliceBudget <= 0 )
			throw new InvalidDataException( $"RSE slice budget {SliceBudget} is not positive." );
		Variables = new int[script.VariableCount];

		instructions = new Instruction[script.Instructions.Count];
		for ( var index = 0; index < instructions.Length; ++index )
		{
			var source = script.Instructions[index];
			var operands = source.Operands.Select( operand => new Operand( this, operand,
				operand.Kind == RideScriptOperandKind.Branch ? script.GetInstructionIndexAtWord( operand.Value ) : -1 ) ).ToArray();
			RideOpcodes.Definitions.TryGetValue( (Opcode)source.Opcode, out var definition );
			if ( definition != null && definition.OperandCount != operands.Length )
				throw new InvalidDataException( $"RSE instruction at word {source.WordOffset}: {(Opcode)source.Opcode} has {operands.Length} operands, the VM expects {definition.OperandCount}." );
			instructions[index] = new Instruction( index, source.WordOffset, source.Opcode, operands, definition );
		}

		ScriptId = World.Register( this );
	}

	public int this[RideVariables variable]
	{
		get => Variables[(int)variable];
		set => Variables[(int)variable] = value;
	}

	public int GetVariableIndex( string name )
	{
		for ( var index = 0; index < Script.VariableNames.Count; ++index )
			if ( string.Equals( Script.VariableNames[index], name, StringComparison.Ordinal ) )
				return index;
		return -1;
	}

	/// <summary>Advances the script clock and runs at most one slice of this script and then its children.</summary>
	public void Advance( double deltaSeconds )
	{
		if ( !double.IsFinite( deltaSeconds ) || deltaSeconds < 0 )
			throw new ArgumentOutOfRangeException( nameof( deltaSeconds ) );
		// [EXT:vm-fault-containment] a faulted script's clock stops and its children are removed; the original has no managed
		// fault state to compare with (docs/RSE-VM.md, "Independent root-fault lifecycle controls")
		if ( State != RideVMState.Faulted )
		{
			TimeMilliseconds += deltaSeconds * 1000;
			World.ObserveClock( SchedulerMilliseconds );
			RunSlice();
		}
		if ( StopChildrenIfFaulted() )
			return;
		Child?.Advance( deltaSeconds );
		if ( StopChildrenIfFaulted() )
			return;
		SoundChild?.Advance( deltaSeconds );
		StopChildrenIfFaulted();
	}

	private bool StopChildrenIfFaulted()
	{
		// Managed host containment also covers a descendant callback faulting its parent.
		if ( State != RideVMState.Faulted )
			return false;
		RemoveChild();
		RemoveSoundChild();
		return true;
	}

	private void RunSlice()
	{
		if ( State is RideVMState.Halted or RideVMState.Faulted )
			return;
		if ( State == RideVMState.Waiting )
		{
			if ( TimeMilliseconds < WakeTimeMilliseconds )
				return;
			State = RideVMState.Running;
		}

		++SliceCount;
		yieldRequested = false;
		var executed = 0;
		while ( !yieldRequested && State == RideVMState.Running )
		{
			if ( executed >= SliceBudget && !InCriticalSection )
				break;
			if ( executed >= MaximumInstructionsPerSlice )
			{
				Fault( $"slice exceeded {MaximumInstructionsPerSlice} instructions inside a critical section" );
				break;
			}
			if ( ProgramCounter >= instructions.Length )
			{
				State = RideVMState.Halted;
				break;
			}

			var instruction = instructions[ProgramCounter++];
			++executed;
			++ExecutedInstructions;
			InstructionExecuting?.Invoke( this, instruction );
			RideOpcodeRecorder.Current?.Record( this, instruction );
			if ( instruction.Definition == null )
			{
				Fault( $"opcode {RideScriptAnalysis.GetOpcodeName( instruction.RawOpcode )} at word {instruction.WordOffset} has no evidence-backed semantics" );
				break;
			}
			try
			{
				instruction.Definition.Invoke( this, instruction.operands );
			}
			catch ( RideScriptException exception )
			{
				Fault( $"{instruction} at word {instruction.WordOffset}: {exception.Message}" );
			}
		}
	}

	private void Fault( string message )
	{
		State = RideVMState.Faulted;
		FaultMessage = message;
		Log?.Warning( $"RSE '{ScriptName}' ({SourceName}) faulted: {message}" );
	}

	/// <summary>
	/// Maps a native negative-PC diagnostic stop onto the VM's fault state.
	/// A non-variable COPY leaves its source word to the next native dispatch;
	/// that invalid opcode tag produces PC -10000. Parsed instructions abort directly.
	/// </summary>
	internal void Abort( string message )
	{
		ProgramCounter = -10000;
		Fault( message );
	}

	/// <summary>Stops this script and its children and removes them from the world.</summary>
	public void Stop()
	{
		RemoveChild();
		RemoveSoundChild();
		World.Unregister( this );
		if ( State != RideVMState.Faulted )
			State = RideVMState.Halted;
	}

	// ---- Park-resume snapshot (docs in RideVMSnapshot) ----

	/// <summary>
	/// Explicit state of this script alone (no world counters, no children state): identity, instruction state,
	/// clocks, counters and the full RNG state. <see cref="RideScriptWorld.CaptureSnapshot"/> adds the graph links.
	/// </summary>
	public RideVMSnapshot CaptureSnapshot()
	{
		string? fileName = null;
		if ( Parent != null )
		{
			var prefix = Parent.SourceName + " > ";
			if ( !SourceName.StartsWith( prefix, StringComparison.Ordinal ) )
				throw new InvalidDataException( $"child script source '{SourceName}' does not extend its parent's '{Parent.SourceName}'." );
			fileName = SourceName[prefix.Length..];
		}
		return new RideVMSnapshot
		{
			ScriptId = ScriptId,
			ParentScriptId = Parent?.ScriptId ?? 0,
			ChildScriptId = Child?.ScriptId ?? 0,
			SoundChildScriptId = SoundChild?.ScriptId ?? 0,
			ScriptFileName = fileName,
			SourceName = SourceName,
			ScriptName = ScriptName,
			ScriptIdentityHash = RideScriptIdentity.Hash( Script ),
			Seed = seed,
			RandomState = random.CaptureState(),
			RandomDraws = randomDraws,
			State = State,
			FaultMessage = FaultMessage,
			ProgramCounter = ProgramCounter,
			CallStack = callStack.Reverse().ToArray(),
			Variables = (int[])Variables.Clone(),
			Flags = Flags,
			SliceBudget = SliceBudget,
			InCriticalSection = InCriticalSection,
			YieldRequested = yieldRequested,
			TimeMilliseconds = TimeMilliseconds,
			ClockOriginMilliseconds = ClockOriginMilliseconds,
			WakeTimeMilliseconds = WakeTimeMilliseconds,
			TimerEndMilliseconds = timerEndMilliseconds,
			Wait4AnimDeadline = Wait4AnimDeadline,
			LoopAnimationToken = LoopAnimationToken,
			TriggerWaitToken = TriggerWaitToken,
			SpeedBias = SpeedBias,
			ExecutedInstructions = ExecutedInstructions,
			SliceCount = SliceCount,
			UnimplementedEffects = new Dictionary<Opcode, int>( unimplementedEffects )
		};
	}

	/// <summary>
	/// Applies a snapshot to this VM. Validates everything the resolved script can check (variable count, stack
	/// bounds, program-counter range, slice budget) and throws <see cref="InvalidDataException"/> — leaving this VM
	/// unchanged — on the first violation. Only <see cref="RideScriptWorld.RestoreSnapshot"/> calls this; it rebinds
	/// ids and links around it.
	/// </summary>
	internal void RestoreSnapshot( RideVMSnapshot snapshot )
	{
		ArgumentNullException.ThrowIfNull( snapshot );
		if ( snapshot.Variables.Length != Variables.Length )
			throw new InvalidDataException( $"script '{ScriptName}' ({SourceName}) has {Variables.Length} variables, the snapshot stores {snapshot.Variables.Length}." );
		if ( snapshot.CallStack.Length > Script.StackSize )
			throw new InvalidDataException( $"script '{ScriptName}' declares stack size {Script.StackSize}, the snapshot stores {snapshot.CallStack.Length} return addresses." );
		foreach ( var address in snapshot.CallStack )
			if ( address < 0 || address > instructions.Length )
				throw new InvalidDataException( $"script '{ScriptName}' return address {address} is outside [0, {instructions.Length}]." );
		if ( snapshot.ProgramCounter < -10000 || snapshot.ProgramCounter > instructions.Length )
			throw new InvalidDataException( $"script '{ScriptName}' program counter {snapshot.ProgramCounter} is outside [-10000, {instructions.Length}]." );
		if ( snapshot.ProgramCounter < 0 && snapshot.State != RideVMState.Faulted )
			throw new InvalidDataException( $"script '{ScriptName}' is {snapshot.State} with the faulted-only program counter {snapshot.ProgramCounter}." );
		if ( snapshot.SliceBudget != SliceBudget )
			throw new InvalidDataException( $"script '{ScriptName}' runs a slice budget of {SliceBudget}, the snapshot stores {snapshot.SliceBudget}." );

		ScriptName = snapshot.ScriptName;
		State = snapshot.State;
		FaultMessage = snapshot.FaultMessage;
		ProgramCounter = snapshot.ProgramCounter;
		callStack.Clear();
		for ( var index = snapshot.CallStack.Length - 1; index >= 0; index-- )
			callStack.Push( snapshot.CallStack[index] );
		Array.Copy( snapshot.Variables, Variables, Variables.Length );
		Flags = snapshot.Flags;
		InCriticalSection = snapshot.InCriticalSection;
		yieldRequested = snapshot.YieldRequested;
		TimeMilliseconds = snapshot.TimeMilliseconds;
		ClockOriginMilliseconds = snapshot.ClockOriginMilliseconds;
		WakeTimeMilliseconds = snapshot.WakeTimeMilliseconds;
		timerEndMilliseconds = snapshot.TimerEndMilliseconds;
		Wait4AnimDeadline = snapshot.Wait4AnimDeadline;
		LoopAnimationToken = snapshot.LoopAnimationToken;
		TriggerWaitToken = snapshot.TriggerWaitToken;
		SpeedBias = snapshot.SpeedBias;
		ExecutedInstructions = snapshot.ExecutedInstructions;
		SliceCount = snapshot.SliceCount;
		unimplementedEffects.Clear();
		foreach ( var (opcode, count) in snapshot.UnimplementedEffects )
			unimplementedEffects[opcode] = count;
		randomDraws = snapshot.RandomDraws;
		random.RestoreState( snapshot.RandomState );
	}

	/// <summary>Adopts a restored script id (the world rebinds the registration around it).</summary>
	internal void AdoptScriptId( int id ) => ScriptId = id;

	/// <summary>
	/// Re-applies a fault to a stopped script: a stopped runtime's VM is unregistered, so the world snapshot does
	/// not carry its state and the restore replays only the fault the runtime snapshot saved.
	/// </summary>
	internal void RestoreStoppedFault( string message )
	{
		State = RideVMState.Faulted;
		FaultMessage = message;
	}

	/// <summary>Links a restored child into this script's child or sound-child slot (validation happened on the snapshot).</summary>
	internal void AdoptChild( RideVM child, bool sound )
	{
		if ( sound )
			SoundChild = child;
		else
			Child = child;
	}

	// ---- Helpers for handlers ----

	internal void SetFlags( int result )
	{
		Flags = (result == 0 ? VMFlags.Zero : VMFlags.None) | (result < 0 ? VMFlags.Sign : VMFlags.None);
	}

	internal void BranchTo( Operand location ) => ProgramCounter = location.RequireBranchTarget();

	internal void PushReturn( Operand location )
	{
		if ( callStack.Count >= Script.StackSize )
			throw new RideScriptException( $"JSR exceeds the declared stack size {Script.StackSize}" );
		callStack.Push( ProgramCounter );
		BranchTo( location );
	}

	internal void PopReturn()
	{
		if ( callStack.Count == 0 )
			throw new RideScriptException( "RETURN without JSR" );
		ProgramCounter = callStack.Pop();
	}

	internal void EndSlice() => yieldRequested = true;

	internal void WaitFor( double milliseconds ) => WaitUntil( TimeMilliseconds + Math.Max( 0, milliseconds ) );

	internal void WaitUntil( double milliseconds )
	{
		WakeTimeMilliseconds = Math.Max( TimeMilliseconds, milliseconds );
		State = RideVMState.Waiting;
		yieldRequested = true;
	}

	internal int NextRandom( int maximumInclusive )
	{
		if ( maximumInclusive < 0 )
			throw new RideScriptException( $"RAND maximum {maximumInclusive} is negative" );
		randomDraws++;
		return random.Next( 0, maximumInclusive + 1 );
	}

	internal void SetTimer( int milliseconds ) => timerEndMilliseconds = TimeMilliseconds + milliseconds;

	internal int TimerRemaining => (int)Math.Max( 0, Math.Ceiling( timerEndMilliseconds - TimeMilliseconds ) );

	/// <summary>Script speed at the creation bias 50: 0.5 + 50 / 100.</summary>
	public const float DefaultScriptSpeed = 1f;

	/// <summary>The creation value of <see cref="SpeedBias"/> ([BIN:TPW-EXE:0x558c3c script creation]).</summary>
	public const int DefaultSpeedBias = 50;

	/// <summary>
	/// The script's speed bias (the ride's SPEED; setter 0x55a300): ride init writes <c>Upgrades[0].InitSpeed</c> when above 0
	/// (0x4db51c..0x4db54c), children copy their parent's (0x551888), and upgrade completion writes the new level's InitSpeed when above 0 (0x4df8f0).
	/// </summary>
	// The signed 16-bit word at script +192 (0xC0); the ride-script loop (tp.exe 0x5516b0, 0x5517e1..0x5517ef) hands it to the coaster speed setter 0x43b2b0 every pass.
	public int SpeedBias { get; internal set; } = DefaultSpeedBias;

	/// <summary>Script speed of the animation opcodes: <c>0.5 + bias / 100</c> in single precision (constants −0.01 and 0.5 at 0x551769).</summary>
	// [BIN:TPW-EXE:0x551769 script speed] speed = 0.5 + bias / 100; the bias is the ride's SPEED: ride init 0x4db51c..0x4db54c, children copy the parent's (0x551888), creation default 50 (0x558c3c), setter 0x55a300.
	// [APPROX:RIDES-033] Ride-settings speed changes (the UI command 0x4af440 → ride SetSpeed 0x4dd6e0, bias 0x55a300) are not modelled, as OpenTPW has no ride speed control — evidence needed: the ride-settings UI of the original
	public float ScriptSpeed => 0.5f + SpeedBias / 100f;

	/// <summary>WAIT4ANIM deadline (script +164) in script-clock milliseconds; 0 = none. Each trigger overwrites it.</summary>
	internal double Wait4AnimDeadline { get; set; }

	/// <summary>LOOPANIM token (script +168): <c>anim + (sub &lt;&lt; 16)</c> of the last LOOPANIM; 0xFFFF at creation.</summary>
	internal int LoopAnimationToken { get; set; } = RideAnimationTiming.NoLoopToken;

	/// <summary>TRIGWAITANIM wait token (script +188): triggered AnimID + 1 while waiting; 0 otherwise.</summary>
	internal int TriggerWaitToken { get; set; }

	/// <summary>Makes the instruction that is executing run again next (the original's program-counter rewind).</summary>
	internal void RepeatInstruction() => ProgramCounter--;

	internal int Effect( Opcode opcode, Operand[] operands ) => Effects.Perform( new RideEffectCall( this, opcode, operands ) );

	/// <summary>An effect result is unavailable when the implementation records this call as unimplemented.</summary>
	internal bool TryEffect( Opcode opcode, Operand[] operands, out int result )
	{
		unimplementedEffects.TryGetValue( opcode, out var before );
		result = Effect( opcode, operands );
		unimplementedEffects.TryGetValue( opcode, out var after );
		return after == before;
	}

	/// <summary>Counts an unimplemented effect; returns true the first time for this opcode.</summary>
	public bool RecordUnimplementedEffect( Opcode opcode )
	{
		unimplementedEffects.TryGetValue( opcode, out var count );
		unimplementedEffects[opcode] = count + 1;
		return count == 0;
	}

	internal int FindScript( string name )
	{
		randomDraws++;
		return World.FindRandom( name, random );
	}

	/// <summary>Script-table part of <see cref="WorldStateHash"/>: id, position, variables, clocks, deadlines, animation tokens, RNG and the object animation state of a root script.</summary>
	internal void AddCanonicalState( StateHasher hash )
	{
		hash.Add( ScriptId );
		hash.Add( ScriptName );
		hash.Add( Parent?.ScriptId ?? 0 );
		hash.Add( (int)State );
		hash.Add( ProgramCounter );
		hash.Add( (int)Flags );
		hash.Add( InCriticalSection );
		hash.Add( yieldRequested );
		hash.Add( callStack.Count );
		foreach ( var address in callStack )
			hash.Add( address );
		hash.Add( Variables.Length );
		foreach ( var value in Variables )
			hash.Add( value );
		hash.Add( ClockOriginMilliseconds );
		hash.Add( TimeMilliseconds );
		hash.Add( WakeTimeMilliseconds );
		hash.Add( timerEndMilliseconds );
		hash.Add( Wait4AnimDeadline );
		hash.Add( LoopAnimationToken );
		hash.Add( TriggerWaitToken );
		// A bias other than the creation value changes the script speed; the default leaves earlier digests alone.
		if ( SpeedBias != DefaultSpeedBias )
			hash.Add( SpeedBias );
		hash.Add( ExecutedInstructions );
		hash.Add( SliceCount );
		hash.Add( seed );
		hash.Add( randomDraws );
		// The object state a root script's animation opcodes read back (GETANIM, TRIGWAITANIM); children share it.
		hash.Add( Parent == null && Effects is IRideCanonicalEffects );
		if ( Parent == null && Effects is IRideCanonicalEffects canonical )
			canonical.AddCanonicalState( hash );
	}

	private int NextChildSeed()
	{
		randomDraws++;
		return random.Next();
	}

	internal void SpawnChild( string fileName, bool sound )
	{
		var resolve = options.ResolveScript ?? throw new RideScriptException( $"cannot load child script '{fileName}': no script resolver" );
		var script = resolve( fileName ) ?? throw new RideScriptException( $"child script '{fileName}' was not found" );
		var childOptions = new RideVMOptions
		{
			Effects = options.Effects,
			World = World,
			ResolveScript = options.ResolveScript,
			Seed = NextChildSeed(),
			SourceName = $"{SourceName} > {fileName}"
		};
		var child = new RideVM( script, childOptions, this );
		if ( sound )
		{
			RemoveSoundChild();
			SoundChild = child;
		}
		else
		{
			RemoveChild();
			Child = child;
		}
	}

	internal void RemoveChild()
	{
		Child?.Stop();
		Child = null;
	}

	private void RemoveSoundChild()
	{
		SoundChild?.Stop();
		SoundChild = null;
	}

	internal static int ReadVariable( RideVM target, int index ) => index >= 0 && index < target.Variables.Length
		? target.Variables[index]
		: throw new RideScriptException( $"variable index {index} is outside '{target.ScriptName}' ({target.Variables.Length} variables)" );

	internal static void WriteVariable( RideVM target, int index, int value )
	{
		if ( index < 0 || index >= target.Variables.Length )
			throw new RideScriptException( $"variable index {index} is outside '{target.ScriptName}' ({target.Variables.Length} variables)" );
		target.Variables[index] = value;
	}
}

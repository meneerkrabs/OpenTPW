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

	/// <summary>
	/// RAND / FINDSCRIPTRAND seed. Null (the default): the script has no generator of its own and draws from the <see cref="RideScriptWorld"/>'s world
	/// generator, as every script of the original does (tp.exe 0x00516330 through 0x0055398A and 0x005560CB); a value gives the script a private seeded generator (tests, standalone scripts).
	/// </summary>
	public int? Seed { get; init; }

	/// <summary>Instructions per slice. Default: the script header's time slice (50 in every corpus script).</summary>
	public int? SliceBudget { get; init; }

	/// <summary>Diagnostic name (archive member path).</summary>
	public string? SourceName { get; init; }
}

/// <summary>
/// Interpreter for compiled RSE scripts.
///
/// Scheduling: every call to <see cref="Advance"/> — one <see cref="FixedStepClock"/> tick in the game —
/// adds the elapsed time to the script clock and runs one slice (the cadence is an assumption: the original
/// runs a slice only on the passes the script's TURBO byte and id select, tp.exe 0x5516E1). The slice follows
/// tp.exe 0x551701..0x551736: the budget (the header time slice) falls per instruction while no critical
/// section is open, CRIT_UNLOCK and ENDSLICE zero it and a wait ends the slice; the budget is
/// what lets the corpus's busy-wait loops (e.g. Totem's passenger loop) give time back to the game.
/// Times (WAIT, GETTIME, SETTIMER) are milliseconds.
/// </summary>
public sealed partial class RideVM
{
	/// <summary>
	/// Stops a slice that never leaves a critical section.
	/// </summary>
	// [EXT:crit-slice-guard] the original has no such bound (a script that locks and never unlocks never ends its slice and hangs the game); the guard faults the script instead
	public const int MaximumInstructionsPerSlice = 100_000;

	/// <summary>Largest stack or limbo size accepted from a header (a managed array bound; the original allocates what the header says).</summary>
	public const int MaximumHeaderTableSize = 1 << 20;

	private readonly Instruction[] instructions;
	// [BIN:TPW-EXE:0x558930 script heap] one int array of the header stack size (script +0x20, element count +0x54) serves JSR/RETURN/PUSH/POP from the top (pointer +0x40 starts at size - 1 at 0x558bf5..0x558c04 and falls) and HUSH/HOP from the bottom (count +0x44 starts at 0); no handler checks the two ends against each other
	private readonly int[] stack;
	private int stackPointer;
	private int heapCount;
	// [BIN:TPW-EXE:0x558a01 limbo table] header limbo size slots of {value, expiry} (script +0x24, slot count +0x58, zero-filled), occupied count +0x60
	private readonly int[] limboValues;
	private readonly int[] limboExpiries;
	private int limboCount;
	// A private seeded .NET-compatible generator whose full state the park-resume snapshot can store; its draws
	// are exactly a seeded System.Random's (ScriptRandomTests proves it). Null for a script created without a seed:
	// it draws from the world generator (RideScriptWorld.World), whose state the world saves. The canonical hash takes the seed
	// plus the number of RNG calls, and marks a world-generator script.
	private ScriptRandom? random;
	private int seed;
	private long randomDraws;
	private readonly RideVMOptions options;
	private readonly Dictionary<Opcode, int> unimplementedEffects = new();
	private bool yieldRequested;
	private double timerEndMilliseconds;
	// [BIN:TPW-EXE:0x555B72 WALKST_FLOAT] script +0x80 (the start on the 32-bit clock), +0x84 (the duration in ms, 0 = not floating) and the two words at +0x88 and +0x8A; all zero at creation
	private uint walkFloatStart;
	private int walkFloatDuration;
	private int walkFloatWords;

	public RideScriptFile Script { get; }
	public string SourceName { get; }
	public string ScriptName { get; internal set; } = "Unnamed";
	public int ScriptId { get; internal set; }
	/// <summary>The constructor options (effects, world, resolver, seed) — children copy them when spawned and the restore reads them.</summary>
	internal RideVMOptions Options => options;
	/// <summary>The constructor seed (0 for a script that draws from the world generator); hashed by the canonical state.</summary>
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
	/// <summary>Return addresses on the shared stack/heap array: its top-down region, <c>size - 1 - pointer</c> (never negative).</summary>
	public int CallDepth => Math.Max( 0, stack.Length - 1 - stackPointer );

	/// <summary>Values HUSH has stored and HOP has not yet taken (the heap grows up from index 0 of the shared array).</summary>
	public int HeapCount => heapCount;

	/// <summary>The shared stack/heap array's size (header stack size).</summary>
	public int StackCapacity => stack.Length;

	/// <summary>One element of the shared stack/heap array (diagnostics and tests).</summary>
	public int GetStackSlot( int index ) => stack[index];

	/// <summary>The script's TURBO byte (script +0xB8): the low byte of TURBO's raw operand word; 0 at creation.</summary>
	// [BIN:TPW-EXE:0x5542B9 TURBO] stores cl (the low byte of the unresolved operand word, a variable operand stores its index byte) at +0xB8; the pass at 0x5516E1 reads the byte (nonzero = the script runs its slice on every pass, zero = only on the passes selected by +8 xor the pass counter & 7)
	public int TurboByte { get; internal set; }

	/// <summary>The script's DIPMUSIC byte (script +0xB9): the low byte of DIPMUSIC's operand; 0 at creation. A script that ends with it set clears the music duck request again (<see cref="Stop"/>).</summary>
	// [BIN:TPW-EXE:0x5564C0 DIPMUSIC] stores the low byte of the resolved operand at +0xB9, then passes the whole operand to 0x51E710, which keeps its low word as the global duck request (0x803AD0); the script destructor (0x558513) calls 0x51E710(0) when the byte is set, whatever other scripts requested
	public int MusicDuckByte { get; internal set; }

	/// <summary>The script's SETREVERB level (script +0xD8): the whole resolved operand; 0 at creation, which the reverb reader treats as "not set".</summary>
	// [BIN:TPW-EXE:0x55649C SETREVERB] stores the resolved operand as a dword at +0xD8 and nothing else; the reader is the main loop's 0x55A470 (ReverbSelector)
	public int ReverbLevel { get; internal set; }

	/// <summary>Occupied limbo slots (script +0x60).</summary>
	public int LimboCount => limboCount;

	/// <summary>The limbo table's slot count (header limbo size).</summary>
	public int LimboCapacity => limboValues.Length;

	/// <summary>One limbo slot: the held value (0 = free) and its expiry on the 32-bit scheduler clock.</summary>
	public (int Value, int Expiry) GetLimboSlot( int index ) => (limboValues[index], limboExpiries[index]);

	/// <summary>Script clock: milliseconds since this VM was created (waits, timers and their deadlines).</summary>
	public double TimeMilliseconds { get; private set; }

	/// <summary>The world's scheduler clock (<see cref="RideScriptWorld.ClockMilliseconds"/>) when this VM was created. The snapshot restore re-applies the saved origin.</summary>
	public double ClockOriginMilliseconds { get; internal set; }

	/// <summary>
	/// The shared scheduler clock as this script reads it: <see cref="ClockOriginMilliseconds"/> plus the script clock.
	/// The original's GETTIME and WAITANIM(_CH) call 0x10e844 on the global r30 (loaded at 0xaf55c) and subtract no
	/// creation timestamp.
	/// </summary>
	// [BIN:TPW-EXE:0x00551F1E GETTIME] the PC handler stores the clock read (0x402D70 on the app-lifetime scaled timer 0x785970, started once at 0x54DCF0, paused and resumed by 0x402ED0/0x402EA0, set only by the save writer 0x4164C0 and loader 0x415270, never reset for a new park) into its variable unchanged, and the shipped scripts only use GETTIME values as deadlines (ADD) or in a difference with a fresh read (SUB; RideClockCorpusTests), so the clock's origin cannot show there
	// [BIN:TPW-EXE:0x55320A WAITANIM_CH clock] the PC handler (jump table 0x5567d8, opcode 24) adds the clock (0x402D70 on 0x785970) to max(ret - 300, 300) (unsigned compare against 0x12c at 0x553348) as a wrapping 32-bit sum and stores it at +0xa0 (0x553371), so a clip under 300 ms gives a deadline that lies in the past only once the clock exceeds the shortfall; the original's clock is the time since program start where this world's starts at 0, which can only show for a WAITANIM_CH inside a park's first 134 ms, and none of the 308 Theme Park World and 282 Theme Park Inc scripts issues WAITANIM_CH (RideClockCorpusTests), so the origin cannot be observed (RIDES-044 retired)
	// [EXT:park-clock-origin] the scheduler clock starts at 0 when the park's script world is created (60 Hz fixed steps) where the original's is the time since program start, including the front end: a park's run depends only on its own script world and seed, never on how long the program ran before it, which keeps runs and replays deterministic
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
		seed = options.Seed ?? 0;
		ClockOriginMilliseconds = parent?.SchedulerMilliseconds ?? World.ClockMilliseconds;
		random = options.Seed is { } explicitSeed ? new ScriptRandom( explicitSeed ) : null;
		SliceBudget = options.SliceBudget ?? script.TimeSlice;
		if ( SliceBudget <= 0 )
			throw new InvalidDataException( $"RSE slice budget {SliceBudget} is not positive." );
		Variables = new int[script.VariableCount];
		// a size above the bound is refused up front (the original allocates whatever the header says); the corpus declares at most a few dozen
		if ( script.StackSize > MaximumHeaderTableSize || script.LimboSize > MaximumHeaderTableSize )
			throw new InvalidDataException( $"RSE stack size {script.StackSize} or limbo size {script.LimboSize} exceeds the VM limit {MaximumHeaderTableSize}." );
		stack = new int[Math.Max( 0, script.StackSize )];
		stackPointer = stack.Length - 1;
		limboValues = new int[Math.Max( 0, script.LimboSize )];
		limboExpiries = new int[limboValues.Length];

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
			RunSliceTail();
		}
		if ( StopChildrenIfFaulted() )
			return;
		Child?.Advance( deltaSeconds );
		if ( StopChildrenIfFaulted() )
			return;
		SoundChild?.Advance( deltaSeconds );
		StopChildrenIfFaulted();
	}

	/// <summary>
	/// One substep of the park's script pass for this script alone (the loop body of tp.exe <c>0x005516B0</c>): the shared script clock gains the substep, and the slice runs only on the passes the script's
	/// TURBO byte and id select. Children are scripts of their own in the pass (<see cref="RideScriptWorld.RunPass"/>), so none is advanced from here.
	/// </summary>
	// [BIN:TPW-EXE:0x005516E1 slice cadence] a script whose TURBO byte (+0xB8) is nonzero runs its slice in every pass, any other only when ((script id +8) xor the pass counter 0x8791A4) has its low three bits clear: one slice per eight passes, which pass by id
	internal void StepSubstep( double substepMilliseconds, long pass )
	{
		if ( State != RideVMState.Faulted )
		{
			TimeMilliseconds += substepMilliseconds;
			World.ObserveClock( SchedulerMilliseconds );
			if ( TurboByte != 0 || ((ScriptId ^ (int)pass) & 7) == 0 )
			{
				RunSlice();
				RunSliceTail();
			}
		}
		StopChildrenIfFaulted();
	}

	/// <summary>The loop body's tail after the slice: the object's controller updates (<see cref="IRideSliceTail"/>); a script that faulted in its slice takes none.</summary>
	// [BIN:TPW-EXE:0x5517D7 slice tail] after the slice loop (0x551719..0x55173C), on every pass the cadence 0x5516E1 selects, the loop body passes the script's words to its object's controllers whether or not instructions ran (a halted script reaches it through 0x5516FF): the speed word +0xC0 to the coaster in the slot +0xE0 (0x43B2B0, 0x5517EA), among others
	private void RunSliceTail()
	{
		if ( State != RideVMState.Faulted )
			( Effects as IRideSliceTail )?.AfterSlice( this );
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
		// [BIN:TPW-EXE:0x551701 slice start] the pass clears the global critical flag (0x87919C) before it hands the slice budget (+0x94, the header time slice) to the counter +0x98; the only reader is the loop below. A waiting script re-executes its wait in every slice, which also clears the flag
		InCriticalSection = false;
		if ( State == RideVMState.Waiting )
		{
			if ( TimeMilliseconds < WakeTimeMilliseconds )
				return;
			State = RideVMState.Running;
		}

		++SliceCount;
		yieldRequested = false;
		var executed = 0;
		var budget = SliceBudget;
		// [BIN:TPW-EXE:0x551719 slice loop] while the counter is above 0 and the pc is valid, one instruction runs (0x551cb0) and the counter falls by one only while the flag is 0 (0x551724..0x551730): CRIT_LOCK (0x551D44) sets the flag, so instructions from the lock on are free, and CRIT_UNLOCK (0x551D59) clears it and zeroes the counter (+0x98), ENDSLICE (0x5542A8) zeroes it too, and a wait rewinds the pc and ends the slice
		while ( !yieldRequested && State == RideVMState.Running && budget > 0 )
		{
			if ( ProgramCounter >= instructions.Length )
			{
				State = RideVMState.Halted;
				break;
			}
			if ( executed >= MaximumInstructionsPerSlice )
			{
				Fault( $"slice exceeded {MaximumInstructionsPerSlice} instructions inside a critical section" );
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
			if ( !InCriticalSection )
				--budget;
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

	/// <summary>END: the program counter becomes -10000 and the script ends without a fault.</summary>
	internal void EndScript()
	{
		ProgramCounter = -10000;
		State = RideVMState.Halted;
		// [BIN:TPW-EXE:0x551ABC script end] the pass destroys a script whose program counter went negative; its destructor (0x558500) ends the records the script still holds
		( Effects as IRideScriptLifetime )?.ScriptDestroyed( this );
	}

	/// <summary>Sends the music duck request word to the host's sound service (the original's 0x51E710): a presentation effect, never part of the simulation.</summary>
	internal void RequestMusicDuck( int word ) =>
		Effect( Opcode.DIPMUSIC, new[] { new Operand( this, new RideScriptOperand( RideScriptOperandKind.Literal, unchecked((ushort)word) ), -1 ) } );

	/// <summary>Stops this script and its children and removes them from the world.</summary>
	public void Stop()
	{
		RemoveChild();
		RemoveSoundChild();
		// [BIN:TPW-EXE:0x558513 script destructor] a script whose DIPMUSIC byte is set writes the duck request 0 when it is destroyed
		if ( MusicDuckByte != 0 )
		{
			MusicDuckByte = 0;
			RequestMusicDuck( 0 );
		}
		World.Unregister( this );
		if ( State != RideVMState.Faulted )
			State = RideVMState.Halted;
		( Effects as IRideScriptLifetime )?.ScriptDestroyed( this );
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
			RandomState = random?.CaptureState(),
			UsesWorldRandom = random == null,
			RandomDraws = randomDraws,
			State = State,
			FaultMessage = FaultMessage,
			ProgramCounter = ProgramCounter,
			Stack = (int[])stack.Clone(),
			StackPointer = stackPointer,
			HeapCount = heapCount,
			LimboValues = (int[])limboValues.Clone(),
			LimboExpiries = (int[])limboExpiries.Clone(),
			LimboCount = limboCount,
			TurboByte = TurboByte,
			MusicDuckByte = MusicDuckByte,
			ReverbLevel = ReverbLevel,
			Variables = (int[])Variables.Clone(),
			Flags = Flags,
			SliceBudget = SliceBudget,
			InCriticalSection = InCriticalSection,
			YieldRequested = yieldRequested,
			TimeMilliseconds = TimeMilliseconds,
			ClockOriginMilliseconds = ClockOriginMilliseconds,
			WakeTimeMilliseconds = WakeTimeMilliseconds,
			TimerEndMilliseconds = timerEndMilliseconds,
			WalkFloatStart = walkFloatStart == 0 ? null : (int)walkFloatStart,
			WalkFloatDuration = walkFloatDuration == 0 ? null : walkFloatDuration,
			WalkFloatWords = walkFloatWords == 0 ? null : walkFloatWords,
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
		var restoredStack = snapshot.ResolveStack( stack.Length, instructions.Length, WordOffsetOf, out var restoredPointer, out var restoredHeapCount );
		if ( snapshot.LimboValues != null && (snapshot.LimboValues.Length != limboValues.Length || snapshot.LimboExpiries?.Length != limboValues.Length) )
			throw new InvalidDataException( $"script '{ScriptName}' declares limbo size {limboValues.Length}, the snapshot stores {snapshot.LimboValues.Length} slots." );
		if ( snapshot.LimboValues == null && snapshot.LimboCount != 0 )
			throw new InvalidDataException( $"script '{ScriptName}' has no limbo table in the snapshot but counts {snapshot.LimboCount} held values." );
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
		Array.Copy( restoredStack, stack, stack.Length );
		stackPointer = restoredPointer;
		heapCount = restoredHeapCount;
		if ( snapshot.LimboValues != null )
		{
			Array.Copy( snapshot.LimboValues, limboValues, limboValues.Length );
			Array.Copy( snapshot.LimboExpiries!, limboExpiries, limboValues.Length );
		}
		else
		{
			Array.Clear( limboValues );
			Array.Clear( limboExpiries );
		}
		limboCount = snapshot.LimboCount;
		TurboByte = snapshot.TurboByte;
		ReverbLevel = snapshot.ReverbLevel;
		// a resumed park hears the duck its script asked for (the request word is not saved; the script's byte is, and the request was the operand, 1 in every shipped script)
		MusicDuckByte = snapshot.MusicDuckByte;
		if ( MusicDuckByte != 0 )
			RequestMusicDuck( MusicDuckByte );
		Array.Copy( snapshot.Variables, Variables, Variables.Length );
		Flags = snapshot.Flags;
		InCriticalSection = snapshot.InCriticalSection;
		yieldRequested = snapshot.YieldRequested;
		TimeMilliseconds = snapshot.TimeMilliseconds;
		ClockOriginMilliseconds = snapshot.ClockOriginMilliseconds;
		WakeTimeMilliseconds = snapshot.WakeTimeMilliseconds;
		timerEndMilliseconds = snapshot.TimerEndMilliseconds;
		walkFloatStart = unchecked( (uint)(snapshot.WalkFloatStart ?? 0) );
		walkFloatDuration = snapshot.WalkFloatDuration ?? 0;
		walkFloatWords = snapshot.WalkFloatWords ?? 0;
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
		// the snapshot decides how this script draws: a script of the world generator has no state of its own to restore (the world restores the generator)
		if ( snapshot.UsesWorldRandom )
		{
			seed = 0;
			random = null;
		}
		else
		{
			seed = snapshot.Seed;
			random = new ScriptRandom( snapshot.Seed );
			random.RestoreState( snapshot.RandomState! );
		}
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

	/// <summary>The tag the original puts on a stored return address (high byte 0x20, the branch operand tag).</summary>
	private const int ReturnAddressTag = 0x20000000;

	/// <summary>
	/// JSR: the return address goes onto the shared array at the stack pointer, which then falls; the jump is taken either way.
	/// </summary>
	// [BIN:TPW-EXE:0x5539A9 JSR] stores (pc after the operand | 0x20000000; the pc +0x3C is a code-word index, so the stored word is the word offset of the next instruction) at heap[+0x40] and decrements +0x40 only while 0 <= +0x40 < size; otherwise the assert (0x5DA3C0 is a bare ret) does nothing and the jump to the operand still runs (0x553A0A..0x553A24), so a full stack jumps without pushing and a later RETURN comes back to an older address
	internal void PushReturn( Operand location )
	{
		var target = location.RequireBranchTarget();
		if ( stackPointer >= 0 && stackPointer < stack.Length )
			stack[stackPointer--] = WordOffsetOf( ProgramCounter ) | ReturnAddressTag;
		ProgramCounter = target;
	}

	/// <summary>
	/// RETURN: pops the shared array; a popped word tagged as an address becomes the pc, any other word (a HUSH value that reached the call region) leaves the pc alone.
	/// </summary>
	// [BIN:TPW-EXE:0x553A32 RETURN] with a heap and -1 <= +0x40 < size - 1 the pointer rises and heap[+0x40] is read; otherwise the pc becomes -10000 (script dead, here a fault). Only a word whose high byte is 0x20 is jumped to (xor 0x20000000, 0x553A66..0x553A7C); the rest returns without touching the pc
	internal void PopReturn()
	{
		if ( stackPointer < -1 || stackPointer >= stack.Length - 1 )
			throw new RideScriptException( "RETURN without JSR" );
		var word = stack[++stackPointer];
		if ( (word & unchecked( (int)0xFF000000 )) == ReturnAddressTag )
		{
			var target = word ^ ReturnAddressTag;
			var index = target == Script.CodeWordCount ? instructions.Length : Script.GetInstructionIndexAtWord( target );
			if ( index < 0 )
				throw new RideScriptException( $"RETURN to code word {target}, which does not start an instruction" );
			ProgramCounter = index;
		}
	}

	/// <summary>The code-word offset of instruction <paramref name="index"/> (the original's pc), the code-word count past the last instruction.</summary>
	internal int WordOffsetOf( int index ) => index >= 0 && index < instructions.Length ? instructions[index].WordOffset : Script.CodeWordCount;

	/// <summary>
	/// PUSH: stores the value at the stack pointer and lowers it; true when stored. A full stack kills the script and still sets the result register.
	/// </summary>
	// [BIN:TPW-EXE:0x553C1E PUSH] heap[+0x40] = value, +0x40--, result = value while 0 <= +0x40 < size; otherwise assert (no-op), pc = -10000 and result = value
	internal bool PushValue( int value )
	{
		SetFlags( value );
		if ( stackPointer < 0 || stackPointer >= stack.Length )
			return false;
		stack[stackPointer--] = value;
		return true;
	}

	/// <summary>POP: raises the pointer and returns the word it reaches; false on an empty stack (the result is 0 and the script dies).</summary>
	// [BIN:TPW-EXE:0x553CBA POP] with -1 <= +0x40 < size - 1: +0x40++, value = heap[+0x40]; otherwise the value is 0 and pc = -10000; either way the shared tail 0x555939 writes the result register and a variable destination
	internal bool PopValue( out int value )
	{
		if ( stackPointer < -1 || stackPointer >= stack.Length - 1 )
		{
			value = 0;
			return false;
		}
		value = stack[++stackPointer];
		return true;
	}

	/// <summary>HUSH: appends to the heap and sets the result register; a full heap does nothing at all.</summary>
	// [BIN:TPW-EXE:0x553D25 HUSH] heap[+0x44] = value, result (+0x48) = value, +0x44++ while 0 <= +0x44 < size (+0x54); on overflow the assert is a no-op and neither the array nor the result register changes
	internal void HeapPush( int value )
	{
		if ( heapCount < 0 || heapCount >= stack.Length )
			return;
		stack[heapCount++] = value;
		SetFlags( value );
	}

	/// <summary>
	/// Seeds the heap of a script restored from a save that predates the heap with the values its bridge held for it (legacy migration), given in the order HOP is to return them (the first ends on top), without touching the flags;
	/// returns how many fit (the heap only grows into the room the call region leaves). Does nothing unless heap and limbo table are empty.
	/// </summary>
	internal int SeedLegacyHeap( IReadOnlyList<int> nextFirst )
	{
		if ( heapCount != 0 )
			return 0;
		var room = Math.Max( 0, stackPointer + 1 );
		var count = Math.Min( room, nextFirst.Count );
		for ( var index = count - 1; index >= 0; index-- )
			stack[heapCount++] = nextFirst[index];
		return count;
	}

	/// <summary>Fills the limbo table of a script restored from a save that predates it (legacy migration): the first slots take the entries; returns how many fit. Does nothing unless the table is empty.</summary>
	internal int SeedLegacyLimbo( IReadOnlyList<(int Value, int Expiry)> entries )
	{
		if ( limboCount != 0 || limboValues.Any( value => value != 0 ) )
			return 0;
		var count = Math.Min( limboValues.Length, entries.Count );
		for ( var index = 0; index < count; index++ )
		{
			limboValues[index] = entries[index].Value;
			limboExpiries[index] = entries[index].Expiry;
		}
		limboCount = count;
		return count;
	}

	/// <summary>HOP: takes the newest heap value (LIFO); false when the heap is empty (nothing changes).</summary>
	// [BIN:TPW-EXE:0x553DAA HOP] with +0x44 > 0 (the +0x40 <= size test at 0x553DE7 cannot fail): +0x44--, value = heap[+0x44], then the shared tail 0x555939 sets the result register and writes a variable destination; an empty heap asserts (no-op) and leaves the result register and the destination alone
	internal bool HeapPop( out int value )
	{
		if ( heapCount <= 0 )
		{
			value = 0;
			return false;
		}
		value = stack[--heapCount];
		return true;
	}

	/// <summary>Current 32-bit scheduler clock as the signed word the limbo table stores.</summary>
	private int LimboClock => unchecked( (int)ClockMilliseconds32 );

	/// <summary>LIMBO: the first slot holding 0 takes the value and an expiry <c>now + seconds * 1000</c>; 1 when stored, 0 when the table is full or empty.</summary>
	// [BIN:TPW-EXE:0x554DE5 LIMBO] size (+0x58) <= 0: result 0. Otherwise the first slot whose value word is 0 (0x554E8E) gets the value and expiry = clock (0x402D70 on timer 0x785970, as GETTIME) + seconds * 1000 as a wrapping 32-bit sum (0x554EC3..0x554ECC, 125 * 8), count (+0x60)++ and result 1; no free slot: result 0, nothing stored. A value of 0 leaves its slot "free" but still counts
	internal int Limbo( int value, int seconds )
	{
		for ( var index = 0; index < limboValues.Length; index++ )
		{
			if ( limboValues[index] != 0 )
				continue;
			limboValues[index] = value;
			limboExpiries[index] = unchecked( LimboClock + seconds * 1000 );
			++limboCount;
			return 1;
		}
		return 0;
	}

	/// <summary>UNLIMBO: releases the first occupied slot whose expiry lies strictly before the clock (signed 32-bit); 0 when none.</summary>
	// [BIN:TPW-EXE:0x554EF0 UNLIMBO] slots in index order; an occupied one (value != 0) whose expiry is below the clock as signed words (jl at 0x554F3A) is released through 0x5567BF: count--, the value word cleared in place (the expiry stays), result = the value
	internal int Unlimbo()
	{
		var now = LimboClock;
		for ( var index = 0; index < limboValues.Length; index++ )
		{
			if ( limboValues[index] == 0 || limboExpiries[index] >= now )
				continue;
			return Release( index );
		}
		return 0;
	}

	/// <summary>FORCEUNLIMBO: releases the first occupied slot whatever its expiry; 0 when none.</summary>
	// [BIN:TPW-EXE:0x554F74 FORCEUNLIMBO] needs a variable destination (any other operand returns before anything happens, 0x554FA6); the first slot with value != 0 (0x554FBA) is released as UNLIMBO does (0x554FC9..0x554FE8), none gives 0; the result register and the variable get the value
	internal int ForceUnlimbo()
	{
		for ( var index = 0; index < limboValues.Length; index++ )
			if ( limboValues[index] != 0 )
				return Release( index );
		return 0;
	}

	private int Release( int index )
	{
		--limboCount;
		var value = limboValues[index];
		limboValues[index] = 0;
		return value;
	}

	// [BIN:TPW-EXE:0x555012 INLIMBO] the result is the count (+0x60); [BIN:TPW-EXE:0x555045 LIMBOSPACE] the result is size (+0x58) - count (+0x60), negative for a table overfilled by LIMBO 0
	internal int LimboOccupied => limboCount;
	internal int LimboFree => limboValues.Length - limboCount;

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
		return random != null ? random.Next( 0, maximumInclusive + 1 ) : World.ScriptRand( maximumInclusive );
	}

	internal void SetTimer( int milliseconds ) => timerEndMilliseconds = TimeMilliseconds + milliseconds;

	/// <summary>The float state (script +0x80 start, +0x84 duration, +0x88 and +0x8A words) for tests and snapshots.</summary>
	public (uint Start, int Duration, int Words) WalkFloat => (walkFloatStart, walkFloatDuration, walkFloatWords);

	/// <summary>
	/// WALKST_FLOAT (<c>0x555B72</c> → 0x557160): the start is the clock now, the duration the first operand times 1000 as a wrapping 32-bit product, the two words the low 16 bits of the other operands.
	/// </summary>
	internal void StartWalkFloat( int seconds, int first, int second )
	{
		walkFloatStart = ClockMilliseconds32;
		walkFloatDuration = unchecked( seconds * 1000 );
		walkFloatWords = (first & 0xFFFF) | ((second & 0xFFFF) << 16);
	}

	/// <summary>
	/// The duration word WALKFLOATSTAT reads (<c>0x555BD8</c>). The original's frame update (0x00557D80, last block) clears it once the clock is unsigned-past start + duration (<c>jae</c> at 0x558075); the VM applies that here, when the script looks.
	/// </summary>
	internal int WalkFloatDurationNow()
	{
		if ( walkFloatDuration != 0 && unchecked( walkFloatStart + (uint)walkFloatDuration ) < ClockMilliseconds32 )
			walkFloatDuration = 0;
		return walkFloatDuration;
	}

	/// <summary>
	/// WALKFLOATSTOP (<c>0x555C11</c>): with a duration set, the share of it spent so far in thousandths (the 32-bit product of the clock difference and 1000, divided unsigned by the duration) moves the start back from now by that share
	/// and the duration becomes 1000, so the float settles over the thousandths that were left; with none set it does nothing.
	/// </summary>
	internal void StopWalkFloat()
	{
		if ( WalkFloatDurationNow() == 0 )
			return;
		var now = ClockMilliseconds32;
		var spent = unchecked( now * 1000u - walkFloatStart * 1000u ) / (uint)walkFloatDuration;
		walkFloatDuration = 1000;
		walkFloatStart = unchecked( now - spent );
	}

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
	// [BIN:TPW-EXE:0x4af440 ride window speed] the ride window's speed slider (0x3e30, MinSpeed..MaxSpeed) sets the bias through SetSpeed 0x4dd6e0 (0x55a300) when the window applies on closing or stepping (OriginalObjectRuntime.ApplyRideWindow)
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
		// the call region of the shared array (top-down) is hashed as before; the heap, any word the two regions share, the limbo table and the TURBO byte are added only when a script has used them, so scripts that do not keep their earlier digests
		var depth = CallDepth;
		hash.Add( depth );
		for ( var index = stackPointer + 1; depth > 0 && index < stack.Length; index++ )
			hash.Add( stack[index] );
		if ( heapCount != 0 )
		{
			hash.Add( heapCount );
			for ( var index = 0; index < heapCount; index++ )
				hash.Add( stack[index] );
		}
		if ( limboCount != 0 || limboValues.Any( x => x != 0 ) )
		{
			hash.Add( limboCount );
			for ( var index = 0; index < limboValues.Length; index++ )
			{
				hash.Add( limboValues[index] );
				hash.Add( limboExpiries[index] );
			}
		}
		if ( TurboByte != 0 )
			hash.Add( TurboByte );
		// the float state only for a script that has started a float (ZeroG), so every other script keeps its digest
		if ( walkFloatDuration != 0 || walkFloatStart != 0 || walkFloatWords != 0 )
		{
			hash.Add( walkFloatStart );
			hash.Add( walkFloatDuration );
			hash.Add( walkFloatWords );
		}
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
		// a script of the world generator is told from one with the seed 0 (the park's generator state is hashed once, with the world)
		if ( random == null )
			hash.Add( true );
		// The object state a root script's animation opcodes read back (GETANIM, TRIGWAITANIM); children share it.
		hash.Add( Parent == null && Effects is IRideCanonicalEffects );
		if ( Parent == null && Effects is IRideCanonicalEffects canonical )
			canonical.AddCanonicalState( hash );
	}

	/// <summary>The seed of a spawned child: drawn from this script's own generator, or null (the world generator) for a script that has none, as the original's children are not seeded.</summary>
	private int? NextChildSeed()
	{
		if ( random == null )
			return null;
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

namespace OpenTPW;

/// <summary>Semantic identities of the bounded staff instruction graphs, never executable addresses.</summary>
public enum StaffSpriteProgram
{
	Walk = 1, Run = 2, Stand = 3, Patrol = 9, Research = 10,
	Sweep = 17, Repair = 18, Strike = 19, State6 = 20, Dangling = 21, Catch = 22, Hold = 23,
	Template24 = 24, Template25 = 25,
	/// <summary>The hire window's figure script (tp.exe <c>0x0074F758</c>, created by <c>0x0049BA00</c>): not a template of the table <c>0x0075A418</c>.</summary>
	HireFigure = 30,
	Performance0 = 256, Performance1 = 257, Performance2 = 258, Performance3 = 259,
	Performance0Loop = 272, Performance1Loop = 273, Performance2Loop = 274, Performance3Loop = 275
}

/// <summary>
/// A complete semantic program/instruction identity in one word. Stack entries retain their own program identity
/// when a portrait refresh replaces only CurrentPc. This encoding is not the native save's word offset or a save format.
/// </summary>
public readonly record struct StaffSpritePc( uint Word )
{
	public StaffSpritePc( StaffSpriteProgram program, int instruction ) : this( Encode( program, instruction ) ) { }
	public StaffSpriteProgram Program => (StaffSpriteProgram)(Word >> 16);
	public int Instruction => (int)(Word & 0xFFFF);
	internal StaffSpritePc Next => new( Program, checked( Instruction + 1 ) );

	private static uint Encode( StaffSpriteProgram program, int instruction )
	{
		if ( (uint)program > ushort.MaxValue || (uint)instruction > ushort.MaxValue )
			throw new ArgumentOutOfRangeException( nameof( instruction ), "A semantic PC requires a 16-bit program and instruction." );
		return ((uint)program << 16) | (uint)instruction;
	}
}

public enum StaffSpriteRunState { Running = 1, Yielded = 2, Stopped = 3, Ended = 4 }
public enum StaffSpriteRegister
{
	Scratch, X, Y, Z, RotationX, RotationY, RotationZ, Alpha, PrimaryScale, SecondaryScale,
	Kind, Variant, Slot, Frame, FrameCount, Heading, Flags, Intro, Hold
}

/// <summary>
/// Detached, mutable, GPU-free state for the complete sprite record. Arrays are owned by this instance.
/// Unused words are preserved for copy invariance; host construction zeroes unspecified words for safety.
/// No renderer, registry enrollment, source lifecycle or persistence is implied by this value.
/// </summary>
public sealed class StaffSpriteVmState
{
	public const int RegisterCount = 19;
	public const int StackCapacity = 20;
	public const int DrawCacheWordCount = 17;
	public const uint BuiltinProgramTable = 1;

	public uint OpaqueWord { get; set; }
	public uint Handle { get; set; }
	public StaffSpritePc CurrentPc { get; set; }
	public StaffSpritePc RestartPc { get; set; }
	public StaffSpritePc LastPc { get; set; }
	public uint ProgramTable { get; set; } = BuiltinProgramTable;
	public StaffSpriteRunState RunState { get; set; } = StaffSpriteRunState.Running;
	public int StackPointer { get; set; } = StackCapacity;
	/// <summary>Descending stack of complete semantic PC words. Inactive words are retained, not cleared on pop/restart.</summary>
	public uint[] Stack { get; private set; } = new uint[StackCapacity];
	public int IfDepth { get; set; }
	public int CallDepth { get; set; }
	public int LoopDepth { get; set; }
	public int Due { get; set; }
	public int Interval { get; set; } = 62;
	/// <summary>Raw 32-bit words; only registers 1 through 6 use float SET/ADD arithmetic.</summary>
	public uint[] Registers { get; private set; } = new uint[RegisterCount];
	/// <summary>Opaque derived draw words. The pure VM preserves them; later rendering integration must rebuild them.</summary>
	public uint[] DrawCache { get; private set; } = new uint[DrawCacheWordCount];
	public uint OutputValid { get; set; }
	public bool HasOutput => OutputValid != 0;

	public uint this[StaffSpriteRegister register]
	{
		get => Registers[(int)register];
		set => Registers[(int)register] = value;
	}

	/// <summary>A complete value copy, including inactive stack and cache words.</summary>
	public StaffSpriteVmState DeepCopy()
	{
		var copy = (StaffSpriteVmState)MemberwiseClone();
		copy.Stack = (uint[])Stack.Clone();
		copy.Registers = (uint[])Registers.Clone();
		copy.DrawCache = (uint[])DrawCache.Clone();
		return copy;
	}
}

/// <summary>Exact full/partial portrait copy operations, without window ownership or registry allocation.</summary>
public static class StaffSpriteCloneCopy
{
	// [BIN:TPW-EXE:0x00475CF0 full sprite copy] all record words are copied before handle, XYZ, primary scale and flags are replaced
	public static StaffSpriteVmState Create( StaffSpriteVmState source, uint cloneHandle, uint x, uint y, uint z, uint primaryScale )
	{
		ArgumentNullException.ThrowIfNull( source );
		var clone = source.DeepCopy();
		clone.Handle = cloneHandle;
		clone[StaffSpriteRegister.X] = x;
		clone[StaffSpriteRegister.Y] = y;
		clone[StaffSpriteRegister.Z] = z;
		clone[StaffSpriteRegister.PrimaryScale] = primaryScale;
		clone[StaffSpriteRegister.Flags] = PortraitFlags( clone[StaffSpriteRegister.Flags] );
		return clone;
	}

	// [BIN:TPW-EXE:0x00475E10 partial sprite copy] exact allowlist; flags mask the destination's existing word, never source flags
	public static void Refresh( StaffSpriteVmState clone, StaffSpriteVmState source )
	{
		ArgumentNullException.ThrowIfNull( clone );
		ArgumentNullException.ThrowIfNull( source );
		clone.ProgramTable = source.ProgramTable;
		clone.CurrentPc = source.CurrentPc;
		clone[StaffSpriteRegister.Kind] = source[StaffSpriteRegister.Kind];
		clone[StaffSpriteRegister.Variant] = source[StaffSpriteRegister.Variant];
		clone[StaffSpriteRegister.Slot] = source[StaffSpriteRegister.Slot];
		clone[StaffSpriteRegister.Intro] = source[StaffSpriteRegister.Intro];
		clone[StaffSpriteRegister.Hold] = source[StaffSpriteRegister.Hold];
		clone[StaffSpriteRegister.Alpha] = source[StaffSpriteRegister.Alpha];
		clone[StaffSpriteRegister.SecondaryScale] = source[StaffSpriteRegister.SecondaryScale];
		clone[StaffSpriteRegister.Heading] = source[StaffSpriteRegister.Heading];
		clone.OutputValid = source.OutputValid;
		clone[StaffSpriteRegister.Frame] = source[StaffSpriteRegister.Frame];
		clone[StaffSpriteRegister.FrameCount] = source[StaffSpriteRegister.FrameCount];
		clone[StaffSpriteRegister.Flags] = PortraitFlags( clone[StaffSpriteRegister.Flags] );
	}

	private static uint PortraitFlags( uint existing ) => (existing & ~0x200u) | 0x50000u;
}

namespace OpenTPW;

/// <summary>
/// The sprite banks the staff sources read when a performance template is applied: the bank's animation file of a role and variant (null where none is loaded, as in a headless run).
/// </summary>
public interface IStaffSpriteBanks
{
	SpriteAnimationFile? AnimationsOf( StaffType type, int variantKey );

	/// <summary>How many banks the role's folder has loaded (tp.exe <c>0x00541F60</c>: the table entry <c>0x00763F90</c> of the sprite category), 0 for none.</summary>
	int BankCount( StaffType type );
}

/// <summary>A saved staff sprite source: the complete control and clock words of the sprite record, without the opaque draw cache (rebuilt when it is drawn).</summary>
/// <param name="Stack">The 20 stack words, complete semantic PC words (<see cref="StaffSpritePc"/>), also the inactive ones.</param>
/// <param name="Registers">The 19 register words (<see cref="StaffSpriteRegister"/>).</param>
public sealed record StaffSpriteVmSnapshot( uint CurrentPc, uint RestartPc, uint LastPc, int RunState, int StackPointer, IReadOnlyList<uint> Stack, int IfDepth, int CallDepth, int LoopDepth,
	int Due, int Interval, IReadOnlyList<uint> Registers, uint OutputValid )
{
	/// <summary>Value equality, the two word lists by their words.</summary>
	public bool Equals( StaffSpriteVmSnapshot? other ) => other != null && (CurrentPc, RestartPc, LastPc, RunState, StackPointer, IfDepth, CallDepth, LoopDepth, Due, Interval, OutputValid)
		== (other.CurrentPc, other.RestartPc, other.LastPc, other.RunState, other.StackPointer, other.IfDepth, other.CallDepth, other.LoopDepth, other.Due, other.Interval, other.OutputValid)
		&& Stack.SequenceEqual( other.Stack ) && Registers.SequenceEqual( other.Registers );

	public override int GetHashCode() => HashCode.Combine( CurrentPc, RestartPc, LastPc, RunState, Due, Interval, OutputValid );
}

/// <summary>A saved staff member's sprite: the wrapper's two pending words and the live source, if any.</summary>
public sealed record StaffSpriteStateSnapshot( int PendingTemplate, int PendingInterval, StaffSpriteVmSnapshot? Source );

/// <summary>
/// A staff member's sprite wrapper and its live source (tp.exe: the person's words <c>+0xC</c> handle, <c>+0x10</c> pending template, <c>+0x14</c> pending interval, <c>0x004D3ED0</c>, and the sprite record
/// the handle names in the registry <c>0x007B49F0</c>). The source is the bounded sprite VM (<see cref="StaffSpriteVm"/>) over the decoded staff programs; its passes are the sprite scheduler's
/// (<see cref="Tick"/>, tp.exe <c>0x00475360</c>), which the level runs on the even 31 ms substeps. Presentation only: neither the wrapper nor the source is hashed.
/// </summary>
public sealed class StaffSpriteState
{
	/// <summary>The sprite category of a role (tp.exe <c>0x00763F88</c>: the person's <c>+0x24</c>), the source's kind word.</summary>
	// [BIN:TPW-EXE:0x00763F88 sprite categories] entertainers 4, handymen 5, mechanics 6, guards 7, researchers 8 (0x004D4250, 0x004D6B60, 0x004D9DB0, 0x004D5DE0, 0x00502600 store it at +0x24 before creating the sprite)
	public static int CategoryOf( StaffType type ) => type switch
	{
		StaffType.Entertainer => 4,
		StaffType.Handyman => 5,
		StaffType.Mechanic => 6,
		StaffType.Guard => 7,
		_ => 8
	};

	/// <summary>The first template number of the performance entries (13 to 16 are the bank's entries 0 to 3, the table <c>0x0075A418</c> slots 13 to 16 hold the entry indices 0 to 3).</summary>
	public const int FirstPerformanceTemplate = 13;

	/// <summary>Whether a template number is one the staff programs know (the wrapper's pending word).</summary>
	public static bool IsKnownTemplate( int template ) => template is 1 or 2 or 3 or 9 or 10 or (>= 13 and <= 16) or (>= 17 and <= 25);

	/// <summary>The program a non-performance template number runs, null for another number.</summary>
	public static StaffSpriteProgram? ProgramOf( int template ) => template switch
	{
		1 or 2 or 3 or 9 or 10 or (>= 17 and <= 25) => (StaffSpriteProgram)template,
		_ => null
	};

	/// <summary>The live source (the sprite record the person's handle names), null without a handle.</summary>
	public StaffSpriteVmState? Source { get; private set; }
	/// <summary>The wrapper's pending template (<c>+0x10</c>), 0 for none.</summary>
	public int PendingTemplate { get; private set; }
	/// <summary>The wrapper's pending interval (<c>+0x14</c>), 0 for none.</summary>
	public int PendingInterval { get; private set; }

	/// <summary>Whether the source shows a frame (the sprite pass draws a sprite whose output word is set).</summary>
	public bool HasOutput => Source is { HasOutput: true };

	/// <summary>Creates the source in <paramref name="template"/> at the host clock sample <paramref name="nowMilliseconds"/>: no frame yet, the interval 62 ms and the first due time 62 ms later.</summary>
	// [BIN:TPW-EXE:0x004D4140 sprite creation] 0x475A10 builds the record (0x4758F0: output not valid, the program's start as current and restart PC, interval +0x80 = 0x3E, due +0x7C = clock + interval, alpha 255, both scales 1) and stores the handle in the person's +0xC; the pending words are not touched
	public void Create( StaffType type, int variantKey, int template, int nowMilliseconds, IStaffSpriteBanks? banks )
	{
		var program = ProgramOf( template ) ?? throw new ArgumentOutOfRangeException( nameof( template ) );
		var source = StaffSpriteVm.Create( program, nowMilliseconds );
		source[StaffSpriteRegister.Kind] = (uint)CategoryOf( type );
		source[StaffSpriteRegister.Variant] = unchecked( (uint)variantKey );
		// 0x4758F0 stores the frame count of the slot it is given (0x540C60: the bank's byte of the slot's grid); the slot is 0 for the staff's creation
		source[StaffSpriteRegister.FrameCount] = (uint)FramesOfSlot( banks?.AnimationsOf( type, variantKey ), 0 );
		Source = source;
	}

	/// <summary>Deletes the source and zeroes the handle; the pending words stay.</summary>
	// [BIN:TPW-EXE:0x004D4170 sprite deletion] 0x475550 frees the record, the handle word becomes 0 and the wrapper's pending template and interval are not cleared
	public void Delete() => Source = null;

	/// <summary>Queues a template for the next pre-update (<c>0x004217F0</c>: stores the number at <c>+0x10</c>).</summary>
	public void QueueTemplate( int template )
	{
		if ( !IsKnownTemplate( template ) )
			throw new ArgumentOutOfRangeException( nameof( template ) );
		PendingTemplate = template;
	}

	/// <summary>Queues an interval for the next pre-update (<c>0x004D41D0</c>: the value, clamped to 0 to 250, at <c>+0x14</c>); 0 asks for no change.</summary>
	public void QueueInterval( int interval ) => PendingInterval = Math.Clamp( interval, 0, SpritePlayer.LongestInterval );

	/// <summary>
	/// Whether the source's restart PC is the start of <paramref name="template"/>'s program (<c>0x00475C50</c>): the walk step queues a walk again unless it is. The current PC and the pending request are not
	/// compared, and a person without a handle compares false.
	/// </summary>
	public bool RestartsAt( int template ) =>
		Source != null && ProgramOf( template ) is { } program && Source.RestartPc == new StaffSpritePc( program, 0 );

	/// <summary>
	/// The pre-update's application of the pending words (<c>0x004D4190</c>, from the staff pre-update 0x00505490 through 0x004FA870): the template first, then the interval, each cleared even when the person has
	/// no source (the underlying 0x00475B80 and 0x00475610 ignore a zero handle).
	/// </summary>
	public void ApplyPending( StaffType type, int variantKey, IStaffSpriteBanks? banks )
	{
		if ( PendingTemplate != 0 )
		{
			if ( Source != null )
				ApplyTemplate( Source, PendingTemplate, type, variantKey, banks );
			PendingTemplate = 0;
		}
		if ( PendingInterval != 0 )
		{
			if ( Source != null )
				StaffSpriteVm.ApplyInterval( Source, PendingInterval );
			PendingInterval = 0;
		}
	}

	// [BIN:TPW-EXE:0x00475B80 template replacement] a number 0 to 3 in the table is a bank performance entry: 0x00540C70 reads its program, slot (stored at +0xB4) and the frame count of the slot (0x00540C60, +0xBC) from the bank of the sprite's kind and variant, the entry's intro and hold bytes go to locals and are not stored (they are written only when a sprite is created with such an entry, 0x00475A10, which no staff creation does), then the program becomes the current and restart PC and the stack and three depths are cleared
	private static void ApplyTemplate( StaffSpriteVmState source, int template, StaffType type, int variantKey, IStaffSpriteBanks? banks )
	{
		if ( template is >= FirstPerformanceTemplate and < FirstPerformanceTemplate + 4 )
		{
			var entry = template - FirstPerformanceTemplate;
			var animations = banks?.AnimationsOf( type, variantKey );
			var performance = animations != null && entry < animations.Performances.Count ? animations.Performances[entry] : (SpritePerformance?)null;
			var program = performance is { Program: >= 0 and <= 3 } ? performance.Value.Program : 0;
			var slot = performance is { Slot: >= 0 and < SpriteAnimationFile.SlotCount } ? performance.Value.Slot : StaffSpriteRules.DefaultPerformanceSlot;
			// the frame count is the bank's own, stored as it is (0x00475C16, 0x00475C1B); the core refuses 0, which only a slot without metadata (no bank loaded) gives, and then 1 stands in
			var frames = FramesOfSlot( animations, slot );
			StaffSpriteVm.ApplyPerformance( source, program, slot, frames > 0 ? frames : 1 );
			return;
		}
		StaffSpriteVm.ApplyTemplate( source, ProgramOf( template )!.Value );
	}

	private static int FramesOfSlot( SpriteAnimationFile? animations, int slot ) =>
		animations != null && slot >= 0 && slot < animations.Animations.Count ? animations.Animations[slot].FramesPerDirection : 0;

	/// <summary>One scheduler opportunity at the host clock sample (<c>0x00475360</c>): the source runs when the sample is past its due time.</summary>
	public StaffSpriteTickResult Tick( int nowMilliseconds ) =>
		Source == null ? new StaffSpriteTickResult( StaffSpriteTickStatus.NotDue ) : StaffSpriteVm.Tick( Source, nowMilliseconds );

	/// <summary>A new person's wrapper (<c>0x004D3ED0</c>): no handle and the two pending words 0.</summary>
	public void Reset()
	{
		Source = null;
		PendingTemplate = 0;
		PendingInterval = 0;
	}

	/// <summary>The frame the source shows, as (slot, frame), or null while it shows none.</summary>
	public (int Slot, int Frame)? Output =>
		Source is { HasOutput: true } source && source[StaffSpriteRegister.Slot] <= int.MaxValue && source[StaffSpriteRegister.Frame] <= int.MaxValue
			? ((int)source[StaffSpriteRegister.Slot], (int)source[StaffSpriteRegister.Frame]) : null;

	public StaffSpriteStateSnapshot CaptureSnapshot() => new( PendingTemplate, PendingInterval, Source is { } source ? Capture( source ) : null );

	/// <summary>Validates the whole snapshot before it replaces this wrapper; due times keep the snapshot's clock epoch.</summary>
	public void RestoreSnapshot( StaffSpriteStateSnapshot snapshot )
	{
		ValidateSnapshot( snapshot );
		Source = snapshot.Source is { } source ? Restore( source ) : null;
		PendingTemplate = snapshot.PendingTemplate;
		PendingInterval = snapshot.PendingInterval;
	}

	public static void ValidateSnapshot( StaffSpriteStateSnapshot snapshot )
	{
		ArgumentNullException.ThrowIfNull( snapshot );
		void Require( bool condition ) => _ = condition ? 0 : throw new InvalidDataException( "Staff sprite snapshot is inconsistent." );
		Require( snapshot.PendingTemplate == 0 || IsKnownTemplate( snapshot.PendingTemplate ) );
		Require( snapshot.PendingInterval is >= 0 and <= SpritePlayer.LongestInterval );
		if ( snapshot.Source is not { } source )
			return;
		Require( source.Stack is { Count: StaffSpriteVmState.StackCapacity } && source.Registers is { Count: StaffSpriteVmState.RegisterCount } );
		Require( Enum.IsDefined( (StaffSpriteRunState)source.RunState ) );
		Require( source.StackPointer is >= 0 and <= StaffSpriteVmState.StackCapacity );
		Require( source.IfDepth is >= -64 and <= 64 && source.CallDepth is >= -64 and <= 64 && source.LoopDepth is >= -64 and <= 64 );
		Require( source.Interval is >= 1 and <= SpritePlayer.LongestInterval );
		Require( source.OutputValid <= 1 );
		var programs = StaffSpritePrograms.Builtin;
		Require( programs.Contains( new StaffSpritePc( source.CurrentPc ) ) && programs.Contains( new StaffSpritePc( source.RestartPc ) ) );
		Require( source.LastPc == 0 || programs.Contains( new StaffSpritePc( source.LastPc ) ) );
		foreach ( var word in source.Stack )
			Require( word == 0 || programs.Contains( new StaffSpritePc( word ) ) );
		var registers = source.Registers;
		Require( registers[(int)StaffSpriteRegister.Kind] <= 8 && registers[(int)StaffSpriteRegister.Variant] <= int.MaxValue );
		Require( registers[(int)StaffSpriteRegister.Slot] < SpriteAnimationFile.SlotCount );
		Require( registers[(int)StaffSpriteRegister.Frame] <= 0xFFFF || registers[(int)StaffSpriteRegister.Frame] == uint.MaxValue );
		Require( registers[(int)StaffSpriteRegister.FrameCount] <= 0xFF );
		// a source that shows a frame shows one of its slot's
		Require( source.OutputValid == 0 || registers[(int)StaffSpriteRegister.Frame] != uint.MaxValue );
	}

	private static StaffSpriteVmSnapshot Capture( StaffSpriteVmState source ) => new( source.CurrentPc.Word, source.RestartPc.Word, source.LastPc.Word, (int)source.RunState, source.StackPointer,
		(uint[])source.Stack.Clone(), source.IfDepth, source.CallDepth, source.LoopDepth, source.Due, source.Interval, (uint[])source.Registers.Clone(), source.OutputValid );

	private static StaffSpriteVmState Restore( StaffSpriteVmSnapshot snapshot )
	{
		var state = StaffSpriteVm.Create( StaffSpriteProgram.Stand, 0 );
		state.CurrentPc = new StaffSpritePc( snapshot.CurrentPc );
		state.RestartPc = new StaffSpritePc( snapshot.RestartPc );
		state.LastPc = new StaffSpritePc( snapshot.LastPc );
		state.RunState = (StaffSpriteRunState)snapshot.RunState;
		state.StackPointer = snapshot.StackPointer;
		for ( var index = 0; index < StaffSpriteVmState.StackCapacity; index++ )
			state.Stack[index] = snapshot.Stack[index];
		state.IfDepth = snapshot.IfDepth;
		state.CallDepth = snapshot.CallDepth;
		state.LoopDepth = snapshot.LoopDepth;
		state.Due = snapshot.Due;
		state.Interval = snapshot.Interval;
		for ( var index = 0; index < StaffSpriteVmState.RegisterCount; index++ )
			state.Registers[index] = snapshot.Registers[index];
		state.OutputValid = snapshot.OutputValid;
		return state;
	}
}

namespace OpenTPW;

/// <summary>
/// The word offsets of the sprite-script table (tp.exe <c>0x0074DAB8</c>, the base every saved program counter counts from, <c>0x00475840</c>: <c>+0x14</c> of every record the loader reads): where each decoded program starts and where
/// each of its instructions lies, so a saved program counter names a (program, instruction) of the bounded sprite VM and back.
/// </summary>
internal static class SpriteProgramWords
{
	/// <summary>The program table's base address: the saved program counters are word offsets from it.</summary>
	public const uint TableBase = 0x74DAB8;

	/// <summary>The word offset of an address of the program table.</summary>
	public static int WordOf( uint address ) => checked( (int)((address - TableBase) / 4) );

	/// <summary>The start of the staff programs (the table <c>0x0075A418</c> for the templates 1 to 25, the performance entries <c>0x0074F7C8</c> and the hire script) as words from the table's base.</summary>
	// [BIN:TPW-EXE:0x0075A418 template table] 1 0x74DB60, 2 0x74DBC0, 3 0x74DC20, 9 0x74DAC0, 10 0x74E100, 17 0x74EDD8, 18 0x74EE38, 19 0x74EEC0, 20 0x74EE98, 21 0x74ED98, 22 0x74EF30, 23 0x74EF90, 24 0x74EFF0, 25 0x74F480; the performance entries 0x0074F7C8 name 0x74F638, 0x74F6D8, 0x74F708 and 0x74F738, whose loops start at 0x74F5B0, 0x74F698, 0x74F6F8 and 0x74F728; the hire script is 0x74F758 (0x49BB4A)
	private static readonly (StaffSpriteProgram Program, uint Address)[] StaffStarts =
	{
		(StaffSpriteProgram.Walk, 0x74DB60), (StaffSpriteProgram.Run, 0x74DBC0), (StaffSpriteProgram.Stand, 0x74DC20), (StaffSpriteProgram.Patrol, 0x74DAC0),
		(StaffSpriteProgram.Research, 0x74E100), (StaffSpriteProgram.Sweep, 0x74EDD8), (StaffSpriteProgram.Repair, 0x74EE38), (StaffSpriteProgram.Strike, 0x74EEC0),
		(StaffSpriteProgram.State6, 0x74EE98), (StaffSpriteProgram.Dangling, 0x74ED98), (StaffSpriteProgram.Catch, 0x74EF30), (StaffSpriteProgram.Hold, 0x74EF90),
		(StaffSpriteProgram.Template24, 0x74EFF0), (StaffSpriteProgram.Template25, 0x74F480), (StaffSpriteProgram.HireFigure, 0x74F758),
		(StaffSpriteProgram.Performance0, 0x74F638), (StaffSpriteProgram.Performance0Loop, 0x74F5B0), (StaffSpriteProgram.Performance1, 0x74F6D8), (StaffSpriteProgram.Performance1Loop, 0x74F698),
		(StaffSpriteProgram.Performance2, 0x74F708), (StaffSpriteProgram.Performance2Loop, 0x74F6F8), (StaffSpriteProgram.Performance3, 0x74F738), (StaffSpriteProgram.Performance3Loop, 0x74F728)
	};

	/// <summary>The start of the guest templates 1 to 8 (index = template; the table <c>0x0075A418</c>).</summary>
	// [BIN:TPW-EXE:0x0075A418 guest templates] 1 0x74DB60 walking, 2 0x74DBC0 running, 3 0x74DC20 standing, 4 0x74DC48 the indecision, 5 0x74DCE8 the cheer, 6 0x74DD28, 7 0x74DDA8 the sickness, 8 0x74DD68; a program is SET flags (3 words), SLOT (2), its FRAMEs (2 each) and a JMP (2)
	private static readonly uint[] VisitorStarts = { 0, 0x74DB60, 0x74DBC0, 0x74DC20, 0x74DC48, 0x74DCE8, 0x74DD28, 0x74DDA8, 0x74DD68 };

	/// <summary>The balloon's held program (template 25 of the table: FRAME 0, JMP to itself), as a word.</summary>
	public static int BalloonHeld => WordOf( 0x74F480 );
	/// <summary>The balloon's burst program (<c>0x4FE950</c> rebinds the held balloon to it, <c>0x475B80</c>): SET alpha 250 and a JMP into the fade loop.</summary>
	public static int BalloonBurst => WordOf( 0x74F4C0 );
	/// <summary>The fade loop's instruction after its FRAME 1: the program counter of a yielded burst (DO at 0x74F490, FRAME 1, then the subtraction of 20 from the alpha).</summary>
	public static int BalloonFade => WordOf( 0x74F490 ) + 3;
	/// <summary>The word after the fade loop's END (<c>0x5DA3C0</c>, 1 word): the program counter of an ended balloon.</summary>
	public static int BalloonEnded => WordOf( 0x74F4B8 ) + 1;

	/// <summary>How many table words an instruction takes (its handler and operands).</summary>
	// [BIN:TPW-EXE:0x0075A418 instruction widths] SET, ADD and COPY take a register and a value (3 words), SLOT, FRAME, FRAME-register and JMP one operand (2), IF and WHILE a register, a comparison and an operand (4), ENDIF and DO none (1), checked against the table's 0x74DAB8..0x74F7C0 (every program lands on its JMP and the next start)
	internal static int Width( StaffSpriteOp op ) => op switch
	{
		StaffSpriteOp.Set or StaffSpriteOp.Add or StaffSpriteOp.Copy => 3,
		StaffSpriteOp.Slot or StaffSpriteOp.Frame or StaffSpriteOp.FrameRegister or StaffSpriteOp.Jump => 2,
		StaffSpriteOp.IfGreaterLiteral or StaffSpriteOp.WhileNotEqualRegister or StaffSpriteOp.WhileLessEqualRegister => 4,
		_ => 1
	};

	private static readonly Dictionary<int, uint> StaffWords = BuildStaffWords();
	private static readonly Dictionary<uint, int> StaffOffsets = StaffWords.ToDictionary( pair => pair.Value, pair => pair.Key );

	private static Dictionary<int, uint> BuildStaffWords()
	{
		var words = new Dictionary<int, uint>();
		foreach ( var (program, address) in StaffStarts )
		{
			var offset = WordOf( address );
			for ( var index = 0; StaffSpritePrograms.Builtin.TryRead( new StaffSpritePc( program, index ), out var instruction ); index++ )
			{
				words[offset] = new StaffSpritePc( program, index ).Word;
				offset += Width( instruction.Op );
			}
		}
		return words;
	}

	/// <summary>The saved word offset of a staff program's first instruction.</summary>
	internal static int StartOf( StaffSpriteProgram program ) => WordOfStaffPc( new StaffSpritePc( program, 0 ).Word ) ?? throw new ArgumentOutOfRangeException( nameof( program ) );

	/// <summary>The semantic program counter of a saved word offset, false when it is no instruction of a decoded staff program.</summary>
	public static bool TryStaffPc( uint word, out uint semantic )
	{
		semantic = 0;
		return word <= int.MaxValue && StaffWords.TryGetValue( (int)word, out semantic );
	}

	/// <summary>The saved word offset of a semantic program counter (null for one this table has no word for).</summary>
	public static int? WordOfStaffPc( uint semantic ) => StaffOffsets.TryGetValue( semantic, out var word ) ? word : null;

	/// <summary>The guest template whose program starts at the word, null for none.</summary>
	public static VisitorSpriteTemplate? VisitorTemplateAt( uint word )
	{
		for ( var template = 1; template < VisitorStarts.Length; template++ )
			if ( word == WordOf( VisitorStarts[template] ) )
				return (VisitorSpriteTemplate)template;
		return null;
	}

	/// <summary>The first word of a guest template's program.</summary>
	public static int VisitorStart( VisitorSpriteTemplate template ) => WordOf( VisitorStarts[(int)template] );

	/// <summary>
	/// The guest player's position (program and next frame) of a saved program counter: a start of a program or the instruction after its SLOT is the next frame 0, a FRAME instruction its frame index, and the JMP at the end of
	/// a program the first frame of the program it jumps to (the player already takes the jump when it shows the last frame).
	/// </summary>
	public static (VisitorSpriteTemplate Program, int NextFrame)? VisitorPosition( uint word )
	{
		for ( var index = 1; index < VisitorStarts.Length; index++ )
		{
			var template = (VisitorSpriteTemplate)index;
			var start = VisitorStart( template );
			var script = VisitorSpriteScripts.Get( template );
			var frames = script.Frames.Count;
			if ( word == start || word == start + 5 )
				return (template, 0);
			if ( word > start + 5 && word < start + 5 + 2 * frames && (word - start - 5) % 2 == 0 )
				return (template, (int)(word - start - 5) / 2);
			if ( word == start + 5 + 2 * frames )
				return ((VisitorSpriteTemplate)script.Next, 0);
		}
		return null;
	}
}

/// <summary>
/// The Scripts block of an original save (the sprite scripts, writer <c>0x00475650</c>, loader <c>0x00475730</c>) read back into the host's sprite state: each person's sprite handle (<c>+0x0C</c> of the person) names the record of
/// the slot, whose program counters, run state, stack, due time, interval and registers are the sprite VM's. The wake ticks are absolute readings of the game clock the save's Clock block holds, so they are rebased on the host's clock sample.
/// </summary>
// [BIN:TPW-EXE:0x00475730 sprite loader] 'SCPT' and 0x118, the slot count, the slot words, then the 0x118-byte record of every nonzero slot from 1 on, each with its program table word (+0x14) set to 0x74DAB8; the due time (+0x7C) is read against the game clock 0x785970, which a load sets from the Clock block (0x4031F0)
internal sealed class SavedSpriteScripts
{
	private readonly Dictionary<int, SaveScriptRecord> bySlot;
	private readonly uint clockWord;

	public SavedSpriteScripts( SaveScriptsBlock block, uint clockWord )
	{
		ArgumentNullException.ThrowIfNull( block );
		this.clockWord = clockWord;
		bySlot = block.Records.ToDictionary( record => record.Slot );
	}

	/// <summary>The saved sprite scripts of an import, null when it has no Scripts block or no saved clock.</summary>
	public static SavedSpriteScripts? Of( OriginalParkImport? save ) =>
		save?.Scripts != null && save.RuntimeBlocks is { } blocks && blocks.ClockWords.Length > 0 ? new SavedSpriteScripts( save.Scripts, blocks.ClockWords[0] ) : null;

	/// <summary>The record a sprite handle names, null for handle 0 or a slot without a record.</summary>
	public SaveScriptRecord? Record( uint handle ) => handle != 0 && handle <= int.MaxValue && bySlot.TryGetValue( (int)handle, out var record ) ? record : null;

	/// <summary>The records no person's handle names, with the programs they run (the balloons that were bursting at the save).</summary>
	public IEnumerable<SaveScriptRecord> Records => bySlot.Values;

	/// <summary>A saved due time as a reading of the host's clock: the signed distance from the saved game clock added to <paramref name="hostClock"/> (the 32-bit arithmetic of the scheduler's <c>now &gt; due</c>).</summary>
	public int Rebase( uint stamp, int hostClock ) => unchecked( hostClock + (int)(stamp - clockWord) );

	/// <summary>
	/// The guest's body player of the record a person's handle (<c>+0x0C</c>) names, with the person's pending words (<paramref name="nextAnim"/> <c>+0x10</c>, <paramref name="nextServiceInterval"/> <c>+0x14</c>): null with the reason when the record is no program the guest player runs.
	/// </summary>
	public VisitorSpriteStateSnapshot? Visitor( uint handle, uint nextAnim, uint nextServiceInterval, int hostClock, out string? gap )
	{
		gap = null;
		if ( Record( handle ) is not { } record )
		{
			gap = $"sprite handle {handle} names no saved record";
			return null;
		}
		if ( SpriteProgramWords.VisitorTemplateAt( record.EntryPoint ) is not { } active || SpriteProgramWords.VisitorPosition( record.ProgramCounter ) is not var (current, nextFrame) )
		{
			gap = $"program counter {record.ProgramCounter} (entry {record.EntryPoint}) is no guest program position";
			return null;
		}
		VisitorSpriteFrame? output = record.Word( 0x114 ) != 0 ? new VisitorSpriteFrame( unchecked( (int)record.Word( 0xB4 ) ), unchecked( (int)record.Word( 0xB8 ) ) ) : null;
		VisitorSpriteTemplate? pending = nextAnim is >= 1 and <= 8 ? (VisitorSpriteTemplate)nextAnim : null;
		var pendingInterval = nextServiceInterval <= SpritePlayer.LongestInterval ? (int)nextServiceInterval : 0;
		var snapshot = new VisitorSpriteStateSnapshot(
			new VisitorSpritePlayerSnapshot( active, nextFrame, unchecked( (int)record.Period ), Rebase( record.WakeTick, hostClock ), output, current ), pending, pendingInterval );
		try
		{
			VisitorSpriteState.ValidateSnapshot( snapshot );
		}
		catch ( InvalidDataException exception )
		{
			gap = exception.Message;
			return null;
		}
		return snapshot;
	}

	/// <summary>
	/// A staff member's source and wrapper of a saved record (the person's handle, pending template and interval), null with the reason when a counter, stack word or register is not one the bounded staff VM represents.
	/// </summary>
	public StaffSpriteStateSnapshot? Staff( uint handle, uint nextAnim, uint nextServiceInterval, int hostClock, out string? gap )
	{
		gap = null;
		if ( Record( handle ) is not { } record )
		{
			gap = $"sprite handle {handle} names no saved record";
			return null;
		}
		if ( !SpriteProgramWords.TryStaffPc( record.ProgramCounter, out var current ) || !SpriteProgramWords.TryStaffPc( record.EntryPoint, out var restart ) )
		{
			gap = $"program counter {record.ProgramCounter} (entry {record.EntryPoint}) is no staff program position";
			return null;
		}
		uint last = 0;
		if ( record.LastCounter != 0 && !SpriteProgramWords.TryStaffPc( record.LastCounter, out last ) )
		{
			gap = $"last counter {record.LastCounter} is no staff program position";
			return null;
		}
		var stackPointer = unchecked( (int)record.Word( 0x1C ) );
		var stack = new uint[SaveScriptRecord.StackWords];
		for ( var index = 0; index < stack.Length; index++ )
		{
			var word = record.Word( 0x20 + index * 4 );
			if ( word == 0 )
				continue;
			if ( SpriteProgramWords.TryStaffPc( word, out var semantic ) )
				stack[index] = semantic;
			else if ( index >= stackPointer )
			{
				gap = $"stack word {index} ({word}) is no staff program position";
				return null;
			}
		}
		var source = new StaffSpriteVmSnapshot( current, restart, last, unchecked( (int)record.State ), stackPointer, stack, unchecked( (int)record.Word( 0x70 ) ), unchecked( (int)record.Word( 0x74 ) ),
			unchecked( (int)record.Word( 0x78 ) ), Rebase( record.WakeTick, hostClock ), unchecked( (int)record.Period ), record.Parameters, record.Word( 0x114 ) );
		var snapshot = new StaffSpriteStateSnapshot( nextAnim <= int.MaxValue ? (int)nextAnim : -1, nextServiceInterval <= int.MaxValue ? (int)nextServiceInterval : -1, source );
		try
		{
			StaffSpriteState.ValidateSnapshot( snapshot );
		}
		catch ( InvalidDataException exception )
		{
			gap = exception.Message;
			return null;
		}
		return snapshot;
	}

	/// <summary>
	/// A guest's held balloon of a saved record: the player the balloon script of the guest's handle (<c>+0x210</c>) is, null with the reason when the record is no held balloon program.
	/// </summary>
	public BalloonSpriteState? Balloon( uint handle, int guestId, int hostClock, long identity, out string? gap )
	{
		gap = null;
		if ( Record( handle ) is not { } record )
		{
			gap = $"balloon handle {handle} names no saved record";
			return null;
		}
		var held = SpriteProgramWords.BalloonHeld;
		if ( record.EntryPoint != held || record.ProgramCounter != held && record.ProgramCounter != held + 2 )
		{
			gap = $"the record runs program {record.EntryPoint} at {record.ProgramCounter}, not the held balloon program";
			return null;
		}
		if ( record.State is not (1 or 2) || record.Period is < 1 or > SpritePlayer.LongestInterval )
		{
			gap = $"run state {record.State} and period {record.Period} are not a held balloon's";
			return null;
		}
		return new BalloonSpriteState
		{
			Identity = identity, ColourGuestId = guestId, OwnerId = guestId, Deadline = Rebase( record.WakeTick, hostClock ),
			Visible = record.Word( 0x114 ) != 0
		};
	}
}

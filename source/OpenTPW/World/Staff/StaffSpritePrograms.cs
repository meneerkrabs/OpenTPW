namespace OpenTPW;

internal enum StaffSpriteOp { Set, Add, Copy, Slot, Frame, FrameRegister, Jump, IfGreaterLiteral, EndIf, Do, WhileNotEqualRegister, WhileLessEqualRegister, End }
internal readonly record struct StaffSpriteInstruction( StaffSpriteOp Op, int A = 0, int B = 0, StaffSpritePc Target = default );

/// <summary>Immutable clean semantic instruction descriptions; no original bytes, handler pointers or runtime asset decoding.</summary>
internal sealed class StaffSpriteProgramSet
{
	private readonly Dictionary<StaffSpriteProgram, StaffSpriteInstruction[]> programs;

	internal StaffSpriteProgramSet( IReadOnlyDictionary<StaffSpriteProgram, StaffSpriteInstruction[]> programs )
	{
		// Leave one encodable index for the post-fetch PC. A malformed/custom block cannot overflow Next.
		if ( programs.Any( pair => (uint)pair.Key > ushort.MaxValue || pair.Value.Length > ushort.MaxValue ) )
			throw new ArgumentOutOfRangeException( nameof( programs ), "Semantic program blocks must fit the bounded PC encoding." );
		this.programs = programs.ToDictionary( pair => pair.Key, pair => (StaffSpriteInstruction[])pair.Value.Clone() );
	}

	internal bool TryRead( StaffSpritePc pc, out StaffSpriteInstruction instruction )
	{
		instruction = default;
		if ( !programs.TryGetValue( pc.Program, out var code ) || pc.Instruction >= code.Length )
			return false;
		instruction = code[pc.Instruction];
		return true;
	}

	internal bool Contains( StaffSpritePc pc ) => TryRead( pc, out _ );

	// False IF scans to the matching ENDIF without executing skipped instructions or following JMPs.
	// The decoded staff blocks have no ELSE. Both scan length and nesting are bounded by the immutable block length.
	internal bool TryFindEndIf( StaffSpritePc start, out StaffSpritePc end )
	{
		end = default;
		if ( !programs.TryGetValue( start.Program, out var code ) )
			return false;
		var nesting = 0;
		for ( var index = start.Instruction; index < code.Length; index++ )
		{
			if ( code[index].Op == StaffSpriteOp.IfGreaterLiteral )
				nesting++;
			else if ( code[index].Op == StaffSpriteOp.EndIf && nesting-- == 0 )
			{
				end = new StaffSpritePc( start.Program, index );
				return true;
			}
		}
		return false;
	}
}

internal static class StaffSpritePrograms
{
	private const int Scratch = 0, Frame = 13, FrameCount = 14, Flags = 16, Intro = 17, Hold = 18;
	internal static readonly StaffSpriteProgramSet Builtin = Build();
	internal static bool IsEntry( StaffSpriteProgram program ) => program is
		StaffSpriteProgram.Walk or StaffSpriteProgram.Run or StaffSpriteProgram.Stand or StaffSpriteProgram.Patrol or
		StaffSpriteProgram.Research or StaffSpriteProgram.Sweep or StaffSpriteProgram.Repair or StaffSpriteProgram.Strike or
		StaffSpriteProgram.State6 or StaffSpriteProgram.Dangling or StaffSpriteProgram.Catch or StaffSpriteProgram.Hold or
		StaffSpriteProgram.Template24 or StaffSpriteProgram.Template25 or StaffSpriteProgram.Performance0 or
		StaffSpriteProgram.Performance1 or StaffSpriteProgram.Performance2 or StaffSpriteProgram.Performance3;

	// [BIN:TPW-EXE:0x0075A418 bounded staff graphs] SET flags, SLOT, FRAME yields, and semantic JMP; catch hands off to hold
	// Deliberately independent of StaffScripts: its flattened frame formula remains a separate test oracle.
	private static StaffSpriteProgramSet Build()
	{
		var programs = new Dictionary<StaffSpriteProgram, StaffSpriteInstruction[]>();
		void Ordinary( StaffSpriteProgram id, int slot, IEnumerable<int> frames, StaffSpriteProgram? next = null )
		{
			var code = new List<StaffSpriteInstruction> { new( StaffSpriteOp.Set, Flags, 0x1200 ), new( StaffSpriteOp.Slot, slot ) };
			code.AddRange( frames.Select( frame => new StaffSpriteInstruction( StaffSpriteOp.Frame, frame ) ) );
			code.Add( Jump( next ?? id ) );
			programs.Add( id, code.ToArray() );
		}
		Ordinary( StaffSpriteProgram.Walk, 1, Enumerable.Range( 0, 8 ) );
		Ordinary( StaffSpriteProgram.Run, 2, Enumerable.Range( 0, 8 ) );
		Ordinary( StaffSpriteProgram.Stand, 0, new[] { 0 } );
		Ordinary( StaffSpriteProgram.Patrol, 1, Enumerable.Range( 0, 8 ).SelectMany( frame => new[] { frame, frame } ) );
		Ordinary( StaffSpriteProgram.Research, 4, new[] { 0, 1, 2, 3, 0, 0, 0, 0 } );
		Ordinary( StaffSpriteProgram.Sweep, 4, Enumerable.Range( 0, 4 ).SelectMany( frame => new[] { frame, frame } ) );
		Ordinary( StaffSpriteProgram.Repair, 4, Enumerable.Range( 0, 8 ) );
		Ordinary( StaffSpriteProgram.Strike, 5, new[] { 0, 1, 2, 3, 0, 0, 0, 0, 0, 0 } );
		Ordinary( StaffSpriteProgram.State6, 3, new[] { 0 } );
		Ordinary( StaffSpriteProgram.Dangling, 6, Enumerable.Range( 0, 4 ) );
		Ordinary( StaffSpriteProgram.Catch, 7, Enumerable.Range( 0, 8 ), StaffSpriteProgram.Hold );
		Ordinary( StaffSpriteProgram.Hold, 8, Enumerable.Range( 0, 8 ) );
		// Guard release queues template 24. Template 25 is decoded, but its staff gameplay caller is not established.
		// [BIN:TPW-EXE:0x004D6A50 guard release] escort completion/stuck queues template 24 before releasing the guest
		Ordinary( StaffSpriteProgram.Template24, 9, Enumerable.Range( 0, 8 ), StaffSpriteProgram.Stand );
		programs.Add( StaffSpriteProgram.Template25, new[] { new StaffSpriteInstruction( StaffSpriteOp.Frame, 0 ), Jump( StaffSpriteProgram.Template25 ) } );

		// [BIN:TPW-EXE:0x0074F7C8 performance graphs] program 0 has an inclusive intro, main range and hold+1 zero-frame yields
		programs.Add( StaffSpriteProgram.Performance0, new StaffSpriteInstruction[]
		{
			new( StaffSpriteOp.Set, Flags, 0x1200 ), new( StaffSpriteOp.Set, Frame, 0 ),
			new( StaffSpriteOp.IfGreaterLiteral, Intro, 0 ), new( StaffSpriteOp.Do ),
			new( StaffSpriteOp.FrameRegister, Frame ), new( StaffSpriteOp.Add, Frame, 1 ),
			new( StaffSpriteOp.WhileLessEqualRegister, Frame, Intro ), new( StaffSpriteOp.EndIf ), Jump( StaffSpriteProgram.Performance0Loop )
		} );
		programs.Add( StaffSpriteProgram.Performance0Loop, new StaffSpriteInstruction[]
		{
			new( StaffSpriteOp.Copy, Frame, Intro ), new( StaffSpriteOp.Do ),
			new( StaffSpriteOp.FrameRegister, Frame ), new( StaffSpriteOp.Add, Frame, 1 ),
			new( StaffSpriteOp.WhileNotEqualRegister, Frame, FrameCount ), new( StaffSpriteOp.IfGreaterLiteral, Hold, 0 ),
			new( StaffSpriteOp.Set, Scratch, 0 ), new( StaffSpriteOp.Do ), new( StaffSpriteOp.Frame, 0 ),
			new( StaffSpriteOp.Add, Scratch, 1 ), new( StaffSpriteOp.WhileLessEqualRegister, Scratch, Hold ),
			new( StaffSpriteOp.EndIf ), Jump( StaffSpriteProgram.Performance0Loop )
		} );
		programs.Add( StaffSpriteProgram.Performance1, Prefix( StaffSpriteProgram.Performance1Loop ) );
		programs.Add( StaffSpriteProgram.Performance1Loop, new StaffSpriteInstruction[]
		{
			new( StaffSpriteOp.Set, Frame, 0 ), new( StaffSpriteOp.Do ), new( StaffSpriteOp.FrameRegister, Frame ),
			new( StaffSpriteOp.Add, Frame, 1 ), new( StaffSpriteOp.WhileLessEqualRegister, Frame, FrameCount ), Jump( StaffSpriteProgram.Performance1Loop )
		} );
		programs.Add( StaffSpriteProgram.Performance2, Prefix( StaffSpriteProgram.Performance2Loop ) );
		programs.Add( StaffSpriteProgram.Performance2Loop, new[] { new StaffSpriteInstruction( StaffSpriteOp.Frame, 2 ), Jump( StaffSpriteProgram.Performance2Loop ) } );
		programs.Add( StaffSpriteProgram.Performance3, Prefix( StaffSpriteProgram.Performance3Loop ) );
		programs.Add( StaffSpriteProgram.Performance3Loop, new[] { new StaffSpriteInstruction( StaffSpriteOp.Frame, 2 ), Jump( StaffSpriteProgram.Performance3Loop ) } );
		return new StaffSpriteProgramSet( programs );
	}

	private static StaffSpriteInstruction Jump( StaffSpriteProgram program ) => new( StaffSpriteOp.Jump, Target: new StaffSpritePc( program, 0 ) );
	private static StaffSpriteInstruction[] Prefix( StaffSpriteProgram loop ) => new[]
	{
		new StaffSpriteInstruction( StaffSpriteOp.Set, Flags, 0x1200 ), new( StaffSpriteOp.Set, Frame, 0 ), Jump( loop )
	};
}

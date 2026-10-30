namespace OpenTPW;

public sealed partial class RideVM
{
	/// <summary>
	/// Applies the run state of one script record of an original save's RSSE block (the 0xF4-byte struct and its buffers, loader tp.exe <c>0x5597A0</c>) to this script, which was just created
	/// for the same object: the program counter, the shared stack and heap, the variables, the limbo table, the result register, the TURBO and DIPMUSIC bytes, the reverb and speed words, the
	/// WALKST_FLOAT state and every wait and timer deadline. <paramref name="clockWord"/> is the saved game clock (the first word of the Clock block, <c>0x402E30</c>), which every absolute
	/// deadline of the record counts from. Returns null when the state was applied, else the reason it was refused; a refusal changes nothing.
	/// </summary>
	// [BIN:TPW-EXE:0x5597A0 RSSE loader] the struct is read raw over a fresh 0xF4-byte block (only the list links are kept), then the code, stack, variable, limbo, bounce, walk, string and node buffers are allocated and read; the program counter (+0x3C), stack pointer (+0x40), heap count (+0x44), result register (+0x48), the deadlines (+0xA0, +0xA4, +0xC4) and the other words stay exactly as saved
	// [BIN:TPW-EXE:0x553783 WAIT] with the deadline word (+0xA0) at 0 the handler stores clock + trunc(operand / speed), rewinds the program counter to the instruction (-2) and yields; with it set the instruction runs again each pass and continues, clearing the word, once the clock is not below it. WAITABS (0x553828), WAITANIM (0x5529BC) and WAITANIM_CH (0x55320A) share the word and the rewind; so a saved program counter on one of them, with a deadline, is a script inside its wait
	// [BIN:TPW-EXE:0x55641B SETTIMER] +0xC4 = clock + operand (absolute); GETTIMER (0x55644D) returns max(0, +0xC4 - clock), so a saved timer word counts from the saved clock like the waits
	internal string? ImportSavedRunState( SaveRsseRecord record, uint clockWord )
	{
		var reason = PrepareSavedRunState( record, clockWord, false, true, out var apply );
		if ( reason == null ) apply!();
		return reason;
	}

	// Preparation owns the parsed words; graph import defers every live assignment and presentation callback.
	internal string? PrepareSavedRunState( SaveRsseRecord record, uint clockWord, bool ordinaryGraph, bool notifyEffects, out Action? apply )
	{
		apply = null;
		ArgumentNullException.ThrowIfNull( record );
		if ( !ordinaryGraph && (record.ChildScriptId != 0 || record.SoundScriptId != 0) )
			return $"the script has a child ({record.ChildScriptId}) or sound child ({record.SoundScriptId}) script, which only the graph import restores";
		if ( record.CodeWordCount != Script.CodeWordCount )
			return $"the saved code is {record.CodeWordCount} words, this install's script {Script.CodeWordCount}";
		if ( record.Variables.Length != Variables.Length * 4 )
			return $"the saved script has {record.Variables.Length / 4} variables, this install's {Variables.Length}";
		if ( record.Stack.Length != stack.Length * 4 )
			return $"the saved stack holds {record.Stack.Length / 4} words, this install's header declares {stack.Length}";
		if ( record.Limbo.Length != limboValues.Length * SaveRsseRecord.LimboEntryBytes )
			return $"the saved limbo table holds {record.Limbo.Length / SaveRsseRecord.LimboEntryBytes} entries, this install's header declares {limboValues.Length}";
		var counter = unchecked( (int)record.ProgramCounter );
		var index = counter == Script.CodeWordCount ? instructions.Length : Script.GetInstructionIndexAtWord( counter );
		if ( index < 0 )
			return $"the saved program counter {counter} is not the start of an instruction";
		var pointer = unchecked( (int)record.StackPointer );
		var heap = unchecked( (int)System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian( record.Raw.AsSpan( 0x44 ) ) );
		if ( pointer < -1 || pointer > stack.Length - 1 || heap < 0 || heap > stack.Length )
			return $"the saved stack pointer {pointer} or heap count {heap} is outside the stack of {stack.Length} words";
		if ( record.LimboInUse > limboValues.Length )
			return $"the saved limbo count {record.LimboInUse} exceeds the table of {limboValues.Length}";

		var stackWords = Enumerable.Range( 0, stack.Length ).Select( slot => System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian( record.Stack.AsSpan( slot * 4 ) ) ).ToArray();
		var variableWords = Enumerable.Range( 0, Variables.Length ).Select( slot => System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian( record.Variables.AsSpan( slot * 4 ) ) ).ToArray();
		var values = Enumerable.Range( 0, limboValues.Length ).Select( slot => System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian( record.Limbo.AsSpan( slot * 8 ) ) ).ToArray();
		var expiries = Enumerable.Range( 0, limboValues.Length ).Select( slot => System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian( record.Limbo.AsSpan( slot * 8 + 4 ) ) ).ToArray();
		record = record with { Raw = record.Raw.ToArray() };
		var floatWords = unchecked( (int)System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian( record.Raw.AsSpan( 0x88 ) ) );
		var wakes = false;
		var wake = 0.0;
		var resume = index;
		// [BIN:TPW-EXE:0x00552CD3 TRIGWAITANIM token] +0xBC is the AnimID + 1 the instruction stored when it triggered; each later run compares the channel's AnimID + 1 with it (0x00552D08..0x00552D11) and clears it when they agree. The object's channels come back with
		// the RideSystem record (RestoreSavedScene), so the saved token is kept as it is and the instruction goes on waiting exactly as the original's does with those channels
		var triggerToken = unchecked( (int)record.AnimationWait2 );
		if ( index < instructions.Length )
		{
			var opcode = instructions[index].Opcode;
			if ( opcode is Opcode.WAIT or Opcode.WAITABS or Opcode.WAITANIM or Opcode.WAITANIM_CH && record.WaitDeadline != 0 )
			{
				resume = index + 1;
				if ( clockWord < record.WaitDeadline )
				{
					wakes = true;
					wake = record.WaitDeadline - clockWord;
				}
			}
		}

		// [BIN:TPW-EXE:0x0055429A NAME] +0x74 is the offset of the name in the string blob (a script nothing has named holds -1, 0x00558C4C): the blob comes back with the record, so the name is the string at the saved offset
		string? savedName = null;
		if ( record.Name != uint.MaxValue )
		{
			var blob = record.Strings;
			if ( record.Name >= blob.Length || blob.AsSpan( (int)record.Name ).IndexOf( (byte)0 ) < 0 )
				return $"the saved NAME offset {record.Name} is no string of the saved blob of {blob.Length} bytes";
			var span = blob.AsSpan( (int)record.Name );
			savedName = System.Text.Encoding.Latin1.GetString( span[..span.IndexOf( (byte)0 )] );
		}
		var savedLimboInUse = record.LimboInUse;
		var savedResultRegister = record.ResultRegister;
		var savedTurbo = record.Turbo;
		var savedReverb = record.Reverb;
		var savedSpeed = record.Speed;
		var savedDipMusic = record.DipMusic;
		var savedWalkFloat0 = record.WalkFloat0;
		var savedWalkFloat1 = record.WalkFloat1;
		var savedAnimationWait1 = record.AnimationWait1;
		var savedAnimationWait0 = record.AnimationWait0;
		var savedTimer = record.Timer;
		apply = () =>
		{
			if ( savedName != null )
				ScriptName = savedName;
			ProgramCounter = resume;
			State = wakes ? RideVMState.Waiting : resume >= instructions.Length ? RideVMState.Halted : RideVMState.Running;
			FaultMessage = null;
			Array.Copy( stackWords, stack, stack.Length );
			stackPointer = pointer;
			heapCount = heap;
			Array.Copy( variableWords, Variables, Variables.Length );
			Array.Copy( values, limboValues, values.Length );
			Array.Copy( expiries, limboExpiries, expiries.Length );
			limboCount = (int)savedLimboInUse;
			SetFlags( unchecked( (int)savedResultRegister ) );
			TurboByte = savedTurbo;
			ReverbLevel = unchecked( (int)savedReverb );
			SpeedBias = savedSpeed;
			if ( ordinaryGraph || savedDipMusic != 0 ) MusicDuckByte = savedDipMusic;
			if ( notifyEffects && savedDipMusic != 0 ) RequestMusicDuck( savedDipMusic );
			walkFloatStart = savedWalkFloat0;
			walkFloatDuration = unchecked( (int)savedWalkFloat1 );
			walkFloatWords = floatWords;
			LoopAnimationToken = unchecked( (int)savedAnimationWait1 ) & 0xFFFF;
			TriggerWaitToken = triggerToken;
			// the deadlines are absolute in the saved clock, this script's clock starts at the import: a deadline is the difference to the clock as a signed word (GETTIMER's subtraction, 0x556468)
			Wait4AnimDeadline = RelativeDeadline( savedAnimationWait0, clockWord );
			timerEndMilliseconds = savedTimer == 0 ? 0 : unchecked( (int)(savedTimer - clockWord) );
			WakeTimeMilliseconds = wake;
			TimeMilliseconds = 0;
			ClockOriginMilliseconds = clockWord;
			InCriticalSection = false;
			yieldRequested = false;
		};
		return null;
	}

	/// <summary>
	/// A saved absolute deadline as this script's clock counts it: 0 stays none, a deadline ahead of the saved clock becomes the remaining time,
	/// and a deadline not ahead of it is already past and imports as 0 (none). WAIT4ANIM treats the two alike (0x5538EB: a zero or reached
	/// deadline continues and leaves the deadline cleared), and the deadline must stay non-negative: an earlier -1 stand-in for "past" made every
	/// park-resume or restart snapshot of a park with a finished animation wait fail validation ("WAIT4ANIM deadline -1 is not finite and non-negative").
	/// </summary>
	private static double RelativeDeadline( uint saved, uint clockWord ) =>
		saved == 0 ? 0 : unchecked( (int)(saved - clockWord) ) is var difference && difference > 0 ? difference : 0;
}

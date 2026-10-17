namespace OpenTPW;

public static partial class OpcodeHandlers
{
	/// <summary>Writes a hooked opcode's result to <paramref name="dest"/> and sets flags from it.</summary>
	private static void EffectResult( RideVM vm, Opcode opcode, Operand dest, params Operand[] operands )
	{
		var result = vm.Effect( opcode, operands );
		dest.Value = result;
		vm.SetFlags( result );
	}

	/// <summary>Objects, particles, sound, screams and the park clock. All are pure game effects (inputs only) unless noted.</summary>
	public static class Effects
	{
		[OpcodeHandler( Opcode.ADDOBJ, RideOpcodeStatus.Hooked, "docs (`type parameter id slot`)" )]
		public static void AddObj( RideVM vm, Operand type, Operand parameter, Operand id, Operand slot ) => vm.Effect( Opcode.ADDOBJ, new[] { type, parameter, id, slot } );

		[OpcodeHandler( Opcode.ADDOBJ_EXT, RideOpcodeStatus.Hooked, "TPW-EXE 0x5520E8 (`type key effect control slot`): ADDOBJ with the control operand; no corpus use" )]
		public static void AddObjExt( RideVM vm, Operand type, Operand key, Operand effect, Operand control, Operand slot ) => vm.Effect( Opcode.ADDOBJ_EXT, new[] { type, key, effect, control, slot } );

		[OpcodeHandler( Opcode.KILLOBJ, RideOpcodeStatus.Hooked, "docs (`slot`); does not touch flags (`ADD VAR_COUNT 0xFFFF; KILLOBJ 200; BRANCH_PV`)" )]
		public static void KillObj( RideVM vm, Operand slot ) => vm.Effect( Opcode.KILLOBJ, new[] { slot } );

		[OpcodeHandler( Opcode.FADEOBJ, RideOpcodeStatus.Hooked, "docs (`slot`)" )]
		public static void FadeObj( RideVM vm, Operand slot ) => vm.Effect( Opcode.FADEOBJ, new[] { slot } );

		[OpcodeHandler( Opcode.SETOBJPARAM, RideOpcodeStatus.Hooked, "docs (`slot parameter value`)" )]
		public static void SetObjParam( RideVM vm, Operand slot, Operand parameter, Operand value ) => vm.Effect( Opcode.SETOBJPARAM, new[] { slot, parameter, value } );

		[OpcodeHandler( Opcode.EVENT, RideOpcodeStatus.Hooked, "docs (`type unknown event`); event names are theme-dependent and not assigned" )]
		public static void Event( RideVM vm, Operand type, Operand unknown, Operand id ) => vm.Effect( Opcode.EVENT, new[] { type, unknown, id } );

		[OpcodeHandler( Opcode.SETREVERB, RideOpcodeStatus.Hooked, "docs (0–10)" )]
		public static void SetReverb( RideVM vm, Operand level ) => vm.Effect( Opcode.SETREVERB, new[] { level } );

		[OpcodeHandler( Opcode.DIPMUSIC, RideOpcodeStatus.Hooked, "docs (0/1)" )]
		public static void DipMusic( RideVM vm, Operand value ) => vm.Effect( Opcode.DIPMUSIC, new[] { value } );

		[OpcodeHandler( Opcode.SCREAMLEVEL, RideOpcodeStatus.Hooked, "docs (0–100)" )]
		public static void ScreamLevel( RideVM vm, Operand level ) => vm.Effect( Opcode.SCREAMLEVEL, new[] { level } );

		[OpcodeHandler( Opcode.STARTSCREAM, RideOpcodeStatus.Hooked, "docs (`visitor unknown`)" )]
		public static void StartScream( RideVM vm, Operand visitor, Operand unknown ) => vm.Effect( Opcode.STARTSCREAM, new[] { visitor, unknown } );

		[OpcodeHandler( Opcode.STOPSCREAM, RideOpcodeStatus.Hooked, "docs" )]
		public static void StopScream( RideVM vm ) => vm.Effect( Opcode.STOPSCREAM, Array.Empty<Operand>() );

		[OpcodeHandler( Opcode.SINGLESCREAM, RideOpcodeStatus.Hooked, "docs (`visitor unknown`)" )]
		public static void SingleScream( RideVM vm, Operand visitor, Operand unknown ) => vm.Effect( Opcode.SINGLESCREAM, new[] { visitor, unknown } );

		[OpcodeHandler( Opcode.REPAIREFFECT, RideOpcodeStatus.Hooked, "docs (0 hide / 1 show)" )]
		public static void RepairEffect( RideVM vm, Operand value ) => vm.Effect( Opcode.REPAIREFFECT, new[] { value } );

		// [BIN:TPW-EXE:0x5564ED SPARK] four resolved operands: the first two are stored as words at +0xDC and +0xDE, the key nodes they name (class 0x100, 0x556B90) are looked up and logged (0x5DA3C0); the third and fourth are read and dropped. Nothing reads the two words (the other +0xDC and +0xDE readers belong to the tour and bump controllers), so the opcode has no effect
		[OpcodeHandler( Opcode.SPARK, RideOpcodeStatus.Implemented, "TPW-EXE 0x5564ED (`a b c d`, resolved): stores a and b as words at +0xDC and +0xDE, looks the two key nodes (class 0x100) up and logs them; c and d are dropped; nothing reads the words, so no game effect (1 corpus use, Plasma)" )]
		public static void Spark( RideVM vm, Operand a, Operand b, Operand c, Operand d ) { }

		[OpcodeHandler( Opcode.TURBO, RideOpcodeStatus.Implemented, "TPW-EXE 0x5542B9: stores the low byte of the raw operand word in the script's TURBO byte (a variable operand stores its index byte); nothing else" )]
		public static void Turbo( RideVM vm, Operand value ) => vm.TurboByte = value.Raw & 0xFF;

		[OpcodeHandler( Opcode.HOUR, RideOpcodeStatus.Hooked, "host-local civil hour: tp.exe 0x5566AC calls time/localtime and stores tm_hour in the result register; a variable operand receives it, a literal only sets the flags; the TPW runtime samples host time (PPC-clock.md)" )]
		public static void Hour( RideVM vm, Operand dest ) => EffectResult( vm, Opcode.HOUR, dest, dest );

		[OpcodeHandler( Opcode.MIN, RideOpcodeStatus.Hooked, "host-local civil minute (tm_min), as HOUR: tp.exe 0x5566FF calls time/localtime and stores tm_min in the result register; corpus `MIN 0; BRANCH_NZ` uses a literal dest for flags only" )]
		public static void Minute( RideVM vm, Operand dest ) => EffectResult( vm, Opcode.MIN, dest, dest );

		[OpcodeHandler( Opcode.SEC, RideOpcodeStatus.Hooked, "host-local civil second (tm_sec), as HOUR: tp.exe 0x55674E calls time/localtime and stores tm_sec in the result register; corpus `SEC 0; BRANCH_NZ`" )]
		public static void Second( RideVM vm, Operand dest ) => EffectResult( vm, Opcode.SEC, dest, dest );

		[OpcodeHandler( Opcode.YEAR, RideOpcodeStatus.Hooked, "host-local civil year minus 1900 (tm_year; tp.exe 0x5565B2 and PPC-clock.md); TPW runtime samples host time" )]
		public static void Year( RideVM vm, Operand dest ) => EffectResult( vm, Opcode.YEAR, dest, dest );

		[OpcodeHandler( Opcode.MONTH, RideOpcodeStatus.Hooked, "host-local civil month 1..12 (tm_mon + 1; tp.exe 0x556605 and PPC-clock.md); TPW runtime samples host time" )]
		public static void Month( RideVM vm, Operand dest ) => EffectResult( vm, Opcode.MONTH, dest, dest );

		[OpcodeHandler( Opcode.DAY, RideOpcodeStatus.Hooked, "host-local civil day 1..31 (tm_mday; tp.exe 0x556659 and PPC-clock.md); TPW runtime samples host time" )]
		public static void Day( RideVM vm, Operand dest ) => EffectResult( vm, Opcode.DAY, dest, dest );
	}
}

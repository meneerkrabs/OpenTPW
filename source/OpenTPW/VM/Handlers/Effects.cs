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

		[OpcodeHandler( Opcode.SPARK, RideOpcodeStatus.Hooked, "unknown upstream; 1 corpus use (Plasma); passed through" )]
		public static void Spark( RideVM vm, Operand a, Operand b, Operand c, Operand d ) => vm.Effect( Opcode.SPARK, new[] { a, b, c, d } );

		[OpcodeHandler( Opcode.TURBO, RideOpcodeStatus.Hooked, "docs (`value` 0..1, meaning unknown)" )]
		public static void Turbo( RideVM vm, Operand value ) => vm.Effect( Opcode.TURBO, new[] { value } );

		[OpcodeHandler( Opcode.HOUR, RideOpcodeStatus.Hooked, "docs (in-game hour → dest); corpus Clock.RSE `HOUR VAR_TEMP; MOD VAR_TEMP VAR_TEMP 12`; sets flags; park clock not implemented" )]
		public static void Hour( RideVM vm, Operand dest ) => EffectResult( vm, Opcode.HOUR, dest, dest );

		[OpcodeHandler( Opcode.MIN, RideOpcodeStatus.Hooked, "docs (in-game minute → dest); corpus `MIN 0; BRANCH_NZ` uses a literal dest for flags only" )]
		public static void Minute( RideVM vm, Operand dest ) => EffectResult( vm, Opcode.MIN, dest, dest );

		[OpcodeHandler( Opcode.SEC, RideOpcodeStatus.Hooked, "docs (in-game second → dest); corpus `SEC 0; BRANCH_NZ`" )]
		public static void Second( RideVM vm, Operand dest ) => EffectResult( vm, Opcode.SEC, dest, dest );

		[OpcodeHandler( Opcode.YEAR, RideOpcodeStatus.Hooked, "docs only (no corpus use); as HOUR" )]
		public static void Year( RideVM vm, Operand dest ) => EffectResult( vm, Opcode.YEAR, dest, dest );

		[OpcodeHandler( Opcode.MONTH, RideOpcodeStatus.Hooked, "docs only (no corpus use); as HOUR" )]
		public static void Month( RideVM vm, Operand dest ) => EffectResult( vm, Opcode.MONTH, dest, dest );

		[OpcodeHandler( Opcode.DAY, RideOpcodeStatus.Hooked, "docs only (no corpus use); as HOUR" )]
		public static void Day( RideVM vm, Operand dest ) => EffectResult( vm, Opcode.DAY, dest, dest );
	}
}

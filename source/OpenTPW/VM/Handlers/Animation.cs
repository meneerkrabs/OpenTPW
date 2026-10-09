namespace OpenTPW;

public static partial class OpcodeHandlers
{
	/// <summary>
	/// Animation opcodes. The effects layer plays the animation and returns its duration in milliseconds
	/// (0 when unknown); the VM uses that for waits, WAIT4ANIM and duration outputs. MD2 animation tracks are
	/// not decoded, so by default every duration is 0.
	/// </summary>
	public static class Animation
	{
		[OpcodeHandler( Opcode.TRIGANIM, RideOpcodeStatus.Hooked, "docs example `TRIGANIM ANIM_Main 0 0`; corpus: third operand receives the duration (`TRIGANIM 5 0 VAR_TEMP; SUB VAR_TEMP VAR_TEMP 3000; WAIT VAR_TEMP; …; WAIT4ANIM`)" )]
		public static void TrigAnim( RideVM vm, Operand animation, Operand variant, Operand duration )
		{
			var milliseconds = vm.Effect( Opcode.TRIGANIM, new[] { animation, variant, duration } );
			vm.TrackAnimation( milliseconds );
			duration.Value = milliseconds;
		}

		[OpcodeHandler( Opcode.WAITANIM, RideOpcodeStatus.Hooked, "docs (play and wait for the end)" )]
		public static void WaitAnim( RideVM vm, Operand animation, Operand variant )
		{
			var milliseconds = vm.Effect( Opcode.WAITANIM, new[] { animation, variant } );
			vm.TrackAnimation( milliseconds );
			vm.WaitFor( milliseconds );
		}

		[OpcodeHandler( Opcode.TRIGWAITANIM, RideOpcodeStatus.Hooked, "name (trigger + wait); operand layout as TRIGANIM (`TRIGWAITANIM 4 0 VAR_TEMP`)" )]
		public static void TrigWaitAnim( RideVM vm, Operand animation, Operand variant, Operand duration )
		{
			var milliseconds = vm.Effect( Opcode.TRIGWAITANIM, new[] { animation, variant, duration } );
			vm.TrackAnimation( milliseconds );
			duration.Value = milliseconds;
			vm.WaitFor( milliseconds );
		}

		[OpcodeHandler( Opcode.WAIT4ANIM, RideOpcodeStatus.Hooked, "docs (wait for all playing animations); waits for the latest end time the effects layer reported" )]
		public static void Wait4Anim( RideVM vm ) => vm.WaitUntil( vm.AnimationsEndMilliseconds );

		[OpcodeHandler( Opcode.LOOPANIM, RideOpcodeStatus.Hooked, "docs (loop an animation); loops are not awaited by WAIT4ANIM (assumption)" )]
		public static void LoopAnim( RideVM vm, Operand animation, Operand variant ) => vm.Effect( Opcode.LOOPANIM, new[] { animation, variant } );

		[OpcodeHandler( Opcode.FLUSHANIM, RideOpcodeStatus.Hooked, "docs (stop all animations); clears pending WAIT4ANIM time" )]
		public static void FlushAnim( RideVM vm )
		{
			vm.Effect( Opcode.FLUSHANIM, Array.Empty<Operand>() );
			vm.FlushAnimations();
		}

		[OpcodeHandler( Opcode.TRIGANIMSPEED, RideOpcodeStatus.Hooked, "unknown upstream; corpus `TRIGANIMSPEED 6 0 VAR_TEMP 4000 … WAIT4ANIM`: returned duration is tracked for WAIT4ANIM, operands passed through" )]
		public static void TrigAnimSpeed( RideVM vm, Operand a, Operand b, Operand c, Operand d )
			=> vm.TrackAnimation( vm.Effect( Opcode.TRIGANIMSPEED, new[] { a, b, c, d } ) );

		[OpcodeHandler( Opcode.TRIGANIM_CH, RideOpcodeStatus.Hooked, "unknown upstream; corpus (Totem `TRIGANIM_CH 5 1 0 1` …, .sam `NumSimultAnims 4`) suggests animation/variant/duration/channel; passed through" )]
		public static void TrigAnimChannel( RideVM vm, Operand a, Operand b, Operand c, Operand d ) => vm.Effect( Opcode.TRIGANIM_CH, new[] { a, b, c, d } );

		[OpcodeHandler( Opcode.LOOPANIM_CH, RideOpcodeStatus.Hooked, "unknown upstream; 1 corpus use; passed through" )]
		public static void LoopAnimChannel( RideVM vm, Operand a, Operand b, Operand c ) => vm.Effect( Opcode.LOOPANIM_CH, new[] { a, b, c } );

		[OpcodeHandler( Opcode.GETANIM_CH, RideOpcodeStatus.Hooked, "unknown upstream; corpus `GETANIM_CH 0 n; BRANCH_PV x; BRANCH_Z x` → `dest channel`, result sets flags" )]
		public static void GetAnimChannel( RideVM vm, Operand dest, Operand channel )
		{
			var result = vm.Effect( Opcode.GETANIM_CH, new[] { dest, channel } );
			dest.Value = result;
			vm.SetFlags( result );
		}
	}
}

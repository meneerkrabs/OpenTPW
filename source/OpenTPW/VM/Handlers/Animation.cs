namespace OpenTPW;

public static partial class OpcodeHandlers
{
	/// <summary>
	/// Animation opcodes with the semantics of the Feral Mac handlers (switch table data 0x3f2a4;
	/// docs/reverse/ANIM-plan.md §8). The effects layer triggers, flushes and queries the object's channels and
	/// returns the trigger's millisecond result (<c>ret</c>: the clip length, 1000 for a missing clip, never
	/// speed-scaled). The VM turns <c>ret</c> into the script's waits: each TRIGANIM-family opcode overwrites the
	/// script's single WAIT4ANIM deadline (script +164), WAITANIM/LOOPANIM zero it, and the plain triggers reset the
	/// LOOPANIM token (+168). Without an animation host (an effect recorded as unimplemented) the script has no
	/// object (+200 null): every trigger handler then skips the trigger and uses <c>ret</c> = 0, as the original does.
	/// </summary>
	public static class Animation
	{
		/// <summary>Trigger operand channel of the plain opcodes (0xafb10, 0xafc68, 0xafd7c, 0xafe7c, 0xb005c).</summary>
		public const int MainChannel = 0;

		[OpcodeHandler( Opcode.TRIGANIM, RideOpcodeStatus.Hooked, "STP-PPC 0xafa94: trigger channel 0, flags 0; out = max(ret − 300, 300); WAIT4ANIM deadline = now + out / script speed; LOOPANIM token reset" )]
		public static void TrigAnim( RideVM vm, Operand animation, Operand variant, Operand duration )
		{
			var adjusted = RideAnimationTiming.AdjustTriggerReturn( Trigger( vm, Opcode.TRIGANIM, new[] { animation, variant, duration } ) );
			duration.Value = adjusted;
			vm.Wait4AnimDeadline = vm.TimeMilliseconds + RideAnimationTiming.TriggerDelay( adjusted, vm.ScriptSpeed );
			vm.LoopAnimationToken = RideAnimationTiming.NoLoopToken;
		}

		[OpcodeHandler( Opcode.WAITANIM, RideOpcodeStatus.Hooked, "STP-PPC 0xafbbc: trigger channel 0, flags 0; waits max(unsigned(ret − 300) / script speed, 300) (a clip under 300 ms saturates to INT32_MAX ms) on a u32 deadline; zeroes the WAIT4ANIM deadline; LOOPANIM token reset" )]
		public static void WaitAnim( RideVM vm, Operand animation, Operand variant )
		{
			var wait = RideAnimationTiming.WaitAnimDelay( Trigger( vm, Opcode.WAITANIM, new[] { animation, variant } ), vm.ScriptSpeed );
			vm.Wait4AnimDeadline = 0;
			vm.LoopAnimationToken = RideAnimationTiming.NoLoopToken;
			WaitUntilDeadline( vm, unchecked((uint)wait) );
		}

		[OpcodeHandler( Opcode.LOOPANIM, RideOpcodeStatus.Hooked, "STP-PPC 0xafd00: no-op while token anim + (sub << 16) equals script +168; else loops on channel 0 (flags 1), stores the token, zeroes the WAIT4ANIM deadline" )]
		public static void LoopAnim( RideVM vm, Operand animation, Operand variant )
		{
			// [BIN:STP-PPC:0x100AFD2C LOOPANIM token] an equal token returns before the trigger and leaves the deadline; TRIGANIM, WAITANIM, TRIGWAITANIM and TRIGANIMSPEED store 0xFFFF (0x100AFBB4, 0x100AFCF8, 0x100AFF24, 0x100B00EC), as does script creation (0x100B31A0); the _CH forms leave it
			var token = RideAnimationTiming.LoopToken( animation.Value, variant.Value );
			if ( token == vm.LoopAnimationToken )
				return;
			vm.Effect( Opcode.LOOPANIM, new[] { animation, variant } );
			vm.Wait4AnimDeadline = 0;
			vm.LoopAnimationToken = token;
		}

		[OpcodeHandler( Opcode.TRIGWAITANIM, RideOpcodeStatus.Hooked, "STP-PPC 0xafd94: as TRIGANIM, then re-executes until channel 0's AnimID equals the triggered one (+188 = id + 1), yielding while it differs" )]
		public static void TrigWaitAnim( RideVM vm, Operand animation, Operand variant, Operand duration )
			=> TriggerAndWait( vm, Opcode.TRIGWAITANIM, new[] { animation, variant, duration }, animation, duration, MainChannel, resetLoopToken: true );

		[OpcodeHandler( Opcode.GETANIM, RideOpcodeStatus.Hooked, "STP-PPC 0xaff3c: channel 0's AnimID (12 = none), −1 once its non-looping clip ended (channel flag 0x4); result sets flags" )]
		public static void GetAnim( RideVM vm, Operand dest ) => Query( vm, Opcode.GETANIM, new[] { dest }, dest );

		[OpcodeHandler( Opcode.TRIGANIMSPEED, RideOpcodeStatus.Hooked, "STP-PPC 0xaffc4: trigger channel 0 at script speed × operand / 1000; out = max(ret − 300, 300); deadline = now + out × 1000 / operand (signed divw); LOOPANIM token reset" )]
		public static void TrigAnimSpeed( RideVM vm, Operand animation, Operand variant, Operand duration, Operand perMille )
		{
			var speed = perMille.Value;
			// Zero is undefined (divw by zero on PowerPC); the corpus uses 1800–4000.
			// [EXT:trig-anim-speed-guard] A TRIGANIMSPEED speed operand of 0 or below faults the script: PC 0x552f2e..0x552feb stores the operand at +0xe4 as 16 bits and divides by the full operand (0 is a divide fault there too), a negative speed gives a decreasing frame whose end test in 0x4735d0 never fires, and the sampler's unsigned key searches (0x470c90) would read the 12-byte pose table out of bounds; the corpus has 4 uses, all literals from 1800 to 4000, so this is OpenTPW's guard for malformed input (formerly RIDES-038)
			if ( speed <= 0 )
				throw new RideScriptException( speed == 0
					? "TRIGANIMSPEED speed 0 divides by zero (undefined divw)"
					: $"TRIGANIMSPEED speed {speed} per mille: negative speeds are defined in the original but their channel playback is not traced (RIDES-038)" );
			var adjusted = RideAnimationTiming.AdjustTriggerReturn( Trigger( vm, Opcode.TRIGANIMSPEED, new[] { animation, variant, duration, perMille } ) );
			duration.Value = adjusted;
			vm.Wait4AnimDeadline = vm.TimeMilliseconds + RideAnimationTiming.SpeedTriggerDelay( adjusted, speed );
			vm.LoopAnimationToken = RideAnimationTiming.NoLoopToken;
		}

		[OpcodeHandler( Opcode.FLUSHANIM, RideOpcodeStatus.Hooked, "STP-PPC 0xafa68 → 0xa6c90: clears only channel 0's deferred clip; the playing clip and the WAIT4ANIM deadline stay" )]
		public static void FlushAnim( RideVM vm ) => vm.Effect( Opcode.FLUSHANIM, Array.Empty<Operand>() );

		[OpcodeHandler( Opcode.FLUSHANIM_CH, RideOpcodeStatus.Hooked, "STP-PPC 0xb00f4 → 0xa6c90: clears only the operand channel's deferred clip" )]
		public static void FlushAnimChannel( RideVM vm, Operand channel ) => vm.Effect( Opcode.FLUSHANIM_CH, new[] { channel } );

		[OpcodeHandler( Opcode.TRIGANIM_CH, RideOpcodeStatus.Hooked, "STP-PPC 0xb013c: as TRIGANIM on channel operand 4 (Totem `TRIGANIM_CH 5 1 0 1`); leaves the LOOPANIM token" )]
		public static void TrigAnimChannel( RideVM vm, Operand animation, Operand variant, Operand duration, Operand channel )
		{
			var adjusted = RideAnimationTiming.AdjustTriggerReturn( Trigger( vm, Opcode.TRIGANIM_CH, new[] { animation, variant, duration, channel } ) );
			duration.Value = adjusted;
			vm.Wait4AnimDeadline = vm.TimeMilliseconds + RideAnimationTiming.TriggerDelay( adjusted, vm.ScriptSpeed );
		}

		[OpcodeHandler( Opcode.WAITANIM_CH, RideOpcodeStatus.Hooked, "STP-PPC 0xb0268, PC 0x55320A (same arithmetic): as WAITANIM on channel operand 3 without the speed division or fctiwz: unsigned max(ret − 300, 300) added to the clock as a wrapping u32 (0xb035c), so a clip under 300 ms gives a deadline in the past and the script continues next slice; zeroes the deadline" )]
		public static void WaitAnimChannel( RideVM vm, Operand animation, Operand variant, Operand channel )
		{
			var wait = RideAnimationTiming.WaitAnimChannelDelay( Trigger( vm, Opcode.WAITANIM_CH, new[] { animation, variant, channel } ) );
			vm.Wait4AnimDeadline = 0;
			WaitUntilDeadline( vm, wait );
		}

		[OpcodeHandler( Opcode.LOOPANIM_CH, RideOpcodeStatus.Hooked, "STP-PPC 0xb037c: loops on channel operand 3 (flags 1) on every call, no token check; zeroes the deadline" )]
		public static void LoopAnimChannel( RideVM vm, Operand animation, Operand variant, Operand channel )
		{
			vm.Effect( Opcode.LOOPANIM_CH, new[] { animation, variant, channel } );
			vm.Wait4AnimDeadline = 0;
		}

		[OpcodeHandler( Opcode.TRIGWAITANIM_CH, RideOpcodeStatus.Hooked, "STP-PPC 0xb0404: as TRIGWAITANIM on channel operand 4; leaves the LOOPANIM token" )]
		public static void TrigWaitAnimChannel( RideVM vm, Operand animation, Operand variant, Operand duration, Operand channel )
			=> TriggerAndWait( vm, Opcode.TRIGWAITANIM_CH, new[] { animation, variant, duration, channel }, animation, duration, channel.Value, resetLoopToken: false );

		[OpcodeHandler( Opcode.GETANIM_CH, RideOpcodeStatus.Hooked, "STP-PPC 0xb05b8: `GETANIM_CH dest channel`: the channel's AnimID, −1 once ended; result sets flags (corpus `GETANIM_CH 0 n; BRANCH_PV x; BRANCH_Z x`)" )]
		public static void GetAnimChannel( RideVM vm, Operand dest, Operand channel ) => Query( vm, Opcode.GETANIM_CH, new[] { dest, channel }, dest );

		[OpcodeHandler( Opcode.WAIT4ANIM, RideOpcodeStatus.Hooked, "STP-PPC 0xb07a0: deadline 0 continues; otherwise blocks while now < deadline (re-executing), then clears it" )]
		public static void Wait4Anim( RideVM vm )
		{
			var deadline = vm.Wait4AnimDeadline;
			if ( deadline == 0 )
				return;
			if ( vm.TimeMilliseconds < deadline )
			{
				vm.RepeatInstruction();
				vm.WaitUntil( deadline );
				return;
			}
			vm.Wait4AnimDeadline = 0;
		}

		/// <summary><c>ret</c> of a script without an object (+200 null): <c>li r3,0</c> instead of the trigger.</summary>
		// [BIN:STP-PPC:0x100AFB1C no object] TRIGANIM, WAITANIM (0x100AFC74), TRIGWAITANIM (0x100AFE88), TRIGANIMSPEED (0x100B0078), TRIGANIM_CH (0x100B01D4), WAITANIM_CH (0x100B032C) and TRIGWAITANIM_CH (0x100B0510) skip the trigger and use 0 when script +200 is null
		public const int NoObjectMilliseconds = 0;

		/// <summary>Runs a trigger effect; <c>ret</c>, or 0 when no animation host answers (a script without an object).</summary>
		private static int Trigger( RideVM vm, Opcode opcode, Operand[] operands ) =>
			vm.TryEffect( opcode, operands, out var result ) ? result : NoObjectMilliseconds;

		/// <summary>
		/// WAITANIM(_CH)'s wait (0xafcc4/0xb034c): the deadline +160 is the 32-bit clock plus the wait as a wrapping
		/// add, and each later execution continues once <c>now ≥ deadline</c> (unsigned, 0xafc04/0xb02bc). A wrapped
		/// deadline is already past: the script yields and continues at its next slice (the re-execution that finds the
		/// deadline passed is folded into that yield, as the re-executions of an ordinary wait are). A deadline of exactly
		/// 0 reads as "not waiting", so the next execution triggers again.
		/// </summary>
		private static void WaitUntilDeadline( RideVM vm, uint wait )
		{
			var now = vm.ClockMilliseconds32;
			var deadline = unchecked(now + wait);
			if ( deadline == 0 )
			{
				vm.RepeatInstruction();
				vm.EndSlice();
				return;
			}
			if ( deadline <= now )
			{
				vm.EndSlice();
				return;
			}
			vm.WaitFor( wait );
		}

		private static void Query( RideVM vm, Opcode opcode, Operand[] operands, Operand dest )
		{
			// An unanswered query keeps the destination and flags, like the controller queries.
			if ( !vm.TryEffect( opcode, operands, out var result ) )
				return;
			dest.Value = result;
			vm.SetFlags( result );
		}

		/// <summary>
		/// TRIGWAITANIM(_CH): the first execution triggers, sets the out variable and deadline and stores the
		/// wait token AnimID + 1 (+188), then executes again at once (no yield, 0xaff30). A later execution reads
		/// the channel's AnimID (0xa7dcc) into the stack word that holds the out operand's code word: equal →
		/// clears the token and continues; different → executes again next slice (0xafe28, +152 cleared). A clip
		/// without a member leaves the channel unchanged, so the script then waits until the channel shows that
		/// AnimID. Without an object the query is skipped (0xafde8) and the compare reads the out operand's code
		/// word + 1: a variable word (tag 0xC0 on the Mac, 0x40 in the top byte on the PC) never equals the token and the
		/// script re-executes every slice for good, while a literal word 0x0000vvvv equals it once v is the triggered AnimID (RIDES-037, PC 0x5535c7).
		/// </summary>
		private static void TriggerAndWait( RideVM vm, Opcode opcode, Operand[] operands, Operand animation, Operand duration, int channel, bool resetLoopToken )
		{
			if ( vm.TriggerWaitToken != 0 )
			{
				// [BIN:TPW-EXE:0x5535c7 TRIGWAITANIM compare] Without an object both wait handlers (0x5535c7..0x5535d4 and 0x552cd3) compare the out operand's raw code word + 1 with the token. The loader 0x5587f0 stores code raw and operands decode as variable when (word & 0xff000000) == 0x40000000, else as the sign-extended low 16 bits: a literal word 0x0000vvvv resumes once v equals the triggered AnimID, a variable word never matches (the Mac rule's tag 0xC0 is the same never-equal case).
				var matched = vm.Effects is IRideAnimationQuery query && query.TryGetAnimationId( vm, channel, out var animationId )
					? animationId + 1 == vm.TriggerWaitToken
					: duration.Kind == RideScriptOperandKind.Literal && duration.Raw + 1 == vm.TriggerWaitToken;
				if ( !matched )
				{
					vm.RepeatInstruction();
					vm.EndSlice();
					return;
				}
				vm.TriggerWaitToken = 0;
				return;
			}
			var adjusted = RideAnimationTiming.AdjustTriggerReturn( Trigger( vm, opcode, operands ) );
			duration.Value = adjusted;
			vm.Wait4AnimDeadline = vm.TimeMilliseconds + RideAnimationTiming.TriggerDelay( adjusted, vm.ScriptSpeed );
			if ( resetLoopToken )
				vm.LoopAnimationToken = RideAnimationTiming.NoLoopToken;
			vm.TriggerWaitToken = animation.Value + 1;
			vm.RepeatInstruction();
		}
	}
}

/// <summary>
/// Wait arithmetic of the animation opcodes (Feral Mac handlers; reference model
/// tools/ppc-analysis/lanes/anim/anim_model.py). Single-precision steps round like the original; float →
/// int conversions saturate like PowerPC <c>fctiwz</c>.
/// </summary>
public static class RideAnimationTiming
{
	/// <summary>LOOPANIM token value that matches no animation: script creation and the plain triggers store it.</summary>
	public const int NoLoopToken = 0xFFFF;
	public const int MinimumWaitMilliseconds = 300;

	/// <summary>TRIGANIM/TRIGWAITANIM/TRIGANIMSPEED/TRIGANIM_CH (0xafb20): signed <c>max(ret − 300, 300)</c>.</summary>
	public static int AdjustTriggerReturn( int returned ) => Math.Max( unchecked(returned - MinimumWaitMilliseconds), MinimumWaitMilliseconds );

	/// <summary>Deadline offset of the ordinary triggers: <c>trunc(out / script speed)</c> in single precision.</summary>
	public static int TriggerDelay( int adjusted, float scriptSpeed ) => ObjectAnimator.Fctiwz( (float)adjusted / scriptSpeed );

	/// <summary>TRIGANIMSPEED (0xb00dc): <c>out × 1000 / operand</c>, 32-bit multiply and truncating signed division.</summary>
	public static int SpeedTriggerDelay( int adjusted, int perMille )
	{
		if ( perMille == 0 )
			throw new ArgumentOutOfRangeException( nameof( perMille ) );
		return unchecked(adjusted * 1000) / perMille;
	}

	/// <summary>TRIGANIMSPEED channel speed: <c>script speed × operand / 1000</c>, each step in single precision.</summary>
	public static float SpeedOperand( float scriptSpeed, int perMille )
	{
		var product = scriptSpeed * (float)perMille;
		return product / 1000f;
	}

	/// <summary>
	/// WAITANIM (0xafc78): <c>ret − 300</c> converted unsigned (bias 2^52, 0xafc88), divided by the script speed,
	/// truncated (saturating), then at least 300 by an unsigned compare (0xafcb4). A <c>ret</c> below 300 ms wraps
	/// to about 4.29 × 10⁹, which saturates to <see cref="int.MaxValue"/> ms (about 24.8 days).
	/// </summary>
	public static int WaitAnimDelay( int returned, float scriptSpeed )
	{
		var unsigned = unchecked((uint)(returned - MinimumWaitMilliseconds));
		var value = ObjectAnimator.Fctiwz( (float)unsigned / scriptSpeed );
		return unchecked((uint)value) >= MinimumWaitMilliseconds ? value : MinimumWaitMilliseconds;
	}

	/// <summary>
	/// WAITANIM_CH (0xb0330): unsigned <c>max(ret − 300, 300)</c> without the speed division and without
	/// <c>fctiwz</c>, so a <c>ret</c> below 300 ms gives 2³² − (300 − ret), which the deadline add wraps into the past.
	/// </summary>
	public static uint WaitAnimChannelDelay( int returned )
	{
		var unsigned = unchecked((uint)(returned - MinimumWaitMilliseconds));
		return unsigned >= MinimumWaitMilliseconds ? unsigned : MinimumWaitMilliseconds;
	}

	/// <summary>LOOPANIM token (0xafd2c): <c>anim + (sub &lt;&lt; 16)</c>.</summary>
	public static int LoopToken( int animation, int variant ) => unchecked(animation + (variant << 16));
}

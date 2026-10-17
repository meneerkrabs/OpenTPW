namespace OpenTPW;

public static partial class OpcodeHandlers
{
	/// <summary>
	/// Visitor handling (queue, heads, limbo, bouncing, walking). There is no visitor simulation yet, so
	/// every effect is hooked. Operand direction comes from corpus patterns: e.g. Bee shop
	/// `UNLIMBO VAR_LETMEOFF; BRANCH_Z none; ADD VAR_ONRIDE 0xFFFF` makes UNLIMBO an output with flags.
	/// </summary>
	public static class Visitors
	{
		[OpcodeHandler( Opcode.HUSH, RideOpcodeStatus.Implemented, "TPW-EXE 0x553D25: not a visitor operation; appends the resolved value to the script's heap (header stack size, up from index 0) and sets the flags; a full heap does nothing" )]
		public static void Hush( RideVM vm, Operand value ) => vm.HeapPush( value.Value );

		[OpcodeHandler( Opcode.HOP, RideOpcodeStatus.Implemented, "TPW-EXE 0x553DAA: pops the newest heap value (LIFO) into the flags and a variable destination; an empty heap does nothing (`HUSH VAR_LETMEON … HOP VAR_TEMP2; WALKOFF VAR_TEMP2`)" )]
		public static void Hop( RideVM vm, Operand dest )
		{
			if ( !vm.HeapPop( out var value ) )
				return;
			vm.SetFlags( value );
			dest.Value = value;
		}

		[OpcodeHandler( Opcode.ADDHEAD, RideOpcodeStatus.Hooked, "docs (`visitor`)" )]
		public static void AddHead( RideVM vm, Operand visitor ) => vm.Effect( Opcode.ADDHEAD, new[] { visitor } );

		[OpcodeHandler( Opcode.DELHEAD, RideOpcodeStatus.Hooked, "docs (`visitor`)" )]
		public static void DelHead( RideVM vm, Operand visitor ) => vm.Effect( Opcode.DELHEAD, new[] { visitor } );

		[OpcodeHandler( Opcode.LIMBO, RideOpcodeStatus.Implemented, "TPW-EXE 0x554DE5 (`value seconds`): the first free slot of the script's limbo table (header limbo size) takes the value and expiry now + seconds * 1000; result 1 (flags), 0 when the table is full" )]
		public static void Limbo( RideVM vm, Operand value, Operand seconds ) => vm.SetFlags( vm.Limbo( value.Value, seconds.Value ) );

		[OpcodeHandler( Opcode.UNLIMBO, RideOpcodeStatus.Implemented, "TPW-EXE 0x554EF0: releases the first occupied slot whose expiry is strictly past into the flags and a variable destination, else 0 (`UNLIMBO VAR_LETMEOFF; BRANCH_Z`)" )]
		public static void Unlimbo( RideVM vm, Operand dest ) => WriteResult( vm, dest, vm.Unlimbo() );

		[OpcodeHandler( Opcode.FORCEUNLIMBO, RideOpcodeStatus.Implemented, "TPW-EXE 0x554F74: variable destination only (anything else does nothing at all); releases the first occupied slot whatever its expiry, else 0 (`FORCEUNLIMBO VAR_LETMEOFF; BRANCH_NZ loop`)" )]
		public static void ForceUnlimbo( RideVM vm, Operand dest )
		{
			if ( dest.IsVariable )
				WriteResult( vm, dest, vm.ForceUnlimbo() );
		}

		[OpcodeHandler( Opcode.INLIMBO, RideOpcodeStatus.Implemented, "TPW-EXE 0x555012: the occupied slot count into the flags and the destination (`INLIMBO 0; BRANCH_Z`)" )]
		public static void InLimbo( RideVM vm, Operand dest ) => WriteResult( vm, dest, vm.LimboOccupied );

		[OpcodeHandler( Opcode.LIMBOSPACE, RideOpcodeStatus.Implemented, "TPW-EXE 0x555045: limbo size minus the occupied count into the flags and the destination (`LIMBOSPACE 0; BRANCH_Z full`)" )]
		public static void LimboSpace( RideVM vm, Operand dest ) => WriteResult( vm, dest, vm.LimboFree );

		// [BIN:TPW-EXE:0x555939 result tail] the result register (flags) takes the value and a variable destination is written; a literal destination only sets the flags
		private static void WriteResult( RideVM vm, Operand dest, int value )
		{
			vm.SetFlags( value );
			dest.Value = value;
		}

		[OpcodeHandler( Opcode.BOUNCESETNODE, RideOpcodeStatus.Hooked, "unknown upstream; passed through" )]
		public static void BounceSetNode( RideVM vm, Operand node ) => vm.Effect( Opcode.BOUNCESETNODE, new[] { node } );

		[OpcodeHandler( Opcode.BOUNCESETBASE, RideOpcodeStatus.Hooked, "unknown upstream; passed through" )]
		public static void BounceSetBase( RideVM vm, Operand @base ) => vm.Effect( Opcode.BOUNCESETBASE, new[] { @base } );

		[OpcodeHandler( Opcode.BOUNCE, RideOpcodeStatus.Hooked, "unknown upstream (`visitor unknown`); passed through" )]
		public static void Bounce( RideVM vm, Operand visitor, Operand unknown ) => vm.Effect( Opcode.BOUNCE, new[] { visitor, unknown } );

		[OpcodeHandler( Opcode.UNBOUNCE, RideOpcodeStatus.Hooked, "unknown upstream (`visitor`); passed through" )]
		public static void Unbounce( RideVM vm, Operand visitor ) => vm.Effect( Opcode.UNBOUNCE, new[] { visitor } );

		[OpcodeHandler( Opcode.FORCEUNBOUNCE, RideOpcodeStatus.Hooked, "unknown upstream (`visitor`); passed through" )]
		public static void ForceUnbounce( RideVM vm, Operand visitor ) => vm.Effect( Opcode.FORCEUNBOUNCE, new[] { visitor } );

		[OpcodeHandler( Opcode.BOUNCING, RideOpcodeStatus.Hooked, "unknown upstream; corpus `BOUNCING VAR_RUNNING; BRANCH_Z` → dest + flags" )]
		public static void Bouncing( RideVM vm, Operand dest ) => EffectResult( vm, Opcode.BOUNCING, dest, dest );

		[OpcodeHandler( Opcode.WALKON, RideOpcodeStatus.Hooked, "docs (`visitor u1 u2 u3 u4 action u5`, WALK_ACTION_* in ScriptDefs); passed through" )]
		public static void WalkOn( RideVM vm, Operand visitor, Operand a, Operand b, Operand c, Operand d, Operand action, Operand e )
			=> vm.Effect( Opcode.WALKON, new[] { visitor, a, b, c, d, action, e } );

		[OpcodeHandler( Opcode.WALKOFF, RideOpcodeStatus.Hooked, "unknown upstream (`visitor`); passed through" )]
		public static void WalkOff( RideVM vm, Operand visitor ) => vm.Effect( Opcode.WALKOFF, new[] { visitor } );

		[OpcodeHandler( Opcode.WALKGET, RideOpcodeStatus.Hooked, "unknown upstream (`dest`); corpus `WALKGET VAR_LETMEOFF; BRANCH_Z wait` → dest + flags" )]
		public static void WalkGet( RideVM vm, Operand dest ) => EffectResult( vm, Opcode.WALKGET, dest, dest );

		// [BIN:TPW-EXE:0x555B72 WALKST_FLOAT] three resolved operands, no effect: 0x557160 stores the clock at +0x80, the first operand x 1000 at +0x84 and the low words of the other two at +0x88 and +0x8A (the swing of the floating body, which only the render pass 0x5581D4 reads)
		[OpcodeHandler( Opcode.WALKST_FLOAT, RideOpcodeStatus.Implemented, "TPW-EXE 0x555B72 (`seconds a b`, resolved): starts the script's float: start = clock, duration = seconds x 1000 ms, a and b kept as words (the body's swing, render only); no effect; 1 corpus use (ZeroG: `WALKST_FLOAT VAR_DURATION 1000 1000`)" )]
		public static void WalkStartFloat( RideVM vm, Operand seconds, Operand a, Operand b ) => vm.StartWalkFloat( seconds.Value, a.Value, b.Value );

		// [BIN:TPW-EXE:0x555BD8 WALKFLOATSTAT] the flag word takes the duration word (+0x84) and a variable destination is written; the render pass clears the word once the clock is past start + duration (0x558051..0x558077)
		[OpcodeHandler( Opcode.WALKFLOATSTAT, RideOpcodeStatus.Implemented, "TPW-EXE 0x555BD8: the float's duration word (0 once start + duration has passed) into the flags and a variable destination; ZeroG loops `WALKFLOATSTAT 0; BRANCH_NZ` for VAR_DURATION seconds" )]
		public static void WalkFloatStat( RideVM vm, Operand dest ) => WriteResult( vm, dest, vm.WalkFloatDurationNow() );

		// [BIN:TPW-EXE:0x555C11 WALKFLOATSTOP] with a duration set: the share spent in thousandths moves the start back from now and the duration becomes 1000; without one nothing happens (no flags)
		[OpcodeHandler( Opcode.WALKFLOATSTOP, RideOpcodeStatus.Implemented, "TPW-EXE 0x555C11: with a float running, start = clock - the share spent (thousandths of the duration) and duration = 1000 (the float settles over the rest); no operands, no flags, no effect" )]
		public static void WalkFloatStop( RideVM vm ) => vm.StopWalkFloat();
	}

	/// <summary>
	/// Ride-type commands use reviewed Mac operand and accumulator contracts.
	/// Game effects remain external; unsupported queries preserve state and are recorded.
	/// </summary>
	public static class RideSystems
	{
		[OpcodeHandler( Opcode.TOUR, RideOpcodeStatus.Hooked, "Mac-static command roles: mutators preserve flags; supported queries set flags/output; required-variable gates skip invalid calls" )]
		public static void Tour( RideVM vm, Operand command, Operand parameter ) => Dispatch( vm, Opcode.TOUR, command, parameter );

		[OpcodeHandler( Opcode.BUMP, RideOpcodeStatus.Hooked, "Mac-static command roles: mutators preserve flags; queries set flags/output; duration commands set flags from original input" )]
		public static void Bump( RideVM vm, Operand command, Operand parameter ) => Dispatch( vm, Opcode.BUMP, command, parameter );

		[OpcodeHandler( Opcode.COAST, RideOpcodeStatus.Hooked, "Mac-static: commands 1/4/5/6/8 preserve flags; 2/3 set flags and optional output; 7 is a no-op" )]
		public static void Coast( RideVM vm, Operand command, Operand parameter ) => Dispatch( vm, Opcode.COAST, command, parameter );

		private enum CommandBehavior
		{
			PreserveInput, PreserveIgnored, ResultIgnored, RequiredInputResult,
			RequiredOutputResult, OptionalOutputResult, OriginalInput, NoOperation
		}

		private static CommandBehavior Behavior( Opcode opcode, int command ) => (opcode, command) switch
		{
			(Opcode.COAST, 1 or 4 or 5 or 6) => CommandBehavior.PreserveInput,
			(Opcode.COAST, 8) => CommandBehavior.PreserveIgnored,
			(Opcode.COAST, 2 or 3) => CommandBehavior.OptionalOutputResult,
			(Opcode.COAST, 7) => CommandBehavior.NoOperation,
			(Opcode.BUMP, 3 or 6 or 7 or 10) => CommandBehavior.PreserveIgnored,
			(Opcode.BUMP, 8 or 9 or 17) => CommandBehavior.PreserveInput,
			(Opcode.BUMP, 4 or 5 or 12 or 16) => CommandBehavior.ResultIgnored,
			(Opcode.BUMP, 1) => CommandBehavior.RequiredInputResult,
			(Opcode.BUMP, 2) => CommandBehavior.RequiredOutputResult,
			(Opcode.BUMP, 11) => CommandBehavior.OptionalOutputResult,
			(Opcode.BUMP, 13 or 14) => CommandBehavior.OriginalInput,
			(Opcode.TOUR, 1 or 5 or 8 or 9 or 12 or 14 or 17 or 18) => CommandBehavior.PreserveInput,
			(Opcode.TOUR, 2) => CommandBehavior.PreserveIgnored,
			(Opcode.TOUR, 3) => CommandBehavior.RequiredInputResult,
			(Opcode.TOUR, 4 or 16) => CommandBehavior.RequiredOutputResult,
			(Opcode.TOUR, 10 or 11 or 15) => CommandBehavior.ResultIgnored,
			_ => throw new RideScriptException( $"{opcode}: unreviewed controller command {command}" )
		};

		private static void Dispatch( RideVM vm, Opcode opcode, Operand command, Operand parameter )
		{
			if ( command.Kind != RideScriptOperandKind.Literal )
				throw new RideScriptException( $"{opcode} requires a literal command" );
			var behavior = Behavior( opcode, command.Raw );
			if ( behavior == CommandBehavior.NoOperation )
				return;
			if ( behavior is CommandBehavior.RequiredInputResult or CommandBehavior.RequiredOutputResult && !parameter.IsVariable )
				return;
			if ( behavior is CommandBehavior.PreserveInput or CommandBehavior.OriginalInput && parameter.Kind is not (RideScriptOperandKind.Literal or RideScriptOperandKind.Variable) )
				throw new RideScriptException( $"{opcode} command {command.Raw} requires a numeric input" );
			var operands = new[] { command, parameter };
			if ( behavior is CommandBehavior.PreserveInput or CommandBehavior.PreserveIgnored )
			{
				vm.Effect( opcode, operands );
				return;
			}
			var previousParameter = parameter.Value;
			if ( behavior == CommandBehavior.OriginalInput )
			{
				vm.Effect( opcode, operands );
				vm.SetFlags( previousParameter );
				return;
			}
			var output = behavior is CommandBehavior.RequiredOutputResult or CommandBehavior.OptionalOutputResult;
			if ( !vm.TryEffect( opcode, operands, out var result ) )
			{
				if ( output )
					parameter.Value = previousParameter;
				return;
			}
			if ( output )
				parameter.Value = result;
			vm.SetFlags( result );
		}
	}
}

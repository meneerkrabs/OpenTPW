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
		[OpcodeHandler( Opcode.HUSH, RideOpcodeStatus.Hooked, "unknown upstream; corpus input (`HUSH VAR_LETMEON` before boarding)" )]
		public static void Hush( RideVM vm, Operand visitor ) => vm.Effect( Opcode.HUSH, new[] { visitor } );

		[OpcodeHandler( Opcode.HOP, RideOpcodeStatus.Hooked, "unknown upstream; corpus output (`HOP VAR_TEMP2; WALKOFF VAR_TEMP2`)" )]
		public static void Hop( RideVM vm, Operand dest ) => dest.Value = vm.Effect( Opcode.HOP, new[] { dest } );

		[OpcodeHandler( Opcode.ADDHEAD, RideOpcodeStatus.Hooked, "docs (`visitor`)" )]
		public static void AddHead( RideVM vm, Operand visitor ) => vm.Effect( Opcode.ADDHEAD, new[] { visitor } );

		[OpcodeHandler( Opcode.DELHEAD, RideOpcodeStatus.Hooked, "docs (`visitor`)" )]
		public static void DelHead( RideVM vm, Operand visitor ) => vm.Effect( Opcode.DELHEAD, new[] { visitor } );

		[OpcodeHandler( Opcode.LIMBO, RideOpcodeStatus.Hooked, "docs (`visitor unknown`)" )]
		public static void Limbo( RideVM vm, Operand visitor, Operand unknown ) => vm.Effect( Opcode.LIMBO, new[] { visitor, unknown } );

		[OpcodeHandler( Opcode.UNLIMBO, RideOpcodeStatus.Hooked, "docs say input; corpus output + flags (`UNLIMBO VAR_LETMEOFF; BRANCH_Z`)" )]
		public static void Unlimbo( RideVM vm, Operand dest ) => EffectResult( vm, Opcode.UNLIMBO, dest, dest );

		[OpcodeHandler( Opcode.FORCEUNLIMBO, RideOpcodeStatus.Hooked, "docs say input; corpus output + flags (`FORCEUNLIMBO VAR_LETMEOFF; BRANCH_NZ loop`)" )]
		public static void ForceUnlimbo( RideVM vm, Operand dest ) => EffectResult( vm, Opcode.FORCEUNLIMBO, dest, dest );

		[OpcodeHandler( Opcode.INLIMBO, RideOpcodeStatus.Hooked, "unknown upstream; corpus `INLIMBO 0; BRANCH_Z` → dest + flags" )]
		public static void InLimbo( RideVM vm, Operand dest ) => EffectResult( vm, Opcode.INLIMBO, dest, dest );

		[OpcodeHandler( Opcode.LIMBOSPACE, RideOpcodeStatus.Hooked, "unknown upstream; corpus `LIMBOSPACE 0; BRANCH_Z full` → dest + flags" )]
		public static void LimboSpace( RideVM vm, Operand dest ) => EffectResult( vm, Opcode.LIMBOSPACE, dest, dest );

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

		[OpcodeHandler( Opcode.WALKST_FLOAT, RideOpcodeStatus.Hooked, "unknown upstream; 1 corpus use (ZeroG); passed through" )]
		public static void WalkStartFloat( RideVM vm, Operand a, Operand b, Operand c ) => vm.Effect( Opcode.WALKST_FLOAT, new[] { a, b, c } );

		[OpcodeHandler( Opcode.WALKFLOATSTAT, RideOpcodeStatus.Hooked, "unknown upstream; corpus `WALKFLOATSTAT 0; BRANCH_NZ` → dest + flags" )]
		public static void WalkFloatStat( RideVM vm, Operand dest ) => EffectResult( vm, Opcode.WALKFLOATSTAT, dest, dest );

		[OpcodeHandler( Opcode.WALKFLOATSTOP, RideOpcodeStatus.Hooked, "unknown upstream; 1 corpus use; passed through" )]
		public static void WalkFloatStop( RideVM vm ) => vm.Effect( Opcode.WALKFLOATSTOP, Array.Empty<Operand>() );
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

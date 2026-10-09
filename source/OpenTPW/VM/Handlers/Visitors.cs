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
	/// Ride-type command opcodes (`command parameter`). Whether the parameter is an input or an output
	/// depends on the command (docs: BUMP 11 "get cars on ride"; corpus `COAST 3 VAR_LETMEOFF; BRANCH_Z`),
	/// so the effects layer writes it through <see cref="RideEffectCall.SetOutput"/>; the return value sets flags.
	/// </summary>
	public static class RideSystems
	{
		[OpcodeHandler( Opcode.TOUR, RideOpcodeStatus.Hooked, "docs (command list); corpus `TOUR 10 0; BRANCH_Z` → result sets flags" )]
		public static void Tour( RideVM vm, Operand command, Operand parameter ) => vm.SetFlags( vm.Effect( Opcode.TOUR, new[] { command, parameter } ) );

		[OpcodeHandler( Opcode.BUMP, RideOpcodeStatus.Hooked, "docs (command list); corpus 57 branches consume BUMP flags" )]
		public static void Bump( RideVM vm, Operand command, Operand parameter ) => vm.SetFlags( vm.Effect( Opcode.BUMP, new[] { command, parameter } ) );

		[OpcodeHandler( Opcode.COAST, RideOpcodeStatus.Hooked, "docs (COAST_* ids); corpus 36 branches consume COAST flags" )]
		public static void Coast( RideVM vm, Operand command, Operand parameter ) => vm.SetFlags( vm.Effect( Opcode.COAST, new[] { command, parameter } ) );
	}
}

namespace OpenTPW;

public static partial class OpcodeHandlers
{
	/// <summary>
	/// Child scripts and cross-script variables. Variable IDs are indices into the other script's variable table
	/// (`SETVARINCHILD 1 1` in a parent, `GETVARINPARENT VAR_DOORSTATE 17` in its child).
	/// </summary>
	public static class Children
	{
		[OpcodeHandler( Opcode.SPAWNCHILD, RideOpcodeStatus.Implemented, "docs (one child); corpus: all 20 names resolve to an RSE in the same WAD; the child runs after its parent each tick" )]
		public static void SpawnChild( RideVM vm, Operand fileName ) => vm.SpawnChild( fileName.Text, sound: false );

		[OpcodeHandler( Opcode.SPAWNSOUND, RideOpcodeStatus.Implemented, "docs; corpus: always `EventMap.rse`, which only fills VAR_EVT0…9/VAR_PAR0 and idles; runs in its own slot (bumper.RSE has both). What the engine reads from it is not implemented" )]
		public static void SpawnSound( RideVM vm, Operand fileName ) => vm.SpawnChild( fileName.Text, sound: true );

		[OpcodeHandler( Opcode.REMOVECHILD, RideOpcodeStatus.Implemented, "docs" )]
		public static void RemoveChild( RideVM vm ) => vm.RemoveChild();

		[OpcodeHandler( Opcode.SETVARINCHILD, RideOpcodeStatus.Implemented, "docs (`variable value`)" )]
		public static void SetVarInChild( RideVM vm, Operand variable, Operand value )
			=> RideVM.WriteVariable( vm.Child ?? throw new RideScriptException( "no child script" ), variable.Value, value.Value );

		[OpcodeHandler( Opcode.GETVARINCHILD, RideOpcodeStatus.Implemented, "docs (`dest variable`)" )]
		public static void GetVarInChild( RideVM vm, Operand dest, Operand variable )
			=> dest.Value = RideVM.ReadVariable( vm.Child ?? throw new RideScriptException( "no child script" ), variable.Value );

		[OpcodeHandler( Opcode.SETVARINPARENT, RideOpcodeStatus.Implemented, "docs only (no corpus use); mirror of SETVARINCHILD" )]
		public static void SetVarInParent( RideVM vm, Operand variable, Operand value )
			=> RideVM.WriteVariable( vm.Parent ?? throw new RideScriptException( "no parent script" ), variable.Value, value.Value );

		[OpcodeHandler( Opcode.GETVARINPARENT, RideOpcodeStatus.Implemented, "docs (`dest variable`)" )]
		public static void GetVarInParent( RideVM vm, Operand dest, Operand variable )
			=> dest.Value = RideVM.ReadVariable( vm.Parent ?? throw new RideScriptException( "no parent script" ), variable.Value );

		[OpcodeHandler( Opcode.FINDSCRIPTRAND, RideOpcodeStatus.Implemented, "docs (`name dest`); corpus: `FINDSCRIPTRAND \"Zob Upgrade\" VAR_SCRIPTID; BRANCH_Z none` → dest = random live script with that NAME or 0, sets flags" )]
		public static void FindScriptRand( RideVM vm, Operand name, Operand dest )
		{
			var id = vm.FindScript( name.Text );
			dest.Value = id;
			vm.SetFlags( id );
		}

		[OpcodeHandler( Opcode.GETREMOTEVAR, RideOpcodeStatus.Implemented, "corpus: `GETREMOTEVAR 0 VAR_SCRIPTID 0; BRANCH_Z` → `dest script variable`, sets flags; mirror of SETREMOTEVAR; a missing script reads 0" )]
		public static void GetRemoteVar( RideVM vm, Operand dest, Operand scriptId, Operand variable )
		{
			var remote = vm.World.Find( scriptId.Value );
			var value = remote == null ? 0 : RideVM.ReadVariable( remote, variable.Value );
			dest.Value = value;
			vm.SetFlags( value );
		}

		[OpcodeHandler( Opcode.SETREMOTEVAR, RideOpcodeStatus.Implemented, "docs (`script variable value`); bus.RSE writes to the FINDSCRIPTRAND result without checking it, so a missing script (ID 0) is ignored" )]
		public static void SetRemoteVar( RideVM vm, Operand scriptId, Operand variable, Operand value )
		{
			var remote = vm.World.Find( scriptId.Value );
			if ( remote != null )
				RideVM.WriteVariable( remote, variable.Value, value.Value );
		}
	}
}

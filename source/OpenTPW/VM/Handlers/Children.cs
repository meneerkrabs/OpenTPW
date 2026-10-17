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

		// [BIN:TPW-EXE:0x555282 SETVARINCHILD] two resolved operands (`variable value`). The child is the script whose id the script keeps at +0xC (0: the handler leaves at once, flags untouched); a variable index at or above the child's variable count does nothing either (0x5554CA: a signed compare, so a negative index is a write before the array, which the VM refuses as a fault). A write also sets the flags to the value (0x5554E4)
		[OpcodeHandler( Opcode.SETVARINCHILD, RideOpcodeStatus.Implemented, "TPW-EXE 0x555282 (`variable value`, resolved): writes the child's variable (the script id at +0xC; none, or an index at or above its variable count: nothing, no flags) and sets the flags to the value" )]
		public static void SetVarInChild( RideVM vm, Operand variable, Operand value )
			=> WriteOther( vm, vm.Child, variable.Value, value.Value );

		// [BIN:TPW-EXE:0x555349 GETVARINCHILD] the destination is the first operand, raw: a literal leaves at once; then as SETVARINCHILD for the child lookup and the index; the value goes to the flags (0x5555C5) and the variable (0x5555CB)
		[OpcodeHandler( Opcode.GETVARINCHILD, RideOpcodeStatus.Implemented, "TPW-EXE 0x555349 (`dest variable`: the destination raw, the variable resolved): reads the child's variable into the flags and a variable destination (a literal destination, no child or an index at or above its variable count: nothing, no flags)" )]
		public static void GetVarInChild( RideVM vm, Operand dest, Operand variable )
			=> ReadOther( vm, vm.Child, dest, variable.Value );

		// [BIN:TPW-EXE:0x555402 SETVARINPARENT] the same with the parent id at +0x10
		[OpcodeHandler( Opcode.SETVARINPARENT, RideOpcodeStatus.Implemented, "TPW-EXE 0x555402 (`variable value`, resolved): SETVARINCHILD for the parent (the script id at +0x10); no corpus use" )]
		public static void SetVarInParent( RideVM vm, Operand variable, Operand value )
			=> WriteOther( vm, vm.Parent, variable.Value, value.Value );

		// [BIN:TPW-EXE:0x5554F2 GETVARINPARENT] the same with the parent id at +0x10
		[OpcodeHandler( Opcode.GETVARINPARENT, RideOpcodeStatus.Implemented, "TPW-EXE 0x5554F2 (`dest variable`): GETVARINCHILD for the parent (the script id at +0x10)" )]
		public static void GetVarInParent( RideVM vm, Operand dest, Operand variable )
			=> ReadOther( vm, vm.Parent, dest, variable.Value );

		private static void WriteOther( RideVM vm, RideVM? other, int index, int value )
		{
			if ( other == null || index >= other.Variables.Length )
				return;
			RideVM.WriteVariable( other, index, value );
			vm.SetFlags( value );
		}

		private static void ReadOther( RideVM vm, RideVM? other, Operand dest, int index )
		{
			if ( !dest.IsVariable || other == null || index >= other.Variables.Length )
				return;
			var value = RideVM.ReadVariable( other, index );
			vm.SetFlags( value );
			dest.Value = value;
		}

		// [BIN:TPW-EXE:0x55601A FINDSCRIPTRAND] the first operand must be a string (otherwise the flags are 0 and nothing else happens); the flags start at 0; the live scripts with that NAME are counted, and with none the destination stays as it was; otherwise the one at the 1-based index (the draw shifted right by one, as an absolute value, modulo the count, plus one: 0x5560CB) gives its id to the flags and a variable destination
		[OpcodeHandler( Opcode.FINDSCRIPTRAND, RideOpcodeStatus.Implemented, "TPW-EXE 0x55601A (`name dest`: a string operand, the destination raw): a random live script with that NAME (one world draw when any matches) gives its id to the flags and a variable destination; with no match the flags are 0 and the destination keeps its value (corpus: `FINDSCRIPTRAND \"Zob Upgrade\" VAR_SCRIPTID; BRANCH_Z none`)" )]
		public static void FindScriptRand( RideVM vm, Operand name, Operand dest )
		{
			vm.SetFlags( 0 );
			if ( name.Kind != RideScriptOperandKind.String )
				return;
			var id = vm.FindScript( name.Text );
			if ( id == 0 )
				return;
			vm.SetFlags( id );
			dest.Value = id;
		}

		// [BIN:TPW-EXE:0x55617F GETREMOTEVAR] the flags are cleared first; the script comes from the id (0x55A070: none leaves with the destination untouched), a negative index or one at or above its variable count too; the value goes to the flags and a variable destination
		[OpcodeHandler( Opcode.GETREMOTEVAR, RideOpcodeStatus.Implemented, "TPW-EXE 0x55617F (`dest script variable`: the destination raw, the others resolved): the flags are cleared, then the other script's variable goes to the flags and a variable destination; an unknown script id or an index outside its variables leaves the destination as it was (corpus: `GETREMOTEVAR 0 VAR_SCRIPTID 0; BRANCH_Z`)" )]
		public static void GetRemoteVar( RideVM vm, Operand dest, Operand scriptId, Operand variable )
		{
			vm.SetFlags( 0 );
			var remote = vm.World.Find( scriptId.Value );
			if ( remote == null || variable.Value < 0 || variable.Value >= remote.Variables.Length )
				return;
			var value = RideVM.ReadVariable( remote, variable.Value );
			vm.SetFlags( value );
			dest.Value = value;
		}

		// [BIN:TPW-EXE:0x556216 SETREMOTEVAR] an unknown script id leaves at once; a negative index or one at or above the variable count logs (0x765B78) and does nothing; a write sets the flags to the value (0x55627E)
		[OpcodeHandler( Opcode.SETREMOTEVAR, RideOpcodeStatus.Implemented, "TPW-EXE 0x556216 (`script variable value`, resolved): writes the other script's variable and sets the flags to the value; an unknown script id (bus.RSE writes to the FINDSCRIPTRAND result without checking it) or an index outside its variables does nothing" )]
		public static void SetRemoteVar( RideVM vm, Operand scriptId, Operand variable, Operand value )
		{
			var remote = vm.World.Find( scriptId.Value );
			if ( remote == null || variable.Value < 0 || variable.Value >= remote.Variables.Length )
				return;
			RideVM.WriteVariable( remote, variable.Value, value.Value );
			vm.SetFlags( value.Value );
		}
	}
}

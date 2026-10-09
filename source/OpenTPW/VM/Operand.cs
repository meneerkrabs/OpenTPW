namespace OpenTPW;

/// <summary>
/// One operand of a loaded RSE instruction, bound to the VM that owns it.
/// </summary>
public sealed class Operand
{
	private readonly RideVM vm;

	public RideScriptOperandKind Kind { get; }

	/// <summary>Raw low 16 bits of the code word.</summary>
	public ushort Raw { get; }

	/// <summary>Instruction index of a branch operand's target (code-word index resolved at load time), otherwise -1.</summary>
	public int BranchTarget { get; }

	internal Operand( RideVM vm, RideScriptOperand operand, int branchTarget )
	{
		this.vm = vm;
		Kind = operand.Kind;
		Raw = operand.Value;
		BranchTarget = branchTarget;
	}

	public bool IsVariable => Kind == RideScriptOperandKind.Variable;

	/// <summary>
	/// Variables read the VM's 32-bit variable; literals are sign-extended 16-bit values
	/// (0xFFFF is the corpus's only literal ≥ 0x8000 and is used as -1 by count-down loops such as
	/// <c>ADD VAR_COUNT 0xFFFF; BRANCH_PV</c>). Writes to anything but a variable are discarded:
	/// the corpus uses a literal 0 destination when only the flags are wanted (<c>MOD 0 VAR_CAPACITY 9; BRANCH_Z</c>).
	/// </summary>
	public int Value
	{
		get => Kind switch
		{
			RideScriptOperandKind.Variable => vm.Variables[Raw],
			RideScriptOperandKind.Literal => (short)Raw,
			_ => Raw
		};
		set
		{
			if ( Kind == RideScriptOperandKind.Variable )
				vm.Variables[Raw] = value;
		}
	}

	/// <summary>The referenced string for string operands.</summary>
	public string Text => Kind == RideScriptOperandKind.String
		? vm.Script.Strings[Raw]
		: throw new RideScriptException( $"Expected a string operand, found {Kind}." );

	public int RequireBranchTarget() => Kind == RideScriptOperandKind.Branch
		? BranchTarget
		: throw new RideScriptException( $"Expected a branch operand, found {Kind}." );

	public override string ToString() => Kind switch
	{
		RideScriptOperandKind.Variable => vm.Script.VariableNames[Raw],
		RideScriptOperandKind.Branch => $"label_{Raw}",
		RideScriptOperandKind.String => $"\"{vm.Script.Strings[Raw]}\"",
		_ => ((short)Raw).ToString()
	};
}

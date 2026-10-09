namespace OpenTPW;

/// <summary>
/// A loaded RSE instruction: opcode word plus its operands, with its handler resolved at load time.
/// </summary>
public sealed class Instruction
{
	public int Index { get; }
	public int WordOffset { get; }
	public ushort RawOpcode { get; }
	public Opcode Opcode => (Opcode)RawOpcode;
	public IReadOnlyList<Operand> Operands => operands;

	internal readonly Operand[] operands;
	internal readonly RideOpcodeDefinition? Definition;

	internal Instruction( int index, int wordOffset, ushort rawOpcode, Operand[] operands, RideOpcodeDefinition? definition )
	{
		Index = index;
		WordOffset = wordOffset;
		RawOpcode = rawOpcode;
		this.operands = operands;
		Definition = definition;
	}

	public override string ToString() => operands.Length == 0
		? RideScriptAnalysis.GetOpcodeName( RawOpcode )
		: $"{RideScriptAnalysis.GetOpcodeName( RawOpcode )} {string.Join( " ", operands.Select( x => x.ToString() ) )}";
}

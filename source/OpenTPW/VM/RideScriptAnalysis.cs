using System.Reflection;
using System.Text;

namespace OpenTPW;

/// <summary>
/// Static (non-executing) inventory of one or more parsed RSE scripts.
/// Records observations only; it does not interpret operand meanings.
/// </summary>
public sealed class RideScriptInventory
{
	public int FileCount { get; internal set; }
	public int CodeWordCount { get; internal set; }
	public int InstructionCount { get; internal set; }
	public SortedDictionary<ushort, int> OpcodeHistogram { get; } = new();
	public SortedDictionary<ushort, SortedSet<int>> ObservedOperandCounts { get; } = new();

	/// <summary>Opcode → operand-kind signature (L literal, S string, B branch, V variable) → count.</summary>
	public SortedDictionary<ushort, SortedDictionary<string, int>> OperandSignatures { get; } = new();
	public SortedDictionary<RideScriptOperandKind, int> OperandKindCounts { get; } = new();

	/// <summary>Opcode → referenced string → count (script names, child script file names, etc).</summary>
	public SortedDictionary<ushort, SortedDictionary<string, int>> StringReferences { get; } = new( );

	/// <summary>Raw EVENT operand tuples (literals as hex, variables by name) → count.</summary>
	public SortedDictionary<string, int> EventOperands { get; } = new( StringComparer.Ordinal );
	public SortedDictionary<string, int> VariableNames { get; } = new( StringComparer.Ordinal );
	public SortedDictionary<ushort, int> FinalOpcodes { get; } = new();

	/// <summary>Opcodes outside the <see cref="Opcode"/> name table → count.</summary>
	public SortedDictionary<ushort, int> UnknownOpcodes { get; } = new();

	/// <summary>Opcode + observed count that disagrees with the documented signature operand count.</summary>
	public List<string> OperandCountMismatches { get; } = new();
	public SortedDictionary<int, int> TimeSliceValues { get; } = new();

	public IEnumerable<ushort> UsedWithoutHandler => OpcodeHistogram.Keys.Where( x => !RideScriptAnalysis.ImplementedOpcodes.Contains( (Opcode)x ) );
	public IEnumerable<Opcode> HandlerWithoutCorpusUse => RideScriptAnalysis.ImplementedOpcodes.Where( x => !OpcodeHistogram.ContainsKey( (ushort)x ) ).OrderBy( x => x );
	public IEnumerable<Opcode> NamedButUnused => Enum.GetValues<Opcode>().Where( x => !OpcodeHistogram.ContainsKey( (ushort)x ) );

	/// <summary>Stable "opcode:count,..." string used to pin the corpus histogram.</summary>
	public string HistogramKey => string.Join( ",", OpcodeHistogram.Select( x => $"{x.Key}:{x.Value}" ) );

	public string Format()
	{
		var builder = new StringBuilder();
		builder.AppendLine( $"files={FileCount} words={CodeWordCount} instructions={InstructionCount} distinct-opcodes={OpcodeHistogram.Count}" );
		builder.AppendLine( "operand kinds: " + string.Join( ", ", OperandKindCounts.Select( x => $"{x.Key}={x.Value}" ) ) );
		builder.AppendLine( "opcode count operands signatures handler" );
		foreach ( var (opcode, count) in OpcodeHistogram )
		{
			var signatures = string.Join( " ", OperandSignatures[opcode].Select( x => $"{(x.Key.Length == 0 ? "-" : x.Key)}={x.Value}" ) );
			var handler = RideScriptAnalysis.ImplementedOpcodes.Contains( (Opcode)opcode ) ? "yes" : "no";
			builder.AppendLine( $"{opcode,3} {RideScriptAnalysis.GetOpcodeName( opcode ),-16} {count,5} {string.Join( "/", ObservedOperandCounts[opcode] ),3} {signatures} {handler}" );
		}
		builder.AppendLine( "unknown opcodes: " + (UnknownOpcodes.Count == 0 ? "none" : string.Join( ", ", UnknownOpcodes.Select( x => $"{x.Key}={x.Value}" ) )) );
		builder.AppendLine( "operand count mismatches: " + (OperandCountMismatches.Count == 0 ? "none" : string.Join( "; ", OperandCountMismatches )) );
		builder.AppendLine( "used without VM handler: " + string.Join( " ", UsedWithoutHandler.Select( RideScriptAnalysis.GetOpcodeName ) ) );
		builder.AppendLine( "VM handler without corpus use: " + string.Join( " ", HandlerWithoutCorpusUse ) );
		builder.AppendLine( "named but unused: " + string.Join( " ", NamedButUnused ) );
		builder.AppendLine( "final opcodes: " + string.Join( ", ", FinalOpcodes.Select( x => $"{RideScriptAnalysis.GetOpcodeName( x.Key )}={x.Value}" ) ) );
		builder.AppendLine( "time slices: " + string.Join( ", ", TimeSliceValues.Select( x => $"{x.Key}={x.Value}" ) ) );
		foreach ( var (opcode, strings) in StringReferences )
			builder.AppendLine( $"strings via {RideScriptAnalysis.GetOpcodeName( opcode )}: {strings.Count} distinct, {strings.Values.Sum()} refs: " + string.Join( " | ", strings.Keys ) );
		builder.AppendLine( $"EVENT operand tuples: {EventOperands.Count} distinct" );
		foreach ( var (tuple, count) in EventOperands.OrderByDescending( x => x.Value ).ThenBy( x => x.Key, StringComparer.Ordinal ) )
			builder.AppendLine( $"  {tuple} x{count}" );
		builder.AppendLine( $"variable names: {VariableNames.Count} distinct" );
		return builder.ToString();
	}
}

public static class RideScriptAnalysis
{
	/// <summary>
	/// Operand counts taken from the signature lines of the upstream OpenTPW docs
	/// (rsse-vm-instructions.md). Absent keys are undocumented (CRIT_UNLOCK).
	/// Many documented signatures are themselves labelled "Unknown" upstream.
	/// </summary>
	public static IReadOnlyDictionary<Opcode, int> DocumentedOperandCounts { get; } = new Dictionary<Opcode, int>
	{
		[Opcode.NOP] = 0, [Opcode.CRIT_LOCK] = 0, [Opcode.COPY] = 2, [Opcode.SETLV] = 1, [Opcode.SUB] = 3,
		[Opcode.ENDSLICE] = 0, [Opcode.GETTIME] = 1, [Opcode.ADDOBJ] = 4, [Opcode.ADDOBJ_EXT] = 5, [Opcode.KILLOBJ] = 1,
		[Opcode.FADEOBJ] = 1, [Opcode.SETOBJPARAM] = 3, [Opcode.EVENT] = 3, [Opcode.EVENT_EXT] = 4, [Opcode.FLUSHANIM] = 0,
		[Opcode.TRIGANIM] = 3, [Opcode.WAITANIM] = 2, [Opcode.LOOPANIM] = 2, [Opcode.TRIGWAITANIM] = 3, [Opcode.GETANIM] = 1,
		[Opcode.TRIGANIMSPEED] = 4, [Opcode.FLUSHANIM_CH] = 1, [Opcode.TRIGANIM_CH] = 4, [Opcode.WAITANIM_CH] = 3,
		[Opcode.LOOPANIM_CH] = 3, [Opcode.TRIGWAITANIM_CH] = 4, [Opcode.GETANIM_CH] = 2, [Opcode.RAND] = 2, [Opcode.JSR] = 1,
		[Opcode.RETURN] = 0, [Opcode.BRANCH] = 1, [Opcode.BRANCH_Z] = 1, [Opcode.BRANCH_NZ] = 1, [Opcode.BRANCH_NV] = 1,
		[Opcode.BRANCH_PV] = 1, [Opcode.DBGMSG] = 1, [Opcode.NAME] = 1, [Opcode.TEST] = 1, [Opcode.CMP] = 2, [Opcode.PUSH] = 1,
		[Opcode.POP] = 0, [Opcode.HUSH] = 1, [Opcode.HOP] = 1, [Opcode.WAIT] = 1, [Opcode.WAITABS] = 1, [Opcode.WAIT4ANIM] = 0,
		[Opcode.ADD] = 2, [Opcode.MULT] = 3, [Opcode.DIV] = 3, [Opcode.MOD] = 3, [Opcode.TURBO] = 1, [Opcode.END] = 0,
		[Opcode.TOUR] = 2, [Opcode.BUMP] = 2, [Opcode.COAST] = 2, [Opcode.ADDHEAD] = 1, [Opcode.DELHEAD] = 1, [Opcode.LIMBO] = 2,
		[Opcode.UNLIMBO] = 1, [Opcode.FORCEUNLIMBO] = 1, [Opcode.INLIMBO] = 1, [Opcode.LIMBOSPACE] = 1, [Opcode.SPAWNCHILD] = 1,
		[Opcode.SPAWNSOUND] = 1, [Opcode.REMOVECHILD] = 0, [Opcode.SETVARINCHILD] = 2, [Opcode.GETVARINCHILD] = 2,
		[Opcode.SETVARINPARENT] = 2, [Opcode.GETVARINPARENT] = 2, [Opcode.BOUNCESETNODE] = 1, [Opcode.BOUNCESETBASE] = 1,
		[Opcode.BOUNCE] = 2, [Opcode.UNBOUNCE] = 1, [Opcode.FORCEUNBOUNCE] = 1, [Opcode.BOUNCING] = 1, [Opcode.WALKON] = 7,
		[Opcode.WALKOFF] = 1, [Opcode.WALKGET] = 1, [Opcode.WALKST_FLOAT] = 3, [Opcode.WALKFLOATSTAT] = 1, [Opcode.WALKFLOATSTOP] = 0,
		[Opcode.ENABLELIGHT] = 1, [Opcode.DISABLELIGHT] = 1, [Opcode.SETLIGHT] = 1, [Opcode.COLOURLIGHT] = 4, [Opcode.STARTSCREAM] = 2,
		[Opcode.STOPSCREAM] = 0, [Opcode.SINGLESCREAM] = 2, [Opcode.SCREAMLEVEL] = 1, [Opcode.FINDSCRIPTRAND] = 2,
		[Opcode.GETREMOTEVAR] = 3, [Opcode.SETREMOTEVAR] = 3, [Opcode.REPAIREFFECT] = 1, [Opcode.GETCUSTPTCLCODE] = 2,
		[Opcode.SETTIMER] = 1, [Opcode.GETTIMER] = 1, [Opcode.YEAR] = 1, [Opcode.MONTH] = 1, [Opcode.DAY] = 1, [Opcode.HOUR] = 1,
		[Opcode.MIN] = 1, [Opcode.SEC] = 1, [Opcode.SETREVERB] = 1, [Opcode.DIPMUSIC] = 1, [Opcode.SPARK] = 4
	};

	/// <summary>Opcodes with an <see cref="OpcodeHandlerAttribute"/> method in the current VM (many are TODO stubs).</summary>
	public static IReadOnlySet<Opcode> ImplementedOpcodes { get; } = typeof( RideVM ).Assembly.GetTypes()
		.SelectMany( type => type.GetMethods() )
		.Select( method => method.GetCustomAttribute<OpcodeHandlerAttribute>() )
		.Where( attribute => attribute != null )
		.Select( attribute => attribute!.Opcode )
		.ToHashSet();

	public static bool IsKnownOpcode( ushort opcode ) => Enum.IsDefined( typeof( Opcode ), (int)opcode );

	public static string GetOpcodeName( ushort opcode ) => IsKnownOpcode( opcode ) ? ((Opcode)opcode).ToString() : $"OP_{opcode}";

	private static char KindCode( RideScriptOperandKind kind ) => kind switch
	{
		RideScriptOperandKind.Literal => 'L',
		RideScriptOperandKind.String => 'S',
		RideScriptOperandKind.Branch => 'B',
		RideScriptOperandKind.Variable => 'V',
		_ => '?'
	};

	/// <summary>
	/// Implementation-independent instruction listing for hashing:
	/// one line per instruction, "word:opcode" followed by " Kvalue" per operand (raw decimal values).
	/// </summary>
	public static string CanonicalListing( RideScriptFile script )
	{
		var builder = new StringBuilder();
		foreach ( var instruction in script.Instructions )
		{
			builder.Append( instruction.WordOffset ).Append( ':' ).Append( instruction.Opcode );
			foreach ( var operand in instruction.Operands )
				builder.Append( ' ' ).Append( KindCode( operand.Kind ) ).Append( operand.Value );
			builder.Append( '\n' );
		}
		return builder.ToString();
	}

	/// <summary>
	/// Human-readable listing. Labels are code-word indices; literals ≥ 0x8000 are shown in hex
	/// because their signedness is not verified.
	/// </summary>
	public static string Disassemble( RideScriptFile script )
	{
		var targets = script.Instructions.SelectMany( x => x.Operands ).Where( x => x.Kind == RideScriptOperandKind.Branch ).Select( x => (int)x.Value ).ToHashSet();
		var builder = new StringBuilder();
		builder.AppendLine( $"; variables={script.VariableCount} stack={script.StackSize} timeslice={script.TimeSlice} limbo={script.LimboSize} bounce={script.BounceSize} walk={script.WalkSize}" );
		foreach ( var instruction in script.Instructions )
		{
			if ( targets.Contains( instruction.WordOffset ) )
				builder.AppendLine( $".label_{instruction.WordOffset}" );
			builder.Append( '\t' ).Append( GetOpcodeName( instruction.Opcode ) );
			foreach ( var operand in instruction.Operands )
			{
				builder.Append( ' ' ).Append( operand.Kind switch
				{
					RideScriptOperandKind.String => $"\"{script.Strings[operand.Value]}\"",
					RideScriptOperandKind.Branch => $"label_{operand.Value}",
					RideScriptOperandKind.Variable => script.VariableNames[operand.Value],
					_ => operand.Value >= 0x8000 ? $"0x{operand.Value:X4}" : operand.Value.ToString()
				} );
			}
			builder.AppendLine();
		}
		return builder.ToString();
	}

	public static RideScriptInventory BuildInventory( IEnumerable<RideScriptFile> scripts )
	{
		var inventory = new RideScriptInventory();
		foreach ( var script in scripts )
		{
			inventory.FileCount++;
			inventory.CodeWordCount += script.CodeWordCount;
			inventory.InstructionCount += script.Instructions.Count;
			Increment( inventory.TimeSliceValues, script.TimeSlice );
			foreach ( var name in script.VariableNames )
				Increment( inventory.VariableNames, name );
			if ( script.Instructions.Count > 0 )
				Increment( inventory.FinalOpcodes, script.Instructions[^1].Opcode );
			foreach ( var instruction in script.Instructions )
			{
				var opcode = instruction.Opcode;
				Increment( inventory.OpcodeHistogram, opcode );
				if ( !IsKnownOpcode( opcode ) )
					Increment( inventory.UnknownOpcodes, opcode );
				if ( !inventory.ObservedOperandCounts.TryGetValue( opcode, out var counts ) )
					inventory.ObservedOperandCounts[opcode] = counts = new SortedSet<int>();
				counts.Add( instruction.Operands.Count );
				if ( !inventory.OperandSignatures.TryGetValue( opcode, out var signatures ) )
					inventory.OperandSignatures[opcode] = signatures = new SortedDictionary<string, int>( StringComparer.Ordinal );
				Increment( signatures, new string( instruction.Operands.Select( x => KindCode( x.Kind ) ).ToArray() ) );
				foreach ( var operand in instruction.Operands )
				{
					Increment( inventory.OperandKindCounts, operand.Kind );
					if ( operand.Kind != RideScriptOperandKind.String )
						continue;
					if ( !inventory.StringReferences.TryGetValue( opcode, out var strings ) )
						inventory.StringReferences[opcode] = strings = new SortedDictionary<string, int>( StringComparer.Ordinal );
					Increment( strings, script.Strings[operand.Value] );
				}
				if ( opcode == (ushort)Opcode.EVENT )
					Increment( inventory.EventOperands, string.Join( " ", instruction.Operands.Select( x => x.Kind == RideScriptOperandKind.Variable ? script.VariableNames[x.Value] : $"0x{x.Value:X4}" ) ) );
			}
		}
		foreach ( var (opcode, counts) in inventory.ObservedOperandCounts )
		{
			var hasDocumented = IsKnownOpcode( opcode ) && DocumentedOperandCounts.ContainsKey( (Opcode)opcode );
			var documented = hasDocumented ? DocumentedOperandCounts[(Opcode)opcode].ToString() : "nothing";
			if ( counts.Count != 1 || (hasDocumented && counts.Min != DocumentedOperandCounts[(Opcode)opcode]) )
				inventory.OperandCountMismatches.Add( $"{GetOpcodeName( opcode )} documented {documented}, observed {string.Join( "/", counts )}" );
		}
		return inventory;
	}

	private static void Increment<T>( IDictionary<T, int> dictionary, T key ) where T : notnull
		=> dictionary[key] = dictionary.TryGetValue( key, out var count ) ? count + 1 : 1;
}

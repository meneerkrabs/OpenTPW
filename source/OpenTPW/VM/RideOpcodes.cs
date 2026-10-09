using System.Linq.Expressions;
using System.Reflection;
using System.Text;

namespace OpenTPW;

/// <summary>Raised by handlers when a script does something the VM cannot execute; it faults the script.</summary>
public sealed class RideScriptException : Exception
{
	public RideScriptException( string message ) : base( message ) { }
}

public sealed record RideOpcodeDefinition( Opcode Opcode, RideOpcodeStatus Status, string Evidence, int OperandCount, Action<RideVM, Operand[]> Invoke );

/// <summary>
/// Opcode → handler table, built once from <see cref="OpcodeHandlerAttribute"/> methods.
/// </summary>
public static class RideOpcodes
{
	public static IReadOnlyDictionary<Opcode, RideOpcodeDefinition> Definitions { get; } = Build();

	public static RideOpcodeStatus GetStatus( Opcode opcode ) => Definitions.TryGetValue( opcode, out var definition ) ? definition.Status : RideOpcodeStatus.Unknown;

	private static Dictionary<Opcode, RideOpcodeDefinition> Build()
	{
		var definitions = new Dictionary<Opcode, RideOpcodeDefinition>();
		var vmParameter = Expression.Parameter( typeof( RideVM ), "vm" );
		var operandsParameter = Expression.Parameter( typeof( Operand[] ), "operands" );
		foreach ( var method in typeof( RideOpcodes ).Assembly.GetTypes().SelectMany( type => type.GetMethods( BindingFlags.Public | BindingFlags.Static ) ) )
		{
			var attribute = method.GetCustomAttribute<OpcodeHandlerAttribute>();
			if ( attribute == null )
				continue;
			var parameters = method.GetParameters();
			if ( parameters.Length == 0 || parameters[0].ParameterType != typeof( RideVM ) || parameters.Skip( 1 ).Any( x => x.ParameterType != typeof( Operand ) ) )
				throw new InvalidOperationException( $"Opcode handler {method.Name} must take (RideVM, Operand...)." );
			var arguments = new List<Expression> { vmParameter };
			for ( var index = 1; index < parameters.Length; ++index )
				arguments.Add( Expression.ArrayIndex( operandsParameter, Expression.Constant( index - 1 ) ) );
			var invoke = Expression.Lambda<Action<RideVM, Operand[]>>( Expression.Call( method, arguments ), vmParameter, operandsParameter ).Compile();
			definitions.Add( attribute.Opcode, new RideOpcodeDefinition( attribute.Opcode, attribute.Status, attribute.Evidence, parameters.Length - 1, invoke ) );
		}
		return definitions;
	}

	/// <summary>
	/// Markdown coverage table (one row per named opcode) with corpus use counts from <paramref name="histogram"/>.
	/// docs/RSE-VM.md embeds this exact text; a test keeps them in sync.
	/// </summary>
	public static string FormatCoverageTable( IReadOnlyDictionary<ushort, int> histogram )
	{
		var builder = new StringBuilder();
		builder.Append( "| # | Opcode | Corpus uses | Status | Evidence |\n" );
		builder.Append( "| ---: | --- | ---: | --- | --- |\n" );
		foreach ( var opcode in Enum.GetValues<Opcode>() )
		{
			histogram.TryGetValue( (ushort)opcode, out var uses );
			var status = GetStatus( opcode );
			var evidence = Definitions.TryGetValue( opcode, out var definition ) ? definition.Evidence : "no corpus use; not implemented (executing it faults the script)";
			builder.Append( $"| {(int)opcode} | `{opcode}` | {uses} | {status.ToString().ToLowerInvariant()} | {evidence} |\n" );
		}
		return builder.ToString();
	}
}

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
	private static readonly object gate = new();
	private static Dictionary<Opcode, RideOpcodeDefinition>? definitions;
	private static TableBuilder? building;

	/// <summary>The handler of every implemented opcode; built on first use, or ahead of it in steps (<see cref="PrepareStep"/>).</summary>
	public static IReadOnlyDictionary<Opcode, RideOpcodeDefinition> Definitions
	{
		get
		{
			if ( Volatile.Read( ref definitions ) is { } built )
				return built;
			lock ( gate )
			{
				while ( definitions == null )
					StepLocked();
				return definitions;
			}
		}
	}

	public static RideOpcodeStatus GetStatus( Opcode opcode ) => Definitions.TryGetValue( opcode, out var definition ) ? definition.Status : RideOpcodeStatus.Unknown;

	/// <summary>
	/// Builds part of <see cref="Definitions"/> ahead of its first use, for a park built over frames (docs/BROWSER-PERFORMANCE.md, "Build steps"):
	/// compiling every handler at once is a long frame in the browser. True while work remains. The table is the same (<see cref="TableBuilder"/>),
	/// and a use of <see cref="Definitions"/> meanwhile finishes it at once.
	/// </summary>
	internal static bool PrepareStep()
	{
		if ( Volatile.Read( ref definitions ) != null )
			return false;
		lock ( gate )
		{
			if ( definitions == null )
				StepLocked();
			return definitions == null;
		}
	}

	private static void StepLocked()
	{
		building ??= new TableBuilder();
		if ( building.Step() )
			return;
		Volatile.Write( ref definitions, building.Result );
		building = null;
	}

	/// <summary>
	/// Builds the opcode table from the <see cref="OpcodeHandlerAttribute"/> methods, a part per <see cref="Step"/>: the handlers are found in
	/// the assembly's order and compiled as they are found, so the table, and its order, is that of one build at once.
	/// </summary>
	internal sealed class TableBuilder
	{
		/// <summary>The methods a step looks at, and the handlers it compiles, at most.</summary>
		private const int MethodsPerStep = 256, HandlersPerStep = 8;

		private readonly Dictionary<Opcode, RideOpcodeDefinition> table = new();
		private readonly ParameterExpression vm = Expression.Parameter( typeof( RideVM ), "vm" );
		private readonly ParameterExpression operands = Expression.Parameter( typeof( Operand[] ), "operands" );
		private IEnumerator<MethodInfo>? methods;

		/// <summary>The table once <see cref="Step"/> has returned false.</summary>
		public Dictionary<Opcode, RideOpcodeDefinition>? Result { get; private set; }

		/// <summary>Builds a part of the table; false once it is complete (<see cref="Result"/>).</summary>
		public bool Step()
		{
			if ( Result != null )
				return false;
			if ( methods == null )
			{
				methods = typeof( RideOpcodes ).Assembly.GetTypes().SelectMany( type => type.GetMethods( BindingFlags.Public | BindingFlags.Static ) ).GetEnumerator();
				return true;
			}
			var handlers = 0;
			for ( var examined = 0; examined < MethodsPerStep && handlers < HandlersPerStep; examined++ )
			{
				if ( !methods.MoveNext() )
				{
					methods.Dispose();
					Result = table;
					return false;
				}
				if ( Add( methods.Current ) )
					handlers++;
			}
			return true;
		}

		private bool Add( MethodInfo method )
		{
			var attribute = method.GetCustomAttribute<OpcodeHandlerAttribute>();
			if ( attribute == null )
				return false;
			var parameters = method.GetParameters();
			if ( parameters.Length == 0 || parameters[0].ParameterType != typeof( RideVM ) || parameters.Skip( 1 ).Any( x => x.ParameterType != typeof( Operand ) ) )
				throw new InvalidOperationException( $"Opcode handler {method.Name} must take (RideVM, Operand...)." );
			var arguments = new List<Expression> { vm };
			for ( var index = 1; index < parameters.Length; ++index )
				arguments.Add( Expression.ArrayIndex( operands, Expression.Constant( index - 1 ) ) );
			var invoke = Expression.Lambda<Action<RideVM, Operand[]>>( Expression.Call( method, arguments ), vm, operands ).Compile();
			table.Add( attribute.Opcode, new RideOpcodeDefinition( attribute.Opcode, attribute.Status, attribute.Evidence, parameters.Length - 1, invoke ) );
			return true;
		}
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

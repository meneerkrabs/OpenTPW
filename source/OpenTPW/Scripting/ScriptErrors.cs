namespace OpenTPW.Scripting;

/// <summary>A script that cannot be accepted: a limit exceeded, a syntax error or a forbidden construct. Carries the position.</summary>
public sealed class ScriptCompileException : Exception
{
	public int Line { get; }
	public int Column { get; }

	public ScriptCompileException( string message, int line, int column )
		: base( $"{line}:{column}: {message}" )
	{
		Line = line;
		Column = column;
		Detail = message;
	}

	/// <summary>The message without the position.</summary>
	public string Detail { get; }
}

/// <summary>A runtime failure of one call (internal: callers see <see cref="ScriptOutcome.RuntimeError"/>).</summary>
internal sealed class ScriptRuntimeException : Exception
{
	public int Line { get; }
	public ScriptRuntimeException( string message, int line = 0 ) : base( message ) { Line = line; }
}

/// <summary>The call ran out of steps (internal: callers see <see cref="ScriptOutcome.BudgetExhausted"/>). The language cannot catch it.</summary>
internal sealed class ScriptBudgetException : Exception
{
	public int Line { get; }
	public ScriptBudgetException( int line ) : base( "step budget exhausted" ) { Line = line; }
}

/// <summary>A state that breaks the schema (see <see cref="ScriptState"/>).</summary>
public sealed class ScriptStateException : Exception
{
	public ScriptStateException( string message ) : base( message ) { }
}

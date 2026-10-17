using System.Text;
using OpenTPW.Scripting;

namespace OpenTPW.Tests;

/// <summary>Helpers for the TPW Script tests (docs/SCRIPT-MODS.md).</summary>
internal static class ScriptTestSupport
{
	public const long Previous = 777_000;
	public static readonly ScriptCallIdentity Identity = new( 12345, 42, 100 );
	public static readonly ScriptResultSpec AnyInt = ScriptResultSpec.Int( long.MinValue, long.MaxValue );

	public static ScriptProgram Compile( string source ) => ScriptProgram.Compile( Encoding.UTF8.GetBytes( source ) );

	public static ScriptHost Host( string source, ScriptHostOptions? options = null, ScriptTable? state = null, string modId = "community.test" ) =>
		new( Compile( source ), modId, options, state );

	public static ScriptCallResult Call( ScriptHost host, string hook, params ScriptValue[] arguments ) =>
		host.Call( hook, arguments, ScriptValue.FromInt( Previous ), AnyInt, Identity );

	/// <summary>Runs <c>function main(...)</c> of a script with a fresh host.</summary>
	public static ScriptCallResult Run( string source, params ScriptValue[] arguments ) => Call( Host( source ), "main", arguments );

	/// <summary>The integer a one-line body returns: <c>Eval("return 1 + 2")</c>.</summary>
	public static long Eval( string body )
	{
		var result = Run( $"function main()\n{body}\nend" );
		if ( !result.Succeeded )
			throw new Microsoft.VisualStudio.TestTools.UnitTesting.AssertFailedException( $"{body}: {result.Outcome} {result.Message} (line {result.Line})" );
		return result.Value.AsInt;
	}

	public static ScriptOutcome Outcome( string body ) => Run( $"function main()\n{body}\nend" ).Outcome;
}

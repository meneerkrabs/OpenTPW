using System.Security.Cryptography;
using System.Text;

namespace OpenTPW.Scripting;

internal enum Op : byte
{
	Const, Local, SetLocal, State, Index, SetIndex, Pop, CheckValue, CheckBool,
	Not, Neg, Add, Sub, Mul, IDiv, Mod, Eq, Ne, Lt, Le, Gt, Ge, Concat,
	Jmp, JmpFalse, JmpFalseKeep, JmpTrueKeep,
	Call, CallHost, Ret, RetUnit, NewTable, TableAppend, TableField, ForPrep, ForTest, ForNext
}

internal readonly record struct Instr( Op Op, int A, int B );

internal sealed class ScriptFunction
{
	public required string Name { get; init; }
	public required int ParameterCount { get; init; }
	public required int LocalCount { get; init; }
	public required Instr[] Code { get; init; }
	public required int[] Lines { get; init; }
}

// [EXT:script-mods] OpenTPW's own scripted mods (TPW Script); the original game had no mods
/// <summary>
/// A compiled script: bytecode plus the SHA-256 of the exact source bytes it was compiled from. The compiler decodes and parses the
/// very bytes it hashes, so "the hashed bytes are the executed bytes" holds by construction; callers keep the program, never re-read a path.
/// </summary>
public sealed class ScriptProgram
{
	internal ScriptFunction[] Functions { get; }
	internal ScriptValue[] Constants { get; }
	private readonly Dictionary<string, int> functionIndex;

	/// <summary>Lowercase hex SHA-256 of the source bytes.</summary>
	public string Sha256 { get; }

	internal ScriptProgram( string sha256, ScriptFunction[] functions, ScriptValue[] constants )
	{
		Sha256 = sha256;
		Functions = functions;
		Constants = constants;
		functionIndex = new Dictionary<string, int>( StringComparer.Ordinal );
		for ( int i = 0; i < functions.Length; i++ )
			functionIndex[functions[i].Name] = i;
	}

	public IEnumerable<string> FunctionNames => Functions.Select( f => f.Name );
	public bool HasFunction( string name ) => functionIndex.ContainsKey( name );
	public int ParameterCount( string name ) => Functions[functionIndex[name]].ParameterCount;
	internal bool TryGetFunction( string name, out int index ) => functionIndex.TryGetValue( name, out index );

	/// <summary>Compiles UTF-8 source bytes. Throws <see cref="ScriptCompileException"/> (with line and column) for anything not allowed.</summary>
	public static ScriptProgram Compile( ReadOnlySpan<byte> source )
	{
		if ( source.Length > ScriptLimits.MaxSourceBytes )
			throw new ScriptCompileException( $"source too large ({source.Length} bytes, limit {ScriptLimits.MaxSourceBytes})", 1, 1 );
		var hash = Convert.ToHexString( SHA256.HashData( source ) ).ToLowerInvariant();
		string text;
		try { text = new UTF8Encoding( false, true ).GetString( source ); }
		catch ( ArgumentException ) { throw new ScriptCompileException( "source is not valid UTF-8", 1, 1 ); }
		return ScriptCompiler.Compile( text, hash );
	}

	public static bool TryCompile( ReadOnlySpan<byte> source, out ScriptProgram? program, out ScriptCompileException? error )
	{
		try { program = Compile( source ); error = null; return true; }
		catch ( ScriptCompileException e ) { program = null; error = e; return false; }
	}
}

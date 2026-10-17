namespace OpenTPW.Scripting;

internal enum Builtin : byte { None, Len, Has, Keys, Push, Pop, Del, Rng }

/// <summary>One whitelisted host function. Pure integer functions are typed delegates; the table functions are named built-ins with fixed semantics.</summary>
internal sealed class HostFunction
{
	public required string Name { get; init; }
	public required int Arity { get; init; }
	public Func<long, long>? Int1 { get; init; }
	public Func<long, long, long>? Int2 { get; init; }
	public Func<long, long, long, long>? Int3 { get; init; }
	public Builtin Builtin { get; init; }
}

/// <summary>
/// The fixed host function whitelist (docs/SCRIPT-MODS.md). Nothing is looked up by name at run time and nothing uses reflection:
/// the compiler resolves each call to an index into <see cref="All"/>.
/// </summary>
internal static class ScriptHostFunctions
{
	public static readonly HostFunction[] All =
	{
		new() { Name = "min", Arity = 2, Int2 = static ( a, b ) => a < b ? a : b },
		new() { Name = "max", Arity = 2, Int2 = static ( a, b ) => a > b ? a : b },
		new() { Name = "abs", Arity = 1, Int1 = static a => a < 0 ? checked( -a ) : a },
		new() { Name = "clamp", Arity = 3, Int3 = static ( v, lo, hi ) => lo > hi ? throw new ScriptRuntimeException( "clamp: lo is greater than hi" ) : v < lo ? lo : v > hi ? hi : v },
		new() { Name = "fxmul", Arity = 2, Int2 = static ( a, b ) => ToLong( (Int128)a * b / 100 ) },
		new() { Name = "fxdiv", Arity = 2, Int2 = static ( a, b ) => b == 0 ? throw new ScriptRuntimeException( "division by zero" ) : ToLong( (Int128)a * 100 / b ) },
		new() { Name = "len", Arity = 1, Builtin = Builtin.Len },
		new() { Name = "has", Arity = 2, Builtin = Builtin.Has },
		new() { Name = "keys", Arity = 1, Builtin = Builtin.Keys },
		new() { Name = "push", Arity = 2, Builtin = Builtin.Push },
		new() { Name = "pop", Arity = 1, Builtin = Builtin.Pop },
		new() { Name = "del", Arity = 2, Builtin = Builtin.Del },
		new() { Name = "rng", Arity = 1, Builtin = Builtin.Rng },
	};

	private static long ToLong( Int128 value ) =>
		value < long.MinValue || value > long.MaxValue ? throw new OverflowException() : (long)value;

	public static bool TryFind( string name, out int index )
	{
		for ( index = 0; index < All.Length; index++ )
			if ( All[index].Name == name )
				return true;
		return false;
	}
}

using System.Globalization;

namespace OpenTPW.Scripting;

/// <summary>The identity a call's <c>rng</c> draws hash (see <see cref="ScriptRng"/>); the draw ordinal is counted by the interpreter.</summary>
internal readonly record struct RngIdentity( long ParkSeed, ulong ModHash, ulong HookHash, long GuestBirthId, long ParkTurn );

/// <summary>
/// The bytecode interpreter. One step is one instruction (some operations charge more, in proportion to the data they touch); the step
/// counter is checked in the dispatch loop, which scripts cannot reach, so a budget cannot be caught, reset or skipped. Script calls use
/// an explicit frame array, not the C# stack. Same style as <see cref="RideVM"/>: a slice budget and no ambient state.
/// </summary>
internal sealed class ScriptInterpreter
{
	private struct Frame
	{
		public int Function;
		public int Pc;
		public int Base;
	}

	private readonly ScriptProgram program;
	private readonly ScriptValue[] stack = new ScriptValue[ScriptLimits.StackSize];
	private readonly Frame[] frames = new Frame[ScriptLimits.MaxCallDepth];
	private int highWater;

	private object token = null!;
	private ScriptTable committed = null!;
	private ScriptTable? root;
	private long stepLimit;
	private long memory;
	private long drawOrdinal;
	private RngIdentity rng;

	/// <summary>Steps used by the last call (also after a failure).</summary>
	public long Steps { get; private set; }

	/// <summary>Table mutations in the last call; 0 means the state cannot have changed and need not be re-validated.</summary>
	public int Mutations { get; private set; }

	/// <summary>The call's private copy of the state, made the first time the script touches <c>state</c> (null if it never did).</summary>
	public ScriptTable? Root => root;

	/// <summary>Charges work done after the script finished (commit validation) to this call's step and memory meters.</summary>
	public void ChargeCommit( long steps, long bytes )
	{
		ChargeSteps( steps );
		Alloc( bytes );
	}

	private ScriptTable StateRoot()
	{
		if ( root == null )
		{
			Alloc( 64 + 24L * committed.Count );
			ChargeSteps( committed.Count );
			root = committed.CloneShallow( token );
		}
		return root;
	}

	public ScriptInterpreter( ScriptProgram program ) { this.program = program; }

	public void Reset()
	{
		Array.Clear( stack, 0, Math.Min( highWater + 1, stack.Length ) );
		highWater = 0;
		committed = null!;
		root = null;
		token = null!;
	}

	public ScriptValue Execute( int function, ReadOnlySpan<ScriptValue> arguments, ScriptTable committedState, object callToken, long budget, RngIdentity identity )
	{
		committed = committedState;
		root = null;
		token = callToken;
		stepLimit = budget;
		Steps = 0;
		Mutations = 0;
		memory = 0;
		drawOrdinal = 0;
		rng = identity;

		var consts = program.Constants;
		var fn = program.Functions[function];
		var code = fn.Code;
		int pc = 0, bp = 0, depth = 0, sp, fnIndex = function;
		for ( int i = 0; i < arguments.Length; i++ )
			stack[i] = arguments[i];
		sp = fn.LocalCount;
		highWater = Math.Max( highWater, sp );

		try
		{
			while ( true )
			{
				if ( ++Steps > stepLimit )
					throw new ScriptBudgetException( fn.Lines[pc] );
				var ins = code[pc++];
				switch ( ins.Op )
				{
					case Op.Const: Push( ref sp, consts[ins.A] ); break;
					case Op.Local: Push( ref sp, stack[bp + ins.A] ); break;
					case Op.SetLocal: stack[bp + ins.A] = stack[--sp]; break;
					case Op.State: Push( ref sp, ScriptValue.FromTable( StateRoot() ) ); break;
					case Op.Pop: sp--; break;
					case Op.CheckValue:
						if ( stack[sp - 1].Kind == ScriptKind.Unit )
							throw Fail( "the function returned no value" );
						break;
					case Op.CheckBool: RequireBool( stack[sp - 1], "and/or operand" ); break;

					case Op.Index:
						sp -= 1;
						stack[sp - 1] = GetIndex( stack[sp - 1], stack[sp] );
						break;
					case Op.SetIndex:
						sp -= 3;
						SetIndex( stack[sp], stack[sp + 1], stack[sp + 2] );
						break;

					case Op.Not: stack[sp - 1] = !RequireBool( stack[sp - 1], "not" ); break;
					case Op.Neg: stack[sp - 1] = checked( -Int( stack[sp - 1] ) ); break;
					case Op.Add: sp--; stack[sp - 1] = checked( Int( stack[sp - 1] ) + Int( stack[sp] ) ); break;
					case Op.Sub: sp--; stack[sp - 1] = checked( Int( stack[sp - 1] ) - Int( stack[sp] ) ); break;
					case Op.Mul: sp--; stack[sp - 1] = checked( Int( stack[sp - 1] ) * Int( stack[sp] ) ); break;
					case Op.IDiv:
					{
						sp--;
						long a = Int( stack[sp - 1] ), b = Int( stack[sp] );
						if ( b == 0 )
							throw Fail( "division by zero" );
						if ( b == -1 )
							stack[sp - 1] = checked( -a );
						else
							stack[sp - 1] = a / b;
						break;
					}
					case Op.Mod:
					{
						sp--;
						long a = Int( stack[sp - 1] ), b = Int( stack[sp] );
						if ( b == 0 )
							throw Fail( "division by zero (%)" );
						stack[sp - 1] = b == -1 ? 0 : a % b;
						break;
					}

					case Op.Eq: sp--; stack[sp - 1] = Equal( stack[sp - 1], stack[sp] ); break;
					case Op.Ne: sp--; stack[sp - 1] = !Equal( stack[sp - 1], stack[sp] ); break;
					case Op.Lt: sp--; stack[sp - 1] = Compare( stack[sp - 1], stack[sp] ) < 0; break;
					case Op.Le: sp--; stack[sp - 1] = Compare( stack[sp - 1], stack[sp] ) <= 0; break;
					case Op.Gt: sp--; stack[sp - 1] = Compare( stack[sp - 1], stack[sp] ) > 0; break;
					case Op.Ge: sp--; stack[sp - 1] = Compare( stack[sp - 1], stack[sp] ) >= 0; break;
					case Op.Concat: sp--; stack[sp - 1] = Concat( stack[sp - 1], stack[sp] ); break;

					case Op.Jmp: pc = ins.A; break;
					case Op.JmpFalse:
						if ( !RequireBool( stack[--sp], "condition" ) )
							pc = ins.A;
						break;
					case Op.JmpFalseKeep:
						if ( !RequireBool( stack[sp - 1], "and operand" ) )
							pc = ins.A;
						else
							sp--;
						break;
					case Op.JmpTrueKeep:
						if ( RequireBool( stack[sp - 1], "or operand" ) )
							pc = ins.A;
						else
							sp--;
						break;

					case Op.Call:
					{
						if ( depth + 1 >= ScriptLimits.MaxCallDepth )
							throw Fail( $"call depth limit ({ScriptLimits.MaxCallDepth}) exceeded" );
						var callee = program.Functions[ins.A];
						var newBase = sp - ins.B;
						if ( newBase + callee.LocalCount + 32 > stack.Length )
							throw Fail( "value stack limit exceeded" );
						frames[depth++] = new Frame { Function = fnIndex, Pc = pc, Base = bp };
						fnIndex = ins.A;
						fn = callee;
						code = fn.Code;
						pc = 0;
						bp = newBase;
						sp = bp + fn.LocalCount;
						highWater = Math.Max( highWater, sp );
						break;
					}
					case Op.CallHost:
					{
						sp -= ins.B;
						var result = CallHost( ScriptHostFunctions.All[ins.A], sp );
						Push( ref sp, result );
						break;
					}
					case Op.Ret:
					case Op.RetUnit:
					{
						var value = ins.Op == Op.Ret ? stack[sp - 1] : ScriptValue.Unit;
						if ( depth == 0 )
							return value;
						sp = bp;
						var frame = frames[--depth];
						fnIndex = frame.Function;
						fn = program.Functions[fnIndex];
						code = fn.Code;
						pc = frame.Pc;
						bp = frame.Base;
						Push( ref sp, value );
						break;
					}

					case Op.NewTable:
						Push( ref sp, ScriptValue.FromTable( NewTable( 0 ) ) );
						break;
					case Op.TableAppend:
					{
						sp--;
						var table = stack[sp - 1].RawTable;
						RequireRoom( table.Count );
						Alloc( 24 );
						if ( table.Kind == ScriptTableKind.Empty )
						{
							table.Kind = ScriptTableKind.Array;
							table.Items = new List<ScriptValue>();
						}
						table.Items!.Add( Storable( stack[sp] ) );
						break;
					}
					case Op.TableField:
					{
						sp -= 2;
						var table = stack[sp - 1].RawTable;
						RequireRoom( table.Count );
						Alloc( 24 );
						if ( table.Kind == ScriptTableKind.Empty )
						{
							table.Kind = ScriptTableKind.Map;
							table.Entries = new Dictionary<string, ScriptValue>( StringComparer.Ordinal );
						}
						table.Entries![stack[sp].RawString] = Storable( stack[sp + 1] );
						break;
					}

					case Op.ForPrep:
					{
						sp -= 3;
						var start = Int( stack[sp] );
						var limit = Int( stack[sp + 1] );
						var step = Int( stack[sp + 2] );
						if ( step == 0 )
							throw Fail( "for loop step is zero" );
						stack[bp + ins.A] = start;
						stack[bp + ins.A + 1] = limit;
						stack[bp + ins.A + 2] = step;
						break;
					}
					case Op.ForTest:
					{
						long i = stack[bp + ins.A].RawInt, limit = stack[bp + ins.A + 1].RawInt, step = stack[bp + ins.A + 2].RawInt;
						if ( step > 0 ? i > limit : i < limit )
							pc = ins.B;
						else
							stack[bp + ins.A + 3] = i;
						break;
					}
					case Op.ForNext:
					{
						long i = stack[bp + ins.A].RawInt, step = stack[bp + ins.A + 2].RawInt;
						if ( step > 0 ? i > long.MaxValue - step : i < long.MinValue - step )
							break; // the next value is past the long range: the loop is over
						stack[bp + ins.A] = i + step;
						pc = ins.B;
						break;
					}

					default:
						throw Fail( "invalid instruction" );
				}
			}
		}
		catch ( ScriptRuntimeException e ) when ( e.Line == 0 )
		{
			throw new ScriptRuntimeException( e.Message, fn.Lines[Math.Max( pc - 1, 0 )] );
		}
		catch ( OverflowException )
		{
			throw new ScriptRuntimeException( "integer overflow", fn.Lines[Math.Max( pc - 1, 0 )] );
		}
	}

	private static ScriptRuntimeException Fail( string message ) => new( message );

	private void Push( ref int sp, ScriptValue value )
	{
		if ( sp >= stack.Length )
			throw Fail( "value stack limit exceeded" );
		stack[sp++] = value;
		if ( sp > highWater )
			highWater = sp;
	}

	private static long Int( in ScriptValue value ) =>
		value.Kind == ScriptKind.Int ? value.RawInt : throw Fail( $"expected an int, found {ScriptValue.KindName( value.Kind )}" );

	private static bool RequireBool( in ScriptValue value, string what ) =>
		value.Kind == ScriptKind.Bool ? value.RawInt != 0 : throw Fail( $"{what} must be a bool, found {ScriptValue.KindName( value.Kind )} (there is no truthiness)" );

	private void ChargeSteps( long amount )
	{
		Steps += amount;
		if ( Steps > stepLimit )
		{
			Steps = stepLimit + 1; // reported steps never run past the budget, whatever the last charge was
			throw new ScriptBudgetException( 0 );
		}
	}

	private void Alloc( long bytes )
	{
		memory += bytes;
		if ( memory > ScriptLimits.MaxCallMemory )
			throw Fail( "memory limit exceeded for this call" );
	}

	private ScriptTable NewTable( int entries )
	{
		if ( entries > ScriptLimits.MaxTableEntries )
			throw Fail( $"a table holds at most {ScriptLimits.MaxTableEntries} entries" );
		Alloc( 64 + 24L * entries );
		ChargeSteps( entries );
		return new ScriptTable( token );
	}

	private static bool Equal( in ScriptValue a, in ScriptValue b )
	{
		if ( a.Kind != b.Kind || a.Kind is ScriptKind.Table or ScriptKind.Unit )
			throw Fail( $"cannot compare {ScriptValue.KindName( a.Kind )} with {ScriptValue.KindName( b.Kind )} for equality" );
		return a.Kind == ScriptKind.String ? string.Equals( a.RawString, b.RawString, StringComparison.Ordinal ) : a.RawInt == b.RawInt;
	}

	private static int Compare( in ScriptValue a, in ScriptValue b )
	{
		if ( a.Kind == ScriptKind.Int && b.Kind == ScriptKind.Int )
			return a.RawInt.CompareTo( b.RawInt );
		if ( a.Kind == ScriptKind.String && b.Kind == ScriptKind.String )
			return string.CompareOrdinal( a.RawString, b.RawString );
		throw Fail( $"cannot order {ScriptValue.KindName( a.Kind )} and {ScriptValue.KindName( b.Kind )}" );
	}

	private ScriptValue Concat( in ScriptValue a, in ScriptValue b )
	{
		var left = Text( a );
		var right = Text( b );
		var length = (long)left.Length + right.Length;
		if ( length > ScriptLimits.MaxStringLength )
			throw Fail( $"string longer than {ScriptLimits.MaxStringLength} characters" );
		Alloc( 24 + 2 * length );
		ChargeSteps( length / 16 );
		return ScriptValue.FromString( string.Concat( left, right ) );
	}

	private static string Text( in ScriptValue value ) => value.Kind switch
	{
		ScriptKind.String => value.RawString,
		ScriptKind.Int => value.RawInt.ToString( CultureInfo.InvariantCulture ),
		_ => throw Fail( $"cannot concatenate {ScriptValue.KindName( value.Kind )}" )
	};

	// ----- tables -----

	/// <summary>A value about to be stored in a table: host views cannot be stored (state holds plain data only).</summary>
	private static ScriptValue Storable( ScriptValue value )
	{
		if ( value.Kind == ScriptKind.Table && value.RawTable.ReadOnly )
			throw Fail( "a host view cannot be stored in a table; copy the fields you need" );
		return value;
	}

	/// <summary>A table read out of a writable table: tables shared with the committed state are copied first, so the committed state is never changed.</summary>
	private ScriptValue Adopt( ScriptValue value, out bool cloned )
	{
		cloned = false;
		if ( value.Kind != ScriptKind.Table )
			return value;
		var table = value.RawTable;
		if ( table.ReadOnly || ReferenceEquals( table.Owner, token ) )
			return value;
		Alloc( 64 + 24L * table.Count );
		ChargeSteps( table.Count );
		cloned = true;
		return ScriptValue.FromTable( table.CloneShallow( token ) );
	}

	private ScriptValue GetIndex( in ScriptValue target, in ScriptValue key )
	{
		if ( target.Kind != ScriptKind.Table )
			throw Fail( $"cannot index {ScriptValue.KindName( target.Kind )}" );
		var table = target.RawTable;
		switch ( table.Kind )
		{
			case ScriptTableKind.Array:
			{
				var index = ArrayIndex( key, table.Items!.Count );
				var value = table.Items[index];
				if ( !table.ReadOnly )
				{
					value = Adopt( value, out var cloned );
					if ( cloned )
						table.Items[index] = value;
				}
				return value;
			}
			case ScriptTableKind.Map:
			{
				var name = MapKey( key );
				if ( !table.Entries!.TryGetValue( name, out var value ) )
					throw Fail( $"key '{name}' not found (use has(t, k) to test)" );
				if ( !table.ReadOnly )
				{
					value = Adopt( value, out var cloned );
					if ( cloned )
						table.Entries[name] = value;
				}
				return value;
			}
			default:
				throw Fail( "index into an empty table (use has(t, k) to test)" );
		}
	}

	private static int ArrayIndex( in ScriptValue key, int count )
	{
		if ( key.Kind != ScriptKind.Int )
			throw Fail( $"an array index must be an int, found {ScriptValue.KindName( key.Kind )}" );
		if ( key.RawInt < 1 || key.RawInt > count )
			throw Fail( $"index {key.RawInt} is out of range 1..{count}" );
		return (int)key.RawInt - 1;
	}

	/// <summary>Map keys are 1 to 64 printable ASCII characters. The length test comes first, so a key check costs at most 64 characters.</summary>
	private static string MapKey( in ScriptValue key )
	{
		if ( key.Kind != ScriptKind.String )
			throw Fail( $"a map key must be a string, found {ScriptValue.KindName( key.Kind )}" );
		var name = key.RawString;
		if ( !ScriptState.IsValidKey( name ) )
			throw Fail( $"a key must be 1 to {ScriptLimits.MaxStateKeyLength} printable ASCII characters" );
		return name;
	}

	private void SetIndex( in ScriptValue target, in ScriptValue key, ScriptValue value )
	{
		if ( target.Kind != ScriptKind.Table )
			throw Fail( $"cannot index {ScriptValue.KindName( target.Kind )}" );
		var table = Writable( target.RawTable );
		value = Storable( value );
		Mutations++;

		if ( table.Kind == ScriptTableKind.Empty )
		{
			if ( key.Kind == ScriptKind.Int )
			{
				if ( key.RawInt != 1 )
					throw Fail( $"index {key.RawInt} is out of range 1..0 (an array grows by one at the end)" );
				table.Kind = ScriptTableKind.Array;
				table.Items = new List<ScriptValue>();
			}
			else
			{
				MapKey( key );
				table.Kind = ScriptTableKind.Map;
				table.Entries = new Dictionary<string, ScriptValue>( StringComparer.Ordinal );
			}
		}

		if ( table.Kind == ScriptTableKind.Array )
		{
			if ( key.Kind != ScriptKind.Int )
				throw Fail( "an array cannot take a string key (a table is an array or a map, never both)" );
			var items = table.Items!;
			if ( key.RawInt == items.Count + 1 )
			{
				RequireRoom( items.Count );
				Alloc( 24 );
				items.Add( value );
			}
			else
			{
				items[ArrayIndex( key, items.Count )] = value;
			}
		}
		else
		{
			if ( key.Kind != ScriptKind.String )
				throw Fail( "a map cannot take an int key (a table is an array or a map, never both)" );
			var name = MapKey( key );
			var entries = table.Entries!;
			if ( !entries.ContainsKey( name ) )
			{
				RequireRoom( entries.Count );
				Alloc( 24 + 2L * name.Length );
			}
			entries[name] = value;
		}
	}

	private ScriptTable Writable( ScriptTable table )
	{
		if ( table.ReadOnly )
			throw Fail( "a host view is read-only" );
		if ( !ReferenceEquals( table.Owner, token ) )
			throw Fail( "internal error: a shared table was reached for writing" );
		return table;
	}

	private static void RequireRoom( int count )
	{
		if ( count >= ScriptLimits.MaxTableEntries )
			throw Fail( $"a table holds at most {ScriptLimits.MaxTableEntries} entries" );
	}

	// ----- host functions -----

	private ScriptValue CallHost( HostFunction function, int argBase )
	{
		if ( function.Int1 != null )
			return function.Int1( Int( stack[argBase] ) );
		if ( function.Int2 != null )
			return function.Int2( Int( stack[argBase] ), Int( stack[argBase + 1] ) );
		if ( function.Int3 != null )
			return function.Int3( Int( stack[argBase] ), Int( stack[argBase + 1] ), Int( stack[argBase + 2] ) );

		var first = stack[argBase];
		switch ( function.Builtin )
		{
			case Builtin.Len:
				return first.Kind switch
				{
					ScriptKind.String => first.RawString.Length,
					ScriptKind.Table => first.RawTable.Count,
					_ => throw Fail( $"len needs a string or a table, found {ScriptValue.KindName( first.Kind )}" )
				};
			case Builtin.Has:
			{
				var table = RequireTable( first, "has" );
				var key = stack[argBase + 1];
				switch ( table.Kind )
				{
					case ScriptTableKind.Array:
						if ( key.Kind != ScriptKind.Int )
							throw Fail( $"an array index must be an int, found {ScriptValue.KindName( key.Kind )}" );
						return key.RawInt >= 1 && key.RawInt <= table.Items!.Count;
					case ScriptTableKind.Map:
						return table.Entries!.ContainsKey( MapKey( key ) );
					default:
						return false;
				}
			}
			case Builtin.Keys:
			{
				var table = RequireTable( first, "keys" );
				if ( table.Kind == ScriptTableKind.Array )
					throw Fail( "keys needs a map (iterate an array with a numeric for and len)" );
				var result = NewTable( table.Count );
				if ( table.Kind == ScriptTableKind.Map )
				{
					result.Kind = ScriptTableKind.Array;
					result.Items = new List<ScriptValue>( table.Count );
					foreach ( var name in table.SortedKeys() )
					{
						Alloc( 24 + 2L * name.Length );
						result.Items.Add( ScriptValue.FromString( name ) );
					}
				}
				return ScriptValue.FromTable( result );
			}
			case Builtin.Push:
			{
				var table = Writable( RequireTable( first, "push" ) );
				var value = Storable( stack[argBase + 1] );
				if ( table.Kind == ScriptTableKind.Empty )
				{
					table.Kind = ScriptTableKind.Array;
					table.Items = new List<ScriptValue>();
				}
				if ( table.Kind != ScriptTableKind.Array )
					throw Fail( "push needs an array" );
				RequireRoom( table.Items!.Count );
				Alloc( 24 );
				Mutations++;
				table.Items.Add( value );
				return ScriptValue.Unit;
			}
			case Builtin.Pop:
			{
				var table = Writable( RequireTable( first, "pop" ) );
				if ( table.Kind != ScriptTableKind.Array || table.Items!.Count == 0 )
					throw Fail( "pop needs a non-empty array" );
				Mutations++;
				var value = table.Items[^1];
				table.Items.RemoveAt( table.Items.Count - 1 );
				return Adopt( value, out _ );
			}
			case Builtin.Del:
			{
				var table = Writable( RequireTable( first, "del" ) );
				if ( table.Kind != ScriptTableKind.Map )
					throw Fail( "del needs a map (an array shrinks with pop)" );
				var name = MapKey( stack[argBase + 1] );
				Mutations++;
				if ( !table.Entries!.Remove( name ) )
					throw Fail( $"key '{name}' not found" );
				if ( table.Entries.Count == 0 )
				{
					table.Kind = ScriptTableKind.Empty;
					table.Entries = null;
				}
				return ScriptValue.Unit;
			}
			case Builtin.Rng:
			{
				var n = Int( first );
				if ( n < 1 )
					throw Fail( "rng(n) needs n >= 1" );
				return ScriptRng.DrawHashed( rng, drawOrdinal++, n );
			}
			default:
				throw Fail( "invalid host function" );
		}
	}

	private static ScriptTable RequireTable( in ScriptValue value, string function ) =>
		value.Kind == ScriptKind.Table ? value.RawTable : throw Fail( $"{function} needs a table, found {ScriptValue.KindName( value.Kind )}" );
}

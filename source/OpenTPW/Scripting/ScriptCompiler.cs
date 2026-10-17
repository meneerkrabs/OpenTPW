namespace OpenTPW.Scripting;

/// <summary>
/// Single-pass recursive-descent parser that emits bytecode directly. It enforces the static limits and every language restriction at
/// parse time: the top level holds only functions and constant locals, functions are not values and cannot nest (so nothing can be
/// captured), names resolve to the function's own locals, the constants or the <c>state</c> table, and calls go to a function of this
/// script or to the host whitelist.
/// </summary>
internal sealed class ScriptCompiler
{
	private readonly List<Token> tokens;
	private int pos;
	private int lastLine = 1;
	private int depth;

	private readonly List<ScriptValue> constants = new();
	private readonly Dictionary<long, int> intConstants = new();
	private readonly Dictionary<string, int> stringConstants = new( StringComparer.Ordinal );
	private readonly int[] boolConstants = { -1, -1 };

	private readonly Dictionary<string, ScriptValue> topConstants = new( StringComparer.Ordinal );
	private readonly Dictionary<string, int> functionIndex = new( StringComparer.Ordinal );
	private readonly List<ScriptFunction> functions = new();
	private readonly List<PendingCall> pendingCalls = new();

	// Per-function state.
	private List<Instr> code = new();
	private List<int> lines = new();
	private readonly List<(string Name, int Slot)> locals = new();
	private int nextSlot;
	private int maxSlot;
	private readonly List<List<int>> loops = new();

	private readonly record struct PendingCall( string Name, int Function, int Instruction, int Arguments, int Line, int Column );

	private ScriptCompiler( List<Token> tokens ) { this.tokens = tokens; }

	public static ScriptProgram Compile( string text, string sha256 )
	{
		var compiler = new ScriptCompiler( ScriptLexer.Lex( text ) );
		compiler.ParseChunk();
		compiler.ResolveCalls();
		return new ScriptProgram( sha256, compiler.functions.ToArray(), compiler.constants.ToArray() );
	}

	// ----- token helpers -----

	private Token Peek => tokens[pos];
	private Token PeekAt( int ahead ) => tokens[Math.Min( pos + ahead, tokens.Count - 1 )];

	private Token Next()
	{
		var token = tokens[pos];
		if ( token.Kind != Tok.End )
			pos++;
		lastLine = token.Line;
		return token;
	}

	private bool Accept( Tok kind )
	{
		if ( Peek.Kind != kind )
			return false;
		Next();
		return true;
	}

	private Token Expect( Tok kind, string what )
	{
		if ( Peek.Kind != kind )
			throw Error( Peek, $"expected {what}, found {Describe( Peek )}" );
		return Next();
	}

	private static ScriptCompileException Error( Token at, string message ) => new( message, at.Line, at.Column );

	private static string Describe( Token token ) => token.Kind switch
	{
		Tok.End => "end of file",
		Tok.Name => $"'{token.Text}'",
		Tok.Int or Tok.Fixed => "a number",
		Tok.String => "a string",
		Tok.Function or Tok.Local or Tok.If or Tok.Then or Tok.Elseif or Tok.Else or Tok.While or Tok.Do or Tok.For or Tok.Return or Tok.Break
			or Tok.EndKw or Tok.And or Tok.Or or Tok.Not or Tok.True or Tok.False => $"'{token.Text}'",
		_ => $"'{Symbol( token.Kind )}'"
	};

	private static string Symbol( Tok kind ) => kind switch
	{
		Tok.Plus => "+", Tok.Minus => "-", Tok.Star => "*", Tok.SlashSlash => "//", Tok.Percent => "%", Tok.EqEq => "==", Tok.TildeEq => "~=",
		Tok.Lt => "<", Tok.Le => "<=", Tok.Gt => ">", Tok.Ge => ">=", Tok.Assign => "=", Tok.LParen => "(", Tok.RParen => ")", Tok.LBrace => "{",
		Tok.RBrace => "}", Tok.LBracket => "[", Tok.RBracket => "]", Tok.Comma => ",", Tok.Dot => ".", Tok.DotDot => "..", Tok.Semicolon => ";",
		_ => kind.ToString()
	};

	private void Enter()
	{
		if ( ++depth > ScriptLimits.MaxNesting )
			throw Error( Peek, $"nesting too deep (limit {ScriptLimits.MaxNesting})" );
	}

	private void Leave() => depth--;

	// ----- emission helpers -----

	private int Emit( Op op, int a = 0, int b = 0 )
	{
		code.Add( new Instr( op, a, b ) );
		lines.Add( lastLine );
		return code.Count - 1;
	}

	private void PatchHere( int instruction ) => code[instruction] = code[instruction] with { A = code.Count };

	private void EmitConst( ScriptValue value )
	{
		int index;
		switch ( value.Kind )
		{
			case ScriptKind.Int:
				if ( !intConstants.TryGetValue( value.RawInt, out index ) )
					intConstants[value.RawInt] = index = AddConstant( value );
				break;
			case ScriptKind.String:
				if ( !stringConstants.TryGetValue( value.RawString, out index ) )
					stringConstants[value.RawString] = index = AddConstant( value );
				break;
			default:
				var slot = value.AsBool ? 1 : 0;
				if ( boolConstants[slot] < 0 )
					boolConstants[slot] = AddConstant( value );
				index = boolConstants[slot];
				break;
		}
		Emit( Op.Const, index );
	}

	private int AddConstant( ScriptValue value )
	{
		constants.Add( value );
		return constants.Count - 1;
	}

	// ----- names -----

	private static bool IsReserved( string name ) => name == "state" || name == "fx" || ScriptHostFunctions.TryFind( name, out _ );

	private void CheckNewName( Token name, string what )
	{
		if ( IsReserved( name.Text ) )
			throw Error( name, $"'{name.Text}' is a reserved name and cannot be used for {what}" );
	}

	private int FindLocal( string name )
	{
		for ( int i = locals.Count - 1; i >= 0; i-- )
			if ( locals[i].Name == name )
				return locals[i].Slot;
		return -1;
	}

	private int DeclareLocal( Token name )
	{
		if ( nextSlot >= ScriptLimits.MaxLocalsPerFunction )
			throw Error( name, $"too many local variables in one function (limit {ScriptLimits.MaxLocalsPerFunction})" );
		var slot = nextSlot++;
		maxSlot = Math.Max( maxSlot, nextSlot );
		locals.Add( (name.Text, slot) );
		return slot;
	}

	private int DeclareHidden()
	{
		if ( nextSlot >= ScriptLimits.MaxLocalsPerFunction )
			throw Error( Peek, $"too many local variables in one function (limit {ScriptLimits.MaxLocalsPerFunction})" );
		maxSlot = Math.Max( maxSlot, nextSlot + 1 );
		return nextSlot++;
	}

	// ----- top level -----

	private void ParseChunk()
	{
		while ( Peek.Kind != Tok.End )
		{
			switch ( Peek.Kind )
			{
				case Tok.Function:
					ParseFunction();
					break;
				case Tok.Local:
					ParseTopConstant();
					break;
				case Tok.Semicolon:
					Next();
					break;
				default:
					throw Error( Peek, Peek.Kind == Tok.Name
						? "only function definitions and constant locals are allowed at the top level (mutable data lives in `state`)"
						: $"unexpected {Describe( Peek )} at the top level; only function definitions and constant locals are allowed" );
			}
		}
	}

	private void CheckTopLevelName( Token name, string what )
	{
		CheckNewName( name, what );
		if ( topConstants.ContainsKey( name.Text ) || functionIndex.ContainsKey( name.Text ) )
			throw Error( name, $"'{name.Text}' is already defined" );
	}

	private void ParseTopConstant()
	{
		Next();
		var name = Expect( Tok.Name, "a constant name" );
		CheckTopLevelName( name, "a constant" );
		Expect( Tok.Assign, "'=' (a top-level local must be a constant with a literal value)" );
		topConstants[name.Text] = ParseLiteral();
		Accept( Tok.Semicolon );
	}

	private ScriptValue ParseLiteral()
	{
		var token = Peek;
		switch ( token.Kind )
		{
			case Tok.Int: Next(); return token.Number;
			case Tok.String: Next(); return ScriptValue.FromString( token.Text );
			case Tok.True: Next(); return true;
			case Tok.False: Next(); return false;
			case Tok.Minus:
				Next();
				var number = Expect( Tok.Int, "a number" );
				return -number.Number;
			case Tok.Name when token.Text == "fx":
				Next();
				return ParseFxBody( token );
			case Tok.Fixed:
				throw Error( token, "a fixed-point literal must be written fx(1.25)" );
			default:
				throw Error( token, "a top-level constant must have a literal value (a number, string, true, false or fx(...))" );
		}
	}

	/// <summary><c>fx(1.25)</c> is the integer 125 (hundredths), folded at compile time. The argument must be a literal.</summary>
	private long ParseFxBody( Token at )
	{
		Expect( Tok.LParen, "'(' after fx" );
		var negative = Accept( Tok.Minus );
		var number = Peek;
		if ( number.Kind is not (Tok.Int or Tok.Fixed) )
			throw Error( number, "fx() takes a number literal such as fx(1.25)" );
		Next();
		long value = number.Kind == Tok.Fixed ? number.Number : number.Number > long.MaxValue / 100 ? throw Error( number, "fixed-point literal too large" ) : number.Number * 100;
		Expect( Tok.RParen, "')'" );
		return negative ? -value : value;
	}

	private void ParseFunction()
	{
		Next();
		var name = Expect( Tok.Name, "a function name" );
		CheckTopLevelName( name, "a function" );
		if ( functions.Count >= ScriptLimits.MaxFunctions )
			throw Error( name, $"too many functions (limit {ScriptLimits.MaxFunctions})" );
		var index = functions.Count;
		functionIndex[name.Text] = index;
		functions.Add( null! );

		code = new List<Instr>();
		lines = new List<int>();
		locals.Clear();
		loops.Clear();
		nextSlot = 0;
		maxSlot = 0;

		Expect( Tok.LParen, "'('" );
		var parameters = 0;
		if ( Peek.Kind != Tok.RParen )
		{
			do
			{
				var parameter = Expect( Tok.Name, "a parameter name" );
				CheckNewName( parameter, "a parameter" );
				if ( FindLocal( parameter.Text ) >= 0 )
					throw Error( parameter, $"duplicate parameter '{parameter.Text}'" );
				if ( parameters >= ScriptLimits.MaxParameters )
					throw Error( parameter, $"too many parameters (limit {ScriptLimits.MaxParameters})" );
				DeclareLocal( parameter );
				parameters++;
			}
			while ( Accept( Tok.Comma ) );
		}
		Expect( Tok.RParen, "')'" );

		ParseBlock();
		Expect( Tok.EndKw, "'end'" );
		Emit( Op.RetUnit );

		functions[index] = new ScriptFunction
		{
			Name = name.Text, ParameterCount = parameters, LocalCount = maxSlot, Code = code.ToArray(), Lines = lines.ToArray()
		};
	}

	// ----- statements -----

	private static bool EndsBlock( Tok kind ) => kind is Tok.EndKw or Tok.Else or Tok.Elseif or Tok.End;

	private void ParseBlock()
	{
		Enter();
		var localMark = locals.Count;
		var slotMark = nextSlot;
		while ( !EndsBlock( Peek.Kind ) )
			ParseStatement();
		locals.RemoveRange( localMark, locals.Count - localMark );
		nextSlot = slotMark;
		Leave();
	}

	private void ParseStatement()
	{
		var token = Peek;
		switch ( token.Kind )
		{
			case Tok.Semicolon:
				Next();
				return;
			case Tok.Local:
				Next();
				var name = Expect( Tok.Name, "a variable name" );
				CheckNewName( name, "a variable" );
				Expect( Tok.Assign, "'=' (a local needs an initial value; there is no nil)" );
				ParseExpr();
				SetNewLocal( name );
				return;
			case Tok.If: ParseIf(); return;
			case Tok.While: ParseWhile(); return;
			case Tok.For: ParseFor(); return;
			case Tok.Do:
				Next();
				ParseBlock();
				Expect( Tok.EndKw, "'end'" );
				return;
			case Tok.Return: ParseReturn(); return;
			case Tok.Break:
				Next();
				if ( loops.Count == 0 )
					throw Error( token, "'break' outside a loop" );
				loops[^1].Add( Emit( Op.Jmp ) );
				return;
			case Tok.Function:
				throw Error( token, "nested function definitions are not allowed (functions are top-level only, so nothing can be captured)" );
			case Tok.Name:
				ParseNameStatement();
				return;
			default:
				throw Error( token, $"unexpected {Describe( token )}; expected a statement" );
		}
	}

	private void SetNewLocal( Token name )
	{
		var slot = DeclareLocal( name );
		Emit( Op.SetLocal, slot );
	}

	private void ParseReturn()
	{
		var token = Next();
		if ( EndsBlock( Peek.Kind ) || Peek.Kind == Tok.Semicolon )
			Emit( Op.RetUnit );
		else
		{
			ParseExpr();
			Emit( Op.Ret );
		}
		Accept( Tok.Semicolon );
		if ( !EndsBlock( Peek.Kind ) )
			throw Error( Peek, "'return' must be the last statement of its block" );
	}

	private void ParseIf()
	{
		Next();
		var endJumps = new List<int>();
		ParseExpr();
		Expect( Tok.Then, "'then'" );
		var skip = Emit( Op.JmpFalse );
		ParseBlock();
		while ( Peek.Kind == Tok.Elseif )
		{
			endJumps.Add( Emit( Op.Jmp ) );
			PatchHere( skip );
			Next();
			ParseExpr();
			Expect( Tok.Then, "'then'" );
			skip = Emit( Op.JmpFalse );
			ParseBlock();
		}
		if ( Peek.Kind == Tok.Else )
		{
			endJumps.Add( Emit( Op.Jmp ) );
			PatchHere( skip );
			skip = -1;
			Next();
			ParseBlock();
		}
		Expect( Tok.EndKw, "'end'" );
		if ( skip >= 0 )
			PatchHere( skip );
		foreach ( var jump in endJumps )
			PatchHere( jump );
	}

	private void ParseWhile()
	{
		Next();
		var head = code.Count;
		ParseExpr();
		Expect( Tok.Do, "'do'" );
		var exit = Emit( Op.JmpFalse );
		loops.Add( new List<int>() );
		ParseBlock();
		Expect( Tok.EndKw, "'end'" );
		Emit( Op.Jmp, head );
		PatchHere( exit );
		foreach ( var jump in loops[^1] )
			PatchHere( jump );
		loops.RemoveAt( loops.Count - 1 );
	}

	private void ParseFor()
	{
		Next();
		var name = Expect( Tok.Name, "a loop variable name" );
		CheckNewName( name, "a variable" );
		Expect( Tok.Assign, "'=' (only numeric for loops exist)" );
		var slotMark = nextSlot;
		var localMark = locals.Count;
		var baseSlot = DeclareHidden();
		DeclareHidden();
		DeclareHidden();
		ParseExpr();
		Expect( Tok.Comma, "','" );
		ParseExpr();
		if ( Accept( Tok.Comma ) )
			ParseExpr();
		else
			EmitConst( 1L );
		Expect( Tok.Do, "'do'" );
		Emit( Op.ForPrep, baseSlot );
		var head = Emit( Op.ForTest, baseSlot );
		DeclareLocal( name );
		loops.Add( new List<int>() );
		ParseBlock();
		Expect( Tok.EndKw, "'end'" );
		Emit( Op.ForNext, baseSlot, head );
		code[head] = code[head] with { B = code.Count };
		foreach ( var jump in loops[^1] )
			PatchHere( jump );
		loops.RemoveAt( loops.Count - 1 );
		locals.RemoveRange( localMark, locals.Count - localMark );
		nextSlot = slotMark;
	}

	private void ParseNameStatement()
	{
		var name = Next();
		if ( Peek.Kind == Tok.LParen )
		{
			EmitCall( name );
			Emit( Op.Pop );
			if ( Peek.Kind is Tok.Dot or Tok.LBracket or Tok.Assign )
				throw Error( Peek, "expected a statement; a call used as a statement cannot be indexed or assigned to" );
			return;
		}

		if ( name.Text != "state" )
		{
			var slot = FindLocal( name.Text );
			if ( slot < 0 )
			{
				if ( topConstants.ContainsKey( name.Text ) && Peek.Kind == Tok.Assign )
					throw Error( name, $"cannot assign to the constant '{name.Text}'" );
				if ( functionIndex.ContainsKey( name.Text ) && Peek.Kind == Tok.Assign )
					throw Error( name, $"cannot assign to the function '{name.Text}'" );
				if ( Peek.Kind == Tok.Assign )
					throw Error( name, $"'{name.Text}' is not a local variable; assigning to it would create a global, and there are none (declare it with local, or keep data in state)" );
			}
			else if ( Peek.Kind == Tok.Assign )
			{
				Next();
				ParseExpr();
				Emit( Op.SetLocal, slot );
				return;
			}
		}
		else if ( Peek.Kind == Tok.Assign )
		{
			throw Error( Peek, "cannot assign to state itself; assign to its fields" );
		}

		EmitName( name );
		if ( Peek.Kind is not (Tok.Dot or Tok.LBracket) )
			throw Error( Peek, "expected an assignment or a call as a statement" );
		while ( true )
		{
			if ( Accept( Tok.Dot ) )
				EmitConst( ScriptValue.FromString( Expect( Tok.Name, "a field name" ).Text ) );
			else if ( Accept( Tok.LBracket ) )
			{
				ParseExpr();
				Expect( Tok.RBracket, "']'" );
			}
			else
				throw Error( Peek, "expected '=' after the assignment target" );

			if ( Accept( Tok.Assign ) )
			{
				ParseExpr();
				Emit( Op.SetIndex );
				Accept( Tok.Semicolon );
				return;
			}
			Emit( Op.Index );
		}
	}

	// ----- expressions -----

	private void ParseExpr()
	{
		Enter();
		ParseAnd();
		while ( Peek.Kind == Tok.Or )
		{
			Next();
			var jump = Emit( Op.JmpTrueKeep );
			ParseAnd();
			Emit( Op.CheckBool );
			PatchHere( jump );
		}
		Leave();
	}

	private void ParseAnd()
	{
		ParseComparison();
		while ( Peek.Kind == Tok.And )
		{
			Next();
			var jump = Emit( Op.JmpFalseKeep );
			ParseComparison();
			Emit( Op.CheckBool );
			PatchHere( jump );
		}
	}

	private void ParseComparison()
	{
		ParseConcat();
		while ( Peek.Kind is Tok.Lt or Tok.Le or Tok.Gt or Tok.Ge or Tok.EqEq or Tok.TildeEq )
		{
			var kind = Next().Kind;
			ParseConcat();
			Emit( kind switch { Tok.Lt => Op.Lt, Tok.Le => Op.Le, Tok.Gt => Op.Gt, Tok.Ge => Op.Ge, Tok.EqEq => Op.Eq, _ => Op.Ne } );
		}
	}

	private void ParseConcat()
	{
		ParseAdd();
		if ( Peek.Kind == Tok.DotDot )
		{
			Next();
			Enter();
			ParseConcat();
			Leave();
			Emit( Op.Concat );
		}
	}

	private void ParseAdd()
	{
		ParseMul();
		while ( Peek.Kind is Tok.Plus or Tok.Minus )
		{
			var kind = Next().Kind;
			ParseMul();
			Emit( kind == Tok.Plus ? Op.Add : Op.Sub );
		}
	}

	private void ParseMul()
	{
		ParseUnary();
		while ( Peek.Kind is Tok.Star or Tok.SlashSlash or Tok.Percent )
		{
			var kind = Next().Kind;
			ParseUnary();
			Emit( kind switch { Tok.Star => Op.Mul, Tok.SlashSlash => Op.IDiv, _ => Op.Mod } );
		}
	}

	private void ParseUnary()
	{
		if ( Peek.Kind == Tok.Not )
		{
			Next();
			Enter();
			ParseUnary();
			Leave();
			Emit( Op.Not );
		}
		else if ( Peek.Kind == Tok.Minus )
		{
			Next();
			Enter();
			ParseUnary();
			Leave();
			Emit( Op.Neg );
		}
		else
		{
			ParsePrimary();
			while ( Peek.Kind is Tok.Dot or Tok.LBracket )
			{
				if ( Accept( Tok.Dot ) )
					EmitConst( ScriptValue.FromString( Expect( Tok.Name, "a field name" ).Text ) );
				else
				{
					Next();
					ParseExpr();
					Expect( Tok.RBracket, "']'" );
				}
				Emit( Op.Index );
			}
		}
	}

	private void ParsePrimary()
	{
		var token = Peek;
		switch ( token.Kind )
		{
			case Tok.Int: Next(); EmitConst( token.Number ); return;
			case Tok.String: Next(); EmitConst( ScriptValue.FromString( token.Text ) ); return;
			case Tok.True: Next(); EmitConst( true ); return;
			case Tok.False: Next(); EmitConst( false ); return;
			case Tok.Fixed: throw Error( token, "a fixed-point literal must be written fx(1.25)" );
			case Tok.LParen:
				Next();
				ParseExpr();
				Expect( Tok.RParen, "')'" );
				return;
			case Tok.LBrace: ParseTableConstructor(); return;
			case Tok.Function: throw Error( token, "function values do not exist (no closures); define functions at the top level and call them by name" );
			case Tok.Name:
				Next();
				if ( Peek.Kind == Tok.LParen )
				{
					EmitCall( token );
					Emit( Op.CheckValue );
				}
				else
				{
					EmitName( token );
				}
				return;
			default:
				throw Error( token, $"unexpected {Describe( token )}; expected an expression" );
		}
	}

	private void EmitName( Token name )
	{
		if ( name.Text == "state" )
		{
			Emit( Op.State );
			return;
		}
		var slot = FindLocal( name.Text );
		if ( slot >= 0 )
		{
			Emit( Op.Local, slot );
			return;
		}
		if ( topConstants.TryGetValue( name.Text, out var constant ) )
		{
			EmitConst( constant );
			return;
		}
		if ( functionIndex.ContainsKey( name.Text ) || ScriptHostFunctions.TryFind( name.Text, out _ ) || name.Text == "fx" )
			throw Error( name, $"'{name.Text}' is a function, not a value; call it as {name.Text}(...)" );
		throw Error( name, $"unknown name '{name.Text}' (there are no globals; only locals, top-level constants, state and the host functions exist)" );
	}

	private void EmitCall( Token name )
	{
		Next(); // '('
		if ( name.Text == "fx" )
		{
			pos--; // ParseFxBody expects the '('
			EmitConst( ParseFxBody( name ) );
			return;
		}
		var arguments = 0;
		if ( Peek.Kind != Tok.RParen )
		{
			do
			{
				if ( arguments >= ScriptLimits.MaxParameters )
					throw Error( Peek, $"too many arguments (limit {ScriptLimits.MaxParameters})" );
				ParseExpr();
				arguments++;
			}
			while ( Accept( Tok.Comma ) );
		}
		Expect( Tok.RParen, "')'" );

		if ( ScriptHostFunctions.TryFind( name.Text, out var host ) )
		{
			var arity = ScriptHostFunctions.All[host].Arity;
			if ( arity != arguments )
				throw Error( name, $"{name.Text} takes {arity} argument(s), not {arguments}" );
			Emit( Op.CallHost, host, arguments );
			return;
		}
		pendingCalls.Add( new PendingCall( name.Text, functions.Count - 1, code.Count, arguments, name.Line, name.Column ) );
		Emit( Op.Call, -1, arguments );
	}

	private void ParseTableConstructor()
	{
		Next();
		Enter();
		var count = 0;
		if ( Peek.Kind == Tok.RBrace )
		{
			Next();
			Emit( Op.NewArray, 0 );
			Leave();
			return;
		}

		if ( Peek.Kind == Tok.Name && PeekAt( 1 ).Kind == Tok.Assign )
		{
			var seen = new HashSet<string>( StringComparer.Ordinal );
			do
			{
				if ( Peek.Kind == Tok.RBrace )
					break;
				var key = Expect( Tok.Name, "a field name" );
				if ( !seen.Add( key.Text ) )
					throw Error( key, $"duplicate field '{key.Text}'" );
				Expect( Tok.Assign, "'='" );
				EmitConst( ScriptValue.FromString( key.Text ) );
				ParseExpr();
				CheckEntryCount( ++count );
			}
			while ( Accept( Tok.Comma ) );
			Expect( Tok.RBrace, "'}'" );
			Emit( Op.NewMap, count );
		}
		else
		{
			do
			{
				if ( Peek.Kind == Tok.RBrace )
					break;
				ParseExpr();
				CheckEntryCount( ++count );
			}
			while ( Accept( Tok.Comma ) );
			Expect( Tok.RBrace, "'}'" );
			Emit( Op.NewArray, count );
		}
		Leave();
	}

	private void CheckEntryCount( int count )
	{
		if ( count > ScriptLimits.MaxTableEntries )
			throw Error( Peek, $"too many table entries (limit {ScriptLimits.MaxTableEntries})" );
	}

	// ----- calls to this script's functions -----

	private void ResolveCalls()
	{
		foreach ( var call in pendingCalls )
		{
			if ( !functionIndex.TryGetValue( call.Name, out var target ) )
				throw new ScriptCompileException( $"unknown function '{call.Name}'", call.Line, call.Column );
			var parameters = functions[target].ParameterCount;
			if ( parameters != call.Arguments )
				throw new ScriptCompileException( $"{call.Name} takes {parameters} argument(s), not {call.Arguments}", call.Line, call.Column );
			functions[call.Function].Code[call.Instruction] = new Instr( Op.Call, target, call.Arguments );
		}
	}
}

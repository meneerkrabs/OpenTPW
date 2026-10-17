using System.Buffers.Binary;
using System.Globalization;
using System.Text;

namespace OpenTPW;

/// <summary>
/// Opcode names for the RSE text form. The names come from the <see cref="Opcode"/> enum (the VM's table keys), so the assembler,
/// the disassembler and <c>RideVM</c> cannot disagree; opcode numbers without an enum member are written <c>OP_n</c>.
/// </summary>
public static class RideScriptOpcodeNames
{
	private static readonly Dictionary<string, ushort> ByName = Enum.GetValues<Opcode>().ToDictionary( x => x.ToString(), x => (ushort)x, StringComparer.OrdinalIgnoreCase );

	public static string GetName( ushort opcode ) => Enum.IsDefined( typeof( Opcode ), (int)opcode ) ? ((Opcode)opcode).ToString() : $"OP_{opcode}";

	/// <summary>Parses an opcode name (case-insensitive) or <c>OP_n</c> with n in 0..0x7FFF.</summary>
	public static bool TryParse( string name, out ushort opcode )
	{
		if ( ByName.TryGetValue( name, out opcode ) )
			return true;
		if ( name.StartsWith( "OP_", StringComparison.OrdinalIgnoreCase ) && uint.TryParse( name.AsSpan( 3 ), NumberStyles.None, CultureInfo.InvariantCulture, out var number ) && number <= 0x7FFF )
		{
			opcode = (ushort)number;
			return true;
		}
		opcode = 0;
		return false;
	}
}

/// <summary>A malformed RSE text script. <see cref="Line"/> and <see cref="Column"/> are 1-based; 0 means the error has no source position (a whole-file limit).</summary>
public sealed class RideScriptAssemblyException : FormatException
{
	public int Line { get; }
	public int Column { get; }

	public RideScriptAssemblyException( int line, int column, string message ) : base( line > 0 ? $"line {line}, column {column}: {message}" : message )
	{
		Line = line;
		Column = column;
	}
}

/// <summary>
/// Writes the RSE text form of a compiled script (see docs/RSE-SCRIPTS.md, "Text form and assembler").
/// The text is lossless: <see cref="RideScriptAssembler.Assemble(string)"/> rebuilds the original bytes.
/// </summary>
public static class RideScriptDisassembler
{
	public static string Disassemble( byte[] data )
	{
		ArgumentNullException.ThrowIfNull( data );
		using var stream = new MemoryStream( data, false );
		return Disassemble( new RideScriptFile( stream ) );
	}

	public static string Disassemble( RideScriptFile script )
	{
		ArgumentNullException.ThrowIfNull( script );

		// the string blob in file order; each operand names an entry by its offset
		var blob = script.Strings.OrderBy( x => x.Key ).ToList();
		var indexByOffset = new Dictionary<int, int>();
		for ( var index = 0; index < blob.Count; ++index )
			indexByOffset[blob[index].Key] = index;

		// the assembler's default layout (first-reference order, no duplicates, no unreferenced strings) needs no .string lines
		var defaultLayout = new List<string>();
		foreach ( var instruction in script.Instructions )
			foreach ( var operand in instruction.Operands )
				if ( operand.Kind == RideScriptOperandKind.String && !defaultLayout.Contains( script.Strings[operand.Value] ) )
					defaultLayout.Add( script.Strings[operand.Value] );
		var explicitBlob = !defaultLayout.SequenceEqual( blob.Select( x => x.Value ) );

		var names = script.VariableNames;
		var nameCounts = names.GroupBy( x => x, StringComparer.Ordinal ).ToDictionary( x => x.Key, x => x.Count(), StringComparer.Ordinal );
		string VariableText( int index ) => RideScriptAssembler.IsIdentifier( names[index] ) && nameCounts[names[index]] == 1 ? names[index] : $"${index}";

		var targets = new SortedSet<int>();
		foreach ( var instruction in script.Instructions )
			foreach ( var operand in instruction.Operands )
				if ( operand.Kind == RideScriptOperandKind.Branch )
					targets.Add( operand.Value );

		var text = new StringBuilder();
		text.Append( "; RSSEQ ride script\n" );
		text.Append( $".stack {script.StackSize}\n" );
		text.Append( $".timeslice {script.TimeSlice}\n" );
		text.Append( $".limbo {script.LimboSize}\n" );
		text.Append( $".bounce {script.BounceSize}\n" );
		text.Append( $".walk {script.WalkSize}\n" );
		if ( names.Count > 0 )
			text.Append( '\n' );
		foreach ( var name in names )
			text.Append( ".var " ).Append( RideScriptAssembler.IsIdentifier( name ) ? name : Quote( name ) ).Append( '\n' );
		if ( explicitBlob )
		{
			text.Append( '\n' );
			foreach ( var entry in blob )
				text.Append( ".string " ).Append( Quote( entry.Value ) ).Append( '\n' );
		}
		text.Append( '\n' );

		foreach ( var instruction in script.Instructions )
		{
			if ( targets.Contains( instruction.WordOffset ) )
				text.Append( 'L' ).Append( instruction.WordOffset ).Append( ":\n" );
			text.Append( '\t' ).Append( RideScriptOpcodeNames.GetName( instruction.Opcode ) );
			foreach ( var operand in instruction.Operands )
			{
				text.Append( ' ' );
				switch ( operand.Kind )
				{
					case RideScriptOperandKind.Literal:
						text.Append( operand.Value < 0x8000 ? operand.Value.ToString( CultureInfo.InvariantCulture ) : $"0x{operand.Value:X4}" );
						break;
					case RideScriptOperandKind.Variable:
						text.Append( VariableText( operand.Value ) );
						break;
					case RideScriptOperandKind.Branch:
						text.Append( "@L" ).Append( operand.Value );
						break;
					case RideScriptOperandKind.String:
						var value = script.Strings[operand.Value];
						var entry = indexByOffset[operand.Value];
						// with explicit .string lines a text can occur twice; only the first entry is reachable by its text
						if ( explicitBlob && blob.FindIndex( x => x.Value == value ) != entry )
							text.Append( '#' ).Append( entry );
						else
							text.Append( Quote( value ) );
						break;
				}
			}
			text.Append( '\n' );
		}
		return text.ToString();
	}

	private static string Quote( string value )
	{
		var builder = new StringBuilder( value.Length + 2 ).Append( '"' );
		foreach ( var character in value )
		{
			switch ( character )
			{
				case '"': builder.Append( "\\\"" ); break;
				case '\\': builder.Append( "\\\\" ); break;
				case '\n': builder.Append( "\\n" ); break;
				case '\t': builder.Append( "\\t" ); break;
				default:
					if ( character < 0x20 || character >= 0x7F )
						builder.Append( $"\\x{(int)character:X2}" );
					else
						builder.Append( character );
					break;
			}
		}
		return builder.Append( '"' ).ToString();
	}
}

/// <summary>
/// Builds a compiled RSE script from its text form. The output is checked by <see cref="RideScriptFile"/> before it is returned,
/// so every result is a file the reader accepts. Operand counts are not checked here: <c>RideVM</c> rejects a wrong count at load.
/// </summary>
public static class RideScriptAssembler
{
	/// <summary>
	/// Defaults for header directives the text omits. Only the time slice is verified (50 in all 308 corpus members). The others are unverified
	/// authoring choices informed by the corpus: stack 20 is the most common non-zero size (10 members; 253 declare 0), limbo, bounce and walk 0
	/// are the most common values (284, 304 and 271 members).
	/// </summary>
	public const int DefaultStackSize = 20;
	public const int DefaultTimeSlice = 50;
	public const int DefaultLimboSize = 0;
	public const int DefaultBounceSize = 0;
	public const int DefaultWalkSize = 0;

	private static ReadOnlySpan<byte> Magic => new byte[] { (byte)'R', (byte)'S', (byte)'S', (byte)'E', (byte)'Q', 0x0F, 0x01, 0x00 };

	private enum TokenKind { Word, String }

	private readonly record struct Token( TokenKind Kind, string Text, int Column );

	private sealed class Statement
	{
		public int Line;
		public Token Opcode;
		public ushort OpcodeValue;
		public List<Token> Operands = new();
		public int WordOffset;
	}

	public static bool IsIdentifier( string text )
	{
		if ( text.Length == 0 || !(char.IsAsciiLetter( text[0] ) || text[0] == '_') )
			return false;
		foreach ( var character in text )
			if ( !(char.IsAsciiLetterOrDigit( character ) || character == '_' || character == '.') )
				return false;
		return true;
	}

	public static byte[] Assemble( string text )
	{
		ArgumentNullException.ThrowIfNull( text );
		var header = new Dictionary<string, (int Value, int Line)>();
		var variables = new List<string>();
		var explicitStrings = new List<string>();
		var labels = new Dictionary<string, (int Word, int Line)>();
		var statements = new List<Statement>();
		var wordOffset = 0;

		var lines = text.Replace( "\r\n", "\n" ).Replace( '\r', '\n' ).Split( '\n' );
		for ( var lineIndex = 0; lineIndex < lines.Length; ++lineIndex )
		{
			var line = lineIndex + 1;
			var tokens = Tokenize( lines[lineIndex], line );
			var start = 0;
			// any number of "name:" labels may precede a statement
			while ( start < tokens.Count && tokens[start].Kind == TokenKind.Word && tokens[start].Text.EndsWith( ':' ) )
			{
				var name = tokens[start].Text[..^1];
				if ( !IsIdentifier( name ) )
					throw new RideScriptAssemblyException( line, tokens[start].Column, $"'{name}' is not a valid label name (letters, digits, '_' and '.', not starting with a digit)." );
				if ( labels.TryGetValue( name, out var previous ) )
					throw new RideScriptAssemblyException( line, tokens[start].Column, $"label '{name}' is already defined on line {previous.Line}." );
				labels[name] = (wordOffset, line);
				++start;
			}
			if ( start >= tokens.Count )
				continue;

			var first = tokens[start];
			if ( first.Kind == TokenKind.Word && first.Text.StartsWith( '.' ) )
			{
				ParseDirective( tokens, start, line, header, variables, explicitStrings );
				continue;
			}
			if ( first.Kind != TokenKind.Word )
				throw new RideScriptAssemblyException( line, first.Column, "expected an opcode name, a label or a directive." );
			if ( !RideScriptOpcodeNames.TryParse( first.Text, out var opcode ) )
				throw new RideScriptAssemblyException( line, first.Column, $"unknown opcode '{first.Text}'." );
			var statement = new Statement { Line = line, Opcode = first, OpcodeValue = opcode, WordOffset = wordOffset };
			for ( var index = start + 1; index < tokens.Count; ++index )
				statement.Operands.Add( tokens[index] );
			statements.Add( statement );
			wordOffset += 1 + statement.Operands.Count;
			if ( wordOffset > RideScriptFile.MaximumCodeWords )
				throw new RideScriptAssemblyException( line, first.Column, $"the script exceeds {RideScriptFile.MaximumCodeWords} code words." );
		}
		if ( variables.Count > RideScriptFile.MaximumVariables )
			throw new RideScriptAssemblyException( 0, 0, $"the script declares more than {RideScriptFile.MaximumVariables} variables." );

		// string table: explicit .string lines, else every referenced text once in order of first reference
		var strings = new List<string>( explicitStrings );
		var automaticStrings = explicitStrings.Count == 0;
		var words = new List<uint>( wordOffset );
		var targetsToCheck = new List<(int Line, int Column, int Word)>();
		var offsets = new List<int>();
		var length = 0;

		int StringOffset( int index )
		{
			while ( offsets.Count <= index )
			{
				offsets.Add( length );
				length += Encoding.Latin1.GetByteCount( strings[offsets.Count - 1] ) + 1;
			}
			return offsets[index];
		}

		foreach ( var statement in statements )
		{
			words.Add( 0x8000_0000u | statement.OpcodeValue );
			foreach ( var operand in statement.Operands )
				words.Add( ResolveOperand( statement, operand, variables, labels, strings, automaticStrings, StringOffset, targetsToCheck ) );
		}
		var opcodeWords = statements.Select( x => x.WordOffset ).ToHashSet();
		foreach ( var (line, column, word) in targetsToCheck )
			if ( !opcodeWords.Contains( word ) )
				throw new RideScriptAssemblyException( line, column, "a branch label must be followed by an instruction." );
		var blob = new MemoryStream();
		foreach ( var entry in strings )
		{
			var bytes = Encoding.Latin1.GetBytes( entry );
			blob.Write( bytes );
			blob.WriteByte( 0 );
		}

		var output = new MemoryStream();
		var writer = new BinaryWriter( output );
		writer.Write( Magic );
		writer.Write( variables.Count );
		writer.Write( Header( header, ".stack", DefaultStackSize ) );
		writer.Write( Header( header, ".timeslice", DefaultTimeSlice ) );
		writer.Write( Header( header, ".limbo", DefaultLimboSize ) );
		writer.Write( Header( header, ".bounce", DefaultBounceSize ) );
		writer.Write( Header( header, ".walk", DefaultWalkSize ) );
		writer.Write( "Pad Pad Pad Pad "u8 );
		writer.Write( (uint)words.Count );
		foreach ( var word in words )
			writer.Write( word );
		writer.Write( (uint)blob.Length );
		writer.Write( blob.ToArray() );
		foreach ( var name in variables )
		{
			var bytes = Encoding.Latin1.GetBytes( name );
			writer.Write( (uint)(bytes.Length + 1) );
			writer.Write( bytes );
			writer.Write( (byte)0 );
		}
		var result = output.ToArray();
		try
		{
			using var stream = new MemoryStream( result, false );
			_ = new RideScriptFile( stream );
		}
		catch ( InvalidDataException exception )
		{
			throw new RideScriptAssemblyException( 0, 0, $"the assembled script is not a valid RSE file: {exception.Message}" );
		}
		return result;
	}

	private static int Header( Dictionary<string, (int Value, int Line)> header, string name, int fallback ) => header.TryGetValue( name, out var entry ) ? entry.Value : fallback;

	private static void ParseDirective( List<Token> tokens, int start, int line, Dictionary<string, (int Value, int Line)> header, List<string> variables, List<string> strings )
	{
		var directive = tokens[start];
		var argumentCount = tokens.Count - start - 1;
		void RequireOneArgument()
		{
			if ( argumentCount != 1 )
				throw new RideScriptAssemblyException( line, directive.Column, $"{directive.Text} takes exactly one argument, found {argumentCount}." );
		}
		switch ( directive.Text.ToLowerInvariant() )
		{
			case ".stack":
			case ".timeslice":
			case ".limbo":
			case ".bounce":
			case ".walk":
				RequireOneArgument();
				var name = directive.Text.ToLowerInvariant();
				var argument = tokens[start + 1];
				if ( argument.Kind != TokenKind.Word || !TryParseNumber( argument.Text, out var number ) || number < int.MinValue || number > int.MaxValue )
					throw new RideScriptAssemblyException( line, argument.Column, $"{directive.Text} needs a 32-bit integer, found '{argument.Text}'." );
				if ( header.TryGetValue( name, out var previous ) )
					throw new RideScriptAssemblyException( line, directive.Column, $"{directive.Text} is already set on line {previous.Line}." );
				header[name] = ((int)number, line);
				break;
			case ".var":
				RequireOneArgument();
				var variable = tokens[start + 1];
				if ( variable.Kind == TokenKind.Word && !IsIdentifier( variable.Text ) )
					throw new RideScriptAssemblyException( line, variable.Column, $"'{variable.Text}' is not a valid variable name; quote names with other characters." );
				RequireLatin1( variable, line, nonEmpty: true );
				variables.Add( variable.Text );
				break;
			case ".string":
				RequireOneArgument();
				if ( tokens[start + 1].Kind != TokenKind.String )
					throw new RideScriptAssemblyException( line, tokens[start + 1].Column, ".string needs a quoted string." );
				RequireLatin1( tokens[start + 1], line, nonEmpty: false );
				strings.Add( tokens[start + 1].Text );
				break;
			default:
				throw new RideScriptAssemblyException( line, directive.Column, $"unknown directive '{directive.Text}'." );
		}
	}

	private static void RequireLatin1( Token token, int line, bool nonEmpty )
	{
		if ( nonEmpty && token.Text.Length == 0 )
			throw new RideScriptAssemblyException( line, token.Column, "a variable name cannot be empty." );
		foreach ( var character in token.Text )
		{
			if ( character > 0xFF )
				throw new RideScriptAssemblyException( line, token.Column, $"character U+{(int)character:X4} does not fit the script's Latin-1 text." );
			if ( character == 0 )
				throw new RideScriptAssemblyException( line, token.Column, "text cannot contain a NUL character." );
		}
	}

	private static uint ResolveOperand( Statement statement, Token operand, List<string> variables, Dictionary<string, (int Word, int Line)> labels,
		List<string> strings, bool automaticStrings, Func<int, int> stringOffset, List<(int Line, int Column, int Word)> targets )
	{
		var line = statement.Line;
		if ( operand.Kind == TokenKind.String )
		{
			RequireLatin1( operand, line, nonEmpty: false );
			var index = strings.IndexOf( operand.Text );
			if ( index < 0 )
			{
				if ( !automaticStrings )
					throw new RideScriptAssemblyException( line, operand.Column, $"the string \"{operand.Text}\" is not in the .string table." );
				strings.Add( operand.Text );
				index = strings.Count - 1;
			}
			return StringWord( stringOffset( index ), line, operand.Column );
		}

		var text = operand.Text;
		if ( text.StartsWith( '@' ) )
		{
			if ( !labels.TryGetValue( text[1..], out var label ) )
				throw new RideScriptAssemblyException( line, operand.Column, $"undefined label '{text[1..]}'." );
			targets.Add( (line, operand.Column, label.Word) );
			return 0x2000_0000u | (uint)label.Word;
		}
		if ( text.StartsWith( '#' ) )
		{
			if ( !int.TryParse( text.AsSpan( 1 ), NumberStyles.None, CultureInfo.InvariantCulture, out var stringIndex ) || stringIndex >= strings.Count || automaticStrings )
				throw new RideScriptAssemblyException( line, operand.Column, $"'{text}' is not an entry of the .string table." );
			return StringWord( stringOffset( stringIndex ), line, operand.Column );
		}
		if ( text.StartsWith( '$' ) )
		{
			if ( !int.TryParse( text.AsSpan( 1 ), NumberStyles.None, CultureInfo.InvariantCulture, out var variableIndex ) || variableIndex >= variables.Count )
				throw new RideScriptAssemblyException( line, operand.Column, $"'{text}' is not a declared variable index (the script declares {variables.Count})." );
			return 0x4000_0000u | (uint)variableIndex;
		}
		if ( char.IsAsciiDigit( text[0] ) || text[0] == '-' )
		{
			if ( !TryParseNumber( text, out var value ) )
				throw new RideScriptAssemblyException( line, operand.Column, $"'{text}' is not a valid number." );
			// negative literals are 16-bit two's complement; whether the VM reads them as signed is unverified
			if ( value < -0x8000 || value > 0xFFFF )
				throw new RideScriptAssemblyException( line, operand.Column, $"literal {text} does not fit 16 bits (-32768..65535)." );
			return (uint)(value & 0xFFFF);
		}
		var matches = new List<int>();
		for ( var index = 0; index < variables.Count; ++index )
			if ( string.Equals( variables[index], text, StringComparison.Ordinal ) )
				matches.Add( index );
		if ( matches.Count == 0 )
			throw new RideScriptAssemblyException( line, operand.Column, $"undeclared variable '{text}' (declare it with .var; labels are written @name)." );
		if ( matches.Count > 1 )
			throw new RideScriptAssemblyException( line, operand.Column, $"variable name '{text}' is declared {matches.Count} times; write its index as ${matches[0]}." );
		return 0x4000_0000u | (uint)matches[0];
	}

	private static uint StringWord( int offset, int line, int column )
	{
		if ( offset > 0xFFFF )
			throw new RideScriptAssemblyException( line, column, "the string lies beyond offset 65535 of the string table, which a 16-bit operand cannot address." );
		return 0x1000_0000u | (uint)offset;
	}

	private static bool TryParseNumber( string text, out long value )
	{
		value = 0;
		var negative = text.StartsWith( '-' );
		var digits = negative ? text.AsSpan( 1 ) : text.AsSpan();
		var hex = digits.StartsWith( "0x", StringComparison.OrdinalIgnoreCase );
		if ( hex )
			digits = digits[2..];
		// at most 8 hex or 10 decimal digits keep the value inside a long; the callers range-check it
		if ( digits.Length == 0 || digits.Length > (hex ? 8 : 10) )
			return false;
		if ( !long.TryParse( digits, hex ? NumberStyles.AllowHexSpecifier : NumberStyles.None, CultureInfo.InvariantCulture, out value ) )
			return false;
		if ( negative )
			value = -value;
		return true;
	}

	private static List<Token> Tokenize( string line, int lineNumber )
	{
		var tokens = new List<Token>();
		var index = 0;
		while ( index < line.Length )
		{
			var character = line[index];
			if ( char.IsWhiteSpace( character ) )
			{
				++index;
				continue;
			}
			if ( character == ';' )
				break;
			var column = index + 1;
			if ( character == '"' )
			{
				var builder = new StringBuilder();
				++index;
				while ( true )
				{
					if ( index >= line.Length )
						throw new RideScriptAssemblyException( lineNumber, column, "unterminated string (missing closing quote)." );
					var current = line[index++];
					if ( current == '"' )
						break;
					if ( current != '\\' )
					{
						builder.Append( current );
						continue;
					}
					if ( index >= line.Length )
						throw new RideScriptAssemblyException( lineNumber, index, "unterminated escape sequence." );
					var escape = line[index++];
					switch ( escape )
					{
						case '"': builder.Append( '"' ); break;
						case '\\': builder.Append( '\\' ); break;
						case 'n': builder.Append( '\n' ); break;
						case 't': builder.Append( '\t' ); break;
						case 'x':
							if ( index + 2 > line.Length || !int.TryParse( line.AsSpan( index, 2 ), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var code ) )
								throw new RideScriptAssemblyException( lineNumber, index - 1, "\\x needs two hexadecimal digits." );
							builder.Append( (char)code );
							index += 2;
							break;
						default:
							throw new RideScriptAssemblyException( lineNumber, index - 1, $"unknown escape '\\{escape}' (use \\\" \\\\ \\n \\t or \\xHH)." );
					}
				}
				tokens.Add( new Token( TokenKind.String, builder.ToString(), column ) );
				continue;
			}
			var begin = index;
			while ( index < line.Length && !char.IsWhiteSpace( line[index] ) && line[index] != ';' )
			{
				if ( line[index] == '"' )
					throw new RideScriptAssemblyException( lineNumber, index + 1, "unexpected quote inside a word." );
				++index;
			}
			tokens.Add( new Token( TokenKind.Word, line[begin..index], column ) );
		}
		return tokens;
	}
}

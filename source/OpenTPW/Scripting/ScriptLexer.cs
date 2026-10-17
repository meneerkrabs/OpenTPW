using System.Globalization;
using System.Text;

namespace OpenTPW.Scripting;

internal enum Tok : byte
{
	End, Name, Int, Fixed, String,
	// Keywords
	Function, Local, If, Then, Elseif, Else, While, Do, For, Return, Break, EndKw, And, Or, Not, True, False,
	// Punctuation
	Plus, Minus, Star, SlashSlash, Percent, EqEq, TildeEq, Lt, Le, Gt, Ge, Assign,
	LParen, RParen, LBrace, RBrace, LBracket, RBracket, Comma, Dot, DotDot, Semicolon
}

internal readonly record struct Token( Tok Kind, string Text, long Number, int Line, int Column );

/// <summary>Turns source text into tokens, with the static limits (tokens, string length, name length). Every error carries line and column.</summary>
internal static class ScriptLexer
{
	private static readonly Dictionary<string, Tok> Keywords = new( StringComparer.Ordinal )
	{
		["function"] = Tok.Function, ["local"] = Tok.Local, ["if"] = Tok.If, ["then"] = Tok.Then, ["elseif"] = Tok.Elseif, ["else"] = Tok.Else,
		["while"] = Tok.While, ["do"] = Tok.Do, ["for"] = Tok.For, ["return"] = Tok.Return, ["break"] = Tok.Break, ["end"] = Tok.EndKw,
		["and"] = Tok.And, ["or"] = Tok.Or, ["not"] = Tok.Not, ["true"] = Tok.True, ["false"] = Tok.False
	};

	/// <summary>Words that exist in Lua and are refused here, with the reason a modder needs.</summary>
	private static readonly Dictionary<string, string> Refused = new( StringComparer.Ordinal )
	{
		["nil"] = "there is no nil; test a key with has(t, k)",
		["repeat"] = "repeat/until is not supported; use while",
		["until"] = "repeat/until is not supported; use while",
		["goto"] = "goto is not supported",
		["in"] = "generic for (pairs/ipairs) is not supported; use a numeric for with len(t)"
	};

	public static List<Token> Lex( string text )
	{
		var tokens = new List<Token>();
		int i = 0, line = 1, lineStart = 0;

		void Add( Tok kind, string value, long number, int startIndex )
		{
			if ( tokens.Count >= ScriptLimits.MaxTokens )
				throw new ScriptCompileException( $"too many tokens (limit {ScriptLimits.MaxTokens})", line, startIndex - lineStart + 1 );
			tokens.Add( new Token( kind, value, number, line, startIndex - lineStart + 1 ) );
		}

		while ( i < text.Length )
		{
			var c = text[i];
			int col = i - lineStart + 1;
			if ( c == '\n' ) { line++; i++; lineStart = i; continue; }
			if ( c is ' ' or '\t' or '\r' ) { i++; continue; }

			if ( c == '-' && i + 1 < text.Length && text[i + 1] == '-' )
			{
				if ( i + 3 < text.Length && text[i + 2] == '[' && text[i + 3] == '[' )
				{
					var close = text.IndexOf( "]]", i + 4, StringComparison.Ordinal );
					if ( close < 0 )
						throw new ScriptCompileException( "unterminated block comment", line, col );
					for ( int k = i; k < close; k++ )
						if ( text[k] == '\n' ) { line++; lineStart = k + 1; }
					i = close + 2;
				}
				else
				{
					while ( i < text.Length && text[i] != '\n' )
						i++;
				}
				continue;
			}

			if ( IsAsciiLetter( c ) || c == '_' )
			{
				int start = i;
				while ( i < text.Length && (IsAsciiLetter( text[i] ) || IsDigit( text[i] ) || text[i] == '_') )
					i++;
				var word = text.Substring( start, i - start );
				if ( word.Length > ScriptLimits.MaxNameLength )
					throw new ScriptCompileException( $"name too long (limit {ScriptLimits.MaxNameLength})", line, col );
				if ( Refused.TryGetValue( word, out var why ) )
					throw new ScriptCompileException( $"'{word}' is not supported: {why}", line, col );
				Add( Keywords.TryGetValue( word, out var keyword ) ? keyword : Tok.Name, word, 0, start );
				continue;
			}

			if ( IsDigit( c ) )
			{
				int start = i;
				while ( i < text.Length && IsDigit( text[i] ) )
					i++;
				if ( i < text.Length && (IsAsciiLetter( text[i] ) || text[i] == '_') )
					throw new ScriptCompileException( "malformed number", line, col );
				if ( !long.TryParse( text.AsSpan( start, i - start ), NumberStyles.None, CultureInfo.InvariantCulture, out var whole ) )
					throw new ScriptCompileException( "integer literal too large", line, col );
				if ( i + 1 < text.Length && text[i] == '.' && IsDigit( text[i + 1] ) )
				{
					int fracStart = ++i;
					while ( i < text.Length && IsDigit( text[i] ) )
						i++;
					if ( i - fracStart > 2 )
						throw new ScriptCompileException( "a fixed-point literal has at most 2 decimals (hundredths)", line, col );
					if ( i < text.Length && (IsAsciiLetter( text[i] ) || text[i] == '_') )
						throw new ScriptCompileException( "malformed number", line, col );
					long frac = long.Parse( text.AsSpan( fracStart, i - fracStart ), NumberStyles.None, CultureInfo.InvariantCulture );
					if ( i - fracStart == 1 )
						frac *= 10;
					if ( whole > (long.MaxValue - frac) / 100 )
						throw new ScriptCompileException( "fixed-point literal too large", line, col );
					Add( Tok.Fixed, "", whole * 100 + frac, start );
				}
				else
				{
					Add( Tok.Int, "", whole, start );
				}
				continue;
			}

			if ( c == '"' || c == '\'' )
			{
				int start = i++;
				var builder = new StringBuilder();
				while ( true )
				{
					if ( i >= text.Length || text[i] == '\n' )
						throw new ScriptCompileException( "unterminated string", line, col );
					var ch = text[i++];
					if ( ch == c )
						break;
					if ( ch == '\\' )
					{
						if ( i >= text.Length )
							throw new ScriptCompileException( "unterminated string", line, col );
						var escape = text[i++];
						ch = escape switch
						{
							'n' => '\n', 't' => '\t', 'r' => '\r', '\\' => '\\', '"' => '"', '\'' => '\'',
							_ => throw new ScriptCompileException( $"unknown escape '\\{escape}'", line, i - lineStart )
						};
					}
					builder.Append( ch );
					if ( builder.Length > ScriptLimits.MaxStringLength )
						throw new ScriptCompileException( $"string literal too long (limit {ScriptLimits.MaxStringLength})", line, col );
				}
				var value = builder.ToString();
				try { ScriptState.RequireString( value ); value = ScriptState.Nfc( value ); }
				catch ( ScriptStateException e ) { throw new ScriptCompileException( e.Message, line, col ); }
				Add( Tok.String, value, 0, start );
				continue;
			}

			int begin = i;
			Tok? kind = c switch
			{
				'+' => Tok.Plus, '-' => Tok.Minus, '*' => Tok.Star, '%' => Tok.Percent,
				'(' => Tok.LParen, ')' => Tok.RParen, '{' => Tok.LBrace, '}' => Tok.RBrace, '[' => Tok.LBracket, ']' => Tok.RBracket,
				',' => Tok.Comma, ';' => Tok.Semicolon, _ => null
			};
			if ( kind != null )
			{
				i++;
			}
			else
			{
				var next = i + 1 < text.Length ? text[i + 1] : '\0';
				switch ( c )
				{
					case '/' when next == '/': kind = Tok.SlashSlash; i += 2; break;
					case '/': throw new ScriptCompileException( "'/' is not supported; use '//' (integer division, truncating toward zero) or fxdiv", line, col );
					case '=' when next == '=': kind = Tok.EqEq; i += 2; break;
					case '=': kind = Tok.Assign; i++; break;
					case '~' when next == '=': kind = Tok.TildeEq; i += 2; break;
					case '<' when next == '=': kind = Tok.Le; i += 2; break;
					case '<': kind = Tok.Lt; i++; break;
					case '>' when next == '=': kind = Tok.Ge; i += 2; break;
					case '>': kind = Tok.Gt; i++; break;
					case '.' when next == '.':
						if ( i + 2 < text.Length && text[i + 2] == '.' )
							throw new ScriptCompileException( "'...' (varargs) is not supported", line, col );
						kind = Tok.DotDot; i += 2; break;
					case '.': kind = Tok.Dot; i++; break;
					case '#': throw new ScriptCompileException( "'#' is not supported; use len(x)", line, col );
					case ':': throw new ScriptCompileException( "method calls and ':' are not supported", line, col );
					default: throw new ScriptCompileException( $"unexpected character '{c}'", line, col );
				}
			}
			Add( kind!.Value, "", 0, begin );
		}

		tokens.Add( new Token( Tok.End, "", 0, line, text.Length - lineStart + 1 ) );
		return tokens;
	}

	private static bool IsAsciiLetter( char c ) => c is >= 'a' and <= 'z' or >= 'A' and <= 'Z';
	private static bool IsDigit( char c ) => c is >= '0' and <= '9';
}

using System.Globalization;
using System.Text;

namespace OpenTPW.Scripting;

/// <summary>
/// The <c>state</c> schema (docs/SCRIPT-MODS.md): the root is a map; values are integers, strings (NFC), booleans, arrays and string-keyed
/// maps. No null, no cycles, no host views. Limits on depth, node count, key length and serialised bytes. The canonical serialisation is
/// compact JSON with ordinal-sorted keys, so equal states give equal bytes on every platform. This phase has the codec and the validator;
/// wiring them into park saves comes with the hooks.
/// </summary>
public static class ScriptState
{
	/// <summary>An empty state (an empty map).</summary>
	public static ScriptTable Empty() => new( null );

	/// <summary>Checks the whole schema (including the byte limit) of <paramref name="root"/>. Throws <see cref="ScriptStateException"/>.</summary>
	public static void Validate( ScriptTable root ) => Check( root, out _, out _ );

	/// <summary>The canonical serialisation of a valid state. Does not validate; use <see cref="Validate"/> first.</summary>
	public static string Serialize( ScriptTable root )
	{
		var builder = new StringBuilder();
		Write( builder, root, 0 );
		return builder.ToString();
	}

	/// <summary>Parses and validates a canonical or hand-written JSON state. Throws <see cref="ScriptStateException"/> for anything outside the schema.</summary>
	public static ScriptTable Parse( string json )
	{
		if ( Encoding.UTF8.GetByteCount( json ) > ScriptLimits.MaxStateBytes )
			throw new ScriptStateException( $"state is larger than {ScriptLimits.MaxStateBytes} bytes" );
		var parser = new StateParser( json );
		var root = parser.ParseRoot();
		Validate( root );
		return root;
	}

	internal static string Quote( string value )
	{
		var builder = new StringBuilder();
		WriteString( builder, value, 0 );
		return builder.ToString();
	}

	/// <summary>
	/// Strings are opaque UTF-16 text: compared ordinally, never normalised, so no operation can make one longer than it was.
	/// A string is at most <see cref="ScriptLimits.MaxStringLength"/> characters and has no unpaired surrogate (it must survive UTF-8).
	/// </summary>
	internal static void RequireString( string value )
	{
		if ( value.Length > ScriptLimits.MaxStringLength )
			throw new ScriptStateException( $"string longer than {ScriptLimits.MaxStringLength} characters" );
		for ( int i = 0; i < value.Length; i++ )
		{
			if ( char.IsHighSurrogate( value[i] ) && i + 1 < value.Length && char.IsLowSurrogate( value[i + 1] ) )
				i++;
			else if ( char.IsSurrogate( value[i] ) )
				throw new ScriptStateException( "string contains an unpaired surrogate" );
		}
	}

	/// <summary>A map key: 1 to 64 printable ASCII characters (0x20 to 0x7E).</summary>
	internal static bool IsValidKey( string key )
	{
		if ( key.Length == 0 || key.Length > ScriptLimits.MaxStateKeyLength )
			return false;
		foreach ( var c in key )
			if ( c < ' ' || c > '~' )
				return false;
		return true;
	}

	internal static void RequireKey( string key )
	{
		if ( !IsValidKey( key ) )
			throw new ArgumentException( $"A key must be 1 to {ScriptLimits.MaxStateKeyLength} printable ASCII characters." );
	}

	/// <summary>
	/// Validates the tree and reports its size (nodes visited and characters of the canonical form) so a commit can charge it to the call.
	/// The work is bounded by the limits themselves: a table may appear only once (no shared subtrees, so no expansion and no cycles), the
	/// walk stops at the node limit, and the serialisation stops as soon as it passes the byte limit.
	/// </summary>
	internal static void Check( ScriptTable root, out int nodes, out int characters )
	{
		if ( root.ReadOnly )
			throw new ScriptStateException( "state cannot be a host view" );
		if ( root.Kind == ScriptTableKind.Array )
			throw new ScriptStateException( "the root of state must be a map" );
		var count = 0;
		var seen = new HashSet<ScriptTable>( ReferenceEqualityComparer.Instance );
		CheckTable( root, 1, ref count, seen );
		var builder = new StringBuilder();
		Write( builder, root, ScriptLimits.MaxStateBytes );
		if ( Encoding.UTF8.GetByteCount( builder.ToString() ) > ScriptLimits.MaxStateBytes )
			throw new ScriptStateException( $"state is larger than {ScriptLimits.MaxStateBytes} bytes" );
		nodes = count;
		characters = builder.Length;
	}

	private static void CheckTable( ScriptTable table, int depth, ref int nodes, HashSet<ScriptTable> seen )
	{
		if ( depth > ScriptLimits.MaxStateDepth )
			throw new ScriptStateException( $"state is nested deeper than {ScriptLimits.MaxStateDepth} levels" );
		CountNode( ref nodes );
		if ( table.ReadOnly )
			throw new ScriptStateException( "state cannot hold a host view" );
		if ( !seen.Add( table ) )
			throw new ScriptStateException( "a table appears more than once in state (shared and cyclic tables are not allowed; copy it instead)" );
		if ( table.Items != null )
		{
			foreach ( var value in table.Items )
				CheckValue( value, depth, ref nodes, seen );
		}
		else if ( table.Entries != null )
		{
			foreach ( var (key, value) in table.Entries )
			{
				if ( !IsValidKey( key ) )
					throw new ScriptStateException( $"a key must be 1 to {ScriptLimits.MaxStateKeyLength} printable ASCII characters" );
				CheckValue( value, depth, ref nodes, seen );
			}
		}
	}

	private static void CountNode( ref int nodes )
	{
		if ( ++nodes > ScriptLimits.MaxStateNodes )
			throw new ScriptStateException( $"state has more than {ScriptLimits.MaxStateNodes} values" );
	}

	private static void CheckValue( ScriptValue value, int depth, ref int nodes, HashSet<ScriptTable> seen )
	{
		switch ( value.Kind )
		{
			case ScriptKind.Int:
			case ScriptKind.Bool:
				CountNode( ref nodes );
				break;
			case ScriptKind.String:
				CountNode( ref nodes );
				RequireString( value.RawString );
				break;
			case ScriptKind.Table:
				CheckTable( value.RawTable, depth + 1, ref nodes, seen );
				break;
			default:
				throw new ScriptStateException( "state cannot hold a missing value" );
		}
	}

	// ----- canonical serialisation -----

	private static void Write( StringBuilder builder, ScriptTable table, int limit )
	{
		switch ( table.Kind )
		{
			case ScriptTableKind.Array:
				builder.Append( '[' );
				for ( int i = 0; i < table.Items!.Count; i++ )
				{
					if ( i > 0 )
						builder.Append( ',' );
					WriteValue( builder, table.Items[i], limit );
				}
				builder.Append( ']' );
				break;
			case ScriptTableKind.Map:
				builder.Append( '{' );
				var first = true;
				foreach ( var key in table.SortedKeys() )
				{
					if ( !first )
						builder.Append( ',' );
					first = false;
					WriteString( builder, key, limit );
					builder.Append( ':' );
					WriteValue( builder, table.Entries![key], limit );
				}
				builder.Append( '}' );
				break;
			default:
				builder.Append( "{}" );
				break;
		}
	}

	private static void WriteValue( StringBuilder builder, ScriptValue value, int limit )
	{
		switch ( value.Kind )
		{
			case ScriptKind.Int: builder.Append( value.RawInt.ToString( CultureInfo.InvariantCulture ) ); break;
			case ScriptKind.Bool: builder.Append( value.AsBool ? "true" : "false" ); break;
			case ScriptKind.String: WriteString( builder, value.RawString, limit ); break;
			case ScriptKind.Table: Write( builder, value.RawTable, limit ); break;
			default: throw new ScriptStateException( "state cannot hold a missing value" );
		}
	}

	private static void WriteString( StringBuilder builder, string value, int limit )
	{
		builder.Append( '"' );
		foreach ( var c in value )
		{
			switch ( c )
			{
				case '"': builder.Append( "\\\"" ); break;
				case '\\': builder.Append( "\\\\" ); break;
				case < ' ':
				case '\u007f':
					builder.Append( "\\u" ).Append( ((int)c).ToString( "x4", CultureInfo.InvariantCulture ) );
					break;
				default: builder.Append( c ); break;
			}
		}
		builder.Append( '"' );
		if ( limit > 0 && builder.Length > limit )
			throw new ScriptStateException( $"state is larger than {ScriptLimits.MaxStateBytes} bytes" );
	}

	// ----- parsing -----

	private sealed class StateParser
	{
		private readonly string text;
		private int pos;

		public StateParser( string text ) { this.text = text; }

		public ScriptTable ParseRoot()
		{
			SkipSpace();
			if ( pos >= text.Length || text[pos] != '{' )
				throw Fail( "the root of state must be a map" );
			var root = ParseTable( 1 );
			SkipSpace();
			if ( pos != text.Length )
				throw Fail( "unexpected text after the state" );
			return root;
		}

		private ScriptStateException Fail( string message ) => new( $"state JSON, position {pos}: {message}" );

		private void SkipSpace()
		{
			while ( pos < text.Length && text[pos] is ' ' or '\t' or '\r' or '\n' )
				pos++;
		}

		private ScriptTable ParseTable( int depth )
		{
			if ( depth > ScriptLimits.MaxStateDepth )
				throw Fail( "nested too deeply" );
			var table = new ScriptTable( null );
			var open = text[pos++];
			var close = open == '[' ? ']' : '}';
			SkipSpace();
			if ( pos < text.Length && text[pos] == close )
			{
				pos++;
				table.Kind = open == '[' ? ScriptTableKind.Array : ScriptTableKind.Empty;
				if ( open == '[' )
					table.Items = new();
				return table;
			}

			if ( open == '[' )
			{
				table.Kind = ScriptTableKind.Array;
				table.Items = new();
			}
			else
			{
				table.Kind = ScriptTableKind.Map;
				table.Entries = new( StringComparer.Ordinal );
			}

			while ( true )
			{
				SkipSpace();
				string? key = null;
				if ( open == '{' )
				{
					key = ParseString();
					if ( !IsValidKey( key ) )
						throw Fail( "a key must be 1 to 64 printable ASCII characters" );
					SkipSpace();
					if ( pos >= text.Length || text[pos++] != ':' )
						throw Fail( "expected ':'" );
					SkipSpace();
				}
				var value = ParseValue( depth );
				if ( key != null )
				{
					if ( !table.Entries!.TryAdd( key, value ) )
						throw Fail( $"duplicate key '{key}'" );
				}
				else
				{
					table.Items!.Add( value );
				}
				if ( table.Count > ScriptLimits.MaxStateNodes )
					throw Fail( "too many values" );
				SkipSpace();
				if ( pos >= text.Length )
					throw Fail( "unexpected end" );
				var c = text[pos++];
				if ( c == close )
					return table;
				if ( c != ',' )
					throw Fail( $"expected ',' or '{close}'" );
			}
		}

		private ScriptValue ParseValue( int depth )
		{
			if ( pos >= text.Length )
				throw Fail( "unexpected end" );
			var c = text[pos];
			if ( c is '{' or '[' )
				return ScriptValue.FromTable( ParseTable( depth + 1 ) );
			if ( c == '"' )
				return ScriptValue.FromString( ParseString() );
			if ( string.CompareOrdinal( text, pos, "true", 0, 4 ) == 0 ) { pos += 4; return true; }
			if ( string.CompareOrdinal( text, pos, "false", 0, 5 ) == 0 ) { pos += 5; return false; }
			if ( c == '-' || (c >= '0' && c <= '9') )
			{
				var start = pos++;
				while ( pos < text.Length && text[pos] is >= '0' and <= '9' )
					pos++;
				var span = text.AsSpan( start, pos - start );
				var digits = span.Length > 0 && span[0] == '-' ? span[1..] : span;
				if ( digits.Length == 0 || (digits.Length > 1 && digits[0] == '0') || span.SequenceEqual( "-0" ) )
					throw Fail( "malformed integer" );
				if ( pos < text.Length && text[pos] is '.' or 'e' or 'E' )
					throw Fail( "state holds integers only" );
				if ( !long.TryParse( span, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var number ) )
					throw Fail( "integer out of range" );
				return number;
			}
			throw Fail( "unexpected value (null and floats are not part of the schema)" );
		}

		private string ParseString()
		{
			if ( pos >= text.Length || text[pos] != '"' )
				throw Fail( "expected a string" );
			pos++;
			var builder = new StringBuilder();
			while ( true )
			{
				if ( pos >= text.Length )
					throw Fail( "unterminated string" );
				var c = text[pos++];
				if ( c == '"' )
					break;
				if ( c < ' ' )
					throw Fail( "control character in string" );
				if ( c == '\\' )
				{
					if ( pos >= text.Length )
						throw Fail( "unterminated string" );
					var e = text[pos++];
					switch ( e )
					{
						case '"': builder.Append( '"' ); break;
						case '\\': builder.Append( '\\' ); break;
						case '/': builder.Append( '/' ); break;
						case 'n': builder.Append( '\n' ); break;
						case 't': builder.Append( '\t' ); break;
						case 'r': builder.Append( '\r' ); break;
						case 'b': builder.Append( '\b' ); break;
						case 'f': builder.Append( '\f' ); break;
						case 'u':
							if ( pos + 4 > text.Length || !int.TryParse( text.AsSpan( pos, 4 ), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var code ) )
								throw Fail( "malformed \\u escape" );
							pos += 4;
							builder.Append( (char)code );
							break;
						default: throw Fail( "unknown escape" );
					}
				}
				else
				{
					builder.Append( c );
				}
				if ( builder.Length > ScriptLimits.MaxStringLength )
					throw Fail( "string too long" );
			}
			return builder.ToString();
		}
	}
}

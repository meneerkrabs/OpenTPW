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
	public static void Validate( ScriptTable root ) => Check( root, null, false );

	/// <summary>The canonical serialisation of a valid state. Does not validate; use <see cref="Validate"/> first.</summary>
	public static string Serialize( ScriptTable root )
	{
		var builder = new StringBuilder();
		Write( builder, root );
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
		WriteString( builder, value );
		return builder.ToString();
	}

	internal static string Nfc( string value )
	{
		foreach ( var c in value )
			if ( c >= 0x80 )
				return value.Normalize( NormalizationForm.FormC );
		return value;
	}

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

	internal static void RequireKey( string key )
	{
		RequireString( key );
		if ( key.Length == 0 || key.Length > ScriptLimits.MaxStateKeyLength )
			throw new ArgumentException( $"A key must be 1 to {ScriptLimits.MaxStateKeyLength} characters." );
		if ( Nfc( key ) != key )
			throw new ArgumentException( "A key must be NFC-normalised." );
	}

	/// <summary>
	/// Validates the tree. With <paramref name="owner"/> set (a commit), the strings of tables owned by that call are normalised to NFC in place;
	/// shared tables were normalised when they were committed. Otherwise any non-NFC string is an error.
	/// </summary>
	internal static void Check( ScriptTable root, object? owner, bool normalise )
	{
		if ( root.ReadOnly )
			throw new ScriptStateException( "state cannot be a host view" );
		if ( root.Kind == ScriptTableKind.Array )
			throw new ScriptStateException( "the root of state must be a map" );
		var nodes = 0;
		CheckTable( root, 1, ref nodes, owner, normalise );
		var builder = new StringBuilder();
		Write( builder, root );
		var bytes = Encoding.UTF8.GetByteCount( builder.ToString() );
		if ( bytes > ScriptLimits.MaxStateBytes )
			throw new ScriptStateException( $"state is larger than {ScriptLimits.MaxStateBytes} bytes" );
	}

	private static void CheckTable( ScriptTable table, int depth, ref int nodes, object? owner, bool normalise )
	{
		if ( depth > ScriptLimits.MaxStateDepth )
			throw new ScriptStateException( $"state is nested deeper than {ScriptLimits.MaxStateDepth} levels (or contains a cycle)" );
		if ( ++nodes > ScriptLimits.MaxStateNodes )
			throw new ScriptStateException( $"state has more than {ScriptLimits.MaxStateNodes} values" );
		if ( table.ReadOnly )
			throw new ScriptStateException( "state cannot hold a host view" );
		var mine = normalise && ReferenceEquals( table.Owner, owner );
		if ( table.Items != null )
		{
			for ( int i = 0; i < table.Items.Count; i++ )
			{
				CheckValue( table.Items[i], depth, ref nodes, owner, normalise, mine, out var replaced );
				if ( mine && replaced != null )
					table.Items[i] = replaced.Value;
			}
		}
		else if ( table.Entries != null )
		{
			List<(string Key, ScriptValue Value)>? fixes = null;
			foreach ( var (key, value) in table.Entries )
			{
				if ( key.Length == 0 || key.Length > ScriptLimits.MaxStateKeyLength )
					throw new ScriptStateException( $"a key must be 1 to {ScriptLimits.MaxStateKeyLength} characters" );
				RequireString( key );
				if ( Nfc( key ) != key )
					throw new ScriptStateException( "a key is not NFC-normalised" );
				CheckValue( value, depth, ref nodes, owner, normalise, mine, out var replaced );
				if ( mine && replaced != null )
					(fixes ??= new()).Add( (key, replaced.Value) );
			}
			if ( fixes != null )
				foreach ( var (key, value) in fixes )
					table.Entries[key] = value;
		}
	}

	private static void CheckValue( ScriptValue value, int depth, ref int nodes, object? owner, bool normalise, bool mine, out ScriptValue? replaced )
	{
		replaced = null;
		switch ( value.Kind )
		{
			case ScriptKind.Int:
			case ScriptKind.Bool:
				if ( ++nodes > ScriptLimits.MaxStateNodes )
					throw new ScriptStateException( $"state has more than {ScriptLimits.MaxStateNodes} values" );
				break;
			case ScriptKind.String:
				if ( ++nodes > ScriptLimits.MaxStateNodes )
					throw new ScriptStateException( $"state has more than {ScriptLimits.MaxStateNodes} values" );
				var text = value.RawString;
				RequireString( text );
				var normal = Nfc( text );
				if ( normal != text )
				{
					if ( !mine )
						throw new ScriptStateException( "a string is not NFC-normalised" );
					if ( normal.Length > ScriptLimits.MaxStringLength )
						throw new ScriptStateException( $"string longer than {ScriptLimits.MaxStringLength} characters" );
					replaced = ScriptValue.FromString( normal );
				}
				break;
			case ScriptKind.Table:
				CheckTable( value.RawTable, depth + 1, ref nodes, owner, normalise );
				break;
			default:
				throw new ScriptStateException( "state cannot hold a missing value" );
		}
	}

	// ----- canonical serialisation -----

	private static void Write( StringBuilder builder, ScriptTable table )
	{
		switch ( table.Kind )
		{
			case ScriptTableKind.Array:
				builder.Append( '[' );
				for ( int i = 0; i < table.Items!.Count; i++ )
				{
					if ( i > 0 )
						builder.Append( ',' );
					WriteValue( builder, table.Items[i] );
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
					WriteString( builder, key );
					builder.Append( ':' );
					WriteValue( builder, table.Entries![key] );
				}
				builder.Append( '}' );
				break;
			default:
				builder.Append( "{}" );
				break;
		}
	}

	private static void WriteValue( StringBuilder builder, ScriptValue value )
	{
		switch ( value.Kind )
		{
			case ScriptKind.Int: builder.Append( value.RawInt.ToString( CultureInfo.InvariantCulture ) ); break;
			case ScriptKind.Bool: builder.Append( value.AsBool ? "true" : "false" ); break;
			case ScriptKind.String: WriteString( builder, value.RawString ); break;
			case ScriptKind.Table: Write( builder, value.RawTable ); break;
			default: throw new ScriptStateException( "state cannot hold a missing value" );
		}
	}

	private static void WriteString( StringBuilder builder, string value )
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

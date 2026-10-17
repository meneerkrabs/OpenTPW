using System.Diagnostics.CodeAnalysis;

namespace OpenTPW.Scripting;

public enum ScriptKind : byte
{
	/// <summary>"No value": what a function without a result returns. It cannot be stored or used; there is no nil.</summary>
	Unit,
	Int,
	Bool,
	String,
	Table
}

/// <summary>A TPW Script value: a 64-bit integer, a bool, an immutable string or a table. Tables are plain data (see <see cref="ScriptTable"/>).</summary>
public readonly struct ScriptValue
{
	public ScriptKind Kind { get; }
	private readonly long bits;
	private readonly object? reference;

	private ScriptValue( ScriptKind kind, long bits, object? reference )
	{
		Kind = kind;
		this.bits = bits;
		this.reference = reference;
	}

	public static ScriptValue Unit => default;
	public static ScriptValue FromInt( long value ) => new( ScriptKind.Int, value, null );
	public static ScriptValue FromBool( bool value ) => new( ScriptKind.Bool, value ? 1 : 0, null );
	public static ScriptValue FromString( string value ) => new( ScriptKind.String, 0, value ?? throw new ArgumentNullException( nameof( value ) ) );
	public static ScriptValue FromTable( ScriptTable value ) => new( ScriptKind.Table, 0, value ?? throw new ArgumentNullException( nameof( value ) ) );

	public static implicit operator ScriptValue( long value ) => FromInt( value );
	public static implicit operator ScriptValue( bool value ) => FromBool( value );
	public static implicit operator ScriptValue( string value ) => FromString( value );
	public static implicit operator ScriptValue( ScriptTable value ) => FromTable( value );

	public long AsInt => Kind == ScriptKind.Int ? bits : throw new InvalidOperationException( "Not an int." );
	public bool AsBool => Kind == ScriptKind.Bool ? bits != 0 : throw new InvalidOperationException( "Not a bool." );
	public string AsString => Kind == ScriptKind.String ? (string)reference! : throw new InvalidOperationException( "Not a string." );
	public ScriptTable AsTable => Kind == ScriptKind.Table ? (ScriptTable)reference! : throw new InvalidOperationException( "Not a table." );

	internal long RawInt => bits;
	internal string RawString => (string)reference!;
	internal ScriptTable RawTable => (ScriptTable)reference!;

	/// <summary>Invariant, culture-free text for diagnostics and golden hashes (not a script-visible conversion).</summary>
	public override string ToString() => Kind switch
	{
		ScriptKind.Unit => "unit",
		ScriptKind.Int => bits.ToString( System.Globalization.CultureInfo.InvariantCulture ),
		ScriptKind.Bool => bits != 0 ? "true" : "false",
		ScriptKind.String => ScriptState.Quote( (string)reference! ),
		_ => ScriptState.Serialize( (ScriptTable)reference! )
	};

	internal static string KindName( ScriptKind kind ) => kind switch
	{
		ScriptKind.Unit => "no value",
		ScriptKind.Int => "int",
		ScriptKind.Bool => "bool",
		ScriptKind.String => "string",
		_ => "table"
	};
}

internal enum ScriptTableKind : byte
{
	/// <summary>No entries yet: becomes an array on the first integer key (1) and a map on the first string key.</summary>
	Empty,
	Array,
	Map
}

/// <summary>
/// A table: either a 1-based array of values or a map with string keys, never both. Arrays and maps may nest. There is no nil, so a key
/// is either present or absent (reading an absent key is a runtime error; <c>has</c> asks). Host views are read-only tables.
/// </summary>
public sealed class ScriptTable
{
	internal ScriptTableKind Kind;
	internal List<ScriptValue>? Items;
	internal Dictionary<string, ScriptValue>? Entries;
	internal object? Owner;

	/// <summary>True for host views: scripts can read them but not change them, nor store them in <c>state</c>.</summary>
	public bool ReadOnly { get; private set; }

	internal ScriptTable( object? owner ) { Owner = owner; }

	public int Count => Kind switch { ScriptTableKind.Array => Items!.Count, ScriptTableKind.Map => Entries!.Count, _ => 0 };

	public bool IsArray => Kind == ScriptTableKind.Array;
	public bool IsMap => Kind == ScriptTableKind.Map;

	/// <summary>A read-only array view for a hook argument. Nested tables must be read-only too.</summary>
	public static ScriptTable ArrayView( IEnumerable<ScriptValue> values )
	{
		var table = new ScriptTable( null ) { ReadOnly = true };
		foreach ( var value in values )
		{
			RequireView( value );
			(table.Items ??= new()).Add( value );
		}
		if ( table.Items != null )
			table.Kind = ScriptTableKind.Array;
		return table;
	}

	/// <summary>A read-only record (string-keyed map) view for a hook argument. Nested tables must be read-only too.</summary>
	public static ScriptTable RecordView( IEnumerable<KeyValuePair<string, ScriptValue>> fields )
	{
		var table = new ScriptTable( null ) { ReadOnly = true };
		foreach ( var (key, value) in fields )
		{
			ScriptState.RequireKey( key );
			RequireView( value );
			if ( !(table.Entries ??= new( StringComparer.Ordinal )).TryAdd( key, value ) )
				throw new ArgumentException( $"Duplicate field '{key}'." );
		}
		if ( table.Entries != null )
			table.Kind = ScriptTableKind.Map;
		return table;
	}

	private static void RequireView( ScriptValue value )
	{
		if ( value.Kind == ScriptKind.Unit )
			throw new ArgumentException( "A view cannot hold a missing value." );
		if ( value.Kind == ScriptKind.String )
			ScriptState.RequireString( value.RawString );
		if ( value.Kind == ScriptKind.Table && !value.RawTable.ReadOnly )
			throw new ArgumentException( "A nested table of a view must be a read-only view." );
	}

	/// <summary>Reads an array element (1-based) for host code and tests.</summary>
	public ScriptValue this[int index] => Kind == ScriptTableKind.Array && index >= 1 && index <= Items!.Count ? Items[index - 1] : throw new KeyNotFoundException();

	public bool TryGet( string key, out ScriptValue value )
	{
		value = default;
		return Kind == ScriptTableKind.Map && Entries!.TryGetValue( key, out value );
	}

	/// <summary>The keys of a map in ordinal order.</summary>
	public IReadOnlyList<string> SortedKeys()
	{
		if ( Kind != ScriptTableKind.Map )
			return Array.Empty<string>();
		var keys = new List<string>( Entries!.Keys );
		keys.Sort( StringComparer.Ordinal );
		return keys;
	}

	/// <summary>A shallow copy owned by <paramref name="owner"/>: children stay shared and are copied lazily when a script reads them.</summary>
	internal ScriptTable CloneShallow( object owner )
	{
		var copy = new ScriptTable( owner ) { Kind = Kind };
		if ( Items != null )
			copy.Items = new List<ScriptValue>( Items );
		if ( Entries != null )
			copy.Entries = new Dictionary<string, ScriptValue>( Entries, StringComparer.Ordinal );
		return copy;
	}

	internal bool TryGetEntry( string key, [MaybeNullWhen( false )] out ScriptValue value )
	{
		if ( Kind == ScriptTableKind.Map && Entries!.TryGetValue( key, out value ) )
			return true;
		value = default;
		return false;
	}
}

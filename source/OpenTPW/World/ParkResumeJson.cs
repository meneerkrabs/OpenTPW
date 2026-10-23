using System.Text.Json;
using System.Text.Json.Serialization;

namespace OpenTPW;

/// <summary>The own-format snapshot codec. Tuple fields in navigation and queue DTOs must survive the file roundtrip.</summary>
internal static class ParkResumeJson
{
	private static readonly JsonSerializerOptions options = new()
	{
		IncludeFields = true,
		MaxDepth = 32,
		UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
	};

	/// <summary>[EXT:periodic-autosave] The same bytes as <see cref="Serialize"/>, produced in time-boxed slices (<see cref="SlicedJsonSerializer"/>).</summary>
	public static SlicedJsonSerializer Slice<T>( T snapshot, Action? first = null ) where T : notnull => new( snapshot, typeof( T ), options, first );

	/// <summary>
	/// Makes the codec's reflection metadata (the serializer's type information for every type the snapshot reaches) in steps, one type each, deepest first. The first
	/// serialization or load of a session does this in one go, which takes about 0.4 s in the browser's AOT build (docs/BROWSER-PERFORMANCE.md, "Event hitches"), so a caller
	/// that knows a save is coming (a park about to be entered, whose restart snapshot is its first) runs these steps between frames. Nothing is written and the codec's
	/// output does not change; a type the serializer cannot describe is left for the real call to report.
	/// </summary>
	internal static IEnumerable<bool> WarmupSteps()
	{
		if ( !options.IsReadOnly )
			options.MakeReadOnly( populateMissingResolver: true );
		var seen = new HashSet<Type>();
		foreach ( var step in Warm( typeof( ParkResumeSnapshot ), seen ) )
			yield return step;
	}

	private static IEnumerable<bool> Warm( Type type, HashSet<Type> seen )
	{
		if ( !seen.Add( type ) )
			yield break;
		foreach ( var child in ChildTypes( type ) )
			foreach ( var step in Warm( child, seen ) )
				yield return step;
		try { options.GetTypeInfo( type ); }
		catch ( Exception exception ) when ( exception is NotSupportedException or InvalidOperationException or ArgumentException ) { }
		yield return true;
	}

	/// <summary>The types a type's serialization reaches: its generic arguments, its element type and, for the game's own types, its public properties and fields.</summary>
	private static IEnumerable<Type> ChildTypes( Type type )
	{
		if ( type.IsPrimitive || type.IsEnum || type == typeof( string ) || type.IsPointer || type.IsByRef )
			yield break;
		if ( type.HasElementType && type.GetElementType() is { } element )
			yield return element;
		if ( type.IsGenericType )
			foreach ( var argument in type.GetGenericArguments() )
				yield return argument;
		if ( type.Namespace?.StartsWith( "OpenTPW", StringComparison.Ordinal ) != true )
			yield break;
		foreach ( var property in type.GetProperties( System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance ) )
			if ( property.GetIndexParameters().Length == 0 )
				yield return property.PropertyType;
		foreach ( var field in type.GetFields( System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance ) )
			yield return field.FieldType;
	}

	public static byte[] Serialize<T>( T snapshot ) => JsonSerializer.SerializeToUtf8Bytes( snapshot, options );

	public static T Deserialize<T>( ReadOnlySpan<byte> bytes ) =>
		JsonSerializer.Deserialize<T>( bytes, options ) ?? throw new InvalidDataException( "The park snapshot is null." );

	/// <summary><see cref="Deserialize"/> over steps, a slice of the bytes each (<see cref="SlicedJsonDeserializer{T}"/>): the same value and failures.</summary>
	internal static SlicedJsonDeserializer<T> SliceDeserialize<T>( ReadOnlyMemory<byte> bytes, int slice = SlicedJsonDeserializer.SliceBytes )
	{
		// options that have parsed nothing yet have no resolver: the serializer's own default is used, as on first use
		if ( !options.IsReadOnly )
			options.MakeReadOnly( populateMissingResolver: true );
		return new SlicedJsonDeserializer<T>( bytes, options, slice );
	}
}

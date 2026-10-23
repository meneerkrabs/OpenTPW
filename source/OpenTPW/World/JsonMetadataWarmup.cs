using System.Reflection;
using System.Text.Json;

namespace OpenTPW;

/// <summary>
/// Makes a serializer's reflection metadata (its type information for every type a value of a root type reaches) in steps, one type each, deepest first. The first serialization or
/// parse with a set of options makes it in one go, which takes a page's frame in the browser's AOT build (docs/BROWSER-PERFORMANCE.md, "Event hitches"), so a caller that knows
/// such a call is coming runs these steps between frames. Nothing is written and no output changes; a type the serializer cannot describe is left for the real call to report.
/// </summary>
internal static class JsonMetadataWarmup
{
	internal static IEnumerable<bool> Steps( JsonSerializerOptions options, Type root )
	{
		ArgumentNullException.ThrowIfNull( options );
		ArgumentNullException.ThrowIfNull( root );
		if ( !options.IsReadOnly )
			options.MakeReadOnly( populateMissingResolver: true );
		foreach ( var step in Warm( options, root, new HashSet<Type>() ) )
			yield return step;
	}

	private static IEnumerable<bool> Warm( JsonSerializerOptions options, Type type, HashSet<Type> seen )
	{
		if ( !seen.Add( type ) )
			yield break;
		foreach ( var child in ChildTypes( type, options.IncludeFields ) )
			foreach ( var step in Warm( options, child, seen ) )
				yield return step;
		try { options.GetTypeInfo( type ); }
		catch ( Exception exception ) when ( exception is NotSupportedException or InvalidOperationException or ArgumentException ) { }
		yield return true;
	}

	/// <summary>
	/// The types a type's serialization reaches: its generic arguments, its element type and, for the game's own types, its public properties and, when the options serialize them,
	/// its public fields.
	/// </summary>
	private static IEnumerable<Type> ChildTypes( Type type, bool includeFields )
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
		foreach ( var property in type.GetProperties( BindingFlags.Public | BindingFlags.Instance ) )
			if ( property.GetIndexParameters().Length == 0 )
				yield return property.PropertyType;
		if ( includeFields )
			foreach ( var field in type.GetFields( BindingFlags.Public | BindingFlags.Instance ) )
				yield return field.FieldType;
	}
}

using System.Buffers;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace OpenTPW;

/// <summary>
/// [EXT:periodic-autosave] Serializes a snapshot to the same bytes as <c>JsonSerializer.SerializeToUtf8Bytes</c> in slices of a caller-given time budget, so that a large park's
/// autosave spreads over several frames instead of one long one (the browser has no second thread to hand it to). The top levels of the object graph (the snapshot's sections, the guest
/// and runtime arrays) are walked through the serializer's own contract metadata, one array element or property per step; everything below that depth, and every value that is not an
/// object or a list, is written whole by the serializer itself, so names, order, ignore rules and number formats are the serializer's. It lives with the client code because it reads a wall clock, which simulation code (World) never does. The snapshot is only read: it is already a detached
/// copy of the simulation (capture clones every mutable array), so the park may go on running between slices.
/// </summary>
internal sealed class SlicedJsonSerializer
{
	/// <summary>Object graph levels walked step by step; deeper values are written whole.</summary>
	internal const int SlicedDepth = 3;

	private readonly JsonSerializerOptions options;
	private readonly ArrayBufferWriter<byte> buffer = new( 64 * 1024 );
	private readonly Utf8JsonWriter writer;
	private readonly IEnumerator<bool> steps;

	/// <param name="first">Runs as the first step, before anything is written (a check of the value that must not be written when it fails).</param>
	internal SlicedJsonSerializer( object value, Type type, JsonSerializerOptions options, Action? first = null )
	{
		this.options = options;
		// options that have not serialized anything yet have no resolver: the serializer's own default is used, as it would be on first use
		if ( !options.IsReadOnly ) options.MakeReadOnly( populateMissingResolver: true );
		writer = new Utf8JsonWriter( buffer, new JsonWriterOptions { Encoder = options.Encoder, Indented = options.WriteIndented, SkipValidation = false, MaxDepth = options.MaxDepth } );
		steps = Steps( value, options.GetTypeInfo( type ), first ).GetEnumerator();
	}

	/// <summary>
	/// Serializes an empty string in slices and drops it: the runtime's first-call work of the serializer's own methods (about 6 ms in the browser's
	/// AOT build, the initialization of its constructor) is then done before a park's first restart snapshot needs them in the entering frame
	/// (docs/BROWSER-PERFORMANCE.md, "Build steps"). Nothing is written; a fresh options instance keeps every codec's own settings untouched.
	/// </summary>
	internal static void WarmUp() => new SlicedJsonSerializer( string.Empty, typeof( string ), new JsonSerializerOptions() ).Run( TimeSpan.MaxValue );

	/// <summary>True once <see cref="Bytes"/> is complete.</summary>
	internal bool Done { get; private set; }
	/// <summary>Slices run so far.</summary>
	internal int Slices { get; private set; }

	/// <summary>Runs steps until <paramref name="budget"/> has passed (at least one step), and reports whether the whole value is written.</summary>
	internal bool Run( TimeSpan budget )
	{
		if ( Done ) return true;
		Slices++;
		var started = Stopwatch.GetTimestamp();
		while ( steps.MoveNext() )
			if ( Stopwatch.GetElapsedTime( started ) >= budget )
				return false;
		writer.Flush();
		Done = true;
		return true;
	}

	/// <summary>The complete JSON; only after <see cref="Run"/> returned true.</summary>
	internal byte[] Bytes => WrittenMemory.ToArray();

	/// <summary>The completed immutable buffer, held by this serializer until its detached consumer finishes.</summary>
	internal ReadOnlyMemory<byte> WrittenMemory => Done ? buffer.WrittenMemory : throw new InvalidOperationException( "The serialization is not finished." );

	private IEnumerable<bool> Steps( object value, JsonTypeInfo info, Action? first )
	{
		if ( first != null ) { first(); yield return true; }
		foreach ( var step in Write( value, info, 0 ) ) yield return step;
	}

	private IEnumerable<bool> Write( object? value, JsonTypeInfo info, int depth )
	{
		if ( value == null ) { writer.WriteNullValue(); yield break; }
		if ( depth < SlicedDepth && info.Kind == JsonTypeInfoKind.Object && info.PolymorphismOptions == null )
		{
			writer.WriteStartObject();
			foreach ( var property in info.Properties )
			{
				if ( property.Get == null ) continue;
				var member = property.Get( value );
				if ( property.ShouldSerialize != null && !property.ShouldSerialize( value, member ) ) continue;
				writer.WritePropertyName( property.Name );
				foreach ( var step in Write( member, options.GetTypeInfo( property.PropertyType ), depth + 1 ) ) yield return step;
				yield return true;
			}
			writer.WriteEndObject();
		}
		else if ( depth < SlicedDepth && info.Kind == JsonTypeInfoKind.Enumerable && info.ElementType is { } elementType && value is System.Collections.IEnumerable items )
		{
			var elementInfo = options.GetTypeInfo( elementType );
			writer.WriteStartArray();
			foreach ( var item in items )
			{
				foreach ( var step in Write( item, elementInfo, depth + 1 ) ) yield return step;
				yield return true;
			}
			writer.WriteEndArray();
		}
		else
			System.Text.Json.JsonSerializer.Serialize( writer, value, info );
	}
}

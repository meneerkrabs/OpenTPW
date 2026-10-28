using System.Text.Json;
using System.Text.Json.Serialization;

namespace OpenTPW;

/// <summary>
/// Reads the current tube row and the own-format row emitted by #888. Only the old read/stopped state survives migration:
/// unread rows still restart their full lifetime, as #891 requires. Neither obsolete elapsed time nor queued delivery is restored.
/// The two wire shapes are disjoint; unknown members and a mixture of Read and the legacy timer fields remain errors.
/// </summary>
internal sealed class MessageTubeRowSaveConverter : JsonConverter<MessageTubeRowSave>
{
	private static readonly string[] LegacyFields = ["ElapsedMs", "Active", "PreExpiryEnabled", "PreExpiryReached", "PendingPreExpiry", "PendingExpiry"];

	public override MessageTubeRowSave Read( ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options )
	{
		using var document = JsonDocument.ParseValue( ref reader );
		var value = document.RootElement;
		if ( value.ValueKind != JsonValueKind.Object )
			throw new JsonException( "A message tube row must be an object." );
		if ( LegacyFields.Any( name => value.TryGetProperty( name, out _ ) ) )
		{
			var legacy = value.Deserialize<LegacyRow>( options ) ?? throw new JsonException( "A message tube row is missing." );
			return legacy.ToCurrent();
		}
		var current = value.Deserialize<CurrentRow>( options ) ?? throw new JsonException( "A message tube row is missing." );
		return new( current.Kind, current.Text, current.Tag, current.ContextShort, current.ContextInt, current.Lifetime, current.Read );
	}

	public override void Write( Utf8JsonWriter writer, MessageTubeRowSave value, JsonSerializerOptions options )
	{
		// Keep #891's exact field order and shape, including false Read; never emit the obsolete fields again.
		writer.WriteStartObject();
		writer.WriteNumber( nameof( value.Kind ), value.Kind );
		writer.WriteString( nameof( value.Text ), value.Text );
		writer.WriteNumber( nameof( value.Tag ), value.Tag );
		writer.WriteNumber( nameof( value.ContextShort ), value.ContextShort );
		writer.WriteNumber( nameof( value.ContextInt ), value.ContextInt );
		writer.WriteNumber( nameof( value.Lifetime ), value.Lifetime );
		writer.WriteBoolean( nameof( value.Read ), value.Read );
		writer.WriteEndObject();
	}

	[JsonUnmappedMemberHandling( JsonUnmappedMemberHandling.Disallow )]
	private sealed record CurrentRow( int Kind, string Text, int Tag, short ContextShort, int ContextInt, int Lifetime, bool Read );

	[JsonUnmappedMemberHandling( JsonUnmappedMemberHandling.Disallow )]
	private sealed class LegacyRow
	{
		public LegacyRow() { }
		// #888 always wrote these ten properties, even for plain rows and default values. The pending fields alone were optional.
		public required int Kind { get; init; }
		public required string Text { get; init; }
		public required int Tag { get; init; }
		public required short ContextShort { get; init; }
		public required int ContextInt { get; init; }
		public required int Lifetime { get; init; }
		public required long ElapsedMs { get; init; }
		public required bool Active { get; init; }
		public required bool PreExpiryEnabled { get; init; }
		public required bool PreExpiryReached { get; init; }
		public bool PendingPreExpiry { get; init; }
		public bool PendingExpiry { get; init; }

		public MessageTubeRowSave ToCurrent()
		{
			var current = new MessageTubeRowSave( Kind, Text, Tag, ContextShort, ContextInt, Lifetime, Kind == MessageTubeRowSave.Terminal && !Active );
			if ( current.Problem() is { } problem )
				throw new JsonException( $"The legacy message tube row is invalid: {problem}." );
			if ( Kind == MessageTubeRowSave.Terminal )
			{
				if ( ElapsedMs < 0 || ElapsedMs > (long)Lifetime + MessageTubeSaveData.MaximumLifetime )
					throw new JsonException( "The legacy terminal tube row's elapsed time is out of range." );
				if ( PendingExpiry && !Active || PendingPreExpiry && (!Active || !PreExpiryEnabled || PreExpiryReached) )
					throw new JsonException( "The legacy terminal tube row has inconsistent pending timers." );
			}
			else if ( ElapsedMs != 0 || Active || PreExpiryEnabled || PreExpiryReached || PendingPreExpiry || PendingExpiry )
				throw new JsonException( "A legacy tube row that is no terminal carries timer state." );
			return current;
		}
	}
}

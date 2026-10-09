using System.Text.Json;
using System.Text.Json.Serialization;

namespace OpenTPW.Online.Packages;

/// <summary>
/// JSON with strict rules for every OpenTPW online document: camelCase, unknown members rejected,
/// duplicate keys rejected, bounded depth, no comments or trailing commas.
/// </summary>
public static class StrictJson
{
	public const int MaximumDepth = 16;

	public static readonly JsonSerializerOptions Options = new()
	{
		PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
		UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
		MaxDepth = MaximumDepth,
		AllowTrailingCommas = false,
		ReadCommentHandling = JsonCommentHandling.Disallow,
		NumberHandling = JsonNumberHandling.Strict,
		DefaultIgnoreCondition = JsonIgnoreCondition.Never,
		Converters = { new JsonStringEnumConverter( JsonNamingPolicy.CamelCase, allowIntegerValues: false ) },
	};

	public static T Deserialize<T>( ReadOnlySpan<byte> utf8, string what ) where T : class
	{
		RejectDuplicateKeys( utf8, what );
		try
		{
			return JsonSerializer.Deserialize<T>( utf8, Options ) ?? throw new InvalidDataException( $"{what} is null." );
		}
		catch ( JsonException exception )
		{
			throw new InvalidDataException( $"{what} is malformed: {exception.Message}", exception );
		}
		catch ( NotSupportedException exception )
		{
			throw new InvalidDataException( $"{what} is malformed: {exception.Message}", exception );
		}
	}

	public static byte[] Serialize<T>( T value ) => JsonSerializer.SerializeToUtf8Bytes( value, Options );

	/// <summary>System.Text.Json keeps the last duplicate; OpenTPW documents must not contain any.</summary>
	public static void RejectDuplicateKeys( ReadOnlySpan<byte> utf8, string what )
	{
		var reader = new Utf8JsonReader( utf8, new JsonReaderOptions { MaxDepth = MaximumDepth, AllowTrailingCommas = false, CommentHandling = JsonCommentHandling.Disallow } );
		var scopes = new Stack<HashSet<string>?>();
		try
		{
			while ( reader.Read() )
			{
				switch ( reader.TokenType )
				{
					case JsonTokenType.StartObject:
						scopes.Push( new HashSet<string>( StringComparer.Ordinal ) );
						break;
					case JsonTokenType.StartArray:
						scopes.Push( null );
						break;
					case JsonTokenType.EndObject:
					case JsonTokenType.EndArray:
						scopes.Pop();
						break;
					case JsonTokenType.PropertyName:
						if ( scopes.Peek() is { } names && !names.Add( reader.GetString()! ) )
							throw new InvalidDataException( $"{what} contains the key '{reader.GetString()}' more than once." );
						break;
				}
			}
		}
		catch ( JsonException exception )
		{
			throw new InvalidDataException( $"{what} is malformed: {exception.Message}", exception );
		}
	}
}

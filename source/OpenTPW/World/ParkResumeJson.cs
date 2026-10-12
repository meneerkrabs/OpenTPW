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

	public static byte[] Serialize<T>( T snapshot ) => JsonSerializer.SerializeToUtf8Bytes( snapshot, options );

	public static T Deserialize<T>( ReadOnlySpan<byte> bytes ) =>
		JsonSerializer.Deserialize<T>( bytes, options ) ?? throw new InvalidDataException( "The park snapshot is null." );
}

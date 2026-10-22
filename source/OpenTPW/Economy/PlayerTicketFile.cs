using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using OpenTPW.Online;

namespace OpenTPW;

/// <summary>Validated, atomic application-owned progress. Never reads or writes native gms.dat.</summary>
internal static partial class PlayerTicketFile
{
	public const string FileName = "opentpw-player-progress.json";
	public const int MaximumFileSize = 64 * 1024;
	private const string FormatName = "opentpw-player-progress";
	private static readonly JsonSerializerOptions Options = new()
	{
		UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
		AllowDuplicateProperties = false,
		MaxDepth = 8,
		Converters = { new ExactNameEnumConverter<ParkGameMode>(), new ExactNameEnumConverter<GoldenTicketKind>() }
	};

	// Non-Flags fields require one canonical name, not the built-in converter's comma-combined aliases.
	private sealed class ExactNameEnumConverter<T> : JsonConverter<T> where T : struct, Enum
	{
		public override T Read( ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options )
		{
			if ( reader.TokenType != JsonTokenType.String ) throw new JsonException( "Expected one exact enum name." );
			var name = reader.GetString();
			if ( !Enum.TryParse<T>( name, out var value ) || Enum.GetName( value ) != name ) throw new JsonException( "Expected one exact enum name." );
			return value;
		}
		public override void Write( Utf8JsonWriter writer, T value, JsonSerializerOptions options )
			=> writer.WriteStringValue( Enum.GetName( value ) ?? throw new JsonException( "Unknown enum value." ) );
	}

	private static readonly ProgressJsonContext Context = new( Options );

	[JsonSerializable( typeof( Data ) )]
	private partial class ProgressJsonContext : JsonSerializerContext;

	private sealed record Data
	{
		public required string Format { get; init; }
		public required int Version { get; init; }
		public required ParkGameMode Mode { get; init; }
		public required int ExtraKeys { get; init; }
		public required int SpentTickets { get; init; }
		public required List<TicketData> Earned { get; init; }
		/// <summary>Optional: the park holding each global ticket and its record (absent in files written before it existed, which load with unknown holders).</summary>
		[JsonIgnore( Condition = JsonIgnoreCondition.WhenWritingNull )]
		public List<HolderData>? GlobalHolders { get; init; }
		[JsonIgnore( Condition = JsonIgnoreCondition.WhenWritingNull )]
		public List<PurchaseData>? KnownPurchases { get; init; }
		/// <summary>Optional: the parks whose research the player has completed (absent in files written before it existed and while none has, which load with none).</summary>
		[JsonIgnore( Condition = JsonIgnoreCondition.WhenWritingNull )]
		public List<string>? ResearchCompleted { get; init; }
	}
	private sealed record PurchaseData
	{
		public required int InfoId { get; init; }
		public required int Cost { get; init; }
	}
	private sealed record HolderData
	{
		public required GoldenTicketKind Kind { get; init; }
		public required string Theme { get; init; }
		public required long Record { get; init; }
	}
	private sealed record TicketData
	{
		public required string? Theme { get; init; }
		public required GoldenTicketKind Kind { get; init; }
		/// <summary>[EXT:cheats-ticket-provenance] A cheat awarded this ticket; absent for a ticket the park won.</summary>
		[JsonIgnore( Condition = JsonIgnoreCondition.WhenWritingDefault )]
		public bool Cheated { get; init; }
	}

	public static byte[] Serialize( PlayerTicketSnapshot snapshot )
	{
		var copy = PlayerTicketLedger.Restore( snapshot ).Capture();
		return JsonSerializer.SerializeToUtf8Bytes( new Data { Format = FormatName, Version = copy.KnownPurchases == null ? 1 : 2, Mode = copy.Mode, ExtraKeys = copy.ExtraKeys,
			SpentTickets = copy.SpentTickets, Earned = copy.Earned.Select( item => new TicketData { Theme = item.Theme, Kind = item.Kind, Cheated = copy.Cheated?.Contains( item ) == true } ).ToList(),
			KnownPurchases = copy.KnownPurchases?.Select( item => new PurchaseData { InfoId = item.InfoId, Cost = item.Cost } ).ToList(),
			GlobalHolders = copy.Holders?.Select( item => new HolderData { Kind = item.Kind, Theme = item.Theme, Record = item.Record } ).ToList(),
			ResearchCompleted = copy.ResearchCompleted?.ToList() }, Context.Data );
	}

	public static PlayerTicketLedger Deserialize( ReadOnlySpan<byte> bytes )
	{
		if ( bytes.Length > MaximumFileSize ) throw new InvalidDataException( "Own player progress exceeds the size limit." );
		Data? data;
		try { data = JsonSerializer.Deserialize( bytes, Context.Data ); }
		catch ( JsonException exception ) { throw new InvalidDataException( "Malformed own player progress.", exception ); }
		if ( data == null || data.Format != FormatName || data.Version is not (1 or 2) || data.Earned == null || data.Earned.Count > PlayerTicketLedger.MaximumIdentities || data.Earned.Any( item => item == null ) || data.GlobalHolders?.Count > 4
			|| data.ResearchCompleted?.Count > 4 )
			throw new InvalidDataException( "Invalid own player progress envelope." );
		// Version 1 remains byte/shape compatible when no purchase identities are known. Old strict readers reject version 2.
		if ( data.Version == 1 ? data.KnownPurchases != null : data.KnownPurchases == null || data.KnownPurchases.Count == 0 )
			throw new InvalidDataException( "Invalid purchase history version." );
		if ( data.KnownPurchases?.Count > PlayerTicketLedger.MaximumIdentities ) throw new InvalidDataException( "Too many known ticket purchases." );
		return PlayerTicketLedger.Restore( new( data.Mode, data.ExtraKeys, data.SpentTickets,
			data.Earned.Select( item => new EarnedTicketIdentity( item.Theme, item.Kind ) ).ToArray(),
			data.GlobalHolders?.Select( item => item == null ? throw new InvalidDataException( "Invalid own player progress envelope." ) : new GlobalTicketHolder( item.Kind, item.Theme, item.Record ) ).ToArray(),
			data.Earned.Any( item => item.Cheated ) ? data.Earned.Where( item => item.Cheated ).Select( item => new EarnedTicketIdentity( item.Theme, item.Kind ) ).ToArray() : null,
			data.KnownPurchases?.Select( item => item == null ? throw new InvalidDataException( "Invalid known ticket purchase." ) : new KnownTicketPurchase( item.InfoId, item.Cost ) ).ToArray(),
			data.ResearchCompleted?.ToArray() ) );
	}

	public static void Save( string path, PlayerTicketSnapshot snapshot )
	{
		var destination = OwnPath( path );
		var bytes = Serialize( snapshot ); // Complete validation before touching destination or temporary files.
		AtomicFile.Write( destination, bytes );
	}

	/// <summary>Absence is unknown history, distinct from an explicitly created empty own profile.</summary>
	public static PlayerTicketLedger? LoadIfPresent( string path ) => ReadIfPresent( path )?.Ledger;

	internal sealed record ReadState( PlayerTicketLedger Ledger, string Digest );

	internal static ReadState? ReadIfPresent( string path )
	{
		var destination = OwnPath( path );
		try
		{
			using var stream = new FileStream( destination, FileMode.Open, FileAccess.Read, FileShare.Read );
			if ( stream.Length > MaximumFileSize ) throw new InvalidDataException( "Own player progress exceeds the size limit." );
			var bytes = new byte[stream.Length];
			stream.ReadExactly( bytes );
			return new( Deserialize( bytes ), Convert.ToHexString( SHA256.HashData( bytes ) ) );
		}
		catch ( FileNotFoundException ) { return null; }
		catch ( DirectoryNotFoundException ) { return null; }
	}

	/// <summary>Optimistic single-owner change refusal, not a cross-process atomic compare-and-swap.</summary>
	internal static string SaveIfUnchanged( string path, PlayerTicketSnapshot snapshot, string? expectedDigest )
	{
		var destination = OwnPath( path );
		var bytes = Serialize( snapshot );
		if ( ReadIfPresent( destination )?.Digest != expectedDigest )
			throw new InvalidDataException( "The OpenTPW progress file changed. Reopen the park choice." );
		AtomicFile.Write( destination, bytes );
		return Convert.ToHexString( SHA256.HashData( bytes ) );
	}

	private static string OwnPath( string path )
	{
		ArgumentException.ThrowIfNullOrWhiteSpace( path );
		if ( !Path.GetExtension( path ).Equals( ".json", StringComparison.OrdinalIgnoreCase ) )
			throw new InvalidDataException( "Own player progress requires a JSON destination; native profiles are unsupported." );
		return Path.GetFullPath( path );
	}
}

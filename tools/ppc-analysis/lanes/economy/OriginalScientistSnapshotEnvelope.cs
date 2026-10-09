using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace OpenTPW.Reverse.Economy;

/// <summary>
/// A structurally validated persisted snapshot. Source provenance is retained as a claim;
/// the original payload/container are not embedded or revalidated by JSON loading.
/// </summary>
public sealed class PersistedOriginalScientistSnapshot
{
	internal PersistedOriginalScientistSnapshot( OriginalScientistSnapshot snapshot ) => Snapshot = snapshot;
	public OriginalScientistSnapshot Snapshot { get; }
	public bool SourcePayloadRevalidated => false;
	public bool CanRestoreRuntimeStaff => false;
}

/// <summary>
/// Own-lane strict JSON persistence of original data only, independent of ParkSaveFile.
/// No mode, runtime staff conversion, original actor inference or global mutation occurs.
/// Native u64 time is fixed-width hex; f32 values are raw u32 bits; names are ushort units.
/// </summary>
public static class OriginalScientistSnapshotEnvelope
{
	public const string Format = "opentpw-original-scientist-snapshot";
	public const int Version = 1;
	public const int MaximumJsonBytes = 128 * 1024;
	private const string Scope = "original-data-only; persisted-source-claims";
	private static readonly JsonSerializerOptions Options = new()
	{
		WriteIndented = true,
		PropertyNameCaseInsensitive = false,
		UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
		MaxDepth = 16,
		Converters = { new JsonStringEnumConverter( allowIntegerValues: false ) }
	};

	private sealed class Data
	{
		public required string Format { get; init; }
		public required int Version { get; init; }
		public required string Scope { get; init; }
		public required SnapshotData Snapshot { get; init; }
	}

	private sealed class SnapshotData
	{
		public required ScientistSnapshotProvenance Provenance { get; init; }
		public required OriginalActorPrefixRecord Actor { get; init; }
		public required OriginalActorPrefixRecord[] Prefix { get; init; }
		public required ushort[] InlineNameCodeUnits { get; init; }
		public required string InlineNameSha256 { get; init; }
		public required string PersonRecordSha256 { get; init; }
		public required ushort XWord { get; init; }
		public required ushort YWord { get; init; }
		public required ushort MapChildId { get; init; }
		public required ushort MapParentId { get; init; }
		public required uint GradeWord { get; init; }
		public required uint HappinessBits { get; init; }
		public required uint JobsDone { get; init; }
		public required ushort PatrolBottomLeftWord { get; init; }
		public required ushort PatrolTopRightWord { get; init; }
		public required byte PercentageThroughGrade { get; init; }
		public required ushort RestAreaId { get; init; }
		public required uint RawStateWord { get; init; }
		public required uint StartedIdlingTick { get; init; }
		public required string HiredTimestampHex { get; init; }
		public required uint EnergyBits { get; init; }
		public required uint StartedResearchingTick { get; init; }
		public required ushort NextResearcherId { get; init; }
		public required int PayloadBytesAfterRecord { get; init; }
	}

	private static readonly string[] SnapshotFields =
	{
		"Provenance", "Actor", "Prefix", "InlineNameCodeUnits", "InlineNameSha256", "PersonRecordSha256",
		"XWord", "YWord", "MapChildId", "MapParentId", "GradeWord", "HappinessBits", "JobsDone",
		"PatrolBottomLeftWord", "PatrolTopRightWord", "PercentageThroughGrade", "RestAreaId", "RawStateWord",
		"StartedIdlingTick", "HiredTimestampHex", "EnergyBits", "StartedResearchingTick", "NextResearcherId",
		"PayloadBytesAfterRecord"
	};
	private static readonly string[] ActorFields =
		{ "ActorId", "NextUsedActorId", "Model", "HeaderOffset", "BodyOffset", "BodyBytes", "EndExclusive" };
	private static readonly string[] ProvenanceFields =
	{
		"Qualification", "PayloadSha256", "ContainerSha256", "SchemaBinaryReferenceSha256", "SchemaEvidence",
		"UsedThingHeadOffset", "ExpectedFirstResearcherId", "ObservedWorldTick"
	};

	public static byte[] Serialize( OriginalScientistSnapshot snapshot )
	{
		ArgumentNullException.ThrowIfNull( snapshot );
		var data = Capture( snapshot );
		Validate( data );
		var result = JsonSerializer.SerializeToUtf8Bytes( data, Options );
		if ( result.Length > MaximumJsonBytes )
			throw new InvalidDataException( "Scientist envelope exceeds its JSON size limit." );
		return result;
	}

	public static PersistedOriginalScientistSnapshot Deserialize( ReadOnlySpan<byte> json )
	{
		if ( json.Length > MaximumJsonBytes )
			throw new InvalidDataException( "Scientist envelope exceeds its JSON size limit." );
		Data data;
		try
		{
			using var document = JsonDocument.Parse( json.ToArray(), new JsonDocumentOptions { MaxDepth = 16 } );
			CheckShape( document.RootElement );
			data = document.RootElement.Deserialize<Data>( Options ) ?? throw new InvalidDataException( "Null envelope." );
		}
		catch ( JsonException error )
		{
			throw new InvalidDataException( "Malformed scientist envelope JSON.", error );
		}
		Validate( data );
		var s = data.Snapshot;
		// All parsing, shape checks and consistency validation finish before a
		// new immutable result is published. Existing snapshots are untouched.
		var snapshot = new OriginalScientistSnapshot( s.Provenance, s.Actor, (OriginalActorPrefixRecord[])s.Prefix.Clone(),
			(ushort[])s.InlineNameCodeUnits.Clone(), s.InlineNameSha256, s.PersonRecordSha256,
			s.XWord, s.YWord, s.MapChildId, s.MapParentId, s.GradeWord, s.HappinessBits, s.JobsDone,
			s.PatrolBottomLeftWord, s.PatrolTopRightWord, s.PercentageThroughGrade, s.RestAreaId, s.RawStateWord,
			s.StartedIdlingTick, ulong.Parse( s.HiredTimestampHex, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture ),
			s.EnergyBits, s.StartedResearchingTick, s.NextResearcherId, s.PayloadBytesAfterRecord );
		return new PersistedOriginalScientistSnapshot( snapshot );
	}

	private static Data Capture( OriginalScientistSnapshot s ) => new()
	{
		Format = Format, Version = Version, Scope = Scope,
		Snapshot = new SnapshotData
		{
			Provenance = s.Provenance, Actor = s.Actor, Prefix = s.Prefix.ToArray(),
			InlineNameCodeUnits = s.InlineNameCodeUnits.ToArray(), InlineNameSha256 = s.InlineNameSha256,
			PersonRecordSha256 = s.PersonRecordSha256, XWord = s.XWord, YWord = s.YWord,
			MapChildId = s.MapChildId, MapParentId = s.MapParentId, GradeWord = s.GradeWord,
			HappinessBits = s.HappinessBits, JobsDone = s.JobsDone, PatrolBottomLeftWord = s.PatrolBottomLeftWord,
			PatrolTopRightWord = s.PatrolTopRightWord, PercentageThroughGrade = s.PercentageThroughGrade,
			RestAreaId = s.RestAreaId, RawStateWord = s.RawStateWord, StartedIdlingTick = s.StartedIdlingTick,
			HiredTimestampHex = s.HiredTimestamp.ToString( "x16", CultureInfo.InvariantCulture ), EnergyBits = s.EnergyBits,
			StartedResearchingTick = s.StartedResearchingTick, NextResearcherId = s.NextResearcherId,
			PayloadBytesAfterRecord = s.PayloadBytesAfterRecord
		}
	};

	private static void CheckShape( JsonElement root )
	{
		ObjectShape( root, "Format", "Version", "Scope", "Snapshot" );
		var s = root.GetProperty( "Snapshot" );
		ObjectShape( s, SnapshotFields );
		ObjectShape( s.GetProperty( "Provenance" ), ProvenanceFields );
		var qualification = s.GetProperty( "Provenance" ).GetProperty( "Qualification" );
		Require( qualification.ValueKind == JsonValueKind.String && qualification.GetString() is
			"CallerSuppliedPrefixBoundary" or "IdentifiedPcEasymodeFixture", "qualification must be an exact enum name" );
		ObjectShape( s.GetProperty( "Actor" ), ActorFields );
		var prefix = s.GetProperty( "Prefix" );
		Require( prefix.ValueKind == JsonValueKind.Array && prefix.GetArrayLength() is >= 1 and <= 64, "prefix length" );
		foreach ( var actor in prefix.EnumerateArray() ) ObjectShape( actor, ActorFields );
		var name = s.GetProperty( "InlineNameCodeUnits" );
		Require( name.ValueKind == JsonValueKind.Array && name.GetArrayLength() == 33, "inline name unit count" );
	}

	private static void ObjectShape( JsonElement element, params string[] expected )
	{
		Require( element.ValueKind == JsonValueKind.Object, "expected object" );
		var names = new HashSet<string>( StringComparer.Ordinal );
		foreach ( var property in element.EnumerateObject() )
			Require( names.Add( property.Name ) && expected.Contains( property.Name, StringComparer.Ordinal ),
				"duplicate or unknown member: " + property.Name );
		Require( names.Count == expected.Length, "missing required member" );
	}

	private static void Validate( Data data )
	{
		Require( data.Format == Format && data.Version == Version && data.Scope == Scope, "format/version/scope" );
		var s = data.Snapshot;
		if ( s == null || s.Provenance == null || s.Prefix == null || s.InlineNameCodeUnits == null )
			throw new InvalidDataException( "Null original scientist snapshot data." );
		var p = s.Provenance;
		Require( IsHex( p.PayloadSha256, 64 ) && IsHex( s.PersonRecordSha256, 64 ) && IsHex( s.InlineNameSha256, 64 ), "digest encoding" );
		Require( p.SchemaBinaryReferenceSha256 == OriginalScientistSnapshotReader.SchemaBinaryReferenceSha256,
			"unsupported schema reference" );
		Require( p.SchemaEvidence != null && p.SchemaEvidence.Length is >= 1 and <= 512, "schema evidence citation" );
		Require( Enum.IsDefined( p.Qualification ), "qualification" );
		if ( p.Qualification == ScientistSnapshotQualification.CallerSuppliedPrefixBoundary )
			Require( p.ContainerSha256 == null && p.ObservedWorldTick == null, "caller boundary cannot claim container/clock verification" );
		else
			Require( p.ContainerSha256 == OriginalScientistSnapshotReader.PcContainerSha256
				&& p.PayloadSha256 == OriginalScientistSnapshotReader.PcPayloadSha256
				&& p.UsedThingHeadOffset == 1385521 && p.ExpectedFirstResearcherId == 30 && p.ObservedWorldTick == 755,
				"identified fixture provenance differs" );
		Require( s.InlineNameCodeUnits.Length == 33 && IsHex( s.HiredTimestampHex, 16 ), "name/time encoding" );
		var nameBytes = new byte[66];
		for ( var i = 0; i < s.InlineNameCodeUnits.Length; i++ )
			BinaryPrimitives.WriteUInt16LittleEndian( nameBytes.AsSpan( i * 2, 2 ), s.InlineNameCodeUnits[i] );
		Require( Convert.ToHexString( SHA256.HashData( nameBytes ) ).ToLowerInvariant() == s.InlineNameSha256, "inline name digest mismatch" );
		Require( s.Prefix.Length is >= 1 and <= 64 && p.UsedThingHeadOffset >= 0
			&& p.UsedThingHeadOffset <= OriginalScientistSnapshotReader.MaximumPayloadBytes - 4, "prefix boundary" );
		var seen = new HashSet<uint>();
		long offset = (long)p.UsedThingHeadOffset + 4;
		for ( var i = 0; i < s.Prefix.Length; i++ )
		{
			var actor = s.Prefix[i];
			var last = i == s.Prefix.Length - 1;
			Require( actor.ActorId != 0 && seen.Add( actor.ActorId ), "invalid/cyclic actor identity" );
			Require( actor.Model == (last ? 8u : 1u), "unsupported model in persisted prefix" );
			var bytes = last ? 501 : 525;
			Require( actor.HeaderOffset == offset && actor.BodyOffset == offset + 8 && actor.BodyBytes == bytes
				&& actor.EndExclusive == offset + 8 + bytes && actor.EndExclusive <= OriginalScientistSnapshotReader.MaximumPayloadBytes,
				"actor framing" );
			Require( actor.NextUsedActorId == 0 || !seen.Contains( actor.NextUsedActorId ), "visible next-link cycle" );
			if ( !last ) Require( actor.NextUsedActorId == s.Prefix[i + 1].ActorId, "disconnected prefix" );
			offset = actor.EndExclusive;
		}
		Require( s.Actor == s.Prefix[^1] && (!p.ExpectedFirstResearcherId.HasValue || p.ExpectedFirstResearcherId == s.Actor.ActorId), "scientist identity" );
		Require( s.NextResearcherId == 0 || s.NextResearcherId != s.Actor.ActorId, "researcher self-cycle" );
		Require( s.PayloadBytesAfterRecord >= 0 && offset + s.PayloadBytesAfterRecord <= OriginalScientistSnapshotReader.MaximumPayloadBytes, "unparsed tail size" );
	}

	private static bool IsHex( string? value, int length ) => value != null && value.Length == length
		&& value.All( c => c is >= '0' and <= '9' or >= 'a' and <= 'f' );
	private static void Require( bool condition, string message )
	{
		if ( !condition ) throw new InvalidDataException( "Inconsistent original scientist envelope: " + message );
	}
}

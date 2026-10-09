using System;
using System.Buffers.Binary;
using System.IO;
using System.Text;
using System.Text.Json.Nodes;
using static OpenTPW.Reverse.Economy.OriginalScientistSnapshotTests;

namespace OpenTPW.Reverse.Economy;

internal static class OriginalScientistSnapshotEnvelopeTests
{
	public static void RunAll( Action<string, Action> run )
	{
		run( "envelope lossless raw representations and immutable ownership", RawRoundTrip );
		run( "envelope float-pattern and native-u64 boundary matrix", BitAndTimeMatrix );
		run( "envelope rejects every missing and unknown nested field", RequiredAndUnknown );
		run( "envelope rejects duplicate members and invalid JSON/types", SyntaxAndTypes );
		run( "envelope validates digest, framing, links and provenance", Consistency );
		run( "envelope resource bounds and rejected-load immutability", BoundsAndNoMutation );
	}

	private static OriginalScientistSnapshot Sample()
	{
		var (payload, _, _, staff) = Synthetic();
		BinaryPrimitives.WriteUInt16LittleEndian( payload.AsSpan( staff + 12, 2 ), 0xd800 );
		BinaryPrimitives.WriteUInt16LittleEndian( payload.AsSpan( staff + 14, 2 ), 0 );
		BinaryPrimitives.WriteUInt16LittleEndian( payload.AsSpan( staff + 16, 2 ), 0xdc00 );
		BinaryPrimitives.WriteUInt32LittleEndian( payload.AsSpan( staff + 4, 4 ), 0x7fc01234 );
		BinaryPrimitives.WriteUInt32LittleEndian( payload.AsSpan( staff + 101, 4 ), 0x80000000 );
		BinaryPrimitives.WriteUInt32LittleEndian( payload.AsSpan( staff + 85, 4 ), uint.MaxValue );
		return OriginalScientistSnapshotReader.ReadPrefix( payload, 0, 37 );
	}

	internal static void CheckRoundTrip( OriginalScientistSnapshot source )
	{
		var encoded = OriginalScientistSnapshotEnvelope.Serialize( source );
		var persisted = OriginalScientistSnapshotEnvelope.Deserialize( encoded );
		var restored = persisted.Snapshot;
		Equal( false, persisted.SourcePayloadRevalidated ); Equal( false, persisted.CanRestoreRuntimeStaff );
		Equal( source.Provenance, restored.Provenance ); Equal( source.Actor, restored.Actor );
		Equal( source.Prefix.Count, restored.Prefix.Count );
		for ( var i = 0; i < source.Prefix.Count; i++ ) Equal( source.Prefix[i], restored.Prefix[i] );
		for ( var i = 0; i < source.InlineNameCodeUnits.Count; i++ ) Equal( source.InlineNameCodeUnits[i], restored.InlineNameCodeUnits[i] );
		Equal( source.HappinessBits, restored.HappinessBits ); Equal( source.EnergyBits, restored.EnergyBits );
		Equal( source.HiredTimestamp, restored.HiredTimestamp ); Equal( source.RawStateWord, restored.RawStateWord );
		Equal( source.StartedIdlingTick, restored.StartedIdlingTick ); Equal( source.StartedResearchingTick, restored.StartedResearchingTick );
		Equal( source.PayloadBytesAfterRecord, restored.PayloadBytesAfterRecord ); Equal( false, restored.IsCompleteWorldSnapshot );
		Equal( Encoding.UTF8.GetString( encoded ), Encoding.UTF8.GetString( OriginalScientistSnapshotEnvelope.Serialize( restored ) ) );
	}

	private static JsonObject Tree( OriginalScientistSnapshot? snapshot = null ) =>
		JsonNode.Parse( OriginalScientistSnapshotEnvelope.Serialize( snapshot ?? Sample() ) )!.AsObject();
	private static JsonObject Data( JsonObject tree ) => tree["Snapshot"]!.AsObject();
	private static JsonObject Origin( JsonObject tree ) => Data( tree )["Provenance"]!.AsObject();
	private static byte[] Bytes( JsonObject tree ) => Encoding.UTF8.GetBytes( tree.ToJsonString() );
	private static void Bad( Action<JsonObject> mutate )
	{
		var tree = Tree(); mutate( tree );
		Reject<InvalidDataException>( () => OriginalScientistSnapshotEnvelope.Deserialize( Bytes( tree ) ) );
	}

	private static void RawRoundTrip()
	{
		var source = Sample(); CheckRoundTrip( source );
		var tree = Tree( source );
		Equal( "ffffffffffffffff", Data( tree )["HiredTimestampHex"]!.GetValue<string>() );
		Equal( 0x7fc01234u, Data( tree )["HappinessBits"]!.GetValue<uint>() );
		Equal( (ushort)0xd800, Data( tree )["InlineNameCodeUnits"]![0]!.GetValue<ushort>() );
		var bytes = Bytes( tree ); var loaded = OriginalScientistSnapshotEnvelope.Deserialize( bytes ).Snapshot;
		Array.Fill( bytes, (byte)0 );
		Equal( (ushort)0xd800, loaded.InlineNameCodeUnits[0] ); Equal( 0x7fc01234u, loaded.HappinessBits );
		Equal( ulong.MaxValue, loaded.HiredTimestamp );
		Reject<NotSupportedException>( () => ((System.Collections.Generic.IList<ushort>)loaded.InlineNameCodeUnits)[0] = 1 );
	}

	private static void BitAndTimeMatrix()
	{
		var patterns = new[] { 0u, 0x80000000u, 1u, 0x007fffffu, 0x3f800000u, 0x7f7fffffu,
			0x7f800000u, 0xff800000u, 0x7f800001u, 0x7fc01234u, uint.MaxValue };
		var times = new[] { 0ul, 1ul, (1ul << 53) - 1, 1ul << 53, (1ul << 53) + 1, ulong.MaxValue };
		foreach ( var bits in patterns )
			foreach ( var time in times )
			{
				var (payload, _, _, staff) = Synthetic();
				BinaryPrimitives.WriteUInt32LittleEndian( payload.AsSpan( staff + 4, 4 ), bits );
				BinaryPrimitives.WriteUInt32LittleEndian( payload.AsSpan( staff + 101, 4 ), ~bits );
				BinaryPrimitives.WriteUInt64LittleEndian( payload.AsSpan( staff + 93, 8 ), time );
				var original = OriginalScientistSnapshotReader.ReadPrefix( payload, 0 );
				var encoded = OriginalScientistSnapshotEnvelope.Serialize( original );
				var restored = OriginalScientistSnapshotEnvelope.Deserialize( encoded ).Snapshot;
				Equal( bits, restored.HappinessBits ); Equal( ~bits, restored.EnergyBits );
				Equal( time, restored.HiredTimestamp );
				Equal( Encoding.UTF8.GetString( encoded ), Encoding.UTF8.GetString( OriginalScientistSnapshotEnvelope.Serialize( restored ) ) );
			}
	}

	private static void RequiredAndUnknown()
	{
		foreach ( var path in new[] { "root", "snapshot", "provenance", "actor", "prefixActor" } )
		{
			JsonObject Select( JsonObject tree ) => path switch
			{
				"root" => tree, "snapshot" => Data( tree ), "provenance" => Origin( tree ),
				"actor" => Data( tree )["Actor"]!.AsObject(), _ => Data( tree )["Prefix"]![0]!.AsObject()
			};
			var properties = new System.Collections.Generic.List<string>();
			foreach ( var property in Select( Tree() ) ) properties.Add( property.Key );
			foreach ( var key in properties ) Bad( tree => Select( tree ).Remove( key ) );
			Bad( tree => Select( tree )["Unknown"] = 1 );
		}
		Bad( tree => tree["Format"] = "opentpw-park" );
		Bad( tree => tree["Version"] = 2 );
		Bad( tree => tree["Scope"] = "runtime-staff" );
		Bad( tree => tree["Mode"] = "InstantAction" );
		Bad( tree => tree["Snapshot"] = null );
		Bad( tree => Data( tree )["Provenance"] = null );
		Bad( tree => Data( tree )["PersonRecordSha256"] = null );
		Bad( tree => Data( tree )["HiredTimestampHex"] = null );
	}

	private static void SyntaxAndTypes()
	{
		var json = Encoding.UTF8.GetString( Bytes( Tree() ) );
		foreach ( var key in new[] { "Version", "GradeWord", "Qualification", "ActorId" } )
		{
			var token = "\"" + key + "\":";
			var duplicate = json.Replace( token, token + "0," + token, StringComparison.Ordinal );
			Reject<InvalidDataException>( () => OriginalScientistSnapshotEnvelope.Deserialize( Encoding.UTF8.GetBytes( duplicate ) ) );
		}
		foreach ( var text in new[] { "null", "[]", json[..^1], json + "{}", "{\"x\":NaN}", "{ /* comment */ }" } )
			Reject<InvalidDataException>( () => OriginalScientistSnapshotEnvelope.Deserialize( Encoding.UTF8.GetBytes( text ) ) );
		Bad( tree => Origin( tree )["Qualification"] = 1 );
		Bad( tree => Origin( tree )["Qualification"] = "0" );
		Bad( tree => Origin( tree )["Qualification"] = "Unknown" );
		Reject<InvalidDataException>( () => OriginalScientistSnapshotEnvelope.Deserialize( Encoding.UTF8.GetBytes( new string( '[', 17 ) + "0" + new string( ']', 17 ) ) ) );
		Bad( tree => Data( tree )["GradeWord"] = -1 );
		Bad( tree => Data( tree )["EnergyBits"] = 4294967296ul );
		Bad( tree => Data( tree )["PercentageThroughGrade"] = 256 );
		Bad( tree => Data( tree )["InlineNameCodeUnits"]![0] = 65536 );
		Bad( tree => Data( tree )["InlineNameCodeUnits"]![0] = -1 );
		Bad( tree => Data( tree )["InlineNameCodeUnits"]!.AsArray().RemoveAt( 0 ) );
		Bad( tree => Data( tree )["HiredTimestampHex"] = ulong.MaxValue );
		foreach ( var time in new[] { "fffffffffffffff", "FFFFFFFFFFFFFFFF", "00000000000000xz", "-000000000000001" } )
			Bad( tree => Data( tree )["HiredTimestampHex"] = time );
	}

	private static void Consistency()
	{
		Bad( tree => Data( tree )["InlineNameSha256"] = new string( '0', 64 ) );
		Bad( tree => Origin( tree )["PayloadSha256"] = "not-a-hash" );
		Bad( tree => Origin( tree )["SchemaBinaryReferenceSha256"] = new string( 'a', 64 ) );
		Bad( tree => Origin( tree )["SchemaEvidence"] = "" );
		Bad( tree => Origin( tree )["ContainerSha256"] = OriginalScientistSnapshotReader.PcContainerSha256 );
		Bad( tree => Origin( tree )["ObservedWorldTick"] = 755 );
		Bad( tree => Origin( tree )["Qualification"] = "IdentifiedPcEasymodeFixture" );
		Bad( tree => Origin( tree )["ExpectedFirstResearcherId"] = 38 );
		Bad( tree => Origin( tree )["UsedThingHeadOffset"] = -1 );
		Bad( tree => Data( tree )["Actor"]!["BodyBytes"] = 525 );
		Bad( tree => Data( tree )["Prefix"]![0]!["Model"] = 4 );
		Bad( tree => Data( tree )["Prefix"]![0]!["ActorId"] = 0 );
		Bad( tree => Data( tree )["Prefix"]![0]!["NextUsedActorId"] = 10 );
		Bad( tree => Data( tree )["Prefix"]![0]!["NextUsedActorId"] = 39 );
		Bad( tree => Data( tree )["Prefix"]![0]!["EndExclusive"] = int.MaxValue );
		Bad( tree => Data( tree )["NextResearcherId"] = 37 );
		Bad( tree => Data( tree )["PayloadBytesAfterRecord"] = -1 );
	}

	private static void BoundsAndNoMutation()
	{
		var source = Sample(); var before = Encoding.UTF8.GetString( OriginalScientistSnapshotEnvelope.Serialize( source ) );
		Bad( tree => Data( tree )["PayloadBytesAfterRecord"] = int.MaxValue );
		Bad( tree => Origin( tree )["SchemaEvidence"] = new string( 'x', 513 ) );
		Bad( tree => Data( tree )["Prefix"] = new JsonArray() );
		Bad( tree =>
		{
			var prefix = Data( tree )["Prefix"]!.AsArray();
			while ( prefix.Count < 65 ) prefix.Add( prefix[0]!.DeepClone() );
		} );
		Reject<InvalidDataException>( () => OriginalScientistSnapshotEnvelope.Deserialize( new byte[OriginalScientistSnapshotEnvelope.MaximumJsonBytes + 1] ) );
		Equal( before, Encoding.UTF8.GetString( OriginalScientistSnapshotEnvelope.Serialize( source ) ) );
	}
}

using System;
using System.Buffers.Binary;
using System.IO;
using System.IO.Compression;

namespace OpenTPW.Reverse.Economy;

internal static class OriginalScientistSnapshotTests
{
	private static int assertions;
	private static int groups;
	private static int failures;
	public static int Main( string[] args )
	{
		if ( args.Length != 0 && (args.Length != 2 || args[0] != "--fixture") )
		{
			Console.Error.WriteLine( "Usage: snapshot tests [--fixture /external/path/Easymode.TPWI]" );
			return 2;
		}
		Run( "chain IDs and all native snapshot fields", NativeFields );
		Run( "UTF16 units and copied snapshot ownership", NameAndOwnership );
		Run( "raw float bits and words are not normalized", RawBits );
		Run( "all truncated prefix lengths are rejected", Truncation );
		Run( "visible used-chain and researcher self-cycles", Cycles );
		Run( "unsupported models and missing researcher", UnsupportedAndAbsent );
		Run( "caller bounds and expected world ID", BoundsAndWorldId );
		Run( "following actors and payload tail stay unqualified", UnparsedTail );
		Run( "unidentified containers fail before decoding", Identity );
		if ( args.Length == 2 )
			Run( "identified actual PC fixture fields and provenance", () => ActualFixture( args[1] ) );
		else
			Console.WriteLine( "NOT RUN: actual PC fixture (supply --fixture; no original assets are checked in)." );
		Console.WriteLine( $"{groups - failures}/{groups} groups passed; {assertions} assertions; {failures} failures." );
		return failures == 0 ? 0 : 1;
	}

	private static void Run( string name, Action test )
	{
		groups++;
		try { test(); Console.WriteLine( $"PASS {name}" ); }
		catch ( Exception error ) { failures++; Console.Error.WriteLine( $"FAIL {name}: {error.Message}" ); }
	}

	private static void Equal<T>( T expected, T actual )
	{
		assertions++;
		if ( !Equals( expected, actual ) )
			throw new InvalidOperationException( $"Expected {expected}, got {actual}." );
	}

	private static void Reject<T>( Action action ) where T : Exception
	{
		assertions++;
		try { action(); }
		catch ( T ) { return; }
		throw new InvalidOperationException( $"Expected {typeof(T).Name}." );
	}

	private static void U32( byte[] payload, int offset, uint value ) => BinaryPrimitives.WriteUInt32LittleEndian( payload.AsSpan( offset, 4 ), value );
	private static void U16( byte[] payload, int offset, ushort value ) => BinaryPrimitives.WriteUInt16LittleEndian( payload.AsSpan( offset, 2 ), value );
	private static uint FloatBits( float value ) => unchecked((uint)BitConverter.SingleToInt32Bits( value ));

	private static (byte[] Payload, int Header, int Body, int Staff) Synthetic()
	{
		const int header = 4 + 8 + 390 + 135;
		const int body = header + 8;
		const int staff = body + 390;
		var payload = new byte[body + 501];
		U32( payload, 0, 10 );
		U32( payload, 4, 37 ); U32( payload, 8, 1 );
		U32( payload, header, 9 ); U32( payload, header + 4, 8 );
		U16( payload, body, 0x8000 ); U16( payload, body + 2, 0xffff );
		U16( payload, body + 4, 17 ); U16( payload, body + 6, 19 );
		U32( payload, staff, 4 ); U32( payload, staff + 4, FloatBits( 17f ) ); U32( payload, staff + 8, 91 );
		for ( var index = 0; index < 33; index++ )
			U16( payload, staff + 12 + index * 2, (ushort)(65 + index) );
		U16( payload, staff + 78, 0x1234 ); U16( payload, staff + 80, 0x5678 );
		payload[staff + 82] = 255; U16( payload, staff + 83, 29 ); U32( payload, staff + 85, 15 );
		U32( payload, staff + 89, uint.MaxValue );
		BinaryPrimitives.WriteUInt64LittleEndian( payload.AsSpan( staff + 93, 8 ), ulong.MaxValue );
		U32( payload, staff + 101, FloatBits( 33f ) ); U32( payload, staff + 105, 0xfffffffe );
		U16( payload, staff + 109, 23 );
		return (payload, header, body, staff);
	}

	private static void NativeFields()
	{
		var (payload, header, body, _) = Synthetic();
		var snapshot = OriginalScientistSnapshotReader.ReadPrefix( payload, 0, expectedFirstResearcherId: 37 );
		Equal( 2, snapshot.Prefix.Count ); Equal( 10u, snapshot.Prefix[0].ActorId );
		Equal( 37u, snapshot.Actor.ActorId ); Equal( 9u, snapshot.Actor.NextUsedActorId );
		Equal( header, snapshot.Actor.HeaderOffset ); Equal( body, snapshot.Actor.BodyOffset );
		Equal( 501, snapshot.Actor.BodyBytes ); Equal( payload.Length, snapshot.Actor.EndExclusive );
		Equal( (ushort)0x8000, snapshot.XWord ); Equal( ushort.MaxValue, snapshot.YWord );
		Equal( (ushort)17, snapshot.MapChildId ); Equal( (ushort)19, snapshot.MapParentId );
		Equal( 4u, snapshot.GradeWord ); Equal( 4, snapshot.SignedGrade );
		Equal( 17f, snapshot.SavedHappiness ); Equal( 33f, snapshot.SavedEnergy );
		Equal( 91u, snapshot.JobsDone ); Equal( (ushort)0x1234, snapshot.PatrolBottomLeftWord );
		Equal( (ushort)0x5678, snapshot.PatrolTopRightWord ); Equal( (byte)255, snapshot.PercentageThroughGrade );
		Equal( (ushort)29, snapshot.RestAreaId ); Equal( 15u, snapshot.RawStateWord );
		Equal( uint.MaxValue, snapshot.StartedIdlingTick ); Equal( ulong.MaxValue, snapshot.HiredTimestamp );
		Equal( 0xfffffffeu, snapshot.StartedResearchingTick ); Equal( (ushort)23, snapshot.NextResearcherId );
		Equal( ScientistSnapshotQualification.CallerSuppliedPrefixBoundary, snapshot.Provenance.Qualification );
		Equal( true, snapshot.Provenance.ContainerSha256 == null ); Equal( (uint?)37, snapshot.Provenance.ExpectedFirstResearcherId );
		Equal( true, snapshot.Provenance.ObservedWorldTick == null );
	}

	private static void NameAndOwnership()
	{
		var (payload, _, _, staff) = Synthetic();
		U16( payload, staff + 12, 0xd800 ); U16( payload, staff + 14, 0 ); U16( payload, staff + 16, 0xdc00 );
		var snapshot = OriginalScientistSnapshotReader.ReadPrefix( payload, 0 );
		Equal( 33, snapshot.InlineNameCodeUnits.Count ); Equal( (ushort)0xd800, snapshot.InlineNameCodeUnits[0] );
		Equal( (ushort)0, snapshot.InlineNameCodeUnits[1] ); Equal( (ushort)0xdc00, snapshot.InlineNameCodeUnits[2] );
		Equal( (ushort)97, snapshot.InlineNameCodeUnits[32] );
		var digest = snapshot.InlineNameSha256; var payloadDigest = snapshot.Provenance.PayloadSha256;
		Array.Fill( payload, (byte)0 );
		Equal( (ushort)0xd800, snapshot.InlineNameCodeUnits[0] ); Equal( digest, snapshot.InlineNameSha256 );
		Equal( payloadDigest, snapshot.Provenance.PayloadSha256 ); Equal( 4u, snapshot.GradeWord );
		Reject<NotSupportedException>( () => ((System.Collections.Generic.IList<ushort>)snapshot.InlineNameCodeUnits)[0] = 1 );
	}

	private static void RawBits()
	{
		var (payload, _, _, staff) = Synthetic();
		U32( payload, staff, uint.MaxValue ); U32( payload, staff + 85, uint.MaxValue );
		U32( payload, staff + 4, 0x7fc01234 ); U32( payload, staff + 101, 0x80000000 );
		var snapshot = OriginalScientistSnapshotReader.ReadPrefix( payload, 0 );
		Equal( -1, snapshot.SignedGrade ); Equal( uint.MaxValue, snapshot.RawStateWord );
		Equal( 0x7fc01234u, snapshot.HappinessBits ); Equal( true, float.IsNaN( snapshot.SavedHappiness ) );
		Equal( 0x80000000u, snapshot.EnergyBits ); Equal( 0x80000000u, FloatBits( snapshot.SavedEnergy ) );
	}

	private static void Truncation()
	{
		var (payload, _, _, _) = Synthetic();
		for ( var length = 0; length < payload.Length; length++ )
		{
			var truncated = payload.AsSpan( 0, length ).ToArray();
			Reject<InvalidDataException>( () => OriginalScientistSnapshotReader.ReadPrefix( truncated, 0 ) );
		}
	}

	private static void Cycles()
	{
		var (payload, header, _, staff) = Synthetic();
		U32( payload, 4, 10 );
		Reject<InvalidDataException>( () => OriginalScientistSnapshotReader.ReadPrefix( payload, 0 ) );
		U32( payload, 4, 37 ); U32( payload, header, 10 );
		Reject<InvalidDataException>( () => OriginalScientistSnapshotReader.ReadPrefix( payload, 0 ) );
		U32( payload, header, 37 );
		Reject<InvalidDataException>( () => OriginalScientistSnapshotReader.ReadPrefix( payload, 0 ) );
		U32( payload, header, 9 ); U16( payload, staff + 109, 37 );
		Reject<InvalidDataException>( () => OriginalScientistSnapshotReader.ReadPrefix( payload, 0 ) );
	}

	private static void UnsupportedAndAbsent()
	{
		var (payload, _, _, _) = Synthetic();
		foreach ( var model in new[] { 0u, 4u, 7u, 16u, uint.MaxValue } )
		{
			U32( payload, 8, model );
			Reject<InvalidDataException>( () => OriginalScientistSnapshotReader.ReadPrefix( payload, 0 ) );
		}
		U32( payload, 8, 1 ); U32( payload, 4, 0 );
		Reject<InvalidDataException>( () => OriginalScientistSnapshotReader.ReadPrefix( payload, 0 ) );
		U32( payload, 0, 0 );
		Reject<InvalidDataException>( () => OriginalScientistSnapshotReader.ReadPrefix( payload, 0 ) );
	}

	private static void BoundsAndWorldId()
	{
		var (payload, _, _, _) = Synthetic();
		foreach ( var offset in new[] { -1, payload.Length, int.MaxValue } )
			Reject<InvalidDataException>( () => OriginalScientistSnapshotReader.ReadPrefix( payload, offset ) );
		foreach ( var bound in new[] { 0, 65 } )
			Reject<ArgumentOutOfRangeException>( () => OriginalScientistSnapshotReader.ReadPrefix( payload, 0, maximumActors: bound ) );
		Reject<InvalidDataException>( () => OriginalScientistSnapshotReader.ReadPrefix( payload, 0, maximumActors: 1 ) );
		Reject<InvalidDataException>( () => OriginalScientistSnapshotReader.ReadPrefix( payload, 0, expectedFirstResearcherId: 0 ) );
		Reject<InvalidDataException>( () => OriginalScientistSnapshotReader.ReadPrefix( payload, 0, expectedFirstResearcherId: 38 ) );
		Reject<InvalidDataException>( () => OriginalScientistSnapshotReader.ReadPrefix( new byte[OriginalScientistSnapshotReader.MaximumPayloadBytes + 1], 0 ) );
	}

	private static void UnparsedTail()
	{
		var (payload, header, _, staff) = Synthetic();
		Array.Resize( ref payload, payload.Length + 3 );
		payload[^3] = 255; payload[^2] = 17; payload[^1] = 63;
		var snapshot = OriginalScientistSnapshotReader.ReadPrefix( payload, 0 );
		Equal( 3, snapshot.PayloadBytesAfterRecord ); Equal( true, snapshot.HasUnparsedUsedActors );
		Equal( true, snapshot.HasUnparsedResearcherLinks ); Equal( false, snapshot.IsCompleteWorldSnapshot );
		U32( payload, header, 0 ); U16( payload, staff + 109, 0 );
		snapshot = OriginalScientistSnapshotReader.ReadPrefix( payload, 0 );
		Equal( false, snapshot.HasUnparsedUsedActors ); Equal( false, snapshot.HasUnparsedResearcherLinks );
		Equal( 3, snapshot.PayloadBytesAfterRecord ); Equal( false, snapshot.IsCompleteWorldSnapshot );
	}

	private static void Identity()
	{
		Reject<InvalidDataException>( () => OriginalScientistSnapshotReader.ReadIdentifiedPcContainer( new byte[2000] ) );
		Reject<InvalidDataException>( () => OriginalScientistSnapshotReader.ReadIdentifiedPcContainer( new byte[10] ) );
	}

	private static void ActualFixture( string path )
	{
		if ( new FileInfo( path ).Length > OriginalScientistSnapshotReader.MaximumPayloadBytes )
			throw new InvalidDataException( "External fixture exceeds input bound." );
		var container = File.ReadAllBytes( path );
		var snapshot = OriginalScientistSnapshotReader.ReadIdentifiedPcContainer( container );
		Equal( ScientistSnapshotQualification.IdentifiedPcEasymodeFixture, snapshot.Provenance.Qualification );
		Equal( OriginalScientistSnapshotReader.PcContainerSha256, snapshot.Provenance.ContainerSha256! );
		Equal( OriginalScientistSnapshotReader.PcPayloadSha256, snapshot.Provenance.PayloadSha256 );
		Equal( 1385521, snapshot.Provenance.UsedThingHeadOffset ); Equal( (uint?)30, snapshot.Provenance.ExpectedFirstResearcherId );
		Equal( (uint?)755, snapshot.Provenance.ObservedWorldTick );
		Equal( 13, snapshot.Prefix.Count ); Equal( 42u, snapshot.Prefix[0].ActorId );
		Equal( 31u, snapshot.Prefix[11].ActorId ); Equal( 30u, snapshot.Actor.ActorId );
		Equal( 1391921, snapshot.Actor.HeaderOffset ); Equal( 1391929, snapshot.Actor.BodyOffset );
		Equal( 1392430, snapshot.Actor.EndExclusive ); Equal( 29u, snapshot.Actor.NextUsedActorId );
		Equal( 2, snapshot.SignedGrade ); Equal( (byte)0, snapshot.PercentageThroughGrade );
		Equal( 97f, snapshot.SavedHappiness ); Equal( 93f, snapshot.SavedEnergy );
		Equal( 0u, snapshot.JobsDone ); Equal( 1u, snapshot.RawStateWord ); Equal( (ushort)0, snapshot.RestAreaId );
		Equal( 0u, snapshot.StartedIdlingTick ); Equal( 697u, snapshot.StartedResearchingTick );
		Equal( 125935884000000000ul, snapshot.HiredTimestamp ); Equal( (ushort)0, snapshot.NextResearcherId );
		Equal( "09612b0d278adcaec1ac978505e3540eba6cf770835446234adc336adb3bcc75", snapshot.InlineNameSha256 );
		Equal( 33, snapshot.InlineNameCodeUnits.Count ); Equal( true, snapshot.HasUnparsedUsedActors );
		Equal( false, snapshot.IsCompleteWorldSnapshot ); Equal( 215879, snapshot.PayloadBytesAfterRecord );
		using ( var compressed = new MemoryStream( container, 0x629, container.Length - 0x629, writable: false ) )
		using ( var decoder = new ZLibStream( compressed, CompressionMode.Decompress ) )
		{
			var decoded = new byte[1608309];
			decoder.ReadExactly( decoded );
			var callerBoundary = OriginalScientistSnapshotReader.ReadPrefix( decoded, 1385521, 30 );
			Equal( OriginalScientistSnapshotReader.PcPayloadSha256, callerBoundary.Provenance.PayloadSha256 );
			Equal( ScientistSnapshotQualification.CallerSuppliedPrefixBoundary, callerBoundary.Provenance.Qualification );
			Equal( true, callerBoundary.Provenance.ContainerSha256 == null );
			Equal( true, callerBoundary.Provenance.ObservedWorldTick == null );
		}
		container[0] ^= 1;
		Reject<InvalidDataException>( () => OriginalScientistSnapshotReader.ReadIdentifiedPcContainer( container ) );
		Console.WriteLine( $"Fixture snapshot: actor {snapshot.Actor.ActorId}, grade {snapshot.SignedGrade}, raw state {snapshot.RawStateWord}; name SHA {snapshot.InlineNameSha256}." );
	}
}

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;

namespace OpenTPW.Reverse.Economy;

public enum ScientistSnapshotQualification
{
	CallerSuppliedPrefixBoundary,
	IdentifiedPcEasymodeFixture
}

/// <summary>Identity and limits of a data interpretation, not a selected gameplay rule set.</summary>
public sealed record ScientistSnapshotProvenance( ScientistSnapshotQualification Qualification,
	string PayloadSha256, string? ContainerSha256, string SchemaBinaryReferenceSha256, string SchemaEvidence,
	int UsedThingHeadOffset, uint? ExpectedFirstResearcherId, uint? ObservedWorldTick );

public readonly record struct OriginalActorPrefixRecord( uint ActorId, uint NextUsedActorId, uint Model,
	int HeaderOffset, int BodyOffset, int BodyBytes, int EndExclusive );

/// <summary>
/// Original serialized scientist data. All names, vitals, state words and times remain native.
/// Person navigation/thought fields and subsequent actors/subsystems are not decoded. This is
/// never a StaffMember, original-runtime execution, complete World import, or mode inference.
/// </summary>
public sealed class OriginalScientistSnapshot
{
	internal OriginalScientistSnapshot( ScientistSnapshotProvenance provenance, OriginalActorPrefixRecord actor,
		OriginalActorPrefixRecord[] prefix, ushort[] nameUnits, string nameSha256, string personSha256,
		ushort x, ushort y, ushort mapChild, ushort mapParent, uint grade, uint happinessBits, uint jobs,
		ushort patrolBottomLeft, ushort patrolTopRight, byte percentage, ushort restArea, uint state,
		uint idleTick, ulong hiredTimestamp, uint energyBits, uint researchingTick, ushort nextResearcher,
		int payloadBytesAfterRecord )
	{
		Provenance = provenance;
		Actor = actor;
		Prefix = Array.AsReadOnly( prefix );
		InlineNameCodeUnits = Array.AsReadOnly( nameUnits );
		InlineNameSha256 = nameSha256;
		PersonRecordSha256 = personSha256;
		XWord = x; YWord = y; MapChildId = mapChild; MapParentId = mapParent;
		GradeWord = grade; HappinessBits = happinessBits; JobsDone = jobs;
		PatrolBottomLeftWord = patrolBottomLeft; PatrolTopRightWord = patrolTopRight;
		PercentageThroughGrade = percentage; RestAreaId = restArea; RawStateWord = state;
		StartedIdlingTick = idleTick; HiredTimestamp = hiredTimestamp; EnergyBits = energyBits;
		StartedResearchingTick = researchingTick; NextResearcherId = nextResearcher;
		PayloadBytesAfterRecord = payloadBytesAfterRecord;
	}

	public ScientistSnapshotProvenance Provenance { get; }
	public OriginalActorPrefixRecord Actor { get; }
	public IReadOnlyList<OriginalActorPrefixRecord> Prefix { get; }
	/// <summary>All 33 UTF16 units, including embedded NULs and unpaired surrogates; never normalized.</summary>
	public IReadOnlyList<ushort> InlineNameCodeUnits { get; }
	public string InlineNameSha256 { get; }
	public string PersonRecordSha256 { get; }
	public ushort XWord { get; }
	public ushort YWord { get; }
	public ushort MapChildId { get; }
	public ushort MapParentId { get; }
	public uint GradeWord { get; }
	public int SignedGrade => unchecked((int)GradeWord);
	public uint HappinessBits { get; }
	public float SavedHappiness => BitConverter.Int32BitsToSingle( unchecked((int)HappinessBits) );
	public uint JobsDone { get; }
	public ushort PatrolBottomLeftWord { get; }
	public ushort PatrolTopRightWord { get; }
	public byte PercentageThroughGrade { get; }
	public ushort RestAreaId { get; }
	public uint RawStateWord { get; }
	public uint StartedIdlingTick { get; }
	public ulong HiredTimestamp { get; }
	public uint EnergyBits { get; }
	public float SavedEnergy => BitConverter.Int32BitsToSingle( unchecked((int)EnergyBits) );
	public uint StartedResearchingTick { get; }
	public ushort NextResearcherId { get; }
	/// <summary>All payload bytes after this record, including unparsed actors and later subsystems.</summary>
	public int PayloadBytesAfterRecord { get; }
	public bool HasUnparsedUsedActors => Actor.NextUsedActorId != 0;
	public bool HasUnparsedResearcherLinks => NextResearcherId != 0;
	public bool IsCompleteWorldSnapshot => false;
}

/// <summary>
/// Standalone reader of the reviewed model1/model8 actor-chain prefix (6305d32). A caller-provided
/// boundary is explicitly unqualified; only the exact identified PC container gets fixture
/// qualification. Other models fail before the first scientist; the suffix remains unparsed.
/// </summary>
public static class OriginalScientistSnapshotReader
{
	public const string PcContainerSha256 = "6d89303d098900364bf5e80b236b64bd85976fb947e9e4609d088547f430b39a";
	public const string PcPayloadSha256 = "a3c9a28252c37ad49a8eb78e4a0c5e1d5229d01548fa35801db67015d2589173";
	public const string SchemaBinaryReferenceSha256 = "04809cd4ccee5433c7fb0b7c93d32f6a7aa629c1849181c0b7906415e5e295f5";
	public const int PersonBytes = 390;
	public const int StaffSuffixBytes = 105;
	public const int GuestSuffixBytes = 135;
	public const int ScientistSuffixBytes = 6;
	public const int NameCodeUnits = 33;
	public const int MaximumPayloadBytes = 8 * 1024 * 1024;
	private const int ChunkOffset = 0x60d;
	private const int CompressedOffset = ChunkOffset + 28;

	public static OriginalScientistSnapshot ReadPrefix( ReadOnlySpan<byte> payload, int usedThingHeadOffset,
		uint? expectedFirstResearcherId = null, int maximumActors = 16 ) =>
		Read( payload, usedThingHeadOffset, expectedFirstResearcherId, maximumActors,
			ScientistSnapshotQualification.CallerSuppliedPrefixBoundary, null, null );

	/// <summary>Hash-verified, bounded data decompression; no original executable is run.</summary>
	public static OriginalScientistSnapshot ReadIdentifiedPcContainer( ReadOnlySpan<byte> container )
	{
		if ( container.Length > MaximumPayloadBytes || container.Length < CompressedOffset + 6 )
			throw new InvalidDataException( "Container outside the fixture input bound." );
		var containerSha = Sha( container );
		if ( containerSha != PcContainerSha256 )
			throw new InvalidDataException( "Unidentified container; this entry point qualifies only the reviewed PC fixture." );
		if ( U32( container, 0 ) is not (400 or 500) || container[0x608] != 133 || container[0x609] != 0
			|| !container.Slice( ChunkOffset, 4 ).SequenceEqual( "BILZ"u8 )
			|| U32( container, ChunkOffset + 8 ) != container.Length - ChunkOffset )
			throw new InvalidDataException( "Identified container has an inconsistent offline BILZ header." );
		var declared = U32( container, ChunkOffset + 4 );
		if ( declared > MaximumPayloadBytes )
			throw new InvalidDataException( "Decoded fixture exceeds the output bound." );
		var payload = new byte[(int)declared];
		using var compressed = new MemoryStream( container[CompressedOffset..].ToArray(), writable: false );
		using ( var decoder = new ZLibStream( compressed, CompressionMode.Decompress, leaveOpen: true ) )
		{
			decoder.ReadExactly( payload );
			if ( decoder.ReadByte() != -1 )
				throw new InvalidDataException( "Decoded fixture exceeds its declared length." );
		}
		if ( Sha( payload ) != PcPayloadSha256 )
			throw new InvalidDataException( "Decoded fixture identity does not match the reviewed payload." );
		// Action-record prefix, then the reviewed world-v2 fields. No general
		// calendar or control-manager locator is implied by this fixed fixture.
		if ( U32( payload, 0 ) != 0 )
			throw new InvalidDataException( "Identified action-record flag differs." );
		var worldOffset = checked(8 + (int)U32( payload, 4 ));
		if ( worldOffset != 1179 || U32( payload, worldOffset ) != 2 )
			throw new InvalidDataException( "Identified world prefix differs." );
		var firstResearcher = U16( payload, worldOffset + 60 );
		return Read( payload, 1385521, firstResearcher, 16,
			ScientistSnapshotQualification.IdentifiedPcEasymodeFixture, containerSha, U32( payload, worldOffset + 14 ) );
	}

	private static OriginalScientistSnapshot Read( ReadOnlySpan<byte> payload, int headOffset, uint? expectedId,
		int maximumActors, ScientistSnapshotQualification qualification, string? containerSha, uint? worldTick )
	{
		if ( payload.Length > MaximumPayloadBytes || headOffset < 0 || headOffset > payload.Length - 4 )
			throw new InvalidDataException( "Invalid or truncated actor-prefix boundary." );
		if ( maximumActors is < 1 or > 64 )
			throw new ArgumentOutOfRangeException( nameof( maximumActors ) );
		var current = U32( payload, headOffset );
		var offset = headOffset + 4;
		var seen = new HashSet<uint>();
		var prefix = new List<OriginalActorPrefixRecord>();
		for ( var count = 0; count < maximumActors; count++ )
		{
			if ( current == 0 )
				throw new InvalidDataException( "No scientist in the qualified actor prefix." );
			if ( !seen.Add( current ) )
				throw new InvalidDataException( "Cycle in the actor prefix." );
			if ( offset > payload.Length - 8 )
				throw new InvalidDataException( "Truncated actor header." );
			var next = U32( payload, offset );
			var model = U32( payload, offset + 4 );
			if ( model is not (1 or 8) )
				throw new InvalidDataException( $"Unsupported model {model} before the first scientist." );
			if ( next != 0 && seen.Contains( next ) )
				throw new InvalidDataException( "Cycle visible in the actor's next link." );
			var body = offset + 8;
			var size = PersonBytes + (model == 1 ? GuestSuffixBytes : StaffSuffixBytes + ScientistSuffixBytes);
			if ( body > payload.Length - size )
				throw new InvalidDataException( "Truncated actor body." );
			var actor = new OriginalActorPrefixRecord( current, next, model, offset, body, size, body + size );
			prefix.Add( actor );
			if ( model == 8 )
			{
				if ( expectedId.HasValue && expectedId.Value != current )
					throw new InvalidDataException( "Scientist differs from the supplied world FirstResearcher ID." );
				var staff = body + PersonBytes;
				var nextResearcher = U16( payload, staff + 109 );
				if ( nextResearcher != 0 && nextResearcher == current )
					throw new InvalidDataException( "Self-cycle in the scientist's role link." );
				var name = new ushort[NameCodeUnits];
				for ( var i = 0; i < name.Length; i++ )
					name[i] = U16( payload, staff + 12 + i * 2 );
				var provenance = new ScientistSnapshotProvenance( qualification, Sha( payload ), containerSha,
					SchemaBinaryReferenceSha256, "6305d32; staff_evidence.py; Mac framing of the shared PC fixture",
					headOffset, expectedId, worldTick );
				return new OriginalScientistSnapshot( provenance, actor, prefix.ToArray(), name,
					Sha( payload.Slice( staff + 12, 66 ) ), Sha( payload.Slice( body, PersonBytes ) ),
					U16( payload, body ), U16( payload, body + 2 ), U16( payload, body + 4 ), U16( payload, body + 6 ),
					U32( payload, staff ), U32( payload, staff + 4 ), U32( payload, staff + 8 ),
					U16( payload, staff + 78 ), U16( payload, staff + 80 ), payload[staff + 82],
					U16( payload, staff + 83 ), U32( payload, staff + 85 ), U32( payload, staff + 89 ),
					BinaryPrimitives.ReadUInt64LittleEndian( payload.Slice( staff + 93, 8 ) ),
					U32( payload, staff + 101 ), U32( payload, staff + 105 ), nextResearcher,
					payload.Length - actor.EndExclusive );
			}
			current = next;
			offset = actor.EndExclusive;
		}
		throw new InvalidDataException( "Scientist outside the bounded prefix." );
	}

	private static uint U32( ReadOnlySpan<byte> source, int offset ) => BinaryPrimitives.ReadUInt32LittleEndian( source.Slice( offset, 4 ) );
	private static ushort U16( ReadOnlySpan<byte> source, int offset ) => BinaryPrimitives.ReadUInt16LittleEndian( source.Slice( offset, 2 ) );
	private static string Sha( ReadOnlySpan<byte> source ) => Convert.ToHexString( SHA256.HashData( source ) ).ToLowerInvariant();
}

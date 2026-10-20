using System.Buffers.Binary;

namespace OpenTPW;

/// <summary>A TPW track-ride handle: signed native kind in the high 24 bits and table slot in the low byte.</summary>
public readonly record struct SaveTrackRideIdentity( uint Value )
{
	public int Kind => unchecked((int)Value) >> 8;
	public int Slot => (int)(Value & 0xff);
}

/// <summary>Chunk 3's ten serialized words, not a raw copy of the native 0xd0-byte ride.</summary>
public sealed record SaveTrackRideHeader( SaveTrackRideIdentity Identity, int Coordinate0, int Coordinate1,
	uint FieldB0, uint Field10, uint Field1C, uint Field14, uint Field18, uint Duration, uint Field50 );

/// <summary>Chunk 4's tile parameters. Native 0x54b2f0 constructs geometry and waypoints from these, not from chunk 5 order.</summary>
public sealed record SaveTrackTile( SaveTrackRideIdentity Identity, uint Type, int Coordinate0, int Coordinate1 );

/// <summary>Whether the serialized waypoint relocation is absent or needs the independently reconstructed waypoint chain.</summary>
public enum SaveTrackWaypointReferenceState { Absent, Unresolved }

/// <summary>Chunk 5's trailing relocation key. Owner zero means absent; a nonzero owner is not proof the waypoint exists.</summary>
public sealed record SaveTrackWaypointReference( SaveTrackRideIdentity Owner, int Coordinate0, int Coordinate1 )
{
	public SaveTrackWaypointReferenceState State => Owner.Value == 0 ? SaveTrackWaypointReferenceState.Absent : SaveTrackWaypointReferenceState.Unresolved;
}

/// <summary>A raw 0xac-byte controller record and its serialized fixups. No raw pointer is followed or exposed as a chain identity.</summary>
public sealed class SaveTrackRawRecord
{
	private readonly byte[] bytes;
	public SaveTrackRideIdentity Identity { get; }
	public ReadOnlySpan<byte> Bytes => bytes;
	public uint Flags => BinaryPrimitives.ReadUInt32LittleEndian( bytes );
	public int TileCoordinate0 => BinaryPrimitives.ReadInt32LittleEndian( bytes.AsSpan( 0x34 ) );
	public int TileCoordinate1 => BinaryPrimitives.ReadInt32LittleEndian( bytes.AsSpan( 0x38 ) );
	public int FallbackTileCoordinate0 { get; }
	public int FallbackTileCoordinate1 { get; }
	public SaveTrackWaypointReference Waypoint { get; }

	internal SaveTrackRawRecord( ReadOnlySpan<byte> payload )
	{
		Identity = new( BinaryPrimitives.ReadUInt32LittleEndian( payload ) );
		bytes = payload.Slice( 4, 0xac ).ToArray();
		FallbackTileCoordinate0 = BinaryPrimitives.ReadInt32LittleEndian( payload[0xb0..] );
		FallbackTileCoordinate1 = BinaryPrimitives.ReadInt32LittleEndian( payload[0xb4..] );
		Waypoint = new( new( BinaryPrimitives.ReadUInt32LittleEndian( payload[0xb8..] ) ),
			BinaryPrimitives.ReadInt32LittleEndian( payload[0xbc..] ), BinaryPrimitives.ReadInt32LittleEndian( payload[0xc0..] ) );
	}
}

/// <summary>Chunk 6 requests reference-waypoint selection for a ride. It does not contain a section index.</summary>
public sealed record SaveTrackReferenceRequest( SaveTrackRideIdentity Identity );

/// <summary>One immutable framed chunk, including all header words and opaque bytes even for untyped chunk kinds.</summary>
public sealed class SaveTrackRideChunk
{
	private readonly byte[] payload;
	/// <summary>Offset relative to the outer container header, including when parsed with ParseBody.</summary>
	public int Offset { get; }
	public uint Type { get; }
	public uint HeaderWord { get; }
	public uint Size { get; }
	public ReadOnlySpan<byte> Payload => payload;
	public SaveTrackRideHeader? Ride { get; }
	public SaveTrackTile? Tile { get; }
	public SaveTrackRawRecord? RawRecord { get; }
	public SaveTrackReferenceRequest? ReferenceRequest { get; }

	internal SaveTrackRideChunk( int offset, uint type, uint word, uint size, ReadOnlySpan<byte> data )
	{
		Offset = offset;
		Type = type;
		HeaderWord = word;
		Size = size;
		payload = data.ToArray();
		uint U32( int at ) => BinaryPrimitives.ReadUInt32LittleEndian( payload.AsSpan( at ) );
		int I32( int at ) => BinaryPrimitives.ReadInt32LittleEndian( payload.AsSpan( at ) );
		switch ( type )
		{
			case 3:
				Ride = new( new( U32( 0 ) ), I32( 4 ), I32( 8 ), U32( 12 ), U32( 16 ), U32( 20 ), U32( 24 ), U32( 28 ), U32( 32 ), U32( 36 ) );
				break;
			case 4:
				Tile = new( new( U32( 0 ) ), U32( 4 ), I32( 8 ), I32( 12 ) );
				break;
			case 5:
				RawRecord = new( payload );
				break;
			case 6:
				ReferenceRequest = new( new( U32( 0 ) ) );
				break;
		}
	}
}

/// <summary>
/// Structural TPW TrackRides reader. It preserves stream order, duplicate identities and positions, repeated reference requests,
/// unknown chunks and opaque bytes. It does not import rides, follow pointers, resolve waypoints or validate runtime ownership.
/// </summary>
/// <remarks>
/// Typed chunks require the exact sizes produced by tp.exe's writer: 3=52, 4=28, 5=208, 6=16 including their headers.
/// This is a managed strict writer-shape policy, not native malformed-input behavior (the native loader can zero-fill short reads
/// and skip excess bytes). Theme Park Inc is unverified. TRACK-032 remains open.
/// </remarks>
public sealed class SaveTrackRideBlock
{
	public const int HeaderBytes = 12;
	public uint Type => 1;
	public uint HeaderWord { get; }
	public uint Size { get; }
	public IReadOnlyList<SaveTrackRideChunk> Chunks { get; }

	private SaveTrackRideBlock( uint word, uint size, List<SaveTrackRideChunk> chunks )
	{
		HeaderWord = word;
		Size = size;
		Chunks = chunks.AsReadOnly();
	}

	/// <summary>Parses exactly one complete outer type-1 container, including its 12-byte header; trailing bytes are rejected.</summary>
	public static SaveTrackRideBlock ParseContainer( ReadOnlySpan<byte> container )
	{
		if ( container.Length < HeaderBytes )
			throw new InvalidDataException( "TrackRides container header is truncated." );
		var type = BinaryPrimitives.ReadUInt32LittleEndian( container );
		var word = BinaryPrimitives.ReadUInt32LittleEndian( container[4..] );
		var size = BinaryPrimitives.ReadUInt32LittleEndian( container[8..] );
		if ( type != 1 || size != (uint)container.Length )
			throw new InvalidDataException( "TrackRides requires one complete type-1 container with an exact size." );
		return ParseBody( container[HeaderBytes..], word );
	}

	/// <summary>
	/// Parses only the subchunk body, such as OriginalSavePayload.TrackRides, whose outer header has already been removed.
	/// Pass OriginalSavePayload.TrackRidesWord to preserve its second header word; caller must first check TrackRidesType == 1.
	/// </summary>
	public static SaveTrackRideBlock ParseBody( ReadOnlySpan<byte> body, uint containerWord = 0 )
	{
		if ( body.Length > int.MaxValue - HeaderBytes )
			throw new InvalidDataException( "TrackRides body exceeds the supported container size." );
		var chunks = new List<SaveTrackRideChunk>();
		var position = 0;
		while ( position < body.Length )
		{
			var remaining = body[position..];
			if ( remaining.Length < HeaderBytes )
				throw new InvalidDataException( $"TrackRides subchunk header at {position + HeaderBytes} is truncated." );
			var type = BinaryPrimitives.ReadUInt32LittleEndian( remaining );
			var word = BinaryPrimitives.ReadUInt32LittleEndian( remaining[4..] );
			var size = BinaryPrimitives.ReadUInt32LittleEndian( remaining[8..] );
			if ( size < HeaderBytes || size > (uint)remaining.Length )
				throw new InvalidDataException( $"TrackRides subchunk at {position + HeaderBytes} has an invalid size {size}." );
			var expected = type switch { 3 => 52, 4 => 28, 5 => 208, 6 => 16, _ => 0 };
			if ( expected != 0 && size != expected )
				throw new InvalidDataException( $"TrackRides chunk {type} has size {size}; the strict TPW writer shape requires {expected}." );
			chunks.Add( new( position + HeaderBytes, type, word, size, remaining.Slice( HeaderBytes, (int)size - HeaderBytes ) ) );
			position += (int)size;
		}
		// No partial document escapes if a later chunk is malformed.
		return new( containerWord, (uint)(body.Length + HeaderBytes), chunks );
	}
}

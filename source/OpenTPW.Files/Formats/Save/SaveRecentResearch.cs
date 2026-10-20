using System.Buffers.Binary;
using System.Text.Json.Serialization;

namespace OpenTPW;

/// <summary>One TPW analyser short ring in physical slot order, including serialized scratch and inactive values.</summary>
public sealed record RecentResearchRing(
	[property: JsonRequired] int Head,
	[property: JsonRequired] int Capacity,
	[property: JsonRequired] int Wrapped,
	[property: JsonRequired] int Temp,
	[property: JsonRequired] IReadOnlyList<int> Values );

/// <summary>
/// The four TPW recent-research rings only. Statistics have a separate reader in SaveParkStatistics. These offsets describe the serialized body,
/// not the runtime memory layout; Theme Park Inc has a different analyser format and is not handled here.
/// </summary>
public static class SaveRecentResearch
{
	public const int RingCount = 4, Capacity = 3, RingBytes = 17;
	public const int BodyOffset = SaveThingBase.Bytes + 5 * 4 + 1024 * 61;
	public const int Bytes = RingCount * RingBytes;

	public static RecentResearchRing[] Empty() => Enumerable.Range( 0, RingCount )
		.Select( _ => new RecentResearchRing( -1, Capacity, 0, 0, new int[Capacity] ) ).ToArray();

	/// <summary>Strict host validation; the original loader did not impose these defensive checks.</summary>
	public static void Validate( IReadOnlyList<RecentResearchRing> rings )
	{
		if ( rings == null || rings.Count != RingCount )
			throw new InvalidDataException( "Recent research must contain exactly four rings." );
		foreach ( var ring in rings )
			if ( ring == null || ring.Capacity != Capacity || ring.Head is < -1 or >= Capacity || ring.Wrapped is < 0 or > 1
				|| ring.Head == -1 && ring.Wrapped != 0 || ring.Temp is < 0 or > ushort.MaxValue
				|| ring.Values == null || ring.Values.Count != Capacity || ring.Values.Any( value => value is < 0 or > ushort.MaxValue ) )
				throw new InvalidDataException( "A recent-research ring has invalid head, capacity, wrapped flag or unsigned short values." );
	}

	public static RecentResearchRing[] Copy( IReadOnlyList<RecentResearchRing> rings )
	{
		Validate( rings );
		return rings.Select( ring => ring with { Values = ring.Values.ToArray() } ).ToArray();
	}

	private static void ValidateBody( int payloadLength, SaveThing thing )
	{
		if ( thing.Model != SaveThingModel.ParkAnalyser || thing.BodyOffset < 0 || thing.BodyLength < BodyOffset + Bytes
			|| thing.BodyOffset > payloadLength || thing.BodyLength > payloadLength - thing.BodyOffset )
			throw new InvalidDataException( "The referenced TPW park analyser does not contain the recent-research ring range." );
	}

	public static RecentResearchRing[] Read( ReadOnlySpan<byte> payload, SaveThing thing )
	{
		ValidateBody( payload.Length, thing );
		var bytes = payload.Slice( thing.BodyOffset + BodyOffset, Bytes );
		var result = new RecentResearchRing[RingCount];
		for ( var index = 0; index < RingCount; index++ )
		{
			var ring = bytes.Slice( index * RingBytes, RingBytes );
			result[index] = new RecentResearchRing( BinaryPrimitives.ReadInt32LittleEndian( ring ), BinaryPrimitives.ReadInt32LittleEndian( ring[4..] ), ring[8],
				BinaryPrimitives.ReadUInt16LittleEndian( ring[9..] ), new int[] { BinaryPrimitives.ReadUInt16LittleEndian( ring[11..] ),
					BinaryPrimitives.ReadUInt16LittleEndian( ring[13..] ), BinaryPrimitives.ReadUInt16LittleEndian( ring[15..] ) } );
		}
		Validate( result );
		return result;
	}

	/// <summary>Changes only the 68-byte range, after validating the entire input. No capacity-controlled allocation.</summary>
	public static void Write( Span<byte> payload, SaveThing thing, IReadOnlyList<RecentResearchRing> rings )
	{
		ValidateBody( payload.Length, thing );
		Validate( rings );
		var bytes = payload.Slice( thing.BodyOffset + BodyOffset, Bytes );
		for ( var index = 0; index < RingCount; index++ )
		{
			var ring = rings[index];
			var target = bytes.Slice( index * RingBytes, RingBytes );
			BinaryPrimitives.WriteInt32LittleEndian( target, ring.Head );
			BinaryPrimitives.WriteInt32LittleEndian( target[4..], ring.Capacity );
			target[8] = (byte)ring.Wrapped;
			BinaryPrimitives.WriteUInt16LittleEndian( target[9..], (ushort)ring.Temp );
			for ( var slot = 0; slot < Capacity; slot++ )
				BinaryPrimitives.WriteUInt16LittleEndian( target[(11 + slot * 2)..], (ushort)ring.Values[slot] );
		}
	}
}

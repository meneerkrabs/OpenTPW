using System.Buffers.Binary;

namespace OpenTPW;

/// <summary>The saved TPW arrival scheduler. The timer counts park turns; PeopleOnBus is a signed remainder.</summary>
public sealed record SaveArrivals( uint ArrivalRate, uint TimerStart, uint TargetVehicleCapacity, int PeopleOnBus, bool Offloading, bool GatesOpen )
{
	public static SaveArrivals Parse( ReadOnlySpan<byte> bytes )
	{
		if ( bytes.Length != SaveWorld.ArrivalBytes )
			throw new ArgumentException( "An arrival scheduler must contain exactly 18 bytes.", nameof( bytes ) );
		return new( BinaryPrimitives.ReadUInt32LittleEndian( bytes ), BinaryPrimitives.ReadUInt32LittleEndian( bytes[4..] ),
			BinaryPrimitives.ReadUInt32LittleEndian( bytes[8..] ), BinaryPrimitives.ReadInt32LittleEndian( bytes[12..] ), bytes[16] != 0, bytes[17] != 0 );
	}
}

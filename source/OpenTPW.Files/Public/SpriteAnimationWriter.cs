using System.Buffers.Binary;
using System.Text;

namespace OpenTPW;

/// <summary>Writes the 350-byte sprite animation table (<c>.ESP</c>) read by <see cref="SpriteAnimationFile"/>.</summary>
public static class SpriteAnimationWriter
{
	/// <summary>Writes a table as it was read: the magic, the bank name, zero padding, both flag bytes, the 16 slots and the 16 performance bytes.</summary>
	public static byte[] Write( SpriteAnimationFile file ) => Write( file.BankName, file.FlagA, file.FlagB, file.Animations, file.ExtraSlotBytes );

	public static byte[] Write( string bankName, byte flagA, byte flagB, IReadOnlyList<SpriteAnimation> slots, ReadOnlySpan<byte> performanceBytes )
	{
		var nameLimit = SpriteAnimationFile.SlotOffset - 2 - SpriteAnimationFile.Magic.Length - 1;
		if ( bankName.Length > nameLimit || bankName.Any( c => c is < ' ' or > '~' ) )
			throw new ArgumentException( $"The bank name must be printable ASCII of at most {nameLimit} characters.", nameof( bankName ) );
		if ( slots.Count > SpriteAnimationFile.SlotCount )
			throw new ArgumentException( $"A table has {SpriteAnimationFile.SlotCount} animation slots.", nameof( slots ) );
		if ( performanceBytes.Length != 16 )
			throw new ArgumentException( "The four performance entries are 16 bytes.", nameof( performanceBytes ) );
		var data = new byte[SpriteAnimationFile.FileBytes];
		Encoding.ASCII.GetBytes( SpriteAnimationFile.Magic ).CopyTo( data, 0 );
		Encoding.ASCII.GetBytes( bankName ).CopyTo( data, SpriteAnimationFile.Magic.Length );
		data[SpriteAnimationFile.SlotOffset - 2] = flagA;
		data[SpriteAnimationFile.SlotOffset - 1] = flagB;
		for ( var slot = 0; slot < slots.Count; slot++ )
		{
			var entry = slots[slot];
			var offset = SpriteAnimationFile.SlotOffset + slot * 4;
			BinaryPrimitives.WriteUInt16LittleEndian( data.AsSpan( offset ), checked( (ushort)entry.FirstFrame ) );
			data[offset + 2] = checked( (byte)entry.FramesPerDirection );
			data[offset + 3] = checked( (byte)entry.Directions );
		}
		performanceBytes.CopyTo( data.AsSpan( SpriteAnimationFile.SlotOffset + SpriteAnimationFile.SlotCount * 4 ) );
		return data;
	}
}

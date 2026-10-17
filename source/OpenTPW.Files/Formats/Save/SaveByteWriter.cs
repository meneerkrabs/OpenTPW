using System.Buffers.Binary;
using System.Text;

namespace OpenTPW;

/// <summary>
/// The little-endian field emitter of the save writers. The original writes every field through one stream write of its exact width (tp.exe <c>0x5F5E50</c>:
/// the stream, the source, the byte count); this emits the same bytes, so the order of the calls in a block writer is the order of the fields in the file.
/// </summary>
public sealed class SaveByteWriter
{
	private byte[] buffer = new byte[1 << 16];

	public int Length { get; private set; }

	private Span<byte> Reserve( int count )
	{
		if ( buffer.Length - Length < count )
			Array.Resize( ref buffer, Math.Max( buffer.Length * 2, Length + count ) );
		var span = buffer.AsSpan( Length, count );
		Length += count;
		return span;
	}

	public void U8( byte value ) => Reserve( 1 )[0] = value;
	public void U16( ushort value ) => BinaryPrimitives.WriteUInt16LittleEndian( Reserve( 2 ), value );
	public void U32( uint value ) => BinaryPrimitives.WriteUInt32LittleEndian( Reserve( 4 ), value );
	public void I32( int value ) => BinaryPrimitives.WriteInt32LittleEndian( Reserve( 4 ), value );
	public void I64( long value ) => BinaryPrimitives.WriteInt64LittleEndian( Reserve( 8 ), value );
	public void F32( float value ) => BinaryPrimitives.WriteSingleLittleEndian( Reserve( 4 ), value );
	public void Bytes( ReadOnlySpan<byte> value ) => value.CopyTo( Reserve( value.Length ) );

	/// <summary>A block delimiter: the original's constant (<c>WRLD</c>) is stored byte-reversed (<c>DLRW</c>).</summary>
	public void Tag( string tag )
	{
		Span<byte> bytes = stackalloc byte[tag.Length];
		Encoding.ASCII.GetBytes( tag, bytes );
		bytes.Reverse();
		Bytes( bytes );
	}

	public byte[] ToArray() => buffer.AsSpan( 0, Length ).ToArray();
}

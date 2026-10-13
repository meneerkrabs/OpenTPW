namespace OpenTPW;

/// <summary>
/// Allows reading from a byte array on a bit-by-bit basis.
/// Mainly used for LZSS (de-)compression.
/// </summary>
public sealed class BitReader
{
	private byte[] buffer;
	private int offset;
	private int bitsRemaining;
	private int value;

	public BitReader( byte[] buffer )
	{
		this.buffer = buffer;
		this.offset = 0;
		this.bitsRemaining = 0;
		this.value = 0;
	}

	public short GetBits( int length ) => ReadBits( length, false );

	/// <summary>Reads a complete bit field or rejects truncated input; used by the terminated LZSS stream.</summary>
	public short GetBitsChecked( int length ) => ReadBits( length, true );

	private short ReadBits( int length, bool rejectEnd )
	{
		if ( bitsRemaining < length )
		{
			// No data left!!
			if ( offset + 1 >= buffer.Length )
			{
				if ( rejectEnd ) throw new InvalidDataException( "Truncated LZSS bitstream." );
				return 0;
			}

			int fetch = (buffer[offset] | (buffer[offset + 1] << 8)) & 0xFFFF;
			offset += 2;
			value = value | (fetch << bitsRemaining);
			bitsRemaining += 16;
		}

		short returnValue = (short)(value & ((1 << length) - 1));

		bitsRemaining -= length;
		value >>= length;

		return returnValue;
	}
}

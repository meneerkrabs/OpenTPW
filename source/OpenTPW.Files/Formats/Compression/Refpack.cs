namespace OpenTPW;

/// <summary>
/// RefPack (EA's LZ77 variant, http://wiki.niotso.org/RefPack) as the DWFB archives store it: a two-byte header (<c>10 FB</c>), a
/// 24-bit size, then commands. The first byte picks the command (bit 0 is the top bit): <c>0xxxxxxx</c> two bytes, <c>10xxxxxx</c>
/// three, <c>110xxxxx</c> four (each: up to three literal bytes, then a copy from the output), <c>111xxxxx</c> up to <c>0xFB</c> a run of
/// 4-112 literal bytes, <c>0xFC</c>-<c>0xFF</c> the end (with up to three literal bytes). A literal that would read past the input, or a
/// copy that starts before the output, stops short instead of failing; an opcode in the input's last byte is not read. One pass into
/// a growing array: the former decoder found its commands by reflecting over the assembly on every call and built a List of bytes,
/// which made it the largest cost of a park start in the browser (docs/BROWSER-PERFORMANCE.md).
/// </summary>
internal sealed class Refpack
{
	private readonly byte[] data;

	public Refpack( byte[] data )
	{
		this.data = data;
	}

	public byte[] Decompress()
	{
		// The header is read big-endian: the bytes are 10 FB.
		if ( data.Length < 2 || data[1] != 0xFB || data[0] != 0x10 )
			throw new Exception( "Data was not compressed using refpack (header does not match) - possibly corrupted?" );

		// The 24-bit decompressed size only sizes the first buffer, at most 16 MiB (a corrupt size cannot make it huge); the buffer
		// grows as the commands need, and they decide the output.
		var declared = data.Length >= 5 ? (data[2] << 16) | (data[3] << 8) | data[4] : 0;
		var output = new byte[(int)Math.Min( Math.Clamp( declared, 16L, data.Length * 128L + 1024 ), 16L * 1024 * 1024 )];
		var count = 0;

		void Grow( int needed )
		{
			if ( count + needed <= output.Length )
				return;
			Array.Resize( ref output, Math.Max( output.Length * 2, count + needed ) );
		}

		void Literal( int start, int length )
		{
			var available = Math.Min( length, data.Length - start );
			if ( available <= 0 )
				return;
			Grow( available );
			Array.Copy( data, start, output, count, available );
			count += available;
		}

		void Copy( long distance, int length )
		{
			var from = count - distance;
			if ( from < 0 || length <= 0 )
				return;
			Grow( length );
			// Byte by byte: a copy may overlap the bytes it is writing (a distance shorter than its length repeats them).
			for ( var index = 0; index < length; index++ )
				output[count + index] = output[from + index];
			count += length;
		}

		long position = 5;
		while ( true )
		{
			// The former decoder read the opcode first and stopped once that read reached the end, so a last byte is not executed.
			if ( position >= data.Length - 1 )
				break;
			var offset = (int)position;
			var opcode = data[offset];
			int length;
			var stop = false;
			if ( (opcode & 0x80) == 0 )
			{
				length = 2;
				var literals = opcode & 0x03;
				var copyLength = ((opcode & 0x1C) >> 2) + 3;
				var distance = ((opcode & 0x60) << 3) + data[offset + 1] + 1;
				Literal( offset + length, literals );
				Copy( distance, copyLength );
				position = offset + length + literals;
			}
			else if ( (opcode & 0xC0) == 0x80 )
			{
				length = 3;
				var literals = (data[offset + 1] & 0xC0) >> 6;
				var copyLength = (opcode & 0x3F) + 4;
				var distance = ((data[offset + 1] & 0x3F) << 8) + data[offset + 2] + 1;
				Literal( offset + length, literals );
				Copy( distance, copyLength );
				position = offset + length + literals;
			}
			else if ( (opcode & 0xE0) == 0xC0 )
			{
				length = 4;
				var literals = opcode & 0x03;
				var copyLength = ((opcode & 0x0C) << 6) + data[offset + 3] + 5;
				var distance = ((opcode & 0x10) << 12) + (data[offset + 1] << 8) + data[offset + 2] + 1;
				Literal( offset + length, literals );
				Copy( distance, copyLength );
				position = offset + length + literals;
			}
			else
			{
				length = 1;
				var run = ((opcode & 0x1F) + 1) << 2;
				var literals = run <= 0x70 ? run : opcode & 0x03;
				stop = run > 0x70;
				Literal( offset + length, literals );
				position = offset + length + literals;
			}
			if ( stop )
				break;
		}
		Array.Resize( ref output, count );
		return output;
	}
}

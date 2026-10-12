using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

namespace OpenTPW;

/// <summary>
/// Minimal PNG encoder for 8-bit RGBA or BGRA pixel buffers.
/// Used for smoke-test captures and editor thumbnails; always writes truecolour with alpha.
/// </summary>
public static class PngWriter
{
	private static readonly byte[] Signature = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
	private static readonly uint[] CrcTable = BuildCrcTable();

	/// <summary>Writes a top-down BGRA buffer (the GPU readback layout) to a PNG file.</summary>
	public static void SaveBgra( string path, ReadOnlySpan<byte> pixels, int width, int height )
	{
		using var stream = File.Create( path );
		Write( stream, pixels, width, height, bgra: true );
	}

	/// <summary>Writes a top-down RGBA (or BGRA when <paramref name="bgra"/> is set) buffer as a PNG.</summary>
	public static void Write( Stream stream, ReadOnlySpan<byte> pixels, int width, int height, bool bgra = false )
	{
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero( width );
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero( height );
		var rowBytes = checked(width * 4);
		if ( pixels.Length != checked(rowBytes * height) )
			throw new ArgumentException( $"Expected {rowBytes * height} bytes for {width}x{height} pixels, got {pixels.Length}.", nameof( pixels ) );

		stream.Write( Signature );

		Span<byte> header = stackalloc byte[13];
		BinaryPrimitives.WriteInt32BigEndian( header, width );
		BinaryPrimitives.WriteInt32BigEndian( header[4..], height );
		header[8] = 8; // bit depth
		header[9] = 6; // colour type: truecolour with alpha
		header[10] = 0; // deflate
		header[11] = 0; // adaptive filtering
		header[12] = 0; // no interlace
		WriteChunk( stream, "IHDR", header );

		using var compressed = new MemoryStream();
		using ( var zlib = new ZLibStream( compressed, CompressionLevel.Optimal, leaveOpen: true ) )
		{
			// Each scanline starts with filter type 0 (none).
			var row = new byte[1 + rowBytes];
			for ( var y = 0; y < height; ++y )
			{
				pixels.Slice( y * rowBytes, rowBytes ).CopyTo( row.AsSpan( 1 ) );
				if ( bgra )
					for ( var x = 1; x < row.Length; x += 4 )
						(row[x], row[x + 2]) = (row[x + 2], row[x]);
				zlib.Write( row );
			}
		}
		WriteChunk( stream, "IDAT", compressed.GetBuffer().AsSpan( 0, (int)compressed.Length ) );
		WriteChunk( stream, "IEND", ReadOnlySpan<byte>.Empty );
	}

	private static void WriteChunk( Stream stream, string type, ReadOnlySpan<byte> data )
	{
		Span<byte> buffer = stackalloc byte[4];
		BinaryPrimitives.WriteInt32BigEndian( buffer, data.Length );
		stream.Write( buffer );

		Span<byte> typeBytes = stackalloc byte[4];
		Encoding.ASCII.GetBytes( type, typeBytes );
		stream.Write( typeBytes );
		stream.Write( data );

		// The CRC covers the chunk type and data, not the length.
		var crc = UpdateCrc( UpdateCrc( 0xFFFFFFFFu, typeBytes ), data ) ^ 0xFFFFFFFFu;
		BinaryPrimitives.WriteUInt32BigEndian( buffer, crc );
		stream.Write( buffer );
	}

	private static uint UpdateCrc( uint crc, ReadOnlySpan<byte> data )
	{
		foreach ( var value in data )
			crc = CrcTable[(crc ^ value) & 0xFF] ^ (crc >> 8);
		return crc;
	}

	private static uint[] BuildCrcTable()
	{
		var table = new uint[256];
		for ( uint n = 0; n < table.Length; ++n )
		{
			var c = n;
			for ( var k = 0; k < 8; ++k )
				c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
			table[n] = c;
		}
		return table;
	}
}

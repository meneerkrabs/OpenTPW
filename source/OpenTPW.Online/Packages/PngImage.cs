using System.Buffers.Binary;
using System.IO.Compression;

namespace OpenTPW.Online.Packages;

/// <summary>
/// Structural PNG check (signature, IHDR first, chunk CRCs, IEND last, bounded dimensions) and a
/// small RGBA PNG encoder. Images are never decoded by the server; clients decode them with their
/// own image library after this check.
/// </summary>
public static class PngImage
{
	private static readonly byte[] Signature = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };

	public static (int Width, int Height) Validate( ReadOnlySpan<byte> png, int maximumDimension )
	{
		if ( png.Length < Signature.Length + 12 + 13 + 12 || !png[..8].SequenceEqual( Signature ) )
			throw new InvalidDataException( "Image is not a PNG file." );
		var offset = 8;
		var first = true;
		var sawEnd = false;
		int width = 0, height = 0;
		var chunks = 0;
		while ( offset < png.Length )
		{
			if ( sawEnd )
				throw new InvalidDataException( "PNG has data after IEND." );
			if ( ++chunks > 4096 || png.Length - offset < 12 )
				throw new InvalidDataException( "PNG chunk structure is invalid." );
			var length = BinaryPrimitives.ReadUInt32BigEndian( png[offset..] );
			if ( length > (uint)(png.Length - offset - 12) )
				throw new InvalidDataException( "PNG chunk length exceeds the file." );
			var type = png.Slice( offset + 4, 4 );
			var data = png.Slice( offset + 8, (int)length );
			var crc = BinaryPrimitives.ReadUInt32BigEndian( png[(offset + 8 + (int)length)..] );
			if ( Crc32( png.Slice( offset + 4, 4 + (int)length ) ) != crc )
				throw new InvalidDataException( "PNG chunk checksum mismatch." );
			var isHeader = type.SequenceEqual( "IHDR"u8 );
			if ( first != isHeader )
				throw new InvalidDataException( "PNG must start with exactly one IHDR chunk." );
			if ( isHeader )
			{
				if ( length != 13 )
					throw new InvalidDataException( "PNG IHDR has the wrong size." );
				width = (int)Math.Min( int.MaxValue, BinaryPrimitives.ReadUInt32BigEndian( data ) );
				height = (int)Math.Min( int.MaxValue, BinaryPrimitives.ReadUInt32BigEndian( data[4..] ) );
				if ( width < 1 || height < 1 || width > maximumDimension || height > maximumDimension )
					throw new InvalidDataException( $"PNG is {width}x{height}; at most {maximumDimension}x{maximumDimension} is allowed." );
			}
			sawEnd = type.SequenceEqual( "IEND"u8 );
			first = false;
			offset += 12 + (int)length;
		}
		if ( !sawEnd )
			throw new InvalidDataException( "PNG has no IEND chunk." );
		return (width, height);
	}

	/// <summary>Encodes 8-bit RGBA pixels (row-major, 4 bytes per pixel).</summary>
	public static byte[] EncodeRgba( int width, int height, ReadOnlySpan<byte> rgba )
	{
		if ( width < 1 || height < 1 || rgba.Length != width * height * 4 )
			throw new ArgumentException( "Pixel buffer does not match the image size." );
		using var output = new MemoryStream();
		output.Write( Signature );
		var header = new byte[13];
		BinaryPrimitives.WriteUInt32BigEndian( header, (uint)width );
		BinaryPrimitives.WriteUInt32BigEndian( header.AsSpan( 4 ), (uint)height );
		header[8] = 8; // bit depth
		header[9] = 6; // RGBA
		WriteChunk( output, "IHDR"u8, header );
		using var compressed = new MemoryStream();
		using ( var zlib = new ZLibStream( compressed, CompressionLevel.Optimal, leaveOpen: true ) )
		{
			for ( var y = 0; y < height; y++ )
			{
				zlib.WriteByte( 0 ); // filter: none
				zlib.Write( rgba.Slice( y * width * 4, width * 4 ) );
			}
		}
		WriteChunk( output, "IDAT"u8, compressed.ToArray() );
		WriteChunk( output, "IEND"u8, ReadOnlySpan<byte>.Empty );
		return output.ToArray();
	}

	private static readonly uint[] CrcTable = Enumerable.Range( 0, 256 ).Select( index =>
	{
		var value = (uint)index;
		for ( var bit = 0; bit < 8; bit++ )
			value = (value & 1) != 0 ? 0xEDB88320u ^ (value >> 1) : value >> 1;
		return value;
	} ).ToArray();

	/// <summary>CRC-32 as defined by the PNG specification (ISO 3309 polynomial).</summary>
	public static uint Crc32( ReadOnlySpan<byte> data )
	{
		var crc = 0xFFFFFFFFu;
		foreach ( var value in data )
			crc = CrcTable[(crc ^ value) & 0xFF] ^ (crc >> 8);
		return crc ^ 0xFFFFFFFFu;
	}

	private static void WriteChunk( Stream output, ReadOnlySpan<byte> type, ReadOnlySpan<byte> data )
	{
		Span<byte> word = stackalloc byte[4];
		BinaryPrimitives.WriteUInt32BigEndian( word, (uint)data.Length );
		output.Write( word );
		var crcInput = new byte[4 + data.Length];
		type.CopyTo( crcInput );
		data.CopyTo( crcInput.AsSpan( 4 ) );
		output.Write( crcInput );
		BinaryPrimitives.WriteUInt32BigEndian( word, Crc32( crcInput ) );
		output.Write( word );
	}
}

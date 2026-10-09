using System.Buffers.Binary;

namespace OpenTPW;

public sealed record FontGlyph( char Character, int Width, int Height, sbyte OffsetX, sbyte OffsetY, short Advance, byte Encoding, byte[] Coverage );

public sealed class FontFile : BaseFormat
{
	public const int MaximumFileBytes = 16 * 1024 * 1024;
	public const int MaximumGlyphs = 4096;
	public const int MaximumGlyphPixels = 1024 * 1024;
	public const int MaximumTotalPixels = 16 * 1024 * 1024;
	public byte HeaderWidthHint { get; private set; }
	public byte HeaderHeightHint { get; private set; }
	public IReadOnlyList<FontGlyph> Glyphs { get; private set; } = Array.Empty<FontGlyph>();

	public FontFile( string path ) => ReadFromFile( path );
	public FontFile( Stream stream ) => ReadFromStream( stream );

	protected override void ReadFromStream( Stream stream )
	{
		ArgumentNullException.ThrowIfNull( stream );
		using var input = new MemoryStream();
		var readBuffer = new byte[8192];
		while ( true )
		{
			var count = stream.Read( readBuffer, 0, (int)Math.Min( readBuffer.Length, MaximumFileBytes - input.Length + 1 ) );
			if ( count == 0 )
				break;
			if ( count > MaximumFileBytes - input.Length )
				throw new InvalidDataException( "BF4 exceeds the input byte limit." );
			input.Write( readBuffer, 0, count );
		}
		var data = input.ToArray();
		if ( data.Length < 8 || !data.AsSpan( 0, 4 ).SequenceEqual( "F4FB"u8 ) )
			throw new InvalidDataException( "BF4 header is missing or truncated." );
		HeaderWidthHint = data[4];
		HeaderHeightHint = data[5];
		var glyphCount = BinaryPrimitives.ReadUInt16LittleEndian( data.AsSpan( 6 ) );
		var tableEnd = 8 + glyphCount * 4;
		if ( glyphCount > MaximumGlyphs || tableEnd > data.Length )
			throw new InvalidDataException( "BF4 glyph table is truncated or exceeds its entry limit." );
		var glyphs = new List<FontGlyph>( glyphCount );
		var totalPixels = 0;
		for ( var index = 0; index < glyphCount; index++ )
		{
			var offset = BinaryPrimitives.ReadUInt32LittleEndian( data.AsSpan( 8 + index * 4 ) );
			if ( offset < tableEnd || offset > data.Length - 24 )
				throw new InvalidDataException( $"BF4 glyph {index} has an invalid entry offset." );
			var entry = data.AsSpan( (int)offset, 24 );
			var dataSize = BinaryPrimitives.ReadUInt32LittleEndian( entry[4..] );
			var decodedSize = BinaryPrimitives.ReadUInt32LittleEndian( entry[8..] );
			var width = BinaryPrimitives.ReadUInt16LittleEndian( entry[16..] );
			var height = BinaryPrimitives.ReadUInt16LittleEndian( entry[18..] );
			var pixels = (long)width * height;
			if ( pixels > MaximumGlyphPixels || pixels > MaximumTotalPixels - totalPixels )
				throw new InvalidDataException( $"BF4 glyph {index} exceeds the decoded pixel limit." );
			if ( decodedSize != (pixels + 1) / 2 || dataSize > data.Length - (long)offset - 24 )
				throw new InvalidDataException( $"BF4 glyph {index} has inconsistent data sizes." );
			totalPixels += (int)pixels;
			var source = data.AsSpan( (int)offset + 24, (int)dataSize );
			var coverage = DecodeCoverage( source, (int)pixels, entry[12] );
			glyphs.Add( new FontGlyph( (char)BinaryPrimitives.ReadUInt16LittleEndian( entry ), width, height,
				unchecked((sbyte)entry[20]), unchecked((sbyte)entry[21]), BinaryPrimitives.ReadInt16LittleEndian( entry[22..] ), entry[12], coverage ) );
		}
		Glyphs = glyphs.AsReadOnly();
	}

	private static byte[] DecodeCoverage( ReadOnlySpan<byte> source, int pixels, byte encoding )
	{
		if ( encoding > 2 )
			throw new NotSupportedException( $"Unsupported BF4 glyph encoding {encoding}." );
		var coverage = new byte[pixels];
		if ( pixels == 0 )
			return coverage;
		if ( encoding != 1 )
		{
			var samplesPerByte = encoding == 0 ? 2 : 8;
			if ( (pixels + samplesPerByte - 1) / samplesPerByte > source.Length )
				throw new InvalidDataException( "BF4 glyph samples are truncated." );
			for ( var sample = 0; sample < pixels; sample++ )
				coverage[sample] = encoding == 0
					? (byte)((source[sample / 2] >> (sample % 2 == 0 ? 4 : 0)) & 15)
					: (byte)(((source[sample / 8] >> (7 - sample % 8)) & 1) * 15);
			return coverage;
		}
		var nibbleIndex = 0;
		var written = 0;
		while ( true )
		{
			var value = ReadNibble( source, ref nibbleIndex );
			var count = 1;
			if ( value == 0 )
			{
				count = ReadNibble( source, ref nibbleIndex );
				if ( count == 0 )
					break;
				value = ReadNibble( source, ref nibbleIndex );
			}
			if ( count > pixels + pixels % 2 - written )
				throw new InvalidDataException( "BF4 RLE expands beyond the glyph sample capacity." );
			for ( var repeat = 0; repeat < count; repeat++ )
			{
				if ( written < pixels )
					coverage[written] = (byte)value;
				else if ( value != 0 )
					throw new InvalidDataException( "BF4 odd-pixel padding must be zero." );
				written++;
			}
		}
		if ( written < pixels - 1 )
			throw new InvalidDataException( "BF4 RLE terminates before the final glyph sample." );
		return coverage;
	}

	private static int ReadNibble( ReadOnlySpan<byte> source, ref int index )
	{
		if ( index / 2 >= source.Length )
			throw new InvalidDataException( "BF4 RLE command or terminator is truncated." );
		var value = (source[index / 2] >> (index % 2 == 0 ? 4 : 0)) & 15;
		index++;
		return value;
	}
}

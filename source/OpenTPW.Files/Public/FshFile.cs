using System.Buffers.Binary;
using System.Text;

namespace OpenTPW;

/// <summary>Palette record codes that occur in the Theme Park Inc corpus (docs/FSH.md).</summary>
public enum FshPaletteFormat : byte
{
	/// <summary>3 bytes per entry, stored R, G, B (checked against the shipped TGA thumbnails).</summary>
	Rgb24 = 0x24,
	/// <summary>4 bytes per entry, stored B, G, R, A (checked against the shipped TGA thumbnails, alpha included).</summary>
	Bgra32 = 0x2A,
	/// <summary>2 bytes per entry, little-endian A1 R5 G5 B5 (one corpus file; see docs/FSH.md).</summary>
	Argb1555 = 0x2D,
}

/// <summary>One decoded 8-bit palette image of an <see cref="FshFile"/>.</summary>
/// <param name="Tag">Four-character directory tag (NUL padding removed).</param>
/// <param name="Compressed">True for record code 0xFB (0x7B with the RefPack flag 0x80).</param>
/// <param name="CenterX">Raw u16 at record offset 8 (EA "center X"); 0 throughout the corpus.</param>
/// <param name="CenterY">Raw u16 at record offset 10; 0 throughout the corpus.</param>
/// <param name="PositionX">Raw u16 at record offset 12; 0 throughout the corpus.</param>
/// <param name="PositionY">Raw u16 at record offset 14; 0 throughout the corpus.</param>
/// <param name="Indices">Width * Height palette indices, row-major from the top row.</param>
/// <param name="Palette">Palette entries as R, G, B, A bytes (four per entry).</param>
/// <param name="Name">Text of the attached 0x70 name record, or null when the image has none.</param>
/// <param name="Rgba">Width * Height pixels as R, G, B, A bytes, row-major from the top row.</param>
public sealed record FshImage( string Tag, int Width, int Height, bool Compressed, int CenterX, int CenterY, int PositionX, int PositionY,
	FshPaletteFormat PaletteFormat, int PaletteEntries, byte[] Indices, byte[] Palette, string? Name, byte[] Rgba );

/// <summary>
/// EA <c>SHPI</c> shape file (<c>.fsh</c>) as shipped by Theme Park Inc: header <c>SHPI</c>, total size,
/// image count and a four-character directory id, then (tag, offset) pairs. Each image record starts
/// with a code byte and a 24-bit offset to the next attached record (0 = last), width, height and four
/// raw position words. Only the variants that occur in the corpus are accepted: image code 0x7B
/// (8-bit indices) stored raw or RefPack-compressed (0xFB), followed by exactly one palette record
/// (<see cref="FshPaletteFormat"/>) and optionally one 0x70 name record. Anything else is rejected.
/// See docs/FSH.md.
/// </summary>
public sealed class FshFile : BaseFormat
{
	public const int MaximumFileBytes = 16 * 1024 * 1024;
	public const int MaximumImages = 256;
	public const int MaximumDimension = 4096;
	public const int MaximumTotalPixels = 16 * 1024 * 1024;
	public const int MaximumAttachments = 16;
	public const int HeaderBytes = 16;
	public const int RecordHeaderBytes = 16;
	public const byte IndexedImageCode = 0x7B;
	public const byte CompressedFlag = 0x80;
	public const byte NameCode = 0x70;

	/// <summary>Four-character directory id at offset 12 (<c>G231</c> in every Theme Park Inc file).</summary>
	public string Id { get; private set; } = "";
	public IReadOnlyList<FshImage> Images { get; private set; } = Array.Empty<FshImage>();

	public FshFile( string path ) => ReadFromFile( path );
	public FshFile( Stream stream ) => ReadFromStream( stream );

	protected override void ReadFromStream( Stream stream )
	{
		ArgumentNullException.ThrowIfNull( stream );
		var data = ReadBounded( stream );
		if ( data.Length < HeaderBytes || !data.AsSpan( 0, 4 ).SequenceEqual( "SHPI"u8 ) )
			throw new InvalidDataException( "FSH is not an SHPI shape file or its header is truncated." );
		var totalSize = BinaryPrimitives.ReadUInt32LittleEndian( data.AsSpan( 4 ) );
		if ( totalSize != data.Length )
			throw new InvalidDataException( $"FSH header size {totalSize} does not match the {data.Length}-byte input." );
		var count = BinaryPrimitives.ReadUInt32LittleEndian( data.AsSpan( 8 ) );
		if ( count > MaximumImages )
			throw new InvalidDataException( $"FSH image count {count} exceeds the limit of {MaximumImages}." );
		var directoryEnd = HeaderBytes + 8 * (int)count;
		if ( directoryEnd > data.Length )
			throw new InvalidDataException( "FSH directory is truncated." );
		Id = Encoding.Latin1.GetString( data, 12, 4 );

		var entries = new (string Tag, int Offset)[count];
		for ( var index = 0; index < count; index++ )
		{
			var entry = HeaderBytes + 8 * index;
			var offset = BinaryPrimitives.ReadUInt32LittleEndian( data.AsSpan( entry + 4 ) );
			if ( offset < directoryEnd || offset > data.Length - RecordHeaderBytes )
				throw new InvalidDataException( $"FSH image {index} offset {offset} is outside the file." );
			entries[index] = (Encoding.Latin1.GetString( data, entry, 4 ).TrimEnd( '\0' ), (int)offset);
		}
		// Each image's record chain is bounded by the next image (in file order) or the end of the file.
		var starts = entries.Select( entry => entry.Offset ).Distinct().Order().ToArray();
		if ( starts.Length != entries.Length )
			throw new InvalidDataException( "FSH directory lists the same image offset twice." );

		// Dimensions and the aggregate pixel budget are checked for every image before any pixel allocation.
		var totalPixels = 0L;
		foreach ( var (tag, offset) in entries )
		{
			var width = BinaryPrimitives.ReadUInt16LittleEndian( data.AsSpan( offset + 4 ) );
			var height = BinaryPrimitives.ReadUInt16LittleEndian( data.AsSpan( offset + 6 ) );
			if ( width == 0 || height == 0 || width > MaximumDimension || height > MaximumDimension )
				throw new InvalidDataException( $"FSH image '{tag}' has invalid dimensions {width}x{height}." );
			totalPixels += width * height;
			if ( totalPixels > MaximumTotalPixels )
				throw new InvalidDataException( "FSH images exceed the decoded pixel limit." );
		}
		var images = new List<FshImage>( entries.Length );
		foreach ( var (tag, offset) in entries )
		{
			var next = Array.IndexOf( starts, offset ) + 1;
			var limit = next < starts.Length ? starts[next] : data.Length;
			images.Add( ReadImage( data, tag, offset, limit ) );
		}
		Images = images.AsReadOnly();
	}

	private static FshImage ReadImage( byte[] data, string tag, int offset, int limit )
	{
		var record = ReadRecordHeader( data, offset, limit, out var code, out var end );
		if ( (code & ~CompressedFlag) != IndexedImageCode )
			throw new NotSupportedException( $"FSH image '{tag}' has record code 0x{code:X2}; only 0x7B and 0xFB (8-bit palette) are supported." );
		var width = BinaryPrimitives.ReadUInt16LittleEndian( record[4..] );
		var height = BinaryPrimitives.ReadUInt16LittleEndian( record[6..] );
		var pixels = width * height;

		var body = data.AsSpan( offset + RecordHeaderBytes, end - offset - RecordHeaderBytes );
		var compressed = (code & CompressedFlag) != 0;
		var indices = compressed ? Decompress( body, pixels, tag ) : ReadRaw( body, pixels, tag );

		FshPaletteFormat? paletteFormat = null;
		byte[] palette = Array.Empty<byte>();
		string? name = null;
		var attachments = 0;
		var position = end;
		while ( position < limit )
		{
			if ( ++attachments > MaximumAttachments )
				throw new InvalidDataException( $"FSH image '{tag}' has more than {MaximumAttachments} attached records." );
			ReadRecordHeader( data, position, limit, out var attachedCode, out var attachedEnd );
			switch ( attachedCode )
			{
				case (byte)FshPaletteFormat.Rgb24 or (byte)FshPaletteFormat.Bgra32 or (byte)FshPaletteFormat.Argb1555:
					if ( paletteFormat != null )
						throw new InvalidDataException( $"FSH image '{tag}' has more than one palette record." );
					paletteFormat = (FshPaletteFormat)attachedCode;
					palette = ReadPalette( data.AsSpan( position, attachedEnd - position ), paletteFormat.Value, tag );
					break;
				case NameCode:
					if ( name != null )
						throw new InvalidDataException( $"FSH image '{tag}' has more than one name record." );
					var text = data.AsSpan( position + 4, attachedEnd - position - 4 );
					var terminator = text.IndexOf( (byte)0 );
					if ( terminator < 0 )
						throw new InvalidDataException( $"FSH image '{tag}' name record is not NUL-terminated." );
					name = Encoding.Latin1.GetString( text[..terminator] );
					break;
				default:
					throw new NotSupportedException( $"FSH image '{tag}' has an unsupported attached record code 0x{attachedCode:X2}." );
			}
			position = attachedEnd;
		}
		if ( paletteFormat == null )
			throw new NotSupportedException( $"FSH image '{tag}' has no palette record." );

		var entries = palette.Length / 4;
		var rgba = new byte[pixels * 4];
		for ( var index = 0; index < pixels; index++ )
		{
			var value = indices[index];
			if ( value >= entries )
				throw new InvalidDataException( $"FSH image '{tag}' uses palette index {value} of a {entries}-entry palette." );
			palette.AsSpan( value * 4, 4 ).CopyTo( rgba.AsSpan( index * 4 ) );
		}
		return new FshImage( tag, width, height, compressed,
			BinaryPrimitives.ReadUInt16LittleEndian( record[8..] ), BinaryPrimitives.ReadUInt16LittleEndian( record[10..] ),
			BinaryPrimitives.ReadUInt16LittleEndian( record[12..] ), BinaryPrimitives.ReadUInt16LittleEndian( record[14..] ),
			paletteFormat.Value, entries, indices, palette, name, rgba );
	}

	/// <summary>
	/// Reads a record's code and 24-bit next-record offset. The record ends at that offset, or at
	/// <paramref name="limit"/> when the offset is 0 (last record of the chain).
	/// </summary>
	private static ReadOnlySpan<byte> ReadRecordHeader( byte[] data, int offset, int limit, out byte code, out int end )
	{
		if ( limit - offset < 4 )
			throw new InvalidDataException( $"FSH record at {offset} is truncated." );
		code = data[offset];
		var next = data[offset + 1] | data[offset + 2] << 8 | data[offset + 3] << 16;
		end = next == 0 ? limit : offset + next;
		var minimum = code == NameCode ? 4 : RecordHeaderBytes;
		if ( end > limit || end - offset < minimum )
			throw new InvalidDataException( $"FSH record 0x{code:X2} at {offset} has an invalid next-record offset {next}." );
		return data.AsSpan( offset, end - offset );
	}

	private static byte[] ReadRaw( ReadOnlySpan<byte> body, int pixels, string tag )
	{
		if ( body.Length < pixels )
			throw new InvalidDataException( $"FSH image '{tag}' pixel block is truncated." );
		return body[..pixels].ToArray();
	}

	/// <summary>
	/// RefPack body with the 5-byte header <c>10 FB</c> + 24-bit big-endian decoded size, the only header
	/// form in the corpus. The decoded size is the pixel count rounded up to a multiple of 16; the rows are
	/// not padded, only the end. Bytes after the stop command (alignment padding, not always zero) are ignored.
	/// </summary>
	private static byte[] Decompress( ReadOnlySpan<byte> body, int pixels, string tag )
	{
		if ( body.Length < 5 || body[1] != 0xFB )
			throw new InvalidDataException( $"FSH image '{tag}' compressed block has no RefPack header." );
		if ( body[0] != 0x10 )
			throw new NotSupportedException( $"FSH image '{tag}' uses RefPack header flags 0x{body[0]:X2}; only 0x10 is supported." );
		var size = body[2] << 16 | body[3] << 8 | body[4];
		var padded = (pixels + 15) & ~15;
		if ( size != padded )
			throw new InvalidDataException( $"FSH image '{tag}' RefPack size {size} does not match its {pixels} pixels padded to {padded}." );
		return TreCompression.Refpack( body[5..].ToArray(), padded )[..pixels];
	}

	/// <summary>
	/// Palette record: code, next offset, u16 entry count, u16 height (1 in the corpus), four raw words,
	/// then the entries. Trailing alignment bytes before the next record are ignored.
	/// </summary>
	private static byte[] ReadPalette( ReadOnlySpan<byte> record, FshPaletteFormat format, string tag )
	{
		var entries = BinaryPrimitives.ReadUInt16LittleEndian( record[4..] );
		var height = BinaryPrimitives.ReadUInt16LittleEndian( record[6..] );
		if ( entries == 0 || entries > 256 || height != 1 )
			throw new InvalidDataException( $"FSH image '{tag}' palette is {entries}x{height}; expected 1..256 entries in one row." );
		var stride = format switch { FshPaletteFormat.Rgb24 => 3, FshPaletteFormat.Bgra32 => 4, _ => 2 };
		var source = record[RecordHeaderBytes..];
		if ( source.Length < entries * stride )
			throw new InvalidDataException( $"FSH image '{tag}' palette is truncated." );
		var palette = new byte[entries * 4];
		for ( var index = 0; index < entries; index++ )
		{
			var entry = source.Slice( index * stride, stride );
			var target = palette.AsSpan( index * 4, 4 );
			switch ( format )
			{
				case FshPaletteFormat.Rgb24:
					// [APPROX:COMPAT-014] No alpha is stored, so every entry is opaque; no colour key is applied.
					target[0] = entry[0];
					target[1] = entry[1];
					target[2] = entry[2];
					target[3] = 255;
					break;
				case FshPaletteFormat.Bgra32:
					target[0] = entry[2];
					target[1] = entry[1];
					target[2] = entry[0];
					target[3] = entry[3];
					break;
				default:
					// [APPROX:COMPAT-015] A1R5G5B5 with bit 15 as alpha; one corpus file, every entry has bit 15 set.
					var value = BinaryPrimitives.ReadUInt16LittleEndian( entry );
					target[0] = Expand5( value >> 10 );
					target[1] = Expand5( value >> 5 );
					target[2] = Expand5( value );
					target[3] = (value & 0x8000) != 0 ? (byte)255 : (byte)0;
					break;
			}
		}
		return palette;
	}

	private static byte Expand5( int value )
	{
		value &= 31;
		return (byte)(value << 3 | value >> 2);
	}

	private static byte[] ReadBounded( Stream stream )
	{
		using var input = new MemoryStream();
		var readBuffer = new byte[8192];
		while ( true )
		{
			var count = stream.Read( readBuffer, 0, (int)Math.Min( readBuffer.Length, MaximumFileBytes - input.Length + 1 ) );
			if ( count == 0 )
				break;
			if ( count > MaximumFileBytes - input.Length )
				throw new InvalidDataException( "FSH exceeds the input byte limit." );
			input.Write( readBuffer, 0, count );
		}
		return input.ToArray();
	}
}

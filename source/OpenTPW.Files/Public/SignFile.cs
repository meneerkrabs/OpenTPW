using System.Buffers.Binary;
using System.Text;

namespace OpenTPW;

/// <summary>
/// The Windows GDI <c>LOGFONTA</c> stored in a sign text slot (60 bytes). Field meanings are the
/// documented Win32 ones; the observed values (charset 1 = DEFAULT_CHARSET, out precision 4 =
/// OUT_TT_PRECIS, quality 4 = ANTIALIASED_QUALITY) confirm the layout.
/// </summary>
public readonly record struct SignLogFont( int Height, int Width, int Escapement, int Orientation, int Weight,
	bool Italic, bool Underline, bool StrikeOut, byte CharSet, byte OutPrecision, byte ClipPrecision, byte Quality, byte PitchAndFamily, string FaceName );

/// <summary>
/// One text slot of a sign: 392 bytes of face name, font file, two words and a LOGFONTA, then an
/// 11-word block (436 bytes in all), in the order the Mac loader reads them (0x100AA5A0, 0x100AA8A4).
/// Verified: <see cref="FaceName"/>/<see cref="FontFileName"/> name a font in <c>fonts.wad</c> and
/// <see cref="LogFont"/> is a LOGFONTA whose face name equals <see cref="FaceName"/>. Observed but
/// unconfirmed: <see cref="HorizontalScalePercent"/> (85..141, mostly ~100) and <see cref="OffsetY"/>
/// (first slot -61..32, second slot 40..147). <see cref="Effects"/> holds the 11 words of the second
/// block raw; the compositor (0x100ABF14) reads some of them as effect parameters.
/// </summary>
public sealed record SignTextSlot( string FaceName, string FontFileName, int HorizontalScalePercent, int OffsetY,
	SignLogFont LogFont, IReadOnlyList<uint> Effects )
{
	/// <summary>Font pixel height as GDI would select it: negative LOGFONT height = em height.</summary>
	public int EmHeightPixels => LogFont.Height < 0 ? -LogFont.Height : LogFont.Height;
}

/// <summary>A text line's colour block: four bytes (red, green, blue, and a fourth value the compositor passes first to its colour blit) and four raw words.</summary>
public sealed record SignColourBlock( byte R, byte G, byte B, byte Fourth, IReadOnlyList<uint> Words );

/// <summary>An uncompressed bitmap as <c>Bitmap::load</c> reads it: u32 width, u32 height, u32 bytes per pixel, then the pixels.</summary>
public sealed record SignBitmap( int Width, int Height, int BytesPerPixel, byte[] Pixels );

/// <summary>
/// Read-only reader for the original sign description files (<c>*.sgn</c>, 84 members in the
/// gates/sign1 feature WADs, ride WADs and <c>lobby.wad</c>; see docs/COMPATIBILITY.md). The layout
/// follows the Mac loader 0x100ABA40 (little-endian; the Mac swaps every word): u32 version (100 or
/// 101), u32 flag, u8 board flag, u32 colour mode of each line (0, 1 or 2), the two
/// <see cref="SignTextSlot"/> records, for each line with a non-zero colour mode a 20-byte
/// <see cref="SignColourBlock"/>, two <see cref="SignBitmap"/>s, and when the board flag is set a board
/// image (a plain bitmap in version 100, a wavelet stream otherwise, kept raw in <see cref="BoardWavelet"/>).
/// All 88 signs of the Mac data parse to their exact length.
/// </summary>
public sealed class SignFile
{
	public const int HeaderBytes = 17;
	public const int SlotBytes = 436;
	public const int SlotCount = 2;
	public const int ColourBlockBytes = 20;
	public const int MaximumFileBytes = 4 * 1024 * 1024;
	public const int MaximumBitmapSide = 4096;

	public uint Version { get; }
	public uint HeaderFlag { get; }
	public byte BoardFlag { get; }
	public IReadOnlyList<uint> ColourModes { get; }
	public IReadOnlyList<SignTextSlot> Slots { get; }
	public IReadOnlyList<SignColourBlock?> ColourBlocks { get; }
	public IReadOnlyList<SignBitmap> Fills { get; }
	public SignBitmap? Board { get; }
	public byte[]? BoardWavelet { get; }

	public SignFile( byte[] data )
	{
		ArgumentNullException.ThrowIfNull( data );
		if ( data.Length > MaximumFileBytes )
			throw new InvalidDataException( "Sign file exceeds the size limit." );
		if ( data.Length < HeaderBytes + SlotCount * SlotBytes )
			throw new InvalidDataException( $"Sign file is {data.Length} bytes; the header and two text slots need {HeaderBytes + SlotCount * SlotBytes}." );
		Version = U32( data, 0 );
		if ( Version is not (100 or 101) )
			throw new InvalidDataException( $"Unknown sign file version {Version} (100 and 101 are known)." );
		HeaderFlag = U32( data, 4 );
		BoardFlag = data[8];
		ColourModes = new[] { U32( data, 9 ), U32( data, 13 ) };
		var slots = new List<SignTextSlot>();
		for ( var i = 0; i < SlotCount; i++ )
			slots.Add( ReadSlot( data, HeaderBytes + i * SlotBytes, i ) );
		Slots = slots;
		var position = HeaderBytes + SlotCount * SlotBytes;
		var blocks = new SignColourBlock?[SlotCount];
		for ( var i = 0; i < SlotCount; i++ )
		{
			if ( ColourModes[i] == 0 )
				continue;
			Require( data, position, ColourBlockBytes, $"colour block {i}" );
			blocks[i] = new SignColourBlock( data[position], data[position + 1], data[position + 2], data[position + 3],
				new[] { U32( data, position + 4 ), U32( data, position + 8 ), U32( data, position + 12 ), U32( data, position + 16 ) } );
			position += ColourBlockBytes;
		}
		ColourBlocks = blocks;
		Fills = new[] { ReadBitmap( data, ref position, "first fill bitmap" ), ReadBitmap( data, ref position, "second fill bitmap" ) };
		if ( BoardFlag != 0 )
		{
			if ( Version == 100 )
				Board = ReadBitmap( data, ref position, "board bitmap" );
			else
			{
				BoardWavelet = data.AsSpan( position ).ToArray();
				position = data.Length;
			}
		}
		if ( position != data.Length )
			throw new InvalidDataException( $"Sign file has {data.Length - position} bytes after its last block." );
	}

	public SignFile( Stream stream ) : this( ReadAll( stream ) ) { }

	private static SignTextSlot ReadSlot( byte[] data, int offset, int index )
	{
		var faceName = ReadString( data, offset, 64 );
		var fileName = ReadString( data, offset + 64, 260 );
		var logFont = new SignLogFont(
			I32( data, offset + 332 ), I32( data, offset + 336 ), I32( data, offset + 340 ), I32( data, offset + 344 ), I32( data, offset + 348 ),
			data[offset + 352] != 0, data[offset + 353] != 0, data[offset + 354] != 0,
			data[offset + 355], data[offset + 356], data[offset + 357], data[offset + 358], data[offset + 359],
			ReadString( data, offset + 360, 32 ) );
		if ( faceName.Length == 0 || !string.Equals( faceName, logFont.FaceName, StringComparison.Ordinal ) )
			throw new InvalidDataException( $"Sign text slot {index} face '{faceName}' does not match its LOGFONT face '{logFont.FaceName}'." );
		if ( !fileName.EndsWith( ".ttf", StringComparison.OrdinalIgnoreCase ) )
			throw new InvalidDataException( $"Sign text slot {index} font file '{fileName}' is not a .TTF name." );
		var effects = new uint[11];
		for ( var i = 0; i < effects.Length; i++ )
			effects[i] = U32( data, offset + 392 + i * 4 );
		return new SignTextSlot( faceName, fileName, I32( data, offset + 324 ), I32( data, offset + 328 ), logFont, effects );
	}

	private static SignBitmap ReadBitmap( byte[] data, ref int position, string what )
	{
		Require( data, position, 12, what );
		var width = U32( data, position );
		var height = U32( data, position + 4 );
		var bytes = U32( data, position + 8 );
		if ( width > MaximumBitmapSide || height > MaximumBitmapSide || bytes > 4 )
			throw new InvalidDataException( $"Sign {what} is {width}x{height} with {bytes} bytes per pixel." );
		var length = (int)(width * height * bytes);
		Require( data, position + 12, length, what );
		var bitmap = new SignBitmap( (int)width, (int)height, (int)bytes, data.AsSpan( position + 12, length ).ToArray() );
		position += 12 + length;
		return bitmap;
	}

	private static void Require( byte[] data, int position, int length, string what )
	{
		if ( position < 0 || length < 0 || position > data.Length - length )
			throw new InvalidDataException( $"Sign file ends inside its {what}." );
	}

	private static byte[] ReadAll( Stream stream )
	{
		ArgumentNullException.ThrowIfNull( stream );
		using var memory = new MemoryStream();
		var buffer = new byte[16384];
		int read;
		while ( (read = stream.Read( buffer, 0, buffer.Length )) > 0 )
		{
			if ( memory.Length + read > MaximumFileBytes )
				throw new InvalidDataException( "Sign file exceeds the size limit." );
			memory.Write( buffer, 0, read );
		}
		return memory.ToArray();
	}

	private static string ReadString( byte[] data, int offset, int length )
	{
		var span = data.AsSpan( offset, length );
		var end = span.IndexOf( (byte)0 );
		return Encoding.Latin1.GetString( end < 0 ? span : span[..end] );
	}

	private static uint U32( byte[] data, int offset ) => BinaryPrimitives.ReadUInt32LittleEndian( data.AsSpan( offset, 4 ) );
	private static int I32( byte[] data, int offset ) => BinaryPrimitives.ReadInt32LittleEndian( data.AsSpan( offset, 4 ) );
}

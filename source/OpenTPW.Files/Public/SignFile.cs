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
/// One text slot of a sign (436 bytes). Verified: <see cref="FaceName"/>/<see cref="FontFileName"/>
/// name a font in <c>fonts.wad</c> and <see cref="LogFont"/> is a LOGFONTA whose face name equals
/// <see cref="FaceName"/>. Observed but unconfirmed: <see cref="HorizontalScalePercent"/> (85..141,
/// mostly ~100) and <see cref="OffsetY"/> (first slot -61..32, second slot 40..147, i.e. upper and
/// lower line of a 256-pixel-high text canvas). <see cref="StyleId"/>, <see cref="EffectWord"/>,
/// <see cref="Parameters"/> (eight floats; the third to fifth look like an RGB colour, the last
/// three like x/y/width) and <see cref="TrailingWord"/> are kept raw.
/// </summary>
public sealed record SignTextSlot( int StyleId, string FaceName, string FontFileName, int HorizontalScalePercent, int OffsetY,
	SignLogFont LogFont, uint EffectWord, IReadOnlyList<float> Parameters, int TrailingWord )
{
	/// <summary>Font pixel height as GDI would select it: negative LOGFONT height = em height.</summary>
	public int EmHeightPixels => LogFont.Height < 0 ? -LogFont.Height : LogFont.Height;
}

/// <summary>
/// Read-only reader for the original sign description files (<c>*.sgn</c>, 84 members in the
/// gates/sign1 feature WADs, ride WADs and <c>lobby.wad</c>; see docs/COMPATIBILITY.md). Layout
/// (little-endian, packed): u32 version (100 or 101), u32 flag (0/1), u8 flag (0/1, set when an
/// extra image block follows), u32 opaque count, then two 436-byte <see cref="SignTextSlot"/>
/// records. The remainder (per-slot effect blocks, a 128-wide 32-bit pixel block and an optional
/// extra image) is kept as <see cref="Remainder"/>; its meaning is not decoded.
/// </summary>
public sealed class SignFile
{
	public const int HeaderBytes = 13;
	public const int SlotBytes = 436;
	public const int SlotCount = 2;
	public const int MaximumFileBytes = 4 * 1024 * 1024;

	public uint Version { get; }
	public uint HeaderFlag { get; }
	public byte ExtraImageFlag { get; }
	public uint HeaderCount { get; }
	public IReadOnlyList<SignTextSlot> Slots { get; }
	public byte[] Remainder { get; }

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
		ExtraImageFlag = data[8];
		HeaderCount = U32( data, 9 );
		var slots = new List<SignTextSlot>();
		for ( var i = 0; i < SlotCount; i++ )
			slots.Add( ReadSlot( data, HeaderBytes + i * SlotBytes, i ) );
		Slots = slots;
		Remainder = data.AsSpan( HeaderBytes + SlotCount * SlotBytes ).ToArray();
	}

	public SignFile( Stream stream ) : this( ReadAll( stream ) ) { }

	private static SignTextSlot ReadSlot( byte[] data, int offset, int index )
	{
		var faceName = ReadString( data, offset + 4, 64 );
		var fileName = ReadString( data, offset + 68, 260 );
		var logFont = new SignLogFont(
			I32( data, offset + 336 ), I32( data, offset + 340 ), I32( data, offset + 344 ), I32( data, offset + 348 ), I32( data, offset + 352 ),
			data[offset + 356] != 0, data[offset + 357] != 0, data[offset + 358] != 0,
			data[offset + 359], data[offset + 360], data[offset + 361], data[offset + 362], data[offset + 363],
			ReadString( data, offset + 364, 32 ) );
		if ( faceName.Length == 0 || !string.Equals( faceName, logFont.FaceName, StringComparison.Ordinal ) )
			throw new InvalidDataException( $"Sign text slot {index} face '{faceName}' does not match its LOGFONT face '{logFont.FaceName}'." );
		if ( !fileName.EndsWith( ".ttf", StringComparison.OrdinalIgnoreCase ) )
			throw new InvalidDataException( $"Sign text slot {index} font file '{fileName}' is not a .TTF name." );
		var parameters = new float[8];
		for ( var i = 0; i < parameters.Length; i++ )
			parameters[i] = BinaryPrimitives.ReadSingleLittleEndian( data.AsSpan( offset + 400 + i * 4, 4 ) );
		return new SignTextSlot( I32( data, offset ), faceName, fileName, I32( data, offset + 328 ), I32( data, offset + 332 ),
			logFont, U32( data, offset + 396 ), parameters, I32( data, offset + 432 ) );
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

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
/// Legacy 436-byte view retained for callers and hash-keyed font corrections. Font fields have
/// correct offsets, but StyleId crosses native record boundaries: use SignFile.Effects for styles
/// and material coefficients. Parameters 2..4 are shared lighting coefficients, not RGB.
/// </summary>
public sealed record SignTextSlot( int StyleId, string FaceName, string FontFileName, int HorizontalScalePercent, int OffsetY,
	SignLogFont LogFont, uint EffectWord, IReadOnlyList<float> Parameters, int TrailingWord )
{
	/// <summary>Font pixel height as GDI would select it: negative LOGFONT height = em height.</summary>
	public int EmHeightPixels => LogFont.Height < 0 ? -LogFont.Height : LogFont.Height;
}

/// <summary>Shared channel coefficients consumed by the native relief effect; names describe its arithmetic.</summary>
public readonly record struct SignMaterialCoefficients( float Base, float Diffuse, float WhiteSpecular, float SpecularExponent );

/// <summary>Native 44-byte effect record. Stored extent/origin are overwritten from measured text bounds at runtime.</summary>
public sealed record SignEffect( uint Style, uint MaskWord, IReadOnlyList<float> Parameters, int StoredExtent, int StoredOrigin )
{
	public SignMaterialCoefficients Material => new( Parameters[2], Parameters[3], Parameters[4], Parameters[5] );
}

/// <summary>Optional native 20-byte paint record. Four color bytes precede mask-shaping values; no renderer is implied.</summary>
public sealed record SignPaint( byte R, byte G, byte B, byte A, int MaskWord, float MaskParameter, int OffsetX, int OffsetY );

/// <summary>Native bitmap descriptor and untouched payload. Wavelet payloads remain opaque and are never decompressed here.</summary>
public sealed record SignBitmap( int FileOffset, uint Width, uint Height, uint BytesPerPixel, bool IsWavelet, byte[] Payload );

/// <summary>
/// Bounded metadata reader for original signs. The native layout is a 17-byte header, two
/// 392-byte font / 44-byte effect pairs, optional 20-byte paint records for nonzero styles,
/// two source bitmaps, and an optional raw (version 100) or wavelet (101) image.
/// See docs/reverse/PPC-ui.md for identified native consumers. Legacy slot views remain available.
/// </summary>
public sealed class SignFile
{
	// Legacy font-correction offsets. Native records start at NativeHeaderBytes.
	public const int HeaderBytes = 13;
	public const int SlotBytes = 436;
	public const int SlotCount = 2;
	public const int NativeHeaderBytes = 17;
	public const int FontRecordBytes = 392;
	public const int EffectRecordBytes = 44;
	public const int NativeMetadataBytes = NativeHeaderBytes + SlotCount * (FontRecordBytes + EffectRecordBytes);
	public const int MaximumFileBytes = 4 * 1024 * 1024;

	public uint Version { get; }
	public uint HeaderFlag { get; }
	public byte ExtraImageFlag { get; }
	/// <summary>Legacy name for the first native effect style; this is not a record count.</summary>
	public uint HeaderCount { get; }
	public IReadOnlyList<SignTextSlot> Slots { get; }
	public byte[] Remainder { get; }
	public IReadOnlyList<SignEffect> Effects { get; }
	public IReadOnlyList<SignPaint?> Paints { get; }
	public IReadOnlyList<SignBitmap> SourceImages { get; }
	public SignBitmap? ExtraImage { get; }
	public byte[] UnparsedTail { get; }
	public IReadOnlyList<string> Diagnostics { get; }

	public SignFile( byte[] data )
	{
		ArgumentNullException.ThrowIfNull( data );
		if ( data.Length > MaximumFileBytes )
			throw new InvalidDataException( "Sign file exceeds the size limit." );
		RequireSpan( data, 0, NativeMetadataBytes );
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
		var effects = new List<SignEffect>();
		var paints = new List<SignPaint?>();
		var diagnostics = new List<string>();
		for ( var i = 0; i < SlotCount; i++ )
		{
			var offset = NativeHeaderBytes + i * (FontRecordBytes + EffectRecordBytes) + FontRecordBytes;
			var style = U32( data, 9 + i * 4 );
			var parameters = new float[8];
			for ( var j = 0; j < parameters.Length; j++ )
				parameters[j] = BinaryPrimitives.ReadSingleLittleEndian( data.AsSpan( offset + 4 + j * 4, 4 ) );
			effects.Add( new SignEffect( style, U32( data, offset ), Array.AsReadOnly( parameters ), I32( data, offset + 36 ), I32( data, offset + 40 ) ) );
			if ( style > 2 )
				diagnostics.Add( $"Sign effect {i}: style {style} is unsupported; metadata and payload are preserved." );
			if ( parameters.Any( value => !float.IsFinite( value ) ) )
				diagnostics.Add( $"Sign effect {i}: non-finite parameters are preserved and cannot be rendered." );
		}
		Effects = effects.AsReadOnly();
		var position = NativeMetadataBytes;
		foreach ( var effect in effects )
		{
			if ( effect.Style == 0 )
			{
				paints.Add( null );
				continue;
			}
			RequireSpan( data, position, 20 );
			paints.Add( new SignPaint( data[position], data[position + 1], data[position + 2], data[position + 3],
				I32( data, position + 4 ), BinaryPrimitives.ReadSingleLittleEndian( data.AsSpan( position + 8, 4 ) ), I32( data, position + 12 ), I32( data, position + 16 ) ) );
			if ( !float.IsFinite( paints[^1]!.MaskParameter ) )
				diagnostics.Add( $"Sign paint at {position}: non-finite mask parameter is preserved and cannot be rendered." );
			position += 20;
		}
		Paints = paints.AsReadOnly();
		var images = new List<SignBitmap>();
		for ( var i = 0; i < SlotCount; i++ )
			images.Add( ReadBitmap( data, ref position, false, diagnostics ) );
		SourceImages = images.AsReadOnly();
		if ( ExtraImageFlag != 0 )
			ExtraImage = ReadBitmap( data, ref position, Version == 101, diagnostics );
		UnparsedTail = data.AsSpan( position ).ToArray();
		if ( UnparsedTail.Length != 0 )
			diagnostics.Add( $"Sign has {UnparsedTail.Length} unparsed trailing bytes; preserved without interpretation." );
		Diagnostics = diagnostics.AsReadOnly();
	}

	public SignFile( Stream stream ) : this( ReadAll( stream ) ) { }

	private static SignBitmap ReadBitmap( byte[] data, ref int position, bool wavelet, List<string> diagnostics )
	{
		RequireSpan( data, position, 12 );
		var offset = position;
		var width = U32( data, position );
		var height = U32( data, position + 4 );
		var bytesPerPixel = U32( data, position + 8 );
		// Bound all factors before multiplication, including unsupported formats.
		if ( width == 0 || height == 0 || bytesPerPixel == 0 || width > MaximumFileBytes || height > MaximumFileBytes || bytesPerPixel > MaximumFileBytes
			|| (ulong)width * height > (ulong)MaximumFileBytes / bytesPerPixel )
			throw new InvalidDataException( $"Sign bitmap at {position} has invalid or excessive dimensions." );
		position += 12;
		var length = wavelet ? data.Length - position : (int)((ulong)width * height * bytesPerPixel);
		if ( length == 0 )
			throw new InvalidDataException( "Sign bitmap payload is missing." );
		RequireSpan( data, position, length );
		var payload = data.AsSpan( position, length ).ToArray();
		position += length;
		if ( wavelet )
			diagnostics.Add( $"Sign bitmap at {offset}: wavelet payload is preserved; decoding is unsupported." );
		else if ( bytesPerPixel != 4 )
			diagnostics.Add( $"Sign bitmap at {offset}: {bytesPerPixel}-byte pixels are preserved; color interpretation is unsupported." );
		return new SignBitmap( offset, width, height, bytesPerPixel, wavelet, payload );
	}

	private static void RequireSpan( byte[] data, int offset, int length )
	{
		if ( offset < 0 || length < 0 || offset > data.Length - length )
			throw new InvalidDataException( $"Sign record at {offset} requires {length} bytes; file has {data.Length}." );
	}

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

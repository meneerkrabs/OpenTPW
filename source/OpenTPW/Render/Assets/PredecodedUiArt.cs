using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using StbImageSharp;

namespace OpenTPW;

/// <summary>
/// The predecoded store for the shipped hero interface art: the browser build embeds zlib-compressed RGBA
/// (<c>X.wct.rgba</c>) siblings of the shipped PNGs (docs/WEB.md), so opening a menu inflates pixels instead of
/// running PNG decode. The store holds the decoder's exact output before any keying, which is why both
/// <see cref="UiImages"/> (which keys pink) and <see cref="Texture.DecodeWct"/> (which never keys) can read the
/// same files. Only our own build produces the format (tools/hero-art); original game data, host art and player
/// texture packs keep their PNGs, and nothing tries this format for them.
/// </summary>
// [EXT:texture-pack] predecoded interface art is an OpenTPW build option, not original behaviour.
public static class PredecodedUiArt
{
	/// <summary>File name extension of a predecoded sibling (<c>X.wct.png</c> → <c>X.wct.rgba</c>).</summary>
	public const string Extension = ".rgba";
	/// <summary>First four bytes of every predecoded asset.</summary>
	public const string Magic = "OPRG";
	public const ushort FormatVersion = 1;
	/// <summary>The only payload codec: a zlib stream that inflates to exactly <c>width * height * 4</c> bytes.</summary>
	public const ushort CodecZlib = 1;
	/// <summary>Magic, version, codec, width, height, compressed payload length.</summary>
	public const int HeaderSize = 20;
	/// <summary>Dimension bound of downloaded interface art (the same limit <c>AddTextureFile</c> accepts).</summary>
	public const int MaxDimension = 8192;
	/// <summary>Texel bound of downloaded interface art; it also bounds the loader's exact-size allocation.</summary>
	public const long MaxPixels = 16 * 1024 * 1024;

	/// <summary>
	/// Encodes a shipped-art PNG as a predecoded asset: the decoder's exact RGBA output, compressed with zlib at
	/// <paramref name="level"/>. This is the generator entry (tools/hero-art) and the test oracle; the game itself
	/// only loads.
	/// </summary>
	public static byte[] EncodePng( byte[] png, CompressionLevel level = CompressionLevel.SmallestSize )
	{
		var image = ImageResult.FromMemory( png, ColorComponents.RedGreenBlueAlpha );
		if ( image.Data.Length != checked(image.Width * image.Height * 4) )
			throw new InvalidDataException( "The PNG decoded to an unexpected number of bytes." );
		return Encode( image.Data, image.Width, image.Height, level );
	}

	/// <summary>Encodes unkeyed row-major <paramref name="rgba"/> pixels as a predecoded asset.</summary>
	public static byte[] Encode( byte[] rgba, int width, int height, CompressionLevel level = CompressionLevel.SmallestSize )
	{
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero( width );
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero( height );
		if ( width > MaxDimension || height > MaxDimension || (long)width * height > MaxPixels )
			throw new ArgumentOutOfRangeException( $"{width}x{height} exceeds the {MaxDimension} pixel / {MaxPixels} texel bounds." );
		if ( rgba.Length != checked(width * height * 4) )
			throw new ArgumentException( $"Expected {checked(width * height * 4)} bytes for {width}x{height} pixels, got {rgba.Length}.", nameof( rgba ) );
		var payload = new MemoryStream();
		using ( var zlib = new ZLibStream( payload, level, leaveOpen: true ) )
			zlib.Write( rgba );
		var file = new byte[HeaderSize + payload.Length];
		Encoding.ASCII.GetBytes( Magic, file );
		BinaryPrimitives.WriteUInt16LittleEndian( file.AsSpan( 4 ), FormatVersion );
		BinaryPrimitives.WriteUInt16LittleEndian( file.AsSpan( 6 ), CodecZlib );
		BinaryPrimitives.WriteUInt32LittleEndian( file.AsSpan( 8 ), (uint)width );
		BinaryPrimitives.WriteUInt32LittleEndian( file.AsSpan( 12 ), (uint)height );
		BinaryPrimitives.WriteUInt32LittleEndian( file.AsSpan( 16 ), (uint)payload.Length );
		payload.GetBuffer().AsSpan( 0, (int)payload.Length ).CopyTo( file.AsSpan( HeaderSize ) );
		return file;
	}

	/// <summary>
	/// Loads a predecoded asset. Every check is strict: magic, version, codec, positive dimensions within the
	/// download bounds, the declared compressed length against the actual file length (so no truncated or trailing
	/// bytes get in), and an inflate that produces exactly <c>width * height * 4</c> bytes and no more. The deflate
	/// stream itself only has to deliver those bytes: unused slack inside the declared payload, or a missing zlib
	/// trailer after a complete stream, are not detected (canonicity is not claimed). Failures throw
	/// <see cref="InvalidDataException"/>, which the UI's failed-image handling already catches.
	/// </summary>
	public static (int Width, int Height, byte[] Rgba) Load( byte[] file )
	{
		if ( file.Length < HeaderSize )
			throw new InvalidDataException( $"Predecoded UI asset is {file.Length} bytes, shorter than its {HeaderSize}-byte header." );
		if ( !file.AsSpan( 0, 4 ).SequenceEqual( "OPRG"u8 ) )
			throw new InvalidDataException( "Predecoded UI asset does not start with the OPRG magic." );
		var version = BinaryPrimitives.ReadUInt16LittleEndian( file.AsSpan( 4 ) );
		if ( version != FormatVersion )
			throw new InvalidDataException( $"Predecoded UI asset has format version {version}, expected {FormatVersion}." );
		var codec = BinaryPrimitives.ReadUInt16LittleEndian( file.AsSpan( 6 ) );
		if ( codec != CodecZlib )
			throw new InvalidDataException( $"Predecoded UI asset uses codec {codec}, expected {CodecZlib} (zlib)." );
		var declaredWidth = BinaryPrimitives.ReadUInt32LittleEndian( file.AsSpan( 8 ) );
		var declaredHeight = BinaryPrimitives.ReadUInt32LittleEndian( file.AsSpan( 12 ) );
		// Checked before anything is allocated from the dimensions.
		if ( declaredWidth == 0 || declaredHeight == 0 )
			throw new InvalidDataException( $"Predecoded UI asset has a zero dimension ({declaredWidth}x{declaredHeight})." );
		if ( declaredWidth > MaxDimension || declaredHeight > MaxDimension || (long)declaredWidth * declaredHeight > MaxPixels )
			throw new InvalidDataException( $"Predecoded UI asset is {declaredWidth}x{declaredHeight}, beyond the {MaxDimension} pixel / {MaxPixels} texel bounds." );
		var compressedLength = BinaryPrimitives.ReadUInt32LittleEndian( file.AsSpan( 16 ) );
		// The declared payload length pins the whole file: no truncation, no trailing bytes.
		if ( file.Length != HeaderSize + compressedLength )
			throw new InvalidDataException( $"Predecoded UI asset holds {file.Length - HeaderSize} payload bytes but declares {compressedLength}." );
		var (width, height) = ((int)declaredWidth, (int)declaredHeight);
		var rgba = new byte[width * height * 4];
		using var zlib = new ZLibStream( new MemoryStream( file, HeaderSize, (int)compressedLength, writable: false ), CompressionMode.Decompress );
		var read = 0;
		while ( read < rgba.Length )
		{
			var block = zlib.Read( rgba, read, rgba.Length - read );
			if ( block <= 0 )
				throw new InvalidDataException( $"Predecoded UI asset decompresses to less than {rgba.Length} bytes." );
			read += block;
		}
		if ( zlib.ReadByte() != -1 )
			throw new InvalidDataException( $"Predecoded UI asset decompresses to more than {rgba.Length} bytes." );
		return (width, height, rgba);
	}

	/// <summary>The predecoded sibling of a shipped-art path (<c>X.wct.png</c> → <c>X.wct.rgba</c>), or null when the path is not a PNG name.</summary>
	public static string? SiblingPath( string pngPath ) =>
		pngPath.EndsWith( ".png", StringComparison.OrdinalIgnoreCase ) ? string.Concat( pngPath.AsSpan( 0, pngPath.Length - ".png".Length ), Extension ) : null;
}

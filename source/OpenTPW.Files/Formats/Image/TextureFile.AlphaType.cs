namespace OpenTPW;

/// <summary>
/// How tp.exe draws a world texture, decided by the texture's own header and not by its name: the alpha type that
/// the render-state builder 0x590C40 turns into a state word (<see cref="TextureFile.ReadAlphaType(Stream)"/>).
/// </summary>
public enum TextureAlphaType
{
	/// <summary>No alpha plane: state word 0x1256, no blend and no alpha test.</summary>
	Opaque,

	/// <summary>An alpha plane whose header alpha byte is 0 (texture flag 0x100): state word 0x2756, an alpha blend with an alpha test at 0xF0/255, drawn in the opaque list.</summary>
	Cutout,

	/// <summary>An alpha plane whose header alpha byte is not 0 (texture flag 0x200): state word 0x556, an alpha blend with an alpha test at 0x10/255, drawn after the opaque list, far to near.</summary>
	Translucent
}

public partial class TextureFile
{
	/// <summary>How tp.exe draws this texture (<see cref="ReadAlphaType(Stream)"/>), from its header; <see cref="TextureAlphaType.Opaque"/> for a texture that could not be opened.</summary>
	public TextureAlphaType AlphaType { get; private set; }

	/// <summary>
	/// The alpha type of the <c>.wct</c> whose header <paramref name="stream"/> starts with; the stream is not advanced past the header's first four bytes.
	/// A header the loader rejects (a first byte other than 0x12 or 0x13, or fewer than four bytes) is <see cref="TextureAlphaType.Opaque"/>.
	/// </summary>
	// [BIN:TPW-EXE:0x564C00 header load, 0x574D3A texture type, 0x5755F0 texture flags] the loader reads the 0x1C header into the image object and loads the alpha chunk (+0x20) only when header byte 2 (bits per pixel) is 0x20; the
	// type word 0x87A24C is 0x24 without it, 0x25 with it and 0x27 when header byte 1 is also non-zero; 0x5755F0 turns bit 0 (and not bit 1) into texture flag 0x100 and bit 1 into 0x200, and 0x590C40 turns those into the state words
	public static TextureAlphaType ReadAlphaType( Stream stream )
	{
		ArgumentNullException.ThrowIfNull( stream );
		Span<byte> header = stackalloc byte[4];
		var read = 0;
		while ( read < header.Length )
		{
			var count = stream.Read( header[read..] );
			if ( count <= 0 )
				break;
			read += count;
		}
		return read < header.Length ? TextureAlphaType.Opaque : ClassifyAlphaType( header[0], header[1], header[2] );
	}

	/// <summary>The alpha type for a header's first three bytes: flags, the alpha byte and the bits per pixel.</summary>
	internal static TextureAlphaType ClassifyAlphaType( byte flags, byte alphaByte, byte bitsPerPixel )
	{
		// the loader accepts only a first byte of 0x12 or 0x13 (0x564C00: 0x11 < byte < 0x14)
		if ( flags is not (0x12 or 0x13) || bitsPerPixel != 0x20 )
			return TextureAlphaType.Opaque;
		return alphaByte != 0 ? TextureAlphaType.Translucent : TextureAlphaType.Cutout;
	}
}

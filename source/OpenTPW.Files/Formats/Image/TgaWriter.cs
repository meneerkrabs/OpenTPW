namespace OpenTPW;

/// <summary>
/// Writes the TGA variant every shipped Theme Park World <c>.tga</c> uses (docs/CONTENT-KIT.md, "Textures"): image type 2 (uncompressed
/// true colour), 24 bits per pixel (descriptor 0x00) or 32 bits with 8 alpha bits (descriptor 0x08), bottom-left origin so the rows are
/// stored bottom to top, BGR(A) byte order, no image ID and no colour map, followed by the 26-byte TGA 2.0 footer with no extension or
/// developer area. A file with alpha everywhere opaque is written as 24-bit.
/// <para>
/// Why exactly this: the 159 uncompressed true-colour files of the shipped game (loose and inside WADs) are all of this form, and every
/// one of them ends in that footer (156 files end after the footer, three more carry a 495-byte extension area before it). tp.exe loads
/// <c>.tga</c> through the same loader as <c>.wct</c> (0x46EAD0 → 0x46F120); that loader's TGA codec was not traced, so which other variants it accepts
/// (RLE, top-left origin, 15/16-bit, paletted for textures) is unverified. The engine addresses rows in file order (World/Sky.cs), so a
/// top-left descriptor is deliberately not used: an image written this way reads back identically in stb (which honours the origin bit)
/// and in an engine that reads rows in file order.
/// </para>
/// </summary>
public static class TgaWriter
{
	public const int HeaderBytes = 18;
	public const int FooterBytes = 26;
	/// <summary>The largest side tp.exe's textures are known to reach is 1024 (UI backgrounds); the writer allows 4096.</summary>
	public const int MaximumDimension = 4096;

	/// <summary>The TGA 2.0 footer: extension offset 0, developer offset 0, the signature and its terminating NUL.</summary>
	private static readonly byte[] Footer = new byte[8].Concat( "TRUEVISION-XFILE."u8.ToArray() ).Append( (byte)0 ).ToArray();

	/// <summary>True when any pixel's alpha is below 255.</summary>
	public static bool HasAlpha( ReadOnlySpan<byte> rgba )
	{
		for ( var index = 3; index < rgba.Length; index += 4 )
			if ( rgba[index] != 255 )
				return true;
		return false;
	}

	/// <summary>Writes <paramref name="rgba"/> (straight, non-premultiplied RGBA8, row-major from the top row) as a TGA file.</summary>
	public static byte[] Write( int width, int height, ReadOnlySpan<byte> rgba )
	{
		if ( width is < 1 or > MaximumDimension || height is < 1 or > MaximumDimension )
			throw new ArgumentOutOfRangeException( nameof( width ), $"A TGA image is 1 to {MaximumDimension} pixels on each side." );
		if ( rgba.Length != width * height * 4 )
			throw new ArgumentException( "The pixel data must be width x height x 4 bytes.", nameof( rgba ) );
		var alpha = HasAlpha( rgba );
		var bytesPerPixel = alpha ? 4 : 3;
		var data = new byte[HeaderBytes + width * height * bytesPerPixel + FooterBytes];
		data[2] = 2;
		data[12] = (byte)width;
		data[13] = (byte)(width >> 8);
		data[14] = (byte)height;
		data[15] = (byte)(height >> 8);
		data[16] = (byte)(bytesPerPixel * 8);
		data[17] = alpha ? (byte)0x08 : (byte)0x00;
		var offset = HeaderBytes;
		for ( var row = height - 1; row >= 0; row-- )
		{
			for ( var column = 0; column < width; column++ )
			{
				var source = (row * width + column) * 4;
				data[offset++] = rgba[source + 2];
				data[offset++] = rgba[source + 1];
				data[offset++] = rgba[source];
				if ( alpha )
					data[offset++] = rgba[source + 3];
			}
		}
		Footer.CopyTo( data, offset );
		return data;
	}
}

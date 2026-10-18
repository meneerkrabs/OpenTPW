namespace OpenTPW;

public partial class TextureFile
{
	[Flags]
	enum TextureFlags
	{
		None = 0,

		Compressed = 0b10000,
		Unknown    = 0b00010,
		// Bit 0 set (0x13, the world textures): scale/wavelet pairs interleaved in each row; clear (0x12, the interface art): planar rows.
		InterleavedRows = 0b00001
	}
}

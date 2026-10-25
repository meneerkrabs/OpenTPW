namespace OpenTPW;

public partial class TextureFile
{
	struct TextureFileData
	{
		public TextureFlags Flags { get; set; }
		/// <summary>The header's second byte as stored: tp.exe tests it against 0 to tell a translucent texture from a cut-out one (the alpha type); it does not decide whether the alpha plane is decoded.</summary>
		public byte AlphaByte { get; set; }
		/// <summary>
		/// Whether the file has an alpha plane, which is decoded: 32 bits per pixel. The loader 0x564C00 reads the alpha chunk into [img+0x20] exactly then and the decoder takes the plane whenever that pointer is not null,
		/// whatever <see cref="AlphaByte"/> says (a cut-out texture has an alpha byte of 0 and a plane).
		/// </summary>
		public bool HasAlphaChannel => BitsPerPixel == 0x20;
		public byte BitsPerPixel { get; set; }
		public byte Version { get; set; }

		public short Width { get; set; }
		public short Height { get; set; }
		public short YChannelQuantizationScale { get; set; }
		public short CbChannelQuantizationScale { get; set; }
		public short CrChannelQuantizationScale { get; set; }
		public short AChannelQuantizationScale { get; set; }

		public int ColorChunkSize { get; set; }
		public int AlphaChunkSize { get; set; }

		public int Checksum { get; set; }

		public byte[] ColorChunk { get; set; }
		public byte[] AlphaChunk { get; set; }
	}
}

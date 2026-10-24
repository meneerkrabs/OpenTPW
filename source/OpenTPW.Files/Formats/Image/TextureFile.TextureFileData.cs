namespace OpenTPW;

public partial class TextureFile
{
	struct TextureFileData
	{
		public TextureFlags Flags { get; set; }
		/// <summary>The header's second byte as stored: tp.exe tests it against 0 (the alpha type), the decoder here against 1 (<see cref="HasAlphaChannel"/>).</summary>
		public byte AlphaByte { get; set; }
		public bool HasAlphaChannel => AlphaByte == 0x01;
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

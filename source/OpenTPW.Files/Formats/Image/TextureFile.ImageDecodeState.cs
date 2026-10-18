namespace OpenTPW;

public partial class TextureFile
{
	// Scratch sized for the largest channel (the constructor's size S; Cb/Cr
	// decode at S/2 and only ever touch a prefix). Bounds proven against every
	// index DecodeChannel computes, for a channel of size N:
	//
	//   DequantizationBuffer, N*N entries: step 1 writes exactly [0, N*N)
	//   (count = N*(N/2) pairs at stride 2) and the row transform reads
	//   back inside that range (interleaved rows reach N*N - 1, planar rows
	//   N*N - 1 too: the last wavelet sample of a row sits at N/2 + N/2 - 1).
	//
	//   RowDecodeBuffer, N*N entries: the row transform writes [0, N*N) (N/2
	//   output pairs per row) and the column reads reach (N/2-1)*N + col +
	//   N*N/2, i.e. N*N - 1.
	//
	// The buffers are shared by the Y/Cb/Cr/A channels without clearing in
	// between; every read hits an entry the same channel just wrote.
	// Arrays avoid List indexer calls.
	struct ImageDecodeState
	{
		public float[] DequantizationBuffer;
		public float[] RowDecodeBuffer;

		public ImageDecodeState( int size )
		{
			DequantizationBuffer = new float[size * size];
			RowDecodeBuffer = new float[size * size];
		}
	}
}

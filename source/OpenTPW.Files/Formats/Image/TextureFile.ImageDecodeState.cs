namespace OpenTPW;

public partial class TextureFile
{
	// Scratch sized for the largest channel (the constructor's size S; Cb/Cr
	// decode at S/2 and only ever touch a prefix). Bounds proven against every
	// index DecodeChannel computes, for a channel of size N:
	//
	//   DequantizationBuffer, N*N entries: step 1 writes exactly [0, N*N)
	//   (count = N*(N/2) pairs at stride 2) and the row transform only reads
	//   back inside that range - full scale tops out at N*N - 1, half scale at
	//   N*N - N/2 - so no entry outside the channel's own writes is read.
	//
	//   RowDecodeBuffer, 2*N*N entries: full-scale rows write [0, N*N);
	//   half-scale rows write [0, N*N] inclusive because dst = i leaves the
	//   last row's wavelet sample on index N*N. Column reads reach
	//   (N/2-1)*N + col + wOffset, i.e. N*N - 1 with the standard wavelet
	//   plane (wOffset = N*N/2) and N*N + N*N/2 - 1 with the half-scale alpha
	//   plane (wOffset = N*N). Two planes cover the worst case with room to
	//   spare.
	//
	// The buffers are shared by the Y/Cb/Cr/A channels without clearing in
	// between. That reuse is preserved exactly: every read either hits an
	// entry the same channel just wrote or lands in the tail past N*N that no
	// channel ever writes, which must keep its initial zeros - the half-scale
	// alpha wavelet plane reads them, so they affect the decoded output.
	// (The original allocation was N*N*N entries per buffer; only the N*N and
	// 1.5*N*N prefixes above are ever touched.)
	struct ImageDecodeState
	{
		public List<float> DequantizationBuffer;
		public List<float> RowDecodeBuffer;

		public ImageDecodeState( int size )
		{
			DequantizationBuffer = Enumerable.Repeat( 0f, size * size ).ToList();
			RowDecodeBuffer = Enumerable.Repeat( 0f, size * size * 2 ).ToList();
		}
	}
}

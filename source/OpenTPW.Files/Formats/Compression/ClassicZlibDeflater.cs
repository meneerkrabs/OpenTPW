namespace OpenTPW;

/// <summary>
/// A managed deflater that reproduces the bytes of the zlib the original game links (zlib 1.1.3, the version string at tp.exe 0x7780F0), so a save written
/// here is byte-identical to one the game compresses. The game's BILZ chunk writer (vtable method 0x619280) calls
/// <c>deflateInit2(level -1, Z_DEFLATED, windowBits 15, memLevel 9, Z_DEFAULT_STRATEGY)</c> and one <c>deflate(Z_FINISH)</c> over the whole payload:
/// level 6 (lazy matching, good 8, lazy 16, nice 128, chain 128), a 64K hash and a 32K symbol buffer.
/// <para>
/// This is a port of the algorithm (deflate_slow, longest_match, fill_window and the block coder of trees.c), not of any other encoder: the stock
/// <see cref="System.IO.Compression.ZLibStream"/> is built on a different zlib and emits different bytes. The bit-exact claim is tested on every original
/// save of the corpus (the compressed streams are reproduced from the inflated payloads). Only the one configuration the game uses is supported.
/// </para>
/// </summary>
public sealed class ClassicZlibDeflater
{
	private const int WindowBits = 15, MemLevel = 9;
	private const int WSize = 1 << WindowBits, WMask = WSize - 1;
	private const int HashBits = MemLevel + 7, HashSize = 1 << HashBits, HashMask = HashSize - 1, HashShift = (HashBits + 3 - 1) / 3;
	private const int LitBufSize = 1 << (MemLevel + 6);
	private const int MinMatch = 3, MaxMatch = 258, MinLookahead = MaxMatch + MinMatch + 1, MaxDist = WSize - MinLookahead;
	private const int TooFar = 4096;
	private const int GoodMatch = 8, MaxLazy = 16, NiceMatch = 128, MaxChain = 128;
	private const int LengthCodes = 29, Literals = 256, LCodes = Literals + 1 + LengthCodes, DCodes = 30, BlCodes = 19, HeapSize = 2 * LCodes + 1, MaxBits = 15, MaxBlBits = 7;
	private const int EndBlock = 256, Rep3To6 = 16, Repz3To10 = 17, Repz11To138 = 18;
	private const int WindowSize = 2 * WSize;

	private static readonly byte[] ExtraLBits = { 0, 0, 0, 0, 0, 0, 0, 0, 1, 1, 1, 1, 2, 2, 2, 2, 3, 3, 3, 3, 4, 4, 4, 4, 5, 5, 5, 5, 0 };
	private static readonly byte[] ExtraDBits = { 0, 0, 0, 0, 1, 1, 2, 2, 3, 3, 4, 4, 5, 5, 6, 6, 7, 7, 8, 8, 9, 9, 10, 10, 11, 11, 12, 12, 13, 13 };
	private static readonly byte[] ExtraBlBits = { 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 2, 3, 7 };
	private static readonly byte[] BlOrder = { 16, 17, 18, 0, 8, 7, 9, 6, 10, 5, 11, 4, 12, 3, 13, 2, 14, 1, 15 };

	private static readonly byte[] LengthCode = new byte[MaxMatch - MinMatch + 1];
	private static readonly byte[] DistCode = new byte[512];
	private static readonly int[] BaseLength = new int[LengthCodes];
	private static readonly int[] BaseDist = new int[DCodes];
	private static readonly Tree StaticLTree = new( LCodes + 2 );
	private static readonly Tree StaticDTree = new( DCodes );

	static ClassicZlibDeflater()
	{
		// tr_static_init
		var length = 0;
		int code;
		for ( code = 0; code < LengthCodes - 1; code++ )
		{
			BaseLength[code] = length;
			for ( var n = 0; n < 1 << ExtraLBits[code]; n++ )
				LengthCode[length++] = (byte)code;
		}
		LengthCode[length - 1] = (byte)code;
		var dist = 0;
		for ( code = 0; code < 16; code++ )
		{
			BaseDist[code] = dist;
			for ( var n = 0; n < 1 << ExtraDBits[code]; n++ )
				DistCode[dist++] = (byte)code;
		}
		dist >>= 7;
		for ( ; code < DCodes; code++ )
		{
			BaseDist[code] = dist << 7;
			for ( var n = 0; n < 1 << (ExtraDBits[code] - 7); n++ )
				DistCode[256 + dist++] = (byte)code;
		}
		var blCount = new ushort[MaxBits + 1];
		var index = 0;
		while ( index <= 143 ) { StaticLTree.Dl[index++] = 8; blCount[8]++; }
		while ( index <= 255 ) { StaticLTree.Dl[index++] = 9; blCount[9]++; }
		while ( index <= 279 ) { StaticLTree.Dl[index++] = 7; blCount[7]++; }
		while ( index <= 287 ) { StaticLTree.Dl[index++] = 8; blCount[8]++; }
		GenCodes( StaticLTree, LCodes + 1, blCount );
		for ( var n = 0; n < DCodes; n++ )
		{
			StaticDTree.Dl[n] = 5;
			StaticDTree.Fc[n] = (ushort)BiReverse( n, 5 );
		}
	}

	/// <summary>A code tree. zlib's <c>ct_data</c> overlays frequency with code (<see cref="Fc"/>) and parent with length (<see cref="Dl"/>); the overlay is kept because the algorithm reads one after the other writes it.</summary>
	private sealed class Tree
	{
		public readonly ushort[] Fc, Dl;
		public Tree( int size ) { Fc = new ushort[size]; Dl = new ushort[size]; }
	}

	private readonly record struct Description( Tree? Static, byte[] Extra, int ExtraBase, int Elems, int MaxLength );

	private static readonly Description StaticLDesc = new( StaticLTree, ExtraLBits, Literals + 1, LCodes, MaxBits );
	private static readonly Description StaticDDesc = new( StaticDTree, ExtraDBits, 0, DCodes, MaxBits );
	private static readonly Description StaticBlDesc = new( null, ExtraBlBits, 0, BlCodes, MaxBlBits );

	private readonly byte[] window = new byte[WindowSize + MaxMatch + 8];
	private readonly ushort[] prev = new ushort[WSize];
	private readonly ushort[] head = new ushort[HashSize];
	private int insH;
	private long blockStart;
	private int matchLength, prevMatch, matchAvailable, strStart, matchStart, lookahead, prevLength;

	private readonly Tree dynLTree = new( HeapSize ), dynDTree = new( 2 * DCodes + 1 ), blTree = new( 2 * BlCodes + 1 );
	private int lMaxCode, dMaxCode, blMaxCode;
	private readonly ushort[] blCount = new ushort[MaxBits + 1];
	private readonly int[] heap = new int[2 * LCodes + 1];
	private int heapLen, heapMax;
	private readonly byte[] depth = new byte[2 * LCodes + 1];
	private readonly byte[] lBuf = new byte[LitBufSize];
	private readonly ushort[] dBuf = new ushort[LitBufSize];
	private int lastLit;
	private long optLen, staticLen;
	private readonly List<byte> output = new();
	private uint biBuf;
	private int biValid;

	private byte[] input = Array.Empty<byte>();
	private int inputPosition;

	private ClassicZlibDeflater() { }

	/// <summary>The zlib stream (header 78 9C, deflate data, Adler-32) the game's BILZ writer produces for <paramref name="data"/>.</summary>
	public static byte[] Compress( ReadOnlySpan<byte> data )
	{
		var deflater = new ClassicZlibDeflater { input = data.ToArray() };
		deflater.output.Add( 0x78 );
		deflater.output.Add( 0x9C );
		deflater.InitBlock();
		deflater.DeflateSlow();
		var adler = Adler32( data );
		deflater.output.Add( (byte)(adler >> 24) );
		deflater.output.Add( (byte)(adler >> 16) );
		deflater.output.Add( (byte)(adler >> 8) );
		deflater.output.Add( (byte)adler );
		return deflater.output.ToArray();
	}

	/// <summary>The Adler-32 checksum of <paramref name="data"/> (the zlib trailer).</summary>
	public static uint Adler32( ReadOnlySpan<byte> data )
	{
		uint a = 1, b = 0;
		var index = 0;
		while ( index < data.Length )
		{
			var chunk = Math.Min( 5552, data.Length - index );
			for ( var end = index + chunk; index < end; index++ )
			{
				a += data[index];
				b += a;
			}
			a %= 65521;
			b %= 65521;
		}
		return (b << 16) | a;
	}

	private void UpdateHash( int c ) => insH = ((insH << HashShift) ^ c) & HashMask;

	private int InsertString( int str )
	{
		UpdateHash( window[str + MinMatch - 1] );
		var matchHead = head[insH];
		prev[str & WMask] = (ushort)matchHead;
		head[insH] = (ushort)str;
		return matchHead;
	}

	private int ReadBuffer( int offset, int size )
	{
		var length = Math.Min( size, input.Length - inputPosition );
		if ( length <= 0 )
			return 0;
		Buffer.BlockCopy( input, inputPosition, window, offset, length );
		inputPosition += length;
		return length;
	}

	private void FillWindow()
	{
		do
		{
			var more = WindowSize - lookahead - strStart;
			if ( strStart >= WSize + MaxDist )
			{
				Buffer.BlockCopy( window, WSize, window, 0, WSize );
				matchStart -= WSize;
				strStart -= WSize;
				blockStart -= WSize;
				for ( var n = 0; n < HashSize; n++ )
				{
					var m = head[n];
					head[n] = (ushort)(m >= WSize ? m - WSize : 0);
				}
				for ( var n = 0; n < WSize; n++ )
				{
					var m = prev[n];
					prev[n] = (ushort)(m >= WSize ? m - WSize : 0);
				}
				more += WSize;
			}
			if ( inputPosition >= input.Length )
				return;
			lookahead += ReadBuffer( strStart + lookahead, more );
			if ( lookahead >= MinMatch )
			{
				insH = window[strStart];
				UpdateHash( window[strStart + 1] );
			}
		} while ( lookahead < MinLookahead && inputPosition < input.Length );
	}

	private int LongestMatch( int curMatch )
	{
		var chainLength = MaxChain;
		var scan = strStart;
		var bestLen = prevLength;
		var niceMatch = NiceMatch;
		var limit = strStart > MaxDist ? strStart - MaxDist : 0;
		var strEnd = strStart + MaxMatch;
		var scanEnd1 = window[scan + bestLen - 1];
		var scanEnd = window[scan + bestLen];
		if ( prevLength >= GoodMatch )
			chainLength >>= 2;
		if ( niceMatch > lookahead )
			niceMatch = lookahead;
		do
		{
			var match = curMatch;
			if ( window[match + bestLen] != scanEnd || window[match + bestLen - 1] != scanEnd1 || window[match] != window[scan] || window[match + 1] != window[scan + 1] )
				continue;
			// The bytes at +2 are equal by the hash (hash bits >= 8), so the comparison starts at +3 and runs in groups of eight, as the original does.
			scan += 2;
			match += 2;
			while ( true )
			{
				var equal = true;
				for ( var step = 0; step < 8; step++ )
				{
					scan++;
					match++;
					if ( window[scan] != window[match] )
					{
						equal = false;
						break;
					}
				}
				if ( !equal || scan >= strEnd )
					break;
			}
			var len = MaxMatch - (strEnd - scan);
			scan = strEnd - MaxMatch;
			if ( len > bestLen )
			{
				matchStart = curMatch;
				bestLen = len;
				if ( len >= niceMatch )
					break;
				scanEnd1 = window[scan + bestLen - 1];
				scanEnd = window[scan + bestLen];
			}
		} while ( (curMatch = prev[curMatch & WMask]) > limit && --chainLength != 0 );
		return bestLen <= lookahead ? bestLen : lookahead;
	}

	private void DeflateSlow()
	{
		while ( true )
		{
			if ( lookahead < MinLookahead )
			{
				FillWindow();
				if ( lookahead == 0 )
					break;
			}
			var hashHead = 0;
			if ( lookahead >= MinMatch )
				hashHead = InsertString( strStart );
			prevLength = matchLength;
			prevMatch = matchStart;
			matchLength = MinMatch - 1;
			if ( hashHead != 0 && prevLength < MaxLazy && strStart - hashHead <= MaxDist )
			{
				matchLength = LongestMatch( hashHead );
				if ( matchLength <= 5 && matchLength == MinMatch && strStart - matchStart > TooFar )
					matchLength = MinMatch - 1;
			}
			if ( prevLength >= MinMatch && matchLength <= prevLength )
			{
				var maxInsert = strStart + lookahead - MinMatch;
				var flush = TallyDist( strStart - 1 - prevMatch, prevLength - MinMatch );
				lookahead -= prevLength - 1;
				prevLength -= 2;
				do
				{
					if ( ++strStart <= maxInsert )
						InsertString( strStart );
				} while ( --prevLength != 0 );
				matchAvailable = 0;
				matchLength = MinMatch - 1;
				strStart++;
				if ( flush )
					FlushBlock( false );
			}
			else if ( matchAvailable != 0 )
			{
				var flush = TallyLit( window[strStart - 1] );
				if ( flush )
					FlushBlock( false );
				strStart++;
				lookahead--;
			}
			else
			{
				matchAvailable = 1;
				strStart++;
				lookahead--;
			}
		}
		if ( matchAvailable != 0 )
		{
			TallyLit( window[strStart - 1] );
			matchAvailable = 0;
		}
		FlushBlock( true );
	}

	private void FlushBlock( bool last )
	{
		FlushTreeBlock( blockStart >= 0 ? (int)blockStart : -1, (int)(strStart - blockStart), last );
		blockStart = strStart;
	}

	private bool TallyLit( int c )
	{
		dBuf[lastLit] = 0;
		lBuf[lastLit++] = (byte)c;
		dynLTree.Fc[c]++;
		return lastLit == LitBufSize - 1;
	}

	private bool TallyDist( int dist, int lc )
	{
		dBuf[lastLit] = (ushort)dist;
		lBuf[lastLit++] = (byte)lc;
		dist--;
		dynLTree.Fc[LengthCode[lc] + Literals + 1]++;
		dynDTree.Fc[DCode( dist )]++;
		return lastLit == LitBufSize - 1;
	}

	private static int DCode( int dist ) => dist < 256 ? DistCode[dist] : DistCode[256 + (dist >> 7)];

	private void InitBlock()
	{
		for ( var n = 0; n < LCodes; n++ ) dynLTree.Fc[n] = 0;
		for ( var n = 0; n < DCodes; n++ ) dynDTree.Fc[n] = 0;
		for ( var n = 0; n < BlCodes; n++ ) blTree.Fc[n] = 0;
		dynLTree.Fc[EndBlock] = 1;
		optLen = staticLen = 0;
		lastLit = 0;
	}

	private void FlushTreeBlock( int bufferStart, int storedLen, bool last )
	{
		BuildTree( dynLTree, StaticLDesc, ref lMaxCode );
		BuildTree( dynDTree, StaticDDesc, ref dMaxCode );
		var maxBlIndex = BuildBlTree();
		var optLenB = (optLen + 3 + 7) >> 3;
		var staticLenB = (staticLen + 3 + 7) >> 3;
		if ( staticLenB <= optLenB )
			optLenB = staticLenB;
		if ( storedLen + 4 <= optLenB && bufferStart >= 0 )
		{
			StoredBlock( bufferStart, storedLen, last );
		}
		else if ( staticLenB == optLenB )
		{
			SendBits( (1 << 1) + (last ? 1 : 0), 3 );
			CompressBlock( StaticLTree, StaticDTree );
		}
		else
		{
			SendBits( (2 << 1) + (last ? 1 : 0), 3 );
			SendAllTrees( lMaxCode + 1, dMaxCode + 1, maxBlIndex + 1 );
			CompressBlock( dynLTree, dynDTree );
		}
		InitBlock();
		if ( last )
			BiWindup();
	}

	private void StoredBlock( int start, int length, bool last )
	{
		SendBits( (0 << 1) + (last ? 1 : 0), 3 );
		BiWindup();
		PutShort( length );
		PutShort( ~length & 0xFFFF );
		for ( var index = 0; index < length; index++ )
			output.Add( window[start + index] );
	}

	private void PutShort( int value )
	{
		output.Add( (byte)value );
		output.Add( (byte)(value >> 8) );
	}

	private void SendBits( int value, int length )
	{
		biBuf |= (uint)value << biValid;
		biValid += length;
		while ( biValid >= 16 )
		{
			PutShort( (int)(biBuf & 0xFFFF) );
			biBuf >>= 16;
			biValid -= 16;
		}
	}

	private void BiWindup()
	{
		if ( biValid > 8 )
			PutShort( (int)biBuf );
		else if ( biValid > 0 )
			output.Add( (byte)biBuf );
		biBuf = 0;
		biValid = 0;
	}

	private static int BiReverse( int code, int length )
	{
		var result = 0;
		do
		{
			result |= code & 1;
			code >>= 1;
			result <<= 1;
		} while ( --length > 0 );
		return result >> 1;
	}

	private void SendCode( int c, Tree tree ) => SendBits( tree.Fc[c], tree.Dl[c] );

	private static bool Smaller( Tree tree, int n, int m, byte[] depth ) =>
		tree.Fc[n] < tree.Fc[m] || (tree.Fc[n] == tree.Fc[m] && depth[n] <= depth[m]);

	private void PqDownHeap( Tree tree, int k )
	{
		var v = heap[k];
		var j = k << 1;
		while ( j <= heapLen )
		{
			if ( j < heapLen && Smaller( tree, heap[j + 1], heap[j], depth ) )
				j++;
			if ( Smaller( tree, v, heap[j], depth ) )
				break;
			heap[k] = heap[j];
			k = j;
			j <<= 1;
		}
		heap[k] = v;
	}

	private void BuildTree( Tree tree, Description description, ref int maxCodeResult )
	{
		var stree = description.Static;
		var elems = description.Elems;
		var maxCode = -1;
		heapLen = 0;
		heapMax = HeapSize;
		for ( var n = 0; n < elems; n++ )
		{
			if ( tree.Fc[n] != 0 )
			{
				heap[++heapLen] = maxCode = n;
				depth[n] = 0;
			}
			else
			{
				tree.Dl[n] = 0;
			}
		}
		while ( heapLen < 2 )
		{
			var node = heap[++heapLen] = maxCode < 2 ? ++maxCode : 0;
			tree.Fc[node] = 1;
			depth[node] = 0;
			optLen--;
			if ( stree != null )
				staticLen -= stree.Dl[node];
		}
		maxCodeResult = maxCode;
		for ( var n = heapLen / 2; n >= 1; n-- )
			PqDownHeap( tree, n );
		var next = elems;
		do
		{
			var n = heap[1];
			heap[1] = heap[heapLen--];
			PqDownHeap( tree, 1 );
			var m = heap[1];
			heap[--heapMax] = n;
			heap[--heapMax] = m;
			tree.Fc[next] = (ushort)(tree.Fc[n] + tree.Fc[m]);
			depth[next] = (byte)((depth[n] >= depth[m] ? depth[n] : depth[m]) + 1);
			tree.Dl[n] = tree.Dl[m] = (ushort)next;
			heap[1] = next++;
			PqDownHeap( tree, 1 );
		} while ( heapLen >= 2 );
		heap[--heapMax] = heap[1];
		GenBitLen( tree, description, maxCode );
		GenCodes( tree, maxCode, blCount );
	}

	private void GenBitLen( Tree tree, Description description, int maxCode )
	{
		var stree = description.Static;
		var extra = description.Extra;
		var baseIndex = description.ExtraBase;
		var maxLength = description.MaxLength;
		var overflow = 0;
		for ( var bits = 0; bits <= MaxBits; bits++ )
			blCount[bits] = 0;
		tree.Dl[heap[heapMax]] = 0;
		int h;
		for ( h = heapMax + 1; h < HeapSize; h++ )
		{
			var n = heap[h];
			var bits = tree.Dl[tree.Dl[n]] + 1;
			if ( bits > maxLength )
			{
				bits = maxLength;
				overflow++;
			}
			tree.Dl[n] = (ushort)bits;
			if ( n > maxCode )
				continue;
			blCount[bits]++;
			var xbits = n >= baseIndex ? extra[n - baseIndex] : 0;
			var f = tree.Fc[n];
			optLen += (long)f * (bits + xbits);
			if ( stree != null )
				staticLen += (long)f * (stree.Dl[n] + xbits);
		}
		if ( overflow == 0 )
			return;
		do
		{
			var bits = maxLength - 1;
			while ( blCount[bits] == 0 )
				bits--;
			blCount[bits]--;
			blCount[bits + 1] += 2;
			blCount[maxLength]--;
			overflow -= 2;
		} while ( overflow > 0 );
		for ( var bits = maxLength; bits != 0; bits-- )
		{
			var n = (int)blCount[bits];
			while ( n != 0 )
			{
				var m = heap[--h];
				if ( m > maxCode )
					continue;
				if ( tree.Dl[m] != bits )
				{
					optLen += ((long)bits - tree.Dl[m]) * tree.Fc[m];
					tree.Dl[m] = (ushort)bits;
				}
				n--;
			}
		}
	}

	private static void GenCodes( Tree tree, int maxCode, ushort[] blCount )
	{
		var nextCode = new ushort[MaxBits + 1];
		uint code = 0;
		for ( var bits = 1; bits <= MaxBits; bits++ )
		{
			code = (code + blCount[bits - 1]) << 1;
			nextCode[bits] = (ushort)code;
		}
		for ( var n = 0; n <= maxCode; n++ )
		{
			int len = tree.Dl[n];
			if ( len == 0 )
				continue;
			tree.Fc[n] = (ushort)BiReverse( nextCode[len]++, len );
		}
	}

	private void ScanTree( Tree tree, int maxCode )
	{
		var prevLen = -1;
		int nextLen = tree.Dl[0];
		var count = 0;
		var maxCount = 7;
		var minCount = 4;
		if ( nextLen == 0 )
		{
			maxCount = 138;
			minCount = 3;
		}
		tree.Dl[maxCode + 1] = 0xFFFF;
		for ( var n = 0; n <= maxCode; n++ )
		{
			var curLen = nextLen;
			nextLen = tree.Dl[n + 1];
			if ( ++count < maxCount && curLen == nextLen )
				continue;
			if ( count < minCount )
				blTree.Fc[curLen] += (ushort)count;
			else if ( curLen != 0 )
			{
				if ( curLen != prevLen )
					blTree.Fc[curLen]++;
				blTree.Fc[Rep3To6]++;
			}
			else if ( count <= 10 )
				blTree.Fc[Repz3To10]++;
			else
				blTree.Fc[Repz11To138]++;
			count = 0;
			prevLen = curLen;
			if ( nextLen == 0 )
			{
				maxCount = 138;
				minCount = 3;
			}
			else if ( curLen == nextLen )
			{
				maxCount = 6;
				minCount = 3;
			}
			else
			{
				maxCount = 7;
				minCount = 4;
			}
		}
	}

	private void SendTree( Tree tree, int maxCode )
	{
		var prevLen = -1;
		int nextLen = tree.Dl[0];
		var count = 0;
		var maxCount = 7;
		var minCount = 4;
		if ( nextLen == 0 )
		{
			maxCount = 138;
			minCount = 3;
		}
		for ( var n = 0; n <= maxCode; n++ )
		{
			var curLen = nextLen;
			nextLen = tree.Dl[n + 1];
			if ( ++count < maxCount && curLen == nextLen )
				continue;
			if ( count < minCount )
			{
				do { SendCode( curLen, blTree ); } while ( --count != 0 );
			}
			else if ( curLen != 0 )
			{
				if ( curLen != prevLen )
				{
					SendCode( curLen, blTree );
					count--;
				}
				SendCode( Rep3To6, blTree );
				SendBits( count - 3, 2 );
			}
			else if ( count <= 10 )
			{
				SendCode( Repz3To10, blTree );
				SendBits( count - 3, 3 );
			}
			else
			{
				SendCode( Repz11To138, blTree );
				SendBits( count - 11, 7 );
			}
			count = 0;
			prevLen = curLen;
			if ( nextLen == 0 )
			{
				maxCount = 138;
				minCount = 3;
			}
			else if ( curLen == nextLen )
			{
				maxCount = 6;
				minCount = 3;
			}
			else
			{
				maxCount = 7;
				minCount = 4;
			}
		}
	}

	private int BuildBlTree()
	{
		ScanTree( dynLTree, lMaxCode );
		ScanTree( dynDTree, dMaxCode );
		BuildTree( blTree, StaticBlDesc, ref blMaxCode );
		int maxBlIndex;
		for ( maxBlIndex = BlCodes - 1; maxBlIndex >= 3; maxBlIndex-- )
			if ( blTree.Dl[BlOrder[maxBlIndex]] != 0 )
				break;
		optLen += 3 * ((long)maxBlIndex + 1) + 5 + 5 + 4;
		return maxBlIndex;
	}

	private void SendAllTrees( int lCodes, int dCodes, int blCodes )
	{
		SendBits( lCodes - 257, 5 );
		SendBits( dCodes - 1, 5 );
		SendBits( blCodes - 4, 4 );
		for ( var rank = 0; rank < blCodes; rank++ )
			SendBits( blTree.Dl[BlOrder[rank]], 3 );
		SendTree( dynLTree, lCodes - 1 );
		SendTree( dynDTree, dCodes - 1 );
	}

	private void CompressBlock( Tree ltree, Tree dtree )
	{
		for ( var index = 0; index < lastLit; index++ )
		{
			int dist = dBuf[index];
			int lc = lBuf[index];
			if ( dist == 0 )
			{
				SendCode( lc, ltree );
				continue;
			}
			int code = LengthCode[lc];
			SendCode( code + Literals + 1, ltree );
			var extra = ExtraLBits[code];
			if ( extra != 0 )
				SendBits( lc - BaseLength[code], extra );
			dist--;
			code = DCode( dist );
			SendCode( code, dtree );
			extra = ExtraDBits[code];
			if ( extra != 0 )
				SendBits( dist - BaseDist[code], extra );
		}
		SendCode( EndBlock, ltree );
	}
}

namespace OpenTPW;

/// <summary>
/// The two codecs of the original autorun launcher's TREE archives (docs/AUTORUN.md): a headerless
/// RefPack stream (the EA LZ77 command set without the 0xFB10 header and size) and PKWARE Data
/// Compression Library "implode" (the launcher links PKWARE's explode routine; no shipped entry uses
/// it, so it is verified against the published reference vector only).
/// </summary>
public static class TreCompression
{
	/// <summary>Decodes a headerless RefPack stream to exactly <paramref name="expectedSize"/> bytes.</summary>
	public static byte[] Refpack( byte[] source, int expectedSize )
	{
		var output = new byte[expectedSize];
		var written = 0;
		var read = 0;

		byte Next()
		{
			if ( read >= source.Length )
				throw new InvalidDataException( "RefPack stream ends inside a command." );
			return source[read++];
		}

		void Literals( int count )
		{
			if ( count > source.Length - read || count > expectedSize - written )
				throw new InvalidDataException( "RefPack literal run exceeds the data." );
			source.AsSpan( read, count ).CopyTo( output.AsSpan( written ) );
			read += count;
			written += count;
		}

		void Copy( int distance, int length )
		{
			if ( distance > written || length > expectedSize - written )
				throw new InvalidDataException( "RefPack back-reference is outside the data." );
			for ( var index = 0; index < length; index++, written++ )
				output[written] = output[written - distance];
		}

		while ( true )
		{
			var command = Next();
			if ( command < 0x80 )
			{
				var second = Next();
				Literals( command & 3 );
				Copy( ((command & 0x60) << 3) + second + 1, ((command & 0x1C) >> 2) + 3 );
			}
			else if ( command < 0xC0 )
			{
				var second = Next();
				var third = Next();
				Literals( second >> 6 );
				Copy( ((second & 0x3F) << 8) + third + 1, (command & 0x3F) + 4 );
			}
			else if ( command < 0xE0 )
			{
				var second = Next();
				var third = Next();
				var fourth = Next();
				Literals( command & 3 );
				Copy( (((command & 0x10) >> 4) << 16) + (second << 8) + third + 1, ((command & 0x0C) << 6) + fourth + 5 );
			}
			else
			{
				var run = (command & 0x1F) * 4 + 4;
				if ( run <= 0x70 )
				{
					Literals( run );
					continue;
				}
				// 0xFC..0xFF: stop, with 0..3 trailing literals.
				Literals( command & 3 );
				break;
			}
		}
		if ( written != expectedSize )
			throw new InvalidDataException( $"RefPack stream produced {written} bytes, expected {expectedSize}." );
		return output;
	}

	// Code tables of PKWARE DCL as run-length packed lengths (high nibble: repeat - 1, low nibble: code length),
	// in the form used by zlib's contrib/blast reference decoder.
	private static readonly byte[] LiteralLengths =
	[
		11, 124, 8, 7, 28, 7, 188, 13, 76, 4, 10, 8, 12, 10, 12, 10, 8, 23, 8, 9, 7, 6, 7, 8, 7, 6, 55, 8, 23, 24, 12, 11,
		7, 9, 11, 12, 6, 7, 22, 5, 7, 24, 6, 11, 9, 6, 7, 22, 7, 11, 38, 7, 9, 8, 25, 11, 8, 11, 9, 12, 8, 12, 5, 38,
		5, 38, 5, 11, 7, 5, 6, 21, 6, 10, 53, 8, 7, 24, 10, 27, 44, 253, 253, 253, 252, 252, 252, 13, 12, 45, 12, 45, 12, 61, 12, 45,
		44, 173
	];
	private static readonly byte[] LengthLengths = [2, 35, 36, 53, 38, 23];
	private static readonly byte[] DistanceLengths = [2, 20, 53, 230, 247, 151, 248];
	private static readonly ushort[] LengthBase = [3, 2, 4, 5, 6, 7, 8, 9, 10, 12, 16, 24, 40, 72, 136, 264];
	private static readonly byte[] LengthExtra = [0, 0, 0, 0, 0, 0, 0, 0, 1, 2, 3, 4, 5, 6, 7, 8];

	private sealed class Huffman
	{
		public readonly short[] Count = new short[16];
		public readonly short[] Symbol;

		public Huffman( byte[] packed )
		{
			var lengths = new List<int>();
			foreach ( var value in packed )
				for ( var repeat = (value >> 4) + 1; repeat > 0; repeat-- )
					lengths.Add( value & 15 );
			Symbol = new short[lengths.Count];
			foreach ( var length in lengths )
				Count[length]++;
			var offsets = new int[16];
			for ( var length = 1; length < 15; length++ )
				offsets[length + 1] = offsets[length] + Count[length];
			for ( var symbol = 0; symbol < lengths.Count; symbol++ )
				if ( lengths[symbol] != 0 )
					Symbol[offsets[lengths[symbol]]++] = (short)symbol;
		}
	}

	private static readonly Huffman LiteralCode = new( LiteralLengths );
	private static readonly Huffman LengthCode = new( LengthLengths );
	private static readonly Huffman DistanceCode = new( DistanceLengths );

	/// <summary>Decodes a PKWARE DCL stream to exactly <paramref name="expectedSize"/> bytes.</summary>
	public static byte[] Explode( byte[] source, int expectedSize )
	{
		if ( source.Length < 2 || source[0] > 1 || source[1] < 4 || source[1] > 6 )
			throw new InvalidDataException( "Not a PKWARE DCL stream." );
		var codedLiterals = source[0] == 1;
		var dictionaryBits = source[1];
		var position = 2;
		var buffer = 0;
		var count = 0;
		var output = new byte[expectedSize];
		var written = 0;

		int Bits( int need )
		{
			var value = buffer;
			while ( count < need )
			{
				if ( position >= source.Length )
					throw new InvalidDataException( "PKWARE stream ends early." );
				value |= source[position++] << count;
				count += 8;
			}
			buffer = value >> need;
			count -= need;
			return value & ((1 << need) - 1);
		}

		int Decode( Huffman code )
		{
			var accumulated = 0;
			var first = 0;
			var index = 0;
			for ( var length = 1; length <= 15; length++ )
			{
				accumulated |= Bits( 1 ) ^ 1;
				var codes = code.Count[length];
				if ( accumulated - codes < first )
					return code.Symbol[index + (accumulated - first)];
				index += codes;
				first += codes;
				first <<= 1;
				accumulated <<= 1;
			}
			throw new InvalidDataException( "Invalid PKWARE code." );
		}

		while ( true )
		{
			if ( Bits( 1 ) != 0 )
			{
				var symbol = Decode( LengthCode );
				var length = LengthBase[symbol] + Bits( LengthExtra[symbol] );
				if ( length == 519 )
					break;
				var shift = length == 2 ? 2 : dictionaryBits;
				var distance = (Decode( DistanceCode ) << shift) + Bits( shift ) + 1;
				if ( distance > written || length > expectedSize - written )
					throw new InvalidDataException( "PKWARE back-reference is outside the data." );
				for ( var copied = 0; copied < length; copied++, written++ )
					output[written] = output[written - distance];
			}
			else
			{
				if ( written >= expectedSize )
					throw new InvalidDataException( "PKWARE stream exceeds the expected size." );
				output[written++] = (byte)(codedLiterals ? Decode( LiteralCode ) : Bits( 8 ));
			}
		}
		if ( written != expectedSize )
			throw new InvalidDataException( $"PKWARE stream produced {written} bytes, expected {expectedSize}." );
		return output;
	}
}

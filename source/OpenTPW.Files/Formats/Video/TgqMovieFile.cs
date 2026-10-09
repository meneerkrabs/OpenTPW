using System.Buffers.Binary;
using System.Text;

namespace OpenTPW;

public sealed record TgqChunk( string FourCC, int Offset, int Size );

public sealed record TgqPatchTag( byte Tag, byte[] Value );

/// <summary>Audio parameters from the SCHl "PT" patch substream. Absent tags use the documented SCxl defaults.</summary>
public sealed record TgqAudioHeader(
	int Platform, int Channels, int Compression, int? CompressionRevision, int SampleRate,
	long SampleCount, int? FrameRate, IReadOnlyList<TgqPatchTag> Tags );

/// <summary>
/// Theme Park World FMV container (.tgq): little-endian EA chunks (FourCC + u32 size including the
/// 8-byte preamble). Observed layout: SCHl, interleaved pIQT video and SCDl audio, one SCCl audio-block
/// count, and SCEl as the final chunk. Unobserved chunk types are rejected.
/// </summary>
public sealed class TgqMovieFile : BaseFormat
{
	public const int MaximumFileBytes = 64 * 1024 * 1024;
	public const int MaximumChunkBytes = 1024 * 1024;
	public const int MaximumChunks = 65536;
	public const long MaximumAudioSamplesPerChannel = 16 * 1024 * 1024;
	public const int DefaultSampleRate = 22050;

	private byte[] data = Array.Empty<byte>();
	private readonly List<TgqChunk> videoChunks = new();
	private readonly List<TgqChunk> audioChunks = new();

	public IReadOnlyList<TgqChunk> Chunks { get; private set; } = Array.Empty<TgqChunk>();
	public TgqAudioHeader AudioHeader { get; private set; } = null!;
	public int DeclaredAudioBlockCount { get; private set; }
	public int AudioBlockCount => audioChunks.Count;
	public int VideoFrameCount => videoChunks.Count;
	public int Width { get; private set; }
	public int Height { get; private set; }

	public TgqMovieFile( string path ) => ReadFromFile( path );
	public TgqMovieFile( Stream stream ) => ReadFromStream( stream );

	protected override void ReadFromStream( Stream stream )
	{
		ArgumentNullException.ThrowIfNull( stream );
		data = ReadBounded( stream );
		var chunks = new List<TgqChunk>();
		var offset = 0;
		var declaredBlocks = -1;
		TgqAudioHeader? header = null;
		var ended = false;
		while ( offset < data.Length )
		{
			if ( ended )
				throw new InvalidDataException( "TGQ has data after its SCEl end chunk." );
			if ( data.Length - offset < 8 )
				throw new InvalidDataException( "TGQ chunk preamble is truncated." );
			if ( chunks.Count >= MaximumChunks )
				throw new InvalidDataException( "TGQ exceeds the chunk count limit." );
			var fourCC = Encoding.ASCII.GetString( data, offset, 4 );
			var size = BinaryPrimitives.ReadUInt32LittleEndian( data.AsSpan( offset + 4 ) );
			if ( size < 8 || size > MaximumChunkBytes || size > (uint)(data.Length - offset) )
				throw new InvalidDataException( $"TGQ chunk {fourCC} at {offset} has an invalid size." );
			var chunk = new TgqChunk( fourCC, offset, (int)size );
			var payload = data.AsSpan( offset + 8, (int)size - 8 );
			if ( chunks.Count == 0 && fourCC != "SCHl" )
				throw new InvalidDataException( "TGQ must begin with an SCHl audio header." );
			switch ( fourCC )
			{
				case "SCHl":
					if ( header != null )
						throw new InvalidDataException( "TGQ has more than one SCHl header." );
					header = ParseAudioHeader( payload );
					break;
				case "SCCl":
					if ( declaredBlocks >= 0 || audioChunks.Count > 0 || payload.Length != 4 )
						throw new InvalidDataException( "TGQ SCCl must be a single 4-byte count before audio data." );
					var count = BinaryPrimitives.ReadUInt32LittleEndian( payload );
					if ( count > MaximumChunks )
						throw new InvalidDataException( "TGQ SCCl audio block count exceeds the chunk limit." );
					declaredBlocks = (int)count;
					break;
				case "SCDl":
					if ( declaredBlocks < 0 )
						throw new InvalidDataException( "TGQ SCDl audio data precedes its SCCl count." );
					audioChunks.Add( chunk );
					break;
				case "pIQT":
					var frame = TqiDecoder.ReadHeader( payload );
					if ( videoChunks.Count == 0 )
					{
						Width = frame.Width;
						Height = frame.Height;
					}
					else if ( frame.Width != Width || frame.Height != Height )
						throw new InvalidDataException( "TGQ video frame dimensions change mid-stream." );
					videoChunks.Add( chunk );
					break;
				case "SCEl":
					if ( payload.Length != 0 )
						throw new InvalidDataException( "TGQ SCEl end chunk must be empty." );
					ended = true;
					break;
				default:
					throw new NotSupportedException( $"TGQ chunk type {fourCC} is not supported." );
			}
			chunks.Add( chunk );
			offset += (int)size;
		}
		if ( header == null || !ended )
			throw new InvalidDataException( "TGQ is missing its SCHl header or SCEl end chunk." );
		if ( declaredBlocks != audioChunks.Count )
			throw new InvalidDataException( "TGQ SCCl count does not match the number of SCDl blocks." );
		if ( videoChunks.Count == 0 )
			throw new InvalidDataException( "TGQ contains no pIQT video frames." );
		AudioHeader = header;
		DeclaredAudioBlockCount = declaredBlocks;
		Chunks = chunks.AsReadOnly();
	}

	/// <summary>Decodes video frame <paramref name="index"/> (0-based, file order) to planar 4:2:0.</summary>
	public TqiFrame DecodeVideoFrame( int index )
	{
		ArgumentOutOfRangeException.ThrowIfNegative( index );
		ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual( index, videoChunks.Count );
		var chunk = videoChunks[index];
		return TqiDecoder.Decode( data.AsSpan( chunk.Offset + 8, chunk.Size - 8 ) );
	}

	/// <summary>Raw pIQT payload (frame header + bitstream) of video frame <paramref name="index"/>.</summary>
	public ReadOnlyMemory<byte> GetVideoFramePayload( int index )
	{
		ArgumentOutOfRangeException.ThrowIfNegative( index );
		ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual( index, videoChunks.Count );
		var chunk = videoChunks[index];
		return data.AsMemory( chunk.Offset + 8, chunk.Size - 8 );
	}

	/// <summary>
	/// Decodes all SCDl blocks to interleaved signed 16-bit PCM. Only the observed variant is supported:
	/// PC platform, compression 7 (EA-XA ADPCM) with no revision tag, two channels.
	/// The decoded per-channel sample total must equal the SCHl sample count.
	/// </summary>
	public short[] DecodeAudio()
	{
		var total = ValidateAudio();
		var pcm = new short[checked((int)(total * 2))];
		var written = 0;
		for ( var index = 0; index < audioChunks.Count; index++ )
			written += DecodeAudioBlock( index, pcm.AsSpan( written * 2 ) );
		return pcm;
	}

	/// <summary>
	/// Checks the supported audio variant and that the SCDl block sample counts add up to the SCHl sample count;
	/// returns that per-channel sample count. Lets streaming players fail before playback starts.
	/// </summary>
	public long ValidateAudio()
	{
		var header = AudioHeader;
		if ( header.Platform != 0 || header.Compression != 7 || header.Channels != 2 || (header.CompressionRevision ?? 1) != 1 )
			throw new NotSupportedException( "Only PC stereo EA-XA revision 1 TGQ audio is supported." );
		long total = 0;
		for ( var index = 0; index < audioChunks.Count && total <= header.SampleCount; index++ )
			total += GetAudioBlockSampleCount( index );
		if ( total != header.SampleCount )
			throw new InvalidDataException( "TGQ audio blocks do not add up to the SCHl sample count." );
		return total;
	}

	/// <summary>Per-channel sample count of SCDl block <paramref name="index"/> (file order).</summary>
	public int GetAudioBlockSampleCount( int index ) => EaXaAdpcmDecoder.ReadStereoBlockSampleCount( GetAudioBlock( index ) );

	/// <summary>Decodes SCDl block <paramref name="index"/> to interleaved stereo PCM; returns the sample frames written.</summary>
	public int DecodeAudioBlock( int index, Span<short> interleavedOutput ) => EaXaAdpcmDecoder.DecodeStereoBlock( GetAudioBlock( index ), interleavedOutput );

	private ReadOnlySpan<byte> GetAudioBlock( int index )
	{
		ArgumentOutOfRangeException.ThrowIfNegative( index );
		ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual( index, audioChunks.Count );
		var chunk = audioChunks[index];
		return data.AsSpan( chunk.Offset + 8, chunk.Size - 8 );
	}

	private static TgqAudioHeader ParseAudioHeader( ReadOnlySpan<byte> payload )
	{
		if ( payload.Length < 5 || payload[0] != (byte)'P' || payload[1] != (byte)'T' )
			throw new InvalidDataException( "TGQ SCHl does not contain a PT patch." );
		var platform = BinaryPrimitives.ReadUInt16LittleEndian( payload[2..] );
		var tags = new List<TgqPatchTag>();
		var numeric = new Dictionary<byte, long>();
		var offset = 4;
		while ( true )
		{
			if ( offset >= payload.Length )
				throw new InvalidDataException( "TGQ PT patch is not terminated." );
			var tag = payload[offset++];
			if ( tag == 0xff )
				break;
			// 0xFC-0xFE carry no data (SCxl documentation); every other tag has a length byte and value.
			if ( tag is 0xfc or 0xfd or 0xfe )
			{
				tags.Add( new TgqPatchTag( tag, Array.Empty<byte>() ) );
				continue;
			}
			if ( offset >= payload.Length )
				throw new InvalidDataException( "TGQ PT patch tag length is truncated." );
			long length = payload[offset++];
			if ( length == 0xff )
			{
				if ( payload.Length - offset < 4 )
					throw new InvalidDataException( "TGQ PT patch extended length is truncated." );
				length = BinaryPrimitives.ReadUInt32BigEndian( payload[offset..] );
				offset += 4;
			}
			if ( length > payload.Length - offset )
				throw new InvalidDataException( "TGQ PT patch tag value is truncated." );
			var value = payload.Slice( offset, (int)length ).ToArray();
			offset += (int)length;
			tags.Add( new TgqPatchTag( tag, value ) );
			if ( tag is 0x1b or 0x80 or 0x82 or 0x83 or 0x84 or 0x85 )
			{
				if ( length is < 1 or > 4 || numeric.ContainsKey( tag ) )
					throw new InvalidDataException( $"TGQ PT patch tag 0x{tag:X2} is malformed or repeated." );
				long number = 0;
				foreach ( var part in value )
					number = (number << 8) | part;
				numeric[tag] = number;
			}
		}
		foreach ( var padding in payload[offset..] )
			if ( padding != 0 )
				throw new InvalidDataException( "TGQ SCHl has nonzero bytes after its PT patch." );
		var channels = (int)numeric.GetValueOrDefault( (byte)0x82, 1 );
		var sampleRate = (int)numeric.GetValueOrDefault( (byte)0x84, DefaultSampleRate );
		var sampleCount = numeric.GetValueOrDefault( (byte)0x85, 0 );
		if ( channels is < 1 or > 8 || sampleRate is < 1 or > 192000 || sampleCount > MaximumAudioSamplesPerChannel )
			throw new InvalidDataException( "TGQ audio header values are outside supported limits." );
		return new TgqAudioHeader( platform, channels, (int)numeric.GetValueOrDefault( (byte)0x83, 7 ),
			numeric.TryGetValue( 0x80, out var revision ) ? (int)revision : null, sampleRate, sampleCount,
			numeric.TryGetValue( 0x1b, out var rate ) ? (int)rate : null, tags.AsReadOnly() );
	}

	private static byte[] ReadBounded( Stream stream )
	{
		using var input = new MemoryStream();
		var readBuffer = new byte[81920];
		while ( true )
		{
			var count = stream.Read( readBuffer, 0, (int)Math.Min( readBuffer.Length, MaximumFileBytes - input.Length + 1 ) );
			if ( count == 0 )
				break;
			if ( count > MaximumFileBytes - input.Length )
				throw new InvalidDataException( "TGQ exceeds the input byte limit." );
			input.Write( readBuffer, 0, count );
		}
		return input.ToArray();
	}
}

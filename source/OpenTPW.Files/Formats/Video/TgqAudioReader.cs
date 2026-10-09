namespace OpenTPW;

/// <summary>
/// Sequential PCM reader over a <see cref="TgqMovieFile"/>'s SCDl blocks: decodes one EA-XA block at a time, so
/// memory stays bounded by the largest block instead of the whole soundtrack. Output is interleaved stereo s16.
/// </summary>
public sealed class TgqAudioReader
{
	public const int Channels = 2;

	private readonly TgqMovieFile movie;
	private short[] block = Array.Empty<short>();
	private int blockIndex;
	private int blockFrames;
	private int blockPosition;

	public TgqAudioReader( TgqMovieFile movie )
	{
		ArgumentNullException.ThrowIfNull( movie );
		this.movie = movie;
		TotalFrames = movie.ValidateAudio();
		SampleRate = movie.AudioHeader.SampleRate;
	}

	public int SampleRate { get; }
	/// <summary>Per-channel sample frames in the soundtrack (SCHl sample count).</summary>
	public long TotalFrames { get; }
	/// <summary>Sample frames already returned by <see cref="Read"/>.</summary>
	public long Position { get; private set; }
	public bool EndOfStream => Position >= TotalFrames;

	/// <summary>Fills <paramref name="interleaved"/> with whole stereo frames; returns the frames written (0 at the end).</summary>
	public int Read( Span<short> interleaved )
	{
		var written = 0;
		var capacity = interleaved.Length / Channels;
		while ( written < capacity )
		{
			if ( blockPosition == blockFrames )
			{
				if ( blockIndex >= movie.AudioBlockCount )
					break;
				var frames = movie.GetAudioBlockSampleCount( blockIndex );
				if ( block.Length < frames * Channels )
					block = new short[frames * Channels];
				blockFrames = movie.DecodeAudioBlock( blockIndex++, block );
				blockPosition = 0;
				continue;
			}
			var count = Math.Min( capacity - written, blockFrames - blockPosition );
			block.AsSpan( blockPosition * Channels, count * Channels ).CopyTo( interleaved[(written * Channels)..] );
			blockPosition += count;
			written += count;
		}
		Position += written;
		return written;
	}
}

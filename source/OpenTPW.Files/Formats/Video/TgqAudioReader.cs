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

	/// <summary>Fills whole stereo frames (0 at the end). A failed read restores the entry cursor;
	/// the caller must discard its potentially partially written buffer before retrying.</summary>
	public int Read( Span<short> interleaved )
	{
		var written = 0;
		var capacity = interleaved.Length / Channels;
		var startBlock = block;
		var startIndex = blockIndex;
		var startFrames = blockFrames;
		var startPosition = blockPosition;
		try
		{
			while ( written < capacity )
			{
				if ( blockPosition == blockFrames )
				{
					if ( blockIndex >= movie.AudioBlockCount )
						break;
					var frames = movie.GetAudioBlockSampleCount( blockIndex );
					if ( block.Length < frames * Channels )
						block = new short[frames * Channels];
					blockFrames = movie.DecodeAudioBlock( blockIndex, block );
					blockIndex++;
					blockPosition = 0;
					continue;
				}
				var count = Math.Min( capacity - written, blockFrames - blockPosition );
				block.AsSpan( blockPosition * Channels, count * Channels ).CopyTo( interleaved[(written * Channels)..] );
				blockPosition += count;
				written += count;
			}
		}
		catch
		{
			block = startBlock;
			blockIndex = startIndex;
			blockFrames = startFrames;
			blockPosition = startPosition;
			// A later decode can overwrite the cache before failing. EA-XA starts from its
			// own block header on every decode, so reconstruct the already valid cached block
			// only when the restored cursor still needs samples from it. No filter state persists.
			if ( startPosition < startFrames )
				movie.DecodeAudioBlock( startIndex - 1, block );
			throw;
		}

		Position += written;
		return written;
	}
}

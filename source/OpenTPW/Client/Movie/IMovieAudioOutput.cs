namespace OpenTPW;

/// <summary>
/// Audio sink for movie playback. Positions are per-channel sample frames of interleaved signed 16-bit stereo.
/// The movie clock is derived from <see cref="PlayedFrames"/>, so the sink's consumption rate drives video timing.
/// </summary>
public interface IMovieAudioOutput : IDisposable
{
	int SampleRate { get; }
	/// <summary>Frames the device has taken from the queue so far.</summary>
	long PlayedFrames { get; }
	/// <summary>Frames queued but not yet taken by the device.</summary>
	long QueuedFrames { get; }
	/// <summary>Estimated delay between a frame being taken and being heard (device buffer).</summary>
	double LatencySeconds { get; }
	void Queue( ReadOnlySpan<short> interleavedStereo );
	void Play();
	/// <summary>Stops output and discards queued audio.</summary>
	void Stop();
}

/// <summary>
/// Deterministic audio sink for headless runs and tests: consumes queued frames at <see cref="SampleRate"/>
/// (optionally scaled by <see cref="Speed"/>) only when <see cref="Advance"/> is called. Retains no PCM.
/// </summary>
public sealed class SimulatedMovieAudioOutput : IMovieAudioOutput
{
	private double fractionalFrames;

	public SimulatedMovieAudioOutput( int sampleRate, double speed = 1 )
	{
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero( sampleRate );
		if ( !double.IsFinite( speed ) || speed < 0 )
			throw new ArgumentOutOfRangeException( nameof( speed ) );
		SampleRate = sampleRate;
		Speed = speed;
	}

	public int SampleRate { get; }
	public double Speed { get; set; }
	public long PlayedFrames { get; private set; }
	public long QueuedFrames { get; private set; }
	public long MaximumQueuedFrames { get; private set; }
	public double LatencySeconds => 0;
	public bool IsPlaying { get; private set; }
	public long UnderrunFrames { get; private set; }

	public void Queue( ReadOnlySpan<short> interleavedStereo )
	{
		if ( interleavedStereo.Length % 2 != 0 )
			throw new ArgumentException( "Stereo PCM must contain whole frames.", nameof( interleavedStereo ) );
		QueuedFrames += interleavedStereo.Length / 2;
		MaximumQueuedFrames = Math.Max( MaximumQueuedFrames, QueuedFrames );
	}

	public void Play() => IsPlaying = true;

	public void Stop()
	{
		IsPlaying = false;
		QueuedFrames = 0;
	}

	/// <summary>Plays <paramref name="elapsedSeconds"/> of audio; frames requested beyond the queue count as underrun.</summary>
	public void Advance( double elapsedSeconds )
	{
		if ( !IsPlaying )
			return;
		fractionalFrames += elapsedSeconds * SampleRate * Speed;
		var wanted = (long)Math.Floor( fractionalFrames );
		fractionalFrames -= wanted;
		var taken = Math.Min( wanted, QueuedFrames );
		UnderrunFrames += wanted - taken;
		QueuedFrames -= taken;
		PlayedFrames += taken;
	}

	public void Dispose() => Stop();
}

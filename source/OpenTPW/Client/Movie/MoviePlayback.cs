namespace OpenTPW;

public enum MovieClockSource
{
	/// <summary>Movie time is the audio sink's played position.</summary>
	Audio,
	/// <summary>Movie time advances with elapsed time in <see cref="FixedStepClock"/> ticks (no audio, or after it drained).</summary>
	FixedStep,
}

/// <summary>
/// GPU-independent TGQ playback: streams audio into an <see cref="IMovieAudioOutput"/>, derives the movie clock
/// from the audio position (or a <see cref="FixedStepClock"/> without audio) and decodes only the video frame due
/// at that time. Every TQI frame is intra-coded, so late frames are dropped without being decoded and early ones
/// are held. Memory is bounded: the compressed file, one decoded frame and one audio block.
/// Playback ends at the later of the last video frame and the end of the audio; the last frame is held meanwhile
/// (plan.tgq's audio runs ~12.9 s past its video). The original player's behaviour there is unverified.
/// </summary>
public sealed class MoviePlayback : IDisposable
{
	/// <summary>Audio kept queued ahead of the device.</summary>
	public const double AudioLeadSeconds = 0.25;
	public const int AudioChunkFrames = 1024;
	/// <summary>Elapsed-time extrapolation allowed past the last audio position when the device reports no buffer size.</summary>
	public const double MinimumExtrapolationSeconds = 0.05;

	private readonly TgqMovieFile movie;
	private readonly IMovieAudioOutput? audio;
	private readonly TgqAudioReader? audioReader;
	private readonly FixedStepClock fixedClock = new();
	private readonly short[] audioChunk;
	private double fixedClockOrigin;
	private long anchorFrames = -1;
	private double anchorClock;
	private double sinceAnchor;
	private bool started;

	public MoviePlayback( TgqMovieFile movie, IMovieAudioOutput? audio )
	{
		ArgumentNullException.ThrowIfNull( movie );
		this.movie = movie;
		FrameRate = movie.AudioHeader.FrameRate ?? throw new NotSupportedException( "TGQ has no SCHl frame-rate tag (0x1B)." );
		if ( FrameRate is < 1 or > 120 )
			throw new NotSupportedException( $"TGQ frame rate {FrameRate} is outside the supported range." );
		VideoDuration = movie.VideoFrameCount / (double)FrameRate;
		if ( audio != null )
		{
			audioReader = new TgqAudioReader( movie );
			if ( audio.SampleRate != audioReader.SampleRate )
				throw new ArgumentException( "Audio output sample rate must match the movie.", nameof( audio ) );
			this.audio = audio;
			AudioDuration = audioReader.TotalFrames / (double)audioReader.SampleRate;
		}
		audioChunk = new short[AudioChunkFrames * TgqAudioReader.Channels];
		Duration = Math.Max( VideoDuration, AudioDuration );
	}

	public int FrameRate { get; }
	public int FrameCount => movie.VideoFrameCount;
	public int Width => movie.Width;
	public int Height => movie.Height;
	public double VideoDuration { get; }
	/// <summary>Soundtrack length; 0 when playing without an audio output.</summary>
	public double AudioDuration { get; }
	public double Duration { get; }
	public bool HasAudio => audio != null;
	public MovieClockSource ClockSource => audio != null && !AudioDrained ? MovieClockSource.Audio : MovieClockSource.FixedStep;
	/// <summary>Movie time in seconds.</summary>
	public double Clock { get; private set; }
	public int CurrentFrameIndex { get; private set; } = -1;
	public TqiFrame? CurrentFrame { get; private set; }
	public int FramesDecoded { get; private set; }
	public int FramesDropped { get; private set; }
	/// <summary>Updates that kept the previous frame on screen.</summary>
	public int Holds { get; private set; }
	public bool IsFinished { get; private set; }
	public bool WasSkipped { get; private set; }
	/// <summary>
	/// Linear volume 0..1 applied to the PCM before it is queued (the Movie volume option; the original maps its
	/// percentage to QuickTime's linear 0..256 movie volume). 1 leaves the samples untouched.
	/// </summary>
	public float Gain { get; set; } = 1f;
	private bool AudioDrained => audioReader!.EndOfStream && audio!.QueuedFrames == 0;

	/// <summary>
	/// Advances playback. <paramref name="elapsedSeconds"/> only drives the clock without audio (or once audio has
	/// drained); with audio the clock follows the sink. Returns true when a new frame became current.
	/// </summary>
	public bool Update( double elapsedSeconds )
	{
		if ( !double.IsFinite( elapsedSeconds ) || elapsedSeconds < 0 )
			throw new ArgumentOutOfRangeException( nameof( elapsedSeconds ) );
		if ( IsFinished )
			return false;
		if ( audio != null )
		{
			FillAudio();
			if ( !started )
				audio.Play();
		}
		if ( started )
			AdvanceClock( elapsedSeconds );
		started = true;
		if ( Clock >= Duration )
		{
			Finish();
			return false;
		}
		var target = Math.Min( FrameCount - 1, (int)Math.Floor( Clock * FrameRate + 1e-9 ) );
		if ( target <= CurrentFrameIndex )
		{
			++Holds;
			return false;
		}
		FramesDropped += target - CurrentFrameIndex - 1;
		CurrentFrame = movie.DecodeVideoFrame( target );
		CurrentFrameIndex = target;
		++FramesDecoded;
		return true;
	}

	/// <summary>Ends playback immediately (key press or click) and discards queued audio.</summary>
	public void Skip()
	{
		if ( IsFinished )
			return;
		WasSkipped = true;
		Finish();
	}

	private void AdvanceClock( double elapsedSeconds )
	{
		if ( audio != null && !AudioDrained )
		{
			// Devices take queued audio a buffer at a time, so the position moves in steps (1024 frames is ~46 ms,
			// more than a video frame). Between steps the clock runs on elapsed time, at most one buffer ahead of
			// the last position; it never runs backwards, so overshoot turns into a hold until audio catches up.
			var played = audio.PlayedFrames;
			if ( played != anchorFrames )
			{
				anchorFrames = played;
				anchorClock = played / (double)audio.SampleRate - audio.LatencySeconds;
				sinceAnchor = 0;
			}
			else
				sinceAnchor += elapsedSeconds;
			Clock = Math.Max( Clock, anchorClock + Math.Min( sinceAnchor, Math.Max( audio.LatencySeconds, MinimumExtrapolationSeconds ) ) );
			fixedClockOrigin = Clock;
			fixedClock.Reset();
			return;
		}
		fixedClock.Advance( elapsedSeconds, _ => { } );
		Clock = fixedClockOrigin + fixedClock.TickCount * FixedStepClock.TickDuration;
	}

	private void FillAudio()
	{
		var lead = (long)(AudioLeadSeconds * audio!.SampleRate);
		while ( audio.QueuedFrames < lead && !audioReader!.EndOfStream )
		{
			var frames = audioReader.Read( audioChunk );
			if ( frames == 0 )
				break;
			var samples = audioChunk.AsSpan( 0, frames * TgqAudioReader.Channels );
			ApplyGain( samples, Gain );
			audio.Queue( samples );
		}
	}

	/// <summary>Scales interleaved 16-bit PCM in place by a linear gain in 0..1.</summary>
	public static void ApplyGain( Span<short> samples, float gain )
	{
		if ( !(gain < 1f) )
			return;
		if ( !(gain > 0f) )
		{
			samples.Clear();
			return;
		}
		for ( var i = 0; i < samples.Length; i++ )
			samples[i] = (short)(samples[i] * gain);
	}

	private void Finish()
	{
		IsFinished = true;
		audio?.Stop();
	}

	/// <summary>Disposes the audio output, which the playback owns.</summary>
	public void Dispose() => audio?.Dispose();
}

namespace OpenTPW;

/// <summary>The mixer groups the original's volume controls act on.</summary>
public enum AudioChannel
{
	Effects,
	Music,
	Speech
}

/// <summary>
/// One sound in the mixer: 16-bit PCM (mono or stereo) at its own sample rate, resampled linearly to the
/// output rate. <see cref="Completed"/> runs on the mixing thread of control (the main thread) when the
/// last frame has been mixed; a sentence player uses it to start the next segment without a gap.
/// </summary>
public sealed class AudioVoice
{
	private readonly short[] samples;
	private readonly int channels;
	private readonly double step;
	private double position;

	public AudioVoice( Mp2Audio audio, AudioChannel channel, float gain, int outputRate )
	{
		ArgumentNullException.ThrowIfNull( audio );
		if ( audio.Channels is not (1 or 2) )
			throw new NotSupportedException( "Voices support mono or stereo PCM." );
		samples = audio.Samples;
		channels = audio.Channels;
		SampleRate = audio.SampleRate;
		Channel = channel;
		Gain = gain;
		step = audio.SampleRate / (double)outputRate;
		Frames = samples.Length / channels;
	}

	public AudioChannel Channel { get; }
	public int SampleRate { get; }
	public int Frames { get; }
	public float Gain { get; set; }
	public bool IsFinished { get; private set; }
	/// <summary>The output frame (mixer timeline) at which this voice's first frame was mixed, or -1 before that.</summary>
	public long StartOutputFrame { get; internal set; } = -1;
	public event Action<AudioVoice>? Completed;

	public void Stop() => IsFinished = true;

	/// <summary>Adds up to <paramref name="frames"/> output frames into <paramref name="mix"/> (interleaved stereo floats); returns the frames used before the voice ran out.</summary>
	internal int MixInto( Span<float> mix, int frames, float gain )
	{
		var total = gain * Gain;
		for ( var frame = 0; frame < frames; frame++ )
		{
			var index = (int)position;
			if ( IsFinished || index >= Frames )
			{
				IsFinished = true;
				return frame;
			}
			var fraction = (float)(position - index);
			var next = Math.Min( index + 1, Frames - 1 );
			float left, right;
			if ( channels == 1 )
			{
				// [APPROX:ADVISOR-012] Mono samples are duplicated to both channels — evidence needed: original output channel layout/panning
				left = right = samples[index] + (samples[next] - samples[index]) * fraction;
			}
			else
			{
				left = samples[2 * index] + (samples[2 * next] - samples[2 * index]) * fraction;
				right = samples[2 * index + 1] + (samples[2 * next + 1] - samples[2 * index + 1]) * fraction;
			}
			mix[2 * frame] += left * total;
			mix[2 * frame + 1] += right * total;
			position += step;
		}
		if ( position >= Frames )
			IsFinished = true;
		return frames;
	}

	internal void RaiseCompleted() => Completed?.Invoke( this );

	/// <summary>Frame within the buffer being mixed at which this voice starts (set for voices started by a completing voice).</summary>
	internal int BufferOffset { get; set; }
}

/// <summary>
/// Software mixer over one SDL queued output (<see cref="SdlMovieAudioOutput"/>): each <see cref="Pump"/>
/// tops the device queue up to <see cref="TargetQueuedFrames"/> by mixing the active voices with their
/// channel volumes. While speech plays, music and effects are lowered to the ducking level, as the
/// original's sound service does. Without an audio device the mixer still advances its clock so
/// voices finish and lip sync runs.
/// </summary>
public sealed class AudioMixer : IDisposable
{
	public const int OutputRate = 22050;
	/// <summary>Frames kept queued ahead of the device (≈ 93 ms at 22,050 Hz).</summary>
	public const int TargetQueuedFrames = 2048;
	private readonly IMovieAudioOutput? output;
	private readonly List<AudioVoice> voices = new();
	private readonly float[] channelGains = { 1, 1, 1 };
	private readonly System.Diagnostics.Stopwatch wallClock = new();
	private float[] mix = Array.Empty<float>();
	private short[] pcm = Array.Empty<short>();
	private long mixedFrames;
	private bool started;
	private bool mixing;
	private int completionOffset;

	public AudioMixer( IMovieAudioOutput? output )
	{
		this.output = output;
		if ( output != null && output.SampleRate != OutputRate )
			throw new ArgumentException( "Audio output must run at the mixer rate.", nameof( output ) );
	}

	/// <summary>Opens the default device; without one the mixer runs silently on the wall clock.</summary>
	public static AudioMixer Open()
	{
		var output = SdlMovieAudioOutput.TryOpen( OutputRate, out var failure );
		if ( output == null )
			Log.Warning( $"No audio device ({failure}); sound is silent." );
		return new AudioMixer( output ) { DeviceError = failure };
	}

	public static AudioMixer? Current { get; set; }

	public bool HasDevice => output != null;
	public string? DeviceError { get; private init; }

	/// <summary>Speech volume share applied to music and effects while a speech voice plays (0–1).</summary>
	public float DuckingLevel { get; set; } = 1;

	public IReadOnlyList<AudioVoice> Voices => voices;

	/// <summary>Output frames the listener has been given (the device's played frames, or the wall clock without a device).</summary>
	public long PlayedFrames => output?.PlayedFrames ?? (long)(wallClock.Elapsed.TotalSeconds * OutputRate);

	public void SetChannelGain( AudioChannel channel, float gain ) => channelGains[(int)channel] = Math.Clamp( gain, 0, 1 );

	public float GetChannelGain( AudioChannel channel ) => channelGains[(int)channel];

	public AudioVoice Play( Mp2Audio audio, AudioChannel channel, float gain = 1 )
	{
		var voice = new AudioVoice( audio, channel, gain, OutputRate ) { BufferOffset = mixing ? completionOffset : 0 };
		voices.Add( voice );
		return voice;
	}

	/// <summary>Seconds of <paramref name="voice"/> the listener has heard (0 before it starts).</summary>
	public double PlayedSeconds( AudioVoice voice ) =>
		voice.StartOutputFrame < 0 ? 0 : Math.Max( 0, PlayedFrames - voice.StartOutputFrame ) / (double)OutputRate;

	/// <summary>Mixes enough audio to keep the device queue at the target; call once per frame.</summary>
	public void Pump()
	{
		if ( !started )
		{
			started = true;
			output?.Play();
			wallClock.Start();
		}
		var queued = output?.QueuedFrames ?? Math.Max( 0, mixedFrames - PlayedFrames );
		var frames = (int)Math.Max( 0, TargetQueuedFrames - queued );
		if ( frames > 0 )
			Mix( frames );
	}

	/// <summary>Mixes <paramref name="frames"/> output frames now (tests drive this directly).</summary>
	internal short[] Mix( int frames )
	{
		if ( mix.Length < frames * 2 )
		{
			mix = new float[frames * 2];
			pcm = new short[frames * 2];
		}
		var buffer = mix.AsSpan( 0, frames * 2 );
		buffer.Clear();
		// [BIN:STP-PPC:0x100BB18C sound service] while speech plays (and speech is on with a volume above 0) music and the second user volume are set to volume × SoundInfo.DUCKINGLEVEL / 100
		var speaking = voices.Any( voice => voice.Channel == AudioChannel.Speech && !voice.IsFinished ) && channelGains[(int)AudioChannel.Speech] > 0;
		mixing = true;
		// Index loop: a voice that completes may start its successor, which is mixed from the frame where it ended.
		for ( var index = 0; index < voices.Count; index++ )
		{
			var voice = voices[index];
			if ( voice.IsFinished )
				continue;
			var offset = Math.Min( voice.BufferOffset, frames );
			voice.BufferOffset = 0;
			if ( voice.StartOutputFrame < 0 )
				voice.StartOutputFrame = mixedFrames + offset;
			var gain = channelGains[(int)voice.Channel];
			// [APPROX:AUDIO-001] the second user volume the original ducks is the effects channel — evidence needed: the names of the TbSysCommand volume commands at 0x100BB18C
			if ( speaking && voice.Channel != AudioChannel.Speech )
				gain *= DuckingLevel;
			var used = voice.MixInto( buffer[(offset * 2)..], frames - offset, gain );
			if ( voice.IsFinished )
			{
				completionOffset = offset + used;
				voice.RaiseCompleted();
			}
		}
		mixing = false;
		voices.RemoveAll( voice => voice.IsFinished );
		var output16 = pcm.AsSpan( 0, frames * 2 );
		for ( var index = 0; index < buffer.Length; index++ )
			output16[index] = (short)Math.Clamp( buffer[index], short.MinValue, short.MaxValue );
		mixedFrames += frames;
		output?.Queue( output16 );
		return output16.ToArray();
	}

	public void StopAll( AudioChannel? channel = null )
	{
		foreach ( var voice in voices.Where( voice => channel == null || voice.Channel == channel ) )
			voice.Stop();
	}

	public void Dispose() => output?.Dispose();
}

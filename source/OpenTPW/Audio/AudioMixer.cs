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
/// A voice given a <see cref="Position"/> (in the original's sound units: cell edges are 10) and a
/// <see cref="Range"/> is turned down by the distance law against the mixer's listener and panned by its
/// bearing; a voice without a position plays flat on both channels.
/// </summary>
public sealed class AudioVoice
{
	private readonly short[] samples;
	private readonly int channels;
	private double step;
	private readonly int outputRate;
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
		this.outputRate = outputRate;
		Frequency = audio.SampleRate;
		step = audio.SampleRate / (double)outputRate;
		Frames = samples.Length / channels;
	}

	public AudioChannel Channel { get; }
	public int SampleRate { get; }
	/// <summary>The playback frequency in Hz: the sample's rate, or after <see cref="SetPitch"/> the rate the original hands its mixer.</summary>
	public int Frequency { get; private set; }
	/// <summary>The pitch step this voice was set to (0: the sample's own rate).</summary>
	public int PitchStep { get; private set; }
	public int Frames { get; }
	public float Gain { get; set; }
	public bool IsFinished { get; private set; }
	/// <summary>The output frame (mixer timeline) at which this voice's first frame was mixed, or -1 before that.</summary>
	public long StartOutputFrame { get; internal set; } = -1;
	/// <summary>The voice's place in the original's sound units, or null for a flat (unpositioned) voice.</summary>
	public Vector3? Position { get; internal set; }
	/// <summary>The distance at which the voice is no longer heard (0: no range, always full gain).</summary>
	public float Range { get; internal set; }
	/// <summary>True when the sample wraps to its start and the voice lives on until stopped (an event flagged 0x8 loops its sample).</summary>
	public bool Loops { get; internal set; }
	public event Action<AudioVoice>? Completed;

	public void Stop() => IsFinished = true;

	/// <summary>
	/// The playback ratio of a pitch step: 1 for step 0, otherwise the float of the original's table, which holds 2^((|step| + 1) / 96) (or its inverse) so step 1 is already +25 cents.
	/// </summary>
	// [BIN:TPW-EXE:0x6d2820 mixer set-frequency wrapper] a non-zero step multiplies the sample rate by the float at 0x782f40 + 4 * step (positive) or 0x783540 + 4 * |step| (negative) and rounds to an integer for QSWaveMixSetFrequency
	// [BIN:TPW-EXE:0x782f40 pitch ratio tables] the floats are stored bit for bit below (110 entries each, read from the executable; the first positive ones are 0x3f80ed70 0x3f81dc9c 0x3f82cd85 ...): entry k is within one float ULP of 2^((k + 1) / 96) and its inverse but not always equal to it, and the index is one step off: step 1 reads entry 1, 2^(2/96), and 2^(1/96) (entry 0) is never used
	public static float PitchRatio( int step )
	{
		if ( step == 0 )
			return 1f;
		var table = step > 0 ? PositivePitchTable : NegativePitchTable;
		var index = Math.Abs( step );
		// beyond the stored entries (no shipped sound reaches them) the analytic value stands in
		return index < table.Length ? BitConverter.UInt32BitsToSingle( table[index] ) : (float)Math.Pow( 2.0, Math.Sign( step ) * (index + 1) / 96.0 );
	}

	private static readonly uint[] PositivePitchTable =
	{
		0x3F80ED70, 0x3F81DC9C, 0x3F82CD85, 0x3F83C02B, 0x3F84B496, 0x3F85AAC5, 0x3F86A2BA, 0x3F879C7C,
		0x3F88980B, 0x3F899570, 0x3F8A94AB, 0x3F8B95C4, 0x3F8C98B3, 0x3F8D9D88, 0x3F8EA43C, 0x3F8FACD6,
		0x3F90B75F, 0x3F91C3D7, 0x3F92D23D, 0x3F93E29B, 0x3F94F4F1, 0x3F960946, 0x3F971F9B, 0x3F9837EF,
		0x3F995254, 0x3F9A6EC1, 0x3F9B8D37, 0x3F9CADCD, 0x3F9DD074, 0x3F9EF534, 0x3FA01C15, 0x3FA14517,
		0x3FA27043, 0x3FA39D99, 0x3FA4CD21, 0x3FA5FEDA, 0x3FA732C6, 0x3FA868F5, 0x3FA9A15E, 0x3FAADC0A,
		0x3FAC1901, 0x3FAD5843, 0x3FAE99D0, 0x3FAFDDBA, 0x3FB123F6, 0x3FB26C8F, 0x3FB3B78D, 0x3FB504F7,
		0x3FB654C5, 0x3FB7A701, 0x3FB8FBB1, 0x3FBA52D6, 0x3FBBAC79, 0x3FBD08A2, 0x3FBE6751, 0x3FBFC886,
		0x3FC12C49, 0x3FC292A3, 0x3FC3FB9C, 0x3FC5672C, 0x3FC6D55A, 0x3FC84639, 0x3FC9B9BF, 0x3FCB2FF5,
		0x3FCCA8E3, 0x3FCE2489, 0x3FCFA2F9, 0x3FD12420, 0x3FD2A81A, 0x3FD42EE4, 0x3FD5B888, 0x3FD744FE,
		0x3FD8D456, 0x3FDA6690, 0x3FDBFBB5, 0x3FDD93CD, 0x3FDF2ED8, 0x3FE0CCDE, 0x3FE26DE7, 0x3FE411ED,
		0x3FE5B906, 0x3FE7632C, 0x3FE9106F, 0x3FEAC0C6, 0x3FEC7443, 0x3FEE2AE5, 0x3FEFE4BD, 0x3FF1A1C2,
		0x3FF361FE, 0x3FF52581, 0x3FF6EC42, 0x3FF8B652, 0x3FFA83B2, 0x3FFC5469, 0x3FFE2880, 0x40000000,
		0x4000ED74, 0x4001DCA0, 0x4002CD85, 0x4003C02B, 0x4004B496, 0x4005AAC5, 0x4006A2BA, 0x40079C7C,
		0x4008980F, 0x40099574, 0x400A94AF, 0x400B95C0, 0x400C98B3, 0x400D9D84,
	};

	private static readonly uint[] NegativePitchTable =
	{
		0x3F7E2889, 0x3F7C5469, 0x3F7A83B2, 0x3F78B652, 0x3F76EC39, 0x3F752578, 0x3F7361FE, 0x3F71A1BA,
		0x3F6FE4BD, 0x3F6E2AE5, 0x3F6C7443, 0x3F6AC0C6, 0x3F69106F, 0x3F67632C, 0x3F65B90F, 0x3F6411F5,
		0x3F626DDF, 0x3F60CCDE, 0x3F5F2EE0, 0x3F5D93D5, 0x3F5BFBBD, 0x3F5A6699, 0x3F58D456, 0x3F5744F6,
		0x3F55B888, 0x3F542EEC, 0x3F52A822, 0x3F512429, 0x3F4FA2F0, 0x3F4E2489, 0x3F4CA8E3, 0x3F4B2FFD,
		0x3F49B9B6, 0x3F484630, 0x3F46D55A, 0x3F456723, 0x3F43FB9C, 0x3F4292A3, 0x3F412C49, 0x3F3FC88E,
		0x3F3E6751, 0x3F3D08A2, 0x3F3BAC82, 0x3F3A52DF, 0x3F38FBA9, 0x3F37A701, 0x3F3654C5, 0x3F3504F7,
		0x3F33B795, 0x3F326C8F, 0x3F3123F6, 0x3F2FDDBA, 0x3F2E99D9, 0x3F2D5843, 0x3F2C18F8, 0x3F2ADC0A,
		0x3F29A155, 0x3F2868EC, 0x3F2732BE, 0x3F25FEDA, 0x3F24CD21, 0x3F239DA1, 0x3F22703B, 0x3F214520,
		0x3F201C0D, 0x3F1EF534, 0x3F1DD074, 0x3F1CADCD, 0x3F1B8D3F, 0x3F1A6EB9, 0x3F19524C, 0x3F1837F8,
		0x3F171F9B, 0x3F160946, 0x3F14F4E8, 0x3F13E293, 0x3F12D235, 0x3F11C3CE, 0x3F10B75F, 0x3F0FACD6,
		0x3F0EA433, 0x3F0D9D88, 0x3F0C98B3, 0x3F0B95C4, 0x3F0A94AB, 0x3F099579, 0x3F08980B, 0x3F079C84,
		0x3F06A2C2, 0x3F05AAC5, 0x3F04B48D, 0x3F03C02B, 0x3F02CD8E, 0x3F01DCA5, 0x3F00ED70, 0x3F000000,
		0x3EFE2889, 0x3EFC547A, 0x3EFA83B2, 0x3EF8B652, 0x3EF6EC39, 0x3EF52589, 0x3EF361FE, 0x3EF1A1BA,
		0x3EEFE4BD, 0x3EEE2AE5, 0x3EEC7454, 0x3EEAC0C6, 0x3EE9105E, 0x3EE7633D,
	};

	/// <summary>Sets the pitch step before the voice is first mixed: the frequency is the sample rate times <see cref="PitchRatio"/>, rounded to even as the original's x87 store does.</summary>
	public void SetPitch( int pitchStep )
	{
		PitchStep = pitchStep;
		Frequency = pitchStep == 0 ? SampleRate : (int)MathF.Round( (float)SampleRate * PitchRatio( pitchStep ), MidpointRounding.ToEven );
		step = Frequency / (double)outputRate;
	}

	/// <summary>Adds up to <paramref name="frames"/> output frames into <paramref name="mix"/> (interleaved stereo floats); returns the frames used before the voice ran out.</summary>
	internal int MixInto( Span<float> mix, int frames, float gain )
	{
		var total = gain * Gain;
		var leftScale = 1f;
		var rightScale = 1f;
		if ( Position is { } place && Range > 0 )
		{
			var (distanceGain, pan) = AudioListener.Placement( place, Range );
			// [EXT:inaudible-loop-advance] inaudible positioned voices advance silently; loops remain available when the listener returns, without recreating the event
			// [APPROX:AUDIO-018] constant-power pan by the source's bearing against the listener — the original's pan law (QSound's processing) is not read
			var angle = (pan + 1) * MathF.PI / 4;
			leftScale = distanceGain * MathF.Cos( angle );
			rightScale = distanceGain * MathF.Sin( angle );
		}
		for ( var frame = 0; frame < frames; frame++ )
		{
			if ( Loops && Frames > 0 && position >= Frames )
				position %= Frames;
			var index = (int)position;
			if ( IsFinished || index >= Frames )
			{
				IsFinished = true;
				return frame;
			}
			var fraction = (float)(position - index);
			var next = Loops ? (index + 1) % Frames : Math.Min( index + 1, Frames - 1 );
			float left, right;
			if ( channels == 1 )
			{
				// [APPROX:ADVISOR-012] Mono samples are duplicated to both channels; the original submits the advisor's speech unpositioned (0x51BFC0 with 0,0,0) and the library places flat sounds at (0,1,0) (0x6BC51D), the left/right levels come from QMixer (AUDIO-018) — evidence needed: QMixer or a capture
				left = right = samples[index] + (samples[next] - samples[index]) * fraction;
			}
			else
			{
				left = samples[2 * index] + (samples[2 * next] - samples[2 * index]) * fraction;
				right = samples[2 * index + 1] + (samples[2 * next + 1] - samples[2 * index + 1]) * fraction;
			}
			mix[2 * frame] += left * total * leftScale;
			mix[2 * frame + 1] += right * total * rightScale;
			position += step;
		}
		if ( position >= Frames && !Loops )
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

	/// <summary>
	/// Plays a positioned voice: the gain follows the distance law against the listener over the range
	/// (see <see cref="AudioListener.RangeGain"/>), the pan the source's bearing. A null position or a
	/// zero range plays flat. The distance and pan laws are host approximations.
	/// </summary>
	public AudioVoice Play( Mp2Audio audio, AudioChannel channel, float gain, Vector3? position, float range )
	{
		if ( position is { } place )
			AudioListener.ValidatePosition( place, nameof( position ) );
		if ( !float.IsFinite( range ) || range < 0 )
			throw new ArgumentOutOfRangeException( nameof( range ) );
		var voice = Play( audio, channel, gain );
		voice.Position = position;
		voice.Range = range;
		return voice;
	}

	/// <summary>Seconds of <paramref name="voice"/> the listener has heard (0 before it starts).</summary>
	public double PlayedSeconds( AudioVoice voice ) =>
		voice.StartOutputFrame < 0 ? 0 : Math.Max( 0, PlayedFrames - voice.StartOutputFrame ) / (double)OutputRate;

	/// <summary>Output frames in <paramref name="seconds"/> of mixer time.</summary>
	public long SecondsToFrames( double seconds ) => (long)Math.Round( seconds * OutputRate );

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
			// [BIN:STP-PPC:0x100BB18C speech ducking] while speech plays, Music = settings+0x20 × duck / 100 and User2 = settings+0x1C × duck / 100 (TbSysCommand::SetMusicVolume / SetUser2Volume); +0x1C is fed from the SFX slider (profile +0x10, PC serializer 0x423E90), so the ducked second volume is the effects channel; master and speech stay
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

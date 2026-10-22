using System.Globalization;
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
	// A catalogue sample keeps its public control handle while an out-of-range output is released.
	// Re-admission creates a fresh cursor, rather than seeking or advancing the old output silently.
	private sealed class SampleOutput { internal double Position; }
	private SampleOutput? output = new();

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

	/// <summary>The creation order among all voices of the sound system, newest highest (the voice pool breaks equal ranks by it).</summary>
	internal long Serial { get; set; } = VoiceOrder.Next();
	public AudioChannel Channel { get; }
	public int SampleRate { get; }
	/// <summary>The playback frequency in Hz: the sample's rate, or after <see cref="SetPitch"/> the rate the original hands its mixer.</summary>
	public int Frequency { get; private set; }
	/// <summary>The pitch step this voice was set to (0: the sample's own rate).</summary>
	public int PitchStep { get; private set; }
	public int Frames { get; }
	public float Gain { get; set; }
	/// <summary>The logical sample already supplied final device gain, including its enabled groups.</summary>
	internal bool HasDeviceGain { get; set; }
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

	/// <summary>The decoded PCM this voice plays (shared with the decoded-sample cache when it is cached there), for the memory counters.</summary>
	internal short[] SampleData => samples;

	/// <summary>Whether this logical voice currently owns a sample output.</summary>
	internal bool HasOutput => !IsFinished && output != null;

	public void Stop()
	{
		IsFinished = true;
		output = null;
	}

	/// <summary>Releases a sample's output without completing its logical owner or invoking a successor callback.</summary>
	internal bool ReleaseSampleOutput()
	{
		if ( !HasOutput ) return false;
		output = null;
		return true;
	}

	/// <summary>Creates a fresh output for an admitted sample; an explicit Stop is terminal.</summary>
	internal void RestartSampleOutput()
	{
		if ( IsFinished || output != null ) return;
		output = new SampleOutput();
		StartOutputFrame = -1;
		BufferOffset = 0;
	}

	/// <summary>
	/// The playback ratio of a pitch step: 1 for step 0, otherwise the float of the original's table, which holds 2^((|step| + 1) / 96) (or its inverse) so step 1 is already +25 cents.
	/// </summary>
	// [BIN:TPW-EXE:0x6d2820 mixer set-frequency wrapper] a non-zero step multiplies the sample rate by the float at 0x782f40 + 4 * step (positive) or 0x783540 + 4 * |step| (negative) and rounds to an integer for QSWaveMixSetFrequency
	// [BIN:TPW-EXE:0x782f40 pitch ratio tables] each table's entry k is the float of 2^((k + 1) / 96) (or its inverse) written with six decimals, so it can differ from the exact power by a few ULP (all 110 entries of both tables match that rule; the test pins some of them bit for bit); the index is one step off: step 1 reads entry 1, 2^(2/96), and 2^(1/96) (entry 0) is never used
	public static float PitchRatio( int step )
	{
		if ( step == 0 )
			return 1f;
		var index = Math.Min( Math.Abs( (long)step ), PitchTableLength - 1 );
		return (step > 0 ? PositivePitchTable : NegativePitchTable)[index];
	}

	// the original's tables hold 110 entries each; data steps are signed bytes, and a larger step (no shipped sound has one) is held at the last entry
	private const int PitchTableLength = 110;

	private static readonly float[] PositivePitchTable = PitchTable( 1 );

	private static readonly float[] NegativePitchTable = PitchTable( -1 );

	private static float[] PitchTable( int sign )
	{
		var table = new float[PitchTableLength];
		for ( var k = 0; k < table.Length; k++ )
			table[k] = (float)double.Parse( Math.Pow( 2.0, sign * (k + 1) / 96.0 ).ToString( "F6", CultureInfo.InvariantCulture ), CultureInfo.InvariantCulture );
		return table;
	}

	/// <summary>Sets the pitch step before the voice is first mixed: the frequency is the sample rate times <see cref="PitchRatio"/>, rounded to even as the original's x87 store does.</summary>
	public void SetPitch( int pitchStep )
	{
		PitchStep = pitchStep;
		Frequency = pitchStep == 0 ? SampleRate : Math.Max( 1, (int)MathF.Round( (float)SampleRate * PitchRatio( pitchStep ), MidpointRounding.ToEven ) );
		step = Frequency / (double)outputRate;
	}

	/// <summary>Adds up to <paramref name="frames"/> output frames into <paramref name="mix"/> (interleaved stereo floats); returns the frames used before the voice ran out.</summary>
	internal int MixInto( Span<float> mix, int frames, float gain )
	{
		if ( IsFinished || output is not { } sampleOutput ) return 0;
		var total = gain * Gain;
		var leftScale = 1f;
		var rightScale = 1f;
		if ( Position is { } place && Range > 0 )
		{
			var (distanceGain, pan) = AudioListener.Placement( place, Range );
			// Range admission belongs to SoundEventSystem, not this distance/pan stage; a released loop output is not mixed.
			// [BIN:QMIXER-DLL:0x18005830 volume and pan] tp.exe gives every voice the channel mode 0x1000000 (the software stage: 0x6bc61b ORs 1 into the request flags of every started sample, 0x6d23ab turns that into 0x1000000), whose buffer has no DirectSound 3D object (0x2000000 only): SetVolume(2000 log10(gain) hundredths of a decibel, 0x1800ca40) and, for a mono wave only (0x18005982 tests the wave format table 0x1803c6b4, 1 = mono), SetPan(44.444 x azimuth, mirrored behind the listener, 0x18005730)
			// [APPROX:AUDIO-018] the pan takes the azimuth in the listener's camera basis; the voices placed flat with the any-distance flag (speech, music, most UI sounds) keep both channels at the voice's gain, as the unpositioned original submit leaves them, though the original hands them (0,1,0) through SetPosition (0x6bc51d, 0x6d2640) and how QMixer places such a source is not read
			var levelled = AudioListener.DecibelGain( AudioListener.GainHundredthsOfDecibel( distanceGain ) );
			(leftScale, rightScale) = channels == 1 ? AudioListener.PanGains( pan ) : (1f, 1f);
			leftScale *= levelled;
			rightScale *= levelled;
		}
		for ( var frame = 0; frame < frames; frame++ )
		{
			if ( Loops && Frames > 0 && sampleOutput.Position >= Frames )
				sampleOutput.Position %= Frames;
			var index = (int)sampleOutput.Position;
			if ( IsFinished || index >= Frames )
			{
				IsFinished = true;
				return frame;
			}
			var fraction = (float)(sampleOutput.Position - index);
			var next = Loops ? (index + 1) % Frames : Math.Min( index + 1, Frames - 1 );
			float left, right;
			if ( channels == 1 )
			{
				// [APPROX:ADVISOR-012] Mono samples are duplicated to both channels; the original submits the advisor's speech unpositioned (0x51BFC0 with 0,0,0) and the library places a voice with voice flag 8 at (0,1,0) (0x6BC51D), the left/right levels come from QMixer's software stage (AUDIO-018: a source at (0,1,0) is centred only if the mixer's listener and the placeholder share a frame, and which channel mode a speech voice gets, 0x6d23ab-0x6d23bb, is not read)
				left = right = samples[index] + (samples[next] - samples[index]) * fraction;
			}
			else
			{
				left = samples[2 * index] + (samples[2 * next] - samples[2 * index]) * fraction;
				right = samples[2 * index + 1] + (samples[2 * next + 1] - samples[2 * index + 1]) * fraction;
			}
			mix[2 * frame] += left * total * leftScale;
			mix[2 * frame + 1] += right * total * rightScale;
			sampleOutput.Position += step;
		}
		if ( sampleOutput.Position >= Frames && !Loops )
			IsFinished = true;
		return frames;
	}

	internal void RaiseCompleted()
	{
		// Output release is not logical completion. A stale notification cannot finish a re-admitted loop.
		if ( IsFinished ) Completed?.Invoke( this );
	}

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
	private float[] reverbSend = Array.Empty<float>();
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
	private float duckingLevel = 1;
	public float DuckingLevel { get => duckingLevel; set { duckingLevel = Math.Clamp( value, 0, 1 ); GroupsChanged?.Invoke(); } }
	private float masterGain = 1;
	internal float MasterGain { get => masterGain; set { masterGain = Math.Clamp( value, 0, 1 ); GroupsChanged?.Invoke(); } }
	internal event Action? GroupsChanged;
	internal event Action<AudioChannel?>? Stopping;

	/// <summary>
	/// The music duck request word a ride script's DIPMUSIC sets (<see cref="GameAudio.SetMusicDuck"/>): while it is non-zero the music channel is silent.
	/// </summary>
	// [BIN:TPW-EXE:0x51BE5A music duck] the sound service (0x51BD70, applied from the main loop whenever one of its settings changed) compares the 16-bit request 0x803AD0 with 0: non-zero sends the music channel's volume as 0 (0x51BE62-0x51BE7E, or 0x51BDC2-0x51BDEC while speech ducks the others), zero sends the option volume 0x803AC0 (scaled by the speech duck 0x785914 percent in the ducking branch); the other three channels are not touched
	private ushort musicDuckRequest;
	public ushort MusicDuckRequest { get => musicDuckRequest; set { musicDuckRequest = value; GroupsChanged?.Invoke(); } }

	public IReadOnlyList<AudioVoice> Voices => voices;

	/// <summary>
	/// The reverb of the held <c>SndReverb.map</c> preset (OpenTPW's own stand-in, the audio-reverb extension): the positioned voices of the effects channel are sent through it and its return is added to the mix; flat voices, speech and music stay dry.
	/// </summary>
	public ReverbUnit Reverb { get; } = new( OutputRate );

	/// <summary>Output frames the listener has been given (the device's played frames, or the wall clock without a device).</summary>
	public long PlayedFrames => output?.PlayedFrames ?? (long)(wallClock.Elapsed.TotalSeconds * OutputRate);

	public void SetChannelGain( AudioChannel channel, float gain )
	{
		gain = Math.Clamp( gain, 0, 1 );
		if ( channelGains[(int)channel] == gain ) return;
		channelGains[(int)channel] = gain;
		GroupsChanged?.Invoke();
	}

	// [BIN:TPW-EXE:0x6B98D0,0x51BD70] group order: master, music, speech, effects; ducking changes those same factors.
	internal float GetGroupFactor( int index )
	{
		if ( index == 0 ) return masterGain;
		var channel = index switch { 1 => AudioChannel.Music, 2 => AudioChannel.Speech, 3 => AudioChannel.Effects, _ => throw new ArgumentOutOfRangeException( nameof( index ) ) };
		var gain = GetChannelGain( channel );
		if ( channel == AudioChannel.Music && MusicDuckRequest != 0 ) return 0;
		var speaking = voices.Any( voice => voice.Channel == AudioChannel.Speech && voice.HasOutput ) && GetChannelGain( AudioChannel.Speech ) > 0;
		return speaking && channel != AudioChannel.Speech ? gain * DuckingLevel : gain;
	}

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
		var speaking = voices.Any( voice => voice.Channel == AudioChannel.Speech && voice.HasOutput ) && channelGains[(int)AudioChannel.Speech] > 0;
		GroupsChanged?.Invoke();
		mixing = true;
		var reverbOn = Reverb.Active;
		if ( reverbOn )
		{
			if ( reverbSend.Length < frames * 2 )
				reverbSend = new float[frames * 2];
			reverbSend.AsSpan( 0, frames * 2 ).Clear();
		}
		var send = reverbSend.AsSpan( 0, reverbOn ? frames * 2 : 0 );
		// Index loop: a voice that completes may start its successor, which is mixed from the frame where it ended.
		for ( var index = 0; index < voices.Count; index++ )
		{
			var voice = voices[index];
			if ( !voice.HasOutput )
				continue;
			var offset = Math.Min( voice.BufferOffset, frames );
			voice.BufferOffset = 0;
			if ( voice.StartOutputFrame < 0 )
				voice.StartOutputFrame = mixedFrames + offset;
			var gain = masterGain * channelGains[(int)voice.Channel];
			if ( voice.Channel == AudioChannel.Music && MusicDuckRequest != 0 )
				gain = 0;
			// [BIN:STP-PPC:0x100BB18C speech ducking] while speech plays, Music = settings+0x20 × duck / 100 and User2 = settings+0x1C × duck / 100 (TbSysCommand::SetMusicVolume / SetUser2Volume); +0x1C is fed from the SFX slider (profile +0x10, PC serializer 0x423E90), so the ducked second volume is the effects channel; master and speech stay
			if ( speaking && voice.Channel != AudioChannel.Speech )
				gain *= DuckingLevel;
			if ( voice.HasDeviceGain ) gain = 1;
			// the positioned effects (rides, screams, staff, guests, placed ambients) go to the reverb send instead of straight into the mix; the send is added back below. Flat voices (interface clicks, the weather's own rain) stay dry
			var used = voice.MixInto( (reverbOn && voice.Channel == AudioChannel.Effects && voice.Position != null ? send : buffer)[(offset * 2)..], frames - offset, gain );
			if ( voice.IsFinished )
			{
				completionOffset = offset + used;
				voice.RaiseCompleted();
			}
		}
		mixing = false;
		if ( reverbOn )
		{
			for ( var index = 0; index < send.Length; index++ )
				buffer[index] += send[index];
			Reverb.Process( send, buffer, frames );
		}
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
		Stopping?.Invoke( channel );
		foreach ( var voice in voices.Where( voice => channel == null || voice.Channel == channel ) )
			voice.Stop();
	}

	public void Dispose()
	{
		StopAll();
		output?.Dispose();
	}
}

/// <summary>A process-wide creation counter for the sound voices (presentation only: not part of the world state or of any save).</summary>
internal static class VoiceOrder
{
	private static long last;
	internal static long Next() => Interlocked.Increment( ref last );
}

using System.Diagnostics;

namespace OpenTPW;

/// <summary>
/// Plays one decoded speech clip through the shared SDL2 queued audio output
/// (<see cref="SdlMovieAudioOutput"/>; mono speech is duplicated to both channels) and
/// reports the playback position used to drive lip sync. The position is the PCM the
/// device has taken from its queue, so it leads the speaker by at most one device buffer
/// (≈46 ms at 1,024 frames, 22,050 Hz). When no audio device can be opened the clip is
/// not heard and a wall clock drives the position.
/// </summary>
internal sealed class SpeechAudioPlayer : IDisposable
{
	private readonly IMovieAudioOutput? output;
	private readonly AudioMixer? mixer;
	private readonly Mp2Audio? mixerAudio;
	private AudioVoice? voice;
	private readonly Stopwatch clock = new();
	private readonly TimeSpan duration;
	private readonly int sampleRate;

	/// <summary>True when the clip goes to an audio output rather than the wall clock.</summary>
	public bool HasDevice => output != null || mixer?.HasDevice == true;
	public string ClockSource => mixer != null ? (mixer.HasDevice ? "game mixer (SDL audio queue)" : "game mixer (wall clock, no audio device)") : HasDevice ? "SDL audio queue" : "wall clock (no audio device)";
	public string? DeviceError { get; }
	public bool IsStarted { get; private set; }
	public bool IsFinished => IsStarted && Position >= duration;

	public TimeSpan Position
	{
		get
		{
			if ( mixer != null )
				return voice == null ? TimeSpan.Zero : TimeSpan.FromSeconds( Math.Min( mixer.PlayedSeconds( voice ), duration.TotalSeconds ) );
			// [BIN:TPW-EXE:0x0051C570 sample length] the original's mouth runs on the advisor clock, never on audio; with the sound system inactive (0x802BC8/0x802BD4 clear, set at 0x51B660 only when sound init succeeds) the sample length is 1 ms (0x51C570), there is no sound handle (0x51BFC0) and the advice ends with a sequence built from a 501 ms budget
			// [EXT:advisor-no-device-speech] wall clock for the full speech when no audio device opened (--mute and headless runs), where the original shows no speech at all (the rule is fully traced: 0x51BFC0 returns 0 and 0x51C570 returns its preset 1 without the sound system); an optional strict-parity change would use length 1 (budget 501) and no mouth
			if ( output == null )
				return clock.Elapsed < duration ? clock.Elapsed : duration;
			// [BIN:TPW-EXE:0x599880 lip clock] the original compares LIP marks with the advisor timer (0x785970 +0x50 via 0x402D80, a QueryPerformanceCounter/timeGetTime millisecond clock 0x5F5F10) minus its own paused time (0xF79784 while 0x4030C0 reports a pause), from the request - 200 ms (0x599050), never with the mixer position; the automatic advisor does so (Advisor.LipPosition)
			// [EXT:speech-tool-audio-clock] the manual advisor tools have no advisor clock and drive the mouth from the frames SDL took from its queue, which leads the speaker by up to one device buffer (≈46 ms)
			return TimeSpan.FromSeconds( (double)output.PlayedFrames / sampleRate );
		}
	}

	public SpeechAudioPlayer( Mp2Audio audio ) : this( audio, null, openDevice: AudioMixer.Current == null ) { }

	/// <summary>Plays the clip on the speech channel of <paramref name="mixer"/> (the game's sound service), which also ducks music and effects.</summary>
	internal SpeechAudioPlayer( Mp2Audio audio, AudioMixer mixer )
	{
		ArgumentNullException.ThrowIfNull( audio );
		duration = TimeSpan.FromSeconds( audio.DurationSeconds );
		sampleRate = audio.SampleRate;
		this.mixer = mixer;
		mixerAudio = audio;
	}

	/// <summary>Uses <paramref name="sink"/> (e.g. a simulated output in tests) instead of opening SDL audio.</summary>
	internal SpeechAudioPlayer( Mp2Audio audio, IMovieAudioOutput? sink, bool openDevice = false )
	{
		ArgumentNullException.ThrowIfNull( audio );
		if ( audio.Channels is not (1 or 2) )
			throw new NotSupportedException( "Speech playback supports mono or stereo PCM." );
		duration = TimeSpan.FromSeconds( audio.DurationSeconds );
		sampleRate = audio.SampleRate;
		output = sink;
		if ( output == null && openDevice )
		{
			output = SdlMovieAudioOutput.TryOpen( audio.SampleRate, out var failure );
			DeviceError = failure;
		}
		if ( output != null && output.SampleRate != audio.SampleRate )
			throw new ArgumentException( "Audio output sample rate differs from the clip.", nameof( sink ) );
		output?.Queue( ToStereo( audio ) );
	}

	internal static short[] ToStereo( Mp2Audio audio )
	{
		// [BIN:TPW-EXE:0x0051BFC0 sound start] the advisor's sample start gets three 0.0 floats (position)
		// [BIN:QMIXER-DLL:0x18005982 mono pan] the speech is a mono wave placed flat (every cat_speechSFX.map event carries the any-distance flag 0x200, 0x6bc51d): azimuth 0, SetPan(0), both channels at the same level, which duplicating the mono samples reproduces (AudioMixer, 0x18007abd, 0x18009dc0)
		if ( audio.Channels == 2 )
			return audio.Samples;
		var stereo = new short[audio.Samples.Length * 2];
		for ( var index = 0; index < audio.Samples.Length; index++ )
			stereo[2 * index] = stereo[2 * index + 1] = audio.Samples[index];
		return stereo;
	}

	/// <summary>The speech now held as the advisor's sound handle (the original's 0xF79680): the one <see cref="GamePauseSound"/> pauses and resumes; null while none plays through the game mixer.</summary>
	// [BIN:TPW-EXE:0x599050 advisor sound handle] the response player stores the handle of its speech in 0xF79680 and the response flag 0xF7966C; the stop 0x5994E0 releases both (0x51C2C0, 0x59958C-0x59959E)
	internal static SpeechAudioPlayer? AdvisorHandle { get; private set; }

	/// <summary>Pauses the speech's channel (the pause-handle message): the voice stands still where it is.</summary>
	internal void Pause()
	{
		if ( voice != null )
			voice.Paused = true;
	}

	/// <summary>Resumes the speech's channel where it stopped.</summary>
	internal void Resume()
	{
		if ( voice != null )
			voice.Paused = false;
	}

	/// <summary>Starts playback (the device stays paused until the first rendered frame).</summary>
	public void Start()
	{
		if ( IsStarted )
			return;
		IsStarted = true;
		if ( mixer != null )
		{
			voice = mixer.Play( mixerAudio!, AudioChannel.Speech );
			AdvisorHandle = this;
		}
		else if ( output != null )
			output.Play();
		else
			clock.Start();
	}

	public void Dispose()
	{
		if ( ReferenceEquals( AdvisorHandle, this ) )
		{
			AdvisorHandle = null;
			GamePauseSound.AdvisorSpeechStopped();
		}
		voice?.Stop();
		output?.Dispose();
		clock.Stop();
	}
}

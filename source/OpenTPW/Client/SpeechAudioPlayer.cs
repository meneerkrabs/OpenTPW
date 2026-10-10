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
			// [APPROX:ADVISOR-011] Wall clock when no audio device opened — evidence needed: original behaviour without sound hardware
			if ( output == null )
				return clock.Elapsed < duration ? clock.Elapsed : duration;
			// [APPROX:ADVISOR-010] Lip-sync clock = frames SDL took from its queue; leads the speaker by up to one device buffer (≈46 ms) — evidence needed: original A/V sync source and latency measurement
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
		// [APPROX:ADVISOR-012] Mono speech duplicated to both channels — evidence needed: original speech output channel layout/panning
		if ( audio.Channels == 2 )
			return audio.Samples;
		var stereo = new short[audio.Samples.Length * 2];
		for ( var index = 0; index < audio.Samples.Length; index++ )
			stereo[2 * index] = stereo[2 * index + 1] = audio.Samples[index];
		return stereo;
	}

	/// <summary>Starts playback (the device stays paused until the first rendered frame).</summary>
	public void Start()
	{
		if ( IsStarted )
			return;
		IsStarted = true;
		if ( mixer != null )
			voice = mixer.Play( mixerAudio!, AudioChannel.Speech );
		else if ( output != null )
			output.Play();
		else
			clock.Start();
	}

	public void Dispose()
	{
		voice?.Stop();
		output?.Dispose();
		clock.Stop();
	}
}

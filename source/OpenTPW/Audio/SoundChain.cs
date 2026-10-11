namespace OpenTPW;

/// <summary>
/// A held-chain event (flags 0x404): a voice that plays nothing itself and keeps a chain of one-shot
/// children, as the original's held-voice class does. The first child takes the event's first sound;
/// every later child follows the link of the current sound whose parameter range holds the current
/// <see cref="Parameter"/> (the same rule a branching sentence follows), with a fresh weighted sample.
/// A child is started a wait after the previous child's start — drawn between the sound's delay bounds,
/// not after the previous sample's end — so children may overlap, and a child that ends plays out over
/// the next one. <see cref="Stop"/> stops every live child, as the original's held-voice stop does.
/// </summary>
// [BIN:STP-PPC:sound_shared 0x10011F20 CreatePlaceHolder] the event flags 0x404 (cat_kids 71-74, the screams) make a voice that chains one-shot children
// [BIN:TPW-EXE:0x006BDAE0 held voice tick] each pass, a new child once the clock passes the newest child's time; the wait is min + draw % (max - min) from the sound's delay pair (0x100BC2D0), counted from the child's start
// [BIN:TPW-EXE:0x006C3E00 child variation] the first child takes the effect's first sound whatever the parameter; later children pick by the links' parameter ranges
// [BIN:TPW-EXE:0x006BD9B0 held stop] a held parent's stop hard-stops each child still in its chain
public sealed class SoundChain
{
	private readonly SoundEventSystem system;
	private readonly SoundCategory category;
	private readonly SoundEvent soundEvent;
	private readonly AudioChannel channel;
	private readonly Vector3? position;
	private readonly float range;
	private readonly List<AudioVoice> children = new();
	private int soundIndex;
	private int lastSample = -1;
	private long nextChildAt = -1;
	private bool stopped;

	/// <summary>The value the links are matched against (the original's per-voice parameter; for a scream, clamp((operand + script speed) / 2, 0, 100)).</summary>
	public int Parameter { get; set; }
	public bool IsStopped => stopped;

	internal SoundChain( SoundEventSystem system, SoundCategory category, SoundEvent soundEvent, AudioChannel channel, int parameter, Vector3? position, float range )
	{
		this.system = system;
		this.category = category;
		this.soundEvent = soundEvent;
		this.channel = channel;
		Parameter = parameter;
		this.position = position;
		this.range = range;
	}

	/// <summary>Starts the first child now.</summary>
	internal void Start()
	{
		nextChildAt = system.Mixer.PlayedFrames;
		Pump();
	}

	/// <summary>Starts a child once the clock has passed the newest child's time; finished children are dropped without being stopped.</summary>
	internal void Pump()
	{
		if ( stopped )
			return;
		children.RemoveAll( child => child.IsFinished );
		if ( nextChildAt < 0 || system.Mixer.PlayedFrames < nextChildAt )
			return;
		var sample = SoundEventSystem.ChooseSample( soundEvent.Sounds[soundIndex].Samples, system.Draw(), -1, avoidRepeat: false );
		if ( sample >= 0 && system.Decode( category, soundEvent.Sounds[soundIndex].Samples[sample] ) is { } audio )
		{
			var child = system.Mixer.Play( audio, channel, system.Volume( soundEvent.Sounds[soundIndex] ) / 100f, position, range );
			children.Add( child );
			lastSample = sample;
		}
		// The wait until the next child, drawn from this sound's delay bounds (milliseconds at the mixer's rate).
		nextChildAt = system.Mixer.PlayedFrames + system.Mixer.SecondsToFrames( WaitSeconds( soundEvent.Sounds[soundIndex] ) );
		var successor = SoundSentence.NextSound( soundEvent, soundIndex, Parameter, system.Draw() );
		if ( successor >= 0 && successor < soundEvent.Sounds.Count )
			soundIndex = successor;
		else
			nextChildAt = -1; // no link covers the parameter: the chain waits, as a branching sentence does
	}

	/// <summary>The wait between children: nought when both bounds are nought, otherwise min + draw % (max − min), in seconds.</summary>
	internal double WaitSeconds( SoundEntry sound )
	{
		var low = Math.Min( sound.MinimumDelay, sound.MaximumDelay );
		var high = Math.Max( sound.MinimumDelay, sound.MaximumDelay );
		if ( high == 0 )
			return 0;
		var milliseconds = low + (double)(system.Draw() % (uint)Math.Max( 1, high - low ));
		return milliseconds / 1000;
	}

	/// <summary>Retries the chain's link for the current parameter (the original restarts the walk when the parameter changes).</summary>
	public void Resume()
	{
		if ( stopped || nextChildAt >= 0 )
			return;
		var successor = SoundSentence.NextSound( soundEvent, soundIndex, Parameter, system.Draw() );
		if ( successor >= 0 && successor < soundEvent.Sounds.Count )
		{
			soundIndex = successor;
			nextChildAt = system.Mixer.PlayedFrames;
		}
	}

	public void Stop()
	{
		stopped = true;
		foreach ( var child in children )
			child.Stop();
		children.Clear();
	}
}

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
// [BIN:TPW-EXE:0x6bdb50 held chain advance] the root voice (vtable 0x70a2e0, made for flags 0x404 by 0x6b6700 → 0x6bdd30) ticks in 0x6bdae0: a child node whose end (+0x1c, its creation time + a delay drawn min + R % (max − min) from its own sound, 0x6c3a80) has passed makes the next child (0x6bdbb0); released children play out, and the stop 0x6bd9b0 stops every live child; the root is ticked only while it holds a place in the sound manager's pool (SoundEventSystem.Pump)
// [BIN:TPW-EXE:0x6bdbb0 next child] the next child starts when the newest child's end has passed and it has no successor: a new node takes its sound from 0x6c3e00 (the first child the event's first sound, [event+8]; every later one a link of the previous sound whose bytes +6/+7 hold the root's parameter byte, picked by the target sounds' weights as ror13(LCG) % total with cumulative >= pick, 0x6c3e00) and its end is the start time plus the delay of 0x6bc2d0 (u16 min + r % (max - min), or (max - min) x v / 100 + min with a parameter-bound delay slot, 0 when both are 0); the children overlap and 0x6bdc10 drops the nodes whose end has passed
// [BIN:TPW-EXE:0x6bd9b0 chain stop] the stop walks the child list, sends each child the stop command (0x6bcab0, command 1 sets the child's stopped flag 0x10) and destroys it, then stops the root, so every child ends at once
// [APPROX:AUDIO-034] when no link covers the parameter the original (0x6c3e00) writes 0x7f into the root's parameter byte, sets the root flag 0x40 (the root's tick 0x6bdae0 does nothing while flags & 0x50 is set) and drops the half-made child; what clears 0x40 and starts the walk again (OpenTPW resumes when the parameter is set) and whether the forced 0x7f is visible to the script's next parameter call are not read
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
	/// <summary>The chain's place (null: flat) and range, for the voice pool (<see cref="SoundEventSystem.Pump"/>).</summary>
	internal Vector3? Position => position;
	internal SoundEvent Event => soundEvent;
	internal float Range => range;

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
	/// <summary>Makes the first child due: it starts in the next pooled tick of <see cref="SoundEventSystem.Pump"/>, as the original makes it in the root's tick.</summary>
	internal void Start() => nextChildAt = system.Mixer.PlayedFrames;

	/// <summary>Starts a child once the clock has passed the newest child's time; finished children are dropped without being stopped.</summary>
	internal void Pump()
	{
		if ( stopped )
			return;
		children.RemoveAll( child => child.IsFinished );
		if ( nextChildAt < 0 || system.Mixer.PlayedFrames < nextChildAt )
			return;
		var sample = system.PickSample( soundEvent.Sounds[soundIndex].Samples, -1, avoidRepeat: false );
		if ( sample >= 0 && system.Decode( category, soundEvent.Sounds[soundIndex].Samples[sample] ) is { } audio )
		{
			// [BIN:TPW-EXE:0x006BBF60 placeholder setup] a chain's placeholder has the table of its event and sound like every placeholder; the shipped chain sounds bind no selector, so they draw
			var table = SoundParameterTable.ForPlayback( soundEvent, soundEvent.Sounds[soundIndex] );
			var volume = system.Volume( soundEvent.Sounds[soundIndex], table ) / 100f;
			var child = system.Mixer.Play( audio, channel, volume, position, range );
			// [BIN:TPW-EXE:0x6bc410 sample start] volume, then pitch, then (below) the wait: the order of the original's draws
			child.SetPitch( system.Pitch( soundEvent.Sounds[soundIndex], table ) );
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
		var milliseconds = low + (double)system.DrawBelow( (uint)Math.Max( 1, high - low ) );
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

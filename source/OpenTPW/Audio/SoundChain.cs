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
public sealed class SoundChain
{
	/// <summary>The creation order among all voices (see <see cref="SoundEventSystem.ComparePoolOrder"/>).</summary>
	internal long Serial { get; } = VoiceOrder.Next();
	private readonly SoundEventSystem system;
	private readonly SoundCategory category;
	private readonly SoundEvent soundEvent;
	private readonly AudioChannel channel;
	private Vector3? position;
	private readonly float range;
	private readonly List<AudioVoice> children = new();
	private int soundIndex;
	private int lastSample = -1;
	private long nextChildAt = -1;
	private bool stopped;
	private bool started;
	private bool halted;

	/// <summary>The value the links are matched against (the original's per-voice parameter; for a scream, clamp((operand + script speed) / 2, 0, 100)).</summary>
	public int Parameter
	{
		get => parameter;
		set
		{
			parameter = value;
			// [BIN:TPW-EXE:0x6bda90 held-chain parameter setter] the root's setter (vtable 0x70a2e0 +0x28) writes the slot (0x6bbb80) and then clears the root flag 0x40 unless the stored byte is 0x7f, so a parameter set after a halt restarts the walk at the next tick
			if ( halted && value != HaltedParameter )
				halted = false;
		}
	}
	private int parameter;
	public bool IsStopped => stopped;
	/// <summary>The chain's place (null: flat) and range, for the voice pool (<see cref="SoundEventSystem.Pump"/>).</summary>
	internal Vector3? Position => position;

	/// <summary>Moves the held chain (0x5512D0): the children it makes from now on, and the ones playing, take the place.</summary>
	internal void MoveTo( Vector3 place )
	{
		if ( stopped )
			return;
		position = place;
		foreach ( var child in children )
			child.Position = place;
	}
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
		if ( stopped || halted )
			return;
		children.RemoveAll( child => child.IsFinished );
		if ( nextChildAt < 0 || system.Mixer.PlayedFrames < nextChildAt )
			return;
		if ( started )
		{
			// [BIN:TPW-EXE:0x6bdbb0 next child] the link is walked when the next child is made, with the parameter of that moment (not when the previous child started)
			switch ( NextLink( soundEvent, soundIndex, Parameter, system.DrawBelow, out var successor ) )
			{
				case LinkWalk.Terminal:
					// [BIN:TPW-EXE:0x6c3e00 terminal sound] a sound without any link clears the walk's position and fails without a flag; the half-made child is dropped and the next attempt, the next pooled tick, starts the event's first sound again
					started = false;
					return;
				case LinkWalk.Uncovered:
					// [BIN:TPW-EXE:0x6c3f49 uncovered parameter] links exist but none holds the parameter: 0x7f goes into the root's parameter byte and the root flag 0x40 is set; its tick 0x6bdae0 does nothing while flags & 0x50, so the root makes no child until a parameter is set to something other than 0x7f (the root's setter 0x6bda90 clears 0x40 then, see Parameter); the voice lives until it is stopped
					parameter = HaltedParameter;
					halted = true;
					return;
				default:
					soundIndex = successor;
					break;
			}
		}
		else
			soundIndex = 0;
		started = true;
		var sample = system.PickSample( soundEvent.Sounds[soundIndex].Samples, -1, avoidRepeat: false );
		if ( sample >= 0 && system.Decode( category, soundEvent.Sounds[soundIndex].Samples[sample] ) is { } audio )
		{
			// [BIN:TPW-EXE:0x006BBF60 placeholder setup] a chain's placeholder has the table of its event and sound like every placeholder; the shipped chain sounds bind no selector, so they draw
			var table = SoundParameterTable.ForPlayback( soundEvent, soundEvent.Sounds[soundIndex] );
			var volume = system.Volume( soundEvent.Sounds[soundIndex], table ) / 100f;
			var child = system.Mixer.Play( audio, channel, volume, position, range );
			// [BIN:TPW-EXE:0x6bc410 sample start] volume, then pitch, then (below) the wait: the order of the original's draws
			child.ForEvent( soundEvent.GroupMask );
			child.SetPitch( system.Pitch( soundEvent.Sounds[soundIndex], table ) );
			children.Add( child );
			lastSample = sample;
		}
		// The wait until the next child, drawn from this sound's delay bounds (milliseconds at the mixer's rate).
		nextChildAt = system.Mixer.PlayedFrames + system.Mixer.SecondsToFrames( WaitSeconds( soundEvent.Sounds[soundIndex] ) );
	}

	/// <summary>The value the root's parameter byte takes when no link covers the parameter (tp.exe 0x6c3f4c).</summary>
	internal const int HaltedParameter = 0x7f;

	/// <summary>True once no link covered the parameter: the chain makes no further child until a parameter other than 0x7f is set.</summary>
	internal bool IsHalted => halted;

	/// <summary>The outcome of walking the links of the newest child's sound.</summary>
	internal enum LinkWalk
	{
		/// <summary>A covering link was picked.</summary>
		Next,
		/// <summary>The sound has no link at all.</summary>
		Terminal,
		/// <summary>The sound has links but none holds the parameter.</summary>
		Uncovered
	}

	/// <summary>
	/// Walks the links of <paramref name="current"/> for <paramref name="parameter"/>: the links whose [minimum, maximum] byte range holds it are
	/// candidates, picked by the weights of their target sounds (the running sum against ror13(state) % total, the draw only made when a link covers).
	/// </summary>
	// [BIN:TPW-EXE:0x6c3e00 link walk] the covering links are counted (bytes +6 and +7 against the parameter), the weight total of their targets is summed, the pick is ror(state, 13) % total (0x6c3ece-0x6c3edf) and the first covering link whose running sum reaches it is taken (0x6c3f14)
	internal static LinkWalk NextLink( SoundEvent soundEvent, int current, int parameter, Func<uint, uint> drawBelow, out int next )
	{
		next = -1;
		var links = soundEvent.Sounds[current].Links;
		ArgumentNullException.ThrowIfNull( links );
		if ( links.Count == 0 )
			return LinkWalk.Terminal;
		var covering = links.Where( link => link.MinimumParameter <= parameter && parameter <= link.MaximumParameter
			&& link.TargetSound >= 1 && link.TargetSound <= soundEvent.Sounds.Count ).ToList();
		if ( covering.Count == 0 )
			return LinkWalk.Uncovered;
		var total = (uint)covering.Sum( link => (long)soundEvent.Sounds[(int)link.TargetSound - 1].Weight );
		// a zero total would fault the original's division; the first covering link stands without a draw
		var pick = total == 0 ? 0u : drawBelow( total );
		uint sum = 0;
		foreach ( var link in covering )
		{
			sum += soundEvent.Sounds[(int)link.TargetSound - 1].Weight;
			if ( sum >= pick )
			{
				next = (int)link.TargetSound - 1;
				return LinkWalk.Next;
			}
		}
		next = (int)covering[^1].TargetSound - 1;
		return LinkWalk.Next;
	}

	/// <summary>The wait between children: nought when both bounds are nought, otherwise min + draw % (max − min), in seconds (<see cref="SoundEventSystem.DelaySeconds"/>).</summary>
	internal double WaitSeconds( SoundEntry sound ) => system.DelaySeconds( sound );

	public void Stop()
	{
		stopped = true;
		foreach ( var child in children )
			child.Stop();
		children.Clear();
	}
}

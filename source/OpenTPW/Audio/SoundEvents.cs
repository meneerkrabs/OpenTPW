namespace OpenTPW;

/// <summary>
/// One loaded sound category (<c>cat_&lt;name&gt;SFX.map</c> with its <c>cat_&lt;name&gt;BANK.map</c>), as the
/// original registers them (for example <c>cat_ui</c> from <c>global/sound</c>, <c>cat_music</c> from a
/// level's <c>Music</c> folder). Bank names such as <c>Sound\UI</c> are resolved below the folder that
/// holds the map's folder, with the <c>HD</c> quality suffix of the shipped banks, and fall back to <c>global</c>.
/// </summary>
public sealed class SoundCategory
{
	private readonly BaseFileSystem fileSystem;
	private readonly string root;
	private readonly IReadOnlyList<string> bankNames;
	private readonly Dictionary<int, SoundBank?> banks = new();

	private SoundCategory( BaseFileSystem fileSystem, string name, string root, SoundCatalog catalog, IReadOnlyList<string> bankNames )
	{
		this.fileSystem = fileSystem;
		Name = name;
		this.root = root;
		Catalog = catalog;
		this.bankNames = bankNames;
	}

	public string Name { get; }
	public SoundCatalog Catalog { get; }
	public IReadOnlyList<string> BankNames => bankNames;

	/// <summary>Loads <c>&lt;directory&gt;/&lt;name&gt;SFX.map</c> and <c>…BANK.map</c>; returns null (with a log line) when either is missing or damaged.</summary>
	public static SoundCategory? TryLoad( BaseFileSystem fileSystem, string directory, string name )
	{
		var sfx = $"{directory}/{name}SFX.map";
		var bank = $"{directory}/{name}BANK.map";
		try
		{
			if ( !fileSystem.FileExists( sfx ) || !fileSystem.FileExists( bank ) )
			{
				Log.Trace( $"Sound category {name}: no {sfx}." );
				return null;
			}
			var root = directory.Contains( '/' ) ? directory[..directory.LastIndexOf( '/' )] : "";
			return new SoundCategory( fileSystem, name, root, new SoundCatalog( fileSystem.ReadAllBytes( sfx ) ), SoundCatalog.ReadBankNames( fileSystem.ReadAllBytes( bank ) ) );
		}
		catch ( Exception exception ) when ( exception is IOException or InvalidDataException or UnauthorizedAccessException )
		{
			Log.Warning( $"Sound category {name} ignored: {exception.Message}" );
			return null;
		}
	}

	/// <summary>The bank a sample's 1-based bank number refers to, loaded on first use.</summary>
	public SoundBank? GetBank( int bankNumber )
	{
		lock ( banks )
			return LoadBank( bankNumber );
	}

	private SoundBank? LoadBank( int bankNumber )
	{
		if ( banks.TryGetValue( bankNumber, out var cached ) )
			return cached;
		SoundBank? bank = null;
		if ( bankNumber >= 1 && bankNumber <= bankNames.Count )
		{
			var relative = bankNames[bankNumber - 1].Replace( '\\', '/' );
			// [APPROX:AUDIO-002] banks resolve to <map folder's parent>/<name>HD.sdt, then to global/<name>HD.sdt — evidence needed: TbMapStreamer::BankDoesNotExist (0x10015234) and the quality suffix rule
			foreach ( var candidate in new[] { $"{root}/{relative}HD.sdt", $"/global/{relative}HD.sdt" } )
			{
				if ( fileSystem.FileExists( candidate ) )
				{
					bank = new SoundBank( fileSystem.ReadAllBytes( candidate ) );
					break;
				}
			}
			if ( bank == null )
				Log.Trace( $"Sound category {Name}: bank {relative} not found." );
		}
		banks[bankNumber] = bank;
		return bank;
	}
}

/// <summary>
/// Plays catalogue events the way the original's placeholders do: a sound is chosen among the event's
/// sounds and a sample among the sound's samples by cumulative weight, using the top 16 bits of the
/// library's linear congruential generator; the volume is a random percentage in the sound's range.
/// One-shot events play once; sentence events chain samples, and branching sentences (music) follow
/// the sound's links whose parameter range holds the current parameter.
/// </summary>
public sealed class SoundEventSystem
{
	private readonly AudioMixer mixer;
	private readonly Dictionary<(SoundCategory, int, uint), Mp2Audio?> decoded = new();
	private uint seed;
	/// <summary>Samples shorter than this (≈ 10 s at 22,050 Hz) stay decoded in memory.</summary>
	public const int CacheFrameLimit = 220500;
	private readonly List<SoundSentence> sentences = new();

	/// <summary>Starts decoded segments that were waiting for a background decode; call once per frame.</summary>
	public void Pump()
	{
		foreach ( var sentence in sentences.ToArray() )
			sentence.Pump();
		sentences.RemoveAll( sentence => sentence.IsStopped );
	}

	public SoundEventSystem( AudioMixer mixer, uint seed = 1 )
	{
		this.mixer = mixer;
		this.seed = seed;
	}

	public AudioMixer Mixer => mixer;

	// [BIN:STP-PPC:sound_shared 0x1000FCB4 ChooseRandomSample] draw = (seed × 0x19660D + 0x3C6EF35F) >> 16 (32-bit); the first sample whose cumulative weight is >= draw wins
	internal uint Draw()
	{
		seed = unchecked(seed * 0x19660D + 0x3C6EF35F);
		return seed >> 16;
	}

	internal static int ChooseSample( IReadOnlyList<SoundSample> samples, uint draw, int lastIndex, bool avoidRepeat )
	{
		if ( samples.Count == 0 )
			return -1;
		if ( samples.Count == 1 )
			return 0;
		for ( var index = 0; index < samples.Count; index++ )
		{
			if ( draw <= samples[index].CumulativeWeight )
				// with more than two samples a sentence does not repeat the previous one
				return avoidRepeat && samples.Count > 2 && index == lastIndex ? (index + 1) % samples.Count : index;
		}
		return -1;
	}

	// [BIN:STP-PPC:sound_shared 0x1000FF40 ChooseRandomSound] sounds are chosen by the running sum of their weights (+0x1E) against the same 16-bit draw
	internal static int ChooseSound( IReadOnlyList<SoundEntry> sounds, uint draw )
	{
		if ( sounds.Count <= 1 )
			return sounds.Count - 1;
		uint sum = 0;
		for ( var index = 0; index < sounds.Count; index++ )
		{
			sum += sounds[index].Weight;
			if ( draw <= sum )
				return index;
		}
		return 0;
	}

	// [BIN:STP-PPC:sound_shared 0x1000F32C GetRandomVolume] volume = min + random % (max - min), in percent
	internal int Volume( SoundEntry sound )
	{
		var low = Math.Min( sound.MinimumVolume, sound.MaximumVolume );
		var range = Math.Abs( sound.MaximumVolume - sound.MinimumVolume );
		return range == 0 ? low : (int)(low + unchecked(seed = seed * 0x19660D + 0x3C6EF35F) % (uint)range);
	}

	/// <summary>Decodes a sample (thread safe; short samples are cached, long ones such as music segments are not).</summary>
	internal Mp2Audio? Decode( SoundCategory category, SoundSample sample )
	{
		var key = (category, (int)sample.BankNumber, sample.SampleNumber);
		lock ( decoded )
		{
			if ( decoded.TryGetValue( key, out var cached ) )
				return cached;
		}
		Mp2Audio? audio = null;
		var mpeg = category.GetBank( sample.BankNumber )?.GetMpeg( sample.SampleNumber );
		if ( mpeg != null )
		{
			try
			{
				audio = Mp2Decoder.Decode( mpeg );
			}
			catch ( Exception exception ) when ( exception is InvalidDataException or NotSupportedException )
			{
				Log.Trace( $"Sound {category.Name} bank {sample.BankNumber} sample {sample.SampleNumber}: {exception.Message}" );
			}
		}
		if ( audio == null || audio.SampleFrames < CacheFrameLimit )
		{
			lock ( decoded )
				decoded[key] = audio;
		}
		return audio;
	}

	/// <summary>Plays a one-shot event (or the first sample of a sentence event, once). Returns the voice, or null when nothing plays.</summary>
	public AudioVoice? Play( SoundCategory? category, uint eventId, AudioChannel channel )
	{
		var soundEvent = category?.Catalog.Find( eventId );
		if ( category == null || soundEvent == null || soundEvent.Sounds.Count == 0 )
			return null;
		var sound = soundEvent.Sounds[Math.Max( 0, ChooseSound( soundEvent.Sounds, Draw() ) )];
		var index = ChooseSample( sound.Samples, Draw(), -1, avoidRepeat: false );
		if ( index < 0 || Decode( category, sound.Samples[index] ) is not { } audio )
			return null;
		// [APPROX:AUDIO-003] pitch, delay, 3D position and reverb of a sound are not applied — evidence needed: TbSoundSampleInfo pitch units and the placeholder 3D update
		return mixer.Play( audio, channel, Volume( sound ) / 100f );
	}

	/// <summary>Starts a sentence event (music); null when the event is missing.</summary>
	public SoundSentence? StartSentence( SoundCategory? category, uint eventId, AudioChannel channel )
	{
		var soundEvent = category?.Catalog.Find( eventId );
		if ( category == null || soundEvent == null || soundEvent.Sounds.Count == 0 )
			return null;
		var sentence = new SoundSentence( this, category, soundEvent, channel );
		sentences.Add( sentence );
		sentence.Start();
		return sentence;
	}
}

/// <summary>
/// A playing sentence: plays one sample of the current sound, then the next sample — of the same sound
/// for a plain sentence, of the sound a matching link leads to for a branching sentence — on the frame
/// the previous one ends. The next sample is chosen and decoded in the background while the current one
/// plays, so long music segments never stall a frame.
/// </summary>
public sealed class SoundSentence
{
	private readonly SoundEventSystem system;
	private readonly SoundCategory category;
	private readonly SoundEvent soundEvent;
	private readonly AudioChannel channel;
	private int soundIndex;
	private int lastSample = -1;
	private AudioVoice? voice;
	private Task<(int Sound, int Sample, Mp2Audio? Audio)>? next;
	private bool waiting;

	internal SoundSentence( SoundEventSystem system, SoundCategory category, SoundEvent soundEvent, AudioChannel channel )
	{
		this.system = system;
		this.category = category;
		this.soundEvent = soundEvent;
		this.channel = channel;
	}

	/// <summary>The value branching links are matched against (the original's parameter 4; for park music the guest count / 2, capped).</summary>
	public int Parameter { get; set; }
	public int CurrentSound => soundIndex;
	public bool IsPlaying => voice is { IsFinished: false };
	public bool IsStopped { get; private set; }
	public SoundEvent Event => soundEvent;

	internal void Start()
	{
		// [BIN:STP-PPC:sound_shared 0x100192F0 AssignSoundToNextBranch] a branching sentence starts on the event's first sound
		var first = soundEvent.Player == SoundEventPlayer.BranchingSentence ? 0 : Math.Max( 0, SoundEventSystem.ChooseSound( soundEvent.Sounds, system.Draw() ) );
		Prefetch( first );
		waiting = true;
	}

	public void Stop()
	{
		IsStopped = true;
		voice?.Stop();
		voice = null;
	}

	/// <summary>The next sound after <paramref name="current"/> for <paramref name="parameter"/>, or -1 when no link covers it.</summary>
	// [BIN:STP-PPC:sound_shared 0x100192F0 AssignSoundToNextBranch] links whose [byte 6, byte 7] range holds the parameter are candidates, chosen by their target sound's weight; with none the sentence waits
	internal static int NextSound( SoundEvent soundEvent, int current, int parameter, uint draw )
	{
		var links = soundEvent.Sounds[current].Links.Where( link => link.MinimumParameter <= parameter && parameter <= link.MaximumParameter
			&& link.TargetSound >= 1 && link.TargetSound <= soundEvent.Sounds.Count ).ToList();
		if ( links.Count == 0 )
			return -1;
		var total = (uint)links.Sum( link => (long)soundEvent.Sounds[(int)link.TargetSound - 1].Weight );
		if ( total == 0 )
			return (int)links[0].TargetSound - 1;
		var pick = draw % total;
		uint sum = 0;
		foreach ( var link in links )
		{
			sum += soundEvent.Sounds[(int)link.TargetSound - 1].Weight;
			if ( pick <= sum )
				return (int)link.TargetSound - 1;
		}
		return (int)links[^1].TargetSound - 1;
	}

	/// <summary>Chooses the sample of <paramref name="sound"/> to play next and starts decoding it.</summary>
	private void Prefetch( int sound )
	{
		var samples = soundEvent.Sounds[sound].Samples;
		var sample = SoundEventSystem.ChooseSample( samples, system.Draw(), sound == soundIndex ? lastSample : -1, avoidRepeat: sound == soundIndex && lastSample >= 0 );
		next = sample < 0 ? null : Task.Run( () => (sound, sample, system.Decode( category, samples[sample] )) );
	}

	/// <summary>The sound that follows the current one, or -1 for a branching sentence with no link for the parameter.</summary>
	private int Successor() => soundEvent.Player == SoundEventPlayer.BranchingSentence
		? NextSound( soundEvent, soundIndex, Parameter, system.Draw() )
		: soundIndex;

	/// <summary>Plays the prefetched sample if it is decoded; returns false when it is not ready yet.</summary>
	private bool TryPlayNext()
	{
		if ( IsStopped || next is not { IsCompleted: true } task )
			return false;
		next = null;
		var (sound, sample, audio) = task.Result;
		if ( audio == null )
			return true;
		soundIndex = sound;
		lastSample = sample;
		voice = system.Mixer.Play( audio, channel, system.Volume( soundEvent.Sounds[sound] ) / 100f );
		voice.Completed += _ => OnCompleted();
		// [APPROX:AUDIO-005] the next segment is chosen (with the parameter at that moment) when the current one starts, so it can be decoded in time — evidence needed: when CPlaceHolderSentence::SoundCallback (sound_shared 0x1001A1F0) runs relative to the end of a sample
		var successor = Successor();
		if ( successor >= 0 )
			Prefetch( successor );
		return true;
	}

	private void OnCompleted()
	{
		voice = null;
		if ( IsStopped )
			return;
		waiting = !TryPlayNext();
	}

	internal void Pump()
	{
		if ( IsStopped )
			return;
		if ( waiting && TryPlayNext() )
			waiting = false;
	}

	/// <summary>Restarts a branching sentence that stopped for lack of a matching link (the original retries when the parameter changes).</summary>
	public void Resume()
	{
		if ( IsStopped || next != null )
			return;
		var successor = Successor();
		if ( successor >= 0 )
		{
			Prefetch( successor );
			waiting = voice == null;
		}
	}
}

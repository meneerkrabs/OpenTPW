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
/// Plays catalogue events the way the original's placeholders do: by default a sound is chosen among the event's
/// sounds and a sample among the sound's samples by cumulative weight, using the top 16 bits of the
/// library's linear congruential generator; the volume is a random percentage in the sound's range.
/// One-shot events play once; sentence events chain samples, and branching sentences (music) follow
/// the sound's links whose parameter range holds the current parameter.
/// </summary>
public sealed class SoundEventSystem
{
	private readonly AudioMixer mixer;
	private readonly Dictionary<(SoundCategory, int, uint), Mp2Audio?> decoded = new();
	private readonly LinkedList<(SoundCategory, int, uint)> longDecodedOrder = new();
	private long retainedSentenceBytes;
	private uint seed;
	/// <summary>Samples shorter than this (≈ 10 s at 22,050 Hz) stay decoded in memory.</summary>
	public const int CacheFrameLimit = 220500;
	internal const int LongCacheByteLimit = 8 * 1024 * 1024;
	internal long RetainedSentenceBytes { get { lock ( decoded ) return retainedSentenceBytes; } }
	/// <summary>Bytes of every decoded sample kept in memory, short and long (for the memory accounting of the browser harness).</summary>
	public long DecodedBytes { get { lock ( decoded ) return decoded.Values.Sum( audio => (long)(audio?.Samples.Length ?? 0) * sizeof( short ) ); } }
	private readonly List<SoundSentence> sentences = new();
	private readonly List<SoundChain> chains = new();
	private readonly List<PlacedSound> placements = new();

	/// <summary>Starts decoded segments that were waiting for an asynchronous decode; call once per frame.</summary>
	public void Pump()
	{
		foreach ( var placement in placements.ToArray() )
		{
			if ( placement.Pump() )
				placements.Remove( placement );
		}
		foreach ( var sentence in sentences.ToArray() )
			sentence.Pump();
		sentences.RemoveAll( sentence => sentence.IsStopped );
		foreach ( var chain in chains.ToArray() )
		{
			try
			{
				chain.Pump();
			}
			catch
			{
				chain.Stop();
				chains.Remove( chain );
				throw;
			}
		}
		chains.RemoveAll( chain => chain.IsStopped );
	}

	public SoundEventSystem( AudioMixer mixer, uint seed = 1 )
	{
		this.mixer = mixer;
		this.seed = seed;
	}

	public AudioMixer Mixer => mixer;

	/// <summary>Current chooser seed (saved with the park; see <see cref="WorldRandomState"/>).</summary>
	public uint Seed => seed;

	/// <summary>Restarts the chooser from <paramref name="value"/> (a park's <see cref="WorldSeed.SoundStream"/> or a saved seed).</summary>
	public void Reseed( uint value ) => seed = value;

	// [BIN:STP-PPC:sound_shared 0x1000FCB4 ChooseRandomSample] draw = (seed × 0x19660D + 0x3C6EF35F) >> 16 (32-bit); the first sample whose cumulative weight is >= draw wins
	// [BIN:STP-PPC:sound_shared 0x100118C8 seed store] the successor is never stored: the only store to CAudioPlaceHolder::mRandomSeed (TOC+0x42E4) is the library init (0x100118B4, from LbTime_GetClock); the eight LCG sites only load it and no library imports the symbol, so every draw is the same until the seed is set again
	internal uint Draw() => unchecked(seed * 0x19660D + 0x3C6EF35F) >> 16;

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
	internal int Volume( SoundEntry sound, byte? parameterCode = null, int parameter = 0 )
	{
		var low = Math.Min( sound.MinimumVolume, sound.MaximumVolume );
		var range = Math.Abs( sound.MaximumVolume - sound.MinimumVolume );
		// [BIN:STP-PPC:sound_shared 0x1000F2E0 GetParameterValue] the first slot whose +24/+28 mask contains bit 1 supplies volume; selectors +22/+26 are truncated to bytes when loaded (0x100100D8–0x100100EC)
		byte? volumeCode = null;
		if ( sound.Raw.Length >= 30 )
		{
			if ( (sound.Raw[24] & 1) != 0 )
				volumeCode = sound.Raw[22];
			else if ( (sound.Raw[28] & 1) != 0 )
				volumeCode = sound.Raw[26];
		}
		// [BIN:STP-PPC:sound_shared 0x1000F3C8–0x1000F3F4 GetRandomVolume] a matching parameter yields low + floor((high-low) * byte value / 100), including the high endpoint at 100
		if ( parameterCode != null && volumeCode == parameterCode )
			return low + range * unchecked((byte)parameter) / 100;
		return range == 0 ? low : (int)(low + unchecked(seed * 0x19660D + 0x3C6EF35F) % (uint)range);
	}

	private bool TryDecoded( SoundCategory category, SoundSample sample, out Mp2Audio? audio )
	{
		var key = (category, (int)sample.BankNumber, sample.SampleNumber);
		lock ( decoded )
		{
			if ( !decoded.TryGetValue( key, out audio ) )
				return false;
			if ( longDecodedOrder.Find( key ) is { } entry )
			{
				longDecodedOrder.Remove( entry );
				longDecodedOrder.AddLast( entry );
			}
			return true;
		}
	}

	private static readonly object Ended = new();
	private readonly System.Runtime.CompilerServices.ConditionalWeakTable<SoundCategory, object> ended = new();

	internal Mp2Audio? RememberDecoded( SoundCategory category, SoundSample sample, Mp2Audio? audio, bool retainLong )
	{
		var key = (category, (int)sample.BankNumber, sample.SampleNumber);
		lock ( decoded )
		{
			if ( ended.TryGetValue( category, out _ ) )
				return audio;
			if ( decoded.TryGetValue( key, out var existing ) )
				return existing;
			if ( audio == null || audio.SampleFrames < CacheFrameLimit )
				decoded[key] = audio;
			else if ( retainLong )
			{
				var bytes = audio.Samples.LongLength * sizeof( short );
				if ( bytes > LongCacheByteLimit )
					return audio;
				while ( retainedSentenceBytes + bytes > LongCacheByteLimit )
				{
					var oldest = longDecodedOrder.First!;
					retainedSentenceBytes -= decoded[oldest.Value]!.Samples.LongLength * sizeof( short );
					decoded.Remove( oldest.Value );
					longDecodedOrder.RemoveFirst();
				}
				decoded[key] = audio;
				longDecodedOrder.AddLast( key );
				retainedSentenceBytes += bytes;
			}
		}
		return audio;
	}

	/// <summary>
	/// Forgets every decoded sample of the given categories. A park's and the lobby's own categories end when they are left; their
	/// cache entries would otherwise keep each category, and with it its sound banks, alive for the rest of the session.
	/// </summary>
	public void Forget( params SoundCategory?[] categories )
	{
		var gone = categories.Where( category => category != null ).ToHashSet();
		if ( gone.Count == 0 )
			return;
		lock ( decoded )
		{
			// A decode still running for one of them must not put it back (the table does not keep them alive).
			foreach ( var category in gone )
				ended.AddOrUpdate( category!, Ended );
			foreach ( var key in decoded.Keys.Where( key => gone.Contains( key.Item1 ) ).ToList() )
			{
				if ( longDecodedOrder.Find( key ) is { } entry )
				{
					retainedSentenceBytes -= decoded[key]!.Samples.LongLength * sizeof( short );
					longDecodedOrder.Remove( entry );
				}
				decoded.Remove( key );
			}
		}
	}

	/// <summary>Decodes a sample (thread safe); long sentence samples share an additional bounded PCM cache.</summary>
	internal Mp2Audio? Decode( SoundCategory category, SoundSample sample )
		=> Decode( category, sample, retainLong: false );

	private Mp2Audio? Decode( SoundCategory category, SoundSample sample, bool retainLong )
	{
		if ( TryDecoded( category, sample, out var cached ) )
			return cached;
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
		return RememberDecoded( category, sample, audio, retainLong );
	}

	internal Task<Mp2Audio?> DecodeSentenceAsync( SoundCategory category, SoundSample sample, bool? cooperative = null, Func<Task>? yieldBatch = null )
	{
		if ( TryDecoded( category, sample, out var cached ) )
			return Task.FromResult( cached );
		return (cooperative ?? OperatingSystem.IsBrowser())
			? DecodeCooperatively( category, sample, yieldBatch )
			: Task.Run( () => Decode( category, sample, retainLong: true ) );
	}

	private async Task<Mp2Audio?> DecodeCooperatively( SoundCategory category, SoundSample sample, Func<Task>? yieldBatch )
	{
		Mp2Audio? audio = null;
		var mpeg = category.GetBank( sample.BankNumber )?.GetMpeg( sample.SampleNumber );
		if ( mpeg != null )
		{
			try
			{
				audio = await Mp2Decoder.DecodeCooperativelyAsync( mpeg, yieldBatch: yieldBatch );
			}
			catch ( Exception exception ) when ( exception is InvalidDataException or NotSupportedException )
			{
				Log.Trace( $"Sound {category.Name} bank {sample.BankNumber} sample {sample.SampleNumber}: {exception.Message}" );
			}
		}
		return RememberDecoded( category, sample, audio, retainLong: true );
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
		// [APPROX:AUDIO-003] pitch, delay and reverb of a sound are not applied — evidence needed: TbSoundSampleInfo pitch units and the placeholder 3D update
		return mixer.Play( audio, channel, Volume( sound ) / 100f );
	}

	/// <summary>
	/// Plays a one-shot event at a place: the voice carries <paramref name="position"/> and — unless a
	/// range is given — the chosen sound's own range (the float at the sound record's +34, the far end the
	/// original hands its mixer), and the mixer turns it down over its distance from the listener.
	/// </summary>
	public AudioVoice? PlayAt( SoundCategory? category, uint eventId, AudioChannel channel, Vector3 position, float? range = null )
	{
		var soundEvent = category?.Catalog.Find( eventId );
		if ( category == null || soundEvent == null || soundEvent.Sounds.Count == 0 )
			return null;
		var sound = soundEvent.Sounds[Math.Max( 0, ChooseSound( soundEvent.Sounds, Draw() ) )];
		var index = ChooseSample( sound.Samples, Draw(), -1, avoidRepeat: false );
		if ( index < 0 || Decode( category, sound.Samples[index] ) is not { } audio )
			return null;
		// [DATA:global/sound/cat_ambientSFX.map] events 7 and 8 (the waterfall and river) are one-shots (no 0x4) flagged 0x8
		// [LEAD:maexah/OpenTPW docs/exe/audio.md "A voice's range" table: bit 0x8 loops the sample, the voice lives on] PC addresses unverifiable (packed exe)
		// [APPROX:AUDIO-019] the loop is applied to positioned one-shots only; a flat one-shot with 0x8 still plays once — evidence needed: the Mac flat path's flag test beside 0x0051C130
		var voice = mixer.Play( audio, channel, Volume( sound ) / 100f, position, range ?? sound.Rolloff );
		voice.Loops = (soundEvent.Flags & 0x8) != 0;
		return voice;
	}

	/// <summary>
	/// <see cref="PlayAt"/> without waiting for the sample: the sound and sample are drawn now (the same draws, in the same order), a
	/// sample not decoded yet is decoded like a sentence's (cooperatively in the browser, on a worker on the desktop) and the voice
	/// starts at the <see cref="Pump"/> that finds it ready. A park's placed ambients start this way, so their decoding (about a tenth
	/// of a second for the jungle in the browser) is not part of the park's first frame.
	/// </summary>
	public PlacedSound? PlayAtWhenDecoded( SoundCategory? category, uint eventId, AudioChannel channel, Vector3 position, float? range = null )
	{
		var soundEvent = category?.Catalog.Find( eventId );
		if ( category == null || soundEvent == null || soundEvent.Sounds.Count == 0 )
			return null;
		var sound = soundEvent.Sounds[Math.Max( 0, ChooseSound( soundEvent.Sounds, Draw() ) )];
		var index = ChooseSample( sound.Samples, Draw(), -1, avoidRepeat: false );
		if ( index < 0 )
			return null;
		// [APPROX:AUDIO-019] as in PlayAt: the loop flag 0x8 applies to positioned one-shots
		var placed = new PlacedSound( mixer, channel, Volume( sound ) / 100f, position, range ?? sound.Rolloff, (soundEvent.Flags & 0x8) != 0,
			DecodeSentenceAsync( category, sound.Samples[index] ) );
		if ( !placed.Pump() )
			placements.Add( placed );
		return placed;
	}

	/// <summary>Starts a sentence event; an optional chooser replaces only its sample draws. Null when the event is missing.</summary>
	/// <param name="sampleDraw">Optional 16-bit sample chooser; does not alter sound selection, branch links or volume parameters.</param>
	public SoundSentence? StartSentence( SoundCategory? category, uint eventId, AudioChannel channel, Func<uint>? sampleDraw = null )
	{
		var soundEvent = category?.Catalog.Find( eventId );
		if ( category == null || soundEvent == null || soundEvent.Sounds.Count == 0 )
			return null;
		var sentence = new SoundSentence( this, category, soundEvent, channel, sampleDraw );
		sentences.Add( sentence );
		sentence.Start();
		return sentence;
	}

	/// <summary>Stops every held chain still running (a park ends with its screams).</summary>
	public void StopChains()
	{
		foreach ( var chain in chains )
			chain.Stop();
		chains.Clear();
	}

	/// <summary>Starts a held-chain event (a scream) at a place; null when the event is missing.</summary>
	public SoundChain? StartChain( SoundCategory? category, uint eventId, AudioChannel channel, int parameter, Vector3? position, float? range = null )
	{
		var soundEvent = category?.Catalog.Find( eventId );
		if ( category == null || soundEvent == null || soundEvent.Sounds.Count == 0 )
			return null;
		var effectiveRange = range ?? soundEvent.Sounds[0].Rolloff;
		if ( position is { } place )
			AudioListener.ValidatePosition( place, nameof( position ) );
		if ( !float.IsFinite( effectiveRange ) || effectiveRange < 0 )
			throw new ArgumentOutOfRangeException( nameof( range ) );
		var chain = new SoundChain( this, category, soundEvent, channel, parameter, position, effectiveRange );
		try
		{
			chain.Start();
		}
		catch
		{
			chain.Stop();
			throw;
		}
		chains.Add( chain );
		return chain;
	}
}

/// <summary>
/// A playing sentence: plays one sample of the current sound, then the next sample — of the same sound
/// for a plain sentence, of the sound a matching link leads to for a branching sentence — on the frame
/// the previous one ends. The next sample is chosen and decoded ahead while the current one plays:
/// native hosts decode in a background task, and browser hosts yield between bounded MPEG frame batches.
/// </summary>
/// <summary>A positioned sound that starts once its sample is decoded (<see cref="SoundEventSystem.PlayAtWhenDecoded"/>); it can be stopped before.</summary>
public sealed class PlacedSound
{
	private readonly AudioMixer mixer;
	private readonly AudioChannel channel;
	private readonly float volume;
	private readonly Vector3 position;
	private readonly float range;
	private readonly bool loops;
	private readonly Task<Mp2Audio?> decoding;
	private bool stopped;

	internal PlacedSound( AudioMixer mixer, AudioChannel channel, float volume, Vector3 position, float range, bool loops, Task<Mp2Audio?> decoding )
	{
		this.mixer = mixer;
		this.channel = channel;
		this.volume = volume;
		this.position = position;
		this.range = range;
		this.loops = loops;
		this.decoding = decoding;
	}

	/// <summary>The voice once the sample is decoded and playing; null before, or when the sample could not be decoded.</summary>
	public AudioVoice? Voice { get; private set; }

	/// <summary>Starts the voice when the sample is ready; true once nothing is left to wait for.</summary>
	internal bool Pump()
	{
		if ( stopped )
			return true;
		if ( !decoding.IsCompleted )
			return false;
		if ( decoding.IsFaulted )
			Log.Warning( $"A placed sound was not started: its sample could not be decoded ({decoding.Exception?.GetBaseException().Message})." );
		else if ( decoding.IsCompletedSuccessfully && decoding.Result is { } audio )
		{
			Voice = mixer.Play( audio, channel, volume, position, range );
			Voice.Loops = loops;
		}
		return true;
	}

	public void Stop()
	{
		stopped = true;
		Voice?.Stop();
	}
}

public sealed class SoundSentence
{
	private readonly SoundEventSystem system;
	private readonly SoundCategory category;
	private readonly SoundEvent soundEvent;
	private readonly AudioChannel channel;
	private readonly Func<uint> sampleDraw;
	private int soundIndex;
	private int lastSample = -1;
	private AudioVoice? voice;
	private Task<(int Sound, int Sample, Mp2Audio? Audio)>? next;
	private bool waiting;

	internal SoundSentence( SoundEventSystem system, SoundCategory category, SoundEvent soundEvent, AudioChannel channel, Func<uint>? sampleDraw = null )
	{
		this.system = system;
		this.category = category;
		this.soundEvent = soundEvent;
		this.channel = channel;
		this.sampleDraw = sampleDraw ?? system.Draw;
	}

	/// <summary>The value branching links are matched against (the original's parameter 4; for park music the guest count / 2, capped).</summary>
	public int Parameter { get; set; }
	/// <summary>The known native code of <see cref="Parameter"/>; only the crowd's code 7 is currently wired.</summary>
	public byte? ParameterCode { get; set; }
	public int CurrentSound => soundIndex;
	internal int CurrentSample => lastSample;
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
		var sample = SoundEventSystem.ChooseSample( samples, sampleDraw(), sound == soundIndex ? lastSample : -1, avoidRepeat: sound == soundIndex && lastSample >= 0 );
		next = sample < 0 ? null : DecodeNext( sound, sample );
	}

	private async Task<(int Sound, int Sample, Mp2Audio? Audio)> DecodeNext( int sound, int sample ) =>
		(sound, sample, await system.DecodeSentenceAsync( category, soundEvent.Sounds[sound].Samples[sample] ));

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
		// [APPROX:AUDIO-036] density-controlled crowd gain is evaluated when a segment starts; native parameter propagation into an already-playing sentence child is unverified
		voice = system.Mixer.Play( audio, channel, system.Volume( soundEvent.Sounds[sound], ParameterCode, Parameter ) / 100f );
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

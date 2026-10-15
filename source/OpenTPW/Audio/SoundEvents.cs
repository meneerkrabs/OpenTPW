namespace OpenTPW;

/// <summary>
/// One loaded sound category (<c>cat_&lt;name&gt;SFX.map</c> with its <c>cat_&lt;name&gt;BANK.map</c>), as the
/// original registers them (for example <c>cat_ui</c> from <c>global/sound</c>, <c>cat_music</c> from a
/// level's <c>Music</c> folder). Bank names such as <c>Sound\UI</c> are resolved below the folder that
/// holds the map's folder, with the <c>HD</c> quality suffix of the shipped banks; a bank whose map record carries the Global flag (0x20) lives under <c>global</c> instead, and there is no fallback between the two.
/// </summary>
public sealed class SoundCategory
{
	private readonly BaseFileSystem fileSystem;
	private readonly string root;
	private readonly IReadOnlyList<SoundBankName> bankNames;
	private readonly Dictionary<int, SoundBank?> banks = new();

	private SoundCategory( BaseFileSystem fileSystem, string name, string root, SoundCatalog catalog, IReadOnlyList<SoundBankName> bankNames )
	{
		this.fileSystem = fileSystem;
		Name = name;
		this.root = root;
		Catalog = catalog;
		this.bankNames = bankNames;
	}

	public string Name { get; }
	public SoundCatalog Catalog { get; }
	public IReadOnlyList<string> BankNames => bankNames.Select( bank => bank.Name ).ToList();

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
			return new SoundCategory( fileSystem, name, root, new SoundCatalog( fileSystem.ReadAllBytes( sfx ) ), SoundCatalog.ReadBankMap( fileSystem.ReadAllBytes( bank ) ) );
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
			var entry = bankNames[bankNumber - 1];
			var relative = entry.Name.Replace( '\\', '/' );
			// [BIN:TPW-EXE:0x6bff14] a bank record's flag 0x20 selects the data root's Global folder, every other bank is under the category's own folder (the map folder's parent); there is no second folder to try: the open at 0x6b9310 makes one existence test (0x6c5a00) and returns 0 when it fails
			// [BIN:TPW-EXE:0x6c58f0] the quality suffix: <name>HW.sdt first, HD.sdt as fallback, reversed when the manager's flags +0x28 have bits 0 and 1; only HD files ship
			// [DATA:global/sound,levels/*/Sound cat_*BANK.map] no shipped bank record carries 0x20 (flags 1, 2, 4, 0x41, 0x42), and every shipped name resolves under the map folder's parent
			var candidate = entry.Global ? $"/global/{relative}HD.sdt" : $"{root}/{relative}HD.sdt";
			if ( fileSystem.FileExists( candidate ) )
			{
				// Read entry by entry from the file, not held whole (a bank is tens of megabytes).
				bank = new SoundBank( () => fileSystem.OpenRead( candidate ), fileSystem.GetSize( candidate ) );
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
/// sounds and a sample among the sound's samples by cumulative weight, using the top 16 bits of the rotated state of the
/// library's one shared, advancing linear congruential generator; the volume is a random percentage in the sound's range.
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
			{
				placements.Remove( placement );
				if ( placement.Voice is { } started )
					oneShots.Add( (started, placement.Priority, placement.Flags) );
			}
		}
		oneShots.RemoveAll( entry => entry.Voice.IsFinished );
		StopVoicesOutOfRange();
		foreach ( var sentence in sentences.ToArray() )
			sentence.Pump();
		sentences.RemoveAll( sentence => sentence.IsStopped );
		SelectPooledChains();
		foreach ( var chain in chains.ToArray() )
		{
			if ( !pooledChains.Contains( chain ) )
				continue;
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

	/// <summary>The event flag that makes a voice audible at any distance (voice flag 8 of the original, set by 0x6bbe90 from the event's 0x200).</summary>
	internal const ushort AnyDistanceFlag = 0x200;

	/// <summary>
	/// A voice out of the listener's range whose event lacks <see cref="AnyDistanceFlag"/> has its sample stopped, for every voice type: one-shots here,
	/// sentences in <see cref="SoundSentence.Pump"/>, held chains by no longer starting children. Looping voices keep advancing silently (see below).
	/// </summary>
	// [BIN:TPW-EXE:0x6bca10 audibility test] a voice whose range² is below the ground distance and whose +0x30 bit 8 is clear goes to 0x6bca70, which sets +0x30 |= 0x800 and, for a playing voice with a sample, posts the type-3 message (vtable 0x70a2d0) to the mixer manager; in range, or with flag 8, it clears 0x800 and only updates the playing sample's 3D parameters (0x6bba90)
	// [BIN:TPW-EXE:0x6b8d60 type-3 handler] the message looks the sample handle up in the manager's slot table (0x8c bytes each, 0x6bab50) and runs 0x6b7e60 -> 0x6bb7c0 on the slot: stop the wave (vf+0x2c), release it, clear the slot's playing bit; the same message and handler serve the muted-group case (0x6bc760), which also clears the voice's handle at +0x24, and 0x6ba390 runs 0x6bb7c0 over every slot to stop all
	// [BIN:TPW-EXE:0x6bbe90 voice flags from the event] the event's flag 0x200 becomes voice flag 8 (any distance), 0x8 becomes 6 (loops, with 0x4 clear of the expiry test), 0x4 becomes 2
	// [DATA:global/sound,levels/*/Sound cat_*SFX.map] 0x200 is set on the crowd sentence (kids 91), music, speech and most UI events, never on a chain, a scream, a ride or staff sound
	// [APPROX:AUDIO-038] looping voices are not stopped: the range test also reaches them in the original, but nothing read restarts a looped sample after the listener returns, and tp.exe's waterfall evidently resumes, so the restart path is unknown
	private void StopVoicesOutOfRange()
	{
		foreach ( var (voice, _, flags) in oneShots )
			if ( !voice.Loops && voice.Position != null && voice.Range > 0 && (flags & AnyDistanceFlag) == 0 && !Audible( voice.Position, voice.Range ) )
				voice.Stop();
	}

	/// <summary>The voices the sound manager updates per tick (tp.exe <c>0x6b8380</c> passes 12 to the pool set-up <c>0x6b6050</c>).</summary>
	public const int VoicePool = 12;

	/// <summary>
	/// A voice's distance word in the pool: 0xFFFF − round(d² / 100) over the ground distance to the listener (tp.exe <c>0x6bb9f0</c>), 0xFFFF for a flat
	/// voice or one at the listener, and 0 past the scale (the original leaves its old word there).
	/// </summary>
	internal static int PoolKey( Vector3? position )
	{
		if ( position is not { } place )
			return 0xFFFF;
		var squared = GroundDistanceSquared( place );
		if ( squared <= 0f )
			return 0xFFFF;
		var scaled = Math.Round( squared * 0.01f, MidpointRounding.ToEven );
		return scaled > 0xFFFF ? 0 : 0xFFFF - (int)scaled;
	}

	/// <summary>
	/// A voice's rank in the pool: the event priority (<c>+0xC</c> of the event record) in the high word and the distance word in the low one, compared
	/// as one unsigned dword (tp.exe <c>0x6bb9f0</c> stores the priority at <c>+0x22</c> beside the distance word at <c>+0x20</c>; the admission <c>0x6b6bc0</c>
	/// compares the dword at <c>+0x20</c>), so priority decides and distance breaks ties.
	/// </summary>
	internal static uint PoolRank( uint priority, Vector3? position ) => ((priority & 0xFFFF) << 16) | (uint)PoolKey( position );

	private static float GroundDistanceSquared( Vector3 place )
	{
		var listener = AudioListener.Position;
		var dx = place.X - listener.X;
		var dz = place.Z - listener.Z;
		return dx * dx + dz * dz;
	}

	/// <summary>Whether a voice at <paramref name="position"/> with <paramref name="range"/> is heard: flat, no range, or the listener within the range over the ground.</summary>
	internal static bool Audible( Vector3? position, float range ) =>
		position is not { } place || range <= 0 || GroundDistanceSquared( place ) <= range * range;

	/// <summary>Whether the pool admits a voice: audible, or flagged audible at any distance (0x6bca10, flag 8 at 0x6bca44).</summary>
	internal static bool Admitted( Vector3? position, float range, ushort flags ) => (flags & AnyDistanceFlag) != 0 || Audible( position, range );

	private readonly List<(AudioVoice Voice, uint Priority, ushort Flags)> oneShots = new();
	private readonly List<(object Voice, uint Rank)> poolCandidates = new();
	private readonly HashSet<SoundChain> pooledChains = new();
	private static readonly Comparison<(object Voice, uint Rank)> ByRankDescending = ( a, b ) => b.Rank.CompareTo( a.Rank );

	/// <summary>
	/// Collects the held chains among the <see cref="VoicePool"/> highest-ranked audible voices this tick. A chain outside them, or out of the listener's range,
	/// starts no new child; the children it already started play out.
	/// </summary>
	// [BIN:TPW-EXE:0x6b62b0 sound manager tick] only the voices in the pool of 12 are ticked; the pool is rebuilt every tick (0x6b6ba0), a voice is admitted (0x6b6bc0) only while audible (0x6bca10: the listener within the voice's range, the map sound's float +34, over the ground) and ranked by the dword priority:distance of 0x6bb9f0; a held chain's next child, its first included, is made in its tick (0x6bdae0 → 0x6bdb50 → 0x6bdbb0)
	// [APPROX:AUDIO-038] the pool ranks the held chains with the running sentences, placed sounds and one-shots started through this system; the admission's order among equal ranks (0x6b6bc0) is not modelled, and a voice with the any-distance flag is admitted whatever its distance (0x6bca44)
	private void SelectPooledChains()
	{
		pooledChains.Clear();
		if ( chains.Count == 0 )
			return;
		poolCandidates.Clear();
		foreach ( var sentence in sentences )
			if ( !sentence.IsStopped && Admitted( sentence.Position, sentence.Range, sentence.Event.Flags ) )
				poolCandidates.Add( (sentence, PoolRank( sentence.Event.Priority, sentence.Position )) );
		foreach ( var placement in placements )
			if ( Admitted( placement.Position, placement.Range, placement.Flags ) )
				poolCandidates.Add( (placement, PoolRank( placement.Priority, placement.Position )) );
		foreach ( var (voice, priority, flags) in oneShots )
			if ( !voice.IsFinished && Admitted( voice.Position, voice.Range, flags ) )
				poolCandidates.Add( (voice, PoolRank( priority, voice.Position )) );
		foreach ( var chain in chains )
			if ( !chain.IsStopped && Admitted( chain.Position, chain.Range, chain.Event.Flags ) )
				poolCandidates.Add( (chain, PoolRank( chain.Event.Priority, chain.Position )) );
		poolCandidates.Sort( ByRankDescending );
		for ( var index = 0; index < Math.Min( VoicePool, poolCandidates.Count ); index++ )
			if ( poolCandidates[index].Voice is SoundChain chain )
				pooledChains.Add( chain );
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

	// [BIN:TPW-EXE:0x6bc6ce] one shared chooser for every catalogue draw: s = s * 0x19660D + 0x3C6EF35F, stored back into the shared state (0xfb1f20) on every draw, and the draw is ror(s, 13), then >> 16 (weights: sound 0x6bc810, sample 0x6bc680) or % (max - min) (ranges: volume 0x6bc090, pitch 0x6bc170, delay 0x6bc343); the same state serves the sentence link choices (0x6be450, 0x6c3e00)
	// [BIN:TPW-EXE:0x72e09c] the state is seeded once at static initialisation (0x6bb930 -> 0x5f5f10, QueryPerformanceCounter) and is never saved
	// [APPROX:AUDIO-037] OpenTPW seeds the chooser from the clock when the sound service starts (as the original) but reseeds it from the park's world seed at park entry (GameAudio.EnterPark) and saves it with the park, so headless runs and tests start from a known state; with sound on, the draws follow wall-clock events (mixer-timed sentence segments, UI clicks), so the state is not a replay guarantee; the original's chooser is clock-seeded and unsaved
	// [EXT:audio-chooser-mac-reading] the Mac reading of this chooser (the seed is never stored and the draw takes the top 16 bits without the rotate, STP-PPC sound_shared 0x1000FCB4 / 0x100118C8) contradicts the PC instructions; the PC reading is used and the Mac reading is to be rechecked (docs/AUDIO.md)
	private uint Advance()
	{
		seed = unchecked(seed * 0x19660D + 0x3C6EF35F);
		return System.Numerics.BitOperations.RotateRight( seed, 13 );
	}

	/// <summary>A 16-bit weight draw: <c>ror(s, 13) &gt;&gt; 16</c> of the advanced state (0..65535).</summary>
	internal uint Draw() => Advance() >> 16;

	/// <summary>A range draw: <c>ror(s, 13) % range</c> of the advanced state (the range's maximum is never drawn).</summary>
	internal uint DrawBelow( uint range ) => Advance() % range;

	/// <summary>The sound an event plays: the only sound draws nothing (0x6bc822), otherwise the weight draw picks it.</summary>
	internal int PickSound( IReadOnlyList<SoundEntry> sounds ) => sounds.Count <= 1 ? sounds.Count - 1 : ChooseSound( sounds, Draw() );

	/// <summary>The sample of a sound: the only sample draws nothing (0x6bc69d), otherwise the weight draw (<paramref name="draw"/> or the shared chooser) picks it.</summary>
	internal int PickSample( IReadOnlyList<SoundSample> samples, int lastIndex, bool avoidRepeat, Func<uint>? draw = null )
		=> samples.Count <= 1 ? samples.Count - 1 : ChooseSample( samples, draw != null ? draw() : Draw(), lastIndex, avoidRepeat );

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
		// [BIN:TPW-EXE:0x6bc090] volume = min + ror13(chooser) % (max - min); an empty range draws nothing (0x6bc11b)
		return range == 0 ? low : (int)(low + DrawBelow( (uint)range ));
	}

	/// <summary>
	/// The pitch step of a playback: the bounds are put in order (signed), then min + r % (max - min), an empty range drawing nothing; the draw follows the volume's.
	/// </summary>
	// [BIN:TPW-EXE:0x6bc170 pitch draw] swaps the signed bytes +0xE/+0xF of the sound record when max < min, range = max - min, and without a parameter-bound pitch slot (the vf+0x40 query for parameter kind 2) draws ror13(chooser) % range + min; 0x6bc410 calls it right after the volume draw (0x6bc090) and hands it to 0x6c5170, unless the placeholder flag 0x10000 fixes the pitch
	// [BIN:TPW-EXE:0x6bc410 sample start] the order of draws for one playback is sound, sample, volume, pitch, then the delay (0x6bc343)
	// [DATA:global/sound,levels/*/Sound cat_*SFX.map] 1,343 shipped sounds have no pitch range; the chain, scream and crowd sounds draw 0..6 (steps 0-5 are played), the others signed ranges from -24..36 to -2..0
	// [APPROX:AUDIO-003] the placeholder flag 0x10000 (pitch fixed at the short +0x4A) and a parameter-bound pitch slot are not modelled: every playback draws from the range
	internal int Pitch( SoundEntry sound )
	{
		var low = Math.Min( sound.MinimumPitch, sound.MaximumPitch );
		var range = Math.Abs( sound.MaximumPitch - sound.MinimumPitch );
		return range == 0 ? low : low + (int)DrawBelow( (uint)range );
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
		var bank = category.GetBank( sample.BankNumber );
		var rejectedPayloadBytes = 0;
		var mpeg = bank?.GetMpeg( sample.SampleNumber, Mp2Decoder.MaximumInputBytes, out rejectedPayloadBytes );
		try
		{
			if ( rejectedPayloadBytes > 0 )
				throw new InvalidDataException( $"Selected sound entry contains {rejectedPayloadBytes} bytes, exceeding the caller's {Mp2Decoder.MaximumInputBytes}-byte input bound." );
			if ( mpeg != null ) audio = Mp2Decoder.Decode( mpeg );
		}
		catch ( Exception exception ) when ( exception is InvalidDataException or NotSupportedException )
		{
			Log.Trace( $"Sound {category.Name} bank {sample.BankNumber} sample {sample.SampleNumber}: {exception.Message}" );
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
		var bank = category.GetBank( sample.BankNumber );
		var rejectedPayloadBytes = 0;
		var mpeg = bank?.GetMpeg( sample.SampleNumber, Mp2Decoder.MaximumInputBytes, out rejectedPayloadBytes );
		try
		{
			if ( rejectedPayloadBytes > 0 )
				throw new InvalidDataException( $"Selected sound entry contains {rejectedPayloadBytes} bytes, exceeding the caller's {Mp2Decoder.MaximumInputBytes}-byte input bound." );
			if ( mpeg != null ) audio = await Mp2Decoder.DecodeCooperativelyAsync( mpeg, yieldBatch: yieldBatch );
		}
		catch ( Exception exception ) when ( exception is InvalidDataException or NotSupportedException )
		{
			Log.Trace( $"Sound {category.Name} bank {sample.BankNumber} sample {sample.SampleNumber}: {exception.Message}" );
		}
		return RememberDecoded( category, sample, audio, retainLong: true );
	}

	/// <summary>Plays a one-shot event (or the first sample of a sentence event, once). Returns the voice, or null when nothing plays.</summary>
	public AudioVoice? Play( SoundCategory? category, uint eventId, AudioChannel channel )
	{
		var soundEvent = category?.Catalog.Find( eventId );
		if ( category == null || soundEvent == null || soundEvent.Sounds.Count == 0 )
			return null;
		var sound = soundEvent.Sounds[Math.Max( 0, PickSound( soundEvent.Sounds ) )];
		var index = PickSample( sound.Samples, -1, avoidRepeat: false );
		if ( index < 0 || Decode( category, sound.Samples[index] ) is not { } audio )
			return null;
		// [APPROX:AUDIO-003] the pitch is applied (Pitch, AudioVoice.SetPitch); the delay of non-chain sounds (u16 + r % range, 0x6bc343) and reverb (SndReverb.map) are not applied, chain delays are milliseconds (video, 2026-10-15)
		// [BIN:TPW-EXE:0x6bc4e7 sample setup] the fixed (flat) branch and the positioned branch both reach the loop test 0x6bc621: a placeholder with flag 0x8 (+0x30 bit 4) loops its sample (0x6c5110, 9999 times), so a flat one-shot loops exactly as a positioned one does
		// [EXT:inaudible-loop-advance] a loop whose source is inaudible is assumed to keep advancing silently and resume when the listener returns; the early exit 0x6bcb60 -> 0x6bc643 that skips starting in some cases is not read
		var volume = Volume( sound ) / 100f;
		var voice = mixer.Play( audio, channel, volume );
		voice.SetPitch( Pitch( sound ) );
		voice.Loops = (soundEvent.Flags & 0x8) != 0;
		oneShots.Add( (voice, soundEvent.Priority, soundEvent.Flags) );
		return voice;
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
		var sound = soundEvent.Sounds[Math.Max( 0, PickSound( soundEvent.Sounds ) )];
		var index = PickSample( sound.Samples, -1, avoidRepeat: false );
		if ( index < 0 || Decode( category, sound.Samples[index] ) is not { } audio )
			return null;
		// [DATA:global/sound/cat_ambientSFX.map] events 7 and 8 (the waterfall and river) are one-shots (no 0x4) flagged 0x8
		// [LEAD:maexah/OpenTPW docs/exe/audio.md "A voice's range" table: bit 0x8 loops the sample, the voice lives on] PC addresses unverifiable (packed exe)
		// [BIN:TPW-EXE:0x6bc621 loop test] flag 0x8 loops the sample after both the positioned and the fixed branch (see Play)
		var volume = Volume( sound ) / 100f;
		var voice = mixer.Play( audio, channel, volume, position, range ?? sound.Rolloff );
		voice.SetPitch( Pitch( sound ) );
		voice.Loops = (soundEvent.Flags & 0x8) != 0;
		oneShots.Add( (voice, soundEvent.Priority, soundEvent.Flags) );
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
		var sound = soundEvent.Sounds[Math.Max( 0, PickSound( soundEvent.Sounds ) )];
		var index = PickSample( sound.Samples, -1, avoidRepeat: false );
		if ( index < 0 )
			return null;
		// [BIN:TPW-EXE:0x6bc621 loop test] as in PlayAt: the loop flag 0x8 applies
		var volume = Volume( sound ) / 100f;
		var pitch = Pitch( sound );
		var placed = new PlacedSound( mixer, channel, volume, position, range ?? sound.Rolloff, (soundEvent.Flags & 0x8) != 0,
			DecodeSentenceAsync( category, sound.Samples[index] ) ) { Priority = soundEvent.Priority, Flags = soundEvent.Flags, Pitch = pitch };
		if ( !placed.Pump() )
			placements.Add( placed );
		else if ( placed.Voice is { } started )
			oneShots.Add( (started, soundEvent.Priority, soundEvent.Flags) );
		return placed;
	}

	/// <summary>Starts a sentence event; an optional chooser replaces only its sample draws. Null when the event is missing.</summary>
	/// <param name="sampleDraw">Optional 16-bit sample chooser; does not alter sound selection, branch links or volume parameters.</param>
	/// <param name="position">Optional sound-space place: a positioned sentence plays every segment there (a scape.omp placed sentence), turned down over its distance from the listener.</param>
	/// <param name="range">The positioned sentence's range; the first sound's own range when null.</param>
	public SoundSentence? StartSentence( SoundCategory? category, uint eventId, AudioChannel channel, Func<uint>? sampleDraw = null, Vector3? position = null, float? range = null )
	{
		var soundEvent = category?.Catalog.Find( eventId );
		if ( category == null || soundEvent == null || soundEvent.Sounds.Count == 0 )
			return null;
		if ( position is { } place )
			AudioListener.ValidatePosition( place, nameof( position ) );
		if ( range is { } far && (!float.IsFinite( far ) || far < 0) )
			throw new ArgumentOutOfRangeException( nameof( range ) );
		var sentence = new SoundSentence( this, category, soundEvent, channel, sampleDraw ) { Position = position, Range = range ?? soundEvent.Sounds[0].Rolloff };
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
	internal Vector3 Position => position;
	internal float Range => range;
	/// <summary>The event priority, for the voice pool.</summary>
	internal uint Priority { get; init; }
	/// <summary>The event flags (0x200: audible at any distance).</summary>
	internal ushort Flags { get; init; }
	/// <summary>The pitch step drawn when the sound was placed.</summary>
	internal int Pitch { get; init; }

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
			Voice.SetPitch( Pitch );
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

	/// <summary>The place a positioned sentence plays at (null: flat).</summary>
	public Vector3? Position { get; init; }
	/// <summary>The range of a positioned sentence.</summary>
	public float Range { get; init; }
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
		var first = soundEvent.Player == SoundEventPlayer.BranchingSentence ? 0 : Math.Max( 0, system.PickSound( soundEvent.Sounds ) );
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
		var sample = system.PickSample( samples, sound == soundIndex ? lastSample : -1, sound == soundIndex && lastSample >= 0, sampleDraw );
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
		// [BIN:TPW-EXE:0x6bbb80 SetParameter] the original updates the matching slot and, for a slot index >= 1 with placeholder flag 0x4000 clear, calls vf+0x34 (0x6bbbe0), which re-evaluates the playing sample's volume (0x6bc090) and pitch (0x6bc170)
		// [APPROX:AUDIO-036] OpenTPW evaluates density-controlled crowd gain when a segment starts, not on a parameter change inside a playing segment; which slot carries the crowd's parameter 7 is not read
		var gain = system.Volume( soundEvent.Sounds[sound], ParameterCode, Parameter ) / 100f;
		voice = Position is { } place ? system.Mixer.Play( audio, channel, gain, place, Range ) : system.Mixer.Play( audio, channel, gain );
		voice.SetPitch( system.Pitch( soundEvent.Sounds[sound] ) );
		voice.Completed += _ => OnCompleted();
		// [EXT:sentence-prefetch] a sentence's next segment is chosen and decoded ahead when the current one starts, so the draw and the parameter are taken one segment early; the original chooses when the current segment's data runs out (sound_shared GetDataChunk 0x10008CC4 -> 0x1001A1F0), but its chooser is the shared clock-seeded one (see Advance), so the draw's timing has no observable rule to match
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
		// a segment that ends (or is stopped) out of range starts nothing: Pump starts the next one when the listener is back
		waiting = OutOfRange || !TryPlayNext();
	}

	private bool OutOfRange => (soundEvent.Flags & SoundEventSystem.AnyDistanceFlag) == 0 && !SoundEventSystem.Audible( Position, Range );

	internal void Pump()
	{
		if ( IsStopped )
			return;
		// [BIN:TPW-EXE:0x6b62b0 sound manager tick] a voice out of range is not admitted to the pool (0x6bca10) and so not ticked, and its sample is stopped (0x6bca70, message 3)
		// [APPROX:AUDIO-038] the sentence stands still until the listener is back, then starts the next segment; nothing read shows how the original resumes a stopped sentence
		if ( OutOfRange )
		{
			if ( voice is { IsFinished: false } playing )
			{
				playing.Stop();
				voice = null;
				waiting = true;
			}
			return;
		}
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

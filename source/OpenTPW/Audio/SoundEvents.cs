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

	/// <summary>
	/// Loads <c>&lt;directory&gt;/&lt;name&gt;SFX.map</c> and <c>…BANK.map</c>; returns null (with a log line) when either is missing or damaged. <paramref name="exists"/> tests the
	/// maps' presence where <see cref="BaseFileSystem.FileExists"/> cannot see them (the members of an object archive); by default the file system's own test.
	/// </summary>
	public static SoundCategory? TryLoad( BaseFileSystem fileSystem, string directory, string name, Func<string, bool>? exists = null )
	{
		var sfx = $"{directory}/{name}SFX.map";
		var bank = $"{directory}/{name}BANK.map";
		exists ??= fileSystem.FileExists;
		try
		{
			if ( !exists( sfx ) || !exists( bank ) )
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
	private readonly Dictionary<(SoundCategory, int, uint), Lazy<Task<Mp2Audio?>>> decoding = new();
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

	/// <summary>
	/// What the sound system holds right now (<see cref="SoundSystemCounts"/>): the memory accounting of the browser harness and the tests that
	/// check a park visit leaves nothing behind read it.
	/// </summary>
	public SoundSystemCounts Counts()
	{
		var voiceSamples = new HashSet<short[]>( ReferenceEqualityComparer.Instance );
		foreach ( var voice in mixer.Voices )
			voiceSamples.Add( voice.SampleData );
		lock ( decoded )
			return new SoundSystemCounts( mixer.Voices.Count, voiceSamples.Sum( samples => (long)samples.Length * sizeof( short ) ), sentences.Count, chains.Count,
				placements.Count, oneShots.Count, sampleOwners.Keys.Count( voice => voice.Loops ), HeldSounds.Sounds.Count, decoded.Count,
				decoded.Values.Sum( audio => (long)(audio?.Samples.Length ?? 0) * sizeof( short ) ), decoding.Count );
	}

	/// <summary>Starts decoded segments that were waiting for an asynchronous decode; call once per frame.</summary>
	public void Pump()
	{
		HeldSounds.RefreshGroups();
		HeldSounds.Poll();
		HeldSounds.ProcessStops();
		foreach ( var placement in placements.ToArray() )
		{
			if ( placement.Pump() )
			{
				placements.Remove( placement );
				if ( placement.Voice is { } started )
					TrackOneShot( started, placement.Priority, placement.Flags, placement.Delay, placement.Parameters, placement.Lifetime );
			}
		}
		var now = PoolClock();
		oneShots.RemoveAll( entry =>
		{
			var expiresAt = sampleOwners.TryGetValue( entry.Voice, out var owner ) ? owner.ExpiresAt : entry.ExpiresAt;
			if ( expiresAt < 0 ? !entry.Voice.IsFinished : now <= expiresAt ) return false;
			// An expired released owner must not remain in the mixer forever or return later.
			if ( !entry.Voice.HasOutput ) entry.Voice.Stop();
			sampleOwners.Remove( entry.Voice );
			return true;
		} );
		finishedSamples.Clear();
		foreach ( var voice in sampleOwners.Keys )
			if ( voice.IsFinished && sampleOwners[voice].ExpiresAt < 0 )
				finishedSamples.Add( voice );
		foreach ( var voice in finishedSamples ) sampleOwners.Remove( voice );
		StopVoicesOutOfRange();
		foreach ( var sentence in sentences.ToArray() )
			sentence.Pump();
		sentences.RemoveAll( sentence => sentence.IsStopped );
		SelectPooledVoices();
		foreach ( var loop in pooledSamples ) loop.Restart();
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

	/// <summary>Stops out-of-range samples. Finite catalogue samples and loops retain their owner for a later admitted fresh output.</summary>
	// [BIN:TPW-EXE:0x6BCA70,0x6B8D60,0x6BB7C0,0x6C8E50,0x6BBE40] range flag 0x800 is set before stop; the successful output callback clears +0x24 and playing bit 1 without making the owner terminal.
	// [BIN:TPW-EXE:0x6BD800,0x6BC410,0x6B9AB0] the same owner's next admitted tick rebuilds the selected sample request with a zero existing output handle; this is a fresh source, not silent cursor advance.
	// [APPROX:AUDIO-038] sentence selected-descriptor/refill timing and expiry-exempt nonloop retention remain separate residuals; held-chain children are not recursively range-stopped.
	private void StopVoicesOutOfRange()
	{
		foreach ( var (voice, _, flags, _) in oneShots )
			if ( voice.Position != null && voice.Range > 0 && (flags & AnyDistanceFlag) == 0 && !Audible( voice.Position, voice.Range ) )
			{
				// Only catalogue samples with retained parameters can be rebuilt; unrelated voices stop as before.
				if ( sampleOwners.TryGetValue( voice, out var loop ) )
				{
					if ( voice.ReleaseSampleOutput() )
						loop.Lifetime?.LoseOutputReply();
				}
				else voice.Stop();
			}
	}

	/// <summary>The voices the sound manager updates per tick (tp.exe <c>0x6b8380</c> passes 12 to the pool set-up <c>0x6b6050</c>).</summary>
	public const int VoicePool = 12;

	/// <summary>
	/// A seam for the tests: raised for every event the game asks this system to play, start or chain (the category name, the event id, the place), before the catalogue is
	/// consulted, so a headless run can tell which sound events fire without an audio device or the original data. Presentation only; it never draws.
	/// </summary>
	internal static Action<string?, uint, Vector3?>? Triggered { get; set; }

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

	private readonly List<(AudioVoice Voice, uint Priority, ushort Flags, long ExpiresAt)> oneShots = new();
	private readonly Dictionary<AudioVoice, RetainedSample> sampleOwners = new();
	private readonly List<AudioVoice> finishedSamples = new();

	private void TrackOneShot( AudioVoice voice, uint priority, ushort flags, double delay, PlacedSoundParameters? parameters, HeldSoundLifetime? lifetime = null )
	{
		var expiresAt = ExpiryFrame( voice, flags, delay );
		oneShots.Add( (voice, priority, flags, expiresAt) );
		if ( parameters != null && (voice.Loops || (flags & 0xC) == 0) ) sampleOwners.Add( voice, new RetainedSample( this, voice, parameters, lifetime, expiresAt, delay ) );
	}

	/// <summary>The selected source and parameter state survive output release; the public voice remains the stable Stop/move handle.</summary>
	private sealed class RetainedSample
	{
		private readonly SoundEventSystem system;
		private readonly PlacedSoundParameters parameters;
		private readonly double lifetimeSeconds;
		private readonly long bornAt;
		private readonly long duration;
		internal AudioVoice Voice { get; }
		internal HeldSoundLifetime? Lifetime { get; }
		internal long ExpiresAt { get; private set; }
		internal bool CanAdmit => Lifetime is not { CanAdmit: false } and not { Suspended: true };

		internal RetainedSample( SoundEventSystem system, AudioVoice voice, PlacedSoundParameters parameters,
			HeldSoundLifetime? lifetime, long expiresAt, double delay )
		{
			this.system = system; this.parameters = parameters; Voice = voice; Lifetime = lifetime;
			ExpiresAt = expiresAt;
			var length = voice.SampleRate > 0 ? voice.Frames / (double)voice.SampleRate : 0;
			duration = system.mixer.SecondsToFrames( length );
			lifetimeSeconds = length + ExpiryTailSeconds;
			// Retain the exact initial birth and round the combined duration as ExpiryFrame does.
			bornAt = expiresAt < 0 ? system.PoolClock() : expiresAt - system.mixer.SecondsToFrames( lifetimeSeconds + delay );
		}

		internal void Restart()
		{
			if ( Voice.IsFinished || Voice.HasOutput || !CanAdmit ) return;
			var now = system.PoolClock();
			// Manager admission used the previous delay. Setup redraws it, but never renews birth/end.
			// [BIN:TPW-EXE:0x6BC4D8-0x6BC504,0x6BCB60] expiry is strict and checked again after that draw.
			if ( ExpiresAt >= 0 && now > ExpiresAt ) { Voice.Stop(); return; }
			var (volume, pitch) = parameters.Restart( Lifetime, out var delay );
			if ( ExpiresAt >= 0 )
			{
				ExpiresAt = bornAt + system.mixer.SecondsToFrames( lifetimeSeconds + delay );
				var second = system.mixer.SecondsToFrames( 1 );
				var elapsed = Math.Max( 0, now - bornAt );
				var remainder = duration > 0 ? elapsed % duration : 0;
				// [BIN:TPW-EXE:0x6BC534-0x6BC55D] late-start eligibility only, never a seek offset.
				if ( now > ExpiresAt || (duration > second && remainder > second && duration - remainder < second) )
				{
					Voice.Stop();
					return;
				}
			}
			Voice.Gain = volume / 100f;
			Voice.SetPitch( pitch );
			Voice.RestartSampleOutput();
			Lifetime?.Admit( Voice, volume );
		}
	}

	/// <summary>The clock the voice expiry reads (mixer output frames); tests replace it.</summary>
	internal Func<long> PoolClock { get; set; } = null!;
	private readonly List<(object Voice, uint Rank, long Serial)> poolCandidates = new();
	private readonly HashSet<SoundChain> pooledChains = new();
	private readonly List<RetainedSample> pooledSamples = new();

	/// <summary>
	/// The pool order of two voices: the higher rank first, and among equal ranks the newer voice first.
	/// </summary>
	// [BIN:TPW-EXE:0x6b6a80 voice list insert] a new voice is linked at the head of the manager's voice list (head +0x24, next +0x34, previous +0x38), so the tick 0x6b62b0 meets the newest voice first
	// [BIN:TPW-EXE:0x6b6bc0 pool admission] each voice is slotted into the descending array before the first slot whose rank (the dword at +0x20) is strictly lower, the last slot's voice is pushed out; an equal rank therefore stays behind the voice already slotted, which is the newer one
	internal static int ComparePoolOrder( uint rankA, long serialA, uint rankB, long serialB )
	{
		var byRank = rankB.CompareTo( rankA );
		return byRank != 0 ? byRank : serialB.CompareTo( serialA );
	}

	private static readonly Comparison<(object Voice, uint Rank, long Serial)> ByRankThenNewest = ( a, b ) => ComparePoolOrder( a.Rank, a.Serial, b.Rank, b.Serial );

	/// <summary>
	/// Selects held-chain ticks and sample-output restarts among the highest-ranked audible owners. Pool eviction does not stop existing outputs.
	/// A chain outside the pool starts no new child; the children it already started play out.
	/// </summary>
	// [BIN:TPW-EXE:0x6b62b0 sound manager tick] only the voices in the pool of 12 are ticked; the pool is rebuilt every tick (0x6b6ba0), a voice is admitted (0x6b6bc0) only while audible (0x6bca10: the listener within the voice's range, the map sound's float +34, over the ground) and ranked by the dword priority:distance of 0x6bb9f0; a held chain's next child, its first included, is made in its tick (0x6bdae0 → 0x6bdb50 → 0x6bdbb0)
	// [BIN:TPW-EXE:0x6bca44 any-distance admission] a voice with the any-distance flag is admitted whatever its distance
	// [BIN:TPW-EXE:0x6b6bc0 equal ranks] among equal ranks the newer logical owner keeps its slot (ComparePoolOrder); recreating output does not renew its serial.
	// [BIN:TPW-EXE:0x6BDB50,0x6BDBB0,0x6C3AE0] held-chain children are locally linked and ticked directly, with a distinct output setup; they are not extra manager pool entries.
	private bool AnySampleWithoutOutput()
	{
		foreach ( var loop in sampleOwners.Values )
			if ( !loop.Voice.IsFinished && !loop.Voice.HasOutput )
				return true;
		return false;
	}

	private void SelectPooledVoices()
	{
		pooledChains.Clear();
		pooledSamples.Clear();
		if ( chains.Count == 0 && !AnySampleWithoutOutput() )
			return;
		poolCandidates.Clear();
		foreach ( var sentence in sentences )
			if ( !sentence.IsStopped && Admitted( sentence.Position, sentence.Range, sentence.Event.Flags ) )
				poolCandidates.Add( (sentence, PoolRank( sentence.Event.Priority, sentence.Position ), sentence.Serial) );
		foreach ( var placement in placements )
			if ( Admitted( placement.Position, placement.Range, placement.Flags ) )
				poolCandidates.Add( (placement, PoolRank( placement.Priority, placement.Position ), placement.Serial) );
		foreach ( var (voice, priority, flags, expiresAt) in oneShots )
			if ( (expiresAt >= 0 || !voice.IsFinished) && Admitted( voice.Position, voice.Range, flags )
				&& sampleOwners.GetValueOrDefault( voice ) is not { CanAdmit: false } )
				poolCandidates.Add( (voice, PoolRank( priority, voice.Position ), voice.Serial) );
		foreach ( var chain in chains )
			if ( !chain.IsStopped && Admitted( chain.Position, chain.Range, chain.Event.Flags ) )
				poolCandidates.Add( (chain, PoolRank( chain.Event.Priority, chain.Position ), chain.Serial) );
		// [BIN:TPW-EXE:0x6b6ba0 pool reset] the pool is cleared every tick with its admission threshold (+0x48) at 0, and 0x6b6bc0 rejects a rank that is not above the threshold, so a voice of rank 0 (priority 0 and past the distance scale) never enters
		poolCandidates.RemoveAll( candidate => candidate.Rank == 0 );
		poolCandidates.Sort( ByRankThenNewest );
		for ( var index = 0; index < Math.Min( VoicePool, poolCandidates.Count ); index++ )
			if ( poolCandidates[index].Voice is SoundChain chain )
				pooledChains.Add( chain );
			else if ( poolCandidates[index].Voice is AudioVoice voice
				&& sampleOwners.GetValueOrDefault( voice ) is { } loop )
				pooledSamples.Add( loop );
	}

	/// <summary>
	/// The delay of a sample start: nought when both bounds are nought, otherwise min + draw % (max - min) milliseconds, in seconds. A chain
	/// waits it between children; for any other voice it is the time the voice is kept after its sample's end (<see cref="ExpiryFrame"/>).
	/// </summary>
	// [BIN:TPW-EXE:0x6bc2d0 delay draw] 0 when both u16 bounds are 0, else min + ror13(state) % (max - min), drawn at every sample start (0x6bc4f4) after the volume and the pitch, and stored at the placeholder's +0x3c (0x6bc4fa)
	internal double DelaySeconds( SoundEntry sound )
	{
		var low = Math.Min( sound.MinimumDelay, sound.MaximumDelay );
		var high = Math.Max( sound.MinimumDelay, sound.MaximumDelay );
		if ( high == 0 )
			return 0;
		return (low + (double)DrawBelow( (uint)Math.Max( 1, high - low ) )) / 1000;
	}

	/// <summary>The extra time a finished voice keeps its place in the pool after its sample's nominal end (tp.exe 0x6bc01b adds 0xfa to the end time).</summary>
	internal const double ExpiryTailSeconds = 0.25;

	/// <summary>
	/// The mixer frame after which a voice leaves the pool: its start plus the sample's nominal length, 250 ms and the drawn delay; -1 for a voice
	/// that lives until it finishes (an event flagged 0x4 or 0x8, which the original exempts from the expiry test).
	/// </summary>
	// [BIN:TPW-EXE:0x6bc01b voice end time] a started voice's end (+0x1c) is the clock plus the sample's length (+8 of the sample record) plus 0xfa milliseconds
	// [BIN:TPW-EXE:0x6bcb60 expiry test] a voice without voice flag 2 is expired once the clock passes its end plus its delay (+0x3c); the manager tick 0x6b62b0 then releases it (0x6b6440) without ranking it, whether or not its sample has finished
	// [BIN:TPW-EXE:0x6bbe90 voice flags from the event] the event flag 0x4 makes voice flag 2 and 0x8 makes 6 (loops, with 2), which exempt a voice from the expiry
	internal long ExpiryFrame( AudioVoice voice, ushort eventFlags, double delaySeconds )
	{
		if ( (eventFlags & 0xC) != 0 )
			return -1;
		var length = voice.SampleRate > 0 ? voice.Frames / (double)voice.SampleRate : 0;
		return PoolClock() + mixer.SecondsToFrames( length + ExpiryTailSeconds + delaySeconds );
	}

	public SoundEventSystem( AudioMixer mixer, uint seed = 1, Func<double>? monotonicMilliseconds = null )
	{
		this.mixer = mixer;
		this.seed = seed;
		PoolClock = () => mixer.PlayedFrames;
		HeldSounds = new HeldSoundService( monotonicMilliseconds ?? (() => System.Diagnostics.Stopwatch.GetTimestamp() * 1000.0 / System.Diagnostics.Stopwatch.Frequency) );
		mixer.GroupsChanged += HeldSounds.RefreshGroups;
		mixer.Stopping += HeldSounds.StopAll;
	}

	public AudioMixer Mixer => mixer;
	internal HeldSoundService HeldSounds { get; }

	/// <summary>Current chooser seed (saved with the park; see <see cref="WorldRandomState"/>).</summary>
	public uint Seed => seed;

	/// <summary>Restarts the chooser from <paramref name="value"/> (a park's <see cref="WorldSeed.SoundStream"/> or a saved seed).</summary>
	public void Reseed( uint value ) => seed = value;

	// [BIN:TPW-EXE:0x6bc6ce] one shared chooser for every catalogue draw: s = s * 0x19660D + 0x3C6EF35F, stored back into the shared state (0xfb1f20) on every draw, and the draw is ror(s, 13), then >> 16 (weights: sound 0x6bc810, sample 0x6bc680) or % (max - min) (ranges: volume 0x6bc090, pitch 0x6bc170, delay 0x6bc343); the same state serves the sentence link choices (0x6be450, 0x6c3e00)
	// [BIN:TPW-EXE:0x72e09c] the state is seeded once at static initialisation (0x6bb930 -> 0x5f5f10, QueryPerformanceCounter) and is never saved
	// [EXT:sound-chooser-seed] OpenTPW seeds the chooser from the clock when the sound service starts (as the original, QueryPerformanceCounter, tp.exe 0x72e09c) but reseeds it from the park's world seed at park entry (GameAudio.EnterPark) and saves it with the park, so headless runs and tests start from a known state; with sound on, the draws follow wall-clock events (mixer-timed sentence segments, UI clicks), so this is not a replay guarantee; a deliberate determinism choice with no original behaviour to match (the original's chooser is clock-seeded and unsaved)
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

	/// <summary>The volume of a playback without a parameter binding: min + r % (max - min) percent, an empty range drawing nothing.</summary>
	// [BIN:STP-PPC:sound_shared 0x1000F32C GetRandomVolume] volume = min + random % (max - min), in percent
	// [BIN:TPW-EXE:0x6bc090] volume = min + ror13(chooser) % (max - min); an empty range draws nothing (0x6bc11b)
	internal int Volume( SoundEntry sound )
	{
		var low = Math.Min( sound.MinimumVolume, sound.MaximumVolume );
		var range = Math.Abs( sound.MaximumVolume - sound.MinimumVolume );
		return range == 0 ? low : (int)(low + DrawBelow( (uint)range ));
	}

	/// <summary>
	/// The volume a placeholder playing <paramref name="sound"/> evaluates after one parameter call (<paramref name="parameterCode"/>, <paramref name="parameter"/>),
	/// or after none when the code is null: the table the sound's setup creates (selectors +0x16/+0x1A), the call stored in it, then <see cref="Volume(SoundEntry, SoundParameterTable?)"/>.
	/// </summary>
	internal int Volume( SoundEntry sound, byte? parameterCode, int parameter )
	{
		SoundParameterTable? table = null;
		SoundParameterTable.SelectSound( ref table, sound );
		if ( parameterCode is { } code )
		{
			table ??= new SoundParameterTable();
			table.Set( code, unchecked((byte)parameter) );
		}
		return Volume( sound, table );
	}

	/// <summary>
	/// The pitch step of a playback: the bounds are put in order (signed), then min + r % (max - min), an empty range drawing nothing; the draw follows the volume's.
	/// </summary>
	// [BIN:TPW-EXE:0x6bc170 pitch draw] swaps the signed bytes +0xE/+0xF of the sound record when max < min, range = max - min, and without a parameter-bound pitch slot (the vf+0x40 query for parameter kind 2) draws ror13(chooser) % range + min; 0x6bc410 calls it right after the volume draw (0x6bc090) and hands it to 0x6c5170, unless the placeholder flag 0x10000 fixes the pitch
	// [BIN:TPW-EXE:0x6bc410 sample start] the order of draws for one playback is sound, sample, volume, pitch, then the delay (0x6bc343)
	// [DATA:global/sound,levels/*/Sound cat_*SFX.map] 1,343 shipped sounds have no pitch range; the chain, scream and crowd sounds draw 0..6 (steps 0-5 are played), the others signed ranges from -24..36 to -2..0
	// [BIN:TPW-EXE:0x6bc484 fixed-pitch flag] a placeholder with flag 0x10000 (+0x30) plays the short at +0x4A instead of this draw, and a flag 0x8000 plays the short at +0x48 instead of the volume draw (0x6bc451); nothing in the library sets 0x10000 (the only 'or 0x10000' is the mixer handle's own flag word, 0x6c51d3) and the events' flags are 16-bit (the shipped values are 0x0-0x16, 0x200-0x208, 0x404-0x606), so the draw is the only pitch path here; 0x8000 is set by the placeholder's volume command (0x6bc9e7)
	internal int Pitch( SoundEntry sound )
	{
		var low = Math.Min( sound.MinimumPitch, sound.MaximumPitch );
		var range = Math.Abs( sound.MaximumPitch - sound.MinimumPitch );
		return range == 0 ? low : low + (int)DrawBelow( (uint)range );
	}

	/// <summary>The volume of a playback whose placeholder has a parameter table: a sound bound to a parameter for volume (kind 1) takes it, any other draws.</summary>
	// [BIN:TPW-EXE:0x006BC090 volume] with a table (+0x4C) the vf+0x40 query for kind 1 (0x6BC040) yields the bound value v and the volume is min + (v × (max − min)) / 100 in unsigned arithmetic; without a binding it draws
	// [BIN:TPW-EXE:0x006BC040 parameter query] only the masks are tested: a sound whose mask +0x18 (or +0x1C) has the kind's bit takes slot 1's (or 2's) value byte whenever the placeholder has a table, a value never set being 0, so such a sound plays at its minimum until a matching call arrives
	// [BIN:STP-PPC:sound_shared 0x1000F2E0 GetParameterValue] the first slot whose +24/+28 mask contains bit 1 supplies volume; selectors +22/+26 are truncated to bytes when loaded (0x100100D8–0x100100EC)
	// [BIN:STP-PPC:sound_shared 0x1000F3C8–0x1000F3F4 GetRandomVolume] the Mac interpolates the same way, low + floor((high - low) × byte value / 100), the high endpoint included at 100
	internal int Volume( SoundEntry sound, SoundParameterTable? table )
	{
		if ( table != null && table.TryValue( sound, SoundParameterTable.KindVolume, out var value ) )
		{
			var low = Math.Min( sound.MinimumVolume, sound.MaximumVolume );
			var range = (uint)Math.Abs( sound.MaximumVolume - sound.MinimumVolume );
			return low + (int)((uint)value * range / 100);
		}
		return Volume( sound );
	}

	/// <summary>The pitch step of a playback whose placeholder has a parameter table: a sound bound to a parameter for pitch (kind 2) takes it, any other draws.</summary>
	// [BIN:TPW-EXE:0x006BC170 pitch] with a table (+0x4C) the vf+0x40 query for kind 2 (0x6BC040) yields the bound value v and the step is (v × (max − min)) / 100 + min (unsigned multiply and divide, no clamp), without a draw
	internal int Pitch( SoundEntry sound, SoundParameterTable? table )
	{
		if ( table != null && table.TryValue( sound, SoundParameterTable.KindPitch, out var value ) )
		{
			var low = Math.Min( sound.MinimumPitch, sound.MaximumPitch );
			var range = (uint)Math.Abs( sound.MaximumPitch - sound.MinimumPitch );
			return low + (int)((uint)value * range / 100);
		}
		return Pitch( sound );
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
		using var decodeMark = FrameMarks.Measure( "audio.decodeSync" );
		Mp2Audio? audio = null;
		var bank = category.GetBank( sample.BankNumber );
		var rejectedPayloadBytes = 0;
		int? playbackBytes = null;
		var mpeg = bank?.GetMpeg( sample.SampleNumber, Mp2Decoder.MaximumInputBytes, out rejectedPayloadBytes, out playbackBytes );
		try
		{
			if ( rejectedPayloadBytes > 0 )
				throw new InvalidDataException( $"Selected sound entry contains {rejectedPayloadBytes} bytes, exceeding the caller's {Mp2Decoder.MaximumInputBytes}-byte input bound." );
			// The game plays only the entry's own length of the decoded PCM (SdtEntryHeader.PlaybackBytes).
			if ( mpeg != null ) audio = Mp2Decoder.Decode( mpeg, playbackBytes );
		}
		catch ( Exception exception ) when ( exception is InvalidDataException or NotSupportedException )
		{
			Log.Trace( $"Sound {category.Name} bank {sample.BankNumber} sample {sample.SampleNumber}: {exception.Message}" );
		}
		return RememberDecoded( category, sample, audio, retainLong );
	}

	/// <summary>Shares one pending decode per sample; each caller still owns its playback and random draws.</summary>
	internal Task<Mp2Audio?> DecodeSentenceAsync( SoundCategory category, SoundSample sample, bool? cooperative = null, Func<Task>? yieldBatch = null )
	{
		var key = (category, (int)sample.BankNumber, sample.SampleNumber);
		Lazy<Task<Mp2Audio?>> flight;
		lock ( decoded )
		{
			if ( TryDecoded( category, sample, out var cached ) )
				return Task.FromResult( cached );
			if ( !decoding.TryGetValue( key, out flight! ) )
			{
				flight = new Lazy<Task<Mp2Audio?>>( DecodeOnce );
				decoding.Add( key, flight );
			}
		}
		// Do not run the decoder or the scheduler callback under the cache lock. Lazy publishes the same task
		// even when another native caller reaches Value first or a synthetic scheduler completes synchronously.
		return flight.Value;

		async Task<Mp2Audio?> DecodeOnce()
		{
			try
			{
				return (cooperative ?? OperatingSystem.IsBrowser())
					? await DecodeCooperatively( category, sample, yieldBatch )
					: await Task.Run( () => Decode( category, sample, retainLong: true ) );
			}
			finally
			{
				// Faults and cancellation may be retried. Forget still prevents late results from entering the cache.
				lock ( decoded ) decoding.Remove( key );
			}
		}
	}

	private async Task<Mp2Audio?> DecodeCooperatively( SoundCategory category, SoundSample sample, Func<Task>? yieldBatch )
	{
		// Return to the browser before bank I/O and the first MPEG batch, not only between later batches.
		await (yieldBatch?.Invoke() ?? Task.Delay( 1 ));
		Mp2Audio? audio = null;
		var bank = category.GetBank( sample.BankNumber );
		var rejectedPayloadBytes = 0;
		int? playbackBytes = null;
		var mpeg = bank?.GetMpeg( sample.SampleNumber, Mp2Decoder.MaximumInputBytes, out rejectedPayloadBytes, out playbackBytes );
		try
		{
			if ( rejectedPayloadBytes > 0 )
				throw new InvalidDataException( $"Selected sound entry contains {rejectedPayloadBytes} bytes, exceeding the caller's {Mp2Decoder.MaximumInputBytes}-byte input bound." );
			if ( mpeg != null ) audio = await Mp2Decoder.DecodeCooperativelyAsync( mpeg, yieldBatch: yieldBatch, playbackBytes: playbackBytes );
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
		Triggered?.Invoke( category?.Name, eventId, null );
		var soundEvent = category?.Catalog.Find( eventId );
		if ( category == null || soundEvent == null || soundEvent.Sounds.Count == 0 )
			return null;
		var sound = soundEvent.Sounds[Math.Max( 0, PickSound( soundEvent.Sounds ) )];
		var index = PickSample( sound.Samples, -1, avoidRepeat: false );
		if ( index < 0 || Decode( category, sound.Samples[index] ) is not { } audio )
			return null;
		// [APPROX:AUDIO-003] the pitch and the delay are applied (Pitch, AudioVoice.SetPitch, DelaySeconds, ExpiryFrame); reverb (SndReverb.map, loaded at 0x6c5d20) is OpenTPW's stand-in (EXT:audio-reverb), chain delays are milliseconds (video, 2026-10-15)
		// [BIN:TPW-EXE:0x6bc4e7 sample setup] the fixed (flat) branch and the positioned branch both reach the loop test 0x6bc621: a placeholder with flag 0x8 (+0x30 bit 4) loops its sample (0x6c5110, 9999 times), so a flat one-shot loops exactly as a positioned one does
		// [BIN:TPW-EXE:0x006BBF60 placeholder setup] every placeholder class (vtables 0x70a338, 0x70a3e8, 0x70a440, 0x70a498, 0x70a7e0, 0x70a878, 0x70a8d0) sets up at +0x10 with 0x6BBF60 and queries at +0x40 with 0x6BC040: a one-shot whose event has a code or whose sound has a selector gets a table, and a bound volume or pitch reads its slot (0 with no parameter call)
		var table = SoundParameterTable.ForPlayback( soundEvent, sound );
		var volume = Volume( sound, table ) / 100f;
		var voice = mixer.Play( audio, channel, volume );
		voice.SetPitch( Pitch( sound, table ) );
		voice.Loops = (soundEvent.Flags & 0x8) != 0;
		// [BIN:TPW-EXE:0x6bc4f4 sample start] the delay is drawn after the volume and the pitch
		TrackOneShot( voice, soundEvent.Priority, soundEvent.Flags, DelaySeconds( sound ), new PlacedSoundParameters( this, sound, table ) );
		return voice;
	}

	/// <summary>
	/// Plays a one-shot event at a place: the voice carries <paramref name="position"/> and — unless a
	/// range is given — the chosen sound's own range (the float at the sound record's +34, the far end the
	/// original hands its mixer), and the mixer turns it down over its distance from the listener.
	/// </summary>
	public AudioVoice? PlayAt( SoundCategory? category, uint eventId, AudioChannel channel, Vector3 position, float? range = null )
	{
		Triggered?.Invoke( category?.Name, eventId, position );
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
		// [BIN:TPW-EXE:0x006BBF60 placeholder setup] as in Play: the one-shot's table, its bound volume and pitch read their slots
		var table = SoundParameterTable.ForPlayback( soundEvent, sound );
		var volume = Volume( sound, table ) / 100f;
		var voice = mixer.Play( audio, channel, volume, position, range ?? sound.Rolloff );
		voice.SetPitch( Pitch( sound, table ) );
		voice.Loops = (soundEvent.Flags & 0x8) != 0;
		TrackOneShot( voice, soundEvent.Priority, soundEvent.Flags, DelaySeconds( sound ), new PlacedSoundParameters( this, sound, table ) );
		return voice;
	}

	/// <summary>
	/// <see cref="PlayAt"/> without waiting for the sample: the sound and sample are drawn now (the same draws, in the same order), a
	/// sample not decoded yet is decoded like a sentence's (cooperatively in the browser, on a worker on the desktop) and the voice
	/// starts at the <see cref="Pump"/> that finds it ready. A park's placed ambients start this way, so their decoding (about a tenth
	/// of a second for the jungle in the browser) is not part of the park's first frame.
	/// </summary>
	public PlacedSound? PlayAtWhenDecoded( SoundCategory? category, uint eventId, AudioChannel channel, Vector3 position, float? range = null )
		=> PlaceWhenDecoded( category, eventId, channel, position, range );

	/// <summary>
	/// <see cref="PlayAtWhenDecoded"/> for a sound that may have no place (a flat voice, the original's unpositioned submit); the held ADDOBJ sounds use it
	/// (<see cref="PlayHeld"/>), because the script moves, parameterises and ends them later.
	/// </summary>
	internal PlacedSound? PlaceWhenDecoded( SoundCategory? category, uint eventId, AudioChannel channel, Vector3? position, float? range = null, HeldSoundLifetime? lifetime = null )
	{
		Triggered?.Invoke( category?.Name, eventId, position );
		var soundEvent = category?.Catalog.Find( eventId );
		if ( category == null || soundEvent == null || soundEvent.Sounds.Count == 0 )
			return null;
		var sound = soundEvent.Sounds[Math.Max( 0, PickSound( soundEvent.Sounds ) )];
		var index = PickSample( sound.Samples, -1, avoidRepeat: false );
		if ( index < 0 )
			return null;
		// [BIN:TPW-EXE:0x6bc621 loop test] as in PlayAt: the loop flag 0x8 applies
		// [BIN:TPW-EXE:0x006BBF60 placeholder setup] as in Play: the one-shot's table, its bound volume and pitch read their slots
		var table = SoundParameterTable.ForPlayback( soundEvent, sound );
		var volume = Volume( sound, table ) / 100f;
		var pitch = Pitch( sound, table );
		var placed = new PlacedSound( mixer, channel, volume, position, range ?? sound.Rolloff, (soundEvent.Flags & 0x8) != 0,
			DecodeSentenceAsync( category, sound.Samples[index] ) ) { Priority = soundEvent.Priority, Flags = soundEvent.Flags, PitchValue = pitch, Delay = DelaySeconds( sound ), Parameters = new PlacedSoundParameters( this, sound, table ), Lifetime = lifetime };
		if ( !placed.Pump() )
			placements.Add( placed );
		else if ( placed.Voice is { } started )
			TrackOneShot( started, soundEvent.Priority, soundEvent.Flags, placed.Delay, placed.Parameters, lifetime );
		return placed;
	}

	/// <summary>
	/// Starts an event the way the original's submit (0x51BFC0) does for a record the script keeps: a repeating (sentence) event as a sentence, a one-shot as a sound that
	/// starts once its sample is decoded; both at <paramref name="position"/>, or flat without one. The result takes the later position, parameter and stop calls.
	/// </summary>
	internal HeldSound? PlayHeld( SoundCategory? category, uint eventId, AudioChannel channel, Vector3? position )
	{
		var soundEvent = category?.Catalog.Find( eventId );
		if ( category == null || soundEvent == null || soundEvent.Sounds.Count == 0 )
		{
			Triggered?.Invoke( category?.Name, eventId, position );
			return null;
		}
		// [BIN:TPW-EXE:0x6B87D0] queued flag 1 returns the prior handle (zero for fresh ADDOBJ).
		if ( (soundEvent.Flags & 1) != 0 )
		{
			PlaceWhenDecoded( category, eventId, channel, position );
			return null;
		}
		var lifetime = new HeldSoundLifetime( mixer, soundEvent.Player, soundEvent.GroupMask );
		if ( soundEvent.Player != SoundEventPlayer.OneShot )
		{
			var sentence = StartSentence( category, eventId, channel, null, position, null );
			if ( sentence == null ) return null;
			sentence.Lifetime = lifetime;
			return HeldSounds.Add( new HeldSound( sentence, HeldSounds, channel ) );
		}
		return PlaceWhenDecoded( category, eventId, channel, position, lifetime: lifetime ) is { } placed
			? HeldSounds.Add( new HeldSound( placed, HeldSounds, channel ) ) : null;
	}

	/// <summary>Starts a sentence event; an optional chooser replaces only its sample draws. Null when the event is missing.</summary>
	/// <param name="sampleDraw">Optional 16-bit sample chooser; does not alter sound selection, branch links or volume parameters.</param>
	/// <param name="position">Optional sound-space place: a positioned sentence plays every segment there (a scape.omp placed sentence), turned down over its distance from the listener.</param>
	/// <param name="range">The positioned sentence's range; the first sound's own range when null.</param>
	public SoundSentence? StartSentence( SoundCategory? category, uint eventId, AudioChannel channel, Func<uint>? sampleDraw = null, Vector3? position = null, float? range = null )
	{
		Triggered?.Invoke( category?.Name, eventId, position );
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
		Triggered?.Invoke( category?.Name, eventId, position );
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
/// <summary>What a placed one-shot needs to take the original's parameter call later: the sound's own record and the placeholder's parameter table.</summary>
internal sealed class PlacedSoundParameters
{
	private readonly SoundEventSystem system;
	private readonly SoundEntry sound;
	private SoundParameterTable? table;

	public PlacedSoundParameters( SoundEventSystem system, SoundEntry sound, SoundParameterTable? table )
	{
		this.system = system;
		this.sound = sound;
		this.table = table;
	}

	/// <summary>Re-evaluates the retained sample's volume, pitch and delay, without selecting another sound or sample.</summary>
	// [BIN:TPW-EXE:0x6BC410] a returning sample repeats setup; volume and pitch precede the delay draw at 0x6BC4F4. Loops are expiry-exempt.
	internal (int Volume, int Pitch) Restart( HeldSoundLifetime? lifetime ) => Restart( lifetime, out _ );

	internal (int Volume, int Pitch) Restart( HeldSoundLifetime? lifetime, out double delay )
	{
		var volume = lifetime is { PreserveVolume: true } ? lifetime.OwnVolume : system.Volume( sound, table );
		var pitch = system.Pitch( sound, table );
		delay = system.DelaySeconds( sound );
		return (volume, pitch);
	}

	/// <summary>Stores the value in every table slot coded <paramref name="code"/> and returns the re-evaluated (volume, pitch) when a slot of the sound's own bindings (1-3) matched, else null.</summary>
	// [BIN:TPW-EXE:0x6bbb80 SetParameter] the table is created when missing (0x6b7150); the code is compared with all four slot codes and the byte value stored in each match; a match in slot 1-3, with placeholder flag 0x4000 clear, re-evaluates the volume and pitch (0x6bbbe0), as SoundSentence.SetParameter does for a sentence
	public (float? Volume, int Pitch)? Set( int code, int value, bool fading = false )
	{
		table ??= new SoundParameterTable();
		if ( !table.Set( unchecked((byte)code), unchecked((byte)value) ) )
			return null;
		return (fading ? null : system.Volume( sound, table ) / 100f, system.Pitch( sound, table ));
	}
}

/// <summary>A positioned (or flat) sound that starts once its sample is decoded (<see cref="SoundEventSystem.PlayAtWhenDecoded"/>); it can be moved, given parameters and stopped before.</summary>
public sealed class PlacedSound
{
	/// <summary>The creation order among all voices (see <see cref="SoundEventSystem.ComparePoolOrder"/>).</summary>
	internal long Serial { get; } = VoiceOrder.Next();
	private readonly AudioMixer mixer;
	private readonly AudioChannel channel;
	private float volume;
	private Vector3? position;
	private readonly float range;
	private readonly bool loops;
	private readonly Task<Mp2Audio?> decoding;
	private bool stopped;
	internal HeldSoundLifetime? Lifetime { get; init; }
	internal Vector3? Position => position;

	/// <summary>Moves the sound, and its voice once it plays (a rectangle sound follows the listener each tick).</summary>
	internal void MoveTo( Vector3 place )
	{
		position = place;
		if ( Voice is { } playing )
			playing.Position = place;
	}
	internal float Range => range;
	/// <summary>The event priority, for the voice pool.</summary>
	internal uint Priority { get; init; }
	/// <summary>The event flags (0x200: audible at any distance).</summary>
	internal ushort Flags { get; init; }
	/// <summary>The pitch step drawn when the sound was placed (a parameter call changes it).</summary>
	internal int Pitch { get; private set; }
	/// <summary>The delay drawn when the sound was placed, in seconds (<see cref="SoundEventSystem.DelaySeconds"/>).</summary>
	internal double Delay { get; init; }
	/// <summary>The parameter binding of the placed sound's event, null for a sound placed by a caller that gives it no parameters.</summary>
	internal PlacedSoundParameters? Parameters { get; init; }

	internal PlacedSound( AudioMixer mixer, AudioChannel channel, float volume, Vector3? position, float range, bool loops, Task<Mp2Audio?> decoding )
	{
		this.mixer = mixer;
		this.channel = channel;
		this.volume = volume;
		this.position = position;
		this.range = range;
		this.loops = loops;
		this.decoding = decoding;
	}

	internal int PitchValue { init => Pitch = value; }

	/// <summary>The voice once the sample is decoded and playing; null before, or when the sample could not be decoded.</summary>
	public AudioVoice? Voice { get; private set; }

	/// <summary>Whether the sound has ended: stopped, or its voice has played out (a sample not decoded yet has not).</summary>
	public bool IsFinished => stopped || Lifetime is { Terminal: true } || Voice is { IsFinished: true };

	/// <summary>The original's parameter call on the placed sound: the stored volume and pitch change, and a playing voice takes them at once.</summary>
	internal void SetParameter( int code, int value )
	{
		if ( stopped || Lifetime is { CanAdmit: false } || Parameters?.Set( code, value, Lifetime?.Fading ?? false ) is not { } changed )
			return;
		if ( changed.Volume is { } updated ) volume = updated;
		Pitch = changed.Pitch;
		if ( Voice is { IsFinished: false } playing )
		{
			if ( Lifetime is { IsContainer: false } logical ) logical.SetVolume( (int)MathF.Round( volume * 100 ) );
			else if ( changed.Volume != null ) playing.Gain = volume;
			playing.SetPitch( Pitch );
		}
	}

	/// <summary>Starts the voice when the sample is ready; true once nothing is left to wait for.</summary>
	internal bool Pump()
	{
		if ( stopped || Lifetime is { CanAdmit: false } )
			return true;
		if ( Voice != null ) return true;
		if ( !decoding.IsCompleted )
			return false;
		if ( decoding.IsFaulted )
			Log.Warning( $"A placed sound was not started: its sample could not be decoded ({decoding.Exception?.GetBaseException().Message})." );
		else if ( decoding.IsCompletedSuccessfully && decoding.Result is { } audio )
		{
			Voice = position is { } place ? mixer.Play( audio, channel, volume, place, range ) : mixer.Play( audio, channel, volume );
			Voice.Serial = Serial;
			Voice.SetPitch( Pitch );
			Voice.Loops = loops;
			Lifetime?.Admit( Voice, (int)MathF.Round( volume * 100 ) );
		}
		if ( Voice == null ) Stop();
		return true;
	}

	public void Stop()
	{
		Lifetime?.End();
		stopped = true;
		Voice?.Stop();
	}
}

public sealed class SoundSentence
{
	/// <summary>The creation order among all voices (see <see cref="SoundEventSystem.ComparePoolOrder"/>).</summary>
	internal long Serial { get; } = VoiceOrder.Next();
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
	internal HeldSoundLifetime? Lifetime { get; set; }
	/// <summary>The placeholder's parameter table (+0x4C), null until the event's code, a sound's selector or a parameter call creates it.</summary>
	private SoundParameterTable? parameters;

	internal SoundSentence( SoundEventSystem system, SoundCategory category, SoundEvent soundEvent, AudioChannel channel, Func<uint>? sampleDraw = null )
	{
		this.system = system;
		this.category = category;
		this.soundEvent = soundEvent;
		this.channel = channel;
		this.sampleDraw = sampleDraw ?? system.Draw;
		parameters = SoundParameterTable.ForEvent( soundEvent );
	}

	/// <summary>The place a positioned sentence plays at (null: flat); <see cref="MoveTo"/> moves it.</summary>
	public Vector3? Position { get; internal set; }
	/// <summary>The range of a positioned sentence.</summary>
	public float Range { get; init; }
	/// <summary>The value branching links are matched against (slot 0's value byte; for park music the guest count / 2, capped); <see cref="SetParameter"/> with the event's code sets it.</summary>
	public int Parameter { get; set; }
	public int CurrentSound => soundIndex;
	internal int CurrentSample => lastSample;
	/// <summary>The playing voice of the current segment (null between segments).</summary>
	internal AudioVoice? Voice => voice;

	/// <summary>
	/// The library's parameter call (the original's 0x51BC40 → 0x6BBB80): <paramref name="value"/> as a byte goes into every slot of the placeholder's table coded
	/// <paramref name="code"/>, and a match in slot 1-3 re-evaluates the playing segment's volume and pitch at once. A call with the event's own code (slot 0)
	/// also sets the branching <see cref="Parameter"/>. The crowd (7), the park music (4), the level rain (8) and the ride and tour vehicles' engines
	/// (EventMap variable 10, tour 80/15/30/60) call it.
	/// </summary>
	// [BIN:TPW-EXE:0x6bbb80 SetParameter] the table is created when missing (0x6b7150); the code is compared with all four slot codes of the placeholder's table (+0x4C) and the byte value stored in each match; a match in slot 1-3, with placeholder flag 0x4000 clear, calls vf+0x34 (0x6bbbe0)
	// [BIN:TPW-EXE:0x6bbf60 placeholder setup] slot 0 holds the event's code (event record +0x12), slots 1 and 2 the current sound's selectors +0x16 and +0x1A truncated to bytes and slot 3 is 0 (0x6bbf00, run again whenever the sentence moves to another sound, 0x6bc8d7); the volume query 0x6bc040 reads slot 1 under mask +0x18 and slot 2 under mask +0x1C
	// [BIN:TPW-EXE:0x6BBBE0 re-evaluate] with a sample playing (+0x24), recomputes volume unless fade flag 0x400 is set (0x6BBC5E..0x6BBC98), and always evaluates pitch again (0x6BBC98..0x6BBCA4). The fade flag protects the current decaying sample volume; it does not mean an absolute fixed gain of 60 percent. Logical sample volume is preserved across host segment changes while fading.
	// [BIN:TPW-EXE:0x006BDA90 sentence SetParameter] the sentence containers (vtables 0x70a390, 0x70a2e0) allocate the table in their constructor and override the call: 0x6bbb80, then the wait flag 0x40 (+0x30) is cleared unless slot 0's value is 0x7f; OpenTPW's waiting branching sentence is restarted by its callers' Resume after the call (GameAudio), and the 0x7f exception and what sets 0x40 are not read
	// [BIN:TPW-EXE:0x006BDAC0 sentence re-evaluate] the container's vf+0x34 walks its child placeholders (+0x18 chain) and re-evaluates each, and a child's queries read the parent's table (0x6c3d80); OpenTPW's sentence holds the table and its one playing segment, so re-evaluating the segment is the same walk
	public void SetParameter( int code, int value )
	{
		if ( IsStopped || Lifetime is { CanAdmit: false } ) return;
		parameters ??= new SoundParameterTable();
		// [BIN:TPW-EXE:0x006BE5E0 branch choice] the branch picker (0x6BE450) reads slot 0's value byte (table +4), which a call writes only when its code is the event's code (slot 0)
		if ( unchecked((byte)code) == parameters.Code( 0 ) )
			Parameter = unchecked((byte)value);
		if ( !parameters.Set( unchecked((byte)code), unchecked((byte)value) ) || voice is not { IsFinished: false } playing )
			return;
		var sound = soundEvent.Sounds[soundIndex];
		if ( Lifetime?.Fading != true )
		{
			var volume = system.Volume( sound, parameters );
			if ( Lifetime is { IsContainer: false } logical ) logical.SetVolume( volume );
			else playing.Gain = volume / 100f;
		}
		playing.SetPitch( system.Pitch( sound, parameters ) );
	}

	/// <summary>Whether <paramref name="code"/> is in slot 1, 2 or 3 of a sound: its selectors +0x16 and +0x1A (low bytes), or slot 3's 0.</summary>
	internal static bool BindsSlot( SoundEntry sound, byte code ) =>
		code == 0 || sound.Raw.Length >= 28 && (code == sound.Raw[22] || code == sound.Raw[26]);
	public bool IsPlaying => voice is { IsFinished: false };
	public bool IsStopped { get; private set; }
	public SoundEvent Event => soundEvent;

	internal void Start()
	{
		// [BIN:STP-PPC:sound_shared 0x100192F0 AssignSoundToNextBranch] a branching sentence starts on the event's first sound
		var first = soundEvent.Player == SoundEventPlayer.BranchingSentence ? 0 : Math.Max( 0, system.PickSound( soundEvent.Sounds ) );
		// [BIN:TPW-EXE:0x006BBF60 placeholder setup] the chosen sound's selectors are in the table from the creation (0x6BBF00), so a parameter set before the first sample plays is kept
		soundIndex = first;
		SoundParameterTable.SelectSound( ref parameters, soundEvent.Sounds[first] );
		Prefetch( first );
		waiting = true;
	}

	public void Stop()
	{
		IsStopped = true;
		Lifetime?.End();
		next = null;
		waiting = false;
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
		if ( IsStopped || Lifetime is { CanAdmit: false } || next is not { IsCompleted: true } task )
			return false;
		next = null;
		if ( !task.IsCompletedSuccessfully ) { Stop(); return true; }
		var (sound, sample, audio) = task.Result;
		if ( audio == null ) { Stop(); return true; }
		soundIndex = sound;
		lastSample = sample;
		// [BIN:TPW-EXE:0x6bc8d7 sound change] moving to a sound runs the placeholder's sound setup again (0x6bbf00): slots 1 and 2 take the new sound's selectors, the values stay
		SoundParameterTable.SelectSound( ref parameters, soundEvent.Sounds[sound] );
		// [BIN:TPW-EXE:0x6bc410 sample start] a new segment evaluates its volume (0x6bc090) and pitch (0x6bc170) with the values the slot table holds; a later parameter change re-evaluates them (SetParameter)
		var volume = Lifetime is { PreserveVolume: true } logical ? logical.OwnVolume : system.Volume( soundEvent.Sounds[sound], parameters );
		var gain = volume / 100f;
		var pitch = system.Pitch( soundEvent.Sounds[sound], parameters );
		// [BIN:TPW-EXE:0x6bc4f4 sample start] the delay is drawn after the volume and the pitch at every segment; a sentence's voice is ranked as the sentence itself, so the draw is all of it that is observable here
		_ = system.DelaySeconds( soundEvent.Sounds[sound] );
		voice = Position is { } place ? system.Mixer.Play( audio, channel, gain, place, Range ) : system.Mixer.Play( audio, channel, gain );
		Lifetime?.Admit( voice, volume );
		voice.SetPitch( pitch );
		voice.Completed += OnCompleted;
		// [EXT:sentence-prefetch] a sentence's next segment is chosen and decoded ahead when the current one starts, so the draw and the parameter are taken one segment early; the original chooses when the current segment's data runs out (sound_shared GetDataChunk 0x10008CC4 -> 0x1001A1F0), but its chooser is the shared clock-seeded one (see Advance), so the draw's timing has no observable rule to match
		var successor = Successor();
		if ( successor >= 0 )
			Prefetch( successor );
		return true;
	}

	private void OnCompleted( AudioVoice completed )
	{
		if ( !ReferenceEquals( voice, completed ) ) return;
		voice = null;
		if ( IsStopped || Lifetime is { CanAdmit: false } )
			return;
		// a segment that ends (or is stopped) out of range starts nothing: Pump starts the next one when the listener is back
		waiting = OutOfRange || !TryPlayNext();
	}

	private bool OutOfRange => (soundEvent.Flags & SoundEventSystem.AnyDistanceFlag) == 0 && !SoundEventSystem.Audible( Position, Range );

	/// <summary>Moves a positioned sentence and its playing segment (the original's placeholder position call).</summary>
	// [BIN:TPW-EXE:0x006BBCE0 placeholder position] the place is stored at +0x10 and, while a sample plays (+0x24), handed to it
	public void MoveTo( Vector3 position )
	{
		AudioListener.ValidatePosition( position, nameof( position ) );
		Position = position;
		if ( voice is { } playing )
			playing.Position = position;
	}

	/// <summary>The value a sound's parameter binding of <paramref name="kind"/> reads (1 volume, 2 pitch), or null without a table or binding (tests).</summary>
	internal int? ParameterValue( int kind ) => parameters != null && parameters.TryValue( soundEvent.Sounds[soundIndex], kind, out var value ) ? value : null;

	internal void Pump()
	{
		if ( IsStopped || Lifetime is { CanAdmit: false } )
			return;
		// [BIN:TPW-EXE:0x6b62b0 sound manager tick] a voice out of range is not admitted to the pool (0x6bca10) and so not ticked, and its sample is stopped (0x6bca70, message 3)
		// [APPROX:AUDIO-038] the sentence starts its prefetched next segment on return. Native 0x6BDF50 recreates the selected descriptor, which refill 0x6BDE00 may already have advanced before audible completion; mapping this early-prefetch extension remains unresolved.
		if ( OutOfRange )
		{
			if ( voice is { IsFinished: false } playing )
			{
				playing.Stop();
				Lifetime?.LoseOutputReply();
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
		if ( IsStopped || Lifetime is { CanAdmit: false } || next != null )
			return;
		var successor = Successor();
		if ( successor >= 0 )
		{
			Prefetch( successor );
			waiting = voice == null;
		}
	}
}

/// <summary>
/// A placeholder's parameter table (four code bytes, four value bytes): slot 0 holds the event's parameter code, slots 1 and 2 the playing sound's two
/// parameter selectors; a sound binds a selector to volume (kind 1) or pitch (kind 2) through the mask that follows it.
/// </summary>
// [BIN:TPW-EXE:0x006B7150 parameter table] 0x20 bytes at placeholder +0x4C: codes at +0..3, values at +4..7, created zeroed on first need
internal sealed class SoundParameterTable
{
	public const int KindVolume = 1;
	public const int KindPitch = 2;
	private readonly byte[] codes = new byte[4];
	private readonly byte[] values = new byte[4];

	/// <summary>The table an event's placeholder starts with: one when the event's short +0x12 is nonzero, its low byte as slot 0's code.</summary>
	// [BIN:TPW-EXE:0x006BBF60 placeholder setup] a nonzero event short +0x12 creates the table and stores its low byte as code 0
	public static SoundParameterTable? ForEvent( SoundEvent soundEvent )
	{
		if ( soundEvent.Extra == 0 )
			return null;
		var table = new SoundParameterTable();
		table.codes[0] = unchecked((byte)soundEvent.Extra);
		return table;
	}

	/// <summary>The table of a placeholder that plays <paramref name="sound"/> of <paramref name="soundEvent"/> with no parameter call (a one-shot or a chain): the event's code and the sound's selectors, or null when both are 0.</summary>
	// [BIN:TPW-EXE:0x006BBF60 placeholder setup] the event's code (0x6B7150 when nonzero), then the chosen sound's selectors (0x6BBF00)
	public static SoundParameterTable? ForPlayback( SoundEvent soundEvent, SoundEntry sound )
	{
		var table = ForEvent( soundEvent );
		SelectSound( ref table, sound );
		return table;
	}

	/// <summary>Takes the selectors of the sound the placeholder chose.</summary>
	// [BIN:TPW-EXE:0x006BBF00 sound parameters] with both shorts +0x16 and +0x1A of the sound 0, an existing table's codes 1–3 are cleared; otherwise the table is created as needed and codes 1 and 2 take their low bytes, code 3 is cleared (the values are kept)
	public static void SelectSound( ref SoundParameterTable? table, SoundEntry sound )
	{
		var first = sound.Raw.Length >= 24 ? BitConverter.ToUInt16( sound.Raw, 22 ) : (ushort)0;
		var second = sound.Raw.Length >= 28 ? BitConverter.ToUInt16( sound.Raw, 26 ) : (ushort)0;
		if ( first == 0 && second == 0 )
		{
			if ( table != null )
				table.codes[1] = table.codes[2] = table.codes[3] = 0;
			return;
		}
		table ??= new SoundParameterTable();
		table.codes[1] = unchecked((byte)first);
		table.codes[2] = unchecked((byte)second);
		table.codes[3] = 0;
	}

	/// <summary>Stores <paramref name="value"/> in every slot coded <paramref name="code"/>; true when a slot 1–3 matched (the playing sample is evaluated again).</summary>
	public bool Set( byte code, byte value )
	{
		var reevaluate = false;
		for ( var slot = 0; slot < 4; slot++ )
		{
			if ( codes[slot] != code )
				continue;
			values[slot] = value;
			if ( slot != 0 )
				reevaluate = true;
		}
		return reevaluate;
	}

	/// <summary>The value bound to <paramref name="kind"/> for <paramref name="sound"/>: the first mask (short +0x18) selects slot 1, else the second (short +0x1C) slot 2.</summary>
	// [BIN:TPW-EXE:0x006BC040 parameter query] kind &amp; mask +0x18 returns value byte +5, else kind &amp; mask +0x1C returns value byte +6, else nothing
	public bool TryValue( SoundEntry sound, int kind, out int value )
	{
		var first = sound.Raw.Length >= 26 ? BitConverter.ToUInt16( sound.Raw, 24 ) : 0;
		var second = sound.Raw.Length >= 30 ? BitConverter.ToUInt16( sound.Raw, 28 ) : 0;
		value = (kind & first) != 0 ? values[1] : (kind & second) != 0 ? values[2] : 0;
		return (kind & (first | second)) != 0;
	}

	internal byte Code( int slot ) => codes[slot];
}

/// <summary>
/// The sound system's holdings at one moment (<see cref="SoundEventSystem.Counts"/>): the mixer's voices and the distinct PCM they play, the sentences, held chains,
/// placed sounds waiting for their decode, the tracked one-shots and loops, the held sounds, the decoded-sample cache and the decodes in flight.
/// </summary>
public readonly record struct SoundSystemCounts( int Voices, long VoiceSampleBytes, int Sentences, int Chains, int Placements, int OneShots, int LoopSamples,
	int HeldSounds, int DecodedSamples, long DecodedBytes, int DecodesInFlight );

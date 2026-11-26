namespace OpenTPW;

/// <summary>
/// The saved state of the advisor's presentation (docs/reverse/TPI-ADVISOR.md §4): the presentation random stream, the speech's clock words, the voice, the LIP walk, the mouth and the clip
/// sequence. A load restores it, so the next draws and the speaking test follow as in an uninterrupted run; the client's audio starts silent.
/// </summary>
public sealed record ThemeParkIncAdvisorPresentationSnapshot( uint Random, bool Speaking, int Response, uint Start, uint Base, uint SpeechEnd, uint VoiceDue, bool VoiceLive, uint VoiceEnd,
	bool HeadHidden, bool HeadSuppressed, bool LipLoaded, int LipIndex, int LipNext, bool Talking, int Mouth, int MouthNext, uint Timer, int[] Clips, int[] Bits, bool Immediate );

/// <summary>What the presentation asks of the client: a speech started, its voice started, a stop sound, a canned timer fired.</summary>
public enum ThemeParkIncAdvisorCueKind { Speech, Voice, Stop, Timer }

/// <summary>A presentation event for the client, at advisor clock <paramref name="At"/> (milliseconds); <paramref name="Sound"/> is the voice or the stop sound's speech event.</summary>
public readonly record struct ThemeParkIncAdvisorCue( ThemeParkIncAdvisorCueKind Kind, int Response, uint Sound, uint At );

/// <summary>The speech data the presentation reads in the simulation: never an audio device, so every run sees the same durations.</summary>
internal interface IThemeParkIncAdvisorSpeechData
{
	/// <summary>The voice's length in milliseconds, null when the speech data has no voice for it.</summary>
	uint? VoiceLength( ThemeParkIncAdviceResponse response );

	/// <summary>The LIP marks (microseconds), null when there is no LIP file.</summary>
	IReadOnlyList<uint>? LipMarks( ThemeParkIncAdviceResponse response );

	/// <summary>The 25 clip lengths of the advisor model, null when no model clip is found.</summary>
	AdvisorClipDurations? ClipDurations { get; }
}

/// <summary>
/// Theme Park Inc's advisor presentation, run in the simulation: the speech (<c>0x716230</c>), the per-frame advisor (<c>0x716ED0</c>) and the stop (<c>0x716950</c>). It decides how long a
/// speech holds the advisor (the playback window), whether a new speech is refused because one still speaks, and the three <c>rand()</c> draws of the presentation, which come from a stream of
/// its own. The model, the clips and the audio are the client's, which follows the state and the cues.
/// </summary>
internal sealed class ThemeParkIncAdvisorPresentation
{
	/// <summary>The mouth shapes (<c>0xBEF18C</c>, set to 5 by the init <c>0x715850</c> and never changed).</summary>
	public const int Mouths = 5;

	/// <summary>The mouth changes at most once per this many milliseconds while talking.</summary>
	public const int MouthInterval = 100;

	/// <summary>The stop sounds (<c>0x46E</c>, <c>0x46F</c>, <c>0x470</c> of the global speech bank).</summary>
	public const uint FirstStopSound = 0x46E;

	/// <summary>The length the original reads for a voice that does not play: <c>0x653060</c>'s initial 1.</summary>
	// [BIN:TPI-EXE:0x653069 no voice] the length query starts its answer at 1 and returns it unchanged without a sound system (0xADF07C, 0xADF088); a speech whose sample does not play has
	// handle 0, which the engine does not answer either. D-3 (lead): the no-voice path is the original's, never an invented duration
	public const uint NoVoiceLength = 1;

	private readonly IThemeParkIncAdvisorSpeechData data;
	private readonly List<ThemeParkIncAdvisorCue> cues = new();
	private AdvisorRandom random;
	private IReadOnlyList<uint>? marks;
	private AdvisorClipDurations? durations;

	public ThemeParkIncAdvisorPresentation( IThemeParkIncAdvisorSpeechData data, uint seed )
	{
		this.data = data ?? throw new ArgumentNullException( nameof( data ) );
		random = new AdvisorRandom( seed );
	}

	/// <summary>The presentation stream's state.</summary>
	public uint RandomState => random.State;
	/// <summary><c>0xBEF074</c>: a speech is active.</summary>
	public bool Speaking { get; private set; }
	/// <summary><c>0x9DDBE4</c>: the response said last.</summary>
	public int Response { get; private set; } = ThemeParkIncAdvisor.NoResponse;
	/// <summary>The advisor clock when the last speech started.</summary>
	public uint Start { get; private set; }
	/// <summary><c>0xBEF06C</c>: the speech's base, the start less 200 (1000 later for a delayed voice); the LIP marks count from it.</summary>
	public uint Base { get; private set; }
	/// <summary><c>0xBEF070</c>: the speech's end, the base plus the sequence.</summary>
	public uint SpeechEnd { get; private set; }
	/// <summary><c>0xBEF09C</c>: when the delayed voice starts, 0 for none.</summary>
	public uint VoiceDue { get; private set; }
	/// <summary><c>0xBEF088</c> is set: the voice plays (until <see cref="VoiceEnd"/>).</summary>
	public bool VoiceLive { get; private set; }
	public uint VoiceEnd { get; private set; }
	/// <summary><c>0xBF7240</c>: the head is hidden (no LIP, no stop sound).</summary>
	public bool HeadHidden { get; private set; }
	/// <summary><c>0xBF7241</c>: the head shows without its parts (animation 0x11).</summary>
	public bool HeadSuppressed { get; private set; }
	/// <summary><c>0xBEF1B8</c>, <c>0xBEF1B4</c>, <c>0xBEF190</c>, <c>0xBEF078</c>: the LIP is loaded, its next mark, the mark's time and whether the advisor talks.</summary>
	public bool LipLoaded { get; private set; }
	public int LipIndex { get; private set; }
	public int LipNext { get; private set; }
	public bool Talking { get; private set; }
	/// <summary><c>0xBEF1A4</c>, <c>0xBEF1AC</c>: the mouth shown (1 closed, 1 to 5 while talking) and when it may change next.</summary>
	public int Mouth { get; private set; }
	public int MouthNext { get; private set; }
	/// <summary><c>0xBEF194</c>: the canned sequence's timer, 0 for none.</summary>
	public uint Timer { get; private set; }
	/// <summary>The clips of the last sequence (<c>0xBEF134</c>, without the terminator) and their accessory bits, which the client plays.</summary>
	public IReadOnlyList<int> Clips { get; private set; } = Array.Empty<int>();
	public IReadOnlyList<int> Bits { get; private set; } = Array.Empty<int>();
	/// <summary>The last speech's voice started with it (<c>0xBEF09C</c> cleared by the canned row).</summary>
	public bool Immediate { get; private set; }

	/// <summary>The cues since the last call, oldest first.</summary>
	public IReadOnlyList<ThemeParkIncAdvisorCue> TakeCues()
	{
		var taken = cues.ToArray();
		cues.Clear();
		return taken;
	}

	private AdvisorClipDurations Durations => durations ??= data.ClipDurations ?? new AdvisorClipDurations( Array.Empty<uint>(), AdvisorClipDurations.ThemeParkIncCount );

	// [EXT:tpi-advisor-presentation-rand] (user-approved 2026-11-24) the original's three presentation draws (0x715AC4, 0x716A2C, 0x717175) call the C runtime's rand(), one stream shared with
	// every other rand() of the process. They decide the speech's length and so the playback window, which the simulation reads: they come from one stream of their own, the same generator,
	// seeded from the session seed (D-4), saved and hashed, and never from the world generator
	private int Draw() => random.Next();

	/// <summary>
	/// The speech of <paramref name="response"/> at <paramref name="now"/> (<c>0x716230</c>): 0 when it is refused, else the milliseconds it returns, the sequence plus clip 15 plus 1000.
	/// <paramref name="advisorOn"/> is the option; <paramref name="help"/> tells that the response is the help button's, which the option does not silence.
	/// </summary>
	// [BIN:TPI-EXE:0x716230 speech] a response missing from the table returns 0; so does an active speech (0xBEF074), and with AdvisorOn (0xA10218) off a speech outside the presenter's ranges
	// (0x54D3E0 < 0, 0x54D4D0 false) that is not the help button's response (0x9DDBE8). Then 0x9DDBE8 = 0x3F6 and 0x9DDBE4 = the response; the base is the presentation clock (less the paused
	// time, 0 here: the advisor clock stops while paused) less 200; the head is hidden by the animation word, the accessory 0x29A or the presenter, and shows without its parts for animation 0x11;
	// a shown head loads the LIP (talking from its start); the delayed voice is due at base + 1000; the voice plays (0x652860) and its length + 200 makes the sequence (GenerateAnimSequence or
	// SetCannedSequence); a voice still delayed is stopped again, talking cleared and the base moved by 1000; the next LIP mark is base + mark / 1000; the end is base + the sequence; a set timer
	// moves by the base
	public uint Speak( int response, uint now, bool animationWord, bool advisorOn, bool help )
	{
		if ( ThemeParkIncAdviceResponse.For( response ) is not { } row )
			return 0;
		var forced = ThemeParkIncAdviceResponse.PresenterGroup( row.Voice ) >= 0;
		if ( Speaking || !advisorOn && !forced && !help )
			return 0;
		if ( forced || row.LevelBank )
			throw new NotSupportedException( $"Theme Park Inc advisor response {response}: the presenter's speeches and the level's speech bank are not ported." );
		Response = response;
		Start = now;
		Base = unchecked( now - 200 );
		// [EXT:tpi-advisor-tick-clock] D-2: the "game" key group (0xA07A28 +6) is a client state the simulation reads as on, as a running park has it
		HeadHidden = animationWord || row.Accessory == ThemeParkIncAdviceResponse.HiddenHead;
		HeadSuppressed = row.Animation == ThemeParkIncAdviceResponse.SuppressedHead;
		if ( HeadSuppressed )
			HeadHidden = false;
		Talking = false;
		LipLoaded = false;
		marks = null;
		if ( !HeadSuppressed && !HeadHidden && data.LipMarks( row ) is { } lips )
		{
			marks = lips;
			(LipLoaded, LipIndex, Talking) = (true, 0, true);
		}
		VoiceDue = unchecked( Base + 1000 );
		var length = data.VoiceLength( row );
		(VoiceLive, VoiceEnd) = (length != null, unchecked( now + (length ?? 0) ));
		var (duration, sequence, clips, immediate) = ThemeParkIncAdvisorSequences.Speech( row.Animation, length ?? NoVoiceLength, Durations, Draw );
		(Clips, Bits, Immediate) = (clips?.Clips ?? Array.Empty<int>(), clips?.AccessoryBits ?? (clips?.Clips.Select( _ => 0 ).ToArray() ?? Array.Empty<int>()), immediate);
		if ( clips is { TimerMilliseconds: { } timer } )
			Timer = unchecked( (uint)timer );
		else if ( row.Animation != -1 )
			Timer = 0;
		cues.Add( new ThemeParkIncAdvisorCue( ThemeParkIncAdvisorCueKind.Speech, response, row.Voice, now ) );
		if ( immediate )
		{
			VoiceDue = 0;
			if ( VoiceLive )
				cues.Add( new ThemeParkIncAdvisorCue( ThemeParkIncAdvisorCueKind.Voice, response, row.Voice, now ) );
		}
		else
		{
			VoiceLive = false;
			Talking = false;
			Base = unchecked( Base + 1000 );
		}
		LipNext = marks is { Count: > 0 } ? unchecked( (int)Base + Mark( LipIndex++ ) ) : unchecked( (int)Base - 1 );
		SpeechEnd = unchecked( Base + sequence );
		Speaking = true;
		if ( Timer != 0 )
			Timer = unchecked( Timer + Base );
		return duration;
	}

	// the walk reads the next word of the decoded LIP buffer (-1 ends it) and divides it by 1000 as a signed value (0x10624DD3, 0x717119)
	private int Mark( int index ) => (int)marks![index] / 1000;

	/// <summary>The per-frame advisor at advisor clock <paramref name="now"/> (<c>0x716ED0</c>): the speech's end, the delayed voice, the canned timer, the LIP walk and the mouth.</summary>
	// [BIN:TPI-EXE:0x716ED0 advisor frame] not speaking: the LIP buffer is freed. Speaking: a voice handle asks the sound whether it still plays (0x652C40); with none left and the end reached
	// (end - now <= 0) the speech ends (0xBEF074 = 0); a due delayed voice (due - now <= 0) plays and sets talking; a due timer plays effect 0x57 and sound 0xC6 (bank 0xAE0124) and clears; the
	// clips step on the model (the client's); with the LIP loaded and now > the next mark talking flips and the next mark is read (none left: the LIP ends, talking stops and the mouth is 1);
	// talking with the LIP: once now > 0xBEF1AC the mouth is rand() % [0xBEF18C] + 1 and 0xBEF1AC = now + 100, else it stays; not talking: mouth 1.
	// [EXT:tpi-advisor-tick-clock] the original runs it once per drawn frame; OpenTPW runs it once per 31 ms substep of the world tick on the advisor clock (ThemeParkIncAdvisor.AdvanceClock),
	// one frame per substep as an ideal frame pacing has it, and plays the voice for its sample length from its start on that clock.
	// [EXT:tpi-advisor-presentation-rand] (user-approved 2026-11-24) rand() at 0x717175 draws from the presentation stream
	public void Frame( uint now )
	{
		if ( !Speaking )
		{
			if ( LipLoaded )
				(LipLoaded, marks) = (false, null);
			return;
		}
		if ( VoiceLive )
			VoiceLive = unchecked( (int)(VoiceEnd - now) ) > 0;
		if ( !VoiceLive && unchecked( (int)(SpeechEnd - now) ) <= 0 )
			Speaking = false;
		if ( VoiceDue != 0 && unchecked( (int)(VoiceDue - now) ) <= 0 )
		{
			if ( ThemeParkIncAdviceResponse.For( Response ) is { } row && data.VoiceLength( row ) is { } length )
			{
				(VoiceLive, VoiceEnd) = (true, unchecked( now + length ));
				cues.Add( new ThemeParkIncAdvisorCue( ThemeParkIncAdvisorCueKind.Voice, Response, row.Voice, now ) );
			}
			VoiceDue = 0;
			Talking = true;
		}
		if ( Timer != 0 && unchecked( (int)(Timer - now) ) <= 0 )
		{
			cues.Add( new ThemeParkIncAdvisorCue( ThemeParkIncAdvisorCueKind.Timer, Response, 0, now ) );
			Timer = 0;
		}
		var lipLoaded = LipLoaded;
		if ( lipLoaded && unchecked( (int)now ) > LipNext )
		{
			Talking = !Talking;
			if ( marks != null && LipIndex < marks.Count )
				LipNext = unchecked( Mark( LipIndex++ ) + (int)Base );
			else
			{
				(LipLoaded, Talking, Mouth, marks) = (false, false, 1, null);
				return;
			}
		}
		if ( Talking && lipLoaded )
		{
			if ( unchecked( (int)now ) > MouthNext )
			{
				Mouth = Draw() % Mouths + 1;
				MouthNext = unchecked( (int)now + MouthInterval );
			}
		}
		else
			Mouth = 1;
	}

	/// <summary>Stops the speech (<c>0x716950</c>): the voice, the LIP and the clips; a voice that was playing behind a shown head ends with a stop sound chosen by <c>rand() % 3</c>.</summary>
	// [BIN:TPI-EXE:0x716950 advisor stop] only while speaking: with a voice handle: the voice is stopped and, unless the head is hidden (0xBF7240) or without its parts (0xBF7241), rand() % 3
	// plays 0x46E, 0x46F or 0x470 of the global speech bank (0xAE012C); without one: the same without the sound. Either way 0xBEF074 = 0, the clips end and the LIP is freed; the delayed voice
	// and the timer words are left as they are. [EXT:tpi-advisor-presentation-rand] (user-approved 2026-11-24) rand() at 0x716A2C draws from the presentation stream
	public void Stop()
	{
		if ( !Speaking )
			return;
		var live = VoiceLive;
		Speaking = false;
		(LipLoaded, marks, VoiceLive) = (false, null, false);
		if ( live && !HeadSuppressed && !HeadHidden )
			cues.Add( new ThemeParkIncAdvisorCue( ThemeParkIncAdvisorCueKind.Stop, Response, FirstStopSound + (uint)(Draw() % 3), 0 ) );
		else if ( live )
			cues.Add( new ThemeParkIncAdvisorCue( ThemeParkIncAdvisorCueKind.Stop, Response, 0, 0 ) );
	}

	public ThemeParkIncAdvisorPresentationSnapshot Capture() => new( RandomState, Speaking, Response, Start, Base, SpeechEnd, VoiceDue, VoiceLive, VoiceEnd, HeadHidden, HeadSuppressed,
		LipLoaded, LipIndex, LipNext, Talking, Mouth, MouthNext, Timer, Clips.ToArray(), Bits.ToArray(), Immediate );

	/// <summary>Whether a saved presentation can be restored: a loaded LIP needs a response OpenTPW plays, and the clips and bits pair up.</summary>
	public static bool IsValid( ThemeParkIncAdvisorPresentationSnapshot state ) =>
		state is { Clips: not null, Bits: not null, LipIndex: >= 0 } && state.Clips.Length == state.Bits.Length && state.Mouth is >= 0 and <= Mouths
		&& (!state.LipLoaded && !state.Speaking || ThemeParkIncAdviceResponse.For( state.Response ) != null);

	/// <summary>Restores a saved presentation; a loaded LIP is read again from the speech data (its marks are data).</summary>
	public void Restore( ThemeParkIncAdvisorPresentationSnapshot state )
	{
		if ( !IsValid( state ) )
			throw new InvalidDataException( "Invalid Theme Park Inc advisor presentation." );
		random = new AdvisorRandom( state.Random );
		(Speaking, Response, Start, Base, SpeechEnd, VoiceDue, VoiceLive, VoiceEnd) =
			(state.Speaking, state.Response, state.Start, state.Base, state.SpeechEnd, state.VoiceDue, state.VoiceLive, state.VoiceEnd);
		(HeadHidden, HeadSuppressed, LipLoaded, LipIndex, LipNext, Talking, Mouth, MouthNext, Timer) =
			(state.HeadHidden, state.HeadSuppressed, state.LipLoaded, state.LipIndex, state.LipNext, state.Talking, state.Mouth, state.MouthNext, state.Timer);
		(Clips, Bits, Immediate) = (state.Clips.ToArray(), state.Bits.ToArray(), state.Immediate);
		marks = LipLoaded && ThemeParkIncAdviceResponse.For( Response ) is { } row ? data.LipMarks( row ) : null;
		if ( LipLoaded && (marks == null || LipIndex > marks.Count) )
			throw new InvalidDataException( $"The saved advisor speech's LIP (response {Response}) does not match the speech data." );
		cues.Clear();
	}

	/// <summary>Restarts the presentation silent with a fresh stream, as the init <c>0x715850</c> leaves it (a save from before the presentation).</summary>
	// [BIN:TPI-EXE:0x715850 advisor init] the 0x5F words from 0xBEF068 are zeroed (no speech, mouth 0, no LIP, no timer), 0xBEF18C = 5
	public void Reset( uint seed )
	{
		random = new AdvisorRandom( seed );
		(Speaking, Response, Start, Base, SpeechEnd, VoiceDue, VoiceLive, VoiceEnd) = (false, ThemeParkIncAdvisor.NoResponse, 0, 0, 0, 0, false, 0);
		(HeadHidden, HeadSuppressed, LipLoaded, LipIndex, LipNext, Talking, Mouth, MouthNext, Timer) = (false, false, false, 0, 0, false, 0, 0, 0);
		(Clips, Bits, Immediate, marks) = (Array.Empty<int>(), Array.Empty<int>(), false, null);
		cues.Clear();
	}
}

/// <summary>
/// The speech data of the presentation, read from the game files in the simulation: the voice's length decoded from its sample in the global <c>cat_speech</c> catalogue, the LIP from
/// <c>global/Speech/lips</c> and the clip lengths of the level's advisor (else the global one). Kept per response for the park's life.
/// </summary>
internal sealed class ThemeParkIncAdvisorSpeech : IThemeParkIncAdvisorSpeechData
{
	private readonly string? level;
	private readonly Dictionary<int, uint?> lengths = new();
	private readonly Dictionary<int, IReadOnlyList<uint>?> lips = new();
	private SoundCategory? speech;
	private bool speechLoaded, durationsLoaded;
	private AdvisorClipDurations? durations;

	/// <param name="level">The level whose advisor archive (<c>/levels/&lt;level&gt;/advisor</c>) the clip lengths come from first.</param>
	public ThemeParkIncAdvisorSpeech( string? level ) => this.level = level;

	/// <summary>The clip lengths: the level advisor's (model 1, which 0x4B0170 loads after the global one) when it has clips, else the global advisor's.</summary>
	// [BIN:TPI-EXE:0x4B0170 advisor models] model 0 is the global advisor (Data\Global\Advisor, "Advisor.md2"), model 1 the level's ("Advisor%s.md2" with the level's suffix, advisor.wad);
	// then the init 0x715850 reads the lengths from model 1 when it is loaded (0x40A597(1)), else model 0
	public AdvisorClipDurations? ClipDurations
	{
		get
		{
			if ( !durationsLoaded && FileSystem is { } files )
			{
				durationsLoaded = true;
				durations = AdvisorClipDurations.Load( files, level, AdvisorClipDurations.ThemeParkIncCount );
			}
			return durations;
		}
	}

	// [BIN:TPI-EXE:0x71663F advisor sample length] "Advisor sample is %d ms long": the sound engine's length of the playing sample (0x653060), read here from the sample's data as the client
	// decodes it (its frames × 1000 / its rate)
	public uint? VoiceLength( ThemeParkIncAdviceResponse response )
	{
		if ( lengths.TryGetValue( response.Response, out var known ) )
			return known;
		return lengths[response.Response] = Measure( response );
	}

	private uint? Measure( ThemeParkIncAdviceResponse response )
	{
		if ( FileSystem is not { } files )
			return null;
		if ( !speechLoaded )
		{
			speechLoaded = true;
			speech = SoundCategory.TryLoad( files, "/global/Speech", "cat_speech" );
		}
		if ( speech?.Catalog.Find( response.Voice ) is not { Sounds: [{ Samples: [var sample] }] } )
			return null;
		try
		{
			if ( speech.GetBank( sample.BankNumber ) is not { } bank || bank.GetMpeg( sample.SampleNumber, Mp2Decoder.MaximumInputBytes, out _, out var playbackBytes ) is not { } mpeg
				|| Mp2Decoder.Decode( mpeg, playbackBytes ) is not { SampleRate: > 0 } audio )
				return null;
			return (uint)(audio.SampleFrames * 1000L / audio.SampleRate);
		}
		catch ( Exception exception ) when ( exception is IOException or InvalidDataException or NotSupportedException or UnauthorizedAccessException )
		{
			Log.Warning( $"Theme Park Inc advisor: the voice of response {response.Response} does not decode ({exception.Message}); it has no voice." );
			return null;
		}
	}

	// [BIN:TPI-EXE:0x716453 advisor LIP] "\Speech\lips\sp_%04d.lip" with +8 under "Data\Global" (0x9DDB94) or the level's folder (0x435040), opened through 0x497880 (lips.wad)
	public IReadOnlyList<uint>? LipMarks( ThemeParkIncAdviceResponse response )
	{
		if ( lips.TryGetValue( response.Lip, out var known ) )
			return known;
		IReadOnlyList<uint>? marks = null;
		var name = $"sp_{response.Lip:0000}.lip";
		if ( FileSystem is { } files && LipMembers( files ).TryGetValue( name, out var path ) )
		{
			try
			{
				marks = new LipSyncFile( new MemoryStream( files.ReadAllBytes( path ) ) ).Marks;
			}
			catch ( Exception exception ) when ( exception is IOException or InvalidDataException or UnauthorizedAccessException )
			{
				Log.Warning( $"Theme Park Inc advisor: {path} does not read ({exception.Message}); the speech has no LIP." );
			}
		}
		return lips[response.Lip] = marks;
	}

	private Dictionary<string, string>? lipMembers;

	// the archive's members are named SP_0001.LIP and so on; the original opens them by a lower-case name (the file system ignores the case)
	private Dictionary<string, string> LipMembers( BaseFileSystem files )
	{
		if ( lipMembers != null )
			return lipMembers;
		lipMembers = new Dictionary<string, string>( StringComparer.OrdinalIgnoreCase );
		try
		{
			foreach ( var member in files.GetFiles( "/global/Speech/lips" ) )
				lipMembers.TryAdd( Path.GetFileName( member ), member );
		}
		catch ( Exception exception ) when ( exception is IOException or DirectoryNotFoundException or UnauthorizedAccessException or InvalidDataException )
		{
			Log.Warning( $"Theme Park Inc advisor: the LIP archive does not list ({exception.Message}); the speeches have no LIP." );
		}
		return lipMembers;
	}
}

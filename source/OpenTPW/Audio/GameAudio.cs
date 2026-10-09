namespace OpenTPW;

/// <summary>
/// The game's sound service: one mixer, the original sound categories and the park music. Mirrors the
/// Mac game's sound manager (<c>0x100BC6F0</c> registers the global <c>cat_ambient</c>, <c>cat_rides</c>,
/// <c>cat_ui</c>, <c>cat_kids</c>, <c>cat_staff</c> and <c>cat_speech</c>; <c>0x100BC864</c> a level's
/// <c>cat_ambient</c>, <c>cat_rides</c>, <c>cat_speech</c> and <c>cat_music</c>). Volumes come from the
/// options; speech ducks the other channels by <c>SoundInfo.DUCKINGLEVEL</c> from <c>sound.sam</c>.
/// </summary>
public static class GameAudio
{
	/// <summary>The park music event the original starts when a park opens (<c>0x100BC144</c>: event 2 of the level's music category).</summary>
	public const uint ParkMusicEvent = 2;
	/// <summary>The music parameter's cap (<c>0x101C2448</c>: values above 0x59 become 0x59).</summary>
	public const int MaximumMusicParameter = 89;
	private static SoundEventSystem? events;
	private static SoundSentence? music;

	public static bool Enabled { get; set; } = true;
	public static AudioMixer? Mixer => events?.Mixer;
	public static SoundEventSystem? Events => events;
	public static SoundCategory? Ui { get; private set; }
	public static SoundCategory? Speech { get; private set; }
	public static SoundCategory? LevelMusic { get; private set; }
	public static SoundCategory? LevelSpeech { get; private set; }
	public static SoundSentence? Music => music;

	/// <summary>Opens the device and loads the global categories on first use; false when sound is off.</summary>
	public static bool EnsureStarted()
	{
		if ( events != null )
			return true;
		if ( !Enabled )
			return false;
		AudioApproximations.LogOnce();
		var mixer = AudioMixer.Open();
		AudioMixer.Current = mixer;
		events = new SoundEventSystem( mixer, (uint)Environment.TickCount );
		mixer.DuckingLevel = ReadDuckingLevel() / 100f;
		Ui = SoundCategory.TryLoad( FileSystem, "/global/sound", "cat_ui" );
		Speech = SoundCategory.TryLoad( FileSystem, "/global/Speech", "cat_speech" );
		Log.Trace( $"Sound: {(mixer.HasDevice ? "SDL audio" : "no device")}, ducking {mixer.DuckingLevel:P0}, UI events {Ui?.Catalog.Events.Count ?? 0}, speech events {Speech?.Catalog.Events.Count ?? 0}." );
		return true;
	}

	// [DATA:sound.sam:SoundInfo.DUCKINGLEVEL] the percentage music and effects keep while speech plays (38 in the shipped file)
	private static int ReadDuckingLevel()
	{
		try
		{
			if ( !FileSystem.FileExists( "/sound.sam" ) )
				return 100;
			var settings = new SamSettings( new[] { SamDocument.Parse( FileSystem.ReadAllText( "/sound.sam" ), "/sound.sam" ) } );
			return Math.Clamp( settings.GetInt( "SoundInfo.DUCKINGLEVEL", 100, optional: true ), 0, 100 );
		}
		catch ( Exception exception ) when ( exception is IOException or InvalidDataException or FormatException )
		{
			Log.Warning( $"sound.sam ignored: {exception.Message}" );
			return 100;
		}
	}

	/// <summary>Loads a level's categories and starts its park music.</summary>
	public static void EnterPark( string level )
	{
		if ( !EnsureStarted() )
			return;
		LeavePark();
		LevelMusic = SoundCategory.TryLoad( FileSystem, $"/levels/{level}/Music", "cat_music" );
		LevelSpeech = SoundCategory.TryLoad( FileSystem, $"/levels/{level}/Speech", "cat_speech" );
		music = events!.StartSentence( LevelMusic, ParkMusicEvent, AudioChannel.Music );
		Log.Trace( $"Sound: {level} music {(music == null ? "not available" : $"{music.Event.Sounds.Count} sections")}." );
	}

	public static void LeavePark()
	{
		music?.Stop();
		music = null;
		LevelMusic = null;
		LevelSpeech = null;
	}

	/// <summary>
	/// The music parameter for <paramref name="guestsInPark"/>: the original passes the cached guest count
	/// halved and capped at 100 (<c>0x100C2344</c>), then caps it at 89 and uses 0 in world state 4.
	/// </summary>
	// [BIN:STP-PPC:0x101C2444 park turn] music parameter = min(min(guests in park / 2, 100), 89), or 0 when the world state is 4
	public static int MusicParameter( int guestsInPark ) => Math.Min( Math.Min( Math.Max( 0, guestsInPark ) / 2, 100 ), MaximumMusicParameter );

	/// <summary>Per frame: applies the option volumes, the music parameter and tops up the output.</summary>
	public static void Update( int? guestsInPark = null )
	{
		if ( events == null )
			return;
		var mixer = events.Mixer;
		var options = GameOptions.Current;
		mixer.SetChannelGain( AudioChannel.Effects, GameOptions.Gain( options.SoundEffectsVolume ) );
		mixer.SetChannelGain( AudioChannel.Music, GameOptions.Gain( options.MusicVolume ) );
		mixer.SetChannelGain( AudioChannel.Speech, GameOptions.Gain( options.SpeechVolume ) );
		if ( music != null && guestsInPark is { } guests )
		{
			var parameter = MusicParameter( guests );
			if ( parameter != music.Parameter )
			{
				music.Parameter = parameter;
				music.Resume();
			}
		}
		events.Pump();
		mixer.Pump();
	}

	/// <summary>Plays a <c>cat_ui</c> event (button and view clicks).</summary>
	public static AudioVoice? PlayUi( uint eventId ) => events?.Play( Ui, eventId, AudioChannel.Effects );

	public static void Shutdown()
	{
		LeavePark();
		events?.Mixer.Dispose();
		events = null;
		AudioMixer.Current = null;
	}
}

using OpenTPW.FrontEnd;
using OpenTPW.UI.Original;

namespace OpenTPW;

/// <summary>
/// The experimental Theme Park Inc slice of the game flow (docs/THEME-PARK-INC.md): a Theme Park Inc world runs
/// with the shared terrain, object, path and guest systems and the experimental HUD, under the edition capability
	/// policy — money only (no staff, research or challenges), no advisor beyond the four traced weather forecasts, and no sharing. The desktop Science park can continue an OpenTPW save; layout checkpoints start fresh sessions.
/// The experimental flow is not a reproduction of the original Theme Park Inc interface.
/// </summary>
// [EXT:tpi-capability-gates] the slice's frame is gated by OpenTPW's capability policy (a scope decision), not by traced Theme Park Inc behaviour
internal sealed partial class GameFlow
{
	/// <summary>The experimental HUD of the running Theme Park Inc level (null outside the slice).</summary>
	public ThemeParkIncHud? IncHud { get; private set; }

	/// <summary>The Theme Park Inc Personnel File start screen (null outside it).</summary>
	public ThemeParkIncFrontEnd? IncFrontEnd { get; private set; }

	/// <summary>
	/// Shows the Theme Park Inc Personnel File (docs/THEME-PARK-INC.md): Enter Game starts the slice in the chosen zone, Quit Game closes the window.
	/// The zone's files are waited for (<see cref="LevelDataReady"/>) as for any queued level.
	/// </summary>
	// [EXT:tpi-front-end-phase1] an OpenTPW start screen over the traced Personnel File layout, not a reproduction of Theme Park Inc's front end
	public void ShowThemeParkIncFrontEnd()
	{
		TearDown();
		IncFrontEnd = new ThemeParkIncFrontEnd( Strings, new ThemeParkIncFrontEndHost
		{
			EnterWorld = zone => QueueLevel( zone, () => StartLevel( zone, original: true, developerPanels: false ) ),
			ContinueScience = !OperatingSystem.IsBrowser() && File.Exists( IncScienceSavePath() ) ? QueueScienceContinuation : null,
			Quit = Quit
		}, UiFonts.CachedLoader( GameLanguage.Current ) );
		Log.Trace( "Theme Park Inc front end: the Personnel File (experimental slice; Enter Game starts the chosen zone)." );
	}

	private void QueueScienceContinuation()
	{
		var frontEnd = IncFrontEnd;
		if ( frontEnd == null )
			return;
		QueueLevel( "science", () =>
		{
			if ( !ReferenceEquals( IncFrontEnd, frontEnd ) )
				return;
			try { ContinueThemeParkIncScience(); }
			catch ( Exception error ) when ( error is IOException or InvalidDataException or UnauthorizedAccessException
				or ArgumentException or InvalidOperationException or NotSupportedException )
			{
				if ( ReferenceEquals( IncFrontEnd, frontEnd ) )
					frontEnd.ShowLoadFailure( error.Message );
				else
					throw;
			}
		} );
	}

	/// <summary>Starts the experimental HUD over a Theme Park Inc level (called from StartLevel, after TearDown).</summary>
	private ThemeParkIncHud CreateThemeParkIncHud( Level level, GameSpeed speed = GameSpeed.Normal ) => new( level, Strings, new ThemeParkIncHudHost
	{
		Quit = Quit,
		SaveLayout = SaveThemeParkIncLayout,
		LoadLayout = LoadThemeParkIncLayout,
		SavePark = level.LevelName == "science" && !OperatingSystem.IsBrowser() ? SaveThemeParkIncScience : null,
		LoadPark = level.LevelName == "science" && !OperatingSystem.IsBrowser() ? LoadThemeParkIncScience : null,
		SelectNewPlayer = ShowThemeParkIncFrontEnd
	}, speed );

	/// <summary>
	/// One frame of the slice: the experimental HUD takes input, the level runs at its selected speed,
	/// and crowd and precipitation audio follow the current park, as does the weather forecast's speech. Staff voices remain unavailable.
	/// </summary>
	// [EXT:tpi-twp-guest-rules] guests start with Theme Park Inc profiles, meters and generation-specific art, but still use shared movement, sprite animation and guest-update systems where TPI behavior has not been ported
	private void UpdateThemeParkIncSlice( Level level, ThemeParkIncHud hud, UiInput input )
	{
		var captured = hud.Update( Context, input );
		// A confirmed layout load can replace the world inside HUD input; do not tick the retired level.
		if (!ReferenceEquals(Level, level)) return;
		level.UiCapturesMouse = captured;
		level.SimulationTimeScale = hud.TimeScale;
		level.Update();
		GameAudio.Update( level.Guests?.GetStatistics().InPark ?? 0 );
		if ( level.Park?.Economy.TpiWeather is { } weather )
		{
			GameAudio.UpdateThemeParkIncPrecipitation( weather );
			// [BIN:TPI-EXE:0x6423AB thunder due] the strike's top, played once when the storm's thunder countdown reaches zero.
			if ( weather.TakeThunder() is { } thunder )
				GameAudio.PlayThunder( thunder );
		}
		if ( level.Park?.Economy is { } tpiEconomy )
		{
			if ( tpiEconomy.TakeTpiMail() is { } mail )
				PresentThemeParkIncMail( hud, tpiEconomy, mail );
			if ( tpiEconomy.TpiForecast is { } forecast )
				PresentThemeParkIncForecast( level, hud, tpiEconomy, forecast );
		}
		ReverbSelector.Update( level.RideView?.Ride );
	}

	/// <summary>The voice the last forecast started; stopped when the park goes (a global speech one-shot outlives LeavePark).</summary>
	private AudioVoice? themeParkIncForecastVoice;

	/// <summary>
	/// [BIN:TPI-EXE:0x635D80 notification letter] A notification letter (the bank's six-months-in-the-red letter or the challenge system's
	/// firing letter): the tag system queues its advice, whose response plays the speech event from the global catalogue, and opens the letter
	/// window on the record (0x636217). Closing the letter is what loses the park (<see cref="DismissThemeParkIncLetter"/>).
	/// </summary>
	private void PresentThemeParkIncMail( ThemeParkIncHud hud, ParkEconomy economy, ThemeParkIncAdviceMail mail )
	{
		themeParkIncForecastVoice?.Stop();
		themeParkIncForecastVoice = GameAudio.PlaySpeechEffect( mail.SpeechEvent );
		Log.Trace( $"Theme Park Inc letter: message {mail.Message}, response {mail.Response}, speech event {mail.SpeechEvent}, body {mail.TextEntry}." );
		StringFile tags;
		try
		{
			tags = GameLanguage.Current.LoadStrings( "TAG_SYSTEM.str" );
		}
		catch ( Exception exception ) when ( exception is IOException or InvalidDataException or UnauthorizedAccessException )
		{
			Log.Warning( $"TAG_SYSTEM.str is not available for a Theme Park Inc letter: {exception.Message}" );
			return;
		}
		var strings = hud.Strings;
		// [BIN:TPI-EXE:0x5CD14D letter addressee] the To field is the player's name (the object at 0xAA0128)
		// [EXT:no-player-profiles] OpenTPW has no player profiles: the one player is the Personnel File's "New Player" (UITEXT 20), as its first slot shows
		var addressee = strings.Entry( ThemeParkIncText.NewPlayer );
		var letter = ThemeParkIncLetterWindow.Compose( mail, tags, addressee, ThemeParkIncLetterDate( strings, economy ) );
		hud.ShowLetter( letter, () => DismissThemeParkIncLetter( economy, mail ) );
	}

	/// <summary>
	/// [BIN:TPI-EXE:0x526960 letter dismissed] Closing a letter of category 0x5E (the bank's) or 0x5F (the challenge system's) runs 0x649E60
	/// with reason 0 or 1: the park is lost.
	/// </summary>
	private static void DismissThemeParkIncLetter( ParkEconomy economy, ThemeParkIncAdviceMail mail ) => economy.LoseThemeParkIncPark( mail.LossReason );

	/// <summary>
	/// [BIN:TPI-EXE:0x635E05 letter date] The tag system dates the record when it builds it: the clock (world +0x428) turns the park tick into
	/// FILETIME units, its start (2000-01-01) plus tick × 15000 / 4 seconds (0x6089C0), which FileTimeToSystemTime splits into year, month
	/// and day (0x7D91E0); 0x518BC0 packs them and [BIN:TPI-EXE:0x518C30 letter date] the window prints UITEXT 956, "{0x29}.{0x2A}.{0x2B}",
	/// with the day (0x29), the month (0x2A) and the year (0x2B). That is the shared calendar's date of the park turn.
	/// </summary>
	private static string ThemeParkIncLetterDate( UiStringTable strings, ParkEconomy economy )
	{
		var date = ParkCalendar.DateAtTurn( economy.Turn );
		var culture = System.Globalization.CultureInfo.InvariantCulture;
		return strings.FormatByType( ThemeParkIncLetterWindow.DateTemplate, new Dictionary<int, string>
		{
			[0x29] = date.Day.ToString( culture ),
			[0x2A] = date.Month.ToString( culture ),
			[0x2B] = date.Year.ToString( culture )
		} );
	}

	/// <summary>Looks an advice text up in <c>TAG_SYSTEM.str</c> and puts it on the advice line.</summary>
	private static void ShowThemeParkIncAdviceCaption( ThemeParkIncHud hud, int textEntry, string kind )
	{
		try
		{
			var strings = GameLanguage.Current.LoadStrings( "TAG_SYSTEM.str" );
			if ( OpenTPW.Hud.ParkHud.AdvisorCaptionText( strings, textEntry ) is { } text )
				hud.ShowAdvisorCaption( text );
			else
				Log.Warning( $"TAG_SYSTEM.str has no entry {textEntry} for the Theme Park Inc {kind}." );
		}
		catch ( Exception exception ) when ( exception is IOException or InvalidDataException or UnauthorizedAccessException )
		{
			Log.Warning( $"TAG_SYSTEM.str is not available for a Theme Park Inc {kind}: {exception.Message}" );
		}
	}

	/// <summary>
	/// [BIN:TPI-EXE:0x718190 forecast playback] The advisor's update on the scaled clock (<see cref="SceneClock"/>): the queue
	/// picks a due forecast, the original speech event plays from the global catalogue, and the tag's <c>TAG_SYSTEM.str</c>
	/// entry becomes the caption. The voice's own length is the window the original waits before the next advice.
	/// </summary>
	private void PresentThemeParkIncForecast( Level level, ThemeParkIncHud hud, ParkEconomy economy, ThemeParkIncForecastQueue forecast )
	{
		if ( level.SceneClock.Milliseconds is var now && forecast.Tick( now, economy.TpiForecastWorldTime, economy.TpiForecastEligible, Present ) is not { } )
			return;

		uint Present( ThemeParkIncForecastLine line )
		{
			themeParkIncForecastVoice?.Stop();
			themeParkIncForecastVoice = GameAudio.PlaySpeechEffect( line.SpeechEvent );
			var voice = themeParkIncForecastVoice;
			// [BIN:TPI-EXE:0x7167CE speech duration] the original returns the sound's length plus 1000 ms; the queue adds the 1000.
			var duration = (uint)(voice is { SampleRate: > 0 } ? voice.Frames * 1000L / voice.SampleRate : 0);
			Log.Trace( $"Theme Park Inc forecast: response {line.Response}, speech event {line.SpeechEvent}{(voice == null ? " (no voice)" : "")}, caption {line.TextEntry}, window {duration} + 1000 ms." );
			ShowThemeParkIncAdviceCaption( hud, line.TextEntry, "forecast" );
			return duration;
		}
	}
}

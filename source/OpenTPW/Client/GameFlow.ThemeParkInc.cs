using OpenTPW.FrontEnd;
using OpenTPW.UI.Original;

namespace OpenTPW;

/// <summary>
/// The experimental Theme Park Inc slice of the game flow (docs/THEME-PARK-INC.md): a Theme Park Inc world runs
/// with the shared terrain, object, path and guest systems and the experimental HUD, under the edition capability
	/// policy — money and the cleaners (no other staff, research or challenges), no advisor beyond the four traced weather forecasts, and no sharing. The desktop Science park can continue an OpenTPW save; layout checkpoints start fresh sessions.
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
			EnterWorld = zone => QueueLevel( zone, () => EnterThemeParkIncZone( zone ) ),
			ContinueScience = !OperatingSystem.IsBrowser() && File.Exists( IncScienceSavePath() ) ? QueueScienceContinuation : null,
			Quit = Quit
		}, UiFonts.CachedLoader( GameLanguage.Current ) );
		Log.Trace( "Theme Park Inc front end: the Personnel File (experimental slice; Enter Game starts the chosen zone)." );
	}

	/// <summary>Enter Game: the zone's park starts with the player's career (the player file's, or a new one).</summary>
	// [BIN:TPI-EXE:0x56AB40 enter game] the panel selects the player, which loads the player's global save, and changes to the zone
	private void EnterThemeParkIncZone( string zone )
	{
		var frontEnd = IncFrontEnd;
		ThemeParkIncCareer? career;
		try { career = LoadThemeParkIncPlayerCareer( zone ); }
		catch ( Exception error ) when ( error is IOException or InvalidDataException or UnauthorizedAccessException or NotSupportedException )
		{
			if ( frontEnd == null )
				throw;
			frontEnd.ShowLoadFailure( error.Message );
			return;
		}
		using ( ParkEconomy.EnterThemeParkIncCareer( career ) )
			StartLevel( zone, original: true, developerPanels: false );
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
	private ThemeParkIncHud CreateThemeParkIncHud( Level level, GameSpeed speed = GameSpeed.Normal )
	{
		// a new or loaded park starts its advisor silent (0x715850 before the park file); the speech data are read now, not at the first speech
		StopThemeParkIncAdviceVoice();
		level.Park?.Economy.TpiAdvisor?.PreloadSpeech();
		return new ThemeParkIncHud( level, Strings, new ThemeParkIncHudHost
	{
		Quit = Quit,
		SaveLayout = SaveThemeParkIncLayout,
		LoadLayout = LoadThemeParkIncLayout,
		SavePark = level.LevelName == "science" && !OperatingSystem.IsBrowser() ? SaveThemeParkIncScience : null,
		LoadPark = level.LevelName == "science" && !OperatingSystem.IsBrowser() ? LoadThemeParkIncScience : null,
		SelectNewPlayer = ShowThemeParkIncFrontEnd
	}, speed );
	}

	/// <summary>The advisor model of the running Theme Park Inc park (null outside the slice and before its first frame), made for its level.</summary>
	internal ThemeParkIncAdvisorView? ThemeParkIncAdvisorView { get; private set; }
	private Level? themeParkIncAdvisorViewLevel;

	/// <summary>
	/// World pass: draws the Theme Park Inc advisor's model (<see cref="OpenTPW.ThemeParkIncAdvisorView"/>) as the drawn frame's point of the running world tick shows it. It only reads the
	/// park's advisor: the simulation and its hash are the same with or without it.
	/// </summary>
	private void RenderThemeParkIncAdvisor( Level level )
	{
		if ( IncHud == null || level.Park?.Economy is not { TpiAdvisor: { } advisor } economy )
		{
			EndThemeParkIncAdvisorView();
			return;
		}
		// a replaced park (a layout or a Science save loaded) gets a view of its own; the old one's models are released
		if ( !ReferenceEquals( themeParkIncAdvisorViewLevel, level ) )
		{
			EndThemeParkIncAdvisorView();
			ThemeParkIncAdvisorView = new ThemeParkIncAdvisorView( level.LevelName );
			themeParkIncAdvisorViewLevel = level;
		}
		ThemeParkIncAdvisorView!.Render( advisor, ParkCalendar.TurnFraction( economy.Tick ), Screen.PixelSize );
	}

	/// <summary>Releases the Theme Park Inc advisor's models and image (the park goes).</summary>
	internal void EndThemeParkIncAdvisorView()
	{
		ThemeParkIncAdvisorView?.Dispose();
		ThemeParkIncAdvisorView = null;
		themeParkIncAdvisorViewLevel = null;
	}

	/// <summary>Stops the advice voice that is playing (a global speech one-shot outlives <see cref="GameAudio.LeavePark"/>).</summary>
	internal void StopThemeParkIncAdviceVoice()
	{
		themeParkIncAdviceVoice?.Stop();
		themeParkIncAdviceVoice = null;
	}

	/// <summary>The slice's simulation time scale: a flight's clock rate while one is on (0 while the park is paused), else the HUD's speed.</summary>
	internal static float ThemeParkIncTimeScale( double? flightRate, GameSpeed speed, float hudScale ) =>
		flightRate is { } rate ? speed == GameSpeed.Paused ? 0f : (float)rate : hudScale;

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
		// [BIN:TPI-EXE:0x5D053C flight clock] a camera flight sets the park clock's rate to its mode's (1.0, 0.7 in mode 1) until its end restores the speed word's rate; 0x41C000 writes the rate (+0x20)
		// only, and the pause is the clock's own flag (+0x30, 0x41BBF0), so a paused park stays paused under a flight until the pause key (reachable during the flight, ThemeParkIncHud.UpdateFlight) resumes it
		level.SimulationTimeScale = ThemeParkIncTimeScale( level.Park?.Economy.TpiCutscene?.ClockRate, hud.Speed, hud.TimeScale );
		// the advisor clock's rate is the player's speed while the park runs (a pause runs no world tick, so it keeps the last one)
		if ( level.Park?.Economy is { } rated && hud.Speed != GameSpeed.Paused )
			rated.TpiSpeedRate = GameSpeedScale.Of( hud.Speed );
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
			// a mail that comes while a letter is open waits for it: what the tag system does with a second notification then is not read (docs/reverse/TPI-BANK-MAIL.md)
			if ( !hud.LetterOpen && tpiEconomy.TakeTpiMail() is { } mail )
				PresentThemeParkIncMail( hud, tpiEconomy, mail );
			// [BIN:TPI-EXE:0x628D93 wage demand advice] the daily routine posts advisor message 0x1C8 with each wage demand; [BIN:TPI-EXE:0x7176E0 strike messages] the HQ's and the strikers' type-0x17
			// messages reach only the advisor's listener. [EXT:tpi-capability-gates] neither is posted (the staff senders come with P5): they are noted
			foreach ( var advice in tpiEconomy.TakeThemeParkIncStaffAdvice() )
				Log.Trace( $"Theme Park Inc staff advice {advice:X} is not said: the advisor's message path is not ported." );
			foreach ( var message in tpiEconomy.TakeThemeParkIncStrikeMessages() )
				Log.Trace( $"Theme Park Inc strike message (role {message.Role}, kind {message.Kind}, reason {message.Reason}) is not said: the advisor's message path is not ported." );
			foreach ( var advice in tpiEconomy.TakeThemeParkIncAdvice() )
				Log.Trace( $"Theme Park Inc advice {advice.Message:X}: response {advice.Response}." );
			if ( tpiEconomy.TpiAdvisor is { } tpiAdvisor )
				foreach ( var cue in tpiAdvisor.Presentation.TakeCues() )
					PresentThemeParkIncAdvisorCue( cue );
		}
		ReverbSelector.Update( level.RideView?.Ride );
	}

	/// <summary>The voice the last advice started; stopped when the park goes (a global speech one-shot outlives LeavePark).</summary>
	private AudioVoice? themeParkIncAdviceVoice;

	/// <summary>
	/// [BIN:TPI-EXE:0x635D80 notification letter] A notification letter (the bank's six-months-in-the-red letter or the challenge system's
	/// firing letter): the tag system queues its advice, which the advisor's playback speaks (<see cref="PresentThemeParkIncAdvisorCue"/>), and opens the
	/// letter window on the record (0x636217). Closing the letter is what loses the park (<see cref="DismissThemeParkIncLetter"/>).
	/// </summary>
	private void PresentThemeParkIncMail( ThemeParkIncHud hud, ParkEconomy economy, ThemeParkIncAdviceMail mail )
	{
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
	private static string ThemeParkIncLetterDate( UiStringTable strings, ParkEconomy economy ) =>
		ThemeParkIncLetterWindow.FormatDate( strings, ThemeParkIncTagLetter.PackedDate( economy.Turn ) );

	/// <summary>
	/// [BIN:TPI-EXE:0x716230 advisor speech] The simulation's advisor presentation (<see cref="ThemeParkIncAdvisorPresentation"/>, on the advisor clock) asks for a voice, a stop sound or the
	/// canned timer: the voice is the response's event in the global <c>cat_speech</c> catalogue, which the speech plays at once or the per-frame advisor 1000 ms after the base (a sequence that
	/// opens with clip 14); the stop sound ends a voice the cancel stopped. The caption is a Message Centre row the simulation files (<see cref="ThemeParkIncTagTube"/>).
	/// </summary>
	private void PresentThemeParkIncAdvisorCue( ThemeParkIncAdvisorCue cue )
	{
		switch ( cue.Kind )
		{
			case ThemeParkIncAdvisorCueKind.Voice:
				themeParkIncAdviceVoice?.Stop();
				themeParkIncAdviceVoice = GameAudio.PlaySpeechEffect( cue.Sound );
				Log.Trace( $"Theme Park Inc advisor voice: response {cue.Response}, speech event {cue.Sound}{(themeParkIncAdviceVoice == null ? " (no voice)" : "")}." );
				break;
			case ThemeParkIncAdvisorCueKind.Stop:
				// [BIN:TPI-EXE:0x716A2C advisor stop sound] the stopped voice ends with 0x46E, 0x46F or 0x470 of the global speech bank (0xAE012C) behind a shown head
				themeParkIncAdviceVoice?.Stop();
				themeParkIncAdviceVoice = null;
				if ( cue.Sound != 0 )
					GameAudio.PlaySpeechEffect( cue.Sound );
				break;
			case ThemeParkIncAdvisorCueKind.Timer:
				// [BIN:TPI-EXE:0x716FF3 advisor timer] effect 0x57 and sound 0xC6 of the bank 0xAE0124: only the canned rows 17 and 18 (animations 33 and 34) have a timer, which no ported response uses
				Log.Trace( $"Theme Park Inc advisor timer of response {cue.Response}." );
				break;
			default:
				Log.Trace( $"Theme Park Inc advisor speech: response {cue.Response}, voice {cue.Sound}." );
				break;
		}
	}
}

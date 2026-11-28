namespace OpenTPW;

/// <summary>
/// What a Theme Park Inc window asks of the advisor thing (<c>0x64EA00</c>): its help said after the panel delay when it opens (<c>0x503AD0</c>), its help button (<c>0x503A40</c>), its help
/// cancelled when it goes (<c>0x503B40</c>), and the window's own advice posted (<c>0x71A930</c>, <c>0x718820</c>) or cancelled (<c>0x718990</c>).
/// </summary>
// [EXT:tpi-advisor-tick-clock] (D-2: window state is simulation input) the client's windows call it between world ticks, as the original's window procedures run between them
public interface IThemeParkIncWindowAdvice
{
	void DelayedHelp( int message );
	bool HelpButton( int message );
	bool CancelHelp( int message );
	bool Post( int message );
	bool Cancel( int message );
	/// <summary>The mission status refresh (<c>0x54D660</c>) a window calls: its requirements-met advice.</summary>
	void MissionStatus() { }
}

public sealed partial class ParkEconomy
{
	/// <summary>The advisor as Theme Park Inc's windows reach it (<see cref="IThemeParkIncWindowAdvice"/>); every call does nothing in a park without an advisor.</summary>
	public IThemeParkIncWindowAdvice TpiWindowAdvice => tpiWindowAdvice ??= new ThemeParkIncWindowAdvice( this );
	private IThemeParkIncWindowAdvice? tpiWindowAdvice;

	private sealed class ThemeParkIncWindowAdvice( ParkEconomy economy ) : IThemeParkIncWindowAdvice
	{
		public void DelayedHelp( int message ) => economy.ThemeParkIncDelayedHelp( message );
		public bool HelpButton( int message ) => economy.ThemeParkIncHelpButton( message );
		public bool CancelHelp( int message ) => economy.CancelThemeParkIncHelp( message );
		public bool Post( int message ) => economy.PostThemeParkIncAdvice( message );
		public bool Cancel( int message ) => economy.CancelThemeParkIncAdvice( message );
		public void MissionStatus() => economy.ThemeParkIncMissionStatusAdvice();
	}

	/// <summary>
	/// Theme Park Inc's advisor thing (docs/reverse/TPI-ADVISOR.md): the single queue every advice goes through (the forecasts and the notification letters so far), updated once per world
	/// tick at its place in the thing pass. Null in Theme Park World.
	/// </summary>
	// [BIN:TPI-EXE:0x64836F advisor thing] the new-world init builds the advisor (model 11) before the tag system (0x6483B7); its scorer loads Advisor.sam on first use (0x71B6B0)
	public ThemeParkIncAdvisor? TpiAdvisor { get; private set; }


	/// <summary>
	/// The park clock's rate the player chose (<see cref="GameSpeedScale"/>: 1, 2 or 4), which the client sets while the park runs; a camera flight's rate replaces it
	/// (<see cref="TpiAdvisorClockRate"/>). It decides how much real time a world tick spans on the advisor clock.
	/// </summary>
	// [EXT:speed-buttons] OpenTPW's speeds are 1, 2 and 4, where the original steps its clock rate by 0.8 and 1.25 within 0.25..2
	internal double TpiSpeedRate { get; set; } = 1;

	/// <summary>The park clock's rate this tick: a playing camera flight's (<see cref="ThemeParkIncCutscene.ClockRate"/>), else the player's.</summary>
	// [BIN:TPI-EXE:0x5D053C flight clock] the flight sets the park clock's rate to its mode's until its end restores the speed word's rate
	internal double TpiAdvisorClockRate => TpiCutscene?.ClockRate ?? TpiSpeedRate;

	private void CreateThemeParkIncAdvisor( ulong seed )
	{
		if ( Rules.ThemeParkInc )
			TpiAdvisor = new ThemeParkIncAdvisor( ThemeParkIncAdvisorSettings.Load(), ThemeParkIncAdvisorTable.Shipped, new ThemeParkIncAdvisorSpeech( Settings.Theme ),
				PresentationSeed( seed ) ) { World = new ThemeParkIncAdviceWorld( this ) };
	}

	/// <summary>
	/// The presentation stream's seed from the economy's stream seed (D-4: the session seed): its two halves XORed (the default world seed gives 1). The original seeds the C runtime from the tick count at startup (srand(GetTickCount()), 0x6A1514), which no run can repeat.
	/// </summary>
	// [EXT:tpi-advisor-presentation-rand] (user-approved 2026-11-24; D-4, lead 2026-11-25) the stream is seeded from the session seed, saved and hashed
	internal static uint PresentationSeed( ulong seed ) => unchecked( (uint)(seed ^ (seed >> 32)) );

	/// <summary>
	/// The advice context of the class check: the career game's mode word 0, and the cutscene thing playing with its mode. The game object's bytes <c>+0x2E</c> and <c>+0x2F</c> are not
	/// modelled and read as clear, which a running park's world has.
	/// </summary>
	// [BIN:TPI-EXE:0x71BDC0 class check] [BIN:TPI-EXE:0x5D0770 animation word] the cutscene's 0xA9D230 and its +0x24 (ThemeParkIncCutscene.AnimThing, Mode); the mode word 0xC65B80 is 0 in the
	// single-player career OpenTPW runs (ThemeParkIncChallenges)
	internal ThemeParkIncAdviceContext TpiAdviceContext => new( 0, false, false, TpiCutscene?.AnimThing ?? 0, TpiCutscene?.Mode ?? 0 );

	/// <summary>
	/// Posts advice <paramref name="message"/> with its descriptor's score and the locate word and dword its caption carries, as a sender's <c>0x71A930</c> and <c>0x718820</c> do; false when the
	/// queue refused it.
	/// </summary>
	internal bool PostThemeParkIncAdvice( int message, ushort locate = 0, int locateData = 0 ) =>
		TpiAdvisor is { } advisor && TpiCareer is { } career && advisor.Post( message, career, Turn, TpiAdviceContext, locate, locateData );

	/// <summary>Cancels advice <paramref name="message"/> (<c>0x718990</c> on the advisor thing): its pending records go and a playing one stops; false when nothing was cancelled.</summary>
	internal bool CancelThemeParkIncAdvice( int message ) => TpiAdvisor is { } advisor && TpiCareer is { } career && advisor.Cancel( message, career, out _ );

	/// <summary>Whether the player's history has advice <paramref name="message"/> played (<c>0x71B520</c>, the entry's byte <c>+8</c>); false without a career.</summary>
	internal bool ThemeParkIncAdvicePlayed( int message ) => TpiCareer?.AdviceHistory( message ).Played ?? false;

	/// <summary>A window's help button (<c>0x503A40</c>, <see cref="ThemeParkIncAdvisor.HelpButton"/>); false when the queue refused it or the park has no advisor.</summary>
	// [EXT:tpi-advisor-tick-clock] (D-2: window state is simulation input) the client's window calls it between world ticks, as the original's window procedure runs between them
	public bool ThemeParkIncHelpButton( int message ) =>
		TpiAdvisor is { } advisor && TpiCareer is { } career && advisor.HelpButton( message, career, Turn, TpiAdviceContext );

	/// <summary>A window's help said after the panel delay (<c>0x503AD0</c>, <see cref="ThemeParkIncAdvisor.PostDelayed"/>).</summary>
	public void ThemeParkIncDelayedHelp( int message ) => TpiAdvisor?.PostDelayed( message );

	/// <summary>A window's or a panel's help said at once (<c>0x5039F0</c>, <see cref="ThemeParkIncAdvisor.PanelHelp"/>).</summary>
	public bool ThemeParkIncPanelHelp( int message ) =>
		TpiAdvisor is { } advisor && TpiCareer is { } career && advisor.PanelHelp( message, career, Turn, TpiAdviceContext );

	/// <summary>A window's help cancelled when the window goes (<c>0x503B40</c>, <see cref="ThemeParkIncAdvisor.CancelHelp"/>).</summary>
	public bool CancelThemeParkIncHelp( int message ) => TpiAdvisor is { } advisor && TpiCareer is { } career && advisor.CancelHelp( message, career );

	/// <summary>The stored tutorial advice said again (<c>0x718AB0</c>: the F1 key, <see cref="ThemeParkIncAdvisor.RepeatTutorial"/>); false with none stored.</summary>
	public bool RepeatThemeParkIncTutorial() => TpiAdvisor is { } advisor && TpiCareer is { } career && advisor.RepeatTutorial( career, Turn );

	/// <summary>The advisor thing's update (<c>0x718190</c>), once per world pass and not in a lost park.</summary>
	// [BIN:TPI-EXE:0x6376C7 advisor update] the thing pass's class switch sends model 11 to 0x637704 -> 0x718190 (skipped while the mode word is 1, an online game)
	internal void UpdateThemeParkIncAdvisor( long turn )
	{
		if ( TpiAdvisor is not { } advisor || TpiCareer is not { } career )
			return;
		advisor.Update( turn, career, TpiAdviceContext );
		foreach ( var broadcast in advisor.TakeBroadcasts() )
			FileThemeParkIncAdviceRow( broadcast );
	}

	// [BIN:TPI-EXE:0x7191AB easy-mode history reset] the advisor's handler clears every history entry but its time stamp (variant -1, played, slaps and plays 0) on RESET_FOR_EASY_MODE
	// ("Clearing advisor history afer receiving RESET_FOR_EASY_MODE event"). [EXT:tpi-capability-gates] Theme Park Inc's easy mode is not ported (an easy Theme Park Inc save is refused), so
	// nothing asks for it. [BIN:TPI-EXE:0x7177B0 prebuilt history reset] the same reset runs from the world reset 0x64B940 when the prebuilt park loads (0x7468B0 "PreBuilt\", 0x6A3CF9), the first
	// world of a new career: OpenTPW's new career builds its history from the constructor's values (ThemeParkIncCareer), which that reset leaves as they are, so it is not repeated

	/// <summary>The advice the advisor played since the last call, oldest first, for the client to voice and caption.</summary>
	internal IReadOnlyList<ThemeParkIncAdvicePlay> TakeThemeParkIncAdvice() => TpiAdvisor?.TakePlayed() ?? Array.Empty<ThemeParkIncAdvicePlay>();
}

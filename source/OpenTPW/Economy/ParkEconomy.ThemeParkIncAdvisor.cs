namespace OpenTPW;

public sealed partial class ParkEconomy
{
	/// <summary>
	/// Theme Park Inc's advisor thing (docs/reverse/TPI-ADVISOR.md): the single queue every advice goes through (the forecasts and the notification letters so far), updated once per world
	/// tick at its place in the thing pass. Null in Theme Park World.
	/// </summary>
	// [BIN:TPI-EXE:0x64836F advisor thing] the new-world init builds the advisor (model 11) before the tag system (0x6483B7); its scorer loads Advisor.sam on first use (0x71B6B0)
	public ThemeParkIncAdvisor? TpiAdvisor { get; private set; }

	private readonly ThemeParkIncAdvisorSpeech tpiAdvisorSpeech = new();

	private void CreateThemeParkIncAdvisor()
	{
		if ( Rules.ThemeParkInc )
			TpiAdvisor = new ThemeParkIncAdvisor( ThemeParkIncAdvisorSettings.Load(), ThemeParkIncAdvisorTable.Shipped );
	}

	/// <summary>
	/// The advice context of the class check: the career game's mode word 0, and the cutscene thing playing with its mode. The game object's bytes <c>+0x2E</c> and <c>+0x2F</c> are not
	/// modelled and read as clear, which a running park's world has.
	/// </summary>
	// [BIN:TPI-EXE:0x71BDC0 class check] [BIN:TPI-EXE:0x5D0770 animation word] the cutscene's 0xA9D230 and its +0x24 (ThemeParkIncCutscene.AnimThing, Mode); the mode word 0xC65B80 is 0 in the
	// single-player career OpenTPW runs (ThemeParkIncChallenges)
	internal ThemeParkIncAdviceContext TpiAdviceContext => new( 0, false, false, TpiCutscene?.AnimThing ?? 0, TpiCutscene?.Mode ?? 0 );

	/// <summary>Posts advice <paramref name="message"/> with its descriptor's score, as a sender's <c>0x71A930</c> and <c>0x718820</c> do; false when the queue refused it.</summary>
	internal bool PostThemeParkIncAdvice( int message ) =>
		TpiAdvisor is { } advisor && TpiCareer is { } career && advisor.Post( message, career, Turn, TpiAdviceContext );

	/// <summary>The advisor thing's update (<c>0x718190</c>), once per world pass and not in a lost park.</summary>
	// [BIN:TPI-EXE:0x6376C7 advisor update] the thing pass's class switch sends model 11 to 0x637704 -> 0x718190 (skipped while the mode word is 1, an online game)
	internal void UpdateThemeParkIncAdvisor( long turn )
	{
		if ( TpiAdvisor is { } advisor && TpiCareer is { } career )
			advisor.Update( turn, career, TpiAdviceContext, tpiAdvisorSpeech.Duration );
	}

	/// <summary>The advice the advisor played since the last call, oldest first, for the client to voice and caption.</summary>
	internal IReadOnlyList<ThemeParkIncAdvicePlay> TakeThemeParkIncAdvice() => TpiAdvisor?.TakePlayed() ?? Array.Empty<ThemeParkIncAdvicePlay>();
}

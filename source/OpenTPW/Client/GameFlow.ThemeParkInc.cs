using OpenTPW.FrontEnd;
using OpenTPW.UI.Original;

namespace OpenTPW;

/// <summary>
/// The experimental Theme Park Inc slice of the game flow (docs/THEME-PARK-INC.md): a Theme Park Inc world runs
/// with the shared terrain, object, path and guest systems and the experimental HUD, under the edition capability
	/// policy — no economy, staff, advisor, front end, continuation saves or sharing. Layout checkpoints start fresh sessions. Everything here is an OpenTPW extension, not
/// traced Theme Park Inc behaviour.
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
			Quit = Quit
		}, UiFonts.CachedLoader( GameLanguage.Current ) );
		Log.Trace( "Theme Park Inc front end: the Personnel File (experimental slice; Enter Game starts the chosen zone)." );
	}

	/// <summary>Starts the experimental HUD over a Theme Park Inc level (called from StartLevel, after TearDown).</summary>
	private ThemeParkIncHud CreateThemeParkIncHud( Level level ) => new( level, Strings, new ThemeParkIncHudHost
	{
		Quit = Quit,
		SaveLayout = SaveThemeParkIncLayout,
		LoadLayout = LoadThemeParkIncLayout
	} );

	/// <summary>
	/// One frame of the slice: the experimental HUD takes the input, the level runs under its speed (0 while paused or a
	/// modal menu is open) and the crowd audio follows the park's guests. Nothing else of the reference game's frame
	/// (advisor, staff voices, weather sounds) runs: those systems need the economy the slice refuses.
	/// </summary>
	// [EXT:tpi-twp-guest-rules] the guests and scripts that run here are the ported Theme Park World rules over Theme Park Inc data (an OpenTPW scope decision); no Theme Park Inc guest behaviour is claimed
	private void UpdateThemeParkIncSlice( Level level, ThemeParkIncHud hud, UiInput input )
	{
		var captured = hud.Update( Context, input );
		// A confirmed layout load can replace the world inside HUD input; do not tick the retired level.
		if (!ReferenceEquals(Level, level)) return;
		level.UiCapturesMouse = captured;
		level.SimulationTimeScale = hud.TimeScale;
		level.Update();
		GameAudio.Update( level.Guests?.GetStatistics().InPark ?? 0 );
	}
}

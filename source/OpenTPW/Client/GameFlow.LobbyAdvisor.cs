using OpenTPW.FrontEnd;
using OpenTPW.UI.Original;

namespace OpenTPW;

internal sealed partial class GameFlow
{
	private LobbyAdvisor? lobbyAdvisor;

	/// <summary>The lobby's advisor, null outside the front-end state (smoke instrumentation and tests).</summary>
	internal LobbyAdvisor? LobbyAdvisorHost => lobbyAdvisor;

	// [BIN:TPW-EXE:0x5F5FA0 queue timer] the timer the lobby queue's idle delay is measured on counts milliseconds from the program's start; the advisor's own clock is the same here (the lobby never pauses it)
	private static long LobbyAdvisorMilliseconds() => (long)(Time.Now * 1000.0);

	/// <summary>Sets the front end's advisor up (state 1 of the front-end state, 0x54E5EC); none with the automatic advisor off (--no-advisor, the manual advisor tools).</summary>
	private void StartLobbyAdvisor()
	{
		EndLobbyAdvisor();
		if ( !AutomaticAdvisorEnabled || GameEditions.IsThemeParkInc || global::Global.Render == null )
			return;
		// the advisor itself is made when the first line is said (LobbyAdvisor), as the park's presentation is
		lobbyAdvisor = new LobbyAdvisor( () => new Advisor( LobbyAdvisorMilliseconds ), LobbyAdvisorMilliseconds );
	}

	/// <summary>The front end's frame (0x599880 and the lobby's tasks, 0x54E6DF): the screen decides the ambient, not whether the advisor runs.</summary>
	private void UpdateLobbyAdvisor() =>
		lobbyAdvisor?.Update( backdrop == Backdrop.Lobby ? LobbyAdvisorScreen.Islands : LobbyAdvisorScreen.Globe );

	private void RenderLobbyAdvisor() => lobbyAdvisor?.Render();

	private void DrawLobbyAdvisor()
	{
		if ( Level == null && Hud == null && IncHud == null && IncFrontEnd == null )
			lobbyAdvisor?.Draw( Context );
	}

	/// <summary>Tears the front end's advisor down (state 3, 0x54E7F5): the queue cuts a response that plays off, then the advisor goes.</summary>
	private void EndLobbyAdvisor()
	{
		lobbyAdvisor?.Dispose();
		lobbyAdvisor = null;
	}
}

using System.Diagnostics;
using OpenTPW.FrontEnd;

namespace OpenTPW;

internal sealed partial class GameFlow
{
	/// <summary>The lobby's frames run steps of <see cref="PumpLobbyCodecWarmup"/> until at least this much of a frame has gone.</summary>
	internal const double LobbyWarmupBudgetMilliseconds = 4;

	private IEnumerator<bool>? lobbyCodecWarmup;
	private bool lobbyCodecWarmupDone;

	/// <summary>
	/// The first click on the lobby's enter-park button reads the island's saved parks to offer them (<see cref="FindContinueEntry"/>,
	/// <see cref="FindAutomaticContinueEntry"/>). The first read of a session made the world-save codec's reflection metadata at once:
	/// about 0.4 s in the browser, between the click and the screen it opens (docs/BROWSER-PERFORMANCE.md, "The lobby's enter-park
	/// button"). When a saved park exists, the lobby's idle frames make that metadata in steps instead (<see cref="ParkResumeJson.WarmupSteps"/>):
	/// nothing is read or written, the codec's output does not change, and a click before the steps end makes the rest itself, as before.
	/// </summary>
	private void PumpLobbyCodecWarmup()
	{
		if ( lobbyCodecWarmupDone || Menu == null || LobbyEntering || pending != null || parkBuild != null )
			return;
		if ( lobbyCodecWarmup == null )
		{
			if ( !HasSavedWorld() )
			{
				// a session without saved parks offers none; its first park's restart snapshot makes the metadata in that park's build steps
				lobbyCodecWarmupDone = true;
				return;
			}
			lobbyCodecWarmup = ParkResumeJson.WarmupSteps().GetEnumerator();
		}
		var started = Stopwatch.GetTimestamp();
		while ( Stopwatch.GetElapsedTime( started ).TotalMilliseconds < LobbyWarmupBudgetMilliseconds )
		{
			if ( lobbyCodecWarmup.MoveNext() )
				continue;
			lobbyCodecWarmup.Dispose();
			lobbyCodecWarmup = null;
			lobbyCodecWarmupDone = true;
			return;
		}
	}

	/// <summary>Whether any theme has an own world save, an Exit To Lobby checkpoint or an autosave (the files the theme start offers).</summary>
	private bool HasSavedWorld()
	{
		if ( GameEditions.Current != GameEdition.ThemeParkWorld || !GameEditions.Capabilities.ParkSaves )
			return false;
		try
		{
			return ObjectCatalog.Themes.Any( theme => File.Exists( SaveFileSystem.GetAbsolutePath( $"opentpw-{theme}-world.json" ) )
				|| File.Exists( AutomaticThemeContinuation.Slot( SaveFileSystem, theme, AutomaticSlotKind.Checkpoint ) )
				|| File.Exists( AutomaticThemeContinuation.Slot( SaveFileSystem, theme, AutomaticSlotKind.Autosave ) ) );
		}
		catch ( Exception error ) when ( error is IOException or UnauthorizedAccessException or ArgumentException or InvalidDataException )
		{
			return false;
		}
	}
}

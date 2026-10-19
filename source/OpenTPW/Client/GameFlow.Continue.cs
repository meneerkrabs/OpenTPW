using OpenTPW.FrontEnd;
using OpenTPW.UI.Original;

namespace OpenTPW;

internal sealed partial class GameFlow
{
	internal ParkContinueEntry? FindContinueEntry( string theme ) => ThemeContinuation.Probe( SaveFileSystem, theme );

	internal void ContinuePark( ParkContinueEntry entry ) => TryContinuePark( entry, snapshot => ResumePark( snapshot, extensionSwitch: entry.Park.Switch ) );

	/// <summary>The same activation path for the real staged resume and asset-free continuation integration tests.</summary>
	internal bool TryContinuePark( ParkContinueEntry entry, Action<ParkResumeSnapshot> resume )
	{
		try
		{
			resume( ThemeContinuation.Read( SaveFileSystem, entry ) );
			return true;
		}
		catch ( Exception error ) when ( error is IOException or InvalidDataException or UnauthorizedAccessException or System.Text.Json.JsonException
			or ArgumentException or InvalidOperationException or NotSupportedException )
		{
			Log.Warning( $"Park continuation failed: {error.Message}" );
			var stack = Hud?.Stack ?? Menu?.Stack;
			stack?.Push( UiDialogs.Message( "continueFailed", () => error.Message,
				(() => Strings.Extra( OpenTpwText.Back ), () => stack.Pop()) ) );
			return false;
		}
	}
}

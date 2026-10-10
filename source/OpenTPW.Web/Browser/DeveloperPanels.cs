using OpenTPW.Online;

namespace OpenTPW.UI;

// The ImGui developer panels need the native ImGui library, which the browser build does not have
// (docs/WEB.md). Level creates them only when developer panels are on; these keep it compiling.

public sealed class ParkLayout
{
	public ParkLayout( Level level ) { }
	public void Draw() { }
}

public sealed class OnlinePanel : IDisposable
{
	public OnlinePanel( Level level, OnlineFolders folders ) { }
	public void Draw() { }
	public void Dispose() { }
}

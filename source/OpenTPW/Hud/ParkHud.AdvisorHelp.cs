namespace OpenTPW.Hud;

public sealed partial class ParkHud
{
	// The screens (other than the HUD itself) the host has been told about, by name.
	private readonly HashSet<string> reportedScreens = new( StringComparer.Ordinal );

	/// <summary>
	/// Tells the host which screens opened and closed since the last frame: the advisor posts a window's help when it opens and
	/// withdraws it when it closes (<see cref="AdvisorUiHelp"/>), and any screen taking over is an interface mode change.
	/// </summary>
	// [BIN:TPW-EXE:0x00486B00 advise] the original's window builders post their help as they open (0x486B00) and their close handlers withdraw it (0x486B40)
	private void ReportScreens()
	{
		if ( host?.WindowOpened == null && host?.WindowClosed == null )
			return;
		var open = new HashSet<string>( Stack.Screens.Where( screen => screen != hud ).Select( screen => screen.Name ), StringComparer.Ordinal );
		foreach ( var closed in reportedScreens.Where( name => !open.Contains( name ) ).ToArray() )
		{
			reportedScreens.Remove( closed );
			host.WindowClosed?.Invoke( closed );
		}
		foreach ( var opened in open.Where( name => !reportedScreens.Contains( name ) ).ToArray() )
		{
			reportedScreens.Add( opened );
			host.WindowOpened?.Invoke( opened );
		}
	}
}

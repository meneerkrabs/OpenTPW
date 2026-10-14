namespace OpenTPW.Hud;

public sealed partial class ParkHud
{
	// The screens (other than the HUD itself) the host has been told about, by name; reused every frame.
	private readonly HashSet<string> reportedScreens = new( StringComparer.Ordinal );
	private readonly HashSet<string> openScreens = new( StringComparer.Ordinal );
	private readonly List<string> screenChanges = new();

	/// <summary>
	/// Tells the host which screens opened and closed since the last frame: the advisor posts a window's help when it opens and
	/// withdraws it when it closes (<see cref="AdvisorUiHelp"/>), and any screen taking over is an interface mode change.
	/// </summary>
	// [BIN:TPW-EXE:0x00486B00 advise] the original's window builders post their help as they open (0x486B00) and their close handlers withdraw it (0x486B40)
	private void ReportScreens()
	{
		if ( host?.WindowOpened == null && host?.WindowClosed == null )
			return;
		openScreens.Clear();
		var screens = Stack.Screens;
		for ( var index = 0; index < screens.Count; index++ )
			if ( screens[index] != hud )
				openScreens.Add( screens[index].Name );
		if ( openScreens.SetEquals( reportedScreens ) )
			return;
		screenChanges.Clear();
		foreach ( var name in reportedScreens )
			if ( !openScreens.Contains( name ) )
				screenChanges.Add( name );
		foreach ( var closed in screenChanges )
		{
			reportedScreens.Remove( closed );
			host.WindowClosed?.Invoke( closed );
		}
		screenChanges.Clear();
		foreach ( var name in openScreens )
			if ( !reportedScreens.Contains( name ) )
				screenChanges.Add( name );
		foreach ( var opened in screenChanges )
		{
			reportedScreens.Add( opened );
			host.WindowOpened?.Invoke( opened );
		}
	}
}

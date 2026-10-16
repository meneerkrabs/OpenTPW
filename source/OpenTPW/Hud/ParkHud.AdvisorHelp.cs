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
	// [BIN:TPW-EXE:0x00489F50 camcorder screen] the ride view's screen is not reported: the whole direct-call closure of its two creators (0x489F50: 424 functions, 0x48A410: 181, followed to the end) never reaches the interface mode change 0x52F200, so entering it does not withdraw mode help (calls through pointers are not followed)
	private void ReportScreens()
	{
		if ( host?.WindowOpened == null && host?.WindowClosed == null )
			return;
		openScreens.Clear();
		var screens = Stack.Screens;
		for ( var index = 0; index < screens.Count; index++ )
			if ( screens[index] != hud && screens[index] != rideScreen )
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

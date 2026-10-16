namespace OpenTPW.Hud;

public sealed partial class ParkHud
{
	// The screens (other than the HUD itself) the host has been told about, by name; reused every frame.
	private readonly HashSet<string> reportedScreens = new( StringComparer.Ordinal );
	private readonly HashSet<string> openScreens = new( StringComparer.Ordinal );
	private readonly List<string> screenChanges = new();

	/// <summary>The name under which the hire's Place tool is reported as an open window, so its advisor help is posted and withdrawn with it.</summary>
	// [BIN:TPW-EXE:0x49b9dd hire help] the hire list's click handler takes the candidate (0x507bf0), closes the window (0x658d9f, message 4) and then posts help 0x135 (309) through 0x486b00;
	// the Place tool (CMMPlaceStaff, vtable 0x6fea40) withdraws it through 0x59aa70 when it ends (slot +0x2c, 0x46c890, at 0x46c8d4), whether the member was placed or not
	public const string StaffPlacementHelp = "staffPlacement";

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
		if ( level.StaffPlacement is { Kind: StaffToolKind.Place } )
			openScreens.Add( StaffPlacementHelp );
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

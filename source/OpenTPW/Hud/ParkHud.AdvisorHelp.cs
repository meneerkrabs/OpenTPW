using OpenTPW.UI.Original;

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
			if ( screens[index] != hud && screens[index] != rideScreen && screens[index] != chatModeScreen )
				openScreens.Add( screens[index].Name );
		if ( level.StaffPlacement is { Kind: StaffToolKind.Place } )
			openScreens.Add( StaffPlacementHelp );
		if ( ObjectInfoHelpWindow() is { } objectWindow )
			openScreens.Add( objectWindow );
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

	// The help of the tools. A tool start in the original is SetMode (0x52F200), which withdraws the ten mode help messages whenever the mode changes and only
	// then posts the new tool's help (0x486B00); a help the HUD wants posted is held until the frame's mode change has been reported, so the order survives the poll.
	private List<(int Message, string Mode)>? toolHelp;
	private string? reportedMode;

	/// <summary>
	/// Asks the advisor for the help message of a tool that just started or acted; it is posted after this frame's mode change is reported, and dropped when that
	/// change ended the mode it was posted in (the original withdraws the mode help in SetMode, which then ran after the post).
	/// </summary>
	/// <param name="message">The advisor message.</param>
	/// <param name="mode">The interface mode the original posts it in, when that is not the mode now (a post that came before the click ended the tool).</param>
	private void PostToolHelp( int message, string? mode = null ) => (toolHelp ??= new()).Add( (message, mode ?? InterfaceMode()) );

	/// <summary>
	/// The interface mode as a key: which tool the park view runs, or "park". Two frames with different keys are a mode change (the original's SetMode with a new mode).
	/// </summary>
	// [BIN:TPW-EXE:0x0052F200 interface mode change] SetMode stores the new mode (0x81AE2C) after withdrawing the ten tool help messages when it differs from the stored one
	internal string InterfaceMode()
	{
		if ( level.IsCoasterToolActive )
			return "coaster";
		if ( level.CellTool.IsActive )
			return $"cell/{level.CellTool.Mode}";
		if ( level.StaffPlacement is { IsActive: true } )
			return "staffPlacement";
		if ( level.PatrolTool is { IsActive: true } )
			return "patrol";
		if ( level.BuyLandTool is { IsActive: true } )
			return "buyLand";
		if ( level.AreaClearTool is { IsActive: true } )
			return "areaClear";
		if ( level.IsRemovingObjects )
			return "remove";
		if ( level.BuildEntry != null || level.IsPlacing )
			return "build";
		return "park";
	}

	/// <summary>Reports a change of the interface mode to the advisor, then posts the tool help asked for since the last frame.</summary>
	private void ReportInterfaceMode()
	{
		var mode = InterfaceMode();
		var restoredTip = 0;
		if ( reportedMode != null && reportedMode != mode )
		{
			host?.ModeChanged?.Invoke();
			// [BIN:TPW-EXE:0x0052F4B0 queue tool start] SetMode(3) (and the variant 0x52F580, 0x52F7B2) posts 0xCB (203) after the withdrawal, for every way into the queue tool
			if ( mode == $"cell/{CellToolMode.Queue}" )
				(toolHelp ??= new()).Add( (AdvisorToolHelp.QueueTool, mode) );
			// [EXT:restored-advice-tutorial] a blueprint to place (202, rotate it with the comma and full stop keys) and the roller coaster tool (204, build track rides), after the original's own help of the mode: the advisor posts them only while the extension is on
			restoredTip = mode == "build" ? RestoredAdvice.RotateBlueprintHelp : mode == "coaster" ? RestoredAdvice.TrackRideHelp : 0;
		}
		reportedMode = mode;
		if ( toolHelp is { Count: > 0 } )
		{
			foreach ( var (message, postedIn) in toolHelp )
				if ( postedIn == mode )
					host?.HelpPosted?.Invoke( message );
			toolHelp.Clear();
		}
		if ( restoredTip != 0 )
			host?.RestoredHelp?.Invoke( restoredTip );
	}

	/// <summary>The clicks of the path and queue tool since its last first click (the original's counter 0x8186DC); the third later click posts help.</summary>
	private int cellToolClicks;

	/// <summary>
	/// The advisor help of a path or queue tool click that was not refused: the first click that sets the start posts 0xB8 (184) in the path tool, the third click after
	/// it 0xD6 (214), and a click that ends the queue tool without joining a path 0x151 (337).
	/// </summary>
	/// <param name="hadStart">The tool had a start before the click.</param>
	/// <param name="mode">The tool's mode before the click.</param>
	/// <param name="joinedPath">The queue line ends on a path cell, which links the queue to it (the original's flag 0x816D60).</param>
	/// <param name="modeBefore"><see cref="InterfaceMode"/> before the click.</param>
	// [BIN:TPW-EXE:0x00527222 first click] with the start unset a start the cell check accepts is stored (0x5272B3), and the path mode (0x52F860(1)) then posts 0xB8 through 0x59B590 (0x5272FE); the counter 0x8186DC is cleared (0x527316)
	// [BIN:TPW-EXE:0x0052732F later click] a click with a start set increments the counter and posts 0xD6 when it reaches 3 (0x52733D-0x52735A), in either mode
	// [BIN:TPW-EXE:0x005275B1 tool end] a click that ends the tool (the start reached, or the line ending on an existing path or queue cell) sets the mode to 0 (0x5275F2); in the queue mode (the check 0x52F860(3) before it) with the layout replay word 0x785A2C and the link flag 0x816D60 both clear it then posts 0x151 (0x527629)
	internal void ReportCellToolClick( bool hadStart, CellToolMode mode, CellBuildTool tool, bool joinedPath, string? modeBefore = null )
	{
		if ( mode is not (CellToolMode.Path or CellToolMode.Queue) )
			return;
		if ( !hadStart )
		{
			if ( tool.Start == null )
				return;
			cellToolClicks = 0;
			if ( mode == CellToolMode.Path )
				PostToolHelp( AdvisorToolHelp.PathStart );
			return;
		}
		// the post comes before the line is laid, so a click that ends the tool takes it back through SetMode's withdrawal: it belongs to the mode before the click
		if ( ++cellToolClicks == 3 )
			PostToolHelp( AdvisorToolHelp.PathLaterClick, modeBefore );
		if ( mode == CellToolMode.Queue && !tool.IsActive && !joinedPath )
			PostToolHelp( AdvisorToolHelp.QueueEnd );
	}

	/// <summary>The window kind of a feature by its type's flags: relief first (thing bit 0), then rest (bit 1), else the generic feature window.</summary>
	private static ObjectInfoKind FeatureKind( ObjectCatalogEntry entry ) =>
		entry.Settings.GetInt( "UsageInfo.ProvidesRelief" ) != 0 ? ObjectInfoKind.Relief
		: entry.Settings.GetInt( "UsageInfo.ChillsYouOut" ) != 0 ? ObjectInfoKind.Rest : ObjectInfoKind.Feature;

	private OriginalObject? objectHelpItem;
	private string? objectHelpName;
	private int objectHelpOpenings;

	/// <summary>
	/// The name under which the open object window (the info arm) is reported: it carries the window kind and, for a ride, the repair choice made
	/// when the window was shown, and changes with each object shown (<see cref="ObjectInfoHelp"/>); null while no object window is open.
	/// </summary>
	private string? ObjectInfoHelpWindow()
	{
		// a fixed item (Info.WhichUIType 4) has no window in the original: the dispatcher's jump table ends at 3 (0x486920, cmp ecx, 3; ja)
		if ( !InfoArmOpen || selectedObject is not { IsDeleted: false, Runtime: not null } item || item.Entry.WhichUIType > 3 || ObjectLayout is not { } layout )
		{
			objectHelpItem = null;
			objectHelpName = null;
			return null;
		}
		if ( !ReferenceEquals( item, objectHelpItem ) || objectHelpName == null )
		{
			// the layout tables of OpenTPW's windows are the original's ride, shop and sideshow tables; a feature (Info.WhichUIType 3) takes the shop table here but opens its own windows in the original
			var kind = layout.Table == OriginalLayoutTables.RideWindow ? (item.Entry.Settings.GetInt( "AddOn.UpgradeType" ) == 2 ? ObjectInfoKind.AddOn : ObjectInfoKind.Ride)
				: layout.Table == OriginalLayoutTables.SideshowWindow ? ObjectInfoKind.Sideshow
				: item.Entry.WhichUIType == 3 ? FeatureKind( item.Entry ) : ObjectInfoKind.Shop;
			var repair = LiveState( item )?.Repair ?? level.PublishedObjectState( item )?.Repair;
			objectHelpItem = item;
			objectHelpName = ObjectInfoHelp.WindowName( kind, repair, ++objectHelpOpenings );
		}
		return objectHelpName;
	}
}

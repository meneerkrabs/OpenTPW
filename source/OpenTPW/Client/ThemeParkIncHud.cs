using OpenTPW.Hud;
using OpenTPW.UI.Original;
using NVector2 = System.Numerics.Vector2;

namespace OpenTPW;

/// <summary>What the Theme Park Inc HUD asks of its host.</summary>
public sealed class ThemeParkIncHudHost
{
	/// <summary>Closes the window.</summary>
	public Action Quit { get; init; } = () => { };
}

/// <summary>
/// The experimental Theme Park Inc slice's HUD: an OpenTPW text interface over the shared build, path and guest
/// systems. Theme Park Inc's own interface is not traced (its <c>ui.wad</c> panels are not Theme Park World's), so
/// nothing here imitates it: the state line names the experiment, the build list offers the world's catalog free
/// (no economy runs), and every button is an OpenTPW control. Building, path and queue interaction follows the
/// traced Theme Park World tools (docs/PATHS.md) because they are what the slice runs.
/// </summary>
// [APPROX:TPI-900] the whole HUD is an OpenTPW text interface; no original Theme Park Inc interface behaviour is traced or reproduced
public sealed class ThemeParkIncHud
{
	/// <summary>Seconds a build/refusal message stays on screen (the TW HUD's message timing, UI-025).</summary>
	public const float MessageSeconds = 8f;

	private readonly Level level;
	private readonly UiStringTable strings;
	private readonly ThemeParkIncHudHost host;
	private readonly UiScreen hud;
	private readonly UiScrollList buildList;
	private readonly UiButton removeButton;
	private readonly List<(string Text, float Age)> messages = new();
	private readonly OriginalBuildCatalog catalog;
	private BuildCategory category = BuildCategory.Rides;
	private string lastLevelMessage = "";
	private GameSpeed speed = GameSpeed.Normal;
	private int cursorX = -1;
	private int cursorY = -1;
	/// <summary>Set by a press the path/queue tool used, so its release does not select an object.</summary>
	private bool toolPress;
	private OriginalObject? selectedObject;

	public ThemeParkIncHud( Level level, UiStringTable strings, ThemeParkIncHudHost host )
	{
		this.level = level;
		this.strings = strings;
		this.host = host;
		catalog = new OriginalBuildCatalog( level.Objects.Catalog );
		hud = new UiScreen( "tpiHud" ) { Modal = false };
		Stack.Push( hud );
		AddLabel( "guests", () => $"Guests in park: {level.Guests?.GetStatistics().InPark ?? 0}", new UiRect( 24, 196, 900, 60 ) );
		AddLabel( "selection", () => SelectedText, new UiRect( 24, 256, 1300, 60 ) );
		AddLabel( "message", () => messages.Count > 0 ? messages[0].Text : "", new UiRect( 24, 316, 1300, 120 ) );
		BuildTopLine();
		BuildWindow();
		removeButton = ToolButton( "remove", () => "Remove", "Click park objects, queues or paths to remove them (no refund: no economy runs)",
			() => { level.IsRemovingObjects = !level.IsRemovingObjects; if ( level.IsRemovingObjects ) DisarmBuild(); }, 1 );
		removeButton.Selected = () => level.IsRemovingObjects;
		ToolButton( "build", () => "Build…", "Open the world's object catalogue (free; nothing is charged)", ToggleBuildWindow, 0 );
		ToolButton( "rotate", () => "Rotate", "Turn the next built object by 90 degrees", () => level.RotateBuild(), 2 );
		ToolButton( "cancel", () => "Cancel", "Put away the build item, remove tool, path and queue tools", DisarmAll, 3 );
		ToolButton( "speed", SpeedText, "Simulation speed: paused, normal, fast, fastest", CycleSpeed, 4 );
		ToolButton( "menu", () => "Menu", "Pause menu (Esc)", OpenPauseMenu, 5 );
		buildList = (UiScrollList)hud.Find( "buildList" )!;
		// [APPROX:PATH-011] Escape inside a tool ends it without writing; only a Back outside every tool opens the menu
		hud.Back = () =>
		{
			if ( level.CellTool.IsActive )
				level.CellTool.Cancel();
			else if ( level.BuildEntry != null )
				DisarmBuild();
			else if ( level.IsRemovingObjects )
				level.IsRemovingObjects = false;
			else
				OpenPauseMenu();
		};
	}

	public UiScreenStack Stack { get; } = new();
	/// <summary>The HUD screen itself (below every open menu on the same stack).</summary>
	public UiScreen Screen => hud;
	/// <summary>True while a menu stands on top of the HUD; the park's clicks wait then.</summary>
	public bool MenuOpen => Stack.Screens.Count > 1;
	/// <summary>The simulation speed the HUD's buttons chose (the game flow scales the level by it).</summary>
	public GameSpeed Speed => speed;
	public float TimeScale => GameSpeedScale.Of( speed );
	public IReadOnlyList<string> Messages => messages.Select( message => message.Text ).ToArray();
	public BuildCategory Category => category;

	private void BuildTopLine()
	{
		// State line: the experiment is named on screen, not only in the log.
		hud.Add( new UiLabel
		{
			Id = "title",
			Text = () => "Theme Park Inc (Sim Coaster) — EXPERIMENTAL OpenTPW slice",
			Font = fonts => fonts.Heading,
			Color = UiColors.Title,
			Bounds = new UiRect( 24, 16, 1500, 70 ),
			Anchor = UiAnchor.TopLeft
		} );
		hud.Add( new UiLabel
		{
			Id = "limits",
			Text = () => GameEditions.Capabilities.ExperimentalNote ?? "",
			Font = fonts => fonts.Small,
			Color = UiColors.Text,
			Wrap = true,
			Bounds = new UiRect( 24, 84, 1300, 110 ),
			Anchor = UiAnchor.TopLeft
		} );
	}

	/// <summary>The build window's parts, hidden on the HUD screen until Build opens them (no modal stack of its own).</summary>
	private void BuildWindow()
	{
		hud.Add( new UiLabel
		{
			Id = "buildTitle",
			Text = () => $"Build {category} — free (no Theme Park Inc economy runs)",
			Font = fonts => fonts.Heading,
			Color = UiColors.Title,
			Visible = false,
			Bounds = new UiRect( 244, 210, 1060, 70 ),
			Anchor = UiAnchor.TopLeft
		} );
		var categories = (BuildCategory[])Enum.GetValues<BuildCategory>();
		for ( var index = 0; index < categories.Length; index++ )
		{
			var choice = categories[index];
			hud.Add( new UiButton
			{
				Id = $"category{choice}",
				Text = () => $"{choice}",
				Font = fonts => fonts.Menu,
				Visible = false,
				Selected = () => category == choice,
				Clicked = () => { category = choice; },
				Bounds = new UiRect( 244 + index * 400, 284, 380, 84 ),
				Anchor = UiAnchor.TopLeft
			} );
		}
		hud.Add( new UiScrollList
		{
			Id = "buildList",
			Rows = () => catalog.GetItems( category ).Select( ItemText ).ToArray(),
			RowHeight = 52,
			Visible = false,
			RowActivated = index => ArmBuild( catalog.GetItems( category )[index].Entry! ),
			Bounds = new UiRect( 244, 380, 1560, 1000 ),
			Anchor = UiAnchor.TopLeft
		} );
		hud.Add( new UiButton
		{
			Id = "buildClose",
			Text = () => "Close (Esc)",
			Font = fonts => fonts.Menu,
			Visible = false,
			Clicked = CloseBuildWindow,
			Bounds = new UiRect( 1330, 210, 474, 84 ),
			Anchor = UiAnchor.TopLeft
		} );
	}

	private UiButton ToolButton( string id, Func<string> text, string help, Action clicked, int position ) => hud.Add( new UiButton
	{
		Id = id,
		Text = text,
		Font = fonts => fonts.Menu,
		Help = help,
		Clicked = clicked,
		Bounds = new UiRect( 2024 - (6 - position) * 306, 1400, 286, 110 ),
		Anchor = UiAnchor.BottomRight
	} );

	private UiLabel AddLabel( string id, Func<string> text, UiRect bounds ) => hud.Add( new UiLabel
	{
		Id = id,
		Text = text,
		Font = fonts => fonts.Label,
		Color = UiColors.Text,
		Wrap = true,
		Bounds = bounds,
		Anchor = UiAnchor.TopLeft
	} );

	private string SpeedText() => speed switch
	{
		GameSpeed.Paused => "Paused",
		GameSpeed.Normal => "Speed x1",
		GameSpeed.Fast => "Speed x2",
		_ => "Speed x4"
	};

	private void CycleSpeed() => speed = speed switch
	{
		GameSpeed.Paused => GameSpeed.Normal,
		GameSpeed.Normal => GameSpeed.Fast,
		GameSpeed.Fast => GameSpeed.Fastest,
		_ => GameSpeed.Paused
	};

	private string ItemText( BuildItem item )
	{
		var name = item.Entry != null && item.Entry.ObjectNameIndex is int index ? strings.Object( index ) : "";
		if ( string.IsNullOrWhiteSpace( name ) )
			name = item.Entry?.DisplayName ?? item.Id;
		// [APPROX:TPI-901] builds are free because the slice runs no economy; the original would charge its own prices
		return $"{name} — free";
	}

	private string SelectedText => selectedObject is not { IsDeleted: false } item ? "" : $"Selected: {item.Entry.DisplayName}{(item.Entry.IsFixedItem ? " (fixed item)" : "")}";

	private void ToggleBuildWindow()
	{
		if ( buildOpen )
			CloseBuildWindow();
		else
		{
			buildOpen = true;
			level.IsRemovingObjects = false;
			SetBuildWindowVisible( true );
		}
	}

	private bool buildOpen;

	private void CloseBuildWindow()
	{
		buildOpen = false;
		SetBuildWindowVisible( false );
	}

	private void SetBuildWindowVisible( bool visible )
	{
		foreach ( var element in hud.Elements )
		{
			if ( element.Id is "buildTitle" or "buildClose" or "buildList" || element.Id.StartsWith( "category", StringComparison.Ordinal ) )
				element.Visible = visible;
		}
		if ( !visible )
			buildList.Selected = -1;
	}

	private void ArmBuild( ObjectCatalogEntry entry )
	{
		level.BuildEntry = entry;
		level.IsRemovingObjects = false;
		CloseBuildWindow();
		PostMessage( $"{entry.DisplayName}: click the park to place it (free); the Rotate button turns it, Esc puts it away." );
	}

	private void DisarmBuild() => level.BuildEntry = null;

	private void DisarmAll()
	{
		DisarmBuild();
		level.IsRemovingObjects = false;
		level.CellTool.Cancel();
		level.ExitCoasterTool();
		PostMessage( "Tools put away." );
	}

	private void OpenPauseMenu()
	{
		if ( MenuOpen )
			return;
		var menu = new UiScreen( "tpiMenu" );
		menu.Add( new UiLabel
		{
			Id = "heading",
			Text = () => "Theme Park Inc experimental slice",
			Font = fonts => fonts.Heading,
			Color = UiColors.Title,
			Align = UiAlign.Center,
			Bounds = new UiRect( 544, 400, 960, 90 ),
			Anchor = UiAnchor.TopLeft
		} );
		menu.Add( new UiLabel
		{
			Id = "limits",
			Text = () => GameEditions.Capabilities.ExperimentalNote ?? "",
			Font = fonts => fonts.Small,
			Wrap = true,
			Align = UiAlign.Center,
			Bounds = new UiRect( 344, 500, 1360, 220 ),
			Anchor = UiAnchor.TopLeft
		} );
		MenuButton( menu, "resume", "Resume", () => Stack.Pop(), 760 );
		MenuButton( menu, "pause", SpeedText, () => { speed = speed == GameSpeed.Paused ? GameSpeed.Normal : GameSpeed.Paused; }, 880 );
		MenuButton( menu, "quit", "Quit", host.Quit, 1000 );
		menu.Back = () => Stack.Pop();
		Stack.Push( menu );
	}

	private static UiButton MenuButton( UiScreen menu, string id, Func<string> text, Action clicked, float top ) => menu.Add( new UiButton
	{
		Id = id,
		Text = text,
		Font = fonts => fonts.Menu,
		Clicked = clicked,
		Bounds = UiRect.FromCenter( new NVector2( 1024, top ), 520, 100 )
	} );

	private static UiButton MenuButton( UiScreen menu, string id, string text, Action clicked, float top ) =>
		MenuButton( menu, id, () => text, clicked, top );

	private void PostMessage( string text )
	{
		if ( string.IsNullOrWhiteSpace( text ) )
			return;
		messages.Add( (text, 0) );
		if ( messages.Count > 3 )
			messages.RemoveAt( 0 );
	}

	/// <summary>One frame: menus first, then the park-view tools. Returns true when the pointer belongs to the HUD.</summary>
	public bool Update( UiContext context, UiInput input )
	{
		// The build window rides on the HUD screen, so a modal menu on top of it hides it (ModalOpen stops the camera keys).
		SetBuildWindowVisible( buildOpen && !MenuOpen );
		var overUi = Stack.Covers( context.Canvas, input.Mouse );
		var consumed = Stack.Update( context, input );
		if ( Camera.Mode is ParkCameraMode camera )
		{
			camera.UiCapturesKeyboard = Input.TextEntryActive || Stack.Screens.Count > 1;
			camera.ModalOpen = Stack.Screens.Any( screen => screen != hud && screen.Modal );
		}
		if ( !MenuOpen && input.Has( UiKeys.Pause ) )
			speed = speed == GameSpeed.Paused ? GameSpeed.Normal : GameSpeed.Paused;

		// The level's last action (builds, refusals) becomes a message here; the slice has no advisor to say it.
		if ( level.LastActionMessage != lastLevelMessage )
		{
			lastLevelMessage = level.LastActionMessage;
			PostMessage( lastLevelMessage );
			if ( lastLevelMessage.StartsWith( "Cannot build", StringComparison.Ordinal ) )
				GameAudio.PlayUi( GameAudio.ErrorEvent );
		}

		var inPark = !overUi && !consumed && level.TryGetGridCell( input.Mouse, new NVector2( context.Canvas.Width, context.Canvas.Height ), out cursorX, out cursorY );
		if ( !inPark )
			cursorX = cursorY = -1;
		var toolClick = UpdateCellTool( context, input, inPark );
		UpdateBlueprint( inPark );
		// [APPROX:UI-027] select by occupied grid cell; original cursor picking is not verified.
		if ( inPark && level.BuildEntry == null && !level.IsPlacing && !level.CellTool.IsActive && !level.IsRemovingObjects && input.LeftReleased && !toolPress )
			selectedObject = level.Objects.FindAt( cursorX, cursorY );
		if ( input.LeftReleased )
			toolPress = false;

		for ( var index = messages.Count - 1; index >= 0; index-- )
		{
			messages[index] = (messages[index].Text, messages[index].Age + context.Delta);
			if ( messages[index].Age > MessageSeconds )
				messages.RemoveAt( index );
		}
		return overUi || consumed || toolClick;
	}

	/// <summary>
	/// The path/queue tool's HUD side, as the Theme Park World HUD drives it (PATH-plan §9.4): a plain click on an
	/// empty owned cell starts the path tool there, Backspace undoes a segment, right click cancels, and clicks inside
	/// the tool commit lines. The commit reports the pointer as captured, so <see cref="Level"/>'s own park click skips it.
	/// </summary>
	private bool UpdateCellTool( UiContext context, UiInput input, bool inPark )
	{
		var tool = level.CellTool;
		if ( MenuOpen || level.BuildEntry != null || level.IsRemovingObjects || level.IsCoasterToolActive )
			return false;
		if ( inPark && tool.HoverHelpId( level.Paths, cursorX, cursorY ) is int help )
			context.HoverHelp = strings.Help( help );
		if ( !tool.IsActive )
		{
			if ( !inPark || !input.LeftPressed || level.Objects.FindAt( cursorX, cursorY ) != null )
				return false;
			// A left click on a queue cell edits that ride's queue (help 444); on an empty owned cell or a path cell it starts the path tool (441/442).
			if ( tool.HoverHelpId( level.Paths, cursorX, cursorY ) is 444 && level.EditQueueAt( cursorX, cursorY ) )
			{
				tool.Hover( (cursorX, cursorY) );
				return toolPress = true;
			}
			if ( tool.HoverHelpId( level.Paths, cursorX, cursorY ) is not (441 or 442) || !level.EnterPathTool( cursorX, cursorY ) )
				return false;
			PostMessage( strings.Help( 443 ) );
			tool.Hover( (cursorX, cursorY) );
			return toolPress = true;
		}
		if ( input.Backspaces > 0 )
			tool.Undo();
		if ( input.RightPressed && inPark && GameOptions.Current.RmbCancel )
		{
			tool.Cancel();
			return true;
		}
		if ( !inPark )
			return false;
		tool.Hover( (cursorX, cursorY) );
		if ( !input.LeftPressed )
			return false;
		// The commit click; Level skips it because this frame's pointer is reported as captured.
		var result = tool.Click( (cursorX, cursorY) );
		if ( result is { Completed: true } )
			GameAudio.PlayPathToolSound( GameAudio.PathLayEvent );
		if ( result is { Completed: false } )
		{
			GameAudio.PlayPathToolSound( GameAudio.PathRefusedEvent );
			PostMessage( strings.Extra( result.StoppedBy == CellBuildResult.NotEnoughMoney ? OpenTpwText.NotEnoughMoney : OpenTpwText.CannotBuildHere ) );
		}
		return toolPress = true;
	}

	/// <summary>The build ghost: the tool's line, or the armed object's footprint, as blueprint markers.</summary>
	private void UpdateBlueprint( bool inPark )
	{
		if ( level.Blueprint is not { } blueprint )
			return;
		var cells = new List<(int X, int Y, BlueprintMarker Marker)>();
		if ( level.CellTool.IsActive && level.CellTool.Ghost is { } ghost )
		{
			foreach ( var cell in ghost.Line )
			{
				var refused = cell == ghost.StoppedAt;
				cells.Add( (cell.X, cell.Y, refused ? BlueprintMarker.Refused : BlueprintMarker.Allowed) );
				if ( refused )
					break;
			}
		}
		else if ( inPark && level.BuildEntry is { } entry && cursorX >= 0 )
		{
			// No economy runs: the footprint's own terrain and occupancy check decides the colour.
			var allowed = level.CheckObject( entry, cursorX, cursorY, level.BuildRotation ) == OriginalPlacementResult.Allowed;
			var anchor = Level.GetCentredAnchor( entry, cursorX, cursorY, level.BuildRotation );
			foreach ( var (x, y, _) in ObjectFootprint.GetCells( entry.Shape, anchor.X, anchor.Y, level.BuildRotation ) )
				cells.Add( (x, y, allowed ? BlueprintMarker.Allowed : BlueprintMarker.Refused) );
		}
		blueprint.Show( cells );
	}

	public void Draw( UiContext context ) => Stack.Draw( context );

	/// <summary>Clears the tool state the level keeps (called when the park is left).</summary>
	public void Close()
	{
		level.CellTool.Cancel();
		Stack.Clear();
	}
}

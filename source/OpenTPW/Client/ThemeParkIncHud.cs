using OpenTPW.Hud;
using OpenTPW.UI.Original;
using NVector2 = System.Numerics.Vector2;

namespace OpenTPW;

/// <summary>What the Theme Park Inc HUD asks of its host.</summary>
public sealed class ThemeParkIncHudHost
{
	/// <summary>Closes the window.</summary>
	public Action Quit { get; init; } = () => { };
	/// <summary>Application-owned layout only; guests, script progress and clock are not persisted.</summary>
	public Action? SaveLayout { get; init; }
	/// <summary>Replaces the world with a fresh experimental session after the HUD's explicit confirmation.</summary>
	public Action? LoadLayout { get; init; }
}

/// <summary>
/// The experimental Theme Park Inc slice's HUD: an OpenTPW text interface over the shared build, path and guest
/// systems. Theme Park Inc's own interface is not traced (its <c>ui.wad</c> panels are not Theme Park World's), so
/// nothing here imitates it: the state line names the experiment, the money line shows the park's cash and date,
/// the build list offers the world's catalog at the prices of its own data, and every button is an OpenTPW control.
/// Building, path and queue interaction follows the traced Theme Park World tools (docs/PATHS.md) because they are what the slice runs.
/// </summary>
// [EXT:tpi-text-hud] the whole HUD is an OpenTPW text interface (an OpenTPW design decision for the experimental slice); no original Theme Park Inc interface behaviour is claimed
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
	/// <summary>The refused path click's press latch: its sound plays at the release (GameAudio.PathRefusedEvent); created on first use.</summary>
	private PathRefusalClick? refusalClickState;
	private PathRefusalClick refusalClick => refusalClickState ??= new();
	private OriginalObject? selectedObject;
	private bool retiredByLayoutLoad;

	public ThemeParkIncHud( Level level, UiStringTable strings, ThemeParkIncHudHost host )
	{
		this.level = level;
		this.strings = strings;
		this.host = host;
		catalog = new OriginalBuildCatalog( level.Objects.Catalog );
		hud = new UiScreen( "tpiHud" ) { Modal = false };
		Stack.Push( hud );
		AddLabel( "guests", () => GuestsText(), new UiRect( 24, 196, 900, 60 ) );
		AddLabel( "money", MoneyText, new UiRect( 960, 196, 1000, 60 ) );
		AddLabel( "selection", () => SelectedText, new UiRect( 24, 256, 1300, 60 ) );
		AddLabel( "message", () => messages.Count > 0 ? messages[0].Text : "", new UiRect( 24, 316, 1300, 120 ) );
		BuildTopLine();
		BuildWindow();
		removeButton = ToolButton( "remove", () => T( OpenTpwText.TpiRemove ), T( OpenTpwText.TpiRemoveHelp ),
			() => { level.IsRemovingObjects = !level.IsRemovingObjects; if ( level.IsRemovingObjects ) DisarmBuild(); }, 1 );
		removeButton.Selected = () => level.IsRemovingObjects;
		ToolButton( "build", () => T( OpenTpwText.TpiBuild ), T( OpenTpwText.TpiBuildHelp ), ToggleBuildWindow, 0 );
		ToolButton( "rotate", () => T( OpenTpwText.TpiRotate ), T( OpenTpwText.TpiRotateHelp ), () => level.RotateBuild(), 2 );
		ToolButton( "cancel", () => T( OpenTpwText.TpiCancel ), T( OpenTpwText.TpiCancelHelp ), DisarmAll, 3 );
		ToolButton( "speed", SpeedText, T( OpenTpwText.TpiSpeedHelp ), CycleSpeed, 4 );
		ToolButton( "menu", () => T( OpenTpwText.TpiMenu ), T( OpenTpwText.TpiMenuHelp ), OpenPauseMenu, 5 );
		buildList = (UiScrollList)hud.Find( "buildList" )!;
		// The place flow's HUD finish (Level.PlaceObject owns the build and the guest link):
		// [BIN:STP-PPC:0x1007497C ride placement] a placed HasQueue ride enters tool mode 3, the queue tool (PATH-plan §8)
		level.Objects.ObjectPlaced += OnObjectPlaced;
		// [BIN:TPW-EXE:0x40C180 Escape in a tool] Escape ends a tool (installs the default park mode, SetMode(0)) and is consumed; only an Escape with no tool on reaches the pause menu row (0x748378, 0x40C4D0)
		hud.Back = () =>
		{
			if ( level.CellTool.IsActive )
				level.CellTool.Cancel();
			else if ( level.BuildEntry != null )
				DisarmBuild();
			else if ( buildOpen )
				CloseBuildWindow();
			else if ( level.IsRemovingObjects )
				level.IsRemovingObjects = false;
			else
				OpenPauseMenu();
		};
	}

	/// <summary>The catalog entry the build window armed (null: the object came from somewhere else).</summary>
	private ObjectCatalogEntry? armedEntry;

	private void OnObjectPlaced( OriginalObject item )
	{
		if ( armedEntry == null || armedEntry != item.Entry )
			return;
		armedEntry = null;
		level.BuildEntry = null;
		// The purchase was charged by Level.PlaceObject (Theme Park Inc's own prices); the queue tool follows as in the Theme Park World place flow.
		PostMessage( string.Format( strings.Extra( OpenTpwText.Built ), ItemNameOf( item.Entry ) ) );
		if ( item.Entry.HasQueue && item.Runtime.IsAttraction && item.Visitors.HasCells && level.EnterQueueTool( item.Visitors ) )
			PostMessage( strings.Extra( OpenTpwText.LayQueue ) );
	}

	private string ItemNameOf( ObjectCatalogEntry entry ) => entry.ObjectNameIndex is int index && !string.IsNullOrWhiteSpace( strings.Object( index ) )
		? strings.Object( index ) : entry.DisplayName;

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
			Text = () => T( OpenTpwText.TpiTitle ),
			Font = fonts => fonts.Heading,
			Color = UiColors.Title,
			Bounds = new UiRect( 24, 16, 1500, 70 ),
			Anchor = UiAnchor.TopLeft
		} );
		hud.Add( new UiLabel
		{
			Id = "limits",
			Text = () => GameEditions.Capabilities.ExperimentalNote == null ? "" : T( OpenTpwText.TpiLimits ),
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
			Text = () => string.Format( T( OpenTpwText.TpiBuildTitle ), CategoryText( category ) ),
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
				Text = () => CategoryText( choice ),
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
			// [EXT:tpi-text-hud] the text HUD's build list clicks like its buttons; no original Theme Park Inc list is claimed (Theme Park World's list rows are silent, UiScrollList.Activate)
			ClicksOnActivate = true,
			Bounds = new UiRect( 244, 380, 1560, 1000 ),
			Anchor = UiAnchor.TopLeft
		} );
		hud.Add( new UiButton
		{
			Id = "buildClose",
			Text = () => T( OpenTpwText.TpiClose ),
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
		GameSpeed.Paused => T( OpenTpwText.TpiPaused ),
		GameSpeed.Normal => string.Format( T( OpenTpwText.TpiSpeed ), 1 ),
		GameSpeed.Fast => string.Format( T( OpenTpwText.TpiSpeed ), 2 ),
		_ => string.Format( T( OpenTpwText.TpiSpeed ), 4 )
	};

	/// <summary>An experimental-HUD text in the selected language (OpenTPW's own strings; docs/LANGUAGES.md, "Theme Park Inc").</summary>
	private string T( OpenTpwText key ) => strings.Extra( key );

	/// <summary>The guest counter line the smoke test also compares against.</summary>
	internal string GuestsText() => string.Format( T( OpenTpwText.TpiGuestsInPark ), level.Guests?.GetStatistics().InPark ?? 0 );

	private string CategoryText( BuildCategory choice ) => T( choice switch
	{
		BuildCategory.Rides => OpenTpwText.TpiCategoryRides,
		BuildCategory.Shops => OpenTpwText.TpiCategoryShops,
		BuildCategory.Sideshows => OpenTpwText.TpiCategorySideshows,
		_ => OpenTpwText.TpiCategoryFeatures
	} );

	private void CycleSpeed() => speed = speed switch
	{
		GameSpeed.Paused => GameSpeed.Normal,
		GameSpeed.Normal => GameSpeed.Fast,
		GameSpeed.Fast => GameSpeed.Fastest,
		_ => GameSpeed.Paused
	};

	private string ItemText( BuildItem item )
	{
		var name = item.Entry != null ? ItemNameOf( item.Entry ) : item.Id;
		// the price is Upgrades[0].CostOfUpgrade of the object's own settings (docs/THEME-PARK-INC.md, "Economy"); an object the economy does not list is not charged
		return item.Entry != null && level.Park?.Economy.Catalog.TryGet( item.Entry.InfoId, out var info ) == true
			? string.Format( T( OpenTpwText.TpiItemPriced ), name, info.PurchaseCost.ToString( "N0" ) )
			: string.Format( T( OpenTpwText.TpiItemNotPriced ), name );
	}

	/// <summary>The money line: cash, the park's date and the entrance fee (the park's own clock; docs/THEME-PARK-INC.md).</summary>
	private string MoneyText()
	{
		if ( level.Park?.Economy is not { } economy )
			return "";
		var state = economy.IsBankrupt ? T( OpenTpwText.TpiBankrupt ) : economy.Balance < 0 ? T( OpenTpwText.TpiInTheRed ) : "";
		return string.Format( T( OpenTpwText.TpiMoney ), economy.Balance.ToString( "N0" ), state, economy.Date, economy.EntranceFee );
	}

	private string SelectedText => selectedObject is not { IsDeleted: false } item ? "" : string.Format( T( OpenTpwText.TpiSelected ), item.Entry.DisplayName ) + (item.Entry.IsFixedItem ? T( OpenTpwText.TpiFixedItem ) : "");

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
		armedEntry = entry;
		level.IsRemovingObjects = false;
		CloseBuildWindow();
		var price = level.Park?.Economy.Catalog.TryGet( entry.InfoId, out var info ) == true ? $" (${info.PurchaseCost:N0})" : "";
		PostMessage( string.Format( T( OpenTpwText.TpiPlaceHint ), ItemNameOf( entry ), price ) );
	}

	private void DisarmBuild()
	{
		level.BuildEntry = null;
		armedEntry = null;
	}

	private void DisarmAll()
	{
		DisarmBuild();
		level.IsRemovingObjects = false;
		level.CellTool.Cancel();
		level.ExitCoasterTool();
		PostMessage( T( OpenTpwText.TpiToolsPutAway ) );
	}

	private void OpenPauseMenu()
	{
		if ( MenuOpen )
			return;
		var menu = new UiScreen( "tpiMenu" );
		menu.Add( new UiLabel
		{
			Id = "heading",
			Text = () => T( OpenTpwText.TpiMenuHeading ),
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
		MenuButton( menu, "resume", T( OpenTpwText.TpiResume ), () => Stack.Pop(), 760 );
		MenuButton( menu, "pause", SpeedText, () => { speed = speed == GameSpeed.Paused ? GameSpeed.Normal : GameSpeed.Paused; }, 880 );
		if (host.SaveLayout != null)
			MenuButton(menu, "saveLayout", T( OpenTpwText.TpiSaveLayout ), () => RunLayoutAction(host.SaveLayout, T( OpenTpwText.TpiLayoutSaved )), 1000);
		if (host.LoadLayout != null)
			MenuButton(menu, "loadLayout", T( OpenTpwText.TpiLoadLayout ), ConfirmLayoutLoad, 1120);
		MenuButton( menu, "quit", T( OpenTpwText.TpiQuit ), host.Quit, host.SaveLayout == null && host.LoadLayout == null ? 1000 : 1240 );
		menu.Back = () => Stack.Pop();
		Stack.Push( menu );
	}

	/// <summary>Runs a layout action; <paramref name="success"/> is posted here, or nowhere when the action replaced this HUD (it posts on the new one).</summary>
	private bool RunLayoutAction(Action action, string? success)
	{
		try { action(); if (success != null) PostMessage(success); return true; }
		catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or NotSupportedException)
		{
			Stack.Push(UiDialogs.Message("layoutFailed", () => error.Message,
				(() => T( OpenTpwText.Back ), () => Stack.Pop())));
			return false;
		}
	}

	private void ConfirmLayoutLoad()
	{
		Stack.Push(UiDialogs.Message("confirmLayoutLoad",
			() => T( OpenTpwText.TpiConfirmLoad ),
			(() => T( OpenTpwText.TpiLoadNew ), () => { Stack.Pop(); retiredByLayoutLoad = RunLayoutAction(host.LoadLayout!, null); }),
			(() => T( OpenTpwText.TpiCancel ), () => Stack.Pop())));
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

	internal void PostMessage( string text )
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
		// Successful load retired this HUD during its callback: leave the candidate camera and old tools alone.
		if (retiredByLayoutLoad) return true;
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
		UpdateBlueprint( inPark, context.Delta );
		// [BIN:TPW-EXE:0x4879D0 object click] the click selects the owner of the hovered cell, the cell of the cursor ray's nearest hit on the terrain or an object's model (0x45bf90, ObjectRayPick),
		// as the Theme Park World park click does (ParkHud)
		if ( inPark && level.BuildEntry == null && !level.IsPlacing && !level.CellTool.IsActive && !level.IsRemovingObjects && input.LeftReleased && !toolPress )
			selectedObject = level.Objects.FindAt( cursorX, cursorY );
		if ( input.LeftReleased )
		{
			toolPress = false;
			if ( refusalClick.Release() is { } refused )
				GameAudio.PlayPathRefused( refused.X, refused.Y );
		}

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
	// [EXT:tpi-tool-flow] the tools follow the traced Theme Park World flow (the TPW tool flow reused for the slice); no original Theme Park Inc build tool is claimed
	private bool UpdateCellTool( UiContext context, UiInput input, bool inPark )
	{
		var tool = level.CellTool;
		if ( MenuOpen || level.BuildEntry != null || level.IsPlacing || level.IsRemovingObjects || level.IsCoasterToolActive )
			return false;
		if ( inPark && tool.HoverHelpId( level.Paths, cursorX, cursorY ) is int help )
			context.HoverHelp = strings.Help( help );
		if ( !tool.IsActive )
		{
			// Backspace over a path or queue cell with no tool on: the backtrack sound, then that cell is removed (GameAudio.BacktrackEvent; a queue is [EXT:queue-backtrack]).
			if ( inPark && input.Backspaces > 0 && level.Commands.Issue( new BacktrackCommand( cursorX, cursorY ) ).Accepted )
				return true;
			if ( !inPark || !input.LeftPressed || level.Objects.FindAt( cursorX, cursorY ) != null )
				return false;
			// A left click on a queue cell edits that ride's queue (help 444); on an empty owned cell or a path cell it starts the path tool (441/442).
			if ( tool.HoverHelpId( level.Paths, cursorX, cursorY ) is 444 && level.EditQueueAt( cursorX, cursorY ) )
			{
				tool.Hover( (cursorX, cursorY) );
				return toolPress = true;
			}
			if ( tool.HoverHelpId( level.Paths, cursorX, cursorY ) is not (441 or 442) || !level.EnterPathTool( cursorX, cursorY, out var refused ) )
				return false;
			// The entering click is the tool's first click: a refused cell ends the tool at once, and the refused click sounds at the release (PathRefusalClick).
			if ( refused != null )
			{
				refusalClick.Press();
				refusalClick.Refused( cursorX, cursorY );
				PostMessage( strings.Extra( refused.StoppedBy == CellBuildResult.NotEnoughMoney ? OpenTpwText.NotEnoughMoney : OpenTpwText.CannotBuildHere ) );
				return toolPress = true;
			}
			// [BIN:TPW-EXE:0x005271B7 lay click] every path or queue click past the refused-ghost test plays cat_ui 101 (0x65) before its start or line, the first click included
			GameAudio.PlayPathToolSound( GameAudio.PathLayEvent );
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
		refusalClick.Press();
		var queueJoin = level.QueueJoinCell( tool );
		var result = tool.Click( (cursorX, cursorY) );
		// [BIN:TPW-EXE:0x527587 queue-join sound] it follows every commit; a click on a refused ghost lays nothing and returns before the commit (0x524A63)
		if ( result is { GhostRefused: false } )
			level.PlayQueueJoin( queueJoin );
		if ( result is { Completed: true } )
			GameAudio.PlayPathToolSound( GameAudio.PathLayEvent );
		if ( result is { Completed: false } )
		{
			// a commit stopped only by the back-of-queue walk's bound is not one of the original's refusals
			if ( !result.StoppedByOwnLimit )
				refusalClick.Refused( cursorX, cursorY );
			PostMessage( strings.Extra( result.StoppedBy == CellBuildResult.NotEnoughMoney ? OpenTpwText.NotEnoughMoney : OpenTpwText.CannotBuildHere ) );
		}
		return toolPress = true;
	}

	/// <summary>The build ghost: the tool's line, or the armed object's footprint, as blueprint markers.</summary>
	private void UpdateBlueprint( bool inPark, float delta )
	{
		if ( level.Blueprint is not { } blueprint )
			return;
		var cells = new List<(int X, int Y, BlueprintMarker Marker)>();
		Dictionary<(int X, int Y), byte>? directions = null;
		if ( level.CellTool.IsActive && level.CellTool.Ghost is { } ghost )
		{
			cells.AddRange( BlueprintGhosts.LineCells( ghost.Line, ghost.StoppedAt, ghost.Symbols ) );
		}
		else if ( inPark && level.BuildEntry is { } entry && cursorX >= 0 )
		{
			// the footprint's own terrain and occupancy check decides the colour (the purchase is checked when the click places it)
			var placementResult = level.CheckObject( entry, cursorX, cursorY, level.BuildRotation );
			var allowed = placementResult == OriginalPlacementResult.Allowed;
			var anchor = Level.GetCentredAnchor( entry, cursorX, cursorY, level.BuildRotation );
			var footprint = level.ObjectBlueprint( entry, anchor.X, anchor.Y, level.BuildRotation, placementResult, allowed );
			cells.AddRange( footprint.Cells );
			directions = footprint.Directions;
		}
		blueprint.Update( delta, false );
		blueprint.Show( cells, directions );
	}

	public void Draw( UiContext context ) => Stack.Draw( context );

	/// <summary>Clears the tool state the level keeps (called when the park is left).</summary>
	public void Close()
	{
		level.CellTool.Cancel();
		Stack.Clear();
	}
}

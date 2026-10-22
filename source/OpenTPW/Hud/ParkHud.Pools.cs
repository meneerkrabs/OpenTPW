using OpenTPW.UI.Original;

namespace OpenTPW.Hud;

/// <summary>
/// The HUD side of the pool tool of the <c>opentpw.pools</c> extension (docs/POOLS.md). The original HUD has no pool entry, so the extension adds pool, slide, river and bridge rows to
/// the buy window's Miscellaneous list (beside Buy Land and Clear Land) while it is on: "Pool water" arms the paint mode and "Remove pool water" the erase mode. Press and drag in the
/// park to paint or erase; bridges use a single click. Backspace with no tool on removes the pool bridge or the one pool water cell under the cursor (<see cref="Level.BacktrackAt"/>). Right click or Escape leaves.
/// </summary>
// [EXT:pools-ui] OpenTPW's own rows and texts; the original game has no pools
public sealed partial class ParkHud
{
	/// <summary>The Miscellaneous list's pool paint row (row id -3).</summary>
	public static readonly BuildItem PoolPaintRow = new( "pool:paint", -3, BuildCategory.Features, 0, ParkEconomy.PoolCellCost, null, Array.Empty<string>() );
	/// <summary>The Miscellaneous list's pool erase row (row id -4).</summary>
	public static readonly BuildItem PoolEraseRow = new( "pool:erase", -4, BuildCategory.Features, 0, 0, null, Array.Empty<string>() );

	/// <summary>[EXT:pools-slides] The Miscellaneous list's slide track row (row id -5): continues the first unfinished slide.</summary>
	public static readonly BuildItem PoolSlideRow = new( "pool:slide", -5, BuildCategory.Features, 0, ParkEconomy.SlidePieceCost, null, Array.Empty<string>() );

	/// <summary>[EXT:pools-river] The Miscellaneous list's lazy river row (row id -20): paints pool water with the river option.</summary>
	public static readonly BuildItem PoolRiverRow = new( "pool:river", -20, BuildCategory.Features, 0, ParkEconomy.PoolCellCost, null, Array.Empty<string>() );

	/// <summary>[EXT:pools-bridges] The Miscellaneous list's single pool bridge row (row id -21). One bridge is built in every direction: it turns with the blueprint keys (<see cref="PoolTool.RotateBridge"/>). Its buy-window preview is the bridge's own Blender mesh (<see cref="PoolBridgePreview"/>).</summary>
	public static readonly BuildItem PoolBridgeRow = new( "pool:bridge", -21, BuildCategory.Features, 0, PoolBridgeWorld.Price, null, Array.Empty<string>() );

	private static bool IsPoolBridgeRow( BuildItem item ) => item.InfoId == PoolBridgeRow.InfoId;

	/// <summary>Whether <paramref name="item"/> is one of the pool rows.</summary>
	public static bool IsPoolRow( BuildItem item ) => item.InfoId is -3 or -4 or -5 or -20 or -21 && item.Entry == null && item.Category == BuildCategory.Features;

	private string PoolRowName( BuildItem item ) => strings.Extra( item.InfoId == PoolBridgeRow.InfoId ? OpenTpwText.PoolBridge
		: item.InfoId == PoolPaintRow.InfoId ? OpenTpwText.PoolRowPaint : item.InfoId == PoolRiverRow.InfoId ? OpenTpwText.PoolRowRiver
		: item.InfoId == PoolSlideRow.InfoId ? OpenTpwText.PoolRowSlide : OpenTpwText.PoolRowErase );

	private long PoolRowPrice( BuildItem item ) => IsPoolBridgeRow( item ) ? PoolBridgeWorld.Price : item.InfoId == PoolSlideRow.InfoId ? (level.Park?.Economy.CellCost( CellPurchase.SlidePiece ) ?? 0)
		: item.InfoId != PoolEraseRow.InfoId ? (level.Park?.Economy.CellCost( CellPurchase.Pool ) ?? 0) : 0;

	private bool IsPoolRowAvailable() => FeatureGates.Pools && !level.IsReadOnlyVisit && level.PoolTool != null;

	/// <summary>
	/// Starts the pool tool in the paint or erase mode after ending the other park tools; closes the window. Choosing the other row while the tool is on switches the mode and keeps
	/// the strokes that can be undone.
	/// </summary>
	private void BeginPoolTool( BuildItem item )
	{
		if ( !IsPoolRowAvailable() || level.PoolTool is not { } tool )
		{
			PostMessage( strings.Extra( OpenTpwText.NotAvailable ) );
			GameAudio.PlayUi( GameAudio.ErrorEvent );
			return;
		}
		// [EXT:pools-slides] the slide row continues the first unfinished slide
		if ( item.InfoId == PoolSlideRow.InfoId )
		{
			if ( level.NextUnfinishedSlideTower() is not { } tower )
			{
				PostMessage( strings.Extra( OpenTpwText.PoolSlideNoTower ) );
				GameAudio.PlayUi( GameAudio.ErrorEvent );
				return;
			}
			if ( level.EnterSlideTool( tower ) )
			{
				CloseBuyWindow();
				PostMessage( strings.Extra( OpenTpwText.PoolSlideHint ) );
			}
			return;
		}
		var erase = item.InfoId == PoolEraseRow.InfoId;
		if ( !tool.IsActive )
		{
			level.EndBuildTools();
			level.StaffPlacement?.Cancel();
			LeaveBuildTool();
		}
		var river = item.InfoId == PoolRiverRow.InfoId;
		var bridge = IsPoolBridgeRow( item );
		if ( !(bridge ? tool.BeginBridge( 1 ) : tool.Begin( erase, river )) )
			return;
		CloseBuyWindow();
		PostMessage( strings.Extra( bridge ? OpenTpwText.PoolBridgeHelp : erase ? OpenTpwText.PoolEraseHint : river ? OpenTpwText.PoolRiverHint : OpenTpwText.PoolPaintHint ) );
	}

	/// <summary>
	/// The pool tool's HUD side: the hovered cell and its help, press and drag to write cells, right click leaves. Clicks inside the tool are captured here so
	/// <see cref="Level"/>'s park click skips them. A refused stroke says so once, when it ends.
	/// </summary>
	private bool UpdatePoolTool( UiContext context, UiInput input, bool inPark )
	{
		if ( level.PoolTool is not { IsActive: true } tool )
			return false;
		if ( input.RightPressed && inPark && GameOptions.Current.RmbCancel )
		{
			tool.Cancel();
			return toolPress = true;
		}
		// Backspace takes back the last stroke
		if ( input.Backspaces > 0 && !tool.IsDragging )
			tool.Undo();
		if ( !inPark )
		{
			// a drag that leaves the park view ends there
			var ended = tool.IsDragging;
			tool.HoverNone();
			if ( ended )
				ReportPoolStroke( tool );
			return false;
		}
		tool.Hover( cursorX, cursorY );
		// a bridge the pointer cannot place over a pool says why instead of the help, so the player sees the reason before clicking (the blueprint marks the cell to blame)
		context.HoverHelp = strings.Extra( tool.ShowsBridgeReason && tool.HoverBridgeResult is { } hoverRefusal ? BridgeRefusalText( hoverRefusal ) : tool.IsBridge ? OpenTpwText.PoolBridgeHelp : tool.IsSlide ? OpenTpwText.PoolSlideHelp : tool.IsErasing ? OpenTpwText.PoolEraseHelp : tool.IsRiver ? OpenTpwText.PoolRiverHelp : OpenTpwText.PoolPaintHelp );
		if ( input.LeftPressed )
		{
			refusalClick.Press();
			tool.Press( cursorX, cursorY );
			return toolPress = true;
		}
		if ( tool.IsDragging )
		{
			if ( input.LeftDown )
				return toolPress = true;
			ReportPoolStroke( tool );
			return toolPress = true;
		}
		return false;
	}

	/// <summary>[EXT:pools-bridges] What a refused bridge placement or removal tells the player, on the click and in the hover help.</summary>
	internal static OpenTpwText BridgeRefusalText( PoolBridgeResult result ) => result switch
	{
		PoolBridgeResult.NotEnoughMoney => OpenTpwText.NotEnoughMoney,
		PoolBridgeResult.NotOverPool => OpenTpwText.PoolBridgeNeedsWater,
		PoolBridgeResult.NotBridge => OpenTpwText.PoolBridgeNotBridge,
		PoolBridgeResult.Standing => OpenTpwText.PoolBridgeStanding,
		PoolBridgeResult.TooSteep => OpenTpwText.PoolBridgeTooSteep,
		PoolBridgeResult.LandIsWater => OpenTpwText.PoolBridgeLandIsWater,
		PoolBridgeResult.Blocked => OpenTpwText.PoolBridgeBlocked,
		PoolBridgeResult.BridgeInTheWay => OpenTpwText.PoolBridgeInTheWay,
		PoolBridgeResult.NotOwned => OpenTpwText.PoolBridgeNotOwned,
		_ => OpenTpwText.CannotBuildHere
	};

	/// <summary>Ends the stroke: the lay sound if it wrote cells, and one refusal sound and message if a cell was refused.</summary>
	private void ReportPoolStroke( PoolTool tool )
	{
		var stroke = tool.Release();
		if ( stroke is { Cells.Count: > 0 } )
			GameAudio.PlayPathToolSound( GameAudio.PathLayEvent );
		if ( tool.SlideCompleted && stroke != null )
			PostMessage( strings.Extra( OpenTpwText.PoolSlideDone ) );
		if ( tool.LastRefusal is not { } refusal )
			return;
		refusalClick.Refused( refusal.Cell.X, refusal.Cell.Y );
		if ( tool.LastBridgeRefusal is { } bridgeRefusal )
		{
			PostMessage( strings.Extra( BridgeRefusalText( bridgeRefusal ) ) );
			return;
		}
		PostMessage( strings.Extra( PoolRefusalText( refusal.Reason, tool.LastSlideRefusal ) ) );
	}

	/// <summary>
	/// Backspace with no tool on (<see cref="BacktrackCommand"/>). True when the key did something: it took a cell or a pool bridge off, or a pool target was refused, which says why with the error sound. False over any other cell, so the key does nothing there.
	/// </summary>
	// [EXT:pools-bridges] [EXT:pools-tool] the refusals of removing a bridge or a water cell with Backspace read as the pool tool's own (BridgeRefusalText, PoolRefusalText)
	private bool BacktrackKey()
	{
		var result = level.Commands.Issue( new BacktrackCommand( cursorX, cursorY ) );
		if ( result.Accepted )
			return true;
		OpenTpwText? why = result.Value switch
		{
			PoolBridgeResult bridge => BridgeRefusalText( bridge ),
			PoolBuildResult water => PoolRefusalText( water ),
			_ => null
		};
		if ( why is not { } text )
			return false;
		GameAudio.PlayUiAt( GameAudio.ErrorEvent, level.CellSoundPosition( cursorX, cursorY ) );
		PostMessage( strings.Extra( text ) );
		return true;
	}

	/// <summary>What a refused pool cell write or erase tells the player (<paramref name="slide"/> says why a slide piece was refused).</summary>
	internal static OpenTpwText PoolRefusalText( PoolBuildResult reason, PoolSlideResult? slide = null ) => reason switch
	{
			PoolBuildResult.SlideRefused => slide switch
			{
				PoolSlideResult.TooSteep => OpenTpwText.PoolSlideTooSteep,
				PoolSlideResult.TooGentle => OpenTpwText.PoolSlideTooGentle,
				PoolSlideResult.TooLow => OpenTpwText.PoolSlideTooLow,
				PoolSlideResult.NotNext => OpenTpwText.PoolSlideNotNext,
				PoolSlideResult.NotOverPool => OpenTpwText.PoolSlideNotPool,
				PoolSlideResult.BadStart => OpenTpwText.PoolSlideBadStart,
				_ => OpenTpwText.CannotBuildHere
			},
			PoolBuildResult.NotEnoughMoney => OpenTpwText.NotEnoughMoney,
			PoolBuildResult.TooSteep => OpenTpwText.PoolTooSteep,
			PoolBuildResult.Swimmers => OpenTpwText.PoolSwimmersInWater,
			PoolBuildResult.ChangingRoomAccess => OpenTpwText.PoolChangingRoomNeedsWater,
			PoolBuildResult.SlideRiders => OpenTpwText.PoolSlideRiders,
			PoolBuildResult.SlideWater => OpenTpwText.PoolSlideWaterNeeded,
			PoolBuildResult.AttractionAccess => OpenTpwText.PoolAttractionNeedsWater,
			PoolBuildResult.ExtraNeedsWater => OpenTpwText.PoolExtraNeedsWater,
			_ => OpenTpwText.CannotBuildHere
	};

	private UiButton? waveSettingsButton;

	/// <summary>
	/// [EXT:pools-waves] The object window's "Wave settings" button, shown while a wave machine is selected: it opens the machine's settings window (<see cref="OpenWaveSettings"/>). The original's object
	/// windows have no row for an OpenTPW object's settings, so the button takes the row of the shop window's settings controls, which a wave machine does not show (ParkHud.ObjectSettings.cs); a second click on the selected machine opens it too.
	/// </summary>
	private void AddWaveSettingsButton()
	{
		waveSettingsButton = hud.Add( new UiButton
		{
			Id = "waveSettings", Text = () => strings.Extra( OpenTpwText.PoolWavesSettings ), Font = fonts => fonts.Label,
			Clicked = () => { if ( selectedObject is { } item ) OpenWaveSettings( item ); },
			Anchor = UiAnchor.Center,
			Visible = false, Enabled = false
		} );
		infoElements.Add( waveSettingsButton );
	}

	private bool IsWaveMachine( OriginalObject? item ) => item is { IsDeleted: false } && FeatureGates.Pools && PoolAttractions.KindOf( item.Entry ) == PoolAttractionKind.WaveMachine
		&& level.Objects.Objects.Contains( item ) && level.PoolWavesOf( item ) is not null;

	/// <summary>
	/// The settings window may change a machine: not in a read-only visit, not in the chat mode (which has no object window), not while another window covers the park, a tool is armed, the camera
	/// rides or text is typed (as the shop price editor, <see cref="PriceViewAllowed"/>); paused is fine.
	/// </summary>
	private bool WaveSettingsAllowed => !level.IsReadOnlyVisit && !ChatMode && Stack.Screens.Count == 1 && !Input.TextEntryActive && PriceViewAllowed;

	private void UpdateWaveSettingsButton()
	{
		if ( waveSettingsButton == null )
			return;
		// hidden in a visit like the object window's other settings controls; a mod's wave machine in the shop or sideshow category keeps that window's own controls in this row
		waveSettingsButton.Visible = InfoArmOpen && !level.IsReadOnlyVisit && IsWaveMachine( selectedObject )
			&& selectedObject!.Entry.WhichUIType is not ((int)BuildCategory.Shops or (int)BuildCategory.Sideshows);
		waveSettingsButton.Enabled = waveSettingsButton.Visible && WaveSettingsAllowed;
	}

	/// <summary>
	/// [EXT:pools-waves] The wave machine's settings window: waves on or off, the height (low, medium, high: <see cref="PoolWaveSettings.Height"/> 1 to 3) and the rest between two runs (short, normal,
	/// long: <see cref="PoolWaveSettings.Interval"/> 0 to 2, <see cref="PoolWaves.RestTurns"/>). The choices edit the window's own copy; the tick writes them to the machine (<see cref="Level.SetPoolWaves"/>),
	/// the cross, Escape or a right click leave it as it was. The window is OpenTPW's own (<see cref="UiDialogs.CenteredWindow"/>), titled with the machine's name; RCT3 keeps these settings in the changing room's window.
	/// </summary>
	internal void OpenWaveSettings( OriginalObject item )
	{
		if ( !WaveSettingsAllowed || !IsWaveMachine( item ) || level.PoolWavesOf( item ) is not { } current )
			return;
		var chosen = current;
		var screen = new ShopPriceScreen( "waveSettings" );
		var window = UiDialogs.CenteredWindow( 1400, 1050 );
		UiDialogs.AddWindow( screen, window, "w_dialog", () => EntryName( item.Entry ) );
		// the frame's right side is thicker than its left, so the content centres a little left of the window's middle, where the title is
		const float Toggle = 350, Gap = 25, Height = 71;
		var middle = window.X + window.Width * 0.48f;
		void Choice( string id, int column, int columns, float y, OpenTpwText text, Func<bool> selected, Action pick )
		{
			var left = middle - (columns * Toggle + (columns - 1) * Gap) / 2 + column * (Toggle + Gap);
			screen.Add( new UiButton { Id = id, Model = "b_ltoggle", Text = () => strings.Extra( text ), Font = fonts => fonts.Label, ClickSound = GameAudio.SelectClickEvent,
				Selected = selected, Clicked = pick, Bounds = new UiRect( left, y, Toggle, Height ), Anchor = UiAnchor.Center } );
		}
		void Heading( string id, float y, OpenTpwText text ) =>
			screen.Add( new UiLabel { Id = id, Text = () => strings.Extra( text ), Font = fonts => fonts.Label, Align = UiAlign.Center,
				Bounds = new UiRect( middle - 500, y, 1000, 80 ), Anchor = UiAnchor.Center } );
		Choice( "wavesOn", 0, 2, window.Y + 180, OpenTpwText.PoolWavesOn, () => chosen.On, () => chosen = chosen with { On = true } );
		Choice( "wavesOff", 1, 2, window.Y + 180, OpenTpwText.PoolWavesOff, () => !chosen.On, () => chosen = chosen with { On = false } );
		Heading( "heightLabel", window.Y + 300, OpenTpwText.PoolWavesHeight );
		var heights = new[] { OpenTpwText.PoolWavesLow, OpenTpwText.PoolWavesMedium, OpenTpwText.PoolWavesHigh };
		for ( var index = 0; index < heights.Length; index++ )
		{
			var height = index + 1;
			Choice( $"height{height}", index, heights.Length, window.Y + 390, heights[index], () => chosen.Height == height, () => chosen = chosen with { Height = height } );
		}
		Heading( "intervalLabel", window.Y + 520, OpenTpwText.PoolWavesInterval );
		var intervals = new[] { OpenTpwText.PoolWavesShort, OpenTpwText.PoolWavesNormal, OpenTpwText.PoolWavesLong };
		for ( var index = 0; index < intervals.Length; index++ )
		{
			var interval = index;
			Choice( $"interval{interval}", index, intervals.Length, window.Y + 610, intervals[index], () => chosen.Interval == interval, () => chosen = chosen with { Interval = interval } );
		}
		void Close() { if ( Stack.Top == screen ) Stack.Pop(); }
		void Apply()
		{
			if ( Stack.Top != screen )
				return;
			// the machine may have gone while the window was open (a visit's owner cannot reach it; a demolition clears the selection)
			// an unchanged choice writes nothing, so a machine still running the default keeps its cell as it was
			if ( IsWaveMachine( item ) && level.PoolWavesOf( item ) != chosen )
				level.Commands.Issue( new SetPoolWavesCommand( item.Runtime.AttractionId, chosen.On, chosen.Height, chosen.Interval ) );
			Close();
		}
		screen.Add( new UiButton { Id = "apply", Model = "b_okay", Clicked = Apply,
			Bounds = new UiRect( window.X + 180, window.Bottom - 180, 200, 100 ), Anchor = UiAnchor.Center } );
		screen.Add( new UiButton { Id = "cancel", Model = "b_exit", Help = strings.Help( 2 ), Clicked = Close,
			Bounds = new UiRect( window.Right - 400, window.Bottom - 180, 200, 100 ), Anchor = UiAnchor.Center } );
		screen.Back = Close;
		Stack.Push( screen );
	}

#if DEBUG
	/// <summary>[EXT:debug-hud] Smoke-only: selects the first placed wave machine and, unless only <paramref name="select"/>, opens its settings window, for interface captures. False when the park has none.</summary>
	internal bool DebugWaveSettings( bool select )
	{
		if ( level.Objects.Objects.FirstOrDefault( IsWaveMachine ) is not { } machine )
			return false;
		SelectObject( machine );
		if ( !select )
			OpenWaveSettings( machine );
		return true;
	}
#endif
}

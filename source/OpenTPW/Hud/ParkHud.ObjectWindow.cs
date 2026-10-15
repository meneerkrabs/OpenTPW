using OpenTPW.UI.Original;
using NVector2 = System.Numerics.Vector2;

namespace OpenTPW.Hud;

/// <summary>
/// The object window of a selected ride, shop or sideshow: the original builds one window per object type from the layout tables
/// 0x755150 (rides), 0x755908 (shops) and 0x755e80 (sideshows) of the Windows executable (<see cref="OriginalLayoutTables"/>): the
/// w_med frame at (248,30)-(1800,1007), the preview box top left, a stats frame of label and value rows, and a bottom row of round buttons.
/// The controls keep the HUD elements' ids; each frame they take the rectangle, help id and model of the control of the selected object's table.
/// </summary>
public sealed partial class ParkHud
{
	// [BIN:TPW-EXE:0x755150 object window tables] the three tables share the window rectangle, the preview box (348,162), the stats frame (853,162) with 7 or 8 rows of a label (x 876..1273) and a value (x 1315..1550), the title box at y 74..153 and the buttons b_door (1430,784), b_erase (300,800), b_okay (1671,818), b_arup/b_ardown (1662,126/216) and b_allthings (1648,583); the ride table also has b_queue, b_ride it!, b_callmech and b_upgrade in the bottom row
	// [DATA:video 4:00, 10:40, 23:40, 24:20, 77:09, 78:20] the windows of Belly Bounce, Tempel der Duisternis, Wilde Aap, Drankwinkel, Klein Toilet and IJswinkeltje have this layout (title in yellow, preview, seven rows, bottom row of round buttons, right column of arrows, list and tick); labels and values are white
	// [APPROX:UI-024] the sliders (speed, capacity, duration, price, quality, sugar, salt, ice, fat, chance of winning) and b_move are not drawn (ride settings and the move tool are not simulated), apply-all copies only the price; the preview is OpenTPW's orthographic icon
	/// <summary>What a row of the stats frame shows; the labels are UITEXT 17-23 for rides and 32-37 and 23 for shops and sideshows.</summary>
	internal enum ObjectRowKind { None, Users, Age, Excitement, Reliability, Repair, Life, Scrap, Customers, CostOfGoods, Takings, Profit, Satisfaction, Happiness }

	// [BIN:TPW-EXE:0x4adfad ride window refresh] the ride window fills control 0x3e21 with the customer sum of the monthly history (0x495cf0), 0x3e1b with the age in days of the object (0x4dd670: the clock less the build time at +0x18, divided by the day), the gauges 0x3e16 and 0x3e18 from 0x4e0560 and 0x4df640 (the excitement and reliability from the three slider values +0x2c..+0x34), 0x3e19 from the float at +0x44, 0x3e17 from 0x4dd6d0 (the float at +0x48) and 0x3e23 with the scrap value (0x4e2400)
	// [BIN:TPW-EXE:0x4b0454 shop window refresh] the shop window fills rows 0 (0xc070, a history sum) and 1 (0xc076, 0x4e1a30), 2 (0xc077, a history sum), 3 (0xc078, 0x4e1c70), the gauge row 4 (0xc079, 0x4e1e30), the scrap value in row 5 (0xc07d, 0x4e2400) and the gauge row 6 (0xc07c, help 23 "Happiness of visitors in the area around the shop")
	// [BIN:TPW-EXE:0x4b1914 sideshow window refresh] the sideshow window's row 0 (0xa09d) is a history sum, row 1 a gauge (0xa096, 0x4e1f20), row 2 a history sum (0xa093), row 3 (0xa092) the shop's 0x4e1c70, the gauge row 4 (0xa099) the ride's excitement routine 0x4e0560, the gauge row 5 (0xa094) the shop's 0x4e1e30, the scrap value in row 6 (0xa0a0) and the happiness gauge in row 7 (0xa09e)
	// [APPROX:UI-024] the labels of the shop and sideshow rows follow the consecutive UITEXT 32-37 (customers, cost of goods, takings, profit, customer satisfaction, local happiness) against those functions, which are not decoded; the sideshow's row 1 gauge (0x4e1f20) is not identified and stays empty; the satisfaction and happiness gauges are not simulated and draw empty
	private static readonly ObjectRowKind[] RideRows = { ObjectRowKind.Users, ObjectRowKind.Age, ObjectRowKind.Excitement, ObjectRowKind.Reliability, ObjectRowKind.Repair, ObjectRowKind.Life, ObjectRowKind.Scrap };
	private static readonly ObjectRowKind[] ShopRows = { ObjectRowKind.Customers, ObjectRowKind.CostOfGoods, ObjectRowKind.Takings, ObjectRowKind.Profit, ObjectRowKind.Satisfaction, ObjectRowKind.Scrap, ObjectRowKind.Happiness };
	private static readonly ObjectRowKind[] SideshowRows = { ObjectRowKind.Customers, ObjectRowKind.None, ObjectRowKind.Takings, ObjectRowKind.Profit, ObjectRowKind.Excitement, ObjectRowKind.Satisfaction, ObjectRowKind.Scrap, ObjectRowKind.Happiness };

	/// <summary>Whether the row kind is a red-to-green gauge (the type-9 value controls of the tables).</summary>
	internal static bool IsGaugeRow( ObjectRowKind kind ) => kind is ObjectRowKind.Excitement or ObjectRowKind.Reliability or ObjectRowKind.Repair or ObjectRowKind.Life or ObjectRowKind.Satisfaction or ObjectRowKind.Happiness;

	private static UIStrings RowLabel( ObjectRowKind kind ) => kind switch
	{
		ObjectRowKind.Users => UIStrings.UsersLastMonth,
		ObjectRowKind.Age => UIStrings.Age,
		ObjectRowKind.Excitement => UIStrings.Excitement,
		ObjectRowKind.Reliability => UIStrings.Reliability,
		ObjectRowKind.Repair => UIStrings.StateOfRepair,
		ObjectRowKind.Life => UIStrings.RemainingLife,
		ObjectRowKind.Scrap => UIStrings.ScrapValue,
		ObjectRowKind.Customers => UIStrings.CustomersLastMonth,
		ObjectRowKind.CostOfGoods => UIStrings.CostOfGoods,
		ObjectRowKind.Takings => UIStrings.TakingsLastMonth,
		ObjectRowKind.Profit => UIStrings.ProfitLastMonth,
		ObjectRowKind.Satisfaction => UIStrings.CustomerSatisfaction,
		ObjectRowKind.Happiness => UIStrings.LocalHappiness,
		_ => UIStrings.Blank
	};

	/// <summary>The row kinds of the table's stats frame, in row order.</summary>
	internal static IReadOnlyList<ObjectRowKind> RowKinds( int category ) => category switch
	{
		(int)BuildCategory.Rides => RideRows,
		(int)BuildCategory.Sideshows => SideshowRows,
		_ => ShopRows
	};

	/// <summary>One object window table with the nodes the HUD places its controls on, found once.</summary>
	private sealed class ObjectWindowLayout
	{
		private static readonly Dictionary<LayoutTable, ObjectWindowLayout> cache = new();

		public required LayoutTable Table { get; init; }
		public required LayoutNode Stats { get; init; }
		public required LayoutNode Preview { get; init; }
		public required LayoutNode Title { get; init; }
		public required (LayoutNode Label, LayoutNode Value)[] Rows { get; init; }
		public LayoutNode? Door { get; init; }
		public LayoutNode? Erase { get; init; }
		public LayoutNode? Okay { get; init; }
		public LayoutNode? Previous { get; init; }
		public LayoutNode? Next { get; init; }
		public LayoutNode? List { get; init; }
		public LayoutNode? RideIt { get; init; }
		public LayoutNode? CallMech { get; init; }
		public LayoutNode? Upgrade { get; init; }
		public LayoutNode? Queue { get; init; }
		public LayoutNode? Track { get; init; }
		public LayoutNode? ApplyAll { get; init; }
		public UiRect? PriceSlot { get; init; }

		/// <summary>The table the original opens for an object of <paramref name="category"/>: rides, sideshows, and the shop table for every other object.</summary>
		public static ObjectWindowLayout For( int category ) => Of( category switch
		{
			(int)BuildCategory.Rides => OriginalLayoutTables.RideWindow,
			(int)BuildCategory.Sideshows => OriginalLayoutTables.SideshowWindow,
			_ => OriginalLayoutTables.ShopWindow
		} );

		private static ObjectWindowLayout Of( LayoutTable table )
		{
			if ( cache.TryGetValue( table, out var known ) )
				return known;
			var stats = table.All.First( node => node.Model == "!frame" && node.Rect.X == 853 );
			var preview = table.All.First( node => node.Model == "!frame" && node.Rect.X == 348 );
			var title = table.All.First( node => node.Type == 1 && node.Model == null && node.Rect.Y == 74 );
			var labels = stats.Children.Where( node => node.Rect.X < 1000 ).OrderBy( node => node.Rect.Y ).ToArray();
			var values = stats.Children.Where( node => node.Rect.X >= 1000 ).OrderBy( node => node.Rect.Y ).ToArray();
			if ( labels.Length != values.Length )
				throw new InvalidOperationException( $"Layout table {table.Name} has {labels.Length} labels and {values.Length} values." );
			// [EXT:shop-price-editor] the price editor takes the place of the table's price slider (shop 0xc086, sideshow 0xa090)
			var slot = table.All.FirstOrDefault( node => node.Type is 3 or 12 && node.Rect.X == 753 );
			return cache[table] = new ObjectWindowLayout
			{
				Table = table, Stats = stats, Preview = preview, Title = title, Rows = labels.Zip( values ).ToArray(),
				Door = table.TryModel( "b_door" ), Erase = table.TryModel( "b_erase" ), Okay = table.TryModel( "b_okay" ), Previous = table.TryModel( "b_arup" ),
				Next = table.TryModel( "b_ardown" ), List = table.TryModel( "b_allthings" ), RideIt = table.TryModel( "b_ride it!" ),
				// a toilet's call button (shops have none of their own) uses the ride table's slot
				CallMech = table.TryModel( "b_callmech" ) ?? OriginalLayoutTables.RideWindow.Model( "b_callmech" ), Upgrade = table.TryModel( "b_upgrade" ), Queue = table.TryModel( "b_queue" ), Track = table.TryModel( "b_track" ), ApplyAll = table.TryModel( "b_applyall" ),
				PriceSlot = slot == null ? null : UiRect.FromCenter( slot.Rect.Center, 340, 150 )
			};
		}
	}

	private const int ObjectWindowRows = 8;

	/// <summary>The stat of the window row <paramref name="row"/> (the original's order), null when the row has none.</summary>
	private ObjectStat? StatOfRow( int row )
	{
		if ( InfoNow is not { } info || row >= info.Stats.Count || info.Stats[row].Label == UIStrings.Blank )
			return null;
		return info.Stats[row];
	}

	/// <summary>The stats of an object in the row order of its window; a row without a value or a kind is blank.</summary>
	private IReadOnlyList<ObjectStat> ObjectStats( OriginalObject item )
	{
		var kinds = RowKinds( item.Entry.WhichUIType );
		var rows = new ObjectStat[kinds.Count];
		var state = LiveState( item );
		var economy = Status.EconomySource as ParkEconomy;
		string Number( long value ) => value.ToString( System.Globalization.CultureInfo.InvariantCulture );
		for ( var index = 0; index < rows.Length; index++ )
		{
			var kind = kinds[index];
			var label = RowLabel( kind );
			rows[index] = kind switch
			{
				ObjectRowKind.None => new ObjectStat( UIStrings.Blank, null ),
				// [DATA:video 15:20, 51:40 ride window] the gauges are red-to-green bars (UiGauge), not percentages; an unknown value draws the empty track
				// [APPROX:UI-028] the fills: excitement is the authored UsageInfo.ExcitementLevel / 100, reliability is 1 - WearRate / 10 of the object's base level (as the buy window does), repair and remaining life are the live economy record's repair and life gauge (a version 2 visit uses the owner's published repair); the original reads the excitement and reliability from the slider values (0x4e0560, 0x4df640) and the other two from the floats at +0x44 and +0x48, which OpenTPW's economy fields do not map onto with certainty
				ObjectRowKind.Excitement => new ObjectStat( label, null, item.Entry.Settings.Has( "UsageInfo.ExcitementLevel" ) ? Math.Clamp( item.Entry.Settings.GetInt( "UsageInfo.ExcitementLevel" ) / 100f, 0f, 1f ) : null, true ),
				ObjectRowKind.Reliability => new ObjectStat( label, null, item.Entry.Upgrades.FirstOrDefault( upgrade => upgrade.Level == 0 ) is { } baseLevel ? Math.Clamp( 1 - baseLevel.GetInt( "WearRate" ) / 10f, 0f, 1f ) : null, true ),
				ObjectRowKind.Repair => new ObjectStat( label, null, state?.Repair is { } liveRepair ? Math.Clamp( (float)liveRepair / 100f, 0f, 1f )
					: level.PublishedObjectState( item )?.Repair is { } repair ? Math.Clamp( (float)repair / 100f, 0f, 1f ) : null, true ),
				ObjectRowKind.Life => new ObjectStat( label, null, state?.LifeGauge is { } life ? Math.Clamp( (float)life / 100f, 0f, 1f ) : null, true ),
				ObjectRowKind.Satisfaction or ObjectRowKind.Happiness => new ObjectStat( label, null, null, true ),
				ObjectRowKind.Users or ObjectRowKind.Customers => new ObjectStat( label, state == null ? null : Number( state.CustomersLastMonth ) ),
				ObjectRowKind.Age => new ObjectStat( label, state == null || economy == null ? null
					: string.Format( strings.Extra( OpenTpwText.AgeDays ), Number( Math.Max( 0, ParkCalendar.DayIndex( economy.Tick ) - ParkCalendar.DayIndex( state.BuiltTick ) ) ) ) ),
				ObjectRowKind.Scrap => new ObjectStat( label, state == null || economy == null ? null : FormatMoney( economy.ScrapValue( state ) ) ),
				ObjectRowKind.CostOfGoods => new ObjectStat( label, state == null ? null : FormatMoney( state.CostOfGoods ) ),
				ObjectRowKind.Takings => new ObjectStat( label, state == null ? null : FormatMoney( state.TakingsLastMonth ) ),
				ObjectRowKind.Profit => new ObjectStat( label, state == null ? null : FormatMoney( state.ProfitLastMonth ) ),
				_ => new ObjectStat( UIStrings.Blank, null )
			};
		}
		return rows;
	}

	private ObjectWindowLayout? ObjectLayout => selectedObject is { IsDeleted: false, Runtime: not null } item ? ObjectWindowLayout.For( item.Entry.WhichUIType ) : null;

	private void BuildObjectWindow()
	{
		T Info<T>( T element ) where T : UiElement
		{
			element.Anchor = UiAnchor.Center;
			element.Visible = false;
			infoElements.Add( hud.Add( element ) );
			return element;
		}
		objWindow = hud.Find( "infoArm" );

		objStatsFrame = Info( new UiModelImage { Id = "infoStatsFrame", Model = "!frame" } );
		objPreviewFrame = Info( new UiModelImage { Id = "infoPreviewFrame", Model = "!frame" } );
		objPreview = Info( new ObjectPreview( this ) { Id = "infoPreview" } );
		objName = Info( new UiLabel
		{
			Id = "infoName",
			Text = () => InfoNow == null ? "" : InfoNow.DisplayName ?? strings.Object( InfoNow.ObjectNameIndex ),
			Font = fonts => fonts.Heading,
			Color = UiColors.Title,
			Align = UiAlign.Center,
			Fit = true
		} );
		for ( var row = 0; row < ObjectWindowRows; row++ )
		{
			var index = row;
			objLabels[index] = Info( new UiLabel { Id = $"statLabel{index}", Text = () => StatOfRow( index ) is { } stat ? strings[stat.Label] : "", Font = fonts => fonts.Small } );
			// [DATA:video 77:09, 24:20] the values are white in the object windows ("23 dagen", "$ 10000"), not green
			objValues[index] = Info( new UiLabel
			{
				Id = $"statValue{index}",
				Text = () => StatOfRow( index ) is { } stat ? stat.Value ?? (stat.IsGauge ? "" : strings.Extra( OpenTpwText.NotSimulated )) : "",
				Font = fonts => fonts.Small,
				Color = UiColors.Text
			} );
			objGauges[index] = Info( new ObjectGauge( this, index ) { Id = $"statGauge{index}" } );
		}
		objDoor = Info( new UiButton { Id = "openRide", Model = "b_door", Clicked = ToggleRideOpen,
			// b_door state frames: open door normally, closed door in the "down" frames.
			Selected = () => selectedObject?.IsOpen == false } );
		objErase = Info( new UiButton { Id = "deleteRide", Model = "b_erase", Clicked = DeleteRide } );
		objOkay = Info( new UiButton { Id = "retractInfo", Model = "b_okay", Clicked = () => SetInfoArm( false ) } );
		objPrevious = Info( new UiButton { Id = "infoPrevious", Model = "b_arup", Clicked = () => CycleSelectedObject( -1 ) } );
		objNext = Info( new UiButton { Id = "infoNext", Model = "b_ardown", Clicked = () => CycleSelectedObject( 1 ) } );
		objList = Info( new UiButton { Id = "infoList", Model = "b_allthings", Clicked = () => { SetInfoArm( false ); OpenAttractionsWindow(); } } );
		// the original's "Ride it!" button (gui_CameraFlags 4); owners ride as well as visitors of a published park
		// [BIN:TPW-EXE:0x755150 b_queue, b_track, b_applyall] the ride window's queue button (help 9) lets the player edit the ride's queue, its track button (help 10) the track; the shop and sideshow windows' apply-all button (help 536, 537) copies the window's settings to every object of the type
		objQueue = Info( new UiButton { Id = "editQueue", Model = "b_queue", Clicked = EditQueue } );
		objTrack = Info( new UiButton { Id = "editTrack", Model = "b_track", Clicked = EditTrack } );
		objApplyAll = Info( new UiButton { Id = "applyAll", Model = "b_applyall", Clicked = ApplyPriceToAll } );
		objRideIt = Info( new UiButton { Id = "rideView", Model = "b_ride it!", Clicked = () => { if ( IsRiding ) StopRide(); else StartRide( selectedObject ); } } );
	}

	private bool CanEditQueue( OriginalObject item ) => !level.IsReadOnlyVisit && item.Entry.HasQueue && item.Runtime.IsAttraction && item.Visitors.HasCells && item.Visitors.QueueFrontCell != null;

	private bool CanEditTrack( OriginalObject item ) => !level.IsReadOnlyVisit && (item.Runtime.Coaster != null || item.Runtime.Bump != null);

	private void EditQueue()
	{
		if ( selectedObject is not { IsDeleted: false } item || !CanEditQueue( item ) || RejectReadOnlyAction() )
			return;
		SetInfoArm( false );
		LeaveBuildTool();
		if ( level.EnterQueueTool( item.Visitors ) )
			PostMessage( strings.Extra( OpenTpwText.LayQueue ) );
	}

	/// <summary>
	/// The ride window's track button (<c>b_track</c>, control 0x3e2a, help 10): for a coaster (track type 3 of the object's type record, +0x9c) it opens the coaster tool on the selected ride
	/// (<c>0x4af200</c> → <c>0x4988c0</c> → <c>0x4474d0</c>, the panel's first button selected by <c>0x4455c0</c>); a water track ride opens its track tool.
	/// </summary>
	// [BIN:TPW-EXE:0x4ad53a track button] the ride window shows control 0x3e2a when the object's type record has a track type (+0x9c nonzero), and its handler 0x4af200 sends type 1 to tool 0xe, type 2 to 0x13 and type 3, a coaster, to 0x4988c0, which enters the tool (0x4474d0, state 2 for a complete track whose circuit is closed)
	private void EditTrack()
	{
		if ( selectedObject is not { IsDeleted: false } item || !CanEditTrack( item ) || RejectReadOnlyAction() )
			return;
		SetInfoArm( false );
		LeaveBuildTool();
		var entered = item.Runtime.Coaster != null ? level.EnterCoasterTool( item ) : level.EnterTrackTool( item );
		if ( entered )
			PostMessage( item.Runtime.Coaster != null ? strings.Help( 0x125 ) : TrackToolHelp );
	}

	/// <summary>Copies the selected shop's or sideshow's price to every placed object of the same type.</summary>
	// [APPROX:UI-024] the original's apply-all copies all of the window's settings (the sliders as well); OpenTPW's window has only the price
	private void ApplyPriceToAll()
	{
		if ( selectedObject is not { IsDeleted: false } owner || !TryPriceTarget( owner, out var economy, out var state ) || RejectReadOnlyAction() )
			return;
		foreach ( var other in level.Objects.Objects )
		{
			if ( other.IsDeleted || ReferenceEquals( other, owner ) || other.Entry.InfoId != owner.Entry.InfoId || level.GetEconomyInstance( other ) is not int instance
				|| !economy.TryGetObject( instance, out var target ) || target.InfoId != state.InfoId )
				continue;
			economy.SetPrices( target.Id, state.Price );
		}
	}

	/// <summary>Moves the selection to the next (<paramref name="direction"/> 1) or previous placed object of the selected one's category, wrapping around.</summary>
	// [BIN:TPW-EXE:0x755150 b_arup/b_ardown] the two round arrows (help 21 and 20) at the top of the right column step through the objects of the window's type
	public void CycleSelectedObject( int direction )
	{
		if ( selectedObject is not { IsDeleted: false } current )
			return;
		var same = level.Objects.Objects.Where( item => !item.IsDeleted && item.Entry.WhichUIType == current.Entry.WhichUIType ).ToList();
		var index = same.IndexOf( current );
		if ( same.Count < 2 || index < 0 )
			return;
		selectedObject = same[((index + Math.Sign( direction )) % same.Count + same.Count) % same.Count];
	}

	// the elements of the object window, found once by BuildObjectWindow
	private UiElement? objQueue, objTrack, objApplyAll, objWindow, objStatsFrame, objPreviewFrame, objPreview, objName, objDoor, objErase, objOkay, objPrevious, objNext, objList, objRideIt;
	private readonly UiElement?[] objLabels = new UiElement?[ObjectWindowRows], objValues = new UiElement?[ObjectWindowRows], objGauges = new UiElement?[ObjectWindowRows];
	private ObjectWindowLayout? appliedObjectLayout;
	private string? appliedObjectLanguage;

	private static void Place( UiElement? element, LayoutNode? node, string? help = null )
	{
		if ( element == null )
			return;
		if ( node == null )
		{
			element.Visible = false;
			return;
		}
		element.Bounds = node.Rect;
		element.Anchor = UiAnchor.Center;
		if ( element is UiButton )
			element.Help = help;
	}

	private string? HelpOf( int id ) => id >= 0 ? strings.Help( id ) : null;

	/// <summary>Places each control of the object window at its table rectangle for the selected object; a control the table lacks is hidden. Nothing runs while the window is closed.</summary>
	private void ApplyObjectLayout()
	{
		if ( !InfoArmOpen || ObjectLayout is not { } layout )
			return;
		var changed = !ReferenceEquals( layout, appliedObjectLayout ) || appliedObjectLanguage != strings.Language;
		if ( changed )
		{
			appliedObjectLayout = layout;
			appliedObjectLanguage = strings.Language;
			Place( objWindow, layout.Table.Root );
			Place( objStatsFrame, layout.Stats );
			Place( objPreviewFrame, layout.Preview );
			Place( objPreview, layout.Preview );
			Place( objName, layout.Title );
			Place( objDoor, layout.Door );
			Place( objErase, layout.Erase, HelpOf( layout.Erase?.Help ?? -1 ) );
			Place( objOkay, layout.Okay, HelpOf( layout.Okay?.Help ?? -1 ) );
			Place( objPrevious, layout.Previous, HelpOf( layout.Previous?.Help ?? -1 ) );
			Place( objNext, layout.Next, HelpOf( layout.Next?.Help ?? -1 ) );
			Place( objList, layout.List, HelpOf( layout.List?.Help ?? -1 ) );
			Place( objRideIt, layout.RideIt, HelpOf( layout.RideIt?.Help ?? -1 ) );
			Place( objQueue, layout.Queue, HelpOf( layout.Queue?.Help ?? -1 ) );
			Place( objTrack, layout.Track, HelpOf( layout.Track?.Help ?? -1 ) );
			Place( objApplyAll, layout.ApplyAll, HelpOf( layout.ApplyAll?.Help ?? -1 ) );
			Place( callStaffButton, layout.CallMech, HelpOf( layout.CallMech?.Help ?? -1 ) );
			Place( rideUpgradeButton, layout.Upgrade, HelpOf( layout.Upgrade?.Help ?? -1 ) );
			if ( shopPriceButton != null && layout.PriceSlot is { } slot )
			{
				shopPriceButton.Bounds = slot;
				shopPriceButton.Anchor = UiAnchor.Center;
			}
			for ( var row = 0; row < ObjectWindowRows; row++ )
			{
				var present = row < layout.Rows.Length;
				Place( objLabels[row], present ? layout.Rows[row].Label : null );
				Place( objValues[row], present ? layout.Rows[row].Value : null );
				Place( objGauges[row], present ? layout.Rows[row].Value : null );
			}
		}
		// the table's second help id (12, open) is the door's prompt while it is down, that is closed; its first (13, close) while it is open
		if ( objDoor != null && layout.Door != null )
			objDoor.Help = HelpOf( selectedObject?.IsOpen == false && layout.Door.AltHelp >= 0 ? layout.Door.AltHelp : layout.Door.Help );
		// the per-frame visibility reset shows every control of the window: hide the ones this table lacks
		if ( layout.RideIt == null && objRideIt != null ) objRideIt.Visible = false;
		if ( layout.Upgrade == null && rideUpgradeButton != null ) rideUpgradeButton.Visible = false;
		// the queue, track and apply-all buttons show only where they can act
		var target = selectedObject!;
		if ( objQueue != null ) objQueue.Visible &= layout.Queue != null && CanEditQueue( target );
		if ( objTrack != null ) objTrack.Visible &= layout.Track != null && CanEditTrack( target );
		if ( objApplyAll != null ) objApplyAll.Visible &= layout.ApplyAll != null && TryPriceTarget( target, out _, out _ );
		for ( var row = layout.Rows.Length; row < ObjectWindowRows; row++ )
		{
			if ( objLabels[row] != null ) objLabels[row]!.Visible = false;
			if ( objValues[row] != null ) objValues[row]!.Visible = false;
			if ( objGauges[row] != null ) objGauges[row]!.Visible = false;
		}
	}

	/// <summary>The red-to-green gauge of a window row; rows whose stat is text draw nothing.</summary>
	private sealed class ObjectGauge : UiElement
	{
		private readonly ParkHud owner;
		private readonly int row;

		public ObjectGauge( ParkHud owner, int row )
		{
			this.owner = owner;
			this.row = row;
		}

		public override void Draw( UiContext context, bool focused, bool pressed )
		{
			if ( owner.StatOfRow( row ) is not { IsGauge: true } stat )
				return;
			var rect = ScreenRect( context.Canvas );
			UiGauge.Draw( context.Batch, rect.Inflate( -rect.Height * 0.12f ), stat.Gauge );
		}
	}

	/// <summary>The spinning preview of the selected object in the window's preview box.</summary>
	private sealed class ObjectPreview : UiElement
	{
		private readonly ParkHud owner;
		private object? cachedEntry;
		private BuildItem? cachedItem;

		public ObjectPreview( ParkHud owner ) => this.owner = owner;

		public override void Draw( UiContext context, bool focused, bool pressed )
		{
			if ( owner.selectedObject is not { IsDeleted: false } item )
				return;
			if ( !ReferenceEquals( cachedEntry, item.Entry ) )
			{
				cachedEntry = item.Entry;
				cachedItem = owner.Catalog.GetItems( (BuildCategory)item.Entry.WhichUIType ).FirstOrDefault( candidate => candidate.InfoId == item.Entry.InfoId );
			}
			if ( cachedItem == null )
				return;
			var rect = ScreenRect( context.Canvas );
			var size = Math.Min( rect.Width, rect.Height );
			var square = new UiRect( rect.X + (rect.Width - size) / 2, rect.Y + (rect.Height - size) / 2, size, size ).Inflate( -size * 0.1f );
			owner.GetIcon( cachedItem )?.Draw( context.Batch, square, context.Time * 0.8f );
		}
	}
}

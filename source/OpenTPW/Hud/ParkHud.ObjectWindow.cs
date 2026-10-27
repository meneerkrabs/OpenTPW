using OpenTPW.UI.Original;
using Veldrid;
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
	// [BIN:TPW-EXE:0x4ad6c7 b_move] the ride window's refresh shows control 0x3e2c and enables it with 0x52c2b0 (Level.CanMove); the shop and sideshow windows' b_move (0xc080, 0xa0a8) have no such test and click the same pick-up 0x48cfa0 (their handlers 0x4b0b30 and 0x4b2150)
	// [APPROX:UI-024] the preview is OpenTPW's orthographic icon; the settings controls are ParkHud.ObjectSettings
	/// <summary>What a row of the stats frame shows; the labels are UITEXT 17-23 for rides, 32-38 for shops and 41-48 for sideshows (<see cref="RowLabels"/>).</summary>
	internal enum ObjectRowKind { None, Users, Age, Excitement, Reliability, Repair, Life, Scrap, Customers, CostOfGoods, Takings, Profit, Satisfaction, Happiness, Winners }

	// [BIN:TPW-EXE:0x4adfad ride window refresh] the ride window fills control 0x3e21 with the customer sum of the monthly history (0x495cf0), 0x3e1b with the age in days of the object (0x4dd670: the clock less the build time at +0x18, divided by the day), the gauges 0x3e16 and 0x3e18 from 0x4e0560 and 0x4df640 (the excitement and reliability from the three slider values +0x2c..+0x34), 0x3e19 from the float at +0x44, 0x3e17 from 0x4dd6d0 (the float at +0x48) and 0x3e23 with the scrap value (0x4e2400)
	// [BIN:TPW-EXE:0x4b0454 shop window refresh] the shop window fills rows 0 (0xc070, a history sum) and 1 (0xc076, 0x4e1a30), 2 (0xc077, a history sum), 3 (0xc078, 0x4e1c70), the gauge row 4 (0xc079, 0x4e1e30), the scrap value in row 5 (0xc07d, 0x4e2400) and the gauge row 6 (0xc07c, help 23 "Happiness of visitors in the area around the shop")
	// [BIN:TPW-EXE:0x4b1914 sideshow window refresh] the sideshow window's row 0 (0xa09d) is a history sum, row 1 a gauge (0xa096, 0x4e1f20), row 2 a history sum (0xa093), row 3 (0xa092) the shop's 0x4e1c70, the gauge row 4 (0xa099) the ride's excitement routine 0x4e0560, the gauge row 5 (0xa094) the shop's 0x4e1e30, the scrap value in row 6 (0xa0a0) and the happiness gauge in row 7 (0xa09e)
	// [BIN:TPW-EXE:0x4afc10 shop window labels] the label controls 0xc075, 0xc071, 0xc072, 0xc073, 0xc074, 0xc07a and 0xc07b (rows 0-6 by their y) take UITEXT 0x20-0x26 (0x485b00): customers last month, cost of goods, takings last month, profit last month, customer satisfaction, scrap value, local happiness
	// [BIN:TPW-EXE:0x4b1040 sideshow window labels] the label controls 0xa09c, 0xa095, 0xa09a, 0xa098, 0xa09b, 0xa097, 0xa0a1 and 0xa09f (rows 0-7) take UITEXT 0x29-0x30: customers last month, winners last month, takings last month, profit last month, excitement, customer satisfaction, scrap value, local happiness
	// [BIN:TPW-EXE:0x4e1f20 winners gauge] the sideshow's row 1 gauge is 100 × the winners over the customers (0 without customers), the two summed over the 30-entry histories at +0x2bc and +0x1ac
	// [BIN:TPW-EXE:0x4b01d0 shop window refresh] the customers, takings and profit rows are sums over the object's 30-day rings, the profit row the takings ring's sum less the costs ring's (0x4e1c70), not a calendar month (ECON-057)
	private static readonly ObjectRowKind[] RideRows = { ObjectRowKind.Users, ObjectRowKind.Age, ObjectRowKind.Excitement, ObjectRowKind.Reliability, ObjectRowKind.Repair, ObjectRowKind.Life, ObjectRowKind.Scrap };
	private static readonly ObjectRowKind[] ShopRows = { ObjectRowKind.Customers, ObjectRowKind.CostOfGoods, ObjectRowKind.Takings, ObjectRowKind.Profit, ObjectRowKind.Satisfaction, ObjectRowKind.Scrap, ObjectRowKind.Happiness };
	private static readonly ObjectRowKind[] SideshowRows = { ObjectRowKind.Customers, ObjectRowKind.Winners, ObjectRowKind.Takings, ObjectRowKind.Profit, ObjectRowKind.Excitement, ObjectRowKind.Satisfaction, ObjectRowKind.Scrap, ObjectRowKind.Happiness };
	private static readonly UIStrings[] RideLabels = { UIStrings.UsersLastMonth, UIStrings.Age, UIStrings.Excitement, UIStrings.Reliability, UIStrings.StateOfRepair, UIStrings.RemainingLife, UIStrings.ScrapValue };
	private static readonly UIStrings[] ShopLabels = { UIStrings.CustomersLastMonth, UIStrings.CostOfGoods, UIStrings.TakingsLastMonth, UIStrings.ProfitLastMonth, UIStrings.CustomerSatisfaction, UIStrings.ShopScrapValue, UIStrings.LocalHappiness };
	private static readonly UIStrings[] SideshowLabels = { UIStrings.SideshowCustomersLastMonth, UIStrings.WinnersLastMonth, UIStrings.SideshowTakingsLastMonth, UIStrings.SideshowProfitLastMonth,
		UIStrings.SideshowExcitement, UIStrings.SideshowCustomerSatisfaction, UIStrings.SideshowScrapValue, UIStrings.SideshowLocalHappiness };

	/// <summary>Whether the row kind is a red-to-green gauge (the type-9 value controls of the tables).</summary>
	internal static bool IsGaugeRow( ObjectRowKind kind ) => kind is ObjectRowKind.Excitement or ObjectRowKind.Reliability or ObjectRowKind.Repair or ObjectRowKind.Life or ObjectRowKind.Satisfaction or ObjectRowKind.Happiness or ObjectRowKind.Winners;

	/// <summary>The row kinds of the table's stats frame, in row order.</summary>
	internal static IReadOnlyList<ObjectRowKind> RowKinds( int category ) => category switch
	{
		(int)BuildCategory.Rides => RideRows,
		(int)BuildCategory.Sideshows => SideshowRows,
		_ => ShopRows
	};

	/// <summary>The UITEXT labels of the table's stats frame, in row order.</summary>
	internal static IReadOnlyList<UIStrings> RowLabels( int category ) => category switch
	{
		(int)BuildCategory.Rides => RideLabels,
		(int)BuildCategory.Sideshows => SideshowLabels,
		_ => ShopLabels
	};

	/// <summary>The sideshow's winners gauge, 0..1: <c>trunc(100 × winners / customers)</c> as the window sets it, <c>(percent &lt;&lt; 10) / 100</c> of 1024 (<c>0x4b16c0</c>), 0 without customers (<c>0x4e1f20</c>).</summary>
	internal static float WinnersGauge( long winners, long customers ) => customers <= 0 ? 0f : UiGauge.OfPercent( (int)Math.Clamp( winners * 100 / customers, 0, 100 ) );

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
		public LayoutNode? Move { get; init; }
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
		/// <summary>The status banner over the preview ("chev": 0x3E25 rides, 0xC06D shops, 0xA0A3 sideshows).</summary>
		public LayoutNode? Status { get; init; }

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
			return cache[table] = new ObjectWindowLayout
			{
				Table = table, Stats = stats, Preview = preview, Title = title, Rows = labels.Zip( values ).ToArray(),
				Door = table.TryModel( "b_door" ), Erase = table.TryModel( "b_erase" ), Move = table.TryModel( "b_move" ), Okay = table.TryModel( "b_okay" ), Previous = table.TryModel( "b_arup" ),
				Next = table.TryModel( "b_ardown" ), List = table.TryModel( "b_allthings" ), RideIt = table.TryModel( "b_ride it!" ),
				// a toilet's call button (shops have none of their own) uses the ride table's slot
				CallMech = table.TryModel( "b_callmech" ) ?? OriginalLayoutTables.RideWindow.Model( "b_callmech" ), Upgrade = table.TryModel( "b_upgrade" ), Queue = table.TryModel( "b_queue" ), Track = table.TryModel( "b_track" ), ApplyAll = table.TryModel( "b_applyall" ),
				Status = table.TryModel( "chev" )
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
		var labels = RowLabels( item.Entry.WhichUIType );
		var rows = new ObjectStat[kinds.Count];
		var state = LiveState( item );
		var economy = Status.EconomySource as ParkEconomy;
		var window = WindowValues( item );
		for ( var index = 0; index < rows.Length; index++ )
			rows[index] = ObjectStatOf( item, kinds[index], state, economy, labels[index], window );
		return rows;
	}

	/// <summary>The shop and sideshow windows' customers row: UITEXT record 0x1CC ("{0x19} of {0x1A}"), the 30-day customers of the first slot and the customers and walk-aways of the second.</summary>
	// [BIN:TPW-EXE:0x4b01d0 shop window customers row] the row (0xc070) sums the customers ring (+0x1A8) and the walk-aways ring (+0x230) over their 30 days and formats record 0x1CC with the text variable 0x19 = the customers and 0x1A = the customers plus the walk-aways (0x4b0423); the sideshow window's first row (0xa09d) does the same (0x4b18e6)
	internal static string CustomersText( UiStringTable strings, long customers, long walkAways ) =>
		strings.FormatByType( CustomersOfTotalRecord, new Dictionary<int, string>
		{
			[0x19] = customers.ToString( System.Globalization.CultureInfo.InvariantCulture ),
			[0x1A] = (customers + walkAways).ToString( System.Globalization.CultureInfo.InvariantCulture ),
		} );

	/// <summary>The index of UITEXT record 0x1CC in the table (the formatter takes the id one-based): the only record with the text variables 0x19 and 0x1A.</summary>
	internal const int CustomersOfTotalRecord = 0x1CC - 1;

	/// <summary>One stat of an object as its window shows it (the All Attractions list's bars use the same values).</summary>
	private ObjectStat ObjectStatOf( OriginalObject item, ObjectRowKind kind, ParkObjectState? state, ParkEconomy? economy, UIStrings label = UIStrings.Blank, ObjectWindowSettings? window = null )
	{
		string Number( long value ) => value.ToString( System.Globalization.CultureInfo.InvariantCulture );
		return kind switch
		{
			ObjectRowKind.None => new ObjectStat( UIStrings.Blank, null ),
			// [DATA:video 15:20, 51:40 ride window] the gauges are red-to-green bars (UiGauge), not percentages; an unknown value draws the empty track
			// [BIN:TPW-EXE:0x4ade40 ride window refresh] the four gauges take (byte value << 10) / 100: 0x3e16 the excitement routine 0x4e0560 and 0x3e18 the reliability routine 0x4df640, both called with the window's own slider values (+0x2c speed, +0x30 capacity, +0x34 duration), 0x3e19 the float at the object's +0x44 (the state of repair) and 0x3e17 the float at +0x48 (the second gauge, the remaining life), both truncated
			ObjectRowKind.Excitement => new ObjectStat( label, null, ExcitementGauge( item, window ), true ),
			ObjectRowKind.Reliability => new ObjectStat( label, null, ReliabilityGauge( item, window ), true ),
			ObjectRowKind.Repair => new ObjectStat( label, null, state?.Repair is { } liveRepair ? UiGauge.OfPercent( RideBreakdownRules.GaugeByte( (float)liveRepair ) )
				: level.PublishedObjectState( item )?.Repair is { } repair ? UiGauge.OfPercent( RideBreakdownRules.GaugeByte( (float)repair ) ) : null, true ),
			ObjectRowKind.Life => new ObjectStat( label, null, state?.LifeGauge is { } life ? UiGauge.OfPercent( RideBreakdownRules.GaugeByte( (float)life ) ) : null, true ),
			// [BIN:TPW-EXE:0x4b1914 shop and sideshow refresh] the local happiness gauge (0xc07c, 0xa09e) is 0x4cd880 of the object's cell with radius 1 × 1024 / 100
			ObjectRowKind.Happiness => new ObjectStat( label, null, LocalHappinessGauge( item ), true, true ),
			// [BIN:TPW-EXE:0x4b1a89 customer satisfaction gauge] the shop and sideshow windows' gauge (0xc079, 0xa094) is 0x4e1e30 of the object's own 30-day history (50 plus the mean of the visits' happiness changes), (value << 10) / 100
			ObjectRowKind.Satisfaction => new ObjectStat( label, null, state == null ? null : UiGauge.OfPercent( state.SatisfactionRing.Satisfaction ), true ),
			ObjectRowKind.Winners => new ObjectStat( label, null, state == null ? null : WinnersGauge( state.WinnersLast30Days, state.CustomersLast30Days ), true ),
			ObjectRowKind.Users => new ObjectStat( label, state == null ? null : Number( state.CustomersLast30Days ) ),
			ObjectRowKind.Customers => new ObjectStat( label, state == null ? null : CustomersText( strings, state.CustomersLast30Days, state.WalkAwayDays.Sum() ) ),
			ObjectRowKind.Age => new ObjectStat( label, state == null || economy == null ? null
				: string.Format( strings.Extra( OpenTpwText.AgeDays ), Number( Math.Max( 0, ParkCalendar.DayIndex( economy.Tick ) - ParkCalendar.DayIndex( state.BuiltTick ) ) ) ) ),
			ObjectRowKind.Scrap => new ObjectStat( label, state == null || economy == null ? null : FormatMoney( economy.ScrapValue( state ) ) ),
			ObjectRowKind.CostOfGoods => new ObjectStat( label, state == null ? null : FormatMoney( state.CostOfGoods ) ),
			ObjectRowKind.Takings => new ObjectStat( label, state == null ? null : FormatMoney( state.TakingsLast30Days ) ),
			ObjectRowKind.Profit => new ObjectStat( label, state == null ? null : FormatMoney( state.ProfitLast30Days ) ),
			_ => new ObjectStat( UIStrings.Blank, null )
		};
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
		objStatus = Info( new StatusBanner { Id = "infoStatus" } );
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
			// [EXT:online-coop-commands] a guest's door draws its open word as drawn (C4b-2)
			Selected = () => selectedObject is { } shown && !DrawnIsOpen( shown ) } );
		objErase = Info( new UiButton { Id = "deleteRide", Model = "b_erase", Clicked = DeleteRide } );
		objMove = Info( new UiButton { Id = "moveRide", Model = "b_move", Clicked = MoveObject } );
		objOkay = Info( new UiButton { Id = "retractInfo", Model = "b_okay", Clicked = () => SetInfoArm( false ) } );
		objPrevious = Info( new UiButton { Id = "infoPrevious", Model = "b_arup", Clicked = () => CycleSelectedObject( -1 ) } );
		objNext = Info( new UiButton { Id = "infoNext", Model = "b_ardown", Clicked = () => CycleSelectedObject( 1 ) } );
		objList = Info( new UiButton { Id = "infoList", Model = "b_allthings", Clicked = () => { var item = selectedObject; SetInfoArm( false ); OpenAttractionsWindow( item is { } shown ? (BuildCategory)shown.Entry.WhichUIType : null, item ); } } );
		// the original's "Ride it!" button (gui_CameraFlags 4); owners ride as well as visitors of a published park
		// [BIN:TPW-EXE:0x755150 b_queue, b_track, b_applyall] the ride window's queue button (help 9) lets the player edit the ride's queue, its track button (help 10) the track; the shop and sideshow windows' apply-all button (help 536, 537) copies the window's settings to every object of the type
		objQueue = Info( new UiButton { Id = "editQueue", Model = "b_queue", Clicked = EditQueue } );
		objTrack = Info( new UiButton { Id = "editTrack", Model = "b_track", Clicked = EditTrack } );
		objApplyAll = Info( new UiButton { Id = "applyAll", Model = "b_applyall", Clicked = ApplySettingsToAll } );
		objRideIt = Info( new UiButton { Id = "rideView", Model = "b_ride it!", Clicked = () => { if ( IsRiding ) StopRide(); else StartRide( selectedObject ); } } );
	}

	private bool CanEditQueue( OriginalObject item ) => !level.IsReadOnlyVisit && item.Entry.HasQueue && item.Runtime.IsAttraction && item.Visitors.HasCells && item.Visitors.QueueFrontCell != null
		// [EXT:online-coop-claims] the queue of a ride another player claims is that player's while its track or coaster tool is on it (C3c-1, C3c-2): the window offers no tool whose lines
		// would be refused
		&& !ClaimedAgainstThisPlayer( item );

	private bool CanEditTrack( OriginalObject item ) => !level.IsReadOnlyVisit && (item.Runtime.Coaster != null || item.Runtime.Bump != null)
		// [EXT:online-coop-claims] nor the track of a ride another player claims (C3c-1); a guest's track tool claims its kart or water ride (C3c-1b) and its coaster tool its coaster (C3c-2)
		// through the host
		&& !ClaimedAgainstThisPlayer( item );

	/// <summary>
	/// [EXT:online-coop-claims] Whether <paramref name="item"/> is claimed against this machine's player in a shared park (<see cref="CoopBuildRights.IsClaimedAgainst"/>): its window offers
	/// that player no queue or track tool, sale or Move, whose commands the rights would refuse (C3c-1).
	/// </summary>
	private bool ClaimedAgainstThisPlayer( OriginalObject item ) =>
		level.CoopSession != null && level.Commands.Rights is CoopBuildRights rights && rights.IsClaimedAgainst( level.Commands.Player, item );

	private bool CanMoveObject( OriginalObject item ) => !level.IsReadOnlyVisit && !item.Entry.IsFixedItem && level.CanMove( item ) && !ClaimedAgainstThisPlayer( item );

	/// <summary>
	/// The window's move button (<c>b_move</c>, <c>0x48cfa0</c>): the object is demolished as its erase button does, without the confirmation, and the Move tool arms the same type at its angle;
	/// the window closes.
	/// </summary>
	// [BIN:TPW-EXE:0x48cfa0 move pick-up] the pick-up neither asks for the confirmation the erase button's handler 0x48cd10 asks (the option 0x78d90f) nor posts a tool help: its mode switch 0x52f200(0x3b, 1) posts only for modes 3, 0x15, 0x16, 1 and 0x35
	private void MoveObject()
	{
		if ( RejectReadOnlyAction() )
			return;
		if ( selectedObject is not { IsDeleted: false } item || !CanMoveObject( item ) )
			return;
		if ( BuyItemOf( item.Entry ) is not { } buyItem )
			return;
		level.CellTool.Cancel();
		level.StaffPlacement?.Cancel();
		level.PatrolTool?.Cancel();
		level.BuyLandTool?.Cancel();
		level.AreaClearTool?.Cancel();
		level.PoolTool?.Cancel();
		LeaveBuildTool();
		var waiting = level.WaitingToolStart;
		if ( !level.BeginMove( item ) )
		{
			// [EXT:online-coop-commands] a guest's pick-up went to the host: the Move tool arms with this item once it comes back (C3b-2), and the window closes now, as for the host; a
			// click that sent none (another pick-up waits) binds nothing and keeps the window
			if ( level.WaitingToolStart is { Command: PickUpObjectCommand } request && !ReferenceEquals( request, waiting ) )
			{
				waitingMove = (request, buyItem);
				selectedObject = null;
				SetInfoArm( false );
			}
			return;
		}
		pendingItem = buyItem;
		selectedObject = null;
		SetInfoArm( false );
	}

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
		// [EXT:online-coop-claims] a guest's track or coaster tool starts once its claim (and a coaster's entry) is back, with the help then (C3c-1b, C3c-2)
		var entered = item.Runtime.Coaster != null ? level.EnterCoasterTool( item ) : level.BeginTrackTool( item, placed: false ) == TrackToolEntry.Entered;
		if ( entered )
			PostMessage( item.Runtime.Coaster != null ? strings.Help( 0x125 ) : TrackToolHelp );
	}

	/// <summary>Moves the selection to the next (<paramref name="direction"/> 1) or previous placed object of the selected one's category, wrapping around.</summary>
	// [BIN:TPW-EXE:0x755150 b_arup/b_ardown] the two round arrows (help 21 and 20) at the top of the right column step through the objects of the window's type
	// [BIN:TPW-EXE:0x004828F0 next object of a type] the window code's walks over the objects of a type (the next one after a given object, 0x004824D0 → 0x004828F0, and the counts 0x00481E40) skip every add-on (AddOn.UpgradesId through 0x004E2710, 0x00482972), and a click on an add-on never opens its own window (Level.WindowTarget): the arrows skip them too
	public void CycleSelectedObject( int direction )
	{
		if ( selectedObject is not { IsDeleted: false } current )
			return;
		var same = level.Objects.Objects.Where( item => !item.IsDeleted && item.Entry.WhichUIType == current.Entry.WhichUIType && !TrackAddOns.IsAddOn( item.Entry ) ).ToList();
		var index = same.IndexOf( current );
		if ( same.Count < 2 || index < 0 )
			return;
		selectedObject = same[((index + Math.Sign( direction )) % same.Count + same.Count) % same.Count];
	}

	// the elements of the object window, found once by BuildObjectWindow
	private UiElement? objQueue, objTrack, objApplyAll, objWindow, objStatsFrame, objPreviewFrame, objPreview, objName, objDoor, objErase, objMove, objOkay, objPrevious, objNext, objList, objRideIt;
	private StatusBanner? objStatus;
	/// <summary>The selected object is condemned (state 4): its window hides the door and the service buttons (<see cref="ApplyObjectStatus"/>).</summary>
	private bool objectCondemned;
	/// <summary>The ride window's door may be used: not for the two track statuses, nor for a closed ride the reopening guard refuses (<see cref="ApplyObjectStatus"/>).</summary>
	private bool objectDoorAllowed = true;
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
		objectCondemned = false;
		objectDoorAllowed = true;
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
			Place( objStatus, layout.Status );
			if ( objStatus != null )
				objStatus.Inner = layout.Status?.Inner;
			Place( objName, layout.Title );
			Place( objDoor, layout.Door );
			Place( objErase, layout.Erase, HelpOf( layout.Erase?.Help ?? -1 ) );
			Place( objMove, layout.Move, HelpOf( layout.Move?.Help ?? -1 ) );
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
			objDoor.Help = HelpOf( selectedObject is { } shown && !DrawnIsOpen( shown ) && layout.Door.AltHelp >= 0 ? layout.Door.AltHelp : layout.Door.Help );
		// the per-frame visibility reset shows every control of the window: hide the ones this table lacks
		if ( layout.RideIt == null && objRideIt != null ) objRideIt.Visible = false;
		if ( layout.Upgrade == null && rideUpgradeButton != null ) rideUpgradeButton.Visible = false;
		// the queue, track and apply-all buttons show only where they can act
		var target = selectedObject!;
		ApplyObjectStatus( target, layout );
		if ( objQueue != null ) objQueue.Visible &= layout.Queue != null && CanEditQueue( target );
		if ( objTrack != null ) objTrack.Visible &= layout.Track != null && CanEditTrack( target );
		if ( objMove != null ) objMove.Enabled = layout.Move != null && CanMoveObject( target );
		if ( objApplyAll != null ) objApplyAll.Visible &= layout.ApplyAll != null && TryPriceTarget( target, out _, out _ );
		for ( var row = layout.Rows.Length; row < ObjectWindowRows; row++ )
		{
			if ( objLabels[row] != null ) objLabels[row]!.Visible = false;
			if ( objValues[row] != null ) objValues[row]!.Visible = false;
			if ( objGauges[row] != null ) objGauges[row]!.Visible = false;
		}
		ApplyObjectSettingLayout( layout );
	}

	/// <summary>
	/// The window's status banner and the controls it governs (<c>0x4ad4e0</c>): the banner shows the object's status (<see cref="RideWindowStatus"/>, none for an
	/// open object with nothing to report); a condemned ride hides its door, queue, track, call, move, ride and upgrade buttons; a ride's door greys for the
	/// two track statuses whatever its open word, and while it is closed and the reopening guard refuses.
	/// </summary>
	// [BIN:TPW-EXE:0x4ad4e0 ride window refresh] status 3 (condemned) hides b_door, b_queue, b_track, b_callmech, b_move, b_ride it! and b_upgrade; status 0x19 or 0x1a disables b_door whatever the open word; a closed ride whose guard 0x4df290 refuses disables it too; the shop and sideshow refreshes (0x4b01d0, 0x4b16c0) show the banner without door logic
	private void ApplyObjectStatus( OriginalObject target, ObjectWindowLayout layout )
	{
		var facts = StatusFactsOf( target );
		var status = facts is { } known ? RideWindowStatus.Of( known ) : null;
		// the shop and sideshow refreshes test no status: only the ride window hides and greys
		var rideWindow = ReferenceEquals( layout.Table, OriginalLayoutTables.RideWindow );
		objectCondemned = rideWindow && facts is { State: RideObjectStates.Condemned };
		if ( objStatus != null )
		{
			objStatus.Status = status is { } shown ? strings[shown] : "";
			objStatus.Colour = status is { } coloured ? StatusColour( coloured ) : UiColors.Text;
			objStatus.Visible &= layout.Status != null && status != null;
		}
		if ( objDoor != null )
			objDoor.Visible &= !objectCondemned;
		// the frame's door enabling (ParkHud.Update) takes this with the object's own CanOpen
		// [BIN:TPW-EXE:0x4973a0 toilet window refresh] the toilet's own window (0x497640) enables its door (0x15bc4) exactly while the reopening guard 0x4df290 allows, open or closed
		var reliefWindow = !rideWindow && LiveState( target ) is { } liveRecord && Status.EconomySource is ParkEconomy { } source && source.Catalog.TryGet( liveRecord.InfoId, out var reliefInfo ) && reliefInfo.ProvidesRelief;
		objectDoorAllowed = reliefWindow ? level.Gate?.MayOpen( target.Runtime ) ?? true
			: !rideWindow || !RideWindowStatus.LocksDoor( status ) && (target.IsOpen || (level.Gate?.MayOpen( target.Runtime ) ?? true));
		if ( objectCondemned )
			foreach ( var element in new[] { objQueue, objTrack, objMove, objRideIt } )
				if ( element != null )
					element.Visible = false;
	}

	/// <summary>What the status banner reads of <paramref name="item"/>; null in a visit or for an object without an economy record.</summary>
	private RideStatusFacts? StatusFactsOf( OriginalObject item )
	{
		// [EXT:online-coop-commands] a guest's window reads the record as its waiting open-state action will leave it (C4b-2)
		if ( Status.EconomySource is not ParkEconomy economy || DrawnRecord( item ) is not { } record )
			return null;
		var relief = economy.Catalog.TryGet( record.InfoId, out var info ) && info.ProvidesRelief;
		var state = ParkEconomy.ObjectState( record );
		var guests = level.Guests;
		// [BIN:TPW-EXE:0x00485F60 exit status] the exit test 0x4DE5A0 (ParkGate.ExitConnected) runs for a ride (type +0x4AC 0) that is no add-on (upgrade parent 0, +0x288 not 2) once its queue is connected, and a failing exit shows status 0x18, EXIT NOT CONNECTED
		return new RideStatusFacts(
			state,
			TrackIncomplete: item.Runtime.TrackIncomplete,
			TrackInvalid: item.Runtime.TrackInvalid,
			ServiceCalled: record.MaintenanceRequested,
			Relief: relief,
			// an upgrade always waits for the mechanics; a toilet's call is the handymen's
			Service: ServiceProgress( economy, record, relief && state != RideObjectStates.Upgrading ? StaffType.Handyman : StaffType.Mechanic ),
			QueueDetached: item.Entry.HasQueue && guests != null && !ParkGate.QueueConnected( guests.Grid, item.Visitors ),
			ExitDetached: item.Entry.WhichUIType == 0 && !TrackAddOns.IsAddOn( item.Entry ) && guests != null && !ParkGate.ExitConnected( guests.Grid, item.Visitors ),
			IsOpen: DrawnIsOpen( item ) );
	}

	/// <summary>How far the service of <paramref name="record"/> (an upgrade or a call) has come with the employees of <paramref name="role"/>.</summary>
	// [BIN:TPW-EXE:0x485f60 service progress] none hired (the world's role count), on strike (0x509990), claimed (+0x5e) and on the way, or the claimant at work on this object (0x4e02f0 → 0x5065d0), else waiting
	private static RideServiceProgress ServiceProgress( ParkEconomy economy, ParkObjectState record, StaffType role )
	{
		if ( !economy.Staff.OfType( role ).Any() )
			return RideServiceProgress.NoneHired;
		if ( economy.Staff.Strikes.IsActive( role ) )
			return RideServiceProgress.OnStrike;
		if ( record.MechanicId == 0 )
			return RideServiceProgress.Waiting;
		var claimant = economy.Staff.Members.FirstOrDefault( member => member.Id == record.MechanicId );
		return claimant != null && WorksOn( economy, claimant, record ) ? RideServiceProgress.AtWork : RideServiceProgress.OnTheWay;
	}

	/// <summary>Whether <paramref name="member"/>, the object's claimant, works on <paramref name="record"/> now.</summary>
	// [BIN:TPW-EXE:0x5065d0 at work] a mechanic whose target +0x218 is the object in state 13 (repairing or upgrading), a handyman whose toilet +0x21a is the object in state 11 (cleaning); the headless economy, which drives no member through these states, counts the job's countdown on the object once the member arrived
	private static bool WorksOn( ParkEconomy economy, StaffMember member, ParkObjectState record ) => economy.DrivenByWorld( member )
		? member.Type == StaffType.Handyman ? member.Job == StaffJob.Cleaning && member.ToiletId == record.Id : member.Job == StaffJob.Repairing && member.RideId == record.Id
		: member is { BusyTicks: > 0, AwaitingJobArrival: false } && member.AssignedInstanceId == record.Id;

	/// <summary>The status banner's colour (table <c>0x74fb50</c>).</summary>
	// [BIN:TPW-EXE:0x74fb50 status colours] CLOSED grey (128,128,128), broken down and condemned red (255,50,30), the upgrade, cleaning and repair statuses blue (80,100,255), the queue, exit and track statuses orange (255,150,30), each with alpha 255 (0x4861e0)
	internal static RgbaByte StatusColour( UIStrings status ) => status switch
	{
		UIStrings.Closed => new RgbaByte( 128, 128, 128, 255 ),
		UIStrings.BrokenDown or UIStrings.Condemned => new RgbaByte( 255, 50, 30, 255 ),
		>= UIStrings.WaitingToBeUpgraded and <= UIStrings.RepairsInProgress => new RgbaByte( 80, 100, 255, 255 ),
		_ => new RgbaByte( 255, 150, 30, 255 ),
	};

	/// <summary>
	/// The object window's status banner (the "chev" control): the status in font slot 8, centred and wrapped in the control's inner rectangle, in the status
	/// colour; nothing while there is no status.
	/// </summary>
	// [BIN:TPW-EXE:0x4ad720 status banner] the window's init makes the chev control a centred text in font slot 8, hidden; 0x48d160 hides it for status 0 and otherwise shows it with the status text and colour
	// [BIN:TPW-EXE:0x65c5d5 text colour] the colour setter sends message 0x40000 to the control's model (+0x98) and text (+0x9c), and only the text class takes it (0x48fd07 stores it at +0x48): the chev art (f_chev, chev.tga) keeps its own gold-striped frame and black centre under the coloured text
	internal sealed class StatusBanner : UiElement
	{
		public string Status { get; set; } = "";
		public RgbaByte Colour { get; set; } = UiColors.Text;
		/// <summary>The control's inner (text) rectangle, canvas units.</summary>
		public UiRect? Inner { get; set; }

		public override void Draw( UiContext context, bool focused, bool pressed )
		{
			if ( Status.Length == 0 )
				return;
			var rect = ScreenRect( context.Canvas );
			context.DrawModel( "chev", 0, rect );
			// [BIN:TPW-EXE:0x6aec80 text wrap] a status wider than the control wraps into it (the text library's formatter), the block centred
			context.DrawText( context.Fonts.Label, Status, Inner is { } inner ? context.Canvas.Map( inner, Anchor ) : rect, Colour, UiAlign.Center, wrap: true );
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
			UiGauge.Draw( context.Batch, UiGauge.BarRect( rect, context.Canvas ), stat.Gauge, stat.IsHappiness ? UiGauge.HappinessArtPath : UiGauge.ArtPath );
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

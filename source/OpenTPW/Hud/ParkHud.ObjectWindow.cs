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
	// [APPROX:UI-024] the sliders (speed, capacity, duration, price), b_queue, b_move, b_track and b_applyall are not drawn (their features are not simulated); the Age row has no value; the preview is OpenTPW's orthographic icon
	private sealed class ObjectWindowLayout
	{
		private static readonly Dictionary<LayoutTable, ObjectWindowLayout> cache = new();

		public required LayoutTable Table { get; init; }
		public required LayoutNode Stats { get; init; }
		public required LayoutNode Preview { get; init; }
		public required LayoutNode Title { get; init; }
		public required (LayoutNode Label, LayoutNode Value)[] Rows { get; init; }

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
			return cache[table] = new ObjectWindowLayout { Table = table, Stats = stats, Preview = preview, Title = title, Rows = labels.Zip( values ).ToArray() };
		}
	}

	private const int ObjectWindowRows = 8;
	// [BIN:TPW-EXE:0x755150 ride window rows] the seven rows are the UITEXT 17 to 23 labels in order: users last month, age, excitement, reliability, state of repair, remaining life, scrap value
	private static readonly UIStrings[] ObjectRowLabels = { UIStrings.UsersLastMonth, UIStrings.Age, UIStrings.Excitement, UIStrings.Reliability, UIStrings.StateOfRepair, UIStrings.RemainingLife, UIStrings.ScrapValue };

	/// <summary>The stat of the window row <paramref name="row"/> (the original's order), null when the row has none.</summary>
	private ObjectStat? StatOfRow( int row )
	{
		if ( InfoNow is not { } info || row >= ObjectRowLabels.Length )
			return null;
		var label = ObjectRowLabels[row];
		foreach ( var stat in info.Stats )
		{
			if ( stat.Label == label || row == 0 && stat.Label == UIStrings.CustomersLastMonth )
				return stat;
		}
		return null;
	}

	private ObjectWindowLayout? ObjectLayout => selectedObject is { IsDeleted: false } item ? ObjectWindowLayout.For( item.Entry.WhichUIType ) : null;

	private void BuildObjectWindow()
	{
		T Info<T>( T element ) where T : UiElement
		{
			element.Anchor = UiAnchor.Center;
			element.Visible = false;
			infoElements.Add( hud.Add( element ) );
			return element;
		}

		Info( new UiModelImage { Id = "infoStatsFrame", Model = "!frame" } );
		Info( new UiModelImage { Id = "infoPreviewFrame", Model = "!frame" } );
		Info( new ObjectPreview( this ) { Id = "infoPreview" } );
		Info( new UiLabel
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
			Info( new UiLabel { Id = $"statLabel{index}", Text = () => StatOfRow( index ) is { } stat ? strings[stat.Label] : "", Font = fonts => fonts.Small } );
			// [DATA:video 77:09, 24:20] the values are white in the object windows ("23 dagen", "$ 10000"), not green
			Info( new UiLabel
			{
				Id = $"statValue{index}",
				Text = () => StatOfRow( index ) is { } stat ? stat.Value ?? (stat.IsGauge ? "" : strings.Extra( OpenTpwText.NotSimulated )) : "",
				Font = fonts => fonts.Small,
				Color = UiColors.Text
			} );
			Info( new ObjectGauge( this, index ) { Id = $"statGauge{index}" } );
		}
		Info( new UiButton { Id = "openRide", Model = "b_door", Clicked = ToggleRideOpen,
			// b_door state frames: open door normally, closed door in the "down" frames.
			Selected = () => selectedObject?.IsOpen == false } );
		Info( new UiButton { Id = "deleteRide", Model = "b_erase", Clicked = DeleteRide } );
		Info( new UiButton { Id = "retractInfo", Model = "b_okay", Clicked = () => SetInfoArm( false ) } );
		Info( new UiButton { Id = "infoPrevious", Model = "b_arup", Clicked = () => CycleSelectedObject( -1 ) } );
		Info( new UiButton { Id = "infoNext", Model = "b_ardown", Clicked = () => CycleSelectedObject( 1 ) } );
		Info( new UiButton { Id = "infoList", Model = "b_allthings", Clicked = () => { SetInfoArm( false ); OpenAttractionsWindow(); } } );
		// the original's "Ride it!" button (gui_CameraFlags 4); owners ride as well as visitors of a published park
		Info( new UiButton { Id = "rideView", Model = "b_ride it!", Clicked = () => { if ( IsRiding ) StopRide(); else StartRide( selectedObject ); } } );
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

	/// <summary>Places each control of the object window at its table rectangle for the selected object; a control the table lacks is hidden.</summary>
	private void ApplyObjectLayout()
	{
		if ( ObjectLayout is not { } layout )
			return;
		void Place( string id, LayoutNode? node, Func<LayoutNode, int>? help = null )
		{
			if ( hud.Find( id ) is not { } element )
				return;
			if ( node == null )
			{
				element.Visible = false;
				return;
			}
			element.Bounds = node.Rect;
			element.Anchor = UiAnchor.Center;
			if ( element is UiButton )
			{
				var helpId = help?.Invoke( node ) ?? node.Help;
				element.Help = helpId >= 0 ? strings.Help( helpId ) : null;
			}
		}
		var table = layout.Table;
		Place( "infoArm", table.Root );
		Place( "infoStatsFrame", layout.Stats );
		Place( "infoPreviewFrame", layout.Preview );
		Place( "infoPreview", layout.Preview );
		Place( "infoName", layout.Title );
		for ( var row = 0; row < ObjectWindowRows; row++ )
		{
			var present = row < layout.Rows.Length;
			Place( $"statLabel{row}", present ? layout.Rows[row].Label : null );
			Place( $"statValue{row}", present ? layout.Rows[row].Value : null );
			Place( $"statGauge{row}", present ? layout.Rows[row].Value : null );
		}
		// b_door: the table's second help id (12, open) is the prompt while the door is down, that is closed; its first (13, close) while it is open
		Place( "openRide", table.TryModel( "b_door" ), node => selectedObject?.IsOpen == false && node.AltHelp >= 0 ? node.AltHelp : node.Help );
		Place( "deleteRide", table.TryModel( "b_erase" ) );
		Place( "retractInfo", table.TryModel( "b_okay" ) );
		Place( "infoPrevious", table.TryModel( "b_arup" ) );
		Place( "infoNext", table.TryModel( "b_ardown" ) );
		Place( "infoList", table.TryModel( "b_allthings" ) );
		Place( "rideView", table.TryModel( "b_ride it!" ) );
		// a toilet's call button (shops have none of their own) and the host buttons use the slots the tables give the neighbouring controls
		Place( "callStaff", table.TryModel( "b_callmech" ) ?? OriginalLayoutTables.RideWindow.Model( "b_callmech" ) );
		Place( "rideUpgrade", table.TryModel( "b_upgrade" ) );
		if ( hud.Find( "shopPrice" ) is { } price )
		{
			// [EXT:shop-price-editor] the price editor takes the place of the table's price slider (shop 0xc086, sideshow 0xa090)
			var slot = table.All.FirstOrDefault( node => node.Type is 3 or 12 && node.Rect.X == 753 );
			price.Bounds = slot != null ? UiRect.FromCenter( slot.Rect.Center, 340, 150 ) : price.Bounds;
			price.Anchor = UiAnchor.Center;
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
